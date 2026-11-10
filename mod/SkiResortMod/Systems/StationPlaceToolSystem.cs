// The station placer: a scripted, prefab-less subclass of the vanilla ObjectToolBaseSystem that places lift stations from code, one at a
// time from a queue. It was the P0 probe of the Resort Planner round (option A: the planner builds the lifts, DECISIONS 630). Entries:
// Enqueue(why, request) (the lift build queue, ResortLiftBuildSystem) and TryPlace(why, prefab, position, rotation) (the dev trigger,
// StationPlaceProbeUISystem, Measurement diagnostics only). Each request ends in one outcome: Placed (the permanent station), Refused (the
// game's error types on the preview), or Canceled (dropped before its emission). TryGetOutcome reads it by serial. The tool stays active
// while the queue holds requests and hands the tool back to the default tool when the queue is empty.
// Own Temp: the Temp building count can include a selected building's Temp copy (P0 note 1), so FindTemp takes a Create Temp of the
// request's prefab within OwnTempM (xz) of the requested position, the nearest one.
// Why a subclass: vanilla builds no object definitions outside ObjectToolBaseSystem.CreateDefinitions, which is protected
// (DC/Game.Tools/ObjectToolBaseSystem.cs:2363; 1.6.2f1 :2382 adds a PlacementOverrides argument). With one ControlPoint and distance 0 its
// job emits the building at the point's position and rotation (:633 UpdateObject), with its sub-objects, SUB-NETS (the line and LV stubs)
// and sub-areas (:1101-1297, :1561). A hand-written ObjectDefinition would give a station without its stubs.
// The subclass contract is UpgradeToolSystem's (DC/Game.Tools/UpgradeToolSystem.cs): toolID, GetPrefab, TrySetPrefab are the only
// abstract members (ToolBaseSystem.cs:142, :426, :428). The base's lookups are assigned in its own OnCreateForCompiler (:2479), a
// virtual that runs for any subclass, as ToolBaseSystem's (:732-737) does for our other tools. Its OnCreate (:2354) resolves the barrier,
// the search tree, water and terrain. The control point list is persistent and owned by this system, as ObjectToolSystem's
// (ObjectToolSystem.cs:2612, :2668). The definitions job reads it, so every write first completes the last emission's job.
// Height: vanilla stands a building on the terrain at the FRONT edge of its lot (PlacementFlags.RoadSide on every BuildingData building,
// DC/Game.Prefabs/BuildingInitializeSystem.cs:1131-1133; ObjectToolSystem.CalculateHeight :1608-1625 at BuildingUtils.CalculateFrontPosition
// :88-92), plus PlaceableObjectData.m_PlacementOffset.y. ResolveY does the same when the caller gives no y (NaN) and the request's
// HeightRule is VanillaFront (the dev probe). The build queue asks Fit (StationAlign.StationY): on a pad station the lowest terrain of
// the front edge, the origin and the line-side edge; on a slope station (the prefab's BuildingTerraformData has DontRaise and DontLower)
// the terrain at the origin. On a top station the front edge is uphill, so the vanilla y floats (field r1c, 5 to 14 m).
// Phases: Pending (activated, emits on its first update), Emitted (definitions out: wait for the Temp building, one settle frame for
// validation, then Apply when GetAllowApply holds, else Clear), Applying (the frame after Apply: read the permanent station, log it,
// hand the tool back to the default tool). Apply and Clear are read from the tool that ran this frame (ToolSystem.cs:147-156,
// ToolOutputSystem.cs:22-28), so a switch in the decide frame would also work. Apply keeps the Temp entity as the building
// (ApplyObjectsSystem.cs:670). A follow-up line 120 frames after Apply re-reads the station once the lot cut has settled (TickWatch,
// called every frame by StationPlaceProbeUISystem: an inactive tool never ticks, ToolSystem.cs:312-327).
// Logs (silence impossible): ARMED / NOT ARMED per load. One line per request, activation, emission, Temp, verdict, APPLIED (entity,
// Transform, Lot, sub-net nodes, sub-objects, sub-areas, money), follow-up and deactivation. An exception disables the tool until the next
// load.
// Engine notes: EngineNotes/StationPlaceToolSystem.md

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class StationPlaceToolSystem : ObjectToolBaseSystem
    {
        private const int TempTimeoutFrames = 30;   // our other tools' value (QuickRunsToolSystem.Build.cs, LiftLineToolSystem.cs)
        private const int SettleFrames = 1;         // frames between the first sight of the Temp and the verdict (validation lands)
        private const int FollowUpFrames = 120;     // the follow-up line after Apply: the lot cut and LotHeightSystem have run by then
        private const int ApplyWaitFrames = 5;      // frames the Applying phase waits for the Temp tag to go before it searches by pose
        private const float OwnTempM = 1f;          // our Temp stands at the requested xz (one control point, distance 0)
        private const int MaxOutcomes = 64;         // outcomes kept for TryGetOutcome (the oldest go first)
        private const int MaxQueued = 32;

        internal enum PlacePhase : byte { Idle, Pending, Emitted, Applying }

        /// <summary>The state of one request. Pending covers queued and in flight.</summary>
        internal enum PlaceStatus : byte { Pending, Placed, Refused, Canceled }

        /// <summary>One station to place. Position y NaN = the height of HeightRule (default VanillaFront, the object tool's height). Tag is
        /// the caller's (the build queue names the lift and the role, CancelPending matches it).</summary>
        internal struct PlaceRequest
        {
            public Entity Prefab;
            public float3 Position;
            public quaternion Rotation;
            public string Tag;
            public StationAlign.StationHeightRule HeightRule;
        }

        /// <summary>The outcome of one request. Placed is the permanent station. ErrorTypes counts the game's error types on a refused
        /// preview ("OverlapExisting x2, InWater x1"). FirstError is the first typed one (the sheet's reason key), else null.</summary>
        internal sealed class PlaceOutcome
        {
            public int Serial;
            public string Tag;
            public PlaceStatus Status;
            public Entity Placed;
            public string Summary;
            public string ErrorTypes;
            public string FirstError;
            public long Paid;
        }

        /// <summary>The outcome of the last request. Placed is the permanent station (Entity.Null on a refusal). Summary is one line for a
        /// caller (the planner's lift row later).</summary>
        internal readonly struct PlaceResult
        {
            public readonly Entity Placed;
            public readonly string Summary;
            public readonly int Serial;

            public PlaceResult(Entity placed, string summary, int serial)
            {
                Placed = placed;
                Summary = summary;
                Serial = serial;
            }

            public bool Ok => Placed != Entity.Null;
        }

        public override string toolID => "SkiResort Station Place Tool";

        internal bool IsActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;
        internal bool IsArmed => m_Armed;
        internal bool IsDisabled => m_Disabled;
        internal PlacePhase Phase => m_Phase;
        internal PlaceResult LastResult => m_Result;
        /// <summary>Requests waiting behind the one in flight.</summary>
        internal int QueuedCount => m_Queue.Count;
        /// <summary>A request is in flight or waiting.</summary>
        internal bool Busy => m_Phase != PlacePhase.Idle || m_Queue.Count > 0;

        private CityConfigurationSystem m_CityConfig;
        private CitySystem m_CitySystem;
        private EntityQuery m_DefinitionQuery, m_TempBuildingQuery, m_BuildingQuery;
        private NativeList<ControlPoint> m_ControlPoints;
        private JobHandle m_EmitJob;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Frame, m_Serial;
        private PlacePhase m_Phase;
        private PlaceResult m_Result;

        // The request in flight
        private string m_Why;
        private Entity m_Prefab;
        private float3 m_Position;
        private quaternion m_Rotation;
        private StationAlign.StationHeightRule m_HeightRule;
        private int m_EmitFrame, m_SeenFrame = -1, m_ApplyFrame;
        private long m_MoneyBefore;
        private Entity m_Temp;
        private PlaceOutcome m_Current;

        // The queue behind the request in flight, and the outcomes by serial
        private readonly List<PlaceOutcome> m_Queue = new List<PlaceOutcome>();
        private readonly List<PlaceRequest> m_QueueReq = new List<PlaceRequest>();
        private readonly Dictionary<int, PlaceOutcome> m_Outcomes = new Dictionary<int, PlaceOutcome>();
        private readonly Queue<int> m_OutcomeOrder = new Queue<int>();

        // The follow-up after Apply (one per placed station)
        private readonly List<(Entity station, int frame)> m_Watches = new List<(Entity station, int frame)>();


        protected override void OnCreate()
        {
            base.OnCreate();   // ObjectToolBaseSystem.cs:2354-2361: m_ToolOutputBarrier, m_ObjectSearchSystem, m_WaterSystem, m_TerrainSystem
            m_CityConfig = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_DefinitionQuery = GetDefinitionQuery();   // All CreationDefinition, None Updated (ToolBaseSystem.cs:689-692)
            m_TempBuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            m_BuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Game.Objects.Transform>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_ControlPoints = new NativeList<ControlPoint>(1, Allocator.Persistent);
            Mod.Log.Info($"{nameof(StationPlaceToolSystem)} created");
        }

        protected override void OnDestroy()
        {
            m_EmitJob.Complete();
            if (m_ControlPoints.IsCreated)
                m_ControlPoints.Dispose();
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = m_ErrorLogged = false;
            m_Phase = PlacePhase.Idle;
            m_Temp = Entity.Null;
            m_Current = null;
            m_Watches.Clear();
            m_Queue.Clear();
            m_QueueReq.Clear();
            m_Outcomes.Clear();
            m_OutcomeOrder.Clear();
            m_Result = default;
            m_Armed = mode == GameMode.Game;
            Mod.Log.Info(m_Armed
                ? $"StationPlace [tool]: ARMED — '{toolID}' places stations from code, one at a time from a queue (CreateDefinitions, one " +
                  "control point each); callers: the lift build queue, and the dev trigger skiResortDev.probePlaceStation (Measurement " +
                  "diagnostics only)"
                : $"StationPlace [tool]: NOT ARMED (mode {mode})");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            applyMode = ApplyMode.None;
            Mod.Log.Info($"StationPlace [tool]: running (phase {m_Phase}, request #{CurSerial}, {m_Queue.Count} queued)");
        }

        protected override void OnStopRunning()
        {
            try
            {
                if (m_Phase == PlacePhase.Pending || m_Phase == PlacePhase.Emitted)
                {
                    // Closed by something else before the verdict: drop the definitions. The incoming tool's first frame clears the Temps
                    // (ToolSystem reads the new tool's applyMode on a switch, ToolSystem.cs:147-156).
                    if (!m_DefinitionQuery.IsEmptyIgnoreFilter)
                        EntityManager.DestroyEntity(m_DefinitionQuery);
                    Finish(Entity.Null, PlaceStatus.Canceled, $"closed by another tool in phase {m_Phase}: nothing placed", null, null);
                }
                else if (m_Phase == PlacePhase.Applying)
                {
                    // Apply ran: the station stands. Find it by pose now (the Temp entity is the station, ApplyObjectsSystem.cs:670)
                    Entity found = EntityManager.Exists(m_Temp) && !EntityManager.HasComponent<Temp>(m_Temp) ? m_Temp : FindPermanent();
                    Mod.Log.Info($"StationPlace [tool]: closed in the frame after Apply; station {(found == Entity.Null ? "not found yet" : Fmt(found))}");
                    Finish(found, found != Entity.Null ? PlaceStatus.Placed : PlaceStatus.Refused,
                           found != Entity.Null ? $"placed {Fmt(found)} (tool closed after Apply)" : "Apply ran, the tool closed, no station found", null, null);
                }
                CancelQueued("the tool was closed by another tool");
                m_Phase = PlacePhase.Idle;
                Mod.Log.Info($"StationPlace [tool]: stopped (request #{CurSerial})");
            }
            catch (Exception e)
            {
                Fail(e, "OnStopRunning");
            }
            base.OnStopRunning();
        }

        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;   // never picked by ToolSystem.ActivatePrefabTool

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;   // unused: the pose comes from the caller
        }

        protected override bool GetAllowApply()
        {
            return base.GetAllowApply() && m_Phase == PlacePhase.Emitted;   // ToolBaseSystem.cs:533-540 (no Error entity anywhere)
        }


        /// <summary>Place one station (the dev trigger's entry): Enqueue with no tag. True when the request was queued.</summary>
        internal bool TryPlace(string why, Entity prefab, float3 position, quaternion rotation) =>
            Enqueue(why, new PlaceRequest { Prefab = prefab, Position = position, Rotation = rotation, Tag = null }) > 0;

        /// <summary>Queue one station. position.y NaN = the height of request.HeightRule (StationAlign.StationY). Activates
        /// this tool when it is idle; the emission runs in its next update. Returns the request's serial (read the outcome with
        /// TryGetOutcome), or 0 (logged, an outcome Refused under the next serial is NOT kept) when the tool is not armed, disabled, the
        /// queue is full or the request is malformed. Main thread, outside a job.</summary>
        internal int Enqueue(string why, PlaceRequest request)
        {
            int serial = ++m_Serial;
            Entity prefab = request.Prefab;
            float3 position = request.Position;
            string refusal = !m_Armed ? "not armed (not in a game)"
                : m_Disabled ? "disabled after an exception until the next load"
                : m_Queue.Count >= MaxQueued ? $"the queue is full ({MaxQueued})"
                : prefab == Entity.Null ? "no prefab"
                : !EntityManager.HasComponent<BuildingData>(prefab) ? $"prefab {PrefabNameOf(prefab)} is not a building"
                : !math.isfinite(position.x) || !math.isfinite(position.z) ? "position x / z not finite"
                : null;
            if (refusal != null)
            {
                Mod.Log.Warn($"StationPlace [request]: #{serial} REFUSED ({why}{TagText(request.Tag)}) — {refusal}");
                m_Result = new PlaceResult(Entity.Null, "refused: " + refusal, serial);
                return 0;
            }
            request.Rotation = math.normalizesafe(request.Rotation, quaternion.identity);
            var outcome = new PlaceOutcome { Serial = serial, Tag = request.Tag, Status = PlaceStatus.Pending, Summary = "queued" };
            Remember(outcome);
            m_Queue.Add(outcome);
            m_QueueReq.Add(request);
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "StationPlace [request]: #{0} ({1}{2}) — {3} at x {4:F2}, z {5:F2}, y {6}, yaw {7:F1} deg; {8} in the queue, phase {9}; active tool: {10}",
                serial, why, TagText(request.Tag), PrefabNameOf(prefab), position.x, position.z,
                math.isfinite(position.y) ? position.y.ToString("F2", CultureInfo.InvariantCulture) : "vanilla",
                YawDeg(request.Rotation), m_Queue.Count, m_Phase, m_ToolSystem.activeTool?.toolID ?? "none"));
            m_Why = why;
            if (m_Phase == PlacePhase.Idle)
                StartNext();
            if (!IsActive)
            {
                m_ToolSystem.activeTool = this;
                Mod.Log.Info($"StationPlace [tool]: activated ({why})");
            }
            return serial;
        }

        /// <summary>The outcome of a request by its serial (Pending while queued or in flight). False when the serial is unknown or too old
        /// (the last MaxOutcomes are kept).</summary>
        internal bool TryGetOutcome(int serial, out PlaceOutcome outcome) => m_Outcomes.TryGetValue(serial, out outcome);

        /// <summary>Drop the queued requests with this tag (never the one in flight). Their outcome is Canceled. Returns the count.</summary>
        internal int CancelPending(string tag, string why)
        {
            int n = 0;
            for (int i = m_Queue.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(m_Queue[i].Tag, tag, StringComparison.Ordinal))
                    continue;
                PlaceOutcome o = m_Queue[i];
                o.Status = PlaceStatus.Canceled;
                o.Summary = "canceled before its emission: " + why;
                m_Queue.RemoveAt(i);
                m_QueueReq.RemoveAt(i);
                Mod.Log.Info($"StationPlace [request]: #{o.Serial}{TagText(tag)} CANCELED — {why}");
                n++;
            }
            return n;
        }

        /// <summary>The next queued request becomes the one in flight (phase Pending). The caller keeps the tool active.</summary>
        private void StartNext()
        {
            if (m_Queue.Count == 0)
                return;
            m_Current = m_Queue[0];
            PlaceRequest r = m_QueueReq[0];
            m_Queue.RemoveAt(0);
            m_QueueReq.RemoveAt(0);
            m_Prefab = r.Prefab;
            m_Rotation = r.Rotation;
            m_Position = r.Position;
            m_HeightRule = r.HeightRule;
            m_Phase = PlacePhase.Pending;
            m_Temp = Entity.Null;
            m_SeenFrame = -1;
            m_Current.Summary = "in flight";
            m_Result = new PlaceResult(Entity.Null, "pending", m_Current.Serial);
        }

        private void CancelQueued(string why)
        {
            for (int i = 0; i < m_Queue.Count; i++)
            {
                m_Queue[i].Status = PlaceStatus.Canceled;
                m_Queue[i].Summary = "canceled before its emission: " + why;
                Mod.Log.Info($"StationPlace [request]: #{m_Queue[i].Serial}{TagText(m_Queue[i].Tag)} CANCELED — {why}");
            }
            m_Queue.Clear();
            m_QueueReq.Clear();
        }

        private void Remember(PlaceOutcome o)
        {
            m_Outcomes[o.Serial] = o;
            m_OutcomeOrder.Enqueue(o.Serial);
            while (m_OutcomeOrder.Count > MaxOutcomes)
                m_Outcomes.Remove(m_OutcomeOrder.Dequeue());
        }

        private static string TagText(string tag) => string.IsNullOrEmpty(tag) ? string.Empty : ", " + tag;

        /// <summary>Every frame (StationPlaceProbeUISystem): the follow-up line after an Apply, also while the tool is closed.</summary>
        internal void TickWatch()
        {
            if (m_Disabled)
                return;
            if (!IsActive)
                m_Frame++;
            if (m_Watches.Count == 0 || m_Frame < m_Watches[0].frame)
                return;
            try
            {
                Entity e = m_Watches[0].station;
                m_Watches.RemoveAt(0);
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e))
                {
                    Mod.Log.Warn($"StationPlace [after]: the station {Fmt(e)} is gone {FollowUpFrames} frames after Apply");
                    return;
                }
                Mod.Log.Info($"StationPlace [after]: {FollowUpFrames} frames after Apply — {DescribeStation(e)}; money {CityMoney()}");
            }
            catch (Exception ex)
            {
                Fail(ex, "TickWatch");
            }
        }


        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            try
            {
                m_Frame++;
                applyMode = ApplyMode.None;
                switch (m_Phase)
                {
                    case PlacePhase.Pending:
                        return Emit(inputDeps);
                    case PlacePhase.Emitted:
                        return UpdateEmitted(inputDeps);
                    case PlacePhase.Applying:
                        FinishApplied();
                        return inputDeps;
                    default:
                        if (m_Queue.Count > 0)
                        {
                            StartNext();   // a request queued while idle and active: emit on the next update
                            return inputDeps;
                        }
                        Deactivate("idle");   // opened with nothing to place (never by the player: TrySetPrefab refuses every prefab)
                        return inputDeps;
                }
            }
            catch (Exception e)
            {
                Fail(e, "OnUpdate");
                applyMode = ApplyMode.Clear;
                m_Phase = PlacePhase.Idle;
                try
                {
                    if (m_Current != null && m_Current.Status == PlaceStatus.Pending)
                        Finish(Entity.Null, PlaceStatus.Refused, "the placer failed (exception, see the log)", null, null);
                    CancelQueued("the placer failed (exception)");
                    Deactivate("exception");
                }
                catch (Exception) { /* logged */ }
                return inputDeps;
            }
        }

        private JobHandle Emit(JobHandle inputDeps)
        {
            m_EmitJob.Complete();   // the list is read by the last emission's job
            float3 p = m_Position;
            if (!math.isfinite(p.y))
                p.y = ResolveY(m_Prefab, p, m_Rotation, m_HeightRule);
            m_Position = p;
            var cp = new ControlPoint
            {
                m_Position = p,
                m_HitPosition = p,
                m_Rotation = m_Rotation,
                m_OriginalEntity = Entity.Null,   // no parent: UpdateObject attaches to nothing (ObjectToolBaseSystem.cs:1206-1240)
            };
            m_ControlPoints.Clear();
            m_ControlPoints.Add(in cp);
            m_MoneyBefore = CityMoney();
            JobHandle destroy = DestroyDefinitions(m_DefinitionQuery, m_ToolOutputBarrier, inputDeps);
            // A plain new building, as ObjectToolSystem.UpdateDefinitions passes it (ObjectToolSystem.cs:4353): no owner (upgrade), no
            // original (move), no lane editor (editor only), the city theme, no attachment (placeholder buildings only, :4331-4350), game
            // mode, not removing or stamping, the brush values unused (no brush prefab), distance 0 = one object (else CreateCurve, :610),
            // a fresh seed, Snap.None (the job reads m_Snap only for brushes and editor auto-parenting, :874, :1076, :1168), Sapling (the
            // tool's age for anything that cannot age, ObjectToolSystem.cs:2381-2383), no decoration, no placement overrides (editor only).
            JobHandle create = CreateDefinitions(m_Prefab, Entity.Null, Entity.Null, Entity.Null, Entity.Null, Entity.Null,
                m_CityConfig.defaultTheme, m_ControlPoints, default(NativeReference<AttachmentData>), editorMode: false,
                m_CityConfig.leftHandTraffic, removing: false, stamping: false, brushSize, math.radians(brushAngle), brushStrength,
                distance: 0f, UnityEngine.Time.deltaTime, RandomSeed.Next(), Snap.None, Game.Tools.AgeMask.Sapling, decorationMode: false,
                default(PlacementOverrides), inputDeps);
            m_EmitJob = create;
            m_Phase = PlacePhase.Emitted;
            m_EmitFrame = m_Frame;
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "StationPlace [emit]: #{0} {1} definitions scheduled at ({2:F2}, {3:F2}, {4:F2}) yaw {5:F1} deg (line leaves toward {6}); money {7}; " +
                "waiting for the game's Temp building (timeout {8} frames)",
                CurSerial, PrefabNameOf(m_Prefab), p.x, p.y, p.z, YawDeg(m_Rotation), Dir(math.rotate(m_Rotation, new float3(0f, 0f, -1f))),
                m_MoneyBefore, TempTimeoutFrames));
            return JobHandle.CombineDependencies(destroy, create);
        }

        private JobHandle UpdateEmitted(JobHandle inputDeps)
        {
            int waited = m_Frame - m_EmitFrame;
            if (m_Temp == Entity.Null)
                m_Temp = FindTemp(out _);
            if (m_Temp == Entity.Null)
            {
                if (waited <= TempTimeoutFrames)
                    return inputDeps;
                applyMode = ApplyMode.Clear;
                JobHandle h = DestroyDefinitions(m_DefinitionQuery, m_ToolOutputBarrier, inputDeps);
                Mod.Log.Warn($"StationPlace [verdict]: #{CurSerial} NOTHING PLACED — no Temp building of {PrefabNameOf(m_Prefab)} " +
                             $"{TempTimeoutFrames} frames after the emission (please send the log)");
                Finish(Entity.Null, PlaceStatus.Refused, $"no Temp building {TempTimeoutFrames} frames after the emission", "no Temp", "NoTemp");
                NextOrDeactivate("no Temp");
                return h;
            }
            if (m_SeenFrame < 0)
            {
                m_SeenFrame = m_Frame;
                Temp t0 = EntityManager.GetComponentData<Temp>(m_Temp);
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "StationPlace [temp]: #{0} Temp building {1} after {2} frame(s): flags {3}, cost {4}, at {5}; {6} Temp building(s) in the world",
                    CurSerial, Fmt(m_Temp), waited, t0.m_Flags, t0.m_Cost, PosOf(m_Temp), m_TempBuildingQuery.CalculateEntityCount()));
            }
            if (m_Frame - m_SeenFrame < SettleFrames)
                return inputDeps;
            if (!EntityManager.Exists(m_Temp) || !EntityManager.HasComponent<Temp>(m_Temp))
            {
                m_Temp = Entity.Null;   // recycled: look again next frame (the timeout still counts from the emission)
                m_SeenFrame = -1;
                return inputDeps;
            }
            JobHandle deps = DestroyDefinitions(m_DefinitionQuery, m_ToolOutputBarrier, inputDeps);
            if (!GetAllowApply())
            {
                int errors = m_ErrorQuery.CalculateEntityCount();
                string detail = DescribeErrors(out string summary, out string first);
                applyMode = ApplyMode.Clear;   // ToolClearSystem deletes the Temps (ToolClearSystem.cs:63-113)
                Mod.Log.Warn($"StationPlace [verdict]: #{CurSerial} REFUSED — {errors} error(s) on the preview: {summary}; nothing placed{detail}");
                Finish(Entity.Null, PlaceStatus.Refused, $"refused by the game: {summary}", summary, first);
                NextOrDeactivate("refused");
                return deps;
            }
            Temp t = EntityManager.GetComponentData<Temp>(m_Temp);
            applyMode = ApplyMode.Apply;   // ToolOutputSystem.cs:22-28 leads to ApplyObjectsSystem / ApplyNetSystem / ApplyAreasSystem
            m_Phase = PlacePhase.Applying;
            m_ApplyFrame = m_Frame;
            Mod.Log.Info($"StationPlace [verdict]: #{CurSerial} APPLY — Temp {Fmt(m_Temp)} validated (flags {t.m_Flags}, cost {t.m_Cost}), " +
                         $"{waited} frame(s) after the emission");
            return deps;
        }

        /// <summary>The frame after Apply: the Temp entity is now the permanent station (ApplyObjectsSystem.cs:670 keeps the entity).</summary>
        private void FinishApplied()
        {
            Entity e = m_Temp;
            if (EntityManager.Exists(e) && EntityManager.HasComponent<Temp>(e) && m_Frame - m_ApplyFrame < ApplyWaitFrames)
                return;   // still tagged: give the apply chain a few frames (expected: permanent on the first frame after Apply)
            if (!EntityManager.Exists(e) || EntityManager.HasComponent<Temp>(e) || EntityManager.HasComponent<Deleted>(e))
            {
                // Fall back to the newest permanent building of the prefab at the pose (the Temp entity was not kept)
                Entity found = FindPermanent();
                Mod.Log.Warn($"StationPlace [applied]: #{CurSerial} the Temp {Fmt(e)} is not the permanent station the frame after Apply " +
                             $"(exists {EntityManager.Exists(e)}); by pose: {(found == Entity.Null ? "none" : Fmt(found))}");
                e = found;
            }
            if (e == Entity.Null)
            {
                Finish(Entity.Null, PlaceStatus.Refused, "Apply ran but no permanent station was found", "not found", "NotFound");
                NextOrDeactivate("applied, not found");
                return;
            }
            long after = CityMoney();
            Mod.Log.Info($"StationPlace [applied]: #{CurSerial} APPLIED — {DescribeStation(e)}; money {m_MoneyBefore} -> {after} " +
                         $"(paid {m_MoneyBefore - after}, prefab cost {CostOf(m_Prefab)})");
            m_Watches.Add((e, m_Frame + FollowUpFrames));
            if (m_Current != null)
                m_Current.Paid = m_MoneyBefore - after;
            Finish(e, PlaceStatus.Placed, $"placed {Fmt(e)}", null, null);
            NextOrDeactivate("applied");
        }


        private int CurSerial => m_Current != null ? m_Current.Serial : m_Serial;

        /// <summary>The request in flight ends: its outcome, LastResult, phase Idle.</summary>
        private void Finish(Entity placed, PlaceStatus status, string summary, string errorTypes, string firstError)
        {
            m_Result = new PlaceResult(placed, summary, CurSerial);
            if (m_Current != null)
            {
                m_Current.Status = status;
                m_Current.Placed = placed;
                m_Current.Summary = summary;
                m_Current.ErrorTypes = errorTypes;
                m_Current.FirstError = firstError;
                Mod.Log.Info($"StationPlace [outcome]: #{m_Current.Serial}{TagText(m_Current.Tag)} {status} — {summary}; {m_Queue.Count} request(s) still queued");
            }
            m_Current = null;
            m_Phase = PlacePhase.Idle;
            m_Temp = Entity.Null;
            m_SeenFrame = -1;
        }

        /// <summary>After a verdict: the next queued request starts in the next update (the tool stays active), else the tool goes back to
        /// the default tool.</summary>
        private void NextOrDeactivate(string why)
        {
            if (m_Queue.Count > 0)
            {
                StartNext();
                Mod.Log.Info($"StationPlace [tool]: next request #{CurSerial}{TagText(m_Current?.Tag)} ({why} before it); the tool stays active");
                return;
            }
            Deactivate(why);
        }

        private void Deactivate(string why)
        {
            if (m_ToolSystem != null && m_ToolSystem.activeTool == this)
            {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
                Mod.Log.Info($"StationPlace [tool]: deactivated ({why}) — default tool; result #{m_Result.Serial}: {m_Result.Summary}");
            }
        }

        /// <summary>The y of a request without one, by <paramref name="rule"/> (StationAlign.StationY). VanillaFront: the terrain at the
        /// front edge of the lot (local +z, lot depth x 4 m) plus the placement offset (see the header). LowestOfThree: the lowest terrain
        /// of the front edge, the origin and the line-side edge (local -z, the stub tip side) plus the offset. Fit: LowestOfThree on a pad
        /// station, the origin on a slope station (the prefab's BuildingTerraformData). One log line per request with the three heights,
        /// the rule, the station kind and the chosen y (the vanilla y beside it).</summary>
        private float ResolveY(Entity prefab, float3 p, quaternion rotation, StationAlign.StationHeightRule rule)
        {
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            float lotDepthM = float.NaN;
            string lot = "no BuildingData: origin only";
            if (EntityManager.TryGetComponent(prefab, out BuildingData bd))
            {
                // the lot front = BuildingUtils.CalculateFrontPosition (local z +depth x 4), the line side = local z -depth x 4
                lotDepthM = bd.m_LotSize.y * 4f;
                float3 front = p + math.mul(rotation, new float3(0f, 0f, lotDepthM)), back = p + math.mul(rotation, new float3(0f, 0f, -lotDepthM));
                lot = string.Format(CultureInfo.InvariantCulture, "lot {0}x{1}, front edge ({2:F1}, {3:F1}), line-side edge ({4:F1}, {5:F1})",
                    bd.m_LotSize.x, bd.m_LotSize.y, front.x, front.z, back.x, back.z);
            }
            float offset = 0f;
            if (EntityManager.TryGetComponent(prefab, out PlaceableObjectData pod))
            {
                const Game.Objects.PlacementFlags water = Game.Objects.PlacementFlags.Hovering | Game.Objects.PlacementFlags.Shoreline |
                                                          Game.Objects.PlacementFlags.Floating;
                if ((pod.m_Flags & water) != 0)
                    Mod.Log.Warn($"StationPlace [height]: the prefab has water placement flags ({pod.m_Flags & water}); the vanilla water " +
                                 "branch is NOT reproduced, the plain branch is used");
                offset = pod.m_PlacementOffset.y;
            }
            // The station kind comes from the prefab's own terrain flags (BuildingTerraformData, set from its BuildingTerraformOverride at
            // BuildingInitializeSystem.cs:1364-1365; every BuildingPrefab has the component, BuildingPrefab.cs:36). No override = Pad.
            StationAlign.StationTerrainKind kind = EntityManager.TryGetComponent(prefab, out BuildingTerraformData td)
                ? StationAlign.TerrainKindOf(td.m_DontRaise, td.m_DontLower) : StationAlign.StationTerrainKind.Pad;
            // The queue's station height rule is one pure function, shared with the planner's and the realizer's tower checks
            // (StationAlign.PlaceStation, LiftLineInput.PlanQueueLine), so a line they call sound is the line the queue plans.
            TerrainHeightData h = hd;
            StationAlign.QueueStation qs = StationAlign.PlaceStation(rule, kind, p, rotation, lotDepthM, float.NaN, 0f, float.NaN, offset,
                q => TerrainUtils.SampleHeight(ref h, q));
            float originGround = qs.OriginGround, frontGround = qs.FrontGround, lineGround = qs.LineSideGround;
            float vanilla = StationAlign.StationY(StationAlign.StationHeightRule.VanillaFront, kind, frontGround, originGround, lineGround, offset);
            float y = qs.StationY;
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "StationPlace [height]: #{0}{1} rule {2}, {12} station: terrain front {3:F2}, origin {4:F2}, line side {5:F2}; placement offset {6:F2} -> y {7:F2} " +
                "(vanilla y {8:F2}, {9:F2} m lower; y minus the origin terrain {10:F2}); {11}",
                CurSerial, TagText(m_Current?.Tag), rule, frontGround, originGround, lineGround, offset, y, vanilla, vanilla - y, y - originGround, lot,
                kind == StationAlign.StationTerrainKind.Slope ? "slope (DontRaise + DontLower)" : "pad"));
            return y;
        }

        private Entity FindTemp(out int count)
        {
            count = 0;
            Entity best = Entity.Null;
            float bestD = OwnTempM;
            using (NativeArray<Entity> temps = m_TempBuildingQuery.ToEntityArray(Allocator.Temp))
            {
                count = temps.Length;
                for (int i = 0; i < temps.Length; i++)
                {
                    Entity e = temps[i];
                    if (EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != m_Prefab)
                        continue;
                    Temp t = EntityManager.GetComponentData<Temp>(e);
                    if (t.m_Original != Entity.Null || (t.m_Flags & TempFlags.Create) == 0)
                        continue;
                    // A selected building's Temp copy carries Select / an original (P0 note 1): only ours stands at the requested xz
                    if (!EntityManager.TryGetComponent(e, out Game.Objects.Transform tr))
                        continue;
                    float d = math.distance(tr.m_Position.xz, m_Position.xz);
                    if (d <= bestD)
                    {
                        bestD = d;
                        best = e;
                    }
                }
            }
            return best;
        }

        /// <summary>A permanent building of the request's prefab within 0.5 m (xz) of the requested position.</summary>
        private Entity FindPermanent()
        {
            using (NativeArray<Entity> all = m_BuildingQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (EntityManager.GetComponentData<PrefabRef>(all[i]).m_Prefab != m_Prefab)
                        continue;
                    float3 p = EntityManager.GetComponentData<Game.Objects.Transform>(all[i]).m_Position;
                    if (math.distance(p.xz, m_Position.xz) < 0.5f)
                        return all[i];
                }
            }
            return Entity.Null;
        }

        /// <summary>The station as the probe reads it: Transform, Elevation, Lot (pad heights), sub-net nodes with positions, sub-objects,
        /// sub-areas, and the terrain under the origin.</summary>
        private string DescribeStation(Entity e)
        {
            var sb = new StringBuilder();
            sb.Append(Fmt(e)).Append(' ').Append(PrefabNameOf(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab));
            if (EntityManager.TryGetComponent(e, out Game.Objects.Transform tr))
            {
                TerrainHeightData hd = m_TerrainSystem.GetHeightData();
                float ground = TerrainUtils.SampleHeight(ref hd, tr.m_Position);
                sb.AppendFormat(CultureInfo.InvariantCulture, "; Transform ({0:F2}, {1:F2}, {2:F2}) yaw {3:F1} deg; terrain at the origin now {4:F2}",
                    tr.m_Position.x, tr.m_Position.y, tr.m_Position.z, YawDeg(tr.m_Rotation), ground);
            }
            if (EntityManager.TryGetComponent(e, out Game.Objects.Elevation el))
                sb.AppendFormat(CultureInfo.InvariantCulture, "; Elevation {0:F2}", el.m_Elevation);
            if (EntityManager.TryGetComponent(e, out Game.Buildings.Lot lot))
                sb.Append("; Lot front ").Append(F3(lot.m_FrontHeights)).Append(" right ").Append(F3(lot.m_RightHeights))
                  .Append(" back ").Append(F3(lot.m_BackHeights)).Append(" left ").Append(F3(lot.m_LeftHeights));
            else
                sb.Append("; no Lot");
            var nodes = new List<Entity>();
            int edges = 0;
            if (EntityManager.TryGetBuffer(e, true, out DynamicBuffer<Game.Net.SubNet> subNets))
            {
                for (int i = 0; i < subNets.Length; i++)
                {
                    Entity n = subNets[i].m_SubNet;
                    if (EntityManager.TryGetComponent(n, out Game.Net.Edge edge))
                    {
                        edges++;
                        AddUnique(nodes, edge.m_Start);
                        AddUnique(nodes, edge.m_End);
                    }
                    else if (EntityManager.HasComponent<Game.Net.Node>(n))
                        AddUnique(nodes, n);
                }
            }
            sb.Append("; sub-nets ").Append(edges).Append(" edge(s), ").Append(nodes.Count).Append(" node(s)");
            foreach (Entity n in nodes)
            {
                if (EntityManager.TryGetComponent(n, out Game.Net.Node node))
                    sb.AppendFormat(CultureInfo.InvariantCulture, " [{0} {1} ({2:F2}, {3:F2}, {4:F2})]", Fmt(n), PrefabNameOfInstance(n),
                        node.m_Position.x, node.m_Position.y, node.m_Position.z);
            }
            int subObjects = EntityManager.TryGetBuffer(e, true, out DynamicBuffer<Game.Objects.SubObject> so) ? so.Length : 0;
            int subAreas = EntityManager.TryGetBuffer(e, true, out DynamicBuffer<Game.Areas.SubArea> sa) ? sa.Length : 0;
            sb.Append("; sub-objects ").Append(subObjects).Append(", sub-areas ").Append(subAreas);
            return sb.ToString();
        }

        /// <summary>Every entity the game flagged (the Error query of GetAllowApply, ToolBaseSystem.cs:313, 533-540): kind, prefab, Temp
        /// flags and the error types of its icons (IconElement → icon PrefabRef → ToolErrorData.m_Error, as LiftLineToolSystem reads them).
        /// <paramref name="summary"/> counts per type. <paramref name="first"/> is the first typed error met (null when none is typed).</summary>
        private string DescribeErrors(out string summary, out string first)
        {
            first = null;
            var counts = new SortedDictionary<string, int>();
            var sb = new StringBuilder();
            using (NativeArray<Entity> flagged = m_ErrorQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < flagged.Length && i < 20; i++)
                {
                    Entity e = flagged[i];
                    string kind = EntityManager.HasComponent<Game.Buildings.Building>(e) ? "building" : EntityManager.HasComponent<Game.Net.Edge>(e) ? "edge"
                        : EntityManager.HasComponent<Game.Net.Node>(e) ? "node" : EntityManager.HasComponent<Game.Objects.Object>(e) ? "object"
                        : EntityManager.HasComponent<Game.Areas.Area>(e) ? "area" : "other";
                    sb.Append("\n  ").Append(kind).Append(' ').Append(Fmt(e)).Append(' ').Append(PrefabNameOfInstance(e));
                    if (EntityManager.TryGetComponent(e, out Temp temp))
                        sb.Append(" Temp ").Append(temp.m_Flags);
                    if (!EntityManager.TryGetBuffer(e, true, out DynamicBuffer<Game.Notifications.IconElement> icons) || icons.Length == 0)
                    {
                        sb.Append(": no icon");
                        Count(counts, "untyped");
                        continue;
                    }
                    sb.Append(':');
                    for (int k = 0; k < icons.Length; k++)
                    {
                        string type = "untyped";
                        if (EntityManager.TryGetComponent(icons[k].m_Icon, out PrefabRef ipr) &&
                            EntityManager.TryGetComponent(ipr.m_Prefab, out ToolErrorData ted))
                            type = ted.m_Error.ToString();
                        if (first == null && type != "untyped")
                            first = type;
                        Count(counts, type);
                        sb.Append(' ').Append(type);
                        if (EntityManager.TryGetComponent(icons[k].m_Icon, out Target target) && target.m_Target != Entity.Null)
                            sb.Append(" (with ").Append(Fmt(target.m_Target)).Append(' ').Append(PrefabNameOfInstance(target.m_Target)).Append(')');
                    }
                }
                if (flagged.Length > 20)
                    sb.Append("\n  ... ").Append(flagged.Length - 20).Append(" more");
            }
            var sum = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in counts)
                sum.Append(sum.Length > 0 ? ", " : string.Empty).Append(kv.Key).Append(" x").Append(kv.Value);
            summary = sum.Length > 0 ? sum.ToString() : "no error entity found (an original was deleted?)";
            return sb.ToString();
        }

        private static void Count(SortedDictionary<string, int> counts, string key) =>
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

        private static void AddUnique(List<Entity> list, Entity e)
        {
            if (e != Entity.Null && !list.Contains(e))
                list.Add(e);
        }

        private long CityMoney()
        {
            Entity city = m_CitySystem.City;
            return city != Entity.Null && EntityManager.TryGetComponent(city, out PlayerMoney money) ? money.money : long.MinValue;
        }

        private string CostOf(Entity prefab) =>
            EntityManager.TryGetComponent(prefab, out PlaceableObjectData pod) ? pod.m_ConstructionCost.ToString(CultureInfo.InvariantCulture) : "?";

        private string PrefabNameOf(Entity prefab) => prefab != Entity.Null ? m_PrefabSystem.GetPrefabName(prefab) : "none";

        private string PrefabNameOfInstance(Entity e) =>
            EntityManager.TryGetComponent(e, out PrefabRef pr) && pr.m_Prefab != Entity.Null ? m_PrefabSystem.GetPrefabName(pr.m_Prefab) : "?";

        private string PosOf(Entity e) =>
            EntityManager.TryGetComponent(e, out Game.Objects.Transform tr)
                ? string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2}, {2:F2})", tr.m_Position.x, tr.m_Position.y, tr.m_Position.z)
                : "(no Transform)";

        /// <summary>Yaw of local +z in degrees, the trigger's convention: 0 = +z, 90 = +x (quaternion.RotateY).</summary>
        internal static float YawDeg(quaternion q)
        {
            float3 f = math.rotate(q, new float3(0f, 0f, 1f));
            return math.degrees(math.atan2(f.x, f.z));
        }

        private static string Dir(float3 d) => string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2})", d.x, d.z);

        private static string F3(float3 v) => string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2}, {2:F2})", v.x, v.y, v.z);

        private static string Fmt(Entity e) => e.Index + ":" + e.Version;

        private void Fail(Exception e, string where)
        {
            m_Disabled = true;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(StationPlaceToolSystem)} EXCEPTION in {where} — the station placer is disabled until the next game " +
                              $"load. Please report this stack:\n{e}");
            }
        }
    }
}
