"""Tests for launcher.py. Run: python3 steamos/launcher_test.py
The logic tests need no display and no Ally. The window tests build every page when tkinter and a display (for example Xvfb) exist, and skip otherwise.
The Windows sources (src/Launcher) are compared when this checkout has them."""
import ast
import json
import os
import re
import sys
import tempfile
import time
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from unittest import mock
import launcher as L

WINDOWS = os.path.join(os.path.dirname(HERE), "src", "Launcher")


def used_keys():
    """Every English text the window passes through tr() or marks with _k(), plus the field and panel titles and the rule and special names."""
    with open(os.path.join(HERE, "launcher.py"), encoding="utf-8") as f:
        tree = ast.parse(f.read())
    keys = []

    def add(node):
        if isinstance(node, ast.Constant) and isinstance(node.value, str) and node.value not in keys:
            keys.append(node.value)
    for n in ast.walk(tree):
        if isinstance(n, ast.Call):
            name = n.func.id if isinstance(n.func, ast.Name) else n.func.attr if isinstance(n.func, ast.Attribute) else ""
            index = {"tr": 0, "_k": 0, "panel": 1, "field": 2, "caption": 1}.get(name)
            if index is not None and len(n.args) > index:
                add(n.args[index])
        if isinstance(n, ast.Assign) and isinstance(n.value, ast.List) and getattr(n.targets[0], "id", "") in ("RULE_NAMES", "SPECIALS"):
            for e in n.value.elts:
                add(e)
    return keys


def windows_file(name):
    try:
        with open(os.path.join(WINDOWS, name), encoding="utf-8") as f:
            return f.read()
    except OSError:
        return None


class Invite(unittest.TestCase):
    def test_valid(self):
        self.assertEqual(L.parse_invite("hyrule://100.64.0.5:5050?pw=a%20b%26c%C3%A9"), ("100.64.0.5", 5050, "a b&cé"))
        self.assertEqual(L.parse_invite("  192.168.1.20:6000 "), ("192.168.1.20", 6000, ""))
        self.assertEqual(L.parse_invite("my-host.example"), ("my-host.example", 5050, ""))
        self.assertEqual(L.parse_invite("HYRULE://h:1/"), ("h", 1, ""))
        self.assertEqual(L.parse_invite("h:65535?x=1&pw=p"), ("h", 65535, "p"))

    def test_invalid(self):
        for bad in ("", "   ", "h:0", "h:65536", "h:", "h:abc", "a..b", "-a", "a-", "a b", "300.1.1.1", "1.2.3", "h:5050:1", "x" * 201,
                    "hyrule://", ":5050", "h.-x"):
            self.assertIsNone(L.parse_invite(bad), bad)

    def test_url(self):
        self.assertEqual(L.invite_url("100.64.0.5", 5050, ""), "hyrule://100.64.0.5:5050")
        url = L.invite_url("h", 6000, "a b&é;")
        self.assertEqual(url, "hyrule://h:6000?pw=a%20b%26%C3%A9%3B")
        self.assertEqual(L.parse_invite(url), ("h", 6000, "a b&é;"))


class CliOutput(unittest.TestCase):
    def test_status(self):
        s = L.parse_status("version=0.1.0 mode=host server=up client=down joined=no port=5050 invite=hyrule://192.168.1.5:5050?pw=x invite_vpn=hyrule://100.64.0.5:5050?pw=x\n")
        self.assertEqual((s["version"], s["mode"], s["server"], s["client"], s["joined"], s["port"]), ("0.1.0", "host", "up", "down", "no", "5050"))
        self.assertEqual((s["invite"], s["invite_vpn"]), ("hyrule://192.168.1.5:5050?pw=x", "hyrule://100.64.0.5:5050?pw=x"))
        self.assertEqual(L.parse_status("version=0.1.0 mode=idle server=down client=down joined=no port=")["port"], "")
        self.assertEqual(L.parse_status("version=0.1.0 mode=join server=down client=up joined=yes port=5050 host=h")["host"], "h")

    def test_not_a_status(self):
        self.assertIsNone(L.parse_status("That is not a valid invite or address: --status\nUse hyrule://host:port?pw=PASSWORD, host:port"))
        self.assertIsNone(L.parse_status(""))

    def test_check(self):
        text = "ok: cemu\nmissing: BOTW game files\nok: mod\nskipped: no display\ninfo: using the saved settings\nmissing:   Milk Bar mod \n"
        self.assertEqual(L.parse_check(text), ["BOTW game files", "Milk Bar mod"])
        self.assertEqual(L.parse_check("ok: everything\n"), [])
        self.assertEqual(L.parse_check(""), [])

    def test_presets(self):
        got = L.parse_presets("10111100 Game Completion\r\n10000000 Hide n' seek\nbad line\n00000002 Death Swap\n1011110 Short\n")
        self.assertEqual(list(got.items()), [("Game Completion", "10111100"), ("Hide n' seek", "10000000"), ("Death Swap", "00000002")])
        self.assertEqual(L.rules_name("10000000", got), "Hide n' seek")
        self.assertEqual(L.parse_presets(""), {})

    def test_builtin_presets(self):
        self.assertTrue(all(L.valid_bits(b) for b in L.PRESETS.values()))
        self.assertEqual(L.rules_name("10111100"), "Game Completion")
        self.assertEqual(L.rules_name("00000002"), "Death Swap")
        self.assertEqual(L.rules_name("11111110"), "Custom")
        self.assertFalse(L.valid_bits("1011110"))
        self.assertFalse(L.valid_bits("10111103"))
        self.assertEqual(L.RECOMMENDED[1], L.PRESETS[L.RECOMMENDED[0]])
        self.assertEqual(set(L.PRESET_BLURBS), set(L.PRESETS) | {"Custom"})
        self.assertEqual(len(L.RULE_NAMES), 7)
        self.assertEqual(len(L.RULE_TIPS), 7)
        self.assertEqual(len(L.SPECIALS), len(L.SPECIAL_BLURBS))

    def test_presets_match_the_script(self):
        with open(os.path.join(HERE, "hyrule-online.sh"), encoding="utf-8") as f:
            text = f.read()
        for name, bits in L.PRESETS.items():
            self.assertIn(bits, text, name)

    def test_shares(self):
        L.set_language("en")
        self.assertEqual(L.shared_rules("10111100"), (["Enemies", "Koroks", "Towers", "Shrines", "Locations"], ["Quests", "Divine Beasts"]))
        self.assertEqual(L.shares_text("00000000"), "Shared: Nothing\nNot shared: " + ", ".join(L.RULE_NAMES))


class Limits(unittest.TestCase):
    def test_constants(self):
        self.assertEqual(L.DEFAULT_PORT, 5050)
        self.assertEqual(L.DEFAULT_DESC, "Explore Hyrule with Friends!")
        self.assertEqual(L.LIMITS, {"port_low": 1024, "port_high": 65535, "host_password": 40, "description": 80, "host_address": 100,
                                    "join_address": 200, "join_password": 40, "name": 16})

    def test_against_windows(self):
        xaml, rules, chars = windows_file("Launcher.xaml"), windows_file("ServerRules.cs"), windows_file("Characters.cs")
        if xaml is None or rules is None or chars is None:
            self.skipTest("the Windows sources are not in this checkout")
        for box, key in (("HostPortBox", None), ("HostPasswordBox", "host_password"), ("HostDescriptionBox", "description"), ("HostAddressBox", "host_address"),
                         ("JoinAddressBox", "join_address"), ("JoinPasswordBox", "join_password")):
            m = re.search(r'x:Name="%s"[^>]*MaxLength="(\d+)"' % box, xaml)
            self.assertTrue(m, box)
            self.assertEqual(int(m.group(1)), 5 if key is None else L.LIMITS[key], box)
        self.assertEqual(int(re.search(r"MaxName = (\d+)", chars).group(1)), L.LIMITS["name"])
        self.assertEqual(int(re.search(r"DefaultPort = (\d+)", rules).group(1)), L.DEFAULT_PORT)
        self.assertIn("1024", rules)
        self.assertIn("65535", rules)
        self.assertIn(L.DEFAULT_DESC, rules)

    def test_port(self):
        for good in ("1024", "5050", "65535"):
            self.assertEqual(L.clean_port(good), int(good))
        for bad in ("1023", "65536", "", "abc", "50 50", "-1", "0"):
            self.assertEqual(L.clean_port(bad), 5050, bad)
        self.assertEqual(L.clean_port(" 6000 "), 6000)
        self.assertTrue(L.valid_port("1"))
        self.assertFalse(L.valid_port("65536"))

    def test_text(self):
        self.assertEqual(L.clean_text(" a\nb\t ", 80), "ab")
        self.assertEqual(L.clean_text("x" * 100, 80), "x" * 80)
        self.assertEqual(L.host_password("a;b;c"), "abc")
        self.assertEqual(L.host_password(";" * 5 + "x" * 50), "x" * 40)
        self.assertEqual(L.clean_name("bad name!"), "badname")
        self.assertEqual(L.clean_name("x" * 17), "x" * 16)
        self.assertEqual(L.clean_name(""), "Link")

    def test_live_limits(self):
        self.assertEqual(L.live_limit("hostpass", "a;b"), "ab")
        self.assertEqual(L.live_limit("hostpass", "x" * 41), "x" * 40)
        self.assertEqual(L.live_limit("port", "50a50123"), "50501")
        self.assertEqual(L.live_limit("desc", "x" * 81), "x" * 80)
        self.assertEqual(L.live_limit("addr", "x" * 101), "x" * 100)
        self.assertEqual(L.live_limit("invite", "x" * 201), "x" * 200)
        self.assertEqual(L.live_limit("joinpass", "x" * 41), "x" * 40)
        self.assertEqual(L.live_limit("joinpass", "a;b"), "a;b")  # only a host password loses the semicolon
        self.assertEqual(L.live_limit("name", "A b.c"), "Abc")
        self.assertEqual(L.live_limit("name", "x" * 17), "x" * 16)

    def test_command_line(self):
        f = dict(port="5050", hostpass=" pw\n", desc=" Hi there ", invite="hyrule://100.64.0.5:6000?pw=inv", joinpass="", name="Link")
        self.assertEqual(L.build_args(L.LOCAL, f, "10000000"), (["--local", "--rules", "10000000", "--desc", "Hi there", "--name", "Link"], None))
        self.assertEqual(L.build_args(L.HOST, f, "00000002"), (["--host", "5050", "pw", "--rules", "00000002", "--desc", "Hi there", "--name", "Link"], None))
        self.assertEqual(L.build_args(L.HOST, dict(f, hostpass="", desc=" "))[0][:3], ["--host", "5050", ""])
        self.assertEqual(L.build_args(L.HOST, dict(f, desc=" "))[0][6], L.DEFAULT_DESC)
        self.assertEqual(L.build_args(L.JOIN, f), (["100.64.0.5", "6000", "inv", "Link"], None))
        self.assertEqual(L.build_args(L.JOIN, dict(f, joinpass="own"))[0][2], "own")
        self.assertEqual(L.build_args(L.JOIN, dict(f, invite="h"))[0], ["h", "5050", "", "Link"])

    def test_cleaned_like_windows(self):
        f = dict(port="70000", hostpass="a;b", desc="x" * 100, invite="h:1", joinpass="", name="bad name")
        args = L.build_args(L.HOST, f)[0]
        self.assertEqual((args[1], args[2], len(args[6]), args[8]), ("5050", "ab", 80, "badname"))

    def test_bad_invite(self):
        f = dict(port="5050", hostpass="", desc="d", invite="a..b", joinpass="", name="Link")
        args, error = L.build_args(L.JOIN, f)
        self.assertIsNone(args)
        self.assertTrue(error)


class Strings(unittest.TestCase):
    def setUp(self):
        L.set_language("en")

    tearDown = setUp

    def test_every_used_text_has_es_and_fr(self):
        missing = []
        for key in used_keys():
            if key in L.NEUTRAL or key in L.PRESETS:
                continue
            row = L.STRINGS.get(key)
            if not isinstance(row, dict) or not row.get("es") or not row.get("fr"):
                missing.append(key)
        self.assertEqual(missing, [])

    def test_placeholders_and_wording(self):
        for key, row in L.STRINGS.items():
            holes = sorted(re.findall(r"\{\d+\}", key))
            for lang in ("es", "fr"):
                self.assertEqual(sorted(re.findall(r"\{\d+\}", row[lang])), holes, (key, lang))
                self.assertNotRegex(row[lang], r"\bPCs?\b", (key, lang))  # the Ally is a device, not a PC
            self.assertNotRegex(key, r"\bPCs?\b", key)

    def test_no_unused_rows(self):
        self.assertEqual(sorted(set(L.STRINGS) - set(used_keys())), [])

    def test_rows_equal_lang_cs(self):
        src = windows_file("Lang.cs")
        if src is None:
            self.skipTest("the Windows sources are not in this checkout")
        lit = r'"((?:[^"\\]|\\.)*)"'
        rows = {}
        for m in re.finditer(r"new\[\] \{ " + lit + r",\s*" + lit + r",\s*" + lit + r" \},?", src):
            en, es, fr = (json.loads('"' + g + '"') for g in m.groups())
            rows[en] = (es, fr)
        same = [k for k, row in L.STRINGS.items() if k in rows and (row["es"], row["fr"]) == rows[k]]
        self.assertGreater(len(same), 80)
        for key in L.STRINGS:
            if key not in rows:
                continue
            if (L.STRINGS[key]["es"], L.STRINGS[key]["fr"]) != rows[key]:
                self.assertRegex(" ".join(rows[key]), r"PC", key)  # the only edit allowed is PC -> device

    def test_lookup(self):
        self.assertEqual(L.tr("Exit"), "Exit")
        L.set_language("es")
        self.assertEqual(L.tr("Koroks"), "Kologs")
        self.assertEqual(L.tr("Not in the table"), "Not in the table")
        self.assertEqual(L.tr("{0} joined.", "Ana"), L.STRINGS["{0} joined."]["es"].format("Ana"))
        L.set_language("fr")
        self.assertEqual(L.tr("Koroks"), "Korogus")
        L.set_language("xx")
        self.assertEqual(L.LANG, "en")

    def test_language_choice(self):
        self.assertEqual(L.choose_language("fr", {"LANG": "es_ES.UTF-8"}), "fr")
        self.assertEqual(L.choose_language(None, {"LANG": "es_ES.UTF-8"}), "es")
        self.assertEqual(L.choose_language("zz", {"LANG": "fr_FR"}), "fr")
        self.assertEqual(L.choose_language(None, {"LANG": "de_DE"}), "en")
        self.assertEqual(L.choose_language(None, {"LANG": "C"}), "en")
        self.assertEqual(L.choose_language(None, {}), "en")
        self.assertEqual(L.system_language({"LC_ALL": "fr_CA.UTF-8", "LANG": "es_ES"}), "fr")
        self.assertEqual(L.system_language({"LANGUAGE": "es:en"}), "es")

    def test_help_sections(self):
        for lang in ("en", "es", "fr"):
            L.set_language(lang)
            for section in L.HELP_SECTIONS:
                title, sep, text = L.tr(section).partition(":")
                self.assertTrue(sep and text.strip(), section)

    def test_file_is_clean(self):
        with open(os.path.join(HERE, "strings.json"), "rb") as f:
            raw = f.read()
        self.assertNotIn(b"\r", raw)
        for key, row in json.loads(raw.decode("utf-8")).items():
            self.assertEqual(set(row), {"es", "fr"}, key)

    def test_unreadable_file(self):
        self.assertEqual(L.load_strings(os.path.join(HERE, "nope.json")), {})
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "s.json")
            with open(path, "w") as f:
                f.write("[1, 2")
            self.assertEqual(L.load_strings(path), {})


class Network(unittest.TestCase):
    def test_classify(self):
        cases = {"100.64.0.5": "vpn", "100.127.255.1": "vpn", "100.128.0.1": "public", "100.63.0.1": "public", "25.1.2.3": "vpn", "26.1.2.3": "vpn",
                 "10.0.0.2": "home", "192.168.1.5": "home", "172.16.0.1": "home", "172.31.9.9": "home", "172.32.0.1": "public",
                 "127.0.0.1": "none", "169.254.1.1": "none", "8.8.8.8": "public", "host.tailnet.ts.net": "vpn", "my-host.example": "name",
                 "": "none", "1.2.3": "none"}
        for host, kind in cases.items():
            self.assertEqual(L.classify(host), kind, host)

    def test_vpn_name(self):
        self.assertEqual(L.vpn_name("100.64.0.5"), "Tailscale")
        self.assertEqual(L.vpn_name("a.ts.net"), "Tailscale")
        self.assertEqual(L.vpn_name("25.1.1.1"), "Hamachi")
        self.assertEqual(L.vpn_name("26.1.1.1"), "Radmin VPN")

    def test_addresses(self):
        text = "1: lo    inet 127.0.0.1/8 scope host lo\n2: wlan0    inet 192.168.1.43/24 brd 192.168.1.255\n3: tailscale0    inet 100.110.16.75/32 scope global tailscale0\n"
        found = L.parse_addresses(text)
        self.assertEqual(found, ["127.0.0.1", "192.168.1.43", "100.110.16.75"])
        self.assertEqual(L.best_address(found), "100.110.16.75")
        self.assertEqual(L.best_address(["127.0.0.1", "192.168.1.43"]), "192.168.1.43")
        self.assertEqual(L.best_address(["127.0.0.1", "8.8.8.8"]), "")
        self.assertEqual(L.best_address([]), "")

    def test_hint(self):
        L.set_language("en")
        self.assertIn("Tailscale", L.address_hint("100.64.0.5"))
        self.assertIn("home network", L.address_hint("192.168.1.5"))
        self.assertIn("No network address", L.address_hint(""))
        self.assertIn("forwarded", L.address_hint("8.8.8.8"))
        self.assertIn("forwarded", L.address_hint("my-host.example"))


class ServerEvents(unittest.TestCase):
    def setUp(self):
        L.set_language("en")

    def test_lines(self):
        cases = {"[12:00:01] Player Link joined the server. Assigned to player 1.": ("joined", "Link", 1),
                 "Player Zelda joined the server. Assigned to player 2": ("joined", "Zelda", 2),
                 "Player Link disconnected.": ("left", "Link"),
                 "[12:00:09] Player Link disconnected - Details: timeout": ("left", "Link"),
                 "Player Bob tried to connect but failed with error 3.": ("refused", "Bob", 3),
                 "nothing here": None, "": None}
        for line, event in cases.items():
            self.assertEqual(L.parse_server_line(line), event, line)

    def test_texts(self):
        self.assertEqual(L.event_text(("joined", "A", 1)), "A joined.")
        self.assertEqual(L.event_text(("left", "A")), "A left.")
        self.assertEqual(L.event_text(("refused", "A", 3)), "A could not join: wrong password.")
        self.assertEqual(L.event_text(("refused", "A", 9)), "A could not join (error 9).")

    def test_roster(self):
        r = L.Roster()
        for e in (("joined", "B", 2), ("joined", "A", 1), ("joined", "C", 3), ("left", "C")):
            r.apply(e)
        self.assertEqual(r.names(), ["A", "B"])
        r.apply(("joined", "D", 1))  # the slot is taken over
        self.assertEqual(r.names(), ["D", "B"])

    def test_log_masks_password(self):
        log = L.ServerLog(os.path.join(HERE, "no-such.log"))
        log.feed("Player A joined the server. Assigned to player 1.")
        log.feed("note: hyrule://h:1?pw=secret")
        self.assertNotIn("secret", "\n".join(log.lines))
        self.assertEqual(log.roster.names(), ["A"])

    def test_failure_text(self):
        self.assertEqual(L.failure_text(["Hyrule Online 0.1", "Connecting... 3s", "No route to host"], False), "Start failed: No route to host")
        self.assertTrue(L.failure_text(["x"], True).endswith(L.CHECKLIST))
        self.assertEqual(L.failure_text([], False), "Start failed: The start script failed.")


class Prefs(unittest.TestCase):
    def test_load(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "join.conf")
            self.assertEqual(L.load_conf(path), {})
            with open(path, "w", encoding="utf-8") as f:
                f.write("host=old\nport=1\nname=Ally\nrules=10111100\ndesc=a=b c\nport=2\nnoequals\n")
            c = L.load_conf(path)
            self.assertEqual((c["host"], c["port"], c["name"], c["rules"], c["desc"]), ("old", "1", "Ally", "10111100", "a=b c"))  # first line wins
            self.assertNotIn("noequals", c)

    def test_save_and_reload(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "sub", "launcher.conf")
            L.save_conf(path, {"language": "fr", "tab": "World", "mode": "Host"})
            self.assertEqual(L.load_conf(path), {"language": "fr", "tab": "World", "mode": "Host"})
            self.assertFalse(os.path.exists(path + ".tmp"))

    def test_conf_path(self):
        self.assertEqual(L.default_conf_path({"HYRULE_LAUNCHER_CONF": "/x/y.conf"}), "/x/y.conf")
        self.assertEqual(L.default_conf_path({"XDG_CONFIG_HOME": "/cfg"}), os.path.join("/cfg", "hyrule-online", "launcher.conf"))
        self.assertTrue(L.default_conf_path({}).endswith(os.path.join(".config", "hyrule-online", "launcher.conf")))

    def test_initial_state(self):
        presets = dict(L.PRESETS)
        empty = L.initial_state({}, {}, presets)
        self.assertEqual((empty["tab"], empty["mode"], empty["bits"]), ("Play", L.LOCAL, "10111100"))
        self.assertEqual(empty["values"]["port"], "5050")
        self.assertEqual(empty["values"]["desc"], L.DEFAULT_DESC)
        self.assertEqual(empty["values"]["name"], "Link")
        saved = L.initial_state({"tab": "World", "mode": "Join"}, {"mode": "host", "host": "h", "port": "6000", "rules": "10000000", "hostpass": "a;b", "name": "Ally"}, presets)
        self.assertEqual((saved["tab"], saved["mode"], saved["bits"]), ("World", L.JOIN, "10000000"))
        self.assertEqual((saved["values"]["invite"], saved["values"]["hostpass"], saved["values"]["name"]), ("h:6000", "ab", "Ally"))
        self.assertEqual(L.initial_state({"tab": "Help"}, {"mode": "host a server"}, presets)["tab"], "Play")
        self.assertEqual(L.initial_state({}, {"mode": "host a server"}, presets)["mode"], L.HOST)
        self.assertEqual(L.initial_state({}, {"rules": "bad"}, presets)["bits"], "10111100")

    def test_version(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertEqual(L.read_version((d,)), "unknown")
            with open(os.path.join(d, "VERSION"), "w") as f:
                f.write(" 0.1.0 \r\n")
            self.assertEqual(L.read_version((d,)), "0.1.0")

    def test_scale(self):
        design_w, design_h, scale = L.shell_fit(1280, 800)
        self.assertEqual((round(design_w), round(design_h), round(scale, 3)), (1648, 1030, 0.746))
        for size in ((1280, 800), (1024, 600), (1920, 1080), (3840, 2160), (640, 400)):
            design_w, design_h, scale = L.shell_fit(*size)
            self.assertLessEqual(design_w * scale, size[0] + 1, size)
            self.assertLessEqual(design_h * scale, size[1] + 1, size)
            self.assertGreater(design_w * scale, size[0] * 0.9, size)


class Navigation(unittest.TestCase):
    @staticmethod
    def grid(rows, cols, w=100, h=40, gap=10):
        return [(c * (w + gap), r * (h + gap), w, h) for r in range(rows) for c in range(cols)]

    def test_grid_moves(self):
        items = self.grid(3, 3)
        self.assertEqual(L.spatial_pick(items[4], items, 0), 1)
        self.assertEqual(L.spatial_pick(items[4], items, 1), 7)
        self.assertEqual(L.spatial_pick(items[4], items, 2), 3)
        self.assertEqual(L.spatial_pick(items[4], items, 3), 5)
        self.assertEqual(L.spatial_pick(items[0], items, 0), -1)
        self.assertEqual(L.spatial_pick(items[0], items, 2), -1)
        self.assertEqual(L.spatial_pick(items[4], items, 9), -1)

    def test_grid_reaches_everything(self):
        items = self.grid(4, 4)
        self.assertEqual(L.unreachable(items, [False] * 16), [])

    def test_uneven_rows(self):
        items = [(0, 0, 500, 40), (0, 50, 240, 40), (260, 50, 240, 40), (0, 100, 100, 40), (150, 100, 100, 40), (300, 100, 200, 40)]
        self.assertEqual(L.unreachable(items, [False] * 6), [])

    def test_chrome_does_not_wrap_sideways(self):
        tabs = [(0, 0, 100, 40), (110, 0, 100, 40)]
        content = [(0, 100, 100, 40), (110, 100, 100, 40)]
        items, chrome = tabs + content, [True, True, False, False]
        self.assertEqual(L.spatial_next(items[1], items, chrome, True, 3), -1)
        self.assertEqual(L.spatial_next(items[2], items, chrome, False, 3), 3)
        self.assertEqual(L.spatial_next(items[3], items, chrome, False, 3), -1)

    def test_content_crosses_rows_sideways(self):
        items = [(0, 0, 100, 40), (0, 100, 100, 40), (200, 100, 100, 40)]
        self.assertEqual(L.spatial_next(items[0], items, [False] * 3, False, 3), 2)


class StartIcon(unittest.TestCase):
    def test_png_is_valid_and_leaves_the_gap(self):
        import base64
        import struct
        import zlib
        raw = base64.b64decode(L.start_icon_png(1.0))
        self.assertEqual(raw[:8], b"\x89PNG\r\n\x1a\n")
        w, h = struct.unpack(">II", raw[16:24])
        self.assertEqual((w, h), (24 + 16, 24))
        idat = raw[raw.index(b"IDAT") + 4:raw.index(b"IEND") - 8]
        rows = zlib.decompress(idat)
        self.assertEqual(len(rows), h * (1 + w * 4))
        self.assertTrue(any(rows[1 + x * 4 + 3] for x in range(16, w)) or any(rows[(1 + w * 4) * 12 + 1 + x * 4 + 3] for x in range(16, w)))
        self.assertTrue(all(rows[(1 + w * 4) * 12 + 1 + x * 4 + 3] == 0 for x in range(16)))


class TagGame(unittest.TestCase):
    def test_every_cemu_window_gets_the_shortcut_id(self):
        calls = []

        def run(cmd, **kw):
            calls.append(cmd)
            return type("R", (), {"stdout": "10 11\n" if cmd[0] == "xdotool" else ""})()
        real, L.subprocess.run = L.subprocess.run, run
        try:
            self.assertEqual(L.tag_game_windows("4253636849"), 2)
        finally:
            L.subprocess.run = real
        self.assertEqual([c[2] for c in calls[1:]], ["10", "11"])
        self.assertTrue(all(c[-1] == "4253636849" for c in calls[1:]))

    def test_missing_tools_find_no_window(self):
        def run(cmd, **kw):
            raise FileNotFoundError(cmd[0])
        real, L.subprocess.run = L.subprocess.run, run
        try:
            self.assertEqual(L.tag_game_windows("1"), 0)
        finally:
            L.subprocess.run = real


class FakePad:
    def __init__(self):
        self.down, self.pulses = set(), 0

    def held(self):
        return set(self.down)

    def rumble(self, ms=180):
        self.pulses += 1


class MenuShortcut(unittest.TestCase):
    """The hold shortcut and its capture, on a stand-in for App: no Tk needed."""

    def app(self, saved=None):
        class Me:
            shortcut, shortcut_choices, capture_step, pad_hold, set_shortcut = (L.App.shortcut, L.App.shortcut_choices, L.App.capture_step,
                                                                                 L.App.pad_hold, L.App.set_shortcut)

            def remember(self, key, value):
                self.own[key] = value

            def refresh_shortcut(self):
                pass

            def bring_back(self):
                self.calls.append("back")

            def step_aside(self):
                self.calls.append("aside")
        me = Me()
        me.own, me.calls, me.capture, me.note = {"shortcut": saved} if saved else {}, [], None, None
        me.tracker, me.tap = L.gamepad.Tracker(), L.gamepad.Tap()
        me.hidden, me.dialog, me.help_open, me.up, me.appid, me.game_seen = False, None, False, False, "123", True
        me.start_btn = type("B", (), {"invoke": lambda s: me.calls.append("start")})()
        return me

    def run_for(self, me, pad, names, start, end):
        pad.down = set(names)
        now = start
        while now <= end:
            L.time.monotonic = lambda n=now: n
            me.pad_hold(pad)
            now += .1

    def setUp(self):
        self.real = L.time.monotonic
        self.addCleanup(setattr, L.time, "monotonic", self.real)

    def test_default_is_start_and_lt(self):
        self.assertEqual(self.app().shortcut(), {"Start", "LT"})
        self.assertIsNone(self.app("Off").shortcut())
        self.assertEqual(self.app("L3+R3").shortcut(), {"L3", "R3"})

    def test_hold_steps_aside_with_a_pulse_and_does_not_press_start(self):
        me, pad = self.app(), FakePad()
        self.run_for(me, pad, {"Start", "LT"}, 0, 3)
        self.assertEqual((me.calls, pad.pulses), (["aside"], 1))
        self.run_for(me, pad, set(), 3.1, 3.2)
        self.assertEqual(me.calls, ["aside"])  # letting go of Start does not press Start session

    def test_hold_brings_it_back_when_hidden(self):
        me, pad = self.app("L3+R3"), FakePad()
        me.hidden = True
        self.run_for(me, pad, {"Start", "LT"}, 0, 2)
        self.assertEqual(me.calls, [])  # not the saved shortcut
        self.run_for(me, pad, set(), 2.1, 2.2)
        self.run_for(me, pad, {"L3", "R3"}, 3, 5)
        self.assertEqual(me.calls, ["back"])

    def test_too_short_a_hold_does_nothing(self):
        me, pad = self.app(), FakePad()
        self.run_for(me, pad, {"Start", "LT"}, 0, 1.2)
        self.assertEqual((me.calls, pad.pulses), ([], 0))

    def test_start_alone_presses_start_session_only_off_the_shortcut(self):
        me, pad = self.app("L3+R3"), FakePad()
        self.run_for(me, pad, {"Start"}, 0, .2)
        self.assertEqual(me.calls, ["start"])
        me, pad = self.app(), FakePad()  # Start is part of the shortcut: the press counts when it is let go
        self.run_for(me, pad, {"Start"}, 0, .2)
        self.assertEqual(me.calls, [])
        self.run_for(me, pad, set(), .3, .4)
        self.assertEqual(me.calls, ["start"])

    def test_capture_saves_the_released_combination(self):
        me, pad = self.app(), FakePad()
        me.capture = {"armed": False, "seen": set(), "end": 10}
        self.run_for(me, pad, set(), 0, .2)
        self.run_for(me, pad, {"L3"}, .3, .5)
        self.run_for(me, pad, {"L3", "RB"}, .6, .8)
        self.run_for(me, pad, set(), .9, 1)
        self.assertEqual((me.own["shortcut"], me.capture, me.calls), ("L3+RB", None, []))

    def test_capture_waits_for_the_button_that_started_it(self):
        me, pad = self.app(), FakePad()
        me.capture = {"armed": False, "seen": set(), "end": 10}
        self.run_for(me, pad, {"A"}, 0, .5)  # the confirm press that opened it
        self.assertEqual(me.capture["seen"], set())

    def test_capture_rejects_one_input_and_the_two_centre_buttons_and_b_cancels(self):
        me, pad = self.app(), FakePad()
        me.capture = {"armed": False, "seen": set(), "end": 10}
        for names in ({"X"}, {"Start", "Back", "LB"}):
            self.run_for(me, pad, set(), 0, .1)
            self.run_for(me, pad, names, .2, .3)
            self.run_for(me, pad, set(), .4, .5)
            self.assertIsNotNone(me.capture)
            self.assertNotIn("shortcut", me.own)
        self.run_for(me, pad, {"B"}, .6, .7)
        self.run_for(me, pad, set(), .8, .9)
        self.assertIsNone(me.capture)
        self.assertNotIn("shortcut", me.own)

    def test_capture_gives_up_after_ten_seconds(self):
        me, pad = self.app(), FakePad()
        me.capture = {"armed": False, "seen": set(), "end": L.CAPTURE_S}
        self.run_for(me, pad, set(), 0, L.CAPTURE_S + .5)
        self.assertIsNone(me.capture)


class Window(unittest.TestCase):
    SIZES = ((1280, 800), (1024, 600), (1920, 1080))

    @classmethod
    def setUpClass(cls):
        if L.tk is None:
            raise unittest.SkipTest("tkinter is not installed")
        try:
            root = L.tk.Tk()
        except L.tk.TclError:
            raise unittest.SkipTest("no display (run under xvfb-run to build the pages)")
        root.destroy()
        cls.tmp = tempfile.TemporaryDirectory()

    @classmethod
    def tearDownClass(cls):
        if hasattr(cls, "tmp"):
            cls.tmp.cleanup()

    def make(self, width, height, language="en"):
        conf = os.path.join(self.tmp.name, "launcher.conf")
        with open(conf, "w", encoding="utf-8") as f:
            f.write("language=%s\n" % language)
        root = L.tk.Tk()
        self.addCleanup(L.set_language, "en")
        try:
            root.attributes("-alpha", 0.01)
        except L.tk.TclError:
            pass
        root.geometry("%dx%d+0+0" % (width, height))
        cli = L.Cli(script=os.path.join(self.tmp.name, "missing.sh"))
        app = L.App(root, cli=cli, conf=conf, join_conf=os.path.join(self.tmp.name, "join.conf"), size=(width, height))
        root.update()
        root.focus_force()
        root.update()
        app.search_fn = lambda last, cancel: ([], 0)  # the tests never scan a real network
        app.check_fn = lambda host, port: 0
        return root, app

    def settle(self, app, until):
        for _ in range(100):
            app.root.update()
            if until():
                return
            time.sleep(0.02)
        self.fail("the background work did not finish")

    def show_rows(self, app, rows):
        app.found = rows
        app.render_found()
        app.root.update()

    def test_finder_controls_exist_and_the_list_hides_when_empty(self):
        for width, height in self.SIZES:
            for language in ("en", "es", "fr"):
                root, app = self.make(width, height, language)
                app.set_mode(L.JOIN)
                app.show_page("Online")
                self.show_rows(app, [])
                self.assertFalse(app.foundbox.winfo_ismapped())
                self.show_rows(app, [L.finder.Found("100.64.0.5", 5050, "vpn", 12), L.finder.Found("192.168.1.20", 5050, "lan", 3)] * 3)
                where = (width, height, language)
                self.assertTrue(app.foundbox.winfo_ismapped(), where)
                self.assertEqual(len(app.foundbox.winfo_children()), 6, where)
                items = app.focusables()
                self.assertEqual(len([w for w in items if str(w).startswith(str(app.foundbox) + ".")]), 6, where)
                for w in (app.search_btn, app.test_btn):
                    self.assertIn(w, items, where)
                self.assertEqual(L.unreachable(app.boxes(items), [w in app.chrome for w in items]), [], where)
                self.assertLessEqual(app.pages["Online"].winfo_reqheight(), app.host.winfo_height(), where)
                for w in app.foundbox.winfo_children() if (width, height) == (1280, 800) else ():
                    self.assertGreaterEqual(w.winfo_height(), 30, where)
                root.destroy()

    def test_search_runs_on_the_online_page_in_join_and_not_while_up_or_busy(self):
        root, app = self.make(1280, 800)
        calls = []
        app.search_fn = lambda last, cancel: calls.append(last) or ([], 0)
        app.show_page("Online")
        app.set_mode(L.HOST)
        root.update()
        self.assertEqual(calls, [])
        app.set_mode(L.JOIN)
        self.settle(app, lambda: not app.searching)
        self.assertEqual(len(calls), 1)
        app.up = True
        app.set_mode(L.JOIN)
        app.show_page("Play")
        app.show_page("Online")
        root.update()
        self.assertEqual(len(calls), 1)
        app.up = False
        app.searching = True
        app.find()
        root.update()
        self.assertEqual(len(calls), 1)
        root.destroy()

    def test_search_fills_the_address_only_when_it_is_empty_or_was_filled_by_the_finder(self):
        root, app = self.make(1280, 800)
        rows = [L.finder.Found("100.64.0.5", 5050, "vpn", 12)]
        app.search_fn = lambda last, cancel: (rows, 1)
        app.set_mode(L.JOIN)
        app.show_page("Online")
        self.settle(app, lambda: not app.searching)
        self.assertEqual(app.v["invite"].get(), "100.64.0.5:5050")
        self.assertEqual(app.find_text.cget("text"), "Servers found: 1.")
        rows[:] = [L.finder.Found("100.64.0.9", 5050, "vpn", 12)]
        app.find()
        self.settle(app, lambda: not app.searching)
        self.assertEqual(app.v["invite"].get(), "100.64.0.9:5050")
        app.v["invite"].set("my.host:6000")
        app.find()
        self.settle(app, lambda: not app.searching)
        self.assertEqual(app.v["invite"].get(), "my.host:6000")
        app.v["invite"].set("  ")
        app.find()
        self.settle(app, lambda: not app.searching)
        self.assertEqual(app.v["invite"].get(), "  ")
        app.pick_found(rows[0])
        self.assertEqual(app.v["invite"].get(), "100.64.0.9:5050")
        root.destroy()

    def test_empty_search_says_so(self):
        root, app = self.make(1280, 800)
        app.set_mode(L.JOIN)
        app.show_page("Online")
        self.settle(app, lambda: not app.searching)
        self.assertIn("No servers found.", app.find_text.cget("text"))
        self.assertIn("Tailscale was not found", app.find_text.cget("text"))
        self.assertFalse(app.foundbox.winfo_ismapped())
        root.destroy()

    def test_test_button_reports_the_result_and_rejects_a_bad_address(self):
        root, app = self.make(1280, 800)
        app.set_mode(L.JOIN)
        app.show_page("Online")
        app.v["invite"].set("")
        app.try_connect()
        self.assertIn("not valid", app.find_text.cget("text"))
        app.v["invite"].set("10.0.0.2:5050")
        app.check_fn = lambda host, port: 42
        app.try_connect()
        self.assertEqual(str(app.test_btn.cget("state")), "disabled")
        self.settle(app, lambda: not app.testing)
        self.assertEqual(str(app.test_btn.cget("state")), "normal")
        self.assertEqual(app.find_text.cget("text"), "Reachable in 42 ms.")
        app.check_fn = lambda host, port: 0
        app.try_connect()
        self.settle(app, lambda: not app.testing)
        self.assertTrue(app.find_text.cget("text").startswith("Cannot reach 10.0.0.2:5050."))
        root.destroy()

    def test_closing_cancels_the_search(self):
        root, app = self.make(1280, 800)
        app.stop_timers()
        self.assertTrue(app.cancel.is_set())
        root.destroy()

    def pages(self, app):
        for mode in (L.LOCAL, L.HOST, L.JOIN):
            app.set_mode(mode)
            for tab in L.TABS:
                app.show_page(tab)
                app.root.update()
                yield mode, tab

    def test_every_page_fits_without_scrolling(self):
        for width, height in self.SIZES:
            for language in ("en", "es", "fr"):
                root, app = self.make(width, height, language)
                app.v["invite"].set("hyrule://100.64.0.5:5050")  # the Play page's online card shows the invite in Host and Join
                app.v["addr"].set("100.64.0.5")
                for mode, tab in self.pages(app):
                    if tab == "Details":
                        continue  # the logs scroll by design
                    app.render()
                    root.update()
                    where = (width, height, language, mode, tab)
                    self.assertLessEqual(app.pages[tab].winfo_reqheight(), app.host.winfo_height(), where)
                    self.assertLessEqual(app.pages[tab].winfo_reqwidth(), app.host.winfo_width() + 2, where)
                root.destroy()

    def test_every_control_is_reachable_by_arrows(self):
        for width, height in self.SIZES:
            root, app = self.make(width, height)
            for mode, tab in self.pages(app):
                items = app.focusables()
                self.assertGreater(len(items), 4)
                chrome = [w in app.chrome for w in items]
                self.assertEqual(L.unreachable(app.boxes(items), chrome), [], (width, height, mode, tab))
            root.destroy()

    def test_tab_visits_every_control(self):
        root, app = self.make(1280, 800)
        for mode, tab in self.pages(app):
            items = app.focusables()
            items[0].focus_set()
            root.update()
            seen = {root.focus_get()}
            for _ in items:
                app.nav("Tab")
                seen.add(root.focus_get())
            self.assertEqual(seen, set(items), (mode, tab))
        root.destroy()

    def test_every_control_is_big_enough(self):
        root, app = self.make(1280, 800)
        for mode, tab in self.pages(app):
            for w in app.focusables():
                if w.winfo_class() != "Text":
                    self.assertGreaterEqual(getattr(w, "box", w).winfo_height(), 30, (mode, tab, w))
        root.destroy()

    def test_pages_and_modes(self):
        root, app = self.make(1280, 800)
        self.assertEqual(L.TABS, ["Play", "Online", "Graphics", "World", "Language", "Details"])
        self.assertEqual([m for m, _ in L.MODES], [L.LOCAL, L.HOST, L.JOIN])
        self.assertEqual(set(app.pages), set(L.TABS))
        app.show_page("World")
        self.assertEqual(app.tab.get(), "World")
        app.set_mode(L.JOIN)
        self.assertEqual(app.mode.get(), L.JOIN)
        self.assertEqual(L.load_conf(app.conf_path).get("mode"), L.JOIN)  # the last tab and mode are remembered
        self.assertEqual(L.load_conf(app.conf_path).get("tab"), "World")
        root.destroy()
        root, app = self.make(1280, 800)
        self.assertEqual((app.tab.get(), app.mode.get()), ("Play", L.LOCAL))  # make() starts a fresh conf, so nothing is remembered yet
        root.destroy()

    def fixture_cemu(self):
        """A Cemu folder with just the two packs the Graphics page reads: the game's presets and the FPS++ limits."""
        def block(name, category, extra=""):
            return "\n[Preset]\nname = %s\ncategory = %s\n%s" % (name, category, extra)
        files = {os.path.join(L.display.STOCK, "rules.txt"): "[Definition]\ntitle = Graphics\n" + block("Low (50%)", "Shadows") + block("Medium (100%, Default)", "Shadows", "default = 1\n")
                 + block("High (Default)", "Shadow Draw Distance", "default = 1\n") + block("Normal FXAA (Default)", "Anti-Aliasing", "default = 1\n"),
                 L.display.FPS_PACK: "[Definition]\ntitle = FPS++\n" + "".join(block("%d FPS" % n, "FPS Limit", "$fpsLimitNormal = %d\n" % n) for n in (30, 60, 120, 144))}
        cemu = os.path.join(self.tmp.name, "cemu")
        for path, text in files.items():
            os.makedirs(os.path.dirname(os.path.join(cemu, path)), exist_ok=True)
            with open(os.path.join(cemu, path), "w", encoding="utf-8", newline="") as f:
                f.write(text)
        return cemu

    def test_graphics_page_saves_what_display_py_reads(self):
        with mock.patch.object(L.display, "detect_screen", return_value=(1920, 1080, 120)), mock.patch.object(L.display, "detect_hardware", return_value={"tier": 1}):
            root, app = self.make(1280, 800)
            self.assertEqual(str(app.gfx_btns["fps"].cget("state")), "disabled")  # no FPS++ pack next to the test
            self.assertEqual(app.gfx_btns["fps"].cget("text"), "FPS++ is not installed")
            app.CEMU = self.fixture_cemu()
            self.assertTrue(str(app.gfx_btns["fps"]).startswith(str(app.pages["Play"])) and str(app.screen_info).startswith(str(app.pages["Play"])))  # as in Windows, not on Graphics
            app.show_page("Play")
            root.update()
            self.assertEqual(app.screen_info.cget("text"), "1920 × 1080  /  120 Hz")
            self.assertEqual(app.layout_info.cget("text"), "One player · the whole display\nEach game renders 1920 × 1080 at 120 FPS")
            app.set_gfx({"scale": 0.75, "vsync": True})
            self.assertIn("75%", app.gfx_btns["scale"].cget("text"))
            self.assertIn("1440 × 810", app.layout_info.cget("text"))
            app.pick_gfx("fps")
            app.close_dialog(2)  # Auto, 30, 60, 120 FPS
            self.assertEqual(app.gfx_btns["fps"].cget("text"), "60 FPS")
            app.pick_gfx("shadows")
            app.close_dialog(0)
            self.assertEqual(app.gfx_btns["shadows"].cget("text"), "Low (50%)")
            app.pick_gfx("volume")
            app.close_dialog(len(L.display.VOLUMES))  # the Cancel entry changes nothing
            saved = L.load_conf(app.conf_path)
            prefs = L.display.read_prefs(saved)
            self.assertEqual((prefs["scale"], prefs["fps"], prefs["vsync"], prefs["shadows"], prefs["volume"]), (0.75, 60, True, "Low (50%)", L.display.DEFAULTS["volume"]))
            app.use_recommended_graphics()
            self.assertEqual(L.display.read_prefs(L.load_conf(app.conf_path))["scale"], 1.0)
            self.assertEqual(app.gfx_btns["fps"].cget("text"), "FPS target: Auto · 120 FPS")
            self.assertTrue(app.gfx_vars["async"].get() and not app.gfx_vars["vsync"].get())
            self.assertEqual(app.note, ("Recommended graphics settings applied ({0} class).", ("Balanced",)))
            root.destroy()

    def test_world_is_locked_when_joining(self):
        root, app = self.make(1280, 800)
        app.set_mode(L.HOST)
        app.show_page("World")
        root.update()
        self.assertTrue(all(str(c.cget("state")) == "normal" for c, _ in app.checks))
        app.set_mode(L.JOIN)
        root.update()
        self.assertTrue(all(str(c.cget("state")) == "disabled" for c, _ in app.checks))
        self.assertTrue(app.locked.winfo_viewable())
        root.destroy()

    def test_rules_follow_the_checks(self):
        root, app = self.make(1280, 800)
        app.set_mode(L.HOST)
        app.show_page("World")
        app.checks[1][0].invoke()
        self.assertEqual(app.bits[:7], "1111110")
        self.assertIn("Custom", app.preset_btn.cget("text"))
        app.use_recommended()
        self.assertEqual(app.bits, "10111100")
        root.destroy()

    def test_semicolon_never_stays_in_a_host_password(self):
        root, app = self.make(1280, 800)
        app.set_mode(L.HOST)
        app.show_page("Online")
        app.v["hostpass"].set("ab;cd")
        self.assertEqual(app.v["hostpass"].get(), "abcd")
        app.v["hostpass"].set("x" * 60)
        self.assertEqual(len(app.v["hostpass"].get()), 40)
        app.v["port"].set("12ab3")
        self.assertEqual(app.v["port"].get(), "123")
        self.assertIn("(;)", app.semicolon.cget("text"))
        root.destroy()

    def test_invite_follows_the_form(self):
        root, app = self.make(1280, 800)
        app.set_mode(L.HOST)
        app.v["addr"].set("100.64.0.5")
        app.v["port"].set("6000")
        app.v["hostpass"].set("p w")
        root.update()
        self.assertEqual(app.invite_box.get(), "hyrule://100.64.0.5:6000?pw=p%20w")
        self.assertEqual(str(app.copy_btn.cget("state")), "normal")
        app.copy_btn.invoke()
        self.assertEqual(root.clipboard_get(), "hyrule://100.64.0.5:6000?pw=p%20w")
        root.destroy()

    def test_escape_order(self):
        root, app = self.make(1280, 800)
        app.show_page("World")
        app.open_help()
        self.assertTrue(app.help_open)
        app.escape()
        self.assertFalse(app.help_open)
        self.assertEqual(app.tab.get(), "World")
        app.escape()
        self.assertEqual(app.tab.get(), "Play")
        app.pick_mode()
        root.update()
        self.assertIsNotNone(app.overlay)
        self.assertEqual(len(app.focusables()), 4)  # three modes and Cancel
        app.escape()
        self.assertIsNone(app.overlay)
        self.assertEqual(app.mode.get(), L.LOCAL)  # Esc cancels, it never picks
        for ask in (app.pick_special, app.pick_preset):
            ask()
            app.escape()
        self.assertEqual(app.bits, "10111100")
        app.escape()  # the first page with nothing running closes the launcher
        with self.assertRaises(L.tk.TclError):
            root.tk.call("winfo", "exists", ".")

    def test_the_pad_shows_one_cursor(self):
        root, app = self.make(1280, 800)
        root.update()
        app.show_page("Play")
        self.assertIs(root.focus_get(), app.start_btn)
        app.cycle(1)
        root.update()
        self.assertNotIn(root.focus_get(), app.tab_btns.values())  # LB and RB change the page, the cursor stays on its content
        app.show_page("Graphics")
        root.update()
        first = root.focus_get()
        self.assertNotIn(first, app.chrome)
        app.pad_event("down")
        moved = root.focus_get()
        self.assertIsNot(moved, first)
        app.show_page("Online")
        app.show_page("Graphics")
        self.assertIs(root.focus_get(), moved)  # a page remembers where the cursor was
        skin = app.help_btn.skin
        skin.change(hover=True)
        app.pad_event("up")
        self.assertFalse(skin.hover)  # a pointer parked on a control is not a second selection
        skin.change(hover=True)
        self.assertFalse(skin.hover)
        app.pointer_moved(None)
        skin.change(hover=True)
        self.assertTrue(skin.hover)
        root.destroy()

    def test_choice_lists_pick_with_enter(self):
        root, app = self.make(1280, 800)
        app.show_page("Online")
        app.pick_mode()
        root.update()
        self.assertEqual(root.focus_get().cget("text"), L.tr(L.MODE_WORDS[L.LOCAL]))
        app.nav("Down")
        app.activate()
        self.assertEqual(app.mode.get(), L.HOST)
        root.destroy()

    def test_language_changes_the_window(self):
        root, app = self.make(1280, 800)
        app.show_page("Language")
        app.pick_language("es")
        self.assertEqual(app.tab_btns["Play"].cget("text"), "JUGAR")
        self.assertEqual(L.load_conf(app.conf_path)["language"], "es")
        self.assertEqual(app.tab.get(), "Language")
        root.destroy()

    def test_game_language_is_chosen_apart_from_the_launcher_one(self):
        langs = [("en", "English", 1), ("es", "Español (Latinoamérica)", 5), ("fr", "Français (Canada)", 2)]
        with mock.patch.object(L.display, "game_languages", return_value=langs):
            root, app = self.make(1280, 800)
        app.show_page("Language")
        self.assertEqual(set(app.game_btns), {"en", "es", "fr"})
        app.pick_game_language("es")
        saved = L.load_conf(app.conf_path)
        self.assertEqual((saved["game_language"], saved.get("language"), app.game_lang.get(), app.lang.get()), ("es", "en", "es", "en"))
        root.destroy()

    def test_start_without_script_says_so(self):
        root, app = self.make(1280, 800)
        app.start()
        app.render()
        self.assertIn("hyrule-online.sh", app.status.cget("text"))
        app.set_mode(L.JOIN)
        app.v["invite"].set("a..b")
        app.start()
        self.assertEqual(app.tab.get(), "Online")
        app.render()
        self.assertIn("not valid", app.status.cget("text"))
        root.destroy()

    def test_every_family_in_use_is_bundled_or_a_fallback(self):
        root, app = self.make(1280, 800)
        allowed = {L.MENU_FONT, L.TITLE_FONT, L.LOGO_FONT, "TkDefaultFont"}
        for names in L.FALLBACK_FONTS.values():
            allowed.update(names)
        self.assertLessEqual(set(app.fam.values()), allowed)
        if L.register_fonts():
            self.assertEqual((app.fam["menu"], app.fam["title"], app.fam["logo"]), (L.MENU_FONT, L.TITLE_FONT, L.LOGO_FONT))
        root.destroy()


class Fonts(unittest.TestCase):
    def test_bundled_files_and_licences_exist(self):
        for name in L.FONT_FILES + ("Cinzel-OFL.txt", "Roboto-LICENSE.txt"):
            path = os.path.join(L.FONT_DIR, name)
            self.assertTrue(os.path.isfile(path) and os.path.getsize(path) > 1000, name)

    def test_missing_folder_falls_back(self):
        with tempfile.TemporaryDirectory() as empty:
            self.assertFalse(L.register_fonts(empty))

    def test_the_bundled_files_are_the_ones_of_the_windows_launcher(self):
        windows = os.path.join(os.path.dirname(HERE), "src", "Launcher", "Fonts")
        if not os.path.isdir(windows):
            self.skipTest("the Windows sources are not next to the Ally files")
        for name in L.FONT_FILES:
            with open(os.path.join(windows, name), "rb") as a, open(os.path.join(L.FONT_DIR, name), "rb") as b:
                self.assertEqual(a.read(), b.read(), name)

    def test_windows_launcher_families(self):
        self.assertEqual((L.MENU_FONT, L.TITLE_FONT, L.LOGO_FONT), ("Roboto Condensed", "Cinzel", "Cinzel Decorative"))


if __name__ == "__main__":
    unittest.main()

