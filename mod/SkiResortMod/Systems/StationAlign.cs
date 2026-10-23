// Pure geometry of the station alignment aid (read-only guide + readout + an "aligned" state at 0.5 m / 0.5 deg). Consumers:
// SkiStationAlignSystem (subject / partner / overlay / placement log), SkiLiftTooltipSystem (readout), LiftCensus (per-row
// AlignKnown / AlignOffsetM / AlignYawDeg), tests/LiftBatch. StubTipLocalZ aliases the pure SkiCatalog.cs, so SkiCatalog.cs is
// linked wherever this file is; SkiResortStatsSystem's stub-tip height calls StubTip.
// The stub tip is per station row: StubTip(position, rotation, StationDef.StubTipLocalZ) (-16 on the detachable / gondola
// stations, -12 on the fixed-grip and tram stations); the 2-argument overload is the fixed-grip -12. Also the pure up-lane rule of
// the loop cache: SideLookingUphill / UpLaneIsSecond (chain A = the cable on the RIGHT looking uphill), tested in LiftTypeTests.
//
// Geometry (verified in the prefab JSON):
//   * Both station prefabs carry their high-voltage connection stub as an ObjectSubNet of the SkiLiftMarker net from
//     station-local (0,0,-9) to (0,0,-12), node indices {0,1}: SkiTopStationBuilding.Prefab:58-106 (tip z at :86),
//     SkiBaseStationBuilding.Prefab:65-112 (net :67, tip z at :92). The base's SECOND stub at x = -6 is the low-voltage feed, NOT
//     the lift axis. The lift line leaves the station along local -Z through x = 0 on every station but the tram base, whose
//     6 x 3 lot puts its line axis at prefab x +4.00 in the game frame (the 4-argument StubTip), so:
//       tip  P = LocalToWorld(T, (0,0,StubTipLocalZ))   (ObjectUtils.cs:208-211: position + mul(rotation, local))
//       axis f = normalize(xz(mul(R, (0,0,-1))))
//   * Measure(partner p, subject s), in the horizontal plane (x, z):
//       d        = xz(P_s - P_p)
//       lateral  = cross(f_p, d) = f_p.x*d.z - f_p.z*d.x   (signed; see sign convention)
//       along    = dot(f_p, d)                               (> 0 = in front of the partner's stub)
//       yaw      = signed angle from -f_p to f_s, degrees in (-180, 180]
//       aligned  <=> |lateral| <= AlignedOffsetM and |yaw| <= AlignedYawDeg and along > 0
//     "Perfectly 180" = the subject's tip on the partner's axis and the two stubs pointing at each other. The formula is
//     symmetric in role, so it works whichever station is placed second.
//   * Bearing error (the partner pick) = atan2(|lateral|, along) in degrees: how far off the candidate partner's axis the
//     subject sits (0 = dead ahead, 90 = abeam, 180 = behind).
//
// Sign convention: Unity / CS2 world space is left-handed: seen from above (+y), x points right and z points up the screen, so
// turning from +z to +x is CLOCKWISE. With cross(a, b) = a.x*b.z - a.z*b.x: lateral > 0 = the subject's tip is LEFT of the
// partner's axis (seen from the partner, looking out along its stub), < 0 = right; yaw > 0 = the subject's stub is turned
// COUNTER-CLOCKWISE (seen from above) from pointing straight back at the partner, < 0 = clockwise.
//
// Tolerance 0.5 m / 0.5 deg: the vanilla object tool's right-drag rotation turns 2*pi*0.002 rad = 0.72 deg per pixel
// (ObjectToolSystem.cs:3495) and the precise-rotation input 90 deg/s x input (:3478-3479), so 0.5 deg is reachable with the
// precise input or a one-pixel drag, not by a coarse drag.
//
// Readout class: the best and the worst state must not both say 180 (at +-180 the sign is noise; the UI prints whole degrees
// from 100). Classify(r), first match wins:
//   * Straight          r.Aligned;
//   * PartnerFacingAway r.Valid and AlongM <= 0: the subject's tip is behind (or abeam of) the PARTNER's stub, i.e. the other
//                       station's line leaves away from this one. Turning the SUBJECT cannot fix this (a base facing its top
//                       correctly while the top faces away measures yaw 180 from the base, and "turn it around" would be wrong
//                       advice for the base; the top, measured against the base, is in front and gets FacingAway). Reachable
//                       only with a ROW partner: a free candidate is picked only with along > 0;
//   * FacingAway        r.Valid and FacesAway(YawDeg): |yaw| > FacingAwayDeg (90 deg, exclusive), the subject's own line
//                       leaves away from the partner: "turn it around";
//   * Numbers           everything else (offset + signed angle, the fine-tuning view).
//
// Readout place: where SkiLiftTooltipSystem puts the readout rows, PlaceOf(preview, moved, defaultTool), first match wins:
//   * the subject is the object tool's preview  -> Mouse (the preview sits under the cursor);
//   * the default tool is active                 -> World (a group at the subject's stub tip);
//   * any other tool, the subject JUST MOVED     -> Mouse (SkiStationAlignSystem subject (ii): a Move It nudge);
//   * any other tool, the subject only SELECTED  -> None (a station left selected must not add its rows to the hub's run brush,
//     the Net Tool, the bulldozer or the tree tool).
//
// Move episodes: `Align: moved and settled` once logged a line per 15-frame pause (18 lines in 3.7 s for one station whose height
// re-settled after placement). StationMoveEpisodes folds the census's "last moved station" stream into one episode PER STATION:
// a move frame at least MoveGapFrames after that station's previous one counts as a new move; each episode closes on its own
// after QuietSeconds of wall clock AND MoveGapFrames frames without a move of that station. The system writes one line per
// closed episode. Per station, not "close when another station moves": should the census's last-moved station alternate
// between two stations frame by frame, a single-episode tracker would close and log every frame. QuietSeconds = 2 s matches
// SkiStationAlignSystem.MovedWindowS (unmeasured). The pattern of LiftReconnectEpisodes.cs.
//
// Pure file: System, System.Collections.Generic and Unity.Mathematics only (tests/SlopeBatch host rule), plus the shared pure
// EntityRef (same namespace, linked into LiftBatch). The static math holds no state; StationMoveEpisodes is a small class owned
// by SkiStationAlignSystem with a reused list of open episodes, so no allocation in steady state.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class StationAlign
    {
        /// <summary>Station-local z of the HV stub's outer node (the line's connection point): SkiTopStationBuilding.Prefab:86 /
        /// SkiBaseStationBuilding.Prefab:92 (curve point d). An alias of SkiCatalog.StubTipLocalZ (every StationDef carries the same
        /// value; CatalogTests CT-6).</summary>
        public const float StubTipLocalZ = SkiCatalog.StubTipLocalZ;

        /// <summary>Offset tolerance, meters.</summary>
        public const float AlignedOffsetM = 0.5f;

        /// <summary>Yaw tolerance, degrees.</summary>
        public const float AlignedYawDeg = 0.5f;

        /// <summary>Beyond this yaw error (exclusive) the subject faces away from the partner.</summary>
        public const float FacingAwayDeg = 90f;

        /// <summary>What the readout shows for one measurement (see header, Readout class). The values are SkiLiftTooltipSystem's row layouts.</summary>
        public enum Readout
        {
            Numbers = 0,
            Straight = 1,
            FacingAway = 2,
            PartnerFacingAway = 3,
        }

        /// <summary>Where the readout rows go (see header, Readout place).</summary>
        public enum ReadoutPlace
        {
            None = 0,
            Mouse = 1,
            World = 2,
        }

        /// <summary><paramref name="subjectIsPreview"/> = the object tool's preview (subject (i)); <paramref name="subjectJustMoved"/> =
        /// subject (ii), a station moved within SkiStationAlignSystem.MovedWindowS; <paramref name="defaultToolActive"/> =
        /// ToolSystem.activeTool is the DefaultToolSystem.</summary>
        public static ReadoutPlace PlaceOf(bool subjectIsPreview, bool subjectJustMoved, bool defaultToolActive)
        {
            if (subjectIsPreview)
                return ReadoutPlace.Mouse;
            if (defaultToolActive)
                return ReadoutPlace.World;
            return subjectJustMoved ? ReadoutPlace.Mouse : ReadoutPlace.None;
        }

        public struct Result
        {
            /// <summary>False when either axis is degenerate (a station rotated so that its -Z has no horizontal component; not reachable
            /// with yaw-only placement, guarded anyway).</summary>
            public bool Valid;
            /// <summary>Signed: + left, − right of the partner's axis (seen from the partner).</summary>
            public float LateralM;
            /// <summary>Distance along the partner's axis; ≤ 0 = behind the partner's stub.</summary>
            public float AlongM;
            /// <summary>Signed angle from −f_p to f_s, degrees in (−180, 180]; + counter-clockwise.</summary>
            public float YawDeg;
            /// <summary>Horizontal tip-to-tip distance, meters.</summary>
            public float DistanceM;
            /// <summary>Subject tip height minus partner tip height, meters.</summary>
            public float RiseM;
            /// <summary>atan2(|lateral|, along) in degrees, [0, 180].</summary>
            public float BearingErrorDeg;
            /// <summary>|lateral| ≤ 0.5 m ∧ |yaw| ≤ 0.5° ∧ along > 0.</summary>
            public bool Aligned;
        }

        /// <summary>World position of the HV stub tip: position + mul(rotation, (0,0,-12)), the fixed-grip stations' value. A caller that
        /// has the station's StationDef passes its StubTipLocalZ to the 3-argument overload.</summary>
        public static float3 StubTip(float3 position, quaternion rotation)
        {
            return StubTip(position, rotation, StubTipLocalZ);
        }

        /// <summary>The HV stub tip of a station whose stub ends at <paramref name="stubTipLocalZ"/> (StationDef.StubTipLocalZ, per row:
        /// -12 on the fixed-grip and tram stations, -16 on the detachable and gondola stations): position + mul(rotation, (0, 0,
        /// stubTipLocalZ)). A non-finite z falls back to StubTipLocalZ (the fixed-grip value).</summary>
        public static float3 StubTip(float3 position, quaternion rotation, float stubTipLocalZ)
        {
            return StubTip(position, rotation, stubTipLocalZ, 0f);
        }

        /// <summary>The same with the lift line axis at prefab-local x <paramref name="stubTipLocalX"/> (StationDef.StubTipLocalX: +4.00
        /// on the tram base, whose 6 x 3 lot moved its building 4 m off the origin; 0 elsewhere): position + mul(rotation, (x, 0, z)). A
        /// non-finite x counts as 0, a non-finite z falls back to StubTipLocalZ.</summary>
        public static float3 StubTip(float3 position, quaternion rotation, float stubTipLocalZ, float stubTipLocalX)
        {
            float z = float.IsNaN(stubTipLocalZ) || float.IsInfinity(stubTipLocalZ) ? StubTipLocalZ : stubTipLocalZ;
            float x = float.IsNaN(stubTipLocalX) || float.IsInfinity(stubTipLocalX) ? 0f : stubTipLocalX;
            return position + math.mul(rotation, new float3(x, 0f, z));
        }

        /// <summary>Which side of the lift a point lies on, LOOKING UPHILL (from the base toward the top), in the horizontal plane. +1 =
        /// right, -1 = left, 0 = on the axis (within 1 mm) or a degenerate axis (base and top within 1 mm in xz). Left-handed CS2 world
        /// (see header, Sign convention): looking along d = top - base, the right-hand normal is (d.z, -d.x).</summary>
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

        /// <summary>The up lane of a loop is the cable on the RIGHT looking uphill, so a carrier facing its travel direction with its
        /// hanger at local +x keeps the hanger OUTBOARD of the tower decks on both sides. Given the two chains' midpoints (the mean of a
        /// chain's two end points: independent of the chain's direction), true when the SECOND chain lies right of the first, i.e. the
        /// caller must swap them to make chain A the up lane. False when the first is already right, or when the two cannot be told
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

        public static bool IsAligned(float lateralM, float yawDeg, float alongM)
        {
            return alongM > 0f && math.abs(lateralM) <= AlignedOffsetM && math.abs(yawDeg) <= AlignedYawDeg;
        }

        /// <summary>|yaw| &gt; 90 deg (exclusive; NaN gives false): the subject's stub points away from the partner rather than back at
        /// it. Yaw only: the caller decides the AlongM &lt;= 0 case first (Classify).</summary>
        public static bool FacesAway(float yawDeg)
        {
            return math.abs(yawDeg) > FacingAwayDeg;
        }

        /// <summary>The readout class of one measurement (first match wins: Straight, PartnerFacingAway, FacingAway, Numbers). An invalid
        /// measurement (a degenerate axis) shows the numbers.</summary>
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

    /// <summary>"Moved and settled" episodes, one per station (see header, Move episodes). Fed every update with the census's
    /// last-moved station, its move frame and its wall-clock time.</summary>
    public sealed class StationMoveEpisodes
    {
        /// <summary>A move frame at least this many frames after the station's previous one is a new move.</summary>
        public const int MoveGapFrames = 15;

        /// <summary>Wall-clock seconds without a move before an episode closes (= SkiStationAlignSystem.MovedWindowS).</summary>
        public const double QuietSeconds = 2.0;

        public struct Episode
        {
            public EntityRef Station;
            public int Moves;           // moves separated by ≥ MoveGapFrames frames (≥ 1)
            public int MoveFrames;      // distinct frames on which the station moved
            public long LastFrame;
            public double FirstTime, LastTime;
            public double SpanSeconds => math.max(0.0, LastTime - FirstTime);
        }

        private readonly List<Episode> m_Open = new List<Episode>(4);
        private EntityRef m_SeenStation;
        private long m_SeenFrame = long.MinValue;

        public int OpenCount => m_Open.Count;

        public void Reset()
        {
            m_Open.Clear();
            m_SeenStation = default;
            m_SeenFrame = long.MinValue;
        }

        /// <summary>One observation of the census's last move. A null station and a repeat of the last (station, frame) pair are ignored;
        /// otherwise the station's open episode takes the move, or a new episode opens.</summary>
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

        /// <summary>Closes ONE open episode that has been quiet for QuietSeconds of wall clock AND MoveGapFrames frames (so a slow frame
        /// rate still waits for the station to settle); call until it returns false.</summary>
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
