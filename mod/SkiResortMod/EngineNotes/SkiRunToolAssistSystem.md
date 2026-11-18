# SkiRunToolAssistSystem (1.0.2)

Two small helpers for the run drawing tool. They do not need the slope overlay's rating. Phase: ModificationEnd, before
`IconCommandSystem` (Mod.cs, lock anchor).

## T244: Esc restarts the outline

Vanilla facts (1.6.4f1):

* `AreaToolSystem.UpdateCancelAction` enables `cancelAction` only in the Modify state (moving, merging, inserting a node of a complete
  area). In the Create state it is disabled, so the tool never takes Esc while an outline is drawn.
* Esc goes to the UI. The asset menu closes (`onClose` = `toolbar.clearAssetSelection`). `ToolbarUISystem.ClearAssetSelection` calls
  `ToolSystem.ActivatePrefabTool(null)`, which selects the default tool. One Esc drops the tool and loses the outline.
* `AreaToolSystem.OnStartRunning` clears `m_ControlPoints`.

The mod has no Harmony, so it cannot take the key first. It undoes the switch instead:

1. Each frame, it records the Esc press (`Keyboard.current.escapeKey.wasPressedThisFrame`). It also records the last frame of a started
   outline. Then the area tool is active with one of our run prefabs. A new run Temp also has at least one placed point (2+ nodes: the
   placed points and the cursor).
2. When the active tool becomes `DefaultToolSystem`, and both events are at most `EscWindowFrames` (10) frames old, it calls
   `ActivatePrefabTool` with the same prefab. The outline starts over with the tool still in hand.
3. A second Esc finds no placed point, so the tool leaves as in vanilla. A click on another tool is not the default tool, so it is left
   alone.

Each reset logs `RunToolAssist [esc]` (at most 20 lines per load). Not checked in game yet. The hub and the asset menu may also react to
the key. For example, the asset menu may stay closed while the tool is active again.

Fallback if the game check fails: drop the reset. Add the tooltip line "Right click removes the last point" instead. (Vanilla right
click removes the last control point.)

## T243b: money icon on the run Temp

* Vanilla `ValidationSystem` shows `NotEnoughMoney` only on a Temp with `Temp.m_Cost > 0`. A run Temp has no cost (the fee is charged at
  the bind), so vanilla shows nothing.
* The system adds the vanilla icon (the `NotificationIconData` prefab whose `ToolErrorData.m_Error` is `NotEnoughMoney`) on the new run's
  Temp when `SkiRunTooltipSystem.TryGetNewRunFee` is above `CitySystem.moneyAmount`. Location: `Geometry.m_CenterPosition` (an area owner
  has no location of its own in `IconCommandSystem`).
* `isTemp: true`: the icon dies with the Temp (`ToolClearSystem` removes every Temp). A rebuilt Temp is a new entity, so the icon is added
  again. It is removed when the fee becomes affordable.
* ModificationEnd, not Rendering: in Rendering the command would play back one frame later, after the tool rebuilt the Temp, and
  `IconCommandSystem` skips a dead owner.
* The fee comes from the overlay readout, so the icon follows the `ShowRunGradeWhileDrawing` setting like the fee row.

It does not block the creation. An icon adds no `Game.Tools.Error` component, so the area still applies. Blocking options (owner
ruling pending):

* Add `Game.Tools.Error` to the Temp. Vanilla then refuses the click and draws the outline red.
* Set `Temp.m_Cost`. Vanilla validation then shows the icon and blocks. But vanilla also charges the cost at apply, so the bind fee
  would be charged twice.
