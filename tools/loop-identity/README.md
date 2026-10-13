# Loop bit-identity evidence (code round 3, 2026-10-11)

Kept here because the session scratchpad is not durable (round-3 review, contract finding #3).

- `bodycompare.js <baseline LiftLoopCache.cs> <working LiftLoopCache.cs>` — compares the Loop-mode bodies
  (geometry, Sample, the `double cum` closure, note texts) item by item; prints `ALL SAME (23)` when the
  loop mover is untouched. Run with node from the repo root.
- `baseline-d3ae435b/LiftLoopCache.cs` — the pre-Phase-C file (hash d3ae435b…) the round compared against.
  Phase A + B are uncommitted, so this copy is the only baseline until they are committed.
- `fingerprint-harness/` — `Harness.cs` + `compare.js` compute the ResortBatch T1-T3 / W1-W3 score
  fingerprints from a copy of `SkiResortScore.cs` (A = git HEAD, B = working tree) and diff the six rows;
  `fp.csproj.example` is the project file used for one side; `round-2-baseline.out` is side A's output
  (all rows 0x36445C5260A84AE5, 760 fields). See docs/research/art/assets-2026-10/code-round-3/round-3-integration.md §3.
