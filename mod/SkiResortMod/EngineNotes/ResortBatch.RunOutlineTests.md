# RunOutlineTests: the engine's area-border drawing rules the run outline mirrors

`RunOutlineTests` covers `RunOutlineRules` (width and dot size from size, Temp and dot eligibility, color order, closing side) and the
outline walks. Oracles are literal hand values read off `Game.Rendering/AreaBorderRenderSystem.cs` (1.6.0f1), never a second run of the
same code. The float widths use a tolerance of 1e-5.

## Sections

- A, sizes: the clamp 0..16, on / off. Line width and dot diameter are 0.3 x size (8 -> 2.4 m, 16 -> 4.8 m).
- B, flag mirrors: the literal `TempFlags` / `AreaFlags` values (`Game.Tools/TempFlags.cs`, `Game.Areas/AreaFlags.cs`, the mod also
  compares them with the enums at OnCreate) and the two composite masks.
- C, Outlined (:157-187, :388-437): every Temp flag alone in a Temp-only chunk, Hidden, no Temp, Error and Warning chunks (no Temp
  filter there), Slave always out.
- D, Dots (:427-432): Create / Modify only, never without a Temp.
- E, color order (:388-426): all eight (Error, Warning, Parent) combinations. Error > Warning > Parent > hovered.
- F, ClosingSide (:243, :487) and the shared-edge keys AddKey / DrawKey (:221-262, :457-498) for both windings.
- G, the walks AddEdges / DrawArea on hand-built shapes with the exact draw sequence. The shapes are:
  - an open 3-node Temp
  - the same closed
  - a 2-node line
  - a 1-node cursor
  - an empty buffer
  - no dots for a Delete copy
  - two 10 m squares sharing a side (the shared side drawn by neither, its two corners dotted once)
  - a square that only touches a corner keeps all its sides
- H, SizeChangeDue, the size-change log rule: a switch to 0 is due once a frame would draw. A clamped equal value is not due.
  8 -> 0 -> 16 -> 8 gives exactly three lines with the right old values. A slider drag gives one line.
- I, run-visual constants.
