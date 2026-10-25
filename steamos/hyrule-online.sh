#!/usr/bin/env bash
# Hyrule Online - join a server from SteamOS. Started by the Steam shortcut (Gaming Mode) or by hand.
# Usage: hyrule-online.sh [host port [password] [name]]   join (dialogs for whatever is missing)
#        hyrule-online.sh --stop                           stop a running session
set -u
HERE=$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)
. "$HERE/lib.sh"

UNIT=hyrule-client
CONF="$HERE/join.conf"
LOG="$HERE/logs/client.log"

conf_get() { [ -f "$CONF" ] && sed -n "s/^$1=//p" "$CONF" | head -n1; }

conf_save() {
  ( umask 077
    printf 'host=%s\nport=%s\nname=%s\npassword=%s\n' "$HOST" "$PORT" "$NAME" "$PASS" > "$CONF.tmp" ) \
    && chmod 600 "$CONF.tmp" && mv "$CONF.tmp" "$CONF"
}

fatal() { msg_box error "$1"; exit 1; }

session_active() { systemctl --user is-active --quiet "$UNIT.service"; }

# Stops only what this launcher started: its systemd unit and the Wine processes of its own prefix.
stop_session() {
  systemctl --user stop "$UNIT.service" 2>/dev/null
  # shellcheck source=/dev/null
  ( . "$HERE/env.sh" && wineserver -k ) 2>/dev/null
  return 0
}

# ask_valid <var> <title> <text> <validator> <error text>
ask_valid() {
  local v
  while :; do
    v=$(ask_text "$2" "$3" "${!1}") || exit 0
    if "$4" "$v"; then printf -v "$1" '%s' "$v"; return 0; fi
    msg_box warning "$5"
  done
}

ask_values() {
  HOST=$(conf_get host); PORT=$(conf_get port); NAME=$(conf_get name); PASS=$(conf_get password)
  PORT=${PORT:-5050}; NAME=${NAME:-Link}
  ask_valid HOST "Hyrule Online" "Server address (IP or name)" valid_host "That is not a valid server address."
  ask_valid PORT "Hyrule Online" "Server port" valid_port "The port must be a number from 1 to 65535."
  PASS=$(ask_text "Hyrule Online" "Server password (leave empty if there is none)" "$PASS" hide) || exit 0
  ask_valid NAME "Hyrule Online" "Your player name (letters, numbers, _ and -, up to 16)" valid_name "Use 1 to 16 letters, numbers, _ or -."
}

# $1 and $2 are expanded by the inner bash, on purpose.
# shellcheck disable=SC2016
tcp_ok() { timeout 5 bash -c 'exec 3<>"/dev/tcp/$1/$2"' _ "$1" "$2" 2>/dev/null; }

start_session() {
  local var envargs=()
  # Same as the working setup: every join starts from an empty shader cache.
  rm -f "$HERE"/cemu/shaderCache/transferable/*.bin "$HERE"/cemu/shaderCache/precompiled/*.bin "$HERE"/cemu/shaderCache/driver/vk/*.bin
  mkdir -p "$HERE/logs" "$HERE/run"
  rm -f "$HERE/cemu/log.txt" "$LOG" "$HERE/logs/client.err"
  # The password only lives in this private file, never in a script or a command line of ours.
  ( umask 077
    { printf 'HOST=%q\nPORT=%q\nNAME=%q\nPASS=%q\n' "$HOST" "$PORT" "$NAME" "$PASS"
      printf 'CEMU_W=%q\nGAME_W=%q\nDLL_W=%q\n' "$(winpath "$HERE/cemu/Cemu.exe")" \
        "$(winpath "$HERE/game/code/U-King.rpx")" "$(winpath "$HERE/milkbar/InjectDLL.dll")"
    } > "$HERE/run/session.env" )
  {
    echo '#!/usr/bin/env bash'
    printf '. %q\n. %q\ncd %q || exit 1\n' "$HERE/env.sh" "$HERE/run/session.env" "$HERE/milkbar"
    cat <<'EOF'
export WINEDEBUG=err+all,fixme-all
args=(--ip "$HOST" --port "$PORT" --name "$NAME" --model Link:Link --cemu "$CEMU_W" --game "$GAME_W" --dll "$DLL_W" --timeout 300)
[ -n "$PASS" ] && args+=(--password "$PASS")
cmd=(wine64 MilkBar.CLI.exe "${args[@]}")
# Keep the device awake while playing; only when the inhibitor is actually allowed.
if command -v systemd-inhibit >/dev/null 2>&1 && systemd-inhibit --what=sleep true 2>/dev/null; then
  cmd=(systemd-inhibit --what=sleep --who="Hyrule Online" --why="Playing online" "${cmd[@]}")
fi
exec "${cmd[@]}"
EOF
  } > "$HERE/run/client-run.sh"
  # systemd-run starts from a clean environment, so Steam's overlay preload never reaches Wine.
  for var in DISPLAY XAUTHORITY WAYLAND_DISPLAY DBUS_SESSION_BUS_ADDRESS SDL_GAMECONTROLLERCONFIG; do
    [ -n "${!var:-}" ] && envargs+=("--setenv=$var=${!var}")
  done
  [ -n "${DISPLAY:-}" ] || envargs+=("--setenv=DISPLAY=:0")
  systemctl --user reset-failed "$UNIT.service" 2>/dev/null
  systemd-run --user --collect --unit="$UNIT" "${envargs[@]}" \
    -p StandardOutput=file:"$LOG" -p StandardError=file:"$HERE/logs/client.err" \
    bash "$HERE/run/client-run.sh" >/dev/null 2>&1
}

# Waits for the client log to say it joined. Cemu needs about a minute to get there.
wait_connect() {
  local t=0
  progress_start "Hyrule Online"
  while [ "$t" -lt 360 ]; do
    if grep -q 'SUCCESS' "$LOG" 2>/dev/null; then progress_stop; return 0; fi
    if grep -q '"Response":3' "$LOG" 2>/dev/null; then
      progress_stop; stop_session
      fatal "Wrong password. The server at $HOST:$PORT refused it (Response:3). Ask the host for the password and try again."
    fi
    session_active || break
    progress "Starting the game and joining $HOST:$PORT... ${t}s (about a minute)" || { progress_stop; stop_session; exit 0; }
    sleep 5; t=$((t + 5))
  done
  progress_stop
  grep -q 'SUCCESS' "$LOG" 2>/dev/null && return 0
  local why="The game did not finish joining within 6 minutes."
  if ! session_active; then
    if grep -q '"Response":3' "$LOG" 2>/dev/null; then why="Wrong password (Response:3)."
    elif ! grep -q '5/5' "$LOG" 2>/dev/null; then why="Cemu or the Milk Bar client could not start."
    else why="The server did not accept the connection."; fi
  fi
  stop_session
  fatal "$why
Logs: $HERE/logs/client.log, $HERE/logs/client.err and $HERE/cemu/log.txt"
}

main() {
  case "${1:-}" in
    --stop) stop_session; echo "Stopped."; exit 0 ;;
    -h|--help) sed -n '2,4p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
  esac
  [ $# -ne 1 ] || fatal "Usage: hyrule-online.sh host port [password] [name]"
  [ -f "$HERE/env.sh" ] && [ -f "$HERE/cemu/Cemu.exe" ] || fatal "Hyrule Online is not installed in $HERE. Run install.sh first."
  # shellcheck source=/dev/null
  ( . "$HERE/env.sh" && command -v wine64 >/dev/null ) \
    || fatal "The Proton folder used at install time is gone. Install Proton 10.0 from Steam again, or run install.sh once more."

  trap 'stop_session; exit 143' INT TERM HUP
  if session_active; then
    ask_yesno "A Hyrule Online session is already running. Stop it and start a new one?" || exit 0
    stop_session
  fi

  if [ $# -ge 2 ]; then
    HOST=$1; PORT=$2; PASS=${3:-}; NAME=${4:-$(conf_get name)}; NAME=${NAME:-Link}
  else
    ask_values
  fi
  valid_host "$HOST" || fatal "That is not a valid server address: $HOST"
  valid_port "$PORT" || fatal "The port must be a number from 1 to 65535, not: $PORT"
  valid_name "$NAME" || fatal "The player name must be 1 to 16 letters, numbers, _ or -: $NAME"
  [[ $PASS != *$'\n'* ]] || fatal "The password cannot contain a line break."
  PORT=$((10#$PORT))
  conf_save

  tcp_ok "$HOST" "$PORT" || fatal "Cannot reach $HOST:$PORT.
Check that the host has started the server, that you are on the same network or VPN, and that the address and port are right."
  start_session || fatal "Could not start the session. Is systemd available for your user (systemd-run --user)?"
  wait_connect
  while session_active; do sleep 3; done
  stop_session
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then main "$@"; fi
