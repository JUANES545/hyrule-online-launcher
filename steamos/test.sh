#!/usr/bin/env bash
# shellcheck disable=SC2034  # variables are read by the functions in lib.sh
# Offline checks for the SteamOS scripts: no network, no game files, nothing outside a temp folder.
set -u
TDIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
HERE=$TDIR
TMP=$(mktemp -d) || exit 1
trap 'rm -rf "$TMP"' EXIT
FAILS=0 PASSES=0

ok() { PASSES=$((PASSES + 1)); }
bad() { FAILS=$((FAILS + 1)); echo "FAIL: $*"; }
check() { local what=$1; shift; if "$@" >/dev/null 2>&1; then ok; else bad "$what"; fi; }
refuse() { local what=$1; shift; if "$@" >/dev/null 2>&1; then bad "$what (should have failed)"; else ok; fi; }
equal() { if [ "$2" = "$3" ]; then ok; else bad "$1: expected '$2', got '$3'"; fi; }

# On Windows Git Bash python3 can be a Store stub, so use the first interpreter that really runs.
PY=
for c in python3 python; do if "$c" -c 'import sys; sys.exit(sys.version_info < (3, 6))' 2>/dev/null; then PY=$c; break; fi; done
[ -n "$PY" ] || { echo "No working python found."; exit 1; }
SHORT="$HERE/steam_shortcut.py"
py_run() { "$PY" "$SHORT" "$@"; }
mkdir -p "$TMP/bin"; printf '#!/bin/sh\nexec "%s" "$@"\n' "$(command -v "$PY")" > "$TMP/bin/python3"; chmod +x "$TMP/bin/python3"
PATH=$TMP/bin:$PATH

echo "== syntax =="
for f in "$HERE"/*.sh; do check "bash -n ${f##*/}" bash -n "$f"; done
check "python compiles" "$PY" -m py_compile "$SHORT"
if [ -f "$HERE/launcher.py" ]; then
  check "launcher.py compiles" "$PY" -m py_compile "$HERE/launcher.py"
  check "launcher.py starts with a python3 shebang" grep -q "^#!/usr/bin/env python3" "$HERE/launcher.py"
  [ -f "$HERE/launcher_test.py" ] && check "launcher.py logic tests" "$PY" "$HERE/launcher_test.py"
else echo "launcher.py not found, skipped"; fi
STUB=$HERE/../tests/fixtures/steamos/hyrule-online-stub.sh
if command -v shellcheck >/dev/null 2>&1; then
  for f in "$HERE"/*.sh "$STUB"; do
    [ -f "$f" ] || continue
    if out=$(shellcheck -x -S warning "$f" 2>&1); then ok; else bad "shellcheck ${f##*/}"; printf '%s\n' "$out"; fi
  done
else echo "shellcheck not installed, skipped"; fi
no_cr() { local f; for f; do [ "$(tr -cd '\r' < "$f" | wc -c)" = 0 ] || return 1; done; }
check "no CRLF in scripts" no_cr "$HERE"/*.sh "$HERE"/*.py
[ ! -f "$STUB" ] || check "no CRLF in the GUI check stub and script" no_cr "$STUB" "$HERE/../tests/steamos_gui_check.py"

echo "== parity manifest =="
# tests\ParityTests.cs (Windows) compares parity.json with the Windows code; here only the file itself is checked.
if [ -f "$HERE/parity.json" ]; then
  check "parity.json is valid JSON with its sections" "$PY" -c 'import json,sys; m=json.load(open(sys.argv[1],encoding="utf-8")); [m[k] for k in ("version","tabs","facts","presets","cli","mirrored","notApplicable")]' "$HERE/parity.json"
  if [ -f "$HERE/../VERSION" ]; then
    check "parity.json is at the VERSION release" "$PY" -c 'import json,sys; sys.exit(json.load(open(sys.argv[1],encoding="utf-8"))["version"] != open(sys.argv[2]).readline().strip())' "$HERE/parity.json" "$HERE/../VERSION"
  fi
else bad "steamos/parity.json is missing"; fi

echo "== lib.sh =="
export HYRULE_NO_GUI=1
. "$HERE/lib.sh"
for h in 192.168.1.43 example.com my-host.lan 10.0.0.1 localhost; do check "host $h" valid_host "$h"; done
# '$(x)' must stay literal: it checks that shell syntax is rejected as a host name.
# shellcheck disable=SC2016
for h in "" 256.1.1.1 1.2.3 1.2.3.4.5 -bad.com bad-.com "a b" a..b 'a;b' '$(x)'; do refuse "host '$h'" valid_host "$h"; done
for p in 1 5050 65535 05050; do check "port $p" valid_port "$p"; done
for p in 0 65536 -1 abc "" 1.5 123456; do refuse "port '$p'" valid_port "$p"; done
for n in Link a my_name-1 1234567890123456; do check "name $n" valid_name "$n"; done
for n in "" 12345678901234567 "a b" 'x;y'; do refuse "name '$n'" valid_name "$n"; done
equal winpath 'Z:\home\deck\x y\Cemu.exe' "$(winpath /home/deck/x\ y/Cemu.exe)"
equal "ask_choice default" Host "$(ask_choice t q Host Join Host 2>/dev/null <<<'')"
equal "ask_choice prefix" Join "$(ask_choice t q Host Join Host 2>/dev/null <<<'j')"
refuse "ask_choice cancelled" ask_choice t q Host Join Host </dev/null

echo "== steam_shortcut.py =="
equal "appid CLI" "$("$PY" -c 'import zlib; print(zlib.crc32(b"\"12345\"6789") | 0x80000000)')" "$(py_run appid 12345 6789 | head -n1)"
"$PY" - "$SHORT" "$TMP" <<'PY' || FAILS=$((FAILS + 1))
import importlib.util, os, sys
spec = importlib.util.spec_from_file_location("s", sys.argv[1]); s = importlib.util.module_from_spec(spec); spec.loader.exec_module(s)
tmp = sys.argv[2]
n = [0]
def check(cond, what):
    n[0] += 1
    if not cond:
        print("FAIL:", what); check.bad = True
check.bad = False

check(s.appid("", "") == 0x80000000, "appid of empty strings")
check(s.appid("123456", "789") == 0xCBF43926 | 0x80000000, "appid is crc32(exe+name) | 0x80000000")
expected = (b"\x00shortcuts\x00" b"\x000\x00" b"\x02appid\x00\x01\x00\x00\x00" b"\x01AppName\x00A\x00" b"\x08\x08\x08")
tree = {"shortcuts": {"0": {"appid": 1, "AppName": "A"}}}
check(s.dump(tree) == expected, "known byte layout")
check(s.parse(expected) == tree, "parse of the known bytes")
check(s.parse(s.dump(tree)) == tree, "round trip")

other = {"shortcuts": {"0": {"appid": 7, "AppName": "Other", "Exe": '"/x/other"', "tags": {"0": "a"}}}}
path = os.path.join(tmp, "shortcuts.vdf")
open(path, "wb").write(s.dump(other))
entry = s.make_entry("/h/hyrule-online.sh", "Hyrule Online", "/h", "", "", "Hyrule Online")
check(s.update_file(path, lambda t: s.add(t, dict(entry))) is True, "add changes the file")
t = s.parse(open(path, "rb").read())["shortcuts"]
check(len(t) == 2 and t["0"]["AppName"] == "Other", "existing entry kept, new one appended")
check(t["1"]["Exe"] == '"/h/hyrule-online.sh"' and t["1"]["appid"] == s.appid('"/h/hyrule-online.sh"', "Hyrule Online"), "new entry fields")
check(s.update_file(path, lambda t: s.add(t, dict(entry))) is False, "second add changes nothing")
check(len(s.parse(open(path, "rb").read())["shortcuts"]) == 2, "no duplicate")
baks = [f for f in os.listdir(tmp) if ".bak-hyrule-" in f]
check(len(baks) == 1, "exactly one backup")
check(s.update_file(path, lambda t: s.remove(t, "/h/hyrule-online.sh")) is True, "remove changes the file")
check(s.parse(open(path, "rb").read()) == other, "remove restores the original content")
check(s.update_file(path, lambda t: s.remove(t, "/h/hyrule-online.sh")) is False, "second remove changes nothing")
new = os.path.join(tmp, "new.vdf")
check(s.update_file(new, lambda t: s.add(t, dict(entry))) is True and len(s.parse(open(new, "rb").read())["shortcuts"]) == 1, "missing file is created")
bad = os.path.join(tmp, "bad.vdf")
open(bad, "wb").write(b"\x00shortcuts\x00\x05x\x00")
try:
    s.update_file(bad, lambda t: s.add(t, dict(entry))); check(False, "unknown type is refused")
except ValueError:
    check(open(bad, "rb").read() == b"\x00shortcuts\x00\x05x\x00", "unknown type: file untouched")
print("python checks:", n[0], "FAILED" if check.bad else "ok")
sys.exit(1 if check.bad else 0)
PY

echo "== install.sh =="
. "$HERE/install.sh"
STATE=$TMP/state/state
state_set a 1; state_mark dir; state_set a 2
equal "state overwrite" 2 "$(state_get a)"
check "state done" state_done dir
refuse "state not done" state_done download
if HYRULE_STATE=$TMP/state/state bash -c '. "$1"; state_done dir' _ "$HERE/install.sh"; then ok; else bad "state survives a new shell (resume)"; fi
DRY=1; state_set zzz 1; DRY=0
equal "dry run does not write state" "" "$(state_get zzz)"

printf 'abc' > "$TMP/f"
check "sha_ok (upper case ok)" sha_ok "$TMP/f" BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD
refuse "sha_ok mismatch" sha_ok "$TMP/f" 0000
check "valid_dir" valid_dir /home/deck/hyrule-online
refuse "valid_dir space" valid_dir "/home/deck/my games"
refuse "valid_dir relative" valid_dir hyrule
refuse "valid_dir parent" valid_dir /home/../etc

# Fake Steam library with Proton versions.
S=$TMP/steam; STEAM_ROOT=$S
for v in 9.0 10.0 11.0; do
  mkdir -p "$S/steamapps/common/Proton $v/files/bin"; printf '#!/bin/sh\n' > "$S/steamapps/common/Proton $v/files/bin/wine64"; chmod +x "$S/steamapps/common/Proton $v/files/bin/wine64"
done
mkdir -p "$S/steamapps/common/Proton - Experimental/files/bin"
equal "find_proton picks the oldest 10+" "$S/steamapps/common/Proton 10.0" "$(find_proton)"
rm -rf "$S/steamapps/common/Proton 10.0" "$S/steamapps/common/Proton 11.0"
equal "find_proton ignores 9.x and Experimental" "" "$(find_proton)"
mkdir -p "$TMP/lib2/steamapps/common/Proton 10.0/files/bin"; printf '#!/bin/sh\n' > "$TMP/lib2/steamapps/common/Proton 10.0/files/bin/wine64"; chmod +x "$TMP/lib2/steamapps/common/Proton 10.0/files/bin/wine64"
printf '"libraryfolders"\n{\n\t"1"\n\t{\n\t\t"path"\t\t"%s"\n\t}\n}\n' "$TMP/lib2" > "$S/steamapps/libraryfolders.vdf"
equal "find_proton reads libraryfolders.vdf" "$TMP/lib2/steamapps/common/Proton 10.0" "$(find_proton)"

# Fake dump: base game, update, DLC, save and the mod pack.
D=$TMP/dump
mk_title() { mkdir -p "$1/meta" "$1/content/$4"; printf '<?xml version="1.0"?>\n<menu><title_version type="unsignedInt" length="4">%s</title_version>\n<title_id type="hexBinary" length="8">%s</title_id></menu>\n' "$3" "$2" > "$1/meta/meta.xml"; }
mk_title "$D/game" 00050000101C9400 0 x; mkdir -p "$D/game/code"; : > "$D/game/code/U-King.rpx"
mk_title "$D/update" 0005000E101C9400 208 x
mk_title "$D/dlc" 0005000C101C9400 80 0010
mkdir -p "$D/save/user/80000001"; : > "$D/save/user/80000001/game_data.sav"
mkdir -p "$D/gp/bcmlPatches/MilkBarLauncher" "$D/gp/BreathOfTheWild_BCML/content/Actor/Pack"
: > "$D/gp/bcmlPatches/MilkBarLauncher/rules.txt"; : > "$D/gp/BreathOfTheWild_BCML/rules.txt"; : > "$D/gp/BreathOfTheWild_BCML/content/Actor/Pack/Jugador32.sbactorpack"
scan_dump "$D"
equal "scan base" "$D/game" "$SCAN_BASE"; equal "scan update" "$D/update" "$SCAN_UPDATE"
equal "scan dlc" "$D/dlc" "$SCAN_DLC"; equal "scan save" "$D/save" "$SCAN_SAVE"
equal "find_mod" "$D/gp" "$(find_mod "$D")"
mk_title "$D/update" 0005000E101C9400 192 x
scan_dump "$D"; equal "old update is rejected" "" "$SCAN_UPDATE"
mk_title "$D/update" 0005000E101C9400 208 x
scan_dump "$D"; equal "scan save" "$D/save" "$SCAN_SAVE"
mkdir -p "$D/boss/00050000/101c9400/user/80000001"
scan_dump "$D"; equal "boss data is not a save" "$D/save" "$SCAN_SAVE"
rm -rf "$D/boss" "$D/save"; scan_dump "$D"; equal "no save found" "" "$SCAN_SAVE"
refuse "find_mod without the pack" find_mod "$TMP/steam"

# copy_title copies only what is needed and does not redo a finished copy.
mkdir -p "$D/game/junk"; : > "$D/game/junk/big.bin"
LINK=0; copy_title "$D/game" "$TMP/out-game"
check "copy keeps code" test -f "$TMP/out-game/code/U-King.rpx"
refuse "copy skips other folders" test -e "$TMP/out-game/junk"
check "copy is not redone" copy_title "$D/game" "$TMP/out-game"
if [ "$(uname -s)" = Linux ]; then
  LINK=1; copy_title "$D/update" "$TMP/out-link"; check "link mode links" test -L "$TMP/out-link"; LINK=0
fi

# patch_cli on a synthetic DLL.
DIR=$TMP/inst; mkdir -p "$DIR/milkbar"
printf 'AAAA\x20\xEE\x02\x00\x00BBBB' > "$DIR/milkbar/MilkBar.CLI.dll"
check "patch_cli" patch_cli
equal "patched bytes" 41414141207011010042424242 "$(od -An -tx1 -v < "$DIR/milkbar/MilkBar.CLI.dll" | tr -d ' \n')"
check "patch keeps .orig" cmp -s "$DIR/milkbar/MilkBar.CLI.dll.orig" <(printf 'AAAA\x20\xEE\x02\x00\x00BBBB')
check "patch_cli is repeatable" patch_cli
printf 'nothing here' > "$DIR/milkbar/MilkBar.CLI.dll"; rm -f "$DIR/milkbar/MilkBar.CLI.dll.orig"
refuse "patch_cli refuses an unknown build" patch_cli
mkdir -p "$DIR/downloads" "$DIR/prefix/drive_c/users/Public" "$DIR/prefix/drive_c/users/u"
python3 -c 'import sys,zipfile; zipfile.ZipFile(sys.argv[1],"w").writestr("Milk Bar Launcher.dll", b"not the real dll")' "$DIR/downloads/MilkBarLauncher.zip"
refuse "install_appdata refuses a DLL without the data files" install_appdata
equal "install_appdata leaves no file behind on failure" "" "$(ls "$DIR/prefix/drive_c/users/u/AppData/Roaming/BOTWM" 2>/dev/null)"

echo "== hyrule-online.sh =="
. "$HERE/hyrule-online.sh"
refuse "no TCP pre-check (it crashes the Milk Bar server)" grep -q tcp_ok "$TDIR/hyrule-online.sh"
for v in DISPLAY XAUTHORITY DBUS_SESSION_BUS_ADDRESS; do check "session passes $v to systemd-run" grep -q "for var in .*$v" "$TDIR/hyrule-online.sh"; done
check "session uses systemd-inhibit" grep -q 'systemd-inhibit --what=sleep' "$TDIR/hyrule-online.sh"
refuse "no /dev/tcp probe" grep -q /dev/tcp "$TDIR/hyrule-online.sh" "$TDIR/install.sh" "$TDIR/lib.sh"
refuse "no nc probe" grep -qE '(^|[ |;(])(nc|ncat|telnet) ' "$TDIR/hyrule-online.sh"
check "server keeps stdin open" grep -q 'tail -f /dev/null' "$TDIR/hyrule-online.sh"
check "server rolls forward to .NET 8" grep -q 'DOTNET_ROLL_FORWARD=Major' "$TDIR/hyrule-online.sh"
check "installer unpacks the server step" grep -q 'run_step server' "$TDIR/install.sh"

INI=$TMP/ServerConfig.ini
ini_get() { sed -n "s/^$1=//p" "$INI"; }
# The rule digits read back from an ini, the way the Windows launcher reads the same keys.
ini_rules() {
  local k b=''
  for k in EnemySync QuestSync KorokSync TowerSync ShrineSync LocationSync DungeonSync; do if [ "$(ini_get $k)" = True ]; then b+="1"; else b+="0"; fi; done
  printf '%s%s' "$b" "$(ini_get Special)"
}
write_server_ini "$INI" 0.0.0.0 6000 'p w;#=x' 'A description' 10111100
equal "ini ip" IP=0.0.0.0 "$(grep '^IP=' "$INI")"
equal "ini port" Port=6000 "$(grep '^Port=' "$INI")"
equal "ini password" 'p w;#=x' "$(ini_get Password)"
equal "ini description" 'A description' "$(ini_get Description)"
equal "ini default gamemode" 1 "$(grep -c '^DefaultGamemode=True$' "$INI")"
equal "ini section order" '[Connection] [ServerInformation] [Gamemode] [DefaultGamemode]' "$(grep '^\[' "$INI" | tr '\n' ' ' | sed 's/ $//')"
equal "ini empty password" Password= "$(write_server_ini "$INI" 127.0.0.1 5050 '' d 10111100; grep '^Password=' "$INI")"
check "ini has no CR" no_cr "$INI"
equal "preset count" "${#PRESETS[@]}" "${#PRESET_BITS[@]}"
equal "default preset" "Game Completion" "${PRESETS[0]}"
equal "default rules are the shipped ones" "EnemySync=True QuestSync=False KorokSync=True TowerSync=True ShrineSync=True LocationSync=True DungeonSync=False Special=0" \
  "$(write_server_ini "$INI" 127.0.0.1 5050 '' d "${PRESET_BITS[0]}"; sed -n '/^EnemySync/,$p' "$INI" | tr '\n' ' ' | sed 's/ $//')"
for i in "${!PRESETS[@]}"; do
  write_server_ini "$INI" 0.0.0.0 5050 '' d "${PRESET_BITS[i]}"
  equal "preset ${PRESETS[i]} round trip" "${PRESET_BITS[i]}" "$(ini_rules)"
  equal "preset ${PRESETS[i]} name" "${PRESETS[i]}" "$(ini_get Name)"
  check "preset ${PRESETS[i]} digits" grep -Eq '^[01]{7}[012]$' <<<"${PRESET_BITS[i]}"
done
write_server_ini "$INI" 0.0.0.0 5050 '' d 01010102
equal "custom rules round trip" 01010102 "$(ini_rules)"; equal "custom name" Custom "$(ini_get Name)"
equal "Hunter Vs Speedrunner is special 1" 1 "${PRESET_BITS[1]:7:1}"
equal "Death Swap is special 2" 2 "${PRESET_BITS[4]:7:1}"
equal "clean_text strips controls and trims" ab "$(clean_text $'  a\tb\n ' 10)"
equal "clean_text cuts" abc "$(clean_text abcdef 3)"
check "host port 1024" valid_host_port 1024; check "host port 65535" valid_host_port 65535
refuse "host port 1023" valid_host_port 1023; refuse "host port 65536" valid_host_port 65536
BITS=10111100
ask_rules 2>/dev/null <<<$'custom\nEnemies, Koroks\nDeath\n'
equal "custom rules dialog (terminal)" 10100002 "$BITS"
ask_rules 2>/dev/null <<<$'hide\n'
equal "preset dialog (terminal)" 10000000 "$BITS"

pi() { parse_invite "$1" && printf '%s|%s|%s' "$INV_HOST" "$INV_PORT" "$INV_PASS"; }
equal "invite plain" '192.168.1.43|5050|' "$(pi hyrule://192.168.1.43:5050)"
equal "invite upper case scheme and space" 'host.example|6000|p w&x' "$(pi '  HYRULE://host.example:6000?pw=p%20w%26x ')"
equal "invite trailing slash" 'h|1|' "$(pi hyrule://h:1//)"
equal "invite default port" 'host|5050|' "$(pi host)"
equal "host:port" 'host|7000|' "$(pi host:7000)"
equal "invite pw among other keys" 'h|5050|xé' "$(pi 'hyrule://h:5050?a=b&pw=x%C3%A9&c=d')"
equal "invite empty pw" 'h|5050|' "$(pi 'hyrule://h:5050?pw=')"
equal "invite plus is kept" 'h|5050|a+b' "$(pi 'h:5050?pw=a+b')"
equal "invite bad escape is kept" 'h|5050|%zz' "$(pi 'h:5050?pw=%zz')"
for t in "" hyrule:// h:0 h:65536 h:abc h: :5050 "bad host:5050" a..b:5050 'h;x:5050' h:5050:6000 "$(printf 'a%.0s' $(seq 1 201))"; do refuse "invite '$t'" parse_invite "$t"; done
for pw in 'p w&x#=/?%' é a+b 100% 'a\x41b' '"q"' "it's"; do
  equal "invite round trip $pw" "h|5050|$pw" "$(pi "$(invite_url h 5050 "$pw")")"
done

# Local binds loopback only; Host binds every address. The unit start and the game are stubbed out.
run_unit() { :; }; wait_server() { :; }; start_session() { :; }; wait_connect() { :; }
HERE=$TMP; CONF=$TMP/conf; mkdir -p "$TMP/server"; : > "$TMP/server/MBL.DedicatedServer.exe"
NOPLAY=1 ASK=0
INI=$TMP/server/ServerConfig.ini
server_session local >/dev/null 2>&1
equal "local binds loopback" IP=127.0.0.1 "$(grep '^IP=' "$INI")"
equal "local port" Port=5050 "$(grep '^Port=' "$INI")"
equal "local has no password" Password= "$(grep '^Password=' "$INI")"
equal "local description" "$DEFAULT_DESC" "$(ini_get Description)"
server_session host 6000 'p w' >/dev/null 2>&1
equal "host binds every address" IP=0.0.0.0 "$(grep '^IP=' "$INI")"
equal "host port" Port=6000 "$(grep '^Port=' "$INI")"
equal "host password" 'p w' "$(ini_get Password)"
equal "host saved the rules" "${PRESET_BITS[0]}" "$(conf_get rules)"
refuse "host refuses a password with a semicolon" eval '( server_session host 6001 "a;b" )'
server_session host 6000 'a#b=c' >/dev/null 2>&1
equal "host keeps # and = in a password" 'a#b=c' "$(ini_get Password)"
conf_set rules 00000002; conf_set desc 'My world'
server_session host >/dev/null 2>&1
equal "host reuses the saved rules" 00000002 "$(ini_rules)"
equal "host reuses the saved description" 'My world' "$(ini_get Description)"
equal "host reuses the saved port" Port=6000 "$(grep '^Port=' "$INI")"
join_session 'hyrule://h.example:6001?pw=a%20b' '' Zelda >/dev/null 2>&1
equal "join from an invite" 'h.example|6001|a b|Zelda' "$HOST|$PORT|$PASS|$NAME"
join_session h.example:6002 typed >/dev/null 2>&1
equal "a typed password wins over the invite" 'h.example|6002|typed' "$HOST|$PORT|$PASS"
join_session 10.0.0.9 6003 pw2 Navi >/dev/null 2>&1
equal "join with host port password name" '10.0.0.9|6003|pw2|Navi' "$HOST|$PORT|$PASS|$NAME"
equal "join saved the values" '10.0.0.9' "$(conf_get host)"
server_session local >/dev/null 2>&1
equal "local still binds loopback" IP=127.0.0.1 "$(grep '^IP=' "$INI")"
equal "local reuses the saved rules" 00000002 "$(ini_rules)"
check "local says it is private" grep -q 'Only this device can join' "$TDIR/hyrule-online.sh"
equal "failed join checklist (same as the Windows launcher)" "Check the invite, the password, that the host has a session running, and that its firewall allows the port." "$CHECKLIST"
[ ! -f "$TDIR/../src/Launcher/Lang.cs" ] || check "checklist is in the Windows launcher" grep -qF "$CHECKLIST" "$TDIR/../src/Launcher/Lang.cs"
check "wrong password is reported" grep -q 'could not join: wrong password' "$TDIR/hyrule-online.sh"

# --status, --rules/--desc and --presets: the command line the window uses.
IPS='2: wlan0    inet 192.168.1.43/24 scope global wlan0
4: tailscale0    inet 100.110.16.75/32 scope global tailscale0'
ip() { printf '%s\n' "$IPS"; }
SA=0 CA=0; server_active() { [ "$SA" = 1 ]; }; session_active() { [ "$CA" = 1 ]; }
LOG=$TMP/client.log; V=$(app_version); rm -f "$TMP/run/mode"
equal "status idle" "version=$V mode=idle server=down client=down joined=no port=" "$(status_line)"
set_mode host 5050; conf_set hostpass 'p w'; SA=1
equal "status host with both invites" "version=$V mode=host server=up client=down joined=no port=5050 invite=hyrule://192.168.1.43:5050?pw=p%20w invite_vpn=hyrule://100.110.16.75:5050?pw=p%20w" "$(status_line)"
set_mode local 5051
equal "status local has no invite" "version=$V mode=local server=up client=down joined=no port=5051" "$(status_line)"
SA=0 CA=1; set_mode join 5052; conf_set host h.example; echo 'SUCCESS' > "$LOG"
equal "status join" "version=$V mode=join server=down client=up joined=yes port=5052 host=h.example" "$(status_line)"
SA=0 CA=0
equal "status ignores a stale mode file" "version=$V mode=idle server=down client=down joined=no port=" "$(status_line)"
equal "status is one line" 1 "$(main --status | wc -l)"
equal "presets list" "10111100 Game Completion" "$(main --presets | head -n1)"
equal "five presets" 5 "$(main --presets | wc -l)"
equal "rules_bits by name" 00000002 "$(rules_bits 'death swap')"
equal "rules_bits by digits" 10101010 "$(rules_bits 10101010)"
for r in 1010101 10101013 'Nope' ''; do refuse "rules_bits '$r'" rules_bits "$r"; done
RULES_ARG="Hide n' seek" DESC_ARG='  Fun  world  ' NOPLAY=1 ASK=0
server_session local >/dev/null 2>&1
equal "--rules sets the rules" 10000000 "$(ini_rules)"
equal "--desc is cleaned and used" 'Fun  world' "$(ini_get Description)"
equal "--rules is remembered" 10000000 "$(conf_get rules)"
refuse "an unknown --rules is refused" eval '( RULES_ARG=bogus server_session local )'
RULES_ARG='' DESC_ARG=''
check "README documents the CLI used by the GUI" grep -q '^## CLI used by the GUI' "$TDIR/README.md"
echo "== version =="
V=
for f in "$TDIR/../VERSION" "$TDIR/VERSION"; do [ -f "$f" ] && { V=$(head -n1 "$f" | tr -d '[:space:]'); break; }; done
check "VERSION looks like 1.2.3" grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$' <<<"$V"
equal "app_version reads VERSION" "$V" "$(app_version)"
check "install.sh records the version" grep -q 'state_set version' "$TDIR/install.sh"
check "install.sh installs VERSION" grep -q '> "$DIR/VERSION"' "$TDIR/install.sh"
if [ -f "$TDIR/../.github/workflows/release.yml" ]; then
  # The Windows build, the installer and the release all read the first line of the same VERSION file.
  check "release.yml reads VERSION" grep -q 'head -n1 VERSION' "$TDIR/../.github/workflows/release.yml"
  check "release.yml names the tarball with it" grep -qF 'HyruleOnline-$v-steamos.tar.gz' "$TDIR/../.github/workflows/release.yml"
  check "Package.ps1 reads VERSION" grep -q "'VERSION'" "$TDIR/../scripts/Package.ps1"
  grep -q 'VERSION' <(sed -n '/Package SteamOS installer/,/Publish release/p' "$TDIR/../.github/workflows/release.yml") \
    || echo "NOTE: release.yml does not copy VERSION into the SteamOS tarball yet"
fi
equal "escape plain" abc-1._~ "$(url_escape abc-1._~)"
equal "escape specials" 'p%20w%26x%23%3D%2F%3F%25' "$(url_escape 'p w&x#=/?%')"
equal "escape utf-8" '%C3%A9' "$(url_escape é)"
equal "invite without password" hyrule://192.168.1.43:5050 "$(invite_url 192.168.1.43 5050 '')"
equal "invite with password" 'hyrule://192.168.1.43:5050?pw=p%20w%26x' "$(invite_url 192.168.1.43 5050 'p w&x')"
IPS='1: lo    inet 127.0.0.1/8 scope host lo
2: wlan0    inet 192.168.1.43/24 brd 192.168.1.255 scope global dynamic wlan0
3: docker0    inet 172.17.0.1/16 scope global docker0
4: tailscale0    inet 100.110.16.75/32 scope global tailscale0
5: eth0    inet 10.0.0.7/8 scope global eth0'
equal "lan ip prefers 192.168" 192.168.1.43 "$(pick_ip lan <<<"$IPS")"
equal "vpn ip" 100.110.16.75 "$(pick_ip vpn <<<"$IPS")"
equal "lan ip without 192.168" 10.0.0.7 "$(pick_ip lan <<<"$(grep -v wlan0 <<<"$IPS")")"
equal "no lan ip" "" "$(pick_ip lan <<<"$(grep -E 'lo |docker|tailscale' <<<"$IPS")")"
equal "100.63 is not a VPN address" "" "$(pick_ip vpn <<<'2: eth0    inet 100.63.0.1/24 scope global eth0')"
equal "172.32 is not private but still usable" 172.32.0.5 "$(pick_ip lan <<<'2: eth0    inet 172.32.0.5/24 scope global eth0')"
check "Cemu stays pinned to 1.26.2" grep -q 'cemu_1.26.2.zip' "$TDIR/install.sh"
HERE_CONF=$TMP/conf; HERE=$TMP; CONF=$HERE_CONF
HOST=10.0.0.5 PORT=5050 NAME=Link PASS='p w'; conf_save
[ "$(uname -s)" != Linux ] || equal "config perms" 600 "$(stat -c %a "$CONF")"
equal "config host" 10.0.0.5 "$(conf_get host)"; equal "config password" 'p w' "$(conf_get password)"
conf_set mode Host; conf_set hostport 6000
equal "conf_set keeps the join values" 10.0.0.5 "$(conf_get host)"; equal "conf_set mode" Host "$(conf_get mode)"
conf_set mode Join; equal "conf_set replaces" 1 "$(grep -c '^mode=' "$CONF")"

echo "== components.json drift =="
J=$TDIR/../scripts/components.json
if [ -f "$J" ]; then
  for v in "$CEMU_URL" "$CEMU_SHA" "$GP_URL" "$GP_SHA" "$MILKBAR_URL" "$MILKBAR_SHA" "$GAME_ID" "$UPDATE_ID" "$DLC_ID"; do
    check "components.json has $v" grep -qi -- "$v" "$J"
  done
else echo "scripts/components.json not found, skipped"; fi

echo "== dry run (Linux only) =="
if [ "$(uname -s)" = Linux ] && [ "$(uname -m)" = x86_64 ]; then
  mkdir -p "$D/save/user/80000001"
  H=$TMP/home; mkdir -p "$H/.local/share/Steam/steamapps/common/Proton 10.0/files/bin" "$H/.local/share/Steam/userdata/123/config"
  : > "$H/.local/share/Steam/steamapps/common/Proton 10.0/files/bin/wine64"; chmod +x "$H/.local/share/Steam/steamapps/common/Proton 10.0/files/bin/wine64"
  before=$(find "$H" | sort | md5sum)
  out=$(HOME=$H HYRULE_STEAM_ROOT=$H/.local/share/Steam HYRULE_STATE=$H/state HYRULE_NO_GUI=1 bash "$TDIR/install.sh" --dry-run --dump "$D" --dir "$H/hyrule" 2>&1); rc=$?
  equal "dry run exit code" 0 "$rc"
  case $out in *"Nothing was changed"*) ok ;; *) bad "dry run output: $out" ;; esac
  case $out in *"unpack the dedicated server"*) ok ;; *) bad "dry run does not mention the dedicated server: $out" ;; esac
  equal "dry run changes nothing" "$before" "$(find "$H" | sort | md5sum)"
else echo "not Linux x86_64, skipped"; fi

echo
echo "passed: $PASSES, failed: $FAILS"
[ "$FAILS" -eq 0 ]
