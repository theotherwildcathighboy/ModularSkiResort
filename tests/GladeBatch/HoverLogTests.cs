// GladeHoverLog tests check these points:
// - dedup
// - the 40-line cap with an exact suppressed count
// - Switching is never logged
// - dwell buckets sum to the total
// - the summary format (0-plan case included)
// - no managed allocation on the per-frame calls

using System;
using System.Globalization;
using SkiResortMod.Systems;

namespace GladeBatch
{
    internal static partial class Program
    {
        private static void HoverLogTests()
        {
            const string w = "hoverlog";
            GladeHoverLog log = new GladeHoverLog();

            // ---- dedup on (kind, entity, reason).
            log.Begin();
            Check(log.Offer(GladeHoverKind.Terrain, 0, 0), w, "first offer not logged");
            Check(!log.Offer(GladeHoverKind.Terrain, 0, 0), w, "the same (kind, entity, reason) twice logged twice");
            Check(log.Offer(GladeHoverKind.Terrain, 0, 1), w, "a new reason was not logged");
            Check(log.Offer(GladeHoverKind.Unbound, 42, 0), w, "a new kind was not logged");
            Check(log.Offer(GladeHoverKind.Unbound, 43, 0), w, "a new entity was not logged");
            Check(log.Logged == 4 && log.Suppressed == 0, w, "Logged " + log.Logged + " (expected 4)");

            // ---- Switching / None are never logged and do not move the dedup key.
            for (int i = 0; i < 100; i++)
                Check(!log.Offer(GladeHoverKind.Switching, i, (byte)i), w, "Switching logged");
            Check(!log.Offer(GladeHoverKind.None, 0, 0), w, "None logged");
            Check(!log.Offer(GladeHoverKind.Unbound, 43, 0), w, "Switching reset the dedup key (Unbound → Switching → Unbound logged twice)");
            Check(log.Logged == 4, w, "Switching/None changed Logged to " + log.Logged);

            // ---- cap: 41 alternating offers → 40 logged + exactly 1 suppressed; CapJustReached once.
            log.Begin();
            int trues = 0, capJust = 0;
            for (int i = 0; i < 41; i++)
            {
                if (log.Offer(i % 2 == 0 ? GladeHoverKind.Terrain : GladeHoverKind.Foreign, i % 2, 0))
                    trues++;
                if (log.CapJustReached)
                    capJust++;
            }
            Check(trues == 40 && log.Logged == 40 && log.Suppressed == 1, w, "41 alternating offers: " + trues + " logged, suppressed " + log.Suppressed);
            Check(capJust == 1, w, "CapJustReached true " + capJust + " times (expected once, on the first suppressed offer)");
            for (int i = 0; i < 10; i++)
            {
                log.Offer(i % 2 == 0 ? GladeHoverKind.Held : GladeHoverKind.Nothing, 0, 0);
                if (log.CapJustReached)
                    capJust++;
            }
            log.Offer(GladeHoverKind.Nothing, 0, 0); // duplicate: not a transition
            Check(log.Logged == 40 && log.Suppressed == 11 && capJust == 1, w, "after the cap: logged " + log.Logged + ", suppressed " + log.Suppressed + ", capJust " + capJust);
            log.Begin();
            Check(log.Logged == 0 && log.Suppressed == 0 && log.Offer(GladeHoverKind.Nothing, 0, 0), w, "Begin did not reset the cap / dedup state");

            // ---- dwell buckets sum to the total; garbage deltas ignored.
            log.Begin();
            System.Random r = new System.Random(99);
            double fed = 0.0;
            GladeHoverKind[] kinds = (GladeHoverKind[])Enum.GetValues(typeof(GladeHoverKind));
            GladePlanState[] states = (GladePlanState[])Enum.GetValues(typeof(GladePlanState));
            for (int i = 0; i < 20000; i++)
            {
                GladeHoverKind k = kinds[r.Next(kinds.Length)];
                GladePlanState s = states[r.Next(states.Length)];
                float dt;
                switch (i % 97)
                {
                    case 0: dt = float.NaN; break;
                    case 1: dt = -0.016f; break;
                    case 2: dt = float.PositiveInfinity; break;
                    case 3: dt = 0f; break;
                    default: dt = (float)(r.NextDouble() * 0.05); break;
                }
                log.AddDwell(k, s, dt);
                if (dt > 0f && !float.IsInfinity(dt))
                    fed += dt;
            }
            double bucketSum = 0.0;
            for (int b = 0; b < GladeHoverLog.BucketCount; b++)
                bucketSum += log.Dwell(b);
            Check(Math.Abs(log.TotalSecondsExact - fed) <= 1e-9 * Math.Max(1.0, fed), w, "total " + log.TotalSecondsExact + " vs fed " + fed);
            Check(Math.Abs(bucketSum - log.TotalSecondsExact) <= 1e-9 * Math.Max(1.0, fed), w, "Σ buckets " + bucketSum + " vs total " + log.TotalSecondsExact);
            Check(Math.Abs(log.TotalSeconds - fed) <= 1e-3 * Math.Max(1.0, fed), w, "float TotalSeconds " + log.TotalSeconds + " vs fed " + fed);
            // The printed buckets add up to the printed total (each rounded to 0.1 s).
            string sum = log.Summary("x");
            double printed = 0.0;
            int printedBuckets = 0;
            int dw = sum.IndexOf("dwell: ", StringComparison.Ordinal);
            int end = sum.IndexOf(';', dw);
            foreach (string part in sum.Substring(dw + 7, end - dw - 7).Split(','))
            {
                string[] tok = part.Trim().Split(' ');
                printed += double.Parse(tok[tok.Length - 2], CultureInfo.InvariantCulture);
                printedBuckets++;
            }
            int tsStart = "tool stopped after ".Length;
            double printedTotal = double.Parse(sum.Substring(tsStart, sum.IndexOf(" s —", StringComparison.Ordinal) - tsStart), CultureInfo.InvariantCulture);
            Check(Math.Abs(printed - printedTotal) <= 0.05 * printedBuckets + 0.051, w, "printed buckets " + printed + " vs printed total " + printedTotal);

            // ---- bucket mapping.
            Check(GladeHoverLog.BucketName(GladeHoverLog.BucketOf(GladeHoverKind.Terrain, GladePlanState.Complete)) == "terrain" &&
                  GladeHoverLog.BucketName(GladeHoverLog.BucketOf(GladeHoverKind.Bound, GladePlanState.Complete)) == "complete" &&
                  GladeHoverLog.BucketName(GladeHoverLog.BucketOf(GladeHoverKind.Bound, GladePlanState.Previewing)) == "previewing" &&
                  GladeHoverLog.BucketName(GladeHoverLog.BucketOf(GladeHoverKind.Bound, GladePlanState.Blocked)) == "blocked" &&
                  GladeHoverLog.BucketName(GladeHoverLog.BucketOf(GladeHoverKind.Held, GladePlanState.Previewing)) == "held" &&
                  GladeHoverLog.BucketName(GladeHoverLog.BucketOf(GladeHoverKind.Unbound, GladePlanState.None)) == "unbound",
                w, "bucket mapping");

            // ---- summary format, exact (0-plan case).
            log.Begin();
            log.AddDwell(GladeHoverKind.Terrain, GladePlanState.None, 1.25f);
            log.AddDwell(GladeHoverKind.Bound, GladePlanState.Previewing, 2.0f);
            log.Offer(GladeHoverKind.Terrain, 0, 0);
            string exp0 = "tool stopped after 3.3 s — 0 plan(s) (no bound run previewed), 0 apply(s); dwell: terrain 1.3 s, " +
                          "foreign 0.0 s, unbound 0.0 s, held 0.0 s, nothing 0.0 s, previewing 2.0 s, complete 0.0 s, blocked 0.0 s; " +
                          "1 transition(s) logged, 0 suppressed, 0 via point test; last: hover terrain";
            string got0 = log.Summary("hover terrain");
            Check(got0 == exp0, w, "0-plan summary\n  got:      " + got0 + "\n  expected: " + exp0);
            // with plans, applies, via-point hits and extra buckets
            log.Plans = 3;
            log.Applies = 1;
            log.ViaPoint = 2;
            log.AddDwell(GladeHoverKind.Bound, GladePlanState.MoneyShort, 0.5f);
            log.AddDwell(GladeHoverKind.Legacy, GladePlanState.None, 0.25f);
            string got1 = log.Summary(null);
            Check(got1.Contains("— 3 plan(s), 1 apply(s); ") && got1.Contains(", money short 0.5 s") && got1.Contains(", legacy 0.3 s") &&
                  got1.Contains("2 via point test; last: -") && got1.StartsWith("tool stopped after 4.0 s", StringComparison.Ordinal) &&
                  !got1.Contains("switching"), w, "summary with plans: " + got1);

            // ---- per-frame calls allocate nothing.
            log.Begin();
            log.Offer(GladeHoverKind.Terrain, 1, 0);
            log.AddDwell(GladeHoverKind.Terrain, GladePlanState.None, 0.01f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100000; i++)
            {
                log.Offer((GladeHoverKind)(i % 9), i & 7, (byte)(i & 3));
                log.AddDwell((GladeHoverKind)(i % 9), (GladePlanState)(i % 7), 0.016f);
            }
            long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(alloc == 0, w, "Offer/AddDwell allocated " + alloc + " bytes over 100,000 frames");
            Console.WriteLine("INFO  hoverlog: dwell/summary/cap/dedup checks done; per-frame allocation " + alloc + " bytes");
        }
    }
}
