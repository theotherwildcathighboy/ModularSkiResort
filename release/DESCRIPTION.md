# Modular Ski Resort — Paradox Mods front page (draft)

*This is the listing's front page: a scan-in-30-seconds summary. The depth (numbers, every setting, troubleshooting, how it was made) lives on the docs site; the front page links to it. Every claim is traced to its source in release/LISTING-NOTES-2026-10-22.md. Sizing and structure follow docs/research/release-preview-2026-10-22.md section 4.*

*Four tokens must be replaced before the PUBLIC flip and checked by `node tools/sync-listing.js --check --public`, docs/RELEASE-CHECKLIST.md section 7: `{{DOCS_URL}}` (the GitHub Pages address, no trailing slash) and `{{GITHUB_URL}}` (the repository address), which exist only once the repository is public, and `{{FORUM_URL}}` / `{{DISCORD_URL}}` (the Paradox forum and Discord bug-report threads; if only one is used, delete the other's clause). The two thread tokens also stand in PublishConfiguration.xml (`ForumLink`, `ExternalLink Type="discord"`) and in docs-site `guides/troubleshooting.mdx` (Bug Reports). The text between the last two horizontal rules below is exactly what goes into LongDescription in `mod/SkiResortMod/Properties/PublishConfiguration.xml` (edit this file only, then run `node tools/sync-listing.js` to copy it; no angle brackets, ampersands or backticks inside the text, the file is XML and the page takes a small markdown subset).*

---

**Short description** (200 characters is the longest on any live listing, 24 sit exactly at 200, none above: an observed cap, not a documented one; ours is 178):

Ski resorts: lifts that fit your terrain, runs on natural or machine-made snow, lodges, ski school and patrol, a resort planner, resort economics, and merging your own buildings.

---

**Long description** (4741 characters as XML text since the 1.0.2 cleanup; the 40 most subscribed code mods in the local cache have a median of 3,800, quartiles 2,200 to 5,900; asset packs 500 to 1,000):

---

[Trailer](https://youtu.be/NJvzaxwGfa4) | [Features](https://youtu.be/mkysRTD0zmQ)

**Build a ski resort in Cities: Skylines II.**

Draw lifts up your mountain and runs down it. Natural or machine-made snow opens the runs. Guests ski (counted, not shown, yet), buy tickets and lessons, and the resort draws tourists to your city. No mods required, but Anarchy and Move It are highly recommended, along with Write Everywhere.

## Highlights
- **Four lifts, one tool.** Fixed-grip triple chairlift, detachable quad chairlift, gondola and aerial tram. The tool plans numbered towers over your terrain, and you can move them. Chairs and cabins ride the rope.
- **Runs on real snow.** Groomed or natural, with floodlights. Snow guns build a 2 m base that keeps a run open through a thaw. Each run is rated from its hardest pitch, in North American or European grading. It opens at 3 ft (91 cm) of snow on 60 % of the run.
- **Night on the mountain.** Floodlights come on at dusk, like the street lights. Groomers work the cat tracks from dusk to dawn.
- **Resort Planner.** Outline a mountainside and get a plan of linked runs, cat tracks and lifts.
- **Seasons.** Snow melts in spring, runs close, and chairlifts and gondolas stop. Lodges and the school close off-season. The aerial tram keeps running for summer sightseeing. Northern and Southern Hemisphere maps both work.
- **Lodges, Ski School, Ski Patrol and a Ski Bus Depot.** Five lodges, a school, two patrol buildings, and a coach stop in town for a hill too far away.
- **Merge your own buildings.** A landmark, signature or recreation building near the runs can join the resort as a lodge.
- **One hub.** Attraction, runs, lifts, guests, finances and history, with every problem and its cause.
- **A resort economy.** Tickets, season passes, lessons and lodge spend against running costs. Set the ticket and season pass prices with sliders in the hub. To raise ticket tolerance, build better lodges. The resort tourism bonus raises the whole city's attractiveness, as an airport does.

## Requirements
Cities: Skylines II 1.6 (tested on 1.6.4f1). No DLC or other mod required.

## Getting started
Open the Ski Resort hub (button at the top left). Its Start card lists the four steps to a first resort.

## Good to know
- Skiers are counted, not visually simulated. Chairs and cabins move but carry no riders. (Post Park Adventures, I will try my best to at least get Cims on the chairlifts)
- Lifts are built on the game's power-line network, so the game shows some power-line details for them, such as a Transformer section in station panels. Like a real lift, each base station needs low-voltage power. The game refuses a high-voltage line at a lift.
- The Mountain Hut, Chalet and Summit lodges, both patrol buildings, the Ski School and the fixed-grip and tram top stations keep the slope they stand on. Build them into the hillside with the terrain tools or Move It.
- Bulldozing a Welcome Sign deletes every run linked to it and refunds no run fee. A warning comes first.
- Two lifts of the same type cannot cross. A cat track cannot pass under a lift that is already built, so build cat tracks first.
- Before uninstalling, use "Delete all ski resort content" in the mod's Options, then save the city.
- Resorts cost money all year and earn in winter. Welcome to ski resort economics!

## Guides and details
- [Docs site](https://theotherwildcathighboy.github.io/ModularSkiResort): guides, every number, settings and troubleshooting.
- Start with the [Resort Quickstart](https://theotherwildcathighboy.github.io/ModularSkiResort/quickstart/) and the [Lifts guide](https://theotherwildcathighboy.github.io/ModularSkiResort/guides/lifts/).
- Bug reports: the [Paradox forum thread](https://forum.paradoxplaza.com/forum/threads/modular-ski-resort.1949673/) or the [Discord thread](https://discord.com/channels/263634513861541888/1570379334672326736). Paste the text from the Copy diagnostics button in the mod's Options.
- [Source code on GitHub](https://github.com/theotherwildcathighboy/ModularSkiResort)

## Credits
- Modular Golf Course Pack by joergeske: the modular recreation idea this mod grew from.
- Meapy's Tourism Overhaul: studied for how tourism and attractions work. Not required. Works alongside this mod.
- Logo and in-game icon by [Lexa](https://lexakeenen.com/).
- All assets (chairs, lodges, patrol huts) are based on [real places](https://theotherwildcathighboy.github.io/ModularSkiResort/credits/#asset-inspirations).
- Built with Claude (Anthropic) for decompilation, prefab work and asset generation. Direction and many, many hours of testing by the author.
- [Full credits and licenses](https://theotherwildcathighboy.github.io/ModularSkiResort/credits/).

---

*Kept off the front page on purpose (all in the docs site; see docs/research/release-preview-2026-10-22.md section 5 for the move list): the per-lift slope and span limits, the economy numbers (ticket 100, pass 400, lessons 30 percent, off-season half staff, income booked as public transportation fees), the attraction formula, the lift tool keys, cat track grading, glades, piste map, powder days, since 1.0.2 also the getting-started steps (the hub's Start card has them), the Resort Planner default, the grading and unit Options and the Ski School terrain tip, the long Honest-notes list (stations sit on the slope, wind and chair sway, all chairs show one number, canyon and cliff lines), Yenyang's Recolor, Scene Explorer, Write Everywhere and the "ideas, not promises" line.*

