#!/usr/bin/env python3
"""Graphical launcher for Hyrule Online on SteamOS (tkinter only). It never talks to a game server:
everything goes through hyrule-online.sh (see Cli); join.conf (written only by that script) is read to prefill the forms."""
import os
import re
import subprocess
import threading
from urllib.parse import unquote

try:
    import tkinter as tk
except ImportError:  # the logic below stays importable (tests) without Tk
    tk = None

HERE = os.path.dirname(os.path.realpath(__file__))
LOCAL, HOST, JOIN = "Local", "Host", "Join"
MODES = [(LOCAL, "Play on this device"), (HOST, "Host a server"), (JOIN, "Join a server")]  # labels as in hyrule-online.sh
TABS = ["Play", "Online", "Help"]
DEFAULT_PORT, DEFAULT_DESC = 5050, "Explore Hyrule with Friends!"
CHECKLIST = "Check the invite, the password, that the host has a session running, and that its firewall allows the port."
# World rules as in hyrule-online.sh / ServerRules.cs: 7 flags (1 = shared) then the special mode digit.
RULE_NAMES = ["Enemies", "Quests", "Koroks", "Towers", "Shrines", "Locations", "Divine Beasts"]
SPECIALS = ["No special mode", "Hunter vs Speedrunner", "Death Swap"]
PRESETS = {"Game Completion": "10111100", "Hunter Vs Speedrunner": "10011101", "Any% Speedrun": "10011100",  # only if `--presets` fails
           "Hide n' seek": "10000000", "Death Swap": "00000002"}
NAME_RE = re.compile(r"[A-Za-z0-9_-]{1,16}")
ONE_PLAYER = ("This device plays one local player: there is no second player, split screen or swap here. "
              "Play on this device keeps the server private: it listens on 127.0.0.1 only, so nobody else can join it.")
HELP = [
    ("SESSION", "Start session opens the server and the game, then joins. When the game reaches its main menu, press "
                "Continue in its window (about a minute). Stop closes the server and the game."),
    ("PLAYERS", "This device plays one local player."),
    ("ONLINE", "Play on this device keeps the server private. Host a server opens it to friends: set the port, password "
               "and description, copy the invite and allow the port in the firewall. Join a server connects to a friend's "
               "server with their invite; the friend sets the rules and runs the server."),
    ("WORLD RULES", "They choose what everyone on the server shares and apply when the server starts. Pick a preset, or "
                    "switch single rules and the special mode."),
    ("CONTROLS", "Arrows or D-pad move, Enter or A confirms, Escape or B goes back. Ctrl+Tab, Page Up and Page Down "
                 "change page. Changes apply when the next session starts."),
]

BG, PANEL, FIELD, LINE, FG, MUTED = "#0B1015", "#152027", "#1B252B", "#2C3A3F", "#EAF2F2", "#9EB2BD"
GOLD, SECTION, GREEN, HOVER, AMBER, TEAL, OFF = "#E1CFA1", "#CBBF9F", "#3C554B", "#354549", "#E0BE75", "#63E0BD", "#4B5A60"
FAMILY = "DejaVu Sans"
FONT = (FAMILY, 16)


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


def parse_invite(text):
    """Same rules as ServerConnection.TryParse: hyrule://host:port?pw=..., host:port or host. Returns (host, port, password) or None."""
    t = text.strip()
    if len(t) > 200:
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


def clean(text, limit):
    return "".join(c for c in text if c.isprintable()).strip()[:limit]


def parse_presets(text):
    """`--presets` output: lines `DIGITS Name` -> {name: digits}."""
    found = (re.fullmatch(r"([01]{7}[012]) (.+)", line.strip()) for line in text.splitlines())
    return {m.group(2): m.group(1) for m in found if m}


def rules_name(bits, presets=PRESETS):
    return next((n for n, b in presets.items() if b == bits), "Custom")


def valid_bits(bits):
    return re.fullmatch(r"[01]{7}[012]", bits) is not None


def build_args(mode, f, bits=PRESETS["Game Completion"]):
    """Mode and form values -> (CLI arguments, None) or (None, error text). Limits are the ones hyrule-online.sh enforces."""
    if mode in (LOCAL, HOST):
        if len(f["desc"].strip()) > 80:
            return None, "The description can have up to 80 characters."
        rules = ["--rules", bits, "--desc", clean(f["desc"], 80) or DEFAULT_DESC]
        if mode == LOCAL:
            return ["--local", *rules], None
        pw = f["hostpass"].strip()
        if not valid_port(f["port"], 1024):
            return None, "The port must be a number from 1024 to 65535."
        if len(pw) > 40 or ";" in pw:
            return None, "The password can have up to 40 characters and cannot contain a semicolon (;)."
        return ["--host", str(int(f["port"])), pw, *rules], None
    parsed = parse_invite(f["invite"])
    if not parsed:
        return None, "That is not a valid invite or address. Use hyrule://host:port?pw=PASSWORD or host:port."
    if not NAME_RE.fullmatch(f["name"]):
        return None, "Use 1 to 16 letters, numbers, _ or -."
    return [parsed[0], str(parsed[1]), f["joinpass"].strip() or parsed[2], f["name"]], None


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


# ---- the one place that knows the hyrule-online.sh contract ----

class Cli:
    """Contract (see "CLI used by the GUI" in README.md; adjust here only). Always HYRULE_NO_GUI=1 and stdin from /dev/null.
    `--local|--host [PORT [PASSWORD]] [--no-play] [--rules R] [--desc T]` and `HOST PORT [PASSWORD] [NAME]` block until the game
    closes (exit 1 = failed with the reason on stderr, 143 = stopped); `--stop`; `--presets` (`DIGITS Name` lines);
    `--status` (one line of key=value pairs: version mode server client joined port [host] [invite] [invite_vpn])."""

    def __init__(self, script=os.path.join(HERE, "hyrule-online.sh")):
        self.script, self.proc, self.lines, self.stat = script, None, [], None
        self.busy = self.quiet = self.refreshing = False

    def available(self):
        return os.path.isfile(self.script)

    def presets(self):
        found = parse_presets(self.run("--presets", timeout=10)[1])
        return found or dict(PRESETS)

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
        finally:
            self.refreshing = False


# ---- widgets ----

def look(p, **kw):
    o = dict(bg=FIELD, fg=FG, activebackground=HOVER, activeforeground=FG, relief="flat", bd=0, font=FONT,
             highlightthickness=4, highlightbackground=p["bg"], highlightcolor=GOLD, padx=18, pady=12, cursor="hand2")
    o.update(kw)
    return o


def label(p, text, **kw):
    o = dict(bg=p["bg"], fg=FG, font=FONT, justify="left", anchor="w")
    o.update(kw)
    return tk.Label(p, text=text, **o)


def button(p, text, cmd, **kw):
    return tk.Button(p, text=text, command=cmd, **look(p, **kw))


def radio(p, text, var, value, cmd):
    return tk.Radiobutton(p, text=text, variable=var, value=value, command=cmd, indicatoron=False, selectcolor=GREEN, **look(p))


def entry(p, var):
    return tk.Entry(p, textvariable=var, bg=FIELD, fg=FG, insertbackground=GOLD, relief="flat", bd=0, font=FONT,
                    highlightthickness=4, highlightbackground=p["bg"], highlightcolor=GOLD)


def panel(p, title):
    f = tk.Frame(p, bg=PANEL, highlightthickness=1, highlightbackground=LINE, padx=20, pady=12)
    label(f, title, fg=SECTION).grid(row=0, column=0, columnspan=2, sticky="w", pady=(0, 6))
    return f


class App:
    def __init__(self, root, cli=None, conf=os.path.join(HERE, "join.conf")):
        self.root, self.cli, self.conf, self.overlay, self.note = root, cli or Cli(), conf, None, ""
        self.wraps, self.shown, self.started_mode, self.up = [], None, LOCAL, False
        self.presets = self.cli.presets()
        self.load()
        self.build()
        self.show_tab()
        self.show_mode()
        self.refresh_rules()
        self.bind_keys()
        root.protocol("WM_DELETE_WINDOW", self.quit)
        root.bind("<Configure>", self.resize)
        root.after_idle(lambda: self.wrap(root.winfo_width()))
        if not self.cli.available():
            self.note = "hyrule-online.sh was not found next to the launcher."
        self.tick()

    # state read from join.conf (written only by hyrule-online.sh)
    def load(self):
        c = load_conf(self.conf)
        modes = {"host": HOST, "host a server": HOST, "join": JOIN, "join a server": JOIN}
        self.mode = tk.StringVar(self.root, modes.get(c.get("mode", "").lower(), LOCAL))
        self.bits = c["rules"] if valid_bits(c.get("rules", "")) else next(iter(self.presets.values()))
        host = c.get("host", "")
        values = {"port": c.get("hostport") or str(DEFAULT_PORT), "hostpass": c.get("hostpass", ""), "desc": c.get("desc") or DEFAULT_DESC,
                  "invite": f"{host}:{c.get('port') or DEFAULT_PORT}" if host else "", "joinpass": c.get("password", ""), "name": c.get("name") or "Link"}
        self.v = {k: tk.StringVar(self.root, x) for k, x in values.items()}

    def form(self):
        return {k: v.get() for k, v in self.v.items()}

    # layout
    def wrapped(self, lab, frac=1.0):
        self.wraps.append((lab, frac))
        return lab

    def resize(self, event):
        if event.widget is self.root:
            self.wrap(event.width)

    def wrap(self, width):
        for lab, frac in self.wraps:
            lab.config(wraplength=max(240, int(width * frac) - 110))

    def build(self):
        r = self.root
        r.configure(bg=BG)
        top = tk.Frame(r, bg=BG)
        top.pack(fill="x", padx=16, pady=(8, 0))
        label(top, "HYRULE ONLINE", fg=GOLD, font=(FAMILY, 22, "bold")).pack(side="left")
        label(top, "Version " + read_version(), fg=MUTED).pack(side="left", padx=18)
        self.stop_btn = button(top, "Stop", self.stop, padx=30)
        self.stop_btn.pack(side="right")
        bar = tk.Frame(r, bg=BG)
        bar.pack(pady=6)
        self.tab = tk.StringVar(r, TABS[0])
        self.tab_btns = {n: radio(bar, n.upper(), self.tab, n, self.show_tab) for n in TABS}
        for b in self.tab_btns.values():
            b.pack(side="left", padx=4)
        foot = tk.Frame(r, bg=BG)
        foot.pack(side="bottom", fill="x", padx=16, pady=(4, 10))
        self.status = label(foot, "", fg="#AEE2D2")
        self.status.pack(side="left", fill="x", expand=True)
        self.start_btn = button(foot, "START SESSION", self.start, bg=GREEN, font=(FAMILY, 20, "bold"), padx=36, pady=12)
        self.start_btn.pack(side="right")
        self.pages = {n: tk.Frame(r, bg=BG) for n in TABS}
        self.build_play(self.pages["Play"])
        self.build_online(self.pages["Online"])
        text = label(self.pages["Help"], "", fg=FG, anchor="nw")
        text.config(text="\n\n".join(f"{h}: {t}" for h, t in HELP))
        text.pack(fill="both", expand=True, anchor="nw")
        self.wrapped(text)

    def build_play(self, page):
        card = panel(page, "SESSION")
        card.pack(fill="x", pady=(4, 8))
        card.columnconfigure(2, weight=1)
        self.dots = {}
        for i, (key, name) in enumerate((("server", "Dedicated server"), ("client", "Player 1 (Cemu)")), 1):
            dot = label(card, "●", fg=OFF)
            dot.grid(row=i, column=0, sticky="w")
            label(card, name).grid(row=i, column=1, sticky="w", padx=(8, 24))
            word = label(card, "Stopped", fg=MUTED)
            word.grid(row=i, column=2, sticky="w")
            self.dots[key] = (dot, word)
        self.modeline = self.wrapped(label(card, ""))
        self.modeline.grid(row=3, column=0, columnspan=3, sticky="w", pady=(8, 0))
        self.wrapped(label(card, ONE_PLAYER, fg=AMBER)).grid(row=4, column=0, columnspan=3, sticky="w", pady=(8, 0))
        logs = panel(page, "RECENT LOG")
        logs.pack(fill="both", expand=True)
        self.log = label(logs, "", fg=MUTED, anchor="nw")
        self.log.grid(row=1, column=0, sticky="nw")

    def build_online(self, page):
        page.columnconfigure((0, 1), weight=1, uniform="c")
        modes = tk.Frame(page, bg=BG)
        modes.grid(row=0, column=0, columnspan=2, pady=(4, 4))
        for m, text in MODES:
            radio(modes, text, self.mode, m, self.pick_mode).pack(side="left", padx=4)
        self.info = self.wrapped(label(page, "", fg=MUTED))
        self.info.grid(row=1, column=0, columnspan=2, sticky="w", pady=(0, 6))
        self.hostf, self.joinf = panel(page, "HOST A SERVER"), panel(page, "JOIN A SERVER")
        for f in (self.hostf, self.joinf):
            f.columnconfigure(1, weight=1)
        for row, (text, key) in enumerate((("Port", "port"), ("Password", "hostpass"), ("Server description", "desc")), 1):
            label(self.hostf, text, fg=MUTED).grid(row=row, column=0, sticky="w", padx=(0, 14))
            entry(self.hostf, self.v[key]).grid(row=row, column=1, sticky="ew", pady=3, ipady=10)
        label(self.joinf, "Invite or address", fg=MUTED).grid(row=1, column=0, sticky="w", padx=(0, 14))
        entry(self.joinf, self.v["invite"]).grid(row=1, column=1, sticky="ew", pady=3, ipady=10)
        self.wrapped(label(self.joinf, "Example: hyrule://100.64.0.5:5050 or 192.168.1.20:5050", fg=MUTED), 0.4).grid(row=2, column=1, sticky="w")
        for row, (text, key) in enumerate((("Password", "joinpass"), ("Name", "name")), 3):
            label(self.joinf, text, fg=MUTED).grid(row=row, column=0, sticky="w", padx=(0, 14))
            entry(self.joinf, self.v[key]).grid(row=row, column=1, sticky="ew", pady=3, ipady=10)
        self.joinhelp = panel(page, "JOINING A SERVER")
        self.wrapped(label(self.joinhelp, "Ask the host for their invite and paste it here. You play on this device with one player. "
                           "The host chooses the world rules and runs the server."), 0.5).grid(row=1, column=0, sticky="w")
        self.share = panel(page, "INVITE FRIENDS")
        self.invite_rows = tk.Frame(self.share, bg=PANEL)
        self.invite_rows.grid(row=1, column=0, sticky="ew")
        self.share.columnconfigure(0, weight=1)
        self.wrapped(label(self.share, "Friends outside your network need a VPN such as Tailscale on both devices, or a forwarded router port.",
                           fg=MUTED), 0.5).grid(row=2, column=0, sticky="w", pady=(8, 0))
        rules = self.rules = panel(page, "WORLD RULES")
        rules.columnconfigure((0, 1, 2), weight=1, uniform="r")
        self.preset_btn = button(rules, "", self.pick_preset)
        self.preset_btn.grid(row=1, column=0, columnspan=3, sticky="ew", pady=3)
        self.special_btn = button(rules, "", self.pick_special)
        self.special_btn.grid(row=2, column=0, columnspan=3, sticky="ew", pady=3)
        self.checks = []
        for i, name in enumerate(RULE_NAMES):
            var = tk.BooleanVar(self.root)
            c = tk.Checkbutton(rules, text=name, variable=var, command=self.rules_changed, indicatoron=False, selectcolor=GREEN, **look(rules))
            c.grid(row=3 + i // 3, column=i % 3, sticky="ew", padx=2, pady=3)
            self.checks.append((c, var))

    def show_tab(self):
        for f in self.pages.values():
            f.pack_forget()
        self.pages[self.tab.get()].pack(fill="both", expand=True, padx=16, pady=(0, 4))

    def show_mode(self):
        m = self.mode.get()
        for f in (self.hostf, self.joinf, self.joinhelp, self.share, self.rules):
            f.grid_remove()
        gap = {"padx": (0, 8), "sticky": "nsew"}
        right = {"padx": (8, 0), "sticky": "nsew"}
        if m == LOCAL:
            self.rules.grid(row=2, column=0, columnspan=2, sticky="nsew")
        elif m == HOST:
            self.hostf.grid(row=2, column=0, **gap)
            self.share.grid(row=3, column=0, pady=(8, 0), **gap)
            self.rules.grid(row=2, column=1, rowspan=2, **right)
        else:
            self.joinf.grid(row=2, column=0, **gap)
            self.joinhelp.grid(row=2, column=1, **right)
        self.info.config(text={LOCAL: "The server only accepts players from this device.",
                               HOST: "Friends who run this launcher can join this device. The world rules are the ones on the right.",
                               JOIN: "This device joins a friend's server. The friend sets the rules."}[m])

    def pick_mode(self):
        self.show_mode()

    # world rules
    def refresh_rules(self):
        for i, (c, var) in enumerate(self.checks):
            var.set(self.bits[i] == "1")
            c.config(text=("☑  " if var.get() else "☐  ") + RULE_NAMES[i])
        self.preset_btn.config(text="Rules: " + rules_name(self.bits, self.presets))
        self.special_btn.config(text="Special mode: " + SPECIALS[int(self.bits[7])])

    def rules_changed(self):
        self.bits = "".join("1" if var.get() else "0" for _, var in self.checks) + self.bits[7]
        self.refresh_rules()

    def pick_preset(self):
        i = self.ask("World rules", "Choose what everyone on the server shares.", *self.presets, "Back")
        if i < len(self.presets):
            self.bits = list(self.presets.values())[i]
            self.refresh_rules()

    def pick_special(self):
        i = self.ask("Special mode", "An optional game mode on top of the rules.", *SPECIALS, "Back")
        if i < len(SPECIALS):
            self.bits = self.bits[:7] + str(i)
            self.refresh_rules()

    # session
    def invites(self):
        s = self.cli.stat or {}
        if s.get("server") != "up":
            return []
        lan, vpn = s.get("invite", ""), s.get("invite_vpn", "")
        out = [("Invite" if lan == vpn else "Invite (same network)", lan)] if lan else []
        return out + ([("Invite (Tailscale or other VPN)", vpn)] if vpn and vpn != lan else [])

    def start(self):
        args, error = build_args(self.mode.get(), self.form(), self.bits)
        if error:
            return self.ask("Hyrule Online", error, "OK")
        if not self.cli.available():
            return self.ask("Hyrule Online", "hyrule-online.sh was not found next to the launcher.", "OK")
        restart = self.cli.alive() or self.up
        if restart and self.ask("Hyrule Online", "A Hyrule Online session is already running. Stop it and start a new one?",
                                "Stop it and start again", "Cancel") != 0:
            return
        self.note, self.started_mode = "", self.mode.get()
        self.cli.start(args, restart)

    def stop(self):
        self.note = ""
        self.cli.stop()

    def failed(self):
        c = self.cli
        lines = [mask(l) for l in c.lines if not re.search(r"\.\.\. \d+s|^Hyrule Online ", l)][-6:]
        text = "\n".join(lines) or "The script stopped without a message."
        joining = self.started_mode == JOIN
        if joining and CHECKLIST not in text:
            text += "\n\n" + CHECKLIST
        self.note = "Start failed: " + (lines[-1] if lines else "the script stopped.")
        self.ask("Wrong password" if "wrong password" in text.lower() else "Could not join" if joining else "Start failed", text, "OK")

    def tick(self):
        self.root.after(1500, self.tick)
        if self.cli.available() and not self.cli.refreshing:
            threading.Thread(target=self.cli.refresh, daemon=True).start()
        self.render()

    def render(self):
        c, s = self.cli, self.cli.stat or {}
        failed = False
        if c.proc is not None and not c.alive():
            failed, c.proc = c.proc.returncode not in (0, 143, -15) and not c.quiet, None  # 143: stopped
        alive = c.alive()
        srv, cli = s.get("server") == "up", s.get("client") == "up"
        self.up = srv or cli
        for key, on in (("server", srv), ("client", cli)):
            dot, word = self.dots[key]
            dot.config(fg=TEAL if on else OFF)
            word.config(text="Running" if on else "Stopped")
        text = ("Stopping..." if c.busy else "Playing." if s.get("joined") == "yes" else "Connecting, about a minute..." if cli else
                "Server running." if srv else mask(c.lines[-1]) if alive and c.lines else "Starting..." if alive else self.note or "Ready.")
        self.status.config(text=text)
        self.start_btn.config(text="RESTART SESSION" if self.up else "START SESSION", state="disabled" if c.busy or (alive and not self.up) else "normal")
        m, f = self.mode.get(), self.form()
        invites, parsed = self.invites(), parse_invite(f["invite"])
        host = parse_invite(invites[0][1]) if invites else None
        self.modeline.config(text={
            LOCAL: "The server only accepts players from this device.",
            HOST: f"Server open at {host[0]}:{host[1]}." if host else f"Friends will join on port {f['port']} once the session starts.",
            JOIN: f"Playing on the server at {parsed[0]}:{parsed[1]}." if parsed else "Enter an invite on the Online page."}[m])
        self.log.config(text="\n".join(mask(l)[:90] for l in c.lines[-6:]) or "Nothing yet.")
        self.show_invites(invites)
        if failed:
            self.failed()

    def show_invites(self, invites):
        if invites == self.shown:
            return
        self.shown = invites
        for w in self.invite_rows.winfo_children():
            w.destroy()
        self.invite_rows.columnconfigure(0, weight=1)
        if not invites:
            self.wrapped(label(self.invite_rows, "The invite appears here once the server is running.", fg=MUTED), 0.5).grid(row=0, column=0, sticky="w")
        for i, (name, url) in enumerate(invites):
            self.wrapped(label(self.invite_rows, f"{name}\n{url}"), 0.4).grid(row=i, column=0, sticky="w", pady=4)
            b = button(self.invite_rows, "Copy invite", lambda u=url, n=i: self.copy(u, n))
            b.grid(row=i, column=1, padx=(10, 0), pady=4)
        self.invite_rows.update_idletasks()

    def copy(self, url, i):
        self.root.clipboard_clear()
        self.root.clipboard_append(url)
        b = self.invite_rows.grid_slaves(row=i, column=1)[0]
        b.config(text="Copied")
        self.root.after(2000, lambda: b.winfo_exists() and b.config(text="Copy invite"))

    def quit(self):
        if self.cli.alive() or self.up:
            i = self.ask("Hyrule Online", "A session is running. The server and the game keep running if you close the launcher.",
                         "Close and leave it running", "Stop it and exit", "Keep the launcher open")
            if i == 2:
                return
            if i == 1:
                self.cli.run("--stop", timeout=40)
        self.root.destroy()

    # dialogs live inside the window: a second window may not get focus under gamescope
    def ask(self, title, text, *labels):
        """Modal box. Escape picks the last button. Returns the index of the button used."""
        ov = tk.Frame(self.root, bg=BG)
        ov.place(x=0, y=0, relwidth=1, relheight=1)
        box = tk.Frame(ov, bg=PANEL, highlightthickness=2, highlightbackground=GOLD, padx=28, pady=20)
        box.place(relx=0.5, rely=0.5, anchor="center")
        label(box, title, fg=GOLD, font=(FAMILY, 20, "bold")).pack(anchor="w")
        if text:
            label(box, text, wraplength=620).pack(anchor="w", pady=(8, 8))
        result = tk.IntVar(self.root, -1)
        first = None
        for i, t in enumerate(labels):
            b = button(box, t, lambda i=i: result.set(i))
            b.pack(fill="x", pady=3)
            first = first or b
        before, previous = self.root.focus_get(), self.overlay
        self.overlay = (ov, result, len(labels) - 1)
        first.focus_set()
        ov.grab_set()
        ov.wait_variable(result)
        ov.grab_release()
        ov.destroy()
        self.overlay = previous
        if before is not None and before.winfo_exists():
            before.focus_set()
        return result.get()

    # keyboard and D-pad (Steam Input sends keys)
    def bind_keys(self):
        b = self.root.bind_all
        for k in ("Up", "Down", "Left", "Right"):
            b(f"<{k}>", lambda e, k=k: self.nav(k))
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
        b("<Next>", lambda e: self.cycle(1))
        b("<Prior>", lambda e: self.cycle(-1))
        self.root.after(300, self.root.focus_force)

    def focusables(self):
        out = []

        def walk(w):
            for c in w.winfo_children():
                if c.winfo_class() in ("Button", "Radiobutton", "Checkbutton", "Entry") and c.winfo_viewable() and str(c.cget("state")) != "disabled":
                    out.append(c)
                walk(c)
        walk(self.overlay[0] if self.overlay else self.root)
        return out

    def nav(self, key):
        w = self.root.focus_get()
        if isinstance(w, tk.Entry) and key in ("Left", "Right"):
            return None
        if w is self.stop_btn and key == "Down" and not self.overlay:
            self.tab_btns[self.tab.get()].focus_set()
            return "break"
        c = self.focusables()
        if not c:
            return "break"
        if w not in c:
            c[0].focus_set()
        elif key in ("Tab", "BackTab"):
            c[(c.index(w) + (1 if key == "Tab" else -1)) % len(c)].focus_set()
        else:
            def mid(x):
                return x.winfo_rootx() + x.winfo_width() / 2, x.winfo_rooty() + x.winfo_height() / 2
            x0, y0 = mid(w)
            best = None
            for o in c:
                dx, dy = mid(o)[0] - x0, mid(o)[1] - y0
                main, side = {"Up": (-dy, abs(dx)), "Down": (dy, abs(dx)), "Left": (-dx, abs(dy)), "Right": (dx, abs(dy))}[key]
                if o is not w and main > 8 and (best is None or main + 2 * side < best[0]):
                    best = (main + 2 * side, o)
            if best:
                best[1].focus_set()
        return "break"

    def activate(self, event):
        w = self.root.focus_get()
        if isinstance(w, tk.Entry):
            self.nav("Tab")
        elif w is not None and w.winfo_class() in ("Button", "Radiobutton", "Checkbutton"):
            w.invoke()
        return "break"

    def cycle(self, step):
        if not self.overlay:
            self.tab.set(TABS[(TABS.index(self.tab.get()) + step) % len(TABS)])
            self.show_tab()

    def escape(self):
        if self.overlay:
            self.overlay[1].set(self.overlay[2])
        elif self.tab.get() != TABS[0]:
            self.tab.set(TABS[0])
            self.show_tab()
        else:
            self.quit()


def main():
    root = tk.Tk()
    root.title("Hyrule Online")
    if os.environ.get("XDG_CURRENT_DESKTOP") == "gamescope" or os.environ.get("HYRULE_FULLSCREEN") == "1":
        root.attributes("-fullscreen", True)
    else:
        root.geometry(f"{min(1280, root.winfo_screenwidth())}x{min(800, root.winfo_screenheight() - 80)}")
        root.minsize(800, 560)
    App(root)
    root.mainloop()


if __name__ == "__main__":
    main()
