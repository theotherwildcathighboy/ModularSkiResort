# SkiCatalog: the prefab catalog's contract, who reads it, and the tram's frames

`SkiCatalog` is the pure prefab catalog: every prefab name the lift / station / lodge / sign / hub-build code resolves has ONE home here,
with its numbers: 4 lift types (fixed-grip triple, detachable quad, gondola 8, aerial tram), 8 stations, the lodge ladder (5 tiers), the
ski school, 2 patrol huts, the sign and the runs, plus the ONE build-item table the hub strip mirrors. Citations are the decompile
baseline 1.6.0f1.

## Pure file
`using System;`, `using System.Collections.Generic;`, `using Unity.Mathematics;` only, plus `RunKind` (EntityRef.cs). Linked into
tests/LiftBatch and tests/ResortBatch.

## Names are save identity
`PrefabSystem.Serialize` writes PrefabID = type + name (PrefabSystem.cs:827-865) and a load looks it up by that ID (:878-892). Never
rename an entry; a new generation is a new name.

## Expected / pending
Every NAME carries its own Expected flag. false = "pending": the prefab is designed and named but not imported yet (its NAMES line ends
", pending").
- A MISSING pending name is an Info line, never a WARN.
- A FOUND pending name works exactly like an expected one (behavior never keys on the flag), and `SkiPrefabCatalogSystem` writes one
  "[pending-found]" WARN per load so the import commit flips it.
- A row's Expected (`LiftTypeDef.Expected`, `StationDef.Expected`, ...) is DERIVED: true when every name of the row is expected. A family
  imported over two sessions flips name by name, so no WARN window opens between them.
- Flipping a name = delete ", pending" on its NAMES line and set its row flag (`LineExpected` / `CarrierExpected` / `Expected`) to true
  in the same edit; LiftBatch CT-13 fails on any disagreement.

## What reads this file
- `SkiPrefabCatalogSystem` (the ECS side) resolves every name through ONE typed resolver, lazily per load, revalidated each frame, and
  writes one "PrefabCatalog [arm]" line per load.
- The pure rule files ALIAS the FixedGrip / AFrameLodge / Ladder / School / Patrol / LodgeSpend consts (`SkiMoneyRules`, `SkiDayRules` and
  `SchoolRule` are Burst-callable, so only compile-time consts may cross; a `SkiCatalog.Lifts[i].X` read there would break Burst). Per-row
  numbers reach Burst jobs as struct fields filled on the main thread.
- `tools/preflight.ps1` section 7 NAMES and tests/LiftBatch CatalogTests CT-8 / CT-13 parse the NAMES block as TEXT; CT-9 / CT-14 and
  `mod/SkiResortUI/tools/check-bundle.js` R16 parse the BUILD-ITEMS block as TEXT. That is why the `// NAMES-BEGIN` / `// NAMES-END` and
  `// BUILD-ITEMS-BEGIN` / `-END` markers and the trailing `// Type[, item id][, pending]` comments are load-bearing.
- `catalog/carriers.json` is the ONE carrier-contract source: the generators read it, this file mirrors its numbers as literals (the mod
  never reads repo files), LiftBatch CT-11 compares the two.
- tests/ResortBatch CatalogGuardTests: alias == row == const, and the locale texts against these numbers.

## Grips and pivots (the carrier grip round)
Each lift type names a GRIP prefab (`GripPrefab`, kind Grip = 11, its own Expected flag `GripExpected`, pending until the art
import) and the split carrier's pivots (`CarrierPivots`, prefab-local meters, base-pivot meshes: GripRope = the rope point in the grip's
frame, GripPin = the pin center in the grip's frame, BodyPin = the pin center in the body's frame). `RigidOffset` = BodyPin + GripRope -
GripPin is the rope point in the body's frame at zero pitch and swing; it must equal (0, HangM - GripTopAboveRopeM, 0) (LiftBatch
CT-17), the one-piece carrier's rigid drop. A grip is not a carrier (never in the carrier views, DeleteAllRules skips the kind), and the
row's `Expected` does not include the grip: a pending grip never makes a lift type pending. The pivots are the split build's
(lift_chair.py / lift_cabin.py --split, its build.md; the source comments on each row give the meshes and the CT-17 sum); the proposed
carriers.json fields are `grip_rope_m`, `grip_pin_m`, `body_pin_m` per carrier (CT-11 would then compare them). The pivots also set each
type's roll limits (`CarrierPose.Limits`: the lever ratio |BodyPin.xy| / |RigidOffset.xy|, LiftBatch CP-18).

## The static constructor never throws
A TypeInitializationException would take down every system that touches the class. Every sanity finding goes to `Problems`, which
`SkiPrefabCatalogSystem.OnCreate` logs as "!!!" and LiftBatch CT-1 fails on. Nothing allocates after the static constructor.

## Shuttle (tram) profile and frames
- `LiftTypeDef.AccelMps2` / `.DwellS` (`ShuttleProfile.cs`): tram 0.8 m/s2 (real jig-back trams are quoted around 0.5-1.0 m/s2,
  unverified) and 60 s dwell (carriers.json tram dwell_s 60, CT-11); loop types 0 / 0 (unused).
- `StationDef.DockLocalA` / `.DockLocalB`: the track-rope point at bay 0 / 1, prefab-local, at the ROPE (y 16.00 = track y, not the
  carriage top 16.50); NaN on Loop stations. `.HostsCarriers`: the prefab's ObjectSubObjects host this type's carriers (the tram base).
  Source: SkiTram{Base,Top}StationA.manifest.json `prefab_contract.docks[0..1].rope`.
- Sanity (`CheckRows`): Shuttle types need SpeedMps / AccelMps2 > 0, DwellS >= 0 (finite), exactly one Base station that HostsCarriers,
  no Top that does, finite docks on every station; Loop types need AccelMps2 = DwellS = 0, NaN docks and HostsCarriers false.
- `StationDef.StubTipLocalX` = the lift LINE AXIS in prefab x (the HV stub tip is (StubTipLocalX, 0, StubTipLocalZ); the 4-argument
  `StationAlign.StubTip` overload). 0 everywhere but the tram base: its lot is 6 x 3 (grown on the clock-tower side for the 1:8 ramp) and
  a CS2 lot is centered on the prefab origin, so the building sits 4 m off the origin.
- The game negates FBX x on import and the prefab rows are mirrored, so in the GAME frame the tram base's axis is +4.00 and its docks A / B
  (9.50 / -1.50, 16.00, -5.50); the manifest numbers (axis_x -4.0, docks -9.50 / 1.50) are the authoring frame (-x). The tram top (axis 0,
  docks -5.50 / 5.50) maps onto itself. Sanity: a Shuttle station's docks are centered on its axis.

## Station rows (sources)
Wheel = bullwheel center at rope height, prefab-local (x, y = rope, z); NaN = no loop (Shuttle). FG: `lift_terminal.py` drive (0.2, 3.5) +
0.05 / return 3.7 at the origin, authoring (x, y, z) to (-y, z, x) in the game frame. DQ and gondola: the station manifests'
`contract.wheel_local_prefab` / `stub_tip_local_z` (DQ: the rope at the GRIP-TOP lane). Tram: manifest `prefab_contract.stub_tip_z` /
docks / axis_x; the tram base row is in the GAME frame (docks 9.50 / -1.50, axis +4.00, the manifest x negated). BuildCost = the type's
StationCost{Base,Top} (Sanity checks it).
