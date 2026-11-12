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
| (none yet: the first save is made on 1.0.0) | | | | | |
