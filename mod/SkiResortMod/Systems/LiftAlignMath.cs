// Pure geometry of the lift alignment fix (docs: lift-alignment-2026-11-02). It covers the plan bend at a tower and the rope slopes a
// hanging lane meets a node with. It also covers the pitch of a sheave train that follows them. It uses no Game or Entities type
// (Unity.Mathematics only), so tests/LiftBatch links it (AlignTests.cs, AL-*).
// The rope of a span is the engine's hanging lane. It is the chord a..d with b, c at 1/3 and 2/3 lowered by |a.xz - d.xz| * m_Hanging * 4/3
// (LaneSystem.cs:4128-4138, LiftTowerEdit.HangingCurve). Its end slopes are the chord slope -4h (leaving a) and +4h (arriving at d). So a
// tower between two spans sees a break-over of the chord change plus 8h.
// Engine notes: EngineNotes/LiftAlignMath.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>The sheave train a line tower needs (breakover-holddown-plan-2026-11-03, section 2.2). None means no train was judged (a
    /// station end, a tram saddle, an end without a neighbor). Support means the rope presses down. HoldDown means the rope pulls up, and
    /// the train sits over the rope. PushPull means the load reverses between load cases: wheels on both sides.</summary>
    public enum LiftTrainKind : byte
    {
        None,
        Support,
        HoldDown,
        PushPull,
    }

    internal static class LiftAlignMath
    {
        /// <summary>Plan bend above which the lift tool and the hub warn (owner verdict 2026-11-02: warn, never refuse). Real lines turn
        /// with sheaves only by a few degrees. Larger turns are angle stations.</summary>
        public const float BendWarnDeg = 5f;

        /// <summary>The plan angle, degrees in [0, 180], between the line arriving at <paramref name="node"/> from <paramref name="prev"/>
        /// and leaving it toward <paramref name="next"/> (0 = straight). y is ignored. A zero-length leg gives 0.</summary>
        public static float PlanBendDeg(float3 prev, float3 node, float3 next)
        {
            float2 a = node.xz - prev.xz, b = next.xz - node.xz;
            float la = math.length(a), lb = math.length(b);
            if (la < 1e-4f || lb < 1e-4f)
                return 0f;
            float c = math.clamp(math.dot(a, b) / (la * lb), -1f, 1f);
            return math.degrees(math.acos(c));
        }

        /// <summary>Slope dy/dxz of the hanging rope a..d where it LEAVES a (toward d): chord - 4h.</summary>
        public static float LeaveSlope(float3 a, float3 d, float hanging)
        {
            float l = math.distance(a.xz, d.xz);
            return l < 1e-4f ? 0f : (d.y - a.y) / l - 4f * hanging;
        }

        /// <summary>Slope dy/dxz of the hanging rope a..d where it ARRIVES at d (coming from a): chord + 4h.</summary>
        public static float ArriveSlope(float3 a, float3 d, float hanging)
        {
            float l = math.distance(a.xz, d.xz);
            return l < 1e-4f ? 0f : (d.y - a.y) / l + 4f * hanging;
        }

        /// <summary>THE rope angles at a tower. This is the one place where they are computed: the train pitch, the breakover / hold-down
        /// pass of the tower planner and the tilt round all call it. <paramref name="prev"/> / <paramref name="node"/> /
        /// <paramref name="next"/> are the rope points, in line order. Their y is the rope lane y at the anchors. Their x / z are in the plan, and
        /// the distance between them is the xz length. <paramref name="inAngle"/> = atan of the slope the rope ARRIVES at the node with
        /// (chord + 4h). <paramref name="outAngle"/> = atan of the slope it LEAVES with toward next (chord - 4h). Both are in radians,
        /// measured along prev -> next (up = +).</summary>
        public static void RopeAngles(float3 prev, float3 node, float3 next, float hanging, out float inAngle, out float outAngle)
        {
            inAngle = math.atan(ArriveSlope(prev, node, hanging));
            outAngle = math.atan(LeaveSlope(node, next, hanging));
        }

        /// <summary>The rope's deflection at the tower, degrees: in - out (RopeAngles). A value &gt; 0 is convex (a crest): the rope
        /// presses DOWN on support wheels. A value &lt; 0 is concave (a dip): the rope pulls UP (a hold-down train). The result is the same
        /// either way along the line. On level ground with equal neighbors it is the engine's sag kink 2 atan(4h) (FG 9.15, DQ / GD 11.42,
        /// tram 18.18 deg). On a uniform slope g it is atan(g + 4h) - atan(g - 4h), a little less.</summary>
        public static float DeflectionDeg(float3 prev, float3 node, float3 next, float hanging)
        {
            RopeAngles(prev, node, next, hanging, out float a, out float b);
            return math.degrees(a - b);
        }

        /// <summary>Train pitch, radians, nose UP toward <paramref name="next"/>, at a line node <paramref name="node"/>. Its rope points
        /// are <paramref name="prev"/> and <paramref name="next"/> (the axle or crown points of the neighbors). The pitch is the bisector
        /// of the arriving and leaving rope angles (RopeAngles), clamped to ±<paramref name="maxPitch"/>. A straight uniform slope gives
        /// exactly that slope.</summary>
        public static float TrainPitch(float3 prev, float3 node, float3 next, float hanging, float maxPitch)
        {
            RopeAngles(prev, node, next, hanging, out float inAngle, out float outAngle);
            return math.clamp(0.5f * (inAngle + outAngle), -maxPitch, maxPitch);
        }

        // ── Rope pitch at a tower (DECISIONS 600, docs research/steep-angles-real-lifts-2026-11-04.md sections 2, 5.1) ───────────

        /// <summary>The rope's pitch through a tower, degrees, up toward next = +. It is the mean of the two adjacent chords' angles (prev
        /// -> node and node -> next, (along, rope y) points). Unlike the bend (DeflectionDeg), a pair or a taller tower barely changes it.
        /// It follows the ground. A zero-length chord takes the other's angle.</summary>
        public static float ChordPitchDeg(float sP, float rP, float s, float r, float sN, float rN)
        {
            float dl = s - sP, dr = sN - s;
            float a = math.abs(dl) < 1e-4f ? float.NaN : math.atan((r - rP) / dl);
            float b = math.abs(dr) < 1e-4f ? float.NaN : math.atan((rN - r) / dr);
            if (math.isnan(a)) a = math.isnan(b) ? 0f : b;
            if (math.isnan(b)) b = a;
            return math.degrees(0.5f * (a + b));
        }

        /// <summary>The hard pitch cap per lift type, degrees. These are the steepest real line maxima (research steep-angles section 5.1):
        /// FG 38 (Pic des Tentes 72 %, Tete de Balme 77 %), DQ 40 and GD 40 (Lai Alv 79 %, Grand Massif 81 %), tram 55 (Loen 54, Schilthorn
        /// 57.9). Past it the planner repairs (shift, member, pair) and the lift tool warns "too steep". The value is +infinity for an
        /// unknown type.</summary>
        public static float PitchCapDeg(string typeId)
        {
            switch (typeId)
            {
                case "fixedGrip": return 38f;
                case "detachable": return 40f;
                case "gondola": return 40f;
                case "tram": return 55f;
                default: return float.PositiveInfinity;
            }
        }

        /// <summary>The soft pitch cap per lift type, degrees (research section 5.1, the value the planner prefers): FG 36, DQ 38, GD 39,
        /// tram 45. The value is +infinity for an unknown type.</summary>
        public static float PitchSoftDeg(string typeId)
        {
            switch (typeId)
            {
                case "fixedGrip": return 36f;
                case "detachable": return 38f;
                case "gondola": return 39f;
                case "tram": return 45f;
                default: return float.PositiveInfinity;
            }
        }

        // ── Mast lean (DECISIONS 600, spike tilted-towers-2026-11-04 option A, research steep-angles section 5.3) ─────────────────

        /// <summary>The head (DQ / GD Head bone) and the FG trains' mean pitch relative to the mast, at most, degrees. A visual choice
        /// (research section 5.3: "do not exceed 25 deg head tilt relative to the mast"). The value 20 keeps the 594 trunnion inside its
        /// swept wedge.</summary>
        public const float HeadRelMaxDeg = 20f;

        /// <summary>HeadRelMaxDeg in radians: the headed towers' head clamp (HeadPitch / HeadedHalfPitches headMax), relative to the mast.</summary>
        public static float HeadRelMax => math.radians(HeadRelMaxDeg);

        /// <summary>A tower's mast pitch, radians nose up along its +z, read from its rotation. It is 0 for a plumb, yaw-only tower and
        /// LeanRotation's signed lean for a leaned one. TrainPitchSystem subtracts it from the absolute lane-end pitches: the bones are
        /// local to the mast.</summary>
        public static float MastPitch(quaternion rotation)
        {
            float3 f = math.mul(rotation, new float3(0f, 0f, 1f));
            return math.atan2(f.y, math.length(f.xz));
        }

        /// <summary>The steepest mast lean, degrees: DQ / GD 20 (sourced range 10-18 deg measured, +-40 RM), FG 25 (FgMaxLeanDeg), tram
        /// 25 (Sandia 18, Loen "strongly overhanging"). A visual choice.</summary>
        public const float MaxLeanDeg = 20f, TramMaxLeanDeg = 25f;

        /// <summary>The FG's own lean rule (DECISIONS 600 a, call d, critique SHOULD-3). The FG has no head, so its mast sets the crossarm
        /// to the rope grade. The FG mast leans from FgLeanFreeDeg 12 deg of pitch, up to FgMaxLeanDeg 25. The call said about 10. At 10
        /// the 1-2 deg leans of 11-12 deg pitches left a 2 cm floor shortfall on two FG20s that LeanPass cannot raise (LP-10). They also
        /// left a needless 4 m raise beside a station (LP-30). At the 38 cap the trains' mean sits 13 off the mast either way. The halves
        /// (mean + the Break twin's 16 deg half-break) sit at most 28, under the FG art's 30 (PC-6).</summary>
        public const float FgLeanFreeDeg = 12f, FgMaxLeanDeg = 25f;

        /// <summary>The pitch the head alone follows before the mast leans, degrees: HeadRelMaxDeg for DQ / GD, FgLeanFreeDeg for the FG
        /// (no head: the trains hang off the mast's crossarm), the tram saddle's TrainPitchLimitDeg (the letter C halves) for the tram.</summary>
        public static float LeanFreeDeg(string typeId) =>
            typeId == "tram" ? TrainPitchLimitDeg : typeId == "fixedGrip" ? FgLeanFreeDeg : HeadRelMaxDeg;

        /// <summary>The type's steepest lean: MaxLeanDeg (DQ / GD), FgMaxLeanDeg (FG), TramMaxLeanDeg (tram).</summary>
        public static float MaxLeanDegOf(string typeId) =>
            typeId == "tram" ? TramMaxLeanDeg : typeId == "fixedGrip" ? FgMaxLeanDeg : MaxLeanDeg;

        /// <summary>The largest mean rope pitch relative to the mast up to the type's hard pitch cap, degrees: max(LeanFreeDeg, cap -
        /// max lean) (FG 13, DQ / GD 20, tram 30).</summary>
        public static float MaxRelMeanDeg(string typeId) =>
            math.max(LeanFreeDeg(typeId), PitchCapDeg(typeId) - MaxLeanDegOf(typeId));

        /// <summary>How the lean follows the pitch (THE lean rule: the owner judges the look from previews, so it is one switch). HeadFirst
        /// (default): lean = clamp(|pitch| - LeanFreeDeg, 0, max lean). A mast leans only where the head alone cannot follow the rope.
        /// Proportional: lean = |pitch| x LeanShare, at most the max lean. Every pitched tower leans a little.</summary>
        public enum LeanRule : byte
        {
            HeadFirst,
            Proportional,
        }

        /// <summary>The lean rule in force (LeanDeg).</summary>
        public const LeanRule Rule = LeanRule.HeadFirst;

        /// <summary>LeanRule.Proportional's share of the pitch.</summary>
        public const float LeanShare = 0.5f;

        /// <summary>The mast lean, in degrees, of a <paramref name="typeId"/> tower whose rope pitch is <paramref name="pitchDeg"/>
        /// (ChordPitchDeg, either sign). The value is &gt;= 0. The top leans downhill, toward the lower of the two chords' ends.</summary>
        public static float LeanDeg(string typeId, float pitchDeg)
        {
            float p = math.abs(pitchDeg), max = MaxLeanDegOf(typeId);
            if (!math.isfinite(p))
                return 0f;
            return Rule == LeanRule.Proportional ? math.min(p * LeanShare, max) : math.clamp(p - LeanFreeDeg(typeId), 0f, max);
        }

        /// <summary>The rope anchor of a leaned tower in the line's (along, y) plane. The mast turns about its foot
        /// (<paramref name="along"/>, <paramref name="footY"/>). So the anchor (<paramref name="ropeY"/> - footY above the foot) moves downhill by
        /// that height x sin(lean) and drops by height x (1 - cos(lean)). Downhill is against the sign of <paramref name="pitchDeg"/> (up
        /// toward +along = +). See spike tilted-towers-2026-11-04 section 1.2: the head shift yA sin(lean).</summary>
        public static void LeanedAnchor(float along, float footY, float ropeY, float pitchDeg, float leanDeg, out float anchorAlong,
            out float anchorY)
        {
            float h = ropeY - footY, a = math.radians(leanDeg);
            float sign = pitchDeg > 0f ? 1f : pitchDeg < 0f ? -1f : 0f;
            anchorAlong = along - sign * h * math.sin(a);
            anchorY = footY + h * math.cos(a);
        }

        /// <summary>The leaned tower's rotation. It is the node yaw <paramref name="yaw"/> pitched about its local x so the mast top leans
        /// by <paramref name="leanDeg"/> downhill. <paramref name="pitchAlongZ"/> is the rope's pitch (degrees, nose up along the tower's
        /// +z). A rope rising toward +z leans the top toward -z. The mast is turned nose up along +z by the lean (PitchedRotation's
        /// convention, RotateX(-a) = nose up along +z).</summary>
        public static quaternion LeanRotation(quaternion yaw, float pitchAlongZ, float leanDeg)
        {
            float a = math.radians(leanDeg) * (pitchAlongZ > 0f ? 1f : pitchAlongZ < 0f ? -1f : 0f);
            return math.mul(yaw, quaternion.RotateX(-a));
        }

        /// <summary>The lean, radians nose up along the tower's +z, that LeanRotation applied (the signed mast pitch the bones are
        /// relative to).</summary>
        public static float SignedLean(float pitchAlongZ, float leanDeg) =>
            math.radians(leanDeg) * (pitchAlongZ > 0f ? 1f : pitchAlongZ < 0f ? -1f : 0f);

        // ── The padded face uphill (DECISIONS 600 e) ──────────────────────────────────────────────────────────────────────────────
        // Every lift tower mesh has its crash pad (padded face, warning triangle) and its TrainFront half on local +z. The ladder, the
        // pad's opening and the Hold / Combo deck ends are on -z (art convention: research art/tower-lean-2026-11/build.md "Yaw fix").
        // Vanilla yaws a node tower along the line's draw direction (Game.Net/NodeAlignSystem.cs:85-111 sums the edges' travel tangents,
        // and :209-212 flips the yaw to agree). So a line drawn top to bottom showed every pad downhill. TowerLeanSystem instead turns the
        // tower by 0 or 180 deg about vertical: +z toward the section's higher rope end. It makes one decision per section (station to
        // station, end to end). So every tower of a section faces the same way, as on a real lift. A section whose ends differ by less than
        // PadFlatRiseM keeps vanilla's yaw. Ropes are unchanged by the turn: every tower's anchor set is symmetric under it (LiftBatch
        // PF-2). So LaneSystem.FindAnchors snaps every rope lane to the same points. It matches by position
        // (Game.Net/LaneSystem.cs:2492-2560, FindAnchor :4049-4072).

        /// <summary>A section whose two rope ends differ by less than this, meters, is flat: its towers keep vanilla's yaw.</summary>
        public const float PadFlatRiseM = 1f;

        /// <summary>Per node of one section, in order from its first end to its last (ends included), the unit xz direction its tower's +z
        /// (the padded face) must point. Each direction points toward the next node on the side of the higher rope end. The higher end
        /// itself points away from its neighbor (the line falls away from it). All values are zero when the ends' rope heights
        /// <paramref name="firstRopeY"/> / <paramref name="lastRopeY"/> differ by less than PadFlatRiseM (or are not finite). Then the tower keeps
        /// vanilla's yaw. Either draw order of the same section gives the same world directions.</summary>
        public static void SectionUphill(IReadOnlyList<float3> nodes, float firstRopeY, float lastRopeY, List<float2> into)
        {
            into.Clear();
            int n = nodes.Count;
            float rise = lastRopeY - firstRopeY;
            bool flat = n < 2 || !(math.abs(rise) >= PadFlatRiseM);
            int s = rise > 0f ? 1 : -1;
            for (int i = 0; i < n; i++)
            {
                if (flat)
                {
                    into.Add(float2.zero);
                    continue;
                }
                int j = i + s;
                float2 d = j >= 0 && j < n ? nodes[j].xz - nodes[i].xz : nodes[i].xz - nodes[i - s].xz;
                into.Add(math.lengthsq(d) > 1e-8f ? math.normalize(d) : float2.zero);
            }
        }

        /// <summary>The yaw-only rotation <paramref name="yaw"/>, or the same turned 180 deg about vertical, whichever puts the tower's +z
        /// along <paramref name="uphill"/> (xz). A zero <paramref name="uphill"/> keeps <paramref name="yaw"/>. Compose the lean after it
        /// (LeanRotation with the rope's pitch along the new +z). The top still tips downhill. The leaned frame of the turned tower is the
        /// old one turned about its own y (Q x RotateY(pi)), so a symmetric anchor set lands on the same world points.</summary>
        public static quaternion PadUphillYaw(quaternion yaw, float2 uphill)
        {
            float2 f = math.mul(yaw, new float3(0f, 0f, 1f)).xz;
            if (math.lengthsq(uphill) < 1e-8f || math.dot(f, uphill) >= 0f)
                return yaw;
            return math.normalize(math.mul(yaw, quaternion.RotateY(math.PI)));
        }

        // ── Breakover / hold-down (docs/research/breakover-holddown-plan-2026-11-03.md, section 2.2) ─────────────────────────────

        /// <summary>The largest rope deflection one roller may take, degrees: 0.07 rad (D.M. 8 March 1999, fixed-grip and detachable
        /// chairlifts, 3.18.3: support and hold-down rollers alike, worst steady load case). A train's cap is its wheels x this.</summary>
        public const float PerWheelMaxDeg = 4.0f;

        /// <summary>The least deflection, degrees, that keeps a roller pressed. It is derived from 500 N per roller (decree 16 November
        /// 2012, 14.6.4) at an assumed 120-300 kN haul-rope tension (4 x 500 / 120 kN = 0.95, 8 x 500 / 300 kN = 0.76 deg). The tension is
        /// unsourced for our types.</summary>
        public const float MinPressDeg = 1.0f;

        /// <summary>Sag scale of the TIGHT load case (+40 % rope tension, 14.6.4: a support tower in a dip must keep the rope): sag is
        /// load over tension, so h / 1.4.</summary>
        public const float TightSagScale = 1f / 1.4f;

        /// <summary>Sag scale of the HEAVY load case (-20 % tension and +25 % payload, 14.6.4: a hold-down tower must keep the rope):
        /// 1.25 / 0.8 = 1.5625 h (conservative: the whole weight scaled, not only the payload).</summary>
        public const float HeavySagScale = 1.5625f;

        /// <summary>A tower keeps its kind while that kind is still valid within this many degrees. The fit's re-classification after a
        /// terrain nudge or a re-fit then never flips a head back and forth.</summary>
        public const float KindHystDeg = 0.25f;

        /// <summary>Tram line saddles: the bare track rope must break over a line saddle by at least 0.02 rad (decree 16 November 2012,
        /// 14.4.4). Under it the saddle may lift. Logged only (trams are scoped out of pairs and hold-down).</summary>
        public const float SaddleMinBreakDeg = 1.15f;

        /// <summary>Sag scale of the tram's bare track rope (no cabin on the span): an assumption of the plan (section 2.6), logged only.</summary>
        public const float BareSagScale = 0.75f;

        /// <summary>The cap, degrees, of a sheave train with <paramref name="wheels"/> wheels: wheels x PerWheelMaxDeg, at most the art's
        /// fold 2 x MaxHalfBreakDeg (40). No wheels (a tram saddle) = no cap (+infinity).</summary>
        public static float TrainCapDeg(int wheels) =>
            wheels <= 0 ? float.PositiveInfinity : math.min(wheels * PerWheelMaxDeg, 2f * MaxHalfBreakDeg);

        /// <summary>The deflection of the two load cases (RopeAngles at h x TightSagScale and h x HeavySagScale), degrees.</summary>
        public static void LoadCaseDeflections(float3 prev, float3 node, float3 next, float hanging, out float tightDeg, out float heavyDeg)
        {
            tightDeg = DeflectionDeg(prev, node, next, hanging * TightSagScale);
            heavyDeg = DeflectionDeg(prev, node, next, hanging * HeavySagScale);
        }

        /// <summary>A tower's sheave-train kind from the two load-case deflections (degrees). The kind is Support when the rope still
        /// presses in the tight case (tight &gt;= +MinPressDeg). It is HoldDown when the rope still lifts in the heavy case (heavy &lt;=
        /// -MinPressDeg). Otherwise it is PushPull (the load reverses between the cases: wheels on both sides). The deflection grows with
        /// the sag, so tight &lt; heavy and the first two never both hold. <paramref name="previous"/> (None = unknown): the tower keeps
        /// that kind while it is still valid within KindHystDeg.</summary>
        public static LiftTrainKind ClassifyTrain(float tightDeg, float heavyDeg, LiftTrainKind previous = LiftTrainKind.None)
        {
            switch (previous)
            {
                case LiftTrainKind.Support:
                    if (tightDeg >= MinPressDeg - KindHystDeg)
                        return LiftTrainKind.Support;
                    break;
                case LiftTrainKind.HoldDown:
                    if (heavyDeg <= -MinPressDeg + KindHystDeg)
                        return LiftTrainKind.HoldDown;
                    break;
                case LiftTrainKind.PushPull:
                    if (tightDeg < MinPressDeg + KindHystDeg && heavyDeg > -MinPressDeg - KindHystDeg)
                        return LiftTrainKind.PushPull;
                    break;
            }
            if (tightDeg >= MinPressDeg)
                return LiftTrainKind.Support;
            if (heavyDeg <= -MinPressDeg)
                return LiftTrainKind.HoldDown;
            return LiftTrainKind.PushPull;
        }

        /// <summary>One letter for the logs: S support, H hold-down, P push-pull, - none.</summary>
        public static char KindLetter(LiftTrainKind k) =>
            k == LiftTrainKind.Support ? 'S' : k == LiftTrainKind.HoldDown ? 'H' : k == LiftTrainKind.PushPull ? 'P' : '-';

        /// <summary>Pitch, radians, nose up toward the line, of a station's line-side train. It is the angle of the first span's rope where
        /// it meets the station anchor <paramref name="station"/>, coming from (or going to) the first line rope point
        /// <paramref name="line"/>.</summary>
        public static float StationTrainPitch(float3 station, float3 line, float hanging, float maxPitch)
        {
            return math.clamp(math.atan(LeaveSlope(station, line, hanging)), -maxPitch, maxPitch);
        }

        /// <summary>The rotation about local x that pitches a node-yawed object nose up by <paramref name="pitchTowardNext"/> toward
        /// <paramref name="next"/>. The sign flips when the object's +z faces <paramref name="prev"/>. Unity's RotateX(+a) turns +z toward
        /// -y (nose down), so nose up along +z is RotateX(-a).</summary>
        public static quaternion PitchedRotation(quaternion yaw, float3 prev, float3 next, float pitchTowardNext)
        {
            float3 forward = math.mul(yaw, new float3(0f, 0f, 1f));
            float sign = math.dot(forward.xz, next.xz - prev.xz) >= 0f ? 1f : -1f;
            return math.mul(yaw, quaternion.RotateX(-sign * pitchTowardNext));
        }

        /// <summary>Pitch, radians, nose up along a tower's +z (<paramref name="forward"/>), that one rope lane end shows. The lane leaves
        /// its anchor <paramref name="end"/> toward its next control point <paramref name="inner"/> (b for end a, c for end d). The
        /// direction to it is the end tangent's direction. A lane on the +z side climbing away gives +, one on the -z side climbing away
        /// gives - (the rope comes down toward +z there). The mean over a tower's lane ends is TrainPitch's bisector on the engine's own
        /// curves.</summary>
        public static float LaneEndPitch(float3 end, float3 inner, float3 forward)
        {
            float3 t = inner - end;
            float l = math.length(t.xz);
            if (l < 1e-4f)
                return 0f;
            float side = math.dot(t.xz, forward.xz) >= 0f ? 1f : -1f;
            return side * math.atan2(t.y, l);
        }

        /// <summary>Which half of a tower's train one rope lane end feeds: +1 when the lane leaves its anchor <paramref name="end"/> toward
        /// the tower's +z (<paramref name="forward"/>, the TrainFront bones), -1 toward -z (TrainBack). The same side test as
        /// LaneEndPitch.</summary>
        public static int LaneEndSide(float3 end, float3 inner, float3 forward)
        {
            return math.dot((inner - end).xz, forward.xz) >= 0f ? 1 : -1;
        }

        /// <summary>The largest half-break, degrees, the articulated trains take: half the angle between the front and back halves (the
        /// rope's break at the tower over 2). The tower art clears it at any pitch (G6 joint sweep: inner wheels 0.06 m further out).</summary>
        public const float MaxHalfBreakDeg = 20f;

        /// <summary>The two half-train pitches of an articulated tower (owner verdict 2026-11-02, option C: halves hinged on the main pin,
        /// the rope corners there). <paramref name="front"/> / <paramref name="back"/> are the mean lane-end pitches (LaneEndPitch, nose up
        /// along +z) of the ropes leaving toward +z / -z. <paramref name="nFront"/> / <paramref name="nBack"/> are how many. A side without
        /// a rope takes the other's pitch (a straight train). Each half clamps to ±<paramref name="maxPitch"/>. The half-break clamps to
        /// MaxHalfBreakDeg about their mean. Then each half clamps again (so both limits hold). Returns true when the half-break was
        /// clamped. Returns false and zeros when neither side has a rope.</summary>
        public static bool HalfPitches(float front, int nFront, float back, int nBack, float maxPitch, out float pFront, out float pBack)
        {
            return HalfPitches(front, nFront, back, nBack, maxPitch, math.radians(MaxHalfBreakDeg), out pFront, out pBack);
        }

        /// <summary>HalfPitches with the half-break limit <paramref name="maxHalfBreak"/> (radians) of the tower's art (TrainLimits: a
        /// wide tram saddle clears TramHalfBreakMaxDeg).</summary>
        public static bool HalfPitches(float front, int nFront, float back, int nBack, float maxPitch, float maxHalfBreak, out float pFront,
            out float pBack)
        {
            pFront = pBack = 0f;
            if (nFront == 0 && nBack == 0)
                return false;
            if (nFront == 0)
                front = back;
            else if (nBack == 0)
                back = front;
            front = math.clamp(front, -maxPitch, maxPitch);
            back = math.clamp(back, -maxPitch, maxPitch);
            float mid = 0.5f * (front + back), half = 0.5f * (back - front), limit = maxHalfBreak;
            bool clamped = math.abs(half) > limit;
            if (clamped)
            {
                half = math.sign(half) * limit;
                front = mid - half;
                back = mid + half;
            }
            pFront = math.clamp(front, -maxPitch, maxPitch);
            pBack = math.clamp(back, -maxPitch, maxPitch);
            return clamped;
        }

        /// <summary>How far, degrees, the rope at a tower asks more of its train than HalfPitches gives. It is the larger of a half's pitch
        /// past <paramref name="maxPitch"/> and the half-break past MaxHalfBreakDeg, both from the unclamped pitches (inputs as
        /// HalfPitches, a side without a rope takes the other's pitch). The value is 0 when the train follows the rope, or when neither
        /// side has a rope. The hub's LiftTrainLimit row (TrainWarnDeg).</summary>
        public static float TrainOverDeg(float front, int nFront, float back, int nBack, float maxPitch)
        {
            return TrainOverDeg(front, nFront, back, nBack, maxPitch, math.radians(MaxHalfBreakDeg));
        }

        /// <summary>TrainOverDeg with the half-break limit <paramref name="maxHalfBreak"/> (radians) of the tower's art (TrainLimits).</summary>
        public static float TrainOverDeg(float front, int nFront, float back, int nBack, float maxPitch, float maxHalfBreak)
        {
            if (nFront == 0 && nBack == 0)
                return 0f;
            if (nFront == 0)
                front = back;
            else if (nBack == 0)
                back = front;
            float pitch = math.max(math.abs(front), math.abs(back)) - maxPitch;
            float halfBreak = 0.5f * math.abs(back - front) - maxHalfBreak;
            return math.max(0f, math.degrees(math.max(pitch, halfBreak)));
        }

        /// <summary>Tram saddle halves of art letter D or later (DECISIONS 597, docs research/art/tram-saddle-2026-11/build.md): each half
        /// follows its rope to this many degrees. A saddle half is the end zone of a real tram saddle. Its shoe leaves the rope
        /// tangentially however steep the span (34 Pa. Code 7.428(e)(2): the rope never reaches the groove's end). So the tram is not held
        /// to TrainPitchLimitDeg's upright-train look. The value 60 is the static head's rope reach. The tram's steepest rope is 68.2 deg.
        /// Past 60 the rope meets the lifting lugs whatever the halves do. The art clears it (tram_saddle.py G6 joint grid 60 /
        /// 30).</summary>
        public const float TramSaddlePitchLimitDeg = 60f;

        /// <summary>The half-break the letter D tram saddle halves clear (V-notch faces 32 deg off the vertical, haul rollers 0.10 m
        /// further out): 30 deg, against MaxHalfBreakDeg for every other train.</summary>
        public const float TramHalfBreakMaxDeg = 30f;

        /// <summary>The planner's tram saddle cap, degrees per half (DECISIONS 600 a, call b, critique SHOULD-1). Half the rope's bend at a
        /// tram tower (DeflectionDeg / 2, the drawn rope) is held to this for the LOOK, not for the art's reach (D clears
        /// TramHalfBreakMaxDeg 30). A 60 deg V on one saddle reads as a kink. A 15-20 deg half reads as a shoe. Past it the angle pass
        /// repairs the tower like a chair's (shift / member, a breakover pair, a longer span). Where a tower is still past it, the outcome
        /// is: OVER CAP, PLAN_SADDLE / TOWER_SADDLE and a red tower. The number: LiftBatch PC-7 (TS-1 coverage at 15 / 17.5 /
        /// 20).</summary>
        public const float TramBendHalfCapDeg = 20f;

        // ── Carrier clearance: the rope half's world pitch (DECISIONS 600 c, art test_grip_clearance.py ART_WORLD_HALF_MAX) ─────────
        // The carrier hangs plumb in the WORLD. Past a world pitch of the rope half it rides (the drawn rope's arriving or leaving angle at
        // the tower), the body swings into its own sheave train or the head's deck end, whatever the mast's lean. The lean only makes such
        // pitches reachable. The art measured the limit per family and kind (research art/tower-lean-2026-11/build.md "Pre-import checks"
        // 1, passes to / fails from: FG support and Break 41.5 / 44, GD members 55 / 57.5, GD Hold 45 / 47.5, DQ Hold 57.5 / 60). DQ
        // members, every Combo and the FG Hold clear the whole range. The planner repairs a tower past it like a bend (the angle pass:
        // shift / member, breakover pair, longer span). Where a tower is still past it, the outcome is: PAST CLEARANCE, the red tower,
        // TOWER_CLEARANCE (warn only). TrainPitchSystem never poses a half past it (PoseTrain).

        /// <summary>THE carrier-clearance table, (family, kind, degrees). Each row is the steepest world pitch of a rope half at which the
        /// art's carrier clears its train (the "passes to" column). It mirrors ART_WORLD_HALF_MAX in
        /// assets/blender/tests/test_grip_clearance.py. LiftBatch PC-9 parses that file and fails when the two differ. Family is FG / DQ /
        /// GD (WorldHalfFamily). Kind is "support" (the line member's head), "Break", "Hold" or "Combo". No row means no limit. Each call
        /// returns a new array. Callers cache it (LiftTowerPlanConstants, TrainPitchSystem per prefab).</summary>
        public static (string Family, string Kind, float Deg)[] WorldHalfTable() => new[]
        {
            ("FG", "support", 41.5f), ("FG", "Break", 41.5f), ("GD", "support", 55.0f), ("GD", "Hold", 45.0f), ("DQ", "Hold", 57.5f),
        };

        /// <summary>The table's family of a lift type id: FG / DQ / GD. Null for the tram and unknown types (no limit).</summary>
        public static string WorldHalfFamily(string typeId) =>
            typeId == "fixedGrip" ? "FG" : typeId == "detachable" ? "DQ" : typeId == "gondola" ? "GD" : null;

        /// <summary>The carrier-clearance limit, degrees, of a <paramref name="typeId"/> tower whose head is <paramref name="kind"/>
        /// ("support", "Break", "Hold", "Combo"). +infinity without a row.</summary>
        public static float WorldHalfMaxDeg(string typeId, string kind)
        {
            string family = WorldHalfFamily(typeId);
            if (family != null)
                foreach ((string Family, string Kind, float Deg) r in WorldHalfTable())
                    if (r.Family == family && r.Kind == kind)
                        return r.Deg;
            return float.PositiveInfinity;
        }

        /// <summary>The steeper rope half's world pitch at a tower, degrees (&gt;= 0): max(|in|, |out|) of RopeAngles (inputs as there).</summary>
        public static float WorldHalfDeg(float3 prev, float3 node, float3 next, float hanging)
        {
            RopeAngles(prev, node, next, hanging, out float a, out float b);
            return math.degrees(math.max(math.abs(a), math.abs(b)));
        }

        /// <summary>Degrees the rope halves at a tower lie past the carrier-clearance limit <paramref name="worldMax"/> (radians) in the
        /// world. <paramref name="front"/> / <paramref name="back"/> are the mean lane-end pitches relative to the mast (inputs as
        /// HalfPitches). <paramref name="mast"/> is the mast's pitch (MastPitch), so a half's world pitch is its pitch + mast. The value is
        /// 0 when within the limit, without a rope or without a limit.</summary>
        public static float WorldHalfOverDeg(float front, int nFront, float back, int nBack, float mast, float worldMax)
        {
            if ((nFront == 0 && nBack == 0) || !math.isfinite(worldMax))
                return 0f;
            if (nFront == 0)
                front = back;
            else if (nBack == 0)
                back = front;
            return math.max(0f, math.degrees(math.max(math.abs(front + mast), math.abs(back + mast)) - worldMax));
        }

        /// <summary>One line tower's train pose (TrainPitchSystem's whole rule, LiftBatch PC-9 replays it on the sweep plans). Inputs: the
        /// mean lane-end pitches relative to the mast and their counts (as HalfPitches), the mast's pitch <paramref name="mast"/>
        /// (radians), whether the mesh has a Head bone (<paramref name="headMax"/> its clamp), the unheaded train limits
        /// (<paramref name="maxPitch"/>, <paramref name="maxHalfBreak"/>: TrainLimits) and the carrier-clearance limit <paramref name="worldMax"/>
        /// (radians in the world, +infinity = none). The head and halves follow HeadedHalfPitches or HalfPitches. Then each half is clamped
        /// so its world pitch (half + mast) stays within worldMax (DECISIONS 600 c: the carrier hangs plumb, a steeper half would draw it
        /// through the train). Returns the degrees the rope asks past what the train follows (TrainOverDeg / HeadedTrainOverDeg, or the
        /// world excess when larger: the hub's LiftTrainLimit row). <paramref name="clamped"/> = a half was clamped (either rule).
        /// <paramref name="worldClamped"/> = clamped by the world limit.</summary>
        public static float PoseTrain(float meanF, int nF, float meanB, int nB, float mast, bool headed, float headMax, float maxPitch,
            float maxHalfBreak, float worldMax, out float head, out float front, out float back, out bool clamped, out bool worldClamped)
        {
            head = front = back = 0f;
            clamped = worldClamped = false;
            if (nF == 0 && nB == 0)
                return 0f;
            float over;
            if (headed)
            {
                over = HeadedTrainOverDeg(meanF, nF, meanB, nB, headMax);
                clamped = HeadedHalfPitches(meanF, nF, meanB, nB, headMax, out head, out front, out back);
            }
            else
            {
                over = TrainOverDeg(meanF, nF, meanB, nB, maxPitch, maxHalfBreak);
                clamped = HalfPitches(meanF, nF, meanB, nB, maxPitch, maxHalfBreak, out front, out back);
            }
            if (math.isfinite(worldMax))
            {
                over = math.max(over, WorldHalfOverDeg(meanF, nF, meanB, nB, mast, worldMax));
                float lo = -worldMax - mast, hi = worldMax - mast;
                float f = math.clamp(front, lo, hi), b = math.clamp(back, lo, hi);
                if (f != front || b != back)
                {
                    worldClamped = clamped = true;
                    front = f;
                    back = b;
                }
            }
            return over;
        }

        // ── Tram pedestals (DECISIONS 600 a, call c, critique SHOULD-2, art tower_lean.py foundation table) ─────────────────────
        // The tram tower stands on four concrete columns at its base corners (tram_tower.build_footings: a 1.5 m pedestal over a 12 m pier,
        // 1.4 m square, at +-B/2, B = 10 / 11 / 12 m for the 30 / 45 / 60 m tower). A lean turns them about the foot (the Transform
        // origin), so on a level crest the downhill legs dig in and the uphill piers stand proud. The planner floats the tower (node y =
        // ground + E + FloatM, the engine stands the member's pivot FloatM above the ground). The float is at least the dig-in of the
        // downhill steel (the FLOOR) and at most what the piers still cover (the CEILING). Between them the tower stands on four unequal
        // pedestals. The lean is never limited by the foot slope.

        /// <summary>Tram pier: depth below the origin, half its width (tram_tower.PIER_TR 12.0, PW_TR 1.4).</summary>
        public const float TramPierDepthM = 12f, TramPierHalfM = 0.7f;

        /// <summary>The downhill steel's lowest point in the tower frame: TramSteelOutM past the column center (B/2) and TramSteelZM above
        /// the origin (the leg gussets over the pedestal top). The values are fitted to the art's foundation check (steel_buried_m on a
        /// level crest: 0.92 / 1.13 / 1.34 m at lean 25, 0.44 / 0.61 / 0.78 at lean 20). The fit reproduces the check within 3
        /// mm.</summary>
        public const float TramSteelOutM = 0.325f, TramSteelZM = 1.466f;

        /// <summary>Half the tram tower's base width B, meters, by its top (tram_tower.make_spec: B 10 / 11 / 12 for the 30 / 45 / 60 m
        /// tower, linear between).</summary>
        public static float TramBaseHalfM(float topM) => 0.5f * (10f + (topM - 30f) / 15f);

        /// <summary>A tram tower's pedestal float, meters (the origin over the ground at the foot): max(0, min(FLOOR, CEILING)).
        /// <paramref name="groundDown"/>(x) is the terrain relative to the ground at the foot, x meters along the line DOWNHILL of it (the side the
        /// mast leans to, negative x = uphill). FLOOR = the downhill steel's depth under the terrain at float 0. CEILING = the least depth
        /// of the four pier-bottom edges (both piers' inner and outer edge, uphill and downhill) under the terrain at float 0 (the piers
        /// never lift off). Lean 0: 0 (the plumb tower stands on its pivot as before).</summary>
        public static float TramFloatM(float baseHalfM, float leanDeg, Func<float, float> groundDown, out float floorM, out float ceilingM)
        {
            floorM = 0f;
            ceilingM = float.PositiveInfinity;
            if (!(leanDeg > 0f) || groundDown == null)
                return 0f;
            float a = math.radians(leanDeg), sa = math.sin(a), ca = math.cos(a);
            // a tower-frame point (x downhill, z up) after the lean about the origin: (x cos + z sin, -x sin + z cos)
            float xs = baseHalfM + TramSteelOutM, zs = TramSteelZM;
            float sx = xs * ca + zs * sa, sy = -xs * sa + zs * ca;
            floorM = math.max(0f, groundDown(sx) - sy);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int edge = -1; edge <= 1; edge += 2)
                {
                    float x = side * (baseHalfM + edge * TramPierHalfM), z = -TramPierDepthM;
                    float px = x * ca + z * sa, py = -x * sa + z * ca;
                    ceilingM = math.min(ceilingM, groundDown(px) - py);
                }
            }
            return math.max(0f, math.min(floorM, ceilingM));
        }

        /// <summary>The first tram tower art letter whose saddle halves clear TramSaddlePitchLimitDeg / TramHalfBreakMaxDeg.</summary>
        public const char WideTramSaddleLetter = 'D';

        /// <summary>True when <paramref name="meshName"/> (a tower mesh's RenderPrefab name, "SkiTramTower30D Mesh", "SkiTramTower45D_LOD1
        /// Mesh") is a tram tower of art letter WideTramSaddleLetter or later. Its train limits are the tram saddle's (TrainLimits). An
        /// earlier letter, a name without one or any other mesh keeps the train limits, so this code never folds a narrow saddle past what
        /// its art clears. The meshes arrive with the import round.</summary>
        public static bool WideTramSaddle(string meshName)
        {
            const string prefix = "SkiTramTower";
            if (meshName == null || !meshName.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            int i = prefix.Length, digits = 0;
            while (i < meshName.Length && meshName[i] >= '0' && meshName[i] <= '9')
            {
                i++;
                digits++;
            }
            if (digits == 0 || i >= meshName.Length)
                return false;
            char letter = meshName[i];
            bool endOfLetter = i + 1 == meshName.Length || !char.IsLetter(meshName[i + 1]);
            return letter >= WideTramSaddleLetter && letter <= 'Z' && endOfLetter;
        }

        /// <summary>A tower's train limits, radians. A wide tram saddle (WideTramSaddle) has TramSaddlePitchLimitDeg (at most the type's
        /// steepest rope, MaxTrainPitch) and TramHalfBreakMaxDeg. Every other train has TrainPitchLimit and MaxHalfBreakDeg.</summary>
        public static void TrainLimits(float slopeMax, bool wideTramSaddle, out float maxPitch, out float maxHalfBreak)
        {
            if (wideTramSaddle)
            {
                maxPitch = math.min(MaxTrainPitch(slopeMax), math.radians(TramSaddlePitchLimitDeg));
                maxHalfBreak = math.radians(TramHalfBreakMaxDeg);
            }
            else
            {
                maxPitch = TrainPitchLimit(slopeMax);
                maxHalfBreak = math.radians(MaxHalfBreakDeg);
            }
        }

        /// <summary>The head pitch of a headed tower (DQ / GD, DECISIONS 594, spike tilted-towers-2026-11-04 option B). The whole head
        /// (gantry, deck, trains) turns about the trains' pivot line to the bisector of its ropes. This is the mean of the front and back
        /// lane-end pitches (inputs as HalfPitches, relative to the mast, a side without a rope takes the other's pitch), clamped to
        /// ±<paramref name="headMax"/> (HeadRelMax since DECISIONS 600: the mast leans for the rest, 594 used the type's steepest rope).
        /// The value is 0 when neither side has a rope.</summary>
        public static float HeadPitch(float front, int nFront, float back, int nBack, float headMax)
        {
            if (nFront == 0 && nBack == 0)
                return 0f;
            if (nFront == 0)
                front = back;
            else if (nBack == 0)
                back = front;
            return math.clamp(0.5f * (front + back), -headMax, headMax);
        }

        /// <summary>HalfPitches for a headed tower: the head at HeadPitch, each half at its rope's pitch clamped to the head ±
        /// MaxHalfBreakDeg (no TrainPitchLimit: the head carries the slope). Inputs and outputs are relative to the mast (the Head bone and
        /// the halves are siblings on the same pivot). Returns true when a half was clamped. Returns false and zeros when neither side has
        /// a rope.</summary>
        public static bool HeadedHalfPitches(float front, int nFront, float back, int nBack, float headMax, out float head, out float pFront,
            out float pBack)
        {
            head = pFront = pBack = 0f;
            if (nFront == 0 && nBack == 0)
                return false;
            if (nFront == 0)
                front = back;
            else if (nBack == 0)
                back = front;
            head = HeadPitch(front, 1, back, 1, headMax);
            float limit = math.radians(MaxHalfBreakDeg);
            pFront = math.clamp(front, head - limit, head + limit);
            pBack = math.clamp(back, head - limit, head + limit);
            return pFront != front || pBack != back;
        }

        /// <summary>TrainOverDeg for a headed tower, measured against the head: degrees a half's rope pitch lies past head ±
        /// MaxHalfBreakDeg (inputs unclamped, as HeadedHalfPitches). With the head on the bisector this is the half-break past the limit. A
        /// mean rope past <paramref name="headMax"/> adds the head's shortfall. The value is 0 when the train follows the rope or there is
        /// no rope.</summary>
        public static float HeadedTrainOverDeg(float front, int nFront, float back, int nBack, float headMax)
        {
            if (nFront == 0 && nBack == 0)
                return 0f;
            if (nFront == 0)
                front = back;
            else if (nBack == 0)
                back = front;
            float head = HeadPitch(front, 1, back, 1, headMax);
            float dev = math.max(math.abs(front - head), math.abs(back - head)) - math.radians(MaxHalfBreakDeg);
            return math.max(0f, math.degrees(dev));
        }

        /// <summary>A fitting fixed on a headed tower's head (number tile, beacon) posed with the head. <paramref name="pivot"/> is the
        /// Head bone's origin and <paramref name="frame"/> its rest rotation, both in the tower's prefab frame (ProceduralBone
        /// m_ObjectPosition / m_ObjectRotation). The head turns by TrainBoneRotation(rest, <paramref name="head"/>) in its bone frame. In
        /// the prefab frame this is frame * RotateX(-head) * inverse(frame), about the pivot.</summary>
        public static void HeadPose(float3 pivot, quaternion frame, float head, float3 localPos, quaternion localRot, out float3 pos,
            out quaternion rot)
        {
            quaternion delta = math.mul(frame, math.mul(quaternion.RotateX(-head), math.inverse(frame)));
            pos = pivot + math.mul(delta, localPos - pivot);
            rot = math.normalize(math.mul(delta, localRot));
        }

        /// <summary>The hub warns about a tower's train only past this many degrees over its limits (TrainOverDeg). Rounding and rope sag
        /// noise at a tower right at a limit never raise the row.</summary>
        public const float TrainWarnDeg = 0.5f;

        /// <summary>The steepest rope a lift type allows, radians: atan of its envelope's SlopeMax (FG 38.7, DQ 42.0, GD 50.2, tram 68.2
        /// deg). The trains stop at TrainPitchLimit.</summary>
        public static float MaxTrainPitch(float slopeMax)
        {
            return math.atan(slopeMax);
        }

        /// <summary>The steepest a train half tilts, degrees, on any lift type (owner verdict 2026-11-02: "more like 30", steeper looks
        /// absurd on an upright tower). Tilted tower bodies are a later idea. Past it the rope leaves the outer wheels.</summary>
        public const float TrainPitchLimitDeg = 30f;

        /// <summary>A lift type's train pitch limit, radians: its steepest rope (MaxTrainPitch), at most TrainPitchLimitDeg.</summary>
        public static float TrainPitchLimit(float slopeMax)
        {
            return math.min(MaxTrainPitch(slopeMax), math.radians(TrainPitchLimitDeg));
        }

        /// <summary>A train bone's local rotation for a pitch nose up along the tower's +z: its rest rotation turned about the bone's own
        /// x (nose up along +z is RotateX(-a), see PitchedRotation). The bone's origin is its rope's anchor, so the rope points stay.</summary>
        public static quaternion TrainBoneRotation(quaternion rest, float pitch)
        {
            return math.mul(rest, quaternion.RotateX(-pitch));
        }

        /// <summary>Where a point fixed on a pitched object lands: <paramref name="pivotWorld"/> is the pin the object turns about (world),
        /// <paramref name="local"/> the point in the object's frame relative to that pin.</summary>
        public static float3 AboutPin(float3 pivotWorld, quaternion rotation, float3 local)
        {
            return pivotWorld + math.mul(rotation, local);
        }
    }
}
