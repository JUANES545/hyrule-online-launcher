"""Installs Milk Bar's MilkBarLauncher.bnp into the project's own BCML and exports the merge to both Cemu folders (see RESEARCH.md).

Run it through scripts/Merge-Mod.ps1, which uses the project's Python and points LOCALAPPDATA at .tools/localappdata before BCML is imported.
BCML's Rust extension ignores that override, so refresh_master_export and link_master_mod are never called: the export is a plain copy of
the mods' content and aoc into merged/ (export_merged). Idempotent: an installed mod is not installed again and copies overwrite in place.
"""
import argparse
import multiprocessing
import os
import shutil
import subprocess
import sys
from pathlib import Path

MOD = '0100_MilkBarLauncher'
MASTER = '9999_BCML'
INSTANCES = ('cemuA', 'cemuB')


def say(kind, name, state, text):
    print('SETUP|%s|%s|%s|%s' % (kind, name, state, text), flush=True)


def mirror(source, target):
    target.mkdir(parents=True, exist_ok=True)
    for item in source.iterdir():
        if item.is_dir():
            shutil.copytree(str(item), str(target / item.name), dirs_exist_ok=True)
        else:
            shutil.copy2(str(item), str(target / item.name))


# BCML's own export: every mod's content and aoc in priority order, with the merged master on top. logs/ and rules.txt stay out.
def export_merged(mods, merged):
    for mod in sorted((m for m in mods.iterdir() if m.is_dir() and m.name != MASTER), key=lambda m: m.name) + [mods / MASTER]:
        for part in ('content', 'aoc'):
            if (mod / part).is_dir():
                shutil.copytree(str(mod / part), str(merged / part), dirs_exist_ok=True)


# Junctions need no administrator rights, unlike symbolic links.
def junction(link, target):
    if link.exists():
        return
    link.parent.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(['cmd.exe', '/d', '/c', 'mklink', '/J', str(link), str(target)], capture_output=True, text=True)
    if result.returncode != 0 or not link.exists():
        raise RuntimeError('The folder link %s could not be created: %s' % (link, (result.stdout + result.stderr).strip()))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', required=True)
    root = Path(parser.parse_args().root).resolve()
    tools = root / '.tools'
    bcml_data = tools / 'localappdata' / 'bcml'
    bnp = root / '.artifacts' / 'extract' / 'milkbar' / 'BNPs' / 'MilkBarLauncher.bnp'
    for path, what in ((bnp, 'Milk Bar Launcher is not downloaded yet (MilkBarLauncher.bnp is missing).'),
                       (bcml_data / 'settings.json', 'The BCML settings are missing. Locate your game files first.')):
        if not path.is_file():
            raise RuntimeError(what)
    os.environ['LOCALAPPDATA'] = str(tools / 'localappdata')

    from bcml import install, util
    if Path(os.path.expandvars('%LOCALAPPDATA%')) / 'bcml' != bcml_data:
        raise RuntimeError('BCML would use a data folder outside the project: %s' % util.get_data_dir())

    mods = bcml_data / 'mods'
    if not (mods / MOD).is_dir():
        say('PROGRESS', 'all', 10, 'the multiplayer mod in BCML (this takes a few minutes)')
        install.install_mod(bnp, merge_now=True)
    if not (mods / MASTER).is_dir():
        raise RuntimeError('BCML did not produce the merged mod (mods/%s).' % MASTER)

    say('PROGRESS', 'all', 70, 'the merged mod into both Cemu folders')
    merged = bcml_data / 'merged'
    export_merged(mods, merged)
    first = tools / 'instances' / INSTANCES[0] / 'graphicPacks'
    second = tools / 'instances' / INSTANCES[1] / 'graphicPacks'
    if not (first / 'BreathOfTheWild_BCML').exists():
        junction(first / 'BreathOfTheWild_BCML', merged)
    elif (first / 'BreathOfTheWild_BCML').resolve() != merged.resolve():
        mirror(merged, first / 'BreathOfTheWild_BCML')
    mirror(merged, second / 'BreathOfTheWild_BCML')
    if (first / 'bcmlPatches').is_dir():
        mirror(first / 'bcmlPatches', second / 'bcmlPatches')
    junction(tools / 'localappdata' / 'bcml-b' / 'store' / 'merged', second / 'BreathOfTheWild_BCML')


if __name__ == '__main__':
    multiprocessing.freeze_support()
    try:
        main()
    except Exception as error:
        say('STEP', 'mod', 'failed', '%s: %s' % (type(error).__name__, error))
        sys.exit(1)
