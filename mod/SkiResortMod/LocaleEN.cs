// English display names + descriptions for the asset pack's player-facing prefabs.
// Registered in Mod.OnLoad via GameManager.instance.localizationManager.AddSource("en-US", ...)
// alongside the options-UI source (Settings\LocaleEN.cs) — the manager merges every source
// registered for the active locale.
//
// Key patterns (verified against decompiled Game.UI.InGame.PrefabUISystem.GetTitleAndDescription):
//   - plain assets:                Assets.NAME[<prefab name>] / Assets.DESCRIPTION[<prefab name>]
//   - ServiceUpgrade assets:       Assets.UPGRADE_NAME[...] / Assets.UPGRADE_DESCRIPTION[...]
//     (the lodge carries Game.Prefabs.ServiceUpgrade, so the selected-info panel reads the
//     UPGRADE_* keys; the plain keys are supplied too for every other UI path, e.g. search)
// Round M1 (docs/research/hub-u2-spec.md §2.4 "Removed", §6.1 d/e; 2026-10-03, implementer I-4):
// the SkiResortCategory toolbar tab prefab is gone (zero vanilla toolbar entries, OD-10), so its
// SubServices.NAME / Assets.SUB_SERVICE_DESCRIPTION keys are removed; the three run prefabs lost
// their ServiceUpgrade component (OD-12 (a): the sign's upgrade strip holds the lodge only), so
// their six Assets.UPGRADE_NAME / UPGRADE_DESCRIPTION keys are removed too.
// Round E1 (2026-10-04, stream S6; docs/research/econ-round/spec/ch6-hub-ui.md §7.5, which owns this
// file, e1-spec.md R11 / I-27): hub contract 3 — 64 retired SkiResort.Hub.* keys deleted (ch6 §7.4,
// no C# reference), 7 texts changed in place (RESORT_PREV, RESORT_NEXT, UNLINKED_ENTRY, RUNS_EMPTY,
// LIFT_CAPACITY, BUILD_HINT_SIGN, HINT[LiftNoPair]), 281 new keys in block (e); chapter 2 §4.6's run
// asset names (Title Case, D17), the natural run's name and description and SkiResort.Tooltip.RUN_FEE;
// ch5 R-UI-2's RUN_DEFAULT[Natural]. Gate rule R4 (mod/SkiResortUI/tools/check-bundle.js) fails the UI
// build on any text that differs from its src/hub/hubText.ts tuple.
// Round F7 (2026-10-04, implementer 4; docs/research/field-test-7-fixes.md D1-D6, findings/hub-ux.md and
// its Verification): this round CUTS text. The sign, lodge and base station descriptions are one line
// each (D6). New hub keys: RUN_STATE[Open|Patchy|Closed], TILE[RunsOpen], EST_TO_TIP, MONEY_TIP[Rwh],
// PROBLEM[SignNoRoad|LodgeNoRoad] + PROBLEM_TIP (D5; C# HubProblemCode 18 / 19). Changed in place: the
// four efficiency rows (PROBLEM[Efficiency|EfficiencyMore|NotRunning|NotRunningMore]: "Welcome Sign ...",
// "and {N} more"), PROBLEM_TIP[Efficiency], RECEIPT[LodgeBaseHalf], EST_TO_NOTE, MONEY_NOTE[Rwh] (the
// "bills less" claim is retired: never measured), RUN_OPEN[Partly], TILE_TIP_PARTLY, and the five longest
// tooltips (PISTE_MAP_TIP, PROBLEM_TIP[NoLift|RunsUnserved|LiftNoPower], TILE_TIP[Attraction]) cut to 25
// words or fewer. Retired: START_NOTE, MONEY_NOTE[Unknown]. F7 fixer: STATUS_LINE_PARTLY (+ _ONE, _EST,
// _ONE_EST), STATUS_TIP_COUNTS and RECEIPT_TIP[Open] count "patchy" runs, not "partly open" ones (one name per
// run state; STATUS_WORD[Partly] "Partly open" stays the area's word).
// Exemption to the 25-word tooltip rule above (code round 4 fixer, 2026-10-11; round-4 review of build-hub.md):
// MONEY_TIP[Lifts] and MONEY_TIP[LiftsTypes] carry live figures ({BASE} / {PERKM}, {FULL}, {REDUCED}) and the billing
// rule, so they are exempt; each is capped at its Phase D length (42 and 44 words, `wc -w`) and tests/ResortBatch
// CatalogGuardTests CG-T8 (g) fails if either grows.
// Round S1 "Ski days" (2026-10-05, stream E; docs/research/ski-days/s1-spec.md §2.12 with the §5.3 rulings,
// spec/ch4-hub.md §G.1): hub contract 4, 28 new SkiResort.Hub.* keys, each placed in its block below with a
// "Round S1" comment: POWDER_DAY (D29), PROBLEM[SkiDaysCannotReach|NoParkingNearLodge|LodgeNotSlopeside] and
// their PROBLEM_TIP (D28, C# HubProblemCode 20-22), the Guests view's ski-day row, state line and tooltips (D27),
// GUESTS_MONTH[LastPartial] and GUESTS_LAST_PARTIAL (F7), and the placeholder keys STATUS_TIP_POWDER,
// GUESTS_BAR_SCALE, SKI_DAYS[RoomRuns|RoomLodges|Room] ("Holds {N} skiers at once", ruling R-Q3) and
// SKI_DAYS_TIP[Sent|Now]. Nothing changed in place, nothing retired. The options texts of the four ski-day
// settings live in Settings\LocaleEN.cs.
// Round M1 "measure first" (2026-10-05; docs/research/ski-days/measure/m1-spec.md M10 D1, M7c; not the hub-u2 "M1"
// above): changed in place, with the same texts in src/hub/hubText.ts (gate R4): PROBLEM[SkiDaysCannotReach] and
// PROBLEM_TIP[SkiDaysCannotReach] (D1: the row is about ski-day guests with a car who never arrived, most of whom
// never left home, not an unreachable lodge; field test 9, DECISIONS 152; texts per review L3) and
// GUESTS_LAST_PARTIAL (D5: a month can now be partial because time was skipped ahead). No key is new and none is
// retired. The [reach] log line reads the row text from the active dictionary, so it follows these texts.
// Round M2 (2026-10-10, builder B3; docs/research/round-m2/build-contract.md §8.3-§8.4, gate R4 with
// src/hub/hubText.ts): 11 new SkiResort.Hub.* keys, each with a "Round M2" comment in its block: MONEY_PRICE_LINE and
// MONEY_PRICE_TIP[Title|Where|Curve|Who] (R7, the Money view's price line; the Options path read from
// Settings\LocaleEN.cs: tab Main, group Ski area balance), TRIP_PLANNER_NOTE[Known|KnownOff|Unknown|UnknownOff] and
// TRIP_PLANNER_TIP (R9, the Guests view's trip-planner note; HUB_CONTRACT 5), LIFT_COST_REDUCED[NoOpenRun] (R3, T2).
// 10 texts changed in place: TICKETS_NOTE[NothingOpen] (R2: a running lift alone no longer opens the gate) and
// TICKETS_NOTE[NotKnownYet] (also a new sign), MONEY_TIP[Lifts] (R3), the five SEASON_TIP texts (R5: they say what
// they read; "never has a winter" goes), PROBLEM_TIP[Efficiency] and RECEIPT_TIP[Efficiency] (R5: garbage named).
// Nothing retired; MONEY_TICKETS_PRICE stays.
// Catalog Phase B (2026-10-11, code round 2, hub group; docs/research/art/assets-2026-10/code-round-2/round-2-contract.md
// §8.6, gate R4 with src/hub/hubText.ts; hub contract 6): the 4 locale keys of each new upgrade building (SkiLodgeHut,
// SkiLodgeMid, SkiLodgeSummit, SkiLodgeGrand, SkiSchool, SkiPatrolHut, SkiPatrolHQ: 28 keys; R21: no "Needs educated staff")
// and the NAME / DESCRIPTION of the three new lines and six new stations (prefabs still pending: the keys wait for the
// prefab-surgery round, round-2-contract §10.1); D17: the fixed-grip stations' names gain the type ("Fixed-Grip Base
// Station", "Fixed-Grip Top Station"). New SkiResort.Hub.* keys, each with a "Catalog Phase B" comment in its block:
// LIFT_TYPE[*], LODGE_TIER[*], PICKER_TIP[*], PROBLEM / PROBLEM_TIP [MixedLiftTypes|SummitLodgeNotLiftServed|
// SchoolNoGreenRun] (C# HubProblemCode 23-25), REASON[AlreadyBuilt] (C# sends it; REASON[OneLuxury] and
// RECEIPT_TIP_LUXURY_IGNORED were removed on 2026-10-11 with the one-luxury-lodge-per-area rule), BUILD_SHORT[School|Patrol],
// BUILD_TIP_SCHOOL / PATROL / LUXURY, RECEIPT_TIP[School], RECEIPT_TIP_LODGE_BEST,
// RECEIPT_PARTS_SCHOOL, LIFTS_TOTAL_CAPACITY_TIP, MONEY_PRICE_TIP[Tolerance], MONEY_ROW[LodgeSpend], MONEY_TIP[LodgeSpend],
// MONEY_TIP[LiftsTypes], MONEY_TIP_LIFT_RATE. Changed in place: LIFT_CAPACITY_TIP and LIFT_RIDE_TIP (placeholders {SEATS} /
// {SPEED}: every lift type has its own), RECEIPT_TIP[LodgeBase] (the R11 ladder) and RECEIPT_TIP[Lifts] (R12 per type).
// The numbers baked into BUILD_TIP_SCHOOL / PATROL / LUXURY, PROBLEM_TIP[SummitLodgeNotLiftServed], RECEIPT_TIP[LodgeBase] and
// RECEIPT_TIP[Lifts] are SkiCatalog's: tests/ResortBatch CatalogGuardTests CG-T2..T6 derive them from the catalog, so each of
// those keys must stay a single-line `{ "key", "value" },` entry. Nothing retired.
// Catalog Phase C (2026-10-11, code round 3, rules group; docs/research/art/assets-2026-10/code-round-3/round-3-contract.md
// C15 / §6.5, gate R4 with src/hub/hubText.ts; hub contract 6 unchanged): three texts changed in place so they hold for both
// movers (a chair's loop and the tram's shuttle): LIFT_RIDE_TIP ("Base to top …", no "loop"), LIFT_CAPACITY_TIP (no
// "spaced"; "a tram counts its station stops") and, in the round's minors fix (contract review note), the totals row's
// LIFTS_TOTAL_CAPACITY_TIP (same wording: it sums a tram too), CG-T2 / T3 / T7 guard them;
// Assets.DESCRIPTION[SkiTramBaseStationBuilding] names its two hosted cabins (no R4 pair). No key new, none retired.
// Phase D "unpowered lifts are inert" (2026-10-11, code round 4, hub group; docs/research/art/assets-2026-10/code-round-4/
// round-4-contract.md §4.4 with round-4-contract-skeptic.md MUST-1 / MUST-3, DECISIONS 171 rulings: no capacity, no guests,
// the carriers stop; full upkeep regardless of power; power at the base station only), gate R4 with src/hub/hubText.ts;
// hub contract 6 unchanged: seven texts changed in place. TILE_TIP[Lifts] (no "still move"), LIFT_CAPACITY_TIP and
// LIFTS_TOTAL_CAPACITY_TIP (an unpowered lift counts none) and PROBLEM_TIP[LiftNoPower] (it stops, serves no run,
// maintenance still charged), each 25 words or fewer; MONEY_TIP[Lifts] and MONEY_TIP[LiftsTypes] (in full even without
// power; a quarter only while unfinished or with no open run); LIFT_COST_REDUCED[NoPower] (a No power row is now billed
// reduced only for NoOpenRun, so it names that reason). tests/ResortBatch CatalogGuardTests CG-T8 guards them. No key new,
// none retired. Round-4 fixer: PROBLEM_TIP[LiftNoPower] says "maintenance is still charged", no longer "full": power never
// lowers the bill, but the kept NoOpenRun quarter reaches an unpowered lift whenever its ski area counts no run (no snow,
// or backcountry 0 with no other serving lift), so "full" was false then (round-4-integration.md §6 L1).
// Names pass (2026-10-13, Richard's 26th look: "make sure the NAMES of our assets make sense to a reader that is not
// myself. People may use Find It"; table and rationale: docs/research/art/assets-2026-10/NAMES-2026-10-13.md). Find It
// (pdx 77240 v58, decompiled) lists every StaticObjectPrefab, pillar, service building, upgrade and surface under the
// title PrefabUISystem.GetTitleAndDescription picks (PrefabUISystem.cs:1498-1528, 1.6.0f1: Assets.UPGRADE_NAME for a
// ServiceUpgrade, else Assets.NAME) and falls back to the raw prefab name split at capitals ("Ski Lift Tower FG2"). So
// every prefab a player can meet now has a NAME + DESCRIPTION here, deployed and to-import alike (a key whose prefab does
// not exist yet is inert); the exceptions are never shown (pieces, sections, effects, meshes) or listed for deletion
// (the 19 unused legacy prefabs, NAMES-2026-10-13.md §2.9-§2.10). Scheme: family first, then size, then the variant in parentheses ("Gondola Tower 16 m",
// "Ski Rack, Freestanding, 12 pairs + 2 boards (Red)"); test objects say so ("Ski Resort — import probe B: Sheet (delete after test)").
// Wording revision (2026-10-13 ~11:00, NAMES-2026-10-13-check.md R1-R4; names only, every key unchanged): the eight station
// buildings say the lift type and the end ("Fixed-Grip Chairlift: Base Station", "Detachable Quad Chairlift: Top Station",
// "Gondola: Base Station"); the tram says "Aerial Tram: Base Terminal" / "Top Terminal" (never "Tram Station": a stranger reads
// the city tram) and its two descriptions say terminals; hubText.ts ASSET_NAME follows (gate R4). The kit racks describe their
// loadout instead of a letter (dress-kit.md 1.2: R1 / R2 / R3 freestanding, W1 / W2 wall). Parts a player can meet (pickers,
// station towers, cable lanes and stubs, sign sets, fence lane, run-edge groups, empty slot) put the variant FIRST and drop the
// "Ski Resort — <kind>:" prefix (a narrow Find It cell may cut the tail that tells Base from Top: unverified); test objects keep the prefix.
// Changed in place: Assets.NAME / UPGRADE_NAME of SkiLodgeSummit and SkiLodgeGrand gain "Swanky" (Richard: "we can def
// keep the 'swanky' in that big lodge"; hubText.ts ASSET_NAME follows, gate R4). Nothing retired. Internal prefab names
// are untouched (deployed ones are save identity, the rest are Richard's call from the table). Keep every entry unique:
// a duplicate key throws inside ReadEntries and LocalizationManager.LoadLocaleSource drops this whole source.
// Additional languages: further IDictionarySource implementations — see SPEC §13 Phase 6.

using System.Collections.Generic;
using Colossal;

namespace SkiResortMod
{
    public sealed class LocaleEN : IDictionarySource
    {
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                // --- Resort anchor --------------------------------------------------------
                // Description changed in M1 (hub-u2-spec.md §2.4 "Changed"): the hub, not the sign's
                // upgrade menu, is where everything is built. Round F7 (D6): one line in the vanilla
                // info panel (findings/hub-ux.md §5 item 12; it was 4 lines). Each of the three Round F7
                // descriptions is at most 334 rem in the panel's S font at 100 % text (Overpass advances;
                // the panel is 400 rem wide, its description box narrower: one line at 100 % [likely]).
                { "Assets.NAME[SkiResortGate]", "Ski Area Welcome Sign" },
                { "Assets.DESCRIPTION[SkiResortGate]", "The ski area's entrance. It opens the Ski Resort panel." },

                // --- Lodge (ServiceUpgrade child of the gate) -----------------------------
                // Round F7 (D6): one line (was 2), both keys (the selected-info panel reads UPGRADE_*).
                { "Assets.NAME[SkiAFrameDayLodge]", "A-Frame Day Lodge" },
                { "Assets.DESCRIPTION[SkiAFrameDayLodge]", "A first lodge, where guests arrive." },
                { "Assets.UPGRADE_NAME[SkiAFrameDayLodge]", "A-Frame Day Lodge" },
                { "Assets.UPGRADE_DESCRIPTION[SkiAFrameDayLodge]", "A first lodge, where guests arrive." },
                // Catalog Phase B (round-2-contract §8.6; lodges-basic §4.10 / §5.10, lodges-swanky §12 without "Needs educated
                // staff" (R21), civic-sign §2.8 / §3.7): the four lodge tiers, the school and the two patrol huts, every one a
                // ServiceUpgrade of the sign (so the UPGRADE_* keys are the ones its info panel reads). One line each (Round F7 D6).
                { "Assets.NAME[SkiLodgeHut]", "Mountain Hut" },
                { "Assets.DESCRIPTION[SkiLodgeHut]", "A small hut on the slopes, no road needed. Counts more in summer near a lift's top." },
                { "Assets.UPGRADE_NAME[SkiLodgeHut]", "Mountain Hut" },
                { "Assets.UPGRADE_DESCRIPTION[SkiLodgeHut]", "A small hut on the slopes, no road needed. Counts more in summer near a lift's top." },
                { "Assets.NAME[SkiLodgeMid]", "Chalet Lodge" },
                { "Assets.DESCRIPTION[SkiLodgeMid]", "A bigger base lodge: more ski-day guests and a higher ticket price." },
                { "Assets.UPGRADE_NAME[SkiLodgeMid]", "Chalet Lodge" },
                { "Assets.UPGRADE_DESCRIPTION[SkiLodgeMid]", "A bigger base lodge: more ski-day guests and a higher ticket price." },
                // Names pass 2026-10-13: the two luxury tiers say "Swanky" (Richard kept it for the big lodge; the summit
                // lodge is its smaller sibling, mesh SkiLodgeSwankyTop*). Was "Summit Lodge" / "Grand Lodge".
                { "Assets.NAME[SkiLodgeSummit]", "Swanky Summit Lodge" },
                { "Assets.DESCRIPTION[SkiLodgeSummit]", "Glass-and-stone restaurant on the mountain, near a lift's top station." },
                { "Assets.UPGRADE_NAME[SkiLodgeSummit]", "Swanky Summit Lodge" },
                { "Assets.UPGRADE_DESCRIPTION[SkiLodgeSummit]", "Glass-and-stone restaurant on the mountain, near a lift's top station." },
                { "Assets.NAME[SkiLodgeGrand]", "Swanky Grand Lodge" },
                { "Assets.DESCRIPTION[SkiLodgeGrand]", "The resort's flagship base lodge: great hall and dining pavilion." },
                { "Assets.UPGRADE_NAME[SkiLodgeGrand]", "Swanky Grand Lodge" },
                { "Assets.UPGRADE_DESCRIPTION[SkiLodgeGrand]", "The resort's flagship base lodge: great hall and dining pavilion." },
                { "Assets.NAME[SkiSchool]", "Ski School" },
                { "Assets.DESCRIPTION[SkiSchool]", "Teaches on the green runs: the ski area scores more and locals come more often. Terrain tip: level the ground at the top of the magic carpet bubble, then use the slope tool down to its base." },
                { "Assets.UPGRADE_NAME[SkiSchool]", "Ski School" },
                { "Assets.UPGRADE_DESCRIPTION[SkiSchool]", "Teaches on the green runs: the ski area scores more and locals come more often." },
                { "Assets.NAME[SkiPatrolHut]", "Ski Patrol Hut" },
                { "Assets.DESCRIPTION[SkiPatrolHut]", "Patrols the steep runs nearby. No road needed." },
                { "Assets.UPGRADE_NAME[SkiPatrolHut]", "Ski Patrol Hut" },
                { "Assets.UPGRADE_DESCRIPTION[SkiPatrolHut]", "Patrols the steep runs nearby. No road needed." },
                { "Assets.NAME[SkiPatrolHQ]", "Ski Patrol HQ" },
                { "Assets.DESCRIPTION[SkiPatrolHQ]", "Patrol headquarters: patrols the steep runs nearby. No road needed." },
                { "Assets.UPGRADE_NAME[SkiPatrolHQ]", "Ski Patrol HQ" },
                { "Assets.UPGRADE_DESCRIPTION[SkiPatrolHQ]", "Patrol headquarters: patrols the steep runs nearby. No road needed." },

                // --- Lift network ---------------------------------------------------------
                // Round F7 (D6): the base station's description is one line (was 3).
                // Catalog Phase B (D17; round-2-contract §8.6): the fixed-grip stations' names gain the type (were "Chairlift
                // Base Station" / "Chairlift Top Station"), so the picker's types read apart; hubText.ts ASSET_NAME equals them.
                // Wording revision 2026-10-13 (NAMES check R1 / R2): "<lift type>: <end>" for all eight stations; the tram's are
                // terminals. The deployed .loc of the base / top station carries different NAME and DESCRIPTION texts; this source
                // loads after it and wins (probes-B.md "Deployed .loc name conflicts").
                { "Assets.NAME[SkiBaseStationBuilding]", "Fixed-Grip Chairlift: Base Station" },
                { "Assets.DESCRIPTION[SkiBaseStationBuilding]", "Drive terminal. Connect a low-voltage line to the wire end at its front corner." },
                { "Assets.NAME[SkiTopStationBuilding]", "Fixed-Grip Chairlift: Top Station" },
                {
                    "Assets.DESCRIPTION[SkiTopStationBuilding]",
                    "Return terminal of a fixed-grip chairlift. Power flows up the line from " +
                    "the base — no hookup needed here."
                },
                // Catalog Phase A (ROUND-SPEC R2; ext row 23; was "Fixed-Grip Chairlift"): equals SkiCatalog.FixedGrip — tests/ResortBatch
                // CatalogGuardTests text guard; hubText.ts holds the same text (gate R4).
                { "Assets.NAME[SkiChairliftHV]", "Fixed-Grip Triple Chairlift" },
                {
                    "Assets.DESCRIPTION[SkiChairliftHV]",
                    "Draw the lift line like a road: towers pick their own height, chairs " +
                    "ride the cable."
                },
                // Catalog Phase B (round-2-contract §8.6, §10.1): the three new lines and six new stations (keys written while
                // pending; all nine names Expected since the S1 import flips of 2026-10-13, build/s1-fix-codeflip.md). The tram is
                // not offered until Phase C (Shuttle mover). hubText.ts ASSET_NAME holds the same names (gate R4).
                { "Assets.NAME[SkiDetachableHV]", "Detachable Quad Chairlift" },
                { "Assets.DESCRIPTION[SkiDetachableHV]", "Four-seat chairs that detach from the cable in the stations. Draw it like a road." },
                { "Assets.NAME[SkiGondolaHV]", "Gondola" },
                { "Assets.DESCRIPTION[SkiGondolaHV]", "Eight-seat cabins on a long-span cable, open in summer too. Draw it like a road." },
                { "Assets.NAME[SkiTramHV]", "Aerial Tram" },
                { "Assets.DESCRIPTION[SkiTramHV]", "Two large cabins shuttle between the base and top terminals." },
                { "Assets.NAME[SkiDQBaseStationBuilding]", "Detachable Quad Chairlift: Base Station" },
                { "Assets.DESCRIPTION[SkiDQBaseStationBuilding]", "Drive terminal. Connect a low-voltage line to the wire end at its front corner." },
                { "Assets.NAME[SkiDQTopStationBuilding]", "Detachable Quad Chairlift: Top Station" },
                { "Assets.DESCRIPTION[SkiDQTopStationBuilding]", "Return terminal. Power flows up the line from the base: no hookup needed here." },
                { "Assets.NAME[SkiGondolaBaseStationBuilding]", "Gondola: Base Station" },
                { "Assets.DESCRIPTION[SkiGondolaBaseStationBuilding]", "Drive terminal. Connect a low-voltage line to the wire end at its front corner." },
                { "Assets.NAME[SkiGondolaTopStationBuilding]", "Gondola: Top Station" },
                { "Assets.DESCRIPTION[SkiGondolaTopStationBuilding]", "Return terminal. Power flows up the line from the base: no hookup needed here." },
                { "Assets.NAME[SkiTramBaseStationBuilding]", "Aerial Tram: Base Terminal" },
                // Catalog Phase C (round-3-contract §6.5, optional row; was "Drive terminal. Connect a low-voltage line to its
                // pole."): the base hosts the two cabins (R29). Not in hubText.ts (no R4 pair).
                { "Assets.DESCRIPTION[SkiTramBaseStationBuilding]", "Drive terminal; its two cabins park here. Connect a low-voltage line to the wire end at its front corner." },
                { "Assets.NAME[SkiTramTopStationBuilding]", "Aerial Tram: Top Terminal" },
                { "Assets.DESCRIPTION[SkiTramTopStationBuilding]", "Return terminal. Power flows up the line from the base: no hookup needed here." },

                // --- Lift parts: names pass 2026-10-13 (NAMES-2026-10-13.md) ----------------------------------------------
                // Not hub items: these surface in Find It (towers under Pillars, the rest under Props) and in the info panel
                // of anything a player can click. Scheme: family first, then size, then variant; parts say they are parts.
                // Line towers. SkiLiftTowerFG2 is the deployed 8 m member (its name predates the height naming, frozen).
                { "Assets.NAME[SkiLiftTowerFG2]", "Fixed-Grip Chairlift Tower 8 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerFG2]", "A fixed-grip chairlift's line tower. The lift line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerFG12]", "Fixed-Grip Chairlift Tower 12 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerFG12]", "A fixed-grip chairlift's line tower. The lift line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerFG16]", "Fixed-Grip Chairlift Tower 16 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerFG16]", "A fixed-grip chairlift's line tower. The lift line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerFG20]", "Fixed-Grip Chairlift Tower 20 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerFG20]", "A fixed-grip chairlift's line tower. The lift line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerDQ10]", "Detachable Chairlift Tower 10 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerDQ10]", "A detachable quad chairlift's line tower. The lift line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerDQ12]", "Detachable Chairlift Tower 12 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerDQ12]", "A detachable quad chairlift's line tower. The lift line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerDQ14]", "Detachable Chairlift Tower 14 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerDQ14]", "A detachable quad chairlift's line tower. The lift line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerDQ16]", "Detachable Chairlift Tower 16 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerDQ16]", "A detachable quad chairlift's line tower. The lift line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerDQ18]", "Detachable Chairlift Tower 18 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerDQ18]", "A detachable quad chairlift's line tower. The lift line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerDQ20]", "Detachable Chairlift Tower 20 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerDQ20]", "A detachable quad chairlift's line tower. The lift line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerGD12]", "Gondola Tower 12 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerGD12]", "A gondola line tower with a red warning beacon. The gondola line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerGD16]", "Gondola Tower 16 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerGD16]", "A gondola line tower with a red warning beacon. The gondola line places its towers by itself." },
                { "Assets.NAME[SkiLiftTowerGD20]", "Gondola Tower 20 m" },
                { "Assets.DESCRIPTION[SkiLiftTowerGD20]", "A gondola line tower with a red warning beacon. The gondola line places its towers by itself." },
                { "Assets.NAME[SkiGondolaLattice25]", "Gondola Lattice Tower 25 m" },
                { "Assets.DESCRIPTION[SkiGondolaLattice25]", "A steel-lattice gondola tower for long spans, with a red warning beacon. The gondola line places its towers by itself." },
                { "Assets.NAME[SkiGondolaLattice32]", "Gondola Lattice Tower 32 m" },
                { "Assets.DESCRIPTION[SkiGondolaLattice32]", "A steel-lattice gondola tower for long spans, with a red warning beacon. The gondola line places its towers by itself." },
                { "Assets.NAME[SkiGondolaLattice40]", "Gondola Lattice Tower 40 m" },
                { "Assets.DESCRIPTION[SkiGondolaLattice40]", "A steel-lattice gondola tower for long spans, with a red warning beacon. The gondola line places its towers by itself." },
                { "Assets.NAME[SkiTramTower30]", "Aerial Tram Tower 30 m" },
                { "Assets.DESCRIPTION[SkiTramTower30]", "A steel-lattice aerial tram tower. The tram line places its towers by itself." },
                { "Assets.NAME[SkiTramTower45]", "Aerial Tram Tower 45 m" },
                { "Assets.DESCRIPTION[SkiTramTower45]", "A steel-lattice aerial tram tower. The tram line places its towers by itself." },
                { "Assets.NAME[SkiTramTower60]", "Aerial Tram Tower 60 m" },
                { "Assets.DESCRIPTION[SkiTramTower60]", "A steel-lattice aerial tram tower with a red warning beacon. The tram line places its towers by itself." },
                // Tower pickers (PlaceholderObject: the line plants one and the game picks the member by height).
                { "Assets.NAME[SkiLiftTowerPlaceholder]", "Fixed-Grip Chairlift Tower Picker (used by the line)" },
                { "Assets.DESCRIPTION[SkiLiftTowerPlaceholder]", "The lift line uses this to choose a tower height. Place a sized tower instead." },
                { "Assets.NAME[SkiLiftTowerDQPlaceholder]", "Detachable Chairlift Tower Picker (used by the line)" },
                { "Assets.DESCRIPTION[SkiLiftTowerDQPlaceholder]", "The lift line uses this to choose a tower height. Place a sized tower instead." },
                { "Assets.NAME[SkiGondolaTowerPlaceholder]", "Gondola Tower Picker (used by the line)" },
                { "Assets.DESCRIPTION[SkiGondolaTowerPlaceholder]", "The gondola line uses this to choose a tower height. Place a sized tower instead." },
                { "Assets.NAME[SkiTramTowerPlaceholder]", "Aerial Tram Tower Picker (used by the line)" },
                { "Assets.DESCRIPTION[SkiTramTowerPlaceholder]", "The tram line uses this to choose a tower height. Place a sized tower instead." },
                // Station towers (hosted by a station building at its lift end).
                { "Assets.NAME[SkiLiftTerminalTowerBase]", "Fixed-Grip Base Station Tower (part of a station)" },
                { "Assets.DESCRIPTION[SkiLiftTerminalTowerBase]", "The fixed-grip base station builds this tower at its lift end." },
                { "Assets.NAME[SkiLiftTerminalTowerTop]", "Fixed-Grip Top Station Tower (part of a station)" },
                { "Assets.DESCRIPTION[SkiLiftTerminalTowerTop]", "The fixed-grip top station builds this tower at its lift end." },
                { "Assets.NAME[SkiLiftTerminalTowerDQ]", "Detachable Base Station Tower (part of a station)" },
                { "Assets.DESCRIPTION[SkiLiftTerminalTowerDQ]", "The detachable base station builds this tower at its lift end." },
                { "Assets.NAME[SkiLiftTerminalTowerGD]", "Gondola Base Station Tower (part of a station)" },
                { "Assets.DESCRIPTION[SkiLiftTerminalTowerGD]", "The gondola base station builds this tower at its lift end." },
                // Carriers. SkiLiftChair4 is the deployed three-seat chair (its name predates the triple, frozen).
                { "Assets.NAME[SkiLiftChair4]", "Chairlift Chair (Fixed-Grip Triple)" },
                { "Assets.DESCRIPTION[SkiLiftChair4]", "A three-seat chair. The lift line hangs its chairs on the cable by itself." },
                { "Assets.NAME[SkiLiftChairDQ4]", "Chairlift Chair (Detachable Quad)" },
                { "Assets.DESCRIPTION[SkiLiftChairDQ4]", "A four-seat chair with a safety bar. The lift line hangs its chairs on the cable by itself." },
                { "Assets.NAME[SkiGondolaCabin8]", "Gondola Cabin (8 Seats)" },
                { "Assets.DESCRIPTION[SkiGondolaCabin8]", "An eight-seat cabin. The gondola line hangs its cabins on the cable by itself." },
                { "Assets.NAME[SkiTramCabin]", "Aerial Tram Cabin" },
                { "Assets.DESCRIPTION[SkiTramCabin]", "A large aerial tram cabin. An aerial tram has two, shuttling between its terminals." },
                // Cable lanes (Find It lists them only with Extra Detailing Tools) and the stations' cable stubs.
                { "Assets.NAME[SkiLiftHaulRopeB]", "Fixed-Grip Chairlift Haul Rope (part of a lift)" },
                { "Assets.DESCRIPTION[SkiLiftHaulRopeB]", "The cable a fixed-grip chairlift line draws between its towers." },
                { "Assets.NAME[SkiDetachableHaulRope]", "Detachable Chairlift Haul Rope (part of a lift)" },
                { "Assets.DESCRIPTION[SkiDetachableHaulRope]", "The cable a detachable quad chairlift line draws between its towers." },
                { "Assets.NAME[SkiGondolaHaulRope]", "Gondola Haul Rope (part of a lift)" },
                { "Assets.DESCRIPTION[SkiGondolaHaulRope]", "The cable a gondola line draws between its towers." },
                { "Assets.NAME[SkiTramTrackRope]", "Aerial Tram Track Rope (part of a lift)" },
                { "Assets.DESCRIPTION[SkiTramTrackRope]", "The track cable an aerial tram's cabins ride on." },
                { "Assets.NAME[SkiTramHaulRope]", "Aerial Tram Haul Rope (part of a lift)" },
                { "Assets.DESCRIPTION[SkiTramHaulRope]", "The cable that pulls an aerial tram's cabins." },
                { "Assets.NAME[SkiLiftMarker]", "Lift Line Cable Stub (part of a station)" },
                { "Assets.DESCRIPTION[SkiLiftMarker]", "A short cable end on a lift station, where the lift line connects." },
                { "Assets.NAME[SkiLiftMarkerLV]", "Power Feed Wire Stub (part of a station)" },
                { "Assets.DESCRIPTION[SkiLiftMarkerLV]", "A short wire end at a base station's front corner, where its low-voltage power connects." },
                { "Assets.NAME[SkiBeaconRed]", "Warning Beacon (Red, Blinking)" },
                { "Assets.DESCRIPTION[SkiBeaconRed]", "A red aircraft-warning light that blinks day and night. Tall lift towers carry one." },
                // R16 tram prototype (test only; tram-prototype-sheet.md, NAMES-2026-10-13.md item R1: never the real SkiTramHV).
                { "Assets.NAME[SkiTramHVProto]", "Ski Resort — test line: Aerial Tram Prototype (delete after test)" },
                { "Assets.DESCRIPTION[SkiTramHVProto]", "A one-off test line for a field test. Remove it when the test is done." },
                { "Assets.NAME[SkiTramHVProtoH]", "Ski Resort — test line: Aerial Tram Prototype H (delete after test)" },
                { "Assets.DESCRIPTION[SkiTramHVProtoH]", "A one-off test line for a field test. Remove it when the test is done." },

                // --- Building dressing: names pass 2026-10-13 ---------------------------------------------------------------
                // The kit (props/B): one object prefab per loadout and colour set; the buildings place them as sub-objects,
                // and Find It users can place them anywhere. Freestanding racks are 1.7 m and double-sided, wall racks 2.9 m.
                // Rack names carry the loadout (NAMES check R3; build/dress-kit.md 1.2): V1 = R1 10 pairs + 3 boards + 2 pole pairs,
                // V2 = R2 12 pairs + 2 boards, V3 = R3 8 pairs + 2 boards + 2 pole pairs; wall V1 = W1 7 pairs + 2 boards, wall
                // V2 = W2 6 pairs + 1 board + 1 pole pair. The DESCRIPTION counts are the same figures.
                { "Assets.NAME[SkiDressSkiRack_Red]", "Ski Rack, Freestanding, 10 pairs + 3 boards + 2 pole pairs (Red)" },
                { "Assets.DESCRIPTION[SkiDressSkiRack_Red]", "Double-sided ski rack: 10 pairs of skis, 3 snowboards and 2 pairs of poles." },
                { "Assets.NAME[SkiDressSkiRack_Orange]", "Ski Rack, Freestanding, 10 pairs + 3 boards + 2 pole pairs (Orange)" },
                { "Assets.DESCRIPTION[SkiDressSkiRack_Orange]", "Double-sided ski rack: 10 pairs of skis, 3 snowboards and 2 pairs of poles." },
                { "Assets.NAME[SkiDressSkiRack_Green]", "Ski Rack, Freestanding, 10 pairs + 3 boards + 2 pole pairs (Green)" },
                { "Assets.DESCRIPTION[SkiDressSkiRack_Green]", "Double-sided ski rack: 10 pairs of skis, 3 snowboards and 2 pairs of poles." },
                { "Assets.NAME[SkiDressSkiRack_Blue]", "Ski Rack, Freestanding, 10 pairs + 3 boards + 2 pole pairs (Blue)" },
                { "Assets.DESCRIPTION[SkiDressSkiRack_Blue]", "Double-sided ski rack: 10 pairs of skis, 3 snowboards and 2 pairs of poles." },
                { "Assets.NAME[SkiDressSkiRackV2_Red]", "Ski Rack, Freestanding, 12 pairs + 2 boards (Red)" },
                { "Assets.DESCRIPTION[SkiDressSkiRackV2_Red]", "Double-sided ski rack: 12 pairs of skis and 2 snowboards." },
                { "Assets.NAME[SkiDressSkiRackV2_Orange]", "Ski Rack, Freestanding, 12 pairs + 2 boards (Orange)" },
                { "Assets.DESCRIPTION[SkiDressSkiRackV2_Orange]", "Double-sided ski rack: 12 pairs of skis and 2 snowboards." },
                { "Assets.NAME[SkiDressSkiRackV2_Green]", "Ski Rack, Freestanding, 12 pairs + 2 boards (Green)" },
                { "Assets.DESCRIPTION[SkiDressSkiRackV2_Green]", "Double-sided ski rack: 12 pairs of skis and 2 snowboards." },
                { "Assets.NAME[SkiDressSkiRackV2_Blue]", "Ski Rack, Freestanding, 12 pairs + 2 boards (Blue)" },
                { "Assets.DESCRIPTION[SkiDressSkiRackV2_Blue]", "Double-sided ski rack: 12 pairs of skis and 2 snowboards." },
                { "Assets.NAME[SkiDressSkiRackV3_Red]", "Ski Rack, Freestanding, 8 pairs + 2 boards + 2 pole pairs (Red)" },
                { "Assets.DESCRIPTION[SkiDressSkiRackV3_Red]", "Double-sided ski rack with gaps: 8 pairs of skis, 2 snowboards and 2 pairs of poles." },
                { "Assets.NAME[SkiDressSkiRackV3_Orange]", "Ski Rack, Freestanding, 8 pairs + 2 boards + 2 pole pairs (Orange)" },
                { "Assets.DESCRIPTION[SkiDressSkiRackV3_Orange]", "Double-sided ski rack with gaps: 8 pairs of skis, 2 snowboards and 2 pairs of poles." },
                { "Assets.NAME[SkiDressSkiRackV3_Green]", "Ski Rack, Freestanding, 8 pairs + 2 boards + 2 pole pairs (Green)" },
                { "Assets.DESCRIPTION[SkiDressSkiRackV3_Green]", "Double-sided ski rack with gaps: 8 pairs of skis, 2 snowboards and 2 pairs of poles." },
                { "Assets.NAME[SkiDressSkiRackWall_Orange]", "Ski Rack, Wall-Mounted, 7 pairs + 2 boards (Orange)" },
                { "Assets.DESCRIPTION[SkiDressSkiRackWall_Orange]", "Ski rack for a wall: 7 pairs of skis and 2 snowboards." },
                { "Assets.NAME[SkiDressSkiRackWallV2_Orange]", "Ski Rack, Wall-Mounted, 6 pairs + 1 board + 1 pole pair (Orange)" },
                { "Assets.DESCRIPTION[SkiDressSkiRackWallV2_Orange]", "Ski rack for a wall: 6 pairs of skis, a snowboard and a pair of poles." },
                { "Assets.NAME[SkiDressSkiRackWallV2_Yellow]", "Ski Rack, Wall-Mounted, 6 pairs + 1 board + 1 pole pair (Yellow)" },
                { "Assets.DESCRIPTION[SkiDressSkiRackWallV2_Yellow]", "Ski rack for a wall: 6 pairs of skis, a snowboard and a pair of poles." },
                { "Assets.NAME[SkiDressAkja_Red]", "Rescue Toboggan, Leaning (Red)" },
                { "Assets.DESCRIPTION[SkiDressAkja_Red]", "A ski patrol rescue sled (akja), stood on end against a wall." },
                { "Assets.NAME[SkiDressSpineBoard]", "Rescue Spine Board, Leaning" },
                { "Assets.DESCRIPTION[SkiDressSpineBoard]", "A ski patrol spine board, stood on end against a wall." },
                { "Assets.NAME[SkiDressHoseReel]", "Fire Hose Reel (Wall)" },
                { "Assets.DESCRIPTION[SkiDressHoseReel]", "A wall-mounted fire hose reel." },
                { "Assets.NAME[SkiDressExtinguisher]", "Fire Extinguisher (Wall)" },
                { "Assets.DESCRIPTION[SkiDressExtinguisher]", "A wall-mounted fire extinguisher." },
                { "Assets.NAME[SkiDressTools]", "Snow Shovel and Ice Scraper, Leaning" },
                { "Assets.DESCRIPTION[SkiDressTools]", "A snow shovel and an ice scraper leaning against a wall." },
                { "Assets.NAME[SkiDressEStop]", "Lift Emergency Stop (Post)" },
                { "Assets.DESCRIPTION[SkiDressEStop]", "A red emergency stop button on a post, as at every lift station." },
                // Sign sets (signs/B): a host's lettered boards, drawn at the host's origin. Hidden from Find It by their staged
                // EditorAssetCategoryOverride (exclude "FindIt"); named for the Editor and for anyone who meets one anyway.
                { "Assets.NAME[SkiSignSetSchool]", "Ski School Sign Set (auto-placed)" },
                { "Assets.DESCRIPTION[SkiSignSetSchool]", "The Ski School's lettered boards. The building places them; placed alone they float." },
                { "Assets.NAME[SkiSignSetHut]", "Mountain Hut Sign Set (auto-placed)" },
                { "Assets.DESCRIPTION[SkiSignSetHut]", "The Mountain Hut's lettered boards. The building places them; placed alone they float." },
                { "Assets.NAME[SkiSignSetPatrolHut]", "Ski Patrol Hut Sign Set (auto-placed)" },
                { "Assets.DESCRIPTION[SkiSignSetPatrolHut]", "The Ski Patrol Hut's lettered boards. The building places them; placed alone they float." },
                { "Assets.NAME[SkiSignSetPatrolHQ]", "Ski Patrol HQ Sign Set (auto-placed)" },
                { "Assets.DESCRIPTION[SkiSignSetPatrolHQ]", "The Ski Patrol HQ's lettered board. The building places it; placed alone it floats." },
                { "Assets.NAME[SkiSignSetGate]", "Ski Area Welcome Sign Set (auto-placed)" },
                { "Assets.DESCRIPTION[SkiSignSetGate]", "The welcome sign's lettered header. The sign places it; placed alone it floats." },
                { "Assets.NAME[SkiSignSetGrand]", "Swanky Grand Lodge Sign Set (auto-placed)" },
                { "Assets.DESCRIPTION[SkiSignSetGrand]", "The Grand Lodge name board. The building places it; placed alone it floats." },
                // Import probes (deleted after their test: surgery/probes-B.md §7; probes/A are optional and not scheduled).
                { "Assets.NAME[SkiProbeSheet]", "Ski Resort — import probe B: Sheet (delete after test)" },
                { "Assets.DESCRIPTION[SkiProbeSheet]", "A test object for one import session. Remove it when the test is done." },
                { "Assets.NAME[SkiProbeSheetUser]", "Ski Resort — import probe B: Sheet Sharer (delete after test)" },
                { "Assets.DESCRIPTION[SkiProbeSheetUser]", "A test object for one import session. Remove it when the test is done." },
                { "Assets.NAME[SkiProbeSheetLate]", "Ski Resort — import probe B: Late Sheet Sharer (delete after test)" },
                { "Assets.DESCRIPTION[SkiProbeSheetLate]", "A test object for one import session. Remove it when the test is done." },
                { "Assets.NAME[SkiProbeSignSet]", "Ski Resort — import probe B: Sign Set (delete after test)" },
                { "Assets.DESCRIPTION[SkiProbeSignSet]", "A test object for one import session. Remove it when the test is done." },
                { "Assets.NAME[SkiProbeRack]", "Ski Resort — import probe B: Rack (delete after test)" },
                { "Assets.DESCRIPTION[SkiProbeRack]", "A test object for one import session. Remove it when the test is done." },
                { "Assets.NAME[SkiProbeCoatA]", "Ski Resort — import probe A: Coat (delete after test)" },
                { "Assets.DESCRIPTION[SkiProbeCoatA]", "A test object for one import session. Remove it when the test is done." },
                { "Assets.NAME[SkiProbeFndA]", "Ski Resort — import probe A: Foundation (delete after test)" },
                { "Assets.DESCRIPTION[SkiProbeFndA]", "A test object for one import session. Remove it when the test is done." },
                // FndA is the one probe that is a ServiceUpgrade (its prefab carries Game.Prefabs.ServiceUpgrade), so the panel
                // and Find It read the UPGRADE_* keys (header :8; build/s1-probes-check.md §5 item 1; S1 fix 2026-10-13).
                { "Assets.UPGRADE_NAME[SkiProbeFndA]", "Ski Resort — import probe A: Foundation (delete after test)" },
                { "Assets.UPGRADE_DESCRIPTION[SkiProbeFndA]", "A test object for one import session. Remove it when the test is done." },
                { "Assets.NAME[SkiProbeGlsA]", "Ski Resort — import probe A: Glass (delete after test)" },
                { "Assets.DESCRIPTION[SkiProbeGlsA]", "A test object for one import session. Remove it when the test is done." },
                { "Assets.NAME[SkiProbeLodA]", "Ski Resort — import probe A: LOD (delete after test)" },
                { "Assets.DESCRIPTION[SkiProbeLodA]", "A test object for one import session. Remove it when the test is done." },

                // --- Safety-net fence (fences/A): names pass 2026-10-13 -------------------------------------------------------
                // NAME / DESCRIPTION of SkiSafetyFence equal the staged SkiSafetyFence_en-US.loc byte for byte
                // (assets/blender/safety_fence_stage.py LOC_ENTRIES): both sources may load, the later one wins
                // (LocalizationDictionary.Add overwrites), so they must agree. Change both or neither.
                { "Assets.NAME[SkiSafetyFence]", "Safety Net Fence" },
                { "Assets.DESCRIPTION[SkiSafetyFence]", "Orange plastic safety netting on thin poles. Draw it like a path to build corrals, mark run edges or close a slope." },
                { "Assets.NAME[SkiNetPost]", "Safety Net Fence Post" },
                { "Assets.DESCRIPTION[SkiNetPost]", "The safety net fence's end post. The fence places it by itself." },
                { "Assets.NAME[SkiSafetyFenceLane]", "Safety Net Fence Lane (part of the fence)" },
                { "Assets.DESCRIPTION[SkiSafetyFenceLane]", "The netting the safety net fence draws between its posts." },
                { "Assets.NAME[SkiSafetyFenceProbe]", "Ski Resort — fence probe: Chain-Link Test (delete after test)" },
                { "Assets.DESCRIPTION[SkiSafetyFenceProbe]", "A test fence for one field test. Remove it when the test is done." },

                // --- Run dressing (deployed; the runs place them along their edges): names pass 2026-10-13 ----------------------
                { "Assets.NAME[SkiSnowGunTowerLow]", "Fan Gun, Tower (Low)" },
                { "Assets.DESCRIPTION[SkiSnowGunTowerLow]", "A fan gun on a short mast. It blows water and air through a nozzle ring into a mist. Snowmaking and night runs place it by themselves." },
                { "Assets.NAME[SkiSnowGunTower]", "Fan Gun, Tower (Medium)" },
                { "Assets.DESCRIPTION[SkiSnowGunTower]", "A fan gun on a mast. It blows water and air through a nozzle ring into a mist. Snowmaking and night runs place it by themselves." },
                { "Assets.NAME[SkiSnowGunTowerTall]", "Fan Gun, Tower (Tall)" },
                { "Assets.DESCRIPTION[SkiSnowGunTowerTall]", "A fan gun on a tall mast. It blows water and air through a nozzle ring into a mist. Snowmaking and night runs place it by themselves." },
                { "Assets.NAME[SkiSnowGunFan]", "Fan Gun, Wheeled" },
                { "Assets.DESCRIPTION[SkiSnowGunFan]", "A fan gun on wheels, aimed up. It blows water and air through a nozzle ring into a mist. Snowmaking and night runs place it by themselves." },
                { "Assets.NAME[SkiSnowGunFanIdle]", "Fan Gun, Wheeled (Parked)" },
                { "Assets.DESCRIPTION[SkiSnowGunFanIdle]", "A fan gun on wheels, parked with its barrel down. Snowmaking and night runs place it by themselves." },
                { "Assets.NAME[SkiSnowGunLance]", "Snow Lance (Medium)" },
                { "Assets.DESCRIPTION[SkiSnowGunLance]", "A snow lance: a tall inclined aluminium tube with the nucleator at the head. Snowmaking and night runs place it by themselves." },
                { "Assets.NAME[SkiSnowGunLanceTall]", "Snow Lance (Tall)" },
                { "Assets.DESCRIPTION[SkiSnowGunLanceTall]", "A tall snow lance: an inclined aluminium tube with the nucleator at the head. Snowmaking and night runs place it by themselves." },
                { "Assets.NAME[SkiSnowGunLanceDown]", "Snow Lance (Folded)" },
                { "Assets.DESCRIPTION[SkiSnowGunLanceDown]", "A snow lance folded down. Snowmaking and night runs place it by themselves." },
                { "Assets.NAME[SkiHydrantBox]", "Snowmaking Hydrant Box" },
                { "Assets.DESCRIPTION[SkiHydrantBox]", "The water and air hookup for snow lances and fan guns, with a coiled hose. Snowmaking and night runs place these by themselves." },
                { "Assets.NAME[SkiHydrantBoxBare]", "Snowmaking Hydrant Box (No Hose)" },
                { "Assets.DESCRIPTION[SkiHydrantBoxBare]", "The water and air hookup for snow lances and fan guns. Snowmaking and night runs place these by themselves." },
                { "Assets.NAME[SkiPisteFloodlightLow]", "Ski Run Floodlight (Low)" },
                { "Assets.DESCRIPTION[SkiPisteFloodlightLow]", "A floodlight mast for night skiing. Night runs place floodlights by themselves." },
                { "Assets.NAME[SkiPisteFloodlight]", "Ski Run Floodlight (Medium)" },
                { "Assets.DESCRIPTION[SkiPisteFloodlight]", "A floodlight mast for night skiing. Night runs place floodlights by themselves." },
                { "Assets.NAME[SkiPisteFloodlightTall]", "Ski Run Floodlight (Tall)" },
                { "Assets.DESCRIPTION[SkiPisteFloodlightTall]", "A tall floodlight mast for night skiing. Night runs place floodlights by themselves." },
                { "Assets.NAME[SkiRunSnowmakingBorder]", "Snowmaking Run Edge Props (random group)" },
                { "Assets.DESCRIPTION[SkiRunSnowmakingBorder]", "Picks a random snow lance, fan gun, hydrant box or gap along a snowmaking run's edge." },
                { "Assets.NAME[SkiRunNightBorder]", "Night Run Edge Props (random group)" },
                { "Assets.DESCRIPTION[SkiRunNightBorder]", "Picks a random floodlight, snow lance, fan gun, hydrant box or gap along a night run's edge." },
                { "Assets.NAME[SkiPropNone]", "Empty Slot (used by run props)" },
                { "Assets.DESCRIPTION[SkiPropNone]", "Places nothing: run edges use it to leave gaps between their props." },

                // --- Runs -----------------------------------------------------------------
                // Round E1 (econ-round spec ch2 §4.6, D17): run asset names in Title Case like every
                // other asset name of ours ("Groomed Run", was "Groomed Ski Run"; "Snowmaking Run", was
                // "Ski Run with Snowmaking"; "Night Run", was "Night Skiing Run"); the natural run (D16)
                // is new. The hub says the sentence-case run types (SkiResort.Hub.RUN_TYPE[*], block (e)).
                { "Assets.NAME[SkiRunGroomedA]", "Groomed Run" },
                {
                    "Assets.DESCRIPTION[SkiRunGroomedA]",
                    "Freeform ski run. Paint the trail within 5 km of a Ski Area Welcome " +
                    "Sign; grooming appears on the mountain and counts toward the " +
                    "resort's attractiveness."
                },
                { "Assets.NAME[SkiRunSnowmakingA]", "Snowmaking Run" },
                {
                    "Assets.DESCRIPTION[SkiRunSnowmakingA]",
                    "A groomed run with snowmaking coverage — holds its season longer and " +
                    "scores attractiveness in marginal weather. Operates within 5 km of a " +
                    "Ski Area Welcome Sign."
                },
                { "Assets.NAME[SkiRunNightA]", "Night Run" },
                {
                    "Assets.DESCRIPTION[SkiRunNightA]",
                    "A snowmaking run with floodlighting for a second, after-dark shift — " +
                    "the most expensive trail to keep open, and the biggest draw. Operates " +
                    "within 5 km of a Ski Area Welcome Sign."
                },
                { "Assets.NAME[SkiRunNaturalA]", "Natural Run" },
                {
                    "Assets.DESCRIPTION[SkiRunNaturalA]",
                    "A marked trail on natural snow. Nothing is groomed or painted: the mountain's " +
                    "own snow is the run, and it opens only when real snow covers it. No monthly " +
                    "cost. Cut the trail through the forest with the tree tool. Links to the nearest " +
                    "Ski Area Welcome Sign within 5 km."
                },

                // --- Run drawing feedback (docs/research/run-feedback-spec.md §5, §9.6) -------
                // Mouse tooltip of SkiRunTooltipSystem while the area tool draws or edits a
                // run. Read through LocalizedString.Id(key) (LocalizedString.cs:39-42); keys
                // are free-form strings, the bracketed band suffix = SlopeBand member name.
                // The pitch / drop / patch labels sit in front of IntTooltip values whose units ("percentage",
                // "length") the UI formats, so they carry no number or unit themselves.
                // R2 (DECISIONS 116): the rating is the crux of the easiest line, so
                // STEEPEST_PITCH ("Steepest 20 m pitch") is retired in favour of CRUX_PITCH;
                // STEEPEST_PATCH labels the old statistic when it is shown as an avoidable patch.
                // APPROX_PITCH (R2 fixer round) replaces CRUX_PITCH when the run outline falls
                // apart (Readout.Connected false): the number is then the P90 fallback, not an
                // unavoidable pitch. Bands follow the amended thresholds 6 / 25 / 40 / 60 % (spec §9.2).
                // U2 (hub-u2-spec.md §2.4 "Changed"): Flat no longer says "unrated" — band -1 in the hub is
                // "Being rated" (SkiResort.Hub.PENDING), so no two states share the word.
                // Round E1 (econ-round spec ch2 §4.6, §6.4, D21): RUN_FEE labels the one-time run fee row
                // (a money IntTooltip value) of SkiRunTooltipSystem.
                { "SkiResort.SlopeBand[Flat]", "Flat (too gentle to rate)" },
                { "SkiResort.SlopeBand[Green]", "Green circle" },
                { "SkiResort.SlopeBand[Blue]", "Blue square" },
                { "SkiResort.SlopeBand[Black]", "Black diamond" },
                { "SkiResort.SlopeBand[DoubleBlack]", "Double black diamond" },
                { "SkiResort.Tooltip.CRUX_PITCH", "Hardest unavoidable pitch" },
                { "SkiResort.Tooltip.APPROX_PITCH", "Typical steep pitch (run outline broken, rating approximate)" },
                { "SkiResort.Tooltip.VERTICAL_DROP", "Vertical drop" },
                { "SkiResort.Tooltip.STEEPEST_PATCH", "Steepest patch (avoidable)" },
                // T35 / T44 (2026-10-16): the drawing tooltip's live grade; AD / AP = average in degrees / percent, SD / SP = steepest.
                { "SkiResort.Tooltip.SLOPE_AVG_MAX", "Avg {AD} ({AP}) · steepest {SD} ({SP})" },
                // T44 (2026-10-16): the same line plus the deciding factor of the difficulty layer (SkiRunRater.NoteLine). W = width in
                // metres (unit "length", converted by the UI), SS / ST = steep share and its threshold in percent.
                { "SkiResort.Tooltip.SLOPE_AVG_MAX_NARROW", "Avg {AD} ({AP}) · steepest {SD} ({SP}) · narrow {W} at the crux" },
                { "SkiResort.Tooltip.SLOPE_AVG_MAX_WIDE", "Avg {AD} ({AP}) · steepest {SD} ({SP}) · wide {W} at the crux" },
                { "SkiResort.Tooltip.SLOPE_AVG_MAX_SHARE", "Avg {AD} ({AP}) · steepest {SD} ({SP}) · only {SS} of the run is steeper than {ST}" },
                { "SkiResort.Tooltip.SLOPE_AVG_MAX_GENTLE", "Avg {AD} ({AP}) · steepest {SD} ({SP}) · gentle run, short steep pitch" },
                { "SkiResort.Tooltip.RUN_FEE", "Run fee (one-time)" },
                {
                    "SkiResort.Tooltip.NO_GATE_IN_RANGE",
                    "No Ski Area Welcome Sign within 5 km — this run will stay unbound " +
                    "until one is placed"
                },
                // R3 (docs/research/run-binding-glade-spec.md §2 "Locale keys", §5.1): the run's
                // horizontal length, an IntTooltip label in front of a "length" unit value
                // (SkiRunTooltipSystem.KeyLength).
                { "SkiResort.Tooltip.RUN_LENGTH", "Length" },

                // --- Glade tool tooltip (run-binding-glade-spec.md §2 "Locale keys", §4.9, §8.3) --
                // Read by SkiGladeTooltipSystem through LocalizedString.Id(key), built once in
                // OnCreate. Round G3 retired MODE[Corridor|Glade|TreeLine] and VARIANT and added
                // TARGET[Interior|TreeLine], LEVEL, LEVEL_NAME[1..8] (the names of spec §8.2's
                // table, also SkiGladeToolSystem.Tuning.LevelNames for the log), TREES_NOW and
                // TREES_TARGET. PLANT / FELL / REMOVE / BLOCKED_SPOTS / LEVEL / TREES_NOW /
                // TREES_TARGET label IntTooltips (unit "integer"), so they carry no number
                // themselves. Texts are the spec's (final unless Richard edits them), except
                // REMOVE: G2's "Planted trees to replace" became "Planted trees to remove" (G3
                // removes members to lower the density; nothing is replaced), and COMPLETE: "this
                // layout is already planted" became "the run is already at this tree density"
                // (level 1 plants nothing). G3 fixer round (findings tool-2 / behaviour-2; texts
                // are the fixer's, Richard may edit them): COMPLETE_BLOCKED for a complete plan
                // below its target (learned blocked spots used up the stream) and COMPLETE_FIXED
                // for one above it (the trees left inside may not be felled), so COMPLETE is shown
                // only when Trees now = Target.
                { "SkiResort.GladeTooltip.HINT", "Hover a ski run to plan trees" },
                { "SkiResort.GladeTooltip.NO_RUNS", "No ski run in this city is linked to a Welcome Sign yet" },
                { "SkiResort.GladeTooltip.UNBOUND", "Run not linked: a Ski Area Welcome Sign must stand within 5 km of its centre" },
                { "SkiResort.GladeTooltip.BLOCKED", "Trees cannot be planned on this run (see the mod log)" },
                { "SkiResort.GladeTooltip.COMPLETE", "Nothing to change: the run is already at this tree density" },
                { "SkiResort.GladeTooltip.COMPLETE_BLOCKED", "Nothing more can be planted: the remaining spots are blocked (roads, buildings)" },
                { "SkiResort.GladeTooltip.COMPLETE_FIXED", "Nothing to change: the remaining trees cannot be felled (other runs', buildings')" },
                { "SkiResort.GladeTooltip.CLAMPED", "Large run: apply again to continue" },
                { "SkiResort.GladeTooltip.SATURATED", "No room for more trees" },
                { "SkiResort.GladeTooltip.MONEY", "Some trees cancelled — not enough money?" },
                { "SkiResort.GladeTooltip.TARGET[Interior]", "Run interior" },
                { "SkiResort.GladeTooltip.TARGET[TreeLine]", "Tree line" },
                { "SkiResort.GladeTooltip.LEVEL", "Tree density (1-8)" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[1]", "Clear" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[2]", "Scattered trees" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[3]", "Sparse" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[4]", "Open glade" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[5]", "Glade" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[6]", "Open forest" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[7]", "Forest" },
                { "SkiResort.GladeTooltip.LEVEL_NAME[8]", "Thick forest" },
                { "SkiResort.GladeTooltip.TREES_NOW", "Trees now" },
                { "SkiResort.GladeTooltip.TREES_TARGET", "Target" },
                { "SkiResort.GladeTooltip.PLANT", "Trees to plant" },
                { "SkiResort.GladeTooltip.FELL", "Wild trees to fell" },
                { "SkiResort.GladeTooltip.REMOVE", "Planted trees to remove" },
                { "SkiResort.GladeTooltip.BLOCKED_SPOTS", "Spots blocked (roads, buildings)" },

                // --- Ski Resort hub (docs/research/resort-hub-understand.md §5.3 last paragraph,
                // Round U1, implementer U1-B, 2026-10-01) ----------------------------------------
                // The UI module (mod/SkiResortUI) renders every SkiResort.Hub.* key through
                // translate(key, englishFallback); each text below MUST equal its TS fallback (the
                // fallback shows only while a key is missing). GLYPHS: printable ASCII plus the
                // research §2.F.2 safe list only (← → ↑ ↓ … · – × ° ² ≈ NBSP; NO em dash):
                // mod/SkiResortUI/tools/check-bundle.js fails the UI build on any other glyph in a
                // SkiResort.Hub.* value of this file. Editing this file re-runs the UI build
                // (it is an input of the csproj's SkiUiBuild target).
                //
                // (a) src/l10n.ts LOC (U1-A): launcher, connection and error texts.
                { "SkiResort.Hub.TITLE", "Ski Resort" },
                // CONNECTED, CONTRACT_MISMATCH: retired in contract 2 (no longer used by TS or C#; the mismatch
                // text is CONTRACT_MISMATCH_CORE; integrator 2026-10-03, check-bundle rule 4 warned on both).
                { "SkiResort.Hub.LAUNCHER_TOOLTIP", "Ski Resort: runs, resort totals and every building tool" },
                { "SkiResort.Hub.NOT_CONNECTED", "The mod is not answering: its hub system is missing or failed. See SkiResortMod.Mod.log." },
                { "SkiResort.Hub.STAMP_DIFFERS", "The UI bundle comes from a different build than the DLL." },
                { "SkiResort.Hub.UI_ERROR", "The Ski Resort panel hit an error and was closed. Details are in the mod log." },

                // (b) Keys the C# side SENDS (SkiResortHubUISystem / SkiRunCatalogSystem /
                // SkiGladeToolSystem). RUN_DEFAULT[*]: the default run names of OD7, read by
                // SkiRunCatalogSystem from the ACTIVE dictionary (so a translation applies) with
                // "{N}" replaced by the run's number by top height; the UI receives the finished
                // string as RunRow.name. RUN_DEFAULT[Natural] is new in Round E1 (ch2 §4.6, ch5 R-UI-2;
                // the catalog's English fallback "Natural run {N}" is the same text). REASON[*]:
                // build[].reason; GLADE_REASON[*]: glade.reason. HINT[LiftNoPair]: build[lift].hint for
                // the SHOWN area (D37); its text changed in Round E1 (ch5 R-UI-2) and it moved here from
                // block (d).
                { "SkiResort.Hub.RUN_DEFAULT[Groomed]", "Groomed run {N}" },
                { "SkiResort.Hub.RUN_DEFAULT[Snowmaking]", "Snowmaking run {N}" },
                { "SkiResort.Hub.RUN_DEFAULT[Night]", "Night run {N}" },
                { "SkiResort.Hub.RUN_DEFAULT[Natural]", "Natural run {N}" },
                { "SkiResort.Hub.REASON[Locked]", "Not unlocked yet" },
                { "SkiResort.Hub.REASON[NoSign]", "Place a Ski Area Welcome Sign first" },
                { "SkiResort.Hub.REASON[Missing]", "Asset not found: is the Modular Ski Resort asset pack installed?" },
                // Catalog Phase B (round-2-contract §8.3, reason byte 4; SkiResortHubUISystem ReasonKeyAlreadyBuilt): the SHOWN
                // ski area's sign already has the upgrade. (Reason 5 / REASON[OneLuxury] was removed 2026-10-11: no lodge limit.)
                { "SkiResort.Hub.REASON[AlreadyBuilt]", "Already built at this ski area" },
                { "SkiResort.Hub.HINT[LiftNoPair]", "Place the other station first: a lift needs a base and a top station." },
                { "SkiResort.Hub.GLADE_REASON[Disabled]", "The tree tool hit an error and is off until the next load. Details are in the mod log." },
                { "SkiResort.Hub.GLADE_REASON[NotInGame]", "The tree tool works in a loaded city only." },
                { "SkiResort.Hub.GLADE_REASON[NoRuns]", "The ski run assets were not found: is the asset pack installed?" },
                { "SkiResort.Hub.GLADE_REASON[NoSpecies]", "No tree species to plant were found." },

                // (c) src/hub/hubText.ts HUB_LOC (U1-C, the panel), copied as of 2026-10-01 17:26
                // while U1-C was still writing it — the integrator re-diffs hubText.ts against this
                // block before the build. Contract 2 (integrator 2026-10-03, re-diffed by check-bundle rule 4):
                // SECTION[Resort], SECTION[Build], COL_AREA_TIP, RUNS_UNBOUND_HEADER and RUNS_UNBOUND_HINT are
                // retired — the dashboard, the Build footer and the resort switcher's Not linked entry replace them.
                // Contract 3 (Round E1, ch6 §7.4): SECTION[Runs], NO_SIGN, BUILD_HINT_RUN, TREES_TOOL_OFF,
                // TREES_TOOL_ON and RUN_FLY retired; RUNS_EMPTY's text changed (ch6 §7.2).
                { "SkiResort.Hub.SECTION[Trees]", "Trees" },
                { "SkiResort.Hub.SECTION_FAILED", "This part of the panel hit an error. Details are in the mod log." },
                { "SkiResort.Hub.PLACE_SIGN", "Place Welcome Sign" },
                // SIGNS, RUNS_BOUND, RUNS_UNBOUND, LIFTS: removed in contract 2 (hub-u2-spec.md §2.4
                // "Removed": the `summary` binding they labelled is gone; the resort rows replace it).
                { "SkiResort.Hub.AREA", "Skiable area" },
                { "SkiResort.Hub.BUILD_EMPTY", "No building tools were reported. See SkiResortMod.Mod.log." },
                { "SkiResort.Hub.BUILD_ACTIVE", "In use: click again to stop" },
                // Round F6 (C4): the hub shows a ski area -> the lodge goes on its sign (T12: the camera no longer flies
                // there first; SkiResortHubUISystem lodge launch, item A2); BUILD_HINT_LODGE_SELECTED below
                // is the Not linked / mismatch text (src/hub/BuildStrip.tsx picks one by FooterInfo.lodgeShown).
                { "SkiResort.Hub.BUILD_HINT_LODGE", "Goes near this ski area's Welcome Sign." },
                { "SkiResort.Hub.BUILD_HINT_LODGE_SELECTED", "Goes on the selected Welcome Sign, or the nearest one." },
                { "SkiResort.Hub.LOCKED", "Not available yet" },
                { "SkiResort.Hub.TREES_FEWER", "Fewer trees" },
                { "SkiResort.Hub.TREES_MORE", "More trees" },
                { "SkiResort.Hub.TREES_APPLY_TO", "Apply to" },
                { "SkiResort.Hub.TREES_UNAVAILABLE", "The tree tool is not available right now." },
                { "SkiResort.Hub.RUNS_EMPTY", "No runs yet. Pick a run type in the Build strip below and draw its outline." },
                { "SkiResort.Hub.COL_LENGTH", "Length" },
                { "SkiResort.Hub.COL_DROP", "Drop" },
                { "SkiResort.Hub.COL_AREA", "Area" },
                { "SkiResort.Hub.COL_LENGTH_TIP", "Horizontal length, estimated from the run's outline" },
                { "SkiResort.Hub.COL_DROP_TIP", "Height difference between the highest and lowest points of the run's outline (approximate)" },

                // (d) Contract 2 (docs/research/hub-u2-spec.md §2.4, Rounds U2/L1/M1, implementer I-4,
                // 2026-10-03). Texts are the spec's, verbatim (final unless Richard edits them); the TS
                // fallbacks (I-3) carry the same text and tools/check-bundle.js rule 4 fails the UI build
                // on any difference. Keys with {X} placeholders are rendered by LocalizedString in TS, never
                // through translate() (gate rule 5), except RESORT_DEFAULT and LIFT_DEFAULT, which C#
                // formats (SkiResortStatsSystem / SkiRunBindTriggerSystem, "{N}" replaced) and sends as a
                // finished name.
                // Contract 3 (Round E1, ch6 §7.4) retired the contract-2 dashboard and list keys (KPI[*],
                // KPI_TIP[*], LODGE_NO_RUNS, NO_LODGE, NO_LODGE_TIP, LIFTS_OPEN, DETAILS, DETAIL[*] but Share,
                // DETAIL_TIP[*], SEASON[*], EST_TIP, CITY_TIP, PAUSED_TIP, TERM[*], TERM_CAP, SNOW_LINE,
                // AIR_TEMP, LODGE_N, TAB[Lifts], FOCUS_HINT, COL_RISE, COL_RISE_TIP, LIFT_STATE[*],
                // BUILD_ORDER, BUILD_PRICE, TREES_IDLE): the five tiles and views of hub v3 replace them.
                // RESORT_PREV, RESORT_NEXT, UNLINKED_ENTRY ("ski area", D17 wording), LIFT_CAPACITY and
                // BUILD_HINT_SIGN changed in place (ch6 §7.2).
                // Resort switcher (title bar, §4.3).
                { "SkiResort.Hub.RESORT_PREV", "Previous ski area" },
                { "SkiResort.Hub.RESORT_NEXT", "Next ski area" },
                { "SkiResort.Hub.RESORT_POSITION", "{N} / {COUNT}" },
                { "SkiResort.Hub.RESORT_DEFAULT", "Ski area {N}" },
                { "SkiResort.Hub.RESORT_FLY", "Fly to the Welcome Sign" },
                { "SkiResort.Hub.UNLINKED_ENTRY", "Not linked to a ski area" },
                { "SkiResort.Hub.UNLINKED_HINT", "More than 5 km from every Welcome Sign. Place a sign nearby to link them." },
                { "SkiResort.Hub.RENAME", "Rename" },
                { "SkiResort.Hub.RENAME_TIP", "Enter or clicking away keeps the name, Esc cancels, an empty name restores the default." },
                // Kept from the contract-2 dashboard (now the Overview's mountain block and receipt).
                { "SkiResort.Hub.DIFFICULTY", "Difficulty" },
                { "SkiResort.Hub.PENDING", "Being rated" },
                // Fixer round (findings u2-data-05 / u2-panel-5): band -2, a rating that ended in no terrain /
                // too large / failed — final, so it must not read "Being rated" forever.
                { "SkiResort.Hub.UNRATEABLE", "Cannot be rated (outline too large or too thin, or no terrain under it)" },
                { "SkiResort.Hub.DETAIL[Share]", "Share of the city's tourist draw" },
                // Round F6 (C3, attraction-honesty D8): the target-tourists row's own tooltip.
                { "SkiResort.Hub.EST_TOURISTS_TIP", "Worked out with the game's own tourist formula, not counted. Tourism mods can replace that formula, so real arrivals may differ." },
                // Lists (now the Runs and Lifts views).
                { "SkiResort.Hub.LIFTS_EMPTY", "No lifts yet. Place a base station and a top station facing each other, then draw the lift line between them." },
                { "SkiResort.Hub.LIFT_DEFAULT", "Lift {N}" },
                { "SkiResort.Hub.LIFT_SECTIONS", "Sections" },
                { "SkiResort.Hub.LIFT_TOWERS", "Towers" },
                { "SkiResort.Hub.LIFT_CHAIRS", "Chairs" },
                // Fixer round (finding u2-data-06): chairs run on the simulation clock, so the hour is one at
                // normal speed (spec §2.4's "real time" was wrong at 2× / 3× and while paused). Round E1: a
                // design figure (D34 honest labels; the game does not simulate riders).
                { "SkiResort.Hub.LIFT_CAPACITY", "Design capacity (riders an hour at 1× game speed)" },
                { "SkiResort.Hub.LIFT_TOWER_REMOVED", "A tower was removed by another tool. Redraw a section next to it to restore it." },
                { "SkiResort.Hub.LIFT_STRAIGHT", "Stations straight (180°)" },
                // Fixer round (finding behaviour-4): a lift without a base station stores its name on the top.
                { "SkiResort.Hub.LIFT_NAME_TIP", "The name is stored on the base station (on the top station while the lift has no base)" },
                { "SkiResort.Hub.LIFT_SIGN_TIP", "Lifts belong to the nearest Welcome Sign within 5 km of their base station; runs keep the sign they were drawn for" },
                // Build strip (§4.2 BuildStrip, §5.8).
                { "SkiResort.Hub.BUILD_HINT_SIGN", "The sign is the ski area: runs and lifts within 5 km belong to it." },
                { "SkiResort.Hub.BUILD_HINT_BASE", "Place at the bottom of the lift." },
                { "SkiResort.Hub.BUILD_HINT_TOP", "Place at the top, facing the base station. A guide shows when it is straight." },
                { "SkiResort.Hub.BUILD_HINT_LIFT", "Draw from station to station. On steep ground, redraw one section at a time." },
                // Round F6 (C4): an extra TOOLTIP line of the base, top and lift tiles (the footer's two
                // one-string lines would cut it at 130-150 % text; src/hub/BuildStrip.tsx header).
                { "SkiResort.Hub.BUILD_HINT_LIFT_SIGN", "Lifts join the nearest Welcome Sign within 5 km of their base station." },
                // The link states (§4.5).
                { "SkiResort.Hub.CONTRACT_MISMATCH_CORE", "This panel (version {UI}) and the mod's DLL (version {DLL}) come from different builds. Building still works; the resort statistics are hidden until the mod is reinstalled." },

                // (e) Contract 3 (Round E1, econ-round spec ch6 §7): NEW hub keys; each text equals its hubText.ts tuple (gate R4).
                // One line per NEW row of ch6 §7.2, in that order (= src/hub/hubText.ts order): HUB_LOC, then
                // HUB_FMT (placeholder entries: rendered only by LocalizedString), then the family maps
                // STATUS_WORD, SEASON_HINT, SEASON_TIP, LIFT_STATUS, RUN_TYPE, BUILD_SHORT[run kinds], NEXT, NEXT2.
                // Texts are final unless Richard edits them (DECISIONS 132: the owner reads new player text).
                { "SkiResort.Hub.START_TITLE", "Start a ski area" },
                { "SkiResort.Hub.START_STEP[1]", "Place a Welcome Sign" },
                { "SkiResort.Hub.START_STEP[2]", "Connect the sign: road, power, water and sewage" },
                { "SkiResort.Hub.START_STEP[3]", "Add a lodge to the sign" },
                { "SkiResort.Hub.START_STEP[4]", "Build a lift: two stations, then the line" },
                { "SkiResort.Hub.START_STEP[5]", "Draw runs from the top of the lift" },
                // START_NOTE retired in Round F7 (D6): footer line 2 (BUILD_HINT_SIGN) says it.
                { "SkiResort.Hub.STATUS_NO_RUNS", "Closed · no runs yet" },
                { "SkiResort.Hub.STATUS_UNLINKED", "Not linked to a ski area" },
                { "SkiResort.Hub.STATUS_TIP_RULE", "Open: 15 cm or more of snow covers 60 % of the run (31 cm on natural black runs). Closed: below 40 %. In between: patchy." },
                { "SkiResort.Hub.STATUS_TIP_MEASURED", "Snow is measured on each run from the game's own snow." },
                { "SkiResort.Hub.STATUS_TIP_ESTIMATED", "Some runs are estimated from the day's temperature until their snow can be measured." },
                { "SkiResort.Hub.STATUS_TIP_GATING", "Snow & season gating is off in Options, so every run counts as open." },
                { "SkiResort.Hub.STATUS_TIP_VISUALS", "Snow is hidden in your Gameplay settings. Runs still open on the simulated snow." },
                // Round S1 (D29): the status line's hint slot at powder >= 0.5.
                { "SkiResort.Hub.POWDER_DAY", "Powder day" },
                { "SkiResort.Hub.PROBLEM[None]", "Nothing needs your attention." },
                { "SkiResort.Hub.PROBLEM[Unlinked]", "More than 5 km from every Welcome Sign" },
                { "SkiResort.Hub.PROBLEM[NoLodge]", "No lodge: guests have nowhere to arrive" },
                { "SkiResort.Hub.PROBLEM[LodgesAllOff]", "All lodges are switched off" },
                { "SkiResort.Hub.PROBLEM[NoRuns]", "No runs yet: draw one to open" },
                // Round F7 (D5): the two road rows (C# HubProblemCode SignNoRoad 18, LodgeNoRoad 19).
                { "SkiResort.Hub.PROBLEM[SignNoRoad]", "Welcome Sign is not at a road" },
                { "SkiResort.Hub.PROBLEM[LodgeNoRoad]", "Lodge is not at a road: guests cannot reach it" },
                // Round S1 (D28): the ski-day rows (C# HubProblemCode SkiDaysCannotReach 20, NoParkingNearLodge 21,
                // LodgeNotSlopeside 22). Round M1 (D1): SkiDaysCannotReach's row and tip reworded (header).
                { "SkiResort.Hub.PROBLEM[SkiDaysCannotReach]", "Many ski-day guests with a car never arrive" },
                { "SkiResort.Hub.PROBLEM[NoParkingNearLodge]", "No parking near the lodge" },
                { "SkiResort.Hub.PROBLEM[LodgeNotSlopeside]", "Lodge is far from the runs" },
                // Catalog Phase B (D13): C# HubProblemCode SchoolNoGreenRun 25 (23 and 24 carry a placeholder: block (e) FMT).
                { "SkiResort.Hub.PROBLEM[SchoolNoGreenRun]", "The ski school has no green run" },
                { "SkiResort.Hub.PROBLEM_TIP[NoLodge]", "Guests arrive at a lodge, so a ski area without one has an attraction of 0. Place a lodge near the Welcome Sign with the lodge tile in the Build strip." },
                { "SkiResort.Hub.PROBLEM_TIP[LodgesAllOff]", "A lodge that is switched off does not count. With none left, guests have nowhere to arrive and the attraction is 0. Switch one on in the Welcome Sign's info panel (Upgrades)." },
                { "SkiResort.Hub.PROBLEM_TIP[LodgesSomeOff]", "A lodge that is switched off does not count in the lodge base." },
                // Round F7 (D4): no longer tells the player to connect a sign that is connected.
                // Round M2 (R5): changed in place; names garbage and points at the factor rows below it.
                { "SkiResort.Hub.PROBLEM_TIP[Efficiency]", "Efficiency as on the Welcome Sign's info panel: staff, power, water, sewage, garbage, budget and more. Any cause costing 5 % or more is listed below." },
                // Phase D (round-4-contract §4.4, DECISIONS 171 rulings 1-3, skeptic MUST-1; was "Its base station gets no
                // electricity, so the lift adds nothing and its runs count as having no lift. ..."): the lift stops, serves no
                // run, and its upkeep is still billed (power never lowers it; no "full": the NoOpenRun quarter can still apply,
                // round-4 fixer); power is the base station's only (CG-T8).
                { "SkiResort.Hub.PROBLEM_TIP[LiftNoPower]", "No electricity at its base station: the lift stops and serves no run, but maintenance is still charged. Connect a low-voltage line to it." },
                { "SkiResort.Hub.PROBLEM_TIP[LiftSectionMissing]", "A section of the line is being redrawn or was removed. The lift counts again as soon as the line is closed; until then it costs a quarter of its running cost." },
                { "SkiResort.Hub.PROBLEM_TIP[LiftUnfinished]", "The line does not join a base station and a top station yet." },
                { "SkiResort.Hub.PROBLEM_TIP[NoRuns]", "Pick a run type in the Build strip and draw its outline within 5 km of the Welcome Sign." },
                { "SkiResort.Hub.PROBLEM_TIP[Stations]", "A station is placed, but no line joins it to its partner yet." },
                { "SkiResort.Hub.PROBLEM_TIP[LiftChairsStopped]", "The lift is complete and still counts, but its chairs stopped. The mod log says why." },
                { "SkiResort.Hub.PROBLEM_TIP[RunsUnrateable]", "Their outline is too large or too thin, or there is no terrain under it. Such a run counts very little." },
                { "SkiResort.Hub.PROBLEM_TIP[RunsRating]", "Their difficulty is being worked out, one run at a time. A new run counts 0 until then; a reshaped run keeps its last rating." },
                { "SkiResort.Hub.PROBLEM_TIP[RunsFlat]", "A run rated Flat adds nothing to the score. Draw it on steeper ground." },
                { "SkiResort.Hub.PROBLEM_TIP[FreeLodges]", "These lodges are not on a Welcome Sign, so they count nowhere." },
                // Round F7 (D5): the tooltip's first line is the row ("Welcome Sign is not at a road" /
                // "Lodge is not at a road: ..."), so "it" names the building; the fix hints come from
                // findings/sign-utilities.md §5 and ped-access.md §2.4 / §4.2 (the deck side is the lot front;
                // the game links a road within 8.4 m of the front point).
                { "SkiResort.Hub.PROBLEM_TIP[SignNoRoad]", "Move it so its front touches a road. Power, water and sewage come from that road." },
                { "SkiResort.Hub.PROBLEM_TIP[LodgeNoRoad]", "Move it so its deck side touches a road. Guests walk in from that road." },
                // Round S1 (D28): 300 m is D26's census radius, 800 m D10's slopeside distance.
                { "SkiResort.Hub.PROBLEM_TIP[SkiDaysCannotReach]", "Many recent ski-day guests with a car did not reach the lodge in time. Fewer are sent until more arrive." },
                { "SkiResort.Hub.PROBLEM_TIP[NoParkingNearLodge]", "No car spaces within 300 m of the lodge. Guests who drive need a parking lot or a street with parking." },
                { "SkiResort.Hub.PROBLEM_TIP[LodgeNotSlopeside]", "Ski-day guests only go to a lodge within 800 m of this ski area's runs or lift stations." },
                // Catalog Phase B: C# HubProblemCode 23-25 (D18 mixed types; D11 summit-served 300 m = SkiCatalog.Ladder
                // .SummitServedRadiusM, CG-T6; D13 the school's green run).
                { "SkiResort.Hub.PROBLEM_TIP[MixedLiftTypes]", "A line and the stations it joins must be one kind of lift. Redraw the line as the stations' kind, or move its end off the other lift's station." },
                { "SkiResort.Hub.PROBLEM_TIP[SummitLodgeNotLiftServed]", "In summer a summit lodge counts more only within 300 m of a working lift's top station." },
                { "SkiResort.Hub.PROBLEM_TIP[SchoolNoGreenRun]", "The school adds to the score only while a green run counts." },
                { "SkiResort.Hub.PROBLEM_MORE_TIP", "Also needs attention:" },
                { "SkiResort.Hub.PROBLEM_CLICK_FLY", "Click to fly there." },
                { "SkiResort.Hub.PROBLEM_CLICK_LIST", "Click to open the list." },
                { "SkiResort.Hub.TILE[Attraction]", "Attraction" },
                { "SkiResort.Hub.TILE[Runs]", "Runs" },
                { "SkiResort.Hub.TILE[RunsOpen]", "Runs open" },
                { "SkiResort.Hub.TILE[Lifts]", "Lifts" },
                { "SkiResort.Hub.TILE[Guests]", "Guests" },
                { "SkiResort.Hub.TILE[Money]", "Money" },
                { "SkiResort.Hub.TILE_TIP[Attraction]", "Its score on the game's scale: a large park is about 10, a landmark 120. Lodges carry it; their own panels show 0." },
                { "SkiResort.Hub.TILE_TIP_HELD", "Checking runs and snow after loading. The game keeps the saved score meanwhile." },
                { "SkiResort.Hub.TILE_TIP[Runs]", "Runs fully open now, of all runs of this ski area." },
                // Phase D (round-4-contract §4.4; was "... Without power the chairs still move, but the lift adds nothing."): the
                // carriers stop without power (ChairPinSystem hold, the tram Held), CG-T8.
                { "SkiResort.Hub.TILE_TIP[Lifts]", "Lifts that count (complete, with power at the base station), of the complete lifts. Without power a lift stops and adds nothing." },
                { "SkiResort.Hub.TILE_TIP[Guests]", "Guests who arrived this month: tourists at the lodges and the Welcome Sign, and residents visiting a lodge." },
                { "SkiResort.Hub.TILE_TIP_SINCE_LOAD", "Counted since you loaded the city." },
                { "SkiResort.Hub.TILE_TIP[Money]", "Resort money a month: lift tickets minus running costs and the game's bill for buildings and wages, at today's rates. What tourists spend in your city's shops and hotels is not included." },
                { "SkiResort.Hub.TILE_TIP_EST", "≈ estimated: less than a full month of lift tickets has been counted." },
                { "SkiResort.Hub.TILE_TIP_EST_BUILDINGS", "≈ The game's bill for buildings and wages is not known yet." },
                { "SkiResort.Hub.TILE_TIP_OPEN", "Click to show it below." },
                { "SkiResort.Hub.SECTION[Attraction]", "Attraction" },
                { "SkiResort.Hub.SCORE_TICK[Attraction]", "tourist attraction 50" },
                { "SkiResort.Hub.SCORE_TICK[Landmark]", "landmark 120" },
                { "SkiResort.Hub.RECEIPT[LodgeBase]", "Lodge base" },
                { "SkiResort.Hub.RECEIPT[LodgeBaseHalf]", "Lodge base (half until a run counts)" },
                { "SkiResort.Hub.RECEIPT[LodgeBaseNone]", "Lodge base (no lodge)" },
                // Catalog Phase B (R11 ladder; CG-T5: "up to <Ladder.ExtraCap>"): changed in place.
                { "SkiResort.Hub.RECEIPT_TIP[LodgeBase]", "The best lodge's base, plus each other lodge's extra, up to 24 more. Switched-off lodges do not count. While no run counts: half, more for a summit lodge a lift reaches." },
                { "SkiResort.Hub.RECEIPT[Terrain]", "Runs and lifts" },
                { "SkiResort.Hub.RECEIPT_TIP[Runs]", "Runs: the sum of each run's value now (difficulty, size, type, lift and snow). Each run's tooltip in the Runs view shows its own." },
                // Catalog Phase B (R12; CG-T4: every Loop type's "<name> <ScoreValue>" in index order and the summer-share words):
                // changed in place.
                { "SkiResort.Hub.RECEIPT_TIP[Lifts]", "Lifts: each complete lift with power adds its kind's value: fixed-grip 6, detachable 15, gondola 25. While no run counts: chairlifts a quarter, gondolas three quarters." },
                // Catalog Phase B: the school's line of the receipt's parts tooltip (shown while ResortLive.schoolValue > 0).
                { "SkiResort.Hub.RECEIPT_TIP[School]", "School: a ski school on the Welcome Sign adds its value while a green run counts." },
                { "SkiResort.Hub.RECEIPT_TIP[Variety]", "Variety: 15 % more while a green, a blue and a black (or harder) run all count." },
                { "SkiResort.Hub.RECEIPT_OPEN_NONE", "Runs open now: –" },
                { "SkiResort.Hub.RECEIPT[Efficiency]", "Welcome Sign efficiency" },
                // Round M2 (R5): changed in place, "garbage" added (one wording across the hub).
                { "SkiResort.Hub.RECEIPT_TIP[Efficiency]", "The Welcome Sign's efficiency, as on its info panel: staff, power, water, sewage, garbage, budget and more. The score uses the day's average, at most 100 %." },
                { "SkiResort.Hub.RECEIPT[Result]", "Attraction" },
                { "SkiResort.Hub.RECEIPT[ResultNoLodge]", "Attraction (no lodge: 0)" },
                { "SkiResort.Hub.RECEIPT_TIP[Result]", "(Lodge base + runs and lifts) × efficiency, at most 200. The active lodges carry it in equal shares; a lodge's own panel shows 0." },
                { "SkiResort.Hub.SHARE_TIP", "This ski area's share of the city's attraction, counted the game's way: the square of each building's score. It sets how many tourists come, not where they go." },
                { "SkiResort.Hub.SECTION[Mountain]", "Mountain" },
                { "SkiResort.Hub.MOUNTAIN[Vertical]", "Vertical" },
                { "SkiResort.Hub.MOUNTAIN_TIP[Vertical]", "From the lowest run bottom to the highest run top. ≈ while runs are being rated." },
                { "SkiResort.Hub.AREA_TIP", "The plan area of all runs of this ski area." },
                { "SkiResort.Hub.MOUNTAIN[Snow]", "Snow now" },
                { "SkiResort.Hub.MOUNTAIN[SnowMeasured]", "Snow now (measured)" },
                { "SkiResort.Hub.MOUNTAIN[SnowEstimated]", "Snow now (estimated)" },
                { "SkiResort.Hub.MOUNTAIN_TIP[Snow]", "Share of the run area with at least 15 cm of snow (31 cm on natural black runs)." },
                { "SkiResort.Hub.UNLINKED_NOTE", "These runs and lifts belong to no ski area, so they score nothing and draw no guests." },
                { "SkiResort.Hub.TREE_TOOL", "Tree tool" },
                { "SkiResort.Hub.TREE_TOOL_TIP", "Plan trees on and around your runs: turn the tool on, hover a run and click." },
                { "SkiResort.Hub.TREE_TOOL_TIP_ON", "The tree tool is on. Click to turn it off." },
                { "SkiResort.Hub.PISTE_MAP", "Piste map" },
                { "SkiResort.Hub.PISTE_MAP_TIP", "Draws this ski area's runs on the ground, in difficulty colours, while the panel is open. Natural: dashed. Too flat: grey. Cannot be rated: orange." },
                { "SkiResort.Hub.COL_LIFT", "Lift" },
                { "SkiResort.Hub.COL_LIFT_TIP", "↑: a running lift reaches the top of this run." },
                { "SkiResort.Hub.RUN_ESTIMATED", "Estimated from the day's temperature." },
                { "SkiResort.Hub.RUN_OPEN_GATING", "Counts as open: snow rules are off." },
                { "SkiResort.Hub.RUN_OPEN_NONE", "Not linked: no snow state." },
                { "SkiResort.Hub.RUN_SERVED_ANON", "A running lift reaches its top." },
                { "SkiResort.Hub.RUN_TYPE_NOTE", "Natural green and blue runs count less; a groomed run counts in full." },
                { "SkiResort.Hub.RUN_VALUE", "Score value" },
                { "SkiResort.Hub.RUN_COST", "Running cost" },
                { "SkiResort.Hub.RUN_ACTIONS", "Click to fly there. The pen renames it." },
                { "SkiResort.Hub.LIFT_LINE2[SectionMissing]", "A section is missing: redraw it with the lift line tool" },
                { "SkiResort.Hub.LIFT_LINE2[Unfinished]", "Not connected to a station at both ends yet" },
                { "SkiResort.Hub.LIFT_LINE2[StationsPair]", "Both stations placed: draw the lift line between them" },
                { "SkiResort.Hub.LIFT_LINE2[StationsBaseOnly]", "Base station placed: place the top station facing it" },
                { "SkiResort.Hub.LIFT_LINE2[StationsTopOnly]", "Top station placed: place the base station facing it" },
                { "SkiResort.Hub.LIFT_RIDE", "Ride time" },
                // Catalog Phase B (round-2-contract §8.6; CG-T3: {SPEED}, no digit but the game-speed "1×"): changed in place,
                // now a placeholder text (hubText.ts HUB_FMT.liftRideTip; was "... at 2.5 m/s ...").
                // Catalog Phase C (round-3-contract C15; CG-T7: no "loop"; was "Half the cable loop at {SPEED} m/s, at 1× game
                // speed."): true for both movers — a chair rides half its loop base to top, a tram cabin rides dock to dock.
                { "SkiResort.Hub.LIFT_RIDE_TIP", "Base to top at {SPEED} m/s line speed, at 1× game speed." },
                // Catalog Phase A (ROUND-SPEC R2; ext row 23; was "4-seat chairs …"). Catalog Phase B (round-2-contract §8.6; CG-T2:
                // {SEATS} and {SPEED}, no digit): changed in place, a placeholder text, LiftRow.seats / speedMps per lift type
                // (hubText.ts HUB_FMT.liftCapacityTip; was "3-seat chairs at 2.5 m/s, ...").
                // Catalog Phase C (round-3-contract C15; CG-T7: no "spaced"; was "..., spaced as built. ..."): a tram row's
                // capacity includes its station stops (HubRules.ShuttleCapacityPerHour).
                // Phase D (round-4-contract §4.4 with skeptic MUST-3, 24 words; was "..., as built; a tram counts its station
                // stops. The game ..."): an unpowered lift shows capacity – (SkiResortStatsSystem, HubRules.LiftCapacityShown).
                { "SkiResort.Hub.LIFT_CAPACITY_TIP", "{SEATS} seats a carrier at {SPEED} m/s; a tram counts its station stops; none unpowered or with no open run. Riders are not simulated." },
                // Catalog Phase B: the Lifts view's totals row (it sums several lift types: no {SEATS} / {SPEED}).
                // Phase D (round-4-contract §4.4 with skeptic MUST-3, 25 words; was "The sum over the complete lifts, each with
                // its own seats and speed, as built; ..."): the area total sums only the lifts that carry.
                { "SkiResort.Hub.LIFTS_TOTAL_CAPACITY_TIP", "The sum over the powered, open complete lifts, at their own seats and speed; a tram counts its station stops. Riders are not simulated." },
                { "SkiResort.Hub.LIFT_RUNS", "Runs it serves" },
                { "SkiResort.Hub.LIFT_COST", "Running cost" },
                // Phase D (DECISIONS 171 ruling 2, skeptic MUST-1; build-rules.md §3.2; was "Billed at 25 %: no power"): power no
                // longer lowers the bill (SkiMoneyRules.LiftUpkeepBillingOf), so a reduced row whose status is No power is
                // billed reduced only because its ski area has no counted run (NoOpenRun). hubRules.ts liftCostReason still
                // picks this key for that row, so the key keeps its slot and names the true reason (text only, contract 6).
                { "SkiResort.Hub.LIFT_COST_REDUCED[NoPower]", "Billed at 25 %: no open run in its ski area" },
                { "SkiResort.Hub.LIFT_COST_REDUCED[Unfinished]", "Billed at 25 % while unfinished" },
                { "SkiResort.Hub.LIFT_COST_REDUCED[SectionMissing]", "Billed at 25 % while a section is missing" },
                // Round M2 (R3, T2): a complete, powered lift whose ski area has no counted run (hubText.ts liftCostReducedNoOpenRun).
                { "SkiResort.Hub.LIFT_COST_REDUCED[NoOpenRun]", "Billed at 25 %: no open run in its ski area" },
                { "SkiResort.Hub.LIFT_ACTIONS", "Click to fly there (to the gap if a section is missing). The pen renames it." },
                { "SkiResort.Hub.LIFTS_TOTAL_LENGTH", "Total lift length" },
                { "SkiResort.Hub.LIFTS_TOTAL_CAPACITY", "Design capacity, all lifts" },
                { "SkiResort.Hub.SECTION[Guests]", "Guests" },
                { "SkiResort.Hub.GUESTS_MONTH[This]", "This month" },
                { "SkiResort.Hub.GUESTS_MONTH[Last]", "Last month" },
                // Round S1: F7's partial last month (bar label) and D27's ski-day row with its tooltip body.
                { "SkiResort.Hub.GUESTS_MONTH[LastPartial]", "Last month (partial)" },
                { "SkiResort.Hub.GUESTS_ROW[SkiDays]", "Ski days" },
                { "SkiResort.Hub.GUESTS_TIP[SkiDays]", "Skiers the resort sent here, counted when they reach a lodge. Counting starts when the lift ticket gate first opens in a season, so it is 0 before that. They also count as residents or tourists." },
                { "SkiResort.Hub.GUESTS_GROUP[Lodges]", "At the lodges" },
                { "SkiResort.Hub.GUESTS_GROUP[Sent]", "Sent to ski by the resort" },
                { "SkiResort.Hub.GUESTS_GROUP[Tickets]", "Lift tickets" },
                { "SkiResort.Hub.GUESTS_NOTE[NeverSki]", "Includes visitors who never ski" },
                { "SkiResort.Hub.GUESTS_ROW[Tourists]", "Tourists" },
                { "SkiResort.Hub.GUESTS_ROW[Groups]", "Tourist groups" },
                { "SkiResort.Hub.GUESTS_ROW[Residents]", "Residents" },
                { "SkiResort.Hub.GUESTS_ROW[Paid]", "Paid a lift ticket" },
                { "SkiResort.Hub.GUESTS_ROW[Tickets]", "Lift tickets" },
                { "SkiResort.Hub.GUESTS_TIP[Tourists]", "People in tourist groups that arrived at this ski area's lodges or Welcome Sign." },
                { "SkiResort.Hub.GUESTS_TIP[Groups]", "Tourists arrive in groups; each group counts once." },
                { "SkiResort.Hub.GUESTS_TIP[Residents]", "Residents of your city who came to a lodge for leisure." },
                { "SkiResort.Hub.GUESTS_TIP[Tickets]", "Paid by each guest's household into your city's balance." },
                { "SkiResort.Hub.GUESTS_LAST_NA", "Not counted: guests are counted only from the moment the city loads." },
                // Round M1 (D5): a month can now also be partial because time was skipped ahead; the new text waits (header).
                { "SkiResort.Hub.GUESTS_LAST_PARTIAL", "Last month was not counted in full: the city was loaded, or time was skipped ahead, during it." },
                // Round S1 (D27): the ski-day state line and its tooltips; the Room lines are placeholder keys below.
                // 800 m is D10's slopeside distance, 360 D21's skiers per lodge.
                { "SkiResort.Hub.SKI_DAYS[Off]", "Ski days are off (Options)" },
                { "SkiResort.Hub.SKI_DAYS[SignOff]", "Closed: the Welcome Sign is not running" },
                { "SkiResort.Hub.SKI_DAYS[NoRun]", "Closed: no open run" },
                { "SkiResort.Hub.SKI_DAYS[NoLodge]", "No lodge for ski days" },
                { "SkiResort.Hub.SKI_DAYS[Night]", "Lifts closed for the night" },
                { "SkiResort.Hub.SKI_DAYS[Full]", "Full right now" },
                { "SkiResort.Hub.SKI_DAYS_TIP[Off]", "Locals who ski and Tourists who ski are both 0 in Options." },
                { "SkiResort.Hub.SKI_DAYS_TIP[NoLodge]", "Ski days need a lodge that is switched on, at a road, and within 800 m of the runs." },
                { "SkiResort.Hub.SKI_DAYS_TIP[Runs]", "The runs set the limit: bigger and easier runs hold more skiers at once." },
                { "SkiResort.Hub.SKI_DAYS_TIP[Lodges]", "The lodges set the limit: each lodge takes 360 skiers at once, more on a powder day." },
                { "SkiResort.Hub.EST_ROW[Tourists]", "Target tourists from this ski area (estimated)" },
                { "SkiResort.Hub.EST_TO_NOTE", "Detected: Tourism Overhaul. It sets tourist numbers." },
                { "SkiResort.Hub.EST_TO_TIP", "Tourism Overhaul sets tourist numbers in this city, so no estimate is shown." },
                // Round M2 (R9): the Guests view's trip-planner note (no placeholder; Known / KnownOff are in the
                // placeholder block below) and its tooltip.
                { "SkiResort.Hub.TRIP_PLANNER_NOTE[Unknown]", "Part of the game's trip planner is switched off (another mod, or a game error), so trip times can differ." },
                { "SkiResort.Hub.TRIP_PLANNER_NOTE[UnknownOff]", "Ski days off: the game's leisure systems are switched off (another mod, or a game error)." },
                { "SkiResort.Hub.TRIP_PLANNER_TIP", "Read at each city load: which of the game's own trip systems are switched on, and which known mods are loaded. The mod's log lists the systems." },
                { "SkiResort.Hub.GUESTS_NOTE[Where]", "Counted when tourists arrive at this ski area's lodge or Welcome Sign, and when residents come to a lodge for leisure." },
                { "SkiResort.Hub.GUESTS_NOTE[SinceLoad]", "Counting started when you loaded the city, partway through this month." },
                { "SkiResort.Hub.TICKETS_NOTE[Off]", "Lift tickets are off (Options: Lift ticket price is 0)." },
                { "SkiResort.Hub.TICKETS_NOTE[NoEfficiency]", "No lift tickets now: the Welcome Sign is not running." },
                // Round M2 (R2, T1): changed in place; a running lift alone no longer opens the gate, and NotKnownYet also
                // covers a new sign without a score (fixer, review-ui-hub-money F2: shortened to 58 characters so the
                // one-line ticket slot shows it whole; hubText.ts holds the same text).
                { "SkiResort.Hub.TICKETS_NOTE[NothingOpen]", "No lift tickets now: no run is open." },
                { "SkiResort.Hub.TICKETS_NOTE[NotKnownYet]", "No lift tickets yet: runs are being checked (new sign or load)." },
                { "SkiResort.Hub.SECTION[Money]", "Money" },
                { "SkiResort.Hub.MONEY_COL", "A month" },
                { "SkiResort.Hub.MONEY_TICKETS[Unknown]", "Lift tickets: counted after a quarter month" },
                { "SkiResort.Hub.MONEY_TICKETS[Off]", "Lift tickets: off" },
                { "SkiResort.Hub.MONEY_TICKETS_TIP", "Last month's lift ticket money once a whole month has been counted. Before that, an estimate: this month so far scaled to a whole month (≈). The count is the guests who paid, each at the price when they arrived." },
                { "SkiResort.Hub.SECTION[Costs]", "Running costs" },
                { "SkiResort.Hub.MONEY_ROW[Buildings]", "Buildings and wages (game formula)" },
                { "SkiResort.Hub.MONEY_NET", "Net a month" },
                { "SkiResort.Hub.MONEY_TIP[Natural]", "Natural runs have no running cost." },
                { "SkiResort.Hub.MONEY_TIP[Net]", "Lift ticket income minus the running costs at today's rates. What tourists spend in your city's shops and hotels is not included." },
                { "SkiResort.Hub.MONEY_NOTE[Panel]", "Lift tickets, season passes, lessons and lodge spend show in the Economy panel under public transportation fees while Options > Modular Ski Resort > Main > Ski area balance > Book resort income as public transportation fees is on. When it is off they go straight to your balance. The game bills buildings and wages under Parks & Recreation." },
                { "SkiResort.Hub.MONEY_NOTE[Rwh]", "Detected: Realistic Workplaces. The real bill can differ." },
                { "SkiResort.Hub.MONEY_TIP[Rwh]", "Realistic Workplaces And Households changes staff and service costs, so the bill can differ." },
                { "SkiResort.Hub.MONEY_NOTE[Est]", "≈ Estimate: lift tickets are projected from this month so far until a full month has been counted." },
                // Round M2 (R7, hub-price.md): the Money view's price line, tooltip lines without a placeholder.
                { "SkiResort.Hub.MONEY_PRICE_TIP[Title]", "Lift ticket price" },
                { "SkiResort.Hub.MONEY_PRICE_TIP[Where]", "Change it in Options > Modular Ski Resort > Main > Ski area balance > Lift ticket price. One price for every ski area." },
                { "SkiResort.Hub.MONEY_PRICE_TIP[Who]", "Only ski days react to it. Visitors the game sends on its own come at any price, and the attraction score ignores it." },
                // Catalog Phase B (D15): the Money view's lodge-spend income row (only while it earned money this month).
                { "SkiResort.Hub.MONEY_ROW[LodgeSpend]", "Lodge spend" },
                // Contract 7 (season passes v1; hubText.ts holds the same texts, gate R4).
                { "SkiResort.Hub.MONEY_NOTE[Fees]", "Resort income shows in the Economy panel under public transportation fees." },
                { "SkiResort.Hub.MONEY_ROW[Passes]", "Season passes sold (this season) · {N}" },
                // Contract 8 (econ-2 round 2): the pre-season sale and lesson rows, the city-income estimate row, the lesson guests row.
                { "SkiResort.Hub.MONEY_ROW[PassesPre]", "Season passes sold before the season · {N}" },
                { "SkiResort.Hub.MONEY_ROW[Lessons]", "Lessons (this month) · {N}" },
                { "SkiResort.Hub.MONEY_TIP[Lessons]", "Lessons taken this month and what they paid, counted so far. Needs a working ski school. Booked like lift tickets." },
                { "SkiResort.Hub.MONEY_TIP[PassesPre]", "Season passes sold in the one sale before the season, when the ski area first opens. Already counted in the row above." },
                { "SkiResort.Hub.MONEY_ROW[CityIncome]", "City income the resort brings (estimate)" },
                { "SkiResort.Hub.MONEY_TIP[CityIncome]", "This resort's share of the city's tourist draw times the money the game counts as tourist income. It is not resort income and not in the net." },
                { "SkiResort.Hub.GUESTS_ROW[Lessons]", "of which took a lesson" },
                { "SkiResort.Hub.GUESTS_TIP[Lessons]", "People who took a lesson this month, one lesson each, at a ski area with a working ski school." },
                { "SkiResort.Hub.MONEY_TIP[Passes]", "Season passes sold this ski season (July to June) and what they paid, counted since the game was loaded. A household with a pass skis free at the gate for the rest of the season and goes skiing more often. Set the share and the price in Options > Modular Ski Resort > Main > Ski area balance. Booked like lift tickets." },
                { "SkiResort.Hub.MONEY_ROW[PassHolders]", "Pass holders skiing (this month)" },
                { "SkiResort.Hub.MONEY_TIP[PassHolders]", "Guests who came in on a season pass this month. They paid for the pass once, so they add no lift ticket money now." },
                { "SkiResort.Hub.GUESTS_ROW[PassHolders]", "of which pass holders" },
                { "SkiResort.Hub.GUESTS_TIP[PassHolders]", "Residents who came in on a season pass. They paid once for the season and pay nothing at the gate. Tourists never hold one." },
                { "SkiResort.Hub.MONEY_TIP[LodgeSpend]", "What paying ski guests spent at the lodges (Options > Modular Ski Resort > Main > Ski area balance > Lodge spend per guest)." },
                // MONEY_NOTE[Unknown] retired in Round F7 (D2): the tickets row says "counted after a quarter month".
                { "SkiResort.Hub.UNLINKED_MONEY", "Runs that are not linked cost nothing and earn nothing; their lifts still cost money to run." },
                { "SkiResort.Hub.BUILD_SHORT[Lodge]", "Goes near this ski area's Welcome Sign." },
                { "SkiResort.Hub.BUILD_SHORT[Top]", "Place at the top, facing the base station." },
                { "SkiResort.Hub.BUILD_SHORT[Lift]", "Draw from station to station, one section at a time." },
                // Catalog Phase B (round-2-contract §8.5-§8.6): the school and patrol tiles, the luxury tiers, the pickers. CG-T6
                // derives "+12", "20 %" (School), "1 km", "15 %", "70 %" (Patrol) and "40" (the luxury PriceTolerance) from SkiCatalog.
                { "SkiResort.Hub.BUILD_SHORT[School]", "Goes near this ski area's Welcome Sign; teaches on green runs." },
                { "SkiResort.Hub.BUILD_SHORT[Patrol]", "Goes on the Welcome Sign; place it near steep runs." },
                { "SkiResort.Hub.BUILD_TIP_SCHOOL", "Adds +12 to the runs and lifts sum while a green run counts; locals come 20 % more often." },
                { "SkiResort.Hub.BUILD_TIP_PATROL", "Black and double-black runs within 1 km score 15 % more; runs there without a lift count at least 70 %." },
                { "SkiResort.Hub.BUILD_TIP_LUXURY", "Luxury lodge. Ski days stay full at a lift ticket price up to 40 higher." },
                { "SkiResort.Hub.PICKER_TIP[LiftType]", "Pick the kind of lift to build." },
                { "SkiResort.Hub.PICKER_TIP[LodgeTier]", "Pick the lodge to build." },
                { "SkiResort.Hub.LIFT_TYPE[fixedGrip]", "Fixed-grip" },
                { "SkiResort.Hub.LIFT_TYPE[detachable]", "Detachable" },
                { "SkiResort.Hub.LIFT_TYPE[gondola]", "Gondola" },
                { "SkiResort.Hub.LIFT_TYPE[tram]", "Tram" },
                { "SkiResort.Hub.LODGE_TIER[lodge]", "A-frame" },
                { "SkiResort.Hub.LODGE_TIER[lodgeHut]", "Hut" },
                { "SkiResort.Hub.LODGE_TIER[lodgeMid]", "Chalet" },
                { "SkiResort.Hub.LODGE_TIER[lodgeSummit]", "Summit" },
                { "SkiResort.Hub.LODGE_TIER[lodgeGrand]", "Grand" },
                // T31 (2026-10-16): the Build strip's tabs, their tooltips and the tiles' visible labels.
                { "SkiResort.Hub.BUILD_TAB[Lifts]", "Lifts" },
                { "SkiResort.Hub.BUILD_TAB[Runs]", "Runs" },
                { "SkiResort.Hub.BUILD_TAB[Buildings]", "Buildings" },
                { "SkiResort.Hub.BUILD_TAB_TIP[Lifts]", "Lifts: build the two stations, then the line. Pick the lift type below." },
                { "SkiResort.Hub.BUILD_TAB_TIP[Runs]", "Runs: draw a run from the top of a lift, or plan trees with the tree tool." },
                { "SkiResort.Hub.BUILD_TAB_TIP[Buildings]", "Buildings: the Welcome Sign, lodges, the ski school and patrol. Pick the lodge below." },
                { "SkiResort.Hub.BUILD_LABEL[Base]", "Base station" },
                { "SkiResort.Hub.BUILD_LABEL[Top]", "Top station" },
                { "SkiResort.Hub.BUILD_LABEL[Line]", "Lift line" },
                { "SkiResort.Hub.BUILD_LABEL[Sign]", "Welcome Sign" },
                { "SkiResort.Hub.BUILD_LABEL[Lodge]", "Lodge" },
                { "SkiResort.Hub.BUILD_LABEL[School]", "Ski school" },
                { "SkiResort.Hub.BUILD_LABEL[PatrolHut]", "Patrol hut" },
                { "SkiResort.Hub.BUILD_LABEL[PatrolHQ]", "Patrol HQ" },
                { "SkiResort.Hub.BUILD_LABEL[Natural]", "Natural" },
                { "SkiResort.Hub.BUILD_LABEL[Groomed]", "Groomed" },
                { "SkiResort.Hub.BUILD_LABEL[Snowmaking]", "Snowmaking" },
                { "SkiResort.Hub.BUILD_LABEL[Night]", "Night" },
                { "SkiResort.Hub.BUILD_TIP_ONCE", "One-time" },
                { "SkiResort.Hub.BUILD_TIP_MONTH", "Each month" },
                { "SkiResort.Hub.BUILD_MONTH_NONE", "Nothing" },
                { "SkiResort.Hub.BUILD_MONTH_GAME", "Upkeep and wages, billed by the game" },
                { "SkiResort.Hub.BUILD_MONTH_STATION", "The whole lift is priced by its length: both stations and the line. This station is one part." },
                { "SkiResort.Hub.BUILD_TIP_RESHAPE", "Reshaping a run later costs nothing more." },
                { "SkiResort.Hub.BUILD_TIP_LINK", "Runs link to the nearest Welcome Sign within 5 km." },
                { "SkiResort.Hub.BUILD_TIP_NATURAL", "Nothing is drawn on the ground: find it with the piste map." },
                { "SkiResort.Hub.BUILD_TIP_LODGE", "A new lodge needs staff, power and water; until it has them, the sign's efficiency, and so the attraction, can fall." },
                { "SkiResort.Hub.BUILD_TIP_TYPE[Natural]", "Score: greens count half, blues three quarters, blacks in full. It opens only when natural snow covers it." },
                { "SkiResort.Hub.STATUS_LINE", "{STATE} · {OPEN} of {TOTAL} runs open" },
                { "SkiResort.Hub.STATUS_LINE_ONE", "{STATE} · {OPEN} of 1 run open" },
                { "SkiResort.Hub.STATUS_LINE_EST", "{STATE} · ≈ {OPEN} of {TOTAL} runs open" },
                { "SkiResort.Hub.STATUS_LINE_ONE_EST", "{STATE} · ≈ {OPEN} of 1 run open" },
                { "SkiResort.Hub.STATUS_LINE_PARTLY", "{STATE} · {PARTLY} of {TOTAL} runs patchy" },
                { "SkiResort.Hub.STATUS_LINE_PARTLY_ONE", "{STATE} · {PARTLY} of 1 run patchy" },
                { "SkiResort.Hub.STATUS_LINE_PARTLY_EST", "{STATE} · ≈ {PARTLY} of {TOTAL} runs patchy" },
                { "SkiResort.Hub.STATUS_LINE_PARTLY_ONE_EST", "{STATE} · ≈ {PARTLY} of 1 run patchy" },
                { "SkiResort.Hub.STATUS_TIP_COUNTS", "Open {OPEN} · patchy {PARTLY} · closed {CLOSED}" },
                { "SkiResort.Hub.STATUS_TIP_TEMP", "Day average at the top of the highest run: {TEMP}" },
                // Round S1 (D29): status tooltip line while powder > 0.
                { "SkiResort.Hub.STATUS_TIP_POWDER", "Fresh snow at the top: powder {PCT}. More skiers come." },
                { "SkiResort.Hub.PROBLEM[LodgesSomeOff]", "Lodges switched off: {N}" },
                { "SkiResort.Hub.PROBLEM[Efficiency]", "Welcome Sign at {PCT}: {REASON}" },
                { "SkiResort.Hub.PROBLEM[EfficiencyMore]", "Welcome Sign at {PCT}: {REASON} and {N} more" },
                { "SkiResort.Hub.PROBLEM[NotRunning]", "Welcome Sign not running: {REASON}" },
                { "SkiResort.Hub.PROBLEM[NotRunningMore]", "Welcome Sign not running: {REASON} and {N} more" },
                { "SkiResort.Hub.PROBLEM[LiftNoPower]", "{NAME} has no power: it counts 0" },
                { "SkiResort.Hub.PROBLEM[LiftSectionMissing]", "{NAME} is missing a section: it counts 0" },
                { "SkiResort.Hub.PROBLEM[LiftUnfinished]", "{NAME} is unfinished" },
                { "SkiResort.Hub.PROBLEM[NoLift]", "No lift yet: runs count {PCT} without one" },
                { "SkiResort.Hub.PROBLEM[StationsPair]", "{NAME}: draw the lift line" },
                { "SkiResort.Hub.PROBLEM[StationsBaseOnly]", "{NAME}: place its top station" },
                { "SkiResort.Hub.PROBLEM[StationsTopOnly]", "{NAME}: place its base station" },
                { "SkiResort.Hub.PROBLEM[LiftChairsStopped]", "{NAME}: the chairs are not moving" },
                // Catalog Phase B: C# HubProblemCode MixedLiftTypes 23 (D18) and SummitLodgeNotLiftServed 24 (D11).
                { "SkiResort.Hub.PROBLEM[MixedLiftTypes]", "{NAME} mixes lift types: it counts 0" },
                { "SkiResort.Hub.PROBLEM[SummitLodgeNotLiftServed]", "Summit lodges no lift reaches: {N}" },
                { "SkiResort.Hub.PROBLEM[RunsUnserved]", "Runs without a lift: {N} (they count {PCT})" },
                { "SkiResort.Hub.PROBLEM[RunsUnrateable]", "Runs that cannot be rated: {N}" },
                { "SkiResort.Hub.PROBLEM[RunsRating]", "Runs being rated: {N}" },
                { "SkiResort.Hub.PROBLEM[RunsFlat]", "Runs too flat to score: {N}" },
                { "SkiResort.Hub.PROBLEM[FreeLodges]", "Lodges on no Welcome Sign: {N}" },
                { "SkiResort.Hub.PROBLEM_MORE", "+{N}" },
                { "SkiResort.Hub.PROBLEM_TIP[NoLift]", "A run counts {PCT} until a running lift reaches its top: within 150 m of the line, at most 15 m above the top station." },
                { "SkiResort.Hub.PROBLEM_TIP[RunsUnserved]", "Each counts {PCT} until a running lift reaches its top: within 150 m of the line, at most 15 m above the top station." },
                { "SkiResort.Hub.PROBLEM_GAIN[Efficiency]", "At full efficiency the attraction would be {FULL}." },
                { "SkiResort.Hub.PROBLEM_GAIN[Lodge]", "With a lodge, the attraction would be {A}." },
                { "SkiResort.Hub.PROBLEM_GAIN[LodgesOn]", "With them switched on, the attraction would be {A}." },
                { "SkiResort.Hub.STAFF_LINE", "Staff: {FILLED} of {JOBS}" },
                { "SkiResort.Hub.TILE_TIP_FULL", "At full efficiency: {N}" },
                { "SkiResort.Hub.TILE_TIP_STAFFING", "× efficiency {PCT}" },
                { "SkiResort.Hub.TILE_TIP_PARTLY", "Patchy: {N}" },
                { "SkiResort.Hub.TILE_TIP_UNFINISHED", "Not complete (a section missing, unfinished, or stations only): {N}" },
                { "SkiResort.Hub.TILE_TIP_LAST", "Last month: {N}" },
                { "SkiResort.Hub.SCORE_OF", "{A} of 200" },
                { "SkiResort.Hub.SCORE_BAR_TIP", "{NOW} now. {FULL} at full efficiency. The ticks show what game buildings score: a tourist attraction about 50, a landmark about 120." },
                { "SkiResort.Hub.RECEIPT_TIP_LODGES", "Lodges counted: {N}" },
                { "SkiResort.Hub.RECEIPT_TIP[Terrain]", "{K} × the square root of ((runs + lifts) × variety × vertical). {K} is the Attractiveness scale option. Adding a run never lowers it." },
                { "SkiResort.Hub.RECEIPT_PARTS", "Runs {RUNS} · lifts {LIFTS} · variety ×{VARIETY} · vertical ×{VERTICAL}" },
                // Catalog Phase B (R13, round-2-contract §8.5): the parts line with the school term and the lodge base's best lodge
                // and extras (ResortLive.lodgeBest / lodgeExtra). (RECEIPT_TIP_LUXURY_IGNORED, for ResortLive.luxuryIgnored, was
                // removed 2026-10-11 with the one-luxury-lodge cap; the field stays on the wire, always 0.)
                { "SkiResort.Hub.RECEIPT_PARTS_SCHOOL", "Runs {RUNS} · lifts {LIFTS} · school {SCHOOL} · variety ×{VARIETY} · vertical ×{VERTICAL}" },
                { "SkiResort.Hub.RECEIPT_TIP_LODGE_BEST", "Best lodge: {NAME}; the others add {E}." },
                { "SkiResort.Hub.RECEIPT_TIP[Vertical]", "Vertical: up to double at 600 m, from the lowest bottom to the highest top of the runs that count now ({M})." },
                { "SkiResort.Hub.RECEIPT_OPEN", "Runs open now: {PCT} of their full value" },
                { "SkiResort.Hub.RECEIPT_TIP[Open]", "Closed runs add nothing and patchy runs add part, so snow moves the score. With every run fully open, runs would add {FULL}." },
                { "SkiResort.Hub.RECEIPT_TIMES", "× {PCT}" },
                { "SkiResort.Hub.DIFF_TIP_PENDING", "Being rated: {N}" },
                { "SkiResort.Hub.DIFF_TIP_UNRATEABLE", "Cannot be rated: {N}" },
                { "SkiResort.Hub.AREA_HA", "{HA} ha" },
                { "SkiResort.Hub.SNOW_DEPTH", "Average depth on the measured runs: {CM} cm" },
                { "SkiResort.Hub.RUN_OPEN[Open]", "Open: snow on {PCT} of it" },
                { "SkiResort.Hub.RUN_OPEN[Partly]", "Patchy: snow on {PCT} of it" },
                { "SkiResort.Hub.RUN_OPEN[Closed]", "Closed: snow on {PCT} of it" },
                { "SkiResort.Hub.RUN_SERVED", "Served by {NAME}" },
                { "SkiResort.Hub.RUN_NOT_SERVED", "No running lift reaches its top: it counts {PCT}" },
                { "SkiResort.Hub.RUN_VALUE_OF", "{NOW} of {FULL}" },
                { "SkiResort.Hub.RUN_VALUE_PARTS", "difficulty {B} × size {S} × type {T} × lift {L} × open {O}" },
                { "SkiResort.Hub.LIFT_DETAIL", "{LENGTH} · rise {RISE} · {MIN} min · {CAP}/h design · {N} runs" },
                { "SkiResort.Hub.LIFT_DETAIL_ONE", "{LENGTH} · rise {RISE} · {MIN} min · {CAP}/h design · 1 run" },
                { "SkiResort.Hub.LIFT_DETAIL_NONE", "{LENGTH} · rise {RISE} · {MIN} min · {CAP}/h design · no runs" },
                { "SkiResort.Hub.LIFT_RIDE_MIN", "{MIN} min" },
                { "SkiResort.Hub.CAPACITY_VALUE", "{CAP} riders/h" },
                { "SkiResort.Hub.GUESTS_TIP[Paid]", "{GUESTS} guests this month: {PAID} paid, {UNPAID} could not pay, {FREE} came in free (lift tickets off or the ski area closed)." },
                { "SkiResort.Hub.TICKETS_NOTE[Open]", "Each guest without a season pass pays {PRICE} on arrival (Options: Lift ticket price)." },
                // Round M2 (R9): the Guests view's trip-planner note when a known mod is loaded; NAME = CityStats.tripPlanner.
                { "SkiResort.Hub.TRIP_PLANNER_NOTE[Known]", "Detected: {NAME}. It replaces the game's trip planner, so trip times can differ." },
                { "SkiResort.Hub.TRIP_PLANNER_NOTE[KnownOff]", "Ski days off: {NAME} replaced the game's leisure systems." },
                // Round S1: the guest bars' fixed scale, and D27's capacity lines ("Holds {N} skiers at once":
                // ruling R-Q3, N = the capacity at once, D21) and the Ski days row's count lines.
                { "SkiResort.Hub.GUESTS_BAR_SCALE", "Full bar: {MAX} guests" },
                { "SkiResort.Hub.SKI_DAYS[RoomRuns]", "Holds {N} skiers at once (runs limit)" },
                { "SkiResort.Hub.SKI_DAYS[RoomLodges]", "Holds {N} skiers at once (lodges limit)" },
                { "SkiResort.Hub.SKI_DAYS[Room]", "Holds {N} skiers at once" },
                { "SkiResort.Hub.SKI_DAYS_TIP[Sent]", "Sent this month: {SENT} · close by {WALK} · with a car {CAR} · far, no car {FAR}" },
                { "SkiResort.Hub.SKI_DAYS_TIP[Now]", "On the hill or on the way now: {NOW} of {CAP}" },
                { "SkiResort.Hub.MONEY_TICKETS[Last]", "Lift tickets last month: {N} paid" },
                { "SkiResort.Hub.MONEY_TICKETS[Est]", "Lift tickets (estimated): {N} paid" },
                { "SkiResort.Hub.MONEY_TICKETS_PRICE", "Lift ticket price now: {PRICE}" },
                // Round M2 (R7): the price line (PCT = hubRules.priceFactorPct as a percent) and its curve tooltip line.
                { "SkiResort.Hub.MONEY_PRICE_LINE", "Lift ticket {PRICE} · ski days {PCT}" },
                { "SkiResort.Hub.MONEY_PRICE_TIP[Curve]", "Ski days: as many at any price up to {FULL}, then fewer: {F1} as many at {P1}, {F2} at {P2}, {F3} at {P3}." },
                // Catalog Phase B (D15 price tolerance; skeptic SHOULD: not "Luxury lodges", the Chalet has 20): FULL = 40 + tolerance.
                { "SkiResort.Hub.MONEY_PRICE_TIP[Tolerance]", "This ski area's lodges keep ski days full up to {FULL}." },
                { "SkiResort.Hub.MONEY_ROW[Natural]", "Natural runs · {HA} ha" },
                { "SkiResort.Hub.MONEY_ROW[Groomed]", "Groomed runs · {HA} ha with snow" },
                { "SkiResort.Hub.MONEY_ROW[Snowmaking]", "Snowmaking runs · {HA} ha with snow" },
                { "SkiResort.Hub.MONEY_ROW[Night]", "Night runs · {HA} ha with snow" },
                { "SkiResort.Hub.MONEY_ROW[Lifts]", "Lifts · {N} · {LEN} of line" },
                { "SkiResort.Hub.MONEY_TIP[Runs]", "{RATE} per hectare a month where the run holds managed snow now. It moves with the temperature through the day." },
                // Round M2 (R3, T2): changed in place, the third reduced case.
                // Phase D (DECISIONS 171 ruling 2, skeptic MUST-1; was "...; a quarter while unfinished, without power or with no
                // open run in its ski area. ..."): an unpowered lift still pays in full; two reductions remain.
                { "SkiResort.Hub.MONEY_TIP[Lifts]", "{BASE} a month for each complete lift, plus {PERKM} per km of line, in full even without power; a quarter while unfinished or with no open run in its ski area. Now {FULL} at the full rate and {REDUCED} at a quarter." },
                // Catalog Phase B: the lifts row's tooltip when two or more lift types are offered (each type's rates on its own line).
                // Phase D (as MONEY_TIP[Lifts]; was "...; a quarter while unfinished, without power or with no open run ...").
                { "SkiResort.Hub.MONEY_TIP[LiftsTypes]", "Each complete lift costs its kind's monthly base plus a rate per km of line, in full even without power; a quarter while unfinished or with no open run in its ski area. Now {FULL} at the full rate and {REDUCED} at a quarter." },
                { "SkiResort.Hub.MONEY_TIP_LIFT_RATE", "{TYPE}: {BASE} a month + {PERKM} per km" },
                { "SkiResort.Hub.MONEY_TIP[Buildings]", "The game's own formula for the lodges and the Welcome Sign at the current Parks & Recreation budget: upkeep {UPKEEP}, wages {WAGES} for {FILLED} of {JOBS} staff." },
                { "SkiResort.Hub.BUILD_LINE_RUN", "{NAME} · {FEE} + {PERHA} per ha" },
                { "SkiResort.Hub.BUILD_LINE_COST", "{NAME} · {COST}" },
                { "SkiResort.Hub.BUILD_LINE_LIFT", "{NAME} · about {PERKM} per km of line" },
                { "SkiResort.Hub.BUILD_ONCE_RUN", "{FEE} + {PERHA} per ha" },
                { "SkiResort.Hub.BUILD_ONCE_LIFT", "about {PERKM} per km of line" },
                { "SkiResort.Hub.BUILD_MONTH_RUN", "{RATE} per ha with snow" },
                { "SkiResort.Hub.BUILD_MONTH_LIFT", "{BASE} per complete lift + {PERKM} per km of line" },
                { "SkiResort.Hub.FOLLOW", "Now showing {NAME}." },
                { "SkiResort.Hub.STATUS_WORD[Open]", "Open" },
                { "SkiResort.Hub.STATUS_WORD[Partly]", "Partly open" },
                { "SkiResort.Hub.STATUS_WORD[Closed]", "Closed" },
                // Round F7 (D1): the Runs view's per-run state word (hubText.ts RUN_STATE_TEXT).
                { "SkiResort.Hub.RUN_STATE[Closed]", "Closed" },
                { "SkiResort.Hub.RUN_STATE[Patchy]", "Patchy" },
                { "SkiResort.Hub.RUN_STATE[Open]", "Open" },
                { "SkiResort.Hub.SEASON_HINT[SnowSeason]", "Snow season" },
                { "SkiResort.Hub.SEASON_HINT[SnowGunsOnly]", "Snow guns only" },
                { "SkiResort.Hub.SEASON_HINT[Thawing]", "Thawing" },
                { "SkiResort.Hub.SEASON_HINT[WaitingForSnow]", "Waiting for snow" },
                { "SkiResort.Hub.SEASON_HINT[TooWarm]", "Too warm for snow" },
                { "SkiResort.Hub.SEASON_HINT[GatingOff]", "Snow rules off" },
                // Round M2 (R5): changed in place; each tip says what it reads (the day average at the top decides
                // natural snow, the live gun gates decide "snow guns"); "never has a winter" goes.
                { "SkiResort.Hub.SEASON_TIP[SnowSeason]", "Day average at the top at or below freezing: natural snow can fall." },
                { "SkiResort.Hub.SEASON_TIP[SnowGunsOnly]", "Day average at the top above freezing, but snow guns are running now." },
                { "SkiResort.Hub.SEASON_TIP[Thawing]", "Day average at the top above freezing and no snow gun running now. The runs are open on the snow that is left." },
                { "SkiResort.Hub.SEASON_TIP[WaitingForSnow]", "Cold enough (day average at the top at or below freezing, or snow guns running), but no run has enough snow yet." },
                { "SkiResort.Hub.SEASON_TIP[TooWarm]", "Day average at the top above freezing and no snow gun can run now. Runs open again when it turns cold; if it never does on this map, turn off Options > Modular Ski Resort > Main > Snow > Snow & season gating." },
                { "SkiResort.Hub.LIFT_STATUS[Running]", "Running" },
                { "SkiResort.Hub.LIFT_STATUS[NoPower]", "No power" },
                { "SkiResort.Hub.LIFT_STATUS[ChairsStopped]", "Chairs stopped" },
                { "SkiResort.Hub.LIFT_STATUS[SectionMissing]", "Section missing" },
                { "SkiResort.Hub.LIFT_STATUS[Unfinished]", "Unfinished" },
                { "SkiResort.Hub.RUN_TYPE[Groomed]", "Groomed run" },
                { "SkiResort.Hub.RUN_TYPE[Snowmaking]", "Snowmaking run" },
                { "SkiResort.Hub.RUN_TYPE[Night]", "Night run" },
                { "SkiResort.Hub.RUN_TYPE[Natural]", "Natural run" },
                { "SkiResort.Hub.BUILD_SHORT[Groomed]", "Snow laid whenever it freezes; scores more on easy runs." },
                { "SkiResort.Hub.BUILD_SHORT[Snowmaking]", "Snow guns open it earlier and keep it open longer." },
                { "SkiResort.Hub.BUILD_SHORT[Night]", "Floodlit snowmaking run: scores 25 % more." },
                { "SkiResort.Hub.BUILD_SHORT[Natural]", "Natural snow only, no running cost; easy runs score less." },
                { "SkiResort.Hub.NEXT[Sign]", "Next: Buildings tab: place a Ski Area Welcome Sign." },
                { "SkiResort.Hub.NEXT[Unlinked]", "Runs and lifts link to the nearest Welcome Sign within 5 km." },
                { "SkiResort.Hub.NEXT[Lodge]", "Next: Buildings tab: a lodge near this ski area's Welcome Sign." },
                { "SkiResort.Hub.NEXT[Lift]", "Next: Lifts tab: a base station, a top station, then the line." },
                { "SkiResort.Hub.NEXT[Run]", "Next: Runs tab: draw a run within 5 km of this Welcome Sign." },
                { "SkiResort.Hub.NEXT[Done]", "Hover a tile to see what it builds and what it costs." },
                { "SkiResort.Hub.NEXT2[Unlinked]", "Place a sign near them to link them." },
                { "SkiResort.Hub.NEXT2[Lodge]", "Guests arrive at a lodge. Without one the attraction is 0." },
                { "SkiResort.Hub.NEXT2[Lift]", "Runs that a lift serves count in full." },
                { "SkiResort.Hub.NEXT2[Run]", "Natural runs cost nothing to run; the others cost per hectare." },
                { "SkiResort.Hub.NEXT2[Done]", "Click a tile to build; click it again to stop." },

                // --- Lift tooltips (docs/research/hub-u2-spec.md §2.4, §5.5, §5.7; Round L1) ----------
                // Read by SkiLiftTooltipSystem (I-2) through LocalizedString.Id(key), built once — never
                // Id(key, args) per frame. ALIGN_OFFSET / ANGLE / DISTANCE / RISE and BULLDOZE_COUNT /
                // LENGTH label FloatTooltip / IntTooltip values whose units the UI formats. These are not
                // SkiResort.Hub.* keys, so the UI gate's glyph check does not cover them: ALIGN_STRAIGHT's
                // em dash is the spec's text and is NOT on the research §2.F.2 safe-glyph list; it renders
                // correctly in game (field test 6, screenshot 20.46.20.81; ui-small.md Verification), so
                // ALIGN_FACING_AWAY reuses it.
                { "SkiResort.LiftTooltip.ALIGN_OFFSET", "Offset from the straight line" },
                { "SkiResort.LiftTooltip.ALIGN_ANGLE", "Angle off straight" },
                { "SkiResort.LiftTooltip.ALIGN_DISTANCE", "Distance to the other station" },
                { "SkiResort.LiftTooltip.ALIGN_RISE", "Rise" },
                { "SkiResort.LiftTooltip.ALIGN_STRAIGHT", "Straight — 180°" },
                { "SkiResort.LiftTooltip.ALIGN_NO_PARTNER", "No free station of the other kind within 3 km" },
                // Round F6 (B2): in place of the angle row when the yaw is beyond 90° (findings/ui-small.md
                // §2.5), and when this station sits behind the other one's stub (StationAlign READOUT CLASS).
                { "SkiResort.LiftTooltip.ALIGN_FACING_AWAY", "Facing away from the other station — turn it around" },
                { "SkiResort.LiftTooltip.ALIGN_PARTNER_AWAY", "The other station faces away from this one" },
                { "SkiResort.LiftTooltip.BULLDOZE_SECTION", "Removes 1 lift section. The lift stops until you redraw it." },
                { "SkiResort.LiftTooltip.BULLDOZE_TOWER", "This is a tower: it removes every section attached to it. Click the cable to remove one section." },
                // Fixer round (finding l1-lifts-3): a bulldozer drag over several cables is not "a tower".
                { "SkiResort.LiftTooltip.BULLDOZE_SECTIONS", "Removes these lift sections. The lift stops until you redraw them." },
                { "SkiResort.LiftTooltip.BULLDOZE_STUB", "Also removes the station's connection stub" },
                { "SkiResort.LiftTooltip.BULLDOZE_COUNT", "Sections removed" },
                { "SkiResort.LiftTooltip.BULLDOZE_LENGTH", "Length removed" },
                // T32 (2026-10-16): SkiSnowLineTooltipSystem, the one line under the cursor while a run or the Welcome Sign is
                // placed. {SNOW_LINE} and {GUN_LINE} are integer elevations in world metres (LocalizedNumber<int>, unit "integer");
                // "≈" and "·" are on the research §2.F.2 safe-glyph list.
                { "SkiResort.Tooltip.SNOW_LINE", "Winter snow line ≈ {SNOW_LINE} m · guns work above ≈ {GUN_LINE} m" },
            };
        }

        public void Unload()
        {
        }
    }
}
