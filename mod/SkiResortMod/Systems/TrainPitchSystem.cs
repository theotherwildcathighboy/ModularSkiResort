// Phase 2 of the lift alignment fix (docs: lift-alignment-2026-11-02, DECISIONS 570): a line tower's sheave trains pitch with the rope.
// CS2 draws each span's rope straight to the tower's anchor, so the rope corners there. A rigid train cannot seat its wheels on a corner.
// The trains are articulated (option C of DECISIONS 570): two half-trains hinged on the main pin, which lies on the anchor, each
// pitched to its own span's rope. The tower mesh is skinned with bones "TrainFront*" (the half on the tower's +z side) and "TrainBack*"
// (-z). Each bone's origin is the anchor its rope corners at (a second rope on the same half, the tram's haul rope, gets its own bone,
// e.g. "TrainFrontHaul"). The pitch is visual only. The tower Transform stays yaw-only (AlignSystem resets it on every node update) unless
// TowerLeanSystem leans it (DECISIONS 600). The rope lanes keep anchoring on the prefab's sub-lane ends, which the bones turn about.
//   TrainPitchSystem (Modification4B, after LiftCensusSystem): per tower with train bones, it takes the pitches from the rope lanes the
//     engine built (LaneSystem runs at Modification4). Every lift-edge rope lane end on the tower's anchors gives its angle along the
//     tower's +z (LiftAlignMath.LaneEndPitch). The lanes leaving toward +z average to the front pitch, those toward -z to the back pitch
//     (LiftAlignMath.HalfPitches: each clamped to TrainPitchLimit, the type's steepest rope but at most 30 deg. The half-break is clamped
//     to MaxHalfBreakDeg). The system keeps the result in TrainPitch and the TrainBonePose buffer (not saved: the first census of a load
//     recomputes every tower).
//   Wide tram saddles (DECISIONS 597): a tower whose mesh is a tram tower of art letter D or later (LiftAlignMath.WideTramSaddle on
//     the RenderPrefab name) takes the tram saddle's limits instead (LiftAlignMath.TrainLimits: each half to TramSaddlePitchLimitDeg
//     60, the half-break to TramHalfBreakMaxDeg 30). The letter C meshes keep 30 / 20, so this code never folds them past their art.
//   Train limits (hub row LiftTrainLimit, 2026-11-03): the same pass measures every line tower with anchors, rigged or not, by how far its
//     rope asks past those limits (LiftAlignMath.TrainOverDeg on the unclamped pitches). A tower over TrainWarnDeg enters TrainOver. When
//     that set changes the pass asks LiftCensusSystem for a coalesced build, which reads it into LiftCensus.Node.TrainOverDeg.
//   TrainBoneSystem (Rendering, after InitializeBonesSystem): writes the bones. Skeletons exist only near the camera. They come back at rest
//     on every (re)allocation (InitializeBonesSystem.cs:57-70, :161-171). Vanilla never writes a None bone (ObjectInterpolateSystem
//     AnimateStaticBone has no case for it). So a Burst job re-applies the rotations each frame where they differ.
// The bone indices come from the mesh's ProceduralAnimationProperties by name (bone names are not kept at runtime). The engine builds the
// ProceduralBone buffer in m_Bones order with m_BindIndex = that index (MeshSystem.cs:549-562). Then it sorts the buffer by hierarchy
// depth, parent and source (InitializeMeshJob, MeshSystem.cs:162-164, :237). Sibling order is not kept, so the slot is the one whose
// m_BindIndex matches. The Bone buffer follows the sorted slots (ProceduralSkeletonSystem.GetSkinMatrices). The Skeleton index is the
// prefab SubMesh index (ObjectInterpolateSystem.cs:344-351).
//   Head (DECISIONS 594, spike tilted-towers-2026-11-04 option B): a DQ / GD mesh with a bone named "Head*" (child of Root, origin on the
//     same pivot line as the halves) is headed. The head (gantry, deck, trains' carrier) turns to LiftAlignMath.HeadPitch: the bisector
//     of the ropes clamped to +-HeadRelMaxDeg (20) relative to the mast (DECISIONS 600). 594's MaxTrainPitch head limits DQ 42.0 /
//     GD 50.2 went. The halves turn to the head +- MaxHalfBreakDeg (HeadedHalfPitches). Its train limit is HeadedTrainOverDeg. Head
//     and halves are siblings on the mast. A pose change re-arms TowerNumberTileSystem, which poses the tiles (and the GD beacon)
//     through the head.
//   Mast lean (DECISIONS 600): TowerLeanSystem leans a steep tower's mast about its foot. Every bone is local to the mast, so the pass
//     takes the mast's pitch (LiftAlignMath.MastPitch of the tower rotation, 0 on a plumb tower) off the absolute lane-end pitches
//     before every limit above. So the train limits and the hub row are relative to the mast.
//   Padded face uphill (DECISIONS 600 e): TowerLeanSystem turns a tower 180 deg where vanilla's yaw put its +z (pad, TrainFront)
//     downhill. Nothing here depends on the yaw. Front / back, the pitches and MastPitch are all read along the tower's current +z.
//     So on a turned tower TrainFront is the uphill half, and both halves read nose up (LiftBatch PF-3).
//   Carrier clearance (DECISIONS 600 c): the carrier hangs plumb in the world. So the system clamps, last, a half whose WORLD pitch
//     (half + mast) passes the art's limit for the tower's family and head (LiftAlignMath.WorldHalfMaxDeg: FG support / Break 41.5,
//     GD support 55, GD Hold 45, DQ Hold 57.5. The head comes from the prefab name, LiftTowerPlanConstants.HeadOfTwin).
//     The clamp is LiftAlignMath.PoseTrain: one rule for the system and LiftBatch PC-9. Therefore a built tower past the limit never
//     renders the carrier through its train. Its excess joins the train limit (hub row LiftTrainLimit).
//   Stations (T256): the line's end node at a station is owned by the station, and the first span's rope corners at the station's
//     anchors. A station mesh with bones "NoseTrain*" (the outer half of its nose train: origin on the anchor, rest pose along the
//     in-station rope) pitches that half to the line rope (LiftAlignMath.StationNosePose: the tower limits, the inner half is static
//     on the in-station rope). A hosted end tower with train bones (the fixed-grip top's) takes the tower rule, with the in-station
//     rope (anchor to the bullwheel's tangent point, from the catalog's WheelLocal) as the rope of its station side. An over-limit
//     station is logged only: it is not in the hub row. A station-side entity that loses its line goes back to rest.
// Never: Temp or Deleted towers (a preview stays level), towers whose prefab has no train bone (unskinned art: nothing changes), Editor.
// Log discipline: one line per rigged tower prefab per load, one line per pass that posed a tower or changed the rope-less count (with
// the towers whose break was clamped), a one-shot "!!!" catch.
// Mod.cs: UpdateAfter<TrainPitchSystem, LiftCensusSystem>(Modification4B).
// Mod.cs: UpdateAfter<TrainBoneSystem, InitializeBonesSystem>(Rendering).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using SubLane = Game.Net.SubLane;
using SubObject = Game.Objects.SubObject;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    /// <summary>A rigged line tower's half-train pitches (runtime only, rebuilt from the rope lanes). The bone rotations are in its
    /// TrainBonePose buffer.</summary>
    public struct TrainPitch : IComponentData
    {
        public float m_Front;           // radians, nose up along the tower's +z, of the +z half (TrainFront bones)
        public float m_Back;            // the same for the -z half (TrainBack bones)
        public bool m_Clamped;          // the half-break was over LiftAlignMath.MaxHalfBreakDeg (diagnostics)
        public bool m_Headed;           // the mesh has a Head bone: m_Head / m_HeadPivot / m_HeadFrame are valid
        public float m_Head;            // radians, nose up along +z, of the head (LiftAlignMath.HeadPitch)
        public float3 m_HeadPivot;      // the Head bone's origin in the prefab frame (ProceduralBone.m_ObjectPosition)
        public quaternion m_HeadFrame;  // its rest rotation in the prefab frame (ProceduralBone.m_ObjectRotation)
        public float m_Nose;            // a station's NoseTrain bones: radians, nose up along +z, relative to their rest pose (T256)
    }

    /// <summary>One train bone's local rotation: rest turned by its half's pitch (LiftAlignMath.TrainBoneRotation).</summary>
    [InternalBufferCapacity(4)]
    public struct TrainBonePose : IBufferElementData
    {
        public quaternion m_Rotation;
        public int m_SubMesh;           // prefab SubMesh index = Skeleton index of the mesh holding the bone
        public int m_Bone;              // bone index in that mesh (the sorted ProceduralBone slot)
    }

    public sealed partial class TrainPitchSystem : GameSystemBase
    {
        /// <summary>A lane end this close to one of the tower's anchors is that anchor's rope (FindAnchor snaps exactly, U1: 0.0000 m).</summary>
        internal const float AnchorMatchM = 0.25f;

        /// <summary>Below this change, radians, a pass leaves a tower's pose alone (no rewrite churn on re-runs).</summary>
        internal const float PitchEpsilon = 1e-4f;

        internal const string FrontPrefix = "TrainFront", BackPrefix = "TrainBack", HeadPrefix = "Head";

        /// <summary>A station mesh's nose train bones (T256): the outer half-train on the line side of the station anchor. The bone's
        /// origin is that anchor, its rest pose lies along the in-station rope.</summary>
        internal const string NosePrefix = "NoseTrain";

        private const int PartBack = 0, PartFront = 1, PartHead = 2, PartNose = 3;

        /// <summary>One pass's counters (the log line).</summary>
        private struct PassStats
        {
            public int Towers, NoRope, Clamped, Headed, HeadsPosed, WorldHeld, Stations, StationsOver;
            public float MaxDeg, MaxHalfBreakDeg, MaxHeadDeg, MaxLeanDeg, MaxWorldDeg, MaxNoseDeg, StationWorstOver;
        }

        /// <summary>A rope lane end on an anchor: the end point and the lane's next control point (LiftAlignMath.LaneEndPitch).</summary>
        private struct RopeEnd
        {
            public float3 End, Inner;
        }

        private struct RigBone
        {
            public int SubMesh, Bone, Part;
            public quaternion Rest;
            public float3 ObjectPosition;
            public quaternion ObjectRotation;
        }

        private struct Pending
        {
            public Entity Tower;
            public TrainPitch Pitch;
            public List<RigBone> Rig;
            public bool Add;
        }

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_UpdatedEdges;
        private bool m_Armed, m_Disabled, m_Dirty;
        private string m_DirtyWhy = "load";
        private readonly List<LiftCensus.Edge> m_Edges = new List<LiftCensus.Edge>();
        private readonly Dictionary<Entity, List<RigBone>> m_Rigs = new Dictionary<Entity, List<RigBone>>();   // null: no train bone
        private readonly HashSet<Entity> m_WideTram = new HashSet<Entity>();   // rigged prefabs with a wide tram saddle mesh (RigOf)
        private readonly Dictionary<Entity, float> m_WorldMax = new Dictionary<Entity, float>();   // per tower prefab: WorldMaxOf
        private readonly HashSet<Entity> m_SeenNodes = new HashSet<Entity>();
        private readonly List<float3> m_Anchors = new List<float3>(8);
        private readonly List<Pending> m_Pending = new List<Pending>();
        // The station visit (T256): the stations seen this pass, the line rope ends on the station's anchors, the in-station rope from
        // each of those anchors to the bullwheel's tangent point, and the station-side entities (a station with nose bones, its hosted
        // towers with train bones) posed by this and by the last pass (one that lost its line goes back to rest).
        private readonly HashSet<Entity> m_SeenStations = new HashSet<Entity>();
        private readonly List<RopeEnd> m_Ends = new List<RopeEnd>(8);
        private readonly List<RopeEnd> m_Fixed = new List<RopeEnd>(4);
        private HashSet<Entity> m_StationSide = new HashSet<Entity>();
        private HashSet<Entity> m_StationSideNext = new HashSet<Entity>();
        private int m_Passes, m_LastNoRope;
        // Per line node, the degrees its tower's rope asks past the train limits (LiftAlignMath.TrainOverDeg over TrainWarnDeg), for every
        // line tower with anchors, rigged or not. LiftCensusSystem reads it into LiftCensus.Node.TrainOverDeg (the hub's LiftTrainLimit).
        private Dictionary<Entity, float> m_Over = new Dictionary<Entity, float>();
        private Dictionary<Entity, float> m_OverNext = new Dictionary<Entity, float>();
        private LiftCensusSystem m_Census;

        /// <summary>The line nodes whose tower's rope goes past what its train follows, and by how many degrees (last pass).</summary>
        internal IReadOnlyDictionary<Entity, float> TrainOver => m_Over;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            m_UpdatedEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            Mod.Log.Info($"{nameof(TrainPitchSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_Dirty = false;
            m_Edges.Clear();
            m_Rigs.Clear();
            m_WideTram.Clear();
            m_WorldMax.Clear();
            m_Passes = 0;
            m_LastNoRope = 0;
            m_Over.Clear();
            m_StationSide.Clear();
            m_StationSideNext.Clear();
        }

        /// <summary>Called by LiftCensusSystem after each build with its line edges (main thread, no ECS read here).</summary>
        internal void OnCensusBuilt(List<LiftCensus.Edge> edges)
        {
            if (!m_Armed || m_Disabled)
                return;
            m_Edges.Clear();
            m_Edges.AddRange(edges);
            Arm("census");
        }

        private void Arm(string why)
        {
            if (!m_Dirty)
                m_DirtyWhy = why;
            m_Dirty = true;
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                // A tower move or a span edit updates the lift edges (their lanes are rebuilt this frame at Modification4). The census
                // only rebuilds on Created / Deleted edges.
                if (!m_Dirty && m_Edges.Count > 0 && !m_UpdatedEdges.IsEmptyIgnoreFilter && LiftEdgeUpdated())
                    Arm("edge updated");
                if (!m_Dirty)
                    return;
                m_Dirty = false;
                Pass(m_DirtyWhy);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! Towers [trains]: {e.GetType().Name}: {e.Message} — disabled for this load (trains keep their last pose)\n{e}");
            }
        }

        private bool LiftEdgeUpdated()
        {
            IReadOnlyDictionary<Entity, LiftTypeDef> lines = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>().LiftLineTypes;
            NativeArray<Entity> upd = m_UpdatedEdges.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < upd.Length; i++)
                    if (lines.ContainsKey(EntityManager.GetComponentData<PrefabRef>(upd[i]).m_Prefab))
                        return true;
                return false;
            }
            finally
            {
                upd.Dispose();
            }
        }

        private void Pass(string why)
        {
            long t0 = Stopwatch.GetTimestamp();
            m_Passes++;
            m_SeenNodes.Clear();
            m_SeenStations.Clear();
            m_StationSideNext.Clear();
            m_Pending.Clear();
            m_OverNext.Clear();
            var st = default(PassStats);
            // Read everything first: the component adds below are structural changes, which would invalidate the node buffers read here.
            for (int i = 0; i < m_Edges.Count; i++)
            {
                LiftCensus.Edge ce = m_Edges[i];
                Entity edge = LiftCensusSystem.Ent(ce.Id);
                if (ce.Type == LiftCensus.NoType || ce.Type >= SkiCatalog.Lifts.Count || !EntityManager.Exists(edge) ||
                    EntityManager.HasComponent<Deleted>(edge) || !EntityManager.TryGetComponent(edge, out Edge e))
                    continue;                             // a newer census build carries the replacement
                float slopeMax = SkiCatalog.Lifts[ce.Type].Envelope.SlopeMax;
                string typeId = SkiCatalog.Lifts[ce.Type].Id;
                float headMax = LiftAlignMath.HeadRelMax;   // relative to the (leaned) mast, DECISIONS 600 (594's MaxTrainPitch went)
                for (int end = 0; end < 2; end++)
                {
                    Entity node = end == 0 ? e.m_Start : e.m_End;
                    if (!m_SeenNodes.Add(node))
                        continue;
                    if (EntityManager.TryGetBuffer(node, true, out DynamicBuffer<SubObject> subs))
                        for (int s = 0; s < subs.Length; s++)
                        {
                            Entity tower = subs[s].m_SubObject;
                            if (EntityManager.HasComponent<Temp>(tower) || EntityManager.HasComponent<Deleted>(tower) ||
                                !EntityManager.TryGetComponent(tower, out PrefabRef pr) || !EntityManager.TryGetComponent(tower, out Transform t))
                                continue;
                            bool roped = RopeAt(node, pr.m_Prefab, t, out float meanF, out int nF, out float meanB, out int nB);
                            VisitTower(node, tower, pr.m_Prefab, t, roped, meanF, nF, meanB, nB, slopeMax, typeId, headMax, ref st);
                        }
                    // The line's end node at a station is owned by the station (its stub net): the station's nose train and its hosted
                    // end tower follow the first span's rope (T256).
                    if (EntityManager.TryGetComponent(node, out Owner owner))
                        VisitStation(node, owner.m_Owner, slopeMax, typeId, headMax, ref st);
                }
            }
            // A station-side entity posed by the last pass that no line reaches any more goes back to its rest pose
            foreach (Entity gone in m_StationSide)
            {
                if (m_StationSideNext.Contains(gone) || !EntityManager.Exists(gone) || EntityManager.HasComponent<Deleted>(gone) ||
                    !EntityManager.TryGetComponent(gone, out TrainPitch old) || !EntityManager.TryGetComponent(gone, out PrefabRef gonePrefab))
                    continue;
                List<RigBone> rig = RigOf(gonePrefab.m_Prefab);
                if (rig == null || (math.abs(old.m_Front) < PitchEpsilon && math.abs(old.m_Back) < PitchEpsilon &&
                                    math.abs(old.m_Nose) < PitchEpsilon))
                    continue;
                m_Pending.Add(new Pending { Tower = gone, Pitch = new TrainPitch { m_HeadFrame = quaternion.identity }, Rig = rig, Add = false });
            }
            (m_StationSide, m_StationSideNext) = (m_StationSideNext, m_StationSide);
            int towers = st.Towers, noRope = st.NoRope, clamped = st.Clamped, headed = st.Headed, headsPosed = st.HeadsPosed,
                worldHeld = st.WorldHeld;
            float maxDeg = st.MaxDeg, maxHalfBreakDeg = st.MaxHalfBreakDeg, maxHeadDeg = st.MaxHeadDeg, maxLeanDeg = st.MaxLeanDeg,
                maxWorldDeg = st.MaxWorldDeg;

            int added = 0;
            for (int i = 0; i < m_Pending.Count; i++)
            {
                Pending p = m_Pending[i];
                DynamicBuffer<TrainBonePose> poses;
                if (p.Add)
                {
                    EntityManager.AddComponentData(p.Tower, p.Pitch);
                    poses = EntityManager.AddBuffer<TrainBonePose>(p.Tower);
                    added++;
                }
                else
                {
                    EntityManager.SetComponentData(p.Tower, p.Pitch);
                    poses = EntityManager.GetBuffer<TrainBonePose>(p.Tower);
                }
                poses.Clear();
                foreach (RigBone b in p.Rig)
                    poses.Add(new TrainBonePose
                    {
                        m_Rotation = LiftAlignMath.TrainBoneRotation(b.Rest,
                            b.Part == PartHead ? p.Pitch.m_Head : b.Part == PartFront ? p.Pitch.m_Front
                            : b.Part == PartNose ? p.Pitch.m_Nose : p.Pitch.m_Back),
                        m_SubMesh = b.SubMesh,
                        m_Bone = b.Bone,
                    });
            }
            if (headsPosed > 0)
                World.GetOrCreateSystemManaged<TowerNumberTileSystem>().OnHeadsChanged(headsPosed);

            // A census rebuild re-runs the pass about every 40 s. A pass that posed nothing logs only when the rope-less count changed.
            if (m_Pending.Count > 0 || noRope != m_LastNoRope)
            {
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                Mod.Log.Info($"Towers [trains]: pass {m_Passes} ({why}) rigged towers {towers}, posed {m_Pending.Count} (new {added}), max |pitch| " +
                             $"{maxDeg:F1} deg (relative to the mast), max half-break {maxHalfBreakDeg:F1} deg" +
                             (maxLeanDeg > 0.05f ? $", max mast lean {maxLeanDeg:F1} deg" : string.Empty) +
                             (headed > 0 ? $", headed {headed} (max |head| {maxHeadDeg:F1} deg)" : string.Empty) +
                             (clamped > 0 ? $", {clamped} clamped to their half-break limit ({LiftAlignMath.MaxHalfBreakDeg:F0} deg, wide tram " +
                                            $"saddles {LiftAlignMath.TramHalfBreakMaxDeg:F0}: the rope leaves the inner wheels or the saddle)" : string.Empty) +
                             (worldHeld > 0 ? $", {worldHeld} held at their carrier-clearance limit (a half at most {maxWorldDeg:F1} deg in " +
                                              "the world: the rope leaves the train, the carrier stays clear of it)" : string.Empty) +
                             (noRope > 0 ? $", {noRope} without a rope lane on their anchors (level)" : string.Empty) +
                             (st.Stations > 0 ? $", station nose trains {st.Stations} (max {st.MaxNoseDeg:F1} deg off the station rope)"
                                 : string.Empty) +
                             (st.StationsOver > 0 ? $", {st.StationsOver} station train(s) whose first span goes past what the train " +
                                                    $"follows (worst {st.StationWorstOver:F1} deg over; log only)" : string.Empty) +
                             $"; {ms:F2} ms");
            }
            m_LastNoRope = noRope;
            m_Pending.Clear();

            // The towers past their train's limits. When the set or a tower's figure changed, log it and have the census re-measure. So
            // the hub's LiftTrainLimit row follows the trains within one coalesce window (the census reads m_Over at its harvest).
            if (!SameOver(m_Over, m_OverNext))
            {
                (m_Over, m_OverNext) = (m_OverNext, m_Over);
                float worst = 0f;
                foreach (float v in m_Over.Values)
                    worst = math.max(worst, v);
                Mod.Log.Info($"Towers [trains]: {m_Over.Count} line tower(s) whose rope goes past what the train follows (a half past " +
                             $"its pitch limit or its half-break limit), worst {worst:F1} deg over" +
                             (m_Over.Count > 0 ? $"; nodes {string.Join(", ", OverNodes())}" : string.Empty) + " (hub: LiftTrainLimit)");
                m_Census?.RequestBuild("train limits");
            }
        }

        /// <summary>One tower's read of a pass: its train pose from the mean lane-end pitches on its anchors (LiftAlignMath.PoseTrain), the
        /// counters, and a Pending row when the pose changed. <paramref name="overNode"/> = the line node whose hub row takes the tower's
        /// train limit, or Entity.Null for a station's hosted end tower (log only, T256).</summary>
        private void VisitTower(Entity overNode, Entity tower, Entity prefab, Transform t, bool roped, float meanF, int nF, float meanB,
            int nB, float slopeMax, string typeId, float headMax, ref PassStats st)
        {
            // The bones are local to the mast: a leaned tower (TowerLeanSystem) takes its lean off the absolute rope pitches.
            float mast = LiftAlignMath.MastPitch(t.m_Rotation);
            meanF -= mast;
            meanB -= mast;
            st.MaxLeanDeg = math.max(st.MaxLeanDeg, math.degrees(math.abs(mast)));
            List<RigBone> rig = RigOf(prefab);
            int headBone = HeadOf(rig);
            LiftAlignMath.TrainLimits(slopeMax, rig != null && m_WideTram.Contains(prefab), out float maxPitch,
                out float maxHalfBreak);
            float front = 0f, back = 0f, head = 0f, over = 0f;
            bool wasClamped = false, worldClamped = false;
            if (roped)
                over = LiftAlignMath.PoseTrain(meanF, nF, meanB, nB, mast, headBone >= 0, headMax, maxPitch, maxHalfBreak,
                    WorldMaxOf(prefab, typeId), out head, out front, out back, out wasClamped, out worldClamped);
            if (worldClamped && rig != null)
            {
                st.WorldHeld++;
                st.MaxWorldDeg = math.max(st.MaxWorldDeg, math.degrees(math.max(math.abs(front + mast), math.abs(back + mast))));
            }
            if (roped && over > LiftAlignMath.TrainWarnDeg)
            {
                if (overNode != Entity.Null)
                    m_OverNext[overNode] = m_OverNext.TryGetValue(overNode, out float o) ? math.max(o, over) : over;
                else
                {
                    st.StationsOver++;
                    st.StationWorstOver = math.max(st.StationWorstOver, over);
                }
            }
            if (rig == null)
                return;
            st.Towers++;
            if (headBone >= 0)
                st.Headed++;
            if (!roped)
            {
                st.NoRope++;
                return;
            }
            if (wasClamped)
                st.Clamped++;
            st.MaxDeg = math.max(st.MaxDeg, math.degrees(math.max(math.abs(front), math.abs(back))));
            st.MaxHalfBreakDeg = math.max(st.MaxHalfBreakDeg, math.degrees(math.abs(back - front) * 0.5f));
            st.MaxHeadDeg = math.max(st.MaxHeadDeg, math.degrees(math.abs(head)));
            var pitch = new TrainPitch { m_Front = front, m_Back = back, m_Clamped = wasClamped, m_HeadFrame = quaternion.identity };
            if (headBone >= 0)
            {
                pitch.m_Headed = true;
                pitch.m_Head = head;
                pitch.m_HeadPivot = rig[headBone].ObjectPosition;
                pitch.m_HeadFrame = rig[headBone].ObjectRotation;
            }
            bool add = !EntityManager.TryGetComponent(tower, out TrainPitch old);
            if (!add && math.abs(old.m_Front - front) < PitchEpsilon && math.abs(old.m_Back - back) < PitchEpsilon &&
                math.abs(old.m_Head - head) < PitchEpsilon && old.m_Headed == pitch.m_Headed)
                return;
            if (pitch.m_Headed)
                st.HeadsPosed++;
            m_Pending.Add(new Pending { Tower = tower, Pitch = pitch, Rig = rig, Add = add });
        }

        /// <summary>The station at a line's end node (T256). The first span's rope corners at the station's anchors. The station's nose
        /// train (bones "NoseTrain*": the outer half, the inner half is static on the in-station rope) follows it by
        /// LiftAlignMath.StationNosePose. A hosted end tower with train bones (the fixed-grip top's) takes the tower rule, with the
        /// in-station rope from the anchor to the bullwheel's tangent point as the rope of its station side. A station without a wheel
        /// row (the tram), without such bones (art before T256) or with no line rope on its anchors keeps its pose. Over-limit
        /// stations are logged only: they are not in the hub's LiftTrainLimit row.</summary>
        private void VisitStation(Entity node, Entity station, float slopeMax, string typeId, float headMax, ref PassStats st)
        {
            if (!m_SeenStations.Add(station) || EntityManager.HasComponent<Temp>(station) || EntityManager.HasComponent<Deleted>(station) ||
                !EntityManager.TryGetComponent(station, out PrefabRef pr) || !EntityManager.TryGetComponent(station, out Transform t) ||
                m_Census == null || !m_Census.TryGetStationDef(pr.m_Prefab, out StationDef def) || !math.all(math.isfinite(def.WheelLocal)))
                return;
            List<RigBone> rig = RigOf(pr.m_Prefab);
            bool nose = HasPart(rig, PartNose);
            bool hosted = false;
            if (EntityManager.TryGetBuffer(station, true, out DynamicBuffer<SubObject> subs))
                for (int s = 0; s < subs.Length && !hosted; s++)
                    hosted = EntityManager.TryGetComponent(subs[s].m_SubObject, out PrefabRef hp) && HasHalves(RigOf(hp.m_Prefab));
            if (!nose && !hosted)
                return;

            // The line rope ends on the station's anchors, and from each of them the in-station rope to the wheel's tangent point (the
            // wheel's radius is half the rope gauge: the tangent point has the anchor's x in the station frame)
            CollectEnds(node, pr.m_Prefab, t);
            m_Fixed.Clear();
            quaternion inverse = math.inverse(t.m_Rotation);
            for (int i = 0; i < m_Ends.Count; i++)
            {
                float3 local = math.mul(inverse, m_Ends[i].End - t.m_Position);
                float3 tangentPoint = Game.Objects.ObjectUtils.LocalToWorld(t, new float3(local.x, def.WheelLocal.y, def.WheelLocal.z));
                m_Fixed.Add(new RopeEnd { End = m_Ends[i].End, Inner = tangentPoint });
            }

            if (nose)
            {
                float3 forward = math.mul(t.m_Rotation, new float3(0f, 0f, 1f));
                float fixF = 0f, fixB = 0f, lineF = 0f, lineB = 0f;
                int nFixF = 0, nFixB = 0, nLineF = 0, nLineB = 0;
                Sum(m_Fixed, forward, ref fixF, ref nFixF, ref fixB, ref nFixB);
                Sum(m_Ends, forward, ref lineF, ref nLineF, ref lineB, ref nLineB);
                // The in-station rope runs toward +z (the wheel), the line leaves toward -z. A line drawn back over the station is not a
                // rope this train can follow: it keeps its rest pose.
                float fixedPitch = nFixF > 0 ? fixF / nFixF : 0f;
                LiftAlignMath.TrainLimits(slopeMax, false, out float maxPitch, out float maxHalfBreak);
                float over = LiftAlignMath.StationNosePose(fixedPitch, nLineB > 0 ? lineB / nLineB : 0f, nFixF > 0 ? nLineB : 0, maxPitch,
                    maxHalfBreak, float.PositiveInfinity, out float line, out _);
                float delta = line - fixedPitch;
                st.Stations++;
                st.MaxNoseDeg = math.max(st.MaxNoseDeg, math.degrees(math.abs(delta)));
                if (over > LiftAlignMath.TrainWarnDeg)
                {
                    st.StationsOver++;
                    st.StationWorstOver = math.max(st.StationWorstOver, over);
                }
                m_StationSideNext.Add(station);
                bool add = !EntityManager.TryGetComponent(station, out TrainPitch old);
                if (add || math.abs(old.m_Nose - delta) >= PitchEpsilon)
                    m_Pending.Add(new Pending
                    {
                        Tower = station, Pitch = new TrainPitch { m_Nose = delta, m_HeadFrame = quaternion.identity }, Rig = rig, Add = add,
                    });
            }

            if (!hosted)
                return;
            for (int s = 0; s < subs.Length; s++)
            {
                Entity tower = subs[s].m_SubObject;
                if (EntityManager.HasComponent<Temp>(tower) || EntityManager.HasComponent<Deleted>(tower) ||
                    !EntityManager.TryGetComponent(tower, out PrefabRef hp) || !HasHalves(RigOf(hp.m_Prefab)) ||
                    !EntityManager.TryGetComponent(tower, out Transform ht))
                    continue;
                float3 forward = math.mul(ht.m_Rotation, new float3(0f, 0f, 1f));
                float sumF = 0f, sumB = 0f;
                int nF = 0, nB = 0;
                Sum(m_Ends, forward, ref sumF, ref nF, ref sumB, ref nB);
                Sum(m_Fixed, forward, ref sumF, ref nF, ref sumB, ref nB);
                m_StationSideNext.Add(tower);
                VisitTower(Entity.Null, tower, hp.m_Prefab, ht, nF + nB > 0, nF > 0 ? sumF / nF : 0f, nF, nB > 0 ? sumB / nB : 0f, nB,
                    slopeMax, typeId, headMax, ref st);
            }
        }

        private static bool HasPart(List<RigBone> rig, int part)
        {
            if (rig != null)
                for (int i = 0; i < rig.Count; i++)
                    if (rig[i].Part == part)
                        return true;
            return false;
        }

        private static bool HasHalves(List<RigBone> rig) => HasPart(rig, PartFront) || HasPart(rig, PartBack);

        private static bool SameOver(Dictionary<Entity, float> a, Dictionary<Entity, float> b)
        {
            if (a.Count != b.Count)
                return false;
            foreach (KeyValuePair<Entity, float> kv in a)
                if (!b.TryGetValue(kv.Key, out float v) || math.abs(v - kv.Value) >= 0.05f)
                    return false;
            return true;
        }

        private IEnumerable<string> OverNodes()
        {
            int n = 0;
            foreach (KeyValuePair<Entity, float> kv in m_Over)
            {
                if (n++ == 8)
                {
                    yield return $"+{m_Over.Count - 8} more";
                    yield break;
                }
                yield return $"{kv.Key.Index}:{kv.Key.Version} {kv.Value:F1}";
            }
        }

        /// <summary>The mean lane-end pitches of the ropes on the tower's anchors leaving toward +z (front) and -z (back), with their counts
        /// (the inputs of LiftAlignMath.HalfPitches / HeadedHalfPitches). False when no lane end sits on an anchor.</summary>
        private bool RopeAt(Entity node, Entity towerPrefab, Transform t, out float meanF, out int nF, out float meanB, out int nB)
        {
            meanF = meanB = 0f;
            nF = nB = 0;
            if (!CollectEnds(node, towerPrefab, t))
                return false;
            float3 forward = math.mul(t.m_Rotation, new float3(0f, 0f, 1f));
            float sumF = 0f, sumB = 0f;
            Sum(m_Ends, forward, ref sumF, ref nF, ref sumB, ref nB);
            if (nF + nB == 0)
                return false;
            meanF = nF > 0 ? sumF / nF : 0f;
            meanB = nB > 0 ? sumB / nB : 0f;
            return true;
        }

        /// <summary>Adds the lane-end pitches of <paramref name="ends"/> to the front (+z) and back (-z) sums of an object whose +z is
        /// <paramref name="forward"/> (LiftAlignMath.LaneEndPitch / LaneEndSide).</summary>
        private static void Sum(List<RopeEnd> ends, float3 forward, ref float sumF, ref int nF, ref float sumB, ref int nB)
        {
            for (int i = 0; i < ends.Count; i++)
                AddEnd(ends[i].End, ends[i].Inner, forward, ref sumF, ref nF, ref sumB, ref nB);
        }

        /// <summary>Fills m_Ends with every lift rope lane end of the node's edges that sits on an anchor of
        /// <paramref name="anchorPrefab"/> (a tower's or a station's prefab sub-lanes, mapped by <paramref name="t"/>). False when the
        /// prefab has no sub-lanes or the node no edges; m_Ends is empty then.</summary>
        private bool CollectEnds(Entity node, Entity anchorPrefab, Transform t)
        {
            m_Ends.Clear();
            m_Anchors.Clear();
            if (!EntityManager.TryGetBuffer(anchorPrefab, true, out DynamicBuffer<Game.Prefabs.SubLane> anchors) ||
                !EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges))
                return false;
            for (int i = 0; i < anchors.Length; i++)
            {
                Colossal.Mathematics.Bezier4x3 w = Game.Objects.ObjectUtils.LocalToWorld(t.m_Position, t.m_Rotation, anchors[i].m_Curve);
                m_Anchors.Add(w.a);
                m_Anchors.Add(w.d);
            }
            for (int i = 0; i < edges.Length; i++)
            {
                if (!EntityManager.TryGetBuffer(edges[i].m_Edge, true, out DynamicBuffer<SubLane> lanes))
                    continue;
                for (int l = 0; l < lanes.Length; l++)
                {
                    Entity lane = lanes[l].m_SubLane;
                    if (!EntityManager.TryGetComponent(lane, out PrefabRef lp) || !CableLanePrefabs.IsHighVoltage(EntityManager, lp.m_Prefab) ||
                        !EntityManager.TryGetComponent(lane, out Curve c))
                        continue;
                    if (OnAnchor(c.m_Bezier.a))
                        m_Ends.Add(new RopeEnd { End = c.m_Bezier.a, Inner = c.m_Bezier.b });
                    if (OnAnchor(c.m_Bezier.d))
                        m_Ends.Add(new RopeEnd { End = c.m_Bezier.d, Inner = c.m_Bezier.c });
                }
            }
            return m_Ends.Count > 0;
        }

        /// <summary>The carrier-clearance limit (radians, in the world) of a tower prefab on a <paramref name="typeId"/> line, cached per
        /// prefab: LiftAlignMath.WorldHalfMaxDeg by the type and the prefab's head (a twin's name, LiftTowerPlanConstants.HeadOfTwin, else the
        /// line member's "support"). +infinity without a row (the tram, DQ members, every Combo, the FG Hold).</summary>
        private float WorldMaxOf(Entity prefab, string typeId)
        {
            if (m_WorldMax.TryGetValue(prefab, out float v))
                return v;
            LiftTowerHead head = LiftTowerPlanConstants.HeadOfTwin(m_PrefabSystem.GetPrefabName(prefab));
            v = math.radians(LiftAlignMath.WorldHalfMaxDeg(typeId, LiftTowerPlanConstants.WorldHalfKind(head)));
            m_WorldMax[prefab] = v;
            return v;
        }

        /// <summary>The rig index of the prefab's Head bone. -1 when it has none (unheaded: FG, tram, the pre-head DQ / GD meshes).</summary>
        private static int HeadOf(List<RigBone> rig)
        {
            if (rig != null)
                for (int i = 0; i < rig.Count; i++)
                    if (rig[i].Part == PartHead)
                        return i;
            return -1;
        }

        private static void AddEnd(float3 end, float3 inner, float3 forward, ref float sumF, ref int nF, ref float sumB, ref int nB)
        {
            float pitch = LiftAlignMath.LaneEndPitch(end, inner, forward);
            if (LiftAlignMath.LaneEndSide(end, inner, forward) > 0)
            {
                sumF += pitch;
                nF++;
            }
            else
            {
                sumB += pitch;
                nB++;
            }
        }

        private bool OnAnchor(float3 p)
        {
            for (int i = 0; i < m_Anchors.Count; i++)
                if (math.distancesq(m_Anchors[i], p) < AnchorMatchM * AnchorMatchM)
                    return true;
            return false;
        }

        /// <summary>The prefab's train bones (TrainFront* / TrainBack*), resolved once per prefab per load from the mesh prefabs (main
        /// thread). It returns null when it has none.</summary>
        private List<RigBone> RigOf(Entity prefab)
        {
            if (m_Rigs.TryGetValue(prefab, out List<RigBone> rig))
                return rig;
            rig = null;
            int front = 0, back = 0, head = 0, nose = 0;
            bool wide = false;
            if (EntityManager.TryGetBuffer(prefab, true, out DynamicBuffer<SubMesh> meshes))
                for (int j = 0; j < meshes.Length; j++)
                {
                    Entity mesh = meshes[j].m_SubMesh;
                    if (!m_PrefabSystem.TryGetPrefab(mesh, out RenderPrefab render) ||
                        !EntityManager.TryGetBuffer(mesh, true, out DynamicBuffer<ProceduralBone> bones))
                        continue;
                    wide |= LiftAlignMath.WideTramSaddle(render.name);
                    ProceduralAnimationProperties props = render.GetComponent<ProceduralAnimationProperties>();
                    if (props == null || props.m_Bones == null || props.m_Bones.Length != bones.Length)
                        continue;
                    for (int k = 0; k < props.m_Bones.Length; k++)
                    {
                        string name = props.m_Bones[k].name;
                        if (name == null)
                            continue;
                        int part = name.StartsWith(FrontPrefix, StringComparison.Ordinal) ? PartFront
                            : name.StartsWith(BackPrefix, StringComparison.Ordinal) ? PartBack
                            : name.StartsWith(HeadPrefix, StringComparison.Ordinal) ? PartHead
                            : name.StartsWith(NosePrefix, StringComparison.Ordinal) ? PartNose : -1;
                        if (part < 0)
                            continue;
                        for (int b = 0; b < bones.Length; b++)
                            if (bones[b].m_BindIndex == k)    // m_Bones index k -> the sorted buffer slot
                            {
                                (rig ??= new List<RigBone>()).Add(new RigBone
                                {
                                    SubMesh = j, Bone = b, Part = part, Rest = bones[b].m_Rotation,
                                    ObjectPosition = bones[b].m_ObjectPosition, ObjectRotation = bones[b].m_ObjectRotation,
                                });
                                if (part == PartFront)
                                    front++;
                                else if (part == PartBack)
                                    back++;
                                else if (part == PartNose)
                                    nose++;
                                else
                                    head++;
                                break;
                            }
                    }
                }
            m_Rigs[prefab] = rig;
            if (rig != null && wide)
                m_WideTram.Add(prefab);
            if (rig != null && nose > 0 && front == 0 && back == 0)
                Mod.Log.Info($"Towers [trains]: {m_PrefabSystem.GetPrefabName(prefab)} has {nose} station nose-train bone(s); the outer " +
                             "half of its nose train pitches with the first span's rope");
            else if (rig != null)
                Mod.Log.Info($"Towers [trains]: {m_PrefabSystem.GetPrefabName(prefab)} has train bones ({front} front, {back} back" +
                             (head > 0 ? $", {head} head" : string.Empty) + "); its half-trains pitch with the rope" +
                             (head > 0 ? ", its head with their mean" : string.Empty) +
                             (wide ? $" (wide tram saddle: halves to {LiftAlignMath.TramSaddlePitchLimitDeg:F0} deg, half-break " +
                                                    $"{LiftAlignMath.TramHalfBreakMaxDeg:F0})" : string.Empty) +
                             (front == 0 || back == 0 ? " — !!! one half has no bone, that half stays level" : string.Empty));
            return rig;
        }
    }

    public sealed partial class TrainBoneSystem : GameSystemBase
    {
        [BurstCompile]
        private struct WriteBonesJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<TrainBonePose> PoseType;
            public BufferTypeHandle<Skeleton> SkeletonType;
            public BufferTypeHandle<Bone> BonesType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                BufferAccessor<TrainBonePose> poses = chunk.GetBufferAccessor(ref PoseType);
                BufferAccessor<Skeleton> skeletons = chunk.GetBufferAccessor(ref SkeletonType);
                BufferAccessor<Bone> bones = chunk.GetBufferAccessor(ref BonesType);
                for (int i = 0; i < chunk.Count; i++)
                {
                    DynamicBuffer<TrainBonePose> pose = poses[i];
                    DynamicBuffer<Skeleton> skel = skeletons[i];
                    DynamicBuffer<Bone> bone = bones[i];
                    for (int k = 0; k < pose.Length; k++)
                    {
                        TrainBonePose p = pose[k];
                        if (p.m_SubMesh >= skel.Length)
                            continue;                     // not near the camera: no skeleton (InitializeBonesSystem Remove)
                        ref Skeleton s = ref skel.ElementAt(p.m_SubMesh);
                        if (s.m_BufferAllocation.Empty || s.m_BoneOffset < 0 || s.m_BoneOffset + p.m_Bone >= bone.Length)
                            continue;
                        ref Bone b = ref bone.ElementAt(s.m_BoneOffset + p.m_Bone);
                        if (b.m_Rotation.Equals(p.m_Rotation))
                            continue;
                        b.m_Rotation = p.m_Rotation;
                        s.m_CurrentUpdated = true;        // ProceduralUploadSystem uploads it this frame
                    }
                }
            }
        }

        private EntityQuery m_Query;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TrainBonePose>(), ComponentType.ReadWrite<Skeleton>(), ComponentType.ReadWrite<Bone>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            RequireForUpdate(m_Query);
            // Job reflection data and JIT before the first Schedule (the PrewarmJit precedent). Schedules nothing.
            JobChunkExtensions.EarlyJobInit<WriteBonesJob>();
            Mod.Log.Info($"{nameof(TrainBoneSystem)} created");
        }

        protected override void OnUpdate()
        {
            Dependency = new WriteBonesJob
            {
                PoseType = GetBufferTypeHandle<TrainBonePose>(true),
                SkeletonType = GetBufferTypeHandle<Skeleton>(false),
                BonesType = GetBufferTypeHandle<Bone>(false),
            }.Schedule(m_Query, Dependency);
        }
    }
}
