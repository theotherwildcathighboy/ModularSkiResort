# MountainJobsSystem: why mountain building jobs live on the Welcome Sign, and the engine path that reads them

The roadless mountain buildings' jobs live on their linked Welcome Sign (rows: hut 4, summit 8, patrol HQ 2, patrol hut 2). The pure
arithmetic is in `MountainJobsRules` (tests/BuildingBatch).

## Why the sign
The four are top-level buildings that demand nothing: a Workplace on a top-level building sets RequireRoad
(BuildingInitializeSystem.cs:229-283) and they stand on slopes. The sign has the road.

Vanilla's ServiceUpgrade combine gave the owner its children's workplaces:
- `ServiceUpgradeSystem.UpgradeInstalled` added the child's upgrade components (Workplace.GetUpgradeComponents = WorkProvider + Employee,
  Workplace.cs:45-52) to the owner (ServiceUpgradeSystem.cs:137-154);
- `CityServiceWorkplaceInitializeSystem` wrote WorkProvider.m_MaxWorkers = CityUtils.GetCityServiceWorkplaceMaxWorkers (CityUtils.cs:41-78)
  when an upgrade was Created or Deleted (CityServiceWorkplaceInitializeSystem.cs:97-121, query :222-248).

This system does the same two things for the distance link (`SchoolLinkRules`, `SkiResortAttractivenessSystem.CopyTopLevelLinks`):
- a sign with a target > 0 and no WorkProvider gets WorkProvider + an Employee buffer, with vanilla's new-workplace grace
  m_EfficiencyCooldown = -m_ServiceBuildingEfficiencyGracePeriod (:111, :119);
- a sign whose m_MaxWorkers differs from its target is set (0 too: the component stays and the tick lays the staff off).

## The engine path that reads it
`WorkProviderSystem`'s tick (query :513-531, every 512 frames per UpdateFrame slice, :552) takes reference.m_MaxWorkers as the entity's size
for every non-company, non-school building (:133-184):
- workplaces by education (EconomyUtils.CalculateNumberOfWorkplaces with the SIGN prefab's WorkplaceData.m_Complexity);
- FreeWorkplaces (:199-230);
- the staffing factors in the sign's Efficiency buffer (:252-281) and the workers notifications.

It never recomputes m_MaxWorkers for a city service building (only schools and outside connections, :153-168), and
CityServiceWorkplaceInitializeSystem runs only when the sign itself or an upgrade of it is Created / Deleted, so the value we write stays;
this system re-asserts it every poll anyway.

The tick needs WorkplaceData on the sign PREFAB (:133, else it liquidates the staff): `SkiResortGate.Prefab` keeps its Workplace block with
m_Workplaces 0 (BuildingBatch J8 gates it); with 0 the archetype gets no WorkProvider (Workplace.cs:36-43), hence the add here.

Saves: WorkProvider and Employee serialize (ISerializable), so a load keeps them; the first poll re-asserts.

## Sync
Every PollFrames simulation frames, Game mode only. Targets = SumBySign over the published top-level links (place / remove / relink / Move
It / sign bulldozed all reach those links at the next recompute). Carriers = every sign that has a target or a WorkProvider (the load pass
scans the signs once). An Inactive building carries 0. A sign that is gone drops out; no component is ever removed. A lodge's sign jobs
(hut 4, summit 8) halve while `OffSeasonStaffSystem.IsHalf(its sign)` (off-season, setting on); patrol stays full
(`MountainJobsRules.SignJobsFor`).

## Logs
"created"; one load line; one line per sign change "MountainJobs [sign] Welcome Sign i:v workplaces a -> b (summit 8 + patrol hut 2; ...)";
"!!!" once on a throw (then disabled until the next load). DUMP: AppendDump (hub "Dump diagnostics").

Phase GameSimulation (not load-bearing: the tick reads the value whenever it runs).
