# Modular Ski Resort — Paradox Mods front page (draft)

*This is the listing's front page: a scan-in-30-seconds summary. The depth (numbers, every setting, troubleshooting, how it was made) lives on the docs site; the front page links to it. Every claim is traced to its source in release/LISTING-NOTES-2026-10-22.md. Sizing and structure follow docs/research/release-preview-2026-10-22.md section 4.*

*Four tokens must be replaced before the PUBLIC flip and checked by `node tools/sync-listing.js --check --public`, docs/RELEASE-CHECKLIST.md section 7: `{{DOCS_URL}}` (the GitHub Pages address, no trailing slash) and `{{GITHUB_URL}}` (the repository address), which exist only once the repository is public, and `{{FORUM_URL}}` / `{{DISCORD_URL}}` (the Paradox forum and Discord bug-report threads; if only one is used, delete the other's clause). The two thread tokens also stand in PublishConfiguration.xml (`ForumLink`, `ExternalLink Type="discord"`) and in docs-site `guides/troubleshooting.mdx` (Bug Reports). The text between the two horizontal rules below is exactly what goes into LongDescription in `mod/SkiResortMod/Properties/PublishConfiguration.xml` (edit this file only, then run `node tools/sync-listing.js` to copy it; no angle brackets, ampersands or backticks inside the text, the file is XML and the page takes a small markdown subset).*

---

**Short description** (200 characters is the longest on any live listing, 24 sit exactly at 200, none above: an observed cap, not a documented one; ours is 140):

Build a ski resort: drawn chairlifts, gondolas and an aerial tram, runs on the game's own snow, lodges and a resort economy. Base game only.

---

**Long description** (5,017 characters as XML text; the 40 most subscribed code mods in the local cache have a median of 3,800, quartiles 2,200 to 5,900; asset packs 500 to 1,000):

---

**Build a ski resort in Cities: Skylines II.**

Draw chairlifts, gondolas and an aerial tram up your mountain, draw runs down it, and let the game's own snow decide when they open. Guests ski, buy tickets and lessons, and the resort draws tourists to your city. Base game only: no other mod is required.

## Highlights
- **Four lifts, one tool.** Fixed-grip triple chairlift, detachable quad chairlift, gondola and aerial tram. Click the base station, click the top, and the tool plans numbered towers over your terrain. Chairs and cabins ride the cable.
- **Runs on real snow.** Draw natural, groomed, snowmaking, night and cat track runs. Each is rated green to double black from its hardest pitch, and opens at 3 feet (91 cm) of snow. Snow guns extend the season where it is cold enough.
- **Seasons.** Snow melts in spring, runs close and lifts stop. The aerial tram keeps running as summer sightseeing.
- **Lodges, Ski School and Ski Patrol.** Five lodges, a school and two patrol buildings, each following a real place.
- **One panel.** The Ski Resort button (top left) opens Attraction, Runs, Lifts, Guests, Finances and History views, plus a problem list that flies you to what is wrong.
- **A resort economy.** Lift tickets, season passes, lessons, lodge spend and running costs. A ski area's attraction score feeds the game's tourism, so a bigger resort brings more visitors.

## Requirements
Cities: Skylines II 1.6 (tested on 1.6.2f1). No other mod and no DLC. Start from a new city.

## Getting started
1. Open the Ski Resort panel. Place a Welcome Sign snapped to a road (it stands set back from it). It comes straight or angled: pick one before you draw runs. Then place a lodge near where the runs will be.
2. Pick a lift type. Place the base station, then the top station facing it, then draw the line between them. Press Enter to build.
3. Connect a low-voltage power line to the base station.
4. Draw runs downhill from the top station, and wait for snow.

## Good to know
- Skiers are counted, not simulated. Chairs and cabins move but carry no riders, and the chairs do not sway in the wind.
- Stations sit on the slope as placed, so on steep ground one can end up partly in the hillside. Move It lifts it out (see Works with).
- Chairlifts and gondolas stop in summer when no run is open. The aerial tram keeps running as sightseeing.
- Bulldozing a Welcome Sign also deletes every run linked to it, with the runs' glade trees, and the run fees are not refunded. A warning shows the run count and asks first. To move a sign, use Relocate in its info panel: its runs stay linked.
- The Resort Planner (outline a slope, get proposed lifts and a set of runs) is experimental and off by default: turn it on in Options. It builds the runs only: you build its proposed lifts yourself with the lift tool. On steep ground it plans mostly black runs with few greens. Roads, buildings and lift stations inside the outline are not avoided, and its Undo ends when the tool closes.
- Lifts are power-line networks and need power. Do not connect city high-voltage lines to a lift (untested).
- Before uninstalling, bulldoze your lifts and runs (or use the Delete all ski resort content button in the mod's Options), then save the city. Otherwise run paint can stay in the saved terrain, and what leftover lift pieces do is untested.
- Resorts cost money all year and earn in winter. Build in steps.

## Guides and details
Step-by-step guides, every number, settings and troubleshooting are on the docs site: {{DOCS_URL}}
Start with the Resort Quickstart at {{DOCS_URL}}/quickstart/ and the Lifts guide at {{DOCS_URL}}/guides/lifts/.
Bug reports: the Paradox forum thread {{FORUM_URL}} or the Discord thread {{DISCORD_URL}}. Source code: {{GITHUB_URL}}

## Works with
Nothing else is required, but Move It is highly recommended: lift stations sit on the slope as placed, and on steep ground they can end up partly in the hillside. Move It works on every building and station. Write Everywhere is recommended too: with it, the Welcome Sign shows your ski area's name (without it, the name board stays plain). If Tourism Overhaul, Realistic Workplaces and Households or a trip-planner mod is installed, the panel shows a notice and changes nothing in them.
Realistic Trips replaces the game's leisure system that ski days need: with it, the resort sends no skiers of its own. Lifts, runs, snow and buildings still work.

## Credits
- Modular Golf Course Pack by joergeske: the modular recreation idea this mod grew from.
- Meapy's Tourism Overhaul: studied for how tourism and attractions work. Not required.
- The fixed-grip lift is a tribute to the Pallavicini lift at Arapahoe Basin.
- Built with Claude (Anthropic) for decompilation, prefab work and asset generation; direction and testing by the author.
- Full credits and licenses: {{DOCS_URL}}/credits/

**License**: code MIT; art assets (meshes, textures) CC BY-NC-SA 4.0 (https://creativecommons.org/licenses/by-nc-sa/4.0/): reuse them in free mods with credit to "Modular Ski Resort by theotherwildcathighboy", share your changes under the same license, no commercial use.

---

*Kept off the front page on purpose (all in the docs site; see docs/research/release-preview-2026-10-22.md section 5 for the move list): the per-lift slope and span limits, the economy numbers (ticket 80, pass 2 tickets, lessons 30 percent, off-season half staff, income booked as public transportation fees), the attraction formula, the lift tool keys, cat track grading, glades, piste map, powder days, the long Honest-notes list (stations sit on the slope, wind and chair sway, all chairs show one number, canyon and cliff lines), Yenyang's Recolor, Scene Explorer, Write Everywhere and the "ideas, not promises" line.*

