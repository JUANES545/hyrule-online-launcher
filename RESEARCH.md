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

- Start order: server, then launcher A and its Cemu (press Continue within ~60 s), then launcher B and its Cemu. Each launcher folder has its own `user.config` and its own BCML settings (`bcml\settings.json` for A, `bcml-b\settings.json` for B, each with its own `store_dir`, see "Player name and model"). Player names differ (Link / Pedro by default).
- Both DLLs write the same `%APPDATA%\BOTWM\LatestLog.txt`, so lines from both instances interleave. The server assigns player 0 to A and player 1 to B.
- Both instances run at ~30 FPS with Vulkan (OpenGL hangs after the intro).

### Files BCML drops from the merge (fixed by hand in `merged\content` and both graphic packs)

BCML's merged output is incomplete for this mod. Compare the mod's own packs against the merged result whenever something does not work.

- `Actor\Pack\GameRomCamera.sbactorpack` lacked `Actor/AISchedule/GameRomCamera.baischedule`. That actor runs the `GameRomCamera_EachFrame` event flow that sets the `notPaused` flag. Without it the DLL logs "Create request failed: Game is paused." forever and never spawns anyone. After the fix the log shows "Game paused" / "Game unpaused".
- `Actor\ActorInfo.product.sbyml` was missing entirely, so the game did not know `Jugador1..32`. Rebuilt from the vanilla update file plus the 39 added actors and 2 partial diffs in the mod's `logs\actorinfo.yml` (merged by name hash, 7973 actors). After that the log shows "Spawned player N" and the remote Link appears.
- `Pack\TitleBG.pack` event flows (see above).

`scripts/Patch-Mod.ps1` reapplies all of this, plus the 32 `Jugador*.sbactorpack` files, to `merged\content` and to every instance graphic pack that is not a junction to it (`scripts/patch_mod.py`, run with the project's Python and `oead`). It reads only the mod's own packs and `logs\actorinfo.yml` and the vanilla `ActorInfo.product.sbyml` from an instance's `mlc01`. It is idempotent (writes only on a difference, atomically), refuses to run while a project Cemu is open, and `-Check` reports what is missing and exits 1 without writing. Run it after any BCML merge or clean install. Verified by putting the BCML export (`mods\9999_BCML`) back into `merged` and cemuB's pack, deleting `ActorInfo`, then patching: `-Check` went from 6 pending files to clean, and `ActorInfo` and `GameRomCamera` came out byte-identical to the hand-patched ones. `TitleBG.pack` is identical entry by entry except `Armor_Default_Extra_00/01` and `GameROMPlayer`, which the Milk Bar launcher rewrites on every Connect (player model selection).

### Input separation

- Both Cemu copies shipped the same XInput profile (uuid 0), so both Links moved with one controller. With two physical controllers, cemuB uses the same profile with `<uuid>1</uuid>`.
- A hand-written `<api>Keyboard</api>` profile crashes Cemu 1.26.2f at startup (C++ exception while creating the main window). Do not reuse it; use a second XInput pad or create the profile from the Cemu UI.

### Scripted start

`scripts/Start-Online.ps1` refuses to run if Cemu, a Milk Bar launcher or the server is already open. It then writes the controller profile of each Cemu (A = XInput 0, B = XInput 1, same bindings as the local launcher's `Configure-Cemu.ps1`), starts the server, and drives launcher A and B through `scripts/Connect-MilkBar.ps1` (UI Automation on the Connect button). B is staggered behind A, not fully simultaneous: the DLL always connects to the fixed pipe `\\.\pipe\languageConnectionPipe`, and the server numbers players by connection order, so two DLLs connecting at once could attach to the wrong launcher and swap the player numbers. The script waits for "Assigned to player" from A, then until Cemu A's window title leaves "loading..." (the shaders and game are loaded), and only then starts B. B's load then overlaps with the time the user needs to press Continue on A. The script finally waits for two "Joined server" lines in the DLL log. The user still presses Continue in each Cemu. Measured: A loaded 43 s after Connect, B connected 3 s later, both joined about 2 min 15 s after Start (about 3 min before). The shared log cannot say which player joined, so only the count is reported.

`scripts/Stop-Online.ps1` closes only the Cemu, launcher, server and router processes that run from this repository.

Pitfall: the DLL's address scan starts about 45-50 s after Connect and needs the save loaded. If Continue is not pressed in time it logs "Could not find map pin address" 15 times, throws a C++ exception and Cemu closes. Press Continue as soon as the main menu appears.

### SDL input router (`-InputMode Routed`)

`scripts/Build-Input.ps1` downloads the pinned SDL3 and GameControllerDB into `.tools/input` and builds into `.artifacts/input`: `InputRouter.exe` (headless, C# 5 with csc) and `Bridge\xinput1_4.dll` (zig, the local launcher's bridge, unchanged). With `Start-Online.ps1 -InputMode Routed` the bridge DLL is copied beside both Cemu executables and the router is started. The router reads up to four pads through SDL3, assigns the first pad to player 1 and the second to player 2, and publishes them in shared memory `Local\HyruleCoop.Routing.v2`. The bridge answers Cemu's XInput index 0 with player 1 and index 1 with player 2. If the router stops, the bridge falls back to the real XInput after 5 s. `-InputMode Native` (default) removes the bridge and uses the Cemu profiles directly. The local launcher and the router must not run together.

Confirmed by the user in Routed mode: both Links visible in both windows and each pad moves only its own Link. Rumble is not verified yet.

### Launcher (`HyruleOnline.exe`)

`scripts/Build.ps1` runs `Build-Input.ps1`, then compiles the WPF launcher (csc, no extra dependencies) into `.artifacts\app`. It hosts the same router in-process (`src/Input/Router.cs` + `Sdl.cs`, shared with the console `InputRouter.exe`), so it can show live pads. Functions adapted from the local co-op launcher: live stick/trigger preview, assign by pressing the bottom face button, swap players, vibration test, reserved player slots across reconnects, and its look (fullscreen on the primary display, same fonts, controller cards, display panel with split preview). Functions that do not apply online and were dropped: rotation, the compositor, motion/DSU, Steam shortcut and the in-game return chord.

### Display: one window per player, rendered in its own shape

The launcher splits the primary display (physical pixels from `EnumDisplaySettings`) between the two Cemu windows: Auto goes side by side from 2:1 up, otherwise stacked; the Split button can force either. `ScreenLayout.Plan` (src/Launcher/Layout.cs) turns the display size, split and quality into one `PlayerView` per window: area, even render size (capped at 8192 per side), a reduced aspect ratio (terms kept at or below 1000) and the HUD flags.

Before Cemu starts, `CemuDisplay.Apply` (src/Launcher/CemuDisplay.cs) writes that view into each instance. It copies the stock BOTW Graphics pack to `graphicPacks\HyruleOnline\Graphics` (the downloaded pack is never edited), appends two presets, `Online Display` (aspect ratio) and `Online Resolution`, and selects them in `settings.xml`. The stock Graphics entry is removed so two packs never patch the same code, and `FullscreenScaling` stays at 1 (keep aspect). Ultrawide HUD options are enabled above 16:9 and square HUD options at 3:2 or narrower, mirroring the stock presets.

Placing the window: the launcher strips the title bar and border, removes the menu bar and sets the window to its area. Cemu puts its menu back when the game loads (it costs 20 px of picture), so the launcher removes it again as soon as it sees it. Each window is placed once, so moving it by hand is not undone.

Real session on the 5120x1440 primary display, confirmed by screenshot: side by side gave two 2560x1440 (16:9) windows, and top/bottom gave two 5120x720 (64:9) windows, with the game area equal to the window in both and no letterboxing or stretching. In top/bottom the HUD sits on the screen edges. Quality and FPS are saved but not yet applied to Cemu.

`scripts/Test.ps1` covers the plan for 22 displays (4:3 to 32:9, portrait, odd sizes, 8K) with every split and quality: the two windows tile the display, render sizes are even and within the limit, the aspect ratio matches the render, the HUD flags follow the shape. It also checks the written pack and `settings.xml` on a temporary instance (stock pack untouched, repeat runs replace values, other packs kept).

The Start button runs `Start-Online.ps1 -InputMode Routed -ExternalRouter` (hidden) and maps its output lines to the three step indicators (server, Cemu A, Cemu B). It prompts for Continue at the right moment. Stop runs `Stop-Online.ps1`. Every two seconds it also checks which project-owned Cemu/server processes exist, so it reflects sessions started elsewhere. Closing it offers: stop the session, close only the launcher (the bridge falls back to real XInput after 5 s, and the Cemu profiles are XInput 0/1), or cancel. `scripts/Test.ps1` runs the router and display tests.

### Result (2026-10-22)

Both instances on localhost see the other player's Link, and each controller moves only its own Link. Confirmed visually by the user. Still untested: other machines over a VPN, long sessions, armor sync edge cases ("Couldn't find armor address" appears once, then it retries and succeeds).

### Hidden Milk Bar and server

The launcher starts `MBL.DedicatedServer.exe` itself (`ServerHost`, no console window, redirected stdin/stdout) and passes `-ExternalServer` to `Start-Online.ps1`. Its output goes to a "Server console" box in the details panel, with a one-line command box, and a small "Server running" chip sits in the header corner. `Start-Online.ps1` run on its own still starts the server hidden.

Milk Bar Launcher is a WPF window that ignores `-WindowStyle Hidden`, and `SW_HIDE` makes its UI Automation tree unusable (no Connect button). What works: `Connect-MilkBar.ps1` turns the window into a tool window (out of the taskbar and Alt+Tab) and moves it to (-32000, -32000) as soon as it appears. It stays "visible" for UIA, so Connect can still be invoked. Pass `-ShowMilkBar` to `Start-Online.ps1` to keep it on screen when debugging.

Checked on 2026-10-22: both players joined (server log shows players 1 and 2), the Milk Bar and server processes report no main window, and Stop leaves no project process behind. A visual check that nothing flashes on screen is still pending the user's confirmation.

### FPS target from the mod

Frame rate is not a Milk Bar feature: it is the Cemu graphic pack FPS++ (`Mods\FPS++`), which was not enabled before, so the game ran at 30 FPS. `FpsMod` reads the "FPS Limit" presets of Normal Settings from the instance's own `rules.txt` (20, 30, 45, 55, 60, 72, 75, 90, 120, 144, 165, 240 for v6; the Advanced-only rates are not offered), and the launcher shows a dropdown with only those from 30 up to 144 that the primary display can show (one Hz of slack, because Windows reports 143.98 Hz as 143; 60 is assumed when Windows reports 0 or 1). The first entry is Auto, the default: it picks the highest offered rate of at least 60 FPS that divides the refresh rate evenly (120 on 240 Hz, 144 on 144 Hz, 90 on 100 Hz), so frames are paced without judder. The choice is saved as a wish in `preferences.xml` (`Auto` or a number); if a display cannot show it, the next lower offered rate is used, and the wish is kept for a faster display. Each session writes the `FPS++` entry (Mode = Normal Settings, FPS Limit = preset name) into both instances' `settings.xml`.

Checked on 2026-10-22 with 120 selected on a 240 Hz display: Cemu A showed about 100 FPS while B was loading, and both instances about 82 and 119 FPS once running. Not checked: the known MBL softlock when leaving Vah Medoh with FPS++, and long sessions at high rates.

### Controller motion display (DualSense)

Each player card shows a small glyph for the motion sensors of its controller, ported from the local co-op launcher. `Sdl` sets `SDL_JOYSTICK_ENHANCED_REPORTS`, enables the accelerometer and gyroscope when the pad has them, and drains the sensor events on the router thread; `ControllerMotion` turns them into a `MotionReading` (tilt in g relative to the pose when motion started, a gyro angle limited to 35 degrees, activity 0..1) that travels in the `RouterSnapshot` next to the pad state. The glyph follows the player's route, so it moves with the controller when players are swapped or reassigned. A crossed-out glyph means the pad has no sensors (XInput pads, for example); a dashed ring means only one of the two sensors reports. This is a display only: the readings are not sent to Cemu (the old DSU server was not ported).

Checked with the unit tests (`controller motion`) and by starting the launcher with two XInput pads, which show the crossed-out glyph. Not checked: a real DualSense, which still needs the user to connect one and move it.
## Graphics quality

The launcher replaces the old balanced/sharp toggle with options read from the stock BOTW Graphics pack (`rules.txt` in the cemuA instance), so it only offers what the pack supports.

- Render resolution: a free scale (50%, 75%, 100%, 125%, 150%, 200%) of each window size. It feeds the `Online Resolution` preset.
- Shadows, Shadow draw distance and Anti-aliasing: dropdowns built from the pack categories. Labels keep percentages and the Default/Recommended/Unstable flags.
- Choices are written as presets in the `HyruleOnline\Graphics` entry of both instances' `settings.xml` when a session starts; they never change a running game.
- Preferences: `RenderScale`, `Shadows`, `ShadowDistance`, `AntiAliasing` (an old `Quality=Sharp` maps to 1.5).

Checked: tests, UI layout, and a short real session with 150% / Shadows High / Very High distance / AA None. Both `settings.xml` files got the presets. Visual quality in game is not confirmed yet.

## Pages and window fitting

The launcher no longer scrolls. It has three pages (Play, Graphics, Details) shown one at a time, so new settings get their own page instead of crowding one view.

- Navigation: LB / RB on any controller, Ctrl+Tab / Ctrl+Shift+Tab, or the tabs. Pages wrap around. LB / RB icons flank the tabs. Controller paging only works while the launcher window is in front and no dropdown is open, so it never fights with the games.
- Fitting: the UI is drawn on a design canvas that a Viewbox scales to the window. `ShellFit` picks the canvas from the window shape: wide windows get a wider canvas up to 1760 units and the content is centred, narrower windows get a taller canvas, and tall windows stack the display column under the players. Tests cover 156 window sizes.
- `HYRULE_LAUNCHER_SIZE=WIDTHxHEIGHT` opens a smaller window for layout checks (the default is the primary display).

Checked: tests, and screenshots at 1920x1080, 1280x720, 1024x600, 3440x1440, a tall 900x1400 window and the 5120x1440 display, plus Ctrl+Tab. LB / RB with a physical pad is not confirmed yet.

## Player name and model

Each player card has a name box and a model list. They are written to each Milk Bar launcher's `user.config` (`playerName`, `playAsModel`, `playerModel`) right before the session starts, so the Milk Bar window is never needed. Defaults are Link and Pedro. The model list comes from the `NpcData.json` embedded in `Milk Bar Launcher.dll` (Link plus 34 NPCs). Only Link and Urbosa are confirmed working as the local player (checked in a real session with both windows); the other NPC models are untested (flagged `Experimental` in `Characters.cs`, not labelled in the UI; update `Verified` as more are confirmed). Revali hung the loading screen of the instance that used it, so it is not offered. BumiiMaker, Environmental and Mii models are not offered.

- On Connect the Milk Bar launcher rewrites `GameROMPlayer`, `Armor_Default_Extra_00/01` and `PauseMenuPlayer` in `TitleBG.pack` under `<store_dir>\merged`, and sends the name and remote model to the server. So the local model of an instance depends on its own `merged`.
- Each instance therefore needs its own `merged`. Cemu A's graphic pack is a junction to `localappdata\bcml\merged`. Cemu B has a real copy, and `bcml-b\settings.json` now uses `store_dir` = `localappdata\bcml-b\store`, where `merged` is a junction to cemuB's `BreathOfTheWild_BCML`. With the shared `store_dir`, B's model was written into A's pack: in a first test A showed both players as Revali and B showed both as Link.
- Other mod options not in the launcher yet: the server `Password`, `Description`, IP and port, the `Landmarks`, `DeathSwap`, `Glyph` and `PropHunt` console commands. The sync rules, the preset list, Time, Weather, NameTags, sync pause / resume and Teleport are on the Multiplayer page (below).

## Multiplayer page

A fourth page next to Play, Graphics and Language, aimed at local play but built on the same server options an online host would use.

- Rules card: preset list (`Gamemodes.json`; Prop Hunt is hidden because it needs locations), the Special mode (None, Hunter vs Speedrunner, Death Swap) and one checkbox per sync rule. The launcher rewrites only the `[DefaultGamemode]` keys of `ServerConfig.ini` (`EnemySync`, `QuestSync`, `KorokSync`, `TowerSync`, `ShrineSync`, `LocationSync`, `DungeonSync`, `Special`) plus `DefaultGamemode=True`, and keeps IP, port, password, comments, line endings and the UTF-8 BOM. Rules are locked while a session runs.
- Live card (enabled only while the launcher's own server runs, through the server's stdin): `Time Set HH:MM`, `Weather Set <word>`, `NameTags` (toggle, the checkbox follows the server's reply), `Stop` / `Start` (pause / resume enemy and quest sync) and `Teleport <name> <name>`.
- Teleport matches players by name (quoted when it has spaces), not by `p1` / `p2` or by join number. The server only knows a player's name once that game is in the world, so before that it answers "Could not find player" or "Destination input was not valid".
- Checked in real sessions: the preset and each checkbox rewrite the ini; time, weather, name tags and sync pause / resume print the expected server lines; Teleport was accepted for both players once both games were in the world ("Requested teleport of 1 players to ..."). That the character visibly moves, and Prop Hunt, are not confirmed.

## Language

- The game is the US version. `content\Pack` has `Bootup_USen.pack`, `Bootup_USes.pack` and `Bootup_USfr.pack`, so the launcher offers English, Español (Latinoamérica) and Français (Canada). The list comes from a scan of those packs (read-only, through `game_dir` in the project's BCML settings), so other regions list their own languages.
- The game language is Cemu's `<console_language>` in `settings.xml` (direct child of `<content>`): 0 ja, 1 en, 2 fr, 3 de, 4 it, 5 es, 6 zh, 7 ko, 8 nl, 9 pt, 10 ru, 11 Traditional Chinese. `<language>` is Cemu's own menu language and is left alone. Both instances get the value each time a session starts.
- The launcher language is separate from the game language. The Language page has a Launcher row (English, Español, Français; English by default, saved as `LauncherLanguage` in `preferences.xml`) that changes the launcher text at once, and a Game list that only writes Cemu's setting. Any combination works, for example a Spanish launcher with an English game. Older preferences without `LauncherLanguage` start in English.
- Log lines and the mod's own graphics pack labels stay English.
- Checked in a real session in Spanish: both `settings.xml` files got `5` and the game text came up in Spanish. The BCML-merged content only has `Bootup_USen.pack`, so text added by mods can stay in English.
- One Cemu A crash (exception thrown from `InjectDLL.dll` after both instances were loading) happened in the first Spanish run and did not repeat in the second. Not shown to depend on the language.

## One display per player (built)

Findings from this machine and the code, and what was built:

- Windows lists two displays here: `\\.\DISPLAY2` primary 5120x1440 @ 240 Hz at 0,0 and `\\.\DISPLAY1` 1920x720 @ 60 Hz at 1602,1440. `EnumDisplayDevices` plus `EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS)` return each attached display's physical size, refresh rate and desktop position (mirrored copies are skipped), so no new dependency is needed.
- Cemu's manifest is `dpiAware True/PM` (per-monitor). Confirmed in a real session: with one display per player, Cemu A sat at 0,0 with a 5120x1440 client area on `DISPLAY2` and Cemu B at 1602,1440 with a 1920x720 client area on `DISPLAY1`, matching the log lines.
- `CemuDisplay.Apply` and `FpsMod` already work per instance, so only the plan changed: `ScreenLayout.PlanSeparate` builds one `PlayerView` per chosen display. Each player has their own render scale and FPS target, and the FPS list uses the refresh rate of that player's display (60 Hz here for the secondary one).
- The primary-display split stays the default. The "Displays" button only shows with two or more usable displays (at least 320x240), and `ScreenLayout.Pair` falls back to the first free display when a chosen one is gone, and never gives both players the same display. Choosing the other player's display swaps them.
- The choice is saved in `preferences.xml` (`Arrangement`, `Display1/2`, `Scale1/2`, `Fps1/2`). The launcher re-reads the display list every minute and updates when displays appear or disappear.
- Not verified: gameplay on the second display (the real session only covered placement, sizes and the log until the first player loaded), more than two displays, and displays with a different DPI scale.

## Advanced Cemu graphics and option hints (built)

- The Graphics page has a second column, "Advanced (Cemu)", written into each instance's `settings.xml` `<Graphic>` block when a session starts: `api` (0 OpenGL, 1 Vulkan), `VSync`, `AsyncCompile`, `VulkanAccurateBarriers`, `UpscaleFilter` and `DownscaleFilter` (0 bilinear, 1 bicubic, 2 bicubic hermite, 3 nearest neighbor). The values follow the numbers already in the real `settings.xml` and Cemu 2.x's `CemuConfig.h`; the 1.26.2 meaning of each is not independently verified, apart from `api`.
- Auto resolves to Vulkan when `vulkan-1.dll` loads and to OpenGL otherwise. It does not benchmark the GPU. Vulkan is disabled in the list when the DLL is missing, and accurate barriers only apply to Vulkan.
- Checked in a real session on this machine: with the preference forced to OpenGL, Cemu A's log shows "Init OpenGL graphics backend" and `settings.xml` holds `api 0`, `AsyncCompile true`, filters 3 and 1. The default run earlier logged "Init Vulkan graphics backend" on the RTX 5070 Ti. The session was stopped right after Cemu A started, so OpenGL gameplay was not checked.
- Caution: the earlier isolated-install notes say an OpenGL first boot with an empty shader cache hung on the loading screen, and the shader cache is separate per API. Changing the API may force a long shader rebuild on the next start, which also delays player B in the staggered start. The UI warns about it in soft words; this has not been measured.
- Hints: every Graphics and Multiplayer option has a tooltip and an info dot. A hint bar under the options shows the same text on mouse hover, keyboard focus or when a controller cursor lands on the option.

## Avatar picker and controller navigation (built)

- Avatar images are the mod's own busts: the Milk Bar DLL embeds `images/bust/*.png` (transparent, one style, face visible). The launcher reads them at run time from the installed DLL; none are copied into the repository. They were used instead of images from the internet because of copyright and the rule against committing mod assets.
- On Play, the avatar slot of each card opens a mosaic (7 columns) with the name under each image. Both cards can be open together and the two players choose at the same time. Wide windows show them side by side; portrait windows keep them stacked.
- Controller cursors: the controller routed to a player drives that player's cursor (green P1, orange P2); every other controller, and every controller on the other pages, shares a white cursor. D-pad or left stick moves by screen position, A presses/toggles/opens a list, B goes back or closes. LB/RB still switch pages (blocked while a list or mosaic is open). The cursor does nothing unless the launcher is the active window, and is paused while a controller is being assigned (B cancels the assignment).
- Verified: pure logic by `Test.ps1` (repeat, dead zone, nearest-control picking) and a scripted run (`HYRULE_PAD_SCRIPT`, simulated presses) that opens both mosaics, picks avatars, changes a list value with the cursor and flips pages at 1920x1080, 1280x720 and 800x1000. Not verified: a real physical controller, and names cannot be typed with a pad (the name box only takes keyboard text).
- Fine adjustment (tested with a ViGEm virtual Xbox pad driving the real launcher, 1600x900 and 1280x720):
  - The first cursor seat ran before a new page was laid out, so only Help and the footer were candidates and every page started on Help. `ShowPage` now lays the page out first, and the cursor lands on the first option of the page (never Help or the footer).
  - Each cursor remembers where it left every page; coming back with LB/RB puts it there again.
  - Left and right only move along the current row, so they no longer jump to an unrelated row; up and down fall back to the nearest box when nothing is straight above or below. An audit of every page's neighbour graph found no unreachable option.
  - With Help open the cursor stays on Help, and B closes it.
  - Not covered: typing text (names, time of day, server command) still needs a keyboard.

## One, three or four players (research, not built)

The launcher and its helpers assume two players in many places, so the answers differ a lot.

- **One player: low effort.** It needs one Cemu on the whole display, no second launcher or second instance, and the layout, router and status code accepting a count of 1. The server and the mod do not require a second player. Still to confirm: that the Milk Bar DLL works alone with the dedicated server (it should, since player A already joins first).
- **Three or four players: medium to high effort and not safe to promise.**
  - The code hard-codes two: `joined = new bool[2]`, `for (i < 2)` loops in `Launcher.cs`, the `cemuA`/`cemuB` names and paths, `PlayerView[2]` in `Layout.cs`, `Router.Routes = new int[2]`, and `player[2]` in `xinput_bridge.c` (the shared memory already holds four pad states, the bridge answers only Cemu XInput 0 and 1).
  - More instances (`cemuC`, `cemuD`) mean more disk, RAM and VRAM: each BOTW instance with shaders is heavy, and four render windows on one GPU will probably not hold 60 FPS.
  - The Milk Bar DLL uses one fixed pipe and the launcher creates it with max 2 instances; starts would have to stay staggered one by one and the pipe limit would need a patch or a different launcher.
  - The server config has no player cap here, but the DLL has a `PlayerLimit` setting whose value was not read, and the Milk Bar FAQ lists "crashes with more players" as a known issue.
  - The layout would need 2x2 cells, per-display mode would become N displays, and names, models, teleport and rules would need N players.
- **More than two players with the extra ones on other PCs.** The local limits (RAM, VRAM, one pipe, the launcher's two-player layout) go away, because each remote player runs their own Cemu. The launcher would host the server plus one or two local players. What remains is the mod and server's own limit and the FAQ's crash reports, which depend on the mod and were not tested here.
- Not tested: any session with more than two Cemu instances.

## Restart Session

While a session is open, the Start button becomes "Restart session" as soon as an option that is only read at start differs from what the session was started with. Pressing it stops the session and starts it again with the new values.

- **Needs a restart:** graphics quality, advanced Cemu options (API, VSync, async shaders, barriers, up/downscale), FPS target, display, split, one display per player and scale, game language, player names, avatars and rules. They are written to Cemu's `settings.xml`, the graphic packs, the Milk Bar `settings.json` or the server ini before the games start.
- **Applies live:** the launcher language and the live controls (time, weather, name tags, teleport). Changing a value back to what the session started with removes the prompt.
- **Verified:** a scripted run (UI Automation) with a real server: Start stays disabled until an option changes, turns into "Restart session", turns back when the value is restored, and pressing it replaced the server process. Not verified: a full restart with both Cemu windows in the game (Cemu did not start in that scripted run, cause not found).

## Open questions

0. Pipe limit: the launcher creates `languageConnectionPipe` with max 2 instances, which fits exactly two launchers on one PC. The DLL pipe name is fixed, and in the two-instance run both DLLs connected without a visible mix-up, but this was not tested on purpose.
1. ~~Where are binaries?~~ Answered: GitHub release 2.0.1 (also the `.bnp` in the repo, 15.8 MB).
2. Does the pattern scan work on our Cemu 1.26.2 copy, and does it conflict with the `BOTWMultiplayer` graphic pack?
3. Which parts of our launcher (display policy, Apollo/Artemis, controller routing, Steam shortcut) can drive two Cemu processes.
4. Local per-instance pipe name and port handling.
