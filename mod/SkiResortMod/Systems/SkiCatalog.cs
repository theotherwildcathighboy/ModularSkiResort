// The pure prefab catalog: every prefab name the lift / station / lodge / sign / hub-build code resolves has ONE home here, with
// its numbers: 4 lift types (fixed-grip triple, detachable quad, gondola 8, aerial tram), 8 stations, the lodge ladder (5 tiers),
// the ski school, 2 patrol huts, the sign and the runs, plus the ONE build-item table the hub strip mirrors.
// Pure file: `using System;`, `using System.Collections.Generic;`, `using Unity.Mathematics;` only, plus RunKind (EntityRef.cs).
// Linked into tests/LiftBatch and tests/ResortBatch.
// Names are save identity: PrefabSystem.Serialize writes PrefabID = type + name (PrefabSystem.cs:827-865, 1.6.0f1) and a load
// looks it up by that ID (:878-892). Never rename an entry; a new generation is a new name.
//
// Expected / pending: every NAME carries its own Expected flag. false = "pending": the prefab is designed and named but not
// imported yet (its NAMES line ends ", pending"). A MISSING pending name is an Info line, never a WARN; a FOUND pending name works
// exactly like an expected one (behavior never keys on the flag) and SkiPrefabCatalogSystem writes one "[pending-found]" WARN per
// load so the import commit flips it. A row's Expected (LiftTypeDef.Expected, StationDef.Expected, ...) is DERIVED: true when
// every name of the row is expected. A family imported over two sessions flips name by name, so no WARN window opens between
// them. Flipping a name = delete ", pending" on its NAMES line and set its row flag (LineExpected / CarrierExpected / Expected)
// to true in the same edit; LiftBatch CT-13 fails on any disagreement.
//
// What reads this file:
//   * SkiPrefabCatalogSystem (the ECS side): resolves every name through ONE typed resolver, lazily per load, revalidated each
//     frame, and writes one "PrefabCatalog [arm]" line per load.
//   * The pure rule files ALIAS the FixedGrip / AFrameLodge / Ladder / School / Patrol / LodgeSpend consts (SkiMoneyRules,
//     SkiDayRules and SchoolRule are Burst-callable, so only compile-time consts may cross; a `SkiCatalog.Lifts[i].X` read there
//     would break Burst). Per-row numbers reach Burst jobs as struct fields filled on the main thread.
//   * tools/preflight.ps1 section 7 NAMES and tests/LiftBatch CatalogTests CT-8 / CT-13 parse the NAMES block as TEXT; CT-9 /
//     CT-14 and mod/SkiResortUI/tools/check-bundle.js R16 parse the BUILD-ITEMS block as TEXT.
//   * catalog/carriers.json is the ONE carrier-contract source: the generators read it, this file mirrors its numbers as
//     literals (the mod never reads repo files), LiftBatch CT-11 compares the two.
//   * tests/ResortBatch CatalogGuardTests: alias == row == const, and the locale texts against these numbers.
//
// The static constructor NEVER throws (a TypeInitializationException would take down every system that touches the class):
// every sanity finding goes to Problems, which SkiPrefabCatalogSystem.OnCreate logs as "!!!" and LiftBatch CT-1 fails on. Nothing
// allocates after the static constructor.
//
// Shuttle (tram) profile: LiftTypeDef .AccelMps2 / .DwellS (ShuttleProfile.cs): tram 0.8 m/s2 (real jig-back trams are quoted
// around 0.5-1.0 m/s2, unverified) and 60 s dwell (carriers.json tram dwell_s 60, CT-11); loop types 0 / 0 (unused).
// StationDef .DockLocalA / .DockLocalB: the track-rope point at bay 0 / 1, prefab-local, at the ROPE (y 16.00 = track y, not the
// carriage top 16.50); NaN on Loop stations. .HostsCarriers: the prefab's ObjectSubObjects host this type's carriers (the tram
// base). Source: SkiTram{Base,Top}StationA.manifest.json prefab_contract.docks[0..1].rope. Sanity (CheckRows): Shuttle types need
// SpeedMps / AccelMps2 > 0, DwellS >= 0 (finite), exactly one Base station that HostsCarriers, no Top that does, finite docks on
// every station; Loop types need AccelMps2 = DwellS = 0, NaN docks and HostsCarriers false.
// StationDef .StubTipLocalX = the lift LINE AXIS in prefab x (the HV stub tip is (StubTipLocalX, 0, StubTipLocalZ); StationAlign
// .StubTip's 4-argument overload). 0 everywhere but the tram base: its lot is 6 x 3 (grown on the clock-tower side for the 1:8
// ramp) and a CS2 lot is centered on the prefab origin, so the building sits 4 m off the origin. The game negates FBX x on
// import and the prefab rows are mirrored, so in the GAME frame the tram base's axis is +4.00 and its docks A / B (9.50 / -1.50,
// 16.00, -5.50); the manifest numbers (axis_x -4.0, docks -9.50 / 1.50) are the authoring frame (-x). The tram top (axis 0, docks
// -5.50 / 5.50) maps onto itself. Sanity: a Shuttle station's docks are centered on its axis.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>APPEND ONLY. Mid is appended when mid-stations are designed.</summary>
    public enum StationRole : byte { Base = 0, Top = 1 }

    /// <summary>APPEND ONLY.</summary>
    public enum CatalogKind : byte { Line = 0, Carrier = 1, Marker = 2, Station = 3, Lodge = 4, Sign = 5, Run = 6, School = 7, Patrol = 8 }

    /// <summary>SkiPrefabCatalogSystem's next step for a query (NextAction; LiftBatch CT-10).</summary>
    public enum CatalogAction : byte { Serve = 0, Validate = 1, Resolve = 2 }

    /// <summary>How a lift type's carriers move: Loop = ChairPinSystem's endless loop; Shuttle = the tram.</summary>
    public enum MoverKind : byte { Loop = 0, Shuttle = 1 }

    /// <summary>LiftServedRule.Lift.Rule carries the byte. AlongLine = within the radius of the line's chords;
    /// TerminalsOnly = within the radius of the top station's stub tip (the tram).</summary>
    public enum ServeRuleKind : byte { AlongLine = 0, TerminalsOnly = 1 }

    /// <summary>LodgeRoadSystem's rule per building row: Base = needs a road (today's A-frame); Mountain = no road
    /// needed, never warned (NoRoadConnection cleared, RequireRoad off); None = not written at all (patrol huts).</summary>
    public enum LodgeRoadRule : byte { Base = 0, Mountain = 1, None = 2 }

    /// <summary>The build strip's item kinds (bindings.ts BuildItemMeta.kind, lower camel). APPEND ONLY.</summary>
    public enum BuildItemKind : byte { Sign = 0, Lodge = 1, School = 2, Patrol = 3, Station = 4, Line = 5, Run = 6 }

    /// <summary>One prefab name of the catalog: the PrefabID (type + name), its hub build item id and its Expected flag.</summary>
    public readonly struct CatalogName
    {
        public readonly string Name;         // prefab name (save identity)
        public readonly string TypeName;     // PrefabID type: SkiCatalog.TypeLine / TypeCarrier / TypeBuilding / TypeSurface
        public readonly CatalogKind Kind;
        public readonly string BuildItemId;  // hub build item id (bindings.ts BUILD_ITEM_IDS) or null
        public readonly bool Expected;       // false = pending (not imported yet): MISSING logs Info, not WARN

        public CatalogName(string name, string typeName, CatalogKind kind, string buildItemId, bool expected)
        {
            Name = name;
            TypeName = typeName;
            Kind = kind;
            BuildItemId = buildItemId;
            Expected = expected;
        }

        public override string ToString()
        {
            return Name + " (" + TypeName + (BuildItemId != null ? ", item " + BuildItemId : "") + (Expected ? "" : ", pending") + ")";
        }
    }

    /// <summary>A lift type's net envelope. INFORMATIONAL: the prefabs carry the real values;
    /// LiftBatch CT-12 compares an Expected row with gamedata-mirror, CT-15 the HeightRange bound.</summary>
    public readonly struct LiftEnvelope
    {
        public readonly float MaxPylonM;        // PowerLinePrefab m_MaxPylonDistance (= CourseSplit's max edge length)
        public readonly float SlopeMax;         // m_MaxSlopeSteepness
        public readonly float NetHanging;       // the line's m_Hanging
        public readonly float LaneHanging;      // the rope lanes' m_Hanging (0.004 on long spans)
        public readonly float HeightRangeMinM;  // the piece's m_HeightRange.min (clearance formula)
        public readonly float ElevationMaxM;    // PlaceableNet m_ElevationRange.max

        public LiftEnvelope(float maxPylonM, float slopeMax, float netHanging, float laneHanging, float heightRangeMinM,
                            float elevationMaxM)
        {
            MaxPylonM = maxPylonM;
            SlopeMax = slopeMax;
            NetHanging = netHanging;
            LaneHanging = laneHanging;
            HeightRangeMinM = heightRangeMinM;
            ElevationMaxM = elevationMaxM;
        }
    }

    /// <summary>Mutable builder for one lift row (public fields; object-initializer syntax in the static ctor and the
    /// tests). LiftTypeDef copies it; nothing keeps a reference.</summary>
    public sealed class LiftTypeSpec
    {
        public string Id, LinePrefab, PiecePrefab, LineItemId;
        public string[] CarrierPrefabs;
        public MoverKind Mover;
        public int SeatsPerCarrier;
        public float SpeedMps, GaugeHalfM, HangM, GripTopAboveRopeM, AxleM, AimHeightM, SpacingM, DesignSpacingM,
                     ServeRadiusM, TopMarginM, FixedPerMonth, PerKmPerMonth;
        public int LineBuildPer8m, ElevationCostPer10m, StationCostBase, StationCostTop;
        /// <summary>The shuttle profile's acceleration (m/s²) and station dwell (s); 0 / 0 on Loop types (Sanity).</summary>
        public float AccelMps2, DwellS;
        public double ScoreValue, SummerShare;
        public ServeRuleKind ServeRule;
        public LiftEnvelope Envelope;
        public bool CloseElectricityView;
        /// <summary>The LINE name's flag (its NAMES line has no ", pending").</summary>
        public bool LineExpected;
        /// <summary>Per CarrierPrefabs entry; null (or a shorter array) = that carrier takes LineExpected.</summary>
        public bool[] CarrierExpected;
    }

    /// <summary>One carrier prefab of a lift type (the carriers.json row's numbers, copied from the owning row).</summary>
    public sealed class CarrierDef
    {
        public readonly string Prefab;          // StaticObjectPrefab name
        public readonly string LiftTypeId;
        public readonly int LiftTypeIndex;
        public readonly int Seats;
        public readonly float HangM;            // grip top → lowest point (carriers.json hang_m)
        public readonly float GripTopAboveRopeM;
        public readonly bool Expected;          // this NAME's flag

        internal CarrierDef(string prefab, string liftTypeId, int liftTypeIndex, int seats, float hangM, float gripTopAboveRopeM,
                            bool expected)
        {
            Prefab = prefab;
            LiftTypeId = liftTypeId;
            LiftTypeIndex = liftTypeIndex;
            Seats = seats;
            HangM = hangM;
            GripTopAboveRopeM = gripTopAboveRopeM;
            Expected = expected;
        }
    }

    /// <summary>One lift type: its line, its carriers and its numbers. Built from a LiftTypeSpec.</summary>
    public sealed class LiftTypeDef
    {
        public readonly int Index;                             // position in SkiCatalog.Lifts — APPEND ONLY, Lifts[0] = fixedGrip forever
        public readonly string Id;                             // "fixedGrip", "detachable", "gondola", "tram": stable (logs, hub ids)
        public readonly string LinePrefab;                     // PowerLinePrefab name
        public readonly string PiecePrefab;                    // NetPiecePrefab name: informational (not resolved; CT-12 reads its mirror)
        public readonly string BuildItemId;                    // the LINE item ("lift", "liftDetachable", …)
        public readonly IReadOnlyList<string> CarrierPrefabs;  // StaticObjectPrefab names; grip height from mesh bounds
        public readonly IReadOnlyList<CarrierDef> Carriers;    // the same carriers with their numbers and flags
        public readonly MoverKind Mover;
        public readonly int SeatsPerCarrier;
        public readonly float SpeedMps;
        public readonly float GaugeHalfM;                      // rope lane |x|
        public readonly float HangM;                           // carrier contract (carriers.json hang_m)
        public readonly float GripTopAboveRopeM;               // informational (the grip top rides ON the lane)
        public readonly float AxleM;                           // lane y on the shortest tower
        public readonly float AimHeightM;                      // CursorAim's line aim plane
        public readonly float SpacingM;                        // the line prefab's NetSubObjects m_Spacing; 0 = Shuttle
        public readonly float DesignSpacingM;                  // the ride spacing the design wants (carriers.json spacing_m)
        public readonly float ServeRadiusM;
        public readonly float TopMarginM;
        public readonly float FixedPerMonth;
        public readonly float PerKmPerMonth;
        public readonly ServeRuleKind ServeRule;
        public readonly int LineBuildPer8m;                    // the piece's m_ConstructionCost per 8 m piece
        public readonly int ElevationCostPer10m;               // informational (the piece's m_ElevationCost)
        public readonly int StationCostBase;
        public readonly int StationCostTop;
        public readonly float AccelMps2;                       // shuttle acceleration / deceleration, m/s² (Loop 0)
        public readonly float DwellS;                          // shuttle dwell at each terminal, s (Loop 0)
        public readonly int LinePerKm;                         // computed: LineBuildPer8m × 125 (8 m pieces per km)
        public readonly float ApproachPinCeilingM;             // computed: max(ApproachPinCeilingFloorM, HangM + 1.5)
        public readonly double ScoreValue;                     // winter weight
        public readonly double SummerShare;                    // summer share (lambda)
        public readonly LiftEnvelope Envelope;
        public readonly bool CloseElectricityView;
        public readonly bool LineExpected;                     // the line NAME's flag
        public readonly bool Expected;                         // derived: the line and every carrier expected

        internal LiftTypeDef(int index, LiftTypeSpec s)
        {
            Index = index;
            Id = s.Id;
            LinePrefab = s.LinePrefab;
            PiecePrefab = s.PiecePrefab;
            BuildItemId = s.LineItemId;
            Mover = s.Mover;
            SeatsPerCarrier = s.SeatsPerCarrier;
            SpeedMps = s.SpeedMps;
            GaugeHalfM = s.GaugeHalfM;
            HangM = s.HangM;
            GripTopAboveRopeM = s.GripTopAboveRopeM;
            AxleM = s.AxleM;
            AimHeightM = s.AimHeightM;
            SpacingM = s.SpacingM;
            DesignSpacingM = s.DesignSpacingM;
            ServeRadiusM = s.ServeRadiusM;
            TopMarginM = s.TopMarginM;
            FixedPerMonth = s.FixedPerMonth;
            PerKmPerMonth = s.PerKmPerMonth;
            ServeRule = s.ServeRule;
            LineBuildPer8m = s.LineBuildPer8m;
            ElevationCostPer10m = s.ElevationCostPer10m;
            StationCostBase = s.StationCostBase;
            StationCostTop = s.StationCostTop;
            AccelMps2 = s.AccelMps2;
            DwellS = s.DwellS;
            LinePerKm = s.LineBuildPer8m * SkiCatalog.PiecesPerKm;
            ApproachPinCeilingM = math.max(SkiCatalog.ApproachPinCeilingFloorM, s.HangM + SkiCatalog.ApproachPinHangMarginM);
            ScoreValue = s.ScoreValue;
            SummerShare = s.SummerShare;
            Envelope = s.Envelope;
            CloseElectricityView = s.CloseElectricityView;
            LineExpected = s.LineExpected;

            // Copies: the caller's arrays are never shared (a later write to them cannot change the catalog).
            string[] carriers = s.CarrierPrefabs != null ? (string[])s.CarrierPrefabs.Clone() : new string[0];
            CarrierPrefabs = Array.AsReadOnly(carriers);
            var defs = new CarrierDef[carriers.Length];
            bool all = s.LineExpected;
            for (int c = 0; c < carriers.Length; c++)
            {
                bool expected = s.CarrierExpected != null && c < s.CarrierExpected.Length ? s.CarrierExpected[c] : s.LineExpected;
                defs[c] = new CarrierDef(carriers[c], s.Id, index, s.SeatsPerCarrier, s.HangM, s.GripTopAboveRopeM, expected);
                all &= expected;
            }
            Carriers = Array.AsReadOnly(defs);
            Expected = all;
        }
    }

    /// <summary>One terminal building. Base or top comes from Role, never from name equality.</summary>
    public sealed class StationDef
    {
        public readonly string Prefab;         // BuildingPrefab name
        public readonly StationRole Role;
        public readonly string LiftTypeId;     // the LiftTypeDef.Id this terminal belongs to
        public readonly int LiftTypeIndex;     // SkiCatalog.Lifts index; −1 = unknown (a Problems entry)
        public readonly float3 WheelLocal;     // bullwheel center at rope height, prefab-local; NaN = no bullwheel arc (Shuttle)
        public readonly float StubTipLocalZ;   // HV stub's outer node, prefab-local z; per row (−16 DQ / gondola)
        public readonly int BuildCost;         // informational mirror of PlaceableObject.m_ConstructionCost (the hub reads the prefab)
        public readonly string BuildItemId;    // "baseStation", "topStationDetachable", …
        public readonly bool Expected;         // this NAME's flag
        /// <summary>The track-rope point at the hosted-entry-0 bay, prefab-local (game frame: the tram base's A is the +X bay (9.50,
        /// 16.00, −5.50), the tram top's A the −X bay (−5.50, 16.00, −5.50)); NaN = no dock (Loop stations).</summary>
        public readonly float3 DockLocalA;
        /// <summary>The other bay's track-rope point (the tram base's B is the −X bay (−1.50, 16.00, −5.50), the top's the +X bay); NaN = no dock.</summary>
        public readonly float3 DockLocalB;
        /// <summary>The prefab's ObjectSubObjects host this type's carriers (the tram base).</summary>
        public readonly bool HostsCarriers;
        /// <summary>The lift LINE AXIS, prefab-local x: the HV stub tip is (StubTipLocalX, 0, StubTipLocalZ) and the docks sit about it.
        /// 0 on every station but the tram base, whose lot is 6 x 3: a CS2 lot is centered on the prefab origin, so the building sits 4 m
        /// off the origin (+4.00 in the game frame).</summary>
        public readonly float StubTipLocalX;

        /// <summary>LiftTypeIndex −1 (unknown). The catalog's own rows use the overload below; this one exists for hand-made rows (tests).</summary>
        public StationDef(string prefab, StationRole role, string liftTypeId, float3 wheelLocal, float stubTipLocalZ,
                          int buildCost, string buildItemId, bool expected)
            : this(prefab, role, liftTypeId, wheelLocal, stubTipLocalZ, buildCost, buildItemId, expected, -1)
        {
        }

        /// <summary>A station without docks (NaN) that hosts no carriers: every Loop station.</summary>
        public StationDef(string prefab, StationRole role, string liftTypeId, float3 wheelLocal, float stubTipLocalZ,
                          int buildCost, string buildItemId, bool expected, int liftTypeIndex)
            : this(prefab, role, liftTypeId, wheelLocal, stubTipLocalZ, buildCost, buildItemId, expected, liftTypeIndex,
                   new float3(float.NaN), new float3(float.NaN), false)
        {
        }

        /// <summary>The full row, with the two docks and the carrier-host flag (the line axis at prefab x 0).</summary>
        public StationDef(string prefab, StationRole role, string liftTypeId, float3 wheelLocal, float stubTipLocalZ,
                          int buildCost, string buildItemId, bool expected, int liftTypeIndex,
                          float3 dockLocalA, float3 dockLocalB, bool hostsCarriers)
            : this(prefab, role, liftTypeId, wheelLocal, stubTipLocalZ, buildCost, buildItemId, expected, liftTypeIndex,
                   dockLocalA, dockLocalB, hostsCarriers, 0f)
        {
        }

        /// <summary>The full row with the line axis off the prefab origin (StubTipLocalX; the tram base).</summary>
        public StationDef(string prefab, StationRole role, string liftTypeId, float3 wheelLocal, float stubTipLocalZ,
                          int buildCost, string buildItemId, bool expected, int liftTypeIndex,
                          float3 dockLocalA, float3 dockLocalB, bool hostsCarriers, float stubTipLocalX)
        {
            Prefab = prefab;
            Role = role;
            LiftTypeId = liftTypeId;
            LiftTypeIndex = liftTypeIndex;
            WheelLocal = wheelLocal;
            StubTipLocalZ = stubTipLocalZ;
            BuildCost = buildCost;
            BuildItemId = buildItemId;
            Expected = expected;
            DockLocalA = dockLocalA;
            DockLocalB = dockLocalB;
            HostsCarriers = hostsCarriers;
            StubTipLocalX = stubTipLocalX;
        }
    }

    /// <summary>One lodge tier.</summary>
    public sealed class LodgeDef
    {
        public readonly int Index;               // position in SkiCatalog.Lodges — APPEND ONLY, Lodges[0] = the A-frame
        public readonly string Id;               // "aFrame", "hut", "mid", "summit", "grand"
        public readonly string Prefab;
        public readonly string BuildItemId;      // "lodge", "lodgeHut", …
        public readonly double B;                // lodge base when it is the best lodge
        public readonly double E;                // extra when it is not
        public readonly double Mu;               // summer share
        public readonly double MuSummit;         // summer share when summit-served (within Ladder.SummitServedRadiusM of a top station)
        public readonly int Capacity;            // skiers a day (ski-day capacity)
        public readonly int PriceTolerance;      // ticket price added before the ski-day price factor falls
        public readonly int Jobs;                // informational: the prefab's Workplace is the truth
        public readonly int SignJobs;            // jobs the linked Welcome Sign carries for a roadless row (MountainJobsSystem)
        public readonly int BuildCost;           // informational: PlaceableObject.m_ConstructionCost
        public readonly int Upkeep;              // informational: ServiceConsumption.m_Upkeep
        public readonly bool ForbidMultiple;     // ServiceUpgrade.m_ForbidMultiple (engine-validated per prefab per owner); false on every lodge tier
        public readonly bool Luxury;             // the luxury tiers (summit, grand): a LABEL for the tip's price-tolerance line and the logs. NO count limit
                                                 // (any number per ski area; the ladder's 24-point extras cap is the only limiter)
        public readonly float SpendMultiplier;   // lodge spend per guest × this
        public readonly LodgeRoadRule RoadRule;
        public readonly bool Expected;           // this NAME's flag

        public LodgeDef(int index, string id, string prefab, string buildItemId, double b, double e, double mu, double muSummit,
                        int capacity, int priceTolerance, int jobs, int buildCost, int upkeep, bool forbidMultiple,
                        bool luxury, float spendMultiplier, LodgeRoadRule roadRule, bool expected, int signJobs = 0)
        {
            Index = index;
            SignJobs = signJobs;
            Id = id;
            Prefab = prefab;
            BuildItemId = buildItemId;
            B = b;
            E = e;
            Mu = mu;
            MuSummit = muSummit;
            Capacity = capacity;
            PriceTolerance = priceTolerance;
            Jobs = jobs;
            BuildCost = buildCost;
            Upkeep = upkeep;
            ForbidMultiple = forbidMultiple;
            Luxury = luxury;
            SpendMultiplier = spendMultiplier;
            RoadRule = roadRule;
            Expected = expected;
        }

        /// <summary>Alias of Capacity (LodgeRoad / SkiDayRules callers).</summary>
        public double SkiersPerDay => Capacity;
    }

    /// <summary>The ski school: +ScoreValue to S and locals × DemandFactor while a green run counts.</summary>
    public sealed class SchoolDef
    {
        public readonly string Prefab, BuildItemId;
        public readonly double ScoreValue, DemandFactor;
        public readonly int Jobs, BuildCost, Upkeep;
        public readonly bool ForbidMultiple;
        public readonly LodgeRoadRule RoadRule;
        public readonly bool Expected;

        public SchoolDef(string prefab, string buildItemId, double scoreValue, double demandFactor, int jobs, int buildCost,
                         int upkeep, bool forbidMultiple, LodgeRoadRule roadRule, bool expected)
        {
            Prefab = prefab;
            BuildItemId = buildItemId;
            ScoreValue = scoreValue;
            DemandFactor = demandFactor;
            Jobs = jobs;
            BuildCost = buildCost;
            Upkeep = upkeep;
            ForbidMultiple = forbidMultiple;
            RoadRule = roadRule;
            Expected = expected;
        }
    }

    /// <summary>A ski patrol hut (both huts share one row's numbers; the HQ differs in look only).</summary>
    public sealed class PatrolDef
    {
        public readonly string Prefab, BuildItemId;
        public readonly float RadiusM;
        public readonly double ExpertFactor, BackcountryServedMin;
        public readonly int Jobs, BuildCost, Upkeep;
        public readonly int SignJobs;            // jobs the linked Welcome Sign carries (MountainJobsSystem)
        public readonly bool ForbidMultiple;
        public readonly LodgeRoadRule RoadRule;
        public readonly bool Expected;

        public PatrolDef(string prefab, string buildItemId, float radiusM, double expertFactor, double backcountryServedMin,
                         int jobs, int buildCost, int upkeep, bool forbidMultiple, LodgeRoadRule roadRule, bool expected,
                         int signJobs = 0)
        {
            SignJobs = signJobs;
            Prefab = prefab;
            BuildItemId = buildItemId;
            RadiusM = radiusM;
            ExpertFactor = expertFactor;
            BackcountryServedMin = backcountryServedMin;
            Jobs = jobs;
            BuildCost = buildCost;
            Upkeep = upkeep;
            ForbidMultiple = forbidMultiple;
            RoadRule = roadRule;
            Expected = expected;
        }
    }

    public sealed class SignDef
    {
        public readonly string Prefab;
        public readonly string BuildItemId;
        public readonly int BuildCost;          // informational mirror of PlaceableObject.m_ConstructionCost
        public readonly bool Expected;

        public SignDef(string prefab, string buildItemId, int buildCost, bool expected)
        {
            Prefab = prefab;
            BuildItemId = buildItemId;
            BuildCost = buildCost;
            Expected = expected;
        }
    }

    /// <summary>Names only. Kind = the append-only RunKind. Every run is expected.</summary>
    public sealed class RunDef
    {
        public readonly RunKind Kind;
        public readonly string Prefab;
        public readonly string BuildItemId;

        public RunDef(RunKind kind, string prefab, string buildItemId)
        {
            Kind = kind;
            Prefab = prefab;
            BuildItemId = buildItemId;
        }
    }

    /// <summary>One row of the ONE build-item table: the hub strip's item, in strip order.</summary>
    public readonly struct BuildItemDef
    {
        public readonly string Id;            // bindings.ts BuildItemId
        public readonly string Prefab;        // a NAMES entry
        public readonly BuildItemKind Kind;
        public readonly string LiftTypeId;    // "" unless Station / Line
        public readonly sbyte RunKind;        // (sbyte)RunKind for Run items, −1 otherwise

        public BuildItemDef(string id, string prefab, BuildItemKind kind, string liftTypeId, int runKind)
        {
            Id = id;
            Prefab = prefab;
            Kind = kind;
            LiftTypeId = liftTypeId ?? "";
            RunKind = (sbyte)runKind;
        }

        /// <summary>Lodges, school and patrol huts are ServiceUpgrades of the sign: launched through the upgrade path.</summary>
        public bool IsUpgrade => Kind == BuildItemKind.Lodge || Kind == BuildItemKind.School || Kind == BuildItemKind.Patrol;

        public override string ToString()
        {
            return Id + " = " + Prefab + " (" + Kind + (!string.IsNullOrEmpty(LiftTypeId) ? ", " + LiftTypeId : "") +
                   (RunKind >= 0 ? ", run kind " + RunKind : "") + ")";
        }
    }

    public static class SkiCatalog
    {
        // PrefabID type names. Pure file: no Game reference — SkiPrefabCatalogSystem.OnCreate asserts each equals
        // nameof(PowerLinePrefab) / nameof(StaticObjectPrefab) / nameof(BuildingPrefab) / nameof(SurfacePrefab).
        public const string TypeLine = "PowerLinePrefab";
        public const string TypeCarrier = "StaticObjectPrefab";
        public const string TypeBuilding = "BuildingPrefab";
        public const string TypeSurface = "SurfacePrefab";


        /// <summary>Every prefab name, once. tools/preflight.ps1 (NAMES) and tests/LiftBatch CatalogTests (CT-8, CT-13) parse the lines
        /// between NAMES-BEGIN and NAMES-END: exactly `public const string X = "Name"; // Type[, item id][, pending]`.</summary>
        public static class Names
        {
            // NAMES-BEGIN
            public const string LineFixedGrip = "SkiChairliftHV"; // PowerLinePrefab, item lift
            public const string CarrierFixedGrip = "SkiLiftChair4"; // StaticObjectPrefab
            public const string LineDetachable = "SkiDetachableHV"; // PowerLinePrefab, item liftDetachable
            public const string CarrierDetachable = "SkiLiftChairDQ4"; // StaticObjectPrefab
            public const string LineGondola = "SkiGondolaHV"; // PowerLinePrefab, item liftGondola
            public const string CarrierGondola = "SkiGondolaCabin8"; // StaticObjectPrefab
            public const string LineTram = "SkiTramHV"; // PowerLinePrefab, item liftTram
            public const string CarrierTram = "SkiTramCabin"; // StaticObjectPrefab
            public const string Marker = "SkiLiftMarker"; // PowerLinePrefab
            public const string StationFixedGripBase = "SkiBaseStationBuilding"; // BuildingPrefab, item baseStation
            public const string StationFixedGripTop = "SkiTopStationBuilding"; // BuildingPrefab, item topStation
            public const string StationDetachableBase = "SkiDQBaseStationBuilding"; // BuildingPrefab, item baseStationDetachable
            public const string StationDetachableTop = "SkiDQTopStationBuilding"; // BuildingPrefab, item topStationDetachable
            public const string StationGondolaBase = "SkiGondolaBaseStationBuilding"; // BuildingPrefab, item baseStationGondola
            public const string StationGondolaTop = "SkiGondolaTopStationBuilding"; // BuildingPrefab, item topStationGondola
            public const string StationTramBase = "SkiTramBaseStationBuilding"; // BuildingPrefab, item baseStationTram
            public const string StationTramTop = "SkiTramTopStationBuilding"; // BuildingPrefab, item topStationTram
            public const string LodgeAFrame = "SkiAFrameDayLodge"; // BuildingPrefab, item lodge
            public const string LodgeHut = "SkiLodgeHut"; // BuildingPrefab, item lodgeHut
            public const string LodgeMid = "SkiLodgeMid"; // BuildingPrefab, item lodgeMid
            public const string LodgeSummit = "SkiLodgeSummit"; // BuildingPrefab, item lodgeSummit
            public const string LodgeGrand = "SkiLodgeGrand"; // BuildingPrefab, item lodgeGrand
            public const string School = "SkiSchool"; // BuildingPrefab, item school
            public const string PatrolHut = "SkiPatrolHut"; // BuildingPrefab, item patrolHut
            public const string PatrolHQ = "SkiPatrolHQ"; // BuildingPrefab, item patrolHQ
            public const string Sign = "SkiResortGate"; // BuildingPrefab, item sign
            public const string RunNatural = "SkiRunNaturalA"; // SurfacePrefab, item runNatural
            public const string RunGroomed = "SkiRunGroomedA"; // SurfacePrefab, item runGroomed
            public const string RunSnowmaking = "SkiRunSnowmakingA"; // SurfacePrefab, item runSnowmaking
            public const string RunNight = "SkiRunNightA"; // SurfacePrefab, item runNight
            public const string RunCatTrack = "SkiRunCatTrackA"; // SurfacePrefab, item runCatTrack
            // NAMES-END
        }

        /// <summary>The fixed-grip triple's numbers as consts: the Burst-callable rule files alias these, Lifts[0] is built from them
        /// (ResortBatch CG-1: alias == row == const). Per-row numbers of the other types reach Burst jobs as struct fields filled on the
        /// main thread.</summary>
        public static class FixedGrip
        {
            public const string Id = "fixedGrip";
            public const int SeatsPerCarrier = 3;          // the triple (lift_chair.py --seats default 3)
            public const float SpeedMps = 2.5f;
            public const float AimHeightM = 8.0f;          // tower head: axle 7.04, comm 8.15
            public const float ServeRadiusM = 150f;
            public const float TopMarginM = 15f;
            public const float FixedPerMonth = 2500f;
            public const float PerKmPerMonth = 1000f;
            public const double ScoreValue = 6.0;
            public const string BuildItemId = "lift";
            public const bool CloseElectricityView = true;
            // HAND COPY: SkiChairliftPiece.Prefab:65 m_ConstructionCost 60 per 8 m piece × 125 pieces per km. The hub sends it as is; a piece
            // price change must change this line. Must equal LineBuildPer8m × PiecesPerKm (LiftBatch CT-5) and the piece's mirror (CT-12).
            public const int LinePerKm = 7500;
            public const float ApproachPinCeilingM = 4.0f; // = max(4.0, HangM + 1.5) per row (CT-15)

            public const float SpacingM = 8f;              // the line prefab's NetSubObjects m_Spacing: SkiChairliftHV.Prefab:117, :147
            public const float DesignSpacingM = 15f;       // ride spacing (carriers.json tr spacing_m)
            public const float HangM = 2.4f;               // lift_chair.py:77 HEIGHT_CONTRACT = carriers.json tr hang_m 2.400
            public const float GripTopAboveRopeM = 0.08f;  // lift_chair.py:78 GRIP_TOP_Z = carriers.json tr grip_top_above_rope_m
            public const float GaugeHalfM = 1.8f;          // the haul-rope lanes at x ±1.8 (carriers.json tr lane_x_m)
            public const float AxleM = 7.04f;              // carriers.json tr axle_m
            public const double SummerShare = 0.25;        // summer share (lambda)
            public const int LineBuildPer8m = 60;          // SkiChairliftPiece.Prefab:65 m_ConstructionCost
        }

        public static class AFrameLodge
        {
            public const double SkiersPerDay = 360.0;      // == Lodges[0].Capacity (ResortBatch CG-1)
        }

        /// <summary>The lodge ladder's area rules. There is NO per-area lodge count limit of any tier; ExtraCap is the only limiter.</summary>
        public static class Ladder
        {
            public const double ExtraCap = 24.0;           // LodgeBase = B(best) + min(Σ E(others), 24)
            public const float SummitServedRadiusM = 300f; // lodge xz within 300 m of a complete, powered lift's top stub tip
        }

        /// <summary>The ski school (SchoolRule aliases these; ResortBatch CG-1).</summary>
        public static class School
        {
            public const double ScoreValue = 12.0;         // +12 to S while a green run counts
            public const double DemandFactor = 1.2;        // locals' ski-day chance × 1.2
        }

        /// <summary>The ski patrol (PatrolRule aliases these; ResortBatch CG-1).</summary>
        public static class Patrol
        {
            public const float RadiusM = 1000f;            // expert runs within 1 km
            public const double ExpertFactor = 1.15;       // × 1.15
            public const double BackcountryServedMin = 0.70; // a patrolled unserved run's served factor ≥ 70 %
        }

        /// <summary>The "Lodge spend per guest" option (SkiMoneyRules aliases these).</summary>
        public static class LodgeSpend
        {
            // Default 15 / 23 / 45 per paying guest at the A-frame and hut / the chalet / the summit and grand lodges (x1 / x1.5 / x3). 0
            // turns it off.
            public const int Default = 15, Max = 50, Step = 5;
        }

        /// <summary>The approach pin ceiling is max(this, HangM + ApproachPinHangMarginM).</summary>
        public const float ApproachPinCeilingFloorM = 4.0f;
        public const float ApproachPinHangMarginM = 1.5f;
        /// <summary>8 m pieces per km: LinePerKm = LineBuildPer8m × 125.</summary>
        public const int PiecesPerKm = 125;

        /// <summary>The fixed-grip stations' HV stub tip z (SkiBaseStationBuilding.Prefab:92, SkiTopStationBuilding.Prefab:86;
        /// StationAlign aliases it). Every reader that has a station uses StationDef.StubTipLocalZ (−16 on the DQ and gondola stations).</summary>
        public const float StubTipLocalZ = -12f;

        public const string MarkerPrefab = Names.Marker;

        /// <summary>4 rows, APPEND ONLY; Lifts[0] = fixedGrip forever.</summary>
        public static readonly IReadOnlyList<LiftTypeDef> Lifts;
        /// <summary>8 rows: one Base and one Top per lift type.</summary>
        public static readonly IReadOnlyList<StationDef> Stations;
        /// <summary>5 rows, APPEND ONLY; Lodges[0] = the A-frame forever.</summary>
        public static readonly IReadOnlyList<LodgeDef> Lodges;
        public static readonly IReadOnlyList<SchoolDef> Schools;   // 1 row
        public static readonly IReadOnlyList<PatrolDef> Patrols;   // 2 rows
        public static readonly IReadOnlyList<SignDef> Signs;       // 1 row
        /// <summary>Natural 3 "runNatural", Groomed 0, Snowmaking 1, Night 2 (the NAMES-block order).</summary>
        public static readonly IReadOnlyList<RunDef> Runs;
        /// <summary>The ONE build-item table (25 rows, strip order; the BUILD-ITEMS block below).</summary>
        public static readonly IReadOnlyList<BuildItemDef> BuildItems;
        /// <summary>The static initializer's sanity findings. EMPTY when sane. The initializer NEVER throws.</summary>
        public static readonly IReadOnlyList<string> Problems;
        /// <summary>Names whose flag is Expected / pending (computed).</summary>
        public static readonly int ExpectedNameCount, PendingNameCount;

        // The ONE build-item table. The markers sit INSIDE the braces, so every line between them is a row; LiftBatch CT-9 / CT-14 and
        // check-bundle R16 parse it as text with
        // ^\s*new BuildItemDef\("(\w+)",\s*Names\.(\w+),\s*BuildItemKind\.(\w+),\s*"(\w*)",\s*(-?\d+)\),\s*$
        // Order = the strip order (the TS table mirrors it row by row). Run kinds = RunKind (EntityRef.cs:91-97: Groomed 0, Snowmaking 1,
        // Night 2, Natural 3).
        private static readonly BuildItemDef[] s_BuildItemTable =
        {
            // BUILD-ITEMS-BEGIN
            new BuildItemDef("sign", Names.Sign, BuildItemKind.Sign, "", -1),
            new BuildItemDef("lodge", Names.LodgeAFrame, BuildItemKind.Lodge, "", -1),
            new BuildItemDef("lodgeHut", Names.LodgeHut, BuildItemKind.Lodge, "", -1),
            new BuildItemDef("lodgeMid", Names.LodgeMid, BuildItemKind.Lodge, "", -1),
            new BuildItemDef("lodgeSummit", Names.LodgeSummit, BuildItemKind.Lodge, "", -1),
            new BuildItemDef("lodgeGrand", Names.LodgeGrand, BuildItemKind.Lodge, "", -1),
            new BuildItemDef("school", Names.School, BuildItemKind.School, "", -1),
            new BuildItemDef("patrolHut", Names.PatrolHut, BuildItemKind.Patrol, "", -1),
            new BuildItemDef("patrolHQ", Names.PatrolHQ, BuildItemKind.Patrol, "", -1),
            new BuildItemDef("baseStation", Names.StationFixedGripBase, BuildItemKind.Station, "fixedGrip", -1),
            new BuildItemDef("topStation", Names.StationFixedGripTop, BuildItemKind.Station, "fixedGrip", -1),
            new BuildItemDef("lift", Names.LineFixedGrip, BuildItemKind.Line, "fixedGrip", -1),
            new BuildItemDef("baseStationDetachable", Names.StationDetachableBase, BuildItemKind.Station, "detachable", -1),
            new BuildItemDef("topStationDetachable", Names.StationDetachableTop, BuildItemKind.Station, "detachable", -1),
            new BuildItemDef("liftDetachable", Names.LineDetachable, BuildItemKind.Line, "detachable", -1),
            new BuildItemDef("baseStationGondola", Names.StationGondolaBase, BuildItemKind.Station, "gondola", -1),
            new BuildItemDef("topStationGondola", Names.StationGondolaTop, BuildItemKind.Station, "gondola", -1),
            new BuildItemDef("liftGondola", Names.LineGondola, BuildItemKind.Line, "gondola", -1),
            new BuildItemDef("baseStationTram", Names.StationTramBase, BuildItemKind.Station, "tram", -1),
            new BuildItemDef("topStationTram", Names.StationTramTop, BuildItemKind.Station, "tram", -1),
            new BuildItemDef("liftTram", Names.LineTram, BuildItemKind.Line, "tram", -1),
            new BuildItemDef("runNatural", Names.RunNatural, BuildItemKind.Run, "", 3),
            new BuildItemDef("runGroomed", Names.RunGroomed, BuildItemKind.Run, "", 0),
            new BuildItemDef("runSnowmaking", Names.RunSnowmaking, BuildItemKind.Run, "", 1),
            new BuildItemDef("runNight", Names.RunNight, BuildItemKind.Run, "", 2),
            new BuildItemDef("runCatTrack", Names.RunCatTrack, BuildItemKind.Run, "", 4),
            // BUILD-ITEMS-END
        };

        // Lookups (Ordinal; filled once by the static constructor; the first entry wins on a clash, Sanity reports it)
        private static readonly Dictionary<string, LiftTypeDef> s_ByLine = new Dictionary<string, LiftTypeDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, LiftTypeDef> s_ByCarrier = new Dictionary<string, LiftTypeDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, LiftTypeDef> s_ById = new Dictionary<string, LiftTypeDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, StationDef> s_Stations = new Dictionary<string, StationDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, LodgeDef> s_Lodges = new Dictionary<string, LodgeDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, SchoolDef> s_Schools = new Dictionary<string, SchoolDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PatrolDef> s_Patrols = new Dictionary<string, PatrolDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, SignDef> s_Signs = new Dictionary<string, SignDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, RunDef> s_Runs = new Dictionary<string, RunDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, CatalogName> s_ByName = new Dictionary<string, CatalogName>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> s_ItemIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly IReadOnlyList<CatalogName> s_AllNames;
        private static readonly StationDef[] s_StationOf;   // [liftTypeIndex * 2 + (int)role]

        /// <summary>Rows first, then the name list, then the lookups and the sanity findings. Never throws.</summary>
        static SkiCatalog()
        {
            var problems = new List<string>();
            LiftTypeDef[] lifts = new LiftTypeDef[0];
            StationDef[] stations = new StationDef[0];
            LodgeDef[] lodges = new LodgeDef[0];
            SchoolDef[] schools = new SchoolDef[0];
            PatrolDef[] patrols = new PatrolDef[0];
            SignDef[] signs = new SignDef[0];
            RunDef[] runs = new RunDef[0];
            BuildItemDef[] items = s_BuildItemTable ?? new BuildItemDef[0];
            var all = new List<CatalogName>(32);
            var stationOf = new StationDef[0];
            int expectedNames = 0, pendingNames = 0;
            try
            {
                // Lift rows. Sources: rates, line cost, station costs, weights and summer share = the round spec; the triple = FixedGrip consts;
                // MaxPylon / slope / hanging from the line prefabs; HeightRange.min = the clearance formula; elevation max: FG
                // SkiChairliftHV.Prefab:19 (16), DQ 10, gondola lattice-towers 40 − 12, tram lattice-towers 30.
                var specs = new[]
                {
                    new LiftTypeSpec
                    {
                        Id = FixedGrip.Id, LinePrefab = Names.LineFixedGrip, PiecePrefab = "SkiChairliftPiece",
                        LineItemId = FixedGrip.BuildItemId, CarrierPrefabs = new[] { Names.CarrierFixedGrip },
                        Mover = MoverKind.Loop, SeatsPerCarrier = FixedGrip.SeatsPerCarrier, SpeedMps = FixedGrip.SpeedMps,
                        GaugeHalfM = FixedGrip.GaugeHalfM, HangM = FixedGrip.HangM, GripTopAboveRopeM = FixedGrip.GripTopAboveRopeM,
                        AxleM = FixedGrip.AxleM, AimHeightM = FixedGrip.AimHeightM, SpacingM = FixedGrip.SpacingM,
                        DesignSpacingM = FixedGrip.DesignSpacingM, ServeRule = ServeRuleKind.AlongLine,
                        ServeRadiusM = FixedGrip.ServeRadiusM, TopMarginM = FixedGrip.TopMarginM,
                        FixedPerMonth = FixedGrip.FixedPerMonth, PerKmPerMonth = FixedGrip.PerKmPerMonth,
                        LineBuildPer8m = FixedGrip.LineBuildPer8m,
                        ElevationCostPer10m = 5,                       // SkiChairliftPiece.Prefab:66 m_ElevationCost
                        StationCostBase = 60000, StationCostTop = 30000,   // SkiBase/TopStationBuilding.Prefab PlaceableObject (CT-12)
                        ScoreValue = FixedGrip.ScoreValue, SummerShare = FixedGrip.SummerShare,
                        // SkiChairliftHV.Prefab:292 / :288 / :293, PlaceableNet :19; SkiLiftHaulRopeB 0.03; SkiChairliftPiece.Prefab:133. Elevation max 16
                        // equals SkiChairliftHV.Prefab:19 m_ElevationRange.max (CT-12): the FG ladder grounds exactly to 16, so 0 m of margin, and FG24
                        // stays unneeded only while ≤ 16.
                        Envelope = new LiftEnvelope(60f, 0.8f, 0.03f, 0.03f, 6f, 16f),
                        CloseElectricityView = FixedGrip.CloseElectricityView, LineExpected = true,
                    },
                    new LiftTypeSpec
                    {
                        Id = "detachable", LinePrefab = Names.LineDetachable, PiecePrefab = "SkiDetachablePiece",
                        LineItemId = "liftDetachable", CarrierPrefabs = new[] { Names.CarrierDetachable },
                        Mover = MoverKind.Loop, SeatsPerCarrier = 4, SpeedMps = 5.0f,     // carriers.json dq seats 4
                        GaugeHalfM = 2.6f, HangM = 3.3f, GripTopAboveRopeM = 0.22f,      // carriers.json dq
                        AxleM = 8.0f,                                                   // SkiLiftTowerDQ10O.manifest.json axle
                        AimHeightM = 9.0f,                                              // SkiLiftTowerDQ10O.manifest.json catalog_handoff.AimHeightM
                        SpacingM = 22.5f, DesignSpacingM = 30f,                         // rides ≈ 31 m; design 30 m
                        ServeRule = ServeRuleKind.AlongLine, ServeRadiusM = 150f, TopMarginM = 15f,
                        FixedPerMonth = 5000f, PerKmPerMonth = 2000f, LineBuildPer8m = 120, ElevationCostPer10m = 10,
                        StationCostBase = 125000, StationCostTop = 65000, ScoreValue = 15.0, SummerShare = 0.25,
                        Envelope = new LiftEnvelope(90f, 0.9f, 0f, 0.004f, 5.8f, 10f),
                        CloseElectricityView = true, LineExpected = true,
                        // SkiDetachableHV and SkiLiftChairDQ4 exist game-side: line and carrier Expected, the row derived-Expected.
                        CarrierExpected = new[] { true },
                    },
                    new LiftTypeSpec
                    {
                        Id = "gondola", LinePrefab = Names.LineGondola, PiecePrefab = "SkiGondolaPiece",
                        LineItemId = "liftGondola", CarrierPrefabs = new[] { Names.CarrierGondola },
                        Mover = MoverKind.Loop, SeatsPerCarrier = 8, SpeedMps = 5.5f,     // carriers.json gd8
                        GaugeHalfM = 3.0f, HangM = 4.5f, GripTopAboveRopeM = 0.08f,      // carriers.json gd8 (cabin lens)
                        AxleM = 10.0f,
                        AimHeightM = 11.0f,                                             // axle 10 / comm 11.2
                        SpacingM = 40f, DesignSpacingM = 60f,                           // rides ≈ 60 m; design 60 m. SpacingM mirrors SkiGondolaHV m_Spacing (140 m edges spawn 3 cabins, not 2)
                        ServeRule = ServeRuleKind.AlongLine, ServeRadiusM = 150f, TopMarginM = 15f,
                        FixedPerMonth = 8000f, PerKmPerMonth = 3000f, LineBuildPer8m = 175, ElevationCostPer10m = 15,
                        StationCostBase = 190000, StationCostTop = 110000, ScoreValue = 25.0, SummerShare = 0.25,
                        Envelope = new LiftEnvelope(250f, 1.2f, 0f, 0.004f, 6f, 28f),
                        CloseElectricityView = true, LineExpected = true,
                        // SkiGondolaHV and SkiGondolaCabin8 exist game-side: line and carrier Expected, the row derived-Expected.
                        CarrierExpected = new[] { true },
                    },
                    new LiftTypeSpec
                    {
                        Id = "tram", LinePrefab = Names.LineTram, PiecePrefab = "SkiTramPiece",
                        LineItemId = "liftTram", CarrierPrefabs = new[] { Names.CarrierTram },
                        Mover = MoverKind.Shuttle, SeatsPerCarrier = 100, SpeedMps = 10.0f,  // cabins.md §8.1
                        GaugeHalfM = 5.5f,                                              // the track ropes = the HV lanes
                        HangM = 10.5f, GripTopAboveRopeM = 0.5f,                        // carriers.json tram (cabin lens)
                        AxleM = 26.25f,                                                 // SkiTramTower30A.manifest.json stubs y
                        AimHeightM = 26.25f,                                            // lattice-towers §15.1
                        SpacingM = 0f, DesignSpacingM = 0f,                             // 2 cabins hosted on the base station
                        ServeRule = ServeRuleKind.TerminalsOnly, ServeRadiusM = 300f, TopMarginM = 15f,
                        FixedPerMonth = 11000f, PerKmPerMonth = 4500f, LineBuildPer8m = 270, ElevationCostPer10m = 25,
                        StationCostBase = 300000, StationCostTop = 190000, ScoreValue = 30.0, SummerShare = 1.0,
                        Envelope = new LiftEnvelope(1000f, 2.5f, 0f, 0.004f, 13.75f, 30f),
                        CloseElectricityView = true, LineExpected = true,
                        // SkiTramCabin and SkiTramHV exist game-side: both names Expected, the row derived-Expected.
                        CarrierExpected = new[] { true },
                        // The shuttle profile. Accel 0.8 m/s² (real jig-back trams ≈ 0.5-1.0, unverified); dwell 60 s (carriers.json tram dwell_s 60, CT-11).
                        AccelMps2 = 0.8f, DwellS = 60f,
                    },
                };
                lifts = new LiftTypeDef[specs.Length];
                for (int i = 0; i < specs.Length; i++)
                    lifts[i] = new LiftTypeDef(i, specs[i]);

                // Station rows. Wheel = bullwheel center at rope height, prefab-local (x, y = rope, z); NaN = no loop (Shuttle). FG:
                // lift_terminal.py drive (0.2, 3.5) + 0.05 / return 3.7 at the origin, authoring (x, y, z) → (-y, z, x) in the game frame. DQ:
                // SkiDQ{Base,Top}StationA.manifest.json contract.wheel_local_prefab / stub_tip_local_z (the rope at the GRIP-TOP lane). Gondola:
                // SkiGondola{Base,Top}StationA.manifest.json contract.wheel_local_prefab / stub_tip_local_z. Tram:
                // SkiTram{Base,Top}StationA.manifest.json prefab_contract.stub_tip_z / docks / axis_x; the tram base row is in the GAME frame
                // (docks 9.50 / −1.50, axis +4.00, the manifest x negated). BuildCost = the type's StationCost{Base,Top} (Sanity checks it).
                float nan = float.NaN;
                stations = new[]
                {
                    // Per-row stub tip. The DQ / gondola / tram rows are Expected (prefabs game-side and mirrored; CT-12 costs 125,000 / 65,000 /
                    // 190,000 / 110,000 / 300,000 / 190,000 equal the mirror).
                    Station(lifts, Names.StationFixedGripBase, StationRole.Base, FixedGrip.Id, new float3(0f, 3.55f, 0.2f), StubTipLocalZ, 60000, "baseStation", true),
                    Station(lifts, Names.StationFixedGripTop, StationRole.Top, FixedGrip.Id, new float3(0f, 3.75f, 0f), StubTipLocalZ, 30000, "topStation", true),
                    Station(lifts, Names.StationDetachableBase, StationRole.Base, "detachable", new float3(0f, 3.70f, 9.60f), -16f, 125000, "baseStationDetachable", true),
                    Station(lifts, Names.StationDetachableTop, StationRole.Top, "detachable", new float3(0f, 3.70f, 5.60f), -16f, 65000, "topStationDetachable", true),
                    Station(lifts, Names.StationGondolaBase, StationRole.Base, "gondola", new float3(0f, 4.85f, 9.40f), -16f, 190000, "baseStationGondola", true),
                    Station(lifts, Names.StationGondolaTop, StationRole.Top, "gondola", new float3(0f, 4.85f, 5.40f), -16f, 110000, "topStationGondola", true),
                    // The tram's two docks at the track rope (SkiTram{Base,Top}StationA.manifest.json prefab_contract.docks[0..1].rope; levels
                    // identical on base and top); the base hosts the cabins. The base's lot is 6 x 3 (the new cell on the clock-tower side), so its
                    // building sits 4 m off the origin: in the game frame the line axis is at prefab x +4.00 and the docks A / B are (9.50 / -1.50) =
                    // hosted cabin entries 0 / 1 (A = the +X bay, B = the -X bay); the top is unchanged.
                    Station(lifts, Names.StationTramBase, StationRole.Base, "tram", new float3(nan, nan, nan), -12f, 300000, "baseStationTram", true,
                            new float3(9.50f, 16.00f, -5.50f), new float3(-1.50f, 16.00f, -5.50f), true, 4.00f),
                    Station(lifts, Names.StationTramTop, StationRole.Top, "tram", new float3(nan, nan, nan), -12f, 190000, "topStationTram", true,
                            new float3(-5.50f, 16.00f, -5.50f), new float3(5.50f, 16.00f, -5.50f), false),
                };

                // Lodge ladder. Jobs / BuildCost / Upkeep are informational (the prefab is the truth; CT-12 compares an expected row). Every row is
                // top-level (BuildCost = PlaceableObject.m_ConstructionCost); the hut, the summit lodge and both patrol huts demand nothing:
                // jobs 0 = no Workplace. Their jobs (hut 4, summit 8, patrol 2 + 2) are signJobs, carried by the linked Welcome Sign's
                // WorkProvider (MountainJobsSystem; staff ride up).
                lodges = new[]
                {
                    // The A-frame = SkiAFrameDayLodge.Prefab (m_UpgradeCost 40000, m_Upkeep 3000, m_Workplaces 4, ForbidMultiple false). Jobs 4
                    // (vanilla leisure parks of attraction 30-40 carry 2-10 jobs).
                    new LodgeDef(0, "aFrame", Names.LodgeAFrame, "lodge", b: 20, e: 4, mu: 0.5, muSummit: 0.5, capacity: 360,
                                 priceTolerance: 0, jobs: 4, buildCost: 40000, upkeep: 3000, forbidMultiple: false,
                                 luxury: false, spendMultiplier: 1.0f, roadRule: LodgeRoadRule.Base, expected: true),
                    new LodgeDef(1, "hut", Names.LodgeHut, "lodgeHut", b: 16, e: 4, mu: 0.5, muSummit: 0.75, capacity: 200,
                                 priceTolerance: 0, jobs: 0, buildCost: 50000, upkeep: 3000, forbidMultiple: false,
                                 luxury: false, spendMultiplier: 1.0f, roadRule: LodgeRoadRule.Mountain, expected: true, signJobs: 4),
                    new LodgeDef(2, "mid", Names.LodgeMid, "lodgeMid", b: 30, e: 8, mu: 0.5, muSummit: 0.5, capacity: 720,
                                 priceTolerance: 20, jobs: 12, buildCost: 200000, upkeep: 8000, forbidMultiple: false,
                                 luxury: false, spendMultiplier: 1.5f, roadRule: LodgeRoadRule.Base, expected: true),
                    new LodgeDef(3, "summit", Names.LodgeSummit, "lodgeSummit", b: 40, e: 12, mu: 0.5, muSummit: 1.0, capacity: 480,
                                 priceTolerance: 40, jobs: 0, buildCost: 1500000, upkeep: 18000, forbidMultiple: false,
                                 luxury: true, spendMultiplier: 3.0f, roadRule: LodgeRoadRule.Mountain, expected: true, signJobs: 8),
                    new LodgeDef(4, "grand", Names.LodgeGrand, "lodgeGrand", b: 40, e: 12, mu: 0.6, muSummit: 0.6, capacity: 1080,
                                 priceTolerance: 40, jobs: 18, buildCost: 1200000, upkeep: 20000, forbidMultiple: false,
                                 luxury: true, spendMultiplier: 3.0f, roadRule: LodgeRoadRule.Base, expected: true),
                };

                // School and patrol (the school is a Base road row, patrol huts stay roadless and are not written)
                schools = new[]
                {
                    // SkiSchool.Prefab: jobs 4, upkeep 3,000
                    new SchoolDef(Names.School, "school", School.ScoreValue, School.DemandFactor, jobs: 4, buildCost: 80000,
                                  upkeep: 3000, forbidMultiple: true, roadRule: LodgeRoadRule.Base, expected: true),
                };
                patrols = new[]
                {
                    new PatrolDef(Names.PatrolHut, "patrolHut", Patrol.RadiusM, Patrol.ExpertFactor, Patrol.BackcountryServedMin,
                                  jobs: 0, buildCost: 15000, upkeep: 1500, forbidMultiple: false, roadRule: LodgeRoadRule.None,
                                  expected: true, signJobs: 2),
                    new PatrolDef(Names.PatrolHQ, "patrolHQ", Patrol.RadiusM, Patrol.ExpertFactor, Patrol.BackcountryServedMin,
                                  jobs: 0, buildCost: 15000, upkeep: 1500, forbidMultiple: false, roadRule: LodgeRoadRule.None,
                                  expected: true, signJobs: 2),
                };

                // Sign: SkiResortGate.Prefab:25 PlaceableObject m_ConstructionCost must equal this 12000 (LiftBatch CT-12 fails on any difference,
                // and the hub writes one "[items] drift" WARN per load while the two disagree).
                signs = new[] { new SignDef(Names.Sign, "sign", 12000, true) };

                runs = new[]
                {
                    new RunDef(RunKind.Natural, Names.RunNatural, "runNatural"),
                    new RunDef(RunKind.Groomed, Names.RunGroomed, "runGroomed"),
                    new RunDef(RunKind.Snowmaking, Names.RunSnowmaking, "runSnowmaking"),
                    new RunDef(RunKind.Night, Names.RunNight, "runNight"),
                    new RunDef(RunKind.CatTrack, Names.RunCatTrack, "runCatTrack"),
                };

                all = NameList(lifts, MarkerPrefab, stations, lodges, schools, patrols, signs, runs);
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Expected)
                        expectedNames++;
                    else
                        pendingNames++;
                }

                for (int i = 0; i < all.Count; i++)
                    AddFirst(s_ByName, all[i].Name, all[i]);
                for (int i = 0; i < lifts.Length; i++)
                {
                    LiftTypeDef l = lifts[i];
                    AddFirst(s_ById, l.Id, l);
                    AddFirst(s_ByLine, l.LinePrefab, l);
                    for (int c = 0; c < l.CarrierPrefabs.Count; c++)
                        AddFirst(s_ByCarrier, l.CarrierPrefabs[c], l);
                }
                stationOf = new StationDef[lifts.Length * 2];
                for (int i = 0; i < stations.Length; i++)
                {
                    StationDef s = stations[i];
                    AddFirst(s_Stations, s.Prefab, s);
                    int slot = s.LiftTypeIndex * 2 + (int)s.Role;
                    if (s.LiftTypeIndex >= 0 && s.LiftTypeIndex < lifts.Length && (int)s.Role < 2 && stationOf[slot] == null)
                        stationOf[slot] = s;
                }
                for (int i = 0; i < lodges.Length; i++)
                    AddFirst(s_Lodges, lodges[i].Prefab, lodges[i]);
                for (int i = 0; i < schools.Length; i++)
                    AddFirst(s_Schools, schools[i].Prefab, schools[i]);
                for (int i = 0; i < patrols.Length; i++)
                    AddFirst(s_Patrols, patrols[i].Prefab, patrols[i]);
                for (int i = 0; i < signs.Length; i++)
                    AddFirst(s_Signs, signs[i].Prefab, signs[i]);
                for (int i = 0; i < runs.Length; i++)
                    AddFirst(s_Runs, runs[i].Prefab, runs[i]);
                for (int i = 0; i < items.Length; i++)
                {
                    if (!string.IsNullOrEmpty(items[i].Id) && !s_ItemIndex.ContainsKey(items[i].Id))
                        s_ItemIndex.Add(items[i].Id, i);
                }

                // Sanity: the generic rules (shared with LiftBatch CT-16's injected-fault twin)
                CheckRows(problems, lifts, stations, lodges, schools, patrols, signs, runs, items, all);

                // Sanity: this catalog's frozen facts (APPEND ONLY rows)
                if (lifts.Length == 0 || lifts[0].Id != FixedGrip.Id)
                    problems.Add("Lifts[0] is not '" + FixedGrip.Id + "' (APPEND ONLY: row 0 is the fixed-grip triple forever)");
                if (lodges.Length == 0 || lodges[0].Prefab != Names.LodgeAFrame)
                    problems.Add("Lodges[0] is not '" + Names.LodgeAFrame + "' (APPEND ONLY: row 0 is the A-frame forever)");
                if (lodges.Length > 0 && lodges[0].Capacity != (int)AFrameLodge.SkiersPerDay)
                    problems.Add("Lodges[0].Capacity " + lodges[0].Capacity + " != AFrameLodge.SkiersPerDay " + AFrameLodge.SkiersPerDay);
            }
            catch (Exception e)
            {
                problems.Add("the static initialiser threw " + e.GetType().Name + ": " + e.Message + " — the catalog is incomplete");
            }

            Lifts = Array.AsReadOnly(lifts);
            Stations = Array.AsReadOnly(stations);
            Lodges = Array.AsReadOnly(lodges);
            Schools = Array.AsReadOnly(schools);
            Patrols = Array.AsReadOnly(patrols);
            Signs = Array.AsReadOnly(signs);
            Runs = Array.AsReadOnly(runs);
            BuildItems = Array.AsReadOnly(items);
            s_AllNames = Array.AsReadOnly(all.ToArray());
            s_StationOf = stationOf;
            ExpectedNameCount = expectedNames;
            PendingNameCount = pendingNames;
            Problems = Array.AsReadOnly(problems.ToArray());
        }

        private static StationDef Station(LiftTypeDef[] lifts, string prefab, StationRole role, string liftTypeId, float3 wheel,
                                          float stubTipLocalZ, int buildCost, string item, bool expected)
        {
            return Station(lifts, prefab, role, liftTypeId, wheel, stubTipLocalZ, buildCost, item, expected,
                           new float3(float.NaN), new float3(float.NaN), false);
        }

        /// <summary>A row with docks and the carrier-host flag, and the line axis x (StationDef.StubTipLocalX, 0 unless given).</summary>
        private static StationDef Station(LiftTypeDef[] lifts, string prefab, StationRole role, string liftTypeId, float3 wheel,
                                          float stubTipLocalZ, int buildCost, string item, bool expected,
                                          float3 dockLocalA, float3 dockLocalB, bool hostsCarriers, float stubTipLocalX = 0f)
        {
            int index = -1;
            for (int i = 0; i < lifts.Length; i++)
            {
                if (lifts[i].Id == liftTypeId)
                {
                    index = i;
                    break;
                }
            }
            return new StationDef(prefab, role, liftTypeId, wheel, stubTipLocalZ, buildCost, item, expected, index,
                                  dockLocalA, dockLocalB, hostsCarriers, stubTipLocalX);
        }

        private static void AddFirst<T>(Dictionary<string, T> map, string key, T value)
        {
            if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                map.Add(key, value);
        }

        /// <summary>The name list in NAMES-block order: each lift type's line then its carriers, the marker, the stations,
        /// the lodges, the school, the patrol huts (School and Patrol rows sit between the lodges and the sign), the sign,
        /// the runs. Internal: LiftBatch CT-16 builds its twin's names with it.</summary>
        internal static List<CatalogName> NameList(IReadOnlyList<LiftTypeDef> lifts, string marker, IReadOnlyList<StationDef> stations,
                                                   IReadOnlyList<LodgeDef> lodges, IReadOnlyList<SchoolDef> schools,
                                                   IReadOnlyList<PatrolDef> patrols, IReadOnlyList<SignDef> signs,
                                                   IReadOnlyList<RunDef> runs)
        {
            var all = new List<CatalogName>(32);
            for (int i = 0; i < lifts.Count; i++)
            {
                LiftTypeDef l = lifts[i];
                all.Add(new CatalogName(l.LinePrefab, TypeLine, CatalogKind.Line, l.BuildItemId, l.LineExpected));
                for (int c = 0; c < l.Carriers.Count; c++)
                    all.Add(new CatalogName(l.Carriers[c].Prefab, TypeCarrier, CatalogKind.Carrier, null, l.Carriers[c].Expected));
            }
            if (marker != null)
                all.Add(new CatalogName(marker, TypeLine, CatalogKind.Marker, null, true));
            for (int i = 0; i < stations.Count; i++)
                all.Add(new CatalogName(stations[i].Prefab, TypeBuilding, CatalogKind.Station, stations[i].BuildItemId, stations[i].Expected));
            for (int i = 0; i < lodges.Count; i++)
                all.Add(new CatalogName(lodges[i].Prefab, TypeBuilding, CatalogKind.Lodge, lodges[i].BuildItemId, lodges[i].Expected));
            for (int i = 0; i < schools.Count; i++)
                all.Add(new CatalogName(schools[i].Prefab, TypeBuilding, CatalogKind.School, schools[i].BuildItemId, schools[i].Expected));
            for (int i = 0; i < patrols.Count; i++)
                all.Add(new CatalogName(patrols[i].Prefab, TypeBuilding, CatalogKind.Patrol, patrols[i].BuildItemId, patrols[i].Expected));
            for (int i = 0; i < signs.Count; i++)
                all.Add(new CatalogName(signs[i].Prefab, TypeBuilding, CatalogKind.Sign, signs[i].BuildItemId, signs[i].Expected));
            for (int i = 0; i < runs.Count; i++)
                all.Add(new CatalogName(runs[i].Prefab, TypeSurface, CatalogKind.Run, runs[i].BuildItemId, true));
            return all;
        }

        /// <summary>The generic sanity rules over any row set (the static constructor's, or LiftBatch CT-16's hand-made twin). Appends
        /// one finding per broken rule instance; never throws on null entries it can skip.</summary>
        internal static void CheckRows(List<string> problems, IReadOnlyList<LiftTypeDef> lifts, IReadOnlyList<StationDef> stations,
                                       IReadOnlyList<LodgeDef> lodges, IReadOnlyList<SchoolDef> schools,
                                       IReadOnlyList<PatrolDef> patrols, IReadOnlyList<SignDef> signs, IReadOnlyList<RunDef> runs,
                                       IReadOnlyList<BuildItemDef> items, IReadOnlyList<CatalogName> names)
        {
            // Names: non-empty, unique; name item ids unique
            var byName = new Dictionary<string, CatalogName>(StringComparer.Ordinal);
            var nameItems = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                CatalogName n = names[i];
                if (string.IsNullOrEmpty(n.Name))
                {
                    problems.Add("entry " + i + " (" + n.Kind + ") has an empty prefab name");
                    continue;
                }
                if (byName.ContainsKey(n.Name))
                    problems.Add("duplicate prefab name '" + n.Name + "' (" + byName[n.Name].Kind + " and " + n.Kind + ")");
                else
                    byName.Add(n.Name, n);
                if (n.BuildItemId != null)
                {
                    if (nameItems.ContainsKey(n.BuildItemId))
                        problems.Add("build item id '" + n.BuildItemId + "' on two names ('" + nameItems[n.BuildItemId] + "' and '" + n.Name + "')");
                    else
                        nameItems.Add(n.BuildItemId, n.Name);
                }
            }

            var typeIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var lineOwner = new Dictionary<string, string>(StringComparer.Ordinal);
            var carrierOwner = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < lifts.Count; i++)
            {
                LiftTypeDef l = lifts[i];
                if (l == null)
                {
                    problems.Add("lift type " + i + " is null");
                    continue;
                }
                string at = "lift type '" + l.Id + "'";
                if (l.Index != i)
                    problems.Add(at + " has Index " + l.Index + " at position " + i + " (APPEND ONLY)");
                if (string.IsNullOrEmpty(l.Id))
                    problems.Add("lift type " + i + " has an empty Id");
                else if (typeIndex.ContainsKey(l.Id))
                    problems.Add("duplicate lift type Id '" + l.Id + "'");
                else
                    typeIndex.Add(l.Id, i);
                if (string.IsNullOrEmpty(l.LinePrefab))
                    problems.Add(at + " has no line prefab");
                else if (lineOwner.ContainsKey(l.LinePrefab))
                    problems.Add("line prefab '" + l.LinePrefab + "' in two lift types ('" + lineOwner[l.LinePrefab] + "' and '" + l.Id + "')");
                else
                    lineOwner.Add(l.LinePrefab, l.Id);
                if (string.IsNullOrEmpty(l.BuildItemId))
                    problems.Add(at + " has no line build item id");
                if (l.CarrierPrefabs.Count == 0)
                    problems.Add(at + " has no carrier prefab");
                for (int c = 0; c < l.CarrierPrefabs.Count; c++)
                {
                    string carrier = l.CarrierPrefabs[c];
                    if (string.IsNullOrEmpty(carrier))
                        continue;   // reported with the names as an empty prefab name
                    if (carrierOwner.ContainsKey(carrier))
                        problems.Add("carrier prefab '" + carrier + "' in two lift types ('" + carrierOwner[carrier] + "' and '" + l.Id + "')");
                    else
                        carrierOwner.Add(carrier, l.Id);
                }
                if (l.Mover == MoverKind.Loop)
                {
                    if (!(l.SpacingM > 0f))
                        problems.Add("Loop " + at + " has SpacingM " + l.SpacingM + " (the line prefab's m_Spacing must be > 0)");
                    if (!(l.SpeedMps > 0f))
                        problems.Add("Loop " + at + " has SpeedMps " + l.SpeedMps);
                }
                else if (l.SpacingM != 0f)
                {
                    problems.Add("Shuttle " + at + " has SpacingM " + l.SpacingM + " (its cabins are hosted, not spawned along the line: 0)");
                }
                // The shuttle profile's numbers (ShuttleProfile.Valid), and none on a Loop
                if (l.Mover == MoverKind.Shuttle)
                {
                    if (!(l.SpeedMps > 0f) || float.IsInfinity(l.SpeedMps))
                        problems.Add("Shuttle " + at + " has SpeedMps " + l.SpeedMps + " (want finite > 0)");
                    if (!(l.AccelMps2 > 0f) || float.IsInfinity(l.AccelMps2))
                        problems.Add("Shuttle " + at + " has AccelMps2 " + l.AccelMps2 + " (want finite > 0: the trapezoid's ramps)");
                    if (!(l.DwellS >= 0f) || float.IsInfinity(l.DwellS))
                        problems.Add("Shuttle " + at + " has DwellS " + l.DwellS + " (want finite ≥ 0)");
                }
                else if (l.AccelMps2 != 0f || l.DwellS != 0f)
                {
                    problems.Add("Loop " + at + " has AccelMps2 " + l.AccelMps2 + " / DwellS " + l.DwellS + " (want 0 / 0: a loop runs at constant speed)");
                }
                if (!(l.HangM > 0f))
                    problems.Add(at + " has HangM " + l.HangM);
                if (!(l.GaugeHalfM > 0f))
                    problems.Add(at + " has GaugeHalfM " + l.GaugeHalfM);
                if (l.SeatsPerCarrier <= 0)
                    problems.Add(at + " has SeatsPerCarrier " + l.SeatsPerCarrier);
                if (!(l.SummerShare > 0.0 && l.SummerShare <= 1.0))
                    problems.Add(at + " has SummerShare " + l.SummerShare + " (want 0 < λ ≤ 1)");
            }

            // Stations: a known type, the index matching it, one Base and one Top per type, wheels by mover, cost
            var bases = new int[lifts.Count];
            var tops = new int[lifts.Count];
            var hostBases = new int[lifts.Count];   // Base stations that host the type's carriers
            var stationNames = new Dictionary<string, StationDef>(StringComparer.Ordinal);
            for (int i = 0; i < stations.Count; i++)
            {
                StationDef s = stations[i];
                if (s == null)
                {
                    problems.Add("station " + i + " is null");
                    continue;
                }
                if (!string.IsNullOrEmpty(s.Prefab) && !stationNames.ContainsKey(s.Prefab))
                    stationNames.Add(s.Prefab, s);
                if (s.LiftTypeId == null || !typeIndex.TryGetValue(s.LiftTypeId, out int ti))
                {
                    problems.Add("station '" + s.Prefab + "' names an unknown lift type '" + s.LiftTypeId + "'");
                    continue;
                }
                if (s.LiftTypeIndex != ti)
                    problems.Add("station '" + s.Prefab + "' has LiftTypeIndex " + s.LiftTypeIndex + ", its type '" + s.LiftTypeId + "' is " + ti);
                if (s.Role == StationRole.Base)
                    bases[ti]++;
                else if (s.Role == StationRole.Top)
                    tops[ti]++;
                LiftTypeDef l = lifts[ti];
                bool finite = math.all(math.isfinite(s.WheelLocal));
                bool allNaN = float.IsNaN(s.WheelLocal.x) && float.IsNaN(s.WheelLocal.y) && float.IsNaN(s.WheelLocal.z);
                if (l.Mover == MoverKind.Loop && !finite)
                    problems.Add("station '" + s.Prefab + "' of Loop type '" + l.Id + "' has no finite WheelLocal");
                if (l.Mover == MoverKind.Shuttle && !allNaN)
                    problems.Add("station '" + s.Prefab + "' of Shuttle type '" + l.Id + "' has a WheelLocal (want NaN: no bullwheel arc)");
                // Docks and the carrier host by mover
                bool docksFinite = math.all(math.isfinite(s.DockLocalA)) && math.all(math.isfinite(s.DockLocalB));
                bool docksNaN = float.IsNaN(s.DockLocalA.x) && float.IsNaN(s.DockLocalA.y) && float.IsNaN(s.DockLocalA.z) &&
                                float.IsNaN(s.DockLocalB.x) && float.IsNaN(s.DockLocalB.y) && float.IsNaN(s.DockLocalB.z);
                if (l.Mover == MoverKind.Shuttle)
                {
                    if (!docksFinite)
                        problems.Add("station '" + s.Prefab + "' of Shuttle type '" + l.Id + "' has DockLocalA " + s.DockLocalA +
                                     " / DockLocalB " + s.DockLocalB + " (want both finite: the track-rope point at each bay)");
                    else if (!(math.abs((s.DockLocalA.x + s.DockLocalB.x) * 0.5f - s.StubTipLocalX) <= 1e-3f))
                        // the two bays sit either side of the line axis (the tram base: 9.50 / -1.50 about +4.00)
                        problems.Add("station '" + s.Prefab + "' of Shuttle type '" + l.Id + "' has its docks centred on x " +
                                     ((s.DockLocalA.x + s.DockLocalB.x) * 0.5f) + ", not on its line axis StubTipLocalX " +
                                     s.StubTipLocalX);
                    if (s.HostsCarriers && s.Role == StationRole.Base)
                        hostBases[ti]++;
                    else if (s.HostsCarriers)
                        problems.Add("station '" + s.Prefab + "' of Shuttle type '" + l.Id + "' is a " + s.Role +
                                     " station that HostsCarriers (only the Base hosts the cabins, R29)");
                }
                else
                {
                    if (!docksNaN)
                        problems.Add("station '" + s.Prefab + "' of Loop type '" + l.Id + "' has a dock (want NaN: a loop has no docks)");
                    if (s.HostsCarriers)
                        problems.Add("station '" + s.Prefab + "' of Loop type '" + l.Id + "' HostsCarriers (a loop's carriers ride its line)");
                }
                int want = s.Role == StationRole.Base ? l.StationCostBase : l.StationCostTop;
                if (s.BuildCost != want)
                    problems.Add("station '" + s.Prefab + "' BuildCost " + s.BuildCost + " != its type's StationCost" + s.Role + " " + want);
            }
            for (int i = 0; i < lifts.Count; i++)
            {
                if (lifts[i] == null)
                    continue;
                if (bases[i] != 1 || tops[i] != 1)
                    problems.Add("lift type '" + lifts[i].Id + "' has " + bases[i] + " Base and " + tops[i] +
                                 " Top station(s); the two-terminal rule (LiftCensus.cs:44-47) wants exactly one each");
                if (lifts[i].Mover == MoverKind.Shuttle && hostBases[i] != 1)
                    problems.Add("Shuttle lift type '" + lifts[i].Id + "' has " + hostBases[i] +
                                 " Base station(s) that HostsCarriers (want exactly one: its cabins are hosted, R29)");
            }

            for (int i = 0; i < lodges.Count; i++)
            {
                LodgeDef d = lodges[i];
                if (d == null)
                {
                    problems.Add("lodge " + i + " is null");
                    continue;
                }
                string at = "lodge '" + d.Prefab + "'";
                if (d.Index != i)
                    problems.Add(at + " has Index " + d.Index + " at position " + i + " (APPEND ONLY)");
                if (!(d.B >= d.E && d.E > 0.0))
                    problems.Add(at + " has B " + d.B + " / E " + d.E + " (want B ≥ E > 0)");
                if (!(d.Mu > 0.0 && d.Mu <= 1.0 && d.MuSummit >= d.Mu && d.MuSummit <= 1.0))
                    problems.Add(at + " has μ " + d.Mu + " / μ summit " + d.MuSummit + " (want 0 < μ ≤ μ summit ≤ 1)");
                if (d.Capacity <= 0)
                    problems.Add(at + " has Capacity " + d.Capacity);
                if (d.ForbidMultiple)
                    problems.Add(at + " is ForbidMultiple: no lodge tier may be (no count limit, Richard 2026-10-11; the engine would refuse a second one on the sign)");
                if (d.PriceTolerance < 0 || !(d.SpendMultiplier > 0f))
                    problems.Add(at + " has PriceTolerance " + d.PriceTolerance + " / SpendMultiplier " + d.SpendMultiplier);
            }

            if (items.Count == 0)
                problems.Add("the BUILD-ITEMS table is empty");
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            var lineItems = new int[lifts.Count];
            var baseItems = new int[lifts.Count];
            var topItems = new int[lifts.Count];
            for (int i = 0; i < items.Count; i++)
            {
                BuildItemDef it = items[i];
                string at = "build item '" + it.Id + "'";
                if (string.IsNullOrEmpty(it.Id))
                {
                    problems.Add("build item " + i + " has an empty id");
                    continue;
                }
                if (ids.ContainsKey(it.Id))
                {
                    problems.Add("duplicate build item id '" + it.Id + "' (rows " + ids[it.Id] + " and " + i + ")");
                    continue;
                }
                ids.Add(it.Id, i);
                if (it.Prefab == null || !byName.TryGetValue(it.Prefab, out CatalogName n))
                {
                    problems.Add(at + " names '" + it.Prefab + "', not a NAMES entry");
                    continue;
                }
                if (!KindMatches(it.Kind, n.Kind))
                    problems.Add(at + " is a " + it.Kind + " item but '" + it.Prefab + "' is a " + n.Kind);
                if (n.TypeName != TypeNameOf(it.Kind))
                    problems.Add(at + ": '" + it.Prefab + "' has type " + n.TypeName + ", a " + it.Kind + " item wants " + TypeNameOf(it.Kind));
                if (n.BuildItemId != it.Id)
                    problems.Add(at + ": '" + it.Prefab + "' carries item token '" + n.BuildItemId + "'");
                bool typed = it.Kind == BuildItemKind.Station || it.Kind == BuildItemKind.Line;
                string liftType = it.LiftTypeId ?? "";   // default(BuildItemDef) carries null
                if (!typed)
                {
                    if (liftType.Length != 0)
                        problems.Add(at + " is a " + it.Kind + " item with lift type '" + liftType + "' (want \"\")");
                }
                else if (!typeIndex.TryGetValue(liftType, out int ti))
                {
                    problems.Add(at + " names an unknown lift type '" + liftType + "'");
                }
                else if (it.Kind == BuildItemKind.Line)
                {
                    lineItems[ti]++;
                    if (lifts[ti].LinePrefab != it.Prefab)
                        problems.Add(at + " is lift type '" + liftType + "' but '" + it.Prefab + "' is not its line '" + lifts[ti].LinePrefab + "'");
                }
                else if (!stationNames.TryGetValue(it.Prefab, out StationDef st) || st.LiftTypeId != liftType)
                {
                    problems.Add(at + " is lift type '" + liftType + "' but '" + it.Prefab + "' is not a station of that type");
                }
                else if (st.Role == StationRole.Base)
                {
                    baseItems[ti]++;
                }
                else
                {
                    topItems[ti]++;
                }
                if (it.Kind == BuildItemKind.Run)
                {
                    RunDef run = null;
                    for (int r = 0; r < runs.Count && run == null; r++)
                    {
                        if (runs[r] != null && runs[r].Prefab == it.Prefab)
                            run = runs[r];
                    }
                    if (run == null || (int)run.Kind != it.RunKind)
                        problems.Add(at + " has run kind " + it.RunKind + ", its RunDef says " + (run != null ? ((int)run.Kind).ToString() : "none"));
                }
                else if (it.RunKind != -1)
                {
                    problems.Add(at + " is not a run item but has run kind " + it.RunKind + " (want -1)");
                }
            }
            foreach (KeyValuePair<string, string> kv in nameItems)
            {
                if (!ids.ContainsKey(kv.Key))
                    problems.Add("'" + kv.Value + "' carries item token '" + kv.Key + "' but no BUILD-ITEMS row has that id");
            }
            for (int i = 0; i < lifts.Count; i++)
            {
                if (lifts[i] == null)
                    continue;
                if (lineItems[i] != 1 || baseItems[i] != 1 || topItems[i] != 1)
                    problems.Add("lift type '" + lifts[i].Id + "' has " + baseItems[i] + " base station, " + topItems[i] +
                                 " top station and " + lineItems[i] + " line item(s) (want one each)");
            }
        }

        private static bool KindMatches(BuildItemKind item, CatalogKind name)
        {
            switch (item)
            {
                case BuildItemKind.Sign: return name == CatalogKind.Sign;
                case BuildItemKind.Lodge: return name == CatalogKind.Lodge;
                case BuildItemKind.School: return name == CatalogKind.School;
                case BuildItemKind.Patrol: return name == CatalogKind.Patrol;
                case BuildItemKind.Station: return name == CatalogKind.Station;
                case BuildItemKind.Line: return name == CatalogKind.Line;
                case BuildItemKind.Run: return name == CatalogKind.Run;
                default: return false;
            }
        }

        // Lookups (Ordinal; null / "" never match)

        /// <summary>The lift type whose LINE this is; null when not a lift line (the marker is NOT a line).</summary>
        public static LiftTypeDef ByLinePrefab(string name)
        {
            return !string.IsNullOrEmpty(name) && s_ByLine.TryGetValue(name, out LiftTypeDef def) ? def : null;
        }

        /// <summary>The type owning the carrier, or null.</summary>
        public static LiftTypeDef ByCarrierPrefab(string name)
        {
            return !string.IsNullOrEmpty(name) && s_ByCarrier.TryGetValue(name, out LiftTypeDef def) ? def : null;
        }

        public static LiftTypeDef LiftById(string id)
        {
            return !string.IsNullOrEmpty(id) && s_ById.TryGetValue(id, out LiftTypeDef def) ? def : null;
        }

        /// <summary>Lifts[index]; null when out of range (LiftCensus.NoType 255 included).</summary>
        public static LiftTypeDef LiftByIndex(int index)
        {
            return index >= 0 && index < Lifts.Count ? Lifts[index] : null;
        }

        public static StationDef StationByPrefab(string name)
        {
            return !string.IsNullOrEmpty(name) && s_Stations.TryGetValue(name, out StationDef def) ? def : null;
        }

        /// <summary>The lift type's Base or Top station row; null when out of range.</summary>
        public static StationDef StationOf(int liftTypeIndex, StationRole role)
        {
            int slot = liftTypeIndex * 2 + (int)role;
            return liftTypeIndex >= 0 && (int)role < 2 && slot < s_StationOf.Length ? s_StationOf[slot] : null;
        }

        public static LodgeDef LodgeByPrefab(string name)
        {
            return !string.IsNullOrEmpty(name) && s_Lodges.TryGetValue(name, out LodgeDef def) ? def : null;
        }

        public static bool IsLodge(string name)
        {
            return LodgeByPrefab(name) != null;
        }

        public static SchoolDef SchoolByPrefab(string name)
        {
            return !string.IsNullOrEmpty(name) && s_Schools.TryGetValue(name, out SchoolDef def) ? def : null;
        }

        public static PatrolDef PatrolByPrefab(string name)
        {
            return !string.IsNullOrEmpty(name) && s_Patrols.TryGetValue(name, out PatrolDef def) ? def : null;
        }

        /// <summary>A lodge, the school or a patrol hut: every ServiceUpgrade of the sign this catalog knows.</summary>
        public static bool IsUpgradeBuilding(string name)
        {
            return IsLodge(name) || SchoolByPrefab(name) != null || PatrolByPrefab(name) != null;
        }

        public static bool IsSign(string name)
        {
            return !string.IsNullOrEmpty(name) && s_Signs.ContainsKey(name);
        }

        public static RunDef RunByPrefab(string name)
        {
            return !string.IsNullOrEmpty(name) && s_Runs.TryGetValue(name, out RunDef def) ? def : null;
        }

        /// <summary>Any NAMES entry by prefab name (its type, kind, item and Expected flag).</summary>
        public static bool TryGetName(string name, out CatalogName entry)
        {
            if (!string.IsNullOrEmpty(name) && s_ByName.TryGetValue(name, out entry))
                return true;
            entry = default(CatalogName);
            return false;
        }

        /// <summary>The NAME's flag: false for a pending name AND for a name that is not in the catalog.</summary>
        public static bool IsExpected(string name)
        {
            return TryGetName(name, out CatalogName entry) && entry.Expected;
        }

        /// <summary>NAMES-block order. Returns the cached list (no allocation per call).</summary>
        public static IReadOnlyList<CatalogName> AllPrefabNames()
        {
            return s_AllNames;
        }

        /// <summary>The build item's prefab NAME entry; runKind = (int)RunKind for run items, −1 otherwise.</summary>
        public static bool TryGetBuildItem(string buildItemId, out CatalogName name, out int runKind)
        {
            if (TryGetBuildItemDef(buildItemId, out BuildItemDef item) && TryGetName(item.Prefab, out name))
            {
                runKind = item.RunKind;
                return true;
            }
            name = default(CatalogName);
            runKind = -1;
            return false;
        }

        public static bool TryGetBuildItemDef(string buildItemId, out BuildItemDef item)
        {
            if (!string.IsNullOrEmpty(buildItemId) && s_ItemIndex.TryGetValue(buildItemId, out int i) && i < BuildItems.Count)
            {
                item = BuildItems[i];
                return true;
            }
            item = default(BuildItemDef);
            return false;
        }

        /// <summary>"lift", "liftDetachable", …; null when out of range.</summary>
        public static string LineItemOf(int liftTypeIndex)
        {
            LiftTypeDef def = LiftByIndex(liftTypeIndex);
            return def != null ? def.BuildItemId : null;
        }

        /// <summary>"baseStation", "topStationGondola", …; null when out of range.</summary>
        public static string StationItemOf(int liftTypeIndex, StationRole role)
        {
            StationDef def = StationOf(liftTypeIndex, role);
            return def != null ? def.BuildItemId : null;
        }

        /// <summary>The PrefabID type of an item kind: Line → TypeLine, Run → TypeSurface, else TypeBuilding.</summary>
        public static string TypeNameOf(BuildItemKind kind)
        {
            switch (kind)
            {
                case BuildItemKind.Line: return TypeLine;
                case BuildItemKind.Run: return TypeSurface;
                default: return TypeBuilding;
            }
        }

        /// <summary>= SubObjectSystem.cs:1739-1748 (1.6.0f1; 1.6.2 :1746): an EvenSpacing NetSubObjects row spawns
        /// n = (int)(L_xz / max(1, m_Spacing) − 0.5f) objects per edge (:1743), in float, as the engine computes it, at
        /// (m+1)·L/(n+1) (:1748). 0 when either input is not finite and &gt; 0 (m_Spacing 0 = EvenSpacing off,
        /// NetInitializeSystem.cs:1048-1051).</summary>
        public static int EvenSpacingCount(float planLengthM, float spacingM)
        {
            if (!(planLengthM > 0f) || !(spacingM > 0f) || float.IsInfinity(planLengthM) || float.IsInfinity(spacingM))
                return 0;
            float q = planLengthM / math.max(1f, spacingM) - 0.5f;
            if (q >= 2.0e9f)
                return int.MaxValue;
            return q > 0f ? (int)q : 0;
        }

        /// <summary>dirty → Resolve; else failedThisLoad → Serve (no retry storm after an exception); else
        /// validatedFrame == frame → Serve; else Validate.</summary>
        public static CatalogAction NextAction(bool dirty, bool failedThisLoad, int validatedFrame, int frame)
        {
            if (dirty)
                return CatalogAction.Resolve;
            if (failedThisLoad)
                return CatalogAction.Serve;
            if (validatedFrame == frame)
                return CatalogAction.Serve;
            return CatalogAction.Validate;
        }
    }
}
