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
- Layout versions: Binding 1, TreeMember 2, SeasonPass 1, HistoryMonth 2, MonthInProgress 2, RunPropMember 1, RunPropSettings 1, LodgeNumber 1, TrackSnow 1, RunName 1
- Do not load in older versions: no

Notes...
```

## 1.0.0 (PDX version -, unreleased)
- Save compatible with previous: n/a (first release)
- Layout versions: Binding 1, TreeMember 2, SeasonPass 1, HistoryMonth 2, MonthInProgress 2, RunPropMember 1, RunPropSettings 1, LodgeNumber 1, TrackSnow 1, RunName 1, MergedLodge 1
- Do not load in older versions: no

First public release. Draft notes (check against the build at upload):

- Four lift types: fixed-grip triple chairlift, detachable quad chairlift, gondola and aerial tram. Chairs and cabins move
  along the cable; the tram shuttles two cabins.
- One lift tool: click the base station and the top station, click the line to add a tower, drag to move one, Enter builds.
  The tower planner fits tower heights to the terrain with few towers, prefers short towers next to the stations, and
  draws spans that are too steep in red. Towers are numbered.
- Ski runs you draw: natural, groomed, snowmaking, night and cat track, each rated green to double black from its slope.
- Cat track tool: lay out a track, and one key grades the ground under 25 percent and builds the cat track on it. Where it
  crosses a run it grades up to 2 m inside the run and keeps the run's difficulty. Delete mode (Ctrl+Shift+D) and a hub trash
  button remove a track; right click and Esc never do.
- Resort Planner (experimental): outline a slope of up to 9 km2 and the planner proposes lifts and a set of linked runs
  around them, grouped in pods, with each lift's way home (green, blue, black only, or ride the lift down). Leave out runs,
  pods or lifts, set the number of lifts, the way home, the mix and the run kinds, then build the runs in one go and undo
  them; the proposed lifts you build yourself with the lift tool. The scan runs in the background with a progress line, and
  right click stops it. Optional tree filler. Ctrl+Shift+Q; it is off by default and is turned on in Options.
- Safety Net Fence: decorative orange netting on thin poles, in the hub's Runs tab.
- Snow-depth run system: runs open at 3 ft (91 cm) on 60 percent of the run and stay open while 1 ft covers 40 percent, snow guns on snowmaking and night runs
  (two months before winter to one month after), powder days, seasons.
- Snow guns, hydrants and floodlights stand at a set distance along a run's edge, however its outline was drawn, with two values per run in the
  Runs view. Cat tracks carry guns and hydrants on one side, and pay snowmaking upkeep by plan area while their guns can run; with the gun value at Off, none, and snow already made stays until it melts.
- Welcome Sign (set back 8 m from the road on a leveled lot), five lodges, Ski School, two patrol
  buildings.
- The Welcome Sign is the ski area: runs, lifts and buildings within its radius belong to it (5 km by default, 2 to 10 km
  in Options). While placing a sign, a ring shows the radius and the tooltip gives the height under the cursor, the lowest and
  highest dry ground in the ring, the vertical above the winter snow line, and how much ground lies above it. A selected sign shows its ring too.
- Lodge parking: car spaces within 600 m of a lodge with a road count for it (street parking, parking lots, a parking garage
  at its entrance); tourist parties drive in and need them. A blue ring shows the 600 m while placing or selecting a lodge.
- Ski Bus Depot: a coach stop in town for the nearest ski area, at any distance. A home closer to it than to a lodge starts
  its ski day there; guests pay as at a lodge. It adds no capacity and no score, needs a lodge on the hill, and bills 1,500
  a month plus 300 per km to that lodge (Finances, row Ski bus). An oxblood coach, with SKI BUS painted on it, drives between the depot and the hill. Selecting the coach or the depot shows a Ski Bus card beside the game's panel: guests by bus this month, the coach bill and the distance to the hill lodge.
- Merge an existing building into a ski area as a lodge: a park, landmark, signature or decor building near the runs can be merged as
  a lodge tier its lot allows. It keeps its own use and adds score, skier room, staff and attraction, for a one-time charge and a
  monthly fee. The Merge tile on the hub's Buildings tab (and a switch in the Overview's Merged buildings fold) outlines the
  buildings near the runs and lifts that can be merged, green when a lodge tier fits the lot and gray when the lot is too small;
  the merge card beside the info panel says whether a building is near enough to count before you pay.
- The Ski Resort panel: Attraction, Runs, Lifts, Guests, Finances and History views, a problem list and a build strip.
- Economy: lift tickets, season passes, lessons, lodge spend, off-season half staff.
- Delete all ski resort content, a button in the mod's Options (Reset group): removes everything the mod placed in the
  loaded city, after a confirmation, to clean a city before uninstalling. It cannot be undone.
- Copy diagnostics, a button in the mod's Options (Diagnostics group): puts the versions, the changed options, the code mods and the latest log errors on the clipboard for a bug report.
- Works on the base game; no other mod is required. Move It is highly recommended and Write Everywhere is recommended.
- Known limitations: a hill far from the city's homes and hotels gets few ski-day guests (the panel shows the distance), chairs carry no riders, two lifts of the same type cannot cross, a cat track cannot pass under a lift that is already built, stations can sit partly in the hillside on steep
  ground, chairlifts and gondolas stop in summer.

## Beta notes (pre-release, not a release entry)
Early beta builds only: the first working chairlift network (drawn line, station snapping, sagging cable) and snow-line
groundwork. Everything above came after. Beta saves are not supported by the first public release.
