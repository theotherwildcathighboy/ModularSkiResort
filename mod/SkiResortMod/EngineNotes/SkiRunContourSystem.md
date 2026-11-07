# SkiRunContourSystem: how the vanilla contour lines are switched on for the area tool, and handed back

`SkiRunContourSystem` turns the vanilla terrain contour lines on while the area tool holds one of our run prefabs, and gives the
player's own setting back afterwards. Engine citations are `Game.X/File.cs:line` in tools/decompile/1.6.0f1.

## The only route

`UndergroundViewSystem.OnUpdate` sets `contourLinesOn = globalContourLinesOn || (GetActualSnap(selectedSnap, on, off) & Snap.ContourLines) != 0`
(UndergroundViewSystem.cs:120, `GetActualSnap` ToolBaseSystem.cs:467-470). The area tool's Surface branch never puts ContourLines into its
masks (AreaToolSystem.cs:3291-3299, 1.6.2 :3302). So OR-ing it into `selectedSnap` would be masked out and would corrupt the player's
snap selection. What remains is the public setter `globalContourLinesOn` (UndergroundViewSystem.cs:67), the flag the vanilla contour
button reads and writes (ToolUISystem.cs:154, 177, 499-511, 1.6.2 :555-567). It is not serialized, and `OnGameLoaded` resets it to false
on every load (:92-96).

## Order

Mod.cs declares `UpdateBefore<SkiRunContourSystem, UndergroundViewSystem>(PreCulling)` (UndergroundViewSystem at SystemOrder.cs:632). So
our write is read the same frame. PreCulling runs every frame, paused included.

## State machine (Step())

```
want = setting && armed && activeTool == areaTool && areaTool.prefab is one of our runs
g    = uvs.globalContourLinesOn
if (m_Active) { if (g != m_Written) { m_Active = false; m_Latched = want; }  // someone else wrote it: theirs, never restore
                else if (!want)     { g := false; m_Active = false; } }
else if (want && !m_Latched) { if (!g) { g := true; m_Written = true; m_Active = true; } else m_Latched = true; }
if (!want) m_Latched = false;
```

Release (OnStopRunning / OnDestroy / exception): `if (m_Active && g == m_Written) g := false`. Then clear the flags. OnGameLoadingComplete
applies Release too. Vanilla resets g on load (:94), so the guard (`g == m_Written`) is false after a load and nothing is written. The
guard can only fire if g still holds our write. Then it removes our own leftover.

## Why poll

A click on the vanilla button is a plain property write with no callback (ToolUISystem.cs:508-511). The button shows ON while we override
(its getter reads the same flag, :499-506). A click writes false. The next frame sees this as `g != m_Written`. So the player's click wins
and is never undone. The "Toggle Global Contour Lines" hotkey has no C# handler (JS-side, presumably the same trigger, unverified).
Same-frame double writes are indistinguishable from no write. The design accepts this.

## No RequireForUpdate

OnUpdate must run to restore. The system caches the tool's prefab as reference -> isRunPrefab. It resolves the prefab
(`PrefabSystem.TryGetEntity`, PrefabSystem.cs:710-713, + `SkiRunOwnershipSystem.IsRunPrefab`) only when the reference changes and
ownership is armed. `AreaToolSystem.prefab` and `ToolSystem.activeTool` are plain field getters (AreaToolSystem.cs:2637-2652,
ToolSystem.cs:84-99). While `contourLinesOn` is set, UndergroundPass skips its early-out. It re-renders terrain heights + one
full-screen contour compute per frame (UndergroundPass.cs:105-126). This is vanilla's own toggle cost, and it applies only while we
override.

## Defensive contract

- Game mode only. Outside it, `want` is false, so a leftover override is still restored.
- A "created" line and an ARMED line per load.
- `m_Armed` / `m_Disabled` / `m_ErrorLogged` reset in OnGameLoadingComplete.
- A one-shot "!!!" catch restores the override (if ours) and disables it until the next game load.
