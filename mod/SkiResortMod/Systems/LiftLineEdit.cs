// Pure edit model of the lift-line tool. No ECS, no Game types: LiftBatch tests it headless. LiftLineToolSystem owns one
// instance per line being drawn and turns its Emit() list into CreationDefinition + NetCourse entities.
// Pins are snapped to the planner's sample grid (StepM, from the end it works from: FromB) so LiftTowerPlanner.Plan keeps them as
// seeds; every edit re-plans.
// Emit: stations first and last, each tower at (x, ground + E(member), z) with E = TopM[member] - TopM[0] (LiftTowerEdit.ElevationM),
// so the line is born fitted. LiftToolLines registers the emitted inner nodes (in-session only, not saved) so LiftTowerFitSystem
// recognizes a tool line (Match / MatchSplit) and fits heights only, never Moved.
// Engine notes: EngineNotes/LiftLineEdit.md

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public enum LiftLineNodeKind : byte
    {
        Station,
        Free,     // placed by the planner
        Pinned,   // placed or moved by the user
    }

    /// <summary>One node of the emitted line (stations first and last).</summary>
    public struct LiftLineNode
    {
        public float AlongM;
        public float3 Position;   // world: stations = the stub node; towers = (x, ground + ElevationM, z)
        public float GroundM;     // center ground (towers)
        public float ElevationM;  // E = TopM[member] - TopM[0] (towers); 0 for stations
        public int Member;        // ladder index; -1 for a station
        public LiftLineNodeKind Kind;
    }

    public enum LiftLineEditResult
    {
        Done,
        Unchanged,
        TooCloseToStation,
        TooCloseToPin,
        NoTower,
    }

    public sealed class LiftLineEdit
    {
        public readonly LiftTowerPlanConstants C;
        public readonly float3 A, B;
        public readonly float RopeA, RopeB;
        public readonly float LengthM;
        /// <summary>The ends are stations (approach floor, shortest tower by the station); false = a loose line end or open ground.</summary>
        public readonly bool StationA, StationB;
        public LiftTowerPlan Plan { get; private set; }
        public int Revision { get; private set; }

        private readonly LiftTowerPlanner m_Planner = new LiftTowerPlanner();
        private readonly List<float> m_Pins = new List<float>();   // ascending, on the StepM grid
        private readonly List<float> m_Removed = new List<float>();   // spots a free tower was removed from (planner Forbidden)
        private readonly float2 m_Dir, m_Right;

        public LiftLineEdit(LiftTowerPlanConstants c, float3 a, float3 b, float ropeA, float ropeB, bool stationA = true, bool stationB = true)
        {
            C = c;
            A = a;
            B = b;
            RopeA = ropeA;
            RopeB = ropeB;
            StationA = stationA;
            StationB = stationB;
            LengthM = math.distance(a.xz, b.xz);
            m_Dir = LengthM > 1e-3f ? (b.xz - a.xz) / LengthM : new float2(0f, 1f);
            m_Right = new float2(m_Dir.y, -m_Dir.x);
            FromB = LiftTowerPlanner.PlansFromB(ropeA, ropeB, a.xz, b.xz);
        }

        /// <summary>The planner works from end B (LiftTowerPlanner.PlansFromB: the end with the lower rope; equal ropes, the smaller
        /// world xz): its sample grid starts there, so pins snap to that grid (Snap) and Replan hands it the ground sampled from B
        /// (PointFromB). The same two ends give the same answer in either order, so the plan does not depend on the click order.</summary>
        public readonly bool FromB;

        public IReadOnlyList<float> Pins => m_Pins;
        public IReadOnlyList<float> Removed => m_Removed;

        /// <summary>No new tower within this of a removed spot: two samples (2 x StepM) or MinSpanM, whichever is larger.</summary>
        public float RemovedRadiusM => math.max(C.MinSpanM, 2f * C.StepM);

        /// <summary>No pin nearer this end of the line than this. A loose or ground end is a tower: MinSpanM, the tower-to-tower minimum
        /// (FG / DQ / GD 10 m, tram 40 m). A station end only needs the pinned tower clear of the terminal's front (the stub tip): FG /
        /// DQ / GD 10 m as before; tram one step (10 m: the 11.4 m wide tower, SkiTramTower30 m_LegSize, then stands 4 m clear of the stub
        /// tip). Field test 2 refused a tram pin at 1,737 m of a 1,756 m line (19 m from the top station) five times under the old
        /// MinSpanM (40 m) at both kinds of end.</summary>
        public float PinEndMarginM(bool stationEnd) =>
            stationEnd && C.TypeId == "tram" ? C.StepM : math.max(C.MinSpanM, C.StepM);

        public float PinMarginA => PinEndMarginM(StationA);
        public float PinMarginB => PinEndMarginM(StationB);

        /// <summary>The margin of the end that refused the last pin (TooCloseToStation), for the tool's message.</summary>
        public float LastEndMarginM { get; private set; }
        public int LineTowers => Plan == null ? 0 : Plan.LineTowers;


        /// <summary>World xz point at <paramref name="alongM"/> (y = 0) plus <paramref name="lateralM"/> to the right of A -> B.</summary>
        public float3 PointAt(float alongM, float lateralM = 0f)
        {
            float2 p = A.xz + m_Dir * alongM + m_Right * lateralM;
            return new float3(p.x, 0f, p.y);
        }

        /// <summary>World xz point at <paramref name="alongM"/> from B toward A plus <paramref name="lateralM"/> to the right of B -> A:
        /// the point a line drawn from B first gets for the same numbers (bit for bit: its direction is exactly -A -> B), so a planner
        /// working from B (FromB) samples the same ground in either click order.</summary>
        public float3 PointFromB(float alongM, float lateralM = 0f)
        {
            float2 p = B.xz + (-m_Dir) * alongM + (-m_Right) * lateralM;
            return new float3(p.x, 0f, p.y);
        }

        public float3 Right => new float3(m_Right.x, 0f, m_Right.y);

        /// <summary>Along (clamped to the line) and lateral offset of a world point.</summary>
        public float Project(float3 p, out float lateralM)
        {
            float2 d = p.xz - A.xz;
            lateralM = math.dot(d, m_Right);
            return math.clamp(math.dot(d, m_Dir), 0f, LengthM);
        }

        /// <summary>The line crosses the straight piece c..d, another lift's edge in plan view: the game joins the two at the crossing. A
        /// piece met within <paramref name="endMarginM"/> of the line's ends does not count: a section drawn on from a lift meets it there.</summary>
        public bool Crosses(float2 c, float2 d, float endMarginM)
        {
            float2 e = d - c;
            float denom = m_Dir.x * e.y - m_Dir.y * e.x;
            if (math.abs(denom) < 1e-5f)
                return false;   // parallel
            float2 ac = c - A.xz;
            float along = (ac.x * e.y - ac.y * e.x) / denom;
            float onPiece = (ac.x * m_Dir.y - ac.y * m_Dir.x) / denom;
            return onPiece >= 0f && onPiece <= 1f && along > endMarginM && along < LengthM - endMarginM;
        }

        /// <summary>The planner sample grid (pins are samples, so Plan keeps them exactly): every StepM from the end the planner works
        /// from (FromB: from B, so meters along from A are LengthM - k x StepM).</summary>
        public float Snap(float alongM) =>
            FromB ? LengthM - math.round((LengthM - alongM) / C.StepM) * C.StepM : math.round(alongM / C.StepM) * C.StepM;


        /// <summary>A loose end joining a built line: the built neighbor's rope point beyond end A / B (xz distance from the end node,
        /// rope y there; NaN = unknown, that end is not judged), so the planner judges the junction tower's rope deflection and may give it
        /// a partner inside this section (breakover-holddown-plan-2026-11-03, section 2.3 step 1). The tool sets them after the pick.</summary>
        public float OuterDistA = float.NaN, OuterRopeA = float.NaN, OuterDistB = float.NaN, OuterRopeB = float.NaN;

        /// <summary>Re-plan with the pins as seeds (the fewest-towers objective fills between them), then the breakover / hold-down
        /// pairs (LiftTowerPlanner.Pairs: tool plans only; partners are Free nodes, pins never move). <paramref name="terrain"/> is
        /// sampled from A (PointAt), <paramref name="terrainFromB"/> (optional) the same ground from B (PointFromB): the planner reads
        /// it when it works from B (FromB), so both click orders read the same ground points; null = the planner mirrors
        /// <paramref name="terrain"/>.</summary>
        public LiftTowerPlan Replan(LiftTerrainSampler terrain, LiftTerrainSampler terrainFromB = null)
        {
            m_Planner.EndKeyA = A.xz;
            m_Planner.EndKeyB = B.xz;
            m_Planner.Pairs = true;
            m_Planner.EndOuterDistA = OuterDistA;
            m_Planner.EndOuterRopeA = OuterRopeA;
            m_Planner.EndOuterDistB = OuterDistB;
            m_Planner.EndOuterRopeB = OuterRopeB;
            var seeds = new List<LiftPlanNode>(m_Pins.Count);
            for (int i = 0; i < m_Pins.Count; i++)
                seeds.Add(new LiftPlanNode { AlongM = m_Pins[i], Member = -1, Flags = LiftPlanNodeFlags.Seed });   // the planner sets its height
            m_Planner.Forbidden = m_Removed;
            m_Planner.ForbiddenRadiusM = RemovedRadiusM;
            m_Planner.StationA = StationA;
            m_Planner.StationB = StationB;
            // The engine refuses an edge steeper than the line prefab allows (SteepSlope): plan the nodes as they will be built.
            m_Planner.CheckEdgeSlope = true;
            m_Planner.EndNodeYA = A.y;
            m_Planner.EndNodeYB = B.y;
            Plan = m_Planner.Plan(terrain, LengthM, RopeA, RopeB, C, seeds, terrainFromB);
            Revision++;
            return Plan;
        }

        public bool IsPinned(float alongM)
        {
            for (int i = 0; i < m_Pins.Count; i++)
                if (math.abs(m_Pins[i] - alongM) < 1e-3f)
                    return true;
            return false;
        }

        /// <summary>Index in Plan.Nodes of the line tower nearest <paramref name="alongM"/> within <paramref name="pickM"/>, else -1.</summary>
        public int TowerNear(float alongM, float pickM)
        {
            if (Plan == null)
                return -1;
            int best = -1;
            float bestD = pickM;
            for (int i = 1; i < Plan.Nodes.Length - 1; i++)
            {
                float d = math.abs(Plan.Nodes[i].AlongM - alongM);
                if (d <= bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }


        // Every edit that returns Done first saves the pins and removed spots it changed; Undo restores the last save (the caller
        // re-plans). ClearPins saves too, so it can be undone.

        private readonly List<float[][]> m_Undo = new List<float[][]>();

        /// <summary>Edits that can be undone (0 = the planner's own line as it was first drawn, or after every undo).</summary>
        public int UndoDepth => m_Undo.Count;

        private float[][] Snapshot() => new[] { m_Pins.ToArray(), m_Removed.ToArray() };

        private LiftLineEditResult Saved(float[][] before, LiftLineEditResult r)
        {
            if (r == LiftLineEditResult.Done)
                m_Undo.Add(before);
            return r;
        }

        /// <summary>Step back one edit: the pins and removed spots before it. False when there is nothing to undo.</summary>
        public bool Undo()
        {
            if (m_Undo.Count == 0)
                return false;
            float[][] s = m_Undo[m_Undo.Count - 1];
            m_Undo.RemoveAt(m_Undo.Count - 1);
            m_Pins.Clear();
            m_Pins.AddRange(s[0]);
            m_Removed.Clear();
            m_Removed.AddRange(s[1]);
            return true;
        }

        /// <summary>Pin a tower at <paramref name="alongM"/> (snapped). Refused within PinEndMarginM of an end or MinSpanM of another pin.</summary>
        public LiftLineEditResult AddPin(float alongM) => Saved(Snapshot(), AddPinExcept(alongM, float.NaN));

        private LiftLineEditResult AddPinExcept(float alongM, float ignore)
        {
            float s = Snap(alongM);
            if (s < PinMarginA - 1e-3f || s > LengthM - PinMarginB + 1e-3f)
            {
                LastEndMarginM = s < PinMarginA - 1e-3f ? PinMarginA : PinMarginB;
                return LiftLineEditResult.TooCloseToStation;
            }
            for (int i = 0; i < m_Pins.Count; i++)
            {
                if (!float.IsNaN(ignore) && math.abs(m_Pins[i] - ignore) < 1e-3f)
                    continue;
                if (math.abs(m_Pins[i] - s) < 1e-3f)
                    return LiftLineEditResult.Unchanged;
                if (math.abs(m_Pins[i] - s) < C.MinSpanM - 1e-3f)
                    return LiftLineEditResult.TooCloseToPin;
            }
            int at = 0;
            while (at < m_Pins.Count && m_Pins[at] < s)
                at++;
            m_Pins.Insert(at, s);
            // A pin the user puts back near a removed spot lifts that exclusion.
            for (int r = m_Removed.Count - 1; r >= 0; r--)
                if (math.abs(m_Removed[r] - s) < RemovedRadiusM - 1e-3f)
                    m_Removed.RemoveAt(r);
            return LiftLineEditResult.Done;
        }

        /// <summary>Move the tower at <paramref name="fromAlongM"/> (a pin, or a free tower, which becomes a pin) to
        /// <paramref name="toAlongM"/> (snapped). Refused like AddPin; the old pin stays when refused.</summary>
        public LiftLineEditResult MoveTower(float fromAlongM, float toAlongM) => Saved(Snapshot(), MoveTowerCore(fromAlongM, toAlongM));

        private LiftLineEditResult MoveTowerCore(float fromAlongM, float toAlongM)
        {
            bool wasPinned = IsPinned(fromAlongM);
            float s = Snap(toAlongM);
            if (math.abs(s - fromAlongM) < 1e-3f)
            {
                if (wasPinned)
                    return LiftLineEditResult.Unchanged;
                return AddPinExcept(fromAlongM, float.NaN);   // a click-drag that did not move pins the free tower where it is
            }
            LiftLineEditResult r = AddPinExcept(s, wasPinned ? fromAlongM : float.NaN);
            if (r != LiftLineEditResult.Done)
                return r;
            if (wasPinned)
                RemovePinAt(fromAlongM);
            return LiftLineEditResult.Done;
        }

        /// <summary>A tower dragged off the line: a pin is unpinned; a free tower is removed by pinning every other line tower where it
        /// stands and dropping this one.</summary>
        public LiftLineEditResult RemoveTower(float alongM) => Saved(Snapshot(), RemoveTowerCore(alongM));

        private LiftLineEditResult RemoveTowerCore(float alongM)
        {
            if (Plan == null)
                return LiftLineEditResult.NoTower;
            if (IsPinned(alongM))
            {
                RemovePinAt(alongM);
                return LiftLineEditResult.Done;
            }
            bool found = false;
            for (int i = 1; i < Plan.Nodes.Length - 1; i++)
                found |= math.abs(Plan.Nodes[i].AlongM - alongM) < 1e-3f;
            if (!found)
                return LiftLineEditResult.NoTower;
            for (int i = 1; i < Plan.Nodes.Length - 1; i++)
            {
                float s = Plan.Nodes[i].AlongM;
                if (math.abs(s - alongM) < 1e-3f || IsPinned(s))
                    continue;
                int at = 0;
                while (at < m_Pins.Count && m_Pins[at] < s)
                    at++;
                m_Pins.Insert(at, s);   // already a plan node: on the grid, spaced >= MinSpanM by the planner
            }
            m_Removed.Add(alongM);   // no new tower back here unless the line has no other way (LiftTowerPlanner.Forbidden)
            return LiftLineEditResult.Done;
        }

        /// <summary>Back to the planner's own line: no pins, no removed spots.</summary>
        public void ClearPins()
        {
            if (m_Pins.Count > 0 || m_Removed.Count > 0)
                m_Undo.Add(Snapshot());
            m_Pins.Clear();
            m_Removed.Clear();
        }

        private void RemovePinAt(float alongM)
        {
            for (int i = 0; i < m_Pins.Count; i++)
            {
                if (math.abs(m_Pins[i] - alongM) < 1e-3f)
                {
                    m_Pins.RemoveAt(i);
                    return;
                }
            }
        }


        /// <summary>The line to build: the plan's nodes, stations at A / B, towers at (x, ground + E, z).</summary>
        public List<LiftLineNode> Emit()
        {
            var list = new List<LiftLineNode>();
            if (Plan == null)
                return list;
            int n = Plan.Nodes.Length;
            for (int i = 0; i < n; i++)
            {
                LiftPlanNode pn = Plan.Nodes[i];
                if (i == 0 || i == n - 1)
                {
                    list.Add(new LiftLineNode
                    {
                        AlongM = pn.AlongM, Position = i == 0 ? A : B, GroundM = pn.GroundM, ElevationM = 0f, Member = -1,
                        Kind = LiftLineNodeKind.Station,
                    });
                    continue;
                }
                float e = LiftTowerEdit.ElevationM(C, pn.Member);
                float3 p = PointAt(pn.AlongM);
                list.Add(new LiftLineNode
                {
                    AlongM = pn.AlongM, Position = new float3(p.x, pn.GroundM + e, p.z), GroundM = pn.GroundM, ElevationM = e,
                    Member = pn.Member, Kind = IsPinned(pn.AlongM) ? LiftLineNodeKind.Pinned : LiftLineNodeKind.Free,
                });
            }
            return list;
        }

        /// <summary>One log-ready line: towers, member mix, spans, clearance.</summary>
        public string Describe()
        {
            if (Plan == null)
                return "no plan";
            var sb = new StringBuilder();
            sb.Append(Plan.LineTowers).Append(" tower(s) {").Append(Plan.MemberCounts()).Append("}, ").Append(m_Pins.Count)
              .Append(" pinned; spans ");
            for (int s = 0; s < Plan.Spans.Length; s++)
                sb.Append(s == 0 ? string.Empty : "/").Append(Plan.Spans[s].LengthM.ToString("F0", CultureInfo.InvariantCulture));
            sb.Append(" m (").Append(LengthM.ToString("F0", CultureInfo.InvariantCulture)).Append(" m); min floor margin ")
              .Append(LiftTowerPlan.Sg(Plan.MinFloorMarginM)).Append(" m; ").Append(Plan.Clear ? "clear" : "VIOLATED");
            return sb.ToString();
        }
    }

    /// <summary>The tool's step (LiftLineToolSystem.ToolState without its two build frames).</summary>
    public enum LiftLineStep : byte
    {
        PickBase,
        PickTop,
        Edit,
        Building,
    }

    /// <summary>What one right click does (LiftLineInput.RightClick).</summary>
    public enum LiftLineBack : byte
    {
        None,        // building: ignored
        CancelDrag,  // a tower is being dragged: it goes back where it was
        UndoEdit,    // the last add / move / remove is undone
        DropTop,     // no edit left: the second point goes, the tool waits for it again
        DropBase,    // the first point goes
        CloseTool,   // nothing left: back to the default tool
    }

    /// <summary>What a line end is: a station's stub node, the loose end of a lift line (a section was bulldozed: the node has
    /// one lift-line edge and no station), or open ground (a fresh end node, the vanilla net tool's line-first drawing).</summary>
    public enum LiftLineEndKind : byte
    {
        Station,
        LooseEnd,
        Ground,
    }

    /// <summary>The lift tool's input rules, pure so LiftBatch tests them. One model for every lift type: left click places a point /
    /// adds a tower, drag moves a tower (released off the line: removes it), right click steps back one action and closes the tool
    /// only when nothing is left, the Build key (Enter) builds, Esc closes (the frontend's Back). Vanilla: NetToolSystem binds right
    /// click ("Cancel", override "Undo Net Control Point") to removing the last control point (NetToolSystem.cs:5482-5483 enable,
    /// :6256-6330 Cancel -> m_ControlPoints.RemoveAt(Length - 1)); it disables the action when fewer than two points are placed
    /// (:5482), where we close the tool instead.</summary>
    public static class LiftLineInput
    {
        public static LiftLineBack RightClick(LiftLineStep step, bool dragging, int undoDepth)
        {
            switch (step)
            {
                case LiftLineStep.Edit:
                    if (dragging)
                        return LiftLineBack.CancelDrag;
                    return undoDepth > 0 ? LiftLineBack.UndoEdit : LiftLineBack.DropTop;
                case LiftLineStep.PickTop:
                    return LiftLineBack.DropBase;
                case LiftLineStep.PickBase:
                    return LiftLineBack.CloseTool;
                default:
                    return LiftLineBack.None;
            }
        }

        /// <summary>The step after a right click (CloseTool / None leave it as it is: the tool closes or ignores the click).</summary>
        public static LiftLineStep After(LiftLineStep step, LiftLineBack back)
        {
            switch (back)
            {
                case LiftLineBack.DropTop: return LiftLineStep.PickTop;
                case LiftLineBack.DropBase: return LiftLineStep.PickBase;
                default: return step;
            }
        }

        /// <summary>A drag released this far (or more) to the side of the line removes the tower instead of moving it.</summary>
        public static bool DragRemoves(float lateralM, float pickLateralM) => math.abs(lateralM) > pickLateralM;

        /// <summary>The shortest line the tool draws: two planner samples, the fit's own floor (LiftTowerFitSystem "too short to fit"). A
        /// station-to-station line also needs room for its two station run-outs (2 x MinSpanM).</summary>
        public static float MinLengthM(LiftTowerPlanConstants c, bool bothStations) =>
            bothStations ? 2f * c.MinSpanM : 2f * c.StepM;

        /// <summary>The plan bend, degrees, the drawn line makes where it joins a built line at a loose end: <paramref name="builtFar"/> =
        /// the built edge's other node, <paramref name="joint"/> = the loose end, <paramref name="otherEnd"/> = the drawn line's other end
        /// (the tool's line is straight, so its first tower lies on that heading). 0 when the bend is at or under LiftAlignMath.BendWarnDeg:
        /// the tool warns over it and never refuses (owner verdict 2026-11-02).</summary>
        public static float JoinBendWarnDeg(float3 builtFar, float3 joint, float3 otherEnd)
        {
            float bend = LiftAlignMath.PlanBendDeg(builtFar, joint, otherEnd);
            return bend > LiftAlignMath.BendWarnDeg ? bend : 0f;
        }

        /// <summary>A rope-lane anchor this far (xz) from the end node or nearer belongs to this end (LiftTowerFitSystem.StationRope).</summary>
        public const float AnchorSearchM = 30f;

        /// <summary>The rope height the built line will have at a station end, so the tool plans with what the fit reads. LaneSystem
        /// anchors the line's rope lane end to the station: anchors are the world end points of the HV sub-lanes of the node's
        /// sub-objects (order 0) and of the node's owner, the station (order 2) (FindAnchors, Game.Net/LaneSystem.cs:2371-2416,
        /// :2441-2453; FindAnchor :4049-4071, applied :4117-4121); the fit takes the lowest rope-lane end within AnchorSearchM of the
        /// node. <paramref name="subObjectAnchors"/> / <paramref name="ownerAnchors"/> = those world points (the lower order wins when it
        /// has any within reach); none gives <paramref name="fallback"/> (stub node + the shortest member's rope).
        /// <paramref name="fromLane"/> = an anchor was used.</summary>
        public static float StationRope(IList<float3> subObjectAnchors, IList<float3> ownerAnchors, float3 node, float fallback,
                                        out bool fromLane)
        {
            float best = Lowest(subObjectAnchors, node);
            if (float.IsPositiveInfinity(best))
                best = Lowest(ownerAnchors, node);
            fromLane = !float.IsPositiveInfinity(best);
            return fromLane ? best : fallback;
        }

        private static float Lowest(IList<float3> anchors, float3 node)
        {
            float best = float.PositiveInfinity;
            if (anchors == null)
                return best;
            for (int i = 0; i < anchors.Count; i++)
                if (math.distance(anchors[i].xz, node.xz) <= AnchorSearchM)
                    best = math.min(best, anchors[i].y);
            return best;
        }

        /// <summary>The player name of a lift type id (SkiCatalog LiftTypeDef.Id); "Lift" when unknown.</summary>
        public static string TypeName(string typeId)
        {
            switch (typeId)
            {
                case "fixedGrip": return "Fixed-grip";
                case "detachable": return "Detachable";
                case "gondola": return "Gondola";
                case "tram": return "Tram";
                default: return "Lift";
            }
        }

        /// <summary>The step hint's LocaleEN key (SkiResort.LiftTool.*; {TYPE} = the lift type): what the next click does.</summary>
        public static string PromptKey(LiftLineStep step, string typeId, bool dragging, bool dragOff)
        {
            switch (step)
            {
                case LiftLineStep.PickBase: return string.IsNullOrEmpty(typeId) ? "PICK_FIRST_ANY" : "PICK_FIRST";
                case LiftLineStep.PickTop: return "PICK_SECOND";
                case LiftLineStep.Edit: return dragOff ? "DRAG_OFF" : dragging ? "DRAG" : "EDIT";
                default: return "BUILDING";
            }
        }

        /// <summary>A lift type id with its own TYPE[id] name in LocaleEN.</summary>
        public static bool KnownType(string typeId) =>
            typeId == "fixedGrip" || typeId == "detachable" || typeId == "gondola" || typeId == "tram";

        /// <summary>The plan line's key: the first thing wrong (a span below the floor, an edge too steep for the engine, a span above
        /// the max rope height), else PLAN_CLEAR. <paramref name="lengthM"/> = the line's length (sum of the spans).</summary>
        public static string PlanKey(LiftTowerPlan plan, out float lengthM)
        {
            bool low = false, steep = false, high = false;
            lengthM = 0f;
            for (int i = 0; i < plan.Spans.Length; i++)
            {
                low |= plan.Spans[i].FloorViolated;
                steep |= plan.Spans[i].SlopeViolated;
                high |= plan.Spans[i].HeightViolated;
                lengthM += plan.Spans[i].LengthM;
            }
            return low ? "PLAN_LOW" : steep ? "PLAN_STEEP" : high ? "PLAN_HIGH" : "PLAN_CLEAR";
        }

        /// <summary>The hovered tower's key, its height (the ladder member's tower top above its base, as the model stands) and how
        /// high a chair or cabin passes over the ground at the tower (rope above the base - the carrier's drop). TOWER_LOW when a span
        /// beside it is below the floor (the span minimum is not used: at a station end the chairs are at loading height by design).</summary>
        public static string TowerKey(LiftTowerPlan plan, int node, bool pinned, out float heightM, out float clearM)
        {
            LiftTowerPlanConstants c = plan.Constants;
            int m = plan.Nodes[node].Member;
            heightM = c.Ladder[m].TopM;
            clearM = math.max(0f, c.RopeAboveBase(m) - c.CarrierDropM);
            if (plan.Spans[node - 1].FloorViolated || plan.Spans[node].FloorViolated)
                return "TOWER_LOW";
            return pinned ? "TOWER_PINNED" : "TOWER";
        }

        /// <summary>The step hint in English for the log (the tooltip shows PromptKey's LocaleEN text).</summary>
        public static string Prompt(LiftLineStep step, string typeId, bool dragging)
        {
            string type = string.IsNullOrEmpty(typeId) ? "Lift" : TypeName(typeId);
            switch (step)
            {
                case LiftLineStep.PickBase:
                    return string.IsNullOrEmpty(typeId)
                        ? "Lift line: click a lift station or a loose line end to start · right click: close"
                        : type + " line: click a station, a loose line end or open ground to start · right click: close";
                case LiftLineStep.PickTop:
                    return type + " line: click the other end (a station, a loose line end or open ground) · right click: back";
                case LiftLineStep.Edit:
                    return dragging
                        ? "Release: place the tower here · release off the line: remove it · right click: cancel the move"
                        : "Click the line: add a tower · drag a tower: move it (off the line: remove it) · right click: undo · " +
                          "Enter: build · Esc: close";
                default:
                    return type + " line: building…";
            }
        }
    }

    /// <summary>A tool line found in a built path that the engine split into more nodes than the tool emitted
    /// (LiftToolLines.MatchSplit). Per inner path node: the tool's member (-1 = an extra engine split node), pinned, extra.</summary>
    public sealed class LiftToolMatch
    {
        public LiftToolLines.Line Line;
        public int[] Members;      // tool member per inner path node; -1 = extra split node (the fit gives it ChordMembers)
        public bool[] Pinned;
        public bool[] Extra;
        public int Registered;     // tool inner nodes found (= Line.Inner.Length)
        public int Extras;         // inner path nodes the tool did not emit
        public int Pins;           // pinned tool nodes found
        public bool Reversed;      // the path runs from the tool's last end to its first
    }

    /// <summary>In-session registry of lines built by the lift-line tool (EngineNotes/LiftLineEdit.md, Tool lines). The engine may build a tool
    /// course as more edges than emitted (CourseSplitSystem.CheckHeightRange elevation transitions, CourseSplitSystem.cs:2884-2942;
    /// crossings). MatchSplit then recognizes the line when every emitted inner node has its own path node within MatchTolM, in order,
    /// the path's ends are the tool's ends (EndTolM) and every extra node lies on the tool's straight line (ExtraLateralTolM).</summary>
    public sealed class LiftToolLines
    {
        public const float MatchTolM = 0.5f;
        /// <summary>The path's end nodes vs the tool's emitted ends (station stub / loose end / fresh ground node: the same node,
        /// so 0 in practice; 2 m allows float noise and never confuses two different lines' ends).</summary>
        public const float EndTolM = 2f;
        /// <summary>An extra split node lies on the course, so on the tool's straight xz line; farther off = another line.</summary>
        public const float ExtraLateralTolM = 1f;
        /// <summary>ChordMembers: two members whose ropes are this close in distance to the planned chord tie (the taller wins).</summary>
        public const float ChordTolM = 0.25f;
        /// <summary>ChordMembers hysteresis: the extra node's current member is kept while the chord moves less than this (the ground
        /// under a written node shifts a few decimeters with our own carve).</summary>
        public const float ChordHystM = 0.5f;
        public const int MaxLines = 64;

        public sealed class Line
        {
            public string TypeId;
            public float3[] Inner;     // emitted inner node positions (world)
            public int[] Members;
            public bool[] Pinned;
            public int Pins;
            public float3 EndA, EndB;  // emitted first / last node positions (MatchSplit)
            public bool HasEnds;
        }

        private readonly List<Line> m_Lines = new List<Line>();
        public int Count => m_Lines.Count;

        public void Clear() => m_Lines.Clear();

        public Line Register(string typeId, IList<LiftLineNode> emitted)
        {
            int inner = math.max(0, emitted.Count - 2);
            var line = new Line { TypeId = typeId, Inner = new float3[inner], Members = new int[inner], Pinned = new bool[inner] };
            if (emitted.Count >= 2)
            {
                line.EndA = emitted[0].Position;
                line.EndB = emitted[emitted.Count - 1].Position;
                line.HasEnds = true;
            }
            for (int i = 0; i < inner; i++)
            {
                LiftLineNode n = emitted[i + 1];
                line.Inner[i] = n.Position;
                line.Members[i] = n.Member;
                line.Pinned[i] = n.Kind == LiftLineNodeKind.Pinned;
                line.Pins += line.Pinned[i] ? 1 : 0;
            }
            m_Lines.Add(line);
            if (m_Lines.Count > MaxLines)
                m_Lines.RemoveAt(0);
            return line;
        }

        /// <summary>The registered line whose inner nodes are exactly these (xz within MatchTolM, same count, either direction),
        /// with its members / pins in the order of <paramref name="innerXz"/>; null when none matches.</summary>
        public Line Match(IList<float3> innerXz, out int[] members, out bool[] pinned)
        {
            members = null;
            pinned = null;
            int n = innerXz.Count;
            if (n == 0)
                return null;
            for (int l = m_Lines.Count - 1; l >= 0; l--)
            {
                Line line = m_Lines[l];
                if (line.Inner.Length != n)
                    continue;
                for (int dir = 0; dir < 2; dir++)
                {
                    bool ok = true;
                    for (int i = 0; i < n && ok; i++)
                    {
                        int j = dir == 0 ? i : n - 1 - i;
                        ok = math.distance(innerXz[i].xz, line.Inner[j].xz) <= MatchTolM;
                    }
                    if (!ok)
                        continue;
                    members = new int[n];
                    pinned = new bool[n];
                    for (int i = 0; i < n; i++)
                    {
                        int j = dir == 0 ? i : n - 1 - i;
                        members[i] = line.Members[j];
                        pinned[i] = line.Pinned[j];
                    }
                    return line;
                }
            }
            return null;
        }

        /// <summary>The registered line this built path is, allowing extra engine split nodes. <paramref name="path"/> = every node of
        /// the path in order, ends included (world positions). Needs: the path's ends within EndTolM of the tool's ends (either
        /// direction); every tool inner node matched, in order, by its own path node within MatchTolM (xz); every other inner path node
        /// within ExtraLateralTolM of the tool's end-to-end xz line. Newest line first; null when none.</summary>
        public LiftToolMatch MatchSplit(IList<float3> path)
        {
            if (path == null || path.Count < 3)
                return null;
            int n = path.Count - 2;
            for (int l = m_Lines.Count - 1; l >= 0; l--)
            {
                Line line = m_Lines[l];
                if (!line.HasEnds || line.Inner.Length > n)
                    continue;
                for (int dir = 0; dir < 2; dir++)
                {
                    float3 first = dir == 0 ? line.EndA : line.EndB, last = dir == 0 ? line.EndB : line.EndA;
                    if (math.distance(path[0].xz, first.xz) > EndTolM || math.distance(path[path.Count - 1].xz, last.xz) > EndTolM)
                        continue;
                    LiftToolMatch m = Assign(line, path, dir == 1);
                    if (m != null)
                        return m;
                }
            }
            return null;
        }

        private static LiftToolMatch Assign(Line line, IList<float3> path, bool reversed)
        {
            int n = path.Count - 2, r = line.Inner.Length;
            var m = new LiftToolMatch
            {
                Line = line, Members = new int[n], Pinned = new bool[n], Extra = new bool[n], Registered = r, Reversed = reversed,
            };
            int j = 0;
            for (int i = 0; i < n; i++)
            {
                float3 p = path[i + 1];
                int reg = reversed ? r - 1 - j : j;
                if (j < r && math.distance(p.xz, line.Inner[reg].xz) <= MatchTolM)
                {
                    m.Members[i] = line.Members[reg];
                    m.Pinned[i] = line.Pinned[reg];
                    m.Pins += line.Pinned[reg] ? 1 : 0;
                    j++;
                    continue;
                }
                if (Lateral(line.EndA, line.EndB, p) > ExtraLateralTolM)
                    return null;
                m.Members[i] = -1;
                m.Extra[i] = true;
                m.Extras++;
            }
            return j == r ? m : null;
        }

        /// <summary>xz distance of <paramref name="p"/> from the segment a -> b.</summary>
        public static float Lateral(float3 a, float3 b, float3 p)
        {
            float2 ab = b.xz - a.xz;
            float len2 = math.lengthsq(ab);
            if (len2 < 1e-6f)
                return math.distance(a.xz, p.xz);
            float t = math.saturate(math.dot(p.xz - a.xz, ab) / len2);
            return math.distance(a.xz + ab * t, p.xz);
        }

        /// <summary>Diagnostics for a line that did not match: the registered line with the most inner nodes found in
        /// <paramref name="path"/> (xz within MatchTolM, any order), <paramref name="found"/> / <paramref name="pins"/> of them; null when
        /// no registered node is on the path. The fit WARNs when it would move such a line's pinned towers.</summary>
        public Line BestPartial(IList<float3> path, out int found, out int pins)
        {
            found = pins = 0;
            Line best = null;
            if (path == null)
                return null;
            for (int l = m_Lines.Count - 1; l >= 0; l--)
            {
                Line line = m_Lines[l];
                int f = 0, p = 0;
                for (int k = 0; k < line.Inner.Length; k++)
                {
                    for (int i = 0; i < path.Count; i++)
                    {
                        if (math.distance(path[i].xz, line.Inner[k].xz) <= MatchTolM)
                        {
                            f++;
                            p += line.Pinned[k] ? 1 : 0;
                            break;
                        }
                    }
                }
                if (f > found)
                {
                    found = f;
                    pins = p;
                    best = line;
                }
            }
            return best;
        }

        /// <summary>Members for the extra split nodes of a matched tool line (members[i] &lt; 0), so the rope stays on the plan: the
        /// planned chord runs between the neighboring tool nodes' ropes (ground + RopeAboveBase(member); the station ropes at the ends)
        /// and an extra node gets the member whose rope is nearest that chord (a tie goes to the taller: never a dip below the planned
        /// rope when a member sits exactly between). <paramref name="current"/> (the node's member now, -1 unknown) is kept while its
        /// rope is within ChordHystM of the best one's distance (no flip-flop on re-fits). Tool nodes are not touched. Pure. The fit
        /// then checks the clearance; an extra that breaks it is re-filled by the planner (LiftTowerPlanner.Fit spareNodes).</summary>
        public static void ChordMembers(LiftTowerPlanConstants c, IList<float> innerAlongM, IList<float> groundM, int[] members,
                                        IList<int> current, float lengthM, float ropeA, float ropeB)
        {
            int n = members.Length;
            var tool = (int[])members.Clone();   // the chord runs between TOOL nodes only, never an extra filled in this pass
            for (int i = 0; i < n; i++)
            {
                if (tool[i] >= 0)
                    continue;
                int a = i - 1, b = i + 1;
                while (a >= 0 && tool[a] < 0)
                    a--;
                while (b < n && tool[b] < 0)
                    b++;
                float sa = a >= 0 ? innerAlongM[a] : 0f, sb = b < n ? innerAlongM[b] : lengthM;
                float ra = a >= 0 ? groundM[a] + c.RopeAboveBase(tool[a]) : ropeA;
                float rb = b < n ? groundM[b] + c.RopeAboveBase(tool[b]) : ropeB;
                float t = sb - sa > 1e-3f ? math.saturate((innerAlongM[i] - sa) / (sb - sa)) : 0.5f;
                float chord = math.lerp(ra, rb, t);
                int pick = Nearest(c, groundM[i], chord);
                int cur = current != null && i < current.Count ? current[i] : -1;
                if (cur >= 0 && cur < c.Ladder.Length && cur != pick &&
                    math.abs(groundM[i] + c.RopeAboveBase(cur) - chord) <= math.abs(groundM[i] + c.RopeAboveBase(pick) - chord) + ChordHystM)
                    pick = cur;
                members[i] = pick;
            }
        }

        /// <summary>The member whose rope (ground + RopeAboveBase) is nearest <paramref name="ropeY"/>; a tie (within ChordTolM) goes
        /// to the taller.</summary>
        public static int Nearest(LiftTowerPlanConstants c, float ground, float ropeY)
        {
            int best = 0;
            float bestD = float.PositiveInfinity;
            for (int m = 0; m < c.Ladder.Length; m++)
            {
                float d = math.abs(ground + c.RopeAboveBase(m) - ropeY);
                if (d <= bestD + ChordTolM)   // ascending ladder: a near-tie takes the taller member
                {
                    best = m;
                    bestD = math.min(bestD, d);
                }
            }
            return best;
        }
    }
}
