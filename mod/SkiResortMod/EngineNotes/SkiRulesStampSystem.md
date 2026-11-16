# SkiRulesStampSystem and SkiRulesStamp: the per-object rules level (1.0.2, T229)

Pure rules: `SkiRulesLevel.cs` (tests/ResortBatch RS-1 to RS-3). Persisted type: `SkiRulesStamp.cs` (SaveLayouts row `RulesStamp`,
tests/SaveBatch SL-S1 to SL-S5, and SL-R for every release line).

## Why
A later patch may change how lifts, runs or buildings are built. The planned Sync Resort engine (the private sync-resort design)
brings objects built under older rules up to date. It needs to know which rules built what. 1.0.2 ships only the stamp, with no
engine, no button and no visible change. Every object built from 1.0.2 on carries its level. Objects from older saves read level 0.

## The stamp
- `SkiRulesStamp { m_Rules: i32, m_Flags: u8 }`, layout v1, 6 bytes. A NEW type: no shipped layout grows, so a 1.0.0 / 1.0.1 build skips
  it as an unknown type and loads everything else.
- One global level, `SkiRulesLevel.Current` = 1 (the 1.0.2 baseline, which already holds every 1.0.1 change to built objects). Each
  later sync step raises it by one. A level is never reused or renumbered.
- Absent = level 0. A level is a hint: every sync step checks the real state before it acts.
- `m_Flags`: bit 1 Kept, bit 2 HandEdited, reserved for the engine. 1.0.2 never sets them.

## Who writes it
| Object | Where | When |
|---|---|---|
| Lift line sections (lift line edges without an Owner) | this system | `Created`, after the load guard |
| Stations, lodges, school, patrol huts, bus depot, Welcome Signs | this system | `Created`, after the load guard |
| Runs, cat tracks (ski ways are cat tracks) | this system | `Created`, after the load guard |
| A re-emitted run (Groomed toggle) | SkiRunReemitSystem via `CarryOver` | the source's level; an unstamped source gives an explicit 0 |
| Merged buildings | SkiMergeSystem via `StampMerged` / `UnstampMerged` | at merge; removed at unmerge |

Not stamped: tower nodes (a section's edge stands for its towers), carriers, labels, decals, snow and run props (they self-heal), planner
filler trees (`SkiRunTreeMember.m_PlanGen` is their own stamp).

## Why not in the tower fit (the design's first idea)
At a load, the fit keeps a line that stands as built and re-fits the others. Its own writes change the line's node positions. The next
census build sees a new signature and queues the line again as "changed in session" (not the load baseline). A fit-side stamp on
non-baseline lines would therefore mark old lines current. Stamping new sections at creation avoids that. It also matches how lifts
are edited: section by section. A line is as current as its oldest section.

Known edge cases (accepted, the level is a hint): a vanilla split of an old section makes new edges, which are stamped current. A
station move that rebuilds line edges stamps them current. A section built while "Plan lift towers" is off is stamped current too
(the setting is a player choice, like a hand edit; the engine must respect both).

## Loaded objects are never stamped (decompile 1.6.4f1)
- `Game.Common.Created` is an empty IComponentData, not ISerializable (Created.cs), so a save never holds it.
- After a load, `RequiredComponentSystem` (serialization namespace, :752-801) adds missing non-serialized components such as
  NodeGeometry and MeshBatch without Created. Version upgrades there add only `Updated` (:1189-1205, :1391, :1517, :1814).
- Tool placement adds Created through the apply systems (ApplyObjectsSystem.cs:924, ApplyAreasSystem.cs:272, ApplyNetSystem.cs:901).
- On top, nothing is stamped in the first `GuardFrames` (120) frames of a load. The guard counts our objects seen with Created there and
  logs `Stamp [guard]` (expected 0). In game: see the r2-sync report.

## Logs
- `Stamp: ARMED ...` once per Game-mode load.
- `Stamp [guard]: N of our objects carried Created in the load's first 120 frames ...` once per load.
- `Stamp [census] load: lift sections 12 (12 at 0, 0 at 1), runs 3 (...)` once per load, after the guard (prefab sets ready).
- `Stamp [new]: 2 lift sections, 2 stations at rules level 1` per frame that stamps.
- `Stamp [census] after building: ...` once a build burst is quiet for 300 frames.
- `SkiRulesStampSystem.LastCensusLine` / `LastCensus` hold the newest census (dev read by reflection). The bridge's `dump` op reads the
  stamps too: `{ op: "dump", all: ["SkiResortMod.Systems.SkiRulesStamp"], show: ["SkiResortMod.Systems.SkiRulesStamp"] }`.

## Cost
The stamp-new pass runs only on frames with a Created edge, building or area: a few HashSet reads per new entity. The census reads
every edge, building and area once per load and once per quiet build burst, never per frame.
