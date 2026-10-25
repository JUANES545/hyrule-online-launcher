#!/usr/bin/env python3
"""Opens steamos/launcher.py against a stub hyrule-online.sh and checks it the way a player would use it:
every page builds, every control can be reached with the keyboard, the limits and texts match steamos/parity.json,
a session can be started and stopped, and a scripted close ends the program cleanly.
Needs a display: `xvfb-run -a python3 tests/steamos_gui_check.py`. Screenshots go to .artifacts/gui (Linux only)."""
import json
import os
import re
import shutil
import subprocess
import sys
import time
import tkinter as tk

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(ROOT, "steamos"))
import launcher  # noqa: E402

OUT = os.environ.get("HYRULE_GUI_OUT") or os.path.join(ROOT, ".artifacts", "gui")
STUB = os.path.join(HERE, "fixtures", "steamos", "hyrule-online-stub.sh").replace("\\", "/")
INTERACTIVE = ("Button", "Radiobutton", "Checkbutton", "Entry", "Scale", "Spinbox", "Listbox", "Text")
problems, shown_texts, notes = [], set(), []


def check(ok, message):
    if not ok:
        problems.append(message)


def describe(w):
    try:
        text = w.cget("text")
    except tk.TclError:
        text = ""
    return f"{w.winfo_class()} {text!r}" if text else f"{w.winfo_class()} {w}"


def widgets(top):
    for c in top.winfo_children():
        yield c
        yield from widgets(c)


def interactive(top):
    """Controls a player can operate; a widget with takefocus=0 (the read-only invite box) is meant to be skipped."""
    return {w for w in widgets(top) if w.winfo_class() in INTERACTIVE and w.winfo_viewable() and str(w.cget("state")) != "disabled"
            and str(w.cget("takefocus")) != "0"}


def settle(root, seconds=0.0):
    end = time.time() + seconds
    root.update()
    while time.time() < end:
        root.update()
        time.sleep(0.02)


def remember_texts(root):
    for w in widgets(root):
        try:
            text = str(w.cget("text"))
        except tk.TclError:
            continue
        if text:
            shown_texts.add(text)


def screenshot(name):
    if not sys.platform.startswith("linux") or not os.environ.get("DISPLAY") or not shutil.which("import"):
        return
    os.makedirs(OUT, exist_ok=True)
    subprocess.run(["import", "-window", "root", os.path.join(OUT, name + ".png")], check=False, timeout=30)


def audit(app, name, top=None):
    """Everything the user can operate must be in the keyboard focus order, and Tab must really visit it."""
    root = app.root
    top = top or root
    remember_texts(root)
    mine, reach = interactive(top), set(app.focusables())
    check(mine, f"{name}: no control is visible")
    check(mine <= reach, f"{name}: the keyboard cannot reach {sorted(describe(w) for w in mine - reach)}; add them to App.focusables().")
    root.focus_force()
    settle(root)
    seen = set()
    for _ in range(len(mine) + 2):
        app.nav("Tab")
        settle(root)
        seen.add(root.focus_get())
    check(mine <= seen, f"{name}: Tab never reached {sorted(describe(w) for w in mine - seen)}.")
    screenshot(name)


def with_dialog(app, name, trigger):
    """Opens a modal list (it does not block), audits it, then closes it with Escape, which picks the last choice."""
    trigger()
    settle(app.root, 0.1)
    if not app.overlay:
        check(False, f"{name}: the dialog did not open.")
        return
    audit(app, name, app.overlay[0])
    app.escape()
    settle(app.root, 0.05)
    check(not app.overlay, f"{name}: Escape did not close the dialog.")

def calls(out):
    path = os.path.join(out, "calls.log")
    return open(path, encoding="utf-8").read().splitlines() if os.path.exists(path) else []


def wait_for(root, condition, seconds=8):
    end = time.time() + seconds
    while time.time() < end:
        settle(root, 0.05)
        if condition():
            return True
    return False


def parity(app, manifest):
    """The launcher cleans values like the Windows one (ServerRules); what the manifest says must be what ends up on the command line."""
    facts = manifest["facts"]
    form = {"port": "5050", "hostpass": "", "desc": "ok", "invite": "h:5050", "joinpass": "", "name": "Link"}
    host = lambda **kw: launcher.build_args(launcher.HOST, dict(form, **kw))[0]  # noqa: E731
    low, high = (int(n) for n in facts["port.host.range"].split("-"))
    default = facts["port.default"]
    check(host(port=str(low))[1] == str(low) and host(port=str(high))[1] == str(high), f"launcher.py refuses a host port inside {low}-{high} (parity.json).")
    check(host(port=str(low - 1))[1] == default and host(port=str(high + 1))[1] == default,
          f"launcher.py does not fall back to the default port {default} outside {low}-{high} like ServerRules.HostPort does.")
    pw = int(facts["password.host.max"])
    check(host(hostpass="x" * (pw + 5))[2] == "x" * pw, f"launcher.py does not cut the host password at {pw} characters.")
    check(host(hostpass="a" + facts["password.host.forbidden"] + "b")[2] == "ab", "launcher.py keeps a forbidden character in the host password.")
    desc = int(facts["description.max"])
    args = host(desc="d" * (desc + 5))
    check(args[args.index("--desc") + 1] == "d" * desc, f"launcher.py does not cut the description at {desc} characters.")
    longest = int(facts["name.max"])
    args = host(name="n" * (longest + 5))
    check(args[args.index("--name") + 1] == "n" * longest, f"launcher.py does not cut the player name at {longest} characters.")
    for key, value in facts.items():
        if key.startswith("invite.parse "):
            got = launcher.parse_invite(key[len("invite.parse "):])
            got = "invalid" if got is None else f"{got[0]}|{got[1]}|{got[2]}"
            check(got == value, f"launcher.py reads the invite {key[13:]!r} as {got!r} but the Windows launcher gives {value!r}.")
        if key.startswith("host.valid "):
            check(str(launcher.valid_host(key[11:].strip('"'))) == value, f"launcher.py valid_host({key[11:]}) disagrees with Windows ({value}).")
    wanted = dict((p.rsplit(" ", 1)[0], p.rsplit(" ", 1)[1]) for p in manifest["presets"])
    check(app.presets == wanted, f"the presets the window shows {app.presets} differ from parity.json {wanted}.")

def show(app, tab, mode=None):
    app.tab.set(tab)
    app.show_tab()
    if mode:
        app.set_mode(mode)
    settle(app.root, 0.1)
    remember_texts(app.root)


def main():
    shutil.rmtree(OUT, ignore_errors=True)
    os.makedirs(OUT)
    os.environ["HYRULE_STUB_DIR"] = OUT.replace("\\", "/")
    manifest = json.load(open(os.path.join(ROOT, "steamos", "parity.json"), encoding="utf-8"))
    root = tk.Tk()
    root.title("Hyrule Online (GUI check)")
    root.geometry("1280x800+0+0")
    if os.name == "nt":  # a local dry run: use Git's bash, not the WSL launcher in System32
        root.attributes("-alpha", 0)
        git_bash = shutil.which("bash", path=r"C:\Program Files\Git\bin")
        real_run, real_popen = subprocess.run, subprocess.Popen
        swap = lambda a: [git_bash if a and a[0] == "bash" else a[0]] + list(a[1:])  # noqa: E731
        launcher.subprocess.run = lambda a, *x, **k: real_run(swap(a) if git_bash else a, *x, **k)
        launcher.subprocess.Popen = lambda a, *x, **k: real_popen(swap(a) if git_bash else a, *x, **k)
    app = launcher.App(root, cli=launcher.Cli(script=STUB), conf=os.path.join(OUT, "launcher.conf"), join_conf=os.path.join(OUT, "join.conf"),
                       size=(1280, 800), server_log=launcher.ServerLog(os.path.join(OUT, "server.log")))
    settle(root, 0.3)

    for tab in launcher.TABS:
        show(app, tab)
        check(app.pages[tab].winfo_ismapped(), f"page {tab}: it is not shown.")
        if tab == "Online":
            for mode, _ in launcher.MODES:
                show(app, tab, mode)
                audit(app, f"online-{mode}")
        else:
            audit(app, f"page-{tab}")

    show(app, "Online", launcher.LOCAL)
    with_dialog(app, "dialog-mode", app.pick_mode)
    show(app, "World", launcher.HOST)
    with_dialog(app, "dialog-preset", app.pick_preset)
    with_dialog(app, "dialog-special", app.pick_special)
    show(app, "Details")
    app.open_help()
    audit(app, "help")
    app.close_help()
    check(not app.help_open, "Escape or close_help did not close the help.")

    show(app, "Online", launcher.JOIN)
    app.v["invite"].set("nonsense ///")
    app.start()
    settle(root, 0.2)
    check(app.note and app.tab.get() == "Online", "a start with an invalid invite did not stay on the Online page with a message.")
    check(not any(c.startswith("nonsense") for c in calls(OUT)), "an invalid invite reached hyrule-online.sh.")
    app.v["invite"].set("")
    app.note = ("{0}", (launcher.failure_text(["The start script failed."], True),))
    app.render()
    remember_texts(root)

    parity(app, manifest)

    show(app, "Play", launcher.LOCAL)
    app.start()
    check(wait_for(root, lambda: any(c.startswith("--local ") for c in calls(OUT))), "starting a session never ran hyrule-online.sh --local.")
    check(wait_for(root, lambda: os.path.exists(os.path.join(OUT, "session.pid"))), "the stub session did not start.")
    app.cli.refresh()
    app.render()
    show(app, "Online", launcher.HOST)
    audit(app, "online-host-running")
    show(app, "Play")
    with_dialog(app, "dialog-quit", app.quit)
    app.stop()
    check(wait_for(root, lambda: not app.cli.alive() and not app.cli.busy), "Stop did not end the session.")
    check(any(c.startswith("--stop") for c in calls(OUT)), "Stop never ran hyrule-online.sh --stop.")

    for lang, _ in launcher.LANGUAGES:
        app.pick_language(lang)
        settle(root, 0.1)
        audit(app, f"language-{lang}")
    app.pick_language("en")
    settle(root, 0.2)

    missing = []
    for item in manifest["mirrored"]:
        text = item.get("steamos", item)["en"]
        if not any(text in s for s in shown_texts):
            missing.append(text)
    check(not missing, f"steamos\\parity.json lists these texts as mirrored but no window shows them: {missing}.")

    show(app, "Play")
    app.cli.refresh()
    app.render()
    closed = []
    root.after(300, app.escape)
    root.after(20000, lambda: (closed.append("timeout"), root.destroy()))
    root.mainloop()
    check(not closed, "the launcher did not close when Escape was pressed on the Play page.")
    try:
        root.winfo_exists()
        check(False, "the window still exists after the scripted close.")
    except tk.TclError:
        pass

    if problems:
        print("GUI CHECK FAILED:")
        for p in problems:
            print("  - " + p)
        return 1
    print(f"GUI check passed ({len(shown_texts)} texts shown, screenshots in {os.path.relpath(OUT, ROOT)})")
    return 0


if __name__ == "__main__":
    sys.exit(main())