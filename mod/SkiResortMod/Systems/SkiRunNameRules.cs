// The trail-name pool (R3, DECISIONS 578): Count names, one locale key each (SkiResort.RUN_NAME:<index>, LocaleEN.cs). The system picks one name
// per run and stores it (SkiRunName). The list is APPEND ONLY. An index is a save contract (a removed or moved name renames runs in every
// save). Translators translate per index. tests/ResortBatch pins the count, the order and the tags. A name fits the run's character:
// each name carries traits (TraitCodes). Pick takes a random unused name among those that match the run best. A black run gets a "Chute"
// or a "Wall". A cat track gets a "Road" or a "Traverse". Else Pick takes any unused name, so no two runs share a name while one is free.
// When all are taken it returns None and the catalog shows "Run N". A name tagged Prefix takes the "Upper" or "Lower" form the run asks for (FormOf).
// Pure file: no usings (it compiles unchanged into the net48 mod and the net8 test host), no ECS, no allocation.

namespace SkiResortMod.Systems
{
    public static class SkiRunNameRules
    {
        /// <summary>The names in the pool (LocaleEN SkiResort.RUN_NAME:0 .. Count - 1). Raising it means appending names, never reordering.</summary>
        public const int Count = 200;

        /// <summary>No name: every name was taken when the run was made (the catalog shows "Run N").</summary>
        public const int None = -1;

        public const string KeyPrefix = "SkiResort.RUN_NAME:";

        // Traits, of a name (TraitCodes) and of a run (the system's reading of its band, trees and kind)
        public const byte Steep = 1;     // black and double black: "Chute", "Wall", "Plunge"
        public const byte Gentle = 2;    // green: "Meadow", "Ramble", "Lane"
        public const byte Glade = 4;     // trees inside the run: "Glades", "Timber"
        public const byte Track = 8;     // cat tracks: "Road", "Traverse", "Way"
        public const byte Prefix = 16;   // a name only: reads well as "Upper ..." / "Lower ..."
        private const byte Character = Steep | Gentle | Glade | Track;

        // The run's place in its area's elevation range, and the form its name takes (SkiRunName.m_Form, locale RUN_NAME_FORM[*])
        public const byte FormNone = 0, FormUpper = 1, FormLower = 2;

        /// <summary>An area's elevation range below this (m) gives no form: too little height for "Upper" and "Lower" to mean anything.</summary>
        public const float FormMinRangeM = 150f;

        /// <summary>The share of the range at the top ("Upper") and at the bottom ("Lower") a run's mid height must fall in.</summary>
        public const float FormShare = 0.3f;

        // One character per name, '@' + its trait bits (Steep 1, Gentle 2, Glade 4, Track 8, Prefix 16), in index order, 20 per line.
        // APPEND ONLY with the names (tests/ResortBatch pins it).
        private const string TraitCodes =
            "PRRTPXPPPTPPRPPPPRQR" + // 0
            "PQPRRRPPPZRUPTTXVTVP" + // 20
            "PPPRRRQQQPQQPQQQRRRQ" + // 40
            "QRQQPQQQRVPPTRP@PPPQ" + // 60
            "PPPPPPPPPPPPPP@PP@PP" + // 80
            "X@QPPTXXP@VXPPPRPPPP" + // 100
            "PRRRRPPHZRXPX@ZRVBBR" + // 120
            "PQPQPPPBBPZZXXAPAAPA" + // 140
            "@@Z@PT@PQXPBQPPXXXXR" + // 160
            "BBBABRARPRZZRQPPPXRR";  // 180

        /// <summary>The locale key of a name, or null for an index outside the pool.</summary>
        public static string Key(int index) =>
            index >= 0 && index < Count ? KeyPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

        /// <summary>The trait bits of a name (0 outside the pool).</summary>
        public static byte TraitsOf(int index) =>
            index >= 0 && index < Count && index < TraitCodes.Length ? (byte)((TraitCodes[index] - '@') & 31) : (byte)0;

        /// <summary>A run's traits: Steep for black and double black (band 3, 4), Gentle for flat and green (0, 1), none for blue or no
        /// rating. Glade when trees stand inside it. A cat track is Track and Gentle (a track name may also read gentle).</summary>
        public static byte RunTraits(bool catTrack, int band, bool glade)
        {
            byte t = glade ? Glade : (byte)0;
            if (catTrack)
                return (byte)(t | Track | Gentle);
            if (band == 3 || band == 4)
                return (byte)(t | Steep);
            if (band == 0 || band == 1)
                return (byte)(t | Gentle);
            return t;
        }

        /// <summary>The form of a run whose heights run bottomM .. topM in an area spanning areaBottomM .. areaTopM. Upper when its mid
        /// height is in the top FormShare of the range. Lower when it is in the bottom FormShare. None in between, for a range under
        /// FormMinRangeM, or for a NaN.</summary>
        public static byte FormOf(float bottomM, float topM, float areaBottomM, float areaTopM)
        {
            float range = areaTopM - areaBottomM;
            float mid = (bottomM + topM) * 0.5f;
            if (!(range >= FormMinRangeM) || float.IsNaN(mid))
                return FormNone;
            float at = (mid - areaBottomM) / range;
            return at >= 1f - FormShare ? FormUpper : at <= FormShare ? FormLower : FormNone;
        }

        /// <summary>A name for a run with traits <paramref name="run"/> (RunTraits) asking for <paramref name="wantForm"/> (FormOf).
        /// Returns None when every index of <paramref name="used"/> (length Count, not changed here) is taken. The candidates are the
        /// unused names that contradict none of the run's traits (a Steep, Gentle, Glade or Track tag the run lacks. A Track run needs a
        /// Track name). Of these it takes those matching most (grade and track count 2, trees 1). With a form, it takes those of them tagged
        /// Prefix if any (else it drops the form). With no such name, it takes any unused name. The <c>seed % n</c>-th of the set: the same
        /// seed and set give the same pick. <paramref name="form"/> is the form the pick takes.</summary>
        public static int Pick(bool[] used, uint seed, byte run, byte wantForm, out byte form)
        {
            form = FormNone;
            int best = -1, bestCount = 0, prefixCount = 0, free = 0;
            for (int i = 0; i < Count; i++)
            {
                if (used[i])
                    continue;
                free++;
                byte name = TraitsOf(i);
                if (Contradicts(name, run))
                    continue;
                int s = Score(name, run);
                if (s > best)
                {
                    best = s;
                    bestCount = prefixCount = 0;
                }
                if (s == best)
                {
                    bestCount++;
                    if ((name & Prefix) != 0)
                        prefixCount++;
                }
            }
            if (free == 0)
                return None;
            if (bestCount == 0)
                return Nth(used, seed % (uint)free, run, -1, false);
            bool prefixed = wantForm != FormNone && prefixCount > 0;
            if (prefixed)
                form = wantForm;
            return Nth(used, seed % (uint)(prefixed ? prefixCount : bestCount), run, best, prefixed);
        }

        /// <summary>A trait-free pick (a blue run on its own): <see cref="Pick(bool[], uint, byte, byte, out byte)"/> with no traits and no form.</summary>
        public static int Pick(bool[] used, uint seed) => Pick(used, seed, 0, FormNone, out _);

        /// <summary>A well-mixed seed from a run's first outline point (meters): the same run gives the same seed. The method applies FNV-1a, then a finalizer.</summary>
        public static uint Seed(float x, float z)
        {
            uint h = 2166136261u;
            h = (h ^ Bits(x)) * 16777619u;
            h = (h ^ Bits(z)) * 16777619u;
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            h *= 0x846ca68bu;
            h ^= h >> 16;
            return h;
        }

        /// <summary>A name tag the run lacks, or a non-track name on a cat track.</summary>
        private static bool Contradicts(byte name, byte run) =>
            (name & Character & ~run) != 0 || ((run & Track) != 0 && (name & Track) == 0);

        /// <summary>How well a non-contradicting name fits: grade and track 2 each, trees 1. A cat track's Gentle does not count, so its
        /// "Road" beats its "Meadow Way" only by chance.</summary>
        private static int Score(byte name, byte run)
        {
            int shared = name & run;
            if ((run & Track) != 0)
                shared &= ~Gentle;
            return ((shared & Steep) != 0 ? 2 : 0) + ((shared & Gentle) != 0 ? 2 : 0) + ((shared & Track) != 0 ? 2 : 0) +
                   ((shared & Glade) != 0 ? 1 : 0);
        }

        /// <summary>The k-th unused index of the set Pick chose: score <paramref name="best"/> (-1: any unused name), Prefix-tagged when
        /// <paramref name="prefixed"/>.</summary>
        private static int Nth(bool[] used, uint k, byte run, int best, bool prefixed)
        {
            for (int i = 0; i < Count; i++)
            {
                if (used[i])
                    continue;
                byte name = TraitsOf(i);
                if (best >= 0 && (Contradicts(name, run) || Score(name, run) != best || (prefixed && (name & Prefix) == 0)))
                    continue;
                if (k == 0)
                    return i;
                k--;
            }
            return None;   // unreachable
        }

        // 1/16 m steps (net48 has no SingleToInt32Bits. Map coordinates stay far inside int range). NaN reads 0.
        private static uint Bits(float v) => !float.IsNaN(v) ? unchecked((uint)(int)(v * 16f)) : 0u;
    }
}
