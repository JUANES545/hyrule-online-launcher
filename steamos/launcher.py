#!/usr/bin/env python3
"""Graphical launcher for Hyrule Online on SteamOS (tkinter only), laid out like the Windows launcher (src/Launcher).
It never talks to a game server: everything goes through hyrule-online.sh (see Cli); join.conf (written only by that script)
is read to prefill the forms and launcher.conf keeps the launcher's own choices (language, last page, last mode).
Texts are the English ones of Lang.cs; strings.json carries the Spanish and French rows, keyed by the English text (see tr)."""
import json
import os
import re
import subprocess
import sys
import threading
import unicodedata
from urllib.parse import quote, unquote

try:
    import tkinter as tk
    import tkinter.font as tkfont
except ImportError:  # the logic below stays importable (tests) without Tk
    tk = tkfont = None

HERE = os.path.dirname(os.path.realpath(__file__))
LOCAL, HOST, JOIN = "Local", "Host", "Join"
TABS = ["Play", "Online", "World", "Language", "Details"]  # the Windows order without Graphics (nothing to set on the Ally)
LANGUAGES = [("en", "English"), ("es", "Español"), ("fr", "Français")]
NEUTRAL = ("Hyrule Online", "THE LEGEND OF ZELDA", "BREATH OF THE WILD", "LB", "RB", "Hunter vs Speedrunner", "Death Swap")  # read the same everywhere
DEFAULT_PORT, DEFAULT_DESC = 5050, "Explore Hyrule with Friends!"
# The limits of ServerRules.cs and OnlineUi.cs; the script enforces the same ones.
LIMITS = {"port_low": 1024, "port_high": 65535, "host_password": 40, "description": 80, "host_address": 100,
          "join_address": 200, "join_password": 40, "name": 16}
RULE_NAMES = ["Enemies", "Quests", "Koroks", "Towers", "Shrines", "Locations", "Divine Beasts"]
SPECIALS = ["No special mode", "Hunter vs Speedrunner", "Death Swap"]
PRESETS = {"Game Completion": "10111100", "Hunter Vs Speedrunner": "10011101", "Any% Speedrun": "10011100",  # only if `--presets` fails
           "Hide n' seek": "10000000", "Death Swap": "00000002"}
RECOMMENDED = ("Game Completion", "10111100")
NAME_RE = re.compile(r"[A-Za-z0-9_-]{1,16}")


def _k(english):
    """Marks an English text that the GUI looks up with tr() later; tests scan for it."""
    return english


TAB_LABELS = {"Play": _k("PLAY"), "Online": _k("ONLINE"), "World": _k("WORLD"), "Language": _k("LANGUAGE"), "Details": _k("DETAILS")}
MODE_WORDS = {LOCAL: _k("Play on this device"), HOST: _k("Host a server"), JOIN: _k("Join a server")}  # as in hyrule-online.sh
MODES = [(LOCAL, "Play on this device"), (HOST, "Host a server"), (JOIN, "Join a server")]
MODE_INFO = {LOCAL: _k("The server only accepts players from this device."),
             HOST: _k("Friends who run this launcher can join this device. The world rules are the ones on the World page."),
             JOIN: _k("One player on this device joins a friend's server. The friend sets the rules.")}
DEFAULT_HINT = _k("Hover an option, or move the focus to it, to see what it does.")
CHECKLIST = _k("Check the invite, the password, that the host has a session running, and that its firewall allows the port.")
RULE_TIPS = [_k("Keeps enemies in sync: what one player defeats is defeated for both."), _k("Shares quest progress between the players."),
             _k("Shares the Korok seeds found by each player."), _k("Shares activated towers and the map they reveal."),
             _k("Shares the shrines either player has completed."), _k("Shares the map locations either player has discovered."),
             _k("Shares Divine Beast progress between the players.")]
PRESET_BLURBS = {
    "Game Completion": _k("For playing the whole game together. Defeated enemies, Korok seeds, towers, shrines and map locations are shared; quests and Divine Beasts stay separate for each player."),
    "Hunter Vs Speedrunner": _k("A chase game: the speedrunner races through the game while the hunters try to stop them. What one player clears, activates or discovers counts for both."),
    "Any% Speedrun": _k("Built for speedruns: what one player defeats, activates or discovers counts for both, except quests, Korok seeds and Divine Beasts."),
    "Hide n' seek": _k("Only defeated enemies are shared. Everything else stays separate for each player, which suits hide and seek in the same world."),
    "Death Swap": _k("A game mode where the players are swapped around the world at intervals. Nothing else is shared."),
    "Custom": _k("Your own mix: choose below what the players share.")}
SPECIAL_BLURBS = [_k("Normal co-op: only the rules above decide what the players share."),
                  _k("Adds a chase: one player speedruns while the others hunt them. Best with the Hunter Vs Speedrunner preset."),
                  _k("Adds Death Swap: the players are swapped around the world at intervals. Best with the Death Swap preset.")]
HELP_SECTIONS = [
    _k("SESSION: Start session opens the server and the game. When the game reaches its main menu, press Continue in its window (about a minute). "
       "This device plays one local player. If you change the name, the online settings or the world rules while a session runs, Start becomes "
       "Restart session. The launcher language applies at once."),
    _k("ONLINE: Choose how this device takes part. Play on this device keeps the server private. Host a server opens it to friends: set the port, "
       "password and description, then copy the invite. Join a server connects this device to a friend's server with their invite. Changes apply "
       "at the next session start."),
    _k("WORLD: World rules choose what everyone on the server shares (enemies, quests, koroks, towers, shrines, locations, Divine Beasts) and apply "
       "when the server starts. When you join a friend's server their rules apply and these are locked. Use recommended rules selects Game Completion."),
    _k("LANGUAGE: The launcher language changes at once."),
    _k("MOVING AROUND: The D-pad, the arrow keys or Tab move the focus. Confirm or Enter presses; back or Esc goes back and closes this help. "
       "LB and RB (or Ctrl+Tab) change page. Esc on the first page closes the launcher.")]

# Windows launcher palette (Launcher.xaml)
BG, PANEL, FIELD, LINE, FG, MUTED = "#0B1015", "#152027", "#1B252B", "#2E3F44", "#EAF2F2", "#8FA7B1"
GOLD, SECTION, GREEN, HOVER, AMBER, TEAL, OFF = "#E1CFA1", "#CBBF9F", "#3C554B", "#354549", "#E0BE75", "#63E0BD", "#4B5A60"
STATUS_FG, LOGBG, LOGFG = "#AEE2D2", "#0F171C", "#A5BAC5"


# ---- texts ----

def load_strings(path=os.path.join(HERE, "strings.json")):
    """{english: {"es": ..., "fr": ...}}; an unreadable file means English everywhere."""
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, ValueError):
        return {}
    return data if isinstance(data, dict) else {}


STRINGS = load_strings()
LANG = "en"


def set_language(code):
    global LANG
    LANG = code if code in dict(LANGUAGES) else "en"


def tr(english, *args):
    """Lang.T: the text of the current language (English when the row or the cell is missing), formatted with args."""
    row = STRINGS.get(english)
    text = (row.get(LANG) if LANG != "en" and isinstance(row, dict) else None) or english
    return text.format(*args) if args else text


def system_language(env=None):
    env = os.environ if env is None else env
    for name in ("LC_ALL", "LC_MESSAGES", "LANGUAGE", "LANG"):
        value = (env.get(name) or "").split(":")[0].strip()
        if value and value not in ("C", "POSIX"):
            return value[:2].lower() if value[:2].lower() in dict(LANGUAGES) else "en"
    return "en"


def choose_language(saved, env=None):
    """The saved language wins, then the system one, then English."""
    return saved if saved in dict(LANGUAGES) else system_language(env)


# ---- pure logic (tested in launcher_test.py) ----

def valid_host(h):
    if not h or len(h) > 253:
        return False
    if re.fullmatch(r"[0-9.]+", h):
        m = re.fullmatch(r"([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})", h)
        return bool(m) and all(int(o) <= 255 for o in m.groups())
    return re.fullmatch(r"[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?", h) is not None and not any(b in h for b in ("..", "-.", ".-"))


def valid_port(text, low=1):
    return re.fullmatch(r"[0-9]{1,5}", text) is not None and low <= int(text) <= 65535


def clean_text(text, limit):
    """ServerRules.CleanText: control characters out, trimmed, cut at limit."""
    return "".join(c for c in text if unicodedata.category(c) != "Cc").strip()[:limit]


def host_password(text):
    """ServerRules.HostPassword: a semicolon cannot be in it (it separates the server's settings)."""
    return clean_text(text.replace(";", ""), LIMITS["host_password"])


def clean_port(text):
    t = text.strip()
    return int(t) if re.fullmatch(r"[0-9]{1,5}", t) and LIMITS["port_low"] <= int(t) <= LIMITS["port_high"] else DEFAULT_PORT


def clean_name(text):
    return "".join(c for c in text if re.fullmatch(r"[A-Za-z0-9_-]", c))[:LIMITS["name"]] or "Link"


def live_limit(key, text):
    """What a field may hold while it is being typed (the entry's own limit); a semicolon never stays in a host password."""
    text = "".join(c for c in text if unicodedata.category(c) != "Cc")
    if key == "hostpass":
        return text.replace(";", "")[:LIMITS["host_password"]]
    if key == "port":
        return "".join(c for c in text if c in "0123456789")[:5]
    if key == "name":
        return "".join(c for c in text if re.fullmatch(r"[A-Za-z0-9_-]", c))[:LIMITS["name"]]
    cap = {"desc": "description", "addr": "host_address", "invite": "join_address", "joinpass": "join_password"}.get(key)
    return text[:LIMITS[cap]] if cap else text


def invite_url(host, port, password):
    """hyrule://host:port, with ?pw= and the escaped password when there is one."""
    return f"hyrule://{host}:{port}" + (f"?pw={quote(password, safe='')}" if password else "")


def parse_invite(text):
    """Same rules as ServerConnection.TryParse: hyrule://host:port?pw=..., host:port or host. Returns (host, port, password) or None."""
    t = text.strip()
    if len(t) > LIMITS["join_address"]:
        return None
    if t.lower().startswith("hyrule://"):
        t = t[9:]
    t, _, query = t.partition("?")
    m = re.search(r"(^|&)pw=([^&]*)", query)
    password = unquote(m.group(2)) if m else ""
    t, port = t.rstrip("/"), DEFAULT_PORT
    if ":" in t:
        t, p = t.rsplit(":", 1)
        if not valid_port(p):
            return None
        port = int(p)
    return (t, port, password) if valid_host(t) else None


def parse_status(text):
    """`version=0.1.0 mode=host server=up ... invite=hyrule://...` -> dict, or None when the text is not a status line."""
    pairs = dict(re.findall(r"(\w+)=(\S*)", text))
    return pairs if "server" in pairs and "client" in pairs else None


def parse_check(text):
    """`--check` output: `ok: X`, `missing: X`, `skipped: ...`, `info: ...` -> the missing items."""
    return [m.group(1).strip() for m in (re.match(r"missing:\s*(.+)", line.strip()) for line in text.splitlines()) if m]


def parse_presets(text):
    """`--presets` output: lines `DIGITS Name` -> {name: digits}."""
    found = (re.fullmatch(r"([01]{7}[012]) (.+)", line.strip()) for line in text.splitlines())
    return {m.group(2): m.group(1) for m in found if m}


def rules_name(bits, presets=PRESETS):
    return next((n for n, b in presets.items() if b == bits), "Custom")


def valid_bits(bits):
    return re.fullmatch(r"[01]{7}[012]", bits) is not None


def shared_rules(bits):
    """(shared names, not shared names) of the seven rules."""
    return ([n for n, b in zip(RULE_NAMES, bits) if b == "1"], [n for n, b in zip(RULE_NAMES, bits) if b != "1"])


def shares_text(bits):
    on, off = shared_rules(bits)
    return tr("Shared: {0}", ", ".join(tr(n) for n in on) if on else tr("Nothing")) + "\n" + \
        tr("Not shared: {0}", ", ".join(tr(n) for n in off) if off else tr("Nothing"))


def ipv4(text):
    m = re.fullmatch(r"(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})", text.strip())
    return tuple(int(g) for g in m.groups()) if m else None


def classify(host):
    """NetworkHint.Classify: none, vpn, home, public or name."""
    ip = ipv4(host)
    if ip is None:
        h = host.strip().lower()
        return "none" if not h else "vpn" if h.endswith(".ts.net") else "name" if re.search(r"[a-z]", h) else "none"
    a, b = ip[0], ip[1]
    if a == 127 or (a == 169 and b == 254):
        return "none"
    if (a == 100 and 64 <= b <= 127) or a in (25, 26):
        return "vpn"
    if a == 10 or (a == 192 and b == 168) or (a == 172 and 16 <= b <= 31):
        return "home"
    return "public"


def vpn_name(host):
    ip = ipv4(host)
    return "Tailscale" if host.lower().endswith(".ts.net") or (ip and ip[0] == 100) else "Hamachi" if ip and ip[0] == 25 else "Radmin VPN"


def parse_addresses(text):
    """`ip -4 -o addr` output -> the IPv4 addresses."""
    return re.findall(r"inet (\d+\.\d+\.\d+\.\d+)", text)


def best_address(addresses):
    """A VPN address first, then a home network one."""
    for kind in ("vpn", "home"):
        for a in addresses:
            if classify(a) == kind:
                return a
    return ""


def address_hint(host):
    kind = classify(host)
    if kind == "vpn":
        return tr("This is a {0} address. Friends on the same {0} network can join from anywhere.", vpn_name(host))
    if kind == "home":
        return tr("This is a home network address, so only people on the same network can join. For anyone else, install a VPN such as Tailscale on both devices, or forward the port on your router and use your public address.")
    if kind == "none":
        return tr("No network address was found. Connect this device to a network, or type an address.")
    return tr("Friends can join from anywhere if the port is forwarded on your router.")


def build_args(mode, f, bits=PRESETS["Game Completion"]):
    """Mode and form values -> (CLI arguments, None) or (None, error text). Like the Windows launcher, values are cleaned instead of
    refused (port, password, description, name); only an unusable invite stops the start."""
    name = clean_name(f["name"])
    if mode in (LOCAL, HOST):
        rules = ["--rules", bits, "--desc", clean_text(f["desc"], LIMITS["description"]) or DEFAULT_DESC, "--name", name]
        if mode == LOCAL:
            return ["--local", *rules], None
        return ["--host", str(clean_port(f["port"])), host_password(f["hostpass"]), *rules], None
    parsed = parse_invite(f["invite"])
    if not parsed:
        return None, tr("The server address is not valid. Enter an invite link or host:port on the Online page.")
    return [parsed[0], str(parsed[1]), clean_text(f["joinpass"], LIMITS["join_password"]) or parsed[2], name], None


def failure_text(lines, joining):
    """The status line after a failed start: the last useful line of the script, plus the checklist when joining."""
    useful = [mask(line) for line in lines if not re.search(r"\.\.\. \d+s|^Hyrule Online ", line)]
    text = useful[-1] if useful else tr("The start script failed.")
    return tr("Start failed: {0}", text) + (" " + tr(CHECKLIST) if joining else "")


def parse_server_line(line):
    """ServerEvents.cs: a line of the server console -> ("joined", name, slot), ("left", name), ("refused", name, code) or None."""
    start = r"^\s*(?:\[\d{1,2}:\d{2}:\d{2}\]\s*)?Player (?P<name>.+?)"
    m = re.match(start + r" joined the server\. Assigned to player (?P<slot>\d+)\.?\s*$", line)
    if m:
        return ("joined", m.group("name"), int(m.group("slot")))
    m = re.match(start + r" disconnected\.?(?:\s+-\s+Details:.*)?\s*$", line)
    if m:
        return ("left", m.group("name"))
    m = re.match(start + r" tried to connect but failed with error (?P<code>\d+)\.?\s*$", line)
    return ("refused", m.group("name"), int(m.group("code"))) if m else None


def event_text(event):
    if event[0] == "joined":
        return tr("{0} joined.", event[1])
    if event[0] == "left":
        return tr("{0} left.", event[1])
    return tr("{0} could not join: wrong password.", event[1]) if event[2] == 3 else tr("{0} could not join (error {1}).", event[1], event[2])


class Roster:
    """Who is in the world, from the server console."""

    def __init__(self):
        self.rows, self.order = [], 0

    def apply(self, event):
        if event[0] == "joined":
            self.order += 1
            self.rows = [r for r in self.rows if r[1] != event[2] and r[0] != event[1]] + [(event[1], event[2], self.order)]
        elif event[0] == "left":
            self.rows = [r for r in self.rows if r[0] != event[1]]

    def names(self):
        return [r[0] for r in sorted(self.rows, key=lambda r: (r[1], r[2]))]


def load_conf(path):
    """key=value lines like conf_get in hyrule-online.sh (the first line of a key wins)."""
    try:
        with open(path, encoding="utf-8") as f:
            lines = f.read().splitlines()
    except OSError:
        return {}
    out = {}
    for line in lines:
        key, sep, value = line.partition("=")
        if sep and key not in out:
            out[key] = value
    return out


def save_conf(path, values):
    """The launcher's own choices; a failure only means they are not remembered."""
    try:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="utf-8", newline="\n") as f:
            f.write("".join(f"{k}={v}\n" for k, v in values.items() if "\n" not in str(v)))
        os.chmod(tmp, 0o600)
        os.replace(tmp, path)
    except OSError:
        pass


def default_conf_path(env=None):
    env = os.environ if env is None else env
    if env.get("HYRULE_LAUNCHER_CONF"):
        return env["HYRULE_LAUNCHER_CONF"]
    return os.path.join(env.get("XDG_CONFIG_HOME") or os.path.join(os.path.expanduser("~"), ".config"), "hyrule-online", "launcher.conf")


def initial_state(own, join, presets):
    """Starting values: the launcher's own choices (page, mode), then what hyrule-online.sh last saved in join.conf."""
    modes = {"local": LOCAL, "host": HOST, "host a server": HOST, "join": JOIN, "join a server": JOIN}
    mode = modes.get(own.get("mode", "").lower()) or modes.get(join.get("mode", "").lower(), LOCAL)
    host = join.get("host", "")
    return {"tab": own.get("tab") if own.get("tab") in TABS else TABS[0], "mode": mode,
            "bits": join["rules"] if valid_bits(join.get("rules", "")) else next(iter(presets.values())),
            "values": {"port": join.get("hostport") or str(DEFAULT_PORT), "hostpass": host_password(join.get("hostpass", "")),
                       "desc": join.get("desc") or DEFAULT_DESC, "addr": "", "invite": f"{host}:{join.get('port') or DEFAULT_PORT}" if host else "",
                       "joinpass": join.get("password", ""), "name": clean_name(join.get("name", ""))}}


def read_version(dirs=(HERE, os.path.dirname(HERE))):
    for d in dirs:
        try:
            with open(os.path.join(d, "VERSION"), encoding="utf-8") as f:
                return "".join(f.readline().split()) or "unknown"
        except OSError:
            pass
    return "unknown"


def mask(line):
    return re.sub(r"pw=\S*", "pw=…", line)


def ui_scale(width, height):
    """The layout is drawn for 1280x800; other screens scale it (never below what stays readable)."""
    return max(0.7, min(1.6, width / 1280, height / 800))


# ---- keyboard / D-pad navigation: Navigation.cs Spatial, on (x, y, width, height) boxes ----

def _gap(start, length, other_start, other_length):
    return max(0, max(other_start - (start + length), start - (other_start + other_length)))


def _away(point, start, length):
    return start - point if point < start else point - (start + length) if point > start + length else 0


def spatial_pick(frm, items, direction, sideways=False):
    """Index of the box a step in direction (0 up, 1 down, 2 left, 3 right) reaches, or -1. A box lined up with the current one wins."""
    if direction not in (0, 1, 2, 3):
        return -1
    vertical = direction < 2
    best, fallback, best_score, fallback_score = -1, -1, float("inf"), float("inf")
    fx, fy = frm[0] + frm[2] / 2, frm[1] + frm[3] / 2
    for i, it in enumerate(items):
        ix, iy = it[0] + it[2] / 2, it[1] + it[3] / 2
        if direction == 0:
            if iy >= fy - 1:
                continue
            along = max(0, frm[1] - (it[1] + it[3])) + (fy - iy) * .1
        elif direction == 1:
            if iy <= fy + 1:
                continue
            along = max(0, it[1] - (frm[1] + frm[3])) + (iy - fy) * .1
        elif direction == 2:
            if ix >= fx - 1:
                continue
            along = max(0, frm[0] - (it[0] + it[2])) + (fx - ix) * .1
        else:
            if ix <= fx + 1:
                continue
            along = max(0, it[0] - (frm[0] + frm[2])) + (ix - fx) * .1
        if vertical:
            across, offset, miss = _gap(frm[0], frm[2], it[0], it[2]), abs(ix - fx), _away(fx, it[0], it[2])
        else:
            across, offset, miss = _gap(frm[1], frm[3], it[1], it[3]), abs(iy - fy), _away(fy, it[1], it[3])
        if across <= 0:
            score = along + miss * .5 + offset * .1
            if score < best_score:
                best, best_score = i, score
        elif vertical or sideways:
            score = along + across * 1.5 + offset * .3
            if score < fallback_score:
                fallback, fallback_score = i, score
    return best if best >= 0 else fallback


def spatial_next(frm, items, chrome, from_chrome, direction):
    """One step. Left and right from a content control may cross to another row; the chrome (tabs, Help, footer) never does."""
    pick = spatial_pick(frm, items, direction)
    if pick < 0 and direction >= 2 and not from_chrome:
        content = [i for i in range(len(items)) if not chrome[i]]
        side = spatial_pick(frm, [items[i] for i in content], direction, True)
        if side >= 0:
            pick = content[side]
    return pick


def unreachable(items, chrome):
    """[(start, [unreachable])]: from every control, the others that no chain of steps in the four directions reaches (LayoutTests.CheckNavigation)."""
    bad = []
    for start in range(len(items)):
        seen, todo = {start}, [start]
        while todo:
            cur = todo.pop()
            for d in range(4):
                nxt = spatial_next(items[cur], items, chrome, chrome[cur], d)
                if nxt >= 0 and nxt not in seen:
                    seen.add(nxt)
                    todo.append(nxt)
        if len(seen) < len(items):
            bad.append((start, [i for i in range(len(items)) if i not in seen]))
    return bad


# ---- the one place that knows the hyrule-online.sh contract ----

class Cli:
    """Contract (see "CLI used by the GUI" in README.md; adjust here only). Always HYRULE_NO_GUI=1 and stdin from /dev/null.
    `--local|--host [PORT [PASSWORD]] [--no-play] [--rules R] [--desc T] [--name N]` and `HOST PORT [PASSWORD] [NAME]` block until the game
    closes (exit 1 = failed with the reason on stderr, 143 = stopped); `--stop`; `--presets` (`DIGITS Name` lines); `--check` (`ok:`/`missing:`
    lines); `--status` (one line of key=value pairs: version mode server client joined port [host] [invite] [invite_vpn])."""

    def __init__(self, script=os.path.join(HERE, "hyrule-online.sh")):
        self.script, self.proc, self.lines, self.stat, self.missing, self.addresses = script, None, [], None, [], []
        self.busy = self.quiet = self.refreshing = False

    def available(self):
        return os.path.isfile(self.script)

    def presets(self):
        found = parse_presets(self.run("--presets", timeout=10)[1])
        return found or dict(PRESETS)

    def check(self):
        self.missing = parse_check(self.run("--check", timeout=30)[1])

    def run(self, *args, timeout=60):
        try:
            r = subprocess.run(["bash", self.script, *args], capture_output=True, text=True, errors="replace", timeout=timeout,
                               stdin=subprocess.DEVNULL, env=dict(os.environ, HYRULE_NO_GUI="1"))
            return r.returncode, r.stdout + r.stderr
        except (OSError, subprocess.SubprocessError) as error:
            return 1, str(error)

    def alive(self):
        return self.proc is not None and self.proc.poll() is None

    def _spawn(self, args):
        self.lines, self.quiet = [], False
        self.proc = subprocess.Popen(["bash", self.script, *args], stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                     text=True, errors="replace", env=dict(os.environ, HYRULE_NO_GUI="1"))
        threading.Thread(target=self._pump, args=(self.proc, self.lines), daemon=True).start()

    @staticmethod
    def _pump(proc, lines):
        for line in proc.stdout:
            if line.strip():
                lines.append(line.rstrip())

    def _stop(self):
        self.busy = self.quiet = True
        try:
            self.run("--stop")
        finally:
            self.busy = False

    def stop(self):
        threading.Thread(target=self._stop, daemon=True).start()

    def start(self, args, restart):
        def work():
            if restart:
                self._stop()
            try:
                self._spawn(args)
            except OSError as error:
                self.lines = [str(error)]
        threading.Thread(target=work, daemon=True).start()

    def refresh(self):
        self.refreshing = True
        try:
            self.stat = parse_status(self.run("--status", timeout=10)[1])
            try:
                out = subprocess.run(["ip", "-4", "-o", "addr"], capture_output=True, text=True, timeout=5, stdin=subprocess.DEVNULL).stdout
                self.addresses = parse_addresses(out)
            except (OSError, subprocess.SubprocessError):
                self.addresses = []
        finally:
            self.refreshing = False


class ServerLog:
    """Follows logs/server.log (rewritten at each server start): the console lines and who is in the world."""

    def __init__(self, path=os.path.join(HERE, "logs", "server.log")):
        self.path = path
        self.reset()

    def reset(self):
        self.pos, self.partial, self.lines, self.events, self.roster = 0, "", [], [], Roster()

    def poll(self):
        try:
            size = os.path.getsize(self.path)
            if size < self.pos:
                self.reset()
            if size == self.pos:
                return False
            with open(self.path, "rb") as f:
                f.seek(self.pos)
                data = f.read()
        except OSError:
            return False
        self.pos += len(data)
        *done, self.partial = (self.partial + data.decode("utf-8", "replace")).split("\n")
        for line in done:
            self.feed(line.rstrip("\r"))
        return True

    def feed(self, line):
        self.lines = (self.lines + [mask(line)])[-200:]
        event = parse_server_line(line)
        if event:
            self.roster.apply(event)
            self.events = (self.events + [event])[-6:]


# ---- window ----

class App:
    def __init__(self, root, cli=None, conf=None, join_conf=os.path.join(HERE, "join.conf"), size=None, server_log=None):
        self.root, self.cli, self.conf_path = root, cli or Cli(), conf or default_conf_path()
        self.log = server_log or ServerLog()
        self.own = load_conf(self.conf_path)
        set_language(choose_language(self.own.get("language")))
        self.presets = self.cli.presets()
        st = initial_state(self.own, load_conf(join_conf), self.presets)
        self.size = size or (root.winfo_screenwidth(), root.winfo_screenheight())
        self.scale = ui_scale(*self.size)
        self.bits, self.dialog, self.note, self.up, self.started, self.started_mode = st["bits"], None, None, False, None, LOCAL
        self.tab, self.mode = tk.StringVar(root, st["tab"]), tk.StringVar(root, st["mode"])
        self.lang = tk.StringVar(root, LANG)
        self.v = {k: tk.StringVar(root, x) for k, x in st["values"].items()}
        self.last_addr, self.addr_msg, self.copied_until, self.semicolon_until, self.shown = "", False, 0, 0, None
        for key, var in self.v.items():
            var.trace_add("write", lambda *a, k=key: self.typed(k))
        self.fonts()
        self.build()
        self.bind_keys()
        root.protocol("WM_DELETE_WINDOW", self.quit)
        root.bind("<Configure>", self.resize)
        root.bind("<Destroy>", lambda e: e.widget is root and self.stop_timers())
        threading.Thread(target=self.cli.check, daemon=True).start() if self.cli.available() else None
        self.tick()

    @property
    def overlay(self):
        return self.dialog

    # ---- look ----
    def fonts(self):
        have = set(tkfont.families(self.root)) if tkfont else set()
        sans = next((f for f in ("Segoe UI", "Noto Sans", "DejaVu Sans", "Liberation Sans") if f in have), "TkDefaultFont")
        serif = next((f for f in ("Georgia", "Noto Serif", "DejaVu Serif", "Liberation Serif") if f in have), sans)
        mono = next((f for f in ("Consolas", "DejaVu Sans Mono", "Liberation Mono") if f in have), "TkFixedFont")
        sizes = {"body": 18, "small": 14, "section": 16, "tab": 18, "logo": 30, "title": 14, "start": 20}
        self.font = {k: ((serif if k in ("logo", "title") else sans), -max(10, round(v * self.scale)), "bold" if k in ("section", "start") else "normal")
                     for k, v in sizes.items()}
        self.font["mono"] = (mono, -max(10, round(14 * self.scale)))

    def px(self, n):
        return max(1, round(n * self.scale))

    def label(self, p, text="", fg=FG, font="body", wrap=0, **kw):
        o = dict(bg=p["bg"], anchor="w")
        o.update(kw)
        lab = tk.Label(p, text=text, fg=fg, font=self.font[font], justify="left", **o)
        if wrap:
            self.wraps.append((lab, wrap))
        return lab

    def button(self, p, text, cmd, tip=None, kind="Button", **kw):
        o = dict(bg=FIELD, fg=FG, activebackground=HOVER, activeforeground=FG, disabledforeground=OFF, relief="flat", bd=0, font=self.font["body"],
                 highlightthickness=3, highlightbackground=p["bg"], highlightcolor=GOLD, padx=self.px(18), pady=self.px(7), cursor="hand2")
        o.update(kw)
        w = (tk.Button if kind == "Button" else tk.Radiobutton if kind == "Radiobutton" else tk.Checkbutton)(p, text=text, command=cmd, **o)
        return self.tip(w, tip)

    def entry(self, p, var, tip=None, **kw):
        o = dict(textvariable=var, bg=FIELD, fg=FG, insertbackground=GOLD, relief="flat", bd=0, font=self.font["body"], disabledbackground=PANEL,
                 readonlybackground=FIELD, highlightthickness=3, highlightbackground=p["bg"], highlightcolor=GOLD)
        o.update(kw)
        e = tk.Entry(p, **o)
        e.bind("<Left>", lambda ev: None if e.index("insert") > 0 else self.nav(2) or "break")
        e.bind("<Right>", lambda ev: None if e.index("insert") < len(e.get()) else self.nav(3) or "break")
        return self.tip(e, tip)

    def tip(self, w, key):
        if key:
            w.bind("<Enter>", lambda e: self.show_tip(key), add="+")
            w.bind("<FocusIn>", lambda e: self.show_tip(key), add="+")
            w.bind("<Leave>", lambda e: self.show_tip(None), add="+")
            w.bind("<FocusOut>", lambda e: self.show_tip(None), add="+")
        return w

    def show_tip(self, key):
        self.hint.config(text=tr(key or DEFAULT_HINT))

    def panel(self, p, title, **kw):
        f = tk.Frame(p, bg=PANEL, highlightthickness=1, highlightbackground=LINE, padx=self.px(18), pady=self.px(8), **kw)
        self.label(f, tr(title), fg=SECTION, font="section").grid(row=0, column=0, columnspan=4, sticky="w", pady=(0, self.px(4)))
        return f

    def combo(self, p, key, get, cmd):
        """A button that shows the current choice and opens the choice list (Windows has a combo box)."""
        return self.button(p, "", cmd, key, anchor="w")

    def field(self, p, row, text, var, tip, **kw):
        self.label(p, tr(text), fg=MUTED).grid(row=row, column=0, sticky="w", padx=(0, self.px(14)))
        e = self.entry(p, var, tip, **kw)
        e.grid(row=row, column=1, sticky="ew", pady=self.px(3), ipady=self.px(5))
        return e

    # ---- layout ----
    def build(self):
        r = self.root
        for w in r.winfo_children():
            w.destroy()
        self.wraps, self.tips_on = [], True
        r.configure(bg=BG)
        pad = self.px(16)
        head = tk.Frame(r, bg=BG)
        head.pack(fill="x", padx=pad, pady=(self.px(8), 0))
        head.columnconfigure(1, weight=1)
        corner = tk.Frame(head, bg=BG)
        corner.grid(row=0, column=0, sticky="w")
        self.dot = self.label(corner, "●", fg=OFF)
        self.dot.pack(side="left")
        self.corner = self.label(corner, tr("Server stopped"), fg=LOGFG, font="small")
        self.corner.pack(side="left", padx=(self.px(6), 0))
        self.label(head, "THE LEGEND OF ZELDA", fg="#CFBF9C", font="title", anchor="center").grid(row=0, column=1)
        self.help_btn = self.button(head, tr("Help"), self.toggle_help, padx=self.px(16), pady=self.px(4))
        self.help_btn.grid(row=0, column=2, sticky="e")
        self.label(head, "BREATH OF THE WILD", fg="#E2D5B2", font="logo", anchor="center").grid(row=1, column=0, columnspan=3)
        bar = tk.Frame(r, bg=BG)
        bar.pack(fill="x", padx=pad, pady=(self.px(4), self.px(6)))
        lb = self.tip(self.label(bar, "LB", fg=MUTED, font="small"), _k("Previous page (LB or Ctrl+Shift+Tab)"))
        lb.pack(side="left")
        rb = self.tip(self.label(bar, "RB", fg=MUTED, font="small"), _k("Next page (RB or Ctrl+Tab)"))
        rb.pack(side="right")
        tabs = tk.Frame(bar, bg=BG)
        tabs.pack()
        self.tab_btns = {n: self.button(tabs, tr(TAB_LABELS[n]), self.show_tab, kind="Radiobutton", variable=self.tab, value=n, indicatoron=False,
                                        selectcolor=GREEN, font=self.font["tab"], padx=self.px(22)) for n in TABS}
        for b in self.tab_btns.values():
            b.pack(side="left", padx=self.px(4))
        foot = tk.Frame(r, bg=BG)
        foot.pack(side="bottom", pady=(self.px(4), self.px(8)))
        self.start_btn = self.button(foot, tr("START SESSION"), self.start, bg=GREEN, font=self.font["start"], highlightbackground=BG, padx=self.px(36))
        self.stop_btn = self.button(foot, tr("Stop session"), self.stop, padx=self.px(24))
        self.exit_btn = self.button(foot, tr("Exit"), self.quit, padx=self.px(24))
        for b in (self.start_btn, self.stop_btn, self.exit_btn):
            b.pack(side="left", padx=self.px(6))
        self.status = self.label(r, "", fg=STATUS_FG, wrap=1, bg="#212E2D")
        self.status.pack(side="bottom", fill="x", padx=pad, ipady=self.px(3))
        self.hint = self.label(r, tr(DEFAULT_HINT), fg=MUTED, font="small", wrap=1)
        self.hint.pack(side="bottom", fill="x", padx=pad)
        self.host = tk.Frame(r, bg=BG)
        self.host.pack(fill="both", expand=True, padx=pad, pady=(0, self.px(4)))
        self.pages = {n: tk.Frame(self.host, bg=BG) for n in TABS}
        for name in TABS:
            getattr(self, "build_" + name.lower())(self.pages[name])
        self.helpbox = tk.Frame(self.host, bg=PANEL, highlightthickness=1, highlightbackground=LINE)
        self.build_help(self.helpbox)
        self.help_open = False
        self.zones = [head, bar, self.host, foot]
        self.chrome = {self.help_btn, *self.tab_btns.values(), self.start_btn, self.stop_btn, self.exit_btn}
        self.shown = None
        self.show_tab()
        self.show_mode()
        self.wrap(r.winfo_width())

    def resize(self, event):
        if event.widget is self.root:
            self.wrap(event.width)

    def wrap(self, width):
        width = width if width > 1 else self.size[0]
        for lab, frac in self.wraps:
            if lab.winfo_exists():
                lab.config(wraplength=max(self.px(240), int(width * frac) - self.px(70)))

    def build_play(self, page):
        page.columnconfigure(0, weight=1)
        card = self.panel(page, "SESSION")
        card.grid(row=0, column=0, sticky="ew", pady=(self.px(4), self.px(8)))
        card.columnconfigure(3, weight=1)
        self.dots = {}
        for i, (key, name) in enumerate((("server", _k("Dedicated server")), ("client", _k("Player 1 (Cemu A)"))), 1):
            dot = self.label(card, "●", fg=OFF)
            dot.grid(row=i, column=0, sticky="w")
            self.label(card, tr(name)).grid(row=i, column=1, sticky="w", padx=(self.px(8), self.px(24)))
            word = self.label(card, tr("Stopped"), fg=MUTED)
            word.grid(row=i, column=2, sticky="w")
            self.dots[key] = (dot, word)
        right = tk.Frame(card, bg=PANEL)
        right.grid(row=1, column=3, rowspan=2, sticky="e")
        self.label(right, tr("Player 1 name"), fg=MUTED).pack(side="left", padx=(0, self.px(12)))
        self.entry(right, self.v["name"], _k("Player 1 name"), width=14).pack(side="left", ipady=self.px(5))
        self.prompt = self.label(card, tr("Cemu A: wait for the main menu, then press Continue within about a minute."), fg=AMBER, wrap=1)
        self.prompt.grid(row=3, column=0, columnspan=4, sticky="w", pady=(self.px(8), 0))
        box = self.panel(page, "ONLINE")
        box.grid(row=1, column=0, sticky="ew")
        box.columnconfigure(0, weight=1)
        self.modeline = self.label(box, "", wrap=1)
        self.modeline.grid(row=1, column=0, sticky="w")
        self.world = self.label(box, "", fg=MUTED, wrap=1)
        self.world.grid(row=2, column=0, sticky="w")
        self.event = self.label(box, "", fg=MUTED, wrap=1)
        self.event.grid(row=3, column=0, sticky="w")
        self.copy_play = self.button(box, tr("Copy invite"), self.copy_invite, _k("Copies the invite. Your friends paste it in the Join a server mode of their launcher."))
        self.copy_play.grid(row=1, column=1, rowspan=3, sticky="e")

    def build_online(self, page):
        page.columnconfigure((0, 1), weight=1, uniform="c")
        left = self.panel(page, "ONLINE")
        left.grid(row=0, column=0, sticky="nsew", padx=(0, self.px(8)), pady=(self.px(4), 0))
        left.columnconfigure(1, weight=1)
        self.label(left, tr("How this device takes part. Applied when the next session starts."), fg=MUTED, wrap=.5).grid(row=1, column=0, columnspan=2, sticky="w")
        self.label(left, tr("Online mode"), fg=MUTED).grid(row=2, column=0, sticky="w", padx=(0, self.px(14)))
        self.mode_btn = self.combo(left, _k("Play on this device keeps the server private. Host a server lets friends join over the network. Join a server connects to a friend's server."), None, self.pick_mode)
        self.mode_btn.grid(row=2, column=1, sticky="ew", pady=self.px(4))
        self.info = self.label(left, "", fg=MUTED, wrap=.5)
        self.info.grid(row=3, column=0, columnspan=2, sticky="w", pady=(0, self.px(4)))
        self.hostf, self.joinf = tk.Frame(left, bg=PANEL), tk.Frame(left, bg=PANEL)
        for f in (self.hostf, self.joinf):
            f.columnconfigure(1, weight=1)
        self.field(self.hostf, 0, "Port", self.v["port"], _k("The TCP port the server listens on, from 1024 to 65535. It goes in the invite. The default is 5050."))
        self.field(self.hostf, 1, "Password", self.v["hostpass"], _k("Optional. Friends need it to join. Leave it empty for an open server."))
        self.semicolon = self.label(self.hostf, tr("A semicolon (;) is removed from the password."), fg=MUTED, font="small", wrap=.5)
        self.semicolon.grid(row=2, column=1, sticky="w")
        self.field(self.hostf, 3, "Server description", self.v["desc"], _k("The text friends see about this server."))
        self.field(self.joinf, 0, "Invite or address", self.v["invite"], _k("Paste the invite your friend sent you, or type their address and port."))
        self.label(self.joinf, tr("Example: hyrule://100.64.0.5:5050 or 192.168.1.20:5050"), fg=MUTED, font="small", wrap=.5).grid(row=1, column=1, sticky="w")
        self.field(self.joinf, 2, "Password", self.v["joinpass"], _k("The server password, if the invite does not carry it."), show="")
        self.share = self.panel(page, "INVITE FRIENDS")
        self.share.columnconfigure(1, weight=1)
        self.addr_entry = self.field(self.share, 1, "Address in the invite", self.v["addr"], _k("The Tailscale or home network address of this device. Use your public address if you forward the port on your router. Empty it to detect the address again."))
        self.addr_entry.bind("<FocusOut>", lambda e: self.commit_addr(), add="+")
        self.label(self.share, tr("Invite"), fg=MUTED).grid(row=2, column=0, sticky="w", padx=(0, self.px(14)))
        self.invite_box = self.entry(self.share, tk.StringVar(self.root), None, state="readonly", takefocus=0)
        self.invite_box.grid(row=2, column=1, sticky="ew", pady=self.px(3), ipady=self.px(5))
        self.copy_btn = self.button(self.share, tr("Copy invite"), self.copy_invite, _k("Copies the invite. Your friends paste it in the Join a server mode of their launcher."))
        self.copy_btn.grid(row=3, column=0, columnspan=2, sticky="ew", pady=self.px(4))
        self.nethint = self.label(self.share, "", fg=MUTED, font="small", wrap=.5)
        self.nethint.grid(row=4, column=0, columnspan=2, sticky="w")
        self.joinhelp = self.panel(page, "JOINING A SERVER")
        self.label(self.joinhelp, tr("Ask the host for their invite and paste it here. You play on this device with one player. The host chooses the world rules and runs the server."), wrap=.5).grid(row=1, column=0, sticky="w")

    def build_world(self, page):
        page.columnconfigure(0, weight=1)
        card = self.panel(page, "WORLD RULES")
        card.grid(row=0, column=0, sticky="nsew", pady=(self.px(4), 0))
        card.columnconfigure((1, 2, 3), weight=1, uniform="r")
        self.label(card, tr("What everyone on the server shares. Applied when the next session starts."), fg=MUTED, wrap=1).grid(row=1, column=0, columnspan=4, sticky="w")
        self.locked = self.label(card, tr("You joined a friend's server: the host sets the world rules, so these are locked."), fg=AMBER, wrap=1)
        self.locked.grid(row=2, column=0, columnspan=4, sticky="w")
        self.label(card, tr("Preset"), fg=MUTED).grid(row=3, column=0, sticky="w", padx=(0, self.px(14)))
        self.preset_btn = self.combo(card, _k("A ready-made set of sync rules. You can still change each rule below."), None, self.pick_preset)
        self.preset_btn.grid(row=3, column=1, columnspan=3, sticky="ew", pady=self.px(3))
        self.preset_info = self.label(card, "", fg=MUTED, font="small", wrap=.82)
        self.preset_info.grid(row=4, column=1, columnspan=3, sticky="w")
        self.label(card, tr("Special mode"), fg=MUTED).grid(row=5, column=0, sticky="w", padx=(0, self.px(14)))
        self.special_btn = self.combo(card, _k("An optional game mode on top of the rules: Hunter vs Speedrunner or Death Swap."), None, self.pick_special)
        self.special_btn.grid(row=5, column=1, columnspan=3, sticky="ew", pady=self.px(3))
        self.special_info = self.label(card, "", fg=MUTED, font="small", wrap=.82)
        self.special_info.grid(row=6, column=1, columnspan=3, sticky="w")
        self.shares = self.label(card, "", font="small", wrap=1)
        self.shares.grid(row=7, column=0, columnspan=4, sticky="w", pady=(self.px(4), self.px(4)))
        self.checks = []
        grid = tk.Frame(card, bg=PANEL)
        grid.grid(row=8, column=0, columnspan=4, sticky="ew")
        grid.columnconfigure((0, 1, 2), weight=1, uniform="r")
        grid.columnconfigure(3, weight=2)
        for i, name in enumerate(RULE_NAMES):
            var = tk.BooleanVar(self.root)
            c = self.button(grid, "", self.rules_changed, RULE_TIPS[i], kind="Checkbutton", variable=var, indicatoron=False, selectcolor=GREEN)
            c.grid(row=i // 4, column=i % 4, sticky="ew", padx=self.px(2), pady=self.px(2))
            self.checks.append((c, var))
        self.recommended = self.button(grid, tr("Use recommended rules"), self.use_recommended,
                                       _k("Selects the Game Completion preset with no special mode, the set that shares the most for playing the whole game together. It does not depend on this device's hardware."))
        self.recommended.grid(row=1, column=3, sticky="ew", padx=self.px(2), pady=self.px(2))

    def build_language(self, page):
        card = self.panel(page, "LANGUAGE")
        card.pack(fill="x", pady=(self.px(4), 0))
        self.label(card, tr("The launcher text changes right away."), fg=MUTED, wrap=1).grid(row=1, column=0, sticky="w")
        self.lang_btns = {}
        for i, (code, name) in enumerate(LANGUAGES, 2):
            b = self.button(card, name, lambda c=code: self.pick_language(c), kind="Radiobutton", variable=self.lang, value=code, indicatoron=False,
                            selectcolor=GREEN, anchor="w")
            b.grid(row=i, column=0, sticky="ew", pady=self.px(3))
            self.lang_btns[code] = b
        card.columnconfigure(0, weight=1)

    def build_details(self, page):
        page.columnconfigure((0, 1), weight=1, uniform="d")
        page.rowconfigure(0, weight=1)
        self.texts = {}
        for col, title in enumerate((_k("Session log"), _k("Server console"))):
            card = self.panel(page, title)
            card.grid(row=0, column=col, sticky="nsew", padx=(0, self.px(8)) if col == 0 else (0, 0), pady=(self.px(4), 0))
            card.rowconfigure(1, weight=1)
            card.columnconfigure(0, weight=1)
            box = tk.Text(card, bg=LOGBG, fg=LOGFG, font=self.font["mono"], relief="flat", wrap="word", height=6, width=10, highlightthickness=3,
                          highlightbackground=PANEL, highlightcolor=GOLD, state="disabled", cursor="arrow", insertwidth=0)
            self.scroll_keys(box)
            box.grid(row=1, column=0, sticky="nsew")
            self.texts[col] = [box, None]

    def build_help(self, frame):
        box = tk.Text(frame, bg=PANEL, fg=FG, font=self.font["body"], relief="flat", wrap="word", highlightthickness=3, highlightbackground=PANEL,
                      highlightcolor=GOLD, padx=self.px(18), pady=self.px(12), cursor="arrow", insertwidth=0, spacing3=self.px(10))
        box.tag_configure("head", foreground=GOLD, font=self.font["section"])
        for section in HELP_SECTIONS:
            title, _, text = tr(section).partition(":")
            box.insert("end", title.strip() + ": ", "head")
            box.insert("end", text.strip() + "\n\n")
        box.config(state="disabled")
        self.scroll_keys(box)
        box.pack(fill="both", expand=True)
        self.help_text = box

    def scroll_keys(self, box):
        def scroll(direction):
            lo, hi = box.yview()
            if (direction == 0 and lo <= 0) or (direction == 1 and hi >= 1):
                return self.nav(direction) or "break"
            box.yview_scroll(-1 if direction == 0 else 1, "units")
            return "break"
        box.bind("<Up>", lambda e: scroll(0))
        box.bind("<Down>", lambda e: scroll(1))
        box.bind("<Button-1>", lambda e: box.focus_set())

    # ---- pages and modes ----
    def show_tab(self):
        name = self.tab.get() if self.tab.get() in TABS else TABS[0]
        self.close_help()
        for f in self.pages.values():
            f.pack_forget()
        self.pages[name].pack(fill="both", expand=True)
        self.remember("tab", name)

    def show_mode(self):
        m = self.mode.get()
        for f in (self.hostf, self.joinf, self.share, self.joinhelp):
            f.grid_remove()
        gap = {"sticky": "nsew", "padx": (self.px(8), 0), "pady": (self.px(4), 0)}
        if m == HOST:
            self.hostf.grid(row=4, column=0, columnspan=2, sticky="ew")
            self.share.grid(row=0, column=1, **gap)
        elif m == JOIN:
            self.joinf.grid(row=4, column=0, columnspan=2, sticky="ew")
            self.joinhelp.grid(row=0, column=1, **gap)
        self.info.config(text=tr(MODE_INFO[m]))
        self.mode_btn.config(text=tr(MODE_WORDS[m]) + "   ▾")
        self.remember("mode", m)
        self.refresh_rules()
        self.refresh_invite()

    def show_page(self, name):
        self.tab.set(name)
        self.show_tab()

    def toggle_help(self):
        self.close_help() if self.help_open else self.open_help()

    def open_help(self):
        self.help_open = True
        self.helpbox.place(x=0, y=0, relwidth=1, relheight=1)
        self.helpbox.lift()
        self.help_text.focus_set()

    def close_help(self):
        if getattr(self, "help_open", False):
            self.help_open = False
            self.helpbox.place_forget()
            self.help_btn.focus_set()

    def remember(self, key, value):
        if self.own.get(key) != value:
            self.own[key] = value
            save_conf(self.conf_path, self.own)

    def pick_language(self, code):
        set_language(code)
        self.remember("language", code)
        self.lang.set(code)
        self.build()
        self.lang_btns[code].focus_set()

    # ---- online ----
    def typed(self, key):
        var = self.v[key]
        text = live_limit(key, var.get())
        if text != var.get():
            if key == "hostpass":
                self.semicolon_until = 1
                self.semicolon.config(fg=AMBER)
                self.root.after(3000, lambda: self.semicolon.winfo_exists() and self.semicolon.config(fg=MUTED))
            var.set(text)
            return
        if key in ("port", "hostpass", "addr"):
            self.refresh_invite()

    def address(self):
        return self.v["addr"].get().strip()

    def commit_addr(self):
        """Leaving the address field: empty detects it again, an invalid one goes back to the previous one."""
        text = self.address()
        if not text:
            self.v["addr"].set(best_address(self.cli.addresses))
        elif not valid_host(text):
            self.v["addr"].set(self.last_addr)
            self.addr_msg = True
        self.last_addr = self.address()

    def invite_text(self):
        host = self.address() or best_address(self.cli.addresses)
        return invite_url(host, clean_port(self.v["port"].get()), host_password(self.v["hostpass"].get())) if valid_host(host) else ""

    def refresh_invite(self):
        text = self.invite_text()
        self.invite_box.config(state="normal")
        self.invite_box.delete(0, "end")
        self.invite_box.insert(0, text)
        self.invite_box.config(state="readonly")
        self.copy_btn.config(state="normal" if text else "disabled")
        self.nethint.config(text=address_hint(self.address() or best_address(self.cli.addresses)))

    def copy_invite(self):
        text = self.invite_text()
        if text:
            self.root.clipboard_clear()
            self.root.clipboard_append(text)
            self.copied_until = 1
            self.note = ("The invite was copied.", ())
            self.root.after(3000, self.clear_copied)

    def clear_copied(self):
        if self.note and self.note[0] == "The invite was copied.":
            self.note = None

    def pick_mode(self):
        keys = [LOCAL, HOST, JOIN]
        self.ask(tr("Online mode"), tr("Play on this device keeps the server private. Host a server lets friends join over the network. Join a server connects to a friend's server."),
                 [tr(MODE_WORDS[m]) for m in keys] + [tr("Cancel")], lambda i: self.set_mode(keys[i]) if i < len(keys) else None, keys.index(self.mode.get()))

    def set_mode(self, m):
        self.mode.set(m)
        self.show_mode()

    # ---- world rules ----
    def refresh_rules(self):
        locked = self.mode.get() == JOIN
        state = "disabled" if locked else "normal"
        for i, (c, var) in enumerate(self.checks):
            var.set(self.bits[i] == "1")
            c.config(text=("✓  " if var.get() else "✕  ") + tr(RULE_NAMES[i]), state=state)
        name = rules_name(self.bits, self.presets)
        self.preset_btn.config(text=(tr("Custom") if name == "Custom" else name) + "   ▾", state=state)
        self.special_btn.config(text=tr(SPECIALS[int(self.bits[7])]) + "   ▾", state=state)
        self.recommended.config(state=state)
        self.preset_info.config(text=tr(PRESET_BLURBS.get(name, PRESET_BLURBS["Custom"])))
        self.special_info.config(text=tr(SPECIAL_BLURBS[int(self.bits[7])]))
        self.shares.config(text=shares_text(self.bits))
        (self.locked.grid if locked else self.locked.grid_remove)()

    def rules_changed(self):
        self.bits = "".join("1" if var.get() else "0" for _, var in self.checks) + self.bits[7]
        self.refresh_rules()

    def pick_preset(self):
        names = list(self.presets) + ["Custom"]

        def done(i):
            if i < len(self.presets):
                self.bits = list(self.presets.values())[i]
                self.refresh_rules()
        self.ask(tr("Preset"), tr("A ready-made set of sync rules. You can still change each rule below."), [tr(n) if n == "Custom" else n for n in names] + [tr("Cancel")], done,
                 names.index(rules_name(self.bits, self.presets)))

    def pick_special(self):
        def done(i):
            if i < len(SPECIALS):
                self.bits = self.bits[:7] + str(i)
                self.refresh_rules()
        self.ask(tr("Special mode"), tr("An optional game mode on top of the rules: Hunter vs Speedrunner or Death Swap."), [tr(s) for s in SPECIALS] + [tr("Cancel")], done, int(self.bits[7]))

    def use_recommended(self):
        self.bits = self.presets.get(RECOMMENDED[0], RECOMMENDED[1])[:7] + "0"
        self.refresh_rules()

    # ---- session ----
    def form(self):
        return {k: v.get() for k, v in self.v.items()}

    def snapshot(self):
        return (self.mode.get(), self.bits, *(self.v[k].get() for k in ("port", "hostpass", "desc", "name", "invite", "joinpass")))

    def start(self):
        args, error = build_args(self.mode.get(), self.form(), self.bits)
        if error:
            self.note = ("The server address is not valid. Enter an invite link or host:port on the Online page.", ())
            self.show_page("Online")
            self.root.after(50, lambda: self.focus_widget(self.joinf, "Entry"))
            return
        if not self.cli.available():
            self.note = ("hyrule-online.sh was not found next to the launcher.", ())
            return
        restart = self.cli.alive() or self.up
        self.note, self.started_mode, self.started = None, self.mode.get(), self.snapshot()
        self.cli.start(args, restart)

    def focus_widget(self, frame, cls):
        for w in frame.winfo_children():
            if w.winfo_class() == cls:
                return w.focus_set()

    def stop(self):
        self.note = None
        self.cli.stop()

    def stop_timers(self):
        for job in self.root.tk.call("after", "info"):
            self.root.after_cancel(job)

    def tick(self):
        self.root.after(1500, self.tick)
        if self.cli.available() and not self.cli.refreshing:
            threading.Thread(target=self.cli.refresh, daemon=True).start()
        self.render()

    def render(self):
        c, s = self.cli, self.cli.stat or {}
        self.log.poll()
        if c.proc is not None and not c.alive():
            ended, c.proc = c.proc.returncode not in (0, 143, -15) and not c.quiet, None  # 143: stopped
            if ended:
                self.note = ("{0}", (failure_text(c.lines, self.started_mode == JOIN),))
        alive = c.alive()
        srv, cli = s.get("server") == "up", s.get("client") == "up"
        self.up = srv or cli
        joined = s.get("joined") == "yes"
        words = {"server": tr("Running") if srv else tr("Starting") if alive else tr("Stopped"),
                 "client": tr("Joined the server") if cli and joined else tr("Waiting for Continue") if cli else tr("Waiting") if alive and srv else tr("Starting") if alive else tr("Stopped")}
        for key, on in (("server", srv), ("client", cli)):
            dot, word = self.dots[key]
            dot.config(fg=TEAL if on else OFF)
            word.config(text=words[key])
        self.dot.config(fg=TEAL if srv else OFF)
        self.corner.config(text=tr("Server running") if srv else tr("Server stopped"))
        (self.prompt.grid if cli and not joined else self.prompt.grid_remove)()
        self.start_btn.config(text=tr("RESTART SESSION") if self.up else tr("START SESSION"), state="disabled" if c.busy or (alive and not self.up) else "normal")
        self.status.config(text=self.status_text(c, alive, joined))
        self.render_online(srv)
        self.render_details()

    def status_text(self, c, alive, joined):
        if c.busy:
            return tr("Stopping...")
        if self.addr_msg:
            self.addr_msg = False
            self.note = ("That address is not valid, so the previous one was kept. Use letters, digits, dots and dashes only.", ())
        if self.note and not (self.note[0] == "{0}" and alive):
            return tr(self.note[0], *self.note[1])
        if self.up and self.started is not None and self.snapshot() != self.started:
            return tr("Some changes only apply after a restart. Press Restart session to apply them.")
        if self.up:
            return tr("Session running.")
        if alive:
            return tr("Starting...")
        return tr("{0} is missing", c.missing[0]) if c.missing else tr("Ready.")

    def render_online(self, srv):
        m, f = self.mode.get(), self.form()
        invite = parse_invite(self.invite_text())
        parsed = parse_invite(f["invite"])
        if m == LOCAL:
            line = tr("The server only accepts players from this device.")
        elif m == HOST:
            line = tr("Server open at {0}:{1}.", invite[0], invite[1]) if srv and invite else \
                tr("Friends will join at {0}:{1} once the session starts.", invite[0] if invite else "…", clean_port(f["port"]))
        else:
            line = tr("Playing on the server at {0}:{1}.", parsed[0], parsed[1]) if parsed else tr("Enter an invite on the Online page.")
        self.modeline.config(text=line)
        names = self.log.roster.names() if srv and m != JOIN else []
        self.world.config(text=(tr("In the world: {0}", ", ".join(names)) if names else tr("Nobody has joined yet.")) if srv and m == HOST else "")
        self.event.config(text=event_text(self.log.events[-1]) if srv and m == HOST and self.log.events else "")
        host_mode = m == HOST and bool(self.invite_text())
        (self.copy_play.grid if host_mode else self.copy_play.grid_remove)()
        if self.shown != self.cli.addresses:  # a new address list refreshes the detected address
            self.shown = list(self.cli.addresses)
            if not self.address() and self.root.focus_get() is not self.addr_entry:
                self.commit_addr()
            self.refresh_invite()

    def render_details(self):
        for col, text in ((0, "\n".join(mask(line) for line in self.cli.lines[-200:])), (1, "\n".join(self.log.lines))):
            box, shown = self.texts[col]
            if shown != text:
                at_end = box.yview()[1] >= 0.99
                box.config(state="normal")
                box.delete("1.0", "end")
                box.insert("end", text)
                box.config(state="disabled")
                if at_end:
                    box.see("end")
                self.texts[col][1] = text

    def quit(self):
        if self.cli.alive() or self.up:
            def done(i):
                if i == 1:
                    self.cli.run("--stop", timeout=40)
                if i in (0, 1):
                    self.root.destroy()
            self.ask(tr("Hyrule Online"), tr("A session is running. The server and the game keep running if you close the launcher."),
                     [tr("Exit"), tr("Stop session"), tr("Cancel")], done, 2)
        else:
            self.root.destroy()

    # ---- dialogs live inside the window: a second window may not get focus under gamescope ----
    def ask(self, title, text, labels, cb, current=0):
        """Modal list of choices; Escape picks the last one. cb(index) runs after the list closed."""
        self.close_dialog(None)
        ov = tk.Frame(self.root, bg=BG)
        ov.place(x=0, y=0, relwidth=1, relheight=1)
        box = tk.Frame(ov, bg=PANEL, highlightthickness=2, highlightbackground=GOLD, padx=self.px(28), pady=self.px(18))
        box.place(relx=.5, rely=.5, anchor="center")
        self.label(box, title, fg=GOLD, font="section").pack(anchor="w")
        if text:
            self.label(box, text, wrap=0, wraplength=self.px(620), fg=MUTED, font="small").pack(anchor="w", pady=(self.px(4), self.px(8)))
        buttons = [self.button(box, t, lambda i=i: self.close_dialog(i)) for i, t in enumerate(labels)]
        for b in buttons:
            b.pack(fill="x", pady=self.px(2))
        self.dialog = (ov, cb, len(labels) - 1, self.root.focus_get())
        buttons[min(max(current, 0), len(buttons) - 1)].focus_set()

    def close_dialog(self, index):
        if not self.dialog:
            return
        ov, cb, last, before = self.dialog
        self.dialog = None
        ov.destroy()
        try:
            if before is not None and before.winfo_exists():
                before.focus_set()
        except tk.TclError:
            pass
        if index is not None:
            cb(last if index == "escape" else index)

    # ---- keyboard and D-pad (Steam Input sends keys) ----
    def bind_keys(self):
        b = self.root.bind_all
        for name, d in (("Up", 0), ("Down", 1), ("Left", 2), ("Right", 3)):
            b(f"<{name}>", lambda e, d=d: self.nav(d))
        b("<Tab>", lambda e: self.nav("Tab"))
        for seq in ("<Shift-Tab>", "<ISO_Left_Tab>"):
            try:
                b(seq, lambda e: self.nav("BackTab"))
            except tk.TclError:
                pass
        for seq in ("<Return>", "<KP_Enter>"):
            b(seq, self.activate)
        b("<Escape>", lambda e: self.escape())
        b("<Control-Tab>", lambda e: self.cycle(1))
        b("<Control-Shift-Tab>", lambda e: self.cycle(-1))
        b("<Next>", lambda e: self.cycle(1))
        b("<Prior>", lambda e: self.cycle(-1))

    def focusables(self):
        out = []

        def walk(w):
            for c in w.winfo_children():
                kind = c.winfo_class()
                if kind in ("Button", "Radiobutton", "Checkbutton", "Entry", "Text") and c.winfo_viewable() and str(c.cget("state")) != "disabled" \
                        and str(c.cget("takefocus")) != "0":
                    out.append(c)
                walk(c)
        if self.dialog:
            walk(self.dialog[0])
        else:
            for zone in self.zones:
                walk(zone)
            if self.help_open:
                out = [c for c in out if c in self.chrome or c is self.help_text]
        return out

    def boxes(self, items):
        return [(w.winfo_rootx(), w.winfo_rooty(), w.winfo_width(), w.winfo_height()) for w in items]

    def nav(self, key):
        items = self.focusables()
        w = self.root.focus_get()
        if not items:
            return "break"
        if w not in items:
            items[0].focus_set()
        elif key in ("Tab", "BackTab"):
            items[(items.index(w) + (1 if key == "Tab" else -1)) % len(items)].focus_set()
        else:
            d = {"Up": 0, "Down": 1, "Left": 2, "Right": 3}.get(key, key)
            boxes = self.boxes(items)
            i = items.index(w)
            pick = spatial_next(boxes[i], boxes, [x in self.chrome for x in items], w in self.chrome, d)
            if pick >= 0:
                items[pick].focus_set()
        return "break"

    def activate(self, event=None):
        w = self.root.focus_get()
        if isinstance(w, tk.Entry):
            self.nav("Tab")
        elif w is not None and w.winfo_class() in ("Button", "Radiobutton", "Checkbutton"):
            w.invoke()
        return "break"

    def cycle(self, step):
        if not self.dialog:
            self.show_page(TABS[(TABS.index(self.tab.get()) + step) % len(TABS)])
        return "break"

    def escape(self):
        if self.dialog:
            self.close_dialog("escape")
        elif self.help_open:
            self.close_help()
        elif self.tab.get() != TABS[0]:
            self.show_page(TABS[0])
        else:
            self.quit()
        return "break"


def main():
    root = tk.Tk()
    root.title("Hyrule Online")
    sw, sh = root.winfo_screenwidth(), root.winfo_screenheight()
    if os.environ.get("XDG_CURRENT_DESKTOP") == "gamescope" or os.environ.get("HYRULE_FULLSCREEN") == "1":
        root.attributes("-fullscreen", True)
        size = (sw, sh)
    else:
        size = (min(1280, sw), min(800, sh - 80))
        root.geometry(f"{size[0]}x{size[1]}")
    app = App(root, size=size)

    def take_focus():
        root.lift()
        root.focus_force()
        try:
            root.grab_set()
        except tk.TclError:  # not viewable yet
            pass
        app.tab_btns[app.tab.get()].focus_set()
    root.after(200, take_focus)
    root.after(800, take_focus)
    root.mainloop()


if __name__ == "__main__":
    main()
    sys.exit(0)
