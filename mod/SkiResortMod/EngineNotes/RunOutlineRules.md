# RunOutlineRules: how the run outline mirrors the game's area outline (AreaBorderRenderSystem)

The rules mirror the game's area outline, `Game.Rendering/AreaBorderRenderSystem.cs` (1.6.0f1, same in 1.6.2f1), non-infoview pass
(:129-136). Each rule below names its lines in that file.

## Rules

- `Outlined`: `AddBorders` :157-187 = `DrawBorders` :388-437.
  - A chunk with Error or Warning outlines every area in it.
  - A chunk with only Temp outlines an area whose Temp flags hold Create, Delete, Select, Modify, Replace or Parent and not Hidden.
  - A chunk with neither draws nothing.
  - A Slave area is never outlined.
- `ColourOf`: :388-426. The order is Error, then Warning, then the Temp-only hovered color. Parent (Temp-only chunks only) takes the owner color.
- `Dots`: :427-432. Corner dots only for a Temp area flagged Create or Modify.
- `ClosingSide`: :243, :487. The side from the last node back to the first only when the area is Complete.
- `AddKey` / `DrawKey` (shared-edge rule): `AddBorders` (:221-262) files every outlined area's edges in one rotational sense (a
  CounterClockwise area's edges reversed). `DrawBorders` (:457-465, :490-498) skips an edge when the REVERSED key is filed.
  Neighboring outlined areas walk their common edge in opposite senses, so the game draws it for neither.
- `AddEdges` / `DrawArea`: the two walks of one area in the game's order (`DrawBorders`' loop :445-511). A 1-node area draws one dot
  (:446-452). Each side draws three things: the dot at its start, the side unless its reverse is filed, and the dot at its end. Every
  dot is drawn only the first time its position is seen in the frame (nodeMap, :448, :466, :481, :499, :507).
- `Width`: :517 (DrawCircle diameter) and :531 (DrawLine width) are `m_SnapDistance` x 0.3. Ours is RunOutlineSize x 0.3 for both. The
  size is clamped 0..16 (the Options slider's range). 0 = off.
- `SizeChangeDue`: not a game rule. It decides when `SkiRunOutlineSystem` writes its "size a -> b" line.

## Not mirrored (none applies to a ski run)

The Lot first-side half color (:472-474), the MapTile dashes and projection (:367-380), the infoview pass (:117-128). The game
draws surfaces as straight 3D lines (:363). Ours are Projected (draped).

## Flag constants

The flag constants are the literal values of `Game.Tools.TempFlags` and `Game.Areas.AreaFlags`. `RunOutlineRules.cs` cannot
reference Game.dll (the test host links it alone). So `SkiRunOutlineSystem.OnCreate` compares each with the game's enum. It refuses
to arm (one WARN) if a patch changed one.
