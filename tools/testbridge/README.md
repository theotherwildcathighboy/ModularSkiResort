# SkiTestBridge: a dev-only helper for driving in-game tests

This is a separate local mod. It is not part of Modular Ski Resort. It never goes into the publish upload (the project refuses
`Publish`). `SkiResortMod.dll` does not change, so a driven test runs the build that ships. Without `--uiDeveloperMode` on the
game's command line, it registers nothing.

It adds what the gameface MCP lacks: real mouse and keyboard input at world positions, camera control and ECS state as JSON.

## Install

With the game closed, from this folder:

```powershell
dotnet build -c Release
```

That deploys to `<Mods>\SkiTestBridge`. Set the Steam launch options to `--developerMode --uiDeveloperMode`. The mod log is
`SkiTestBridge.log` in the game's Logs folder. Its first line says Active or Inert. To remove the mod, delete `<Mods>\SkiTestBridge`.

### Starting the game without the launcher

Do this only where the owner has allowed a restart. The Steam URL opens the Paradox Launcher. A direct start skips the launcher,
but it needs the Steam variables. Without them, the game logs a NullReference in `AssetDatabase.PopulateFromDataSource` in a loop
on a black screen.

```powershell
$env:SteamAppId="949230"; $env:SteamGameId="949230"; $env:SteamOverlayGameId="949230"; $g="C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II"; Start-Process -FilePath "$g\Cities2.exe" -WorkingDirectory $g -ArgumentList "--developerMode","--uiDeveloperMode"
```

Port 9444 opens after 1 to 2 minutes. The window then has no focus, so input does not reach it (WScript `AppActivate` returned False).
To bring the window to the front, run:

```powershell
Add-Type @"
using System;using System.Runtime.InteropServices;
public class W{[DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);[DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int c);[DllImport("user32.dll")]public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);}
"@; $h=(Get-Process Cities2).MainWindowHandle; [W]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [W]::keybd_event(0x12,0,2,[UIntPtr]::Zero); [W]::ShowWindow($h,9) | Out-Null; [W]::SetForegroundWindow($h)
```

## Keep tests fast

Do not work in the order screenshot, move, screenshot. One `game_eval` should run a whole test:

1. **Aim with world coordinates**, not by looking at pixels. A `move` takes `wx` / `wz` and projects them on the frame it runs.
2. **Batch.** A `seq` runs camera, moves, clicks, keys and checks in one call, frame-accurately.
3. **Wait on results.** `waitFor` watches an ECS query, so there are no guessed delays and no polling calls.
4. **Check data.** `hover` says what the tool hit. `dump` reads components. The hub bindings and DOM give the UI text.
5. **Screenshot only for what data cannot say**, at the end of a sequence (Alt+F1).

## Calling it

Through the gameface MCP, `game_eval` with `awaitPromise`:

```js
engine.call("skitest.cmd", JSON.stringify({ op: "status" }))
```

Every reply is a JSON string with `ok`, and `error` when `ok` is false.

| op | Arguments | Result |
|---|---|---|
| `status` | | game mode, `ready` (see "Wait for ready"), focus, screen, cursor, tool, camera, sim speed, date, money, climate, sequence |
| `ready` | | the readiness gate alone: `ready`, `quietFor`, `parts` (state, mode, mods, vt, tasks, ui), `pageLoads`, `sinceMode`, `sincePageLoad` |
| `settings` | optional `textScale`, `width` + `height`, `displayMode` (`Fullscreen`, `FullscreenWindow`, `Window`) | sets them through the options menu's setters and Apply, without its confirmation dialog. The page applies a text scale only through its own `--fontScale` / `--fontScaleChange` (vanilla `FR(e)`: `e` and `e - 1`): set those two CSS vars on `document.documentElement` in the same test. The game saves both into `Settings.coc`: back it up and restore it with the game closed |
| `hover` | | cursor (Windows and game), `mouseOverUI`, active tool and prefab, what the tool raycast hit |
| `photo` | `name` (a bare file name), optional `dir` (an absolute folder, created if missing), `ui` | hides the UI and saves a natural-color 3840 x 2160 PNG with Photo Mode's call, HDR on or off, to `<dir>\<name>.png` (default dir: `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Screenshots`). It replies with `path`, written a frame later. Point `dir` at the folder the test reports from. Use this op to take shots |
| `pm` | `on`, `cam`: `{x, y, z, at: [x, y, z]}` or `yaw`, `pitch`; `set`: `{id: number or null}`; `list`, `group`, `reset` | Photo Mode through the panel's own properties. `list` gives ids and ranges (`focalLength`, `lensShift`, `iso`, `Roll`, `Time of Day`, ...). A null disables an override. Leave Photo Mode with `on:false` before you use the `camera` op |
| `project` | `wx`, `wz`, optional `wy` | client pixel. `safe` is false when off screen or in the edge-scroll band |
| `camera` | optional `wx` + `wz`, `zoom`, `yaw`, `tilt` | camera state. It is applied on the next frames, so follow with a `settle` step |
| `sim` | optional `speed` (0 pauses) | speed and frame index |
| `time` | `minutes` | advances the game clock (the dev menu's `DebugAdvanceTime`). Citizens age across the jump with no sim frames to serve them: repeated jumps accumulate deaths and hearse icons, so never judge city health after them |
| `money` | optional `add`, `unlimited` | new balance |
| `tool` | `prefab`, optional `mode` (`Straight`, `SimpleCurve`, `ComplexCurve`, `Continuous`, `Grid`, `Replace`, `Point`) | active tool. No `prefab` returns to the default tool. For setup: a test of the hub or toolbar clicks the real buttons |
| `prefabs` | `match` (part of a name) | matching prefab names and classes |
| `unlock`, `milestone`, `demand` | | the dev menu's Unlock all, Next MS and Unlimited demands |
| `tiles` | | unlocks every map tile (`MapTilePurchaseSystem.UnlockMapTiles`) |
| `heights` | `x0`, `z0`, `x1`, `z1`, `step` | terrain height grid, `min`, `max`. Use it to pick test sites by slope without a screenshot |
| `errors` | optional `clear` | game errors and exceptions since the last clear, with `serial`. It includes mods whose errors never reach a dialog |
| `climate` | any of `date`, `temperature`, `precipitation`, `cloudiness` | a number overrides the value. `null` returns it to the simulation |
| `dump` | `all`, optional `none`, `show`, `limit` (default 50) | match count and rows with prefab name and the `show` components |
| `entity` | `id` as `"index:version"`, optional `only` | component names, with values for `only` (all when absent) |
| `perf` | `action`: `start` (`label`), `read`, `stop`, `memory`, `modSystems` (optional `enabled`, `match` regex), `systemCost` (`samples`), `systemCostRead` | frame times (fps, mean, p50, p95, p99, max), sim frames per second and GC counts of the window. Unity memory, entity count, vsync. Switches the ski mod's systems (or those matching) for an A/B on a throwaway save. Times one extra run of each mod system, also on a throwaway save |
| `comp` | `id`, optional `remove`, `add` (component names), `eff` (`{factor: value}`), `clearIcons`, `updated` | edits one entity in that order and replies with `util` |
| `util` | `id` | a building's consumers, road edge, efficiency factors, icons by prefab name, jobs |
| `offgrid` | optional `add`, `remove` (ids), `clear`, `clearEvents` | prototype of switching road-only services off and on with the road edge. It lists the watched buildings and every switch with its sim frame |
| `prefabFlags` | `prefab`, optional `clear`, `set` (`BuildingFlags` names) | edits a prefab's `BuildingData.m_Flags` for this run of the game |
| `autosave` | optional `on` | switches the autosave system for this run of the game, so a test on a save does not prune real autosaves |
| `saveTarget` | optional `name` (`Local`, or a name from `available`) | the target the pause menu's Save Game writes to (UserState.lastCloudTarget). It replies with `previous`, `target`, `available`. The save helpers use it and restore `previous` after a save |
| `u1probe` | `on`, `prefab` (a line prefab), `tilt` (deg), `frames` (log lines) | lift alignment risk U1: tilts the towers of that line when they update, at Modification3 and again after AlignSystem, and logs `[U1]` lines (Align reset, lane ends vs the tilted anchors, the tilt at +1 / +2 / +30 / +300 frames). Stop the probe after the test: it tilts every tower of the prefab that updates |
| `bone` | `id`, optional `mesh` (sub-mesh index), `bone`, `rotX`, `rotY` (deg) | lists a skinned object's bones (type, rest and current pose). With `bone` and a rotation, it writes that bone as rest x RotateY x RotateX and flags the skeleton for upload, as our own bone writers will. Bones exist only near the camera |
| `mprobe` | `action`: `tag` (`id`, `serial`, `provider`), `untag`, `list`, `trace` (`id`, `p`, `write`), `read`, `stop` | merge-asset P0 probe: tags a building with `tools/testbridge-mergetag`'s saved component. `trace` writes round(sqrt(V² + P²)) right after vanilla AttractionSystem and samples the value every frame |
| `trains` | `x`, `z`, optional `r` (default 400), `maxDeg` | gate G8 of the pitched sheave trains: per line tower with a `Train` bone near (x, z), the pitch the bone shows (relative to rest, nose up along +z, and null when far from the camera) against the analytic hanging-lane end slopes of the rope lanes on its anchors, and the worst difference |
| `campath` | `seconds`, `keys`, optional `mode` (`pm`, `game`), `ease`, `props`, `follow`, `fps`, `snap` | an eased camera move on a spline, as a one-step sequence. See "Filming" |
| `ui` | `hidden`, optional `seconds` (default 60, at most 900) | the whole UI view off or on. It comes back by itself after `seconds`, and when the sequence that hid it ends |
| `terrain` | `action`: `begin` (`prefab`, `size` 1 to 10, `strength` 0.35 or less, optional `height`, `wx`, `wz`, `r`, `snapDir`, `log`, `note`), `end`, `cancel`, `undo` (`file`, optional `log`), `read` | a small terrain brush edit with a bit-exact snapshot before and after, an undo and a log row. See "Filming" |
| `wind` | optional `x`, `y`, `force`; optional `wx`, `wz` | sets `WindSimulationSystem.constantWind` (default 0.275, 0.275). With `force`, it also writes that velocity into every cell of the wind simulation. Both are saved values. It reads the wind map at a point. See "Filming" |
| `fps` | optional `target` (-1 = no cap, or 24 to 240) | `Application.targetFrameRate` for this run of the game. Not a game setting, not saved |
| `seq` | `steps` | starts a sequence |
| `seqStatus` | | `done`, `step`, `error`, `results` |
| `abort` | | stops the sequence and releases all held input |

Component names may be short when they match one type (`SkiRun`), else full (`Game.Objects.Transform`). An ambiguous name returns the
candidates. `dump` leaves out `Deleted` entities unless `none` is given. Pass `none: []` to include them.

### Save and load

Use the pause menu with real clicks.

To save:

1. Press Esc.
2. Click Save Game.
3. Click the name field.
4. Press `ctrl+a`.
5. Type the name, one `key` step per letter.
6. Click Save.

Then to load:

1. Click Load Game.
2. Click the save.
3. Click Load Game.
4. Click Yes.

`__save` / `__saveFromPanel` (helpers.js) write to the local `Saves` folder. They set the target to `Local` with `saveTarget`,
and they restore the player's own target after the click. A save in Steam Cloud could not be read back after a direct launch
(IOException in `SteamCloudStream.LoadCache`, 2026-11-06), so bridge saves stay local.

Do not call `engine.trigger("menu.saveGame", name)`. If the save panel is not open, the preview texture is null. Then the game
throws a `NullReferenceException` and shows its error dialog (measured, see `MenuUISystem.cs:896-902` and `GameManager.cs:947`).

### Wait for ready

After a launch, a save load or a "return to menu", make no bridge or `game_eval` call until the bridge logs READY:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\testbridge\wait-ready.ps1
```

After a save load, pass `-Since` with the time taken just before the load click. The script reads only `SkiTestBridge.log` and
sends nothing to the game. Exit 0 = ready, 1 = timeout, 2 = the game is not running or exited.

READY (in the log, in `status.ready` and in the `ready` op) means every part below has held for 15 s with nothing changing:

- `GameManager.state` is WorldReady, and the mode is MainMenu, Game or Editor.
- The mod manager is initialized and Paradox Mods is registered. The code-mod, loaded and enabled counts stay the same.
- Virtual texturing has no materials left to load and no duplicates to process.
- No TaskManager task is running and no `SaveLoadGame` job is queued (a `Ping` job comes and goes all the time and is ignored).
- The UI view takes bindings, and no page load has finished in the last 15 s.
- The prefab count stays the same.

While the gate is closed, the log gets a WAITING line every 30 s that names what holds it.

Why: with `--uiDeveloperMode`, Gameface live reload is on (`UIManager` `liveReload = developerMode`) unless the FallbackSettings section
below turns it off (the default for bridge runs since round 624). About 6 to 10 s after every
mode change, a "media" reload in `UILiveReload` (`Reloading media 0` in UI.log) loads `reload.html` and then `index.html` again,
so every page helper is lost. The 2026-11-08 silent exits (access violation 0xc0000005 at `cohtml_Unity3DPlugin.dll+0xc410`, no
crash dialog) each came within that window: two UI.logs end on the `Reloading media 0` line, and a third ends in the middle of a page
load. The cause is UNVERIFIED. It is a correlation, not a proof, and READY does not prove that a call is safe.

Measured on 2026-11-08 (83-mod playset, 4K): READY came 45.5 s after the main menu and 40.6 s after a save load. That is close
to the old "main menu + 40 s" rule, which was too short after the menu. None of the 4 launches in that pass had an exit, but
only the last one had the finished gate.

### Turning live reload off

**Bridge runs keep live reload OFF. It is the default state of this machine** (round 624). With the game closed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\testbridge\live-reload.ps1 -Status   # or -Off / -On
```

`-Off` adds this section to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\FallbackSettings.coc`, after a
backup beside it (`FallbackSettings.coc.live-reload-<time>.bak`). Only the section changes: every other byte stays, so `-Off` then
`-On` gives the file back. The script refuses to edit while Cities2 runs (exit 2).

```
UIManager Settings
{
    "liveReload": false
}
```

`UIManager` reads it through `AssetDatabase.global.LoadSettings("UIManager Settings", ...)` after its defaults (UIManager.cs:191). With
it, a launch and a save load had 0 page loads and no `Reloading media` line, and the page helpers survived the save load. READY came
43.2 s after the main menu (virtual texturing and prefabs settling) and 20.5 s after a save load (2026-11-08). **The game keeps the
section** when it rewrites the .coc files on exit (measured in round 622, icons2-pass), so it stays off across launches: do not restore
the file after a session. Turn it on (`-On`) only to see a UI file edit in a running game; deploys happen with the game closed anyway.
UNVERIFIED: whether this prevents the silent exits (0 exits in the launches since, too few to tell).

### The inline-text probe

`__probeTip(ms = 2000)` (helpers.js) is the round 622 tooltip probe as one call. Hover the row with a real `move`, then await it while the
tooltip shows. It returns the body text (U+00A0 as "⍽", icons as "[file]"), `leadHits` (a run after an inline element that starts
with an ASCII space), `glued` (a word touching an icon), `lines` (what starts each drawn line of a cohinline paragraph), `unitSplits`
(a number and its unit with an ASCII space), `keyRows` (scrollWidth / clientWidth of each icon key row), `inViewport`, the font sizes
and the mutation count over `ms`. `ok` is true when all of these are clean.

`game_eval` returns `{}` for a promise. So start the probe with `__probeTip().then(x => window.__p = x)`, return, and read
`window.__p` in a later call. Wait at least 40 frames after the `move`. A probe that starts while the balloon still settles can count one
mutation; run it again on the same open tooltip. Gameface rejects `:scope` in a selector, so the probe filters `children` instead.

### Practical limits, all measured

- **No bridge or `game_eval` call before READY** (see Wait for ready).
- **The pause menu's exit is two steps** (1.6.4f1): "Exit", then "Exit To Desktop" in the dialog (`__exitToDesktop`).
- **`game_eval` times out after 15 seconds.** Longer work keeps running in the page, but its result is lost. So start the work,
  return, and poll. Loading a small city took about 3 s.
- **DOM rects are client pixels here** (3840 x 2160 at `devicePixelRatio` 1), so `getBoundingClientRect()` gives the click point for a
  real `move`. `innerText` is empty in this UI. Read `textContent` of leaf elements.
- **Hub rows move as the hub's contents change.** Read the rect in the same call as the click. Do not reuse an old rect.
- **Esc goes one level.** It steps back in a tool, cancels a dialog, and opens the pause menu when nothing is left. Bulldozing a
  building shows a Yes / No dialog, and Esc answers No.
- **The selection panel stays closed while a toolbar menu is open.** The `tool` op with no prefab leaves the asset menu showing. A click on a building then opens nothing, and `__relBtn` finds nothing. Press Esc once first.
- **`comp` with `updated` runs too late to matter.** Commands run in UIUpdate, after the Modification phases. `PrepareCleanUpSystem` removes the tag at the end of that frame, so no Modification system sees it (`SystemOrder.cs:50-54`). For this reason, `offgrid` runs in Modification1.
- **Rotate a placement by right-dragging.** Do these steps:
  1. `down` right.
  2. `move` 250 pixels in x (half a turn, `ObjectToolSystem.cs:3554`).
  3. `up`.
  4. `move` back.
  5. `click`.
- **The object tool's raycast is offset by the object's height** (`ObjectToolSystem.cs:3190`), so `hover.hit` lands up to a few meters
  short of the target with the camera tilted, and the object lands there. Read the placed `Transform` for the true position.
- **A station buried or floating:** use Move It, Toolbox, "To Terrain Height", with no terrain edit. If the station sits past a crest, move it along its line first. A large Level brush cuts shelves. When terrain must change, brush 1 to 10 at 35 %, with the target from `engine.trigger("tool.setBrushHeight", worldY - seaLevel)`.
- **`pm` op, lighting:** "Time of Day" changes the sky, not the lamps. The lamps follow sim time (on by about 20:20). Snow gun plumes do not depend on the hour: they run when the calendar is in the gun season (normalized date 0.75 to 1.25), the run's pack is below 0.91 m and it is cold (measured 2026-11-10: 0 of 44 guns at date 0.474 with 102 cm, 37 of 44 at noon at date 0.868 after a melt). Lifts stop outside open hours. A lift with no power stops soon after it is built. To sample chair motion, pick a powered lift at sim day.
- **Close shots:** the camera keeps about 8.5 m above the terrain at any zoom, so a pivot near the ground needs tilt 40 to 60
  to stay on screen. Read `project` for the pivot at a few tilts. Keep the one nearest the screen center. The station roofs hide
  their loading areas from above.
- **Summer shots:** deep snow at 1.4 km and above does not melt in hours at 24 C. Ground below about 800 m goes bare in minutes at x8.
- Alt+F1 reaches the NVIDIA overlay. Files appear in `%USERPROFILE%\Videos\NVIDIA\Cities  Skylines II\` (two spaces), and
  `tools\jxr2png.ps1 -Path <file> -Auto -OutDir <dir>` converts a 4K shot in about 4 s.

This helper is worth defining at the start of a session. It survives until the UI view reloads:

```js
window.__st = (seq) => engine.call("skitest.cmd", JSON.stringify(seq)).then(() => new Promise(r => {
  const t0 = Date.now();
  const poll = () => engine.call("skitest.cmd", JSON.stringify({ op: "seqStatus" })).then(s => {
    const j = JSON.parse(s);
    if (j.done || Date.now() - t0 > 13000) r(s); else setTimeout(poll, 100);
  });
  poll();
}));
```

### Sequence steps

Input steps wait `after` frames afterward (default 2).

| Step | Arguments |
|---|---|
| `move` | `x`, `y` (client pixels, origin top left) or `wx`, `wz`, optional `wy`. `frames` spreads it over frames, for a drag |
| `down`, `up` | `button`: `left` (default), `right`, `middle` |
| `click` | `button`, `hold` frames (default 3) |
| `key` | `keys` such as `"ctrl+shift+l"`, `"enter"`, `"alt+f1"`. `hold` frames (default 3) |
| `keydown`, `keyup` | `key` |
| `wheel` | `delta` (120 is one notch) |
| `wait` | `frames` |
| `camera` | same as the op |
| `settle` | waits for the camera to hold still. `timeout` frames (default 600) |
| `waitFor` | a `dump` query plus `min` (default 1). After `timeout` frames (default 1200), the sequence fails |
| `hover`, `status`, `dump` | adds the reading to `results` |
| `glide` | `x`, `y` or `wx`, `wz`, optional `wy`; `seconds` (default 1), `ease` (default `inOut`). The cursor travels there in real time, eased. A world target is projected again on every frame |
| `campath` | same as the op. The sequence goes on when the move ends. `after` frames (default 0). The reply (frames, seconds, fps, `maxFrameMs`, `hitches` = frames over 25 ms) goes to `results` |
| `ui` | same as the op |
| `sim`, `time`, `money`, `climate`, `tool`, `unlock`, `milestone`, `demand`, `errors`, `pm`, `wind`, `fps`, `terrain` | same as the ops. The reply goes to `results` |

Rectangles in the area tool: the vanilla right-angle snap can pull the closing click off the first corner, so the area stays open and nothing is built (`Area.m_Flags` without `Complete`, a fifth node 0.5 m from the first). Draw test areas slightly off square.

Two guards fail a sequence instead of letting it click on blindly:

- **A new game error stops it**, since an error dialog would swallow the input. Pass `stopOnError: false` on the `seq` when a known
  error is expected, then read `errors`.
- **A press aimed at a world point fails if UI is under the cursor.** Pass `allowUI: true` on the `click` or `down` to allow it.

## Filming

Ops for recorded takes (the release trailer, DECISIONS 647). Page helpers: `shots/trailer-helpers.js`. Camera paths are kept as JSON
in `shots/`, one file per shot, so a retake after a game patch is a re-run. Recording is `obs/rec.mjs` (its own README).

### `campath`

An eased camera move on a spline. The pose is set once per rendered frame from real frame time (`Time.unscaledDeltaTime`), so
the move takes `seconds` at any frame rate. `fps` gives each frame a fixed step of 1 / fps instead (frame-locked motion for a
capture at that rate).

- `mode`: `pm` (the Photo Mode camera; the default while Photo Mode is on) or `game` (the normal camera).
- `keys`, at least two. `t` runs from 0 to 1 and rises; without `t` the keys are spread evenly.
  - `pm`: `pos: [x, y, z]` and `at: [x, y, z]` (a look-at point), or `yaw` and `pitch` in place of `at` on every key.
  - `game`: `wx`, `wz`, optional `wy` (the pivot), `zoom`, `yaw`, `tilt`. Zoom blends in log space.
- The position and the look-at point run on a Catmull-Rom spline through the keys. Two keys give a straight line.
- `ease`: `linear`, `in`, `out`, `inOut` (the default), `inOut2` (the softest start and end).
- `props` (`pm` only): `{"Time of Day": [16.2, 17.0]}` moves a Photo Mode property from the first value to the second over the
  move. Any id of `{op:"pm",list:true}` works (`PhotoModeRenderSystem.focalLength` for a zoom).
- `follow` (`pm` only): `{entity: "index:version", offset: [0, 1.5, 0], attach: false}`. The camera looks at the entity's drawn
  position (InterpolatedTransform) plus `offset`. With `attach`, the camera also moves by what the entity moved since the start
  (a tracking shot). The groomers are respawned on every load: find the id again (`__groomers()`).
- While a `pm` path runs, the Photo Mode camera takes no input and is not clamped to the terrain: keep the keys above the ground.
- The reply in `results`: `frames`, `seconds`, `fps`, `maxFrameMs` and `hitches` (frames over 25 ms). A take with a hitch is
  retaken.

Measured on 2026-11-10 (4K, the 83-mod playset, no frame cap): a 5 s Photo Mode path ran 331 frames at 66 fps with a longest
frame of 19.4 ms and no hitch. A path that moved Time of Day from 15 to 19.5 in 4 s had one frame of 33 ms. The normal-camera
path ended on its last key exactly (pivot, zoom, yaw, tilt).

### `glide`

A sequence step. The cursor travels to a point in `seconds`, eased, where `move` jumps. Use it before each `click` of a tool
shot. Measured: a 2 s glide to a world point 1,000 px away arrived within one pixel and the tool raycast hit the point.

### `ui`

`{op:"ui",hidden:true}` switches the whole UI view off (the same switch the `photo` op uses for one frame), so a recording shows
the picture alone. The page may not run while the view is off, so the bridge shows it again by itself: when the sequence that hid
it ends (also on an error or `abort`), and after `seconds` in any case. `photoMode.setOverlayHidden` is a different switch: it
hides the notification icons and the piste map, in or out of Photo Mode.

### `fps`

The game runs with no frame cap and no vsync on this machine (66 to 78 fps at 4K). `{op:"fps",target:60}` caps it for this run.
Measured with the cap: 59.7 to 59.8 fps over 6 s (`perf`).

### `terrain`

Small brush edits for staging, with the game's own terrain tool and real input. The limits are in the code: brush size 1 to 10,
strength 0.35 or less.

1. `{op:"terrain",action:"begin",prefab,size,strength,height,wx,wz,r,snapDir,log,note}` reads the base heightmap texels around the
   point (`r` meters, default size x 2 + 12) and saves them as `<snapDir>\<id>-before.json` (bit-exact, 16 bit). Then it activates
   the terraforming prefab and sets the brush. `height` is the target in world meters (Level and Slope).
   Prefabs: `Terrain Level Tool`, `Terrain Soften Tool`, `Terrain Shift Tool`, `Terrain Slope Tool`.
2. The stroke is input: `move` to the point, `down`, `wait` some frames, `up`. Several strokes may follow one `begin`.
3. `{op:"terrain",action:"end"}` saves `<id>-after.json`, compares, appends one row to the `log` file (position, tool, size,
   strength, target, the largest raise and lowering, changed texels, the note, the snapshot) and leaves the tool.
4. `{op:"terrain",action:"undo",file:"<id>-before.json",log}` writes the snapshot back into the base heightmap, verifies it by a
   readback (`mismatches` must be 0) and runs the game's own change notifications (the ski mod's `HeightmapWriter` recipe).

`__terr` (trailer-helpers.js) runs steps 1 to 3 for a list of points. Check every edit from a low camera before the next one.

Measured on 2026-11-10 (this map: 4,096 x 4,096 texels of 3.5 m, height step 0.0625 m):
- One Level stroke, size 10, strength 0.3, held 20 frames: 4 texels changed, +0.56 / -0.13 m. So a size 10 brush touches about
  four texels, and a pad needs a grid of strokes about 4 m apart.
- A 12 m pad: nine Level strokes of 25 frames (17 texels, +1.50 / -0.81 m), then eight Soften strokes of 15 frames at strength 0.2
  on a ring of 9 m (23 texels). The pad's center cells stand within 0.2 m of the target, and a low photo shows no shelf.
- Undo of the first stroke: 0 mismatches, and the CPU heights read the old values again.

### Move It: "To Terrain Height"

A building that sits in the ground or floats is fitted with Move It's own tool, driven with real clicks (no call into Move It):
its toolbar button (`MoveIt_Off.svg`), a click on the building, Toolbox, "To Terrain Height". `__moveItToTerrain(x, y, z)` does
these steps and returns the height before and after. Measured (Move It 0.5.16): the Swanky Summit Lodge went from y 1,094.98 to
1,098.15, the terrain height at its center, with the same entity and no game error. The building then stands at the height of its
center: on a slope its uphill side is still in the ground, and that part is a `terrain` job (Level strokes along the uphill wall).
Move It is a staging tool. Nothing in Modular Ski Resort needs it.

### `wind`

The lift carriers sway with the wind map (`WindSystem`), which the wind simulation feeds every 512 simulation frames. Setting
`constantWind` alone showed no change at a lift after one game hour (the simulation follows its boundary pressures slowly).
`force: true` writes the velocity into every cell, so the next map update shows it. Both values are saved with the city: put the
default (0.275, 0.275) back before a save, or do not save.

Measured on 2026-11-10 at the Summit Express (a detachable quad chair, tilt from plumb sampled for 6 s): with the save's own wind
(strength 0.23) the chair swung over 2.1 degrees across the line and 5.0 degrees along it. After `{op:"wind",x:0.5,y:0.5,force:true}`
the map read 0.71 within 11 s, and the chair leaned 2.5 degrees with a swing of 2.4 degrees across and 6.2 degrees along. The forced
wind fell to 0.70 in the next 8 s.

## Editor import

Launch with `--startEditor`. The first port 9444 reply comes before the Editor is ready, so wait for "Workspace" in the UI. `helpers.js` has `__rows` and `__enter` for the file browser. The controls:

- Toolbar icon (1647,1968): opens the importer.
- Project root button (3300,134).
- Assets folder button (3300,220).
- Back arrow (2947,217).
- `Select Directory`: find it with `__find`.
- Import (3370,1992).

Set Project root to the letter folder. Set Assets folder to the asset folder itself, so the file list shows only that asset. Never point at a letter folder that holds other assets. Result: `ImportedData\c030ee3ff256df91\<Name>\`, log `Logs\AssetPipeline.log`.

## Building a test city

Setup does not need to be pretty. Use `unlock`, `money` with `unlimited`, and `demand`. Then build roads with `tool` and real clicks, and zones with
`tool` and a real drag. `prefabs` finds the exact names. `status.bridgeBuild` shows when the deployed bridge was built.

Example: fly to a spot, click it, wait for an entity to appear, read it back.

```js
engine.call("skitest.cmd", JSON.stringify({ op: "seq", steps: [
  { op: "camera", wx: 120, wz: -340, zoom: 300, tilt: 50 },
  { op: "settle" },
  { op: "move", wx: 120, wz: -340 },
  { op: "hover" },
  { op: "click" },
  { op: "waitFor", all: ["Game.Objects.Transform", "SkiResortMod.Systems.SomeComponent"], timeout: 600 },
  { op: "dump", all: ["SkiResortMod.Systems.SomeComponent"], show: ["Game.Objects.Transform"] },
  { op: "key", keys: "alt+f1" }
] }))
```

Then poll `seqStatus` until `done`.

## Rules of use

- **Input needs focus.** Every input step checks that the game is the foreground window. If it is not, the step fails the sequence,
  so input cannot land in another app. First bring the game to the front from outside, with the `SetForegroundWindow` recipe above.
  Never use `open_application` from computer use: it starts a second game. The game loses focus whenever the Claude desktop app
  comes to the front. Check `status.focused`.
- **A dead corner can swallow clicks.** In a large playset (83 code mods, 2026-11-07), synthetic clicks in the top-right corner
  (x >= ~3200, y < ~350 at 3840 x 2160) never reached the UI. This includes vanilla buttons, so the cause is the environment, not our
  panel. There, dispatch `mousedown`, `mouseup` and `click` MouseEvents on the element through the UI eval instead (Gameface has no
  `Element.click`).
- **Left-hand traffic needs a new city** (a loaded save keeps its own setting). The New Game toggle persists in `UserState.coc`, so
  restore it afterward, with the game closed.
- **It takes the real mouse while a sequence runs.** Nobody else can use the PC during input steps. Reads never touch the mouse.
- Sequences release held buttons and keys when they end, fail or abort.
- To measure performance, use `perf`, never `dump`: `dump` builds a query and reflects on every call. For an A/B, use the same camera
  for both halves, run nothing else heavy, and alternate the halves so city drift averages out.
