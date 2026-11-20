// The pass rules of 1.0.3 (R3-B, docs/research/round3-2026-11-18/r4-fleet/dest-pass/DESIGN.md): the destination pass of tourist parties and
// the sale window of the residents' season pass (an opening price, a price step, a sales end, and each household's own buy hour).
// Consumers: SkiLiftTicketSystem (the ticket job and the hourly sale, Burst), SkiDaySystem (the send: a party on a destination pass gets P = 1),
// the hub (SkiResortHubUISystem; hubRules.ts mirrors these numbers, gate HubRulesTests). Tests: tests/ResortBatch PassRulesTests.
// Pure file: `using Unity.Mathematics;` and SkiMoneyRules only. Every method is static, allocation-free and Burst-callable. No state is saved
// for any of it: the destination roll and the buy hour are hashes of (household, season), so a load gives the same answers.

using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SkiPassRules
    {
        // ---- Destination pass (tourist parties) ----
        // Share = SeasonPassShare % × PassFactor(pass, ticket) × PriceFactor(fee, the area's tolerance): the same share knob and the same pass
        // demand as the residents' pass, and fewer pass holders when a day on the pass is dear. The fee per person and counted day is a quarter of
        // the pass price (PassFullAt, the residents' break-even), never above the ticket. A party on a pass pays the fee instead of the ticket and
        // is not deterred by the ticket price in the send (P = 1, as a resident holder). The money books as pass income.

        /// <summary>The pass price divided by this is the destination fee: the residents' break-even (4 days).</summary>
        public const int DestVisits = SkiMoneyRules.PassFullAt;

        /// <summary>The destination fee per person and counted day: round(pass / 4), half up, never above the ticket. 0 with tickets off.</summary>
        public static int DestFee(int passPriceSetting, int ticketPrice)
        {
            if (ticketPrice <= 0)
                return 0;
            int pass = SkiMoneyRules.ClampPassPrice(passPriceSetting);
            int quarter = (pass + DestVisits / 2) / DestVisits;
            return math.min(ticketPrice, math.max(1, quarter));
        }

        /// <summary>The share of tourist parties on a destination pass, in [0, 1]: share/100 × PassFactor(pass, ticket) × feePriceFactor.
        /// feePriceFactor is SkiDayRules.PriceFactor(DestFee, the area's tolerance), supplied by the caller (this file does not link SkiDayRules).
        /// 0 with the share at 0 (passes off), tickets off, or a factor that is not &gt; 0 (NaN included).</summary>
        public static double DestShare(int sharePct, int passPriceSetting, int ticketPrice, double feePriceFactor)
        {
            int share = SkiMoneyRules.ClampPassShare(sharePct);
            if (share <= 0 || ticketPrice <= 0 || !(feePriceFactor > 0.0))
                return 0.0;
            double demand = SkiMoneyRules.PassFactor(SkiMoneyRules.SalePassPrice(passPriceSetting, ticketPrice), ticketPrice);
            if (!(demand > 0.0))
                return 0.0;
            double s = share / 100.0 * demand * math.min(1.0, feePriceFactor);
            return s >= 1.0 ? 1.0 : s;
        }

        /// <summary>The party's one destination roll of the season, uniform in [0, 1): a fixed hash of the tourist household (index, version)
        /// and the season key with its own salt, so it is unrelated to TouristRoll (per day), PassRoll and PresaleUniform. The same party gets the
        /// same answer on every day of its stay and after a load.</summary>
        public static double DestRoll(int householdIndex, int householdVersion, int seasonKey)
        {
            uint h;
            unchecked
            {
                h = (uint)householdIndex * 0x9E3779B1u ^ Mix((uint)householdVersion * 0xC2B2AE35u + 0x44455354u);   // "DEST"
                h = Mix(h + (uint)seasonKey * 0x85EBCA6Bu + 0x165667B1u);
            }
            return (h >> 8) * (1.0 / 16777216.0);
        }

        /// <summary>The party is on a destination pass this season: DestRoll &lt; share. A share of 0 (or NaN) never.</summary>
        public static bool OnDestPass(int householdIndex, int householdVersion, int seasonKey, double share)
            => share > 0.0 && DestRoll(householdIndex, householdVersion, seasonKey) < share;

        // ---- Sale window (residents' season pass) ----
        // Season months (0 = the season's first month, SkiMoneyRules.SeasonFirstMonthOf: July for a December winter):
        //   0      Before   no pass sales (the season has just rolled over)
        //   1-2    Opening  the slider price
        //   3-4    Stepped  the slider price + StepPct %, snapped to the 20 step
        //   5-11   Closed   sales end (the winter): first arrivals pay day tickets
        // Each household's buy hour sits in the window (months 1-4) at 1 - sqrt(1 - u): a decreasing triangular density, so 43.75 % of the
        // households have their hour in the first month and 75 % before the step (the early-bird rush).

        public const int WindowFirstMonth = SkiMoneyRules.PresaleMonthOfSeason;   // 1
        public const int WindowMonths = 4;
        public const int StepMonth = 3;     // the first season month at the stepped price
        public const int StepPct = 15;      // Epic 2025-26: +12.8 % opening to final, Epic Local +17.5 %

        public enum SalePhase : byte { Before = 0, Opening = 1, Stepped = 2, Closed = 3 }

        /// <summary>Months since the start of the season's first month, in [0, 12): calendar months from normalizedDate × 12 (as
        /// SkiMoneyRules.MonthKey) shifted by the season's first month (1..12, clamped). A non-finite date reads 0.</summary>
        public static double SeasonPosition(float normalizedDate, int firstMonth)
        {
            double d = math.isfinite(normalizedDate) ? normalizedDate - math.floor(normalizedDate) : 0.0;
            int f = math.clamp(firstMonth, 1, SkiMoneyRules.MonthsPerYear);
            double pos = d * SkiMoneyRules.MonthsPerYear - (f - 1);
            if (pos < 0.0)
                pos += SkiMoneyRules.MonthsPerYear;
            return pos >= SkiMoneyRules.MonthsPerYear ? pos - SkiMoneyRules.MonthsPerYear : pos;
        }

        /// <summary>The sale phase at <paramref name="seasonPosition"/> (SeasonPosition).</summary>
        public static SalePhase PhaseOf(double seasonPosition)
        {
            int m = (int)math.floor(seasonPosition);
            if (m < WindowFirstMonth)
                return SalePhase.Before;
            if (m < StepMonth)
                return SalePhase.Opening;
            if (m < WindowFirstMonth + WindowMonths)
                return SalePhase.Stepped;
            return SalePhase.Closed;
        }

        /// <summary>Sales are open (Opening or Stepped).</summary>
        public static bool SaleOpen(SalePhase phase) => phase == SalePhase.Opening || phase == SalePhase.Stepped;

        /// <summary>How far the window has run, [0, 1) inside it: (position − 1) / 4. Below 0 before it, 1 or more after it.</summary>
        public static double WindowPosition(double seasonPosition) => (seasonPosition - WindowFirstMonth) / WindowMonths;

        /// <summary>The stepped pass price: the setting × (100 + StepPct) / 100, snapped to the 20 step and clamped (SkiMoneyRules.ClampPassPrice).</summary>
        public static int SteppedPrice(int passPriceSetting)
        {
            long p = (long)SkiMoneyRules.ClampPassPrice(passPriceSetting) * (100 + StepPct);
            return SkiMoneyRules.ClampPassPrice((int)math.min((p + 50) / 100, (long)int.MaxValue));
        }

        /// <summary>The pass price a resident pays now: the setting while Opening, SteppedPrice while Stepped, 0 when sales are not open or
        /// tickets are off (a pass is judged against day tickets, SkiMoneyRules.SalePassPrice).</summary>
        public static int SalePrice(int passPriceSetting, int ticketPrice, SalePhase phase)
        {
            int open = SkiMoneyRules.SalePassPrice(passPriceSetting, ticketPrice);
            if (open <= 0)
                return 0;
            switch (phase)
            {
                case SalePhase.Opening: return open;
                case SalePhase.Stepped: return SteppedPrice(passPriceSetting);
                default: return 0;
            }
        }

        /// <summary>The household's buy hour as a window position in [0, 1): 1 − sqrt(1 − u), u a fixed hash of (household index, season) with its
        /// own salt. Front-loaded: P(b &lt; x) = 2x − x².</summary>
        public static double BuyPosition(int householdIndex, int seasonKey)
        {
            uint h;
            unchecked
            {
                h = Mix((uint)householdIndex * 0x9E3779B1u + (uint)seasonKey * 0x85EBCA6Bu + 0x42555931u);   // "BUY1"
            }
            double u = (h >> 8) * (1.0 / 16777216.0);
            return 1.0 - math.sqrt(1.0 - u);
        }

        /// <summary>The household's hour has come: its BuyPosition is at or before <paramref name="windowPosition"/>.</summary>
        public static bool HourCome(int householdIndex, int seasonKey, double windowPosition)
            => BuyPosition(householdIndex, seasonKey) <= windowPosition;

        /// <summary>The Murmur3 32-bit finalizer (the constants of SkiMoneyRules.Mix, re-stated: that one is private).</summary>
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
    }
}
