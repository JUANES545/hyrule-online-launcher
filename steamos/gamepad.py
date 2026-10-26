#!/usr/bin/env python3
"""Gamepad events for the Hyrule Online SteamOS launcher (Tk has no controller support of its own).

Reads /dev/input/event* with the plain evdev protocol (stdlib only, no root, no grab) and turns it into
menu events. In Gaming Mode Steam Input hides the real pad (mode 000) and exposes a virtual
"Microsoft X-Box 360 pad" (28de:11ff) to the app, so that one is preferred; any other readable pad works too.

    pad = Pad(); events = pad.poll()   # 'up' 'down' 'left' 'right' 'accept' 'back' 'lb' 'rb' 'menu' 'view' 'x' 'y'
    python3 gamepad.py --list | --watch [seconds]
"""
import errno
import glob
import os
import re
import struct
import sys
import time

try:
    import fcntl
except ImportError:  # not Linux: the tests still run, a real scan reports it
    fcntl = None

EV_KEY, EV_ABS = 1, 3
ABS_X, ABS_Y, ABS_HAT0X, ABS_HAT0Y = 0, 1, 16, 17
BTN_SOUTH = 0x130
BUTTONS = {0x130: "accept", 0x131: "back", 0x133: "x", 0x134: "y", 0x136: "lb", 0x137: "rb", 0x13A: "view", 0x13B: "menu"}
DPAD_BUTTONS = {0x220: (1, -1), 0x221: (1, 1), 0x222: (0, -1), 0x223: (0, 1)}  # BTN_DPAD_*: (axis, sign)
REC = struct.calcsize("llHHi")  # struct input_event
FIRST_REPEAT, NEXT_REPEAT, RESCAN = 0.4, 0.12, 3.0
STICK_ON, STICK_OFF = 0.5, 0.35  # deflection to start / keep a stick direction
PAD_NAME = re.compile(r"pad|controller|joystick|x-?box", re.I)


def _ioc(nr, size, ty=ord("E")):
    return (2 << 30) | (size << 16) | (ty << 8) | nr


class Dev:
    """One input device. Tests subclass it and override _recv()."""

    def __init__(self, fd, path, name, virtual=False, ranges=None, start=None):
        self.fd, self.path, self.name, self.virtual = fd, path, name, virtual
        self.ranges = ranges or {}  # abs code -> (min, max)
        self.dead = False
        self.buf = b""
        self.hat = [0, 0]
        self.stick = [0.0, 0.0]
        self.stick_dir = None
        for code, value in (start or {}).items():
            self._abs(code, value)

    def _recv(self):
        return os.read(self.fd, REC * 64)

    def close(self):
        if self.fd is not None:
            try:
                os.close(self.fd)
            except OSError:
                pass
            self.fd = None

    def read(self):
        """Every complete (type, code, value) record waiting on the device; marks it dead when it is gone."""
        while True:
            try:
                data = self._recv()
            except BlockingIOError:
                break
            except InterruptedError:
                continue
            except OSError:
                self.dead = True
                break
            if not data:
                self.dead = True
                break
            self.buf += data
        n = len(self.buf) // REC * REC
        raw, self.buf = self.buf[:n], self.buf[n:]
        return [struct.unpack_from("llHHi", raw, i)[2:] for i in range(0, n, REC)]

    def _abs(self, code, value):
        if code == ABS_HAT0X:
            self.hat[0] = (value > 0) - (value < 0)
        elif code == ABS_HAT0Y:
            self.hat[1] = (value > 0) - (value < 0)
        elif code in (ABS_X, ABS_Y):
            lo, hi = self.ranges.get(code, (-32768, 32767))
            half = (hi - lo) / 2.0 or 1.0
            self.stick[code] = (value - (lo + hi) / 2.0) / half

    def feed(self, etype, code, value, out):
        """Apply one record; button presses are appended to out, axes only update the state."""
        if etype == EV_KEY:
            if code in DPAD_BUTTONS and value in (0, 1):
                axis, sign = DPAD_BUTTONS[code]
                self.hat[axis] = sign * value
            elif value == 1 and code in BUTTONS:
                out.append(BUTTONS[code])
        elif etype == EV_ABS:
            self._abs(code, value)

    def direction(self):
        """'left'/'right'/'up'/'down' currently held (D-pad first, then the stick), or None."""
        hx, hy = self.hat
        if hx or hy:
            return ("left" if hx < 0 else "right") if hx else ("up" if hy < 0 else "down")
        x, y = self.stick
        limit = STICK_OFF if self.stick_dir else STICK_ON
        if max(abs(x), abs(y)) < limit:
            self.stick_dir = None
        elif abs(x) >= abs(y):
            self.stick_dir = "left" if x < 0 else "right"
        else:
            self.stick_dir = "up" if y < 0 else "down"
        return self.stick_dir


def _bits(fd, ev, size=96):
    buf = bytearray(size)
    fcntl.ioctl(fd, _ioc(0x20 + ev, size), buf, True)
    return lambda n: bool(buf[n // 8] >> (n % 8) & 1)


def _sysname(path):
    try:
        with open("/sys/class/input/%s/device/name" % os.path.basename(path)) as f:
            return f.read().strip()
    except OSError:
        return ""


def scan(input_dir="/dev/input"):
    """Open every readable gamepad under input_dir. Returns (devices, notes, problem)."""
    devs, notes, problem = [], [], ""
    if fcntl is None:
        return devs, notes, "gamepad input needs Linux evdev"
    paths = sorted(glob.glob(os.path.join(input_dir, "event*")), key=lambda p: int(re.sub(r"\D", "", p) or 0))
    if not paths:
        return devs, notes, "no input devices in %s" % input_dir
    denied = []
    for path in paths:
        try:
            fd = os.open(path, os.O_RDONLY | os.O_NONBLOCK | os.O_CLOEXEC)
        except OSError as e:
            if e.errno in (errno.EACCES, errno.EPERM):
                name = _sysname(path)
                notes.append("%s: %s: permission denied" % (path, name or "?"))
                if PAD_NAME.search(name):
                    denied.append("%s (%s)" % (path, name))
            else:
                notes.append("%s: %s" % (path, e.strerror))
            continue
        try:
            buf = bytearray(256)
            fcntl.ioctl(fd, _ioc(0x06, 256), buf, True)
            name = bytes(buf).split(b"\0", 1)[0].decode("utf-8", "replace")
            ident = bytearray(8)
            fcntl.ioctl(fd, _ioc(0x02, 8), ident, True)
            bus, vendor, product, _ = struct.unpack("HHHH", bytes(ident))
            key, absb = _bits(fd, EV_KEY), _bits(fd, EV_ABS)
            if not (key(BTN_SOUTH) and absb(ABS_X) and absb(ABS_Y)):
                notes.append("%s: %s: not a gamepad" % (path, name))
                os.close(fd)
                continue
            ranges, start = {}, {}
            for code in (ABS_X, ABS_Y):
                info = bytearray(24)
                fcntl.ioctl(fd, _ioc(0x40 + code, 24), info, True)
                value, lo, hi = struct.unpack_from("iii", bytes(info))
                ranges[code], start[code] = (lo, hi), value
            virtual = vendor == 0x28DE and (product == 0x11FF or "x-box 360" in name.lower())
            devs.append(Dev(fd, path, name, virtual, ranges, start))
            notes.append("%s: %s: gamepad%s" % (path, name, " (Steam virtual pad)" if virtual else ""))
        except Exception as e:
            os.close(fd)
            notes.append("%s: %s" % (path, e))
    if not devs:
        if denied:
            problem = ("controller present but not readable: %s. Steam Input hides the real pad; set the shortcut's "
                       "controller layout to a gamepad template or keyboard layout (see steamos/README.md, Controller)"
                       % ", ".join(denied))
        else:
            problem = "no gamepad device found (Steam Input exposes none for this shortcut)"
    return devs, notes, problem


class Pad:
    def __init__(self, clock=time.monotonic, scanner=scan, rescan=RESCAN, trace=None):
        self.clock, self.scanner, self.rescan, self.trace = clock, scanner, rescan, trace
        self.devs = []
        self.problem = "not scanned yet"
        self._next_scan = 0.0
        self._cur, self._next = None, 0.0
        self._failed = False

    @property
    def available(self):
        return bool(self.devs)

    def describe(self):
        if not self.devs:
            return "no gamepad: " + self.problem
        return "gamepad: " + "; ".join("%s (%s%s)" % (d.name, d.path, ", Steam virtual pad" if d.virtual else "") for d in self.devs)

    def close(self):
        for d in self.devs:
            d.close()
        self.devs = []

    def _rescan(self, now):
        self._next_scan = now + self.rescan
        known = {d.path for d in self.devs}
        found, _, problem = self.scanner()
        for d in found:
            if d.path in known:
                d.close()
            else:
                self.devs.append(d)
        if any(d.virtual for d in self.devs):  # the real pad is hidden by Steam; never double-count it
            for d in [d for d in self.devs if not d.virtual]:
                d.close()
            self.devs = [d for d in self.devs if d.virtual]
        self.problem = problem

    def poll(self):
        try:
            return self._poll()
        except Exception as e:
            if not self._failed:
                self._failed = True
                print("gamepad: %s: %s" % (type(e).__name__, e), file=sys.stderr)
            return []

    def _poll(self):
        now = self.clock()
        if now >= self._next_scan:
            self._rescan(now)
        out, want = [], None
        for d in self.devs:
            for etype, code, value in d.read():
                if self.trace:
                    self.trace(d, etype, code, value)
                d.feed(etype, code, value, out)
            held = d.direction()
            want = want or held
        for d in [d for d in self.devs if d.dead]:
            d.close()
            self.devs.remove(d)
            self._next_scan = 0.0
        if want != self._cur:
            self._cur = want
            if want:
                out.append(want)
                self._next = now + FIRST_REPEAT
        elif want and now >= self._next:
            out.append(want)
            self._next = now + NEXT_REPEAT
        return out


def main(argv):
    mode = argv[1] if len(argv) > 1 else ""
    if mode not in ("--list", "--watch"):
        print("usage: gamepad.py --list | --watch [seconds]")
        return 2
    devs, notes, problem = scan()
    print("\n".join(notes) or "(no devices)")
    for d in devs:
        d.close()
    if mode == "--list":
        print("would use:" if devs else "would use: nothing -- " + problem)
        pad = Pad()
        pad.poll()
        print(pad.describe())
        pad.close()
        return 0
    stop = time.monotonic() + float(argv[2]) if len(argv) > 2 else None
    pad = Pad(trace=lambda d, t, c, v: print("raw %s type=%d code=0x%x value=%d" % (os.path.basename(d.path), t, c, v)) if t in (EV_KEY, EV_ABS) else None)
    pad.poll()
    print(pad.describe())
    try:
        while stop is None or time.monotonic() < stop:
            for e in pad.poll():
                print("event:", e)
            sys.stdout.flush()
            time.sleep(0.016)
    except KeyboardInterrupt:
        pass
    pad.close()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
