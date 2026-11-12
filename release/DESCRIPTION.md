# Modular Ski Resort — Paradox Mods front page (draft)

*This is the listing's front page: a scan-in-30-seconds summary. The depth (numbers, every setting, troubleshooting, how it was made) lives on the docs site; the front page links to it. Every claim is traced to its source in release/LISTING-NOTES-2026-10-22.md. Sizing and structure follow docs/research/release-preview-2026-10-22.md section 4.*

*Four tokens must be replaced before the PUBLIC flip and checked by `node tools/sync-listing.js --check --public`, docs/RELEASE-CHECKLIST.md section 7: `{{DOCS_URL}}` (the GitHub Pages address, no trailing slash) and `{{GITHUB_URL}}` (the repository address), which exist only once the repository is public, and `{{FORUM_URL}}` / `{{DISCORD_URL}}` (the Paradox forum and Discord bug-report threads; if only one is used, delete the other's clause). The two thread tokens also stand in PublishConfiguration.xml (`ForumLink`, `ExternalLink Type="discord"`) and in docs-site `guides/troubleshooting.mdx` (Bug Reports). The text between the last two horizontal rules below is exactly what goes into LongDescription in `mod/SkiResortMod/Properties/PublishConfiguration.xml` (edit this file only, then run `node tools/sync-listing.js` to copy it; no angle brackets, ampersands or backticks inside the text, the file is XML and the page takes a small markdown subset).*

---

**Short description** (200 characters is the longest on any live listing, 24 sit exactly at 200, none above: an observed cap, not a documented one; ours is 178):

Ski resorts: lifts that fit your terrain, runs on natural or machine-made snow, lodges, ski school and patrol, a resort planner, resort economics, and merging your own buildings.

---

**Long description** (5734 characters as XML text; the 40 most subscribed code mods in the local cache have a median of 3,800, quartiles 2,200 to 5,900; asset packs 500 to 1,000):

---

[Trailer](https://youtu.be/NJvzaxwGfa4) | [Features](https://youtu.be/mkysRTD0zmQ)

**Build a ski resort in Cities: Skylines II.**

Draw chairlifts, gondolas and an aerial tram up your mountain, draw runs down it, and natural or artificial snow will open the runs. Guests ski (simulated, not visualized, yet), buy tickets and lessons, and the resort draws tourists to your city. No mods required, but Anarchy and Move It are highly recommended, along with Write Everywhere.

## Highlights
- **Four lifts, one tool.** Fixed-grip triple chairlift, detachable quad chairlift, gondola and aerial tram. Click the base station, click the top, and the tool plans numbered towers over your terrain. Chairs and cabins ride the rope. You can edit the tower placement.
- **Runs on real snow.** Draw a run and set its features: groomed or natural, snow guns, floodlights for night skiing. Cat tracks link runs across the hill. Each run is rated green to double black from its hardest pitch. It opens at 3 ft (91 cm) of snow on 60 % of the run. Snow guns extend the season where it is cold enough.
- **Resort Planner (experimental).** Outline a mountainside and get a plan of linked runs, cat tracks and lifts. Leave out what you do not want, then build it.
- **Seasons.** Snow melts in spring, runs close and lifts stop. The aerial tram keeps running as summer sightseeing. Northern and Southern Hemisphere seasons are both accounted for.
- **Lodges, Ski School and Ski Patrol.** Five lodges, a school and two patrol buildings, adding amenities (and costs) to your resort.
- **Merge your own buildings.** Is there an asset you'd love to be in your ski area? A landmark, signature or recreation building near the runs can merge into the resort, increasing its attraction (and costs).
- **Ski Bus Depot.** A coach stop in town that brings guests to a ski resort too far away for them to reach on their own. (Helps alleviate vanilla issues with travel times to far-away ski areas.)
- **One hub.** The Ski Resort button (default top left) opens Attraction, Runs, Lifts, Guests, Finances and History views. You can also see any issues with your resort.
- **A resort economy.** Lift tickets, season passes, lessons, lodge spend and running costs. A ski area's attraction score feeds the game's tourism, so a bigger resort brings more visitors.

## Requirements
Cities: Skylines II 1.6 (tested on 1.6.4f1). No DLC or other mod required.

## Getting started
1. Open the Ski Resort hub. Place a Welcome Sign, then place a lodge near where the runs will be.
2. Pick a lift type. Place the base station, top station, then draw the line between them. Press Enter to build.
3. Connect a low-voltage power line to the base station.
4. Draw runs downhill from the top station, and wait for snow.

## Good to know
- Skiers are counted, not visually simulated. Chairs and cabins move but carry no riders. (Post Park Adventures, I will try my best to at least get Cims on the chairlifts)
- Ground: the Welcome Sign, the Ski Bus Depot, the A-Frame Day Lodge, the Swanky Grand Lodge and the aerial tram base level their ground. Most chairlift and gondola stations cut a level pad. The Chalet Lodge, Mountain Hut, Swanky Summit Lodge, both patrol buildings, the Ski School, the fixed-grip top station and the tram top terminal are meant to be built into the hillside with the terrain tools or Move It. The Ski School is the most demanding: level the top of its magic-carpet area, then slope it down to the base.
- Chairlifts and gondolas stop in summer when no run is open. The aerial tram keeps running as sightseeing.
- Bulldozing a Welcome Sign also deletes every run linked to it, with the runs' glade trees, and refunds no run fee. There is a warning popup before you bulldoze the sign.
- The Resort Planner is somewhat experimental and off by default: turn it on in Options.
- Lifts can cross lifts of another type, over or under, where the towers can reach the height. Two lifts of the same type cannot cross.
- A cat track cannot pass under a lift that is already built: build cat tracks first.
- Lifts are power-line networks and need low-voltage power at the base station. The game refuses a city high-voltage line at a lift. This is an odd quirk of how I decided to build the lifts system.
- Before uninstalling, use the "Delete all ski resort content" button in the mod's Options, then save the city.
- Resorts cost money all year and earn in winter. Build in steps if budget-constrained. Welcome to ski resort economics!

## Guides and details
- [Docs site](https://theotherwildcathighboy.github.io/ModularSkiResort): guides, every number, settings and troubleshooting.
- Start with the [Resort Quickstart](https://theotherwildcathighboy.github.io/ModularSkiResort/quickstart/) and the [Lifts guide](https://theotherwildcathighboy.github.io/ModularSkiResort/guides/lifts/).
- Bug reports: the [Paradox forum thread](https://forum.paradoxplaza.com/forum/threads/modular-ski-resort.1949673/). Paste the text from the Copy diagnostics button in the mod's Options.
- [Source code on GitHub](https://github.com/theotherwildcathighboy/ModularSkiResort)

## Credits
- Modular Golf Course Pack by joergeske: the modular recreation idea this mod grew from.
- Meapy's Tourism Overhaul: studied for how tourism and attractions work. Not required. Works alongside this mod.
- All assets (chairs, lodges, patrol huts) are based on real places: https://theotherwildcathighboy.github.io/ModularSkiResort/credits/#asset-inspirations
- Built with Claude (Anthropic) for decompilation, prefab work and asset generation. Direction and many, many hours of testing by the author.
- Full credits and licenses: https://theotherwildcathighboy.github.io/ModularSkiResort/credits/

---

*Kept off the front page on purpose (all in the docs site; see docs/research/release-preview-2026-10-22.md section 5 for the move list): the per-lift slope and span limits, the economy numbers (ticket 80, pass 2 tickets, lessons 30 percent, off-season half staff, income booked as public transportation fees), the attraction formula, the lift tool keys, cat track grading, glades, piste map, powder days, the long Honest-notes list (stations sit on the slope, wind and chair sway, all chairs show one number, canyon and cliff lines), Yenyang's Recolor, Scene Explorer, Write Everywhere and the "ideas, not promises" line.*

