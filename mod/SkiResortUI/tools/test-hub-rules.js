/*
 * tools/test-hub-rules.js - literal oracles for src/hub/hubRules.ts (pure display rules); tools/check-bundle.js runs it as
 * rule R18 (full gate and --contract-only, against --src= when given). Standalone: `node tools/test-hub-rules.js [srcDir]`
 * prints `test-hub-rules: N check(s), failures=0`.
 * Compiles src/bindings.ts and src/hub/hubRules.ts with the installed typescript (transpileModule, CommonJS) and runs them in
 * a vm context: cs2/api is stubbed (bindValue returns { value: fallback }, trigger is a no-op), __BUILD_STAMP__ = "test".
 * No new npm package. Exports run(srcDir) -> { checks, failures: string[] }.
 * T11 reads bindings.ts too (FALLBACK.city, TRIP_PLANNER_UNKNOWN), the same compiled module hubRules.ts imports. priceFactor /
 * priceFactorPct take a tolerance, so the T10 table rows call them through an arrow (Array.map would pass the INDEX as the
 * tolerance). T12 runs the pickers over the real buildItems.ts BUILD_ITEMS, which load() compiles too. T20 checks the History
 * view and runs tools/test-history-rules.js.
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
      // hubRules.ts itself may load no value but ../bindings - a cs2/* value import would otherwise pass against the stubs
      // below (type-only imports are erased by transpileModule and never reach here).
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
  // The bindings module hubRules.ts loaded (same cache entry, so the same values) for T11; buildItems.ts (BUILD_ITEMS, the table
  // the strip passes to the pickers) for T12, importing values only from ../bindings (the purity check above applies to
  // hubRules.ts only); historyRules.ts for T20, pure like hubRules.ts (its own purity check is in tools/test-history-rules.js).
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

  // T2 band rows, factor rows, slots, dot (Copper Bowl)
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
  // SignNoRoad 18, LodgeNoRoad 19.
  eq("T2.13 road rows kept in the band, C# order", R.bandRowsOf([P(A, 18, 3), P(A, 4, 2, { pct: 12 }), P(A, 19, 1, { count: 1 })], A).map((p) => p.code), [18, 4, 19]);
  eq("T2.14 road rows name no build item", R.dotItem([P(A, 18, 3), P(A, 19, 3, { count: 1 })], A, true), null);
  // SkiDaysCannotReach 20, NoParkingNearLodge 21, LodgeNotSlopeside 22.
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
  eq("T3.16 road row icons: vanilla's Road required icon", [R.problemIcon(P(A, 18, 3)), R.problemIcon(P(A, 19, 1)), R.problemIcon(P(A, 29, 2))],
    [{ kind: "img", src: "Media/Game/Notifications/RoadNotConnected.svg" }, { kind: "img", src: "Media/Game/Notifications/RoadNotConnected.svg" },
      { kind: "img", src: "Media/Game/Notifications/RoadNotConnected.svg" }]);
  eq("T3.17 ski-day variants", [R.problemVariant(P(A, 20, 2)), R.problemVariant(P(A, 21, 1)), R.problemVariant(P(A, 22, 1))], ["skiDaysCannotReach", "noParkingNearLodge", "lodgeNotSlopeside"]);
  eq("T3.18 ski-day rows fly to their subject", [20, 21, 22].map((c) => R.problemClick(P(A, c, 1))), ["sign", "sign", "sign"]);
  eq("T3.19 ski-day row icons", [R.problemIcon(P(A, 20, 2)), R.problemIcon(P(A, 21, 1)), R.problemIcon(P(A, 22, 1))],
    [{ kind: "img", src: "Media/Game/Notifications/PathfindFailed.svg" }, { kind: "img", src: "Media/Game/Icons/Parking.svg" }, { kind: "glyph", src: "Media/Glyphs/Info.svg", tone: "info" }]);

  // T4 status line
  const row = (runs, kind = 0) => ({ kind, runs, liftRows: 3 });
  const lv = (status, est = false, extra = {}) => Object.assign({ status, snowEstimated: est, runsOpen: 4, runsPartly: 1, liftsComplete: 2, nextStep: 0, ticketGate: 0, ticketIncomeMonth: 8000, ticketIncomeEstimated: false, buildingsMonth: 6000, wagesMonth: 15000 }, extra);
  eq("T4.1 unlinked", R.statusVariant(row(2, 1), null), "unlinked");
  eq("T4.2 unknown", R.statusVariant(row(7), null), "unknown");
  eq("T4.2b not known yet", R.statusVariant(row(7), lv(0, false, { runsOpen: -1 })), "unknown");
  eq("T4.2c not known yet: no hint", R.statusHint(row(7), lv(0, false, { runsOpen: -1 })), "none");
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
  // "Powder day" at 50 (lv(3) has no skiDaysPowderPct: a row without the field reads "season").
  eq("T4.14 status hint", [R.statusHint(row(5), lv(3, false, { skiDaysPowderPct: 50 })), R.statusHint(row(5), lv(3, false, { skiDaysPowderPct: 49 })),
    R.statusHint(row(5), lv(3, false, { skiDaysPowderPct: -1 })), R.statusHint(row(5), lv(3)), R.statusHint(row(0), lv(0, false, { skiDaysPowderPct: 100 })),
    R.statusHint(row(2, 1), lv(3, false, { skiDaysPowderPct: 100 })), R.statusHint(row(5), null)],
    ["powder", "season", "season", "season", "none", "none", "none"]);
  // The slot shows the snow while it is known, except a warning hint (Thawing 3, Waiting for snow 4, Too warm 5) or powder.
  const sn = (extra) => lv(3, false, Object.assign({ snowDepthCm: 64, snowPct: 88, seasonHint: 1 }, extra));
  eq("T4.15 status hint with snow", [R.statusHint(row(5), sn({})), R.statusHint(row(5), sn({ seasonHint: 3 })), R.statusHint(row(5), sn({ seasonHint: 4 })),
    R.statusHint(row(5), sn({ seasonHint: 5 })), R.statusHint(row(5), sn({ seasonHint: 6 })), R.statusHint(row(5), sn({ snowDepthCm: -1, snowPct: -1 })),
    R.statusHint(row(5), sn({ snowDepthCm: -1 })), R.statusHint(row(5), sn({ seasonHint: 5, skiDaysPowderPct: 60 })), R.statusHint(row(0), sn({}))],
    ["snow", "warn", "warn", "warn", "snow", "season", "snow", "powder", "none"]);

  // T5 footer
  eq("T5.1 next no sign", R.nextVariant(false, false, null), "sign");
  eq("T5.2 next unlinked", R.nextVariant(true, true, null), "unlinked");
  eq("T5.3 next steps", [0, 1, 2, 3].map((s) => R.nextVariant(true, false, { nextStep: s })), ["done", "lodge", "lift", "run"]);
  eq("T5.4 next missing live", R.nextVariant(true, false, null), "done");
  const item = (extra) => Object.assign({ id: "x", locked: false, active: false, reason: "" }, extra);
  // line1Variant takes the item's KIND (buildItems.ts BuildItemMeta.kind), not its id.
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
  // Every kind (a line of any lift type reads "about per km"; an unknown kind reads its cost).
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
  // A since-load last month is "partial", never "–".
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
  // The fixed ladder 50, 100, 200, 500, 1000, 2000, and two field-observed cases.
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
  // No live row, or liftsComplete -1 (Not linked, no result) -> 0, never a guess.
  eq("T8.6 unfinishedLifts", [R.unfinishedLifts(row(7), lv(2)), R.unfinishedLifts(row(7), null), R.unfinishedLifts({ liftRows: 1 }, lv(2))], [1, 0, 0]);
  eq("T8.7 unfinishedLifts unknown complete", R.unfinishedLifts(row(7), Object.assign({}, lv(2), { liftsComplete: -1 })), 0);
  // Lone stations pair across rows (the census never builds a StationsOnly row with both).
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
  // A complete, powered lift billed at 25 % reads "no open run" (Running 0, ChairsStopped 2).
  eq("T8.11 liftCostReason", [lift(0, 0, { billedReduced: false }), lift(1, 0, { billedReduced: true }), lift(3, 2, { billedReduced: true }),
    lift(4, 3, { billedReduced: true }), lift(0, 0, { billedReduced: true }), lift(2, 1, { billedReduced: true })].map(R.liftCostReason),
    [null, "noPower", "sectionMissing", "unfinished", "noOpenRun", "noOpenRun"]);

  // T9 ski days: the state line, its tooltip and the walk count.
  const sd = (state, limit = 0, extra = {}) => Object.assign({ skiDaysState: state, skiDaysLimit: limit, skiDaysCap: 154 }, extra);
  eq("T9.1 skiDayLine states", [0, 1, 2, 3, 4, 5, 7, 99].map((s) => R.skiDayLine(sd(s))), ["unknown", "off", "signOff", "noRun", "noLodge", "night", "full", "unknown"]);
  eq("T9.2 skiDayLine room by limit", [R.skiDayLine(sd(6, 1)), R.skiDayLine(sd(6, 2)), R.skiDayLine(sd(6, 0)), R.skiDayLine(null)], ["roomRuns", "roomLodges", "room", "unknown"]);
  eq("T9.3 skiDayWalk", [R.skiDayWalk({ skiDaysSent: 52, skiDaysSentCar: 41, skiDaysSentNoCar: 3 }), R.skiDayWalk({ skiDaysSent: 5, skiDaysSentCar: 4, skiDaysSentNoCar: 3 }),
    R.skiDayWalk({ skiDaysSent: -1, skiDaysSentCar: -1, skiDaysSentNoCar: -1 }), R.skiDayWalk(null)], [8, 0, -1, -1]);
  eq("T9.4 skiDayTip", [R.skiDayTip(sd(1)), R.skiDayTip(sd(4)), R.skiDayTip(sd(6, 1)), R.skiDayTip(sd(7, 2)), R.skiDayTip(sd(5)), R.skiDayTip(sd(6, 0))],
    ["off", "noLodge", "runs", "lodges", null, null]);

  // T10 ticket price line: the price factor; T10.1's table is also ResortBatch HubRulesTests H17 (the C# half, on factors).
  const prices = [0, 5, 40, 45, 80, 100, 120, 150, 200, 260];
  // priceFactor / priceFactorPct take a tolerance (default 0), so every row calls them through an arrow.
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

  // T11 trip-planner note.
  const tp = (tripPlanner, tripPlannerBlocksSkiDays) => Object.assign({}, BIND.FALLBACK.city, { known: true, tripPlanner, tripPlannerBlocksSkiDays });
  eq("T11.1 tripPlannerNote fallback city", R.tripPlannerNote(BIND.FALLBACK.city), "none");
  eq("T11.2 known mod", R.tripPlannerNote(tp("Realistic PathFinding", false)), "known");
  eq("T11.3 known mod blocks ski days", R.tripPlannerNote(tp("Realistic Trips", true)), "knownOff");
  eq("T11.4 unknown", R.tripPlannerNote(tp("?", false)), "unknown");
  eq("T11.5 unknown blocks ski days", R.tripPlannerNote(tp("?", true)), "unknownOff");
  eq("T11.6 empty label", R.tripPlannerNote(tp("", true)), "none");
  eq("T11.7 no city, no field", [null, { tripPlanner: undefined }].map(R.tripPlannerNote), ["none", "none"]);
  eq("T11.8 TRIP_PLANNER_UNKNOWN", BIND.TRIP_PLANNER_UNKNOWN, "?");

  // T12 pickers over the real buildItems.ts BUILD_ITEMS.
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
  // Every sent lodge tier is its own Buildings tile; the lodge choice does not filter.
  eq("T12.4 T114: every sent tier drawn", [R.visibleIds(TWO_LODGES, META, NO).has("lodge"), R.visibleIds(TWO_LODGES, META, NO).has("lodgeMid")], [true, true]);
  eq("T12.5 T114: a lodge choice hides no tier", [R.visibleIds(TWO_LODGES, META, { lodge: "lodgeMid", lift: "" }).has("lodge"),
    R.visibleIds(TWO_LODGES, META, { lodge: "lodgeMid", lift: "" }).has("lodgeMid")], [true, true]);
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
  eq("T12.16 T114: dotTarget on a lodge tier -> its own tile (no lodge chips)", R.dotTarget("lodgeMid", TWO_LODGES, META, NO), { kind: "tile", id: "lodgeMid" });
  eq("T12.17 dotTarget: fixed-grip item while detachable is chosen -> the fixed-grip chip",
    R.dotTarget("baseStation", FG_DQ, META, { lodge: "", lift: "detachable" }), { kind: "chip", set: "lift", value: "fixedGrip" });
  eq("T12.18 dotTarget: nowhere (half-imported, not sent, none)", [R.dotTarget("baseStationDetachable", HALF_DQ, META, NO),
    R.dotTarget("school", FG_ONLY, META, NO), R.dotTarget(null, FG_ONLY, META, NO), R.dotTarget("runGroomed", FG_ONLY, META, NO)],
    [null, null, null, { kind: "tile", id: "runGroomed" }]);
  eq("T12.19 school and patrol huts are not alternatives", sorted(R.visibleIds(FG_ONLY.concat(sent("school", "patrolHut", "patrolHQ")), META, NO)).filter((x) => /school|patrol/.test(x)),
    ["patrolHQ", "patrolHut", "school"]);
  eq("T12.20 unknown ids and junk are ignored", R.pickerSets([null, { id: 7 }, item({ id: "runPowder" })].concat(FG_ONLY), META), { lodge: ["lodge"], lift: ["fixedGrip"] });

  // T13 price tolerance (C# SkiDayRules.PriceFactor(price, tolerance); ResortBatch SkiDayTests holds the C# half).
  eq("T13.1 priceFactor(price, tolerance)", [[80, 0], [80, 20], [80, 40], [200, 40], [250, 40], [0, 40], [200, 0]].map(([p, t]) => R.priceFactor(p, t)),
    [0.8, 0.9, 1, 0.4, 0.4, 1, 0.2]);
  eq("T13.2 priceFactorPct(price, tolerance)", [[80, 0], [80, 20], [80, 40], [200, 40], [250, 40]].map(([p, t]) => R.priceFactorPct(p, t)), [80, 90, 100, 40, 40]);
  eq("T13.3 a bad tolerance reads 0", [R.priceFactor(80, -20), R.priceFactor(80, NaN), R.priceFactorPct(80, Infinity)], [0.8, 0.8, 80]);
  eq("T13.4 priceFullWith", [R.priceFullWith(40), R.priceFullWith(20), R.priceFullWith(0), R.priceFullWith(-5)], [80, 60, 40, 40]);

  // T14 band rows 23-25, ids, lift types, receipt and money helpers.
  eq("T14.1 codes 23-25 variants", [23, 24, 25].map((c) => R.problemVariant(P(A, c, c === 23 ? 2 : 1))), ["mixedLiftTypes", "summitLodgeNotLiftServed", "schoolNoGreenRun"]);
  eq("T14.2 codes 23-25 clicks", [23, 24, 25].map((c) => R.problemClick(P(A, c, 1))), ["lift", "sign", "sign"]);
  eq("T14.3 kept in the band, C# order; the mixed row's line item is the dot",
    [R.bandRowsOf([P(A, 23, 2, { buildItem: "liftDetachable" }), P(A, 24, 1, { count: 1 }), P(A, 25, 1)], A).map((p) => p.code),
      R.dotItem([P(A, 23, 2, { buildItem: "liftDetachable" })], A, true)], [[23, 24, 25], "liftDetachable"]);
  eq("T14.4 isBuildItemId: all 28 ids", BIND.BUILD_ITEM_IDS.filter((id) => R.isBuildItemId(id)).length, 28); // includes runCatTrack, safetyFence and busDepot
  eq("T14.5 isBuildItemId: strangers", ["lift2", "", null, "Lodge", "liftCable"].map((id) => R.isBuildItemId(id)), [false, false, false, false, false]);
  eq("T14.6 liftTypesOf", [R.liftTypesOf([{ type: "fixedGrip" }, { type: "fixedGrip" }, { type: "" }, { type: "detachable" }, {}]), R.liftTypesOf(null)],
    [["fixedGrip", "detachable"], []]);
  eq("T14.7 receiptPartsVariant", [R.receiptPartsVariant({ schoolValue: 12 }), R.receiptPartsVariant({ schoolValue: 0 }), R.receiptPartsVariant({ schoolValue: -1 }),
    R.receiptPartsVariant({}), R.receiptPartsVariant(null)], ["school", "plain", "plain", "plain", "plain"]);
  eq("T14.8 lodgeSpendShown", [R.lodgeSpendShown({ lodgeSpendMonth: 150 }), R.lodgeSpendShown({ lodgeSpendMonth: 0 }), R.lodgeSpendShown({ lodgeSpendMonth: -1 }),
    R.lodgeSpendShown(null)], [true, false, false, false]);
  // The pass rows show while the share is above 0 or a pass sold / a holder skied.
  const pl = (o) => Object.assign({ passesSoldSeason: 0, passHoldersThisMonth: 0, passHoldersLastMonth: 0 }, o);
  eq("T15.1 passesShown", [R.passesShown(pl({}), { passShare: 50 }), R.passesShown(pl({}), { passShare: 0 }), R.passesShown(pl({ passesSoldSeason: 3 }), { passShare: 0 }),
    R.passesShown(pl({ passHoldersThisMonth: 4 }), { passShare: 0 }), R.passesShown(pl({ passHoldersLastMonth: 1 }), { passShare: 0 }),
    R.passesShown(pl({ passesSoldSeason: -1, passHoldersThisMonth: -1, passHoldersLastMonth: -1 }), { passShare: 0 }), R.passesShown(null, { passShare: 50 })],
    [true, false, true, true, true, false, false]);
  // T16 lessons, the pre-season sale and the city-income estimate rows.
  eq("T16.1 lessonsShown", [R.lessonsShown({ lessonsThisMonth: 3, lessonsLastMonth: 0 }), R.lessonsShown({ lessonsThisMonth: 0, lessonsLastMonth: 5 }),
    R.lessonsShown({ lessonsThisMonth: 0, lessonsLastMonth: 0 }), R.lessonsShown({ lessonsThisMonth: -1, lessonsLastMonth: -1 }), R.lessonsShown(null)],
    [true, true, false, false, false]);
  eq("T16.2 presaleShown", [R.presaleShown({ passesPreSeason: 12 }), R.presaleShown({ passesPreSeason: 0 }), R.presaleShown({ passesPreSeason: -1 }),
    R.presaleShown(null)], [true, false, false, false]);
  eq("T16.3 cityIncomeShown", [R.cityIncomeShown({ cityIncomeEst: 4200 }, { tourismOverhaul: false }), R.cityIncomeShown({ cityIncomeEst: 0 }, { tourismOverhaul: false }),
    R.cityIncomeShown({ cityIncomeEst: -1 }, { tourismOverhaul: false }), R.cityIncomeShown({ cityIncomeEst: 4200 }, { tourismOverhaul: true }),
    R.cityIncomeShown(null, { tourismOverhaul: false })], [true, true, false, false, false]);
  // The Build strip's three tabs over the real BUILD_ITEMS table.
  const kindTabs = {};
  for (const m of META) kindTabs[m.id] = R.tabOfItem(m.id, META);
  eq("T17.1 every one of the 28 items is on exactly one tab, by kind", [Object.keys(kindTabs).length, Object.values(kindTabs).filter((x) => x === null).length], [28, 0]);
  eq("T17.2 tab sizes: 12 lifts, 6 runs, 9 buildings", [Object.values(kindTabs).filter((x) => x === "lifts").length, Object.values(kindTabs).filter((x) => x === "runs").length,
    Object.values(kindTabs).filter((x) => x === "buildings").length], [12, 6, 10]); // the cat track is the fifth run tile, the safety fence the sixth tile of the Runs tab
  eq("T17.2b the cat track is a run item of kind 4 on the runs tab", [kindTabs.runCatTrack, R.runKindOfItem("runCatTrack"), R.RUN_ITEMS.has("runCatTrack")], ["runs", 4, true]);
  eq("T17.3a the bus depot: kind depot, Buildings tab, in the lodges' group, no picker chip", [META.find((m) => m.id === "busDepot").kind, kindTabs.busDepot, R.tabOfKind("depot"), R.optionOf("busDepot", META)],
    ["depot", "buildings", "buildings", null]);
  eq("T17.3 spot rows", [kindTabs.sign, kindTabs.lodgeGrand, kindTabs.school, kindTabs.patrolHQ, kindTabs.liftTram, kindTabs.topStationGondola, kindTabs.runNight],
    ["buildings", "buildings", "buildings", "buildings", "lifts", "lifts", "runs"]);
  // The safety net fence (a net prefab drawn like a path) is a Runs-tab tile of its own kind, not a run item and not a lift or a building.
  eq("T17.3b the safety fence: kind fence, Runs tab, not a run item, its own group, its own style-E icon (SkiSafetyFence.png, 2026-10-26)",
    [META.find((m) => m.id === "safetyFence").kind, kindTabs.safetyFence, R.RUN_ITEMS.has("safetyFence"), R.runKindOfItem("safetyFence"), R.tabOfKind("fence"),
      ITEMS.BUILD_GROUPS.filter((g) => g.includes("safetyFence")).map((g) => g.length), ITEMS.BUILD_ITEM_ICON.safetyFence === "assetdb://global/0e39601f806f95d2a8a542e3575d7e75",
      R.runToolActive([{ id: "safetyFence", active: true, locked: false, reason: "" }], false)],
    ["fence", "runs", false, undefined, "runs", [1], true, false]);
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

  // The Runs-tab follow: a run-making tool (a Build run item, the cat track included, or Quick runs) becoming active selects Runs once.
  const bi = (id, active) => ({ id, active, locked: false, reason: "" });
  eq("T17.5 runToolActive: run items, cat track and Quick runs count; lifts, buildings and none do not",
    [R.runToolActive([bi("runGroomed", true)], false), R.runToolActive([bi("runCatTrack", true)], false), R.runToolActive([], true),
      R.runToolActive([bi("lift", true), bi("lodge", true)], false), R.runToolActive([bi("runNight", false)], false), R.runToolActive(null, undefined),
      R.runToolActive([bi("nope", true)], false)], [true, true, true, false, false, false, false]);
  // Field test 3: the follow is keyed on WHICH tool is active (quick > lift tool > active Build item id), not on "a run tool is on".
  eq("T17.6a activeToolKey: Quick runs, then the lift tool, then the active Build item, else none",
    [R.activeToolKey([bi("runGroomed", true)], true, true), R.activeToolKey([bi("runGroomed", true)], false, true), R.activeToolKey([bi("runCatTrack", true)], false, false),
      R.activeToolKey([bi("runNight", false)], false, false), R.activeToolKey(null, undefined, undefined), R.activeToolKey([bi("nope", true)], false, false)],
    ["quick", "lift", "runCatTrack", "", "", ""]);
  eq("T17.6b toolView: run items, cat track, Quick runs -> runs; line items, the lift tool -> lifts; stations, buildings, fence, none -> null",
    ["runNatural", "runCatTrack", "quick", "lift", "liftGondola", "baseStation", "lodge", "safetyFence", "", "nope"].map((k) => R.toolView(k)),
    ["runs", "runs", "runs", "lifts", "lifts", null, null, null, null, null]);
  eq("T17.6c toolTabFollow: none -> run tool selects Runs, run tool -> other run tool selects Runs again, run tool -> lift tool selects Lifts",
    [R.toolTabFollow("", "runGroomed"), R.toolTabFollow("runGroomed", "runCatTrack"), R.toolTabFollow("runCatTrack", "quick"), R.toolTabFollow("quick", "lift"),
      R.toolTabFollow("runGroomed", "liftGondola")],
    [{ select: "runs", seen: "runGroomed" }, { select: "runs", seen: "runCatTrack" }, { select: "runs", seen: "quick" }, { select: "lifts", seen: "lift" },
      { select: "lifts", seen: "liftGondola" }]);
  eq("T17.6d toolTabFollow: the same tool staying active selects nothing; a tool without a view, or none, selects nothing and is remembered",
    [R.toolTabFollow("runGroomed", "runGroomed"), R.toolTabFollow("lift", "lift"), R.toolTabFollow("lift", "baseStation"), R.toolTabFollow("runGroomed", ""),
      R.toolTabFollow("", "")],
    [{ select: null, seen: "runGroomed" }, { select: null, seen: "lift" }, { select: null, seen: "baseStation" }, { select: null, seen: "" }, { select: null, seen: "" }]);
  const panelSrc = fs.readFileSync(path.join(srcDir, "hub", "HubPanel.tsx"), "utf8");
  eq("T17.7 the panel mounts the follow once, keys it on the three tool bindings and resets its memory on unmount",
    [(panelSrc.match(/<RunsTabFollow [/]>/g) || []).length, panelSrc.includes("activeToolKey(items, quick?.active, lift?.active)"),
      panelSrc.includes("toolTabFollow(runToolSeen.value, key)"), panelSrc.includes('runToolSeen.value = ""')],
    [1, true, true, true]);
  // T33 (contract 20) the Quick runs "N lines dropped: A, B, C" line: the top three of the scan's reject buckets.
  eq("T33.1 rejectSummary: top three by count, ties by bucket order, total of all",
    [R.rejectSummary([18, 15, 9, 3, 0, 0, 1, 0]), R.rejectSummary([0, 5, 0, 5, 5, 5, 0, 0])],
    [{ total: 46, top: [{ bucket: 0, n: 18 }, { bucket: 1, n: 15 }, { bucket: 2, n: 9 }] }, { total: 20, top: [{ bucket: 1, n: 5 }, { bucket: 3, n: 5 }, { bucket: 4, n: 5 }] }]);
  eq("T33.2 rejectSummary: under 2 dropped lines and missing input say nothing, junk counts are skipped",
    [R.rejectSummary([0, 0, 0, 0, 0, 0, 0, 0]), R.rejectSummary([1, 0, 0, 0, 0, 0, 0, 0]), R.rejectSummary(null), R.rejectSummary([NaN, -4, "x", 2.7, 0])],
    [{ total: 0, top: [] }, { total: 1, top: [] }, { total: 0, top: [] }, { total: 2, top: [{ bucket: 3, n: 2 }] }]);
  const hubTextSrc = fs.readFileSync(path.join(srcDir, "hub", "hubText.ts"), "utf8");
  const rejectKeys = (hubTextSrc.match(/"SkiResort\.Hub\.QUICK_REJECT\[/g) || []).length;
  eq("T33.3 one plain-word text per bucket, the binding has the field and the fallback has the zeros",
    [rejectKeys, BIND.QUICK_REJECT_BUCKETS, BIND.FALLBACK.quickRuns.rejects.length, BIND.HUB_CONTRACT >= 20], [BIND.QUICK_REJECT_BUCKETS, 8, 8, true]);
  // T18 source checks over the texts and the Attraction view. (a) Snow is ALWAYS two numbers, depth in cm and run coverage in %:
  // the MOUNTAIN snow entries name both, no "Snow now" label survives, and OverviewView.tsx renders both fields. (b) No wall of
  // text: every tooltip entry (a key with "TIP") is at most 25 words; a word is a token with a letter, a digit or a {PLACEHOLDER}
  // ("·", ">", "×", "%" do not count). The two lift-money tips carry live figures and a billing rule, so they are capped at
  // their own length (CG-T8 caps them at 42 / 44).
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

  // T19 the ski school's row state; the Lessons and School rows flag a 0 with "No ski school" / "No green run".
  const S1 = { index: 1, version: 1 }, S2 = { index: 2, version: 1 };
  const P25 = [{ sign: S1, code: BIND.PROBLEM.SchoolNoGreenRun }];
  eq("T19.1 schoolState", [R.schoolState({ schoolValue: 12 }, P25, S1), R.schoolState({ schoolValue: 0 }, P25, S1), R.schoolState({ schoolValue: 0 }, P25, S2),
    R.schoolState({ schoolValue: 0 }, [], S1), R.schoolState({ schoolValue: -1 }, [], S1), R.schoolState(null, P25, S1)],
    ["on", "noGreen", "none", "none", "unknown", "unknown"]);
  // A built school (the tile's AlreadyBuilt lock) at 0 is never "No ski school": no counted run (live.summer) -> "waiting", runs
  // count with no row 25 -> "off" (switched off); row 25 still wins; the lock must be the school's.
  const AB = R.REASON_ALREADY_BUILT;
  eq("T19.2 schoolBuiltOf", [R.schoolBuiltOf([{ id: "school", locked: true, reason: AB }]), R.schoolBuiltOf([{ id: "school", locked: true, reason: "SkiResort.Hub.REASON[Locked]" }]),
    R.schoolBuiltOf([{ id: "lodge", locked: true, reason: AB }]), R.schoolBuiltOf([{ id: "school", locked: false, reason: "" }]), R.schoolBuiltOf(null)],
    [true, false, false, false, false]);
  eq("T19.3 schoolState with a built school", [R.schoolState({ schoolValue: 0, summer: true }, [], S1, true), R.schoolState({ schoolValue: 0, summer: false }, [], S1, true),
    R.schoolState({ schoolValue: 0, summer: false }, P25, S1, true), R.schoolState({ schoolValue: 12, summer: false }, [], S1, true),
    R.schoolState({ schoolValue: 0, summer: true }, [], S1, false), R.schoolState({ schoolValue: -1, summer: true }, [], S1, true)],
    ["waiting", "off", "noGreen", "on", "none", "unknown"]);
  eq("T19.4 schoolFlagOf", ["none", "noGreen", "off", "waiting", "on", "unknown"].map(R.schoolFlagOf),
    ["noSchool", "schoolNoGreen", "schoolOff", "schoolWaiting", null, null]);
  eq("T19.5 the field case (Ash Flat 22:13, log: school on, runs 0/0, SUMMER) reads waiting, not none",
    R.schoolState({ schoolValue: 0, summer: true }, [], S1, R.schoolBuiltOf([{ id: "school", locked: true, reason: AB }])), "waiting");
  eq("T19.6 the views pass the lock", [fs.readFileSync(path.join(srcDir, "hub", "OverviewView.tsx"), "utf8").includes("schoolBuiltOf(build)"), fs.readFileSync(path.join(srcDir, "hub", "MoneyView.tsx"), "utf8").includes("schoolBuiltOf(build)")], [true, true]);
  // The run stripe has its own band: .buildTile lays out from the top under an 8 rem padding (the
  // stripe is absolute at top 2, height 4), border-box, height 12 rem + 44 rem x fontScale; never centered content again.
  {
    const scss = fs.readFileSync(path.join(srcDir, "hub", "hub.module.scss"), "utf8");
    const tile = (scss.match(/\n\.buildTile \{[^}]*\}/) || [""])[0].replace(/\/\/[^\n]*/g, "");
    const stripe = (scss.match(/\n\.buildStripe \{[^}]*\}/) || [""])[0].replace(/\/\/[^\n]*/g, "");
    const num = (re, txt) => { const m = txt.match(re); return m ? parseFloat(m[1]) : NaN; };
    const padTop = num(/padding:\s*([\d.]+)rem/, tile), top = num(/\btop:\s*([\d.]+)rem/, stripe), h = num(/\bheight:\s*([\d.]+)rem/, stripe);
    eq("T23.1 build tile: stripe band above the icon (top + height < padding-top), content from the top, border-box",
      [top + h < padTop, /justify-content:\s*flex-start/.test(tile), /box-sizing:\s*border-box/.test(tile),
        tile.includes("height: calc(12rem + 44rem * var(--fontScale))")],
      [true, true, true, true]);
  }
  // The Lodge tile names its tier. No tile says it joins the sign.
  {
    const strip = fs.readFileSync(path.join(srcDir, "hub", "BuildStrip.tsx"), "utf8");
    const header = fs.readFileSync(path.join(srcDir, "hub", "HubHeader.tsx"), "utf8");
    eq("T23.2 tier label, upgrade line", [strip.includes("LODGE_TIER[id] ?? BUILD_LABEL.lodge"),
      (strip.match(/HUB_LOC\.buildActiveUpgrade/g) || []).length], [true, 0]);
  }
  // T24 the header selector. Its item counts (singular forms; the Not linked row adds its free lodges) and the source facts the
  // gate cannot see: the vanilla Dropdown with FOCUS_DISABLED, DropdownItem from the shim, picking = selectResort(entrySign), the
  // drag guard around it, the note.
  eq("T24.1 resortCountsVariant", [[5, 3], [1, 3], [5, 1], [1, 1], [0, 0], [1, 0], [0, 1]].map(([r, l]) => R.resortCountsVariant(r, l)),
    ["many", "runOne", "lodgeOne", "one", "many", "runOne", "lodgeOne"]);
  eq("T24.2 resortListCounts: a sign's own lodges; Not linked adds its free lodges; null and negatives read 0",
    [R.resortListCounts({ kind: 0, runs: 5, lodges: 3, freeLodges: 1 }), R.resortListCounts({ kind: 1, runs: 1, lodges: 0, freeLodges: 1 }),
      R.resortListCounts(null), R.resortListCounts({ kind: 0, runs: -1, lodges: -1, freeLodges: 4 })],
    [{ runs: 5, lodges: 3 }, { runs: 1, lodges: 1 }, { runs: 0, lodges: 0 }, { runs: 0, lodges: 0 }]);
  {
    // The name is a plain Button (FOCUS_DISABLED, drag guard) that opens AreaList, an overlay in the content (no Dropdown, no
    // Portal); picking = selectResort(entrySign(r)); note and caption (the list title) stay.
    const header = fs.readFileSync(path.join(srcDir, "hub", "HubHeader.tsx"), "utf8");
    const panel = fs.readFileSync(path.join(srcDir, "hub", "HubPanel.tsx"), "utf8");
    eq("T24.3 T114 selector source: no Dropdown, name Button with FOCUS_DISABLED + drag guard, AreaList in the panel body, selectResort(entrySign(r)), note, caption",
      [!/\bDropdown\b/.test(header.replace(/\/\*[\s\S]*?\*\//g, "")), /theme=\{\{ button: cx\(styles\.areaName/.test(header),
        /focusKey=\{FOCUS_DISABLED\}\s*\n\s*theme=\{\{ button: cx\(styles\.areaName/.test(header), header.includes("hubTriggers.selectResort(entrySign(r))"),
        /<AreaList \/>/.test(panel), header.includes("HUB_LOC.resortMenuNote"), header.includes("HUB_LOC.resortCaption")],
      [true, true, true, true, true, true, true]);
  }

  // T26 folded per-lift band rows, the name list, the Lifts tile (built / drawn), and the source facts of "never a scrollbar"
  // (folds closed by default and accordion, the pager always drawn).
  {
    const L = (code, name, sev = 2) => P(A, code, sev, { name });
    const rows = [L(7, "Lift 3"), L(4, ""), L(7, "Lift 4"), L(5, "Lift 9"), L(7, "Lift 5"), L(12, "", 1)];
    const folded = R.foldBandRows(rows);
    eq("T26.1 foldBandRows: three LiftUnfinished -> one row at the first one's rank, lone codes untouched",
      folded.map((b) => [b.row.code, b.group.length]), [[7, 3], [4, 1], [5, 1], [12, 1]]);
    eq("T26.2 foldBandRows: group names in C#'s order", folded[0].group.map((p) => p.name), ["Lift 3", "Lift 4", "Lift 5"]);
    eq("T26.3 foldBandRows: two no-power rows fold too; one row of a code never does",
      R.foldBandRows([L(5, "Lift 1"), L(5, "Lift 2"), L(6, "Lift 7")]).map((b) => [b.row.code, b.group.length]), [[5, 2], [6, 1]]);
    eq("T26.4 liftNameList", [R.liftNameList(["Lift 3", "Lift 4", "Lift 5"]), R.liftNameList(["Meadow Quad", "Lift 4"]),
      R.liftNameList(["Lift 3"]), R.liftNameList(["Lift 3", "Gondola 4"]), R.liftNameList(["", "Lift 2"]), R.liftNameList([])],
      ["Lift 3, 4, 5", "Meadow Quad, Lift 4", "Lift 3", "Lift 3, Gondola 4", "–, Lift 2", "–"]);
    eq("T26.5 bandSlots over folded rows: 8 raw rows (3 unfinished) -> 6 band rows, +3", R.bandSlots(R.foldBandRows(rows.concat([L(9, ""), L(15, "", 1)]))).more, 3);
    eq("T26.6 liftsTile: built / drawn; unknown -> null; drawn never below built",
      [R.liftsTile({ liftRows: 5 }, { liftsComplete: 1 }), R.liftsTile({ liftRows: 5 }, null), R.liftsTile({ liftRows: 5 }, { liftsComplete: -1 }),
        R.liftsTile({ liftRows: 1 }, { liftsComplete: 2 })],
      [{ built: 1, drawn: 5 }, null, null, { built: 2, drawn: 2 }]);
    const read = (n) => fs.readFileSync(path.join(srcDir, "hub", n), "utf8");
    const fit = read("fit.tsx"), state = read("hubState.ts"), strip = read("BuildStrip.tsx"), tiles = read("ViewTiles.tsx");
    eq("T26.7 never a scrollbar, source facts: folds closed by default + accordion, pager always drawn, folds in Attraction / Guests / Finances, no pager in Runs / Lifts (T131)",
      [/export const FOLD_DEFAULT: Readonly<Record<string, string>> = \{\};/.test(state), /open \? "" : id/.test(state),
        /<div className=\{styles\.pagerRow\}>/.test(fit),
        (read("OverviewView.tsx").match(/<Fold\b/g) || []).length, (read("GuestsView.tsx").match(/<Fold\b/g) || []).length,
        (read("MoneyView.tsx").match(/<Fold\b/g) || []).length, /<Pager fit=\{fit\} \/>/.test(read("RunsView.tsx")), /<Pager fit=\{fit\} \/>/.test(read("LiftsView.tsx"))],
      [true, true, true, 2, 4, 2, false, false]);
    // T27 the Lifts-tab toggle mirrors the tree tool: liftLine$ + liftLineToggle, ok panel only.
    const binds = fs.readFileSync(path.join(srcDir, "bindings.ts"), "utf8");
    eq("T27.1 lift generator: contract 12 or later (13 = T136), liftLine binding + liftLineToggle trigger, toggle on the Lifts tab only in the ok panel",
      [BIND.HUB_CONTRACT >= 12 ? 12 : BIND.HUB_CONTRACT, /liftLine: "liftLine"/.test(binds), /liftLineToggle: "liftLineToggle"/.test(binds), BIND.CORE_NAMES.indexOf("liftLine") < 0,
        strip.includes("{tab === \"lifts\" && info.liftTool && <LiftToolSet t={t} />}"), strip.includes("onSelect={() => hubTriggers.liftLineToggle()}"),
        /liftTool: true/.test(strip), !/liftTool: true/.test(strip.slice(strip.indexOf("MISMATCH_FOOTER"), strip.indexOf("MISMATCH_FOOTER") + 160))],
      [12, true, true, true, true, true, true, true]);
    eq("T26.8 Buildings tab: no lodge chips, the Lodges caption; Lifts tile = liftsTile",
      [/set="lodge"/.test(strip), strip.includes("HUB_LOC.lodgeGroup"), tiles.includes("liftsTile(row, l)")], [false, true, true]);
  }

  // T25 problem 26 (top-level schools on no sign) renders and flies to the school; the school's hints say "goes near the sign" and
  // its active line is the plain one (it is no upgrade); a launcher click with no mounted HubRoot reports and still sends toggleHub.
  eq("T25.1 code 26: variant schoolsNotLinked, click flies to the subject", [BIND.PROBLEM.SchoolsNotLinked, R.problemVariant(P(NUL, 26, 1, { count: 2 })), R.problemClick(P(NUL, 26, 1))],
    [26, "schoolsNotLinked", "sign"]);
  {
    const strip = fs.readFileSync(path.join(srcDir, "hub", "BuildStrip.tsx"), "utf8");
    const status = fs.readFileSync(path.join(srcDir, "hub", "StatusBlock.tsx"), "utf8");
    eq("T25.2 school hints: never the selected-sign hint; active line plain; row 26 drawn with its FMT text and tip",
      // The school's hint is its own BUILD_SHORT[School]; no selected-sign hint exists.
      [(strip.match(/case "school": return HUB_LOC\.buildShortSchool;/g) || []).length === 1, !strip.includes("buildHintLodgeSelected"),
        strip.includes('k === "lodge" || k === "school"'), status.includes("HUB_FMT.problemSchoolsNotLinked"), status.includes("HUB_LOC.problemTipSchoolsNotLinked")],
      [true, true, false, true, true]);
    const state = fs.readFileSync(path.join(srcDir, "hub", "hubState.ts"), "utf8");
    const root = fs.readFileSync(path.join(srcDir, "hub", "HubRoot.tsx"), "utf8");
    const toggleBody = (state.match(/export function toggleHub\(connected: boolean\): void \{([\s\S]*?)\n\}/) || [, ""])[1];
    eq("T25.3 M12: toggleHub reports when no HubRoot is mounted, then still sends; HubRoot sets and clears the flag",
      [toggleBody.indexOf("if (!rootMounted)") >= 0 && toggleBody.indexOf("reportUiError(\"launcher\"") > toggleBody.indexOf("if (!rootMounted)"),
        toggleBody.indexOf("if (connected) hubTriggers.toggleHub();") > toggleBody.indexOf("reportUiError("),
        root.includes("setRootMounted(true);") && root.includes("return () => setRootMounted(false);")], [true, true, true]);
    const hubCsT = fs.readFileSync(path.resolve(__dirname, "..", "..", "SkiResortMod", "Systems", "SkiResortHubUISystem.cs"), "utf8");
    eq("T25.4 C#: an upgrade-kind item launches like a station after TopLevelUpgradeLaunchAllowed (no sign-Upgrade path); one no-uiHello WARN per session",
      [hubCsT.includes("if (!TopLevelUpgradeLaunchAllowed(item, id, source, out link))"),
        !/LaunchUpgrade\(|m_ItemViaUpgrade/.test(hubCsT),
        hubCsT.includes("private static bool s_NoHelloWarned;")], [true, true, true]);
  }

  // T20 the History view. Source checks plus the pure helper's own oracles (tools/test-history-rules.js, run here so gate R18
  // covers them).
  const read = (rel) => fs.readFileSync(path.join(srcDir, rel), "utf8");
  const csDir = path.resolve(__dirname, "..", "..", "SkiResortMod");
  const stateSrc = read(path.join("hub", "hubState.ts"));
  const viewsLit = (stateSrc.match(/export const HUB_VIEWS: readonly HubView\[\] = \[([^\]]*)\]/) || [, ""])[1].split(",").map((s) => s.trim().replace(/"/g, "")).filter(Boolean);
  eq("T20.1 HUB_VIEWS: six views, history after money (Finances)", viewsLit, ["overview", "runs", "lifts", "guests", "money", "history"]);
  const locale = fs.readFileSync(path.join(csDir, "LocaleEN.cs"), "utf8");
  const csTuples = new Map();
  for (const m of locale.matchAll(/\{\s*"([^"]+)",\s*"((?:[^"\\]|\\.)*)"\s*\}/g)) csTuples.set(m[1], m[2]);
  const histKeys = [...tuples.keys()].filter((k) => /^SkiResort\.Hub\.(HISTORY|TILE\[History\]|TILE_TIP\[History\])/.test(k));
  eq("T20.2 every HISTORY key of hubText.ts is in LocaleEN.cs with the same text (50 keys)",
    [histKeys.length, histKeys.filter((k) => csTuples.get(k) !== tuples.get(k))], [50, []]);
  const viewSrc = read(path.join("hub", "HistoryView.tsx"));
  const H = HR;
  eq("T20.3 three range options (12 months, this season vs last, by season), each with a label and a tooltip in HistoryView",
    [H.HISTORY_RANGES, ["RANGE_LABEL", "RANGE_TIP"].map((m) => (viewSrc.match(new RegExp(`const ${m}[^=]*= \\{([^}]*)\\}`)) || [, ""])[1].split("\n").filter((l) => /:\s*HUB_LOC\./.test(l)).length),
      viewSrc.includes("options={HISTORY_RANGES}")], [["months12", "seasonVs", "bySeason"], [3, 3], true]);
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
  // T142 B7b (contract 15): the store has the depth too, so the Attraction strip's hover names a month's snow, always as the pair
  // (both labels, both fields, in one conditional block: neither number alone); still no snow line or series in a chart.
  const snowBlock = (viewSrc.match(/hovered\.snowDepthCm >= 0 \|\| hovered\.snowPct >= 0\) \? \[([\s\S]*?)\] : \[\]/) || [, ""])[1];
  eq("T20.7 History shows snow only as depth + coverage together in the strip hover (T18), never as a chart line",
    [["HUB_LOC.mountainSnowDepth", "HUB_LOC.mountainSnowCover", "hovered.snowDepthCm", "hovered.snowPct"].map((x) => snowBlock.includes(x)),
      (viewSrc.match(/snowPct|snowDepthCm/g) || []).length === (snowBlock.match(/snowPct|snowDepthCm/g) || []).length + 2],
    [[true, true, true, true], true]);
  eq("T20.8 the History tile shows a glyph, not a number from `history` (ViewTiles never reads history$)", /history\$/.test(read(path.join("hub", "ViewTiles.tsx"))), false);
  // T21 source checks for what the pure rules cannot see: two money series (income blue, costs red, the validated pair) and the
  // legend Income / Costs / Net; lines take linePoints, the chart hatches gaps and has a scale gutter with "0"; the status line
  // renders STATUS_SNOW with depth and coverage; the receipt's six operator cells, no "of 200"; no attraction control or strip in
  // Whole city; our own History glyph.
  const chartSrc = read(path.join("hub", "HistoryChart.tsx"));
  const statusSrc = read(path.join("hub", "StatusBlock.tsx"));
  const tilesSrc = read(path.join("hub", "ViewTiles.tsx"));
  eq("T21.1 B2: HISTORY_COLOR income #3987e5 / costs #e34948; one income and one cost series; legend Income, Costs, Net",
    [/income: "#3987e5"/.test(chartSrc), /costs: "#e34948"/.test(chartSrc), (viewSrc.match(/const incomeUp: ChartSeries\[\] = \[([^\]]*)\]/) || [, ""])[1].split("key:").length - 1,
      (viewSrc.match(/const costsDown: ChartSeries\[\] = \[([^\]]*)\]/) || [, ""])[1].split("key:").length - 1,
      ["HUB_LOC.historyIncome) }", "HUB_LOC.historyCosts) }", "HUB_LOC.historyNet), line: true }"].every((x) => viewSrc.includes(x))], [true, true, 1, 1, true]);
  eq("T21.2 B3: every line through linePoints; gap hatch; scale gutter with a \"0\" label",
    [(viewSrc.match(/points: linePoints\(/g) || []).length, chartSrc.includes("gapMarks(slots"), chartSrc.includes("hatchPath("), chartSrc.includes('node: "0"'), /value: \(i\) => b\(i\)\.(skiDays|net|attraction)/.test(viewSrc)],
    [5, true, true, true, false]);
  eq("T21.3 B5a: the status line renders STATUS_SNOW (or its inch form, T130) from snowDepthCm and snowPct (snow is two numbers, T18)",
    ["HUB_FMT.statusSnow", "HUB_FMT.statusSnowIn", "l.snowDepthCm", "l.snowPct", "useInches()"].map((x) => statusSrc.includes(x)), [true, true, true, true, true]);
  eq("T21.4 B6: operator cells + + + x x x on the receipt's term rows; no SCORE_OF key left",
    [(overview.match(/op="([^"]*)"/g) || []).map((m) => m.slice(4, -1)), tuples.has("SkiResort.Hub.SCORE_OF"), overview.includes("scoreTickPark")],
    [["+", "+", "+", "×", "×", "×"], false, true]);
  eq("T21.5 B7a: the Attraction checkbox and strip only in area scope",
    [/const attractionToggle = scope === "area" \? \(\s*<div className=\{styles\.histControls\}>\s*<Tip tip=\{t\(HUB_LOC\.historyTipAttraction\)\}/.test(viewSrc),viewSrc.includes('const attraction = attractionOn && scope === "area";')], [true, true]);
  const glyphSvg = fs.readFileSync(path.join(srcDir, "hub", "HistoryGlyph.svg"), "utf8");
  eq("T21.6 B8: the History tile uses our HistoryGlyph.svg (three bars + a baseline), not the game's CityStatistics.svg",
    [tilesSrc.includes('from "./HistoryGlyph.svg"'), tilesSrc.includes('"Media/Game/Icons/CityStatistics.svg"'), (glyphSvg.match(/<rect /g) || []).length], [true, false, 4]);
  // T22 the Finances column holds the Net's eight rows in order and they add up to netMonth (C#'s invariant: Σ max(0, income) −
  // Σ max(0, cost); -1 reads 0); an income row is hidden only while it is 0; the harness rows satisfy the invariant and equal
  // their area's month in progress in History.
  const N8 = { ticketsThisMonth: 1000, lodgeIncomeMonth: 200, lessonIncomeMonth: 50, passIncomeMonth: 3000, liftChargedMonth: 300, runCostMonth: 100,
    buildingsSoFar: 400, wagesSoFar: 600, busChargedMonth: 0 };
  eq("T22.1 netRows: the nine rows in the column's order, income +, costs -", [R.NET_ROW_IDS, R.netRows(lv(3, false, N8)).map((r) => r.sign)],
    [["tickets", "lodge", "lessons", "passes", "lifts", "runs", "bus", "buildings", "wages"], [1, 1, 1, 1, -1, -1, -1, -1, -1]]);
  eq("T22.1b the coach bill is in the Net; the Ski bus row shows with a depot or with money, never otherwise",
    [R.netRowsTotal(lv(3, false, Object.assign({}, N8, { busChargedMonth: 150 }))),
      R.netRowShown("bus", lv(3, false, { depots: 0, busChargedMonth: 0 }), { passShare: 0 }), R.netRowShown("bus", lv(3, false, { depots: 1, busChargedMonth: 0 }), { passShare: 0 }),
      R.netRowShown("bus", lv(3, false, { depots: 0, busChargedMonth: 9 }), { passShare: 0 })], [2700, false, true, true]);
  // The player text repeats the coach bill's numbers: SkiBusRules.cs is the truth.
  const busRulesCs = fs.readFileSync(path.join(csDir, "Systems", "SkiBusRules.cs"), "utf8");
  const billOf = (name) => { const m = new RegExp(name + " = (\\d+)f").exec(busRulesCs); return m ? Number(m[1]).toLocaleString("en-US") : "?"; };
  const billText = read(path.join("hub", "hubText.ts"));
  eq("T22.1c the depot tips say the coach bill's numbers (1,500 / 300)",
    [billOf("BillFixedPerMonth"), billOf("BillPerKmPerMonth"), /BUILD_TIP_DEPOT[^\n]*1,500 a month plus 300 per straight-line km/.test(billText), /BusBooked[^\n]*1,500 a month per depot plus 300 per straight-line km/.test(billText)],
    ["1,500", "300", true, true]);
  eq("T22.2 netRowsTotal: 4250 - 1400 = 2850; an unknown row (-1) reads 0; all unknown = 0",
    [R.netRowsTotal(lv(3, false, N8)), R.netRowsTotal(lv(3, false, Object.assign({}, N8, { lodgeIncomeMonth: -1, wagesSoFar: -1 }))),
      R.netRowsTotal(lv(3, false, Object.fromEntries(Object.keys(N8).map((k) => [k, -1]))))], [2850, 3250, 0]);
  const C0 = { passShare: 0 }, C50 = { passShare: 50 };
  eq("T22.3 netRowShown: lodge / lessons / passes hidden only while they hold no money; costs and tickets always",
    [R.netRowShown("lodge", lv(3, false, { lodgeSpendMonth: 0, lodgeIncomeMonth: 0 }), C0), R.netRowShown("lodge", lv(3, false, { lodgeSpendMonth: 0, lodgeIncomeMonth: 5 }), C0),
      R.netRowShown("lessons", lv(3, false, { lessonsThisMonth: 0, lessonsLastMonth: 0, lessonIncomeMonth: 0 }), C0),
      R.netRowShown("lessons", lv(3, false, { lessonsThisMonth: 0, lessonsLastMonth: 0, lessonIncomeMonth: 80 }), C0),
      R.netRowShown("passes", lv(3, false, { passesSoldSeason: 0, passHoldersThisMonth: 0, passHoldersLastMonth: 0, passIncomeMonth: 0 }), C0),
      R.netRowShown("passes", lv(3, false, { passesSoldSeason: 0, passHoldersThisMonth: 0, passHoldersLastMonth: 0, passIncomeMonth: 0 }), C50),
      R.netRowShown("passes", lv(3, false, { passesSoldSeason: 0, passHoldersThisMonth: 0, passHoldersLastMonth: 0, passIncomeMonth: 9 }), C0),
      R.netRowShown("tickets", lv(3), C0), R.netRowShown("wages", lv(3), C0)],
    [false, true, false, true, false, true, true, true, true]);
  const harnessWin = {};
  vm.runInNewContext(fs.readFileSync(path.resolve(__dirname, "..", "harness", "scenarios.js"), "utf8"), { window: harnessWin, Math, Object, JSON });
  const harnessRows = [];
  for (const [name, sc] of Object.entries(harnessWin.SkiScenarios)) {
    for (const l of sc.resortLive || []) {
      const h = (sc.history || []).find((x) => x.sign.index === l.sign.index && x.sign.version === l.sign.version);
      const last = h ? HR.lastKeyOf(h) : 0;
      harnessRows.push([name, l.sign.index, R.netRowsTotal(l) === l.netMonth, h ? HR.sumRange(h, "month", last, last, last).net === l.netMonth : true]);
    }
  }
  eq("T22.4 harness: every live row's nine rows add up to its netMonth, and its area's month in progress in History nets to the same",
    harnessRows.filter((r) => !r[2] || !r[3]), []);
  const moneySrc = read(path.join("hub", "MoneyView.tsx"));
  eq("T22.5 MoneyView draws the nine Net rows from their fields; the season pass total sits in the Also block",
    [["l.ticketsThisMonth", "l.lodgeIncomeMonth", "l.lessonIncomeMonth", "l.passIncomeMonth", "l.liftChargedMonth", "l.runCostMonth", "l.buildingsSoFar", "l.wagesSoFar", "l.busChargedMonth",
      "<AlsoBlock"].map((x) => moneySrc.includes(x)), /function AlsoBlock[\s\S]*<PassRows/.test(moneySrc), (moneySrc.match(/<PassRows /g) || []).length],
    [[true, true, true, true, true, true, true, true, true, true], true, 1]);
  // T29 a building tile shows what the building draws, from the prefab. BUILDING_DRAW must equal every building
  // prefab's ServiceConsumption (upkeep only where CityServiceBuilding bills it), its Workplace (or SkiCatalog's SignJobs for a roadless
  // row) and its BuildingTerraformOverride's own m_DontRaise + m_DontLower (LotHeightSystem.cs:253 keeps the slope only with both).
  const mirror = path.resolve(__dirname, "..", "..", "..", "gamedata-mirror", "ImportedData");
  const catalogCs = fs.readFileSync(path.join(csDir, "Systems", "SkiCatalog.cs"), "utf8");
  const blocksOf = (txt) => {
    const re = /"\$type": "\d+\|Game\.Prefabs\.(\w+), Game"/g; const at = []; let m;
    while ((m = re.exec(txt))) at.push({ i: m.index, name: m[1] });
    return at.map((b, k) => ({ name: b.name, text: txt.slice(b.i, k + 1 < at.length ? at[k + 1].i : txt.length) }));
  };
  const num = (t, fld) => { const m = new RegExp('"' + fld + '": (-?[0-9.]+)').exec(t); return m ? Number(m[1]) : 0; };
  const prefabDraw = (id, prefab) => {
    const txt = fs.readFileSync(path.join(mirror, prefab, prefab + ".Prefab"), "utf8");
    const bl = blocksOf(txt);
    const cons = bl.find((b) => b.name === "ServiceConsumption");
    const billed = bl.some((b) => b.name === "CityServiceBuilding");
    const work = bl.find((b) => b.name === "Workplace");
    const tf = bl.find((b) => b.name === "BuildingTerraformOverride");
    const last = (t, fld) => { const all = [...t.matchAll(new RegExp('"' + fld + '": (true|false)', "g"))]; return all.length ? all[all.length - 1][1] === "true" : false; };
    // The catalog row that names this build id (rows start at "new LodgeDef(" / "new PatrolDef(" / "new SchoolDef("); its signJobs, if any.
    const rows = catalogCs.split(/new (?:LodgeDef|PatrolDef|SchoolDef)\(/).slice(1).map((r) => r.slice(0, r.indexOf(")")));
    const own = rows.find((r) => r.includes('"' + id + '"'));
    const sj = own ? /signJobs: (\d+)/.exec(own) : null;
    const signJobs = sj ? Number(sj[1]) : 0;
    return { prefab, upkeep: cons && billed ? num(cons.text, "m_Upkeep") : 0, power: cons ? num(cons.text, "m_ElectricityConsumption") : 0,
      water: cons ? num(cons.text, "m_WaterConsumption") : 0, garbage: cons ? num(cons.text, "m_GarbageAccumulation") : 0,
      workers: (work ? num(work.text, "m_Workplaces") : 0) || signJobs,
      keepsSlope: !!tf && last(tf.text, "m_DontRaise") && last(tf.text, "m_DontLower") };
  };
  const drawIds = Object.keys(ITEMS.BUILDING_DRAW);
  const buildingIds = ITEMS.BUILD_ITEMS.filter((m) => m.kind !== "line" && m.kind !== "run" && m.kind !== "fence").map((m) => m.id);
  eq("T29.1 BUILDING_DRAW has one row per building tile (sign, lodges, school, patrol, stations), none for lines, runs or the fence", drawIds, buildingIds);
  eq("T29.2 every BUILDING_DRAW row equals its prefab (consumption, billed upkeep, workers / SignJobs, terrain flags)",
    drawIds.filter((id) => JSON.stringify(prefabDraw(id, ITEMS.BUILDING_DRAW[id].prefab)) !== JSON.stringify(ITEMS.BUILDING_DRAW[id]))
      .map((id) => id + " prefab " + JSON.stringify(prefabDraw(id, ITEMS.BUILDING_DRAW[id].prefab))), []);
  const linkText = [...tuples.entries()].filter(([, v]) => /join(s|ed)? (this ski area's |the )?Welcome Sign|(near|on) (this ski area's |the |a )?(selected )?Welcome Sign|needs staff/i.test(v)).map(([k]) => k);
  eq("T29.3 no hub text tells the player a building joins or goes near the Welcome Sign, or that it \"needs staff\" (T129)", linkText, []);
  // A depth is a number with cm or ft, or with in not followed by a word ("0 in Options", "0 in the net" are not depths). Checked
  // on the text WITHOUT its placeholders, so a {DEPTH} text cannot also bake a threshold ("3 ft reached", "opens at 3 ft (91 cm)"
  // slipped through the first version: the "ft + word" exception and the {DEPTH} exemption; DECISIONS 381).
  const bakedDepth = [...tuples.entries()].filter(([, v]) => /\d\s*(cm|ft)\b|\d\s*in\b(?!\s+[A-Za-z])/.test(v.replace(/\{[A-Z_]+\}/g, ""))).map(([k]) => k);
  eq("T29.4 no hub text bakes a snow depth (the open depth is C#'s and changes, T133; depth goes through {DEPTH})", bakedDepth, []);
  const stripSrc = read(path.join("hub", "BuildStrip.tsx"));
  eq("T29.5 the tile tooltip draws the vanilla rows from BUILDING_DRAW (Power / VolumePerMonth / WeightPerMonth / MoneyPerMonth) and the terrain line",
    ["...drawRows(e.id, t)", "Unit.Power", "Unit.VolumePerMonth", "Unit.WeightPerMonth", "Unit.MoneyPerMonth", "HUB_LOC.terrainSlope", "HUB_LOC.terrainLevel"].map((x) => stripSrc.includes(x)),
    [true, true, true, true, true, true, true]);
  // T30 the Runs and Lifts views list every row (no pager) and carry listAll, the class the layout test lets scroll.
  const listViews = ["RunsView.tsx", "LiftsView.tsx"].map((v) => read(path.join("hub", v)));
  eq("T30.1 Runs / Lifts: every row in one list (styles.listAll), no useFitPage, no <Pager>",
    listViews.map((v) => [v.includes("styles.listAll"), /useFitPage|<Pager/.test(v)]), [[true, false], [true, false]]);
  // T31 (DECISIONS 423b) the cat track delete: a trash only on a cat track row (other rows keep an empty slot), armed by the first click,
  // sent by the second; the trigger is the deleteCatTrack one (C# queues it and applies it in OnUpdate).
  const runList = read(path.join("hub", "RunList.tsx"));
  eq("T31.1 the trash is the cat track row's only (an empty penSpacer elsewhere)",
    runList.includes("row.kind === RUN_KIND.CatTrack ? <TrashButton") && runList.includes(": <div className={styles.penSpacer} />"), true);
  eq("T31.2 two clicks: the first arms (setArmed(true); return), the second sends hubTriggers.deleteCatTrack(row.entity)",
    [/if \(!armed\) \{\s*setArmed\(true\);\s*return;/.test(runList), runList.includes("hubTriggers.deleteCatTrack(row.entity)")], [true, true]);
  eq("T31.3 the arm times out (4 s) and leaving the row disarms",
    [runList.includes("setTimeout(() => setArmed(false), 4000)"), runList.includes("onMouseLeave={() => armed && setArmed(false)}")], [true, true]);
  // T32 (contract 19, field test 2026-10-26b) the Runs tab's Delete cat tracks chip: the catTrackDeleteMode value (off by default, C#
  // owns it and follows the tool) and the setCatTrackDeleteMode(bool) trigger; the chip flips the C# value and is drawn on the ok panel
  // only, beside the Quick runs chip, always (nothing moves under the mouse).
  eq("T32.1 binding, trigger and fallback (off)",
    [BIND.BINDING.catTrackDeleteMode, BIND.TRIGGER.setCatTrackDeleteMode, BIND.FALLBACK.catTrackDeleteMode, BIND.HUB_CONTRACT >= 19,
      BIND.CORE_NAMES.indexOf("catTrackDeleteMode") < 0],
    ["catTrackDeleteMode", "setCatTrackDeleteMode", false, true, true]);
  const ctChip = read(path.join("hub", "CatTrackControls.tsx"));
  eq("T32.2 the chip sends setCatTrackDeleteMode(!on) from the C# value and lights while on",
    [ctChip.includes("useValue(catTrackDeleteMode$)"), ctChip.includes("hubTriggers.setCatTrackDeleteMode(!on)"), ctChip.includes("selected={on}"),
      ctChip.includes("focusKey={FOCUS_DISABLED}")], [true, true, true, true]);
  eq("T32.3 drawn on the ok panel's Runs tab beside Quick runs, unconditionally there",
    [stripSrc.includes("{quick && <QuickRunsChip t={t} />}\n        {quick && <CatTrackDeleteChip t={t} />}"), /disabled=/.test(ctChip)], [true, false]);
  // T34 hub text states what a player cannot find by trying: no how-to for a road snap, a tool's stop or a plain click (PIPELINE).
  const obvious = /\bsnap\b|like a path|point at a (bar|tile)|click (here |again )?to (stop|close|turn|fly|open|show|list|pick)|^pick (the|a) /i;
  const hubTuples = read(path.join("hub", "hubText.ts")).split("\n")
    .map((l) => /\["(?:SkiResort|Assets)[^"]*", "([^"]*)"\]/.exec(l)).filter(Boolean).map((m) => m[1]);
  eq("T34 no over-instructive hub text", [hubTuples.length > 500, hubTuples.filter((s) => obvious.test(s))], [true, []]);
  // T35 (contract 28) the Runs view's prop rows: a step under the minimum reads off, a step up from off lands on the minimum at the least, the
  // maximum holds. Range from RunPropPlacerRules (tools/check-run-props.js compares the constants); the rows act on the focused run.
  const step = R.runPropSpacingStep;
  eq("T35.1 runPropSpacingStep", [step(0, 10), step(0, 50), step(20, 10), step(20, -10), step(20, -50), step(60, -50), step(70, -50), step(100, 50), step(190, 50), step(200, 10), step(200, -10)],
    [20, 50, 30, 0, 0, 0, 20, 150, 200, 200, 190]);
  const propRows = read(path.join("hub", "RunPropControls.tsx"));
  const runsView = read(path.join("hub", "RunsView.tsx"));
  eq("T35.2 the rows send the two triggers, read C#'s value and are drawn under the tree rows while Quick runs is closed, for the focused row",
    [propRows.includes("hubTriggers.setRunGunSpacing(row.entity, m)"), propRows.includes("hubTriggers.setRunLightSpacing(row.entity, m)"), propRows.includes("row.gunSpacingM"), propRows.includes("row.lightSpacingM"),
      /!quick\?\.active && <RunPropRows row=\{rows\.find\(\(r\) => r\.entity && entityId\(r\.entity\) === focusedId\)\} \/>/.test(runsView)],
    [true, true, true, true, true]);
  eq("T35.3 a cat track row shows its running cost (billed by plan area while its guns can run)",
    [read(path.join("hub", "RunList.tsx")).includes("row.kind !== RUN_KIND.Natural && row.bound && {\n          label: t(HUB_LOC.runCost)")], [true]);

  // T36 (owner verdict 2026-11-02: warn, never refuse) code 30 LiftBend: a dim band row naming the lift, the bend (detail = tenths) and the
  // tower (count); its click flies to the bent tower (focusLift with the tower node as subject and no top, SkiResortHubUISystem).
  eq("T36.1 code 30: variant liftBend, click lift, info glyph", [BIND.PROBLEM.LiftBend, R.problemVariant(P(A, 30, 1, { count: 7, detail: 124 })),
    R.problemClick(P(A, 30, 1)), R.problemIcon(P(A, 30, 1)).tone], [30, "liftBend", "lift", "info"]);
  {
    const status = read(path.join("hub", "StatusBlock.tsx"));
    eq("T36.2 row 30 drawn with its FMT text (DEG from detail / 10, N from count) and its tip",
      [status.includes("HUB_FMT.problemLiftBend} args={{ NAME, DEG: numArg(p.detail / 10, Unit.FloatSingleFraction), N }}"),
        status.includes("HUB_LOC.problemTipLiftBend")], [true, true]);
  }

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
