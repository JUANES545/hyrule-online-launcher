#!/usr/bin/env python3
"""Add or remove one non-Steam shortcut in Steam's binary shortcuts.vdf (standard library only).

  steam_shortcut.py add EXE NAME FILE... [--startdir DIR] [--icon FILE] [--launch-options TEXT] [--tag TAG]
  steam_shortcut.py remove EXE FILE...
  steam_shortcut.py appid EXE NAME

Existing entries are kept as they are. A file that changes is backed up first as
shortcuts.vdf.bak-hyrule-<timestamp>. Steam rewrites the file when it exits, so run this while Steam is closed.
"""
import argparse
import os
import shutil
import struct
import sys
import time
import zlib


def appid(exe, name):
    # exe is the quoted path exactly as stored in the shortcut
    return (zlib.crc32((exe + name).encode()) & 0xFFFFFFFF) | 0x80000000


def parse(data):
    pos = 0

    def cstr():
        nonlocal pos
        end = data.index(b"\0", pos)
        text = data[pos:end].decode("utf-8")
        pos = end + 1
        return text

    def read_map():
        nonlocal pos
        out = {}
        while True:
            kind = data[pos]
            pos += 1
            if kind == 8:
                return out
            key = cstr()
            if kind == 0:
                out[key] = read_map()
            elif kind == 1:
                out[key] = cstr()
            elif kind == 2:
                out[key] = struct.unpack_from("<I", data, pos)[0]
                pos += 4
            else:
                raise ValueError("unsupported VDF value type %d" % kind)

    result = read_map()
    if pos != len(data):
        raise ValueError("unexpected data after the end of the file")
    return result


def dump(tree):
    out = bytearray()
    for key, value in tree.items():
        if isinstance(value, dict):
            out += b"\0" + key.encode() + b"\0" + dump(value)
        elif isinstance(value, int):
            out += b"\2" + key.encode() + b"\0" + struct.pack("<I", value)
        else:
            out += b"\1" + key.encode() + b"\0" + value.encode() + b"\0"
    return bytes(out) + b"\x08"


def make_entry(exe, name, startdir, icon, launch_options, tag):
    quoted = '"%s"' % exe
    return {
        "appid": appid(quoted, name), "AppName": name, "Exe": quoted, "StartDir": '"%s"' % startdir,
        "icon": icon, "ShortcutPath": "", "LaunchOptions": launch_options, "IsHidden": 0,
        "AllowDesktopConfig": 1, "AllowOverlay": 1, "OpenVR": 0, "Devkit": 0, "DevkitGameID": "",
        "DevkitOverrideAppID": 0, "LastPlayTime": 0, "FlatpakAppID": "", "tags": {"0": tag},
    }


def exe_of(entry):
    return next((v for k, v in entry.items() if k.lower() == "exe"), None)


def add(tree, new):
    shortcuts = tree.setdefault("shortcuts", {})
    for key, old in shortcuts.items():
        if exe_of(old) == new["Exe"]:
            new["LastPlayTime"] = old.get("LastPlayTime", 0)
            shortcuts[key] = new
            return tree
    shortcuts[str(len(shortcuts))] = new
    return tree


def remove(tree, exe):
    if "shortcuts" not in tree:
        return tree
    quoted = '"%s"' % exe
    kept = [e for e in tree["shortcuts"].values() if exe_of(e) != quoted]
    tree["shortcuts"] = {str(i): e for i, e in enumerate(kept)}
    return tree


def update_file(path, change):
    old = open(path, "rb").read() if os.path.exists(path) else b""
    tree = change(parse(old) if old else {})
    if not tree:
        return False
    new = dump(tree)
    if new == old:
        return False
    if old:
        shutil.copy2(path, "%s.bak-hyrule-%s" % (path, time.strftime("%Y%m%d-%H%M%S")))
    tmp = path + ".tmp"
    with open(tmp, "wb") as handle:
        handle.write(new)
    if os.path.exists(path):
        shutil.copymode(path, tmp)
    os.replace(tmp, path)
    return True


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("add")
    p.add_argument("exe")
    p.add_argument("name")
    p.add_argument("files", nargs="+")
    p.add_argument("--startdir")
    p.add_argument("--icon", default="")
    p.add_argument("--launch-options", default="")
    p.add_argument("--tag", default="Hyrule Online")
    p = sub.add_parser("remove")
    p.add_argument("exe")
    p.add_argument("files", nargs="+")
    p = sub.add_parser("appid")
    p.add_argument("exe")
    p.add_argument("name")
    args = parser.parse_args(argv)

    if args.cmd == "appid":
        print(appid('"%s"' % args.exe, args.name))
        return 0
    status = 0
    for path in args.files:
        try:
            if args.cmd == "add":
                entry = make_entry(args.exe, args.name, args.startdir or os.path.dirname(args.exe), args.icon, args.launch_options, args.tag)
                changed = update_file(path, lambda tree: add(tree, entry))
            else:
                changed = update_file(path, lambda tree: remove(tree, args.exe))
            print("%s: %s" % (path, "updated" if changed else "already up to date"))
        except (OSError, ValueError, IndexError) as error:
            print("%s: not changed (%s)" % (path, error), file=sys.stderr)
            status = 1
    return status


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
