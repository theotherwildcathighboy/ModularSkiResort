/*
 * src/hub/hubText.ts - every player-facing string of the hub panel: locale key + English fallback.
 * Every entry is a literal `[key, english]` tuple (gate R4 compares each with mod/SkiResortMod/LocaleEN.cs). An entry whose English
 * holds "{" lives in HUB_FMT only (R5 / R13); numbers are always arguments (parts.tsx numArg). Texts with "TIP" in the key are at most
 * 25 words (two lift-money tips have own caps); texts that bake in catalog numbers are guarded by CatalogGuardTests. Glyphs: the safe
 * list of tools/check-bundle.js. US spelling only.
 * Engine notes: EngineNotes/hubText.ts.md
 */
import type { LocEntry } from "../l10n";
import { BAND, clampGladeLevel } from "../bindings";
import type { BuildItemId, RunKind } from "../bindings";
import type { NextVariant, StatusWord } from "./hubRules";

/* Plain entries (no placeholder). */
export const HUB_LOC = {
  sectionFailed: ["SkiResort.Hub.SECTION_FAILED", "This part of the panel failed. Details are in the mod log."],
  sectionTrees: ["SkiResort.Hub.SECTION[Trees]", "Trees"],

  resortPrev: ["SkiResort.Hub.RESORT_PREV", "Previous ski area"],
  resortNext: ["SkiResort.Hub.RESORT_NEXT", "Next ski area"],
  resortFly: ["SkiResort.Hub.RESORT_FLY", "Fly to the Welcome Sign"],
  /* the header selector's tooltip line: the one rule (the panel is one ski area, keyed to its sign) */
  resortTip: ["SkiResort.Hub.RESORT_TIP", "Click to pick the ski area the panel shows."],
  /* the selector's caption, the open list's note, and an item's counts when both are 1 (the other forms: HUB_FMT) */
  resortCaption: ["SkiResort.Hub.RESORT_CAPTION", "Ski area"],
  resortMenuNote: ["SkiResort.Hub.RESORT_MENU_NOTE", "Selecting a sign, a lodge or a station on the map shows its ski area here."],
  resortCountsOne: ["SkiResort.Hub.RESORT_COUNTS[One]", "1 run · 1 lodge"],
  unlinkedEntry: ["SkiResort.Hub.UNLINKED_ENTRY", "Not linked to a ski area"],
  unlinkedHint: ["SkiResort.Hub.UNLINKED_HINT", "More than 5 km from every Welcome Sign. Place a sign nearby to link them."],
  rename: ["SkiResort.Hub.RENAME", "Rename"],
  renameTip: ["SkiResort.Hub.RENAME_TIP", "Enter or a click elsewhere keeps the name. Esc cancels. An empty name restores the default."],

  startTitle: ["SkiResort.Hub.START_TITLE", "Start a ski area"],
  startStep1: ["SkiResort.Hub.START_STEP[1]", "Place a Welcome Sign"],
  startStep2: ["SkiResort.Hub.START_STEP[2]", "Connect the sign to a road"],
  startStep3: ["SkiResort.Hub.START_STEP[3]", "Build a lodge near the runs"],
  startStep4: ["SkiResort.Hub.START_STEP[4]", "Build a lift: two stations, then the line"],
  startStep5: ["SkiResort.Hub.START_STEP[5]", "Draw runs from the top of the lift"],
  placeSign: ["SkiResort.Hub.PLACE_SIGN", "Place Welcome Sign"],

  statusNoRuns: ["SkiResort.Hub.STATUS_NO_RUNS", "Closed · no runs yet"],
  statusUnlinked: ["SkiResort.Hub.STATUS_UNLINKED", "No Welcome Sign within 5 km"],
  statusTipRule: ["SkiResort.Hub.STATUS_TIP_RULE", "Open: enough snow on 60 % of the run. Closed: under 40 %. Patchy: in between."],
  statusTipEstimated: ["SkiResort.Hub.STATUS_TIP_ESTIMATED", "Some runs are estimated from the day's temperature until their snow can be measured."],
  statusTipGating: ["SkiResort.Hub.STATUS_TIP_GATING", "Snow & season gating is off in Options, so every run counts as open."],
  statusTipVisuals: ["SkiResort.Hub.STATUS_TIP_VISUALS", "Snow is hidden in your Gameplay settings. Runs still open on the simulated snow."],
  /* the hint slot at powder >= 0.5 (hubRules.statusHint) */
  powderDay: ["SkiResort.Hub.POWDER_DAY", "Powder day"],

  problemNone: ["SkiResort.Hub.PROBLEM[None]", "Nothing needs your attention."],
  problemUnlinked: ["SkiResort.Hub.PROBLEM[Unlinked]", "More than 5 km from every Welcome Sign"],
  problemNoLodge: ["SkiResort.Hub.PROBLEM[NoLodge]", "No lodge"],
  problemLodgesAllOff: ["SkiResort.Hub.PROBLEM[LodgesAllOff]", "All lodges are switched off"],
  problemNoRuns: ["SkiResort.Hub.PROBLEM[NoRuns]", "No runs yet"],
  problemSignNoRoad: ["SkiResort.Hub.PROBLEM[SignNoRoad]", "Welcome Sign is not at a road"],
  problemLodgeNoRoad: ["SkiResort.Hub.PROBLEM[LodgeNoRoad]", "Lodge or school is not at a road"],
  problemSkiDaysCannotReach: ["SkiResort.Hub.PROBLEM[SkiDaysCannotReach]", "Many ski-day guests with a car never arrive"],
  problemNoParkingNearLodge: ["SkiResort.Hub.PROBLEM[NoParkingNearLodge]", "No parking near the lodge"],
  problemLodgeNotSlopeside: ["SkiResort.Hub.PROBLEM[LodgeNotSlopeside]", "Lodge is far from the runs"],
  problemSchoolNoGreenRun: ["SkiResort.Hub.PROBLEM[SchoolNoGreenRun]", "Ski school has no green run"],
  problemNoLift: ["SkiResort.Hub.PROBLEM[NoLift]", "No lift yet"],
  problemTipNoLodge: ["SkiResort.Hub.PROBLEM_TIP[NoLodge]", "Guests arrive at a lodge. Build one from the Buildings tab."],
  problemTipLodgesAllOff: ["SkiResort.Hub.PROBLEM_TIP[LodgesAllOff]", "Switched-off lodges do not count. Switch one on in its info panel."],
  problemTipLodgesSomeOff: ["SkiResort.Hub.PROBLEM_TIP[LodgesSomeOff]", "Switched-off lodges do not count in the lodge base."],
  problemTipEfficiency: ["SkiResort.Hub.PROBLEM_TIP[Efficiency]", "As in the Welcome Sign's info panel. Every cause that costs 5 % or more is listed below."],
  // The lift stops, serves no run, and is still billed (no "full": the NoOpenRun quarter can still apply); power is the base
  // station's only.
  problemTipLiftNoPower: ["SkiResort.Hub.PROBLEM_TIP[LiftNoPower]", "Its base station has no electricity, so the lift stops and serves no run. Maintenance is still charged. Connect a low-voltage line."],
  problemTipLiftSectionMissing: ["SkiResort.Hub.PROBLEM_TIP[LiftSectionMissing]", "A section was removed or is being redrawn. Until the line is complete the lift counts 0 and costs a quarter."],
  problemTipLiftUnfinished: ["SkiResort.Hub.PROBLEM_TIP[LiftUnfinished]", "The line does not join a base station and a top station yet."],
  problemTipNoRuns: ["SkiResort.Hub.PROBLEM_TIP[NoRuns]", "Pick a run type in the Runs tab and draw its outline."],
  problemTipStations: ["SkiResort.Hub.PROBLEM_TIP[Stations]", "A station is placed, but no line joins it to its partner yet."],
  problemTipLiftChairsStopped: ["SkiResort.Hub.PROBLEM_TIP[LiftChairsStopped]", "The lift is complete and still counts, but its chairs stopped. The mod log says why."],
  problemTipRunsUnrateable: ["SkiResort.Hub.PROBLEM_TIP[RunsUnrateable]", "The outline is too large or too thin, or has no terrain under it. These runs count very little."],
  problemTipRunsRating: ["SkiResort.Hub.PROBLEM_TIP[RunsRating]", "Runs are rated one at a time. A new run counts 0 until it is rated; a reshaped run keeps its last rating."],
  problemTipRunsFlat: ["SkiResort.Hub.PROBLEM_TIP[RunsFlat]", "Redraw them on steeper ground."],
  problemTipFreeLodges: ["SkiResort.Hub.PROBLEM_TIP[FreeLodges]", "They are more than 5 km from every Welcome Sign, so they count nowhere. Place a sign nearby or move them."],
  problemTipSignNoRoad: ["SkiResort.Hub.PROBLEM_TIP[SignNoRoad]", "Move it so its front touches a road: staff and visitors come in from it."],
  problemTipLodgeNoRoad: ["SkiResort.Hub.PROBLEM_TIP[LodgeNoRoad]", "Move it so its entrance side touches a road."],
  problemTipSchoolsNotLinked: ["SkiResort.Hub.PROBLEM_TIP[SchoolsNotLinked]", "More than 5 km from every Welcome Sign, so they teach nobody. Place a sign nearby or move the school."],
  problemTipSkiDaysCannotReach: ["SkiResort.Hub.PROBLEM_TIP[SkiDaysCannotReach]", "Many recent ski-day guests with a car did not reach the lodge in time. Fewer are sent until more arrive."],
  problemTipNoParkingNearLodge: ["SkiResort.Hub.PROBLEM_TIP[NoParkingNearLodge]", "No car spaces within 300 m of the lodge. Guests who drive need a parking lot or a street with parking."],
  problemTipLodgeNotSlopeside: ["SkiResort.Hub.PROBLEM_TIP[LodgeNotSlopeside]", "Ski-day guests only go to a lodge within 800 m of this ski area's runs or lift stations."],
  problemTipMixedLiftTypes: ["SkiResort.Hub.PROBLEM_TIP[MixedLiftTypes]", "A line and its stations must be one lift type; until then the lift counts 0. Redraw the line as the stations' type."],
  problemTipSummitLodgeNotLiftServed: ["SkiResort.Hub.PROBLEM_TIP[SummitLodgeNotLiftServed]", "In summer a summit lodge counts more only within 300 m of a working lift's top station."],
  problemTipSchoolNoGreenRun: ["SkiResort.Hub.PROBLEM_TIP[SchoolNoGreenRun]", "The ski school adds to the score only while a green run counts."],
  problemMoreTip: ["SkiResort.Hub.PROBLEM_MORE_TIP", "Also needs attention:"],
  problemClickFly: ["SkiResort.Hub.PROBLEM_CLICK_FLY", "Click to fly there."],
  problemClickList: ["SkiResort.Hub.PROBLEM_CLICK_LIST", "Click to open the list."],
  problemMoreClick: ["SkiResort.Hub.PROBLEM_MORE_CLICK", "Click to list them."],
  pagePrev: ["SkiResort.Hub.PAGE_PREV", "Previous page"],
  pageNext: ["SkiResort.Hub.PAGE_NEXT", "Next page"],
  lodgeGroup: ["SkiResort.Hub.BUILD_GROUP[Lodges]", "Lodges"],
  liftTool: ["SkiResort.Hub.LIFT_TOOL", "Lift generator"],
  liftToolTip: ["SkiResort.Hub.LIFT_TOOL_TIP", "Plan a lift line between two stations: Ctrl+Shift+L"],
  liftToolTipOn: ["SkiResort.Hub.LIFT_TOOL_TIP_ON", "On: click a station, then the other station of the lift. Click again to stop."],
  liftToolUnavailable: ["SkiResort.Hub.LIFT_TOOL_UNAVAILABLE", "Available once a city is loaded."],
  patrolGroup: ["SkiResort.Hub.BUILD_GROUP[Patrol]", "Ski patrol"],

  tileAttraction: ["SkiResort.Hub.TILE[Attraction]", "Attraction"],
  tileRuns: ["SkiResort.Hub.TILE[Runs]", "Runs"],
  /* a sign's Runs tile ("2 / 3" counts the open runs); the Not linked tile keeps TILE[Runs] (a total) */
  tileRunsOpen: ["SkiResort.Hub.TILE[RunsOpen]", "Runs open"],
  tileLifts: ["SkiResort.Hub.TILE[Lifts]", "Lifts"],
  tileGuests: ["SkiResort.Hub.TILE[Guests]", "Guests"],
  tileMoney: ["SkiResort.Hub.TILE[Money]", "Finances"],
  tileTipAttraction: ["SkiResort.Hub.TILE_TIP[Attraction]", "The ski area's score on the attraction scale: a park is about 10, a landmark about 120. Lodges share it; their panels show 0."],
  tileTipHeld: ["SkiResort.Hub.TILE_TIP_HELD", "Checking runs and snow after loading. The game keeps the saved score meanwhile."],
  tileTipRuns: ["SkiResort.Hub.TILE_TIP[Runs]", "Runs fully open now, out of all runs in this ski area."],
  tileTipLifts: ["SkiResort.Hub.TILE_TIP[Lifts]", "Complete lifts out of every lift drawn, unfinished ones included. Without power a complete lift stops and adds nothing."],
  tileTipSinceLoad: ["SkiResort.Hub.TILE_TIP_SINCE_LOAD", "Counted since you loaded the city."],
  tileTipMoney: ["SkiResort.Hub.TILE_TIP[Money]", "Net of the month so far: all resort income booked, minus lifts, runs, buildings and wages. The Finances view's Net."],
  tileTipEst: ["SkiResort.Hub.TILE_TIP_EST", "≈ Some income or costs are not counted yet; they read as 0."],
  tileTipEstBuildings: ["SkiResort.Hub.TILE_TIP_EST_BUILDINGS", "≈ The game's bill for buildings and wages is not known yet."],
  tileTipOpen: ["SkiResort.Hub.TILE_TIP_OPEN", "Click to show it below."],

  sectionAttraction: ["SkiResort.Hub.SECTION[Attraction]", "Attraction"],
  scoreTickPark: ["SkiResort.Hub.SCORE_TICK[Park]", "park 10"],
  scoreTickAttraction: ["SkiResort.Hub.SCORE_TICK[Attraction]", "tourist attraction 50"],
  scoreTickLandmark: ["SkiResort.Hub.SCORE_TICK[Landmark]", "landmark 120"],
  receiptLodgeBase: ["SkiResort.Hub.RECEIPT[LodgeBase]", "Lodge base"],
  receiptLodgeBaseHalf: ["SkiResort.Hub.RECEIPT[LodgeBaseHalf]", "Lodge base, half until a run counts"],
  receiptLodgeBaseNone: ["SkiResort.Hub.RECEIPT[LodgeBaseNone]", "Lodge base, no lodge"],
  receiptTipLodgeBase: ["SkiResort.Hub.RECEIPT_TIP[LodgeBase]", "Best lodge's base plus the others' extras, up to 24 more. Half while no run counts, more for a summit lodge a lift reaches."],
  receiptTerrain: ["SkiResort.Hub.RECEIPT[Terrain]", "Runs, lifts and school"],
  receiptTipRuns: ["SkiResort.Hub.RECEIPT_TIP[Runs]", "Each run's value now, from its difficulty, size, type, lift and snow. The Runs view lists them."],
  receiptTipLifts: ["SkiResort.Hub.RECEIPT_TIP[Lifts]", "Each complete lift with power adds fixed-grip 6, detachable 15, gondola 25. While no run counts, chairlifts and gondolas add a quarter."],
  /* the school's line of the RECEIPT_PARTS tooltip (shown while ResortLive.schoolValue > 0) */
  receiptTipSchool: ["SkiResort.Hub.RECEIPT_TIP[School]", "A ski school adds its value while a green run counts."],
  receiptTipVariety: ["SkiResort.Hub.RECEIPT_TIP[Variety]", "× 1.15 while a green, a blue and a black or harder run all count, else × 1."],
  /*
   * each term of the "Runs, lifts and school" section is its own labeled row; the full equation is the section header's tooltip
   * (RECEIPT_PARTS[_SCHOOL] + RECEIPT_ROOT)
   */
  receiptRowRuns: ["SkiResort.Hub.RECEIPT_ROW[Runs]", "Runs"],
  receiptRowLifts: ["SkiResort.Hub.RECEIPT_ROW[Lifts]", "Lifts"],
  receiptRowSchool: ["SkiResort.Hub.RECEIPT_ROW[School]", "Ski school"],
  receiptRowVariety: ["SkiResort.Hub.RECEIPT_ROW[Variety]", "Variety"],
  receiptRowVertical: ["SkiResort.Hub.RECEIPT_ROW[Vertical]", "Vertical"],
  receiptRowScale: ["SkiResort.Hub.RECEIPT_ROW[Scale]", "Scale, after the square root"],
  receiptTipScale: ["SkiResort.Hub.RECEIPT_TIP[Scale]", "The section total is this scale × the square root of the terms above. Attractiveness scale in Options."],
  /*
   * a row whose 0 has a cause says it with the warning glyph instead of a tooltip sentence (hubRules.schoolState: no school at
   * all, or a school but no green run counts)
   */
  noSchool: ["SkiResort.Hub.NO_SCHOOL", "No ski school"],
  schoolNoGreen: ["SkiResort.Hub.SCHOOL_NO_GREEN", "No green run"],
  /* a school that is built but adds 0 says why (hubRules.schoolState "off" / "waiting") */
  schoolOff: ["SkiResort.Hub.SCHOOL_OFF", "Switched off"],
  schoolWaiting: ["SkiResort.Hub.SCHOOL_WAITING", "No run counts yet"],
  receiptOpenNone: ["SkiResort.Hub.RECEIPT_OPEN_NONE", "Runs open now: –"],
  receiptEfficiency: ["SkiResort.Hub.RECEIPT[Efficiency]", "Welcome Sign efficiency"],
  receiptTipEfficiency: ["SkiResort.Hub.RECEIPT_TIP[Efficiency]", "As in the Welcome Sign's info panel: staff, power, water, sewage, garbage, budget and more. The score uses the day's average, at most 100 %."],
  receiptResult: ["SkiResort.Hub.RECEIPT[Result]", "Attraction"],
  receiptResultNoLodge: ["SkiResort.Hub.RECEIPT[ResultNoLodge]", "Attraction, no lodge"],
  receiptTipResult: ["SkiResort.Hub.RECEIPT_TIP[Result]", "(Lodge base + runs, lifts and school) × efficiency, at most 200. The active lodges share it; their own panels show 0."],
  detailShare: ["SkiResort.Hub.DETAIL[Share]", "Share of the city's tourist draw"],
  shareTip: ["SkiResort.Hub.SHARE_TIP", "This ski area's share of the city's attraction, the game's way: each building's score squared. The tourists it draws visit the whole city."],
  sectionMountain: ["SkiResort.Hub.SECTION[Mountain]", "Mountain"],
  difficulty: ["SkiResort.Hub.DIFFICULTY", "Difficulty"],
  pending: ["SkiResort.Hub.PENDING", "Being rated"],
  unrateable: ["SkiResort.Hub.UNRATEABLE", "Cannot be rated: outline too large or too thin, or no terrain under it"],
  mountainVertical: ["SkiResort.Hub.MOUNTAIN[Vertical]", "Vertical"],
  mountainTipVertical: ["SkiResort.Hub.MOUNTAIN_TIP[Vertical]", "From the lowest run bottom to the highest run top. ≈ while runs are being rated."],
  area: ["SkiResort.Hub.AREA", "Skiable area"],
  areaTip: ["SkiResort.Hub.AREA_TIP", "The plan area of all runs in this ski area."],
  // Snow is always two numbers, depth in cm and run coverage in %; "≈" marks an estimate. tools/test-hub-rules.js T18 guards it.
  mountainSnowDepth: ["SkiResort.Hub.MOUNTAIN[SnowDepth]", "Snow depth"],
  mountainSnowCover: ["SkiResort.Hub.MOUNTAIN[SnowCover]", "Run coverage"],
  mountainTipDepth: ["SkiResort.Hub.MOUNTAIN_TIP[Depth]", "Average snow depth on the runs."],
  mountainTipSnow: ["SkiResort.Hub.MOUNTAIN_TIP[Snow]", "Share of the run area with enough snow to ski."],
  unlinkedNote: ["SkiResort.Hub.UNLINKED_NOTE", "These runs and lifts belong to no ski area, so they score nothing and draw no guests."],

  treeTool: ["SkiResort.Hub.TREE_TOOL", "Tree tool"],
  treeToolTip: ["SkiResort.Hub.TREE_TOOL_TIP", "Plans trees on and around runs. Turn it on, point at a run and click."],
  treeToolTipOn: ["SkiResort.Hub.TREE_TOOL_TIP_ON", "The tree tool is on. Click to turn it off."],
  treesFewer: ["SkiResort.Hub.TREES_FEWER", "Fewer trees"],
  treesMore: ["SkiResort.Hub.TREES_MORE", "More trees"],
  treesApplyTo: ["SkiResort.Hub.TREES_APPLY_TO", "Apply to"],
  treesUnavailable: ["SkiResort.Hub.TREES_UNAVAILABLE", "The tree tool is not available right now."],
  gladeLevel: ["SkiResort.GladeTooltip.LEVEL", "Tree density (1-8)"],
  gladeTargetInterior: ["SkiResort.GladeTooltip.TARGET[Interior]", "Run interior"],
  gladeTargetTreeLine: ["SkiResort.GladeTooltip.TARGET[TreeLine]", "Tree line"],
  pisteMap: ["SkiResort.Hub.PISTE_MAP", "Piste map"],
  pisteMapTip: ["SkiResort.Hub.PISTE_MAP_TIP", "Draws this ski area's runs on the ground in difficulty colors while the panel is open. Natural: dashed. Too flat: gray. Cannot be rated: orange."],
  colLift: ["SkiResort.Hub.COL_LIFT", "Lift"],
  colLiftTip: ["SkiResort.Hub.COL_LIFT_TIP", "↑: a running lift reaches the top of this run."],
  colLength: ["SkiResort.Hub.COL_LENGTH", "Length"],
  colDrop: ["SkiResort.Hub.COL_DROP", "Drop"],
  colArea: ["SkiResort.Hub.COL_AREA", "Area"],
  colLengthTip: ["SkiResort.Hub.COL_LENGTH_TIP", "Horizontal length, estimated from the outline."],
  colDropTip: ["SkiResort.Hub.COL_DROP_TIP", "Approximate height from the lowest to the highest point of the outline."],
  runsEmpty: ["SkiResort.Hub.RUNS_EMPTY", "No runs yet. Pick a run type in the Runs tab below and draw its outline."],
  runEstimated: ["SkiResort.Hub.RUN_ESTIMATED", "Estimated from the day's temperature."],
  /* the run tooltip's snow-gun line by RUN_GUN_WHY (the depth line is HUB_FMT.runDepth) */
  runGunsMaking: ["SkiResort.Hub.RUN_GUNS[Making]", "Snow guns: making snow now"],
  runGunsDepth: ["SkiResort.Hub.RUN_GUNS[DepthReached]", "Snow guns: off, the snow is deep enough"],
  runGunsWarm: ["SkiResort.Hub.RUN_GUNS[TooWarm]", "Snow guns: off, too warm"],
  runGunsSeason: ["SkiResort.Hub.RUN_GUNS[OutOfSeason]", "Snow guns: off, out of season (October–March)"],
  runGunsWaiting: ["SkiResort.Hub.RUN_GUNS[Waiting]", "Snow guns: waiting for the first snow reading"],
  runOpenGating: ["SkiResort.Hub.RUN_OPEN_GATING", "Counts as open: snow rules are off."],
  runOpenNone: ["SkiResort.Hub.RUN_OPEN_NONE", "Not linked: no snow state."],
  runServedAnon: ["SkiResort.Hub.RUN_SERVED_ANON", "A running lift reaches its top."],
  runTypeNote: ["SkiResort.Hub.RUN_TYPE_NOTE", "Natural green and blue runs count less; a groomed run counts in full."],
  runCatTrackNote: ["SkiResort.Hub.RUN_CAT_TRACK_NOTE", "A cat track always rates green and counts a quarter of a run its size, in the score and the capacity."],
  runValue: ["SkiResort.Hub.RUN_VALUE", "Score value"],
  runCost: ["SkiResort.Hub.RUN_COST", "Running cost"],
  runActions: ["SkiResort.Hub.RUN_ACTIONS", "Click to fly there. The pen renames it."],
  cruxPitch: ["SkiResort.Tooltip.CRUX_PITCH", "Hardest unavoidable pitch"],
  steepestPatch: ["SkiResort.Tooltip.STEEPEST_PATCH", "Steepest patch (avoidable)"],

  liftsEmpty: ["SkiResort.Hub.LIFTS_EMPTY", "No lifts yet. Place a base station and a top station facing each other, then draw the lift line between them."],
  liftLine2SectionMissing: ["SkiResort.Hub.LIFT_LINE2[SectionMissing]", "Redraw the gap with the lift line tool"],
  liftLine2Unfinished: ["SkiResort.Hub.LIFT_LINE2[Unfinished]", "Not connected to a station at both ends yet"],
  liftLine2StationsPair: ["SkiResort.Hub.LIFT_LINE2[StationsPair]", "Both stations placed: draw the lift line between them"],
  liftLine2StationsBaseOnly: ["SkiResort.Hub.LIFT_LINE2[StationsBaseOnly]", "Base station placed: place the top station facing it"],
  liftLine2StationsTopOnly: ["SkiResort.Hub.LIFT_LINE2[StationsTopOnly]", "Top station placed: place the base station facing it"],
  liftRide: ["SkiResort.Hub.LIFT_RIDE", "Ride time"],
  liftCapacity: ["SkiResort.Hub.LIFT_CAPACITY", "Capacity"],
  // LIFT_RIDE_TIP and LIFT_CAPACITY_TIP are in HUB_FMT ({SPEED}, {SEATS} per lift type); the totals row of the Lifts view sums
  // several types, so it takes this plain tip. The total sums only the lifts that carry.
  liftsTotalCapacityTip: ["SkiResort.Hub.LIFTS_TOTAL_CAPACITY_TIP", "Sum over complete lifts with power and an open run; a tram counts its station stops. Riders are not simulated."],
  liftRuns: ["SkiResort.Hub.LIFT_RUNS", "Runs it serves"],
  liftCost: ["SkiResort.Hub.LIFT_COST", "Running cost"],
  // Power does not lower the bill, so a reduced row whose status is No power is billed reduced only for NoOpenRun;
  // hubRules.liftCostReason picks this key for it, so the text names that reason.
  liftCostReducedNoPower: ["SkiResort.Hub.LIFT_COST_REDUCED[NoPower]", "Billed reduced: no open run in its ski area (a quarter; a tram half)"],
  liftCostReducedUnfinished: ["SkiResort.Hub.LIFT_COST_REDUCED[Unfinished]", "Billed at a quarter while unfinished"],
  liftCostReducedSectionMissing: ["SkiResort.Hub.LIFT_COST_REDUCED[SectionMissing]", "Billed at a quarter while a section is missing"],
  // a complete, powered lift in a ski area with no counted run
  liftCostReducedNoOpenRun: ["SkiResort.Hub.LIFT_COST_REDUCED[NoOpenRun]", "Billed reduced: no open run in its ski area (a quarter; a tram half)"],
  liftActions: ["SkiResort.Hub.LIFT_ACTIONS", "Click to fly there (to the gap if a section is missing). The pen renames it."],
  liftsTotalLength: ["SkiResort.Hub.LIFTS_TOTAL_LENGTH", "Total lift length"],
  liftsTotalCapacity: ["SkiResort.Hub.LIFTS_TOTAL_CAPACITY", "Capacity, all lifts"],
  liftSections: ["SkiResort.Hub.LIFT_SECTIONS", "Sections"],
  liftTowers: ["SkiResort.Hub.LIFT_TOWERS", "Towers"],
  liftChairs: ["SkiResort.Hub.LIFT_CHAIRS", "Chairs"],
  liftStraight: ["SkiResort.Hub.LIFT_STRAIGHT", "Stations straight (180°)"],
  liftTowerRemoved: ["SkiResort.Hub.LIFT_TOWER_REMOVED", "A tower was removed by another tool. Redraw a section next to it to restore it."],
  liftNameTip: ["SkiResort.Hub.LIFT_NAME_TIP", "The name is stored on the base station, or on the top station while there is no base."],
  liftSignTip: ["SkiResort.Hub.LIFT_SIGN_TIP", "A lift belongs to the ski area of its base station; a run keeps the ski area it was drawn for."],
  alignOffset: ["SkiResort.LiftTooltip.ALIGN_OFFSET", "Offset from the straight line"],
  alignAngle: ["SkiResort.LiftTooltip.ALIGN_ANGLE", "Angle off straight"],

  sectionGuests: ["SkiResort.Hub.SECTION[Guests]", "Guests"],
  /* the Ski days row and its tooltip body */
  guestsRowSkiDays: ["SkiResort.Hub.GUESTS_ROW[SkiDays]", "Reached a lodge"],
  guestsTipSkiDays: ["SkiResort.Hub.GUESTS_TIP[SkiDays]", "Skiers the resort sends out on a ski day, counted when they reach a lodge. They also count as residents or tourists."],
  /* the three group headers of the Guests table and the dim note under the first */
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
  // the ski-day state line (hubRules.skiDayLine; Room variants are HUB_FMT) and its tooltips (skiDayTip)
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
  estTouristsTip: ["SkiResort.Hub.EST_TOURISTS_TIP", "An estimate from the game's tourist formula. Tourism mods can replace the formula, so real arrivals can differ."],
  estToNote: ["SkiResort.Hub.EST_TO_NOTE", "Detected: Tourism Overhaul. It sets tourist numbers."],
  estToTip: ["SkiResort.Hub.EST_TO_TIP", "Tourism Overhaul sets tourist numbers in this city, so no estimate is shown."],
  // the trip-planner note (Known / KnownOff carry {NAME}: HUB_FMT)
  tripPlannerNoteUnknown: ["SkiResort.Hub.TRIP_PLANNER_NOTE[Unknown]", "Part of the game's trip planner is switched off (another mod, or a game error), so trip times can differ."],
  tripPlannerNoteUnknownOff: ["SkiResort.Hub.TRIP_PLANNER_NOTE[UnknownOff]", "Ski days off: the game's leisure systems are switched off (another mod, or a game error)."],
  tripPlannerTip: ["SkiResort.Hub.TRIP_PLANNER_TIP", "Read at each city load: which of the game's trip systems are on, and which known mods are loaded. The mod log lists them."],
  guestsNoteWhere: ["SkiResort.Hub.GUESTS_NOTE[Where]", "Counted when tourists arrive at this ski area's lodge or Welcome Sign, and when residents come to a lodge for leisure."],
  guestsNoteSinceLoad: ["SkiResort.Hub.GUESTS_NOTE[SinceLoad]", "Counting started when you loaded the city, partway through the month."],
  ticketsNoteOff: ["SkiResort.Hub.TICKETS_NOTE[Off]", "Lift tickets are off: the Lift ticket price in Options is 0."],
  ticketsNoteNoEfficiency: ["SkiResort.Hub.TICKETS_NOTE[NoEfficiency]", "No lift tickets now: the Welcome Sign is not running."],
  ticketsNoteNothingOpen: ["SkiResort.Hub.TICKETS_NOTE[NothingOpen]", "No lift tickets now: no run is open. Off-season, lodges drop to half staff (Options); set one Inactive: a tenth of upkeep, no wages."],
  ticketsNoteNotKnownYet: ["SkiResort.Hub.TICKETS_NOTE[NotKnownYet]", "No lift tickets yet: runs are being checked after a load or a new sign."],

  sectionMoney: ["SkiResort.Hub.SECTION[Money]", "Finances"],
  moneyTicketsOff: ["SkiResort.Hub.MONEY_TICKETS[Off]", "Lift tickets: off"],
  moneyRowTickets: ["SkiResort.Hub.MONEY_ROW[Tickets]", "Lift tickets"],
  moneyTicketsTip: ["SkiResort.Hub.MONEY_TICKETS_TIP", "Booked this month so far: each guest who paid × the price when they arrived."],
  sectionCosts: ["SkiResort.Hub.SECTION[Costs]", "Running costs"],
  moneyRowRuns: ["SkiResort.Hub.MONEY_ROW[Runs]", "Runs"],
  moneyTipRunsBooked: ["SkiResort.Hub.MONEY_TIP[RunsBooked]", "Run upkeep booked this month so far. Today's rate per month, by kind:"],
  moneyRowBuildings: ["SkiResort.Hub.MONEY_ROW[Buildings]", "Building upkeep"],
  moneyRowWages: ["SkiResort.Hub.MONEY_ROW[Wages]", "Staff wages"],
  moneyNet: ["SkiResort.Hub.MONEY_NET", "Net so far"],
  moneyTipNet: ["SkiResort.Hub.MONEY_TIP[Net]", "All income booked this month minus all costs: the rows above add up to it. Buildings and wages: today's rate × the month so far."],
  sectionAlso: ["SkiResort.Hub.SECTION[Also]", "Other figures"],
  moneyNotePanel: ["SkiResort.Hub.MONEY_NOTE[Panel]", "This month so far: income, lift and run costs as booked; buildings and wages at today's rate for the share of the month gone."],
  moneyNoteRwh: ["SkiResort.Hub.MONEY_NOTE[Rwh]", "Detected: Realistic Workplaces. The real bill can differ."],
  moneyTipRwh: ["SkiResort.Hub.MONEY_TIP[Rwh]", "Realistic Workplaces And Households changes staff costs; the bill can differ."],
  moneyNoteEst: ["SkiResort.Hub.MONEY_NOTE[Est]", "≈ Some income or costs are not counted yet; they read as 0 in the net."],
  // the Money view's price line, tooltip lines without a placeholder
  moneyPriceTitle: ["SkiResort.Hub.MONEY_PRICE_TIP[Title]", "Lift ticket price"],
  moneyPriceTipWhere: ["SkiResort.Hub.MONEY_PRICE_TIP[Where]", "Options > Modular Ski Resort > Main > Ski area balance. One price for every ski area."],
  moneyPriceTipWho: ["SkiResort.Hub.MONEY_PRICE_TIP[Who]", "Only ski days react to it; other visitors and the attraction score ignore it."],
  // the lodge-spend income row (drawn only while lodgeSpendMonth > 0; hubRules.lodgeSpendShown)
  moneyRowLodgeSpend: ["SkiResort.Hub.MONEY_ROW[LodgeSpend]", "Lodge spend"],
  moneyRowPassesMonth: ["SkiResort.Hub.MONEY_ROW[PassesMonth]", "Season passes sold"],
  moneyTipLodgeSpend: ["SkiResort.Hub.MONEY_TIP[LodgeSpend]", "Booked this month so far: paying ski guests' spend at the lodges. Set per guest in Options > Modular Ski Resort > Main > Ski area balance."],
  // rows drawn while passes are on or used (hubRules.passesShown)
  moneyTipPasses: ["SkiResort.Hub.MONEY_TIP[Passes]", "Sold July to June. Holders ski free all season and come more often. Share and price: Options > Modular Ski Resort > Main > Ski area balance."],
  moneyTipPassHolders: ["SkiResort.Hub.MONEY_TIP[PassHolders]", "Guests who came in on a season pass: no lift ticket money now."],
  guestsRowPassHolders: ["SkiResort.Hub.GUESTS_ROW[PassHolders]", "Came in on a season pass"],
  guestsTipPassHolders: ["SkiResort.Hub.GUESTS_TIP[PassHolders]", "Residents who came in on a season pass. They paid once for the season and pay nothing at the gate. Tourists never hold one."],
  // lessons, the pre-season sale, the city-income estimate (hidden under Tourism Overhaul: EST_TO_NOTE / EST_TO_TIP)
  moneyTipLessons: ["SkiResort.Hub.MONEY_TIP[Lessons]", "Booked this month so far, one lesson per guest. Share and price: Options > Modular Ski Resort > Main > Ski area balance."],
  moneyTipPassesPre: ["SkiResort.Hub.MONEY_TIP[PassesPre]", "The one sale before the season opens. Part of the season's sales; in the net in the month it sold."],
  moneyRowCityIncome: ["SkiResort.Hub.MONEY_ROW[CityIncome]", "City income the resort brings"],
  moneyTipCityIncome: ["SkiResort.Hub.MONEY_TIP[CityIncome]", "Estimate: this ski area's share of the city's tourist draw × the game's tourist income. City income, outside the net."],
  guestsRowLessons: ["SkiResort.Hub.GUESTS_ROW[Lessons]", "Took a lesson"],
  guestsTipLessons: ["SkiResort.Hub.GUESTS_TIP[Lessons]", "Guests who took a lesson, one each."],
  unlinkedMoney: ["SkiResort.Hub.UNLINKED_MONEY", "Runs and lifts that are not linked earn nothing. Their running costs are still charged."],

  buildEmpty: ["SkiResort.Hub.BUILD_EMPTY", "No building tools were reported. See SkiResortMod.Mod.log."],
  buildActive: ["SkiResort.Hub.BUILD_ACTIVE", "In use: click again to stop"],
  locked: ["SkiResort.Hub.LOCKED", "Not available yet"],
  buildHintSign: ["SkiResort.Hub.BUILD_HINT_SIGN", "Starts a ski area: the runs and lifts within 5 km belong to it."],
  buildHintLodge: ["SkiResort.Hub.BUILD_HINT_LODGE", "Guests start their ski day here: build it close to the runs."],
  buildHintBase: ["SkiResort.Hub.BUILD_HINT_BASE", "Place at the bottom of the lift."],
  buildHintTop: ["SkiResort.Hub.BUILD_HINT_TOP", "Place at the top, facing the base station. A guide shows when it is straight."],
  buildHintLift: ["SkiResort.Hub.BUILD_HINT_LIFT", "Draw from station to station. On steep ground, redraw one section at a time."],
  buildShortLodge: ["SkiResort.Hub.BUILD_SHORT[Lodge]", "Build it close to the runs."],
  buildShortTop: ["SkiResort.Hub.BUILD_SHORT[Top]", "Place at the top, facing the base station."],
  buildShortLift: ["SkiResort.Hub.BUILD_SHORT[Lift]", "Draw from station to station, one section at a time."],
  buildShortSchool: ["SkiResort.Hub.BUILD_SHORT[School]", "Teaches on green runs."],
  buildShortPatrol: ["SkiResort.Hub.BUILD_SHORT[Patrol]", "Covers the steep runs nearby."],
  buildTipSchool: ["SkiResort.Hub.BUILD_TIP_SCHOOL", "Adds +12 to Runs, lifts and school while a green run counts; locals come 20 % more often."],
  buildTipPatrol: ["SkiResort.Hub.BUILD_TIP_PATROL", "Black and double-black runs within 1 km score 15 % more; runs there without a lift count at least 70 %."],
  buildTipLuxury: ["SkiResort.Hub.BUILD_TIP_LUXURY", "Luxury lodge. Ski days stay full at a lift ticket price up to 40 higher."],
  pickerTipLiftType: ["SkiResort.Hub.PICKER_TIP[LiftType]", "Pick the kind of lift to build."],
  pickerTipLodgeTier: ["SkiResort.Hub.PICKER_TIP[LodgeTier]", "Pick the lodge to build."],
  buildTipOnce: ["SkiResort.Hub.BUILD_TIP_ONCE", "One-time"],
  buildTipMonth: ["SkiResort.Hub.BUILD_TIP_MONTH", "Per month"],
  buildMonthNone: ["SkiResort.Hub.BUILD_MONTH_NONE", "Nothing"],
  buildMonthStation: ["SkiResort.Hub.BUILD_MONTH_STATION", "The whole lift is priced by its length: both stations and the line. This station is one part."],
  buildTipReshape: ["SkiResort.Hub.BUILD_TIP_RESHAPE", "Reshaping a run later costs nothing more."],
  buildTipNatural: ["SkiResort.Hub.BUILD_TIP_NATURAL", "Nothing is drawn on the ground: find it with the piste map."],
  buildTipTypeNatural: ["SkiResort.Hub.BUILD_TIP_TYPE[Natural]", "Score: greens count half, blues three quarters, blacks in full. It opens only when natural snow covers it."],
  // a building tile's draw rows (vanilla's labels, DRAW_LABEL) end with Workers; then one terrain line from the prefab's
  // BuildingTerraformOverride (buildItems.ts BUILDING_DRAW.keepsSlope)
  buildTipWorkers: ["SkiResort.Hub.BUILD_TIP_WORKERS", "Workers"],
  terrainLevel: ["SkiResort.Hub.TERRAIN[Level]", "Levels the ground"],
  terrainSlope: ["SkiResort.Hub.TERRAIN[Slope]", "Sits on the slope"],

  tileHistory: ["SkiResort.Hub.TILE[History]", "History"],
  tileTipHistory: ["SkiResort.Hub.TILE_TIP[History]", "Guests and finances of every month this ski area has recorded."],
  historyScopeArea: ["SkiResort.Hub.HISTORY[ScopeArea]", "This ski area"],
  historyScopeCity: ["SkiResort.Hub.HISTORY[ScopeCity]", "Whole city"],
  historyTipScope: ["SkiResort.Hub.HISTORY_TIP[Scope]", "Whole city adds every ski area that exists now; a deleted one's history goes with it. No attraction line: each area has its own."],
  historyRange12: ["SkiResort.Hub.HISTORY[Range12]", "12 months"],
  historyRangeSeasons: ["SkiResort.Hub.HISTORY[RangeSeasons]", "This season vs last"],
  historyRangeBySeason: ["SkiResort.Hub.HISTORY[RangeBySeason]", "By season"],
  historyTipRange12: ["SkiResort.Hub.HISTORY_TIP[Range12]", "The last 12 months. The month in progress is the last bar."],
  historyTipRangeSeasons: ["SkiResort.Hub.HISTORY_TIP[RangeSeasons]", "This ski season, July to June, in color. Last season's same months in gray behind it."],
  historyTipRangeBySeason: ["SkiResort.Hub.HISTORY_TIP[RangeBySeason]", "One bar per ski season, July to June: the average month, so a season with missing months compares fairly."],
  historyTipRangeOff: ["SkiResort.Hub.HISTORY_TIP[RangeOff]", "Not enough months recorded yet."],
  historyGuests: ["SkiResort.Hub.HISTORY[Guests]", "Guests"],
  historyFinances: ["SkiResort.Hub.HISTORY[Finances]", "Finances"],
  historyBoth: ["SkiResort.Hub.HISTORY[Both]", "Both"],
  historyAttraction: ["SkiResort.Hub.HISTORY[Attraction]", "Attraction"],
  historyTipAttraction: ["SkiResort.Hub.HISTORY_TIP[Attraction]", "The attraction score and the snow when each month closed. A season shows its averages."],
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
  // the Quick runs tool (QuickRunsControls.tsx, BuildStrip.tsx QuickRunsChip)
  quickTool: ["SkiResort.Hub.QUICK_TOOL", "Quick runs (experimental)"],
  quickToolTip: ["SkiResort.Hub.QUICK_TOOL_TIP", "Outline a slope and get a set of runs to pick from. Experimental. Ctrl+Shift+Q also opens it."],
  quickToolTipOn: ["SkiResort.Hub.QUICK_TOOL_TIP_ON", "On: click the corners of a slope on the map. Click here to close the tool."],
  quickUnavailable: ["SkiResort.Hub.QUICK_UNAVAILABLE", "Quick runs is not available right now."],
  quickScan: ["SkiResort.Hub.QUICK_SCAN", "Scan"],
  quickScanTip: ["SkiResort.Hub.QUICK_SCAN_TIP", "Scan the outlined slope for runs. Enter does the same."],
  quickBuild: ["SkiResort.Hub.QUICK_BUILD", "Build"],
  quickBuildTip: ["SkiResort.Hub.QUICK_BUILD_TIP", "Build every run that is not grayed out. Refused when the city cannot pay. Large areas can take a few seconds to a minute."],
  quickUndo: ["SkiResort.Hub.QUICK_UNDO", "Undo"],
  quickUndoTip: ["SkiResort.Hub.QUICK_UNDO_TIP", "Remove the runs of the last build and refund what they cost."],
  quickShuffle: ["SkiResort.Hub.QUICK_SHUFFLE", "Shuffle"],
  quickShuffleTip: ["SkiResort.Hub.QUICK_SHUFFLE_TIP", "Try another layout of the same slope."],
  quickMixCaption: ["SkiResort.Hub.QUICK_MIX", "Mix"],
  quickMixTip: ["SkiResort.Hub.QUICK_MIX_TIP", "Which colors the runs should be. Changing it picks again from the same scan."],
  quickMix0: ["SkiResort.Hub.QUICK_MIX[Balanced]", "Balanced"],
  quickMix1: ["SkiResort.Hub.QUICK_MIX[Beginner]", "Beginner"],
  quickMix2: ["SkiResort.Hub.QUICK_MIX[Expert]", "Expert"],
  quickMix3: ["SkiResort.Hub.QUICK_MIX[Terrain]", "As the slope allows"],
  quickDensityCaption: ["SkiResort.Hub.QUICK_DENSITY", "Runs"],
  quickDensityTip: ["SkiResort.Hub.QUICK_DENSITY_TIP", "How many runs to fit on the slope."],
  quickDensity0: ["SkiResort.Hub.QUICK_DENSITY[Sparse]", "Few"],
  quickDensity1: ["SkiResort.Hub.QUICK_DENSITY[Normal]", "Normal"],
  quickDensity2: ["SkiResort.Hub.QUICK_DENSITY[Dense]", "Many"],
  quickKindsCaption: ["SkiResort.Hub.QUICK_KINDS", "Kinds"],
  quickKindsTip: ["SkiResort.Hub.QUICK_KINDS_TIP", "Run kinds the tool may use. Click one to allow it or leave it out."],
  quickKindGroomed: ["SkiResort.Hub.QUICK_KIND[Groomed]", "Groomed"],
  quickKindNatural: ["SkiResort.Hub.QUICK_KIND[Natural]", "Natural"],
  quickKindSnowmaking: ["SkiResort.Hub.QUICK_KIND[Snowmaking]", "Snowmaking"],
  quickKindNight: ["SkiResort.Hub.QUICK_KIND[Night]", "Night"],
  quickStart: ["SkiResort.Hub.QUICK_START", "Click the corners of a slope on the map, then Scan."],
  quickRegionOver: ["SkiResort.Hub.QUICK_REGION_OVER", "The region is larger than the 1 square km limit: make it smaller."],
  quickPreviewHint: ["SkiResort.Hub.QUICK_PREVIEW_HINT", "Click a run on the map to leave it out or take it back."],
  quickBuilding: ["SkiResort.Hub.QUICK_BUILDING", "Building the runs. Large areas can take a few seconds to a minute."],
  quickStop: ["SkiResort.Hub.QUICK_STOP", "Stop"],
  quickStopTip: ["SkiResort.Hub.QUICK_STOP_TIP", "Stop after the part in progress. What is built or planted so far stays, and Undo removes it. Right click does the same."],
  quickTreesCaption: ["SkiResort.Hub.QUICK_TREES", "Trees"],
  quickTrees: ["SkiResort.Hub.QUICK_TREES_TOGGLE", "Tree filler"],
  quickTreesTip: ["SkiResort.Hub.QUICK_TREES_TIP", "Fill the outlined slope with trees up to the tree line, around the runs. Plant when it looks right."],
  quickPlant: ["SkiResort.Hub.QUICK_PLANT", "Plant"],
  quickPlantTip: ["SkiResort.Hub.QUICK_PLANT_TIP", "Plant the trees, a few thousand at a time. Large areas can take a few seconds to a minute. Enter does the same."],
  quickTreesUndo: ["SkiResort.Hub.QUICK_TREES_UNDO", "Undo trees"],
  quickTreesUndoTip: ["SkiResort.Hub.QUICK_TREES_UNDO_TIP", "Remove the trees of the last planting."],
  quickTreeLine: ["SkiResort.Hub.QUICK_TREE_LINE", "Tree line"],
  quickTreeLineTip: ["SkiResort.Hub.QUICK_TREE_LINE_TIP", "Trees grow up to this height. It starts where the wild trees nearby stop."],
  quickTreeLineNone: ["SkiResort.Hub.QUICK_TREE_LINE_NONE", "Above the slope"],
  quickLower: ["SkiResort.Hub.QUICK_LOWER", "Lower"],
  quickHigher: ["SkiResort.Hub.QUICK_HIGHER", "Higher"],
  quickGroup0: ["SkiResort.Hub.QUICK_GROUP[Conifer]", "Conifer"],
  quickGroup1: ["SkiResort.Hub.QUICK_GROUP[Deciduous]", "Deciduous"],
  quickGroup2: ["SkiResort.Hub.QUICK_GROUP[Palm]", "Palm"],
  quickGroup3: ["SkiResort.Hub.QUICK_GROUP[All]", "All trees"],
  quickGroupTip: ["SkiResort.Hub.QUICK_GROUP_TIP", "Which trees to plant: a group, or one species below."],
  quickSpeciesCaption: ["SkiResort.Hub.QUICK_SPECIES", "Species"],
  quickSpeciesGroup: ["SkiResort.Hub.QUICK_SPECIES_GROUP", "The whole group"],
  quickSpeciesTip: ["SkiResort.Hub.QUICK_SPECIES_TIP", "Step through single species. Step back past the first one for the whole group."],
  quickTreeDensity: ["SkiResort.Hub.QUICK_TREE_DENSITY", "Density"],
  quickDensityTrees: ["SkiResort.Hub.QUICK_TREE_DENSITY_TIP", "How thick the trees grow."],
} as const;

/* Placeholder entries: only as <LocText entry={HUB_FMT.x} args={...} /> (gate R5, R13). */
export const HUB_FMT = {
  resortPosition: ["SkiResort.Hub.RESORT_POSITION", "{N} / {COUNT}"],
  // folded per-lift band rows (hubRules.foldBandRows), the Lifts tile's power line, the list pager
  problemGroupLiftUnfinished: ["SkiResort.Hub.PROBLEM_GROUP[LiftUnfinished]", "{N} lifts unfinished: {NAMES}"],
  problemGroupLiftNoPower: ["SkiResort.Hub.PROBLEM_GROUP[LiftNoPower]", "{N} lifts without power: {NAMES}"],
  problemGroupLiftSectionMissing: ["SkiResort.Hub.PROBLEM_GROUP[LiftSectionMissing]", "{N} lifts missing a section: {NAMES}"],
  tileTipPowered: ["SkiResort.Hub.TILE_TIP_POWERED", "Running with power: {N}"],
  pagePosition: ["SkiResort.Hub.PAGE_POSITION", "{N} / {COUNT}"],
  // a selector item's counts (hubRules.resortCountsVariant picks; "one" is HUB_LOC.resortCountsOne)
  resortCounts: ["SkiResort.Hub.RESORT_COUNTS[Many]", "{RUNS} runs · {LODGES} lodges"],
  resortCountsRunOne: ["SkiResort.Hub.RESORT_COUNTS[RunOne]", "1 run · {LODGES} lodges"],
  resortCountsLodgeOne: ["SkiResort.Hub.RESORT_COUNTS[LodgeOne]", "{RUNS} runs · 1 lodge"],
  contractMismatchCore: ["SkiResort.Hub.CONTRACT_MISMATCH_CORE", "This panel (version {UI}) and the mod's DLL (version {DLL}) come from different builds. Building still works; the resort statistics are hidden until the mod is reinstalled."],
  statusLine: ["SkiResort.Hub.STATUS_LINE", "{STATE} · {OPEN} of {TOTAL} runs"],
  statusLineOne: ["SkiResort.Hub.STATUS_LINE_ONE", "{STATE} · {OPEN} of 1 run open"],
  statusLineEst: ["SkiResort.Hub.STATUS_LINE_EST", "{STATE} · ≈ {OPEN} of {TOTAL} runs"],
  statusLineOneEst: ["SkiResort.Hub.STATUS_LINE_ONE_EST", "{STATE} · ≈ {OPEN} of 1 run open"],
  statusLinePartly: ["SkiResort.Hub.STATUS_LINE_PARTLY", "{STATE} · {PARTLY} of {TOTAL} runs patchy"],
  statusLinePartlyOne: ["SkiResort.Hub.STATUS_LINE_PARTLY_ONE", "{STATE} · {PARTLY} of 1 run patchy"],
  statusLinePartlyEst: ["SkiResort.Hub.STATUS_LINE_PARTLY_EST", "{STATE} · ≈ {PARTLY} of {TOTAL} runs patchy"],
  statusLinePartlyOneEst: ["SkiResort.Hub.STATUS_LINE_PARTLY_ONE_EST", "{STATE} · ≈ {PARTLY} of 1 run patchy"],
  statusTipCounts: ["SkiResort.Hub.STATUS_TIP_COUNTS", "Open {OPEN} · patchy {PARTLY} · closed {CLOSED}"],
  statusTipTemp: ["SkiResort.Hub.STATUS_TIP_TEMP", "Day average at the top of the highest run: {TEMP}"],
  /* the status tooltip line while powder > 0 ({PCT} = skiDaysPowderPct) */
  statusTipPowder: ["SkiResort.Hub.STATUS_TIP_POWDER", "Fresh snow at the top: powder {PCT}. More skiers come."],
  statusSnow: ["SkiResort.Hub.STATUS_SNOW", "{DEPTH} cm · {PCT}"],
  statusSnowIn: ["SkiResort.Hub.STATUS_SNOW_IN", "{DEPTH} in · {PCT}"], // US customary units
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
  problemLiftChairsStopped: ["SkiResort.Hub.PROBLEM[LiftChairsStopped]", "{NAME}: chairs stopped"],
  problemMixedLiftTypes: ["SkiResort.Hub.PROBLEM[MixedLiftTypes]", "{NAME} mixes lift types"],
  problemSummitLodgeNotLiftServed: ["SkiResort.Hub.PROBLEM[SummitLodgeNotLiftServed]", "Summit lodges no lift reaches: {N}"],
  problemRunsUnserved: ["SkiResort.Hub.PROBLEM[RunsUnserved]", "Runs unconnected to a lift: {N}"],
  problemRunsUnrateable: ["SkiResort.Hub.PROBLEM[RunsUnrateable]", "Runs that cannot be rated: {N}"],
  problemRunsRating: ["SkiResort.Hub.PROBLEM[RunsRating]", "Runs being rated: {N}"],
  problemRunsFlat: ["SkiResort.Hub.PROBLEM[RunsFlat]", "Runs too flat to score: {N}"],
  problemFreeLodges: ["SkiResort.Hub.PROBLEM[FreeLodges]", "Lodges not linked to a ski area: {N}"],
  problemSchoolsNotLinked: ["SkiResort.Hub.PROBLEM[SchoolsNotLinked]", "Ski schools not linked to a ski area: {N}"],
  problemMore: ["SkiResort.Hub.PROBLEM_MORE", "+{N}"],
  problemTipNoLift: ["SkiResort.Hub.PROBLEM_TIP[NoLift]", "A run counts {PCT} until a running lift reaches its top: within 150 m of the line, at most 15 m above the top station."],
  problemTipRunsUnserved: ["SkiResort.Hub.PROBLEM_TIP[RunsUnserved]", "Each counts {PCT} until a running lift reaches its top: within 150 m of the line, at most 15 m above the top station."],
  problemGainEfficiency: ["SkiResort.Hub.PROBLEM_GAIN[Efficiency]", "At full efficiency the attraction would be {FULL}."],
  problemGainLodge: ["SkiResort.Hub.PROBLEM_GAIN[Lodge]", "With a lodge, the attraction would be {A}."],
  problemGainLodgesOn: ["SkiResort.Hub.PROBLEM_GAIN[LodgesOn]", "With them switched on, the attraction would be {A}."],
  staffLine: ["SkiResort.Hub.STAFF_LINE", "Staff: {FILLED} of {JOBS}"],
  tileTipFull: ["SkiResort.Hub.TILE_TIP_FULL", "At full efficiency: {N}"],
  tileTipStaffing: ["SkiResort.Hub.TILE_TIP_STAFFING", "× efficiency {PCT}"],
  tileTipPartly: ["SkiResort.Hub.TILE_TIP_PARTLY", "Patchy: {N}"],
  tileTipUnfinished: ["SkiResort.Hub.TILE_TIP_UNFINISHED", "Not complete yet: {N}"],
  tileTipLast: ["SkiResort.Hub.TILE_TIP_LAST", "Month of {MONTH}: {N}"],
  // {MONTH} = the month's name (monthName(city.month))
  tileTipGuests: ["SkiResort.Hub.TILE_TIP[Guests]", "Month of {MONTH}: tourists who arrived at the lodges or the Welcome Sign, and residents who visited a lodge."],
  scoreBarTip: ["SkiResort.Hub.SCORE_BAR_TIP", "{NOW} now, {FULL} at full efficiency, out of 200."],
  receiptTipLodges: ["SkiResort.Hub.RECEIPT_TIP_LODGES", "Lodges counted: {N}"],
  receiptTipTerrain: ["SkiResort.Hub.RECEIPT_TIP[Terrain]", "{K} is the Attractiveness scale in Options. Adding a run never lowers the total."],
  receiptParts: ["SkiResort.Hub.RECEIPT_PARTS", "(runs {RUNS} + lifts {LIFTS}) × variety {VARIETY} × vertical {VERTICAL}"],
  /* RECEIPT_PARTS with the school term, while ResortLive.schoolValue > 0 (hubRules.receiptPartsVariant) */
  receiptPartsSchool: ["SkiResort.Hub.RECEIPT_PARTS_SCHOOL", "(runs {RUNS} + lifts {LIFTS} + school {SCHOOL}) × variety {VARIETY} × vertical {VERTICAL}"],
  /*
   * the second line under Runs and lifts. The row's value is {K} x the square root of the parts line (HubRules.Terrain = K x
   * sqrt(S), S = (runs + lifts + school) x variety x vertical, see SkiResortScore); {TERRAIN} = ResortLive.terrain, nothing is
   * multiplied in the UI.
   */
  receiptRoot: ["SkiResort.Hub.RECEIPT_ROOT", "{K} × the square root of the line above = {TERRAIN}"],
  /* the lodge-base tooltip's best lodge ({NAME} = its LODGE_TIER word) and the others' extras ({E}) */
  receiptTipLodgeBest: ["SkiResort.Hub.RECEIPT_TIP_LODGE_BEST", "Best lodge: {NAME}; the others add {E}."],
  receiptTipVertical: ["SkiResort.Hub.RECEIPT_TIP[Vertical]", "From × 1 at 0 m to × 2 at 600 m or more. Now {M}, lowest bottom to highest top of the runs that count."],
  receiptOpen: ["SkiResort.Hub.RECEIPT_OPEN", "Runs open now: {PCT} of their full value"],
  receiptTipOpen: ["SkiResort.Hub.RECEIPT_TIP[Open]", "Closed runs add nothing and patchy runs add part, so snow moves the score. With every run fully open, runs would add {FULL}."],
  receiptTimes: ["SkiResort.Hub.RECEIPT_TIMES", "× {PCT}"],
  diffTipPending: ["SkiResort.Hub.DIFF_TIP_PENDING", "Being rated: {N}"],
  diffTipUnrateable: ["SkiResort.Hub.DIFF_TIP_UNRATEABLE", "Cannot be rated: {N}"],
  areaHa: ["SkiResort.Hub.AREA_HA", "{HA} ha"],
  snowDepth: ["SkiResort.Hub.SNOW_DEPTH", "{DEPTH} cm"],
  // the depth under vanilla's US customary units (parts.tsx useInches / depthValue: whole inches)
  snowDepthIn: ["SkiResort.Hub.SNOW_DEPTH_IN", "{DEPTH} in"],
  runOpenOpen: ["SkiResort.Hub.RUN_OPEN[Open]", "Open: snow on {PCT} of it"],
  runOpenPartly: ["SkiResort.Hub.RUN_OPEN[Partly]", "Patchy: snow on {PCT} of it"],
  runOpenClosed: ["SkiResort.Hub.RUN_OPEN[Closed]", "Closed: snow on {PCT} of it"],
  /* {DEPTH} = RunLive.snowDepthCm / 100 (unit length: m or ft by the player's unit setting). No baked open depth: the threshold
     is C#'s and would need its own placeholder from RunList.tsx. */
  runDepth: ["SkiResort.Hub.RUN_DEPTH", "Snow depth {DEPTH}"],
  runServed: ["SkiResort.Hub.RUN_SERVED", "Served by {NAME}"],
  runNotServed: ["SkiResort.Hub.RUN_NOT_SERVED", "No running lift reaches its top: it counts {PCT}"],
  runValueOf: ["SkiResort.Hub.RUN_VALUE_OF", "{NOW} of {FULL}"],
  runValueParts: ["SkiResort.Hub.RUN_VALUE_PARTS", "difficulty {B} × size {S} × type {T} × lift {L} × open {O}"],
  liftDetail: ["SkiResort.Hub.LIFT_DETAIL", "{LENGTH} · rise {RISE} · {MIN} min ride · {CAP} skiers/h · {N} runs"],
  liftDetailOne: ["SkiResort.Hub.LIFT_DETAIL_ONE", "{LENGTH} · rise {RISE} · {MIN} min ride · {CAP} skiers/h · 1 run"],
  liftDetailNone: ["SkiResort.Hub.LIFT_DETAIL_NONE", "{LENGTH} · rise {RISE} · {MIN} min ride · {CAP} skiers/h · no runs"],
  liftRideMin: ["SkiResort.Hub.LIFT_RIDE_MIN", "{MIN} min"],
  // LiftRow.seats / speedMps per lift type; both texts hold for the tram too
  liftRideTip: ["SkiResort.Hub.LIFT_RIDE_TIP", "Base to top at {SPEED} m/s line speed, at 1× game speed."],
  // an unpowered lift counts none
  liftCapacityTip: ["SkiResort.Hub.LIFT_CAPACITY_TIP", "{SEATS} seats per carrier at {SPEED} m/s; a tram counts its station stops. None without power or an open run. Riders are not simulated."],
  capacityValue: ["SkiResort.Hub.CAPACITY_VALUE", "{CAP} skiers/h"],
  // "Month of {MONTH}" is the one way the hub names a month; {MONTH} = monthName(city.month), last month's = city.month - 1.
  guestsMonthThis: ["SkiResort.Hub.GUESTS_MONTH[This]", "Month of {MONTH}"],
  guestsMonthLast: ["SkiResort.Hub.GUESTS_MONTH[Last]", "Month of {MONTH}"],
  /* bar 2's label while last month was counted only since the load (hubRules.lastMonthMark) */
  guestsMonthLastPartial: ["SkiResort.Hub.GUESTS_MONTH[LastPartial]", "Month of {MONTH}, partial"],
  guestsTipPaid: ["SkiResort.Hub.GUESTS_TIP[Paid]", "Month of {MONTH}: {GUESTS} guests, {PAID} paid, {HOLDERS} on a pass, {UNPAID} could not pay, {FREE} free while tickets were off or closed."],
  ticketsNoteOpen: ["SkiResort.Hub.TICKETS_NOTE[Open]", "Each guest without a season pass pays {PRICE} on arrival. Set the price in Options."],
  /* both guest bars' tooltip, {MAX} = hubRules.guestBarScale */
  guestsBarScale: ["SkiResort.Hub.GUESTS_BAR_SCALE", "Full bar: {MAX} guests"],
  /* {N} = skiDaysCap, the capacity at once */
  skiDaysRoomRuns: ["SkiResort.Hub.SKI_DAYS[RoomRuns]", "Holds {N} skiers at once, set by the runs"],
  skiDaysRoomLodges: ["SkiResort.Hub.SKI_DAYS[RoomLodges]", "Holds {N} skiers at once, set by the lodges"],
  skiDaysRoom: ["SkiResort.Hub.SKI_DAYS[Room]", "Holds {N} skiers at once"],
  /* The Ski days row's tooltip: the three classes partition {SENT} ("close by" = the walk class). */
  skiDaysTipSent: ["SkiResort.Hub.SKI_DAYS_TIP[Sent]", "Month of {MONTH}: {SENT} sent, {ARRIVED} reached a lodge"],
  /* the split of the sent skiers ("close by" = the walk class, sent - car - no car) */
  skiDaysTipSplit: ["SkiResort.Hub.SKI_DAYS_TIP[Split]", "Close by {WALK} · by car {CAR} · far, no car {FAR}"],
  skiDaysTipNow: ["SkiResort.Hub.SKI_DAYS_TIP[Now]", "On the hill or on the way now: {NOW} of {CAP}"],
  /* {NAME} = CityStats.tripPlanner, the known mod's name (hubRules.tripPlannerNote "known" / "knownOff") */
  tripPlannerNote: ["SkiResort.Hub.TRIP_PLANNER_NOTE[Known]", "Detected: {NAME}. It replaces the game's trip planner, so trip times can differ."],
  tripPlannerNoteOff: ["SkiResort.Hub.TRIP_PLANNER_NOTE[KnownOff]", "Ski days off: {NAME} replaced the game's leisure systems."],
  moneyTicketsLast: ["SkiResort.Hub.MONEY_TICKETS[Last]", "Lift tickets, Month of {MONTH}: {N} paid"],
  moneyColMonth: ["SkiResort.Hub.MONEY_COL_MONTH", "Month of {MONTH} so far"],
  moneyColMonthPartial: ["SkiResort.Hub.MONEY_COL_MONTH_PARTIAL", "Month of {MONTH} so far (partial)"],
  moneyTipPassesMonth: ["SkiResort.Hub.MONEY_TIP[PassesMonth]", "Sold this month so far, in the net now. Of it, the pre-season sale: {PRE}."],
  moneyTipLiftsRate: ["SkiResort.Hub.MONEY_TIP[LiftsRate]", "Booked this month so far. At today's rate: {RATE} per month."],
  moneyTipSoFar: ["SkiResort.Hub.MONEY_TIP[SoFar]", "{RATE} per month at today's budget; {SHARE} of the month so far."],
  moneyRowPasses: ["SkiResort.Hub.MONEY_ROW[Passes]", "Season passes sold this season · {N}"],
  moneyRowPassHolders: ["SkiResort.Hub.MONEY_ROW[PassHolders]", "Pass holders skiing, Month of {MONTH}"],
  /* {N} = the pre-season sale's passes (Integer) / the lessons taken this month (Integer) */
  moneyRowPassesPre: ["SkiResort.Hub.MONEY_ROW[PassesPre]", "Season passes sold before the season · {N}"],
  moneyRowLessons: ["SkiResort.Hub.MONEY_ROW[Lessons]", "Lessons · {N}"],
  moneyTicketsPrice: ["SkiResort.Hub.MONEY_TICKETS_PRICE", "Lift ticket price now: {PRICE}"],
  /* {PRICE} = hubRules.hubPrice (Money), {PCT} = hubRules.priceFactorPct (Percentage) */
  moneyPriceLine: ["SkiResort.Hub.MONEY_PRICE_LINE", "Lift ticket {PRICE} · ski days {PCT}"],
  /* {FULL} = PRICE_NO_EFFECT_MAX, {P1..P3} = PRICE_EXAMPLES (Money), {F1..F3} = their priceFactorPct (Percentage) */
  moneyPriceTipCurve: ["SkiResort.Hub.MONEY_PRICE_TIP[Curve]", "Ski days: as many at any price up to {FULL}, then fewer: {F1} as many at {P1}, {F2} at {P2}, {F3} at {P3}."],
  /* {FULL} = 40 + priceTolerance (not "Luxury lodges": the Chalet has 20) */
  moneyPriceTipTolerance: ["SkiResort.Hub.MONEY_PRICE_TIP[Tolerance]", "This ski area's lodges keep ski days full up to {FULL}."],
  moneyRowNatural: ["SkiResort.Hub.MONEY_ROW[Natural]", "Natural runs · {HA} ha"],
  moneyRowGroomed: ["SkiResort.Hub.MONEY_ROW[Groomed]", "Groomed runs · {HA} ha with snow"],
  moneyRowSnowmaking: ["SkiResort.Hub.MONEY_ROW[Snowmaking]", "Snowmaking runs · {HA} ha with snow"],
  moneyRowNight: ["SkiResort.Hub.MONEY_ROW[Night]", "Night runs · {HA} ha with snow"],
  moneyRowLifts: ["SkiResort.Hub.MONEY_ROW[Lifts]", "Lifts · {N} · {LEN} of line"],
  // an unpowered lift still pays in full; the two remaining reductions are unfinished and no open run
  // (SkiMoneyRules.LiftUpkeepBillingOf)
  moneyTipLifts: ["SkiResort.Hub.MONEY_TIP[Lifts]", "{BASE} per month per lift + {PERKM} per km of line, in full even without power; a quarter while unfinished or with no open run (a tram half). Now {FULL} full, {REDUCED} reduced."],
  /*
   * MONEY_TIP[Lifts] when two or more lift types are offered (each has its own rates; one MONEY_TIP_LIFT_RATE line per type
   * follows)
   */
  moneyTipLiftsTypes: ["SkiResort.Hub.MONEY_TIP[LiftsTypes]", "Each lift: its kind's base per month + a rate per km of line, in full even without power; a quarter while unfinished or with no open run (a tram half). Now {FULL} full, {REDUCED} reduced."],
  moneyTipLiftRate: ["SkiResort.Hub.MONEY_TIP_LIFT_RATE", "{TYPE}: {BASE} per month + {PERKM} per km"],
  moneyTipBuildings: ["SkiResort.Hub.MONEY_TIP[Buildings]", "Upkeep {UPKEEP} · wages {WAGES} for {FILLED} of {JOBS} staff, at the Parks & Recreation budget."],
  buildLineRun: ["SkiResort.Hub.BUILD_LINE_RUN", "{NAME} · {FEE} + {PERHA} per ha"],
  buildLineCost: ["SkiResort.Hub.BUILD_LINE_COST", "{NAME} · {COST}"],
  buildLineLift: ["SkiResort.Hub.BUILD_LINE_LIFT", "{NAME} · about {PERKM} per km of line"],
  buildOnceRun: ["SkiResort.Hub.BUILD_ONCE_RUN", "{FEE} + {PERHA} per ha"],
  buildOnceLift: ["SkiResort.Hub.BUILD_ONCE_LIFT", "about {PERKM} per km of line"],
  buildMonthRun: ["SkiResort.Hub.BUILD_MONTH_RUN", "{RATE} per ha with snow"],
  buildMonthLift: ["SkiResort.Hub.BUILD_MONTH_LIFT", "{BASE} per complete lift + {PERKM} per km of line"],
  follow: ["SkiResort.Hub.FOLLOW", "Now showing {NAME}."],
  // {MONTH} = monthName, {YEAR} = the game year as plain digits, {FROM}/{TO} = a season's years
  historyMonthYear: ["SkiResort.Hub.HISTORY_MONTH", "Month of {MONTH} {YEAR}"],
  historySeason: ["SkiResort.Hub.HISTORY_SEASON", "Ski season {FROM}/{TO}"],
  historyHeadGuests: ["SkiResort.Hub.HISTORY_HEAD[Guests]", "Guests, {MONTH}"],
  historyHeadNet: ["SkiResort.Hub.HISTORY_HEAD[Net]", "Net, {MONTH}"],
  historyTipBefore: ["SkiResort.Hub.HISTORY_TIP[Before]", "Month of {MONTH} {YEAR}: {N}"],
  historySeasonMonths: ["SkiResort.Hub.HISTORY_SEASON_MONTHS", "{N} of 12 months · the average month"],
  historyVs: ["SkiResort.Hub.HISTORY_VS", "vs {MONTH} {YEAR}"],
  // the Quick runs tool (QuickRunsControls.tsx); the MIX / CLUSTER / POD templates are also the tool tooltip's
  quickRegion: ["SkiResort.Hub.QUICK_REGION_LINE", "{N} corners · {AREA} of {MAX} · vertical {V}"],
  quickScanning: ["SkiResort.Hub.QUICK_SCANNING_LINE", "Scanning the slope: {P}"],
  quickTotals: ["SkiResort.Hub.QUICK_TOTALS_LINE", "{N} of {M} runs · {FEE} · {SKIERS} skiers a day"],
  quickBatch: ["SkiResort.Hub.QUICK_BATCH_LINE", "Last build: {N} runs. Undo removes them and refunds the fee."],
  quickTreeCounts: ["SkiResort.Hub.QUICK_TREE_COUNTS", "{P} to plant · {R} to remove"],
  quickMixLine: ["SkiResort.Tooltip.QUICK_MIX", "Green {G} of {TG} · Blue {B} of {TB} · Black {K} of {TK} · Double black {D} of {TD}"],
  quickCluster: ["SkiResort.Tooltip.QUICK_CLUSTER", "One lift here serves {N} runs"],
  quickPodBeginner: ["SkiResort.Tooltip.QUICK_POD[Beginner]", "Beginner lift here: {N} runs"],
  quickPodMid: ["SkiResort.Tooltip.QUICK_POD[MidStation]", "Mid-station here: {N} runs"],
  quickBuildProgress: ["SkiResort.Tooltip.QUICK_BUILDING_PROGRESS", "Building runs {N} / {M}"],
  quickPlantProgress: ["SkiResort.Tooltip.QUICK_PLANTING_PROGRESS", "Planting trees {N} / {M}"],
} as const;

/* STATUS_WORD by hubRules.statusWord. */
export const STATUS_WORD: Readonly<Record<StatusWord, LocEntry>> = {
  open: ["SkiResort.Hub.STATUS_WORD[Open]", "Open"],
  partly: ["SkiResort.Hub.STATUS_WORD[Partly]", "Partly open"],
  closed: ["SkiResort.Hub.STATUS_WORD[Closed]", "Closed"],
};

/*
 * The Runs view's state word of one run, index = OPEN_STATE value (0 Closed, 1 Partly, 2 Open; -1 None has no word). "Patchy"
 * = partly covered: never "Partly", which STATUS_WORD uses for the area.
 */
export const RUN_STATE_TEXT: readonly LocEntry[] = [
  ["SkiResort.Hub.RUN_STATE[Closed]", "Closed"],
  ["SkiResort.Hub.RUN_STATE[Patchy]", "Patchy"],
  ["SkiResort.Hub.RUN_STATE[Open]", "Open"],
];

/* SEASON_HINT words, index = SEASON_HINT value (0 None = no text). */
export const SEASON_HINT_TEXT: ReadonlyArray<LocEntry | null> = [
  null,
  ["SkiResort.Hub.SEASON_HINT[SnowSeason]", "Snow season"],
  ["SkiResort.Hub.SEASON_HINT[SnowGunsOnly]", "Snow guns only"],
  ["SkiResort.Hub.SEASON_HINT[Thawing]", "Thawing"],
  ["SkiResort.Hub.SEASON_HINT[WaitingForSnow]", "Waiting for snow"],
  ["SkiResort.Hub.SEASON_HINT[TooWarm]", "Too warm for snow"],
  ["SkiResort.Hub.SEASON_HINT[GatingOff]", "Snow rules off"],
];

/*
 * SEASON_TIP, index = SEASON_HINT value (None and GatingOff: none; STATUS_TIP_GATING says it). Each tip says what C#
 * HubRules.Season read: the 24-hour mean at the top for natural snow, and the live gun gates ("snow guns running now").
 */
export const SEASON_TIP_TEXT: ReadonlyArray<LocEntry | null> = [
  null,
  ["SkiResort.Hub.SEASON_TIP[SnowSeason]", "At or below freezing: natural snow can fall."],
  ["SkiResort.Hub.SEASON_TIP[SnowGunsOnly]", "Above freezing, but the snow guns are running."],
  ["SkiResort.Hub.SEASON_TIP[Thawing]", "Above freezing and no snow gun running. Runs stay open on the snow that is left."],
  ["SkiResort.Hub.SEASON_TIP[WaitingForSnow]", "Cold enough for snow or snow guns, but no run has enough snow yet."],
  ["SkiResort.Hub.SEASON_TIP[TooWarm]", "Too warm for snow guns. Runs reopen when it cools, or turn off Snow & season gating in Options."],
  null,
];

/* LIFT_STATUS words, index = LIFT_STATUS value. */
export const LIFT_STATUS_TEXT: readonly LocEntry[] = [
  ["SkiResort.Hub.LIFT_STATUS[Running]", "Running"],
  ["SkiResort.Hub.LIFT_STATUS[NoPower]", "No power"],
  ["SkiResort.Hub.LIFT_STATUS[ChairsStopped]", "Chairs stopped"],
  ["SkiResort.Hub.LIFT_STATUS[SectionMissing]", "Section missing"],
  ["SkiResort.Hub.LIFT_STATUS[Unfinished]", "Unfinished"],
  ["SkiResort.Hub.LIFT_STATUS[Closed]", "Closed"], // held still while its ski area has no open run
];

export function liftStatusEntry(status: number): LocEntry {
  return status >= 0 && status < LIFT_STATUS_TEXT.length ? LIFT_STATUS_TEXT[status] : LIFT_STATUS_TEXT[4];
}

/* Run-type names (sentence case), by RunKind. */
export const RUN_TYPE: Readonly<Record<RunKind, LocEntry>> = {
  0: ["SkiResort.Hub.RUN_TYPE[Groomed]", "Groomed run"],
  1: ["SkiResort.Hub.RUN_TYPE[Snowmaking]", "Snowmaking run"],
  2: ["SkiResort.Hub.RUN_TYPE[Night]", "Night run"],
  3: ["SkiResort.Hub.RUN_TYPE[Natural]", "Natural run"],
  4: ["SkiResort.Hub.RUN_TYPE[CatTrack]", "Cat track"],
};

/* Footer line 2 short hint of a run tile, by RunKind. */
export const BUILD_SHORT_RUN: Readonly<Record<RunKind, LocEntry>> = {
  0: ["SkiResort.Hub.BUILD_SHORT[Groomed]", "Snow laid whenever it freezes; scores more on easy runs."],
  1: ["SkiResort.Hub.BUILD_SHORT[Snowmaking]", "Snow guns open it earlier and keep it open longer."],
  2: ["SkiResort.Hub.BUILD_SHORT[Night]", "Floodlit snowmaking run: scores 25 % more."],
  3: ["SkiResort.Hub.BUILD_SHORT[Natural]", "Natural snow only, no running cost; easy runs score less."],
  4: ["SkiResort.Hub.BUILD_SHORT[CatTrack]", "Graded as you lay it out; always green, counts a quarter."],
};

/* Footer idle lines (NEXT, NEXT2) by hubRules.nextVariant. NEXT2[Sign] is BUILD_HINT_SIGN (no own key). */
export const NEXT_LINE1: Readonly<Record<NextVariant, LocEntry>> = {
  sign: ["SkiResort.Hub.NEXT[Sign]", "Next: place a Ski Area Welcome Sign from the Buildings tab."],
  unlinked: ["SkiResort.Hub.NEXT[Unlinked]", "Runs and lifts link to the nearest Welcome Sign within 5 km."],
  lodge: ["SkiResort.Hub.NEXT[Lodge]", "Next: build a lodge from the Buildings tab."],
  lift: ["SkiResort.Hub.NEXT[Lift]", "Next: in the Lifts tab, a base station, a top station, then the line."],
  run: ["SkiResort.Hub.NEXT[Run]", "Next: draw a run from the Runs tab."],
  done: ["SkiResort.Hub.NEXT[Done]", "Point at a tile to see its cost and what it does."],
};
export const NEXT_LINE2: Readonly<Record<NextVariant, LocEntry>> = {
  sign: HUB_LOC.buildHintSign,
  unlinked: ["SkiResort.Hub.NEXT2[Unlinked]", "Place a sign near them to link them."],
  lodge: ["SkiResort.Hub.NEXT2[Lodge]", "Guests arrive at a lodge. Without one the attraction is 0."],
  lift: ["SkiResort.Hub.NEXT2[Lift]", "Runs that a lift serves count in full."],
  run: ["SkiResort.Hub.NEXT2[Run]", "Natural runs cost nothing to run; the others cost per hectare."],
  done: ["SkiResort.Hub.NEXT2[Done]", "Click a tile to build; click it again to stop."],
};

/*
 * `Assets.NAME[<prefab>]` of the non-run build items (runs use RUN_TYPE; the run assets are Title Case, so a run asset name
 * here would fail R4).
 */
export const ASSET_NAME: Readonly<Partial<Record<BuildItemId, LocEntry>>> = {
  sign: ["Assets.NAME[SkiResortGate]", "Ski Area Welcome Sign"],
  lodge: ["Assets.NAME[SkiAFrameDayLodge]", "A-Frame Day Lodge"],
  lodgeHut: ["Assets.NAME[SkiLodgeHut]", "Mountain Hut"],
  lodgeMid: ["Assets.NAME[SkiLodgeMid]", "Chalet Lodge"],
  lodgeSummit: ["Assets.NAME[SkiLodgeSummit]", "Swanky Summit Lodge"],
  lodgeGrand: ["Assets.NAME[SkiLodgeGrand]", "Swanky Grand Lodge"],
  school: ["Assets.NAME[SkiSchool]", "Ski School"],
  patrolHut: ["Assets.NAME[SkiPatrolHut]", "Ski Patrol Hut"],
  patrolHQ: ["Assets.NAME[SkiPatrolHQ]", "Ski Patrol HQ"],
  lift: ["Assets.NAME[SkiChairliftHV]", "Fixed-Grip Triple Chairlift"],
  // Every station says its lift type and its end ("<lift type>: <end>"); the tram's are terminals, never "Tram Station". A
  // tile's footer line 1 is "{NAME} · {COST}", one nowrap ellipsis line (hub.module.scss .buildStatusLine): the longest name is
  // 39 characters.
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

/*
 * The lift-type picker's chips and the Lifts view's type line, by LiftRow.type (LIFT_TYPE_IDS; an unknown id draws no word).
 * Sentence case like RUN_TYPE.
 */
export const LIFT_TYPE: Readonly<Record<string, LocEntry>> = {
  fixedGrip: ["SkiResort.Hub.LIFT_TYPE[fixedGrip]", "Fixed-grip"],
  detachable: ["SkiResort.Hub.LIFT_TYPE[detachable]", "Detachable"],
  gondola: ["SkiResort.Hub.LIFT_TYPE[gondola]", "Gondola"],
  tram: ["SkiResort.Hub.LIFT_TYPE[tram]", "Tram"],
};

/* The lodge-tier picker's chips (and the receipt's best lodge), by the tier's build item id. */
export const LODGE_TIER: Readonly<Partial<Record<BuildItemId, LocEntry>>> = {
  lodge: ["SkiResort.Hub.LODGE_TIER[lodge]", "A-frame"],
  lodgeHut: ["SkiResort.Hub.LODGE_TIER[lodgeHut]", "Hut"],
  lodgeMid: ["SkiResort.Hub.LODGE_TIER[lodgeMid]", "Chalet"],
  lodgeSummit: ["SkiResort.Hub.LODGE_TIER[lodgeSummit]", "Summit"],
  lodgeGrand: ["SkiResort.Hub.LODGE_TIER[lodgeGrand]", "Grand"],
};

/* The Build strip's three tabs (hubRules.BuildTab), their tooltips, and the tiles' visible labels. */
export const BUILD_TAB: Readonly<Record<string, LocEntry>> = {
  lifts: ["SkiResort.Hub.BUILD_TAB[Lifts]", "Lifts"],
  runs: ["SkiResort.Hub.BUILD_TAB[Runs]", "Runs"],
  buildings: ["SkiResort.Hub.BUILD_TAB[Buildings]", "Buildings"],
};
export const BUILD_TAB_TIP: Readonly<Record<string, LocEntry>> = {
  lifts: ["SkiResort.Hub.BUILD_TAB_TIP[Lifts]", "Lifts: build the two stations, then the line. Pick the lift type below."],
  runs: ["SkiResort.Hub.BUILD_TAB_TIP[Runs]", "Runs: draw a run from the top of a lift, or plan trees with the tree tool."],
  buildings: ["SkiResort.Hub.BUILD_TAB_TIP[Buildings]", "Buildings: the Welcome Sign, lodges, the ski school and ski patrol."],
};
/*
 * A tile's visible label by its role: base / top / line (every lift type), sign, lodge (the chosen tier), school, patrolHut,
 * patrolHQ, and the four run kinds (short: the Runs tab already says "run"; the tooltip and footer keep RUN_TYPE).
 */
export const BUILD_LABEL: Readonly<Record<string, LocEntry>> = {
  base: ["SkiResort.Hub.BUILD_LABEL[Base]", "Base station"],
  top: ["SkiResort.Hub.BUILD_LABEL[Top]", "Top station"],
  line: ["SkiResort.Hub.BUILD_LABEL[Line]", "Lift line"],
  sign: ["SkiResort.Hub.BUILD_LABEL[Sign]", "Sign"],
  lodge: ["SkiResort.Hub.BUILD_LABEL[Lodge]", "Lodge"],
  school: ["SkiResort.Hub.BUILD_LABEL[School]", "School"],
  patrolHut: ["SkiResort.Hub.BUILD_LABEL[PatrolHut]", "Hut"],
  patrolHQ: ["SkiResort.Hub.BUILD_LABEL[PatrolHQ]", "HQ"],
  run3: ["SkiResort.Hub.BUILD_LABEL[Natural]", "Natural"],
  run0: ["SkiResort.Hub.BUILD_LABEL[Groomed]", "Groomed"],
  run1: ["SkiResort.Hub.BUILD_LABEL[Snowmaking]", "Snowmaking"],
  run2: ["SkiResort.Hub.BUILD_LABEL[Night]", "Night"],
};

/* Band names (the drawing tooltip's keys), index = band 0..4. */
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

/* Glade level names, index = level 1..8 (0 unused). */
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

/* Keys C# sends as data (build[].reason, build[].hint, glade.reason). */
export const SENT_KEYS: readonly LocEntry[] = [
  ["SkiResort.Hub.REASON[Locked]", "Not unlocked yet"],
  ["SkiResort.Hub.REASON[NoSign]", "Place a Ski Area Welcome Sign first"],
  ["SkiResort.Hub.REASON[Missing]", "Asset not found: is the Modular Ski Resort asset pack installed?"],
  // reason byte 4, on the shown ski area's sign
  ["SkiResort.Hub.REASON[AlreadyBuilt]", "Already built at this ski area"],
  ["SkiResort.Hub.HINT[LiftNoPair]", "Place the other station first: a lift needs a base and a top station."],
  ["SkiResort.Hub.GLADE_REASON[Disabled]", "The tree tool hit an error and is off until the next load. Details are in the mod log."],
  ["SkiResort.Hub.GLADE_REASON[NotInGame]", "The tree tool works in a loaded city only."],
  ["SkiResort.Hub.GLADE_REASON[NoRuns]", "The ski run assets were not found: is the asset pack installed?"],
  ["SkiResort.Hub.GLADE_REASON[NoSpecies]", "No tree species to plant were found."],
  // the Quick runs tool: quickRuns.reason / status / warnings / notice
  ["SkiResort.Hub.QUICK_REASON[Off]", "Turned off in Options: Modular Ski Resort, Quick runs (experimental)."],
  ["SkiResort.Hub.QUICK_REASON[NotArmed]", "Available once a city is loaded."],
  ["SkiResort.Hub.QUICK_REASON[Disabled]", "Quick runs hit an error and is off until the next load. Details are in the mod log."],
  ["SkiResort.Tooltip.QUICK_STATUS[BadInput]", "The region could not be read. Draw it again."],
  ["SkiResort.Tooltip.QUICK_STATUS[TooLarge]", "The region is larger than 1 square km. Make it smaller."],
  ["SkiResort.Tooltip.QUICK_STATUS[TooSmall]", "The region is too small or too narrow for a run."],
  ["SkiResort.Tooltip.QUICK_STATUS[Blocked]", "Most of the region is taken by other areas or by land the city does not own."],
  ["SkiResort.Tooltip.QUICK_STATUS[NoVertical]", "The region has too little vertical for ski runs."],
  ["SkiResort.Tooltip.QUICK_STATUS[AllCliff]", "The region is mostly cliff: no run reaches the bottom."],
  ["SkiResort.Tooltip.QUICK_STATUS[TooFlat]", "The region is too flat for ski runs."],
  ["SkiResort.Tooltip.QUICK_STATUS[NoRuns]", "No run fits this region. Try another shape or another mix."],
  ["SkiResort.Tooltip.QUICK_WARN[FewRuns]", "Fewer than 2 runs fit here."],
  ["SkiResort.Tooltip.QUICK_WARN[NoGreen]", "No green runs: the slope is too steep for them."],
  ["SkiResort.Tooltip.QUICK_WARN[NoBlue]", "No blue runs: the slope gives none."],
  ["SkiResort.Tooltip.QUICK_WARN[NoBlack]", "No black runs: the slope is too gentle for them."],
  ["SkiResort.Tooltip.QUICK_WARN[NoEasyWayHome]", "A lift top here has no green way down."],
  ["SkiResort.Tooltip.QUICK_WARN[OverWater]", "A run crosses water."],
  ["SkiResort.Tooltip.QUICK_WARN[NoFlatRunout]", "No gentle runout at the bottom of the slope."],
  ["SkiResort.Tooltip.QUICK_WARN[MixShort]", "Fewer runs of some colors than the mix asks for."],
  ["SkiResort.Tooltip.QUICK_WARN[CrossesCatTrack]", "A run crosses a cat track."],
  ["SkiResort.Tooltip.QUICK_WARN[PartlyBlocked]", "Part of the region is blocked by other areas or by land the city does not own."],
  ["SkiResort.Tooltip.QUICK_WARN[MoneyShort]", "The city cannot pay the total fee: building is refused."],
  ["SkiResort.Tooltip.QUICK_WARN[Renumber]", "Runs with default names will be renumbered."],
  ["SkiResort.Tooltip.QUICK_WARN[OtherResort]", "These runs belong to more than one ski area."],
  ["SkiResort.Tooltip.QUICK_WARN[NoSign]", "No Welcome Sign within 5 km: the runs will not count until one is placed nearby."],
  ["SkiResort.Tooltip.QUICK_WARN[ChecksOff]", "The game's own checks are off (Anarchy): only this tool's checks protect the build."],
  ["SkiResort.Tooltip.QUICK_NEED_CORNERS", "Click at least three corners first."],
  ["SkiResort.Tooltip.QUICK_NEED_SCAN", "Scan the region first."],
  ["SkiResort.Tooltip.QUICK_RESCANNED", "The map changed since the scan: scanned again. Check the preview and build again."],
  ["SkiResort.Tooltip.QUICK_NOTHING_ENABLED", "Every run is left out: click a run to take it back."],
  ["SkiResort.Tooltip.QUICK_DROPPED", "The game refused some runs: they are red and left out. Building the rest."],
  ["SkiResort.Tooltip.QUICK_REFUSED", "Nothing was built: the game refused the red runs. Build again to try the rest."],
  ["SkiResort.Tooltip.QUICK_BUILT", "Runs built."],
  ["SkiResort.Tooltip.QUICK_STOPPED", "Build stopped. The runs built so far stay; Undo removes them."],
  ["SkiResort.Tooltip.QUICK_PARTIAL", "Only part of the runs were built: the game refused the rest. Undo removes what was built."],
  ["SkiResort.Tooltip.QUICK_UNDONE", "The build was undone and its fee refunded."],
  ["SkiResort.Tooltip.QUICK_NOTHING_TO_UNDO", "Nothing to undo."],
  ["SkiResort.Tooltip.QUICK_RUNS_MISSING", "The ski run assets did not load: check that the mod is installed completely."],
  ["SkiResort.Tooltip.QUICK_TREES_OFF", "Turn the tree filler on first."],
  ["SkiResort.Tooltip.QUICK_TREES_COMPLETE", "Nothing to plant: the slope already has this many trees."],
  ["SkiResort.Tooltip.QUICK_TREES_UNDONE", "The planted trees were removed."],
  ["SkiResort.Tooltip.QUICK_TREES_BLOCKED", "The game refuses some of these trees: planting stopped. Trees planted before stay."],
  ["SkiResort.Tooltip.QUICK_TREES_STOPPED", "Planting stopped. The trees planted so far stay; Undo trees removes them."],
  ["SkiResort.Tooltip.QUICK_TREES_PLANTED", "Trees planted."],
  ["SkiResort.Tooltip.QUICK_NO_SPECIES", "No tree species of this kind were found."],
];

export function sentKeyEntry(key: string | undefined | null, fallback: LocEntry): LocEntry {
  if (!key) return fallback;
  for (const e of SENT_KEYS) if (e[0] === key) return e;
  return [key, fallback[1]];
}

/*
 * The game's own efficiency factor names: `SelectedInfoPanel.EFFICIENCY_FACTORS[<enum name>]` (EfficiencySection.cs:32-42).
 * English fallbacks read from the installed Content/Game/Locale.cok (en-US block). Not our keys: they are not in LocaleEN.cs
 * and gate R4 does not check them (its key pattern is SkiResort./Assets./SubServices.).
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

/*
 * The labels of a building tile's draw rows, vanilla's own (the asset panel's PrefabUISystem ConsumptionBinder and
 * UpkeepPropertyBinderSystem, PrefabUISystem.cs:539-560 / 626-690; the installed index.js renders each row only when its value
 * is > 0, with units Power / VolumePerMonth / WeightPerMonth / moneyPerMonth). English fallbacks read from the installed
 * Content/Game/Locale.cok (en-US). Not our keys: not in LocaleEN.cs, gate R4 does not check them (like factorEntry).
 */
export const DRAW_LABEL = {
  upkeep: ["Properties.UPKEEP", "Upkeep"],
  electricity: ["Properties.ELECTRICITY_CONSUMPTION", "Electricity Consumption"],
  water: ["Properties.WATER_CONSUMPTION", "Water Consumption"],
  garbage: ["Properties.GARBAGE_ACCUMULATION", "Garbage Accumulation"],
} as const;

/* The game's text for a factor (unknown name: the raw enum name as the fallback). */
export function factorEntry(name: string): LocEntry {
  return [`SelectedInfoPanel.EFFICIENCY_FACTORS[${name}]`, EFFICIENCY_FACTOR_FALLBACK[name] ?? name];
}

/*
 * The game's own month names, `Common.MONTH:<0-11>` (an indexed entry: the installed index.js `Common.MONTH:new Ec` builds the
 * id `${id}:${index}`; the vanilla birth-date picker renders index 0 for January). Present in all 12 shipped locales of
 * Content/Game/Locale.cok. Not our keys: not in LocaleEN.cs, gate R4 does not check them (like factorEntry). Fallbacks:
 * English.
 */
const MONTH_FALLBACK: readonly string[] = [
  "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
];

/* The month's entry for a CityStats.month index (0..11); null when unknown (-1 or out of range). */
export function monthEntry(month: number): LocEntry | null {
  return Number.isInteger(month) && month >= 0 && month < 12 ? [`Common.MONTH:${month}`, MONTH_FALLBACK[month]] : null;
}

/* The {MONTH} argument: the month `back` months before city.month, translated; "–" while the month is not known. */
export function monthName(month: number, t: (e: LocEntry) => string, back = 0): string {
  const e = monthEntry(Number.isInteger(month) && month >= 0 ? (month - back + 12 * 4) % 12 : -1);
  return e ? t(e) : "–";
}

/* Every month's name, translated: width samples for a cell that shows a month. */
export function allMonthNames(t: (e: LocEntry) => string): string[] {
  return MONTH_FALLBACK.map((_, i) => monthName(i, t));
}

/*
 * The game's short month names, `Common.MONTH_SHORT:<0-11>` (the same indexed entry kind as Common.MONTH; installed index.js
 * `MONTH_SHORT:new Ec` @444971, rendered with {index} by the vanilla time-line charts @953286; Locale.cok holds
 * `Common.MONTH_SHORT:9` in all 12 locales: "Oct" in English, "Okt" in German, "10" in the CJK tables, so the index is 0-based
 * like Common.MONTH). The History view's axis labels. Not our keys: not in LocaleEN.cs (like monthEntry).
 */
const MONTH_SHORT_FALLBACK: readonly string[] = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/* The short name of a calendar month 0..11, translated; "–" when out of range. */
export function monthShortName(month: number, t: (e: LocEntry) => string): string {
  return Number.isInteger(month) && month >= 0 && month < 12 ? t([`Common.MONTH_SHORT:${month}`, MONTH_SHORT_FALLBACK[month]]) : "–";
}
