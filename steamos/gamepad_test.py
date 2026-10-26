"""Tests for gamepad.py with synthetic evdev records: no /dev, no controller, no display. Run: python3 steamos/gamepad_test.py"""
import os
import struct
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import gamepad as G

ABS = (-32767, 32767)


def rec(t, c, v):
    return struct.pack("llHHi", 0, 0, t, c, v)


class Clock:
    now = 100.0

    def __call__(self):
        return self.now


class Fake(G.Dev):
    """A device whose bytes come from a list instead of /dev."""

    def __init__(self, path="/dev/input/event16", virtual=True, **kw):
        super().__init__(None, path, "Microsoft X-Box 360 pad 0", virtual, {0: ABS, 1: ABS}, **kw)
        self.queue = []
        self.fail = None

    def put(self, *records):
        self.queue.append(b"".join(rec(*r) for r in records))

    def _recv(self):
        if self.fail:
            raise self.fail
        if not self.queue:
            raise BlockingIOError()
        return self.queue.pop(0)


def make(devs=None, rescan=3.0):
    clock = Clock()
    box = {"devs": list(devs or []), "problem": "no gamepad device found"}
    pad = G.Pad(clock=clock, scanner=lambda: (list(box["devs"]), [], box["problem"]), rescan=rescan)
    return pad, clock, box


class Mapping(unittest.TestCase):
    def test_buttons(self):
        d = Fake()
        pad, clock, _ = make([d])
        pad.poll()
        want = {0x130: "accept", 0x131: "back", 0x133: "x", 0x134: "y", 0x136: "lb", 0x137: "rb", 0x13A: "view", 0x13B: "menu"}
        d.put(*[(G.EV_KEY, c, 1) for c in want])
        self.assertEqual(pad.poll(), list(want.values()))

    def test_release_repeat_and_unknown_buttons_are_silent(self):
        d = Fake()
        pad, _, _ = make([d])
        pad.poll()
        d.put((G.EV_KEY, 0x130, 0), (G.EV_KEY, 0x130, 2), (G.EV_KEY, 0x13C, 1), (G.EV_KEY, 0x13D, 1), (3, 2, 255), (0, 0, 0))
        self.assertEqual(pad.poll(), [])

    def test_hat_and_dpad_buttons(self):
        d = Fake()
        pad, clock, _ = make([d])
        pad.poll()
        for axis, value, name in ((G.ABS_HAT0X, -1, "left"), (G.ABS_HAT0X, 1, "right"), (G.ABS_HAT0Y, -1, "up"), (G.ABS_HAT0Y, 1, "down")):
            d.put((G.EV_ABS, axis, value))
            self.assertEqual(pad.poll(), [name])
            d.put((G.EV_ABS, axis, 0))
            self.assertEqual(pad.poll(), [])
        d.put((G.EV_KEY, 0x221, 1))
        self.assertEqual(pad.poll(), ["down"])
        d.put((G.EV_KEY, 0x221, 0))
        self.assertEqual(pad.poll(), [])

    def test_stick_deadzone_and_axis_range(self):
        d = Fake()
        pad, _, _ = make([d])
        pad.poll()
        d.put((G.EV_ABS, 0, 12000))  # 37 %: inside the dead zone
        self.assertEqual(pad.poll(), [])
        d.put((G.EV_ABS, 0, 20000))
        self.assertEqual(pad.poll(), ["right"])
        d.put((G.EV_ABS, 0, 13000))  # 40 %: still held (hysteresis), no new event
        self.assertEqual(pad.poll(), [])
        d.put((G.EV_ABS, 0, 0))
        self.assertEqual(pad.poll(), [])
        d.put((G.EV_ABS, 1, -30000))  # pushing up is a negative Y
        self.assertEqual(pad.poll(), ["up"])

    def test_stick_dominant_axis(self):
        d = Fake()
        pad, _, _ = make([d])
        pad.poll()
        d.put((G.EV_ABS, 0, 25000), (G.EV_ABS, 1, 30000))
        self.assertEqual(pad.poll(), ["down"])

    def test_other_axis_range(self):
        d = Fake()
        d.ranges = {0: (0, 255), 1: (0, 255)}  # an 8-bit stick centred on 128
        pad, _, _ = make([d])
        pad.poll()
        d.put((G.EV_ABS, 0, 140))
        self.assertEqual(pad.poll(), [])
        d.put((G.EV_ABS, 0, 20))
        self.assertEqual(pad.poll(), ["left"])

    def test_initial_stick_value_is_read_from_the_device(self):
        d = Fake(start={0: 30000})
        pad, _, _ = make([d])
        self.assertEqual(pad.poll(), ["right"])

    def test_partial_record_is_kept_until_complete(self):
        d = Fake()
        pad, _, _ = make([d])
        pad.poll()
        raw = rec(G.EV_KEY, 0x130, 1)
        d.queue.append(raw[:7])
        self.assertEqual(pad.poll(), [])
        d.queue.append(raw[7:])
        self.assertEqual(pad.poll(), ["accept"])


class Repeat(unittest.TestCase):
    def test_timing(self):
        d = Fake()
        pad, clock, _ = make([d])
        pad.poll()
        t0 = clock.now
        d.put((G.EV_ABS, G.ABS_HAT0Y, 1))
        self.assertEqual(pad.poll(), ["down"])
        got = []
        for ms in range(16, 1000, 16):
            clock.now = t0 + ms / 1000.0
            if pad.poll():
                got.append(ms)
        self.assertTrue(0.4 <= got[0] / 1000.0 < 0.42, got)
        self.assertTrue(all(0.12 <= (b - a) / 1000.0 < 0.14 for a, b in zip(got, got[1:])), got)
        d.put((G.EV_ABS, G.ABS_HAT0Y, 0))
        clock.now += 0.5
        self.assertEqual(pad.poll(), [])
        clock.now += 0.5
        self.assertEqual(pad.poll(), [])

    def test_new_direction_restarts_the_delay(self):
        d = Fake()
        pad, clock, _ = make([d])
        pad.poll()
        d.put((G.EV_ABS, G.ABS_HAT0X, 1))
        self.assertEqual(pad.poll(), ["right"])
        clock.now += 0.3
        d.put((G.EV_ABS, G.ABS_HAT0X, -1))
        self.assertEqual(pad.poll(), ["left"])
        clock.now += 0.3  # 0.3 s after "left": no repeat yet
        self.assertEqual(pad.poll(), [])

    def test_stick_repeats_too(self):
        d = Fake()
        pad, clock, _ = make([d])
        pad.poll()
        d.put((G.EV_ABS, 1, 32767))
        self.assertEqual(pad.poll(), ["down"])
        clock.now += 0.41
        self.assertEqual(pad.poll(), ["down"])


class Devices(unittest.TestCase):
    def test_hot_plug(self):
        pad, clock, box = make()
        self.assertEqual(pad.poll(), [])
        self.assertFalse(pad.available)
        self.assertIn("no gamepad", pad.describe())
        d = Fake()
        box["devs"] = [d]
        clock.now += 1.0  # before the next scan
        self.assertFalse(pad.available or pad.poll())
        clock.now += 3.0
        pad.poll()
        self.assertTrue(pad.available)
        self.assertIn("Steam virtual pad", pad.describe())
        d.put((G.EV_KEY, 0x130, 1))
        self.assertEqual(pad.poll(), ["accept"])

    def test_problem_text_is_reported(self):
        pad, _, box = make()
        box["problem"] = "controller present but not readable: /dev/input/event14"
        pad.poll()
        self.assertEqual(pad.describe(), "no gamepad: controller present but not readable: /dev/input/event14")

    def test_unplug_then_replug(self):
        d = Fake()
        pad, clock, box = make([d])
        pad.poll()
        d.fail = OSError(19, "No such device")
        box["devs"] = []
        pad.poll()
        self.assertFalse(pad.available)
        d2 = Fake()
        box["devs"] = [d2]
        clock.now += 0.1  # the scan is forced right after a loss
        pad.poll()
        self.assertTrue(pad.available)

    def test_known_device_is_not_added_twice(self):
        d = Fake()
        pad, clock, box = make([d])
        pad.poll()
        box["devs"] = [Fake()]  # the scanner opens the same path again
        clock.now += 4
        pad.poll()
        self.assertEqual(len(pad.devs), 1)
        self.assertIs(pad.devs[0], d)

    def test_virtual_pad_wins_over_the_real_one(self):
        real = Fake("/dev/input/event14", virtual=False)
        virtual = Fake("/dev/input/event16")
        pad, _, _ = make([real, virtual])
        pad.poll()
        self.assertEqual(pad.devs, [virtual])
        real.put((G.EV_KEY, 0x130, 1))
        virtual.put((G.EV_KEY, 0x130, 1))
        self.assertEqual(pad.poll(), ["accept"])

    def test_real_pad_is_used_when_alone(self):
        real = Fake("/dev/input/event14", virtual=False)
        pad, _, _ = make([real])
        real.put((G.EV_KEY, 0x131, 1))
        self.assertEqual(pad.poll(), ["back"])
        self.assertNotIn("Steam virtual", pad.describe())

    def test_close(self):
        pad, _, _ = make([Fake()])
        pad.poll()
        pad.close()
        self.assertFalse(pad.available)


class Safety(unittest.TestCase):
    def test_poll_never_raises(self):
        def boom():
            raise RuntimeError("scan exploded")
        pad = G.Pad(scanner=boom)
        self.assertEqual(pad.poll(), [])
        self.assertEqual(pad.poll(), [])

    def test_device_errors_never_escape(self):
        d = Fake()
        d.fail = ValueError("garbage")
        pad, _, _ = make([d])
        self.assertEqual(pad.poll(), [])
        d.fail = OSError(5, "I/O error")
        self.assertEqual(pad.poll(), [])
        self.assertFalse(pad.available)

    def test_trace_error_is_contained(self):
        d = Fake()
        pad, _, _ = make([d])
        pad.trace = lambda *a: 1 / 0
        d.put((G.EV_KEY, 0x130, 1))
        self.assertEqual(pad.poll(), [])

    def test_real_scan_on_this_machine_does_not_raise(self):
        pad = G.Pad()
        self.assertIsInstance(pad.poll(), list)
        self.assertIsInstance(pad.describe(), str)
        pad.close()

    def test_scan_of_missing_dir(self):
        devs, notes, problem = G.scan(os.path.join(HERE, "no-such-dir"))
        self.assertEqual(devs, [])
        self.assertTrue(problem)


@unittest.skipUnless(os.name == "posix", "needs a POSIX pipe")
class RealRead(unittest.TestCase):
    def test_pipe_with_the_real_read_path(self):
        r, w = os.pipe()
        os.set_blocking(r, False)
        d = G.Dev(r, "/pipe", "pipe pad", True, {0: ABS, 1: ABS})
        pad, _, _ = make([d])
        pad.poll()
        os.write(w, rec(G.EV_KEY, 0x13B, 1) + rec(G.EV_ABS, G.ABS_HAT0X, 1) + rec(0, 0, 0))
        self.assertEqual(pad.poll(), ["menu", "right"])
        os.close(w)
        pad.poll()  # EOF: the device is dropped
        self.assertFalse(pad.available)
        pad.close()


if __name__ == "__main__":
    unittest.main()
