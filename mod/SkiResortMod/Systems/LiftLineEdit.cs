// Pure edit model of the lift-line tool. No ECS, no Game types: LiftBatch tests it headless. LiftLineToolSystem owns one instance per line
// being drawn and turns its Emit() list into CreationDefinition + NetCourse entities. Pins are snapped to the planner's sample grid
// (StepM, from the end the planner works from: FromB), so LiftTowerPlanner.Plan keeps them as seeds. Every edit re-plans.
// Emit puts stations first and last. It puts each tower at (x, ground + E(member), z) with E = TopM[member] - TopM[0]
// (LiftTowerEdit.ElevationM). Therefore the line is born fitted. LiftToolLines registers the emitted inner nodes (in-session only, not
// saved). Therefore LiftTowerFitSystem recognizes a tool line (Match / MatchSplit) and fits heights only, never Moved.
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
        public float3 Position;   // world: stations = the stub node, towers = (x, ground + ElevationM, z)
        public float GroundM;     // center ground (towers)
        public float ElevationM;  // E of the placed member (towers): Top(twin or member) - TopM[0], LiftTowerEdit.ElevationM(member, head). 0 for stations
        public int Member;        // ladder index. -1 for a station
        public LiftTowerHead Head; // the planned head (a twin of Member, or Line)
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
        /// <summary>The ends are stations (approach floor, shortest tower by the station). False means a loose line end or open
        /// ground.</summary>
        public readonly bool StationA, StationB;
        public LiftTowerPlan Plan { get; private set; }
        public int Revision { get; private set; }

        private readonly LiftTowerPlanner m_Planner;
        private readonly List<float> m_Pins = new List<float>();   // ascending, on the StepM grid
        private readonly List<float> m_Removed = new List<float>();   // spots a free tower was removed from (planner Forbidden)
        private readonly float2 m_Dir, m_Right;

        /// <summary><paramref name="planner"/>: a planner to reuse (the planner's and the realizer's tower checks keep one per thread);
        /// null = a new one.</summary>
        public LiftLineEdit(LiftTowerPlanConstants c, float3 a, float3 b, float ropeA, float ropeB, bool stationA = true, bool stationB = true,
                            LiftTowerPlanner planner = null)
        {
            m_Planner = planner ?? new LiftTowerPlanner();
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

        /// <summary>The planner works from end B (LiftTowerPlanner.PlansFromB: the end with the lower rope, or with equal ropes the smaller
        /// world xz). Its sample grid starts there, so pins snap to that grid (Snap). Replan gives it the ground sampled from B
        /// (PointFromB). The same two ends give the same answer in either order, so the plan does not depend on the click order.</summary>
        public readonly bool FromB;

        public IReadOnlyList<float> Pins => m_Pins;
        public IReadOnlyList<float> Removed => m_Removed;

        /// <summary>No new tower within this of a removed spot: two samples (2 x StepM) or MinSpanM, whichever is larger.</summary>
        public float RemovedRadiusM => math.max(C.MinSpanM, 2f * C.StepM);

        /// <summary>No pin nearer this end of the line than this. A loose or ground end is a tower: the margin is MinSpanM, the
        /// tower-to-tower minimum (FG / DQ / GD 10 m, tram 40 m). A station end takes the game's limit, not the planner's floor: the
        /// game joins the span of a tower nearer than StubSnapM to the stub node and refuses the line (FG / DQ 11 m, GD 12.1 m, tram
        /// 15.5 m, task R2-l). A pin keeps StubSnapM + PinSnapMarginM (FG / DQ 11.5 m, GD 12.6 m, tram 16 m), at least one step. The
        /// planner places its own towers no nearer than StationFloorM (20 m). A player's pin may stand nearer, where the game accepts it.
        /// Field test 2 refused a tram pin 19 m from the top station five times under the old MinSpanM (40 m) at both kinds of end. That
        /// pin (snapped to 16 m from the top) stays legal.</summary>
        public float PinEndMarginM(bool stationEnd) =>
            stationEnd ? math.max(C.StubSnapM + LiftTowerPlanConstants.PinSnapMarginM, C.StepM) : math.max(C.MinSpanM, C.StepM);

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

        /// <summary>World xz point at <paramref name="alongM"/> from B toward A plus <paramref name="lateralM"/> to the right of B -> A.
        /// This is the point a line drawn from B first gets for the same numbers (bit for bit: its direction is exactly -A -> B). Therefore
        /// a planner working from B (FromB) samples the same ground in either click order.</summary>
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

        /// <summary>The line crosses the straight piece c..d, another lift's edge in plan view. A lift of the same type joins this one at
        /// the crossing. Another type is crossed over or under with the box clearance (FIX-LIST 60 a). A piece met within
        /// <paramref name="endMarginM"/> of the line's ends does not count: a section drawn onward from a lift meets it there.</summary>
        public bool Crosses(float2 c, float2 d, float endMarginM) => CrossAt(c, d, endMarginM, out _, out _);

        /// <summary>Crosses, with where: <paramref name="alongM"/> along this line from A, <paramref name="onPiece"/> the fraction of
        /// c..d (0 at c). The line nets' curves are straight in plan view (NetUtils.StraightCurve: b / c are xz lerps), so this
        /// fraction is the crossed curve's own t.</summary>
        public bool CrossAt(float2 c, float2 d, float endMarginM, out float alongM, out float onPiece)
        {
            float2 e = d - c;
            float denom = m_Dir.x * e.y - m_Dir.y * e.x;
            alongM = onPiece = 0f;
            if (math.abs(denom) < 1e-5f)
                return false;   // parallel
            float2 ac = c - A.xz;
            alongM = (ac.x * e.y - ac.y * e.x) / denom;
            onPiece = (ac.x * m_Dir.y - ac.y * m_Dir.x) / denom;
            return onPiece >= 0f && onPiece <= 1f && alongM > endMarginM && alongM < LengthM - endMarginM;
        }

        /// <summary>The plan angle (degrees, 0..90) between this line and the piece c..d.</summary>
        public float AngleTo(float2 c, float2 d)
        {
            float len = math.distance(c, d);
            if (len < 1e-4f)
                return 0f;
            return math.degrees(math.acos(math.saturate(math.abs(math.dot(m_Dir, (d - c) / len)))));
        }

        /// <summary>Crossings with built lifts of another type (FIX-LIST 60 a). The tool fills them before each Replan. Null means
        /// none.</summary>
        public IList<LiftPlanCrossing> Crossings;

        /// <summary>Road, track and path stretches under the line (x..y meters along from A, merged, FIX-LIST 62). The tool fills them
        /// before each Replan. The planner keeps new towers out of them where the line has a clear plan without
        /// (LiftTowerPlanner.Keepouts). Null means none.</summary>
        public IList<float2> Keepouts;

        /// <summary>Clearance (meters) beyond a road's half width and the tower head's half width: the stretch of a road kept tower-free.</summary>
        public const float KeepoutClearM = 1f;

        /// <summary>The stretch of this line (meters along from A, clipped to 0..LengthM) whose plan-view distance to the piece c..d is at
        /// most <paramref name="reachM"/>. It is the line through the capsule around c..d (convex, so one interval: the union of the
        /// intervals of the two end circles and the side band). False means the line passes farther.</summary>
        public bool KeepoutOf(float2 c, float2 d, float reachM, out float2 stretch)
        {
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            Circle(c, reachM, ref lo, ref hi);
            Circle(d, reachM, ref lo, ref hi);
            float len = math.distance(c, d);
            if (len > 1e-4f)
            {
                float2 e = (d - c) / len;
                float2 a0 = A.xz - c;
                // along the piece: 0 <= dot(a0, e) + s dot(dir, e) <= len. Across: |cross(e, a0) + s cross(e, dir)| <= reachM
                float s0 = float.NegativeInfinity, s1 = float.PositiveInfinity;
                if (Slab(math.dot(a0, e), math.dot(m_Dir, e), 0f, len, ref s0, ref s1) &&
                    Slab(e.x * a0.y - e.y * a0.x, e.x * m_Dir.y - e.y * m_Dir.x, -reachM, reachM, ref s0, ref s1))
                {
                    lo = math.min(lo, s0);
                    hi = math.max(hi, s1);
                }
            }
            lo = math.max(lo, 0f);
            hi = math.min(hi, LengthM);
            stretch = new float2(lo, hi);
            return lo < hi;
        }

        private void Circle(float2 q, float r, ref float lo, ref float hi)
        {
            float2 v = q - A.xz;
            float t = math.dot(v, m_Dir), l = math.dot(v, m_Right);
            if (math.abs(l) > r)
                return;
            float h = math.sqrt(r * r - l * l);
            lo = math.min(lo, t - h);
            hi = math.max(hi, t + h);
        }

        /// <summary>Narrows s0..s1 to the s where lo &lt;= v0 + s k &lt;= hi. False means none.</summary>
        private static bool Slab(float v0, float k, float lo, float hi, ref float s0, ref float s1)
        {
            if (math.abs(k) < 1e-6f)
                return v0 >= lo && v0 <= hi;
            float a = (lo - v0) / k, b = (hi - v0) / k;
            s0 = math.max(s0, math.min(a, b));
            s1 = math.min(s1, math.max(a, b));
            return s0 <= s1;
        }

        /// <summary>Sorts and merges stretches that touch or overlap (in place).</summary>
        public static void MergeStretches(List<float2> stretches)
        {
            stretches.Sort((p, q) => p.x.CompareTo(q.x));
            int w = 0;
            for (int i = 0; i < stretches.Count; i++)
            {
                if (w > 0 && stretches[i].x <= stretches[w - 1].y)
                    stretches[w - 1] = new float2(stretches[w - 1].x, math.max(stretches[w - 1].y, stretches[i].y));
                else
                    stretches[w++] = stretches[i];
            }
            stretches.RemoveRange(w, stretches.Count - w);
        }

        /// <summary>The planner sample grid (pins are samples, so Plan keeps them exactly): every StepM from the end the planner works
        /// from. When FromB is true, the grid starts at B, so meters along from A are LengthM - k x StepM.</summary>
        public float Snap(float alongM) =>
            FromB ? LengthM - math.round((LengthM - alongM) / C.StepM) * C.StepM : math.round(alongM / C.StepM) * C.StepM;


        /// <summary>A loose end joining a built line: the built neighbor's rope point beyond end A / B (xz distance from the end node, rope
        /// y there. NaN means unknown, and that end is not judged). The planner therefore judges the junction tower's rope deflection and
        /// may give it a partner inside this section (breakover-holddown-plan-2026-11-03, section 2.3 step 1). The tool sets them after the
        /// pick.</summary>
        public float OuterDistA = float.NaN, OuterRopeA = float.NaN, OuterDistB = float.NaN, OuterRopeB = float.NaN;

        /// <summary>Re-plan with the pins as seeds (the fewest-towers objective fills between them), then the breakover / hold-down pairs
        /// (LiftTowerPlanner.Pairs: tool plans only, partners are Free nodes, pins never move). <paramref name="terrain"/> is sampled from
        /// A (PointAt). <paramref name="terrainFromB"/> (optional) is the same ground from B (PointFromB). The planner reads it when it
        /// works from B (FromB), so both click orders read the same ground points. Null means the planner mirrors
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
            m_Planner.Crossings = Crossings;   // different lift types cross with box clearance (FIX-LIST 60 a)
            m_Planner.Keepouts = Keepouts;     // no new tower on a road, track or path where the line can do without (FIX-LIST 62)
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


        // Every edit that returns Done first saves the pins and removed spots it changed. Undo restores the last save (the caller
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
        /// <paramref name="toAlongM"/> (snapped). Refused like AddPin. The old pin stays when refused.</summary>
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

        /// <summary>A tower dragged off the line: a pin is unpinned, and a free tower is removed by pinning every other line tower where it
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
                float e = LiftTowerEdit.ElevationM(C, pn.Member, pn.Head);   // Elevation (E, E) picks the member. FloatM lifts the node (tram pedestals)
                float3 p = PointAt(pn.AlongM);
                list.Add(new LiftLineNode
                {
                    AlongM = pn.AlongM, Position = new float3(p.x, pn.GroundM + e + pn.FloatM, p.z), GroundM = pn.GroundM, ElevationM = e,
                    Member = pn.Member, Head = pn.Head, Kind = IsPinned(pn.AlongM) ? LiftLineNodeKind.Pinned : LiftLineNodeKind.Free,
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
              .Append(LiftTowerPlan.Sg(Plan.MinFloorMarginM)).Append(" m; ").Append(Plan.Verdict);
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

    /// <summary>What a line end is: a station's stub node, the loose end of a lift line, or open ground. A loose end is a node with one
    /// lift-line edge and no station (a section was bulldozed). Open ground is a fresh end node (the vanilla net tool's line-first
    /// drawing).</summary>
    public enum LiftLineEndKind : byte
    {
        Station,
        LooseEnd,
        Ground,
    }

    /// <summary>The lift tool's input rules, pure so LiftBatch tests them. One model for every lift type. Left click places a point / adds
    /// a tower. Drag moves a tower (released off the line, it removes the tower). Right click steps back one action and closes the tool
    /// only when nothing is left. The Build key (Enter) builds. Esc closes (the frontend's Back). Vanilla: NetToolSystem binds right click
    /// ("Cancel", override "Undo Net Control Point") to removing the last control point (NetToolSystem.cs:5482-5483 enable, :6256-6330
    /// Cancel -> m_ControlPoints.RemoveAt(Length - 1)). It disables the action when fewer than two points are placed (:5482). In that case
    /// we close the tool instead.</summary>
    public static class LiftLineInput
    {
        /// <summary>The line the build queue's lift tool plans between a planned lift's two stations (field r2g round 2, task 2b-l). The
        /// stations stand where the queue places them (StationAlign.QueueLift under StationAlign.QueueHeightRule: the pose, the station
        /// height rule, the stub tips, the pad cut). The line runs stub tip to stub tip, its end nodes at the ground under the stubs, its
        /// ropes at the stations' own anchors, over the ground the pads leave (PadSampler), and plans as the tool plans it
        /// (LiftLineEdit.Replan: pairs on, from the end with the lower rope). The planner's Towers pass and the realizer's lift rows call
        /// it, so their Sound is the queue's. Not modeled: crossings with other lifts and keepouts (the tool collects them from the
        /// city). <paramref name="ground"/> gives the terrain at a world point; the caller handles a miss. Null when the pose is invalid.</summary>
        public static LiftTowerPlan PlanQueueLine(LiftTowerPlanConstants c, StationDef baseStation, StationDef topStation, float2 bottomXz,
            float2 topXz, System.Func<float3, float> ground, LiftTowerPlanner planner, out StationAlign.QueueStation bottom,
            out StationAlign.QueueStation top, out LiftLineEdit edit)
        {
            edit = null;
            if (baseStation == null || topStation == null ||
                !StationAlign.QueueLift(bottomXz, topXz, baseStation, topStation, StationAlign.QueueHeightRule, ground, out bottom, out top))
            {
                bottom = top = default;
                return null;
            }
            float ropeA = math.isfinite(bottom.RopeY) ? bottom.RopeY : c.StationEndRope(baseStation, bottom.NodeY);
            float ropeB = math.isfinite(top.RopeY) ? top.RopeY : c.StationEndRope(topStation, top.NodeY);
            LiftLineEdit e = edit = new LiftLineEdit(c, bottom.Tip, top.Tip, ropeA, ropeB, true, true, planner);
            if (e.LengthM < 1f)
                return null;
            LiftTerrainSampler fromA = (s, l) => ground(e.PointAt(s, l));
            LiftTerrainSampler fromB = (s, l) => ground(e.PointFromB(s, l));
            // A placed station's cut ends at its stub tip (field r2g round 2, r4 P3 built by hand: the ground 10 m past the base stub tip
            // read 821.3 m in game = the bare terrain, not the 811 m plane a 12 m runway would leave): PlacedPadReachM, then the apron.
            fromA = PadSampler(fromA, e.LengthM, false, baseStation, bottom.StationY, topStation, top.StationY, PlacedPadReachM);
            fromB = PadSampler(fromB, e.LengthM, true, baseStation, bottom.StationY, topStation, top.StationY, PlacedPadReachM);
            return e.Replan(fromA, fromB);
        }

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

        /// <summary>The plan bend, in degrees, that the drawn line makes where it joins a built line at a loose end.
        /// <paramref name="builtFar"/> = the built edge's other node, <paramref name="joint"/> = the loose end, <paramref name="otherEnd"/>
        /// = the drawn line's other end (the tool's line is straight, so its first tower lies on that heading). The value is 0 when the
        /// bend is at or under LiftAlignMath.BendWarnDeg: the tool warns over it and never refuses (owner verdict 2026-11-02).</summary>
        public static float JoinBendWarnDeg(float3 builtFar, float3 joint, float3 otherEnd)
        {
            float bend = LiftAlignMath.PlanBendDeg(builtFar, joint, otherEnd);
            return bend > LiftAlignMath.BendWarnDeg ? bend : 0f;
        }

        /// <summary>A line drawn ALONG a built lift (FIX-LIST 60 b, field 2026-11-05). A gondola drawn on top of an FG line read as a
        /// crossing, and the build replaced the FG's edges with its own. Another lift's edge counts as along this line where it runs within
        /// AlongMaxOffsetM of it (plan view) at AlongMaxAngleDeg or less, over more than AlongMinOverlapM of this line. The tool refuses
        /// Build then. Two lines side by side that far apart would hang their carriers into each other anyway (DQ rope gauge 2.6 m + a 1.1
        /// m carrier half width per side).</summary>
        public const float AlongMaxOffsetM = 8f, AlongMaxAngleDeg = 10f, AlongMinOverlapM = 15f;

        /// <summary>Meters of the drawn line <paramref name="a"/> -> <paramref name="b"/> (xz) that the segment <paramref name="c"/> ->
        /// <paramref name="d"/> runs along (FIX-LIST 60 b): the part of c-d within AlongMaxOffsetM of the line a-b, projected onto a-b and
        /// clipped to [endMarginM, length - endMarginM]. A lift met at an end is the section's own joint: a line drawn onward from a loose
        /// end runs past its built neighbor, never along it. The value is 0 when the two are more than AlongMaxAngleDeg apart, or either is
        /// shorter than 1 cm.</summary>
        public static float AlongOverlapM(float2 a, float2 b, float2 c, float2 d, float endMarginM)
        {
            float len = math.distance(a, b), seg = math.distance(c, d);
            if (len < 0.01f || seg < 0.01f)
                return 0f;
            float2 dir = (b - a) / len, right = new float2(dir.y, -dir.x);
            float2 e = (d - c) / seg;
            if (math.abs(math.dot(dir, e)) < math.cos(math.radians(AlongMaxAngleDeg)))
                return 0f;
            // c-d as s in [0, 1]: lateral offset and along position are linear in s
            float oc = math.dot(c - a, right), od = math.dot(d - a, right);
            float tc = math.dot(c - a, dir), td = math.dot(d - a, dir);
            float s0 = 0f, s1 = 1f;
            if (!ClipLinear(oc, od, -AlongMaxOffsetM, AlongMaxOffsetM, ref s0, ref s1))
                return 0f;
            float lo = math.lerp(tc, td, s0), hi = math.lerp(tc, td, s1);
            if (lo > hi)
                (lo, hi) = (hi, lo);
            lo = math.max(lo, endMarginM);
            hi = math.min(hi, len - endMarginM);
            return math.max(0f, hi - lo);
        }

        /// <summary>Narrows [s0, s1] to where v0 + s (v1 - v0) lies in [min, max]. False when nothing is left.</summary>
        private static bool ClipLinear(float v0, float v1, float min, float max, ref float s0, ref float s1)
        {
            float dv = v1 - v0;
            if (math.abs(dv) < 1e-6f)
                return v0 >= min && v0 <= max;
            float sa = (min - v0) / dv, sb = (max - v0) / dv;
            if (sa > sb)
                (sa, sb) = (sb, sa);
            s0 = math.max(s0, sa);
            s1 = math.min(s1, sb);
            return s0 <= s1;
        }

        /// <summary>The crossing notice key (FIX-LIST 60 a), or null. A crossing with another lift type that the plan cannot build gives
        /// CROSS_SHALLOW / CROSS_STATION / CROSS_TOWER / CROSS_OWN_TOWER / CROSS_CLEARANCE. This is the first refused crossing: Build is
        /// refused, <paramref name="refused"/> is true, and <paramref name="needM"/> = the separation it needs, for CROSS_CLEARANCE.
        /// Otherwise the key is CROSSES_UNDER / CROSSES_OVER when the line passes another type (under first: the rarer side). Otherwise it
        /// is CROSSES when the line crosses a lift of its own type (<paramref name="sameType"/>: the game joins the two there,
        /// unchanged).</summary>
        public static string CrossKey(LiftTowerPlan plan, bool sameType, out bool refused, out float needM)
        {
            refused = false;
            needM = 0f;
            LiftPlanCrossing[] xs = plan?.Crossings;
            bool over = false, under = false;
            if (xs != null)
            {
                for (int i = 0; i < xs.Length; i++)
                {
                    if (xs[i].Refusal == LiftCrossRefusal.None)
                    {
                        over |= xs[i].Side == LiftCrossSide.Over;
                        under |= xs[i].Side == LiftCrossSide.Under;
                        continue;
                    }
                    refused = true;
                    needM = xs[i].RequiredM;
                    switch (xs[i].Refusal)
                    {
                        case LiftCrossRefusal.Shallow: return "CROSS_SHALLOW";
                        case LiftCrossRefusal.Station: return "CROSS_STATION";
                        case LiftCrossRefusal.Tower: return "CROSS_TOWER";
                        case LiftCrossRefusal.OwnTower: return "CROSS_OWN_TOWER";
                        case LiftCrossRefusal.SameType: return "CROSS_SAME_TYPE";
                        default: return "CROSS_CLEARANCE";
                    }
                }
            }
            return under ? "CROSSES_UNDER" : over ? "CROSSES_OVER" : sameType ? "CROSSES" : null;
        }

        /// <summary>A rope-lane anchor this far (xz) from the end node or nearer belongs to this end (LiftTowerFitSystem.StationRope).</summary>
        public const float AnchorSearchM = 30f;

        /// <summary>The rope height the built line will have at a station end, so the tool plans with what the fit reads. LaneSystem
        /// anchors the line's rope lane end to the station. The anchors are the world end points of the HV sub-lanes of the node's
        /// sub-objects (order 0) and of the node's owner, the station (order 2) (FindAnchors, Game.Net/LaneSystem.cs:2371-2416, :2441-2453;
        /// FindAnchor :4049-4071, applied :4117-4121). The fit takes the lowest rope-lane end within AnchorSearchM of the node.
        /// <paramref name="subObjectAnchors"/> / <paramref name="ownerAnchors"/> = those world points (the lower order wins when it has any
        /// within reach). With none, the result is <paramref name="fallback"/> (stub node + the shortest member's rope).
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

        /// <summary>The rope a GROUND end plans with (FIX-LIST 48). The open end is where a station goes (line-first, its stub course
        /// reuses this node), so it plans with that station's own anchor (LiftTowerPlanConstants.StationEndRope). <paramref name="source"/>
        /// says which height won.</summary>
        public static float GroundEndRope(LiftTowerPlanConstants c, StationDef station, float nodeY, float frontGroundY, out string source)
        {
            float rope = c.StationEndRope(station, nodeY, frontGroundY);
            string role = station == null ? string.Empty : station.Role == StationRole.Base ? "base" : "top";
            string why = station == null || !math.isfinite(station.RopeAnchorLocalY) ? "tower"
                : math.isfinite(frontGroundY) && rope < nodeY + station.RopeAnchorLocalY - 1e-3f
                    ? "the " + role + " station's anchor over its lot front " +
                      LiftTowerPlanConstants.FrontDistM(station).ToString("F0", CultureInfo.InvariantCulture) + " m out"
                : "the " + role + " station's anchor";
            source = "ground end, node + " + (rope - nodeY).ToString("F2", CultureInfo.InvariantCulture) + " (" + why + ")";
            return rope;
        }

        /// <summary>The engine's smoothing apron past a lot's cut window: GetTerrainSmoothingWidth = max(8, |lot size| / 12)
        /// (ObjectUtils.cs:1004-1007) = 8 m for every station lot. The blend inside it lives in the terrain shader [I]: modeled linear.</summary>
        public const float PadApronM = 8f;

        /// <summary>How far past the stub tip, along the line, a station holds the ground at its plane (StationDef.PadBackLocalZ). NaN when
        /// the station keeps the slope or the row has no pad. Bases: 12 m (the runway). Tops and the tram base: 0 (the pad ends at the stub
        /// tip, the lot's back edge).</summary>
        public static float PadReachM(StationDef station) =>
            station == null || !math.isfinite(station.PadBackLocalZ) ? float.NaN : math.max(0f, station.StubTipLocalZ - station.PadBackLocalZ);

        /// <summary>How far past the stub tip a station the build queue PLACED holds its plane: 0 (task 2b-l2). The bare terrain came back
        /// 10 m past a DQ base's stub tip in game (r2g round 2, r4 P3: Towers [plan] "lowest carrier above ground min +1.8 at 10 m" over
        /// rope 819.21 -> 841.51 puts the ground at 821.3; the bare dump reads 821.31 there; a 12 m runway would leave 811). Whether a
        /// ground end's coming station (PadReachM, the lift tool) cuts further is not measured.</summary>
        public const float PlacedPadReachM = 0f;

        /// <summary>The ground a GROUND end's coming station leaves under the line (banks-pass 2026-11-09). A pad-cutting station cuts
        /// the ground above its plane <paramref name="stationY"/> and never fills: out to PadReachM from the stub tip the ground is
        /// min(raw, plane); over the next PadApronM it blends back to raw. A station that keeps the slope leaves raw. A built station's
        /// cut is in the terrain the samplers read already; a ground end's is not, so without this the end planned with ground the station
        /// will cut away (a false SUNK_END on a base runway). <paramref name="distM"/> = xz distance from the end node along the line.
        /// <paramref name="reachOverrideM"/> (finite) replaces PadReachM for a pad-cutting station: PlacedPadReachM for the queue line.</summary>
        public static float PadGround(float raw, float distM, StationDef station, float stationY, float reachOverrideM = float.NaN)
        {
            float reach = PadReachM(station);
            if (math.isfinite(reach) && math.isfinite(reachOverrideM))
                reach = reachOverrideM;
            if (!math.isfinite(reach) || !math.isfinite(stationY) || distM >= reach + PadApronM)
                return raw;
            float t = math.saturate((distM - reach) / PadApronM);
            return math.min(raw, math.lerp(stationY, raw, t));
        }

        /// <summary>The plane a GROUND end's coming station stands at: vanilla stands it on the terrain at its lot front
        /// (StationEndRope), never above the node. NaN when <paramref name="frontGroundY"/> was not sampled (then the node).</summary>
        public static float GroundEndStationY(float nodeY, float frontGroundY) =>
            math.isfinite(frontGroundY) ? math.min(nodeY, frontGroundY) : nodeY;

        /// <summary><paramref name="raw"/> with PadGround applied at each GROUND end that has a station row: end A at along 0, end B at
        /// <paramref name="lengthM"/> (<paramref name="fromB"/>: the sampler's along runs from B). A null station leaves that end raw.</summary>
        public static LiftTerrainSampler PadSampler(LiftTerrainSampler raw, float lengthM, bool fromB, StationDef stationA, float stationYA,
                                                    StationDef stationB, float stationYB, float reachOverrideM = float.NaN)
        {
            if (!math.isfinite(PadReachM(stationA)) && !math.isfinite(PadReachM(stationB)))
                return raw;
            return (along, lateral) =>
            {
                float g = raw(along, lateral);
                float fromA = fromB ? lengthM - along : along, fromBEnd = fromB ? along : lengthM - along;
                g = PadGround(g, fromA, stationA, stationYA, reachOverrideM);
                return PadGround(g, fromBEnd, stationB, stationYB, reachOverrideM);
            };
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

        /// <summary>The player name of a lift type id (SkiCatalog LiftTypeDef.Id). "Lift" when unknown.</summary>
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

        /// <summary>The step hint's LocaleEN key (SkiResort.LiftTool.*, with {TYPE} = the lift type): what the next click does.</summary>
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

        /// <summary>The plan line's key: the first thing wrong, else PLAN_CLEAR. PLAN_CLEAR is only ever the key of a Sound plan. The
        /// things that can be wrong are:
        ///   * a span below the floor
        ///   * an edge too steep for the engine
        ///   * a span above the max rope height
        ///   * a tower whose rope bends past its sheave train's cap (PLAN_TRAIN, FIX-LIST 55) or past the tram saddle's cap (PLAN_SADDLE,
        ///     DECISIONS 600 a)
        ///   * a tower whose rope runs through it steeper than the type's pitch cap (PLAN_PITCH, DECISIONS 600)
        ///   * a tower whose rope half is past its carrier clearance (PLAN_CLEARANCE, DECISIONS 600 c). It read PLAN_TRAIN, a bend text,
        ///     until the lean docs round.
        /// <paramref name="lengthM"/> = the line's length (sum of the spans).</summary>
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
            return low ? "PLAN_LOW" : steep ? "PLAN_STEEP" : high ? "PLAN_HIGH" : plan.OverCapTowers > 0 ? (plan.Constants.HasTrains ? "PLAN_TRAIN" : "PLAN_SADDLE")
                : plan.TooSteepTowers > 0 ? "PLAN_PITCH" : plan.ClearanceTowers > 0 ? "PLAN_CLEARANCE" : "PLAN_CLEAR";
        }

        /// <summary>The hovered tower's key, its height (the ladder member's tower top above its base, as the model stands) and how high a
        /// chair or cabin passes over the ground at the tower (rope above the base - the carrier's drop). The keys are:
        ///   * TOWER_LOW when a span beside it is below the floor (the span minimum is not used: at a station end the chairs are at loading
        ///     height by design)
        ///   * TOWER_TRAIN when its rope bends past its sheave train's cap (<paramref name="bendDeg"/> = the bend,
        ///     <paramref name="capDeg"/> = the cap, both 0 otherwise, FIX-LIST 55)
        ///   * TOWER_SADDLE for the same on a tram saddle (its cap 2 x SaddleHalfCapDeg, DECISIONS 600 a)
        ///   * TOWER_PITCH when the rope runs through it steeper than the type's pitch cap (then <paramref name="bendDeg"/> = the pitch,
        ///     <paramref name="capDeg"/> = LiftTowerPlanConstants.PitchCapDeg, DECISIONS 600)
        ///   * TOWER_CLEARANCE when its steeper rope half is past the carrier-clearance limit of its head (then <paramref name="bendDeg"/>
        ///     = LiftPlanNode.HalfDeg, <paramref name="capDeg"/> = LiftTowerPlanConstants.WorldHalfMaxDeg(head), DECISIONS 600 c)</summary>
        public static string TowerKey(LiftTowerPlan plan, int node, bool pinned, out float heightM, out float clearM, out float bendDeg,
                                      out float capDeg)
        {
            LiftTowerPlanConstants c = plan.Constants;
            int m = plan.Nodes[node].Member;
            heightM = c.TopOf(m, plan.Nodes[node].Head);
            clearM = math.max(0f, c.RopeAboveBase(m) - c.CarrierDropM);
            bool over = plan.IsOverCap(node);
            bendDeg = over ? math.abs(plan.Nodes[node].DeflectionDeg) : 0f;
            capDeg = over ? plan.Nodes[node].CapDeg : 0f;
            if (plan.Spans[node - 1].FloorViolated || plan.Spans[node].FloorViolated)
                return "TOWER_LOW";
            if (over)
                return c.HasTrains ? "TOWER_TRAIN" : "TOWER_SADDLE";
            if (plan.IsTooSteep(node))
            {
                bendDeg = math.abs(plan.Nodes[node].PitchDeg);
                capDeg = c.PitchCapDeg;
                return "TOWER_PITCH";
            }
            if (plan.IsOverClearance(node))
            {
                bendDeg = plan.Nodes[node].HalfDeg;
                capDeg = c.WorldHalfMaxDeg(plan.Nodes[node].Head);
                return "TOWER_CLEARANCE";
            }
            return pinned ? "TOWER_PINNED" : "TOWER";
        }

        /// <summary>Carriers leaving a station end more than this below the ground ahead of it: the SUNK notices.</summary>
        public const float SunkWarnM = 0.25f;

        /// <summary>The sunk-end notice key, or null (FIX-LIST 55). A station end is sunk when its carriers leave more than SunkWarnM below
        /// the ground (LiftTowerPlan.EndCarrierA / B). A station end is a station's stub node, or a ground end, which plans with the
        /// station that will stand there: GroundEndRope. The key is SUNK when a built station is sunk. The key is SUNK_END when only a
        /// ground end is (no station stands there yet: past a crest vanilla stands it on the lower lot front, so its rope starts low). A
        /// loose end is a tower node and is never sunk.</summary>
        public static string SunkKey(LiftTowerPlan plan, LiftLineEndKind kindA, LiftLineEndKind kindB)
        {
            bool a = SunkEnd(plan.StationA, kindA, plan.EndCarrierA), b = SunkEnd(plan.StationB, kindB, plan.EndCarrierB);
            if ((a && kindA == LiftLineEndKind.Station) || (b && kindB == LiftLineEndKind.Station))
                return "SUNK";
            return a || b ? "SUNK_END" : null;
        }

        /// <summary>One end of SunkKey: a station end (not a loose end) whose carriers leave more than SunkWarnM below the ground.</summary>
        public static bool SunkEnd(bool stationEnd, LiftLineEndKind kind, float endCarrierM) =>
            stationEnd && kind != LiftLineEndKind.LooseEnd && endCarrierM < -SunkWarnM;

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
        public int[] Members;      // tool member per inner path node. -1 = extra split node (the fit gives it ChordMembers)
        public bool[] Pinned;
        public bool[] Extra;
        public int Registered;     // tool inner nodes found (= Line.Inner.Length)
        public int Extras;         // inner path nodes the tool did not emit
        public int Pins;           // pinned tool nodes found
        public bool Reversed;      // the path runs from the tool's last end to its first
    }

    /// <summary>In-session registry of lines built by the lift-line tool (EngineNotes/LiftLineEdit.md, Tool lines). The engine may build a
    /// tool course as more edges than emitted (CourseSplitSystem.CheckHeightRange elevation transitions, CourseSplitSystem.cs:2884-2942,
    /// and crossings). MatchSplit then recognizes the line when all of these are true:
    ///   * every emitted inner node has its own path node within MatchTolM, in order
    ///   * the path's ends are the tool's ends (EndTolM)
    ///   * every extra node lies on the tool's straight line (ExtraLateralTolM)</summary>
    public sealed class LiftToolLines
    {
        public const float MatchTolM = 0.5f;
        /// <summary>The path's end nodes vs the tool's emitted ends (station stub / loose end / fresh ground node: the same node, so 0 in
        /// practice). 2 m allows float noise and never confuses two different lines' ends.</summary>
        public const float EndTolM = 2f;
        /// <summary>An extra split node lies on the course, so on the tool's straight xz line. Farther off means another line.</summary>
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

        /// <summary>The registered line whose inner nodes are exactly these (xz within MatchTolM, same count, either direction), with its
        /// members / pins in the order of <paramref name="innerXz"/>. Null when none matches.</summary>
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

        /// <summary>The registered line this built path is, allowing extra engine split nodes. <paramref name="path"/> = every node of the
        /// path in order, ends included (world positions). Needs:
        ///   * the path's ends within EndTolM of the tool's ends (either direction)
        ///   * every tool inner node matched, in order, by its own path node within MatchTolM (xz)
        ///   * every other inner path node within ExtraLateralTolM of the tool's end-to-end xz line
        /// Newest line first. Null when none.</summary>
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
        /// <paramref name="path"/> (xz within MatchTolM, any order), <paramref name="found"/> / <paramref name="pins"/> of them. Null when
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

        /// <summary>Members for the extra split nodes of a matched tool line (members[i] &lt; 0), so the rope stays on the plan. The
        /// planned chord runs between the neighboring tool nodes' ropes (ground + RopeAboveBase(member), the station ropes at the ends). An
        /// extra node gets the member whose rope is nearest that chord (a tie goes to the taller: never a dip below the planned rope when a
        /// member sits exactly between). <paramref name="current"/> (the node's member now, -1 unknown) is kept while its rope is within
        /// ChordHystM of the best one's distance (no flip-flop on re-fits). Tool nodes are not touched. Pure. The fit then checks the
        /// clearance. The planner re-fills an extra that breaks it (LiftTowerPlanner.Fit spareNodes).</summary>
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

        /// <summary>The member whose rope (ground + RopeAboveBase) is nearest <paramref name="ropeY"/>. A tie (within ChordTolM) goes to
        /// the taller.</summary>
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
