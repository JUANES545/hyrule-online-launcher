# Online multiplayer research (two Cemu instances)

Separate from `hyrule-coop-launcher` (local split-screen mod). Nothing here has been downloaded, built or run. Findings come from reading public GitHub source only (2026-10-22).

## Candidate: Milk Bar Launcher

- Upstream: https://github.com/MilkBarModding/MilkBarLauncher (archived 2025-03-13, last release 2.0.1, license NOASSERTION).
- Fork with a headless CLI client (Linux/SteamOS): https://github.com/CarlosEvCode/MilkBarLauncher and installer https://github.com/CarlosEvCode/botw-multiplayer-linux.
- Target: Cemu 1.26.2 (same version as the local project), BCML for the 32 player-model actor packs (`Jugador1`..`Jugador32`).
- License terms (README): free to use and modify, no redistribution unless a substantial part of the code is modified. A public fork needs care.

## Architecture (from source)

- `C#/BOTWM.Server` + `BOTW.DedicatedServer`: socket server, default `127.0.0.1:5050`, optional password, gamemodes in `Gamemodes.json`.
- `DLL/InjectDLL`: C++ DLL loaded into Cemu with `CreateRemoteThread` + `LoadLibraryA`. Finds game structures by memory pattern scan (`Scanner.cpp`) and reads/writes player data.
- `WPF .NET 6/...`: launcher UI, BCML settings, `CemuFollower` (window overlay), injects the DLL and talks to it over a named pipe.
- Synced per `PlayerDTO`: position, 4 rotations, animation, health, attack-up, equipped flag, equipment, location, bombs. Server also has enemy, quest, world, teleport, prop hunt and death swap DTOs.
- Not evidenced: shared item pool or inventory sync. Each player appears to keep a separate save.

## Observations relevant to two instances on one PC

- The pipe name is fixed: `\\.\pipe\languageConnectionPipe`, server created with max 2 instances, in both the DLL and the launcher. Two launcher/client pairs on one PC will collide unless the name is made per-instance (needs changes in DLL and launcher).
- `Injector.Inject` accepts a filter list of process IDs and there is an `InjectToSpecificProcess`, so targeting a specific Cemu process is plausible.
- DLL injection is likely to trigger antivirus heuristics.
- Cemu pattern scanning is tied to game/Cemu builds. Compatibility with our Cemu 1.26.2 variants and the multiplayer graphic pack must be checked before any combination.

## Other projects checked

- https://github.com/msork/BreathM: alpha 0.7, requires Cemu 2.x, gameplay sync not started. Not useful yet.
- The claim of an official PointCrow server / Discord-only distribution could not be verified.

## Maintainer info (Milk Bar Discord FAQ and channels, pasted by the user, 2026-10-22)

- Official download: GitHub release 2.0.1 of MilkBarModding/MilkBarLauncher (single `Milk Bar Launcher.bnp`; older BNPs from 2.0 and earlier no longer work). Project is open source and development has officially ended; forks may continue it.
- Cemu: must be 2.0-48 or earlier; maintainers recommend 1.26.2f. Needs the `Extended Memory` and `Utilities` graphic packs enabled, update v208 + DLC v80.
- Other requirements: BCML (Python 3.8.10 from python.org, not the Store), .NET 6 or 8 runtime, VC++ redistributable. Keep the launcher in a short root path such as `C:\BotwMP`.
- Multiplayer model: each player runs their own Cemu with their own game/AI. Synced: position, rotation, animations, weather/time, Korok seeds, some missions, armor, bombs; optional enemy health. Enemy position/AI is NOT synced. Bullet time affects only the local player. No shared inventory.
- Local split screen is possible by opening several instances of the mod ("some sort of split screen").
- Known issues: "All pipe instances are busy" (restart the UI), position drift under latency, invisible armor/weapons/players, red textures in Hateno, softlock leaving Vah Medoh with FPS++, crashes with more players.
- Incompatible: mods that change Link's animations or model. Our local multiplayer pack must be checked for this.
- Network: Hamachi or similar, or port forwarding; the server IP in `ServerConfig.ini` must match the VPN IP. Localhost works for same-PC tests.
- Not mentioned: the PointCrow-era server. The upstream repo is the real source.

## Isolated install (all under `.tools/`, git-ignored)

- `.tools/python38`: Python 3.8.10 (per-user, no PATH changes) with BCML 3.10.8.
- `.tools/localappdata/bcml`: BCML data dir for this project (`settings.json`, `mods/`, `merged/`). Always run BCML with `LOCALAPPDATA` pointing to `.tools/localappdata`.
- `.tools/instances/cemuA`: clean Cemu 1.26.2 (same `Cemu.exe` hash as the official zip). Own `mlc01` with empty saves; update/DLC are read-only junctions to the existing install. `graphicPacks/BreathOfTheWild_BCML` is a junction to `.tools/localappdata/bcml/merged`.
- `MilkBarLauncher.bnp` is installed and merged. `Extended Memory` and the BCML packs are enabled in `cemuA/settings.xml`.
- Graphics: use Vulkan (`<api>1</api>`) with the copied BOTW shader caches and the reference `gameProfiles/00050000101c9400.ini`. With OpenGL and an empty shader cache the first boot hung on the loading screen; with Vulkan the cached pipelines (~41k) take a few minutes to load, then the game reaches the title screen at ~30 FPS.

- Starting a brand new game in `cemuA` hung on the loading screen after the intro (also without any mod packs and with a fixed audio device). Loading a copy of an existing save (`user\80000001` copied from the reference Cemu, read-only) works. Use a copied save as the baseline.

### Isolation caveats

- The `LOCALAPPDATA` override is honoured by BCML's Python code but NOT by its Rust extension (`rsext.link_master_mod`, called by `install.refresh_master_export`). It reads the global BCML settings and rebuilt `bcml_store\merged` of the other Cemu (verified equivalent, all 394 files of its `9999_BCML` present with the same sizes). Never call `refresh_master_export`, `link_master_mod` or the BCML GUI for this project. Export by copying `mods/9999_BCML` into `merged/` instead.
- The launcher reads and writes `{store_dir}\merged\content` from its `bcmlLocation` user setting (empty by default, chosen through a file dialog). It must point at `.tools/localappdata/bcml/settings.json`, never at the global one.

### First working instance (cemuA)

- Server: `MBL.DedicatedServer.exe` with `DefaultGamemode=True`; it listens on the machine's VPN/Tailscale IP (port 5050), so the launcher entry uses that IP.
- The launcher refuses to connect with "Mod is not setup on BCML" unless `merged\content\Actor\Pack\Jugador*.sbactorpack` exist (32 files). Copy them from `mods\0100_MilkBarLauncher\content`.
- The BCML-merged `TitleBG.pack` was missing the mod's `EventFlow/MultiplayerEvent`, `AddItem`, `BindEvent`, `ChangeFlag` and `Event/ResidentEvent.byml`. Without `MultiplayerEvent.bfevfl` the DLL cannot find the `Jugador*_Hold` flag ("Failed to find mod address"). Fix: copy those 5 entries from the mod's own `TitleBG.pack` into `merged\content\Pack\TitleBG.pack` (oead SARC edit).
- The DLL starts scanning ~45 s after Connect and needs the save loaded (press Continue straight away). Each scan retries 15 times and then throws, which closes Cemu.
- Result: "Scanned game flags successfully" and "Joined server" in `%APPDATA%\BOTWM\LatestLog.txt`.

### Two instances on one PC (cemuA + cemuB)

- Start order: server, then launcher A and its Cemu (press Continue within ~60 s), then launcher B and its Cemu. Each launcher folder has its own `user.config` and its own BCML settings (`bcml\settings.json` for A, `bcml-b\settings.json` for B, same `store_dir`). Player names differ (Link / Zelda).
- Both DLLs write the same `%APPDATA%\BOTWM\LatestLog.txt`, so lines from both instances interleave. The server assigns player 0 to A and player 1 to B.
- Both instances run at ~30 FPS with Vulkan (OpenGL hangs after the intro).

### Files BCML drops from the merge (fixed by hand in `merged\content` and both graphic packs)

BCML's merged output is incomplete for this mod. Compare the mod's own packs against the merged result whenever something does not work.

- `Actor\Pack\GameRomCamera.sbactorpack` lacked `Actor/AISchedule/GameRomCamera.baischedule`. That actor runs the `GameRomCamera_EachFrame` event flow that sets the `notPaused` flag. Without it the DLL logs "Create request failed: Game is paused." forever and never spawns anyone. After the fix the log shows "Game paused" / "Game unpaused".
- `Actor\ActorInfo.product.sbyml` was missing entirely, so the game did not know `Jugador1..32`. Rebuilt from the vanilla update file plus the 39 added actors and 2 partial diffs in the mod's `logs\actorinfo.yml` (merged by name hash, 7973 actors). After that the log shows "Spawned player N" and the remote Link appears.
- `Pack\TitleBG.pack` event flows (see above).

### Input separation

- Both Cemu copies shipped the same XInput profile (uuid 0), so both Links moved with one controller. With two physical controllers, cemuB uses the same profile with `<uuid>1</uuid>`.
- A hand-written `<api>Keyboard</api>` profile crashes Cemu 1.26.2f at startup (C++ exception while creating the main window). Do not reuse it; use a second XInput pad or create the profile from the Cemu UI.

### Scripted start

`scripts/Start-Online.ps1` refuses to run if Cemu, a Milk Bar launcher or the server is already open. It then writes the controller profile of each Cemu (A = XInput 0, B = XInput 1, same bindings as the local launcher's `Configure-Cemu.ps1`), starts the server, and drives launcher A and B through `scripts/Connect-MilkBar.ps1` (UI Automation on the Connect button). It waits for "Joined server" in the DLL log before starting B. The user still presses Continue in each Cemu. Verified once end to end (both joined, "Spawned player" logged).

`scripts/Stop-Online.ps1` closes only the Cemu, launcher, server and router processes that run from this repository.

Pitfall: the DLL's address scan starts about 45-50 s after Connect and needs the save loaded. If Continue is not pressed in time it logs "Could not find map pin address" 15 times, throws a C++ exception and Cemu closes. Press Continue as soon as the main menu appears.

### SDL input router (`-InputMode Routed`)

`scripts/Build-Input.ps1` downloads the pinned SDL3 and GameControllerDB into `.tools/input` and builds into `.artifacts/input`: `InputRouter.exe` (headless, C# 5 with csc) and `Bridge\xinput1_4.dll` (zig, the local launcher's bridge, unchanged). With `Start-Online.ps1 -InputMode Routed` the bridge DLL is copied beside both Cemu executables and the router is started. The router reads up to four pads through SDL3, assigns the first pad to player 1 and the second to player 2, and publishes them in shared memory `Local\HyruleCoop.Routing.v2`. The bridge answers Cemu's XInput index 0 with player 1 and index 1 with player 2. If the router stops, the bridge falls back to the real XInput after 5 s. `-InputMode Native` (default) removes the bridge and uses the Cemu profiles directly. The local launcher and the router must not run together.

Confirmed by the user in Routed mode: both Links visible in both windows and each pad moves only its own Link. Rumble is not verified yet.

### Launcher (`HyruleOnline.exe`)

`scripts/Build.ps1` runs `Build-Input.ps1`, then compiles the WPF launcher (csc, no extra dependencies) into `.artifacts\app`. It hosts the same router in-process (`src/Input/Router.cs` + `Sdl.cs`, shared with the console `InputRouter.exe`), so it can show live pads. Functions adapted from the local co-op launcher: live stick/trigger preview, assign by pressing the bottom face button, swap players, vibration test, reserved player slots across reconnects. Functions that do not apply online and were dropped: split-screen layout, display plan and resolution changes, motion/DSU, Steam shortcut and the in-game return chord.

The Start button runs `Start-Online.ps1 -InputMode Routed -ExternalRouter` (hidden) and maps its output lines to the three step indicators (server, Cemu A, Cemu B). It prompts for Continue at the right moment. Stop runs `Stop-Online.ps1`. Every two seconds it also checks which project-owned Cemu/server processes exist, so it reflects sessions started elsewhere. Closing it offers: stop the session, close only the launcher (the bridge falls back to real XInput after 5 s, and the Cemu profiles are XInput 0/1), or cancel. `scripts/Test.ps1` runs the router reconciliation and assignment tests.

### Result (2026-10-22)

Both instances on localhost see the other player's Link, and each controller moves only its own Link. Confirmed visually by the user. Still untested: other machines over a VPN, long sessions, armor sync edge cases ("Couldn't find armor address" appears once, then it retries and succeeds).

## Open questions

0. Pipe limit: the launcher creates `languageConnectionPipe` with max 2 instances, which fits exactly two launchers on one PC. The DLL pipe name is fixed, and in the two-instance run both DLLs connected without a visible mix-up, but this was not tested on purpose.
1. ~~Where are binaries?~~ Answered: GitHub release 2.0.1 (also the `.bnp` in the repo, 15.8 MB).
2. Does the pattern scan work on our Cemu 1.26.2 copy, and does it conflict with the `BOTWMultiplayer` graphic pack?
3. Which parts of our launcher (display policy, Apollo/Artemis, controller routing, Steam shortcut) can drive two Cemu processes.
4. Local per-instance pipe name and port handling.
