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

## 1.0.4 (unreleased)

- Save compatible with previous: yes
- Layout versions: Binding 1, TreeMember 2, SeasonPass 1, HistoryMonth 2, MonthInProgress 2, RunPropMember 1, RunPropSettings 1, LodgeNumber 1, TrackSnow 1, RunName 1, MergedLodge 1, TrackPack 2, RulesStamp 1, PassPlan 1, PassArea 1
- Do not load in older versions: yes

**Prices per ski area:**
- Each ski area has its own lift ticket price and its own season pass plan, set in its Finances view.
- A pass plan has an early price, a rise size (up to +50 %, at most 3 rises) and a 12-month strip of the months on sale and the months a price rise starts.
- Passes for the next season can go on sale from July. Late in the winter fewer households buy: the less winter is left, the fewer buyers.
- Once a season's sales start, its prices stay as planned. A change applies from the next season, and the Finances view shows it.
- A pass is good at the ski area that sold it. Passes of areas that use the default prices are good at every such area.
- Pass sales count at each household's nearest ski area.
- The price lines say in plain words how many guests a ticket price turns away and how many buyers a pass price puts off.
- With a lift ticket price of 0, the pass controls stay in place, dimmed: there are no pass sales while lift tickets are off.
- The Options prices are now the default for every ski area that has no price of its own. Lowering the default pass price after a season's sales started applies from the next season.
- Old saves keep today's prices and calendar until you change an area's prices. This version adds new save data: do not load its saves in 1.0.3 or older (their prices fall back to the Options values).

**Hub:**
- Long views turn pages with a footer row: Back on the left, dots in the middle, and a button on the right that names the next page (for example "Income and costs").
- The view stays on the rows you are looking at while you change a price.
- The Net row tooltip in History is short: year over year.
- The build strip arrow is the same size as the Lifts, Runs and Buildings buttons.

**Tourism:**
- Cold weather no longer keeps tourists away while a ski area is open: the game's cold cut on new tourists now starts at -30 °C instead of -10 °C (new option, on by default).

## 1.0.3 (PDX version 4, 2026-11-19)

- Save compatible with previous: yes
- Layout versions: Binding 1, TreeMember 2, SeasonPass 1, HistoryMonth 2, MonthInProgress 2, RunPropMember 1, RunPropSettings 1, LodgeNumber 1, TrackSnow 1, RunName 1, MergedLodge 1, TrackPack 2, RulesStamp 1
- Do not load in older versions: no

**Season passes:**
- Passes go on sale before the winter and sell for four months. The price rises 15 % halfway through.
- Sales end in the fifth month of the season. Guests who first arrive after that pay day tickets.
- Tourists can now ski on a destination pass. They pay a reduced day fee, never skip a ski day over the price, and the fee counts as pass income.
- The Finances view shows how long passes stay on sale, and the destination fee with the share of tourists on a pass.
- Pass texts in the Finances and Guests views are reworded ("On a pass", "Season passes sold").

**Lift tickets:**
- Tourist groups that the game sends on outings now react to the ticket price. Above the default ticket some walk away, about half at the highest ticket.
- The Guests view counts them as "Turned away by price".

**Lodges:**
- Lodge spending now follows the ticket price, damped: a higher ticket brings more per guest, but less than the ticket rises. At the default ticket nothing changes.

**History:**
- A revenue chart in the season block: income and costs by part on one scale, the net gap between them, and the pass share of lift income. Each part has a tooltip.

**Behind the scenes:**
- The mod now carries its real version number (1.0.3) instead of 1.0.0, so Skyve and the game show the right version.
- Saves from 1.0.0 to 1.0.2 load as before.

## 1.0.2 (PDX version 3, 2026-11-18)

- Save compatible with previous: yes
- Layout versions: Binding 1, TreeMember 2, SeasonPass 1, HistoryMonth 2, MonthInProgress 2, RunPropMember 1, RunPropSettings 1, LodgeNumber 1, TrackSnow 1, RunName 1, MergedLodge 1, TrackPack 2, RulesStamp 1
- Do not load in older versions: yes

**What you may notice on load:**
- Some runs may change difficulty band, mostly from black to blue, under the new rating rule (Runs, below).
- The season pass is now set as a price. Your Options value carries over as that price.

**Saves:**
- What you build from now on remembers the version that built it, so a later update can bring older objects up to date.
- A city saved with 1.0.2 still loads in 1.0.1, but that version forgets which version built what.

**Sync resort:**
- When an update changes how resort objects look, the Attraction view lists the out-of-date ones under "Sync resort". First: Patrol Huts from before 1.0.1 get their firewood pile.
- Sync is free, per object, and has no undo.

**Prices:**
- Sliders in the Finances view for the lift ticket price and the season pass price.
- The ticket price shows one demand word, from unaffected to very low. Its tooltip says how many fewer guests come.
- Season pass price: 20 to 2,000, default 400. Above 4 day tickets' worth, fewer households buy one.
- Season passes go on sale in August (the season's second month), whether or not anything is open yet. Households judge the resort as if it were fully open.
- Ticket tolerance reads the same everywhere: 40, 60 with a Chalet Lodge, 80 with a Swanky Summit or Grand Lodge.
- Tooltips name the lever, such as "build better lodges".

**Hub:**
- Sliders for snow gun and light spacing on runs, and for tree density.
- History: a "This season so far" block with totals, the best month, the average ticket and the income and cost mix, against last season.
- Snow report on the Welcome Sign card and in the status tooltip: summit and base, snow depth, temperature, new snow and season snowfall.
- "Closed for the summer" in the off-season, instead of "Too warm for snow".
- Lodges and the school closed for the off-season are no longer listed as a problem.
- "Ski-day cars not arriving" names the main cause: no route, stuck on the way, never left home, or out of time.
- The merge card shows only with the hub open. Tiers the city cannot pay for are blocked.
- The build strip keeps its size: a tab click never closes it, the chevron does.
- One height for all text buttons. The Resort Planner's features are check boxes.
- The Start card says a lift needs low-voltage power.
- The full problem list moves the view down instead of covering it.

**Tourism:**
- Resort tourism bonus: ski areas raise the whole city's attractiveness, as an airport does. Up to 10 % by default (Options, 0 to 20 %, 0 turns it off). It brings tourists to the city, not skiers to the runs.
- New Attraction view rows: tourists in the city, guests at resort hotels (merged buildings), and the tourist spending the ski area draws.
- "City tourist income" is now "Tourist spending" and shows under Tourism Overhaul too. It is spending at city businesses, so it stays out of the resort's net.

**Runs:**
- Blue runs are easier to get: a steep pitch makes a run black only when it is 40 m or longer. A narrow steep spot, such as a cat track bank, no longer does.
- While you draw, the rating reads "(so far)" until the outline is closed.
- Black and double black shading is easier to see in evening light.
- A run that costs more than the city has shows the game's no-money icon.
- Run labels change color when you switch the run grading.
- Runs that cannot be rated are pale orange, apart from the orange ski route.

**Snow:**
- Snow guns build a 2 m base, then stop, and start again below 1.95 m. Runs stay open longer through a thaw.
- Made snow melts at the game's own rate, on runs and cat tracks alike. A run and a cat track over the same height open together, at any game speed.
- Some narrow runs that never opened on made snow now do.
- A cat track pays snowmaking for the part of it the guns work on.
- After a load the snow guns continue as they were: no plume flash, no missed bill.

**Groomers and lights:**
- Groomers work from dusk to dawn. They arrive one after another, nearest first, and leave at dawn.
- Groomers leave a cat track below 80 % snow cover, so they no longer drive on grass.
- On short cat tracks, groomers rest at each end.
- Run floodlights, snow gun beacons and groomer lamps turn on at dusk, like street lights, instead of at 18:00.

**Lift tool:**
- Lifts can cross at 15°, down from 20°.
- Right click a tower to remove it. Right click elsewhere or Esc still steps back.
- A line the city cannot pay for says so.

**Buildings:**
- Placing a lodge, the ski school or a patrol building no longer opens the Happiness view.
- A merged building's goods trade no longer makes the ski area's efficiency jump off-season.
- The Ski Bus roof roundel reads right from every side.
- Menu and Find It icons show the building alone, like vanilla.

**Options:**
- The ski area radius is measured from the Welcome Sign in a straight line, not along roads.

## 1.0.1 (PDX version 2, 2026-11-16)
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
