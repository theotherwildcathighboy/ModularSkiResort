# Changelog

Newest release first. Every release entry is a `## X.Y.Z (PDX version N, YYYY-MM-DD)` heading followed by the
three fixed lines, then the notes. `tests/SaveBatch` (SL-CL) checks them: the newest entry's "Layout versions" line
must equal `SaveLayouts.ChangelogLine` (mod/SkiResortMod/Systems/SaveLayouts.cs), and an entry whose layouts differ
from the previous entry's must say "Do not load in older versions: yes". Rules: docs/research/release-update-
constraints-2026-10-18.md section 3 (patch rules) and section 4 item 9; the test-from-old-save checklist is
docs/research/field/old-save-checklist.md (local docs).

- **Save compatible with previous**: yes = a save made on the previous release loads with nothing lost (checklist
  run); no = say what is lost and why. `n/a` only on the first release.
- **Layout versions**: copy `SaveLayouts.ChangelogLine` (SaveBatch prints a FAIL with the exact text if it differs).
- **Do not load in older versions**: yes whenever any layout version went up (players can pick an older version on
  Paradox Mods; an older build reading a longer block throws "Data size mismatch"), or a new persisted type or a new
  prefab that an older build would turn into a placeholder was added. Then say it in the notes too.

Template (copy above the newest entry):

```
## X.Y.Z (PDX version N, YYYY-MM-DD)
- Save compatible with previous: yes
- Layout versions: Binding 1, TreeMember 2, SeasonPass 1, HistoryMonth 2, MonthInProgress 2, RunPropMember 1, RunPropSettings 1, LodgeNumber 1, TrackSnow 1, RunName 1, MergedLodge 1
- Do not load in older versions: no

Notes...
```

## 1.0.1 (PDX version 2, YYYY-MM-DD)
- Save compatible with previous: yes
- Layout versions: Binding 1, TreeMember 2, SeasonPass 1, HistoryMonth 2, MonthInProgress 2, RunPropMember 1, RunPropSettings 1, LodgeNumber 1, TrackSnow 1, RunName 1, MergedLodge 1, TrackPack 2
- Do not load in older versions: yes

**Resort Planner:**
- No more long green lines outside the region while planning.
- The lift list scrolls, and the steps read in a clearer order.
- The planner stays open when you build a lift that needs hand placement.
- No station on top of another station.
- Runs reach the run they join, and cat tracks come out in one piece.
- The planner is on by default.

**Hub:**
- Fly to an unfinished lift.
- Click a run to go to it.
- Mountain stats.
- Clearer wording when the ski school is not working, for example off-season.
- Warnings say "open" when the runs exist but are all closed, for example "no open green run or cat track".
- The Mountain section labels its number: "Vertical drop".
- An open cat track counts as the ski school's run. Cat tracks do not count toward Variety.

**Units:**
- Degrees for slopes everywhere.
- Four display options: run grading, lengths and elevation, and weather are Auto by default (grading follows the city's map theme, the units follow the game's setting). Slope shows degrees by default.
- European grading: green, blue, red, black, ski route.
- Imperial lengths switch from yards to miles at 500 yd.

**Assets:**
- Staff Only signs on the doors.
- New flags.
- Patrol Hut firewood.
- 3D icons.
- The Chalet, A-Frame and Grand run off-grid when a cat track links them to a road lodge or bus depot.
- New thumbnail and launcher icon.

**Season:**
- A run or cat track needs real snow to read Open, with clearer snow text.
- Snow guns on cat tracks now work like the guns on runs: they follow the air temperature, and they need about one game hour to build the snow.
- The planner's tree filler keeps out of lift corridors, the tram's too.

**Economy:**
- Ticket default 80 to 100, season pass x2 to x4.
- Lodges and the school close off-season (an option).
- Cat tracks pay snowmaking only while their guns make snow.
- Guests without a car who live far from every lodge start at the bus depot.

**Ski way:**
- A narrow grading mode in the cat track tool: the Ski way button in the hub's Runs tab, or Ctrl+Shift+J.

**Behind the scenes:**
- Dev-only screens out of the release build.
- Lift towers keep their saved shape on load.
- Less log noise.
- Saves from 1.0.0 load fine.

## 1.0.0 (PDX version 1, 2026-11-12)
- Save compatible with previous: n/a (first release)
- Layout versions: Binding 1, TreeMember 2, SeasonPass 1, HistoryMonth 2, MonthInProgress 2, RunPropMember 1, RunPropSettings 1, LodgeNumber 1, TrackSnow 1, RunName 1, MergedLodge 1
- Do not load in older versions: no

First public release. Every feature, number and setting is on the docs site: https://theotherwildcathighboy.github.io/ModularSkiResort

## Beta notes (pre-release, not a release entry)
Early beta builds only: the first working chairlift network (drawn line, station snapping, sagging cable) and snow-line
groundwork. Everything above came after. Beta saves are not supported by the first public release.
