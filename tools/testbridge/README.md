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
| `project` | `wx`, `wz`, optional `wy` | client pixel; `safe` is false when off screen or in the edge-scroll band |
| `camera` | optional `wx` + `wz`, `zoom`, `yaw`, `tilt` | camera state; applied on the next frames, so follow with a `settle` step |
| `sim` | optional `speed` (0 pauses) | speed and frame index |
| `time` | `minutes` | advances the game clock (the dev menu's `DebugAdvanceTime`) |
| `money` | `add` | new balance |
| `climate` | any of `date`, `temperature`, `precipitation`, `cloudiness` | a number overrides, `null` hands back to the simulation |
| `dump` | `all`, optional `none`, `show`, `limit` (default 50) | match count and rows with prefab name and the `show` components |
| `entity` | `id` as `"index:version"`, optional `only` | component names, with values for `only` (all when absent) |
| `seq` | `steps` | starts a sequence |
| `seqStatus` | | `done`, `step`, `error`, `results` |
| `abort` | | stops the sequence and releases all held input |

Component names may be short when they match one type (`SkiRun`), else full (`Game.Objects.Transform`). An ambiguous name returns the
candidates. `dump` leaves out `Deleted` entities unless `none` is given. Pass `none: []` to include them.

Save and load need no helper: `engine.trigger("menu.saveGame", "<name>")` or the pause menu through the MCP (not yet run).

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
  land in another app. Bring the game forward from outside first (computer-use `open_application`).
- **It takes the real mouse while a sequence runs.** Nobody else can use the PC during input steps. Reads never touch the mouse.
- Sequences release held buttons and keys when they end, fail or abort.
- Not for measuring performance: `dump` builds a query and reflects on every call.
