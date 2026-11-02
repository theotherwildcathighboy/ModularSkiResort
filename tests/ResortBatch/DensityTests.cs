// Tests for the settle rule, the prop classes and the mast aim (RunPropDensityRules, MastAimRules). Oracles are literal hand values (evaluated by
// hand and with node), never a second run of the code. The placer's walk has its own file: PlacerTests.cs.
// Not testable headless: the ECS writes, the prop census.
// Engine notes: EngineNotes/ResortBatch.DensityTests.md

using System;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class DensityTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            SectionSettle();
            SectionClassOf();
            SectionAllowed();
            SectionAim();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        private static void SectionSettle()
        {
            const string w = "density settle";
            Check(RunPropDensityRules.SettleFrames == 30, w, "SettleFrames != 30");
            Check(RunPropDensityRules.NextStable(0, false) == 1, w, "NextStable(0, false) != 1");
            Check(RunPropDensityRules.NextStable(29, false) == 30, w, "NextStable(29, false) != 30");
            Check(RunPropDensityRules.NextStable(30, false) == 30, w, "NextStable(30, false) != 30 (capped)");
            Check(RunPropDensityRules.NextStable(17, true) == 0, w, "NextStable(17, true) != 0 (a change restarts the count)");
            Check(RunPropDensityRules.NextStable(-4, false) == 1, w, "NextStable(-4, false) != 1");
        }

        private static void SectionClassOf()
        {
            const string w = "density classes";
            Check(RunPropDensityRules.ClassOf("SkiPisteFloodlightTall", true) == RunPropDensityRules.ClassMast, w, "ClassOf(SkiPisteFloodlightTall, night only) != Mast");
            Check(RunPropDensityRules.ClassOf("SkiPropNone", false) == RunPropDensityRules.ClassEmpty, w, "ClassOf(SkiPropNone) != Empty");
            Check(RunPropDensityRules.ClassOf("SkiHydrantBoxBare", false) == RunPropDensityRules.ClassHydrant, w, "ClassOf(SkiHydrantBoxBare) != Hydrant");
            Check(RunPropDensityRules.ClassOf("SkiSnowGunFanIdle", false) == RunPropDensityRules.ClassGun, w, "ClassOf(SkiSnowGunFanIdle) != Gun");
            Check(RunPropDensityRules.ClassOf(null, false) == RunPropDensityRules.ClassOther, w, "ClassOf(null) != Other");
            Check(RunPropDensityRules.ClassOf("", true) == RunPropDensityRules.ClassOther, w, "ClassOf(\"\", night only) != Other");
        }

        // Which classes stand on which kind. Literal table: kind x (gun, hydrant, mast unlit, mast lit); the empty marker and "other" never stand.
        private static void SectionAllowed()
        {
            const string w = "density allowed";
            int[] kinds = { (int)RunKind.Groomed, (int)RunKind.Snowmaking, (int)RunKind.Night, (int)RunKind.Natural, (int)RunKind.CatTrack };
            bool[,] table =
            {
                // gun    hydrant  mast    mast lit
                { false, false, false, false },   // groomed
                { true,  true,  false, false },   // snowmaking: never masts, lit or not
                { true,  true,  true,  true  },   // night
                { false, false, false, false },   // natural
                { true,  true,  false, true  },   // cat track: masts only when lit
            };
            for (int i = 0; i < kinds.Length; i++)
            {
                int k = kinds[i];
                Check(RunPropDensityRules.PropClassAllowed(k, RunPropDensityRules.ClassGun, false) == table[i, 0], w, $"kind {k}: gun");
                Check(RunPropDensityRules.PropClassAllowed(k, RunPropDensityRules.ClassHydrant, false) == table[i, 1], w, $"kind {k}: hydrant");
                Check(RunPropDensityRules.PropClassAllowed(k, RunPropDensityRules.ClassMast, false) == table[i, 2], w, $"kind {k}: mast, unlit");
                Check(RunPropDensityRules.PropClassAllowed(k, RunPropDensityRules.ClassMast, true) == table[i, 3], w, $"kind {k}: mast, lit");
                foreach (bool lit in new[] { false, true })
                {
                    Check(!RunPropDensityRules.PropClassAllowed(k, RunPropDensityRules.ClassEmpty, lit), w, $"kind {k}: the empty marker never stands");
                    Check(!RunPropDensityRules.PropClassAllowed(k, RunPropDensityRules.ClassOther, lit), w, $"kind {k}: other never stands");
                }
            }
            Check(!RunPropDensityRules.PropClassAllowed(99, RunPropDensityRules.ClassGun, true), w, "an unknown kind carries nothing");
        }

        // Travel +x, ring clockwise (ccw false): interior on the right, inward (0,-1). Counter-clockwise: inward (0,1).
        // Fall 0.075 m/m: k 0.5, bias 0.5 x tan 25 deg = 0.23315, aim (0.22707,-0.97389); a rise mirrors the sign; a fall of 0.3 clamps k to 1, exactly
        // 25 deg (0.42262,-0.90631).
        private static void SectionAim()
        {
            const string w = "density aim";
            Check(MastAimRules.BiasMaxDeg == 25f && MastAimRules.SlopeFull == 0.15f, w, "constants 25 / 0.15");

            Aim(w, 1f, 0f, false, 0f, 0.0, -1.0, "flat, clockwise, inward");
            Aim(w, 1f, 0f, true, 0f, 0.0, 1.0, "flat, counter-clockwise, inward");
            Aim(w, 0f, 1f, true, 0f, -1.0, 0.0, "travel +z, counter-clockwise: inward -x");
            Aim(w, 5f, 0f, false, 0f, 0.0, -1.0, "a tangent need not be unit");
            Aim(w, 1f, 0f, false, 0.075f, 0.22707, -0.97389, "half the fall, half the lean");
            Aim(w, 1f, 0f, false, -0.075f, -0.22707, -0.97389, "a rise mirrors");
            Aim(w, 1f, 0f, false, 0.3f, 0.42262, -0.90631, "clamped to 25 deg");
            Aim(w, 1f, 0f, false, float.NaN, 0.0, -1.0, "a NaN fall reads flat");
            Check(!MastAimRules.AimFromTangent(0f, 0f, true, 0f, out _, out _), w, "a zero tangent has no aim");

            Check(Near(MastAimRules.AngleDeg(1f, 0f, 0f, 1f), 90.0, 1e-3), w, "AngleDeg 90");
            Check(Near(MastAimRules.AngleDeg(1f, 0f, -1f, 0f), 180.0, 1e-3), w, "AngleDeg 180");
            Check(Near(MastAimRules.AngleDeg(1f, 0f, 0.70710678f, -0.70710678f), 45.0, 1e-3), w, "AngleDeg 45, unsigned");
        }

        private static void Aim(string w, float tx, float tz, bool ccw, float fall, double ex, double ez, string what)
        {
            bool ok = MastAimRules.AimFromTangent(tx, tz, ccw, fall, out float ax, out float az);
            Check(ok && Near(ax, ex, 1e-4) && Near(az, ez, 1e-4), w, $"{what}: ({ax:F5},{az:F5}), expected ({ex:F5},{ez:F5})");
        }
    }
}
