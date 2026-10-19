/*
 * tools/test-hub-rules.js - gate rule R18: literal oracles for src/hub/hubRules.ts (pure display rules).
 * Compiles src/bindings.ts and src/hub/hubRules.ts with the installed typescript (transpileModule,
 * CommonJS) and runs them in a vm context: cs2/api is stubbed (bindValue returns { value: fallback },
 * trigger is a no-op), __BUILD_STAMP__ = "test". No new npm package. Exports run(srcDir) -> { checks, failures: string[] }.
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6; docs/research/econ-round/spec/
 * ch6-hub-ui.md section 5.3, the checked reference spec/ch6-hub-ui-ref/tools/test-hub-rules.js).
 * tools/check-bundle.js runs it as rule R18 (full gate and --contract-only, against --src= when given);
 * standalone: `node tools/test-hub-rules.js [srcDir]` -> `test-hub-rules: 147 check(s), failures=0` (76 in Round E1;
 * the E1 fixer added T8.7-T8.10 and changed T8.6's no-live oracle 3 -> 0, e1-spec 8.9 F9 / F11; Round F7 D5 added
 * T2.13-T2.14 and T3.14-T3.16 for the two road rows, docs/research/field-test-7-fixes.md; Round S1 added
 * T2.15-T2.16, T3.17-T3.19, T4.14, T6.9, T7.7-T7.8 and section T9, docs/research/ski-days/spec/ch4-hub.md G.2;
 * 98 + 1 + 6 + 8 = 113: Round M2 added T8.11, T10 (hub-price) and T11 (enabled-flags), docs/research/round-m2/
 * build-contract.md section 7; T11 reads bindings.ts too (FALLBACK.city, TRIP_PLANNER_UNKNOWN), the same
 * compiled module hubRules.ts imports; 113 + 1 (T5.14) + 20 (T12) + 4 (T13) + 9 (T14) = 147 in contract 6).
 * Contract 6 (2026-10-11, Catalog Phase B, code round 2, hub group; docs/research/art/assets-2026-10/code-round-2/
 * round-2-contract.md section 8.7): line1Variant switches on the item KIND (T5.5-T5.11 rewritten, T5.14 every kind);
 * priceFactor / priceFactorPct take a tolerance, so the T10 table rows call them through an arrow (Array.map would pass
 * the INDEX as the tolerance); new T12 (the pickers over the real buildItems.ts BUILD_ITEMS, which load() now compiles
 * too: sets with 1 / 2 / 3 sent options, the fallback, a half-imported type, visible ids, optionOf, dotTarget), T13 (the
 * price tolerance: (80, 0) 0.8, (80, 20) 0.9, (80, 40) 1, (200, 40) 0.4, (250, 40) 0.4), T14 (PROBLEM 23-25, isBuildItemId
 * for all 25 ids, liftTypesOf, receiptPartsVariant, lodgeSpendShown).
 * T78 build 2 (contract 10): T20 checks the History view (six views, HISTORY keys in both files, four ranges, no chart.js, HISTORY_FLAG
 * parity with C#, history$ only in HistoryView, no snow without depth) and runs tools/test-history-rules.js (31 oracles): 208 checks.
 * A new rule in hubRules.ts adds its literal rows here (oracles are literals, never recomputed).
 */
"use strict";
const fs = require("fs");
const path = require("path");
const vm = require("vm");

function load(srcDir, ts) {
  const cache = new Map();
  const stubs = {
    "cs2/api": { bindValue: (_g, _n, fb) => ({ value: fb, subscribe() { return { dispose() {} }; } }), trigger() {}, bindLocalValue: (v) => ({ value: v, update() {} }) },
    "cs2/utils": {},
  };
  const rulesFile = path.resolve(path.join(srcDir, "hub", "hubRules.ts"));
  const compile = (file) => {
    const abs = path.resolve(file);
    if (cache.has(abs)) return cache.get(abs).exports;
    const src = fs.readFileSync(abs, "utf8");
    const out = ts.transpileModule(src, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, esModuleInterop: true } }).outputText;
    const mod = { exports: {} };
    cache.set(abs, mod);
    const req = (spec) => {
      // S6 hardening (ch6 5.1 "imports values only from ../bindings"): hubRules.ts itself may load no
      // value but ../bindings - a cs2/* value import would otherwise pass against the stubs below
      // (type-only imports are erased by transpileModule and never reach here).
      if (abs === rulesFile && spec !== "../bindings") {
        throw new Error(`test-hub-rules: hubRules.ts imports the value(s) of "${spec}" - it must stay pure (values only from ../bindings)`);
      }
      if (stubs[spec]) return stubs[spec];
      if (spec.startsWith(".")) {
        const base = path.resolve(path.dirname(abs), spec);
        for (const ext of [".ts", ".tsx"]) if (fs.existsSync(base + ext)) return compile(base + ext);
      }
      throw new Error(`test-hub-rules: unexpected import "${spec}" from ${file} (hubRules.ts must stay pure)`);
    };
    vm.runInNewContext(out, { require: req, module: mod, exports: mod.exports, __BUILD_STAMP__: "test", console, Map, Set, Math, Number }, { filename: abs });
    return mod.exports;
  };
  const rules = compile(path.join(srcDir, "hub", "hubRules.ts"));
  // Round M2 (T11): the bindings module hubRules.ts loaded (same cache entry, so the same values).
  // Contract 6 (T12): buildItems.ts (BUILD_ITEMS, the table the strip passes to the pickers); it imports values only from
  // ../bindings (the same cache entry). The purity check above applies to hubRules.ts only.
  // T78 (T20): historyRules.ts, pure like hubRules.ts (its own purity check is in tools/test-history-rules.js).
  return { rules, bindings: compile(path.join(srcDir, "bindings.ts")), items: compile(path.join(srcDir, "hub", "buildItems.ts")),
    history: compile(path.join(srcDir, "hub", "historyRules.ts")) };
}

function run(srcDir) {
  const ts = require("typescript");
  const { rules: R, bindings: BIND, items: ITEMS, history: HR } = load(srcDir, ts);
  const failures = [];
  let checks = 0;
  const eq = (name, got, want) => {
    checks++;
    const g = JSON.stringify(got), w = JSON.stringify(want);
    if (g !== w) failures.push(`${name}: got ${g}, want ${w}`);
  };
  const E = (i, v = 1) => ({ index: i, version: v });
  const NUL = { index: 0, version: 0 };
  const P = (sign, code, severity, extra = {}) => Object.assign({ sign, code, severity, subject: NUL, subjectTop: NUL, name: "",
    count: -1, pct: -1, factor: "", more: 0, detail: 0, ifFixed: -1, buildItem: "" }, extra);
  const A = E(7), B = E(9);

  // T1 joins
  const live = [{ sign: A, nextStep: 0 }, { sign: B, nextStep: 2 }, { sign: NUL, nextStep: 0 }];
  eq("T1.1 liveOf by index", R.liveOf(live, B, 1).nextStep, 2);
  eq("T1.2 liveOf by scan", R.liveOf(live, B, 0).nextStep, 2);
  eq("T1.3 liveOf Not linked", R.liveOf(live, NUL).sign, NUL);
  eq("T1.4 liveOf missing", R.liveOf(live, E(99)), null);
  eq("T1.5 runLiveMap", R.runLiveMap([{ entity: E(3, 2), openState: 2 }]).get("3:2").openState, 2);

  // T2 band rows, factor rows, slots, dot (Copper Bowl, design 3.1 / hub-rules P2)
  const probs = [
    P(A, 4, 2, { pct: 50, factor: "ElectricitySupply", ifFixed: 69 }),
    P(A, 5, 2, { name: "Lift 2" }),
    P(A, 10, 1, { name: "Lift 3", detail: 1, buildItem: "topStation" }),
    P(A, 12, 1, { count: 2, pct: 40 }),
    P(A, 16, 0, { pct: -50, factor: "ElectricitySupply" }),
    P(B, 1, 3, { buildItem: "lodge" }),
  ];
  eq("T2.1 bandRowsOf codes", R.bandRowsOf(probs, A).map((p) => p.code), [4, 5, 10, 12]);
  eq("T2.2 factorRowsOf", R.factorRowsOf(probs, A).map((p) => p.pct), [-50]);
  eq("T2.3 bandSlots more", R.bandSlots(R.bandRowsOf(probs, A)).more, 1);
  eq("T2.4 bandSlots empty", R.bandSlots([]).slots, [null, null, null]);
  eq("T2.5 dot Copper Bowl", R.dotItem(probs, A, true), "topStation");
  eq("T2.6 dot other area", R.dotItem(probs, B, true), "lodge");
  eq("T2.7 dot no sign", R.dotItem([], NUL, false), "sign");
  eq("T2.8 dot healthy area", R.dotItem([P(A, 13, 1, { count: 1 })], A, true), null);
  eq("T2.9 dot unknown id ignored", R.dotItem([P(A, 7, 2, { buildItem: "runPowder" }), P(A, 8, 1, { buildItem: "baseStation" })], A, true), "baseStation");
  eq("T2.10 factor never in band", R.bandRowsOf([P(A, 16, 0)], A).length, 0);
  eq("T2.11 unknown code dropped", R.bandRowsOf([P(A, 99, 2)], A).length, 0);
  eq("T2.12 RunsFlat (17) kept in the band", R.bandRowsOf([P(A, 17, 1, { count: 2 })], A).map((p) => p.code), [17]);
  // Round F7 D5 (docs/research/field-test-7-fixes.md; rows from field-test-7-notes-impl4.md): SignNoRoad 18, LodgeNoRoad 19.
  eq("T2.13 road rows kept in the band, C# order", R.bandRowsOf([P(A, 18, 3), P(A, 4, 2, { pct: 12 }), P(A, 19, 1, { count: 1 })], A).map((p) => p.code), [18, 4, 19]);
  eq("T2.14 road rows name no build item", R.dotItem([P(A, 18, 3), P(A, 19, 3, { count: 1 })], A, true), null);
  // Round S1 D28 (ch4 G.2): SkiDaysCannotReach 20, NoParkingNearLodge 21, LodgeNotSlopeside 22.
  eq("T2.15 ski-day rows kept in the band, C# order", R.bandRowsOf([P(A, 4, 2, { pct: 50 }), P(A, 20, 2), P(A, 21, 1, { count: 1 }), P(A, 22, 1, { count: 1 })], A).map((p) => p.code), [4, 20, 21, 22]);
  eq("T2.16 ski-day rows name no build item", R.dotItem([P(A, 20, 2), P(A, 21, 1, { count: 1 }), P(A, 22, 1, { count: 1 })], A, true), null);

  // T3 problem variants, icons, clicks
  eq("T3.1 efficiency", R.problemVariant(P(A, 4, 2, { pct: 50 })), "efficiency");
  eq("T3.2 efficiencyMore", R.problemVariant(P(A, 4, 2, { pct: 4, more: 3 })), "efficiencyMore");
  eq("T3.3 notRunning", R.problemVariant(P(A, 4, 3, { pct: 0 })), "notRunning");
  eq("T3.4 notRunningMore", R.problemVariant(P(A, 4, 3, { pct: 0, more: 1 })), "notRunningMore");
  eq("T3.5 stations detail 0/1/2", [0, 1, 2].map((d) => R.problemVariant(P(A, 10, 1, { detail: d }))), ["stationsPair", "stationsBaseOnly", "stationsTopOnly"]);
  eq("T3.6 factor row", R.problemVariant(P(A, 16, 0)), null);
  eq("T3.7 icon efficiency power", R.problemIcon(P(A, 4, 2, { factor: "ElectricitySupply" })), { kind: "img", src: "Media/Game/Notifications/NotEnoughElectricity.svg" });
  eq("T3.8 icon efficiency budget", R.problemIcon(P(A, 4, 2, { factor: "ServiceBudget" })), { kind: "glyph", src: "Media/Glyphs/Warning.svg", tone: "warn" });
  eq("T3.9 icon lodges off", R.problemIcon(P(A, 3, 1, { count: 1 })).src, "Media/Game/Notifications/TurnedOff.svg");
  eq("T3.10 icon notice", R.problemIcon(P(A, 13, 1)), { kind: "glyph", src: "Media/Glyphs/Info.svg", tone: "info" });
  eq("T3.11 icon none", R.problemIcon(null), { kind: "glyph", src: "Media/Glyphs/Checkmark.svg", tone: "ok" });
  eq("T3.13 runsFlat variant and click", [R.problemVariant(P(A, 17, 1)), R.problemClick(P(A, 17, 1))], ["runsFlat", "runsView"]);
  eq("T3.12 clicks", [1, 4, 5, 10, 8, 9, 12, 0, 15].map((c) => R.problemClick(P(A, c, 1))), ["sign", "sign", "lift", "lift", "liftsView", "runsView", "runsView", null, null]);
  eq("T3.14 signNoRoad / lodgeNoRoad variants", [R.problemVariant(P(A, 18, 3)), R.problemVariant(P(A, 19, 2, { count: 1 }))], ["signNoRoad", "lodgeNoRoad"]);
  eq("T3.15 road rows fly to their subject", [R.problemClick(P(A, 18, 3)), R.problemClick(P(A, 19, 1))], ["sign", "sign"]);
  eq("T3.16 road row icons", [R.problemIcon(P(A, 18, 3)), R.problemIcon(P(A, 19, 1))],
    [{ kind: "glyph", src: "Media/Glyphs/Warning.svg", tone: "warn" }, { kind: "glyph", src: "Media/Glyphs/Info.svg", tone: "info" }]);
  eq("T3.17 ski-day variants", [R.problemVariant(P(A, 20, 2)), R.problemVariant(P(A, 21, 1)), R.problemVariant(P(A, 22, 1))], ["skiDaysCannotReach", "noParkingNearLodge", "lodgeNotSlopeside"]);
  eq("T3.18 ski-day rows fly to their subject", [20, 21, 22].map((c) => R.problemClick(P(A, c, 1))), ["sign", "sign", "sign"]);
  eq("T3.19 ski-day row icons", [R.problemIcon(P(A, 20, 2)), R.problemIcon(P(A, 21, 1)), R.problemIcon(P(A, 22, 1))],
    [{ kind: "glyph", src: "Media/Glyphs/Warning.svg", tone: "warn" }, { kind: "glyph", src: "Media/Glyphs/Info.svg", tone: "info" }, { kind: "glyph", src: "Media/Glyphs/Info.svg", tone: "info" }]);

  // T4 status line
  const row = (runs, kind = 0) => ({ kind, runs, liftRows: 3 });
  const lv = (status, est = false, extra = {}) => Object.assign({ status, snowEstimated: est, runsOpen: 4, runsPartly: 1, liftsComplete: 2, nextStep: 0, ticketGate: 0, ticketIncomeMonth: 8000, ticketIncomeEstimated: false, buildingsMonth: 6000, wagesMonth: 15000 }, extra);
  eq("T4.1 unlinked", R.statusVariant(row(2, 1), null), "unlinked");
  eq("T4.2 unknown", R.statusVariant(row(7), null), "unknown");
  eq("T4.3 noRuns", R.statusVariant(row(0), lv(0)), "noRuns");
  eq("T4.4 line", R.statusVariant(row(7), lv(2)), "line");
  eq("T4.5 lineEst", R.statusVariant(row(7), lv(2, true)), "lineEst");
  eq("T4.6 one", R.statusVariant(row(1), lv(3)), "one");
  eq("T4.7 oneEst", R.statusVariant(row(1), lv(1, true)), "oneEst");
  eq("T4.8 words", [1, 2, 3].map(R.statusWord), ["closed", "partly", "open"]);
  eq("T4.9 closedRuns", R.closedRuns(row(7), lv(2)), 2);
  eq("T4.10 closedRuns floor", R.closedRuns(row(3), lv(2, false, { runsOpen: 4, runsPartly: 1 })), 0);
  eq("T4.11 partly only (0 open, 2 partly)", R.statusVariant(row(7), lv(2, false, { runsOpen: 0, runsPartly: 2 })), "partly");
  eq("T4.12 partly only, one run, estimated", R.statusVariant(row(1), lv(2, true, { runsOpen: 0, runsPartly: 1 })), "partlyOneEst");
  eq("T4.13 result label", [R.resultVariant(lv(2, false, { lodgesActive: 0 })), R.resultVariant(lv(2, false, { lodgesActive: 2 })), R.resultVariant(lv(2, false, { lodgesActive: -1 })), R.resultVariant(null)], ["noLodge", "result", "result", "result"]);
  // Round S1 D29: "Powder day" at 50 (lv(3) has no skiDaysPowderPct: a row without the field reads "season").
  eq("T4.14 status hint", [R.statusHint(row(5), lv(3, false, { skiDaysPowderPct: 50 })), R.statusHint(row(5), lv(3, false, { skiDaysPowderPct: 49 })),
    R.statusHint(row(5), lv(3, false, { skiDaysPowderPct: -1 })), R.statusHint(row(5), lv(3)), R.statusHint(row(0), lv(0, false, { skiDaysPowderPct: 100 })),
    R.statusHint(row(2, 1), lv(3, false, { skiDaysPowderPct: 100 })), R.statusHint(row(5), null)],
    ["powder", "season", "season", "season", "none", "none", "none"]);

  // T5 footer
  eq("T5.1 next no sign", R.nextVariant(false, false, null), "sign");
  eq("T5.2 next unlinked", R.nextVariant(true, true, null), "unlinked");
  eq("T5.3 next steps", [0, 1, 2, 3].map((s) => R.nextVariant(true, false, { nextStep: s })), ["done", "lodge", "lift", "run"]);
  eq("T5.4 next missing live", R.nextVariant(true, false, null), "done");
  const item = (extra) => Object.assign({ id: "x", locked: false, active: false, reason: "" }, extra);
  // Contract 6: line1Variant takes the item's KIND (buildItems.ts BuildItemMeta.kind), not its id.
  eq("T5.5 line1 run", R.line1Variant("run", item({ price: 500, pricePerHa: 250 })), "run");
  eq("T5.6 line1 run no per-ha (contract-2 DLL)", R.line1Variant("run", item({ price: 2000 })), "name");
  eq("T5.7 line1 lift", R.line1Variant("line", item({ costPerKm: 7500 })), "lift");
  eq("T5.8 line1 lift unknown", R.line1Variant("line", item({ costPerKm: -1 })), "name");
  eq("T5.9 line1 cost", R.line1Variant("lodge", item({ cost: 40000 })), "cost");
  eq("T5.10 line1 cost zero", R.line1Variant("sign", item({ cost: 0 })), "cost");
  eq("T5.11 line1 name", R.line1Variant("station", item({})), "name");
  eq("T5.12 footer priority", [
    R.footerState(false, true, true, true, true), R.footerState(true, true, true, true, true),
    R.footerState(true, false, true, true, true), R.footerState(true, false, false, true, true),
    R.footerState(true, false, false, false, true), R.footerState(true, false, false, false, false)],
    ["empty", "hover", "follow", "active", "locked", "idle"]);
  eq("T5.13 run kinds", ["runNatural", "runGroomed", "runSnowmaking", "runNight", "lift"].map(R.runKindOfItem), [3, 0, 1, 2, undefined]);
  // Contract 6: every kind (a line of any lift type reads "about per km"; an unknown kind reads its cost).
  const full = item({ cost: 125000, costPerKm: 15000, price: 500, pricePerHa: 250 });
  eq("T5.14 line1 every kind", ["sign", "lodge", "school", "patrol", "station", "line", "run", undefined, "bogus"].map((k) => R.line1Variant(k, full)),
    ["cost", "cost", "cost", "cost", "cost", "lift", "run", "cost", "cost"]);

  // T6 money and guests
  eq("T6.1 ticketNote", [[0, 40], [1, 40], [2, 40], [3, 40], [4, 40], [0, 0]].map(([g, p]) => R.ticketNote(g, p)), ["open", "off", "noEfficiency", "nothingOpen", "notKnownYet", "off"]);
  eq("T6.2 ticketsRow last", R.ticketsRow(lv(2), 40), "last");
  eq("T6.3 ticketsRow est", R.ticketsRow(lv(2, false, { ticketIncomeEstimated: true }), 40), "est");
  eq("T6.4 ticketsRow unknown", R.ticketsRow(lv(2, false, { ticketIncomeMonth: -1, ticketIncomeEstimated: true }), 40), "unknown");
  eq("T6.5 ticketsRow off", R.ticketsRow(lv(2), 0), "off");
  eq("T6.6 incomeNote", [R.incomeNote(lv(2), 40), R.incomeNote(lv(2, false, { ticketIncomeEstimated: true }), 40), R.incomeNote(lv(2, false, { ticketIncomeMonth: -1 }), 40)], [null, "est", "unknown"]);
  eq("T6.7 buildingsAndWages", [R.buildingsAndWages(lv(2)), R.buildingsAndWages(lv(2, false, { wagesMonth: -1 }))], [21000, -1]);
  eq("T6.8 hectares", [R.hectares(160000), R.hectares(-5), R.hectares(NaN)], [16, 0, 0]);
  // Round S1 F7: a since-load last month is "partial", never "–".
  eq("T6.9 lastMonthMark", [R.lastMonthMark({ lastMonthFull: true, guestsLastMonth: 46 }), R.lastMonthMark({ lastMonthFull: false, guestsLastMonth: 46 }),
    R.lastMonthMark({ lastMonthFull: false, guestsLastMonth: 0 }), R.lastMonthMark({ lastMonthFull: false, guestsLastMonth: -1 }), R.lastMonthMark(null)],
    ["full", "partial", "partial", "none", "none"]);

  // T7 bars
  eq("T7.1 score Copper Bowl", R.scoreBarParts(34, 69), [34, 35, 131]);
  eq("T7.2 score unknown", R.scoreBarParts(-1, -1), [0, 0, 200]);
  eq("T7.3 score above full (budget > 100 %)", R.scoreBarParts(60, 48), [60, 0, 140]);
  eq("T7.4 score clamp", R.scoreBarParts(250, 300), [200, 0, 0]);
  eq("T7.5 score zero", R.scoreBarParts(0, 0), [0, 0, 200]);
  eq("T7.6 guests", [R.guestBarParts(84, 210), R.guestBarParts(210, 210), R.guestBarParts(-1, 210), R.guestBarParts(0, 0)], [[84, 126], [210, 0], [0, 1], [0, 1]]);
  // Round S1 (ch4 B.3): the fixed ladder 50, 100, 200, 500, 1000, 2000; field test 8's two cases.
  eq("T7.7 guestBarScale", [R.guestBarScale(2, -1), R.guestBarScale(0, 0), R.guestBarScale(50, 46), R.guestBarScale(51, 0), R.guestBarScale(90, 46),
    R.guestBarScale(162, 205), R.guestBarScale(1000, 999), R.guestBarScale(1001, 0), R.guestBarScale(NaN, -5)], [50, 50, 50, 100, 100, 500, 1000, 2000, 50]);
  eq("T7.8 FT8 bars on a fixed scale", [R.guestBarParts(2, R.guestBarScale(2, -1)), R.guestBarParts(90, R.guestBarScale(90, 46)), R.guestBarParts(46, R.guestBarScale(90, 46))],
    [[2, 48], [90, 10], [46, 54]]);

  // T8 lifts
  const lift = (status, state, extra = {}) => Object.assign({ status, state, hasBase: true, hasTop: true, runsServed: 5 }, extra);
  eq("T8.1 line2 detail", [R.liftLine2(lift(0, 0)), R.liftLine2(lift(1, 0, { runsServed: 0 })), R.liftLine2(lift(2, 1, { runsServed: 1 }))], ["detail", "detailNone", "detailOne"]);
  eq("T8.2 line2 section", R.liftLine2(lift(3, 2)), "sectionMissing");
  eq("T8.3 line2 unfinished", R.liftLine2(lift(4, 3, { hasTop: false })), "unfinished");
  eq("T8.4 line2 stations", [R.liftLine2(lift(4, 4)), R.liftLine2(lift(4, 4, { hasTop: false })), R.liftLine2(lift(4, 4, { hasBase: false })), R.liftLine2(lift(4, 4, { hasBase: false, hasTop: false }))],
    ["stationsPair", "stationsBaseOnly", "stationsTopOnly", "unfinished"]);
  eq("T8.5 marks", [0, 1, 2, 3, 4].map(R.liftMark), [{ ring: false, tone: "normal" }, { ring: false, tone: "warn" }, { ring: false, tone: "dim" }, { ring: true, tone: "warn" }, { ring: true, tone: "dim" }]);
  // E1 fixer (e1-spec 8.9 F11): no live row, or liftsComplete -1 (Not linked, no result) -> 0, never a guess.
  eq("T8.6 unfinishedLifts", [R.unfinishedLifts(row(7), lv(2)), R.unfinishedLifts(row(7), null), R.unfinishedLifts({ liftRows: 1 }, lv(2))], [1, 0, 0]);
  eq("T8.7 unfinishedLifts unknown complete", R.unfinishedLifts(row(7), Object.assign({}, lv(2), { liftsComplete: -1 })), 0);
  // E1 fixer (F9): lone stations pair across rows (the census never builds a StationsOnly row with both).
  eq("T8.8 line2 partner placed", [R.liftLine2(lift(4, 4, { hasTop: false }), true), R.liftLine2(lift(4, 4, { hasBase: false }), true),
    R.liftLine2(lift(4, 4, { hasBase: false, hasTop: false }), true), R.liftLine2(lift(4, 3, { hasTop: false }), true)],
    ["stationsPair", "stationsPair", "unfinished", "unfinished"]);
  const lone = (rows) => R.loneStations(rows);
  eq("T8.9 loneStations", [lone([lift(4, 4, { hasTop: false }), lift(4, 4, { hasBase: false })]), lone([lift(4, 4, { hasTop: false }), lift(0, 0)]),
    lone([lift(4, 3, { hasBase: false })]), lone(null)],
    [{ base: true, top: true }, { base: true, top: false }, { base: false, top: true }, { base: false, top: false }]);
  eq("T8.10 partnerPlaced", [R.partnerPlaced(lift(4, 4, { hasTop: false }), { base: true, top: true }), R.partnerPlaced(lift(4, 4, { hasTop: false }), { base: true, top: false }),
    R.partnerPlaced(lift(4, 4, { hasBase: false }), { base: true, top: false }), R.partnerPlaced(lift(0, 0), { base: true, top: true })],
    [true, false, true, false]);
  // Round M2 T2 (thaw-rules.md section 3, R3): a complete, powered lift billed at 25 % reads "no open run" (Running 0, ChairsStopped 2).
  eq("T8.11 liftCostReason", [lift(0, 0, { billedReduced: false }), lift(1, 0, { billedReduced: true }), lift(3, 2, { billedReduced: true }),
    lift(4, 3, { billedReduced: true }), lift(0, 0, { billedReduced: true }), lift(2, 1, { billedReduced: true })].map(R.liftCostReason),
    [null, "noPower", "sectionMissing", "unfinished", "noOpenRun", "noOpenRun"]);

  // T9 ski days (Round S1 D27, ch4 B.2 / B.6): the state line, its tooltip and the walk count.
  const sd = (state, limit = 0, extra = {}) => Object.assign({ skiDaysState: state, skiDaysLimit: limit, skiDaysCap: 154 }, extra);
  eq("T9.1 skiDayLine states", [0, 1, 2, 3, 4, 5, 7, 99].map((s) => R.skiDayLine(sd(s))), ["unknown", "off", "signOff", "noRun", "noLodge", "night", "full", "unknown"]);
  eq("T9.2 skiDayLine room by limit", [R.skiDayLine(sd(6, 1)), R.skiDayLine(sd(6, 2)), R.skiDayLine(sd(6, 0)), R.skiDayLine(null)], ["roomRuns", "roomLodges", "room", "unknown"]);
  eq("T9.3 skiDayWalk", [R.skiDayWalk({ skiDaysSent: 52, skiDaysSentCar: 41, skiDaysSentNoCar: 3 }), R.skiDayWalk({ skiDaysSent: 5, skiDaysSentCar: 4, skiDaysSentNoCar: 3 }),
    R.skiDayWalk({ skiDaysSent: -1, skiDaysSentCar: -1, skiDaysSentNoCar: -1 }), R.skiDayWalk(null)], [8, 0, -1, -1]);
  eq("T9.4 skiDayTip", [R.skiDayTip(sd(1)), R.skiDayTip(sd(4)), R.skiDayTip(sd(6, 1)), R.skiDayTip(sd(7, 2)), R.skiDayTip(sd(5)), R.skiDayTip(sd(6, 0))],
    ["off", "noLodge", "runs", "lodges", null, null]);

  // T10 ticket price line (Round M2 R7, hub-price.md section 3; build-contract.md section 7 and Decided here 13): D16's
  // factor; T10.1's table is also ResortBatch HubRulesTests H17 (the C# half, on factors).
  const prices = [0, 5, 40, 45, 80, 100, 120, 150, 200, 260];
  // Contract 6: priceFactor / priceFactorPct take a tolerance (default 0), so every row calls them through an arrow.
  const pf = (p) => R.priceFactor(p), pfp = (p) => R.priceFactorPct(p);
  eq("T10.1 priceFactor table", prices.map(pf), [1, 1, 1, 0.975, 0.8, 0.7, 0.6, 0.45, 0.2, 0.2]);
  eq("T10.2 unknown price (priceFactor, priceFactorPct)", [[-1, -5, NaN, Infinity].map(pf), [-1, -5, NaN, Infinity].map(pfp)],
    [[-1, -1, -1, -1], [-1, -1, -1, -1]]);
  eq("T10.3 price constants", [R.PRICE_FULL_AT, R.PRICE_SPAN, R.PRICE_MAX, R.PRICE_NO_EFFECT_MAX, R.PRICE_EXAMPLES], [240, 200, 200, 40, [80, 120, 200]]);
  eq("T10.4 priceFactorPct table (45 rounds half up)", prices.map(pfp), [100, 100, 100, 98, 80, 70, 60, 45, 20, 20]);
  eq("T10.5 tooltip examples", R.PRICE_EXAMPLES.map(pfp), [80, 60, 20]);
  eq("T10.6 hubPrice", [R.hubPrice({ known: true, ticketPrice: 40 }), R.hubPrice({ known: true, ticketPrice: 0 }),
    R.hubPrice({ known: false, ticketPrice: 0 }), R.hubPrice({ known: true, ticketPrice: -3 }), R.hubPrice(null)],
    [40, 0, -1, -1, -1]);

  // T11 trip-planner note (Round M2 R9, enabled-flags.md section 3 T10.1-T10.8 renumbered; build-contract.md section 7).
  const tp = (tripPlanner, tripPlannerBlocksSkiDays) => Object.assign({}, BIND.FALLBACK.city, { known: true, tripPlanner, tripPlannerBlocksSkiDays });
  eq("T11.1 tripPlannerNote fallback city", R.tripPlannerNote(BIND.FALLBACK.city), "none");
  eq("T11.2 known mod", R.tripPlannerNote(tp("Realistic PathFinding", false)), "known");
  eq("T11.3 known mod blocks ski days", R.tripPlannerNote(tp("Realistic Trips", true)), "knownOff");
  eq("T11.4 unknown", R.tripPlannerNote(tp("?", false)), "unknown");
  eq("T11.5 unknown blocks ski days", R.tripPlannerNote(tp("?", true)), "unknownOff");
  eq("T11.6 empty label", R.tripPlannerNote(tp("", true)), "none");
  eq("T11.7 no city, no field", [null, { tripPlanner: undefined }].map(R.tripPlannerNote), ["none", "none"]);
  eq("T11.8 TRIP_PLANNER_UNKNOWN", BIND.TRIP_PLANNER_UNKNOWN, "?");

  // T12 pickers (contract 6, round-2-contract section 8.5) over the real buildItems.ts BUILD_ITEMS.
  const META = ITEMS.BUILD_ITEMS;
  const sent = (...ids) => ids.map((id) => item({ id }));
  const sorted = (s) => [...s].sort();
  const FG_ONLY = sent("sign", "lodge", "baseStation", "topStation", "lift", "runNatural", "runGroomed", "runSnowmaking", "runNight");
  const DQ = ["baseStationDetachable", "topStationDetachable", "liftDetachable"];
  const GD = ["baseStationGondola", "topStationGondola", "liftGondola"];
  const NO = R.NO_PICK;
  eq("T12.1 FG-only city: one option per set (no picker row)", R.pickerSets(FG_ONLY, META), { lodge: ["lodge"], lift: ["fixedGrip"] });
  eq("T12.2 FG-only city: today's nine tiles", sorted(R.visibleIds(FG_ONLY, META, NO)),
    ["baseStation", "lift", "lodge", "runGroomed", "runNatural", "runNight", "runSnowmaking", "sign", "topStation"]);
  const TWO_LODGES = FG_ONLY.concat(sent("lodgeMid"));
  eq("T12.3 two tiers sent: lodge set", R.pickerSets(TWO_LODGES, META).lodge, ["lodge", "lodgeMid"]);
  eq("T12.4 default = the first option", [R.visibleIds(TWO_LODGES, META, NO).has("lodge"), R.visibleIds(TWO_LODGES, META, NO).has("lodgeMid")], [true, false]);
  eq("T12.5 chosen tier drawn instead", [R.visibleIds(TWO_LODGES, META, { lodge: "lodgeMid", lift: "" }).has("lodge"),
    R.visibleIds(TWO_LODGES, META, { lodge: "lodgeMid", lift: "" }).has("lodgeMid")], [false, true]);
  const THREE_LODGES = FG_ONLY.concat(sent("lodgeHut", "lodgeGrand"));
  eq("T12.6 three tiers, table order", R.pickerSets(THREE_LODGES, META).lodge, ["lodge", "lodgeHut", "lodgeGrand"]);
  eq("T12.7 a chosen tier that disappeared falls back to the first", R.effectiveSelection(R.pickerSets(THREE_LODGES, META), { lodge: "lodgeMid", lift: "gondola" }),
    { lodge: "lodge", lift: "fixedGrip" });
  eq("T12.8 effectiveSelection of null", R.effectiveSelection({ lodge: [], lift: ["detachable"] }, null), { lodge: "", lift: "detachable" });
  const FG_DQ = FG_ONLY.concat(sent(...DQ));
  eq("T12.9 two lift types", R.pickerSets(FG_DQ, META).lift, ["fixedGrip", "detachable"]);
  eq("T12.10 detachable chosen: its three tiles, no fixed-grip ones", sorted(R.visibleIds(FG_DQ, META, { lodge: "", lift: "detachable" })),
    ["baseStationDetachable", "liftDetachable", "lodge", "runGroomed", "runNatural", "runNight", "runSnowmaking", "sign", "topStationDetachable"]);
  const HALF_DQ = FG_ONLY.concat(sent("baseStationDetachable", "topStationDetachable"));
  eq("T12.11 half-imported type (stations, no line) is not offered", R.pickerSets(HALF_DQ, META).lift, ["fixedGrip"]);
  eq("T12.12 ... and never drawn", [R.visibleIds(HALF_DQ, META, { lodge: "", lift: "detachable" }).has("baseStationDetachable"),
    R.visibleIds(HALF_DQ, META, { lodge: "", lift: "detachable" }).has("baseStation")], [false, true]);
  eq("T12.13 three lift types", R.pickerSets(FG_DQ.concat(sent(...GD)), META).lift, ["fixedGrip", "detachable", "gondola"]);
  eq("T12.14 optionOf", ["lodgeGrand", "liftGondola", "topStation", "sign", "school", "runNight", "bogus", null].map((id) => R.optionOf(id, META)),
    [{ set: "lodge", value: "lodgeGrand" }, { set: "lift", value: "gondola" }, { set: "lift", value: "fixedGrip" }, null, null, null, null, null]);
  eq("T12.15 dotTarget: drawn tile", R.dotTarget("topStation", FG_DQ, META, NO), { kind: "tile", id: "topStation" });
  eq("T12.16 dotTarget: unchosen tier -> its chip", R.dotTarget("lodgeMid", TWO_LODGES, META, NO), { kind: "chip", set: "lodge", value: "lodgeMid" });
  eq("T12.17 dotTarget: fixed-grip item while detachable is chosen -> the fixed-grip chip",
    R.dotTarget("baseStation", FG_DQ, META, { lodge: "", lift: "detachable" }), { kind: "chip", set: "lift", value: "fixedGrip" });
  eq("T12.18 dotTarget: nowhere (half-imported, not sent, none)", [R.dotTarget("baseStationDetachable", HALF_DQ, META, NO),
    R.dotTarget("school", FG_ONLY, META, NO), R.dotTarget(null, FG_ONLY, META, NO), R.dotTarget("runGroomed", FG_ONLY, META, NO)],
    [null, null, null, { kind: "tile", id: "runGroomed" }]);
  eq("T12.19 school and patrol huts are not alternatives", sorted(R.visibleIds(FG_ONLY.concat(sent("school", "patrolHut", "patrolHQ")), META, NO)).filter((x) => /school|patrol/.test(x)),
    ["patrolHQ", "patrolHut", "school"]);
  eq("T12.20 unknown ids and junk are ignored", R.pickerSets([null, { id: 7 }, item({ id: "runPowder" })].concat(FG_ONLY), META), { lodge: ["lodge"], lift: ["fixedGrip"] });

  // T13 price tolerance (contract 6; C# SkiDayRules.PriceFactor(price, tolerance), economy seam; ResortBatch SkiDayTests holds the C# half).
  eq("T13.1 priceFactor(price, tolerance)", [[80, 0], [80, 20], [80, 40], [200, 40], [250, 40], [0, 40], [200, 0]].map(([p, t]) => R.priceFactor(p, t)),
    [0.8, 0.9, 1, 0.4, 0.4, 1, 0.2]);
  eq("T13.2 priceFactorPct(price, tolerance)", [[80, 0], [80, 20], [80, 40], [200, 40], [250, 40]].map(([p, t]) => R.priceFactorPct(p, t)), [80, 90, 100, 40, 40]);
  eq("T13.3 a bad tolerance reads 0", [R.priceFactor(80, -20), R.priceFactor(80, NaN), R.priceFactorPct(80, Infinity)], [0.8, 0.8, 80]);
  eq("T13.4 priceFullWith", [R.priceFullWith(40), R.priceFullWith(20), R.priceFullWith(0), R.priceFullWith(-5)], [80, 60, 40, 40]);

  // T14 contract 6 band rows, ids, lift types, receipt and money helpers.
  eq("T14.1 codes 23-25 variants", [23, 24, 25].map((c) => R.problemVariant(P(A, c, c === 23 ? 2 : 1))), ["mixedLiftTypes", "summitLodgeNotLiftServed", "schoolNoGreenRun"]);
  eq("T14.2 codes 23-25 clicks", [23, 24, 25].map((c) => R.problemClick(P(A, c, 1))), ["lift", "sign", "sign"]);
  eq("T14.3 kept in the band, C# order; the mixed row's line item is the dot",
    [R.bandRowsOf([P(A, 23, 2, { buildItem: "liftDetachable" }), P(A, 24, 1, { count: 1 }), P(A, 25, 1)], A).map((p) => p.code),
      R.dotItem([P(A, 23, 2, { buildItem: "liftDetachable" })], A, true)], [[23, 24, 25], "liftDetachable"]);
  eq("T14.4 isBuildItemId: all 26 ids", BIND.BUILD_ITEM_IDS.filter((id) => R.isBuildItemId(id)).length, 26);   // T54 probe 1: + runCatTrack
  eq("T14.5 isBuildItemId: strangers", ["lift2", "", null, "Lodge", "liftCable"].map((id) => R.isBuildItemId(id)), [false, false, false, false, false]);
  eq("T14.6 liftTypesOf", [R.liftTypesOf([{ type: "fixedGrip" }, { type: "fixedGrip" }, { type: "" }, { type: "detachable" }, {}]), R.liftTypesOf(null)],
    [["fixedGrip", "detachable"], []]);
  eq("T14.7 receiptPartsVariant", [R.receiptPartsVariant({ schoolValue: 12 }), R.receiptPartsVariant({ schoolValue: 0 }), R.receiptPartsVariant({ schoolValue: -1 }),
    R.receiptPartsVariant({}), R.receiptPartsVariant(null)], ["school", "plain", "plain", "plain", "plain"]);
  eq("T14.8 lodgeSpendShown", [R.lodgeSpendShown({ lodgeSpendMonth: 150 }), R.lodgeSpendShown({ lodgeSpendMonth: 0 }), R.lodgeSpendShown({ lodgeSpendMonth: -1 }),
    R.lodgeSpendShown(null)], [true, false, false, false]);
  // Contract 7 (season passes v1): the pass rows show while the share is above 0 or a pass sold / a holder skied.
  const pl = (o) => Object.assign({ passesSoldSeason: 0, passHoldersThisMonth: 0, passHoldersLastMonth: 0 }, o);
  eq("T15.1 passesShown", [R.passesShown(pl({}), { passShare: 50 }), R.passesShown(pl({}), { passShare: 0 }), R.passesShown(pl({ passesSoldSeason: 3 }), { passShare: 0 }),
    R.passesShown(pl({ passHoldersThisMonth: 4 }), { passShare: 0 }), R.passesShown(pl({ passHoldersLastMonth: 1 }), { passShare: 0 }),
    R.passesShown(pl({ passesSoldSeason: -1, passHoldersThisMonth: -1, passHoldersLastMonth: -1 }), { passShare: 0 }), R.passesShown(null, { passShare: 50 })],
    [true, false, true, true, true, false, false]);
  // Contract 8 (econ-2 round 2): lessons, the pre-season sale and the city-income estimate rows.
  eq("T16.1 lessonsShown", [R.lessonsShown({ lessonsThisMonth: 3, lessonsLastMonth: 0 }), R.lessonsShown({ lessonsThisMonth: 0, lessonsLastMonth: 5 }),
    R.lessonsShown({ lessonsThisMonth: 0, lessonsLastMonth: 0 }), R.lessonsShown({ lessonsThisMonth: -1, lessonsLastMonth: -1 }), R.lessonsShown(null)],
    [true, true, false, false, false]);
  eq("T16.2 presaleShown", [R.presaleShown({ passesPreSeason: 12 }), R.presaleShown({ passesPreSeason: 0 }), R.presaleShown({ passesPreSeason: -1 }),
    R.presaleShown(null)], [true, false, false, false]);
  eq("T16.3 cityIncomeShown", [R.cityIncomeShown({ cityIncomeEst: 4200 }, { tourismOverhaul: false }), R.cityIncomeShown({ cityIncomeEst: 0 }, { tourismOverhaul: false }),
    R.cityIncomeShown({ cityIncomeEst: -1 }, { tourismOverhaul: false }), R.cityIncomeShown({ cityIncomeEst: 4200 }, { tourismOverhaul: true }),
    R.cityIncomeShown(null, { tourismOverhaul: false })], [true, true, false, false, false]);
  // T31 (2026-10-16): the Build strip's three tabs over the real BUILD_ITEMS table.
  const kindTabs = {};
  for (const m of META) kindTabs[m.id] = R.tabOfItem(m.id, META);
  eq("T17.1 every one of the 26 items is on exactly one tab, by kind", [Object.keys(kindTabs).length, Object.values(kindTabs).filter((x) => x === null).length], [26, 0]);
  eq("T17.2 tab sizes: 12 lifts, 5 runs, 9 buildings", [Object.values(kindTabs).filter((x) => x === "lifts").length, Object.values(kindTabs).filter((x) => x === "runs").length,
    Object.values(kindTabs).filter((x) => x === "buildings").length], [12, 5, 9]);   // T54 probe 1: the cat track is the fifth run tile
  eq("T17.2b the cat track is a run item of kind 4 on the runs tab", [kindTabs.runCatTrack, R.runKindOfItem("runCatTrack"), R.RUN_ITEMS.has("runCatTrack")], ["runs", 4, true]);
  eq("T17.3 spot rows", [kindTabs.sign, kindTabs.lodgeGrand, kindTabs.school, kindTabs.patrolHQ, kindTabs.liftTram, kindTabs.topStationGondola, kindTabs.runNight],
    ["buildings", "buildings", "buildings", "buildings", "lifts", "lifts", "runs"]);
  eq("T17.4 tabOfItem of an unknown id / null", [R.tabOfItem("nope", META), R.tabOfItem(null, META), R.tabOfKind("weird")], [null, null, null]);
  eq("T17.5 drawnTabs: FG-only city draws all three, table order", R.drawnTabs(R.visibleIds(FG_ONLY, META, NO), META), ["lifts", "runs", "buildings"]);
  eq("T17.6 drawnTabs: no lift items sent -> no Lifts tab", R.drawnTabs(R.visibleIds(sent("sign", "lodge", "runNatural"), META, NO), META), ["runs", "buildings"]);
  eq("T17.7 drawnTabs: nothing sent -> none", R.drawnTabs(new Set(), META), []);
  const ALL3 = ["lifts", "runs", "buildings"];
  eq("T17.8 effectiveTab: the remembered tab wins while drawn", [R.effectiveTab(ALL3, "runs", "lift"), R.effectiveTab(ALL3, "buildings", "done")], ["runs", "buildings"]);
  eq("T17.9 effectiveTab: none remembered -> the NEXT step's tab", [R.effectiveTab(ALL3, "", "sign"), R.effectiveTab(ALL3, "", "lodge"), R.effectiveTab(ALL3, "", "lift"),
    R.effectiveTab(ALL3, "", "run")], ["buildings", "buildings", "lifts", "runs"]);
  eq("T17.10 effectiveTab: done / unlinked -> the first drawn; a remembered tab that is not drawn falls back", [R.effectiveTab(ALL3, "", "done"),
    R.effectiveTab(["runs", "buildings"], "lifts", "unlinked"), R.effectiveTab(["runs", "buildings"], "lifts", "lift"), R.effectiveTab([], "runs", "run")], ["lifts", "runs", "runs", ""]);
  eq("T17.11 tabOfNext", [R.tabOfNext("sign"), R.tabOfNext("unlinked"), R.tabOfNext("lodge"), R.tabOfNext("lift"), R.tabOfNext("run"), R.tabOfNext("done")],
    ["buildings", null, "buildings", "lifts", "runs", null]);
  eq("T17.12 tabOfDot: tile, chip, none", [R.tabOfDot({ kind: "tile", id: "runGroomed" }, META), R.tabOfDot({ kind: "chip", set: "lift", value: "gondola" }, META),
    R.tabOfDot({ kind: "chip", set: "lodge", value: "lodgeMid" }, META), R.tabOfDot(null, META)], ["runs", "lifts", "buildings", null]);
  eq("T14.9 BUILD_ITEMS covers every id once, table order = BUILD_ITEM_IDS", META.map((m) => m.id), BIND.BUILD_ITEM_IDS);

  // T18 (T80, 2026-10-18; PIPELINE section 0 "Hub UI design rule"): source checks over the texts and the Attraction view.
  // (a) Snow is ALWAYS two numbers, depth in cm and run coverage in % (Richard's pet peeve, three rounds): the MOUNTAIN snow entries
  // name both, no "Snow now" label survives, and OverviewView.tsx renders both fields. (b) No wall of text: every tooltip entry (a key
  // with "TIP") is at most 25 words; a word is a token with a letter, a digit or a {PLACEHOLDER} ("·", ">", "×", "%" do not count).
  // The two lift-money tips carry live figures and a billing rule: capped at their T80 length instead (CG-T8 caps them at 42 / 44).
  const textSrc = fs.readFileSync(path.join(srcDir, "hub", "hubText.ts"), "utf8") + "\n" + fs.readFileSync(path.join(srcDir, "l10n.ts"), "utf8");
  const tuples = new Map();
  for (const m of textSrc.matchAll(/\["([^"]+)",\s*"((?:[^"\\]|\\.)*)"\]/g)) if (!tuples.has(m[1])) tuples.set(m[1], m[2]);
  const depth = tuples.get("SkiResort.Hub.MOUNTAIN[SnowDepth]") || "";
  const cover = tuples.get("SkiResort.Hub.MOUNTAIN[SnowCover]") || "";
  eq("T18.1 MOUNTAIN snow: one entry names the depth, one the coverage", [/depth/i.test(depth), /coverage/i.test(cover)], [true, true]);
  eq("T18.2 no 'Snow now' label and no single-number snow key left", [[...tuples.values()].some((v) => /^Snow now/i.test(v)),
    ["SkiResort.Hub.MOUNTAIN[Snow]", "SkiResort.Hub.MOUNTAIN[SnowMeasured]", "SkiResort.Hub.MOUNTAIN[SnowEstimated]"].some((k) => tuples.has(k))], [false, false]);
  const overview = fs.readFileSync(path.join(srcDir, "hub", "OverviewView.tsx"), "utf8");
  eq("T18.3 OverviewView renders depth (snowDepthCm) and coverage (snowPct) with their labels",
    ["HUB_LOC.mountainSnowDepth", "HUB_LOC.mountainSnowCover", "snowDepthCm", "live.snowPct"].map((s) => overview.includes(s)), [true, true, true, true]);
  const words = (s) => s.split(/\s+/).filter((w) => /[A-Za-z0-9{]/.test(w)).length;
  const CAPS = { "SkiResort.Hub.MONEY_TIP[Lifts]": 32, "SkiResort.Hub.MONEY_TIP[LiftsTypes]": 35 };
  const over = [];
  for (const [k, v] of tuples) if (/TIP/.test(k) && words(v) > (CAPS[k] ?? 25)) over.push(`${k} ${words(v)}`);
  eq("T18.4 every tooltip text is at most 25 words (lift-money tips: their T80 caps)", over, []);

  // T19 (T80, Richard item 3): the ski school's row state; the Lessons and School rows flag a 0 with "No ski school" / "No green run".
  const S1 = { index: 1, version: 1 }, S2 = { index: 2, version: 1 };
  const P25 = [{ sign: S1, code: BIND.PROBLEM.SchoolNoGreenRun }];
  eq("T19.1 schoolState", [R.schoolState({ schoolValue: 12 }, P25, S1), R.schoolState({ schoolValue: 0 }, P25, S1), R.schoolState({ schoolValue: 0 }, P25, S2),
    R.schoolState({ schoolValue: 0 }, [], S1), R.schoolState({ schoolValue: -1 }, [], S1), R.schoolState(null, P25, S1)],
    ["on", "noGreen", "none", "none", "unknown", "unknown"]);

  // T20 (T78 build 2, contract 10; docs/research/ui/hub-history-build-2-2026-10-18.md): the History view. Source checks plus the pure
  // helper's own oracles (tools/test-history-rules.js, run here so gate R18 covers them).
  const read = (rel) => fs.readFileSync(path.join(srcDir, rel), "utf8");
  const csDir = path.resolve(__dirname, "..", "..", "SkiResortMod");
  const stateSrc = read(path.join("hub", "hubState.ts"));
  const viewsLit = (stateSrc.match(/export const HUB_VIEWS: readonly HubView\[\] = \[([^\]]*)\]/) || [, ""])[1].split(",").map((s) => s.trim().replace(/"/g, "")).filter(Boolean);
  eq("T20.1 HUB_VIEWS: six views, history after money (Finances)", viewsLit, ["overview", "runs", "lifts", "guests", "money", "history"]);
  const locale = fs.readFileSync(path.join(csDir, "LocaleEN.cs"), "utf8");
  const csTuples = new Map();
  for (const m of locale.matchAll(/\{\s*"([^"]+)",\s*"((?:[^"\\]|\\.)*)"\s*\}/g)) csTuples.set(m[1], m[2]);
  const histKeys = [...tuples.keys()].filter((k) => /^SkiResort\.Hub\.(HISTORY|TILE\[History\]|TILE_TIP\[History\]|SECTION\[History\])/.test(k));
  eq("T20.2 every HISTORY key of hubText.ts is in LocaleEN.cs with the same text (45 keys)",
    [histKeys.length, histKeys.filter((k) => csTuples.get(k) !== tuples.get(k))], [45, []]);
  const viewSrc = read(path.join("hub", "HistoryView.tsx"));
  const H = HR;
  eq("T20.3 four range options (12 months, 2 seasons, 5 years, all), each with a label and a tooltip in HistoryView",
    [H.HISTORY_RANGES, ["RANGE_LABEL", "RANGE_TIP"].map((m) => (viewSrc.match(new RegExp(`const ${m}[^=]*= \\{([^}]*)\\}`)) || [, ""])[1].split("\n").filter((l) => /:\s*HUB_LOC\./.test(l)).length),
      viewSrc.includes("options={HISTORY_RANGES}")], [["months12", "seasons2", "years5", "all"], [4, 4], true]);
  const allSrc = [];
  const walk = (d) => { for (const e of fs.readdirSync(d, { withFileTypes: true })) { const p = path.join(d, e.name); if (e.isDirectory()) walk(p); else if (/\.(ts|tsx)$/.test(e.name)) allSrc.push([p, fs.readFileSync(p, "utf8")]); } };
  walk(srcDir);
  eq("T20.4 no chart.js import in src/ (inline SVG, React only)", allSrc.filter(([, s]) => /(from\s+|require\s*\(\s*|import\s*\(\s*)["']chart\.js["']/.test(s)).map(([p]) => path.relative(srcDir, p)), []);
  const histCs = fs.readFileSync(path.join(csDir, "Systems", "SkiResortHistory.cs"), "utf8");
  const hubCs = fs.readFileSync(path.join(csDir, "Systems", "SkiResortHubUISystem.cs"), "utf8");
  const csFlags = {};
  for (const m of (histCs.match(/class HistoryFlags\s*\{([\s\S]*?)\}/) || [, ""])[1].matchAll(/const byte (\w+) = (\d+);/g)) csFlags[m[1]] = Number(m[2]);
  csFlags.Current = Number((hubCs.match(/public const int HistoryFlagCurrent = (\d+);/) || [, "-1"])[1]);
  eq("T20.5 HISTORY_FLAG = C# HistoryFlags (stored bits) + HistoryFlagCurrent (binding only)", BIND.HISTORY_FLAG, csFlags);
  const historyUsers = allSrc.filter(([p, s]) => /\bhistory\$/.test(s) && !/bindings\.ts$/.test(p)).map(([p]) => path.relative(srcDir, p).split(path.sep).join("/"));
  eq("T20.6 history$ is subscribed only by the History view (C# builds `history` only while it is observed)", historyUsers, ["hub/HistoryView.tsx"]);
  eq("T20.7 the History view draws no snow (the store has coverage but no depth; snow is never one number, T18)", /snowPct|snowDepth/.test(viewSrc), false);
  eq("T20.8 the History tile shows a glyph, not a number from `history` (ViewTiles never reads history$)", /history\$/.test(read(path.join("hub", "ViewTiles.tsx"))), false);
  const hr = require("./test-history-rules").run(srcDir);
  checks += hr.checks;
  for (const f of hr.failures) failures.push(`test-history-rules ${f}`);
  return { checks, failures };
}

module.exports = { run };
if (require.main === module) {
  const r = run(process.argv[2] || path.join(__dirname, "..", "src"));
  for (const f of r.failures) console.error(`test-hub-rules: FAIL ${f}`);
  console.log(`test-hub-rules: ${r.checks} check(s), failures=${r.failures.length}`);
  process.exit(r.failures.length ? 1 : 0);
}
