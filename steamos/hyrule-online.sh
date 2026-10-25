#!/usr/bin/env bash
# Hyrule Online - join or host a server from SteamOS. Started by the Steam shortcut (Gaming Mode) or by hand.
# Usage: hyrule-online.sh                                   ask Join or Host
#        hyrule-online.sh host port [password] [name]       join (dialogs for whatever is missing)
#        hyrule-online.sh --host [port [password]] [--no-play]   host here (port 5050) and join it; --no-play only hosts
#        hyrule-online.sh --stop                            stop the server and the game
set -u
HERE=$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)
. "$HERE/lib.sh"

UNIT=hyrule-client
SRV_UNIT=hyrule-server
CONF="$HERE/join.conf"
LOG="$HERE/logs/client.log"
SRV_LOG="$HERE/logs/server.log"

conf_get() { [ -f "$CONF" ] && sed -n "s/^$1=//p" "$CONF" | head -n1; }

conf_set() {
  ( umask 077
    { grep -v "^$1=" "$CONF" 2>/dev/null; printf '%s=%s\n' "$1" "$2"; } > "$CONF.tmp" ) \
    && chmod 600 "$CONF.tmp" && mv "$CONF.tmp" "$CONF"
}

conf_save() { conf_set host "$HOST" && conf_set port "$PORT" && conf_set name "$NAME" && conf_set password "$PASS"; }

fatal() { msg_box error "$1"; exit 1; }

session_active() { systemctl --user is-active --quiet "$UNIT.service"; }
server_active() { systemctl --user is-active --quiet "$SRV_UNIT.service"; }

# Stops only what this launcher started: its systemd units and the Wine processes of its own prefix.
stop_session() {
  # shellcheck source=/dev/null
  ( . "$HERE/env.sh" && wineserver -k ) 2>/dev/null
  systemctl --user stop "$UNIT.service" "$SRV_UNIT.service" 2>/dev/null
  return 0
}

# Stops the game only; the Wine processes are left alone while the server uses the same prefix.
stop_client() {
  # shellcheck source=/dev/null
  server_active || ( . "$HERE/env.sh" && wineserver -k ) 2>/dev/null
  systemctl --user stop "$UNIT.service" 2>/dev/null
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

# Lines for a run script: keep the device awake, but only when the inhibitor is actually allowed.
inhibit_lines() {
  cat <<EOF
if command -v systemd-inhibit >/dev/null 2>&1 && systemd-inhibit --what=sleep true 2>/dev/null; then
  cmd=(systemd-inhibit --what=sleep --who="Hyrule Online" --why="$1" "\${cmd[@]}")
fi
EOF
}

# run_unit <unit> <script> <stdout file> <stderr file>
run_unit() {
  local var envargs=()
  # systemd-run starts from a clean environment, so Steam's overlay preload never reaches Wine.
  for var in DISPLAY XAUTHORITY WAYLAND_DISPLAY DBUS_SESSION_BUS_ADDRESS SDL_GAMECONTROLLERCONFIG; do
    [ -n "${!var:-}" ] && envargs+=("--setenv=$var=${!var}")
  done
  [ -n "${DISPLAY:-}" ] || envargs+=("--setenv=DISPLAY=:0")
  systemctl --user reset-failed "$1.service" 2>/dev/null
  systemd-run --user --collect --unit="$1" -p TimeoutStopSec=10 "${envargs[@]}" \
    -p StandardOutput=file:"$3" -p StandardError=file:"$4" bash "$2" >/dev/null 2>&1
}

# No TCP pre-check on purpose: the Milk Bar dedicated server crashes on a connection that sends nothing.

start_session() {
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
EOF
    inhibit_lines "Playing online"
    echo 'exec "${cmd[@]}"'
  } > "$HERE/run/client-run.sh"
  run_unit "$UNIT" "$HERE/run/client-run.sh" "$LOG" "$HERE/logs/client.err"
}

# Waits for the client log to say it joined. Cemu needs about a minute to get there.
wait_connect() {
  local t=0
  progress_start "Hyrule Online"
  while [ "$t" -lt 360 ]; do
    if grep -q 'SUCCESS' "$LOG" 2>/dev/null; then progress_stop; return 0; fi
    if grep -q '"Response":3' "$LOG" 2>/dev/null; then
      progress_stop; stop_client
      fatal "Wrong password. The server at $HOST:$PORT refused it (Response:3). Ask the host for the password and try again."
    fi
    session_active || break
    progress "Starting the game and joining $HOST:$PORT... ${t}s (about a minute)" || { progress_stop; stop_client; exit 0; }
    sleep 5; t=$((t + 5))
  done
  progress_stop
  grep -q 'SUCCESS' "$LOG" 2>/dev/null && return 0
  local why="The game did not finish joining within 6 minutes."
  if ! session_active; then
    if grep -q '"Response":3' "$LOG" 2>/dev/null; then why="Wrong password (Response:3)."
    elif ! grep -q '5/5' "$LOG" 2>/dev/null; then why="Cemu or the Milk Bar client could not start."
    else why="Could not join $HOST:$PORT. Check that the host has started the server, that you are on the same network or VPN, and that the address and port are right."; fi
  fi
  stop_client
  fatal "$why
Logs: $HERE/logs/client.log, $HERE/logs/client.err and $HERE/cemu/log.txt"
}

# ---- hosting ----
write_server_ini() {
  ( umask 077
    cat > "$1" <<EOF
[Connection]
IP=0.0.0.0
Port=$2
Password=$3

[ServerInformation]
Description=Hyrule Online on SteamOS

[Gamemode]
DefaultGamemode=True

[DefaultGamemode]
Name=Custom
EnemySync=True
QuestSync=False
KorokSync=True
TowerSync=True
ShrineSync=True
LocationSync=True
DungeonSync=False
Special=0
EOF
  )
}

# Percent-encodes every byte except A-Z a-z 0-9 . _ ~ - (the same text Uri.EscapeDataString makes).
url_escape() {
  local LC_ALL=C s=$1 c i out=''
  for ((i = 0; i < ${#s}; i++)); do
    c=${s:i:1}
    case $c in [A-Za-z0-9._~-]) out+=$c ;; *) out+=$(printf '%%%02X' "'$c") ;; esac
  done
  printf '%s' "$out"
}

# invite_url <host> <port> [password], the format the Windows launcher uses.
invite_url() { printf 'hyrule://%s:%s%s' "$1" "$2" "${3:+?pw=$(url_escape "$3")}"; }

# pick_ip lan|vpn: reads `ip -4 -o addr` lines and prints the best address of that kind (192.168, then 10, then 172.16/12).
pick_ip() {
  local want=$1 _ ifc cidr ip kind rank best='' bestrank=9
  while read -r _ ifc _ cidr _; do
    ip=${cidr%/*} kind=lan
    case $ifc in lo|docker*|br-*|veth*|virbr*) continue ;; tailscale*|wg*|tun*|tap*|zt*) kind=vpn ;; esac
    [[ $ip =~ ^100\.(6[4-9]|[7-9][0-9]|1[01][0-9]|12[0-7])\. ]] && kind=vpn
    [ "$kind" = "$want" ] || continue
    case $ip in 127.*|169.254.*) continue ;; 192.168.*) rank=1 ;; 10.*) rank=2 ;; 172.1[6-9].*|172.2?.*|172.3[01].*) rank=3 ;; *) rank=4 ;; esac
    if [ "$rank" -lt "$bestrank" ]; then best=$ip bestrank=$rank; fi
  done
  [ -n "$best" ] && printf '%s' "$best"
}

# Only detects; the firewall is never changed. Prints the names of active ones.
firewall_active() {
  local n out=''
  for n in ufw firewalld nftables; do systemctl is-active --quiet "$n" 2>/dev/null && out+="$n "; done
  if [ -z "$out" ] && [ -n "$(nft list ruleset 2>/dev/null)" ]; then out='nftables '; fi
  printf '%s' "${out% }"
}

# invite_text <port> <password>
invite_text() {
  local ips lan vpn fw
  ips=$(ip -4 -o addr show scope global 2>/dev/null)
  lan=$(pick_ip lan <<<"$ips"); vpn=$(pick_ip vpn <<<"$ips"); fw=$(firewall_active)
  echo "The Hyrule Online server is running on port $1."
  if [ -n "$lan" ]; then printf '\nInvite (same network):\n%s\n' "$(invite_url "$lan" "$1" "$2")"; fi
  if [ -n "$vpn" ]; then printf '\nInvite (Tailscale or other VPN):\n%s\n' "$(invite_url "$vpn" "$1" "$2")"; fi
  [ -n "$lan$vpn" ] || printf '\nNo network address was found, so only this device can join right now.\n'
  [ -z "$fw" ] || printf '\nNote: %s is active and may block port %s (TCP) for other devices. This launcher does not change it.\n' "$fw" "$1"
  printf '\nOn the Windows PC: open the launcher, choose Join and paste the invite.\n'
}

show_invite() {
  local text; text=$(invite_text "$1" "$2")
  printf '%s\n' "$text"
  if have_gui; then printf '%s\n' "$text" | zenity --text-info --title "Hyrule Online server" --width=520 --height=340 2>/dev/null; fi
  return 0
}

start_server() {
  mkdir -p "$HERE/logs" "$HERE/run"
  rm -f "$SRV_LOG" "$HERE/logs/server.err" "$HERE/server/LatestLog.txt"
  write_server_ini "$HERE/server/ServerConfig.ini" "$PORT" "$PASS" || return 1
  {
    echo '#!/usr/bin/env bash'
    printf '. %q\ncd %q || exit 1\n' "$HERE/env.sh" "$HERE/server"
    cat <<'EOF'
export WINEDEBUG=err+all,fixme-all
# The server is built for .NET 6 and the prefix only has .NET 8.
export DOTNET_ROLL_FORWARD=Major
cmd=(wine64 MBL.DedicatedServer.exe)
EOF
    inhibit_lines "Hosting online"
    # The server spins at 100% CPU once its stdin is closed, so it gets one that stays open.
    echo '"${cmd[@]}" < <(exec tail -f /dev/null)'
  } > "$HERE/run/server-run.sh"
  run_unit "$SRV_UNIT" "$HERE/run/server-run.sh" "$SRV_LOG" "$HERE/logs/server.err"
}

server_failed() {
  stop_session
  fatal "The Hyrule Online server stopped.
$(tail -n 5 "$SRV_LOG" 2>/dev/null)
Logs: $SRV_LOG and $HERE/logs/server.err"
}

wait_server() {
  local t=0
  progress_start "Hyrule Online"
  while [ "$t" -lt 90 ]; do
    if grep -q 'Server opened' "$SRV_LOG" 2>/dev/null; then progress_stop; return 0; fi
    server_active || { progress_stop; server_failed; }
    progress "Starting the server on port $PORT... ${t}s" || { progress_stop; stop_session; exit 0; }
    sleep 2; t=$((t + 2))
  done
  progress_stop
  server_failed
}

host_session() {
  [ -f "$HERE/server/MBL.DedicatedServer.exe" ] || fatal "The dedicated server is not installed. Run $HERE/install.sh again to unpack it."
  if [ "$ASK" = 0 ]; then PORT=${1:-5050} PASS=${2:-}
  else
    PORT=$(conf_get hostport); PASS=$(conf_get hostpass); PORT=${PORT:-5050}
    ask_valid PORT "Hyrule Online" "Port to host on" valid_port "The port must be a number from 1 to 65535."
    PASS=$(ask_text "Hyrule Online" "Server password (leave empty for none)" "$PASS" hide) || exit 0
  fi
  valid_port "$PORT" || fatal "The port must be a number from 1 to 65535, not: $PORT"
  [[ $PASS != *$'\n'* ]] || fatal "The password cannot contain a line break."
  PORT=$((10#$PORT))
  conf_set hostport "$PORT"; conf_set hostpass "$PASS"

  start_server || fatal "Could not start the server. Is systemd available for your user (systemd-run --user)?"
  wait_server
  show_invite "$PORT" "$PASS"
  if [ "$NOPLAY" = 1 ]; then echo "Server running. Stop it with: $HERE/hyrule-online.sh --stop"; return 0; fi

  HOST=127.0.0.1; NAME=$(conf_get name); NAME=${NAME:-Ally}
  start_session || fatal "Could not start the game. Is systemd available for your user (systemd-run --user)?"
  wait_connect
  echo "Joined your own server as $NAME. The server keeps running after you close the game; stop it with: $HERE/hyrule-online.sh --stop"
  while server_active; do sleep 3; done
  server_failed
}

join_session() {
  [ $# -ne 1 ] || fatal "Usage: hyrule-online.sh host port [password] [name]"
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

  start_session || fatal "Could not start the session. Is systemd available for your user (systemd-run --user)?"
  wait_connect
  echo "Joined $HOST:$PORT as $NAME. Close the game to leave."
  while session_active; do sleep 3; done
  stop_session
  echo "Session ended."
}

main() {
  local a args=() mode=Join
  NOPLAY=0 ASK=0
  for a in "$@"; do if [ "$a" = --no-play ]; then NOPLAY=1; else args+=("$a"); fi; done
  set -- "${args[@]+"${args[@]}"}"
  case "${1:-}" in
    --stop) stop_session; echo "Stopped."; exit 0 ;;
    -h|--help) sed -n '2,6p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
  esac
  [ -f "$HERE/env.sh" ] && [ -f "$HERE/cemu/Cemu.exe" ] || fatal "Hyrule Online is not installed in $HERE. Run install.sh first."
  # shellcheck source=/dev/null
  ( . "$HERE/env.sh" && command -v wine64 >/dev/null ) \
    || fatal "The Proton folder used at install time is gone. Install Proton 10.0 from Steam again, or run install.sh once more."

  trap 'stop_session; exit 143' INT TERM HUP
  if session_active || server_active; then
    ask_yesno "A Hyrule Online session is already running. Stop it and start a new one?" || exit 0
    stop_session
  fi

  if [ $# -eq 0 ]; then
    mode=$(conf_get mode)
    mode=$(ask_choice "Hyrule Online" "Join a server or host one on this device?" "${mode:-Join}" Join Host) || exit 0
    conf_set mode "$mode"; ASK=1
  elif [ "$1" = --host ]; then mode=Host; shift
  fi
  if [ "$mode" = Host ]; then host_session "$@"; else join_session "$@"; fi
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then main "$@"; fi