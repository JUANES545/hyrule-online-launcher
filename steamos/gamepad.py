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

EV_KEY, EV_ABS, EV_FF = 1, 3, 0x15
ABS_X, ABS_Y, ABS_Z, ABS_RZ, ABS_HAT0X, ABS_HAT0Y = 0, 1, 2, 5, 16, 17
FF_RUMBLE = 0x50
BTN_SOUTH = 0x130
# The inputs a menu shortcut can use, named as in the Windows launcher's MenuShortcut.
SHORTCUT_NAMES = ("Up", "Down", "Left", "Right", "Start", "Back", "L3", "R3", "LB", "RB", "A", "B", "X", "Y", "LT", "RT")
SHORTCUT_DEFAULT, HOLD_S, SHORTCUT_MAX = "Start+LT", 1.5, 4
KEY_NAMES = {0x130: "A", 0x131: "B", 0x133: "X", 0x134: "Y", 0x136: "LB", 0x137: "RB", 0x138: "LT", 0x139: "RT", 0x13A: "Back", 0x13B: "Start", 0x13D: "L3", 0x13E: "R3"}
HAT_NAMES = (("Left", "Right"), ("Up", "Down"))  # hat axis -> (negative, positive)
TRIGGER_NAMES = {ABS_Z: "LT", ABS_RZ: "RT"}
TRIGGER_ON, TRIGGER_OFF = 204 / 255, 130 / 255  # the Windows launcher's thresholds, as a fraction of the axis range
BUTTONS = {0x130: "accept", 0x131: "back", 0x133: "x", 0x134: "y", 0x136: "lb", 0x137: "rb", 0x13A: "view", 0x13B: "menu"}
DPAD_BUTTONS = {0x220: (1, -1), 0x221: (1, 1), 0x222: (0, -1), 0x223: (0, 1)}  # BTN_DPAD_*: (axis, sign)
REC = struct.calcsize("llHHi")  # struct input_event
FIRST_REPEAT, NEXT_REPEAT, RESCAN = 0.4, 0.12, 3.0
STICK_ON, STICK_OFF = 0.5, 0.35  # deflection to start / keep a stick direction
PAD_NAME = re.compile(r"pad|controller|joystick|x-?box", re.I)


def _ioc(nr, size, ty=ord("E"), write=False):
    return ((1 if write else 2) << 30) | (size << 16) | (ty << 8) | nr


def parse_shortcut(text):
    """'Start+LT' -> {'Start', 'LT'}; None when it is not a usable shortcut (unknown input, fewer than 2 or more than 4 inputs,
    or both small centre buttons: Moonlight quits the stream with Start+Back+LB+RB)."""
    names = {part.strip() for part in (text or "").split("+")}
    ok = names <= set(SHORTCUT_NAMES) and 2 <= len(names) <= SHORTCUT_MAX and not {"Start", "Back"} <= names
    return names if ok else None


def format_shortcut(names):
    return "+".join(n for n in SHORTCUT_NAMES if n in names)


class Tracker:
    """Fires once when every input of the shortcut has been held together for HOLD_S seconds, then again only after a release."""

    def __init__(self):
        self.since, self.fired, self.engaged = None, False, False

    def update(self, names, held, now):
        self.engaged = bool(names) and names <= held
        if not self.engaged:
            self.since, self.fired = None, False
            return False
        if self.since is None:
            self.since = now
        if self.fired or now - self.since < HOLD_S:
            return False
        self.fired = True
        return True


class Tap:
    """Start as a menu action. When the shortcut uses it, the action waits for the release and is dropped if anything else was held
    meanwhile, so opening or closing the menu with the shortcut never presses Start session."""

    def __init__(self):
        self.down = self.spoiled = False

    def update(self, held, name, on_release):
        mine, other = name in held, bool(held - {name})
        fire = False
        if not on_release:
            fire = mine and not self.down
        elif mine and not self.down:
            self.spoiled = other
        elif mine and other:
            self.spoiled = True
        elif not mine and self.down:
            fire = not self.spoiled
        self.down = mine
        return fire


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
        self.keys, self.triggers = set(), set()  # shortcut inputs held: buttons, triggers
        self.ff, self.effect = None, -1  # a second, writable handle for rumble, and the effect uploaded through it
        for code, value in (start or {}).items():
            self._abs(code, value)

    def _recv(self):
        return os.read(self.fd, REC * 64)

    def close(self):
        for attr in ("fd", "ff"):
            fd = getattr(self, attr)
            if fd is not None:
                try:
                    os.close(fd)
                except OSError:
                    pass
                setattr(self, attr, None)

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
        elif code in TRIGGER_NAMES:
            name = TRIGGER_NAMES[code]
            lo, hi = self.ranges.get(code, (0, 255))
            pull = (value - lo) / ((hi - lo) or 1)
            (self.triggers.add if pull >= (TRIGGER_OFF if name in self.triggers else TRIGGER_ON) else self.triggers.discard)(name)
        elif code in (ABS_X, ABS_Y):
            lo, hi = self.ranges.get(code, (-32768, 32767))
            half = (hi - lo) / 2.0 or 1.0
            self.stick[code] = (value - (lo + hi) / 2.0) / half

    def feed(self, etype, code, value, out):
        """Apply one record; button presses are appended to out, axes only update the state."""
        if etype == EV_KEY:
            if code in KEY_NAMES and value in (0, 1):
                (self.keys.add if value else self.keys.discard)(KEY_NAMES[code])
            if code in DPAD_BUTTONS and value in (0, 1):
                axis, sign = DPAD_BUTTONS[code]
                self.hat[axis] = sign * value
            elif value == 1 and code in BUTTONS:
                out.append(BUTTONS[code])
        elif etype == EV_ABS:
            self._abs(code, value)

    def held(self):
        """Names (SHORTCUT_NAMES) of the buttons, D-pad directions and triggers held right now."""
        pressed = self.keys | self.triggers
        for axis, value in enumerate(self.hat):
            if value:
                pressed.add(HAT_NAMES[axis][value > 0])
        return pressed

    def rumble(self, ms):
        """A short vibration through a writable handle of the same device (the Steam virtual pad hands it to the real pad). Never raises."""
        try:
            if self.ff is None:
                self.ff = os.open(self.path, os.O_RDWR | os.O_NONBLOCK | os.O_CLOEXEC)
            if self.effect >= 0:
                fcntl.ioctl(self.ff, _ioc(0x81, 4, write=True), self.effect)  # EVIOCRMFF: the previous pulse
            effect = bytearray(48)  # struct ff_effect: type, id, direction, trigger, replay {length, delay}, then the rumble union at 16
            struct.pack_into("HhH", effect, 0, FF_RUMBLE, -1, 0)
            struct.pack_into("HH", effect, 10, ms, 0)
            struct.pack_into("HH", effect, 16, 0xA000, 0xA000)
            fcntl.ioctl(self.ff, _ioc(0x80, 48, write=True), effect, True)  # EVIOCSFF fills in the id
            self.effect = struct.unpack_from("h", effect, 2)[0]
            os.write(self.ff, struct.pack("llHHi", 0, 0, EV_FF, self.effect, 1))
            return True
        except (OSError, AttributeError, struct.error):
            return False

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
            for code in (ABS_X, ABS_Y, ABS_Z, ABS_RZ):
                if code > ABS_Y and not absb(code):
                    continue
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

    def held(self):
        """Everything held on any gamepad, as SHORTCUT_NAMES."""
        return set().union(*(d.held() for d in self.devs))

    def rumble(self, ms=180):
        for d in self.devs:
            d.rumble(ms)

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
