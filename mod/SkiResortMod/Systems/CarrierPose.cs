// The two-object carrier pose (the carrier grip round, docs plan carrier-pivot-swing-plan-2026-11-03 sections 2-4): a lift carrier
// is a GRIP (the rope side: jaws or carriage plus the hanger head that carries the pivot pin) and a BODY (hanger + seat or cabin below
// the pin). The grip pitches with the rope about the rope point; the body hangs from the pin, plumb, and swings in the wind.
// Swing convention (the art critique, 2026-11-03: a tram hangs from one lateral pin, no cardan, and a chair's clevis cannot roll
// either): the LATERAL swing is ONE world rotation Q_roll of the whole assembly, grip and body together, about the rope point and the
// ROPE TANGENT (the pitched travel axis, so the jaw never leaves the rope); the FORE-AFT swing turns the body alone about the pin.
// Grip rotation = Q_roll x yaw x pitch (= yaw x pitch x roll), body rotation = Q_roll x yaw x foreAft; pin = rope + R_grip x
// (GripPin - GripRope); body = pin - R_body x BodyPin (CP-18). On a pitched span the roll about the inclined axis also yaws the body
// a little (about roll x sin(pitch)) and leans it by about roll x cos(pitch): what a carrier swinging about its rope does. The roll's
// lever is the rope point to the body's base, longer than the old pin roll's, so a chair / gondola scales its roll gain and clamp by
// the lever ratio (Limits); the tram's long hanger damps it to half the fore-aft amplitude and 2 deg.
// Pure (Unity.Mathematics only): MoveChairsJob (Burst), ChairPinSystem's managed paths, TramShuttleSystem, tests/LiftBatch
// (CarrierPoseTests, CP-1 to CP-18) and tests/MoverBench (B-5) share it. CarrierWind (the per-loop wind snapshots) lives here too.
// Frames: Unity's left-handed world, y up; a carrier's local frame is X right, Y up, Z forward. The YAW is the caller's: the rigid
// pose's NetUtils.GetNodeRotation(travel tangent, fallback) (Game.Net/NetUtils.cs:316-324 in 1.6.2f1), unchanged, so the facing
// conventions (the loop's travel yaw, the static pin's flip, the tram's -t_up) stay where they are.
// Pivots (SkiCatalog, per carrier type, from each mesh's base-pivot origin = its lowest vertex): GripRope = the rope point in the
// grip mesh, GripPin = the pin center in the grip mesh, BodyPin = the pin center in the body mesh. Rigid identity (CatalogTests
// CT-17): BodyPin + GripRope - GripPin = (0, drop, 0), drop = HangM - GripTopAboveRopeM, so zero pitch and zero swing put the
// body exactly where today's one-piece carrier stands (rope point - drop) and the grip where the old mesh's grip was (CP-1).
// Swing (closed form, no integrated state, plan section 3): lean = the wind's cross / along components in the carrier frame
// (fore-aft at ForeAftShare), clamped; sway = an amplitude that grows with |w| times sin / cos of an argument that is a pure
// function of the loop distance s (SwayArg: omega / v x s + a per-entity phase), so a paused game freezes it, a hold freezes it
// and the hold's Rebase resumes it without a jump (s is continuous there), and neighbors differ by omega x spacing / v. Both are
// zero on guided segments (bull wheel arcs, the synthesized station runs) and ramp in linearly: the lean over the first RampM of
// a free run, the sway over the next RampM (CP-9: no step over 0.5 deg per frame at 30 fps and 3x speed).
// The wind per loop: two snapshots and the clock of the change, smoothstep over WindBlendS (BlendWind; the caller samples
// WindSystem on the main thread). Every constant below is a PLACEHOLDER until in-game photos tune it.
// Engine notes: EngineNotes/ChairPinSystem.Mover.md ("Grips and swing")

using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    // CarrierPivots (the three offsets) lives in SkiCatalog.cs: the catalog is linked into six test hosts, this file into two.
    internal static class CarrierPose
    {
        // ---- the swing constants (PLACEHOLDERS: tuned from in-game photos; step 0 measured SampleWind |w| ~ 0.31, units UNVERIFIED) ----
        /// <summary>Lean (rad) per |w|^2 of full cross wind: drag grows with the square of the wind (0.3 x 0.31^2 = 1.65 deg).</summary>
        public const float LeanPerWind2 = 0.3f;
        /// <summary>The fore-aft share of the lean and the sway (the plan: fore-aft at 1/3).</summary>
        public const float ForeAftShare = 1f / 3f;
        /// <summary>The lateral reference: 8 deg of body roll about the PIN (the first convention). A chair / gondola's roll about the
        /// rope point is clamped so its body's base moves no farther than this pin roll moved it (Limits).</summary>
        public const float MaxLateral = 0.13962634f;
        /// <summary>The tram's roll clamp: 2 deg (a 10 m hanger: 0.35 m at the cabin floor).</summary>
        public const float TramMaxRoll = 0.034906585f;
        /// <summary>The tram's lateral gain as a share of its fore-aft gain (the long hanger damps the roll: about half).</summary>
        public const float TramRollShare = 0.5f;
        /// <summary>Fore-aft clamp: 3 deg (about the lateral axis).</summary>
        public const float MaxForeAft = 0.05235988f;
        /// <summary>Sway amplitude (rad) per |w| (0.05 x 0.31 = 0.9 deg), capped by MaxSway.</summary>
        public const float SwayPerWind = 0.05f;
        /// <summary>Sway cap: 1 deg. Bounds the sway's own rate (amplitude x omega x 0.1 s of clock per frame at 30 fps, 3x).</summary>
        public const float MaxSway = 0.017453292f;
        /// <summary>Gravity for the pendulum frequency omega = sqrt(g / L).</summary>
        public const float Gravity = 9.81f;
        /// <summary>The pendulum length L as a share of the pin-to-body-bottom length (the center of mass sits about there).</summary>
        public const float PendulumShare = 0.7f;
        /// <summary>A wind change blends in over this many seconds of mover clock (smoothstep).</summary>
        public const double WindBlendS = 4.0;
        /// <summary>The ramp from a guided segment into a free run: at least this many meters ...</summary>
        public const float RampMinM = 10f;
        /// <summary>... or this many seconds of travel at the loop speed, whichever is longer (the tram's 10 m/s: 20 m).</summary>
        public const float RampSeconds = 2f;
        /// <summary>A free run with no guided segment anywhere (cannot happen on a built loop): no ramp.</summary>
        public const float NoGuidedM = 1e9f;

        /// <summary>The pin's pendulum frequency (rad/s) for a body whose pin is <paramref name="bodyPinY"/> above its bottom.</summary>
        public static float Omega(float bodyPinY)
        {
            return math.sqrt(Gravity / math.max(PendulumShare * bodyPinY, 0.1f));
        }

        /// <summary>The ramp length for a loop speed (m/s).</summary>
        public static float RampM(float speedMps)
        {
            return math.max(RampMinM, RampSeconds * speedMps);
        }

        /// <summary>The rope pitch (rad, + = forward end up) of <paramref name="travelTangent"/> seen along the yaw's forward
        /// (the tangent is flipped when the carrier faces against it, as the static pin's facing flip does).</summary>
        public static float PitchOf(quaternion yaw, float3 travelTangent)
        {
            float3 forward = math.rotate(yaw, new float3(0f, 0f, 1f));
            float3 d = travelTangent;
            if (d.x * forward.x + d.z * forward.z < 0f)
                d = -d;
            float h = math.sqrt(d.x * d.x + d.z * d.z);
            if (!(h > 1e-6f) && math.abs(d.y) < 1e-6f)
                return 0f;
            return math.atan2(d.y, h);
        }

        /// <summary>The roll (lateral swing) in a carrier's own frame: about its travel axis (Z; + = bottom to the right). Exactly
        /// identity at 0.</summary>
        public static quaternion RollRotation(float roll)
        {
            return roll == 0f ? quaternion.identity : quaternion.RotateZ(roll);
        }

        /// <summary>The body's swing rotation in the yaw frame under the grip's pitch rotation <paramref name="pitch"/>: the roll about
        /// the pitched travel axis (Q_roll in the yaw frame = pitch x roll x pitch^-1), then the fore-aft turn about the pin's lateral axis
        /// (X; + = bottom forward). Exactly identity at zero swing, exactly the fore-aft turn at zero roll.</summary>
        public static quaternion BodySwing(float2 swing, quaternion pitch)
        {
            quaternion foreAft = swing.y == 0f ? quaternion.identity : quaternion.RotateX(-swing.y);
            if (swing.x == 0f)
                return foreAft;
            return math.mul(math.mul(math.mul(pitch, RollRotation(swing.x)), math.conjugate(pitch)), foreAft);
        }

        /// <summary>The body's swing rotation in the yaw frame on a level rope (tests: the swing's own angle).</summary>
        public static quaternion SwingRotation(float2 swing)
        {
            return BodySwing(swing, quaternion.identity);
        }

        /// <summary>The grip and body pose. <paramref name="rope"/> = the sampled rope point, <paramref name="yaw"/> = the rigid
        /// pose's yaw, <paramref name="pitch"/> false = the grip keeps the yaw only (the Options toggle off), <paramref name="swing"/>
        /// = (roll, fore-aft) rad. One world rotation Q_roll (the roll about the rope tangent, through the rope point) turns the whole
        /// assembly: grip rotation = Q_roll x yaw x pitch = yaw x pitch x roll (the jaw stays along the rope, CP-18), body rotation =
        /// Q_roll x yaw x foreAft (BodySwing; plumb under pitch at zero swing). The fore-aft turns the body alone about the pin. Grip
        /// position = rope - R_grip x GripRope; pin = rope + R_grip x (GripPin - GripRope); body position = pin - R_body x BodyPin. The
        /// offsets rotate in the yaw frame first, so zero pitch and swing reproduce the rigid pose to the float (math.rotate by identity
        /// returns its vector unchanged, math.mul of identities is the identity), and zero roll reproduces the unrolled pose exactly.</summary>
        public static void Pose(float3 rope, float3 travelTangent, quaternion yaw, in CarrierPivots pivots, bool pitch, float2 swing,
                                out float3 gripPosition, out quaternion gripRotation, out float3 bodyPosition, out quaternion bodyRotation)
        {
            quaternion p = pitch ? quaternion.RotateX(-PitchOf(yaw, travelTangent)) : quaternion.identity;
            quaternion g = math.mul(p, RollRotation(swing.x));
            quaternion b = BodySwing(swing, p);
            float3 gripLocal = -math.rotate(g, pivots.GripRope);
            float3 pinLocal = math.rotate(g, pivots.GripPin - pivots.GripRope);
            float3 bodyLocal = pinLocal - math.rotate(b, pivots.BodyPin);
            gripRotation = math.mul(yaw, g);
            bodyRotation = math.mul(yaw, b);
            gripPosition = rope + math.rotate(yaw, gripLocal);
            bodyPosition = rope + math.rotate(yaw, bodyLocal);
        }

        /// <summary>A carrier type's lateral limits. Tram (<paramref name="shuttle"/>): TramMaxRoll, gain TramRollShare x ForeAftShare
        /// (half the fore-aft amplitude). Chairs / gondola: the lever ratio k = |BodyPin.xy| / |RigidOffset.xy| (the old pin roll's
        /// lever over the rope-point roll's, both in the roll's plane on a level rope: a roll about Z leaves z alone), at most 1; gain k
        /// (the same base excursion per unit wind) and clamp 2 asin(k sin(MaxLateral / 2)) (the same base excursion, the chord, at the
        /// clamp: FG 7.6, DQ 7.4, GD 7.2 deg on the build's pivots; CP-18). On a pitched span the inclined roll axis shortens the
        /// base's lever by cos(pitch), so the excursion only shrinks.</summary>
        public static SwingLimits Limits(in CarrierPivots pivots, bool shuttle)
        {
            if (shuttle)
                return new SwingLimits(TramMaxRoll, TramRollShare * ForeAftShare);
            float lever = math.length(pivots.RigidOffset.xy);
            float k = lever > 1e-3f ? math.saturate(math.length(pivots.BodyPin.xy) / lever) : 1f;
            return new SwingLimits(2f * math.asin(k * math.sin(0.5f * MaxLateral)), k);
        }

        /// <summary>The pin center in world space (tests, diagnostics).</summary>
        public static float3 PinWorld(float3 rope, quaternion gripRotation, in CarrierPivots pivots)
        {
            return rope + math.rotate(gripRotation, pivots.GripPin - pivots.GripRope);
        }
        /// <summary>The grip assembled on a body that stands where it stands (no pitch of its own): the tram's parked carriage and a
        /// static carrier the pin pass leaves at vanilla's placement (no cable lane, lane too far). Grip position = body position +
        /// R_body x (BodyPin - GripPin), the body's rotation; the two pins coincide (CP-16).</summary>
        public static void GripOnBody(float3 bodyPosition, quaternion bodyRotation, in CarrierPivots pivots,
                                      out float3 gripPosition, out quaternion gripRotation)
        {
            gripPosition = bodyPosition + math.rotate(bodyRotation, pivots.BodyPin - pivots.GripPin);
            gripRotation = bodyRotation;
        }

        /// <summary>The distance between the grip's pin and the body's pin (0 = the joint holds; CP-15, CP-16).</summary>
        public static float PinGap(float3 gripPosition, quaternion gripRotation, float3 bodyPosition, quaternion bodyRotation,
                                   in CarrierPivots pivots)
        {
            return math.distance(gripPosition + math.rotate(gripRotation, pivots.GripPin),
                                 bodyPosition + math.rotate(bodyRotation, pivots.BodyPin));
        }

        /// <summary>The ramp factors (lean, sway) at <paramref name="distanceM"/> from the nearest guided segment: the lean ramps
        /// linearly over [0, rampM], the sway over [rampM, 2 x rampM] (one ramp for both fails CP-9 at the gondola's 3x speed).</summary>
        public static float2 Ramp(float distanceM, float rampM)
        {
            float r = math.max(rampM, 1e-3f);
            return new float2(math.saturate(distanceM / r), math.saturate((distanceM - r) / r));
        }

        /// <summary>The distance from the nearest guided segment at <paramref name="d"/> meters into a free segment of
        /// <paramref name="length"/> m with <paramref name="runBefore"/> / <paramref name="runAfter"/> m of free run before / after
        /// it (FlatSegment.RunBefore / RunAfter). 0 on a guided segment.</summary>
        public static float DistanceToGuided(bool guided, float d, float length, float runBefore, float runAfter)
        {
            if (guided)
                return 0f;
            return math.max(0f, math.min(runBefore + d, runAfter + (length - d)));
        }

        /// <summary>The sway argument (rad, wrapped to [0, 2 pi)): omega / v x s + the per-entity phase. A pure function of the loop
        /// distance s (double: s reaches kilometers), so it inherits the mover's continuity (pause, hold, Rebase). The loop's wrap
        /// (s = 0) lies on the base turn, a guided segment where the sway is zero.</summary>
        public static float SwayArg(double s, float omega, float speedMps, float phase)
        {
            const double twoPi = 6.283185307179586;
            double arg = s * omega / math.max(speedMps, 0.1f) + phase;
            arg -= math.floor(arg / twoPi) * twoPi;
            return (float)arg;
        }

        /// <summary>A per-carrier phase in [0, 2 pi) from a stable seed (the body entity's index), so neighbors never swing in step
        /// even when omega x spacing / v lands near a multiple of 2 pi.</summary>
        public static float SeedPhase(uint seed)
        {
            uint h = math.hash(new uint2(seed, 0x2545F491u));
            return (h & 0xFFFFu) * (2f * math.PI / 65536f);
        }

        /// <summary>The swing (roll, fore-aft) in rad for a wind <paramref name="wind"/> (world x, z), the carrier's yaw, its sway
        /// argument, the ramp factors and its type's lateral limits (Limits). Cross wind = the wind's component along the carrier's right
        /// ((f.z, -f.x) for forward f).</summary>
        public static float2 Swing(float2 wind, quaternion yaw, float swayArg, float2 ramp, in SwingLimits limits)
        {
            if (ramp.x <= 0f && ramp.y <= 0f)
                return float2.zero;
            float3 f3 = math.rotate(yaw, new float3(0f, 0f, 1f));
            float2 f = math.normalizesafe(new float2(f3.x, f3.z), new float2(0f, 1f));
            float2 right = new float2(f.y, -f.x);
            float mag = math.length(wind);
            float cross = math.dot(wind, right);
            float along = math.dot(wind, f);
            // The lean is clamped BEFORE the ramp scales it: a strong wind's unclamped lean would saturate within the first meter of
            // the ramp (a step of degrees per frame, CP-9).
            float maxRoll = limits.MaxRoll;
            float leanLat = math.clamp(LeanPerWind2 * limits.RollGain * mag * cross, -maxRoll, maxRoll);
            float leanFa = math.clamp(LeanPerWind2 * ForeAftShare * mag * along, -MaxForeAft, MaxForeAft);
            float amp = math.min(SwayPerWind * mag, MaxSway);
            math.sincos(swayArg, out float sin, out float cos);
            float lat = ramp.x * leanLat + ramp.y * amp * limits.RollGain * sin;
            float fa = ramp.x * leanFa + ramp.y * amp * ForeAftShare * cos;
            return new float2(math.clamp(lat, -maxRoll, maxRoll), math.clamp(fa, -MaxForeAft, MaxForeAft));
        }

        /// <summary>The blended wind <paramref name="sinceChangeS"/> seconds of mover clock after a change from
        /// <paramref name="from"/> to <paramref name="to"/> (smoothstep over WindBlendS; continuous, flat at both ends).</summary>
        public static float2 BlendWind(float2 from, float2 to, double sinceChangeS)
        {
            if (!(sinceChangeS > 0.0))
                return from;
            if (sinceChangeS >= WindBlendS)
                return to;
            float u = (float)(sinceChangeS / WindBlendS);
            return math.lerp(from, to, u * u * (3f - 2f * u));
        }

        // ---- the grip pairing math (vanilla's EvenSpacing, Game.Objects/SubObjectSystem.cs:1742-1757 in 1.6.2f1) ----

        /// <summary>Aligned.m_SubObjectIndex = m x rowCount + row (rowCount = the line prefab's SubObject buffer length, row = the
        /// NetSubObjects row): the carrier's slot m along its edge. A carrier and its grip on the same edge share m.</summary>
        public static int AlignSlot(int alignIndex, int rowCount)
        {
            return rowCount > 0 ? alignIndex / rowCount : -1;
        }

        /// <summary>The NetSubObjects row of an Aligned index.</summary>
        public static int AlignRow(int alignIndex, int rowCount)
        {
            return rowCount > 0 ? alignIndex % rowCount : -1;
        }

        /// <summary>The pairing key's side: -1 / +1 from the row's lateral offset (the two lanes of a line), 0 on the axis.</summary>
        public static int RowSide(float rowX)
        {
            return rowX > 0.01f ? 1 : rowX < -0.01f ? -1 : 0;
        }
    }

    /// <summary>A carrier type's lateral swing limits (CarrierPose.Limits): the roll clamp (rad) and the roll gain relative to the
    /// reference lean / sway. Blittable (MoveChairsJob's JobLoop carries it).</summary>
    internal readonly struct SwingLimits
    {
        public readonly float MaxRoll;
        public readonly float RollGain;

        public SwingLimits(float maxRoll, float rollGain)
        {
            MaxRoll = maxRoll;
            RollGain = rollGain;
        }
    }

    /// <summary>One loop's (or tram's) wind for the swing: the last two snapshots of WindSystem's map at the loop and the mover clock of
    /// the change (CarrierPose.BlendWind). Held by ChairPinSystem / TramShuttleSystem per base station, so a rebuild keeps it. Pure
    /// (here, not in LiftLoopCache, so LiftBatch CP-8 / CP-17 can test it).</summary>
    internal sealed class CarrierWind
    {
        public float2 m_From, m_To;
        public double m_ChangeS;
        public bool m_Known;
        /// <summary>At was read while the wind was unknown: carriers were posed with zero wind, so the first sample must blend in.</summary>
        public bool m_ServedUnknown;

        /// <summary>The blended wind at mover clock <paramref name="timeS"/>; zero while unknown.</summary>
        public float2 At(double timeS)
        {
            if (!m_Known)
            {
                m_ServedUnknown = true;
                return float2.zero;
            }
            return CarrierPose.BlendWind(m_From, m_To, timeS - m_ChangeS);
        }

        /// <summary>A new snapshot: the blend restarts from where it is now (CP-8), only when the sample changed. The FIRST sample
        /// starts there when nothing was posed with the unknown wind yet (the poll runs before the first pose), else it blends in from
        /// the zero those poses used (CP-17: no snap when a late first sample arrives mid-span).</summary>
        public void Set(float2 sample, double timeS)
        {
            if (!m_Known)
            {
                m_From = m_ServedUnknown ? float2.zero : sample;
                m_To = sample;
                m_ChangeS = timeS;
                m_Known = true;
                return;
            }
            if (sample.Equals(m_To))
                return;
            m_From = At(timeS);
            m_To = sample;
            m_ChangeS = timeS;
        }
    }
}
