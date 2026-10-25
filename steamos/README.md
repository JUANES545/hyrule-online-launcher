# Hyrule Online on SteamOS (play, host or join)

Lets a Steam Deck or ROG Ally on SteamOS play, host or join a Hyrule Online server, with the same modes, invites and rules as the Windows launcher.

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

## Play, host or join

Start **Hyrule Online** (Gaming Mode: Library, Non-Steam, Hyrule Online, Play; Desktop Mode: the menu entry or `~/hyrule-online/hyrule-online.sh`). It shows the same modes as the Windows launcher and remembers your last choice:

- **Play on this device:** a private server on `127.0.0.1` with one player (you). Nobody else can join; the startup message says so.
- **Host a server:** the server listens on `0.0.0.0`, shows an invite (copyable in a dialog; LAN address first, Tailscale or another VPN address as an extra line) and you play on it too. Friends paste the invite in the Join mode of their launcher, Windows or SteamOS.
- **Join a server:** paste an invite (`hyrule://HOST:PORT?pw=PASSWORD`) or type `HOST:PORT`, then the password if the invite has none, and your player name. The last values are remembered.
- **Stop:** stops the server and the game of this install.

From a terminal, to skip the menu:

```
hyrule-online.sh                               # menu
hyrule-online.sh INVITE|HOST:PORT [PASSWORD] [NAME]   # join
hyrule-online.sh HOST PORT [PASSWORD] [NAME]          # join
hyrule-online.sh --host [PORT [PASSWORD]]      # host with the saved rules, port and password
hyrule-online.sh --local                       # private server, 127.0.0.1 only
hyrule-online.sh --stop                        # stop everything of this install
hyrule-online.sh --version
```

Add `--no-play` to `--host` or `--local` to run only the server. Starting takes about a minute before the game connects. The server keeps running after you close the game; use Stop (or the "Stop session" action of the menu entry) to end it.

### World rules

Host and Play on this device ask for the rules with the same presets and defaults as the Windows World page: Game Completion (the default), Hunter Vs Speedrunner, Any% Speedrun, Hide n' seek, Death Swap, or Custom (a checklist of Enemies, Quests, Koroks, Towers, Shrines, Locations and Divine Beasts, plus the special mode). Hosting also asks for the port (1024 to 65535, default 5050), password (up to 40 characters) and description (up to 80). They are written to `ServerConfig.ini` with the same keys the Windows launcher writes, and saved in `join.conf` for the next time. `--host` and `--local` skip the dialogs and use the saved rules and description. When there is no desktop and no terminal, the saved or default rules are used.

These are a few small dialogs, not one form: zenity forms cannot show the saved values, so each dialog starts from them instead. When you join a friend's server their rules apply.

## Host from SteamOS

The server runs as the user service `hyrule-server` (the dedicated server of Milk Bar 2.0.1 inside the same Wine prefix, with `DOTNET_ROLL_FORWARD=Major` because the server is built for .NET 6 and the prefix has .NET 8). The launcher shows the invite once the server log says it is listening. SteamOS has no firewall by default; if ufw, firewalld or nftables rules are active, a one-line warning says the port may be blocked. The launcher never changes a firewall, so open the TCP port yourself if you use one.

Three-way play: a Windows PC with two local players (Join mode, two players) plus the Ally itself can all join the Ally's server (3 players). The server accepts up to 32 players.

## Windows launcher and SteamOS

One version, one `VERSION` file (the number in the release name): `hyrule-online.sh --version` and the installer print it, and the installer saves it in its state file. Both sides use the same invite format, the same rule presets and keys, and the same failure checklist. What the Ally does not do:

| Feature | Windows launcher | SteamOS (Ally / Deck) |
| --- | --- | --- |
| Play on this device (private server) | yes | yes |
| Host a server, invite, password, description | yes | yes (no copy button; the invite is in a dialog you can select and copy) |
| Join with an invite, `host:port` and password | yes | yes |
| World rules and presets | World page | dialogs at start; same names, defaults and ini keys |
| Local players on one device | 1 or 2 | 1 only (no second Cemu window, no pad router) |
| Firewall rule for the host port | added for you | not added; detected and warned about only |
| Steam Input / controller router | yes | no; set the controller up in Cemu (Options, Input settings) |
| Live controls (time, weather, name tags) | yes | no |
| Graphics, language, avatar settings | yes | no; the installer's Cemu settings |
| Test connection button | removed | none; nothing probes the port (a bare TCP connection crashes the server) |

## Troubleshooting

- **Join failed:** the message lists the same checklist as the Windows launcher: check the invite, the password, that the host has a session running, and that its firewall allows the port.
- **Wrong password:** the server answers `Response:3`. Ask the host for the password.
- **Server did not start:** see `~/hyrule-online/logs/server.log` and `server.err`. Do not test the port with a plain TCP connection: the server crashes on a connection that sends nothing.
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
