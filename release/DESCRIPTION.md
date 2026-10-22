# Modular Ski Resort — listing copy (draft)

*Paradox Mods long description. Keep claims matched to shipped features; update at upload. Every claim is traced to its source in release/LISTING-NOTES-2026-10-22.md.*

---

**Short description:** Build a ski resort: drawn chairlifts, gondolas and an aerial tram, runs on the game's own snow, lodges and a resort economy. Base game only.

---

**Build a ski resort in Cities: Skylines II.**

Place a Welcome Sign, add a lodge, draw a lift up the mountain and runs down it. Chairs and cabins ride the cable. Runs open when real snow lies on them. Guests ski, buy tickets and lessons, and the resort draws tourists to your city.

No other mod is required. Everything works on the base game.

**Lifts**
- Four lift types: fixed-grip triple chairlift, detachable quad chairlift, gondola and aerial tram. Chairs and cabins move along the cable. The tram shuttles two cabins between its terminals.
- One lift tool for all four. Pick a type, click the base station, click the top, and the tool plans the line over your terrain. Click the line to add a tower, drag a tower to move it (it stays where you put it), press Enter to build.
- The tower planner fits tower heights to the ground and uses the fewest towers the lift type allows. The preview colors the cable by how far it clears the ground, so you see a bad line before you build it.
- Towers are numbered up the line from the base station.
- Draw a lift one section at a time on steep ground. Bulldoze or redraw a single section; the rest keeps its towers, cable and carriers.

**Runs and snow**
- Draw runs with the area tool: natural, groomed, snowmaking, night and cat track.
- Every run gets a rating from the slope of its hardest unavoidable pitch: green, blue, black or double black. A slope heat map and a rating tooltip show while you draw.
- Every run opens at 3 feet (91 cm) of snow, natural or made, once it covers 60 percent of the run. It closes again below 40 percent.
- Snow comes from the game's own snow simulation. Snowmaking and night runs add snow guns where the air is cold enough, and keep the run at about 3 feet. The guns run October through March.
- Night runs are floodlit and worth 25 percent more to the attraction score.
- Fresh snowfall in the last day makes a powder day, and more locals go skiing.
- Seasons: snow melts in spring, runs close, and lifts stop while their ski area has no open run. The aerial tram keeps running in summer as sightseeing.
- Cat tracks link runs across the hill. A cat track grading tool cuts a graded path across a slope, never steeper than the grade you set (25 percent at most).
- A tree tool plans glades on and around runs, and a piste map draws every run on the ground in difficulty colors.

**Buildings**
- Five lodges: Mountain Hut, A-Frame Day Lodge, Chalet Lodge, Swanky Summit Lodge and Swanky Grand Lodge.
- A Ski School and two patrol buildings (Ski Patrol Hut and Ski Patrol HQ).
- The Welcome Sign, which starts a ski area. Runs, lifts and buildings within 5 km belong to the nearest sign.
- Station and lodge art follows real places; see Credits.

**The Ski Resort panel**
- Open it from the Ski Resort button at the top left. Everything in the mod is built from this panel; nothing is added to the game's build menus.
- Six views: Attraction (the overview, with the score broken down), Runs, Lifts, Guests, Finances and History.
- A problem list names what is wrong ("No lift yet", "Lift has no power") and where. Click it to fly there.
- A build strip along the bottom has three tabs: Lifts, Runs and Buildings.
- History charts guests and money by month, by season and over the last 12 months.

**Economy**
- Lift tickets are paid at the gate. Default price 80; you can set it from 0 to 200.
- Season passes: resident households decide once per season whether to buy. Default price is 2 lift tickets. Tourists never buy a pass.
- Lessons: a working Ski School sells lessons to 30 percent of skiers by default, at 1.5 lift tickets.
- Lodges earn lodge spend from each paying guest.
- Lifts, runs, lodges, the school and staff all cost money. Lifts bill monthly; runs bill per hectare while snow covers them.
- Off-season, lodges and the Ski School drop to half their staff after 12 game hours with no open run, and come back when a run opens (an option, on by default). Building upkeep is not reduced.
- Resort income is booked as public transportation fees (an option, on by default). The Economy panel has no ski resort line of its own; the Finances view shows the full picture.

**Tourism**
- Each ski area gets an attraction score from 0 to 200 on the game's own scale. The best lodge sets the base. Runs count by difficulty, size and type, lifts by type, and the Ski School, variety of runs and vertical drop add to it. Welcome Sign efficiency (staff, power, water and so on) multiplies it.
- The score feeds the game's tourist formula, so a bigger ski area brings more tourists to the city.
- Locals and hotel tourists also take ski days: day trips to a lodge, shaped by the score, fresh snow, distance and the ticket price.

**How to build (short version)**
1. Open the Ski Resort panel. Buildings tab: place the Welcome Sign at a road.
2. Buildings tab: place a lodge close to where the runs will be.
3. Lifts tab: pick a lift type and place the base station at the bottom and the top station at the top, facing the base station. A guide line shows when they are straight.
4. Click the base station with the lift tool, click the top station, adjust the towers if you want, press Enter.
5. Connect a low-voltage power line to the base station.
6. Runs tab: pick a run type and draw runs downhill from the top station.
7. Wait for snow. When runs open, the panel shows them open and the tourists start to come.

A full guide is on the mod page.

**Honest notes**
- Power: lifts are power-line networks. They show in the electricity overlay, and the base station acts as a transformer. Wire a low-voltage line into the stub at the front corner of the base station. The line carries the power up to the top station. No power, no lift.
- Do not connect city high-voltage lines to a lift tower or lift line. This is untested and can break the lift.
- Remove all lifts before uninstalling the mod. What happens to leftover lift pieces after removal is untested.
- Each lift type has a steepest slope (fixed-grip 80 percent, detachable 90, gondola 120, tram 250), a longest span between towers and a tallest tower. The tram spans the most, 1,000 m. Push a lift across a canyon or up a cliff face and the result can look wrong, as vanilla power lines do in the same places.
- Stations sit on the slope; they do not flatten the ground. The tram base terminal does level a pad. Each building's tooltip says whether it levels the ground. Site stations on reasonably level ground, or terraform first.
- Skiers are counted, not simulated. Chairs and cabins move, but they carry no passengers, and nobody skis down the runs. Lift capacity is a design figure.
- The cable moves in the wind; the chairs do not sway with it (engine limit).
- All chairs show the same number. Tower numbers run 1 to 200, then start over at 1.
- Resorts cost money all year and earn in winter. Build in steps.
- Saves: this is the first public release. Start from a new city. Every later update's changelog says whether saves made on the previous version still load.

**Other mods**
Nothing else is required. The mod works fully on its own.
- Move It works on every building.
- If Tourism Overhaul, Realistic Workplaces and Households or a trip-planner mod such as Realistic Path Finding is installed, the panel shows a notice. The mod changes nothing in them and does not depend on them.
- If Write Everywhere is installed, the Welcome Sign shows your ski area's name.

**Credits**
- Modular Golf Course Pack by joergeske: the modular recreation idea this mod grew from.
- Meapy's Tourism Overhaul: studied for how tourism and attractions work. Not required.
- Yenyang's Recolor: the color convention the stations follow. Optional; its painter recolors line towers.
- krzychu124's Scene Explorer: debugging during development.
- klyte45's Write Everywhere: optional name on the Welcome Sign.
- The fixed-grip lift is a tribute to the Pallavicini lift at Arapahoe Basin. More places are in the full credits.
- Built with Claude (Anthropic) for decompilation, prefab work and asset generation; direction and testing by the author.

*Add to the Credits paragraph at the first upload that ships derived meshes (nothing does yet; assets/external/\*/SOURCE.md):* Includes geometry adapted from "Chair Lift" by Kagelok
(https://sketchfab.com/3d-models/chair-lift-afd305cdd4cb450f80b59f24b4b3e236) and "Ski
gongola and lift station detailed drafts" by Wilin_Amaurea
(https://sketchfab.com/3d-models/ski-gongola-and-lift-station-detailed-drafts-a8fabd3e955643a2a9e9f09bd46635db),
both licensed CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/), modified.

**License**: code MIT; art assets (meshes, textures) CC BY-NC-SA 4.0
(https://creativecommons.org/licenses/by-nc-sa/4.0/): reuse them in free mods with credit to
"Modular Ski Resort by theotherwildcathighboy", share your changes under the same license, no
commercial use.

---

*Ideas, not promises (public): chair numbers, riders on the chairs, a one-click button that removes all ski resort content before you uninstall.*
