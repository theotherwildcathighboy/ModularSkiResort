/*
 * tools/test-hub-rules.js - gate rule R18: literal oracles for src/hub/hubRules.ts (pure display rules).
 * Compiles src/bindings.ts and src/hub/hubRules.ts with the installed typescript (transpileModule,
 * CommonJS) and runs them in a vm context: cs2/api is stubbed (bindValue returns { value: fallback },
 * trigger is a no-op), __BUILD_STAMP__ = "test". No new npm package. Exports run(srcDir) -> { checks, failures: string[] }.
 *
 * Written for this repo (MIT). Created 2026-10-04 (Round E1, stream S6; docs/research/econ-round/spec/
 * ch6-hub-ui.md section 5.3, the checked reference spec/ch6-hub-ui-ref/tools/test-hub-rules.js).
 * tools/check-bundle.js runs it as rule R18 (full gate and --contract-only, against --src= when given);
 * standalone: `node tools/test-hub-rules.js [srcDir]` -> `test-hub-rules: 98 check(s), failures=0` (76 in Round E1;
 * the E1 fixer added T8.7-T8.10 and changed T8.6's no-live oracle 3 -> 0, e1-spec 8.9 F9 / F11; Round F7 D5 added
 * T2.13-T2.14 and T3.14-T3.16 for the two road rows, docs/research/field-test-7-fixes.md; Round S1 added
 * T2.15-T2.16, T3.17-T3.19, T4.14, T6.9, T7.7-T7.8 and section T9, docs/research/ski-days/spec/ch4-hub.md G.2).
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
  return compile(path.join(srcDir, "hub", "hubRules.ts"));
}

function run(srcDir) {
  const ts = require("typescript");
  const R = load(srcDir, ts);
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
  eq("T5.5 line1 run", R.line1Variant("runNatural", item({ price: 500, pricePerHa: 250 })), "run");
  eq("T5.6 line1 run no per-ha (contract-2 DLL)", R.line1Variant("runGroomed", item({ price: 2000 })), "name");
  eq("T5.7 line1 lift", R.line1Variant("lift", item({ costPerKm: 7500 })), "lift");
  eq("T5.8 line1 lift unknown", R.line1Variant("lift", item({ costPerKm: -1 })), "name");
  eq("T5.9 line1 cost", R.line1Variant("lodge", item({ cost: 40000 })), "cost");
  eq("T5.10 line1 cost zero", R.line1Variant("sign", item({ cost: 0 })), "cost");
  eq("T5.11 line1 name", R.line1Variant("baseStation", item({})), "name");
  eq("T5.12 footer priority", [
    R.footerState(false, true, true, true, true), R.footerState(true, true, true, true, true),
    R.footerState(true, false, true, true, true), R.footerState(true, false, false, true, true),
    R.footerState(true, false, false, false, true), R.footerState(true, false, false, false, false)],
    ["empty", "hover", "follow", "active", "locked", "idle"]);
  eq("T5.13 run kinds", ["runNatural", "runGroomed", "runSnowmaking", "runNight", "lift"].map(R.runKindOfItem), [3, 0, 1, 2, undefined]);

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

  // T9 ski days (Round S1 D27, ch4 B.2 / B.6): the state line, its tooltip and the walk count.
  const sd = (state, limit = 0, extra = {}) => Object.assign({ skiDaysState: state, skiDaysLimit: limit, skiDaysCap: 154 }, extra);
  eq("T9.1 skiDayLine states", [0, 1, 2, 3, 4, 5, 7, 99].map((s) => R.skiDayLine(sd(s))), ["unknown", "off", "signOff", "noRun", "noLodge", "night", "full", "unknown"]);
  eq("T9.2 skiDayLine room by limit", [R.skiDayLine(sd(6, 1)), R.skiDayLine(sd(6, 2)), R.skiDayLine(sd(6, 0)), R.skiDayLine(null)], ["roomRuns", "roomLodges", "room", "unknown"]);
  eq("T9.3 skiDayWalk", [R.skiDayWalk({ skiDaysSent: 52, skiDaysSentCar: 41, skiDaysSentNoCar: 3 }), R.skiDayWalk({ skiDaysSent: 5, skiDaysSentCar: 4, skiDaysSentNoCar: 3 }),
    R.skiDayWalk({ skiDaysSent: -1, skiDaysSentCar: -1, skiDaysSentNoCar: -1 }), R.skiDayWalk(null)], [8, 0, -1, -1]);
  eq("T9.4 skiDayTip", [R.skiDayTip(sd(1)), R.skiDayTip(sd(4)), R.skiDayTip(sd(6, 1)), R.skiDayTip(sd(7, 2)), R.skiDayTip(sd(5)), R.skiDayTip(sd(6, 0))],
    ["off", "noLodge", "runs", "lodges", null, null]);
  return { checks, failures };
}

module.exports = { run };
if (require.main === module) {
  const r = run(process.argv[2] || path.join(__dirname, "..", "src"));
  for (const f of r.failures) console.error(`test-hub-rules: FAIL ${f}`);
  console.log(`test-hub-rules: ${r.checks} check(s), failures=${r.failures.length}`);
  process.exit(r.failures.length ? 1 : 0);
}
