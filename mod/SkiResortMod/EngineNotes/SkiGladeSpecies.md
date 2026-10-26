# SkiGladeSpecies: tree species groups for the Quick runs tree filler

Research: docs/research/autogen-2026-10-25/build/C-tree-filler.md (species section). Pure; linked into tests/GladeBatch (`species/*`).

## What the data supports (VERIFIED, 1.6.0f1 decompile + vanilla prefab dump; 1.6.2f1 identical for these classes)

- A tree prefab = `TreeObject` (adds PlantData, TreeData, GrowthScaleData; Game.Prefabs/TreeObject.cs:12-30). `TreeData` holds only
  `m_WoodAmount`. Bushes are `PlantObject` (PlantData only), except four vanilla "Wild" bushes that carry TreeObject with wood 0
  (GreenBushWild01/02, FlowerBushWild01/02); real trees have wood 2000 or 3000.
- No field encodes a botanical family. `SpawnableObject.m_Placeholders` (the wild random-tree placeholder mixes Pine, Spruce, Birch,
  Oak, Alder and Hickory), `ThemeObject` (EU_ / NA_ regional only, Game.Objects/PlaceholderSystem.cs:168-202) and the single shared
  UIObject group are not families.
- 15 vanilla trees: conifer Pine, Spruce; deciduous Birch, Oak, Apple, EU Alder / Chestnut / Poplar, NA Hickory / Linden / London
  Plane; palm Coconut, Florida, Royal, Sylvester. No DLC pack adds a tree.

## Rule

`GroupOf(name, woodAmount)`: the vanilla table by exact name; else the name's lower-case words (split at '_', '-', ' ', '.', digits and
lower-to-upper case changes) against keyword lists, bush (or wood 0) first, then palm, conifer, deciduous; else `Ungrouped`. Whole
words only ("FirewoodStack" is not a fir). Asset-pack names are a heuristic (UNVERIFIED: no mod trees on the test machine).

`ApplyPool(ref p, n)`: SpeciesCount n, DominantShare 1/n = every pool member equally likely (the planner's "index 0 with p, else
uniform over the rest").
