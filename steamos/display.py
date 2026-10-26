#!/usr/bin/env python3
"""Display and graphics settings of the SteamOS launcher (standard library only).

Detects the screen and writes what Cemu needs before a session: the Hyrule Online Graphics pack (a copy of the stock
BOTW pack with two extra presets for the screen's shape and the render resolution) and the graphics part of
settings.xml. The same steps and numbers as CemuDisplay.cs, GraphicsMod.cs, FpsMod.cs, Hardware.cs and Layout.cs on
Windows, without the screen rotation and the two-player layouts, which this device does not have.

  display.py apply [DIR]   write the pack and settings.xml for the next session (DIR is the install folder)
  display.py info [DIR]    print the detected screen, the hardware class and what apply would use
"""
import glob
import math
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.realpath(__file__))
STOCK = os.path.join("graphicPacks", "downloadedGraphicPacks", "BreathOfTheWild", "Graphics")
FPS_PACK = os.path.join("graphicPacks", "downloadedGraphicPacks", "BreathOfTheWild", "Mods", "FPS++", "rules.txt")
PACK = os.path.join("graphicPacks", "HyruleOnline", "Graphics", "rules.txt")
DISPLAY_PRESET, RESOLUTION_PRESET = "Online Display", "Online Resolution"
SQUARE_HUD = ("Square HUD Mode", "Enabled (Experimental, Will Have Some Issues)")
SHADOWS, SHADOW_DISTANCE, ANTI_ALIASING = "Shadows", "Shadow Draw Distance", "Anti-Aliasing"
SCALES = (0.5, 0.75, 1.0, 1.25, 1.5, 2.0)
APIS = ("Auto", "Vulkan", "OpenGL")
FILTERS = ("Bilinear", "Bicubic", "Bicubic Hermite", "Nearest neighbor")  # Cemu's numbers 0 to 3
VOLUMES = (0, 10, 25, 50, 75, 100)
TIERS = ("Basic", "Balanced", "High", "Ultra")
AUTO_FPS, MIN_FPS, MAX_FPS, DEFAULT_FPS = 0, 30, 144, 60
MAX_RENDER_SIDE = 8192
# What the pack presets are called when nothing was chosen; they are the values install.sh has always written.
DEFAULT_WORDS = {SHADOWS: "High", SHADOW_DISTANCE: "Very High", ANTI_ALIASING: "Normal FXAA"}
DEFAULTS = {"scale": 1.0, "fps": AUTO_FPS, "shadows": "", "shadow_distance": "", "aa": "", "api": "Auto", "upscale": 1, "downscale": 0,
            "volume": 100, "vsync": False, "async": True, "barriers": False, "showfps": True, "usage": False, "shader": True}
BOOLS = ("vsync", "async", "barriers", "showfps", "usage", "shader")


# ---- the screen ----
def parse_xrandr(text):
    """(width, height, hz) of the primary output's current mode, else of the first connected one; None when there is none."""
    found, primary, current = [], False, False
    for line in text.splitlines():
        head = re.match(r"^(\S+) (connected|disconnected)( primary)?", line)
        if head:
            current, primary = head.group(2) == "connected", bool(head.group(3))
            continue
        mode = re.match(r"^\s+(\d+)x(\d+)\S*\s+(.*)$", line)
        rate = mode and re.search(r"(\d+(?:\.\d+)?)\*", mode.group(3))
        if current and rate:
            found.append((not primary, int(mode.group(1)), int(mode.group(2)), round(float(rate.group(1)))))
    return min(found)[1:] if found else None


def drm_screen(root="/sys/class/drm"):
    """The panel's preferred mode when no X display answers; the refresh rate is not known then (0)."""
    for status in sorted(glob.glob(os.path.join(root, "card*-*", "status"))):
        try:
            with open(status, encoding="utf-8") as f:
                connected = f.read().strip() == "connected"
            with open(os.path.join(os.path.dirname(status), "modes"), encoding="utf-8") as f:
                mode = re.match(r"(\d+)x(\d+)", f.readline())
        except OSError:
            continue
        if connected and mode:
            return int(mode.group(1)), int(mode.group(2)), 0
    return None


def detect_screen(env=None, run=subprocess.run):
    """In Game Mode the launcher sits on gamescope's game display (:1, larger than the panel), while Steam's own display (:0)
    is the real panel, so :0 is asked first. Returns (width, height, hz) with hz 0 when unknown, or None."""
    env = dict(os.environ if env is None else env)
    for name in dict.fromkeys([":0", env.get("DISPLAY", "")]):
        if not name:
            continue
        try:
            out = run(["xrandr", "--current"], env={**env, "DISPLAY": name}, capture_output=True, text=True, timeout=4).stdout
        except (OSError, subprocess.SubprocessError):
            continue
        screen = parse_xrandr(out or "")
        if screen:
            return screen
    return drm_screen()


# ---- what the game renders ----
def even(value):
    return max(2, int(math.floor(value / 2 + 1e-9)) * 2)


def render_size(width, height, scale):
    fit = min(1.0, MAX_RENDER_SIDE / max(width, height) / scale)
    return even(width * scale * fit), even(height * scale * fit)


def aspect_ratio(width, height):
    """The reduced ratio the BOTW pack gets, kept small enough for it."""
    g = math.gcd(width, height)
    rw, rh = width // g, height // g
    largest = max(rw, rh)
    if largest <= 1000:
        return rw, rh
    return max(1, round(rw * 1000 / largest)), max(1, round(rh * 1000 / largest))


def make_view(width, height, scale):
    rw, rh = render_size(width, height, scale)
    ratio = rw / rh
    return {"render": (rw, rh), "ratio": aspect_ratio(rw, rh), "ultrawide": ratio > 16 / 9 + 0.01, "square": ratio <= 1.51}


def nearest_scale(value):
    try:
        value = float(value)
    except (TypeError, ValueError):
        return 1.0
    return 1.0 if math.isnan(value) or math.isinf(value) else min(SCALES, key=lambda s: abs(s - value))


def scale_label(scale, t=lambda s: s):
    percent = f"{round(scale * 100)}%"
    return percent + " " + t("(window size)" if scale == 1.0 else "(sharper)" if scale > 1.0 else "(faster)")


def build_rules(stock, view):
    """The stock Graphics pack renamed, with the Online Display and Online Resolution presets added."""
    eol = "\r\n" if "\r\n" in stock else "\n"
    rules = re.sub(r"(?m)^name = Graphics[ \t]*(?=\r?$)", "name = Graphics (online display)", stock)
    rules = re.sub(r'(?m)^(path = ".*)/Graphics"', rf'\1/{DISPLAY_PRESET}"', rules)
    if rules == stock or "[Definition]" not in rules:
        raise ValueError("The BOTW Graphics pack has an unexpected format.")
    rw, rh = view["ratio"]
    lines = ["", "# Written by the Hyrule Online launcher for this screen.", "[Preset]", f"name = {DISPLAY_PRESET}", "category = Aspect Ratio",
             f"$aspectRatioWidth = {rw}", f"$aspectRatioHeight = {rh}", f"$showUltrawideOptions:int = {int(view['ultrawide'])}",
             f"$showSquareHUDOption:int = {int(view['square'])}"]
    if not view["ultrawide"]:
        lines.append("$ultrawideHUDMode:int = 0")
    lines += ["", "[Preset]", f"name = {RESOLUTION_PRESET}", "category = Resolution", f"$width = {view['render'][0]}", f"$height = {view['render'][1]}"]
    return rules.rstrip() + eol + eol.join(lines) + eol


# ---- the pack's own choices ----
def parse_presets(rules, category):
    """[(name, is_default)] of the pack's [Preset] blocks of one category."""
    out = []
    for block in re.split(r"(?m)^\[Preset\][ \t]*\r?$", rules or "")[1:]:
        name = re.search(r"(?m)^name[ \t]*=[ \t]*(.+?)[ \t]*\r?$", block)
        kind = re.search(r"(?m)^category[ \t]*=[ \t]*(.+?)[ \t]*\r?$", block)
        if name and kind and kind.group(1) == category and all(n != name.group(1) for n, _ in out):
            out.append((name.group(1), bool(re.search(r"(?m)^default[ \t]*=[ \t]*1[ \t]*\r?$", block))))
    return out


def label(name, t=lambda s: s):
    """The name and the facts worth a glance (percentages and flags): "Low (Low Draw Distance, Sharp Shadows)" reads "Low"."""
    open_at = name.find(" (")
    if open_at < 0 or not name.endswith(")"):
        return t(name)
    keep = [p.strip() for p in name[open_at + 2:-1].split(",")]
    keep = [p for p in keep if re.match(r"^\d+%$", p) or re.match(r"^(Default|Recommended|Unstable|Experimental)\b", p)]
    word = t(name[:open_at])
    return word + " (" + ", ".join(p if p.endswith("%") else t(p) for p in keep) + ")" if keep else word


def pick_preset(choices, wanted):
    """The preset called exactly the wanted word (before any "(...)" note), else the pack's default, else its first; None without choices."""
    found = next((n for n, _ in choices if n == wanted or n.startswith(wanted + " ")), None) or next((n for n, d in choices if d), None)
    return found or (choices[0][0] if choices else None)


def choose(choices, saved, category):
    """The saved preset when the pack still has it, else the default word for the category."""
    return saved if any(n == saved for n, _ in choices) else pick_preset(choices, DEFAULT_WORDS[category])


def read_file(path):
    try:
        with open(path, encoding="utf-8", newline="") as f:
            return f.read()
    except OSError:
        return ""


def read_presets(folder, category):
    return parse_presets(read_file(os.path.join(folder, STOCK, "rules.txt")), category)


# ---- the FPS++ pack ----
def fps_limits(folder):
    """[(fps, preset name)] sorted, from the FPS++ "FPS Limit" presets of the Normal Settings."""
    found = {}
    for block in re.split(r"(?m)^\[Preset\][ \t]*\r?$", read_file(os.path.join(folder, FPS_PACK))):
        name = re.search(r"(?m)^name[ \t]*=[ \t]*(.+?)[ \t]*\r?$", block)
        kind = re.search(r"(?m)^category[ \t]*=[ \t]*(.+?)[ \t]*\r?$", block)
        fps = re.search(r"(?m)^\$fpsLimitNormal[ \t]*=[ \t]*(\d+)[ \t]*\r?$", block)
        if name and kind and fps and kind.group(1) == "FPS Limit" and int(fps.group(1)) > 0:
            found.setdefault(int(fps.group(1)), name.group(1))
    return sorted(found.items())


def available_fps(limits, hz):
    """Between 30 and 144, never above the display (one Hz of slack for 59.94 style rates); 60 is assumed when the rate is unknown."""
    cap = min(MAX_FPS, hz + 1 if hz > 1 else DEFAULT_FPS)
    fit = [l for l in limits if MIN_FPS <= l[0] <= cap]
    return fit or [l for l in limits if l[0] >= MIN_FPS][:1]


def best_fps(options, hz):
    """The highest rate that divides the refresh rate evenly (and is 60 or more), else the highest offered."""
    ordered = sorted(options, reverse=True)
    if hz > 1:
        even_rate = next((l for l in ordered if l[0] >= 60 and abs(hz - l[0] * round(hz / l[0])) <= 1), None)
        if even_rate:
            return even_rate
    return ordered[0] if ordered else None


def pick_fps(options, wanted, hz):
    """The wanted rate when offered, else the closest lower one (or the lowest); Auto means best_fps."""
    if wanted == AUTO_FPS or not options:
        return best_fps(options, hz)
    return max((l for l in options if l[0] <= wanted), default=None) or min(options)


# ---- the hardware class ----
def raw_tier(n):
    m = re.search(r"\bRTX\s*(?:A|PRO\s*)?(\d{4})\b", n)
    if m:
        tens = int(m.group(1)) % 100
        return 3 if tens >= 70 else 2 if tens >= 60 else 1
    m = re.search(r"\bGTX\s*(\d{3,4})\b", n)
    if m:
        number = int(m.group(1))
        if number >= 1600:
            return 2 if number >= 1660 else 1
        if number >= 1000:
            return 2 if number >= 1070 else 1 if number >= 1060 or (number == 1050 and " TI" in n) else 0
        return 1 if number in (970, 980) else 0
    if re.search(r"\b(GT|MX)\s*\d{3,4}\b", n):
        return 0
    m = re.search(r"\bRX\s*(\d{4})(?!\d)", n)
    if m:
        number = int(m.group(1))
        series, level = number // 1000, number // 100 % 10
        if series >= 9:
            return 3 if number >= 9070 else 2
        if series >= 6:
            return 3 if level >= 7 else 2 if level >= 6 else 1 if level >= 4 else 0
        return (2 if level >= 7 else 1 if level >= 5 else 0) if series == 5 else 0
    m = re.search(r"\bRX\s*(\d{3})(?!\d)", n)
    if m:
        return 1 if int(m.group(1)) >= 570 else 0
    if re.search(r"\bRX\s*VEGA\s*(56|64)\b", n):
        return 2
    m = re.search(r"\bARC\b.*?\b[AB](\d{3})\b", n)
    if m:
        return 2 if int(m.group(1)) >= 570 else 1
    if "ARC" in n:
        return 1
    m = re.search(r"\bRADEON\s*(\d{3})M\b", n)
    if m:
        return 1 if int(m.group(1)) >= 780 else 0
    if re.search(r"\b(UHD|HD GRAPHICS|IRIS|VEGA|RADEON(\(TM\))? GRAPHICS|RADEON PRO)\b", n):
        return 0
    return 1  # unknown cards, the Steam Deck's VanGogh among them, count as balanced


def gpu_tier(name):
    n = (name or "").upper()
    tier = raw_tier(n)
    return tier - 1 if tier >= 2 and any(w in n for w in ("LAPTOP", "MAX-Q", "MOBILE")) else tier


def hardware_tier(gpu, vram_mb, threads, ram_gb):
    tier = gpu_tier(gpu)
    if 0 < vram_mb < 2048:
        tier = 0
    elif 0 < vram_mb < 4096:
        tier = min(tier, 1)
    if 0 < threads < 4 or 0 < ram_gb < 12:
        tier = min(tier, 1)
    return tier


def detect_hardware(run=subprocess.run):
    """{"gpu", "threads", "ram", "tier"}: the best graphics adapter lspci names, the CPU threads and the RAM in GB."""
    info = {"gpu": "", "threads": os.cpu_count() or 0, "ram": 0.0}
    try:
        for line in read_file("/proc/meminfo").splitlines():
            if line.startswith("MemTotal:"):
                info["ram"] = round(int(line.split()[1]) / 1048576, 1)
    except (ValueError, IndexError):
        pass
    try:
        out = run(["lspci"], capture_output=True, text=True, timeout=4).stdout
    except (OSError, subprocess.SubprocessError):
        out = ""
    names = [l.split(": ", 1)[1].strip() for l in out.splitlines() if re.search(r"VGA|3D|Display", l) and ": " in l]
    info["gpu"] = max(names, key=gpu_tier, default="")
    info["tier"] = hardware_tier(info["gpu"], 0, info["threads"], info["ram"])
    return info


def recommended_scale(tier, width, height):
    """The sharpest offered scale up to 100% that this class of card should manage with one game."""
    budget = (1.0e6, 2.1e6, 4.5e6, 8.3e6)[max(0, min(3, tier))]
    fits = [s for s in SCALES if s <= 1.0 and width * height * s * s <= budget]
    return max(fits) if fits else min(SCALES)


def recommended(folder, tier, screen):
    """The graphics values that suit this class of device; the on-screen options and the volume are left as they are."""
    width, height = screen[:2] if screen else (1920, 1080)
    def pick(category, word):
        try:
            return pick_preset(read_presets(folder, category), word) or ""
        except OSError:
            return ""
    return {"scale": recommended_scale(tier, width, height), "fps": AUTO_FPS, "api": "Auto", "vsync": False, "async": True, "barriers": False,
            "upscale": 0 if tier == 0 else 1, "downscale": 0, "shadows": pick(SHADOWS, ("Low", "Medium", "High", "High")[tier]),
            "shadow_distance": pick(SHADOW_DISTANCE, ("Medium", "High", "Very High", "Very High")[tier]), "aa": pick(ANTI_ALIASING, "Normal FXAA")}


# ---- the saved choices ----
def conf_path(env=None):
    env = os.environ if env is None else env
    return env.get("HYRULE_LAUNCHER_CONF") or os.path.join(env.get("XDG_CONFIG_HOME") or os.path.join(os.path.expanduser("~"), ".config"),
                                                           "hyrule-online", "launcher.conf")


def read_prefs(own):
    """The graphics choices (gfx_* keys of launcher.conf) with every missing or odd value replaced by its default."""
    prefs = dict(DEFAULTS)
    def number(key, wanted):
        try:
            return int(own.get("gfx_" + key, ""))
        except ValueError:
            return wanted
    prefs["scale"] = nearest_scale(own.get("gfx_scale"))
    prefs["fps"] = max(AUTO_FPS, number("fps", AUTO_FPS))
    prefs["api"] = own.get("gfx_api") if own.get("gfx_api") in APIS else "Auto"
    for key in ("upscale", "downscale"):
        value = number(key, DEFAULTS[key])
        prefs[key] = value if 0 <= value < len(FILTERS) else DEFAULTS[key]
    prefs["volume"] = min(VOLUMES, key=lambda v: abs(v - number("volume", DEFAULTS["volume"])))
    for key in BOOLS:
        if own.get("gfx_" + key) in ("0", "1"):
            prefs[key] = own["gfx_" + key] == "1"
    for key in ("shadows", "shadow_distance", "aa"):
        prefs[key] = own.get("gfx_" + key, "")
    return prefs


def load_own(path):
    own = {}
    try:
        with open(path, encoding="utf-8") as f:
            for line in f.read().splitlines():
                key, sep, value = line.partition("=")
                if sep and key not in own:
                    own[key] = value
    except OSError:
        pass
    return own


def vulkan_available():
    return any(glob.glob(os.path.join(d, "*.json")) for d in ("/usr/share/vulkan/icd.d", "/etc/vulkan/icd.d", "/usr/local/share/vulkan/icd.d"))


# ---- the game language ----
CONSOLES = {"ja": 0, "en": 1, "fr": 2, "de": 3, "it": 4, "es": 5, "zh": 6, "ko": 7, "nl": 8, "pt": 9, "ru": 10}  # Cemu's console_language numbers
TRADITIONAL_CHINESE = 11
LANGUAGE_NAMES = {"en": "English", "es": "Español", "fr": "Français", "de": "Deutsch", "it": "Italiano", "ja": "日本語", "zh": "中文", "ko": "한국어",
                  "nl": "Nederlands", "pt": "Português", "ru": "Русский"}
REGION_NAMES = {"USes": "Español (Latinoamérica)", "USfr": "Français (Canada)", "EUes": "Español (España)", "TWzh": "中文 (繁體)"}  # the same language differs by region


def game_languages(folder):
    """The languages the game can show, as (code, name, console number): one Bootup_<region><language>.pack per language in FOLDER/game/content/Pack.
    English first, then the others by code; without a readable game only English is offered."""
    found = {}
    for file in glob.glob(os.path.join(folder, "game", "content", "Pack", "Bootup_*.pack")):
        match = re.fullmatch(r"Bootup_([A-Z]{2})([a-z]{2})\.pack", os.path.basename(file))
        if match and match[2] in CONSOLES and match[2] not in found:
            region, code = match.groups()
            found[code] = (code, REGION_NAMES.get(region + code, LANGUAGE_NAMES[code]), TRADITIONAL_CHINESE if (region, code) == ("TW", "zh") else CONSOLES[code])
    if not found:
        found["en"] = ("en", "English", CONSOLES["en"])
    return sorted(found.values(), key=lambda item: (item[0] != "en", item[0]))


def pick_language(languages, wanted):
    return next((item for item in languages if item[0] == wanted), languages[0])


# ---- settings.xml ----
def norm(path):
    return re.sub(r"\\+", r"\\", path.replace("/", "\\")).lower()


def child(parent, tag):
    node = parent.find(tag)
    return ET.SubElement(parent, tag) if node is None else node


def put(parent, tag, value):
    child(parent, tag).text = str(value)


def entry_for(packs, filename):
    for e in packs.findall("Entry"):
        if norm(e.get("filename", "")) == norm(filename):
            return e
    return ET.SubElement(packs, "Entry", filename=filename)


def set_preset(entry, category, name):
    for p in entry.findall("Preset"):
        if p.findtext("category") == category:
            child(p, "preset").text = name
            return
    p = ET.SubElement(entry, "Preset")
    ET.SubElement(p, "category").text = category
    ET.SubElement(p, "preset").text = name


def write_settings(path, view, prefs, fps_preset, vulkan, console=CONSOLES["en"]):
    tree = ET.parse(path)
    content = tree.getroot()
    packs = child(content, "GraphicPack")
    ours = PACK.replace(os.sep, "\\")
    for e in list(packs.findall("Entry")):
        # Two Graphics packs would patch the same game code, so the stock one must not stay enabled.
        if norm(e.get("filename", "")).endswith("breathofthewild\\graphics\\rules.txt"):
            packs.remove(e)
    entry = entry_for(packs, ours)
    for p in entry.findall("Preset"):  # rebuilt from scratch, as the Windows launcher does, so no stale preset survives
        entry.remove(p)
    set_preset(entry, "Aspect Ratio", DISPLAY_PRESET)
    set_preset(entry, "Resolution", RESOLUTION_PRESET)
    if view["square"]:
        set_preset(entry, *SQUARE_HUD)
    for category, key in ((SHADOWS, "shadows"), (SHADOW_DISTANCE, "shadow_distance"), (ANTI_ALIASING, "aa")):
        if prefs[key]:
            set_preset(entry, category, prefs[key])
    if fps_preset:
        fps = entry_for(packs, FPS_PACK.replace(os.sep, "\\"))
        for p in fps.findall("Preset"):
            fps.remove(p)
        set_preset(fps, "Mode", "Normal Settings")
        set_preset(fps, "FPS Limit", fps_preset)
    put(content, "fullscreen", "true")
    put(content, "console_language", console)
    graphic = child(content, "Graphic")
    put(graphic, "FullscreenScaling", 1)
    put(graphic, "api", 0 if prefs["api"] == "OpenGL" or not vulkan else 1)
    put(graphic, "VSync", int(prefs["vsync"]))
    for tag, key in (("AsyncCompile", "async"), ("VulkanAccurateBarriers", "barriers")):
        put(graphic, tag, "true" if prefs[key] else "false")
    put(graphic, "UpscaleFilter", prefs["upscale"])
    put(graphic, "DownscaleFilter", prefs["downscale"])
    overlay = child(graphic, "Overlay")
    put(overlay, "FPS", "true" if prefs["showfps"] else "false")
    for tag in ("CPUUsage", "RAMUsage", "VRAMUsage"):
        put(overlay, tag, "true" if prefs["usage"] else "false")
    put(child(graphic, "Notification"), "ShaderCompiling", "true" if prefs["shader"] else "false")
    put(child(content, "Audio"), "TVVolume", prefs["volume"])
    if hasattr(ET, "indent"):
        ET.indent(tree, "    ")
    tmp = path + ".tmp"
    tree.write(tmp, encoding="utf-8", xml_declaration=True)
    os.replace(tmp, path)


def plan(folder, own, screen):
    """What apply writes: the view, the saved choices resolved against the pack, the FPS pick and a one-line summary."""
    prefs = read_prefs(own)
    width, height, hz = screen or (1920, 1080, 0)
    view = make_view(width, height, prefs["scale"])
    for category, key in ((SHADOWS, "shadows"), (SHADOW_DISTANCE, "shadow_distance"), (ANTI_ALIASING, "aa")):
        prefs[key] = choose(read_presets(folder, category), prefs[key], category) or ""
    fps = pick_fps(available_fps(fps_limits(folder), hz), prefs["fps"], hz)
    summary = f"Display {width}x{height}" + (f" at {hz} Hz" if hz > 1 else "") + f": rendering {view['render'][0]}x{view['render'][1]}" + (f" at {fps[0]} FPS" if fps else "")
    return view, prefs, fps, summary


def apply(folder, conf=None, screen=None, vulkan=None):
    """Writes the pack and settings.xml of the Cemu in FOLDER; returns the summary line. Raises when a file is missing or odd."""
    cemu = os.path.join(folder, "cemu")
    stock = os.path.join(cemu, STOCK)
    settings = os.path.join(cemu, "settings.xml")
    if not os.path.isfile(os.path.join(stock, "rules.txt")):
        raise FileNotFoundError(f"The BOTW Graphics pack is missing from {cemu}.")
    if not os.path.isfile(settings):
        raise FileNotFoundError(f"Cemu settings are missing from {cemu}.")
    screen = detect_screen() if screen is None else screen
    own = load_own(conf or conf_path())
    view, prefs, fps, summary = plan(cemu, own, screen)
    console = pick_language(game_languages(folder), own.get("game_language"))[2]
    pack = os.path.join(cemu, PACK)
    os.makedirs(os.path.dirname(pack), exist_ok=True)
    for name in os.listdir(stock):
        target = os.path.join(os.path.dirname(pack), name)
        if name.lower() != "rules.txt" and not os.path.exists(target) and os.path.isfile(os.path.join(stock, name)):
            with open(os.path.join(stock, name), "rb") as src, open(target, "wb") as dst:
                dst.write(src.read())
    with open(pack, "w", encoding="utf-8", newline="") as f:
        f.write(build_rules(read_file(os.path.join(stock, "rules.txt")), view))
    write_settings(settings, view, prefs, fps[1] if fps else None, vulkan_available() if vulkan is None else vulkan, console)
    return summary


def main(argv):
    command = argv[1] if len(argv) > 1 else ""
    folder = os.path.abspath(argv[2]) if len(argv) > 2 else HERE
    if command == "apply":
        try:
            print(apply(folder))
        except Exception as error:  # the hook is best effort: whatever goes wrong must not stop the session
            print(f"Display settings were not applied: {error}")
            return 1
        return 0
    if command == "info":
        screen, hardware = detect_screen(), detect_hardware()
        print("screen:", screen, "| gpu:", hardware["gpu"] or "unknown", "| class:", TIERS[hardware["tier"]], f"| {hardware['threads']} threads, {hardware['ram']} GB")
        print(plan(os.path.join(folder, "cemu"), load_own(conf_path()), screen)[3])
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
