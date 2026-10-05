# Modular Ski Resort — a Cities: Skylines II mod

Build a real ski resort in CS2: draw a fixed-grip chairlift like a road (auto-height
towers, sagging cable, pinned chairs), anchor the area with a welcome-sign gate whose
upgrade menu builds the resort (lodge first), paint groomed/snowmaking runs that the
game's own snow simulation keeps white when the guns can run — and that melt when they
can't. Attractiveness and tourism economics react to trails, lifts, and season.

Started 2026-08-03. Working chairlift beta in 5 days; see docs/TIMELINE.md.

## Layout

| Path | What |
|---|---|
| `mod/SkiResortMod/` | The code mod (C#, net48, game-referenced). `dotnet build` auto-deploys. |
| `assets/blender/` | Deterministic Blender/Python generators for every mesh + texture. |
| `assets/import/` | Generator OUTPUT staged for Editor import (gitignored — regenerable). |
| `gamedata-mirror/` | Version-controlled backup of our hand-made game-side prefabs (tools/sync-gamedata.ps1). |
| `docs/` | SPEC, PIPELINE (the runbook), DECISIONS (the ledger), TIMELINE, research/. Local-only (untracked by design). |
| `tools/` | preflight.ps1, validate_prefab.ps1, jxr2png.ps1, sync-gamedata.ps1. |
| `release/` | Paradox Mods staging: listing copy, credits, changelog, screenshots. |

## Licence

Code (the C# mod, the Blender generators, the tools) is MIT — see `LICENSE`. Art assets and
everything generated from them are CC-BY-4.0 — see `assets/LICENSE`. Design references are
credited in `release/CREDITS.md`; no third-party code is included, and the GPL-licensed mods we
studied (ParkManager, ParkingLotTool) were re-derived from the game's own systems, never copied.

## Reading order for a new session

1. `CLAUDE.md` — the working contract.
2. `docs/PIPELINE.md` — the asset/prefab process (blood was spilled for every rule).
3. `docs/DECISIONS.md` — why everything is the way it is.
4. `docs/research/run-rendering-synthesis.md` — how ski runs are rendered (layered:
   snow injection + decals; the slot-hijack rejection story).
