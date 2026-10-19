// LiftTowerPlanner — PURE tower planner for a lift line (ticket T48 step 1; design docs/research/lift-tower-planner-notes.md, the
// field evidence and build plan docs/research/field/2026-10-19/analysis-towers.md §4, build note
// docs/research/field/2026-10-19/tower-planner-build-1.md). No ECS, no Game types: LiftBatch tests it headless (LiftPlannerTests).
// It runs once per edit (or once per printed diag block), so it is plain C#, not Burst; arrays are reused per planner instance.
//
// INPUT
//   * a terrain sampler (along-line metres from station A, lateral metres; + = right of A→B) — sampled every StepM along the line
//     at the centre and at ±HalfWidthM (the engine's own side samples, CourseSplitSystem.cs:3566-3573, half the net width);
//   * the line length (xz) and the two station anchors' rope heights (world y of the haul rope at the stub);
//   * LiftTowerPlanConstants for the lift type (ladder members, rope offset, carrier drop, floor, max rope height, spans, width, sag);
//   * optionally seed nodes (the output of an earlier plan): they are kept as anchors, which makes re-planning a plan a fixed point.
//
// GROUND MODEL (per 5 m sample)
//   centre = the ground under the line axis (the tower base: our own spawn stands on it, step 3b);
//   high   = max(centre, left, right): the clearance ground. The rope must keep the chair floor over the HIGHER side; this is the
//            engine's "lower elevation side" (cmin of Net.Elevation, SubObjectSystem.cs:980), so the side that governs the engine's
//            ladder also governs ours. A node whose high and low side differ by more than SideHillM is flagged SideHill.
//   rope(t) on a span = straight chord between the two rope points minus the engine's sag: LaneSystem.cs:4128-4134 lowers the
//            bezier's b/c by d·m_Hanging·4/3, which is a parabola 4·d·m_Hanging·t(1-t) (mid-span sag = d·m_Hanging).
//   floor margin(t) = rope(t) - CarrierDropM - high(t) - FloorM (>= 0 required);
//   rope height(t)  = rope(t) - centre(t) (<= MaxRopeAboveGroundM when that is > 0; the tram has no cap).
//
// PHASES
//   (a) anchors: the station-to-station chord (or a seed-to-seed span) that has a violation is split where the terrain forces it.
//       The split is solved EXACTLY (Anchors: a shortest-path search over the samples x ladder members) instead of the greedy
//       recursive worst-intrusion split, which put 18 towers on LP-5's 40 m wall: fewest towers (anchors + the infill (b) will add),
//       then fewest anchors, shortest members, anchors nearest the worst-intrusion sample. The floor test ignores the sag on spans
//       (b) will split (their sub-spans sag far less) and keeps it on spans (b) leaves whole (FloorSag). Anchor spans <= 3 x MaxSpanM.
//       If no clear arrangement exists, the greedy split (Split) minimises what is left. Shoulders on convex ground become
//       breakovers and move one sample past the crest toward the higher ground (uphill of the crest, planner notes "Real-world span
//       lengths", rule 1) when both spans stay clear. Flags: Shoulder (the ground pokes through the chord), Valley (max height),
//       Breakover.
//   (b) infill: a span between anchors gets max(ceil(len / MaxSpanM), round(len / TypicalSpanM)) sub-spans: first at the
//       equal-spacing samples, else each infill node slides inside ±half a sub-span of its spot to the clear spot with the shortest
//       member, nearest the spot. No sub-span exceeds MaxSpanM or 1.5 × TypicalSpanM - StepM (so a re-plan rounds it to one span:
//       idempotence). If no arrangement is clear, one to three extra sub-spans are tried; the best is kept and its violation reported.
//   (c) elevation: every tower starts at the shortest member that keeps the floor at its own node (side-hill); then, while a span
//       has a floor violation, the end nearer the worst sample (the other end if that one is at the top of the ladder or is a
//       station) goes up one member. Members whose rope is above MaxRopeAboveGroundM are never used. If (c) leaves a violation that
//       the (a)/(b) members did not have, the (a)/(b) members are kept.
//   (d) output: ordered nodes (along, ground, high side, member, rope y, flags) + per-span clearance profile, and AppendLog writes
//       a "Towers [plan]" block in the "Towers [nodes]" format of LiftTowerDiagSystem so the two compare line by line.
//
// Deterministic: no randomness, no hash order, ties broken by the sample index.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Ground height (world y) at <paramref name="alongM"/> metres from station A along the line and
    /// <paramref name="lateralM"/> metres to its right.</summary>
    public delegate float LiftTerrainSampler(float alongM, float lateralM);

    public readonly struct LiftTowerMember
    {
        public readonly string Name;   // the tower object prefab (placeholder member) name
        public readonly float TopM;    // ObjectGeometryData m_Size.y = mesh bounds top (the manifests' top_anchor_y)

        public LiftTowerMember(string name, float topM)
        {
            Name = name;
            TopM = topM;
        }
    }

    [Flags]
    public enum LiftPlanNodeFlags
    {
        None = 0,
        Station = 1,      // the two ends (rope height given, no tower of ours)
        Shoulder = 2,     // phase (a): the ground came through the chord (floor split)
        Valley = 4,       // phase (a): the chord was above the max rope height (max-height split)
        Breakover = 8,    // phase (a): a floor split on convex ground (moved uphill of the crest when that was no worse)
        Infill = 16,      // phase (b)
        SideHill = 32,    // high side - low side > SideHillM (the engine ladder would score the uphill side, cmin)
        Seed = 64,        // given by the caller (re-plan)
    }

    public sealed class LiftTowerPlanConstants
    {
        public readonly string TypeId;
        public readonly LiftTowerMember[] Ladder;        // ascending TopM
        public readonly float RopeBelowTopM;             // rope (haul-rope lane y) = base + TopM - RopeBelowTopM
        public readonly float CarrierDropM;              // rope to the carrier's lowest point (catalog HangM)
        public readonly float FloorM;                    // lowest carrier point above the ground, at least
        public readonly float MaxRopeAboveGroundM;       // 0 = no cap
        public readonly float TypicalSpanM;
        public readonly float MaxSpanM;
        public readonly float MinSpanM;
        public readonly float HalfWidthM;                // side samples (half the piece m_Width)
        public readonly float SagRatio;                  // rope lane m_Hanging: mid-span sag = span × SagRatio
        public readonly float EngineElevationCapM;       // PlaceableNet m_ElevationRange.max: informational (engine-ladder path only)
        public readonly float StepM;
        public readonly float SideHillM;
        public readonly float ConvexM;                   // breakover: second difference over ±2 samples below -ConvexM

        public LiftTowerPlanConstants(string typeId, LiftTowerMember[] ladder, float ropeBelowTopM, float carrierDropM, float floorM,
                                      float maxRopeAboveGroundM, float typicalSpanM, float maxSpanM, float minSpanM, float halfWidthM,
                                      float sagRatio, float engineElevationCapM, float stepM = 5f, float sideHillM = 2f, float convexM = 0.5f)
        {
            TypeId = typeId;
            Ladder = ladder;
            RopeBelowTopM = ropeBelowTopM;
            CarrierDropM = carrierDropM;
            FloorM = floorM;
            MaxRopeAboveGroundM = maxRopeAboveGroundM;
            TypicalSpanM = typicalSpanM;
            MaxSpanM = maxSpanM;
            MinSpanM = minSpanM;
            HalfWidthM = halfWidthM;
            SagRatio = sagRatio;
            EngineElevationCapM = engineElevationCapM;
            StepM = stepM;
            SideHillM = sideHillM;
            ConvexM = convexM;
        }

        /// <summary>Rope height above the tower base for ladder member <paramref name="m"/>.</summary>
        public float RopeAboveBase(int m) => Ladder[m].TopM - RopeBelowTopM;

        /// <summary>A copy with other rules (tests and Richard's questions: floor, max rope height, spans).</summary>
        public LiftTowerPlanConstants With(float? floorM = null, float? maxRopeAboveGroundM = null, float? typicalSpanM = null,
                                           float? maxSpanM = null, float? carrierDropM = null)
        {
            return new LiftTowerPlanConstants(TypeId, Ladder, RopeBelowTopM, carrierDropM ?? CarrierDropM, floorM ?? FloorM,
                maxRopeAboveGroundM ?? MaxRopeAboveGroundM, typicalSpanM ?? TypicalSpanM, maxSpanM ?? MaxSpanM, MinSpanM, HalfWidthM,
                SagRatio, EngineElevationCapM, StepM, SideHillM, ConvexM);
        }

        // ── Per-type constants (sources: build note tower-planner-build-1.md §2) ───────────────────────────────────────────────
        // Ladder tops: the tower manifests' top_anchor_y = ObjectGeometryData m_Size.y, confirmed in game by the 2026-10-19 dump
        // (SkiResortMod.Mod.log:632/:669/:789 member lists). Rope offset = shortest top - catalog AxleM (lane y on the shortest tower),
        // which equals every member's top - axle in the manifests (FG 1.974, DQ 1.95, GD 2.0, tram stubs 3.75).
        // Drop = catalog HangM. Floor / max rope / spans = Richard's defaults (2026-10-19). Half width = piece m_Width / 2
        // (gamedata-mirror SkiChairliftPiece / SkiDetachablePiece 6, SkiGondolaPiece 8.2, SkiTramPiece 15). Sag = PIPELINE m_Hanging
        // (FG net SkiChairliftHV 0.03; DQ / GD / tram rope lanes 0.004). Elevation caps (informational): planner notes.

        public static readonly LiftTowerMember[] FixedGripLadder =
        {
            new LiftTowerMember("SkiLiftTowerFG2", 9.014f),    // the FG8 member (prefab keeps its legacy name; PIPELINE line 355)
            new LiftTowerMember("SkiLiftTowerFG12", 13.014f),
            new LiftTowerMember("SkiLiftTowerFG16", 17.014f),
            new LiftTowerMember("SkiLiftTowerFG20", 21.014f),
        };

        public static readonly LiftTowerMember[] DetachableLadder =
        {
            new LiftTowerMember("SkiLiftTowerDQ10", 9.95f),
            new LiftTowerMember("SkiLiftTowerDQ12", 11.95f),
            new LiftTowerMember("SkiLiftTowerDQ14", 13.95f),
            new LiftTowerMember("SkiLiftTowerDQ16", 15.95f),
            new LiftTowerMember("SkiLiftTowerDQ18", 17.95f),
            new LiftTowerMember("SkiLiftTowerDQ20", 19.95f),
        };

        public static readonly LiftTowerMember[] GondolaLadder =
        {
            new LiftTowerMember("SkiLiftTowerGD12", 12f),
            new LiftTowerMember("SkiLiftTowerGD16", 16f),
            new LiftTowerMember("SkiLiftTowerGD20", 20f),
        };

        public static readonly LiftTowerMember[] TramLadder =
        {
            new LiftTowerMember("SkiTramTower30", 30f),
            new LiftTowerMember("SkiTramTower45", 45f),
            new LiftTowerMember("SkiTramTower60", 60f),
        };

        /// <summary>The constants for a catalog lift type (null for an unknown id). Drop and rope offset come from the catalog.</summary>
        public static LiftTowerPlanConstants For(LiftTypeDef def)
        {
            if (def == null)
                return null;
            switch (def.Id)
            {
                case "fixedGrip":
                    return new LiftTowerPlanConstants(def.Id, FixedGripLadder, FixedGripLadder[0].TopM - def.AxleM, def.HangM, 3f, 30f,
                        60f, 90f, 10f, 3f, 0.03f, 16f);
                case "detachable":
                    return new LiftTowerPlanConstants(def.Id, DetachableLadder, DetachableLadder[0].TopM - def.AxleM, def.HangM, 3f, 30f,
                        110f, 245f, 10f, 3f, 0.004f, 10f);
                case "gondola":
                    return new LiftTowerPlanConstants(def.Id, GondolaLadder, GondolaLadder[0].TopM - def.AxleM, def.HangM, 3f, 30f,
                        110f, 245f, 10f, 4.1f, 0.004f, 28f);
                case "tram":
                    return new LiftTowerPlanConstants(def.Id, TramLadder, TramLadder[0].TopM - def.AxleM, def.HangM, 5f, 0f,
                        800f, 1000f, 40f, 7.5f, 0.004f, 30f, 10f);   // 10 m samples: a 1-3 km line stays cheap
                default:
                    return null;
            }
        }

        public static LiftTowerPlanConstants For(string typeId) => For(SkiCatalog.LiftById(typeId));
    }

    public struct LiftPlanNode
    {
        public float AlongM;
        public float GroundM;           // centre ground (the tower base)
        public float HighSideM;         // max(centre, left, right)
        public float LowSideM;          // min(centre, left, right)
        public int Member;              // ladder index; -1 for a station
        public float RopeM;             // world y of the rope at the node
        public LiftPlanNodeFlags Flags;
        public bool IsStation => (Flags & LiftPlanNodeFlags.Station) != 0;
    }

    public struct LiftPlanSpan
    {
        public float LengthM;
        public float MinFloorMarginM;   // min over the samples of rope - drop - high side - floor (>= 0 = clear)
        public float MinFloorMarginAtM; // along, from the span start
        public float MinChairAboveGroundM; // the same sample: lowest carrier point above the high side (margin + floor)
        public float MaxRopeAboveM;     // max of rope - centre ground
        public float MaxRopeAboveAtM;
        public bool FloorViolated;
        public bool HeightViolated;
    }

    public sealed class LiftTowerPlan
    {
        public LiftTowerPlanConstants Constants;
        public float LengthM;
        public LiftPlanNode[] Nodes;
        public LiftPlanSpan[] Spans;
        public float[] SampleAlongM;    // the planner's profile (copied): along, centre ground, high side, per sample
        public float[] SampleCentreM;
        public float[] SampleHighM;
        public int LineTowers => Nodes == null ? 0 : math.max(0, Nodes.Length - 2);

        /// <summary>Rope y on span <paramref name="span"/> at <paramref name="alongM"/> (line metres), with the engine's sag.</summary>
        public float RopeAt(int span, float alongM)
        {
            LiftPlanNode a = Nodes[span], b = Nodes[span + 1];
            float d = b.AlongM - a.AlongM;
            if (d <= 0f)
                return a.RopeM;
            float t = math.saturate((alongM - a.AlongM) / d);
            return a.RopeM + (b.RopeM - a.RopeM) * t - 4f * Constants.SagRatio * d * t * (1f - t);
        }

        /// <summary>Floor margin (rope - drop - high side - floor) at profile sample <paramref name="q"/> on span <paramref name="span"/>.</summary>
        public float MarginAt(int span, int q) =>
            RopeAt(span, SampleAlongM[q]) - Constants.CarrierDropM - SampleHighM[q] - Constants.FloorM;
        public bool Clear
        {
            get
            {
                for (int i = 0; i < Spans.Length; i++)
                    if (Spans[i].FloorViolated || Spans[i].HeightViolated)
                        return false;
                return true;
            }
        }

        public float MinFloorMarginM
        {
            get
            {
                float v = float.PositiveInfinity;
                for (int i = 0; i < Spans.Length; i++)
                    v = math.min(v, Spans[i].MinFloorMarginM);
                return v;
            }
        }

        /// <summary>The "Towers [plan]" block (the "Towers [nodes]" node/span layout of LiftTowerDiagSystem). <paramref name="header"/>
        /// is the text after "Towers [plan]: " on the first line; the caller appends the planned-vs-actual numbers there.</summary>
        public void AppendLog(StringBuilder sb, string header)
        {
            LiftTowerPlanConstants c = Constants;
            sb.Append("Towers [plan]: ").Append(header).Append(" — planned ").Append(Nodes.Length).Append(" node(s), ")
              .Append(LineTowers).Append(" line tower(s), ").Append(Spans.Length).Append(" span(s), ").Append(F(LengthM, "F0"))
              .Append(" m xz; rules ").Append(c.TypeId).Append(": floor ").Append(F(c.FloorM, "F1")).Append(" + drop ")
              .Append(F(c.CarrierDropM, "F1")).Append(", max rope ").Append(c.MaxRopeAboveGroundM > 0f ? F(c.MaxRopeAboveGroundM, "F0") : "none")
              .Append(", span typ ").Append(F(c.TypicalSpanM, "F0")).Append(" / max ").Append(F(c.MaxSpanM, "F0"))
              .Append(", sides ±").Append(F(c.HalfWidthM, "F1")).Append('\n');
            for (int i = 0; i < Nodes.Length; i++)
            {
                LiftPlanNode n = Nodes[i];
                sb.Append("  p").Append(i + 1).Append(" at ").Append(F(n.AlongM, "F0")).Append(" m: ground ").Append(F(n.GroundM, "F2"))
                  .Append(", high side ").Append(Sg(n.HighSideM - n.GroundM)).Append(", low side ").Append(Sg(n.LowSideM - n.GroundM))
                  .Append(", ").Append(n.IsStation ? "station" : "member " + c.Ladder[n.Member].Name + " (top " + F(c.Ladder[n.Member].TopM, "F2") + ")")
                  .Append(", rope y ").Append(F(n.RopeM, "F2")).Append(" (").Append(Sg(n.RopeM - n.GroundM)).Append(" above ground)")
                  .Append(" [").Append(FlagText(n.Flags)).Append("]\n");
                if (i < Spans.Length)
                {
                    LiftPlanSpan s = Spans[i];
                    sb.Append("      span -> p").Append(i + 2).Append(": ").Append(F(s.LengthM, "F0")).Append(" m xz, dy ")
                      .Append(Sg(Nodes[i + 1].RopeM - n.RopeM)).Append(", lowest carrier above ground min ").Append(Sg(s.MinChairAboveGroundM))
                      .Append(" at ").Append(F(s.MinFloorMarginAtM, "F0")).Append(" m (floor margin ").Append(Sg(s.MinFloorMarginM))
                      .Append("), rope max ").Append(Sg(s.MaxRopeAboveM)).Append(" above ground at ").Append(F(s.MaxRopeAboveAtM, "F0"))
                      .Append(" m -> ").Append(s.FloorViolated ? "FLOOR VIOLATED" : s.HeightViolated ? "MAX HEIGHT VIOLATED" : "clear").Append('\n');
                }
            }
        }

        /// <summary>{member x count} for the summary line, shortest first.</summary>
        public string MemberCounts()
        {
            var counts = new int[Constants.Ladder.Length];
            for (int i = 0; i < Nodes.Length; i++)
                if (!Nodes[i].IsStation)
                    counts[Nodes[i].Member]++;
            var sb = new StringBuilder();
            for (int m = 0; m < counts.Length; m++)
            {
                if (counts[m] == 0)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(Constants.Ladder[m].Name).Append(" x").Append(counts[m]);
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        internal static string FlagText(LiftPlanNodeFlags f)
        {
            if (f == LiftPlanNodeFlags.None)
                return "-";
            var sb = new StringBuilder();
            if ((f & LiftPlanNodeFlags.Station) != 0) sb.Append("station,");
            if ((f & LiftPlanNodeFlags.Seed) != 0) sb.Append("seed,");
            if ((f & LiftPlanNodeFlags.Shoulder) != 0) sb.Append("shoulder,");
            if ((f & LiftPlanNodeFlags.Valley) != 0) sb.Append("valley,");
            if ((f & LiftPlanNodeFlags.Breakover) != 0) sb.Append("breakover,");
            if ((f & LiftPlanNodeFlags.Infill) != 0) sb.Append("infill,");
            if ((f & LiftPlanNodeFlags.SideHill) != 0) sb.Append("side-hill,");
            sb.Length--;
            return sb.ToString();
        }

        internal static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
        internal static string Sg(float v) => (v >= 0f ? "+" : string.Empty) + v.ToString("F1", CultureInfo.InvariantCulture);
    }

    public sealed class LiftTowerPlanner
    {
        private const float Tol = 0.01f;
        private const int MaxDepth = 40;

        private LiftTowerPlanConstants m_C;
        private float m_Length;
        private int m_N;                      // samples 0..m_N (m_N + 1 values), S[m_N] = length
        private float[] m_S = new float[0], m_Centre = new float[0], m_High = new float[0], m_Low = new float[0];
        private float m_RopeA, m_RopeB;
        private bool m_UseSag = true;

        // The working node list: sample index, member (-1 station), flags. Kept sorted by sample.
        private readonly List<int> m_K = new List<int>();
        private readonly List<int> m_M = new List<int>();
        private readonly List<LiftPlanNodeFlags> m_F = new List<LiftPlanNodeFlags>();

        /// <summary>Plan a line of <paramref name="lengthM"/> metres (xz) between two stations whose rope heights are given.
        /// <paramref name="seeds"/> (optional) are kept as anchors with their members, e.g. a previous plan's nodes.</summary>
        public LiftTowerPlan Plan(LiftTerrainSampler terrain, float lengthM, float ropeA, float ropeB, LiftTowerPlanConstants c,
                                  IList<LiftPlanNode> seeds = null)
        {
            if (terrain == null || c == null || c.Ladder == null || c.Ladder.Length == 0)
                throw new ArgumentException("LiftTowerPlanner.Plan: terrain, constants and a ladder are required");
            m_C = c;
            m_Length = math.max(lengthM, 0.1f);
            m_RopeA = ropeA;
            m_RopeB = ropeB;
            Sample(terrain);

            m_K.Clear(); m_M.Clear(); m_F.Clear();
            m_K.Add(0); m_M.Add(-1); m_F.Add(LiftPlanNodeFlags.Station);
            m_K.Add(m_N); m_M.Add(-1); m_F.Add(LiftPlanNodeFlags.Station);
            if (seeds != null)
            {
                for (int i = 0; i < seeds.Count; i++)
                {
                    if (seeds[i].IsStation)
                        continue;
                    int k = (int)math.round(seeds[i].AlongM / c.StepM);
                    if (k <= 0 || k >= m_N || IndexOf(k) >= 0)
                        continue;
                    int m = math.clamp(seeds[i].Member, 0, c.Ladder.Length - 1);
                    Insert(k, m, (seeds[i].Flags & ~LiftPlanNodeFlags.Station) | LiftPlanNodeFlags.Seed);
                }
            }

            // (a) anchors: split every violated span (station to station, or between seeds). The floor test ignores the sag here:
            // a long station-to-station chord sags metres that its infilled spans will not (FG 0.03 x 300 m = 9 m).
            m_UseSag = false;
            for (int i = 0; i < m_K.Count - 1; )
            {
                int added = Anchors(i, i + 1);
                if (added < 0)
                    added = Split(i, i + 1, 0);   // no clear arrangement exists: the greedy split minimises what is left
                i += added + 1;
            }
            m_UseSag = true;

            // (b) infill.
            for (int i = 0; i < m_K.Count - 1; )
            {
                int added = Infill(i, i + 1);
                i += added + 1;
            }

            // (c) elevation.
            AssignMembers();

            return Build();
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Profile
        // ════════════════════════════════════════════════════════════════════════════════════════

        private void Sample(LiftTerrainSampler terrain)
        {
            float step = m_C.StepM;
            m_N = math.max(1, (int)math.ceil(m_Length / step - 1e-4f));
            int count = m_N + 1;
            if (m_S.Length < count)
            {
                m_S = new float[count];
                m_Centre = new float[count];
                m_High = new float[count];
                m_Low = new float[count];
            }
            float hw = m_C.HalfWidthM;
            for (int k = 0; k <= m_N; k++)
            {
                float s = k == m_N ? m_Length : k * step;
                float c = terrain(s, 0f);
                float l = hw > 0f ? terrain(s, -hw) : c;
                float r = hw > 0f ? terrain(s, hw) : c;
                m_S[k] = s;
                m_Centre[k] = c;
                m_High[k] = math.max(c, math.max(l, r));
                m_Low[k] = math.min(c, math.min(l, r));
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Span evaluation
        // ════════════════════════════════════════════════════════════════════════════════════════

        private float Rope(int k, int member)
        {
            if (member < 0)
                return k == 0 ? m_RopeA : m_RopeB;
            return m_Centre[k] + m_C.RopeAboveBase(member);
        }

        /// <summary>The node point itself: the carrier at a tower passes over the high side; the rope never above the cap.</summary>
        private bool NodeOk(int k, int member)
        {
            if (member < 0)
                return true;
            float rope = Rope(k, member);
            if (rope - m_C.CarrierDropM - m_High[k] - m_C.FloorM < -Tol)
                return false;
            return !(m_C.MaxRopeAboveGroundM > 0f && rope - m_Centre[k] > m_C.MaxRopeAboveGroundM + Tol);
        }

        private bool MemberAllowed(int k, int member)
        {
            return !(m_C.MaxRopeAboveGroundM > 0f && m_C.RopeAboveBase(member) > m_C.MaxRopeAboveGroundM + Tol) && member >= 0;
        }

        /// <summary>Worst violation of the span (i sample, ri rope) → (j, rj): max over the inner samples of the floor shortfall and
        /// the max-height excess (> 0 = violated). <paramref name="worstK"/> = that sample, <paramref name="floorType"/> = it is a
        /// floor shortfall.</summary>
        private float SpanViolation(int i, float ri, int j, float rj, out int worstK, out bool floorType)
        {
            worstK = -1;
            floorType = true;
            float worst = float.NegativeInfinity;
            float d = m_S[j] - m_S[i];
            if (d <= 0f)
                return 0f;
            float sag4 = 4f * m_C.SagRatio * d;
            float floorSag4 = FloorSag(d, sag4);
            float cap = m_C.MaxRopeAboveGroundM;
            float need = m_C.CarrierDropM + m_C.FloorM;
            for (int q = i + 1; q < j; q++)
            {
                float t = (m_S[q] - m_S[i]) / d;
                float chord = ri + (rj - ri) * t;
                float r = chord - sag4 * t * (1f - t);
                float fv = need + m_High[q] - (chord - floorSag4 * t * (1f - t));
                if (fv > worst)
                {
                    worst = fv;
                    worstK = q;
                    floorType = true;
                }
                if (cap > 0f)
                {
                    float hv = r - m_Centre[q] - cap;
                    if (hv > worst)
                    {
                        worst = hv;
                        worstK = q;
                        floorType = false;
                    }
                }
            }
            return worstK < 0 ? 0f : worst;
        }

        private float SpanViolation(int i, float ri, int j, float rj) => SpanViolation(i, ri, j, rj, out _, out _);

        /// <summary>The sag the floor test uses: the real one, except in phase (a) on a span that phase (b) will split (its sub-spans
        /// sag far less: FG 0.03 × 300 m = 9 m for the whole chord); a span (b) leaves whole is final and keeps its sag.</summary>
        private float FloorSag(float d, float sag4) => m_UseSag || InfillTowers(d) == 0 ? sag4 : 0f;

        /// <summary>The far-end rope heights [lo, hi] that keep the span from sample i (rope ri) to sample j clear, in one pass: every
        /// inner sample is a linear bound on rj (floor from below, max height from above). False when the interval is empty.</summary>
        private bool SpanInterval(int i, float ri, int j, out float lo, out float hi)
        {
            lo = float.NegativeInfinity;
            hi = float.PositiveInfinity;
            float d = m_S[j] - m_S[i];
            if (d <= 0f)
                return true;
            float sag4 = 4f * m_C.SagRatio * d;
            float floorSag4 = FloorSag(d, sag4);
            float cap = m_C.MaxRopeAboveGroundM;
            float need = m_C.CarrierDropM + m_C.FloorM;
            for (int q = i + 1; q < j; q++)
            {
                float t = (m_S[q] - m_S[i]) / d;
                float bow = t * (1f - t);
                // rope(t) = ri + (rj - ri) t - sag·bow  =>  rj = ri + (target - ri + sag·bow) / t
                lo = math.max(lo, ri + (need + m_High[q] + floorSag4 * bow - ri) / t);
                if (cap > 0f)
                    hi = math.min(hi, ri + (cap + m_Centre[q] + sag4 * bow - ri) / t);
                if (lo > hi + Tol)
                    return false;
            }
            return true;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // (a) anchors
        // ════════════════════════════════════════════════════════════════════════════════════════

        private struct Cost
        {
            public int Towers;      // anchors + the infill towers phase (b) will add to the spans so far
            public int Anchors;
            public int Members;     // sum of ladder indices
            public int Dist;        // sum of |anchor - worst-intrusion sample|
            public int Parent;      // state index (k * ladder + m), -1 = the start node

            public bool Better(Cost o)
            {
                if (Towers != o.Towers) return Towers < o.Towers;
                if (Anchors != o.Anchors) return Anchors < o.Anchors;
                if (Members != o.Members) return Members < o.Members;
                return Dist < o.Dist;
            }
        }

        private Cost[] m_Cost = new Cost[0];
        private bool[] m_Reached = new bool[0];

        /// <summary>Phase (a) between working nodes a and b = a + 1: the anchor set with the fewest towers (anchors + the infill (b)
        /// will add), then the fewest anchors, the shortest members, and anchors nearest the worst-intrusion sample, such that every
        /// span is clear (floor against the bare chord, max height with the sag). This is the recursive worst-intrusion split solved
        /// exactly (a search over the samples) instead of greedily: the greedy split put 18 towers on LP-5's 40 m wall. Returns the
        /// nodes added, 0 when the span is already clear, -1 when no clear arrangement exists.</summary>
        private int Anchors(int a, int b)
        {
            int i = m_K[a], j = m_K[b];
            float ri = Rope(i, m_M[a]), rj = Rope(j, m_M[b]);
            float v0 = SpanViolation(i, ri, j, rj, out int kWorst, out _);
            if (v0 <= Tol)
                return 0;
            int L = m_C.Ladder.Length;
            int states = (m_N + 1) * L;
            if (m_Cost.Length < states)
            {
                m_Cost = new Cost[states];
                m_Reached = new bool[states];
            }
            for (int q = i * L; q < (j + 1) * L && q < states; q++)
                m_Reached[q] = false;
            int minGap = MinGap();
            // Anchor spans are capped at 3 x MaxSpanM (phase (b) splits them anyway): bounds the search to O(samples x cap x span)
            // per ladder member at the source; SpanInterval removes the far member's factor.
            int maxGap = math.max(minGap, (int)math.floor(3f * m_C.MaxSpanM / m_C.StepM));
            bool endReached = false;
            Cost end = default;
            // Sources in sample order: the start node, then every reached (k, m).
            for (int k = i; k < j; k++)
            {
                for (int m = 0; m < L; m++)
                {
                    Cost from;
                    float rk;
                    if (k == i)
                    {
                        if (m > 0)
                            break;
                        from = new Cost { Parent = -1 };
                        rk = ri;
                    }
                    else
                    {
                        int st = k * L + m;
                        if (!m_Reached[st])
                            continue;
                        from = m_Cost[st];
                        from.Parent = st;
                        rk = Rope(k, m);
                    }
                    // To the end node.
                    if (k > i && j - k >= minGap && j - k <= maxGap && SpanInterval(k, rk, j, out float lo, out float hi) &&
                        rj >= lo - Tol && rj <= hi + Tol)
                    {
                        Cost c = from;
                        c.Towers += InfillTowers(m_S[j] - m_S[k]);
                        if (!endReached || c.Better(end))
                        {
                            end = c;
                            endReached = true;
                        }
                    }
                    // To an anchor (k2, m2).
                    for (int k2 = k + minGap; k2 <= math.min(j - minGap, k + maxGap); k2++)
                    {
                        if (!SpanInterval(k, rk, k2, out float lo2, out float hi2))
                            continue;
                        int infill = InfillTowers(m_S[k2] - m_S[k]);
                        for (int m2 = 0; m2 < L; m2++)
                        {
                            float r2 = Rope(k2, m2);
                            if (r2 < lo2 - Tol)
                                continue;
                            if (r2 > hi2 + Tol)
                                break;   // ropes ascend with the ladder
                            if (!MemberAllowed(k2, m2) || !NodeOk(k2, m2))
                                continue;
                            Cost c = from;
                            c.Towers += infill + 1;
                            c.Anchors += 1;
                            c.Members += m2;
                            c.Dist += math.abs(k2 - kWorst);
                            int st2 = k2 * L + m2;
                            if (!m_Reached[st2] || c.Better(m_Cost[st2]))
                            {
                                m_Cost[st2] = c;
                                m_Reached[st2] = true;
                            }
                        }
                    }
                }
            }
            if (!endReached)
                return -1;

            // Walk back; every insert lands between a and the end node, so a stays valid.
            int added = 0;
            int state = end.Parent;
            float chordSlope = (rj - ri) / math.max(m_S[j] - m_S[i], 1e-3f);
            while (state >= 0)
            {
                int k = state / L, m = state % L;
                // Shoulder: the ground pokes through the station-to-station chord here (floor); else Valley (max height).
                float chordHere = ri + chordSlope * (m_S[k] - m_S[i]);
                bool shoulder = m_C.CarrierDropM + m_C.FloorM + m_High[k] > chordHere;
                Insert(k, m, shoulder ? LiftPlanNodeFlags.Shoulder : LiftPlanNodeFlags.Valley);
                added++;
                state = m_Cost[state].Parent;
            }
            BreakoverPass(a, a + added + 1);
            return added;
        }

        /// <summary>Shoulders on convex ground (between working nodes a and b) become breakovers: each moves one sample past the crest
        /// toward the higher ground (uphill of the crest, planner notes span table rule 1) with the shortest member that keeps both of
        /// its spans clear and adds no infill tower.</summary>
        private void BreakoverPass(int a, int b)
        {
            int minGap = MinGap();
            for (int n = a + 1; n < b; n++)
            {
                if ((m_F[n] & LiftPlanNodeFlags.Shoulder) == 0)
                    continue;
                int i = m_K[n - 1], k = m_K[n], j = m_K[n + 1];
                int crest = Crest(k, i, j);
                if (crest < 0)
                    continue;
                m_F[n] |= LiftPlanNodeFlags.Breakover;
                int up = m_High[math.min(crest + 1, m_N)] >= m_High[math.max(crest - 1, 0)] ? crest + 1 : crest - 1;
                if (up == k || up - i < minGap || j - up < minGap)
                    continue;
                if (InfillTowers(m_S[up] - m_S[i]) + InfillTowers(m_S[j] - m_S[up]) >
                    InfillTowers(m_S[k] - m_S[i]) + InfillTowers(m_S[j] - m_S[k]))
                    continue;
                float ri = Rope(i, m_M[n - 1]), rj = Rope(j, m_M[n + 1]);
                for (int mm = 0; mm < m_C.Ladder.Length; mm++)
                {
                    if (!MemberAllowed(up, mm) || !NodeOk(up, mm))
                        continue;
                    float ru = Rope(up, mm);
                    if (SpanViolation(i, ri, up, ru) <= Tol && SpanViolation(up, ru, j, rj) <= Tol)
                    {
                        m_K[n] = up;
                        m_M[n] = mm;
                        break;
                    }
                }
            }
        }

        /// <summary>Fallback when no clear arrangement exists (e.g. ground through the rope right at a station): greedy split of the
        /// span between working nodes a and a+1 (b == a + 1) while it is violated; returns the nodes added.</summary>
        private int Split(int a, int b, int depth)
        {
            int i = m_K[a], j = m_K[b];
            float ri = Rope(i, m_M[a]), rj = Rope(j, m_M[b]);
            float v = SpanViolation(i, ri, j, rj, out int kWorst, out bool floorType);
            if (v <= Tol || depth >= MaxDepth)
                return 0;
            if (!BestSplit(i, ri, j, rj, kWorst, out int k, out int m, out int bad, out float sum))
                return 0;
            if (bad == 2 && sum >= v - Tol)
                return 0;   // no split improves this span: (c) / the report carry the violation

            LiftPlanNodeFlags flags = floorType ? LiftPlanNodeFlags.Shoulder : LiftPlanNodeFlags.Valley;
            int crest = floorType ? Crest(k, i, j) : -1;
            if (crest >= 0)
            {
                flags |= LiftPlanNodeFlags.Breakover;
                // Uphill of the crest: one sample past the most convex sample toward the higher ground, with the shortest member
                // that leaves the halves no worse than the chosen split (planner notes, span table rule 1).
                int up = m_High[math.min(crest + 1, m_N)] >= m_High[math.max(crest - 1, 0)] ? crest + 1 : crest - 1;
                int minGap = MinGap();
                if (up != k && up - i >= minGap && j - up >= minGap)
                {
                    int infill = InfillTowers(m_S[k] - m_S[i]) + InfillTowers(m_S[j] - m_S[k]);
                    int infillUp = InfillTowers(m_S[up] - m_S[i]) + InfillTowers(m_S[j] - m_S[up]);
                    for (int mm = 0; mm < m_C.Ladder.Length && infillUp <= infill; mm++)
                    {
                        if (!MemberAllowed(up, mm) || !NodeOk(up, mm))
                            continue;
                        float ru = Rope(up, mm);
                        float vl = SpanViolation(i, ri, up, ru), vr = SpanViolation(up, ru, j, rj);
                        int badU = (vl > Tol ? 1 : 0) + (vr > Tol ? 1 : 0);
                        float sumU = math.max(vl, 0f) + math.max(vr, 0f);
                        if (badU < bad || (badU == bad && sumU <= sum + Tol))
                        {
                            k = up;
                            m = mm;
                            break;
                        }
                    }
                }
            }
            int at = Insert(k, m, flags);
            int added = 1;
            added += Split(at, at + 1, depth + 1);              // right half first: indices left of `at` stay valid
            added += Split(at - 1, at, depth + 1);
            return added;
        }

        private int MinGap() => math.max(1, (int)math.ceil(m_C.MinSpanM / m_C.StepM - 1e-4f));

        private bool BestSplit(int i, float ri, int j, float rj, int kWorst, out int bestK, out int bestM, out int bestBad, out float bestSum)
        {
            bestK = -1; bestM = -1; bestBad = 3; bestSum = float.PositiveInfinity;
            int bestDist = int.MaxValue, bestInfill = int.MaxValue;
            int minGap = MinGap();
            for (int k = i + minGap; k <= j - minGap; k++)
            {
                int infill = InfillTowers(m_S[k] - m_S[i]) + InfillTowers(m_S[j] - m_S[k]);
                for (int m = 0; m < m_C.Ladder.Length; m++)
                {
                    if (!MemberAllowed(k, m) || !NodeOk(k, m))
                        continue;
                    float rk = Rope(k, m);
                    float vl = SpanViolation(i, ri, k, rk), vr = SpanViolation(k, rk, j, rj);
                    int bad = (vl > Tol ? 1 : 0) + (vr > Tol ? 1 : 0);
                    float sum = math.max(vl, 0f) + math.max(vr, 0f);
                    int dist = math.abs(k - kWorst);
                    // Rank: fewer violated halves, smaller remaining violation (0.05 m bins), fewer infill towers the halves will
                    // need in (b), shorter member, nearer the worst-intrusion point.
                    bool better;
                    if (bad != bestBad) better = bad < bestBad;
                    else if (bad > 0 && math.abs(sum - bestSum) > 0.05f) better = sum < bestSum;
                    else if (infill != bestInfill) better = infill < bestInfill;
                    else if (m != bestM) better = m < bestM;
                    else better = dist < bestDist;
                    if (better)
                    {
                        bestK = k; bestM = m; bestBad = bad; bestSum = sum; bestDist = dist; bestInfill = infill;
                    }
                    if (bad == 0)
                        break;   // a taller member at this sample can only rank lower
                }
            }
            return bestK >= 0;
        }

        /// <summary>Sub-spans phase (b) will make of a span of <paramref name="len"/> metres, minus one = the infill towers it adds.</summary>
        private int InfillTowers(float len)
        {
            int n = math.max((int)math.ceil(len / m_C.MaxSpanM - 1e-4f), (int)math.round(len / m_C.TypicalSpanM));
            return math.max(0, n - 1);
        }

        /// <summary>Change of the high-side slope across sample k (±2 samples), in metres per 2·StepM: below -ConvexM = convex.</summary>
        private float Curvature(int k)
        {
            int a = math.max(0, k - 2), b = math.min(m_N, k + 2);
            if (b - k < 1 || k - a < 1)
                return 0f;
            float left = (m_High[k] - m_High[a]) / (m_S[k] - m_S[a]);
            float right = (m_High[b] - m_High[k]) / (m_S[b] - m_S[k]);
            return (right - left) * 2f * m_C.StepM;
        }

        /// <summary>The breakover crest near a floor split at k: the most convex sample within ±2 samples (inside the span), or -1
        /// when the ground there is not convex. Ties go to the sample nearest k, then the lower index.</summary>
        private int Crest(int k, int i, int j)
        {
            int best = -1;
            float bestC = -m_C.ConvexM;
            for (int q = math.max(i + 1, k - 2); q <= math.min(j - 1, k + 2); q++)
            {
                float c = Curvature(q);
                if (c < bestC - 1e-4f || (best >= 0 && math.abs(c - bestC) <= 1e-4f && math.abs(q - k) < math.abs(best - k)))
                {
                    bestC = math.min(c, bestC);
                    best = q;
                }
            }
            return best;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // (b) infill
        // ════════════════════════════════════════════════════════════════════════════════════════

        private int Infill(int a, int b)
        {
            int i = m_K[a], j = m_K[b];
            float len = m_S[j] - m_S[i];
            int n = InfillTowers(len) + 1;
            if (n <= 1)
                return 0;

            var bestKs = new List<int>();
            var bestMs = new List<int>();
            float bestViol = float.PositiveInfinity;
            var ks = new List<int>();
            var ms = new List<int>();
            for (int extra = 0; extra <= 3 && bestViol > Tol; extra++)
            {
                for (int pass = 0; pass < 2 && bestViol > Tol; pass++)
                {
                    // Pass 0: the equal-spacing samples; pass 1: each node slides within half a sub-span of its spot.
                    float viol = TryInfill(a, b, n + extra, pass == 1, ks, ms);
                    if (viol < bestViol - Tol)
                    {
                        bestViol = viol;
                        bestKs.Clear(); bestKs.AddRange(ks);
                        bestMs.Clear(); bestMs.AddRange(ms);
                    }
                }
            }
            if (bestKs.Count == 0)
                return 0;
            for (int q = 0; q < bestKs.Count; q++)
                Insert(bestKs[q], bestMs[q], LiftPlanNodeFlags.Infill);
            return bestKs.Count;
        }

        /// <summary>Place n-1 infill nodes between working nodes a and b; returns the summed violation of the n sub-spans
        /// (+inf when no placement satisfies the span-length rules).</summary>
        private float TryInfill(int a, int b, int n, bool slide, List<int> ks, List<int> ms)
        {
            ks.Clear();
            ms.Clear();
            int i0 = m_K[a], j = m_K[b];
            float rj = Rope(j, m_M[b]);
            int prev = i0;
            float rPrev = Rope(i0, m_M[a]);
            float total = 0f;
            float maxSub = math.min(m_C.MaxSpanM, 1.5f * m_C.TypicalSpanM - m_C.StepM);
            int minGap = MinGap();
            for (int r = n; r >= 2; r--)
            {
                float rest = m_S[j] - m_S[prev];
                float ideal = m_S[prev] + rest / r;
                float half = 0.5f * rest / r;
                int bestK = -1, bestM = -1, bestBad = 3;
                float bestV = float.PositiveInfinity, bestDist = float.PositiveInfinity;
                int kLo = slide ? (int)math.ceil((ideal - half) / m_C.StepM) : (int)math.round(ideal / m_C.StepM);
                int kHi = slide ? (int)math.floor((ideal + half) / m_C.StepM) : kLo;
                kLo = math.max(prev + minGap, kLo);
                kHi = math.min(j - minGap, kHi);
                for (int k = kLo; k <= kHi; k++)
                {
                    float sub = m_S[k] - m_S[prev];
                    float after = m_S[j] - m_S[k];
                    if (sub > maxSub + 1e-3f || after > (r - 1) * maxSub + 1e-3f)
                        continue;
                    for (int m = 0; m < m_C.Ladder.Length; m++)
                    {
                        if (!MemberAllowed(k, m) || !NodeOk(k, m))
                            continue;
                        float rk = Rope(k, m);
                        float v = math.max(SpanViolation(prev, rPrev, k, rk), 0f);
                        if (r == 2)
                            v += math.max(SpanViolation(k, rk, j, rj), 0f);
                        int bad = v > Tol ? 1 : 0;
                        float dist = math.abs(m_S[k] - ideal);
                        bool better;
                        if (bad != bestBad) better = bad < bestBad;
                        else if (bad > 0 && math.abs(v - bestV) > 0.05f) better = v < bestV;
                        else if (m != bestM) better = m < bestM;
                        else better = dist < bestDist - 1e-3f;
                        if (better)
                        {
                            bestK = k; bestM = m; bestBad = bad; bestV = v; bestDist = dist;
                        }
                        if (bad == 0)
                            break;
                    }
                }
                if (bestK < 0)
                    return float.PositiveInfinity;
                ks.Add(bestK);
                ms.Add(bestM);
                total += bestV;
                prev = bestK;
                rPrev = Rope(bestK, bestM);
            }
            if (m_S[j] - m_S[prev] > maxSub + 1e-3f && n > 1)
                return float.PositiveInfinity;
            return total;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // (c) elevation
        // ════════════════════════════════════════════════════════════════════════════════════════

        private void AssignMembers()
        {
            int count = m_K.Count;
            var before = new int[count];
            for (int i = 0; i < count; i++)
                before[i] = m_M[i];
            float violBefore = TotalViolation();

            for (int i = 0; i < count; i++)
            {
                if (m_M[i] < 0)
                    continue;
                int k = m_K[i];
                int m = 0;
                while (m < m_C.Ladder.Length - 1 && !(MemberAllowed(k, m) && NodeOk(k, m)))
                    m++;
                m_M[i] = m;
            }
            int guard = count * m_C.Ladder.Length + 4;
            while (guard-- > 0)
            {
                // The span with the worst floor shortfall.
                int worstSpan = -1, worstK = -1;
                float worstV = Tol;
                for (int s = 0; s < count - 1; s++)
                {
                    float v = SpanViolation(m_K[s], Rope(m_K[s], m_M[s]), m_K[s + 1], Rope(m_K[s + 1], m_M[s + 1]), out int wk, out bool floorType);
                    if (floorType && v > worstV)
                    {
                        worstV = v;
                        worstSpan = s;
                        worstK = wk;
                    }
                }
                if (worstSpan < 0)
                    break;
                int a = worstSpan, b = worstSpan + 1;
                float t = (m_S[worstK] - m_S[m_K[a]]) / (m_S[m_K[b]] - m_S[m_K[a]]);
                int first = t >= 0.5f ? b : a, second = first == a ? b : a;
                if (!Raise(first) && !Raise(second))
                    break;   // both ends are stations or at the top of the ladder: the violation is reported
            }

            if (TotalViolation() > violBefore + Tol)
            {
                for (int i = 0; i < count; i++)
                    m_M[i] = before[i];
            }
        }

        private bool Raise(int node)
        {
            int m = m_M[node];
            if (m < 0)
                return false;
            int k = m_K[node];
            for (int up = m + 1; up < m_C.Ladder.Length; up++)
            {
                if (MemberAllowed(k, up) && NodeOk(k, up))
                {
                    m_M[node] = up;
                    return true;
                }
            }
            return false;
        }

        private float TotalViolation()
        {
            float total = 0f;
            for (int s = 0; s < m_K.Count - 1; s++)
                total += math.max(0f, SpanViolation(m_K[s], Rope(m_K[s], m_M[s]), m_K[s + 1], Rope(m_K[s + 1], m_M[s + 1])));
            return total;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Working list + output
        // ════════════════════════════════════════════════════════════════════════════════════════

        private int IndexOf(int k)
        {
            for (int i = 0; i < m_K.Count; i++)
                if (m_K[i] == k)
                    return i;
            return -1;
        }

        private int Insert(int k, int m, LiftPlanNodeFlags flags)
        {
            int at = 0;
            while (at < m_K.Count && m_K[at] < k)
                at++;
            m_K.Insert(at, k);
            m_M.Insert(at, m);
            m_F.Insert(at, flags);
            return at;
        }

        private LiftTowerPlan Build()
        {
            int count = m_K.Count;
            var nodes = new LiftPlanNode[count];
            for (int i = 0; i < count; i++)
            {
                int k = m_K[i];
                LiftPlanNodeFlags f = m_F[i];
                if (m_M[i] >= 0 && m_High[k] - m_Low[k] > m_C.SideHillM)
                    f |= LiftPlanNodeFlags.SideHill;
                nodes[i] = new LiftPlanNode
                {
                    AlongM = m_S[k],
                    GroundM = m_Centre[k],
                    HighSideM = m_High[k],
                    LowSideM = m_Low[k],
                    Member = m_M[i],
                    RopeM = Rope(k, m_M[i]),
                    Flags = f,
                };
            }
            var spans = new LiftPlanSpan[count - 1];
            float need = m_C.CarrierDropM + m_C.FloorM;
            for (int s = 0; s < count - 1; s++)
            {
                int i = m_K[s], j = m_K[s + 1];
                float ri = nodes[s].RopeM, rj = nodes[s + 1].RopeM;
                float d = m_S[j] - m_S[i];
                float sag4 = 4f * m_C.SagRatio * d;
                var span = new LiftPlanSpan { LengthM = d, MinFloorMarginM = float.PositiveInfinity, MaxRopeAboveM = float.NegativeInfinity };
                for (int q = i; q <= j; q++)
                {
                    // Station ends are not judged (their rope height is given); tower ends are (the carrier passes over the node).
                    if ((q == i && nodes[s].IsStation) || (q == j && nodes[s + 1].IsStation))
                        continue;
                    float t = d > 0f ? (m_S[q] - m_S[i]) / d : 0f;
                    float r = ri + (rj - ri) * t - sag4 * t * (1f - t);
                    float margin = r - need - m_High[q];
                    if (margin < span.MinFloorMarginM)
                    {
                        span.MinFloorMarginM = margin;
                        span.MinFloorMarginAtM = m_S[q] - m_S[i];
                        span.MinChairAboveGroundM = r - m_C.CarrierDropM - m_High[q];
                    }
                    float above = r - m_Centre[q];
                    if (above > span.MaxRopeAboveM)
                    {
                        span.MaxRopeAboveM = above;
                        span.MaxRopeAboveAtM = m_S[q] - m_S[i];
                    }
                }
                if (float.IsPositiveInfinity(span.MinFloorMarginM))
                {
                    span.MinFloorMarginM = 0f;
                    span.MaxRopeAboveM = 0f;
                }
                span.FloorViolated = span.MinFloorMarginM < -Tol;
                span.HeightViolated = m_C.MaxRopeAboveGroundM > 0f && span.MaxRopeAboveM > m_C.MaxRopeAboveGroundM + Tol;
                spans[s] = span;
            }
            var along = new float[m_N + 1];
            var centre = new float[m_N + 1];
            var high = new float[m_N + 1];
            Array.Copy(m_S, along, m_N + 1);
            Array.Copy(m_Centre, centre, m_N + 1);
            Array.Copy(m_High, high, m_N + 1);
            return new LiftTowerPlan
            {
                Constants = m_C, LengthM = m_Length, Nodes = nodes, Spans = spans,
                SampleAlongM = along, SampleCentreM = centre, SampleHighM = high,
            };
        }
    }
}
