// ComposeTests — unit tests of SkiGladeCompose (run-binding-glade-spec.md §8.3 as amending §4.3):
// moved constants, the §8.2 level tables, ThinHash (the rank key) against ThinSelect and a
// test-local restatement, SeparationHolds, garbage inputs, "Params.Mode / ThinFraction /
// GladeDensity / MaxPlant are not read", PlanKey properties (level-independent, relevance of
// Existing and other-layer trees), the planner's PREFIX PROPERTY and the TIME report.

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
        private static void ComposeUnitTests(List<Fixture> fixtures)
        {
            // ---- moved constants keep their G1 values (spec §2: "same value", "same body").
            Check(SkiGladeCompose.ThinSeed == 0x7A1C3E5Bu, "compose/consts", "ThinSeed is not the G1 value 0x7A1C3E5B");
            uint[] g1Salts = { 0x5C1A9E5Du ^ 0x9E3779B1u, 0x5C1A9E5Du ^ 0x3C6EF362u, 0x5C1A9E5Du ^ 0xDAA66D13u, 0x5C1A9E5Du ^ 0x78DDE6C4u,
                               0x5C1A9E5Du ^ 0x17156075u, 0x5C1A9E5Du ^ 0xB54CDA26u, 0x5C1A9E5Du ^ 0x538453D7u, 0x5C1A9E5Du ^ 0xF1BBCD88u };
            for (int v = 0; v < 8; v++)
                Check(SkiGladeCompose.VariantSalt(v) == g1Salts[v], "compose/consts", "VariantSalt(" + v + ") differs from the G1 body");
            Check(SkiGladeCompose.PlanGeneration == 1, "compose/consts", "PlanGeneration " + SkiGladeCompose.PlanGeneration + " (spec §2: 1; §8 does not amend it)");
            Check(SkiGladeCompose.LayerGlade == 0 && SkiGladeCompose.LayerBelt == 1 && SkiGladeCompose.LayerNone == 255 &&
                  SkiGladeCompose.VariantUnknown == 0xFF, "compose/consts", "layer / VariantUnknown constants");

            // ---- §8.2 / §8.3 tables (typed in from the spec).
            Check(SkiGladeCompose.Levels == 8 && SkiGladeCompose.DefaultLevel == 4, "compose/levels", "Levels " + SkiGladeCompose.Levels + ", DefaultLevel " + SkiGladeCompose.DefaultLevel);
            for (int l = 1; l <= 8; l++)
            {
                double want = OracleDensityPerHa[l - 1] / 10000.0;
                double got = SkiGladeCompose.InteriorDensity(l);
                Check(Math.Abs(got - want) <= 1e-7 * want + 1e-12, "compose/levels", "InteriorDensity(" + l + ") " + F(got) + " vs table " + F(want));
                double bw = OracleBeltFactor[l - 1];
                double bg = SkiGladeCompose.BeltFactor(l);
                Check(Math.Abs(bg - bw) <= 1e-6, "compose/levels", "BeltFactor(" + l + ") " + F(bg) + " vs table " + F(bw));
            }
            Check(SkiGladeCompose.InteriorDensity(0) == SkiGladeCompose.InteriorDensity(1) && SkiGladeCompose.InteriorDensity(-5) == 0f &&
                  SkiGladeCompose.InteriorDensity(9) == SkiGladeCompose.InteriorDensity(8) && SkiGladeCompose.BeltFactor(99) == 1f &&
                  SkiGladeCompose.ClampLevel(0) == 1 && SkiGladeCompose.ClampLevel(255) == 8, "compose/levels", "levels are not clamped to 1..8");
            Check(SkiGladeCompose.InteriorDensity(8) == OracleD8, "compose/levels", "level-8 density is not 143/10000");
            // §8.2 as amended in the G3 fixer round (finding behaviour-2): the stream holds 1.10 × level 8.
            Check(SkiGladeCompose.StreamDensityPerM2 == OracleStreamD && Math.Abs(SkiGladeCompose.StreamDensityPerM2 / OracleD8 - 1.10) < 1e-6,
                "compose/levels", "stream density " + F(SkiGladeCompose.StreamDensityPerM2) + " is not 1.10 × level 8 (157.3/10000)");

            // ---- SeparationHolds (D11).
            Check(SkiGladeCompose.SeparationHolds(TInnerMargin, TBeltGap, TGladeMinSpacing, TBeltMinSpacing), "compose/separation",
                "the tuning (2.5 + 3.1 vs 5.5 / 4.5) does not separate the layers");
            Check(!SkiGladeCompose.SeparationHolds(2.5f, 1.5f, 5.5f, 4.5f), "compose/separation", "the G1 tuning (belt gap 1.5) passes");
            Check(!SkiGladeCompose.SeparationHolds(2.5f, 3.0f, 5.5f, 4.5f), "compose/separation", "equality (no float margin) passes");
            Check(!SkiGladeCompose.SeparationHolds(float.NaN, 3.1f, 5.5f, 4.5f), "compose/separation", "NaN passes");

            ThinHashTests();

            // ---- garbage inputs: never throw, degenerate statuses leave every list empty.
            try
            {
                ComposeResult r = new ComposeResult();
                Check(SkiGladeCompose.Compose(null, r) == GladeStatus.TooFewVertices && Empty(r), "compose/garbage", "null input");
                SkiGladeCompose.Compose(null, null);
                SkiGladeCompose.Compose(new ComposeInput(), null);
                Check(SkiGladeCompose.Compose(new ComposeInput { Params = SimParams(SimTarget.Interior), Level = 1 }, r) == GladeStatus.TooFewVertices && Empty(r),
                    "compose/garbage", "null ring");
                ComposeInput nan = new ComposeInput { Ring = Pts(0, 0, 10, 0, float.NaN, 10, 0, 10), Params = SimParams(SimTarget.Interior), Level = 1, Caps = BigCaps() };
                Check(SkiGladeCompose.Compose(nan, r) == GladeStatus.NonFinite && Empty(r) && SkiGladeCompose.PlanKey(nan) == 0UL, "compose/garbage", "NaN ring");

                float3[] sq = Pts(0, 0, 80, 0, 80, 80, 0, 80);
                float3[] ex = { new float3(10, 0, 10), new float3(40, 0, 40), new float3(float.NaN, 0, 3), new float3(70, 0, 20) };
                ComposeInput nullThin = new ComposeInput { Ring = sq, Params = SimParams(SimTarget.Interior), Existing = ex, Thinnable = null, Level = 1, Caps = BigCaps() };
                SkiGladeCompose.Compose(nullThin, r);
                Check(r.WildInside == 0 && r.FixedInside == 3 && r.Fell.Count == 0 && r.Inside == 3 && r.Target == 0, "compose/garbage",
                    "Thinnable == null must mean nothing thinnable: W " + r.WildInside + ", X " + r.FixedInside + ", fell " + r.Fell.Count);

                ComposeInput negCaps = new ComposeInput { Ring = sq, Params = SimParams(SimTarget.Interior), Existing = ex, Thinnable = new[] { true, true, true, true },
                    Own = new[] { new ComposeMember { Position = new float3(30, 0, 30), Layer = 0, Variant = 6, PlanGen = 1 } },
                    Level = 1, Caps = new ComposeCaps { MaxPlant = -5, MaxFell = -1, MaxRemove = int.MinValue } };
                SkiGladeCompose.Compose(negCaps, r);
                Check(r.Plant.Count == 0 && r.Fell.Count == 0 && r.Remove.Count == 0 && r.Clamped, "compose/garbage", "negative caps: lists not empty or not Clamped");

                // Level 0 reads as 1 (Clear), 200 as 8.
                ComposeInput lv = new ComposeInput { Ring = sq, Params = SimParams(SimTarget.Interior), Existing = ex, Thinnable = new[] { true, true, true, true }, Level = 0, Caps = BigCaps() };
                SkiGladeCompose.Compose(lv, r);
                Check(r.Level == 1 && r.Target == 0 && r.Fell.Count == 3, "compose/garbage", "Level 0: level " + r.Level + ", T " + r.Target + ", fell " + r.Fell.Count);
                lv.Level = 200;
                SkiGladeCompose.Compose(lv, r);
                Check(r.Level == 8 && r.Target == (int)Math.Floor(6400 * OracleD8 + 0.5), "compose/garbage", "Level 200: level " + r.Level + ", T " + r.Target);

                ComposeInput odd = new ComposeInput { Ring = sq, Params = SimParams(SimTarget.Interior), Existing = ex, Thinnable = new[] { true }, Caps = BigCaps(), Level = 4,
                    Own = new[] { new ComposeMember { Position = new float3(40, 0, 41), Layer = 7, Variant = 0, PlanGen = 1 },
                                  new ComposeMember { Position = new float3(float.NaN, 0, 1), Layer = 0, Variant = 0, PlanGen = 1 } },
                    SkipSlots = new HashSet<int> { -3, 0, 1000000 } };
                SkiGladeCompose.Compose(odd, r);
                // G3 fixer round: the non-finite interior member reads "outside" → misplaced →
                // removed first (it was G3-A's uncounted, never-removed OwnOutside).
                Check(r.Remove.Count == 1 && r.Remove[0] == 1 && r.Skipped <= 1 && r.Same == 0 && r.SameIn == 0 && r.Misplaced == 1 && r.Other == 1,
                    "compose/garbage", "odd members/skips: remove " + r.Remove.Count + ", skipped " + r.Skipped + ", same " + r.Same + ", sameIn " + r.SameIn +
                    ", misplaced " + r.Misplaced + ", other " + r.Other);
                Check(r.OccupantsOtherLayer == 1 && r.FixedInside == 3 && r.WildInside == 1, "compose/garbage", "a member with an unknown layer must be an occupant (and X inside), never removed: " +
                    r.OccupantsOtherLayer + " / X " + r.FixedInside);
            }
            catch (Exception e)
            {
                Fail("compose/garbage", "exception " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            }

            Fixture rf = fixtures.Find(x => x.Name == "rect_excl");
            if (rf == null)
            {
                Fail("compose/plankey", "fixture rect_excl missing");
                return;
            }
            float3[] st;
            bool[] th;
            MakeStand(rf, 3, out st, out th);

            // ---- Params.Mode / ThinFraction / GladeDensity / MaxPlant are not read (§8.3).
            foreach (SimTarget target in new[] { SimTarget.Interior, SimTarget.TreeLine })
            {
                for (int level = 1; level <= 8; level += 3)
                {
                    ComposeInput a = new ComposeInput { Ring = rf.Ring, Params = SimParams(target), Level = (byte)level, TreeLine = target == SimTarget.TreeLine,
                        Obstacles = rf.Obstacles, ExclusionRings = rf.Exclusions, Existing = st, Thinnable = th, Own = new ComposeMember[0], Caps = BigCaps() };
                    ComposeInput b2 = Copy(a);
                    b2.Params.Mode = GladeMode.Belt | GladeMode.Thin;
                    b2.Params.ThinFraction = 1f;
                    b2.Params.GladeDensity = 0.5f;
                    b2.Params.MaxPlant = 3;
                    ComposeResult ra = new ComposeResult(), rb2 = new ComposeResult();
                    SkiGladeCompose.Compose(a, ra);
                    SkiGladeCompose.Compose(b2, rb2);
                    Check(ra.PlanKey == rb2.PlanKey && SamePlanned(ra.Plan.Planted, rb2.Plan.Planted) && SameInts(ra.Plant, rb2.Plant) &&
                          SameInts(ra.Fell, rb2.Fell) && ra.Target == rb2.Target, "compose/params-not-read",
                        target + " L" + level + ": Params.Mode / ThinFraction / GladeDensity / MaxPlant changed the result");
                }
            }

            // ---- PlanKey properties.
            ComposeInput b = new ComposeInput { Ring = rf.Ring, Params = SimParams(SimTarget.Interior), Level = 4, Obstacles = rf.Obstacles,
                ExclusionRings = rf.Exclusions, Existing = st, Thinnable = th, Own = new ComposeMember[0], Caps = BigCaps() };
            ComposeResult rb = new ComposeResult();
            SkiGladeCompose.Compose(b, rb);
            ulong k0 = SkiGladeCompose.PlanKey(b);
            Check(k0 != 0UL && k0 == rb.PlanKey, "compose/plankey", "PlanKey(input) != Compose().PlanKey or 0");

            // level, caps and skip set are not in the key; the stream is level-independent
            for (int level = 1; level <= 8; level++)
            {
                ComposeInput lv = Copy(b);
                lv.Level = (byte)level;
                lv.Caps = new ComposeCaps { MaxPlant = 7, MaxFell = 3, MaxRemove = 1 };
                lv.SkipSlots = new HashSet<int> { 0, 5 };
                Check(SkiGladeCompose.PlanKey(lv) == k0, "compose/plankey", "L" + level + " / caps / skips changed the key");
            }

            // permutation of Existing (Thinnable alike) and of the exclusion rings: same key, same P
            int n = st.Length;
            float3[] pst = new float3[n];
            bool[] pth = new bool[n];
            for (int i = 0; i < n; i++)
            {
                pst[i] = st[n - 1 - i];
                pth[i] = th[n - 1 - i];
            }
            ComposeInput perm = Copy(b);
            perm.Existing = pst;
            perm.Thinnable = pth;
            perm.ExclusionRings = new[] { rf.Exclusions[1], rf.Exclusions[0] };
            ComposeResult rp = new ComposeResult();
            SkiGladeCompose.Compose(perm, rp);
            Check(rp.PlanKey == k0 && SamePlanned(rp.Plan.Planted, rb.Plan.Planted), "compose/plankey", "permuted Existing / exclusion order changed the key or P");

            ComposeInput vv = Copy(b);
            vv.Variant = 3;
            Check(SkiGladeCompose.PlanKey(vv) != k0, "compose/plankey", "variant not in the key");
            ComposeInput belt = Copy(b);
            belt.TreeLine = true;
            belt.Params = SimParams(SimTarget.TreeLine);
            Check(SkiGladeCompose.PlanKey(belt) != k0, "compose/plankey", "layer not in the key");

            // A same-layer member is not an occupant: key unchanged.
            ComposeInput same = Copy(b);
            same.Own = new[] { new ComposeMember { Position = new float3(50, GroundY(50, 150), 150), Layer = 0, Variant = 0, PlanGen = 1 } };
            Check(SkiGladeCompose.PlanKey(same) == k0, "compose/plankey", "a same-layer member changed the key");

            // Interior: an occupant that cannot block a slot (outside, >= MinSpacing − InnerMargin
            // = 3.0 m from the edge) leaves key AND P unchanged; nearer, or inside, it changes the key.
            KeyRelevance(b, rb, k0, new float3(-3.2f, GroundY(-3.2, 150), 150), false, "an other-layer member 3.2 m outside", 1);
            KeyRelevance(b, rb, k0, new float3(-2.9f, GroundY(-2.9, 150), 150), true, "an other-layer member 2.9 m outside", 1);
            // G3 fixer round: a tree-line member INSIDE is misplaced — removed first, never an
            // occupant, so the key may not change (G3-A: an occupant and X). P itself differs: the
            // compose that removes it plants nothing (never both), so only the key is compared;
            // keybook soundness (ApplySim) ties an equal key to an equal P.
            KeyIgnoresMisplaced(b, k0, new float3(20, GroundY(20, 20), 20), 1, "a misplaced tree-line member inside");
            KeyRelevance(b, rb, k0, new float3(-3.2f, GroundY(-3.2, 151), 151), false, "a band tree 3.2 m outside", -1);
            KeyRelevance(b, rb, k0, new float3(-2.9f, GroundY(-2.9, 151), 151), true, "a band tree 2.9 m outside", -1);
            // Tree line: a tree deep inside cannot block a belt slot (>= BeltGap outside); one
            // within MinSpacing − BeltGap = 1.4 m of the edge can.
            ComposeResult rbelt = new ComposeResult();
            SkiGladeCompose.Compose(belt, rbelt);
            ulong kb = rbelt.PlanKey;
            KeyRelevance(belt, rbelt, kb, new float3(30, GroundY(30, 30), 30), false, "(belt) a glade member deep inside", 0);
            KeyRelevance(belt, rbelt, kb, new float3(99f, GroundY(99, 30), 30), true, "(belt) a glade member 1 m inside", 0);
            KeyRelevance(belt, rbelt, kb, new float3(30, GroundY(30, 31), 31), false, "(belt) a wild tree deep inside", -1);
            KeyRelevance(belt, rbelt, kb, new float3(99f, GroundY(99, 31), 31), true, "(belt) a wild tree 1 m inside", -1);
            KeyIgnoresMisplaced(belt, kb, new float3(-3f, GroundY(-3, 31), 31), 0, "(belt) a misplaced glade member 3 m outside");

            MisplacedUnitTests();
        }

        /// <summary>A misplaced member (G3 fixer round) is Other: the key is unchanged and the
        /// compose lists it first in Remove.</summary>
        private static void KeyIgnoresMisplaced(ComposeInput baseInput, ulong baseKey, float3 pos, int layer, string what)
        {
            ComposeInput x = Copy(baseInput);
            x.Own = new[] { new ComposeMember { Position = pos, Layer = (byte)layer, Variant = 0, PlanGen = 1 } };
            ComposeResult r = new ComposeResult();
            SkiGladeCompose.Compose(x, r);
            Check(r.PlanKey == baseKey && r.Misplaced == 1 && r.Remove.Count >= 1 && r.Remove[0] == 0 && r.OccupantsOtherLayer == 0,
                "compose/plankey", what + ": key changed " + (r.PlanKey != baseKey) + ", misplaced " + r.Misplaced + ", remove " + r.Remove.Count +
                ", other-layer occupants " + r.OccupantsOtherLayer);
        }

        /// <summary>G3 fixer round (findings compose-1 / behaviour-1): this run's members outside
        /// their own layer's region are Other for the target that sees them — removed first, never
        /// X / M / occupants — and a correctly placed other-layer member stays an occupant.</summary>
        private static void MisplacedUnitTests()
        {
            float3[] sq = Pts(0, 0, 80, 0, 80, 80, 0, 80);
            float3 P(float x, float z) => new float3(x, GroundY(x, z), z);
            float3[] ex = { P(10, 10), P(70, 70), P(-2, 40) };            // wild inside, fixed inside, band
            bool[] th = { true, false, false };
            ComposeMember Mb(float3 p, byte layer, byte variant = 0) => new ComposeMember { Position = p, Layer = layer, Variant = variant, PlanGen = SkiGladeCompose.PlanGeneration };
            ComposeMember[] own =
            {
                Mb(P(40, 40), 1),    // 0: tree-line member INSIDE (misplaced)
                Mb(P(-3, 20), 0),    // 1: interior member 3 m OUTSIDE (misplaced)
                Mb(P(-5, 60), 1),    // 2: tree-line member 5 m outside (correct)
                Mb(P(30, 50), 0),    // 3: interior member inside (correct)
                Mb(P(50, 30), 0, 5), // 4: interior member inside, another variant (other-tagged)
            };
            foreach (bool treeLine in new[] { false, true })
            {
                string where = "compose/misplaced." + (treeLine ? "TreeLine" : "Interior");
                SimTarget target = treeLine ? SimTarget.TreeLine : SimTarget.Interior;
                ComposeInput input = new ComposeInput { Ring = sq, Params = SimParams(target), Level = 1, TreeLine = treeLine, Existing = ex, Thinnable = th,
                    Own = own, Caps = BigCaps() };
                ComposeResult r = new ComposeResult();
                SkiGladeCompose.Compose(input, r);
                if (!treeLine)
                {
                    // Misplaced: 0 (belt inside) and 1 (interior outside); tagged: 4; Same: 3;
                    // occupant: 2. X = the fixed Existing tree only. Others first (x, z), then the
                    // count rule (L1 keeps 0), then — nothing standing — the wild fell.
                    Check(r.Misplaced == 2 && r.OtherTagged == 1 && r.Other == 3 && r.Same == 1 && r.OccupantsOtherLayer == 1 &&
                          r.FixedInside == 1 && r.WildInside == 1 && r.Inside == 3, where,
                        "misplaced " + r.Misplaced + ", tagged " + r.OtherTagged + ", other " + r.Other + ", same " + r.Same + ", other-layer occupants " +
                        r.OccupantsOtherLayer + ", W/X/N " + r.WildInside + "/" + r.FixedInside + "/" + r.Inside);
                    Check(SameInts(r.Remove, new List<int> { 1, 0, 4, 3 }) && r.Fell.Count == 1, where,
                        "Remove " + string.Join(",", r.Remove) + " (expected 1,0,4 first by x then 3), fell " + r.Fell.Count);
                }
                else
                {
                    // Misplaced: 0 (belt inside) and 1 (interior outside); Same: 2; occupants:
                    // 3 and 4 (interior members inside, any tag — the other layer's tag is not
                    // the tree line's business).
                    Check(r.Misplaced == 2 && r.OtherTagged == 0 && r.Other == 2 && r.Same == 1 && r.OccupantsOtherLayer == 2 &&
                          r.FixedInside == 0 && r.Inside == 1, where,
                        "misplaced " + r.Misplaced + ", tagged " + r.OtherTagged + ", other " + r.Other + ", same " + r.Same + ", other-layer occupants " +
                        r.OccupantsOtherLayer + ", X/N " + r.FixedInside + "/" + r.Inside);
                    Check(SameInts(r.Remove, new List<int> { 1, 0, 2 }) && r.Fell.Count == 0, where, "Remove " + string.Join(",", r.Remove) + ", fell " + r.Fell.Count);
                }
                // The key ignores the misplaced members (their removal changes neither P nor the key).
                ComposeInput without = Copy(input);
                without.Own = new[] { own[2], own[3], own[4] };
                Check(SkiGladeCompose.PlanKey(without) == r.PlanKey, where, "the misplaced members changed the PlanKey");
            }
        }

        /// <summary>Adds one tree (layer >= 0: a member of that layer; -1: an Existing wild tree)
        /// to `baseInput` and checks the key changes iff `matters`; when it does not, P must be
        /// bit-identical too.</summary>
        private static void KeyRelevance(ComposeInput baseInput, ComposeResult baseResult, ulong baseKey, float3 pos, bool matters, string what, int layer)
        {
            ComposeInput x = Copy(baseInput);
            if (layer >= 0)
            {
                x.Own = new[] { new ComposeMember { Position = pos, Layer = (byte)layer, Variant = 0, PlanGen = 1 } };
            }
            else
            {
                float3[] e = new float3[baseInput.Existing.Length + 1];
                bool[] t = new bool[e.Length];
                Array.Copy(baseInput.Existing, e, baseInput.Existing.Length);
                Array.Copy(baseInput.Thinnable, t, baseInput.Thinnable.Length);
                e[e.Length - 1] = pos;
                t[t.Length - 1] = true;
                x.Existing = e;
                x.Thinnable = t;
            }
            ComposeResult r = new ComposeResult();
            SkiGladeCompose.Compose(x, r);
            if (matters)
            {
                Check(r.PlanKey != baseKey, "compose/plankey", what + " left the key unchanged (it can affect P)");
            }
            else
            {
                Check(r.PlanKey == baseKey && SamePlanned(r.Plan.Planted, baseResult.Plan.Planted), "compose/plankey",
                    what + " changed the key or P (it cannot affect P)");
            }
        }

        /// <summary>ThinHash is THE documented hash (test-local restatement) and the full word
        /// ThinSelect thresholds: ThinSelect(p, s, f) == ((ThinHash(p, s) &amp; 0xFFFF) &lt; f·65536).</summary>
        private static void ThinHashTests()
        {
            System.Random r = new System.Random(8181);
            int mismatchOracle = 0, mismatchSelect = 0, yDep = 0;
            for (int i = 0; i < 100000; i++)
            {
                float3 p = new float3((float)(r.NextDouble() * 28000 - 14000), (float)(r.NextDouble() * 900), (float)(r.NextDouble() * 28000 - 14000));
                uint seed = (uint)r.Next() | 1u;
                uint h = SkiGladeCompose.ThinHash(p, seed);
                if (h != OracleThinHash(p, seed))
                    mismatchOracle++;
                if (h != SkiGladeCompose.ThinHash(new float3(p.x, p.y + 123f, p.z), seed))
                    yDep++;
                float f = (float)r.NextDouble();
                if (SkiGladePlanner.ThinSelect(p, seed, f) != ((float)(h & 0xFFFFu) < f * 65536f))
                    mismatchSelect++;
            }
            Check(mismatchOracle == 0, "compose/thinhash", mismatchOracle + " hash(es) differ from the documented formula");
            Check(mismatchSelect == 0, "compose/thinhash", mismatchSelect + " ThinSelect verdict(s) are not the low-16-bit threshold of ThinHash");
            Check(yDep == 0, "compose/thinhash", "ThinHash depends on y");
            Check(SkiGladeCompose.ThinHash(new float3(float.NaN, 0, 1), 5) == 0u && SkiGladeCompose.ThinHash(new float3(1, 0, float.PositiveInfinity), 5) == 0u &&
                  SkiGladeCompose.ThinHash(new float3(3e9f, 0, 1), 5) == 0u, "compose/thinhash", "non-finite / out-of-range positions must hash to 0");
        }

        private static bool Empty(ComposeResult r)
        {
            return r.Plant.Count == 0 && r.Fell.Count == 0 && r.Remove.Count == 0 && r.Plan.Planted.Count == 0;
        }

        private static ComposeInput Copy(ComposeInput a)
        {
            return new ComposeInput
            {
                Ring = a.Ring,
                Triangles = a.Triangles,
                Params = a.Params,
                Level = a.Level,
                TreeLine = a.TreeLine,
                Variant = a.Variant,
                Obstacles = a.Obstacles,
                ExclusionRings = a.ExclusionRings,
                Existing = a.Existing,
                Thinnable = a.Thinnable,
                Own = a.Own,
                SkipSlots = a.SkipSlots,
                Caps = a.Caps,
            };
        }

        /// <summary>The stream's planner params from the spec definition (interior: the stream
        /// density 157.3 / ha, §8.2 as amended in the G3 fixer round; the belt as in G2).</summary>
        private static GladeParams StreamTestParams(SimTarget target)
        {
            GladeParams p = SimParams(target);
            p.Mode = target == SimTarget.Interior ? GladeMode.Glade : GladeMode.Belt;
            p.ThinFraction = 0f;
            p.GladeDensity = OracleStreamD;
            return p;
        }

        // ================================================================== prefix property

        /// <summary>Spec §4.3 step 4 (kept by §8): Plan(MaxPlant m) is a bit-exact PREFIX of
        /// Plan(MaxPlant large), for both layers, with occupants, exclusion rings and saturation.
        /// A slice may be SHORTER than min(m, |full|) only when it ran out of its target-scaled
        /// attempt budget (then it reports Saturated) — the case SkiGladeCompose re-plans in full.</summary>
        private static void PrefixTests(List<Fixture> fixtures, List<SimRun> simRuns, int seeds)
        {
            int cases = 0, shortSlices = 0;
            int nSeeds = Math.Max(2, seeds / 40);
            List<(string, float3[], int3[], GladeObstacle[], float3[][], float3[])> inputs = new List<(string, float3[], int3[], GladeObstacle[], float3[][], float3[])>();
            foreach (string name in new[] { "rect100x300", "L", "U", "zigzag", "rect_excl", "L_far7000" })
            {
                Fixture f = fixtures.Find(x => x.Name == name);
                if (f == null)
                {
                    Fail("prefix/" + name, "fixture missing");
                    continue;
                }
                float3[] ex;
                bool[] th;
                MakeStand(f, 1, out ex, out th);
                inputs.Add((name, f.Ring, f.Tris, f.Obstacles, f.Exclusions, ex));
            }
            foreach (string name in new[] { "run284k", "saturated", "run140k" })
            {
                SimRun r = simRuns.Find(x => x.Name == name);
                World w = MakeWorld(r, 1);
                List<float3> ex = new List<float3>();
                foreach (WTree t in w.Trees)
                    ex.Add(t.Pos);
                inputs.Add((name, r.Ring, null, r.Obstacles, r.Exclusions, ex.ToArray()));
            }
            GladePlan full = new GladePlan(), slice = new GladePlan();
            foreach (var inp in inputs)
            {
                foreach (SimTarget target in new[] { SimTarget.Interior, SimTarget.TreeLine })
                {
                    for (int s = 0; s < nSeeds; s++)
                    {
                        string where = "prefix/" + inp.Item1 + "." + target + "/" + s;
                        GladeParams p = StreamTestParams(target);
                        uint seed = SkiGladePlanner.Seed32(inp.Item2[0], SkiGladeCompose.VariantSalt(s));
                        p.MaxPlant = int.MaxValue;
                        SkiGladePlanner.Plan(inp.Item2, inp.Item3, seed, in p, inp.Item4, inp.Item6, null, inp.Item5, full);
                        int nf = full.Planted.Count;
                        int raw = target == SimTarget.Interior ? full.RawTargetGlade : full.RawTargetBelt;
                        foreach (int m in new[] { 0, 1, 7, 50, nf / 3, nf / 2, nf - 1, nf, nf + 5 })
                        {
                            if (m < 0)
                                continue;
                            p.MaxPlant = m;
                            SkiGladePlanner.Plan(inp.Item2, inp.Item3, seed, in p, inp.Item4, inp.Item6, null, inp.Item5, slice);
                            s_Plans += 1;
                            cases++;
                            bool prefix = slice.Planted.Count <= nf;
                            for (int i = 0; prefix && i < slice.Planted.Count; i++)
                                prefix = SameTree(slice.Planted[i], full.Planted[i]);
                            Check(prefix, where, "MaxPlant " + m + ": " + slice.Planted.Count + " trees are not a bit-exact prefix of the full plan (" + nf + ")");
                            int sliceRaw = target == SimTarget.Interior ? slice.RawTargetGlade : slice.RawTargetBelt;
                            Check(sliceRaw == raw, where, "RawTarget changed with MaxPlant (" + sliceRaw + " vs " + raw + ")");
                            Check(slice.Clamped == (raw > m), where, "MaxPlant " + m + " raw " + raw + ": Clamped " + slice.Clamped);
                            if (slice.Planted.Count < Math.Min(m, nf))
                            {
                                shortSlices++;
                                Check(slice.Saturated, where, "MaxPlant " + m + ": short slice (" + slice.Planted.Count + ") without Saturated");
                            }
                        }
                    }
                }
            }
            Console.WriteLine("INFO  prefix: " + cases + " slice plans checked; " + shortSlices +
                              " slice(s) stopped on their attempt budget before min(MaxPlant, |full|) (still prefixes)");
        }

        // ================================================================== timing (report-only)

        /// <summary>§4.11 TIME, G3 form: Compose with the full stream (level 8 from the forested
        /// stand, uncapped) and a level-1 fell compose, on the big fixtures and the two field-sized
        /// ones; WARN above 20 ms (CoreCLR here; Mono in the game is unmeasured).</summary>
        private static void ComposeTimingReport(List<SimRun> runs)
        {
            foreach (string name in new[] { "run284k", "run800k", "saturated", "run124k", "run140k" })
            {
                SimRun run = runs.Find(x => x.Name == name);
                if (run == null)
                {
                    Fail("time/" + name, "fixture missing");
                    continue;
                }
                World w = MakeWorld(run, 0);
                foreach (SimTarget target in new[] { SimTarget.Interior, SimTarget.TreeLine })
                {
                    foreach (int level in new[] { 8, 1 })
                    {
                        if (target == SimTarget.TreeLine && level == 1)
                            continue;
                        CensusOut c = Census(w, run, target);
                        ComposeInput input = BuildInput(run, target, level, c, BigCaps());
                        ComposeResult r = new ComposeResult();
                        SkiGladeCompose.Compose(input, r); // warm-up / JIT
                        const int reps = 5;
                        double max = 0, sum = 0;
                        for (int i = 0; i < reps; i++)
                        {
                            Stopwatch sw = Stopwatch.StartNew();
                            SkiGladeCompose.Compose(input, r);
                            sw.Stop();
                            s_Plans += 2;
                            double ms = sw.Elapsed.TotalMilliseconds;
                            sum += ms;
                            if (ms > max)
                                max = ms;
                        }
                        string tag = max > 20.0 ? "WARN" : "TIME";
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "{0}  compose.{1}.{2}.L{3}: mean {4:F2} ms, max {5:F2} ms over {6} composes (T {7}, N {8}, slots {9}, plant {10}, fell {11}, attempts {12}, saturated {13}){14}",
                            tag, name, target, level, sum / reps, max, reps, r.Target, r.Inside, r.Slice, r.Plant.Count, r.Fell.Count, r.Plan.Attempts, r.Saturated,
                            tag == "WARN" ? " — above the 20 ms main-thread budget (report-only)" : ""));
                    }
                }
            }
        }
    }
}
