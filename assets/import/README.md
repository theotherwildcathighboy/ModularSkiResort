# Import asset tree

Export/import staging for CS2 Editor assets, organized `<category>/<generation letter>/<AssetName>/`.

- **towers/** — line + terminal towers. SHARED across lift types: fixed grip, and the future
  detachable and gondola tiers reuse these. The aerial tram gets its own category
  (`tramtowers/`) when built. `legacy/` holds pre-J iterations (FG2/FG3/FG4) whose numbering
  predates the generation-letter convention.
- **stations/`<lift type>`/** — terminal buildings, by lift type then letter. Currently only
  `fixedgrip/`. Detachable/gondola/tram stations get sibling type folders.
- **chairs/** — carriers. Chair1–5 in `legacy/` (pre-letter numbering); J = swan-neck Chair6.
  Future: detachable chairs, gondola cabins, tram cabins as new assets here or a `cabins/`
  sibling when the taxonomy grows.
- **cables/legacy/** — obsolete modelled-rope-era assets (the drawn line's anchored lanes
  replaced them). Kept for history.
- **props/** — piste-edge decoration placed automatically by the vanilla per-vertex
  border spawner on our run surfaces (DECISIONS 71/73; spec in
  `docs/research/art/snowmaking-props-spec.md`). Snow guns (tower / lance / carriage),
  hydrant + standpipe clutter, marker poles, hoses, B-net, trail signs, parked groomer.
  These are plain StaticObjectPrefabs — no utility or net components. Origin sits at
  ground level on the machine's own footprint centre; per-instance offset and yaw come
  from the prefab's SubObject transform, NOT from baked mesh offsets.

Current shipping generation per category: towers **N**, stations **K**, chairs **K**,
props **A** (first generation).
Generator `--out` paths must target the letter folder, e.g. `--out ../import/towers/N`.
Never reuse a generation letter (the Editor mints a new mesh CID per import; hand-made
prefabs repoint by CID — see docs/PIPELINE.md).
