// Pure decisions of the built-run rating cache in SkiRunCatalogSystem, linked into tests/ResortBatch so the key, queue and band rules
// are tested headless. Nothing here reads the game; System and Unity.Mathematics only.
// Key hashes node x and z only (all the rating reads); YKey hashes y, so a terraform re-snap never flips a run to "pending".
// Queue: front reasons (bind, edit) skip the duty cycle; warm-up, terrain and backstop are duty-cycled. A queued warm-up entry is
// never promoted by a bind (every rebuild re-asks unrated rows as "bind"); an edit still promotes it.
// Row band: -1 = not rated yet, -2 = cannot be rated (final for this outline and terrain), so the hub never says "Being rated" forever.
// Engine notes: EngineNotes/SkiRunRatingRules.md

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SkiRunRatingRules
    {
        /// <summary>Queue reasons (0 = nothing to do).</summary>
        public const byte ReasonNone = 0, ReasonWarmup = 1, ReasonBind = 2, ReasonEdit = 3, ReasonTerrain = 4, ReasonBackstop = 5;

        /// <summary>RunRow.RateState values.</summary>
        public const byte RatePending = 0, RateRated = 1, RateNoTerrain = 2, RateTooLarge = 3, RateFailed = 4;

        /// <summary>RunRow.Band values that are not a SlopeBand (bindings.ts BAND).</summary>
        public const sbyte BandPending = -1, BandUnrateable = -2;

        public const ulong FnvOffset = 14695981039346656037UL;

        /// <summary>FNV-1a over the four bytes of <paramref name="value"/>, low byte first.</summary>
        public static ulong Mix(ulong hash, uint value)
        {
            for (int b = 0; b < 4; b++)
            {
                hash ^= (value >> (8 * b)) & 0xFFu;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public static void Begin(out ulong key, out ulong yKey)
        {
            key = FnvOffset;
            yKey = FnvOffset;
        }

        /// <summary>Adds one node: x and z bits to Key, y bits to YKey.</summary>
        public static void AddNode(ref ulong key, ref ulong yKey, float3 p)
        {
            key = Mix(Mix(key, math.asuint(p.x)), math.asuint(p.z));
            yKey = Mix(yKey, math.asuint(p.y));
        }

        public static void Finish(ref ulong key, ref ulong yKey, int count)
        {
            key = Mix(key, (uint)count);
            yKey = Mix(yKey, (uint)count);
        }

        /// <summary>Both keys of the first <paramref name="n"/> nodes (n &lt; 1 gives the keys of an empty ring).</summary>
        public static void Keys(float3[] nodes, int n, out ulong key, out ulong yKey)
        {
            Begin(out key, out yKey);
            if (nodes == null || n < 1)
            {
                Finish(ref key, ref yKey, 0);
                return;
            }
            int m = n < nodes.Length ? n : nodes.Length;
            for (int k = 0; k < m; k++)
                AddNode(ref key, ref yKey, nodes[k]);
            Finish(ref key, ref yKey, m);
        }

        /// <summary>Why a row needs a (re-)rating, or ReasonNone.</summary>
        public static byte NeedsRating(bool cached, ulong cachedKey, ulong cachedYKey, int cachedEpoch,
            ulong rowKey, ulong rowYKey, int epoch, byte missingReason)
        {
            if (!cached)
                return missingReason;
            if (cachedKey != rowKey)
                return ReasonEdit;
            if (cachedYKey != rowYKey || cachedEpoch < epoch)
                return ReasonTerrain;
            return ReasonNone;
        }

        /// <summary>Front reasons run at once, one per frame; the rest obey the duty cycle.</summary>
        public static bool IsFront(byte reason)
        {
            return reason == ReasonBind || reason == ReasonEdit;
        }

        /// <summary>Whether <paramref name="reason"/> replaces an entry already queued as <paramref name="queued"/> (see Queue above).</summary>
        public static bool Replaces(byte queued, byte reason)
        {
            if (!IsFront(reason) || IsFront(queued))
                return false;
            return !(queued == ReasonWarmup && reason == ReasonBind);
        }

        /// <summary>The band a row shows: the SlopeBand when rated, -1 while pending, -2 when the rating ended in no terrain / too
        /// large / failed.</summary>
        public static sbyte RowBand(byte state, sbyte band)
        {
            if (state == RateRated)
                return band;
            return state == RatePending ? BandPending : BandUnrateable;
        }

        /// <summary>The first SlopeBand a cat track may not have: Blue (2). The classifier cuts Blue at GradeBlue = 25 %
        /// (SkiSlopeClassifier.cs BandOf), so a cat track is suitable below 25 % and Unsuitable at 25 % or steeper. The band is the
        /// rule; there is no separate grade constant.</summary>
        public const sbyte CatTrackFirstUnsuitableBand = 2;

        /// <summary>Is a cat track with this row band (RowBand: -1 pending, -2 unrateable, 0..4 rated) unsuitable? Only a rated band
        /// of Blue or steeper is; a pending or unrateable track cannot be judged yet. The hub shows it as the "Unsuitable" run state
        /// and the drawing tooltip warns before the click.</summary>
        public static bool CatTrackUnsuitable(sbyte rowBand)
        {
            return rowBand >= CatTrackFirstUnsuitableBand;
        }

        public static bool IsUnrateable(byte state)
        {
            return state == RateNoTerrain || state == RateTooLarge || state == RateFailed;
        }

        /// <summary>RatingServiceState values.</summary>
        public const byte ServiceOff = 0, ServiceWarmingUp = 1, ServiceReady = 2, ServiceFailed = 3;

        /// <summary>Background duty factor with the hub open and closed.</summary>
        public const float DutyFactorOpen = 8f, DutyFactorClosed = 32f;

        /// <summary>Panel closed: background ratings start at least this many seconds apart.</summary>
        public const float ClosedMinGapS = 0.25f;

        /// <summary>The service turns Ready this many wall-clock seconds after a load even if its warm-up has not finished.</summary>
        public const float WarmupDeadlineS = 90f;

        /// <summary>When the next background rating may start: startS + factor x rateMs / 1000, and with the panel closed at least
        /// ClosedMinGapS after startS.</summary>
        public static float NextBackgroundStart(float startS, float rateMs, bool panelOpen)
        {
            float wait = (panelOpen ? DutyFactorOpen : DutyFactorClosed) * rateMs * 0.001f;
            if (!panelOpen && wait < ClosedMinGapS)
                wait = ClosedMinGapS;
            return startS + wait;
        }

        /// <summary>Off and Failed never change here (load / exception set them); WarmingUp becomes Ready when the warm-up is done or
        /// nowS - armedAtS &gt;= WarmupDeadlineS; Ready stays Ready.</summary>
        public static byte NextServiceState(byte state, bool warmupDone, float nowS, float armedAtS)
        {
            if (state != ServiceWarmingUp)
                return state;
            return warmupDone || nowS - armedAtS >= WarmupDeadlineS ? ServiceReady : ServiceWarmingUp;
        }
    }
}
