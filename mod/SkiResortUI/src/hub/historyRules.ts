/*
 * src/hub/historyRules.ts - pure rules of the History view (T78 build 2, contract 10): the whole-city sum, the range windows, the
 * month and July-June season buckets, the year-on-year headline and the axis scale. No React, no binding, no text: values only
 * from ../bindings (HISTORY_FLAG). tools/test-history-rules.js holds its literal oracles; tools/test-hub-rules.js runs it.
 *
 * Written for this repo (MIT). Created 2026-10-18 (T78 build 2; docs/research/ui/hub-history-build-2-2026-10-18.md section 4).
 * Rules (the plan, docs/research/ui/hub-history-chart-plan-2026-10-18.md section 4.2, with Richard's answers):
 *   - Month keys are C# SkiMoneyRules.MonthKey: year x 12 + calendar month 0..11. A ski season is July-June, keyed by the year of its
 *     July (C# SkiMoneyRules.SeasonKeyOf, SeasonFirstMonth 7).
 *   - Ranges: see the T82 UI round below (the build-2 set "2 seasons" / "5 years" / "All" is gone). Months before the area existed
 *     or GAP months are empty buckets (`none`), never zeros.
 *   - A season bucket SUMS the flows (guests, ski days, income, costs) of its months with data and AVERAGES the point values
 *     (attraction, snow coverage) over the months that know them. Unknown parts (-1) are left out of a sum; a bucket with none known
 *     is -1.
 *   - partial = the bucket holds the month in progress or a month flagged Partial, Short, SnapshotMissing, SkiDaysPartial or First,
 *     or (a season) fewer months with data than the season has (the area started or the data ends partway). The chart draws it hollow.
 *   - Whole city = the areas sent (the live ones), month by month; attraction and snow are the mean over the areas that know them.
 *   - Year on year: the last whole month (the one before the month in progress) against the same month a year earlier.
 * T82 UI round (2026-10-19; docs/research/ui/hub-design-review-2026-10-18.md B3 + B4, approved by Richard "all seven, go"):
 *   - Ranges (B4, supersedes "2 seasons" / "5 years" / "All"): "months12" = the last 12 months (as before); "seasonVs" = the 12
 *     months July-June of THIS season (months not reached yet are `future`, empty) with last season's same months beside them
 *     (compareOf); "bySeason" = one bucket per July-June season drawn as the MONTHLY AVERAGE (seasonAverage) over its whole months
 *     with data: the month in progress and GAP months are left out of both the average and the season total, so a season with
 *     missing months or one still running is not drawn as a slump. totalGuests / totalNet = the sums over the same months (average
 *     x perMonth). Both season ranges need data in an earlier season (rangeEnabled).
 *   - B3: `gap` = an empty bucket with a GAP-flagged month inside the area's range (recorded nothing: the chart hatches it); months
 *     before the area existed or not reached yet are empty without a hatch. linePoints ends every line at the last whole month
 *     (the month in progress and empty buckets have no point); gapSlots lists the hatched buckets. netChange = the headline's
 *     year-on-year change in money (a percentage of a net is unstable, F9).
 */
import { HISTORY_FLAG } from "../bindings";
import type { HistoryArea } from "../bindings";

export type HistoryRange = "months12" | "seasonVs" | "bySeason";
export const HISTORY_RANGES: readonly HistoryRange[] = ["months12", "seasonVs", "bySeason"];

/** Which charts the view draws. */
export type HistoryCharts = "guests" | "money" | "both";
export const HISTORY_CHARTS: readonly HistoryCharts[] = ["guests", "money", "both"];

/** July as a calendar month 0..11 (C# SkiMoneyRules.SeasonFirstMonth 7, 1-based). */
export const SEASON_FIRST_MONTH0 = 6;

const PARTIAL_BITS = HISTORY_FLAG.Partial | HISTORY_FLAG.Short | HISTORY_FLAG.SnapshotMissing | HISTORY_FLAG.SkiDaysPartial |
  HISTORY_FLAG.First | HISTORY_FLAG.Current;

export function yearOfKey(key: number): number {
  return Math.floor(key / 12);
}

/** Calendar month 0..11 of a key (Common.MONTH:<n> / Common.MONTH_SHORT:<n> index). */
export function monthOfKey(key: number): number {
  return key - yearOfKey(key) * 12;
}

/** The ski season (July-June) of a month key: the year of its July. */
export function seasonOf(key: number): number {
  return monthOfKey(key) >= SEASON_FIRST_MONTH0 ? yearOfKey(key) : yearOfKey(key) - 1;
}

/** The month key of a season's July. */
export function seasonFirstKey(season: number): number {
  return season * 12 + SEASON_FIRST_MONTH0;
}

/** The key of the area's last entry (normally the month in progress). */
export function lastKeyOf(area: HistoryArea): number {
  return area.firstMonthKey + lengthOf(area) - 1;
}

/** Entries the area carries (the shortest array, so a malformed row never reads past an end). */
export function lengthOf(area: HistoryArea | null | undefined): number {
  if (!area) return 0;
  const a = area;
  return Math.min(a.flags.length, a.tourists.length, a.residents.length, a.skiDays.length, a.ticketIncome.length, a.lodgeIncome.length,
    a.passIncome.length, a.lessonIncome.length, a.liftCost.length, a.runCost.length, a.buildingsCost.length, a.wagesCost.length,
    a.attraction.length, a.snowPct.length, a.ticketsPaid.length);
}

/** One drawn bucket: a month or a season. Money in credits, costs positive; -1 = unknown where noted. */
export interface HistoryBucket {
  kind: "month" | "season";
  /** A month key, or a season key (the year of its July). */
  key: number;
  firstKey: number;
  lastKey: number;
  /** Months with data in it. */
  months: number;
  /** No data at all (a gap, or before the area existed): drawn empty. */
  none: boolean;
  /** Empty because a month inside the area's range recorded nothing (GAP): drawn as a hatched band (B3). */
  gap: boolean;
  /** Empty because it lies after the area's last month (the rest of this season, "seasonVs"). */
  future: boolean;
  /** 0 = the flows are sums; N > 0 = a season drawn as the monthly average over N whole months (B4 "bySeason"). */
  perMonth: number;
  /** Guests and net over the bucket's counted months (= guests / net for a sum; average x perMonth for a season average). */
  totalGuests: number;
  totalNet: number;
  /** Holds the month in progress. */
  current: boolean;
  /** Not a whole, measured month / season (header). */
  partial: boolean;
  tourists: number;
  residents: number;
  guests: number;
  /** -1 unknown. */
  skiDays: number;
  ticketsPaid: number;
  ticketIncome: number;
  lodgeIncome: number;
  passIncome: number;
  lessonIncome: number;
  income: number;
  liftCost: number;
  runCost: number;
  /** Buildings + wages (the game's bill); -1 when neither is known. */
  staffCost: number;
  costs: number;
  net: number;
  /** Mean; -1 unknown. */
  attraction: number;
  /** Mean run snow coverage 0-100; -1 unknown. */
  snowPct: number;
}

interface Acc {
  months: number; current: boolean; partial: boolean;
  tourists: number; residents: number; skiDays: number; skiKnown: number; ticketsPaid: number;
  ticketIncome: number; lodgeIncome: number; passIncome: number; lessonIncome: number; liftCost: number; runCost: number;
  staff: number; staffKnown: number; attr: number; attrKnown: number; snow: number; snowKnown: number;
}

function acc(): Acc {
  return { months: 0, current: false, partial: false, tourists: 0, residents: 0, skiDays: 0, skiKnown: 0, ticketsPaid: 0, ticketIncome: 0,
    lodgeIncome: 0, passIncome: 0, lessonIncome: 0, liftCost: 0, runCost: 0, staff: 0, staffKnown: 0, attr: 0, attrKnown: 0, snow: 0, snowKnown: 0 };
}

const num = (v: number | undefined): number => (typeof v === "number" && Number.isFinite(v) ? v : 0);

/** Adds entry i of the area (not GAP) to the accumulator. */
function addMonth(a: Acc, area: HistoryArea, i: number): void {
  const f = area.flags[i] | 0;
  a.months++;
  if (f & HISTORY_FLAG.Current) a.current = true;
  if (f & PARTIAL_BITS) a.partial = true;
  a.tourists += Math.max(0, num(area.tourists[i]));
  a.residents += Math.max(0, num(area.residents[i]));
  a.ticketsPaid += Math.max(0, num(area.ticketsPaid[i]));
  if (area.skiDays[i] >= 0) { a.skiDays += area.skiDays[i]; a.skiKnown++; }
  // B1 (contract 11, hub-one-net-b1 section 1): a term below 0 is unknown and reads 0, as in C# HubRules.NetMonth (Σ max(0, term)).
  a.ticketIncome += Math.max(0, num(area.ticketIncome[i]));
  a.lodgeIncome += Math.max(0, num(area.lodgeIncome[i]));
  a.passIncome += Math.max(0, num(area.passIncome[i]));
  a.lessonIncome += Math.max(0, num(area.lessonIncome[i]));
  a.liftCost += Math.max(0, num(area.liftCost[i]));
  a.runCost += Math.max(0, num(area.runCost[i]));
  const b = area.buildingsCost[i], w = area.wagesCost[i];
  if (b >= 0 || w >= 0) { a.staff += Math.max(0, b) + Math.max(0, w); a.staffKnown++; }
  if (b < 0 || w < 0) a.partial = true;   // the game's bill is (partly) unknown: the costs are incomplete
  if (area.attraction[i] >= 0) { a.attr += area.attraction[i]; a.attrKnown++; }
  if (area.snowPct[i] >= 0) { a.snow += area.snowPct[i]; a.snowKnown++; }
}

function finish(kind: "month" | "season", key: number, firstKey: number, lastKey: number, a: Acc, expected: number): HistoryBucket {
  const income = a.ticketIncome + a.lodgeIncome + a.passIncome + a.lessonIncome;
  const staff = a.staffKnown > 0 ? a.staff : -1;
  const costs = a.liftCost + a.runCost + Math.max(0, staff);
  const guests = a.tourists + a.residents;
  return {
    kind, key, firstKey, lastKey, months: a.months, none: a.months === 0, gap: false, future: false, perMonth: 0, current: a.current,
    partial: a.months > 0 && (a.partial || a.months < expected),
    tourists: a.tourists, residents: a.residents, guests, skiDays: a.skiKnown > 0 ? a.skiDays : -1,
    ticketsPaid: a.ticketsPaid, ticketIncome: a.ticketIncome, lodgeIncome: a.lodgeIncome, passIncome: a.passIncome, lessonIncome: a.lessonIncome,
    income, liftCost: a.liftCost, runCost: a.runCost, staffCost: staff, costs, net: income - costs,
    attraction: a.attrKnown > 0 ? a.attr / a.attrKnown : -1, snowPct: a.snowKnown > 0 ? a.snow / a.snowKnown : -1,
    totalGuests: guests, totalNet: income - costs,
  };
}

/** Sums the area's months [firstKey, lastKey] into one bucket (GAP and absent months count nothing). `expected` = the months a
 *  whole bucket has (fewer with data = partial: a season the area entered or left partway is not whole). `skipCurrent` leaves the
 *  month in progress out (a season average counts whole months only). */
export function sumRange(area: HistoryArea, kind: "month" | "season", key: number, firstKey: number, lastKey: number, skipCurrent = false): HistoryBucket {
  const a = acc();
  const n = lengthOf(area);
  const lo = Math.max(firstKey, area.firstMonthKey), hi = Math.min(lastKey, area.firstMonthKey + n - 1);
  let gapSeen = false, currentSeen = false;
  for (let k = lo; k <= hi; k++) {
    const i = k - area.firstMonthKey;
    const f = area.flags[i] | 0;
    if (f & HISTORY_FLAG.Gap) { gapSeen = true; continue; }
    if (skipCurrent && (f & HISTORY_FLAG.Current)) { currentSeen = true; continue; }
    addMonth(a, area, i);
  }
  const b = finish(kind, key, firstKey, lastKey, a, lastKey - firstKey + 1);
  b.gap = b.none && gapSeen;
  b.future = b.none && firstKey > area.firstMonthKey + n - 1;
  if (currentSeen) b.current = true;
  return b;
}

/** One bucket per month of [firstKey, lastKey]. */
export function monthBuckets(area: HistoryArea, firstKey: number, lastKey: number): HistoryBucket[] {
  const out: HistoryBucket[] = [];
  for (let k = firstKey; k <= lastKey; k++) out.push(sumRange(area, "month", k, k, k));
  return out;
}

/** One bucket per July-June season of [firstSeason, lastSeason]. */
export function seasonBuckets(area: HistoryArea, firstSeason: number, lastSeason: number): HistoryBucket[] {
  const out: HistoryBucket[] = [];
  for (let s = firstSeason; s <= lastSeason; s++) out.push(sumRange(area, "season", s, seasonFirstKey(s), seasonFirstKey(s) + 11));
  return out;
}

/** One July-June season as the MONTHLY AVERAGE of its whole months with data (B4): the month in progress and GAP months are left
 *  out; flows, costs and the net are the season's sums over those months divided by their count (perMonth), ski days over the
 *  months that know them, attraction and snow the mean as before. totalGuests / totalNet keep the sums. No whole month: empty. */
export function seasonAverage(area: HistoryArea, season: number): HistoryBucket {
  const b = sumRange(area, "season", season, seasonFirstKey(season), seasonFirstKey(season) + 11, true);
  if (b.none) return b;
  const m = b.months;
  let skiKnown = 0;
  const n = lengthOf(area);
  for (let k = Math.max(b.firstKey, area.firstMonthKey); k <= Math.min(b.lastKey, area.firstMonthKey + n - 1); k++) {
    const i = k - area.firstMonthKey;
    const f = area.flags[i] | 0;
    if (!(f & (HISTORY_FLAG.Gap | HISTORY_FLAG.Current)) && area.skiDays[i] >= 0) skiKnown++;
  }
  const per = (v: number): number => v / m;
  return {
    ...b, perMonth: m,
    tourists: per(b.tourists), residents: per(b.residents), guests: per(b.guests), skiDays: skiKnown > 0 ? b.skiDays / skiKnown : -1,
    ticketsPaid: per(b.ticketsPaid), ticketIncome: per(b.ticketIncome), lodgeIncome: per(b.lodgeIncome), passIncome: per(b.passIncome),
    lessonIncome: per(b.lessonIncome), income: per(b.income), liftCost: per(b.liftCost), runCost: per(b.runCost),
    staffCost: b.staffCost >= 0 ? per(b.staffCost) : -1, costs: per(b.costs), net: per(b.net),
  };
}

/** Is the range worth offering for this area? 12 months always; the two season ranges need data in an earlier season. */
export function rangeEnabled(area: HistoryArea | null, range: HistoryRange): boolean {
  if (range === "months12") return true;
  const n = lengthOf(area);
  if (!area || n === 0) return false;
  return area.firstMonthKey < seasonFirstKey(seasonOf(lastKeyOf(area)));
}

/** The range drawn: the chosen one while enabled, else 12 months. */
export function effectiveRange(area: HistoryArea | null, range: HistoryRange): HistoryRange {
  return rangeEnabled(area, range) ? range : "months12";
}

/** The buckets of a range (oldest first); [] without data. */
export function windowOf(area: HistoryArea | null, range: HistoryRange): HistoryBucket[] {
  const n = lengthOf(area);
  if (!area || n === 0) return [];
  const last = lastKeyOf(area);
  switch (effectiveRange(area, range)) {
    case "seasonVs": return monthBuckets(area, seasonFirstKey(seasonOf(last)), seasonFirstKey(seasonOf(last)) + 11);
    case "bySeason": {
      const out: HistoryBucket[] = [];
      for (let s = seasonOf(area.firstMonthKey); s <= seasonOf(last); s++) out.push(seasonAverage(area, s));
      return out;
    }
    default: return monthBuckets(area, last - 11, last);
  }
}

/** "seasonVs": last season's same 12 months (July-June), drawn gray behind this season's; null for the other ranges. */
export function compareOf(area: HistoryArea | null, range: HistoryRange): HistoryBucket[] | null {
  if (!area || lengthOf(area) === 0 || effectiveRange(area, range) !== "seasonVs") return null;
  const s = seasonOf(lastKeyOf(area)) - 1;
  return monthBuckets(area, seasonFirstKey(s), seasonFirstKey(s) + 11);
}

/** A line's points (B3): `pick` of each bucket, null where the line has no point - an empty bucket, the month in progress (the
 *  line ends at the last whole month: no fake crash) or an unknown value (non-finite). */
export function linePoints(buckets: readonly HistoryBucket[], pick: (b: HistoryBucket) => number): Array<number | null> {
  return buckets.map((b) => {
    if (b.none || (b.current && b.kind === "month")) return null;
    const v = pick(b);
    return typeof v === "number" && Number.isFinite(v) ? v : null;
  });
}

/** The buckets drawn as a hatched "no data" band (B3): recorded nothing inside the area's range. */
export function gapSlots(buckets: readonly HistoryBucket[]): number[] {
  const out: number[] = [];
  buckets.forEach((b, i) => { if (b.gap) out.push(i); });
  return out;
}

/** Whole city: the areas summed month by month over the union of their ranges (null without data). Flags: the OR of the months
 *  that have data; GAP where none has; attraction and snow the mean of the areas that know them; -1 parts stay -1 only when no
 *  area knows them (then SnapshotMissing / SkiDaysPartial mark the month). */
export function sumAreas(areas: readonly HistoryArea[]): HistoryArea | null {
  const rows = areas.filter((a) => a && lengthOf(a) > 0);
  if (rows.length === 0) return null;
  let first = Infinity, last = -Infinity;
  for (const a of rows) { first = Math.min(first, a.firstMonthKey); last = Math.max(last, lastKeyOf(a)); }
  const n = last - first + 1;
  const z = (): number[] => new Array<number>(n).fill(0);
  const out: HistoryArea = {
    sign: { index: 0, version: 0 }, firstMonthKey: first, flags: z(), tourists: z(), residents: z(), skiDays: z(), ticketsPaid: z(),
    ticketIncome: z(), lodgeIncome: z(), passIncome: z(), lessonIncome: z(), liftCost: z(), runCost: z(), buildingsCost: z(), wagesCost: z(),
    attraction: z(), snowPct: z(),
  };
  for (let i = 0; i < n; i++) {
    const k = first + i;
    let flags = 0, has = 0, ski = 0, skiK = 0, bld = 0, bldK = 0, wag = 0, wagK = 0, attr = 0, attrK = 0, snow = 0, snowK = 0;
    for (const a of rows) {
      const j = k - a.firstMonthKey;
      if (j < 0 || j >= lengthOf(a)) continue;
      const f = a.flags[j] | 0;
      if (f & HISTORY_FLAG.Gap) continue;
      has++;
      flags |= f;
      out.tourists[i] += num(a.tourists[j]); out.residents[i] += num(a.residents[j]); out.ticketsPaid[i] += num(a.ticketsPaid[j]);
      out.ticketIncome[i] += num(a.ticketIncome[j]); out.lodgeIncome[i] += num(a.lodgeIncome[j]); out.passIncome[i] += num(a.passIncome[j]);
      out.lessonIncome[i] += num(a.lessonIncome[j]); out.liftCost[i] += num(a.liftCost[j]); out.runCost[i] += num(a.runCost[j]);
      if (a.skiDays[j] >= 0) { ski += a.skiDays[j]; skiK++; } else flags |= HISTORY_FLAG.SkiDaysPartial;
      if (a.buildingsCost[j] >= 0) { bld += a.buildingsCost[j]; bldK++; } else flags |= HISTORY_FLAG.SnapshotMissing;
      if (a.wagesCost[j] >= 0) { wag += a.wagesCost[j]; wagK++; } else flags |= HISTORY_FLAG.SnapshotMissing;
      if (a.attraction[j] >= 0) { attr += a.attraction[j]; attrK++; }
      if (a.snowPct[j] >= 0) { snow += a.snowPct[j]; snowK++; }
    }
    if (has === 0) flags = HISTORY_FLAG.Gap;
    // First is per area: in a city sum it would mark every month an area started in; keep it only if every area starts there.
    else if ((flags & HISTORY_FLAG.First) && rows.some((a) => a.firstMonthKey < k)) flags &= ~HISTORY_FLAG.First;
    out.flags[i] = flags;
    out.skiDays[i] = skiK > 0 ? ski : -1;
    out.buildingsCost[i] = bldK > 0 ? bld : -1;
    out.wagesCost[i] = wagK > 0 ? wag : -1;
    out.attraction[i] = attrK > 0 ? attr / attrK : -1;
    out.snowPct[i] = snowK > 0 ? snow / snowK : -1;
  }
  return out;
}

/** The headline pair: the last whole month (before the month in progress) and the same month a year earlier (null = no data then). */
export interface YearOnYear {
  now: HistoryBucket | null;
  before: HistoryBucket | null;
}

export function yearOnYear(area: HistoryArea | null): YearOnYear {
  const n = lengthOf(area);
  if (!area || n === 0) return { now: null, before: null };
  let key = lastKeyOf(area);
  if ((area.flags[n - 1] | 0) & HISTORY_FLAG.Current) key--;
  if (key < area.firstMonthKey) return { now: null, before: null };
  const now = sumRange(area, "month", key, key, key);
  const before = key - 12 >= area.firstMonthKey ? sumRange(area, "month", key - 12, key - 12, key - 12) : null;
  return { now: now.none ? null : now, before: before && !before.none ? before : null };
}

/** Change in percent (rounded), or null when there is nothing to compare with (no month, or a base of 0 or below). */
export function pctChange(now: number | null | undefined, before: number | null | undefined): number | null {
  if (typeof now !== "number" || typeof before !== "number" || !Number.isFinite(now) || !Number.isFinite(before) || before <= 0) return null;
  return Math.round(((now - before) / before) * 100);
}

/** The headline's year-on-year change in money (B3, F9): now - before, or null without both months. */
export function netChange(y: YearOnYear): number | null {
  return y.now && y.before ? y.now.net - y.before.net : null;
}

/** A round axis top >= v: 1, 2, 2.5 or 5 x 10^n (v <= 0 -> 1). */
export function niceMax(v: number): number {
  if (!(v > 0) || !Number.isFinite(v)) return 1;
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  for (const m of [1, 2, 2.5, 5, 10]) if (m * p >= v - 1e-9) return m * p;
  return 10 * p;
}

/** The bucket under a pointer at fraction x (0..1) of the plot width: index 0..count-1, or -1 outside. */
export function bucketAt(x: number, count: number): number {
  if (!(count > 0) || !(x >= 0) || x > 1) return -1;
  return Math.min(count - 1, Math.floor(x * count));
}

/** Which axis labels to draw: every month for <= 12 buckets, else every `step` buckets ending at the last one. */
export function labelStep(count: number, kind: "month" | "season"): number {
  if (kind === "season") return count <= 12 ? 1 : Math.ceil(count / 12);
  if (count <= 12) return 1;
  if (count <= 24) return 3;
  return 6;
}
