// SkiMoneyRules — the PURE money rules of Round E1 (docs/research/econ-round/spec/ch4-money.md §6.1;
// decisions D23 lift running charge, D26 lift tickets). Consumers: SkiLiftChargeSystem (LiftBillingOf,
// LiftAreaRunOpen, LiftRate, LiftOperating, Accrue, AccruedShare), SkiLiftTicketSystem (ClampTicketPrice,
// TicketGateOf, TicketsPaid — from its Burst job), SkiGuestCounterSystem (MonthKey), SkiResortStatsSystem /
// HubRules (gate, price, item rates), SkiResortAttractivenessSystem (MonthKey / YearOfKey / MonthOfKey for its
// [cost] clock, Round M2 D8), the settings (TicketPriceDefault); SkiGuestCounterSystem (TouristLeisureCounts,
// OutingPeople — from its Burst jobs, Round S1); SkiGuestCounterSystem and SkiDaySystem (IsShortMonth,
// SimulatedPct, Round M1); tests/ResortBatch MoneyBatch (T1-T7; T9-T11 Round S1; T12 Round M1; T13 Round M2).
//
//   * Lift charge (D23): Full = LiftFixedPerMonth + LiftPerKmPerMonth × km; a lift that is not
//     complete, or complete without power (LiftServedRule.IsPowered, e1-spec R1), or (Round M2 T2,
//     docs/research/round-m2/decisions.md R3) a complete, powered lift whose ski area has no counted run
//     (LiftAreaRunOpen false: the area's released score counts 0 runs), bills LiftReducedShare of that.
//     Phase D: the charge bills through LiftUpkeepBillingOf, so "without power" no longer reduces it (paragraph below).
//     LiftOperating(complete, powered) is the charge's operating count (logs and the hub only; the ticket
//     gate no longer reads lifts since Round M2 T1).
//   * Accrual: the run-upkeep pattern (SkiRunUpkeepSystem's month = TicksPerDay × daysPerYear / 12
//     sim frames; TimeSystem.kTicksPerDay, Game.Simulation/TimeSystem.cs:18): carry += rate × elapsed
//     / month, the whole credits are debited, the fraction carried.
//   * Ticket gate (D26, R6; Round M2 T1, decisions.md R2): Off (price 0) → NoEfficiency (sign efficiency
//     not > 0) → NotKnownYet (no score yet, or the area's first score after a load still held) → Open on a
//     counted run, else NothingOpen (a running lift alone no longer opens it; the operating-lift branch and
//     its parameter are gone). TicketGate's literal values are compared with bindings.ts TICKET_GATE by gate
//     rule R17 (unchanged).
//   * Months: MonthKey = year × 12 + calendar month 0..11; MonthOfKey is 1..12. Round M1 (D5): a month with
//     fewer than FullMonthShare (95 %) of its frames simulated is short (IsShortMonth) and is booked as partial.
//   * Guest days (Round S1 D23, ski-days spec ch3 §1.2): per ski area and game day, a tourist on leisure at a
//     lodge counts once and not when its household came by outing that day; an outing counts only the
//     members not yet counted there that day. The household's day flags are GuestDayOuting | GuestDayLeisure;
//     the day key is SkiDayRules.DayKey (s1-spec N3), the day sets live in the counter.
//
// Season passes (DECISIONS 240, docs/research/econ-2/build/season-pass-v1.md): SeasonKeyOf / SeasonKeyOfDate (July on = the year, else
// year - 1), PassPrice, PassRoll (the household's one deterministic roll), PassStandingOf (what its SkiSeasonPass says this season),
// PassSale (NotEligible / Declined / Bought / CannotPay). Called from the ticket job (Burst); MoneyBatch T15.
//
// Econ-2 round 2 (DECISIONS 241, docs/research/econ-2/build/round-2.md): TicketPriceDefault 40 -> 60 and LodgeSpendDefault 0 -> 15
// (through SkiCatalog.LodgeSpend); lessons (LessonShare / LessonMultiplier consts and clamps, LessonPrice, LessonRoll, LessonsTaken,
// LessonsPaid); the pre-season pass sale (PresaleUniform, PresaleProbability, PresaleDecision). MoneyBatch T16 / T17.
//
// PURE FILE: `using System;` and `using Unity.Mathematics;` only, + SkiCatalog consts (compile-time literals;
// Burst sees numbers — never a SkiCatalog.Lifts[…] row read here, contract D4 / risk R-c). Every method is static,
// allocation-free and Burst-callable (the ticket job calls TicketGateOf / TicketsPaid).
//
// Created 2026-10-04 (Round E1 stage 0, S0: ch4 §6.1 verbatim); owned by stream S4.
// Round M2 (2026-10-10, builder B3, docs/research/round-m2/build-contract.md): T1 TicketGateOf loses the
// operating-lift branch and parameter; T2 LiftBilling.NoOpenRun, the three-argument LiftBillingOf, LiftAreaRunOpen.
// Catalog Phase A (2026-10-11, docs/research/art/assets-2026-10/catalog-a/phase-a-contract.md R7): the two lift
// charge numbers alias SkiCatalog.FixedGrip (one source; tests/ResortBatch CatalogGuardTests CG-1, MoneyBatch T1-T7
// unchanged). LiftReducedShare stays global policy here (extensibility.md §1).
// Catalog Phase B (2026-10-11, docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §6.2, lift group):
//   * LiftFullRate(lengthM, fixedPerMonth, perKmPerMonth) and LiftRate(billing, lengthM, fixed, perKm): the lift type's
//     rate, the numbers passed in by the caller (SkiLiftChargeSystem reads the row on the main thread; nothing here
//     reads a SkiCatalog row, risk R-a). The one-length overloads stay = the fixed-grip consts (MoneyBatch T1-T7).
//   * Lodge spend (seam S3, D15): LodgeSpendDefault / Max / Step alias SkiCatalog.LodgeSpend (CG-1);
//     ClampLodgeSpend, LodgeSpendOf(setting, multiplier) = floor(setting × multiplier + 0.5) (0 when either is not
//     > 0), SpendPaid(paid, spendPerPerson, moneyAfterTickets) = min(paid, money / spend) (0 when any is not > 0):
//     the ticket job charges what a household has left after its tickets, never into debt (DV9).
// Phase D (2026-10-11, docs/research/art/assets-2026-10/code-round-4/round-4-contract-skeptic.md MUST-1 (a); Richard's
// sub-ruling ~15:25 under DECISIONS 171: "an unpowered lift still costs its maintenance ('it does not magically
// disappear') — upkeep is charged regardless of power"; rules group): LiftUpkeepBillingOf(complete, areaRunOpen) is the
// billing SkiLiftChargeSystem applies — LiftBillingOf with powered = true, so power never reduces the upkeep. The reductions
// that remain (each LiftReducedShare, 25 %): Unfinished (any census state but Complete) and NoOpenRun (complete, its ski area
// counts no run). A run counts only when its value, the snow open factor included, is > 0 (SkiResortScore.cs:326-338,
// :369-380), so NoOpenRun applies in every period without snow at ANY backcountry share, and with backcountry 0 to an area
// whose only serving lift is unpowered. An unpowered complete lift lands there like every complete lift of its area: it pays
// in full while its area counts a run, 25 % while it counts none (round-4-integration.md §6 L1 asks Richard to confirm this
// or choose "strict", i.e. an unpowered lift always in full). LiftOperating and the charge's Operating count keep the real
// power. LiftBillingOf, the LiftBilling enum (NoPower stays bound, now unreachable from the
// charge) and their MoneyBatch oracles are unchanged.

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SkiMoneyRules
    {
        // D23. Catalog Phase A (ext row 13; was SkiMoneyRules.cs:46-47 = 2500f / 1000f): const aliases (D4).
        public const float LiftFixedPerMonth = SkiCatalog.FixedGrip.FixedPerMonth;
        public const float LiftPerKmPerMonth = SkiCatalog.FixedGrip.PerKmPerMonth;
        public const float LiftReducedShare = 0.25f;
        // D26
        public const int TicketPriceDefault = 80;   // T137 I1 (DECISIONS 358): was 60 (DECISIONS 241; 40 before); the ski-day price factor at 80 is 0.8
        public const int TicketPriceMax = 200;
        public const int TicketPriceStep = 5;
        // time (TimeSystem.kTicksPerDay, Game.Simulation/TimeSystem.cs:18; the ECS systems assert it at arm time)
        public const int TicksPerDay = 262144;
        public const int MonthsPerYear = 12;

        // Round M2 T2 appends NoOpenRun = 3 (not bound to the UI: no R17 entry).
        public enum LiftBilling : byte { Full = 0, NoPower = 1, Unfinished = 2, NoOpenRun = 3 }
        public enum TicketGate : byte { Open = 0, Off = 1, NoEfficiency = 2, NothingOpen = 3, NotKnownYet = 4 }

        public static bool LiftOperating(bool complete, bool powered) => complete && powered;

        /// <summary>Round M2 T2 (decisions.md R3): precedence unfinished, then no power, then no counted run in
        /// the lift's ski area (areaRunOpen from LiftAreaRunOpen). Any billing but Full bills LiftReducedShare.</summary>
        public static LiftBilling LiftBillingOf(bool complete, bool powered, bool areaRunOpen)
            => !complete ? LiftBilling.Unfinished : !powered ? LiftBilling.NoPower
               : areaRunOpen ? LiftBilling.Full : LiftBilling.NoOpenRun;

        /// <summary>Phase D (skeptic MUST-1 (a); DECISIONS 171 sub-ruling): THE billing of the lift charge — power never
        /// reduces the upkeep, so this is LiftBillingOf(complete, powered: true, areaRunOpen): Unfinished, else NoOpenRun when
        /// the area has no counted run, else Full. It takes no power argument on purpose.</summary>
        public static LiftBilling LiftUpkeepBillingOf(bool complete, bool areaRunOpen)
            => LiftBillingOf(complete, true, areaRunOpen);

        /// <summary>Round M2 T2: the lift's ski area has a counted run, for billing. Unknown reads true (full, the
        /// old rate): not linked, no score yet, or the area's first score after a load still held.</summary>
        public static bool LiftAreaRunOpen(bool linked, bool scoreKnown, bool writeHeld, int runsCounted)
            => !linked || !scoreKnown || writeHeld || runsCounted > 0;

        /// <summary>2500 + 1000 × km; a negative or non-finite length counts as 0 m. Catalog Phase B: = the fixed-grip
        /// row's LiftFullRate(lengthM, LiftFixedPerMonth, LiftPerKmPerMonth).</summary>
        public static float LiftFullRate(float lengthM)
            => LiftFullRate(lengthM, LiftFixedPerMonth, LiftPerKmPerMonth);

        public static float LiftRate(LiftBilling billing, float lengthM)
            => LiftRate(billing, lengthM, LiftFixedPerMonth, LiftPerKmPerMonth);

        /// <summary>Catalog Phase B (round-2-contract §6.2): the lift type's full rate, fixedPerMonth + perKmPerMonth ×
        /// km (LiftTypeDef.FixedPerMonth / PerKmPerMonth, passed in by the caller); a negative or non-finite length counts
        /// as 0 m, a negative or non-finite rate part as 0.</summary>
        public static float LiftFullRate(float lengthM, float fixedPerMonth, float perKmPerMonth)
        {
            float m = math.isfinite(lengthM) && lengthM > 0f ? lengthM : 0f;
            float fixedPart = math.isfinite(fixedPerMonth) && fixedPerMonth > 0f ? fixedPerMonth : 0f;
            float perKm = math.isfinite(perKmPerMonth) && perKmPerMonth > 0f ? perKmPerMonth : 0f;
            return fixedPart + perKm * m / 1000f;
        }

        /// <summary>Catalog Phase B: LiftRate with the lift type's two rate numbers; any billing but Full bills
        /// LiftReducedShare (global policy) of the full rate.</summary>
        public static float LiftRate(LiftBilling billing, float lengthM, float fixedPerMonth, float perKmPerMonth)
            => LiftRate(billing, lengthM, fixedPerMonth, perKmPerMonth, LiftReducedShare);

        /// <summary>T137 S1 (DECISIONS 358): the share a Shuttle type (the tram, sightseeing) bills while its ski area has no
        /// open run: it keeps running (DECISIONS 347 e), so half the full rate instead of the quarter of a stopped lift.</summary>
        public const float ShuttleNoOpenRunShare = 0.5f;

        /// <summary>T137 S1: LiftRate with the type's share for NoOpenRun (<paramref name="noOpenRunShare"/>, LiftClosedRule.NoOpenRunShare);
        /// Unfinished and NoPower keep LiftReducedShare. A share that is not finite or not in 0..1 reads LiftReducedShare.</summary>
        public static float LiftRate(LiftBilling billing, float lengthM, float fixedPerMonth, float perKmPerMonth, float noOpenRunShare)
        {
            float full = LiftFullRate(lengthM, fixedPerMonth, perKmPerMonth);
            if (billing == LiftBilling.Full)
                return full;
            float share = billing == LiftBilling.NoOpenRun && math.isfinite(noOpenRunShare) && noOpenRunShare >= 0f && noOpenRunShare <= 1f
                ? noOpenRunShare
                : LiftReducedShare;
            return share * full;
        }

        // ── Catalog Phase B seam S3 (round-2-contract §6.2, D15): lodge spend per paying guest ─────────────────────
        public const int LodgeSpendDefault = SkiCatalog.LodgeSpend.Default;   // 15 (DECISIONS 241; was 0 = off; ResortBatch CG-1)
        public const int LodgeSpendMax = SkiCatalog.LodgeSpend.Max;           // 50
        public const int LodgeSpendStep = SkiCatalog.LodgeSpend.Step;         // 5

        /// <summary>The slider value clamped to 0..LodgeSpendMax (the settings file can hold anything).</summary>
        public static int ClampLodgeSpend(int v) => math.clamp(v, 0, LodgeSpendMax);

        /// <summary>Spend per person at one lodge: floor(setting × multiplier + 0.5) (LodgeDef.SpendMultiplier: A-frame
        /// and hut 1, chalet 1.5, summit and grand 3); 0 when the setting is not &gt; 0 or the multiplier not &gt; 0.</summary>
        public static int LodgeSpendOf(int setting, float multiplier)
            => setting <= 0 || !(multiplier > 0f) ? 0 : (int)math.floor(setting * multiplier + 0.5f);

        /// <summary>Paying guests of a household that also spend at the lodge: min(paid, money left after the tickets /
        /// spend per person) — never into debt; 0 when any input is not &gt; 0.</summary>
        public static int SpendPaid(int paidPeople, int spendPerPerson, int moneyAfterTickets)
            => paidPeople <= 0 || spendPerPerson <= 0 || moneyAfterTickets <= 0
                ? 0
                : math.min(paidPeople, moneyAfterTickets / spendPerPerson);

        /// <summary>Sim frames in one month: TicksPerDay × max(1, daysPerYear) / 12 (SkiRunUpkeepSystem's month).</summary>
        public static float MonthFrames(int daysPerYear) => (float)TicksPerDay * math.max(1, daysPerYear) / MonthsPerYear;

        // Round M1 (D5, m1-spec M7c): a developer time jump moves the clock, not the frame counter, so a jump that
        // lands exactly one month later rolls a month that was barely simulated. Such a month is partial.
        /// <summary>A month counts as full from this share of its frames simulated on (one "+1 h" press keeps a
        /// month full at 95.8 %, two make it partial; m1-spec Open point 2).</summary>
        public const float FullMonthShare = 0.95f;

        /// <summary>Fewer than FullMonthShare × MonthFrames(daysPerYear) frames simulated (double compare, so a
        /// large frame count loses no precision).</summary>
        public static bool IsShortMonth(uint framesSimulated, int daysPerYear)
            => (double)framesSimulated < (double)FullMonthShare * MonthFrames(daysPerYear);

        /// <summary>The share of a month simulated in whole percent: min(100, floor(100 × frames / MonthFrames)).</summary>
        public static int SimulatedPct(uint framesSimulated, int daysPerYear)
            => (int)math.min(100.0, math.floor(100.0 * framesSimulated / MonthFrames(daysPerYear)));

        /// <summary>The upkeep's accrual: carry += rate × elapsed / month; returns the whole credits to debit
        /// and keeps the fraction. A rate that is not > 0 accrues nothing.</summary>
        public static int Accrue(ref float carry, float monthlyRate, uint elapsedFrames, int daysPerYear)
        {
            if (!(monthlyRate > 0f)) return 0;
            carry += monthlyRate * (elapsedFrames / MonthFrames(daysPerYear));
            int debit = (int)carry;
            carry -= debit;
            return debit;
        }

        /// <summary>What a rate accrues over elapsedFrames (for per-ski-area attribution).</summary>
        public static double AccruedShare(float monthlyRate, uint elapsedFrames, int daysPerYear)
            => monthlyRate > 0f ? (double)monthlyRate * elapsedFrames / MonthFrames(daysPerYear) : 0.0;

        public static int ClampTicketPrice(int price) => math.clamp(price, 0, TicketPriceMax);

        /// <summary>Round M2 T1 (decisions.md R2): tickets need a counted open run. runStateKnown = a score is
        /// present AND not in the first-write hold (callers: the ticket job, the gate watch, the hub). Free entry
        /// during a load hold is accepted (at most RatingHoldMaxTicks = 1,024 ticks = 16,384 frames).</summary>
        public static TicketGate TicketGateOf(int price, float efficiency, bool runStateKnown, int openRuns)
        {
            if (price <= 0) return TicketGate.Off;
            if (!(efficiency > 0f)) return TicketGate.NoEfficiency;
            if (!runStateKnown) return TicketGate.NotKnownYet;
            return openRuns > 0 ? TicketGate.Open : TicketGate.NothingOpen;
        }

        /// <summary>Whole tickets a household pays: min(people, money / price); 0 when any input is not > 0.</summary>
        public static int TicketsPaid(int people, int price, int householdMoney)
        {
            if (people <= 0 || price <= 0 || householdMoney <= 0) return 0;
            return math.min(people, householdMoney / price);
        }

        // ── Season passes ────────────────────────────────────────────────────────────────────────────────────────────────
        public const int PassMultiplierMin = 2;
        public const int PassMultiplierMax = 10;
        public const int PassMultiplierDefault = 2;   // T137 I3 (DECISIONS 358): was 4; a season is about six ski days
        public const int PassShareMax = 100;
        public const int PassShareStep = 5;
        public const int PassShareDefault = 50;   // 0 = passes off
        /// <summary>The ski season starts with this calendar month (1..12): July. Months 7..12 belong to the year, 1..6 to the
        /// year before, so December and the following March share a season key.</summary>
        public const int SeasonFirstMonth = 7;
        /// <summary>SkiSeasonPass.Flags bit: the household was asked this season and did not buy (rolled out or could not pay).</summary>
        public const byte PassFlagDeclined = 1;

        public enum PassSaleResult : byte { NotEligible = 0, Declined = 1, Bought = 2, CannotPay = 3 }
        public enum PassStanding : byte { None = 0, Holder = 1, Declined = 2 }

        /// <summary>The ski-season year of <paramref name="year"/> + calendar <paramref name="month"/> (1..12, clamped): the
        /// year from July on, else the year before.</summary>
        public static int SeasonKeyOf(int year, int month)
            => math.clamp(month, 1, MonthsPerYear) >= SeasonFirstMonth ? year : year - 1;

        /// <summary>SeasonKeyOf from the clock the systems read (TimeSystem.year, normalizedDate; MonthKey's month rule).</summary>
        public static int SeasonKeyOfDate(int year, float normalizedDate)
            => SeasonKeyOf(year, MonthOfKey(MonthKey(year, normalizedDate)));

        public static int ClampPassMultiplier(int v) => math.clamp(v, PassMultiplierMin, PassMultiplierMax);
        public static int ClampPassShare(int v) => math.clamp(v, 0, PassShareMax);

        /// <summary>The pass price: multiplier × the day-ticket price at the moment of sale (multiplier clamped to
        /// PassMultiplierMin..Max); 0 when the ticket price is not &gt; 0 (no pass without a ticket to price it by).</summary>
        public static int PassPrice(int multiplier, int ticketPrice)
            => ticketPrice <= 0 ? 0 : (int)math.min((long)ClampPassMultiplier(multiplier) * ticketPrice, (long)int.MaxValue);

        /// <summary>The Murmur3 32-bit finalizer shared by PassRoll, LessonRoll and PresaleUniform (each builds its own pre-image).</summary>
        private static uint Mix(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
            }
            return h;
        }

        /// <summary>The household's one roll of the season: true with probability sharePct/100. A fixed integer hash of the
        /// household entity index and the season key (no state), so the same inputs give the same answer on every call and
        /// after a load. share &lt;= 0 never, &gt;= 100 always; rolling at a lower share implies the same household rolls
        /// true at every higher one.</summary>
        public static bool PassRoll(int householdIndex, int seasonKey, int sharePct)
        {
            if (sharePct <= 0)
                return false;
            if (sharePct >= PassShareMax)
                return true;
            uint h;
            unchecked
            {
                h = (uint)householdIndex * 0x9E3779B1u + (uint)seasonKey * 0x85EBCA6Bu + 0x27D4EB2Fu;
            }
            return Mix(h) % 100u < (uint)sharePct;
        }

        /// <summary>What the household's SkiSeasonPass says for <paramref name="seasonKey"/>: None when it has no component or the
        /// component is of another season (an older pass counts as absent), Declined with PassFlagDeclined, else Holder.</summary>
        public static PassStanding PassStandingOf(bool hasComponent, int storedSeasonKey, byte flags, int seasonKey)
            => !hasComponent || storedSeasonKey != seasonKey ? PassStanding.None
               : (flags & PassFlagDeclined) != 0 ? PassStanding.Declined : PassStanding.Holder;

        /// <summary>The first-arrival decision for a household with no pass standing this season (job-callable): NotEligible when
        /// it is not a resident, the share is not &gt; 0 or the price not &gt; 0; Declined when the roll fails; Bought when the
        /// roll succeeds and the household has at least the price (never into debt); else CannotPay. Callers store Declined for
        /// both Declined and CannotPay (not asked again this season).</summary>
        public static PassSaleResult PassSale(bool resident, int sharePct, int householdIndex, int seasonKey, int passPrice, int householdMoney)
        {
            if (!resident || sharePct <= 0 || passPrice <= 0)
                return PassSaleResult.NotEligible;
            if (!PassRoll(householdIndex, seasonKey, sharePct))
                return PassSaleResult.Declined;
            return householdMoney >= passPrice ? PassSaleResult.Bought : PassSaleResult.CannotPay;
        }

        // ── Ski-school lessons ───────────────────────────────────────────────────────────────────────────────────────────
        // A counted arrival at an OPEN gate of a ski area whose sign has an ACTIVE ski school: each person counted takes a lesson with
        // probability LessonShare %, decided by a stateless hash of (household index, day key, person index); the lesson costs
        // LessonMultiplier × the day ticket now, is taken from what the household has left after the ticket / pass (never into debt),
        // and is booked exactly like tickets. Called from the ticket job (Burst).
        public const int LessonShareMax = 50;
        public const int LessonShareStep = 5;
        public const int LessonShareDefault = 30;   // 0 = lessons off; T137 I2 (DECISIONS 358): was 20
        public const float LessonMultiplierMin = 1.0f;
        public const float LessonMultiplierMax = 3.0f;
        public const float LessonMultiplierStep = 0.5f;
        public const float LessonMultiplierDefault = 1.5f;
        /// <summary>A household never has more people than this in a counted arrival; LessonsTaken stops here (a guard, not a rule).</summary>
        public const int LessonPeopleGuard = 64;

        public static int ClampLessonShare(int v) => math.clamp(v, 0, LessonShareMax);

        /// <summary>The multiplier clamped to 1.0..3.0; a non-finite value reads the default (the settings file can hold anything).</summary>
        public static float ClampLessonMultiplier(float v)
            => !math.isfinite(v) ? LessonMultiplierDefault : math.clamp(v, LessonMultiplierMin, LessonMultiplierMax);

        /// <summary>The lesson price: floor(multiplier × the day ticket now + 0.5), the multiplier clamped; 0 when the ticket price is not &gt; 0.</summary>
        public static int LessonPrice(float multiplier, int ticketPrice)
            => ticketPrice <= 0 ? 0 : (int)math.min(math.floor((double)ClampLessonMultiplier(multiplier) * ticketPrice + 0.5), (double)int.MaxValue);

        /// <summary>One person's roll: true with probability sharePct/100. A fixed integer hash (pre-image constants different
        /// from PassRoll's) of the household entity index, the day key (SkiDayRules.DayKey) and the person's index within the arrival,
        /// so the same inputs give the same answer on every call. share &lt;= 0 never, &gt;= 100 always; monotone in the share.</summary>
        public static bool LessonRoll(int householdIndex, int dayKey, int personIndex, int sharePct)
        {
            if (sharePct <= 0)
                return false;
            if (sharePct >= 100)
                return true;
            uint h;
            unchecked
            {
                h = (uint)householdIndex * 0x9E3779B1u + (uint)dayKey * 0x7FEB352Du + (uint)personIndex * 0x846CA68Bu + 0x165667B1u;
            }
            return Mix(h) % 100u < (uint)sharePct;
        }

        /// <summary>People of one arrival (0..LessonPeopleGuard) whose roll succeeds: one lesson per person at most.</summary>
        public static int LessonsTaken(int people, int householdIndex, int dayKey, int sharePct)
        {
            int n = math.min(math.max(0, people), LessonPeopleGuard);
            int taken = 0;
            for (int p = 0; p < n; p++)
            {
                if (LessonRoll(householdIndex, dayKey, p, sharePct))
                    taken++;
            }
            return taken;
        }

        /// <summary>Lessons actually paid: min(taken, money left after the ticket or pass / lesson price) — never into debt; 0 when any input is not &gt; 0.</summary>
        public static int LessonsPaid(int taken, int lessonPrice, int moneyAfter)
            => taken <= 0 || lessonPrice <= 0 || moneyAfter <= 0 ? 0 : math.min(taken, moneyAfter / lessonPrice);

        // ── Pre-season pass sale ─────────────────────────────────────────────────────────────────────────────────────────
        // Once per (ski area, season) at the first ticket tick in which the area's gate is Open: every resident household in the pool
        // (within reach of the area, no SkiSeasonPass standing this season) rolls ONE deterministic roll against
        // p = PassShare % × SkiDayRules.Chance(resident, openShare 1, powder 0, pass holder, its own distance / class / confidence).
        // Called from the sale job (Burst).

        /// <summary>The household's one pre-season roll, uniform in [0, 1): a fixed integer hash (the PassRoll pre-image plus 1, so it
        /// is unrelated to the first-arrival roll of the same household and season) of the household entity index and the season key;
        /// 24 bits of the hash. Same inputs, same answer on every call and after a load.</summary>
        public static double PresaleUniform(int householdIndex, int seasonKey)
        {
            uint h;
            unchecked
            {
                h = (uint)householdIndex * 0x9E3779B1u + (uint)seasonKey * 0x85EBCA6Bu + 0x27D4EB2Fu + 1u;
            }
            return (Mix(h) >> 8) * (1.0 / 16777216.0);
        }

        /// <summary>p = (PassShare clamped 0..100) / 100 × chance, in [0, 1]; 0 when the share is not &gt; 0 or the chance not &gt; 0 (NaN included).</summary>
        public static double PresaleProbability(int sharePct, double chance)
        {
            int share = ClampPassShare(sharePct);
            if (share <= 0 || !(chance > 0.0))
                return 0.0;
            double p = share / 100.0 * chance;
            return p >= 1.0 ? 1.0 : p;
        }

        /// <summary>One pool household's outcome: NotEligible when the price is not &gt; 0; Declined when p is not &gt; 0 or the roll
        /// (PresaleUniform &lt; p) fails; Bought when it succeeds and the household has at least the price (never into debt);
        /// else CannotPay. Callers store Declined for both Declined and CannotPay, like the first-arrival sale.</summary>
        public static PassSaleResult PresaleDecision(int householdIndex, int seasonKey, double probability, int passPrice, int householdMoney)
        {
            if (passPrice <= 0)
                return PassSaleResult.NotEligible;
            if (!(probability > 0.0) || !(PresaleUniform(householdIndex, seasonKey) < probability))
                return PassSaleResult.Declined;
            return householdMoney >= passPrice ? PassSaleResult.Bought : PassSaleResult.CannotPay;
        }

        /// <summary>year × 12 + calendar month 0..11 of the game year; a non-finite date reads month 0.</summary>
        public static int MonthKey(int year, float normalizedDate)
        {
            int m = math.isfinite(normalizedDate) ? (int)math.floor(normalizedDate * MonthsPerYear) : 0;
            return year * MonthsPerYear + math.clamp(m, 0, MonthsPerYear - 1);
        }

        public static int YearOfKey(int key) => (int)math.floor(key / (double)MonthsPerYear);
        public static int MonthOfKey(int key) => key - YearOfKey(key) * MonthsPerYear + 1;   // 1..12

        // Round S1 D23 (spec ski-days ch3 §1): per ski area, per game day.
        public const byte GuestDayOuting = 1;    // household counted there today through an attraction outing
        public const byte GuestDayLeisure = 2;   // household counted there today through a member's leisure visit
        // (no DayKey here: the counter calls SkiDayRules.DayKey, s1-spec N3)

        /// <summary>A tourist's leisure arrival at a lodge counts unless the citizen was counted at this ski area
        /// today or its household came by outing today. newGroup: the household's first count there today.</summary>
        public static bool TouristLeisureCounts(bool citizenCountedToday, byte householdDay, out bool newGroup)
        {
            newGroup = false;
            if (citizenCountedToday || (householdDay & GuestDayOuting) != 0)
                return false;
            newGroup = householdDay == 0;
            return true;
        }

        /// <summary>An outing that reached Attending: the attendees not yet counted at this ski area today
        /// (alreadyCountedToday clamped to 0..party). newGroup when someone is counted and the household had no
        /// count there today.</summary>
        public static int OutingPeople(int partySize, int alreadyCountedToday, byte householdDay, out bool newGroup)
        {
            int party = math.max(0, partySize);
            int people = party - math.clamp(alreadyCountedToday, 0, party);
            newGroup = people > 0 && householdDay == 0;
            return people;
        }
    }
}
