/*
 * harness/scenarios.js - sample binding values for the browser harness (MIT, 2026-10-18).
 * Shapes follow src/bindings.ts (BuildItem, GladeState, ResortRow, ResortLive, RunRow, RunLive,
 * LiftRow, ProblemRow, CityStats; HistoryArea since contract 10). Numbers are plausible, not measured: this is for wording and
 * layout, not for sim checks. Enum values: see the *_KIND / BAND / LIFT_STATE / PROBLEM tables in
 * bindings.ts (RunKind Groomed 0 Snowmaking 1 Night 2 Natural 3 CatTrack 4; BAND Green 1 Blue 2
 * Black 3 DoubleBlack 4; LIFT_STATE Running 0 ... StationsOnly 4; RESORT_STATUS Open 3).
 */
(function () {
  "use strict";
  const E = (i) => ({ index: i, version: 1 });
  const NULL = { index: 0, version: 0 };

  const IDS = ["sign", "lodge", "lodgeHut", "lodgeMid", "lodgeSummit", "lodgeGrand", "school", "patrolHut", "patrolHQ",
    "baseStation", "topStation", "lift", "baseStationDetachable", "topStationDetachable", "liftDetachable",
    "baseStationGondola", "topStationGondola", "liftGondola", "baseStationTram", "topStationTram", "liftTram",
    "runNatural", "runGroomed", "runSnowmaking", "runNight", "runCatTrack"];
  function buildItems(opts) {
    const o = opts || {};
    return IDS.map((id) => {
      const it = { id, locked: !!(o.locked && o.locked.includes(id)), active: o.active === id, reason: "" };
      if (it.locked) it.reason = o.lockReason || "Unlock the Ski Resort milestone first.";
      if (id.startsWith("run")) {
        it.price = { runNatural: 0, runGroomed: 120, runSnowmaking: 180, runNight: 160, runCatTrack: 60 }[id];
        it.pricePerHa = { runNatural: 0, runGroomed: 450, runSnowmaking: 900, runNight: 700, runCatTrack: 200 }[id];
        it.monthlyPerHa = { runNatural: 0, runGroomed: 55, runSnowmaking: 140, runNight: 95, runCatTrack: 20 }[id];
      } else if (id.startsWith("lift")) {
        it.costPerKm = { lift: 180000, liftDetachable: 420000, liftGondola: 650000, liftTram: 900000 }[id];
        it.monthly = { lift: 1200, liftDetachable: 2600, liftGondola: 3800, liftTram: 5200 }[id];
        it.monthlyPerKm = { lift: 400, liftDetachable: 800, liftGondola: 1100, liftTram: 1500 }[id];
      } else {
        it.cost = { sign: 25000, lodge: 180000, lodgeHut: 60000, lodgeMid: 320000, lodgeSummit: 540000, lodgeGrand: 1400000,
          school: 260000, patrolHut: 45000, patrolHQ: 210000, baseStation: 150000, topStation: 120000,
          baseStationDetachable: 380000, topStationDetachable: 300000, baseStationGondola: 520000, topStationGondola: 440000,
          baseStationTram: 900000, topStationTram: 760000 }[id];
        it.monthly = Math.round(it.cost / 80);
      }
      return it;
    });
  }

  const city = { known: true, daysPerYear: 365, scaleK: 4, backcountryPct: 40, ticketPrice: 65, snowGating: true,
    snowmakingMarginC: 2, snowVisualsOff: false, tourismOverhaul: false, realisticWorkplaces: false,
    tripPlanner: "", tripPlannerBlocksSkiDays: false, bookTicketsAsFees: true, passShare: 50, month: 1 };

  const SIGN = E(101), SIGN2 = E(102);
  const runs = [
    { entity: E(201), name: "Ash Flat Meadow", kind: 0, bound: true, lengthM: 1420, dropM: 210, areaM2: 84000, sign: SIGN, ordinal: 1, customName: true, band: 1, gradePct: 15, steepestPct: 22, approx: false, member: true, served: true, servedBy: E(301), valueFull: 1.0, bandValue: 1.0, sizeFactor: 1.1, typeFactor: 1.0, servedFactor: 1.0 },
    { entity: E(202), name: "Run 2", kind: 1, bound: true, lengthM: 2180, dropM: 480, areaM2: 126000, sign: SIGN, ordinal: 2, customName: false, band: 2, gradePct: 24, steepestPct: 38, approx: false, member: true, served: true, servedBy: E(301), valueFull: 1.6, bandValue: 1.4, sizeFactor: 1.3, typeFactor: 1.2, servedFactor: 1.0 },
    { entity: E(203), name: "North Face", kind: 3, bound: true, lengthM: 1760, dropM: 610, areaM2: 61000, sign: SIGN, ordinal: 3, customName: true, band: 3, gradePct: 38, steepestPct: 55, approx: true, member: true, served: false, servedBy: NULL, valueFull: 1.9, bandValue: 1.8, sizeFactor: 1.0, typeFactor: 0.9, servedFactor: 0.5 },
    { entity: E(204), name: "Run 4", kind: 2, bound: true, lengthM: 960, dropM: 140, areaM2: 38000, sign: SIGN, ordinal: 4, customName: false, band: 1, gradePct: 15, steepestPct: 19, approx: false, member: true, served: true, servedBy: E(302), valueFull: 0.8, bandValue: 1.0, sizeFactor: 0.8, typeFactor: 1.1, servedFactor: 1.0 },
    { entity: E(205), name: "Cat Track", kind: 4, bound: true, lengthM: 2400, dropM: 90, areaM2: 24000, sign: SIGN, ordinal: 5, customName: false, band: 0, gradePct: 4, steepestPct: 8, approx: false, member: true, served: true, servedBy: E(302), valueFull: 0.2, bandValue: 0.2, sizeFactor: 1.0, typeFactor: 0.5, servedFactor: 1.0 },
    { entity: E(206), name: "Run 6", kind: 0, bound: false, lengthM: 640, dropM: 95, areaM2: 19000, sign: NULL, ordinal: 6, customName: false, band: -1, gradePct: 0, steepestPct: 0, approx: true, member: false, served: false, servedBy: NULL, valueFull: 0, bandValue: 0, sizeFactor: 0, typeFactor: 0, servedFactor: 0 },
  ];
  const runLive = [
    { entity: E(201), openState: 2, openFactor: 1.0, snowPct: 92, snowEstimated: false, valueNow: 1.0, costMonth: 4600 },
    { entity: E(202), openState: 2, openFactor: 1.0, snowPct: 100, snowEstimated: false, valueNow: 1.6, costMonth: 17600 },
    { entity: E(203), openState: 1, openFactor: 0.5, snowPct: 61, snowEstimated: true, valueNow: 0.95, costMonth: 0 },
    { entity: E(204), openState: 2, openFactor: 1.0, snowPct: 88, snowEstimated: false, valueNow: 0.8, costMonth: 3600 },
    { entity: E(205), openState: 2, openFactor: 1.0, snowPct: 90, snowEstimated: false, valueNow: 0.2, costMonth: 480 },
    { entity: E(206), openState: -1, openFactor: 0, snowPct: 0, snowEstimated: true, valueNow: 0, costMonth: 0 },
  ];
  const lifts = [
    { entity: E(301), top: E(311), sign: SIGN, name: "Meadow Quad", customName: true, state: 0, sections: 3, towers: 14, chairs: 62, chairsRiding: 62, gaps: 0, towersRemoved: 0, lengthM: 1680, riseM: 470, horizontalM: 1612, alignKnown: true, alignOffsetM: 0.4, alignYawDeg: 1.2, capacityPerHour: 2400, hasBase: true, hasTop: true, powered: true, counted: true, status: 0, rideTimeS: 420, runsServed: 2, costMonth: 3900, billedReduced: false, type: "detachable", seats: 4, speedMps: 5.0 },
    { entity: E(302), top: E(312), sign: SIGN, name: "Lift 2", customName: false, state: 2, sections: 2, towers: 7, chairs: 28, chairsRiding: 0, gaps: 0, towersRemoved: 0, lengthM: 940, riseM: 160, horizontalM: 926, alignKnown: true, alignOffsetM: 0.1, alignYawDeg: 0.3, capacityPerHour: 1200, hasBase: true, hasTop: true, powered: false, counted: true, status: 1, rideTimeS: 400, runsServed: 2, costMonth: 1600, billedReduced: true, type: "fixedGrip", seats: 2, speedMps: 2.3 },
    { entity: E(303), top: E(313), sign: SIGN, name: "Lift 3", customName: false, state: 3, sections: 1, towers: 3, chairs: 0, chairsRiding: 0, gaps: 1, towersRemoved: 0, lengthM: 520, riseM: 300, horizontalM: 424, alignKnown: false, alignOffsetM: 0, alignYawDeg: 0, capacityPerHour: 0, hasBase: true, hasTop: false, powered: false, counted: false, status: 3, rideTimeS: 0, runsServed: 0, costMonth: 0, billedReduced: false, type: "gondola", seats: 8, speedMps: 6.0 },
  ];
  const resorts = [
    { sign: SIGN, kind: 0, name: "Ash Flat", customName: true, runs: 5, rated: 4, flat: 1, green: 2, blue: 1, black: 1, doubleBlack: 0, areaM2: 333000, verticalM: 610, verticalApprox: false, lodges: 3, freeLodges: 1, liftRows: 3, liftLengthM: 3140, capacityPerHour: 3600 },
    { sign: SIGN2, kind: 0, name: "Resort 2", customName: false, runs: 0, rated: 0, flat: 0, green: 0, blue: 0, black: 0, doubleBlack: 0, areaM2: 0, verticalM: 0, verticalApprox: true, lodges: 0, freeLodges: 0, liftRows: 0, liftLengthM: 0, capacityPerHour: 0 },
  ];
  // T77: the receipt numbers are self-consistent (SkiResortScore.cs:468, 495): S = (4.55 + 21 + 12) × 1.15 × 1.2 = 51.82,
  // terrain = 4 × sqrt(S) = 28.8, full = 18 + 28.8 = 47, attraction = 46.8 × 0.94 = 44. city.month 1 = February.
  const live = {
    sign: SIGN, status: 3, runsOpen: 4, runsPartly: 1, snowEstimated: false, snowPct: 88, snowDepthCm: 64, seasonHint: 1, topTempKnown: true, topTempC: -6,
    attraction: 44, attractionFull: 47, attractionHeld: false, lodgesActive: 2, summer: false, lodgePart: 18, terrain: 28.8, runsValue: 4.55, runsValueFull: 5.5, openSharePct: 83,
    liftsValue: 21, liftsComplete: 2, liftsCounted: 2, varietyMultiplier: 1.15, verticalMultiplier: 1.2, scoreVerticalM: 610, efficiencyPct: 94, sharePct: 61,
    estKnown: true, estTourists: 1240, nextStep: 0, thisMonthSinceLoad: true, lastMonthFull: true,
    guestsThisMonth: 3860, touristsThisMonth: 2410, touristGroupsThisMonth: 640, residentsThisMonth: 1450, paidThisMonth: 3300, unpaidThisMonth: 360, freeThisMonth: 200, ticketsThisMonth: 3300,
    guestsLastMonth: 5120, touristsLastMonth: 3300, touristGroupsLastMonth: 880, residentsLastMonth: 1820, paidLastMonth: 4500, ticketsLastMonth: 4500,
    ticketGate: 0, ticketIncomeMonth: 214500, ticketPaidMonth: 3300, ticketIncomeEstimated: false,
    costGroomedMonth: 8200, costSnowmakingMonth: 17600, costNightMonth: 3600, haNatural: 6.1, haGroomed: 10.3, haSnowmaking: 12.6, haNight: 3.8,
    liftCostMonth: 5500, liftsBilled: 2, liftsBilledReduced: 1, buildingsMonth: 9800, wagesMonth: 31200, jobs: 64, jobsFilled: 58,
    netMonth: 138600, netEstimated: false,
    skiDaysState: 6, skiDaysThisMonth: 2410, skiDaysLastMonth: 3300, skiDaysSent: 2600, skiDaysSentCar: 1900, skiDaysSentNoCar: 700, skiDaysNow: 310, skiDaysCap: 480, skiDaysLimit: 2, skiDaysPowderPct: 35,
    lodgeBest: "lodgeMid", lodgeExtra: 1, luxuryIgnored: 0, schoolValue: 12, patrolledRuns: 3, priceTolerance: 78, lodgeSpendMonth: 46000,
    passesSoldSeason: 420, passIncomeSeason: 126000, passHoldersThisMonth: 390, passHoldersLastMonth: 410, lessonsThisMonth: 120, lessonsLastMonth: 160, lessonIncomeMonth: 9600,
    passesPreSeason: 85, passIncomePreSeason: 25500, cityIncomeEst: 390000,
  };
  const live2 = Object.assign({}, live, { sign: SIGN2, status: 0, runsOpen: 0, runsPartly: 0, snowPct: 0, attraction: 0, attractionFull: 0, lodgesActive: 0, estKnown: false, estTourists: 0, nextStep: 1, netMonth: 0, skiDaysState: 3, guestsThisMonth: 0, ticketIncomeMonth: 0 });
  const problems = [
    { sign: SIGN, code: 5, severity: 2, subject: E(302), subjectTop: E(312), name: "Lift 2", count: 1, pct: 0, factor: "", more: 0, detail: 0, ifFixed: 6, buildItem: "" },
    { sign: SIGN, code: 6, severity: 2, subject: E(303), subjectTop: E(313), name: "Lift 3", count: 1, pct: 0, factor: "", more: 0, detail: 1, ifFixed: 4, buildItem: "liftGondola" },
    { sign: SIGN, code: 12, severity: 1, subject: E(203), subjectTop: NULL, name: "North Face", count: 1, pct: 50, factor: "served", more: 0, detail: 0, ifFixed: 3, buildItem: "lift" },
    { sign: SIGN, code: 15, severity: 1, subject: NULL, subjectTop: NULL, name: "", count: 1, pct: 0, factor: "", more: 0, detail: 0, ifFixed: 2, buildItem: "" },
    { sign: SIGN, code: 21, severity: 1, subject: E(401), subjectTop: NULL, name: "Day Lodge", count: 1, pct: 0, factor: "", more: 0, detail: 300, ifFixed: 0, buildItem: "" },
    { sign: SIGN2, code: 9, severity: 2, subject: NULL, subjectTop: NULL, name: "", count: 0, pct: 0, factor: "", more: 0, detail: 0, ifFixed: 0, buildItem: "runGroomed" },
  ];
  // T78 build 2 (contract 10): `history` - 30 months for Ash Flat (Sep 2024 .. Feb 2027, the month in progress last: city.month 1 =
  // February) with a winter peak, +15 % a year, a two-month GAP (Mar-Apr 2026: the mod was off), one partial month (Oct 2025: a load
  // that was not seeded), the July season boundaries; and 10 months for "Resort 2", which started in May 2026 (First). Flags:
  // HISTORY_FLAG in bindings.ts (Partial 1, Gap 16, First 64, Current 128). Plausible, not measured.
  const WINTER = [1.0, 1.0, 0.8, 0.45, 0.15, 0.1, 0.12, 0.12, 0.15, 0.25, 0.55, 0.9];   // Jan .. Dec
  function historyOf(sign, firstKey, n, scale, opts) {
    const o = opts || {};
    const h = { sign, firstMonthKey: firstKey, flags: [], tourists: [], residents: [], skiDays: [], ticketsPaid: [], ticketIncome: [], lodgeIncome: [],
      passIncome: [], lessonIncome: [], liftCost: [], runCost: [], buildingsCost: [], wagesCost: [], attraction: [], snowPct: [] };
    const push = (m) => { for (const k of Object.keys(m)) h[k].push(m[k]); };
    for (let i = 0; i < n; i++) {
      const key = firstKey + i, month = key % 12;
      if (o.gap && o.gap.includes(key)) {
        push({ flags: 16, tourists: 0, residents: 0, skiDays: -1, ticketsPaid: 0, ticketIncome: 0, lodgeIncome: 0, passIncome: 0, lessonIncome: 0,
          liftCost: 0, runCost: 0, buildingsCost: -1, wagesCost: -1, attraction: -1, snowPct: -1 });
        continue;
      }
      const current = i === n - 1, share = current ? 0.45 : 1;
      const f = WINTER[month], g = 1 + 0.15 * (i / 12);
      const t = Math.round(3300 * f * g * scale * share), r = Math.round(1800 * (0.3 + 0.7 * f) * g * scale * share);
      const paid = Math.round(0.85 * (t + r));
      push({
        flags: (i === 0 ? 64 : 0) | (current ? 129 : 0) | (o.partial && o.partial.includes(key) ? 1 : 0),
        tourists: t, residents: r, skiDays: f > 0.3 ? Math.round(t * 0.9) : Math.round(t * 0.5), ticketsPaid: paid, ticketIncome: paid * 65,
        lodgeIncome: Math.round((t + r) * 9), passIncome: Math.round(({ 8: 40000, 9: 60000, 10: 30000, 11: 10000 }[month] || 0) * g * scale * share),
        lessonIncome: Math.round(t * 0.04) * 80, liftCost: Math.round((i < 6 ? 3900 : 5500) * share), runCost: Math.round((f > 0.3 ? 29400 : 4400) * scale * share),
        buildingsCost: Math.round(9800 * share), wagesCost: Math.round(31200 * share), attraction: Math.round((30 + 14 * f + i * 0.3) * (scale < 1 ? 0.4 : 1)),
        snowPct: Math.round(f > 0.3 ? 70 + 25 * f : 5 + 40 * f),
      });
    }
    return h;
  }
  const NOW_KEY = 2027 * 12 + 1;   // February 2027 = city.month 1
  const history = [
    historyOf(SIGN, NOW_KEY - 29, 30, 1, { gap: [2026 * 12 + 2, 2026 * 12 + 3], partial: [2025 * 12 + 9] }),
    historyOf(SIGN2, 2026 * 12 + 4, 10, 0.2),
  ];

  const glade = { available: true, active: false, level: 4, treeLine: false, reason: "" };

  const base = { hubOpen: false, contract: 10, buildStamp: "", showHubButton: true, build: buildItems(), glade,
    resorts: [], resortLive: [], selectedResort: NULL, runs: [], runLive: [], lifts: [], problems: [], city, pisteMap: true, follow: 0, history: [] };

  window.SkiScenarios = {
    "not connected (no C#)": Object.assign({}, base, { contract: 0, city: Object.assign({}, city, { known: false }) }),
    "contract mismatch (DLL 9, UI 10)": Object.assign({}, base, { contract: 9, hubOpen: true }),
    "connected, empty city": Object.assign({}, base, { hubOpen: true, build: buildItems({ locked: ["lodgeGrand", "liftTram", "baseStationTram", "topStationTram"] }) }),
    "Ash Flat (runs, lifts, problems)": Object.assign({}, base, { hubOpen: true, resorts, resortLive: [live, live2], selectedResort: SIGN, runs, runLive, lifts, problems, history,
      glade: Object.assign({}, glade, { active: true }), build: buildItems({ active: "runGroomed" }) }),
    // T78: 8 years of history, so "All" draws July-June seasons (over 60 months).
    "Ash Flat, 8 years of history": Object.assign({}, base, { hubOpen: true, resorts, resortLive: [live, live2], selectedResort: SIGN, runs, runLive, lifts,
      problems, history: [historyOf(SIGN, NOW_KEY - 95, 96, 1, { gap: [2026 * 12 + 2, 2026 * 12 + 3], partial: [2025 * 12 + 9] }), history[1]] }),
  };
})();
