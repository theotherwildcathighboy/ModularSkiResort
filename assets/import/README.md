# Import asset tree

Export/import staging for CS2 Editor assets, organized `<category>/<generation letter>/<AssetName>/`.

- **towers/** — line + terminal towers. SHARED across lift types: fixed grip, and the future
  detachable and gondola tiers reuse these. The aerial tram gets its own category
  (`tramtowers/`), now built. `legacy/` holds pre-J iterations (FG2/FG3/FG4) whose numbering
  predates the generation-letter convention.
- **stations/`<lift type>`/** — terminal buildings, by lift type then letter: `fixedgrip/`,
  `detachable/A` and `gondola/A` (`lift_terminal_detach.py`, the gondola one = draft 1 of
  2026-10-11, pre-verdict), `tram/A` (`tram_station.py`).
- **chairs/** — carriers. Chair1–5 in `legacy/` (pre-letter numbering); J = swan-neck Chair6.
  `M/` holds the fixed-grip triple (`SkiLiftChair6M`) and the detachable quad (`SkiLiftChairDQ4M`);
  the gondola and tram cabins are in the `cabins/` sibling.
- **cables/legacy/** — obsolete modelled-rope-era assets (the drawn line's anchored lanes
  replaced them). Kept for history.
- **props/** — piste-edge decoration placed automatically by the vanilla per-vertex
  border spawner on our run surfaces (DECISIONS 71/73; spec in
  `docs/research/art/snowmaking-props-spec.md`). Snow guns (tower / lance / carriage),
  hydrant + standpipe clutter, marker poles, hoses, B-net, trail signs, parked groomer.
  These are plain StaticObjectPrefabs — no utility or net components. Origin sits at
  ground level on the machine's own footprint centre; per-instance offset and yaw come
  from the prefab's SubObject transform, NOT from baked mesh offsets.

- **lodges/** — resort lodges from `assets/blender/lodge.py` (recipe engine on the
  `cs2_bldg.py` kit; recipes in `bldg_recipes.py` + `recipes/`). `A/` = the legacy
  `base_lodge.py` A-frame; `B/` = the 2026-10 lodge ladder: L1 `SkiLodgeAFrameCommercialLowB`
  (prefab SkiAFrameDayLodge), L2 `SkiLodgeHutCommercialLowB` (SkiLodgeHut), L3
  `SkiLodgeChaletCommercialLowB` (SkiLodgeMid), L4 `SkiLodgeSwankyTopCommercialLowB`
  (SkiLodgeSummit), L5 `SkiLodgeSwankyBottomCommercialLowB` (SkiLodgeGrand). Mesh names and
  prefab names differ on purpose; the prefab names are the code catalog's
  (`SkiCatalog.Names`). The `_Win` interior keyword (`CommercialLow`) is part of the asset
  name on purpose (the importer matches the FILE name).
- **buildings/** — non-lodge resort buildings from the same engine, letter `A`:
  `SkiSchoolCommercialLowA` (SkiSchool), `SkiPatrolHutCommercialLowA` (SkiPatrolHut),
  `SkiPatrolHQCommercialLowA` (SkiPatrolHQ). All use the `CommercialLow` keyword (civic-sign
  D11; the kit contract's `Office` for patrol was not built).
- **signs/** — `SkiWelcomeSignA` from `assets/blender/welcome_sign.py`: the mesh for
  SkiResortGate's `m_Meshes` (R14). No `_Win`. Its Write Everywhere layout lives in
  `assets/we/` (data only, optional). `B/` = the shared sign sets (`SkiSignSet{Gate,Grand,Hut,
  PatrolHQ,PatrolHut,School}B`, one 2048 sheet at 300 px/m, `cs2_signset.py`).
- **fences/** - `A/SkiSafetyNetA` (the orange safety-net fence: the 4 m lane-mesh tile the
  vanilla curve shader repeats along a drawn fence) and `A/SkiNetPostA` (its end post), from
  `assets/blender/safety_fence.py` (+ `safety_fence_stage.py`, the staged prefab JSON under
  `assets/staging/prefabs/fences/`); design `docs/research/art/assets-2026-10/design/safety-fence.md`.
  Nothing imported yet: S1 step 0 is the JSON-only probe.
- **cabins/** - `A/SkiGondolaCabin8A` and `A/SkiTramCabinA` (`lift_cabin.py`): the gondola and
  tram carriers.
- **tramtowers/** - `A/SkiTramTower{30,45,60}A` (`tram_tower.py`): the aerial tram's lattice
  towers, a separate category from the shared `towers/`.
- **surfaces/** - `A/SkiRunGroomedA`, `A/SkiRunSnowmakingA` (`ski_surface.py`): run-surface
  textures plus hand-made SurfacePrefab staging; see `surfaces/A/README.md`.

Current generation per category (nothing from the 2026-10 round is imported yet, STATE section 1):
chairs **M**, stations **A** (detachable, gondola, tram) and **M** (fixedgrip), towers **O**,
tramtowers **A**, cabins **A**, lodges **B**, buildings **A**, signs **A** (welcome sign) and
**B** (sign sets), props **A** (snow guns, floodlights, hydrant boxes, the red beacon) and **B**
(dressing kit), probes **B** (import-behaviour probes, not shipped), fences **A**, surfaces **A**.
Generator `--out` paths must target the letter folder, e.g. `--out ../import/towers/N`.
Never reuse a generation letter (the Editor mints a new mesh CID per import; hand-made
prefabs repoint by CID — see docs/PIPELINE.md).

Superseded exports (the pre-pass copies of the 2026-10-11 to 2026-10-13 round) live under
`assets/archive/2026-10-11-13/import/` (moved from `assets/import/archive/`; index in
`docs/research/art/assets-2026-10/archive/INDEX.md`; git-ignored, never imported).
`archive/superseded-rev2-2026-10-11` stays here because `assets/blender/tests/test_tram_station_determinism.py`
(T4) reads it; the generators' manifest scans skip any folder named `archive`.
