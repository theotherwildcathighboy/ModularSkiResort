// StationAlign — the PURE geometry of the station alignment aid (docs/research/hub-u2-spec.md §5.5,
// OD-9 (a): read-only guide + readout + an "aligned" state at 0.5 m / 0.5°). Created 2026-10-03
// (Round L1, implementer I-2). Consumers: SkiStationAlignSystem (subject / partner / overlay /
// placement log), SkiLiftTooltipSystem (readout), LiftCensus (per-row AlignKnown / AlignOffsetM /
// AlignYawDeg), tests/LiftBatch (sign and threshold tests). Catalog Phase A (2026-10-11): StubTipLocalZ
// aliases the PURE SkiCatalog.cs, so SkiCatalog.cs is linked wherever this file is (tests/LiftBatch);
// SkiResortStatsSystem's stub-tip height calls StubTip (its own copy of the formula is gone).
// Catalog Phase B (2026-10-11, docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §6.4, lift group): the
// stub tip is PER STATION ROW — StubTip(position, rotation, StationDef.StubTipLocalZ) (−16 on the detachable / gondola
// stations, −12 on the fixed-grip and tram stations); the 2-argument overload stays = the fixed-grip −12. Adds the pure
// up-lane rule of the loop cache (orchestrator addendum 1: chain A = the cable on the RIGHT looking uphill):
// SideLookingUphill / UpLaneIsSecond, tested in tests/LiftBatch LiftTypeTests LT-11.
//
// GEOMETRY (verified in the prefab JSON, gamedata-mirror/ImportedData):
//   * Both station prefabs carry their high-voltage connection stub as an ObjectSubNet of the
//     SkiLiftMarker net (CID 996ee501…) from station-local (0,0,−9) to (0,0,−12), node indices
//     {0,1}: SkiTopStationBuilding.Prefab:58-106 (tip z at :86), SkiBaseStationBuilding.Prefab
//     :65-112 (net :67, tip z at :92). The base's SECOND stub at x = 6 (x = −6 since T14 x-sign N, 2026-10-16; from :115, net 877654d1…
//     at :116) is the low-voltage feed, NOT the lift axis. The lift line leaves the station along local −Z
//     through x = 0 (nitpick B2, 2026-10-13: through x = StationDef.StubTipLocalX - 0 on every station but the
//     tram base, whose 6 x 3 lot put its line axis at prefab x +4.00 in the game frame (−4.00 before T14 x-sign N,
//     2026-10-16); the 4-argument StubTip), so:
//       tip  P = LocalToWorld(T, (0,0,StubTipLocalZ))   — ObjectUtils.LocalToWorld(Transform, float3)
//                                                         is position + mul(rotation, local)
//                                                         (Game.Objects/ObjectUtils.cs:208-211)
//       axis f = normalize(xz(mul(R, (0,0,−1))))
//   * Measure(partner p, subject s), all in the horizontal plane (x, z):
//       d        = xz(P_s − P_p)
//       lateral  = cross(f_p, d) = f_p.x·d.z − f_p.z·d.x   (signed; see SIGN CONVENTION)
//       along    = dot(f_p, d)                               (> 0 = in front of the partner's stub)
//       yaw      = signed angle from −f_p to f_s, degrees in (−180, 180]
//       aligned  ⇔ |lateral| ≤ AlignedOffsetM ∧ |yaw| ≤ AlignedYawDeg ∧ along > 0
//     "Perfectly 180" = the subject's tip on the partner's axis and the two stubs pointing at each
//     other. The formula is symmetric in role, so it works whichever station is placed second.
//   * Bearing error (the partner pick, §5.5) = atan2(|lateral|, along) in degrees: how far off the
//     candidate partner's axis the subject sits (0 = dead ahead, 90 = abeam, 180 = behind).
//
// SIGN CONVENTION. Unity / CS2 world space is left-handed: seen from above (+y), x points right
// and z points up the screen, so turning from +z to +x is CLOCKWISE. With cross(a, b) = a.x·b.z −
// a.z·b.x: lateral > 0 = the subject's tip is LEFT of the partner's axis (seen from the partner,
// looking out along its stub), < 0 = right; yaw > 0 = the subject's stub is turned
// COUNTER-CLOCKWISE (seen from above) from pointing straight back at the partner, < 0 = clockwise.
//
// TOLERANCE (OD-9 (a), owner decision taken as recommended): 0.5 m / 0.5°. The vanilla object tool's
// right-drag rotation turns 2π·0.002 rad = 0.72° per pixel (Game.Tools/ObjectToolSystem.cs:3495,
// 1.6.2 :3554) and the precise-rotation input 90°/s × input (:3478-3479), so 0.5° is reachable
// with the precise input or a one-pixel drag, not by a coarse drag.
//
// READOUT CLASS (Round F6, item B2; docs/research/field/2026-10-03b/findings/ui-small.md §2.5 and its
// Verification "Missed"). Field test 6 showed "Angle off straight −180 °" next to the green
// "Straight — 180°": the best and the worst state both said 180, and at ±180 the sign is noise (the
// UI prints whole degrees from 100°, so −179.6 reads −180). Classify(r), first match wins:
//   * Straight          — r.Aligned;
//   * PartnerFacingAway — r.Valid ∧ AlongM ≤ 0: the subject's tip is behind (or abeam of) the
//                         PARTNER's stub, i.e. the other station's line leaves away from this one.
//                         Decided for the checker's AlongM ≤ 0 case: turning the SUBJECT cannot fix
//                         this (worked case: a base facing its top correctly while the top faces away
//                         measures yaw 180 from the base — "turn it around" would be wrong advice for
//                         the base; the top, measured against the base, is in front and gets
//                         FacingAway). Reachable only with a ROW partner: a free candidate is picked
//                         only with along > 0 and measured with the same Transforms;
//   * FacingAway        — r.Valid ∧ FacesAway(YawDeg): |yaw| > FacingAwayDeg (90°, exclusive) — the
//                         subject's own line leaves away from the partner: "turn it around";
//   * Numbers           — everything else (offset + signed angle, the fine-tuning view).
//
// READOUT PLACE (Round F6 item B1, Round E1 addendum A2 as narrowed by the E1 fixer pass 2, e1-spec
// §8.10 G1). Where SkiLiftTooltipSystem puts the readout rows (and the no-partner row), PlaceOf(preview,
// moved, defaultTool), first match wins:
//   * the subject is the object tool's preview  → Mouse (the preview sits under the cursor);
//   * the default tool is active                 → World (a group at the subject's stub tip);
//   * any other tool, the subject JUST MOVED     → Mouse (SkiStationAlignSystem subject (ii): a Move It
//     nudge — field-test-6-fixes.md §4.3: "send subject (ii) rows to the mouse list when the tool is not
//     the default one");
//   * any other tool, the subject only SELECTED  → None (F6 B1: a station left selected must not add its
//     rows to the hub's run brush, the Net Tool, the bulldozer or the tree tool).
//
// MOVE EPISODES (Round F6, item B3; findings/log-audit.md A5). `Align: moved and settled` was one line
// per 15-frame pause: 18 lines in 3.7 s for one top station whose height re-settled after placement,
// 27 lines in 40 s for one base station (field log 373-432). StationMoveEpisodes (below) folds the
// census's "last moved station" stream into one episode PER STATION: a move frame at least
// MoveGapFrames after that station's previous one counts as a new move; each episode closes on its
// own after QuietSeconds of wall clock AND MoveGapFrames frames without a move of that station. The
// system writes one line per closed episode: "moved N time(s) over T s", then the final numbers.
// Per station, not "close when another station moves": should the census's last-moved station
// alternate between two stations frame by frame, a single-episode tracker would close and log on
// every frame — a flood; here each station keeps one open episode and one line.
// QuietSeconds = 2 s matches SkiStationAlignSystem.MovedWindowS, the time the readout keeps following
// a moved station (tunable, unmeasured). The reconnect-episode pattern (LiftReconnectEpisodes.cs).
//
// PURE FILE: `using System;` + `System.Collections.Generic` + `using Unity.Mathematics;` only (the
// test-host rule of tests/SlopeBatch; tests/LiftBatch links this file into a net8 host), plus the
// shared pure EntityRef (same namespace, linked into LiftBatch). The static math allocates nothing and
// holds no state; the one stateful piece is StationMoveEpisodes, a small class owned by
// SkiStationAlignSystem: a reused list of OPEN episodes (a station or two at a time), so no
// allocation in steady state.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Pure station alignment math (hub-u2-spec.md §5.5).</summary>
    public static class StationAlign
    {
        /// <summary>Station-local z of the HV stub's outer node (the line's connection point):
        /// SkiTopStationBuilding.Prefab:86 / SkiBaseStationBuilding.Prefab:92 (curve point d).
        /// Catalog Phase A (ext row 11; was StationAlign.cs:99 = -12f, and a second copy in
        /// SkiResortStatsSystem.cs:314 that now calls StubTip below): an alias of SkiCatalog.StubTipLocalZ
        /// (every StationDef carries the same value; tests/LiftBatch CatalogTests CT-6).</summary>
        public const float StubTipLocalZ = SkiCatalog.StubTipLocalZ;

        /// <summary>OD-9 (a) offset tolerance, metres.</summary>
        public const float AlignedOffsetM = 0.5f;

        /// <summary>OD-9 (a) yaw tolerance, degrees.</summary>
        public const float AlignedYawDeg = 0.5f;

        /// <summary>F6 B2: beyond this yaw error (exclusive) the subject faces away from the partner.</summary>
        public const float FacingAwayDeg = 90f;

        /// <summary>F6 B2: what the readout shows for one measurement (header READOUT CLASS). The
        /// values are SkiLiftTooltipSystem's row layouts.</summary>
        public enum Readout
        {
            Numbers = 0,
            Straight = 1,
            FacingAway = 2,
            PartnerFacingAway = 3,
        }

        /// <summary>E1 fixer pass 2 (header READOUT PLACE): where the readout rows go.</summary>
        public enum ReadoutPlace
        {
            None = 0,
            Mouse = 1,
            World = 2,
        }

        /// <summary>Header READOUT PLACE: <paramref name="subjectIsPreview"/> = the object tool's preview
        /// (subject (i)); <paramref name="subjectJustMoved"/> = subject (ii), a station moved within
        /// SkiStationAlignSystem.MovedWindowS; <paramref name="defaultToolActive"/> = ToolSystem.activeTool
        /// is the DefaultToolSystem.</summary>
        public static ReadoutPlace PlaceOf(bool subjectIsPreview, bool subjectJustMoved, bool defaultToolActive)
        {
            if (subjectIsPreview)
                return ReadoutPlace.Mouse;
            if (defaultToolActive)
                return ReadoutPlace.World;
            return subjectJustMoved ? ReadoutPlace.Mouse : ReadoutPlace.None;
        }

        /// <summary>One measurement of a subject station against a partner station.</summary>
        public struct Result
        {
            /// <summary>False when either axis is degenerate (a station rotated so that its −Z has no
            /// horizontal component — not reachable with yaw-only placement, guarded anyway).</summary>
            public bool Valid;
            /// <summary>Signed: + left, − right of the partner's axis (seen from the partner).</summary>
            public float LateralM;
            /// <summary>Distance along the partner's axis; ≤ 0 = behind the partner's stub.</summary>
            public float AlongM;
            /// <summary>Signed angle from −f_p to f_s, degrees in (−180, 180]; + counter-clockwise.</summary>
            public float YawDeg;
            /// <summary>Horizontal tip-to-tip distance, metres.</summary>
            public float DistanceM;
            /// <summary>Subject tip height minus partner tip height, metres.</summary>
            public float RiseM;
            /// <summary>atan2(|lateral|, along) in degrees, [0, 180].</summary>
            public float BearingErrorDeg;
            /// <summary>|lateral| ≤ 0.5 m ∧ |yaw| ≤ 0.5° ∧ along > 0.</summary>
            public bool Aligned;
        }

        /// <summary>World position of the HV stub tip: position + mul(rotation, (0,0,−12)) — the fixed-grip stations'
        /// value. Catalog Phase B: = StubTip(position, rotation, StubTipLocalZ); a caller that has the station's
        /// StationDef passes its StubTipLocalZ to the 3-argument overload.</summary>
        public static float3 StubTip(float3 position, quaternion rotation)
        {
            return StubTip(position, rotation, StubTipLocalZ);
        }

        /// <summary>Catalog Phase B (round-2-contract §6.4): the HV stub tip of a station whose stub ends at
        /// <paramref name="stubTipLocalZ"/> (StationDef.StubTipLocalZ, PER ROW: −12 on the fixed-grip and tram
        /// stations, −16 on the detachable and gondola stations, SkiDQBaseStationA.manifest.json `stub_tip_local_z`):
        /// position + mul(rotation, (0, 0, stubTipLocalZ)) — ObjectUtils.LocalToWorld (Game.Objects/ObjectUtils.cs
        /// :208-211). A non-finite z falls back to StubTipLocalZ (the fixed-grip value).</summary>
        public static float3 StubTip(float3 position, quaternion rotation, float stubTipLocalZ)
        {
            return StubTip(position, rotation, stubTipLocalZ, 0f);
        }

        /// <summary>Nitpick B2 (2026-10-13, build/nitpick-b2-tram-ramps.md): the same with the lift line axis at
        /// prefab-local x <paramref name="stubTipLocalX"/> (StationDef.StubTipLocalX: +4.00 on the tram base (T14 x-sign N, 2026-10-16;
        /// −4.00 before the mirror), whose 6 x 3 lot moved its building 4 m off the origin; 0 elsewhere): position + mul(rotation, (x, 0, z)). A non-finite x
        /// counts as 0, a non-finite z falls back to StubTipLocalZ.</summary>
        public static float3 StubTip(float3 position, quaternion rotation, float stubTipLocalZ, float stubTipLocalX)
        {
            float z = float.IsNaN(stubTipLocalZ) || float.IsInfinity(stubTipLocalZ) ? StubTipLocalZ : stubTipLocalZ;
            float x = float.IsNaN(stubTipLocalX) || float.IsInfinity(stubTipLocalX) ? 0f : stubTipLocalX;
            return position + math.mul(rotation, new float3(x, 0f, z));
        }

        /// <summary>Catalog Phase B (round-2-contract orchestrator addendum 1, dq-family-review-geometry.md F2): which
        /// side of the lift a point lies on, LOOKING UPHILL (from the base toward the top), in the horizontal plane.
        /// +1 = right, −1 = left, 0 = on the axis (within 1 mm) or a degenerate axis (base and top within 1 mm in xz).
        /// Left-handed CS2 world (header SIGN CONVENTION): looking along d = top − base, the right-hand normal is
        /// (d.z, −d.x) — along +z the right is +x, along +x it is −z.</summary>
        public static int SideLookingUphill(float2 baseXz, float2 topXz, float2 pointXz)
        {
            float2 d = topXz - baseXz;
            float len = math.length(d);
            if (!(len > 1e-3f))
                return 0;
            float side = math.dot(pointXz - baseXz, new float2(d.y, -d.x) / len);
            if (!(math.abs(side) > 1e-3f))
                return 0;
            return side > 0f ? 1 : -1;
        }

        /// <summary>Catalog Phase B (orchestrator addendum 1): the up lane of a loop is the cable on the RIGHT looking
        /// uphill, so a carrier facing its travel direction with its hanger at local +x keeps the hanger OUTBOARD of
        /// the tower decks on both sides (up: right looking uphill; down: facing downhill, +x is the left looking
        /// uphill — outboard again). Given the two chains' midpoints (the mean of a chain's two end points: independent
        /// of the chain's direction), true when the SECOND chain lies right of the first, i.e. the caller must swap
        /// them to make chain A the up lane. False when the first is already right, or when the two cannot be told
        /// apart (a degenerate axis or both midpoints on the same side within 1 mm: keep the order found).</summary>
        public static bool UpLaneIsSecond(float2 baseXz, float2 topXz, float2 firstMidXz, float2 secondMidXz)
        {
            float2 d = topXz - baseXz;
            float len = math.length(d);
            if (!(len > 1e-3f))
                return false;
            float2 right = new float2(d.y, -d.x) / len;
            float delta = math.dot(secondMidXz - firstMidXz, right);
            return delta > 1e-3f;
        }

        /// <summary>Unit horizontal direction in which the line leaves the station (local −Z), as
        /// (x, z); (0, 0) when degenerate.</summary>
        public static float2 Axis(quaternion rotation)
        {
            float3 f = math.mul(rotation, new float3(0f, 0f, -1f));
            float2 xz = new float2(f.x, f.z);
            float len = math.length(xz);
            return len > 1e-6f ? xz / len : float2.zero;
        }

        /// <summary>2D cross product a.x·b.y − a.y·b.x on (x, z) pairs (see SIGN CONVENTION).</summary>
        public static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        /// <summary>Signed angle from <paramref name="from"/> to <paramref name="to"/>, degrees in
        /// (−180, 180]; + = counter-clockwise seen from above. Both must be non-zero.</summary>
        public static float SignedAngleDeg(float2 from, float2 to)
        {
            float a = math.degrees(math.atan2(Cross(from, to), math.dot(from, to)));
            return a <= -180f ? a + 360f : a;
        }

        /// <summary>The OD-9 aligned predicate (inclusive tolerances, strictly in front).</summary>
        public static bool IsAligned(float lateralM, float yawDeg, float alongM)
        {
            return alongM > 0f && math.abs(lateralM) <= AlignedOffsetM && math.abs(yawDeg) <= AlignedYawDeg;
        }

        /// <summary>F6 B2: |yaw| &gt; 90° (exclusive; NaN → false) — the subject's stub points away from
        /// the partner rather than back at it. Yaw only: the caller decides the AlongM ≤ 0 case first
        /// (Classify).</summary>
        public static bool FacesAway(float yawDeg)
        {
            return math.abs(yawDeg) > FacingAwayDeg;
        }

        /// <summary>F6 B2: the readout class of one measurement (header READOUT CLASS; first match
        /// wins: Straight, PartnerFacingAway, FacingAway, Numbers). An invalid measurement (a
        /// degenerate axis) shows the numbers, as before.</summary>
        public static Readout Classify(in Result r)
        {
            if (r.Aligned)
                return Readout.Straight;
            if (!r.Valid)
                return Readout.Numbers;
            if (r.AlongM <= 0f)
                return Readout.PartnerFacingAway;
            return FacesAway(r.YawDeg) ? Readout.FacingAway : Readout.Numbers;
        }

        /// <summary>Measures the subject (tip + axis) against the partner (tip + axis). Pure,
        /// allocation-free.</summary>
        public static Result Measure(float3 partnerTip, float2 partnerAxis, float3 subjectTip, float2 subjectAxis)
        {
            var r = new Result();
            float2 d = new float2(subjectTip.x - partnerTip.x, subjectTip.z - partnerTip.z);
            r.DistanceM = math.length(d);
            r.RiseM = subjectTip.y - partnerTip.y;
            bool partnerOk = math.lengthsq(partnerAxis) > 0.25f;   // unit axis or (0,0) from Axis()
            bool subjectOk = math.lengthsq(subjectAxis) > 0.25f;
            r.Valid = partnerOk && subjectOk;
            if (!partnerOk)
                return r;
            r.LateralM = Cross(partnerAxis, d);
            r.AlongM = math.dot(partnerAxis, d);
            r.BearingErrorDeg = math.degrees(math.atan2(math.abs(r.LateralM), r.AlongM));
            if (subjectOk)
                r.YawDeg = SignedAngleDeg(-partnerAxis, subjectAxis);
            r.Aligned = r.Valid && IsAligned(r.LateralM, r.YawDeg, r.AlongM);
            return r;
        }
    }

    /// <summary>F6 B3: "moved and settled" episodes, one per station (StationAlign header MOVE
    /// EPISODES). Fed every update with the census's last-moved station, its move frame and its
    /// wall-clock time; pure; a reused list of open episodes.</summary>
    public sealed class StationMoveEpisodes
    {
        /// <summary>A move frame at least this many frames after the station's previous one is a new
        /// move (the former SettleFrames: each such move used to log its own line).</summary>
        public const int MoveGapFrames = 15;

        /// <summary>Wall-clock seconds without a move before an episode closes (tunable, unmeasured;
        /// = SkiStationAlignSystem.MovedWindowS).</summary>
        public const double QuietSeconds = 2.0;

        /// <summary>One episode (open, or closed and handed back for its one log line).</summary>
        public struct Episode
        {
            public EntityRef Station;
            public int Moves;           // moves separated by ≥ MoveGapFrames frames (≥ 1)
            public int MoveFrames;      // distinct frames on which the station moved
            public long LastFrame;
            public double FirstTime, LastTime;
            /// <summary>First move → last move, wall clock.</summary>
            public double SpanSeconds => math.max(0.0, LastTime - FirstTime);
        }

        private readonly List<Episode> m_Open = new List<Episode>(4);
        private EntityRef m_SeenStation;
        private long m_SeenFrame = long.MinValue;

        public int OpenCount => m_Open.Count;

        /// <summary>Forget everything (game load: open episodes name entities of the previous world).</summary>
        public void Reset()
        {
            m_Open.Clear();
            m_SeenStation = default;
            m_SeenFrame = long.MinValue;
        }

        /// <summary>One observation of the census's last move. A null station and a repeat of the
        /// last (station, frame) pair are ignored; otherwise the station's open episode takes the move,
        /// or a new episode opens.</summary>
        public void Observe(EntityRef station, long movedFrame, double movedTime)
        {
            if (station.IsNull || (station == m_SeenStation && movedFrame == m_SeenFrame))
                return;
            m_SeenStation = station;
            m_SeenFrame = movedFrame;
            for (int i = 0; i < m_Open.Count; i++)
            {
                Episode ep = m_Open[i];
                if (ep.Station != station)
                    continue;
                if (movedFrame - ep.LastFrame >= MoveGapFrames)
                    ep.Moves++;
                ep.MoveFrames++;
                ep.LastFrame = movedFrame;
                ep.LastTime = movedTime;
                m_Open[i] = ep;
                return;
            }
            m_Open.Add(new Episode
            {
                Station = station,
                Moves = 1,
                MoveFrames = 1,
                LastFrame = movedFrame,
                FirstTime = movedTime,
                LastTime = movedTime,
            });
        }

        /// <summary>Closes ONE open episode that has been quiet for QuietSeconds of wall clock AND
        /// MoveGapFrames frames (so a slow frame rate still waits for the station to settle); call
        /// until it returns false.</summary>
        public bool TryCloseOne(long frameNow, double timeNow, out Episode closed)
        {
            for (int i = 0; i < m_Open.Count; i++)
            {
                Episode ep = m_Open[i];
                if (timeNow - ep.LastTime < QuietSeconds || frameNow - ep.LastFrame < MoveGapFrames)
                    continue;
                m_Open.RemoveAt(i);
                closed = ep;
                return true;
            }
            closed = default;
            return false;
        }
    }
}
