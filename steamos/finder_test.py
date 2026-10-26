"""Tests for finder.py. Run: python3 steamos/finder_test.py. The vectors are the ones of the shared server-finder spec (Windows: tests/ServerFinderTests.cs).
They need no network: probes are injected."""
import json
import os
import sys
import threading
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import finder as F

INTERFACES = [("192.168.1.43", 24), ("10.1.2.3", 16), ("100.65.1.1", 32), ("169.254.3.4", 16), ("127.0.0.1", 8), ("172.20.5.9", 30)]
TAILSCALE = ('{"Self":{"TailscaleIPs":["100.1.1.1","fd7a::1"],"Online":true},"Peer":{"k1":{"TailscaleIPs":["100.65.234.99","fd7a::2"],"Online":true},'
             '"k2":{"TailscaleIPs":["100.70.0.2"],"Online":false},"k3":{"TailscaleIPs":["fd7a::3"],"Online":true}}}')


def row(via, host, ms):
    return F.Found(host, F.DEFAULT_PORT, via, ms)


class Vectors(unittest.TestCase):
    def test_constants(self):
        self.assertEqual((F.DEFAULT_PORT, F.PROBE_S, F.WORKERS, F.MAX_HOSTS, F.TEST_S, F.MAX_ROWS), (5050, 0.4, 64, 1024, 1.5, 6))

    def test_classify(self):
        for host, via in (("100.65.234.99", "vpn"), ("100.64.0.1", "vpn"), ("100.127.255.254", "vpn"), ("100.63.255.255", "lan"),
                          ("100.128.0.0", "lan"), ("192.168.1.8", "lan"), ("10.0.0.5", "lan"), ("my-host.example", "lan")):
            self.assertEqual(F.classify(host), via, host)

    def test_local_hosts(self):
        hosts = F.local_hosts(INTERFACES)
        want = ["192.168.1.%d" % n for n in range(1, 255) if n != 43] + ["10.1.2.%d" % n for n in range(1, 255) if n != 3] + ["172.20.5.10"]
        self.assertEqual(hosts, want)
        self.assertEqual(len(hosts), 253 + 253 + 1)
        self.assertEqual(F.local_hosts([("100.65.1.1", 32), ("169.254.3.4", 16), ("127.0.0.1", 8), ("8.8.8.8", 24)]), [])

    def test_tailscale(self):
        self.assertEqual(F.parse_tailscale(TAILSCALE), ["100.65.234.99"])
        for text in ("", "garbage", "[]", "null", '{"Peer":[1]}', '{"Peer":{"a":5}}'):
            self.assertEqual(F.parse_tailscale(text), [], text)

    def test_order(self):
        rows = [row("lan", "192.168.1.8", 4), row("vpn", "100.70.0.2", 30), row("vpn", "100.65.0.9", 30), row("vpn", "100.99.0.1", 2), row("lan", "192.168.1.20", 4)]
        self.assertEqual([r.host for r in F.order(rows)], ["100.99.0.1", "100.65.0.9", "100.70.0.2", "192.168.1.8", "192.168.1.20"])

    def test_autofill(self):
        rows = [row("vpn", "100.99.0.1", 2), row("lan", "192.168.1.8", 4)]
        self.assertEqual(F.autofill("", "", rows), "100.99.0.1:5050")
        self.assertIsNone(F.autofill("  ", "", rows))  # whitespace is not empty: never overwritten
        self.assertEqual(F.autofill("192.168.1.8:5050", "192.168.1.8:5050", rows), "100.99.0.1:5050")
        self.assertIsNone(F.autofill("x:1", "192.168.1.8:5050", rows))
        self.assertIsNone(F.autofill("x:1", "", rows))
        self.assertIsNone(F.autofill("", "", []))

    def test_cap_keeps_the_vpn_ones(self):
        vpn = ["100.64.%d.%d" % (n // 250, n % 250 + 1) for n in range(300)]
        many = [("10.%d.0.5" % n, 24) for n in range(10)]
        out = F.candidates(vpn, many, ("192.168.9.9", 5050))
        self.assertEqual(len(out), 1024)  # 300 + 1 + 2530 candidates are cut to 1024
        self.assertEqual([c[0] for c in out[:300]], vpn)
        self.assertEqual(out[300], ("192.168.9.9", 5050))

    def test_candidates(self):
        out = F.candidates(["100.65.234.99", "192.168.1.43"], INTERFACES, ("192.168.1.20", 6000))
        self.assertEqual(out[0], ("100.65.234.99", 5050))  # the VPN peer first
        self.assertNotIn(("192.168.1.43", 5050), out)  # never this device
        self.assertIn(("192.168.1.20", 6000), out)
        self.assertIn(("192.168.1.20", 5050), out)
        self.assertEqual(len(out), len(set(out)))
        self.assertEqual(F.candidates([], [], ("100.65.0.1", 5050)), [("100.65.0.1", 5050)])
        self.assertEqual(F.candidates([], [("100.65.0.1", 32)], ("100.65.0.1", 5050)), [])
        self.assertEqual(F.candidates([], [], ("my-host.example", 5050)), [])  # a name would need a DNS lookup
        self.assertEqual(F.candidates([], [], ("localhost", 5050)), [])


class Probing(unittest.TestCase):
    def test_interfaces_from_ip_output(self):
        text = "1: lo    inet 127.0.0.1/8 scope host lo\n2: wlan0    inet 192.168.1.43/24 brd 192.168.1.255 scope global wlan0\n3: tailscale0    inet 100.110.16.75/32 scope global tailscale0\n"
        self.assertEqual(F.parse_interfaces(text), [("127.0.0.1", 8), ("192.168.1.43", 24), ("100.110.16.75", 32)])

    def test_probe_a_listening_port_and_a_closed_one(self):
        import socket
        with socket.socket() as srv:
            srv.bind(("127.0.0.1", 0))
            srv.listen(1)
            port = srv.getsockname()[1]
            self.assertGreaterEqual(F.probe("127.0.0.1", port), 1)
        self.assertIsNone(F.probe("127.0.0.1", port))
        self.assertIsNone(F.probe("not a host", 5050))

    def test_search_probes_with_the_short_timeout_and_orders(self):
        seen, lock = [], threading.Lock()

        def fake(host, port, timeout):
            with lock:
                seen.append((host, port, timeout))
            return {"192.168.1.8": 5, "100.65.234.99": 40}.get(host)
        rows, vpn = F.search(("192.168.1.8", 5050), interfaces=[("192.168.1.43", 24)], status=lambda: TAILSCALE, probe_fn=fake)
        self.assertEqual([(r.host, r.via, r.ms) for r in rows], [("100.65.234.99", "vpn", 40), ("192.168.1.8", "lan", 5)])
        self.assertEqual(vpn, 1)
        self.assertEqual({t for _, _, t in seen}, {F.PROBE_S})
        self.assertEqual(len(seen), 1 + 253)

    def test_no_tailscale_means_no_vpn_candidates(self):
        rows, vpn = F.search(None, interfaces=[], status=lambda: "", probe_fn=lambda *a: 1)
        self.assertEqual((rows, vpn), ([], 0))

    def test_cancel_stops_the_probes(self):
        cancel, calls = threading.Event(), []
        cancel.set()
        rows, _ = F.search(None, cancel, interfaces=[("10.0.0.1", 24)], status=lambda: "", probe_fn=lambda *a: calls.append(a) or 1)
        self.assertEqual((rows, calls), ([], []))

    def test_check_uses_the_long_timeout(self):
        self.assertEqual(F.check("h", 1, lambda host, port, timeout: timeout), 1.5)

    def test_missing_cli_gives_nothing(self):
        from unittest import mock
        with mock.patch.object(F.shutil, "which", return_value=None):
            self.assertEqual(F.tailscale_status(), "")

    def test_failing_cli_gives_nothing(self):
        from unittest import mock
        with mock.patch.object(F.shutil, "which", return_value="tailscale"), mock.patch.object(F.subprocess, "run", side_effect=OSError):
            self.assertEqual(F.tailscale_status(), "")
        with mock.patch.object(F.shutil, "which", return_value="tailscale"), \
                mock.patch.object(F.subprocess, "run", side_effect=F.subprocess.TimeoutExpired("tailscale", 3)):
            self.assertEqual(F.tailscale_status(), "")

    def test_the_cli_output_is_used(self):
        from unittest import mock
        done = mock.Mock(stdout=TAILSCALE)
        with mock.patch.object(F.shutil, "which", return_value="/usr/bin/tailscale"), mock.patch.object(F.subprocess, "run", return_value=done) as run:
            self.assertEqual(F.parse_tailscale(F.tailscale_status()), ["100.65.234.99"])
        self.assertEqual(run.call_args[0][0], ["/usr/bin/tailscale", "status", "--json"])


class Parity(unittest.TestCase):
    """The shared vectors of steamos/parity.json (key "finder"), which the Windows tests check against ServerFinder.cs."""
    def setUp(self):
        try:
            with open(os.path.join(HERE, "parity.json"), encoding="utf-8") as f:
                self.v = json.load(f).get("finder")
        except OSError:
            self.v = None
        if not self.v:
            self.skipTest("parity.json has no finder vectors")

    def test_vectors(self):
        v = self.v
        c = v["constants"]
        self.assertEqual((F.DEFAULT_PORT, F.PROBE_S * 1000, F.WORKERS, F.MAX_HOSTS, F.TEST_S * 1000, F.MAX_ROWS), (c["port"], c["probeMs"], c["probes"], c["maxHosts"], c["testMs"], c["rows"]))
        for host, via in v["classify"].items():
            self.assertEqual(F.classify(host), via, host)
        want = [h["prefix"] + str(n) for h in v["local"]["hosts"] for n in range(h["from"], h["to"] + 1) if n not in h["except"]]
        self.assertEqual(F.local_hosts([tuple(i) for i in v["local"]["interfaces"]]), want)
        self.assertEqual(F.parse_tailscale(v["tailscale"]["json"]), v["tailscale"]["hosts"])
        for text in v["tailscale"]["garbage"]:
            self.assertEqual(F.parse_tailscale(text), [], text)
        rows = [F.Found(h, 5050, via, ms) for via, h, ms in v["order"]["in"]]
        self.assertEqual([r.host for r in F.order(rows)], v["order"]["out"])
        for a in v["autofill"]:
            self.assertEqual(F.autofill(a["field"], a["finder"], [row("vpn", "100.99.0.1", 2)]) is not None, a["fills"], a)
        self.assertEqual(len(F.candidates(["100.64.%d.%d" % (n // 250, n % 250 + 1) for n in range(2000)], [], None)), v["cap"]["max"])


if __name__ == "__main__":
    unittest.main()
