// Quick runs tool: one scan as a pure job (port round R2-a, plan 7.4). The runs-first planner (RunsFirstPlanner) and the realizer
// (RunsFirstRealizer) run in this one call, on the tool's worker thread (QuickRunsToolSystem.Worker.cs). The test host runs the same
// class (WorkerTests, the rf-scan and rf-replay modes), so what the host measures is what the tool runs.
// Order: the refusal ladder first (the realizer with no plan: area, relief, blocked share, a few hundred ms), so a refused region is
// never planned. Then the plan (or the cached plan: the tool's last two plans, keyed by snapshot, region, obstacles, lifts, access and
// options, so a Kinds change or a density switched back only realizes again), then the realizer.
// Progress (0 to 1) and cancel go through the planner's own: one cancel token for both, a canceled job has Canceled set and no result.
// The job owns its result: no later job writes it, so the tool publishes it as it is.
// Inputs from the game (InputsFrom, plan 7.5): other areas, unowned tiles and existing runs leave the planner's mask with the clearance
// the realizer uses, water leaves it, each existing lift leaves its stations and a corridor along its line (the planner has no fixed
// lift: it plans around them), existing cat tracks stay passable. "Bare" leaves every input out (the dev trigger's cross-runtime
// check against the host's golden, which plans with no input).
// Pure: System, Unity.Mathematics and the mod's pure planner files. No clock in any decision (the times are a report).
// Engine notes: EngineNotes/ResortPlanner.md (Runs-first in the tool)

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed class QuickRunsScanJob
    {
        /// <summary>Half width of the blocked corridor along an existing lift's line (m): the towers and the cable stay clear of new
        /// stations, lift ends and runs that would follow the line.</summary>
        public const float ExistingLiftCorridorM = 10f;
        /// <summary>Share of the progress bar the planner takes. The realizer's share follows (it reports no progress of its own).</summary>
        public const float PlanShare = 0.85f;

        /// <summary>A finished plan the tool may realize again without planning (its cache entry).</summary>
        public sealed class Planned
        {
            public RunsFirstResult Plan;
            public ulong Hash;
            public (string Pass, long Ms)[] PassTimes;
            public double PlanMs;
            public int BlockedCells, OpenCells;
        }

        public readonly AutoPlanInput Input;
        public readonly RunsFirstOptions Options;
        public readonly RunsFirstInputs PlannerInputs;
        public readonly string Id;
        private readonly Planned m_Cached;

        // ---- the outputs (read after Run returns)
        public AutoPlanResult Result;
        public Planned Plan;
        public readonly RealizeReport Report = new RealizeReport();
        public bool FromCache, Canceled, Refused;
        public double PrecheckMs, RealizeMs, TotalMs;

        private volatile RunsFirstPlanner m_Planner;
        private volatile int m_Stage;   // 0 checks, 1 planning, 2 realizing, 3 done

        /// <param name="cached">A plan for exactly this input (the tool's cache), or null: plan.</param>
        public QuickRunsScanJob(AutoPlanInput input, RunsFirstOptions options, RunsFirstInputs plannerInputs, Planned cached, string id = "scan")
        {
            Input = input;
            Options = options ?? new RunsFirstOptions();
            PlannerInputs = plannerInputs;
            m_Cached = cached;
            Id = id;
        }

        /// <summary>0 to 1, safe to read from any thread while Run works. It never goes back.</summary>
        public float Progress
        {
            get
            {
                switch (m_Stage)
                {
                    case 0: return 0f;
                    case 1:
                        RunsFirstPlanner p = m_Planner;
                        return p == null ? 0f : PlanShare * p.ProgressVolatile / 1000f;
                    case 2: return PlanShare + 0.5f * (1f - PlanShare);
                    default: return 1f;
                }
            }
        }

        /// <summary>The scan. Never throws for a refused or canceled input (a fault is a bug: the tool's Fail policy).</summary>
        public void Run()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            AutoPlanCancel cancel = Input.Cancel;
            // 1. The refusal ladder: the realizer with no plan answers BadInput when nothing else refuses the region
            if (Input.Snapshot == null || !Input.Snapshot.Valid || Input.Region == null || Input.Region.Length < 3)
            {
                Result = new AutoPlanResult { Status = AutoPlanStatus.BadInput };
                Refused = true;
                Finish(t0);
                return;
            }
            AutoPlanResult pre = RunsFirstRealizer.Realize(null, Input, null);
            PrecheckMs = Ms(t0);
            if (pre.Status != AutoPlanStatus.BadInput)
            {
                Result = pre;
                Refused = true;
                Finish(t0);
                return;
            }
            if (cancel != null && cancel.Requested)
            {
                Canceled = true;
                Finish(t0);
                return;
            }
            // 2. The plan (or the cached one)
            if (m_Cached != null)
            {
                Plan = m_Cached;
                FromCache = true;
            }
            else
            {
                var planner = new RunsFirstPlanner { Id = Id, Inputs = PlannerInputs };
                m_Planner = planner;
                m_Stage = 1;
                long tp = System.Diagnostics.Stopwatch.GetTimestamp();
                RunsFirstResult plan = planner.Plan(Input.Snapshot, Input.Region, Options, cancel);
                double planMs = Ms(tp);
                if (plan == null)
                {
                    Canceled = true;
                    Finish(t0);
                    return;
                }
                IReadOnlyList<(string Pass, long Ms)> times = planner.PassTimes;
                var copy = new (string, long)[times.Count];
                for (int i = 0; i < copy.Length; i++)
                    copy[i] = times[i];
                Plan = new Planned
                {
                    Plan = plan, Hash = plan.Hash(), PassTimes = copy, PlanMs = planMs, BlockedCells = planner.BlockedCells, OpenCells = planner.OpenCells,
                };
            }
            // 3. The realizer
            m_Stage = 2;
            long tr = System.Diagnostics.Stopwatch.GetTimestamp();
            AutoPlanResult res = RunsFirstRealizer.Realize(Plan.Plan, Input, Report);
            RealizeMs = Ms(tr);
            if (res.Canceled || (cancel != null && cancel.Requested))
                Canceled = true;
            else
                Result = res;
            Finish(t0);
        }

        private void Finish(long t0)
        {
            TotalMs = Ms(t0);
            m_Stage = 3;
        }

        private static double Ms(long t0) => (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>"grid 120, flow 80, ..." of the plan's passes (ms each), or "cached".</summary>
        public string PassText()
        {
            if (Plan == null)
                return "none";
            if (FromCache)
                return "cached";
            var sb = new System.Text.StringBuilder();
            foreach (var (pass, ms) in Plan.PassTimes)
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(pass).Append(' ').Append(ms);
            }
            return sb.ToString();
        }

        /// <summary>"planner [grid=89abcdef ...] realizer [lattice=...]": the low 32 bits of each pass print and stage print (port round
        /// 2b-f), the scan's log line. rf-replay compares the full prints from a dump.</summary>
        public string PrintsText() =>
            "planner [" + RunsFirstPrint.Text(Plan?.Plan.Passes) + "] realizer [" + RunsFirstPrint.Text(Report.Stages) + "]";

        // ---------------------------------------------------------------- the planner's inputs from the tool's input

        /// <summary>What the planner must leave alone (plan 7.5), from the tool's input. bare = no input (the host's golden plans with none).
        /// Obstacles: Surface areas with SurfaceClearanceM, unowned tiles with UnownedClearanceM, existing runs with the density's gap
        /// (PlanGeometry.ClearanceOf, the realizer's clearances), cat tracks none (passable). Water: the snapshot's wet layer. Existing lifts:
        /// each station spot (its station's half diagonal) and each chord (ExistingLiftCorridorM).</summary>
        public static RunsFirstInputs InputsFrom(AutoPlanInput input, bool bare)
        {
            if (bare || input == null)
                return null;
            var inp = new RunsFirstInputs { Water = input.Snapshot != null && input.Snapshot.Wet != null, Access = input.Access };
            if (input.Obstacles != null)
                foreach (AutoObstacle ob in input.Obstacles)
                {
                    if (ob?.Ring == null || ob.Ring.Length == 0 || ob.Kind == AutoObstacleKind.CatTrack)
                        continue;
                    inp.Blocked.Add(new RunsFirstInputs.Shape { Pts = ob.Ring, ClearM = PlanGeometry.ClearanceOf(ob.Kind, input.Knobs.Density) });
                }
            if (input.Pinned != null)
                foreach (PinnedLift lift in input.Pinned)
                {
                    float station = PlanGeometry.StationHalfDiagonalOf(lift.TypeIndex);
                    if (math.all(math.isfinite(lift.Bottom)))
                        inp.Blocked.Add(new RunsFirstInputs.Shape { Pts = new[] { lift.Bottom.xz }, ClearM = station });
                    if (math.all(math.isfinite(lift.Top)))
                        inp.Blocked.Add(new RunsFirstInputs.Shape { Pts = new[] { lift.Top.xz }, ClearM = station });
                    if (lift.Chords != null)
                        for (int c = 0; c + 1 < lift.Chords.Length; c += 2)
                            inp.Blocked.Add(new RunsFirstInputs.Shape { Pts = new[] { lift.Chords[c], lift.Chords[c + 1] }, ClearM = ExistingLiftCorridorM });
                }
            return inp;
        }

        /// <summary>The inputs in one clause for the log: "inputs: 12 shape(s) (3 area, 2 tile, 4 run, 1 lift), water on, 40 access point(s)".</summary>
        public static string InputsText(AutoPlanInput input, RunsFirstInputs inp)
        {
            if (inp == null)
                return "inputs: none (bare)";
            int areas = 0, tiles = 0, runs = 0;
            if (input?.Obstacles != null)
                foreach (AutoObstacle ob in input.Obstacles)
                {
                    if (ob?.Ring == null || ob.Ring.Length == 0)
                        continue;
                    if (ob.Kind == AutoObstacleKind.Surface)
                        areas++;
                    else if (ob.Kind == AutoObstacleKind.Unowned)
                        tiles++;
                    else if (ob.Kind == AutoObstacleKind.ExistingRun)
                        runs++;
                }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "inputs: {0} blocked shape(s) ({1} area, {2} unowned tile, {3} run, {4} existing lift), water {5}, {6} access point(s)",
                inp.Blocked.Count, areas, tiles, runs, input?.Pinned?.Count ?? 0, inp.Water ? "on" : "off", inp.Access?.Length ?? 0);
        }

        // ---------------------------------------------------------------- the cache key

        /// <summary>FNV-1a 64 over what decides the plan: the snapshot print, the region, the obstacle print, the existing lifts, the
        /// access points, the options and the bare flag. The snapshot and obstacle prints are the tool's (heights and water, obstacle rings).</summary>
        public static ulong PlanKey(ulong snapPrint, ulong obstaclePrint, float2[] region, List<PinnedLift> pinned, float2[] access,
            RunsFirstOptions options, bool bare)
        {
            ulong h = 14695981039346656037UL;
            void U(uint v)
            {
                for (int b = 0; b < 4; b++)
                {
                    h ^= (v >> (8 * b)) & 0xFFu;
                    h *= 1099511628211UL;
                }
            }
            void L(ulong v)
            {
                U((uint)v);
                U((uint)(v >> 32));
            }
            void P(float2 p)
            {
                U(math.asuint(p.x));
                U(math.asuint(p.y));
            }
            L(snapPrint);
            L(obstaclePrint);
            U((uint)(region?.Length ?? 0));
            if (region != null)
                foreach (float2 p in region)
                    P(p);
            U((uint)(pinned?.Count ?? 0));
            if (pinned != null)
                foreach (PinnedLift l in pinned)
                {
                    U(l.Key);
                    U(l.TypeIndex);
                    P(l.Bottom.xz);
                    P(l.Top.xz);
                    U((uint)(l.Chords?.Length ?? 0));
                    if (l.Chords != null)
                        foreach (float2 p in l.Chords)
                            P(p);
                }
            U((uint)(access?.Length ?? 0));
            if (access != null)
                foreach (float2 p in access)
                    P(p);
            L(options?.Fingerprint() ?? 0UL);
            U(bare ? 1u : 0u);
            return h;
        }
    }
}
