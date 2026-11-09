# Modular Ski Resort: best practices (player guide draft)

*A short version of the docs site's guides. For the current, complete guides and every number, see the docs site. Sources: release/LISTING-NOTES-2026-10-22.md.*

## First resort, in order

1. Open the Ski Resort panel (button at the top left). On the **Buildings** tab, place the **Welcome Sign** with its front on a road. To move a sign later, use **Relocate** in its info panel, because bulldozing a sign deletes every run linked to it.
2. Place a **lodge** within 800 m of where the runs will be. Ski-day guests only come to a lodge that close. Any lodge works, and the A-Frame Day Lodge is the cheapest with real capacity. No lodge, no attraction score and no ski days.
3. On the **Lifts** tab, pick a lift type. Place the **base station** at the bottom and the **top station** at the top, facing the base station. A guide line shows when they are straight.
4. Click the base station with the lift tool, click the top station, then press **Enter**.
5. Connect a **low-voltage power line** to the base station (see Power).
6. On the **Runs** tab, pick a run type and draw runs downhill from the top station.
7. Wait for snow. The status line reads *Open* when runs open.

If the panel shows a problem line ("No lift yet", "Lodge or school: road required"), click it. It flies to the cause.

## Where to build

- Snow decides everything. While you place a sign or draw a run, the game shows the average winter snow line and a fainter line below it. The fainter line marks where snow guns start to work (option, on by default). Build on or above both.
- A run needs snow to open: 3 feet (91 cm) over 60 percent of it. Snowmaking and night runs make their own snow where it is cold enough, from two months before winter to one month after.
- A mountain with no real winter needs the option **Snow & season gating** turned off. Then every run counts as open all year.
- Ski-day guests drive from homes and hotels, and few make a trip beyond about 7 km. A hill far from the city needs homes or hotels nearby. The panel gives the distance when it is too far.

## Lifts

- **Lift tool.** Click the line to add a tower. Drag a tower to move it. It stays where you put it. Drag a tower off the line to remove it. Right click or Esc steps back one action. Enter builds. Ctrl+Shift+L opens the tool from the keyboard.
- **Let the planner work first.** It picks tower heights to fit the ground and uses the fewest towers the lift type allows. Move towers only where you want them. The preview colors the cable by how far it clears the ground.
- **Steep or uneven ground.** Build one section at a time. To fix one section, bulldoze it and click its two ends again. The other sections keep their towers, cable and chairs.
- **Pick the type for the terrain.** Steepest slope: fixed-grip 80 percent, detachable 90, gondola 120, tram 250. The tram reaches farthest between towers (up to 3,000 m with the lift tool).
- **Stations cut a level pad.** Chairlift and gondola stations cut the slope above them level and fill nothing. A base station cuts 12 m past its lot on the line side. The tram base terminal levels a pad, cut and fill. The tram top terminal keeps the slope. Put stations on reasonably level ground, or terraform first.
- **Push a lift across a canyon or up a cliff face and the result can look wrong**, as vanilla power lines do in the same places. Use the preview.
- **Do not cross another lift.** The game joins two lines where they cross, and the other lift's chairs stop. The tool shows a notice before you build. Bulldozing the new section merges the other lift back.
- The mod numbers towers up the line from the base station.

## Power

Lifts are power-line networks, so they follow power-line rules.

- Lift lines show in the electricity overlay as high-voltage lines.
- The base station acts as a transformer. Wire a **low-voltage line** into the stub at the front corner of the base station. The line carries the power up the mountain. The top station needs no hookup.
- No power, no lift. The panel lists it as a problem.
- City high-voltage lines cannot join a lift tower, stub or lift line: the game refuses them. Low-voltage lines still power a lift.
- **Before uninstalling the mod, bulldoze your lifts and runs** (or use Options > Modular Ski Resort > Reset > **Delete all ski resort content**). Then save the city. Otherwise run paint can stay in the saved terrain. What leftover lift pieces do after removal is untested.

## Runs

- Draw runs from the top of a lift downhill. The rating comes from the hardest unavoidable pitch, so a flat runout does not hide a steep drop.
- A run counts at full value only while a running lift reaches its top. Without one it counts as backcountry, at 40 percent by default.
- Mix green, blue and black runs. A resort with all three scores more.
- **Cat tracks** link runs across the hill and are always green. Use the cat track tool (the **Cat track** tile on the Runs tab, or Ctrl+Shift+K). Click points up the hill and press Enter. In one action the tool cuts and fills the ground so the track never climbs steeper than the max grade (25 percent by default). Then it builds the cat track on the graded strip. Delete mode (Ctrl+Shift+D while the tool is open, then click the track twice) removes a built track. The ground stays graded.
- The **tree tool** plans glades on and around runs. Ctrl+Shift+G toggles it.

## Ski School

- A Ski School adds to the attraction score only while a green run counts. It also sells lessons.
- Terrain tip: level the ground at the top of the magic carpet area, then use the slope tool down to its base.

## Money

- Lifts, runs, lodges, the Ski School and staff cost money all year. Income comes with snow.
- Default lift ticket price is 80. Above 40 it lowers the number of ski days. The Guests view shows what limits them.
- Off-season, lodges and the Ski School drop to half staff on their own after 12 game hours with no open run. Building upkeep keeps running, so consider switching idle lodges off in their info panel.
- Season passes sell to residents once per season. Tourists never buy one.
- The game's Economy panel has no ski resort line. The game books resort income as public transportation fees. The full picture is in the panel's **Finances** view.

## Playing nice

- Move It is useful (not required). It places stations and buildings more precisely than the game's own tools, and it works on every building in the pack.
- Write Everywhere is recommended (not required). It writes your ski area's name on the Welcome Sign. Without it the sign shows its "SKI AREA" header over a plain name board.
- Recoloring: stations and buildings recolor with the game's own color tool (select the object). Towers on a lift line cannot be selected, so the optional Recolor mod's painter is the way to paint them.
- Start from a new city. The first public release makes no promise for saves from test builds.
