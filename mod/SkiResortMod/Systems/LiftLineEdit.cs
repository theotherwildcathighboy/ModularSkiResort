// LiftLineEdit — PURE edit model of the lift-line tool (T48 steps 3c + 4; build note docs/research/field/2026-10-20/
// t48-round2-build.md). No ECS, no Game types: LiftBatch tests it headless (LiftPlannerTests LP-18 / LP-19). LiftLineToolSystem
// owns one instance per line being drawn and turns its Emit() list into CreationDefinition + NetCourse entities.
//
// THE MODEL
//   * Two station ends A (base click) and B (top click): the stub-tip nodes' world positions; the line is the straight xz segment
//     A -> B ("along" = meters from A in xz). Station ropes RopeA / RopeB are given (LiftLineToolSystem: the station's bullwheel
//     height, else stub node y + the shortest member's rope).
//   * PINS: positions the user fixed (click on the line = add, drag a tower = move, which pins a free tower). Pins are snapped to the
//     planner's sample grid (StepM: 5 m, tram 10 m), so a pin is a planner sample and LiftTowerPlanner.Plan keeps it exactly as a
//     seed (Plan: k = round(along / StepM)). A pin closer than MinSpanM to a station or to another pin is refused.
//   * THE PLAN = LiftTowerPlanner.Plan(seeds = pins): the fewest-towers search fills every pin-to-pin segment (round 2 objective),
//     members by phase (c) for every tower (pinned ones included). Re-planned after every change.
//   * UNDO (T125, right click): every Done edit saves the pins / removed spots before it; Undo restores the last (UndoDepth).
//   * REMOVE (drag a tower off the line; was right click before T125): a pinned tower is unpinned (the planner may put a tower back near it if the line needs one);
//     a FREE tower is removed by pinning every OTHER line tower where it stands and dropping this one — the user's "not here" sticks,
//     and the planner fills only spans that still exceed MaxSpanM or violate the floor; it never puts a NEW tower within
//     RemovedRadiusM of a removed spot (LiftTowerPlanner.Forbidden), so a tower the span still needs lands elsewhere in it.
//   * EMIT: the plan's nodes in order, stations first and last, each tower at (x, ground + E(member), z) with E = TopM[member] -
//     TopM[0] (LiftTowerEdit.ElevationM): the node height the fit system writes, so the line is born fitted. Node count = the plan's.
//
// TOOL LINES (LiftToolLines): after an Apply the tool registers the emitted inner nodes (xz, member, pinned). LiftTowerFitSystem
// asks Match() for every line it fits: a line whose every inner node is a registered tool node (xz within MatchTolM) is a TOOL
// LINE — heights only with the tool's members (never Moved, so a pinned node is never snapped). In-session only (the registry is
// not saved): after a load the fit's hysteresis (Fit currentMembers) keeps a clear line as it is.

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
        Pinned,   // placed / moved by the user
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
        OffLine,
        NoTower,
    }

    public sealed class LiftLineEdit
    {
        public readonly LiftTowerPlanConstants C;
        public readonly float3 A, B;
        public readonly float RopeA, RopeB;
        public readonly float LengthM;
        public LiftTowerPlan Plan { get; private set; }
        public int Revision { get; private set; }

        private readonly LiftTowerPlanner m_Planner = new LiftTowerPlanner();
        private readonly List<float> m_Pins = new List<float>();   // ascending, on the StepM grid
        private readonly List<float> m_Removed = new List<float>();   // spots a free tower was removed from (planner Forbidden)
        private readonly float2 m_Dir, m_Right;

        public LiftLineEdit(LiftTowerPlanConstants c, float3 a, float3 b, float ropeA, float ropeB)
        {
            C = c;
            A = a;
            B = b;
            RopeA = ropeA;
            RopeB = ropeB;
            LengthM = math.distance(a.xz, b.xz);
            m_Dir = LengthM > 1e-3f ? (b.xz - a.xz) / LengthM : new float2(0f, 1f);
            m_Right = new float2(m_Dir.y, -m_Dir.x);
        }

        public IReadOnlyList<float> Pins => m_Pins;
        public IReadOnlyList<float> Removed => m_Removed;

        /// <summary>No new tower within this of a removed spot: two samples (2 x StepM) or MinSpanM, whichever is larger.</summary>
        public float RemovedRadiusM => math.max(C.MinSpanM, 2f * C.StepM);
        public int LineTowers => Plan == null ? 0 : Plan.LineTowers;

        // ── geometry ─────────────────────────────────────────────────────────────────────────────

        /// <summary>World xz point at <paramref name="alongM"/> (y = 0) plus <paramref name="lateralM"/> to the right of A -> B.</summary>
        public float3 PointAt(float alongM, float lateralM = 0f)
        {
            float2 p = A.xz + m_Dir * alongM + m_Right * lateralM;
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

        /// <summary>The planner sample grid (pins are samples, so Plan keeps them exactly).</summary>
        public float Snap(float alongM) => math.round(alongM / C.StepM) * C.StepM;

        // ── plan ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>Re-plan with the pins as seeds (the fewest-towers objective fills between them).</summary>
        public LiftTowerPlan Replan(LiftTerrainSampler terrain)
        {
            var seeds = new List<LiftPlanNode>(m_Pins.Count);
            for (int i = 0; i < m_Pins.Count; i++)
                seeds.Add(new LiftPlanNode { AlongM = m_Pins[i], Member = 0, Flags = LiftPlanNodeFlags.Seed });
            m_Planner.Forbidden = m_Removed;
            m_Planner.ForbiddenRadiusM = RemovedRadiusM;
            Plan = m_Planner.Plan(terrain, LengthM, RopeA, RopeB, C, seeds);
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

        // ── edits (the caller re-plans after Done) ───────────────────────────────────────────────

        // ── undo (T125: right click steps back one action) ───────────────────────────────────────
        // Every edit that returns Done first saves the pins and removed spots it changed; Undo restores the last save (the
        // caller re-plans). ClearPins saves too, so it can be undone.

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

        /// <summary>Pin a tower at <paramref name="alongM"/> (snapped). Refused within MinSpanM of a station or of another pin.</summary>
        public LiftLineEditResult AddPin(float alongM) => Saved(Snapshot(), AddPinExcept(alongM, float.NaN));

        private LiftLineEditResult AddPinExcept(float alongM, float ignore)
        {
            float s = Snap(alongM);
            if (s < C.MinSpanM - 1e-3f || s > LengthM - C.MinSpanM + 1e-3f)
                return LiftLineEditResult.TooCloseToStation;
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

        /// <summary>A tower dragged off the line (T125; was right click): a pin is unpinned; a free tower is removed by pinning every
        /// other line tower where it stands and dropping this one.</summary>
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
            m_Removed.Add(alongM);   // the planner never puts a new tower back here (LiftTowerPlanner.Forbidden)
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

        // ── emission ─────────────────────────────────────────────────────────────────────────────

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

    /// <summary>T125 — the lift tool's input rules, pure so LiftBatch tests them (LT-T125-*). One model for every lift type:
    /// left click places a point / adds a tower, drag moves a tower (released off the line: removes it), right click steps back
    /// one action and closes the tool only when nothing is left, the Build key (Enter) builds, Esc closes (the frontend's Back).
    /// Vanilla: NetToolSystem binds right click ("Cancel", override "Undo Net Control Point") to removing the LAST control point
    /// (NetToolSystem.cs:5482-5483 enable, :6256-6330 Cancel -> m_ControlPoints.RemoveAt(Length - 1)); it disables the action
    /// when fewer than two points are placed (:5482), where we close the tool instead (ticket T125: "cancels the tool only when
    /// nothing is left").</summary>
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

        /// <summary>The shortest line the tool draws: two planner samples, the fit's own floor (LiftTowerFitSystem "too short to
        /// fit"). A station-to-station line also needs room for its two station run-outs (2 x MinSpanM), as before.</summary>
        public static float MinLengthM(LiftTowerPlanConstants c, bool bothStations) =>
            bothStations ? 2f * c.MinSpanM : 2f * c.StepM;

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

        /// <summary>The step hint: ALWAYS a non-empty line that names what the next click does in this step.</summary>
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

    /// <summary>In-session registry of lines built by the lift-line tool (see the header TOOL LINES).</summary>
    public sealed class LiftToolLines
    {
        public const float MatchTolM = 0.5f;
        public const int MaxLines = 64;

        public sealed class Line
        {
            public string TypeId;
            public float3[] Inner;     // emitted inner node positions (world)
            public int[] Members;
            public bool[] Pinned;
            public int Pins;
        }

        private readonly List<Line> m_Lines = new List<Line>();
        public int Count => m_Lines.Count;

        public void Clear() => m_Lines.Clear();

        public Line Register(string typeId, IList<LiftLineNode> emitted)
        {
            int inner = math.max(0, emitted.Count - 2);
            var line = new Line { TypeId = typeId, Inner = new float3[inner], Members = new int[inner], Pinned = new bool[inner] };
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
    }
}
