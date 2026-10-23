# SkiGladeHover: hover log contract and dwell accounting for the Glade tool

`SkiGladeHover.cs` holds the hover kinds, plan states and the per-activation hover log (`GladeHoverLog`) of the glade tool. It is
pure on purpose (System, System.Collections.Generic and Unity.Mathematics only): the same file compiles into the net48 mod and is
linked into `tests/GladeBatch`.

## Usage

`SkiGladeToolSystem` resolves the hover every frame into a `GladeHoverKind`, keeps a `GladePlanState` and owns one
`GladeHoverLog`:

- `Begin()` in OnStartRunning.
- `Offer(kind, entityIndex, reason)` from UpdateHoverStatus. When it returns true the tool writes the transition line. When it
  returns false and `CapJustReached` is true, the tool writes the one "transitions capped" line. Read `CapJustReached` right after
  that Offer, before any early return: the next Offer clears it, and it is raised once per activation.
- `AddDwell(kind, state, UnityEngine.Time.unscaledDeltaTime)` on every UpdateCore path (hold, cancel and apply early returns
  included).
- Increments of `Plans` / `Applies` / `ViaPoint` (plain fields).
- At OnStopRunning it writes `"Glade: " + Summary(lastState)` before ResetPreviewState().

## Contract

- Offer dedups on the last offered (kind, entityIndex, reason): the same triple twice gives one line. Switching (every hotkey
  activation's first frame has a disabled raycast) and None are never logged and do not touch the dedup key, so
  Terrain -> Switching -> Terrain stays one line.
- At most `MaxLinesPerActivation` (40) offers per activation return true; every later transition counts in `Suppressed` (dedups
  count nowhere). `CapJustReached` is true after exactly one Offer per activation: the first suppressed one (it reflects the most
  recent Offer call).
- Dwell: every finite positive `seconds` lands in exactly one bucket and in the total, so the buckets sum to `TotalSeconds` (up to
  double rounding; accumulated in double, exposed as float). Non-Bound kinds bucket by kind; Bound buckets by plan state.
- The summary always prints the eight base buckets (terrain, foreign, unbound, held, nothing, previewing, complete, blocked) and
  appends clamped / saturated / money short / legacy / switching / other only when non-zero, so the printed buckets also sum to
  the total.
- Begin / Offer / AddDwell allocate nothing (they run per frame); Summary allocates once, at tool stop. Seconds print with one
  decimal, culture-free.
