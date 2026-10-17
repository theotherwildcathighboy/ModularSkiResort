// SnowGunPlumeRules — T46 (2026-10-17; docs/research/snow-gun-plume-2026-10-17.md): the PURE rules of "the guns of this
// run are running", shared by the snow injection (whether a gun run's cold texels are raised this pass, and so whether they
// are billed) and SkiSnowGunPlumeSystem (whether a gun shows its vapour plume). One predicate, two readers: the plume can
// never show on a run the injection is not raising, and the bill can never run while the guns are stopped.
//
// THE RULE (Richard, 2026-10-17, via the coordinator): a run's guns RUN only when
//   (a) the temperature gate passes (the injection's per-texel thermal test; a run with no cold texel is never "running"),
//   (b) the run's pack is below the gun stop depth, with hysteresis: guns stop when the pack reaches
//       RunSnowRules.GunStopDepthM (within the merge's own at-target tolerance, RunSnowRules.HalfQuantum, MergeRowsJob
//       "sub-quantum shortfall is not a raise"), and start again only below GunStopDepthM - GunRestartBandM,
//   (c) and never on a pack already that deep from natural snow (the pack measure clamps each texel at its own target, so
//       a deep natural texel reads "at target").
//   Warm-spell protection (T09) holds WARM texels; a warm texel is never cold, so a protection-only pass is never "running".
//
// THE PACK MEASURE (PackDepthM): over the run's cold texels that have a target (the texels the guns would raise this pass),
// GunStopDepthM x sum(min(depth, own target)) / sum(own target), from the snapshot the pass measures from (the previous real
// pass's readback). The own target is the fold's EdgeValue (full stop depth inside the run, the fringe partial at the edge),
// so fringe texels count at their own, lower target and never hold the guns on. NaN when nothing was measured.
//
// THE PLUME (PlumeOn): the setting is on, the run's guns are running this pass (GunsRunning: state Running AND at least one
// cold texel this pass), and the gun's OWN elevation is cold: GunLocalC <= threshold, the same expression and threshold the
// fold evaluates per texel (SnowLineSystem.GetLocalTemperature, freezing + THE margin), so where the snow line crosses a run
// the guns above it plume and the guns below it do not.
//
// THE GATE ON THE PROP (lease): the plume EffectPrefab (SkiSnowGunPlume, m_RequiredFlags = Disaster 0x800) is enabled while
// Game.Buildings.EarlyDisasterWarningDuration.m_EndFrame > the simulation frame (EffectControlData.cs:419-424). A running
// gun holds a lease that ends PlumeLeaseFrames ahead and is renewed when less than PlumeRenewFrames is left; a stopped gun's
// lease is cut to 0. Without our code (mod removed but assets kept) every lease runs out on its own.
//
// PURE FILE: `using System;` only. Compiled into the mod by the glob and linked into tests/ResortBatch (SnowGunPlumeTests).
// C# 9 / net48.

using System;

namespace SkiResortMod.Systems
{
    /// <summary>T46: the guns-running predicate and the plume lease (header).</summary>
    internal static class SnowGunPlumeRules
    {
        /// <summary>A gun run's state. Unknown = no pack measured yet this load: treated as stopped (no raise, no plume)
        /// until the first measurement, so a load in deep winter never flashes the plumes.</summary>
        public enum GunState : byte { Unknown = 0, Stopped = 1, Running = 2 }

        /// <summary>The merge's at-target tolerance in metres (half a 16-bit quantum of the 8 m scale, 0.06 mm).</summary>
        public const float AtTargetToleranceM = RunSnowRules.HalfQuantum * RunSnowRules.SnowHeightScaleM;

        /// <summary>The pack at or above which the guns stop.</summary>
        public const float StopAtM = RunSnowRules.GunStopDepthM - AtTargetToleranceM;

        /// <summary>The pack below which stopped guns start again.</summary>
        public const float RestartBelowM = RunSnowRules.GunStopDepthM - RunSnowRules.GunRestartBandM;

        /// <summary>The plume lease, in simulation frames: 16,384 = 1.5 game hours (10,922.7 frames per game hour,
        /// RunSnowRules.FramesPerGameHour). Renewed when less than PlumeRenewFrames is left (every pass of the plume system
        /// is PlumeIntervalFrames apart, 128 x shorter than the renew window).</summary>
        public const uint PlumeLeaseFrames = 16384;
        public const uint PlumeRenewFrames = 8192;

        /// <summary>The plume system's cadence (sim frames): the injection's top-up window, so a gun follows its run within
        /// one window of the injection's own decision (0.006 game hours).</summary>
        public const int PlumeIntervalFrames = RunSnowRules.TopupIntervalFrames;

        /// <summary>= the SkiSnowGunPlume EffectPrefab's VFX.m_MaxCount (VFXSystem.cs:154-162; over it an instance is
        /// dropped silently, :224). The plume system WARNs once per load when more guns than this want a plume.</summary>
        public const int PlumeMaxCount = 256;

        /// <summary>The run's pack in metres on the stop-depth scale (header THE PACK MEASURE); NaN when nothing was
        /// measured (targetSum not above zero, or a non-finite sum).</summary>
        public static float PackDepthM(double targetSum, double packSum)
        {
            if (!(targetSum > 0.0) || double.IsNaN(packSum) || double.IsInfinity(packSum) || double.IsInfinity(targetSum))
                return float.NaN;
            double ratio = packSum / targetSum;
            if (ratio < 0.0)
                ratio = 0.0;
            else if (ratio > 1.0)
                ratio = 1.0;
            return (float)(RunSnowRules.GunStopDepthM * ratio);
        }

        /// <summary>One texel's contribution to the pack measure: min(depth, own target), both normalized (the sums are
        /// taken in the injection's normalized units; the ratio is unit-free). Burst-called.</summary>
        public static float PackTerm(float depthNorm, float ownTargetNorm)
        {
            return depthNorm < ownTargetNorm ? depthNorm : ownTargetNorm;
        }

        /// <summary>The hysteresis (header (b), (c)). NaN = no measurement this pass: the state is kept (Unknown stays
        /// Unknown). At or above StopAtM: Stopped. Below RestartBelowM: Running. In the band: kept, and Unknown becomes
        /// Stopped (a run loaded inside the band waits for the band to be crossed, like a run that just reached the stop).</summary>
        public static GunState NextGunState(GunState previous, float packM)
        {
            if (float.IsNaN(packM))
                return previous;
            if (packM >= StopAtM)
                return GunState.Stopped;
            if (packM < RestartBelowM)
                return GunState.Running;
            return previous == GunState.Running ? GunState.Running : GunState.Stopped;
        }

        /// <summary>Whether a gun run's cold texels are raised this pass (the fold writes their target into the mask, which
        /// is also what the upkeep bills): only while the state is Running.</summary>
        public static bool RaisesThisPass(GunState state)
        {
            return state == GunState.Running;
        }

        /// <summary>"The guns of this run are running this pass": Running and at least one texel cold enough (header (a)).</summary>
        public static bool GunsRunning(GunState state, int coldTexels)
        {
            return state == GunState.Running && coldTexels > 0;
        }

        /// <summary>The plume of one gun (header THE PLUME). NaN temperatures read warm.</summary>
        public static bool PlumeOn(bool settingOn, bool runGunsRunning, float gunLocalC, float thresholdC)
        {
            return settingOn && runGunsRunning && gunLocalC <= thresholdC;
        }

        /// <summary>The lease end for a plume switched on (or renewed) at <paramref name="frame"/>, saturating at
        /// uint.MaxValue.</summary>
        public static uint LeaseEnd(uint frame)
        {
            return frame > uint.MaxValue - PlumeLeaseFrames ? uint.MaxValue : frame + PlumeLeaseFrames;
        }

        /// <summary>The game's own test (EffectControlData.cs:422): the plume condition holds while frame &lt; end.</summary>
        public static bool LeaseLive(bool hasLease, uint endFrame, uint frame)
        {
            return hasLease && frame < endFrame;
        }

        /// <summary>Whether the gate must be written this pass: on and (no lease, an expired one, or less than
        /// PlumeRenewFrames left), or off with a live lease to cut.</summary>
        public static bool NeedsWrite(bool want, bool hasLease, uint endFrame, uint frame)
        {
            bool live = LeaseLive(hasLease, endFrame, frame);
            if (!want)
                return live;
            return !live || endFrame - frame < PlumeRenewFrames;
        }

        /// <summary>Whether the visible state flips (the only case that needs Game.Common.EffectsUpdated): a renewal of a
        /// live lease does not.</summary>
        public static bool Flips(bool want, bool hasLease, uint endFrame, uint frame)
        {
            return want != LeaseLive(hasLease, endFrame, frame);
        }
    }
}
