#!/usr/bin/env bash
# shellcheck disable=SC2034  # variables are read by the functions in lib.sh
# Hyrule Online - guided SteamOS installer (play, host and join). See README.md.
# Usage: install.sh [--dry-run] [--yes --dump PATH] [--mod PATH] [--dir PATH] [--link] [--uninstall]
#   --dry-run    print what would be done, change nothing
#   --yes        no questions (needs --dump); never closes Steam, never deletes the game copy
#   --dump PATH  folder with your dumped game, update, DLC and save (it is searched, not modified)
#   --mod PATH   folder with the Hyrule Online mod pack from the Windows install (default: the dump folder)
#   --dir PATH   install folder (default ~/hyrule-online)
#   --link       link the game, update and DLC instead of copying them (the dump must stay available)
#   --uninstall  remove the Steam entry, the menu entry and the install folder
set -u
SELF_DIR=$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)
. "$SELF_DIR/lib.sh"

# Pinned upstream components. The Cemu, graphic pack and Milk Bar entries are the same as scripts/components.json
# (test.sh checks that). Nothing here is bundled: everything is downloaded and verified before it is used.
CEMU_URL=https://cemu.info/releases/cemu_1.26.2.zip
CEMU_SHA=B0E3ABF5048F78E352B42C3E1660A2C6E85D6905CD9F60D06CA2F2318FA3152C
CEMU_EXE_SHA=01074183E5CBAF553D6636FF7FB0959D4808F7ECBDEC667E6B6B031E289E0801
GP_URL=https://github.com/cemu-project/cemu_graphic_packs/releases/download/Github989/graphicPacks989.zip
GP_SHA=72B4708A476A810990A3EE1026EFC2A7B1C133D4E467EB8D8EBC23C436A03CBB
GP_MARKER_SHA=3E2770CD45E5507A186B8C5F1D97750255E137387DCFEA5D62870874D3C5D14D
MILKBAR_URL=https://github.com/MilkBarModding/MilkBarLauncher/releases/download/2.0.1/MilkBarLauncher.zip
MILKBAR_SHA=8ED0B624489D601A54CE6CF16D6C1A0F43BAC6A608D5E30CFB783D944A30F9B9
INJECT_SHA=FF78ACAAA2B221F0DCCEA0E76EA390A7CEE142525A4E885D1C26D7EB604EF7B1
# The headless Linux client; pinned to the release the working setup used (the "latest" link can change under us).
CLI_URL=https://github.com/CarlosEvCode/MilkBarLauncher/releases/download/v1.0.0-cli/MilkBar.CLI-win-x64.zip
CLI_SHA=0E176C2A99FD048CFA28B7574332399798937B3D8DF253A510AD0BD2DE9B692D
DOTNET_URL=https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.31/windowsdesktop-runtime-8.0.31-win-x64.exe
DOTNET_SHA=C375DFD80A967405CFEFF634912C1FCCC56261CE3D9209EA473F60A21184D1CC
VC_URL=https://download.visualstudio.microsoft.com/download/pr/990c720a-46ce-4dc7-87b9-c2eb615d34b9/CC0FF0EB1DC3F5188AE6300FAEF32BF5BEEBA4BDD6E8E445A9184072096B713B/VC_redist.x64.exe
VC_SHA=CC0FF0EB1DC3F5188AE6300FAEF32BF5BEEBA4BDD6E8E445A9184072096B713B
GAME_ID=00050000101C9400
UPDATE_ID=0005000E101C9400
UPDATE_VER=208
DLC_ID=0005000C101C9400
DLC_VER=80
# The CLI waits 750 ms after starting Cemu (bytes 20 EE 02 00 00). Cemu needs about a minute under Wine to
# reach the connect step, so the working setup raises it to 70 s.
CLI_WAIT_MS=70000

DRY=0 YES=0 LINK=0 UNINSTALL=0 DUMP='' MOD='' DIR=''
NEED_GB=25
STATE=${HYRULE_STATE:-${XDG_CONFIG_HOME:-$HOME/.config}/hyrule-online/state}
STEAM_ROOT=${HYRULE_STEAM_ROOT:-$HOME/.steam/steam}
[ -d "$STEAM_ROOT" ] || STEAM_ROOT=$HOME/.local/share/Steam
APP_NAME="Hyrule Online"
DESKTOP_FILE=$HOME/.local/share/applications/hyrule-online.desktop
STEP_N=0 STEP_TOTAL=10 DEFERRED=

usage() { sed -n '2,10p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

die() { progress_stop; msg_box error "$*"; exit 1; }

# ---- state file: one "key=value" per line, so a re-run can skip finished steps ----
state_get() { [ -f "$STATE" ] && sed -n "s/^$1=//p" "$STATE" | tail -n1; }
state_set() {
  [ "$DRY" = 1 ] && return 0
  mkdir -p "$(dirname "$STATE")" || return 1
  { grep -v "^$1=" "$STATE" 2>/dev/null; printf '%s=%s\n' "$1" "$2"; } > "$STATE.tmp" && mv "$STATE.tmp" "$STATE"
}
state_done() { [ "$(state_get "done_$1")" = 1 ]; }
state_mark() { state_set "done_$1" 1; }

# ---- small helpers ----
sha_ok() { local got; got=$(sha256sum "$1" 2>/dev/null | cut -d' ' -f1); [ -n "$got" ] && [ "${got,,}" = "${2,,}" ]; }

free_gb() { local d=$1; while [ ! -d "$d" ]; do d=$(dirname "$d"); done; df -Pk "$d" | awk 'NR==2 {print int($4/1048576)}'; }

valid_dir() { [[ $1 =~ ^/[A-Za-z0-9._/-]+$ ]] && [[ $1 != *..* ]] && [ "$1" != "$HOME" ]; }

# fetch <url> <sha256> <file name>: resumable, verified, a bad download is deleted (fail closed).
fetch() {
  local f="$DIR/downloads/$3"
  [ -f "$f" ] && sha_ok "$f" "$2" && return 0
  rm -f "$f"
  [ -f "$f.part" ] && sha_ok "$f.part" "$2" || curl -fL --retry 3 --retry-delay 3 -C - --progress-bar -o "$f.part" "$1" \
    || { echo "Download failed: $1" >&2; return 1; }
  sha_ok "$f.part" "$2" || { rm -f "$f.part"; echo "Checksum mismatch for $3: the download was deleted and nothing was installed from it." >&2; return 1; }
  mv "$f.part" "$f"
}

# extract <zip> <dest> [member]
extract() {
  mkdir -p "$2" || return 1
  if command -v unzip >/dev/null 2>&1; then unzip -q -o "$1" ${3:+"$3"} -d "$2"
  else 7z x -y -bd -o"$2" "$1" ${3:+"$3"} >/dev/null; fi
}

proton_ok_name() { [[ $1 =~ ^Proton\ ([0-9]+)\.[0-9]+$ ]] && [ "${BASH_REMATCH[1]}" -ge 10 ]; }

# Prints the oldest Proton folder that is 10.0 or newer (10.0 is the version the setup was proven on).
find_proton() {
  local lib d n
  { printf '%s\n' "$STEAM_ROOT"
    grep -ho '"path"[[:space:]]*"[^"]*"' "$STEAM_ROOT/steamapps/libraryfolders.vdf" 2>/dev/null | sed 's/.*"\([^"]*\)"$/\1/'
  } | while IFS= read -r lib; do
    for d in "$lib/steamapps/common/Proton "*; do
      n=${d##*/}
      if proton_ok_name "$n" && [ -x "$d/files/bin/wine64" ]; then printf '%s\t%s\n' "${n#Proton }" "$d"; fi
    done
  done | sort -V | head -n1 | cut -f2
}

# ---- dump scanning ----
meta_field() { sed -n "s/.*<$2[^>]*>\([^<]*\)<\/$2>.*/\1/p" "$1" | head -n1; }

# scan_dump <folder>: sets SCAN_BASE, SCAN_UPDATE, SCAN_DLC, SCAN_SAVE (folders or empty) and SCAN_NOTE.
scan_dump() {
  SCAN_BASE='' SCAN_UPDATE='' SCAN_DLC='' SCAN_SAVE='' SCAN_NOTE=''
  local meta dir id ver d
  while IFS= read -r meta; do
    dir=${meta%/meta/meta.xml}
    id=$(meta_field "$meta" title_id | tr a-f A-F)
    ver=$((10#$(meta_field "$meta" title_version | tr -dc 0-9)0)); ver=$((ver / 10))
    if [ "$id" = "$UPDATE_ID" ] || { [ "$id" = "$GAME_ID" ] && { [[ $dir == */0005000[eE]/* ]] || [ "$ver" -gt 0 ]; }; }; then
      if [ "$ver" != "$UPDATE_VER" ]; then SCAN_NOTE+="The update at $dir is v$ver, v$UPDATE_VER is needed. "
      elif [ -z "$SCAN_UPDATE" ] && [ -d "$dir/content" ]; then SCAN_UPDATE=$dir; fi
    elif [ "$id" = "$GAME_ID" ]; then
      [ -z "$SCAN_BASE" ] && [ -f "$dir/code/U-King.rpx" ] && [ -d "$dir/content" ] && SCAN_BASE=$dir
    elif [ "$id" = "$DLC_ID" ]; then
      if [ "$ver" != "$DLC_VER" ]; then SCAN_NOTE+="The DLC at $dir is v$ver, v$DLC_VER is needed. "
      elif [ -z "$SCAN_DLC" ] && [ -d "$dir/content/0010" ]; then SCAN_DLC=$dir; fi
    fi
  done < <(find -L "$1" -maxdepth 9 \( -iname graphicPacks -o -iname shaderCache \) -prune -o -type f -path '*/meta/meta.xml' -print 2>/dev/null | sort)
  # A save is a folder named save or 101c9400 that holds user/80000001 (a Cemu mlc01 save or a plain save dump).
  while IFS= read -r d; do
    if [ -d "$d/user/80000001" ] && [[ $d != */boss/* ]]; then SCAN_SAVE=$d; break; fi
  done < <(find -L "$1" -maxdepth 9 -type d \( -iname save -o -iname 101c9400 \) 2>/dev/null | sort)
  return 0
}

# find_mod <folder>: prints the graphicPacks folder that holds the complete Hyrule Online mod pack.
find_mod() {
  local r gp
  while IFS= read -r r; do
    gp=${r%/BreathOfTheWild_BCML/rules.txt}
    if [ -f "$gp/bcmlPatches/MilkBarLauncher/rules.txt" ] && [ -f "$gp/BreathOfTheWild_BCML/content/Actor/Pack/Jugador32.sbactorpack" ]; then
      printf '%s\n' "$gp"; return 0
    fi
  done < <(find -L "$1" -maxdepth 9 -type f -path '*/BreathOfTheWild_BCML/rules.txt' 2>/dev/null | sort)
  return 1
}

# copy_title <src> <dest>: only code, content and meta; a finished copy is never redone.
copy_title() {
  local s
  if [ -e "$2" ] || [ -L "$2" ]; then return 0; fi
  if [ "$LINK" = 1 ]; then ln -s "$1" "$2"; return; fi
  rm -rf "$2.partial"; mkdir "$2.partial" || return 1
  for s in code content meta; do
    if [ -d "$1/$s" ]; then cp -a "$1/$s" "$2.partial/" || return 1; fi
  done
  mv "$2.partial" "$2"
}

# The CLI's start delay, patched from the untouched .orig copy so it can be applied again safely.
patch_cli() {
  local dll=$DIR/milkbar/MilkBar.CLI.dll
  [ -f "$dll.orig" ] || cp "$dll" "$dll.orig" || return 1
  python3 - "$dll" "$CLI_WAIT_MS" <<'PY'
import sys
dll, ms = sys.argv[1], int(sys.argv[2])
data = bytearray(open(dll + ".orig", "rb").read())
pattern = bytes([0x20, 0xEE, 0x02, 0x00, 0x00])
if data.count(pattern) != 1:
    sys.exit("MilkBar.CLI.dll is not the expected build; the start delay was not changed")
i = data.index(pattern)
data[i:i + 5] = bytes([0x20]) + ms.to_bytes(4, "little")
open(dll, "wb").write(data)
PY
}

# The in-game mod reads four data files from %APPDATA%\BOTWM (without them Cemu dies with "IsObject()" on join).
# Milk Bar embeds them in Milk Bar Launcher.dll as length-prefixed blobs; each one is found by size and checked by SHA-256.
install_appdata() {
  local zip=$DIR/downloads/MilkBarLauncher.zip user=""
  local d
  for d in "$DIR/prefix/drive_c/users"/*; do
    [ -d "$d" ] && [ "${d##*/}" != Public ] && { user=${d##*/}; break; }
  done
  [ -n "$user" ] || { echo "The Wine prefix has no user folder." >&2; return 1; }
  python3 - "$zip" "$DIR/prefix/drive_c/users/$user/AppData/Roaming/BOTWM" <<'PY'
import hashlib, os, struct, sys, zipfile
zpath, dest = sys.argv[1:3]
files = [
    ("ArmorMapping.txt", 2618, "1C628CFCE01BF978BA52D371BFCDB02D416BC37038FBEDD8FC47C888AE4484F7"),
    ("QuestFlags.txt", 174333, "F100458C8FEBAF9AD9FAAE9F49E78C33D85E1A144CFC777DE69D78993318C102"),
    ("QuestFlagsNames.txt", 174234, "33B16D29FC343A87EC4363BF6759519FA8E3DBC1C5695EC12AC0FD1AFADFA500"),
    ("WeaponDamages.txt", 3845, "3A526EEB8A98CE51D6FF8759DC8AAE7CA3333F5BDBEC1546663916F67C38F738"),
]
blob = zipfile.ZipFile(zpath).read("Milk Bar Launcher.dll")
os.makedirs(dest, exist_ok=True)
for name, size, sha in files:
    prefix, i, found = struct.pack("<I", size), -1, None
    while found is None:
        i = blob.find(prefix, i + 1)
        if i < 0:
            sys.exit("Milk Bar Launcher.dll does not contain " + name)
        if hashlib.sha256(blob[i + 4:i + 4 + size]).hexdigest().upper() == sha:
            found = blob[i + 4:i + 4 + size]
    with open(os.path.join(dest, name + ".tmp"), "wb") as f:
        f.write(found)
    os.replace(os.path.join(dest, name + ".tmp"), os.path.join(dest, name))
PY
}

# ---- steps ----
say_step() { STEP_N=$((STEP_N + 1)); progress "[$STEP_N/$STEP_TOTAL] $1"; }

# run_step <id> <title> <function>: a function returns 2 to say "not done yet, carry on".
run_step() {
  local rc
  say_step "$2"
  if state_done "$1"; then progress "    already done, skipping"; return 0; fi
  "$3"; rc=$?
  if [ "$rc" = 2 ]; then DEFERRED+="$2; "; return 0; fi
  [ "$rc" = 0 ] || die "This step failed: $2
Fix the problem and run install.sh again. Finished steps are skipped."
  state_mark "$1"
}

PF_ERR=
preflight() {
  local missing='' t
  PF_ERR=
  if [ "$(id -u)" -eq 0 ]; then PF_ERR="Run the installer as your normal user, not as root."; return 1; fi
  if [ "$(uname -s)" != Linux ] || [ "$(uname -m)" != x86_64 ]; then PF_ERR="This installer needs Linux on x86_64 (SteamOS on a Steam Deck or ROG Ally)."; return 1; fi
  grep -qi steamos /etc/os-release 2>/dev/null || echo "Note: this does not look like SteamOS. The setup was only tried on SteamOS."
  for t in curl python3 sha256sum timeout systemd-run; do command -v "$t" >/dev/null 2>&1 || missing+="$t "; done
  command -v unzip >/dev/null 2>&1 || command -v 7z >/dev/null 2>&1 || missing+="unzip-or-7z "
  if [ -n "$missing" ]; then PF_ERR="Missing tools: $missing
They are part of a normal SteamOS install. Install them with your package manager, then run this again."; return 1; fi
  PROTON=$(find_proton)
  if [ -z "$PROTON" ]; then
    PF_ERR="Proton 10.0 (or newer) is not installed.
In Desktop Mode open Steam, go to Library, make the filter drop-down include Tools (or search for 'Proton 10.0'), select 'Proton 10.0' and press Install. Then run this installer again."
    return 1
  fi
  echo "    Proton: $PROTON"
}

step_dir() {
  local stored; stored=$(state_get install_dir)
  if [ -n "$DIR" ] && [ -n "$stored" ] && [ "${DIR%/}" != "$stored" ]; then die "Already set up in $stored. Run install.sh --uninstall first to use another folder."; fi
  [ -n "$DIR" ] || DIR=$stored
  if [ -z "$DIR" ]; then
    DIR=$HOME/hyrule-online
    if [ "$YES" != 1 ] && [ "$DRY" != 1 ]; then
      DIR=$(ask_text "$APP_NAME setup" "Install folder (no spaces; needs about 15 GB for the game copy)" "$DIR") || die "Cancelled."
    fi
  fi
  DIR=${DIR%/}
  valid_dir "$DIR" || die "The install folder must be an absolute path using only letters, numbers, . _ - and /: $DIR"
  if [ -e "$DIR" ] && [ ! -f "$DIR/.hyrule-online" ] && [ -n "$(ls -A "$DIR" 2>/dev/null)" ]; then
    die "$DIR already exists and is not a Hyrule Online install. Choose another folder."
  fi
  if ! state_done dump && [ "$(free_gb "$DIR")" -lt "$NEED_GB" ]; then
    if [ "$DRY" = 1 ]; then
      echo "    WARNING: about $NEED_GB GB are needed, only $(free_gb "$DIR") GB are free; a real run would stop here."
    else
      die "Not enough free space for $DIR: about $NEED_GB GB are needed, $(free_gb "$DIR") GB are free."
    fi
  fi
  echo "    Install folder: $DIR"
  if [ "$DRY" = 1 ]; then echo "    [dry-run] would create $DIR"; return 0; fi
  mkdir -p "$DIR/downloads" || die "Cannot write to $DIR."
  : > "$DIR/.hyrule-online" || die "Cannot write to $DIR."
  state_set install_dir "$DIR"
}

step_download() {
  local f
  if [ "$DRY" = 1 ]; then
    echo "    [dry-run] would download and verify (SHA-256): Cemu 1.26.2f (25 MB), graphic packs v989 (3 MB), Milk Bar Launcher 2.0.1 (65 MB, only InjectDLL.dll and the dedicated server are used),"
    echo "    [dry-run] the Milk Bar CLI (0.3 MB), .NET 8 Desktop Runtime 8.0.31 (59 MB) and the Visual C++ runtime (26 MB)"
    return 0
  fi
  for f in "$CEMU_URL $CEMU_SHA cemu_1.26.2.zip" "$GP_URL $GP_SHA graphicPacks989.zip" "$MILKBAR_URL $MILKBAR_SHA MilkBarLauncher.zip" \
           "$CLI_URL $CLI_SHA MilkBar.CLI-win-x64.zip" "$DOTNET_URL $DOTNET_SHA windowsdesktop-runtime-8.0.31-win-x64.exe" "$VC_URL $VC_SHA VC_redist.x64.exe"; do
    # shellcheck disable=SC2086
    set -- $f
    progress "    $3"
    fetch "$1" "$2" "$3" || return 1
  done
}

step_extract() {
  local dl=$DIR/downloads tmp=$DIR/.extract gp=$DIR/cemu/graphicPacks/downloadedGraphicPacks
  if [ "$DRY" = 1 ]; then echo "    [dry-run] would unpack Cemu, the graphic packs, InjectDLL.dll and the Milk Bar CLI, then patch the CLI's start delay"; return 0; fi
  rm -rf "$tmp"; mkdir -p "$tmp" "$DIR/cemu" "$DIR/milkbar" || return 1
  if ! sha_ok "$DIR/cemu/Cemu.exe" "$CEMU_EXE_SHA"; then
    extract "$dl/cemu_1.26.2.zip" "$tmp/cemu" || return 1
    sha_ok "$tmp/cemu/cemu_1.26.2/Cemu.exe" "$CEMU_EXE_SHA" || { echo "Cemu.exe is not the expected build." >&2; return 1; }
    cp -a "$tmp/cemu/cemu_1.26.2/." "$DIR/cemu/" || return 1
  fi
  if [ ! -f "$gp/BreathOfTheWild/Mods/ExtendedMemory/rules.txt" ]; then
    extract "$dl/graphicPacks989.zip" "$tmp/gp" || return 1
    sha_ok "$tmp/gp/BreathOfTheWild/Mods/ExtendedMemory/rules.txt" "$GP_MARKER_SHA" || { echo "The graphic packs are not the expected build." >&2; return 1; }
    mkdir -p "$gp" && cp -a "$tmp/gp/." "$gp/" || return 1
  fi
  if ! sha_ok "$DIR/milkbar/InjectDLL.dll" "$INJECT_SHA"; then
    extract "$dl/MilkBarLauncher.zip" "$tmp/mb" Resources/InjectDLL.dll || return 1
    sha_ok "$tmp/mb/Resources/InjectDLL.dll" "$INJECT_SHA" || { echo "InjectDLL.dll is not the expected build." >&2; return 1; }
    cp "$tmp/mb/Resources/InjectDLL.dll" "$DIR/milkbar/" || return 1
  fi
  if [ ! -f "$DIR/milkbar/MilkBar.CLI.exe" ]; then
    rm -f "$DIR/milkbar/MilkBar.CLI.dll.orig"
    extract "$dl/MilkBar.CLI-win-x64.zip" "$tmp/cli" || return 1
    cp -a "$tmp/cli/." "$DIR/milkbar/" || return 1
  fi
  patch_cli || return 1
  rm -rf "$tmp"
}

# The dedicated server comes from the same verified zip as InjectDLL.dll; the zip is only fetched again if an earlier run deleted it.
step_server() {
  local tmp=$DIR/.extract
  if [ "$DRY" = 1 ]; then echo "    [dry-run] would unpack the dedicated server from MilkBarLauncher.zip into $DIR/server"; return 0; fi
  [ -f "$DIR/server/MBL.DedicatedServer.exe" ] && return 0
  mkdir -p "$DIR/downloads" && fetch "$MILKBAR_URL" "$MILKBAR_SHA" MilkBarLauncher.zip || return 1
  rm -rf "$tmp/srv" "$DIR/server"
  extract "$DIR/downloads/MilkBarLauncher.zip" "$tmp/srv" 'DedicatedServer/*' || return 1
  [ -f "$tmp/srv/DedicatedServer/MBL.DedicatedServer.exe" ] || { echo "The dedicated server is missing from MilkBarLauncher.zip." >&2; return 1; }
  mv "$tmp/srv/DedicatedServer" "$DIR/server" && rm -rf "$tmp"
}

# shellcheck source=/dev/null
wine_run() { ( . "$DIR/env.sh" && export WINEDEBUG=-all && "$@" ); }

step_prefix() {
  local rc
  if [ "$DRY" = 1 ]; then echo "    [dry-run] would create the Wine prefix $DIR/prefix with $PROTON and install .NET 8 and the Visual C++ runtime in it"; return 0; fi
  {
    echo '#!/usr/bin/env bash'
    echo '# Wine environment for Hyrule Online (Proton'"'"'s Wine, its own prefix). Written by install.sh.'
    printf 'export PROTON_DIR=%q\nexport WINEPREFIX=%q\n' "$PROTON/files" "$DIR/prefix"
    cat <<'EOF'
export WINEARCH=win64
export WINEDEBUG="${WINEDEBUG:--all}"
export WINEDLLOVERRIDES="${WINEDLLOVERRIDES:-mshtml=}"
export PATH="$PROTON_DIR/bin:$PATH"
export LD_LIBRARY_PATH="$PROTON_DIR/lib/x86_64-linux-gnu:$PROTON_DIR/lib:${LD_LIBRARY_PATH:-}"
export WINEDLLPATH="$PROTON_DIR/lib/wine:$PROTON_DIR/lib/vkd3d"
export DISPLAY="${DISPLAY:-:0}"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_EnableWriteXorExecute=0
EOF
  } > "$DIR/env.sh" || return 1
  if ! state_done prefix_boot; then
    progress "    creating the Wine prefix"
    wine_run timeout 600 wine64 wineboot -u || return 1
    wine_run wineserver -w; state_mark prefix_boot
  fi
  if ! state_done prefix_dotnet; then
    progress "    installing .NET 8 Desktop Runtime (a few minutes)"
    wine_run timeout 1200 wine64 "$DIR/downloads/windowsdesktop-runtime-8.0.31-win-x64.exe" /install /quiet /norestart
    wine_run wineserver -w
    [ -d "$DIR/prefix/drive_c/Program Files/dotnet/shared/Microsoft.WindowsDesktop.App/8.0.31" ] || { echo ".NET 8 did not install in the Wine prefix." >&2; return 1; }
    state_mark prefix_dotnet
  fi
  if ! state_done prefix_vc; then
    progress "    installing the Visual C++ runtime"
    wine_run timeout 600 wine64 "$DIR/downloads/VC_redist.x64.exe" /install /quiet /norestart; rc=$?
    wine_run wineserver -w
    case $rc in 0|3010|1638) ;; *) echo "Warning: the Visual C++ installer returned $rc. Continuing; if Cemu does not start later, run install.sh again." >&2 ;; esac
    state_mark prefix_vc
  fi
}

# Asks for (or takes) the dump and mod folders and checks both. Sets SCAN_* and MODSRC.
pick_sources() {
  local src=$DUMP gp
  while :; do
    [ -n "$src" ] || src=$(ask_dir "Pick the folder with your Breath of the Wild dump (game, update, DLC and save)" "$HOME") || return 1
    scan_dump "$src"
    if [ -n "$SCAN_BASE" ] && [ -n "$SCAN_UPDATE" ] && [ -n "$SCAN_DLC" ] && [ -n "$SCAN_SAVE" ]; then break; fi
    msg_box warning "Not everything was found in $src.
$([ -n "$SCAN_BASE" ] && echo "OK: game" || echo "MISSING: the base game (code/U-King.rpx with content and meta)")
$([ -n "$SCAN_UPDATE" ] && echo "OK: update v$UPDATE_VER" || echo "MISSING: the update v$UPDATE_VER")
$([ -n "$SCAN_DLC" ] && echo "OK: DLC v$DLC_VER" || echo "MISSING: the DLC v$DLC_VER")
$([ -n "$SCAN_SAVE" ] && echo "OK: save" || echo "MISSING: a save with user/80000001 (a brand new game hangs in Cemu)")
$SCAN_NOTE"
    if [ "$YES" = 1 ] || [ -n "$DUMP" ]; then return 1; fi
    ask_yesno "Try another folder?" || return 1
    src=
  done
  DUMP=$src
  progress "    game: $SCAN_BASE"; progress "    update: $SCAN_UPDATE"; progress "    DLC: $SCAN_DLC"; progress "    save: $SCAN_SAVE"
  MODSRC=
  src=${MOD:-$DUMP}
  while :; do
    if gp=$(find_mod "$src"); then MODSRC=$gp; break; fi
    msg_box warning "The Hyrule Online mod pack was not found in $src.
It is the folders BreathOfTheWild_BCML, bcmlPatches (and HyruleOnline) inside the graphicPacks folder of your Windows install. See README.md."
    [ "$YES" = 1 ] || [ -n "$MOD" ] && return 1
    src=$(ask_dir "Pick the folder with the Hyrule Online mod pack (the Windows install folder or its graphicPacks folder)" "$HOME") || return 1
  done
  progress "    mod pack: $MODSRC"
}

step_dump() {
  local save=$DIR/cemu/mlc01/usr/save/00050000/101c9400
  if [ "$DRY" = 1 ] && [ -z "$DUMP" ]; then echo "    [dry-run] would ask for the dump folder, check it, and copy game, update, DLC and the save"; return 0; fi
  pick_sources || return 1
  if [ "$DRY" = 1 ]; then
    echo "    [dry-run] would $([ "$LINK" = 1 ] && echo link || echo copy) game, update and DLC into $DIR and copy the save to $save"
    return 0
  fi
  progress "    $([ "$LINK" = 1 ] && echo linking || echo copying) the game files (this takes a while)"
  copy_title "$SCAN_BASE" "$DIR/game" && copy_title "$SCAN_UPDATE" "$DIR/update" && copy_title "$SCAN_DLC" "$DIR/dlc" || return 1
  if [ ! -d "$save" ]; then
    mkdir -p "$(dirname "$save")" && rm -rf "$save.partial" && cp -a "$SCAN_SAVE" "$save.partial" && mv "$save.partial" "$save" || return 1
  fi
  state_set mod_src "$MODSRC"
}

step_config() {
  local c=$DIR/cemu gp p
  if [ "$DRY" = 1 ]; then echo "    [dry-run] would copy the mod pack, link update/DLC into Cemu's mlc01, add the mod's data files to the prefix, and write settings.xml and the game profile"; return 0; fi
  gp=$(state_get mod_src)
  [ -d "$gp" ] || { echo "The mod pack folder is not set. Run install.sh again." >&2; return 1; }
  install_appdata || return 1
  for p in BreathOfTheWild_BCML bcmlPatches HyruleOnline; do
    [ -d "$gp/$p" ] && [ ! -e "$c/graphicPacks/$p" ] || continue
    rm -rf "$c/graphicPacks/$p.partial"
    cp -a "$gp/$p" "$c/graphicPacks/$p.partial" && mv "$c/graphicPacks/$p.partial" "$c/graphicPacks/$p" || return 1
  done
  mkdir -p "$c/mlc01/usr/title/0005000e" "$c/mlc01/usr/title/0005000c" "$c/gameProfiles" || return 1
  ln -sfn "$DIR/update" "$c/mlc01/usr/title/0005000e/101c9400" && ln -sfn "$DIR/dlc" "$c/mlc01/usr/title/0005000c/101c9400" || return 1
  cat > "$c/gameProfiles/00050000101c9400.ini" <<'EOF'
[General]
loadSharedLibraries = true
startWithPadView = false

[CPU]
cpuMode = Auto
threadQuantum = 45000

[Graphics]
accurateShaderMul = true
precompiledShaders = auto

[Controller]
EOF
  if [ -f "$c/settings.xml" ]; then echo "    settings.xml already exists, kept"; return 0; fi
  # Same values as the working setup: Vulkan, extended memory, the multiplayer mod and the online display pack.
  cat > "$c/settings.xml.tmp" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<content>
    <mlc_path>$(winpath "$c/mlc01")</mlc_path>
    <permanent_storage>true</permanent_storage>
    <language>0</language>
    <console_language>1</console_language>
    <check_update>false</check_update>
    <vk_warning>false</vk_warning>
    <gp_download>true</gp_download>
    <GamePaths>
        <Entry>$(winpath "$DIR/game")</Entry>
    </GamePaths>
    <GraphicPack>
        <Entry filename="graphicPacks\\downloadedGraphicPacks\\BreathOfTheWild\\Mods\\ExtendedMemory\\rules.txt"/>
        <Entry filename="graphicPacks\\BreathOfTheWild_BCML\\rules.txt"/>
        <Entry filename="graphicPacks\\bcmlPatches\\MilkBarLauncher\\rules.txt"/>
$([ -f "$c/graphicPacks/HyruleOnline/Graphics/rules.txt" ] && cat <<'EOP'
        <Entry filename="graphicPacks\HyruleOnline\Graphics\rules.txt">
            <Preset><category>Aspect Ratio</category><preset>Online Display</preset></Preset>
            <Preset><category>Shadows</category><preset>High (200%)</preset></Preset>
            <Preset><category>Resolution</category><preset>Online Resolution</preset></Preset>
            <Preset><category>Square HUD Mode</category><preset>Enabled (Experimental, Will Have Some Issues)</preset></Preset>
            <Preset><category>Shadow Draw Distance</category><preset>Very High (Recommended)</preset></Preset>
            <Preset><category>Anti-Aliasing</category><preset>Normal FXAA (Default)</preset></Preset>
        </Entry>
EOP
)
        <Entry filename="graphicPacks\\downloadedGraphicPacks\\BreathOfTheWild\\Mods\\FPS++\\rules.txt">
            <Preset><category>Mode</category><preset>Normal Settings</preset></Preset>
            <Preset><category>FPS Limit</category><preset>120FPS Limit</preset></Preset>
        </Entry>
    </GraphicPack>
    <Graphic>
        <api>1</api>
        <VSync>0</VSync>
        <GX2DrawdoneSync>true</GX2DrawdoneSync>
        <AsyncCompile>true</AsyncCompile>
    </Graphic>
    <Audio>
        <api>0</api>
        <delay>2</delay>
        <TVChannels>1</TVChannels>
        <TVVolume>100</TVVolume>
        <TVDevice>default</TVDevice>
    </Audio>
    <Account>
        <PersistentId>2147483649</PersistentId>
        <OnlineEnabled>false</OnlineEnabled>
    </Account>
</content>
EOF
  mv "$c/settings.xml.tmp" "$c/settings.xml"
}

step_launcher() {
  local f
  if [ "$DRY" = 1 ]; then echo "    [dry-run] would install hyrule-online.sh into $DIR and write $DESKTOP_FILE"; return 0; fi
  for f in install.sh hyrule-online.sh lib.sh steam_shortcut.py launcher.py; do
    [ -f "$SELF_DIR/$f" ] || { [ "$f" = launcher.py ] && continue; return 1; }
    [ "$SELF_DIR/$f" -ef "$DIR/$f" ] || install -m 755 "$SELF_DIR/$f" "$DIR/$f" || return 1
  done
  [ "$(app_version)" = unknown ] || printf '%s\n' "$(app_version)" > "$DIR/VERSION" || return 1
  mkdir -p "$DIR/logs" "$DIR/run" "$(dirname "$DESKTOP_FILE")" || return 1
  cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Name=$APP_NAME
Comment=Play, host or join a Hyrule Online server (Breath of the Wild co-op)
Exec="$DIR/hyrule-online.sh"
Path=$DIR
Terminal=false
Categories=Game;
Actions=Stop;

[Desktop Action Stop]
Name=Stop session
Exec="$DIR/hyrule-online.sh" --stop
EOF
}

steam_configs() { local d; for d in "$STEAM_ROOT"/userdata/*/config; do [ -d "$d" ] && [[ $d != */userdata/0/config ]] && printf '%s\n' "$d/shortcuts.vdf"; done; }

# Steam rewrites shortcuts.vdf when it exits, so the file is only edited while Steam is closed.
steam_closed() {
  pgrep -x steam >/dev/null 2>&1 || return 0
  if [ "$YES" = 1 ] || ! ask_yesno "Steam is running. It rewrites its shortcut list when it closes, so $APP_NAME can only be added while Steam is closed.

Close Steam now? (Save anything you have open in it first. You can start Steam again afterwards.)"; then
    return 1
  fi
  steam -shutdown >/dev/null 2>&1 &
  for _ in $(seq 1 30); do pgrep -x steam >/dev/null 2>&1 || return 0; sleep 1; done
  return 1
}

step_steam() {
  local files=() icon=() f
  while IFS= read -r f; do files+=("$f"); done < <(steam_configs)
  [ -f "$DIR/hyrule-online.png" ] && icon=(--icon "$DIR/hyrule-online.png")
  if [ "$DRY" = 1 ]; then echo "    [dry-run] would add the '$APP_NAME' non-Steam shortcut to ${#files[@]} Steam user(s) (Steam running: $(pgrep -x steam >/dev/null 2>&1 && echo yes || echo no))"; return 0; fi
  if [ "${#files[@]}" = 0 ]; then
    echo "    No Steam user found. Start Steam once, sign in, close it, then run $DIR/install.sh again." >&2
    return 2
  fi
  if ! steam_closed; then
    echo "    Steam is still open, so the Steam entry was not added. Close Steam, then run $DIR/install.sh again." >&2
    return 2
  fi
  python3 "$DIR/steam_shortcut.py" add "$DIR/hyrule-online.sh" "$APP_NAME" "${files[@]}" --startdir "$DIR" "${icon[@]}" || return 1
}

summary() {
  local text
  if [ "$DRY" = 1 ]; then echo "Dry run finished. Nothing was changed."; return 0; fi
  if [ -z "$DEFERRED" ]; then
    rm -rf "$DIR/downloads"
    text="Everything is ready.

Gaming Mode: Library > Non-Steam > $APP_NAME > Play (start Steam again if you closed it).
Desktop Mode: run $DIR/hyrule-online.sh or the \"$APP_NAME\" menu entry.

The menu offers Play on this device, Host a server, Join a server and Stop. To join you need the invite (or address, port and password) from the host.
Controller setup is not automated: if the controller does nothing in game, set it up in Cemu (Options > Input settings)."
  else
    text="Almost done. Still to do: $DEFERRED
Run $DIR/install.sh again to finish. Steps that are already done are skipped."
  fi
  progress_stop
  msg_box info "$text"
}

do_uninstall() {
  local files=() f e big keep_game=1 keep_save=1 kept=''
  DIR=${DIR:-$(state_get install_dir)}
  DIR=${DIR:-$HOME/hyrule-online}; DIR=${DIR%/}
  valid_dir "$DIR" && [ -f "$DIR/.hyrule-online" ] || die "No $APP_NAME install found in $DIR."
  big=$(du -shL "$DIR/game" "$DIR/update" "$DIR/dlc" 2>/dev/null | awk '{s=s" "$1} END{print s}')
  if [ "$DRY" = 1 ]; then
    echo "[dry-run] would remove the Steam entry, $DESKTOP_FILE and $DIR (asking before deleting the game copy [$big ] and the save)"
    return 0
  fi
  ask_yesno "Remove $APP_NAME and its install folder $DIR?" || [ "$YES" = 1 ] || exit 0
  systemctl --user stop hyrule-server.service hyrule-client.service 2>/dev/null
  if [ "$YES" != 1 ]; then
    ask_yesno "Also delete the copied game files (game, update, DLC:$big )? Your original dump is never touched." && keep_game=0
    ask_yesno "Also delete your copied save? This is the progress made on this device." && keep_save=0
  fi
  while IFS= read -r f; do files+=("$f"); done < <(steam_configs)
  if [ "${#files[@]}" -gt 0 ]; then
    if steam_closed; then python3 "$SELF_DIR/steam_shortcut.py" remove "$DIR/hyrule-online.sh" "${files[@]}"
    else echo "Steam is still open: its shortcut was not removed. Close Steam and run this again, or delete \"$APP_NAME\" in Steam." >&2; fi
  fi
  rm -f "$DESKTOP_FILE"
  if [ "$keep_save" = 1 ] && [ -d "$DIR/cemu/mlc01/usr/save/00050000/101c9400" ]; then
    mkdir -p "$DIR/save-backup" && mv "$DIR/cemu/mlc01/usr/save/00050000/101c9400" "$DIR/save-backup/" && kept+=" save-backup"
  fi
  for e in "$DIR"/* "$DIR"/.[!.]*; do
    case ${e##*/} in
      game|update|dlc) if [ "$keep_game" = 1 ]; then kept+=" ${e##*/}"; continue; fi ;;
      save-backup|.hyrule-online) continue ;;
    esac
    rm -rf -- "$e"
  done
  if [ -n "$kept" ]; then
    echo "Kept in $DIR:$kept"
  else
    rm -f "$DIR/.hyrule-online"; rmdir "$DIR" 2>/dev/null
  fi
  rm -f "$STATE" "$STATE.tmp"; rmdir "$(dirname "$STATE")" 2>/dev/null
  msg_box info "$APP_NAME was removed.${kept:+
Kept in $DIR:$kept}"
}

cleanup() { progress_stop; [ -n "$DIR" ] && rm -rf "$DIR/.extract" 2>/dev/null; return 0; }

main() {
  while [ $# -gt 0 ]; do
    case $1 in
      --dry-run) DRY=1 ;;
      --yes|-y) YES=1 ;;
      --link) LINK=1 ;;
      --uninstall) UNINSTALL=1 ;;
      --dump|--mod|--dir)
        [ $# -ge 2 ] || { echo "$1 needs a value." >&2; exit 2; }
        case $1 in --dump) DUMP=$2 ;; --mod) MOD=$2 ;; --dir) DIR=$2 ;; esac; shift ;;
      -h|--help) usage; exit 0 ;;
      *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
    shift
  done
  [ "$YES" = 1 ] && HYRULE_NO_GUI=1
  if [ "$YES" = 1 ] && [ -z "$DUMP" ] && [ "$UNINSTALL" = 0 ] && [ "$DRY" = 0 ]; then echo "--yes needs --dump PATH." >&2; exit 2; fi
  [ "$LINK" = 1 ] && NEED_GB=8
  trap cleanup EXIT
  trap 'exit 130' INT TERM

  if [ "$UNINSTALL" = 1 ]; then do_uninstall; exit 0; fi

  if [ "$DRY" != 1 ] && [ "$YES" != 1 ] && [ -z "$(state_get install_dir)" ]; then
    ask_yesno "This sets up $APP_NAME on this device: it downloads Cemu and the Milk Bar client, prepares Proton, copies your Breath of the Wild dump and adds a Steam entry.

You need about 15 GB of free space and your own game dump and mod pack (see README.md). Continue?" || exit 0
  fi
  [ "$DRY" = 1 ] && echo "Dry run: nothing will be changed."
  echo "$APP_NAME $(app_version)"
  progress_start "$APP_NAME setup"
  say_step "Checking this device"
  preflight || die "$PF_ERR"
  say_step "Choosing the install folder"
  step_dir
  state_set version "$(app_version)"
  run_step download "Downloading and verifying the components" step_download
  run_step extract "Unpacking Cemu, the graphic packs and the Milk Bar client" step_extract
  run_step server "Unpacking the dedicated server (for hosting)" step_server
  run_step prefix "Preparing Wine (.NET 8 and Visual C++ in their own prefix)" step_prefix
  run_step dump "Getting your game files" step_dump
  run_step config "Applying the Cemu configuration" step_config
  [ "$DRY" = 1 ] || state_set done_launcher 0   # the scripts are copied again on every run, so an update reaches the install
  run_step launcher "Installing the launcher and the menu entry" step_launcher
  run_step steam "Adding the Steam library entry" step_steam
  summary
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then main "$@"; fi
