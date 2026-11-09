// RunsFirst result: the plan as one plain object (what the realizer will read) and a stable 64-bit hash over it (the RF golden).
// The hash covers every run, connector, link and lift, coordinates quantized to 0.01 m. It leaves out the log: the log is
// formatted text (number formats follow the thread's culture), not plan geometry. Pure C# 9, net48-clean.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal sealed class RunsFirstResult
    {
        internal sealed class Run
        {
            public List<float2> Pts;             // skiing direction
            public SlopeBand Band;
            public string Kind;                  // line / fill / learning / ... (the pass that made it)
            public bool Graded;                  // cut with the grading tool: built as a connector
            public RunKind Surface;              // groomed or natural
            public int Lift;                     // the lift (1-based index into Lifts) the run starts from; 0 = none
        }

        internal sealed class Lift
        {
            public int Id;                       // 1-based, the order of Lifts
            public string Type;                  // fixed-grip / detachable / gondola / tram / beginner
            public string Kind;                  // main / beginner
            public float3 Bottom, Top;
            public bool RidesDown;               // a download lift (no ski-out)
        }

        public readonly List<Run> Runs = new List<Run>();
        public readonly List<List<float2>> Connectors = new List<List<float2>>();
        public readonly List<List<float2>> Links = new List<List<float2>>();
        public readonly List<Lift> Lifts = new List<Lift>();
        public readonly List<string> Log = new List<string>();

        /// <summary>FNV-1a 64 over the plan: counts, then each run (kind, band, graded, surface, lift, points), connector, link and
        /// lift (type, kind, ends, rides down). Coordinates are quantized to 0.01 m, so the hash is the same on any runtime that
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
                Long((long)r.Surface);
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
            }
            return h;
        }
    }

    internal static partial class RunsFirst
    {
        /// <summary>The plain plan object of a finished plan (copies: the planner's own types stay inside).</summary>
        private static RunsFirstResult MakeResult(Plan built, List<Lift> lifts)
        {
            var res = new RunsFirstResult();
            foreach (PlanRun r in built.Runs)
                res.Runs.Add(new RunsFirstResult.Run
                {
                    Pts = new List<float2>(r.Pts), Band = r.Band, Kind = r.Kind, Graded = r.Graded, Surface = r.Surface,
                    Lift = r.By == null ? 0 : lifts.IndexOf(r.By) + 1,
                });
            foreach (List<float2> c in built.Connectors)
                res.Connectors.Add(new List<float2>(c));
            foreach (List<float2> c in built.Links)
                res.Links.Add(new List<float2>(c));
            for (int i = 0; i < lifts.Count; i++)
                res.Lifts.Add(new RunsFirstResult.Lift
                {
                    Id = i + 1, Type = lifts[i].Type, Kind = lifts[i].Kind, Bottom = lifts[i].Bottom, Top = lifts[i].Top, RidesDown = lifts[i].RidesDown,
                });
            res.Log.AddRange(built.Log);
            return res;
        }
    }
}
