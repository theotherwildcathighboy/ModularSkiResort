// The Resort Planner's plan sheet: the proposed lifts of the last published plan, with their build state (Resort Planner round task R1-c,
// plan section 3.1). It outlives the run build and the planner tool. A new published plan replaces it, an explicit clear and a load empty
// it. Session only: nothing is saved. Owner: ResortLiftBuildSystem (it loads the rows from the planner tool, the build queue writes the
// states). Readers: the build queue, the plan marks outside the planner, and the hub (a later task) through Rows and Revision.
// Pure file: System, System.Collections.Generic, System.Globalization, Unity.Mathematics, the shared pure EntityRef and the pure LiftCensus
// rows (the hand-built match). tests/LiftBatch SheetTests links it.
// Engine notes: EngineNotes/StationPlaceToolSystem.md (Plan sheet)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>The build state of one planned lift. Proposed: nothing built yet. StationsPlaced: both stations stand, the line does
    /// not. Built: the line is permanent. Refused: the city could not pay, the game refused a station spot, or the line's ends could not
    /// be picked (ReasonKey says which; a station that stands is kept in BaseStation / TopStation). NeedsFitting: the lift-line tool waits
    /// for the player with both ends picked (the plan is not sound, a refusal stands, or the game refused the line).</summary>
    public enum SheetLiftState : byte { Proposed, StationsPlaced, Built, Refused, NeedsFitting }

    public sealed class ResortPlanSheet
    {
        /// <summary>One proposed lift of a plan, as the planner tool hands it over (QuickRunsToolSystem.SheetInputs).</summary>
        public struct Input
        {
            public uint Key;          // PlanLift.Key (stable per plan)
            public string Name;       // "P1", "P2" ... (the hub's and the overlay's name)
            public byte TypeIndex;    // SkiCatalog.Lifts index
            public float3 Bottom, Top;   // the two station spots (snapshot heights)
            public long Cost;         // PlanLift.Cost: both stations and the line (SkiCatalog), an estimate
            public bool On;           // the lift and its pod are switched on in the plan
        }

        /// <summary>One row. The hub reads it. Only the sheet's owner writes it.</summary>
        public sealed class Row
        {
            public uint Key { get; internal set; }
            public string Name { get; internal set; }
            public byte TypeIndex { get; internal set; }
            public float3 Bottom { get; internal set; }
            public float3 Top { get; internal set; }
            public long Cost { get; internal set; }
            /// <summary>Switched on in the plan (a "kept" lift). Build all builds only these.</summary>
            public bool On { get; internal set; }
            public SheetLiftState State { get; internal set; }
            /// <summary>Why the row is Refused or NeedsFitting, else null. Keys: MONEY, POSE, BASE[error type], TOP[error type],
            /// PREFAB, LINE[reason], STOPPED, and the lift tool's plan keys (PLAN_LOW, PLAN_STEEP, ALONG, CROSS_*, BACKWARD, GAME).</summary>
            public string ReasonKey { get; internal set; }
            /// <summary>The placed stations (EntityRef.Null when not placed). A Refused row may keep one.</summary>
            public EntityRef BaseStation { get; internal set; }
            public EntityRef TopStation { get; internal set; }
            /// <summary>Build order: 0 = the lowest bottom spot.</summary>
            public int Order { get; internal set; }
            /// <summary>A Built row whose line has towers past their sheave-train limit (task R2-l). The build queue builds a line with
            /// exactly one such tower and no other defect (LiftTowerPlan.QueueRule BuildFlagged), so a queue-built row reads 1. 0 = none.
            /// The state stays Built.</summary>
            public int OverLimitTowers { get; internal set; }
            /// <summary>The first such tower: its place counted from the base end (1 = the first tower), -1 with none.</summary>
            public int OverLimitTower { get; internal set; } = -1;
            /// <summary>That tower's distance along the line from the base (m), its bend (|deflection|, deg) and its train's limit (deg).</summary>
            public float OverLimitAlongM { get; internal set; }
            public float OverLimitBendDeg { get; internal set; }
            public float OverLimitCapDeg { get; internal set; }

            /// <summary>Not built yet and not waiting in the lift tool: the build queue may start it.</summary>
            public bool Buildable => State != SheetLiftState.Built;
        }

        private readonly List<Row> m_Rows = new List<Row>();

        /// <summary>The rows in build order (lowest bottom spot first).</summary>
        public IReadOnlyList<Row> Rows => m_Rows;

        /// <summary>Bumps on every change (load, clear, a state, a toggle, a station). The hub pushes when it moves.</summary>
        public int Revision { get; private set; }

        /// <summary>Bumps on every load and clear: which plan the rows belong to.</summary>
        public int PlanSerial { get; private set; }

        /// <summary>The last load's or clear's reason (the log and the dump).</summary>
        public string Source { get; private set; } = "empty";

        public int Count => m_Rows.Count;

        /// <summary>Replace the rows with a plan's proposed lifts: all Proposed, sorted by the bottom spot's height (ties: the key). A key
        /// that repeats keeps its first row. Returns the row count.</summary>
        public int Load(IEnumerable<Input> lifts, string why)
        {
            m_Rows.Clear();
            var seen = new HashSet<uint>();
            if (lifts != null)
                foreach (Input l in lifts)
                {
                    if (!seen.Add(l.Key))
                        continue;
                    m_Rows.Add(new Row
                    {
                        Key = l.Key, Name = l.Name ?? string.Empty, TypeIndex = l.TypeIndex, Bottom = l.Bottom, Top = l.Top, Cost = l.Cost,
                        On = l.On, State = SheetLiftState.Proposed,
                    });
                }
            m_Rows.Sort((a, b) =>
            {
                int c = LowKey(a.Bottom.y).CompareTo(LowKey(b.Bottom.y));
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            for (int i = 0; i < m_Rows.Count; i++)
                m_Rows[i].Order = i;
            PlanSerial++;
            Revision++;
            Source = why ?? "load";
            return m_Rows.Count;
        }

        /// <summary>A non-finite height sorts last.</summary>
        private static float LowKey(float y) => float.IsNaN(y) || float.IsInfinity(y) ? float.MaxValue : y;

        /// <summary>Empty the sheet (an explicit clear, a load, a scan with no plan).</summary>
        public void Clear(string why)
        {
            m_Rows.Clear();
            PlanSerial++;
            Revision++;
            Source = why ?? "clear";
        }

        public Row Find(uint key)
        {
            for (int i = 0; i < m_Rows.Count; i++)
                if (m_Rows[i].Key == key)
                    return m_Rows[i];
            return null;
        }

        /// <summary>The plan's toggle for one lift. False when the key is unknown or nothing changed.</summary>
        public bool SetOn(uint key, bool on)
        {
            Row r = Find(key);
            if (r == null || r.On == on)
                return false;
            r.On = on;
            Revision++;
            return true;
        }

        /// <summary>A row's state and reason. False when the key is unknown or nothing changed.</summary>
        public bool SetState(uint key, SheetLiftState state, string reasonKey)
        {
            Row r = Find(key);
            if (r == null || (r.State == state && r.ReasonKey == reasonKey))
                return false;
            r.State = state;
            r.ReasonKey = reasonKey;
            Revision++;
            return true;
        }

        /// <summary>The placed stations of a row (EntityRef.Null = none). False when the key is unknown or nothing changed.</summary>
        public bool SetStations(uint key, EntityRef baseStation, EntityRef topStation)
        {
            Row r = Find(key);
            if (r == null || (r.BaseStation == baseStation && r.TopStation == topStation))
                return false;
            r.BaseStation = baseStation;
            r.TopStation = topStation;
            Revision++;
            return true;
        }

        /// <summary>The over-limit towers of a row's built line (Row.OverLimitTowers and the first one's facts). <paramref name="count"/>
        /// 0 clears the flag. False when the key is unknown or nothing changed.</summary>
        public bool SetOverLimit(uint key, int count, int tower, float alongM, float bendDeg, float capDeg)
        {
            Row r = Find(key);
            if (count <= 0)
            {
                count = 0;
                tower = -1;
                alongM = bendDeg = capDeg = 0f;
            }
            if (r == null || (r.OverLimitTowers == count && r.OverLimitTower == tower && r.OverLimitAlongM == alongM &&
                              r.OverLimitBendDeg == bendDeg && r.OverLimitCapDeg == capDeg))
                return false;
            r.OverLimitTowers = count;
            r.OverLimitTower = tower;
            r.OverLimitAlongM = alongM;
            r.OverLimitBendDeg = bendDeg;
            r.OverLimitCapDeg = capDeg;
            Revision++;
            return true;
        }

        /// <summary>The keys the build queue takes for "build all": the rows switched on and not built, in build order.</summary>
        public List<uint> BuildAllKeys()
        {
            var keys = new List<uint>();
            for (int i = 0; i < m_Rows.Count; i++)
                if (m_Rows[i].On && m_Rows[i].Buildable)
                    keys.Add(m_Rows[i].Key);
            return keys;
        }

        /// <summary>The estimated cost of the rows switched on and not built (stations a row already has are not taken off: the
        /// estimate stays on the safe side).</summary>
        public long CostToBuild()
        {
            long sum = 0;
            for (int i = 0; i < m_Rows.Count; i++)
                if (m_Rows[i].On && m_Rows[i].Buildable)
                    sum += Math.Max(0L, m_Rows[i].Cost);
            return sum;
        }

        public int CountIn(SheetLiftState state)
        {
            int n = 0;
            for (int i = 0; i < m_Rows.Count; i++)
                if (m_Rows[i].State == state)
                    n++;
            return n;
        }

        /// <summary>The rows switched on and not built (what "build all" takes). The hub's lifts total prints it.</summary>
        public int CountToBuild()
        {
            int n = 0;
            for (int i = 0; i < m_Rows.Count; i++)
                if (m_Rows[i].On && m_Rows[i].Buildable)
                    n++;
            return n;
        }

        /// <summary>The rows not built yet, switched on or not.</summary>
        public int CountUnbuilt()
        {
            int n = 0;
            for (int i = 0; i < m_Rows.Count; i++)
                if (m_Rows[i].Buildable)
                    n++;
            return n;
        }

        /// <summary>A complete lift of the row's type stands between the row's two placed stations, whoever drew its line (the census rows,
        /// LiftCensus.Row: Base and Top are the station of each role, State Complete). A hand-drawn line makes the row Built this way. False
        /// when the row lacks a station.</summary>
        public static bool HasCompleteLiftBetween(IReadOnlyList<LiftCensus.Row> census, EntityRef baseStation, EntityRef topStation, byte typeIndex)
        {
            if (census == null || baseStation.IsNull || topStation.IsNull)
                return false;
            for (int i = 0; i < census.Count; i++)
            {
                LiftCensus.Row c = census[i];
                if (c.State == LiftCensus.State.Complete && c.Type == typeIndex &&
                    ((c.Base == baseStation && c.Top == topStation) || (c.Base == topStation && c.Top == baseStation)))
                    return true;
            }
            return false;
        }

        /// <summary>A station request as the build queue reads it (the pure mirror of StationPlaceToolSystem.PlaceStatus). None: no request
        /// was made, or its outcome is gone.</summary>
        public enum StationOutcome : byte { None, Pending, Placed, Refused, Canceled }

        /// <summary>The build queue's next step while it places the stations of one lift (NextStationStep).</summary>
        public enum StationStep : byte { Wait, AskTop, Settle, RefuseBase, RefuseTop, PlayerStop }

        /// <summary>The station order of the build queue (plan 3.3): the base first, the top only after the base stands. So a refused base
        /// places nothing and charges nothing, and a refused top leaves the base. <paramref name="baseHad"/> / <paramref name="topHad"/>:
        /// the station stood before the lift started. <paramref name="topAsked"/>: the top request was made. A Canceled request the queue
        /// did not ask for (the player took another tool) is PlayerStop. A missing outcome counts as a refusal.</summary>
        public static StationStep NextStationStep(bool baseHad, StationOutcome baseOutcome, bool topHad, bool topAsked, StationOutcome topOutcome)
        {
            if (!baseHad && baseOutcome != StationOutcome.Placed)
                return baseOutcome == StationOutcome.Pending ? StationStep.Wait
                    : baseOutcome == StationOutcome.Canceled ? StationStep.PlayerStop : StationStep.RefuseBase;
            if (topHad || (topAsked && topOutcome == StationOutcome.Placed))
                return StationStep.Settle;
            if (!topAsked)
                return StationStep.AskTop;
            return topOutcome == StationOutcome.Pending ? StationStep.Wait
                : topOutcome == StationOutcome.Canceled ? StationStep.PlayerStop : StationStep.RefuseTop;
        }

        /// <summary>The 1-based place of the lift in flight or next, as the hub reads it ("Building lift N / M"), 0 while the queue is idle.
        /// <paramref name="total"/> is the run's total: the lifts asked, which a Stop does not shrink (R1-f: "Stopping after lift 1 / 3").
        /// Between a lift's end and the queue's end after a Stop, no lift is next: the place stays on the lift that finished.</summary>
        public static int QueueIndexOf(bool running, int total, int done, bool stopAsked, bool jobInFlight)
        {
            if (!running)
                return 0;
            int next = stopAsked && !jobInFlight ? done : done + 1;
            return Math.Max(1, Math.Min(total, next));
        }

        /// <summary>The row key of a line that waits in the lift tool (LiftLineToolSystem ScriptTicket.Reason). A plan key or a notice key (PLAN_LOW,
        /// PLAN_TRAIN, PLAN_TRAINS, ALONG, CROSS_*, BACKWARD) stays as it is. The game's refusal of the build is BUILD_REFUSED. Any other text (no plan, auto
        /// build off, the build request refused, no Temp edges) is WAIT: the line did not build on its own. Never GAME, because nothing refused
        /// a waiting line unless the game did (R1-f).</summary>
        public static string WaitingKey(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return "WAIT";
            if (reason.StartsWith("the game refused the build", StringComparison.Ordinal))
                return "BUILD_REFUSED";
            foreach (char c in reason)
                if (!(c == '_' || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                    return "WAIT";
            return reason;
        }

        /// <summary>The player-text group of a row's reason key: the suffix of SkiResort.Tooltip.QUICK_LIFT_WHY[*]. "" for no reason. A
        /// station refusal groups by the game's error type (overlap, water, land the city does not own, anything else). An unknown key is
        /// GAME, so the hub never shows a raw key.</summary>
        public static string ReasonGroup(string reasonKey)
        {
            if (string.IsNullOrEmpty(reasonKey))
                return string.Empty;
            switch (reasonKey)
            {
                case "MONEY": case "POSE": case "PREFAB": case "STOPPED": case "GAME": case "ALONG": case "BACKWARD":
                case "PLAN_LOW": case "PLAN_STEEP": case "PLAN_HIGH": case "PLAN_TRAIN": case "PLAN_TRAINS": case "PLAN_SADDLE": case "PLAN_PITCH": case "PLAN_CLEARANCE":
                case "BUILD_REFUSED": case "WAIT":
                    return reasonKey;
                case "BASE[GONE]": case "TOP[GONE]":
                    return "GONE";
                case "LINE[PICK]": case "LINE[ABANDONED]": case "LINE[REFUSED]":
                    return "LINE_" + reasonKey.Substring(5, reasonKey.Length - 6);
            }
            if (reasonKey.StartsWith("CROSS", StringComparison.Ordinal))
                return "CROSS";
            bool isBase = reasonKey.StartsWith("BASE[", StringComparison.Ordinal);
            if ((isBase || reasonKey.StartsWith("TOP[", StringComparison.Ordinal)) && reasonKey.EndsWith("]", StringComparison.Ordinal))
            {
                int open = reasonKey.IndexOf('[');
                string error = reasonKey.Substring(open + 1, reasonKey.Length - open - 2);
                if (error == "NotEnoughMoney")
                    return "MONEY";
                string kind = error == "OverlapExisting" ? "OVERLAP" : error == "InWater" ? "WATER" : error == "ExceedsCityLimits" ? "LAND" : "OTHER";
                return (isBase ? "BASE_" : "TOP_") + kind;
            }
            return "GAME";
        }

        /// <summary>One line per row for the log (the dev dump and the queue's lines).</summary>
        public string Dump()
        {
            var sb = new StringBuilder();
            sb.Append("plan #").Append(PlanSerial).Append(" (").Append(Source).Append("), revision ").Append(Revision).Append(", ")
              .Append(m_Rows.Count).Append(" lift(s): ").Append(CountIn(SheetLiftState.Built)).Append(" built, ")
              .Append(CountIn(SheetLiftState.StationsPlaced)).Append(" stations placed, ").Append(CountIn(SheetLiftState.NeedsFitting))
              .Append(" needs fitting, ").Append(CountIn(SheetLiftState.Refused)).Append(" refused; to build ")
              .Append(CostToBuild().ToString(CultureInfo.InvariantCulture));
            foreach (Row r in m_Rows)
                sb.Append("\n  ").Append(RowText(r));
            return sb.ToString();
        }

        public static string RowText(Row r) => string.Format(CultureInfo.InvariantCulture,
            "#{0} {1} key {2:X8} type {3} {4} {5}{6}{16}; bottom ({7:F0}, {8:F0}, {9:F0}) top ({10:F0}, {11:F0}, {12:F0}); cost {13}; stations {14} / {15}",
            r.Order, r.Name, r.Key, r.TypeIndex, r.On ? "on" : "off", r.State, r.ReasonKey != null ? " (" + r.ReasonKey + ")" : string.Empty,
            r.Bottom.x, r.Bottom.y, r.Bottom.z, r.Top.x, r.Top.y, r.Top.z, r.Cost, Ref(r.BaseStation), Ref(r.TopStation),
            r.OverLimitTowers > 0 ? " [OverLimitTowers=" + r.OverLimitTowers + "]" : string.Empty);

        private static string Ref(EntityRef e) => e.IsNull ? "-" : e.Index + ":" + e.Version;
    }
}
