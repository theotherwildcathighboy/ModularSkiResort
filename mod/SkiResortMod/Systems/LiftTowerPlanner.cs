// Pure tower planner for a lift line. It uses no ECS and no Game types: LiftBatch tests it headless (LiftPlannerTests). It runs once per
// edit (or once per printed diag block). For this reason it is plain C#, not Burst. Arrays are reused per planner instance. It is
// deterministic: no randomness, no hash order, and the sample index breaks ties. Plan works from the end with the lower rope (PlansFromB)
// and mirrors the result back. A line therefore gives the same plan whichever end the caller passes as A (planner-direction-2026-11-04).
// Input: a terrain sampler (along-line meters from station A, lateral meters), the line length, the two stations' rope heights,
// LiftTowerPlanConstants for the lift type, and optional seed nodes. The planner keeps the seed nodes as anchors, so re-planning a plan is
// a fixed point.
// Phases: (a) anchors by exact shortest-path search, (b) even infill, (c) member elevation, (d) rope angles (breakover / hold-down pairs in
// the tool's plans only, LiftAlignMath.RopeAngles). The output follows: every judged node's deflection and train kind. Fit asks the fixed
// node-count variants for LiftTowerFitSystem and never adds a tower. Rope sag is the engine's parabola 4*d*m_Hanging*t(1-t)
// (LaneSystem.cs:4128-4134).
// Engine notes: EngineNotes/LiftTowerPlanner.md

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Ground height (world y) at <paramref name="alongM"/> meters from station A along the line and
    /// <paramref name="lateralM"/> meters to its right.</summary>
    public delegate float LiftTerrainSampler(float alongM, float lateralM);

    public readonly struct LiftTowerMember
    {
        public readonly string Name;   // the tower object prefab (placeholder member) name
        public readonly float TopM;    // ObjectGeometryData m_Size.y = mesh bounds top (the manifests' top_anchor_y)

        public LiftTowerMember(string name, float topM)
        {
            Name = name;
            TopM = topM;
        }
    }

    /// <summary>A line tower's head (breakover-holddown-plan-2026-11-03, section 3): the support member itself (Line) or one of its twins.
    /// A twin is another member of the same placeholder with its own unique top. Hold is the hold-down train over the rope. Combo is
    /// push-pull. Break is the FG 8-wheel breakover train (art v2, docs research art/breakover-twins-2026-11/v2/build.md section
    /// 6).</summary>
    public enum LiftTowerHead : byte
    {
        Line = 0,
        Hold = 1,
        Combo = 2,
        Break = 3,
    }

    /// <summary>One twin member. It holds the support member it is built on (ladder index), its head, the prefab name and its top
    /// (ObjectGeometryData m_Size.y = the manifest's top_anchor_y). Its lanes are the support member's (manifest rope_on_crown lane_y_new =
    /// member_lane_y). Therefore the rope height at a twin is RopeAboveBase(Member).</summary>
    public readonly struct LiftTowerTwin
    {
        public readonly int Member;
        public readonly LiftTowerHead Head;
        public readonly string Name;
        public readonly float TopM;

        public LiftTowerTwin(int member, LiftTowerHead head, string name, float topM)
        {
            Member = member;
            Head = head;
            Name = name;
            TopM = topM;
        }
    }

    [Flags]
    public enum LiftPlanNodeFlags
    {
        None = 0,
        Station = 1,      // the two ends (rope height given, no tower of ours)
        Shoulder = 2,     // phase (a): the ground came through the chord (floor split)
        Valley = 4,       // phase (a): the chord was above the max rope height (max-height split)
        Breakover = 8,    // phase (a): a floor split on convex ground (moved uphill of the crest when that was no worse)
        Infill = 16,      // phase (b)
        SideHill = 32,    // high side - low side > SideHillM (the engine ladder would score the uphill side, cmin)
        Seed = 64,        // given by the caller (re-plan)
        NearStation = 128, // a planner tower inside the station clearance, placed only because no clear plan exists without it
        Pair = 256,       // phase (d): one of two towers 10-40 m apart sharing a rope deflection one train could not take (not Breakover)
        OverCap = 512,    // phase (d): the rope deflection is past the train's cap and no repair was found (the reason is logged)
    }

    public sealed class LiftTowerPlanConstants
    {
        public readonly string TypeId;
        public readonly LiftTowerMember[] Ladder;        // ascending TopM
        public readonly float RopeBelowTopM;             // rope (haul-rope lane y) = base + TopM - RopeBelowTopM
        public readonly float CarrierDropM;              // rope to the carrier's lowest point (catalog HangM)
        public readonly float FloorM;                    // lowest carrier point above the ground, at least
        public readonly float MaxRopeAboveGroundM;       // 0 = no cap
        public readonly float TypicalSpanM;
        public readonly float MaxSpanM;
        public readonly float MinSpanM;
        public readonly float HalfWidthM;                // side samples (half the piece m_Width)
        public readonly float SagRatio;                  // rope lane m_Hanging: mid-span sag = span × SagRatio
        public readonly float EngineElevationCapM;       // PlaceableNet m_ElevationRange.max: informational (engine-ladder path only)
        public readonly float StepM;
        public readonly float SideHillM;
        public readonly float ConvexM;                   // breakover: second difference over ±2 samples below -ConvexM
        /// <summary>No NEW tower nearer a station end than this (meters along the line from the stub). The value is the station's own rope
        /// run (StationRunM: bullwheel or dock to the stub tip, the longest of the type's stations), at least MinSpanM. The planner keeps
        /// seeds (pins, a line's own nodes in PlanFixed) wherever they are.</summary>
        public readonly float StationClearM;
        /// <summary>The line prefab's NetGeometryPrefab m_MaxSlopeSteepness (gamedata-mirror Ski*HV: FG 0.8, DQ 0.9, GD 1.2, tram 2.5). A
        /// value of 0 means no rule. The engine refuses a Temp edge whose node-to-node |dy| / xz length reaches that value + 0.0005
        /// (SteepSlope, Game.Net/ValidationHelpers.cs:1435-1490, test :1462). A tall tower a few meters from a station is such an edge.
        /// These values are the defaults (tests, and a prefab without NetGeometryData). The tool plans with the line prefab's live value
        /// (With(maxEdgeSlope:) in LiftLineToolSystem at the pick). LiftLineToolSystem logs a MISMATCH when the two differ. The tool checks
        /// the value in LiftTowerPlanner.CheckEdgeSlope.</summary>
        public readonly float MaxEdgeSlope;

        /// <summary>Wheels per sheave train of the type's line towers, per kind (breakover-holddown-plan-2026-11-03, section 2.2). The
        /// generator's trains are lift_tower.py _fg_spec sheaves 4, DQ 8, SKIN_GD 10. The hold-down trains are FG 4 / DQ 8 / GD 8 as the
        /// ETGD head. The push-pull trains per side are FG 2S2C, DQ / GD 4S4C. A value of 0 means no sheave train (the tram's saddles): no
        /// cap, no pairs, no kinds.</summary>
        public readonly int SupportWheels, HoldWheels, ComboWheels;

        /// <summary>Wheels per side of the Break twin's train (FG: 8, manifests sheaves_per_side). A value of 0 means the type has no Break
        /// twin.</summary>
        public readonly int BreakWheels;

        /// <summary>The twins this type can place (the ones whose prefab is loaded, TwinFilter at For time). Empty means none. Every twin's
        /// Member is a ladder index.</summary>
        public readonly LiftTowerTwin[] Twins;

        /// <summary>The hard / soft rope pitch caps at a tower, degrees (LiftAlignMath.PitchCapDeg / PitchSoftDeg by TypeId, DECISIONS
        /// 600). The angle pass repairs a tower whose pitch (LiftAlignMath.ChordPitchDeg) is past the hard cap in the same way as one past
        /// its bend cap. +infinity means no pitch rule (tests: the plans as before the cap).</summary>
        public readonly float PitchCapDeg, PitchSoftDeg;

        /// <summary>The tram saddle cap, degrees per half (LiftAlignMath.TramBendHalfCapDeg for the tram, DECISIONS 600 a call b). The
        /// value is +infinity for the train lifts, whose trains have their wheel caps. The planner judges a tram tower's whole bend at
        /// twice this value (LineCapDeg). The angle pass repairs it like a chair's. A bend still past the cap is OVER CAP (PLAN_SADDLE /
        /// TOWER_SADDLE).</summary>
        public readonly float SaddleHalfCapDeg;

        /// <summary>The carrier-clearance rule is on (DECISIONS 600 c, default). The angle pass repairs a tower whose steeper rope half
        /// passes WorldHalfMaxDeg(head) in the world. Otherwise the tower is PAST CLEARANCE. Off means the plans as before 600 c
        /// (tests).</summary>
        public readonly bool WorldHalfOn;

        /// <summary>The carrier-clearance limit per head (index = (int)LiftTowerHead), degrees: LiftAlignMath.WorldHalfMaxDeg by TypeId and
        /// WorldHalfKind(head). The value is +infinity without a row or with the rule off.</summary>
        private readonly float[] m_WorldHalfMax = new float[4];

        /// <summary>The least carrier-clearance limit of any head (+infinity: none): below it the angle pass skips the load cases.</summary>
        public readonly float MinWorldHalfMaxDeg;

        /// <summary>The carrier-clearance limit of a tower with <paramref name="head"/>, degrees (+infinity = none).</summary>
        public float WorldHalfMaxDeg(LiftTowerHead head) => m_WorldHalfMax[(int)head];

        /// <summary>The limit the angle pass judges a <paramref name="kind"/> train at (it plans members, not heads). It is the limit of
        /// the head that kind takes: HoldDown takes Hold, PushPull takes Combo, and Support takes the stricter of Line and Break. Build
        /// judges the placed head.</summary>
        public float WorldHalfMaxDeg(LiftTrainKind kind) =>
            kind == LiftTrainKind.HoldDown ? m_WorldHalfMax[(int)LiftTowerHead.Hold]
            : kind == LiftTrainKind.PushPull ? m_WorldHalfMax[(int)LiftTowerHead.Combo]
            : math.min(m_WorldHalfMax[(int)LiftTowerHead.Line], m_WorldHalfMax[(int)LiftTowerHead.Break]);

        /// <summary>The carrier-clearance table's kind of a head (LiftAlignMath.WorldHalfTable): Line = "support", else the head's name.</summary>
        public static string WorldHalfKind(LiftTowerHead head) => head == LiftTowerHead.Line ? "support" : head.ToString();

        /// <summary>The head of a tower prefab by its name: a twin's (FixedGripTwins / DetachableTwins / GondolaTwins), else Line.</summary>
        public static LiftTowerHead HeadOfTwin(string prefabName)
        {
            if (prefabName != null)
                foreach (LiftTowerTwin t in AllTwins())
                    if (t.Name == prefabName)
                        return t.Head;
            return LiftTowerHead.Line;
        }

        /// <summary>Set once per game load (LiftTowerFitSystem.OnGameLoadingComplete): true for a twin prefab name the game has loaded.
        /// For(def) drops the others. A build without the twin prefabs therefore plans the support heads only. A missing twin would leave
        /// the node raised by E with the support member spawned 0.2-0.6 m above the ground. Null means every twin (tests).</summary>
        public static Predicate<string> TwinFilter;

        /// <summary>The twin of ladder member <paramref name="member"/> with <paramref name="head"/>, or -1 (Line is never a twin).</summary>
        public int TwinIndex(int member, LiftTowerHead head)
        {
            if (head == LiftTowerHead.Line || Twins == null)
                return -1;
            for (int i = 0; i < Twins.Length; i++)
                if (Twins[i].Member == member && Twins[i].Head == head)
                    return i;
            return -1;
        }

        public bool HasTwin(int member, LiftTowerHead head) => TwinIndex(member, head) >= 0;

        /// <summary>The placed member's top: the twin's for a twin head, else the ladder member's.</summary>
        public float TopOf(int member, LiftTowerHead head)
        {
            int t = TwinIndex(member, head);
            return t >= 0 ? Twins[t].TopM : Ladder[member].TopM;
        }

        /// <summary>The placed member's prefab name: the twin's for a twin head, else the ladder member's.</summary>
        public string NameOf(int member, LiftTowerHead head)
        {
            int t = TwinIndex(member, head);
            return t >= 0 ? Twins[t].Name : Ladder[member].Name;
        }

        /// <summary>Every ladder member has a Break twin: the type's support trains are judged at the Break cap.</summary>
        public bool BreakEverywhere
        {
            get
            {
                if (BreakWheels <= 0 || Twins == null)
                    return false;
                for (int m = 0; m < Ladder.Length; m++)
                    if (!HasTwin(m, LiftTowerHead.Break))
                        return false;
                return true;
            }
        }

        /// <summary>A breakover pair's towers stand at least this far apart: the type's MinSpanM (10 m). A visual choice (no source gives
        /// a pair spacing).</summary>
        public float PairMinM => MinSpanM;

        /// <summary>A breakover pair's towers stand at most this far apart. A visual choice (no source gives a pair spacing): close
        /// enough to read as a pair (liftblog photos: two towers almost touching at a crest).</summary>
        public const float PairMaxM = 40f;

        /// <summary>Towers per breakover group: 2 (pairs only, groups of 3 are not planned).</summary>
        public const int MaxGroup = 2;

        /// <summary>How far a single tower may slide (meters) when a shifted tower alone brings its rope under the cap.</summary>
        public const float AngleShiftM = 20f;

        /// <summary>The type has sheave trains (the chairs and the gondola, not the tram).</summary>
        public bool HasTrains => SupportWheels > 0;

        /// <summary>The cap, in degrees of WHOLE bend (DeflectionDeg, in - out), of the line member's own <paramref name="kind"/> train
        /// (LiftAlignMath.TrainCapDeg: wheels x 4 deg, at most the 40 deg art fold). The values are FG 16 / 16 / 8, DQ 32 / 32 / 16, GD 40
        /// / 32 / 16 (support / hold-down / push-pull). Without trains (the tram's saddle) the cap is 2 x SaddleHalfCapDeg for every
        /// kind.</summary>
        public float LineCapDeg(LiftTrainKind kind) =>
            !HasTrains ? 2f * SaddleHalfCapDeg
            : LiftAlignMath.TrainCapDeg(kind == LiftTrainKind.HoldDown ? HoldWheels : kind == LiftTrainKind.PushPull ? ComboWheels : SupportWheels);

        /// <summary>The cap the planner judges a <paramref name="kind"/> train at. It is LineCapDeg, except for a support train on a type
        /// whose every member has a Break twin. That train gets the Break train's cap (FG 8 wheels: 32). The convex tower takes the Break
        /// head instead of a pair. With the twins loaded the FG caps are 32 / 16 / 8. Without them the cap is as LineCapDeg.</summary>
        public float CapDeg(LiftTrainKind kind) =>
            kind != LiftTrainKind.HoldDown && kind != LiftTrainKind.PushPull && BreakEverywhere
                ? LiftAlignMath.TrainCapDeg(BreakWheels) : LineCapDeg(kind);

        /// <summary>The cap of the train a tower with <paramref name="head"/> carries for <paramref name="kind"/>. A twin head has its own
        /// train (Hold, Combo, Break wheels). Otherwise the train is the line member's (LineCapDeg: a kind whose twin is missing is judged
        /// as B1 did).</summary>
        public float CapDeg(LiftTrainKind kind, LiftTowerHead head)
        {
            switch (head)
            {
                case LiftTowerHead.Hold: return LiftAlignMath.TrainCapDeg(HoldWheels);
                case LiftTowerHead.Combo: return LiftAlignMath.TrainCapDeg(ComboWheels);
                case LiftTowerHead.Break: return LiftAlignMath.TrainCapDeg(BreakWheels);
                default: return LineCapDeg(kind);
            }
        }

        /// <summary>The head a <paramref name="kind"/> train wants at deflection <paramref name="deflectionDeg"/> (drawn rope, degrees).
        /// HoldDown wants Hold and PushPull wants Combo. Support wants Break past the line train's cap, and otherwise Line. Break is kept
        /// while |d| stays above that cap minus LiftAlignMath.KindHystDeg when <paramref name="previous"/> was Break. Whether the member
        /// has that twin is the planner's question (the fallback in LiftTowerPlanner.AssignHeads).</summary>
        public LiftTowerHead WantHead(LiftTrainKind kind, float deflectionDeg, LiftTowerHead previous = LiftTowerHead.Line)
        {
            switch (kind)
            {
                case LiftTrainKind.HoldDown: return LiftTowerHead.Hold;
                case LiftTrainKind.PushPull: return LiftTowerHead.Combo;
                case LiftTrainKind.Support:
                    if (BreakWheels <= 0)
                        return LiftTowerHead.Line;
                    float line = LineCapDeg(LiftTrainKind.Support), ad = math.abs(deflectionDeg);
                    if (ad > line + LiftTowerPlan.AngleTolDeg || (previous == LiftTowerHead.Break && ad > line - LiftAlignMath.KindHystDeg))
                        return LiftTowerHead.Break;
                    return LiftTowerHead.Line;
                default:
                    return LiftTowerHead.Line;
            }
        }

        public LiftTowerPlanConstants(string typeId, LiftTowerMember[] ladder, float ropeBelowTopM, float carrierDropM, float floorM,
                                      float maxRopeAboveGroundM, float typicalSpanM, float maxSpanM, float minSpanM, float halfWidthM,
                                      float sagRatio, float engineElevationCapM, float stepM = 5f, float sideHillM = 2f, float convexM = 0.5f,
                                      float stationClearM = 0f, float maxEdgeSlope = 0f, int supportWheels = 0, int holdWheels = 0,
                                      int comboWheels = 0, int breakWheels = 0, LiftTowerTwin[] twins = null, float pitchCapDeg = float.NaN,
                                      float pitchSoftDeg = float.NaN, float saddleHalfCapDeg = float.NaN, bool worldHalfOn = true)
        {
            WorldHalfOn = worldHalfOn;
            MinWorldHalfMaxDeg = float.PositiveInfinity;
            for (int h = 0; h < m_WorldHalfMax.Length; h++)
            {
                m_WorldHalfMax[h] = worldHalfOn ? LiftAlignMath.WorldHalfMaxDeg(typeId, WorldHalfKind((LiftTowerHead)h)) : float.PositiveInfinity;
                MinWorldHalfMaxDeg = math.min(MinWorldHalfMaxDeg, m_WorldHalfMax[h]);
            }
            SaddleHalfCapDeg = !float.IsNaN(saddleHalfCapDeg) ? saddleHalfCapDeg
                : typeId == "tram" ? LiftAlignMath.TramBendHalfCapDeg : float.PositiveInfinity;
            PitchCapDeg = float.IsNaN(pitchCapDeg) ? LiftAlignMath.PitchCapDeg(typeId) : pitchCapDeg;
            PitchSoftDeg = float.IsNaN(pitchSoftDeg) ? LiftAlignMath.PitchSoftDeg(typeId) : pitchSoftDeg;
            MaxEdgeSlope = maxEdgeSlope;
            SupportWheels = supportWheels;
            HoldWheels = holdWheels;
            ComboWheels = comboWheels;
            BreakWheels = breakWheels;
            Twins = twins ?? new LiftTowerTwin[0];
            TypeId = typeId;
            Ladder = ladder;
            RopeBelowTopM = ropeBelowTopM;
            CarrierDropM = carrierDropM;
            FloorM = floorM;
            MaxRopeAboveGroundM = maxRopeAboveGroundM;
            TypicalSpanM = typicalSpanM;
            MaxSpanM = maxSpanM;
            MinSpanM = minSpanM;
            HalfWidthM = halfWidthM;
            SagRatio = sagRatio;
            EngineElevationCapM = engineElevationCapM;
            StepM = stepM;
            SideHillM = sideHillM;
            ConvexM = convexM;
            StationClearM = math.max(minSpanM, stationClearM);
        }

        /// <summary>Rope height above the tower base for ladder member <paramref name="m"/>.</summary>
        public float RopeAboveBase(int m) => Ladder[m].TopM - RopeBelowTopM;

        /// <summary>The line net's composition m_HeightRange (NetCompositionData of an edge's Composition.m_Edge), in meters above the edge
        /// curve. The game validates a crossing with this box (FIX-LIST 60 a). It was read in game on 12d5ff6 (field a601-import-test run3)
        /// as 6..20 (DQ 5.8..20, tram 13.75..40). Two lifts therefore needed 14 m between their node curves. Since the 2026-11-05 collision
        /// box round, the box follows the hardware. It runs from about 1 m under the carrier bottom (node + AxleM + RopeSeatM - HangM) to
        /// about 1 m over the rope (node + AxleM + RopeSeatM). The min is set so that the terrain cut ceiling (min + the pieces' 0 offset)
        /// is unchanged. The lift tool reads a crossed edge's own composition and logs a MISMATCH against these values.</summary>
        public static void NetHeightRange(string typeId, out float minM, out float maxM)
        {
            switch (typeId)
            {
                case "fixedGrip": minM = 4f; maxM = 8.5f; return;
                case "detachable": minM = 3.8f; maxM = 9.5f; return;
                case "tram": minM = 11.75f; maxM = 27.5f; return;
                default: minM = 4f; maxM = 11.5f; return;   // gondola
            }
        }

        public float NetHeightMinM { get { NetHeightRange(TypeId, out float lo, out _); return lo; } }
        public float NetHeightMaxM { get { NetHeightRange(TypeId, out _, out float hi); return hi; } }

        /// <summary>The most a twin head raises its node over the ladder member's (Top(twin) - Top(member): 0.15 to 0.57 m in the
        /// tables, 0 without twins). The planner searches members, not heads, so an UNDER crossing keeps this much extra room.</summary>
        public float MaxTwinRiseM
        {
            get
            {
                float r = 0f;
                if (Twins != null)
                    for (int i = 0; i < Twins.Length; i++)
                        r = math.max(r, Twins[i].TopM - Ladder[Twins[i].Member].TopM);
                return r;
            }
        }

        /// <summary>The rope at an end a station will finish (FIX-LIST 48). The station's own anchor sits lower than a tower's rope. On
        /// level ground the rope is node + <see cref="StationDef.RopeAnchorLocalY"/> (the stub tip is at the prefab's y 0, FG 5.84 / 5.24,
        /// DQ 3.70, gondola 4.85, tram 16.00 against the shortest tower's 7.27 / 8.26 / 10.28 / 26.25). Vanilla stands the building on the
        /// terrain at its lot front (StationDef.FrontLocalZ: RoadSide, ObjectToolSystem.CalculateHeight :1621-1625). On ground falling away
        /// from the line the anchor therefore sits lower still. <paramref name="frontGroundY"/> is the terrain there (NaN = not sampled).
        /// The result is the lowest of the tower rope, the level anchor and the anchor over the front. A station or a tower there then
        /// clears the plan. If <paramref name="station"/> is null or has no anchor, the result is the tower rope.</summary>
        public float StationEndRope(StationDef station, float nodeY, float frontGroundY = float.NaN)
        {
            float rope = nodeY + RopeAboveBase(0);
            if (station == null || !math.isfinite(station.RopeAnchorLocalY))
                return rope;
            rope = math.min(rope, nodeY + station.RopeAnchorLocalY);
            if (math.isfinite(frontGroundY))
                rope = math.min(rope, frontGroundY + station.RopeAnchorLocalY);
            return rope;
        }

        /// <summary>From the stub tip to the lot front along the station axis (FrontLocalZ - StubTipLocalZ: FG / tram 24 m, DQ / gondola 32
        /// m). The value is NaN when unknown.</summary>
        public static float FrontDistM(StationDef station) =>
            station == null ? float.NaN : station.FrontLocalZ - station.StubTipLocalZ;

        /// <summary>A copy with other rules (tests and what-if questions: floor, max rope height, spans). sagRatio replays a line logged at
        /// another rope sag.</summary>
        public LiftTowerPlanConstants With(float? floorM = null, float? maxRopeAboveGroundM = null, float? typicalSpanM = null,
                                           float? maxSpanM = null, float? carrierDropM = null, float? maxEdgeSlope = null,
                                           float? sagRatio = null, LiftTowerTwin[] twins = null, float? pitchCapDeg = null,
                                           float? pitchSoftDeg = null, float? saddleHalfCapDeg = null, bool? worldHalfOn = null)
        {
            return new LiftTowerPlanConstants(TypeId, Ladder, RopeBelowTopM, carrierDropM ?? CarrierDropM, floorM ?? FloorM,
                maxRopeAboveGroundM ?? MaxRopeAboveGroundM, typicalSpanM ?? TypicalSpanM, maxSpanM ?? MaxSpanM, MinSpanM, HalfWidthM,
                sagRatio ?? SagRatio, EngineElevationCapM, StepM, SideHillM, ConvexM, StationClearM, maxEdgeSlope ?? MaxEdgeSlope,
                SupportWheels, HoldWheels, ComboWheels, BreakWheels, twins ?? Twins, pitchCapDeg ?? PitchCapDeg, pitchSoftDeg ?? PitchSoftDeg,
                saddleHalfCapDeg ?? SaddleHalfCapDeg, worldHalfOn ?? WorldHalfOn);
        }

        /// <summary>The station-side rule's exchange rate. One more line tower is worth this many meters of extra height (above the
        /// shortest member) on the towers next to the stations (MinCost.EndRiseM). Fewest towers alone put DQ18 / DQ20 100-125 m from both
        /// stations of an ordinary 1,200 m slope (LP-27). The long end span from a low station rope needs a tall tower. A short one needs
        /// one tower more. A value of 8 m buys that tower (DQ18 + DQ20 = 18 m of rise). It keeps a 2-tower DQ line with one DQ14 (4 m) at
        /// 2. It does not stand an FG8 15 m from a station to save an FG12 (LP-3, LP-17). A strict station-first order did all three. A
        /// value of 0 means fewest towers first, and the rise only breaks ties. The tram uses 0 (its towers are the rare 11.4 m wide ones,
        /// and a span of up to 3 km is the point of the type, DECISIONS 381). The fit uses 0 too (LiftTowerPlanner.StationRule off: its
        /// free plan only counts towers).</summary>
        public float TowerWorthM => TypeId == "tram" ? 0f : 8f;

        /// <summary>The floor (lowest carrier point above the high side) at <paramref name="distM"/> meters from a station end. Carriers
        /// leave and enter a station at loading height (DQ: bullwheel rope 3.70 m above the platform, hang 3.30 m = 0.40 m). The floor
        /// therefore ramps from 0 at the station to FloorM at StationClearM (the station approach). Without the ramp the floor applied from
        /// the first sample on. It forced a tall tower right at every station (LP-26, field test 2026-10-26). <paramref name="atStationM"/>
        /// is the carrier's height above the high side at the station end itself. A station sunk into the slope (no-level terraform,
        /// carriers leaving below the ground) ramps from that negative height instead of 0. The planner then does not try to dig the rope
        /// out of the station's own hole with a tall tower beside it (the engine refuses that edge as SteepSlope). The tool warns
        /// instead.</summary>
        public float ApproachFloor(float distM, float atStationM = 0f) =>
            StationClearM > 0f ? math.lerp(math.min(0f, atStationM), FloorM, math.saturate(distM / StationClearM)) : FloorM;

        /// <summary>A station's own rope run, in meters along the line axis from where the carriers turn or dock to the HV stub tip (prefab
        /// z). It is |WheelLocal.z - StubTipLocalZ| on a loop station and |DockLocalA.z - StubTipLocalZ| on a shuttle station. It is 0 when
        /// the row has neither. Catalog rows (SkiCatalog stations): FG base 12.2 / top 12.0, DQ 25.6 / 21.6, gondola 25.4 / 21.4, tram 6.5
        /// / 6.5.</summary>
        public static float StationRunM(StationDef s)
        {
            if (s == null)
                return 0f;
            if (math.isfinite(s.WheelLocal.z))
                return math.abs(s.WheelLocal.z - s.StubTipLocalZ);
            if (math.isfinite(s.DockLocalA.z))
                return math.abs(s.DockLocalA.z - s.StubTipLocalZ);
            return 0f;
        }

        /// <summary>The longest StationRunM over the type's base and top stations. The planner does not know which station a line end is.
        /// Both ends therefore keep the longer run.</summary>
        public static float StationRunM(LiftTypeDef def)
        {
            if (def == null)
                return 0f;
            return math.max(StationRunM(SkiCatalog.StationOf(def.Index, StationRole.Base)), StationRunM(SkiCatalog.StationOf(def.Index, StationRole.Top)));
        }

        // Ladder tops: the tower manifests' top_anchor_y = ObjectGeometryData m_Size.y, confirmed in game.
        // Rope offset = shortest top - catalog AxleM + RopeSeatM (lane y on the shortest tower). The rope rests on the crowns where
        // RopeSeatM > 0, T27.
        // Drop = catalog HangM. Half width = piece m_Width / 2.
        // Sag = the rope lane's m_Hanging, read from the catalog (Envelope.LaneHanging, and CatalogTests mirror it against the lane
        // prefabs).
        // Per-type values: EngineNotes/LiftTowerPlanner.md

        /// <summary>The tram's longest free span (Peak 2 Peak's record span is 3.03 km). Only the lift-line tool builds a span this long.
        /// While the tool is open, the line prefab's max edge length is raised to it (LiftLineToolSystem.SetSpansRaised). The vanilla net
        /// tool keeps SkiTramHV m_MaxPylonDistance 1,000 m. The sag check is unchanged. The engine's rope is the parabola
        /// 4·d·m_Hanging·t(1-t) at any length (LaneSystem.cs:4128-4134), 12 m mid-span at 3 km. The planner tests every span at every 10 m
        /// sample. The other engine limits for one 3 km edge: EngineNotes/LiftTowerPlanner.md.</summary>
        public const float TramMaxSpanM = 3000f;

        public static readonly LiftTowerMember[] FixedGripLadder =
        {
            new LiftTowerMember("SkiLiftTowerFG2", 9.014f),    // the FG8 member (prefab keeps its legacy name)
            new LiftTowerMember("SkiLiftTowerFG12", 13.014f),
            new LiftTowerMember("SkiLiftTowerFG16", 17.014f),
            new LiftTowerMember("SkiLiftTowerFG20", 21.014f),
        };

        public static readonly LiftTowerMember[] DetachableLadder =
        {
            new LiftTowerMember("SkiLiftTowerDQ10", 9.95f),
            new LiftTowerMember("SkiLiftTowerDQ12", 11.95f),
            new LiftTowerMember("SkiLiftTowerDQ14", 13.95f),
            new LiftTowerMember("SkiLiftTowerDQ16", 15.95f),
            new LiftTowerMember("SkiLiftTowerDQ18", 17.95f),
            new LiftTowerMember("SkiLiftTowerDQ20", 19.95f),
        };

        public static readonly LiftTowerMember[] GondolaLadder =
        {
            new LiftTowerMember("SkiLiftTowerGD12", 12f),
            new LiftTowerMember("SkiLiftTowerGD16", 16f),
            new LiftTowerMember("SkiLiftTowerGD20", 20f),
        };

        public static readonly LiftTowerMember[] TramLadder =
        {
            new LiftTowerMember("SkiTramTower30", 30f),
            new LiftTowerMember("SkiTramTower45", 45f),
            new LiftTowerMember("SkiTramTower60", 60f),
        };

        // Twin members (B2 art v2, the 16 manifests docs research art/breakover-twins-2026-11/v2/manifests/*.twin.json: top_anchor_y,
        // twin_of_prefab, placeholder, and lanes = the member's).
        // E = top - ladder[0] top. FG 0.2 / 0.35 / 0.5 (FG8), 4.2 / 4.35 / 4.5 (FG12), 8.5 (FG16 Break), 12.5 (FG20 Break).
        // DQ 0.5178 / 0.2589 (DQ10), 2.5178 / 2.2589 (DQ12). GD 0.568 / 0.284 (GD12), 4.568 / 4.284 (GD16).
        // art-601 (taller twins, assets/import/towers/Q manifests): FG16 Hold / Combo 8.2 / 8.35, DQ16 / DQ20 Hold 6.5178 / 10.5178, GD20
        // Hold 8.568.
        // Every E is under the line's m_ElevationRange.max (FG 16, DQ 11 since art-601 for DQ20 Hold, GD 28). Every top is >= 0.1 m from
        // every other member of its placeholder (check-lift-align LADDER).

        public static readonly LiftTowerTwin[] FixedGripTwins =
        {
            new LiftTowerTwin(0, LiftTowerHead.Hold, "SkiLiftTowerFG8HoldA", 9.214f),
            new LiftTowerTwin(0, LiftTowerHead.Combo, "SkiLiftTowerFG8ComboA", 9.364f),
            new LiftTowerTwin(0, LiftTowerHead.Break, "SkiLiftTowerFG8BreakA", 9.514f),
            new LiftTowerTwin(1, LiftTowerHead.Hold, "SkiLiftTowerFG12HoldA", 13.214f),
            new LiftTowerTwin(1, LiftTowerHead.Combo, "SkiLiftTowerFG12ComboA", 13.364f),
            new LiftTowerTwin(1, LiftTowerHead.Break, "SkiLiftTowerFG12BreakA", 13.514f),
            new LiftTowerTwin(2, LiftTowerHead.Hold, "SkiLiftTowerFG16HoldA", 17.214f),
            new LiftTowerTwin(2, LiftTowerHead.Combo, "SkiLiftTowerFG16ComboA", 17.364f),
            new LiftTowerTwin(2, LiftTowerHead.Break, "SkiLiftTowerFG16BreakA", 17.514f),
            new LiftTowerTwin(3, LiftTowerHead.Break, "SkiLiftTowerFG20BreakA", 21.514f),
        };

        public static readonly LiftTowerTwin[] DetachableTwins =
        {
            new LiftTowerTwin(0, LiftTowerHead.Hold, "SkiLiftTowerDQ10HoldA", 10.4678f),
            new LiftTowerTwin(0, LiftTowerHead.Combo, "SkiLiftTowerDQ10ComboA", 10.2089f),
            new LiftTowerTwin(1, LiftTowerHead.Hold, "SkiLiftTowerDQ12HoldA", 12.4678f),
            new LiftTowerTwin(1, LiftTowerHead.Combo, "SkiLiftTowerDQ12ComboA", 12.2089f),
            new LiftTowerTwin(3, LiftTowerHead.Hold, "SkiLiftTowerDQ16HoldA", 16.4678f),
            new LiftTowerTwin(5, LiftTowerHead.Hold, "SkiLiftTowerDQ20HoldA", 20.4678f),
        };

        public static readonly LiftTowerTwin[] GondolaTwins =
        {
            new LiftTowerTwin(0, LiftTowerHead.Hold, "SkiLiftTowerGD12HoldA", 12.568f),
            new LiftTowerTwin(0, LiftTowerHead.Combo, "SkiLiftTowerGD12ComboA", 12.284f),
            new LiftTowerTwin(1, LiftTowerHead.Hold, "SkiLiftTowerGD16HoldA", 16.568f),
            new LiftTowerTwin(1, LiftTowerHead.Combo, "SkiLiftTowerGD16ComboA", 16.284f),
            new LiftTowerTwin(2, LiftTowerHead.Hold, "SkiLiftTowerGD20HoldA", 20.568f),
        };

        /// <summary>Every twin of every type (the plate table, the LADDER gate and the per-load availability line).</summary>
        public static IEnumerable<LiftTowerTwin> AllTwins()
        {
            foreach (LiftTowerTwin t in FixedGripTwins) yield return t;
            foreach (LiftTowerTwin t in DetachableTwins) yield return t;
            foreach (LiftTowerTwin t in GondolaTwins) yield return t;
        }

        /// <summary><paramref name="all"/> without the twins TwinFilter does not know (all of them when it is null).</summary>
        public static LiftTowerTwin[] Available(LiftTowerTwin[] all)
        {
            Predicate<string> filter = TwinFilter;
            if (filter == null)
                return all;
            var list = new List<LiftTowerTwin>(all.Length);
            for (int i = 0; i < all.Length; i++)
                if (filter(all[i].Name))
                    list.Add(all[i]);
            return list.ToArray();
        }

        /// <summary>The constants for a catalog lift type (null for an unknown id). Drop and rope offset come from the catalog.</summary>
        public static LiftTowerPlanConstants For(LiftTypeDef def)
        {
            if (def == null)
                return null;
            float run = StationRunM(def);   // the station clearance (StationClearM = max(MinSpanM, run))
            switch (def.Id)
            {
                case "fixedGrip":
                    return new LiftTowerPlanConstants(def.Id, FixedGripLadder, FixedGripLadder[0].TopM - (def.AxleM + def.RopeSeatM), def.HangM, 3f, 30f,
                        60f, 90f, 10f, 3f, def.Envelope.LaneHanging, 16f, stationClearM: run, maxEdgeSlope: 0.8f,
                        supportWheels: 4, holdWheels: 4, comboWheels: 2, breakWheels: 8, twins: Available(FixedGripTwins));
                case "detachable":
                    return new LiftTowerPlanConstants(def.Id, DetachableLadder, DetachableLadder[0].TopM - (def.AxleM + def.RopeSeatM), def.HangM, 3f, 30f,
                        110f, 245f, 10f, 3f, def.Envelope.LaneHanging, 11f, stationClearM: run, maxEdgeSlope: 0.9f,   // 11: DQ20 Hold E 10.52 (art-601)
                        supportWheels: 8, holdWheels: 8, comboWheels: 4, twins: Available(DetachableTwins));
                case "gondola":
                    return new LiftTowerPlanConstants(def.Id, GondolaLadder, GondolaLadder[0].TopM - (def.AxleM + def.RopeSeatM), def.HangM, 3f, 30f,
                        110f, 245f, 10f, 4.1f, def.Envelope.LaneHanging, 28f, stationClearM: run, maxEdgeSlope: 1.2f,
                        supportWheels: 10, holdWheels: 8, comboWheels: 4, twins: Available(GondolaTwins));
                case "tram":
                    return new LiftTowerPlanConstants(def.Id, TramLadder, TramLadder[0].TopM - (def.AxleM + def.RopeSeatM), def.HangM, 5f, 0f,
                        800f, TramMaxSpanM, 40f, 7.5f, def.Envelope.LaneHanging, 30f, 10f, stationClearM: run, maxEdgeSlope: 2.5f);   // 10 m samples: a 1-3 km line stays cheap
                default:
                    return null;
            }
        }

        public static LiftTowerPlanConstants For(string typeId) => For(SkiCatalog.LiftById(typeId));
    }

    public struct LiftPlanNode
    {
        public float AlongM;
        public float GroundM;           // center ground (the tower base)
        public float HighSideM;         // max(center, left, right)
        public float LowSideM;          // min(center, left, right)
        public int Member;              // ladder index, -1 for a station
        public float RopeM;             // world y of the rope at the node
        public LiftPlanNodeFlags Flags;
        public bool IsStation => (Flags & LiftPlanNodeFlags.Station) != 0;
        // Phase (d) rope angles (LiftAlignMath.RopeAngles on the planner's rope points): set by Build on every plan.
        public bool HasAngles;          // judged: a line tower, or a line end joining a built line (the planner's EndOuter point)
        public float DeflectionDeg;     // in - out on the drawn rope (h): + convex (support), - concave (hold-down)
        public float TightDeg;          // the tight load case, h x LiftAlignMath.TightSagScale
        public float HeavyDeg;          // the heavy load case, h x LiftAlignMath.HeavySagScale
        public float BareDeg;           // the tram's bare track rope, h x LiftAlignMath.BareSagScale (trams only, 0 otherwise)
        public LiftTrainKind Kind;      // ClassifyTrain(tight, heavy). None without trains (tram) or angles
        public float CapDeg;            // the cap of the train the tower carries (LiftTowerPlanConstants.CapDeg(Kind, Head)). +inf without trains
        public LiftTowerHead Head;      // the placed head: Line or a twin of Member (LiftTowerPlanner.AssignHeads). Line on stations and the tram
        public float HalfDeg;           // the steeper rope half's world pitch, degrees (LiftAlignMath.WorldHalfDeg on the drawn rope, DECISIONS 600 c)
        // Pitch and lean (DECISIONS 600), set by Build on every tower node with both neighbors' rope points (HasPitch):
        public bool HasPitch;
        public float PitchDeg;          // LiftAlignMath.ChordPitchDeg on the plumb rope points, up toward B = +
        public float LeanDeg;           // LiftAlignMath.LeanDeg (>= 0, the top leans downhill, toward the lower neighbor)
        public float AnchorAlongM;      // the leaned tower's rope anchor (LiftAlignMath.LeanedAnchor, equal to AlongM / RopeM unleaned and at stations):
        public float AnchorRopeM;       //   the spans hang from here (Build, RopeAt)
        // Tram pedestals (DECISIONS 600 a, call c): a leaned tram tower floats its origin this far over the ground (LiftAlignMath.TramFloatM,
        // set by Build). The node goes to ground + E + FloatM (the engine stands the member's pivot that far up), RopeM includes it.
        public float FloatM;
    }

    public struct LiftPlanSpan
    {
        public float LengthM;
        public float MinFloorMarginM;   // min over the samples of rope - drop - high side - floor (>= 0 = clear)
        public float MinFloorMarginAtM; // along, from the span start
        public float MinChairAboveGroundM; // the same sample: lowest carrier point above the high side (margin + floor)
        public float MaxRopeAboveM;     // max of rope - center ground
        public float MaxRopeAboveAtM;
        public bool FloorViolated;
        public bool HeightViolated;
        public float EdgeSlope;         // node-to-node |dy| / xz length as the engine builds the edge (0 when an end height is unknown)
        public bool SlopeViolated;      // EdgeSlope over the line prefab's MaxEdgeSlope (only when the planner checks it)
    }

    /// <summary>The side a crossing passes the other lift on (FIX-LIST 60 a). None = not planned (refused before planning).</summary>
    public enum LiftCrossSide : byte
    {
        None,
        Over,
        Under,
    }

    /// <summary>Why a crossing cannot be built (FIX-LIST 60 a). None = the plan keeps the clearance.</summary>
    public enum LiftCrossRefusal : byte
    {
        None,
        Clearance,   // (a) no member arrangement gives the separation the two edge boxes need (over or under)
        Station,     // (b) inside a station's run-out of either line (the rope height is fixed there)
        Tower,       // (c) at or right beside a tower of the crossed lift
        Shallow,     // (d) the two lines meet at less than LiftTowerPlanner.CrossMinAngleDeg
        OwnTower,    // a tower of this line (a pin) stands inside the crossed lift's corridor
        SameType,    // the crossed lift is of this line's own type: the game splits the drawn course beside the crossing (same layer, CourseSplitSystem), so the crossing is refused
    }

    /// <summary>A crossing of the drawn line with a built lift of ANOTHER type (FIX-LIST 60 a). Since 12d5ff6 the two nets no longer join
    /// (one layer bit per type). The game therefore validates the two edges against each other. Each edge is a box from its edge geometry
    /// (the hanging curve, NetUtils.StraightCurve with the net's m_Hanging) up by the composition m_HeightRange. The box covers the whole
    /// xz overlap of the two strips (ValidationHelpers.cs:3316-3370, QuadIntersect :3429). Overlapping boxes refuse the build ("Clearance
    /// too low", :182-408). Inputs come from LiftLineToolSystem (the crossed edge as built), outputs from the planner's Build. Meters along
    /// are from the caller's A.</summary>
    public struct LiftPlanCrossing
    {
        public float AlongM;            // along the drawn line from A (xz)
        public float CrossedY;          // the crossed edge's curve y at the crossing (world, its edge geometry follows the curve)
        public float CrossedSlope;      // |dy / dxz| of that curve there, along the crossed edge
        public float AngleDeg;          // plan angle between the two lines, 0..90
        public float CrossedWidthM;     // the crossed edge's composition m_Width
        public float CrossedMinM, CrossedMaxM;   // the crossed edge's composition m_HeightRange
        public float TowerDistM;        // xz from the crossing to the nearest crossed node that carries a tower (+inf: none)
        public float StationDistM;      // xz from the crossing to the nearest crossed node that is a station's (+inf: none)
        public float CrossedClearM;     // the crossed type's station run-out (its LiftTowerPlanConstants.StationClearM)
        public float CrossedHeadHalfM;  // half width of the crossed type's tower head (its HalfWidthM)
        public string CrossedType;      // the crossed lift type id (log)
        // Build: the side planned, the separation the boxes need between the two curves at the crossing point (box clearance, the
        // footprint's slope terms, CrossMarginM), the plan's separation there, and the refusal (None = buildable). The plan's separation is
        // new - crossed for Over and crossed - new for Under. A crossing refused before planning reads Over's numbers.
        public LiftCrossSide Side;
        public float RequiredM, PlannedM;
        public LiftCrossRefusal Refusal;
    }

    public sealed class LiftTowerPlan
    {
        public LiftTowerPlanConstants Constants;
        public float LengthM;
        public LiftPlanNode[] Nodes;
        public LiftPlanSpan[] Spans;
        /// <summary>Crossings with built lifts of another type (LiftTowerPlanner.Crossings), judged on this plan. Empty when
        /// none.</summary>
        public LiftPlanCrossing[] Crossings = new LiftPlanCrossing[0];

        /// <summary>Crossings this plan cannot build (Refusal != None).</summary>
        public int CrossingsRefused
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Crossings.Length; i++)
                    n += Crossings[i].Refusal != LiftCrossRefusal.None ? 1 : 0;
                return n;
            }
        }
        public float[] SampleAlongM;    // the planner's profile (copied): along, center ground, high side, per sample
        public float[] SampleCentreM;
        public float[] SampleHighM;
        public float[] SampleFloorM;    // the floor at each sample (FloorM, ramped near a station end: LiftTowerPlanConstants.ApproachFloor)
        public bool StationA, StationB; // the ends are stations (approach floor, station-side member rule), false = a tower node
        /// <summary>The lowest carrier point above the high side at each end sample, with the end's own rope. A station whose rope leaves
        /// it below the ground in front (a station sunk into the slope) reads negative. No short tower can clear that.</summary>
        public float EndCarrierA, EndCarrierB;
        public int LineTowers => Nodes == null ? 0 : math.max(0, Nodes.Length - 2);

        /// <summary>Phase (d): wall time of the angle pass (pairs) in ms. Also its deterministic work count (candidate evaluations, the
        /// cost gate) and one note per repair tried (what was over its cap and what the pass did).</summary>
        public double AnglesMs;
        public long AngleWork;
        public List<string> AngleNotes = new List<string>();

        /// <summary>Towers per kind among the judged nodes, pairs (towers flagged Pair) and towers still over their cap.</summary>
        public void KindCounts(out int support, out int holdDown, out int pushPull, out int pairTowers, out int overCap)
        {
            support = holdDown = pushPull = pairTowers = overCap = 0;
            for (int i = 0; i < Nodes.Length; i++)
            {
                LiftPlanNode n = Nodes[i];
                if ((n.Flags & LiftPlanNodeFlags.Pair) != 0) pairTowers++;
                if (!n.HasAngles)
                    continue;
                if (n.Kind == LiftTrainKind.Support) support++;
                else if (n.Kind == LiftTrainKind.HoldDown) holdDown++;
                else if (n.Kind == LiftTrainKind.PushPull) pushPull++;
                if (IsOverCap(i)) overCap++;
            }
        }

        /// <summary>Node <paramref name="i"/> is judged and its deflection is past its cap (by more than AngleTolDeg): a train lift's
        /// sheave train, or the tram's saddle (2 x SaddleHalfCapDeg, DECISIONS 600 a).</summary>
        public bool IsOverCap(int i) =>
            Nodes[i].HasAngles && math.abs(Nodes[i].DeflectionDeg) > Nodes[i].CapDeg + AngleTolDeg;

        /// <summary>Towers still over their cap after the angle pass (KindCounts' overCap).</summary>
        public int OverCapTowers
        {
            get
            {
                KindCounts(out _, out _, out _, out _, out int over);
                return over;
            }
        }

        /// <summary>The plan's verdict for the logs (FIX-LIST 55). It is "clear" only when Sound (every span clear and no tower over its
        /// cap). It is "spans clear, N tower(s) OVER CAP" when only the angles fail. Otherwise it is "VIOLATED". Clear alone says nothing
        /// about the angles, so no log line prints it as the plan's verdict.</summary>
        public string Verdict
        {
            get
            {
                if (!Clear)
                    return "VIOLATED";
                string angles = AngleFaults();
                return angles.Length == 0 ? "clear" : "spans clear, " + angles;
            }
        }

        /// <summary>", but N tower(s) OVER CAP" / "TOO STEEP" when a tower is past its bend or pitch cap, and empty otherwise. It is for
        /// the log lines that report the spans.</summary>
        public string OverCapNote
        {
            get
            {
                string angles = AngleFaults();
                return angles.Length == 0 ? string.Empty : ", but " + angles;
            }
        }

        /// <summary>"N tower(s) OVER CAP", "N tower(s) TOO STEEP (pitch past P deg)" and / or "N tower(s) PAST CLEARANCE (...)", or
        /// empty.</summary>
        private string AngleFaults()
        {
            int over = OverCapTowers, steep = TooSteepTowers, clearance = ClearanceTowers;
            string a = over == 0 ? string.Empty : over + " tower(s) OVER CAP";
            if (steep > 0)
                a += (a.Length > 0 ? ", " : string.Empty) + steep + " tower(s) TOO STEEP (pitch past " + F(Constants.PitchCapDeg, "F0") + " deg)";
            if (clearance > 0)
                a += (a.Length > 0 ? ", " : string.Empty) + clearance + " tower(s) PAST CLEARANCE (a rope half steeper than its carrier clears)";
            return a;
        }

        /// <summary>Tower node <paramref name="i"/> is judged. Its steeper rope half's world pitch (HalfDeg) is past the carrier-clearance
        /// limit of its placed head (LiftTowerPlanConstants.WorldHalfMaxDeg, by more than AngleTolDeg, DECISIONS 600 c). The carrier,
        /// hanging plumb, would swing into its own train or the head's deck end. This is the lift tool's red tower,
        /// TOWER_CLEARANCE.</summary>
        public bool IsOverClearance(int i) =>
            Nodes[i].HasAngles && Nodes[i].Member >= 0 && Nodes[i].HalfDeg > Constants.WorldHalfMaxDeg(Nodes[i].Head) + AngleTolDeg;

        /// <summary>Towers past their carrier-clearance limit (IsOverClearance).</summary>
        public int ClearanceTowers
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Nodes.Length; i++)
                    if (IsOverClearance(i))
                        n++;
                return n;
            }
        }

        /// <summary>Tower node <paramref name="i"/> has its pitch judged and the rope runs through it steeper than the type's hard pitch
        /// cap (LiftTowerPlanConstants.PitchCapDeg, by more than AngleTolDeg, DECISIONS 600). This is the lift tool's "too steep"
        /// tower.</summary>
        public bool IsTooSteep(int i) =>
            Nodes[i].HasPitch && Nodes[i].Member >= 0 && math.abs(Nodes[i].PitchDeg) > Constants.PitchCapDeg + AngleTolDeg;

        /// <summary>Towers whose pitch is past the type's hard pitch cap (IsTooSteep).</summary>
        public int TooSteepTowers
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Nodes.Length; i++)
                    if (IsTooSteep(i))
                        n++;
                return n;
            }
        }

        /// <summary>The steepest |pitch| and the largest lean over the plan's towers, degrees, and how many towers lean.</summary>
        public void PitchStats(out float maxPitchDeg, out float maxLeanDeg, out int leaning)
        {
            maxPitchDeg = maxLeanDeg = 0f;
            leaning = 0;
            for (int i = 0; i < Nodes.Length; i++)
            {
                if (!Nodes[i].HasPitch || Nodes[i].Member < 0)
                    continue;
                maxPitchDeg = math.max(maxPitchDeg, math.abs(Nodes[i].PitchDeg));
                maxLeanDeg = math.max(maxLeanDeg, Nodes[i].LeanDeg);
                if (Nodes[i].LeanDeg > 0f)
                    leaning++;
            }
        }

        /// <summary>One tower's pitch and lean for the plan dump: "pitch +31.2, lean 11.2 (anchor -3.5 m along, -0.2 m)" ("TOO STEEP"
        /// past the hard cap).</summary>
        public string PitchText(int i)
        {
            LiftPlanNode n = Nodes[i];
            if (!n.HasPitch)
                return "pitch n/a";
            var sb = new StringBuilder();
            sb.Append("pitch ").Append(Sg(n.PitchDeg)).Append(", lean ").Append(F(n.LeanDeg, "F1"));
            if (n.LeanDeg > 0f)
                sb.Append(" (anchor ").Append(Sg(n.AnchorAlongM - n.AlongM)).Append(" m along, ").Append(Sg(n.AnchorRopeM - n.RopeM)).Append(" m)");
            if (IsTooSteep(i))
                sb.Append(" TOO STEEP ").Append(F(Constants.PitchCapDeg, "F0"));
            return sb.ToString();
        }

        /// <summary>A deflection this little past its cap is at the cap (float noise between the pass and Build).</summary>
        public const float AngleTolDeg = 0.01f;

        /// <summary>The angle summary for "LiftLine [plan]" / "Towers [angles]": kinds, pairs, the worst support and hold-down
        /// deflection against its cap.</summary>
        public string AngleSummary()
        {
            KindCounts(out int s, out int h, out int p, out int pairs, out int over);
            float worstS = 0f, capS = float.PositiveInfinity, worstH = 0f, capH = float.PositiveInfinity;
            bool any = false;
            for (int i = 0; i < Nodes.Length; i++)
            {
                LiftPlanNode n = Nodes[i];
                if (!n.HasAngles)
                    continue;
                any = true;
                if (n.DeflectionDeg > worstS) { worstS = n.DeflectionDeg; capS = n.CapDeg; }
                if (n.DeflectionDeg < worstH) { worstH = n.DeflectionDeg; capH = n.CapDeg; }
            }
            if (!any)
                return "no tower judged";
            if (!Constants.HasTrains)
            {
                // the tram: saddles. The bare-rope breakover rule (14.4.4) is logged, never planned
                int lift = 0;
                for (int i = 0; i < Nodes.Length; i++)
                    if (Nodes[i].HasAngles && Nodes[i].BareDeg < LiftAlignMath.SaddleMinBreakDeg)
                        lift++;
                return "saddles: worst d " + Sg(worstS) + " / " + Sg(worstH) + " deg (cap " + F(2f * Constants.SaddleHalfCapDeg, "F0") +
                       ", pair towers " + pairs + ", over cap " + over + "); bare-rope breakover under " +
                       F(LiftAlignMath.SaddleMinBreakDeg, "F2") + " deg at " + lift + " tower(s) (logged only)";
            }
            return "support " + s + ", hold-down " + h + ", push-pull " + p + "; pair towers " + pairs + ", over cap " + over +
                   "; worst support d " + Sg(worstS) + (worstS > 0f ? " / cap " + F(capS, "F0") : string.Empty) +
                   ", worst hold-down d " + Sg(worstH) + (worstH < 0f ? " / cap " + F(capH, "F0") : string.Empty);
        }

        /// <summary>One tower's angle text: "d +12.3 (tight +9.1 / heavy +17.0) S [pair]" (tram: "d +8.0 (bare +6.1) saddle").</summary>
        public string AngleText(int i)
        {
            LiftPlanNode n = Nodes[i];
            if (!n.HasAngles)
                return "d n/a";
            var sb = new StringBuilder();
            sb.Append("d ").Append(Sg(n.DeflectionDeg));
            if (!Constants.HasTrains)
            {
                sb.Append(" (bare ").Append(Sg(n.BareDeg)).Append(n.BareDeg < LiftAlignMath.SaddleMinBreakDeg ? ", UNDER " + F(LiftAlignMath.SaddleMinBreakDeg, "F2") : string.Empty)
                  .Append(") saddle");
                if (IsOverCap(i))
                    sb.Append(" OVER CAP ").Append(F(n.CapDeg, "F0"));
            }
            else
            {
                sb.Append(" (tight ").Append(Sg(n.TightDeg)).Append(" / heavy ").Append(Sg(n.HeavyDeg)).Append(") ").Append(LiftAlignMath.KindLetter(n.Kind));
                if (n.Head != LiftTowerHead.Line)
                    sb.Append(' ').Append(n.Head);
                if (IsOverCap(i))
                    sb.Append(" OVER CAP ").Append(F(n.CapDeg, "F0"));
                if (IsOverClearance(i))
                    sb.Append(" half ").Append(F(n.HalfDeg, "F1")).Append(" PAST CLEARANCE ").Append(F(Constants.WorldHalfMaxDeg(n.Head), "F1"));
            }
            if ((n.Flags & LiftPlanNodeFlags.Pair) != 0)
                sb.Append(" [pair]");
            return sb.ToString();
        }

        /// <summary>Rope y on span <paramref name="span"/> at <paramref name="alongM"/> (line meters), with the engine's sag, hung from the
        /// two towers' (leaned) rope anchors (AnchorAlongM / AnchorRopeM).</summary>
        public float RopeAt(int span, float alongM)
        {
            LiftPlanNode a = Nodes[span], b = Nodes[span + 1];
            float d = b.AnchorAlongM - a.AnchorAlongM;
            if (d <= 0f)
                return a.AnchorRopeM;
            float t = math.saturate((alongM - a.AnchorAlongM) / d);
            return a.AnchorRopeM + (b.AnchorRopeM - a.AnchorRopeM) * t - 4f * Constants.SagRatio * d * t * (1f - t);
        }

        /// <summary>Floor margin (rope - drop - high side - floor) at profile sample <paramref name="q"/> on span <paramref name="span"/>.</summary>
        public float MarginAt(int span, int q) =>
            RopeAt(span, SampleAlongM[q]) - Constants.CarrierDropM - SampleHighM[q] - (SampleFloorM != null ? SampleFloorM[q] : Constants.FloorM);
        /// <summary>Every span is clear (floor, max rope height, edge slope). This says nothing about the towers' angles. The plan's
        /// verdict is Sound / Verdict.</summary>
        public bool Clear
        {
            get
            {
                for (int i = 0; i < Spans.Length; i++)
                    if (Spans[i].FloorViolated || Spans[i].HeightViolated || Spans[i].SlopeViolated)
                        return false;
                return true;
            }
        }

        public float MaxEdgeSlope
        {
            get
            {
                float v = 0f;
                for (int i = 0; i < Spans.Length; i++)
                    v = math.max(v, Spans[i].EdgeSlope);
                return v;
            }
        }

        public float MinFloorMarginM
        {
            get
            {
                float v = float.PositiveInfinity;
                for (int i = 0; i < Spans.Length; i++)
                    v = math.min(v, Spans[i].MinFloorMarginM);
                return v;
            }
        }

        /// <summary>Spans violated (floor, max rope height or edge slope).</summary>
        public int ViolatedSpans
        {
            get
            {
                int v = 0;
                for (int i = 0; i < Spans.Length; i++)
                    if (Spans[i].FloorViolated || Spans[i].HeightViolated || Spans[i].SlopeViolated)
                        v++;
                return v;
            }
        }

        /// <summary>The worst deflection past its cap among the judged towers, deg (0 = every one within its cap).</summary>
        public float MaxCapExcessDeg
        {
            get
            {
                float v = 0f;
                for (int i = 0; i < Nodes.Length; i++)
                    if (Nodes[i].HasAngles)
                        v = math.max(v, math.abs(Nodes[i].DeflectionDeg) - Nodes[i].CapDeg);
                return v;
            }
        }

        /// <summary>Every span clear, no tower over its cap, none too steep (IsTooSteep) and none past its carrier clearance
        /// (IsOverClearance).</summary>
        public bool Sound => Clear && OverCapTowers == 0 && TooSteepTowers == 0 && ClearanceTowers == 0;

        /// <summary>Null when <paramref name="plan"/> is no worse than <paramref name="built"/> on any axis (floor margin, violated spans,
        /// towers over their cap, the worst excess over a cap). Otherwise the result names the axes it is worse on (FIX-LIST 52).</summary>
        public static string WorseThan(LiftTowerPlan plan, LiftTowerPlan built)
        {
            var why = new List<string>(4);
            if (plan.MinFloorMarginM < built.MinFloorMarginM - WorseMarginTolM)
                why.Add("floor margin " + Sg(built.MinFloorMarginM) + " -> " + Sg(plan.MinFloorMarginM) + " m");
            if (plan.ViolatedSpans > built.ViolatedSpans)
                why.Add("violated spans " + built.ViolatedSpans + " -> " + plan.ViolatedSpans);
            plan.KindCounts(out _, out _, out _, out _, out int overP);
            built.KindCounts(out _, out _, out _, out _, out int overB);
            if (overP > overB)
                why.Add("towers over cap " + overB + " -> " + overP);
            if (plan.TooSteepTowers > built.TooSteepTowers)
                why.Add("towers too steep " + built.TooSteepTowers + " -> " + plan.TooSteepTowers);
            if (plan.ClearanceTowers > built.ClearanceTowers)
                why.Add("towers past clearance " + built.ClearanceTowers + " -> " + plan.ClearanceTowers);
            if (plan.MaxCapExcessDeg > built.MaxCapExcessDeg + AngleTolDeg)
                why.Add("worst excess over a cap " + Sg(built.MaxCapExcessDeg) + " -> " + Sg(plan.MaxCapExcessDeg) + " deg");
            return why.Count == 0 ? null : string.Join(", ", why);
        }

        /// <summary>A floor margin this much lower is the same margin (the 5 / 10 m sample grid, float noise).</summary>
        public const float WorseMarginTolM = 0.05f;

        /// <summary>The "Towers [plan]" block (the "Towers [nodes]" node/span layout of LiftTowerDiagSystem). <paramref name="header"/> is
        /// the text after "Towers [plan]: " on the first line. The caller appends the planned-vs-actual numbers there.</summary>
        public void AppendLog(StringBuilder sb, string header)
        {
            LiftTowerPlanConstants c = Constants;
            sb.Append("Towers [plan]: ").Append(header).Append(" — planned ").Append(Nodes.Length).Append(" node(s), ")
              .Append(LineTowers).Append(" line tower(s), ").Append(Spans.Length).Append(" span(s), ").Append(F(LengthM, "F0"))
              .Append(" m xz; rules ").Append(c.TypeId).Append(": floor ").Append(F(c.FloorM, "F1")).Append(" + drop ")
              .Append(F(c.CarrierDropM, "F1")).Append(", max rope ").Append(c.MaxRopeAboveGroundM > 0f ? F(c.MaxRopeAboveGroundM, "F0") : "none")
              .Append(", span typ ").Append(F(c.TypicalSpanM, "F0")).Append(" / max ").Append(F(c.MaxSpanM, "F0"))
              .Append(", sides ±").Append(F(c.HalfWidthM, "F1")).Append('\n');
            for (int i = 0; i < Nodes.Length; i++)
            {
                LiftPlanNode n = Nodes[i];
                sb.Append("  p").Append(i + 1).Append(" at ").Append(F(n.AlongM, "F0")).Append(" m: ground ").Append(F(n.GroundM, "F2"))
                  .Append(", high side ").Append(Sg(n.HighSideM - n.GroundM)).Append(", low side ").Append(Sg(n.LowSideM - n.GroundM))
                  .Append(", ").Append(n.IsStation ? "station" : "member " + c.NameOf(n.Member, n.Head) + " (top " + F(c.TopOf(n.Member, n.Head), "F2") + ")")
                  .Append(", rope y ").Append(F(n.RopeM, "F2")).Append(" (").Append(Sg(n.RopeM - n.GroundM)).Append(" above ground)")
                  .Append(" [").Append(FlagText(n.Flags)).Append("]").Append(n.HasAngles ? ", " + AngleText(i) : string.Empty)
                  .Append(n.HasPitch && !n.IsStation ? ", " + PitchText(i) : string.Empty).Append('\n');
                if (i < Spans.Length)
                {
                    LiftPlanSpan s = Spans[i];
                    sb.Append("      span -> p").Append(i + 2).Append(": ").Append(F(s.LengthM, "F0")).Append(" m xz, dy ")
                      .Append(Sg(Nodes[i + 1].RopeM - n.RopeM)).Append(", lowest carrier above ground min ").Append(Sg(s.MinChairAboveGroundM))
                      .Append(" at ").Append(F(s.MinFloorMarginAtM, "F0")).Append(" m (floor margin ").Append(Sg(s.MinFloorMarginM))
                      .Append("), rope max ").Append(Sg(s.MaxRopeAboveM)).Append(" above ground at ").Append(F(s.MaxRopeAboveAtM, "F0"))
                      .Append(" m -> ").Append(s.FloorViolated ? "FLOOR VIOLATED" : s.HeightViolated ? "MAX HEIGHT VIOLATED"
                          : s.SlopeViolated ? "EDGE TOO STEEP (" + F(s.EdgeSlope, "F2") + " > " + F(c.MaxEdgeSlope, "F2") + ")" : "clear").Append('\n');
                }
            }
        }

        /// <summary>{twin x count} of the towers that carry a twin head ("none" when every tower carries its support head).</summary>
        public string HeadCounts()
        {
            var names = new List<string>();
            var counts = new List<int>();
            for (int i = 0; i < Nodes.Length; i++)
            {
                if (Nodes[i].IsStation || Nodes[i].Head == LiftTowerHead.Line)
                    continue;
                string name = Constants.NameOf(Nodes[i].Member, Nodes[i].Head);
                int at = names.IndexOf(name);
                if (at < 0)
                {
                    names.Add(name);
                    counts.Add(1);
                }
                else
                {
                    counts[at]++;
                }
            }
            if (names.Count == 0)
                return "none";
            var sb = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
                sb.Append(i > 0 ? ", " : string.Empty).Append(names[i]).Append(" x").Append(counts[i]);
            return sb.ToString();
        }

        /// <summary>{member x count} for the summary line, shortest first (ladder members, and HeadCounts names the twins).</summary>
        public string MemberCounts()
        {
            var counts = new int[Constants.Ladder.Length];
            for (int i = 0; i < Nodes.Length; i++)
                if (!Nodes[i].IsStation)
                    counts[Nodes[i].Member]++;
            var sb = new StringBuilder();
            for (int m = 0; m < counts.Length; m++)
            {
                if (counts[m] == 0)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(Constants.Ladder[m].Name).Append(" x").Append(counts[m]);
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        internal static string FlagText(LiftPlanNodeFlags f)
        {
            if (f == LiftPlanNodeFlags.None)
                return "-";
            var sb = new StringBuilder();
            if ((f & LiftPlanNodeFlags.Station) != 0) sb.Append("station,");
            if ((f & LiftPlanNodeFlags.Seed) != 0) sb.Append("seed,");
            if ((f & LiftPlanNodeFlags.Shoulder) != 0) sb.Append("shoulder,");
            if ((f & LiftPlanNodeFlags.Valley) != 0) sb.Append("valley,");
            if ((f & LiftPlanNodeFlags.Breakover) != 0) sb.Append("breakover,");
            if ((f & LiftPlanNodeFlags.Infill) != 0) sb.Append("infill,");
            if ((f & LiftPlanNodeFlags.SideHill) != 0) sb.Append("side-hill,");
            if ((f & LiftPlanNodeFlags.NearStation) != 0) sb.Append("near-station,");
            if ((f & LiftPlanNodeFlags.Pair) != 0) sb.Append("pair,");
            if ((f & LiftPlanNodeFlags.OverCap) != 0) sb.Append("over-cap,");
            sb.Length--;
            return sb.ToString();
        }

        internal static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
        internal static string Sg(float v) => (v >= 0f ? "+" : string.Empty) + v.ToString("F1", CultureInfo.InvariantCulture);
    }

    /// <summary>How a line with a fixed node count was fitted (LiftTowerPlanner.Fit).</summary>
    public enum LiftFitMode
    {
        Kept,    // heights only, at the nodes where they are
        Moved,   // the planner's positions (spare nodes at span midpoints), heights by phase (c)
        Short,   // no clear arrangement with this many nodes: the better of the two, violation reported
    }

    public sealed class LiftTowerFit
    {
        public LiftFitMode Mode;
        public LiftTowerPlan Plan;    // the arrangement to build (Kept, Moved or the better of the two)
        public LiftTowerPlan Kept;    // heights only at the current positions
        public LiftTowerPlan Moved;   // null unless tried
        public LiftTowerPlan Free;    // the planner's own line (its node count may differ)
        public int Inner;             // line towers the line has (its inner nodes)
        public int Needed;            // line towers the free plan uses
    }

    /// <summary>The engine-side numbers of a fitted node. They are pure, so LiftBatch checks them (LP-15). A tower node is raised so that
    /// the vanilla placeholder ladder spawns exactly the planned member on the ground. The values are: node y = center ground + E,
    /// Game.Net.Elevation = (E, E), E = TopM[member] - TopM[0]. SubObjectSystem scores the planned member spare 0 / score 1
    /// (SubObjectSystem.cs:980, :2189-2199). A Standalone pillar is placed top-anchored (:2464-2469, :2477-2482). The rope lane is node y +
    /// lane y (LaneSystem.cs:4112-4115). The edge curve is NetUtils.StraightCurve(a, d, net m_Hanging) (NetUtils.cs:178-191). Derivation:
    /// EngineNotes/LiftTowerPlanner.md</summary>
    public static class LiftTowerEdit
    {
        /// <summary>A node this far BELOW its target (ground + E) is rewritten: the tower would sink into the ground.</summary>
        public const float HeightBelowTolM = 0.25f;

        /// <summary>A node up to this far ABOVE its target is left alone. Every write re-curves the node's edges. The carve (cut ceiling =
        /// edge y + m_HeightRange.min + m_MinHeightOffset = edge y + 4, TerrainSystem.AddSegment) follows the curve. On the steep tram
        /// spans the cut reaches the node. The ground under it sank 0.39 -> 0.33 -> 0.27 m per write (ratio ~0.85). A fit that re-grounds
        /// on every 0.25 m therefore wrote three times and hit the loop guard. The write that caused the sink is ours. The tower base then
        /// stands at most this far above the carved ground.</summary>
        public const float HeightAboveTolM = 1.0f;

        /// <summary>Does a node at <paramref name="oldY"/> need a height write to reach <paramref name="targetY"/> (ground + E)
        /// when its member and position are unchanged? One-sided: below by more than HeightBelowTolM, or above by more than
        /// HeightAboveTolM.</summary>
        public static bool HeightWrite(float oldY, float targetY) =>
            oldY < targetY - HeightBelowTolM || oldY > targetY + HeightAboveTolM;

        /// <summary>HeightWrite over a BUILT line (LiftTowerFitSystem.Built: every inner node carries a fit's exact Elevation). It writes
        /// only when the node is below its target by more than HeightBelowTolM. A built tower is never lowered for ground that sank under
        /// it with its member, head and position unchanged (FIX-LIST 57). The line's own cut lowers the ground under its towers after the
        /// fit that placed them (TerrainSystem.AddSegment, TerrainSystem.cs:1397-1516: a cut, never a fill). The fit right after a build
        /// reads the ground before that cut reaches the CPU heights. The load re-fit reads it after. Field result: every tram tower stood
        /// 0.3-1.0 m over its ground at load, and one T60 stood 1.05 m over and was re-written. The cut only lowers, so "above" over a
        /// built line is our own carve. A player's terraform is not re-fitted in session either (the fit key is edges, node positions and
        /// stations). A load now does what the session does. With BuiltFootSampler the target of an unchanged built tower is never below
        /// its node. This rule states it at the write.</summary>
        public static bool HeightWrite(float oldY, float targetY, bool built) =>
            built ? oldY < targetY - HeightBelowTolM : HeightWrite(oldY, targetY);

        /// <summary>A built tower whose foot stands more than this over the ground now under it is counted in the fit's log line.</summary>
        public const float FootSunkLogM = 0.05f;

        /// <summary>A planner sample this close (m along) to a built node is that node's own sample (LiftTowerPlanner.Sample inserts
        /// each fixed node position exactly, or uses a grid sample within 1e-3 m).</summary>
        public const float FootAtTolM = 0.01f;

        /// <summary><paramref name="ground"/> with each built tower's own center sample at its FOOT (FIX-LIST 57): max(ground, foot), foot
        /// = node y - E (the ground the fit stood it on, plus a tram tower's pedestal float). Our own cut sinks the ground under a built
        /// tower after the fit (field: every tram tower 0.3-1.05 m, the high side beside a T60 5 m). When the planner judges on the sunk
        /// ground, it hangs the rope that much low at the tower (rope = center + the member's rope height). A load re-fit therefore
        /// re-grounded a tower or re-membered a line near its floor. Only the center at the node changes. The sides, the span samples and a
        /// tram tower's float profile stay the ground as it is (lower = more clearance, and the float sees its own foot and comes out 0).
        /// Ground now higher than the foot (a raise under a tower) wins, so a buried tower is still raised.</summary>
        public static LiftTerrainSampler BuiltFootSampler(LiftTerrainSampler ground, IList<float> innerAlongM, IList<float> footY) =>
            (s, l) =>
            {
                float g = ground(s, l);
                if (l != 0f)
                    return g;
                for (int i = 0; i < innerAlongM.Count; i++)
                    if (math.abs(s - innerAlongM[i]) <= FootAtTolM)
                        return math.max(g, footY[i]);
                return g;
            };

        /// <summary>The fit's write decision for one inner node (LiftTowerFitSystem). It is pure, for LiftBatch (RL-57 / RL-1). A write is
        /// due for a move, a new member or head, a height off its target (<see cref="HeightWrite(float, float, bool)"/>), or an Elevation
        /// component that is not the member's exact (E, E). Target = ground + E + a tram tower's pedestal float.</summary>
        public static bool NodeWrite(bool built, bool moved, bool reMembered, float oldY, float targetY, bool elevationOff) =>
            moved || reMembered || HeightWrite(oldY, targetY, built) || elevationOff;

        public static float ElevationM(LiftTowerPlanConstants c, int member) =>
            member <= 0 ? 0f : c.Ladder[member].TopM - c.Ladder[0].TopM;

        /// <summary>E of the placed member: Top(twin) - Top(ladder[0]) for a twin head, ElevationM(member) for Line. The node goes to
        /// ground + E. The engine then spawns the twin (unique top, score 1) with its pivot on the ground. The rope stays at the member's
        /// lane height (the twin's lanes are the member's).</summary>
        public static float ElevationM(LiftTowerPlanConstants c, int member, LiftTowerHead head)
        {
            if (member < 0)
                return 0f;
            int t = c.TwinIndex(member, head);
            return t >= 0 ? c.Twins[t].TopM - c.Ladder[0].TopM : ElevationM(c, member);
        }

        /// <summary>The (member, head) a node at <paramref name="elevationM"/> carries. It is the ladder member or twin whose E matches
        /// within MemberMatchM (tops are >= 0.1 m apart, check-lift-align LADDER, so at most one matches). Otherwise it is the tallest
        /// LADDER member not above it with the Line head (a node the engine elevated by itself, e.g. one drawn above the ground).</summary>
        public static int MemberOf(LiftTowerPlanConstants c, float elevationM, out LiftTowerHead head)
        {
            head = LiftTowerHead.Line;
            for (int m = 0; m < c.Ladder.Length; m++)
                if (math.abs(ElevationM(c, m) - elevationM) <= MemberMatchM)
                    return m;
            for (int t = 0; t < c.Twins.Length; t++)
            {
                if (math.abs(c.Twins[t].TopM - c.Ladder[0].TopM - elevationM) <= MemberMatchM)
                {
                    head = c.Twins[t].Head;
                    return c.Twins[t].Member;
                }
            }
            int best = 0;
            for (int m = 0; m < c.Ladder.Length; m++)
                if (ElevationM(c, m) <= elevationM + MemberMatchM)
                    best = m;
            return best;
        }

        /// <summary>The rope height above the NODE of a tower placed as (<paramref name="member"/>, <paramref name="head"/>). The node
        /// stands at ground + E(member, head), and the tower's pivot stands on the ground. So the lane anchor (pivot-local, the support
        /// member's lane for a twin: LADDER) gives rope = ground + RopeAboveBase(member) = node y + RopeAboveBase(member) - E(member,
        /// head). A Line head is RopeAboveBase(0) for every member. A twin sits Top(twin) - Top(member) (0.15-0.57 m) lower than
        /// that.</summary>
        public static float RopeAboveNodeM(LiftTowerPlanConstants c, int member, LiftTowerHead head)
        {
            if (member < 0 || head == LiftTowerHead.Line || !c.HasTwin(member, head))
                return c.RopeAboveBase(0);
            return c.RopeAboveBase(member) - ElevationM(c, member, head);
        }

        /// <summary>RopeAboveNodeM of the head a node at <paramref name="elevationM"/> carries (MemberOf, the read the fit uses). The rope
        /// height a loose end, a station stub or a path end has when no lane anchor is found (the lane-less fallback of the lift tool, the
        /// fit and the diag).</summary>
        public static float RopeAboveNodeM(LiftTowerPlanConstants c, float elevationM)
        {
            int m = MemberOf(c, elevationM, out LiftTowerHead head);
            return RopeAboveNodeM(c, m, head);
        }

        /// <summary>MemberOf's match tolerance (meters of E).</summary>
        public const float MemberMatchM = 0.05f;

        /// <summary>The train kind a head implies (the hysteresis input when no classified kind is known, e.g. after a load). Hold implies
        /// HoldDown, Combo implies PushPull, and Break implies Support. Line implies None (a support head can carry any kind whose twin was
        /// missing).</summary>
        public static LiftTrainKind KindOf(LiftTowerHead head) =>
            head == LiftTowerHead.Hold ? LiftTrainKind.HoldDown : head == LiftTowerHead.Combo ? LiftTrainKind.PushPull
            : head == LiftTowerHead.Break ? LiftTrainKind.Support : LiftTrainKind.None;

        /// <summary>Where the engine stands member <paramref name="member"/> at a node <paramref name="elevationM"/> above the ground: its
        /// pivot above the ground (AnchorTop, :2477-2482: node y + spec y - top, and spec y = TopM[0]). 0 = on the ground.</summary>
        public static float EnginePivotAboveGround(LiftTowerPlanConstants c, int member, float elevationM) =>
            elevationM + c.Ladder[0].TopM - c.Ladder[member].TopM;

        /// <summary>EnginePivotAboveGround for the placed (member, head): a twin's own top.</summary>
        public static float EnginePivotAboveGround(LiftTowerPlanConstants c, int member, LiftTowerHead head, float elevationM) =>
            elevationM + c.Ladder[0].TopM - c.TopOf(member, head);

        /// <summary>The (member, head) the engine spawns at an owner elevation, over the ladder and the twins (the placeholder's members as
        /// the planner knows them). The highest score wins. Ties return -1.</summary>
        public static int EngineWinner(LiftTowerPlanConstants c, float ownerElevationM, out LiftTowerHead head, out float score)
        {
            int best = EngineWinner(c, ownerElevationM, out score);
            bool tie = best < 0;
            head = LiftTowerHead.Line;
            float specY = c.Ladder[0].TopM;
            for (int t = 0; t < c.Twins.Length; t++)
            {
                float s = EngineScore(c.Twins[t].TopM, ownerElevationM, specY);
                if (s > score + 1e-6f)
                {
                    score = s;
                    best = c.Twins[t].Member;
                    head = c.Twins[t].Head;
                    tie = false;
                }
                else if (math.abs(s - score) <= 1e-6f)
                {
                    tie = true;
                }
            }
            return tie ? -1 : best;
        }

        /// <summary>SubObjectSystem placeholder score (Standalone pillar, :2189-2199) of a member with top <paramref name="topM"/>.</summary>
        public static float EngineScore(float topM, float ownerElevationM, float specY)
        {
            float spare = topM - (ownerElevationM + specY);
            return 1f / (1f + (spare >= 0f ? 2f * spare : -spare));
        }

        /// <summary>The member the engine spawns at an owner elevation: the highest score. Ties give -1 (the engine would draw).</summary>
        public static int EngineWinner(LiftTowerPlanConstants c, float ownerElevationM, out float score)
        {
            float specY = c.Ladder[0].TopM;
            int best = -1;
            score = -1f;
            bool tie = false;
            for (int m = 0; m < c.Ladder.Length; m++)
            {
                float s = EngineScore(c.Ladder[m].TopM, ownerElevationM, specY);
                if (s > score + 1e-6f)
                {
                    score = s;
                    best = m;
                    tie = false;
                }
                else if (math.abs(s - score) <= 1e-6f)
                {
                    tie = true;
                }
            }
            return tie ? -1 : best;
        }

        /// <summary>NetUtils.StraightCurve(a, d, hanging) (NetUtils.cs:178-191): the inner control points of a power-line edge.</summary>
        public static void HangingCurve(float3 a, float3 d, float hanging, out float3 b, out float3 c)
        {
            b = math.lerp(a, d, 1f / 3f);
            c = math.lerp(a, d, 2f / 3f);
            float drop = math.distance(a.xz, d.xz) * hanging * 1.3333334f;
            b.y -= drop;
            c.y -= drop;
        }

        /// <summary>The y of HangingCurve(a, d, hanging) at xz fraction <paramref name="t"/> (its control points are evenly spaced in
        /// xz, so the bezier parameter is the xz fraction): lerp(a.y, d.y, t) - 3·t·(1-t)·drop.</summary>
        public static float HangingY(float3 a, float3 d, float hanging, float t)
        {
            float drop = math.distance(a.xz, d.xz) * hanging * 1.3333334f;
            return math.lerp(a.y, d.y, t) - 3f * t * (1f - t) * drop;
        }

        // A lift line is a FlattenTerrain power line. TerrainSystem cuts the terrain down to a ceiling of edge y + m_HeightRange.min +
        // m_MinHeightOffset (AddSegment, TerrainSystem.cs:1397-1516) and never fills. Our pieces have min 4 (DQ 3.8, tram 11.75) and 0 =
        // edge y + 4. The cut is live (DrawHeightAdjustments, :4727-4800), and the CPU heights the fit samples read back from the cascade
        // (:3598-3646). A vanilla line drawn on the ground therefore leaves a trench the fit would take for ground and stand towers in (and
        // refill into pits around them).
        // Rule: a sample at this line's own cut ceiling (within OwnCutTolM) is replaced by the plane through the ground just outside the
        // cut (lateral +- (half width + CutFarMarginM)), when that plane is higher. Natural ground is never exactly at the ceiling. Ground
        // below the ceiling (a valley the rope crosses) is the player's and is kept. More: EngineNotes/LiftTowerPlanner.md

        public const float OwnCutTolM = 0.3f;
        /// <summary>Lateral margin beyond the piece half width for the uncut reference samples. The cut's smoothing width lives in the
        /// compiled terrain shader: UNVERIFIED (8 m is the allowance TerrainSystem subtracts from the smoothing width when it culls lots,
        /// :899). Too small = the reference is cut too and the raw sample stands (no harm). Too large = the plane spans more of a cross
        /// slope's curvature.</summary>
        public const float CutFarMarginM = 8f;
        /// <summary>NetTerrainPiece m_MinHeightOffset of all four line pieces (gamedata-mirror Ski*Piece: (0, 0, 0). It was -2 under the
        /// old 6..20 box and was raised with the 2026-11-05 collision box round, so the cut stays at edge y + 4). The fit reads the edge's
        /// TerrainComposition when it has one.</summary>
        public const float PieceMinHeightOffsetM = 0f;

        /// <summary>The ground at a sample: <paramref name="raw"/> unless it sits at <paramref name="ceiling"/> (our own cut), then
        /// the higher of raw and the plane through <paramref name="farLeft"/> / <paramref name="farRight"/> (sampled at
        /// ±<paramref name="farM"/>) at <paramref name="lateral"/>.</summary>
        public static float UncutGround(float raw, float ceiling, float farLeft, float farRight, float lateral, float farM, out bool cut)
        {
            cut = false;
            if (!(math.abs(raw - ceiling) <= OwnCutTolM) || farM <= 0f)
                return raw;
            float plane = math.lerp(farLeft, farRight, math.saturate((lateral + farM) / (2f * farM)));
            if (plane <= raw + OwnCutTolM)
                return raw;
            cut = true;
            return plane;
        }

        /// <summary><paramref name="raw"/> with UncutGround applied against the line's own cut. The line runs through the node points
        /// <paramref name="pts"/> (xz distance <paramref name="cum"/> from the first), each span a HangingCurve with
        /// <paramref name="hanging"/>, its ceiling <paramref name="cutOffset"/>[span] above the curve. The uncut reference is sampled at
        /// ±<paramref name="farM"/>. <paramref name="onCut"/> (optional) gets the along of every replaced sample.</summary>
        public static LiftTerrainSampler UncutSampler(LiftTerrainSampler raw, float3[] pts, float[] cum, float hanging, float[] cutOffset,
                                                      float farM, Action<float> onCut = null)
        {
            int n = pts.Length;
            return (along, lateral) =>
            {
                float r = raw(along, lateral);
                if (n < 2)
                    return r;
                int seg = 0;
                while (seg < n - 2 && along > cum[seg + 1])
                    seg++;
                float t = math.saturate((along - cum[seg]) / math.max(cum[seg + 1] - cum[seg], 1e-3f));
                float ceiling = HangingY(pts[seg], pts[seg + 1], hanging, t) + cutOffset[math.min(seg, cutOffset.Length - 1)];
                float g = UncutGround(r, ceiling, raw(along, -farM), raw(along, farM), lateral, farM, out bool cut);
                if (cut)
                    onCut?.Invoke(along);
                return g;
            };
        }
    }

    /// <summary>Tower numbers along a lift line. The number on a tower's plate is its height class baked into the prefab texture
    /// (assets/blender/lift_tower.py:4146-4152 plate_numeral: FG8 -> 8, DQ12 -> 12, GD20 -> 20). A line that mixes members would therefore
    /// read 12, 16, 20, 12, ... instead. The order rule: tower n = the n-th line tower by distance along the line from the base station, 1
    /// at the base, dense (nodes without a tower are skipped). The numbers come from geometry only, so they are the same after a save /
    /// load. An edit renumbers only the towers after the first changed one.</summary>
    public static class LiftTowerNumbering
    {
        /// <summary>Numbers 1..N for towers at <paramref name="alongFromBaseM"/> (meters from the base station, any order). Ties keep the
        /// input order. <paramref name="baseIsFirst"/> false = the distances are from the TOP station (numbers are reversed).</summary>
        public static int[] Numbers(IList<float> alongFromBaseM, bool baseIsFirst = true)
        {
            int n = alongFromBaseM == null ? 0 : alongFromBaseM.Count;
            var idx = new int[n];
            for (int i = 0; i < n; i++)
                idx[i] = i;
            Array.Sort(idx, (x, y) =>
            {
                int c = alongFromBaseM[x].CompareTo(alongFromBaseM[y]);
                if (!baseIsFirst)
                    c = -c;
                return c != 0 ? c : x.CompareTo(y);
            });
            var num = new int[n];
            for (int r = 0; r < n; r++)
                num[idx[r]] = r + 1;
            return num;
        }
    }

    public sealed class LiftTowerPlanner
    {
        private const float Tol = 0.01f;
        private const int MaxDepth = 40;

        private LiftTowerPlanConstants m_C;
        private float m_Length;
        private int m_N;                      // samples 0..m_N (m_N + 1 values), S[m_N] = length
        private float[] m_S = new float[0], m_Centre = new float[0], m_High = new float[0], m_Low = new float[0], m_Floor = new float[0];
        private readonly List<float> m_Along = new List<float>();
        private float m_RopeA, m_RopeB;
        private bool m_UseSag = true;

        /// <summary>Objective switch. false (default) = fewest towers: an exact search for the fewest towers with every span <= MaxSpanM
        /// and clear with its real sag, tall members wherever one saves a tower. The next ranks are the fewest ladder steps, then even
        /// spans. true = the legacy objective (anchors, then infill at the typical span, shortest members). It is kept for the before /
        /// after replays (LiftBatch LP-17) and as the fallback where no clear arrangement exists.</summary>
        public bool LegacySpacing;

        /// <summary>Lift-line tool: spots the user removed a tower from (meters along). The fewest-towers search puts a NEW tower
        /// within ForbiddenRadiusM of one only where the line has no clear plan without it (a seed there is still kept). Null / empty = none.</summary>
        public IList<float> Forbidden;
        public float ForbiddenRadiusM = 10f;

        /// <summary>Lift-line tool: stretches (x..y, meters along from the caller's A) where a road, track or path runs under the line
        /// (LiftLineEdit.KeepoutOf: within its half width plus the tower head's half width plus KeepoutClearM). Soft, like Forbidden: the
        /// planner puts no NEW tower inside one unless the line has no clear plan without it. In that case vanilla reads the tower on the
        /// road as Overlapping items, and the player moves the road or a tower. Seeds (pins) are kept. Null / empty = none, and the plan is
        /// bit-identical.</summary>
        public IList<float2> Keepouts;

        /// <summary>The ends are stations (default: both, the planner's original assumption). A station end gets the approach floor
        /// (LiftTowerPlanConstants.ApproachFloor) and the station-side member rule (MinCost.EndRiseM, TowerWorthM). False = a loose line
        /// end or a fresh ground node: a tower node, full floor. The caller sets it before each Plan / Fit (the lift tool from its end
        /// kinds, LiftTowerFitSystem from the end node's Owner: a station sub-net stub).</summary>
        public bool StationA = true, StationB = true;

        /// <summary>A built line's tower positions (meters along) whose ground the caller samples at the tower's FOOT
        /// (LiftTowerEdit.BuiltFootSampler, FIX-LIST 57). That foot already holds a tram tower's pedestal float, so SetFloats adds none
        /// there (the float would count twice and raise a built tower on every re-fit). Null = none. LiftTowerFitSystem sets it per
        /// line.</summary>
        public IList<float> FootAlong;

        /// <summary>Lift-line tool: plan with the engine's edge slope rule (LiftTowerPlanConstants.MaxEdgeSlope) between the nodes as they
        /// will be built. Towers stand at ground + E(member). The ends stand at EndNodeYA / EndNodeYB (NaN = unknown, and that end's span
        /// is not checked). Off for the fit: a built line's edges are permanent, and the engine validates Temp edges only.</summary>
        public bool CheckEdgeSlope;
        /// <summary>Trade a tower for shorter towers beside the stations (LiftTowerPlanConstants.TowerWorthM). Fit turns it off for its
        /// free plan, whose tower count says how many nodes the line needs.</summary>
        public bool StationRule = true;
        public float EndNodeYA = float.NaN, EndNodeYB = float.NaN;

        /// <summary>Phase (d) breakover / hold-down pairs (breakover-holddown-plan-2026-11-03, section 2.3): a tower whose rope deflection
        /// is past its train's cap is shifted (within AngleShiftM, any member) or replaced by a pair PairMinM..PairMaxM apart. This is for
        /// the lift-line tool's plans only (LiftLineEdit.Replan). It is off by default. Fit forces it off for its free plan, so the fit
        /// never adds a tower (it keeps the engine's node count) and Fit.Needed is unchanged. It has no effect on a type without trains
        /// (the tram). Build sets kinds and angles on every plan.</summary>
        public bool Pairs;

        /// <summary>The fit's hysteresis inputs, one per inner node of the PlanFixed / Fit call (the line's nodes as they stand): the kind
        /// each tower had (LiftTrainKind.None = unknown) and the head it carries. Only the fixed-node plans at exactly those positions use
        /// them (PlanFixed, Fit's Kept). Plan and Fit's Moved ignore them. Null = none (the tool's plans).</summary>
        public IList<LiftTrainKind> PreviousKinds;
        public IList<LiftTowerHead> PreviousHeads;

        /// <summary>Evaluate the heads as given (PreviousHeads, Line where none is given) instead of choosing them: the "before" of a
        /// line as it stands (LiftTowerFitSystem). Kinds are still classified, with hysteresis.</summary>
        public bool HeadsAsGiven;

        /// <summary>Sample index -> index into PreviousKinds / PreviousHeads for the plan being built (fixed-node plans only).</summary>
        private readonly Dictionary<int, int> m_PrevAt = new Dictionary<int, int>();

        /// <summary>A non-station end that joins a built line (a loose end). The value is the built neighbor's rope point beyond it, as the
        /// xz distance from the end node and the rope y there. So the junction tower is judged too (NaN = unknown: that end is not judged).
        /// The tool sets them before each Plan. The fit never does (its path ends are stations or open line ends).</summary>
        public float EndOuterDistA = float.NaN, EndOuterRopeA = float.NaN, EndOuterDistB = float.NaN, EndOuterRopeB = float.NaN;

        /// <summary>World keys of the two ends (the lift tool: their xz), used only to order two ends whose ropes are exactly equal
        /// (PlansFromB). NaN = none: such a line is planned from A.</summary>
        public float2 EndKeyA = new float2(float.NaN), EndKeyB = new float2(float.NaN);

        /// <summary>Plan works from end B: the end it works from has the lower rope (the base). With equal ropes, the smaller world key (x,
        /// then z) decides, when both ends have one. The planner's search runs from the end it works from. Every tie and grid remainder
        /// resolves toward that end (its sample grid starts there, the search keeps the first of equal arrangements, the station rule's
        /// extra tower goes to the first station segment). Therefore it always works from the same end of a line and mirrors the plan back:
        /// the same line gives the same plan, mirrored, whichever end is A (planner-direction-2026-11-04). Exact comparisons: the same two
        /// floats swap with the ends, so the answer flips exactly.</summary>
        public static bool PlansFromB(float ropeA, float ropeB, float2 keyA, float2 keyB)
        {
            if (ropeA != ropeB)
                return ropeB < ropeA;
            if (!math.all(math.isfinite(keyA)) || !math.all(math.isfinite(keyB)))
                return false;
            return keyA.x != keyB.x ? keyB.x < keyA.x : keyB.y < keyA.y;
        }

        /// <summary>True while Plan works from end B: the working arrays are in B's frame (along from B), and the angle notes name the
        /// caller's positions (NoteAt / NoteNode).</summary>
        private bool m_FromB;

        /// <summary>Test hook (LiftBatch DirectionTests): Plan works from A as given, the behavior before 2026-11-04, so the direction
        /// sweep shows that it binds (the plans differ by direction without the rule).</summary>
        internal bool AsGiven { get; set; }

        // The working node list: sample index, member (-1 station), flags. Kept sorted by sample.
        private readonly List<int> m_K = new List<int>();
        private readonly List<int> m_M = new List<int>();
        private readonly List<LiftPlanNodeFlags> m_F = new List<LiftPlanNodeFlags>();

        // Crossings with built lifts of another type (FIX-LIST 60 a). LiftPlanCrossing has the engine's box rule.

        /// <summary>Lift-line tool: the crossings of this line with built lifts of ANOTHER type (meters along from the caller's A).
        /// Plan keeps each one's box clearance as a span constraint (SpanInterval / SpanViolation, so every phase keeps it) and keeps
        /// new towers out of the crossed corridor (NearStation). Null / empty = none: the plan is bit-identical to a plan without
        /// this field. PlanFixed and the fit ignore it.</summary>
        public IList<LiftPlanCrossing> Crossings;

        /// <summary>Safety margin on top of the box clearance (meters).</summary>
        public const float CrossMarginM = 0.5f;
        /// <summary>A crossing flatter than this (plan angle, degrees) is refused. The overlap of the two strips grows with 1 / sin(angle)
        /// (at 20 deg two 6 m chair strips overlap over 34 m of each line). The height the slope terms ask grows in the same way.</summary>
        public const float CrossMinAngleDeg = 20f;
        /// <summary>Clearance (meters) beyond the crossed tower's head half width and the drawn strip's reach along the crossed line:
        /// a crossing nearer a crossed tower than that is refused (refusal (c)).</summary>
        public const float CrossTowerClearM = 3f;
        /// <summary>No new tower stands within the crossed strip plus this (meters, beyond both half widths, measured across the
        /// crossed line): a tower under another lift's rope.</summary>
        public const float CrossBandClearM = 2f;

        /// <summary>Plan-view reach of the two strips' overlap at a crossing: <paramref name="alongNewM"/> along the drawn line (either
        /// side of the crossing point) and <paramref name="alongCrossedM"/> along the crossed one. Strips of widths
        /// <paramref name="newWidthM"/> and <paramref name="crossedWidthM"/> at <paramref name="angleDeg"/> overlap in a parallelogram
        /// whose corners lie (wC + wN cos) / (2 sin) along the drawn line and (wN + wC cos) / (2 sin) along the crossed one.</summary>
        public static void CrossReach(float angleDeg, float newWidthM, float crossedWidthM, out float alongNewM, out float alongCrossedM)
        {
            float a = math.radians(math.clamp(angleDeg, 1f, 90f));
            float sin = math.sin(a), cos = math.abs(math.cos(a));
            alongNewM = (crossedWidthM + newWidthM * cos) / (2f * sin);
            alongCrossedM = (newWidthM + crossedWidthM * cos) / (2f * sin);
        }

        /// <summary>Meters along the drawn line, either side of a crossing, where no new tower stands: the crossed strip (half its
        /// width) plus the drawn line's own half width plus CrossBandClearM, measured across the crossed line.</summary>
        public static float CrossBandM(float angleDeg, float newWidthM, float crossedWidthM) =>
            (0.5f * crossedWidthM + 0.5f * newWidthM + CrossBandClearM) / math.sin(math.radians(math.clamp(angleDeg, 1f, 90f)));

        /// <summary>The refusals known before planning (FIX-LIST 60 a). (d) too shallow. (b) inside a station's run-out (the drawn line's
        /// station ends: <paramref name="c"/>.StationClearM, and the crossed line's stations: CrossedClearM). (c) at or beside a crossed
        /// tower (nearer than the drawn strip's reach along the crossed line + the crossed head's half width + CrossTowerClearM). None =
        /// the planner plans it (a refusal (a) can still follow from the plan).</summary>
        public static LiftCrossRefusal CrossRefusalBeforePlan(LiftPlanCrossing x, LiftTowerPlanConstants c, float lengthM, bool stationA,
                                                              bool stationB)
        {
            if (x.CrossedType != null && x.CrossedType == c.TypeId)
                return LiftCrossRefusal.SameType;
            if (!(x.AngleDeg >= CrossMinAngleDeg))
                return LiftCrossRefusal.Shallow;
            if ((stationA && x.AlongM < c.StationClearM) || (stationB && lengthM - x.AlongM < c.StationClearM) ||
                x.StationDistM < x.CrossedClearM)
                return LiftCrossRefusal.Station;
            CrossReach(x.AngleDeg, 2f * c.HalfWidthM, x.CrossedWidthM, out _, out float alongCrossed);
            if (x.TowerDistM < alongCrossed + x.CrossedHeadHalfM + CrossTowerClearM)
                return LiftCrossRefusal.Tower;
            return LiftCrossRefusal.None;
        }

        /// <summary>The side per caller crossing for the plan being made (None = refused before planning, not a constraint). It is null
        /// outside Plan.</summary>
        private LiftCrossSide[] m_XSides;
        private LiftCrossRefusal[] m_XBefore;
        // The active crossings in the working frame (PrepareCrossings): count, along, side, crossed y, reach along the drawn line,
        // the over / under thresholds on the drawn footprint's lowest / highest point, the tower-free band.
        private int m_XN;
        private float[] m_XS = new float[0], m_XEN = new float[0], m_XR = new float[0], m_XBand = new float[0];
        private bool[] m_XOver = new bool[0];

        /// <summary>Plan a line of <paramref name="lengthM"/> meters (xz) between two stations whose rope heights are given.
        /// <paramref name="seeds"/> (optional) are kept as anchors with their members, e.g. a previous plan's nodes. A seed with a member below 0 (a
        /// tool pin: the player sets where it stands, the planner how tall) gets its member from ChooseSeedMembers. When PlansFromB, the
        /// plan is made from end B and mirrored back (the result is always along from A). Every per-end input goes with its end (ropes,
        /// StationA / B, EndNodeY, EndOuter, the seeds and Forbidden mirrored, the ground sampled from B). <paramref name="terrainFromB"/>
        /// (optional) is the same ground sampled from B (along from B, lateral to the right of B to A). The planner then reads exactly the
        /// ground points a line drawn from B first reads (the lift tool passes it). Null = the planner mirrors <paramref name="terrain"/>,
        /// the same points to float rounding.
        /// With Crossings: every crossing not refused before planning is planned OVER first. Then, one at a time, UNDER is planned where
        /// the crossed line is high enough over the ground for this line's carriers to pass below it (UnderRoom). The under plan is kept
        /// when it is better (CrossBetter: fewer refused crossings, then clear spans, then fewer towers, then shorter ones).</summary>
        public LiftTowerPlan Plan(LiftTerrainSampler terrain, float lengthM, float ropeA, float ropeB, LiftTowerPlanConstants c,
                                  IList<LiftPlanNode> seeds = null, LiftTerrainSampler terrainFromB = null)
        {
            if (terrain == null || c == null || c.Ladder == null || c.Ladder.Length == 0)
                throw new ArgumentException("LiftTowerPlanner.Plan: terrain, constants and a ladder are required");
            if (Crossings == null || Crossings.Count == 0)
                return PlanOnce(terrain, lengthM, ropeA, ropeB, c, seeds, terrainFromB);
            int n = Crossings.Count;
            var sides = new LiftCrossSide[n];
            m_XBefore = new LiftCrossRefusal[n];
            for (int i = 0; i < n; i++)
            {
                m_XBefore[i] = CrossRefusalBeforePlan(Crossings[i], c, lengthM, StationA, StationB);
                sides[i] = m_XBefore[i] == LiftCrossRefusal.None ? LiftCrossSide.Over : LiftCrossSide.None;
            }
            try
            {
                m_XSides = sides;
                LiftTowerPlan best = PlanOnce(terrain, lengthM, ropeA, ropeB, c, seeds, terrainFromB);
                for (int i = 0; i < n; i++)
                {
                    if (sides[i] != LiftCrossSide.Over || UnderRoom(Crossings[i], c, terrain) < 0f)
                        continue;
                    var trial = (LiftCrossSide[])sides.Clone();
                    trial[i] = LiftCrossSide.Under;
                    m_XSides = trial;
                    LiftTowerPlan p = PlanOnce(terrain, lengthM, ropeA, ropeB, c, seeds, terrainFromB);
                    if (CrossBetter(p, best))
                    {
                        best = p;
                        sides = trial;
                    }
                }
                // A crossing neither side can meet is refused (Clearance). Its constraint is dropped and the line is planned without it.
                // The preview then shows the line as it would stand, rather than a search's last resort (a row of the tallest towers). The
                // refused record (side tried, separation required and reached) is the constrained plan's. The same applies when every
                // crossing is met, but only by a plan that is no longer clear while the line without them is clear. The crossings cost the
                // line its clearance, so they are refused (Clearance) instead.
                var refuse = new bool[n];
                bool any = false;
                for (int i = 0; i < n && i < best.Crossings.Length; i++)
                    if (best.Crossings[i].Refusal == LiftCrossRefusal.Clearance)
                        refuse[i] = any = true;
                LiftTowerPlan relaxed = null;
                if (any)
                {
                    for (int i = 0; i < n; i++)
                        if (refuse[i])
                            sides[i] = LiftCrossSide.None;
                    m_XSides = sides;
                    relaxed = PlanOnce(terrain, lengthM, ropeA, ropeB, c, seeds, terrainFromB);
                }
                else if (!best.Clear)
                {
                    m_XSides = new LiftCrossSide[n];
                    LiftTowerPlan free = PlanOnce(terrain, lengthM, ropeA, ropeB, c, seeds, terrainFromB);
                    if (free.Clear)
                    {
                        relaxed = free;
                        for (int i = 0; i < n && i < best.Crossings.Length; i++)
                            if (best.Crossings[i].Side != LiftCrossSide.None && best.Crossings[i].Refusal == LiftCrossRefusal.None)
                            {
                                best.Crossings[i].Refusal = LiftCrossRefusal.Clearance;
                                refuse[i] = true;
                            }
                    }
                }
                if (relaxed == null)
                    return best;
                for (int i = 0; i < n && i < best.Crossings.Length; i++)
                    if (refuse[i])
                        relaxed.Crossings[i] = best.Crossings[i];
                return relaxed;
            }
            finally
            {
                m_XSides = null;
                m_XBefore = null;
                m_XN = 0;
            }
        }

        /// <summary>Room (meters) for this line's carriers under the crossed line's box at the crossing, on level rope. It is the highest
        /// rope the under side allows there (the box rule, RopeAboveBase(0) over the node chord) minus the lowest rope the floor allows
        /// (the high side + carrier drop + FloorM). Below 0 the under side cannot work, so it is not tried.</summary>
        private float UnderRoom(LiftPlanCrossing x, LiftTowerPlanConstants c, LiftTerrainSampler terrain)
        {
            float s = x.AlongM, hw = c.HalfWidthM;
            float high = terrain(s, 0f);
            if (hw > 0f)
                high = math.max(high, math.max(terrain(s, -hw), terrain(s, hw)));
            CrossReach(x.AngleDeg, 2f * c.HalfWidthM, x.CrossedWidthM, out _, out float eC);
            float top = x.CrossedY - math.abs(x.CrossedSlope) * eC - (c.NetHeightMaxM - x.CrossedMinM) - CrossMarginM - c.MaxTwinRiseM;
            return top + c.RopeAboveBase(0) - (high + c.CarrierDropM + c.FloorM);
        }

        /// <summary>Plan <paramref name="p"/> is better than <paramref name="q"/> for the crossings: fewer refused crossings, then every
        /// span clear, then fewer towers, then fewer ladder steps. Ties keep <paramref name="q"/> (the over side).</summary>
        private static bool CrossBetter(LiftTowerPlan p, LiftTowerPlan q)
        {
            int rp = p.CrossingsRefused, rq = q.CrossingsRefused;
            if (rp != rq) return rp < rq;
            if (p.Clear != q.Clear) return p.Clear;
            if (p.LineTowers != q.LineTowers) return p.LineTowers < q.LineTowers;
            return Steps(p) < Steps(q);
        }

        private static int Steps(LiftTowerPlan p)
        {
            int s = 0;
            for (int i = 0; i < p.Nodes.Length; i++)
                s += math.max(0, p.Nodes[i].Member);
            return s;
        }

        /// <summary>One plan (the body of Plan before the crossings): from the end with the lower rope, mirrored back.</summary>
        private LiftTowerPlan PlanOnce(LiftTerrainSampler terrain, float lengthM, float ropeA, float ropeB, LiftTowerPlanConstants c,
                                       IList<LiftPlanNode> seeds, LiftTerrainSampler terrainFromB)
        {
            if (AsGiven || !PlansFromB(ropeA, ropeB, EndKeyA, EndKeyB))
                return PlanCore(terrain, lengthM, ropeA, ropeB, c, seeds);
            float len = math.max(lengthM, 0.1f);
            IList<float> forbidden = Forbidden;
            IList<float2> keepouts = Keepouts;
            List<LiftPlanNode> mirrored = null;
            if (seeds != null)
            {
                mirrored = new List<LiftPlanNode>(seeds.Count);
                for (int i = seeds.Count - 1; i >= 0; i--)   // reversed: B's frame keeps the caller's order along the line
                {
                    LiftPlanNode s = seeds[i];
                    s.AlongM = len - s.AlongM;
                    mirrored.Add(s);
                }
            }
            if (forbidden != null)
            {
                var f = new float[forbidden.Count];
                for (int i = 0; i < f.Length; i++)
                    f[i] = len - forbidden[i];
                Forbidden = f;
            }
            if (keepouts != null)
            {
                var ko = new float2[keepouts.Count];
                for (int i = 0; i < ko.Length; i++)
                    ko[i] = new float2(len - keepouts[i].y, len - keepouts[i].x);
                Keepouts = ko;
            }
            SwapEnds();
            m_FromB = true;
            try
            {
                LiftTerrainSampler fromB = terrainFromB ?? ((s, l) => terrain(len - s, -l));
                return Mirrored(PlanCore(fromB, lengthM, ropeB, ropeA, c, mirrored));
            }
            finally
            {
                m_FromB = false;
                SwapEnds();
                Forbidden = forbidden;
                Keepouts = keepouts;
            }
        }

        /// <summary>Swaps the per-end inputs (Plan working from B).</summary>
        private void SwapEnds()
        {
            (StationA, StationB) = (StationB, StationA);
            (EndNodeYA, EndNodeYB) = (EndNodeYB, EndNodeYA);
            (EndOuterDistA, EndOuterDistB) = (EndOuterDistB, EndOuterDistA);
            (EndOuterRopeA, EndOuterRopeB) = (EndOuterRopeB, EndOuterRopeA);
        }

        /// <summary>A plan made from B, as seen from A. Nodes, spans and the profile are reversed, along = length - along (the "at" offsets
        /// inside a span likewise), and the per-end fields are swapped back. Every other number is the same (a deflection reads the same
        /// either way along the line, LiftAlignMath.DeflectionDeg).</summary>
        private static LiftTowerPlan Mirrored(LiftTowerPlan p)
        {
            float len = p.LengthM;
            int n = p.Nodes.Length;
            var nodes = new LiftPlanNode[n];
            for (int i = 0; i < n; i++)
            {
                nodes[i] = p.Nodes[n - 1 - i];
                nodes[i].AlongM = len - nodes[i].AlongM;
                nodes[i].AnchorAlongM = len - nodes[i].AnchorAlongM;
                nodes[i].PitchDeg = -nodes[i].PitchDeg;   // up toward the caller's B
            }
            int sc = p.Spans.Length;
            var spans = new LiftPlanSpan[sc];
            for (int i = 0; i < sc; i++)
            {
                LiftPlanSpan s = p.Spans[sc - 1 - i];
                s.MinFloorMarginAtM = s.LengthM - s.MinFloorMarginAtM;
                s.MaxRopeAboveAtM = s.LengthM - s.MaxRopeAboveAtM;
                spans[i] = s;
            }
            int q = p.SampleAlongM.Length;
            var along = new float[q];
            var center = new float[q];
            var high = new float[q];
            var floor = new float[q];
            for (int i = 0; i < q; i++)
            {
                along[i] = len - p.SampleAlongM[q - 1 - i];
                center[i] = p.SampleCentreM[q - 1 - i];
                high[i] = p.SampleHighM[q - 1 - i];
                floor[i] = p.SampleFloorM[q - 1 - i];
            }
            return new LiftTowerPlan
            {
                Constants = p.Constants, LengthM = len, Nodes = nodes, Spans = spans,
                SampleAlongM = along, SampleCentreM = center, SampleHighM = high, SampleFloorM = floor,
                StationA = p.StationB, StationB = p.StationA, EndCarrierA = p.EndCarrierB, EndCarrierB = p.EndCarrierA,
                AnglesMs = p.AnglesMs, AngleWork = p.AngleWork, AngleNotes = p.AngleNotes,
                Crossings = p.Crossings,   // Build writes them in the caller's frame
            };
        }

        /// <summary>An angle note's position: meters along from the caller's A.</summary>
        private string NoteAt(float s) => LiftTowerPlan.F(m_FromB ? m_Length - s : s, "F0");

        /// <summary>An angle note's node number (1-based, from the caller's A) of working node <paramref name="n"/>.</summary>
        private int NoteNode(int n) => m_FromB ? m_K.Count - n : n + 1;

        /// <summary>Plan in the working frame (along from its A): phases (a+b), (c), (d), then the output.</summary>
        private LiftTowerPlan PlanCore(LiftTerrainSampler terrain, float lengthM, float ropeA, float ropeB, LiftTowerPlanConstants c,
                                       IList<LiftPlanNode> seeds)
        {
            m_C = c;
            m_Length = math.max(lengthM, 0.1f);
            m_RopeA = ropeA;
            m_RopeB = ropeB;
            ResetAngles();
            Sample(terrain);
            PrepareCrossings();

            m_K.Clear(); m_M.Clear(); m_F.Clear();
            m_K.Add(0); m_M.Add(-1); m_F.Add(LiftPlanNodeFlags.Station);
            m_K.Add(m_N); m_M.Add(-1); m_F.Add(LiftPlanNodeFlags.Station);
            m_ExtraTowers = 1;
            bool chooseMembers = false;
            if (seeds != null)
            {
                for (int i = 0; i < seeds.Count; i++)
                {
                    if (seeds[i].IsStation)
                        continue;
                    int k = (int)math.round(seeds[i].AlongM / c.StepM);
                    if (k <= 0 || k >= m_N || IndexOf(k) >= 0)
                        continue;
                    int m = math.clamp(seeds[i].Member, 0, c.Ladder.Length - 1);
                    Insert(k, m, (seeds[i].Flags & ~LiftPlanNodeFlags.Station) | LiftPlanNodeFlags.Seed);
                    chooseMembers |= seeds[i].Member < 0;
                }
            }
            if (chooseMembers && !LegacySpacing)
                ChooseSeedMembers();

            // (a+b) fewest towers: per segment (station to station, or between seeds) the exact search. Where it finds no clear
            // arrangement, the legacy phases (a) anchors / greedy split + (b) infill minimize what is left.
            for (int i = 0; i < m_K.Count - 1; )
            {
                int added = LegacySpacing ? -1 : MinTowers(i, i + 1);
                if (added < 0 && !LegacySpacing && ((Forbidden != null && Forbidden.Count > 0) || (Keepouts != null && Keepouts.Count > 0)))
                {
                    // a removed spot or a road stretch yields where the line has no clear plan without a tower there (LP-32, KO-1).
                    m_IgnoreForbidden = true;
                    added = MinTowers(i, i + 1);
                }
                if (added < 0 && !LegacySpacing)
                {
                    // the station clearance yields only where the ground leaves no clear plan without a tower inside it. The 1,408 m
                    // gondola's top station is such a case: it sits on a crest, and the ground 8 m in front of it is at station level
                    // (LP-22). Such a tower is flagged NearStation.
                    m_IgnoreForbidden = true;
                    m_NoClearance = true;
                    added = MinTowers(i, i + 1);
                    m_NoClearance = false;
                    for (int q = i + 1; q <= i + added; q++)
                        if (NearStation(m_K[q]))
                            m_F[q] |= LiftPlanNodeFlags.NearStation;
                }
                m_IgnoreForbidden = false;
                if (added < 0)
                    added = LegacySpacing ? LegacySegment(i) : FallbackSegment(i);
                i += added + 1;
            }

            // (c) elevation.
            AssignMembers();

            // (d) rope angles: breakover / hold-down pairs (tool plans only, and Build sets every node's angles and kind).
            if (Pairs && (c.HasTrains || math.isfinite(c.PitchCapDeg)))
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                AnglePass();
                m_AnglesMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }

            // (e) lean: a span the leaned anchors leave short gets a taller tower.
            LeanPass();
            return Build();
        }

        // Profile

        private void Sample(LiftTerrainSampler terrain) => Sample(terrain, null);

        /// <summary>The profile: one sample every StepM (the last at the line's end), plus one at each of <paramref name="extra"/>
        /// (fixed node positions: a node off the grid gets its own sample, so its tower base and rope are exact). Plan()
        /// passes none, which keeps its grid (k * StepM) unchanged.</summary>
        private void Sample(LiftTerrainSampler terrain, IList<float> extra)
        {
            float step = m_C.StepM;
            int grid = math.max(1, (int)math.ceil(m_Length / step - 1e-4f));
            m_Along.Clear();
            for (int k = 0; k <= grid; k++)
                m_Along.Add(k == grid ? m_Length : k * step);
            if (extra != null)
            {
                for (int e = 0; e < extra.Count; e++)
                {
                    float s = extra[e];
                    if (!(s > 1e-3f && s < m_Length - 1e-3f))
                        continue;
                    int at = m_Along.BinarySearch(s);
                    if (at >= 0)
                        continue;
                    at = ~at;
                    if ((at > 0 && s - m_Along[at - 1] < 1e-3f) || (at < m_Along.Count && m_Along[at] - s < 1e-3f))
                        continue;
                    m_Along.Insert(at, s);
                }
            }
            m_N = m_Along.Count - 1;
            int count = m_N + 1;
            if (m_S.Length < count)
            {
                m_S = new float[count];
                m_Centre = new float[count];
                m_High = new float[count];
                m_Low = new float[count];
                m_Floor = new float[count];
            }
            float hw = m_C.HalfWidthM;
            for (int k = 0; k <= m_N; k++)
            {
                float s = m_Along[k];
                float c = terrain(s, 0f);
                float l = hw > 0f ? terrain(s, -hw) : c;
                float r = hw > 0f ? terrain(s, hw) : c;
                m_S[k] = s;
                m_Centre[k] = c;
                m_High[k] = math.max(c, math.max(l, r));
                m_Low[k] = math.min(c, math.min(l, r));
            }
            // The carrier at each station end above its high side (negative: the station is sunk, see ApproachFloor).
            float atA = m_RopeA - m_C.CarrierDropM - m_High[0], atB = m_RopeB - m_C.CarrierDropM - m_High[m_N];
            for (int k = 0; k <= m_N; k++)
            {
                float floor = m_C.FloorM;
                if (StationA)
                    floor = math.min(floor, m_C.ApproachFloor(m_S[k], atA));
                if (StationB)
                    floor = math.min(floor, m_C.ApproachFloor(m_Length - m_S[k], atB));
                m_Floor[k] = floor;
            }
        }

        /// <summary>The sample index nearest <paramref name="along"/> (fixed-node mode: every node has its own sample).</summary>
        private int SampleIndex(float along)
        {
            int at = m_Along.BinarySearch(along);
            if (at >= 0)
                return at;
            at = ~at;
            if (at <= 0)
                return 0;
            if (at > m_N)
                return m_N;
            return along - m_S[at - 1] <= m_S[at] - along ? at - 1 : at;
        }

        // Fixed nodes (the engine's node count): PlanFixed, Fit

        /// <summary>Members for towers at FIXED positions <paramref name="innerAlongM"/> (meters from station A, ascending, the line's
        /// existing nodes): phase (c) only, no node added or moved. <paramref name="members"/> given and <paramref name="assign"/> false =
        /// evaluate those members as they stand (the "before" of a line). <paramref name="flags"/> go to the output (default
        /// Seed).</summary>
        public LiftTowerPlan PlanFixed(LiftTerrainSampler terrain, float lengthM, float ropeA, float ropeB, LiftTowerPlanConstants c,
                                       IList<float> innerAlongM, IList<int> members = null, IList<LiftPlanNodeFlags> flags = null,
                                       bool assign = true)
        {
            return PlanFixedCore(terrain, lengthM, ropeA, ropeB, c, innerAlongM, members, flags, assign, false);
        }

        /// <summary>PlanFixed. <paramref name="fillSpares"/> (the Moved fit): the given members are kept (the free plan's own, found by the
        /// exact search). A member of -1 marks a spare node, which gets the shortest member that keeps both of its spans clear (otherwise
        /// the least violation). Phase (c) runs only if the result is still violated, and its result is kept when better.</summary>
        private LiftTowerPlan PlanFixedCore(LiftTerrainSampler terrain, float lengthM, float ropeA, float ropeB, LiftTowerPlanConstants c,
                                            IList<float> innerAlongM, IList<int> members, IList<LiftPlanNodeFlags> flags, bool assign,
                                            bool fillSpares)
        {
            if (terrain == null || c == null || c.Ladder == null || c.Ladder.Length == 0 || innerAlongM == null)
                throw new ArgumentException("LiftTowerPlanner.PlanFixed: terrain, constants, a ladder and the node positions are required");
            m_C = c;
            m_Length = math.max(lengthM, 0.1f);
            m_RopeA = ropeA;
            m_RopeB = ropeB;
            ResetAngles();
            Sample(terrain, innerAlongM);
            m_XN = 0;   // the crossings are the tool's (Plan): fixed-node plans never judge them
            m_K.Clear(); m_M.Clear(); m_F.Clear();
            m_K.Add(0); m_M.Add(-1); m_F.Add(LiftPlanNodeFlags.Station);
            m_K.Add(m_N); m_M.Add(-1); m_F.Add(LiftPlanNodeFlags.Station);
            var spares = fillSpares ? new List<int>() : null;
            bool prev = PreviousKinds != null && PreviousHeads != null && PreviousKinds.Count == innerAlongM.Count &&
                        PreviousHeads.Count == innerAlongM.Count;
            for (int i = 0; i < innerAlongM.Count; i++)
            {
                int k = SampleIndex(innerAlongM[i]);
                if (k <= 0 || k >= m_N || IndexOf(k) >= 0)
                    continue;
                if (prev)
                    m_PrevAt[k] = i;
                int given = members != null && i < members.Count ? members[i] : 0;
                if (spares != null && given < 0)
                    spares.Add(k);
                int m = math.clamp(given, 0, c.Ladder.Length - 1);
                LiftPlanNodeFlags f = flags != null && i < flags.Count ? flags[i] & ~LiftPlanNodeFlags.Station : LiftPlanNodeFlags.Seed;
                Insert(k, m, f);
            }
            m_UseSag = true;
            if (spares != null)
            {
                for (int s = 0; s < spares.Count; s++)
                    FillSpare(IndexOf(spares[s]));
                if (TotalViolation() > Tol)
                    AssignMembers();   // restores the members above when phase (c) does no better
            }
            else if (assign)
            {
                AssignMembers();
            }
            if (spares != null || assign)
                LeanPass();
            return Build();
        }

        /// <summary>The member of spare node <paramref name="n"/> (between two nodes whose members are set): the shortest that
        /// keeps its node and both spans clear, else the one with the least summed violation.</summary>
        private void FillSpare(int n)
        {
            if (n <= 0 || n >= m_K.Count - 1)
                return;
            int i = m_K[n - 1], k = m_K[n], j = m_K[n + 1];
            float ri = Rope(i, m_M[n - 1]), rj = Rope(j, m_M[n + 1]);
            int best = 0;
            float bestV = float.PositiveInfinity;
            for (int m = 0; m < m_C.Ladder.Length; m++)
            {
                if (!MemberAllowed(k, m) || !NodeOk(k, m))
                    continue;
                float rk = Rope(k, m);
                float v = math.max(SpanViolation(i, ri, k, rk), 0f) + math.max(SpanViolation(k, rk, j, rj), 0f);
                if (v < bestV - Tol)
                {
                    bestV = v;
                    best = m;
                }
                if (v <= Tol)
                    break;
            }
            m_M[n] = best;
        }

        /// <summary>Fit a line whose node COUNT is fixed (the engine split it, CourseSplitSystem.cs:3485-3493). Adding a node needs a new
        /// course. (1) Kept: heights only at the current positions. (2) Moved (allowed on a straight line): the free plan (spans capped at
        /// the engine's max edge length <paramref name="engineMaxSpanM"/>) when it needs no more towers than the line has. The spare nodes
        /// go to the midpoints of the longest spans. Heights are set by phase (c). (3) Short: neither is clear. The result is the better of
        /// the two by floor margin, and the tower count the free plan needs.</summary>
        public LiftTowerFit Fit(LiftTerrainSampler terrain, float lengthM, float ropeA, float ropeB, LiftTowerPlanConstants c,
                                IList<float> currentInnerAlongM, bool allowMove, float engineMaxSpanM, IList<int> currentMembers = null,
                                IList<bool> spareNodes = null)
        {
            var fit = new LiftTowerFit { Inner = currentInnerAlongM.Count };
            LiftTowerPlanConstants cm = c;
            if (engineMaxSpanM > 0f && engineMaxSpanM < c.MaxSpanM)
                cm = c.With(maxSpanM: engineMaxSpanM, typicalSpanM: math.min(c.TypicalSpanM, engineMaxSpanM));
            bool rule = StationRule, pairs = Pairs;
            StationRule = false;   // the count the line needs is the fewest towers (TowerWorthM would add one for the stations)
            Pairs = false;         // the fit never adds a tower: no breakover pairs in the count it compares with the line's
            fit.Free = Plan(terrain, lengthM, ropeA, ropeB, cm);
            StationRule = rule;
            Pairs = pairs;
            fit.Needed = fit.Free.LineTowers;
            // Hysteresis: the members the line already carries win when they clear it as they stand. Phase (c), run again on ground that
            // moved a few decimeters (our own carve, rounding), can pick an equally clear but different assignment (a long tram flipped
            // between two member mixes on every re-fit). A clear line is final.
            if (currentMembers != null && currentMembers.Count == currentInnerAlongM.Count)
            {
                LiftTowerPlan current = PlanFixed(terrain, lengthM, ropeA, ropeB, c, currentInnerAlongM, currentMembers, null, assign: false);
                if (KeepClear(current))
                {
                    fit.Kept = current;
                    fit.Mode = LiftFitMode.Kept;
                    fit.Plan = current;
                    return fit;
                }
                // A tool line the engine split (LiftToolLines.MatchSplit): the extra split nodes (spareNodes) take the spare fill (the
                // shortest member that keeps both of their spans clear). Every tool node keeps its member. Only when that is still not
                // clear does phase (c) below re-assign the whole line.
                if (spareNodes != null && spareNodes.Count == currentInnerAlongM.Count && spareNodes.Contains(true))
                {
                    var spareMembers = new List<int>(currentMembers.Count);
                    for (int i = 0; i < currentMembers.Count; i++)
                        spareMembers.Add(spareNodes[i] ? -1 : currentMembers[i]);
                    LiftTowerPlan filled = PlanFixedCore(terrain, lengthM, ropeA, ropeB, c, currentInnerAlongM, spareMembers, null, false, true);
                    if (KeepClear(filled))
                    {
                        fit.Kept = filled;
                        fit.Mode = LiftFitMode.Kept;
                        fit.Plan = filled;
                        return fit;
                    }
                }
            }
            fit.Kept = PlanFixed(terrain, lengthM, ropeA, ropeB, c, currentInnerAlongM);
            if (fit.Kept.Clear)
            {
                fit.Mode = LiftFitMode.Kept;
                fit.Plan = fit.Kept;
                return fit;
            }
            if (allowMove && fit.Needed <= fit.Inner && fit.Inner > 0)
            {
                // The free plan's own members travel with its nodes. Phase (c) alone, greedy from the shortest member, missed the exact
                // search's assignment. A 1,408 m gondola was Short with 11 nodes where the free plan needs 9. It was then re-planned on
                // every re-fit until the loop guard. Spares get FillSpare.
                var along = new List<float>();
                var flags = new List<LiftPlanNodeFlags>();
                var members = new List<int>();
                for (int i = 1; i < fit.Free.Nodes.Length - 1; i++)
                {
                    along.Add(fit.Free.Nodes[i].AlongM);
                    flags.Add(fit.Free.Nodes[i].Flags);
                    members.Add(fit.Free.Nodes[i].Member);
                }
                // Spare nodes: the midpoint of the longest span, one at a time (ties: the first).
                while (along.Count < fit.Inner)
                {
                    int best = 0;
                    float bestLen = -1f, prev = 0f;
                    for (int s = 0; s <= along.Count; s++)
                    {
                        float end = s < along.Count ? along[s] : lengthM;
                        if (end - prev > bestLen + 1e-3f)
                        {
                            bestLen = end - prev;
                            best = s;
                        }
                        prev = end;
                    }
                    float start = best == 0 ? 0f : along[best - 1];
                    along.Insert(best, start + 0.5f * bestLen);
                    flags.Insert(best, LiftPlanNodeFlags.Infill);
                    members.Insert(best, -1);
                }
                IList<LiftTrainKind> pk = PreviousKinds;
                IList<LiftTowerHead> ph = PreviousHeads;
                PreviousKinds = null;   // new positions: the line's kinds and heads do not apply
                PreviousHeads = null;
                try
                {
                    fit.Moved = PlanFixedCore(terrain, lengthM, ropeA, ropeB, c, along, members, flags, false, true);
                }
                finally
                {
                    PreviousKinds = pk;
                    PreviousHeads = ph;
                }
                if (fit.Moved.Clear)
                {
                    fit.Mode = LiftFitMode.Moved;
                    fit.Plan = fit.Moved;
                    return fit;
                }
            }
            fit.Mode = LiftFitMode.Short;
            fit.Plan = fit.Moved != null && fit.Moved.MinFloorMarginM > fit.Kept.MinFloorMarginM + Tol ? fit.Moved : fit.Kept;
            return fit;
        }

        /// <summary>A standing line (its own members, hysteresis) is kept when every span is within KeepTolM of the floor and under the max
        /// rope height. The planner's 5 / 10 m grid starts at the path's first node, so the same line sampled from the other end reads the
        /// ground a few decimeters differently. A 1,925 m tram planned from the top (+0.2 m) read -0.1 m when the fit sampled from the
        /// base, and the fit called it Short.</summary>
        public const float KeepTolM = 0.5f;

        private static bool KeepClear(LiftTowerPlan p)
        {
            for (int i = 0; i < p.Spans.Length; i++)
                if (p.Spans[i].HeightViolated || p.Spans[i].MinFloorMarginM < -KeepTolM)
                    return false;
            return true;
        }

        /// <summary>The plan the fit writes over a line (FIX-LIST 52). It is the fit's plan, except over a BUILT line (its nodes carry the
        /// exact elevations of a fit, so <paramref name="asBuilt"/> is the line as it stands, its own members and heads). Over a BUILT
        /// line, when the fit's plan is not sound (a span violated or a tower over its cap) and worse than the line on any axis
        /// (LiftTowerPlan.WorseThan), the result is null and nothing is written. <paramref name="worse"/> says why. A sound plan is always
        /// written. A line the engine or the tool just built (not <paramref name="built"/>) always takes the fit.</summary>
        public static LiftTowerPlan ToWrite(LiftTowerPlan asBuilt, LiftTowerFit fit, bool built, out string worse)
        {
            worse = null;
            if (!built || asBuilt == null || fit.Plan.Sound)
                return fit.Plan;
            worse = LiftTowerPlan.WorseThan(fit.Plan, asBuilt);
            return worse == null ? fit.Plan : null;
        }

        // Span evaluation

        private float Rope(int k, int member)
        {
            if (member < 0)
                return k == 0 ? m_RopeA : m_RopeB;
            return m_Centre[k] + m_C.RopeAboveBase(member);
        }

        // Crossings (FIX-LIST 60 a). The game's box of an edge stands on its curve. The curve is the node-to-node chord lowered by the
        // net's sag 4 d h t (1 - t) (NetUtils.StraightCurve, and the line nets' m_Hanging equals their rope lanes', DECISIONS 587, so h =
        // SagRatio). The chord ends are: a tower node = ground + E(member) = its rope - RopeAboveBase(0), and an end node = EndNodeYA /
        // EndNodeYB.
        // Over the strips' overlap (CrossReach) the curve rises and falls by its slope. So OVER asks the drawn footprint's LOWEST point (y
        // - eN |slope|) to clear the crossed footprint's highest point + (crossed max - drawn min) + CrossMarginM. UNDER asks the drawn
        // footprint's HIGHEST point to stay under the crossed lowest - (drawn max - crossed min) - CrossMarginM - MaxTwinRiseM.
        // Both are piecewise linear in the far node's height (concave / convex). So each is an interval on the far-end rope and joins
        // SpanInterval's floor and max-height bounds. The exact search, phase (c), the angle pass and the lean pass all keep it.

        /// <summary>The working frame's crossings (Plan's sides). None outside Plan.</summary>
        private void PrepareCrossings()
        {
            m_XN = 0;
            if (m_XSides == null || Crossings == null)
                return;
            int n = Crossings.Count;
            if (m_XS.Length < n)
            {
                m_XS = new float[n];
                m_XEN = new float[n];
                m_XR = new float[n];
                m_XBand = new float[n];
                m_XOver = new bool[n];
            }
            float wN = 2f * m_C.HalfWidthM;
            for (int i = 0; i < n && i < m_XSides.Length; i++)
            {
                if (m_XSides[i] == LiftCrossSide.None)
                    continue;
                LiftPlanCrossing x = Crossings[i];
                CrossReach(x.AngleDeg, wN, x.CrossedWidthM, out float eN, out float eC);
                float gC = math.abs(x.CrossedSlope);
                bool over = m_XSides[i] == LiftCrossSide.Over;
                m_XS[m_XN] = m_FromB ? m_Length - x.AlongM : x.AlongM;
                m_XOver[m_XN] = over;
                m_XEN[m_XN] = eN;
                m_XR[m_XN] = over ? x.CrossedY + gC * eC + (x.CrossedMaxM - m_C.NetHeightMinM) + CrossMarginM
                                  : x.CrossedY - gC * eC - (m_C.NetHeightMaxM - x.CrossedMinM) - CrossMarginM - m_C.MaxTwinRiseM;
                m_XBand[m_XN] = CrossBandM(x.AngleDeg, wN, x.CrossedWidthM);
                m_XN++;
            }
        }

        /// <summary>Rope minus node height at sample <paramref name="k"/>: RopeAboveBase(0) at a tower (node = ground + E(member)), the
        /// end's own rope minus EndNodeYA / EndNodeYB at a line end (unknown end height: as a tower).</summary>
        private float NodeOff(int k)
        {
            if (k == 0 && math.isfinite(EndNodeYA))
                return m_RopeA - EndNodeYA;
            if (k == m_N && math.isfinite(EndNodeYB))
                return m_RopeB - EndNodeYB;
            return m_C.RopeAboveBase(0);
        }

        /// <summary>Sample <paramref name="k"/> is inside a crossed lift's corridor (CrossBandM): no new tower there.</summary>
        private bool InCrossBand(int k)
        {
            for (int x = 0; x < m_XN; x++)
                if (math.abs(m_S[k] - m_XS[x]) < m_XBand[x] - Tol)
                    return true;
            return false;
        }

        /// <summary>The worst crossing shortfall (meters, > 0 = the boxes overlap) of the span (i, ri) -> (j, rj) over the crossings
        /// strictly inside it. It is -infinity when it has none. <paramref name="xs"/> / <paramref name="over"/>: that crossing.</summary>
        private float CrossShort(int i, float ri, int j, float rj, out float xs, out bool over)
        {
            float worst = float.NegativeInfinity;
            xs = 0f;
            over = true;
            float si = m_S[i], sj = m_S[j], d = sj - si, h = m_C.SagRatio;
            if (d <= 0f)
                return worst;
            float yi = ri - NodeOff(i), delta = rj - NodeOff(j) - yi;
            for (int x = 0; x < m_XN; x++)
            {
                if (!(m_XS[x] > si && m_XS[x] < sj))
                    continue;
                float t = (m_XS[x] - si) / d;
                float y = yi + delta * t - 4f * d * h * t * (1f - t);
                float foot = m_XEN[x] * math.abs(delta / d - 4f * h * (1f - 2f * t));
                float v = m_XOver[x] ? m_XR[x] - (y - foot) : y + foot - m_XR[x];
                if (v > worst)
                {
                    worst = v;
                    xs = m_XS[x];
                    over = m_XOver[x];
                }
            }
            return worst;
        }

        /// <summary>Narrows [lo, hi], the far-end rope interval of the span from sample i (rope ri) to sample j, by every crossing inside
        /// it. It returns false when nothing is left.</summary>
        private bool CrossBounds(int i, float ri, int j, ref float lo, ref float hi)
        {
            float si = m_S[i], sj = m_S[j], d = sj - si, h = m_C.SagRatio;
            if (d <= 0f)
                return true;
            float yi = ri - NodeOff(i), offJ = NodeOff(j);
            for (int x = 0; x < m_XN; x++)
            {
                if (!(m_XS[x] > si && m_XS[x] < sj))
                    continue;
                float t = (m_XS[x] - si) / d;
                float a = yi - 4f * d * h * t * (1f - t), sigma = -4f * h * (1f - 2f * t);
                // drawn curve at the crossing = a + t D, its slope D / d + sigma (D = far node y - near node y)
                bool ok = m_XOver[x] ? ConcaveAtLeast(a, t, m_XEN[x], d, sigma, m_XR[x], out float dlo, out float dhi)
                                     : ConcaveAtLeast(-a, -t, m_XEN[x], d, sigma, -m_XR[x], out dlo, out dhi);
                if (!ok)
                    return false;
                lo = math.max(lo, yi + dlo + offJ);
                hi = math.min(hi, yi + dhi + offJ);
                if (lo > hi + Tol)
                    return false;
            }
            return true;
        }

        /// <summary>The D with a + b D - e |D / d + sigma| >= r (e >= 0, d > 0). The function is concave and two-piece linear, so the
        /// result is an interval [lo, hi] (either end may be infinite). It returns false when empty.</summary>
        private static bool ConcaveAtLeast(float a, float b, float e, float d, float sigma, float r, out float lo, out float hi)
        {
            float k = e / d, x0 = -sigma * d;
            bool right = Piece(a + k * x0, b - k, r, x0, float.PositiveInfinity, out float loR, out float hiR);   // D >= x0
            bool left = Piece(a - k * x0, b + k, r, float.NegativeInfinity, x0, out float loL, out float hiL);    // D <= x0
            lo = left ? loL : loR;
            hi = right ? hiR : hiL;
            return left || right;
        }

        /// <summary>{D in [dLo, dHi] : c + p D >= r} as [lo, hi]. It returns false when empty.</summary>
        private static bool Piece(float c, float p, float r, float dLo, float dHi, out float lo, out float hi)
        {
            lo = dLo;
            hi = dHi;
            if (math.abs(p) < 1e-9f)
                return c >= r;
            float x = (r - c) / p;
            if (p > 0f)
                lo = math.max(lo, x);
            else
                hi = math.min(hi, x);
            return lo <= hi;
        }

        /// <summary>The node point itself. The carrier at a tower passes over the high side. The rope is never above the cap.</summary>
        private bool NodeOk(int k, int member)
        {
            if (member < 0)
                return true;
            float rope = Rope(k, member);
            if (rope - m_C.CarrierDropM - m_High[k] - m_Floor[k] < -Tol)
                return false;
            return !(m_C.MaxRopeAboveGroundM > 0f && rope - m_Centre[k] > m_C.MaxRopeAboveGroundM + Tol);
        }

        /// <summary>The planner keeps this share of MaxEdgeSlope: its node heights are the sampled ground + E, the engine's are the
        /// node's own (a few decimeters apart on a carved or rough slope, LP-28).</summary>
        public const float EdgeSlopeKeep = 0.97f;

        /// <summary>Working node <paramref name="node"/> is a station end (member -1) that the caller marked as a station.</summary>
        private bool StationEnd(int node) => m_M[node] < 0 && (m_K[node] == 0 ? StationA : StationB);

        /// <summary>The node's height as the engine builds it: a tower at ground + E(member), an end at EndNodeYA / EndNodeYB.</summary>
        private float NodeY(int k, int member)
        {
            if (member < 0)
                return k == 0 ? EndNodeYA : EndNodeYB;
            return m_Centre[k] + LiftTowerEdit.ElevationM(m_C, member);
        }

        /// <summary>The edge (k, mk) - (k2, mk2) passes the engine's slope rule (always true unless CheckEdgeSlope).</summary>
        private bool SlopeOk(int k, int mk, int k2, int mk2)
        {
            if (!CheckEdgeSlope || !(m_C.MaxEdgeSlope > 0f))
                return true;
            return SlopeExcess(k, mk, k2, mk2) <= 0f;
        }

        /// <summary>Meters of |dy| above what EdgeSlopeKeep x MaxEdgeSlope allows on the edge (k, mk) - (k2, mk2). It is 0 when it passes
        /// or an end height is unknown.</summary>
        private float SlopeExcess(int k, int mk, int k2, int mk2)
        {
            float d = math.abs(m_S[k2] - m_S[k]);
            float ya = NodeY(k, mk), yb = NodeY(k2, mk2);
            if (d <= 0f || !math.isfinite(ya) || !math.isfinite(yb))
                return 0f;
            return math.max(0f, math.abs(yb - ya) - EdgeSlopeKeep * m_C.MaxEdgeSlope * d);
        }

        private bool MemberAllowed(int k, int member)
        {
            return !(m_C.MaxRopeAboveGroundM > 0f && m_C.RopeAboveBase(member) > m_C.MaxRopeAboveGroundM + Tol) && member >= 0;
        }

        /// <summary>Worst violation of the span (i sample, ri rope) → (j, rj): max over the inner samples of the floor shortfall and
        /// the max-height excess (> 0 = violated). <paramref name="worstK"/> = that sample, <paramref name="floorType"/> = it is a
        /// floor shortfall.</summary>
        private float SpanViolation(int i, float ri, int j, float rj, out int worstK, out bool floorType)
        {
            worstK = -1;
            floorType = true;
            float worst = float.NegativeInfinity;
            float d = m_S[j] - m_S[i];
            if (d <= 0f)
                return 0f;
            float sag4 = 4f * m_C.SagRatio * d;
            float floorSag4 = FloorSag(d, sag4);
            float cap = m_C.MaxRopeAboveGroundM;
            float drop = m_C.CarrierDropM;
            for (int q = i + 1; q < j; q++)
            {
                float t = (m_S[q] - m_S[i]) / d;
                float chord = ri + (rj - ri) * t;
                float r = chord - sag4 * t * (1f - t);
                float fv = drop + m_Floor[q] + m_High[q] - (chord - floorSag4 * t * (1f - t));
                if (fv > worst)
                {
                    worst = fv;
                    worstK = q;
                    floorType = true;
                }
                if (cap > 0f)
                {
                    float hv = r - m_Centre[q] - cap;
                    if (hv > worst)
                    {
                        worst = hv;
                        worstK = q;
                        floorType = false;
                    }
                }
            }
            if (m_XN > 0)
            {
                // A crossing short of its box clearance: an OVER shortfall is floor-like (phase (c) raises the end nearer it), an
                // UNDER one height-like. Its sample: the one nearest the crossing.
                float cv = CrossShort(i, ri, j, rj, out float xs, out bool xOver);
                if (cv > 0f && cv > worst)
                {
                    worst = cv;
                    floorType = xOver;
                    worstK = xs - m_S[i] <= m_S[j] - xs ? i : j;
                    for (int q = i + 1; q < j; q++)
                        if (math.abs(m_S[q] - xs) < math.abs(m_S[worstK] - xs))
                            worstK = q;
                }
            }
            return worstK < 0 ? 0f : worst;
        }

        private float SpanViolation(int i, float ri, int j, float rj) => SpanViolation(i, ri, j, rj, out _, out _);

        /// <summary>The sag the floor test uses: the real one, except in phase (a) on a span that phase (b) will split (its sub-spans sag
        /// far less: FG 0.03 × 300 m = 9 m for the whole chord). A span (b) leaves whole is final and keeps its sag.</summary>
        private float FloorSag(float d, float sag4) => m_UseSag || InfillTowers(d) == 0 ? sag4 : 0f;

        /// <summary>The far-end rope heights [lo, hi] that keep the span from sample i (rope ri) to sample j clear, in one pass: every
        /// inner sample is a linear bound on rj (floor from below, max height from above). False when the interval is empty.</summary>
        private bool SpanInterval(int i, float ri, int j, out float lo, out float hi)
        {
            lo = float.NegativeInfinity;
            hi = float.PositiveInfinity;
            float d = m_S[j] - m_S[i];
            if (d <= 0f)
                return true;
            float sag4 = 4f * m_C.SagRatio * d;
            float floorSag4 = FloorSag(d, sag4);
            float cap = m_C.MaxRopeAboveGroundM;
            float drop = m_C.CarrierDropM;
            for (int q = i + 1; q < j; q++)
            {
                float t = (m_S[q] - m_S[i]) / d;
                float bow = t * (1f - t);
                // rope(t) = ri + (rj - ri) t - sag·bow  =>  rj = ri + (target - ri + sag·bow) / t
                lo = math.max(lo, ri + (drop + m_Floor[q] + m_High[q] + floorSag4 * bow - ri) / t);
                if (cap > 0f)
                    hi = math.min(hi, ri + (cap + m_Centre[q] + sag4 * bow - ri) / t);
                if (lo > hi + Tol)
                    return false;
            }
            return m_XN == 0 || CrossBounds(i, ri, j, ref lo, ref hi);
        }

        // (a) anchors

        private struct Cost
        {
            public int Towers;      // anchors + the infill towers phase (b) will add to the spans so far
            public int Anchors;
            public int Members;     // sum of ladder indices
            public int Dist;        // sum of |anchor - worst-intrusion sample|
            public int Parent;      // state index (k * ladder + m), -1 = the start node

            public bool Better(Cost o)
            {
                if (Towers != o.Towers) return Towers < o.Towers;
                if (Anchors != o.Anchors) return Anchors < o.Anchors;
                if (Members != o.Members) return Members < o.Members;
                return Dist < o.Dist;
            }
        }

        private Cost[] m_Cost = new Cost[0];
        private bool[] m_Reached = new bool[0];

        // (a+b) fewest towers: tall members win wherever they save a tower

        private struct MinCost
        {
            public float Viol;      // m_Soft only: summed floor shortfall of the spans and nodes so far (0 in the strict search)
            public int Towers;      // line towers so far
            public float EndRiseM;  // height above the shortest member of each tower next to a station end (TowerWorthM trades it)
            public int Breaks;      // towers on the uphill sample of a convex crest (breakover rule): more is better
            public int Members;     // sum of ladder indices (fewer steps = shorter towers at the same count)
            public float SumSq;     // sum of squared span lengths (even spans at the same count and steps)
            public int Parent;      // state index (k * ladder + m), -1 = the segment's start node

            public bool Better(MinCost o, float towerWorthM)
            {
                if (Viol != o.Viol && math.abs(Viol - o.Viol) > 1e-3f) return Viol < o.Viol;   // additive like the rest: the search stays exact
                if (towerWorthM > 0f)
                {
                    float x = Towers * towerWorthM + EndRiseM, y = o.Towers * towerWorthM + o.EndRiseM;
                    if (math.abs(x - y) > 1e-3f) return x < y;
                }
                if (Towers != o.Towers) return Towers < o.Towers;
                if (math.abs(EndRiseM - o.EndRiseM) > 1e-3f) return EndRiseM < o.EndRiseM;
                if (Breaks != o.Breaks) return Breaks > o.Breaks;
                if (Members != o.Members) return Members < o.Members;
                return SumSq < o.SumSq - 1e-3f;
            }
        }

        private MinCost[] m_MinCost = new MinCost[0];

        /// <summary>The soft search (FallbackSegment: no clear arrangement exists and the legacy phases leave none either). SearchMin
        /// accepts a span or a node short of the floor and adds the shortfall to MinCost.Viol, which ranks first. The max rope height, the
        /// edge slope rule, MaxSpanM and the gaps stay hard.</summary>
        private bool m_Soft;

        private interface ISearchMode { bool Soft { get; } }
        private struct StrictSearch : ISearchMode { public bool Soft => false; }
        private struct SoftSearch : ISearchMode { public bool Soft => true; }

        /// <summary>The floor shortfall (meters, 0 when within Tol) of the span (i, ri) -> (j, rj) with the sag phase (a+b) uses.</summary>
        private float FloorShortfall(int i, float ri, int j, float rj)
        {
            float d = m_S[j] - m_S[i];
            if (d <= 0f)
                return 0f;
            float floorSag4 = FloorSag(d, 4f * m_C.SagRatio * d);
            float drop = m_C.CarrierDropM, worst = 0f;
            for (int q = i + 1; q < j; q++)
            {
                float t = (m_S[q] - m_S[i]) / d;
                worst = math.max(worst, drop + m_Floor[q] + m_High[q] - (ri + (rj - ri) * t - floorSag4 * t * (1f - t)));
            }
            return worst > Tol ? worst : 0f;
        }

        /// <summary>The floor shortfall at a tower node (k, member): the carrier over its own high side. It is 0 when within Tol.</summary>
        private float NodeShortfall(int k, int member)
        {
            float v = m_C.CarrierDropM + m_High[k] + m_Floor[k] - Rope(k, member);
            return v > Tol ? v : 0f;
        }

        /// <summary>The soft search's arrival at a segment's end node. It returns false when the rope passes the max height. Otherwise it
        /// puts the floor shortfall of the span in <paramref name="viol"/>.</summary>
        private bool SoftArrive(int k, float rk, int j, float rj, out float viol)
        {
            SpanBounds(k, rk, j, out float lo, out float hi);
            viol = 0f;
            if (rj > hi + Tol)
                return false;
            if (m_XN > 0 && CrossShort(k, rk, j, rj, out _, out _) > Tol)
                return false;   // a crossing's box clearance stays hard, like the max height
            if (rj < lo - Tol)
                viol = FloorShortfall(k, rk, j, rj);
            return true;
        }

        /// <summary>SpanInterval without the early exit (the soft search needs both bounds even when they cross).</summary>
        private void SpanBounds(int i, float ri, int j, out float lo, out float hi)
        {
            lo = float.NegativeInfinity;
            hi = float.PositiveInfinity;
            float d = m_S[j] - m_S[i];
            if (d <= 0f)
                return;
            float sag4 = 4f * m_C.SagRatio * d;
            float floorSag4 = FloorSag(d, sag4);
            float cap = m_C.MaxRopeAboveGroundM;
            float drop = m_C.CarrierDropM;
            for (int q = i + 1; q < j; q++)
            {
                float t = (m_S[q] - m_S[i]) / d;
                float bow = t * (1f - t);
                lo = math.max(lo, ri + (drop + m_Floor[q] + m_High[q] + floorSag4 * bow - ri) / t);
                if (cap > 0f)
                    hi = math.min(hi, ri + (cap + m_Centre[q] + sag4 * bow - ri) / t);
            }
        }

        private bool m_IgnoreForbidden;

        private bool IsForbidden(int k)
        {
            if (m_IgnoreForbidden)
                return false;
            if (Forbidden != null)
                for (int f = 0; f < Forbidden.Count; f++)
                    if (math.abs(m_S[k] - Forbidden[f]) < ForbiddenRadiusM - 1e-3f)
                        return true;
            if (Keepouts != null)
                for (int f = 0; f < Keepouts.Count; f++)
                    if (m_S[k] > Keepouts[f].x + 1e-3f && m_S[k] < Keepouts[f].y - 1e-3f)
                        return true;
            return false;
        }
        private bool[] m_UphillOfCrest = new bool[0];

        /// <summary>Marks, for samples i..j, the uphill neighbor of every convex crest (Curvature below -ConvexM and the most convex
        /// within ±2 samples): a tower there is the real-world "tower just before a breakover".</summary>
        private void MarkCrests(int i, int j)
        {
            if (m_UphillOfCrest.Length < m_N + 1)
                m_UphillOfCrest = new bool[m_N + 1];
            for (int k = i; k <= j; k++)
                m_UphillOfCrest[k] = false;
            for (int k = i + 1; k < j; k++)
            {
                float c = Curvature(k);
                if (c >= -m_C.ConvexM)
                    continue;
                bool most = true;
                for (int q = math.max(i + 1, k - 2); q <= math.min(j - 1, k + 2) && most; q++)
                    if (q != k && (Curvature(q) < c - 1e-4f || (math.abs(Curvature(q) - c) <= 1e-4f && q < k)))
                        most = false;
                if (!most)
                    continue;
                int up = m_High[math.min(k + 1, m_N)] >= m_High[math.max(k - 1, 0)] ? k + 1 : k - 1;
                if (up > i && up < j)
                    m_UphillOfCrest[up] = true;
            }
        }

        /// <summary>MinTowers' search over samples x ladder members from working node a to b: the best end state under
        /// MinCost.Better(worth). A value cap >= 0 adds the tower count to the state (layer t = towers so far, at most cap). The result is
        /// exact for the best arrangement with at most cap towers, which the plain search is not (it keeps one state per sample and
        /// member). The search must visit the working nodes between a and b (ChooseSeedMembers), at any member. m_MinCost / m_Reached hold
        /// the search the walk back reads (state index t * (m_N + 1) * L + k * L + m).</summary>
        private bool SearchMin(int a, int b, float worth, int cap, out MinCost end) =>
            m_Soft ? SearchMin<SoftSearch>(a, b, worth, cap, out end) : SearchMin<StrictSearch>(a, b, worth, cap, out end);

        /// <summary>SearchMin for one mode, a struct type argument so the JIT compiles the strict search without the soft branches
        /// (planner cost unchanged on every line that has a clear plan).</summary>
        private bool SearchMin<TMode>(int a, int b, float worth, int cap, out MinCost end) where TMode : struct, ISearchMode
        {
            bool soft = default(TMode).Soft;
            int i = m_K[a], j = m_K[b];
            float ri = Rope(i, m_M[a]), rj = Rope(j, m_M[b]);
            float maxSpan = m_C.MaxSpanM + 1e-3f;
            int L = m_C.Ladder.Length;
            int layers = cap >= 0 ? cap + 1 : 1, perLayer = (m_N + 1) * L;
            int states = layers * perLayer;
            if (m_MinCost.Length < states)
                m_MinCost = new MinCost[states];
            if (m_Reached.Length < states)
                m_Reached = new bool[states];
            for (int t = 0; t < layers; t++)
                for (int q = t * perLayer + i * L; q < t * perLayer + (j + 1) * L; q++)
                    m_Reached[q] = false;
            int minGap = MinGap();
            MarkCrests(i, j);
            bool endReached = false;
            end = default;
            for (int t = 0; t < layers; t++)
            for (int k = i, next = a + 1; k < j; k++)
            {
                while (m_K[next] <= k)
                    next++;
                int stop = m_K[next];                  // no span passes the next working node
                bool atNode = m_K[next - 1] == k;      // k is a working node: its span to the next one may be short (as MinTowers' one span)
                for (int m = 0; m < L; m++)
                {
                    MinCost from;
                    float rk;
                    int mk = m;
                    if (k == i)
                    {
                        if (m > 0 || t > 0)
                            break;
                        from = new MinCost { Parent = -1 };
                        rk = ri;
                        mk = m_M[a];
                    }
                    else
                    {
                        int st = t * perLayer + k * L + m;
                        if (!m_Reached[st])
                            continue;
                        from = m_MinCost[st];
                        from.Parent = st;
                        rk = Rope(k, m);
                    }
                    // To the segment's end node.
                    float dEnd = m_S[j] - m_S[k];
                    float endViol = 0f;
                    if (k > i && stop == j && (atNode || j - k >= minGap) && dEnd <= maxSpan && SlopeOk(k, mk, j, m_M[b]) &&
                        (soft ? SoftArrive(k, rk, j, rj, out endViol)
                                : SpanInterval(k, rk, j, out float lo, out float hi) && rj >= lo - Tol && rj <= hi + Tol))
                    {
                        MinCost c = from;
                        c.Viol += endViol;
                        c.SumSq += dEnd * dEnd;
                        if (StationEnd(b))
                            c.EndRiseM += Rise(m);
                        if (!endReached || c.Better(end, worth))
                        {
                            end = c;
                            endReached = true;
                        }
                    }
                    // To a tower (k2, m2).
                    for (int k2 = math.min(k + minGap, stop); k2 <= stop && m_S[k2] - m_S[k] <= maxSpan; k2++)
                    {
                        if (k2 == stop && stop < j)
                        {
                            if (!atNode && k2 - k < minGap)
                                continue;
                        }
                        else if (k2 > j - minGap || IsForbidden(k2) || NearStation(k2))
                            continue;
                        float lo2, hi2;
                        if (soft)
                            SpanBounds(k, rk, k2, out lo2, out hi2);
                        else if (!SpanInterval(k, rk, k2, out lo2, out hi2))
                            continue;
                        float d = m_S[k2] - m_S[k];
                        for (int m2 = 0; m2 < L; m2++)
                        {
                            float r2 = Rope(k2, m2);
                            float viol = 0f;
                            if (r2 < lo2 - Tol)
                            {
                                if (!soft)
                                    continue;
                                viol = FloorShortfall(k, rk, k2, r2);
                            }
                            if (r2 > hi2 + Tol)
                                break;   // ropes ascend with the ladder
                            if (!soft)
                            {
                                if (!MemberAllowed(k2, m2) || !NodeOk(k2, m2) || !SlopeOk(k, mk, k2, m2))
                                    continue;
                            }
                            else
                            {
                                if (!MemberAllowed(k2, m2) || !SlopeOk(k, mk, k2, m2) ||
                                    (m_C.MaxRopeAboveGroundM > 0f && r2 - m_Centre[k2] > m_C.MaxRopeAboveGroundM + Tol) ||
                                    (m_XN > 0 && CrossShort(k, rk, k2, r2, out _, out _) > Tol))
                                    continue;
                                viol += NodeShortfall(k2, m2);
                            }
                            MinCost c = from;
                            c.Viol += viol;
                            c.Towers += 1;
                            if (cap >= 0 && c.Towers > cap)
                                break;
                            if (k == i && StationEnd(a))
                                c.EndRiseM += Rise(m2);
                            c.Breaks += m_UphillOfCrest[k2] ? 1 : 0;
                            c.Members += m2;
                            c.SumSq += d * d;
                            int st2 = (cap >= 0 ? c.Towers : 0) * perLayer + k2 * L + m2;
                            if (!m_Reached[st2] || c.Better(m_MinCost[st2], worth))
                            {
                                m_MinCost[st2] = c;
                                m_Reached[st2] = true;
                            }
                        }
                    }
                }
            }
            return endReached;
        }

        /// <summary>The members of seeds placed without one. One search runs over the whole line, with every seed a node it must visit. So
        /// a pin is as tall as the line needs, and the towers between pins are the fewest. The old way searched span by span from a pin at
        /// its shortest member. Each span added towers only to rescue that short tower: removing one tower of three gave nine (LP-32).
        /// Phase (c) still sets the final members. With no clear plan at all, the members stay and the segments' fallback reports
        /// it.</summary>
        private void ChooseSeedMembers()
        {
            m_UseSag = true;
            int last = m_K.Count - 1;
            bool found = SearchMin(0, last, 0f, -1, out MinCost end);
            m_IgnoreForbidden = true;
            if (!found)
                found = SearchMin(0, last, 0f, -1, out end);
            m_NoClearance = true;
            if (!found)
                found = SearchMin(0, last, 0f, -1, out end);
            m_NoClearance = false;
            m_IgnoreForbidden = false;
            if (!found)
                return;
            int L = m_C.Ladder.Length;
            for (int state = end.Parent; state >= 0; state = m_MinCost[state].Parent)
            {
                int at = IndexOf(state / L);
                if (at >= 0)
                    m_M[at] = state % L;
            }
        }

        /// <summary>Between working nodes a and b = a + 1: the arrangement with the FEWEST towers. Every span is at most MaxSpanM and clear
        /// with its real sag (floor over the high side, max rope height). Every node keeps the floor over its own high side.
        /// The ranking after the fewest towers is: the most towers just uphill of a convex crest (breakover rule, never at the cost of a
        /// tower), then the fewest ladder steps, then the most even spans. The search is exact: a shortest-path search over samples x
        /// ladder members (states k * L + m), with each span's far-end rope interval from SpanInterval (one pass per span). So a tall
        /// member wins wherever it lets a span reach further and saves a tower. With the station rule on, a segment that ends at a station
        /// may use more towers for shorter station-side ones (TowerWorthM), at most one more than the fewest per line. Returns the nodes
        /// added (0 when the segment is already one clear span), or -1 when no clear arrangement exists (the caller then runs
        /// LegacySegment).</summary>
        private int MinTowers(int a, int b)
        {
            m_UseSag = true;
            int i = m_K[a], j = m_K[b];
            float ri = Rope(i, m_M[a]), rj = Rope(j, m_M[b]);
            // One span, clear for the floor: done when the edge also passes the slope rule. Otherwise the search runs. Where it finds
            // nothing, the span stays as it is and Build flags it SlopeViolated (no LegacySegment towers that fix nothing). A chord between
            // fixed end heights steeper than the rule cannot be cut by towers: their edges average the same slope.
            bool oneSpan = m_S[j] - m_S[i] <= m_C.MaxSpanM + 1e-3f && SpanViolation(i, ri, j, rj) <= Tol;
            if (oneSpan && SlopeOk(i, m_M[a], j, m_M[b]))
                return 0;
            // The station rule (TowerWorthM) trades towers for shorter station-side towers, but buys at most ONE tower more than the fewest
            // per line (m_ExtraTowers). The fewest-towers search runs first, then the weighted one. When the weighted best needs more
            // towers than the budget allows, the search runs again with the tower count in the state (exact under the cap).
            float worth = StationEnd(a) || StationEnd(b) ? TowerWorth : 0f;
            MinCost end;
            if (worth > 0f)
            {
                if (!SearchMin(a, b, 0f, -1, out MinCost fewest))
                    return oneSpan ? 0 : -1;
                int cap = fewest.Towers + math.max(0, m_ExtraTowers);
                if (!SearchMin(a, b, worth, -1, out end))
                    return -1;
                if (end.Towers > cap && !SearchMin(a, b, worth, cap, out end))
                    return -1;
                m_ExtraTowers -= end.Towers - fewest.Towers;
            }
            else if (!SearchMin(a, b, 0f, -1, out end))
                return oneSpan ? 0 : -1;

            int L = m_C.Ladder.Length, perLayer = (m_N + 1) * L;
            // Walk back. Every insert lands between a and the end node. Flags against the segment's straight chord: Shoulder (the ground
            // pokes through it), Valley (it is too high above the ground), otherwise Infill.
            int added = 0;
            int state = end.Parent;
            float chordSlope = (rj - ri) / math.max(m_S[j] - m_S[i], 1e-3f);
            while (state >= 0)
            {
                int k = (state % perLayer) / L, m = state % L;
                float chordHere = ri + chordSlope * (m_S[k] - m_S[i]);
                LiftPlanNodeFlags f = m_C.CarrierDropM + m_Floor[k] + m_High[k] > chordHere ? LiftPlanNodeFlags.Shoulder
                    : m_C.MaxRopeAboveGroundM > 0f && chordHere - m_Centre[k] > m_C.MaxRopeAboveGroundM ? LiftPlanNodeFlags.Valley
                    : LiftPlanNodeFlags.Infill;
                Insert(k, m, f);
                added++;
                state = m_MinCost[state].Parent;
            }
            BreakoverPass(a, a + added + 1);
            return added;
        }

        /// <summary>Working nodes a..b as they stand are clear: every span's floor and max height with the sag, every tower node, and
        /// (CheckEdgeSlope) every edge under the engine's own limit (MaxEdgeSlope + 0.0005, the LiftPlanSpan.SlopeViolated test).</summary>
        private bool SegmentClear(int a, int b)
        {
            for (int n = a; n < b; n++)
            {
                int k = m_K[n], k2 = m_K[n + 1];
                if (SpanViolation(k, Rope(k, m_M[n]), k2, Rope(k2, m_M[n + 1])) > Tol)
                    return false;
                if (n > a && !NodeOk(k, m_M[n]))
                    return false;
                float d = m_S[k2] - m_S[k], ya = NodeY(k, m_M[n]), yb = NodeY(k2, m_M[n + 1]);
                if (CheckEdgeSlope && m_C.MaxEdgeSlope > 0f && d > 0f && math.isfinite(ya) && math.isfinite(yb) &&
                    math.abs(yb - ya) / d >= m_C.MaxEdgeSlope + 0.0005f)
                    return false;
            }
            return true;
        }

        /// <summary>The segment from working node a had no clear arrangement, and LegacySegment left it violated (its
        /// <paramref name="legacy"/> nodes after a). This method runs the soft search instead (m_Soft: the least floor shortfall, then the fewest
        /// towers, the edge slope rule kept), so the violation stays where no tower can remove it. The legacy greedy split had put 13
        /// towers 10 m apart across a 450 m FG line for ground inside its end zone that no tower may stand in
        /// (planner-fallback-2026-11-04). Where the soft search finds nothing either (the slope rule or the max height), the legacy nodes
        /// are put back.</summary>
        private int SoftInsteadOfLegacy(int a, int legacy)
        {
            List<int> ks = m_K.GetRange(a + 1, legacy), ms = m_M.GetRange(a + 1, legacy);
            List<LiftPlanNodeFlags> fs = m_F.GetRange(a + 1, legacy);
            m_K.RemoveRange(a + 1, legacy);
            m_M.RemoveRange(a + 1, legacy);
            m_F.RemoveRange(a + 1, legacy);
            int extra = m_ExtraTowers;
            m_IgnoreForbidden = true;
            m_Soft = true;
            int added = MinTowers(a, a + 1);
            m_Soft = false;
            m_IgnoreForbidden = false;
            if (added >= 0)
                return added;
            m_ExtraTowers = extra;
            m_K.InsertRange(a + 1, ks);
            m_M.InsertRange(a + 1, ms);
            m_F.InsertRange(a + 1, fs);
            return legacy;
        }

        /// <summary>The segment from working node a has no clear arrangement by the exact search. (a) The anchors (exact, the floor without
        /// the sag) with the infill (b) are kept when that is clear (a tower on a narrow knoll the search refuses, LP-27). Otherwise the
        /// soft search replaces it (SoftInsteadOfLegacy). If (a) found no anchors, the soft search runs FIRST, and the greedy split runs
        /// only where the soft search finds nothing (FIX-LIST 49). The split was computed and thrown away on every such segment (a split
        /// plus its infill is never clear where the anchors search found none: 0 of 66 in LiftBatch and AutoGenBatch). That was about a
        /// fifth of a violated 2 km DQ plan. Returns the nodes added.</summary>
        private int FallbackSegment(int a)
        {
            m_UseSag = false;
            int anchors = Anchors(a, a + 1);
            m_UseSag = true;
            if (anchors >= 0)
            {
                int added = anchors + InfillAnchors(a, anchors);
                return SegmentClear(a, a + added + 1) ? added : SoftInsteadOfLegacy(a, added);
            }
            int extra = m_ExtraTowers;
            m_IgnoreForbidden = true;
            m_Soft = true;
            int soft = MinTowers(a, a + 1);
            m_Soft = false;
            m_IgnoreForbidden = false;
            if (soft >= 0)
                return soft;
            m_ExtraTowers = extra;
            m_UseSag = false;
            anchors = Split(a, a + 1, 0);
            m_UseSag = true;
            return anchors + InfillAnchors(a, anchors);
        }

        /// <summary>The legacy phases for the segment from working node a to a + 1: (a) anchors (exact search, else the greedy
        /// split), then (b) infill of every anchor span. Returns the nodes added.</summary>
        private int LegacySegment(int a)
        {
            // (a) The floor test ignores the sag here: a long chord sags meters that its infilled spans will not.
            m_UseSag = false;
            int anchors = Anchors(a, a + 1);
            if (anchors < 0)
                anchors = Split(a, a + 1, 0);   // no clear arrangement exists: the greedy split minimizes what is left
            m_UseSag = true;
            return anchors + InfillAnchors(a, anchors);
        }

        /// <summary>Phase (b) on the <paramref name="anchors"/> spans after working node a. Returns the infill nodes added.</summary>
        private int InfillAnchors(int a, int anchors)
        {
            int added = 0;
            int end = a + anchors + 1;
            for (int i = a; i < end; )
            {
                int inf = Infill(i, i + 1);
                end += inf;
                added += inf;
                i += inf + 1;
            }
            return added;
        }

        /// <summary>Phase (a) between working nodes a and b = a + 1. It finds the anchor set with the fewest towers (anchors + the infill
        /// (b) will add), then the fewest anchors, the shortest members, and anchors nearest the worst-intrusion sample. Every span must be
        /// clear (floor against the bare chord, max height with the sag). This is the recursive worst-intrusion split solved exactly (a
        /// search over the samples) instead of greedily. The greedy split put 18 towers on LP-5's 40 m wall. Returns the nodes added, 0
        /// when the span is already clear, or -1 when no clear arrangement exists.</summary>
        private int Anchors(int a, int b)
        {
            int i = m_K[a], j = m_K[b];
            float ri = Rope(i, m_M[a]), rj = Rope(j, m_M[b]);
            float v0 = SpanViolation(i, ri, j, rj, out int kWorst, out _);
            if (v0 <= Tol)
                return 0;
            int L = m_C.Ladder.Length;
            int states = (m_N + 1) * L;
            if (m_Cost.Length < states)
            {
                m_Cost = new Cost[states];
                m_Reached = new bool[states];
            }
            for (int q = i * L; q < (j + 1) * L && q < states; q++)
                m_Reached[q] = false;
            int minGap = MinGap();
            // Anchor spans are capped at 3 x MaxSpanM (phase (b) splits them anyway). This bounds the search to O(samples x cap x span) per
            // ladder member at the source. SpanInterval removes the far member's factor.
            int maxGap = math.max(minGap, (int)math.floor(3f * m_C.MaxSpanM / m_C.StepM));
            bool endReached = false;
            Cost end = default;
            // Sources in sample order: the start node, then every reached (k, m).
            for (int k = i; k < j; k++)
            {
                for (int m = 0; m < L; m++)
                {
                    Cost from;
                    float rk;
                    if (k == i)
                    {
                        if (m > 0)
                            break;
                        from = new Cost { Parent = -1 };
                        rk = ri;
                    }
                    else
                    {
                        int st = k * L + m;
                        if (!m_Reached[st])
                            continue;
                        from = m_Cost[st];
                        from.Parent = st;
                        rk = Rope(k, m);
                    }
                    // To the end node.
                    if (k > i && j - k >= minGap && j - k <= maxGap && SpanInterval(k, rk, j, out float lo, out float hi) &&
                        rj >= lo - Tol && rj <= hi + Tol)
                    {
                        Cost c = from;
                        c.Towers += InfillTowers(m_S[j] - m_S[k]);
                        if (!endReached || c.Better(end))
                        {
                            end = c;
                            endReached = true;
                        }
                    }
                    // To an anchor (k2, m2).
                    for (int k2 = k + minGap; k2 <= math.min(j - minGap, k + maxGap); k2++)
                    {
                        if (NearStation(k2) || !SpanInterval(k, rk, k2, out float lo2, out float hi2))
                            continue;
                        int infill = InfillTowers(m_S[k2] - m_S[k]);
                        for (int m2 = 0; m2 < L; m2++)
                        {
                            float r2 = Rope(k2, m2);
                            if (r2 < lo2 - Tol)
                                continue;
                            if (r2 > hi2 + Tol)
                                break;   // ropes ascend with the ladder
                            if (!MemberAllowed(k2, m2) || !NodeOk(k2, m2))
                                continue;
                            Cost c = from;
                            c.Towers += infill + 1;
                            c.Anchors += 1;
                            c.Members += m2;
                            c.Dist += math.abs(k2 - kWorst);
                            int st2 = k2 * L + m2;
                            if (!m_Reached[st2] || c.Better(m_Cost[st2]))
                            {
                                m_Cost[st2] = c;
                                m_Reached[st2] = true;
                            }
                        }
                    }
                }
            }
            if (!endReached)
                return -1;

            // Walk back. Every insert lands between a and the end node, so a stays valid.
            int added = 0;
            int state = end.Parent;
            float chordSlope = (rj - ri) / math.max(m_S[j] - m_S[i], 1e-3f);
            while (state >= 0)
            {
                int k = state / L, m = state % L;
                // Shoulder: the ground pokes through the station-to-station chord here (floor). Otherwise Valley (max height).
                float chordHere = ri + chordSlope * (m_S[k] - m_S[i]);
                bool shoulder = m_C.CarrierDropM + m_Floor[k] + m_High[k] > chordHere;
                Insert(k, m, shoulder ? LiftPlanNodeFlags.Shoulder : LiftPlanNodeFlags.Valley);
                added++;
                state = m_Cost[state].Parent;
            }
            BreakoverPass(a, a + added + 1);
            return added;
        }

        /// <summary>Shoulders on convex ground (between working nodes a and b) become breakovers. Each moves one sample past the crest
        /// toward the higher ground (uphill of the crest) with the shortest member that keeps both of its spans clear and adds no infill
        /// tower.</summary>
        private void BreakoverPass(int a, int b)
        {
            int minGap = MinGap();
            for (int n = a + 1; n < b; n++)
            {
                if ((m_F[n] & LiftPlanNodeFlags.Shoulder) == 0)
                    continue;
                int i = m_K[n - 1], k = m_K[n], j = m_K[n + 1];
                int crest = Crest(k, i, j);
                if (crest < 0)
                    continue;
                m_F[n] |= LiftPlanNodeFlags.Breakover;
                int up = m_High[math.min(crest + 1, m_N)] >= m_High[math.max(crest - 1, 0)] ? crest + 1 : crest - 1;
                if (up == k || up - i < minGap || j - up < minGap || NearStation(up) || IsForbidden(up))
                    continue;
                if (InfillTowers(m_S[up] - m_S[i]) + InfillTowers(m_S[j] - m_S[up]) >
                    InfillTowers(m_S[k] - m_S[i]) + InfillTowers(m_S[j] - m_S[k]))
                    continue;
                float ri = Rope(i, m_M[n - 1]), rj = Rope(j, m_M[n + 1]);
                for (int mm = 0; mm < m_C.Ladder.Length; mm++)
                {
                    if (!MemberAllowed(up, mm) || !NodeOk(up, mm) || !SlopeOk(i, m_M[n - 1], up, mm) || !SlopeOk(up, mm, j, m_M[n + 1]))
                        continue;
                    float ru = Rope(up, mm);
                    if (SpanViolation(i, ri, up, ru) <= Tol && SpanViolation(up, ru, j, rj) <= Tol)
                    {
                        m_K[n] = up;
                        m_M[n] = mm;
                        break;
                    }
                }
            }
        }

        /// <summary>Fallback when no clear arrangement exists (e.g. ground through the rope right at a station). It is a greedy split of
        /// the span between working nodes a and a+1 (b == a + 1) while the span is violated. It returns the nodes added.</summary>
        private int Split(int a, int b, int depth)
        {
            int i = m_K[a], j = m_K[b];
            float ri = Rope(i, m_M[a]), rj = Rope(j, m_M[b]);
            float v = SpanViolation(i, ri, j, rj, out int kWorst, out bool floorType);
            if (v <= Tol || depth >= MaxDepth)
                return 0;
            if (!BestSplit(i, ri, j, rj, kWorst, out int k, out int m, out int bad, out float sum))
                return 0;
            if (bad == 2 && sum >= v - Tol)
                return 0;   // no split improves this span: (c) / the report carry the violation

            LiftPlanNodeFlags flags = floorType ? LiftPlanNodeFlags.Shoulder : LiftPlanNodeFlags.Valley;
            int crest = floorType ? Crest(k, i, j) : -1;
            if (crest >= 0)
            {
                flags |= LiftPlanNodeFlags.Breakover;
                // Uphill of the crest: one sample past the most convex sample toward the higher ground, with the shortest member
                // that leaves the halves no worse than the chosen split.
                int up = m_High[math.min(crest + 1, m_N)] >= m_High[math.max(crest - 1, 0)] ? crest + 1 : crest - 1;
                int minGap = MinGap();
                if (up != k && up - i >= minGap && j - up >= minGap && !NearStation(up))
                {
                    int infill = InfillTowers(m_S[k] - m_S[i]) + InfillTowers(m_S[j] - m_S[k]);
                    int infillUp = InfillTowers(m_S[up] - m_S[i]) + InfillTowers(m_S[j] - m_S[up]);
                    for (int mm = 0; mm < m_C.Ladder.Length && infillUp <= infill; mm++)
                    {
                        if (!MemberAllowed(up, mm) || !NodeOk(up, mm))
                            continue;
                        float ru = Rope(up, mm);
                        float vl = SpanViolation(i, ri, up, ru), vr = SpanViolation(up, ru, j, rj);
                        int badU = (vl > Tol ? 1 : 0) + (vr > Tol ? 1 : 0);
                        float sumU = math.max(vl, 0f) + math.max(vr, 0f);
                        if (badU < bad || (badU == bad && sumU <= sum + Tol))
                        {
                            k = up;
                            m = mm;
                            break;
                        }
                    }
                }
            }
            int at = Insert(k, m, flags);
            int added = 1;
            added += Split(at, at + 1, depth + 1);              // right half first: indices left of `at` stay valid
            added += Split(at - 1, at, depth + 1);
            return added;
        }

        private float Rise(int member) => m_C.Ladder[member].TopM - m_C.Ladder[0].TopM;

        /// <summary>The station-side exchange rate in use (LiftTowerPlanConstants.TowerWorthM, and 0 while StationRule is off).</summary>
        private float TowerWorth => StationRule ? m_C.TowerWorthM : 0f;

        /// <summary>The towers the station rule may still add over the fewest, per line (DECISIONS 429: at most one). Plan resets
        /// it.</summary>
        private int m_ExtraTowers;

        private int MinGap() => math.max(1, (int)math.ceil(m_C.MinSpanM / m_C.StepM - 1e-4f));

        /// <summary>Sample k is inside a station's clearance (StationClearM from either end, in meters: the grid's last step is short, so a
        /// sample count alone let a tower stand 8 m from a 10 m-MinSpan station). Also true inside a crossed lift's corridor (InCrossBand).
        /// The NoClearance retry never lifts this part. The angle pass counts those refusals as "station".</summary>
        private bool NearStation(int k) =>
            (m_XN > 0 && InCrossBand(k)) ||
            (!m_NoClearance && (m_S[k] < m_C.StationClearM - Tol || m_Length - m_S[k] < m_C.StationClearM - Tol));

        private bool m_NoClearance;

        private bool BestSplit(int i, float ri, int j, float rj, int kWorst, out int bestK, out int bestM, out int bestBad, out float bestSum)
        {
            bestK = -1; bestM = -1; bestBad = 3; bestSum = float.PositiveInfinity;
            int bestDist = int.MaxValue, bestInfill = int.MaxValue;
            int minGap = MinGap();
            for (int k = i + minGap; k <= j - minGap; k++)
            {
                if (NearStation(k))
                    continue;
                int infill = InfillTowers(m_S[k] - m_S[i]) + InfillTowers(m_S[j] - m_S[k]);
                for (int m = 0; m < m_C.Ladder.Length; m++)
                {
                    if (!MemberAllowed(k, m) || !NodeOk(k, m))
                        continue;
                    float rk = Rope(k, m);
                    float vl = SpanViolation(i, ri, k, rk), vr = SpanViolation(k, rk, j, rj);
                    int bad = (vl > Tol ? 1 : 0) + (vr > Tol ? 1 : 0);
                    float sum = math.max(vl, 0f) + math.max(vr, 0f);
                    int dist = math.abs(k - kWorst);
                    // Rank: fewer violated halves, smaller remaining violation (0.05 m bins), fewer infill towers the halves will
                    // need in (b), shorter member, nearer the worst-intrusion point.
                    bool better;
                    if (bad != bestBad) better = bad < bestBad;
                    else if (bad > 0 && math.abs(sum - bestSum) > 0.05f) better = sum < bestSum;
                    else if (infill != bestInfill) better = infill < bestInfill;
                    else if (m != bestM) better = m < bestM;
                    else better = dist < bestDist;
                    if (better)
                    {
                        bestK = k; bestM = m; bestBad = bad; bestSum = sum; bestDist = dist; bestInfill = infill;
                    }
                    if (bad == 0)
                        break;   // a taller member at this sample can only rank lower
                }
            }
            return bestK >= 0;
        }

        /// <summary>Sub-spans phase (b) will make of a span of <paramref name="len"/> meters, minus one = the infill towers it adds.</summary>
        private int InfillTowers(float len)
        {
            // The fewest sub-spans that respect MaxSpanM. LegacySpacing keeps the typical-span count.
            int n = LegacySpacing ? math.max((int)math.ceil(len / m_C.MaxSpanM - 1e-4f), (int)math.round(len / m_C.TypicalSpanM))
                                  : (int)math.ceil(len / m_C.MaxSpanM - 1e-4f);
            return math.max(0, n - 1);
        }

        /// <summary>Change of the high-side slope across sample k (±2 samples), in meters per 2·StepM: below -ConvexM = convex.</summary>
        private float Curvature(int k)
        {
            int a = math.max(0, k - 2), b = math.min(m_N, k + 2);
            if (b - k < 1 || k - a < 1)
                return 0f;
            float left = (m_High[k] - m_High[a]) / (m_S[k] - m_S[a]);
            float right = (m_High[b] - m_High[k]) / (m_S[b] - m_S[k]);
            return (right - left) * 2f * m_C.StepM;
        }

        /// <summary>The breakover crest near a floor split at k: the most convex sample within ±2 samples (inside the span), or -1
        /// when the ground there is not convex. Ties go to the sample nearest k, then the lower index.</summary>
        private int Crest(int k, int i, int j)
        {
            int best = -1;
            float bestC = -m_C.ConvexM;
            for (int q = math.max(i + 1, k - 2); q <= math.min(j - 1, k + 2); q++)
            {
                float c = Curvature(q);
                if (c < bestC - 1e-4f || (best >= 0 && math.abs(c - bestC) <= 1e-4f && math.abs(q - k) < math.abs(best - k)))
                {
                    bestC = math.min(c, bestC);
                    best = q;
                }
            }
            return best;
        }

        // (b) infill

        private int Infill(int a, int b)
        {
            int i = m_K[a], j = m_K[b];
            float len = m_S[j] - m_S[i];
            int n = InfillTowers(len) + 1;
            if (n <= 1)
                return 0;

            var bestKs = new List<int>();
            var bestMs = new List<int>();
            float bestViol = float.PositiveInfinity;
            var ks = new List<int>();
            var ms = new List<int>();
            for (int extra = 0; extra <= 3 && bestViol > Tol; extra++)
            {
                for (int pass = 0; pass < 2 && bestViol > Tol; pass++)
                {
                    // Pass 0: the equal-spacing samples. Pass 1: each node slides within half a sub-span of its spot.
                    float viol = TryInfill(a, b, n + extra, pass == 1, ks, ms);
                    if (viol < bestViol - Tol)
                    {
                        bestViol = viol;
                        bestKs.Clear(); bestKs.AddRange(ks);
                        bestMs.Clear(); bestMs.AddRange(ms);
                    }
                }
            }
            if (bestKs.Count == 0)
                return 0;
            for (int q = 0; q < bestKs.Count; q++)
                Insert(bestKs[q], bestMs[q], LiftPlanNodeFlags.Infill);
            return bestKs.Count;
        }

        /// <summary>Place n-1 infill nodes between working nodes a and b. It returns the summed violation of the n sub-spans (+inf when no
        /// placement satisfies the span-length rules).</summary>
        private float TryInfill(int a, int b, int n, bool slide, List<int> ks, List<int> ms)
        {
            ks.Clear();
            ms.Clear();
            int i0 = m_K[a], j = m_K[b];
            float rj = Rope(j, m_M[b]);
            int prev = i0;
            float rPrev = Rope(i0, m_M[a]);
            float total = 0f;
            float maxSub = LegacySpacing ? math.min(m_C.MaxSpanM, 1.5f * m_C.TypicalSpanM - m_C.StepM) : m_C.MaxSpanM;
            int minGap = MinGap();
            for (int r = n; r >= 2; r--)
            {
                float rest = m_S[j] - m_S[prev];
                float ideal = m_S[prev] + rest / r;
                float half = 0.5f * rest / r;
                int bestK = -1, bestM = -1, bestBad = 3;
                float bestV = float.PositiveInfinity, bestDist = float.PositiveInfinity;
                int kLo = slide ? (int)math.ceil((ideal - half) / m_C.StepM) : (int)math.round(ideal / m_C.StepM);
                int kHi = slide ? (int)math.floor((ideal + half) / m_C.StepM) : kLo;
                kLo = math.max(prev + minGap, kLo);
                kHi = math.min(j - minGap, kHi);
                for (int k = kLo; k <= kHi; k++)
                {
                    if (NearStation(k))
                        continue;
                    float sub = m_S[k] - m_S[prev];
                    float after = m_S[j] - m_S[k];
                    if (sub > maxSub + 1e-3f || after > (r - 1) * maxSub + 1e-3f)
                        continue;
                    for (int m = 0; m < m_C.Ladder.Length; m++)
                    {
                        if (!MemberAllowed(k, m) || !NodeOk(k, m))
                            continue;
                        float rk = Rope(k, m);
                        float v = math.max(SpanViolation(prev, rPrev, k, rk), 0f);
                        if (r == 2)
                            v += math.max(SpanViolation(k, rk, j, rj), 0f);
                        int bad = v > Tol ? 1 : 0;
                        float dist = math.abs(m_S[k] - ideal);
                        bool better;
                        if (bad != bestBad) better = bad < bestBad;
                        else if (bad > 0 && math.abs(v - bestV) > 0.05f) better = v < bestV;
                        else if (m != bestM) better = m < bestM;
                        else better = dist < bestDist - 1e-3f;
                        if (better)
                        {
                            bestK = k; bestM = m; bestBad = bad; bestV = v; bestDist = dist;
                        }
                        if (bad == 0)
                            break;
                    }
                }
                if (bestK < 0)
                    return float.PositiveInfinity;
                ks.Add(bestK);
                ms.Add(bestM);
                total += bestV;
                prev = bestK;
                rPrev = Rope(bestK, bestM);
            }
            if (m_S[j] - m_S[prev] > maxSub + 1e-3f && n > 1)
                return float.PositiveInfinity;
            return total;
        }

        // (c) elevation

        private void AssignMembers()
        {
            int count = m_K.Count;
            var before = new int[count];
            for (int i = 0; i < count; i++)
                before[i] = m_M[i];
            float violBefore = TotalViolation();

            for (int i = 0; i < count; i++)
            {
                if (m_M[i] < 0)
                    continue;
                int k = m_K[i];
                int m = 0;
                while (m < m_C.Ladder.Length - 1 && !(MemberAllowed(k, m) && NodeOk(k, m)))
                    m++;
                m_M[i] = m;
            }
            int guard = count * m_C.Ladder.Length + 4;
            while (guard-- > 0)
            {
                // The span with the worst floor shortfall.
                int worstSpan = -1, worstK = -1;
                float worstV = Tol;
                for (int s = 0; s < count - 1; s++)
                {
                    float v = SpanViolation(m_K[s], Rope(m_K[s], m_M[s]), m_K[s + 1], Rope(m_K[s + 1], m_M[s + 1]), out int wk, out bool floorType);
                    if (floorType && v > worstV)
                    {
                        worstV = v;
                        worstSpan = s;
                        worstK = wk;
                    }
                }
                if (worstSpan < 0)
                    break;
                int a = worstSpan, b = worstSpan + 1;
                float t = (m_S[worstK] - m_S[m_K[a]]) / (m_S[m_K[b]] - m_S[m_K[a]]);
                int first = t >= 0.5f ? b : a, second = first == a ? b : a;
                if (!Raise(first) && !Raise(second))
                    break;   // both ends are stations or at the top of the ladder: the violation is reported
            }

            if (TotalViolation() > violBefore + Tol)
            {
                for (int i = 0; i < count; i++)
                    m_M[i] = before[i];
                return;
            }
            // The station rule chose the members beside the stations (a tower bought for them included). The reset above must not undo
            // that. Where it raised one, that node returns to the search's member. If that breaks the line, the search's whole assignment
            // is kept (it was clear).
            if (TowerWorth > 0f && violBefore <= Tol && count >= 3 && EndRiseOf(m_M) > EndRiseOf(before) + 1e-3f)
            {
                if (StationEnd(0)) m_M[1] = before[1];
                if (StationEnd(count - 1)) m_M[count - 2] = before[count - 2];
                if (TotalViolation() > Tol)
                    for (int i = 0; i < count; i++)
                        m_M[i] = before[i];
            }
        }

        /// <summary>The station-side rise of a member assignment: Rise of the tower next to each station end.</summary>
        private float EndRiseOf(IList<int> members)
        {
            int count = m_K.Count;
            float r = 0f;
            if (StationEnd(0) && members[1] >= 0) r += Rise(members[1]);
            if (StationEnd(count - 1) && members[count - 2] >= 0) r += Rise(members[count - 2]);
            return r;
        }

        private bool Raise(int node)
        {
            int m = m_M[node];
            if (m < 0)
                return false;
            int k = m_K[node];
            for (int up = m + 1; up < m_C.Ladder.Length; up++)
            {
                if (MemberAllowed(k, up) && NodeOk(k, up) && SlopeOk(m_K[node - 1], m_M[node - 1], k, up) &&
                    SlopeOk(k, up, m_K[node + 1], m_M[node + 1]))
                {
                    m_M[node] = up;
                    return true;
                }
            }
            return false;
        }

        // Lean (DECISIONS 600): a tower whose rope pitch is past LiftAlignMath.LeanFreeDeg leans downhill about its foot. Its rope anchor
        // therefore moves downhill and a little lower (LiftAlignMath.LeanedAnchor). Pitch and lean are judged on the plumb rope points (the
        // search, the angle pass and the game's TowerLeanSystem all use them, so the lean never feeds back into itself). The spans hang
        // from the leaned anchors (Build). LeanPass raises a tower where only the lean leaves a span short.

        /// <summary>Working node <paramref name="n"/>'s rope pitch (ChordPitchDeg on the plumb rope points). It returns false at a station
        /// end or a line end without its outer point.</summary>
        private bool PitchAt(int n, out float pitchDeg)
        {
            pitchDeg = 0f;
            if (!Around(n, out float sP, out float rP, out float sN, out float rN))
                return false;
            pitchDeg = LiftAlignMath.ChordPitchDeg(sP, rP, NodeS(n), NodeR(n), sN, rN);
            return true;
        }

        /// <summary>Working node <paramref name="n"/>'s rope anchor (leaned when it is a tower whose pitch leans it, and the node's own
        /// rope point at the ends: their rope is given).</summary>
        private void AnchorOf(int n, out float s, out float r)
        {
            s = NodeS(n);
            r = NodeR(n);
            if (m_M[n] < 0 || !PitchAt(n, out float pitch))
                return;
            float lean = LiftAlignMath.LeanDeg(m_C.TypeId, pitch);
            if (lean > 0f)
                LiftAlignMath.LeanedAnchor(s, m_Centre[m_K[n]], r, pitch, lean, out s, out r);
        }

        /// <summary>The floor shortfall of working span <paramref name="span"/> hung from the two nodes' leaned anchors. The floor is
        /// judged on the leaned rope. The max rope height stays on the plumb rope points, as the search judged it. Reason: a lean lowers
        /// the anchor over its own foot. The few meters it moves the anchor downhill only lift a steep chord over the foot of a cliff past
        /// a rule that is about rope height over the tower (LP-5).</summary>
        private float LeanedSpanViolation(int span)
        {
            AnchorOf(span, out float sa, out float ra);
            AnchorOf(span + 1, out float sb, out float rb);
            int i = m_K[span], j = m_K[span + 1];
            float d = sb - sa;
            if (d <= 0f)
                return 0f;
            float sag4 = 4f * m_C.SagRatio * d, worst = float.NegativeInfinity;
            // a lean can carry an anchor past its own node (downhill), so the leaned span may reach samples beyond the node range
            int qLo = i + 1, qHi = j - 1;
            while (qLo > 0 && m_S[qLo - 1] >= sa - 1e-3f) qLo--;
            while (qHi < m_N && m_S[qHi + 1] <= sb + 1e-3f) qHi++;
            for (int q = qLo; q <= qHi; q++)
            {
                if (q == i || q == j || m_S[q] < sa - 1e-3f || m_S[q] > sb + 1e-3f)
                    continue;   // under a leaned head: the tower, not the rope, overhangs there
                float t = math.saturate((m_S[q] - sa) / d);
                float r = ra + (rb - ra) * t - sag4 * t * (1f - t);
                worst = math.max(worst, m_C.CarrierDropM + m_Floor[q] + m_High[q] - r);
            }
            return float.IsNegativeInfinity(worst) ? 0f : worst;
        }

        /// <summary>Where a lean leaves a span short that the plumb towers cleared (or worse than they did), the leaned end tower takes the
        /// next taller member that keeps its node and edges (Raise). The other end takes it when the first cannot. A raise is kept only
        /// when it lowers the summed leaned violation of the spans it touches (the lean of a taller tower moves its anchor further, and a
        /// taller rope can pass the max rope height). It stops when no raise helps.</summary>
        private void LeanPass()
        {
            int guard = m_K.Count * m_C.Ladder.Length + 4;
            while (guard-- > 0)
            {
                int worst = -1;
                float worstV = Tol;
                for (int s = 0; s < m_K.Count - 1; s++)
                {
                    float v = LeanedSpanViolation(s);
                    if (v <= Tol)
                        continue;
                    float plumb = math.max(0f, SpanViolation(m_K[s], NodeR(s), m_K[s + 1], NodeR(s + 1)));
                    if (v > plumb + Tol && v - plumb > worstV)
                    {
                        worstV = v - plumb;
                        worst = s;
                    }
                }
                if (worst < 0)
                    return;
                bool aLeans = m_M[worst] >= 0 && PitchAt(worst, out float pa) && LiftAlignMath.LeanDeg(m_C.TypeId, pa) > 0f;
                int first = aLeans ? worst : worst + 1, second = first == worst ? worst + 1 : worst;
                if (!StepHelps(first, +1) && !StepHelps(second, +1))
                    return;
            }
        }

        /// <summary>Summed positive leaned violation of the spans within two of working node <paramref name="n"/> (a raise changes its
        /// neighbors' pitch and lean too).</summary>
        private float LeanedAround(int n)
        {
            float v = 0f;
            for (int s = math.max(0, n - 2); s <= math.min(m_K.Count - 2, n + 1); s++)
                v += math.max(0f, LeanedSpanViolation(s)) + math.max(0f, SpanViolation(m_K[s], NodeR(s), m_K[s + 1], NodeR(s + 1)));
            return v;
        }

        /// <summary>Working node <paramref name="n"/> one ladder step taller (<paramref name="dir"/> +1) or shorter (-1) at a time, to the
        /// first member that keeps its node and both edges and lowers LeanedAround(n). Otherwise the member is restored. LeanPass uses +1
        /// only (a shorter step broke LP-5 idempotence: the search is lean-unaware).</summary>
        private bool StepHelps(int n, int dir)
        {
            if (n <= 0 || n >= m_K.Count - 1 || m_M[n] < 0)
                return false;
            int m0 = m_M[n], k = m_K[n];
            float before = LeanedAround(n);
            for (int m = m0 + dir; m >= 0 && m < m_C.Ladder.Length; m += dir)
            {
                if (!MemberAllowed(k, m) || !NodeOk(k, m) || !SlopeOk(m_K[n - 1], m_M[n - 1], k, m) || !SlopeOk(k, m, m_K[n + 1], m_M[n + 1]))
                    continue;
                m_M[n] = m;
                if (LeanedAround(n) < before - Tol)
                    return true;
            }
            m_M[n] = m0;
            return false;
        }

        private float TotalViolation()
        {
            float total = 0f;
            for (int s = 0; s < m_K.Count - 1; s++)
            {
                total += math.max(0f, SpanViolation(m_K[s], Rope(m_K[s], m_M[s]), m_K[s + 1], Rope(m_K[s + 1], m_M[s + 1])));
                if (CheckEdgeSlope && m_C.MaxEdgeSlope > 0f)
                    total += SlopeExcess(m_K[s], m_M[s], m_K[s + 1], m_M[s + 1]);
            }
            return total;
        }

        // (d) rope angles: breakover / hold-down pairs (breakover-holddown-plan-2026-11-03, section 2.3). A local post-pass like
        // BreakoverPass: the deflection at a tower needs both neighbors, so an exact search would carry the previous node in its state.

        private readonly List<string> m_AngleNotes = new List<string>();
        private long m_AngleWork;
        private double m_AnglesMs;
        private float m_MinCap;
        private float[] m_BeforeOver = new float[0];

        private void ResetAngles()
        {
            m_AngleNotes.Clear();
            m_AngleWork = 0;
            m_AnglesMs = 0;
            m_PrevAt.Clear();
        }

        private bool OuterA => !StationA && math.isfinite(EndOuterDistA) && math.isfinite(EndOuterRopeA) && EndOuterDistA > 1e-3f;
        private bool OuterB => !StationB && math.isfinite(EndOuterDistB) && math.isfinite(EndOuterRopeB) && EndOuterDistB > 1e-3f;

        private float NodeS(int n) => m_S[m_K[n]];
        private float NodeR(int n) => Rope(m_K[n], m_M[n]);

        /// <summary>Working node n stands as a tower: every inner node, a line end only where it joins a built line (not a station).</summary>
        private bool IsTower(int n)
        {
            int last = m_K.Count - 1;
            if (n < 0 || n > last) return false;
            if (n == 0) return !StationA;
            if (n == last) return !StationB;
            return true;
        }

        /// <summary>A fixed working node: a line end, or a pin / seed tower (the player's spot).</summary>
        private bool IsFixed(int n) => n == 0 || n == m_K.Count - 1 || (m_F[n] & LiftPlanNodeFlags.Seed) != 0;

        private readonly float[] m_GroupBuf = new float[6];

        /// <summary>True when the touched towers at <paramref name="sA"/> (and <paramref name="sB"/> when <paramref name="two"/>) would
        /// stand in a group of more than MaxGroup consecutive towers PairMaxM or nearer apart. The towers lie between the unchanged working
        /// nodes <paramref name="iL"/> and <paramref name="iR"/> (-1: none). The rule is pairs, never threes. Station ends are not
        /// towers.</summary>
        private bool Grouped(int iL, float sA, float sB, bool two, int iR)
        {
            float[] s = m_GroupBuf;
            int c = 0;
            if (iL >= 0 && IsTower(iL))
            {
                if (IsTower(iL - 1)) s[c++] = NodeS(iL - 1);
                s[c++] = NodeS(iL);
            }
            int t0 = c;
            s[c++] = sA;
            if (two) s[c++] = sB;
            int t1 = c - 1;
            if (iR >= 0 && IsTower(iR))
            {
                s[c++] = NodeS(iR);
                if (IsTower(iR + 1)) s[c++] = NodeS(iR + 1);
            }
            int start = 0;
            for (int i = 1; i <= c; i++)
            {
                if (i < c && s[i] - s[i - 1] <= LiftTowerPlanConstants.PairMaxM + 1e-3f)
                    continue;
                // the run start .. i - 1
                if (i - start > LiftTowerPlanConstants.MaxGroup && start <= t1 && i - 1 >= t0)
                    return true;
                start = i;
            }
            return false;
        }

        private static float3 RopePoint(float alongM, float ropeY) => new float3(0f, ropeY, alongM);

        /// <summary>How far, degrees, the rope at a tower with these rope points (along, y) is past its train's cap (&lt;= 0 = within),
        /// and its deflection. A deflection under every cap of the type skips the load cases.</summary>
        private float Over(float sP, float rP, float s, float r, float sN, float rN, out float d)
        {
            m_AngleWork++;
            float3 p = RopePoint(sP, rP), q = RopePoint(s, r), n = RopePoint(sN, rN);
            LiftAlignMath.RopeAngles(p, q, n, m_C.SagRatio, out float inA, out float outA);
            d = math.degrees(inA - outA);
            float pitchOver = math.abs(LiftAlignMath.ChordPitchDeg(sP, rP, s, r, sN, rN)) - m_C.PitchCapDeg;
            // the carrier clearance (DECISIONS 600 c): the steeper rope half's world pitch against its kind's limit
            float half = math.degrees(math.max(math.abs(inA), math.abs(outA)));
            float ad = math.abs(d);
            if (ad <= m_MinCap && half <= m_C.MinWorldHalfMaxDeg)
                return math.max(math.max(ad - m_MinCap, pitchOver), half - m_C.MinWorldHalfMaxDeg);
            LiftAlignMath.LoadCaseDeflections(p, q, n, m_C.SagRatio, out float tight, out float heavy);
            LiftTrainKind kind = LiftAlignMath.ClassifyTrain(tight, heavy);
            return math.max(math.max(ad - m_C.CapDeg(kind), pitchOver), half - m_C.WorldHalfMaxDeg(kind));
        }

        /// <summary>The rope points beside working node <paramref name="n"/>: false when it is not judged (a station end, or a line end
        /// without its built neighbor's point).</summary>
        private bool Around(int n, out float sP, out float rP, out float sN, out float rN)
        {
            int last = m_K.Count - 1;
            sP = rP = sN = rN = 0f;
            if (n < 0 || n > last || last < 1)
                return false;
            if (n == 0)
            {
                if (!OuterA)
                    return false;
                sP = -EndOuterDistA;
                rP = EndOuterRopeA;
            }
            else
            {
                sP = NodeS(n - 1);
                rP = NodeR(n - 1);
            }
            if (n == last)
            {
                if (!OuterB)
                    return false;
                sN = m_Length + EndOuterDistB;
                rN = EndOuterRopeB;
            }
            else
            {
                sN = NodeS(n + 1);
                rN = NodeR(n + 1);
            }
            return true;
        }

        private float OverAt(int n, out float d)
        {
            d = 0f;
            if (!Around(n, out float sP, out float rP, out float sN, out float rN))
                return float.NegativeInfinity;
            return Over(sP, rP, NodeS(n), NodeR(n), sN, rN, out d);
        }

        /// <summary>The worst tower over its cap first, re-evaluated after every change, at most one repair per original tower (towers a
        /// repair creates are final). Ties go to the lower index.</summary>
        private void AnglePass()
        {
            m_UseSag = true;
            m_MinCap = math.min(m_C.CapDeg(LiftTrainKind.Support), math.min(m_C.CapDeg(LiftTrainKind.HoldDown), m_C.CapDeg(LiftTrainKind.PushPull)));
            var done = new List<int>();   // sample indices handled: repaired, created by a repair, or found unrepairable
            for (int guard = 2 * m_K.Count + 8; guard > 0; guard--)
            {
                int worst = -1;
                float worstOver = LiftTowerPlan.AngleTolDeg, worstD = 0f;
                for (int n = 0; n < m_K.Count; n++)
                {
                    if (done.Contains(m_K[n]))
                        continue;
                    float over = OverAt(n, out float d);
                    if (over > worstOver + 1e-4f)
                    {
                        worstOver = over;
                        worstD = d;
                        worst = n;
                    }
                }
                if (worst < 0)
                    break;
                done.Add(m_K[worst]);
                Repair(worst, worstD, worstOver, done);
            }
        }

        private struct AngleRej
        {
            public int Station, Removed, Span, Cap, Neighbor, Group;
            public bool Pin;
        }

        /// <summary>One candidate repair. Kind 1 is single (node n to (K1, M1)). Kind 2 is pair (n -> (K1, M1) + new (K2, M2)). Kind 3 is
        /// partner (n keeps its spot with member M1, new (K2, M2)). Kind 4 is re-member (n gets M1, working node K2 gets M2, two fixed
        /// towers PairMaxM or nearer). Kind 5 is pull (fixed n gets M1, its free neighbor, working node K1, moves to (K2, M2) as the
        /// partner).</summary>
        private struct AngleFix
        {
            public int Kind, Towers, Steps, K1, M1, K2, M2;
            public float MaxD, GapM, DistM, D1, D2;

            /// <summary>Rank: fewer towers, fewer ladder steps, smaller max |d|, shorter spacing (reads as a pair), nearer the old spot. A
            /// tie keeps the earlier candidate (ascending sample, then member: deterministic).</summary>
            public bool Better(AngleFix o)
            {
                if (o.Kind == 0) return true;
                if (Towers != o.Towers) return Towers < o.Towers;
                if (Steps != o.Steps) return Steps < o.Steps;
                if (math.abs(MaxD - o.MaxD) > 1e-3f) return MaxD < o.MaxD;
                if (math.abs(GapM - o.GapM) > 1e-3f) return GapM < o.GapM;
                return DistM < o.DistM - 1e-3f;
            }
        }

        /// <summary>The span (k, mk) -> (k2, mk2) as the planner builds it: within MaxSpanM, clear with its sag and within the edge slope rule.</summary>
        private bool SpanFine(int k, int mk, int k2, int mk2)
        {
            if (m_S[k2] - m_S[k] > m_C.MaxSpanM + 1e-3f || k2 <= k)
                return false;
            return SpanViolation(k, Rope(k, mk), k2, Rope(k2, mk2)) <= Tol && SlopeOk(k, mk, k2, mk2);
        }

        /// <summary>A neighbor's over-cap before the repair (cached per working index for one repair).</summary>
        private float BeforeOver(int q)
        {
            if (!math.isnan(m_BeforeOver[q]))
                return m_BeforeOver[q];
            float v = OverAt(q, out _);
            m_BeforeOver[q] = v;
            return v;
        }

        /// <summary>Judges a local change: touched towers T1 = (kA, mA) [and T2 = (kB, mB)], in line order. They lie between the unchanged
        /// working nodes iL (-1: T1 is the A end, its left point the outer A point) and iR (-1: the last T is the B end, outer B). A member
        /// of -1 is a line end (its rope fixed). True when every touched span is fine, the touched towers are within their caps and the
        /// unchanged neighbors are pushed no further past theirs. <paramref name="leftOk"/> / <paramref name="rightOk"/> cache the outer
        /// spans per (k, m) (0 unknown, 1 fine, 2 not).</summary>
        private bool Judge(int iL, int iR, int nT, int kA, int mA, int kB, int mB, sbyte[] leftOk, sbyte[] rightOk, ref AngleRej rej,
                           out float dA, out float dB)
        {
            dA = dB = 0f;
            int L = m_C.Ladder.Length;
            int kLast = nT == 2 ? kB : kA, mLast = nT == 2 ? mB : mA;
            // spans
            if (iL >= 0)
            {
                int c = kA * L + mA;
                if (leftOk[c] == 0)
                    leftOk[c] = (sbyte)(SpanFine(m_K[iL], m_M[iL], kA, mA) ? 1 : 2);
                if (leftOk[c] == 2) { rej.Span++; return false; }
            }
            if (iR >= 0 && mLast >= 0)
            {
                int c = kLast * L + mLast;
                if (rightOk[c] == 0)
                    rightOk[c] = (sbyte)(SpanFine(kLast, mLast, m_K[iR], m_M[iR]) ? 1 : 2);
                if (rightOk[c] == 2) { rej.Span++; return false; }
            }
            else if (iR >= 0 && !SpanFine(kLast, mLast, m_K[iR], m_M[iR]))
            {
                rej.Span++;
                return false;
            }
            if (nT == 2 && !SpanFine(kA, mA, kB, mB)) { rej.Span++; return false; }
            // points
            float sL, rL, sR, rR;
            if (iL >= 0) { sL = NodeS(iL); rL = NodeR(iL); }
            else { sL = -EndOuterDistA; rL = EndOuterRopeA; }
            if (iR >= 0) { sR = NodeS(iR); rR = NodeR(iR); }
            else { sR = m_Length + EndOuterDistB; rR = EndOuterRopeB; }
            float sA = m_S[kA], rA = Rope(kA, mA), sB = nT == 2 ? m_S[kB] : sR, rB = nT == 2 ? Rope(kB, mB) : rR;
            // the touched towers' caps (an end without its outer point is not judged)
            bool judgeA = iL >= 0 || OuterA;
            if (judgeA && Over(sL, rL, sA, rA, sB, rB, out dA) > LiftTowerPlan.AngleTolDeg) { rej.Cap++; return false; }
            if (nT == 2)
            {
                bool judgeB = iR >= 0 || OuterB;
                if (judgeB && Over(sA, rA, sB, rB, sR, rR, out dB) > LiftTowerPlan.AngleTolDeg) { rej.Cap++; return false; }
            }
            // the unchanged neighbors
            float sT = nT == 2 ? sB : sA, rT = nT == 2 ? rB : rA;
            if (iL >= 0 && Around(iL, out float sP, out float rP, out _, out _) &&
                Over(sP, rP, sL, rL, sA, rA, out _) > math.max(0f, BeforeOver(iL)) + LiftTowerPlan.AngleTolDeg)
            {
                rej.Neighbor++;
                return false;
            }
            if (iR >= 0 && Around(iR, out _, out _, out float sN, out float rN) &&
                Over(sT, rT, sR, rR, sN, rN, out _) > math.max(0f, BeforeOver(iR)) + LiftTowerPlan.AngleTolDeg)
            {
                rej.Neighbor++;
                return false;
            }
            return true;
        }

        private int MemberSum()
        {
            int s = 0;
            for (int i = 0; i < m_M.Count; i++)
                s += math.max(0, m_M[i]);
            return s;
        }

        /// <summary>Repairs working node <paramref name="n"/> (deflection <paramref name="d0"/>, <paramref name="over0"/> past its cap). It
        /// picks the cheapest feasible of these:
        /// (a) the tower shifted within AngleShiftM with any member, or into a fixed neighbor's partner window (a pin or a seed keeps its
        /// spot and only changes member).
        /// (b) a free tower replaced by a pair PairMinM..PairMaxM apart that straddles its spot or stands within AngleShiftM of it. A fixed
        /// tower (a pin, a seed, the junction end) keeps its spot. Its free neighbor moves in as its partner (no tower added), or a new
        /// free partner stands beside it. Neither applies when a fixed neighbor stands PairMaxM or nearer (two pins 10 m apart are the
        /// player's choice: only their members change).
        /// No candidate may leave three towers in a row PairMaxM or nearer apart (MaxGroup). If none is feasible, the node is flagged
        /// OverCap and logged with the reasons.</summary>
        private void Repair(int n, float d0, float over0, List<int> done)
        {
            int count = m_K.Count, last = count - 1, L = m_C.Ladder.Length;
            if (m_BeforeOver.Length < count + 2)
                m_BeforeOver = new float[count + 8];
            for (int i = 0; i < m_BeforeOver.Length; i++)
                m_BeforeOver[i] = float.NaN;
            int k0 = m_K[n], m0 = m_M[n];
            float s0 = m_S[k0];
            bool isEnd = n == 0 || n == last;
            bool fixedNode = isEnd || (m_F[n] & LiftPlanNodeFlags.Seed) != 0;
            int baseSteps = MemberSum() - math.max(0, m0);
            int minGap = MinGap();
            int pairMaxS = (int)math.floor(LiftTowerPlanConstants.PairMaxM / m_C.StepM + 1e-4f);
            int pairMinS = math.max(1, (int)math.ceil(m_C.PairMinM / m_C.StepM - 1e-4f));
            var rej = new AngleRej();
            var best = new AngleFix();
            int cache = (m_N + 1) * L;
            var leftOk = new sbyte[cache];
            var rightOk = new sbyte[cache];

            // (a) single: shifted (free) or re-membered in place (fixed). A free tower PairMaxM or nearer to a fixed tower (a pin, a seed,
            // the junction end) may also move anywhere in that tower's partner window (PairMinM..PairMaxM on its side). The free planner's
            // filler beside a pin becomes the pin's partner instead of a third tower beside a new pair.
            if (!isEnd)
            {
                int shift = fixedNode ? 0 : (int)math.floor(LiftTowerPlanConstants.AngleShiftM / m_C.StepM + 1e-4f);
                int kLo = math.max(m_K[n - 1] + minGap, k0 - shift), kHi = math.min(m_K[n + 1] - minGap, k0 + shift);
                if (fixedNode)
                    kLo = kHi = k0;
                else
                {
                    if (IsFixed(n - 1) && IsTower(n - 1) && s0 - NodeS(n - 1) <= LiftTowerPlanConstants.PairMaxM + 1e-3f)
                        kHi = math.max(kHi, math.min(m_K[n + 1] - minGap, m_K[n - 1] + pairMaxS));
                    if (IsFixed(n + 1) && IsTower(n + 1) && NodeS(n + 1) - s0 <= LiftTowerPlanConstants.PairMaxM + 1e-3f)
                        kLo = math.min(kLo, math.max(m_K[n - 1] + minGap, m_K[n + 1] - pairMaxS));
                }
                for (int k = kLo; k <= kHi; k++)
                {
                    if (k != k0 && NearStation(k)) { rej.Station++; continue; }
                    if (k != k0 && IsForbidden(k)) { rej.Removed++; continue; }
                    if (k != k0 && Grouped(n - 1, m_S[k], 0f, false, n + 1)) { rej.Group++; continue; }
                    for (int m = 0; m < L; m++)
                    {
                        if (!MemberAllowed(k, m) || !NodeOk(k, m)) { rej.Span++; continue; }
                        var c = new AngleFix { Kind = 1, Towers = 0, Steps = baseSteps + m, K1 = k, M1 = m, DistM = math.abs(m_S[k] - s0) };
                        if (best.Kind != 0 && c.Steps > best.Steps)
                            break;   // members ascend: every later member has more steps
                        if (!Judge(n - 1, n + 1, 1, k, m, 0, 0, leftOk, rightOk, ref rej, out float dA, out _))
                            continue;
                        c.MaxD = math.abs(dA);
                        c.D1 = dA;
                        if (c.Better(best))
                            best = c;
                    }
                }
            }

            // (b) pair / partner / re-member (only when no single fix exists: a single adds no tower and ranks first). The tram comes here
            // for its pitch cap or its saddle cap (DECISIONS 600 / 600 a): a breakover pair on a crest or shoulder is the one place it
            // takes a short span. Its long spans elsewhere are untouched.
            if (best.Kind == 0)
            {
                Array.Clear(leftOk, 0, leftOk.Length);
                Array.Clear(rightOk, 0, rightOk.Length);
                if (!fixedNode)
                {
                    // the pair stands within AngleShiftM of the old spot (it straddles it, or lies beside it: the planner's tall single
                    // often stands past the ground's break, where clearance put it)
                    int shiftS = (int)math.floor(LiftTowerPlanConstants.AngleShiftM / m_C.StepM + 1e-4f);
                    int kLo = math.max(m_K[n - 1] + minGap, k0 - pairMaxS), kHi1 = math.min(m_K[n + 1] - minGap - pairMinS, k0 + shiftS);
                    for (int k1 = kLo; k1 <= kHi1; k1++)
                    {
                        if (k1 != k0 && NearStation(k1)) { rej.Station++; continue; }
                        if (k1 != k0 && IsForbidden(k1)) { rej.Removed++; continue; }
                        int k2Lo = math.max(k0 - shiftS, k1 + pairMinS), k2Hi = math.min(m_K[n + 1] - minGap, k1 + pairMaxS);
                        for (int k2 = k2Lo; k2 <= k2Hi; k2++)
                        {
                            float gap = m_S[k2] - m_S[k1];
                            if (k2 == k1 || gap < m_C.PairMinM - 1e-3f || gap > LiftTowerPlanConstants.PairMaxM + 1e-3f)
                                continue;
                            if (k2 != k0 && NearStation(k2)) { rej.Station++; continue; }
                            if (k2 != k0 && IsForbidden(k2)) { rej.Removed++; continue; }
                            if (Grouped(n - 1, m_S[k1], m_S[k2], true, n + 1)) { rej.Group++; continue; }
                            for (int m1 = 0; m1 < L; m1++)
                            {
                                if (!MemberAllowed(k1, m1) || !NodeOk(k1, m1)) { rej.Span++; continue; }
                                for (int m2 = 0; m2 < L; m2++)
                                {
                                    if (!MemberAllowed(k2, m2) || !NodeOk(k2, m2)) { rej.Span++; continue; }
                                    var c = new AngleFix
                                    {
                                        Kind = 2, Towers = 1, Steps = baseSteps + m1 + m2, K1 = k1, M1 = m1, K2 = k2, M2 = m2, GapM = gap,
                                        DistM = math.abs(0.5f * (m_S[k1] + m_S[k2]) - s0),
                                    };
                                    if (best.Kind != 0 && c.Steps > best.Steps)
                                        break;   // members ascend: every later m2 has more steps
                                    if (!Judge(n - 1, n + 1, 2, k1, m1, k2, m2, leftOk, rightOk, ref rej, out float dA, out float dB))
                                        continue;
                                    c.MaxD = math.max(math.abs(dA), math.abs(dB));
                                    c.D1 = dA;
                                    c.D2 = dB;
                                    if (c.Better(best))
                                        best = c;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // A fixed neighbor (pin / seed tower) PairMaxM or nearer: the two are the player's pair, judged and re-membered only.
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int q = n + side;
                        if (q <= 0 || q >= last || (m_F[q] & LiftPlanNodeFlags.Seed) == 0 ||
                            math.abs(m_S[m_K[q]] - s0) > LiftTowerPlanConstants.PairMaxM + 1e-3f)
                            continue;
                        rej.Pin = true;
                        if (isEnd)
                            continue;
                        int lo = side > 0 ? n : q, hi = side > 0 ? q : n;
                        int iL = lo - 1, iR = hi + 1;
                        Array.Clear(leftOk, 0, leftOk.Length);
                        Array.Clear(rightOk, 0, rightOk.Length);
                        int stepsQ = baseSteps - math.max(0, m_M[q]);
                        for (int ma = 0; ma < L; ma++)
                        {
                            if (!MemberAllowed(m_K[lo], ma) || !NodeOk(m_K[lo], ma)) continue;
                            for (int mb = 0; mb < L; mb++)
                            {
                                if (!MemberAllowed(m_K[hi], mb) || !NodeOk(m_K[hi], mb)) continue;
                                int mn = side > 0 ? ma : mb, mq = side > 0 ? mb : ma;
                                var c = new AngleFix
                                {
                                    Kind = 4, Towers = 0, Steps = stepsQ + ma + mb, M1 = mn, K2 = q, M2 = mq,
                                    GapM = math.abs(m_S[m_K[q]] - s0),
                                };
                                if (best.Kind != 0 && c.Steps > best.Steps)
                                    break;
                                if (!Judge(iL, iR >= count ? -1 : iR, 2, m_K[lo], ma, m_K[hi], mb, leftOk, rightOk, ref rej, out float dA, out float dB))
                                    continue;
                                c.MaxD = math.max(math.abs(dA), math.abs(dB));
                                c.D1 = side > 0 ? dA : dB;
                                c.D2 = side > 0 ? dB : dA;
                                if (c.Better(best))
                                    best = c;
                            }
                        }
                    }
                    if (!rej.Pin)
                    {
                        // Pull: the free neighbor moves into the fixed tower's partner window with any member (no tower added).
                        for (int side = -1; side <= 1; side += 2)
                        {
                            int q = n + side, far = q + side;
                            if (q <= 0 || q >= last || IsFixed(q) || far < 0 || far > last)
                                continue;
                            Array.Clear(leftOk, 0, leftOk.Length);
                            Array.Clear(rightOk, 0, rightOk.Length);
                            int kLo = side > 0 ? k0 + pairMinS : math.max(m_K[far] + minGap, k0 - pairMaxS);
                            int kHi = side > 0 ? math.min(m_K[far] - minGap, k0 + pairMaxS) : k0 - pairMinS;
                            int mnLo = isEnd ? -1 : 0, mnHi = isEnd ? -1 : L - 1;
                            int stepsQ = baseSteps - math.max(0, m_M[q]);
                            for (int k = kLo; k <= kHi; k++)
                            {
                                float gap = math.abs(m_S[k] - s0);
                                if (gap < m_C.PairMinM - 1e-3f || gap > LiftTowerPlanConstants.PairMaxM + 1e-3f)
                                    continue;
                                if (NearStation(k)) { rej.Station++; continue; }
                                if (IsForbidden(k)) { rej.Removed++; continue; }
                                if (side > 0 ? Grouped(n - 1, s0, m_S[k], true, far) : Grouped(far, m_S[k], s0, true, n == last ? -1 : n + 1))
                                {
                                    rej.Group++;
                                    continue;
                                }
                                for (int mn = mnLo; mn <= mnHi; mn++)
                                {
                                    if (mn >= 0 && (!MemberAllowed(k0, mn) || !NodeOk(k0, mn))) continue;
                                    for (int mp = 0; mp < L; mp++)
                                    {
                                        if (!MemberAllowed(k, mp) || !NodeOk(k, mp)) { rej.Span++; continue; }
                                        var c = new AngleFix
                                        {
                                            Kind = 5, Towers = 0, Steps = stepsQ + math.max(0, mn) + mp, K1 = q, M1 = mn, K2 = k, M2 = mp, GapM = gap,
                                            DistM = math.abs(m_S[k] - NodeS(q)),
                                        };
                                        if (best.Kind != 0 && c.Steps > best.Steps)
                                            break;
                                        bool ok;
                                        float dN, dP;
                                        if (side > 0)
                                            ok = Judge(n - 1, far, 2, k0, mn, k, mp, leftOk, rightOk, ref rej, out dN, out dP);
                                        else
                                            ok = Judge(far, n == last ? -1 : n + 1, 2, k, mp, k0, mn, leftOk, rightOk, ref rej, out dP, out dN);
                                        if (!ok)
                                            continue;
                                        c.MaxD = math.max(math.abs(dN), math.abs(dP));
                                        c.D1 = dN;
                                        c.D2 = dP;
                                        if (c.Better(best))
                                            best = c;
                                    }
                                }
                            }
                        }
                        for (int side = -1; side <= 1; side += 2)
                        {
                            int q = n + side;   // the neighbor on that side: the partner stands between n and q
                            if (q < 0 || q > last)
                                continue;
                            Array.Clear(leftOk, 0, leftOk.Length);
                            Array.Clear(rightOk, 0, rightOk.Length);
                            int kLo = side > 0 ? k0 + pairMinS : math.max(m_K[q] + minGap, k0 - pairMaxS);
                            int kHi = side > 0 ? math.min(m_K[q] - minGap, k0 + pairMaxS) : k0 - pairMinS;
                            int mnLo = isEnd ? -1 : 0, mnHi = isEnd ? -1 : L - 1;
                            for (int k = kLo; k <= kHi; k++)
                            {
                                float gap = math.abs(m_S[k] - s0);
                                if (gap < m_C.PairMinM - 1e-3f || gap > LiftTowerPlanConstants.PairMaxM + 1e-3f)
                                    continue;
                                if (NearStation(k)) { rej.Station++; continue; }
                                if (IsForbidden(k)) { rej.Removed++; continue; }
                                if (side > 0 ? Grouped(n - 1, s0, m_S[k], true, q) : Grouped(q, m_S[k], s0, true, n == last ? -1 : n + 1))
                                {
                                    rej.Group++;
                                    continue;
                                }
                                for (int mn = mnLo; mn <= mnHi; mn++)
                                {
                                    if (mn >= 0 && (!MemberAllowed(k0, mn) || !NodeOk(k0, mn))) continue;
                                    for (int mp = 0; mp < L; mp++)
                                    {
                                        if (!MemberAllowed(k, mp) || !NodeOk(k, mp)) { rej.Span++; continue; }
                                        var c = new AngleFix
                                        {
                                            Kind = 3, Towers = 1, Steps = baseSteps + math.max(0, mn) + mp, M1 = mn, K2 = k, M2 = mp, GapM = gap,
                                            DistM = 0.5f * gap,
                                        };
                                        if (best.Kind != 0 && c.Steps > best.Steps)
                                            break;
                                        bool ok;
                                        float dN, dP;
                                        if (side > 0)
                                        {
                                            ok = Judge(n - 1, q, 2, k0, mn, k, mp, leftOk, rightOk, ref rej, out dN, out dP);
                                        }
                                        else
                                        {
                                            ok = Judge(q, n == last ? -1 : n + 1, 2, k, mp, k0, mn, leftOk, rightOk, ref rej, out dP, out dN);
                                        }
                                        if (!ok)
                                            continue;
                                        c.MaxD = math.max(math.abs(dN), math.abs(dP));
                                        c.D1 = dN;
                                        c.D2 = dP;
                                        if (c.Better(best))
                                            best = c;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // (c) a longer span (the last resort): a free tower is dropped when the span over its spot is fine (clear, within MaxSpanM and
            // the edge slope rule) and neither neighbor is pushed further past its caps.
            if (best.Kind == 0 && !fixedNode && n > 0 && n < last && SpanFine(m_K[n - 1], m_M[n - 1], m_K[n + 1], m_M[n + 1]))
            {
                bool ok = true;
                if (Around(n - 1, out float sP, out float rP, out _, out _))
                    ok = Over(sP, rP, NodeS(n - 1), NodeR(n - 1), NodeS(n + 1), NodeR(n + 1), out _) <= math.max(0f, BeforeOver(n - 1)) + LiftTowerPlan.AngleTolDeg;
                if (ok && Around(n + 1, out _, out _, out float sN, out float rN))
                    ok = Over(NodeS(n - 1), NodeR(n - 1), NodeS(n + 1), NodeR(n + 1), sN, rN, out _) <= math.max(0f, BeforeOver(n + 1)) + LiftTowerPlan.AngleTolDeg;
                if (ok)
                    best = new AngleFix { Kind = 6, Towers = -1 };
                else
                    rej.Neighbor++;
            }

            if (best.Kind == 0 && rej.Removed > 0 && !m_IgnoreForbidden && Keepouts != null && Keepouts.Count > 0)
            {
                // a road stretch yields where the angle has no other fix (FIX-LIST 62, KO-3). The removed spots yield with it, as in the
                // MinTowers retry. Nothing above changed the nodes, so the search runs again from the same state.
                m_IgnoreForbidden = true;
                try { Repair(n, d0, over0, done); }
                finally { m_IgnoreForbidden = false; }
                return;
            }

            string at = (isEnd ? "end " : "p") + NoteNode(n) + " at " + NoteAt(s0) + " m (" +
                        (m0 >= 0 ? Short(m_C.Ladder[m0].Name) : "line end") + (fixedNode && !isEnd ? ", pinned / seed" : string.Empty) + "): d " +
                        LiftTowerPlan.Sg(d0) + (PitchAt(n, out float p0) ? ", pitch " + LiftTowerPlan.Sg(m_FromB ? -p0 : p0) : string.Empty) + ", " +
                        LiftTowerPlan.F(over0, "F1") + " past its cap" + (m_C.HasTrains
                            ? (math.isfinite(m_C.MinWorldHalfMaxDeg) ? " (bend, pitch or carrier clearance)" : string.Empty)
                            : " (saddle bend " + LiftTowerPlan.F(2f * m_C.SaddleHalfCapDeg, "F0") + " or pitch)");
            LiftPlanNodeFlags pairFlags = (m_F[n] & ~LiftPlanNodeFlags.Seed & ~LiftPlanNodeFlags.Station) | LiftPlanNodeFlags.Pair;
            switch (best.Kind)
            {
                case 1:
                {
                    m_K[n] = best.K1;
                    m_M[n] = best.M1;
                    done.Add(best.K1);
                    // a shift next to a tower PairMaxM or nearer makes the two a pair
                    string with = string.Empty;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int q = n + side;
                        if (best.K1 == k0 || !IsTower(q) || math.abs(NodeS(q) - m_S[best.K1]) > LiftTowerPlanConstants.PairMaxM + 1e-3f)
                            continue;
                        m_F[n] |= LiftPlanNodeFlags.Pair;
                        m_F[q] |= LiftPlanNodeFlags.Pair;
                        with = ", the partner of the " + (IsFixed(q) ? "fixed " : string.Empty) + "tower at " + NoteAt(NodeS(q)) + " m";
                    }
                    m_AngleNotes.Add(at + " -> " + (best.K1 == k0 ? "member " : "shifted to " + NoteAt(m_S[best.K1]) + " m, ") +
                                     Short(m_C.Ladder[best.M1].Name) + ", d " + LiftTowerPlan.Sg(best.D1) + with);
                    break;
                }
                case 2:
                {
                    m_K[n] = best.K1;
                    m_M[n] = best.M1;
                    m_F[n] = pairFlags;
                    Insert(best.K2, best.M2, pairFlags);
                    done.Add(best.K1);
                    done.Add(best.K2);
                    // the pair in the caller's order along the line (working from B, K2 comes first)
                    bool flip = m_FromB;
                    int ka = flip ? best.K2 : best.K1, kb = flip ? best.K1 : best.K2, ma = flip ? best.M2 : best.M1, mb = flip ? best.M1 : best.M2;
                    float da = flip ? best.D2 : best.D1, db = flip ? best.D1 : best.D2;
                    m_AngleNotes.Add(at + " -> PAIR " + NoteAt(m_S[ka]) + " / " + NoteAt(m_S[kb]) + " m (" +
                                     LiftTowerPlan.F(best.GapM, "F0") + " m apart) " + Short(m_C.Ladder[ma].Name) + " + " +
                                     Short(m_C.Ladder[mb].Name) + ", d " + LiftTowerPlan.Sg(da) + " / " + LiftTowerPlan.Sg(db));
                    break;
                }
                case 3:
                    if (best.M1 >= 0)
                        m_M[n] = best.M1;
                    m_F[n] |= LiftPlanNodeFlags.Pair;
                    Insert(best.K2, best.M2, LiftPlanNodeFlags.Infill | LiftPlanNodeFlags.Pair);
                    done.Add(best.K2);
                    m_AngleNotes.Add(at + " -> PARTNER at " + NoteAt(m_S[best.K2]) + " m (" + LiftTowerPlan.F(best.GapM, "F0") +
                                     " m away) " + Short(m_C.Ladder[best.M2].Name) + (best.M1 >= 0 ? ", the fixed tower " + Short(m_C.Ladder[best.M1].Name) : string.Empty) +
                                     ", d " + LiftTowerPlan.Sg(best.D1) + " / " + LiftTowerPlan.Sg(best.D2));
                    break;
                case 5:
                {
                    float from = NodeS(best.K1);
                    if (best.M1 >= 0)
                        m_M[n] = best.M1;
                    m_K[best.K1] = best.K2;
                    m_M[best.K1] = best.M2;
                    m_F[n] |= LiftPlanNodeFlags.Pair;
                    m_F[best.K1] = (m_F[best.K1] & ~LiftPlanNodeFlags.Seed) | LiftPlanNodeFlags.Pair;
                    done.Add(best.K2);
                    m_AngleNotes.Add(at + " -> PULLED the tower at " + NoteAt(from) + " m to " + NoteAt(m_S[best.K2]) +
                                     " m as its partner (" + LiftTowerPlan.F(best.GapM, "F0") + " m away) " + Short(m_C.Ladder[best.M2].Name) +
                                     (best.M1 >= 0 ? ", the fixed tower " + Short(m_C.Ladder[best.M1].Name) : string.Empty) +
                                     ", d " + LiftTowerPlan.Sg(best.D1) + " / " + LiftTowerPlan.Sg(best.D2));
                    break;
                }
                case 4:
                    m_M[n] = best.M1;
                    m_M[best.K2] = best.M2;
                    m_F[n] |= LiftPlanNodeFlags.Pair;
                    m_F[best.K2] |= LiftPlanNodeFlags.Pair;
                    done.Add(m_K[best.K2]);
                    m_AngleNotes.Add(at + " -> the fixed pair re-membered " + Short(m_C.Ladder[best.M1].Name) + " + " +
                                     Short(m_C.Ladder[best.M2].Name) + ", d " + LiftTowerPlan.Sg(best.D1) + " / " + LiftTowerPlan.Sg(best.D2));
                    break;
                case 6:
                    m_K.RemoveAt(n);
                    m_M.RemoveAt(n);
                    m_F.RemoveAt(n);
                    m_AngleNotes.Add(at + " -> DROPPED, one longer span over its spot");
                    break;
                default:
                    m_F[n] |= LiftPlanNodeFlags.OverCap;
                    m_AngleNotes.Add(at + " -> NO REPAIR, the tower stays (" + RejText(rej) + ")");
                    break;
            }
        }

        private static string RejText(AngleRej r)
        {
            var sb = new StringBuilder();
            if (r.Pin) sb.Append("a pinned tower beside it PairMaxM or nearer: the player's pair, members only; ");
            sb.Append("candidates refused: ").Append(r.Cap).Append(" over a cap, ").Append(r.Span).Append(" no clear span, ")
              .Append(r.Neighbor).Append(" a neighbor pushed past its cap, ").Append(r.Station).Append(" station clearance, ")
              .Append(r.Removed).Append(" removed spot, ").Append(r.Group).Append(" a group of three");
            return sb.ToString();
        }

        private static string Short(string member) => member.Replace("SkiLiftTower", string.Empty).Replace("SkiTramTower", "T");

        /// <summary>Build's phase (d) numbers on the output nodes: deflection, load cases, kind and cap of every judged node.</summary>
        private void SetAngles(LiftPlanNode[] nodes)
        {
            for (int i = 0; i < nodes.Length; i++)
                AnglesAt(nodes, i);
        }

        /// <summary>SetAngles for one node (its kind without hysteresis, the cap of its current head, and AssignHeads refines
        /// both).</summary>
        private void AnglesAt(LiftPlanNode[] nodes, int i)
        {
            int last = nodes.Length - 1;
            if (last < 1 || i < 0 || i > last)
                return;
            float sP, rP, sN, rN;
            if (i == 0)
            {
                if (!OuterA) return;
                sP = -EndOuterDistA; rP = EndOuterRopeA;
            }
            else
            {
                sP = nodes[i - 1].AlongM; rP = nodes[i - 1].RopeM;
            }
            if (i == last)
            {
                if (!OuterB) return;
                sN = m_Length + EndOuterDistB; rN = EndOuterRopeB;
            }
            else
            {
                sN = nodes[i + 1].AlongM; rN = nodes[i + 1].RopeM;
            }
            float3 p = RopePoint(sP, rP), q = RopePoint(nodes[i].AlongM, nodes[i].RopeM), n = RopePoint(sN, rN);
            float h = m_C.SagRatio;
            nodes[i].HasAngles = true;
            nodes[i].DeflectionDeg = LiftAlignMath.DeflectionDeg(p, q, n, h);
            nodes[i].HalfDeg = LiftAlignMath.WorldHalfDeg(p, q, n, h);
            LiftAlignMath.LoadCaseDeflections(p, q, n, h, out nodes[i].TightDeg, out nodes[i].HeavyDeg);
            if (m_C.HasTrains)
            {
                nodes[i].Kind = LiftAlignMath.ClassifyTrain(nodes[i].TightDeg, nodes[i].HeavyDeg);
                nodes[i].CapDeg = m_C.CapDeg(nodes[i].Kind, nodes[i].Head);
            }
            else
            {
                nodes[i].Kind = LiftTrainKind.None;
                nodes[i].CapDeg = m_C.LineCapDeg(LiftTrainKind.None);   // the saddle cap (2 x SaddleHalfCapDeg)
                nodes[i].BareDeg = LiftAlignMath.DeflectionDeg(p, q, n, h * LiftAlignMath.BareSagScale);
            }
        }

        /// <summary>Build's pitch and lean on the output nodes (DECISIONS 600). Every node's anchor starts at its rope point. A tower node
        /// with both neighbors' rope points (an end: its outer point) gets its pitch (ChordPitchDeg), its lean (LiftAlignMath.LeanDeg) and
        /// the leaned anchor (foot = the ground under it, plus a tram tower's FloatM).</summary>
        private void SetLeans(LiftPlanNode[] nodes)
        {
            int last = nodes.Length - 1;
            for (int i = 0; i <= last; i++)
            {
                nodes[i].AnchorAlongM = nodes[i].AlongM;
                nodes[i].AnchorRopeM = nodes[i].RopeM;
                nodes[i].HasPitch = false;
                nodes[i].PitchDeg = nodes[i].LeanDeg = 0f;
                if (last < 1)
                    continue;
                float sP, rP, sN, rN;
                if (i == 0)
                {
                    if (!OuterA) continue;
                    sP = -EndOuterDistA; rP = EndOuterRopeA;
                }
                else
                {
                    sP = nodes[i - 1].AlongM; rP = nodes[i - 1].RopeM;
                }
                if (i == last)
                {
                    if (!OuterB) continue;
                    sN = m_Length + EndOuterDistB; rN = EndOuterRopeB;
                }
                else
                {
                    sN = nodes[i + 1].AlongM; rN = nodes[i + 1].RopeM;
                }
                nodes[i].HasPitch = true;
                nodes[i].PitchDeg = LiftAlignMath.ChordPitchDeg(sP, rP, nodes[i].AlongM, nodes[i].RopeM, sN, rN);
                if (nodes[i].Member < 0)
                    continue;   // a line end: its rope is given (a junction tower leans in game, and the plan keeps its point)
                float lean = LiftAlignMath.LeanDeg(m_C.TypeId, nodes[i].PitchDeg);
                nodes[i].LeanDeg = lean;
                if (lean > 0f)
                    LiftAlignMath.LeanedAnchor(nodes[i].AlongM, nodes[i].GroundM + nodes[i].FloatM, nodes[i].RopeM, nodes[i].PitchDeg, lean,
                        out nodes[i].AnchorAlongM, out nodes[i].AnchorRopeM);
            }
        }

        /// <summary>Tram pedestals (DECISIONS 600 a, call c, critique SHOULD-2). Every leaned tram tower floats by LiftAlignMath.TramFloatM
        /// on the line's center profile around its foot (downhill = toward the lower neighbor, where the mast leans). The float is at least
        /// the downhill steel's dig-in and at most the piers' cover. Its rope rises with it (RopeM). The search, the angle pass and the
        /// bend (SetAngles) stay on the unfloated ropes (a float is a meter or so on spans of hundreds of meters). The train lifts never
        /// float (their pier shows at most 0.09 m on a crest at full lean: the art's foundation table). True when a tower floats.</summary>
        private bool SetFloats(LiftPlanNode[] nodes)
        {
            if (m_C.TypeId != "tram")
                return false;
            bool any = false;
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].FloatM = 0f;
                if (nodes[i].Member < 0 || !(nodes[i].LeanDeg > 0f) || AtFoot(nodes[i].AlongM))
                    continue;
                float foot = nodes[i].AlongM, ground = nodes[i].GroundM, down = nodes[i].PitchDeg > 0f ? -1f : 1f;
                float f = LiftAlignMath.TramFloatM(LiftAlignMath.TramBaseHalfM(m_C.Ladder[nodes[i].Member].TopM), nodes[i].LeanDeg,
                    x => CentreAt(foot + down * x) - ground, out _, out _);
                if (f <= 0f)
                    continue;
                nodes[i].FloatM = f;
                nodes[i].RopeM += f;
                any = true;
            }
            return any;
        }

        /// <summary>Is <paramref name="s"/> a built tower's position in FootAlong (within LiftTowerEdit.FootAtTolM)?</summary>
        private bool AtFoot(float s)
        {
            if (FootAlong == null)
                return false;
            for (int i = 0; i < FootAlong.Count; i++)
                if (math.abs(s - FootAlong[i]) <= LiftTowerEdit.FootAtTolM)
                    return true;
            return false;
        }

        /// <summary>The center ground at <paramref name="s"/> meters along, linear between the samples (clamped to the line).</summary>
        private float CentreAt(float s)
        {
            if (s <= m_S[0])
                return m_Centre[0];
            if (s >= m_S[m_N])
                return m_Centre[m_N];
            int lo = 0, hi = m_N;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (m_S[mid] <= s) lo = mid;
                else hi = mid;
            }
            float d = m_S[hi] - m_S[lo];
            return d > 1e-6f ? math.lerp(m_Centre[lo], m_Centre[hi], (s - m_S[lo]) / d) : m_Centre[lo];
        }

        /// <summary>The previous kind / head of output node <paramref name="i"/> (fixed-node plans with PreviousKinds), else None / Line.
        /// An unknown kind (None, e.g. after a load) is the one the head implies (LiftTowerEdit.KindOf).</summary>
        private void Previous(int i, out LiftTrainKind kind, out LiftTowerHead head)
        {
            kind = LiftTrainKind.None;
            head = LiftTowerHead.Line;
            if (m_PrevAt.Count == 0 || !m_PrevAt.TryGetValue(m_K[i], out int at))
                return;
            head = PreviousHeads[at];
            kind = PreviousKinds[at] != LiftTrainKind.None ? PreviousKinds[at] : LiftTowerEdit.KindOf(head);
        }

        /// <summary>Node <paramref name="i"/>'s kind with hysteresis (the previous kind when known) and the head that kind wants.</summary>
        private LiftTowerHead KindAndWant(LiftPlanNode[] nodes, int i)
        {
            Previous(i, out LiftTrainKind pk, out LiftTowerHead ph);
            if (pk != LiftTrainKind.None)
                nodes[i].Kind = LiftAlignMath.ClassifyTrain(nodes[i].TightDeg, nodes[i].HeavyDeg, pk);
            return m_C.WantHead(nodes[i].Kind, nodes[i].DeflectionDeg, ph);
        }

        /// <summary>The heads (breakover-holddown-plan-2026-11-03 section 3, B2): every judged line tower gets the head its kind wants
        /// (KindAndWant) when its member has that twin. For a member without that twin, the nearest member (fewest ladder steps, then the
        /// shorter) with that twin is used if it meets three conditions. Its node, both spans and (CheckEdgeSlope) both edges stay clear at
        /// the twin's E. Its own kind still wants that head. Its neighbor before it keeps its head. Otherwise the Line head stays and an
        /// angle note says why. The tram (no trains) and a type without loaded twins are untouched. Nodes are visited from A, so the result
        /// is deterministic.</summary>
        private void AssignHeads(LiftPlanNode[] nodes)
        {
            int last = nodes.Length - 1;
            if (!m_C.HasTrains || m_C.Twins.Length == 0 || last < 2)
                return;
            for (int i = 1; i < last; i++)
            {
                if (nodes[i].Member < 0 || !nodes[i].HasAngles)
                    continue;
                LiftTowerHead want = KindAndWant(nodes, i);
                int m0 = nodes[i].Member;
                if (HeadsAsGiven)
                {
                    Previous(i, out _, out LiftTowerHead given);
                    SetHead(nodes, i, m0, given);
                    continue;
                }
                int k = m_K[i], kp = m_K[i - 1], kn = m_K[i + 1];
                bool has = m_C.HasTwin(m0, want);
                if (want == LiftTowerHead.Line || (has && EdgesOk(nodes, i, m0, want)))
                {
                    SetHead(nodes, i, m0, want);
                    continue;
                }
                // the member has no such twin, or its twin's E would make an edge the engine refuses (a Break 15 m from a station)
                string why = has ? "its " + HeadWord(want) + " twin makes an edge too steep" : "has no " + HeadWord(want) + " twin";
                bool done = false;
                int refusedNode = 0, refusedSpan = 0, refusedKind = 0;
                for (int step = 1; step < m_C.Ladder.Length && !done; step++)
                {
                    for (int sgn = -1; sgn <= 1 && !done; sgn += 2)
                    {
                        int m = m0 + sgn * step;
                        if (m < 0 || m >= m_C.Ladder.Length || !m_C.HasTwin(m, want))
                            continue;
                        if (!MemberAllowed(k, m) || !NodeOk(k, m))
                        {
                            refusedNode++;
                            continue;
                        }
                        m_AngleWork++;
                        float r = Rope(k, m);
                        if (SpanViolation(kp, nodes[i - 1].RopeM, k, r) > Tol || SpanViolation(k, r, kn, nodes[i + 1].RopeM) > Tol ||
                            !EdgesOk(nodes, i, m, want))
                        {
                            refusedSpan++;
                            continue;
                        }
                        LiftPlanNode saveI = nodes[i], saveP = nodes[i - 1], saveN = nodes[i + 1];
                        nodes[i].Member = m;
                        nodes[i].RopeM = r;
                        AnglesAt(nodes, i - 1);
                        AnglesAt(nodes, i);
                        AnglesAt(nodes, i + 1);
                        bool keeps = KindAndWant(nodes, i) == want &&
                                     (saveP.Member < 0 || !saveP.HasAngles || HeadAfter(nodes, i - 1) == saveP.Head);
                        if (!keeps)
                        {
                            refusedKind++;
                            nodes[i] = saveI;
                            nodes[i - 1] = saveP;
                            nodes[i + 1] = saveN;
                            continue;
                        }
                        if (saveP.Member >= 0 && saveP.HasAngles)
                            SetHead(nodes, i - 1, saveP.Member, saveP.Head);
                        SetHead(nodes, i, m, want);
                        m_M[i] = m;
                        m_AngleNotes.Add("t" + NoteNode(i) + " at " + NoteAt(m_S[k]) + " m: " + HeadWord(want) + " head, " +
                                         Short(m_C.Ladder[m0].Name) + " " + why + " -> " + Short(m_C.NameOf(m, want)) +
                                         " (" + (m > m0 ? "taller" : "shorter") + ", both spans clear), d " + LiftTowerPlan.Sg(nodes[i].DeflectionDeg));
                        done = true;
                    }
                }
                if (!done)
                {
                    SetHead(nodes, i, m0, LiftTowerHead.Line);
                    m_AngleNotes.Add("t" + NoteNode(i) + " at " + NoteAt(m_S[k]) + " m: wants the " + HeadWord(want) + " head, " +
                                     Short(m_C.Ladder[m0].Name) + " " + why + " and no member with one fits (" + refusedNode + " node / max height, " + refusedSpan +
                                     " span or edge, " + refusedKind + " kind or neighbor head refused): the support head stays, d " + LiftTowerPlan.Sg(nodes[i].DeflectionDeg) + " (cap " +
                                     LiftTowerPlan.F(nodes[i].CapDeg, "F0") + ")");
                }
            }
        }

        /// <summary>The head assigned node <paramref name="i"/> would now get on its member: its wanted head when the member has that
        /// twin, else Line.</summary>
        private LiftTowerHead HeadAfter(LiftPlanNode[] nodes, int i)
        {
            LiftTowerHead want = KindAndWant(nodes, i);
            return m_C.HasTwin(nodes[i].Member, want) ? want : LiftTowerHead.Line;
        }

        private void SetHead(LiftPlanNode[] nodes, int i, int member, LiftTowerHead head)
        {
            nodes[i].Member = member;
            nodes[i].Head = m_C.HasTwin(member, head) ? head : LiftTowerHead.Line;
            nodes[i].CapDeg = m_C.CapDeg(nodes[i].Kind, nodes[i].Head);
        }

        private static string HeadWord(LiftTowerHead h) =>
            h == LiftTowerHead.Hold ? "hold-down" : h == LiftTowerHead.Combo ? "push-pull" : h == LiftTowerHead.Break ? "breakover" : "support";

        /// <summary>The engine's node height of output node <paramref name="node"/> at sample <paramref name="k"/>: ground + E(member,
        /// head). A line end is at EndNodeYA / EndNodeYB.</summary>
        private float NodeY(int k, LiftPlanNode node) =>
            node.Member < 0 ? (k == 0 ? EndNodeYA : EndNodeYB) : m_Centre[k] + LiftTowerEdit.ElevationM(m_C, node.Member, node.Head) + node.FloatM;

        /// <summary>Both edges of output node <paramref name="i"/> with (member, head) at it pass the engine's slope rule (MaxEdgeSlope +
        /// 0.0005, ValidationHelpers.cs:1462-1463: the LiftPlanSpan.SlopeViolated test). The result is always true unless CheckEdgeSlope.
        /// The neighbors stand at their own E (member, head).</summary>
        private bool EdgesOk(LiftPlanNode[] nodes, int i, int member, LiftTowerHead head)
        {
            if (!CheckEdgeSlope || !(m_C.MaxEdgeSlope > 0f))
                return true;
            int k = m_K[i], kp = m_K[i - 1], kn = m_K[i + 1];
            float y = m_Centre[k] + LiftTowerEdit.ElevationM(m_C, member, head);
            return EdgeOk(m_S[k] - m_S[kp], NodeY(kp, nodes[i - 1]), y) && EdgeOk(m_S[kn] - m_S[k], y, NodeY(kn, nodes[i + 1]));
        }

        private bool EdgeOk(float d, float ya, float yb)
        {
            d = math.abs(d);
            if (d <= 0f || !math.isfinite(ya) || !math.isfinite(yb))
                return true;
            return math.abs(yb - ya) / d < m_C.MaxEdgeSlope + 0.0005f;
        }

        // Working list + output

        private int IndexOf(int k)
        {
            for (int i = 0; i < m_K.Count; i++)
                if (m_K[i] == k)
                    return i;
            return -1;
        }

        private int Insert(int k, int m, LiftPlanNodeFlags flags)
        {
            int at = 0;
            while (at < m_K.Count && m_K[at] < k)
                at++;
            m_K.Insert(at, k);
            m_M.Insert(at, m);
            m_F.Insert(at, flags);
            return at;
        }

        private LiftTowerPlan Build()
        {
            int count = m_K.Count;
            var nodes = new LiftPlanNode[count];
            for (int i = 0; i < count; i++)
            {
                int k = m_K[i];
                LiftPlanNodeFlags f = m_F[i];
                if (m_M[i] >= 0 && m_High[k] - m_Low[k] > m_C.SideHillM)
                    f |= LiftPlanNodeFlags.SideHill;
                nodes[i] = new LiftPlanNode
                {
                    AlongM = m_S[k],
                    GroundM = m_Centre[k],
                    HighSideM = m_High[k],
                    LowSideM = m_Low[k],
                    Member = m_M[i],
                    RopeM = Rope(k, m_M[i]),
                    Flags = f,
                };
            }
            SetAngles(nodes);
            AssignHeads(nodes);
            SetLeans(nodes);
            if (SetFloats(nodes))
                SetLeans(nodes);   // pitch, lean and anchors on the floated ropes, as the game reads them (the bend stays as judged)
            var spans = new LiftPlanSpan[count - 1];
            for (int s = 0; s < count - 1; s++)
            {
                int i = m_K[s], j = m_K[s + 1];
                // the rope hangs from the (leaned) anchors. The span length stays node to node (the engine's edge)
                float ri = nodes[s].AnchorRopeM, rj = nodes[s + 1].AnchorRopeM;
                float sa = nodes[s].AnchorAlongM, ds = nodes[s + 1].AnchorAlongM - sa;
                float d = m_S[j] - m_S[i];
                float sag4 = 4f * m_C.SagRatio * ds;
                var span = new LiftPlanSpan { LengthM = d, MinFloorMarginM = float.PositiveInfinity, MaxRopeAboveM = float.NegativeInfinity };
                // a lean can carry an anchor past its own node (a 25 deg FG20 moves it about 8 m downhill), so the leaned rope may hang
                // over samples beyond the node range. The max rope height stays on the node range (the plumb chord)
                int qLo = i, qHi = j;
                while (qLo > 0 && m_S[qLo - 1] >= sa - 1e-3f) qLo--;
                while (qHi < m_N && m_S[qHi + 1] <= sa + ds + 1e-3f) qHi++;
                for (int q = qLo; q <= qHi; q++)
                {
                    // Station ends are not judged (their rope height is given). Tower ends are judged (the carrier passes over the node).
                    if ((q == i && nodes[s].IsStation) || (q == j && nodes[s + 1].IsStation))
                        continue;
                    if (m_S[q] < sa - 1e-3f || m_S[q] > sa + ds + 1e-3f)
                        continue;   // under a leaned head (between its foot and its anchor): the tower overhangs there, not the rope
                    float t = ds > 0f ? math.saturate((m_S[q] - sa) / ds) : 0f;
                    float r = ri + (rj - ri) * t - sag4 * t * (1f - t);
                    float margin = r - m_C.CarrierDropM - m_Floor[q] - m_High[q];
                    if (margin < span.MinFloorMarginM)
                    {
                        span.MinFloorMarginM = margin;
                        span.MinFloorMarginAtM = m_S[q] - m_S[i];
                        span.MinChairAboveGroundM = r - m_C.CarrierDropM - m_High[q];
                    }
                    // the max rope height on the plumb chord, as the search judged it (LeanedSpanViolation)
                    if (q < i || q > j)
                        continue;
                    float tp = d > 0f ? (m_S[q] - m_S[i]) / d : 0f;
                    float above = nodes[s].RopeM + (nodes[s + 1].RopeM - nodes[s].RopeM) * tp - 4f * m_C.SagRatio * d * tp * (1f - tp) - m_Centre[q];
                    if (above > span.MaxRopeAboveM)
                    {
                        span.MaxRopeAboveM = above;
                        span.MaxRopeAboveAtM = m_S[q] - m_S[i];
                    }
                }
                if (float.IsPositiveInfinity(span.MinFloorMarginM))
                {
                    span.MinFloorMarginM = 0f;
                    span.MaxRopeAboveM = 0f;
                }
                span.FloorViolated = span.MinFloorMarginM < -Tol;
                span.HeightViolated = m_C.MaxRopeAboveGroundM > 0f && span.MaxRopeAboveM > m_C.MaxRopeAboveGroundM + Tol;
                float ya = NodeY(i, nodes[s]), yb = NodeY(j, nodes[s + 1]);
                span.EdgeSlope = d > 0f && math.isfinite(ya) && math.isfinite(yb) ? math.abs(yb - ya) / d : 0f;
                // The engine flags it at MaxSlopeSteepness + 0.0005 (ValidationHelpers.cs:1462-1463).
                span.SlopeViolated = CheckEdgeSlope && m_C.MaxEdgeSlope > 0f && span.EdgeSlope >= m_C.MaxEdgeSlope + 0.0005f;
                spans[s] = span;
            }
            var along = new float[m_N + 1];
            var centre = new float[m_N + 1];
            var high = new float[m_N + 1];
            var floor = new float[m_N + 1];
            Array.Copy(m_S, along, m_N + 1);
            Array.Copy(m_Centre, centre, m_N + 1);
            Array.Copy(m_High, high, m_N + 1);
            Array.Copy(m_Floor, floor, m_N + 1);
            return new LiftTowerPlan
            {
                Constants = m_C, LengthM = m_Length, Nodes = nodes, Spans = spans,
                SampleAlongM = along, SampleCentreM = centre, SampleHighM = high, SampleFloorM = floor,
                StationA = StationA, StationB = StationB,
                EndCarrierA = m_RopeA - m_C.CarrierDropM - m_High[0], EndCarrierB = m_RopeB - m_C.CarrierDropM - m_High[m_N],
                AnglesMs = m_AnglesMs, AngleWork = m_AngleWork, AngleNotes = new List<string>(m_AngleNotes),
                Crossings = BuildCrossings(nodes),
            };
        }

        /// <summary>Every caller crossing judged on the nodes as built (heads, tram pedestal floats). For each crossing it finds the drawn
        /// edge's curve at the crossing point, the separation the side needs there (box clearance + both footprints' slope terms +
        /// CrossMarginM) and the plan's own. A crossing is refused when a tower of this line stands in the crossed corridor (a pin: the
        /// search keeps new towers out) or the separation is short. Along stays in the caller's frame.</summary>
        private LiftPlanCrossing[] BuildCrossings(LiftPlanNode[] nodes)
        {
            if (m_XSides == null || Crossings == null || Crossings.Count == 0)
                return new LiftPlanCrossing[0];
            int n = Crossings.Count;
            var result = new LiftPlanCrossing[n];
            float wN = 2f * m_C.HalfWidthM, h = m_C.SagRatio;
            for (int i = 0; i < n; i++)
            {
                LiftPlanCrossing x = Crossings[i];
                LiftCrossSide side = i < m_XSides.Length ? m_XSides[i] : LiftCrossSide.None;
                x.Side = side;
                x.Refusal = m_XBefore != null && i < m_XBefore.Length ? m_XBefore[i] : LiftCrossRefusal.None;
                float ws = m_FromB ? m_Length - x.AlongM : x.AlongM;
                CrossReach(x.AngleDeg, wN, x.CrossedWidthM, out float eN, out float eC);
                int s = 0;
                while (s < nodes.Length - 2 && m_S[m_K[s + 1]] < ws)
                    s++;
                int k = m_K[s], k2 = m_K[s + 1];
                float d = m_S[k2] - m_S[k];
                float ya = BuiltNodeY(k, nodes[s]), yb = BuiltNodeY(k2, nodes[s + 1]);
                float t = d > 0f ? math.saturate((ws - m_S[k]) / d) : 0f;
                float y = ya + (yb - ya) * t - 4f * d * h * t * (1f - t);
                float g = d > 0f ? (yb - ya) / d - 4f * h * (1f - 2f * t) : 0f;
                float foot = eN * math.abs(g) + math.abs(x.CrossedSlope) * eC;
                bool under = side == LiftCrossSide.Under;
                x.RequiredM = (under ? m_C.NetHeightMaxM - x.CrossedMinM : x.CrossedMaxM - m_C.NetHeightMinM) + foot + CrossMarginM;
                x.PlannedM = under ? x.CrossedY - y : y - x.CrossedY;
                if (side != LiftCrossSide.None)
                {
                    float band = CrossBandM(x.AngleDeg, wN, x.CrossedWidthM);
                    for (int q = 1; q < nodes.Length - 1 && x.Refusal == LiftCrossRefusal.None; q++)
                        if (math.abs(m_S[m_K[q]] - ws) < band - Tol)
                            x.Refusal = LiftCrossRefusal.OwnTower;
                    if (x.Refusal == LiftCrossRefusal.None && x.PlannedM < x.RequiredM - Tol)
                        x.Refusal = LiftCrossRefusal.Clearance;
                }
                result[i] = x;
            }
            return result;
        }

        /// <summary>The node's height as built (NodeY with its head and float). For an end of unknown height it is its rope minus
        /// NodeOff.</summary>
        private float BuiltNodeY(int k, LiftPlanNode node)
        {
            float y = NodeY(k, node);
            return math.isfinite(y) ? y : node.RopeM - NodeOff(k);
        }
    }
}
