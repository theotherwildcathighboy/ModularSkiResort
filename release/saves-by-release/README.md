# saves-by-release

One test city save per published release, kept so the NEXT release can be tested on it
(docs/research/release-update-constraints-2026-10-18.md section 3 rule 10, section 4 items 8 and 10).
Everything in this folder except this README is git-ignored (`.gitignore`, T74): saves are large binaries
that live on this machine only. Back the folder up with the rest of the machine.

## What to keep, per release

- The save, copied from `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Saves\<id>\`
  (the `.cok` and its `.cid`), renamed `<mod version>_pdx<PDX version>_<yyyy-mm-dd>_<what>.cok`, e.g.
  `1.0.0_pdx1_2026-11-02_testresort.cok`.
- A line in the table below: mod version, PDX version number (Paradox Mods counts 1, 2, 3 ...), game
  version, the layout line of release/CHANGELOG.md, and what the city holds.

The save must exercise every persisted thing: a Welcome Sign with bound runs (SkiRunBinding), glade trees
on a run (SkiRunTreeMember), sold season passes (SkiSeasonPass), at least two closed months of history plus
a month in progress (SkiResortHistoryMonth, SkiResortMonthInProgress), and one of every lift type with chairs
moving, a hosted tram, a lodge of each tier, a painted run of each surface. Make it in a NEW city on that
release (no older saves; memory "no save compat before release" ends at the first upload).

## Use

Before publishing release N+1, load the save of release N and run
docs/research/field/old-save-checklist.md (local docs) end to end. Record the result in the changelog line
"Save compatible with previous".

## Saves

| File | Mod | PDX | Game | Layout versions | Holds |
|---|---|---|---|---|---|
| (none yet: the first save is made on 1.0.0) | | | | | |
