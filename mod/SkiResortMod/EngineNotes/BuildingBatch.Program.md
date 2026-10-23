# BuildingBatch: what the top-level resort building tests pin

The tests cover the ski school, the five lodges and both patrol huts as standalone top-level buildings linked to the nearest
Welcome Sign. Groups, in the order the checks run:

- **L, attribution** (`SchoolLinkRules.Resolve` = `SkiResortIndex.TryResolve`, TetherMeters 5000): L1 the nearest sign; L2 5000.00 m is
  in; L3 5000.01 m is out; L4 NaN never links; L5 an exact tie goes to the lower (Index, Version); L6 the sign deleted means not
  linked; L7 a nearer new sign takes the school (the link is not sticky); L8 no sign at all.
- **C, the one count rule** (`SchoolLinkRules.Count`): C1 active and live; C2 Inactive; C3 not live; C4 a child and a top-level school in
  one area (2 listed, active); C5 an inactive child plus an active top-level school.
- **I, a linked top-level school's slot facts** (1 listed, on = active) and what follows from them: SchoolValue, lessons and the
  HubProblems row 25, over four fixtures (green counted / no counted green / no runs / school Inactive). I5: an unlinked school means no
  school.
- **D, the link-log diff** (`SchoolLinkRules.Diff`): Linked, Relinked, Unlinked (new and lost), Removed; a distance-only change gives no
  line; stable order; an empty "before" gives one line per school (the per-load re-log).
- **E, `SchoolLinkRules.AreaEfficiency`**: E1 no top-level lodge gives the sign's own efficiency; E2 the tier-weighted mean times the
  sign; E3 a lodge at 0 alone gives 0; E4 the sign at 0 gives 0; E5 rounding to 0.01 with a 0.01 floor; E6 the school and patrol weigh 0.
- **J, mountain jobs on the sign** (`MountainJobsRules`, SkiCatalog SignJobs): J1 the sum over one sign; J2 a relink moves the slice;
  J3 not linked, removed or sign deleted gives 0; J4 the write rule and the grace; J5 the catalog rows; J6 the efficiency slice (staffing
  counted once); J7 the four mountain buildings on one sign give 16; J8 the sign prefab keeps its Workplace block
  (`WorkProviderSystem.cs:133` needs WorkplaceData or it liquidates the staff).
- **P, the mirror gate per building** (gamedata-mirror, all eight: SkiAFrameDayLodge, SkiLodgeHut, SkiLodgeMid, SkiLodgeSummit,
  SkiLodgeGrand, SkiSchool, SkiPatrolHut, SkiPatrolHQ):
  1. no ServiceUpgrade;
  2. ServiceObject = Parks and Recreation;
  3. PlaceableObject cost = the catalog row's BuildCost, XP = the XP the former ServiceUpgrade carried;
  4. CityServiceBuilding present;
  5. the Park component is kept;
  6. Workplace = the row's jobs (jobs 0 means no Workplace block);
  7. upkeep = the row's, and a roadless row (hut, summit, patrol huts) demands nothing (electricity, water, garbage 0);
  8. the mesh CID resolves to a mesh prefab's `.cid` in the mirror;
  9. the door spawn rows (Pedestrian Spawn Location: school 4, A-frame 1, Mid 5, Grand 5, the roadless four 0) and the sub-object
     `$rlength` equal the rows present;
  10. every declared `$type` id is unique.
