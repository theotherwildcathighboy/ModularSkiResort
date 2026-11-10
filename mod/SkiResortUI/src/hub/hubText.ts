/*
 * src/hub/hubText.ts - every player-facing string of the hub panel: locale key + English fallback.
 * Every entry is a literal `[key, english]` tuple (gate R4 compares each with mod/SkiResortMod/LocaleEN.cs). An entry whose English
 * holds "{" lives in HUB_FMT only (R5 / R13). Numbers are always arguments (parts.tsx numArg). Texts with "TIP" in the key have at most
 * 25 words (two lift-money tips have own caps). CatalogGuardTests guards the texts that bake in catalog numbers. Glyphs: the safe
 * list of tools/check-bundle.js. US spelling only.
 * Engine notes: EngineNotes/hubText.ts.md
 */
import type { LocEntry } from "../l10n";
import { BAND, clampGladeLevel } from "../bindings";
import type { BuildItemId, RunKind } from "../bindings";
import type { NextVariant, StatusWord } from "./hubRules";

/* Plain entries (no placeholder). */
export const HUB_LOC = {
  sectionFailed: ["SkiResort.Hub.SECTION_FAILED", "This part of the hub failed. Details are in the mod log."],
  sectionTrees: ["SkiResort.Hub.SECTION[Trees]", "Trees"],

  busCardTitle: ["Assets.NAME[SkiCoachBus]", "Ski Bus"],
  busCardGuests: ["SkiResort.Hub.BUS_CARD[Guests]", "Guests by bus this month"],
  busCardBill: ["SkiResort.Hub.BUS_CARD[Bill]", "Coach bill"],
  busCardDistance: ["SkiResort.Hub.BUS_CARD[Distance]", "To the hill lodge, straight line"],
  busCardStatus: ["SkiResort.Hub.BUS_CARD[Status]", "Status"],
  busCardNoRoad: ["SkiResort.Hub.BUS_CARD[NoRoad]", "No road to the hill"],
  busCardNoHillLodge: ["SkiResort.Hub.BUS_CARD[NoHillLodge]", "No hill lodge"],
  busCardOff: ["SkiResort.Hub.BUS_CARD[Off]", "Not running"],

  /* the merge card (contract 35) and the Overview's merged buildings fold */
  mergeCardTitle: ["SkiResort.Hub.MERGE_CARD[Title]", "Merge into a ski area"],
  mergeCardTip: ["SkiResort.Hub.MERGE_CARD[Tip]", "Merge fee: the tier's price less the building's own cost. Monthly fee: the tier's upkeep less its own, billed only near the runs and lifts."],
  mergeCardNote: ["SkiResort.Hub.MERGE_CARD[Note]", "Becomes one of this ski area's lodges and keeps working as before. Its tier adds score, skier room, staff and tourism. Guests still arrive through the road lodges."],
  mergeCardNear: ["SkiResort.Hub.MERGE_CARD[Near]", "Near the runs and lifts"],
  mergeCardFar: ["SkiResort.Hub.MERGE_CARD[Far]", "Too far from the runs and lifts to count"],
  mergeCardTier: ["SkiResort.Hub.MERGE_CARD[Tier]", "Lodge tier"],
  mergeCardOnce: ["SkiResort.Hub.MERGE_CARD[Once]", "Merge fee"],
  mergeCardMonthly: ["SkiResort.Hub.MERGE_CARD[Monthly]", "Monthly fee"],
  mergeCardStatus: ["SkiResort.Hub.MERGE_CARD[Status]", "Status"],
  mergeCardMerge: ["SkiResort.Hub.MERGE_CARD[Merge]", "Merge"],
  mergeCardChange: ["SkiResort.Hub.MERGE_CARD[Change]", "Change tier"],
  mergeCardUnmerge: ["SkiResort.Hub.MERGE_CARD[Unmerge]", "Unmerge"],
  mergeCardUnmergeTip: ["SkiResort.Hub.MERGE_CARD[UnmergeTip]", "The building stays. Fees paid are not refunded."],
  mergeCardTooSmall: ["SkiResort.Hub.MERGE_CARD[TooSmall]", "The lot is too small for any lodge tier"],
  mergeCardNoMoney: ["SkiResort.Hub.MERGE_CARD[NoMoney]", "Not enough money"],
  mergeStateCounted: ["SkiResort.Hub.MERGE_STATE[Counted]", "Counts as a lodge"],
  mergeStateNoArea: ["SkiResort.Hub.MERGE_STATE[NoArea]", "No ski area in range"],
  mergeStateTooFar: ["SkiResort.Hub.MERGE_STATE[TooFar]", "Too far from the runs and lifts"],
  mergeStateAssetMissing: ["SkiResort.Hub.MERGE_STATE[AssetMissing]", "Asset changed, not counted"],
  mergeStateInactive: ["SkiResort.Hub.MERGE_STATE[Inactive]", "Deactivated"],
  mergeStateDestroyed: ["SkiResort.Hub.MERGE_STATE[Destroyed]", "Destroyed"],
  mergeStateUnknown: ["SkiResort.Hub.MERGE_STATE[Unknown]", "Checking"],
  mergedFold: ["SkiResort.Hub.MERGED_FOLD", "Merged buildings"],
  mergedFoldTip: ["SkiResort.Hub.MERGED_FOLD_TIP", "Buildings merged into this ski area as lodges. Each keeps working as before and adds its tier's score, skier room and staff."],
  /* the merge overlay chip in the merged buildings fold (contract 36) */
  mergeOverlay: ["SkiResort.Hub.MERGE_OVERLAY", "Mergeable buildings"],
  /* the Buildings tab's Mergeable buildings toggle (contract 37, its label is MERGE_OVERLAY) and the green / gray legend */
  mergeTileFact: ["SkiResort.Hub.MERGE_TILE[Fact]", "A landmark, signature or recreation building near the runs can serve as a lodge"],
  mergeTileArmed: ["SkiResort.Hub.MERGE_TILE[Armed]", "Outlined buildings can join this ski area as lodges"],
  mergeLegendFits: ["SkiResort.Hub.MERGE_LEGEND[Fits]", "Tier fits"],
  mergeLegendTooSmall: ["SkiResort.Hub.MERGE_LEGEND[TooSmall]", "Too small"],

  resortPrev: ["SkiResort.Hub.RESORT_PREV", "Previous ski area"],
  resortNext: ["SkiResort.Hub.RESORT_NEXT", "Next ski area"],
  resortFly: ["SkiResort.Hub.RESORT_FLY", "Fly to the Welcome Sign"],
  /* the selector's caption, the open list's note, and an item's counts when both are 1 (the other forms: HUB_FMT) */
  resortCaption: ["SkiResort.Hub.RESORT_CAPTION", "Ski area"],
  resortMenuNote: ["SkiResort.Hub.RESORT_MENU_NOTE", "Selecting a sign, a lodge or a station on the map shows its ski area here."],
  resortCountsOne: ["SkiResort.Hub.RESORT_COUNTS[One]", "1 run · 1 lodge"],
  unlinkedEntry: ["SkiResort.Hub.UNLINKED_ENTRY", "Not linked to a ski area"],
  unlinkedHint: ["SkiResort.Hub.UNLINKED_HINT", "Out of range of every Welcome Sign."],
  rename: ["SkiResort.Hub.RENAME", "Rename"],
  renameTip: ["SkiResort.Hub.RENAME_TIP", "An empty name restores the default."],
  runDelete: ["SkiResort.Hub.RUN_DELETE", "Delete this cat track"],
  runDeleteTip: ["SkiResort.Hub.RUN_DELETE_TIP", "The ground stays graded and the fee is not refunded."],
  runDeleteConfirm: ["SkiResort.Hub.RUN_DELETE_CONFIRM", "Click again to delete this cat track."],

  startTitle: ["SkiResort.Hub.START_TITLE", "Start a ski area"],
  startStep1: ["SkiResort.Hub.START_STEP[1]", "Welcome Sign"],
  startStep2: ["SkiResort.Hub.START_STEP[2]", "A lift: two stations, then the line"],
  startStep3: ["SkiResort.Hub.START_STEP[3]", "Runs from the top of the lift"],
  startStep4: ["SkiResort.Hub.START_STEP[4]", "A lodge within 800 m of the runs"],
  placeSign: ["SkiResort.Hub.PLACE_SIGN", "Place Welcome Sign"],

  statusNoRuns: ["SkiResort.Hub.STATUS_NO_RUNS", "Closed"],
  statusUnlinked: ["SkiResort.Hub.STATUS_UNLINKED", "No Welcome Sign in range"],
  statusTipRule: ["SkiResort.Hub.STATUS_TIP_RULE", "<mark:open> Open: enough snow on 60 % of the run. <mark:closed> Closed: under 40 %. <mark:patchy> Patchy: in between, and the run scores part of its value."],
  statusTipEstimated: ["SkiResort.Hub.STATUS_TIP_ESTIMATED", "Some runs are estimated from the day's temperature until their snow can be measured."],
  statusTipGating: ["SkiResort.Hub.STATUS_TIP_GATING", "Snow & season gating is off in Options, so every run counts as open."],
  statusTipVisuals: ["SkiResort.Hub.STATUS_TIP_VISUALS", "Snow is hidden in your Gameplay settings. Runs still open on the simulated snow."],
  /* the hint slot at powder >= 0.5 (hubRules.statusHint) */
  powderDay: ["SkiResort.Hub.POWDER_DAY", "Powder day"],

  problemNone: ["SkiResort.Hub.PROBLEM[None]", "No problems"],
  problemUnlinked: ["SkiResort.Hub.PROBLEM[Unlinked]", "Out of range of every Welcome Sign"],
  problemNoLodge: ["SkiResort.Hub.PROBLEM[NoLodge]", "No lodge"],
  problemLodgesAllOff: ["SkiResort.Hub.PROBLEM[LodgesAllOff]", "All lodges deactivated"],
  problemNoRuns: ["SkiResort.Hub.PROBLEM[NoRuns]", "No runs yet"],
  problemSignNoRoad: ["SkiResort.Hub.PROBLEM[SignNoRoad]", "Welcome Sign: road required"],
  problemLodgeNoRoad: ["SkiResort.Hub.PROBLEM[LodgeNoRoad]", "Lodge or school: road required"],
  problemSkiDaysCannotReach: ["SkiResort.Hub.PROBLEM[SkiDaysCannotReach]", "Ski-day cars not arriving"],
  problemDepotNoHillLodge: ["SkiResort.Hub.PROBLEM[DepotNoHillLodge]", "Bus depot: no hill lodge"],
  problemDepotNoRoad: ["SkiResort.Hub.PROBLEM[DepotNoRoad]", "Bus depot: road required"],
  problemTipDepotNoHillLodge: ["SkiResort.Hub.PROBLEM_TIP[DepotNoHillLodge]", "A depot sends guests only while its ski area has a working hill lodge. A Hut or Summit counts. It bills nothing until then."],
  problemTipDepotNoRoad: ["SkiResort.Hub.PROBLEM_TIP[DepotNoRoad]", "A depot with no road sends nobody and bills nothing."],
  /* the row shows only while the sign carries jobs (MountainJobsRules.SignNoRoadRow): no active road lodge in the area */
  problemTipSignNoRoad: ["SkiResort.Hub.PROBLEM_TIP[SignNoRoad]", "No active lodge with a road. The sign is the job host for the Hut, Summit, patrol and off-road lodges. Their staff come by road."],
  problemNoParkingNearLodge: ["SkiResort.Hub.PROBLEM[NoParkingNearLodge]", "Lodge without parking"],
  problemLodgeNotSlopeside: ["SkiResort.Hub.PROBLEM[LodgeNotSlopeside]", "Lodge is far from the runs"],
  problemSchoolNoGreenRun: ["SkiResort.Hub.PROBLEM[SchoolNoGreenRun]", "Ski school: no green run"],
  problemNoLift: ["SkiResort.Hub.PROBLEM[NoLift]", "No lift yet"],
  problemTipNoLodge: ["SkiResort.Hub.PROBLEM_TIP[NoLodge]", "Ski days need any lodge within 800 m of the runs or lift stations. With no lodge, attraction is 0."],
  problemTipLodgesAllOff: ["SkiResort.Hub.PROBLEM_TIP[LodgesAllOff]", "Deactivated lodges do not count."],
  problemTipLodgesSomeOff: ["SkiResort.Hub.PROBLEM_TIP[LodgesSomeOff]", "Deactivated lodges do not count in the lodge base."],
  problemTipEfficiency: ["SkiResort.Hub.PROBLEM_TIP[Efficiency]", "The Welcome Sign and its lodges, as in their info panels. Every cause that costs 5 % or more is listed below."],
  // The lift stops, serves no run, and is still billed (no "full": the NoOpenRun quarter can still apply). Power is the base
  // station's only.
  problemTipLiftNoPower: ["SkiResort.Hub.PROBLEM_TIP[LiftNoPower]", "Its base station has no power: the lift stops and serves no run. Upkeep is still charged."],
  problemTipLiftSectionMissing: ["SkiResort.Hub.PROBLEM_TIP[LiftSectionMissing]", "A section was removed or is being redrawn. Until the line is complete, the lift adds no points and pays a quarter of its upkeep."],
  problemTipLiftUnfinished: ["SkiResort.Hub.PROBLEM_TIP[LiftUnfinished]", "The line does not join a base station and a top station yet."],
  problemTipStations: ["SkiResort.Hub.PROBLEM_TIP[Stations]", "A station is placed, but no line joins it to its partner yet."],
  problemTipLiftChairsStopped: ["SkiResort.Hub.PROBLEM_TIP[LiftChairsStopped]", "The lift is complete and still adds its points, but its chairs stopped. The mod log says why."],
  problemTipRunsUnrateable: ["SkiResort.Hub.PROBLEM_TIP[RunsUnrateable]", "The outline is too large or too thin, or has no terrain under it. These runs add only a few points each."],
  problemTipRunsRating: ["SkiResort.Hub.PROBLEM_TIP[RunsRating]", "The mod rates runs one at a time. A new run adds 0 until it has a rating. A reshaped run keeps its last rating."],
  problemTipFreeLodges: ["SkiResort.Hub.PROBLEM_TIP[FreeLodges]", "They are out of range of every Welcome Sign, so they add nothing to any ski area."],
  problemTipSchoolsNotLinked: ["SkiResort.Hub.PROBLEM_TIP[SchoolsNotLinked]", "Out of range of every Welcome Sign, so they teach nobody."],
  problemTipSkiDaysCannotReach: ["SkiResort.Hub.PROBLEM_TIP[SkiDaysCannotReach]", "Many recent ski-day guests with a car did not reach the lodge in time. Fewer are sent until more arrive."],
  problemTipSkiDaysNoGuestsNear: ["SkiResort.Hub.PROBLEM_TIP[SkiDaysNoGuestsNear]", "Ski-day guests come from homes and hotels, within the longest ski-day trip in Options. Beyond about 7 km few arrive in time."],
  problemTipNoParkingNearLodge: ["SkiResort.Hub.PROBLEM_TIP[NoParkingNearLodge]", "No car spaces within 600 m. Tourists from outside the city drive here, so none arrive. Hotel guests within 900 m walk."],
  problemTipLodgeNotSlopeside: ["SkiResort.Hub.PROBLEM_TIP[LodgeNotSlopeside]", "Ski-day guests only go to a lodge within 800 m of this ski area's runs or lift stations."],
  problemTipMixedLiftTypes: ["SkiResort.Hub.PROBLEM_TIP[MixedLiftTypes]", "A line and its stations must be one lift type. Until then, the lift adds no points."],
  /* C# HubProblemCode LiftTrainLimit (LiftAlignMath.PoseTrain, relative to the mast since DECISIONS 600). Conditions: a half past its 30 deg pitch
     limit (headed DQ / GD: head 20 + half 20), the halves past the 40 deg break, or a rope half past the carrier world-half
     clearance. Shown only. The lift is unchanged */
  /* the LiftLowSpan row's {CARRIERS} (pct 1 = cabins: gondola, tram) and its tip's {WHERE} when both ends are stations */
  carriersChairs: ["SkiResort.Hub.CARRIERS[Chairs]", "chairs"],
  carriersCabins: ["SkiResort.Hub.CARRIERS[Cabins]", "cabins"],
  lowSpanStations: ["SkiResort.Hub.LOW_SPAN[Stations]", "Between the stations"],
  problemTipLiftTrainLimit: ["SkiResort.Hub.PROBLEM_TIP[LiftTrainLimit]", "Wheels tilt 30° beyond the tower's lean (detachables, gondolas 40°, tram saddles 60°) and fold 40° (trams 60°). Past that the rope leaves them."],
  /* C# HubProblemCode LiftBend (over LiftAlignMath.BendWarnDeg, 5 deg). Shown only. The lift is unchanged */
  problemTipLiftBend: ["SkiResort.Hub.PROBLEM_TIP[LiftBend]", "Real lift lines turn 5° or less at a tower. Past that the rope runs off the outer wheels. The lift still runs and scores."],
  problemTipSummitLodgeNotLiftServed: ["SkiResort.Hub.PROBLEM_TIP[SummitLodgeNotLiftServed]", "With every run closed, a Hut or Summit lodge scores more near a working lift top (300 m) or a served run (100 m)."],
  problemTipSchoolNoGreenRun: ["SkiResort.Hub.PROBLEM_TIP[SchoolNoGreenRun]", "The ski school adds to the score only while at least one <icon:green> run is <icon:openpatchy>."],
  pagePrev: ["SkiResort.Hub.PAGE_PREV", "Previous page"],
  pageNext: ["SkiResort.Hub.PAGE_NEXT", "Next page"],
  lodgeGroup: ["SkiResort.Hub.BUILD_GROUP[Lodges]", "Lodges"],
  liftTool: ["SkiResort.Hub.LIFT_TOOL", "Lift generator"],
  liftToolTip: ["SkiResort.Hub.LIFT_TOOL_TIP", "Plan a lift line between two stations: Ctrl+Shift+L"],
  liftToolUnavailable: ["SkiResort.Hub.LIFT_TOOL_UNAVAILABLE", "Available once a city is loaded."],

  tileAttraction: ["SkiResort.Hub.TILE[Attraction]", "Attraction"],
  tileRuns: ["SkiResort.Hub.TILE[Runs]", "Runs"],
  /* a sign's Runs tile ("2 / 3" counts the open runs). The Not linked tile keeps TILE[Runs] (a total) */
  tileRunsOpen: ["SkiResort.Hub.TILE[RunsOpen]", "Runs open"],
  tileLifts: ["SkiResort.Hub.TILE[Lifts]", "Lifts"],
  tileGuests: ["SkiResort.Hub.TILE[Guests]", "Guests"],
  tileMoney: ["SkiResort.Hub.TILE[Money]", "Finances"],
  tileTipAttraction: ["SkiResort.Hub.TILE_TIP[Attraction]", "How strongly this ski area draws tourists, on the game's scale: a park is about 10, a landmark about 120. The most is 200."],
  tileTipHeld: ["SkiResort.Hub.TILE_TIP_HELD", "Checking runs and snow after loading. The game keeps the saved score meanwhile."],
  tileTipRuns: ["SkiResort.Hub.TILE_TIP[Runs]", "Runs fully open now, out of all runs in this ski area."],
  tileTipLifts: ["SkiResort.Hub.TILE_TIP[Lifts]", "Complete lifts out of every lift drawn, unfinished ones included. Without power a complete lift stops and adds nothing."],
  tileTipSinceLoad: ["SkiResort.Hub.TILE_TIP_SINCE_LOAD", "Counted since you loaded the city."],
  tileTipMoney: ["SkiResort.Hub.TILE_TIP[Money]", "Net of the month so far: all resort income booked, minus lifts, runs, buildings and wages. The Finances view's Net."],
  tileTipEst: ["SkiResort.Hub.TILE_TIP_EST", "≈ Some income or costs are not counted yet. They show as 0."],
  tileTipEstBuildings: ["SkiResort.Hub.TILE_TIP_EST_BUILDINGS", "≈ The game's bill for buildings and wages is not known yet."],

  scoreTickPark: ["SkiResort.Hub.SCORE_TICK[Park]", "park 10"],
  scoreTickAttraction: ["SkiResort.Hub.SCORE_TICK[Attraction]", "tourist attraction 50"],
  scoreTickLandmark: ["SkiResort.Hub.SCORE_TICK[Landmark]", "landmark 120"],
  receiptLodgeBase: ["SkiResort.Hub.RECEIPT[LodgeBase]", "Lodge base"],
  receiptLodgeBaseHalf: ["SkiResort.Hub.RECEIPT[LodgeBaseHalf]", "Lodge base, halved"],
  receiptLodgeBaseNone: ["SkiResort.Hub.RECEIPT[LodgeBaseNone]", "Lodge base, no lodge"],
  receiptTipLodgeBase: ["SkiResort.Hub.RECEIPT_TIP[LodgeBase]", "The best lodge's value, plus a little for each other lodge, up to 24 more. About half while every run is closed."],
  receiptTerrain: ["SkiResort.Hub.RECEIPT[Terrain]", "Runs, lifts and school"],
  receiptTipRuns: ["SkiResort.Hub.RECEIPT_TIP[Runs]", "The total of all runs. Harder and bigger runs score more, and a run needs snow and a lift to score in full."],
  receiptTipLifts: ["SkiResort.Hub.RECEIPT_TIP[Lifts]", "Points per complete, powered lift: fixed-grip 6, detachable 15, gondola 25, tram 30. While every run is closed, chairlifts and gondolas add a quarter."],
  /* the school's line of the RECEIPT_PARTS tooltip (shown while ResortLive.schoolValue > 0) */
  receiptTipSchool: ["SkiResort.Hub.RECEIPT_TIP[School]", "An active ski school adds 12 while at least one <icon:green> run is <icon:openpatchy>."],
  receiptTipVariety: ["SkiResort.Hub.RECEIPT_TIP[Variety]", "+15 % (1.15) while at least one <icon:green>, one <icon:blue> and one <icon:black> or <icon:dblack> run is <icon:openpatchy>. Otherwise no bonus."],
  /*
   * each term of the "Runs, lifts and school" section is its own labeled row. The full equation is the section header's tooltip
   * (RECEIPT_PARTS[_SCHOOL] + RECEIPT_ROOT)
   */
  receiptRowRuns: ["SkiResort.Hub.RECEIPT_ROW[Runs]", "Runs"],
  receiptRowLifts: ["SkiResort.Hub.RECEIPT_ROW[Lifts]", "Lifts"],
  receiptRowSchool: ["SkiResort.Hub.RECEIPT_ROW[School]", "Ski school"],
  receiptRowVariety: ["SkiResort.Hub.RECEIPT_ROW[Variety]", "Variety"],
  receiptRowVertical: ["SkiResort.Hub.RECEIPT_ROW[Vertical]", "Vertical"],
  /*
   * a row whose 0 has a cause says it with the warning glyph instead of a tooltip sentence (hubRules.schoolState: no school at
   * all, or a school but no green run counts)
   */
  noSchool: ["SkiResort.Hub.NO_SCHOOL", "No ski school"],
  schoolNoGreen: ["SkiResort.Hub.SCHOOL_NO_GREEN", "No green run"],
  /* a school that is built but adds 0 says why (hubRules.schoolState "off" / "waiting") */
  schoolOff: ["SkiResort.Hub.SCHOOL_OFF", "Deactivated"],
  schoolWaiting: ["SkiResort.Hub.SCHOOL_WAITING", "No run open yet"],
  receiptOpenNone: ["SkiResort.Hub.RECEIPT_OPEN_NONE", "Runs open now: –"],
  receiptEfficiency: ["SkiResort.Hub.RECEIPT[Efficiency]", "Efficiency"],
  receiptTipEfficiency: ["SkiResort.Hub.RECEIPT_TIP[Efficiency]", "How well the Welcome Sign and its lodges work: staff, power, water, garbage, budget. The score uses a one-day average, held between 25 % and 100 %."],
  receiptResult: ["SkiResort.Hub.RECEIPT[Result]", "Attraction"],
  receiptResultNoLodge: ["SkiResort.Hub.RECEIPT[ResultNoLodge]", "Attraction, no lodge"],
  receiptTipResult: ["SkiResort.Hub.RECEIPT_TIP[Result]", "Lodge base plus runs, lifts and school, times efficiency, up to 200. A lodge's own info panel shows 0: it does not show this score."],
  /* icon scheme (hub contract 40): each icon's word (the word form of <icon:NAME>, hubIcons.ts HUB_ICON) and the header legend */
  iconGreen: ["SkiResort.Hub.ICON[green]", "green"],
  iconBlue: ["SkiResort.Hub.ICON[blue]", "blue"],
  iconBlack: ["SkiResort.Hub.ICON[black]", "black"],
  iconDblack: ["SkiResort.Hub.ICON[dblack]", "double-black"],
  iconOpen: ["SkiResort.Hub.ICON[open]", "open"],
  iconPatchy: ["SkiResort.Hub.ICON[patchy]", "patchy"],
  iconClosed: ["SkiResort.Hub.ICON[closed]", "closed"],
  iconOpenpatchy: ["SkiResort.Hub.ICON[openpatchy]", "open or patchy"],
  iconSchool: ["SkiResort.Hub.ICON[school]", "ski school"],
  iconLift: ["SkiResort.Hub.ICON[lift]", "lift"],
  iconPower: ["SkiResort.Hub.ICON[power]", "power"],
  iconSnow: ["SkiResort.Hub.ICON[snow]", "snow"],
  iconLegend: ["SkiResort.Hub.ICON_LEGEND", "Symbols"],
  iconLegendRatings: ["SkiResort.Hub.ICON_LEGEND[Ratings]", "Run ratings"],
  iconLegendStates: ["SkiResort.Hub.ICON_LEGEND[States]", "Run states"],
  iconLegendArea: ["SkiResort.Hub.ICON_LEGEND[Area]", "Ski area"],
  iconLegendCity: ["SkiResort.Hub.ICON_LEGEND[City]", "City services"],
  /* the receipt's missing lines (contract 40): one line per row, at most 10 words with all its parts (gate T18.5) */
  receiptMissingVariety: ["SkiResort.Hub.RECEIPT_MISSING[Variety]", "Missing:"],
  varietyNeedGreen: ["SkiResort.Hub.RECEIPT_MISSING[Variety.Green]", "<icon:green> run"],
  varietyNeedBlue: ["SkiResort.Hub.RECEIPT_MISSING[Variety.Blue]", "<icon:blue> run"],
  varietyNeedBlack: ["SkiResort.Hub.RECEIPT_MISSING[Variety.Black]", "<icon:black> or <icon:dblack> run"],
  receiptMissingSchoolNone: ["SkiResort.Hub.RECEIPT_MISSING[SchoolNone]", "Missing: an active <icon:school>"],
  receiptMissingSchoolGreen: ["SkiResort.Hub.RECEIPT_MISSING[SchoolGreen]", "Missing: a <icon:green> run that is <icon:openpatchy>"],
  receiptMissingSchoolWaiting: ["SkiResort.Hub.RECEIPT_MISSING[SchoolWaiting]", "No run is <icon:openpatchy>"],
  receiptMissingRuns: ["SkiResort.Hub.RECEIPT_MISSING[Runs]", "Not in full:"],
  receiptMissingLifts: ["SkiResort.Hub.RECEIPT_MISSING[Lifts]", "Not counted:"],
  receiptMissingEfficiency: ["SkiResort.Hub.RECEIPT_MISSING[Efficiency]", "Held at the 25 % floor"],
  detailShare: ["SkiResort.Hub.DETAIL[Share]", "Share of city tourism"],
  shareTip: ["SkiResort.Hub.SHARE_TIP", "This ski area's part of what draws tourists to your city. The game squares each score, so 100 draws four times as much as 50."],
  sectionMountain: ["SkiResort.Hub.SECTION[Mountain]", "Mountain"],
  difficulty: ["SkiResort.Hub.DIFFICULTY", "Difficulty"],
  pending: ["SkiResort.Hub.PENDING", "Being rated"],
  unrateable: ["SkiResort.Hub.UNRATEABLE", "Cannot be rated: outline too large or too thin, or no terrain under it"],
  mountainTipVertical: ["SkiResort.Hub.MOUNTAIN_TIP[Vertical]", "From the lowest run bottom to the highest run top, closed runs too. ≈: some runs are still being rated."],
  area: ["SkiResort.Hub.AREA", "Skiable area"],
  areaTip: ["SkiResort.Hub.AREA_TIP", "The area of all runs, measured flat on the map, not along the slope."],
  // Snow is always two numbers: depth in cm and run coverage in %. "≈" marks an estimate. tools/test-hub-rules.js T18 guards it.
  mountainSnowDepth: ["SkiResort.Hub.MOUNTAIN[SnowDepth]", "Snow depth"],
  mountainSnowCover: ["SkiResort.Hub.MOUNTAIN[SnowCover]", "Run coverage"],
  mountainTipDepth: ["SkiResort.Hub.MOUNTAIN_TIP[Depth]", "Average snow depth on the runs."],
  mountainTipSnow: ["SkiResort.Hub.MOUNTAIN_TIP[Snow]", "Share of the run area with enough snow to ski."],
  unlinkedNote: ["SkiResort.Hub.UNLINKED_NOTE", "These runs and lifts belong to no ski area, so they score nothing and draw no guests."],

  treeTool: ["SkiResort.Hub.TREE_TOOL", "Tree tool"],
  treeToolTip: ["SkiResort.Hub.TREE_TOOL_TIP", "Plans trees on and around runs."],
  treesFewer: ["SkiResort.Hub.TREES_FEWER", "Fewer trees"],
  treesMore: ["SkiResort.Hub.TREES_MORE", "More trees"],
  treesApplyTo: ["SkiResort.Hub.TREES_APPLY_TO", "Apply to"],
  treesUnavailable: ["SkiResort.Hub.TREES_UNAVAILABLE", "The tree tool is not available right now."],
  gladeLevel: ["SkiResort.GladeTooltip.LEVEL", "Tree density (1-8)"],
  gladeTargetInterior: ["SkiResort.GladeTooltip.TARGET[Interior]", "Run interior"],
  gladeTargetTreeLine: ["SkiResort.GladeTooltip.TARGET[TreeLine]", "Tree line"],
  pisteMap: ["SkiResort.Hub.PISTE_MAP", "Piste map"],
  pisteMapTip: ["SkiResort.Hub.PISTE_MAP_TIP", "Draws this ski area's runs on the ground in difficulty colors while the hub is open. Natural: dashed. Too flat: gray. Cannot be rated: orange."],
  colLift: ["SkiResort.Hub.COL_LIFT", "Lift"],
  colLiftTip: ["SkiResort.Hub.COL_LIFT_TIP", "↑: a running lift reaches the top of this run."],
  colLength: ["SkiResort.Hub.COL_LENGTH", "Length"],
  colDrop: ["SkiResort.Hub.COL_DROP", "Drop"],
  colArea: ["SkiResort.Hub.COL_AREA", "Area"],
  colLengthTip: ["SkiResort.Hub.COL_LENGTH_TIP", "Horizontal length, estimated from the outline."],
  colDropTip: ["SkiResort.Hub.COL_DROP_TIP", "Approximate height from the lowest to the highest point of the outline."],
  runsEmpty: ["SkiResort.Hub.RUNS_EMPTY", "No runs yet."],
  runEstimated: ["SkiResort.Hub.RUN_ESTIMATED", "Estimated from the day's temperature."],
  /* the run tooltip's snow-gun line by RUN_GUN_WHY (the depth line is HUB_FMT.runDepth) */
  runGunsMaking: ["SkiResort.Hub.RUN_GUNS[Making]", "Snow guns: making snow now"],
  runGunsDepth: ["SkiResort.Hub.RUN_GUNS[DepthReached]", "Snow guns: off, the snow is deep enough"],
  runGunsWarm: ["SkiResort.Hub.RUN_GUNS[TooWarm]", "Snow guns: off, too warm"],
  runGunsSeason: ["SkiResort.Hub.RUN_GUNS[OutOfSeason]", "Snow guns: off, out of season (2 months before winter to 1 month after)"],
  runGunsWaiting: ["SkiResort.Hub.RUN_GUNS[Waiting]", "Snow guns: waiting for the first snow reading"],
  runOpenGating: ["SkiResort.Hub.RUN_OPEN_GATING", "Counts as open: snow gating is off."],
  runOpenNone: ["SkiResort.Hub.RUN_OPEN_NONE", "Not linked: no snow state."],
  runServedAnon: ["SkiResort.Hub.RUN_SERVED_ANON", "A running lift reaches its top."],
  runTypeNote: ["SkiResort.Hub.RUN_TYPE_NOTE", "Natural green and blue runs count less. A groomed run counts in full."],
  runCatTrackNote: ["SkiResort.Hub.RUN_CAT_TRACK_NOTE", "A cat track always rates green and counts a quarter of a run its size, in the score and the skier room."],
  runValue: ["SkiResort.Hub.RUN_VALUE", "Score value"],
  runCost: ["SkiResort.Hub.RUN_COST", "Upkeep"],
  /* the Runs view's prop rows (RunPropControls.tsx) */
  runPropsGuns: ["SkiResort.Hub.RUN_PROPS_GUNS", "Snow guns"],
  runPropsLights: ["SkiResort.Hub.RUN_PROPS_LIGHTS", "Lights"],
  runPropsOff: ["SkiResort.Hub.RUN_PROPS_OFF", "Off"],
  runPropsGunsTip: ["SkiResort.Hub.RUN_PROPS_GUNS_TIP", "Distance between snow guns on this run's edge. Off: no guns, no made snow and no snowmaking upkeep."],
  /* a cat track: its guns at Off also stop its snowmaking bill and its made snow (see DECISIONS 563, 568 and SkiRunPropSystem.CatTrackGunsOn) */
  runPropsGunsTipCatTrack: ["SkiResort.Hub.RUN_PROPS_GUNS_TIP[CatTrack]", "Distance between snow guns and hydrants on this cat track's edge. Off: no guns and no snowmaking upkeep. Made snow stays until it melts."],
  runPropsLightsTip: ["SkiResort.Hub.RUN_PROPS_LIGHTS_TIP", "Distance between floodlight masts on this run's edge. Lit runs stay open in the evening. Off: no lights and no lighting upkeep."],
  runPropsLightsTipCatTrack: ["SkiResort.Hub.RUN_PROPS_LIGHTS_TIP[CatTrack]", "Distance between floodlight masts on this cat track's edge. Lit tracks pay lighting upkeep in the snow season. Off: no lights and no lighting upkeep."],
  /* run types R3: the Groomed row (a re-emit, DECISIONS 577) and the paid-once note (RunRow.paidSteps) */
  runPropsGroomed: ["SkiResort.Hub.RUN_PROPS_GROOMED", "Groomed"],
  drawRunTitle: ["SkiResort.Hub.DRAW_RUN_TITLE", "New run"],
  runPropsOn: ["SkiResort.Hub.RUN_PROPS_ON", "On"],
  runPropsGroomedTip: ["SkiResort.Hub.RUN_PROPS_GROOMED_TIP", "A groomed run counts in full and can carry snow guns and lights. Off: no upkeep, nothing drawn on the ground, natural snow only."],
  runPropsNeedsGroomed: ["SkiResort.Hub.RUN_PROPS_NEEDS_GROOMED", "Snow guns and lights need a groomed run."],
  runPropsPaid: ["SkiResort.Hub.RUN_PROPS_PAID", "Its one-time fee is paid: turning it back on costs nothing."],
  runPropsUnpaid: ["SkiResort.Hub.RUN_PROPS_UNPAID", "Turning it on for the first time charges a one-time fee based on the run's area."],
  runPropsFewer: ["SkiResort.Hub.RUN_PROPS_FEWER", "Fewer"],
  runPropsMore: ["SkiResort.Hub.RUN_PROPS_MORE", "More"],
  runPropsFewerTip: ["SkiResort.Hub.RUN_PROPS_FEWER_TIP", "Farther apart. Past 200 m: off."],
  runPropsMoreTip: ["SkiResort.Hub.RUN_PROPS_MORE_TIP", "Closer together, down to 20 m."],
  cruxPitch: ["SkiResort.Tooltip.CRUX_PITCH", "Hardest unavoidable pitch"],
  steepestPatch: ["SkiResort.Tooltip.STEEPEST_PATCH", "Steepest patch (avoidable)"],

  liftsEmpty: ["SkiResort.Hub.LIFTS_EMPTY", "No lifts yet."],
  liftLine2SectionMissing: ["SkiResort.Hub.LIFT_LINE2[SectionMissing]", "Gap in the line"],
  liftLine2Unfinished: ["SkiResort.Hub.LIFT_LINE2[Unfinished]", "Not joined to both stations"],
  liftLine2StationsPair: ["SkiResort.Hub.LIFT_LINE2[StationsPair]", "No lift line yet"],
  liftLine2StationsBaseOnly: ["SkiResort.Hub.LIFT_LINE2[StationsBaseOnly]", "No top station yet"],
  liftLine2StationsTopOnly: ["SkiResort.Hub.LIFT_LINE2[StationsTopOnly]", "No base station yet"],
  liftRide: ["SkiResort.Hub.LIFT_RIDE", "Ride time"],
  liftLength: ["SkiResort.Hub.LIFT_LENGTH", "Length"],
  liftRise: ["SkiResort.Hub.LIFT_RISE", "Rise"],
  liftCapacity: ["SkiResort.Hub.LIFT_CAPACITY", "Capacity"],
  // LIFT_RIDE_TIP and LIFT_CAPACITY_TIP are in HUB_FMT ({SPEED}, {SEATS} per lift type). The totals row of the Lifts view sums
  // several types, so it takes this plain tip. The total sums only the lifts that carry.
  liftsTotalCapacityTip: ["SkiResort.Hub.LIFTS_TOTAL_CAPACITY_TIP", "Sum over complete lifts with power and an open run. Tram: time at station stops included. Riders are not simulated."],
  liftRuns: ["SkiResort.Hub.LIFT_RUNS", "Runs it serves"],
  liftCost: ["SkiResort.Hub.LIFT_COST", "Upkeep"],
  // Power does not lower the bill, so a reduced row whose status is No power is billed reduced only for NoOpenRun.
  // hubRules.liftCostReason picks this key for it, so the text names that reason.
  liftCostReducedNoPower: ["SkiResort.Hub.LIFT_COST_REDUCED[NoPower]", "Quarter upkeep: no open run (a tram half)"],
  liftCostReducedUnfinished: ["SkiResort.Hub.LIFT_COST_REDUCED[Unfinished]", "Upkeep at a quarter while unfinished"],
  liftCostReducedSectionMissing: ["SkiResort.Hub.LIFT_COST_REDUCED[SectionMissing]", "Upkeep at a quarter while a section is missing"],
  // a complete, powered lift in a ski area with no counted run
  liftCostReducedNoOpenRun: ["SkiResort.Hub.LIFT_COST_REDUCED[NoOpenRun]", "Quarter upkeep: no open run (a tram half)"],
  liftsTotalLength: ["SkiResort.Hub.LIFTS_TOTAL_LENGTH", "Total lift length"],
  liftsTotalCapacity: ["SkiResort.Hub.LIFTS_TOTAL_CAPACITY", "Capacity, all lifts"],
  liftSections: ["SkiResort.Hub.LIFT_SECTIONS", "Sections"],
  liftTowers: ["SkiResort.Hub.LIFT_TOWERS", "Towers"],
  liftChairs: ["SkiResort.Hub.LIFT_CHAIRS", "Chairs"],
  liftStraight: ["SkiResort.Hub.LIFT_STRAIGHT", "Stations straight (180°)"],
  liftTowerRemoved: ["SkiResort.Hub.LIFT_TOWER_REMOVED", "A tower was removed by another tool."],
  liftNameTip: ["SkiResort.Hub.LIFT_NAME_TIP", "The name is stored on the base station, or on the top station while there is no base."],
  liftSignTip: ["SkiResort.Hub.LIFT_SIGN_TIP", "A lift is linked to the ski area of its base station. A run stays linked to the ski area it was drawn for."],
  alignOffset: ["SkiResort.LiftTooltip.ALIGN_OFFSET", "Offset from the straight line"],
  alignAngle: ["SkiResort.LiftTooltip.ALIGN_ANGLE", "Angle off straight"],

  sectionGuests: ["SkiResort.Hub.SECTION[Guests]", "Guests"],
  /* the Ski days row and its tooltip body */
  guestsRowSkiDays: ["SkiResort.Hub.GUESTS_ROW[SkiDays]", "Reached a lodge"],
  guestsTipSkiDays: ["SkiResort.Hub.GUESTS_TIP[SkiDays]", "Skiers sent out on a ski day, counted when they reach a lodge. They also count as residents or tourists."],
  /* the three group headers of the Guests table and the dim note under the first */
  guestsGroupLodges: ["SkiResort.Hub.GUESTS_GROUP[Lodges]", "At the lodges"],
  guestsGroupSent: ["SkiResort.Hub.GUESTS_GROUP[Sent]", "Ski-day trips"],
  guestsGroupTickets: ["SkiResort.Hub.GUESTS_GROUP[Tickets]", "Lift tickets"],
  /* the CAR SPACES fold over the car space rows, said once instead of on every row */
  guestsGroupCarSpaces: ["SkiResort.Hub.GUESTS_GROUP[CarSpaces]", "Car spaces"],
  guestsNoteNeverSki: ["SkiResort.Hub.GUESTS_NOTE[NeverSki]", "Includes visitors who never ski"],
  guestsRowTourists: ["SkiResort.Hub.GUESTS_ROW[Tourists]", "Tourists"],
  guestsRowGroups: ["SkiResort.Hub.GUESTS_ROW[Groups]", "Tourist groups"],
  guestsRowResidents: ["SkiResort.Hub.GUESTS_ROW[Residents]", "Residents"],
  guestsRowPaid: ["SkiResort.Hub.GUESTS_ROW[Paid]", "Paid"],
  /* the car space rows under the folds (GuestsView LodgeParkingLines): SkiParkingRules.RadiusM, per lodge */
  guestsTipCarSpaces: ["SkiResort.Hub.GUESTS_TIP[CarSpaces]", "Street, lot and garage spaces within 600 m of this lodge. Tourist parties drive here."],
  /* the CAR SPACES fold header: its value is the total over the rows, one per active road lodge */
  guestsTipCarSpacesAll: ["SkiResort.Hub.GUESTS_TIP[CarSpacesAll]", "Street, lot and garage spaces within 600 m of each active road lodge. Tourist parties drive here."],
  guestsTipTourists: ["SkiResort.Hub.GUESTS_TIP[Tourists]", "People in tourist groups that arrived at this ski area's lodges or Welcome Sign."],
  guestsTipGroups: ["SkiResort.Hub.GUESTS_TIP[Groups]", "Tourists arrive in groups. Each group counts once."],
  guestsTipResidents: ["SkiResort.Hub.GUESTS_TIP[Residents]", "Residents of your city who came to a lodge for leisure."],
  guestsTipTickets: ["SkiResort.Hub.GUESTS_TIP[Tickets]", "Paid by each guest's household into your city's balance."],
  guestsLastNa: ["SkiResort.Hub.GUESTS_LAST_NA", "Not counted: counting starts when the city loads."],
  guestsLastPartial: ["SkiResort.Hub.GUESTS_LAST_PARTIAL", "Not counted in full: the city was loaded, or time skipped ahead, during that month."],
  // the ski-day state line (hubRules.skiDayLine, whose Room variants are HUB_FMT) and its tooltips (skiDayTip)
  skiDaysOff: ["SkiResort.Hub.SKI_DAYS[Off]", "Ski days off in Options"],
  skiDaysSignOff: ["SkiResort.Hub.SKI_DAYS[SignOff]", "Closed: Welcome Sign stopped"],
  skiDaysNoRun: ["SkiResort.Hub.SKI_DAYS[NoRun]", "Closed: no open run"],
  skiDaysNoLodge: ["SkiResort.Hub.SKI_DAYS[NoLodge]", "No lodge for ski days"],
  skiDaysNight: ["SkiResort.Hub.SKI_DAYS[Night]", "Closed for the night"],
  skiDaysFull: ["SkiResort.Hub.SKI_DAYS[Full]", "Full right now"],
  skiDaysTipOff: ["SkiResort.Hub.SKI_DAYS_TIP[Off]", "Locals who ski and Tourists who ski are both 0 in Options."],
  skiDaysTipNoLodge: ["SkiResort.Hub.SKI_DAYS_TIP[NoLodge]", "Needs an active lodge within 800 m of runs or stations. Off-road: a Hut, Summit, or a lodge whose cat tracks reach a road lodge."],
  skiDaysTipRuns: ["SkiResort.Hub.SKI_DAYS_TIP[Runs]", "The runs set the limit: bigger and easier runs hold more skiers at once."],
  skiDaysTipLodges: ["SkiResort.Hub.SKI_DAYS_TIP[Lodges]", "The lodges set the limit: each lodge takes 360 skiers at once, more on a powder day."],
  estRowTourists: ["SkiResort.Hub.EST_ROW[Tourists]", "Tourists drawn ≈"],
  estTouristsTip: ["SkiResort.Hub.EST_TOURISTS_TIP", "An estimate from the game's tourist formula. Tourism mods can replace the formula, so real arrivals can differ."],
  estToNote: ["SkiResort.Hub.EST_TO_NOTE", "Detected: Tourism Overhaul. It sets tourist numbers."],
  estToTip: ["SkiResort.Hub.EST_TO_TIP", "Tourism Overhaul sets tourist numbers in this city, so no estimate is shown."],
  // the trip-planner note (Known / KnownOff carry {NAME}: HUB_FMT)
  tripPlannerNoteUnknown: ["SkiResort.Hub.TRIP_PLANNER_NOTE[Unknown]", "Part of the game's trip planner is disabled (another mod, or a game error), so trip times can differ."],
  tripPlannerNoteUnknownOff: ["SkiResort.Hub.TRIP_PLANNER_NOTE[UnknownOff]", "Ski days off: the game's leisure systems are disabled (another mod, or a game error)."],
  tripPlannerTip: ["SkiResort.Hub.TRIP_PLANNER_TIP", "Read at each city load: which of the game's trip systems are on, and which known mods are loaded. The mod log lists them."],
  guestsNoteWhere: ["SkiResort.Hub.GUESTS_NOTE[Where]", "Counted when tourists arrive at this ski area's lodge or Welcome Sign, and when residents come to a lodge for leisure."],
  guestsNoteSinceLoad: ["SkiResort.Hub.GUESTS_NOTE[SinceLoad]", "Counting started when you loaded the city, partway through the month."],
  ticketsNoteOff: ["SkiResort.Hub.TICKETS_NOTE[Off]", "Lift tickets off: price 0 in Options"],
  ticketsNoteNoEfficiency: ["SkiResort.Hub.TICKETS_NOTE[NoEfficiency]", "No lift tickets: Welcome Sign stopped"],
  ticketsNoteNothingOpen: ["SkiResort.Hub.TICKETS_NOTE[NothingOpen]", "No lift tickets: no run is open"],
  ticketsNoteNotKnownYet: ["SkiResort.Hub.TICKETS_NOTE[NotKnownYet]", "Lift tickets: checking the runs"],

  sectionMoney: ["SkiResort.Hub.SECTION[Money]", "Finances"],
  moneyTicketsOff: ["SkiResort.Hub.MONEY_TICKETS[Off]", "Lift tickets: off"],
  moneyRowTickets: ["SkiResort.Hub.MONEY_ROW[Tickets]", "Lift tickets"],
  moneyTicketsTip: ["SkiResort.Hub.MONEY_TICKETS_TIP", "Booked this month so far: the ticket price each paying guest paid on arrival."],
  sectionCosts: ["SkiResort.Hub.SECTION[Costs]", "Running costs"],
  sectionIncome: ["SkiResort.Hub.SECTION[Income]", "Income"],
  moneyRowRuns: ["SkiResort.Hub.MONEY_ROW[Runs]", "Runs"],
  moneyRowBus: ["SkiResort.Hub.MONEY_ROW[Bus]", "Ski bus"],
  moneyTipBusBooked: ["SkiResort.Hub.MONEY_TIP[BusBooked]", "Coach bill booked this month so far: 1,500 a month per depot plus 300 per straight-line km to the nearest hill lodge."],
  moneyTipRunsBooked: ["SkiResort.Hub.MONEY_TIP[RunsBooked]", "Run upkeep booked this month so far. Today's rate per month, by kind:"],
  moneyRowBuildings: ["SkiResort.Hub.MONEY_ROW[Buildings]", "Building upkeep"],
  moneyRowWages: ["SkiResort.Hub.MONEY_ROW[Wages]", "Staff wages"],
  /* the Staff wages tooltip's last line (OffSeasonRules, the Options setting Half staff off-season, default on) */
  moneyTipOffSeason: ["SkiResort.Hub.MONEY_TIP[OffSeason]", "With no run open, lodges and the ski school keep half their staff (Options)."],
  moneyNet: ["SkiResort.Hub.MONEY_NET", "Net so far"],
  moneyTipNet: ["SkiResort.Hub.MONEY_TIP[Net]", "All income booked this month minus all costs: the rows above add up to it. Buildings and wages: today's rate over the month so far."],
  sectionAlso: ["SkiResort.Hub.SECTION[Also]", "Other figures"],
  moneyNotePanel: ["SkiResort.Hub.MONEY_NOTE[Panel]", "This month so far. Income, lift costs and run costs: as booked. Buildings and wages: today's rate × the share of the month gone."],
  moneyNoteRwh: ["SkiResort.Hub.MONEY_NOTE[Rwh]", "Detected: Realistic Workplaces. The real bill can differ."],
  moneyTipRwh: ["SkiResort.Hub.MONEY_TIP[Rwh]", "Realistic Workplaces And Households changes staff costs. The real bill can differ."],
  moneyNoteEst: ["SkiResort.Hub.MONEY_NOTE[Est]", "≈ Some income or costs are not counted yet. They show as 0 in the net."],
  // the Money view's price line, tooltip lines without a placeholder
  moneyPriceTitle: ["SkiResort.Hub.MONEY_PRICE_TIP[Title]", "Lift ticket price"],
  moneyPriceTipWhere: ["SkiResort.Hub.MONEY_PRICE_TIP[Where]", "Options > Modular Ski Resort > Ski area balance. One price for every ski area."],
  moneyPriceTipWho: ["SkiResort.Hub.MONEY_PRICE_TIP[Who]", "Only ski days react to it. Other visitors and the attraction score ignore it."],
  // the lodge-spend income row (drawn only while lodgeSpendMonth > 0, hubRules.lodgeSpendShown)
  moneyRowLodgeSpend: ["SkiResort.Hub.MONEY_ROW[LodgeSpend]", "Lodge spend"],
  moneyRowPassesMonth: ["SkiResort.Hub.MONEY_ROW[PassesMonth]", "Season passes sold"],
  moneyTipLodgeSpend: ["SkiResort.Hub.MONEY_TIP[LodgeSpend]", "Booked this month so far: what paying ski guests spend at the lodges. Per guest: Options > Modular Ski Resort > Ski area balance."],
  // rows drawn while passes are on or used (hubRules.passesShown)
  moneyTipPasses: ["SkiResort.Hub.MONEY_TIP[Passes]", "Sold once a season. Holders ski free all season and come more often. Share and price: Options > Modular Ski Resort > Ski area balance."],
  moneyTipPassHolders: ["SkiResort.Hub.MONEY_TIP[PassHolders]", "Guests who came in on a season pass: no lift ticket money now."],
  guestsRowPassHolders: ["SkiResort.Hub.GUESTS_ROW[PassHolders]", "Season pass"],
  guestsTipPassHolders: ["SkiResort.Hub.GUESTS_TIP[PassHolders]", "Residents who came in on a season pass. They paid once for the season and pay nothing at the gate. Tourists never hold one."],
  // lessons, the pre-season sale, the city-income estimate (hidden under Tourism Overhaul: EST_TO_NOTE / EST_TO_TIP)
  moneyTipLessons: ["SkiResort.Hub.MONEY_TIP[Lessons]", "Booked this month so far, one lesson per guest. Share and price: Options > Modular Ski Resort > Ski area balance."],
  moneyTipPassesPre: ["SkiResort.Hub.MONEY_TIP[PassesPre]", "The one sale before the season opens. It is part of the season's sales and counts in the net of the month of the sale."],
  moneyRowCityIncome: ["SkiResort.Hub.MONEY_ROW[CityIncome]", "City tourist income"],
  moneyTipCityIncome: ["SkiResort.Hub.MONEY_TIP[CityIncome]", "Estimate: the game's tourist income, times this ski area's share of the city's tourist draw. It goes to the city, outside this net."],
  guestsRowLessons: ["SkiResort.Hub.GUESTS_ROW[Lessons]", "Lessons"],
  guestsTipLessons: ["SkiResort.Hub.GUESTS_TIP[Lessons]", "Guests who took a lesson, one each."],
  unlinkedMoney: ["SkiResort.Hub.UNLINKED_MONEY", "Runs and lifts that are not linked earn nothing. Their upkeep is still charged."],

  buildEmpty: ["SkiResort.Hub.BUILD_EMPTY", "No building tools were reported. See SkiResortMod.Mod.log."],
  buildActive: ["SkiResort.Hub.BUILD_ACTIVE", "In use"],
  locked: ["SkiResort.Hub.LOCKED", "Not available yet"],
  buildHintSign: ["SkiResort.Hub.BUILD_HINT_SIGN", "Starts a ski area: lifts and runs within its radius join it."],
  buildHintLodge: ["SkiResort.Hub.BUILD_HINT_LODGE", "Ski-day guests only use lodges within 800 m of runs or stations."],
  buildShortSchool: ["SkiResort.Hub.BUILD_SHORT[School]", "Teaches on green runs."],
  buildShortDepot: ["SkiResort.Hub.BUILD_SHORT[Depot]", "Ski days start here, in town."],
  buildHintFence: ["SkiResort.Hub.BUILD_HINT_FENCE", "Decorative only."],
  buildShortPatrol: ["SkiResort.Hub.BUILD_SHORT[Patrol]", "Covers the steep runs nearby."],
  buildTipDepot: ["SkiResort.Hub.BUILD_TIP_DEPOT", "Coach bill: 1,500 a month plus 300 per straight-line km to the nearest hill lodge. It adds no skier room and no score."],
  buildTipSchool: ["SkiResort.Hub.BUILD_TIP_SCHOOL", "+12 on the Ski school row of the score while a <icon:green> run is <icon:openpatchy>. Residents go skiing 20 % more often."],
  buildTipPatrol: ["SkiResort.Hub.BUILD_TIP_PATROL", "Within 1 km, <icon:black> and <icon:dblack> runs score 15 % more. Runs there without a lift still score at least 70 %."],
  /* the A-frame, Chalet and Grand tiles (buildItems.ts ROAD_OPTIONAL_LODGES): LodgeSupplyRule, LiftServedRule.LinkMeters */
  buildTipRoadOptional: ["SkiResort.Hub.BUILD_TIP_ROAD_OPTIONAL", "Off-road, a cat track within 25 m that reaches a road lodge or Ski Bus Depot brings the utilities. No guests arrive there by road."],
  buildTipLuxury: ["SkiResort.Hub.BUILD_TIP_LUXURY", "Luxury lodge. Ski days stay full at a lift ticket price up to 40 higher."],
  buildTipOnce: ["SkiResort.Hub.BUILD_TIP_ONCE", "One-time"],
  buildTipMonth: ["SkiResort.Hub.BUILD_TIP_MONTH", "Per month"],
  buildMonthNone: ["SkiResort.Hub.BUILD_MONTH_NONE", "Nothing"],
  buildMonthStation: ["SkiResort.Hub.BUILD_MONTH_STATION", "The whole lift is priced by its length: both stations and the line. This station is one part."],
  buildTipReshape: ["SkiResort.Hub.BUILD_TIP_RESHAPE", "Reshaping a run later costs nothing more."],
  buildTipGroomedOff: ["SkiResort.Hub.BUILD_TIP_GROOMED_OFF", "Not groomed"],
  buildTipStepsOnce: ["SkiResort.Hub.BUILD_TIP_STEPS_ONCE", "Each feature charges a one-time fee per run the first time it is turned on."],
  // a building tile's draw rows (vanilla's labels, DRAW_LABEL) end with Workers. Then comes one terrain line from the prefab's
  // BuildingTerraformOverride (buildItems.ts BUILDING_DRAW.terrain: level / cut / slope)
  buildTipWorkers: ["SkiResort.Hub.BUILD_TIP_WORKERS", "Workers"],
  terrainLevel: ["SkiResort.Hub.TERRAIN[Level]", "Levels the ground"],
  terrainCut: ["SkiResort.Hub.TERRAIN[Cut]", "Cuts a level pad"],
  terrainSlope: ["SkiResort.Hub.TERRAIN[Slope]", "Sits on the slope"],

  tileHistory: ["SkiResort.Hub.TILE[History]", "History"],
  tileTipHistory: ["SkiResort.Hub.TILE_TIP[History]", "Guests and finances of every month this ski area has recorded."],
  historyScopeArea: ["SkiResort.Hub.HISTORY[ScopeArea]", "This ski area"],
  historyScopeCity: ["SkiResort.Hub.HISTORY[ScopeCity]", "Whole city"],
  historyTipScope: ["SkiResort.Hub.HISTORY_TIP[Scope]", "Whole city adds every ski area that exists now. A deleted one takes its history with it. No attraction line: each area has its own."],
  historyRange12: ["SkiResort.Hub.HISTORY[Range12]", "12 months"],
  historyRangeSeasons: ["SkiResort.Hub.HISTORY[RangeSeasons]", "This season vs last"],
  historyRangeBySeason: ["SkiResort.Hub.HISTORY[RangeBySeason]", "By season"],
  historyTipRange12: ["SkiResort.Hub.HISTORY_TIP[Range12]", "The last 12 months. The month in progress is the last bar."],
  historyTipRangeSeasons: ["SkiResort.Hub.HISTORY_TIP[RangeSeasons]", "This ski season in color, starting 5 months before winter. Last season's same months in gray behind it."],
  historyTipRangeBySeason: ["SkiResort.Hub.HISTORY_TIP[RangeBySeason]", "One bar per ski season, starting 5 months before winter: the average month, so a season with missing months compares fairly."],
  historyTipRangeOff: ["SkiResort.Hub.HISTORY_TIP[RangeOff]", "Not enough months recorded yet."],
  historyGuests: ["SkiResort.Hub.HISTORY[Guests]", "Guests"],
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
  historyTipChart: ["SkiResort.Hub.HISTORY_TIP[Chart]", "Faded bars are partial months. Hatched slots recorded nothing."],
  // the Resort Planner tool (code name Quick runs, in QuickRunsControls.tsx and BuildStrip.tsx QuickRunsChip)
  quickTool: ["SkiResort.Hub.QUICK_TOOL", "Resort Planner"],
  quickToolTip: ["SkiResort.Hub.QUICK_TOOL_TIP", "Outline a region and get a set of runs to pick from. Experimental. Ctrl+Shift+Q also opens it."],
  catTrackDelete: ["SkiResort.Hub.CAT_TRACK_DELETE", "Delete cat tracks"],
  catTrackDeleteTip: ["SkiResort.Hub.CAT_TRACK_DELETE_TIP", "Opens the cat track tool in delete mode. The ground stays graded, no refund. Ctrl+Shift+D in the tool."],
  quickUnavailable: ["SkiResort.Hub.QUICK_UNAVAILABLE", "The Resort Planner is not available right now."],
  quickScan: ["SkiResort.Hub.QUICK_SCAN", "Scan"],
  quickScanTip: ["SkiResort.Hub.QUICK_SCAN_TIP", "Scan the outlined region for runs. Enter does the same."],
  quickBuild: ["SkiResort.Hub.QUICK_BUILD", "Build"],
  quickBuildTip: ["SkiResort.Hub.QUICK_BUILD_TIP", "Builds the cat tracks and runs not grayed out. Lifts have their own button. Refused when the city cannot pay for runs and lifts together."],
  quickUndo: ["SkiResort.Hub.QUICK_UNDO", "Undo"],
  quickUndoTip: ["SkiResort.Hub.QUICK_UNDO_TIP", "Remove the last build's cat tracks and runs and refund what they cost. Only this button does it: right click never removes built runs."],
  quickDensityCaption: ["SkiResort.Hub.QUICK_DENSITY", "How many"],
  quickDensity0: ["SkiResort.Hub.QUICK_DENSITY[Sparse]", "Few"],
  quickDensity1: ["SkiResort.Hub.QUICK_DENSITY[Normal]", "Normal"],
  quickDensity2: ["SkiResort.Hub.QUICK_DENSITY[Dense]", "Many"],
  quickKindsCaption: ["SkiResort.Hub.QUICK_KINDS", "Features"],
  quickKindNatural: ["SkiResort.Hub.QUICK_KIND[Natural]", "Leave black runs natural"],
  quickKindSnowmaking: ["SkiResort.Hub.QUICK_KIND[Snowmaking]", "Snowmaking near the base"],
  quickKindNight: ["SkiResort.Hub.QUICK_KIND[Night]", "Light the two lowest"],
  quickKindNaturalTip: ["SkiResort.Hub.QUICK_KIND_TIP[Natural]", "Black and harder runs stay ungroomed: no grooming fee or upkeep. Every other run is groomed."],
  quickKindSnowmakingTip: ["SkiResort.Hub.QUICK_KIND_TIP[Snowmaking]", "Green runs that reach the base get snow guns."],
  quickKindNightTip: ["SkiResort.Hub.QUICK_KIND_TIP[Night]", "Up to two base runs, blue or easier with the lowest tops, get snow guns and lights."],
  quickStart: ["SkiResort.Hub.QUICK_START", "Click the corners of a region on the map, then the first corner again."],
  quickRegionOver: ["SkiResort.Hub.QUICK_REGION_OVER", "The region is larger than the limit shown above."],
  quickPreviewHint: ["SkiResort.Hub.QUICK_PREVIEW_HINT", "Click a run on the map to leave it out or take it back."],
  quickBuilding: ["SkiResort.Hub.QUICK_BUILDING", "Building the cat tracks, then the runs. A large region can take a few seconds to a minute."],
  quickStop: ["SkiResort.Hub.QUICK_STOP", "Stop"],
  quickStopTip: ["SkiResort.Hub.QUICK_STOP_TIP", "Stop after the part in progress. What is built or planted so far stays, and Undo removes it. Right click does the same."],
  quickTreesCaption: ["SkiResort.Hub.QUICK_TREES", "Trees"],
  quickTrees: ["SkiResort.Hub.QUICK_TREES_TOGGLE", "Tree filler"],
  quickTreesTip: ["SkiResort.Hub.QUICK_TREES_TIP", "Fill the outlined region with trees up to the tree line, around the runs."],
  quickPlant: ["SkiResort.Hub.QUICK_PLANT", "Plant"],
  quickPlantTip: ["SkiResort.Hub.QUICK_PLANT_TIP", "Plants a few thousand at a time, and removes trees a lower density or tree line no longer wants. Enter does the same."],
  quickTreesUndo: ["SkiResort.Hub.QUICK_TREES_UNDO", "Undo trees"],
  quickTreesUndoTip: ["SkiResort.Hub.QUICK_TREES_UNDO_TIP", "Remove the trees of the last planting. Right click never removes planted trees."],
  quickTreeLine: ["SkiResort.Hub.QUICK_TREE_LINE", "Tree line"],
  quickTreeLineTip: ["SkiResort.Hub.QUICK_TREE_LINE_TIP", "Trees grow up to this height. The default is where the wild trees nearby stop."],
  quickTreeLineNone: ["SkiResort.Hub.QUICK_TREE_LINE_NONE", "Above the slope"],
  quickLower: ["SkiResort.Hub.QUICK_LOWER", "Lower"],
  quickHigher: ["SkiResort.Hub.QUICK_HIGHER", "Higher"],
  quickGroup0: ["SkiResort.Hub.QUICK_GROUP[Conifer]", "Conifer"],
  quickGroup1: ["SkiResort.Hub.QUICK_GROUP[Deciduous]", "Deciduous"],
  quickGroup2: ["SkiResort.Hub.QUICK_GROUP[Palm]", "Palm"],
  quickGroup3: ["SkiResort.Hub.QUICK_GROUP[All]", "All trees"],
  quickSpeciesCaption: ["SkiResort.Hub.QUICK_SPECIES", "Species"],
  quickSpeciesGroup: ["SkiResort.Hub.QUICK_SPECIES_GROUP", "The whole group"],
  quickSpeciesTip: ["SkiResort.Hub.QUICK_SPECIES_TIP", "One step before the first species: the whole group."],
  quickTreeDensity: ["SkiResort.Hub.QUICK_TREE_DENSITY", "Density"],
  /* Resort Planner v2: the toggles' tips, the proposal note and the "plan around" hint (the formats are in HUB_FMT). The Lifts, Mix, Shuffle and Way home knobs are gone (round R2-b). */
  quickPodTip: ["SkiResort.Hub.QUICK_POD_TIP", "Click to leave these runs out or take them back."],
  quickLiftTip: ["SkiResort.Hub.QUICK_LIFT_TIP", "Click to leave this lift and its runs out or take them back."],
  quickProposalNote: ["SkiResort.Tooltip.QUICK_PROPOSAL_NOTE", "Build makes the cat tracks and runs. The lifts follow with their own button, Build lifts."],
  quickAroundLift: ["SkiResort.Tooltip.QUICK_AROUND_LIFT", "Click a lift station to plan runs around it"],
  quickLiftIdPinned: ["SkiResort.Tooltip.QUICK_LIFT_ID_PINNED", "Existing lift"],
  /* R1-d (contract 41): the plan sheet's lifts block, the texts without a placeholder (the formats are in HUB_FMT, the reasons in SENT_KEYS) */
  quickLiftsBuild: ["SkiResort.Hub.QUICK_LIFTS_BUILD", "Build lifts"],
  quickLiftsBuildTip: ["SkiResort.Hub.QUICK_LIFTS_BUILD_TIP", "Build every lift switched on and not built, lowest first. Unaffordable lifts are skipped, lines needing a hand fit wait, the rest still build."],
  quickLiftBuild: ["SkiResort.Hub.QUICK_LIFT_BUILD", "Build"],
  quickLiftBuildTip: ["SkiResort.Hub.QUICK_LIFT_BUILD_TIP", "Place both stations and build the line of this lift."],
  quickLiftsStopTip: ["SkiResort.Hub.QUICK_LIFTS_STOP_TIP", "Stop after the lift in progress. Lifts built so far stay."],
  quickLiftsClear: ["SkiResort.Hub.QUICK_LIFTS_CLEAR", "Clear list"],
  quickLiftsClearTip: ["SkiResort.Hub.QUICK_LIFTS_CLEAR_TIP", "Remove the lifts from this list. Stations and lines already built stay."],
  quickLiftProposed: ["SkiResort.Tooltip.QUICK_LIFT_STATE[Proposed]", "Not built yet"],
  quickLiftOff: ["SkiResort.Tooltip.QUICK_LIFT_STATE[Off]", "Switched off in the plan"],
  quickLiftQueued: ["SkiResort.Tooltip.QUICK_LIFT_STATE[Queued]", "Waiting for its turn"],
  quickLiftBuilding: ["SkiResort.Tooltip.QUICK_LIFT_STATE[Building]", "Building now: stations, then the line"],
  quickLiftPlaced: ["SkiResort.Tooltip.QUICK_LIFT_STATE[StationsPlaced]", "Stations placed, no line yet"],
  quickLiftBuilt: ["SkiResort.Tooltip.QUICK_LIFT_STATE[Built]", "Built"],
  quickLiftFlagged: ["SkiResort.Tooltip.QUICK_LIFT_FIT[Flagged]", "Builds, with one tower over its limit"],
  quickLiftWaits: ["SkiResort.Tooltip.QUICK_LIFT_FIT[Waits]", "Needs fitting by hand: the line waits in the lift tool"],
} as const;

/* Placeholder entries: only as <LocText entry={HUB_FMT.x} args={...} /> (gate R5, R13). */
export const HUB_FMT = {
  // folded per-lift band rows (hubRules.foldBandRows), the Lifts tile's power line, the list pager
  problemGroupLiftUnfinished: ["SkiResort.Hub.PROBLEM_GROUP[LiftUnfinished]", "Unfinished: {NAMES}"],
  problemGroupLiftNoPower: ["SkiResort.Hub.PROBLEM_GROUP[LiftNoPower]", "No power: {NAMES}"],
  problemGroupLiftSectionMissing: ["SkiResort.Hub.PROBLEM_GROUP[LiftSectionMissing]", "Section missing: {NAMES}"],
  tileTipPowered: ["SkiResort.Hub.TILE_TIP_POWERED", "Running with power: {N}"],
  pagePosition: ["SkiResort.Hub.PAGE_POSITION", "{N} / {COUNT}"],
  // a selector item's counts (hubRules.resortCountsVariant picks, and "one" is HUB_LOC.resortCountsOne)
  resortCounts: ["SkiResort.Hub.RESORT_COUNTS[Many]", "{RUNS} runs · {LODGES} lodges"],
  resortCountsRunOne: ["SkiResort.Hub.RESORT_COUNTS[RunOne]", "1 run · {LODGES} lodges"],
  resortCountsLodgeOne: ["SkiResort.Hub.RESORT_COUNTS[LodgeOne]", "{RUNS} runs · 1 lodge"],
  contractMismatchCore: ["SkiResort.Hub.CONTRACT_MISMATCH_CORE", "This panel (version {UI}) and the mod's DLL (version {DLL}) come from different builds. Building still works. The resort statistics stay hidden until the mod is reinstalled."],
  /* contract 39: the one warning of the merge card and the Mergeable buildings status line while no ski area has a run or a lift station ({M} = SlopesideMaxM) */
  mergeWarnNoGeometry: ["SkiResort.Hub.MERGE_WARN[NoGeometry]", "No runs or lifts yet. Merges need one within {M} m."],
  statusLine: ["SkiResort.Hub.STATUS_LINE", "{STATE}"],
  statusLineOne: ["SkiResort.Hub.STATUS_LINE_ONE", "{STATE}"],
  statusLineEst: ["SkiResort.Hub.STATUS_LINE_EST", "{STATE} ≈"],
  statusLineOneEst: ["SkiResort.Hub.STATUS_LINE_ONE_EST", "{STATE} ≈"],
  statusLinePartly: ["SkiResort.Hub.STATUS_LINE_PARTLY", "{STATE} · {PARTLY} patchy"],
  statusLinePartlyOne: ["SkiResort.Hub.STATUS_LINE_PARTLY_ONE", "{STATE} · {PARTLY} patchy"],
  statusLinePartlyEst: ["SkiResort.Hub.STATUS_LINE_PARTLY_EST", "{STATE} · ≈ {PARTLY} patchy"],
  statusLinePartlyOneEst: ["SkiResort.Hub.STATUS_LINE_PARTLY_ONE_EST", "{STATE} · ≈ {PARTLY} patchy"],
  statusTipCounts: ["SkiResort.Hub.STATUS_TIP_COUNTS", "Open {OPEN} · patchy {PARTLY} · closed {CLOSED}"],
  statusTipTemp: ["SkiResort.Hub.STATUS_TIP_TEMP", "Day average at the top of the highest run: {TEMP}"],
  /* the status tooltip line while powder > 0 ({PCT} = skiDaysPowderPct) */
  statusTipPowder: ["SkiResort.Hub.STATUS_TIP_POWDER", "Fresh snow at the top: powder {PCT}. More skiers come."],
  statusSnow: ["SkiResort.Hub.STATUS_SNOW", "{DEPTH} cm · {PCT}"],
  statusSnowIn: ["SkiResort.Hub.STATUS_SNOW_IN", "{DEPTH} in · {PCT}"], // US customary units
  problemLodgesSomeOff: ["SkiResort.Hub.PROBLEM[LodgesSomeOff]", "Lodges deactivated: {N}"],
  problemEfficiency: ["SkiResort.Hub.PROBLEM[Efficiency]", "Efficiency {PCT}: {REASON}"],
  problemNotRunning: ["SkiResort.Hub.PROBLEM[NotRunning]", "Welcome Sign stopped: {REASON}"],
  problemLiftNoPower: ["SkiResort.Hub.PROBLEM[LiftNoPower]", "{NAME}: no power"],
  problemLiftSectionMissing: ["SkiResort.Hub.PROBLEM[LiftSectionMissing]", "{NAME}: section missing"],
  problemLiftUnfinished: ["SkiResort.Hub.PROBLEM[LiftUnfinished]", "{NAME}: unfinished"],
  problemSkiDaysNoGuestsNear: ["SkiResort.Hub.PROBLEM[SkiDaysNoGuestsNear]", "Nearest guests: {KM} km"],
  problemStationsPair: ["SkiResort.Hub.PROBLEM[StationsPair]", "{NAME}: no lift line yet"],
  problemStationsBaseOnly: ["SkiResort.Hub.PROBLEM[StationsBaseOnly]", "{NAME}: no top station yet"],
  problemStationsTopOnly: ["SkiResort.Hub.PROBLEM[StationsTopOnly]", "{NAME}: no base station yet"],
  problemLiftChairsStopped: ["SkiResort.Hub.PROBLEM[LiftChairsStopped]", "{NAME}: chairs stopped"],
  problemMixedLiftTypes: ["SkiResort.Hub.PROBLEM[MixedLiftTypes]", "{NAME} mixes lift types"],
  /* {DEG} = the bend in tenths shown with one decimal (detail / 10), {N} = the tower number (count) */
  problemLiftBend: ["SkiResort.Hub.PROBLEM[LiftBend]", "{NAME}: rope turns {DEG}° at tower {N}"],
  /* {N} = the number of the tower whose rope goes furthest past what its sheave train follows (count) */
  problemLiftTrainLimit: ["SkiResort.Hub.PROBLEM[LiftTrainLimit]", "{NAME}: tower {N}'s wheels cannot follow the rope"],
  /* {CARRIERS} = CARRIERS[Chairs] or [Cabins], {M} = their height above the ground in tenths shown with one decimal (detail / 10). The
     tip names the span (a 1080 band row fits about 49 characters) */
  problemLiftLowSpan: ["SkiResort.Hub.PROBLEM[LiftLowSpan]", "{NAME}: {CARRIERS} pass {M} m above the ground"],
  /* the same at or below the ground (detail 0) */
  problemLiftLowSpanGround: ["SkiResort.Hub.PROBLEM[LiftLowSpanGround]", "{NAME}: {CARRIERS} touch the ground"],
  /* C# HubProblemCode LiftLowSpan (LiftClearanceSystem: the planner's floor, LiftTowerPlanConstants FloorM). Shown only. The lift is
     unchanged. {WHERE} = LOW_SPAN[Towers], [Station] or [Stations] */
  problemTipLiftLowSpan: ["SkiResort.Hub.PROBLEM_TIP[LiftLowSpan]", "{WHERE}. Planned lines keep chairs and cabins 3 m clear, tram cabins 5 m. The lift tool replans a section you redraw."],
  /* {A}, {B} = the tower numbers at the span's ends (count, more) */
  lowSpanTowers: ["SkiResort.Hub.LOW_SPAN[Towers]", "Between towers {A} and {B}"],
  /* {N} = the span's one tower (count or more: the other end is a station) */
  lowSpanStation: ["SkiResort.Hub.LOW_SPAN[Station]", "Between tower {N} and the station"],
  /* the parking row when its lodge is one of the area's car space rows (lodgeParking$, hubRules.parkingLodgeOf). {NAME} = the
     player's name for it, else its LODGE_BUILDING word or LODGE_BUILDING_NUMBERED. {N} = the other lodges without parking */
  problemNoParkingAt: ["SkiResort.Hub.PROBLEM[NoParkingAt]", "No parking: {NAME}"],
  problemNoParkingAtMore: ["SkiResort.Hub.PROBLEM[NoParkingAtMore]", "No parking: {NAME} and {N} more"],
  problemSummitLodgeNotLiftServed: ["SkiResort.Hub.PROBLEM[SummitLodgeNotLiftServed]", "Summit lodges unreached: {N}"],
  problemRunsUnserved: ["SkiResort.Hub.PROBLEM[RunsUnserved]", "Runs without a lift: {N}"],
  problemRunsUnrateable: ["SkiResort.Hub.PROBLEM[RunsUnrateable]", "Runs that cannot be rated: {N}"],
  problemRunsRating: ["SkiResort.Hub.PROBLEM[RunsRating]", "Runs being rated: {N}"],
  problemRunsFlat: ["SkiResort.Hub.PROBLEM[RunsFlat]", "Runs too flat to score: {N}"],
  problemFreeLodges: ["SkiResort.Hub.PROBLEM[FreeLodges]", "Lodges in no ski area: {N}"],
  problemSchoolsNotLinked: ["SkiResort.Hub.PROBLEM[SchoolsNotLinked]", "Ski schools in no ski area: {N}"],
  problemMore: ["SkiResort.Hub.PROBLEM_MORE", "{N} more problems"],
  problemMoreOne: ["SkiResort.Hub.PROBLEM_MORE_ONE", "1 more problem"],
  problemTipNoLift: ["SkiResort.Hub.PROBLEM_TIP[NoLift]", "A run scores {PCT} until a running lift reaches its top: within 150 m of the line, at most 15 m above the top station."],
  problemTipRunsUnserved: ["SkiResort.Hub.PROBLEM_TIP[RunsUnserved]", "Each scores {PCT} until a running lift reaches its top: within 150 m of the line, at most 15 m above the top station."],
  problemGainEfficiency: ["SkiResort.Hub.PROBLEM_GAIN[Efficiency]", "At full efficiency the attraction would be {FULL}."],
  problemGainLodge: ["SkiResort.Hub.PROBLEM_GAIN[Lodge]", "With a lodge, the attraction would be {A}."],
  problemGainLodgesOn: ["SkiResort.Hub.PROBLEM_GAIN[LodgesOn]", "With them active, the attraction would be {A}."],
  staffLine: ["SkiResort.Hub.STAFF_LINE", "Staff: {FILLED} of {JOBS}"],
  tileTipFull: ["SkiResort.Hub.TILE_TIP_FULL", "At full efficiency: {N}"],
  tileTipStaffing: ["SkiResort.Hub.TILE_TIP_STAFFING", "Efficiency now: {PCT}"],
  tileTipPartly: ["SkiResort.Hub.TILE_TIP_PARTLY", "Patchy: {N}"],
  tileTipUnfinished: ["SkiResort.Hub.TILE_TIP_UNFINISHED", "Not complete yet: {N}"],
  tileTipLast: ["SkiResort.Hub.TILE_TIP_LAST", "{MONTH}: {N}"],
  // {MONTH} = the month's name (monthName(city.month))
  tileTipGuests: ["SkiResort.Hub.TILE_TIP[Guests]", "{MONTH}: tourists who arrived at the lodges or the Welcome Sign, and residents who visited a lodge."],
  scoreBarTip: ["SkiResort.Hub.SCORE_BAR_TIP", "{NOW} now, {FULL} at full efficiency, out of 200."],
  receiptTipLodges: ["SkiResort.Hub.RECEIPT_TIP_LODGES", "Active lodges: {N}"],
  receiptTipTerrain: ["SkiResort.Hub.RECEIPT_TIP[Terrain]", "Each extra point adds a little less: four times the points gives twice this value. {K} is the Attraction scale in Options."],
  receiptParts: ["SkiResort.Hub.RECEIPT_PARTS", "(runs {RUNS} + lifts {LIFTS}) × variety {VARIETY} × vertical {VERTICAL}"],
  /* RECEIPT_PARTS with the school term, while ResortLive.schoolValue > 0 (hubRules.receiptPartsVariant) */
  receiptPartsSchool: ["SkiResort.Hub.RECEIPT_PARTS_SCHOOL", "(runs {RUNS} + lifts {LIFTS} + school {SCHOOL}) × variety {VARIETY} × vertical {VERTICAL}"],
  /*
   * the second line under Runs and lifts. The row's value is {K} x the square root of the parts line (HubRules.Terrain = K x
   * sqrt(S), S = (runs + lifts + school) x variety x vertical, see SkiResortScore). {TERRAIN} = ResortLive.terrain. The UI
   * multiplies nothing.
   */
  receiptRoot: ["SkiResort.Hub.RECEIPT_ROOT", "{K} × the square root of the line above = {TERRAIN}"],
  /* the lodge-base tooltip's best tier ({NAME} = its LODGE_TIER word: an own lodge or a merged building) and the others' extras ({E}) */
  receiptTipLodgeBest: ["SkiResort.Hub.RECEIPT_TIP_LODGE_BEST", "Best tier: {NAME}. The others add {E}."],
  receiptTipVertical: ["SkiResort.Hub.RECEIPT_TIP[Vertical]", "Vertical bonus: +1 % per 6 m, so 600 m doubles runs, lifts and school. From the lowest to the highest <icon:openpatchy> run: {M}."],
  /* the missing lines' parts with a number (contract 40) */
  runsMissingClosed: ["SkiResort.Hub.RECEIPT_MISSING[Runs.Closed]", "{N} <icon:closed>"],
  runsMissingPatchy: ["SkiResort.Hub.RECEIPT_MISSING[Runs.Patchy]", "{N} <icon:patchy>"],
  runsMissingNoLift: ["SkiResort.Hub.RECEIPT_MISSING[Runs.NoLift]", "{N} no <icon:lift>"],
  liftsMissingNoPower: ["SkiResort.Hub.RECEIPT_MISSING[Lifts.NoPower]", "{N} without <icon:power>"],
  liftsMissingUnfinished: ["SkiResort.Hub.RECEIPT_MISSING[Lifts.Unfinished]", "{N} unfinished"],
  receiptMissingVertical: ["SkiResort.Hub.RECEIPT_MISSING[Vertical]", "To the full bonus: {M}"],
  receiptMissingLodges: ["SkiResort.Hub.RECEIPT_MISSING[LodgeBase]", "Other lodges: {E} of 24"],
  receiptOpen: ["SkiResort.Hub.RECEIPT_OPEN", "Open now: {PCT}"],
  receiptTipOpen: ["SkiResort.Hub.RECEIPT_TIP[Open]", "Closed runs add nothing and patchy runs add part of their value. With every run fully open, runs would add {FULL}."],
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
  runNotServed: ["SkiResort.Hub.RUN_NOT_SERVED", "No lift to its top: counts {PCT}"],
  runValueOf: ["SkiResort.Hub.RUN_VALUE_OF", "{NOW} of {FULL}"],
  runValueParts: ["SkiResort.Hub.RUN_VALUE_PARTS", "difficulty {B} × size {S} × type {T} × lift {L} × open {O}"],
  liftDetail: ["SkiResort.Hub.LIFT_DETAIL", "{CAP} skiers/h · {N} runs"],
  liftDetailOne: ["SkiResort.Hub.LIFT_DETAIL_ONE", "{CAP} skiers/h · 1 run"],
  liftDetailNone: ["SkiResort.Hub.LIFT_DETAIL_NONE", "{CAP} skiers/h · no runs"],
  liftRideMin: ["SkiResort.Hub.LIFT_RIDE_MIN", "{MIN} min"],
  // LiftRow.seats / speedMps per lift type. Both texts hold for the tram too
  liftRideTip: ["SkiResort.Hub.LIFT_RIDE_TIP", "Base to top at {SPEED} m/s line speed, at 1× game speed."],
  // an unpowered lift counts none
  liftCapacityTip: ["SkiResort.Hub.LIFT_CAPACITY_TIP", "{SEATS} seats per chair or cabin at {SPEED} m/s. Tram: time at stops included. None without power or an open run. Riders are not simulated."],
  capacityValue: ["SkiResort.Hub.CAPACITY_VALUE", "{CAP} skiers/h"],
  // The Guests view names a month bare, "{MONTH}" (bars, column heads and their tooltips). {MONTH} = monthName(city.month), last month's = city.month - 1.
  guestsMonthThis: ["SkiResort.Hub.GUESTS_MONTH[This]", "{MONTH}"],
  /* one row per active road lodge of the area (own lodges only: a merged building has no parking row), {NAME} = its LODGE_BUILDING word */
  guestsRowCarSpaces: ["SkiResort.Hub.GUESTS_ROW_FMT[CarSpaces]", "{NAME}"],
  /* {NAME} = a LODGE_BUILDING word, {N} = the lodge's number in its area (LodgeParkingRow.number > 0: two or more of that building there) */
  lodgeBuildingNumbered: ["SkiResort.Hub.LODGE_BUILDING_NUMBERED", "{NAME} {N}"],
  guestsMonthLast: ["SkiResort.Hub.GUESTS_MONTH[Last]", "{MONTH}"],
  /* bar 2's label while last month was counted only since the load (hubRules.lastMonthMark) */
  guestsMonthLastPartial: ["SkiResort.Hub.GUESTS_MONTH[LastPartial]", "{MONTH} (partial)"],
  guestsTipPaid: ["SkiResort.Hub.GUESTS_TIP[Paid]", "{MONTH}: {GUESTS} guests, {PAID} paid, {HOLDERS} on a pass, {UNPAID} could not pay, {FREE} free while tickets were off or closed."],
  ticketsNoteOpen: ["SkiResort.Hub.TICKETS_NOTE[Open]", "Each guest without a season pass pays {PRICE} on arrival. The price is set in Options."],
  /* both guest bars' tooltip, {MAX} = hubRules.guestBarScale */
  guestsBarScale: ["SkiResort.Hub.GUESTS_BAR_SCALE", "Full bar: {MAX} guests"],
  /* {N} = skiDaysCap, the capacity at once */
  skiDaysRoomRuns: ["SkiResort.Hub.SKI_DAYS[RoomRuns]", "Room for {N}, runs limit it"],
  skiDaysRoomLodges: ["SkiResort.Hub.SKI_DAYS[RoomLodges]", "Room for {N}, lodges limit it"],
  skiDaysRoom: ["SkiResort.Hub.SKI_DAYS[Room]", "Room for {N}"],
  /* The Ski days row's tooltip: the three classes partition {SENT} ("close by" = the walk class). */
  skiDaysTipSent: ["SkiResort.Hub.SKI_DAYS_TIP[Sent]", "{MONTH}: {SENT} sent, {ARRIVED} reached a lodge"],
  /* the split of the sent skiers ("close by" = the walk class, sent - car - no car) */
  skiDaysTipSplit: ["SkiResort.Hub.SKI_DAYS_TIP[Split]", "Close by {WALK} · by car {CAR} · far, no car {FAR}"],
  skiDaysTipNow: ["SkiResort.Hub.SKI_DAYS_TIP[Now]", "On the hill or on the way now: {NOW} of {CAP}"],
  /* {NAME} = CityStats.tripPlanner, the known mod's name (hubRules.tripPlannerNote "known" / "knownOff") */
  tripPlannerNote: ["SkiResort.Hub.TRIP_PLANNER_NOTE[Known]", "Detected: {NAME}. It replaces the game's trip planner, so trip times can differ."],
  tripPlannerNoteOff: ["SkiResort.Hub.TRIP_PLANNER_NOTE[KnownOff]", "Ski days off: {NAME} replaced the game's leisure systems."],
  moneyTicketsLast: ["SkiResort.Hub.MONEY_TICKETS[Last]", "Lift tickets, {MONTH}: {N} paid"],
  moneyColMonth: ["SkiResort.Hub.MONEY_COL_MONTH", "{MONTH} so far"],
  moneyColMonthPartial: ["SkiResort.Hub.MONEY_COL_MONTH_PARTIAL", "{MONTH} so far, partial"],
  moneyTipPassesMonth: ["SkiResort.Hub.MONEY_TIP[PassesMonth]", "Sold this month so far, in the net now. Of it, the pre-season sale: {PRE}."],
  moneyTipLiftsRate: ["SkiResort.Hub.MONEY_TIP[LiftsRate]", "Booked this month so far. At today's rate: {RATE} per month."],
  moneyTipSoFar: ["SkiResort.Hub.MONEY_TIP[SoFar]", "{RATE} per month at today's staff and budget. {SHARE} of the month so far. An estimate: it moves when staff or lodges change."],
  moneyRowPasses: ["SkiResort.Hub.MONEY_ROW[Passes]", "Passes this season · {N}"],
  moneyRowPassHolders: ["SkiResort.Hub.MONEY_ROW[PassHolders]", "Pass holders, {MONTH}"],
  /* {N} = the pre-season sale's passes (Integer) / the lessons taken this month (Integer) */
  moneyRowPassesPre: ["SkiResort.Hub.MONEY_ROW[PassesPre]", "Pre-season passes · {N}"],
  moneyRowLessons: ["SkiResort.Hub.MONEY_ROW[Lessons]", "Lessons · {N}"],
  moneyFactBus: ["SkiResort.Hub.MONEY_FACT[Bus]", "Depots {N} · guests by bus {GUESTS} · bill {RATE}"],
  moneyTicketsPrice: ["SkiResort.Hub.MONEY_TICKETS_PRICE", "Lift ticket price now: {PRICE}"],
  /* {PRICE} = hubRules.hubPrice (Money), {PCT} = hubRules.priceFactorPct (Percentage) */
  moneyPriceLine: ["SkiResort.Hub.MONEY_PRICE_LINE", "Ticket {PRICE} · demand {PCT}"],
  /* {FULL} = PRICE_NO_EFFECT_MAX, {P1..P3} = PRICE_EXAMPLES (Money), {F1..F3} = their priceFactorPct (Percentage) */
  moneyPriceTipCurve: ["SkiResort.Hub.MONEY_PRICE_TIP[Curve]", "Ski days: as many at any price up to {FULL}, then fewer: {F1} as many at {P1}, {F2} at {P2}, {F3} at {P3}."],
  /* {FULL} = 40 + priceTolerance (not "Luxury lodges": the Chalet has 20) */
  moneyPriceTipTolerance: ["SkiResort.Hub.MONEY_PRICE_TIP[Tolerance]", "This ski area's lodges keep ski days full up to {FULL}."],
  moneyRowNatural: ["SkiResort.Hub.MONEY_ROW[Natural]", "Natural runs · {HA} ha"],
  moneyRowGrooming: ["SkiResort.Hub.MONEY_ROW[Grooming]", "Grooming · {HA} ha with snow"],
  moneyRowSnowmaking: ["SkiResort.Hub.MONEY_ROW[Snowmaking]", "Snowmaking · {HA} ha where the guns run"],
  moneyRowLights: ["SkiResort.Hub.MONEY_ROW[Lights]", "Lights · {HA} ha with snow"],
  moneyRowCatTracks: ["SkiResort.Hub.MONEY_ROW[CatTracks]", "Cat tracks · {HA} ha with guns on"],
  moneyRowLifts: ["SkiResort.Hub.MONEY_ROW[Lifts]", "Lifts · {N} · {LEN}"],
  // an unpowered lift still pays in full. The two remaining reductions are unfinished and no open run
  // (SkiMoneyRules.LiftUpkeepBillingOf)
  moneyTipLifts: ["SkiResort.Hub.MONEY_TIP[Lifts]", "{BASE} a month + {PERKM} per km per lift, in full even without power. A quarter while unfinished or no run is open (a tram half). Full {FULL}, reduced {REDUCED}."],
  /*
   * MONEY_TIP[Lifts] when two or more lift types are offered (each has its own rates, and one MONEY_TIP_LIFT_RATE line per type
   * follows)
   */
  moneyTipLiftsTypes: ["SkiResort.Hub.MONEY_TIP[LiftsTypes]", "Each lift: its type's base a month + a rate per km, in full even without power. A quarter while unfinished or no run is open (a tram half). Full {FULL}, reduced {REDUCED}."],
  moneyTipLiftRate: ["SkiResort.Hub.MONEY_TIP_LIFT_RATE", "{TYPE}: {BASE} per month + {PERKM} per km"],
  moneyTipBuildings: ["SkiResort.Hub.MONEY_TIP[Buildings]", "Upkeep {UPKEEP} · wages {WAGES} for {FILLED} of {JOBS} staff, at the Parks & Recreation budget."],
  buildLineRun: ["SkiResort.Hub.BUILD_LINE_RUN", "{NAME} · {FEE} + {PERHA} per ha"],
  buildLineCost: ["SkiResort.Hub.BUILD_LINE_COST", "{NAME} · {COST}"],
  buildLineLift: ["SkiResort.Hub.BUILD_LINE_LIFT", "{NAME} · about {PERKM} per km of line"],
  buildOnceRun: ["SkiResort.Hub.BUILD_ONCE_RUN", "{FEE} + {PERHA} per ha"],
  buildOnceLift: ["SkiResort.Hub.BUILD_ONCE_LIFT", "about {PERKM} per km of line"],
  buildMonthRun: ["SkiResort.Hub.BUILD_MONTH_RUN", "{RATE} per ha with snow"],
  buildStepAdd: ["SkiResort.Hub.BUILD_STEP_ADD", "+{FEE} + {PERHA} per ha, +{RATE} per ha a month"],
  buildStepNatural: ["SkiResort.Hub.BUILD_STEP_NATURAL", "{FEE} + {PERHA} per ha, nothing a month"],
  buildMonthLift: ["SkiResort.Hub.BUILD_MONTH_LIFT", "{BASE} per complete lift + {PERKM} per km of line"],
  follow: ["SkiResort.Hub.FOLLOW", "Now showing {NAME}."],
  // {MONTH} = monthName, {YEAR} = the game year as plain digits, {FROM}/{TO} = a season's years
  historyMonthYear: ["SkiResort.Hub.HISTORY_MONTH", "{MONTH} {YEAR}"],
  historySeason: ["SkiResort.Hub.HISTORY_SEASON", "Ski season {FROM}/{TO}"],
  historySeasonYear: ["SkiResort.Hub.HISTORY_SEASON_YEAR", "Ski season {YEAR}"],
  historyHeadGuests: ["SkiResort.Hub.HISTORY_HEAD[Guests]", "Guests, {MONTH}"],
  historyHeadNet: ["SkiResort.Hub.HISTORY_HEAD[Net]", "Net, {MONTH}"],
  historyTipBefore: ["SkiResort.Hub.HISTORY_TIP[Before]", "{MONTH} {YEAR}: {N}"],
  historySeasonMonths: ["SkiResort.Hub.HISTORY_SEASON_MONTHS", "{N} of 12 months · the average month"],
  historyVs: ["SkiResort.Hub.HISTORY_VS", "vs {MONTH} {YEAR}"],
  // the Quick runs tool (QuickRunsControls.tsx). The MIX / CLUSTER / POD templates are also the tool tooltip's
  quickRegion: ["SkiResort.Hub.QUICK_REGION_LINE", "{N} corners · {AREA} of {MAX} · vertical between the corners {V}"],
  quickScanning: ["SkiResort.Hub.QUICK_SCANNING_LINE", "Scanning the region: {P}"],
  quickRejects: ["SkiResort.Hub.QUICK_REJECTS_LINE", "{N} lines dropped: {LIST}."],
  quickTotals: ["SkiResort.Hub.QUICK_TOTALS_LINE", "{N} of {M} runs · cat tracks {T} · {FEE} · {SKIERS} skiers a day"],
  quickBatch: ["SkiResort.Hub.QUICK_BATCH_LINE", "Last build: {N} runs and cat tracks. Undo removes them and refunds the fee."],
  /** QUICK_WARN[NoSignSome] with its numbers (the warnings list holds only the key; N = quickRuns.unbound, M = quickRuns.enabled). */
  quickNoSignSome: ["SkiResort.Tooltip.QUICK_WARN[NoSignSome]", "{N} of {M} runs have no Welcome Sign in range: they will not count until one is placed nearby."],
  quickTreeCounts: ["SkiResort.Hub.QUICK_TREE_COUNTS", "{P} to plant · {R} to remove"],
  quickTreeCountsCost: ["SkiResort.Hub.QUICK_TREE_COUNTS_COST", "{P} to plant · {R} to remove · costs {FEE}"],
  quickMixLine: ["SkiResort.Tooltip.QUICK_MIX", "Green {G} of {TG} · Blue {B} of {TB} · Black {K} of {TK} · Double black {D} of {TD}"],
  quickMixCounts: ["SkiResort.Tooltip.QUICK_MIX_COUNTS", "Green {G} · Blue {B} · Black {K} · Double black {D}"],
  quickTracksN: ["SkiResort.Tooltip.QUICK_TRACKS_N", "Cat tracks: {N}"],
  quickCluster: ["SkiResort.Tooltip.QUICK_CLUSTER", "Runs this lift serves: {N}"],
  quickPodBeginner: ["SkiResort.Tooltip.QUICK_POD[Beginner]", "Beginner lift here: {N} runs"],
  quickPodMid: ["SkiResort.Tooltip.QUICK_POD[MidStation]", "Mid-station here: {N} runs"],
  // Resort Planner: the plan and lift lines (the tooltip's too). {TYPE} = HUB_LOC lift type, {CLASS} / {VERDICT} = SENT_KEYS
  quickPlanLine: ["SkiResort.Tooltip.QUICK_PLAN_LINE", "Planned: {LIFTS} lift(s), {KM} km of runs, {TKM} km of cat tracks"],
  quickMixHonest: ["SkiResort.Tooltip.QUICK_MIX_HONEST", "The mountain gives green {G} %, blue {B} %, black {K} %"],
  quickLiftLine: ["SkiResort.Tooltip.QUICK_LIFT_LINE", "{ID}: {TYPE}, {LENGTH} km, {RISE} m up, reaches {RUNS} run(s)"],
  quickOtherLine: ["SkiResort.Tooltip.QUICK_OTHER_LINE", "Other runs: {RUNS} run(s), {TRACKS} cat track(s)"],
  quickSiteDetail: ["SkiResort.Tooltip.QUICK_SITE_DETAIL", "Pods with their way home: {G} of {PODS}. Largest bench: {HA} ha"],
  quickBuildProgress: ["SkiResort.Tooltip.QUICK_BUILDING_PROGRESS", "Building cat tracks and runs {N} / {M}"],
  quickPlantProgress: ["SkiResort.Tooltip.QUICK_PLANTING_PROGRESS", "Planting trees {N} / {M}"],
  quickRemoveProgress: ["SkiResort.Tooltip.QUICK_REMOVING_PROGRESS", "Removing trees {N} / {M}"],
  // R1-d (contract 41): the plan sheet's lifts total, progress, row line and the state formats ({WHY} = a QUICK_LIFT_WHY text from SENT_KEYS)
  quickLiftsTotal: ["SkiResort.Hub.QUICK_LIFTS_TOTAL", "{N} of {M} lifts to build · about {COST}"],
  quickLiftsAllBuilt: ["SkiResort.Hub.QUICK_LIFTS_ALL_BUILT", "All {N} lifts built."],
  quickBuildingLift: ["SkiResort.Tooltip.QUICK_BUILDING_LIFT", "Building lift {N} / {M}"],
  quickStoppingLift: ["SkiResort.Tooltip.QUICK_STOPPING_LIFT", "Stopping after lift {N} / {M}"],
  quickSheetLine: ["SkiResort.Tooltip.QUICK_SHEET_LINE", "{ID}: {TYPE}, {LENGTH} km, {RISE} m up, about {COST}"],
  quickLiftPlacedWhy: ["SkiResort.Tooltip.QUICK_LIFT_STATE[StationsPlacedWhy]", "Stations placed, no line: {WHY}"],
  quickLiftRefused: ["SkiResort.Tooltip.QUICK_LIFT_STATE[Refused]", "Not built: {WHY}"],
  quickLiftFitting: ["SkiResort.Tooltip.QUICK_LIFT_STATE[NeedsFitting]", "Waits for a hand fit: {WHY}"],
  quickLiftBuiltFlagged: ["SkiResort.Tooltip.QUICK_LIFT_STATE[BuiltFlagged]", "Built. Tower {TOWER} bends the rope {BEND} degrees, its limit is {CAP}."],
  quickLiftFlaggedAt: ["SkiResort.Tooltip.QUICK_LIFT_STATE[FlaggedAt]", "Builds flagged. Tower {TOWER} bends the rope {BEND} degrees, its limit is {CAP}."],
  quickLiftFittingAt: ["SkiResort.Tooltip.QUICK_LIFT_STATE[FittingAt]", "Waits for a hand fit. Tower {TOWER} bends the rope {BEND} degrees, its limit is {CAP}."],
  quickLiftFlagTip: ["SkiResort.Hub.QUICK_LIFT_FLAG_TIP", "Tower {TOWER} stands {ALONG} m from the base station. The rope bends more there than its wheels can follow."],
} as const;

/* STATUS_WORD by hubRules.statusWord. */
export const STATUS_WORD: Readonly<Record<StatusWord, LocEntry>> = {
  open: ["SkiResort.Hub.STATUS_WORD[Open]", "Open"],
  partly: ["SkiResort.Hub.STATUS_WORD[Partly]", "Partly open"],
  closed: ["SkiResort.Hub.STATUS_WORD[Closed]", "Closed"],
};

/* The Quick runs "lines dropped" reasons, index = QuickRunsState.rejects bucket (QuickRunsRules.RejectBuckets). Read after a count: "18 too short". */
export const QUICK_REJECT_TEXT: readonly LocEntry[] = [
  ["SkiResort.Hub.QUICK_REJECT[TooShort]", "too short"],
  ["SkiResort.Hub.QUICK_REJECT[Overlap]", "overlap other runs"],
  ["SkiResort.Hub.QUICK_REJECT[Edge]", "too close to the edge"],
  ["SkiResort.Hub.QUICK_REJECT[Blocked]", "blocked by other areas"],
  ["SkiResort.Hub.QUICK_REJECT[Narrow]", "too narrow"],
  ["SkiResort.Hub.QUICK_REJECT[Twisted]", "too twisted"],
  ["SkiResort.Hub.QUICK_REJECT[Flat]", "too flat"],
  ["SkiResort.Hub.QUICK_REJECT[Other]", "other reasons"],
];

/* The site verdict (contract 21), index = QuickRunsState.siteTier - 1 (QuickRunsRules.SiteTier Gentle .. Beyond). {N} = the mean grade in percent.
   The same text is the tool tooltip's (SkiResort.Tooltip.QUICK_SITE[*]). */
export const QUICK_SITE_TEXT: readonly LocEntry[] = [
  ["SkiResort.Tooltip.QUICK_SITE[Gentle]", "Gentle slope on average ({N} %): good for green and blue runs where it stays gentle."],
  ["SkiResort.Tooltip.QUICK_SITE[Steep]", "Steep slope (average {N} %): mostly blue and black runs. Greens need the gentlest parts."],
  ["SkiResort.Tooltip.QUICK_SITE[VerySteep]", "Very steep (average {N} %): mostly black runs, few or no greens. Gentler ground lower down suits beginners."],
  ["SkiResort.Tooltip.QUICK_SITE[Beyond]", "Steeper than any real ski area (average {N} %): expect black and double black runs only. Look for gentler ground lower down for beginners."],
];

/* SEASON_HINT words, index = SEASON_HINT value (0 None = no text). */
export const SEASON_HINT_TEXT: ReadonlyArray<LocEntry | null> = [
  null,
  ["SkiResort.Hub.SEASON_HINT[SnowSeason]", "Snow season"],
  ["SkiResort.Hub.SEASON_HINT[SnowGunsOnly]", "Snow guns only"],
  ["SkiResort.Hub.SEASON_HINT[Thawing]", "Thawing"],
  ["SkiResort.Hub.SEASON_HINT[WaitingForSnow]", "Waiting for snow"],
  ["SkiResort.Hub.SEASON_HINT[TooWarm]", "Too warm for snow"],
  ["SkiResort.Hub.SEASON_HINT[GatingOff]", "Snow gating off"],
];

/*
 * SEASON_TIP, index = SEASON_HINT value (None and GatingOff: none. STATUS_TIP_GATING says it). Each tip says what C#
 * HubRules.Season read: the 24-hour mean at the top for natural snow, and the live gun gates ("snow guns running now").
 */
export const SEASON_TIP_TEXT: ReadonlyArray<LocEntry | null> = [
  null,
  ["SkiResort.Hub.SEASON_TIP[SnowSeason]", "At or below freezing: natural snow can fall."],
  ["SkiResort.Hub.SEASON_TIP[SnowGunsOnly]", "Above freezing, but the snow guns are running."],
  ["SkiResort.Hub.SEASON_TIP[Thawing]", "Above freezing and no snow gun running. Runs stay open on the snow that is left."],
  ["SkiResort.Hub.SEASON_TIP[WaitingForSnow]", "Cold enough for snow or snow guns, but no run has enough snow yet."],
  ["SkiResort.Hub.SEASON_TIP[TooWarm]", "Too warm for snow guns. Runs reopen when it cools. With Snow & season gating off in Options, every run counts as open."],
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

/* A run's features as one name (sentence case, R3, DECISIONS 578): by RunKind for the draw tiles, runFeaturesEntry for a built run. */
export const RUN_TYPE: Readonly<Record<RunKind, LocEntry>> = {
  0: ["SkiResort.Hub.RUN_TYPE[Groomed]", "Groomed"],
  1: ["SkiResort.Hub.RUN_TYPE[Snowmaking]", "Groomed, snowmaking"],
  2: ["SkiResort.Hub.RUN_TYPE[Night]", "Groomed, snowmaking, lit"],
  3: ["SkiResort.Hub.RUN_TYPE[Natural]", "Natural"],
  4: ["SkiResort.Hub.RUN_TYPE[CatTrack]", "Cat track"],
};
const RUN_TYPE_LIT: LocEntry = ["SkiResort.Hub.RUN_TYPE[Lit]", "Groomed, lit"];

/* A built run's features from its kind and sliders. A piste of a groomed look is groomed. It is snowmaking while its guns are on, and lit
 * while its lights are on (RunRow gunSpacingM / lightSpacingM, 0 = off). */
export function runFeaturesEntry(kind: RunKind, gunSpacingM: number, lightSpacingM: number): LocEntry {
  if (kind === 3 || kind === 4) return RUN_TYPE[kind];
  const snow = gunSpacingM > 0, lit = lightSpacingM > 0;
  return snow ? RUN_TYPE[lit ? 2 : 1] : lit ? RUN_TYPE_LIT : RUN_TYPE[0];
}

/* Footer line 2 short hint of a run tile, by RunKind. */
export const BUILD_SHORT_RUN: Readonly<Record<RunKind, LocEntry>> = {
  0: ["SkiResort.Hub.BUILD_SHORT[Groomed]", "Snow laid whenever it freezes. Scores more on easy runs."],
  1: ["SkiResort.Hub.BUILD_SHORT[Snowmaking]", "Snow guns open it earlier and keep it open longer."],
  2: ["SkiResort.Hub.BUILD_SHORT[Night]", "Floodlit snowmaking run: scores 25 % more."],
  3: ["SkiResort.Hub.BUILD_SHORT[Natural]", "Natural snow only, no upkeep. Easy runs score less."],
  4: ["SkiResort.Hub.BUILD_SHORT[CatTrack]", "Graded as you draw it. Always green, counts a quarter."],
};

/*
 * Footer idle line by hubRules.nextVariant: one fact. The numbered tiles and the dot show what comes next (no "Next:" order). The sign
 * step's fact is BUILD_HINT_SIGN (no own key).
 */
export const NEXT_LINE1: Readonly<Record<NextVariant, LocEntry | null>> = {
  sign: HUB_LOC.buildHintSign,
  unlinked: ["SkiResort.Hub.NEXT[Unlinked]", "Runs and lifts link to the nearest Welcome Sign within its radius."],
  lodge: ["SkiResort.Hub.NEXT[Lodge]", "Ski-day guests need a lodge within 800 m of the runs."],
  lift: ["SkiResort.Hub.NEXT[Lift]", "Runs a lift serves count in full."],
  run: ["SkiResort.Hub.NEXT[Run]", "Natural runs have no upkeep. The others pay upkeep per hectare."],
  done: null,
};
/*
 * `Assets.NAME[<prefab>]` of the non-run build items. Runs use RUN_TYPE. The run assets are Title Case, so a run asset name
 * here would fail R4.
 */
export const ASSET_NAME: Readonly<Partial<Record<BuildItemId, LocEntry>>> = {
  sign: ["Assets.NAME[SkiResortGate]", "Ski Area Welcome Sign"],
  safetyFence: ["Assets.NAME[SkiSafetyFence]", "Safety Net Fence"],
  lodge: ["Assets.NAME[SkiAFrameDayLodge]", "A-Frame Day Lodge"],
  lodgeHut: ["Assets.NAME[SkiLodgeHut]", "Mountain Hut"],
  lodgeMid: ["Assets.NAME[SkiLodgeMid]", "Chalet Lodge"],
  lodgeSummit: ["Assets.NAME[SkiLodgeSummit]", "Swanky Summit Lodge"],
  lodgeGrand: ["Assets.NAME[SkiLodgeGrand]", "Swanky Grand Lodge"],
  busDepot: ["Assets.NAME[SkiBusDepot]", "Ski Bus Depot"],
  school: ["Assets.NAME[SkiSchool]", "Ski School"],
  patrolHut: ["Assets.NAME[SkiPatrolHut]", "Ski Patrol Hut"],
  patrolHQ: ["Assets.NAME[SkiPatrolHQ]", "Ski Patrol HQ"],
  lift: ["Assets.NAME[SkiChairliftHV]", "Fixed-Grip Triple Chairlift"],
  // Every station says its lift type and its end ("<lift type>: <end>"). The tram's are terminals, never "Tram Station". A
  // tile's footer line 1 is "{NAME} · {COST}", one nowrap ellipsis line (hub.module.scss .buildStatusLine). The longest name is
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
 * The lift-type picker's chips and the Lifts view's type line, by LiftRow.type (LIFT_TYPE_IDS, an unknown id draws no word).
 * Sentence case like RUN_TYPE.
 */
export const LIFT_TYPE: Readonly<Record<string, LocEntry>> = {
  fixedGrip: ["SkiResort.Hub.LIFT_TYPE[fixedGrip]", "Fixed-grip"],
  detachable: ["SkiResort.Hub.LIFT_TYPE[detachable]", "Detachable"],
  gondola: ["SkiResort.Hub.LIFT_TYPE[gondola]", "Gondola"],
  tram: ["SkiResort.Hub.LIFT_TYPE[tram]", "Tram"],
};

/* Our own lodge BUILDINGS by short name, by build item id: the Buildings tab's lodge chips and tiles, and the rows of the area's own
   lodges (car spaces, "No parking: Chalet 2"). A merged city building is never named this way (DECISIONS 621). */
export const LODGE_BUILDING: Readonly<Partial<Record<BuildItemId, LocEntry>>> = {
  lodge: ["SkiResort.Hub.LODGE_BUILDING[lodge]", "A-frame"],
  lodgeHut: ["SkiResort.Hub.LODGE_BUILDING[lodgeHut]", "Hut"],
  lodgeMid: ["SkiResort.Hub.LODGE_BUILDING[lodgeMid]", "Chalet"],
  lodgeSummit: ["SkiResort.Hub.LODGE_BUILDING[lodgeSummit]", "Summit"],
  lodgeGrand: ["SkiResort.Hub.LODGE_BUILDING[lodgeGrand]", "Grand"],
};

/* The lodge TIERS by role name (DECISIONS 621), keyed by the tier's build item id (SkiCatalog.Lodges order: day lodge, warming hut,
   base lodge, mountain restaurant, flagship lodge). A tier is what a lodge does for the score. A merged building takes a tier, so
   the merge card's picker, the Merged buildings fold and the receipt's best tier use these words, never a building's name. */
export const LODGE_TIER: Readonly<Partial<Record<BuildItemId, LocEntry>>> = {
  lodge: ["SkiResort.Hub.LODGE_TIER[lodge]", "Day lodge"],
  lodgeHut: ["SkiResort.Hub.LODGE_TIER[lodgeHut]", "Warming hut"],
  lodgeMid: ["SkiResort.Hub.LODGE_TIER[lodgeMid]", "Base lodge"],
  lodgeSummit: ["SkiResort.Hub.LODGE_TIER[lodgeSummit]", "Mountain restaurant"],
  lodgeGrand: ["SkiResort.Hub.LODGE_TIER[lodgeGrand]", "Flagship lodge"],
};

/* The Build strip's three tabs (hubRules.BuildTab) and the tiles' visible labels. */
export const BUILD_TAB: Readonly<Record<string, LocEntry>> = {
  lifts: ["SkiResort.Hub.BUILD_TAB[Lifts]", "Lifts"],
  runs: ["SkiResort.Hub.BUILD_TAB[Runs]", "Runs"],
  buildings: ["SkiResort.Hub.BUILD_TAB[Buildings]", "Buildings"],
};
/* The Buildings tab's family chips (hubRules.BuildingFamily, DECISIONS 613). */
export const BUILD_FAMILY: Readonly<Record<string, LocEntry>> = {
  lodges: ["SkiResort.Hub.BUILD_GROUP[Lodges]", "Lodges"],
  services: ["SkiResort.Hub.BUILD_GROUP[Services]", "Services"],
};
/*
 * A tile's visible label by its role: base / top / line (every lift type), sign, lodge (the chosen tier), school, patrolHut,
 * patrolHQ, and the four run kinds. These are short, because the Runs tab already says "run". The tooltip and footer keep RUN_TYPE.
 */
export const BUILD_LABEL: Readonly<Record<string, LocEntry>> = {
  base: ["SkiResort.Hub.BUILD_LABEL[Base]", "Base station"],
  top: ["SkiResort.Hub.BUILD_LABEL[Top]", "Top station"],
  line: ["SkiResort.Hub.BUILD_LABEL[Line]", "Lift line"],
  sign: ["SkiResort.Hub.BUILD_LABEL[Sign]", "Sign"],
  fence: ["SkiResort.Hub.BUILD_LABEL[Fence]", "Fence"],
  lodge: ["SkiResort.Hub.BUILD_LABEL[Lodge]", "Lodge"],
  depot: ["SkiResort.Hub.BUILD_LABEL[Depot]", "Bus depot"],
  school: ["SkiResort.Hub.BUILD_LABEL[School]", "School"],
  patrolHut: ["SkiResort.Hub.BUILD_LABEL[PatrolHut]", "Patrol hut"],
  patrolHQ: ["SkiResort.Hub.BUILD_LABEL[PatrolHQ]", "Patrol HQ"],
  run3: ["SkiResort.Hub.BUILD_LABEL[Natural]", "Natural"],
  run0: ["SkiResort.Hub.BUILD_LABEL[Groomed]", "Run"],
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
  ["SkiResort.Hub.HINT[LiftNoPair]", "A lift line needs both stations: a base and a top station."],
  ["SkiResort.Hub.GLADE_REASON[Disabled]", "The tree tool hit an error and is off until the next load. Details are in the mod log."],
  ["SkiResort.Hub.GLADE_REASON[NotInGame]", "The tree tool works in a loaded city only."],
  ["SkiResort.Hub.GLADE_REASON[NoRuns]", "The ski run assets were not found: is the asset pack installed?"],
  ["SkiResort.Hub.GLADE_REASON[NoSpecies]", "No tree species to plant were found."],
  // the Quick runs tool: quickRuns.reason / status / warnings / notice
  ["SkiResort.Hub.QUICK_REASON[Off]", "Resort Planner is experimental and off. Its switch is in Options > Modular Ski Resort > Runs."],
  ["SkiResort.Hub.QUICK_REASON[NotArmed]", "Available once a city is loaded."],
  ["SkiResort.Hub.QUICK_REASON[Disabled]", "The Resort Planner hit an error and is off until the next load. Details are in the mod log."],
  ["SkiResort.Tooltip.QUICK_STATUS[BadInput]", "The region could not be read. Draw it again."],
  ["SkiResort.Tooltip.QUICK_STATUS[TooLarge]", "The region is over the size limit shown beside it, or its corners spread wider than about 5 km by 5 km."],
  ["SkiResort.Tooltip.QUICK_STATUS[TooSmall]", "The region is too small for a run."],
  ["SkiResort.Tooltip.QUICK_STATUS[TooNarrow]", "The region is too narrow: a run needs open slope about 70 m across, clear of other areas."],
  ["SkiResort.Tooltip.QUICK_STATUS[Blocked]", "Most of the region is taken by other areas or by land the city does not own."],
  ["SkiResort.Tooltip.QUICK_STATUS[NoVertical]", "The region has too little vertical for ski runs."],
  ["SkiResort.Tooltip.QUICK_STATUS[OutsideTiles]", "Part of this region is outside the city's map tiles, and no runs can go there."],
  ["SkiResort.Tooltip.QUICK_STATUS[AllCliff]", "The region is mostly cliff: no run can reach the bottom. Try a gentler slope."],
  ["SkiResort.Tooltip.QUICK_STATUS[TooFlat]", "The region is too flat for ski runs."],
  ["SkiResort.Tooltip.QUICK_STATUS[NoRuns]", "No run fits this region. Try another shape."],
  ["SkiResort.Tooltip.QUICK_STATUS[ScanFailed]", "The scan stopped on an internal error, and nothing was planned. The error and the scan input are in the mod log folder."],
  ["SkiResort.Tooltip.QUICK_WARN[FewRuns]", "Fewer than 2 runs fit here."],
  ["SkiResort.Tooltip.QUICK_WARN[NoGreen]", "No green runs: the slope is too steep for them, or the gentle part at the bottom is too short."],
  ["SkiResort.Tooltip.QUICK_WARN[NoBlue]", "No blue runs: the slope gives none."],
  ["SkiResort.Tooltip.QUICK_WARN[NoBlack]", "No black runs fit this slope."],
  ["SkiResort.Tooltip.QUICK_WARN[NoEasyWayHome]", "A lift top here has no green way down."],
  ["SkiResort.Tooltip.QUICK_WARN[OverWater]", "A run crosses water."],
  ["SkiResort.Tooltip.QUICK_WARN[NoFlatRunout]", "No gentle runout at the bottom of the slope."],
  ["SkiResort.Tooltip.QUICK_WARN[MixShort]", "Fewer runs of some colors than the planner aimed for."],
  ["SkiResort.Tooltip.QUICK_WARN[CrossesCatTrack]", "A run crosses a cat track."],
  ["SkiResort.Tooltip.QUICK_WARN[PartlyBlocked]", "Part of the region is blocked by other areas or by land the city does not own."],
  ["SkiResort.Tooltip.QUICK_WARN[MoneyShort]", "The city cannot pay the runs and lifts that are switched on: building is refused."],
  ["SkiResort.Tooltip.QUICK_WARN[Renumber]", "Runs with default names will be renumbered."],
  ["SkiResort.Tooltip.QUICK_WARN[OtherResort]", "These runs belong to more than one ski area."],
  ["SkiResort.Tooltip.QUICK_WARN[NoSign]", "No Welcome Sign in range: the runs will not count until one is placed nearby."],
  ["SkiResort.Tooltip.QUICK_WARN[ChecksOff]", "Anarchy is installed: while it is on, the game's own checks are off and only this tool's checks protect the build."],
  ["SkiResort.Tooltip.QUICK_NEED_CORNERS", "Click at least three corners first."],
  ["SkiResort.Tooltip.QUICK_NEED_SCAN", "Scan the region first."],
  ["SkiResort.Tooltip.QUICK_RESCANNED", "The map changed since the scan: scanned again. Check the preview and build again."],
  ["SkiResort.Tooltip.QUICK_NOTHING_ENABLED", "Every run is left out: click a run to take it back."],
  ["SkiResort.Tooltip.QUICK_DROPPED", "The game refused some runs: they are red and left out. Building the rest."],
  ["SkiResort.Tooltip.QUICK_REFUSED", "Nothing was built: the game refused the red runs. Build again to try the rest."],
  ["SkiResort.Tooltip.QUICK_BUILT", "Runs built."],
  ["SkiResort.Tooltip.QUICK_STOPPED", "Build stopped. The runs built so far stay. Undo removes them."],
  ["SkiResort.Tooltip.QUICK_PARTIAL", "Only some of the runs were built: the game refused the rest. Undo removes what was built."],
  ["SkiResort.Tooltip.QUICK_UNDONE", "The build was undone and its fee refunded."],
  ["SkiResort.Tooltip.QUICK_UNDONE_RESCAN", "The build was undone and its fee refunded. The region is scanned again for its plan."],
  ["SkiResort.Tooltip.QUICK_NOTHING_TO_UNDO", "Nothing to undo."],
  ["SkiResort.Tooltip.QUICK_RUNS_MISSING", "The ski run assets did not load: check that the mod is installed completely."],
  ["SkiResort.Tooltip.QUICK_TREES_OFF", "Turn the tree filler on first."],
  ["SkiResort.Tooltip.QUICK_TREES_COMPLETE", "Nothing to plant: the region already has this many trees."],
  ["SkiResort.Tooltip.QUICK_TREES_UNDONE", "The planted trees were removed."],
  ["SkiResort.Tooltip.QUICK_TREES_BLOCKED", "The game refuses some of these trees: planting stopped. Trees planted before stay."],
  ["SkiResort.Tooltip.QUICK_TREES_STOPPED", "Planting stopped. The trees planted so far stay. Undo trees removes them."],
  ["SkiResort.Tooltip.QUICK_TREES_PLANTED", "Trees planted."],
  ["SkiResort.Tooltip.QUICK_NO_SPECIES", "No tree species of this kind were found."],
  // Resort Planner v2: planLifts[].homeKey, pods[].homeKey / classKey, and the planner warnings
  ["SkiResort.Tooltip.QUICK_VERDICT[Green]", "Green way home"],
  ["SkiResort.Tooltip.QUICK_VERDICT[Blue]", "Blue way home"],
  ["SkiResort.Tooltip.QUICK_VERDICT[Black]", "Black only"],
  ["SkiResort.Tooltip.QUICK_VERDICT[DoubleBlack]", "Double Black only"],
  ["SkiResort.Tooltip.QUICK_VERDICT[Download]", "No way down on skis: ride the lift down"],
  ["SkiResort.Tooltip.QUICK_CLASS[Flat]", "Flat"],
  ["SkiResort.Tooltip.QUICK_CLASS[Marginal]", "Nearly flat"],
  ["SkiResort.Tooltip.QUICK_CLASS[Beginner]", "Beginner"],
  ["SkiResort.Tooltip.QUICK_CLASS[Intermediate]", "Intermediate"],
  ["SkiResort.Tooltip.QUICK_CLASS[Advanced]", "Advanced"],
  ["SkiResort.Tooltip.QUICK_CLASS[Expert]", "Expert"],
  ["SkiResort.Tooltip.QUICK_CLASS[Cliff]", "Cliff"],
  ["SkiResort.Tooltip.QUICK_WARN[NoLiftSite]", "No lift fits this region: the ground is too steep or too rough for a lift line, or has no flat spot for a station."],
  ["SkiResort.Tooltip.QUICK_WARN[Download]", "Some lift tops have no way down on skis, so guests ride the lift down."],
  ["SkiResort.Tooltip.QUICK_WARN[DeadEnd]", "A run ends where nothing continues: no lift, run, track or flat ground at its bottom."],
  ["SkiResort.Tooltip.QUICK_WARN[NoWayToBottom]", "Some lifts have no run or cat track to the bottom of the region: cliffs, basins, water, gaps or the region's edge block the way."],
  // R1-d (contract 41): the plan sheet's reasons (QuickRunsSheetRow.reasonKey) and the notices of a refused lift call (QuickRunsSheet.notice)
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[MONEY]", "the city cannot pay for it"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[POSE]", "the two spots are too close together"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[PREFAB]", "a station asset did not load"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[BASE_OVERLAP]", "the base station spot overlaps something"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[BASE_WATER]", "the base station spot is in water"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[BASE_LAND]", "the base station spot is outside the city's land"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[BASE_OTHER]", "the game refused the base station spot"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[TOP_OVERLAP]", "the top station spot overlaps something (the base station stands)"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[TOP_WATER]", "the top station spot is in water (the base station stands)"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[TOP_LAND]", "the top station spot is outside the city's land (the base station stands)"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[TOP_OTHER]", "the game refused the top station spot (the base station stands)"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[GONE]", "a station was removed"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[STOPPED]", "the build was interrupted"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[LINE_PICK]", "the lift tool could not pick the stations"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[LINE_ABANDONED]", "the lift tool was closed before the line was built"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[LINE_REFUSED]", "the game refused the line"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[PLAN_LOW]", "the line hangs too close to the ground"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[PLAN_STEEP]", "the line is too steep for this lift"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[PLAN_HIGH]", "the line runs too high above the ground"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[PLAN_TRAIN]", "one tower bends the rope more than its wheels can follow"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[PLAN_TRAINS]", "two or more towers bend the rope more than their wheels can follow"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[PLAN_SADDLE]", "one tower bends the rope more than its saddle is built for"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[PLAN_PITCH]", "the rope runs through one tower steeper than this lift type allows"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[PLAN_CLEARANCE]", "the rope leaves one tower steeper than its chairs or cabins clear"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[BUILD_REFUSED]", "the game refused the build"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[WAIT]", "the line did not build on its own"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[ALONG]", "the line runs along another lift"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[CROSS]", "the line crosses another lift where it cannot be built"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[BACKWARD]", "a station faces away from the line"],
  ["SkiResort.Tooltip.QUICK_LIFT_WHY[GAME]", "the game or the lift tool refused it"],
  ["SkiResort.Tooltip.QUICK_LIFTS_NOTICE[Nothing]", "No lift is left to build."],
  ["SkiResort.Tooltip.QUICK_LIFTS_NOTICE[Queued]", "Those lifts are already in the build queue."],
  ["SkiResort.Tooltip.QUICK_LIFTS_NOTICE[Busy]", "Lifts are being built right now."],
  ["SkiResort.Tooltip.QUICK_LIFTS_NOTICE[RunsBusy]", "Lifts build once the planner is no longer scanning, showing a preview or building runs."],
  ["SkiResort.Tooltip.QUICK_LIFTS_NOTICE[Off]", "The lift build is off until the next load. Details are in the mod log."],
];

export function sentKeyEntry(key: string | undefined | null, fallback: LocEntry): LocEntry {
  if (!key) return fallback;
  for (const e of SENT_KEYS) if (e[0] === key) return e;
  return [key, fallback[1]];
}

/*
 * The game's own efficiency factor names: `SelectedInfoPanel.EFFICIENCY_FACTORS[<enum name>]` (EfficiencySection.cs:32-42).
 * We read the English fallbacks from the installed Content/Game/Locale.cok (en-US block). Not our keys: they are not in LocaleEN.cs,
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
 * UpkeepPropertyBinderSystem, PrefabUISystem.cs:539-560 / 626-690). The installed index.js renders each row only when its value
 * is > 0, with units Power / VolumePerMonth / WeightPerMonth / moneyPerMonth. We read the English fallbacks from the installed
 * Content/Game/Locale.cok (en-US). Not our keys: they are not in LocaleEN.cs, and gate R4 does not check them (like factorEntry).
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
 * id `${id}:${index}`. The vanilla birth-date picker renders index 0 for January). Present in all 12 shipped locales of
 * Content/Game/Locale.cok. Not our keys: they are not in LocaleEN.cs, and gate R4 does not check them (like factorEntry). Fallbacks:
 * English.
 */
const MONTH_FALLBACK: readonly string[] = [
  "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
];

/* The month's entry for a CityStats.month index (0..11). null when unknown (-1 or out of range). */
export function monthEntry(month: number): LocEntry | null {
  return Number.isInteger(month) && month >= 0 && month < 12 ? [`Common.MONTH:${month}`, MONTH_FALLBACK[month]] : null;
}

/* The {MONTH} argument: the month `back` months before city.month, translated. It is "–" while the month is not known. */
export function monthName(month: number, t: (e: LocEntry) => string, back = 0): string {
  const e = monthEntry(Number.isInteger(month) && month >= 0 ? (month - back + 12 * 4) % 12 : -1);
  return e ? t(e) : "–";
}

/* Every month's name, translated: width samples for a cell that shows a month. */
export function allMonthNames(t: (e: LocEntry) => string): string[] {
  return MONTH_FALLBACK.map((_, i) => monthName(i, t));
}

/*
 * The game's short month names, `Common.MONTH_SHORT:<0-11>` (the same indexed entry kind as Common.MONTH). The installed index.js has
 * `MONTH_SHORT:new Ec` @444971. The vanilla time-line charts render it with {index} @953286. Locale.cok holds
 * `Common.MONTH_SHORT:9` in all 12 locales: "Oct" in English, "Okt" in German, "10" in the CJK tables. So the index is 0-based
 * like Common.MONTH. The History view's axis labels. Not our keys: they are not in LocaleEN.cs (like monthEntry).
 */
const MONTH_SHORT_FALLBACK: readonly string[] = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/* The short name of a calendar month 0..11, translated. It is "–" when out of range. */
export function monthShortName(month: number, t: (e: LocEntry) => string): string {
  return Number.isInteger(month) && month >= 0 && month < 12 ? t([`Common.MONTH_SHORT:${month}`, MONTH_SHORT_FALLBACK[month]]) : "–";
}
