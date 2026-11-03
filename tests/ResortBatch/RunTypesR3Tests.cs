// Run types R3 (DECISIONS 578): the trail-name pool and the per-feature upkeep rule. Names: a pick is unused while any name is free, None
// once all are taken, a freed name comes back, the same seed picks the same name; the locale holds exactly Count names, unique, Title Case,
// in the pinned order with pinned tags (APPEND ONLY: a moved or removed name renames runs in every save). Traits: on a fixed fixture set a
// black run gets a steep name, a green a gentle one, a glade run a glade one, a cat track a track one, a blue a neutral one, and a run high
// or low in its area the Upper / Lower form; with its bucket used up a run takes a name that does not contradict it, then any. Upkeep: grooming and lights bill the covered
// hectares, snowmaking only where the guns make snow, so a gun run at depth still pays its grooming; with guns running it equals the R2
// rate per hectare.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class RunTypesR3Tests
    {
        // FNV-1a 32 of the first PinnedCount names joined by "\n" (UTF-8), pinned when the list shipped. Appending names keeps it.
        private const int PinnedCount = 200;
        private const uint PinnedHash = 0x8c30335fu;

        // FNV-1a 32 of SkiRunNameRules.TraitsOf(0 .. PinnedCount - 1), pinned with the names.
        private const uint PinnedTraitsHash = 0x018649a8u;

        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Picks();
            Traits();
            NameList();
            Upkeep();
            s_Check = null;
        }

        /// <summary>Pick: unique while free, None when full, a freed index returns, deterministic.</summary>
        private static void Picks()
        {
            const string W = "names/pick";
            var used = new bool[SkiRunNameRules.Count];
            var rng = new Random(578);
            bool unique = true;
            for (int i = 0; i < SkiRunNameRules.Count; i++)
            {
                int p = SkiRunNameRules.Pick(used, (uint)rng.Next());
                if (p < 0 || p >= SkiRunNameRules.Count || used[p])
                {
                    unique = false;
                    break;
                }
                used[p] = true;
            }
            Check(unique, W, SkiRunNameRules.Count + " picks give " + SkiRunNameRules.Count + " different names");
            Check(SkiRunNameRules.Pick(used, 12345u) == SkiRunNameRules.None, W, "a full pool gives None (\"Run N\")");
            used[137] = false;
            Check(SkiRunNameRules.Pick(used, 99u) == 137, W, "a bulldozed run's name is the only free one and comes back");
            Array.Clear(used, 0, used.Length);
            uint seed = SkiRunNameRules.Seed(1234.5f, -876.25f);
            Check(SkiRunNameRules.Pick(used, seed) == SkiRunNameRules.Pick(used, seed) &&
                  seed == SkiRunNameRules.Seed(1234.5f, -876.25f), W, "the same run picks the same name");
            Check(SkiRunNameRules.Seed(float.NaN, 0f) == SkiRunNameRules.Seed(0f, 0f), W, "a NaN point seeds like 0");
            // Spread: 2000 seeds from nearby points cover most of the neutral names (a blue run without trees picks among those)
            var hit = new HashSet<int>();
            for (int i = 0; i < 2000; i++)
                hit.Add(SkiRunNameRules.Pick(used, SkiRunNameRules.Seed(100f + i * 0.5f, 200f)));
            int neutral = Bucket(t => (t & Character) == 0);
            Check(hit.Count >= neutral * 9 / 10, W, "nearby runs spread over the neutral names: " + hit.Count + " of " + neutral);
            Check(SkiRunNameRules.Key(0) == "SkiResort.RUN_NAME:0" && SkiRunNameRules.Key(SkiRunNameRules.Count) == null &&
                  SkiRunNameRules.Key(-1) == null, W, "keys: SkiResort.RUN_NAME:<index>, null outside the pool");
        }

        private const byte Character = SkiRunNameRules.Steep | SkiRunNameRules.Gentle | SkiRunNameRules.Glade | SkiRunNameRules.Track;

        private static int Bucket(Func<byte, bool> f)
        {
            int n = 0;
            for (int i = 0; i < SkiRunNameRules.Count; i++)
                if (f(SkiRunNameRules.TraitsOf(i)))
                    n++;
            return n;
        }

        /// <summary>One fixture: the run's reading and what every pick for it must (and must not) carry.</summary>
        private struct Fixture
        {
            public string Name;
            public bool CatTrack, Glade;
            public int Band;
            public byte Form, Must, MustNot, WantForm;
        }

        /// <summary>Trait picks on a fixed fixture set, buckets, RunTraits, FormOf, and the fallbacks once a bucket is used up.</summary>
        private static void Traits()
        {
            const string W = "names/traits";
            const byte S = SkiRunNameRules.Steep, G = SkiRunNameRules.Gentle, L = SkiRunNameRules.Glade, T = SkiRunNameRules.Track,
                       P = SkiRunNameRules.Prefix, Up = SkiRunNameRules.FormUpper, Lo = SkiRunNameRules.FormLower, No = SkiRunNameRules.FormNone;
            // Buckets: big enough for a resort, and never contradictory
            int steep = Bucket(t => (t & S) != 0), gentle = Bucket(t => (t & G) != 0), glade = Bucket(t => (t & L) != 0),
                track = Bucket(t => (t & T) != 0), prefix = Bucket(t => (t & P) != 0);
            Check(steep >= 30 && gentle >= 30 && glade >= 12 && track >= 20 && prefix >= 60, W,
                  $"buckets: steep {steep} (>= 30), gentle {gentle} (>= 30), glade {glade} (>= 12), track {track} (>= 20), prefix {prefix} (>= 60)");
            Check(Bucket(t => (t & S) != 0 && (t & (G | T)) != 0) == 0 && Bucket(t => t > 31) == 0, W,
                  "no name is steep and gentle or steep and track; tags are the five bits");
            // RunTraits
            Check(SkiRunNameRules.RunTraits(false, 3, false) == S && SkiRunNameRules.RunTraits(false, 4, true) == (S | L) &&
                  SkiRunNameRules.RunTraits(false, 1, false) == G && SkiRunNameRules.RunTraits(false, 0, false) == G &&
                  SkiRunNameRules.RunTraits(false, 2, false) == 0 && SkiRunNameRules.RunTraits(false, -2, true) == L &&
                  SkiRunNameRules.RunTraits(true, 1, false) == (T | G), W,
                  "RunTraits: black and double black steep, flat and green gentle, blue and unrated none, trees glade, a cat track track");
            // FormOf: area 1000 .. 1600 m
            Check(SkiRunNameRules.FormOf(1450f, 1600f, 1000f, 1600f) == Up && SkiRunNameRules.FormOf(1000f, 1150f, 1000f, 1600f) == Lo &&
                  SkiRunNameRules.FormOf(1200f, 1400f, 1000f, 1600f) == No && SkiRunNameRules.FormOf(1000f, 1600f, 1000f, 1600f) == No &&
                  SkiRunNameRules.FormOf(1050f, 1100f, 1000f, 1100f) == No && SkiRunNameRules.FormOf(float.NaN, 1600f, 1000f, 1600f) == No, W,
                  "FormOf: Upper in the top 30 %, Lower in the bottom 30 %, none in between, for a whole-mountain run, under 150 m, or NaN");
            var fixtures = new[]
            {
                new Fixture { Name = "black", Band = 3, Must = S, MustNot = G | T | L },
                new Fixture { Name = "double black", Band = 4, Must = S, MustNot = G | T | L },
                new Fixture { Name = "green", Band = 1, Must = G, MustNot = S | T | L },
                new Fixture { Name = "flat", Band = 0, Must = G, MustNot = S | T | L },
                new Fixture { Name = "blue", Band = 2, Must = 0, MustNot = Character },
                new Fixture { Name = "unrated", Band = -2, Must = 0, MustNot = Character },
                new Fixture { Name = "blue glade", Band = 2, Glade = true, Must = L, MustNot = S | G | T },
                new Fixture { Name = "black glade", Band = 3, Glade = true, Must = S | L, MustNot = G | T },
                new Fixture { Name = "green glade", Band = 1, Glade = true, Must = G | L, MustNot = S | T },
                new Fixture { Name = "cat track", CatTrack = true, Band = 1, Must = T, MustNot = S | L },
                new Fixture { Name = "upper black", Band = 3, WantForm = Up, Form = Up, Must = S | P, MustNot = G | T | L },
                new Fixture { Name = "lower green", Band = 1, WantForm = Lo, Form = Lo, Must = G | P, MustNot = S | T | L },
                new Fixture { Name = "upper cat track", CatTrack = true, Band = 1, WantForm = Up, Form = Up, Must = T | P, MustNot = S },
            };
            var empty = new bool[SkiRunNameRules.Count];
            foreach (Fixture f in fixtures)
            {
                byte run = SkiRunNameRules.RunTraits(f.CatTrack, f.Band, f.Glade);
                string bad = null;
                for (int k = 0; k < 64 && bad == null; k++)
                {
                    int p = SkiRunNameRules.Pick(empty, SkiRunNameRules.Seed(k * 37.5f, -k * 11.25f), run, f.WantForm, out byte form);
                    byte t = SkiRunNameRules.TraitsOf(p);
                    if (p < 0 || (t & f.Must) != f.Must || (t & f.MustNot) != 0 || form != f.Form)
                        bad = "index " + p + " traits " + t + " form " + form;
                }
                Check(bad == null, W, "a " + f.Name + " run always gets a fitting name over 64 seeds" + (bad != null ? ": " + bad : ""));
            }
            // A resort's worth of black runs in a row: the steep names while any is free, then neutral ones
            var used = new bool[SkiRunNameRules.Count];
            byte black = SkiRunNameRules.RunTraits(false, 3, false);
            int steepOnly = Bucket(t => (t & S) != 0 && (t & L) == 0), streak = 0;
            for (int k = 0; k < steepOnly; k++)
            {
                int p = SkiRunNameRules.Pick(used, (uint)(k * 7919), black, No, out _);
                if (p >= 0 && (SkiRunNameRules.TraitsOf(p) & S) != 0 && !used[p])
                    streak++;
                if (p >= 0)
                    used[p] = true;
            }
            Check(streak == steepOnly, W, "black runs one after another take the " + steepOnly + " steep names first: " + streak);
            int next = SkiRunNameRules.Pick(used, 5u, black, No, out _);
            Check(next >= 0 && (SkiRunNameRules.TraitsOf(next) & Character) == 0, W,
                  "with the steep names taken a black run gets a neutral name, never a gentle one: index " + next);
            // A cat track with every track name taken still gets a name (any unused, no form)
            Array.Clear(used, 0, used.Length);
            for (int i = 0; i < SkiRunNameRules.Count; i++)
                used[i] = (SkiRunNameRules.TraitsOf(i) & T) != 0;
            int any = SkiRunNameRules.Pick(used, 3u, SkiRunNameRules.RunTraits(true, 1, false), Up, out byte anyForm);
            Check(any >= 0 && !used[any] && anyForm == No, W, "a cat track with the track names taken takes any unused name, without a form");
            // An upper black run whose free steep names take no prefix keeps a steep name and drops the form
            Array.Clear(used, 0, used.Length);
            for (int i = 0; i < SkiRunNameRules.Count; i++)
                used[i] = (SkiRunNameRules.TraitsOf(i) & S) == 0 || (SkiRunNameRules.TraitsOf(i) & P) != 0;
            int bare = SkiRunNameRules.Pick(used, 3u, black, Up, out byte bareForm);
            Check(bare < 0 || ((SkiRunNameRules.TraitsOf(bare) & S) != 0 && bareForm == No), W,
                  "an upper black run whose free steep names take no prefix keeps the steep name and drops the form: index " + bare);
            Check(Bucket(t => (t & S) != 0 && (t & P) == 0) > 0, W, "some steep names take no prefix (the case above runs)");
        }

        /// <summary>LocaleEN: Count names, none beyond, unique, Title Case words, the pinned order.</summary>
        private static void NameList()
        {
            const string W = "names/list";
            string path = CatalogGuardTests.RepoFile("mod/SkiResortMod/LocaleEN.cs");
            if (!Check(path != null, W, "mod/SkiResortMod/LocaleEN.cs not found"))
                return;
            Dictionary<string, string> loc = CatalogGuardTests.SingleLineEntries(File.ReadAllLines(path));
            var names = new List<string>();
            for (int i = 0; ; i++)
            {
                if (!loc.TryGetValue(SkiRunNameRules.KeyPrefix + i.ToString(CultureInfo.InvariantCulture), out string n))
                    break;
                names.Add(n);
            }
            int beyond = 0;
            foreach (string key in loc.Keys)
                if (key.StartsWith(SkiRunNameRules.KeyPrefix, StringComparison.Ordinal) &&
                    (!int.TryParse(key.Substring(SkiRunNameRules.KeyPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int k) ||
                     k >= names.Count))
                    beyond++;
            Check(names.Count == SkiRunNameRules.Count && beyond == 0, W,
                  "LocaleEN holds names 0.." + (SkiRunNameRules.Count - 1) + " and no other: " + names.Count + " in a row, " + beyond + " stray");
            Check(new HashSet<string>(names, StringComparer.OrdinalIgnoreCase).Count == names.Count, W, "the names are unique");
            var title = new Regex("^[A-Z][a-z]+( [A-Z][a-z]+)*$");
            string bad = null;
            foreach (string n in names)
                if (!title.IsMatch(n) || n.Length < 3 || n.Length > 24)
                {
                    bad = n;
                    break;
                }
            Check(bad == null, W, "every name is Title Case words, 3-24 characters" + (bad != null ? ": \"" + bad + "\"" : ""));
            if (names.Count >= PinnedCount)
            {
                uint h = 2166136261u;
                foreach (byte b in Encoding.UTF8.GetBytes(string.Join("\n", names.GetRange(0, PinnedCount))))
                    h = (h ^ b) * 16777619u;
                Check(h == PinnedHash, W, "the first " + PinnedCount + " names are in the pinned order (append only): hash 0x" +
                                          h.ToString("x8", CultureInfo.InvariantCulture));
                uint th = 2166136261u;
                for (int i = 0; i < PinnedCount; i++)
                    th = (th ^ SkiRunNameRules.TraitsOf(i)) * 16777619u;
                Check(th == PinnedTraitsHash, W, "the first " + PinnedCount + " names keep their pinned tags: hash 0x" +
                                                 th.ToString("x8", CultureInfo.InvariantCulture));
            }
        }

        /// <summary>PisteUpkeep: by feature; a gun run at depth pays grooming (and lights); guns running = the R2 per-hectare rate.</summary>
        private static void Upkeep()
        {
            const string W = "upkeep/features";
            RunFeatures gun = RunFeatures.Of(RunBase.Piste, true, true, false);
            RunUpkeep depth = SkiRunUpkeepRates.PisteUpkeep(gun, 0f, 2f);
            Check(Near(depth.Grooming, 2f * SkiRunUpkeepRates.GroomedPerHaMonth) && depth.Snowmaking == 0f && depth.Lights == 0f &&
                  Near(depth.GroomedHa, 2f) && depth.SnowmakingHa == 0f, W,
                  "a gun run at depth (2 ha held off) bills grooming 2 ha x 150, no snowmaking: " + depth.Total);
            RunFeatures night = RunFeatures.Of(RunBase.Piste, true, true, true);
            RunUpkeep lit = SkiRunUpkeepRates.PisteUpkeep(night, 0.5f, 2f);
            Check(Near(lit.Total, 2f * (SkiRunUpkeepRates.GroomedPerHaMonth + SkiRunUpkeepRates.LightsStepPerHaMonth) +
                                  0.5f * SkiRunUpkeepRates.SnowmakingStepPerHaMonth) && Near(lit.LitHa, 2f), W,
                  "a lit gun run: grooming and lights on 2 ha, snowmaking on the 0.5 ha its guns raise: " + lit.Total);
            bool same = true;
            string why = "";
            for (int m = 0; m < 8; m++)
            {
                RunFeatures f = RunFeatures.Of(RunBase.Piste, (m & 1) != 0, (m & 2) != 0 && (m & 1) != 0, (m & 4) != 0 && (m & 1) != 0);
                RunUpkeep u = SkiRunUpkeepRates.PisteUpkeep(f, 3f, 3f);
                if (!Near(u.Total, SkiRunUpkeepRates.PerHaMonth(f) * 3f))
                {
                    same = false;
                    why = f + ": " + u.Total + " vs " + SkiRunUpkeepRates.PerHaMonth(f) * 3f;
                }
            }
            Check(same, W, "guns running on every hectare: PisteUpkeep = PerHaMonth x ha for every piste " + why);
            Check(SkiRunUpkeepRates.PisteUpkeep(RunFeatures.Of(RunBase.CatTrack, true, true, true), 3f, 3f).Total == 0f &&
                  SkiRunUpkeepRates.PisteUpkeep(RunFeatures.Of(RunBase.Piste, false, false, false), 3f, 3f).Total == 0f &&
                  SkiRunUpkeepRates.PisteUpkeep(RunFeatures.Unknown, 3f, 3f).Total == 0f, W,
                  "cat tracks (plan-area rule), natural and unknown runs: 0");
            RunUpkeep odd = SkiRunUpkeepRates.PisteUpkeep(gun, 2f, 1f);
            Check(Near(odd.GroomedHa, 2f) && Near(odd.SnowmakingHa, 2f), W, "covered below the snow hectares reads as the snow hectares");
            Check(SkiRunUpkeepRates.PisteUpkeep(gun, float.NaN, float.PositiveInfinity).Total == 0f &&
                  SkiRunUpkeepRates.PisteUpkeep(gun, -1f, -1f).Total == 0f, W, "NaN, infinite and negative hectares bill 0");
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) <= 1e-3f * Math.Max(1f, Math.Abs(b));

        private static bool Check(bool ok, string what, string detail) => s_Check(ok, what, detail);
    }
}
