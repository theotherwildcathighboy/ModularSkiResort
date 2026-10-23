// Pure decisions of the built-run rating cache in SkiRunCatalogSystem, linked into tests/ResortBatch so the key, queue and
// band rules are tested headless. Nothing here reads the game.
//
// Cache key:
//   * Key = FNV-1a over every node's x and z float bits, then the node count: the only node values the rating reads.
//     SkiSlopeClassifier.PlanCore reads a node's y only for its finiteness check (SkiSlopeClassifier.cs:803-805) and
//     rasterizes x and z (:835-845); SkiRunRater passes the nodes only to Plan (SkiRunRater.cs:266) and ShoelaceArea. The
//     rated heights come from the terrain lattice.
//   * YKey = FNV-1a over every node's y float bits, then the count. It changes when GroundHeightSystem re-snaps a
//     terrain-following area node (UpdateHeightsJob moves a node whose m_Elevation is float.MinValue by >= 1 cm,
//     GroundHeightSystem.cs:777-791; AreaUtils.AdjustPosition writes only m_Position.y, AreaUtils.cs:672-677), so a terraform
//     under a run moves YKey and never Key. Keying on xyz would flip a run to "pending" on every re-snap of a brush stroke.
//   * NeedsRating: no cache entry gives the caller's missing reason; Key differs gives Edit (front: the old rating is no
//     longer the run's); YKey differs or the entry's terrain epoch is older gives Terrain (background, duty-cycled: the old
//     rating stays visible until the re-rate lands).
//
// Queue: front reasons are bind and edit (no duty cycle); warm-up, terrain and backstop are background (duty-cycled). A
// queued run keeps its place unless the new reason is front and the queued one is not, except that a queued warm-up entry is
// never promoted by a bind: every rebuild after the warm-up seed re-asks every unrated row as "bind", which would move the
// whole remaining warm-up to the front. An edit still promotes it.
//
// Row band: -1 = not rated yet (RateState 0), -2 = cannot be rated (RateState 2 no terrain / 3 too large / 4 failed). Those
// states are final for the current outline and terrain, so the hub must not say "Being rated" forever. A band value, not a
// new RunRow field.
//
// Always-on service: the catalog rates built runs whether the hub is open or not. The pure parts are here: the
// RatingServiceState values (Service*), the background duty factor per panel state, the panel-closed minimum gap between
// background ratings (ClosedMinGapS) and the warm-up deadline after which the service reports Ready even if warm-up stalled
// (WarmupDeadlineS).
//
// Pure file: System and Unity.Mathematics only.

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
