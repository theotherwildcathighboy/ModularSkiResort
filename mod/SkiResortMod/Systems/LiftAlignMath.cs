// Pure geometry of the lift alignment fix (docs: lift-alignment-2026-11-02): the plan bend at a tower, the rope slopes a hanging
// lane meets a node with, and the pitch of a sheave train that follows them. No Game or Entities type (Unity.Mathematics only), so
// tests/LiftBatch links it (AlignTests.cs, AL-*).
// The rope of a span is the engine's hanging lane: the chord a..d with b, c at 1/3 and 2/3 lowered by |a.xz - d.xz| * m_Hanging * 4/3
// (LaneSystem.cs:4128-4138; LiftTowerEdit.HangingCurve). Its end slopes are the chord slope -4h (leaving a) and +4h (arriving at d),
// so a tower between two spans sees a break-over of the chord change plus 8h.
// Engine notes: EngineNotes/LiftAlignMath.md

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>The sheave train a line tower needs (breakover-holddown-plan-2026-11-03, section 2.2): None = no train judged (a
    /// station end, a tram saddle, an end without a neighbor), Support (the rope presses down), HoldDown (it pulls up: the train sits
    /// over the rope), PushPull (the load reverses between load cases: wheels on both sides).</summary>
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
        /// with sheaves only by a few degrees; larger turns are angle stations.</summary>
        public const float BendWarnDeg = 5f;

        /// <summary>The plan angle, degrees in [0, 180], between the line arriving at <paramref name="node"/> from
        /// <paramref name="prev"/> and leaving it toward <paramref name="next"/> (0 = straight). y is ignored; a zero-length leg gives 0.</summary>
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

        /// <summary>THE rope angles at a tower (the one place they are computed: the train pitch, the breakover / hold-down pass of the
        /// tower planner and the tilt round all call it). <paramref name="prev"/> / <paramref name="node"/> / <paramref name="next"/> are
        /// the rope points (rope lane y at the anchors; x / z in the plan, the distance between them is the xz length), in line order.
        /// <paramref name="inAngle"/> = atan of the slope the rope ARRIVES at the node with (chord + 4h), <paramref name="outAngle"/> =
        /// atan of the slope it LEAVES with toward next (chord - 4h), radians, both measured along prev -> next (up = +).</summary>
        public static void RopeAngles(float3 prev, float3 node, float3 next, float hanging, out float inAngle, out float outAngle)
        {
            inAngle = math.atan(ArriveSlope(prev, node, hanging));
            outAngle = math.atan(LeaveSlope(node, next, hanging));
        }

        /// <summary>The rope's deflection at the tower, degrees: in - out (RopeAngles). &gt; 0 convex (a crest: the rope presses DOWN on
        /// support wheels), &lt; 0 concave (a dip: it pulls UP, a hold-down train). The same either way along the line. On level ground
        /// with equal neighbors it is the engine's sag kink 2 atan(4h) (FG 9.15, DQ / GD 11.42, tram 18.18 deg); on a uniform slope g it
        /// is atan(g + 4h) - atan(g - 4h), a little less.</summary>
        public static float DeflectionDeg(float3 prev, float3 node, float3 next, float hanging)
        {
            RopeAngles(prev, node, next, hanging, out float a, out float b);
            return math.degrees(a - b);
        }

        /// <summary>Train pitch, radians, nose UP toward <paramref name="next"/>, at a line node <paramref name="node"/> whose rope points
        /// (the axle or crown points of the neighbors) are <paramref name="prev"/> and <paramref name="next"/>: the bisector of the
        /// arriving and leaving rope angles (RopeAngles), clamped to ±<paramref name="maxPitch"/>. A straight uniform slope gives exactly
        /// that slope.</summary>
        public static float TrainPitch(float3 prev, float3 node, float3 next, float hanging, float maxPitch)
        {
            RopeAngles(prev, node, next, hanging, out float inAngle, out float outAngle);
            return math.clamp(0.5f * (inAngle + outAngle), -maxPitch, maxPitch);
        }

        // ── Rope pitch at a tower (DECISIONS 600; docs research/steep-angles-real-lifts-2026-11-04.md sections 2, 5.1) ───────────

        /// <summary>The rope's pitch through a tower, degrees, up toward next = +: the mean of the two adjacent chords' angles (prev ->
        /// node and node -> next, (along, rope y) points). Unlike the bend (DeflectionDeg) a pair or a taller tower barely changes it:
        /// it follows the ground. A zero-length chord takes the other's angle.</summary>
        public static float ChordPitchDeg(float sP, float rP, float s, float r, float sN, float rN)
        {
            float dl = s - sP, dr = sN - s;
            float a = math.abs(dl) < 1e-4f ? float.NaN : math.atan((r - rP) / dl);
            float b = math.abs(dr) < 1e-4f ? float.NaN : math.atan((rN - r) / dr);
            if (math.isnan(a)) a = math.isnan(b) ? 0f : b;
            if (math.isnan(b)) b = a;
            return math.degrees(0.5f * (a + b));
        }

        /// <summary>The hard pitch cap per lift type, degrees: the steepest real line maxima (research steep-angles section 5.1): FG 38
        /// (Pic des Tentes 72 %, Tete de Balme 77 %), DQ 40 and GD 40 (Lai Alv 79 %, Grand Massif 81 %), tram 55 (Loen 54, Schilthorn
        /// 57.9). Past it the planner repairs (shift, member, pair) and the lift tool warns "too steep". +infinity for an unknown type.</summary>
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
        /// tram 45. +infinity for an unknown type.</summary>
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

        // ── Mast lean (DECISIONS 600; spike tilted-towers-2026-11-04 option A; research steep-angles section 5.3) ─────────────────

        /// <summary>The head (DQ / GD Head bone) and the FG trains' mean pitch relative to the mast, at most, degrees: a visual choice
        /// (research section 5.3: "do not exceed 25 deg head tilt relative to the mast"; 20 keeps the 594 trunnion inside its swept wedge).</summary>
        public const float HeadRelMaxDeg = 20f;

        /// <summary>HeadRelMaxDeg in radians: the headed towers' head clamp (HeadPitch / HeadedHalfPitches headMax), relative to the mast.</summary>
        public static float HeadRelMax => math.radians(HeadRelMaxDeg);

        /// <summary>A tower's mast pitch, radians nose up along its +z, read from its rotation (0 for a plumb, yaw-only tower; LeanRotation's
        /// signed lean for a leaned one). TrainPitchSystem subtracts it from the absolute lane-end pitches: the bones are local to the mast.</summary>
        public static float MastPitch(quaternion rotation)
        {
            float3 f = math.mul(rotation, new float3(0f, 0f, 1f));
            return math.atan2(f.y, math.length(f.xz));
        }

        /// <summary>The steepest mast lean, degrees: DQ / GD 20 (sourced range 10-18 deg measured, +-40 RM), FG 25 (FgMaxLeanDeg), tram
        /// 25 (Sandia 18, Loen "strongly overhanging"). A visual choice.</summary>
        public const float MaxLeanDeg = 20f, TramMaxLeanDeg = 25f;

        /// <summary>The FG's own lean rule (DECISIONS 600 a, call d; critique SHOULD-3): the FG has no head, so its mast sets the
        /// crossarm to the rope grade. It leans from FgLeanFreeDeg 12 deg of pitch (the call said about 10: at 10 the 1-2 deg leans of
        /// 11-12 deg pitches left a 2 cm floor shortfall on two FG20s LeanPass cannot raise, LP-10, and a needless 4 m raise beside a
        /// station, LP-30), up to FgMaxLeanDeg 25: at the 38 cap the trains' mean sits 13 off the mast either way and the halves (mean +
        /// the Break twin's 16 deg half-break) at most 28, under the FG art's 30 (PC-6).</summary>
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

        /// <summary>How the lean follows the pitch (THE lean rule; the owner judges the look from previews, so it is one switch).
        /// HeadFirst (default): lean = clamp(|pitch| - LeanFreeDeg, 0, max lean): a mast leans only where the head alone cannot
        /// follow the rope. Proportional: lean = |pitch| x LeanShare, at most the max lean (every pitched tower leans a little).</summary>
        public enum LeanRule : byte
        {
            HeadFirst,
            Proportional,
        }

        /// <summary>The lean rule in force (LeanDeg).</summary>
        public const LeanRule Rule = LeanRule.HeadFirst;

        /// <summary>LeanRule.Proportional's share of the pitch.</summary>
        public const float LeanShare = 0.5f;

        /// <summary>The mast lean, degrees (&gt;= 0, the top leans downhill: toward the lower of the two chords' ends), of a
        /// <paramref name="typeId"/> tower whose rope pitch is <paramref name="pitchDeg"/> (ChordPitchDeg, either sign).</summary>
        public static float LeanDeg(string typeId, float pitchDeg)
        {
            float p = math.abs(pitchDeg), max = MaxLeanDegOf(typeId);
            if (!math.isfinite(p))
                return 0f;
            return Rule == LeanRule.Proportional ? math.min(p * LeanShare, max) : math.clamp(p - LeanFreeDeg(typeId), 0f, max);
        }

        /// <summary>The rope anchor of a leaned tower in the line's (along, y) plane: the mast turns about its foot (<paramref
        /// name="along"/>, <paramref name="footY"/>), so the anchor <paramref name="ropeY"/> - footY above it moves downhill (against the
        /// sign of <paramref name="pitchDeg"/>, up toward +along = +) by that height x sin(lean) and drops by height x (1 - cos(lean))
        /// (spike tilted-towers-2026-11-04 section 1.2: the head shift yA sin(lean)).</summary>
        public static void LeanedAnchor(float along, float footY, float ropeY, float pitchDeg, float leanDeg, out float anchorAlong,
            out float anchorY)
        {
            float h = ropeY - footY, a = math.radians(leanDeg);
            float sign = pitchDeg > 0f ? 1f : pitchDeg < 0f ? -1f : 0f;
            anchorAlong = along - sign * h * math.sin(a);
            anchorY = footY + h * math.cos(a);
        }

        /// <summary>The leaned tower's rotation: the node yaw <paramref name="yaw"/> pitched about its local x so the mast top leans by
        /// <paramref name="leanDeg"/> downhill, where <paramref name="pitchAlongZ"/> is the rope's pitch (degrees, nose up along the
        /// tower's +z). A rope rising toward +z leans the top toward -z: the mast is turned nose up along +z by the lean
        /// (PitchedRotation's convention, RotateX(-a) = nose up along +z).</summary>
        public static quaternion LeanRotation(quaternion yaw, float pitchAlongZ, float leanDeg)
        {
            float a = math.radians(leanDeg) * (pitchAlongZ > 0f ? 1f : pitchAlongZ < 0f ? -1f : 0f);
            return math.mul(yaw, quaternion.RotateX(-a));
        }

        /// <summary>The lean, radians nose up along the tower's +z, that LeanRotation applied (the signed mast pitch the bones are
        /// relative to).</summary>
        public static float SignedLean(float pitchAlongZ, float leanDeg) =>
            math.radians(leanDeg) * (pitchAlongZ > 0f ? 1f : pitchAlongZ < 0f ? -1f : 0f);

        // ── Breakover / hold-down (docs/research/breakover-holddown-plan-2026-11-03.md, section 2.2) ─────────────────────────────

        /// <summary>The largest rope deflection one roller may take, degrees: 0.07 rad (D.M. 8 March 1999, fixed-grip and detachable
        /// chairlifts, 3.18.3: support and hold-down rollers alike, worst steady load case). A train's cap is its wheels x this.</summary>
        public const float PerWheelMaxDeg = 4.0f;

        /// <summary>The least deflection, degrees, that keeps a roller pressed: derived from 500 N per roller (decree 16 November 2012,
        /// 14.6.4) at an assumed 120-300 kN haul-rope tension (4 x 500 / 120 kN = 0.95, 8 x 500 / 300 kN = 0.76 deg); the tension is
        /// unsourced for our types.</summary>
        public const float MinPressDeg = 1.0f;

        /// <summary>Sag scale of the TIGHT load case (+40 % rope tension, 14.6.4: a support tower in a dip must keep the rope): sag is
        /// load over tension, so h / 1.4.</summary>
        public const float TightSagScale = 1f / 1.4f;

        /// <summary>Sag scale of the HEAVY load case (-20 % tension and +25 % payload, 14.6.4: a hold-down tower must keep the rope):
        /// 1.25 / 0.8 = 1.5625 h (conservative: the whole weight scaled, not only the payload).</summary>
        public const float HeavySagScale = 1.5625f;

        /// <summary>A tower keeps its kind while that kind is still valid within this many degrees (the fit's re-classification after a
        /// terrain nudge or a re-fit never flips a head back and forth).</summary>
        public const float KindHystDeg = 0.25f;

        /// <summary>Tram line saddles: the bare track rope must break over a line saddle by at least 0.02 rad (decree 16 November 2012,
        /// 14.4.4); under it the saddle may lift. Logged only (trams are scoped out of pairs and hold-down).</summary>
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

        /// <summary>A tower's sheave-train kind from the two load-case deflections (degrees): Support when the rope still presses in the
        /// tight case (tight &gt;= +MinPressDeg); HoldDown when it still lifts in the heavy case (heavy &lt;= -MinPressDeg); else PushPull
        /// (the load reverses between the cases: wheels on both sides). The deflection grows with the sag, so tight &lt; heavy and the
        /// first two never both hold. <paramref name="previous"/> (None = unknown): the tower keeps that kind while it is still valid
        /// within KindHystDeg.</summary>
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

        /// <summary>Pitch, radians, nose up toward the line, of a station's line-side train: the angle of the first span's rope where it
        /// meets the station anchor <paramref name="station"/>, coming from (or going to) the first line rope point <paramref name="line"/>.</summary>
        public static float StationTrainPitch(float3 station, float3 line, float hanging, float maxPitch)
        {
            return math.clamp(math.atan(LeaveSlope(station, line, hanging)), -maxPitch, maxPitch);
        }

        /// <summary>The rotation about local x that pitches a node-yawed object nose up by <paramref name="pitchTowardNext"/> toward
        /// <paramref name="next"/>: the sign flips when the object's +z faces <paramref name="prev"/>. Unity's RotateX(+a) turns +z toward
        /// -y (nose down), so nose up along +z is RotateX(-a).</summary>
        public static quaternion PitchedRotation(quaternion yaw, float3 prev, float3 next, float pitchTowardNext)
        {
            float3 forward = math.mul(yaw, new float3(0f, 0f, 1f));
            float sign = math.dot(forward.xz, next.xz - prev.xz) >= 0f ? 1f : -1f;
            return math.mul(yaw, quaternion.RotateX(-sign * pitchTowardNext));
        }

        /// <summary>Pitch, radians, nose up along a tower's +z (<paramref name="forward"/>), that one rope lane end shows: the lane leaves
        /// its anchor <paramref name="end"/> toward its next control point <paramref name="inner"/> (b for end a, c for end d; the end
        /// tangent's direction). A lane on the +z side climbing away gives +, one on the -z side climbing away gives - (the rope comes down
        /// toward +z there). The mean over a tower's lane ends is TrainPitch's bisector on the engine's own curves.</summary>
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
        /// the tower's +z (<paramref name="forward"/>; the TrainFront bones), -1 toward -z (TrainBack). The same side test as LaneEndPitch.</summary>
        public static int LaneEndSide(float3 end, float3 inner, float3 forward)
        {
            return math.dot((inner - end).xz, forward.xz) >= 0f ? 1 : -1;
        }

        /// <summary>The largest half-break, degrees, the articulated trains take: half the angle between the front and back halves (the
        /// rope's break at the tower over 2). The tower art clears it at any pitch (G6 joint sweep: inner wheels 0.06 m further out).</summary>
        public const float MaxHalfBreakDeg = 20f;

        /// <summary>The two half-train pitches of an articulated tower (owner verdict 2026-11-02, option C: halves hinged on the main pin,
        /// the rope corners there): <paramref name="front"/> / <paramref name="back"/> are the mean lane-end pitches (LaneEndPitch, nose up
        /// along +z) of the ropes leaving toward +z / -z, <paramref name="nFront"/> / <paramref name="nBack"/> how many. A side without a
        /// rope takes the other's pitch (a straight train). Each half clamps to ±<paramref name="maxPitch"/>, the half-break to
        /// MaxHalfBreakDeg about their mean, then each half again (so both limits hold). True when the half-break was clamped; false and
        /// zeros when neither side has a rope.</summary>
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

        /// <summary>How far, degrees, the rope at a tower asks more of its train than HalfPitches gives: the larger of a half's pitch past
        /// <paramref name="maxPitch"/> and the half-break past MaxHalfBreakDeg, both from the unclamped pitches (inputs as HalfPitches;
        /// a side without a rope takes the other's pitch). 0 when the train follows the rope, or when neither side has a rope. The hub's
        /// LiftTrainLimit row (TrainWarnDeg).</summary>
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

        /// <summary>Tram saddle halves of art letter D or later (DECISIONS 597; docs research/art/tram-saddle-2026-11/build.md): each
        /// half follows its rope to this many degrees. A saddle half is the end zone of a real tram saddle, whose shoe leaves the rope
        /// tangentially however steep the span (34 Pa. Code 7.428(e)(2): the rope never reaches the groove's end), so the tram is not
        /// held to TrainPitchLimitDeg's upright-train look. 60 is the static head's rope reach (the tram's steepest rope is 68.2 deg;
        /// past 60 the rope meets the lifting lugs whatever the halves do). The art clears it (tram_saddle.py G6 joint grid 60 / 30).</summary>
        public const float TramSaddlePitchLimitDeg = 60f;

        /// <summary>The half-break the letter D tram saddle halves clear (V-notch faces 32 deg off the vertical, haul rollers 0.10 m
        /// further out): 30 deg, against MaxHalfBreakDeg for every other train.</summary>
        public const float TramHalfBreakMaxDeg = 30f;

        /// <summary>The planner's tram saddle cap, degrees per half (DECISIONS 600 a, call b; critique SHOULD-1): half the rope's bend at a
        /// tram tower (DeflectionDeg / 2, the drawn rope) is held to this for the LOOK, not for the art's reach (D clears
        /// TramHalfBreakMaxDeg 30): a 60 deg V on one saddle reads as a kink, a 15-20 deg half reads as a shoe. Past it the angle pass
        /// repairs the tower like a chair's (shift / member, a breakover pair, a longer span); still past it: OVER CAP, PLAN_SADDLE /
        /// TOWER_SADDLE and a red tower. The number: LiftBatch PC-7 (TS-1 coverage at 15 / 17.5 / 20).</summary>
        public const float TramBendHalfCapDeg = 20f;

        // ── Tram pedestals (DECISIONS 600 a, call c; critique SHOULD-2; art tower_lean.py foundation table) ─────────────────────
        // The tram tower stands on four concrete columns at its base corners (tram_tower.build_footings: a 1.5 m pedestal over a 12 m
        // pier, 1.4 m square, at +-B/2; B = 10 / 11 / 12 m for the 30 / 45 / 60 m tower). A lean turns them about the foot (the
        // Transform origin), so on a level crest the downhill legs dig in and the uphill piers stand proud. The planner floats the
        // tower (node y = ground + E + FloatM; the engine stands the member's pivot FloatM above the ground): at least the dig-in of
        // the downhill steel (the FLOOR), at most what the piers still cover (the CEILING). Between them it stands on four unequal
        // pedestals; the lean is never limited by the foot slope.

        /// <summary>Tram pier: depth below the origin, half its width (tram_tower.PIER_TR 12.0, PW_TR 1.4).</summary>
        public const float TramPierDepthM = 12f, TramPierHalfM = 0.7f;

        /// <summary>The downhill steel's lowest point in the tower frame: TramSteelOutM past the column center (B/2) and TramSteelZM above
        /// the origin (the leg gussets over the pedestal top). Fitted to the art's foundation check (steel_buried_m on a level crest:
        /// 0.92 / 1.13 / 1.34 m at lean 25, 0.44 / 0.61 / 0.78 at lean 20; reproduced within 3 mm).</summary>
        public const float TramSteelOutM = 0.325f, TramSteelZM = 1.466f;

        /// <summary>Half the tram tower's base width B, meters, by its top (tram_tower.make_spec: B 10 / 11 / 12 for the 30 / 45 / 60 m
        /// tower; linear between).</summary>
        public static float TramBaseHalfM(float topM) => 0.5f * (10f + (topM - 30f) / 15f);

        /// <summary>A tram tower's pedestal float, meters (the origin over the ground at the foot): max(0, min(FLOOR, CEILING)).
        /// <paramref name="groundDown"/>(x) is the terrain relative to the ground at the foot, x meters along the line DOWNHILL of it (the
        /// side the mast leans to; negative x = uphill). FLOOR = the downhill steel's depth under the terrain at float 0; CEILING = the
        /// least depth of the four pier-bottom edges (both piers' inner and outer edge, uphill and downhill) under the terrain at float 0
        /// (the piers never lift off). Lean 0: 0 (the plumb tower stands on its pivot as before).</summary>
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
        /// Mesh") is a tram tower of art letter WideTramSaddleLetter or later: its train limits are the tram saddle's (TrainLimits). An
        /// earlier letter, a name without one or any other mesh keeps the train limits, so this code never folds a narrow saddle past
        /// what its art clears (the meshes arrive with the import round).</summary>
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

        /// <summary>A tower's train limits, radians: a wide tram saddle (WideTramSaddle) TramSaddlePitchLimitDeg (at most the type's
        /// steepest rope, MaxTrainPitch) and TramHalfBreakMaxDeg; every other train TrainPitchLimit and MaxHalfBreakDeg.</summary>
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

        /// <summary>The head pitch of a headed tower (DQ / GD, DECISIONS 594; spike tilted-towers-2026-11-04 option B): the whole head
        /// (gantry, deck, trains) turns about the trains' pivot line to the bisector of its ropes, the mean of the front and back lane-end
        /// pitches (inputs as HalfPitches, relative to the mast; a side without a rope takes the other's pitch), clamped to
        /// ±<paramref name="headMax"/> (HeadRelMax since DECISIONS 600: the mast leans for the rest; 594 used the type's steepest rope).
        /// 0 when neither side has a rope.</summary>
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
        /// the halves are siblings on the same pivot). True when a half was clamped; false and zeros when neither side has a rope.</summary>
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
        /// MaxHalfBreakDeg (inputs unclamped, as HeadedHalfPitches). With the head on the bisector this is the half-break past the limit;
        /// a mean rope past <paramref name="headMax"/> adds the head's shortfall. 0 when the train follows the rope or there is no rope.</summary>
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

        /// <summary>A fitting fixed on a headed tower's head (number tile, beacon) posed with the head: <paramref name="pivot"/> is the Head
        /// bone's origin and <paramref name="frame"/> its rest rotation, both in the tower's prefab frame (ProceduralBone m_ObjectPosition /
        /// m_ObjectRotation); the head turns by TrainBoneRotation(rest, <paramref name="head"/>) in its bone frame, which is
        /// frame * RotateX(-head) * inverse(frame) in the prefab frame, about the pivot.</summary>
        public static void HeadPose(float3 pivot, quaternion frame, float head, float3 localPos, quaternion localRot, out float3 pos,
            out quaternion rot)
        {
            quaternion delta = math.mul(frame, math.mul(quaternion.RotateX(-head), math.inverse(frame)));
            pos = pivot + math.mul(delta, localPos - pivot);
            rot = math.normalize(math.mul(delta, localRot));
        }

        /// <summary>The hub warns about a tower's train only past this many degrees over its limits (TrainOverDeg): rounding and rope
        /// sag noise at a tower right at a limit never raise the row.</summary>
        public const float TrainWarnDeg = 0.5f;

        /// <summary>The steepest rope a lift type allows, radians: atan of its envelope's SlopeMax (FG 38.7, DQ 42.0, GD 50.2, tram 68.2
        /// deg); the trains stop at TrainPitchLimit.</summary>
        public static float MaxTrainPitch(float slopeMax)
        {
            return math.atan(slopeMax);
        }

        /// <summary>The steepest a train half tilts, degrees, on any lift type (owner verdict 2026-11-02: "more like 30", steeper looks
        /// absurd on an upright tower; tilted tower bodies are a later idea). Past it the rope leaves the outer wheels.</summary>
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
