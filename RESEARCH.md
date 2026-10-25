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

`scripts/Patch-Mod.ps1` reapplies all of this, plus the 32 `Jugador*.sbactorpack` files, to `merged\content` and to every instance graphic pack that is not a junction to it (`scripts/patch_mod.py`, run with the project's Python and `oead`). It also builds `Bootup_USes.pack` and `Bootup_USfr.pack` from `logs\texts.json` and drops their RSTB entries (see Player name and model). It reads only the mod's own packs, `logs\texts.json` and `logs\actorinfo.yml` and the vanilla `ActorInfo.product.sbyml` from an instance's `mlc01`. It is idempotent (writes only on a difference, atomically), refuses to run while a project Cemu is open, and `-Check` reports what is missing and exits 1 without writing. Run it after any BCML merge or clean install. Verified by putting the BCML export (`mods\9999_BCML`) back into `merged` and cemuB's pack, deleting `ActorInfo`, then patching: `-Check` went from 6 pending files to clean, and `ActorInfo` and `GameRomCamera` came out byte-identical to the hand-patched ones. `TitleBG.pack` is identical entry by entry except `Armor_Default_Extra_00/01` and `GameROMPlayer`, which the Milk Bar launcher rewrites on every Connect (player model selection).

### Input separation

- Both Cemu copies shipped the same XInput profile (uuid 0), so both Links moved with one controller. With two physical controllers, cemuB uses the same profile with `<uuid>1</uuid>`.
- A hand-written `<api>Keyboard</api>` profile crashes Cemu 1.26.2f at startup (C++ exception while creating the main window). Do not reuse it; use a second XInput pad or create the profile from the Cemu UI.

### Scripted start

`scripts/Start-Online.ps1` refuses to run if Cemu, a Milk Bar launcher or the server is already open. It then writes the controller profile of each Cemu (A = XInput 0, B = XInput 1, same bindings as the local launcher's `Configure-Cemu.ps1`), starts the server, and drives launcher A and B through `scripts/Connect-MilkBar.ps1` (UI Automation on the Connect button). B is staggered behind A, not fully simultaneous: the DLL always connects to the fixed pipe `\\.\pipe\languageConnectionPipe`, and the server numbers players by connection order, so two DLLs connecting at once could attach to the wrong launcher and swap the player numbers. The script waits for "Assigned to player" from A, then until Cemu A's window title leaves "loading..." (the shaders and game are loaded), and only then starts B. B's load then overlaps with the time the user needs to press Continue on A. The script finally waits for the "Joined server" lines in the DLL log (two, or one with `-PlayerCount 1`, which also never starts B). The user still presses Continue in each Cemu. Measured: A loaded 43 s after Connect, B connected 3 s later, both joined about 2 min 15 s after Start (about 3 min before). The shared log cannot say which player joined, so only the count is reported.

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

The Start button runs `Start-Online.ps1 -InputMode Routed -ExternalRouter -PlayerCount N` (hidden) and maps its output lines to the step indicators (server, Cemu A, and Cemu B when there are two players). It prompts for Continue at the right moment. Stop runs `Stop-Online.ps1`. Every two seconds it also checks which project-owned Cemu/server processes exist, so it reflects sessions started elsewhere. Closing it offers: stop the session, close only the launcher (the bridge falls back to real XInput after 5 s, and the Cemu profiles are XInput 0/1), or cancel. `scripts/Test.ps1` runs the router and display tests.

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

Each player card has a name box and a model list. They are written to each Milk Bar launcher's `user.config` (`playerName`, `playAsModel`, `playerModel`) right before the session starts, so the Milk Bar window is never needed. Defaults are Link and Pedro. The model list comes from the `NpcData.json` embedded in `Milk Bar Launcher.dll` (Link plus 34 NPCs). Link, Urbosa, Revali, Mipha and Riju are confirmed working as the local player (real sessions with both windows, Vulkan, game language Spanish); Daruk and Paya are unconfirmed (see below), the other NPC models are untested. A static check of the model files (every `Model\Npc_*.sbfres` against the bones of `Player_Animation.sbfres`, plus the text packs) found nothing wrong except Hestu, whose skeleton has none of the body bones the player animations drive, so he is not offered (`Unsupported` in `Characters.cs`; Robbie only lacks face bones, which the NoFace animation does not use). Paya and Daruk failed to join within 180 s (player 2 stuck on the loading tips screen, its Cemu log flat right after the first Vulkan allocation), but that is not evidence against the models: every success was captured on a 5120x2160 display and those failures on other layouts (a 669x1447 or 1170x2532 virtual display, or an 8960x3780 desktop), and a control run of Urbosa, which passed before, failed the same way on the 1170x2532 display. So player 2 can stall on loading when the display layout is not the usual one; the cause is not found yet. Test avatars with the usual display layout and treat any other result as inconclusive. The model files show no difference from the working ones. Paya and Daruk are not marked verified. The rest are untested (flagged `Experimental` in `Characters.cs`, not labelled in the UI; update `Verified` as more are confirmed). An NPC avatar used to hang the loading screen of the instance that used it (Revali, Urbosa) when the game language was not English: see the text packs below. BumiiMaker, Environmental and Mii models are not offered.

Player 2 sometimes freezes while loading, with any avatar. Its Cemu `log.txt` shows the stall: after `IOSU_ACT` a healthy Cemu logs one `Vulkan-Info: Allocated additional memory` line, then at least two more about 30 s later when the main menu appears, while a stuck one stops after the first. `scripts/Cemu-Stall.ps1` (`Test-CemuStalled`, covered by `tests/Cemu-Stall.Tests.ps1` with logs from a real stuck and a real healthy run) detects that, and `Start-Online.ps1` then closes only Cemu B and its Milk Bar launcher and starts player B again, up to twice. Player A is never restarted because the server numbers players in the order they connect. This is not confirmed in game yet: it assumes the server frees player 2's slot within 5 s of the kill, so the new launcher is assigned player 2 again; if it is not, the existing 90 s "Assigned to player" timeout stops the start as before.

**Narrow windows and the HUD.** The 3D view and the resolution already follow the window, but the stock BOTW pack only corrects the HUD when `$ultrawideHUDMode` is on. Below 16:9 that comes from its "Square HUD Mode" category, whose default is Disabled, so on a phone-shaped display the HUD (minimap, icons) was squashed sideways. `CemuDisplay.Apply` now selects "Enabled (Experimental, Will Have Some Issues)" in `settings.xml` whenever the window is square or narrower (`PlayerView.Square`); the unit sweep covers 22 displays in every layout and rotation. Verified on 1170x2532 with two players (Link and Urbosa): the minimap is a circle and both joined. Cemu windows are also raised above the launcher after placement (`WindowPlacer.BringToFront`); in that run no launcher was visible over the game windows.

**Checking the characters offline.** `scripts/Test-Characters.ps1` (needs the clean install, never starts the game) compares every NPC model in the Milk Bar list with what the mod needs: model and texture files, the bones the player animations drive (taken from the bones shared by the four verified models, so those always pass), the size table entries, the Spanish and French player name labels, and that the merged armor packs are all Link's or all the one chosen NPC. It prints one row per model and exits 1 when an offered model fails. Current result: every model passes except Hestu, which is hidden on purpose, and Scientist_M_000, which only lacks eye bones (a warning). Paya (Shiekah_Heir) and Daruk (Goron_Hero) pass every static check, which agrees with the earlier finding that their failures are not about the model files.

- On Connect the Milk Bar launcher rewrites `GameROMPlayer`, `Armor_Default_Extra_00/01` and `PauseMenuPlayer` in `TitleBG.pack` under `<store_dir>\merged`, and sends the name and remote model to the server. So the local model of an instance depends on its own `merged`.
- **NPC avatar hang with a non-English game language (fixed 2026-10-22).** BCML merges the mod's text changes only into `Bootup_USen.pack` (`logs\texts.json` holds only `USen`), and the merged content had no `Bootup_USes.pack` or `Bootup_USfr.pack`, so Spanish and French used vanilla messages without the mod's labels (`Jugador1..32_Name` in `ActorType/NPC`, quest and layout texts). With an NPC as the local avatar the save never finished loading (the DLL stays silent after `Link:`; Link/Link was fine). Urbosa as player 2 failed twice in Spanish and worked in English. `patch_mod.py` now builds both packs with BCML's own text merger (`rsext.mergers.texts.merge_language`, which reproduces the existing `Bootup_USen.pack` byte for byte) and deletes the `Message/Msg_USes.product.sarc` and `Msg_USfr` entries from `ResourceSizeTable.product.srsizetable`. The second part is required: with the vanilla sizes left in the table the larger merged archives overflow their allocation and Cemu crashes during load (access violation at `cemu.exe+0x373b6b`); BCML already removes the USen entry for the same reason. Verified with Urbosa, Revali, Mipha and Riju as player 2 in Spanish. English and French were not re-run after the fix (French uses the same mechanism). Test pitfall: Cemu's confirm button is Xbox B, not A; pressing A makes player 2 stay on the title menu and looks like a failed join.
- Each instance therefore needs its own `merged`. Cemu A's graphic pack is a junction to `localappdata\bcml\merged`. Cemu B has a real copy, and `bcml-b\settings.json` now uses `store_dir` = `localappdata\bcml-b\store`, where `merged` is a junction to cemuB's `BreathOfTheWild_BCML`. With the shared `store_dir`, B's model was written into A's pack: in a first test A showed both players as Revali and B showed both as Link.
- Other mod options not in the launcher yet: the `Landmarks`, `DeathSwap`, `Glyph` and `PropHunt` console commands. The server `Password`, `Description` and port are on the Online page (below). The sync rules, the preset list, Time, Weather, NameTags, sync pause / resume and Teleport are on the Multiplayer page (below).

## Multiplayer page

A fourth page next to Play, Graphics and Language, aimed at local play but built on the same server options an online host would use.

- Rules card: preset list (`Gamemodes.json`; Prop Hunt is hidden because it needs locations), the Special mode (None, Hunter vs Speedrunner, Death Swap) and one checkbox per sync rule. The launcher rewrites only the `[DefaultGamemode]` keys of `ServerConfig.ini` (`EnemySync`, `QuestSync`, `KorokSync`, `TowerSync`, `ShrineSync`, `LocationSync`, `DungeonSync`, `Special`) plus `DefaultGamemode=True`, and keeps IP, port, password, comments, line endings and the UTF-8 BOM. Rules are locked while a session runs.
- Live card (enabled only while the launcher's own server runs, through the server's stdin): `Time Set HH:MM`, `Weather Set <word>`, `NameTags` (toggle, the checkbox follows the server's reply), `Stop` / `Start` (pause / resume enemy and quest sync) and `Teleport <name> <name>`.
- Teleport matches players by name (quoted when it has spaces), not by `p1` / `p2` or by join number. The server only knows a player's name once that game is in the world, so before that it answers "Could not find player" or "Destination input was not valid".
- Checked in real sessions: the preset and each checkbox rewrite the ini; time, weather, name tags and sync pause / resume print the expected server lines; Teleport was accepted for both players once both games were in the world ("Requested teleport of 1 players to ..."). That the character visibly moves, and Prop Hunt, are not confirmed.

## Online page: host or join a server (built; joined from the Ally over the LAN)

A page right after Play (tab order: Play, Online, Graphics, World, Language, Details) with three modes (`OnlineMode` in the preferences). The old Multiplayer tab is now "World" (world rules and live controls), and the Play page's switch reads "Switch to local multiplayer". The tab row is a `WrapPanel`, so on a narrow portrait window it wraps instead of overflowing; `LayoutTests` checks that it fits at every size. The launcher reopens on the last page (`LastPage` in the preferences, checked against a fixed list).

- **Quality of life (built, unit-tested, not tried against a real second PC):** a Copy invite button on Play while hosting; a Test connection button in Join (a TCP probe, so it does not check the password); readable join failures (after a failed Join start the launcher probes the port: closed means "could not reach", open means "check the password first", which is inferred because the client log has no wrong-password marker); a roster of players parsed from the server log (`ServerEvents`) with joined, left, refused and wrong-password notices; a server-crash notice through `ServerHost.Exited` that turns Start into Restart session (no automatic restart); the world rule controls locked while Joining, since the host sets them; and an address hint for Tailscale, ZeroTier, Hamachi and Radmin adapters (`NetworkHint`; privacy VPNs such as WireGuard, OpenVPN, Nord and Proton are ignored on purpose). Only error code 3 is known; other codes show as "error N".

- **Play on this PC (default):** the launcher writes `IP=127.0.0.1`, port 5050 and an empty password into `ServerConfig.ini`, so the server is private. Before this it kept `IP=localhost`, which binds one interface only (it picked Tailscale in a test).
- **Host a server:** `IP=0.0.0.0` (all interfaces; tested with a temp copy of the server) plus the user's port, password and description (`[Connection]` and `[ServerInformation]`; comments, line endings and the BOM are kept). The page shows the address to share (detected LAN or Tailscale address, editable for a public address or a name) and an invite `hyrule://host:port?pw=password` with a Copy button. The Windows Firewall button adds an inbound TCP rule `Hyrule Online Server <port>` through an elevated `netsh`; it shows whether the rule already exists. No public-IP service is queried, so port forwarding or Tailscale is up to the user.
- **Join a server:** one local player, no local server. The address box takes an invite link or `host:port` (`ServerConnection.TryParse`, host and port validated), plus an optional password. Start passes `-RemoteServer host:port` to `Start-Online.ps1`, which skips the server check and start and prints `Remote server: ...`. The launcher shows the Server step as "Remote". The player count goes back to two when the user leaves Join.
- **How clients know the server:** the Milk Bar client reads `serversAdded` from its own `user.config`. Each session start, `MilkBarSettings.SetServer` rewrites that list for every active player with one entry (127.0.0.1 for local and host, the remote address when joining), and `Connect-MilkBar.ps1` still clicks Connect.
- **Verified:** build, unit tests (invite parsing, ini rewrite, `serversAdded` write, preference round trip), the translation audit and the layout and pad-reachability tests for the new page. **Not verified:** a real second PC joining through this launcher, the server's player limit, whether the client picks the right entry if the list has several, and the firewall rule on a clean machine.
- **Ally / Steam Deck test bench:** the Ally ("steamdeck", Tailscale 100.110.16.75) was offline over Tailscale for about two days, but SSH worked over its LAN address. With a temp copy of the server bound to `0.0.0.0:5056` on the PC (192.168.1.8), a TCP connect from the Ally (Python socket) succeeded, so a remote machine can reach a hosted server on the LAN. The launcher is Windows only, so no join through the game was tried on SteamOS. Whether that connect needed the firewall rule was not isolated: the PC already let the connection through.
- **Real join from the Ally (LAN):** a temp copy of the server on `0.0.0.0:5050` with an empty password. On the Ally (SteamOS, no sudo, so no system Wine) Cemu 1.26.2 and the headless Milk Bar client (`MilkBar.CLI.exe --ip --port --name --model --cemu --game --dll`, from the `CarlosEvCode/MilkBarLauncher` fork) ran under Proton's Wine with its own prefix. The game, update, DLC and save were copied from the PC over tar and ssh (about 14 GB). Evidence: the client printed "Multiplayer session active", the server logged "Player Link joined the server. Assigned to player 1" (four sessions, 04:29 to 04:50), and a screenshot shows Cemu on the BOTW title menu. Not confirmed: the character in the world on the Ally, a second player seen in both games, the player limit, and the Windows launcher's own Join button (the Ally used its own `join.sh`).
- **Password enforced:** with `Password=secret42` in the ini, a client from the Ally with a wrong password got `Response:3` and the server logged "Player WrongPw tried to connect but failed with error 3"; with the right one it logged "Player RightPw joined the server. Assigned to player 1". Cemu on the Ally needs up to about a minute to reach the connect step, so a test script must wait for the client log instead of a fixed delay.
- **Server stability:** with stdin closed the server spins and fills `LatestLog.txt` (128 MB in minutes), so the launcher keeps stdin open. It once died with `ArgumentOutOfRangeException` in `ServerData.GetPlayer` from `Server.handleClient` after an unknown client sent a player number it had not been assigned; the cause was not isolated (a probe or the client).
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
- Menu button layout: Graphics > Menu controls picks Auto (default), Nintendo, Xbox or PlayStation. Auto follows each controller: SDL3's gamepad type (`SDL_GetGamepadType`, with a name fallback when the type is unknown) maps PS3/PS4/PS5 pads to PlayStation, Switch Pro/Joy-Con/GameCube pads to Nintendo, and Xbox 360/One and Steam Input pads to Xbox; with no controller the menus use Nintendo. Nintendo confirms with the right button and goes back with the bottom one; Xbox and PlayStation confirm with the bottom one (shown as A, or the cross on PlayStation). Buttons are read by position, so no mapping tables are needed. A forced layout applies to every controller. The old `PadLayout` preference is ignored because the previous default had been saved as Nintendo for everyone; the new key is `MenuLayout`. It changes only the launcher menus and the "assign a controller" press, never the game input, and never turns Start into Restart session. Verified by unit tests and a virtual pad (reported as an Xbox 360 pad, so Auto showed the Xbox layout). Not verified with a physical Switch or PlayStation pad.
- Recommended graphics (`Hardware.cs`): the launcher reads the graphics card name and memory from the registry, the processor threads and the RAM, rates the card from its name (RTX/GTX/RX/Arc/integrated patterns, laptop parts one class lower), then lowers the class when memory, threads or RAM are small. The class (Basic, Balanced, High, Ultra) picks the render scale per display (capped by the display size and player count), the shadow and shadow distance presets, the upscale filter and Vulkan or OpenGL. This is a heuristic, not a benchmark; the card name patterns were checked only against sample names in unit tests.
- Start and Select shortcuts: while the launcher is the active window and no list or the help is open, a new press of the centre buttons (XInput Start 0x10, Back 0x20) on any controller presses Start/Restart session (if the button is enabled) or, on the Play page, the single player-mode switch (if the options are enabled, so not while a session runs). The switch is one button sized to its text: "Switch to single player" in 2-player mode, "Switch to multiplayer" in 1-player mode. Both buttons carry a drawn icon after their text (`PadIcons.cs`): Menu/View on Xbox and Steam Input, Options/Create on PlayStation, +/− on Nintendo, following the first connected controller's layout. The PlayStation and Nintendo icons follow the usual button conventions, not the official artwork. Verified with unit tests and a virtual pad for Select (toggled 2 → 1 → 2, icons rendered with the Xbox layout); the Start press was not exercised because it launches the games.
- Recommended multiplayer rules: the rules are gameplay choices, not performance ones, so "Use recommended rules" selects the Game Completion preset with no special mode on any PC. The Hunter vs Speedrunner and Death Swap descriptions are inferred from the server commands and mod names, not from mod documentation.
## One player (built), three or four players (research, not built)

The launcher and its helpers assume two players in many places, so the answers differ a lot.

- **One player: built.** The Play page has a "1 player / 2 players" selector (saved as `Players` in the preferences). With one player the launcher shows one card, one preview and one session row, gives Cemu A the whole primary display (`ScreenLayout.PlanSingle`), hides the swap, the split and the one-display-per-player option (the preference is kept for when the user goes back to two), and calls `Start-Online.ps1 -PlayerCount 1`, which skips Cemu B and waits for one "Joined server" line. Changing the mode while a session is open turns Start into "Restart session". Verified: build, unit tests (plan for several displays) and the virtual pad on the UI at 1600x900, 1280x720 and 800x1000. Not verified: a full in-game one-player start with Cemu (the scripted Cemu start was never confirmed here) and a physical pad.
- **Join a remote server: built** as the Join mode of the Online page (below).
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

## Monitor names

The Display card shows each monitor's EDID name (for example "Odyssey G95C") instead of the resolution, in the one-display-per-player layout and in the primary-display caption. The name comes from the 0xFC descriptor of the monitor's EDID in the registry; when it is missing the card falls back to "Display N". Unit test: `EdidName` in `tests\RouterTests.cs`.

## Screen rotation (built, verified on the title screen)

Each player's screen can be turned a quarter turn clockwise per click (0, 90, 180, 270). The avatar button beside the display preview shows the direction. It is only offered with two players, in both the primary-display split and the one-display-per-player layout. In two-display mode the user picks each player's display with the Display dropdowns; the split always uses the primary display.

- Rotation uses ReShade as a Vulkan implicit layer, as the old launcher did, but only for a Cemu whose player has a turned screen. Without rotation nothing is deployed and `VK_IMPLICIT_LAYER_PATH` is not set.
- `ReShade64.dll` and `ReShade64.json` are never committed. They are copied by hand into `.tools\reshade` (git-ignored). Rotation needs Vulkan; otherwise the launcher refuses to start and says why.
- `RotationMod` writes a small FX shader, a preset and a managed `ReShade.ini` into the player's Cemu folder, and removes only its own files when the rotation goes back to 0. It refuses to touch a ReShade setup it did not create.
- `Start-Online.ps1` sets `VK_IMPLICIT_LAYER_PATH` per player right before starting that player's Milk Bar launcher, so only turned players load the layer.
- For 90 and 270 degrees the game renders the swapped shape (the window area is unchanged) and the shader rotates the picture back into the window.
- Unit tests cover the plans and the file deployment. ReShade rewrites its ini on exit and drops comments, so `RotationMod` recognises its own setup by the preset path, not by a comment (the first version refused to start a second session because of this).
- **Verified (2026-10-22, session with player 1 turned 90 degrees):** ReShade 6.5.1 loaded in Cemu A only, the shader compiled, the game rendered 1266 x 1170 in a 1170 x 1266 window, and a screenshot of the title screen shows player 1's picture turned a quarter turn clockwise with no visible stretching, next to player 2's upright picture. When the displays changed during the session the launcher showed player 1 at 1440 x 5120 on the 5120 x 1440 display. The same check at 180 degrees (picture upside down, 1170 x 1266 render) and 270 degrees (1266 x 1170 render, text reads upward) looked correct, with player 2 upright both times. The turn buttons were also reached and pressed with the simulated pad cursor (`HYRULE_PAD_SCRIPT`). Not verified: the buttons with a real pad, and gameplay while turned.

## Milk Bar "Unexpected character encountered ... line 2, position 3" (cause found, 2026-10-23)

Symptom: the session never starts ("Player A did not reach the server"), Cemu is never launched, and the Milk Bar window shows a JSON parse error with an Accept button. It is independent of Cemu and of the graphics API.

- Milk Bar edits the actor packs through `Resources\aapmLib.exe`, which exchanges data with the launcher in one shared file, `%APPDATA%\BOTWM\Temp\AampTemp.txt`. When `aapmLib` crashes, the file keeps the input list of objects and Milk Bar fails to read it back as strings (the JSON error above).
- The crash was `KeyError: ModelData`. The first 32 model lists in the merged tree (GameROMPlayer's list and animation list, `Armor_Default_Extra_00/01`, `PauseMenuPlayer` and 26 `Armor_*` packs) held ActorLink data of the 32 `Jugador*` actors instead of model lists. Those packs were written that way by a successful run earlier on 2026-10-22. The ActorLink data comes from Milk Bar's `LinkTarget` pass over the 32 Jugador packs, read back into the next pass: the shared temp file was read by a pass it did not belong to. Why that happened once is not proven (two Milk Bar processes overlapping on the shared file is the likely reason). Every later run then read the corrupt lists and crashed.
- Repair: the 32 entries were restored from the update's own packs (`TitleBG.pack` is a plain SARC, the actor packs are Yaz0). After the repair `aapmLib.exe` processed all 419 inputs with exit code 0. The corrupt files were saved outside the tree before editing. Not verified: a full session start after the repair.
- Check for it: any `.bmodellist` or `.baslist` in `merged\content\Actor\Pack\Armor_*` or `PauseMenuPlayer` that contains the strings `Jugador` or `MultiplayerAI` is corrupt.
- Pitfall when scripting this with `oead`: a `Sarc` object must stay alive while its file data is used, or the data turns into garbage at random.

## Install folder (path guard)

- Long paths break BCML, Cemu and the Milk Bar launcher, so the launcher checks its folder before it starts (`src/Launcher/PathGuard.cs`; the same limit is in `scripts/Path-Guard.ps1`, which `Start-Online.ps1` runs, and `installer/HyruleOnline.iss`, which warns on the folder page). `tests/StartupTests.cs` fails if the three numbers differ.
- The limit comes from the deepest file known under the root, measured on the clean install: 166 characters (a graphic pack of another game that Cemu downloads, `.tools\instances\cemuA\graphicPacks\downloadedGraphicPacks\XenobladeChroniclesX\Mods\HudNoBattleUI\...`). The deepest BOTW file is 153 (a graphic pack workaround); the BCML content and `mlc01` stay under 105. Root limit = 260 - 1 (the NUL that MAX_PATH counts) - 166 - 10 (headroom for graphic packs added later) = 83 characters. `C:\HyruleOnline` (the installer default) is 15.
- Too long and network (UNC or mapped drive) folders stop the launcher. Program Files, OneDrive (the environment folders or any path part starting with `OneDrive`) and non-ASCII characters only warn: nothing here records a non-ASCII path breaking BCML (Python 3.8) or Cemu, so that rule stays a warning until it is observed.

## Shared shader cache (built, off by default, not verified in game)

Each instance has its own `shaderCache` (about 1.2 GB: `transferable`, `precompiled`, `driver\vk` or `driver\nvidia`). Cemu 1.26 appends new shaders to the transferable cache and rewrites the Vulkan pipeline cache while the game runs, and both instances run the same game at once. A live shared folder (a junction from cemuB to cemuA) would let two processes write the same files, which can interleave records or tear a cache file, and this cannot be tested here. Common Cemu advice (not checked against its source) is to build a cache in one instance, close Cemu and copy it, never to write one cache from two instances. So the launcher never shares a live folder.

The option "Share shader cache between players (experimental)" (`SharedShaderCache` in `preferences.xml`) does the safe part only. When a session starts and no Cemu runs, if cemuB's cache is missing or empty and cemuA's has files, `ShaderCache.Prepare` copies cemuA's cache to `shaderCache.seed` next to cemuB's, checks the size, then renames it. It needs the size plus 256 MB of free disk, otherwise it refuses. A cache that already exists is never overwritten, so repeated starts change nothing. Trade-off: this saves the separate download or copy for the second instance, not disk space (two real copies remain). Sharing disk space would need the junction and an accepted corruption risk, so it was looked at and not built: nothing here can show that two live Cemu processes keep a shared cache intact, and a corrupt cache costs more than the 1.2 GB it saves.

The launcher stays responsive during the first copy: `ShaderCache.Prepare` runs on a worker thread, the status line says "Copying the shader cache for player 2 (first time only)...", the controls are disabled until it ends, and the session then continues as before (`PrepareShaderCache` and `BeginSession` in `Launcher.cs`). A launcher closed in the middle leaves a `shaderCache.seed` folder that the next start replaces.

Reversible: a junction found on either cache (for example made by hand) is first copied to a real folder, only then is the link removed (the target is untouched), whatever the flag says. Any error (locked file, no space, no permission) leaves both instances independent, removes the half-made copy, shows "Shared shader cache is off for this session" in the status line and is written to `shader-cache.log` next to the launcher (flag, paths, sizes, free disk, action, error message, fallback). Unit tests use temp folders only (`tests\ShaderCacheTests.cs`). Not verified: a real session with the option on.

## Menu shortcut from inside the game (built, verified with virtual pads)

- Default chord: Start + LT held 1.5 s on any controller, configurable under Menu controls (presets Start + LT + RT and L3 + R3, a capture mode for 2 to 4 inputs, or Off). Start + Back is refused because Moonlight's quit chord is Start + Back + LB + RB. The Guide button is not mapped by SDL here.
- The router detects the chord per slot with trigger hysteresis, hides the chord's inputs from the game while it is held, and gives a short rumble pulse when it fires. A single press of the first chord button still reaches the game (accepted).
- While the launcher is the active window and a game runs, the games see a neutral pad, so menu presses never reach Cemu.
- Windows blocks background focus changes. `WindowPlacer.Focus` sends a synthetic Alt tap, flips the window topmost and calls `SetForegroundWindow`. When going back, the target must be focused before the launcher is minimized, or the focus falls to the desktop.
- B on the first screen of the launcher returns to that player's Cemu, and so does the "Return to game" button. The same chord toggles open and closed.
- Start still means "Restart session" only when settings changed; a TapGuard fires it on release and only if nothing else was held, so the chord cannot trigger it.
- Live check (`shortcut-test`, 26 of 26 passed): chord hidden from the game, no restart, same Cemu processes, neutral input while open, return to the right Cemu for each player, toggle, short press ignored, capture, refusal of Start + Back, cancel, and the new chord replacing the old one.
- Not verified: a physical pad through Steam Input / Moonlight. BOTW uses ZL for lock-on and shield, so Start + LT may collide with play; Start + LT + RT or L3 + R3 are the fallbacks.

## Single player picks the main controller (built, verified with virtual pads)

- A controller that switches to 1 player (Select, or its cursor on the switch) becomes Player 1; `Router.Promote` uses the same swap as Assign. The routes from before are saved and `Router.SetRoutes` puts them back when multiplayer returns. A mouse click changes no routes. The saved routes live in memory only, so a launcher that starts already in single player has nothing to restore.
- Live check (`mode-test`, no game, two virtual pads): pad 2 picks single and becomes Player 1 (routes 3,2); going back restores 2,3; the pad that is already Player 1 moves nothing; mouse clicks change nothing.

## Responsive layout and simpler text (built, checked with screenshots)

- Cause of the cut-off text: the page area is only about 680 design units tall (the 1030 shell minus the header and footer) and the Graphics columns needed about 1100. Nothing scrolled, so the Menu controls card, the Recommended card and the Apply button fell under the status bar.
- Changes: the Play, Graphics and Multiplayer pages sit in a `ScrollViewer` (safety net at any size); the Graphics page is compact (shared `SectionTitle`, `FieldLabel` and `CompactCombo` styles, two explanatory notes folded into tooltips); the header and status bar are tighter; no text is below 13. Help is now 10 short sections in a scrolling panel (titles in gold) instead of one long block; D-pad up/down scrolls it. A pad cursor calls `BringIntoView` so the focused control never hides off-screen.
- Checked with `HYRULE_LAUNCHER_SIZE` at 1280x720, 1152x720, 1680x720, 3413x960, 960x720 and 760x900 in English, plus 1280x720, 3413x960 and 760x900 in Spanish and French with two players. The translation audit and `mode-test` (virtual pads, no game) pass.
- Not verified: a physical pad scrolling Help, and the Start + LT shortcut after this change (`shortcut-test` starts the game).

## Pad fixes: filter combos and shortcut cancel (built, checked with virtual pads)

- Down from the wide Graphics API combo skipped the two half-width filter combos and landed on VSync. `Spatial.Pick` penalised the distance between box centres, so a full-width box won. It now penalises how far the source centre is from the candidate's span (zero when the candidate is below it), with the centre distance as a small tie-break. A regression test covers a wide box over two narrow boxes.
- While a new menu shortcut is being captured, the pad is read raw and cannot press Cancel. Tapping the back button alone (B, or Circle) now ends the capture; the hint says so. Mouse and the 10 second timeout still work.
- Checked with a pad script and screenshots: Down from the API combo reaches Upscale, Right reaches Downscale, both open and select. Not verified: capture-cancel with a physical pad.

## Single player: Player 1 profile reachable by pad (built, checked with virtual pads)

- In single player a pad that was not routed to Player 1 got the shared white cursor, and `PadCursors.Candidates` hid Player 1's card from it whenever the green cursor was active ("taken" by the other player). The name box and the avatar button could not be reached.
- Now, with one player, every pad drives the Player 1 cursor, and the "taken" rule only applies in multiplayer. Switching back restores the earlier routes as before, so the two-player cursors behave as they did.
- Checked with a moves-only pad script and screenshots: the cursor reaches the name box and the avatar button in single player. Not verified: a physical pad, and typing a name still needs a keyboard.

## Session fixes: lone pad, Return to game, silent Exit, key guard

- **Lone pad is Player 1:** `Router.Reconcile` now swaps the two slots when Player 1's slot is empty or disconnected and Player 2's pad is connected. A single connected pad is always Player 1; the reserved away-slot moves to Player 2 and a returning pad gets its slot back. Covered by new tests in `RouterTests`.
- **Return to game by pad:** the footer button is now in `PadCursors.ChromeNames`, so the pad cursor can reach and accept it. Not checked with a running game (that needs the game started).
- **Exit:** no confirmation dialog. Exit, the window close button and Alt+F4 stop any session the launcher started (`Stop-Online.ps1`) and close.
- **D-pad opens an external window / Start closes the launcher (Ally client session only):** the cause is not confirmed and could not be reproduced. The working hypothesis is that the client's controller mapper also sends keyboard keys (Escape, Enter, arrows, the Menu key). As a defence, navigation and Escape/Enter/Space/arrow/Menu/F10 keys that arrive within 400 ms of pad activity are ignored, and the default text-box context menu is disabled. Ignored keys, Exit and Escape closes are logged to `input-trace.log` next to the preferences, so a repeat can be diagnosed. Not verified in the Ally session.

## Cemu overlay options, and why the FPS target needs a restart

- Cemu 1.26.2f has an on-screen overlay in `settings.xml` (`Graphic\Overlay`: FPS, DrawCalls, CPUUsage, CPUPerCoreUsage, RAMUsage, VRAMUsage, Debug, plus position, colour and scale). The launcher now writes two of them from Graphics > Advanced (Cemu): **Show FPS** (on by default, as it was before) and **Show CPU, RAM and VRAM usage** (off by default). Both apply at the next session start; the Cemu settings file is only read when Cemu starts. Checked: build, unit tests and a screenshot of the Graphics page. Not checked: the overlay in a running game.
- The FPS target is a preset of the FPS++ graphic pack, which patches the game's code and timing when it loads. Cemu does not reload graphic packs in a running game (Cemu wiki, graphic packs pages), and Cemu 1.26 has no frame limiter of its own to change instead. So the target cannot be applied live without writing into the game's memory, which the launcher does not do. Not tested live; this is from how Cemu and the pack work.
- Added later: **Master volume** (`Audio\TVVolume`; Muted, 10, 25, 50, 75, 100%; default 50, the value used before) and **Show the "compiling shaders" notice** (`Graphic\Notification\ShaderCompiling`; on by default). Volume is one shared level, not per player, and is written to both Cemu instances (single-player uses only the first), so it covers both modes. Both apply at the next session start. The "Recommended for this PC" text block was removed; only the "Use recommended settings" button remains, and it keeps the overlay, volume and notice choices. Checked: build, unit tests, screenshots. Not checked: the sound level or the missing notice in a running game.
- Not built, candidates: overlay position and scale.
- **Two-column Advanced options and the layout test:** the Cemu toggles sit in a two-column grid so the Graphics page fits without scrolling. The canvas size and the panel placement (side by side or stacked) live in `ShellFit.cs` and `ShellLayout.cs`, shared by the launcher and by `tests\LayoutTests.cs`, which loads the real `Launcher.xaml`, applies the layout for 13 window shapes (16:9 to 32:9, 4:3, 5:4, near-square and portrait) and measures it headlessly: no scrolling on Graphics, two toggle columns, panels not overlapping, no clipped text. `HYRULE_LAYOUT_DUMP=1` prints every page's content and viewport height. Run it with `scripts\Test-Layout.ps1` (also part of `Test.ps1`). This replaces screenshots for layout regressions; it does not check colours, fonts on a real GPU or the physical screen.
- **Pad reachability test and state dump:** the same test also checks pad navigation. For every window shape and page, with and without the Return to game button, it runs the launcher's own `Spatial.Next` (`Navigation.cs`) from every control in the four D-pad directions and asserts that every control can reach every other one, and that no Button, ComboBox, Slider or editable TextBox is left out of the pad's target list (`ShellLayout.Controls`). It does not cover ComboBox dropdown navigation, Cancel/Back, or the physical pad. For scripted pad runs, `HYRULE_STATE_DUMP=<file>` (with `HYRULE_PAD_SCRIPT`) appends one line per script step after the cursors settle: `step | page | p1 | p2 | any | hint`. Read that file instead of taking screenshots to see where each cursor landed.
## How setup works (first run)

A fresh unzip or the thin installer holds only the launcher, the input router/bridge and `scripts\`. Everything else is found or fetched on first run.

- **Manifest:** `scripts\components.json` lists each download (name, version, URL, SHA-256, size, extract targets, marker files, what it is required for) and the requirements (4 GiB free disk, Windows 10 1809+, .NET Framework 4.8, VC++ runtime, Vulkan, update v208, DLC v80). The launcher (`Setup.cs`) and `Install-Components.ps1` read the same file. The Milk Bar release and the graphic-pack zip publish no digests, so their hashes come from the files downloaded on 2026-10-22; if upstream replaces them the check fails closed instead of installing something unknown.
- **Preflight (`Preflight.Run`):** returns OK / MISSING / BROKEN per item with a fix hint: each component's marker files (hash checked), router, merged mod, base game, update and DLC in both instances, the save baseline, the system requirements and the install folder. `Preflight.RootPathHook` is where the path guard plugs in. The wizard only appears when a required item is not OK, so a complete install shows exactly what it always did.
- **`scripts\Install-Components.ps1`:** no admin rights, nothing outside the install folder. `-Check` (alias `-WhatIf`) only prints status. Downloads go to a cache (`.artifacts\downloads`) with SHA-256 verification, resume through a `.part` file and are deleted on a mismatch. Output lines are `SETUP|STATUS|id|state|detail`, `SETUP|PROGRESS|all|0-100|text`, `SETUP|STEP|id|start/done/skipped/failed|text` and a final `SETUP|DONE|all|OK/PENDING/FAILED|text`; exit code 0 means everything is in place. Python 3.8.10 is installed per user into `.tools\python38` (no PATH change, no Store Python; the installer still registers an uninstall entry under HKCU) and BCML 3.10.8 is installed into it with `pip --require-hashes` from `scripts\bcml-requirements.txt`.
- **Game files are never downloaded.** "Locate my game files" scans a folder chosen by the user, accepts the update only at v208 and the DLC only at v80 (by `meta.xml` title id and version; the update's `meta.xml` carries the base game's id, so it is told apart by its `0005000e` folder or a non-zero version), then `SetupLayout.Apply` links `mlc01\usr\title\0005000e\101c9400` and `0005000c\101c9400` into both instances with junctions, copies the save baseline once (never over existing progress), and writes seed settings and BCML settings.
- **Not automated (manual boundary):** the BCML merge and the Cemu graphic-pack export. Install `MilkBarLauncher.bnp` into BCML with `LOCALAPPDATA=.tools\localappdata` (never `refresh_master_export`, `link_master_mod` or the GUI), copy `mods\9999_BCML` into `merged`, link or copy the export to each instance's `graphicPacks\BreathOfTheWild_BCML`, run `scripts\Patch-Mod.ps1`, then run the layout step again. Until the merged mod exists the preflight keeps reporting it as missing.
- **Not verified:** the real Python and BCML installs, the multi-gigabyte downloads end to end, Cemu accepting the seeded `settings.xml`, and the wizard on screen or with a controller. The automated tests use temp folders, fake archives and a local HTTP server.

## Open questions

0. Pipe limit: the launcher creates `languageConnectionPipe` with max 2 instances, which fits exactly two launchers on one PC. The DLL pipe name is fixed, and in the two-instance run both DLLs connected without a visible mix-up, but this was not tested on purpose.
1. ~~Where are binaries?~~ Answered: GitHub release 2.0.1 (also the `.bnp` in the repo, 15.8 MB).
2. Does the pattern scan work on our Cemu 1.26.2 copy, and does it conflict with the `BOTWMultiplayer` graphic pack?
3. Which parts of our launcher (display policy, Apollo/Artemis, controller routing, Steam shortcut) can drive two Cemu processes.
4. Local per-instance pipe name and port handling.
- **Join with two local players:** Join no longer forces one player and the Switch to local multiplayer button stays enabled there, so both Cemu windows connect to the friend's server (Start-Online.ps1 -RemoteServer already started one or two). The server's player limit is the constant ServerData.PLAYERLIMIT = 32, read by reflection, so two local players plus a SteamOS client fit. **Not verified:** a real three player session (two on this PC and one from the Ally) in game.
