// The carrier grip round in ChairPinSystem: each rostered carrier (the BODY) is paired with its GRIP, a second vanilla sub-object of the
// same edge. CarrierPose poses both (the grip pitched with the rope, the body plumb and swinging in the wind).
// Pairing: the grips are rows of the line prefab's NetSubObjects (prefab surgery after the art import) with the carrier row's EvenSpacing
// and spacing. So vanilla's EvenSpacing loop gives both the same slot m on their edge: Aligned.m_SubObjectIndex = m x rowCount + row
// (Game.Objects/SubObjectSystem.cs:1742-1757 in 1.6.2f1, rowCount = the line prefab's Game.Prefabs.SubObject buffer length, the loop's
// bufferData2). A body pairs with the grip of the same (owner edge, m, lane side). The side is the sign of the row's m_Position.x. A line
// prefab without grip rows (before the surgery) gives no grips. The bodies keep the rigid pose (one INFO per line prefab per load). Grips
// are NOT carriers (never in CarrierGripTops): no roster, census, count, hold or seeding.
// The static pin flow takes the body's pose from the same CarrierPose.Pose call as its pitched grip (LiftBatch CP-15). It assembles the
// grip on a body it leaves at vanilla's placement (CP-16). Contract breaks (two grips on a key, a grip two bodies pair with) keep the first
// and log once.
// Enrollment: a rostered grip gets InterpolatedTransform + one Updated exactly like its chair. An entity CREATED with InterpolatedTransform
// never enters the static search tree (the spike's step-0 finding). Vanilla creates the grip without it.
// Wind: WindSystem's map is sampled on the main thread per loop (the loop box center) every WindPollFrames render frames and at rebuilds
// (the map changes every 512 sim frames, WindSystem.cs:45). CarrierWind blends it per loop. The job gets the blended value per loop.
// Held loops are sampled and posed with the live clock too: their carriers keep swinging (DECISIONS 619, CarrierWind.SetHeld).
// Engine notes: EngineNotes/ChairPinSystem.Mover.md ("Grips and swing")

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class ChairPinSystem
    {
        // ---- the fallback hooks (the owner's order if the cost is over budget, plan section 4). NOT switched on: the field test flips
        // them
        /// <summary>Fallback (1): grips are posed and bounds-tagged only on near loops (closer than the far-loop pace boundary,
        /// LiftMoverMath FarMinM). A far loop's grips keep their last pose while the bodies keep moving.</summary>
        private static readonly bool GripNearOnly = false;
        /// <summary>Fallback (3): grips are written every second pass only.</summary>
        private static readonly bool GripHalfRate = false;
        /// <summary>The wind poll cadence in render frames: WindSystem.kUpdateInterval (WindSystem.cs:45 in 1.6.2f1).</summary>
        private const int WindPollFrames = 512;

        private WindSystem m_WindSystem;
        private readonly Dictionary<Entity, CarrierWind> m_WindByStation = new Dictionary<Entity, CarrierWind>();
        private uint m_WindPollFrame = uint.MaxValue;
        private bool m_WindErrorLogged;
        private bool m_GripHalfOdd;

        // the pairing index (one rebuild or static pass): (edge, slot, side) maps to grip. It is built per edge on first use
        private readonly Dictionary<GripKey, Entity> m_GripIndex = new Dictionary<GripKey, Entity>();
        private readonly HashSet<Entity> m_GripIndexedEdges = new HashSet<Entity>();
        private readonly Dictionary<Entity, bool> m_LineHasGripRows = new Dictionary<Entity, bool>();
        private readonly HashSet<Entity> m_NoGripRowsLogged = new HashSet<Entity>();   // line prefabs, per load
        // the grips rostered this rebuild. A grip paired twice would be written by two job indices at once (the Transform lookup's
        // NativeDisableParallelForRestriction relies on one writer per entity). So the second body stays rigid
        private readonly HashSet<Entity> m_GripRostered = new HashSet<Entity>();
        // the static pass's twin: grip maps to the body that posed it this pass
        private readonly Dictionary<Entity, Entity> m_GripStaticBody = new Dictionary<Entity, Entity>();
        private bool m_GripTwiceLogged;

        // diagnostics (the dump, the [cost] line)
        private int m_DiagGripsPaired, m_DiagGripsUnpaired, m_DiagGripEnrolled;
        private int m_PassGrips, m_PassGripWritten, m_PassGripTags;
        private long m_GripWrittenTotal, m_GripTagTotal;
        private int m_JobGripMoved, m_JobGripInPlace, m_JobGripMissing;
        private int m_LoggedGripsPaired = -1, m_LoggedGripsUnpaired = -1;

        private readonly struct GripKey : IEquatable<GripKey>
        {
            private readonly Entity m_Edge;
            private readonly int m_Slot, m_Side;

            public GripKey(Entity edge, int slot, int side)
            {
                m_Edge = edge;
                m_Slot = slot;
                m_Side = side;
            }

            public bool Equals(GripKey other) => m_Edge.Equals(other.m_Edge) && m_Slot == other.m_Slot && m_Side == other.m_Side;
            public override bool Equals(object obj) => obj is GripKey k && Equals(k);
            public override int GetHashCode() => (m_Edge.GetHashCode() * 397 ^ m_Slot) * 31 + m_Side;
        }

        /// <summary>The Options toggle (Animated lift carriers), read once per pass.</summary>
        private static bool AnimatedOn => SkiResortMod.Settings.SkiResortSettings.AnimatedLiftCarriersOn;

        private void GripsOnCreate()
        {
            m_WindSystem = World.GetOrCreateSystemManaged<WindSystem>();
        }

        /// <summary>Per load (MoverReset).</summary>
        private void GripsReset()
        {
            m_WindByStation.Clear();
            m_WindPollFrame = uint.MaxValue;
            m_WindErrorLogged = false;
            m_NoGripRowsLogged.Clear();
            m_GripTwiceLogged = false;
            GripIndexClear();
            m_DiagGripsPaired = m_DiagGripsUnpaired = m_DiagGripEnrolled = 0;
            m_PassGrips = m_PassGripWritten = m_PassGripTags = 0;
            m_GripWrittenTotal = m_GripTagTotal = 0;
            m_JobGripMoved = m_JobGripInPlace = m_JobGripMissing = 0;
            m_LoggedGripsPaired = m_LoggedGripsUnpaired = -1;
        }

        /// <summary>Forget the pairing index (entities change between passes: every rebuild and static pass starts fresh).</summary>
        private void GripIndexClear()
        {
            m_GripIndex.Clear();
            m_GripIndexedEdges.Clear();
            m_LineHasGripRows.Clear();
            m_GripRostered.Clear();
            m_GripStaticBody.Clear();
        }

        /// <summary>The lift type's grip prefab is in the game (SkiPrefabCatalogSystem.GripTypes): its carriers are bodies.</summary>
        private static bool IsSplit(IReadOnlyDictionary<Entity, LiftTypeDef> gripTypes, LiftTypeDef def)
        {
            if (def == null || gripTypes.Count == 0)
                return false;
            foreach (LiftTypeDef g in gripTypes.Values)
            {
                if (ReferenceEquals(g, def))
                    return true;
            }
            return false;
        }

        /// <summary>The grip paired with <paramref name="body"/>, or Entity.Null (no Aligned, no line rows, no grip rows, not spawned).</summary>
        private Entity FindGrip(Entity body, LiftTypeDef def, IReadOnlyDictionary<Entity, LiftTypeDef> gripTypes)
        {
            if (!IsSplit(gripTypes, def) ||
                !EntityManager.TryGetComponent(body, out Owner owner) ||
                !EntityManager.TryGetComponent(body, out Aligned aligned) ||
                !EntityManager.TryGetComponent(owner.m_Owner, out PrefabRef edgePrefab) ||
                !EntityManager.TryGetBuffer(edgePrefab.m_Prefab, isReadOnly: true, out DynamicBuffer<Game.Prefabs.SubObject> rows) ||
                rows.Length == 0)
                return Entity.Null;
            int row = CarrierPose.AlignRow(aligned.m_SubObjectIndex, rows.Length);
            if (row < 0 || row >= rows.Length)
                return Entity.Null;
            if (!LineHasGripRows(edgePrefab.m_Prefab, rows, gripTypes, def))
                return Entity.Null;
            IndexEdgeGrips(owner.m_Owner, rows, gripTypes);
            var key = new GripKey(owner.m_Owner, CarrierPose.AlignSlot(aligned.m_SubObjectIndex, rows.Length),
                                  CarrierPose.RowSide(rows[row].m_Position.x));
            return m_GripIndex.TryGetValue(key, out Entity grip) ? grip : Entity.Null;
        }

        /// <summary>Does the line prefab carry grip rows? Before the surgery it does not: one INFO per line prefab per load.</summary>
        private bool LineHasGripRows(Entity linePrefab, DynamicBuffer<Game.Prefabs.SubObject> rows,
                                     IReadOnlyDictionary<Entity, LiftTypeDef> gripTypes, LiftTypeDef def)
        {
            if (m_LineHasGripRows.TryGetValue(linePrefab, out bool has))
                return has;
            has = false;
            for (int r = 0; r < rows.Length && !has; r++)
                has = gripTypes.ContainsKey(rows[r].m_Prefab);
            m_LineHasGripRows[linePrefab] = has;
            if (!has && m_NoGripRowsLogged.Add(linePrefab))
                Mod.Log.Info($"{nameof(ChairPinSystem)} [grips]: the '{def.Id}' line prefab has no grip rows (grip '{def.GripPrefab}' found, " +
                             "its NetSubObjects rows not added yet): its carriers keep the rigid one-piece pose");
            return has;
        }

        /// <summary>Indexes every grip sub-object of one edge (once per edge per pass).</summary>
        private void IndexEdgeGrips(Entity edge, DynamicBuffer<Game.Prefabs.SubObject> rows, IReadOnlyDictionary<Entity, LiftTypeDef> gripTypes)
        {
            if (!m_GripIndexedEdges.Add(edge))
                return;
            if (!EntityManager.TryGetBuffer(edge, isReadOnly: true, out DynamicBuffer<Game.Objects.SubObject> subs))
                return;
            for (int i = 0; i < subs.Length; i++)
            {
                Entity e = subs[i].m_SubObject;
                if (!EntityManager.TryGetComponent(e, out PrefabRef pr) || !gripTypes.ContainsKey(pr.m_Prefab) ||
                    !EntityManager.TryGetComponent(e, out Aligned a) || EntityManager.HasComponent<Deleted>(e))
                    continue;
                int row = CarrierPose.AlignRow(a.m_SubObjectIndex, rows.Length);
                if (row < 0 || row >= rows.Length || rows[row].m_Prefab != pr.m_Prefab)
                    continue;
                var key = new GripKey(edge, CarrierPose.AlignSlot(a.m_SubObjectIndex, rows.Length), CarrierPose.RowSide(rows[row].m_Position.x));
                // Two grips on one key (two grip rows on one side of the line prefab, a contract break). The first keeps the key. The other
                // is never posed and stays at its own spawn (vanilla's placement), never mid-span where a body left it. Logged once.
                if (m_GripIndex.TryGetValue(key, out Entity first) && first != e)
                {
                    LogGripContract($"two grips ({first}, {e}) on one slot and side of edge {edge}: two grip rows on one side of the line " +
                                    "prefab? The second stays at its spawn");
                    continue;
                }
                m_GripIndex[key] = e;
            }
        }

        /// <summary>One WARN per load for a grip pairing that breaks the prefab contract (one carrier row and one grip row per side).</summary>
        private void LogGripContract(string what)
        {
            if (m_GripTwiceLogged)
                return;
            m_GripTwiceLogged = true;
            Mod.Log.Warn($"{nameof(ChairPinSystem)} [grips]: {what} (logged once per load)");
        }

        /// <summary>Roster: pair the chair just added to <paramref name="home"/> and enroll its grip like the chair.</summary>
        private void RosterGrip(LiftLoopCache.Loop home, Entity chair, LiftTypeDef def, IReadOnlyDictionary<Entity, LiftTypeDef> gripTypes)
        {
            Entity grip = FindGrip(chair, def, gripTypes);
            if (grip != Entity.Null && !m_GripRostered.Add(grip))
            {
                // The grip stays with its first body (posed every pass). This body keeps the rigid pose.
                LogGripContract($"grip {grip} pairs with two carriers (two carrier rows on one side of the '{def.Id}' line prefab?); it rides " +
                                "with the first, the second keeps the rigid pose");
                grip = Entity.Null;
            }
            home.m_Grips.Add(grip);
            home.m_SwayPhases.Add(CarrierPose.SeedPhase((uint)chair.Index));
            if (grip == Entity.Null)
            {
                if (IsSplit(gripTypes, def))
                    m_DiagGripsUnpaired++;
                return;
            }
            home.m_GripCount++;
            m_DiagGripsPaired++;
            if (!EntityManager.HasComponent<InterpolatedTransform>(grip))
            {
                EntityManager.AddComponentData(grip, new InterpolatedTransform(EntityManager.GetComponentData<Transform>(grip)));
                m_DiagGripEnrolled++;
            }
            if (!EntityManager.HasComponent<Updated>(grip) && !EntityManager.HasComponent<Created>(grip))
                EntityManager.AddComponent<Updated>(grip);
        }

        /// <summary>The loop's wind state, kept per base station across rebuilds.</summary>
        private CarrierWind WindOf(Entity baseStation)
        {
            if (!m_WindByStation.TryGetValue(baseStation, out CarrierWind w))
            {
                w = new CarrierWind();
                m_WindByStation.Add(baseStation, w);
            }
            return w;
        }

        /// <summary>A loop whose wind this poll can sample: split, with a known box. A held loop too: its carriers keep swinging with the
        /// live wind (DECISIONS 619).</summary>
        private static bool WindSamplable(LiftLoopCache.Loop loop)
        {
            return loop.m_GripCount > 0 && loop.m_Wind != null && loop.m_BoxKnown;
        }

        /// <summary>Samples WindSystem's map at every samplable split loop (main thread, the writer completed first). It samples every
        /// WindPollFrames render frames, now when <paramref name="force"/> (a rebuild), and at once while any samplable loop's wind is
        /// still unknown (a new loop, a load's first passes). It runs before the pass poses, so the first sample is the start value
        /// (CarrierWind.Set). The cadence is stamped only when something was sampled. Never throws.</summary>
        private void PollWind(double timeS, bool force)
        {
            uint frame = m_RenderingSystem.frameIndex;
            bool due = force || m_WindPollFrame == uint.MaxValue || frame < m_WindPollFrame || frame - m_WindPollFrame >= WindPollFrames;
            int samplable = 0;
            bool anyUnknown = false;
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                if (!WindSamplable(loop))
                    continue;
                samplable++;
                anyUnknown |= !loop.m_Wind.m_Known;
            }
            if (samplable == 0 || m_WindSystem == null || !(due || anyUnknown))
                return;
            try
            {
                // CellMapSystem.GetMap (CellMapSystem.cs:154-158): readOnly gives the writers' handle. It is completed here, so the read is
                // synchronous and needs no AddReader. WindSystem.GetWind (WindSystem.cs:68-80) = the bilinear sample SampleWind does.
                NativeArray<Wind> map = m_WindSystem.GetMap(readOnly: true, out JobHandle deps);
                deps.Complete();
                if (!map.IsCreated || map.Length == 0)
                    return;   // not stamped: the next pass tries again
                for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
                {
                    LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                    if (!WindSamplable(loop))
                        continue;
                    float3 center = 0.5f * (loop.m_BoxMin + loop.m_BoxMax);
                    loop.m_Wind.Set(WindSystem.GetWind(center, map).m_Wind, timeS);
                }
                m_WindPollFrame = frame;
            }
            catch (Exception e)
            {
                m_WindPollFrame = frame;   // a failing read is retried at the cadence, not every frame
                if (!m_WindErrorLogged)
                {
                    m_WindErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(ChairPinSystem)} [grips]: reading the wind map failed ({e.GetType().Name}: {e.Message}); " +
                                  "the carriers keep their last wind (logged once per load)");
                }
            }
        }

        /// <summary>The per-loop swing inputs of one pass. <paramref name="swayShift"/> = CarrierPose.SwayShift of the loop's sway clock
        /// (added to every carrier's phase). <paramref name="swayScale"/> = its amplitude envelope (DECISIONS 619).</summary>
        private static void SwingInputs(LiftLoopCache.Loop loop, double timeS, out CarrierPivots pivots, out float omega, out float rampM,
                                        out float2 wind, out SwingLimits limits, out float2 gustClock, out float swayShift, out float swayScale)
        {
            LiftTypeDef def = SkiCatalog.LiftByIndex(loop.m_TypeIndex);
            pivots = def != null ? def.Pivots : default;
            limits = def != null ? CarrierPose.Limits(pivots, def.Mover == MoverKind.Shuttle) : default;
            omega = CarrierPose.Omega(pivots.BodyPin.y);
            rampM = CarrierPose.RampM((float)loop.m_SpeedMps);
            wind = loop.m_Wind != null ? loop.m_Wind.At(timeS) : float2.zero;
            gustClock = CarrierPose.GustClock(timeS);
            swayShift = loop.m_Wind != null ? CarrierPose.SwayShift(omega, loop.m_Wind.SwayTimeS(timeS)) : 0f;
            swayScale = loop.m_Wind != null ? loop.m_Wind.SwayScale(timeS) : 1f;
        }

        /// <summary>The managed twin of the job's split pose (a held loop that does not swing, the managed A/B path). It writes the body
        /// and its grip. <paramref name="timeS"/> = the mover clock (the wind, the gusts and the sway clock are live, also on a held
        /// loop).</summary>
        private MoveResult MoveCarrier(LiftLoopCache.Loop loop, int i, double s, double timeS, bool animated, bool writeGrip)
        {
            Entity grip = i < loop.m_Grips.Count ? loop.m_Grips[i] : Entity.Null;
            if (grip == Entity.Null)
                return MoveChair(loop.m_Chairs[i], loop.m_Drops[i], loop, s);
            Entity chair = loop.m_Chairs[i];
            if (!EntityManager.Exists(chair) ||
                EntityManager.HasComponent<Deleted>(chair) ||
                !EntityManager.TryGetComponent<Transform>(chair, out var transform))
                return MoveResult.Missing;

            LiftLoopCache.Sample(loop, s, out float3 rope, out float3 travelTangent, out bool guided, out float distance);
            quaternion yaw = Game.Net.NetUtils.GetNodeRotation(travelTangent, transform.m_Rotation);
            SwingInputs(loop, timeS, out CarrierPivots pivots, out float omega, out float rampM, out float2 wind, out SwingLimits limits,
                        out float2 gustClock, out float swayShift, out float swayScale);
            float2 swing = float2.zero;
            if (animated && !guided)
            {
                float2 ramp = CarrierPose.Ramp(distance, rampM);
                float phase = i < loop.m_SwayPhases.Count ? loop.m_SwayPhases[i] : 0f;
                swing = CarrierPose.Swing(wind, yaw, CarrierPose.SwayArg(s, omega, (float)loop.m_SpeedMps, phase + swayShift), ramp, limits,
                                          CarrierPose.Gust(gustClock, rope), swayScale);
            }
            CarrierPose.Pose(rope, travelTangent, yaw, pivots, animated, swing,
                             out float3 gripPosition, out quaternion gripRotation, out float3 bodyPosition, out quaternion bodyRotation);
            if (writeGrip && !WriteGrip(grip, gripPosition, gripRotation, tagUpdated: false, interpolated: false))
                m_LoopCacheDirty = true;   // the grip died: heal at the next rebuild, as the job's grip Missing does
            if (math.distancesq(bodyPosition, transform.m_Position) < 1e-6f &&
                math.abs(math.dot(bodyRotation, transform.m_Rotation)) > 0.99999f)
                return MoveResult.InPlace;
            transform.m_Position = bodyPosition;
            transform.m_Rotation = bodyRotation;
            EntityManager.SetComponentData(chair, transform);
            return MoveResult.Moved;
        }

        /// <summary>One grip write with the mover's idempotence. The static flow also refreshes InterpolatedTransform (when present) and
        /// tags Updated. Returns false when the grip is gone.</summary>
        private bool WriteGrip(Entity grip, float3 position, quaternion rotation, bool tagUpdated, bool interpolated)
        {
            if (!EntityManager.Exists(grip) || EntityManager.HasComponent<Deleted>(grip) ||
                !EntityManager.TryGetComponent<Transform>(grip, out var transform))
                return false;
            if (math.distancesq(position, transform.m_Position) < 1e-6f &&
                math.abs(math.dot(rotation, transform.m_Rotation)) > 0.99999f)
                return true;
            transform.m_Position = position;
            transform.m_Rotation = rotation;
            EntityManager.SetComponentData(grip, transform);
            if (interpolated && EntityManager.TryGetComponent<InterpolatedTransform>(grip, out var interp))
            {
                interp.m_Position = position;
                interp.m_Rotation = rotation;
                EntityManager.SetComponentData(grip, interp);
            }
            if (tagUpdated && !EntityManager.HasComponent<Updated>(grip) && !EntityManager.HasComponent<Created>(grip))
                EntityManager.AddComponent<Updated>(grip);
            return true;
        }

        /// <summary>The static pin flow's grip (TryPinChair: unclosed lifts, the load pass, Temp tool previews): a pitched grip over a
        /// plumb, still body. True when the carrier has a grip. Then <paramref name="bodyPosition"/> / <paramref name="bodyRotation"/> are
        /// the body's pose from the SAME CarrierPose.Pose call (zero swing). TryPinChair writes this pose instead of the rigid pose, so the
        /// pin joint holds on a pitched span (CP-15). False: no grip, and TryPinChair keeps the rigid pose.</summary>
        private bool PinGripOf(Entity chair, LiftTypeDef def, float3 rope, float3 tangent, quaternion yaw, bool tagUpdated,
                               out float3 bodyPosition, out quaternion bodyRotation)
        {
            bodyPosition = default;
            bodyRotation = yaw;
            Entity grip = StaticGripOf(chair, def);
            if (grip == Entity.Null)
                return false;
            CarrierPose.Pose(rope, tangent, yaw, def.Pivots, AnimatedOn, float2.zero,
                             out float3 gp, out quaternion gr, out bodyPosition, out bodyRotation);
            WriteGrip(grip, gp, gr, tagUpdated, interpolated: true);
            return true;
        }

        /// <summary>A carrier the static pass leaves where vanilla placed it (no cable lane, lane too far). Its grip is assembled on the
        /// body where the body stands (CarrierPose.GripOnBody, the tram's parked carriage rule, CP-16), never left at its own
        /// spawn.</summary>
        private void ParkGripOnBody(Entity chair, LiftTypeDef def, bool tagUpdated)
        {
            Entity grip = StaticGripOf(chair, def);
            if (grip == Entity.Null || !EntityManager.TryGetComponent<Transform>(chair, out var body))
                return;
            CarrierPose.GripOnBody(body.m_Position, body.m_Rotation, def.Pivots, out float3 gp, out quaternion gr);
            WriteGrip(grip, gp, gr, tagUpdated, interpolated: true);
        }

        /// <summary>The grip the static pass may pose for <paramref name="chair"/>: its pairing, unless another body took it earlier in
        /// this pass (two carrier rows on one side, a contract break, logged once). The first body keeps it. This one stays
        /// rigid.</summary>
        private Entity StaticGripOf(Entity chair, LiftTypeDef def)
        {
            IReadOnlyDictionary<Entity, LiftTypeDef> gripTypes = m_PrefabCatalog.GripTypes;
            if (!IsSplit(gripTypes, def))
                return Entity.Null;
            Entity grip = FindGrip(chair, def, gripTypes);
            if (grip == Entity.Null)
                return Entity.Null;
            if (m_GripStaticBody.TryGetValue(grip, out Entity first) && first != chair)
            {
                LogGripContract($"grip {grip} pairs with two carriers ({first}, {chair}; two carrier rows on one side of the '{def.Id}' " +
                                "line prefab?); it stays with the first, the second keeps the rigid pose");
                return Entity.Null;
            }
            m_GripStaticBody[grip] = chair;
            return grip;
        }

        /// <summary>A seeded chair's rope point from its grip's jaw (exact under any body swing). False = no grip (seed from the
        /// body).</summary>
        private bool RopePointOfGrip(LiftLoopCache.Loop loop, int i, out float3 rope)
        {
            rope = default;
            Entity grip = i < loop.m_Grips.Count ? loop.m_Grips[i] : Entity.Null;
            if (grip == Entity.Null || !EntityManager.TryGetComponent<Transform>(grip, out var t))
                return false;
            LiftTypeDef def = SkiCatalog.LiftByIndex(loop.m_TypeIndex);
            if (def == null)
                return false;
            rope = t.m_Position + math.rotate(t.m_Rotation, def.Pivots.GripRope);
            return true;
        }

        /// <summary>The grips of a loop that can take an Updated tag now (CollectChairsToTag's twin).</summary>
        private void CollectGripsToTag(LiftLoopCache.Loop loop, NativeList<Entity> into)
        {
            for (int i = 0; i < loop.m_Grips.Count; i++)
            {
                Entity grip = loop.m_Grips[i];
                if (grip != Entity.Null &&
                    EntityManager.Exists(grip) &&
                    !EntityManager.HasComponent<Deleted>(grip) &&
                    !EntityManager.HasComponent<Updated>(grip) &&
                    !EntityManager.HasComponent<Created>(grip))
                {
                    into.Add(grip);
                }
            }
        }
    }
}
