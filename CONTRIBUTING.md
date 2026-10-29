# Contributing

This project uses Gitflow. Automation enforces it, so a pull request that breaks the flow fails its checks.

## Branches

| Branch | Purpose | Comes from | Goes to |
| --- | --- | --- | --- |
| `main` | Tagged releases only | | |
| `develop` | Integration | | |
| `feature/<name>` | New work | `develop` | `develop` |
| `bugfix/<name>` | Fixes during development or on a release branch | `develop` or `release/*` | `develop` or `release/*` |
| `release/<x.y.z>` | Stabilise and publish a version | `develop` | `main` |
| `hotfix/<x.y.z>` | Urgent fix for the released version | `main` | `main` |
| `backport/<x.y.z>` | Opened by automation when `main` cannot merge into `develop` cleanly | `main` | `develop` |

## Releasing

1. Branch `release/<x.y.z>` from `develop` and put `<x.y.z>` in the `VERSION` file.
2. Open a pull request into `main`. The checks require the branch name to match `VERSION`, and the version to be newer than the latest tag.
3. When it is merged, the Release workflow builds and tests, then publishes the tag `v<x.y.z>` with the installer and the portable zip, and merges `main` back into `develop`.
4. Hotfixes follow the same steps from `main`.

## Checks

- **Gitflow guard** rejects pull requests that do not follow the table above.
- **CI** builds, tests and packages on Windows, and checks the SteamOS launcher on Linux (script tests, launcher tests, and the window under a virtual display against a stub script).
- **Release** also refuses to publish if `main` was changed by anything other than a `release/*` or `hotfix/*` pull request. It runs the SteamOS tests and the parity test first, and checks that `VERSION`, the tag, `steamos/parity.json` and the SteamOS tarball agree.

## Keeping the Windows and SteamOS launchers in sync

`steamos/parity.json` records the Windows facts the SteamOS launcher mirrors: Online and World texts (English, Spanish, French), rule presets and codes, limits (port, password, description, name), the invite format, tab order and the command line the window uses. `tests\ParityTests.cs` reads the real Windows code and compares it with that file, then compares the file with `steamos/`. It runs in `scripts\Test.ps1`.

When it fails, the message names what changed and which file to update:

1. Run `.artifacts\tests\ParityTests.exe . --facts` to print the current Windows values and every Online or World text.
2. Change `steamos/` (and `steamos/strings.json` for texts) to match, or decide the change does not apply to SteamOS.
3. Update `steamos/parity.json` to say so: add a new text to `mirrored` or to `notApplicable` with a reason, and set `version` to the release version before publishing.

`tests/steamos_gui_check.py` opens the SteamOS window against `tests/fixtures/steamos/hyrule-online-stub.sh` and checks every page, keyboard reachability, the limits and a clean close. Run it with `xvfb-run -a python3 tests/steamos_gui_check.py` on Linux.

## Packages

`scripts/Package.ps1` builds `HyruleOnline-<version>-portable.zip` and, when Inno Setup is installed, `HyruleOnline-<version>-setup.exe`. Both hold only this project's own files. Cemu, Python, BCML, Milk Bar and the game files are never bundled.

Installed copies update themselves: on start the launcher asks GitHub for the latest release and, if it is newer, shows an Update button (setup installs only). Pressing it downloads `HyruleOnline-<version>-setup.exe`, checks it against `HyruleOnline-<version>-setup.exe.sha256`, keeps one backup in `.artifacts\update-backup` and runs `scripts\Update-Launcher.ps1`, which installs silently and restores the backup if the new launcher does not start. The release workflow publishes both files, so a release must keep those asset names. The SteamOS launcher does the same with the `-steamos.tar.gz` asset through `hyrule-online.sh --update`.

The icon lives in ssets/icon.svg. ssets/icon.ico (embedded in the exe and used by the installer) and steamos/hyrule-online.png (the SteamOS launcher and Steam entry) are rendered from it, so change the SVG first and regenerate both.
