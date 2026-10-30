# Listing notes, 2026-10-22 (ticket T141)

Files rewritten from the current feature set: `release/DESCRIPTION.md`, `release/BESTPRACTICES.md`, `release/CHANGELOG.md` (1.0.0 notes and the beta notes; the three fixed lines untouched, so SL-CL is unaffected), `release/CREDITS.md` (Golf Pack and Tourism Overhaul as credits only, asset inspirations added), `release/README.md` (one contents line). Not touched: the ticket file, DECISIONS, `mod/SkiResortMod/Properties/PublishConfiguration.xml` (see Stale elsewhere).

Status key: **V** = verified in code or prefab this session. **L** = in DECISIONS or the docs only. **B** = built, not yet seen working in game (test sheet `docs/research/field/2026-10-22/test-sheet.md`). **P** = played in game and passed.

## Second pass, 2026-10-22 evening: front page (DECISIONS 376 (4); research docs/research/release-preview-2026-10-22.md)

`release/DESCRIPTION.md` was cut from the long guide-in-a-listing (about 8,500 characters) to a scannable front page (3,430 characters as XML text, headings: Highlights, Requirements, Getting started, Good to know, Guides and details, Works with, Credits, License). Sizing came from the 40 most subscribed code mods in the local Paradox Mods cache (median 3,800 characters, every one uses headings, all carry GitHub or Discord links, median 6 screenshots). The depth stays in the docs site, which the front page links to. `mod/SkiResortMod/Properties/PublishConfiguration.xml` now carries the same short and long text (generated from DESCRIPTION.md, so they cannot drift), `GameVersion 1.6.2f1`, three tags and `AccessLevel Unlisted`.

What the front page still claims (all rows of the table below still apply to these): base game only; four lift types and the one lift tool with numbered towers; run types, rating, 3 ft opening, snow guns; seasons and the tram in summer; five lodges, school, two patrol buildings; the panel and its six views and the problem list; the economy in one sentence and the attraction score feeding tourism; the four getting-started steps; four honest notes (no riders, power and HV untested, uninstall untested, costs all year); Move It and the detected mods; credits for the Golf Pack, Tourism Overhaul, Pallavicini, Claude; the license line. Still **B** (built, not seen working): tower numbers, the 3 ft rule and snow guns. If a sheet row fails, cut the words from DESCRIPTION.md and rerun the XML sync (research doc section 6).

Rows below that no longer appear on the front page (now docs only): lift tool keys and the drag-off removal, preview colors by clearance (the front page keeps only "plans numbered towers"), section redo, heat map and tooltip, the 60/40 percent open and close shares, night runs 25 percent, powder day, cat track grading, glades and piste map, all money numbers, off-season half staff, public transportation booking, the attraction formula, per-lift slope caps, the stations-sit-on-the-slope note, wind and chair sway, all chairs show one number, canyon and cliff lines, Write Everywhere, Recolor, Scene Explorer, "ideas, not promises". Their homes: guides/lifts, guides/runs-and-snow, guides/passes-and-lessons, guides/economy-panel, guides/lodges-school-patrol, reference/*. Moved in this pass because the docs lacked them: wind and riders (guides/lifts "Known Limits"), Write Everywhere (index Install, credits), Recolor and Lift Blog (credits), "Ideas, Not Promises" (index).

Resolved questions below: Q1 (straight to 1.0: the save line stays "start from a new city"), Q5 (gondola does not run in summer, DECISIONS 374 and 376), Q7 (GameVersion `1.6.2f1`, valid: research doc section 2), Q8 (tagline kept, 140 characters, under the 200-character observed cap), Q9 (MIT code, CC BY-NC-SA 4.0 art, DECISIONS 375; license line on the front page), Q12 (done). Still open: Q2, Q3, Q4, Q6, Q10, Q11. The two link tokens `{{DOCS_URL}}` and `{{GITHUB_URL}}` in DESCRIPTION.md and the XML are not claims; they are filled at the public flip (checklist section 7 gate).

## Claims and sources: DESCRIPTION.md

| Claim | Source | Status |
|---|---|---|
| Base game only, no other mod required, works unmodded | CLAUDE.md "Base game only", DECISIONS 150, RELEASE-CHECKLIST section 0 ("Required mods: none"); `SkiOtherMods.cs` (assembly-name detection, labels only) | V |
| Four lift types: fixed-grip triple, detachable quad, gondola, aerial tram | `SkiCatalog.cs` rows (3 / 4 / 8 / 100 seats, ids fixedGrip, detachable, gondola, tram) | V |
| Chairs and cabins move along the cable | `ChairPinSystem.*`, `LiftMoverMath.cs`; the owner's 10-20 report ("chairlifts look very good", "the tram runs, looks awesome", DECISIONS 350) | P |
| Tram shuttles two cabins between terminals | `SkiTramHV` description in `LocaleEN.cs`; `TramShuttleSystem.cs` | V |
| One lift tool for all four; click base, click top, click line adds a tower, drag moves (stays pinned), Enter builds | DECISIONS 341, 350, 351; `LiftLineEdit.Prompt` (exact prompt text); `LiftLineToolSystem` header | V (code); the tool path in game is B (T125 / R-6 checks, sheet rows 7, 8) |
| Drag a tower off the line removes it; right click steps back; Esc closes; Ctrl+Shift+L | `LiftLineEdit.Prompt`, DECISIONS 351, `SkiResortSettings.cs` (ToggleLiftLineTool default) | V |
| Planner fits heights over terrain, fewest towers the type allows | DECISIONS 339, 341 (`MinTowers`), 352; `LiftTowerPlanner.For` | V (code); tall-tower / fit behavior P ("tall towers / placement much improved", 358) |
| Preview colors the cable by ground clearance | DECISIONS 341 ("rope colored by floor margin") | L |
| Towers numbered up the line from the base (1 to 200, wraps to 1) | DECISIONS 358 (2), 361, 364, 367; `TowerNumberTileSystem.cs` | **B** (sheet rows 4, 5) |
| Draw one section at a time; bulldoze or redraw one section, rest keeps | `LocaleEN` BUILD_HINT_LIFT, BULLDOZE_SECTION; DECISIONS 351 ("section editing"); memory "lifts are edited section by section" | V (strings) |
| Five run types: natural, groomed, snowmaking, night, cat track | `SkiResortScore.cs` kinds, `SkiCatalog`, hub build strip (`hubText.ts` BUILD_TAB) | V |
| Rating from the hardest unavoidable pitch; green / blue / black / double black; heat map + tooltip while drawing | `SkiSlopeClassifier.cs` (grade 6 / 25 / 40 / 60 %), `LocaleEN` CRUX_PITCH; setting "Show run difficulty while drawing" | V |
| Every run opens at 3 ft (91 cm) on 60 % of the run, closes below 40 %, natural or made | `RunSnowRules.cs` (`SkiableDepthM` 0.91, `OpenAtShare` 0.60, `CloseBelowShare` 0.40); DECISIONS 350, 353 | V; **B** in game (sheet row 14) |
| Snow comes from the game's snow simulation | CLAUDE.md (injection), `SkiSnowInjectionSystem.cs`, docs/research/run-rendering-synthesis.md | V |
| Snowmaking and night runs have guns, keep the run at about 3 ft; guns October to March | `RunSnowRules.GunStopDepthM` (0.96), DECISIONS 353 (T133, T135) | V; **B** (sheet row 14) |
| Night runs floodlit, worth 25 % more | `SkiResortScore.NightFactor` 1.25; settings text "Floodlight masts" | V |
| Powder day = fresh snow in the last day; more locals go | DECISIONS 353 (T134: precipitation over 24 h, mark at 4.5), `SkiDayRules.cs` | V; shader vs precipitation match is UNVERIFIED (353) |
| Lifts stop while their ski area has no open run; tram keeps running in summer | `LiftClosedRule` in `LiftServedRule.cs`; `LiftLoopHold.cs`; DECISIONS 347 (e), 348 (e) | V; tram summer run P (342 / 350: "tram runs") |
| Cat track links runs; grading tool, max 25 % | DECISIONS 363, 365; `CatTrackGrade.cs` (`DefaultMaxGrade` 0.25, setting clamp 5 to 25); `LocaleEN` GRADE_TOOL strings | **B** (sheet rows 2, 3). Cat-track run itself P (350) |
| Tree tool plans glades; piste map | `docs-site` runs-and-snow; `SkiGlade*Hotkey`, `SkiPisteMapSystem.cs`; settings table | V |
| Five lodges, names as listed | `LocaleEN` Assets.NAME rows; `SkiCatalog` Lodges | V |
| Ski School, Ski Patrol Hut, Ski Patrol HQ | `LocaleEN` Assets.NAME rows | V |
| Welcome Sign starts a ski area; things within 5 km belong to the nearest sign | `SchoolLinkRules.cs` (tether 5000 m), `hubText.ts` buildHintSign | V |
| Panel opens from the top-left button; nothing in the vanilla build menus | `PublishConfiguration.xml` text, DECISIONS 347 (g) (no prefab has a UI group); `reference/settings.mdx` | V (347 g) |
| Six views: Attraction (overview), Runs, Lifts, Guests, Finances, History; build strip tabs Lifts / Runs / Buildings | `hubState.ts` HubView, `ViewTiles.tsx`, `hubText.ts` BUILD_TAB | V |
| Problem list; click flies to cause | `hubText.ts`, docs-site hub-panel | L |
| History by month, season, last 12 months | DECISIONS 297 (B4) | L |
| Lift ticket default 80, range 0 to 200 | `SkiMoneyRules.TicketPriceDefault`, `TicketPriceMax` | V |
| Season passes: residents decide once per season, price 2 tickets default, tourists never | `SkiMoneyRules` (`PassMultiplierDefault` 2, `PassShareDefault` 50), DECISIONS 240, 241, 358 | V |
| Lessons: 30 % of skiers, 1.5 tickets, needs a working Ski School | `SkiMoneyRules` (`LessonShareDefault` 30, `LessonMultiplierDefault` 1.5), DECISIONS 241, 358 | V |
| Lodge spend per paying guest (15, x1.5, x3) | `SkiCatalog.LodgeSpend`, lodge rows | V |
| Lifts bill monthly, runs per hectare while snow covers them | `SkiMoneyRules`, `SkiRunUpkeepSystem.cs`, docs-site economy / runs | V (lifts); L (runs) |
| Off-season half staff after 12 game hours with no open run, option on, upkeep not halved | DECISIONS 360; `OffSeasonRules`, `SkiResortSettings.OffSeasonHalfStaff` | V; **B** (sheet row 17) |
| Income booked as public transportation fees, option on; no ski line in the Economy panel | `reference/settings.mdx`, docs-site economy; DECISIONS 241 | V (option); the Economy panel line is unverified in game (docs-site marks it) |
| Attraction 0 to 200; best lodge sets base; runs / lifts / school / variety / vertical; sign efficiency multiplies | `SkiResortScore.cs` constants (MaxAttraction 200, Variety 1.15, VerticalRef 600) | V |
| Score feeds the game's tourist formula | docs-site reference/attraction "Tourist Draw"; `SkiResortTourism.cs` | L |
| Ski days: locals and hotel tourists, shaped by score, snow, distance, price | `SkiDayRules.cs`, `SkiDaySystem.cs` | V |
| How to build steps 1 to 7 | `hubText.ts` startStep1-5, buildHint* strings, quickstart.mdx, `LiftLineEdit.Prompt` | V |
| Power: overlay, base station is a transformer, LV into the stub at the front corner, line carries power up, no power no lift | DECISIONS 342, 358 (d), 369, 370; `LocaleEN` station descriptions | V; field PASS "station power as desired" (358) |
| Do not connect city HV lines to a tower | docs-site lifts.mdx, DECISIONS 370 (c) ("labeled untested") | L, untested (sheet row 12) |
| Remove lifts before uninstalling; leftover behavior untested | DECISIONS 370 (c); docs-site lifts.mdx | L; sheet row 19 pending |
| Steepest slope 80 / 90 / 120 / 250 %; tram spans 3,000 m | prefab mirror `m_MaxSlopeSteepness` 0.8 / 0.9 / 1.2 / 2.5; `LiftTowerPlanner.For` (TramMaxSpanM 3000, DECISIONS 382) | V (prefab). Whether the lift tool path enforces the slope cap in game: UNVERIFIED |
| Canyon / cliff results can look wrong, like vanilla power lines | Carried from the old text and docs-site lifts.mdx | L, softened; see questions |
| Stations sit on the slope; tram base terminal levels a pad | prefab mirror (`BuildingTerraformOverride` DontRaise + DontLower on FG / DQ / gondola bases, all top stations, tram top; none on the tram base, A-frame, Grand); DECISIONS 354 (c), 358 (6) | V |
| Skiers counted, not simulated; chairs carry no passengers | docs-site hub-panel ("Riders are not simulated"), V2-IDEAS "Seated riders", DECISIONS 158 (g) | L |
| Cable moves in wind, chairs do not sway | DECISIONS 249 | V (ledger) |
| All chairs show the same number | DECISIONS 356 (T124); the owner's report item 5 | P (observed) |
| Costs run all year, income comes with snow | DECISIONS 357, 360 (upkeep not halved) | L |
| Saves: first public release, start from a new city, later changelogs say whether saves load | memory "no save compat before release"; RELEASE-CHECKLIST section 2 ("flips at the first upload"); `release/CHANGELOG.md` template; `release/saves-by-release/README.md` | L |
| Move It works on every building | DECISIONS 350, 358 | P |
| TO / RWH / trip-planner mods only give a hub notice; Write Everywhere shows the sign name | `SkiOtherMods.cs`; `release/CREDITS.md`; `assets/we/K45_WE` exists | V (detection, folder); WE in game UNVERIFIED |
| Credits: Golf Pack (joergeske, ID 144768), Meapy TO, Recolor, Scene Explorer, Write Everywhere, Pallavicini | old CREDITS.md, docs-site credits.mdx, `PublishConfiguration.xml` | L; not re-checked on Paradox Mods (no network) |
| "Ideas, not promises": chair numbers, riders on chairs, remove-all button | DECISIONS 356 / 358, V2-IDEAS "Seated riders", "Safe removal" | L |

## Removed from the old text (and why)

- "Chairs don't move (yet)": false; chairs and cabins move (DECISIONS 350 PASS).
- "Fixed-grip only", "Base and Top lift stations" only: four types now.
- "A dedicated Ski Resort build tab under Parks & Recreation": nothing is in the vanilla menus (347 g).
- "Companion code mod (required, bundled)": one hybrid item (DECISIONS 278).
- "Best experienced with Tourism Overhaul": vanilla-first (DECISIONS 150).
- "Fixed-grip refuses grades over 80 %... future pack": all four types ship; per-type caps listed.
- "Haul rope sags and sways in the wind" as a feature: sway is the shader's and the chairs ignore it (249).
- "8/12/16 m masts": the tower ladders now run from 8 m (chairlifts) to 60 m (tram) (`LocaleEN` tower names); the description gives no heights.
- "Roadmap: moving chairs, detachables, gondolas, tram, glades, night skiing": all shipped. New ideas line only has V2-IDEAS items.
- "Anarchy works as the escape hatch": not verified for the lift tool (sheet row 13 pending). Dropped.
- "Recolor: stations via vanilla picker" kept in BESTPRACTICES only.

## Unverified (built, not yet seen working in game)

Everything below is described in the copy as working. Do not upload until the 10-22 test sheet clears them or the copy is cut.
1. Tower numbers on towers (sheet rows 4, 5; `Towers [numbers]: ARMED` log line).
2. Cat track grading tool, including the heightmap writer and save / load (rows 2, 3).
3. The 3-ft snow rule, gun stop at about 3 ft, no guns after March, powder day rule (rows 14, 15).
4. Off-season half staff (row 17).
5. Lift tool split fix R-6 and the mover at release scale (rows 7, 9, 10).
6. Power with the new stubs (row 11); HV into a lift (row 12); mod removal (row 19).
7. Economy panel "Public transportation" line (docs-site marks it unverified).
8. Whether the lift tool refuses a line over a type's slope cap.
9. Write Everywhere name on the sign.
10. Powder: shader accumulation vs precipitation (DECISIONS 353).
11. Cims on the Mid / Grand decks (T140): not claimed.

## Stale elsewhere (found, not edited)

- `mod/SkiResortMod/Properties/PublishConfiguration.xml`: FIXED 2026-10-22 (ModVersion 1.0.0, GameVersion 1.6.2f1, descriptions from DESCRIPTION.md, AccessLevel Unlisted).
- `docs-site`: runs-and-snow, quickstart, hub-panel and troubleshooting still say "15 cm on 60 %" and 31 cm for natural black (DECISIONS 354 lists this as a T133 docs follow-up). lodges-school-patrol says lodges are Welcome Sign upgrades (they are top-level since 344) and "Switch on under Upgrades". passes-and-lessons and settings still say ticket 60, pass 4 tickets, lessons 20 %. reference/buildings has A-frame 6 jobs and the school 6 jobs / 4,000 upkeep (now 4 / 3,000, DECISIONS 360). reference/lifts "longest span" row (60 / 90 / 250 / 1,000) is the prefab's informational value; the planner uses 90 / 245 / 245 / 1,000 (`LiftTowerPlanner.For`). index.mdx says "Ski Resort tab under Parks & Recreation".
- `LocaleEN.cs` SkiGondolaHV description says "open in summer too", but `LiftClosedRule` holds a gondola with no open run like a chairlift; only the tram is exempt (DECISIONS 348 e). The score does give the gondola a 75 % summer share. Needs one of the two changed.
- `docs/RELEASE-CHECKLIST.md` section 4 still lists "no moving chairs v1" under known limitations.
- Root `README.md` and `CLAUDE.md` still describe a welcome-sign upgrade menu and "ServiceUpgrade children" for lodges.

## Questions for the owner

1. **Which upload is this copy for?** The save line says "first public release, start from a new city". If the first upload is the unlisted 0.9.x beta (checklist section 9), swap that line for "beta builds, no save promise".
2. **Cut or keep the B-status features?** The copy claims tower numbers, the grading tool and the 3-ft snow rule as working. If row 4, 3 or 14 of the 10-22 sheet fails, which of these lines go?
3. **Canyon / cliff line.** I kept "can look wrong, as vanilla power lines do" because the old text and docs-site say it, but the tower planner and tool changed what happens there. Keep, or replace with what the tool actually shows once you have seen a bad line in game?
4. **Slope caps.** Listed from the prefabs (80 / 90 / 120 / 250). Do you want a field check that the lift tool enforces them before this line ships?
5. **Gondola in summer.** Locale says open in summer; the rule holds it. Which is intended? (If the gondola should run in summer, the listing line "The aerial tram keeps running in summer" changes.)
6. **Move It.** Listed as working with every building (your 10-20 pass). Fine to name it in the listing, given the vanilla-first rule? Anarchy is dropped until sheet row 13.
7. **Game version.** The copy gives none. PublishConfiguration says `1.0.*`, the docs site says 1.6.2 (unverified), the installed game is 1.6.2f1 (engine-contract check passes on it, DECISIONS 370). Which string goes in the listing?
8. **Tagline.** "Build a ski resort: drawn chairlifts, gondolas and an aerial tram, runs on the game's own snow, lodges and a resort economy. Base game only." (about 140 characters). OK as the Paradox short description, or shorter?
9. **Licenses in the listing.** Docs-site says code MIT, art CC BY 4.0; the checklist (section 0) still has "assets license: MIT or CC-BY" open. The listing says nothing about licenses. Add a line once you decide.
10. **CC BY model credits.** I left the Kagelok / Wilin_Amaurea paragraph as a conditional note, as before. Confirm nothing derived from them ships in 1.0.0 (generators mention both; SOURCE.md says "nothing changed yet").
11. **Golf Pack wording.** The Golf Pack credit says "the modular recreation idea this mod grew from". Since lodges are no longer ServiceUpgrade children, the old "same architecture" claim is gone. Does that read right to you?
12. **PublishConfiguration.xml.** Want me to paste the short and long description into it next, or leave it to you after your edits?

## Update 2026-10-26: limitations and recommended mods

"Good to know" in DESCRIPTION.md now also carries: chairs do not sway in the wind; stations can sit partly in the hillside on steep ground (Move It lifts them out, DECISIONS 426); chairlifts and gondolas stop in summer when no run is open; the Resort Planner is experimental and off by default. "Getting started" step 1 mentions the set-back and angled Welcome Sign. Sources: docs-site index "Known Limitations", guides/lifts "Known Limits", release/BESTPRACTICES.md "Playing nice". Write Everywhere stays recommended, not required (DECISIONS 431). The XML copy needs `node tools/sync-listing.js` (the parent runs it).

## Update 2026-10-30: release review text fixes

Sources are the 1.0 release review (docs/research/release-review-2026-10-30/). "Good to know" now also carries:

- **Sign bulldoze:** it deletes every run linked to the sign, with the runs' glade trees, and refunds no run fee
  (`SkiRunGateCascadeSystem.Cascade`; runs.md B1).
- **Relocate:** it keeps the runs linked, because the apply updates the sign entity in place (decompile 1.6.2f1:
  `Game.UI.InGame/ActionsSection.cs:303`, `Game.Tools/ApplyObjectsSystem.cs:354-403`). Not yet tried on the sign in game:
  runs.md in-game test 1.
- **Resort Planner limits:** runs only, mostly black runs with few greens on steep ground, roads, buildings and lift stations
  inside the outline not avoided, Undo ending when the tool closes (planner.md S2 to S4).
- **Uninstall:** bulldoze lifts and runs, or use Delete all ski resort content (`SkiResortSettings.DeleteAllResortContent`),
  then save. Run paint can stay in the saved terrain (runs.md S1).
- **Bug reports:** the forum and Discord thread tokens, FORUM_URL and DISCORD_URL (SHIP-PLAN D3).

"Getting started" step 1 now says to pick the straight or angled sign before drawing runs, because a switch is another prefab, not a relocation. Row "Remove lifts before uninstalling" above is superseded by the uninstall item.

## Update 2026-10-30: Resort Planner v2 merge (d424603)

The merge brought the lift-first Resort Planner (v2) to main; the text above described v1. Changed: the "Good to know" planner line
in DESCRIPTION.md (and the XML copy), the 1.0.0 planner bullet in release/CHANGELOG.md, the docs-site changelog and the
PublishConfiguration `<ChangeLog>`, the docs-site planner section (runs-and-snow), index, hub-panel, settings and troubleshooting.

- **Removed (no longer true):** "runs only, no lifts" (the planner proposes lifts; it still builds runs only and the player
  builds the lifts with the lift tool: `QuickRunsToolSystem.Build.cs` emits runs only); the v1 cluster rings and their
  *beginner lift* / *mid-station* labels (Resort mode shows pods instead; the hub hides the cluster lines when pods exist);
  "a progress line runs for a moment" (the scan runs on a worker thread, right click stops it: `QuickRunsToolSystem.Worker.cs`,
  `StepBack`).
- **Still true, kept:** 9 km2 cap (`SkiRunAutoPlanner.V1MaxAreaM2`), roads, buildings and lift stations not obstacles
  (`CollectObstacles`: Surface areas, runs, unowned tiles only), Undo and Undo trees end when the tool closes (`OnStopRunning`),
  mostly black runs on steep ground (site verdict text).
- **New, described:** Lifts knob (auto or 1 to 9), Way home knob, the plan lines, the lift and pod rows with their toggles, the
  way-home verdicts, plan around a built lift (150 m each side, 400 m below the base: `QuickRunsPlanRules`), the three new
  warnings, the Diagnostics option *Resort Planner on the main thread* (off by default).
- **Not described:** the narrow full-cut cat track profile (`GradeParams.NarrowFullCut`, `NoFill`). No mod code calls it (only
  tests/GradeBatch), the cat track tool and its options are unchanged, and the planner grades no terrain, so a player cannot
  see it in 1.0.
- **Status:** V (code). The v2 hub rows, verdicts and scan cancel are B until a field test on the merged build.
