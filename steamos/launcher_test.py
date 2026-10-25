"""Logic tests for launcher.py. Run: python3 steamos/launcher_test.py (no display, no Tk, no Ally needed)."""
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import launcher as L


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


class Status(unittest.TestCase):
    def test_line(self):
        s = L.parse_status("version=0.1.0 mode=host server=up client=down joined=no port=5050 invite=hyrule://192.168.1.5:5050?pw=x invite_vpn=hyrule://100.64.0.5:5050?pw=x\n")
        self.assertEqual((s["version"], s["mode"], s["server"], s["client"], s["joined"], s["port"]), ("0.1.0", "host", "up", "down", "no", "5050"))
        self.assertEqual((s["invite"], s["invite_vpn"]), ("hyrule://192.168.1.5:5050?pw=x", "hyrule://100.64.0.5:5050?pw=x"))
        self.assertEqual(L.parse_status("version=0.1.0 mode=idle server=down client=down joined=no port=")["port"], "")
        self.assertEqual(L.parse_status("version=0.1.0 mode=join server=down client=up joined=yes port=5050 host=h")["host"], "h")

    def test_not_a_status(self):
        self.assertIsNone(L.parse_status("That is not a valid invite or address: --status\nUse hyrule://host:port?pw=PASSWORD, host:port"))
        self.assertIsNone(L.parse_status(""))


class Rules(unittest.TestCase):
    def test_presets(self):
        self.assertTrue(all(L.valid_bits(b) for b in L.PRESETS.values()))
        self.assertEqual(L.rules_name("10111100"), "Game Completion")
        self.assertEqual(L.rules_name("00000002"), "Death Swap")
        self.assertEqual(L.rules_name("11111110"), "Custom")
        self.assertFalse(L.valid_bits("1011110"))
        self.assertFalse(L.valid_bits("10111103"))

    def test_presets_output(self):
        got = L.parse_presets("10111100 Game Completion\r\n10000000 Hide n' seek\nbad line\n00000002 Death Swap\n1011110 Short\n")
        self.assertEqual(list(got.items()), [("Game Completion", "10111100"), ("Hide n' seek", "10000000"), ("Death Swap", "00000002")])
        self.assertEqual(L.rules_name("10000000", got), "Hide n' seek")
        self.assertEqual(L.parse_presets(""), {})

    def test_command_line(self):
        f = dict(port="5050", hostpass=" pw\n", desc=" Hi there ", invite="hyrule://100.64.0.5:6000?pw=inv", joinpass="", name="Link")
        self.assertEqual(L.build_args(L.LOCAL, f, "10000000"), (["--local", "--rules", "10000000", "--desc", "Hi there"], None))
        self.assertEqual(L.build_args(L.HOST, f, "00000002"), (["--host", "5050", "pw", "--rules", "00000002", "--desc", "Hi there"], None))
        self.assertEqual(L.build_args(L.HOST, dict(f, hostpass="", desc=" "))[0][:3], ["--host", "5050", ""])
        self.assertEqual(L.build_args(L.HOST, dict(f, desc=""))[0][-1], L.DEFAULT_DESC)
        self.assertEqual(L.build_args(L.JOIN, f), (["100.64.0.5", "6000", "inv", "Link"], None))
        self.assertEqual(L.build_args(L.JOIN, dict(f, joinpass="own"))[0][2], "own")
        self.assertEqual(L.build_args(L.JOIN, dict(f, invite="h"))[0], ["h", "5050", "", "Link"])

    def test_bad_input(self):
        f = dict(port="5050", hostpass="", desc="d", invite="h:1", joinpass="", name="Link")
        for port in ("1023", "65536", "", "50 50", "abc"):
            self.assertIsNone(L.build_args(L.HOST, dict(f, port=port))[0], port)
        for pw in ("a;b", "x" * 41):
            self.assertIsNone(L.build_args(L.HOST, dict(f, hostpass=pw))[0], pw)
        self.assertEqual(L.build_args(L.HOST, dict(f, hostpass="x" * 40))[0][2], "x" * 40)
        self.assertIsNone(L.build_args(L.LOCAL, dict(f, desc="x" * 81))[0])
        self.assertIsNone(L.build_args(L.HOST, dict(f, desc="x" * 81))[0])
        self.assertEqual(L.build_args(L.HOST, dict(f, desc="x" * 80))[0][-1], "x" * 80)
        self.assertIsNone(L.build_args(L.JOIN, dict(f, invite="a..b"))[0])
        self.assertIsNone(L.build_args(L.JOIN, dict(f, name="bad name"))[0])
        self.assertIsNone(L.build_args(L.JOIN, dict(f, name="x" * 17))[0])
        self.assertIsNone(L.build_args(L.JOIN, dict(f, name=""))[0])


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

    def test_version(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertEqual(L.read_version((d,)), "unknown")
            with open(os.path.join(d, "VERSION"), "w") as f:
                f.write(" 0.1.0 \r\n")
            self.assertEqual(L.read_version((d,)), "0.1.0")


if __name__ == "__main__":
    unittest.main()
