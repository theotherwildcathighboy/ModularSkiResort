// DensityTests — Round M2 test file for tests/ResortBatch: the Options prop density rules (RunPropDensityRules;
// docs/research/round-m2/understand/density.md §3.1 with density-check.md, build-contract.md §7 B5, ruling R8).
// Written 2026-10-10 (Round M2, builder B5). The R9 convention: `public static class DensityTests` with
// `public static void Run(Func<bool, string, string, bool> check)` and its own Near; Program.Main calls
// DensityTests.Run(Check). Sections D1-D8 = density.md §3.1 A-H (the contract's ids), one check per row:
// 2 + 5 + 9 + 17 + (22 + 4) + (4 + 6) + (7 + 6) + 24 = 106 checks.
//
// ORACLES are literal hand values (density.md §2.1 formulas evaluated by hand and with node; every row re-derived by
// the skeptic check), never a second run of the same code:
//   D1 constants: PctMin 0, PctMax 100, PctStep 5, PctDefault 100, SettleFrames 30; ClassOther..ClassEmpty = 0..4.
//   D2 ClampPct; D3 SnowmakingChance (base, g): round(base × g / 100), half up (7 × 50 % = 3.5 → 4).
//   D4 NightChance (base 100; NM 200 shared = guns 110 + hydrants 40 + empty marker 50, M 150 masts; g, m), the
//      no-mast and no-shared groups, and the empty group.
//   D5 MastWeight (base, g, m) for the three masts 100 / 28 / 22 (the 5 % row rounds 1.4 and 1.1 up to the floor 1)
//      and NonMastWeight (base, g). At (100, 100) every written value equals its base: the default look is today's.
//   D6 NextStable and ApplyDue: the settle rule (one apply per settled change; forced at a load; -1 = nothing applied).
//   D7 ExpectedTenths (the census expectation) and ClassOf (census classes by name).
//   D8 a property test against a TEST-ONLY mirror of the engine's draw order for one area corner
//      (Game.Objects/SecondaryObjectSystem.cs, group branch of CreateSecondaryObject, 1.6.0f1 :3075-3286 = 1.6.2f1:
//      its diff changes only :3499 and :3617, no shift) with Unity.Mathematics.Random from UnityMathematics.NoDeps 1.3.2
//      (the toolchain's com.unity.mathematics@1.3.2, ResortBatch.csproj): H1 thinning is monotone (a lower chance
//      keeps a subset of the corners, same pick and seed); H2 the main stream does not depend on the weights; H3 the
//      night shares match the formula in both member orders (20,000 corners: sd about 0.35 pp, tolerance ±1.0 pp;
//      checked beforehand with a JavaScript port of the xorshift32 generator: all 24 pass, the largest gap 0.905 pp on
//      "masts first" (100, 100) masts, 43.805 % against 42.9); H4 one state step per NextInt whatever the bound, with NextInt(1) in place of the spec's
//      NextInt(0) (density-check gap 9, build-contract Decided here 17).
// Not testable headless: the ECS writes, the ChangedDistrict re-stamp, the census (field sheet, density.md §3.2).

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
            SectionD1();   // constants (2)
            SectionD2();   // ClampPct (5)
            SectionD3();   // SnowmakingChance (9)
            SectionD4();   // NightChance (17)
            SectionD5();   // MastWeight (22) + NonMastWeight (4)
            SectionD6();   // NextStable (4) + ApplyDue (6)
            SectionD7();   // ExpectedTenths (7) + ClassOf (6)
            SectionD8();   // engine mirror (24)
            SectionD9();   // T22 mast aim (MastAimRules)
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        // ================================================================== D9. T22 mast aim (MastAimRules)
        // Hand oracles (2026-10-16): a 20 m square, x right / z up. (0,0)->(0,20)->(20,20)->(20,0) is CLOCKWISE
        // (shoelace -800), interior on the RIGHT of travel; the reverse order is counter-clockwise (interior left).
        // At the corner (0,20) the two inward normals are (1,0) and (0,-1): aim (0.7071,-0.7071). At the mid-edge node
        // (10,20) of the top side both edges run +x, inward (0,-1). Fall 1.5 m over the 10 m out-edge = 0.15 m/m,
        // averaged with a flat in-edge = 0.075, so k 0.5, bias 0.5 x tan 25 deg = 0.23315, aim (0.22707,-0.97389);
        // a 3 m drop clamps k to 1 and gives exactly 25 deg (0.42262,-0.90631); a rise mirrors the sign.

        private static void SectionD9()
        {
            const string w = "density D9";
            Check(MastAimRules.BiasMaxDeg == 25f && MastAimRules.SlopeFull == 0.15f && MastAimRules.MatchRadiusM == 0.6f &&
                  MastAimRules.WriteToleranceDeg == 1.5f, w, "constants 25 / 0.15 / 0.6 / 1.5");

            float[] cx = { 0f, 0f, 20f, 20f }, cz = { 0f, 20f, 20f, 0f }, cy = { 0f, 0f, 0f, 0f };
            Check(Near(MastAimRules.SignedArea2(cx, cz, 4), -800.0, 1e-3), w, "CW square shoelace x2 = -800");
            float[] rx = { 0f, 20f, 20f, 0f }, rz = { 0f, 0f, 20f, 20f }, ry = { 0f, 0f, 0f, 0f };
            Check(Near(MastAimRules.SignedArea2(rx, rz, 4), 800.0, 1e-3), w, "CCW square shoelace x2 = +800");

            const double s2 = 0.70710678;
            float ax, az;
            Check(MastAimRules.Aim(cx, cy, cz, 4, 1, out ax, out az) && Near(ax, s2, 1e-4) && Near(az, -s2, 1e-4), w,
                  $"CW corner (0,20) aims ({ax:F4},{az:F4}), want (0.7071,-0.7071)");
            Check(MastAimRules.Aim(rx, ry, rz, 4, 3, out ax, out az) && Near(ax, s2, 1e-4) && Near(az, -s2, 1e-4), w,
                  $"CCW corner (0,20) aims ({ax:F4},{az:F4}), want (0.7071,-0.7071) (winding-proof)");
            // Every corner of both windings aims toward the centre (10,10).
            bool allIn = true;
            for (int k = 0; k < 4; k++)
            {
                if (!MastAimRules.Aim(cx, cy, cz, 4, k, out ax, out az) || ax * (10f - cx[k]) + az * (10f - cz[k]) <= 0f) allIn = false;
                if (!MastAimRules.Aim(rx, ry, rz, 4, k, out ax, out az) || ax * (10f - rx[k]) + az * (10f - rz[k]) <= 0f) allIn = false;
            }
            Check(allIn, w, "every corner of both windings aims toward the centre");

            // Mid-edge node on the top side (CW ring with a fifth node).
            float[] mx = { 0f, 0f, 10f, 20f, 20f }, mz = { 0f, 20f, 20f, 20f, 0f }, my = { 0f, 0f, 0f, 0f, 0f };
            Check(MastAimRules.Aim(mx, my, mz, 5, 2, out ax, out az) && Near(ax, 0.0, 1e-5) && Near(az, -1.0, 1e-5), w,
                  $"flat mid-edge aims ({ax:F4},{az:F4}), want (0,-1)");
            my[3] = -1.5f;   // 0.15 m/m over the out-edge, flat in-edge
            Check(MastAimRules.Aim(mx, my, mz, 5, 2, out ax, out az) && Near(ax, 0.22707, 1e-3) && Near(az, -0.97389, 1e-3), w,
                  $"downhill along travel aims ({ax:F4},{az:F4}), want (0.2271,-0.9739)");
            my[3] = 1.5f;
            Check(MastAimRules.Aim(mx, my, mz, 5, 2, out ax, out az) && Near(ax, -0.22707, 1e-3) && Near(az, -0.97389, 1e-3), w,
                  $"uphill along travel aims ({ax:F4},{az:F4}), want (-0.2271,-0.9739)");
            my[3] = -3f;
            Check(MastAimRules.Aim(mx, my, mz, 5, 2, out ax, out az) && Near(ax, 0.42262, 1e-3) && Near(az, -0.90631, 1e-3), w,
                  $"steep downhill clamps to 25 deg: ({ax:F4},{az:F4}), want (0.4226,-0.9063)");
            Check(Near(MastAimRules.AngleDeg(0f, -1f, ax, az), 25.0, 0.01), w, "the clamp is exactly 25 deg off the inward normal");

            // NearestNode: the prop sits on the node; 0.6 m radius.
            Check(MastAimRules.NearestNode(mx, mz, 5, 10.3f, 20.2f) == 2, w, "mast 0.36 m from node 2 matches it");
            Check(MastAimRules.NearestNode(mx, mz, 5, 5f, 20f) == -1, w, "mast 5 m from every node: no match");

            // Degenerate rings.
            float[] two = { 0f, 5f }, z2 = { 0f, 0f }, y2 = { 0f, 0f };
            Check(!MastAimRules.Aim(two, y2, z2, 2, 0, out ax, out az), w, "2 nodes: false");
            float[] lx = { 0f, 5f, 10f }, lz = { 0f, 0f, 0f }, ly = { 0f, 0f, 0f };
            Check(!MastAimRules.Aim(lx, ly, lz, 3, 1, out ax, out az), w, "collinear (zero area): false");
            Check(!MastAimRules.Aim(cx, cy, cz, 4, 9, out ax, out az), w, "node index out of range: false");

            // AngleDeg.
            Check(Near(MastAimRules.AngleDeg(0f, 1f, 1f, 0f), 90.0, 1e-4) && Near(MastAimRules.AngleDeg(1f, 0f, 1f, 0f), 0.0, 1e-4) &&
                  Near(MastAimRules.AngleDeg(0f, -1f, 0f, 1f), 180.0, 1e-4), w, "AngleDeg 90 / 0 / 180");

            // Determinism: the same ring twice gives bit-identical aims.
            MastAimRules.Aim(mx, my, mz, 5, 2, out float bx, out float bz);
            MastAimRules.Aim(mx, my, mz, 5, 2, out float dx, out float dz);
            Check(bx == dx && bz == dz, w, "same ring, same aim");
        }

        // ================================================================== D1. constants

        private static void SectionD1()
        {
            const string w = "density D1";
            Check(RunPropDensityRules.PctMin == 0 && RunPropDensityRules.PctMax == 100 && RunPropDensityRules.PctStep == 5 &&
                  RunPropDensityRules.PctDefault == 100 && RunPropDensityRules.SettleFrames == 30,
                  w, "slider constants != 0 / 100 / 5 / 100, settle 30 (the oracles below are literal for them)");
            Check(RunPropDensityRules.ClassOther == 0 && RunPropDensityRules.ClassGun == 1 && RunPropDensityRules.ClassHydrant == 2 &&
                  RunPropDensityRules.ClassMast == 3 && RunPropDensityRules.ClassEmpty == 4,
                  w, "class constants != 0..4 (Other, Gun, Hydrant, Mast, Empty)");
        }

        // ================================================================== D2. ClampPct

        private static void SectionD2()
        {
            const string w = "density D2";
            int[,] rows =
            {
                // pct, expected
                { -5, 0 },
                { 0, 0 },
                { 55, 55 },
                { 100, 100 },
                { 140, 100 },
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                int got = RunPropDensityRules.ClampPct(rows[i, 0]);
                Check(got == rows[i, 1], w, $"ClampPct({rows[i, 0]}) = {got}, expected {rows[i, 1]}");
            }
        }

        // ================================================================== D3. SnowmakingChance

        private static void SectionD3()
        {
            const string w = "density D3";
            int[,] rows =
            {
                // base, gun %, expected chance
                { 100, 100, 100 },
                { 100, 60, 60 },
                { 100, 5, 5 },
                { 100, 0, 0 },
                { 100, 101, 100 },   // the slider clamped to 100
                { 100, -1, 0 },      // the slider clamped to 0
                { 80, 50, 40 },
                { 80, 55, 44 },
                { 7, 50, 4 },        // 3.5 rounds half up
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                int got = RunPropDensityRules.SnowmakingChance(rows[i, 0], rows[i, 1]);
                Check(got == rows[i, 2], w, $"SnowmakingChance({rows[i, 0]}, {rows[i, 1]}) = {got}, expected {rows[i, 2]}");
            }
        }

        // ================================================================== D4. NightChance

        private static void SectionD4()
        {
            const string w = "density D4";
            int[,] rows =
            {
                // base, gun %, mast %, NM (shared), M (masts), expected chance
                { 100, 100, 100, 200, 150, 100 },
                { 100, 60, 100, 200, 150, 77 },    // 100 × (12000 + 15000) / 35000 = 77.14
                { 100, 100, 60, 200, 150, 83 },    // (20000 + 9000) / 350 = 82.86
                { 100, 50, 100, 200, 150, 71 },    // 71.43
                { 100, 50, 50, 200, 150, 50 },     // equal sliders: chance = slider
                { 100, 100, 50, 200, 150, 79 },    // 78.57
                { 100, 100, 0, 200, 150, 57 },     // 57.14
                { 100, 0, 100, 200, 150, 43 },     // 42.86
                { 100, 0, 0, 200, 150, 0 },        // both off
                { 100, 5, 100, 200, 150, 46 },     // 45.71
                { 100, 100, 5, 200, 150, 59 },     // 59.29
                { 100, 60, 40, 200, 150, 51 },     // 51.43
                { 100, 37, 83, 200, 150, 57 },     // 56.71
                { 100, 60, 100, 200, 0, 60 },      // no masts: the gun slider alone
                { 100, 0, 100, 200, 0, 0 },
                { 100, 100, 100, 0, 150, 100 },    // no shared members
                { 100, 100, 100, 0, 0, 0 },        // empty group
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                int got = RunPropDensityRules.NightChance(rows[i, 0], rows[i, 1], rows[i, 2], rows[i, 3], rows[i, 4]);
                Check(got == rows[i, 5], w, $"NightChance({rows[i, 0]}, {rows[i, 1]}, {rows[i, 2]}, NM {rows[i, 3]}, M {rows[i, 4]}) = {got}, expected {rows[i, 5]}");
            }
        }

        // ================================================================== D5. MastWeight, NonMastWeight

        private static void SectionD5()
        {
            const string w = "density D5";
            int[,] masts =
            {
                // base, gun %, mast %, expected weight
                { 100, 100, 100, 100 },
                { 28, 100, 100, 28 },
                { 22, 100, 100, 22 },
                { 100, 60, 100, 167 },     // 166.67
                { 28, 60, 100, 47 },       // 46.67
                { 22, 60, 100, 37 },       // 36.67
                { 100, 50, 100, 200 },
                { 28, 50, 100, 56 },
                { 22, 50, 100, 44 },
                { 100, 100, 50, 50 },
                { 28, 100, 50, 14 },
                { 22, 100, 50, 11 },
                { 100, 100, 5, 5 },
                { 28, 100, 5, 1 },         // 1.4
                { 22, 100, 5, 1 },         // 1.1, the floor 1
                { 100, 0, 100, 100 },      // gun 0: base (the shared weights go to 0 instead)
                { 100, 0, 0, 0 },
                { 100, 100, 0, 0 },
                { 100, 5, 100, 2000 },
                { 28, 5, 100, 560 },
                { 22, 5, 100, 440 },
                { 0, 50, 50, 0 },
            };
            for (int i = 0; i < masts.GetLength(0); i++)
            {
                int got = RunPropDensityRules.MastWeight(masts[i, 0], masts[i, 1], masts[i, 2]);
                Check(got == masts[i, 3], w, $"MastWeight({masts[i, 0]}, {masts[i, 1]}, {masts[i, 2]}) = {got}, expected {masts[i, 3]}");
            }

            int[,] shared =
            {
                // base, gun %, expected weight
                { 25, 100, 25 },
                { 25, 5, 25 },
                { 25, 0, 0 },
                { 50, 0, 0 },
            };
            for (int i = 0; i < shared.GetLength(0); i++)
            {
                int got = RunPropDensityRules.NonMastWeight(shared[i, 0], shared[i, 1]);
                Check(got == shared[i, 2], w, $"NonMastWeight({shared[i, 0]}, {shared[i, 1]}) = {got}, expected {shared[i, 2]}");
            }
        }

        // ================================================================== D6. NextStable, ApplyDue

        private static void SectionD6()
        {
            const string w = "density D6";
            Check(RunPropDensityRules.NextStable(0, false) == 1, w, "NextStable(0, false) != 1");
            Check(RunPropDensityRules.NextStable(29, false) == 30, w, "NextStable(29, false) != 30");
            Check(RunPropDensityRules.NextStable(30, false) == 30, w, "NextStable(30, false) != 30 (capped)");
            Check(RunPropDensityRules.NextStable(17, true) == 0, w, "NextStable(17, true) != 0 (a change restarts the count)");

            Check(!RunPropDensityRules.ApplyDue(60, 100, 100, 100, 29, false), w, "ApplyDue: 29 still frames applied");
            Check(RunPropDensityRules.ApplyDue(60, 100, 100, 100, 30, false), w, "ApplyDue: 30 still frames not applied");
            Check(!RunPropDensityRules.ApplyDue(100, 100, 100, 100, 500, false), w, "ApplyDue: an unchanged pair applied again");
            Check(RunPropDensityRules.ApplyDue(100, 100, 100, 100, 0, true), w, "ApplyDue: the forced load apply skipped");
            Check(!RunPropDensityRules.ApplyDue(60, 100, -1, -1, 0, false), w, "ApplyDue: nothing applied yet, not settled, applied");
            Check(RunPropDensityRules.ApplyDue(60, 100, -1, -1, 30, false), w, "ApplyDue: nothing applied yet, settled, not applied");
        }

        // ================================================================== D7. ExpectedTenths, ClassOf

        private static void SectionD7()
        {
            const string w = "density D7";
            int[,] rows =
            {
                // corners, chance, class weight, total weight, expected tenths
                { 40, 60, 150, 200, 180 },    // 40 × 0.60 × 0.75 = 18.0
                { 31, 100, 150, 350, 133 },   // 13.29
                { 31, 100, 50, 350, 44 },     // 4.43
                { 20, 77, 251, 451, 86 },     // 8.57
                { 10, 0, 150, 200, 0 },
                { 10, 50, 0, 200, 0 },
                { 10, 50, 150, 0, 0 },        // an empty group expects nothing
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                int got = RunPropDensityRules.ExpectedTenths(rows[i, 0], rows[i, 1], rows[i, 2], rows[i, 3]);
                Check(got == rows[i, 4], w, $"ExpectedTenths({rows[i, 0]}, {rows[i, 1]}, {rows[i, 2]}, {rows[i, 3]}) = {got}, expected {rows[i, 4]}");
            }

            Check(RunPropDensityRules.ClassOf("SkiPisteFloodlightTall", true) == RunPropDensityRules.ClassMast, w, "ClassOf(SkiPisteFloodlightTall, night only) != Mast");
            Check(RunPropDensityRules.ClassOf("SkiPropNone", false) == RunPropDensityRules.ClassEmpty, w, "ClassOf(SkiPropNone) != Empty");
            Check(RunPropDensityRules.ClassOf("SkiHydrantBoxBare", false) == RunPropDensityRules.ClassHydrant, w, "ClassOf(SkiHydrantBoxBare) != Hydrant");
            Check(RunPropDensityRules.ClassOf("SkiSnowGunFanIdle", false) == RunPropDensityRules.ClassGun, w, "ClassOf(SkiSnowGunFanIdle) != Gun");
            Check(RunPropDensityRules.ClassOf(null, false) == RunPropDensityRules.ClassOther, w, "ClassOf(null) != Other");
            Check(RunPropDensityRules.ClassOf("", true) == RunPropDensityRules.ClassOther, w, "ClassOf(\"\", night only) != Other");
        }

        // ================================================================== D8. engine mirror (test-only)

        private const uint MirrorSeed = 20261010u;
        private const int ClassG = 0, ClassH = 1, ClassE = 2, ClassM = 3;   // test-local classes of the members below

        // Members "masts last" = the 8 guns, the 2 hydrants, SkiPropNone, the 3 masts (mirror prefabs, density.md §1.2:
        // SkiSnowGun* .Prefab:25, SkiHydrantBox* :25, SkiPropNone :25, SkiPisteFloodlight* :24). "Masts first" = reversed.
        private static readonly int[] GunWeights = { 25, 13, 12, 17, 8, 8, 18, 9 };
        private static readonly int[] HydrantWeights = { 24, 16 };
        private const int EmptyWeight = 50;

        // Mirror of SecondaryObjectSystem.CreateSecondaryObject, group branch, one area corner (1.6.0f1 :3075-3286 =
        // 1.6.2). Every member passes CheckRequirements (no requirement component on ours, :3009-3043) and scores 0 (no
        // traffic data): the first is taken (:3251-3259), the rest tie (:3260-3271). The per-owner selected-spawnable
        // cache (:3246-3250) is left out: a key collision needs the same 32-bit group index (density-check C4).
        // The engine calls NextInt(num2) with num2 = 0 while the first members all weigh 0 (gun 0 %, masts last); Unity's
        // NextInt(max) is one state step returning (int)((state × max) >> 32), 0 for max 0 and 1 alike, so the mirror
        // passes 1 there to stay clear of the package's argument check (density-check gap 9; H4 shows one step whatever
        // the bound). Unity.Mathematics is not in the decompile tree: that body is unverified there, H4 checks the step.
        private static int Corner(ref Unity.Mathematics.Random random, int[] w, int chance, out uint seed)
        {
            seed = 0;
            random.NextInt();                                         // :3077 group index
            int pick = -1, sum = 0;
            var r3 = default(Unity.Mathematics.Random);
            for (int i = 0; i < w.Length; i++)
            {
                var r4 = random;                                      // :3243
                random.NextInt();                                     // :3244
                random.NextInt();                                     // :3245
                if (i == 0)
                {
                    pick = 0; r3 = r4; sum = w[0];                    // :3251-3259
                }
                else
                {
                    sum += w[i];                                      // :3262-3263
                    if (random.NextInt(sum > 0 ? sum : 1) < w[i])     // :3264
                    {
                        pick = i; r3 = r4;
                    }
                }
            }
            if (sum <= 0)
                return -1;                                            // :3273-3276
            if (!(r3.NextInt(100) < chance))
                return -1;                                            // :3279
            seed = r3.NextUInt(268435456u) >> 12;                     // :3392-3393
            return pick;
        }

        /// <summary>Night group weights, masts last: guns, hydrants, empty marker (× shared, 1 or 0), then the masts.</summary>
        private static int[] NightWeights(int shared, int mast0, int mast1, int mast2)
        {
            var w = new int[GunWeights.Length + HydrantWeights.Length + 1 + 3];
            int k = 0;
            for (int i = 0; i < GunWeights.Length; i++) w[k++] = GunWeights[i] * shared;
            for (int i = 0; i < HydrantWeights.Length; i++) w[k++] = HydrantWeights[i] * shared;
            w[k++] = EmptyWeight * shared;
            w[k++] = mast0; w[k++] = mast1; w[k++] = mast2;
            return w;
        }

        private static int[] NightClasses()
        {
            var c = new int[GunWeights.Length + HydrantWeights.Length + 1 + 3];
            int k = 0;
            for (int i = 0; i < GunWeights.Length; i++) c[k++] = ClassG;
            for (int i = 0; i < HydrantWeights.Length; i++) c[k++] = ClassH;
            c[k++] = ClassE;
            c[k++] = ClassM; c[k++] = ClassM; c[k++] = ClassM;
            return c;
        }

        private static int[] SnowWeights()
        {
            var w = new int[GunWeights.Length + HydrantWeights.Length + 1];
            int k = 0;
            for (int i = 0; i < GunWeights.Length; i++) w[k++] = GunWeights[i];
            for (int i = 0; i < HydrantWeights.Length; i++) w[k++] = HydrantWeights[i];
            w[k++] = EmptyWeight;
            return w;
        }

        private static int[] Reversed(int[] a)
        {
            var r = (int[])a.Clone();
            Array.Reverse(r);
            return r;
        }

        /// <summary>Shares in percent of <paramref name="corners"/> night corners: guns+hydrants, masts, empty marker.</summary>
        private static void Shares(int[] w, int[] cls, int chance, int corners, out double gunHydrant, out double mast, out double empty)
        {
            var random = new Unity.Mathematics.Random(MirrorSeed);
            int gh = 0, mm = 0, ee = 0;
            for (int k = 0; k < corners; k++)
            {
                int pick = Corner(ref random, w, chance, out uint _);
                if (pick < 0)
                    continue;
                if (cls[pick] == ClassG || cls[pick] == ClassH) gh++;
                else if (cls[pick] == ClassM) mm++;
                else ee++;
            }
            gunHydrant = 100.0 * gh / corners;
            mast = 100.0 * mm / corners;
            empty = 100.0 * ee / corners;
        }

        private static void SectionD8()
        {
            const string w = "density D8";
            const int H1Corners = 2000, H2Corners = 2000, H3Corners = 20000;
            const double Tolerance = 1.0;   // pp: about 2.9 sd at n = 20,000 (sd 0.35 pp)

            // H1 monotone thinning (snowmaking weights): every corner placed at chance 50 is placed at 100 with the same
            // pick and seed, and about half of them are (binomial sd about 1.3 pp at n = 2,000).
            {
                int[] snow = SnowWeights();
                var a = new Unity.Mathematics.Random(MirrorSeed);
                var b = new Unity.Mathematics.Random(MirrorSeed);
                int placed50 = 0, placed100 = 0, violations = 0;
                for (int k = 0; k < H1Corners; k++)
                {
                    int p50 = Corner(ref a, snow, 50, out uint s50);
                    int p100 = Corner(ref b, snow, 100, out uint s100);
                    if (p100 >= 0) placed100++;
                    if (p50 < 0) continue;
                    placed50++;
                    if (p50 != p100 || s50 != s100) violations++;
                }
                Check(violations == 0, w, $"H1 {violations} corner(s) placed at 50 % but not with the same pick and seed at 100 %");
                double ratio = placed100 > 0 ? (double)placed50 / placed100 : 0.0;
                Check(ratio >= 0.45 && ratio <= 0.55, w, $"H1 placed(50) / placed(100) = {placed50} / {placed100} = {ratio:F4}, expected 0.45..0.55");
            }

            // H2 stream independence: after every corner the main stream's state is the same for the night weights at
            // (100,100), (60,100), (0,100) and (100,0), in both member orders; and a (100,100) pass after a (60,100) pass
            // equals the first (100,100) pass corner for corner.
            {
                int[][] sets =
                {
                    NightWeights(1, 100, 28, 22),   // (100,100): chance 100, base weights
                    NightWeights(1, 167, 47, 37),   // (60,100): masts × 100/60
                    NightWeights(0, 100, 28, 22),   // (0,100): shared 0
                    NightWeights(1, 0, 0, 0),       // (100,0): masts 0
                };
                int[] chances = { 100, 77, 43, 57 };
                int violations = 0;
                for (int order = 0; order < 2; order++)
                {
                    var streams = new Unity.Mathematics.Random[sets.Length];
                    var weights = new int[sets.Length][];
                    for (int s = 0; s < sets.Length; s++)
                    {
                        streams[s] = new Unity.Mathematics.Random(MirrorSeed);
                        weights[s] = order == 0 ? sets[s] : Reversed(sets[s]);
                    }
                    for (int k = 0; k < H2Corners; k++)
                    {
                        for (int s = 0; s < sets.Length; s++)
                            Corner(ref streams[s], weights[s], chances[s], out uint _);
                        for (int s = 1; s < sets.Length; s++)
                            if (streams[s].state != streams[0].state) violations++;
                    }
                }
                Check(violations == 0, w, $"H2 the main stream depends on the weights at {violations} corner(s)");

                var first = new Unity.Mathematics.Random(MirrorSeed);
                var other = new Unity.Mathematics.Random(MirrorSeed);
                var again = new Unity.Mathematics.Random(MirrorSeed);
                int[] firstPick = new int[H2Corners];
                uint[] firstSeed = new uint[H2Corners];
                for (int k = 0; k < H2Corners; k++)
                    firstPick[k] = Corner(ref first, sets[0], 100, out firstSeed[k]);
                for (int k = 0; k < H2Corners; k++)
                    Corner(ref other, sets[1], 77, out uint _);
                int differ = 0;
                for (int k = 0; k < H2Corners; k++)
                {
                    int p = Corner(ref again, sets[0], 100, out uint s);
                    if (p != firstPick[k] || s != firstSeed[k]) differ++;
                }
                Check(differ == 0, w, $"H2 the (100,100) pass after a (60,100) pass differs at {differ} corner(s)");
            }

            // H3 shares of 20,000 night corners in both member orders, against the §2.1 targets.
            {
                int[] cls = NightClasses();
                for (int order = 0; order < 2; order++)
                {
                    string o = order == 0 ? "masts last" : "masts first";
                    int[] c = order == 0 ? cls : Reversed(cls);
                    Func<int[], int[]> ord = a => order == 0 ? a : Reversed(a);

                    // (60,100): chance 77, masts 167/47/37 → guns+hydrants 25.6, masts 42.9, empty marker 8.5.
                    Shares(ord(NightWeights(1, 167, 47, 37)), c, 77, H3Corners, out double gh, out double ms, out double em);
                    Check(Near(gh, 25.6, Tolerance), w, $"H3 {o} (60,100) guns+hydrants {gh:F2} %, expected 25.6 ± 1.0");
                    Check(Near(ms, 42.9, Tolerance), w, $"H3 {o} (60,100) masts {ms:F2} %, expected 42.9 ± 1.0");
                    Check(Near(em, 8.5, Tolerance), w, $"H3 {o} (60,100) empty marker {em:F2} %, expected 8.5 ± 1.0");

                    // (100,100): chance 100, base weights → 42.9 / 42.9 / 14.3.
                    Shares(ord(NightWeights(1, 100, 28, 22)), c, 100, H3Corners, out gh, out ms, out em);
                    Check(Near(gh, 42.9, Tolerance), w, $"H3 {o} (100,100) guns+hydrants {gh:F2} %, expected 42.9 ± 1.0");
                    Check(Near(ms, 42.9, Tolerance), w, $"H3 {o} (100,100) masts {ms:F2} %, expected 42.9 ± 1.0");
                    Check(Near(em, 14.3, Tolerance), w, $"H3 {o} (100,100) empty marker {em:F2} %, expected 14.3 ± 1.0");

                    // (100,0): chance 57, masts 0 → masts exactly 0.
                    Shares(ord(NightWeights(1, 0, 0, 0)), c, 57, H3Corners, out gh, out ms, out em);
                    Check(ms == 0.0, w, $"H3 {o} (100,0) masts {ms:F2} %, expected exactly 0");

                    // (0,100): chance 43, shared 0 → guns, hydrants and empty marker exactly 0, masts 43.0.
                    Shares(ord(NightWeights(0, 100, 28, 22)), c, 43, H3Corners, out gh, out ms, out em);
                    Check(gh == 0.0 && em == 0.0, w, $"H3 {o} (0,100) guns+hydrants {gh:F2} %, empty marker {em:F2} %, expected exactly 0");
                    Check(Near(ms, 43.0, Tolerance), w, $"H3 {o} (0,100) masts {ms:F2} %, expected 43.0 ± 1.0");
                }
            }

            // H4 one state step per NextInt whatever the bound (the mirror's draw count rests on it, density.md C4).
            {
                var a = new Unity.Mathematics.Random(12345u);
                var b = a;
                var c = a;
                a.NextInt(7);
                b.NextInt(350);
                int z = c.NextInt(1);
                Check(a.state == b.state && b.state == c.state, w, $"H4 NextInt(7) / (350) / (1) states {a.state} / {b.state} / {c.state} differ");
                Check(z == 0, w, $"H4 NextInt(1) = {z}, expected 0");
            }
        }
    }
}
