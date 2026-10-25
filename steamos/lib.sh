#!/usr/bin/env bash
# Shared helpers for the Hyrule Online SteamOS scripts. Sourced, never run.

# Dialogs need a desktop and zenity; otherwise everything falls back to the terminal.
have_gui() { [ "${HYRULE_NO_GUI:-0}" != 1 ] && [ -n "${DISPLAY:-}" ] && command -v zenity >/dev/null 2>&1; }

# Wine sees the Linux filesystem as drive Z:.
winpath() { local p=${1//\//\\}; printf 'Z:%s' "$p"; }

valid_port() { [[ $1 =~ ^[0-9]{1,5}$ ]] && [ "$((10#$1))" -ge 1 ] && [ "$((10#$1))" -le 65535 ]; }

valid_host() {
  local h=$1 o
  [ -n "$h" ] && [ "${#h}" -le 253 ] || return 1
  if [[ $h =~ ^[0-9.]+$ ]]; then
    [[ $h =~ ^([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})$ ]] || return 1
    for o in "${BASH_REMATCH[@]:1}"; do [ "$((10#$o))" -le 255 ] || return 1; done
    return 0
  fi
  [[ $h =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ ]] && [[ $h != *..* && $h != *-.* && $h != *.-* ]]
}

valid_name() { [[ $1 =~ ^[A-Za-z0-9_-]{1,16}$ ]]; }

# msg_box <info|warning|error> <text>
msg_box() {
  if have_gui; then zenity "--$1" --no-markup --width=460 --text "$2" 2>/dev/null || true
  else printf '%s\n' "$2" >&2; fi
}

# ask_yesno <text>: the answer defaults to no.
ask_yesno() {
  local a
  if have_gui; then zenity --question --no-markup --width=460 --text "$1" 2>/dev/null
  else read -r -p "$1 [y/N] " a || return 1; [[ $a =~ ^[Yy] ]]; fi
}

# ask_text <title> <text> [default] [hide]: prints the answer, fails when cancelled.
# In the terminal an empty answer keeps the default and "-" clears it.
ask_text() {
  local v
  if have_gui; then
    zenity --entry --title "$1" --text "$2" --entry-text "${3:-}" ${4:+--hide-text} --width=460 2>/dev/null
  else
    if [ -n "${4:-}" ]; then read -r -s -p "$2 (Enter keeps the saved one, - clears it): " v; echo >&2
    else read -r -p "$2 [${3:-}]: " v; fi || return 1
    [ "$v" = - ] && v= || v=${v:-${3:-}}
    printf '%s' "$v"
  fi
}

# ask_choice <title> <text> <default> <option>...: prints the chosen option, fails when cancelled.
# In the terminal a typed prefix is enough, in any case.
ask_choice() {
  local title=$1 text=$2 def=$3 v o args=()
  shift 3
  if have_gui; then
    for o; do if [ "$o" = "$def" ]; then args+=(TRUE "$o"); else args+=(FALSE "$o"); fi; done
    zenity --list --radiolist --hide-header --column= --column= --title "$title" --text "$text" --width=460 --height=260 "${args[@]}" 2>/dev/null
  else
    while :; do
      read -r -p "$text ($*) [$def]: " v || return 1
      v=${v:-$def}
      for o; do if [[ ${o,,} == "${v,,}"* ]]; then printf '%s' "$o"; return 0; fi; done
    done
  fi
}

# ask_dir <title> <default>: prints a folder path, fails when cancelled.
ask_dir() {
  local v
  if have_gui; then zenity --file-selection --directory --title "$1" --filename "${2%/}/" 2>/dev/null
  else read -r -p "$1 (folder path) [$2]: " v || return 1; printf '%s' "${v:-$2}"; fi
}

# A pulsing progress window (GUI only). progress always prints the line too and
# fails once the user has closed the window, which callers may treat as "cancel".
progress_start() {
  have_gui || return 0
  trap '' PIPE
  exec 9> >(zenity --progress --pulsate --auto-close --title "$1" --text "Starting..." --width=460 2>/dev/null)
  PROGRESS_ON=1
}
progress() {
  printf '%s\n' "$*"
  [ "${PROGRESS_ON:-0}" = 1 ] || return 0
  printf '# %s\n' "$*" >&9 2>/dev/null
}
progress_stop() { [ "${PROGRESS_ON:-0}" = 1 ] && exec 9>&-; PROGRESS_ON=0; return 0; }
