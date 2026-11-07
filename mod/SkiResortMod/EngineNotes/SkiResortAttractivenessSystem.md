# SkiResortAttractivenessSystem: how the per-area attraction score is gathered and written over vanilla

The pure `SkiResortScore` computes one attraction score per ski area (one Welcome Sign) from the area's own runs, lifts, active lodges and
the sign's efficiency. This system writes the score onto the active lodges, so the game's tourism sum sees that score.

## Write path (the critical engineering)

Vanilla `Game.Simulation.AttractionSystem` (interval 16, GameSimulation) unconditionally rewrites `AttractivenessProvider` for every
building in its per-tick UpdateFrame slice:

```
query All { AttractivenessProvider RW, PrefabRef RO, UpdateFrame } None { Destroyed, Deleted, Temp }
...
AttractionData data = default;                         // prefab-side input
if (m_AttractionDatas.HasComponent(prefab)) data = m_AttractionDatas[prefab];
...
nativeArray2[i] = new AttractivenessProvider { m_Attractiveness = Mathf.RoundToInt(num) };
```

Two viable anchors:

- (a) Write the PREFAB's `AttractionData` (input side): rejected. Prefab data is shared by every placed instance, so two lodges
  could never score differently.
- (b) Anchored overwrite: CHOSEN. This system is registered `UpdateAfter<..., AttractionSystem>(GameSimulation)` with the same
  interval 16 (anchored systems inherit the anchor's offset, verified in `Game.UpdateSystem`). So each of our writes lands in the
  same tick as vanilla's, immediately after, and simply wins. We write ALL cached lodges every tick rather than mirroring the slice
  bookkeeping. Vanilla only touches the current slice, our post-write is idempotent, and lodge counts are tiny.

## Where the score lives

`Attraction.GetArchetypeComponents` skips upgrade prefabs (Game.Prefabs/Attraction.cs:22-28, the test at :24). `GetUpgradeComponents` puts
`AttractivenessProvider` on the OWNER sign when `m_Attractiveness` > 0 (:30-36, ServiceUpgradeSystem.cs:138-157). The sign prefab has no
Attraction, and the lodge prefab's Attraction is 0. So no sign gets a provider, and vanilla writes nothing that could stack (the (20·N)²
exploit). The model's lodge base (the ladder by tier, extras at most +24) lives in `SkiResortScore`.

Each ACTIVE member lodge carries PerLodge = round(A / √N) (added on first contact, then written every tick after vanilla). So the
game's Σ a² sees N·PerLodge² ≈ A² (exact up to rounding). A lodge that is switched off or listed by no live sign carries NO provider
(removed once). An upgrade has none in vanilla (Attraction.cs:24), and a provider at 0 would still make it a tourist target
candidate (CitizenPathfindSetup.cs:836-853). A sign that still carries a provider (a save from an older version, or another mod) is
held at 0 after the game's write each tick. This system does not remove it.

## Score model

Every constant and the formula live in `SkiResortScore`. This system only gathers the inputs. The hub reads this system's Result and
never recomputes.

```
per member run v = band × size × type × served × open; counted ⇔ v > 0
S = (Σ counted v, ascending + λ × 6 × complete-and-powered lifts) × variety × vertical
A = N ≥ 1 ? clamp(round((lodgeBase(N) × μ + K × √S) × staffing factor), 0, 200) : 0
staffing factor = the sign's efficiency, smoothed (AreaState.StaffingFactor); see the Tuning constants
summer (no counted run): μ 0.5, λ 0.25
```

The lift term is per type (value and λ from the row). The lodge term is the ladder's applied value. S also gains the school and a
patrolled run's factors (`SkiResortScore`'s header).

## Membership

This system computes membership, independent of the hub. The score works with the hub never opened.

- Ski area = a live Welcome Sign from one chunk walk of the PARK query `{Building, Game.Buildings.Park, PrefabRef} None {Temp, Deleted}`.
  Park is on the sign and on every lodge (Park.cs:26-41). So the walk sees parks, signs and lodges only, with no Transform (see Sync
  points). A Destroyed sign stays. Its efficiency reads 0.
- Lodge = a lodge-prefab building in the walk, top-level (no Owner), linked by `LinkTopLevel` (see Top-level buildings). ACTIVE ⇔
  `!BuildingUtils.CheckOption(Building, BuildingOption.Inactive)`. A lodge in the walk linked to no sign is an ORPHAN.
- Run = an area of the BOUND-RUN query `{Area, Geometry, PrefabRef, SkiRunBinding} None {Temp, Deleted}` whose binding's `m_Gate` is
  one of this recompute's signs and whose prefab has an ownership kind (`SkiRunOwnershipSystem.KindOf` ≥ 0: groomed, snowmaking,
  night, natural). Inputs:
  - The catalog's `TryGetRating`. A run rated earlier this load and being re-rated keeps its last band. A run never rated → Pending.
  - `Geometry.m_SurfaceArea` (plan m²).
  - The snow chapter's `GetRunSnow(run, kind, node bottom, node top)` (`Geometry.m_Bounds`' y range is the node height range,
    GeometrySystem.cs:499-576), with Multiplier as the open factor (Source None → 0, counted as "open unknown").
  - The served rule: `CopyServeLifts` + `LiftServedRule.AssignLinked` (Assign + run-to-run links) over every run's `RunFacts.TopNode`,
    once per recompute. If the census is not armed, every run counts as served (fail open, one line per load).
- Lift = a census row (`CopyRows`, the same row order as `CopyServeLifts`) whose base AND top stations exist and are not Deleted
  (State Complete, or Open = a section missing). Its area = `SkiResortIndex.TryResolve(base station xz, TetherMeters)` over this
  recompute's signs (the stats system's lift rule). Complete = census Complete. Powered = `LiftServedRule.IsPowered` (the row's
  `ReadLiftPower` via `CopyServeLifts`), the one power predicate. Unfinished lines and lone stations carry no value and are skipped.
- Efficiency = `SkiResortScore.SignEfficiency` over the sign's Efficiency buffer (BuildingUtils.cs:121-133 re-done;
  `BuildingUtils.GetEfficiency(DynamicBuffer)` does not compile on net48, CS7069). The system keeps the factors below 1, worst first, for
  the hub (`CopyEfficiencyFactors`).

## First write after a load

Nothing is serialized. At load, the system reads every lodge's saved provider value (`m_HeldValues`). An area's first write waits until
both of these are true:

- No member run is pending, or the catalog's rating service Failed.
- `SkiSnowInjectionSystem.SnowSettled`.

While it waits, each active lodge that carried a saved value keeps it (the system writes it every tick; vanilla would rewrite it from the
prefab, now 0), and the snapshot says `WriteHeld`. The wait is capped at `RatingHoldMaxTicks` = 1024 ticks (one WARN, then unrated runs
count 0). A new area (sign placed later) follows the same rule.

## Cadence

This system never sees a Created/Updated/Deleted tag (Cleanup strips them before GameSimulation runs; the chain is in
`SkiResortAttractivenessWatchSystem`'s header). It has one pending flag and two kinds of request:

- STRUCTURAL (next tick). These cause it:
  - the watcher (ModificationEnd, every frame): a lodge, lift station, Welcome Sign or run of ours Created / Updated / Deleted
  - a game load
  - on this system's tick: `SkiRunOwnershipSystem.BindGeneration` (bindings raise no tag), `LiftCensusSystem.Revision`, the settings (K,
    backcountry, gating), the rating hold expiring
- SOFT (at most every `MinSoftGapTicks` = 16 ticks = 256 frames). These cause it:
  - `SkiRunCatalogSystem.RatingRevision`
  - `SkiSnowInjectionSystem.RunSnowGeneration`
  - the `SnowSettled` false → true edge while an area is held
  - the STATE POLL every 16 ticks (one FNV-1a hash of each sign's efficiency percent and its lodges' on/off bits, plus
    `LiftCensusSystem.ComputePowerSignature`: power changes raise no event)
  - the backstop every `PeriodicRefreshTicks` = 256 ticks (4,096 frames)

A structural request upgrades a pending soft one. The system compares counters on the tick (they persist), never per frame. The recompute
runs on the 16-frame tick in GameSimulation, so never while paused.

## Generation

The hub's stats and the guest counter read it. It increments (++) in these cases:

- per load
- per recompute that changed any area view, run view, lift view, efficiency factor list or written value
- when `WriteProviders` drops a deleted lodge

Results are bit-exact in any chunk order (`SkiResortScore` sums the run values ascending), so a chunk reorder does not move it.

## Snapshot

The accessors (`TryGetAreaScore`, `TryGetRunScore`, `TryGetLiftScore`, `CopyEfficiencyFactors`, `CopySigns`,
`CopyActiveLodgeInfo`, `CopySiteMembers`, ...) are main-thread plain reads of the last recompute (frozen while paused).
The snapshot is double-buffered (`m_Cur` / `m_Next`, swapped at the end of a recompute) and allocation-free after the pools grow.
`LodgeSetRevision` moves at a load, when a recompute publishes other (sign, active lodges) lists and when `WriteProviders` drops a
deleted lodge. Its consumers are `SkiDaySystem`, `SkiLodgeParkingSystem` and `SkiLiftTicketSystem`'s gate watch.

## Logs

Numbers use invariant culture. The log has these lines:

- the [arm] line per load
- one area line when A, a count, a flag or WriteHeld changed, folded per area through a `LogWindow`(10 s)
- an efficiency-only area line only when A moves by 2 or the area was quiet for 60 s (`LogArea`)
- [first-write] once per area per load (a WARN when the 1,024-tick cap released it)
- provider added / removed
- a sign carrying a provider
- [settings] per change
- the first trigger of each reason per load ([watch])
- [cost] after 32 recomputes, and one WARN per game day for a recompute over 2 ms, with the game clock (`MonthClockText`). The WARN
  re-arms at the first recompute at or after 12:00 of another day (`SkiDayRules.MiddayRearmDue` / `DoneDayOf`). The load's first
  recompute is kept apart.
- Tourism Overhaul at load and per damped lodge
- one fail-open line per load when the lift census is not armed
- the one-shot "!!!"

## Tourism Overhaul

The system re-checks presence at game load (`Mod.RefreshTourismOverhaulDetection`). Ski days bring visitors on site, so TO's crowding /
park gate can damp our lodges. When TO is present, the system logs a lodge once, and counts it in the DUMP, if its provider reads below
written − max(1, written / 10) on 16 consecutive ticks. No behavior change. Nothing to fold in.

## Cost

Nothing runs per frame (the watcher keeps its two `IsEmptyIgnoreFilter` checks). A Stopwatch around `Recompute` and `PollState` measures
the cost ([cost] line, DUMP).

## Sync points

The per-tick provider writes go through `WriteProvidersJob` on `Dependency`. `EntityManager.SetComponentData` waited for every vanilla job
touching `AttractivenessProvider`: 1.45 ms per tick in a city of 355,000 (docs/research/field/2026-11-01/PERF-BIG-CITY.md). Only
`CheckDamping`, with Tourism Overhaul present, still reads on the main thread.

The park walk syncs only PrefabRef's writers (`EntityManager.CompleteDependencyBeforeRO`, ParkedVehiclesSystem.cs:1732). The query
has no Transform. Reading a Transform through EntityManager completes every job that writes Transform. These jobs include CarMoveSystem,
HumanMoveSystem and the other move systems, which run earlier in the same GameSimulation pass (SystemOrder.cs 1.6.2 :338-356,
AttractionSystem :552). So the system reads the sign positions (`SkiResortIndex`, the lift rule) and the base-station positions
(`m_BaseXz`) only when DUE:

- on a structural recompute: load, binding, census, settings, hold, the watcher's placements and moves (a sign or station moved by Move It
  is tagged Updated, and a moved station also moves the census Revision)
- on the 4,096-frame backstop

Soft recomputes (ratings, snow, the state poll, up to every 256 frames in a snow season) reuse them and pay no Transform sync. A
walk whose signs no longer match the cached index reads anyway (a missed tag, counted, DUMP `fallback read(s)`).

## Lodge ladder and catalog rows

The system reads the sign's ServiceUpgrades by catalog row:

- LODGES (every `SkiCatalog.Lodges` row): `slot.Active` / `Inactive`, plus `slot.ActiveInfo` (`LodgeInfo`: the lodge's
  `SkiCatalog.Lodges` index, `SummitServed`) → `AreaInput.Lodges`, the ladder (`SkiResortScore`). SummitServed =
  `LiftServedRule.SummitServed(lodge xz, the area's complete powered lifts, Ladder.SummitServedRadiusM 300)`. The system evaluates it only
  for rows whose summer share rises when served (μ summit > μ: hut, summit). Every active lodge counts: there is no count limit.
- THE SKI SCHOOL (`SkiCatalog.Schools`): an active entry → `AreaInput.SchoolActive`.
- PATROL HUTS (`SkiCatalog.Patrols`): active entries → `slot.PatrolEntities`. Each member run's `RunInput.Patrolled` =
  `PatrolRule.Patrolled(its top node xz, the area's patrol xz)`.
- LIFTS: `LiftInput.TypeIndex` = the census row's Type (Mixed rows are Unfinished and never reach here).

The poll hash mixes the school and patrol entries' (entity, active) bits too. The system reads lodge and patrol POSITIONS only when due,
and caches them like `m_BaseXz` (see Sync points). Generation also moves when a published `LodgeInfo` list changes (SummitServed flips
without a score change, e.g. in winter), so the hub's "summit lodge not lift-served" row follows.

No serialized state: a lodge's tier IS its PrefabRef. Everything here is recomputed from live components.

## Top-level buildings

Every resort building prefab (5 lodges, the school, 2 patrol huts) is top-level (no ServiceUpgrade): no Owner, no sign lists it. The
park walk collects lodge, school and patrol buildings WITHOUT Owner (an archetype test, `chunk.Has`) into `m_TopLevel` with their
kind. After the signs (`ReadSign`), `LinkTopLevel` resolves each to the nearest live sign within `TetherMeters` over this
recompute's sign index (`SchoolLinkRules.Resolve`, the lift rule, not sticky) and counts it:

- a lodge into Active / ActiveInfo / Inactive (first sign wins, `m_LodgeClaimed`). An unlinked lodge is an orphan: no provider.
- the school through `SchoolLinkRules.Count`
- an active patrol hut into `PatrolEntities`

Active = `Building.m_OptionMask`.

A top-level lodge has its own Efficiency buffer (`CityServiceBuilding`). The area's efficiency = the sign's × the active top-level
lodges' mean weighted by tier base B (`SchoolLinkRules.AreaEfficiency`, school and patrol weigh 0). The system merges the lodges' factors
worst-per-factor into the area's list. The mountain lodges' jobs (and an off-road lodge's on a cat track chain) ride on the area's
carrier (`MountainJobsSystem`). The carrier is the nearest active road lodge to the sign, else the sign. Such a lodge's efficiency = its own × the
carrier's staffing product. The system folds it in after the walk, once the carrier is known. The sign term is then the sign without its
product (`MountainJobsRules.SignTerm`: counted once). The same walk publishes the road-optional lodges' supply (LodgeSupplySystem.md:
roots, chain walk, hold) and each active lodge's `HasRoad` / `Supplied` in `LodgeInfo`. Both, and a depot's road edge, are in the poll
hash. The poll hash mixes each top-level building's (entity, active) and a lodge's efficiency percent. The system writes one
"School [link]" / "Lodge [link]" / "Patrol [link]" line per link change (`SchoolLinkRules.Diff`), and re-logs all of them once per
load. Accessors: `TryGetTopLevelLink` / `CopyTopLevelLinks`.

## Near Tuning (staffing smoothing)

The score uses the sign's efficiency averaged over about one game day (262,144 frames / 16 = 16,384 ticks). Each sample is capped at
1.0, and the result is held to [0.25, 1.0]. Efficiency 0 (a Destroyed / Disabled sign: A = 0) is NOT smoothed or floored.

- Why capped at 1.0: a factor above 1 is a bonus the game gives for something else, not a reason to amplify an attraction score.
  It comes from the EmployeeHappiness factor. That factor is currentWorkforce / averageWorkforce + workConditions × 0.01. So it passes 1
  when the workforce is above its average or the work conditions are good (Game.Simulation/WorkProviderSystem.cs:269-270, 1.6.2).
  The ServiceBudget curve (CityServiceEfficiencySystem.cs:59) may also pass 1 above 100 % budget. UNVERIFIED which one gave the
  1.16 in the field log.
- Why 0.25: a sign swinging down to 44 % must not take the score to nothing in one sample. A staffing collapse still costs three
  quarters of it.

## Near LinkTopLevel

Each top-level building of the last park walk mixes (entity, active) into the hash: three words (index, version, active). So the poll and
the recompute agree. A top-level lodge also mixes its efficiency percent (staffing and utilities are its own). With `next` (the
recompute), the system also does these steps:

- It resolves the building's sign (`SchoolLinkRules.Resolve` over this recompute's sign index, `TetherMeters`) and counts the building
  into that area's slot. A lodge goes into Active / ActiveInfo (tier = its PrefabRef) or Inactive and `m_LodgeClaimed` (first sign wins).
  The school goes through `SchoolLinkRules.Count`. An active patrol hut goes into `PatrolEntities`.
- The area's efficiency folds in its top-level lodges (efficiency = sign × their tier-weighted mean, `SchoolLinkRules.AreaEfficiency`).
  The factors merge, worst per factor.
- It writes one log line per link change (`SchoolLinkRules.Diff`).
- It publishes the links.

Active = the building's own `Building.m_OptionMask` not Inactive (BuildingUtils.cs:351-354). Reading Building syncs its writers
(`BuildingPoliciesSystem`, `RoadConnectionSystem`), once per poll / recompute and only while a top-level building exists.
