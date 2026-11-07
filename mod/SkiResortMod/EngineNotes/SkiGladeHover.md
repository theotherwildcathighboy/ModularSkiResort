# SkiGladeHover: hover log contract and dwell accounting for the Glade tool

`SkiGladeHover.cs` holds the hover kinds, plan states and the per-activation hover log (`GladeHoverLog`) of the glade tool. It is
pure on purpose (System, System.Collections.Generic and Unity.Mathematics only). The same file compiles into the net48 mod and is
linked into `tests/GladeBatch`.

## Usage

`SkiGladeToolSystem` resolves the hover every frame into a `GladeHoverKind`, keeps a `GladePlanState` and owns one
`GladeHoverLog`:

- `Begin()` in OnStartRunning.
- `Offer(kind, entityIndex, reason)` from UpdateHoverStatus. When it returns true the tool writes the transition line. When it
  returns false and `CapJustReached` is true, the tool writes the one "transitions capped" line. Read `CapJustReached` right after
  that Offer, before any early return. The next Offer clears it, and it is raised once per activation.
- `AddDwell(kind, state, UnityEngine.Time.unscaledDeltaTime)` on every UpdateCore path (hold, cancel and apply early returns
  included).
- Increments of `Plans` / `Applies` / `ViaPoint` (plain fields).
- At OnStopRunning it writes `"Glade: " + Summary(lastState)` before ResetPreviewState().

## Contract

- Offer dedups on the last offered (kind, entityIndex, reason): the same triple twice gives one line. The tool never logs Switching
  (every hotkey activation's first frame has a disabled raycast) and None. They do not touch the dedup key. So
  Terrain -> Switching -> Terrain stays one line.
- At most `MaxLinesPerActivation` (40) offers per activation return true. Every later transition counts in `Suppressed` (dedups
  count nowhere). `CapJustReached` is true after exactly one Offer per activation: the first suppressed one (it reflects the most
  recent Offer call).
- Dwell: every finite positive `seconds` lands in exactly one bucket and in the total. So the buckets sum to `TotalSeconds` (up to
  double rounding: the code accumulates in double and exposes float). Non-Bound kinds bucket by kind. Bound buckets by plan state.
- The summary always prints the eight base buckets (terrain, foreign, unbound, held, nothing, previewing, complete, blocked). It
  appends clamped / saturated / money short / legacy / switching / other only when non-zero. So the printed buckets also sum to
  the total.
- Begin / Offer / AddDwell allocate nothing (they run per frame). Summary allocates once, at tool stop. Seconds print with one
  decimal, culture-free.
