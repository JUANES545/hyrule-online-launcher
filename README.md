# Hyrule Online

A launcher for playing The Legend of Zelda: Breath of the Wild with friends: split-screen co-op on one PC, or online through a dedicated server. It runs two Cemu 1.26.2f windows with the Milk Bar mod and handles controllers, display placement, the server and the install for you.

There are two launchers that share the same modes, invites and rules:

- **Windows**: a WPF app (`HyruleOnline.exe`) plus a small native XInput bridge.
- **SteamOS**: a Tk launcher and installer for the Steam Deck and ROG Ally, which runs Cemu through Proton. See [`steamos/README.md`](steamos/README.md).

## What you need

- Your own legally dumped copy of Breath of the Wild: base game, update v208 and DLC v80. Optionally a save. The project never ships or downloads game files.
- Windows 10 version 1809 or newer with .NET Framework 4.8, or SteamOS in Desktop Mode with Proton 10.0 or newer.
- One or two controllers.

Cemu, Python, BCML and Milk Bar are downloaded by the launcher's setup. No game dumps, saves, Cemu builds or mod assets are bundled or committed.

## Install

Download the latest files from [Releases](https://github.com/JUANES545/hyrule-online-launcher/releases):

| File | For |
| --- | --- |
| `HyruleOnline-<version>-setup.exe` | Windows installer (per user, default folder `C:\HyruleOnline`) |
| `HyruleOnline-<version>-portable.zip` | Windows without an installer |
| `HyruleOnline-<version>-steamos.tar.gz` | Steam Deck and ROG Ally |

On first start the launcher checks your install and walks you through anything missing: components, game files and the mod pack. Pick the folder with your game files and it finds the base game, update and DLC.

Installed copies tell you when a newer release exists and update in place after you press the **Update** button. Preferences are kept, and the previous version is restored if the update fails. Portable and development copies do not show the button.

## Modes

- **Single player**: one game window. A new install starts here.
- **Local co-op**: two players on one PC, each with their own Cemu window and controller. It unlocks after the game has saved once, or right away if you provide an existing save.
- **Host**: starts the dedicated server and both game windows.
- **Join**: connects to a friend's server with an invite.

Languages: English, Spanish and French.

## Build from source

Needs Windows with the .NET Framework 4 compiler that ships with Windows. There are no other dependencies.

```powershell
.\scripts\Build.ps1     # builds the launcher and the input bridge into .artifacts\
.\scripts\Test.ps1      # runs every test, including the Windows and SteamOS parity test
.\scripts\Package.ps1   # builds the portable zip and, if Inno Setup is installed, the installer
```

SteamOS tests run on Linux or Git Bash: `bash steamos/test.sh` and `python3 steamos/launcher_test.py`.

## Repository layout

| Folder | Contents |
| --- | --- |
| `src/` | Launcher and input router (C#, WPF) and the native XInput bridge (C) |
| `scripts/` | Build, install, session and update scripts |
| `steamos/` | SteamOS launcher and installer |
| `installer/` | Inno Setup script |
| `tests/` | Unit, layout and parity tests |
| `docs/` | [Architecture](docs/ARCHITECTURE.md) |

Background on how online play works is in [`RESEARCH.md`](RESEARCH.md).

## Contributing

The project follows Gitflow: `feature/*` into `develop`, `release/*` into `main`. See [`CONTRIBUTING.md`](CONTRIBUTING.md) for branches, releases and the checks that enforce them.

## Credits

Built on [Cemu](https://cemu.info), the Milk Bar mod and launcher, and BCML. This project is not affiliated with Nintendo.