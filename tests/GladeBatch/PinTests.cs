// Pinning check for the tree filler extension (Quick runs package C): a digest of what the glade planner and compose produce WITHOUT a
// fill mask, recorded on the code before the extension landed. Any change to the existing glade behavior moves the digest.
// Within-runtime only (net8 CoreCLR here); the digest is not a cross-runtime golden value (Program.cs header).

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace GladeBatch
{
    internal static partial class Program
    {
        /// <summary>Digests recorded on the pre-filler planner / compose (2026-10-25). Seeds 0..PinSeeds-1.</summary>
        private const ulong PinPlannerDigest = 0xE9343AEBA5C86008UL;
        private const ulong PinComposeDigest = 0x6E7AF524B73C5950UL;
        private const int PinSeeds = 12;

        private static void PinTests(List<Fixture> fixtures, List<ParamSet> sets, List<SimRun> simRuns)
        {
            ulong hp = 14695981039346656037UL;
            foreach (ParamSet ps in sets)
            {
                foreach (Fixture f in fixtures)
                {
                    for (int s = 0; s < PinSeeds; s++)
                    {
                        float3[] ex;
                        bool[] th;
                        MakeStand(f, s, out ex, out th);
                        GladePlan a = Run(f, PlanSeed(f, s), ps.P, ex, th);
                        hp = PinPlan(hp, a);
                    }
                }
            }

            ulong hc = 14695981039346656037UL;
            foreach (SimRun run in simRuns)
            {
                for (int s = 0; s < 2; s++)
                {
                    World w = MakeWorld(run, s);
                    foreach (SimTarget target in new[] { SimTarget.Interior, SimTarget.TreeLine })
                    {
                        for (int level = 1; level <= 8; level += 3)
                        {
                            CensusOut c = Census(w, run, target);
                            ComposeInput input = BuildInput(run, target, level, c, Caps(TCap));
                            ComposeResult r = new ComposeResult();
                            SkiGladeCompose.Compose(input, r);
                            s_Plans++;
                            hc = PinCompose(hc, r);

                            // A second compose with the first one's plants standing as members: covers the keep / remove branch.
                            var own = new List<ComposeMember>(c.Own ?? Array.Empty<ComposeMember>());
                            for (int i = 0; i < r.Plant.Count; i++)
                            {
                                own.Add(new ComposeMember
                                {
                                    Position = r.Plan.Planted[r.Plant[i]].Position,
                                    Layer = LayerOf(target),
                                    Variant = 0,
                                    PlanGen = SkiGladeCompose.PlanGeneration,
                                });
                            }
                            input.Own = own.ToArray();
                            input.Level = (byte)(level > 1 ? level - 1 : 2);
                            ComposeResult r2 = new ComposeResult();
                            SkiGladeCompose.Compose(input, r2);
                            s_Plans++;
                            hc = PinCompose(hc, r2);
                        }
                    }
                }
            }

            Console.WriteLine("INFO  pin: planner digest " + hp.ToString("X16") + ", compose digest " + hc.ToString("X16"));
            Check(PinPlannerDigest == 0UL || hp == PinPlannerDigest, "pin/planner",
                "PIN: the glade planner output changed (digest " + hp.ToString("X16") + " vs " + PinPlannerDigest.ToString("X16") + ")");
            Check(PinComposeDigest == 0UL || hc == PinComposeDigest, "pin/compose",
                "PIN: the glade compose output changed (digest " + hc.ToString("X16") + " vs " + PinComposeDigest.ToString("X16") + ")");
            Check(PinPlannerDigest != 0UL && PinComposeDigest != 0UL, "pin/recorded", "PIN: the digests are not recorded");
        }

        private static ulong PinPlan(ulong h, GladePlan p)
        {
            h = PinU(h, (uint)p.Status);
            h = PinF(h, p.Area);
            h = PinF(h, p.BeltArea);
            h = PinU(h, (uint)p.TargetGlade);
            h = PinU(h, (uint)p.TargetBelt);
            h = PinU(h, (uint)p.RawTargetGlade);
            h = PinU(h, (uint)p.RawTargetBelt);
            h = PinU(h, (uint)p.Attempts);
            h = PinU(h, (p.Saturated ? 1u : 0u) | (p.Clamped ? 2u : 0u));
            h = PinU(h, (uint)p.Thinned.Count);
            for (int i = 0; i < p.Thinned.Count; i++)
                h = PinU(h, (uint)p.Thinned[i]);
            h = PinU(h, (uint)p.Planted.Count);
            for (int i = 0; i < p.Planted.Count; i++)
            {
                PlantedTree t = p.Planted[i];
                h = PinF(h, t.Position.x);
                h = PinF(h, t.Position.y);
                h = PinF(h, t.Position.z);
                h = PinF(h, t.Rotation);
                h = PinU(h, (uint)t.Species);
                h = PinF(h, t.Age);
                h = PinU(h, t.Layer);
            }
            return h;
        }

        private static ulong PinCompose(ulong h, ComposeResult r)
        {
            h = PinPlan(h, r.Plan);
            h = PinU(h, (uint)r.Status);
            h = PinU(h, (uint)r.PlanKey);
            h = PinU(h, (uint)(r.PlanKey >> 32));
            int[] ints = { r.Target, r.Inside, r.WildInside, r.FixedInside, r.OwnInside, r.Keep, r.RawTarget, r.Slice, r.FellFull, r.Same,
                           r.Other, r.Matched, r.Missing, r.Skipped, r.Deficit, r.Shortfall, r.Misplaced, r.OwnUnmatched };
            for (int i = 0; i < ints.Length; i++)
                h = PinU(h, (uint)ints[i]);
            h = PinU(h, (r.Clamped ? 1u : 0u) | (r.Saturated ? 2u : 0u) | (r.Complete ? 4u : 0u));
            foreach (List<int> list in new[] { r.Plant, r.Remove, r.Fell })
            {
                h = PinU(h, (uint)list.Count);
                for (int i = 0; i < list.Count; i++)
                    h = PinU(h, (uint)list[i]);
            }
            return h;
        }

        private static ulong PinU(ulong h, uint v)
        {
            unchecked
            {
                for (int b = 0; b < 4; b++)
                {
                    h ^= (v >> (8 * b)) & 0xFFu;
                    h *= 1099511628211UL;
                }
                return h;
            }
        }

        private static ulong PinF(ulong h, float v)
        {
            return PinU(h, (uint)BitConverter.SingleToInt32Bits(v));
        }
    }
}
