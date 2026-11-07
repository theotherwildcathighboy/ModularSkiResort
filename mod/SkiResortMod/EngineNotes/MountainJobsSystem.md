# MountainJobsSystem: why mountain building jobs live on a carrier (a road lodge, else the Welcome Sign), and the engine path that reads them

The roadless mountain buildings' jobs (rows: hut 4, summit 8, patrol HQ 2, patrol hut 2) live on their area's carrier. The pure
arithmetic is in `MountainJobsRules` (tests/BuildingBatch).

## The carrier (b1)
The carrier is the nearest active road lodge to the sign. A road lodge is a lodge row with a road edge that takes arrivals
(`LodgeSupplyRule.IsRoadLodge`). A tie goes to the lower entity (`MountainJobsRules.BetterCarrier`). The attraction recompute picks the carrier
(`SkiResortAttractivenessSystem.CarrierOf`). With no road lodge, the carrier is the sign. So staff use a real door, and a chain that touches only a depot keeps
the staff on the sign (a depot has no jobs). An off-road lodge on a cat track chain hands its own jobs (its prefab's WorkplaceData) to the
carrier too (`LodgeSupplyRule.JobsLeave`).
- The sign is written here only. Its target is the jobs it carries (0 while a lodge carries). The tick lays the staff off.
- `OffSeasonStaffSystem` reads a carrier lodge's sum (`CarriedBy`). It is the one writer of a lodge's m_MaxWorkers
  (`MountainJobsRules.CarrierTarget`: own target + carried). Two writers would undo each other every poll.
- The hub's SignNoRoad row shows only while the sign carries jobs (`SignCarriesJobs`: its target once `TargetsReady`, before that the
  saved WorkProvider's m_MaxWorkers > 0). That is: no active road lodge in the area, and at least one switched-on Hut, Summit, patrol
  building, or off-road lodge on a depot-only chain.
- Re-homing: a carrier that is switched off, loses its road or is bulldozed drops out of the next recompute. The jobs move to the next
  carrier, and the game's tick lays off at the old one and hires at the new one (vanilla grace on a raise).
- Education: the carried jobs take the carrier prefab's complexity (`EconomyUtils.CalculateNumberOfWorkplaces`, EconomyUtils.cs:1370-1400,
  with num = 4 x complexity + level - 1). A non-spawnable building is level 1. The sign is Manual (0): 12/16 uneducated, 4/16 poorly educated.
  The A-frame, Chalet and Grand are Simple (1): 4/16 uneducated, 8/16 poorly, 4/16 educated. Job counts do not change.

## Why the sign
The four are top-level buildings that demand nothing. A Workplace on a top-level building sets RequireRoad
(BuildingInitializeSystem.cs:229-283), and they stand on slopes. The sign has the road.

Vanilla's ServiceUpgrade combine gave the owner its children's workplaces:
- `ServiceUpgradeSystem.UpgradeInstalled` added the child's upgrade components (Workplace.GetUpgradeComponents = WorkProvider + Employee,
  Workplace.cs:45-52) to the owner (ServiceUpgradeSystem.cs:137-154).
- `CityServiceWorkplaceInitializeSystem` wrote WorkProvider.m_MaxWorkers = CityUtils.GetCityServiceWorkplaceMaxWorkers (CityUtils.cs:41-78)
  when an upgrade was Created or Deleted (CityServiceWorkplaceInitializeSystem.cs:97-121, query :222-248).

This system does the same two things for the distance link (`SchoolLinkRules`, `SkiResortAttractivenessSystem.CopyTopLevelLinks`):
- A sign with a target > 0 and no WorkProvider gets WorkProvider + an Employee buffer, with vanilla's new-workplace grace
  m_EfficiencyCooldown = -m_ServiceBuildingEfficiencyGracePeriod (:111, :119).
- A sign whose m_MaxWorkers differs from its target is set (0 too: the component stays and the tick lays the staff off).

## The engine path that reads it
`WorkProviderSystem`'s tick (query :513-531, every 512 frames per UpdateFrame slice, :552) takes reference.m_MaxWorkers as the entity's size
for every non-company, non-school building (:133-184). The tick uses it for:
- workplaces by education (EconomyUtils.CalculateNumberOfWorkplaces with the SIGN prefab's WorkplaceData.m_Complexity),
- FreeWorkplaces (:199-230),
- the staffing factors in the sign's Efficiency buffer (:252-281) and the workers notifications.

The tick never recomputes m_MaxWorkers for a city service building (only schools and outside connections, :153-168).
CityServiceWorkplaceInitializeSystem runs only when the sign itself or an upgrade of it is Created / Deleted. So the value we write stays.
This system re-asserts it every poll anyway.

The tick needs WorkplaceData on the sign PREFAB (:133, else it liquidates the staff). So `SkiResortGate.Prefab` keeps its Workplace block with
m_Workplaces 0 (BuildingBatch J8 gates it). The archetype gets no WorkProvider with 0 (Workplace.cs:36-43), hence the add here.

Saves: WorkProvider and Employee serialize (ISerializable), so a load keeps them. The first poll re-asserts.

## Sync
The sync runs every PollFrames simulation frames, in Game mode only, after the first recompute of the load (`HasSnapshot`). Targets = SumBySign over
the published top-level links, split by carrier. The split gives a sign's own sum and each carrier lodge's sum (`TargetsReady` once the first split is known).
Place, remove, relink, Move It and sign bulldozed all reach those links at the next recompute. Carriers = every sign that has a target or a WorkProvider (the load pass
scans the signs once). An Inactive building carries 0. A sign that is gone drops out. No component is ever removed. A lodge's sign jobs
(hut 4, summit 8) halve while `OffSeasonStaffSystem.IsHalf(its sign)` (off-season, setting on). Patrol stays full
(`MountainJobsRules.SignJobsFor`).

## Logs
- "created"
- One load line.
- One line per sign change: "MountainJobs [sign] Welcome Sign i:v workplaces a -> b (summit 8 + patrol hut 2; ...)"
- "!!!" once on a throw (then disabled until the next load).

DUMP: AppendDump (hub "Dump diagnostics").

Phase GameSimulation (not load-bearing: the tick reads the value whenever it runs).
