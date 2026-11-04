// Pure tower planner for a lift line. No ECS, no Game types: LiftBatch tests it headless (LiftPlannerTests). It runs once per edit
// (or once per printed diag block), so it is plain C#, not Burst; arrays are reused per planner instance. Deterministic: no
// randomness, no hash order, ties broken by the sample index. Plan works from the end with the lower rope (PlansFromB) and mirrors
// the result back, so a line gives the same plan whichever end the caller passes as A (planner-direction-2026-11-04).
// Input: a terrain sampler (along-line meters from station A, lateral meters), the line length, the two stations' rope heights,
// LiftTowerPlanConstants for the lift type and optional seed nodes (kept as anchors, so re-planning a plan is a fixed point).
// Phases: (a) anchors by exact shortest-path search, (b) even infill, (c) member elevation, (d) rope angles (breakover / hold-down
// pairs in the tool's plans only, LiftAlignMath.RopeAngles), then the output (every judged node's deflection and train kind). Fit asks
// the fixed node-count variants for LiftTowerFitSystem and never adds a tower. Rope sag is the engine's parabola 4*d*m_Hanging*t(1-t)
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

    /// <summary>A line tower's head (breakover-holddown-plan-2026-11-03, section 3): the support member itself (Line) or one of its twins,
    /// another member of the same placeholder with its own unique top. Hold = hold-down train over the rope, Combo = push-pull, Break =
    /// the FG 8-wheel breakover train (art v2, docs research art/breakover-twins-2026-11/v2/build.md section 6).</summary>
    public enum LiftTowerHead : byte
    {
        Line = 0,
        Hold = 1,
        Combo = 2,
        Break = 3,
    }

    /// <summary>One twin member: the support member it is built on (ladder index), its head, the prefab name and its top
    /// (ObjectGeometryData m_Size.y = the manifest's top_anchor_y). Its lanes are the support member's (manifest rope_on_crown lane_y_new =
    /// member_lane_y), so the rope height at a twin is RopeAboveBase(Member).</summary>
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
        /// <summary>no NEW tower nearer a station end than this (meters along the line from the stub): the station's own rope
        /// run (StationRunM: bullwheel or dock to the stub tip, the longest of the type's stations), at least MinSpanM. Seeds (pins,
        /// a line's own nodes in PlanFixed) are kept wherever they are.</summary>
        public readonly float StationClearM;
        /// <summary>The line prefab's NetGeometryPrefab m_MaxSlopeSteepness (gamedata-mirror Ski*HV: FG 0.8, DQ 0.9, GD 1.2, tram 2.5;
        /// 0 = no rule). The engine refuses a Temp edge whose node-to-node |dy| / xz length reaches it + 0.0005 (SteepSlope,
        /// Game.Net/ValidationHelpers.cs:1435-1490, test :1462): a tall tower a few meters from a station is such an edge. These values are the
        /// defaults (tests, and a prefab without NetGeometryData): the tool plans with the line prefab's live value (With(maxEdgeSlope:),
        /// LiftLineToolSystem at the pick, which logs a MISMATCH when the two differ) and checks it in LiftTowerPlanner.CheckEdgeSlope.</summary>
        public readonly float MaxEdgeSlope;

        /// <summary>Wheels per sheave train of the type's line towers, per kind (breakover-holddown-plan-2026-11-03, section 2.2; the
        /// generator's trains: lift_tower.py _fg_spec sheaves 4, DQ 8, SKIN_GD 10; hold-down FG 4 / DQ 8 / GD 8 as the ETGD head;
        /// push-pull per side FG 2S2C, DQ / GD 4S4C). 0 = no sheave train (the tram's saddles): no cap, no pairs, no kinds.</summary>
        public readonly int SupportWheels, HoldWheels, ComboWheels;

        /// <summary>Wheels per side of the Break twin's train (FG: 8, manifests sheaves_per_side; 0 = the type has no Break twin).</summary>
        public readonly int BreakWheels;

        /// <summary>The twins this type can place (the ones whose prefab is loaded, TwinFilter at For time); empty = none. Every twin's
        /// Member is a ladder index.</summary>
        public readonly LiftTowerTwin[] Twins;

        /// <summary>Set once per game load (LiftTowerFitSystem.OnGameLoadingComplete): true for a twin prefab name the game has loaded.
        /// For(def) drops the others, so a build without the twin prefabs plans the support heads only (a missing twin would leave the
        /// node raised by E with the support member spawned 0.2-0.6 m above the ground). Null = every twin (tests).</summary>
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

        /// <summary>Towers per breakover group: 2 (pairs only; groups of 3 are not planned).</summary>
        public const int MaxGroup = 2;

        /// <summary>How far a single tower may slide (meters) when a shifted tower alone brings its rope under the cap.</summary>
        public const float AngleShiftM = 20f;

        /// <summary>The type has sheave trains (the chairs and the gondola; not the tram).</summary>
        public bool HasTrains => SupportWheels > 0;

        /// <summary>The cap, degrees, of the line member's own <paramref name="kind"/> train (LiftAlignMath.TrainCapDeg: wheels x 4 deg,
        /// at most the 40 deg art fold): FG 16 / 16 / 8, DQ 32 / 32 / 16, GD 40 / 32 / 16 (support / hold-down / push-pull); +infinity
        /// without trains.</summary>
        public float LineCapDeg(LiftTrainKind kind) =>
            LiftAlignMath.TrainCapDeg(kind == LiftTrainKind.HoldDown ? HoldWheels : kind == LiftTrainKind.PushPull ? ComboWheels : SupportWheels);

        /// <summary>The cap the planner judges a <paramref name="kind"/> train at: LineCapDeg, except a support train on a type whose every
        /// member has a Break twin, which gets the Break train's cap (FG 8 wheels: 32; the convex tower takes the Break head instead of a
        /// pair): FG 32 / 16 / 8 with the twins loaded, else as LineCapDeg.</summary>
        public float CapDeg(LiftTrainKind kind) =>
            kind != LiftTrainKind.HoldDown && kind != LiftTrainKind.PushPull && BreakEverywhere
                ? LiftAlignMath.TrainCapDeg(BreakWheels) : LineCapDeg(kind);

        /// <summary>The cap of the train a tower with <paramref name="head"/> carries for <paramref name="kind"/>: a twin's own train
        /// (Hold, Combo, Break wheels), the line member's otherwise (LineCapDeg: a kind whose twin is missing is judged as B1 did).</summary>
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

        /// <summary>The head a <paramref name="kind"/> train wants at deflection <paramref name="deflectionDeg"/> (drawn rope, degrees):
        /// HoldDown -> Hold, PushPull -> Combo, Support -> Break past the line train's cap (kept while |d| stays above that cap minus
        /// LiftAlignMath.KindHystDeg when <paramref name="previous"/> was Break), else Line. Whether the member has that twin is the
        /// planner's question (the fallback in LiftTowerPlanner.AssignHeads).</summary>
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
                                      int comboWheels = 0, int breakWheels = 0, LiftTowerTwin[] twins = null)
        {
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

        /// <summary>A copy with other rules (tests and what-if questions: floor, max rope height, spans; sagRatio replays a line logged
        /// at another rope sag).</summary>
        public LiftTowerPlanConstants With(float? floorM = null, float? maxRopeAboveGroundM = null, float? typicalSpanM = null,
                                           float? maxSpanM = null, float? carrierDropM = null, float? maxEdgeSlope = null,
                                           float? sagRatio = null, LiftTowerTwin[] twins = null)
        {
            return new LiftTowerPlanConstants(TypeId, Ladder, RopeBelowTopM, carrierDropM ?? CarrierDropM, floorM ?? FloorM,
                maxRopeAboveGroundM ?? MaxRopeAboveGroundM, typicalSpanM ?? TypicalSpanM, maxSpanM ?? MaxSpanM, MinSpanM, HalfWidthM,
                sagRatio ?? SagRatio, EngineElevationCapM, StepM, SideHillM, ConvexM, StationClearM, maxEdgeSlope ?? MaxEdgeSlope,
                SupportWheels, HoldWheels, ComboWheels, BreakWheels, twins ?? Twins);
        }

        /// <summary>The station-side rule's exchange rate: one more line tower is worth this many meters of extra height (above the
        /// shortest member) on the towers next to the stations (MinCost.EndRiseM). Fewest towers alone put DQ18 / DQ20 100-125 m from
        /// both stations of an ordinary 1,200 m slope (LP-27): the long end span from a low station rope needs a tall tower, a short one
        /// needs one tower more. 8 m buys that tower (DQ18 + DQ20 = 18 m of rise), keeps a 2-tower DQ line with one DQ14 (4 m) at 2, and
        /// does not stand an FG8 15 m from a station to save an FG12 (LP-3, LP-17); a strict station-first order did all three. 0 = fewest
        /// towers first, the rise only breaks ties: the tram (its towers are the rare 11.4 m wide ones and a span of up to 3 km is the
        /// point of the type, DECISIONS 381) and the fit (LiftTowerPlanner.StationRule off: its free plan only counts towers).</summary>
        public float TowerWorthM => TypeId == "tram" ? 0f : 8f;

        /// <summary>The floor (lowest carrier point above the high side) at <paramref name="distM"/> meters from a station end: carriers
        /// leave and enter a station at loading height (DQ: bullwheel rope 3.70 m above the platform, hang 3.30 m = 0.40 m), so the floor
        /// ramps from 0 at the station to FloorM at StationClearM (the station approach). Without it the floor applied from the first
        /// sample on and forced a tall tower right at every station (LP-26, field test 2026-10-26). <paramref name="atStationM"/> is the
        /// carrier's height above the high side at the station end itself: a station sunk into the slope (no-level terraform, carriers
        /// leaving below the ground) ramps from that negative height instead of 0, so the planner does not try to dig the rope out
        /// of the station's own hole with a tall tower beside it (the engine refuses that edge as SteepSlope); the tool warns instead.</summary>
        public float ApproachFloor(float distM, float atStationM = 0f) =>
            StationClearM > 0f ? math.lerp(math.min(0f, atStationM), FloorM, math.saturate(distM / StationClearM)) : FloorM;

        /// <summary>A station's own rope run, meters along the line axis from where the carriers turn or dock to the HV stub
        /// tip (prefab z): |WheelLocal.z - StubTipLocalZ| on a loop station, |DockLocalA.z - StubTipLocalZ| on a shuttle station;
        /// 0 when the row has neither. Catalog rows (SkiCatalog stations): FG base 12.2 / top 12.0, DQ 25.6 / 21.6, gondola
        /// 25.4 / 21.4, tram 6.5 / 6.5.</summary>
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

        /// <summary>The longest StationRunM over the type's base and top stations (the planner does not know which station a
        /// line end is, so both ends keep the longer run).</summary>
        public static float StationRunM(LiftTypeDef def)
        {
            if (def == null)
                return 0f;
            return math.max(StationRunM(SkiCatalog.StationOf(def.Index, StationRole.Base)), StationRunM(SkiCatalog.StationOf(def.Index, StationRole.Top)));
        }

        // Ladder tops: the tower manifests' top_anchor_y = ObjectGeometryData m_Size.y, confirmed in game. Rope offset = shortest top -
        // catalog AxleM + RopeSeatM (lane y on the shortest tower; the rope rests on the crowns where RopeSeatM > 0, T27). Drop = catalog HangM. Half width = piece m_Width / 2. Sag = the rope lane's m_Hanging,
        // read from the catalog (Envelope.LaneHanging; CatalogTests mirror it against the lane prefabs). Per-type values: EngineNotes/LiftTowerPlanner.md

        /// <summary>The tram's longest free span (Peak 2 Peak's record span is 3.03 km). Only the lift-line tool builds a span this long:
        /// while it is open the line prefab's max edge length is raised to it (LiftLineToolSystem.SetSpansRaised); the vanilla net tool
        /// keeps SkiTramHV m_MaxPylonDistance 1,000 m. The sag check is unchanged: the engine's rope is the parabola 4·d·m_Hanging·t(1-t)
        /// at any length (LaneSystem.cs:4128-4134), 12 m mid-span at 3 km, and every span is tested at every 10 m sample. The other
        /// engine limits for one 3 km edge: EngineNotes/LiftTowerPlanner.md.</summary>
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
        // twin_of_prefab, placeholder; lanes = the member's). E = top - ladder[0] top: FG 0.2 / 0.35 / 0.5 (FG8), 4.2 / 4.35 / 4.5 (FG12),
        // 8.5 (FG16 Break), 12.5 (FG20 Break); DQ 0.5178 / 0.2589 (DQ10), 2.5178 / 2.2589 (DQ12); GD 0.568 / 0.284 (GD12), 4.568 / 4.284
        // (GD16). Every E is under the line's m_ElevationRange.max (FG 16, DQ 10, GD 28); every top is >= 0.1 m from every other member
        // of its placeholder (check-lift-align LADDER).

        public static readonly LiftTowerTwin[] FixedGripTwins =
        {
            new LiftTowerTwin(0, LiftTowerHead.Hold, "SkiLiftTowerFG8HoldA", 9.214f),
            new LiftTowerTwin(0, LiftTowerHead.Combo, "SkiLiftTowerFG8ComboA", 9.364f),
            new LiftTowerTwin(0, LiftTowerHead.Break, "SkiLiftTowerFG8BreakA", 9.514f),
            new LiftTowerTwin(1, LiftTowerHead.Hold, "SkiLiftTowerFG12HoldA", 13.214f),
            new LiftTowerTwin(1, LiftTowerHead.Combo, "SkiLiftTowerFG12ComboA", 13.364f),
            new LiftTowerTwin(1, LiftTowerHead.Break, "SkiLiftTowerFG12BreakA", 13.514f),
            new LiftTowerTwin(2, LiftTowerHead.Break, "SkiLiftTowerFG16BreakA", 17.514f),
            new LiftTowerTwin(3, LiftTowerHead.Break, "SkiLiftTowerFG20BreakA", 21.514f),
        };

        public static readonly LiftTowerTwin[] DetachableTwins =
        {
            new LiftTowerTwin(0, LiftTowerHead.Hold, "SkiLiftTowerDQ10HoldA", 10.4678f),
            new LiftTowerTwin(0, LiftTowerHead.Combo, "SkiLiftTowerDQ10ComboA", 10.2089f),
            new LiftTowerTwin(1, LiftTowerHead.Hold, "SkiLiftTowerDQ12HoldA", 12.4678f),
            new LiftTowerTwin(1, LiftTowerHead.Combo, "SkiLiftTowerDQ12ComboA", 12.2089f),
        };

        public static readonly LiftTowerTwin[] GondolaTwins =
        {
            new LiftTowerTwin(0, LiftTowerHead.Hold, "SkiLiftTowerGD12HoldA", 12.568f),
            new LiftTowerTwin(0, LiftTowerHead.Combo, "SkiLiftTowerGD12ComboA", 12.284f),
            new LiftTowerTwin(1, LiftTowerHead.Hold, "SkiLiftTowerGD16HoldA", 16.568f),
            new LiftTowerTwin(1, LiftTowerHead.Combo, "SkiLiftTowerGD16ComboA", 16.284f),
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
                        110f, 245f, 10f, 3f, def.Envelope.LaneHanging, 10f, stationClearM: run, maxEdgeSlope: 0.9f,
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
        public int Member;              // ladder index; -1 for a station
        public float RopeM;             // world y of the rope at the node
        public LiftPlanNodeFlags Flags;
        public bool IsStation => (Flags & LiftPlanNodeFlags.Station) != 0;
        // Phase (d) rope angles (LiftAlignMath.RopeAngles on the planner's rope points): set by Build on every plan.
        public bool HasAngles;          // judged: a line tower, or a line end joining a built line (the planner's EndOuter point)
        public float DeflectionDeg;     // in - out on the drawn rope (h): + convex (support), - concave (hold-down)
        public float TightDeg;          // the tight load case, h x LiftAlignMath.TightSagScale
        public float HeavyDeg;          // the heavy load case, h x LiftAlignMath.HeavySagScale
        public float BareDeg;           // the tram's bare track rope, h x LiftAlignMath.BareSagScale (trams only; 0 otherwise)
        public LiftTrainKind Kind;      // ClassifyTrain(tight, heavy); None without trains (tram) or angles
        public float CapDeg;            // the cap of the train the tower carries (LiftTowerPlanConstants.CapDeg(Kind, Head)); +inf without trains
        public LiftTowerHead Head;      // the placed head: Line or a twin of Member (LiftTowerPlanner.AssignHeads); Line on stations and the tram
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

    public sealed class LiftTowerPlan
    {
        public LiftTowerPlanConstants Constants;
        public float LengthM;
        public LiftPlanNode[] Nodes;
        public LiftPlanSpan[] Spans;
        public float[] SampleAlongM;    // the planner's profile (copied): along, center ground, high side, per sample
        public float[] SampleCentreM;
        public float[] SampleHighM;
        public float[] SampleFloorM;    // the floor at each sample (FloorM, ramped near a station end: LiftTowerPlanConstants.ApproachFloor)
        public bool StationA, StationB; // the ends are stations (approach floor, station-side member rule); false = a tower node
        /// <summary>The lowest carrier point above the high side at each end sample, with the end's own rope: a station whose rope
        /// leaves it below the ground in front (a station sunk into the slope) reads negative, and no short tower can clear that.</summary>
        public float EndCarrierA, EndCarrierB;
        public int LineTowers => Nodes == null ? 0 : math.max(0, Nodes.Length - 2);

        /// <summary>Phase (d): wall time of the angle pass (pairs) in ms, its deterministic work count (candidate evaluations, the cost
        /// gate) and one note per repair tried (what was over its cap and what the pass did).</summary>
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
                if (math.abs(n.DeflectionDeg) > n.CapDeg + AngleTolDeg) overCap++;
            }
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
                // the tram: saddles; the bare-rope breakover rule (14.4.4) is logged, never planned
                int lift = 0;
                for (int i = 0; i < Nodes.Length; i++)
                    if (Nodes[i].HasAngles && Nodes[i].BareDeg < LiftAlignMath.SaddleMinBreakDeg)
                        lift++;
                return "saddles: worst d " + Sg(worstS) + " / " + Sg(worstH) + " deg; bare-rope breakover under " +
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
            }
            else
            {
                sb.Append(" (tight ").Append(Sg(n.TightDeg)).Append(" / heavy ").Append(Sg(n.HeavyDeg)).Append(") ").Append(LiftAlignMath.KindLetter(n.Kind));
                if (n.Head != LiftTowerHead.Line)
                    sb.Append(' ').Append(n.Head);
                if (math.abs(n.DeflectionDeg) > n.CapDeg + AngleTolDeg)
                    sb.Append(" OVER CAP ").Append(F(n.CapDeg, "F0"));
            }
            if ((n.Flags & LiftPlanNodeFlags.Pair) != 0)
                sb.Append(" [pair]");
            return sb.ToString();
        }

        /// <summary>Rope y on span <paramref name="span"/> at <paramref name="alongM"/> (line meters), with the engine's sag.</summary>
        public float RopeAt(int span, float alongM)
        {
            LiftPlanNode a = Nodes[span], b = Nodes[span + 1];
            float d = b.AlongM - a.AlongM;
            if (d <= 0f)
                return a.RopeM;
            float t = math.saturate((alongM - a.AlongM) / d);
            return a.RopeM + (b.RopeM - a.RopeM) * t - 4f * Constants.SagRatio * d * t * (1f - t);
        }

        /// <summary>Floor margin (rope - drop - high side - floor) at profile sample <paramref name="q"/> on span <paramref name="span"/>.</summary>
        public float MarginAt(int span, int q) =>
            RopeAt(span, SampleAlongM[q]) - Constants.CarrierDropM - SampleHighM[q] - (SampleFloorM != null ? SampleFloorM[q] : Constants.FloorM);
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

        /// <summary>The "Towers [plan]" block (the "Towers [nodes]" node/span layout of LiftTowerDiagSystem). <paramref name="header"/>
        /// is the text after "Towers [plan]: " on the first line; the caller appends the planned-vs-actual numbers there.</summary>
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
                  .Append(" [").Append(FlagText(n.Flags)).Append("]").Append(n.HasAngles ? ", " + AngleText(i) : string.Empty).Append('\n');
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

        /// <summary>{member x count} for the summary line, shortest first (ladder members; HeadCounts names the twins).</summary>
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

    /// <summary>The engine-side numbers of a fitted node, pure so LiftBatch checks them (LP-15). A tower node is raised so the vanilla
    /// placeholder ladder spawns exactly the planned member on the ground: node y = center ground + E, Game.Net.Elevation = (E, E),
    /// E = TopM[member] - TopM[0]. SubObjectSystem scores the planned member spare 0 / score 1 (SubObjectSystem.cs:980, :2189-2199), a
    /// Standalone pillar is placed top-anchored (:2464-2469, :2477-2482) and the rope lane is node y + lane y (LaneSystem.cs:4112-4115).
    /// The edge curve is NetUtils.StraightCurve(a, d, net m_Hanging) (NetUtils.cs:178-191). Derivation: EngineNotes/LiftTowerPlanner.md</summary>
    public static class LiftTowerEdit
    {
        /// <summary>A node this far BELOW its target (ground + E) is rewritten: the tower would sink into the ground.</summary>
        public const float HeightBelowTolM = 0.25f;

        /// <summary>A node up to this far ABOVE its target is left alone. Every write re-curves the node's edges, the carve (cut ceiling =
        /// edge y + m_HeightRange.min - 2, TerrainSystem.AddSegment) follows the curve, and on the steep tram spans the cut reaches the
        /// node: the ground under it sank 0.39 -> 0.33 -> 0.27 m per write (ratio ~0.85), so a fit that re-grounds on every 0.25 m wrote
        /// three times and hit the loop guard. The write that caused the sink is ours; the tower base then stands at most this far above the
        /// carved ground.</summary>
        public const float HeightAboveTolM = 1.0f;

        /// <summary>Does a node at <paramref name="oldY"/> need a height write to reach <paramref name="targetY"/> (ground + E)
        /// when its member and position are unchanged? One-sided: below by more than HeightBelowTolM, or above by more than
        /// HeightAboveTolM.</summary>
        public static bool HeightWrite(float oldY, float targetY) =>
            oldY < targetY - HeightBelowTolM || oldY > targetY + HeightAboveTolM;

        public static float ElevationM(LiftTowerPlanConstants c, int member) =>
            member <= 0 ? 0f : c.Ladder[member].TopM - c.Ladder[0].TopM;

        /// <summary>E of the placed member: Top(twin) - Top(ladder[0]) for a twin head, ElevationM(member) for Line. The node goes to
        /// ground + E; the engine then spawns the twin (unique top, score 1) with its pivot on the ground, and the rope stays at the
        /// member's lane height (the twin's lanes are the member's).</summary>
        public static float ElevationM(LiftTowerPlanConstants c, int member, LiftTowerHead head)
        {
            if (member < 0)
                return 0f;
            int t = c.TwinIndex(member, head);
            return t >= 0 ? c.Twins[t].TopM - c.Ladder[0].TopM : ElevationM(c, member);
        }

        /// <summary>The (member, head) a node at <paramref name="elevationM"/> carries: the ladder member or twin whose E matches within
        /// MemberMatchM (tops are >= 0.1 m apart, check-lift-align LADDER, so at most one matches), else the tallest LADDER member not
        /// above it with the Line head (a node the engine elevated by itself, e.g. one drawn above the ground).</summary>
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

        /// <summary>MemberOf's match tolerance (meters of E).</summary>
        public const float MemberMatchM = 0.05f;

        /// <summary>The train kind a head implies (the hysteresis input when no classified kind is known, e.g. after a load): Hold ->
        /// HoldDown, Combo -> PushPull, Break -> Support; Line -> None (a support head can carry any kind whose twin was missing).</summary>
        public static LiftTrainKind KindOf(LiftTowerHead head) =>
            head == LiftTowerHead.Hold ? LiftTrainKind.HoldDown : head == LiftTowerHead.Combo ? LiftTrainKind.PushPull
            : head == LiftTowerHead.Break ? LiftTrainKind.Support : LiftTrainKind.None;

        /// <summary>Where the engine stands member <paramref name="member"/> at a node <paramref name="elevationM"/> above the ground:
        /// its pivot above the ground (AnchorTop, :2477-2482: node y + spec y - top; spec y = TopM[0]). 0 = on the ground.</summary>
        public static float EnginePivotAboveGround(LiftTowerPlanConstants c, int member, float elevationM) =>
            elevationM + c.Ladder[0].TopM - c.Ladder[member].TopM;

        /// <summary>EnginePivotAboveGround for the placed (member, head): a twin's own top.</summary>
        public static float EnginePivotAboveGround(LiftTowerPlanConstants c, int member, LiftTowerHead head, float elevationM) =>
            elevationM + c.Ladder[0].TopM - c.TopOf(member, head);

        /// <summary>The (member, head) the engine spawns at an owner elevation, over the ladder and the twins (the placeholder's
        /// members as the planner knows them): highest score, ties -> -1.</summary>
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

        /// <summary>The member the engine spawns at an owner elevation (highest score; ties -> -1, the engine would draw).</summary>
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

        // A lift line is a FlattenTerrain power line: TerrainSystem cuts the terrain down to a ceiling of edge y + m_HeightRange.min +
        // m_MinHeightOffset (AddSegment, TerrainSystem.cs:1397-1516; our pieces {6, 20} and -2 = edge y + 4) and never fills. The cut is
        // live (DrawHeightAdjustments, :4727-4800) and the CPU heights the fit samples read back from the cascade (:3598-3646), so a vanilla
        // line drawn on the ground leaves a trench the fit would take for ground and stand towers in (and refill into pits around them).
        // Rule: a sample at this line's own cut ceiling (within OwnCutTolM; natural ground is never exactly there) is replaced by the plane
        // through the ground just outside the cut (lateral +- (half width + CutFarMarginM)), when that plane is higher. Ground below the
        // ceiling (a valley the rope crosses) is the player's and is kept. More: EngineNotes/LiftTowerPlanner.md

        public const float OwnCutTolM = 0.3f;
        /// <summary>Lateral margin beyond the piece half width for the uncut reference samples. The cut's smoothing width lives in the
        /// compiled terrain shader: UNVERIFIED (8 m is the allowance TerrainSystem subtracts from the smoothing width when it culls
        /// lots, :899). Too small = the reference is cut too and the raw sample stands (no harm); too large = the plane spans more
        /// of a cross slope's curvature.</summary>
        public const float CutFarMarginM = 8f;
        /// <summary>NetTerrainPiece m_MinHeightOffset of all four line pieces (gamedata-mirror Ski*Piece: (-2, -2, -2)); the fit reads
        /// the edge's TerrainComposition when it has one.</summary>
        public const float PieceMinHeightOffsetM = -2f;

        /// <summary>The ground at a sample: <paramref name="raw"/> unless it sits at <paramref name="ceiling"/> (our own cut), then
        /// the higher of raw and the plane through <paramref name="farLeft"/> / <paramref name="farRight"/> (sampled at ±<paramref
        /// name="farM"/>) at <paramref name="lateral"/>.</summary>
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

        /// <summary><paramref name="raw"/> with UncutGround applied against the line's own cut: the line runs through the node
        /// points <paramref name="pts"/> (xz distance <paramref name="cum"/> from the first), each span a HangingCurve with
        /// <paramref name="hanging"/>, its ceiling <paramref name="cutOffset"/>[span] above the curve; the uncut reference is sampled
        /// at ±<paramref name="farM"/>. <paramref name="onCut"/> (optional) gets the along of every replaced sample.</summary>
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
    /// (assets/blender/lift_tower.py:4146-4152 plate_numeral: FG8 -> 8, DQ12 -> 12, GD20 -> 20), so a line that mixes members would read
    /// 12, 16, 20, 12, ... instead. The order rule: tower n = the n-th line tower by distance
    /// along the line from the base station, 1 at the base, dense (nodes without a tower are skipped). Computed from geometry only, so it
    /// is the same after a save / load; an edit renumbers only the towers after the first changed one.</summary>
    public static class LiftTowerNumbering
    {
        /// <summary>Numbers 1..N for towers at <paramref name="alongFromBaseM"/> (meters from the base station, any order); ties keep the
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

        /// <summary>Objective switch. false (default) = fewest towers: an exact search for the fewest towers with every
        /// span <= MaxSpanM and clear with its real sag, tall members wherever one saves a tower; then the fewest ladder steps; then
        /// even spans. true = the legacy objective (anchors, then infill at the typical span, shortest members), kept for the
        /// before / after replays (LiftBatch LP-17) and as the fallback where no clear arrangement exists.</summary>
        public bool LegacySpacing;

        /// <summary>Lift-line tool: spots the user removed a tower from (meters along). The fewest-towers search puts a NEW tower
        /// within ForbiddenRadiusM of one only where the line has no clear plan without it (a seed there is still kept). Null / empty = none.</summary>
        public IList<float> Forbidden;
        public float ForbiddenRadiusM = 10f;

        /// <summary>The ends are stations (default: both, the planner's original assumption). A station end gets the approach floor
        /// (LiftTowerPlanConstants.ApproachFloor) and the station-side member rule (MinCost.EndRiseM, TowerWorthM); false = a loose line end or a
        /// fresh ground node: a tower node, full floor. Set by the caller before each Plan / Fit (the lift tool from its end kinds,
        /// LiftTowerFitSystem from the end node's Owner: a station sub-net stub).</summary>
        public bool StationA = true, StationB = true;

        /// <summary>Lift-line tool: plan with the engine's edge slope rule (LiftTowerPlanConstants.MaxEdgeSlope) between the nodes as
        /// they will be built (towers at ground + E(member), the ends at EndNodeYA / EndNodeYB; NaN = unknown, that end's span is not
        /// checked). Off for the fit: a built line's edges are permanent, and the engine validates Temp edges only.</summary>
        public bool CheckEdgeSlope;
        /// <summary>Trade a tower for shorter towers beside the stations (LiftTowerPlanConstants.TowerWorthM). Fit turns it off for its
        /// free plan, whose tower count says how many nodes the line needs.</summary>
        public bool StationRule = true;
        public float EndNodeYA = float.NaN, EndNodeYB = float.NaN;

        /// <summary>Phase (d) breakover / hold-down pairs (breakover-holddown-plan-2026-11-03, section 2.3): a tower whose rope deflection
        /// is past its train's cap is shifted (within AngleShiftM, any member) or replaced by a pair PairMinM..PairMaxM apart. The lift-line
        /// tool's plans only (LiftLineEdit.Replan); off by default, and Fit forces it off for its free plan, so the fit never adds a tower
        /// (it keeps the engine's node count) and Fit.Needed is unchanged. No effect on a type without trains (the tram). Kinds and angles
        /// are set on every plan by Build.</summary>
        public bool Pairs;

        /// <summary>The fit's hysteresis inputs, one per inner node of the PlanFixed / Fit call (the line's nodes as they stand): the kind
        /// each tower had (LiftTrainKind.None = unknown) and the head it carries. Used only by the fixed-node plans at exactly those
        /// positions (PlanFixed, Fit's Kept); Plan and Fit's Moved ignore them. Null = none (the tool's plans).</summary>
        public IList<LiftTrainKind> PreviousKinds;
        public IList<LiftTowerHead> PreviousHeads;

        /// <summary>Evaluate the heads as given (PreviousHeads, Line where none is given) instead of choosing them: the "before" of a
        /// line as it stands (LiftTowerFitSystem). Kinds are still classified, with hysteresis.</summary>
        public bool HeadsAsGiven;

        /// <summary>Sample index -> index into PreviousKinds / PreviousHeads for the plan being built (fixed-node plans only).</summary>
        private readonly Dictionary<int, int> m_PrevAt = new Dictionary<int, int>();

        /// <summary>A non-station end that joins a built line (a loose end): the built neighbor's rope point beyond it, as the xz distance
        /// from the end node and the rope y there, so the junction tower is judged too (NaN = unknown: that end is not judged). The tool sets
        /// them before each Plan; the fit never does (its path ends are stations or open line ends).</summary>
        public float EndOuterDistA = float.NaN, EndOuterRopeA = float.NaN, EndOuterDistB = float.NaN, EndOuterRopeB = float.NaN;

        /// <summary>World keys of the two ends (the lift tool: their xz), used only to order two ends whose ropes are exactly equal
        /// (PlansFromB). NaN = none: such a line is planned from A.</summary>
        public float2 EndKeyA = new float2(float.NaN), EndKeyB = new float2(float.NaN);

        /// <summary>Plan works from end B: the end it works from has the lower rope (the base); equal ropes: the smaller world key
        /// (x, then z), when both ends have one. The planner's search runs from the end it works from and every tie and grid remainder
        /// resolves toward it (its sample grid starts there, the search keeps the first of equal arrangements, the station rule's
        /// extra tower goes to the first station segment), so it always works from the same end of a line and mirrors the plan back:
        /// the same line gives the same plan, mirrored, whichever end is A (planner-direction-2026-11-04). Exact comparisons: the
        /// same two floats swap with the ends, so the answer flips exactly.</summary>
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

        /// <summary>Plan a line of <paramref name="lengthM"/> meters (xz) between two stations whose rope heights are given.
        /// <paramref name="seeds"/> (optional) are kept as anchors with their members, e.g. a previous plan's nodes. A seed with a
        /// member below 0 (a tool pin: the player sets where it stands, the planner how tall) gets its member from ChooseSeedMembers.
        /// When PlansFromB, the plan is made from end B and mirrored back (the result is always along from A): every per-end input
        /// goes with its end (ropes, StationA / B, EndNodeY, EndOuter, the seeds and Forbidden mirrored, the ground sampled from B).
        /// <paramref name="terrainFromB"/> (optional) is the same ground sampled from B (along from B, lateral to the right of B to A):
        /// the planner then reads exactly the ground points a line drawn from B first reads (the lift tool passes it); null = the
        /// planner mirrors <paramref name="terrain"/>, the same points to float rounding.</summary>
        public LiftTowerPlan Plan(LiftTerrainSampler terrain, float lengthM, float ropeA, float ropeB, LiftTowerPlanConstants c,
                                  IList<LiftPlanNode> seeds = null, LiftTerrainSampler terrainFromB = null)
        {
            if (terrain == null || c == null || c.Ladder == null || c.Ladder.Length == 0)
                throw new ArgumentException("LiftTowerPlanner.Plan: terrain, constants and a ladder are required");
            if (AsGiven || !PlansFromB(ropeA, ropeB, EndKeyA, EndKeyB))
                return PlanCore(terrain, lengthM, ropeA, ropeB, c, seeds);
            float len = math.max(lengthM, 0.1f);
            IList<float> forbidden = Forbidden;
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

        /// <summary>A plan made from B, as seen from A: nodes, spans and the profile reversed, along = length - along (the "at" offsets
        /// inside a span likewise), the per-end fields swapped back. Every other number is the same (a deflection reads the same either
        /// way along the line, LiftAlignMath.DeflectionDeg).</summary>
        private static LiftTowerPlan Mirrored(LiftTowerPlan p)
        {
            float len = p.LengthM;
            int n = p.Nodes.Length;
            var nodes = new LiftPlanNode[n];
            for (int i = 0; i < n; i++)
            {
                nodes[i] = p.Nodes[n - 1 - i];
                nodes[i].AlongM = len - nodes[i].AlongM;
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

            // (a+b) fewest towers: per segment (station to station, or between seeds) the exact search; where it finds
            // no clear arrangement, the legacy phases (a) anchors / greedy split + (b) infill minimize what is left.
            for (int i = 0; i < m_K.Count - 1; )
            {
                int added = LegacySpacing ? -1 : MinTowers(i, i + 1);
                if (added < 0 && !LegacySpacing && Forbidden != null && Forbidden.Count > 0)
                {
                    // a removed spot yields where the line has no clear plan without a tower near it (LP-32).
                    m_IgnoreForbidden = true;
                    added = MinTowers(i, i + 1);
                }
                if (added < 0 && !LegacySpacing)
                {
                    // the station clearance yields only where the ground leaves no clear plan without a tower inside it (the
                    // 1,408 m gondola's top station sits on a crest: the ground 8 m in front of it is at station level, LP-22);
                    // such a tower is flagged NearStation.
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
                    added = LegacySegment(i);
                i += added + 1;
            }

            // (c) elevation.
            AssignMembers();

            // (d) rope angles: breakover / hold-down pairs (tool plans only; Build sets every node's angles and kind).
            if (Pairs && c.HasTrains)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                AnglePass();
                m_AnglesMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }

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

        /// <summary>Members for towers at FIXED positions <paramref name="innerAlongM"/> (meters from station A, ascending; the line's
        /// existing nodes): phase (c) only, no node added or moved. <paramref name="members"/> given and <paramref name="assign"/>
        /// false = evaluate those members as they stand (the "before" of a line). <paramref name="flags"/> are carried to the output
        /// (default Seed).</summary>
        public LiftTowerPlan PlanFixed(LiftTerrainSampler terrain, float lengthM, float ropeA, float ropeB, LiftTowerPlanConstants c,
                                       IList<float> innerAlongM, IList<int> members = null, IList<LiftPlanNodeFlags> flags = null,
                                       bool assign = true)
        {
            return PlanFixedCore(terrain, lengthM, ropeA, ropeB, c, innerAlongM, members, flags, assign, false);
        }

        /// <summary>PlanFixed; <paramref name="fillSpares"/> (the Moved fit): the given members are kept (the free plan's own,
        /// found by the exact search) and a member of -1 marks a spare node, which gets the shortest member that keeps both of its
        /// spans clear (else the least violation); only if the result is still violated does phase (c) run, kept when better.</summary>
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

        /// <summary>Fit a line whose node COUNT is fixed (the engine split it, CourseSplitSystem.cs:3485-3493; adding a node
        /// needs a new course). (1) Kept: heights only at the current positions. (2) Moved (allowed on a straight line): the free plan
        /// (spans capped at the engine's max edge length <paramref name="engineMaxSpanM"/>) when it needs no more towers than the line
        /// has; the spare nodes go to the midpoints of the longest spans; heights by phase (c). (3) Short: neither is clear: the better
        /// of the two by floor margin, and the tower count the free plan needs.</summary>
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
            // Hysteresis: the members the line already carries win when they clear it as they stand. Phase (c) re-run on ground that moved a few
            // decimeters (our own carve, rounding) can pick an equally clear but different assignment (a long tram flipped between two member mixes on
            // every re-fit); a clear line is final.
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
                // A tool line the engine split (LiftToolLines.MatchSplit): the extra split nodes (spareNodes) take the spare fill (the shortest
                // member that keeps both of their spans clear) while every tool node keeps its member; only when that is still not clear does phase
                // (c) below re-assign the whole line.
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
                // The free plan's own members travel with its nodes (phase (c) alone, greedy from the shortest member, missed the exact search's
                // assignment: a 1,408 m gondola was Short with 11 nodes where the free plan needs 9, then re-planned on every re-fit until the loop
                // guard); spares get FillSpare.
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

        /// <summary>A standing line (its own members, hysteresis) is kept when every span is within KeepTolM of the floor and
        /// under the max rope height. The planner's 5 / 10 m grid starts at the path's first node, so the same line sampled from
        /// the other end reads the ground a few decimeters differently: a 1,925 m tram planned from the top (+0.2 m) read -0.1 m when the fit
        /// sampled from the base and was called Short.</summary>
        public const float KeepTolM = 0.5f;

        private static bool KeepClear(LiftTowerPlan p)
        {
            for (int i = 0; i < p.Spans.Length; i++)
                if (p.Spans[i].HeightViolated || p.Spans[i].MinFloorMarginM < -KeepTolM)
                    return false;
            return true;
        }

        // Span evaluation

        private float Rope(int k, int member)
        {
            if (member < 0)
                return k == 0 ? m_RopeA : m_RopeB;
            return m_Centre[k] + m_C.RopeAboveBase(member);
        }

        /// <summary>The node point itself: the carrier at a tower passes over the high side; the rope never above the cap.</summary>
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

        /// <summary>Meters of |dy| above what EdgeSlopeKeep x MaxEdgeSlope allows on the edge (k, mk) - (k2, mk2); 0 when it passes or an
        /// end height is unknown.</summary>
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
            return worstK < 0 ? 0f : worst;
        }

        private float SpanViolation(int i, float ri, int j, float rj) => SpanViolation(i, ri, j, rj, out _, out _);

        /// <summary>The sag the floor test uses: the real one, except in phase (a) on a span that phase (b) will split (its sub-spans
        /// sag far less: FG 0.03 × 300 m = 9 m for the whole chord); a span (b) leaves whole is final and keeps its sag.</summary>
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
            return true;
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
            public int Towers;      // line towers so far
            public float EndRiseM;  // height above the shortest member of each tower next to a station end (TowerWorthM trades it)
            public int Breaks;      // towers on the uphill sample of a convex crest (breakover rule): more is better
            public int Members;     // sum of ladder indices (fewer steps = shorter towers at the same count)
            public float SumSq;     // sum of squared span lengths (even spans at the same count and steps)
            public int Parent;      // state index (k * ladder + m), -1 = the segment's start node

            public bool Better(MinCost o, float towerWorthM)
            {
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

        private bool m_IgnoreForbidden;

        private bool IsForbidden(int k)
        {
            if (Forbidden == null || m_IgnoreForbidden)
                return false;
            for (int f = 0; f < Forbidden.Count; f++)
                if (math.abs(m_S[k] - Forbidden[f]) < ForbiddenRadiusM - 1e-3f)
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
        /// MinCost.Better(worth). cap >= 0 adds the tower count to the state (layer t = towers so far, at most cap): exact for the
        /// best arrangement with at most cap towers, which the plain search is not (it keeps one state per sample and member).
        /// Working nodes between a and b (ChooseSeedMembers) must be visited, at any member.
        /// m_MinCost / m_Reached hold the search the walk back reads (state index t * (m_N + 1) * L + k * L + m).</summary>
        private bool SearchMin(int a, int b, float worth, int cap, out MinCost end)
        {
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
                    if (k > i && stop == j && (atNode || j - k >= minGap) && dEnd <= maxSpan && SlopeOk(k, mk, j, m_M[b]) &&
                        SpanInterval(k, rk, j, out float lo, out float hi) && rj >= lo - Tol && rj <= hi + Tol)
                    {
                        MinCost c = from;
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
                        if (!SpanInterval(k, rk, k2, out float lo2, out float hi2))
                            continue;
                        float d = m_S[k2] - m_S[k];
                        for (int m2 = 0; m2 < L; m2++)
                        {
                            float r2 = Rope(k2, m2);
                            if (r2 < lo2 - Tol)
                                continue;
                            if (r2 > hi2 + Tol)
                                break;   // ropes ascend with the ladder
                            if (!MemberAllowed(k2, m2) || !NodeOk(k2, m2) || !SlopeOk(k, mk, k2, m2))
                                continue;
                            MinCost c = from;
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

        /// <summary>The members of seeds placed without one: one search over the whole line with every seed a node it must visit, so a
        /// pin is as tall as the line needs and the towers between pins are the fewest. Searched span by span from a pin at its shortest
        /// member (the old way), each span added towers only to rescue that short tower: removing one tower of three gave nine (LP-32).
        /// Phase (c) still sets the final members. No clear plan at all: the members stay and the segments' fallback reports it.</summary>
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

        /// <summary>Between working nodes a and b = a + 1: the arrangement with the FEWEST towers such that every span is at most
        /// MaxSpanM and clear with its real sag (floor over the high side, max rope height), every node keeps the floor over its own
        /// high side; then the most towers just uphill of a convex crest (breakover rule, never at the cost of a tower); then the
        /// fewest ladder steps; then the most even spans. Exact: a shortest-path search over samples x ladder
        /// members (states k * L + m), each span's far-end rope interval from SpanInterval (one pass per span), so a tall member
        /// wins wherever it lets a span reach further and saves a tower. With the station rule on, a segment that ends at a station
        /// may use more towers for shorter station-side ones (TowerWorthM), at most one more than the fewest per line. Returns the
        /// nodes added (0 when the segment is already one clear span), -1 when no clear arrangement exists (the caller falls back to LegacySegment).</summary>
        private int MinTowers(int a, int b)
        {
            m_UseSag = true;
            int i = m_K[a], j = m_K[b];
            float ri = Rope(i, m_M[a]), rj = Rope(j, m_M[b]);
            // One span, clear for the floor: done when the edge also passes the slope rule. Else the search; where it finds nothing
            // (a chord between fixed end heights steeper than the rule cannot be cut by towers: their edges average the same slope),
            // the span stays as it is and Build flags it SlopeViolated (no LegacySegment towers that fix nothing).
            bool oneSpan = m_S[j] - m_S[i] <= m_C.MaxSpanM + 1e-3f && SpanViolation(i, ri, j, rj) <= Tol;
            if (oneSpan && SlopeOk(i, m_M[a], j, m_M[b]))
                return 0;
            // The station rule (TowerWorthM) trades towers for shorter station-side towers, but buys at most ONE tower more than the
            // fewest per line (m_ExtraTowers): the fewest-towers search first, then the weighted one; when the weighted best needs
            // more towers than the budget allows, the search again with the tower count in the state (exact under the cap).
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
            // Walk back; every insert lands between a and the end node. Flags against the segment's straight chord: Shoulder (the
            // ground pokes through it), Valley (it is too high above the ground), else Infill.
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
            int added = anchors;
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

        /// <summary>Phase (a) between working nodes a and b = a + 1: the anchor set with the fewest towers (anchors + the infill (b)
        /// will add), then the fewest anchors, the shortest members, and anchors nearest the worst-intrusion sample, such that every
        /// span is clear (floor against the bare chord, max height with the sag). This is the recursive worst-intrusion split solved
        /// exactly (a search over the samples) instead of greedily: the greedy split put 18 towers on LP-5's 40 m wall. Returns the
        /// nodes added, 0 when the span is already clear, -1 when no clear arrangement exists.</summary>
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
            // Anchor spans are capped at 3 x MaxSpanM (phase (b) splits them anyway): bounds the search to O(samples x cap x span)
            // per ladder member at the source; SpanInterval removes the far member's factor.
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

            // Walk back; every insert lands between a and the end node, so a stays valid.
            int added = 0;
            int state = end.Parent;
            float chordSlope = (rj - ri) / math.max(m_S[j] - m_S[i], 1e-3f);
            while (state >= 0)
            {
                int k = state / L, m = state % L;
                // Shoulder: the ground pokes through the station-to-station chord here (floor); else Valley (max height).
                float chordHere = ri + chordSlope * (m_S[k] - m_S[i]);
                bool shoulder = m_C.CarrierDropM + m_Floor[k] + m_High[k] > chordHere;
                Insert(k, m, shoulder ? LiftPlanNodeFlags.Shoulder : LiftPlanNodeFlags.Valley);
                added++;
                state = m_Cost[state].Parent;
            }
            BreakoverPass(a, a + added + 1);
            return added;
        }

        /// <summary>Shoulders on convex ground (between working nodes a and b) become breakovers: each moves one sample past the crest
        /// toward the higher ground (uphill of the crest) with the shortest member that keeps both of
        /// its spans clear and adds no infill tower.</summary>
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

        /// <summary>Fallback when no clear arrangement exists (e.g. ground through the rope right at a station): greedy split of the
        /// span between working nodes a and a+1 (b == a + 1) while it is violated; returns the nodes added.</summary>
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

        /// <summary>The station-side exchange rate in use (LiftTowerPlanConstants.TowerWorthM; 0 while StationRule is off).</summary>
        private float TowerWorth => StationRule ? m_C.TowerWorthM : 0f;

        /// <summary>The towers the station rule may still add over the fewest, per line (DECISIONS 429: at most one); Plan resets it.</summary>
        private int m_ExtraTowers;

        private int MinGap() => math.max(1, (int)math.ceil(m_C.MinSpanM / m_C.StepM - 1e-4f));

        /// <summary>Sample k is inside a station's clearance (StationClearM from either end, in meters: the grid's last step is
        /// short, so a sample count alone let a tower stand 8 m from a 10 m-MinSpan station).</summary>
        private bool NearStation(int k) =>
            !m_NoClearance && (m_S[k] < m_C.StationClearM - Tol || m_Length - m_S[k] < m_C.StationClearM - Tol);

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
            // The fewest sub-spans that respect MaxSpanM; LegacySpacing keeps the typical-span count.
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
                    // Pass 0: the equal-spacing samples; pass 1: each node slides within half a sub-span of its spot.
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

        /// <summary>Place n-1 infill nodes between working nodes a and b; returns the summed violation of the n sub-spans
        /// (+inf when no placement satisfies the span-length rules).</summary>
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
            // The station rule chose the members beside the stations (a tower bought for them included): the reset above must not
            // undo that. Where it raised one, that node goes back to the search's member; if that breaks the line, the search's
            // whole assignment is kept (it was clear).
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

        /// <summary>True when the touched towers at <paramref name="sA"/> (and <paramref name="sB"/> when <paramref name="two"/>), between the
        /// unchanged working nodes <paramref name="iL"/> and <paramref name="iR"/> (-1: none), would stand in a group of more than MaxGroup
        /// consecutive towers PairMaxM or nearer apart: pairs, never threes. Station ends are not towers.</summary>
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
            d = LiftAlignMath.DeflectionDeg(p, q, n, m_C.SagRatio);
            float ad = math.abs(d);
            if (ad <= m_MinCap)
                return ad - m_MinCap;
            LiftAlignMath.LoadCaseDeflections(p, q, n, m_C.SagRatio, out float tight, out float heavy);
            return ad - m_C.CapDeg(LiftAlignMath.ClassifyTrain(tight, heavy));
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

        /// <summary>One candidate repair. Kind 1 single (node n to (K1, M1)), 2 pair (n -> (K1, M1) + new (K2, M2)), 3 partner (n keeps its
        /// spot with member M1, new (K2, M2)), 4 re-member (n gets M1, working node K2 gets M2; two fixed towers PairMaxM or nearer),
        /// 5 pull (fixed n gets M1, its free neighbor, working node K1, moves to (K2, M2) as the partner).</summary>
        private struct AngleFix
        {
            public int Kind, Towers, Steps, K1, M1, K2, M2;
            public float MaxD, GapM, DistM, D1, D2;

            /// <summary>Rank: fewer towers; fewer ladder steps; smaller max |d|; shorter spacing (reads as a pair); nearer the old spot.
            /// A tie keeps the earlier candidate (ascending sample, then member: deterministic).</summary>
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

        /// <summary>Judges a local change: touched towers T1 = (kA, mA) [and T2 = (kB, mB)], in line order, between the unchanged working
        /// nodes iL (-1: T1 is the A end, its left point the outer A point) and iR (-1: the last T is the B end, outer B). A member of -1
        /// is a line end (its rope fixed). True when every touched span is fine, the touched towers are within their caps and the
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

        /// <summary>Repairs working node <paramref name="n"/> (deflection <paramref name="d0"/>, <paramref name="over0"/> past its cap):
        /// the cheapest feasible of (a) the tower shifted within AngleShiftM with any member, or into a fixed neighbor's partner window
        /// (a pin or a seed keeps its spot and only changes member); (b) a free tower replaced by a pair PairMinM..PairMaxM apart
        /// that straddles its spot or stands within AngleShiftM of it; a fixed tower (a pin, a seed, the junction end) keeps its spot
        /// and its free neighbor moves in as its partner (no tower added) or a new free partner stands beside it, unless a fixed
        /// neighbor stands PairMaxM or nearer (two pins 10 m apart are the player's choice: only their members change).
        /// No candidate may leave three towers in a row PairMaxM or nearer apart (MaxGroup). None feasible: flagged OverCap, logged
        /// with the reasons.</summary>
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

            // (a) single: shifted (free) or re-membered in place (fixed). A free tower PairMaxM or nearer to a fixed tower (a pin, a
            // seed, the junction end) may also move anywhere in that tower's partner window (PairMinM..PairMaxM on its side): the
            // free planner's filler beside a pin becomes the pin's partner instead of a third tower beside a new pair.
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

            // (b) pair / partner / re-member (only when no single fix exists: a single adds no tower and ranks first)
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

            string at = (isEnd ? "end " : "p") + NoteNode(n) + " at " + NoteAt(s0) + " m (" +
                        (m0 >= 0 ? Short(m_C.Ladder[m0].Name) : "line end") + (fixedNode && !isEnd ? ", pinned / seed" : string.Empty) + "): d " +
                        LiftTowerPlan.Sg(d0) + ", " + LiftTowerPlan.F(over0, "F1") + " past its cap";
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

        /// <summary>SetAngles for one node (its kind without hysteresis, the cap of its current head; AssignHeads refines both).</summary>
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
            LiftAlignMath.LoadCaseDeflections(p, q, n, h, out nodes[i].TightDeg, out nodes[i].HeavyDeg);
            if (m_C.HasTrains)
            {
                nodes[i].Kind = LiftAlignMath.ClassifyTrain(nodes[i].TightDeg, nodes[i].HeavyDeg);
                nodes[i].CapDeg = m_C.CapDeg(nodes[i].Kind, nodes[i].Head);
            }
            else
            {
                nodes[i].Kind = LiftTrainKind.None;
                nodes[i].CapDeg = float.PositiveInfinity;
                nodes[i].BareDeg = LiftAlignMath.DeflectionDeg(p, q, n, h * LiftAlignMath.BareSagScale);
            }
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
        /// (KindAndWant) when its member has that twin. A member without it: the nearest member (fewest ladder steps, then the shorter)
        /// with that twin whose node, both spans and (CheckEdgeSlope) both edges stay clear at the twin's E, whose own kind still wants
        /// that head and whose neighbor before it keeps its head; else the Line head stays and an angle note says why. The tram (no
        /// trains) and a type without loaded twins are untouched. Nodes are visited from A, so the result is deterministic.</summary>
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

        /// <summary>The engine's node height of output node <paramref name="node"/> at sample <paramref name="k"/>: ground +
        /// E(member, head); a line end at EndNodeYA / EndNodeYB.</summary>
        private float NodeY(int k, LiftPlanNode node) =>
            node.Member < 0 ? (k == 0 ? EndNodeYA : EndNodeYB) : m_Centre[k] + LiftTowerEdit.ElevationM(m_C, node.Member, node.Head);

        /// <summary>Both edges of output node <paramref name="i"/> with (member, head) at it pass the engine's slope rule (MaxEdgeSlope +
        /// 0.0005, ValidationHelpers.cs:1462-1463: the LiftPlanSpan.SlopeViolated test); always true unless CheckEdgeSlope. The
        /// neighbors stand at their own E (member, head).</summary>
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
            var spans = new LiftPlanSpan[count - 1];
            for (int s = 0; s < count - 1; s++)
            {
                int i = m_K[s], j = m_K[s + 1];
                float ri = nodes[s].RopeM, rj = nodes[s + 1].RopeM;
                float d = m_S[j] - m_S[i];
                float sag4 = 4f * m_C.SagRatio * d;
                var span = new LiftPlanSpan { LengthM = d, MinFloorMarginM = float.PositiveInfinity, MaxRopeAboveM = float.NegativeInfinity };
                for (int q = i; q <= j; q++)
                {
                    // Station ends are not judged (their rope height is given); tower ends are (the carrier passes over the node).
                    if ((q == i && nodes[s].IsStation) || (q == j && nodes[s + 1].IsStation))
                        continue;
                    float t = d > 0f ? (m_S[q] - m_S[i]) / d : 0f;
                    float r = ri + (rj - ri) * t - sag4 * t * (1f - t);
                    float margin = r - m_C.CarrierDropM - m_Floor[q] - m_High[q];
                    if (margin < span.MinFloorMarginM)
                    {
                        span.MinFloorMarginM = margin;
                        span.MinFloorMarginAtM = m_S[q] - m_S[i];
                        span.MinChairAboveGroundM = r - m_C.CarrierDropM - m_High[q];
                    }
                    float above = r - m_Centre[q];
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
            };
        }
    }
}
