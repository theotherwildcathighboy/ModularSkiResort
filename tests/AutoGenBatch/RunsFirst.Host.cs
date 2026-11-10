// RunsFirst host: the test host's side of the runs-first planner (mod/SkiResortMod/Systems/RunsFirstPlanner*.cs). The modes
// "rf" (plan, v2 side by side, score, render), "rf-golden" (plans and hashes only) and "rf-holdout", the regions files, the options from
// the RF_* environment switches, and PlanRegion (the call the golden test and other host drivers use). Run from the repo root:
//   dotnet run --project tests/AutoGenBatch/AutoGenBatch.csproj -c Release -- rf <out-dir> [regions file]
// The render, the tables and the debug prints that read the planner's own types are in RunsFirst.Render.cs.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RunsFirst
    {
        // the field test 5 regions, corners as the mod log printed them (docs/research/field/2026-10-28/ft5/logs)
        private static readonly (string Id, float2[] Ring)[] Regions =
        {
            ("ft5-r1", new[] { P(3372, -1943), P(3462, -3193), P(6386, -2246), P(6244, -1083) }),
            ("ft5-r2", new[] { P(2688, -1711), P(2768, -2269), P(2717, -2714), P(3317, -3708), P(3593, -3839), P(6312, -2292), P(7148, -1835),
                P(6951, -983), P(5793, -1107), P(4905, -1124), P(4047, -1423), P(3703, -1716), P(3358, -1858) }),
            ("ft5-r3", new[] { P(6276, -966), P(5976, 559), P(5020, 1226), P(2076, -686), P(3790, -756), P(4412, -411), P(4942, -362), P(5651, -1175) }),
            ("ft5-r4", new[] { P(-2941, -4929), P(-978, -5499), P(-339, -4233), P(-1945, -3261), P(-2580, -3672) }),
        };

        private static float2 P(float x, float z) => new float2(x, z);

        /// <summary>Host only: the options from the RF_* environment switches (unset or unreadable: the default). The planner never reads
        /// the environment itself.</summary>
        internal static RunsFirstOptions HostOptions()
        {
            var o = new RunsFirstOptions();
            o.PicksPerKm2 = Env("RF_PPK", 5f);
            o.MixMode = (int)Env("RF_MIXW", 0f);
            o.MixGreenMin = Env("RF_MIXGREEN", 1.5f);
            o.TopLatticeM = Env("RF_LAT", 100f);
            o.ReturnM = Env("RF_RET", 150f);
            o.ReachBudget = Env("RF_RBUDGET", 15e6f);
            o.PadReachM = Env("RF_PADR", 70f);
            o.ScanThreads = Math.Max(1, (int)Env("RF_THREADS", 4f));
            o.TypeUse = (int)Env("RF_TYPEUSE", 2f);
            o.ChairMaxRise = Env("RF_CHAIRRISE", 700f);
            o.VeryLongM = Env("RF_VERYLONG", 2500f);
            o.TramRise = Env("RF_TRAMRISE", 800f);
            o.GondolaCap = Env("RF_GONDCAP", 0.6f);
            o.LiftFallW = Env("RF_LIFTFALL", 0f);
            o.LiftsPerKm2 = Env("RF_LPK", 1.1f);
            o.StopShare = Env("RF_STOP", 0.05f);
            o.EdgeLiftM = Env("RF_EDGELIFT", 45f);
            o.TransferBeside = (int)Env("RF_TRANSFERBESIDE", 1f);
            o.OwnBottomTries = (int)Env("RF_OWNBOTTOMTRIES", 8f);
            o.MaxRiseM = Env("RF_MAXRISE", 0f);
            o.CoverLifts = Environment.GetEnvironmentVariable("RF_NOCOVERLIFT") != "1";
            o.Below = Environment.GetEnvironmentVariable("RF_NOBELOW") != "1";
            o.Spread = Environment.GetEnvironmentVariable("RF_NOSPREAD") != "1";
            o.SpreadAxis = Environment.GetEnvironmentVariable("RF_NOSPREADAXIS") != "1";
            o.SpreadEach = Environment.GetEnvironmentVariable("RF_NOSPREADEACH") != "1";
            o.NoParallel = Environment.GetEnvironmentVariable("RF_PARALLELOK") != "1";
            o.OwnBottom = Environment.GetEnvironmentVariable("RF_OWNBOTTOM") != "0";
            o.Village = Environment.GetEnvironmentVariable("RF_NOVILLAGE") != "1";
            o.CoverNewBase = Environment.GetEnvironmentVariable("RF_CNEWBASE") == "1";
            o.CoverLiftShare = Env("RF_CLS", 0.015f);
            o.CoverLiftMinPts = (int)Env("RF_CLM", 120f);
            o.CoverParallelM = Env("RF_CPAR", 170f);
            o.CoverClearM = Env("RF_CCLR", 100f);
            o.CoverFallW = Env("RF_COVERFALL", 0.8f);
            o.CoverOwnPads = Environment.GetEnvironmentVariable("RF_COVEROWNPAD") != "0";
            o.PodHold = (int)Env("RF_PODHOLD", 2f);
            o.ServeCapPerKm2 = Env("RF_SCAP", 8f);
            o.RunOutM = Env("RF_RUNOUT", 1600f);
            o.ChordDefer = Env("RF_CDEF", 0.35f);
            o.ChordKeep = Env("RF_CKEEP", 0.5f);
            o.Spurs = Environment.GetEnvironmentVariable("RF_NOSPUR") != "1";
            o.Pods = Environment.GetEnvironmentVariable("RF_NOPOD") != "1";
            o.Bridges = Environment.GetEnvironmentVariable("RF_NOBRIDGE") != "1";
            o.LearningPods = Environment.GetEnvironmentVariable("RF_LEARNPOD") == "1";
            o.WayHome = Environment.GetEnvironmentVariable("RF_NOWAYHOME") != "1";
            o.Downloads = Environment.GetEnvironmentVariable("RF_NODOWNLOAD") != "1";
            o.DropStranded = Environment.GetEnvironmentVariable("RF_NODROP") != "1";
            o.Regrade = Environment.GetEnvironmentVariable("RF_NOREGRADE") != "1";
            o.GradedWays = Environment.GetEnvironmentVariable("RF_NOGRADEDWAY") != "1";
            o.Fill = Environment.GetEnvironmentVariable("RF_NOFILL") != "1";
            o.Chords = Environment.GetEnvironmentVariable("RF_NOCHORD") != "1";
            o.Nudge = Environment.GetEnvironmentVariable("RF_NONUDGE") != "1";
            o.ChordTracks = Environment.GetEnvironmentVariable("RF_NOCHORDTRACK") != "1";
            o.ChordDrop = Environment.GetEnvironmentVariable("RF_NOCHORDDROP") != "1";
            o.Merge = Environment.GetEnvironmentVariable("RF_NOMERGE") != "1";
            o.Prune = Environment.GetEnvironmentVariable("RF_NOPRUNE") != "1";
            o.Straight = Environment.GetEnvironmentVariable("RF_NOSTRAIGHT") != "1";
            o.Junctions = Environment.GetEnvironmentVariable("RF_JUNCTION") == "1";
            o.Heads = Environment.GetEnvironmentVariable("RF_HEAD") == "1";
            o.LearningAreas = Environment.GetEnvironmentVariable("RF_NOLEARN") != "1";
            o.NudgeMaxM = Env("RF_NUDGEM", 350f);
            o.BridgeFallW = Env("RF_BRIDGEFALL", 2f);
            o.GradedLinks = Environment.GetEnvironmentVariable("RF_NOGRADEDLINK") != "1";
            o.HeadForwardM = Env("RF_HFWD", 60f);
            o.JunctionBackM = Env("RF_JBACK", 60f);
            o.EasyUpTol = Env("RF_UPTOL", 0.10f);
            o.EasyBudget = Env("RF_EBUDGET", 3.0e6f);
            o.LinkBudget = Env("RF_LBUDGET", 1.8e6f);
            o.TrackTurnM = Env("RF_TURNM", 40f);
            o.LearnGround = Env("RF_LGROUND", 0.235f);
            o.LearnMinRise = Env("RF_LMINRISE", 32f);
            o.LearnMaxRise = Env("RF_LMAXRISE", 130f);
            o.LearnMinLen = Env("RF_LMINLEN", 180f);
            o.LearnRouteMax = Env("RF_LROUTE", 1500f);
            o.LearnCrowd = Env("RF_LCROWD", 0.45f);
            o.LearnFallDeg = Env("RF_LFALL", 35f);
            o.LearnHigh = Environment.GetEnvironmentVariable("RF_NOLEARNHIGH") != "1";
            o.LearnAbove = Environment.GetEnvironmentVariable("RF_LEARNABOVE") == "1";
            o.LearnConvert = Environment.GetEnvironmentVariable("RF_LEARNCONVERT") == "1";
            o.V3Home = Environment.GetEnvironmentVariable("RF_V3HOME") == "1";
            o.WayPeakOnly = Environment.GetEnvironmentVariable("RF_WAYPEAK") == "1";
            o.PeakWayAgain = Environment.GetEnvironmentVariable("RF_PEAKAGAIN") == "1";
            o.WayGreenFirst = Environment.GetEnvironmentVariable("RF_WAYGREEN1") == "1";
            o.FillMinM = Env("RF_FILLMIN", 130f);
            o.FillCrowdTrade = Env("RF_FTRADE", 0.25f);
            o.FaceLines = Environment.GetEnvironmentVariable("RF_NOFACE") != "1";
            o.Diagonals = Environment.GetEnvironmentVariable("RF_NODIAG") != "1";
            o.NaturalMode = (int)Env("RF_NATURAL", 2f);
            o.NaturalHostile = Env("RF_NATHOSTILE", 0.4f);
            o.StraightDevM = Env("RF_STRDEV", 30f);
            o.NoLoneBase = Environment.GetEnvironmentVariable("RF_LONEBASE") == "1";
            o.StackDegM = Env("RF_STACKDEG", 33f);
            o.StackTopMm = Env("RF_STACKTOP", 330f);
            o.StackBottomMm = Env("RF_STACKBOT", 280f);
            o.NoStacked = Environment.GetEnvironmentVariable("RF_STACKED") != "0";
            o.RegradeStretch = Env("RF_RGSTRETCH", 1.8f);
            o.RegradeMinGain = Env("RF_RGMIN", 0.005f);
            o.PruneMinGain = Env("RF_PRUNEMIN", 0.01f);
            o.Debug = Environment.GetEnvironmentVariable("RF_DEBUG") == "1";
            o.Timing = Environment.GetEnvironmentVariable("RF_TIME") == "1";
            o.DebugTops = Environment.GetEnvironmentVariable("RF_TOPDBG") == "1";
            o.DebugTwins = Environment.GetEnvironmentVariable("RF_TWINDBG") == "1";
            o.DebugTopList = Environment.GetEnvironmentVariable("RF_TOPS") == "1";
            o.DebugConvert = Environment.GetEnvironmentVariable("RF_CONVDBG") == "1";
            o.DebugV3All = Environment.GetEnvironmentVariable("RF_V3ALL") == "1";
            o.DebugWayHome = Environment.GetEnvironmentVariable("RF_WHDBG") == "1";
            o.DebugChords = Environment.GetEnvironmentVariable("RF_CHORDDBG") == "1";
            o.DebugModel = Environment.GetEnvironmentVariable("RF_MODELDBG") == "1";
            o.DebugCover = Environment.GetEnvironmentVariable("RF_COVDBG") == "1";
            o.DebugCoverMap = Environment.GetEnvironmentVariable("RF_COVER") == "1";
            o.DebugCrowd = Environment.GetEnvironmentVariable("RF_CROWD") == "1";
            o.DebugV3 = Environment.GetEnvironmentVariable("RF_V3DBG") == "1";
            o.DebugTurns = Environment.GetEnvironmentVariable("RF_TURNDBG") == "1";
            return o;
        }

        /// <summary>Experiment knob: an environment variable as a float (the default when unset).</summary>
        private static float Env(string name, float dflt) =>
            float.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : dflt;

        public static int Run(string[] args)
        {
            RunsFirstOptions opts = HostOptions();
            // "rf-holdout <file>": writes the real-resort regions (9 km2 around each real fixture base point) as a regions file
            if (args[0] == "rf-holdout")
            {
                var rows = new List<string>();
                foreach (string name in new[] { "JacksonHole", "Whistler", "Snowbird", "Zermatt", "Chamonix", "Okemo" })
                    foreach (V2Case c in V2Fixtures.Real(name, out _))
                        if (c.Id.Contains("base 9 km2"))
                            rows.Add(name.ToLowerInvariant() + " @" + name + " " + string.Join(" ", c.Region.Select(p =>
                                p.x.ToString("F0", CultureInfo.InvariantCulture) + "," + p.y.ToString("F0", CultureInfo.InvariantCulture))));
                File.WriteAllLines(args[1], rows);
                Console.WriteLine($"wrote {rows.Count} holdout region(s) to {args[1]}");
                return 0;
            }
            // "rf-golden <out file> [regions file]": plans only (no v2, render or score) and writes "<region> <hash> <runs> <lifts> <connectors>"
            bool golden = args[0] == "rf-golden";
            string outDir = args.Length > 1 ? args[1] : golden ? "rf-golden.txt" : "rf-out";
            if (!golden)
                Directory.CreateDirectory(outDir);
            if (!Heightmap.TryOpen("MountainVillage", out HeightmapFile file, out string why))
            {
                Console.WriteLine("SKIP RunsFirst: " + why);
                return 0;
            }
            // a regions file (tools/regions-from-log.js: "<id> x,z x,z ...") replaces the built-in field test 5 list
            var regions = new List<(string, float2[])>(Regions);
            if (args.Length > 2)
            {
                regions.Clear();
                foreach (string row in File.ReadAllLines(args[2]))
                {
                    var t = new List<string>(row.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                    // optional second token "@Fixture" (default MountainVillage)
                    string fx = t.Count > 1 && t[1].StartsWith("@") ? t[1].Substring(1) : "MountainVillage";
                    if (t.Count > 1 && t[1].StartsWith("@"))
                        t.RemoveAt(1);
                    if (t.Count < 4)
                        continue;
                    // "hole x,z x,z ..." after the outline cuts a no-go area out of the region (a cliff band). The ring becomes a
                    // keyhole (outline, a zero-width bridge to the hole, the hole reversed, back). Every even-odd inside
                    // test and the shoelace area already read a keyhole as outline minus hole
                    var parts = new List<List<float2>> { new List<float2>() };
                    for (int i = 1; i < t.Count; i++)
                    {
                        if (t[i] == "hole")
                        {
                            parts.Add(new List<float2>());
                            continue;
                        }
                        string[] xz = t[i].Split(',');
                        parts[parts.Count - 1].Add(new float2(float.Parse(xz[0], CultureInfo.InvariantCulture), float.Parse(xz[1], CultureInfo.InvariantCulture)));
                    }
                    var ring = new List<float2>(parts[0]);
                    double Signed(List<float2> r)
                    {
                        double a = 0.0;
                        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
                            a += (double)r[j].x * r[i].y - (double)r[i].x * r[j].y;
                        return a;
                    }
                    foreach (List<float2> hole in parts.Skip(1).Where(h => h.Count >= 3))
                    {
                        if (Math.Sign(Signed(hole)) == Math.Sign(Signed(parts[0])))
                            hole.Reverse();
                        ring.Add(parts[0][0]);
                        ring.AddRange(hole);
                        ring.Add(hole[0]);
                    }
                    regions.Add((fx + "|" + t[0], ring.ToArray()));
                }
            }
            var files = new Dictionary<string, HeightmapFile> { ["MountainVillage"] = file };
            var goldenRows = new List<string>();
            foreach (var (key, ring) in regions)
            {
                int bar = key.IndexOf('|');
                string fx = bar < 0 ? "MountainVillage" : key.Substring(0, bar), id = bar < 0 ? key : key.Substring(bar + 1);
                if (!files.TryGetValue(fx, out HeightmapFile hf))
                {
                    if (!Heightmap.TryOpen(fx, out hf, out string why2))
                    {
                        Console.WriteLine($"SKIP RunsFirst {id}: {why2}");
                        continue;
                    }
                    files[fx] = hf;
                }
                if (!golden)
                {
                    new RunsFirstPlanner { O = opts, Id = id }.RunRegion(id, ring, hf, outDir);
                    continue;
                }
                HeightSnapshot snap = Heightmap.Snapshot(hf, ring);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                RunsFirstResult res = PlanRegion(snap, ring, opts, id);
                string row = $"{id} {res.Hash():x16} {res.Runs.Count} {res.Lifts.Count} {res.Connectors.Count}";
                goldenRows.Add(row);
                Console.WriteLine($"GOLDEN {row} ({sw.ElapsedMilliseconds} ms)");
            }
            if (golden)
                File.WriteAllLines(outDir, goldenRows);
            return 0;
        }

        /// <summary>Plans one region with the given options (null: the defaults) and returns the plain plan object. The options hold
        /// for this call only.</summary>
        internal static RunsFirstResult PlanRegion(HeightSnapshot snap, float2[] ring, RunsFirstOptions options, string id = "region") =>
            new RunsFirstPlanner { Id = id }.Plan(snap, ring, options);
    }
}
