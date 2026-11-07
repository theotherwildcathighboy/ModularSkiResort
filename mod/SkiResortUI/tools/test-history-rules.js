/*
 * tools/test-history-rules.js - literal oracles for src/hub/historyRules.ts: seasons, range windows (12 months / this season
 * vs last / by season), month / season bucketing (flows summed, point values averaged, gaps and absent months empty), the
 * whole-city sum, year on year, line ends and gap bands, netChange, the axis helpers. Compiles bindings.ts + historyRules.ts
 * with the installed typescript (transpileModule, CommonJS) and runs them in a vm context with cs2/api stubbed, like
 * tools/test-hub-rules.js, which calls run() as part of the gate.
 * Standalone: `node tools/test-history-rules.js [srcDir]` -> `test-history-rules: N check(s), failures=0`.
 * Oracles are literals computed by hand from the fixtures below (never recomputed with the code under test).
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

/* An area of n months from `first`; fill(i) returns overrides of the month's defaults. */
function area(first, n, fill) {
  const a = { sign: { index: 1, version: 1 }, firstMonthKey: first, seasonFirstMonth0: 6, flags: [], tourists: [], residents: [], skiDays: [], ticketsPaid: [], ticketIncome: [],
    lodgeIncome: [], passIncome: [], lessonIncome: [], liftCost: [], runCost: [], buildingsCost: [], wagesCost: [], attraction: [], snowPct: [], snowDepthCm: [] };
  for (let i = 0; i < n; i++) {
    const m = Object.assign({ flags: 0, tourists: 0, residents: 0, skiDays: -1, ticketsPaid: 0, ticketIncome: 0, lodgeIncome: 0, passIncome: 0,
      lessonIncome: 0, liftCost: 0, runCost: 0, buildingsCost: -1, wagesCost: -1, attraction: -1, snowPct: -1, snowDepthCm: -1 }, fill(i));
    for (const k of Object.keys(m)) a[k].push(m[k]);
  }
  return a;
}

const GAP = { flags: 16, tourists: 0, residents: 0, skiDays: -1, buildingsCost: -1, wagesCost: -1, attraction: -1, snowPct: -1, snowDepthCm: -1 };

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
  // Fixture D: Sep 2024 (24296) .. Feb 2026 (24313, the month in progress), 18 months. Fixture E: July 2026 only, in progress.
  const D = area(24296, 18, (i) => ({ flags: i === 17 ? 128 : 0, tourists: 10 }));
  const E = area(24318, 1, () => ({ flags: 128, tourists: 5 }));

  eq("TH1.1 seasonOf: June 2026 -> 2025, July 2026 -> 2026, January 2000 -> 1999", [R.seasonOf(24317), R.seasonOf(24318), R.seasonOf(24000)], [2025, 2026, 1999]);
  R.setSeasonFirstMonth0(0);
  eq("TH1.1b a June winter's season starts in January: June and July 2026 are one season, its first key January", [R.seasonOf(24317), R.seasonOf(24318), R.seasonFirstKey(2026)], [2026, 2026, 24312]);
  eq("TH1.1d a season from January is one calendar year", R.seasonIsOneYear(), true);
  R.setSeasonFirstMonth0(99);
  eq("TH1.1c a bad month reads July", [R.seasonOf(24317), R.seasonOf(24318)], [2025, 2026]);
  R.setSeasonFirstMonth0(R.SEASON_FIRST_MONTH0);
  eq("TH1.1e a season from July spans two years", R.seasonIsOneYear(), false);
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
  const vs = R.windowOf(A, "seasonVs");
  eq("TH2.5 This season vs last: the 12 months of season 2025/26 (Jul 2025 .. Jun 2026), the month in progress last",
    [vs.length, vs[0].key, vs[11].key, vs.map((b, j) => (b.current ? j : -1)).filter((j) => j >= 0), vs.filter((b) => b.none).length], [12, 24306, 24317, [11], 0]);
  const prevA = R.compareOf(A, "seasonVs");
  eq("TH2.6 compareOf: last season Jul 2024 .. Jun 2025; 9 months before the area empty without a hatch, the GAP month (Jun 2025) hatched",
    [prevA.length, prevA[0].key, prevA.filter((b) => b.none).length, prevA.map((b, j) => (b.gap ? j : -1)).filter((j) => j >= 0), prevA[0].gap, prevA[0].guests, prevA[9].guests],
    [12, 24294, 10, [11], false, 0, 150]);
  eq("TH2.7 compareOf is null for the other ranges and without data", [R.compareOf(A, "months12"), R.compareOf(A, "bySeason"), R.compareOf(null, "seasonVs"), R.compareOf(B, "seasonVs")], [null, null, null, null]);
  const vsD = R.windowOf(D, "seasonVs");
  eq("TH2.8 the rest of this season (Mar .. Jun 2026 after the month in progress Feb) is future: empty, no hatch",
    [vsD.map((b, j) => (b.future ? j : -1)).filter((j) => j >= 0), vsD.filter((b) => b.gap).length, vsD.map((b, j) => (b.current ? j : -1)).filter((j) => j >= 0)], [[8, 9, 10, 11], 0, [7]]);
  const cS = R.windowOf(C, "bySeason");
  eq("TH2.9 By season: one bucket per July-June season 1999/00 .. 2005/06, months averaged 6 / 12 / 4",
    [cS.length, cS[0].kind, cS[0].key, cS[6].key, cS.map((b) => b.perMonth)], [7, "season", 1999, 2005, [6, 12, 12, 12, 12, 12, 4]]);
  eq("TH2.10 By season draws the average month: partial seasons are NOT lower (1 guest a month everywhere); totals keep the sums",
    [cS.map((b) => b.guests), cS.map((b) => b.totalGuests), [cS[0].partial, cS[1].partial, cS[6].partial]], [[1, 1, 1, 1, 1, 1, 1], [6, 12, 12, 12, 12, 12, 4], [true, false, true]]);
  const [aS, bS] = R.windowOf(A, "bySeason");
  eq("TH2.11 By season of A: 2024/25 = Apr + May (Jun GAP left out), averages of 2 months",
    [aS.perMonth, aS.tourists, aS.guests, aS.totalGuests, aS.skiDays, aS.income, aS.costs, aS.net, aS.totalNet, aS.attraction, aS.partial, aS.current],
    [2, 105, 155, 310, 20, 1250, 1400, -150, -300, 40.5, true, false]);
  eq("TH2.12 By season of A: 2025/26 averages the 11 whole months (the month in progress left out), total = average x 11",
    [bS.perMonth, bS.guests, bS.totalGuests, bS.skiDays, Math.round(bS.income), bS.totalNet, Math.round(bS.net * 11), bS.attraction, bS.snowPct, bS.current, bS.partial],
    [11, 230, 2530, 20, 1523, 1750, 1750, 48, 80, true, true]);
  eq("TH2.13 a season holding only the month in progress has no whole month: empty, current", [R.seasonAverage(E, 2026).none, R.seasonAverage(E, 2026).current, R.seasonAverage(E, 2026).perMonth], [true, true, 0]);

  const [sA, sB] = R.seasonBuckets(A, 2024, 2025);
  eq("TH3.1 season 2024/25 of A: two months with data (Apr, May; Jun GAP) of 3 in range -> partial; flows summed, points averaged",
    [sA.months, sA.partial, sA.tourists, sA.skiDays, sA.attraction, sA.snowPct, sA.firstKey, sA.lastKey], [2, true, 210, 20, 40.5, 80, 24294, 24305]);
  eq("TH3.2 season 2025/26 of A: 12 months, holds the month in progress", [sB.months, sB.current, sB.partial, sB.tourists, sB.skiDays, sB.income, sB.staffCost, sB.costs, sB.net, sB.attraction, sB.snowPct],
    [12, true, true, 2220, 120, 18000, 11600, 16400, 1600, 48.5, 80]);
  eq("TH3.3 a season of B: snapshot unknown -> staff -1, attraction averaged", [R.seasonBuckets(B, 2025, 2025)[0].staffCost, R.seasonBuckets(B, 2025, 2025)[0].attraction, R.seasonBuckets(B, 2025, 2025)[0].income], [-1, 20, 500]);

  eq("TH4.1 rangeEnabled: A all three; B (one season of data) only 12 months", [R.HISTORY_RANGES.map((r) => R.rangeEnabled(A, r)), R.HISTORY_RANGES.map((r) => R.rangeEnabled(B, r))],
    [[true, true, true], [true, false, false]]);
  eq("TH4.2 effectiveRange falls back to 12 months; null area", [R.effectiveRange(B, "bySeason"), R.effectiveRange(A, "seasonVs"), R.effectiveRange(null, "bySeason"), R.windowOf(null, "bySeason").length, R.effectiveRange(E, "seasonVs")], ["months12", "seasonVs", "months12", 0, "months12"]);
  eq("TH4.3 three ranges", R.HISTORY_RANGES, ["months12", "seasonVs", "bySeason"]);

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

  eq("TH9.1 linePoints: the line ends at the last whole month (the month in progress has no point)", R.linePoints(w12, (b) => b.tourists),
    [130, 140, 150, 160, 170, 180, 190, 200, 210, 220, 230, null]);
  eq("TH9.2 linePoints: an unknown value breaks the line", R.linePoints(w12, (b) => (b.skiDays >= 0 ? b.skiDays : NaN)),
    [null, 20, null, 20, null, 20, null, 20, null, 20, null, null]);
  eq("TH9.3 linePoints: empty buckets (before the area, a GAP) have no point", R.linePoints(prevA, (b) => b.guests),
    [null, null, null, null, null, null, null, null, null, 150, 160, null]);
  eq("TH9.4 linePoints on seasons: the running season keeps its point (its average of whole months)", R.linePoints(R.windowOf(A, "bySeason"), (b) => b.guests), [155, 230]);
  eq("TH9.5 gapSlots: only GAP months inside the area's range are hatched", [R.gapSlots(prevA), R.gapSlots(w12), R.gapSlots(R.windowOf(R.sumAreas([A, B]), "seasonVs")), R.gapSlots(vsD)], [[11], [], [], []]);
  eq("TH9.6 a whole-city month no area recorded is a hatched gap", [R.gapSlots(R.compareOf(R.sumAreas([A, B]), "seasonVs"))], [[11]]);
  const yN = { now: { net: 500 }, before: { net: 5000 } };
  eq("TH9.7 netChange: the year-on-year change in money (not a %), null without both months", [R.netChange(yN), R.netChange(R.yearOnYear(A)), R.netChange(R.yearOnYear(B)), R.netChange(R.yearOnYear(null))], [-4500, 0, null, null]);
  // TH10 (T142 B7b): snow depth per month, contract 15. Point value like the coverage: averaged over the months that know it, the
  // areas averaged in Whole city; a missing or short snowDepthCm array reads unknown and never shortens the other series.
  const F = area(24303, 3, (i) => ({ tourists: 1, snowPct: 80, snowDepthCm: [100, -1, 40][i] }));
  const F2 = area(24303, 3, (i) => ({ tourists: 2, snowPct: 60, snowDepthCm: [200, 50, -1][i] }));
  const fS = R.seasonBuckets(F, 2024, 2024)[0];
  const fM = R.windowOf(F, "months12").filter((b) => !b.none).map((b) => b.snowDepthCm);
  eq("TH10.1 depth: a month bucket keeps its value (-1 unknown), a season averages the months that know it", [fM, fS.snowDepthCm, fS.snowPct], [[100, -1, 40], 70, 80]);
  const G = Object.assign({}, F); delete G.snowDepthCm;
  const H = Object.assign({}, F, { snowDepthCm: [100] });
  eq("TH10.2 no / short depth array: lengthOf unchanged, depth unknown where absent, flows intact",
    [R.lengthOf(G), R.seasonBuckets(G, 2024, 2024)[0].snowDepthCm, R.seasonBuckets(G, 2024, 2024)[0].tourists, R.lengthOf(H), R.seasonBuckets(H, 2024, 2024)[0].snowDepthCm, R.depthAt(H, 2)],
    [3, -1, 3, 3, 100, -1]);
  const cityF = R.sumAreas([F, F2]);
  eq("TH10.3 Whole city: depth is the mean of the areas that know it, like the coverage", [cityF.snowDepthCm, cityF.snowPct], [[150, 50, 40], [70, 70, 70]]);
  return { checks, failures };
}

module.exports = { run };
if (require.main === module) {
  const r = run(process.argv[2] || path.join(__dirname, "..", "src"));
  for (const f of r.failures) console.error(`test-history-rules: FAIL ${f}`);
  console.log(`test-history-rules: ${r.checks} check(s), failures=${r.failures.length}`);
  process.exit(r.failures.length ? 1 : 0);
}
