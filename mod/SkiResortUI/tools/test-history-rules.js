/*
 * tools/test-history-rules.js - literal oracles for src/hub/historyRules.ts (T78 build 2, contract 10): seasons, range windows,
 * month / season bucketing (flows summed, point values averaged, gaps and absent months empty), the whole-city sum, year on year,
 * the axis helpers. Compiles bindings.ts + historyRules.ts with the installed typescript (transpileModule, CommonJS) and runs them
 * in a vm context with cs2/api stubbed, like tools/test-hub-rules.js, which calls run() as part of gate R18.
 * Standalone: `node tools/test-history-rules.js [srcDir]` -> `test-history-rules: N check(s), failures=0`.
 * Oracles are literals computed by hand from the fixtures below (never recomputed with the code under test).
 * Written for this repo (MIT). Created 2026-10-18.
 */
"use strict";
const fs = require("fs");
const path = require("path");
const vm = require("vm");

function load(srcDir, ts) {
  const cache = new Map();
  const stubs = { "cs2/api": { bindValue: (_g, _n, fb) => ({ value: fb }), trigger() {}, bindLocalValue: (v) => ({ value: v, update() {} }) }, "cs2/utils": {} };
  const rulesFile = path.resolve(path.join(srcDir, "hub", "historyRules.ts"));
  const compile = (file) => {
    const abs = path.resolve(file);
    if (cache.has(abs)) return cache.get(abs).exports;
    const out = ts.transpileModule(fs.readFileSync(abs, "utf8"), { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;
    const mod = { exports: {} };
    cache.set(abs, mod);
    const req = (spec) => {
      if (abs === rulesFile && spec !== "../bindings") throw new Error(`test-history-rules: historyRules.ts imports "${spec}" - it must stay pure (values only from ../bindings)`);
      if (stubs[spec]) return stubs[spec];
      if (spec.startsWith(".")) {
        const base = path.resolve(path.dirname(abs), spec);
        for (const ext of [".ts", ".tsx"]) if (fs.existsSync(base + ext)) return compile(base + ext);
      }
      throw new Error(`test-history-rules: unexpected import "${spec}" from ${file}`);
    };
    vm.runInNewContext(out, { require: req, module: mod, exports: mod.exports, __BUILD_STAMP__: "test", console, Math, Number, Array, Infinity }, { filename: abs });
    return mod.exports;
  };
  return compile(rulesFile);
}

/** An area of n months from `first`; fill(i) returns overrides of the month's defaults. */
function area(first, n, fill) {
  const a = { sign: { index: 1, version: 1 }, firstMonthKey: first, flags: [], tourists: [], residents: [], skiDays: [], ticketsPaid: [], ticketIncome: [],
    lodgeIncome: [], passIncome: [], lessonIncome: [], liftCost: [], runCost: [], buildingsCost: [], wagesCost: [], attraction: [], snowPct: [] };
  for (let i = 0; i < n; i++) {
    const m = Object.assign({ flags: 0, tourists: 0, residents: 0, skiDays: -1, ticketsPaid: 0, ticketIncome: 0, lodgeIncome: 0, passIncome: 0,
      lessonIncome: 0, liftCost: 0, runCost: 0, buildingsCost: -1, wagesCost: -1, attraction: -1, snowPct: -1 }, fill(i));
    for (const k of Object.keys(m)) a[k].push(m[k]);
  }
  return a;
}

const GAP = { flags: 16, tourists: 0, residents: 0, skiDays: -1, buildingsCost: -1, wagesCost: -1, attraction: -1, snowPct: -1 };

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

  // Fixture A: Apr 2025 (key 24303) .. Jun 2026 (24317, the month in progress), 15 months; i0 First, i2 GAP, i7 Partial,
  // i5 buildings unknown, i14 Current.
  const A = area(24303, 15, (i) => i === 2 ? GAP : {
    flags: i === 0 ? 64 : i === 7 ? 1 : i === 14 ? 128 : 0,
    tourists: 100 + 10 * i, residents: 50, skiDays: i % 2 === 0 ? 20 : -1, ticketIncome: 1000, lodgeIncome: 200, passIncome: i === 3 ? 3000 : 0,
    lessonIncome: 50, liftCost: 300, runCost: 100, buildingsCost: i === 5 ? -1 : 400, wagesCost: 600, attraction: 40 + i, snowPct: i < 5 ? 80 : -1,
  });
  // Fixture B: Feb 2026 (24313) .. Jun 2026, 5 months; snapshot and ski days unknown.
  const B = area(24313, 5, (i) => ({ flags: i === 0 ? 64 : i === 4 ? 128 : 0, tourists: 10, ticketIncome: 100, attraction: 20 }));
  // Fixture C: Jan 2000 (24000) .. Oct 2005 (24069), 70 whole months.
  const C = area(24000, 70, () => ({ tourists: 1, buildingsCost: 0, wagesCost: 0 }));

  eq("TH1.1 seasonOf: June 2026 -> 2025, July 2026 -> 2026, January 2000 -> 1999", [R.seasonOf(24317), R.seasonOf(24318), R.seasonOf(24000)], [2025, 2026, 1999]);
  eq("TH1.2 seasonFirstKey(2026) = July 2026; monthOfKey / yearOfKey", [R.seasonFirstKey(2026), R.monthOfKey(24317), R.yearOfKey(24317), R.monthOfKey(24000)], [24318, 5, 2026, 0]);
  eq("TH1.3 lengthOf / lastKeyOf", [R.lengthOf(A), R.lastKeyOf(A), R.lengthOf(null)], [15, 24317, 0]);

  const w12 = R.windowOf(A, "months12");
  eq("TH2.1 12 months: 12 buckets, Jul 2025 .. Jun 2026", [w12.length, w12[0].key, w12[11].key, w12[0].kind], [12, 24306, 24317, "month"]);
  eq("TH2.2 12 months: partial = buildings unknown (i5), Partial (i7), the month in progress; current = the last",
    [w12.map((b, j) => (b.partial ? j : -1)).filter((j) => j >= 0), w12.map((b, j) => (b.current ? j : -1)).filter((j) => j >= 0)], [[2, 4, 11], [11]]);
  const b3 = w12[0];
  eq("TH2.3 a month bucket's numbers (i3)", [b3.tourists, b3.residents, b3.guests, b3.skiDays, b3.income, b3.staffCost, b3.costs, b3.net, b3.attraction, b3.snowPct, b3.months, b3.none],
    [130, 50, 180, -1, 4250, 1000, 1400, 2850, 43, 80, 1, false]);
  eq("TH2.4 buildings unknown: staff = wages only, the bucket partial (i5)", [w12[2].staffCost, w12[2].costs, w12[2].partial], [600, 1000, true]);
  const s2 = R.windowOf(A, "seasons2");
  eq("TH2.5 2 seasons: July 2024 .. June 2026 = 24 months; 9 before the area + the GAP month are empty",
    [s2.length, s2[0].key, s2.filter((b) => b.none).length, s2[11].none, s2[11].key, s2[9].none], [24, 24294, 10, true, 24305, false]);
  eq("TH2.6 a GAP or absent month draws nothing (not a zero)", [s2[0].none, s2[0].guests, s2[0].partial], [true, 0, false]);
  eq("TH2.7 5 years on a 15-month area: 60 buckets, 46 empty (45 before it + the GAP)", [R.windowOf(A, "years5").length, R.windowOf(A, "years5").filter((b) => b.none).length], [60, 46]);
  eq("TH2.8 All with <= 60 months: every month, monthly", [R.windowOf(A, "all").length, R.windowOf(A, "all")[0].key, R.windowOf(A, "all")[0].kind], [15, 24303, "month"]);
  const cAll = R.windowOf(C, "all");
  eq("TH2.9 All beyond 60 months: July-June seasons 1999/00 .. 2005/06", [cAll.length, cAll[0].kind, cAll[0].key, cAll[6].key, cAll[0].months, cAll[6].months, cAll[1].months],
    [7, "season", 1999, 2005, 6, 4, 12]);
  eq("TH2.10 seasons: first and last partial (fewer months than a season), the middle whole", [cAll[0].partial, cAll[1].partial, cAll[6].partial, cAll[1].tourists], [true, false, true, 12]);
  eq("TH2.11 5 years on 70 months: the last 60", [R.windowOf(C, "years5").length, R.windowOf(C, "years5")[0].key, R.windowOf(C, "years5").filter((b) => b.none).length], [60, 24010, 0]);

  const [sA, sB] = R.seasonBuckets(A, 2024, 2025);
  eq("TH3.1 season 2024/25 of A: two months with data (Apr, May; Jun GAP) of 3 in range -> partial; flows summed, points averaged",
    [sA.months, sA.partial, sA.tourists, sA.skiDays, sA.attraction, sA.snowPct, sA.firstKey, sA.lastKey], [2, true, 210, 20, 40.5, 80, 24294, 24305]);
  eq("TH3.2 season 2025/26 of A: 12 months, holds the month in progress", [sB.months, sB.current, sB.partial, sB.tourists, sB.skiDays, sB.income, sB.staffCost, sB.costs, sB.net, sB.attraction, sB.snowPct],
    [12, true, true, 2220, 120, 18000, 11600, 16400, 1600, 48.5, 80]);
  eq("TH3.3 a season of B: snapshot unknown -> staff -1, attraction averaged", [R.seasonBuckets(B, 2025, 2025)[0].staffCost, R.seasonBuckets(B, 2025, 2025)[0].attraction, R.seasonBuckets(B, 2025, 2025)[0].income], [-1, 20, 500]);

  eq("TH4.1 rangeEnabled: A all four; B only 12 months", [R.HISTORY_RANGES.map((r) => R.rangeEnabled(A, r)), R.HISTORY_RANGES.map((r) => R.rangeEnabled(B, r))],
    [[true, true, true, true], [true, false, false, false]]);
  eq("TH4.2 effectiveRange falls back to 12 months; null area", [R.effectiveRange(B, "years5"), R.effectiveRange(A, "seasons2"), R.effectiveRange(null, "all"), R.windowOf(null, "all").length], ["months12", "seasons2", "months12", 0]);
  eq("TH4.3 four ranges, three chart choices", [R.HISTORY_RANGES.length, R.HISTORY_CHARTS.length, R.ALL_MONTHS_MAX], [4, 3, 60]);

  const city = R.sumAreas([A, B]);
  eq("TH5.1 whole city: the union of the ranges", [city.firstMonthKey, R.lengthOf(city)], [24303, 15]);
  eq("TH5.2 a month no area has data for is GAP", [city.flags[2]], [16]);
  eq("TH5.3 Feb 2026: A + B summed; B's First dropped (A started earlier); B's unknowns flag SnapshotMissing + SkiDaysPartial",
    [city.tourists[10], city.flags[10], city.skiDays[10], city.buildingsCost[10], city.wagesCost[10], city.attraction[10], city.ticketIncome[10]], [210, 40, 20, 400, 600, 35, 1100]);
  eq("TH5.4 June 2026: the month in progress stays Current", [city.flags[14], city.tourists[14]], [168, 250]);
  eq("TH5.5 sumAreas of nothing", [R.sumAreas([]), R.sumAreas([area(24000, 0, () => ({}))])], [null, null]);

  const y = R.yearOnYear(A);
  eq("TH6.1 year on year: the last whole month (May 2026) against May 2025", [y.now.key, y.before.key, y.now.guests, y.before.guests, R.pctChange(y.now.guests, y.before.guests)],
    [24316, 24304, 280, 160, 75]);
  eq("TH6.2 year on year without a year of data", [R.yearOnYear(B).now.key, R.yearOnYear(B).before, R.yearOnYear(null).now], [24316, null, null]);
  eq("TH6.3 pctChange", [R.pctChange(90, 100), R.pctChange(5, 0), R.pctChange(5, -10), R.pctChange(null, 4), R.pctChange(150, 100)], [-10, null, null, null, 50]);

  eq("TH7.1 niceMax", [0, 7, 120, 230, 251, 1000, -5].map((v) => R.niceMax(v)), [1, 10, 200, 250, 500, 1000, 1]);
  eq("TH7.2 bucketAt", [R.bucketAt(0, 12), R.bucketAt(0.999, 12), R.bucketAt(1, 12), R.bucketAt(-0.1, 12), R.bucketAt(0.5, 0), R.bucketAt(0.5, 24)], [0, 11, 11, -1, -1, 12]);
  eq("TH7.3 labelStep", [R.labelStep(12, "month"), R.labelStep(24, "month"), R.labelStep(60, "month"), R.labelStep(8, "season"), R.labelStep(30, "season")], [1, 3, 6, 1, 3]);
  return { checks, failures };
}

module.exports = { run };
if (require.main === module) {
  const r = run(process.argv[2] || path.join(__dirname, "..", "src"));
  for (const f of r.failures) console.error(`test-history-rules: FAIL ${f}`);
  console.log(`test-history-rules: ${r.checks} check(s), failures=${r.failures.length}`);
  process.exit(r.failures.length ? 1 : 0);
}
