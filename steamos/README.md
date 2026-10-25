# Hyrule Online on SteamOS (join or host)

Lets a Steam Deck or ROG Ally on SteamOS join a Hyrule Online server, or host one for other players.

It installs Cemu 1.26.2f and the headless Milk Bar client into their own folder, runs them with Proton's Wine in a private prefix, and adds a **Hyrule Online** entry to your Steam library.

## What you need

- SteamOS in Desktop Mode, x86_64, about 25 GB free (8 GB with `--link`).
- **Proton 10.0 or newer**, installed from Steam (Library, include Tools in the filter, search `Proton 10.0`, Install). The installer checks for it.
- Your own legally dumped copy of Breath of the Wild: base game, update v208, DLC v80 and a save (a brand new game hangs in Cemu). They can sit in one folder; the installer searches it and never changes it.
- The Hyrule Online mod pack from your Windows install: the `BreathOfTheWild_BCML`, `bcmlPatches` and `HyruleOnline` folders inside its `graphicPacks` folder (in `.tools\instances\cemuA\graphicPacks`). Copy them next to your dump, or point the installer at them with `--mod`. This pack is built on Windows from your own game files, so it cannot be downloaded.
- `curl`, `unzip` or `7z`, `python3`, `systemd-run` (all present on SteamOS).

## Install

1. Download `HyruleOnline-<version>-steamos.tar.gz` from the release and extract it.
2. In Desktop Mode, open a terminal in the extracted folder and run `./install.sh`.
3. Follow the dialogs. You can stop at any point and run it again: finished steps are skipped.

Options: `--dry-run` (show what would happen, change nothing), `--yes --dump PATH` (no questions), `--mod PATH`, `--dir PATH` (default `~/hyrule-online`), `--link` (link the game files instead of copying; the dump must stay in place), `--uninstall`.

Steam rewrites its shortcut list when it closes, so the Steam entry is only written while Steam is closed. If Steam is running the installer asks before closing it. If you say no, close Steam yourself and run `~/hyrule-online/install.sh` again.

## Join a game

- **Gaming Mode:** Library, Non-Steam, **Hyrule Online**, Play. Enter the host's address, port, password and your player name. The last values are remembered.
- **Desktop Mode:** run the "Hyrule Online" menu entry or `~/hyrule-online/hyrule-online.sh`. To skip the dialogs: `hyrule-online.sh HOST PORT [PASSWORD] [NAME]`.
- Without arguments the launcher asks **Join** or **Host** and remembers the answer.
- Starting takes about a minute before the game connects. To stop a session: `hyrule-online.sh --stop`, or the "Stop session" action of the menu entry.

## Host from SteamOS

- **Gaming Mode or Desktop Mode:** start **Hyrule Online** and choose **Host**. Enter a port (default 5050) and a password. From a terminal: `hyrule-online.sh --host [PORT [PASSWORD]]`, add `--no-play` to only run the server.
- The server runs as the user service `hyrule-server` (the dedicated server from the Milk Bar release, run with Proton If a controller does nothing in game, configure it in Cemu (Options, Input settings).

## Troubleshooting

- **Wrong password:** the server answers `Response:3`. Ask the host for the password.
- **Server did not start:** see `~/hyrule-online/logs/server.log` and `server.err`. Do not test the port with a plain TCP connection: the server crashes on a connection that sends nothing.
- **"Could not join HOST:PORT":** the host must have started the server, and you must be on the same network or VPN. Check address and port.
- **Proton not found later:** Steam removed or updated Proton. Install Proton 10.0 again, then run `~/hyrule-online/install.sh`.
- **Anything else:** logs are in `~/hyrule-online/logs/` (`client.log`, `client.err`) and `~/hyrule-online/cemu/log.txt`.
- **Uninstall:** `~/hyrule-online/install.sh --uninstall`. It asks before deleting the game copy and your save, and never touches your original dump or Steam.

## What is downloaded

Nothing from the following is bundled. The installer downloads each component from its upstream source and checks a pinned SHA-256 before using it; a mismatch stops the install.

- Cemu 1.26.2 (cemu.info) and the Cemu graphic packs (github.com/cemu-project)
- Milk Bar Launcher 2.0.1 (only `InjectDLL.dll` and the `DedicatedServer` folder are used) and the Milk Bar CLI (github.com/MilkBarModding, github.com/CarlosEvCode)
- .NET 8 Desktop Runtime and the Visual C++ runtime (Microsoft)
- Proton (Valve), which you install from Steam

You must provide your own legally dumped game. No game data is downloaded or distributed.

## Checks

`./test.sh` runs offline checks of the scripts (syntax, validation, the server config and invite builders, the Steam shortcut file writer, resume state). It does not start the game.
