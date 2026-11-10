// The runs-first planner's plan as one plain object (what the realizer reads) and a stable 64-bit hash over it (the RF golden).
// The hash covers every run, connector, link and lift, coordinates quantized to 0.01 m. It leaves out the log (formatted text, not
// plan geometry) and Lift.Base (derived from the lifts). The planner fills it (RunsFirstPlanner.Result.cs, MakeResult).
// Pure: System, System.Collections.Generic, Unity.Mathematics. C# 9, net48-clean.
// Engine notes: EngineNotes/ResortPlanner.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Whether a skier leaving a lift's top gets to the bottom of the region (the base band) on runs and cat tracks alone, and
    /// what is in the way when not (the way-to-the-bottom pass, RunsFirstPlanner.Bottom.cs). In the plan hash.</summary>
    public enum RunsFirstBottom : byte
    {
        Reaches = 0,     // runs and cat tracks lead from the top to the base band
        NoSkiing = 1,    // nothing leaves the top (no way down to judge)
        Cliff = 2,       // the fall line below the skiing has a pitch over the run rules
        Water = 3,       // the fall line runs into water the plan may not use
        Hole = 4,        // the fall line runs into a hole in the region (ground the plan may not use, with region below it)
        Edge = 5,        // the fall line leaves the region above the base band
        Other = 6,       // no route and none of the above (a search past its budget)
        Basin = 7,       // the drainage climbs out of a basin, more than the run rules allow
    }

    public sealed class RunsFirstResult
    {
        public sealed class Run
        {
            public List<float2> Pts;             // skiing direction
            public SlopeBand Band;
            public string Kind;                  // line / fill / learning / ... (the pass that made it)
            public bool Graded;                  // cut with the grading tool: built as a connector
            public bool Groomed;                 // false: natural (ungroomed)
            public int Lift;                     // the lift (1-based index into Lifts) the run starts from; 0 = none
        }

        public sealed class Lift
        {
            public int Id;                       // 1-based, the order of Lifts
            public string Type;                  // fixed-grip / detachable / gondola / tram / beginner
            public string Kind;                  // main / beginner
            public float3 Bottom, Top;
            public bool RidesDown;               // a download lift (no ski-out)
            public RunsFirstBottom ToBottom;     // the way to the bottom from its top (R2-c, in the hash)
            public bool Base;                    // its bottom is in one of the planner's base clusters (not in the hash)
        }

        public readonly List<Run> Runs = new List<Run>();
        public readonly List<List<float2>> Connectors = new List<List<float2>>();
        public readonly List<List<float2>> Links = new List<List<float2>>();
        public readonly List<Lift> Lifts = new List<Lift>();
        public readonly List<string> Log = new List<string>();

        /// <summary>FNV-1a 64 over the plan: counts, then each run (kind, band, graded, surface, lift, points), connector, link and
        /// lift (type, kind, ends, rides down, way to the bottom). Coordinates are quantized to 0.01 m, so the hash is the same on any runtime that
        /// gives the same plan to the centimeter.</summary>
        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;
            void Byte(byte b)
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            void Long(long v)
            {
                for (int i = 0; i < 8; i++)
                    Byte((byte)(v >> (8 * i)));
            }
            void Str(string s)
            {
                Long(s?.Length ?? -1);
                if (s != null)
                    foreach (char c in s)
                        Long(c);
            }
            void Q(float v) => Long((long)Math.Round(v * 100.0));
            void Pts(List<float2> pts)
            {
                Long(pts.Count);
                foreach (float2 p in pts)
                {
                    Q(p.x);
                    Q(p.y);
                }
            }
            Long(Runs.Count);
            foreach (Run r in Runs)
            {
                Str(r.Kind);
                Long((long)r.Band);
                Long(r.Graded ? 1 : 0);
                Long((long)RunFeatures.Of(RunBase.Piste, r.Groomed, false, false).SnowKind);   // the value the port baseline hashed (0 groomed, 3 natural)
                Long(r.Lift);
                Pts(r.Pts);
            }
            Long(Connectors.Count);
            foreach (List<float2> c in Connectors)
                Pts(c);
            Long(Links.Count);
            foreach (List<float2> c in Links)
                Pts(c);
            Long(Lifts.Count);
            foreach (Lift lf in Lifts)
            {
                Long(lf.Id);
                Str(lf.Type);
                Str(lf.Kind);
                Q(lf.Bottom.x);
                Q(lf.Bottom.y);
                Q(lf.Bottom.z);
                Q(lf.Top.x);
                Q(lf.Top.y);
                Q(lf.Top.z);
                Long(lf.RidesDown ? 1 : 0);
                Long((long)lf.ToBottom);
            }
            return h;
        }
    }
}
