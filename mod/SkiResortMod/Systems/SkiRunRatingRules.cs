// SkiRunRatingRules — the PURE decisions of the built-run rating cache in SkiRunCatalogSystem
// (docs/research/hub-u2-spec.md §3.5 as amended in the fixer round, see below). Created 2026-10-03
// (U2/L1/M1 fixer round, findings u2-data-01, behaviour-2 and u2-data-05/u2-panel-5); the catalog calls
// these and tests/ResortBatch links this file, so the cache's key, queue and band rules are tested
// headless. Nothing here reads the game.
//
// THE CACHE KEY (finding u2-data-01; deviation from spec §3.5 :550, :557, reported):
//   * Key  = FNV-1a over every node's x and z float bits, then the node count. These are the ONLY node
//     values the rating reads: SkiSlopeClassifier.PlanCore reads a node's y only for its finiteness
//     check (SkiSlopeClassifier.cs:803-805) and rasterises x and z (:835-845); SkiRunRater passes the
//     nodes only to Plan (SkiRunRater.cs:266) and to ShoelaceArea (plan area). The rated heights come
//     from the terrain lattice, never from the nodes.
//   * YKey = FNV-1a over every node's y float bits, then the count. It changes when GroundHeightSystem
//     re-snaps a terrain-following area node: UpdateHeightsJob moves a node whose m_Elevation is
//     float.MinValue by >= 1 cm and tags the area Updated (Game.Simulation/GroundHeightSystem.cs:777-791,
//     identical in 1.6.2), and AreaUtils.AdjustPosition writes ONLY m_Position.y
//     (Game.Areas/AreaUtils.cs:672-677) — so a terraform under a run moves YKey and never Key.
//   * NeedsRating: no cache entry → the caller's missing reason (warm-up while seeding, else bind);
//     Key differs → EDIT (front: the outline changed, the old rating is no longer the run's);
//     YKey differs or the entry's terrain epoch is older → TERRAIN (back, duty-cycled: the terrain
//     under the run changed, the old rating stays visible until the re-rate lands). Spec §3.5 keyed on
//     node xyz, so every re-snap during a terrain brush stroke flipped the run to "pending", wrote a
//     "Catalog [rebuild] (area edited)" line and started a full front rating — up to once per frame.
//
// THE QUEUE (finding behaviour-2): FRONT reasons are bind and edit (no duty cycle); warm-up, terrain
// and backstop are BACKGROUND (duty-cycled). A run already queued keeps its place unless the new reason
// is a front reason and the queued one is not — EXCEPT that a queued WARM-UP entry is never promoted by
// a BIND: every rebuild after the warm-up seed re-asks for every row with no rating as "bind", and the
// promotion used to move the whole remaining warm-up to the front (no duty cycle, logged as bind, and
// the "[rate-warmup]" summary printed before the ratings ran). An EDIT (the outline changed while the
// run waited) still promotes it.
//
// THE ROW BAND (findings u2-data-05 / u2-panel-5; deviation from spec §2.3 band "-1 not rated", reported):
// -1 = not rated YET (RateState 0, pending), -2 = CANNOT be rated (RateState 2 no terrain / 3 too large /
// 4 failed): those states are final for the current outline and terrain, so the hub must not call them
// "Being rated" forever. The RunRow shape is unchanged (a band VALUE, no new field: no contract bump).
//
// THE ALWAYS-ON SERVICE (Round E1, D12; docs/research/econ-round/spec/ch2-runs.md §2.5; stream S2,
// 2026-10-04): the catalog rates built runs whether the hub is open or not. The pure parts live here:
// the RatingServiceState values (Service*), the background duty factor per panel state
// (DutyFactorOpen 8 = the U2 value, DutyFactorClosed 32), the panel-closed minimum gap between two
// background ratings (ClosedMinGapS), and the warm-up deadline after which the service reports Ready
// even if its warm-up stalled (WarmupDeadlineS). tests/ResortBatch/RatingServiceTests.cs asserts them.
//
// PURE FILE: `using System;` + `using Unity.Mathematics;` only (the test-host rule of tests/SlopeBatch).

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SkiRunRatingRules
    {
        /// <summary>Queue reasons (0 = nothing to do).</summary>
        public const byte ReasonNone = 0, ReasonWarmup = 1, ReasonBind = 2, ReasonEdit = 3, ReasonTerrain = 4, ReasonBackstop = 5;

        /// <summary>RunRow.RateState values (hub-u2-spec.md §2.1).</summary>
        public const byte RatePending = 0, RateRated = 1, RateNoTerrain = 2, RateTooLarge = 3, RateFailed = 4;

        /// <summary>RunRow.Band values that are not a SlopeBand (bindings.ts BAND).</summary>
        public const sbyte BandPending = -1, BandUnrateable = -2;

        /// <summary>FNV-1a 64 offset basis.</summary>
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

        /// <summary>Starts both keys (FnvOffset).</summary>
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

        /// <summary>Closes both keys with the node count.</summary>
        public static void Finish(ref ulong key, ref ulong yKey, int count)
        {
            key = Mix(key, (uint)count);
            yKey = Mix(yKey, (uint)count);
        }

        /// <summary>Both keys of the first <paramref name="n"/> nodes (n &lt; 1 → the keys of an empty ring).</summary>
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

        /// <summary>Why a row needs a (re-)rating, or ReasonNone (header THE CACHE KEY).</summary>
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

        /// <summary>Whether <paramref name="reason"/> replaces an entry already queued as
        /// <paramref name="queued"/> (header THE QUEUE).</summary>
        public static bool Replaces(byte queued, byte reason)
        {
            if (!IsFront(reason) || IsFront(queued))
                return false;
            return !(queued == ReasonWarmup && reason == ReasonBind);
        }

        /// <summary>The band a row shows: the SlopeBand when rated, -1 while pending, -2 when the
        /// rating ended in no terrain / too large / failed (header THE ROW BAND).</summary>
        public static sbyte RowBand(byte state, sbyte band)
        {
            if (state == RateRated)
                return band;
            return state == RatePending ? BandPending : BandUnrateable;
        }

        /// <summary>Is the state a final "cannot be rated" (2 / 3 / 4)?</summary>
        public static bool IsUnrateable(byte state)
        {
            return state == RateNoTerrain || state == RateTooLarge || state == RateFailed;
        }

        // ── Round E1 (ch2 §2): the always-on rating service ──
        /// <summary>RatingServiceState values.</summary>
        public const byte ServiceOff = 0, ServiceWarmingUp = 1, ServiceReady = 2, ServiceFailed = 3;

        /// <summary>Background duty factor with the hub open (the U2 value) and closed.</summary>
        public const float DutyFactorOpen = 8f, DutyFactorClosed = 32f;

        /// <summary>Panel closed: background ratings start at least this many seconds apart.</summary>
        public const float ClosedMinGapS = 0.25f;

        /// <summary>The service turns Ready this many wall-clock seconds after a load even if its
        /// warm-up has not finished.</summary>
        public const float WarmupDeadlineS = 90f;

        /// <summary>When the next BACKGROUND rating may start: startS + factor × rateMs / 1000, and with
        /// the panel closed at least ClosedMinGapS after startS.</summary>
        public static float NextBackgroundStart(float startS, float rateMs, bool panelOpen)
        {
            float wait = (panelOpen ? DutyFactorOpen : DutyFactorClosed) * rateMs * 0.001f;
            if (!panelOpen && wait < ClosedMinGapS)
                wait = ClosedMinGapS;
            return startS + wait;
        }

        /// <summary>Off and Failed never change here (load / exception set them); WarmingUp → Ready when
        /// the warm-up is done or nowS − armedAtS ≥ WarmupDeadlineS; Ready stays Ready.</summary>
        public static byte NextServiceState(byte state, bool warmupDone, float nowS, float armedAtS)
        {
            if (state != ServiceWarmingUp)
                return state;
            return warmupDone || nowS - armedAtS >= WarmupDeadlineS ? ServiceReady : ServiceWarmingUp;
        }
        // (Second pass, e1-spec R14: no GateSettled rule — IsGateSettled is not built, ch2 §2.4.)
    }
}
