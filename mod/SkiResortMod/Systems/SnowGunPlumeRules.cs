// Pure rules for "the guns of this run are running", shared by the snow injection (whether a gun run's cold texels are raised, and so
// billed, this pass) and SkiSnowGunPlumeSystem (whether a gun plumes), so a plume never shows on a run the injection is not raising and
// the bill never runs while the guns are stopped. Linked into tests/ResortBatch (SnowGunPlumeTests); `using System;` only.
// A run's guns run only when the temperature gate passes (some cold texel), the pack is below the stop depth with hysteresis (stop at
// GunStopDepthM, restart below GunStopDepthM - GunRestartBandM), the pack is not already that deep from natural snow, and it is the
// gun season (InGunSeason, around the climate's winter). Plume: setting on, the run's guns running, and the gun's own elevation cold.
// Lease: the plume effect is enabled while EarlyDisasterWarningDuration.m_EndFrame > the sim frame (EffectControlData.cs:419-424).
// Engine notes: EngineNotes/SnowGunPlumeRules.md

using System;

namespace SkiResortMod.Systems
{
    /// <summary>The guns-running predicate and the plume lease (see engine notes).</summary>
    internal static class SnowGunPlumeRules
    {
        /// <summary>A gun run's state. Unknown = no pack measured yet this load: treated as stopped until the first
        /// measurement, so a load in deep winter never flashes plumes.</summary>
        public enum GunState : byte { Unknown = 0, Stopped = 1, Running = 2 }

        /// <summary>The merge's at-target tolerance in meters (half a 16-bit quantum of the 8 m scale, 0.06 mm).</summary>
        public const float AtTargetToleranceM = RunSnowRules.HalfQuantum * RunSnowRules.SnowHeightScaleM;

        /// <summary>The pack at or above which the guns stop.</summary>
        public const float StopAtM = RunSnowRules.GunStopDepthM - AtTargetToleranceM;

        /// <summary>The pack below which stopped guns start again.</summary>
        public const float RestartBelowM = RunSnowRules.GunStopDepthM - RunSnowRules.GunRestartBandM;

        /// <summary>The plume lease in simulation frames: 16,384 = 1.5 game hours (10,922.7 frames per game hour,
        /// RunSnowRules.FramesPerGameHour), renewed when less than PlumeRenewFrames is left.</summary>
        public const uint PlumeLeaseFrames = 16384;
        public const uint PlumeRenewFrames = 8192;

        /// <summary>The plume system's cadence in sim frames: the injection's top-up window, so a gun follows its run within
        /// one window of the injection's decision.</summary>
        public const int PlumeIntervalFrames = RunSnowRules.TopupIntervalFrames;

        /// <summary>The SkiSnowGunPlume EffectPrefab's VFX.m_MaxCount (VFXSystem.cs:154-162; over it an instance is dropped
        /// silently, :224). The plume system WARNs once per load when more guns than this want a plume.</summary>
        public const int PlumeMaxCount = 256;

        /// <summary>The run's pack in meters on the stop-depth scale; NaN when nothing was measured (targetSum not above
        /// zero, or a non-finite sum).</summary>
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

        /// <summary>One texel's contribution to the pack measure: min(depth, own target), both normalized (the ratio is
        /// unit-free). Burst-called.</summary>
        public static float PackTerm(float depthNorm, float ownTargetNorm)
        {
            return depthNorm < ownTargetNorm ? depthNorm : ownTargetNorm;
        }

        /// <summary>The hysteresis. NaN = no measurement this pass: the state is kept. At or above StopAtM: Stopped. Below
        /// RestartBelowM: Running. In the band: kept, and Unknown becomes Stopped (a run loaded inside the band waits for the
        /// band to be crossed).</summary>
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

        /// <summary>The calendar gun season, normalized year date: 1 October (9/12) to 1 April (15/12, wrapping).</summary>
        public const float GunSeasonStart = 9f / 12f;
        public const float GunSeasonEnd = 15f / 12f;

        /// <summary>Whether snow guns may run now. A vanilla-named season decides where it is clear ("SeasonWinter" in,
        /// "SeasonSummer" out: a map whose winter falls in June keeps its guns); any other name uses the window around a
        /// December to February winter, October to March (the overload below takes the climate's own winter).</summary>
        public static bool InGunSeason(string seasonName, float normalizedDate) =>
            InGunSeason(seasonName, normalizedDate, GunSeasonStart + MonthsBeforeWinter / 12f, GunSeasonEnd - MonthsAfterWinter / 12f);

        /// <summary>How far the gun season reaches around the climate's winter. With a December to February winter
        /// (SnowLineRules.WinterWindow) this is October to March; a map whose winter falls in June runs April to September.</summary>
        public const float MonthsBeforeWinter = 2f;
        public const float MonthsAfterWinter = 1f;

        /// <summary>InGunSeason around the climate's own winter (normalized year; the end may pass 1).</summary>
        public static bool InGunSeason(string seasonName, float normalizedDate, float winterStart, float winterEnd)
        {
            if (seasonName == "SeasonWinter")
                return true;
            if (seasonName == "SeasonSummer")
                return false;
            float start = winterStart - MonthsBeforeWinter / 12f;
            float span = winterEnd + MonthsAfterWinter / 12f - start;
            float d = normalizedDate - start + 1e-5f;   // the first instant of the window stays inside it after float rounding
            return d - (float)Math.Floor(d) < span;
        }

        /// <summary>RaisesThisPass inside the season only.</summary>
        public static bool RaisesThisPass(GunState state, bool inSeason)
        {
            return inSeason && RaisesThisPass(state);
        }

        /// <summary>GunsRunning inside the season only.</summary>
        public static bool GunsRunning(GunState state, int coldTexels, bool inSeason)
        {
            return inSeason && GunsRunning(state, coldTexels);
        }

        /// <summary>Whether a gun run's cold texels are raised this pass (the fold writes their target into the mask, which
        /// is also what the upkeep bills): only while Running.</summary>
        public static bool RaisesThisPass(GunState state)
        {
            return state == GunState.Running;
        }

        /// <summary>Running and at least one texel cold enough.</summary>
        public static bool GunsRunning(GunState state, int coldTexels)
        {
            return state == GunState.Running && coldTexels > 0;
        }

        /// <summary>A cat track's guns: it is in no mask (the injection grid cannot measure a 6-12 m ribbon), so there is no pack and no
        /// texel. They can run in the gun season while part of the track is cold enough: <paramref name="coldShare"/> =
        /// RunSnowRules.EstimatedShare with the track's own threshold (freezing + the gun margin).</summary>
        public static bool TrackGunsCanRun(bool inSeason, float coldShare)
        {
            return inSeason && coldShare > 0f;
        }

        /// <summary>The hectares a cat track bills while its guns can run and are on: its plan area (m2, Geometry.m_SurfaceArea), since it
        /// has no active texels. <paramref name="gunsOn"/> = its snow gun value is above Off (SkiRunPropSystem.CatTrackGunsOn): a track
        /// set to Off stands no gun and bills nothing (DECISIONS 563). 0 for a plan area that is not a finite positive
        /// number.</summary>
        public static float TrackBilledHa(float planAreaM2, bool gunsCanRun, bool gunsOn)
        {
            if (!gunsCanRun || !gunsOn || float.IsNaN(planAreaM2) || float.IsInfinity(planAreaM2) || planAreaM2 <= 0f)
                return 0f;
            return planAreaM2 / 10000f;
        }

        /// <summary>The plume of one gun. NaN temperatures read warm.</summary>
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
