# SkiTestBridge: a dev-only helper for driving in-game tests

A separate local mod, not part of Modular Ski Resort. It never goes into the publish upload (the project refuses `Publish`), and
`SkiResortMod.dll` does not change, so a driven test runs the build that ships. Without `--uiDeveloperMode` on the game's command
line it registers nothing.

It adds what the gameface MCP lacks: real mouse and keyboard input at world positions, camera control and ECS state as JSON.

## Install

With the game closed, from this folder:

```powershell
dotnet build -c Release
```

That deploys to `<Mods>\SkiTestBridge`. Steam launch options: `--developerMode --uiDeveloperMode`. The mod log is
`SkiTestBridge.log` in the game's Logs folder, and its first line says Active or Inert. To remove it, delete `<Mods>\SkiTestBridge`.

### Starting the game without the launcher

Only where the owner has allowed a restart. The Steam URL opens the Paradox Launcher. A direct start skips it, but needs the Steam
variables: without them the game logs a NullReference in `AssetDatabase.PopulateFromDataSource` in a loop on a black screen.

```powershell
$env:SteamAppId="949230"; $env:SteamGameId="949230"; $env:SteamOverlayGameId="949230"; $g="C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II"; Start-Process -FilePath "$g\Cities2.exe" -WorkingDirectory $g -ArgumentList "--developerMode","--uiDeveloperMode"
```

Port 9444 opens after 1 to 2 minutes. The window then has no focus, so input does not reach it (WScript `AppActivate` returned False). Bring it forward:

```powershell
Add-Type @"
using System;using System.Runtime.InteropServices;
public class W{[DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);[DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int c);[DllImport("user32.dll")]public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);}
"@; $h=(Get-Process Cities2).MainWindowHandle; [W]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [W]::keybd_event(0x12,0,2,[UIntPtr]::Zero); [W]::ShowWindow($h,9) | Out-Null; [W]::SetForegroundWindow($h)
```

## Keep tests fast

Do not work screenshot, move, screenshot. One `game_eval` should run a whole test:

1. **Aim with world coordinates**, not by looking at pixels. A `move` takes `wx` / `wz` and projects them on the frame it runs.
2. **Batch.** A `seq` runs camera, moves, clicks, keys and checks in one call, frame-accurately.
3. **Wait on results.** `waitFor` watches an ECS query, so there are no guessed delays and no polling calls.
4. **Check data.** `hover` says what the tool hit, `dump` reads components, and the hub bindings and DOM give the UI text.
5. **Screenshot only for what data cannot say**, at the end of a sequence (Alt+F1).

## Calling it

Through the gameface MCP, `game_eval` with `awaitPromise`:

```js
engine.call("skitest.cmd", JSON.stringify({ op: "status" }))
```

Every reply is a JSON string with `ok`, and `error` when `ok` is false.

| op | Arguments | Result |
|---|---|---|
| `status` | | game mode, focus, screen, cursor, tool, camera, sim speed, date, money, climate, sequence |
| `hover` | | cursor (Windows and game), `mouseOverUI`, active tool and prefab, what the tool raycast hit |
| `photo` | `name`, optional `ui` | hides the UI and saves a natural-color 3840 x 2160 PNG with Photo Mode's call, HDR on or off, to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Screenshots\<name>.png`; the way to take shots |
| `pm` | `on`, `cam`: `{x, y, z, at: [x, y, z]}` or `yaw`, `pitch`; `set`: `{id: number or null}`; `list`, `group`, `reset` | Photo Mode through the panel's own properties. `list` gives ids and ranges (`focalLength`, `lensShift`, `iso`, `Roll`, `Time of Day`, ...); null turns an override off. Leave with `on:false` before using the `camera` op |
| `project` | `wx`, `wz`, optional `wy` | client pixel; `safe` is false when off screen or in the edge-scroll band |
| `camera` | optional `wx` + `wz`, `zoom`, `yaw`, `tilt` | camera state; applied on the next frames, so follow with a `settle` step |
| `sim` | optional `speed` (0 pauses) | speed and frame index |
| `time` | `minutes` | advances the game clock (the dev menu's `DebugAdvanceTime`). Citizens age across the jump with no sim frames to serve them: repeated jumps pile up deaths and hearse icons, so never judge city health after them |
| `money` | optional `add`, `unlimited` | new balance |
| `tool` | `prefab`, optional `mode` (`Straight`, `SimpleCurve`, `ComplexCurve`, `Continuous`, `Grid`, `Replace`, `Point`) | active tool; no `prefab` returns to the default tool. For setup: a test of the hub or toolbar clicks the real buttons |
| `prefabs` | `match` (part of a name) | matching prefab names and classes |
| `unlock`, `milestone`, `demand` | | the dev menu's Unlock all, Next MS and Unlimited demands |
| `tiles` | | unlocks every map tile (`MapTilePurchaseSystem.UnlockMapTiles`) |
| `heights` | `x0`, `z0`, `x1`, `z1`, `step` | terrain height grid, `min`, `max`; picks test sites by slope without a screenshot |
| `errors` | optional `clear` | game errors and exceptions since the last clear, with `serial`; includes mods whose errors never reach a dialog |
| `climate` | any of `date`, `temperature`, `precipitation`, `cloudiness` | a number overrides, `null` hands back to the simulation |
| `dump` | `all`, optional `none`, `show`, `limit` (default 50) | match count and rows with prefab name and the `show` components |
| `entity` | `id` as `"index:version"`, optional `only` | component names, with values for `only` (all when absent) |
| `perf` | `action`: `start` (`label`), `read`, `stop`, `memory`, `modSystems` (optional `enabled`, `match` regex), `systemCost` (`samples`), `systemCostRead` | frame times (fps, mean, p50, p95, p99, max), sim frames per second and GC counts of the window; Unity memory, entity count, vsync; switches the ski mod's systems (or those matching) for an A/B on a throwaway save; times one extra run of each mod system, also on a throwaway save |
| `comp` | `id`, optional `remove`, `add` (component names), `eff` (`{factor: value}`), `clearIcons`, `updated` | edits one entity in that order and replies with `util` |
| `util` | `id` | a building's consumers, road edge, efficiency factors, icons by prefab name, jobs |
| `offgrid` | optional `add`, `remove` (ids), `clear`, `clearEvents` | prototype of switching road-only services off and on with the road edge; lists the watched buildings and every switch with its sim frame |
| `prefabFlags` | `prefab`, optional `clear`, `set` (`BuildingFlags` names) | edits a prefab's `BuildingData.m_Flags` for this run of the game |
| `autosave` | optional `on` | switches the autosave system for this run of the game, so a test on a save does not prune real autosaves |
| `u1probe` | `on`, `prefab` (a line prefab), `tilt` (deg), `frames` (log lines) | lift alignment risk U1: tilts the towers of that line when they update, at Modification3 and again after AlignSystem, and logs `[U1]` lines (Align reset, lane ends vs the tilted anchors, the tilt at +1 / +2 / +30 / +300 frames). Turn it off after the test: it tilts every tower of the prefab that updates |
| `bone` | `id`, optional `mesh` (sub-mesh index), `bone`, `rotX`, `rotY` (deg) | lists a skinned object's bones (type, rest and current pose); with `bone` and a rotation it writes that bone as rest x RotateY x RotateX and flags the skeleton for upload, as our own bone writers will. Bones exist only near the camera |
| `trains` | `x`, `z`, optional `r` (default 400), `maxDeg` | gate G8 of the pitched sheave trains: per line tower with a `Train` bone near (x, z), the pitch the bone shows (relative to rest, nose up along +z; null when far from the camera) against the analytic hanging-lane end slopes of the rope lanes on its anchors, and the worst difference |
| `seq` | `steps` | starts a sequence |
| `seqStatus` | | `done`, `step`, `error`, `results` |
| `abort` | | stops the sequence and releases all held input |

Component names may be short when they match one type (`SkiRun`), else full (`Game.Objects.Transform`). An ambiguous name returns the
candidates. `dump` leaves out `Deleted` entities unless `none` is given. Pass `none: []` to include them.

### Save and load

Use the pause menu with real clicks: Esc, Save Game, click the name field, `ctrl+a`, type the name one `key` step per letter, Save.
Then Load Game, click the save, Load Game, Yes. Saves go to the player's last target, which may be Steam Cloud, so they show up in the
game's list rather than under `Saves`.

Do not call `engine.trigger("menu.saveGame", name)`: without the save panel open the preview texture is null, and the game throws a
`NullReferenceException` and shows its error dialog (measured; `MenuUISystem.cs:896-902`, `GameManager.cs:947`).

### Practical limits, all measured

- **`game_eval` times out after 15 seconds.** Longer work keeps running in the page, but its result is lost, so start it, return, and
  poll. Loading a small city took about 3 s.
- **DOM rects are client pixels here** (3840 x 2160 at `devicePixelRatio` 1), so `getBoundingClientRect()` gives the click point for a
  real `move`. `innerText` is empty in this UI; read `textContent` of leaf elements.
- **Hub rows move as its contents change.** Read the rect in the same call as the click rather than reusing an old one.
- **Esc goes one level.** It steps back in a tool, cancels a dialog, and opens the pause menu when nothing is left. Bulldozing a
  building shows a Yes / No dialog, and Esc answers No.
- **The selection panel stays closed while a toolbar menu is open.** The `tool` op with no prefab leaves the asset menu showing, a click on a building then opens nothing and `__relBtn` finds nothing. Press Esc once first.
- **`comp` with `updated` runs too late to matter.** Commands run in UIUpdate, after the Modification phases, and `PrepareCleanUpSystem` removes the tag at the end of that frame, so no Modification system sees it (`SystemOrder.cs:50-54`). `offgrid` runs in Modification1 for this reason.
- **Rotate a placement by right-dragging:** `down` right, `move` 250 pixels in x (half a turn, `ObjectToolSystem.cs:3554`), `up`, `move`
  back, `click`.
- **The object tool's raycast is offset by the object's height** (`ObjectToolSystem.cs:3190`), so `hover.hit` lands up to a few meters
  short of the target with the camera tilted, and the object lands there. Read the placed `Transform` for the true position.
- **A station buried or floating:** Move It, Toolbox, "To Terrain Height", with no terrain edit. Move it along its line first if it sits past a crest. A large Level brush cuts shelves; when terrain must change, brush 1 to 10 at 35 %, target from `engine.trigger("tool.setBrushHeight", worldY - seaLevel)`.
- **`pm` op, lighting:** "Time of Day" changes the sky, not the lamps; those follow sim time (on by about 20:20). Snow guns and their plumes run at sim night only. Lifts stop outside open hours, and a lift with no power stops soon after it is built: pick a powered lift at sim day to sample chair motion.
- **Close shots:** the camera keeps about 8.5 m above the terrain whatever the zoom, so a pivot near the ground needs tilt 40 to 60
  to stay on screen; read `project` for the pivot at a few tilts and keep the one nearest the screen center. The station roofs hide
  their loading areas from above.
- **Summer shots:** deep snow at 1.4 km and above does not melt in hours at 24 C; ground below about 800 m goes bare in minutes at x8.
- Alt+F1 reaches the NVIDIA overlay. Files appear in `%USERPROFILE%\Videos\NVIDIA\Cities  Skylines II\` (two spaces), and
  `tools\jxr2png.ps1 -Path <file> -Auto -OutDir <dir>` converts a 4K shot in about 4 s.

A helper worth defining at the start of a session; it survives until the UI view reloads:

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
| `move` | `x`, `y` (client pixels, origin top left) or `wx`, `wz`, optional `wy`; `frames` spreads it over frames, for a drag |
| `down`, `up` | `button`: `left` (default), `right`, `middle` |
| `click` | `button`, `hold` frames (default 3) |
| `key` | `keys` such as `"ctrl+shift+l"`, `"enter"`, `"alt+f1"`; `hold` frames (default 3) |
| `keydown`, `keyup` | `key` |
| `wheel` | `delta` (120 is one notch) |
| `wait` | `frames` |
| `camera` | same as the op |
| `settle` | waits for the camera to hold still; `timeout` frames (default 600) |
| `waitFor` | a `dump` query plus `min` (default 1); `timeout` frames (default 1200) fails the sequence |
| `hover`, `status`, `dump` | adds the reading to `results` |
| `sim`, `time`, `money`, `climate`, `tool`, `unlock`, `milestone`, `demand`, `errors` | same as the ops; the reply goes to `results` |

Rectangles in the area tool: the vanilla right-angle snap can pull the closing click off the first corner, so the area stays open and nothing is built (`Area.m_Flags` without `Complete`, a fifth node 0.5 m from the first). Draw test areas slightly off square.

Two guards fail a sequence instead of letting it click on blindly:

- **A new game error stops it**, since an error dialog would swallow the input. Pass `stopOnError: false` on the `seq` when a known
  error is expected, then read `errors`.
- **A press aimed at a world point fails if UI is under the cursor.** Pass `allowUI: true` on the `click` or `down` to allow it.

## Editor import

Launch with `--startEditor`; the first port 9444 reply comes before the Editor is ready, so wait for "Workspace" in the UI. `helpers.js` has `__rows` and `__enter` for the file browser. Toolbar icon (1647,1968) opens the importer; Project root button (3300,134), Assets folder button (3300,220), back arrow (2947,217), `Select Directory` by `__find`, Import (3370,1992). Set Project root to the letter folder and Assets folder to the asset folder itself, so the file list shows only that asset. Never point at a letter folder that holds other assets. Result: `ImportedData\c030ee3ff256df91\<Name>\`, log `Logs\AssetPipeline.log`.

## Building a test city

Setup does not need to be pretty: `unlock`, `money` with `unlimited`, `demand`, then roads with `tool` and real clicks, and zones with
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

- **Input needs focus.** Every input step checks that the game is the foreground window and fails the sequence if not, so input cannot
  land in another app. Bring the game forward from outside first with the `SetForegroundWindow` recipe above. Never computer-use
  `open_application`: it starts a second game. Focus is lost whenever the Claude desktop app comes forward; check `status.focused`.
- **Left-hand traffic needs a new city** (a loaded save keeps its own setting). The New Game toggle persists in `UserState.coc`, so set it
  back afterward, with the game closed.
- **It takes the real mouse while a sequence runs.** Nobody else can use the PC during input steps. Reads never touch the mouse.
- Sequences release held buttons and keys when they end, fail or abort.
- For performance use `perf`, never `dump`: it builds a query and reflects on every call. Same camera for both halves of an A/B, nothing
  else heavy running, and alternate the halves so city drift averages out.
