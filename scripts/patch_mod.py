"""Reapplies the files BCML drops when it merges the Milk Bar mod (see RESEARCH.md).

Idempotent: every step compares the target with what it should contain and only writes on a difference.
Run it through scripts/Patch-Mod.ps1, which uses the project's own Python with oead (installed with BCML).
"""
import argparse
import glob
import os
import sys
import zlib

import oead

CAMERA = 'Actor/Pack/GameRomCamera.sbactorpack'
CAMERA_ENTRY = 'Actor/AISchedule/GameRomCamera.baischedule'
ACTOR_INFO = 'Actor/ActorInfo.product.sbyml'
TITLE = 'Pack/TitleBG.pack'
TITLE_ENTRIES = ['EventFlow/MultiplayerEvent.bfevfl', 'EventFlow/AddItem.bfevfl', 'EventFlow/BindEvent.bfevfl',
                 'EventFlow/ChangeFlag.bfevfl', 'Event/ResidentEvent.byml']


def decompress(data):
    return bytes(oead.yaz0.decompress(data)) if data[:4] == b'Yaz0' else bytes(data)


def read(path):
    with open(path, 'rb') as handle:
        return handle.read()


def write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    temp = path + '.tmp'
    with open(temp, 'wb') as handle:
        handle.write(data)
    os.replace(temp, path)


class Patcher:
    def __init__(self, root, check):
        self.root = root
        self.check = check
        self.pending = 0
        tools = os.path.join(root, '.tools')
        bcml = os.path.join(tools, 'localappdata', 'bcml')
        self.mod = os.path.join(bcml, 'mods', '0100_MilkBarLauncher')
        self.targets = []
        seen = set()
        candidates = [os.path.join(bcml, 'merged', 'content')]
        candidates += sorted(glob.glob(os.path.join(tools, 'instances', '*', 'graphicPacks', 'BreathOfTheWild_BCML', 'content')))
        for path in candidates:
            real = os.path.realpath(path)
            if os.path.isdir(path) and real not in seen:
                seen.add(real)
                self.targets.append(path)
        if not self.targets:
            raise SystemExit('No merged content found. Run the clean install first.')
        updates = sorted(glob.glob(os.path.join(tools, 'instances', '*', 'mlc01', 'usr', 'title', '0005000e', '101c9400', 'content', ACTOR_INFO)))
        self.vanilla_actor_info = updates[0] if updates else None

    def report(self, step, target, changed):
        name = os.path.relpath(target, self.root)
        if changed:
            self.pending += 1
            print('[%s] %s: %s' % (step, name, 'needs patching' if self.check else 'patched'))
        else:
            print('[%s] %s: ok' % (step, name))

    def players(self):
        for source in sorted(glob.glob(os.path.join(self.mod, 'content', 'Actor', 'Pack', 'Jugador*.sbactorpack'))):
            data = read(source)
            for target in self.targets:
                path = os.path.join(target, 'Actor', 'Pack', os.path.basename(source))
                changed = not os.path.exists(path) or read(path) != data
                if changed and not self.check:
                    write(path, data)
                if changed or os.path.basename(source) == 'Jugador1.sbactorpack':
                    self.report('players', path, changed)

    def camera(self):
        mod = oead.Sarc(decompress(read(os.path.join(self.mod, 'content', CAMERA))))
        extra = bytes(mod.get_file(CAMERA_ENTRY).data)
        for target in self.targets:
            path = os.path.join(target, CAMERA)
            sarc = oead.Sarc(decompress(read(path)))
            current = sarc.get_file(CAMERA_ENTRY)
            changed = current is None or bytes(current.data) != extra
            if changed and not self.check:
                writer = oead.SarcWriter(endian=oead.Endianness.Big)
                for entry in sarc.get_files():
                    writer.files[entry.name] = bytes(entry.data)
                writer.files[CAMERA_ENTRY] = extra
                _, data = writer.write()
                write(path, bytes(oead.yaz0.compress(data)))
            self.report('camera', path, changed)

    def title(self):
        mod = oead.Sarc(decompress(read(os.path.join(self.mod, 'content', TITLE))))
        wanted = {name: bytes(mod.get_file(name).data) for name in TITLE_ENTRIES}
        for target in self.targets:
            path = os.path.join(target, TITLE)
            raw = read(path)
            sarc = oead.Sarc(decompress(raw))
            changed = False
            for name, data in wanted.items():
                entry = sarc.get_file(name)
                if entry is None or bytes(entry.data) != data:
                    changed = True
            if changed and not self.check:
                writer = oead.SarcWriter.from_sarc(sarc)
                for name, data in wanted.items():
                    writer.files[name] = data
                _, out = writer.write()
                out = bytes(out)
                write(path, bytes(oead.yaz0.compress(out)) if raw[:4] == b'Yaz0' else out)
            self.report('titlebg', path, changed)

    def actor_info(self):
        log = os.path.join(self.mod, 'logs', 'actorinfo.yml')
        if not self.vanilla_actor_info:
            raise SystemExit('The vanilla ActorInfo.product.sbyml was not found under .tools/instances/*/mlc01.')
        info = oead.byml.from_binary(decompress(read(self.vanilla_actor_info)))
        with open(log, encoding='utf-8') as handle:
            changes = oead.byml.from_text(handle.read())
        by_hash = {zlib.crc32(actor['name'].encode()) & 0xFFFFFFFF: actor for actor in info['Actors']}
        for key, value in changes.items():
            value_hash = int(key) & 0xFFFFFFFF
            if 'name' not in value:
                for field, content in value.items():
                    by_hash[value_hash][field] = content
            else:
                by_hash[value_hash] = value
        order = sorted(by_hash)
        info['Actors'] = oead.byml.Array([by_hash[h] for h in order])
        info['Hashes'] = oead.byml.Array([oead.U32(h) if h > 0x7FFFFFFF else oead.S32(h) for h in order])
        payload = bytes(oead.byml.to_binary(info, big_endian=True))
        compressed = bytes(oead.yaz0.compress(payload))
        for target in self.targets:
            path = os.path.join(target, ACTOR_INFO)
            changed = not os.path.exists(path) or decompress(read(path)) != payload
            if changed and not self.check:
                write(path, compressed)
            self.report('actorinfo', path, changed)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', required=True)
    parser.add_argument('--check', action='store_true', help='only report; exit 1 if something needs patching')
    args = parser.parse_args()
    patcher = Patcher(os.path.abspath(args.root), args.check)
    patcher.players()
    patcher.camera()
    patcher.title()
    patcher.actor_info()
    if args.check and patcher.pending:
        print('%d file(s) need patching.' % patcher.pending)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
