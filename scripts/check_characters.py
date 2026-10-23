"""Offline checks of every character the launcher offers as a player model.

Nothing here starts the game. It compares each NPC model with the files the mod needs and prints one row
per character, so a model that would hang or break a session can be spotted (and hidden or fixed) first.
Run it through scripts/Test-Characters.ps1, which uses the project's own Python with oead and rstb.
"""
import argparse
import glob
import json
import os
import re
import sys

import oead
from rstb import ResourceSizeTable

RSTB = 'System/Resource/ResourceSizeTable.product.srsizetable'
LANGUAGES = ['USen', 'USes', 'USfr']
MAX_MISSING_SHARE = 0.25
PLAYER_LABELS = 32


def decompress(data):
    return bytes(oead.yaz0.decompress(data)) if data[:4] == b'Yaz0' else bytes(data)


def read(path):
    with open(path, 'rb') as handle:
        return handle.read()


def strings(data):
    return set(match.decode() for match in re.findall(rb'[A-Za-z][A-Za-z0-9_]{2,40}', data))


def source_list(root, name):
    """The folder names listed in Characters.cs, so the checker and the launcher never disagree."""
    text = open(os.path.join(root, 'src', 'Launcher', 'Characters.cs'), encoding='utf-8').read()
    body = re.search(r'string\[\]\s+%s\s*=\s*\{([^}]*)\}' % name, text)
    return set(re.findall(r'"([^"]+)"', body.group(1))) if body else set()


class Game:
    def __init__(self, root):
        self.root = root
        instances = sorted(glob.glob(os.path.join(root, '.tools', 'instances', '*')))
        if not instances:
            raise SystemExit('No Cemu instances found. Run the clean install first.')
        self.instances = instances
        self.update = self.dlc = self.base = None
        for instance in instances:
            found = glob.glob(os.path.join(instance, 'mlc01', 'usr', 'title', '0005000e', '101c9400', 'content'))
            if found:
                self.update = found[0]
                dlc = glob.glob(os.path.join(instance, 'mlc01', 'usr', 'title', '0005000c', '101c9400', 'content', '0010'))
                self.dlc = dlc[0] if dlc else None
            settings = os.path.join(instance, 'settings.xml')
            if os.path.exists(settings):
                rpx = re.search(r'<Entry>([^<]*U-King\.rpx)</Entry>', open(settings, encoding='utf-8').read())
                if rpx:
                    self.base = os.path.join(os.path.dirname(os.path.dirname(os.path.normpath(rpx.group(1)))), 'content')
        if not (self.update or self.base):
            raise SystemExit('The game files were not found through the Cemu instances.')

    def find(self, relative):
        for base in (self.update, self.dlc, self.base):
            if base and os.path.exists(os.path.join(base, relative)):
                return os.path.join(base, relative)
        return None

    def merged(self, instance):
        return os.path.join(instance, 'graphicPacks', 'BreathOfTheWild_BCML', 'content')


def load_rstb(path):
    return ResourceSizeTable(decompress(read(path)), True)


def rstb_size(table, name):
    return table.get_size(name) if table.is_in_table(name) else None


def model_files(game, folder):
    return {suffix: game.find('Model/%s.%s' % (folder, suffix)) for suffix in ('sbfres', 'Tex1.sbfres', 'Tex2.sbfres')}


def check_character(game, folder, required_bones, vanilla_rstb, merged_rstb):
    """Returns a list of (level, message); an empty list means the model looks fine."""
    problems = []
    files = model_files(game, folder)
    if not files['sbfres']:
        problems.append(('FAIL', 'no model file'))
    else:
        missing = sorted(required_bones - strings(decompress(read(files['sbfres']))))
        if missing:
            # A few missing face bones only lose some facial animation; a mostly different skeleton breaks the player's animations.
            level = 'FAIL' if len(missing) > MAX_MISSING_SHARE * len(required_bones) else 'WARN'
            problems.append((level, '%d of %d bones the player animations drive are missing (%s)' % (len(missing), len(required_bones), ', '.join(missing[:3]))))
    if not files['Tex1.sbfres']:
        problems.append(('FAIL', 'no texture file'))
    for suffix, path in files.items():
        if not path or not merged_rstb:
            continue
        name = 'Model/%s.%s' % (folder, suffix.replace('sbfres', 'bfres'))
        merged = rstb_size(merged_rstb, name)
        vanilla = rstb_size(vanilla_rstb, name) if vanilla_rstb else None
        if merged is None and vanilla is not None:
            problems.append(('FAIL', '%s lost its size table entry' % name))
        elif merged is not None and vanilla is not None and merged < vanilla:
            problems.append(('FAIL', '%s size table entry shrank (%d < %d)' % (name, merged, vanilla)))
    return problems


def check_texts(game, instance):
    problems = []
    for language in LANGUAGES:
        path = os.path.join(game.merged(instance), 'Pack', 'Bootup_%s.pack' % language)
        if not os.path.exists(path):
            problems.append('Bootup_%s.pack is missing' % language)
            continue
        pack = oead.Sarc(decompress(read(path)))
        inner = [entry for entry in pack.get_files() if entry.name.startswith('Message/')]
        found = 0
        if inner:
            messages = oead.Sarc(decompress(bytes(inner[0].data)))
            npc = [entry for entry in messages.get_files() if entry.name.endswith('ActorType/NPC.msbt')]
            blob = bytes(npc[0].data) if npc else b''
            found = sum(1 for index in range(1, PLAYER_LABELS + 1) if ('Jugador%d_Name' % index).encode() in blob)
        if found != PLAYER_LABELS:
            problems.append('%s has %d of %d player name labels' % (language, found, PLAYER_LABELS))
    return problems


def check_armors(game, instance, npc_folders):
    """The merged armor packs must either all be the game's own (Link) or all use the one chosen NPC model, or the avatar changes on equip.
    Returns (problems, description of the current state)."""
    chosen = []
    total = 0
    for path in sorted(glob.glob(os.path.join(game.merged(instance), 'Actor', 'Pack', 'Armor_*.sbactorpack'))):
        total += 1
        sarc = oead.Sarc(decompress(read(path)))
        for entry in sarc.get_files():
            if entry.name.endswith('.bmodellist'):
                text = oead.aamp.ParameterIO.from_binary(bytes(entry.data)).to_text()
                found = re.findall(r'Folder: ([\w.]+)', text)[:1]
                if found and found[0] in npc_folders:
                    chosen.append(found[0])
    if not total:
        return ['no armor packs in the merged content'], 'no armor packs'
    if not chosen:
        return [], 'Link (armor packs untouched)'
    models = sorted(set(chosen))
    if len(models) > 1 or len(chosen) != total:
        return ['armor packs are mixed: %d of %d use an NPC model (%s)' % (len(chosen), total, ', '.join(models))], 'mixed'
    return [], models[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', required=True)
    parser.add_argument('--npcs', required=True, help='NpcData.json taken from the Milk Bar launcher')
    arguments = parser.parse_args()
    root = arguments.root
    game = Game(root)
    npcs = json.load(open(arguments.npcs, encoding='utf-8'))
    verified = source_list(root, 'Verified')
    unsupported = source_list(root, 'Unsupported')

    animation = game.find('Model/Player_Animation.sbfres')
    if not animation:
        raise SystemExit('Player_Animation.sbfres was not found in the game files.')
    # What the player animations drive and every model confirmed in a real session has; a model missing any of it is suspect.
    required = strings(decompress(read(animation)))
    for folder in verified:
        path = game.find('Model/%s.sbfres' % folder)
        if path:
            required &= strings(decompress(read(path)))
    required = {s for s in required if ('_' in s or s in ('Head', 'Neck', 'Root', 'Waist', 'Chin')) and not s.startswith('Mt_')}

    vanilla_path = game.find(RSTB)
    vanilla_rstb = load_rstb(vanilla_path) if vanilla_path else None
    merged_path = os.path.join(game.merged(game.instances[-1]), RSTB)
    merged_rstb = load_rstb(merged_path) if os.path.exists(merged_path) else None

    failures = 0
    print('%-32s %-11s %s' % ('Model', 'State', 'Result'))
    for npc in npcs:
        folder = npc['Folder']
        problems = check_character(game, folder, required, vanilla_rstb, merged_rstb)
        offered = folder not in unsupported
        state = 'unsupported' if not offered else ('verified' if folder in verified else 'untested')
        fails = [message for level, message in problems if level == 'FAIL']
        warns = [message for level, message in problems if level == 'WARN']
        if offered and fails:
            failures += 1
            result = 'FAIL (hide it or fix it): ' + '; '.join(fails)
        elif not offered:
            result = 'hidden, as expected: ' + '; '.join(fails) if fails else 'NOTE hidden but its files look fine; it could be offered again'
        else:
            result = ('WARN ' + '; '.join(warns)) if warns else 'ok'
        print('%-32s %-11s %s' % (folder, state, result))

    for instance in game.instances:
        if not os.path.isdir(game.merged(instance)):
            continue
        armor_problems, current = check_armors(game, instance, {npc['Folder'] for npc in npcs})
        print('%s currently plays as: %s' % (os.path.basename(instance), current))
        for problem in check_texts(game, instance) + armor_problems:
            failures += 1
            print('FAIL %s: %s' % (os.path.basename(instance), problem))

    print('%d problem(s); %d models checked against %d bones' % (failures, len(npcs), len(required)))
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
