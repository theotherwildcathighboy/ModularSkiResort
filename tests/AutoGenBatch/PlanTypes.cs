// PlanTypes suite (A0): the Resort Planner (v2) seams on the stubbed hooks. Runs mode allocates nothing of v2 (V1-2); Resort mode
// returns the v1 runs plus a ResortPlan with pads and pods (A3) carrying the probe slots, the layers are allocated, sliced Step /
// Reselect / cancel / Snapshot behave. Checks that only hold while the hooks are stubs are named "A0 stub" and are removed by the task that fills the
// hook (A3-A6), never loosened.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static void PlanTypes()
        {
            // Default shapes
            var plan = new ResortPlan();
            Check(plan.Pads.Length == 0 && plan.Pods.Length == 0 && plan.Lifts.Length == 0 && plan.PodRuns.Length == 0 &&
                  plan.RunPod.Length == 0 && plan.RunLift.Length == 0 && plan.Graph.Length == 0, "PlanTypes", "a new ResortPlan is not empty");
            Check(plan.MountainMix.Length == 5 && plan.StageUnits.Length == SkiRunAutoPlanner.ResortStageCount, "PlanTypes", "ResortPlan array sizes");
            Check(float.IsNaN(plan.TreeLineY) && plan.Fingerprint == 0UL && plan.DeadEnds == 0, "PlanTypes", "ResortPlan defaults");
            var inDef = new AutoPlanInput();
            Check(inDef.Mode == AutoPlanMode.Runs && inDef.Pinned == null && inDef.Access == null && inDef.Cancel == null &&
                  float.IsNaN(inDef.TreeLineY), "PlanTypes", "AutoPlanInput defaults (Mode must stay Runs)");
            AutoPlanKnobs kd = AutoPlanKnobs.Default(1u);
            Check(kd.Lifts == 0 && kd.Home == HomeLevel.Auto, "PlanTypes", "AutoPlanKnobs defaults (Lifts 0 = Auto, Home Auto)");
            Check(((ushort)AutoPlanWarning.NoLiftSite | (ushort)AutoPlanWarning.Download | (ushort)AutoPlanWarning.DeadEnd) == 7168 &&
                  (ushort)AutoPlanWarning.DeadEnd == 4096, "PlanTypes", "AutoPlanWarning values");

            foreach (var (name, f, region) in new (string, HeightFn, float2[])[]
                     {
                         ("alpine lower", Terrain.Alpine(2u), Terrain.AlpineRegion(true)),
                         ("mixed", Mixed, MixedRegion),
                     })
            {
                string w = "PlanTypes " + name;
                // Runs mode: nothing of v2 (V1-2)
                var pRuns = new SkiRunAutoPlanner();
                AutoPlanResult rRuns = pRuns.Run(Input(f, region));
                Check(rRuns.Status == AutoPlanStatus.Ok, w, "Runs: " + rRuns.Status);
                Check(rRuns.Resort == null && pRuns.ResortLayerBytes == 0, w, "Runs mode allocated v2 state");
                Check(!rRuns.Canceled && Math.Abs(pRuns.ProgressVolatile - 1f) < 1e-6f, w, "Runs: ProgressVolatile after Done = " + pRuns.ProgressVolatile);

                // Resort mode, stubs: the v1 runs plus an empty plan
                AutoPlanInput ri = Input(f, region);
                ri.Mode = AutoPlanMode.Resort;
                var pRes = new SkiRunAutoPlanner();
                AutoPlanResult rRes = pRes.Run(ri);
                Check(rRes.Status == AutoPlanStatus.Ok && rRes.Resort != null, w, "Resort: " + rRes.Status + ", plan " + (rRes.Resort != null));
                if (rRes.Resort == null)
                    continue;
                Check(rRes.Fingerprint == rRuns.Fingerprint, w, "A0 stub: Resort fingerprint differs from Runs (the stubs must not change runs)");
                Check(rRes.Runs.Count == rRuns.Runs.Count && rRes.Warnings == rRuns.Warnings, w, "A0 stub: Resort runs / warnings differ from Runs");
                // A3: pads and pods are real (Terrain / PodRoute / Pods); their invariants are the Terrain suite's
                Check(rRes.Resort.Pads.Length > 0 && rRes.Resort.Pods.Length > 0, w,
                      "Resort: no pads / pods (" + rRes.Resort.Pads.Length + " / " + rRes.Resort.Pods.Length + ")");
                PlanInvariants(w, pRes, rRes);
                Check(pRes.ResortLayerBytes >= 9L * 1000, w, "Resort layers not allocated (" + pRes.ResortLayerBytes + " B)");
                int[] su = rRes.Resort.StageUnits;
                Check(su[0] > 1 && su[1] > 1 && su[2] > 1 && su[4] > 0 && su[5] == 1, w,
                      "probe slots Terrain / PodRoute / Pods / Lifts / Route / Flow = " + string.Join("/", su));
                Check(su[3] >= 1, w, "the Lifts stage ran " + su[3] + " units");

                // Sliced Step == Run (D-2) in Resort mode, with ProgressVolatile rising
                var sliced = new SkiRunAutoPlanner();
                sliced.Begin(ri);
                float last = 0f;
                bool monotone = true;
                while (!sliced.Step(0.05f))
                {
                    monotone &= sliced.ProgressVolatile >= last;
                    last = sliced.ProgressVolatile;
                }
                Check(sliced.Result.Fingerprint == rRes.Fingerprint && monotone && last < 1f + 1e-6f, w, "Resort: sliced Step differs from Run or progress went down");

                // Reselect of the new knobs equals a fresh run (D-3): Lifts restarts at Lifts, Home at Finish
                foreach (var (kname, knobs) in new (string, AutoPlanKnobs)[]
                         {
                             ("lifts 3", WithKnobs(ri.Knobs, 3, HomeLevel.Auto)),
                             ("home blue", WithKnobs(ri.Knobs, 0, HomeLevel.Blue)),
                             ("lifts 2 home green", WithKnobs(ri.Knobs, 2, HomeLevel.Green)),
                         })
                {
                    var p = new SkiRunAutoPlanner();
                    p.Run(ri);
                    Check(p.Reselect(knobs), w + " reselect " + kname, "Reselect refused");
                    while (!p.Step(float.PositiveInfinity))
                    {
                    }
                    AutoPlanInput fresh = new AutoPlanInput
                    {
                        Snapshot = ri.Snapshot, Region = ri.Region, Knobs = knobs, Mode = AutoPlanMode.Resort,
                    };
                    Check(p.Result.Fingerprint == new SkiRunAutoPlanner().Run(fresh).Fingerprint && p.Result.Resort != null, w + " reselect " + kname,
                          "differs from a fresh Resort run with the knobs");
                }

                // Snapshot(): equal content, independent of the planner afterwards
                AutoPlanResult snap = rRes.Snapshot();
                ulong fp = snap.Fingerprint;
                int runs = snap.Runs.Count;
                pRes.Run(Input(f, region));   // reuses the planner in Runs mode: clears the old result
                Check(snap.Fingerprint == fp && snap.Runs.Count == runs && snap.Resort != null && snap.Resort != pRes.Result.Resort, w,
                      "Snapshot() changed when the planner was reused");
                Check(snap.Resort.StageUnits[4] == su[4] && Math.Abs(snap.VerticalM - rRuns.VerticalM) < 1e-3f, w, "Snapshot() lost fields");
            }

            // Cancel: a requested token ends the scan at the next unit, flags it, and leaves the planner reusable (D-5 shape)
            foreach (AutoPlanMode mode in new[] { AutoPlanMode.Runs, AutoPlanMode.Resort })
            {
                string w = "PlanTypes cancel " + mode;
                AutoPlanInput ci = Input(Mixed, MixedRegion);
                ci.Mode = mode;
                var cancel = new AutoPlanCancel();
                ci.Cancel = cancel;
                var p = new SkiRunAutoPlanner();
                p.Begin(ci);
                for (int k = 0; k < 5; k++)
                    p.Step(0f);
                Check(!p.Result.Canceled && !p.Done, w, "canceled before the token was set");
                cancel.Request();
                int units = p.Result.Units;
                Check(p.Step(float.PositiveInfinity) && p.Done && p.Result.Canceled, w, "Step did not stop on Cancel");
                Check(p.Result.Units == units, w, "a unit ran after the cancel");
                Check(!p.Reselect(ci.Knobs), w, "Reselect accepted a canceled scan");
                AutoPlanInput again = Input(Mixed, MixedRegion);
                again.Mode = mode;
                AutoPlanResult r = p.Run(again);
                Check(!r.Canceled && r.Status == AutoPlanStatus.Ok && r.Fingerprint == new SkiRunAutoPlanner().Run(again).Fingerprint, w,
                      "the planner is not reusable after a cancel");
            }
        }

        private static AutoPlanKnobs WithKnobs(AutoPlanKnobs k, int lifts, HomeLevel home)
        {
            k.Lifts = lifts;
            k.Home = home;
            return k;
        }
    }
}
