# saves-by-release

This folder keeps one test city save per published release. You test the NEXT release on it
(docs/research/release-update-constraints-2026-10-18.md section 3 rule 10, section 4 items 8 and 10).
Git ignores everything in this folder except this README (`.gitignore`, T74). Saves are large binaries
that live on this machine only. Back the folder up with the rest of the machine.

## What to keep, per release

- The save. Copy it from `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Saves\<id>\`
  (the `.cok` and its `.cid`). Rename it `<mod version>_pdx<PDX version>_<yyyy-mm-dd>_<what>.cok`, e.g.
  `1.0.0_pdx1_2026-11-02_testresort.cok`.
- A line in the table below with these items: mod version, PDX version number (Paradox Mods counts 1, 2, 3 ...), game
  version, the layout line of release/CHANGELOG.md, and what the city holds.

The save must exercise every persisted thing:

- a Welcome Sign with bound runs (SkiRunBinding)
- glade trees on a run (SkiRunTreeMember)
- sold season passes (SkiSeasonPass): play through a pass sale, in season
- at least two closed months of history plus a month in progress (SkiResortHistoryMonth, SkiResortMonthInProgress)
- snow guns and lights on a run (SkiRunPropMember, SkiRunPropSettings)
- a cat track with snowmaking (SkiTrackSnow)
- runs with names from the hub (SkiRunName)
- two or more lodges, so they carry numbers (SkiLodgeNumber), and one merged lodge (SkiMergedLodge)
- one of every lift type with chairs moving, a hosted tram
- a lodge of each tier, a painted run of each surface, one run and one lift built by the Resort Planner
- the ski bus depot with a working route

Every row of `mod/SkiResortMod/Systems/SaveLayouts.cs` must appear in the save: a new persisted type means a new item here.

Make it in a NEW city on that release, with the PUBLISHED build (subscribed from Paradox Mods, dev copy parked), not a
dev build. Use no older saves. After the copy, set the file read-only: a save loaded and saved again on a newer build is
no longer a save of that release.

## Use

Before publishing release N+1, load the save of release N and run
docs/research/field/old-save-checklist.md (local docs) end to end. Record the result in the changelog line
"Save compatible with previous".

The machine part is one command in the dev setup, with the game at the main menu:
`powershell -NoProfile -ExecutionPolicy Bypass -File tools\loadcheck.ps1 -Resave`. It loads a copy of the newest save here (never the
file itself), prints PASS / FAIL for the log scan, the entity census, one month of billing and the save-and-reload round trip, and
keeps a census baseline beside the save as `<save name>.census.json` (git-ignored like the save). Its first run writes the baseline;
later runs compare against it. The steps it cannot check stay manual (marked in the checklist).

## Saves

| File | Mod | PDX | Game | Layout versions | Holds |
|---|---|---|---|---|---|
| `1.0.0_pdx1_2026-11-14_light.cok` | 1.0.0 | 1 | 1.6.4f1 | Binding 1, TreeMember 2, SeasonPass 1, HistoryMonth 2, MonthInProgress 2, RunPropMember 1, RunPropSettings 1, LodgeNumber 1, TrackSnow 1, RunName 1 | Mountain Village, playset "Ski Light" (Placemaker the only other mod). Town of about 1,000, power and water placed by hand (water short). Welcome Sign, 6 runs, fixed chair (planner), detachable and gondola, A-frame and 2 huts (one merged), 4 cat tracks, bus depot, ski school. Saved Aug 2027 after a full winter. All 12 persisted types present. Gaps: no tram, not every lodge tier, one surface only. |
| `1.0.0_pdx1_2026-11-14_modded.cok` | 1.0.0 | 1 | 1.6.4f1 | (same) | Lakeland, playset "Standard Mod Set" (204 mods). Placemaker town, gas power, groundwater. Welcome Sign, 5 runs (1 green, 2 blue, 2 black), gondola up from the lake (ground-cable power) and a detachable, summit hut with cat track, A-frame (merged), school, bus depot, glade level 8 on a black run (164 trees). Saved Jan 2027, resort open. All 12 persisted types present. Gaps: no tram, no planner run, not every lodge tier, one surface only; the hub shows staff and lift-geometry warnings. |
| `1.0.0_pdx1_2026-11-14_ashflat.cok` | 1.0.0 | 1 | 1.6.4f1 | (same) | Ash Flat, the owner's test city, built on the published 1.0.0. **Dev-ID save (T198):** it carries the prefab IDs of the ImportedData copies, so its lifts, stations, lodges and runs resolve only with the pack overlay or ImportedData present. With ImportedData parked the mod sees 0 lifts. Welcome Sign, 93 bound runs (34 groomed, 14 natural, 45 cat tracks with snowmaking), every lift type (54 sections, 16 stations, 2 trams), every lodge tier (A-frame, Chalet, Summit, Grand, Hut), Ski Patrol HQ and 2 huts, ski school, bus depot with its coach, 6,533 glade trees, 9,131 season passes. Census: 11 persisted types. Gaps: one closed month of history only. |

The light and modded saves were checked with `loadcheck.ps1 -NoLoad -WriteBaseline -NoMonth` (Bridge mode, Standard Mod Set): log
scan PASS, census baseline written. Ash Flat has a census baseline from its first `loadcheck.ps1` run (2026-11-14, mod build
1.0.0+813b5335). Load it only with the pack overlay or ImportedData present (dev-mode without `-ParkImported`).
