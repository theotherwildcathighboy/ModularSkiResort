/*
 * src/hub/hubText.ts - every player-facing string of the hub PANEL: locale key + English fallback.
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-C); rewritten
 * 2026-10-03 (Round U2, implementer I-3); rewritten for contract 3 on 2026-10-04 (Round E1, stream
 * S6: docs/research/econ-round/spec/ch6-hub-ui.md section 7, the checked reference
 * spec/ch6-hub-ui-ref/src/hub/hubText.ts; texts final unless Richard edits them, DECISIONS 132).
 * Round F7 (2026-10-04, implementer 4; docs/research/field-test-7-fixes.md D1-D6, findings/hub-ux.md
 * with its Verification): this round CUTS text. New: RUN_STATE[Open|Patchy|Closed] (the Runs view's
 * state word; "Patchy", never "Partly", which the status line uses for the whole area), TILE[RunsOpen],
 * EST_TO_TIP, MONEY_TIP[Rwh], PROBLEM[SignNoRoad|LodgeNoRoad] and their PROBLEM_TIP. Changed: the four
 * Welcome Sign efficiency rows ("and {N} more", not "+N", which the band's slot-3 cell uses),
 * PROBLEM_TIP[Efficiency], RECEIPT[LodgeBaseHalf], EST_TO_NOTE and MONEY_NOTE[Rwh] ("Detected: ..."),
 * RUN_OPEN[Partly] and TILE_TIP_PARTLY ("Patchy"), and the five longest tooltips cut to 25 words or
 * fewer (PISTE_MAP_TIP, PROBLEM_TIP[NoLift], PROBLEM_TIP[RunsUnserved], PROBLEM_TIP[LiftNoPower],
 * TILE_TIP[Attraction]). Retired: START_NOTE (the footer says it), MONEY_NOTE[Unknown] (the tickets row
 * says it). F7 fixer: the run counts that still said "partly open" say "patchy" too (STATUS_LINE_PARTLY,
 * _ONE, _EST, _ONE_EST, STATUS_TIP_COUNTS, RECEIPT_TIP[Open]), so one run state has one name;
 * STATUS_WORD[Partly] "Partly open" stays the area's word.
 * Round S1 "Ski days" (2026-10-05, stream E; docs/research/ski-days/s1-spec.md section 2.12 with the section
 * 5.3 rulings, spec/ch4-hub.md G.1): 28 new keys, little text. HUB_LOC: GUESTS_ROW[SkiDays], GUESTS_TIP[SkiDays],
 * GUESTS_MONTH[LastPartial], GUESTS_LAST_PARTIAL (F7), SKI_DAYS[Off|SignOff|NoRun|NoLodge|Night|Full],
 * SKI_DAYS_TIP[Off|NoLodge|Runs|Lodges], POWDER_DAY (D29), PROBLEM[SkiDaysCannotReach|NoParkingNearLodge|
 * LodgeNotSlopeside] and their PROBLEM_TIP (D28). HUB_FMT: GUESTS_BAR_SCALE, SKI_DAYS[RoomRuns|RoomLodges|Room]
 * (ruling R-Q3: "Holds {N} skiers at once", N = the capacity at once, D21), SKI_DAYS_TIP[Sent|Now],
 * STATUS_TIP_POWDER. The numbers in them (800 m, 300 m, 360) are D10, D26 and D21's: a change there changes
 * the text.
 * Round M2 (2026-10-10, builder B4; docs/research/round-m2/build-contract.md sections 8.3 and 8.4, identical in
 * root LocaleEN.cs, gate R4): 11 new keys. HUB_LOC: MONEY_PRICE_TIP[Title|Where|Who] (R7, the Money view's price
 * line), TRIP_PLANNER_NOTE[Unknown|UnknownOff] and TRIP_PLANNER_TIP (R9, the Guests view's trip-planner note),
 * LIFT_COST_REDUCED[NoOpenRun] (thaw-rules T2). HUB_FMT: MONEY_PRICE_LINE, MONEY_PRICE_TIP[Curve] (the factor as a
 * percent: Gameface strips trailing zeros), TRIP_PLANNER_NOTE[Known|KnownOff]. 10 changed texts, same keys:
 * TICKETS_NOTE[NothingOpen] (T1: a running lift alone no longer opens the gate), TICKETS_NOTE[NotKnownYet] (a new
 * sign too), MONEY_TIP[Lifts] (T2: a quarter with no open run), SEASON_TIP[SnowSeason|SnowGunsOnly|Thawing|
 * WaitingForSnow|TooWarm] (R5: say what they read - the day average at the top and the guns running now; "never
 * has a winter" is gone), PROBLEM_TIP[Efficiency] and RECEIPT_TIP[Efficiency] (garbage named). Options paths name
 * the tab and the group ("Main > Ski area balance > Lift ticket price", "Main > Snow > Snow & season gating").
 * Nothing retired: MONEY_TICKETS_PRICE stays (the tickets row's tooltip line).
 * Contract 7 (2026-10-14, season passes v1; identical in LocaleEN.cs, gate R4): MONEY_NOTE[Fees] (the line under the tickets row while
 * tickets are booked as public-transport fees), MONEY_ROW[Passes] (HUB_FMT, {N}), MONEY_TIP[Passes], MONEY_ROW[PassHolders],
 * MONEY_TIP[PassHolders], GUESTS_ROW[PassHolders], GUESTS_TIP[PassHolders]; MONEY_NOTE[Panel] reworded (it said tickets are never in the
 * Economy panel, untrue since stage 1a).
 * Catalog Phase A (2026-10-11; docs/research/art/assets-2026-10/catalog-a/phase-a-contract.md R11, ROUND-SPEC R2):
 * 2 changed texts, same keys, identical in root LocaleEN.cs (gate R4): LIFT_CAPACITY_TIP "3-seat chairs ..." (was
 * "4-seat") and Assets.NAME[SkiChairliftHV] "Fixed-Grip Triple Chairlift" (was "Fixed-Grip Chairlift"). No
 * placeholder (contract D1): the numbers equal SkiCatalog.FixedGrip, guarded by tests/ResortBatch CatalogGuardTests.
 * Contract 6 (2026-10-11, Catalog Phase B, code round 2, hub group; docs/research/art/assets-2026-10/code-round-2/
 * round-2-contract.md section 8.6, identical in root LocaleEN.cs, gate R4): LIFT_CAPACITY_TIP and LIFT_RIDE_TIP move
 * to HUB_FMT ({SEATS} / {SPEED} from LiftRow.seats / speedMps: every lift type has its own); LIFTS_TOTAL_CAPACITY_TIP
 * (new, plain) takes over the totals row of the Lifts view, which sums several types. New: LIFT_TYPE[*] and
 * LODGE_TIER[*] (the pickers' chips), PICKER_TIP[*], PROBLEM[MixedLiftTypes|SummitLodgeNotLiftServed|SchoolNoGreenRun]
 * and their PROBLEM_TIP, REASON[AlreadyBuilt] (C# sends it; REASON[OneLuxury] and RECEIPT_TIP_LUXURY_IGNORED were removed
 * on 2026-10-11 with the one-luxury-lodge-per-area rule), BUILD_SHORT[School|Patrol],
 * BUILD_TIP_SCHOOL / PATROL / LUXURY, RECEIPT_TIP_LODGE_BEST, RECEIPT_TIP[School],
 * RECEIPT_PARTS_SCHOOL, MONEY_PRICE_TIP[Tolerance], MONEY_ROW[LodgeSpend], MONEY_TIP[LodgeSpend], MONEY_TIP[LiftsTypes],
 * MONEY_TIP_LIFT_RATE, the Assets.NAME of every new build item (the upgrades without a shown area reuse
 * BUILD_HINT_LODGE_SELECTED, whose text names no lodge). Changed in place: RECEIPT_TIP[LodgeBase] (the ladder), RECEIPT_TIP[Lifts]
 * (per type), Assets.NAME[SkiBaseStationBuilding|SkiTopStationBuilding] ("Fixed-Grip ...", D17). The numbers baked in
 * them (+12, 20 %, 1 km, 15 %, 70 %, 40, 300 m, up to 24, 6 / 15 / 25, a quarter / three quarters) are SkiCatalog's:
 * tests/ResortBatch CatalogGuardTests CG-T2..T6 derive them from the catalog and FAIL on a difference.
 * Phase D "unpowered lifts are inert" (2026-10-11, code round 4, hub group; docs/research/art/assets-2026-10/code-round-4/
 * round-4-contract.md section 4.4 with round-4-contract-skeptic.md MUST-1 / MUST-3, DECISIONS 171 rulings; identical in
 * root LocaleEN.cs, gate R4; HUB_CONTRACT 6 unchanged): 7 changed texts, same keys, no key new or retired.
 * TILE_TIP[Lifts], LIFT_CAPACITY_TIP, LIFTS_TOTAL_CAPACITY_TIP and PROBLEM_TIP[LiftNoPower] (25 words or fewer: an
 * unpowered lift stops, counts no capacity, serves no run, and maintenance is still charged); MONEY_TIP[Lifts] and
 * MONEY_TIP[LiftsTypes] (in full even without power); LIFT_COST_REDUCED[NoPower] (a No power row is billed reduced only for
 * NoOpenRun now, and liftCostReason still picks this key for it). tests/ResortBatch CatalogGuardTests CG-T8 guards them.
 * Round-4 fixer: PROBLEM_TIP[LiftNoPower] no longer says "full" (the kept NoOpenRun quarter reaches an unpowered lift whose
 * ski area counts no run, so "full" was false then); MONEY_TIP[Lifts] / [LiftsTypes] are exempt from the 25-word rule and
 * capped at 42 / 44 words (LocaleEN.cs header, CG-T8 (g)).
 * T77 hub wording round 1 (2026-10-18; docs/research/ui/hub-wording-round-1-2026-10-18.md, the before/after table; identical in
 * LocaleEN.cs, gate R4; HUB_CONTRACT 9): Richard's ten asks and a pass over every text. 123 texts changed in place, same keys.
 * Moved to HUB_FMT for {MONTH} ("Month of <name>", the hub's one way to name a month; monthName below): TILE_TIP[Guests],
 * GUESTS_MONTH[This|Last|LastPartial], MONEY_ROW[PassHolders]. Moved to HUB_LOC: PROBLEM[NoLift] (no {PCT}). New:
 * RECEIPT_ROOT (the receipt's root line). Removed: MONEY_NOTE[Fees] (where vanilla books resort income goes to the docs, T79).
 * T80 hub wording round 2 (2026-10-18, PIPELINE section 0 "Hub UI design rule"; same report, "Round 2"; contract 9 unchanged):
 * layered and to the point. Every text with "TIP" in its key is at most 25 words (tools/test-hub-rules.js T18.4; the two lift-money
 * tips have their own caps). New: RECEIPT_ROW[Runs|Lifts|School|Variety|Vertical|Scale], RECEIPT_TIP[Scale] (the Attraction view's
 * terms as rows), NO_SCHOOL / SCHOOL_NO_GREEN (InfoLine `flag`), MOUNTAIN[SnowDepth|SnowCover] + MOUNTAIN_TIP[Depth] (snow = depth +
 * coverage, T18.1-T18.3), SKI_DAYS_TIP[Split]. Removed: MOUNTAIN[Snow|SnowMeasured|SnowEstimated], MONEY_TIP[Natural].
 * T86 / T97 field fixes (2026-10-19, docs/research/field/2026-10-19/fix-T96-T86-T97.md; contract 11 unchanged): new SCHOOL_OFF,
 * SCHOOL_WAITING (a built school at 0 says why, hubRules.schoolState), BUILD_ACTIVE_UPGRADE, RESORT_TIP; BUILD_TAB_TIP[Buildings]
 * changed in place (lodges, school and patrol join the sign).
 * T97 P1 header selector (2026-10-19, DECISIONS 308; docs/research/ui/hub-selector-p1-p3-2026-10-19.md; contract 11 unchanged): new
 * RESORT_CAPTION, RESORT_MENU_NOTE, RESORT_COUNTS[One] (HUB_LOC) and RESORT_COUNTS[Many|RunOne|LodgeOne] (HUB_FMT); RESORT_TIP changed in
 * place (the one rule, now the selector's tooltip).
 * Contract:
 *   - Every entry is a literal `[key, english]` tuple. tools/check-bundle.js rule R4 FAILS the build
 *     unless each key exists in root mod/SkiResortMod/LocaleEN.cs with IDENTICAL text, and WARNS
 *     about a SkiResort.Hub.* key there that neither TS nor C# uses. That is why the runtime-built
 *     keys (level names, keys C# sends, run types) are spelled out as tuples below.
 *   - R5 / R13: an entry whose English contains "{" lives in HUB_FMT ONLY and is referenced ONLY as
 *     <LocText entry={HUB_FMT.x}> (cs2/l10n LocalizedString). Every other map holds no placeholder
 *     (a map with one placeholder entry could no longer be indexed); helpers return entries of
 *     placeholder-free maps only (deviation U3: hubRules.ts returns variant names).
 *   - Numbers are never baked into a text: each is an argument built by parts.tsx numArg (U4, U6).
 *   - Glyphs: printable ASCII plus the research 2.F.2 safe list (← → ↑ ↓ … · – × ° ² ≈ NBSP;
 *     tools/check-bundle.js SAFE_GLYPHS). No "¢", "−" or "√": money and signs come from
 *     LocalizedNumber.
 *   - Run names in the hub are RUN_TYPE (sentence case, D17), never Assets.NAME (deviation U5:
 *     chapter 2 renames the run assets to Title Case, "Natural Run").
 *   - Reused keys: `Assets.NAME[<prefab>]` (non-run items), `SkiResort.SlopeBand[*]` (the drawing
 *     tooltip's band names), `SkiResort.Tooltip.*`, `SkiResort.LiftTooltip.*`,
 *     `SkiResort.GladeTooltip.*`; vanilla `SelectedInfoPanel.EFFICIENCY_FACTORS[*]` (factorEntry,
 *     not ours, not in LocaleEN.cs).
 */
import type { LocEntry } from "../l10n";
import { BAND, clampGladeLevel } from "../bindings";
import type { BuildItemId, RunKind } from "../bindings";
import type { NextVariant, StatusWord } from "./hubRules";

/** Plain entries (no placeholder). */
export const HUB_LOC = {
  // ---- frame ----
  sectionFailed: ["SkiResort.Hub.SECTION_FAILED", "This part of the panel failed. Details are in the mod log."],
  sectionTrees: ["SkiResort.Hub.SECTION[Trees]", "Trees"],

  // ---- header (6.1) ----
  resortPrev: ["SkiResort.Hub.RESORT_PREV", "Previous ski area"],
  resortNext: ["SkiResort.Hub.RESORT_NEXT", "Next ski area"],
  resortFly: ["SkiResort.Hub.RESORT_FLY", "Fly to the Welcome Sign"],
  /** T97: the header selector's tooltip line: the one rule (the panel is one ski area, keyed to its sign). P1 changed it in place. */
  resortTip: ["SkiResort.Hub.RESORT_TIP", "Everything you build belongs to a Welcome Sign. Click to pick the ski area shown."],
  /** T97 P1: the selector's caption, the open list's note, and an item's counts when both are 1 (the other forms: HUB_FMT). */
  resortCaption: ["SkiResort.Hub.RESORT_CAPTION", "Ski area"],
  resortMenuNote: ["SkiResort.Hub.RESORT_MENU_NOTE", "Selecting a sign, a lodge or a station on the map shows its ski area here."],
  resortCountsOne: ["SkiResort.Hub.RESORT_COUNTS[One]", "1 run · 1 lodge"],
  unlinkedEntry: ["SkiResort.Hub.UNLINKED_ENTRY", "Not linked to a ski area"],
  unlinkedHint: ["SkiResort.Hub.UNLINKED_HINT", "More than 5 km from every Welcome Sign. Place a sign nearby to link them."],
  rename: ["SkiResort.Hub.RENAME", "Rename"],
  renameTip: ["SkiResort.Hub.RENAME_TIP", "Enter or a click elsewhere keeps the name. Esc cancels. An empty name restores the default."],

  // ---- start card (6.11) ----
  startTitle: ["SkiResort.Hub.START_TITLE", "Start a ski area"],
  startStep1: ["SkiResort.Hub.START_STEP[1]", "Place a Welcome Sign"],
  startStep2: ["SkiResort.Hub.START_STEP[2]", "Connect the sign to a road, power, water and sewage"],
  startStep3: ["SkiResort.Hub.START_STEP[3]", "Add a lodge to the sign"],
  startStep4: ["SkiResort.Hub.START_STEP[4]", "Build a lift: two stations, then the line"],
  startStep5: ["SkiResort.Hub.START_STEP[5]", "Draw runs from the top of the lift"],
  placeSign: ["SkiResort.Hub.PLACE_SIGN", "Place Welcome Sign"],

  // ---- status line (6.2) ----
  statusNoRuns: ["SkiResort.Hub.STATUS_NO_RUNS", "Closed · no runs yet"],
  statusUnlinked: ["SkiResort.Hub.STATUS_UNLINKED", "No Welcome Sign within 5 km"],
  statusTipRule: ["SkiResort.Hub.STATUS_TIP_RULE", "Open: at least 15 cm of snow on 60 % of the run, 31 cm on natural black runs. Closed: under 40 %. Patchy: in between."],
  statusTipMeasured: ["SkiResort.Hub.STATUS_TIP_MEASURED", "Snow is measured on each run from the game's snow."],
  statusTipEstimated: ["SkiResort.Hub.STATUS_TIP_ESTIMATED", "Some runs are estimated from the day's temperature until their snow can be measured."],
  statusTipGating: ["SkiResort.Hub.STATUS_TIP_GATING", "Snow & season gating is off in Options, so every run counts as open."],
  statusTipVisuals: ["SkiResort.Hub.STATUS_TIP_VISUALS", "Snow is hidden in your Gameplay settings. Runs still open on the simulated snow."],
  /** Round S1 D29: the hint slot at powder >= 0.5 (hubRules.statusHint). */
  powderDay: ["SkiResort.Hub.POWDER_DAY", "Powder day"],

  // ---- problems band (6.3) ----
  problemNone: ["SkiResort.Hub.PROBLEM[None]", "Nothing needs your attention."],
  problemUnlinked: ["SkiResort.Hub.PROBLEM[Unlinked]", "More than 5 km from every Welcome Sign"],
  problemNoLodge: ["SkiResort.Hub.PROBLEM[NoLodge]", "No lodge"],
  problemLodgesAllOff: ["SkiResort.Hub.PROBLEM[LodgesAllOff]", "All lodges are switched off"],
  problemNoRuns: ["SkiResort.Hub.PROBLEM[NoRuns]", "No runs yet"],
  problemSignNoRoad: ["SkiResort.Hub.PROBLEM[SignNoRoad]", "Welcome Sign is not at a road"],
  problemLodgeNoRoad: ["SkiResort.Hub.PROBLEM[LodgeNoRoad]", "Lodge is not at a road"],
  // Round S1 D28: codes 20-22.
  problemSkiDaysCannotReach: ["SkiResort.Hub.PROBLEM[SkiDaysCannotReach]", "Many ski-day guests with a car never arrive"],
  problemNoParkingNearLodge: ["SkiResort.Hub.PROBLEM[NoParkingNearLodge]", "No parking near the lodge"],
  problemLodgeNotSlopeside: ["SkiResort.Hub.PROBLEM[LodgeNotSlopeside]", "Lodge is far from the runs"],
  // Contract 6: code 25 (D13) - no count, no name.
  problemSchoolNoGreenRun: ["SkiResort.Hub.PROBLEM[SchoolNoGreenRun]", "Ski school has no green run"],
  // T77 (2026-10-18): no {PCT} any more (Richard: the problem line states the fact; PROBLEM_TIP[NoLift] says what it costs).
  problemNoLift: ["SkiResort.Hub.PROBLEM[NoLift]", "No lift yet"],
  problemTipNoLodge: ["SkiResort.Hub.PROBLEM_TIP[NoLodge]", "Guests arrive at a lodge, so without one the attraction is 0. Build one near the Welcome Sign from the Buildings tab."],
  problemTipLodgesAllOff: ["SkiResort.Hub.PROBLEM_TIP[LodgesAllOff]", "Switched-off lodges do not count, so the attraction is 0. Switch one on under Upgrades in the Welcome Sign's info panel."],
  problemTipLodgesSomeOff: ["SkiResort.Hub.PROBLEM_TIP[LodgesSomeOff]", "Switched-off lodges do not count in the lodge base."],
  problemTipEfficiency: ["SkiResort.Hub.PROBLEM_TIP[Efficiency]", "As in the Welcome Sign's info panel. Every cause that costs 5 % or more is listed below."],
  // Phase D (round-4-contract section 4.4, DECISIONS 171 rulings 1-3, skeptic MUST-1; identical in LocaleEN.cs, R4, CG-T8):
  // the lift stops, serves no run, and is still billed (no "full": the NoOpenRun quarter can still apply, round-4 fixer);
  // power is the base station's only.
  problemTipLiftNoPower: ["SkiResort.Hub.PROBLEM_TIP[LiftNoPower]", "Its base station has no electricity, so the lift stops and serves no run. Maintenance is still charged. Connect a low-voltage line."],
  problemTipLiftSectionMissing: ["SkiResort.Hub.PROBLEM_TIP[LiftSectionMissing]", "A section was removed or is being redrawn. Until the line is complete the lift counts 0 and costs a quarter."],
  problemTipLiftUnfinished: ["SkiResort.Hub.PROBLEM_TIP[LiftUnfinished]", "The line does not join a base station and a top station yet."],
  problemTipNoRuns: ["SkiResort.Hub.PROBLEM_TIP[NoRuns]", "Pick a run type in the Runs tab and draw it within 5 km of the Welcome Sign."],
  problemTipStations: ["SkiResort.Hub.PROBLEM_TIP[Stations]", "A station is placed, but no line joins it to its partner yet."],
  problemTipLiftChairsStopped: ["SkiResort.Hub.PROBLEM_TIP[LiftChairsStopped]", "The lift is complete and still counts, but its chairs stopped. The mod log says why."],
  problemTipRunsUnrateable: ["SkiResort.Hub.PROBLEM_TIP[RunsUnrateable]", "The outline is too large or too thin, or has no terrain under it. These runs count very little."],
  problemTipRunsRating: ["SkiResort.Hub.PROBLEM_TIP[RunsRating]", "Runs are rated one at a time. A new run counts 0 until it is rated; a reshaped run keeps its last rating."],
  problemTipRunsFlat: ["SkiResort.Hub.PROBLEM_TIP[RunsFlat]", "A Flat run adds nothing to the score. Draw it on steeper ground."],
  problemTipFreeLodges: ["SkiResort.Hub.PROBLEM_TIP[FreeLodges]", "These lodges belong to no Welcome Sign, so they count nowhere."],
  problemTipSignNoRoad: ["SkiResort.Hub.PROBLEM_TIP[SignNoRoad]", "Move it so its front touches a road. Power, water and sewage come from that road."],
  problemTipLodgeNoRoad: ["SkiResort.Hub.PROBLEM_TIP[LodgeNoRoad]", "Move it so its deck side touches a road. Guests walk in from that road."],
  problemTipSkiDaysCannotReach: ["SkiResort.Hub.PROBLEM_TIP[SkiDaysCannotReach]", "Many recent ski-day guests with a car did not reach the lodge in time. Fewer are sent until more arrive."],
  problemTipNoParkingNearLodge: ["SkiResort.Hub.PROBLEM_TIP[NoParkingNearLodge]", "No car spaces within 300 m of the lodge. Guests who drive need a parking lot or a street with parking."],
  problemTipLodgeNotSlopeside: ["SkiResort.Hub.PROBLEM_TIP[LodgeNotSlopeside]", "Ski-day guests only go to a lodge within 800 m of this ski area's runs or lift stations."],
  // Contract 6: codes 23-25 (D18, D11, D13).
  problemTipMixedLiftTypes: ["SkiResort.Hub.PROBLEM_TIP[MixedLiftTypes]", "A line and its stations must be one lift type; until then the lift counts 0. Redraw the line as the stations' type."],
  problemTipSummitLodgeNotLiftServed: ["SkiResort.Hub.PROBLEM_TIP[SummitLodgeNotLiftServed]", "In summer a summit lodge counts more only within 300 m of a working lift's top station."],
  problemTipSchoolNoGreenRun: ["SkiResort.Hub.PROBLEM_TIP[SchoolNoGreenRun]", "The ski school adds to the score only while a green run counts."],
  problemMoreTip: ["SkiResort.Hub.PROBLEM_MORE_TIP", "Also needs attention:"],
  problemClickFly: ["SkiResort.Hub.PROBLEM_CLICK_FLY", "Click to fly there."],
  problemClickList: ["SkiResort.Hub.PROBLEM_CLICK_LIST", "Click to open the list."],

  // ---- tiles (6.4) ----
  tileAttraction: ["SkiResort.Hub.TILE[Attraction]", "Attraction"],
  tileRuns: ["SkiResort.Hub.TILE[Runs]", "Runs"],
  /** Round F7 D1: a sign's Runs tile ("2 / 3" counts the open runs); the Not linked tile keeps TILE[Runs] (a total). */
  tileRunsOpen: ["SkiResort.Hub.TILE[RunsOpen]", "Runs open"],
  tileLifts: ["SkiResort.Hub.TILE[Lifts]", "Lifts"],
  tileGuests: ["SkiResort.Hub.TILE[Guests]", "Guests"],
  tileMoney: ["SkiResort.Hub.TILE[Money]", "Finances"],
  tileTipAttraction: ["SkiResort.Hub.TILE_TIP[Attraction]", "The ski area's score on the attraction scale: a large park is about 10, a landmark about 120. Lodges carry it; their panels show 0."],
  tileTipHeld: ["SkiResort.Hub.TILE_TIP_HELD", "Checking runs and snow after loading. The game keeps the saved score meanwhile."],
  tileTipRuns: ["SkiResort.Hub.TILE_TIP[Runs]", "Runs fully open now, out of all runs in this ski area."],
  // Phase D (round-4-contract section 4.4, R4, CG-T8; was "... Without power the chairs still move, ..."): carriers stop.
  tileTipLifts: ["SkiResort.Hub.TILE_TIP[Lifts]", "Complete lifts with power at the base station, out of all complete lifts. Without power a lift stops and adds nothing."],
  tileTipSinceLoad: ["SkiResort.Hub.TILE_TIP_SINCE_LOAD", "Counted since you loaded the city."],
  tileTipMoney: ["SkiResort.Hub.TILE_TIP[Money]", "Net of the month so far: all resort income booked, minus lifts, runs, buildings and wages. The Finances view's Net."],
  tileTipEst: ["SkiResort.Hub.TILE_TIP_EST", "≈ Some income or costs are not counted yet; they read as 0."],
  tileTipEstBuildings: ["SkiResort.Hub.TILE_TIP_EST_BUILDINGS", "≈ The game's bill for buildings and wages is not known yet."],
  tileTipOpen: ["SkiResort.Hub.TILE_TIP_OPEN", "Click to show it below."],

  // ---- overview (6.5) ----
  sectionAttraction: ["SkiResort.Hub.SECTION[Attraction]", "Attraction"],
  scoreTickPark: ["SkiResort.Hub.SCORE_TICK[Park]", "park 10"],
  scoreTickAttraction: ["SkiResort.Hub.SCORE_TICK[Attraction]", "tourist attraction 50"],
  scoreTickLandmark: ["SkiResort.Hub.SCORE_TICK[Landmark]", "landmark 120"],
  receiptLodgeBase: ["SkiResort.Hub.RECEIPT[LodgeBase]", "Lodge base"],
  receiptLodgeBaseHalf: ["SkiResort.Hub.RECEIPT[LodgeBaseHalf]", "Lodge base, half until a run counts"],
  receiptLodgeBaseNone: ["SkiResort.Hub.RECEIPT[LodgeBaseNone]", "Lodge base, no lodge"],
  // Contract 6 (R11 ladder; CG-T5 guards "up to 24"): changed in place.
  receiptTipLodgeBase: ["SkiResort.Hub.RECEIPT_TIP[LodgeBase]", "Best lodge's base plus the others' extras, up to 24 more. Half while no run counts, more for a summit lodge a lift reaches."],
  receiptTerrain: ["SkiResort.Hub.RECEIPT[Terrain]", "Runs, lifts and school"],
  receiptTipRuns: ["SkiResort.Hub.RECEIPT_TIP[Runs]", "Each run's value now, from its difficulty, size, type, lift and snow. The Runs view lists them."],
  // Contract 6 (R12 per-type values; CG-T4 guards "fixed-grip 6", "detachable 15", "gondola 25", "a quarter", "three quarters").
  receiptTipLifts: ["SkiResort.Hub.RECEIPT_TIP[Lifts]", "Each complete lift with power adds fixed-grip 6, detachable 15, gondola 25. While no run counts, chairlifts add a quarter and gondolas three quarters."],
  /** Contract 6: the school's line of the RECEIPT_PARTS tooltip (shown while ResortLive.schoolValue > 0). */
  receiptTipSchool: ["SkiResort.Hub.RECEIPT_TIP[School]", "A ski school on the Welcome Sign adds its value while a green run counts."],
  receiptTipVariety: ["SkiResort.Hub.RECEIPT_TIP[Variety]", "× 1.15 while a green, a blue and a black or harder run all count, else × 1."],
  /** T80 (Richard item 4): each term of the "Runs, lifts and school" section is its own labelled row; the full equation is the
   *  section header's tooltip (RECEIPT_PARTS[_SCHOOL] + RECEIPT_ROOT). */
  receiptRowRuns: ["SkiResort.Hub.RECEIPT_ROW[Runs]", "Runs"],
  receiptRowLifts: ["SkiResort.Hub.RECEIPT_ROW[Lifts]", "Lifts"],
  receiptRowSchool: ["SkiResort.Hub.RECEIPT_ROW[School]", "Ski school"],
  receiptRowVariety: ["SkiResort.Hub.RECEIPT_ROW[Variety]", "Variety"],
  receiptRowVertical: ["SkiResort.Hub.RECEIPT_ROW[Vertical]", "Vertical"],
  receiptRowScale: ["SkiResort.Hub.RECEIPT_ROW[Scale]", "Scale, after the square root"],
  receiptTipScale: ["SkiResort.Hub.RECEIPT_TIP[Scale]", "The section total is this scale × the square root of the terms above. Attractiveness scale in Options."],
  /** T80 (Richard item 3): a row whose 0 has a cause says it with the warning glyph instead of a tooltip sentence
   *  (hubRules.schoolState: no school at all, or a school but no green run counts). */
  noSchool: ["SkiResort.Hub.NO_SCHOOL", "No ski school"],
  schoolNoGreen: ["SkiResort.Hub.SCHOOL_NO_GREEN", "No green run"],
  /** T86 (field 2026-10-19 item 5): a school that is built but adds 0 says why (hubRules.schoolState "off" / "waiting"). */
  schoolOff: ["SkiResort.Hub.SCHOOL_OFF", "Switched off"],
  schoolWaiting: ["SkiResort.Hub.SCHOOL_WAITING", "No run counts yet"],
  receiptOpenNone: ["SkiResort.Hub.RECEIPT_OPEN_NONE", "Runs open now: –"],
  receiptEfficiency: ["SkiResort.Hub.RECEIPT[Efficiency]", "Welcome Sign efficiency"],
  receiptTipEfficiency: ["SkiResort.Hub.RECEIPT_TIP[Efficiency]", "As in the Welcome Sign's info panel: staff, power, water, sewage, garbage, budget and more. The score uses the day's average, at most 100 %."],
  receiptResult: ["SkiResort.Hub.RECEIPT[Result]", "Attraction"],
  receiptResultNoLodge: ["SkiResort.Hub.RECEIPT[ResultNoLodge]", "Attraction, no lodge"],
  receiptTipResult: ["SkiResort.Hub.RECEIPT_TIP[Result]", "(Lodge base + runs, lifts and school) × efficiency, at most 200. The active lodges share it; their own panels show 0."],
  detailShare: ["SkiResort.Hub.DETAIL[Share]", "Share of the city's tourist draw"],
  shareTip: ["SkiResort.Hub.SHARE_TIP", "This ski area's share of the city's attraction, the game's way: each building's score squared. It sets how many tourists come, not where they go."],
  sectionMountain: ["SkiResort.Hub.SECTION[Mountain]", "Mountain"],
  difficulty: ["SkiResort.Hub.DIFFICULTY", "Difficulty"],
  pending: ["SkiResort.Hub.PENDING", "Being rated"],
  unrateable: ["SkiResort.Hub.UNRATEABLE", "Cannot be rated: outline too large or too thin, or no terrain under it"],
  mountainVertical: ["SkiResort.Hub.MOUNTAIN[Vertical]", "Vertical"],
  mountainTipVertical: ["SkiResort.Hub.MOUNTAIN_TIP[Vertical]", "From the lowest run bottom to the highest run top. ≈ while runs are being rated."],
  area: ["SkiResort.Hub.AREA", "Skiable area"],
  areaTip: ["SkiResort.Hub.AREA_TIP", "The plan area of all runs in this ski area."],
  // T80 (Richard item 6, PIPELINE section 0 "Hub UI design rule"): snow is ALWAYS two numbers, depth in cm and run coverage in %
  // (MOUNTAIN[Snow|SnowMeasured|SnowEstimated] "Snow now XX %" removed); "≈" marks an estimate. tools/test-hub-rules.js T18 guards it.
  mountainSnowDepth: ["SkiResort.Hub.MOUNTAIN[SnowDepth]", "Snow depth"],
  mountainSnowCover: ["SkiResort.Hub.MOUNTAIN[SnowCover]", "Run coverage"],
  mountainTipDepth: ["SkiResort.Hub.MOUNTAIN_TIP[Depth]", "Average snow depth on the runs."],
  mountainTipSnow: ["SkiResort.Hub.MOUNTAIN_TIP[Snow]", "Share of the run area with at least 15 cm of snow, 31 cm on natural black runs."],
  unlinkedNote: ["SkiResort.Hub.UNLINKED_NOTE", "These runs and lifts belong to no ski area, so they score nothing and draw no guests."],

  // ---- runs view (6.6) ----
  treeTool: ["SkiResort.Hub.TREE_TOOL", "Tree tool"],
  treeToolTip: ["SkiResort.Hub.TREE_TOOL_TIP", "Plans trees on and around runs. Turn it on, hover a run and click."],
  treeToolTipOn: ["SkiResort.Hub.TREE_TOOL_TIP_ON", "The tree tool is on. Click to turn it off."],
  treesFewer: ["SkiResort.Hub.TREES_FEWER", "Fewer trees"],
  treesMore: ["SkiResort.Hub.TREES_MORE", "More trees"],
  treesApplyTo: ["SkiResort.Hub.TREES_APPLY_TO", "Apply to"],
  treesUnavailable: ["SkiResort.Hub.TREES_UNAVAILABLE", "The tree tool is not available right now."],
  gladeLevel: ["SkiResort.GladeTooltip.LEVEL", "Tree density (1-8)"],
  gladeTargetInterior: ["SkiResort.GladeTooltip.TARGET[Interior]", "Run interior"],
  gladeTargetTreeLine: ["SkiResort.GladeTooltip.TARGET[TreeLine]", "Tree line"],
  pisteMap: ["SkiResort.Hub.PISTE_MAP", "Piste map"],
  pisteMapTip: ["SkiResort.Hub.PISTE_MAP_TIP", "Draws this ski area's runs on the ground, in difficulty colours, while the panel is open. Natural: dashed. Too flat: grey. Cannot be rated: orange."],
  colLift: ["SkiResort.Hub.COL_LIFT", "Lift"],
  colLiftTip: ["SkiResort.Hub.COL_LIFT_TIP", "↑: a running lift reaches the top of this run."],
  colLength: ["SkiResort.Hub.COL_LENGTH", "Length"],
  colDrop: ["SkiResort.Hub.COL_DROP", "Drop"],
  colArea: ["SkiResort.Hub.COL_AREA", "Area"],
  colLengthTip: ["SkiResort.Hub.COL_LENGTH_TIP", "Horizontal length, estimated from the outline."],
  colDropTip: ["SkiResort.Hub.COL_DROP_TIP", "Approximate height from the lowest to the highest point of the outline."],
  runsEmpty: ["SkiResort.Hub.RUNS_EMPTY", "No runs yet. Pick a run type in the Runs tab below and draw its outline."],
  runEstimated: ["SkiResort.Hub.RUN_ESTIMATED", "Estimated from the day's temperature."],
  runOpenGating: ["SkiResort.Hub.RUN_OPEN_GATING", "Counts as open: snow rules are off."],
  runOpenNone: ["SkiResort.Hub.RUN_OPEN_NONE", "Not linked: no snow state."],
  runServedAnon: ["SkiResort.Hub.RUN_SERVED_ANON", "A running lift reaches its top."],
  runTypeNote: ["SkiResort.Hub.RUN_TYPE_NOTE", "Natural green and blue runs count less; a groomed run counts in full."],
  runCatTrackNote: ["SkiResort.Hub.RUN_CAT_TRACK_NOTE", "A cat track links the hill but adds nothing to the score or the capacity."],
  runUnsuitable: ["SkiResort.Hub.RUN_UNSUITABLE", "Unsuitable: steeper than 25 % for a cat track. Redraw it on gentler ground."],
  runValue: ["SkiResort.Hub.RUN_VALUE", "Score value"],
  runCost: ["SkiResort.Hub.RUN_COST", "Running cost"],
  runActions: ["SkiResort.Hub.RUN_ACTIONS", "Click to fly there. The pen renames it."],
  cruxPitch: ["SkiResort.Tooltip.CRUX_PITCH", "Hardest unavoidable pitch"],
  steepestPatch: ["SkiResort.Tooltip.STEEPEST_PATCH", "Steepest patch (avoidable)"],

  // ---- lifts view (6.7) ----
  liftsEmpty: ["SkiResort.Hub.LIFTS_EMPTY", "No lifts yet. Place a base station and a top station facing each other, then draw the lift line between them."],
  liftLine2SectionMissing: ["SkiResort.Hub.LIFT_LINE2[SectionMissing]", "A section is missing: redraw it with the lift line tool"],
  liftLine2Unfinished: ["SkiResort.Hub.LIFT_LINE2[Unfinished]", "Not connected to a station at both ends yet"],
  liftLine2StationsPair: ["SkiResort.Hub.LIFT_LINE2[StationsPair]", "Both stations placed: draw the lift line between them"],
  liftLine2StationsBaseOnly: ["SkiResort.Hub.LIFT_LINE2[StationsBaseOnly]", "Base station placed: place the top station facing it"],
  liftLine2StationsTopOnly: ["SkiResort.Hub.LIFT_LINE2[StationsTopOnly]", "Top station placed: place the base station facing it"],
  liftRide: ["SkiResort.Hub.LIFT_RIDE", "Ride time"],
  liftCapacity: ["SkiResort.Hub.LIFT_CAPACITY", "Capacity"],
  // Contract 6: LIFT_RIDE_TIP and LIFT_CAPACITY_TIP moved to HUB_FMT ({SPEED}, {SEATS} per lift type); the totals row
  // of the Lifts view sums several types, so it takes this plain tip. Catalog Phase C minors fix (contract review note):
  // reworded like LIFT_CAPACITY_TIP (no "spaced"; "a tram counts its station stops"), identical in LocaleEN.cs (R4), CG-T7.
  // Phase D (round-4-contract section 4.4 with skeptic MUST-3, 25 words; CG-T8): the total sums only the lifts that carry.
  liftsTotalCapacityTip: ["SkiResort.Hub.LIFTS_TOTAL_CAPACITY_TIP", "Sum over complete lifts with power and an open run; a tram counts its station stops. Riders are not simulated."],
  liftRuns: ["SkiResort.Hub.LIFT_RUNS", "Runs it serves"],
  liftCost: ["SkiResort.Hub.LIFT_COST", "Running cost"],
  // Phase D (DECISIONS 171 ruling 2, skeptic MUST-1, build-rules.md section 3.2; was "Billed at 25 %: no power"): power no
  // longer lowers the bill, so a reduced row whose status is No power is billed reduced only for NoOpenRun;
  // hubRules.liftCostReason still picks this key for it, so the text names that reason (text only, contract 6).
  liftCostReducedNoPower: ["SkiResort.Hub.LIFT_COST_REDUCED[NoPower]", "Billed at a quarter: no open run in its ski area"],
  liftCostReducedUnfinished: ["SkiResort.Hub.LIFT_COST_REDUCED[Unfinished]", "Billed at a quarter while unfinished"],
  liftCostReducedSectionMissing: ["SkiResort.Hub.LIFT_COST_REDUCED[SectionMissing]", "Billed at a quarter while a section is missing"],
  // Round M2 T2 (thaw-rules.md section 2 item 8): a complete, powered lift in a ski area with no counted run.
  liftCostReducedNoOpenRun: ["SkiResort.Hub.LIFT_COST_REDUCED[NoOpenRun]", "Billed at a quarter: no open run in its ski area"],
  liftActions: ["SkiResort.Hub.LIFT_ACTIONS", "Click to fly there (to the gap if a section is missing). The pen renames it."],
  liftsTotalLength: ["SkiResort.Hub.LIFTS_TOTAL_LENGTH", "Total lift length"],
  liftsTotalCapacity: ["SkiResort.Hub.LIFTS_TOTAL_CAPACITY", "Capacity, all lifts"],
  liftSections: ["SkiResort.Hub.LIFT_SECTIONS", "Sections"],
  liftTowers: ["SkiResort.Hub.LIFT_TOWERS", "Towers"],
  liftChairs: ["SkiResort.Hub.LIFT_CHAIRS", "Chairs"],
  liftStraight: ["SkiResort.Hub.LIFT_STRAIGHT", "Stations straight (180°)"],
  liftTowerRemoved: ["SkiResort.Hub.LIFT_TOWER_REMOVED", "A tower was removed by another tool. Redraw a section next to it to restore it."],
  liftNameTip: ["SkiResort.Hub.LIFT_NAME_TIP", "The name is stored on the base station, or on the top station while there is no base."],
  liftSignTip: ["SkiResort.Hub.LIFT_SIGN_TIP", "Lifts belong to the nearest Welcome Sign within 5 km of their base station; runs keep the sign they were drawn for."],
  alignOffset: ["SkiResort.LiftTooltip.ALIGN_OFFSET", "Offset from the straight line"],
  alignAngle: ["SkiResort.LiftTooltip.ALIGN_ANGLE", "Angle off straight"],

  // ---- guests view (6.8) ----
  sectionGuests: ["SkiResort.Hub.SECTION[Guests]", "Guests"],
  /** Round S1 D27: the ski-day row (first in the table) and its tooltip body. */
  guestsRowSkiDays: ["SkiResort.Hub.GUESTS_ROW[SkiDays]", "Reached a lodge"],
  guestsTipSkiDays: ["SkiResort.Hub.GUESTS_TIP[SkiDays]", "Skiers the resort sends out on a ski day, counted when they reach a lodge. They also count as residents or tourists."],
  /** T07: the three group headers of the Guests table and the dim note under the first. */
  guestsGroupLodges: ["SkiResort.Hub.GUESTS_GROUP[Lodges]", "At the lodges"],
  guestsGroupSent: ["SkiResort.Hub.GUESTS_GROUP[Sent]", "Ski-day trips"],
  guestsGroupTickets: ["SkiResort.Hub.GUESTS_GROUP[Tickets]", "Lift tickets"],
  guestsNoteNeverSki: ["SkiResort.Hub.GUESTS_NOTE[NeverSki]", "Includes visitors who never ski"],
  guestsRowTourists: ["SkiResort.Hub.GUESTS_ROW[Tourists]", "Tourists"],
  guestsRowGroups: ["SkiResort.Hub.GUESTS_ROW[Groups]", "Tourist groups"],
  guestsRowResidents: ["SkiResort.Hub.GUESTS_ROW[Residents]", "Residents"],
  guestsRowPaid: ["SkiResort.Hub.GUESTS_ROW[Paid]", "Paid a lift ticket"],
  guestsRowTickets: ["SkiResort.Hub.GUESTS_ROW[Tickets]", "Ticket money"],
  guestsTipTourists: ["SkiResort.Hub.GUESTS_TIP[Tourists]", "People in tourist groups that arrived at this ski area's lodges or Welcome Sign."],
  guestsTipGroups: ["SkiResort.Hub.GUESTS_TIP[Groups]", "Tourists arrive in groups; each group counts once."],
  guestsTipResidents: ["SkiResort.Hub.GUESTS_TIP[Residents]", "Residents of your city who came to a lodge for leisure."],
  guestsTipTickets: ["SkiResort.Hub.GUESTS_TIP[Tickets]", "Paid by each guest's household into your city's balance."],
  guestsLastNa: ["SkiResort.Hub.GUESTS_LAST_NA", "Not counted: counting starts when the city loads."],
  guestsLastPartial: ["SkiResort.Hub.GUESTS_LAST_PARTIAL", "Not counted in full: the city was loaded, or time skipped ahead, during that month."],
  // Round S1 D27: the ski-day state line (hubRules.skiDayLine; Room variants are HUB_FMT) and its tooltips (skiDayTip).
  skiDaysOff: ["SkiResort.Hub.SKI_DAYS[Off]", "Ski days are off in Options"],
  skiDaysSignOff: ["SkiResort.Hub.SKI_DAYS[SignOff]", "Closed: the Welcome Sign is not running"],
  skiDaysNoRun: ["SkiResort.Hub.SKI_DAYS[NoRun]", "Closed: no open run"],
  skiDaysNoLodge: ["SkiResort.Hub.SKI_DAYS[NoLodge]", "No lodge for ski days"],
  skiDaysNight: ["SkiResort.Hub.SKI_DAYS[Night]", "Lifts closed for the night"],
  skiDaysFull: ["SkiResort.Hub.SKI_DAYS[Full]", "Full right now"],
  skiDaysTipOff: ["SkiResort.Hub.SKI_DAYS_TIP[Off]", "Locals who ski and Tourists who ski are both 0 in Options."],
  skiDaysTipNoLodge: ["SkiResort.Hub.SKI_DAYS_TIP[NoLodge]", "Ski days need a lodge that is switched on, at a road, and within 800 m of the runs."],
  skiDaysTipRuns: ["SkiResort.Hub.SKI_DAYS_TIP[Runs]", "The runs set the limit: bigger and easier runs hold more skiers at once."],
  skiDaysTipLodges: ["SkiResort.Hub.SKI_DAYS_TIP[Lodges]", "The lodges set the limit: each lodge takes 360 skiers at once, more on a powder day."],
  estRowTourists: ["SkiResort.Hub.EST_ROW[Tourists]", "Estimated tourists from this ski area"],
  estTouristsTip: ["SkiResort.Hub.EST_TOURISTS_TIP", "From the game's tourist formula, not counted. Tourism mods can replace the formula, so real arrivals can differ."],
  estToNote: ["SkiResort.Hub.EST_TO_NOTE", "Detected: Tourism Overhaul. It sets tourist numbers."],
  estToTip: ["SkiResort.Hub.EST_TO_TIP", "Tourism Overhaul sets tourist numbers in this city, so no estimate is shown."],
  // Round M2 R9 (enabled-flags.md C5): the trip-planner note (Known / KnownOff carry {NAME}: HUB_FMT).
  tripPlannerNoteUnknown: ["SkiResort.Hub.TRIP_PLANNER_NOTE[Unknown]", "Part of the game's trip planner is switched off (another mod, or a game error), so trip times can differ."],
  tripPlannerNoteUnknownOff: ["SkiResort.Hub.TRIP_PLANNER_NOTE[UnknownOff]", "Ski days off: the game's leisure systems are switched off (another mod, or a game error)."],
  tripPlannerTip: ["SkiResort.Hub.TRIP_PLANNER_TIP", "Read at each city load: which of the game's trip systems are on, and which known mods are loaded. The mod log lists them."],
  guestsNoteWhere: ["SkiResort.Hub.GUESTS_NOTE[Where]", "Counted when tourists arrive at this ski area's lodge or Welcome Sign, and when residents come to a lodge for leisure."],
  guestsNoteSinceLoad: ["SkiResort.Hub.GUESTS_NOTE[SinceLoad]", "Counting started when you loaded the city, partway through the month."],
  ticketsNoteOff: ["SkiResort.Hub.TICKETS_NOTE[Off]", "Lift tickets are off: the Lift ticket price in Options is 0."],
  ticketsNoteNoEfficiency: ["SkiResort.Hub.TICKETS_NOTE[NoEfficiency]", "No lift tickets now: the Welcome Sign is not running."],
  ticketsNoteNothingOpen: ["SkiResort.Hub.TICKETS_NOTE[NothingOpen]", "No lift tickets now: no run is open."],
  ticketsNoteNotKnownYet: ["SkiResort.Hub.TICKETS_NOTE[NotKnownYet]", "No lift tickets yet: runs are being checked after a load or a new sign."],

  // ---- money view (6.9) ----
  sectionMoney: ["SkiResort.Hub.SECTION[Money]", "Finances"],
  moneyTicketsOff: ["SkiResort.Hub.MONEY_TICKETS[Off]", "Lift tickets: off"],
  moneyRowTickets: ["SkiResort.Hub.MONEY_ROW[Tickets]", "Lift tickets"],
  moneyTicketsTip: ["SkiResort.Hub.MONEY_TICKETS_TIP", "Booked this month so far: each guest who paid × the price when they arrived."],
  sectionCosts: ["SkiResort.Hub.SECTION[Costs]", "Running costs"],
  moneyRowRuns: ["SkiResort.Hub.MONEY_ROW[Runs]", "Runs"],
  moneyTipRunsBooked: ["SkiResort.Hub.MONEY_TIP[RunsBooked]", "Run upkeep booked this month so far. Today's rate per month, by kind:"],
  moneyRowBuildings: ["SkiResort.Hub.MONEY_ROW[Buildings]", "Building upkeep"],
  moneyRowWages: ["SkiResort.Hub.MONEY_ROW[Wages]", "Staff wages"],
  moneyNet: ["SkiResort.Hub.MONEY_NET", "Net so far this month"],
  moneyTipNet: ["SkiResort.Hub.MONEY_TIP[Net]", "All income booked this month minus all costs: the rows above add up to it. Buildings and wages: today's rate × the month so far."],
  sectionAlso: ["SkiResort.Hub.SECTION[Also]", "Also, not in the net"],
  moneyNotePanel: ["SkiResort.Hub.MONEY_NOTE[Panel]", "This month so far: income, lift and run costs as booked; buildings and wages at today's rate for the share of the month gone."],
  moneyNoteRwh: ["SkiResort.Hub.MONEY_NOTE[Rwh]", "Detected: Realistic Workplaces. The real bill can differ."],
  moneyTipRwh: ["SkiResort.Hub.MONEY_TIP[Rwh]", "Realistic Workplaces And Households changes staff costs; the bill can differ."],
  moneyNoteEst: ["SkiResort.Hub.MONEY_NOTE[Est]", "≈ Some income or costs are not counted yet; they read as 0 in the net."],
  // Round M2 R7 (hub-price.md section 2 item 3): the Money view's price line, tooltip lines without a placeholder.
  moneyPriceTitle: ["SkiResort.Hub.MONEY_PRICE_TIP[Title]", "Lift ticket price"],
  moneyPriceTipWhere: ["SkiResort.Hub.MONEY_PRICE_TIP[Where]", "Options > Modular Ski Resort > Main > Ski area balance. One price for every ski area."],
  moneyPriceTipWho: ["SkiResort.Hub.MONEY_PRICE_TIP[Who]", "Only ski days react to it; other visitors and the attraction score ignore it."],
  // Contract 6 (D15): the lodge-spend income row (drawn only while lodgeSpendMonth > 0; hubRules.lodgeSpendShown).
  moneyRowLodgeSpend: ["SkiResort.Hub.MONEY_ROW[LodgeSpend]", "Lodge spend"],
  moneyRowPassesMonth: ["SkiResort.Hub.MONEY_ROW[PassesMonth]", "Season passes sold"],
  moneyTipLodgeSpend: ["SkiResort.Hub.MONEY_TIP[LodgeSpend]", "Booked this month so far: paying ski guests' spend at the lodges. Set per guest in Options > Modular Ski Resort > Main > Ski area balance."],
  // Contract 7 (season passes v1): rows drawn while passes are on or used (hubRules.passesShown); the note while tickets are fees.
  moneyTipPasses: ["SkiResort.Hub.MONEY_TIP[Passes]", "Sold July to June. Holders ski free all season and come more often. Share and price: Options > Modular Ski Resort > Main > Ski area balance."],
  moneyTipPassHolders: ["SkiResort.Hub.MONEY_TIP[PassHolders]", "Guests who came in on a season pass: no lift ticket money now."],
  guestsRowPassHolders: ["SkiResort.Hub.GUESTS_ROW[PassHolders]", "Came in on a season pass"],
  guestsTipPassHolders: ["SkiResort.Hub.GUESTS_TIP[PassHolders]", "Residents who came in on a season pass. They paid once for the season and pay nothing at the gate. Tourists never hold one."],
  // Contract 8 (econ-2 round 2): lessons, the pre-season sale, the city-income estimate (hidden under Tourism Overhaul: EST_TO_NOTE / EST_TO_TIP).
  moneyTipLessons: ["SkiResort.Hub.MONEY_TIP[Lessons]", "Booked this month so far, one lesson per guest. Share and price: Options > Modular Ski Resort > Main > Ski area balance."],
  moneyTipPassesPre: ["SkiResort.Hub.MONEY_TIP[PassesPre]", "The one sale before the season opens. Part of the season's sales; in the net in the month it sold."],
  moneyRowCityIncome: ["SkiResort.Hub.MONEY_ROW[CityIncome]", "City income the resort brings"],
  moneyTipCityIncome: ["SkiResort.Hub.MONEY_TIP[CityIncome]", "Estimate: this ski area's share of the city's tourist draw × the game's tourist income. Not resort income, not in the net."],
  guestsRowLessons: ["SkiResort.Hub.GUESTS_ROW[Lessons]", "Took a lesson"],
  guestsTipLessons: ["SkiResort.Hub.GUESTS_TIP[Lessons]", "Guests who took a lesson, one each."],
  unlinkedMoney: ["SkiResort.Hub.UNLINKED_MONEY", "Runs that are not linked cost nothing and earn nothing; their lifts still cost money to run."],

  // ---- build strip (6.10) ----
  buildEmpty: ["SkiResort.Hub.BUILD_EMPTY", "No building tools were reported. See SkiResortMod.Mod.log."],
  buildActive: ["SkiResort.Hub.BUILD_ACTIVE", "In use: click again to stop"],
  /** T97 (field 2026-10-19 item 12): line 2 while a lodge, school or patrol tool is active. The launch selects the sign in the world
   *  (the engine's Upgrade mode targets the selected building, ObjectToolSystem.cs:3318), so its vanilla panel opens: say why. */
  buildActiveUpgrade: ["SkiResort.Hub.BUILD_ACTIVE_UPGRADE", "Joins this ski area's Welcome Sign · click again to stop"],
  locked: ["SkiResort.Hub.LOCKED", "Not available yet"],
  buildHintSign: ["SkiResort.Hub.BUILD_HINT_SIGN", "The sign is the ski area: runs and lifts within 5 km belong to it."],
  buildHintLodge: ["SkiResort.Hub.BUILD_HINT_LODGE", "Goes near this ski area's Welcome Sign."],
  buildHintLodgeSelected: ["SkiResort.Hub.BUILD_HINT_LODGE_SELECTED", "Goes on the selected Welcome Sign, or the nearest one."],
  buildHintBase: ["SkiResort.Hub.BUILD_HINT_BASE", "Place at the bottom of the lift."],
  buildHintTop: ["SkiResort.Hub.BUILD_HINT_TOP", "Place at the top, facing the base station. A guide shows when it is straight."],
  buildHintLift: ["SkiResort.Hub.BUILD_HINT_LIFT", "Draw from station to station. On steep ground, redraw one section at a time."],
  buildHintLiftSign: ["SkiResort.Hub.BUILD_HINT_LIFT_SIGN", "Lifts join the nearest Welcome Sign within 5 km of their base station."],
  buildShortLodge: ["SkiResort.Hub.BUILD_SHORT[Lodge]", "Goes near this ski area's Welcome Sign."],
  buildShortTop: ["SkiResort.Hub.BUILD_SHORT[Top]", "Place at the top, facing the base station."],
  buildShortLift: ["SkiResort.Hub.BUILD_SHORT[Lift]", "Draw from station to station, one section at a time."],
  // Contract 6: the school and patrol tiles (upgrades of the sign, launched like the lodge) and the pickers.
  buildShortSchool: ["SkiResort.Hub.BUILD_SHORT[School]", "Goes near this ski area's Welcome Sign; teaches on green runs."],
  buildShortPatrol: ["SkiResort.Hub.BUILD_SHORT[Patrol]", "Goes on the Welcome Sign; place it near steep runs."],
  buildTipSchool: ["SkiResort.Hub.BUILD_TIP_SCHOOL", "Adds +12 to Runs, lifts and school while a green run counts; locals come 20 % more often."],
  buildTipPatrol: ["SkiResort.Hub.BUILD_TIP_PATROL", "Black and double-black runs within 1 km score 15 % more; runs there without a lift count at least 70 %."],
  buildTipLuxury: ["SkiResort.Hub.BUILD_TIP_LUXURY", "Luxury lodge. Ski days stay full at a lift ticket price up to 40 higher."],
  pickerTipLiftType: ["SkiResort.Hub.PICKER_TIP[LiftType]", "Pick the kind of lift to build."],
  pickerTipLodgeTier: ["SkiResort.Hub.PICKER_TIP[LodgeTier]", "Pick the lodge to build."],
  buildTipOnce: ["SkiResort.Hub.BUILD_TIP_ONCE", "One-time"],
  buildTipMonth: ["SkiResort.Hub.BUILD_TIP_MONTH", "Per month"],
  buildMonthNone: ["SkiResort.Hub.BUILD_MONTH_NONE", "Nothing"],
  buildMonthGame: ["SkiResort.Hub.BUILD_MONTH_GAME", "Upkeep and wages, billed by the game"],
  buildMonthStation: ["SkiResort.Hub.BUILD_MONTH_STATION", "The whole lift is priced by its length: both stations and the line. This station is one part."],
  buildTipReshape: ["SkiResort.Hub.BUILD_TIP_RESHAPE", "Reshaping a run later costs nothing more."],
  buildTipLink: ["SkiResort.Hub.BUILD_TIP_LINK", "Runs link to the nearest Welcome Sign within 5 km."],
  buildTipNatural: ["SkiResort.Hub.BUILD_TIP_NATURAL", "Nothing is drawn on the ground: find it with the piste map."],
  buildTipLodge: ["SkiResort.Hub.BUILD_TIP_LODGE", "A new lodge needs staff, power and water. Until it has them, the sign's efficiency and the attraction can fall."],
  buildTipTypeNatural: ["SkiResort.Hub.BUILD_TIP_TYPE[Natural]", "Score: greens count half, blues three quarters, blacks in full. It opens only when natural snow covers it."],

  // ---- history view (T78 build 2, contract 10; SkiResort.Hub.HISTORY[...] family; tooltips <= 25 words, T18.4) ----
  tileHistory: ["SkiResort.Hub.TILE[History]", "History"],
  tileTipHistory: ["SkiResort.Hub.TILE_TIP[History]", "Guests and finances of every month this ski area has recorded."],
  sectionHistory: ["SkiResort.Hub.SECTION[History]", "History"],
  historyScopeArea: ["SkiResort.Hub.HISTORY[ScopeArea]", "This ski area"],
  historyScopeCity: ["SkiResort.Hub.HISTORY[ScopeCity]", "Whole city"],
  historyTipScope: ["SkiResort.Hub.HISTORY_TIP[Scope]", "Whole city adds every ski area that exists now; a deleted one's history goes with it. No attraction line: each area has its own."],
  historyRange12: ["SkiResort.Hub.HISTORY[Range12]", "12 months"],
  historyRangeSeasons: ["SkiResort.Hub.HISTORY[RangeSeasons]", "This season vs last"],
  historyRangeBySeason: ["SkiResort.Hub.HISTORY[RangeBySeason]", "By season"],
  historyTipRange12: ["SkiResort.Hub.HISTORY_TIP[Range12]", "The last 12 months. The month in progress is the last bar."],
  historyTipRangeSeasons: ["SkiResort.Hub.HISTORY_TIP[RangeSeasons]", "This ski season, July to June, in colour. Last season's same months in grey behind it."],
  historyTipRangeBySeason: ["SkiResort.Hub.HISTORY_TIP[RangeBySeason]", "One bar per ski season, July to June: the average month, so a season with missing months compares fairly."],
  historyTipRangeOff: ["SkiResort.Hub.HISTORY_TIP[RangeOff]", "Not enough months recorded yet."],
  historyGuests: ["SkiResort.Hub.HISTORY[Guests]", "Guests"],
  historyFinances: ["SkiResort.Hub.HISTORY[Finances]", "Finances"],
  historyBoth: ["SkiResort.Hub.HISTORY[Both]", "Both"],
  historyAttraction: ["SkiResort.Hub.HISTORY[Attraction]", "Attraction"],
  historyTipAttraction: ["SkiResort.Hub.HISTORY_TIP[Attraction]", "The attraction score when each month closed. A season shows its average."],
  historyTourists: ["SkiResort.Hub.HISTORY[Tourists]", "Tourists"],
  historyResidents: ["SkiResort.Hub.HISTORY[Residents]", "Residents"],
  historySkiDays: ["SkiResort.Hub.HISTORY[SkiDays]", "Ski-day trips"],
  historyTickets: ["SkiResort.Hub.HISTORY[Tickets]", "Lift tickets"],
  historyLodge: ["SkiResort.Hub.HISTORY[Lodge]", "Lodge spend"],
  historyPasses: ["SkiResort.Hub.HISTORY[Passes]", "Season passes"],
  historyLessons: ["SkiResort.Hub.HISTORY[Lessons]", "Lessons"],
  historyLifts: ["SkiResort.Hub.HISTORY[Lifts]", "Lifts"],
  historyRuns: ["SkiResort.Hub.HISTORY[Runs]", "Runs"],
  historyStaff: ["SkiResort.Hub.HISTORY[Staff]", "Buildings and wages"],
  historyNet: ["SkiResort.Hub.HISTORY[Net]", "Net"],
  historyIncome: ["SkiResort.Hub.HISTORY[Income]", "Income"],
  historyCosts: ["SkiResort.Hub.HISTORY[Costs]", "Costs"],
  historyLastSeason: ["SkiResort.Hub.HISTORY[LastSeason]", "Last season"],
  historySeasonTotal: ["SkiResort.Hub.HISTORY[SeasonTotal]", "Season total"],
  historyEmpty: ["SkiResort.Hub.HISTORY[Empty]", "No month recorded yet. The first one closes at the end of this month."],
  historyUnlinked: ["SkiResort.Hub.HISTORY[Unlinked]", "Not linked keeps no history. Whole city shows every ski area."],
  historyPartial: ["SkiResort.Hub.HISTORY[Partial]", "Partial: counted for part of the month only"],
  historyPartialSeason: ["SkiResort.Hub.HISTORY[PartialSeason]", "Partial: some months are missing or partial"],
  historyCurrent: ["SkiResort.Hub.HISTORY[Current]", "So far this month"],
  historyCurrentSeason: ["SkiResort.Hub.HISTORY[CurrentSeason]", "So far this season"],
  historyGap: ["SkiResort.Hub.HISTORY[Gap]", "Nothing recorded"],
  historyTipYoy: ["SkiResort.Hub.HISTORY_TIP[Yoy]", "The last whole month against the same month a year earlier."],
  historyTipStaff: ["SkiResort.Hub.HISTORY_TIP[Staff]", "The game's bill for buildings and wages, at the rate when the month closed."],
  historyTipChart: ["SkiResort.Hub.HISTORY_TIP[Chart]", "Point at a bar for its numbers. Faded bars are partial months; hatched slots recorded nothing."],
} as const;

/** Placeholder entries: ONLY as <LocText entry={HUB_FMT.x} args={...} /> (gate R5, R13). */
export const HUB_FMT = {
  resortPosition: ["SkiResort.Hub.RESORT_POSITION", "{N} / {COUNT}"],
  // T97 P1: a selector item's counts (hubRules.resortCountsVariant picks; "one" is HUB_LOC.resortCountsOne).
  resortCounts: ["SkiResort.Hub.RESORT_COUNTS[Many]", "{RUNS} runs · {LODGES} lodges"],
  resortCountsRunOne: ["SkiResort.Hub.RESORT_COUNTS[RunOne]", "1 run · {LODGES} lodges"],
  resortCountsLodgeOne: ["SkiResort.Hub.RESORT_COUNTS[LodgeOne]", "{RUNS} runs · 1 lodge"],
  contractMismatchCore: ["SkiResort.Hub.CONTRACT_MISMATCH_CORE", "This panel (version {UI}) and the mod's DLL (version {DLL}) come from different builds. Building still works; the resort statistics are hidden until the mod is reinstalled."],
  // status (6.2)
  statusLine: ["SkiResort.Hub.STATUS_LINE", "{STATE} · {OPEN} of {TOTAL} runs open"],
  statusLineOne: ["SkiResort.Hub.STATUS_LINE_ONE", "{STATE} · {OPEN} of 1 run open"],
  statusLineEst: ["SkiResort.Hub.STATUS_LINE_EST", "{STATE} · ≈ {OPEN} of {TOTAL} runs open"],
  statusLineOneEst: ["SkiResort.Hub.STATUS_LINE_ONE_EST", "{STATE} · ≈ {OPEN} of 1 run open"],
  statusLinePartly: ["SkiResort.Hub.STATUS_LINE_PARTLY", "{STATE} · {PARTLY} of {TOTAL} runs patchy"],
  statusLinePartlyOne: ["SkiResort.Hub.STATUS_LINE_PARTLY_ONE", "{STATE} · {PARTLY} of 1 run patchy"],
  statusLinePartlyEst: ["SkiResort.Hub.STATUS_LINE_PARTLY_EST", "{STATE} · ≈ {PARTLY} of {TOTAL} runs patchy"],
  statusLinePartlyOneEst: ["SkiResort.Hub.STATUS_LINE_PARTLY_ONE_EST", "{STATE} · ≈ {PARTLY} of 1 run patchy"],
  statusTipCounts: ["SkiResort.Hub.STATUS_TIP_COUNTS", "Open {OPEN} · patchy {PARTLY} · closed {CLOSED}"],
  statusTipTemp: ["SkiResort.Hub.STATUS_TIP_TEMP", "Day average at the top of the highest run: {TEMP}"],
  /** Round S1 D29: the status tooltip line while powder > 0 ({PCT} = skiDaysPowderPct). */
  statusTipPowder: ["SkiResort.Hub.STATUS_TIP_POWDER", "Fresh snow at the top: powder {PCT}. More skiers come."],
  statusSnow: ["SkiResort.Hub.STATUS_SNOW", "{CM} cm · {PCT}"],
  // problems (6.3)
  problemLodgesSomeOff: ["SkiResort.Hub.PROBLEM[LodgesSomeOff]", "Lodges switched off: {N}"],
  problemEfficiency: ["SkiResort.Hub.PROBLEM[Efficiency]", "Welcome Sign at {PCT}: {REASON}"],
  problemEfficiencyMore: ["SkiResort.Hub.PROBLEM[EfficiencyMore]", "Welcome Sign at {PCT}: {REASON} and {N} more"],
  problemNotRunning: ["SkiResort.Hub.PROBLEM[NotRunning]", "Welcome Sign not running: {REASON}"],
  problemNotRunningMore: ["SkiResort.Hub.PROBLEM[NotRunningMore]", "Welcome Sign not running: {REASON} and {N} more"],
  problemLiftNoPower: ["SkiResort.Hub.PROBLEM[LiftNoPower]", "{NAME} has no power"],
  problemLiftSectionMissing: ["SkiResort.Hub.PROBLEM[LiftSectionMissing]", "{NAME} is missing a section"],
  problemLiftUnfinished: ["SkiResort.Hub.PROBLEM[LiftUnfinished]", "{NAME} is unfinished"],
  problemStationsPair: ["SkiResort.Hub.PROBLEM[StationsPair]", "{NAME}: draw the lift line"],
  problemStationsBaseOnly: ["SkiResort.Hub.PROBLEM[StationsBaseOnly]", "{NAME}: place its top station"],
  problemStationsTopOnly: ["SkiResort.Hub.PROBLEM[StationsTopOnly]", "{NAME}: place its base station"],
  problemLiftChairsStopped: ["SkiResort.Hub.PROBLEM[LiftChairsStopped]", "{NAME}: chairs not moving"],
  // Contract 6: codes 23 (D18) and 24 (D11).
  problemMixedLiftTypes: ["SkiResort.Hub.PROBLEM[MixedLiftTypes]", "{NAME} mixes lift types"],
  problemSummitLodgeNotLiftServed: ["SkiResort.Hub.PROBLEM[SummitLodgeNotLiftServed]", "Summit lodges no lift reaches: {N}"],
  problemRunsUnserved: ["SkiResort.Hub.PROBLEM[RunsUnserved]", "Runs unconnected to a lift: {N}"],
  problemRunsUnrateable: ["SkiResort.Hub.PROBLEM[RunsUnrateable]", "Runs that cannot be rated: {N}"],
  problemRunsRating: ["SkiResort.Hub.PROBLEM[RunsRating]", "Runs being rated: {N}"],
  problemRunsFlat: ["SkiResort.Hub.PROBLEM[RunsFlat]", "Runs too flat to score: {N}"],
  problemFreeLodges: ["SkiResort.Hub.PROBLEM[FreeLodges]", "Lodges on no Welcome Sign: {N}"],
  problemMore: ["SkiResort.Hub.PROBLEM_MORE", "+{N}"],
  problemTipNoLift: ["SkiResort.Hub.PROBLEM_TIP[NoLift]", "A run counts {PCT} until a running lift reaches its top: within 150 m of the line, at most 15 m above the top station."],
  problemTipRunsUnserved: ["SkiResort.Hub.PROBLEM_TIP[RunsUnserved]", "Each counts {PCT} until a running lift reaches its top: within 150 m of the line, at most 15 m above the top station."],
  problemGainEfficiency: ["SkiResort.Hub.PROBLEM_GAIN[Efficiency]", "At full efficiency the attraction would be {FULL}."],
  problemGainLodge: ["SkiResort.Hub.PROBLEM_GAIN[Lodge]", "With a lodge, the attraction would be {A}."],
  problemGainLodgesOn: ["SkiResort.Hub.PROBLEM_GAIN[LodgesOn]", "With them switched on, the attraction would be {A}."],
  staffLine: ["SkiResort.Hub.STAFF_LINE", "Staff: {FILLED} of {JOBS}"],
  // tiles (6.4)
  tileTipFull: ["SkiResort.Hub.TILE_TIP_FULL", "At full efficiency: {N}"],
  tileTipStaffing: ["SkiResort.Hub.TILE_TIP_STAFFING", "× efficiency {PCT}"],
  tileTipPartly: ["SkiResort.Hub.TILE_TIP_PARTLY", "Patchy: {N}"],
  tileTipUnfinished: ["SkiResort.Hub.TILE_TIP_UNFINISHED", "Not complete yet: {N}"],
  tileTipLast: ["SkiResort.Hub.TILE_TIP_LAST", "Month of {MONTH}: {N}"],
  // T77 (contract 9): {MONTH} = the month's name (monthName(city.month)).
  tileTipGuests: ["SkiResort.Hub.TILE_TIP[Guests]", "Month of {MONTH}: tourists who arrived at the lodges or the Welcome Sign, and residents who visited a lodge."],
  // overview (6.5)
  scoreBarTip: ["SkiResort.Hub.SCORE_BAR_TIP", "{NOW} now, {FULL} at full efficiency, out of 200. Ticks: a park scores about 10, a tourist attraction 50, a landmark 120."],
  receiptTipLodges: ["SkiResort.Hub.RECEIPT_TIP_LODGES", "Lodges counted: {N}"],
  receiptTipTerrain: ["SkiResort.Hub.RECEIPT_TIP[Terrain]", "{K} is the Attractiveness scale in Options. Adding a run never lowers the total."],
  receiptParts: ["SkiResort.Hub.RECEIPT_PARTS", "(runs {RUNS} + lifts {LIFTS}) × variety {VARIETY} × vertical {VERTICAL}"],
  /** Contract 6: RECEIPT_PARTS with the school term, while ResortLive.schoolValue > 0 (hubRules.receiptPartsVariant). */
  receiptPartsSchool: ["SkiResort.Hub.RECEIPT_PARTS_SCHOOL", "(runs {RUNS} + lifts {LIFTS} + school {SCHOOL}) × variety {VARIETY} × vertical {VERTICAL}"],
  /** T77 (Richard item 9): the second line under Runs and lifts. The row's value IS {K} × the square root of the parts line (HubRules.Terrain =
   *  K × sqrt(S), S = (runs + lifts + school) × variety × vertical, SkiResortScore.cs:468); {TERRAIN} = ResortLive.terrain, nothing is multiplied
   *  in the UI (D10). */
  receiptRoot: ["SkiResort.Hub.RECEIPT_ROOT", "{K} × the square root of the line above = {TERRAIN}"],
  /** Contract 6: the lodge-base tooltip's best lodge ({NAME} = its LODGE_TIER word) and the others' extras ({E}). */
  receiptTipLodgeBest: ["SkiResort.Hub.RECEIPT_TIP_LODGE_BEST", "Best lodge: {NAME}; the others add {E}."],
  receiptTipVertical: ["SkiResort.Hub.RECEIPT_TIP[Vertical]", "From × 1 at 0 m to × 2 at 600 m or more. Now {M}, lowest bottom to highest top of the runs that count."],
  receiptOpen: ["SkiResort.Hub.RECEIPT_OPEN", "Runs open now: {PCT} of their full value"],
  receiptTipOpen: ["SkiResort.Hub.RECEIPT_TIP[Open]", "Closed runs add nothing and patchy runs add part, so snow moves the score. With every run fully open, runs would add {FULL}."],
  receiptTimes: ["SkiResort.Hub.RECEIPT_TIMES", "× {PCT}"],
  diffTipPending: ["SkiResort.Hub.DIFF_TIP_PENDING", "Being rated: {N}"],
  diffTipUnrateable: ["SkiResort.Hub.DIFF_TIP_UNRATEABLE", "Cannot be rated: {N}"],
  areaHa: ["SkiResort.Hub.AREA_HA", "{HA} ha"],
  snowDepth: ["SkiResort.Hub.SNOW_DEPTH", "{CM} cm"],
  // runs view (6.6)
  runOpenOpen: ["SkiResort.Hub.RUN_OPEN[Open]", "Open: snow on {PCT} of it"],
  runOpenPartly: ["SkiResort.Hub.RUN_OPEN[Partly]", "Patchy: snow on {PCT} of it"],
  runOpenClosed: ["SkiResort.Hub.RUN_OPEN[Closed]", "Closed: snow on {PCT} of it"],
  runServed: ["SkiResort.Hub.RUN_SERVED", "Served by {NAME}"],
  runNotServed: ["SkiResort.Hub.RUN_NOT_SERVED", "No running lift reaches its top: it counts {PCT}"],
  runValueOf: ["SkiResort.Hub.RUN_VALUE_OF", "{NOW} of {FULL}"],
  runValueParts: ["SkiResort.Hub.RUN_VALUE_PARTS", "difficulty {B} × size {S} × type {T} × lift {L} × open {O}"],
  // lifts view (6.7)
  liftDetail: ["SkiResort.Hub.LIFT_DETAIL", "{LENGTH} · rise {RISE} · {MIN} min ride · {CAP} skiers/h · {N} runs"],
  liftDetailOne: ["SkiResort.Hub.LIFT_DETAIL_ONE", "{LENGTH} · rise {RISE} · {MIN} min ride · {CAP} skiers/h · 1 run"],
  liftDetailNone: ["SkiResort.Hub.LIFT_DETAIL_NONE", "{LENGTH} · rise {RISE} · {MIN} min ride · {CAP} skiers/h · no runs"],
  liftRideMin: ["SkiResort.Hub.LIFT_RIDE_MIN", "{MIN} min"],
  // Contract 6 (CG-T2 / CG-T3: no baked number but the game-speed "1×"): LiftRow.seats / speedMps per lift type.
  // Catalog Phase C (round-3-contract C15, CG-T7): both texts reworded to hold for the tram too (identical in LocaleEN.cs, R4).
  liftRideTip: ["SkiResort.Hub.LIFT_RIDE_TIP", "Base to top at {SPEED} m/s line speed, at 1× game speed."],
  // Phase D (round-4-contract section 4.4 with skeptic MUST-3, 24 words; CG-T2 / T7 / T8): an unpowered lift counts none.
  liftCapacityTip: ["SkiResort.Hub.LIFT_CAPACITY_TIP", "{SEATS} seats per carrier at {SPEED} m/s; a tram counts its station stops. None without power or an open run. Riders are not simulated."],
  capacityValue: ["SkiResort.Hub.CAPACITY_VALUE", "{CAP} skiers/h"],
  // guests view (6.8)
  // T77 (contract 9): "Month of {MONTH}" is the one way the hub names a month; {MONTH} = monthName(city.month), last month's = city.month - 1.
  guestsMonthThis: ["SkiResort.Hub.GUESTS_MONTH[This]", "Month of {MONTH}"],
  guestsMonthLast: ["SkiResort.Hub.GUESTS_MONTH[Last]", "Month of {MONTH}"],
  /** Round S1 F7: bar 2's label while last month was counted only since the load (hubRules.lastMonthMark). */
  guestsMonthLastPartial: ["SkiResort.Hub.GUESTS_MONTH[LastPartial]", "Month of {MONTH}, partial"],
  guestsTipPaid: ["SkiResort.Hub.GUESTS_TIP[Paid]", "Month of {MONTH}: {GUESTS} guests, {PAID} paid, {HOLDERS} on a pass, {UNPAID} could not pay, {FREE} free while tickets were off or closed."],
  ticketsNoteOpen: ["SkiResort.Hub.TICKETS_NOTE[Open]", "Each guest without a season pass pays {PRICE} on arrival. Set the price in Options."],
  /** Round S1: both guest bars' tooltip, {MAX} = hubRules.guestBarScale. */
  guestsBarScale: ["SkiResort.Hub.GUESTS_BAR_SCALE", "Full bar: {MAX} guests"],
  /** Round S1 D27 with ruling R-Q3 (s1-spec 5.3): {N} = skiDaysCap, the capacity at once (D21). */
  skiDaysRoomRuns: ["SkiResort.Hub.SKI_DAYS[RoomRuns]", "Holds {N} skiers at once, set by the runs"],
  skiDaysRoomLodges: ["SkiResort.Hub.SKI_DAYS[RoomLodges]", "Holds {N} skiers at once, set by the lodges"],
  skiDaysRoom: ["SkiResort.Hub.SKI_DAYS[Room]", "Holds {N} skiers at once"],
  /** The Ski days row's tooltip: the three classes partition {SENT} ("close by" = the walk class, ch4 RF-7). */
  skiDaysTipSent: ["SkiResort.Hub.SKI_DAYS_TIP[Sent]", "Month of {MONTH}: {SENT} sent, {ARRIVED} reached a lodge"],
  /** T80: the split of the sent skiers ("close by" = the walk class, sent - car - no car). */
  skiDaysTipSplit: ["SkiResort.Hub.SKI_DAYS_TIP[Split]", "Close by {WALK} · by car {CAR} · far, no car {FAR}"],
  skiDaysTipNow: ["SkiResort.Hub.SKI_DAYS_TIP[Now]", "On the hill or on the way now: {NOW} of {CAP}"],
  /** Round M2 R9: {NAME} = CityStats.tripPlanner, the known mod's name (hubRules.tripPlannerNote "known" / "knownOff"). */
  tripPlannerNote: ["SkiResort.Hub.TRIP_PLANNER_NOTE[Known]", "Detected: {NAME}. It replaces the game's trip planner, so trip times can differ."],
  tripPlannerNoteOff: ["SkiResort.Hub.TRIP_PLANNER_NOTE[KnownOff]", "Ski days off: {NAME} replaced the game's leisure systems."],
  // money view (6.9)
  moneyTicketsLast: ["SkiResort.Hub.MONEY_TICKETS[Last]", "Lift tickets, Month of {MONTH}: {N} paid"],
  moneyColMonth: ["SkiResort.Hub.MONEY_COL_MONTH", "Month of {MONTH} so far"],
  moneyColMonthPartial: ["SkiResort.Hub.MONEY_COL_MONTH_PARTIAL", "Month of {MONTH} so far (partial)"],
  moneyTipPassesMonth: ["SkiResort.Hub.MONEY_TIP[PassesMonth]", "Sold this month so far, in the net now. Of it, the pre-season sale: {PRE}."],
  moneyTipLiftsRate: ["SkiResort.Hub.MONEY_TIP[LiftsRate]", "Booked this month so far. At today's rate: {RATE} per month."],
  moneyTipSoFar: ["SkiResort.Hub.MONEY_TIP[SoFar]", "{RATE} per month at today's budget; {SHARE} of the month so far."],
  moneyRowPasses: ["SkiResort.Hub.MONEY_ROW[Passes]", "Season passes sold this season · {N}"],
  moneyRowPassHolders: ["SkiResort.Hub.MONEY_ROW[PassHolders]", "Pass holders skiing, Month of {MONTH}"],
  /** Contract 8 (econ-2 round 2): {N} = the pre-season sale's passes (Integer) / the lessons taken this month (Integer). */
  moneyRowPassesPre: ["SkiResort.Hub.MONEY_ROW[PassesPre]", "Season passes sold before the season · {N}"],
  moneyRowLessons: ["SkiResort.Hub.MONEY_ROW[Lessons]", "Lessons, Month of {MONTH} · {N}"],
  moneyTicketsPrice: ["SkiResort.Hub.MONEY_TICKETS_PRICE", "Lift ticket price now: {PRICE}"],
  /** Round M2 R7: {PRICE} = hubRules.hubPrice (Money), {PCT} = hubRules.priceFactorPct (Percentage). */
  moneyPriceLine: ["SkiResort.Hub.MONEY_PRICE_LINE", "Lift ticket {PRICE} · ski days {PCT}"],
  /** {FULL} = PRICE_NO_EFFECT_MAX, {P1..P3} = PRICE_EXAMPLES (Money), {F1..F3} = their priceFactorPct (Percentage). */
  moneyPriceTipCurve: ["SkiResort.Hub.MONEY_PRICE_TIP[Curve]", "Ski days: as many at any price up to {FULL}, then fewer: {F1} as many at {P1}, {F2} at {P2}, {F3} at {P3}."],
  /** Contract 6 (D15 price tolerance; skeptic SHOULD: not "Luxury lodges", the Chalet has 20): {FULL} = 40 + priceTolerance. */
  moneyPriceTipTolerance: ["SkiResort.Hub.MONEY_PRICE_TIP[Tolerance]", "This ski area's lodges keep ski days full up to {FULL}."],
  moneyRowNatural: ["SkiResort.Hub.MONEY_ROW[Natural]", "Natural runs · {HA} ha"],
  moneyRowGroomed: ["SkiResort.Hub.MONEY_ROW[Groomed]", "Groomed runs · {HA} ha with snow"],
  moneyRowSnowmaking: ["SkiResort.Hub.MONEY_ROW[Snowmaking]", "Snowmaking runs · {HA} ha with snow"],
  moneyRowNight: ["SkiResort.Hub.MONEY_ROW[Night]", "Night runs · {HA} ha with snow"],
  moneyRowLifts: ["SkiResort.Hub.MONEY_ROW[Lifts]", "Lifts · {N} · {LEN} of line"],
  // Phase D (DECISIONS 171 ruling 2, skeptic MUST-1; identical in LocaleEN.cs, R4, CG-T8): an unpowered lift still pays
  // in full; the two remaining reductions are unfinished and no open run (SkiMoneyRules.LiftUpkeepBillingOf).
  moneyTipLifts: ["SkiResort.Hub.MONEY_TIP[Lifts]", "{BASE} per month per lift + {PERKM} per km of line, in full even without power; a quarter while unfinished or with no open run. Now {FULL} in full, {REDUCED} at a quarter."],
  /** Contract 6: MONEY_TIP[Lifts] when two or more lift types are offered (each has its own rates, one
   *  MONEY_TIP_LIFT_RATE line per type follows). Phase D: reworded as MONEY_TIP[Lifts]. */
  moneyTipLiftsTypes: ["SkiResort.Hub.MONEY_TIP[LiftsTypes]", "Each lift: its kind's base per month + a rate per km of line, in full even without power; a quarter while unfinished or with no open run. Now {FULL} in full, {REDUCED} at a quarter."],
  moneyTipLiftRate: ["SkiResort.Hub.MONEY_TIP_LIFT_RATE", "{TYPE}: {BASE} per month + {PERKM} per km"],
  moneyTipBuildings: ["SkiResort.Hub.MONEY_TIP[Buildings]", "Upkeep {UPKEEP} · wages {WAGES} for {FILLED} of {JOBS} staff, at the Parks & Recreation budget."],
  // build strip (6.10)
  buildLineRun: ["SkiResort.Hub.BUILD_LINE_RUN", "{NAME} · {FEE} + {PERHA} per ha"],
  buildLineCost: ["SkiResort.Hub.BUILD_LINE_COST", "{NAME} · {COST}"],
  buildLineLift: ["SkiResort.Hub.BUILD_LINE_LIFT", "{NAME} · about {PERKM} per km of line"],
  buildOnceRun: ["SkiResort.Hub.BUILD_ONCE_RUN", "{FEE} + {PERHA} per ha"],
  buildOnceLift: ["SkiResort.Hub.BUILD_ONCE_LIFT", "about {PERKM} per km of line"],
  buildMonthRun: ["SkiResort.Hub.BUILD_MONTH_RUN", "{RATE} per ha with snow"],
  buildMonthLift: ["SkiResort.Hub.BUILD_MONTH_LIFT", "{BASE} per complete lift + {PERKM} per km of line"],
  follow: ["SkiResort.Hub.FOLLOW", "Now showing {NAME}."],
  // history view (T78 build 2, contract 10): {MONTH} = monthName, {YEAR} = the game year as plain digits, {FROM}/{TO} = a season's years.
  historyMonthYear: ["SkiResort.Hub.HISTORY_MONTH", "Month of {MONTH} {YEAR}"],
  historySeason: ["SkiResort.Hub.HISTORY_SEASON", "Ski season {FROM}/{TO}"],
  historyHeadGuests: ["SkiResort.Hub.HISTORY_HEAD[Guests]", "Guests, {MONTH}"],
  historyHeadNet: ["SkiResort.Hub.HISTORY_HEAD[Net]", "Net, {MONTH}"],
  historyTipBefore: ["SkiResort.Hub.HISTORY_TIP[Before]", "Month of {MONTH} {YEAR}: {N}"],
  historySeasonMonths: ["SkiResort.Hub.HISTORY_SEASON_MONTHS", "{N} of 12 months · the average month"],
  historyVs: ["SkiResort.Hub.HISTORY_VS", "vs {MONTH} {YEAR}"],
} as const;

/** STATUS_WORD by hubRules.statusWord (6.2). */
export const STATUS_WORD: Readonly<Record<StatusWord, LocEntry>> = {
  open: ["SkiResort.Hub.STATUS_WORD[Open]", "Open"],
  partly: ["SkiResort.Hub.STATUS_WORD[Partly]", "Partly open"],
  closed: ["SkiResort.Hub.STATUS_WORD[Closed]", "Closed"],
};

/** Round F7 D1: the Runs view's state word of one run, index = OPEN_STATE value (0 Closed, 1 Partly, 2 Open;
 *  -1 None has no word). "Patchy" = partly covered: never "Partly", which STATUS_WORD uses for the area. */
export const RUN_STATE_TEXT: readonly LocEntry[] = [
  ["SkiResort.Hub.RUN_STATE[Closed]", "Closed"],
  ["SkiResort.Hub.RUN_STATE[Patchy]", "Patchy"],
  ["SkiResort.Hub.RUN_STATE[Open]", "Open"],
  ["SkiResort.Hub.RUN_STATE[Unsuitable]", "Unsuitable"],   // T54 probe 1: OPEN_STATE.Unsuitable = 3, a cat track rated blue or steeper
];

/** SEASON_HINT words, index = SEASON_HINT value (0 None = no text). */
export const SEASON_HINT_TEXT: ReadonlyArray<LocEntry | null> = [
  null,
  ["SkiResort.Hub.SEASON_HINT[SnowSeason]", "Snow season"],
  ["SkiResort.Hub.SEASON_HINT[SnowGunsOnly]", "Snow guns only"],
  ["SkiResort.Hub.SEASON_HINT[Thawing]", "Thawing"],
  ["SkiResort.Hub.SEASON_HINT[WaitingForSnow]", "Waiting for snow"],
  ["SkiResort.Hub.SEASON_HINT[TooWarm]", "Too warm for snow"],
  ["SkiResort.Hub.SEASON_HINT[GatingOff]", "Snow rules off"],
];

/** SEASON_TIP, index = SEASON_HINT value (None and GatingOff: none; STATUS_TIP_GATING says it). Round M2 R5
 *  (thaw-rules.md section 2 item 16): each tip says what C# HubRules.Season read - the 24-hour mean at the top
 *  for natural snow, and the live gun gates ("snow guns running now"). */
export const SEASON_TIP_TEXT: ReadonlyArray<LocEntry | null> = [
  null,
  ["SkiResort.Hub.SEASON_TIP[SnowSeason]", "Day average at the top at or below freezing: natural snow can fall."],
  ["SkiResort.Hub.SEASON_TIP[SnowGunsOnly]", "Day average at the top above freezing, but snow guns are running now."],
  ["SkiResort.Hub.SEASON_TIP[Thawing]", "Day average at the top above freezing and no snow gun running now. The runs are open on the snow that is left."],
  ["SkiResort.Hub.SEASON_TIP[WaitingForSnow]", "Cold enough (day average at the top at or below freezing, or snow guns running), but no run has enough snow yet."],
  ["SkiResort.Hub.SEASON_TIP[TooWarm]", "Day average at the top above freezing, too warm for snow guns. Runs reopen when it cools, or turn off Snow & season gating in Options."],
  null,
];

/** LIFT_STATUS words, index = LIFT_STATUS value. */
export const LIFT_STATUS_TEXT: readonly LocEntry[] = [
  ["SkiResort.Hub.LIFT_STATUS[Running]", "Running"],
  ["SkiResort.Hub.LIFT_STATUS[NoPower]", "No power"],
  ["SkiResort.Hub.LIFT_STATUS[ChairsStopped]", "Chairs stopped"],
  ["SkiResort.Hub.LIFT_STATUS[SectionMissing]", "Section missing"],
  ["SkiResort.Hub.LIFT_STATUS[Unfinished]", "Unfinished"],
];

export function liftStatusEntry(status: number): LocEntry {
  return status >= 0 && status < LIFT_STATUS_TEXT.length ? LIFT_STATUS_TEXT[status] : LIFT_STATUS_TEXT[4];
}

/** D17 run-type names (sentence case), by RunKind. */
export const RUN_TYPE: Readonly<Record<RunKind, LocEntry>> = {
  0: ["SkiResort.Hub.RUN_TYPE[Groomed]", "Groomed run"],
  1: ["SkiResort.Hub.RUN_TYPE[Snowmaking]", "Snowmaking run"],
  2: ["SkiResort.Hub.RUN_TYPE[Night]", "Night run"],
  3: ["SkiResort.Hub.RUN_TYPE[Natural]", "Natural run"],
  4: ["SkiResort.Hub.RUN_TYPE[CatTrack]", "Cat track"],
};

/** Footer line 2 short hint of a run tile, by RunKind (6.10). */
export const BUILD_SHORT_RUN: Readonly<Record<RunKind, LocEntry>> = {
  0: ["SkiResort.Hub.BUILD_SHORT[Groomed]", "Snow laid whenever it freezes; scores more on easy runs."],
  1: ["SkiResort.Hub.BUILD_SHORT[Snowmaking]", "Snow guns open it earlier and keep it open longer."],
  2: ["SkiResort.Hub.BUILD_SHORT[Night]", "Floodlit snowmaking run: scores 25 % more."],
  3: ["SkiResort.Hub.BUILD_SHORT[Natural]", "Natural snow only, no running cost; easy runs score less."],
  4: ["SkiResort.Hub.BUILD_SHORT[CatTrack]", "Green only; crosses other runs. Not part of the score."],
};

/** Footer idle lines (NEXT, NEXT2) by hubRules.nextVariant. NEXT2[Sign] is BUILD_HINT_SIGN (no own key). */
export const NEXT_LINE1: Readonly<Record<NextVariant, LocEntry>> = {
  sign: ["SkiResort.Hub.NEXT[Sign]", "Next: place a Ski Area Welcome Sign from the Buildings tab."],
  unlinked: ["SkiResort.Hub.NEXT[Unlinked]", "Runs and lifts link to the nearest Welcome Sign within 5 km."],
  lodge: ["SkiResort.Hub.NEXT[Lodge]", "Next: a lodge near this ski area's Welcome Sign, from the Buildings tab."],
  lift: ["SkiResort.Hub.NEXT[Lift]", "Next: in the Lifts tab, a base station, a top station, then the line."],
  run: ["SkiResort.Hub.NEXT[Run]", "Next: draw a run within 5 km of this Welcome Sign, from the Runs tab."],
  done: ["SkiResort.Hub.NEXT[Done]", "Hover a tile to see what it builds and what it costs."],
};
export const NEXT_LINE2: Readonly<Record<NextVariant, LocEntry>> = {
  sign: HUB_LOC.buildHintSign,
  unlinked: ["SkiResort.Hub.NEXT2[Unlinked]", "Place a sign near them to link them."],
  lodge: ["SkiResort.Hub.NEXT2[Lodge]", "Guests arrive at a lodge. Without one the attraction is 0."],
  lift: ["SkiResort.Hub.NEXT2[Lift]", "Runs that a lift serves count in full."],
  run: ["SkiResort.Hub.NEXT2[Run]", "Natural runs cost nothing to run; the others cost per hectare."],
  done: ["SkiResort.Hub.NEXT2[Done]", "Click a tile to build; click it again to stop."],
};

/** `Assets.NAME[<prefab>]` of the NON-run build items (runs use RUN_TYPE, D17; chapter 2 renames the
 *  run assets, so a run asset name here would fail R4 the day that lands). */
export const ASSET_NAME: Readonly<Partial<Record<BuildItemId, LocEntry>>> = {
  sign: ["Assets.NAME[SkiResortGate]", "Ski Area Welcome Sign"],
  lodge: ["Assets.NAME[SkiAFrameDayLodge]", "A-Frame Day Lodge"],
  // Contract 6 (section 8.6): the new upgrade buildings, lines and stations; D17 renames the fixed-grip stations.
  lodgeHut: ["Assets.NAME[SkiLodgeHut]", "Mountain Hut"],
  lodgeMid: ["Assets.NAME[SkiLodgeMid]", "Chalet Lodge"],
  // Names pass 2026-10-13 (NAMES-2026-10-13.md): the luxury tiers say "Swanky" (Richard's word for the big lodge).
  lodgeSummit: ["Assets.NAME[SkiLodgeSummit]", "Swanky Summit Lodge"],
  lodgeGrand: ["Assets.NAME[SkiLodgeGrand]", "Swanky Grand Lodge"],
  school: ["Assets.NAME[SkiSchool]", "Ski School"],
  patrolHut: ["Assets.NAME[SkiPatrolHut]", "Ski Patrol Hut"],
  patrolHQ: ["Assets.NAME[SkiPatrolHQ]", "Ski Patrol HQ"],
  lift: ["Assets.NAME[SkiChairliftHV]", "Fixed-Grip Triple Chairlift"],
  // Wording revision 2026-10-13 (NAMES-2026-10-13-check.md R1 / R2): every station says its lift type and its end
  // ("<lift type>: <end>"); the tram's are terminals, never "Tram Station". A tile's footer line 1 is "{NAME} · {COST}", one
  // nowrap ellipsis line (hub.module.scss .buildStatusLine): the longest name is 39 characters (width at small panel
  // sizes unverified).
  baseStation:["Assets.NAME[SkiBaseStationBuilding]", "Fixed-Grip Chairlift: Base Station"],
  topStation: ["Assets.NAME[SkiTopStationBuilding]", "Fixed-Grip Chairlift: Top Station"],
  liftDetachable: ["Assets.NAME[SkiDetachableHV]", "Detachable Quad Chairlift"],
  baseStationDetachable: ["Assets.NAME[SkiDQBaseStationBuilding]", "Detachable Quad Chairlift: Base Station"],
  topStationDetachable: ["Assets.NAME[SkiDQTopStationBuilding]", "Detachable Quad Chairlift: Top Station"],
  liftGondola: ["Assets.NAME[SkiGondolaHV]", "Gondola"],
  baseStationGondola: ["Assets.NAME[SkiGondolaBaseStationBuilding]", "Gondola: Base Station"],
  topStationGondola: ["Assets.NAME[SkiGondolaTopStationBuilding]", "Gondola: Top Station"],
  liftTram: ["Assets.NAME[SkiTramHV]", "Aerial Tram"],
  baseStationTram: ["Assets.NAME[SkiTramBaseStationBuilding]", "Aerial Tram: Base Terminal"],
  topStationTram: ["Assets.NAME[SkiTramTopStationBuilding]", "Aerial Tram: Top Terminal"],
};

/** Contract 6: the lift-type picker's chips and the Lifts view's type line, by LiftRow.type (LIFT_TYPE_IDS; an unknown
 *  id draws no word). Sentence case like RUN_TYPE. */
export const LIFT_TYPE: Readonly<Record<string, LocEntry>> = {
  fixedGrip: ["SkiResort.Hub.LIFT_TYPE[fixedGrip]", "Fixed-grip"],
  detachable: ["SkiResort.Hub.LIFT_TYPE[detachable]", "Detachable"],
  gondola: ["SkiResort.Hub.LIFT_TYPE[gondola]", "Gondola"],
  tram: ["SkiResort.Hub.LIFT_TYPE[tram]", "Tram"],
};

/** Contract 6: the lodge-tier picker's chips (and the receipt's best lodge), by the tier's build item id. */
export const LODGE_TIER: Readonly<Partial<Record<BuildItemId, LocEntry>>> = {
  lodge: ["SkiResort.Hub.LODGE_TIER[lodge]", "A-frame"],
  lodgeHut: ["SkiResort.Hub.LODGE_TIER[lodgeHut]", "Hut"],
  lodgeMid: ["SkiResort.Hub.LODGE_TIER[lodgeMid]", "Chalet"],
  lodgeSummit: ["SkiResort.Hub.LODGE_TIER[lodgeSummit]", "Summit"],
  lodgeGrand: ["SkiResort.Hub.LODGE_TIER[lodgeGrand]", "Grand"],
};

/** T31 (2026-10-16): the Build strip's three tabs (hubRules.BuildTab), their tooltips, and the tiles' visible labels. */
export const BUILD_TAB: Readonly<Record<string, LocEntry>> = {
  lifts: ["SkiResort.Hub.BUILD_TAB[Lifts]", "Lifts"],
  runs: ["SkiResort.Hub.BUILD_TAB[Runs]", "Runs"],
  buildings: ["SkiResort.Hub.BUILD_TAB[Buildings]", "Buildings"],
};
export const BUILD_TAB_TIP: Readonly<Record<string, LocEntry>> = {
  lifts: ["SkiResort.Hub.BUILD_TAB_TIP[Lifts]", "Lifts: build the two stations, then the line. Pick the lift type below."],
  runs: ["SkiResort.Hub.BUILD_TAB_TIP[Runs]", "Runs: draw a run from the top of a lift, or plan trees with the tree tool."],
  buildings: ["SkiResort.Hub.BUILD_TAB_TIP[Buildings]", "Buildings: lodges, the ski school and patrol join this ski area's Welcome Sign. Pick the lodge below."],
};
/** A tile's visible label by its role: base / top / line (every lift type), sign, lodge (the chosen tier), school, patrolHut,
 *  patrolHQ, and the four run kinds (short: the Runs tab already says "run"; the tooltip and footer keep RUN_TYPE). */
export const BUILD_LABEL: Readonly<Record<string, LocEntry>> = {
  base: ["SkiResort.Hub.BUILD_LABEL[Base]", "Base station"],
  top: ["SkiResort.Hub.BUILD_LABEL[Top]", "Top station"],
  line: ["SkiResort.Hub.BUILD_LABEL[Line]", "Lift line"],
  sign: ["SkiResort.Hub.BUILD_LABEL[Sign]", "Welcome Sign"],
  lodge: ["SkiResort.Hub.BUILD_LABEL[Lodge]", "Lodge"],
  school: ["SkiResort.Hub.BUILD_LABEL[School]", "Ski school"],
  patrolHut: ["SkiResort.Hub.BUILD_LABEL[PatrolHut]", "Patrol hut"],
  patrolHQ: ["SkiResort.Hub.BUILD_LABEL[PatrolHQ]", "Patrol HQ"],
  run3: ["SkiResort.Hub.BUILD_LABEL[Natural]", "Natural"],
  run0: ["SkiResort.Hub.BUILD_LABEL[Groomed]", "Groomed"],
  run1: ["SkiResort.Hub.BUILD_LABEL[Snowmaking]", "Snowmaking"],
  run2: ["SkiResort.Hub.BUILD_LABEL[Night]", "Night"],
};

/** Band names (the drawing tooltip's keys), index = band 0..4. */
export const BAND_NAME: readonly LocEntry[] = [
  ["SkiResort.SlopeBand[Flat]", "Flat (too gentle to rate)"],
  ["SkiResort.SlopeBand[Green]", "Green circle"],
  ["SkiResort.SlopeBand[Blue]", "Blue square"],
  ["SkiResort.SlopeBand[Black]", "Black diamond"],
  ["SkiResort.SlopeBand[DoubleBlack]", "Double black diamond"],
];

export function bandEntry(band: number): LocEntry {
  if (band === BAND.Unrateable) return HUB_LOC.unrateable;
  return band >= BAND.Flat && band <= BAND.DoubleBlack ? BAND_NAME[band] : HUB_LOC.pending;
}

/** Glade level names (unchanged), index = level 1..8 (0 unused). */
export const LEVEL_NAME: readonly LocEntry[] = [
  ["SkiResort.GladeTooltip.LEVEL_NAME[1]", "Clear"],
  ["SkiResort.GladeTooltip.LEVEL_NAME[1]", "Clear"],
  ["SkiResort.GladeTooltip.LEVEL_NAME[2]", "Scattered trees"],
  ["SkiResort.GladeTooltip.LEVEL_NAME[3]", "Sparse"],
  ["SkiResort.GladeTooltip.LEVEL_NAME[4]", "Open glade"],
  ["SkiResort.GladeTooltip.LEVEL_NAME[5]", "Glade"],
  ["SkiResort.GladeTooltip.LEVEL_NAME[6]", "Open forest"],
  ["SkiResort.GladeTooltip.LEVEL_NAME[7]", "Forest"],
  ["SkiResort.GladeTooltip.LEVEL_NAME[8]", "Thick forest"],
];

export function gladeLevelEntry(level: number): LocEntry {
  return LEVEL_NAME[clampGladeLevel(level)];
}

/** Keys C# SENDS as data (build[].reason, build[].hint, glade.reason). HINT[LiftNoPair] text CHANGED. */
export const SENT_KEYS: readonly LocEntry[] = [
  ["SkiResort.Hub.REASON[Locked]", "Not unlocked yet"],
  ["SkiResort.Hub.REASON[NoSign]", "Place a Ski Area Welcome Sign first"],
  ["SkiResort.Hub.REASON[Missing]", "Asset not found: is the Modular Ski Resort asset pack installed?"],
  // Contract 6 (section 8.3): reason byte 4, on the SHOWN ski area's sign. (Byte 5 / REASON[OneLuxury], one luxury lodge
  // per ski area, was removed on 2026-10-11: no limit on how many lodges a player builds.)
  ["SkiResort.Hub.REASON[AlreadyBuilt]", "Already built at this ski area"],
  ["SkiResort.Hub.HINT[LiftNoPair]", "Place the other station first: a lift needs a base and a top station."],
  ["SkiResort.Hub.GLADE_REASON[Disabled]", "The tree tool hit an error and is off until the next load. Details are in the mod log."],
  ["SkiResort.Hub.GLADE_REASON[NotInGame]", "The tree tool works in a loaded city only."],
  ["SkiResort.Hub.GLADE_REASON[NoRuns]", "The ski run assets were not found: is the asset pack installed?"],
  ["SkiResort.Hub.GLADE_REASON[NoSpecies]", "No tree species to plant were found."],
];

export function sentKeyEntry(key: string | undefined | null, fallback: LocEntry): LocEntry {
  if (!key) return fallback;
  for (const e of SENT_KEYS) if (e[0] === key) return e;
  return [key, fallback[1]];
}

/**
 * The game's own efficiency factor names: `SelectedInfoPanel.EFFICIENCY_FACTORS[<enum name>]`
 * (EfficiencySection.cs:32-42; key format: hub-capabilities Verification fact 13). English fallbacks
 * read from the installed 1.6.2f1 Content/Game/Locale.cok (en-US block). NOT our keys: they are not in
 * LocaleEN.cs and gate R4 does not check them (its key pattern is SkiResort./Assets./SubServices.).
 */
export const EFFICIENCY_FACTOR_FALLBACK: Readonly<Record<string, string>> = {
  ServiceBudget: "Service Budget",
  NotEnoughEmployees: "Not Enough Employees",
  SickEmployees: "Sick Employees",
  EmployeeHappiness: "Employee Happiness",
  ElectricitySupply: "Lack of electricity",
  ElectricityFee: "Electricity fee",
  WaterSupply: "Lack of water",
  DirtyWater: "Polluted water",
  SewageHandling: "Backed up sewer",
  WaterFee: "Water fee",
  Garbage: "Piled up garbage",
  Telecom: "Network Quality",
  Mail: "Mail Handling",
  Disabled: "Disabled",
  Destroyed: "Destroyed",
  Abandoned: "Abandoned",
  Fire: "Fire",
};

/** The game's text for a factor (unknown name: the raw enum name as the fallback). */
export function factorEntry(name: string): LocEntry {
  return [`SelectedInfoPanel.EFFICIENCY_FACTORS[${name}]`, EFFICIENCY_FACTOR_FALLBACK[name] ?? name];
}

/**
 * T77 (contract 9): the game's own month names, `Common.MONTH:<0-11>` (an indexed entry: installed 1.6.2f1 index.js
 * `Common.MONTH:new Ec`, whose create() builds the id `${id}:${index}`; the vanilla birth-date picker renders index 0 for
 * January). Present in all 12 shipped locales of Content/Game/Locale.cok. NOT our keys: not in LocaleEN.cs, gate R4 does not
 * check them (like factorEntry). Fallbacks: English.
 */
const MONTH_FALLBACK: readonly string[] = [
  "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
];

/** The month's entry for a CityStats.month index (0..11); null when unknown (-1 or out of range). */
export function monthEntry(month: number): LocEntry | null {
  return Number.isInteger(month) && month >= 0 && month < 12 ? [`Common.MONTH:${month}`, MONTH_FALLBACK[month]] : null;
}

/** The {MONTH} argument: the month `back` months before city.month, translated; "–" while the month is not known. */
export function monthName(month: number, t: (e: LocEntry) => string, back = 0): string {
  const e = monthEntry(Number.isInteger(month) && month >= 0 ? (month - back + 12 * 4) % 12 : -1);
  return e ? t(e) : "–";
}

/** Every month's name, translated: width samples for a cell that shows a month. */
export function allMonthNames(t: (e: LocEntry) => string): string[] {
  return MONTH_FALLBACK.map((_, i) => monthName(i, t));
}

/**
 * T78 (contract 10): the game's short month names, `Common.MONTH_SHORT:<0-11>` (the same indexed entry as Common.MONTH, installed
 * 1.6.2f1 index.js `MONTH_SHORT:new Ec` @444971, rendered with {index} by the vanilla time-line charts @953286; Locale.cok holds
 * `Common.MONTH_SHORT:9` in all 12 locales: "Oct" in English, "Okt" in German, "10" in the CJK tables, so the index is 0-based like
 * Common.MONTH; checked 2026-10-18). The History
 * view's axis labels. NOT our keys: not in LocaleEN.cs (like monthEntry).
 */
const MONTH_SHORT_FALLBACK: readonly string[] = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** The short name of a calendar month 0..11, translated; "–" when out of range. */
export function monthShortName(month: number, t: (e: LocEntry) => string): string {
  return Number.isInteger(month) && month >= 0 && month < 12 ? t([`Common.MONTH_SHORT:${month}`, MONTH_SHORT_FALLBACK[month]]) : "–";
}
