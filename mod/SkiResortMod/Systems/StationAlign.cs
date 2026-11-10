// Pure geometry of the station alignment aid (read-only guide + readout + an "aligned" state at 0.5 m / 0.5 deg). Consumers:
// SkiStationAlignSystem, SkiLiftTooltipSystem (readout), LiftCensus (per-row AlignKnown / AlignOffsetM / AlignYawDeg),
// StationFacingRefusalSystem (AttachesBackward / LineEnds, FIX-LIST 54(b)), tests/LiftBatch.
// StubTipLocalZ aliases the pure SkiCatalog.cs, so SkiCatalog.cs is linked wherever this file is. The stub tip is per station row:
// StubTip(position, rotation, StationDef.StubTipLocalZ) (-16 detachable / gondola, -12 fixed-grip and tram). The 2-argument overload is -12.
// Also the pure up-lane rule of the loop cache: SideLookingUphill / UpLaneIsSecond (chain A = the cable on the RIGHT looking uphill).
// Left-handed world: lateral > 0 = the subject's tip is LEFT of the partner's axis. Yaw > 0 = its stub is turned COUNTER-CLOCKWISE.
// Pure file: System, System.Collections.Generic, Unity.Mathematics only (tests/SlopeBatch host rule) plus the shared pure EntityRef.
// Engine notes: EngineNotes/StationAlign.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class StationAlign
    {
        /// <summary>Station-local z of the HV stub's outer node (the line's connection point): SkiTopStationBuilding.Prefab:86 /
        /// SkiBaseStationBuilding.Prefab:92 (curve point d). An alias of SkiCatalog.StubTipLocalZ (every StationDef carries the same
        /// value. See CatalogTests CT-6).</summary>
        public const float StubTipLocalZ = SkiCatalog.StubTipLocalZ;

        /// <summary>Offset tolerance, meters.</summary>
        public const float AlignedOffsetM = 0.5f;

        /// <summary>Yaw tolerance, degrees.</summary>
        public const float AlignedYawDeg = 0.5f;

        /// <summary>Beyond this yaw error (exclusive) the subject faces away from the partner.</summary>
        public const float FacingAwayDeg = 90f;

        /// <summary>What the readout shows for one measurement (see engine notes, Readout class). The values are SkiLiftTooltipSystem's row layouts.</summary>
        public enum Readout
        {
            Numbers = 0,
            Straight = 1,
            FacingAway = 2,
            PartnerFacingAway = 3,
        }

        /// <summary>Where the readout rows go (see engine notes, Readout place).</summary>
        public enum ReadoutPlace
        {
            None = 0,
            Mouse = 1,
            World = 2,
        }

        /// <summary><paramref name="subjectIsPreview"/> = the object tool's preview (subject (i)). <paramref name="subjectJustMoved"/> =
        /// subject (ii), a station moved within SkiStationAlignSystem.MovedWindowS. <paramref name="defaultToolActive"/> =
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
            /// <summary>False when either axis is degenerate (a station rotated so that its -Z has no horizontal component). Yaw-only
            /// placement does not reach this case, but the code guards it anyway.</summary>
            public bool Valid;
            /// <summary>Signed: + left, − right of the partner's axis (seen from the partner).</summary>
            public float LateralM;
            /// <summary>Distance along the partner's axis. ≤ 0 = behind the partner's stub.</summary>
            public float AlongM;
            /// <summary>Signed angle from −f_p to f_s, degrees in (−180, 180]. + counter-clockwise.</summary>
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
        /// -12 on the fixed-grip and tram stations, -16 on the detachable and gondola stations). Formula: position + mul(rotation, (0, 0,
        /// stubTipLocalZ)). A non-finite z falls back to StubTipLocalZ (the fixed-grip value).</summary>
        public static float3 StubTip(float3 position, quaternion rotation, float stubTipLocalZ)
        {
            return StubTip(position, rotation, stubTipLocalZ, 0f);
        }

        /// <summary>The same with the lift line axis at prefab-local x <paramref name="stubTipLocalX"/> (StationDef.StubTipLocalX: +4.00
        /// on the tram base, whose 6 x 3 lot moved its building 4 m off the origin. 0 elsewhere). Formula: position + mul(rotation, (x, 0, z)).
        /// A non-finite x counts as 0. A non-finite z falls back to StubTipLocalZ.</summary>
        public static float3 StubTip(float3 position, quaternion rotation, float stubTipLocalZ, float stubTipLocalX)
        {
            float z = float.IsNaN(stubTipLocalZ) || float.IsInfinity(stubTipLocalZ) ? StubTipLocalZ : stubTipLocalZ;
            float x = float.IsNaN(stubTipLocalX) || float.IsInfinity(stubTipLocalX) ? 0f : stubTipLocalX;
            return position + math.mul(rotation, new float3(x, 0f, z));
        }

        /// <summary>Which side of the lift a point lies on, LOOKING UPHILL (from the base toward the top), in the horizontal plane. +1 =
        /// right, -1 = left. 0 = on the axis (within 1 mm) or a degenerate axis (base and top within 1 mm in xz). Left-handed CS2 world
        /// (see engine notes, Sign convention): looking along d = top - base, the right-hand normal is (d.z, -d.x).</summary>
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

        /// <summary>The up lane of a loop is the cable on the RIGHT looking uphill. Therefore a carrier that faces its travel direction,
        /// with its hanger at local +x, keeps the hanger OUTBOARD of the tower decks on both sides.
        /// Input: the two chains' midpoints (the mean of a chain's two end points, independent of the chain's direction).
        /// True when the SECOND chain lies right of the first, i.e. the caller must swap them to make chain A the up lane.
        /// False when the first is already right, or when the two cannot be told apart (a degenerate axis, or both midpoints on the same
        /// side within 1 mm: keep the order found).</summary>
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
        /// (x, z). It returns (0, 0) when degenerate.</summary>
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
        /// (−180, 180]. + = counter-clockwise seen from above. Both must be non-zero.</summary>
        public static float SignedAngleDeg(float2 from, float2 to)
        {
            float a = math.degrees(math.atan2(Cross(from, to), math.dot(from, to)));
            return a <= -180f ? a + 360f : a;
        }

        public static bool IsAligned(float lateralM, float yawDeg, float alongM)
        {
            return alongM > 0f && math.abs(lateralM) <= AlignedOffsetM && math.abs(yawDeg) <= AlignedYawDeg;
        }

        /// <summary>|yaw| &gt; 90 deg (exclusive, and NaN gives false): the subject's stub points away from the partner rather than back at
        /// it. Yaw only: the caller decides the AlongM &lt;= 0 case first (Classify).</summary>
        public static bool FacesAway(float yawDeg)
        {
            return math.abs(yawDeg) > FacingAwayDeg;
        }

        /// <summary>The line tool's check: the other end of the line lies behind this station's stub, so the line would run back through
        /// the building. False for a degenerate axis.</summary>
        public static bool LineLeavesBackward(float2 stationAxis, float2 stubTipXz, float2 otherEndXz)
        {
            return math.lengthsq(stationAxis) > 0.25f && math.dot(stationAxis, otherEndXz - stubTipXz) <= 0f;
        }

        /// <summary>Length of every station's HV stub (the SkiLiftMarker sub-net), meters: it runs from its inner node at local z
        /// StubTipLocalZ + 3 to the tip at StubTipLocalZ, at x StubTipLocalX (all 8 station prefabs: FG / tram -9 to -12, DQ / gondola
        /// -13 to -16. LiftBatch EI-48 checks the mirror).</summary>
        public const float StubLengthM = 3f;

        /// <summary>World position of the HV stub's INNER node (StubTipLocalZ + StubLengthM, toward the building). The vanilla snap
        /// can put either stub node on a net node (FIX-LIST 54(b), field 2026-11-04: the reversed tram base had its inner node merged
        /// onto the line end).</summary>
        public static float3 StubInner(float3 position, quaternion rotation, float stubTipLocalZ, float stubTipLocalX)
        {
            float z = float.IsNaN(stubTipLocalZ) || float.IsInfinity(stubTipLocalZ) ? StubTipLocalZ : stubTipLocalZ;
            return StubTip(position, rotation, z + StubLengthM, stubTipLocalX);
        }

        /// <summary>A station preview's stub node this close (xz) to a built lift line's end node sits on it (vanilla merges the node
        /// there or stands its own node at the same spot). See StationFacingRefusalSystem.</summary>
        public const float AttachTolM = 1f;

        /// <summary>Which of the station's two stub nodes a net node sits on (StationFacingRefusalSystem).</summary>
        public enum StubNode
        {
            None = 0,
            Tip = 1,
            Inner = 2,
        }

        /// <summary>The stub node within AttachTolM (xz) of <paramref name="nodeXz"/>, the nearer one when both are. It returns None when
        /// neither is, or when a position is not finite.</summary>
        public static StubNode StubNodeOn(float2 stubTipXz, float2 stubInnerXz, float2 nodeXz)
        {
            float dTip = math.distance(stubTipXz, nodeXz), dInner = math.distance(stubInnerXz, nodeXz);
            bool tip = dTip <= AttachTolM, inner = dInner <= AttachTolM;   // NaN compares false
            if (tip && (!inner || dTip <= dInner))
                return StubNode.Tip;
            return inner ? StubNode.Inner : StubNode.None;
        }

        /// <summary>FIX-LIST 54(b): a station preview snapped onto the END node of a built lift line (<paramref name="endXz"/>, under
        /// either of its stub nodes) while the line's next node (<paramref name="nextXz"/>) lies behind the stub, measured from the end
        /// node (LineLeavesBackward). The line would run back through the building.
        /// Returns the stub node that sits on the end. Returns None when the station does not attach there or the line leaves ahead of
        /// the stub.
        /// The vanilla object tool offers the snap with the tool's own rotation as well as the aligned one. Either stub node can land on
        /// the node (the field reversed tram base had its INNER node merged onto the end and placed. Vanilla already refuses the
        /// tip-on-node snap as "Invalid shape"). So a reversed station snaps on, and StationFacingRefusalSystem refuses it.
        /// The caller passes true ends only (LineEnds: one lift-line edge at the node), so it never judges a station on an interior
        /// tower node here.</summary>
        public static StubNode AttachesBackward(float2 stationAxis, float2 stubTipXz, float2 stubInnerXz, float2 endXz, float2 nextXz)
        {
            StubNode on = StubNodeOn(stubTipXz, stubInnerXz, endXz);
            return on != StubNode.None && LineLeavesBackward(stationAxis, endXz, nextXz) ? on : StubNode.None;
        }

        /// <summary>The true ends of a lift-line net: every node with exactly one edge among <paramref name="edges"/> (start, end
        /// pairs), mapped to the other node of that edge (the line's next node). An interior tower node (two edges) is not an end. A
        /// degenerate edge (both nodes the same) is skipped.</summary>
        public static Dictionary<TNode, TNode> LineEnds<TNode>(IEnumerable<KeyValuePair<TNode, TNode>> edges) where TNode : IEquatable<TNode>
        {
            var ends = new Dictionary<TNode, TNode>();
            LineEnds(edges, new Dictionary<TNode, int>(), new Dictionary<TNode, TNode>(), ends);
            return ends;
        }

        /// <summary>LineEnds into caller-owned dictionaries (cleared first. The per-frame caller reuses them).</summary>
        public static void LineEnds<TNode>(IEnumerable<KeyValuePair<TNode, TNode>> edges, Dictionary<TNode, int> degree,
                                           Dictionary<TNode, TNode> next, Dictionary<TNode, TNode> ends) where TNode : IEquatable<TNode>
        {
            degree.Clear();
            next.Clear();
            ends.Clear();
            foreach (KeyValuePair<TNode, TNode> e in edges)
            {
                if (e.Key.Equals(e.Value))
                    continue;
                degree[e.Key] = degree.TryGetValue(e.Key, out int a) ? a + 1 : 1;
                degree[e.Value] = degree.TryGetValue(e.Value, out int b) ? b + 1 : 1;
                next[e.Key] = e.Value;
                next[e.Value] = e.Key;
            }
            foreach (KeyValuePair<TNode, int> d in degree)
                if (d.Value == 1)
                    ends.Add(d.Key, next[d.Key]);
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

        /// <summary>The two stations of a planned lift, posed so that they face each other on one axis (LiftPose). Positions carry y NaN:
        /// the station placer stands each building at the vanilla height (StationPlaceToolSystem.ResolveY).</summary>
        public struct LiftPose
        {
            /// <summary>False when a spot is not finite or the spots are too close for the two stubs (SpotDistanceM at or under
            /// MinTipGapM + both stub offsets).</summary>
            public bool Valid;
            public float3 BasePosition, TopPosition;
            public quaternion BaseRotation, TopRotation;
            /// <summary>Unit horizontal direction from the bottom spot to the top spot, (x, z).</summary>
            public float2 Dir;
            public float SpotDistanceM;
            /// <summary>Horizontal distance between the two stub tips: SpotDistanceM minus both |StubTipLocalZ|.</summary>
            public float TipDistanceM;
            /// <summary>The stub tips (y NaN), for the log and the tests.</summary>
            public float3 BaseTip, TopTip;
        }

        /// <summary>LiftPose refuses spots whose stub tips would end closer than this, meters.</summary>
        public const float MinTipGapM = 1f;

        /// <summary>The planner's lift as two station poses. The spots are the station spots of the plan (PlanLift.Bottom / Top, the lot
        /// centers the overlay draws). Each station's line axis (local x = StubTipLocalX) runs through its spot. So the chord between
        /// the spots is the line, and the tram base's 4 m side offset moves its origin, never the line. The base faces the top: its local
        /// -Z = Dir. The top faces the base: its local -Z = -Dir. Then the stub tips lie on the chord, |StubTipLocalZ| in from each spot.
        /// Yaw convention as quaternion.RotateY: local +z = (sin a, cos a), so local -Z = Dir needs a = atan2(-Dir.x, -Dir.y).</summary>
        public static LiftPose PoseLift(float2 bottomXz, float2 topXz, float baseStubZ, float baseStubX, float topStubZ, float topStubX)
        {
            var p = new LiftPose();
            float bz = math.abs(Finite(baseStubZ) ? baseStubZ : StubTipLocalZ), tz = math.abs(Finite(topStubZ) ? topStubZ : StubTipLocalZ);
            float bx = Finite(baseStubX) ? baseStubX : 0f, tx = Finite(topStubX) ? topStubX : 0f;
            float2 d = topXz - bottomXz;
            p.SpotDistanceM = math.length(d);
            if (!math.all(math.isfinite(bottomXz)) || !math.all(math.isfinite(topXz)) || !(p.SpotDistanceM > bz + tz + MinTipGapM))
                return p;
            d /= p.SpotDistanceM;
            p.Dir = d;
            p.BaseRotation = quaternion.RotateY(math.atan2(-d.x, -d.y));
            p.TopRotation = quaternion.RotateY(math.atan2(d.x, d.y));
            float3 b = new float3(bottomXz.x, 0f, bottomXz.y), t = new float3(topXz.x, 0f, topXz.y);
            float3 bo = b - math.mul(p.BaseRotation, new float3(bx, 0f, 0f));
            float3 to = t - math.mul(p.TopRotation, new float3(tx, 0f, 0f));
            float3 btip = StubTip(bo, p.BaseRotation, -bz, bx), ttip = StubTip(to, p.TopRotation, -tz, tx);
            p.BasePosition = new float3(bo.x, float.NaN, bo.z);
            p.TopPosition = new float3(to.x, float.NaN, to.z);
            p.BaseTip = new float3(btip.x, float.NaN, btip.z);
            p.TopTip = new float3(ttip.x, float.NaN, ttip.z);
            p.TipDistanceM = math.distance(btip.xz, ttip.xz);
            p.Valid = true;
            return p;
        }

        /// <summary>PoseLift with the two stations' catalog rows (StubTipLocalZ, StubTipLocalX). An invalid pose when a row is null.</summary>
        public static LiftPose PoseLift(float2 bottomXz, float2 topXz, StationDef baseStation, StationDef topStation)
        {
            if (baseStation == null || topStation == null)
                return new LiftPose { SpotDistanceM = math.distance(bottomXz, topXz) };
            return PoseLift(bottomXz, topXz, baseStation.StubTipLocalZ, baseStation.StubTipLocalX, topStation.StubTipLocalZ, topStation.StubTipLocalX);
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

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

    /// <summary>"Moved and settled" episodes, one per station (see engine notes, Move episodes). Fed every update with the census's
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

        /// <summary>One observation of the census's last move. The method ignores a null station and a repeat of the last (station, frame)
        /// pair. Otherwise the station's open episode takes the move, or a new episode opens.</summary>
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
        /// rate still waits for the station to settle). Call until it returns false.</summary>
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
