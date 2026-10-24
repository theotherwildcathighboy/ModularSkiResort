// ApplySim - the apply-sequence harness for the glade tool's compose / apply loop. A test-local world of trees (wild, foreign, and
// this run's members tagged {layer, variant, planGen}) plus one run polygon per fixture; ApplyOnce mirrors the tool's apply loop
// (oracle census -> PlanKey -> skip set -> Compose -> stand-in validator -> mutate the world).
// Every compose is judged by an independent recount (CheckComposeOracles) and every apply on world state, never by the planner's own
// arithmetic. Pinned: an apply never plants AND removes in one layer; members go before wild trees; the felled set is the exact
// ThinHash rank top-k and nested across levels; level 1 leaves no member of this run in the census; re-applying is a no-op.
// Engine notes: EngineNotes/GladeBatch.ApplySim.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace GladeBatch
{
    internal static partial class Program
    {
        // SkiGladeToolSystem.Tuning values. The tool needs Game.dll and cannot be
        // linked; these hand mirrors only build realistic inputs.
        private const float TGladeMinSpacing = 5.5f;
        private const float TInnerMargin = 2.5f;
        private const float TBeltGap = 3.1f;
        private const float TBeltDepth = 10f;
        private const float TBeltDensity = 1f / 40f;
        private const float TBeltCoverage = 0.55f;
        private const float TBeltNoiseWavelength = 40f;
        private const float TBeltMinSpacing = 4.5f;
        private const float TDominantShare = 0.7f;
        private const int TAttemptsPerTree = 60;
        private const float TVertexPropRadius = 3f;
        private const int TCap = 4000;
        private const int OurRun = 1, OtherRun = 2;
        private const int WalkSteps = 200;

        // The documented table, typed in (NOT read from SkiGladeCompose): trees per hectare and
        // the tree-line factors; the level-8 density; the stream density (1.10 × level 8 = 157.3 per ha);
        // the documented ThinHash salt.
        private static readonly double[] OracleDensityPerHa = { 0, 6, 14, 28, 45, 70, 100, 143 };
        private static readonly double[] OracleBeltFactor = { 0, 0.15, 0.30, 0.45, 0.60, 0.75, 0.90, 1.00 };
        private const float OracleD8 = 143f / 10000f;
        private const float OracleStreamD = 157.3f / 10000f;
        private const uint OracleSaltThin = 0x7A1B3C5Du;

        /// <summary>Stand-in validator: a planted tree this close (xz, m) to a tree that still stands after the apply is canceled
        /// (vanilla tree collision bounds are unverified; 4 m is below both MinSpacings, so a stream slot never trips it: only plants
        /// next to off-stream members after an edit / a new occupant do).</summary>
        private const double StandInCollisionM = 4.0;

        internal enum SimTarget
        {
            Interior,
            TreeLine,
        }

        internal enum TreeKind
        {
            Wild,
            Foreign,
            Member,
        }

        internal struct WTree
        {
            public int Id;
            public float3 Pos;
            public TreeKind Kind;
            public int Run;
            public byte Layer, Variant, PlanGen;
        }

        internal sealed class SimRun
        {
            public string Name;
            public float3[] Ring;
            public double2[] Oracle;
            public double OracleArea, OraclePerimeter;
            public GladeObstacle[] Obstacles;
            public float3[][] Exclusions;
            public double2 StripA, StripB;
            public double StripHalf;
            public int WildInside, WildBand, ForeignInside;
            public double2 Min, Max;

            public bool Blocks(float3 p)
            {
                return SegDist(new double2(p.x, p.z), StripA, StripB) < StripHalf;
            }
        }

        internal sealed class World
        {
            public readonly List<WTree> Trees = new List<WTree>();
            public int NextId = 1;
            public readonly Dictionary<ulong, HashSet<int>> Skip = new Dictionary<ulong, HashSet<int>>();

            public World Clone()
            {
                World w = new World();
                w.Trees.AddRange(Trees);
                w.NextId = NextId;
                foreach (KeyValuePair<ulong, HashSet<int>> kv in Skip)
                    w.Skip[kv.Key] = new HashSet<int>(kv.Value);
                return w;
            }

            public int Add(float3 pos, TreeKind kind, int run, byte layer, byte variant, byte gen)
            {
                int id = NextId++;
                Trees.Add(new WTree { Id = id, Pos = pos, Kind = kind, Run = run, Layer = layer, Variant = variant, PlanGen = gen });
                return id;
            }

            public void RemoveIds(HashSet<int> ids)
            {
                if (ids.Count > 0)
                    Trees.RemoveAll(t => ids.Contains(t.Id));
            }
        }

        internal sealed class CensusOut
        {
            public float3[] Existing;
            public bool[] Thinnable;
            public int[] ExistingIds;
            public TreeKind[] ExistingKind;
            public ComposeMember[] Own;
            public int[] OwnIds;
        }

        internal sealed class ApplyReport
        {
            public ComposeResult R = new ComposeResult();
            public ulong Key;
            public int Planted, Cancelled, Felled, Removed, CancelledLive;
            public readonly List<float3> PlantedPos = new List<float3>();
            public readonly List<int> PlantedIds = new List<int>();
            public readonly HashSet<int> RemovedIds = new HashSet<int>();
            public readonly HashSet<int> FelledIds = new HashSet<int>();

            public bool NoOp => Planted == 0 && Cancelled == 0 && Felled == 0 && Removed == 0;
        }

        internal sealed class KeyBook
        {
            public readonly Dictionary<ulong, List<PlantedTree>> Plans = new Dictionary<ulong, List<PlantedTree>>();
            public long Hits;
        }

        internal sealed class OracleStreamEntry
        {
            public List<PlantedTree> Slots;
            public bool Saturated;
        }

        private sealed class SimAgg
        {
            public long Composes, Applies, Scenarios, WalkSteps, MatchChecks;
            public long PlantBranch, KeepRemoveBranch, FellBranch, FellWithRemove, OthersBranch;
            public long FullReplans, FullForMembers, CacheHits;
            public long Shortfalls, SaturatedComposes, SliceClampEdge;
            public long PlantSetChecks, PlantSetSkipped, UpDownChecks, UpDownHoleSkips, NestedChecks, KeyKeptChecks, KeyChangedNearEdge;
            public long LiveCancels, Cancelled;
            public long VerbatimChecked, VerbatimFalseComplete, VerbatimShort;
            public long TargetNearHalf;
            // Misplaced members seen by the oracle (summed over composes), by
            // [target, layer of the misplaced member]; level-1 composes with off-stream members,
            // by target; composes repeated into the long-lived result; edit-oor cases by kind;
            // near-end full plans.
            public readonly long[,] Misplaced = new long[2, 2];
            public readonly long[] L1OffStream = new long[2];
            public long SharedResultChecks, EditOorWiden, EditOorNarrow, EditOorBeyondInteriorBand;
            public long L8HeadroomUsed;   // interior L8 settles that reached T despite canceled spots
        }

        private static SimAgg s_Sim = new SimAgg();

        /// <summary>ONE result reused by every compose of the suite (the tool
        /// reuses m_Result the same way); compared field by field with the fresh-object compose.</summary>
        private static readonly ComposeResult s_Shared = new ComposeResult();
        private static readonly Dictionary<ulong, OracleStreamEntry> s_StreamMemo = new Dictionary<ulong, OracleStreamEntry>();


        private static double2 Xf(double2 local, double angleDeg, double2 offset)
        {
            double a = angleDeg * Math.PI / 180.0;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            return new double2(local.x * ca - local.y * sa + offset.x, local.x * sa + local.y * ca + offset.y);
        }

        private static SimRun MakeSimRun(string name, double2[] local, double angle, double2 offset, double2[][] exclLocal,
            double2 stripA, double2 stripB, int wildInside, int wildBand, int foreign)
        {
            SimRun r = new SimRun();
            r.Name = name;
            if (exclLocal != null)
            {
                r.Exclusions = new float3[exclLocal.Length][];
                for (int i = 0; i < exclLocal.Length; i++)
                    r.Exclusions[i] = MakeRing(exclLocal[i], angle, offset);
            }
            r.StripA = Xf(stripA, angle, offset);
            r.StripB = Xf(stripB, angle, offset);
            r.StripHalf = 6.0;
            r.WildInside = wildInside;
            r.WildBand = wildBand;
            r.ForeignInside = foreign;
            SetRing(r, MakeRing(local, angle, offset));
            return r;
        }

        private static void SetRing(SimRun r, float3[] ring)
        {
            r.Ring = ring;
            r.Oracle = ToXz(ring);
            r.OracleArea = Math.Abs(OracleShoelace(r.Oracle));
            r.OraclePerimeter = OraclePerimeter(r.Oracle);
            r.Obstacles = new GladeObstacle[ring.Length];
            double2 mn = new double2(double.MaxValue, double.MaxValue), mx = new double2(double.MinValue, double.MinValue);
            for (int i = 0; i < ring.Length; i++)
            {
                r.Obstacles[i] = new GladeObstacle { Center = ring[i], Radius = TVertexPropRadius };
                double2 v = new double2(ring[i].x, ring[i].z);
                mn = math.min(mn, v);
                mx = math.max(mx, v);
            }
            r.Min = mn;
            r.Max = mx;
        }

        private static SimRun WithRing(SimRun src, float3[] ring)
        {
            SimRun r = new SimRun
            {
                Name = src.Name,
                Exclusions = src.Exclusions,
                StripA = src.StripA,
                StripB = src.StripB,
                StripHalf = src.StripHalf,
                WildInside = src.WildInside,
                WildBand = src.WildBand,
                ForeignInside = src.ForeignInside,
            };
            SetRing(r, ring);
            return r;
        }

        private static List<SimRun> BuildSimRuns()
        {
            List<SimRun> runs = new List<SimRun>();
            // The field run: 200 × 1,420 m rotated 17°, 794 wild inside + 42 band.
            runs.Add(MakeSimRun("run284k", D2(0, 0, 200, 0, 200, 1420, 0, 1420), 17, new double2(-2500, 1800), null,
                new double2(-40, 568), new double2(240, 568), 794, 42, 6));
            runs.Add(MakeSimRun("run127k", D2(0, 0, 90, 0, 90, 1400, 0, 1400), -8, new double2(3100, -2200), null,
                new double2(-40, 770), new double2(130, 770), 360, 24, 4));
            // >= 60 % under an exclusion ring (another bound run): x ∈ [0, 195] of a 300 m square.
            runs.Add(MakeSimRun("saturated", D2(0, 0, 300, 0, 300, 300, 0, 300), 5, new double2(600, 600),
                new double2[][] { D2(-60, -60, 195, -60, 195, 360, -60, 360) },
                new double2(-40, 150), new double2(340, 150), 90, 20, 3));
            runs.Add(MakeSimRun("run800k", D2(0, 0, 400, 0, 400, 2000, 0, 2000), 33, new double2(-6000, -5000), null,
                new double2(-40, 600), new double2(440, 600), 1500, 60, 8));
            // Small, fast, non-convex: more seeds.
            runs.Add(MakeSimRun("L", D2(0, 0, 100, 0, 100, 40, 40, 40, 40, 150, 0, 150), 0, new double2(150, -90), null,
                new double2(-30, 20), new double2(130, 20), 30, 10, 2));
            runs.Add(MakeSimRun("rect100x300", D2(0, 0, 100, 0, 100, 300, 0, 300), 21, new double2(-300, 400), null,
                new double2(-30, 120), new double2(130, 120), 120, 15, 2));
            // Field-sized fixtures: 124,000 m² with 266 wild (21 / ha; a dogleg with a reflex vertex) and
            // 140,000 m² with 652 wild (47 / ha; a neighboring bound run 3 m away = one exclusion ring).
            runs.Add(MakeSimRun("run124k", D2(0, 0, 98, 0, 98, 700, 160, 1250, 76, 1305, 0, 722), 12, new double2(4200, 3100), null,
                new double2(-40, 400), new double2(140, 400), 266, 32, 2));
            runs.Add(MakeSimRun("run140k", D2(0, 0, 110, 0, 110, 1275, 0, 1275), -23, new double2(-3800, -1500),
                new double2[][] { D2(113, -50, 190, -50, 190, 1325, 113, 1325) },
                new double2(-40, 900), new double2(150, 900), 652, 31, 0));
            return runs;
        }

        private static World MakeWorld(SimRun run, int seed, bool bare = false)
        {
            World w = new World();
            System.Random r = new System.Random(StableHash(run.Name) ^ (seed * 7919 + 101));
            double2 mn = run.Min - 15.0, mx = run.Max + 15.0;
            int wantInside = bare ? 0 : run.WildInside;
            int inside = 0, band = 0, foreign = 0, tries = 0;
            while ((inside < wantInside || band < run.WildBand || foreign < run.ForeignInside) && tries < 4000000)
            {
                tries++;
                double x = mn.x + r.NextDouble() * (mx.x - mn.x);
                double z = mn.y + r.NextDouble() * (mx.y - mn.y);
                double2 q = new double2(x, z);
                float3 pos = new float3((float)x, GroundY(x, z), (float)z);
                if (OracleInside(run.Oracle, q))
                {
                    if (inside < wantInside)
                    {
                        w.Add(pos, TreeKind.Wild, 0, 0, 0, 0);
                        inside++;
                    }
                    else if (foreign < run.ForeignInside)
                    {
                        w.Add(pos, TreeKind.Foreign, OtherRun, 0, 0, SkiGladeCompose.PlanGeneration);
                        foreign++;
                    }
                }
                else if (band < run.WildBand && OracleBoundaryDistance(run.Oracle, q) <= 14.0)
                {
                    w.Add(pos, TreeKind.Wild, 0, 0, 0, 0);
                    band++;
                }
            }
            return w;
        }


        /// <summary>The tool's params for a target. Mode / ThinFraction / GladeDensity carry the
        /// old Glade-mode values on purpose: Compose must not read them (asserted in
        /// ComposeTests); MinSpacing is the target layer's.</summary>
        private static GladeParams SimParams(SimTarget target)
        {
            return new GladeParams
            {
                Mode = GladeMode.Thin | GladeMode.Glade,
                ThinFraction = 0.6f,
                GladeDensity = 1f / 70f,
                BeltDensity = TBeltDensity,
                MinSpacing = target == SimTarget.Interior ? TGladeMinSpacing : TBeltMinSpacing,
                InnerMargin = TInnerMargin,
                BeltGap = TBeltGap,
                BeltDepth = TBeltDepth,
                BeltCoverage = TBeltCoverage,
                BeltNoiseWavelength = TBeltNoiseWavelength,
                SpeciesCount = 3,
                DominantShare = TDominantShare,
                AgeWeights = new float4(0f, 0.25f, 0.75f, 0f),
                MaxPlant = 300,
                AttemptsPerTree = TAttemptsPerTree,
            };
        }

        private static ComposeCaps Caps(int plant, int fell = TCap, int remove = TCap)
        {
            return new ComposeCaps { MaxPlant = plant, MaxFell = fell, MaxRemove = remove };
        }

        private static byte LayerOf(SimTarget t)
        {
            return t == SimTarget.Interior ? (byte)0 : (byte)1;
        }

        private static SimTarget Other(SimTarget t)
        {
            return t == SimTarget.Interior ? SimTarget.TreeLine : SimTarget.Interior;
        }

        private static bool IsCurrent(byte variant, byte gen)
        {
            return variant == 0 && gen == SkiGladeCompose.PlanGeneration;
        }

        private static CensusOut Census(World w, SimRun run, SimTarget target)
        {
            double band = target == SimTarget.Interior ? TGladeMinSpacing : TBeltDepth + TBeltMinSpacing;
            List<WTree> ex = new List<WTree>();
            List<WTree> own = new List<WTree>();
            foreach (WTree t in w.Trees)
            {
                double2 q = new double2(t.Pos.x, t.Pos.z);
                if (!OracleInside(run.Oracle, q) && OracleBoundaryDistance(run.Oracle, q) > band)
                    continue;
                if (t.Kind == TreeKind.Member && t.Run == OurRun)
                    own.Add(t);
                else
                    ex.Add(t);
            }
            ex.Sort((a, b) =>
            {
                int c = a.Pos.x.CompareTo(b.Pos.x);
                if (c != 0) return c;
                c = a.Pos.z.CompareTo(b.Pos.z);
                if (c != 0) return c;
                return a.Id.CompareTo(b.Id);
            });
            CensusOut o = new CensusOut();
            o.Existing = new float3[ex.Count];
            o.Thinnable = new bool[ex.Count];
            o.ExistingIds = new int[ex.Count];
            o.ExistingKind = new TreeKind[ex.Count];
            for (int i = 0; i < ex.Count; i++)
            {
                o.Existing[i] = ex[i].Pos;
                o.ExistingIds[i] = ex[i].Id;
                o.ExistingKind[i] = ex[i].Kind;
                o.Thinnable[i] = ex[i].Kind == TreeKind.Wild && OracleInside(run.Oracle, new double2(ex[i].Pos.x, ex[i].Pos.z));
            }
            o.Own = new ComposeMember[own.Count];
            o.OwnIds = new int[own.Count];
            for (int i = 0; i < own.Count; i++)
            {
                o.Own[i] = new ComposeMember { Position = own[i].Pos, Layer = own[i].Layer, Variant = own[i].Variant, PlanGen = own[i].PlanGen };
                o.OwnIds[i] = own[i].Id;
            }
            return o;
        }

        private static ComposeInput BuildInput(SimRun run, SimTarget target, int level, CensusOut c, ComposeCaps caps)
        {
            return new ComposeInput
            {
                Ring = run.Ring,
                Triangles = null,
                Params = SimParams(target),
                Level = (byte)level,
                TreeLine = target == SimTarget.TreeLine,
                Variant = 0,
                Obstacles = run.Obstacles,
                ExclusionRings = run.Exclusions,
                Existing = c.Existing,
                Thinnable = c.Thinnable,
                Own = c.Own,
                Caps = caps,
            };
        }

        private static ComposeResult ComposeOnly(World w, SimRun run, SimTarget target, int level, ComposeCaps caps, string where,
            KeyBook book, GladePlanCache cache = null)
        {
            CensusOut c = Census(w, run, target);
            ComposeInput input = BuildInput(run, target, level, c, caps);
            ulong key = SkiGladeCompose.PlanKey(input);
            HashSet<int> skip;
            w.Skip.TryGetValue(key, out skip);
            input.SkipSlots = skip;
            ComposeResult r = new ComposeResult();
            SkiGladeCompose.Compose(input, r, cache);
            s_Plans++;
            s_Sim.Composes++;
            Check(r.PlanKey == key, where, "Compose PlanKey " + r.PlanKey.ToString("X16") + " != PlanKey(input) " + key.ToString("X16"));
            CheckSharedResult(input, r, cache, where);
            CheckComposeOracles(run, target, level, c, input, r, where);
            if (book != null)
                BookRecord(book, key, r.Plan, where);
            return r;
        }

        private static ApplyReport ApplyOnce(World w, SimRun run, SimTarget target, int level, ComposeCaps caps, string where,
            KeyBook book, GladePlanCache cache = null)
        {
            ApplyReport rep = new ApplyReport();
            CensusOut c = Census(w, run, target);
            ComposeInput input = BuildInput(run, target, level, c, caps);
            ulong key = SkiGladeCompose.PlanKey(input);
            rep.Key = key;
            HashSet<int> skip;
            w.Skip.TryGetValue(key, out skip);
            input.SkipSlots = skip;
            SkiGladeCompose.Compose(input, rep.R, cache);
            ComposeResult r = rep.R;
            s_Plans++;
            s_Sim.Composes++;
            s_Sim.Applies++;
            if (r.FullReplan)
                s_Sim.FullReplans++;
            if (r.FullForMembers)
                s_Sim.FullForMembers++;
            if (r.CacheHit)
                s_Sim.CacheHits++;
            if (level == 1 && r.OwnUnmatched > 0)
                s_Sim.L1OffStream[(int)target]++;
            Check(r.PlanKey == key, where, "Compose PlanKey != PlanKey(input)");
            CheckSharedResult(input, r, cache, where);
            CheckComposeOracles(run, target, level, c, input, r, where);
            if (book != null)
                BookRecord(book, key, r.Plan, where);
            if (r.FullReplan)
                VerbatimSpecControl(run, target, c, input, r);

            byte layer = LayerOf(target);
            var otherLayerBefore = LayerRecords(w, (byte)(1 - layer));
            // The other layer's members this apply may remove: its MISPLACED census members only.
            HashSet<int> otherMayGo = new HashSet<int>();
            for (int j = 0; j < c.Own.Length; j++)
            {
                if (c.Own[j].Layer != layer && OracleMisplaced(run, target, c.Own[j]))
                    otherMayGo.Add(c.OwnIds[j]);
            }
            var foreignBefore = KindIds(w, TreeKind.Foreign);
            int wildBefore = KindIds(w, TreeKind.Wild).Count;

            HashSet<int> removeIds = new HashSet<int>();
            for (int k = 0; k < r.Fell.Count; k++)
            {
                int id = c.ExistingIds[r.Fell[k]];
                removeIds.Add(id);
                rep.FelledIds.Add(id);
            }
            for (int k = 0; k < r.Remove.Count; k++)
            {
                int id = c.OwnIds[r.Remove[k]];
                removeIds.Add(id);
                rep.RemovedIds.Add(id);
            }
            rep.Felled = rep.FelledIds.Count;
            rep.Removed = rep.RemovedIds.Count;

            // Stand-in validator: the strip, and a plant within StandInCollisionM of a tree that
            // still stands after this apply's fells / removals.
            List<double2> standing = new List<double2>();
            foreach (WTree t in w.Trees)
            {
                if (!removeIds.Contains(t.Id))
                    standing.Add(new double2(t.Pos.x, t.Pos.z));
            }
            double2[] plantXz = new double2[r.Plant.Count];
            for (int k = 0; k < plantXz.Length; k++)
            {
                float3 p = r.Plan.Planted[r.Plant[k]].Position;
                plantXz[k] = new double2(p.x, p.z);
            }
            int[] hitsLive = SweepCounts(plantXz, standing.ToArray(), StandInCollisionM);
            List<float3> toPlant = new List<float3>();
            for (int k = 0; k < r.Plant.Count; k++)
            {
                int slot = r.Plant[k];
                float3 pos = r.Plan.Planted[slot].Position;
                bool collides = hitsLive[k] > 0;
                if (collides)
                    rep.CancelledLive++;
                if (run.Blocks(pos) || collides)
                {
                    rep.Cancelled++;
                    HashSet<int> learn;
                    if (!w.Skip.TryGetValue(key, out learn))
                    {
                        learn = new HashSet<int>();
                        w.Skip[key] = learn;
                    }
                    learn.Add(slot);
                }
                else
                {
                    toPlant.Add(pos);
                }
            }
            w.RemoveIds(removeIds);
            for (int k = 0; k < toPlant.Count; k++)
            {
                rep.PlantedIds.Add(w.Add(toPlant[k], TreeKind.Member, OurRun, layer, 0, SkiGladeCompose.PlanGeneration));
                rep.PlantedPos.Add(toPlant[k]);
            }
            rep.Planted = toPlant.Count;
            s_Sim.LiveCancels += rep.CancelledLive;
            s_Sim.Cancelled += rep.Cancelled;

            // ---- world-level assertions (independent recount on the mutated world).
            Check(rep.Planted + rep.Cancelled == 0 || (rep.Removed == 0 && rep.Felled == 0), where,
                "NEVER BOTH: one apply planted " + (rep.Planted + rep.Cancelled) + " and removed " + rep.Removed + " / felled " + rep.Felled);
            {
                // Layers: every other-layer record after the apply stood there before (nothing
                // moved or added), and every one that vanished was a misplaced census member this
                // apply removed.
                var after = LayerRecords(w, (byte)(1 - layer));
                HashSet<(int, uint, uint, uint, byte, byte)> beforeSet = new HashSet<(int, uint, uint, uint, byte, byte)>(otherLayerBefore);
                bool kept = true;
                HashSet<int> afterIds = new HashSet<int>();
                foreach (var rec in after)
                {
                    kept &= beforeSet.Contains(rec);
                    afterIds.Add(rec.Item1);
                }
                int vanishedBad = 0, vanished = 0;
                foreach (var rec in otherLayerBefore)
                {
                    if (afterIds.Contains(rec.Item1))
                        continue;
                    vanished++;
                    if (!otherMayGo.Contains(rec.Item1) || !rep.RemovedIds.Contains(rec.Item1))
                        vanishedBad++;
                }
                Check(kept && vanishedBad == 0, where, "LAYERS: the other layer's members changed (" + vanished + " vanished, " + vanishedBad +
                    " of them not misplaced census members this apply removed; moved/added " + !kept + ")");
            }
            Check(KindIds(w, TreeKind.Foreign).SetEquals(foreignBefore), where, "a foreign tree was removed");
            if (target == SimTarget.TreeLine)
                Check(rep.Felled == 0 && KindIds(w, TreeKind.Wild).Count == wildBefore, where, "the TREE LINE felled " + rep.Felled + " wild tree(s)");
            if (rep.Felled > 0)
            {
                int standingMembers = CountInteriorCensusOwn(w, run);
                Check(standingMembers == 0, where, "MEMBERS FIRST: " + rep.Felled + " wild tree(s) felled while " + standingMembers +
                    " tree(s) of the run still stand in the interior census");
            }
            if (!r.Clamped && rep.Cancelled == 0 && r.Shortfall == 0)
            {
                if (target == SimTarget.Interior)
                {
                    int now = CountInsideAll(w, run);
                    int expect = r.Inside > r.Target ? Math.Max(r.Target, r.FixedInside) : r.Target;
                    Check(now == expect, where, "COUNT: " + now + " trees inside after the apply, expected " + expect +
                        " (T " + r.Target + ", N " + r.Inside + ", X " + r.FixedInside + ", L" + level + ")");
                }
                else
                {
                    int now = CountOwn(w, run, 1, false, true);
                    Check(now == r.Target, where, "COUNT: " + now + " belt members after the apply, expected T " + r.Target + " (L" + level + ")");
                }
            }
            return rep;
        }

        /// <summary>ApplyOnce until a compose reports Complete (nothing emitted). Returns the
        /// applies that changed something (in `all`) and the Complete report in `last`.</summary>
        private static int ApplyToComplete(World w, SimRun run, SimTarget target, int level, ComposeCaps caps, string where,
            KeyBook book, out ApplyReport last, List<ApplyReport> all = null, int maxApplies = 80, GladePlanCache cache = null)
        {
            last = null;
            for (int i = 0; i <= maxApplies; i++)
            {
                ApplyReport rep = ApplyOnce(w, run, target, level, caps, where, book, cache);
                last = rep;
                if (rep.R.Complete)
                {
                    Check(rep.NoOp, where, "Complete compose still changed the world");
                    return i;
                }
                if (all != null)
                    all.Add(rep);
                if (!Check(!rep.NoOp, where, "apply " + i + " not Complete but changed nothing (stall): plant " + rep.R.Plant.Count +
                        ", remove " + rep.R.Remove.Count + ", fell " + rep.R.Fell.Count + ", clamped " + rep.R.Clamped))
                    return i;
            }
            Fail(where, "no Complete within " + maxApplies + " applies");
            return maxApplies;
        }


        private static int CountInsideAll(World w, SimRun run)
        {
            int n = 0;
            foreach (WTree t in w.Trees)
            {
                if (OracleInside(run.Oracle, new double2(t.Pos.x, t.Pos.z)))
                    n++;
            }
            return n;
        }

        private static bool InRegion(SimRun run, byte layer, float3 p)
        {
            double2 q = new double2(p.x, p.z);
            bool inside = OracleInside(run.Oracle, q);
            if (layer == 0)
                return inside;
            return !inside && OracleBoundaryDistance(run.Oracle, q) <= TBeltDepth + TBeltMinSpacing;
        }

        /// <summary>Our members of `layer` in the layer region (interior: inside); current
        /// (variant 0, this generation) only unless anyVariant.</summary>
        private static int CountOwn(World w, SimRun run, byte layer, bool anyVariant, bool regionOnly)
        {
            int n = 0;
            foreach (WTree t in w.Trees)
            {
                if (t.Kind != TreeKind.Member || t.Run != OurRun || t.Layer != layer)
                    continue;
                if (!anyVariant && !IsCurrent(t.Variant, t.PlanGen))
                    continue;
                bool needRegion = regionOnly || layer == 0; // interior members count inside only
                if (!needRegion || InRegion(run, layer, t.Pos))
                    n++;
            }
            return n;
        }

        private static int CountOthersAnywhere(World w, SimRun run, byte layer)
        {
            double band = layer == 0 ? TGladeMinSpacing : TBeltDepth + TBeltMinSpacing;
            int n = 0;
            foreach (WTree t in w.Trees)
            {
                if (t.Kind != TreeKind.Member || t.Run != OurRun || t.Layer != layer || IsCurrent(t.Variant, t.PlanGen))
                    continue;
                double2 q = new double2(t.Pos.x, t.Pos.z);
                if (OracleInside(run.Oracle, q) || OracleBoundaryDistance(run.Oracle, q) <= band)
                    n++;
            }
            return n;
        }

        /// <summary>Oracle of SkiGladeCompose's MISPLACED rule (test-local geometry), as seen by
        /// `target`: an interior member outside the polygon, a tree-line member inside it, and —
        /// for the tree-line target only, which owns that test — a CURRENT tree-line member beyond
        /// BeltDepth + BeltMinSpacing. An other-tagged member of the target layer is Other by its
        /// tag (counted there, not here); unknown layer bytes are never misplaced.</summary>
        private static bool OracleMisplaced(SimRun run, SimTarget target, ComposeMember m)
        {
            bool inside = OracleInside(run.Oracle, new double2(m.Position.x, m.Position.z));
            if (m.Layer == 0)
                return !inside;
            if (m.Layer != 1)
                return false;
            if (inside)
                return true;
            return target == SimTarget.TreeLine && IsCurrent(m.Variant, m.PlanGen) && !InRegion(run, 1, m.Position);
        }

        /// <summary>This run's trees the INTERIOR target must clear before it fells or reads level
        /// 1 as done: every member inside (any layer, any tag) and every interior-layer member
        /// within the interior census band outside (misplaced strays).</summary>
        private static int CountInteriorCensusOwn(World w, SimRun run)
        {
            int n = 0;
            foreach (WTree t in w.Trees)
            {
                if (t.Kind != TreeKind.Member || t.Run != OurRun)
                    continue;
                double2 q = new double2(t.Pos.x, t.Pos.z);
                if (OracleInside(run.Oracle, q))
                    n++;
                else if (t.Layer == 0 && OracleBoundaryDistance(run.Oracle, q) <= TGladeMinSpacing)
                    n++;
            }
            return n;
        }

        /// <summary>This run's trees the TREE LINE at level 1 must leave none of: every tree-line
        /// member in its census (inside or within BeltDepth + BeltMinSpacing) and every
        /// interior-layer member outside within that band (misplaced strays).</summary>
        private static int CountTreeLineCensusOwn(World w, SimRun run)
        {
            double band = TBeltDepth + TBeltMinSpacing;
            int n = 0;
            foreach (WTree t in w.Trees)
            {
                if (t.Kind != TreeKind.Member || t.Run != OurRun)
                    continue;
                double2 q = new double2(t.Pos.x, t.Pos.z);
                bool inside = OracleInside(run.Oracle, q);
                bool inBand = !inside && OracleBoundaryDistance(run.Oracle, q) <= band;
                if (t.Layer == 1 && (inside || inBand))
                    n++;
                else if (t.Layer == 0 && inBand)
                    n++;
            }
            return n;
        }

        private static int CountInteriorStrays(World w, SimRun run, double band)
        {
            int n = 0;
            foreach (WTree t in w.Trees)
            {
                if (t.Kind != TreeKind.Member || t.Run != OurRun || t.Layer != 0)
                    continue;
                double2 q = new double2(t.Pos.x, t.Pos.z);
                if (!OracleInside(run.Oracle, q) && OracleBoundaryDistance(run.Oracle, q) <= band)
                    n++;
            }
            return n;
        }

        private static int CountBeltInside(World w, SimRun run)
        {
            int n = 0;
            foreach (WTree t in w.Trees)
            {
                if (t.Kind == TreeKind.Member && t.Run == OurRun && t.Layer == 1 &&
                    OracleInside(run.Oracle, new double2(t.Pos.x, t.Pos.z)))
                    n++;
            }
            return n;
        }

        /// <summary>Repeat the compose into the suite's ONE long-lived result
        /// (dirty from whatever compose came before) and require every documented field, list
        /// and plan field to equal the fresh-object compose. Skipped with a cache (CacheHit and
        /// the plan path legitimately differ there).</summary>
        private static void CheckSharedResult(ComposeInput input, ComposeResult fresh, GladePlanCache cache, string where)
        {
            if (cache != null)
                return;
            SkiGladeCompose.Compose(input, s_Shared, null);
            s_Plans++;
            s_Sim.SharedResultChecks++;
            string diff = ResultDiff(fresh, s_Shared);
            Check(diff == null, where, "REUSED RESULT: a compose into the long-lived ComposeResult differs from a fresh one in " + diff);
        }

        private static string ResultDiff(ComposeResult a, ComposeResult b)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            void D(bool same, string name)
            {
                if (!same)
                    sb.Append(sb.Length > 0 ? ", " : "").Append(name);
            }
            D(SamePlanned(a.Plan.Planted, b.Plan.Planted), "Plan.Planted");
            D(SameInts(a.Plan.Thinned, b.Plan.Thinned), "Plan.Thinned");
            D(a.Plan.Status == b.Plan.Status && a.Plan.Saturated == b.Plan.Saturated && a.Plan.Clamped == b.Plan.Clamped &&
              a.Plan.TargetGlade == b.Plan.TargetGlade && a.Plan.TargetBelt == b.Plan.TargetBelt &&
              a.Plan.RawTargetGlade == b.Plan.RawTargetGlade && a.Plan.RawTargetBelt == b.Plan.RawTargetBelt &&
              a.Plan.Attempts == b.Plan.Attempts && Bits(a.Plan.Area, b.Plan.Area) && Bits(a.Plan.BeltArea, b.Plan.BeltArea), "Plan fields");
            D(SameInts(a.Fell, b.Fell), "Fell");
            D(SameInts(a.Plant, b.Plant), "Plant");
            D(SameInts(a.Remove, b.Remove), "Remove");
            D(a.Layer == b.Layer && a.Level == b.Level, "Layer/Level");
            D(a.Target == b.Target && a.Inside == b.Inside && a.WildInside == b.WildInside && a.FixedInside == b.FixedInside &&
              a.OwnInside == b.OwnInside && a.Keep == b.Keep, "T/N/W/X/M/Keep");
            D(a.RawTarget == b.RawTarget && a.Slice == b.Slice, "RawTarget/Slice");
            D(a.FellFull == b.FellFull, "FellFull");
            D(a.Same == b.Same && a.SameIn == b.SameIn && a.Other == b.Other && a.OtherTagged == b.OtherTagged && a.Misplaced == b.Misplaced,
              "Same/SameIn/Other/OtherTagged/Misplaced");
            D(a.Matched == b.Matched && a.Missing == b.Missing && a.Skipped == b.Skipped && a.Deficit == b.Deficit, "Matched/Missing/Skipped/Deficit");
            D(a.Clamped == b.Clamped && a.Saturated == b.Saturated && a.Complete == b.Complete && a.Status == b.Status, "flags/Status");
            D(a.PlanKey == b.PlanKey, "PlanKey");
            D(a.OwnUnmatched == b.OwnUnmatched && a.Shortfall == b.Shortfall, "OwnUnmatched/Shortfall");
            D(a.RequestedSlice == b.RequestedSlice && a.PlanIsSlice == b.PlanIsSlice, "RequestedSlice/PlanIsSlice");
            D(a.FullReplan == b.FullReplan, "FullReplan");
            D(a.FullForMembers == b.FullForMembers, "FullForMembers");
            D(a.CacheHit == b.CacheHit, "CacheHit");
            D(a.Occupants == b.Occupants && a.OccupantsOtherLayer == b.OccupantsOtherLayer && a.KeyedOccupants == b.KeyedOccupants &&
              a.KeyedOtherLayer == b.KeyedOtherLayer, "Occupants/Keyed");
            return sb.Length == 0 ? null : sb.ToString();
        }

        /// <summary>Mitre offset of a simple ring by d meters (d > 0 outward, every edge moves by
        /// exactly d; d &lt; 0 inward). Test-local; y re-sampled from the test ground.</summary>
        private static float3[] OffsetRing(float3[] ring, double d)
        {
            double2[] v = ToXz(ring);
            int n = v.Length;
            double sign = OracleShoelace(v) > 0 ? 1.0 : -1.0; // CCW: outward = right-hand normal
            double2[] normal = new double2[n];
            for (int i = 0; i < n; i++)
            {
                double2 e = v[(i + 1) % n] - v[i];
                double len = Math.Sqrt(e.x * e.x + e.y * e.y);
                normal[i] = new double2(e.y, -e.x) * (sign / len);
            }
            float3[] r = new float3[n];
            for (int i = 0; i < n; i++)
            {
                double2 n1 = normal[(i + n - 1) % n], n2 = normal[i];
                double k = 1.0 + n1.x * n2.x + n1.y * n2.y;
                double2 p = v[i] + (n1 + n2) * (d / k);
                r[i] = new float3((float)p.x, GroundY(p.x, p.y), (float)p.y);
            }
            return r;
        }

        private static HashSet<int> KindIds(World w, TreeKind kind)
        {
            HashSet<int> s = new HashSet<int>();
            foreach (WTree t in w.Trees)
            {
                if (t.Kind == kind)
                    s.Add(t.Id);
            }
            return s;
        }

        private static HashSet<int> LayerIds(World w, byte layer)
        {
            HashSet<int> s = new HashSet<int>();
            foreach (WTree t in w.Trees)
            {
                if (t.Kind == TreeKind.Member && t.Run == OurRun && t.Layer == layer)
                    s.Add(t.Id);
            }
            return s;
        }

        private static List<(int, uint, uint, uint, byte, byte)> LayerRecords(World w, byte layer)
        {
            List<(int, uint, uint, uint, byte, byte)> l = new List<(int, uint, uint, uint, byte, byte)>();
            foreach (WTree t in w.Trees)
            {
                if (t.Kind == TreeKind.Member && t.Run == OurRun && t.Layer == layer)
                    l.Add((t.Id, math.asuint(t.Pos.x), math.asuint(t.Pos.z), math.asuint(t.Pos.y), t.Variant, t.PlanGen));
            }
            l.Sort();
            return l;
        }

        private static List<(uint, uint, uint)> PosBits(IEnumerable<float3> ps)
        {
            List<(uint, uint, uint)> l = new List<(uint, uint, uint)>();
            foreach (float3 p in ps)
                l.Add((math.asuint(p.x), math.asuint(p.z), math.asuint(p.y)));
            l.Sort();
            return l;
        }

        private static bool SameList<T>(List<T> a, List<T> b)
        {
            if (a.Count != b.Count)
                return false;
            EqualityComparer<T> eq = EqualityComparer<T>.Default;
            for (int i = 0; i < a.Count; i++)
            {
                if (!eq.Equals(a[i], b[i]))
                    return false;
            }
            return true;
        }

        private static bool SamePlanned(List<PlantedTree> a, List<PlantedTree> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!SameTree(a[i], b[i]))
                    return false;
            }
            return true;
        }


        /// <summary>The documented rank key, restated (SkiGladePlanner.ThinSelect's hash:
        /// Fmix32(math.hash(uint4(seed, SaltThin, floor(8x), floor(8z))))).</summary>
        private static uint OracleThinHash(float3 p, uint seed)
        {
            if (float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.z) || float.IsInfinity(p.z))
                return 0u;
            float qx = MathF.Floor(p.x * 8f), qz = MathF.Floor(p.z * 8f);
            if (Math.Abs(qx) > 2.0e9f || Math.Abs(qz) > 2.0e9f)
                return 0u;
            unchecked
            {
                uint h = math.hash(new uint4(seed, OracleSaltThin, (uint)(int)qx, (uint)(int)qz));
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>The plan stream from its definition: the planner's full glade (density = the stream density 157.3 / ha) or
        /// belt layer, variant-0 seed, occupants = every census tree that is not ours ∪ our
        /// members of the other layer that are NOT misplaced. Memoized by a test-local key.</summary>
        private static OracleStreamEntry OracleStream(CensusOut c, SimRun run, SimTarget target)
        {
            byte layer = LayerOf(target);
            List<float3> occ = new List<float3>(c.Existing);
            for (int j = 0; j < c.Own.Length; j++)
            {
                if (c.Own[j].Layer != layer && !OracleMisplaced(run, target, c.Own[j]))
                    occ.Add(c.Own[j].Position);
            }
            ulong[] bits = new ulong[occ.Count];
            for (int i = 0; i < bits.Length; i++)
                bits[i] = ((ulong)math.asuint(occ[i].x) << 32) | math.asuint(occ[i].z);
            Array.Sort(bits);
            ulong h = 1469598103934665603UL;
            unchecked
            {
                h = (h ^ layer) * 1099511628211UL;
                foreach (float3 v in run.Ring)
                {
                    h = (h ^ math.asuint(v.x)) * 1099511628211UL;
                    h = (h ^ math.asuint(v.y)) * 1099511628211UL;
                    h = (h ^ math.asuint(v.z)) * 1099511628211UL;
                }
                h = (h ^ (ulong)(run.Exclusions != null ? run.Exclusions.Length : 0)) * 1099511628211UL;
                foreach (ulong b in bits)
                    h = (h ^ b) * 1099511628211UL;
            }
            OracleStreamEntry e;
            if (s_StreamMemo.TryGetValue(h, out e))
                return e;
            GladeParams p = SimParams(target);
            p.Mode = target == SimTarget.Interior ? GladeMode.Glade : GladeMode.Belt;
            p.ThinFraction = 0f;
            p.GladeDensity = OracleStreamD;
            p.MaxPlant = int.MaxValue;
            GladePlan plan = new GladePlan();
            SkiGladePlanner.Plan(run.Ring, null, SkiGladePlanner.Seed32(run.Ring[0], SkiGladeCompose.VariantSalt(0)), in p,
                run.Obstacles, occ.ToArray(), null, run.Exclusions, plan);
            s_Plans++;
            e = new OracleStreamEntry { Slots = new List<PlantedTree>(plan.Planted), Saturated = plan.Saturated };
            if (s_StreamMemo.Count > 96)
                s_StreamMemo.Clear();
            s_StreamMemo[h] = e;
            return e;
        }

        /// <summary>floor(x + 0.5) vs `got`: exact unless x is within rounding noise of a .5
        /// boundary (float densities, the planner's origin-shifted shoelace), then ±1.</summary>
        private static bool CheckRounded(string where, string what, int got, double x)
        {
            double expect = Math.Max(0.0, Math.Floor(x + 0.5));
            double frac = x - Math.Floor(x);
            double tol = 2e-6 * Math.Abs(x) + 1e-6;
            if (Math.Abs(frac - 0.5) <= tol)
            {
                s_Sim.TargetNearHalf++;
                return Check(Math.Abs(got - expect) <= 1.0, where, what + " " + got + " vs oracle " + F(expect) + " (near .5)");
            }
            return Check(got == expect, where, what + " " + got + " vs oracle " + F(expect) + " (x " + F(x) + ")");
        }


        private static void BookRecord(KeyBook book, ulong key, GladePlan plan, string where)
        {
            List<PlantedTree> prev;
            if (book.Plans.TryGetValue(key, out prev))
            {
                book.Hits++;
                int n = Math.Min(prev.Count, plan.Planted.Count);
                for (int i = 0; i < n; i++)
                {
                    if (!SameTree(prev[i], plan.Planted[i]))
                    {
                        Fail(where, "PlanKey " + key.ToString("X16") + " reused with a different P (slot " + i + ") — key unsound");
                        return;
                    }
                }
                if (plan.Planted.Count > prev.Count)
                    book.Plans[key] = new List<PlantedTree>(plan.Planted);
            }
            else
            {
                book.Plans[key] = new List<PlantedTree>(plan.Planted);
            }
        }

        private struct OKey
        {
            public int Group;
            public uint Key;
            public float X, Z;
            public int Index;
        }

        private static int OKeyCompare(OKey a, OKey b)
        {
            int c = a.Group.CompareTo(b.Group);
            if (c != 0) return c;
            c = a.Key.CompareTo(b.Key);
            if (c != 0) return c;
            c = a.X.CompareTo(b.X);
            if (c != 0) return c;
            c = a.Z.CompareTo(b.Z);
            if (c != 0) return c;
            return a.Index.CompareTo(b.Index);
        }

        private static void CheckComposeOracles(SimRun run, SimTarget target, int level, CensusOut c, ComposeInput input,
            ComposeResult r, string where)
        {
            byte layer = LayerOf(target);
            bool interior = target == SimTarget.Interior;
            int lv = Math.Max(1, Math.Min(8, level));
            int capP = Math.Max(0, input.Caps.MaxPlant), capF = Math.Max(0, input.Caps.MaxFell), capR = Math.Max(0, input.Caps.MaxRemove);
            Check(r.Layer == layer && r.Level == lv, where, "Layer/Level " + r.Layer + "/" + r.Level);
            Check(r.Complete == (r.Plant.Count == 0 && r.Remove.Count == 0 && r.Fell.Count == 0), where, "Complete flag inconsistent");
            Check(!(r.Saturated && r.Clamped), where, "Saturated and Clamped");
            Check(r.Fell.Count <= capF && r.Plant.Count <= capP && r.Remove.Count <= capR, where, "a cap is exceeded");
            Check(r.Plant.Count == 0 || (r.Remove.Count == 0 && r.Fell.Count == 0), where,
                "NEVER BOTH: plant " + r.Plant.Count + " with remove " + r.Remove.Count + " / fell " + r.Fell.Count);
            if (!interior)
                Check(r.Fell.Count == 0 && r.FellFull == 0, where, "the TREE LINE fells " + r.Fell.Count);

            // ---- recount W / X / M / Others / N.
            int nEx = c.Existing.Length;
            List<int> wild = new List<int>();
            int x = 0;
            for (int i = 0; i < nEx; i++)
            {
                if (!OracleInside(run.Oracle, new double2(c.Existing[i].x, c.Existing[i].z)))
                    continue;
                if (c.Thinnable[i])
                    wild.Add(i);
                else
                    x++;
            }
            // Own (MISPLACED oracle): the target layer → Same in its region / other-tagged /
            // misplaced; the other layer → occupant or misplaced. Others = tagged ∪ misplaced.
            List<int> sameIn = new List<int>(), others = new List<int>();
            int tagged = 0, misplaced = 0;
            for (int j = 0; j < c.Own.Length; j++)
            {
                ComposeMember m = c.Own[j];
                bool mi = OracleInside(run.Oracle, new double2(m.Position.x, m.Position.z));
                if (m.Layer != layer)
                {
                    if (OracleMisplaced(run, target, m))
                    {
                        others.Add(j);
                        misplaced++;
                        s_Sim.Misplaced[(int)target, m.Layer]++;
                    }
                    else if (interior && mi)
                    {
                        x++; // an unknown layer only (ApplySim never writes one)
                    }
                    continue;
                }
                if (!IsCurrent(m.Variant, m.PlanGen))
                {
                    others.Add(j);
                    tagged++;
                }
                else if (!InRegion(run, layer, m.Position))
                {
                    others.Add(j);
                    misplaced++;
                    s_Sim.Misplaced[(int)target, m.Layer]++;
                }
                else
                {
                    sameIn.Add(j);
                }
            }
            int w = interior ? wild.Count : 0;
            if (!interior)
                x = 0;
            int mIn = sameIn.Count;
            int n = interior ? w + x + mIn : mIn;
            Check(r.WildInside == w && r.FixedInside == x && r.OwnInside == mIn && r.Inside == n && r.SameIn == mIn, where,
                "W/X/M/N " + r.WildInside + "/" + r.FixedInside + "/" + r.OwnInside + "/" + r.Inside + " vs recount " + w + "/" + x + "/" + mIn + "/" + n);
            Check(r.Same == mIn && r.Other == others.Count && r.OtherTagged == tagged && r.Misplaced == misplaced, where,
                "MISPLACED: Same/Other/OtherTagged/Misplaced " + r.Same + "/" + r.Other + "/" + r.OtherTagged + "/" + r.Misplaced +
                " vs " + mIn + "/" + others.Count + "/" + tagged + "/" + misplaced);

            // ---- T (documented table typed in here; the test's own area / perimeter).
            if (interior)
            {
                CheckRounded(where, "Target L" + lv, r.Target, OracleDensityPerHa[lv - 1] / 10000.0 * run.OracleArea);
                int ts = (int)Math.Floor(OracleStreamD * run.OracleArea + 0.5);
                Check(Math.Abs(r.RawTarget - Math.Max(0, ts - w - x)) <= 1, where, "stream raw " + r.RawTarget + " vs round(157.3/ha · A) − W − X = " + (ts - w - x));
            }
            else
            {
                double bx = (double)TBeltDensity * TBeltCoverage * run.OraclePerimeter * (TBeltDepth - TBeltGap);
                CheckRounded(where, "belt base B", r.RawTarget, bx);
                CheckRounded(where, "belt Target L" + lv, r.Target, OracleBeltFactor[lv - 1] * r.RawTarget);
            }
            int t = r.Target;
            Check(r.Deficit == Math.Max(0, t - n), where, "Deficit " + r.Deficit + " vs " + Math.Max(0, t - n));

            // ---- P is a bit-exact prefix of the documented stream.
            OracleStreamEntry po = OracleStream(c, run, target);
            List<PlantedTree> ps = po.Slots;
            bool prefix = r.Slice == r.Plan.Planted.Count && r.Slice <= ps.Count;
            for (int s = 0; prefix && s < r.Slice; s++)
                prefix = SameTree(r.Plan.Planted[s], ps[s]);
            Check(prefix, where, "P (" + r.Slice + ") is not a bit-exact prefix of the spec stream (" + ps.Count + ") — wrong occupants, seed or density?");
            if (!r.PlanIsSlice)
                Check(r.Slice == ps.Count, where, "P is not a slice but holds " + r.Slice + " of the stream's " + ps.Count);
            else
                Check(r.Slice == r.RequestedSlice && r.Slice < ps.Count, where, "a slice of " + r.Slice + " (asked " + r.RequestedSlice + ", stream " + ps.Count + ")");

            // ---- matching against the FULL stream (sorted-x sweeps).
            double rad = (interior ? TGladeMinSpacing : TBeltMinSpacing) * 0.5;
            double2[] slotXz = new double2[ps.Count];
            for (int s = 0; s < ps.Count; s++)
                slotXz[s] = new double2(ps[s].Position.x, ps[s].Position.z);
            double2[] sameXz = new double2[sameIn.Count];
            for (int k = 0; k < sameIn.Count; k++)
                sameXz[k] = new double2(c.Own[sameIn[k]].Position.x, c.Own[sameIn[k]].Position.z);
            int[] perSlot = SweepCounts(slotXz, sameXz, rad);
            int[] perMember = SweepCounts(sameXz, slotXz, rad);
            int[] memberSlot = SweepLowestIndex(sameXz, slotXz, rad);
            s_Sim.MatchChecks += ps.Count;
            int doubles = 0;
            for (int k = 0; k < perMember.Length; k++)
            {
                if (perMember[k] > 1)
                    doubles++;
            }
            Check(doubles == 0, where, doubles + " member(s) match two slots (radius MinSpacing/2)");
            HashSet<int> skip = input.SkipSlots;
            int matched = 0, missing = 0, skipped = 0;
            for (int s = 0; s < r.Slice; s++)
            {
                bool isSkip = skip != null && skip.Contains(s);
                if (isSkip)
                    skipped++;
                if (perSlot[s] > 0)
                    matched++;
                else if (!isSkip)
                    missing++;
            }
            Check(r.Matched == matched && r.Missing == missing && r.Skipped == skipped, where,
                "matched/missing/skipped " + r.Matched + "/" + r.Missing + "/" + r.Skipped + " vs " + matched + "/" + missing + "/" + skipped);
            Dictionary<int, int> slotOf = new Dictionary<int, int>();
            for (int k = 0; k < sameIn.Count; k++)
                slotOf[sameIn[k]] = memberSlot[k];
            int unmatchedIn = 0;
            foreach (int j in sameIn)
            {
                if (slotOf[j] < 0 || slotOf[j] >= r.Slice)
                    unmatchedIn++;
            }
            Check(r.OwnUnmatched == unmatchedIn, where, "OwnUnmatched " + r.OwnUnmatched + " vs " + unmatchedIn + " (against P)");

            // ---- Other members first of all: the first min(Others, cap) of Remove, ascending (x, z).
            others.Sort((a, b) =>
            {
                int cc = c.Own[a].Position.x.CompareTo(c.Own[b].Position.x);
                if (cc != 0) return cc;
                cc = c.Own[a].Position.z.CompareTo(c.Own[b].Position.z);
                return cc != 0 ? cc : a.CompareTo(b);
            });
            int othersTake = Math.Min(others.Count, capR);
            bool othersOk = r.Remove.Count >= othersTake;
            for (int k = 0; othersOk && k < othersTake; k++)
                othersOk = r.Remove[k] == others[k];
            Check(othersOk, where, "Remove does not start with the " + othersTake + " Other member(s) in (x, z) order");
            int othersLeft = others.Count - othersTake;
            if (others.Count > 0)
                s_Sim.OthersBranch++;

            bool clampedO;
            bool sliceEdge = false;
            bool satO = false;
            if (n > t)
            {
                Check(r.Plant.Count == 0, where, "too many (N " + n + " > T " + t + ") but Plant " + r.Plant.Count);
                int keepO = interior ? t - w - x : t;
                keepO = Math.Max(0, Math.Min(mIn, keepO));
                Check(r.Keep == keepO, where, "Keep " + r.Keep + " vs " + keepO + " (T " + t + ", W " + w + ", X " + x + ", M " + mIn + ")");
                // Keep order: matched by slot of the FULL stream, unmatched by hash; remove from the end.
                OKey[] order = new OKey[sameIn.Count];
                for (int k = 0; k < sameIn.Count; k++)
                {
                    int j = sameIn[k];
                    int slot = slotOf[j];
                    float3 p = c.Own[j].Position;
                    order[k] = new OKey { Group = slot >= 0 ? 0 : 1, Key = slot >= 0 ? (uint)slot : OracleThinHash(p, 0x7A1C3E5Bu), X = p.x, Z = p.z, Index = j };
                }
                Array.Sort(order, OKeyCompare);
                int removeWanted = mIn - keepO;
                int take = Math.Min(removeWanted, Math.Max(0, capR - othersTake));
                bool seqOk = r.Remove.Count == othersTake + take;
                for (int k = 0; seqOk && k < take; k++)
                    seqOk = r.Remove[othersTake + k] == order[order.Length - 1 - k].Index;
                Check(seqOk, where, "KEEP ORDER: the count-rule removal is not the last " + take + " member(s) by slot (" +
                    (r.Remove.Count - othersTake) + " removed, keep " + keepO + " of " + mIn + ")");
                if (removeWanted > 0)
                    s_Sim.KeepRemoveBranch++;
                bool membersGone = othersLeft == 0 && take == removeWanted;
                int fellFullO = 0;
                if (interior && keepO == 0)
                {
                    fellFullO = Math.Max(0, w - Math.Max(0, t - x));
                    Check(r.FellFull == fellFullO, where, "FellFull " + r.FellFull + " vs W − max(0, T − X) = " + fellFullO);
                    if (membersGone && fellFullO > 0)
                    {
                        OKey[] rank = new OKey[wild.Count];
                        for (int k = 0; k < wild.Count; k++)
                        {
                            float3 p = c.Existing[wild[k]];
                            rank[k] = new OKey { Group = 0, Key = ~OracleThinHash(p, 0x7A1C3E5Bu), X = p.x, Z = p.z, Index = wild[k] };
                        }
                        Array.Sort(rank, OKeyCompare); // ~hash ascending = hash descending
                        int nf = Math.Min(fellFullO, capF);
                        List<int> expect = new List<int>();
                        for (int k = 0; k < nf; k++)
                            expect.Add(rank[k].Index);
                        expect.Sort();
                        Check(SameInts(expect, r.Fell), where, "EXACT RANK: Fell (" + r.Fell.Count + ") is not the top " + nf +
                            " wild tree(s) by ThinHash rank");
                        s_Sim.FellBranch++;
                        if (r.Remove.Count > 0)
                            s_Sim.FellWithRemove++;
                    }
                    else
                    {
                        Check(r.Fell.Count == 0, where, "MEMBERS FIRST: felled " + r.Fell.Count + " while " + (othersLeft + removeWanted - take) +
                            " member(s) stay standing after this apply");
                    }
                }
                else
                {
                    Check(r.Fell.Count == 0 && r.FellFull == 0, where, "felled " + r.Fell.Count + " (FellFull " + r.FellFull + ") although " + keepO +
                        " member(s) are kept");
                }
                clampedO = othersLeft > 0 || take < removeWanted || (interior && keepO == 0 && r.Fell.Count < fellFullO);
            }
            else
            {
                Check(r.Keep == mIn && r.Fell.Count == 0 && r.FellFull == 0 && r.Remove.Count == othersTake, where,
                    "N " + n + " <= T " + t + " but keep " + r.Keep + ", fell " + r.Fell.Count + ", remove " + r.Remove.Count);
                if (n < t && others.Count == 0)
                {
                    s_Sim.PlantBranch++;
                    int want = Math.Min(t - n, capP);
                    List<int> expect = new List<int>();
                    int plantableTotal = 0;
                    for (int s = 0; s < ps.Count; s++)
                    {
                        if (perSlot[s] > 0 || (skip != null && skip.Contains(s)))
                            continue;
                        plantableTotal++;
                        if (expect.Count < want)
                            expect.Add(s);
                    }
                    Check(SameInts(expect, r.Plant), where, "PLANT: " + r.Plant.Count + " slot(s), expected the first " + expect.Count +
                        " unmatched unskipped slots of the stream (want " + want + ")");
                    Check(r.Shortfall == want - expect.Count, where, "Shortfall " + r.Shortfall + " vs " + (want - expect.Count));
                    if (expect.Count < want)
                    {
                        Check(!r.PlanIsSlice, where, "Shortfall on a slice — Compose did not look at the full stream");
                        s_Sim.Shortfalls++;
                    }
                    clampedO = t - n > r.Plant.Count && plantableTotal > r.Plant.Count;
                    if (!clampedO && r.Clamped && r.PlanIsSlice && t - n > r.Plant.Count)
                        sliceEdge = true; // Compose cannot see past its slice (engine notes, FLAGS)
                    satO = expect.Count < want && po.Saturated;
                }
                else
                {
                    Check(r.Plant.Count == 0 && r.Shortfall == 0, where, "plant " + r.Plant.Count + " (N " + n + ", T " + t + ", Others " + others.Count + ")");
                    clampedO = othersLeft > 0 || (n < t && others.Count > 0);
                }
            }
            if (sliceEdge)
                s_Sim.SliceClampEdge++;
            else
                Check(r.Clamped == clampedO, where, "Clamped " + r.Clamped + " vs oracle " + clampedO);
            Check(r.Saturated == satO, where, "Saturated " + r.Saturated + " vs oracle " + satO);
            if (r.Saturated)
                s_Sim.SaturatedComposes++;
        }

        /// <summary>For each point of `a`, the number of points of `b` at xz distance &lt; r
        /// (sorted-x sweep; independent of Compose's bucket grid).</summary>
        private static int[] SweepCounts(double2[] a, double2[] b, double r)
        {
            int[] counts = new int[a.Length];
            if (a.Length == 0 || b.Length == 0)
                return counts;
            double2[] sb = (double2[])b.Clone();
            Array.Sort(sb, (u, v) => u.x.CompareTo(v.x));
            double[] xs = new double[sb.Length];
            for (int i = 0; i < sb.Length; i++)
                xs[i] = sb[i].x;
            double r2 = r * r;
            for (int i = 0; i < a.Length; i++)
            {
                int lo = LowerBoundD(xs, a[i].x - r);
                int n = 0;
                for (int j = lo; j < sb.Length && sb[j].x <= a[i].x + r; j++)
                {
                    double dx = sb[j].x - a[i].x, dz = sb[j].y - a[i].y;
                    if (dx * dx + dz * dz < r2)
                        n++;
                }
                counts[i] = n;
            }
            return counts;
        }

        /// <summary>For each point of `a`, the LOWEST index of a point of `b` at distance &lt; r,
        /// or -1.</summary>
        private static int[] SweepLowestIndex(double2[] a, double2[] b, double r)
        {
            int[] res = new int[a.Length];
            for (int i = 0; i < res.Length; i++)
                res[i] = -1;
            if (a.Length == 0 || b.Length == 0)
                return res;
            int[] order = new int[b.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            Array.Sort(order, (u, v) => b[u].x.CompareTo(b[v].x));
            double[] xs = new double[b.Length];
            for (int i = 0; i < b.Length; i++)
                xs[i] = b[order[i]].x;
            double r2 = r * r;
            for (int i = 0; i < a.Length; i++)
            {
                int lo = LowerBoundD(xs, a[i].x - r);
                for (int j = lo; j < xs.Length && xs[j] <= a[i].x + r; j++)
                {
                    double2 q = b[order[j]];
                    double dx = q.x - a[i].x, dz = q.y - a[i].y;
                    if (dx * dx + dz * dz < r2 && (res[i] < 0 || order[j] < res[i]))
                        res[i] = order[j];
                }
            }
            return res;
        }

        private static int LowerBoundD(double[] xs, double x)
        {
            int lo = 0, hi = xs.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (xs[mid] < x)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        private static double SegDist(double2 p, double2 a, double2 b)
        {
            double2 ab = b - a;
            double len2 = ab.x * ab.x + ab.y * ab.y;
            double t = len2 > 0 ? ((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / len2 : 0;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            double dx = a.x + t * ab.x - p.x, dz = a.y + t * ab.y - p.y;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>CONTROL for the "slice ran out of attempts" refinement (SkiGladeCompose
        /// engine notes): on every compose that needed a full re-plan, plan the requested slice
        /// VERBATIM (planner at MaxPlant = RequestedSlice, test-built occupants) and count how
        /// often it would have offered fewer plantable slots — a false Complete when none.</summary>
        private static void VerbatimSpecControl(SimRun run, SimTarget target, CensusOut c, ComposeInput input, ComposeResult r)
        {
            byte layer = LayerOf(target);
            List<float3> occ = new List<float3>(c.Existing);
            List<double2> same = new List<double2>();
            for (int j = 0; j < c.Own.Length; j++)
            {
                if (c.Own[j].Layer != layer)
                {
                    if (!OracleMisplaced(run, target, c.Own[j]))
                        occ.Add(c.Own[j].Position);
                }
                else if (IsCurrent(c.Own[j].Variant, c.Own[j].PlanGen) && InRegion(run, layer, c.Own[j].Position))
                {
                    same.Add(new double2(c.Own[j].Position.x, c.Own[j].Position.z));
                }
            }
            GladeParams p = SimParams(target);
            p.Mode = target == SimTarget.Interior ? GladeMode.Glade : GladeMode.Belt;
            p.ThinFraction = 0f;
            p.GladeDensity = OracleStreamD;
            p.MaxPlant = r.RequestedSlice;
            GladePlan slice = new GladePlan();
            SkiGladePlanner.Plan(run.Ring, null, SkiGladePlanner.Seed32(run.Ring[0], SkiGladeCompose.VariantSalt(0)), in p,
                run.Obstacles, occ.ToArray(), null, run.Exclusions, slice);
            s_Plans++;
            double2[] slots = new double2[slice.Planted.Count];
            for (int s = 0; s < slots.Length; s++)
                slots[s] = new double2(slice.Planted[s].Position.x, slice.Planted[s].Position.z);
            int[] matched = SweepCounts(slots, same.ToArray(), p.MinSpacing * 0.5);
            int plantable = 0;
            for (int s = 0; s < slots.Length; s++)
            {
                if (matched[s] == 0 && !(input.SkipSlots != null && input.SkipSlots.Contains(s)))
                    plantable++;
            }
            int verbatimPlant = Math.Min(plantable, Math.Min(r.Deficit, Math.Max(0, input.Caps.MaxPlant)));
            s_Sim.VerbatimChecked++;
            if (verbatimPlant == 0 && r.Plant.Count > 0)
                s_Sim.VerbatimFalseComplete++;
            else if (verbatimPlant < r.Plant.Count)
                s_Sim.VerbatimShort++;
        }


        /// <summary>After ApplyToComplete: the count holds (or the stream ran out), L1 clears,
        /// and applying the level once more is a no-op.</summary>
        private static void SettleChecks(World w, SimRun run, SimTarget target, int level, ApplyReport last, string where, KeyBook book)
        {
            if (last == null)
                return;
            ComposeResult r = last.R;
            if (r.Inside > r.Target)
                Check(target == SimTarget.Interior && r.WildInside == 0 && r.OwnInside == 0 && r.Other == 0, where,
                    "settled with N " + r.Inside + " > T " + r.Target + " but W " + r.WildInside + ", M " + r.OwnInside + ", Others " + r.Other);
            else if (r.Inside < r.Target)
                Check(r.Shortfall > 0, where, "settled with N " + r.Inside + " < T " + r.Target + " without a Shortfall (stall)");
            if (level == 1)
            {
                // EVERY tree of this run the target's census sees, any layer, any tag — not the
                // region-only count Compose itself uses (that hid our own trees standing in a widened / narrowed run).
                if (target == SimTarget.Interior)
                {
                    int members = CountInteriorCensusOwn(w, run);
                    Check(members == 0, where, "LEVEL 1 left " + members + " tree(s) of the run in the interior census (inside, or interior strays within " +
                        TGladeMinSpacing + " m)");
                    Check(CountInsideAll(w, run) == r.FixedInside, where, "LEVEL 1 left " + CountInsideAll(w, run) + " trees inside, X = " + r.FixedInside);
                }
                else
                {
                    int members = CountTreeLineCensusOwn(w, run);
                    Check(members == 0, where, "LEVEL 1 left " + members + " tree(s) of the run in the tree-line census (belt members anywhere in it, " +
                        "interior strays outside)");
                }
            }
            ApplyReport again = ApplyOnce(w, run, target, level, Caps(TCap), where + "-again", book);
            Check(again.NoOp && again.R.Complete, where, "APPLY TWICE: the repeat at L" + level + " changed the world (plant " + again.Planted +
                ", remove " + again.Removed + ", fell " + again.Felled + ")");
        }

        /// <summary>True when every current member of the target layer sits on a slot of the
        /// current stream and none stands outside its region (the "stream unchanged"
        /// precondition of the exact plant / up-down checks).</summary>
        private static bool MembersOnStream(World w, SimRun run, SimTarget target, out OracleStreamEntry po, out int[] perSlot)
        {
            CensusOut c = Census(w, run, target);
            po = OracleStream(c, run, target);
            byte layer = LayerOf(target);
            List<double2> same = new List<double2>();
            bool ok = true;
            for (int j = 0; j < c.Own.Length; j++)
            {
                ComposeMember m = c.Own[j];
                if (m.Layer != layer)
                {
                    if (OracleMisplaced(run, target, m))
                        ok = false; // removed first by the next apply: the stream state changes
                    continue;
                }
                if (!IsCurrent(m.Variant, m.PlanGen) || !InRegion(run, layer, m.Position))
                    ok = false;
                same.Add(new double2(m.Position.x, m.Position.z));
            }
            double2[] slots = new double2[po.Slots.Count];
            for (int s = 0; s < slots.Length; s++)
                slots[s] = new double2(po.Slots[s].Position.x, po.Slots[s].Position.z);
            double rad = (target == SimTarget.Interior ? TGladeMinSpacing : TBeltMinSpacing) * 0.5;
            perSlot = SweepCounts(slots, same.ToArray(), rad);
            int[] perMember = SweepCounts(same.ToArray(), slots, rad);
            for (int k = 0; k < perMember.Length; k++)
            {
                if (perMember[k] == 0)
                    ok = false;
            }
            return ok;
        }

        /// <summary>A settle that (from a state whose members are on the stream) planted:
        /// the union of its plants must be exactly the first D unmatched, unblocked slots of
        /// the stream (blocked spots compensated by later slots), or all of them when fewer.</summary>
        private static void CheckPlantSet(SimRun run, OracleStreamEntry po, int[] perSlot, int deficit, List<ApplyReport> reps, string where)
        {
            List<float3> expect = new List<float3>();
            for (int s = 0; s < po.Slots.Count && expect.Count < deficit; s++)
            {
                if (perSlot[s] > 0 || run.Blocks(po.Slots[s].Position))
                    continue;
                expect.Add(po.Slots[s].Position);
            }
            List<float3> got = new List<float3>();
            foreach (ApplyReport rep in reps)
                got.AddRange(rep.PlantedPos);
            s_Sim.PlantSetChecks++;
            Check(SameList(PosBits(got), PosBits(expect)), where, "PLANT SET: planted " + got.Count + " position(s), expected the first " +
                expect.Count + " unmatched unblocked slots of the stream (deficit " + deficit + ")");
        }

        private static List<ApplyReport> Settle(World w, SimRun run, SimTarget target, int level, ComposeCaps caps, string where, KeyBook book)
        {
            OracleStreamEntry po;
            int[] perSlot;
            bool onStream = MembersOnStream(w, run, target, out po, out perSlot);
            List<ApplyReport> reps = new List<ApplyReport>();
            ApplyReport last;
            ApplyToComplete(w, run, target, level, caps, where, book, out last, reps);
            if (target == SimTarget.Interior && level == 8 && last != null && last.R.Inside == last.R.Target)
            {
                bool cancelled = false;
                foreach (ApplyReport rep in reps)
                    cancelled |= rep.Cancelled > 0;
                if (cancelled)
                    s_Sim.L8HeadroomUsed++; // blocked spots replaced from the stream's tail
            }
            if (reps.Count > 0 && reps[0].R.Plant.Count + reps[0].Cancelled > 0 && reps[0].R.Other == 0)
            {
                if (onStream)
                    CheckPlantSet(run, po, perSlot, reps[0].R.Deficit, reps, where);
                else
                    s_Sim.PlantSetSkipped++;
            }
            SettleChecks(w, run, target, level, last, where, book);
            return reps;
        }


        private static void ApplySimTests(List<SimRun> runs, int seeds)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int bigSeeds = Math.Max(1, seeds / 100);
            int smallSeeds = Math.Max(3, seeds / 50);
            foreach (SimRun run in runs)
            {
                bool small = run.Name == "L" || run.Name == "rect100x300";
                bool field = run.Name == "run124k" || run.Name == "run140k";
                int n = small ? smallSeeds : field ? Math.Max(2, bigSeeds) : bigSeeds;
                if (run.Name == "run800k")
                    n = 1;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "INFO  applysim fixture {0}: area {1:F0} m², perimeter {2:F0} m, {3} wild inside ({4:F1} / ha), T per level {5}",
                    run.Name, run.OracleArea, run.OraclePerimeter, run.WildInside, run.WildInside / (run.OracleArea / 10000.0), TargetsText(run)));
                for (int s = 0; s < n; s++)
                {
                    string tag = run.Name + "/" + s;
                    Guard(tag + "/levels", () => LevelScenarios(run, s));
                    if (run.Name == "run800k" || run.Name == "saturated")
                        continue;
                    Guard(tag + "/updown", () => UpDownScenario(run, s));
                    Guard(tag + "/perturb", () => PerturbScenarios(run, s));
                    if (s == 0)
                        Guard(tag + "/editoor", () => EditOorScenarios(run, s));
                    if (run.Name != "run127k")
                        Guard(tag + "/walk", () => WalkScenario(run, s, WalkSteps));
                }
            }
            SimRun r284 = runs.Find(x => x.Name == "run284k");
            SimRun r800 = runs.Find(x => x.Name == "run800k");
            SimRun sat = runs.Find(x => x.Name == "saturated");
            Guard("applysim/cap800k", () => Cap800kTest(r800));
            Guard("applysim/fellcap", () => FellCapTest(r284, 0, 150));
            Guard("applysim/fellcap140", () => FellCapTest(runs.Find(x => x.Name == "run140k"), 1, 100));
            Guard("applysim/saturated", () => SaturatedTest(sat, 0));
            Guard("applysim/capsat", () => CapSaturatedTest(sat, 0));
            Guard("applysim/cache", () => CacheTest(r284, 0));
            Guard("applysim/cache124", () => CacheTest(runs.Find(x => x.Name == "run124k"), 1));

            SimAgg a = s_Sim;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  applysim: {0} scenario runs, {1} composes, {2} applies, {3} walk steps, {4} stream slots matched by the sweep oracle, {5:F1} s",
                a.Scenarios, a.Composes, a.Applies, a.WalkSteps, a.MatchChecks, sw.Elapsed.TotalSeconds));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  applysim branches: plant {0}, keep/remove {1}, fell {2} ({3} together with the last member removals), Others present {4}; shortfalls {5}, saturated composes {6}, slice-edge Clamped {7}",
                a.PlantBranch, a.KeepRemoveBranch, a.FellBranch, a.FellWithRemove, a.OthersBranch, a.Shortfalls, a.SaturatedComposes, a.SliceClampEdge));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  applysim checks: plant sets {0} (skipped {1}: members off the stream), up/down {2} (walk up-steps not tracked: {3}, a hole below members), nested {4}, keys kept across the other layer's applies {5}, tree-line key changed by near-edge fells {6}; T near .5: {7}",
                a.PlantSetChecks, a.PlantSetSkipped, a.UpDownChecks, a.UpDownHoleSkips, a.NestedChecks, a.KeyKeptChecks, a.KeyChangedNearEdge, a.TargetNearHalf));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  applysim stream: {0} full re-plan(s) (slice out of attempts), {1} full re-plan(s) to order members, {2} cache hit(s); {3} plant(s) canceled ({4} by the live-tree stand-in)",
                a.FullReplans, a.FullForMembers, a.CacheHits, a.Cancelled, a.LiveCancels));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  applysim.verbatim: of {0} composes that needed a full re-plan, a verbatim slice would have reported a FALSE Complete on {1} and planted fewer on {2}",
                a.VerbatimChecked, a.VerbatimFalseComplete, a.VerbatimShort));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "INFO  applysim G3 fixer: misplaced member sightings [interior target: interior {0}, tree line {1}; tree-line target: interior {2}, tree line {3}]; " +
                "level-1 composes with off-stream members [interior {4}, tree line {5}]; editoor widen {6} / narrow {7} case(s), {8} with interior strays beyond the interior band; " +
                "{9} interior L8 settle(s) reached T despite canceled spots (stream headroom); {10} composes repeated into the reused result",
                a.Misplaced[0, 0], a.Misplaced[0, 1], a.Misplaced[1, 0], a.Misplaced[1, 1], a.L1OffStream[0], a.L1OffStream[1],
                a.EditOorWiden, a.EditOorNarrow, a.EditOorBeyondInteriorBand, a.L8HeadroomUsed, a.SharedResultChecks));
            // Power: every branch and every refinement was exercised.
            Check(a.PlantBranch > 0 && a.KeepRemoveBranch > 0 && a.FellBranch > 0 && a.OthersBranch > 0, "applysim/power", "a count-rule branch was never exercised");
            Check(a.FellWithRemove > 0, "applysim/power", "no apply removed the last members and felled together");
            Check(a.PlantSetChecks > 0 && a.UpDownChecks > 0 && a.NestedChecks > 0 && a.KeyKeptChecks > 0, "applysim/power", "a world-level check never ran");
            Check(a.FullReplans > 0, "applysim/power", "no compose needed a full re-plan (slice out of attempts) — capsat has no power");
            Check(a.FullForMembers > 0, "applysim/power", "no compose re-planned in full to order members — the holes scenario has no power");
            Check(a.Shortfalls > 0 && a.SaturatedComposes > 0, "applysim/power", "no Shortfall / Saturated compose");
            Check(a.VerbatimFalseComplete + a.VerbatimShort > 0, "applysim/control", "a verbatim slice never fell short — the full re-plan is not shown to be needed");
            // Power: every misplaced kind on both targets (editoor), level 1 on
            // off-stream members of both targets (perturbations settled at L1), the reused result,
            // the near-end rule.
            Check(a.Misplaced[0, 0] > 0 && a.Misplaced[0, 1] > 0 && a.Misplaced[1, 0] > 0 && a.Misplaced[1, 1] > 0, "applysim/power",
                "a misplaced kind was never seen [interior target: interior " + a.Misplaced[0, 0] + ", tree line " + a.Misplaced[0, 1] +
                "; tree-line target: interior " + a.Misplaced[1, 0] + ", tree line " + a.Misplaced[1, 1] + "] — editoor has no power");
            Check(a.L1OffStream[0] > 0 && a.L1OffStream[1] > 0, "applysim/power", "no level-1 compose with off-stream members (interior " +
                a.L1OffStream[0] + ", tree line " + a.L1OffStream[1] + ") — finding compose-2 not covered");
            Check(a.EditOorWiden > 0 && a.EditOorNarrow > 0 && a.EditOorBeyondInteriorBand > 0, "applysim/power", "editoor cases: widen " + a.EditOorWiden +
                ", narrow " + a.EditOorNarrow + ", beyond the interior band " + a.EditOorBeyondInteriorBand);
            Check(a.SharedResultChecks > 0 && a.L8HeadroomUsed > 0, "applysim/power", "reused-result checks " + a.SharedResultChecks +
                ", interior L8 settles that reached T despite canceled spots " + a.L8HeadroomUsed + " (finding behavior-2 not covered)");
        }

        private static string TargetsText(SimRun run)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int l = 1; l <= 8; l++)
            {
                if (l > 1)
                    sb.Append('/');
                sb.Append(((int)Math.Floor(OracleDensityPerHa[l - 1] / 10000.0 * run.OracleArea + 0.5)).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static void Guard(string where, Action a)
        {
            try
            {
                s_Sim.Scenarios++;
                a();
            }
            catch (Exception e)
            {
                Fail(where, "exception " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            }
        }

        private static void LevelScenarios(SimRun run, int seed)
        {
            KeyBook book = new KeyBook();
            World bare = MakeWorld(run, seed, true);
            World forested = MakeWorld(run, seed);
            World planted = forested.Clone();
            Settle(planted, run, SimTarget.Interior, 8, Caps(TCap), run.Name + "/" + seed + "/plant8", book);
            Settle(planted, run, SimTarget.TreeLine, 8, Caps(TCap), run.Name + "/" + seed + "/plant8belt", book);
            Check(CountOwn(planted, run, 0, false, true) > 0 && CountOwn(planted, run, 1, false, true) > 0, run.Name + "/" + seed + "/planted",
                "the already-planted start holds no interior or no belt members");
            string[] names = { "bare", "forested", "planted" };
            World[] starts = { bare, forested, planted };
            for (int si = 0; si < starts.Length; si++)
            {
                foreach (SimTarget target in new[] { SimTarget.Interior, SimTarget.TreeLine })
                {
                    for (int level = 1; level <= 8; level++)
                    {
                        string where = run.Name + "/" + seed + "/levels-" + names[si] + "/" + target + "/L" + level;
                        World w = starts[si].Clone();
                        List<ApplyReport> reps = Settle(w, run, target, level, Caps(TCap), where, book);
                        // Raising from a member-less start only plants; lowering from the
                        // planted (level 8) start only removes / fells.
                        foreach (ApplyReport rep in reps)
                        {
                            if (si < 2)
                                Check(rep.Removed == 0, where, "a start without members removed " + rep.Removed);
                            if (si == 2)
                                Check(rep.Planted + rep.Cancelled == 0, where, "LOWER ONLY REMOVES: from level 8 an apply at L" + level + " planted " + rep.Planted);
                        }
                    }
                }
            }

            // Nested fells on the forested stand (no members): Fell(L) ⊇ Fell(L + 1).
            HashSet<int> prev = null;
            for (int level = 8; level >= 1; level--)
            {
                string where = run.Name + "/" + seed + "/nested/L" + level;
                CensusOut c = Census(forested, run, SimTarget.Interior);
                ComposeResult r = ComposeOnly(forested, run, SimTarget.Interior, level, BigCaps(), where, book);
                HashSet<int> ids = new HashSet<int>();
                foreach (int i in r.Fell)
                    ids.Add(c.ExistingIds[i]);
                if (prev != null)
                {
                    Check(ids.IsSupersetOf(prev), where, "NESTED: the fell set at L" + level + " (" + ids.Count + ") does not contain L" + (level + 1) + "'s (" + prev.Count + ")");
                    s_Sim.NestedChecks++;
                }
                prev = ids;
            }
        }

        private static void UpDownScenario(SimRun run, int seed)
        {
            KeyBook book = new KeyBook();
            System.Random rnd = new System.Random(seed * 97 + StableHash(run.Name));
            foreach (SimTarget target in new[] { SimTarget.Interior, SimTarget.TreeLine })
            {
                for (int rep = 0; rep < 4; rep++)
                {
                    World w = MakeWorld(run, seed + rep);
                    int a = 1 + rnd.Next(7);
                    int b = a + 1 + rnd.Next(8 - a);
                    string where = run.Name + "/" + seed + "/updown-" + target + "/" + a + "-" + b + "-" + a;
                    Settle(w, run, target, a, Caps(TCap), where + "/a", book);
                    UpDownOnce(w, run, target, a, b, where, book);
                }
            }
        }

        private static void UpDownOnce(World w, SimRun run, SimTarget target, int a, int b, string where, KeyBook book)
        {
            OracleStreamEntry po;
            int[] perSlot;
            bool onStream = MembersOnStream(w, run, target, out po, out perSlot);
            HashSet<int> wildBefore = KindIds(w, TreeKind.Wild);
            List<ApplyReport> up = Settle(w, run, target, b, Caps(TCap), where + "/up", book);
            HashSet<int> plantedUp = new HashSet<int>();
            foreach (ApplyReport r in up)
            {
                Check(r.Removed == 0 && r.Felled == 0, where, "RAISE ONLY PLANTS: the up-step removed " + r.Removed + " / felled " + r.Felled);
                plantedUp.UnionWith(r.PlantedIds);
            }
            List<ApplyReport> down = Settle(w, run, target, a, Caps(TCap), where + "/down", book);
            HashSet<int> removed = new HashSet<int>();
            int felled = 0;
            foreach (ApplyReport r in down)
            {
                Check(r.Planted + r.Cancelled == 0, where, "LOWER ONLY REMOVES: the down-step planted " + r.Planted);
                removed.UnionWith(r.RemovedIds);
                felled += r.Felled;
            }
            Check(KindIds(w, TreeKind.Wild).SetEquals(wildBefore), where, "up then down changed the wild stand (felled " + felled + ")");
            if (onStream)
            {
                Check(removed.SetEquals(plantedUp), where, "UP/DOWN: the down-step removed " + removed.Count + " member(s), the up-step planted " +
                    plantedUp.Count + " (not the same set)");
                s_Sim.UpDownChecks++;
            }
        }

        private static void WalkScenario(SimRun run, int seed, int steps)
        {
            KeyBook book = new KeyBook();
            System.Random rnd = new System.Random(seed * 131 + 7 + StableHash(run.Name));
            World w = MakeWorld(run, seed);
            int[] settled = { 0, 0 };                       // per target: last settled level (0 = none)
            int[] upFrom = { 0, 0 };                        // level before the last pure up-step
            HashSet<int>[] upPlanted = { null, null };
            ulong[] upKey = { 0UL, 0UL };                   // the target's PlanKey at that up-step
            List<string> history = new List<string>();      // diagnostics of a failed identity
            Dictionary<int, float3> idPos = new Dictionary<int, float3>();
            for (int step = 0; step < steps; step++)
            {
                s_Sim.WalkSteps++;
                SimTarget target = rnd.NextDouble() < 0.7 ? SimTarget.Interior : SimTarget.TreeLine;
                int ti = (int)target;
                int level = rnd.NextDouble() < 0.5 || settled[ti] == 0 ? 1 + rnd.Next(8) : Math.Max(1, Math.Min(8, settled[ti] + rnd.Next(-2, 3)));
                if (upPlanted[ti] != null && rnd.NextDouble() < 0.35)
                    level = upFrom[ti]; // go back down to test the up/down identity
                string where = run.Name + "/" + seed + "/walk/" + step + "/" + target + "/L" + level;

                // The other layer's PlanKey before the step (layer independence of the keys).
                SimTarget other = Other(target);
                ulong otherKey = SkiGladeCompose.PlanKey(BuildInput(run, other, 4, Census(w, run, other), Caps(TCap)));
                OracleStreamEntry po;
                int[] perSlot;
                bool onStream = MembersOnStream(w, run, target, out po, out perSlot);
                bool noHoles = onStream && NoHoles(run, po, perSlot);
                ulong targetKey = SkiGladeCompose.PlanKey(BuildInput(run, target, 4, Census(w, run, target), Caps(TCap)));
                Dictionary<int, float3> wildPos = new Dictionary<int, float3>();
                foreach (WTree t in w.Trees)
                {
                    if (t.Kind == TreeKind.Wild)
                        wildPos[t.Id] = t.Pos;
                    idPos[t.Id] = t.Pos;
                }
                history.Add(step + ":" + (target == SimTarget.Interior ? "I" : "B") + level);

                List<ApplyReport> reps = Settle(w, run, target, level, Caps(TCap), where, book);
                bool planted = false, removedAny = false, felledAny = false;
                HashSet<int> plantedIds = new HashSet<int>(), removedIds = new HashSet<int>();
                List<int> felledIds = new List<int>();
                foreach (ApplyReport r in reps)
                {
                    planted |= r.Planted + r.Cancelled > 0;
                    removedAny |= r.Removed > 0;
                    felledAny |= r.Felled > 0;
                    plantedIds.UnionWith(r.PlantedIds);
                    removedIds.UnionWith(r.RemovedIds);
                    felledIds.AddRange(r.FelledIds);
                }

                // Up/down identity: a pure down-step back to the level before a pure up-step, on the SAME stream (target PlanKey unchanged since
                // the up-step), where the up-step started with its members forming a prefix of the stream (no unblocked hole below a member: an
                // interior fell near the edge can INSERT a tree-line slot, since the planner's per-tree draws equal one attempt's and later slots
                // survive shifted by one, and the up-step then rightly fills that hole first).
                if (upPlanted[ti] != null && level == upFrom[ti] && settled[ti] > level && targetKey == upKey[ti] && !planted && !felledAny)
                {
                    if (!Check(removedIds.SetEquals(upPlanted[ti]), where, "UP/DOWN (walk): removed " + removedIds.Count + ", the up-step planted " + upPlanted[ti].Count))
                        Console.WriteLine("      walk history " + string.Join(" ", history) + "; slots removed " + SlotsOf(idPos, removedIds, po) +
                                          " / planted " + SlotsOf(idPos, upPlanted[ti], po) + " (stream " + po.Slots.Count + ")");
                    s_Sim.UpDownChecks++;
                }
                if (settled[ti] > 0 && level > settled[ti] && planted && !removedAny && !felledAny && !noHoles)
                    s_Sim.UpDownHoleSkips++;
                if (settled[ti] > 0 && level > settled[ti] && planted && !removedAny && !felledAny && noHoles)
                {
                    upFrom[ti] = settled[ti];
                    upPlanted[ti] = plantedIds;
                    upKey[ti] = targetKey; // a plant never changes its own layer's key
                }
                else
                {
                    upPlanted[ti] = null;
                }
                settled[ti] = level;

                // Key independence: a tree-line step never changes the interior key; an interior
                // step changes the tree-line key exactly when it fells a wild tree nearer the edge
                // than BeltMinSpacing − BeltGap (+ 1 cm) — the only interior change that can move
                // a tree-line slot (the documented exception).
                ulong otherKeyAfter = SkiGladeCompose.PlanKey(BuildInput(run, other, 4, Census(w, run, other), Caps(TCap)));
                if (target == SimTarget.TreeLine)
                {
                    Check(otherKeyAfter == otherKey, where, "a tree-line apply changed the interior PlanKey");
                    s_Sim.KeyKeptChecks++;
                }
                else
                {
                    bool nearEdge = false;
                    foreach (int id in felledIds)
                    {
                        float3 p = wildPos[id];
                        if (OracleBoundaryDistance(run.Oracle, new double2(p.x, p.z)) < TBeltMinSpacing - TBeltGap + 0.01)
                            nearEdge = true;
                    }
                    Check((otherKeyAfter != otherKey) == nearEdge, where, "tree-line PlanKey changed " + (otherKeyAfter != otherKey) +
                        " but a near-edge wild tree was felled " + nearEdge);
                    if (nearEdge)
                        s_Sim.KeyChangedNearEdge++;
                    else
                        s_Sim.KeyKeptChecks++;
                }
                // Planted and felled are in different settles only (never both in one apply is
                // asserted per apply; a settle may remove members and fell in the same apply).
                Check(!(planted && (removedAny || felledAny)), where, "one settle both planted and removed/felled");
            }
        }

        private static bool NoHoles(SimRun run, OracleStreamEntry po, int[] perSlot)
        {
            int max = -1;
            for (int s = 0; s < perSlot.Length; s++)
            {
                if (perSlot[s] > 0)
                    max = s;
            }
            for (int s = 0; s < max; s++)
            {
                if (perSlot[s] == 0 && !run.Blocks(po.Slots[s].Position))
                    return false;
            }
            return true;
        }

        private static string SlotsOf(Dictionary<int, float3> idPos, HashSet<int> ids, OracleStreamEntry po)
        {
            List<int> ordered = new List<int>(ids);
            ordered.Sort();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (int id in ordered)
            {
                float3 p;
                int slot = -1;
                if (idPos.TryGetValue(id, out p))
                {
                    for (int s = 0; s < po.Slots.Count; s++)
                    {
                        double dx = (double)po.Slots[s].Position.x - p.x, dz = (double)po.Slots[s].Position.z - p.z;
                        if (dx * dx + dz * dz < 4.0)
                        {
                            slot = s;
                            break;
                        }
                    }
                }
                sb.Append(id).Append('@').Append(slot >= 0 ? slot.ToString(CultureInfo.InvariantCulture) : "-").Append(' ');
            }
            return sb.ToString();
        }

        private static void PerturbScenarios(SimRun run, int seed)
        {
            KeyBook book = new KeyBook();
            string tag = run.Name + "/" + seed;
            World f = MakeWorld(run, seed);
            // A level with members: the lowest level whose T exceeds the stand by >= 20 trees.
            int lv = 8;
            for (int l = 2; l <= 8; l++)
            {
                int t = (int)Math.Floor(OracleDensityPerHa[l - 1] / 10000.0 * run.OracleArea + 0.5);
                if (t >= run.WildInside + run.ForeignInside + 20)
                {
                    lv = l;
                    break;
                }
            }
            World settled = f.Clone();
            Settle(settled, run, SimTarget.Interior, lv, Caps(TCap), tag + "/perturb-settle", book);
            Settle(settled, run, SimTarget.TreeLine, 5, Caps(TCap), tag + "/perturb-settle-belt", book);

            foreach (SimTarget target in new[] { SimTarget.Interior, SimTarget.TreeLine })
            {
                byte layer = LayerOf(target);
                int level = target == SimTarget.Interior ? lv : 5;
                // ---- repair: bulldozed members' slots are replanted exactly.
                {
                    World w = settled.Clone();
                    string where = tag + "/repair-" + target;
                    List<WTree> members = w.Trees.FindAll(t => t.Kind == TreeKind.Member && t.Run == OurRun && t.Layer == layer);
                    System.Random rnd = new System.Random(seed * 31 + 7);
                    HashSet<int> del = new HashSet<int>();
                    List<float3> delPos = new List<float3>();
                    while (del.Count < Math.Min(7, members.Count))
                    {
                        WTree t = members[rnd.Next(members.Count)];
                        if (del.Add(t.Id))
                            delPos.Add(t.Pos);
                    }
                    w.RemoveIds(del);
                    ApplyReport rep = ApplyOnce(w, run, target, level, Caps(TCap), where, book);
                    Check(SameList(PosBits(rep.PlantedPos), PosBits(delPos)) && rep.Removed == 0 && rep.Felled == 0 && rep.Cancelled == 0, where,
                        "REPAIR: replanted " + rep.Planted + " (canceled " + rep.Cancelled + ") for " + del.Count + " bulldozed; positions equal " +
                        SameList(PosBits(rep.PlantedPos), PosBits(delPos)));
                    ApplyReport again = ApplyOnce(w, run, target, level, Caps(TCap), where + "-again", book);
                    Check(again.NoOp && again.R.Complete, where, "apply after the repair is not a no-op");
                }
                // ---- holes: settle at level 8, bulldoze members on the first 30 % of the stream,
                // then LOWER the level (interior 7, tree line 4: still keeps members): the members
                // pushed beyond Same + |Skip| must still be ordered by slot (full re-plan).
                {
                    World w = f.Clone();
                    string where = tag + "/holes-" + target;
                    Settle(w, run, target, 8, Caps(TCap), where + "-L8", book);
                    CensusOut c = Census(w, run, target);
                    OracleStreamEntry po = OracleStream(c, run, target);
                    List<WTree> members = w.Trees.FindAll(t => t.Kind == TreeKind.Member && t.Run == OurRun && t.Layer == layer);
                    // the members on the first 30 % of the stream
                    HashSet<int> del = new HashSet<int>();
                    int lim = Math.Max(1, po.Slots.Count * 3 / 10);
                    double2[] early = new double2[Math.Min(lim, po.Slots.Count)];
                    for (int s = 0; s < early.Length; s++)
                        early[s] = new double2(po.Slots[s].Position.x, po.Slots[s].Position.z);
                    double2[] mxz = new double2[members.Count];
                    for (int k = 0; k < members.Count; k++)
                        mxz[k] = new double2(members[k].Pos.x, members[k].Pos.z);
                    int[] hit = SweepCounts(mxz, early, (target == SimTarget.Interior ? TGladeMinSpacing : TBeltMinSpacing) * 0.5);
                    for (int k = 0; k < members.Count && del.Count < Math.Max(3, members.Count / 4); k++)
                    {
                        if (hit[k] > 0)
                            del.Add(members[k].Id);
                    }
                    w.RemoveIds(del);
                    Settle(w, run, target, target == SimTarget.Interior ? 7 : 4, Caps(TCap), where, book);
                }
                // ---- moved: one member moved 3 m / 20 m inside its region → no-op.
                foreach (double dist in new[] { 3.0, 20.0 })
                {
                    World w = settled.Clone();
                    string where = tag + "/moved" + dist + "-" + target;
                    System.Random rnd = new System.Random(seed * 17 + (int)dist);
                    int idx = -1;
                    float3 to = default;
                    for (int attempt = 0; attempt < 200 && idx < 0; attempt++)
                    {
                        int i = rnd.Next(w.Trees.Count);
                        WTree t = w.Trees[i];
                        if (t.Kind != TreeKind.Member || t.Run != OurRun || t.Layer != layer)
                            continue;
                        for (int d = 0; d < 8; d++)
                        {
                            double ang = d * Math.PI / 4 + rnd.NextDouble() * 0.1;
                            double x = t.Pos.x + dist * Math.Cos(ang), z = t.Pos.z + dist * Math.Sin(ang);
                            float3 cand = new float3((float)x, GroundY(x, z), (float)z);
                            if (InRegion(run, layer, cand))
                            {
                                idx = i;
                                to = cand;
                                break;
                            }
                        }
                    }
                    if (idx < 0)
                        continue;
                    WTree mv = w.Trees[idx];
                    mv.Pos = to;
                    w.Trees[idx] = mv;
                    ApplyReport rep = ApplyOnce(w, run, target, level, Caps(TCap), where, book);
                    Check(rep.NoOp && rep.R.Complete, where, "MOVED: a moved member changed the count rule's answer (plant " + rep.Planted + ", remove " + rep.Removed + ", fell " + rep.Felled + ")");
                    // and lowering the level still removes by slot (the moved one is unmatched → last)
                    Settle(w, run, target, Math.Max(1, level - 1), Caps(TCap), where + "-lower", book);
                    LowLevels(w, run, target, where, book);
                }
                // ---- Other members (v1 tags, a bumped generation): removed first of all.
                {
                    World w = settled.Clone();
                    string where = tag + "/others-" + target;
                    CensusOut c = Census(w, run, target);
                    OracleStreamEntry po = OracleStream(c, run, target);
                    int added = 0;
                    for (int s = po.Slots.Count - 1; s >= 0 && added < 25; s -= 3)
                    {
                        float3 p = po.Slots[s].Position;
                        if (run.Blocks(p))
                            continue;
                        bool v1 = (added % 2) == 0;
                        w.Add(p, TreeKind.Member, OurRun, layer, v1 ? SkiGladeCompose.VariantUnknown : (byte)0, v1 ? (byte)0 : (byte)(SkiGladeCompose.PlanGeneration + 1));
                        added++;
                    }
                    // Too few at a higher level: the Others go first and the plant waits.
                    ApplyReport first = ApplyOnce(w, run, target, Math.Min(8, level + 2), Caps(TCap), where, book);
                    bool waits = first.R.Inside < first.R.Target;
                    Check(first.R.Other == added && first.Removed == added && first.Planted + first.Cancelled == 0 && first.R.Clamped == waits, where,
                        "OTHERS FIRST: removed " + first.Removed + " of " + added + " Other member(s) (Compose saw " + first.R.Other + "), planted " +
                        first.Planted + ", clamped " + first.R.Clamped + " (a plant waits: " + waits + ")");
                    Settle(w, run, target, Math.Min(8, level + 2), Caps(TCap), where + "-settle", book);
                    Check(CountOthersAnywhere(w, run, layer) == 0, where, "an Other member survived");
                }
                // ---- foreign: one extra foreign tree in the layer region → the count rule (one
                // member less, or one wild tree), then the count holds.
                {
                    World w = settled.Clone();
                    string where = tag + "/foreign-" + target;
                    System.Random rnd = new System.Random(seed * 13 + 3);
                    for (int i = 0; i < 100000; i++)
                    {
                        double x = run.Min.x - 14 + rnd.NextDouble() * (run.Max.x - run.Min.x + 28);
                        double z = run.Min.y - 14 + rnd.NextDouble() * (run.Max.y - run.Min.y + 28);
                        float3 p = new float3((float)x, GroundY(x, z), (float)z);
                        if (!InRegion(run, layer, p))
                            continue;
                        w.Add(p, TreeKind.Foreign, OtherRun, 0, 0, SkiGladeCompose.PlanGeneration);
                        break;
                    }
                    Settle(w, run, target, level, Caps(TCap), where, book);
                    LowLevels(w, run, target, where, book);
                }
                // ---- edit: one vertex moved 1 m out / 4 m in → the count rule on the new ring.
                foreach (int vtx in new[] { 0, run.Ring.Length / 2 })
                {
                    foreach (double move in new[] { 1.0, -4.0 })
                    {
                        World w = settled.Clone();
                        string where = tag + "/edit" + vtx + (move > 0 ? "out" : "in") + "-" + target;
                        float3[] ring = (float3[])run.Ring.Clone();
                        double2 cc = (run.Min + run.Max) * 0.5;
                        double2 v = new double2(ring[vtx].x, ring[vtx].z);
                        double2 nv = v + math.normalizesafe(v - cc) * move;
                        ring[vtx] = new float3((float)nv.x, GroundY(nv.x, nv.y), (float)nv.y);
                        SimRun edited = WithRing(run, ring);
                        Settle(w, edited, target, level, Caps(TCap), where, book);
                        LowLevels(w, edited, target, where, book);
                    }
                }
            }
        }

        /// <summary>From a perturbed, settled world (members partly off the
        /// stream), settle level 1 on a copy and level 2 on the world itself, so level 1 and the
        /// keep order run with OwnUnmatched > 0 (counted in L1OffStream).</summary>
        private static void LowLevels(World w, SimRun run, SimTarget target, string where, KeyBook book)
        {
            World w1 = w.Clone();
            Settle(w1, run, target, 1, Caps(TCap), where + "-L1", book);
            Settle(w, run, target, 2, Caps(TCap), where + "-L2", book);
        }

        /// <summary>The edit-oor case: a run edit that moves the edge past this run's own trees. WIDEN 8 m (> BeltGap) after a tree line
        /// at L8: belt members end up inside the run. NARROW 8 m and 12 m (> InnerMargin) after an interior at L6: interior members end
        /// up outside (12 m: some beyond the interior census band, which only the tree line's band reaches). Each from the same settled
        /// start: level 1 of each target (SettleChecks then requires NO tree of this run in that target's census), the other target
        /// first, and the original levels again, after which no misplaced member may stand in either census.</summary>
        private static void EditOorScenarios(SimRun run, int seed)
        {
            KeyBook book = new KeyBook();
            string tag = run.Name + "/" + seed + "/editoor";
            SimRun wide = WithRing(run, OffsetRing(run.Ring, 8.0));
            World s = MakeWorld(run, seed);
            Settle(s, run, SimTarget.Interior, 4, Caps(TCap), tag + "/settle-I4", book);
            Settle(s, run, SimTarget.TreeLine, 8, Caps(TCap), tag + "/settle-B8", book);
            int swallowed = CountBeltInside(s, wide);
            Check(swallowed > 0, tag + "/widen", "widening 8 m put no tree-line member inside the run (no power)");
            if (swallowed > 0)
                s_Sim.EditOorWiden++;
            {
                World a = s.Clone();
                string where = tag + "/widen-I1-B1";
                Settle(a, wide, SimTarget.Interior, 1, Caps(TCap), where + "/I1", book);
                Check(CountBeltInside(a, wide) == 0, where, "INTERIOR L1 left " + CountBeltInside(a, wide) + " of this run's tree-line trees inside the widened run");
                Settle(a, wide, SimTarget.TreeLine, 1, Caps(TCap), where + "/B1", book);
            }
            {
                World b = s.Clone();
                string where = tag + "/widen-B1-I1";
                Settle(b, wide, SimTarget.TreeLine, 1, Caps(TCap), where + "/B1", book);
                Check(CountBeltInside(b, wide) == 0, where, "TREE LINE L1 left " + CountBeltInside(b, wide) + " of its trees inside the widened run");
                Settle(b, wide, SimTarget.Interior, 1, Caps(TCap), where + "/I1", book);
            }
            {
                World c = s.Clone();
                string where = tag + "/widen-I4-B8";
                Settle(c, wide, SimTarget.Interior, 4, Caps(TCap), where + "/I4", book);
                Check(CountBeltInside(c, wide) == 0, where, "INTERIOR L4 left " + CountBeltInside(c, wide) + " tree-line trees inside the widened run");
                Settle(c, wide, SimTarget.TreeLine, 8, Caps(TCap), where + "/B8", book);
                Check(CountInteriorCensusOwn(c, wide) == CountOwn(c, wide, 0, false, true), where,
                    "after both targets: this run's trees in the interior census are not exactly its current interior members");
            }

            World s2 = MakeWorld(run, seed);
            Settle(s2, run, SimTarget.Interior, 6, Caps(TCap), tag + "/settle-I6", book);
            Settle(s2, run, SimTarget.TreeLine, 5, Caps(TCap), tag + "/settle-B5", book);
            foreach (double d in new[] { 8.0, 12.0 })
            {
                SimRun narrow = WithRing(run, OffsetRing(run.Ring, -d));
                string nt = tag + "/narrow" + d;
                int strays = CountInteriorStrays(s2, narrow, TBeltDepth + TBeltMinSpacing);
                int beyond = strays - CountInteriorStrays(s2, narrow, TGladeMinSpacing);
                Check(strays > 0, nt, "narrowing " + d + " m left no interior member outside the run (no power)");
                if (strays > 0)
                    s_Sim.EditOorNarrow++;
                if (beyond > 0)
                    s_Sim.EditOorBeyondInteriorBand++;
                {
                    World a = s2.Clone();
                    string where = nt + "-I1-B5";
                    Settle(a, narrow, SimTarget.Interior, 1, Caps(TCap), where + "/I1", book);
                    Settle(a, narrow, SimTarget.TreeLine, 5, Caps(TCap), where + "/B5", book);
                    int left = CountInteriorStrays(a, narrow, TBeltDepth + TBeltMinSpacing);
                    Check(left == 0, where, "after Interior L1 and Tree line L5, " + left + " interior stray(s) stand within the tree-line band (" +
                        beyond + " started beyond the interior band)");
                }
                {
                    World b = s2.Clone();
                    string where = nt + "-B1";
                    Settle(b, narrow, SimTarget.TreeLine, 1, Caps(TCap), where + "/B1", book);
                }
                {
                    World c = s2.Clone();
                    string where = nt + "-I6";
                    Settle(c, narrow, SimTarget.Interior, 6, Caps(TCap), where + "/I6", book);
                    int left = CountInteriorStrays(c, narrow, TGladeMinSpacing);
                    Check(left == 0, where, "INTERIOR L6 left " + left + " interior stray(s) within the interior census band");
                }
            }
        }


        /// <summary>run800k: plant slices of 4,000 at level 8, then removal slices of 4,000 at
        /// level 1 with the wild fell only once no member stands.</summary>
        private static void Cap800kTest(SimRun run)
        {
            if (run == null)
                return;
            KeyBook book = new KeyBook();
            string where = run.Name + "/cap800k";
            World w = MakeWorld(run, 0);
            List<ApplyReport> up = Settle(w, run, SimTarget.Interior, 8, Caps(TCap), where + "-L8", book);
            int sliceApplies = 0;
            for (int i = 0; i < up.Count; i++)
            {
                ApplyReport r = up[i];
                bool lastOne = i == up.Count - 1;
                if (lastOne)
                    continue;
                if (r.R.Clamped)
                {
                    sliceApplies++;
                    Check(r.Planted + r.Cancelled == TCap, where, "plant slice " + i + " covered " + (r.Planted + r.Cancelled) + " slots (clamped)");
                }
                else
                {
                    // An un-clamped apply is followed by another only to replace the spots it lost to the blocked
                    // strip (ApplySim learns them at apply time; the stream's headroom has the replacements).
                    Check(r.Cancelled > 0, where, "plant apply " + i + " was not clamped and lost no spot, yet another apply followed");
                }
            }
            Check(sliceApplies >= 2, where, "level 8 on 800k took " + up.Count + " apply(s) — the plant cap never bound twice");
            int t8 = (int)Math.Floor(OracleD8 * run.OracleArea + 0.5);
            Check(CountInsideAll(w, run) == t8, where, "HEADROOM: level 8 settled at " + CountInsideAll(w, run) + " trees inside, T8 " + t8 +
                " — the blocked spots were not replaced from the stream's tail");
            int members = CountOwn(w, run, 0, false, true);
            List<ApplyReport> down = Settle(w, run, SimTarget.Interior, 1, Caps(TCap), where + "-L1", book);
            int removedSoFar = 0;
            for (int i = 0; i < down.Count; i++)
            {
                ApplyReport r = down[i];
                removedSoFar += r.Removed;
                if (removedSoFar < members)
                    Check(r.Removed == TCap && r.Felled == 0 && r.R.Clamped, where, "removal slice " + i + ": removed " + r.Removed + ", felled " + r.Felled + " with " +
                        (members - removedSoFar) + " member(s) left");
            }
            Check(down.Count == (members + TCap - 1) / TCap, where, "level 1 from " + members + " members took " + down.Count + " apply(s), expected " +
                ((members + TCap - 1) / TCap) + " (the last removal slice fells too)");
            Console.WriteLine("INFO  " + where + ": level 8 planted in " + up.Count + " apply(s) (" + members + " members); level 1 removed them in " + down.Count + " apply(s)");
        }

        private static void FellCapTest(SimRun run, int seed, int cap)
        {
            if (run == null)
                return;
            KeyBook book = new KeyBook();
            string where = run.Name + "/fellcap" + cap;
            World w = MakeWorld(run, seed);
            // Expected order: the whole wild stand inside by rank (largest hash first).
            List<WTree> wild = w.Trees.FindAll(t => t.Kind == TreeKind.Wild && OracleInside(run.Oracle, new double2(t.Pos.x, t.Pos.z)));
            wild.Sort((a, b) =>
            {
                int c = OracleThinHash(b.Pos, 0x7A1C3E5Bu).CompareTo(OracleThinHash(a.Pos, 0x7A1C3E5Bu));
                if (c != 0) return c;
                c = a.Pos.x.CompareTo(b.Pos.x);
                if (c != 0) return c;
                return a.Pos.z.CompareTo(b.Pos.z);
            });
            List<ApplyReport> reps = Settle(w, run, SimTarget.Interior, 1, Caps(TCap, cap), where, book);
            int k = 0;
            for (int i = 0; i < reps.Count; i++)
            {
                HashSet<int> expect = new HashSet<int>();
                for (int j = 0; j < cap && k < wild.Count; j++, k++)
                    expect.Add(wild[k].Id);
                Check(reps[i].FelledIds.SetEquals(expect), where, "fell slice " + i + " is not the next " + expect.Count + " by rank");
                Check(reps[i].R.Clamped == (i < reps.Count - 1), where, "fell slice " + i + " Clamped " + reps[i].R.Clamped);
            }
            Check(k == wild.Count && reps.Count == (wild.Count + cap - 1) / cap, where, "fell slices " + reps.Count + " for " + wild.Count + " wild trees at cap " + cap);
        }

        /// <summary>Level 8 on the saturated fixture: no room → Saturated, never Clamped; the
        /// repeat is Complete and still Saturated.</summary>
        private static void SaturatedTest(SimRun run, int seed)
        {
            KeyBook book = new KeyBook();
            string where = run.Name + "/saturated";
            World w = MakeWorld(run, seed);
            ApplyReport r1 = ApplyOnce(w, run, SimTarget.Interior, 8, Caps(TCap), where, book);
            Check(r1.R.Saturated && !r1.R.Clamped && r1.R.Shortfall > 0, where, "level 8 on the saturated fixture: Saturated " + r1.R.Saturated +
                ", Clamped " + r1.R.Clamped + ", shortfall " + r1.R.Shortfall);
            ApplyReport r2 = ApplyOnce(w, run, SimTarget.Interior, 8, Caps(TCap), where, book);
            Check(r2.NoOp && r2.R.Complete && r2.R.Saturated && !r2.R.Clamped, where, "after the apply: Complete " + r2.R.Complete + ", Saturated " + r2.R.Saturated);
            Console.WriteLine("INFO  " + where + ": level 8 wants " + r1.R.Target + " inside, the stream fits " + r1.R.Slice + " slot(s); shortfall " + r1.R.Shortfall);
        }

        /// <summary>Small plant caps on the saturated fixture: slices run out of attempts (full
        /// re-plans), never a stall, the plant set is the stream's first unblocked slots.</summary>
        private static void CapSaturatedTest(SimRun run, int seed)
        {
            foreach (int cap in new[] { 25, 60 })
            {
                KeyBook book = new KeyBook();
                string where = run.Name + "/capsat" + cap;
                World w = MakeWorld(run, seed);
                List<ApplyReport> reps = Settle(w, run, SimTarget.Interior, 8, Caps(cap), where, book);
                Check(reps.Count >= 2, where, "the cap never bound");
            }
        }

        /// <summary>GladePlanCache never changes the outcome: two identical worlds run the same
        /// level sequence with plant cap 300, one cached, one not; every apply compared.</summary>
        private static void CacheTest(SimRun run, int seed)
        {
            if (run == null)
                return;
            string where = run.Name + "/cache/" + seed;
            World wa = MakeWorld(run, seed), wb = MakeWorld(run, seed);
            GladePlanCache cache = new GladePlanCache(4);
            int hits = 0, applies = 0, clampedDiff = 0;
            int[] levels = { 4, 8, 6, 2, 7, 1, 5 };
            foreach (int level in levels)
            {
                foreach (SimTarget target in new[] { SimTarget.Interior, SimTarget.TreeLine })
                {
                    for (int i = 0; i < 60; i++)
                    {
                        ComposeOnly(wa, run, target, level, Caps(300), where, null, null);
                        ComposeResult pb = ComposeOnly(wb, run, target, level, Caps(300), where, null, cache);
                        if (pb.CacheHit)
                            hits++;
                        ApplyReport a = ApplyOnce(wa, run, target, level, Caps(300), where, null, null);
                        ApplyReport b = ApplyOnce(wb, run, target, level, Caps(300), where, null, cache);
                        applies++;
                        if (b.R.CacheHit)
                            hits++;
                        bool same = SamePlanned(a.R.Plan.Planted, b.R.Plan.Planted) && SameInts(a.R.Plant, b.R.Plant) &&
                                    SameInts(a.R.Remove, b.R.Remove) && SameInts(a.R.Fell, b.R.Fell) && a.R.Complete == b.R.Complete &&
                                    a.R.PlanKey == b.R.PlanKey && a.Planted == b.Planted && a.Cancelled == b.Cancelled;
                        if (!Check(same, where, "L" + level + " " + target + " apply " + i + ": cached compose differs (hit " + b.R.CacheHit + ")"))
                            return;
                        if (a.R.Clamped != b.R.Clamped)
                            clampedDiff++; // the documented slice edge (a cached full stream sees its end)
                        if (a.R.Complete)
                            break;
                    }
                }
            }
            Check(hits > 0, where, "the cache never hit (test has no power)");
            Console.WriteLine("INFO  " + where + ": " + hits + " cache hit(s) over " + applies + " apply pair(s); Clamped differed on " +
                              clampedDiff + " (the documented slice edge)");
        }
    }
}
