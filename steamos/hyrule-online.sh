#!/usr/bin/env bash
# Hyrule Online - play, host or join from SteamOS. Started by the Steam shortcut (Gaming Mode) or by hand.
# Usage: hyrule-online.sh                                   window (launcher.py) or menu: Play on this device, Host, Join or Stop
#        hyrule-online.sh invite|host:port [password] [name]    join (an invite looks like hyrule://host:port?pw=PASSWORD)
#        hyrule-online.sh host port [password] [name]       join (dialogs for whatever is missing)
#        hyrule-online.sh --host [port [password]]          host here (saved port, password and rules) and join it
#        hyrule-online.sh --local                           private server on this device only (127.0.0.1) and join it
#        options for --host and --local: --no-play (server only), --rules PRESET|01234567, --desc TEXT, --name NAME
#        hyrule-online.sh --stop                            stop the server and the game
#        hyrule-online.sh --status                          one line: server=up|down client=up|down mode=... (see README)
#        hyrule-online.sh --check                           check the install without starting anything
#        hyrule-online.sh --presets                         list the world rule presets
#        hyrule-online.sh --no-gui                          with no other argument: the zenity menu instead of the window
#        hyrule-online.sh --version
set -u
HERE=$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)
. "$HERE/lib.sh"

UNIT=hyrule-client
SRV_UNIT=hyrule-server

# Without a save the first game is played alone and without the mod (the Windows launcher's requireSave switch is false by default).
# Ship 1 here and in install.sh, or run with HYRULE_REQUIRE_SAVE=1, to ask for a save again.
REQUIRE_SAVE=${HYRULE_REQUIRE_SAVE:-0}

# Same test as SaveGate on Windows: some slot under the account folder holds game_data.sav.
have_save() { [ -n "$(find -L "$HERE/cemu/mlc01/usr/save/00050000/101c9400/user/80000001" -name game_data.sav -print -quit 2>/dev/null)" ]; }
CONF="$HERE/join.conf"
LOG="$HERE/logs/client.log"
SRV_LOG="$HERE/logs/server.log"
# Same wording as the Windows launcher.
CHECKLIST="Check the invite, the password, that the host has a session running, and that its firewall allows the port."
M_LOCAL="Play on this device" M_HOST="Host a server" M_JOIN="Join a server" M_STOP="Stop"

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

# Same rules as ServerConnection.TryParse in the Windows launcher. Sets INV_HOST, INV_PORT and INV_PASS.
url_unescape() {
  local s=$1 out=''
  while [[ $s =~ ^([^%]*)%([0-9A-Fa-f]{2})(.*)$ ]]; do
    out+=${BASH_REMATCH[1]}$(printf '%b' "\\x${BASH_REMATCH[2]}")
    s=${BASH_REMATCH[3]}
  done
  printf '%s' "$out$s"
}

# parse_invite <hyrule://host:port?pw=PASSWORD | host:port | host>
parse_invite() {
  local t=${1#"${1%%[![:space:]]*}"} q='' re='(^|&)pw=([^&]*)'
  INV_HOST='' INV_PORT=5050 INV_PASS=''
  t=${t%"${t##*[![:space:]]}"}
  [ "${#t}" -le 200 ] || return 1
  [[ ${t,,} != hyrule://* ]] || t=${t:9}
  if [[ $t == *\?* ]]; then q=${t#*\?} t=${t%%\?*}; fi
  if [[ $q =~ $re ]]; then INV_PASS=$(url_unescape "${BASH_REMATCH[2]}"); fi
  while [[ $t == */ ]]; do t=${t%/}; done
  if [[ $t == *:* ]]; then
    valid_port "${t##*:}" || return 1
    INV_PORT=$((10#${t##*:})) t=${t%:*}
  fi
  valid_host "$t" || return 1
  INV_HOST=$t
}

ask_values() {
  local inv
  HOST=$(conf_get host); PORT=$(conf_get port); NAME=$(conf_get name); PASS=$(conf_get password)
  inv=${HOST:+$HOST:${PORT:-5050}}; NAME=${NAME:-Link}
  : "$inv"  # read by ask_valid through its name
  ask_valid inv "Hyrule Online" "Invite or address (paste the invite, or type host:port)" parse_invite "That is not a valid invite or address. Use hyrule://host:port?pw=PASSWORD or host:port."
  HOST=$INV_HOST PORT=$INV_PORT
  if [ -n "$INV_PASS" ]; then PASS=$INV_PASS
  else PASS=$(ask_text "Hyrule Online" "Server password (leave empty if there is none)" "$PASS" hide) || exit 0; fi
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

# Cemu has no controller until a profile exists. This one is the Windows launcher's Wii U Pro Controller on XInput pad 0, which under
# Wine is Steam Input's virtual X-Box 360 pad in Gaming Mode. A profile that is already there (the user's own) is never replaced.
ensure_pad_profile() {
  local f=$HERE/cemu/controllerProfiles/controller0.xml
  [ -e "$f" ] && return 0
  mkdir -p "${f%/*}" && cat > "$f.tmp" <<'EOF' && mv "$f.tmp" "$f"
<?xml version="1.0" encoding="UTF-8"?>
<emulated_controller>
  <type>Wii U Pro Controller</type><profile>Online P1</profile>
  <controller>
    <api>XInput</api><uuid>0</uuid><display_name>Controller 1</display_name>
    <rumble>0.65</rumble>
    <axis><deadzone>0.15</deadzone><range>1</range></axis>
    <rotation><deadzone>0.15</deadzone><range>1</range></rotation>
    <trigger><deadzone>0.05</deadzone><range>1</range></trigger>
    <mappings><entry><mapping>1</mapping><button>13</button></entry><entry><mapping>2</mapping><button>12</button></entry><entry><mapping>3</mapping><button>15</button></entry><entry><mapping>4</mapping><button>14</button></entry><entry><mapping>5</mapping><button>8</button></entry><entry><mapping>6</mapping><button>9</button></entry><entry><mapping>7</mapping><button>42</button></entry><entry><mapping>8</mapping><button>43</button></entry><entry><mapping>9</mapping><button>4</button></entry><entry><mapping>10</mapping><button>5</button></entry><entry><mapping>12</mapping><button>0</button></entry><entry><mapping>13</mapping><button>1</button></entry><entry><mapping>14</mapping><button>2</button></entry><entry><mapping>15</mapping><button>3</button></entry><entry><mapping>16</mapping><button>6</button></entry><entry><mapping>17</mapping><button>7</button></entry><entry><mapping>18</mapping><button>39</button></entry><entry><mapping>19</mapping><button>45</button></entry><entry><mapping>20</mapping><button>44</button></entry><entry><mapping>21</mapping><button>38</button></entry><entry><mapping>22</mapping><button>41</button></entry><entry><mapping>23</mapping><button>47</button></entry><entry><mapping>24</mapping><button>46</button></entry><entry><mapping>25</mapping><button>40</button></entry></mappings>
  </controller>
</emulated_controller>
EOF
}

start_session() {
  # Same as the working setup: every join starts from an empty shader cache.
  rm -f "$HERE"/cemu/shaderCache/transferable/*.bin "$HERE"/cemu/shaderCache/precompiled/*.bin "$HERE"/cemu/shaderCache/driver/vk/*.bin
  mkdir -p "$HERE/logs" "$HERE/run"
  rm -f "$HERE/cemu/log.txt" "$LOG" "$HERE/logs/client.err"
  # Best effort: a failure keeps the last Cemu settings and never blocks the start.
  [ -f "$HERE/display.py" ] && { python3 "$HERE/display.py" mod-on "$HERE" || echo "The mod packs were not switched on; Cemu keeps its last ones."; }
  [ -f "$HERE/display.py" ] && { python3 "$HERE/display.py" apply "$HERE" || echo "Display settings were not applied; Cemu keeps its last ones."; }
  ensure_pad_profile || echo "Could not write the Cemu controller profile; the pad may do nothing in game."
  write_client_run
  run_unit "$UNIT" "$HERE/run/client-run.sh" "$LOG" "$HERE/logs/client.err"
}

# Writes the run script of the first game: Cemu alone on the game, with no Milk Bar client and no server.
write_first_run() {
  mkdir -p "$HERE/run"
  {
    echo '#!/usr/bin/env bash'
    printf '. %q\ncd %q || exit 1\n' "$HERE/env.sh" "$HERE/cemu"
    printf 'export WINEDEBUG=err+all,fixme-all\ncmd=(wine64 Cemu.exe -g %q)\n' "$(winpath "$HERE/game/code/U-King.rpx")"
    inhibit_lines "Playing"
    echo 'exec "${cmd[@]}"'
  } > "$HERE/run/first-run.sh"
}

# The first game, before any save exists: no server, no mod. It ends when Cemu closes, and the next start sees the save.
first_game_session() {
  mkdir -p "$HERE/logs" "$HERE/run"
  rm -f "$HERE/cemu/log.txt" "$LOG" "$HERE/logs/client.err"
  if [ -f "$HERE/display.py" ]; then
    python3 "$HERE/display.py" mod-off "$HERE" || echo "The mod packs were not switched off; the game may not start."
    python3 "$HERE/display.py" apply "$HERE" || echo "Display settings were not applied; Cemu keeps its last ones."
  fi
  ensure_pad_profile || echo "Could not write the Cemu controller profile; the pad may do nothing in game."
  write_first_run
  set_mode first ''
  echo "No save yet: Cemu starts alone, without the mod. Start a new game, let it save, then close Cemu."
  run_unit "$UNIT" "$HERE/run/first-run.sh" "$LOG" "$HERE/logs/client.err" || fatal "Could not start the game. Is systemd available for your user (systemd-run --user)?"
  while session_active; do sleep 3; done
  stop_session
  if have_save; then echo "Your save was found. Joining and hosting are unlocked."; else echo "The game closed without a save yet."; fi
}

# Writes the run script of the game for $HOST, $PORT, $NAME and $PASS (nothing is started).
write_client_run() {
  mkdir -p "$HERE/run"
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
}

# Waits for the client log to say it joined. Cemu needs about a minute to get there.
wait_connect() {
  local t=0
  progress_start "Hyrule Online"
  while [ "$t" -lt 360 ]; do
    if grep -q 'SUCCESS' "$LOG" 2>/dev/null; then progress_stop; return 0; fi
    if grep -q '"Response":3' "$LOG" 2>/dev/null; then
      progress_stop; stop_client
      fatal "$NAME could not join: wrong password (Response:3). Ask the host for the password and try again."
    fi
    session_active || break
    progress "Starting the game and joining $HOST:$PORT... ${t}s (about a minute)" || { progress_stop; stop_client; exit 0; }
    sleep 5; t=$((t + 5))
  done
  progress_stop
  grep -q 'SUCCESS' "$LOG" 2>/dev/null && return 0
  local why="The game did not finish joining within 6 minutes. $CHECKLIST"
  if ! session_active; then
    if grep -q '"Response":3' "$LOG" 2>/dev/null; then why="Wrong password (Response:3)."
    elif ! grep -q '5/5' "$LOG" 2>/dev/null; then why="Cemu or the Milk Bar client could not start."
    else why="Could not join $HOST:$PORT. $CHECKLIST"; fi
  fi
  stop_client
  fatal "$why
Logs: $HERE/logs/client.log, $HERE/logs/client.err and $HERE/cemu/log.txt"
}

# ---- hosting ----
# World rules as 8 digits: Enemies Quests Koroks Towers Shrines Locations "Divine Beasts" Special.
# The first 7 are 1 when shared; Special is 0 none, 1 Hunter Vs Speedrunner, 2 Death Swap.
# Same presets and defaults as the Windows World page (ServerRules.cs); the default is Game Completion.
RULE_NAMES=(Enemies Quests Koroks Towers Shrines Locations "Divine Beasts")
SPECIALS=(None "Hunter Vs Speedrunner" "Death Swap")
PRESETS=("Game Completion" "Hunter Vs Speedrunner" "Any% Speedrun" "Hide n' seek" "Death Swap")
PRESET_BITS=(10111100 10011101 10011100 10000000 00000002)
DEFAULT_DESC="Explore Hyrule with Friends!"

# The preset with exactly these rules, else Custom (like the Windows launcher).
rules_name() {
  local i
  for i in "${!PRESETS[@]}"; do [ "${PRESET_BITS[i]}" = "$1" ] && { printf '%s' "${PRESETS[i]}"; return 0; }; done
  printf Custom
}

# rules_bits <preset name | 8 digits>: prints the 8 digits.
rules_bits() {
  local i
  if [[ $1 =~ ^[01]{7}[012]$ ]]; then printf '%s' "$1"; return 0; fi
  for i in "${!PRESETS[@]}"; do [ "${PRESETS[i],,}" = "${1,,}" ] && { printf '%s' "${PRESET_BITS[i]}"; return 0; }; done
  return 1
}

# write_server_ini <file> <ip> <port> <password> <description> <rules>: the same keys the Windows launcher writes.
write_server_ini() {
  local b=$6 k=(EnemySync QuestSync KorokSync TowerSync ShrineSync LocationSync DungeonSync) i
  ( umask 077
    {
      printf '[Connection]\nIP=%s\nPort=%s\nPassword=%s\n\n' "$2" "$3" "$4"
      printf '[ServerInformation]\nDescription=%s\n\n[Gamemode]\nDefaultGamemode=True\n\n' "$5"
      printf '[DefaultGamemode]\nName=%s\n' "$(rules_name "$b")"
      for i in "${!k[@]}"; do printf '%s=%s\n' "${k[i]}" "$([ "${b:i:1}" = 1 ] && echo True || echo False)"; done
      printf 'Special=%s\n' "${b:7:1}"
    } > "$1"
  )
}

# clean_text <text> <max>: no control characters, trimmed and cut, like the Windows launcher.
clean_text() {
  local s; s=$(printf '%s' "$1" | tr -d '[:cntrl:]')
  s=${s#"${s%%[![:space:]]*}"}; s=${s%"${s##*[![:space:]]}"}
  printf '%s' "${s:0:$2}"
}

valid_host_port() { valid_port "$1" && [ "$((10#$1))" -ge 1024 ]; }
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

# Only detects; the firewall is never changed. Prints the names of active ones (firewalld stays quiet when
# its default zone already allows the TCP port, as on SteamOS where 1024-65535 is open).
firewall_active() {
  local n out=''
  for n in ufw firewalld nftables; do
    systemctl is-active --quiet "$n" 2>/dev/null || continue
    [ "$n" = firewalld ] && firewall-cmd --query-port="$1/tcp" >/dev/null 2>&1 && continue
    out+="$n "
  done
  if [ -z "$out" ] && [ -n "$(nft list ruleset 2>/dev/null)" ]; then out='nftables '; fi
  printf '%s' "${out% }"
}

# invite_text <port> <password>
invite_text() {
  local ips lan vpn fw
  ips=$(ip -4 -o addr show scope global 2>/dev/null)
  lan=$(pick_ip lan <<<"$ips"); vpn=$(pick_ip vpn <<<"$ips"); fw=$(firewall_active "$1")
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
  write_server_ini "$HERE/server/ServerConfig.ini" "$SRV_IP" "$PORT" "$PASS" "$DESC" "$BITS" || return 1
  write_server_run
  run_unit "$SRV_UNIT" "$HERE/run/server-run.sh" "$SRV_LOG" "$HERE/logs/server.err"
}

write_server_run() {
  mkdir -p "$HERE/run"
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
    progress "Starting the server on $SRV_IP:$PORT$([ "$SRV_IP" != 127.0.0.1 ] || echo " (only this device can join)")... ${t}s" || { progress_stop; stop_session; exit 0; }
    sleep 2; t=$((t + 2))
  done
  progress_stop
  server_failed
}

# Picks the world rules (a preset, or Custom with a checklist) into BITS.
ask_rules() {
  local pick i out new=''
  pick=$(ask_choice "Hyrule Online" "World rules: what everyone on the server shares" "$(rules_name "$BITS")" "${PRESETS[@]}" Custom) || exit 0
  for i in "${!PRESETS[@]}"; do [ "${PRESETS[i]}" = "$pick" ] && BITS=${PRESET_BITS[i]}; done
  [ "$pick" = Custom ] || return 0
  local on=() args=() cur=''
  for i in "${!RULE_NAMES[@]}"; do
    if [ "${BITS:i:1}" = 1 ]; then on+=("${RULE_NAMES[i]}"); args+=(TRUE "${RULE_NAMES[i]}"); else args+=(FALSE "${RULE_NAMES[i]}"); fi
  done
  if have_gui; then
    out=$(zenity --list --checklist --hide-header --column= --column= --separator=, --title "Hyrule Online" \
      --text "Tick what everyone on the server shares" --width=460 --height=380 "${args[@]}" 2>/dev/null) || exit 0
  else
    cur=$(IFS=,; printf '%s' "${on[*]}")
    out=$(ask_text "Hyrule Online" "Shared rules, comma separated (${RULE_NAMES[*]})" "$cur") || exit 0
  fi
  out=${out//, /,}
  for i in "${!RULE_NAMES[@]}"; do if [[ ",${out,,}," == *",${RULE_NAMES[i],,},"* ]]; then new+="1"; else new+="0"; fi; done
  pick=$(ask_choice "Hyrule Online" "Special mode" "${SPECIALS[${BITS:7:1}]}" "${SPECIALS[@]}") || exit 0
  for i in "${!SPECIALS[@]}"; do [ "${SPECIALS[i]}" = "$pick" ] && BITS=$new$i; done
}

# server_session local|host [port [password]]: starts the server, then joins it from this device unless --no-play.
# local binds 127.0.0.1 only (one player, nobody else can join); host binds 0.0.0.0 and shows the invite.
server_session() {
  local kind=$1
  shift
  [ -f "$HERE/server/MBL.DedicatedServer.exe" ] || fatal "The dedicated server is not installed. Run $HERE/install.sh again to unpack it."
  BITS=$(conf_get rules); DESC=$(conf_get desc)
  [[ $BITS =~ ^[01]{7}[012]$ ]] || BITS=${PRESET_BITS[0]}
  DESC=$(clean_text "${DESC_ARG:-${DESC:-$DEFAULT_DESC}}" 80); DESC=${DESC:-$DEFAULT_DESC}
  SRV_IP=127.0.0.1 PORT=5050 PASS=''
  if [ -n "${RULES_ARG:-}" ]; then BITS=$(rules_bits "$RULES_ARG") || fatal "Unknown world rules: $RULES_ARG (use a preset name from --presets, or 8 digits)"; fi
  # No desktop and no terminal to ask in: the saved (or default) values are used.
  have_gui || [ -t 0 ] || ASK=0
  if [ "$kind" = host ]; then
    SRV_IP=0.0.0.0
    PORT=$(conf_get hostport); PASS=$(conf_get hostpass); PORT=${PORT:-5050}
    if [ "$ASK" = 1 ]; then
      ask_rules
      ask_valid PORT "Hyrule Online" "Port to host on (1024 to 65535)" valid_host_port "The port must be a number from 1024 to 65535."
      PASS=$(ask_text "Hyrule Online" "Server password (leave empty for none)" "$PASS" hide) || exit 0
      DESC=$(ask_text "Hyrule Online" "Server description" "$DESC") || exit 0
    elif [ $# -gt 0 ]; then PORT=$1 PASS=${2:-}; fi
    valid_host_port "$PORT" || fatal "The port must be a number from 1024 to 65535, not: $PORT"
    PORT=$((10#$PORT))
    PASS=$(clean_text "$PASS" 40)
    # The server reads its ini with MadMilkman.Ini, where ; starts a comment, so it would keep only the text before it.
    [[ $PASS != *\;* ]] || fatal "The server password cannot contain a semicolon (;): the server would cut it there. Use another password."
    DESC=$(clean_text "$DESC" 80); DESC=${DESC:-$DEFAULT_DESC}
    conf_set hostport "$PORT"; conf_set hostpass "$PASS"
  elif [ "$ASK" = 1 ]; then ask_rules; fi
  conf_set rules "$BITS"; conf_set desc "$DESC"

  set_mode "$kind" "$PORT"
  start_server || fatal "Could not start the server. Is systemd available for your user (systemd-run --user)?"
  wait_server
  if [ "$kind" = host ]; then show_invite "$PORT" "$PASS"
  else echo "Private server running on 127.0.0.1:$PORT. Only this device can join it."; fi
  if [ "$NOPLAY" = 1 ]; then echo "Server running. Stop it with: $HERE/hyrule-online.sh --stop"; return 0; fi

  HOST=127.0.0.1; NAME=${NAME_ARG:-$(conf_get name)}; NAME=${NAME:-Ally}
  start_session || fatal "Could not start the game. Is systemd available for your user (systemd-run --user)?"
  wait_connect
  echo "Joined your own server as $NAME. The server keeps running after you close the game; stop it with: $HERE/hyrule-online.sh --stop"
  while server_active; do sleep 3; done
  server_failed
}
join_session() {
  if [ $# -eq 0 ]; then ask_values
  elif [[ $1 == *:* ]] || [ $# -eq 1 ]; then
    parse_invite "$1" || fatal "That is not a valid invite or address: $1
Use hyrule://host:port?pw=PASSWORD, host:port, or: host port [password] [name]"
    HOST=$INV_HOST PORT=$INV_PORT PASS=${2:-$INV_PASS}; NAME=${3:-${NAME_ARG:-$(conf_get name)}}; NAME=${NAME:-Link}
  else
    HOST=$1 PORT=$2 PASS=${3:-}; NAME=${4:-${NAME_ARG:-$(conf_get name)}}; NAME=${NAME:-Link}
  fi
  valid_host "$HOST" || fatal "That is not a valid server address: $HOST"
  valid_port "$PORT" || fatal "The port must be a number from 1 to 65535, not: $PORT"
  valid_name "$NAME" || fatal "The player name must be 1 to 16 letters, numbers, _ or -: $NAME"
  [[ $PASS != *$'\n'* ]] || fatal "The password cannot contain a line break."
  PORT=$((10#$PORT))
  conf_save
  set_mode join "$PORT"

  start_session || fatal "Could not start the session. Is systemd available for your user (systemd-run --user)?"
  wait_connect
  echo "Joined $HOST:$PORT as $NAME. Close the game to leave."
  while session_active; do sleep 3; done
  stop_session
  echo "Session ended."
}

# ---- for the window (launcher.py) and scripts ----
set_mode() { mkdir -p "$HERE/run" && printf '%s %s\n' "$1" "$2" > "$HERE/run/mode"; }

# One line of key=value pairs, split on spaces and then on the first "=" (no value has a space):
# version mode(idle|first|local|host|join) server(up|down) client(up|down) joined(yes|no) port [host (join only)] [invite] [invite_vpn] save(yes|no|required)
status_line() {
  local kind='' port='' srv=down cli=down joined=no mode=idle ips lan vpn pw
  server_active && srv=up
  session_active && cli=up
  [ "$cli" = up ] && grep -q SUCCESS "$LOG" 2>/dev/null && joined=yes
  if [ -f "$HERE/run/mode" ]; then read -r kind port < "$HERE/run/mode"; fi
  if [ "$srv$cli" != downdown ]; then mode=${kind:-unknown}; else port=; fi
  printf 'version=%s mode=%s server=%s client=%s joined=%s port=%s' "$(app_version)" "$mode" "$srv" "$cli" "$joined" "$port"
  [ "$mode" != join ] || printf ' host=%s' "$(conf_get host)"
  if [ "$srv" = up ] && [ "$kind" = host ]; then
    ips=$(ip -4 -o addr show scope global 2>/dev/null)
    lan=$(pick_ip lan <<<"$ips"); vpn=$(pick_ip vpn <<<"$ips"); pw=$(conf_get hostpass)
    [ -z "${lan:-$vpn}" ] || printf ' invite=%s' "$(invite_url "${lan:-$vpn}" "$port" "$pw")"
    [ -z "$vpn" ] || printf ' invite_vpn=%s' "$(invite_url "$vpn" "$port" "$pw")"
  fi
  if have_save; then printf ' save=yes'; elif [ "$REQUIRE_SAVE" = 1 ]; then printf ' save=required'; else printf ' save=no'; fi
  echo
}

# The window needs a display, python3 with tkinter and launcher.py next to this script.
have_window() {
  [ "${HYRULE_NO_GUI:-0}" != 1 ] && [ -n "${DISPLAY:-}" ] && [ -f "$HERE/launcher.py" ] && python3 -c 'import tkinter' 2>/dev/null
}

# Checks the install and writes the run scripts to prove they are fine, but starts nothing. Exit 1 when something is missing.
check_install() {
  local f bad=0
  for f in env.sh cemu/Cemu.exe game/code/U-King.rpx milkbar/InjectDLL.dll milkbar/MilkBar.CLI.exe server/MBL.DedicatedServer.exe; do
    if [ -f "$HERE/$f" ]; then echo "ok: $f"; else echo "missing: $f"; bad=1; fi
  done
  # shellcheck source=/dev/null
  if ( . "$HERE/env.sh" && wine64 --version ) >/dev/null 2>&1; then echo "ok: wine64 of the install"; else echo "missing: wine64 (the Proton folder of the install)"; bad=1; fi
  if systemctl --user show-environment >/dev/null 2>&1; then echo "ok: systemd --user"; else echo "missing: systemd --user"; bad=1; fi
  if session_active || server_active; then echo "skipped: run scripts (a session is running)"
  else
    HOST=127.0.0.1 PORT=5050 NAME=Check PASS='' write_client_run && write_server_run && write_first_run \
      && bash -n "$HERE/run/client-run.sh" && bash -n "$HERE/run/server-run.sh" && bash -n "$HERE/run/first-run.sh" \
      && echo "ok: run scripts" || { echo "missing: valid run scripts"; bad=1; }
  fi
  if have_save; then echo "ok: save"
  elif [ "$REQUIRE_SAVE" = 1 ]; then echo "missing: a save (a brand new game hangs in Cemu)"; bad=1
  else echo "info: no save yet, the first game starts without the mod"; fi
  if have_window; then echo "info: window available (launcher.py)"; else echo "info: no window, the zenity menu is used"; fi
  return $bad
}

main() {
  local a args=() mode=$M_JOIN rc
  NOPLAY=0 ASK=0 NOGUI=0 RULES_ARG='' DESC_ARG='' NAME_ARG=''
  while [ $# -gt 0 ]; do
    case $1 in
      --no-play) NOPLAY=1 ;;
      --no-gui) NOGUI=1 ;;
      --rules|--desc|--name)
        [ $# -ge 2 ] || fatal "$1 needs a value."
        case $1 in --rules) RULES_ARG=$2 ;; --desc) DESC_ARG=$2 ;; *) NAME_ARG=$2 ;; esac
        shift ;;
      *) args+=("$1") ;;
    esac
    shift
  done
  set -- "${args[@]+"${args[@]}"}"
  case "${1:-}" in
    --stop) stop_session; echo "Stopped."; exit 0 ;;
    --status) status_line; exit 0 ;;
    --check) check_install; exit ;;
    --presets) for a in "${!PRESETS[@]}"; do echo "${PRESET_BITS[a]} ${PRESETS[a]}"; done; exit 0 ;;
    --version) echo "Hyrule Online $(app_version)"; exit 0 ;;
    -h|--help) sed -n '2,14p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
  esac
  [ -z "$NAME_ARG" ] || valid_name "$NAME_ARG" || fatal "The player name must be 1 to 16 letters, numbers, _ or -: $NAME_ARG"
  [ -f "$HERE/env.sh" ] && [ -f "$HERE/cemu/Cemu.exe" ] || fatal "Hyrule Online is not installed in $HERE. Run install.sh first."
  # shellcheck source=/dev/null
  ( . "$HERE/env.sh" && command -v wine64 >/dev/null ) \
    || fatal "The Proton folder used at install time is gone. Install Proton 10.0 from Steam again, or run install.sh once more."
  echo "Hyrule Online $(app_version)"

  [ -z "$NAME_ARG" ] || conf_set name "$NAME_ARG"

  trap 'stop_session; exit 143' INT TERM HUP
  if [ $# -eq 0 ] && [ "$NOGUI" != 1 ] && have_window; then
    # The window only drives this script; closing it leaves a running server alone. If it fails to start, the menu is used.
    mkdir -p "$HERE/logs"
    python3 "$HERE/launcher.py" 2>>"$HERE/logs/launcher.err"; rc=$?
    [ "$rc" != 0 ] || exit 0
    echo "The window stopped with an error (code $rc, see $HERE/logs/launcher.err). Using the menu."
  fi
  if [ $# -eq 0 ]; then
    ASK=1
    mode=$(conf_get mode)
    case $mode in Host) mode=$M_HOST ;; Join|"") mode=$M_JOIN ;; esac
    mode=$(ask_choice "Hyrule Online $(app_version)" "What do you want to do?" "$mode" "$M_LOCAL" "$M_HOST" "$M_JOIN" "$M_STOP") || exit 0
    conf_set mode "$mode"
  elif [ "$1" = --host ]; then mode=$M_HOST; shift
  elif [ "$1" = --local ]; then mode=$M_LOCAL; shift
  fi
  if [ "$mode" = "$M_STOP" ]; then stop_session; echo "Stopped."; exit 0; fi
  if session_active || server_active; then
    have_gui || [ -t 0 ] || fatal "A Hyrule Online session is already running. Stop it first: $HERE/hyrule-online.sh --stop"
    ask_yesno "A Hyrule Online session is already running. Stop it and start a new one?" || exit 0
    stop_session
  fi
  if [ "$NOPLAY" != 1 ] && ! have_save; then
    [ "$REQUIRE_SAVE" != 1 ] || fatal "A save is needed first: a brand new game hangs in Cemu. Run install.sh again with a dump that has your save."
    first_game_session; exit 0
  fi
  case $mode in
    "$M_LOCAL") server_session local ;;
    "$M_HOST") server_session host "$@" ;;
    *) join_session "$@" ;;
  esac
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then main "$@"; fi