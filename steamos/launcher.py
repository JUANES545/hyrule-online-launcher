#!/usr/bin/env python3
"""Graphical launcher for Hyrule Online on SteamOS (tkinter only), laid out like the Windows launcher (src/Launcher).
It never talks to a game server: everything goes through hyrule-online.sh (see Cli); join.conf (written only by that script)
is read to prefill the forms and launcher.conf keeps the launcher's own choices (language, last page, last mode).
Texts are the English ones of Lang.cs; strings.json carries the Spanish and French rows, keyed by the English text (see tr)."""
import base64
import json
import math
import os
import re
import struct
import subprocess
import sys
import threading
import time
import unicodedata
import zlib
from urllib.parse import quote, unquote

try:
    import tkinter as tk
    import tkinter.font as tkfont
except ImportError:  # the logic below stays importable (tests) without Tk
    tk = tkfont = None
try:
    import gamepad
except ImportError:  # no controller module next to the launcher: keyboard only
    gamepad = None
import display

HERE = os.path.dirname(os.path.realpath(__file__))
LOCAL, HOST, JOIN = "Local", "Host", "Join"
TABS = ["Play", "Online", "Graphics", "World", "Language", "Details"]  # the Windows order, without the menu-controls card of its Graphics page
LANGUAGES = [("en", "English"), ("es", "Español"), ("fr", "Français")]
GAME_DELAY_MS = 4000  # the Cemu window needs a moment to exist before the launcher steps aside
TAG_MS = 2000  # how often new Cemu windows get the shortcut's STEAM_GAME while the launcher is hidden
CAPTURE_S = 10  # seconds the launcher waits for a new menu shortcut before it gives up
SHORTCUT_PRESETS = ("Start+LT", "Start+LT+RT", "L3+R3")  # the Windows picker's presets; "Off" and a saved custom one are added to them
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


TAB_LABELS = {"Play": _k("PLAY"), "Online": _k("ONLINE"), "Graphics": _k("GRAPHICS"), "World": _k("WORLD"), "Language": _k("LANGUAGE"), "Details": _k("DETAILS")}
MODE_WORDS = {LOCAL: _k("Play on this device"), HOST: _k("Host a server"), JOIN: _k("Join a server")}  # as in hyrule-online.sh
MODES = [(LOCAL, "Play on this device"), (HOST, "Host a server"), (JOIN, "Join a server")]
MODE_INFO = {LOCAL: _k("The server only accepts players from this device."),
             HOST: _k("Friends who run this launcher can join this device. The world rules are the ones on the World page."),
             JOIN: _k("One player on this device joins a friend's server. The friend sets the rules.")}
DEFAULT_HINT = _k("Hover an option, or move the focus to it, to see what it does.")
SHORTCUT_TIP = _k("Hold these controller buttons for 1.5 seconds while a game is in front to bring this launcher over it. Hold them again, or press the back button here, to return to the game. Pick buttons the game does not need together, or turn it off.")
HINT_TABS = ("Online", "Graphics", "World")  # the pages of Launcher.xaml that carry the hint bar
GRAPHICS_ROWS = [  # (setting, caption, tip) of the GRAPHICS card
    ("scale", _k("Render resolution"), _k("How many pixels each game renders. Higher is sharper but needs a stronger graphics card.")),
    ("shadows", _k("Shadows"), _k("Quality of the shadows in the game.")),
    ("shadow_distance", _k("Shadow draw distance"), _k("How far from the player shadows are drawn.")),
    ("aa", _k("Anti-aliasing"), _k("Smooths jagged edges."))]
API_ROW = ("api", _k("Graphics API"), _k("The graphics backend Cemu uses. Vulkan is usually the better choice on modern graphics cards; OpenGL is the older backend, useful if Vulkan crashes or glitches on this device. Auto picks Vulkan when this device supports it and OpenGL otherwise. After changing it, the first start can take longer while shaders compile again."))
FILTER_ROWS = [("upscale", _k("Upscale filter"), _k("How Cemu stretches the picture when the window is larger than the game's resolution. Bilinear is the softest and fastest; Bicubic and Bicubic Hermite look sharper at a small cost; Nearest neighbor keeps hard pixel edges.")),
               ("downscale", _k("Downscale filter"), _k("How Cemu shrinks the picture when the window is smaller than the game's resolution, which happens with a render resolution above 100%. Bilinear suits almost everyone; the others are slower and rarely worth it."))]
VOLUME_ROW = ("volume", _k("Master volume"), _k("The sound level of every game window, for one or two players. Muted silences the games. Applied at the next session start."))
FPS_TIP = _k("FPS target")
GFX_WORDS = (_k("Low"), _k("Medium"), _k("High"), _k("Very High"), _k("Ultra"), _k("Extreme"), _k("Normal FXAA"), _k("Default"), _k("Recommended"), _k("Unstable"),
             _k("Experimental"), _k("(window size)"), _k("(sharper)"), _k("(faster)"), _k("Basic"), _k("Balanced"))  # the words display.label and scale_label pass through tr
GRAPHICS_TOGGLES = [
    ("vsync", _k("VSync"), _k("Waits for the display refresh to avoid screen tearing. It can cap the frame rate at the display's refresh rate and adds a little input delay. Off suits most setups.")),
    ("async", _k("Asynchronous shader compile"), _k("Compiles shaders in the background instead of pausing the game, so there is less stutter when something new appears for the first time. The price: objects may be missing for a moment. Recommended on.")),
    ("barriers", _k("Accurate barriers (Vulkan)"), _k("Vulkan only. Fixes rare graphical glitches on some graphics cards at a small cost in speed. Leave it off unless you see flickering or missing effects.")),
    ("showfps", _k("Show FPS"), _k("Shows Cemu's frame counter in a corner of each game window. It only changes what is drawn on screen, not how the game runs. Applied at the next session start.")),
    ("usage", _k("Show CPU, RAM and VRAM usage"), _k("Adds Cemu's CPU, RAM and VRAM readouts to the counter in each game window, to see how much two games ask of this device. Applied at the next session start.")),
    ("shader", _k("Show the \"compiling shaders\" notice"), _k("Shows Cemu's \"compiling shaders\" message in each game window while new shaders build. Turn it off for a cleaner picture; the shaders still compile. Applied at the next session start."))]
RECOMMENDED_TIP = _k("Sets the render resolution, shadows, anti-aliasing, the Cemu options and the FPS target to the values recommended for this device. Applied the next time a session starts. It is a starting point, not a benchmark: adjust it if the game runs badly.")
GFX_TEXT = {key: (caption, tip) for key, caption, tip in GRAPHICS_ROWS + FILTER_ROWS + [API_ROW, VOLUME_ROW]}
GFX_TEXT["fps"] = (FPS_TIP, FPS_TIP)
GFX_PACK = {"shadows": display.SHADOWS, "shadow_distance": display.SHADOW_DISTANCE, "aa": display.ANTI_ALIASING}  # the choices that come from the game's pack
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
    _k("DISPLAY: The launcher detects this device's screen and refresh rate, and each session starts the game at that size and frame rate. "
       "The FPS target never goes above the refresh rate. The preview shows the display, not a game screenshot."),
    _k("GRAPHICS: Render resolution scales the picture sharper or faster. Use recommended settings picks values that suit this device. "
       "Changes apply at the next session start."),
    _k("WORLD: World rules choose what everyone on the server shares (enemies, quests, koroks, towers, shrines, locations, Divine Beasts) and apply "
       "when the server starts. When you join a friend's server their rules apply and these are locked. Use recommended rules selects Game Completion."),
    _k("LANGUAGE: The launcher language changes at once."),
    _k("MOVING AROUND: The D-pad, the arrow keys or Tab move the focus. Confirm or Enter presses; back or Esc goes back and closes this help. "
       "LB and RB (or Ctrl+Tab) change page. Esc on the first page closes the launcher."),
    _k("THE GAME: When the game starts the launcher steps aside so you can see it. Hold the menu shortcut (set on the Graphics page) to bring the launcher back, "
       "and again to go back to the game (or use Return to game). Start presses Start session when no session is running.")]

def start_icon_png(scale, color=(0xEA, 0xF2, 0xF2), gap=16):
    """Base64 PNG of the Start button mark drawn by PadIcons.Start in Windows (a round outline with three bars), `gap` px of clear space on its left.
    Tk 8.6 shows the alpha of a PNG, so the mark sits on whatever fill the button has."""
    def seg(x, y, x1, y1, x2, y2):
        dx, dy = x2 - x1, y2 - y1
        t = max(0.0, min(1.0, ((x - x1) * dx + (y - y1) * dy) / (dx * dx + dy * dy)))
        return math.hypot(x - x1 - t * dx, y - y1 - t * dy)
    s, ss = max(1, round(24 * scale)), 4
    g = round(gap * scale)
    w, rows = s + g, []
    for py in range(s):
        row = bytearray([0])
        for pxl in range(w):
            hit = 0
            for sy in range(ss):
                for sx in range(ss):
                    x, y = ((pxl - g + (sx + .5) / ss) / s) * 24, ((py + (sy + .5) / ss) / s) * 24
                    ring = abs(math.hypot(x - 12, y - 12) - 11) <= .9
                    bars = any(seg(x, y, 7, by, 17, by) <= .9 for by in (8, 12, 16))
                    hit += 1 if pxl >= g and (ring or bars) else 0
            row += bytes(color) + bytes([round(255 * hit / (ss * ss))])
        rows.append(bytes(row))
    raw = b"".join(rows)

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, s, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")
    return base64.b64encode(png).decode("ascii")


def blend(argb, under):
    """A '#AARRGGBB' brush of Launcher.xaml laid over the '#RRGGBB' it sits on (Tk has no alpha); '#RRGGBB' comes back as it is."""
    if len(argb) == 7:
        return argb
    a = int(argb[1:3], 16) / 255
    top = [int(argb[i:i + 2], 16) for i in (3, 5, 7)]
    low = [int(under[i:i + 2], 16) for i in (1, 3, 5)]
    return "#%02X%02X%02X" % tuple(round(t * a + b * (1 - a)) for t, b in zip(top, low))


# Windows launcher palette (Launcher.xaml); the translucent brushes are blended in App.skin over the surface they sit on.
BG, FG = "#0B1015", "#EAF2F2"
GOLD, SECTION, AMBER, TEAL, OFF = "#E1CFA1", "#CBBF9F", "#E0BE75", "#63E0BD", "#4B5A60"
LABEL, INFO, SOFT, MUTED = "#8FA7B1", "#C9D6DB", "#78939F", "#A5BAC5"  # field labels, hints, captions, status words
STATUS_FG, LOGBG, LOGFG, BUTTON_FG, SUN = "#AEE2D2", "#0F171C", "#A5BAC5", "#ECF5F4", "#E5C384"
BRUSH = {"panel": "#A0152027", "edge": "#3449585A", "field": "#801B252B", "hover": "#B0354549", "focus": "#B0354542", "line": "#364E5659",
         "pick": "#B03C554B", "pick_line": "#D5C396", "on_line": "#6A8F83", "bar": "#80212E2D", "hint": "#80111A20", "help": "#FC193039",
         "p1_line": "#557EE9C9", "p2_line": "#55E5C384", "badge": "#801B252B", "popup": "#F01B252B", "deep": "#E0152027"}
BLURB, BADGE_FG, PLAYER1 = "#9EB2BD", "#CFE3E4", "#7EE9C9"
PANEL = blend(BRUSH["panel"], BG)

# The fonts the Windows launcher bundles (src/Launcher/Fonts) are copied to fonts/ and used by name; every other family is only a fallback.
FONT_DIR = os.path.join(HERE, "fonts")
FONT_FILES = ("RobotoCondensed-Regular.ttf", "RobotoCondensed-Italic.ttf", "Cinzel-Regular.ttf", "CinzelDecorative-Regular.ttf")
MENU_FONT, TITLE_FONT, LOGO_FONT = "Roboto Condensed", "Cinzel", "Cinzel Decorative"
FALLBACK_FONTS = {"menu": ("Segoe UI", "Noto Sans", "DejaVu Sans", "Liberation Sans"), "title": ("Georgia", "Noto Serif", "DejaVu Serif", "Liberation Serif"),
                  "mono": ("Consolas", "DejaVu Sans Mono", "Liberation Mono"), "symbol": ("Segoe UI Symbol", "DejaVu Sans", "Noto Sans Symbols")}


_REGISTERED = {}


def register_fonts(folder=FONT_DIR):
    """Makes the bundled fonts known to this process only (no root, nothing installed): GDI private fonts on Windows, fontconfig's
    application fonts on Linux. False when a file is missing or the system refuses; the window then uses the fallback families."""
    if folder not in _REGISTERED:
        _REGISTERED[folder] = _register(folder)
    return _REGISTERED[folder]


def _register(folder):
    paths = [os.path.join(folder, name) for name in FONT_FILES]
    if not all(os.path.isfile(p) for p in paths):
        return False
    try:
        import ctypes
        import ctypes.util
        if os.name == "nt":
            return all(ctypes.windll.gdi32.AddFontResourceExW(p, 0x10, 0) for p in paths)  # FR_PRIVATE
        lib = ctypes.CDLL(ctypes.util.find_library("fontconfig") or "libfontconfig.so.1")
        lib.FcConfigAppFontAddDir.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
        return bool(lib.FcConfigAppFontAddDir(None, os.fsencode(folder)))
    except (OSError, AttributeError):
        return False


def shell_fit(width, height):
    """ShellFit.cs: the design canvas the Windows launcher draws on for a window of this size, and the scale that fits it
    (its Viewbox keeps a 20x16 margin)."""
    natural = max(1, width) / max(1, height) * 1030
    design_w = min(1760, max(1280, natural))
    design_h = 1030 if natural >= 1280 else 1280 * max(1, height) / max(1, width)
    return design_w, design_h, max(0.3, min((width - 40) / design_w, (height - 32) / design_h))


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


def tag_game_windows(appid):
    """Mark the Cemu windows (Wine's steam_proton class) with the shortcut's STEAM_GAME. Gamescope only focuses a window it ties to the Steam
    shortcut, by the process tree or by this property, and Cemu runs under systemd, outside the tree. Returns how many windows were found."""
    try:
        ids = subprocess.run(["xdotool", "search", "--class", "steam_proton"], capture_output=True, text=True, timeout=5).stdout.split()
        for w in ids:
            subprocess.run(["xprop", "-id", w, "-f", "STEAM_GAME", "32c", "-set", "STEAM_GAME", appid], capture_output=True, timeout=5)
    except (OSError, subprocess.SubprocessError):
        return 0
    return len(ids)


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


# ---- rounded look: Tk draws no rounded corners, so a Canvas is painted behind each widget ----

# Fill, edge (a BRUSH key or a colour) and edge width in design px of the boxes that only decorate; the others follow the state of their widget.
STATIC = {"card": ("panel", "edge", 1), "p1": ("panel", "p1_line", 2), "deep": ("deep", "deep", 0), "bar": ("bar", "bar", 0), "hint": ("hint", "hint", 0),
          "help": ("help", "edge", 1), "popup": ("popup", GOLD, 1), "badge": ("badge", MUTED, 2)}


def rounded(x0, y0, x1, y1, radii, steps=5):
    """Outline of a rectangle with its own radius per corner (top left, top right, bottom right, bottom left) for Canvas.create_polygon."""
    pts = []
    for (cx, cy, r, start) in ((x0 + radii[0], y0 + radii[0], radii[0], 180), (x1 - radii[1], y0 + radii[1], radii[1], 270),
                               (x1 - radii[2], y1 - radii[2], radii[2], 0), (x0 + radii[3], y1 - radii[3], radii[3], 90)):
        for i in range(steps + 1):
            a = math.radians(start + 90 * i / steps)
            pts += [cx + r * math.cos(a), cy + r * math.sin(a)]
    return pts


def forward(inner, outer):
    """Layout calls on a skinned widget go to the frame that holds it."""
    for name in ("grid", "pack", "place", "grid_remove", "grid_forget", "pack_forget", "place_forget", "grid_info"):
        setattr(inner, name, getattr(outer, name))


class Skin:
    """Rounded, bordered look for one Tk widget: a frame takes the widget's place in the layout and a Canvas painted behind the widget draws
    the fill and the border. The widget keeps its class, focus and key handling; Skin only recolours it from hover, focus, selection and
    the disabled state. The widget is `.skin.w`; its frame is `.box` (for sizes) and receives the layout calls."""

    def __init__(self, app, parent, make, kind, radius=5, left=None, right=None, minw=0, minh=0, fg=FG, deco=None, var=None, sticky="nsew"):
        self.app, self.kind, self.fg, self.deco, self.sig = app, kind, fg, deco, None
        self.hover = self.focus = False
        self.surface = parent["bg"]
        px = app.px
        edge = STATIC[kind][2] if kind in STATIC else 2
        self.edge = max(1, math.ceil(edge * app.scale - 0.01)) if edge else 0
        self.radii = [px(r) if r else 0 for r in (radius if isinstance(radius, tuple) else (radius,) * 4)]
        o = self.outer = tk.Frame(parent, bg=self.surface)
        self.cv = tk.Canvas(o, bg=self.surface, bd=0, highlightthickness=0)
        self.cv.place(x=0, y=0, relwidth=1, relheight=1)
        w = self.w = make(o)
        w.grid(row=0, column=1, sticky=sticky, pady=self.edge)
        pad_l = self.edge + (px(left) if left else 0)
        pad_r = self.edge + (px(right) if right else 0)
        o.columnconfigure(0, minsize=pad_l)
        o.columnconfigure(1, weight=1, minsize=max(0, px(minw) - pad_l - pad_r) if minw else 0)
        o.columnconfigure(2, minsize=pad_r)
        o.rowconfigure(0, weight=1, minsize=px(minh) if minh else 0)
        self.set = w.configure
        w.configure = w.config = self.reconfigure
        w.skin, w.box = self, o
        forward(w, o)
        for part in (self.cv, w):
            part.bind("<Enter>", lambda e: self.change(hover=True), add="+")
            part.bind("<Leave>", lambda e: self.change(hover=False), add="+")
        w.bind("<FocusIn>", lambda e: self.change(focus=True), add="+")
        w.bind("<FocusOut>", lambda e: self.change(focus=False), add="+")
        self.cv.bind("<Configure>", self.paint)
        self.cv.bind("<Button-1>", self.press)
        if var is not None:
            tid = var.trace_add("write", lambda *a: self.paint())
            w.bind("<Destroy>", lambda e: e.widget is w and self.untrace(var, tid), add="+")
        look = self.look()
        self.tint(look[0], look[2])

    def untrace(self, var, tid):
        try:
            var.trace_remove("write", tid)
        except tk.TclError:
            pass

    def reconfigure(self, cnf=None, **kw):
        result = self.set(cnf, **kw)
        self.paint()
        return result

    def change(self, **kw):
        if "hover" in kw:
            if kw["hover"] and not self.app.pointer:
                return  # the pad moves the cursor now; a pointer parked on a control must not light it as a second selection
            (self.app.hovered.add if kw["hover"] else self.app.hovered.discard)(self)
        self.__dict__.update(kw)
        self.paint()

    def press(self, event):
        w = self.w
        if self.kind in STATIC or str(w.cget("state")) == "disabled":
            return
        w.focus_set()
        if hasattr(w, "invoke"):
            w.invoke()

    def selected(self):
        w, cls = self.w, self.w.winfo_class()
        if cls not in ("Radiobutton", "Checkbutton"):
            return False
        try:
            value = w.getvar(str(w.cget("variable")))
            return str(value) == str(w.cget("value")) if cls == "Radiobutton" else bool(self.app.root.tk.getboolean(value))
        except tk.TclError:
            return False

    def look(self):
        """(fill, edge, text colour, selected, disabled) of the widget now, with the translucent brushes laid over the surface it sits on."""
        S, k = self.surface, self.kind
        if k in STATIC:
            fill, edge = (blend(BRUSH.get(x, x), S) for x in STATIC[k][:2])
            return fill, edge, self.fg, False, False
        off = str(self.w.cget("state")) == "disabled"
        on = self.selected()
        picked = k == "start" or (k == "tab" and on)
        fill = blend(BRUSH["pick" if picked else "field"], S)
        edge = blend(BRUSH["pick_line"] if picked else BRUSH["on_line"] if on and k == "toggle" else BRUSH["line"], S)
        if self.hover and not off and k != "entry":
            fill = blend("#18FFFFFF", fill) if picked else blend(BRUSH["hover"], S)
        if self.focus and not off:
            edge = GOLD
            if k != "entry" and not picked:
                fill = blend(BRUSH["focus"], S)
        fg = self.fg
        if off:  # Windows draws a disabled control at 40% opacity
            fill, edge, fg = (blend("#66" + c[1:], S) for c in (fill, edge, fg))
        return fill, edge, fg, on, off

    def alive(self):
        try:
            return bool(self.cv.winfo_exists())
        except tk.TclError:
            return False

    def paint(self, *_):
        if not self.alive():
            return
        cv, e = self.cv, self.edge
        W, H = cv.winfo_width(), cv.winfo_height()
        if W < 4 or H < 4:
            return
        fill, edge, fg, on, off = self.look()
        sig = (W, H, fill, edge, fg, on, off)
        if sig == self.sig:
            return
        self.sig = sig
        cv.delete("all")
        radii = [min(r, H // 2, W // 2) for r in self.radii]
        cv.create_polygon(rounded(0, 0, W - 1, H - 1, radii), fill=edge, outline=edge)
        if e:
            cv.create_polygon(rounded(e, e, W - 1 - e, H - 1 - e, [max(0, r - e) for r in radii]), fill=fill, outline=fill)
        if self.deco:
            self.draw_deco(W, H, fill, on, off)
        self.tint(fill, fg)

    def tint(self, fill, fg):
        cls = self.w.winfo_class()
        o = {"bg": fill}
        if cls == "Label":
            o["fg"] = fg
        elif cls == "Entry":
            o.update(fg=fg, readonlybackground=fill, disabledbackground=fill, disabledforeground=fg, insertbackground=GOLD)
        elif cls not in ("Frame", "Text"):
            o.update(fg=fg, activebackground=fill, activeforeground=fg, disabledforeground=fg)
            if cls in ("Radiobutton", "Checkbutton"):
                o["selectcolor"] = fill
        self.set(**o)

    def draw_deco(self, W, H, fill, on, off):
        cv, s = self.cv, self.app.scale
        dim = (lambda c: blend("#66" + c[1:], self.surface)) if off else (lambda c: c)
        if self.deco == "chevron":  # the combo box arrow of Launcher.xaml: M0,0 L5,5 L10,0
            cx, cy, a = W - self.app.px(26), H / 2, 5 * s
            cv.create_line(cx - a, cy - a / 2, cx, cy + a / 2, cx + a, cy - a / 2, fill=dim(MUTED), width=max(1, round(2 * s)), capstyle="round", joinstyle="round")
        elif self.deco == "box":  # the rule toggle: a 20x20 box with a tick (M3,9 L7,13 L15,4)
            size, x, r, b = self.app.px(20), self.app.px(12), self.app.px(3), max(1, round(2 * s))
            y = (H - size) // 2
            cv.create_polygon(rounded(x, y, x + size, y + size, (r,) * 4), fill=dim(MUTED), outline=dim(MUTED))
            cv.create_polygon(rounded(x + b, y + b, x + size - b, y + size - b, (max(0, r - b),) * 4), fill=fill, outline=fill)
            if on:
                pts = [v for dx, dy in ((3, 9), (7, 13), (15, 4)) for v in (x + dx * s, y + dy * s)]
                cv.create_line(*pts, fill=dim(TEAL), width=max(1, round(2 * s)), capstyle="round", joinstyle="round")


if tk is not None:
    class Dot(tk.Canvas):
        """A round status light; configure(fg=...) recolours it like a Label's text."""

        def __init__(self, parent, size, color):
            super().__init__(parent, width=size, height=size, bg=parent["bg"], bd=0, highlightthickness=0)
            self.item = self.create_oval(0, 0, size - 1, size - 1, fill=color, outline=color)

        def configure(self, cnf=None, **kw):
            if "fg" in kw:
                self.itemconfigure(self.item, fill=kw["fg"], outline=kw.pop("fg"))
            return super().configure(cnf, **kw) if cnf or kw else None

        config = configure


# ---- window ----

class App:
    CEMU = os.path.join(HERE, "cemu")  # the Cemu folder whose graphic packs the Graphics page reads

    def __init__(self, root, cli=None, conf=None, join_conf=os.path.join(HERE, "join.conf"), size=None, server_log=None):
        self.root, self.cli, self.conf_path = root, cli or Cli(), conf or default_conf_path()
        self.log = server_log or ServerLog()
        self.own = load_conf(self.conf_path)
        set_language(choose_language(self.own.get("language")))
        self.presets = self.cli.presets()
        st = initial_state(self.own, load_conf(join_conf), self.presets)
        self.size = size or (root.winfo_screenwidth(), root.winfo_screenheight())
        self.design_w, self.design_h, self.scale = shell_fit(*self.size)
        self.bits, self.dialog, self.note, self.up, self.started, self.started_mode = st["bits"], None, None, False, None, LOCAL
        self.hidden, self.game_seen, self.capture = False, None, None
        self.tracker, self.tap = (gamepad.Tracker(), gamepad.Tap()) if gamepad else (None, None)
        self.pointer, self.hovered, self.memory = True, set(), {}  # pointer: False while the pad drives; memory: the focus kept per page
        root.bind_all("<Motion>", self.pointer_moved, add="+")
        self.gfx, self.hw, self.screen = display.read_prefs(self.own), None, None
        self.appid = os.environ.get("SteamAppId") if os.environ.get("XDG_CURRENT_DESKTOP") == "gamescope" else None  # only Game Mode needs the launcher to step aside
        self.tab, self.mode = tk.StringVar(root, st["tab"]), tk.StringVar(root, st["mode"])
        self.lang = tk.StringVar(root, LANG)
        self.game_langs = display.game_languages(os.path.dirname(self.CEMU))
        self.game_lang = tk.StringVar(root, display.pick_language(self.game_langs, self.own.get("game_language"))[0])
        self.v = {k: tk.StringVar(root, x) for k, x in st["values"].items()}
        self.last_addr, self.addr_msg, self.copied_until, self.semicolon_until, self.shown = "", False, 0, 0, None
        for key, var in self.v.items():
            var.trace_add("write", lambda *a, k=key: self.typed(k))
        self.fonts()
        self.build()
        self.bind_keys()
        root.protocol("WM_DELETE_WINDOW", self.quit)
        root.bind("<Destroy>", lambda e: e.widget is root and self.stop_timers())
        threading.Thread(target=self.cli.check, daemon=True).start() if self.cli.available() else None
        self.tick()

    @property
    def overlay(self):
        return self.dialog

    # ---- look ----
    def fonts(self):
        register_fonts()
        have = set(tkfont.families(self.root)) if tkfont else set()

        def pick(want, kind):
            return want if want in have else next((f for f in FALLBACK_FONTS[kind] if f in have), "TkDefaultFont")
        self.fam = {"menu": pick(MENU_FONT, "menu"), "title": pick(TITLE_FONT, "title"), "logo": pick(LOGO_FONT, "title"),
                    "mono": pick("", "mono"), "symbol": pick("", "symbol")}

    def f(self, size, kind="menu", style=""):
        """A Tk font for a size of Launcher.xaml (design px, so Windows sizes read the same here); kind is menu, title, logo, mono or symbol."""
        spec = (self.fam[kind], -max(8, round(size * self.scale)))
        return spec + (style,) if style else spec

    def px(self, n):
        return max(1, round(n * self.scale))

    def label(self, p, text="", fg=FG, size=18, kind="menu", style="", wrap=0, **kw):
        o = dict(bg=p["bg"], anchor="w", justify="left", wraplength=self.px(wrap) if wrap else 0)
        o.update(kw)
        return tk.Label(p, text=text, fg=fg, font=self.f(size, kind, style), **o)

    def button(self, p, text, cmd, tip=None, kind="Button", look="button", size=19, style="italic", fk="menu", padx=20, pady=10,
               left=None, right=None, **kw):
        """The default button of Launcher.xaml (italic 19, padding 20x10); look is the Skin kind: button, tab, start, toggle or combo."""
        cls = {"Button": tk.Button, "Radiobutton": tk.Radiobutton, "Checkbutton": tk.Checkbutton}[kind]
        minw, minh = kw.pop("minw", 0), kw.pop("minh", 0)
        o = dict(relief="flat", bd=0, highlightthickness=0, font=self.f(size, fk, style), padx=0, cursor="hand2", command=cmd)
        if kind != "Button":
            o["indicatoron"] = False
        o.update(kw)

        def make(parent):
            w = cls(parent, text=text, pady=0, **o)
            ref = tk.Label(parent, text=text, font=o["font"], bd=0, highlightthickness=0, padx=0, pady=0)
            w.update_idletasks()
            extra = max(0, w.winfo_reqheight() - ref.winfo_reqheight()) // 2  # Windows pads a Tk button by itself
            ref.destroy()
            w.configure(pady=max(0, self.px(pady) - extra))
            return w
        sk = Skin(self, p, make, look, left=padx if left is None else left, right=padx if right is None else right,
                  minw=minw, minh=minh, deco={"combo": "chevron", "toggle": "box"}.get(look), var=o.get("variable"))
        return self.tip(sk.w, tip)

    def entry(self, p, var, tip=None, size=16, style="", h=44, **kw):
        o = dict(textvariable=var, fg=FG, relief="flat", bd=0, highlightthickness=0, font=self.f(size, "menu", style), width=1)
        o.update(kw)
        e = Skin(self, p, lambda parent: tk.Entry(parent, **o), "entry", left=24, right=12, minh=h).w
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

    def panel(self, p, title, kind="card", pad=(24, 17), radius=8, titled=True):
        """A titled card of Launcher.xaml: the title centred above a rounded box; returns the box's content frame, laid out as the whole block.
        The block's `.slot` is a frame between the title and the box."""
        wrap = tk.Frame(p, bg=p["bg"])
        wrap.columnconfigure(0, weight=1)
        if titled and title:
            self.label(wrap, tr(title), fg=SECTION, size=15, kind="title", anchor="center").grid(row=0, column=0, pady=(0, self.px(10)))
        slot = tk.Frame(wrap, bg=p["bg"])
        slot.grid(row=1, column=0, sticky="ew")
        sk = Skin(self, wrap, lambda o: tk.Frame(o, bg=p["bg"]), kind, radius=radius)
        sk.outer.grid(row=2, column=0, sticky="nsew")
        wrap.rowconfigure(2, weight=1)
        body = tk.Frame(sk.w, bg=sk.w["bg"])
        body.pack(fill="both", expand=True, padx=self.px(pad[0]), pady=self.px(pad[1]))
        forward(body, wrap)
        body.slot = slot
        return body

    def combo(self, p, key, get, cmd):
        """A button that shows the current choice and opens the choice list (Windows has a combo box)."""
        return self.button(p, "", cmd, key, look="combo", size=17, padx=44, right=44, pady=6, anchor="center")

    def caption(self, p, text, tip=None, **kw):
        """A field label of Launcher.xaml: small grey text followed by the round info mark."""
        row = tk.Frame(p, bg=p["bg"])
        self.label(row, tr(text), fg=LABEL, size=14).pack(side="left")
        if tip:
            mark = tk.Canvas(row, width=self.px(16), height=self.px(16), bg=p["bg"], bd=0, highlightthickness=0)
            d = self.px(14)
            mark.create_oval(1, 1, d, d, outline=SOFT, width=max(1, self.px(1)))
            mark.create_text((d + 2) / 2, (d + 2) / 2, text="i", fill=SOFT, font=self.f(10, "menu", "bold"))
            mark.pack(side="left", padx=(self.px(6), 0))
            self.tip(mark, tip)
        return row

    def field(self, p, row, text, var, tip, **kw):
        """A caption over a text box, on rows 2*row and 2*row+1 of a one-column grid."""
        self.caption(p, text, tip).grid(row=2 * row, column=0, sticky="w")
        e = self.entry(p, var, tip, **kw)
        e.grid(row=2 * row + 1, column=0, sticky="ew", pady=(self.px(1), self.px(10)))
        return e

    def plate(self, p, kind, size, fg, text="", minh=0, pad=14, **kw):
        """A rounded, borderless strip of text (the status and hint bars)."""
        sk = Skin(self, p, lambda o: tk.Label(o, text=text, fg=fg, font=self.f(size), justify="center", **kw), kind, radius=5 if kind == "bar" else 6,
                  left=pad, right=pad, minh=minh, fg=fg)
        return sk.w

    # ---- layout ----
    def build(self):
        r = self.root
        for w in r.winfo_children():
            w.destroy()
        self.tips_on = True
        r.configure(bg=BG)
        px = self.px
        self.pagew = self.design_w - 28
        self.colw = (self.pagew - 27) // 2
        self.textw = self.colw - 56
        shell = tk.Frame(r, bg=BG)
        shell.pack(fill="both", expand=True, padx=max(0, (self.size[0] - round(self.design_w * self.scale)) // 2),
                   pady=max(0, (self.size[1] - round(self.design_h * self.scale)) // 2))
        head = tk.Frame(shell, bg=BG)
        head.pack(fill="x", padx=px(14), pady=(px(4), 0))
        head.columnconfigure((0, 2), weight=1, uniform="h")
        corner = tk.Frame(head, bg=BG)
        corner.grid(row=0, column=0, sticky="w")
        self.dot = Dot(corner, px(9), OFF)
        self.dot.pack(side="left")
        self.corner = self.label(corner, tr("Server stopped"), fg=MUTED, size=13)
        self.corner.pack(side="left", padx=(px(6), 0))
        self.label(head, "THE LEGEND OF ZELDA", fg="#CFBF9C", size=13, kind="title", anchor="center").grid(row=0, column=1)
        self.help_btn = self.button(head, tr("Help"), self.toggle_help, padx=15, pady=7)
        self.help_btn.grid(row=0, column=2, sticky="e")
        self.label(head, "BREATH OF THE WILD", fg="#E2D5B2", size=34, kind="logo", anchor="center").grid(row=1, column=0, columnspan=3, pady=(px(3), 0))
        self.label(head, tr("ONLINE MULTIPLAYER"), fg="#A7B5B5", size=14, anchor="center").grid(row=2, column=0, columnspan=3, pady=(0, px(6)))
        bar = tk.Frame(shell, bg=BG)
        bar.pack(fill="x", padx=px(14), pady=(0, px(6)))
        tabs = tk.Frame(bar, bg=BG)
        tabs.pack()
        self.badge(tabs, "LB", _k("Previous page (LB or Ctrl+Shift+Tab)"), (16, 5, 5, 5)).pack(side="left", padx=(0, px(14)))
        self.tab_btns = {n: self.button(tabs, tr(TAB_LABELS[n]), self.show_tab, kind="Radiobutton", variable=self.tab, value=n, look="tab", size=16,
                                        style="", fk="title", padx=16, pady=7) for n in TABS}
        for b in self.tab_btns.values():
            b.pack(side="left", padx=px(3))
        self.badge(tabs, "RB", _k("Next page (RB or Ctrl+Tab)"), (5, 16, 5, 5)).pack(side="left", padx=(px(14), 0))
        foot = tk.Frame(shell, bg=BG)
        foot.pack(side="bottom", padx=px(14), pady=(0, px(16)))
        self.start_btn = self.button(foot, tr("START SESSION"), self.start, look="start", size=20, style="", fk="title", padx=40, pady=6, minw=300, minh=63)
        self.start_mark = tk.PhotoImage(data=start_icon_png(self.scale))  # the controller's Start button presses this one
        self.start_btn.config(image=self.start_mark, compound="right")
        self.stop_btn = self.button(foot, tr("Stop session"), self.stop, padx=20, pady=6, minw=180, minh=63)
        self.return_btn = self.button(foot, tr("Return to game"), self.step_aside, padx=20, pady=6, minw=180, minh=63)
        self.exit_btn = self.button(foot, tr("Exit"), self.quit, padx=20, pady=6, minw=150, minh=63)
        for b in (self.start_btn, self.stop_btn, self.exit_btn):
            b.pack(side="left", padx=px(6))
        self.return_shown = False  # the "Return to game" button is packed only while a game runs behind the launcher
        self.status = self.plate(shell, "bar", 18, STATUS_FG, minh=44, wraplength=px(self.pagew - 60))
        self.status.pack(side="bottom", fill="x", padx=px(14), pady=(px(6), px(10)))
        self.hint = self.plate(shell, "hint", 14, MUTED, tr(DEFAULT_HINT), minh=36, pad=16, wraplength=px(self.pagew - 60))
        self.hint.pack(side="bottom", fill="x", padx=px(14))
        self.host = tk.Frame(shell, bg=BG)
        self.host.pack(fill="both", expand=True, padx=px(14), pady=(0, px(6)))
        self.pages = {n: tk.Frame(self.host, bg=BG) for n in TABS}
        self.gfx_btns, self.gfx_vars, self.gfx_toggles = {}, {}, {}  # the FPS box sits on Play, the rest on Graphics
        for name in TABS:
            getattr(self, "build_" + name.lower())(self.pages[name])
        self.help_open = False
        self.build_help()
        self.zones = [head, bar, self.host, foot]
        self.chrome = {self.help_btn, *self.tab_btns.values(), self.start_btn, self.stop_btn, self.return_btn, self.exit_btn}
        self.shown = None
        self.show_tab()
        self.show_mode()

    def badge(self, p, text, tip, radii):
        """The LB / RB mark beside the tabs."""
        sk = Skin(self, p, lambda o: tk.Label(o, text=text, fg=BADGE_FG, font=self.f(15, "menu", "bold")), "badge", radius=radii, left=0, right=0, minw=54,
                  minh=30, fg=BADGE_FG, sticky="")
        self.tip(sk.w, tip)
        return sk.w

    def build_play(self, page):
        px = self.px
        inner = (self.pagew - 30) * 9 // 20 - 40
        page.columnconfigure(0, weight=11, uniform="p")
        page.columnconfigure(1, weight=9, uniform="p")
        mine = self.panel(page, None, kind="p1", pad=(16, 10), titled=False)
        mine.grid(row=0, column=0, sticky="new", padx=(0, px(15)))
        mine.columnconfigure(0, weight=1)
        self.label(mine, tr("Player 1"), fg=PLAYER1, size=23, kind="title").grid(row=0, column=0, sticky="w", pady=(0, px(10)))
        self.label(mine, tr("NAME"), fg=SOFT, size=13).grid(row=1, column=0, sticky="w")
        self.entry(mine, self.v["name"], _k("Player 1 name"), h=40, style="italic", justify="center").grid(row=2, column=0, sticky="ew", pady=(px(4), px(6)))
        right = tk.Frame(page, bg=BG)
        right.grid(row=0, column=1, sticky="new")
        right.columnconfigure(0, weight=1)
        disp = self.panel(right, "DISPLAY", pad=(18, 12))
        disp.grid(row=0, column=0, sticky="ew")
        disp.columnconfigure(0, weight=1)
        self.screen_info = self.label(disp, "", size=23, anchor="center", justify="center", wrap=inner)
        self.screen_info.grid(row=0, column=0, sticky="ew", pady=(0, px(15)))
        self.preview = tk.Canvas(disp, bg=disp["bg"], bd=0, highlightthickness=0, width=px(360), height=px(202))
        self.preview.grid(row=1, column=0)
        self.layout_info = self.label(disp, "", fg=MUTED, size=18, anchor="center", justify="center", wrap=inner)
        self.layout_info.grid(row=2, column=0, sticky="ew", pady=(px(10), px(12)))
        fps = self.button(disp, "", lambda: self.pick_gfx("fps"), FPS_TIP, look="combo", padx=44, right=44, minw=380, anchor="center")
        fps.grid(row=3, column=0, pady=(0, px(4)))
        self.gfx_btns["fps"] = fps
        card = self.panel(right, "SESSION", pad=(18, 14))
        card.grid(row=1, column=0, sticky="ew", pady=(px(14), 0))
        card.columnconfigure(3, weight=1)
        self.dots = {}
        for i, (key, name) in enumerate((("server", _k("Dedicated server")), ("client", _k("Player 1 (Cemu A)")))):
            dot = Dot(card, px(12), OFF)
            dot.grid(row=i, column=0, sticky="w", pady=px(3))
            self.label(card, tr(name), size=16).grid(row=i, column=1, sticky="w", padx=(px(8), px(24)))
            word = self.label(card, tr("Stopped"), fg=BLURB, size=16)
            word.grid(row=i, column=2, sticky="w")
            self.dots[key] = (dot, word)
        self.promptbox = tk.Frame(card, bg=card["bg"])
        tk.Frame(self.promptbox, bg=AMBER, width=px(3)).pack(side="left", fill="y")
        self.prompt = self.label(self.promptbox, tr("Cemu A: wait for the main menu, then press Continue within about a minute."), fg=AMBER, size=15,
                                 wrap=inner * 45 // 100)
        self.prompt.pack(side="left", padx=(px(10), 0))
        self.promptbox.grid(row=0, column=3, rowspan=2, sticky="nsw", padx=(px(16), 0))
        self.online_box = self.panel(right, None, pad=(18, 10), titled=False)
        self.online_box.grid(row=2, column=0, sticky="ew", pady=(px(8), 0))
        self.online_box.columnconfigure(0, weight=1)
        self.online_text = self.label(self.online_box, "", fg=INFO, size=14, wrap=inner - 170)
        self.online_text.grid(row=0, column=0, sticky="w")
        self.copy_play = self.button(self.online_box, tr("Copy invite"), self.copy_invite, _k("Copies the invite. Your friends paste it in the Join a server mode of their launcher."),
                                     size=16, style="", padx=22, pady=9)
        self.copy_play.grid(row=0, column=1, sticky="e", padx=(px(18), 0))

    def build_online(self, page):
        px = self.px
        page.columnconfigure((0, 1), weight=1, uniform="c")
        left = self.panel(page, "ONLINE")
        left.grid(row=0, column=0, sticky="new", padx=(0, px(14)))
        left.columnconfigure(0, weight=1)
        self.label(left, tr("How this device takes part. Applied when the next session starts."), fg=BLURB, size=14, wrap=self.textw, justify="center").grid(row=0, column=0, pady=(0, px(11)))
        self.caption(left, "Online mode").grid(row=1, column=0, sticky="w")
        self.mode_btn = self.combo(left, _k("Play on this device keeps the server private. Host a server lets friends join over the network. Join a server connects to a friend's server."), None, self.pick_mode)
        self.mode_btn.grid(row=2, column=0, sticky="ew", pady=(px(2), px(5)))
        self.info = self.label(left, "", fg=INFO, size=13, wrap=self.textw)
        self.info.grid(row=3, column=0, sticky="w", pady=(0, px(8)))
        self.hostf, self.joinf = tk.Frame(left, bg=left["bg"]), tk.Frame(left, bg=left["bg"])
        for f in (self.hostf, self.joinf):
            f.columnconfigure(0, weight=1)
        self.field(self.hostf, 0, "Port", self.v["port"], _k("The TCP port the server listens on, from 1024 to 65535. It goes in the invite. The default is 5050."))
        self.field(self.hostf, 1, "Password", self.v["hostpass"], _k("Optional. Friends need it to join. Leave it empty for an open server."))
        self.semicolon = self.label(self.hostf, tr("A semicolon (;) is removed from the password."), fg=SOFT, size=13, wrap=self.textw)
        self.semicolon.grid(row=4, column=0, sticky="w", pady=(0, px(8)))
        self.semicolon.grid_remove()
        self.field(self.hostf, 3, "Server description", self.v["desc"], _k("The text friends see about this server."))
        self.field(self.joinf, 0, "Invite or address", self.v["invite"], _k("Paste the invite your friend sent you, or type their address and port."))
        self.label(self.joinf, tr("Example: hyrule://100.64.0.5:5050 or 192.168.1.20:5050"), fg=SOFT, size=13, wrap=self.textw).grid(row=2, column=0, sticky="w", pady=(0, px(8)))
        self.field(self.joinf, 3, "Password", self.v["joinpass"], _k("The server password, if the invite does not carry it."), show="")
        self.share = self.panel(page, "INVITE FRIENDS")
        self.share.columnconfigure(0, weight=1)
        self.addr_entry = self.field(self.share, 0, "Address in the invite", self.v["addr"], _k("The Tailscale or home network address of this device. Use your public address if you forward the port on your router. Empty it to detect the address again."))
        self.addr_entry.bind("<FocusOut>", lambda e: self.commit_addr(), add="+")
        self.caption(self.share, "Invite").grid(row=2, column=0, sticky="w")
        self.invite_box = self.entry(self.share, tk.StringVar(self.root), None, state="readonly", takefocus=0)
        self.invite_box.grid(row=3, column=0, sticky="ew", pady=(px(4), px(12)))
        self.copy_btn = self.button(self.share, tr("Copy invite"), self.copy_invite, _k("Copies the invite. Your friends paste it in the Join a server mode of their launcher."),
                                    size=16, style="", padx=22, pady=9)
        self.copy_btn.grid(row=4, column=0, sticky="ew")
        self.nethint = self.label(self.share, "", fg=SOFT, size=13, wrap=self.textw)
        self.nethint.grid(row=5, column=0, sticky="w", pady=(px(8), 0))
        self.joinhelp = self.panel(page, "JOINING A SERVER")
        self.label(self.joinhelp, tr("Ask the host for their invite and paste it here. You play on this device with one player. The host chooses the world rules and runs the server."), size=16, fg=BLURB, wrap=self.textw).grid(row=0, column=0, sticky="w")

    def build_world(self, page):
        px = self.px
        page.columnconfigure(0, weight=1, uniform="w")
        page.columnconfigure(1, weight=1, uniform="w")
        card = self.panel(page, "WORLD RULES", pad=(24, 12))
        card.grid(row=0, column=0, sticky="new", padx=(0, px(14)))
        card.columnconfigure(0, weight=1)
        self.label(card, tr("What everyone on the server shares. Applied when the next session starts."), fg=BLURB, size=14, wrap=self.textw, justify="center").grid(row=0, column=0, pady=(0, px(12)))
        self.lockbox = self.panel(page, None, titled=False, pad=(18, 14))
        self.locked = self.label(self.lockbox, tr("You joined a friend's server: the host sets the world rules, so these are locked."), fg=AMBER, size=14, wrap=self.textw - 30)
        self.locked.grid(row=0, column=0, sticky="w")
        self.caption(card, "Preset", _k("A ready-made set of sync rules. You can still change each rule below.")).grid(row=1, column=0, sticky="w")
        self.preset_btn = self.combo(card, _k("A ready-made set of sync rules. You can still change each rule below."), None, self.pick_preset)
        self.preset_btn.grid(row=2, column=0, sticky="ew", pady=(px(4), px(4)))
        self.preset_info = self.label(card, "", fg=INFO, size=13, wrap=self.textw)
        self.preset_info.grid(row=3, column=0, sticky="w")
        self.shares = self.label(card, "", size=13, fg=INFO, wrap=self.textw)
        self.shares.grid(row=4, column=0, sticky="w", pady=(px(2), px(8)))
        self.caption(card, "Special mode", _k("An optional game mode on top of the rules: Hunter vs Speedrunner or Death Swap.")).grid(row=5, column=0, sticky="w")
        self.special_btn = self.combo(card, _k("An optional game mode on top of the rules: Hunter vs Speedrunner or Death Swap."), None, self.pick_special)
        self.special_btn.grid(row=6, column=0, sticky="ew", pady=(px(4), px(4)))
        self.special_info = self.label(card, "", fg=INFO, size=13, wrap=self.textw)
        self.special_info.grid(row=7, column=0, sticky="w", pady=(0, px(8)))
        self.checks = []
        grid = tk.Frame(card, bg=card["bg"])
        grid.grid(row=8, column=0, sticky="ew")
        grid.columnconfigure((0, 1), weight=1, uniform="r")
        for i, name in enumerate(RULE_NAMES):
            var = tk.BooleanVar(self.root)
            c = self.button(grid, "", self.rules_changed, RULE_TIPS[i], kind="Checkbutton", variable=var, look="toggle", size=16, style="", padx=34, right=12,
                            pady=6, anchor="w", minh=41)
            c.grid(row=i // 2, column=i % 2, sticky="ew", padx=(0, px(8)), pady=(0, px(8)))
            self.checks.append((c, var))
        self.recommended = self.button(grid, tr("Use recommended rules"), self.use_recommended,
                                       _k("Selects the Game Completion preset with no special mode, the set that shares the most for playing the whole game together. It does not depend on this device's hardware."),
                                       size=16, style="", padx=22, pady=6, minh=43)
        self.recommended.grid(row=(len(RULE_NAMES) + 1) // 2, column=0, columnspan=2, sticky="ew", pady=(px(6), 0))

    def build_graphics(self, page):
        px = self.px
        page.columnconfigure(0, weight=1, uniform="w")
        page.columnconfigure(1, weight=1, uniform="w")
        self.vulkan = display.vulkan_available()
        lcol = tk.Frame(page, bg=BG)  # the GRAPHICS and MENU CONTROLS cards stack in the left column, as in the Windows launcher
        lcol.grid(row=0, column=0, sticky="new", padx=(0, px(14)))
        lcol.columnconfigure(0, weight=1)
        left = self.panel(lcol, "GRAPHICS", pad=(24, 12))
        left.grid(row=0, column=0, sticky="new")
        left.columnconfigure(0, weight=1)
        self.label(left, tr("Applies at the next session start."), fg=BLURB, size=14, wrap=self.textw, justify="center").grid(row=0, column=0, pady=(0, px(8)))
        for i, (key, caption, tip) in enumerate(GRAPHICS_ROWS):
            self.gfx_row(left, 1 + 2 * i, key, caption, tip)
        side = tk.Frame(page, bg=BG)
        side.grid(row=0, column=1, sticky="new", padx=(px(14), 0))
        side.columnconfigure(0, weight=1)
        right = self.panel(side, "ADVANCED (CEMU)", pad=(24, 12))
        right.grid(row=0, column=0, sticky="ew")
        right.columnconfigure((0, 1), weight=1, uniform="a")
        self.label(right, tr("The defaults suit most devices."), fg=BLURB, size=14, wrap=self.textw, justify="center").grid(row=0, column=0, columnspan=2, pady=(0, px(8)))
        self.gfx_row(right, 1, API_ROW[0], API_ROW[1], API_ROW[2], span=2)
        for col, (key, caption, tip) in enumerate(FILTER_ROWS):
            self.gfx_row(right, 3, key, caption, tip, column=col, pad=px(8) if col == 0 else 0)
        self.gfx_row(right, 5, VOLUME_ROW[0], VOLUME_ROW[1], VOLUME_ROW[2], span=2)
        grid = tk.Frame(right, bg=right["bg"])
        grid.grid(row=7, column=0, columnspan=2, sticky="ew")
        grid.columnconfigure((0, 1), weight=1, uniform="g")
        for i, (key, text, tip) in enumerate(GRAPHICS_TOGGLES):
            var = tk.BooleanVar(self.root)
            self.gfx_vars[key] = var
            c = self.button(grid, tr(text), lambda k=key: self.set_gfx({k: self.gfx_vars[k].get()}), tip, kind="Checkbutton", variable=var, look="toggle",
                            size=14, style="", padx=34, right=10, pady=6, anchor="w", minh=41)
            c.grid(row=i // 2, column=i % 2, sticky="ew", padx=(0, px(8)), pady=(0, px(8)))
            self.gfx_toggles[key] = c
        self.recommended_gfx = self.button(side, tr("Use recommended settings"), self.use_recommended_graphics, RECOMMENDED_TIP, size=16, style="", padx=22, pady=7, minh=43)
        self.recommended_gfx.grid(row=1, column=0, sticky="w", pady=(px(12), 0))  # below the card and left-aligned, as in the Windows launcher
        if gamepad:
            menu = self.panel(lcol, "MENU CONTROLS", pad=(24, 6))
            menu.grid(row=1, column=0, sticky="new", pady=(px(8), 0))
            menu.columnconfigure(0, weight=1)
            self.caption(menu, "Open this menu from a game", SHORTCUT_TIP).grid(row=0, column=0, sticky="w")
            self.shortcut_btn = self.combo(menu, None, None, self.pick_shortcut)
            self.shortcut_btn.grid(row=1, column=0, sticky="ew", pady=(px(4), px(4)))
            self.shortcut_change = self.button(menu, tr("Change shortcut"), self.begin_capture, SHORTCUT_TIP, size=16, style="", padx=22, pady=7, minh=43)
            self.shortcut_change.grid(row=2, column=0, sticky="w")
            self.shortcut_info = self.label(menu, "", fg=INFO, size=13, wrap=self.textw)
            self.shortcut_info.grid(row=3, column=0, sticky="w", pady=(px(2), 0))
            self.refresh_shortcut()
        self.refresh_graphics()

    def gfx_row(self, card, row, key, caption, tip, span=1, column=0, pad=0):
        """A caption over a choice button on rows `row` and `row + 1` of a card; the button opens the list of choices."""
        self.caption(card, caption, tip).grid(row=row, column=column, columnspan=span, sticky="w")
        b = self.combo(card, tip, None, lambda: self.pick_gfx(key))
        b.grid(row=row + 1, column=column, columnspan=span, sticky="ew", padx=(0, pad), pady=(self.px(4), self.px(8)))
        self.gfx_btns[key] = b

    def build_language(self, page):
        px = self.px
        page.columnconfigure(0, weight=1)
        card = self.panel(page, "LANGUAGE", pad=(28, 20))
        card.grid(row=0, column=0, sticky="n")
        tk.Frame(card, width=px(644), height=1, bg=card["bg"]).grid(row=0, column=0, columnspan=3)
        card.columnconfigure((0, 1, 2), weight=1, uniform="l")
        self.label(card, tr("LAUNCHER"), fg=SOFT, size=13).grid(row=1, column=0, columnspan=3, sticky="w", padx=px(2), pady=(0, px(6)))
        self.lang_btns = {}
        for i, (code, name) in enumerate(LANGUAGES):
            b = self.button(card, name, lambda c=code: self.pick_language(c), kind="Radiobutton", variable=self.lang, value=code, look="tab", size=18)
            b.grid(row=2, column=i, sticky="ew", padx=(0 if i == 0 else px(4), 0 if i == len(LANGUAGES) - 1 else px(4)))
            self.lang_btns[code] = b
        self.label(card, tr("The launcher text changes right away."), fg=BLURB, size=14, wrap=640).grid(row=3, column=0, columnspan=3, sticky="w", pady=(px(14), px(16)))
        self.label(card, tr("GAME"), fg=SOFT, size=13).grid(row=4, column=0, columnspan=3, sticky="w", padx=px(2), pady=(0, px(6)))
        games = tk.Frame(card, bg=card["bg"])
        games.grid(row=5, column=0, columnspan=3, sticky="ew")
        games.columnconfigure((0, 1), weight=1, uniform="g")
        self.game_btns = {}
        for i, (code, name, _) in enumerate(self.game_langs):
            b = self.button(games, name, lambda c=code: self.pick_game_language(c), kind="Radiobutton", variable=self.game_lang, value=code, look="tab", size=18)
            b.grid(row=i // 2, column=i % 2, sticky="ew", padx=(0, px(4)) if i % 2 == 0 else (px(4), 0), pady=(0, px(8)))
            self.game_btns[code] = b
        self.label(card, tr("The game applies it the next time a session starts. Text added by the mod itself may stay in English."), fg=BLURB, size=14,
                   wrap=640).grid(row=6, column=0, columnspan=3, sticky="w")

    def build_details(self, page):
        px = self.px
        page.columnconfigure(0, weight=1)
        page.rowconfigure(0, weight=1)
        self.texts = {}
        card = self.panel(page, None, kind="deep", pad=(18, 14), titled=False)
        card.grid(row=0, column=0, sticky="nsew")
        card.columnconfigure(0, weight=1)
        for col, (title, weight) in enumerate(((_k("Session log"), 3), (_k("Server console"), 2))):
            self.label(card, tr(title), fg=SECTION, size=14, anchor="center").grid(row=2 * col, column=0, pady=(px(14) if col else 0, px(8)))
            card.rowconfigure(2 * col + 1, weight=weight, uniform="d")
            box = tk.Text(card, bg=LOGBG, fg=LOGFG, font=self.f(13, "mono"), relief="flat", wrap="word", height=3, width=10, highlightthickness=px(2),
                          highlightbackground=LOGBG, highlightcolor=GOLD, padx=px(10), pady=px(8), state="disabled", cursor="arrow", insertwidth=0)
            self.scroll_keys(box)
            box.grid(row=2 * col + 1, column=0, sticky="nsew")
            self.texts[col] = [box, None]

    def build_help(self):
        sk = Skin(self, self.host, lambda o: tk.Text(o, fg="#C7DCDD", font=self.f(14), relief="flat", bd=0, highlightthickness=0, wrap="word", width=1, height=1,
                                                     padx=0, pady=self.px(2), cursor="arrow", insertwidth=0, spacing3=self.px(8)),
                  "help", radius=12, left=22, right=10, minh=0)
        box = sk.w
        box.tag_configure("head", foreground=SECTION, font=self.f(15, "title"))
        for section in HELP_SECTIONS:
            title, _, text = tr(section).partition(":")
            box.insert("end", title.strip() + "\n", "head")
            box.insert("end", text.strip() + "\n\n")
        box.config(state="disabled")
        self.scroll_keys(box)
        self.helpbox, self.help_text = sk.outer, box

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
        if name in ("Play", "Graphics"):
            self.refresh_graphics()
        if name in HINT_TABS:
            self.hint.pack(side="bottom", fill="x", padx=self.px(14), before=self.host)
        else:
            self.hint.pack_forget()
        self.remember("tab", name)

    def show_mode(self):
        m = self.mode.get()
        for f in (self.hostf, self.joinf, self.share, self.joinhelp):
            f.grid_remove()
        gap = {"sticky": "new", "padx": (self.px(13), 0)}
        if m == HOST:
            self.hostf.grid(row=4, column=0, sticky="ew")
            self.share.grid(row=0, column=1, **gap)
        elif m == JOIN:
            self.joinf.grid(row=4, column=0, sticky="ew")
            self.joinhelp.grid(row=0, column=1, **gap)
        self.info.config(text=tr(MODE_INFO[m]))
        self.mode_btn.config(text=tr(MODE_WORDS[m]))
        self.remember("mode", m)
        self.refresh_rules()
        self.refresh_invite()

    def show_page(self, name):
        self.tab.set(name)
        self.show_tab()
        self.root.update_idletasks()  # the page must be laid out before the cursor can be seated on it
        self.seat()

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

    def pick_game_language(self, code):
        self.remember("game_language", code)
        self.game_lang.set(code)
        self.game_btns[code].focus_set()

    def hide_semicolon(self):
        if self.semicolon.winfo_exists():
            self.semicolon.config(fg=SOFT)
            self.semicolon.grid_remove()

    # ---- online ----
    def typed(self, key):
        var = self.v[key]
        text = live_limit(key, var.get())
        if text != var.get():
            if key == "hostpass":
                self.semicolon_until = 1
                self.semicolon.config(fg=AMBER)
                self.semicolon.grid()
                self.root.after(3000, self.hide_semicolon)
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
            c.config(text=tr(RULE_NAMES[i]), state=state)
        name = rules_name(self.bits, self.presets)
        self.preset_btn.config(text=tr("Custom") if name == "Custom" else name, state=state)
        self.special_btn.config(text=tr(SPECIALS[int(self.bits[7])]), state=state)
        self.recommended.config(state=state)
        self.preset_info.config(text=tr(PRESET_BLURBS.get(name, PRESET_BLURBS["Custom"])))
        self.special_info.config(text=tr(SPECIAL_BLURBS[int(self.bits[7])]))
        self.shares.config(text=shares_text(self.bits))
        if locked:
            self.lockbox.grid(row=0, column=1, sticky="new", padx=(self.px(13), 0))
        else:
            self.lockbox.grid_remove()

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

    # ---- graphics ----
    def gfx_choices(self, key):
        """(values, labels, index of the current one) of a Graphics choice; no values means there is nothing to pick."""
        g, hz = self.gfx, self.screen[2] if self.screen else 0
        if key == "scale":
            values = list(display.SCALES)
            return values, [display.scale_label(s, tr) for s in values], values.index(g["scale"])
        if key in GFX_PACK:
            found = display.read_presets(self.CEMU, GFX_PACK[key])
            presets = [n for n, _ in found]
            current = display.choose(found, g[key], GFX_PACK[key])
            return presets, [display.label(n, tr) for n in presets], presets.index(current) if current in presets else 0
        if key == "fps":
            options = display.available_fps(display.fps_limits(self.CEMU), hz)
            best = display.best_fps(options, hz)
            if not best:
                return [], [], 0
            picked = display.pick_fps(options, g["fps"], hz)[0]
            values = [display.AUTO_FPS] + [n for n, _ in options]
            return values, [tr("FPS target: Auto · {0} FPS", best[0])] + [f"{n} FPS" for n, _ in options], 0 if g["fps"] == display.AUTO_FPS else values.index(picked)
        if key == "api":
            values = [a for a in display.APIS if a != "Vulkan" or self.vulkan]
            return values, [tr("Auto ({0})", "Vulkan" if self.vulkan else "OpenGL") if a == "Auto" else a for a in values], values.index(g["api"]) if g["api"] in values else 0
        if key == "volume":
            values = list(display.VOLUMES)
            return values, [tr("Muted") if v == 0 else f"{v}%" for v in values], values.index(g["volume"])
        values = list(range(len(display.FILTERS)))
        return values, list(display.FILTERS), g[key]

    def refresh_graphics(self):
        self.screen = display.detect_screen()
        w, h, hz = self.screen or (1920, 1080, 0)
        self.screen_info.config(text=f"{w} × {h}  /  {round(hz)} Hz" if hz > 1 else f"{w} × {h}")
        if w < 320 or h < 240 or not self.screen:
            self.layout_info.config(text=tr("The primary display is not ready."))
        else:
            view, _, fps, _ = display.plan(self.CEMU, self.own, self.screen)
            self.layout_info.config(text=tr("One player · the whole display") + "\n" + tr("Each game renders {0} × {1}", *view["render"]) + (" " + tr("at {0} FPS", fps[0]) if fps else ""))
        self.draw_preview(w / h)
        for key, b in self.gfx_btns.items():
            values, labels, current = self.gfx_choices(key)
            b.config(text=labels[current] if values else tr("FPS++ is not installed") if key == "fps" else "–", state="normal" if values else "disabled")
        for key, var in self.gfx_vars.items():
            var.set(self.gfx[key])
        values, _, current = self.gfx_choices("api")
        on_vulkan = values[current] == "Vulkan" or (values[current] == "Auto" and self.vulkan)
        self.gfx_toggles["barriers"].config(state="normal" if on_vulkan else "disabled")  # Vulkan only, as in the Windows launcher

    def draw_preview(self, ratio):
        """The one-player preview of Launcher.xaml: a dark frame in the display's shape with the game filling it."""
        px = self.px
        width = 118 if ratio < 1 else min(360, 210 * ratio)
        fw, fh = px(width), px(width / ratio)
        cv = self.preview
        cv.config(width=fw, height=fh)
        cv.delete("all")
        cv.create_polygon(rounded(0, 0, fw, fh, (px(6),) * 4), fill="#080E13", outline="")
        inset = px(9)  # the frame's padding of 6 and the cell's margin of 3
        cv.create_polygon(rounded(inset, inset, fw - inset, fh - inset, (px(3),) * 4), fill="#1E3C38", outline="")

    def pick_gfx(self, key):
        values, labels, current = self.gfx_choices(key)
        caption, tip = GFX_TEXT[key]

        def done(i):
            if i < len(values):
                self.set_gfx({key: values[i]})
        self.ask(tr(caption), tr(tip), labels + [tr("Cancel")], done, current)

    def set_gfx(self, updates):
        """Keeps the choices in launcher.conf (gfx_* keys) for display.py, which writes them to Cemu when a session starts."""
        self.gfx.update(updates)
        for key, value in updates.items():
            self.own["gfx_" + key] = ("1" if value else "0") if isinstance(value, bool) else str(value)
        save_conf(self.conf_path, self.own)
        self.refresh_graphics()

    def use_recommended_graphics(self):
        self.hw = self.hw or display.detect_hardware()
        self.set_gfx(display.recommended(self.CEMU, self.hw["tier"], self.screen))
        self.note = (_k("Recommended graphics settings applied ({0} class)."), (tr(display.TIERS[self.hw["tier"]]),))

    # ---- the menu shortcut: hold it on the pad to bring this launcher over the game (the Windows launcher's MenuShortcut) ----
    def shortcut(self):
        """The saved shortcut as a set of input names, or None when it is off."""
        return gamepad.parse_shortcut(self.own.get("shortcut", gamepad.SHORTCUT_DEFAULT))

    def shortcut_choices(self):
        saved = gamepad.format_shortcut(self.shortcut() or ())
        values = list(SHORTCUT_PRESETS) + ([saved] if saved and saved not in SHORTCUT_PRESETS else []) + ["Off"]
        return values, [tr("Off") if v == "Off" else v.replace("+", " + ") for v in values], (values.index(saved) if saved else len(values) - 1)

    def refresh_shortcut(self):
        if not gamepad:
            return
        values, labels, current = self.shortcut_choices()
        self.shortcut_btn.config(text=labels[current])
        names = self.shortcut()
        self.shortcut_info.config(text=tr("Hold {0} for 1.5 seconds to bring this menu over the game. Hold it again, or press {1}, to go back.",
                                          labels[current], "B") if names else tr("The menu shortcut is off."))

    def set_shortcut(self, value):
        self.remember("shortcut", value)
        self.tracker.update(set(), set(), 0)
        self.refresh_shortcut()

    def pick_shortcut(self):
        values, labels, current = self.shortcut_choices()

        def done(i):
            if i < len(values):
                self.set_shortcut(values[i])
        self.ask(tr("Open this menu from a game"), tr(SHORTCUT_TIP), labels + [tr("Cancel")], done, current)

    def begin_capture(self):
        self.capture = {"armed": False, "seen": set(), "end": time.monotonic() + CAPTURE_S}
        self.note = (_k("Hold the new shortcut on a controller (2 to 4 inputs), then let go. Tap {0} alone, or press nothing for 10 seconds, to cancel."), ("B",))

    def capture_step(self, held, now):
        """Windows CaptureStep: wait until every input is up, take the union of what is held next, and judge it when it is all released."""
        c = self.capture
        if now > c["end"]:
            self.capture, self.note = None, None
        elif not c["armed"]:
            c["armed"] = not held
        elif held:
            c["seen"] |= held
        elif c["seen"]:
            names = gamepad.parse_shortcut("+".join(c["seen"]))
            if c["seen"] == {"B"}:
                self.capture, self.note = None, None
            elif names:
                text = gamepad.format_shortcut(names)
                self.capture, self.note = None, (_k("Shortcut saved: {0}."), (text.replace("+", " + "),))
                self.set_shortcut(text)
            else:
                c["seen"], c["end"] = set(), now + CAPTURE_S
                self.note = (_k("That combination cannot be used: pick 2 to 4 inputs, and not both small centre buttons."), ())

    def pad_hold(self, pad):
        held, now = pad.held(), time.monotonic()
        if self.capture is not None:
            self.capture_step(held, now)
            return
        names = self.shortcut()
        if self.tracker.update(names or set(), held, now):
            pad.rumble()
            if self.hidden:
                self.bring_back()
            elif self.appid and self.game_seen:
                self.step_aside()
        if self.tap.update(held, "Start", bool(names and "Start" in names)) and not self.hidden and not self.dialog and not self.help_open and not self.up:
            self.start_btn.invoke()  # Windows: the Start button presses Start session; with a session running it would restart it, so only the button does

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
        self.follow_game(cli)
        show = bool(self.appid and self.game_seen and not self.hidden)
        if show != self.return_shown:
            self.return_shown = show
            if show:
                self.return_btn.pack(side="left", padx=self.px(6), before=self.exit_btn.box)
            else:
                self.return_btn.pack_forget()
        joined = s.get("joined") == "yes"
        words = {"server": tr("Running") if srv else tr("Starting") if alive else tr("Stopped"),
                 "client": tr("Joined the server") if cli and joined else tr("Waiting for Continue") if cli else tr("Waiting") if alive and srv else tr("Starting") if alive else tr("Stopped")}
        for key, on in (("server", srv), ("client", cli)):
            dot, word = self.dots[key]
            dot.config(fg=TEAL if on else OFF)
            word.config(text=words[key])
        self.dot.config(fg=TEAL if srv else OFF)
        self.corner.config(text=tr("Server running") if srv else tr("Server stopped"))
        (self.promptbox.grid if cli and not joined else self.promptbox.grid_remove)()
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
        names = self.log.roster.names() if srv and m != JOIN else []
        lines = [line]
        if m == HOST and names:
            lines.append(tr("In the world: {0}", ", ".join(names)))
        elif m == HOST and srv:
            lines.append(tr("Nobody has joined yet."))
        if srv and m == HOST and self.log.events:
            lines.append(event_text(self.log.events[-1]))
        self.online_text.config(text="\n".join(lines))
        (self.online_box.grid_remove if m == LOCAL else self.online_box.grid)()  # Windows hides the card when the server is private
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
        sk = Skin(self, ov, lambda o: tk.Frame(o, bg=BG), "popup", radius=8)
        sk.outer.place(relx=.5, rely=.5, anchor="center")
        box = tk.Frame(sk.w, bg=sk.w["bg"])
        box.pack(padx=self.px(28), pady=self.px(20))
        self.label(box, title, fg=GOLD, size=18, kind="title", anchor="center").pack(fill="x")
        if text:
            self.label(box, text, wrap=620, fg=BLURB, size=14, anchor="center", justify="center").pack(fill="x", pady=(self.px(6), self.px(12)))
        buttons = [self.button(box, t, lambda i=i: self.close_dialog(i), minw=360) for i, t in enumerate(labels)]
        for b in buttons:
            b.pack(fill="x", pady=self.px(3))
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
        boxes = [getattr(w, "box", w) for w in items]
        return [(w.winfo_rootx(), w.winfo_rooty(), w.winfo_width(), w.winfo_height()) for w in boxes]

    def pointer_moved(self, event):
        self.pointer = True

    def pad_drives(self):
        self.pointer = False
        for skin in list(self.hovered):
            try:
                skin.change(hover=False)
            except tk.TclError:  # destroyed by a rebuild
                self.hovered.discard(skin)

    def pad_items(self):
        """What the D-pad can reach: like the Windows launcher's cursor, never the tabs (LB and RB change page)."""
        tabs = set(self.tab_btns.values())
        return [w for w in self.focusables() if w not in tabs]

    def seat(self):
        """Where the cursor lands on a page (Windows: PadCursors.Seat): the spot it left last time, else the first control (the Start button
        on Play), so the focus mark never stays on a tab or a control of another page."""
        items = self.pad_items()
        if not items:
            return
        page = self.tab.get()
        pick = self.memory.get(page)
        if pick not in items:
            if page == TABS[0] and self.start_btn in items:
                pick = self.start_btn
            else:
                content = [w for w in items if w not in self.chrome] or items
                boxes = self.boxes(content)
                pick = content[min(range(len(content)), key=lambda i: (round(boxes[i][1] / 12), boxes[i][0]))]
        pick.focus_set()

    def nav(self, key):
        w = self.root.focus_get()
        tabs = set(self.tab_btns.values())
        items = self.focusables() if key in ("Tab", "BackTab") or w in tabs else self.pad_items()
        if not items:
            return "break"
        if w not in items:
            self.seat()
            return "break"
        if key in ("Tab", "BackTab"):
            pick = items[(items.index(w) + (1 if key == "Tab" else -1)) % len(items)]
        else:
            d = {"Up": 0, "Down": 1, "Left": 2, "Right": 3}.get(key, key)
            boxes = self.boxes(items)
            i = items.index(w)
            found = spatial_next(boxes[i], boxes, [x in self.chrome for x in items], w in self.chrome, d)
            pick = items[found] if found >= 0 else None
        if pick is not None:
            pick.focus_set()
            if pick not in tabs:
                self.memory[self.tab.get()] = pick
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

    # ---- the game window: gamescope shows the active window, so this one steps aside while Cemu runs ----
    def follow_game(self, cli):
        if self.game_seen is None:  # opened over a game that already runs: stay in front, the user came here on purpose
            self.game_seen = cli
        elif cli and not self.game_seen:
            self.game_seen = True
            self.root.after(GAME_DELAY_MS, self.step_aside)
        elif not cli:
            self.game_seen = False
            if self.hidden:
                self.bring_back()

    def step_aside(self):
        if not self.game_seen or self.hidden or not self.appid:
            return
        if not self.dialog and tag_game_windows(self.appid):  # hide only once gamescope can focus the game, or the screen would stay black
            self.hidden = True
            try:
                self.root.grab_release()
            except tk.TclError:
                pass
            self.root.withdraw()
            self.root.after(TAG_MS, self.keep_tagged)
        else:
            self.root.after(1000, self.step_aside)

    def keep_tagged(self):
        if self.hidden:
            tag_game_windows(self.appid)  # Cemu opens more windows as it goes
            self.root.after(TAG_MS, self.keep_tagged)

    def bring_back(self):
        self.hidden = False
        self.root.deiconify()
        self.root.lift()
        self.root.focus_force()
        try:
            self.root.grab_set()
        except tk.TclError:  # not viewable yet
            pass
        items = self.focusables()
        (self.start_btn if self.start_btn in items else items[0] if items else self.root).focus_set()

    # ---- game controller (gamepad.py reads the device; this maps its events onto the keyboard actions above) ----
    def pad_event(self, name):
        if self.hidden or self.capture is not None:
            return
        self.pad_drives()
        if name in ("up", "down", "left", "right"):
            self.nav(("Up", "Down", "Left", "Right")[("up", "down", "left", "right").index(name)])
        elif name == "accept":
            self.activate()
        elif name == "back":
            if self.appid and self.game_seen and not self.dialog and not self.help_open:
                self.step_aside()  # Windows: back on the menu returns to the game
            else:
                self.escape()
        elif name in ("lb", "rb"):
            self.cycle(-1 if name == "lb" else 1)

    def poll_pad(self, pad):
        try:
            for name in pad.poll():
                self.pad_event(name)
            self.pad_hold(pad)
        except Exception:  # a failing controller must never stop the window
            pass
        self.pad_job = self.root.after(16, self.poll_pad, pad)


def main():
    register_fonts()
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
    pad = None
    if gamepad is not None:
        try:
            pad = gamepad.Pad()
            app.poll_pad(pad)
        except Exception:
            pad = None

    def take_focus():
        root.lift()
        root.focus_force()
        try:
            root.grab_set()
        except tk.TclError:  # not viewable yet
            pass
        if root.focus_get() not in app.pad_items():
            app.seat()
    root.after(200, take_focus)
    root.after(800, take_focus)
    root.mainloop()
    if pad is not None:
        pad.close()


if __name__ == "__main__":
    main()
    sys.exit(0)
