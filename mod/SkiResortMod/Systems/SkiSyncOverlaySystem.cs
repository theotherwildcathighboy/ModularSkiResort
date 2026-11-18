// World markers for Sync Resort: while the hub's Sync card is open, a ring around every listed object of the card's area (DECISIONS 670:
// "world icons only while the Sync card is open"). Amber = waits for Sync, white = queued or working, red = failed, gray = edited by hand
// (skipped). Nothing shows while the card is closed.
// Drawn with OverlayRenderSystem.Buffer (projected circles), like the merge overlay: nothing is an entity, nothing is saved, and no icon prefab
// has to be registered by every later version. A real notification icon would be saved with the city when the player saves while the card
// is open, and its prefab would have to stay registered forever (design 2, 4.4). The card's "Fly to" buttons stand in for an icon click.
// Phase Rendering, UpdateBefore OverlayRenderSystem (Mod.cs; load-bearing: the renderer uploads and clears its lists in the same frame).
// Main thread: GetBuffer(out deps), deps.Complete() (SkiMergeOverlaySystem pattern). Reads SkiSyncSystem's list only while the card is open.
// Engine notes: EngineNotes/SkiSyncSystem.md

using System;
using Colossal.Entities;
using Game;
using Game.Prefabs;
using Game.Rendering;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiSyncOverlaySystem : GameSystemBase
    {
        /// <summary>Ring diameter = the object's footprint diagonal + this margin, never below MinDiameterM.</summary>
        private const float MarginM = 10f;
        private const float MinDiameterM = 24f;
        private const float OutlineM = 2f;

        // sRGB / 255 (OverlayRenderSystem converts with Color.linear)
        private static readonly UnityEngine.Color s_Open = new UnityEngine.Color(1f, 0.69f, 0.18f, 0.95f);
        private static readonly UnityEngine.Color s_Busy = new UnityEngine.Color(1f, 1f, 1f, 0.9f);
        private static readonly UnityEngine.Color s_Failed = new UnityEngine.Color(0.94f, 0.27f, 0.23f, 0.95f);
        private static readonly UnityEngine.Color s_Hand = new UnityEngine.Color(0.62f, 0.62f, 0.62f, 0.85f);
        private static readonly UnityEngine.Color s_Clear = new UnityEngine.Color(0f, 0f, 0f, 0f);

        private SkiSyncSystem m_Sync;
        private OverlayRenderSystem m_Overlay;
        private bool m_ErrorLogged;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Sync = World.GetOrCreateSystemManaged<SkiSyncSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
        }

        protected override void OnUpdate()
        {
            if (!m_Sync.CardOpen || m_ErrorLogged)
                return;
            try
            {
                Draw();
            }
            catch (Exception e)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiSyncOverlaySystem)} EXCEPTION — the Sync card's world markers are off until the game restarts. " +
                              $"Please report this stack:\n{e}");
            }
        }

        private void Draw()
        {
            var items = m_Sync.Items;
            if (items.Count == 0)
                return;
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);   // OverlayRenderSystem.cs:603-634
            deps.Complete();
            var flags = OverlayRenderSystem.StyleFlags.Projected;
            Entity sign = m_Sync.CardSign;
            for (int i = 0; i < items.Count; i++)
            {
                SkiSyncSystem.Item it = items[i];
                if (m_Sync.AreaOf(it.Entity) != sign || !m_Sync.TryGetPosition(it.Entity, out float3 pos))
                    continue;
                UnityEngine.Color color;
                switch (it.State)
                {
                    case SkiSyncSystem.ItemState.Queued:
                    case SkiSyncSystem.ItemState.Working: color = s_Busy; break;
                    case SkiSyncSystem.ItemState.Failed: color = s_Failed; break;
                    case SkiSyncSystem.ItemState.HandEdited: color = s_Hand; break;
                    default: color = it.Include ? s_Open : s_Hand; break;
                }
                float diameter = MinDiameterM;
                if (EntityManager.TryGetComponent(it.Entity, out PrefabRef pr) && EntityManager.TryGetComponent(pr.m_Prefab, out ObjectGeometryData g))
                {
                    float3 size = g.m_Bounds.max - g.m_Bounds.min;
                    diameter = math.max(MinDiameterM, math.length(size.xz) + MarginM);
                }
                buffer.DrawCircle(color, s_Clear, OutlineM, flags, new float2(0f, 1f), pos, diameter);
            }
        }
    }
}
