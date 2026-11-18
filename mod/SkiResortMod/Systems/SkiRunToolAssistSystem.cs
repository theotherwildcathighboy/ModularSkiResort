// SkiRunToolAssistSystem: two small helpers for the run drawing tool (1.0.2). They work without the slope overlay's rating.
//
// T244, Esc restarts the outline. Vanilla Esc never reaches the area tool in its Create state: AreaToolSystem.UpdateCancelAction keeps
// cancelAction disabled there. Esc goes to the UI instead. The asset menu closes (onClose = toolbar.clearAssetSelection), and
// ToolbarUISystem.ClearAssetSelection calls ToolSystem.ActivatePrefabTool(null), which selects the default tool. So one Esc drops the whole
// tool and loses the outline. There is no Harmony in this mod, so the system cannot take the key first. It undoes the switch instead: when
// the tool changed from our run tool (a new outline with at least one placed point) to the default tool within EscWindowFrames of an Esc
// press, it activates the same run prefab again. AreaToolSystem.OnStartRunning clears its control points, so the outline starts over with
// the tool still in hand. A second Esc (no placed point) leaves the tool as vanilla does. Needs a game check (the hub and the asset menu
// may also react to the key).
//
// T243b, money icon. Vanilla ValidationSystem shows NotEnoughMoney only on a Temp with Temp.m_Cost > 0. A run Temp carries no cost (the fee
// is charged at the bind). This system adds the same vanilla icon on the new run's Temp when the fee is above the city balance. It does not
// block the creation: an icon adds no Game.Tools.Error component, so ApplyAreasSystem still applies the area.
//
// Phase: ModificationEnd, before IconCommandSystem (Mod.cs). The Temp seen here is this frame's Temp, and the icon command plays back in
// the same phase, before the area tool replaces the Temp. Placed in Rendering, the command would play back one frame later, and a Temp the
// tool rebuilt in between (the cursor moved) has no owner left: IconCommandSystem skips the command.

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Notifications;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunToolAssistSystem : GameSystemBase
    {
        /// <summary>Frames between the Esc press, the last frame of the run tool, and the switch to the default tool. The UI handles the key
        /// in its own update, so the switch can land a frame or two after the press.</summary>
        public const int EscWindowFrames = 10;
        /// <summary>Log lines per load for the Esc reset (one per reset).</summary>
        public const int EscLogMax = 20;

        private ToolSystem m_ToolSystem;
        private AreaToolSystem m_AreaTool;
        private PrefabSystem m_PrefabSystem;
        private SkiRunOwnershipSystem m_Ownership;
        private IconCommandSystem m_IconCommandSystem;
        private CitySystem m_CitySystem;
        private SkiRunTooltipSystem m_Tooltip;
        private EntityQuery m_TempQuery;
        private EntityQuery m_ErrorIconQuery;

        private bool m_Armed;
        private bool m_Disabled;

        // Run prefab cache (AreaPrefab -> ours?).
        private AreaPrefab m_CachedPrefab;
        private bool m_CachedIsRun;

        // T244 state.
        private AreaPrefab m_DrawPrefab;
        private int m_DrawFrame = int.MinValue / 2;
        private int m_EscFrame = int.MinValue / 2;
        private int m_EscLogs;

        // T243b state.
        private Entity m_MoneyIcon;
        private Entity m_IconOwner;

        // Dev readouts (plain fields).
        public int EscResets;
        public int MoneyIconsAdded;
        public bool MoneyIconShown;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_AreaTool = World.GetOrCreateSystemManaged<AreaToolSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_IconCommandSystem = World.GetOrCreateSystemManaged<IconCommandSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_Tooltip = World.GetOrCreateSystemManaged<SkiRunTooltipSystem>();
            m_TempQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            m_ErrorIconQuery = GetEntityQuery(ComponentType.ReadOnly<NotificationIconData>(), ComponentType.ReadOnly<ToolErrorData>());
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = false;
            m_CachedPrefab = null;
            m_CachedIsRun = false;
            m_DrawPrefab = null;
            m_DrawFrame = m_EscFrame = int.MinValue / 2;
            m_EscLogs = 0;
            m_IconOwner = Entity.Null;
            EscResets = MoneyIconsAdded = 0;
            MoneyIconShown = false;
            m_MoneyIcon = FindErrorIcon(ErrorType.NotEnoughMoney);
            m_Armed = mode == GameMode.Game;
            if (!m_Armed)
                return;
            Mod.Log.Info($"RunToolAssist: ARMED. Esc with a started outline restarts it (window {EscWindowFrames} frames); money icon " +
                         $"{(m_MoneyIcon != Entity.Null ? "found" : "MISSING (no icon on an unaffordable run)")}");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                int frame = UnityEngine.Time.frameCount;
                bool runTool = RunToolActive();
                float3 at = default;
                Entity temp = runTool ? FindNewRunTemp(out at) : Entity.Null;
                EscReset(frame, runTool, temp != Entity.Null);
                MoneyIcon(runTool ? temp : Entity.Null, at);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! RunToolAssist: disabled until the next load after an exception: {e}");
            }
        }

        /// <summary>The area tool is active with one of our run prefabs.</summary>
        private bool RunToolActive()
        {
            if (m_ToolSystem.activeTool != m_AreaTool || m_Ownership == null || !m_Ownership.PrefabSetsReady)
                return false;
            AreaPrefab prefab = m_AreaTool.prefab;
            if (prefab == null)
                return false;
            if (!ReferenceEquals(prefab, m_CachedPrefab))
            {
                m_CachedPrefab = prefab;
                m_CachedIsRun = m_PrefabSystem.TryGetEntity(prefab, out Entity e) && m_Ownership.IsRunPrefab(e);
            }
            return m_CachedIsRun;
        }

        /// <summary>The new run's Temp (no original) with at least one placed point (2+ nodes: the placed points plus the cursor node; a
        /// 1-node Temp is the cursor preview). Ties: the most nodes. at = the icon location (Geometry center, else the node average).</summary>
        private Entity FindNewRunTemp(out float3 at)
        {
            at = default;
            if (m_TempQuery.IsEmptyIgnoreFilter)
                return Entity.Null;
            Entity best = Entity.Null;
            int bestNodes = 1;
            NativeArray<Entity> entities = m_TempQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int k = 0; k < entities.Length; k++)
                {
                    Entity e = entities[k];
                    if (!m_Ownership.IsRunPrefab(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab))
                        continue;
                    Temp t = EntityManager.GetComponentData<Temp>(e);
                    if (t.m_Original != Entity.Null || (t.m_Flags & (TempFlags.Delete | TempFlags.Hidden)) != 0)
                        continue;
                    DynamicBuffer<Game.Areas.Node> nodes = EntityManager.GetBuffer<Game.Areas.Node>(e, isReadOnly: true);
                    if (nodes.Length <= bestNodes)
                        continue;
                    best = e;
                    bestNodes = nodes.Length;
                    if (EntityManager.HasComponent<Game.Areas.Geometry>(e))
                        at = EntityManager.GetComponentData<Game.Areas.Geometry>(e).m_CenterPosition;
                    else
                    {
                        float3 sum = default;
                        for (int i = 0; i < nodes.Length; i++)
                            sum += nodes[i].m_Position;
                        at = sum / nodes.Length;
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }
            return best;
        }

        /// <summary>T244. Records the Esc press and the last frame of a started outline; undoes an Esc tool switch (see the file header).</summary>
        private void EscReset(int frame, bool runTool, bool started)
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                m_EscFrame = frame;

            if (runTool)
            {
                if (started)
                {
                    m_DrawPrefab = m_AreaTool.prefab;
                    m_DrawFrame = frame;
                }
                return;
            }
            if (m_DrawPrefab == null)
                return;
            AreaPrefab prefab = m_DrawPrefab;
            m_DrawPrefab = null; // one decision per switch
            // Only an Esc switch to the default tool: a click on another tool or on the toolbar keeps vanilla behavior.
            if (frame - m_DrawFrame > EscWindowFrames || frame - m_EscFrame > EscWindowFrames || !(m_ToolSystem.activeTool is DefaultToolSystem))
                return;
            bool ok = m_ToolSystem.ActivatePrefabTool(prefab);
            if (ok)
                EscResets++;
            if (m_EscLogs < EscLogMax)
            {
                m_EscLogs++;
                Mod.Log.Info($"RunToolAssist [esc]: {(ok ? "outline restarted" : "re-activation REFUSED")} for {prefab.name} " +
                             $"(Esc {frame - m_EscFrame} frame(s) ago, last outline frame {frame - m_DrawFrame} ago)");
            }
        }

        /// <summary>T243b. The vanilla NotEnoughMoney icon on the new run's Temp while the fee is above the balance. Re-added when the tool
        /// rebuilds the Temp (a new entity), removed when the fee becomes affordable. A Temp icon dies with its Temp (ToolClearSystem).</summary>
        private void MoneyIcon(Entity temp, float3 at)
        {
            if (m_MoneyIcon == Entity.Null)
                return;
            Entity owner = Entity.Null;
            if (temp != Entity.Null && m_Tooltip.TryGetNewRunFee(out int fee) && fee > m_CitySystem.moneyAmount)
                owner = temp;
            MoneyIconShown = owner != Entity.Null;
            if (owner == m_IconOwner)
                return;
            IconCommandBuffer icb = m_IconCommandSystem.CreateCommandBuffer();
            if (m_IconOwner != Entity.Null && EntityManager.Exists(m_IconOwner))
                icb.Remove(m_IconOwner, m_MoneyIcon);
            if (owner != Entity.Null)
            {
                icb.Add(owner, m_MoneyIcon, at, IconPriority.Error, IconClusterLayer.Default, IconFlags.IgnoreTarget, Entity.Null, isTemp: true);
                MoneyIconsAdded++;
            }
            m_IconOwner = owner;
        }

        private Entity FindErrorIcon(ErrorType error)
        {
            NativeArray<Entity> entities = m_ErrorIconQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int k = 0; k < entities.Length; k++)
                    if (EntityManager.GetComponentData<ToolErrorData>(entities[k]).m_Error == error)
                        return entities[k];
            }
            finally
            {
                entities.Dispose();
            }
            return Entity.Null;
        }
    }
}
