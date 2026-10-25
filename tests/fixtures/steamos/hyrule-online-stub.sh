#!/usr/bin/env bash
# Stand-in for steamos/hyrule-online.sh in tests/steamos_gui_check.py: it records the calls and never starts a server or the game.
dir=${HYRULE_STUB_DIR:?set HYRULE_STUB_DIR}
echo "$*" >> "$dir/calls.log"
case ${1:-} in
  --presets)
    printf '%s\n' "10111100 Game Completion" "10011101 Hunter Vs Speedrunner" "10011100 Any% Speedrun" "10000000 Hide n' seek" "00000002 Death Swap" ;;
  --version) echo "Hyrule Online stub" ;;
  --check) echo "ok: stub" ;;
  --status)
    if [ -f "$dir/session.pid" ]; then
      echo "version=0.0.0 mode=host server=up client=down joined=no port=5050 host=192.168.1.20 invite=hyrule://192.168.1.20:5050 invite_vpn=hyrule://100.64.0.5:5050"
    else
      echo "version=0.0.0 mode=local server=down client=down joined=no port=5050"
    fi ;;
  --stop)
    if [ -f "$dir/session.pid" ]; then kill "$(cat "$dir/session.pid")" 2>/dev/null; rm -f "$dir/session.pid"; fi
    echo "Stopped." ;;
  *)
    echo $$ > "$dir/session.pid"
    echo "Stub session started."
    exec sleep 60 ;;
esac
