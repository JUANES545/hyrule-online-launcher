#!/usr/bin/env bash
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
if command -v shellcheck >/dev/null 2>&1; then
  for f in "$HERE"/*.sh; do check "shellcheck ${f##*/}" shellcheck -x -S warning "$f"; done
else echo "shellcheck not installed, skipped"; fi
check "no CRLF in scripts" bash -c '! grep -q "$(printf "\r")" "$@"' _ "$HERE"/*.sh "$HERE"/*.py

echo "== lib.sh =="
export HYRULE_NO_GUI=1
. "$HERE/lib.sh"
for h in 192.168.1.43 example.com my-host.lan 10.0.0.1 localhost; do check "host $h" valid_host "$h"; done
for h in "" 256.1.1.1 1.2.3 1.2.3.4.5 -bad.com bad-.com "a b" a..b 'a;b' '$(x)'; do refuse "host '$h'" valid_host "$h"; done
for p in 1 5050 65535 05050; do check "port $p" valid_port "$p"; done
for p in 0 65536 -1 abc "" 1.5 123456; do refuse "port '$p'" valid_port "$p"; done
for n in Link a my_name-1 1234567890123456; do check "name $n" valid_name "$n"; done
for n in "" 12345678901234567 "a b" 'x;y'; do refuse "name '$n'" valid_name "$n"; done
equal winpath 'Z:\home\deck\x y\Cemu.exe' "$(winpath /home/deck/x\ y/Cemu.exe)"

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
( . "$HERE/install.sh"; STATE=$TMP/state/state; state_done dir ) && ok || bad "state survives a new shell (resume)"
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

echo "== hyrule-online.sh =="
. "$HERE/hyrule-online.sh"
refuse "tcp pre-check on a closed port" tcp_ok 127.0.0.1 1
HERE_CONF=$TMP/conf; HERE=$TMP; CONF=$HERE_CONF
HOST=10.0.0.5 PORT=5050 NAME=Link PASS='p w'; conf_save
[ "$(uname -s)" != Linux ] || equal "config perms" 600 "$(stat -c %a "$CONF")"
equal "config host" 10.0.0.5 "$(conf_get host)"; equal "config password" 'p w' "$(conf_get password)"

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
  equal "dry run changes nothing" "$before" "$(find "$H" | sort | md5sum)"
else echo "not Linux x86_64, skipped"; fi

echo
echo "passed: $PASSES, failed: $FAILS"
[ "$FAILS" -eq 0 ]
