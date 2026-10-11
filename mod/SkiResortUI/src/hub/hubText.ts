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
  sectionFailed: ["SkiResort.Hub.SECTION_FAILED", "This part of the panel hit an error. Details are in the mod log."],
  sectionTrees: ["SkiResort.Hub.SECTION[Trees]", "Trees"],

  // ---- header (6.1) ----
  resortPrev: ["SkiResort.Hub.RESORT_PREV", "Previous ski area"],
  resortNext: ["SkiResort.Hub.RESORT_NEXT", "Next ski area"],
  resortFly: ["SkiResort.Hub.RESORT_FLY", "Fly to the Welcome Sign"],
  unlinkedEntry: ["SkiResort.Hub.UNLINKED_ENTRY", "Not linked to a ski area"],
  unlinkedHint: ["SkiResort.Hub.UNLINKED_HINT", "More than 5 km from every Welcome Sign. Place a sign nearby to link them."],
  rename: ["SkiResort.Hub.RENAME", "Rename"],
  renameTip: ["SkiResort.Hub.RENAME_TIP", "Enter or clicking away keeps the name, Esc cancels, an empty name restores the default."],

  // ---- start card (6.11) ----
  startTitle: ["SkiResort.Hub.START_TITLE", "Start a ski area"],
  startStep1: ["SkiResort.Hub.START_STEP[1]", "Place a Welcome Sign"],
  startStep2: ["SkiResort.Hub.START_STEP[2]", "Connect the sign: road, power, water and sewage"],
  startStep3: ["SkiResort.Hub.START_STEP[3]", "Add a lodge to the sign"],
  startStep4: ["SkiResort.Hub.START_STEP[4]", "Build a lift: two stations, then the line"],
  startStep5: ["SkiResort.Hub.START_STEP[5]", "Draw runs from the top of the lift"],
  placeSign: ["SkiResort.Hub.PLACE_SIGN", "Place Welcome Sign"],

  // ---- status line (6.2) ----
  statusNoRuns: ["SkiResort.Hub.STATUS_NO_RUNS", "Closed · no runs yet"],
  statusUnlinked: ["SkiResort.Hub.STATUS_UNLINKED", "Not linked to a ski area"],
  statusTipRule: ["SkiResort.Hub.STATUS_TIP_RULE", "A run is open when snow (2 cm or more) covers 60 % of it, and closed below 40 %. In between it counts in part."],
  statusTipMeasured: ["SkiResort.Hub.STATUS_TIP_MEASURED", "Snow is measured on each run from the game's own snow."],
  statusTipEstimated: ["SkiResort.Hub.STATUS_TIP_ESTIMATED", "Some runs are estimated from the day's temperature until their snow can be measured."],
  statusTipGating: ["SkiResort.Hub.STATUS_TIP_GATING", "Snow & season gating is off in Options, so every run counts as open."],
  statusTipVisuals: ["SkiResort.Hub.STATUS_TIP_VISUALS", "Snow is hidden in your Gameplay settings. Runs still open on the simulated snow."],
  /** Round S1 D29: the hint slot at powder >= 0.5 (hubRules.statusHint). */
  powderDay: ["SkiResort.Hub.POWDER_DAY", "Powder day"],

  // ---- problems band (6.3) ----
  problemNone: ["SkiResort.Hub.PROBLEM[None]", "Nothing needs your attention."],
  problemUnlinked: ["SkiResort.Hub.PROBLEM[Unlinked]", "More than 5 km from every Welcome Sign"],
  problemNoLodge: ["SkiResort.Hub.PROBLEM[NoLodge]", "No lodge: guests have nowhere to arrive"],
  problemLodgesAllOff: ["SkiResort.Hub.PROBLEM[LodgesAllOff]", "All lodges are switched off"],
  problemNoRuns: ["SkiResort.Hub.PROBLEM[NoRuns]", "No runs yet: draw one to open"],
  problemSignNoRoad: ["SkiResort.Hub.PROBLEM[SignNoRoad]", "Welcome Sign is not at a road"],
  problemLodgeNoRoad: ["SkiResort.Hub.PROBLEM[LodgeNoRoad]", "Lodge is not at a road: guests cannot reach it"],
  // Round S1 D28: codes 20-22.
  problemSkiDaysCannotReach: ["SkiResort.Hub.PROBLEM[SkiDaysCannotReach]", "Many ski-day guests with a car never arrive"],
  problemNoParkingNearLodge: ["SkiResort.Hub.PROBLEM[NoParkingNearLodge]", "No parking near the lodge"],
  problemLodgeNotSlopeside: ["SkiResort.Hub.PROBLEM[LodgeNotSlopeside]", "Lodge is far from the runs"],
  problemTipNoLodge: ["SkiResort.Hub.PROBLEM_TIP[NoLodge]", "Guests arrive at a lodge, so a ski area without one has an attraction of 0. Place a lodge on the Welcome Sign with the lodge tile in the Build strip."],
  problemTipLodgesAllOff: ["SkiResort.Hub.PROBLEM_TIP[LodgesAllOff]", "A lodge that is switched off does not count. With none left, guests have nowhere to arrive and the attraction is 0. Switch one on in the Welcome Sign's info panel (Upgrades)."],
  problemTipLodgesSomeOff: ["SkiResort.Hub.PROBLEM_TIP[LodgesSomeOff]", "A lodge that is switched off does not count in the lodge base."],
  problemTipEfficiency: ["SkiResort.Hub.PROBLEM_TIP[Efficiency]", "The Welcome Sign needs staff, power, water and sewage. Its info panel shows which is short."],
  problemTipLiftNoPower: ["SkiResort.Hub.PROBLEM_TIP[LiftNoPower]", "Its base station gets no electricity, so the lift adds nothing and its runs count as having no lift. Connect a low-voltage line to it."],
  problemTipLiftSectionMissing: ["SkiResort.Hub.PROBLEM_TIP[LiftSectionMissing]", "A section of the line is being redrawn or was removed. The lift counts again as soon as the line is closed; until then it costs a quarter of its running cost."],
  problemTipLiftUnfinished: ["SkiResort.Hub.PROBLEM_TIP[LiftUnfinished]", "The line does not join a base station and a top station yet."],
  problemTipNoRuns: ["SkiResort.Hub.PROBLEM_TIP[NoRuns]", "Pick a run type in the Build strip and draw its outline within 5 km of the Welcome Sign."],
  problemTipStations: ["SkiResort.Hub.PROBLEM_TIP[Stations]", "A station is placed, but no line joins it to its partner yet."],
  problemTipLiftChairsStopped: ["SkiResort.Hub.PROBLEM_TIP[LiftChairsStopped]", "The lift is complete and still counts, but its chairs stopped. The mod log says why."],
  problemTipRunsUnrateable: ["SkiResort.Hub.PROBLEM_TIP[RunsUnrateable]", "Their outline is too large or too thin, or there is no terrain under it. Such a run counts very little."],
  problemTipRunsRating: ["SkiResort.Hub.PROBLEM_TIP[RunsRating]", "Their difficulty is being worked out, one run at a time. A new run counts 0 until then; a reshaped run keeps its last rating."],
  problemTipRunsFlat: ["SkiResort.Hub.PROBLEM_TIP[RunsFlat]", "A run rated Flat adds nothing to the score. Draw it on steeper ground."],
  problemTipFreeLodges: ["SkiResort.Hub.PROBLEM_TIP[FreeLodges]", "These lodges are not on a Welcome Sign, so they count nowhere."],
  problemTipSignNoRoad: ["SkiResort.Hub.PROBLEM_TIP[SignNoRoad]", "Move it so its front touches a road. Power, water and sewage come from that road."],
  problemTipLodgeNoRoad: ["SkiResort.Hub.PROBLEM_TIP[LodgeNoRoad]", "Move it so its deck side touches a road. Guests walk in from that road."],
  problemTipSkiDaysCannotReach: ["SkiResort.Hub.PROBLEM_TIP[SkiDaysCannotReach]", "Many recent ski-day guests with a car did not reach the lodge in time. Fewer are sent until more arrive."],
  problemTipNoParkingNearLodge: ["SkiResort.Hub.PROBLEM_TIP[NoParkingNearLodge]", "No car spaces within 300 m of the lodge. Guests who drive need a parking lot or a street with parking."],
  problemTipLodgeNotSlopeside: ["SkiResort.Hub.PROBLEM_TIP[LodgeNotSlopeside]", "Ski-day guests only go to a lodge within 800 m of this ski area's runs or lift stations."],
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
  tileMoney: ["SkiResort.Hub.TILE[Money]", "Money"],
  tileTipAttraction: ["SkiResort.Hub.TILE_TIP[Attraction]", "The ski area's score on the game's attractiveness scale (large park about 10, landmark 120). Its lodges carry it, though their own panel shows 0."],
  tileTipHeld: ["SkiResort.Hub.TILE_TIP_HELD", "Checking runs and snow after loading. The game keeps the saved score meanwhile."],
  tileTipRuns: ["SkiResort.Hub.TILE_TIP[Runs]", "Runs fully open now, of all runs of this ski area."],
  tileTipLifts: ["SkiResort.Hub.TILE_TIP[Lifts]", "Lifts that count (complete, with power at the base station), of the complete lifts. Without power the chairs still move, but the lift adds nothing."],
  tileTipGuests: ["SkiResort.Hub.TILE_TIP[Guests]", "Guests who arrived this month: tourists at the lodges and the Welcome Sign, and residents visiting a lodge."],
  tileTipSinceLoad: ["SkiResort.Hub.TILE_TIP_SINCE_LOAD", "Counted since you loaded the city."],
  tileTipMoney: ["SkiResort.Hub.TILE_TIP[Money]", "Resort money a month: lift tickets minus running costs and the game's bill for buildings and wages, at today's rates. What tourists spend in your city's shops and hotels is not included."],
  tileTipEst: ["SkiResort.Hub.TILE_TIP_EST", "≈ estimated: less than a full month of tickets has been counted."],
  tileTipEstBuildings: ["SkiResort.Hub.TILE_TIP_EST_BUILDINGS", "≈ The game's bill for buildings and wages is not known yet."],
  tileTipOpen: ["SkiResort.Hub.TILE_TIP_OPEN", "Click to show it below."],

  // ---- overview (6.5) ----
  sectionAttraction: ["SkiResort.Hub.SECTION[Attraction]", "Attraction"],
  scoreTickAttraction: ["SkiResort.Hub.SCORE_TICK[Attraction]", "tourist attraction 50"],
  scoreTickLandmark: ["SkiResort.Hub.SCORE_TICK[Landmark]", "landmark 120"],
  receiptLodgeBase: ["SkiResort.Hub.RECEIPT[LodgeBase]", "Lodge base"],
  receiptLodgeBaseHalf: ["SkiResort.Hub.RECEIPT[LodgeBaseHalf]", "Lodge base (half until a run counts)"],
  receiptLodgeBaseNone: ["SkiResort.Hub.RECEIPT[LodgeBaseNone]", "Lodge base (no lodge)"],
  receiptTipLodgeBase: ["SkiResort.Hub.RECEIPT_TIP[LodgeBase]", "20 for the first lodge, 4 more for each extra one, up to 12 more. A lodge that is switched off does not count. Half while no run counts (none open, or none worth anything yet): summer visitors only."],
  receiptTerrain: ["SkiResort.Hub.RECEIPT[Terrain]", "Runs and lifts"],
  receiptTipRuns: ["SkiResort.Hub.RECEIPT_TIP[Runs]", "Runs: the sum of each run's value now (difficulty, size, type, lift and snow). Each run's tooltip in the Runs view shows its own."],
  receiptTipLifts: ["SkiResort.Hub.RECEIPT_TIP[Lifts]", "Lifts: 6 for each complete lift with power; a quarter of that while no run counts."],
  receiptTipVariety: ["SkiResort.Hub.RECEIPT_TIP[Variety]", "Variety: 15 % more while a green, a blue and a black (or harder) run all count."],
  receiptOpenNone: ["SkiResort.Hub.RECEIPT_OPEN_NONE", "Runs open now: –"],
  receiptEfficiency: ["SkiResort.Hub.RECEIPT[Efficiency]", "Welcome Sign efficiency"],
  receiptTipEfficiency: ["SkiResort.Hub.RECEIPT_TIP[Efficiency]", "The game's efficiency of the Welcome Sign: staff, power, water, sewage, budget and more, as on its info panel. The score is multiplied by it."],
  receiptResult: ["SkiResort.Hub.RECEIPT[Result]", "Attraction"],
  receiptResultNoLodge: ["SkiResort.Hub.RECEIPT[ResultNoLodge]", "Attraction (no lodge: 0)"],
  receiptTipResult: ["SkiResort.Hub.RECEIPT_TIP[Result]", "(Lodge base + runs and lifts) × efficiency, at most 200. Each active lodge carries an equal part, so the game counts the ski area once. The game's own panel of a lodge shows Attractiveness 0; the ski area's score is this one, carried by its lodges."],
  detailShare: ["SkiResort.Hub.DETAIL[Share]", "Share of the city's tourist draw"],
  shareTip: ["SkiResort.Hub.SHARE_TIP", "This ski area's part of all attraction in the city, counted as the game counts it (the square of each building's score). It sets how many tourists come to the city, not where they go."],
  sectionMountain: ["SkiResort.Hub.SECTION[Mountain]", "Mountain"],
  difficulty: ["SkiResort.Hub.DIFFICULTY", "Difficulty"],
  pending: ["SkiResort.Hub.PENDING", "Being rated"],
  unrateable: ["SkiResort.Hub.UNRATEABLE", "Cannot be rated (outline too large or too thin, or no terrain under it)"],
  mountainVertical: ["SkiResort.Hub.MOUNTAIN[Vertical]", "Vertical"],
  mountainTipVertical: ["SkiResort.Hub.MOUNTAIN_TIP[Vertical]", "From the lowest run bottom to the highest run top. ≈ while runs are being rated."],
  area: ["SkiResort.Hub.AREA", "Skiable area"],
  areaTip: ["SkiResort.Hub.AREA_TIP", "The plan area of all runs of this ski area."],
  mountainSnow: ["SkiResort.Hub.MOUNTAIN[Snow]", "Snow now"],
  mountainSnowMeasured: ["SkiResort.Hub.MOUNTAIN[SnowMeasured]", "Snow now (measured)"],
  mountainSnowEstimated: ["SkiResort.Hub.MOUNTAIN[SnowEstimated]", "Snow now (estimated)"],
  mountainTipSnow: ["SkiResort.Hub.MOUNTAIN_TIP[Snow]", "Share of the run area with at least 2 cm of snow."],
  unlinkedNote: ["SkiResort.Hub.UNLINKED_NOTE", "These runs and lifts belong to no ski area, so they score nothing and draw no guests."],

  // ---- runs view (6.6) ----
  treeTool: ["SkiResort.Hub.TREE_TOOL", "Tree tool"],
  treeToolTip: ["SkiResort.Hub.TREE_TOOL_TIP", "Plan trees on and around your runs: turn the tool on, hover a run and click."],
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
  colLengthTip: ["SkiResort.Hub.COL_LENGTH_TIP", "Horizontal length, estimated from the run's outline"],
  colDropTip: ["SkiResort.Hub.COL_DROP_TIP", "Height difference between the highest and lowest points of the run's outline (approximate)"],
  runsEmpty: ["SkiResort.Hub.RUNS_EMPTY", "No runs yet. Pick a run type in the Build strip below and draw its outline."],
  runEstimated: ["SkiResort.Hub.RUN_ESTIMATED", "Estimated from the day's temperature."],
  runOpenGating: ["SkiResort.Hub.RUN_OPEN_GATING", "Counts as open: snow rules are off."],
  runOpenNone: ["SkiResort.Hub.RUN_OPEN_NONE", "Not linked: no snow state."],
  runServedAnon: ["SkiResort.Hub.RUN_SERVED_ANON", "A running lift reaches its top."],
  runTypeNote: ["SkiResort.Hub.RUN_TYPE_NOTE", "Natural green and blue runs count less; a groomed run counts in full."],
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
  liftRideTip: ["SkiResort.Hub.LIFT_RIDE_TIP", "Half the cable loop at 2.5 m/s, at 1× game speed."],
  liftCapacity: ["SkiResort.Hub.LIFT_CAPACITY", "Design capacity (riders an hour at 1× game speed)"],
  liftCapacityTip: ["SkiResort.Hub.LIFT_CAPACITY_TIP", "4-seat chairs at 2.5 m/s, spaced as built. The game does not simulate riders."],
  liftRuns: ["SkiResort.Hub.LIFT_RUNS", "Runs it serves"],
  liftCost: ["SkiResort.Hub.LIFT_COST", "Running cost"],
  liftCostReducedNoPower: ["SkiResort.Hub.LIFT_COST_REDUCED[NoPower]", "Billed at 25 %: no power"],
  liftCostReducedUnfinished: ["SkiResort.Hub.LIFT_COST_REDUCED[Unfinished]", "Billed at 25 % while unfinished"],
  liftCostReducedSectionMissing: ["SkiResort.Hub.LIFT_COST_REDUCED[SectionMissing]", "Billed at 25 % while a section is missing"],
  liftActions: ["SkiResort.Hub.LIFT_ACTIONS", "Click to fly there (to the gap if a section is missing). The pen renames it."],
  liftsTotalLength: ["SkiResort.Hub.LIFTS_TOTAL_LENGTH", "Total lift length"],
  liftsTotalCapacity: ["SkiResort.Hub.LIFTS_TOTAL_CAPACITY", "Design capacity, all lifts"],
  liftSections: ["SkiResort.Hub.LIFT_SECTIONS", "Sections"],
  liftTowers: ["SkiResort.Hub.LIFT_TOWERS", "Towers"],
  liftChairs: ["SkiResort.Hub.LIFT_CHAIRS", "Chairs"],
  liftStraight: ["SkiResort.Hub.LIFT_STRAIGHT", "Stations straight (180°)"],
  liftTowerRemoved: ["SkiResort.Hub.LIFT_TOWER_REMOVED", "A tower was removed by another tool. Redraw a section next to it to restore it."],
  liftNameTip: ["SkiResort.Hub.LIFT_NAME_TIP", "The name is stored on the base station (on the top station while the lift has no base)"],
  liftSignTip: ["SkiResort.Hub.LIFT_SIGN_TIP", "Lifts belong to the nearest Welcome Sign within 5 km of their base station; runs keep the sign they were drawn for"],
  alignOffset: ["SkiResort.LiftTooltip.ALIGN_OFFSET", "Offset from the straight line"],
  alignAngle: ["SkiResort.LiftTooltip.ALIGN_ANGLE", "Angle off straight"],

  // ---- guests view (6.8) ----
  sectionGuests: ["SkiResort.Hub.SECTION[Guests]", "Guests"],
  guestsMonthThis: ["SkiResort.Hub.GUESTS_MONTH[This]", "This month"],
  guestsMonthLast: ["SkiResort.Hub.GUESTS_MONTH[Last]", "Last month"],
  /** Round S1 F7: bar 2's label while last month was counted only since the load (hubRules.lastMonthMark). */
  guestsMonthLastPartial: ["SkiResort.Hub.GUESTS_MONTH[LastPartial]", "Last month (partial)"],
  /** Round S1 D27: the ski-day row (first in the table) and its tooltip body. */
  guestsRowSkiDays: ["SkiResort.Hub.GUESTS_ROW[SkiDays]", "Ski days"],
  guestsTipSkiDays: ["SkiResort.Hub.GUESTS_TIP[SkiDays]", "Residents and hotel guests sent here to ski, counted when they reach a lodge. Most also count as residents or tourists."],
  guestsRowTourists: ["SkiResort.Hub.GUESTS_ROW[Tourists]", "Tourists"],
  guestsRowGroups: ["SkiResort.Hub.GUESTS_ROW[Groups]", "Tourist groups"],
  guestsRowResidents: ["SkiResort.Hub.GUESTS_ROW[Residents]", "Residents"],
  guestsRowPaid: ["SkiResort.Hub.GUESTS_ROW[Paid]", "Paid a lift ticket"],
  guestsRowTickets: ["SkiResort.Hub.GUESTS_ROW[Tickets]", "Lift tickets"],
  guestsTipTourists: ["SkiResort.Hub.GUESTS_TIP[Tourists]", "People in tourist groups that arrived at this ski area's lodges or Welcome Sign."],
  guestsTipGroups: ["SkiResort.Hub.GUESTS_TIP[Groups]", "Tourists arrive in groups; each group counts once."],
  guestsTipResidents: ["SkiResort.Hub.GUESTS_TIP[Residents]", "Residents of your city who came to a lodge for leisure."],
  guestsTipTickets: ["SkiResort.Hub.GUESTS_TIP[Tickets]", "Paid by each guest's household into your city's balance."],
  guestsLastNa: ["SkiResort.Hub.GUESTS_LAST_NA", "Not counted: guests are counted only from the moment the city loads."],
  guestsLastPartial: ["SkiResort.Hub.GUESTS_LAST_PARTIAL", "Last month was not counted in full: the city was loaded, or time was skipped ahead, during it."],
  // Round S1 D27: the ski-day state line (hubRules.skiDayLine; Room variants are HUB_FMT) and its tooltips (skiDayTip).
  skiDaysOff: ["SkiResort.Hub.SKI_DAYS[Off]", "Ski days are off (Options)"],
  skiDaysSignOff: ["SkiResort.Hub.SKI_DAYS[SignOff]", "Closed: the Welcome Sign is not running"],
  skiDaysNoRun: ["SkiResort.Hub.SKI_DAYS[NoRun]", "Closed: no open run"],
  skiDaysNoLodge: ["SkiResort.Hub.SKI_DAYS[NoLodge]", "No lodge for ski days"],
  skiDaysNight: ["SkiResort.Hub.SKI_DAYS[Night]", "Lifts closed for the night"],
  skiDaysFull: ["SkiResort.Hub.SKI_DAYS[Full]", "Full right now"],
  skiDaysTipOff: ["SkiResort.Hub.SKI_DAYS_TIP[Off]", "Locals who ski and Tourists who ski are both 0 in Options."],
  skiDaysTipNoLodge: ["SkiResort.Hub.SKI_DAYS_TIP[NoLodge]", "Ski days need a lodge that is switched on, at a road, and within 800 m of the runs."],
  skiDaysTipRuns: ["SkiResort.Hub.SKI_DAYS_TIP[Runs]", "The runs set the limit: bigger and easier runs hold more skiers at once."],
  skiDaysTipLodges: ["SkiResort.Hub.SKI_DAYS_TIP[Lodges]", "The lodges set the limit: each lodge takes 360 skiers at once, more on a powder day."],
  estRowTourists: ["SkiResort.Hub.EST_ROW[Tourists]", "Target tourists from this ski area (estimated)"],
  estTouristsTip: ["SkiResort.Hub.EST_TOURISTS_TIP", "Worked out with the game's own tourist formula, not counted. Tourism mods can replace that formula, so real arrivals may differ."],
  estToNote: ["SkiResort.Hub.EST_TO_NOTE", "Detected: Tourism Overhaul. It sets tourist numbers."],
  estToTip: ["SkiResort.Hub.EST_TO_TIP", "Tourism Overhaul sets tourist numbers in this city, so no estimate is shown."],
  guestsNoteWhere: ["SkiResort.Hub.GUESTS_NOTE[Where]", "Counted when tourists arrive at this ski area's lodge or Welcome Sign, and when residents come to a lodge for leisure."],
  guestsNoteSinceLoad: ["SkiResort.Hub.GUESTS_NOTE[SinceLoad]", "Counting started when you loaded the city, partway through this month."],
  ticketsNoteOff: ["SkiResort.Hub.TICKETS_NOTE[Off]", "Lift tickets are off (Options: Lift ticket price is 0)."],
  ticketsNoteNoEfficiency: ["SkiResort.Hub.TICKETS_NOTE[NoEfficiency]", "No tickets now: the Welcome Sign is not running."],
  ticketsNoteNothingOpen: ["SkiResort.Hub.TICKETS_NOTE[NothingOpen]", "No tickets now: no run counts and no lift is running."],
  ticketsNoteNotKnownYet: ["SkiResort.Hub.TICKETS_NOTE[NotKnownYet]", "No tickets yet: the runs are still being checked after loading."],

  // ---- money view (6.9) ----
  sectionMoney: ["SkiResort.Hub.SECTION[Money]", "Money"],
  moneyCol: ["SkiResort.Hub.MONEY_COL", "A month"],
  moneyTicketsUnknown: ["SkiResort.Hub.MONEY_TICKETS[Unknown]", "Lift tickets: counted after a quarter month"],
  moneyTicketsOff: ["SkiResort.Hub.MONEY_TICKETS[Off]", "Lift tickets: off"],
  moneyTicketsTip: ["SkiResort.Hub.MONEY_TICKETS_TIP", "Last month's ticket money once a whole month has been counted. Before that, this month so far scaled to a whole month (≈). The count is the guests who paid, each at the price of the moment they arrived."],
  sectionCosts: ["SkiResort.Hub.SECTION[Costs]", "Running costs"],
  moneyRowBuildings: ["SkiResort.Hub.MONEY_ROW[Buildings]", "Buildings and wages (game formula)"],
  moneyNet: ["SkiResort.Hub.MONEY_NET", "Net a month"],
  moneyTipNatural: ["SkiResort.Hub.MONEY_TIP[Natural]", "Natural runs have no running cost."],
  moneyTipNet: ["SkiResort.Hub.MONEY_TIP[Net]", "Ticket income minus the running costs at today's rates. What tourists spend in your city's shops and hotels is not included."],
  moneyNotePanel: ["SkiResort.Hub.MONEY_NOTE[Panel]", "Resort money is not in the game's Economy panel; it goes straight to your balance. Buildings and wages are billed by the game under Parks & Recreation."],
  moneyNoteRwh: ["SkiResort.Hub.MONEY_NOTE[Rwh]", "Detected: Realistic Workplaces. The real bill can differ."],
  moneyTipRwh: ["SkiResort.Hub.MONEY_TIP[Rwh]", "Realistic Workplaces And Households changes staff and service costs, so the bill can differ."],
  moneyNoteEst: ["SkiResort.Hub.MONEY_NOTE[Est]", "≈ Tickets are projected from this month so far until a full month has been counted."],
  unlinkedMoney: ["SkiResort.Hub.UNLINKED_MONEY", "Runs that are not linked cost nothing and earn nothing; their lifts still cost money to run."],

  // ---- build strip (6.10) ----
  buildEmpty: ["SkiResort.Hub.BUILD_EMPTY", "No building tools were reported. See SkiResortMod.Mod.log."],
  buildActive: ["SkiResort.Hub.BUILD_ACTIVE", "In use: click again to stop"],
  locked: ["SkiResort.Hub.LOCKED", "Not available yet"],
  buildHintSign: ["SkiResort.Hub.BUILD_HINT_SIGN", "The sign is the ski area: runs and lifts within 5 km belong to it."],
  buildHintLodge: ["SkiResort.Hub.BUILD_HINT_LODGE", "Goes on this ski area's Welcome Sign. The camera flies there first."],
  buildHintLodgeSelected: ["SkiResort.Hub.BUILD_HINT_LODGE_SELECTED", "Goes on the selected Welcome Sign, or the nearest one."],
  buildHintBase: ["SkiResort.Hub.BUILD_HINT_BASE", "Place at the bottom of the lift."],
  buildHintTop: ["SkiResort.Hub.BUILD_HINT_TOP", "Place at the top, facing the base station. A guide shows when it is straight."],
  buildHintLift: ["SkiResort.Hub.BUILD_HINT_LIFT", "Draw from station to station. On steep ground, redraw one section at a time."],
  buildHintLiftSign: ["SkiResort.Hub.BUILD_HINT_LIFT_SIGN", "Lifts join the nearest Welcome Sign within 5 km of their base station."],
  buildShortLodge: ["SkiResort.Hub.BUILD_SHORT[Lodge]", "Goes on this ski area's Welcome Sign; the camera flies there."],
  buildShortTop: ["SkiResort.Hub.BUILD_SHORT[Top]", "Place at the top, facing the base station."],
  buildShortLift: ["SkiResort.Hub.BUILD_SHORT[Lift]", "Draw from station to station, one section at a time."],
  buildTipOnce: ["SkiResort.Hub.BUILD_TIP_ONCE", "One-time"],
  buildTipMonth: ["SkiResort.Hub.BUILD_TIP_MONTH", "Each month"],
  buildMonthNone: ["SkiResort.Hub.BUILD_MONTH_NONE", "Nothing"],
  buildMonthGame: ["SkiResort.Hub.BUILD_MONTH_GAME", "Upkeep and wages, billed by the game"],
  buildMonthStation: ["SkiResort.Hub.BUILD_MONTH_STATION", "Part of its lift's running cost"],
  buildTipReshape: ["SkiResort.Hub.BUILD_TIP_RESHAPE", "Reshaping a run later costs nothing more."],
  buildTipLink: ["SkiResort.Hub.BUILD_TIP_LINK", "Runs link to the nearest Welcome Sign within 5 km."],
  buildTipNatural: ["SkiResort.Hub.BUILD_TIP_NATURAL", "Nothing is drawn on the ground: find it with the piste map."],
  buildTipLodge: ["SkiResort.Hub.BUILD_TIP_LODGE", "A new lodge needs staff, power and water; until it has them, the sign's efficiency, and so the attraction, can fall."],
  buildTipTypeNatural: ["SkiResort.Hub.BUILD_TIP_TYPE[Natural]", "Score: greens count half, blues three quarters, blacks in full. It opens only when natural snow covers it."],
} as const;

/** Placeholder entries: ONLY as <LocText entry={HUB_FMT.x} args={...} /> (gate R5, R13). */
export const HUB_FMT = {
  resortPosition: ["SkiResort.Hub.RESORT_POSITION", "{N} / {COUNT}"],
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
  statusTipPowder: ["SkiResort.Hub.STATUS_TIP_POWDER", "Fresh snow at the top: powder {PCT}. Powder brings more skiers."],
  // problems (6.3)
  problemLodgesSomeOff: ["SkiResort.Hub.PROBLEM[LodgesSomeOff]", "Lodges switched off: {N}"],
  problemEfficiency: ["SkiResort.Hub.PROBLEM[Efficiency]", "Welcome Sign at {PCT}: {REASON}"],
  problemEfficiencyMore: ["SkiResort.Hub.PROBLEM[EfficiencyMore]", "Welcome Sign at {PCT}: {REASON} and {N} more"],
  problemNotRunning: ["SkiResort.Hub.PROBLEM[NotRunning]", "Welcome Sign not running: {REASON}"],
  problemNotRunningMore: ["SkiResort.Hub.PROBLEM[NotRunningMore]", "Welcome Sign not running: {REASON} and {N} more"],
  problemLiftNoPower: ["SkiResort.Hub.PROBLEM[LiftNoPower]", "{NAME} has no power: it counts 0"],
  problemLiftSectionMissing: ["SkiResort.Hub.PROBLEM[LiftSectionMissing]", "{NAME} is missing a section: it counts 0"],
  problemLiftUnfinished: ["SkiResort.Hub.PROBLEM[LiftUnfinished]", "{NAME} is unfinished"],
  problemNoLift: ["SkiResort.Hub.PROBLEM[NoLift]", "No lift yet: runs count {PCT} without one"],
  problemStationsPair: ["SkiResort.Hub.PROBLEM[StationsPair]", "{NAME}: draw the lift line"],
  problemStationsBaseOnly: ["SkiResort.Hub.PROBLEM[StationsBaseOnly]", "{NAME}: place its top station"],
  problemStationsTopOnly: ["SkiResort.Hub.PROBLEM[StationsTopOnly]", "{NAME}: place its base station"],
  problemLiftChairsStopped: ["SkiResort.Hub.PROBLEM[LiftChairsStopped]", "{NAME}: the chairs are not moving"],
  problemRunsUnserved: ["SkiResort.Hub.PROBLEM[RunsUnserved]", "Runs without a lift: {N} (they count {PCT})"],
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
  tileTipPartly: ["SkiResort.Hub.TILE_TIP_PARTLY", "Patchy: {N}"],
  tileTipUnfinished: ["SkiResort.Hub.TILE_TIP_UNFINISHED", "Not complete (a section missing, unfinished, or stations only): {N}"],
  tileTipLast: ["SkiResort.Hub.TILE_TIP_LAST", "Last month: {N}"],
  // overview (6.5)
  scoreOf: ["SkiResort.Hub.SCORE_OF", "{A} of 200"],
  scoreBarTip: ["SkiResort.Hub.SCORE_BAR_TIP", "{NOW} now. {FULL} at full efficiency. The ticks show what game buildings score: a tourist attraction about 50, a landmark about 120."],
  receiptTipLodges: ["SkiResort.Hub.RECEIPT_TIP_LODGES", "Lodges counted: {N}"],
  receiptTipTerrain: ["SkiResort.Hub.RECEIPT_TIP[Terrain]", "{K} × the square root of ((runs + lifts) × variety × vertical). {K} is the Attractiveness scale option. Adding a run never lowers it."],
  receiptParts: ["SkiResort.Hub.RECEIPT_PARTS", "Runs {RUNS} · lifts {LIFTS} · variety ×{VARIETY} · vertical ×{VERTICAL}"],
  receiptTipVertical: ["SkiResort.Hub.RECEIPT_TIP[Vertical]", "Vertical: up to double at 600 m, from the lowest bottom to the highest top of the runs that count now ({M})."],
  receiptOpen: ["SkiResort.Hub.RECEIPT_OPEN", "Runs open now: {PCT} of their full value"],
  receiptTipOpen: ["SkiResort.Hub.RECEIPT_TIP[Open]", "Closed runs add nothing and patchy runs add part, so snow moves the score. With every run fully open, runs would add {FULL}."],
  receiptTimes: ["SkiResort.Hub.RECEIPT_TIMES", "× {PCT}"],
  diffTipPending: ["SkiResort.Hub.DIFF_TIP_PENDING", "Being rated: {N}"],
  diffTipUnrateable: ["SkiResort.Hub.DIFF_TIP_UNRATEABLE", "Cannot be rated: {N}"],
  areaHa: ["SkiResort.Hub.AREA_HA", "{HA} ha"],
  snowDepth: ["SkiResort.Hub.SNOW_DEPTH", "Average depth on the measured runs: {CM} cm"],
  // runs view (6.6)
  runOpenOpen: ["SkiResort.Hub.RUN_OPEN[Open]", "Open: snow on {PCT} of it"],
  runOpenPartly: ["SkiResort.Hub.RUN_OPEN[Partly]", "Patchy: snow on {PCT} of it"],
  runOpenClosed: ["SkiResort.Hub.RUN_OPEN[Closed]", "Closed: snow on {PCT} of it"],
  runServed: ["SkiResort.Hub.RUN_SERVED", "Served by {NAME}"],
  runNotServed: ["SkiResort.Hub.RUN_NOT_SERVED", "No running lift reaches its top: it counts {PCT}"],
  runValueOf: ["SkiResort.Hub.RUN_VALUE_OF", "{NOW} of {FULL}"],
  runValueParts: ["SkiResort.Hub.RUN_VALUE_PARTS", "difficulty {B} × size {S} × type {T} × lift {L} × open {O}"],
  // lifts view (6.7)
  liftDetail: ["SkiResort.Hub.LIFT_DETAIL", "{LENGTH} · rise {RISE} · {MIN} min · {CAP}/h design · {N} runs"],
  liftDetailOne: ["SkiResort.Hub.LIFT_DETAIL_ONE", "{LENGTH} · rise {RISE} · {MIN} min · {CAP}/h design · 1 run"],
  liftDetailNone: ["SkiResort.Hub.LIFT_DETAIL_NONE", "{LENGTH} · rise {RISE} · {MIN} min · {CAP}/h design · no runs"],
  liftRideMin: ["SkiResort.Hub.LIFT_RIDE_MIN", "{MIN} min"],
  capacityValue: ["SkiResort.Hub.CAPACITY_VALUE", "{CAP} riders/h"],
  // guests view (6.8)
  guestsTipPaid: ["SkiResort.Hub.GUESTS_TIP[Paid]", "{GUESTS} guests this month: {PAID} paid, {UNPAID} could not pay, {FREE} came in free (tickets off or the ski area closed)."],
  ticketsNoteOpen: ["SkiResort.Hub.TICKETS_NOTE[Open]", "Each guest pays {PRICE} on arrival (Options: Lift ticket price)."],
  /** Round S1: both guest bars' tooltip, {MAX} = hubRules.guestBarScale. */
  guestsBarScale: ["SkiResort.Hub.GUESTS_BAR_SCALE", "Full bar: {MAX} guests"],
  /** Round S1 D27 with ruling R-Q3 (s1-spec 5.3): {N} = skiDaysCap, the capacity at once (D21). */
  skiDaysRoomRuns: ["SkiResort.Hub.SKI_DAYS[RoomRuns]", "Holds {N} skiers at once (runs limit)"],
  skiDaysRoomLodges: ["SkiResort.Hub.SKI_DAYS[RoomLodges]", "Holds {N} skiers at once (lodges limit)"],
  skiDaysRoom: ["SkiResort.Hub.SKI_DAYS[Room]", "Holds {N} skiers at once"],
  /** The Ski days row's tooltip: the three classes partition {SENT} ("close by" = the walk class, ch4 RF-7). */
  skiDaysTipSent: ["SkiResort.Hub.SKI_DAYS_TIP[Sent]", "Sent this month: {SENT} · close by {WALK} · with a car {CAR} · far, no car {FAR}"],
  skiDaysTipNow: ["SkiResort.Hub.SKI_DAYS_TIP[Now]", "On the hill or on the way now: {NOW} of {CAP}"],
  // money view (6.9)
  moneyTicketsLast: ["SkiResort.Hub.MONEY_TICKETS[Last]", "Lift tickets last month: {N} paid"],
  moneyTicketsEst: ["SkiResort.Hub.MONEY_TICKETS[Est]", "Lift tickets (estimated): {N} paid"],
  moneyTicketsPrice: ["SkiResort.Hub.MONEY_TICKETS_PRICE", "Ticket price now: {PRICE}"],
  moneyRowNatural: ["SkiResort.Hub.MONEY_ROW[Natural]", "Natural runs · {HA} ha"],
  moneyRowGroomed: ["SkiResort.Hub.MONEY_ROW[Groomed]", "Groomed runs · {HA} ha with snow"],
  moneyRowSnowmaking: ["SkiResort.Hub.MONEY_ROW[Snowmaking]", "Snowmaking runs · {HA} ha with snow"],
  moneyRowNight: ["SkiResort.Hub.MONEY_ROW[Night]", "Night runs · {HA} ha with snow"],
  moneyRowLifts: ["SkiResort.Hub.MONEY_ROW[Lifts]", "Lifts · {N} · {LEN} of line"],
  moneyTipRuns: ["SkiResort.Hub.MONEY_TIP[Runs]", "{RATE} per hectare a month where the run holds managed snow now. It moves with the temperature through the day."],
  moneyTipLifts: ["SkiResort.Hub.MONEY_TIP[Lifts]", "{BASE} a month for each complete lift, plus {PERKM} per km of line; a quarter while unfinished or without power. Now {FULL} at the full rate and {REDUCED} at a quarter."],
  moneyTipBuildings: ["SkiResort.Hub.MONEY_TIP[Buildings]", "The game's own formula for the lodges and the Welcome Sign at the current Parks & Recreation budget: upkeep {UPKEEP}, wages {WAGES} for {FILLED} of {JOBS} staff."],
  // build strip (6.10)
  buildLineRun: ["SkiResort.Hub.BUILD_LINE_RUN", "{NAME} · {FEE} + {PERHA} per ha"],
  buildLineCost: ["SkiResort.Hub.BUILD_LINE_COST", "{NAME} · {COST}"],
  buildLineLift: ["SkiResort.Hub.BUILD_LINE_LIFT", "{NAME} · about {PERKM} per km of line"],
  buildOnceRun: ["SkiResort.Hub.BUILD_ONCE_RUN", "{FEE} + {PERHA} per ha"],
  buildOnceLift: ["SkiResort.Hub.BUILD_ONCE_LIFT", "about {PERKM} per km of line"],
  buildMonthRun: ["SkiResort.Hub.BUILD_MONTH_RUN", "{RATE} per ha with snow"],
  buildMonthLift: ["SkiResort.Hub.BUILD_MONTH_LIFT", "{BASE} per complete lift + {PERKM} per km of line"],
  follow: ["SkiResort.Hub.FOLLOW", "Now showing {NAME}."],
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

/** SEASON_TIP, index = SEASON_HINT value (None and GatingOff: none; STATUS_TIP_GATING says it). */
export const SEASON_TIP_TEXT: ReadonlyArray<LocEntry | null> = [
  null,
  ["SkiResort.Hub.SEASON_TIP[SnowSeason]", "Cold enough for natural snow at the top."],
  ["SkiResort.Hub.SEASON_TIP[SnowGunsOnly]", "Too warm for natural snow at the top, but cold enough for snow guns."],
  ["SkiResort.Hub.SEASON_TIP[Thawing]", "Too warm for new snow at the top. The runs are open on the snow that is left."],
  ["SkiResort.Hub.SEASON_TIP[WaitingForSnow]", "Cold enough, but the runs have no snow yet."],
  ["SkiResort.Hub.SEASON_TIP[TooWarm]", "Too warm for snow at the top, even for snow guns. If this map never has a winter, turn off Options > Modular Ski Resort > Snow & season gating so runs count all year."],
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
};

/** Footer line 2 short hint of a run tile, by RunKind (6.10). */
export const BUILD_SHORT_RUN: Readonly<Record<RunKind, LocEntry>> = {
  0: ["SkiResort.Hub.BUILD_SHORT[Groomed]", "Snow laid whenever it freezes; scores more on easy runs."],
  1: ["SkiResort.Hub.BUILD_SHORT[Snowmaking]", "Snow guns open it earlier and keep it open longer."],
  2: ["SkiResort.Hub.BUILD_SHORT[Night]", "Floodlit snowmaking run: scores 25 % more."],
  3: ["SkiResort.Hub.BUILD_SHORT[Natural]", "Natural snow only, no running cost; easy runs score less."],
};

/** Footer idle lines (NEXT, NEXT2) by hubRules.nextVariant. NEXT2[Sign] is BUILD_HINT_SIGN (no own key). */
export const NEXT_LINE1: Readonly<Record<NextVariant, LocEntry>> = {
  sign: ["SkiResort.Hub.NEXT[Sign]", "Next: place a Ski Area Welcome Sign."],
  unlinked: ["SkiResort.Hub.NEXT[Unlinked]", "Runs and lifts link to the nearest Welcome Sign within 5 km."],
  lodge: ["SkiResort.Hub.NEXT[Lodge]", "Next: a lodge on this ski area's Welcome Sign."],
  lift: ["SkiResort.Hub.NEXT[Lift]", "Next: a base station, a top station, then the lift line."],
  run: ["SkiResort.Hub.NEXT[Run]", "Next: draw a run within 5 km of this Welcome Sign."],
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
  lift: ["Assets.NAME[SkiChairliftHV]", "Fixed-Grip Chairlift"],
  baseStation: ["Assets.NAME[SkiBaseStationBuilding]", "Chairlift Base Station"],
  topStation: ["Assets.NAME[SkiTopStationBuilding]", "Chairlift Top Station"],
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
