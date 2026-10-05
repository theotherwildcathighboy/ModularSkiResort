// SkiRunSplatSystem — mirrors painted ski-run polygons into the game's terrain-material
// USER SPLATMAP, so runs render as a real terrain material (triplanar, relief-shaded,
// vanilla snow stacks on top) instead of a top-down decal smear. The architecture is
// docs/research/terrain-material-runs.md verbatim: "polygons are data, splat is paint" —
// the RenderedArea polygons stay the gameplay layer (attractiveness, snowmaking, identity);
// this system only PAINTS, and repaints the whole claimed channel on any change.
//
// ─────────────────────────────────────────────────────────────────────────────────────────
// THE MACHINERY (all verified against fullsrc this round; line refs = TerrainMaterialSystem.cs)
//
//   * TerrainMaterialSystem.ApplyBrush(Brush, brushPrefabEntity) is PUBLIC (line 815). It
//     stamps one dab into m_UserSplatmap (4096², GraphicsFormat.R8G8_UNorm, line 758) AND
//     m_UserSplatmapWorld (1024²), via the PaintDither/EraseDither compute kernels —
//     strength > 0 paints, strength < 0 erases (line 872). Both splatmaps serialize into
//     the save (FormatTags.TerrainUserSplatmap), so paint persists; we rebuild the channel
//     from polygons every load anyway (see INVARIANT below).
//     NOTE the R8G8 format: the research doc assumed RGBA. Four Extra channels through two
//     stored channels + kernels named *Dither* implies index-dithered encoding; either way
//     ApplyBrush owns the encoding and vanilla editor maps DO use Extra3/4. The load-time
//     measurement log below is the instrument that settles the open resolution/format
//     question in terrain-material-runs.md.
//   * Channel selection: ApplyToSplatmap → GetMaterialType(brush.m_Tool) (line 805) reads
//     TerrainMaterialProperties.m_Type from the TOOL prefab, which must resolve through
//     PrefabSystem.GetPrefab<TerraformingPrefab>. There is no vanilla TerraformingPrefab
//     with a non-None type outside editor-configured maps, so we register our own tool
//     prefab per claimed slot (public PrefabSystem.AddPrefab — creates the entity
//     immediately, PrefabSystem.cs line 145; no Harmony, no reflection). The name encodes
//     the slot ("SkiResortSplatToolExtra3") so GetMaterialType's per-entity cache can never
//     go stale across loads that claim different slots.
//     GAME MODE ONLY: the map editor's TerrainPanelSystem.OnGameLoaded builds a material-
//     paint button for EVERY Material-target TerraformingPrefab — no UIObject required,
//     the icon just falls back to Media/Editor/Material.svg (splatmap-encoding.md disproved
//     the old "no UIObject = never visible" assumption). So editor loads never claim a slot
//     or register the tool prefab, and OnGamePreload removes any tool prefab left over from
//     an earlier Game load this session BEFORE the panel builds its buttons (preload fires
//     before LoadGameSystem raises OnGameLoaded).
//   * Slot textures: TerrainRenderSettingsPrefab.m_ExtraNDiffuse/m_ExtraNNormal are
//     AssetReference<TextureAsset> — NOT raw textures. ApplyRenderSettings (line 1058)
//     resolves them via AssetDatabase.global lookup and Shader.SetGlobalTexture. So the
//     claim is: load PNGs → Texture2D (LoadImage, mips), register as TextureAssets in
//     AssetDatabase.user (in-memory index entry only — FileSystemDataSource.
//     AddEntryFromDatabase writes nothing to disk; we never Save), assign the prefab
//     fields (implicit TextureAsset→AssetReference), then re-assign
//     currentTerrainRenderSettings to itself (property setter, line 654) which runs
//     ApplyRenderSettings. TextureAsset.SetData pins the texture as the asset's live
//     instance; Load() then returns that instance without touching disk, and the
//     ReleaseTextures/Load refcount cycle inside repeated ApplyRenderSettings calls never
//     drops the object refcount to zero (SetData holds one) — the claim survives quality
//     changes and our own seasonal re-applies.
//   * Collision policy (settled in terrain-material-runs.md): slot occupancy is purely a
//     per-map settings question. We claim the first FREE slot in order Extra3 → Extra4 →
//     Extra2 → Extra1 (free = field null or invalid guid, or carrying OUR asset from an
//     earlier load this session — prefab objects persist in PrefabSystem across loads).
//     No free slot ⇒ loud log, graceful disable for the load. The prefab mutation is
//     runtime-only: nothing calls asset.Save, so map assets on disk are never touched.
//
// INVARIANT: the claimed channel is fully DERIVED state. Every load: wipe the channel
// across the playable area, then re-stamp every live run polygon. Every change event
// afterwards: erase the previous pass's union bounds, re-stamp everything (full channel
// re-rasterize — the research doc's answer to overlap/erase bookkeeping). Stale paint from
// sessions where the mod was absent is cleaned by the load wipe; paint left in saves after
// UNINSTALLING the mod is the known removal footprint (documented, not solvable from here).
//
// SEASONAL: SnowLineSystem is the climate oracle. Winter-ish := snow line at/below the
// mean run mid-elevation OR ambient at/below freezing (with hysteresis on both, so the
// boundary can't flap). On transition: swap the claimed slot's diffuse/normal references
// and re-assign currentTerrainRenderSettings. The splatmap never changes with seasons —
// only the cloth does. Checked every SeasonCheckFrames frames + once at load.
//
// SCHEDULING: PreCulling, anchored before TerrainMaterialSystem (same phase, runs every
// frame in Game AND Editor, paused included — area edits arrive through tool phases that
// ignore simulation pause, and Created/Updated/Deleted tags live until CleanUpSystem at
// Cleanup, so a per-frame phase reliably sees every event). Rasterization is debounced:
// events only mark a pending flag; at most one rasterize per RasterizeDebounceFrames.
//
// OBSERVABILITY (v1.2.1 discipline — silence must be impossible): resolved state logged at
// load (claim/slot/textures/brushes or the precise reason for inertness), every rasterize
// pass logs its outcome (runs/triangles/dab counts/bounds/ms), season transitions log
// their inputs, one-time diagnostics for the brush-prefab inventory and the splatmap
// measurement (a deliverable — see the log format in the header of LogSplatmapOnce).
//
// DEFENSIVE CONTRACT (ChairPinSystem precedent): never throw into the game loop — top-level
// try/catch per update and per load hook, one-shot "!!!" error log, disabled for the
// current load only, re-armed at OnGameLoadingComplete.
// ─────────────────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Prefabs.Terrain;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunSplatSystem : GameSystemBase
    {
        // ── Tuning — the single block ────────────────────────────────────────────────────
        private static class Tuning
        {
            // Paint dabs: soft round brush, grid-clipped to triangle interiors. 16 m brush
            // ≈ 4.6 texels on the measured 4096-over-14336 m splat (3.5 m/texel); spacing
            // 0.5 × size double-covers the interior so the dither saturates.
            public const float PaintBrushSizeMeters = 16f;
            public const float PaintSpacingFactor = 0.5f;

            // Erase passes: rectangle brush when available (hard coverage, no fringe
            // residue), tighter spacing when only the soft round fallback exists.
            public const float EraseBrushSizeMeters = 256f;   // incremental bounds erase
            public const float WipeBrushSizeMeters = 2048f;   // load-time channel wipe
            public const float EraseSpacingFactorRect = 0.5f;
            public const float EraseSpacingFactorSoft = 0.35f;
            public const float EraseMarginMeters = PaintBrushSizeMeters; // bounds inflation

            // Debounce: coalesce area events; at most one full re-rasterize per window.
            public const int RasterizeDebounceFrames = 64;

            // Season: cheap check cadence + hysteresis so a snow line hovering exactly at
            // run mid-elevation cannot flap the texture set every check.
            public const int SeasonCheckFrames = 4096;
            public const float SeasonHysteresisMeters = 50f;
            public const float SeasonTempHysteresisC = 1f;

            // Safety valve: a pass this size is a bug (or a continent of runs) — warn, keep
            // painting, and let the log line be the evidence.
            public const int WarnDabsPerPass = 65536;
        }

        // Run surfaces — same tolerant name resolution as SkiResortAttractivenessSystem.
        // E1 (D16): SkiRunNaturalA is deliberately absent — a natural run paints no terrain.
        private static readonly string[] RunSurfaceNames = { "SkiRunGroomedA", "SkiRunSnowmakingA" };

        // Tool prefab name prefix; the claimed slot is appended ("SkiResortSplatToolExtra3").
        // Shared by RegisterToolPrefab (Game loads) and the editor-preload scrub.
        private const string ToolPrefabNamePrefix = "SkiResortSplatTool";

        // Texture file contract (a parallel agent generates these; missing ⇒ graceful
        // inert for the load, retried next load). Resolved under <mod dir>\Textures\.
        private const string TexturesFolderName = "Textures";
        private const string WinterDiffuseFile = "SkiTerrainSnowCorduroy_BaseColor.png";
        private const string WinterNormalFile = "SkiTerrainSnowCorduroy_Normal.png";
        private const string SummerDiffuseFile = "SkiTerrainSummerRun_BaseColor.png";
        private const string SummerNormalFile = "SkiTerrainSummerRun_Normal.png";

        /// <summary>Claim preference (terrain-material-runs.md): editor-configured maps fill
        /// slots from Extra1 upward, so the tail of the enum is the least contended.</summary>
        private static readonly TerrainMaterialType[] SlotPreference =
        {
            TerrainMaterialType.Extra3, TerrainMaterialType.Extra4,
            TerrainMaterialType.Extra2, TerrainMaterialType.Extra1,
        };

        private PrefabSystem m_PrefabSystem;
        private TerrainMaterialSystem m_TerrainMaterialSystem;
        private TerrainSystem m_TerrainSystem;
        private SnowLineSystem m_SnowLineSystem;   // validated climate oracle (pull-based)

        private EntityQuery m_AreaScanQuery;       // live run polygons (prefab-filtered)
        private EntityQuery m_AreaEventQuery;      // Created/Updated, non-Temp
        private EntityQuery m_AreaDeletedQuery;    // Deleted, non-Temp
        private EntityQuery m_PrefabScanQuery;     // name-resolution fallback
        private EntityQuery m_BrushPrefabQuery;    // BrushPrefab inventory

        private readonly HashSet<Entity> m_RunPrefabs = new HashSet<Entity>();

        // Session-cached (PrefabSystem and the asset database outlive individual loads).
        private bool m_PrefabsResolved;
        private TextureAsset m_WinterDiffuse, m_WinterNormal, m_SummerDiffuse, m_SummerNormal;
        private Entity m_PaintBrushPrefab = Entity.Null;
        private Entity m_EraseBrushPrefab = Entity.Null;
        private bool m_EraseBrushIsRect;
        private bool m_BrushInventoryLogged;

        // Per-load state.
        private bool m_Active;                     // slot claimed + textures + brushes OK
        private bool m_Disabled;                   // tripped by the catch; re-armed per load
        private bool m_ErrorLogged;                // one "!!!" with stack per load
        private TerrainMaterialType m_ClaimedSlot = TerrainMaterialType.None;
        private Entity m_SettingsEntity = Entity.Null;
        private TerrainRenderSettingsPrefab m_SettingsPrefab;
        private Entity m_ToolPrefabEntity = Entity.Null;
        private bool m_WinterActive;

        // Rasterize bookkeeping.
        private bool m_RasterizePending;
        private bool m_ChannelWiped;               // load-time full wipe done
        private bool m_HasLastBounds;
        private float2 m_LastBoundsMin, m_LastBoundsMax;
        private long m_FrameCounter;
        private long m_LastRasterizeFrame;
        private int m_FramesUntilSeasonCheck;
        private bool m_SplatmapLogged;             // the measurement deliverable, once per load

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_TerrainMaterialSystem = World.GetOrCreateSystemManaged<TerrainMaterialSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_SnowLineSystem = World.GetOrCreateSystemManaged<SnowLineSystem>();

            m_AreaScanQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Triangle>(),
                    ComponentType.ReadOnly<Node>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_AreaEventQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_AreaDeletedQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            m_BrushPrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadOnly<BrushData>());

            Mod.Log.Info($"{nameof(SkiRunSplatSystem)} created");
        }

        /// <summary>Editor-session scrub. The map editor's terrain panel
        /// (TerrainPanelSystem.OnGameLoaded) builds a material-paint button for every
        /// Material-target TerraformingPrefab — including ours, which persist in
        /// PrefabSystem across loads within a session. OnGamePreload runs before the
        /// panel's OnGameLoaded (GameManager fires onGamePreload before LoadGameSystem
        /// raises the game-loaded event), so removing the prefabs here keeps the button
        /// out of every editor session, including a same-session switch from a Game load
        /// that registered one. RemovePrefab tags the entity Deleted (the panel's query
        /// excludes Deleted) and clears the PrefabID index, so the next Game load
        /// re-registers cleanly through the existing RegisterToolPrefab path.</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);

            // ANTI-CONTAMINATION (every mode, before anything else): restore the settings
            // prefab's original slot values before the next load can observe them. The
            // managed TerrainRenderSettingsPrefab object persists across loads, and the
            // editor's map save path (PrefabSystem.SaveTerrainRenderSettings) CLONES the
            // live prefab into the author's map asset — an outstanding claim mutation at
            // that moment would bake our textures into someone's map. Reverting at every
            // preload means the mutation never outlives the load it served; Game loads
            // simply re-claim fresh a moment later.
            RevertClaimMutation();

            if (mode != GameMode.Editor)
                return;
            try
            {
                foreach (TerrainMaterialType slot in SlotPreference)
                {
                    string name = ToolPrefabNamePrefix + slot;
                    if (m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(TerraformingPrefab), name),
                            out PrefabBase prefabBase) && prefabBase != null &&
                        m_PrefabSystem.RemovePrefab(prefabBase))
                    {
                        Mod.Log.Info($"Splat: removed tool prefab '{name}' ahead of an editor " +
                                     "load — it must not appear as a material-paint button in " +
                                     "the editor's terrain panel");
                    }
                }
            }
            catch (Exception e)
            {
                // Defensive contract: never throw out of a load hook — the GameSystemBase
                // wrapper would disable this system for the whole session. A failed scrub
                // only means the (harmless, vanilla-supported) button may show this once.
                Mod.Log.Error($"!!! {nameof(SkiRunSplatSystem)} EXCEPTION during editor preload " +
                              $"scrub — the leaked terrain-panel button may be visible this " +
                              $"session. Please report this stack:\n{e}");
            }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Re-arm and reset per load, whatever the mode — leaving to the menu must
            // deactivate painting too.
            m_Active = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_ClaimedSlot = TerrainMaterialType.None;
            m_SettingsEntity = Entity.Null;
            m_SettingsPrefab = null;
            m_ToolPrefabEntity = Entity.Null;
            m_RasterizePending = false;
            m_ChannelWiped = false;
            m_HasLastBounds = false;
            m_SplatmapLogged = false;
            m_FrameCounter = 0;
            m_LastRasterizeFrame = -Tuning.RasterizeDebounceFrames;
            m_FramesUntilSeasonCheck = Tuning.SeasonCheckFrames;

            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;

            try
            {
                ResolveRunPrefabs();
                if (m_RunPrefabs.Count == 0)
                {
                    Mod.Log.Info("Splat: no run surface prefab present — painting inert until " +
                                 $"'{string.Join("', '", RunSurfaceNames)}' is imported (expected pre-import)");
                    return;
                }
                // Decals visible by default — every early-out below leaves runs rendering
                // as their classic surfaces; only a fully ACTIVE splat hides them (below).
                // This must run in the editor too: a Game load earlier this session may
                // have hidden the decals on the (session-persistent) run prefab objects.
                ApplyDecalVisibility(splatActive: false);
                if (mode != GameMode.Game)
                {
                    // Map editor: claiming, tool-prefab registration and painting are
                    // Game-mode only. The editor's terrain panel hands material brushes
                    // to map authors — our claim would collide with their strokes and the
                    // load-time channel wipe would erase them; the tool prefab itself was
                    // scrubbed in OnGamePreload so it cannot appear as a paint button.
                    Mod.Log.Info("Splat: map editor session — terrain paint inert by design " +
                                 "(Game mode only); runs render as their classic decal surfaces");
                    return;
                }
                if (!LoadTextures())
                    return; // reason logged; retried next load (texture agent may deliver)
                ResolveBrushes();
                if (m_PaintBrushPrefab == Entity.Null)
                {
                    Mod.Log.Warn("Splat: no usable BrushPrefab with a stamp texture found — " +
                                 "painting disabled for this load (see brush inventory above)");
                    return;
                }
                m_WinterActive = EvaluateWinter(out string seasonDetail);
                if (!ClaimSlot())
                    return; // reason logged
                if (!RegisterToolPrefab())
                    return; // reason logged

                m_Active = true;
                m_RasterizePending = true; // first pass: wipe + full stamp
                ApplyDecalVisibility(splatActive: true);
                Mod.Log.Info($"Splat: ACTIVE — slot {m_ClaimedSlot}, initial season " +
                             $"{(m_WinterActive ? "winter" : "summer")} ({seasonDetail}), " +
                             $"{m_RunPrefabs.Count} run prefab(s) watched, debounce " +
                             $"{Tuning.RasterizeDebounceFrames} frames, season check every " +
                             $"{Tuning.SeasonCheckFrames} frames");
                LogSplatmapOnce();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunSplatSystem)} EXCEPTION during load setup — " +
                                  $"splat painting disabled until the next game load. Please report " +
                                  $"this stack:\n{e}");
                }
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Active)
                    return;

                m_FrameCounter++;

                // Event watch: emptiness checks first (free), chunk scans only on event
                // frames, prefab-filtered so unrelated area churn stays cheap.
                if (!m_RasterizePending &&
                    ((!m_AreaEventQuery.IsEmptyIgnoreFilter && AnyMatchingPrefab(m_AreaEventQuery)) ||
                     (!m_AreaDeletedQuery.IsEmptyIgnoreFilter && AnyMatchingPrefab(m_AreaDeletedQuery))))
                {
                    m_RasterizePending = true;
                }

                if (m_RasterizePending &&
                    m_FrameCounter - m_LastRasterizeFrame >= Tuning.RasterizeDebounceFrames)
                {
                    m_RasterizePending = false;
                    m_LastRasterizeFrame = m_FrameCounter;
                    Rasterize();
                }

                if (--m_FramesUntilSeasonCheck <= 0)
                {
                    m_FramesUntilSeasonCheck = Tuning.SeasonCheckFrames;
                    UpdateSeason();
                }
            }
            catch (Exception e)
            {
                // Never throw into the game loop. Unmissable, with the full stack, once per
                // load; session caches are KEPT so the next load re-arms automatically.
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunSplatSystem)} EXCEPTION — splat painting " +
                                  $"disabled until the next game load. Please report this stack:\n{e}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Rasterization: full channel rebuild. Erase where the LAST pass painted (or wipe
        // the whole playable channel on the first pass of a load), then stamp every live
        // polygon. Main-thread ApplyBrush calls — each dab is one small compute dispatch;
        // counts are logged so cost is never invisible.
        // ─────────────────────────────────────────────────────────────────────────────────
        private void Rasterize()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int eraseDabs = 0;

            if (!m_ChannelWiped)
            {
                // Load invariant: the channel is derived state. Wipe it across the playable
                // area so stale paint (runs edited while the mod was absent, older bugs)
                // cannot survive a load.
                m_ChannelWiped = true;
                float2 playMin = m_TerrainSystem.playableOffset;
                float2 playMax = playMin + m_TerrainSystem.playableArea;
                eraseDabs += EraseRect(playMin, playMax, Tuning.WipeBrushSizeMeters);
                Mod.Log.Info($"Splat [wipe]: channel {m_ClaimedSlot} wiped across playable " +
                             $"({playMin.x:F0},{playMin.y:F0})-({playMax.x:F0},{playMax.y:F0}) " +
                             $"with {eraseDabs} dab(s)");
            }
            else if (m_HasLastBounds)
            {
                float2 min = m_LastBoundsMin - Tuning.EraseMarginMeters;
                float2 max = m_LastBoundsMax + Tuning.EraseMarginMeters;
                eraseDabs += EraseRect(min, max, Tuning.EraseBrushSizeMeters);
            }

            int runCount = 0, triangleCount = 0, paintDabs = 0;
            float2 boundsMin = new float2(float.MaxValue, float.MaxValue);
            float2 boundsMax = new float2(float.MinValue, float.MinValue);

            var chunks = m_AreaScanQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var triangleHandle = GetBufferTypeHandle<Triangle>(isReadOnly: true);
                var nodeHandle = GetBufferTypeHandle<Node>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var triangleAccessor = chunks[c].GetBufferAccessor(ref triangleHandle);
                    var nodeAccessor = chunks[c].GetBufferAccessor(ref nodeHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (!m_RunPrefabs.Contains(prefabRefs[i].m_Prefab))
                            continue;
                        runCount++;
                        DynamicBuffer<Triangle> triangles = triangleAccessor[i];
                        DynamicBuffer<Node> nodes = nodeAccessor[i];
                        for (int t = 0; t < triangles.Length; t++)
                        {
                            int3 indices = triangles[t].m_Indices;
                            if (math.cmax(indices) >= nodes.Length)
                                continue; // defensive: stale triangle vs node buffer
                            triangleCount++;
                            paintDabs += StampTriangle(
                                nodes[indices.x].m_Position.xz,
                                nodes[indices.y].m_Position.xz,
                                nodes[indices.z].m_Position.xz,
                                ref boundsMin, ref boundsMax);
                        }
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }

            m_HasLastBounds = paintDabs > 0;
            if (m_HasLastBounds)
            {
                m_LastBoundsMin = boundsMin - Tuning.PaintBrushSizeMeters * 0.5f;
                m_LastBoundsMax = boundsMax + Tuning.PaintBrushSizeMeters * 0.5f;
            }

            timer.Stop();
            string boundsText = m_HasLastBounds
                ? $"({m_LastBoundsMin.x:F0},{m_LastBoundsMin.y:F0})-({m_LastBoundsMax.x:F0},{m_LastBoundsMax.y:F0})"
                : "none";
            Mod.Log.Info($"Splat [rasterize]: {runCount} run(s), {triangleCount} triangle(s) -> " +
                         $"{paintDabs} paint dab(s), {eraseDabs} erase dab(s), bounds {boundsText}, " +
                         $"{timer.Elapsed.TotalMilliseconds:F1} ms");
            if (paintDabs + eraseDabs > Tuning.WarnDabsPerPass)
                Mod.Log.Warn($"Splat [rasterize]: dab count {paintDabs + eraseDabs} exceeded the " +
                             $"{Tuning.WarnDabsPerPass} sanity threshold — please report this line");
            LogSplatmapOnce(); // ApplyBrush lazily creates the splatmap; measure once it exists
        }

        /// <summary>Grid-stamps one draped triangle (positions are world xz; the splat is a
        /// top-down map, so xz is the whole story). Grid is world-aligned so repeated passes
        /// land identical dabs; a centroid dab guarantees slivers are never silently empty.</summary>
        private int StampTriangle(float2 a, float2 b, float2 c,
            ref float2 boundsMin, ref float2 boundsMax)
        {
            float step = Tuning.PaintBrushSizeMeters * Tuning.PaintSpacingFactor;
            float2 triMin = math.min(a, math.min(b, c));
            float2 triMax = math.max(a, math.max(b, c));
            int dabs = 0;

            float x0 = math.floor(triMin.x / step) * step;
            float z0 = math.floor(triMin.y / step) * step;
            for (float x = x0; x <= triMax.x; x += step)
            {
                for (float z = z0; z <= triMax.y; z += step)
                {
                    float2 p = new float2(x, z);
                    if (!PointInTriangle(p, a, b, c))
                        continue;
                    Dab(p, Tuning.PaintBrushSizeMeters, 1f, m_PaintBrushPrefab);
                    dabs++;
                }
            }
            if (dabs == 0)
            {
                Dab((a + b + c) / 3f, Tuning.PaintBrushSizeMeters, 1f, m_PaintBrushPrefab);
                dabs = 1;
            }

            boundsMin = math.min(boundsMin, triMin);
            boundsMax = math.max(boundsMax, triMax);
            return dabs;
        }

        /// <summary>Erases the claimed channel across a rectangle with a grid of negative-
        /// strength dabs (EraseDither kernel; the erase only touches OUR channel, which no
        /// one else paints — the free-slot claim is the exclusivity guarantee).</summary>
        private int EraseRect(float2 min, float2 max, float dabSize)
        {
            float factor = m_EraseBrushIsRect
                ? Tuning.EraseSpacingFactorRect
                : Tuning.EraseSpacingFactorSoft;
            float spacing = dabSize * factor;
            int dabs = 0;
            for (float x = min.x; x <= max.x + spacing; x += spacing)
            {
                for (float z = min.y; z <= max.y + spacing; z += spacing)
                {
                    Dab(new float2(x, z), dabSize, -1f, m_EraseBrushPrefab);
                    dabs++;
                }
            }
            return dabs;
        }

        private void Dab(float2 position, float size, float strength, Entity brushPrefab)
        {
            // ApplyToSplatmap consumes m_Position.xz/m_Size/m_Strength/m_Angle + m_Tool
            // (fullsrc line 830); y and the target/start fields are ignored by this path.
            Brush brush = default;
            brush.m_Tool = m_ToolPrefabEntity;
            brush.m_Position = new float3(position.x, 0f, position.y);
            brush.m_Size = size;
            brush.m_Strength = strength;
            brush.m_Opacity = 1f;
            m_TerrainMaterialSystem.ApplyBrush(brush, brushPrefab);
        }

        private static bool PointInTriangle(float2 p, float2 a, float2 b, float2 c)
        {
            // Sign test, edge-inclusive with a small tolerance so grid points on shared
            // triangle edges stamp exactly once per triangle pair at most.
            const float eps = 0.01f;
            float d1 = Cross(p - a, b - a);
            float d2 = Cross(p - b, c - b);
            float d3 = Cross(p - c, a - c);
            bool hasNeg = d1 < -eps || d2 < -eps || d3 < -eps;
            bool hasPos = d1 > eps || d2 > eps || d3 > eps;
            return !(hasNeg && hasPos);
        }

        private static float Cross(float2 u, float2 v)
        {
            return u.x * v.y - u.y * v.x;
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Seasonal swap: only the cloth changes, never the splat.
        // ─────────────────────────────────────────────────────────────────────────────────
        private void UpdateSeason()
        {
            bool wantWinter = EvaluateWinter(out string detail);
            if (wantWinter == m_WinterActive)
                return;
            m_WinterActive = wantWinter;
            ApplyClaimedSlotTextures();
            Mod.Log.Info($"Splat [season]: {(wantWinter ? "summer -> winter" : "winter -> summer")} " +
                         $"({detail}) — slot {m_ClaimedSlot} now " +
                         $"'{(wantWinter ? m_WinterDiffuse : m_SummerDiffuse)?.name}'");
        }

        /// <summary>Winter-ish := snow line at/below the mean run mid-elevation OR ambient
        /// at/below freezing. Hysteresis biases both thresholds toward the CURRENT state so
        /// a hovering boundary cannot flap the texture set every check.</summary>
        private bool EvaluateWinter(out string detail)
        {
            float snowLine = m_SnowLineSystem.GetSnowLineElevation();
            float ambient = m_SnowLineSystem.GetAmbientTemperature();
            float freezing = m_SnowLineSystem.GetFreezingTemperature();

            int runs = 0;
            float midSum = 0f;
            var chunks = m_AreaScanQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var geometryHandle = GetComponentTypeHandle<Geometry>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var geometries = chunks[c].GetNativeArray(ref geometryHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (!m_RunPrefabs.Contains(prefabRefs[i].m_Prefab))
                            continue;
                        runs++;
                        // Terrain-conformed bounds (the area pipeline drapes triangles onto
                        // terrain — same data the attractiveness score reads).
                        midSum += (geometries[i].m_Bounds.min.y + geometries[i].m_Bounds.max.y) * 0.5f;
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }

            float lineBias = m_WinterActive ? Tuning.SeasonHysteresisMeters : 0f;
            float tempBias = m_WinterActive ? Tuning.SeasonTempHysteresisC : 0f;
            float meanMid = runs > 0 ? midSum / runs : float.MinValue;
            bool byLine = runs > 0 && snowLine <= meanMid + lineBias;
            bool byTemp = ambient <= freezing + tempBias;
            detail = $"snowLine {snowLine:F0} m vs {runs} run(s) mid {(runs > 0 ? meanMid.ToString("F0") : "n/a")} m, " +
                     $"ambient {ambient:F1} C / freezing {freezing:F1} C";
            return byLine || byTemp;
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Slot claim + tool prefab + textures (the load-time setup chain).
        // ─────────────────────────────────────────────────────────────────────────────────
        private bool ClaimSlot()
        {
            // User policy (Options → Modular Ski Resort → Terrain). Off = decal look by
            // choice; ForceExtraN = deliberately sacrifice that map material (many CO maps
            // fill all four slots, so Auto legitimately finds nothing there).
            var policy = Mod.Settings != null
                ? Mod.Settings.TerrainSnowSlot
                : SkiResortMod.Settings.TerrainSlotPolicy.Auto;
            if (policy == SkiResortMod.Settings.TerrainSlotPolicy.Off)
            {
                Mod.Log.Info("Splat: terrain paint set to Off in mod options — runs use " +
                             "their classic decal surfaces");
                return false;
            }

            Entity settings = m_TerrainMaterialSystem.currentTerrainRenderSettings;
            if (settings == Entity.Null)
            {
                Mod.Log.Warn("Splat: currentTerrainRenderSettings is Null after load — " +
                             "painting disabled for this load");
                return false;
            }
            if (!m_PrefabSystem.TryGetPrefab(settings, out TerrainRenderSettingsPrefab rs) || rs == null)
            {
                Mod.Log.Warn("Splat: TerrainRenderSettingsPrefab unresolvable from the settings " +
                             "entity — painting disabled for this load");
                return false;
            }

            if (policy != SkiResortMod.Settings.TerrainSlotPolicy.Auto)
            {
                TerrainMaterialType forced = policy switch
                {
                    SkiResortMod.Settings.TerrainSlotPolicy.ForceExtra1 => TerrainMaterialType.Extra1,
                    SkiResortMod.Settings.TerrainSlotPolicy.ForceExtra2 => TerrainMaterialType.Extra2,
                    SkiResortMod.Settings.TerrainSlotPolicy.ForceExtra3 => TerrainMaterialType.Extra3,
                    _ => TerrainMaterialType.Extra4,
                };
                if (!IsSlotFree(rs, forced))
                    Mod.Log.Info($"Splat: FORCING {forced} per mod options — replacing this " +
                                 $"map's material {DescribeSlot(GetSlotDiffuse(rs, forced))} with " +
                                 "ski-run paint (areas the map painted with it will show run " +
                                 "material; revert by changing the option and reloading)");
                return ClaimSpecific(settings, rs, forced);
            }

            foreach (TerrainMaterialType slot in SlotPreference)
            {
                if (!IsSlotFree(rs, slot))
                    continue;
                return ClaimSpecific(settings, rs, slot);
            }

            Mod.Log.Warn("Splat: NO free Extra slot on this map — " +
                         $"Extra1={DescribeSlot(rs.m_Extra1Diffuse)}, Extra2={DescribeSlot(rs.m_Extra2Diffuse)}, " +
                         $"Extra3={DescribeSlot(rs.m_Extra3Diffuse)}, Extra4={DescribeSlot(rs.m_Extra4Diffuse)} — " +
                         "splat painting disabled for this load. To paint terrain anyway, pick a " +
                         "Force slot in Options → Modular Ski Resort → Terrain (runs stay visible " +
                         "via their decal surfaces meanwhile)");
            return false;
        }

        private bool ClaimSpecific(Entity settings, TerrainRenderSettingsPrefab rs, TerrainMaterialType slot)
        {
            {
                CaptureClaimMutation(rs, slot);
                m_SettingsEntity = settings;
                m_SettingsPrefab = rs;
                m_ClaimedSlot = slot;
                SetSlotParams(rs, slot);
                ApplyClaimedSlotTextures();

                // Verify the whole AssetReference-resolution chain end to end: after
                // ApplyRenderSettings, the slot's shader global must be OUR texture object.
                Texture expected = (m_WinterActive ? m_WinterDiffuse : m_SummerDiffuse)?.Load(-1);
                Texture actual = Shader.GetGlobalTexture(DiffuseShaderId(slot));
                bool verified = expected != null && ReferenceEquals(expected, actual);
                Mod.Log.Info($"Splat: claimed {slot} on settings prefab '{rs.name}' — " +
                             $"winter '{Path.GetFileNameWithoutExtension(WinterDiffuseFile)}', " +
                             $"summer '{Path.GetFileNameWithoutExtension(SummerDiffuseFile)}'; " +
                             $"slot params SplatRange=1 BlurDepth=0.1 HeightOffset=0 DirtOverride=0; " +
                             $"shader-verify {(verified ? "OK" : "FAILED")}");
                if (!verified)
                {
                    Mod.Log.Warn($"Splat: shader global for {slot} is " +
                                 $"'{(actual != null ? actual.name : "null")}' instead of our texture — " +
                                 "the asset-reference chain did not resolve; painting disabled for " +
                                 "this load (slot fields left claimed and harmless: gray fallback)");
                    return false;
                }
                return true;
            }
        }

        /// <summary>Runs' RenderedArea decals hide while splat paint is active and show
        /// whenever it is not — replacing the old static prefab-JSON edits that once left
        /// runs invisible on a no-free-slot map. Two layers, both idempotent: the managed
        /// prefab field (covers batches built later this session) and the live batch
        /// material if one already exists (AreaBatchSystem names it "Area batch (name)"
        /// and reads the mask once at batch creation — same live-material pattern as the
        /// seasonal texture swap).</summary>
        private void ApplyDecalVisibility(bool splatActive)
        {
            DecalLayers mask = splatActive ? (DecalLayers)0 : DecalLayers.Terrain;
            int shaderId = Shader.PropertyToID("colossal_DecalLayerMask");
            foreach (Entity prefabEntity in m_RunPrefabs)
            {
                if (!m_PrefabSystem.TryGetPrefab(prefabEntity, out PrefabBase prefab) || prefab == null)
                    continue;
                var rendered = prefab.GetComponent<RenderedArea>();
                if (rendered == null)
                    continue;
                rendered.m_DecalLayerMask = mask;

                string batchName = "Area batch (" + prefab.name + ")";
                foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (material != null && material.name == batchName)
                        material.SetFloat(shaderId, math.asfloat((int)mask));
                }
            }
            Mod.Log.Info($"Splat: run decal surfaces {(splatActive ? "hidden (terrain paint active)" : "visible (terrain paint inactive)")}");
        }

        // ── Claim-mutation record (anti-contamination — see OnGamePreload) ───────────────
        // Held OUTSIDE the per-load reset block: it must survive OnGameLoadingComplete's
        // field wipe so the NEXT preload can still revert it.
        private TerrainRenderSettingsPrefab m_MutatedSettingsPrefab;
        private TerrainMaterialType m_MutatedSlot = TerrainMaterialType.None;
        private AssetReference<TextureAsset> m_OrigDiffuse, m_OrigNormal;
        private float m_OrigDirtOverride, m_OrigSplatRange, m_OrigBlurDepth, m_OrigHeightOffset;

        private void CaptureClaimMutation(TerrainRenderSettingsPrefab rs, TerrainMaterialType slot)
        {
            // A prior un-reverted record can only mean same-load re-claim (seasonal
            // re-applies go through ApplyClaimedSlotTextures, not here) — keep the OLDEST
            // originals, they are the true pre-mutation state.
            if (m_MutatedSettingsPrefab != null)
                return;
            m_MutatedSettingsPrefab = rs;
            m_MutatedSlot = slot;
            m_OrigDiffuse = GetSlotDiffuse(rs, slot);
            switch (slot)
            {
                case TerrainMaterialType.Extra1:
                    m_OrigNormal = rs.m_Extra1Normal;
                    m_OrigDirtOverride = rs.m_Extra1DirtOverride; m_OrigSplatRange = rs.m_Extra1SplatRange;
                    m_OrigBlurDepth = rs.m_Extra1BlurDepth; m_OrigHeightOffset = rs.m_Extra1HeightOffset;
                    break;
                case TerrainMaterialType.Extra2:
                    m_OrigNormal = rs.m_Extra2Normal;
                    m_OrigDirtOverride = rs.m_Extra2DirtOverride; m_OrigSplatRange = rs.m_Extra2SplatRange;
                    m_OrigBlurDepth = rs.m_Extra2BlurDepth; m_OrigHeightOffset = rs.m_Extra2HeightOffset;
                    break;
                case TerrainMaterialType.Extra3:
                    m_OrigNormal = rs.m_Extra3Normal;
                    m_OrigDirtOverride = rs.m_Extra3DirtOverride; m_OrigSplatRange = rs.m_Extra3SplatRange;
                    m_OrigBlurDepth = rs.m_Extra3BlurDepth; m_OrigHeightOffset = rs.m_Extra3HeightOffset;
                    break;
                case TerrainMaterialType.Extra4:
                    m_OrigNormal = rs.m_Extra4Normal;
                    m_OrigDirtOverride = rs.m_Extra4DirtOverride; m_OrigSplatRange = rs.m_Extra4SplatRange;
                    m_OrigBlurDepth = rs.m_Extra4BlurDepth; m_OrigHeightOffset = rs.m_Extra4HeightOffset;
                    break;
            }
        }

        private void RevertClaimMutation()
        {
            if (m_MutatedSettingsPrefab == null)
                return;
            try
            {
                TerrainRenderSettingsPrefab rs = m_MutatedSettingsPrefab;
                switch (m_MutatedSlot)
                {
                    case TerrainMaterialType.Extra1:
                        rs.m_Extra1Diffuse = m_OrigDiffuse; rs.m_Extra1Normal = m_OrigNormal;
                        rs.m_Extra1DirtOverride = m_OrigDirtOverride; rs.m_Extra1SplatRange = m_OrigSplatRange;
                        rs.m_Extra1BlurDepth = m_OrigBlurDepth; rs.m_Extra1HeightOffset = m_OrigHeightOffset;
                        break;
                    case TerrainMaterialType.Extra2:
                        rs.m_Extra2Diffuse = m_OrigDiffuse; rs.m_Extra2Normal = m_OrigNormal;
                        rs.m_Extra2DirtOverride = m_OrigDirtOverride; rs.m_Extra2SplatRange = m_OrigSplatRange;
                        rs.m_Extra2BlurDepth = m_OrigBlurDepth; rs.m_Extra2HeightOffset = m_OrigHeightOffset;
                        break;
                    case TerrainMaterialType.Extra3:
                        rs.m_Extra3Diffuse = m_OrigDiffuse; rs.m_Extra3Normal = m_OrigNormal;
                        rs.m_Extra3DirtOverride = m_OrigDirtOverride; rs.m_Extra3SplatRange = m_OrigSplatRange;
                        rs.m_Extra3BlurDepth = m_OrigBlurDepth; rs.m_Extra3HeightOffset = m_OrigHeightOffset;
                        break;
                    case TerrainMaterialType.Extra4:
                        rs.m_Extra4Diffuse = m_OrigDiffuse; rs.m_Extra4Normal = m_OrigNormal;
                        rs.m_Extra4DirtOverride = m_OrigDirtOverride; rs.m_Extra4SplatRange = m_OrigSplatRange;
                        rs.m_Extra4BlurDepth = m_OrigBlurDepth; rs.m_Extra4HeightOffset = m_OrigHeightOffset;
                        break;
                }
                Mod.Log.Info($"Splat: reverted claim mutation on settings prefab '{rs.name}' " +
                             $"slot {m_MutatedSlot} — original references restored ahead of the " +
                             "next load (map saves can never capture our textures)");
            }
            catch (Exception e)
            {
                Mod.Log.Error($"!!! {nameof(SkiRunSplatSystem)} EXCEPTION reverting the claim " +
                              $"mutation — do NOT save maps in the editor this session. Stack:\n{e}");
            }
            finally
            {
                m_MutatedSettingsPrefab = null;
                m_MutatedSlot = TerrainMaterialType.None;
                m_OrigDiffuse = null;
                m_OrigNormal = null;
            }
        }

        /// <summary>Free := null reference, invalid guid, or OUR OWN asset left from an
        /// earlier load this session (prefab objects persist in PrefabSystem, so a reclaim
        /// of the same map must not read its own footprint as an occupant).</summary>
        private bool IsSlotFree(TerrainRenderSettingsPrefab rs, TerrainMaterialType slot)
        {
            AssetReference<TextureAsset> reference = GetSlotDiffuse(rs, slot);
            if (reference == null)
                return true;
            Colossal.Hash128 guid = reference;
            if (!guid.isValid)
                return true;
            return IsOurAsset(guid);
        }

        private bool IsOurAsset(Colossal.Hash128 guid)
        {
            return (m_WinterDiffuse != null && m_WinterDiffuse.id.guid == guid) ||
                   (m_SummerDiffuse != null && m_SummerDiffuse.id.guid == guid);
        }

        private string DescribeSlot(AssetReference<TextureAsset> reference)
        {
            if (reference == null)
                return "free";
            Colossal.Hash128 guid = reference;
            if (!guid.isValid)
                return "free";
            TextureAsset asset = reference;
            return asset != null ? $"'{asset.name}'" : $"unresolvable {guid}";
        }

        private AssetReference<TextureAsset> GetSlotDiffuse(TerrainRenderSettingsPrefab rs, TerrainMaterialType slot)
        {
            switch (slot)
            {
                case TerrainMaterialType.Extra1: return rs.m_Extra1Diffuse;
                case TerrainMaterialType.Extra2: return rs.m_Extra2Diffuse;
                case TerrainMaterialType.Extra3: return rs.m_Extra3Diffuse;
                case TerrainMaterialType.Extra4: return rs.m_Extra4Diffuse;
                default: return null;
            }
        }

        /// <summary>Writes the current season's diffuse/normal into the claimed slot's
        /// fields and re-assigns currentTerrainRenderSettings to itself — the property
        /// setter runs ApplyRenderSettings, which resolves our TextureAssets and pushes
        /// SetGlobalTexture (the CursorAim managed-prefab-patch pattern, one level up).</summary>
        private void ApplyClaimedSlotTextures()
        {
            TextureAsset diffuse = m_WinterActive ? m_WinterDiffuse : m_SummerDiffuse;
            TextureAsset normal = m_WinterActive ? m_WinterNormal : m_SummerNormal;
            TerrainRenderSettingsPrefab rs = m_SettingsPrefab;
            switch (m_ClaimedSlot)
            {
                case TerrainMaterialType.Extra1:
                    rs.m_Extra1Diffuse = diffuse;
                    rs.m_Extra1Normal = normal;
                    break;
                case TerrainMaterialType.Extra2:
                    rs.m_Extra2Diffuse = diffuse;
                    rs.m_Extra2Normal = normal;
                    break;
                case TerrainMaterialType.Extra3:
                    rs.m_Extra3Diffuse = diffuse;
                    rs.m_Extra3Normal = normal;
                    break;
                case TerrainMaterialType.Extra4:
                    rs.m_Extra4Diffuse = diffuse;
                    rs.m_Extra4Normal = normal;
                    break;
            }
            m_TerrainMaterialSystem.currentTerrainRenderSettings = m_SettingsEntity;
        }

        /// <summary>Deterministic per-slot render params for the claimed (previously empty)
        /// slot — never trust whatever an empty slot happened to carry.</summary>
        private static void SetSlotParams(TerrainRenderSettingsPrefab rs, TerrainMaterialType slot)
        {
            switch (slot)
            {
                case TerrainMaterialType.Extra1:
                    rs.m_Extra1DirtOverride = 0f; rs.m_Extra1SplatRange = 1f;
                    rs.m_Extra1BlurDepth = 0.1f; rs.m_Extra1HeightOffset = 0f;
                    break;
                case TerrainMaterialType.Extra2:
                    rs.m_Extra2DirtOverride = 0f; rs.m_Extra2SplatRange = 1f;
                    rs.m_Extra2BlurDepth = 0.1f; rs.m_Extra2HeightOffset = 0f;
                    break;
                case TerrainMaterialType.Extra3:
                    rs.m_Extra3DirtOverride = 0f; rs.m_Extra3SplatRange = 1f;
                    rs.m_Extra3BlurDepth = 0.1f; rs.m_Extra3HeightOffset = 0f;
                    break;
                case TerrainMaterialType.Extra4:
                    rs.m_Extra4DirtOverride = 0f; rs.m_Extra4SplatRange = 1f;
                    rs.m_Extra4BlurDepth = 0.1f; rs.m_Extra4HeightOffset = 0f;
                    break;
            }
        }

        private static int DiffuseShaderId(TerrainMaterialType slot)
        {
            switch (slot)
            {
                case TerrainMaterialType.Extra1: return TerrainMaterialSystem.ShaderID._COTerrainExtra1Diffuse;
                case TerrainMaterialType.Extra2: return TerrainMaterialSystem.ShaderID._COTerrainExtra2Diffuse;
                case TerrainMaterialType.Extra4: return TerrainMaterialSystem.ShaderID._COTerrainExtra4Diffuse;
                default: return TerrainMaterialSystem.ShaderID._COTerrainExtra3Diffuse;
            }
        }

        /// <summary>Registers (or reuses — the name encodes the slot) the TerraformingPrefab
        /// whose TerrainMaterialProperties.m_Type routes our ApplyBrush dabs into the
        /// claimed channel (GetMaterialType, fullsrc line 805). Public AddPrefab creates the
        /// prefab entity immediately. Lacking a UIObject does NOT hide the prefab from UI:
        /// the map editor's TerrainPanelSystem.OnGameLoaded lists every Material-target
        /// TerraformingPrefab as a paint button (default material icon fallback) — which is
        /// why this registration only ever runs in GameMode.Game (there is no material-paint
        /// UI in game) and OnGamePreload scrubs the prefab before any editor load.</summary>
        private bool RegisterToolPrefab()
        {
            string name = ToolPrefabNamePrefix + m_ClaimedSlot;
            if (!m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(TerraformingPrefab), name),
                    out PrefabBase prefabBase) || prefabBase == null)
            {
                var tool = ScriptableObject.CreateInstance<TerraformingPrefab>();
                tool.name = name;
                tool.m_Target = TerraformingTarget.Material;
                tool.m_Type = TerraformingType.Shift; // irrelevant to the Material path
                tool.AddComponent<TerrainMaterialProperties>().m_Type = m_ClaimedSlot;
                if (!m_PrefabSystem.AddPrefab(tool))
                {
                    Mod.Log.Warn($"Splat: PrefabSystem.AddPrefab rejected '{name}' — painting " +
                                 "disabled for this load");
                    return false;
                }
                prefabBase = tool;
                Mod.Log.Info($"Splat: registered tool prefab '{name}' " +
                             $"(TerrainMaterialProperties.m_Type={m_ClaimedSlot})");
            }
            else
            {
                Mod.Log.Info($"Splat: reusing tool prefab '{name}' from an earlier load this session");
            }

            if (!m_PrefabSystem.TryGetEntity(prefabBase, out m_ToolPrefabEntity) ||
                m_ToolPrefabEntity == Entity.Null)
            {
                Mod.Log.Warn($"Splat: no entity for tool prefab '{name}' — painting disabled " +
                             "for this load");
                return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Textures: PNG files → readable Texture2Ds → TextureAssets in AssetDatabase.user
        // (in-memory index entry only; never saved to disk). Session-cached.
        // ─────────────────────────────────────────────────────────────────────────────────
        private bool LoadTextures()
        {
            if (m_WinterDiffuse != null && m_SummerDiffuse != null)
                return true; // session cache

            string root = Mod.ModRootPath;
            if (string.IsNullOrEmpty(root))
            {
                Mod.Log.Warn("Splat: mod root path unknown (TryGetExecutableAsset failed at " +
                             "OnLoad) — painting disabled for this load");
                return false;
            }
            string dir = Path.Combine(root, TexturesFolderName);
            string[] files = { WinterDiffuseFile, WinterNormalFile, SummerDiffuseFile, SummerNormalFile };
            var missing = new List<string>();
            foreach (string file in files)
            {
                if (!File.Exists(Path.Combine(dir, file)))
                    missing.Add(file);
            }
            if (missing.Count > 0)
            {
                Mod.Log.Warn($"Splat: {missing.Count} of 4 texture file(s) missing under '{dir}': " +
                             $"{string.Join(", ", missing)} — painting disabled for this load " +
                             "(retried next load)");
                return false;
            }

            // BaseColor is sRGB; normal/mask maps are linear data.
            m_WinterDiffuse = RegisterTexture(dir, WinterDiffuseFile, linear: false);
            m_WinterNormal = RegisterTexture(dir, WinterNormalFile, linear: true);
            m_SummerDiffuse = RegisterTexture(dir, SummerDiffuseFile, linear: false);
            m_SummerNormal = RegisterTexture(dir, SummerNormalFile, linear: true);
            Mod.Log.Info($"Splat: 4 texture(s) loaded from '{dir}' and registered as user-db " +
                         $"TextureAssets ({m_WinterDiffuse.width}x{m_WinterDiffuse.height} winter diffuse)");
            return true;
        }

        private TextureAsset RegisterTexture(string dir, string fileName, bool linear)
        {
            string assetName = Path.GetFileNameWithoutExtension(fileName);
            ILocalAssetDatabase db = AssetDatabase.user;
            AssetDataPath assetPath = AssetDataPath.Create(nameof(SkiResortMod), assetName);
            if (db.Exists(assetPath, out TextureAsset existing) && existing != null)
            {
                // A previous world this session registered it; the asset still serves its
                // in-memory texture. (SetData on a referenced asset throws, so no refresh —
                // changed PNGs need a game restart, worth logging.)
                Mod.Log.Info($"Splat: reusing already-registered texture asset '{assetName}'");
                return existing;
            }

            byte[] bytes = File.ReadAllBytes(Path.Combine(dir, fileName));
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear: linear)
            {
                name = assetName,
            };
            if (!ImageConversion.LoadImage(texture, bytes))
                throw new InvalidDataException($"LoadImage failed for '{fileName}'");
            // Terrain materials tile; LoadImage defaults to Clamp, which would smear the
            // last texel row across the mountain.
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            return db.AddAsset<TextureAsset, Texture>(assetPath, texture);
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Brush prefabs: stamp textures for ApplyBrush. Soft round for paint ("Default
        // Brush" — the terraforming tools' standard falloff blob), hard rectangle for erase
        // coverage ("RectangleBrush"). Tolerant scan with a one-time inventory log.
        // ─────────────────────────────────────────────────────────────────────────────────
        private void ResolveBrushes()
        {
            if (m_PaintBrushPrefab != Entity.Null)
                return; // session cache

            var candidates = new List<(Entity entity, string name)>();
            var entities = m_BrushPrefabQuery.ToEntityArray(Allocator.Temp);
            try
            {
                var inventory = new List<string>();
                for (int i = 0; i < entities.Length; i++)
                {
                    string name = m_PrefabSystem.GetPrefabName(entities[i]);
                    bool hasTexture = m_PrefabSystem.TryGetPrefab(entities[i], out BrushPrefab bp) &&
                                      bp != null && bp.m_Texture != null;
                    inventory.Add($"'{name}'({(hasTexture ? "tex" : "NO tex")})");
                    if (hasTexture)
                        candidates.Add((entities[i], name));
                }
                if (!m_BrushInventoryLogged)
                {
                    m_BrushInventoryLogged = true;
                    Mod.Log.Info($"Splat: brush inventory — {entities.Length} BrushPrefab(s): " +
                                 $"{string.Join(", ", inventory)} (logged once per session)");
                }
            }
            finally
            {
                entities.Dispose();
            }

            m_PaintBrushPrefab = PickBrush(candidates, "Default Brush", "circle", "default");
            Entity rect = PickBrush(candidates, "RectangleBrush", "rectangle", "square");
            m_EraseBrushIsRect = rect != Entity.Null;
            m_EraseBrushPrefab = m_EraseBrushIsRect ? rect : m_PaintBrushPrefab;

            if (m_PaintBrushPrefab != Entity.Null)
                Mod.Log.Info($"Splat: paint brush '{m_PrefabSystem.GetPrefabName(m_PaintBrushPrefab)}', " +
                             $"erase brush '{(m_EraseBrushPrefab != Entity.Null ? m_PrefabSystem.GetPrefabName(m_EraseBrushPrefab) : "none")}'" +
                             $"{(m_EraseBrushIsRect ? "" : " (soft fallback — erase spacing tightened)")}");
        }

        private static Entity PickBrush(List<(Entity entity, string name)> candidates,
            string exactName, string substring1, string substring2)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.name == exactName)
                    return candidate.entity;
            }
            foreach (var candidate in candidates)
            {
                string lower = candidate.name.ToLowerInvariant();
                if (lower.Contains(substring1) || lower.Contains(substring2))
                    return candidate.entity;
            }
            // Exact and fuzzy both missed: any textured brush beats none, but only for the
            // paint pick (the erase pick falls back to the paint brush at the call site).
            return exactName == "Default Brush" && candidates.Count > 0
                ? candidates[0].entity
                : Entity.Null;
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Shared helpers.
        // ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>The measurement deliverable (terrain-material-runs.md open question #1):
        /// user splatmap dimensions/format vs playable size, logged once per load as soon as
        /// the splatmap exists (ApplyBrush creates it lazily on fresh maps).</summary>
        private void LogSplatmapOnce()
        {
            if (m_SplatmapLogged)
                return;
            Texture splat = m_TerrainMaterialSystem.UserSplatmap;
            if (splat == null)
                return; // not created yet — retried after the first rasterize
            m_SplatmapLogged = true;
            Texture world = m_TerrainMaterialSystem.UserSplatmapWorld;
            float2 playable = m_TerrainSystem.playableArea;
            float metersPerTexel = math.max(playable.x, playable.y) / math.max(splat.width, 1);
            string format = splat is RenderTexture rt ? rt.graphicsFormat.ToString() : "?";
            Mod.Log.Info($"Splat [measure]: user splatmap {splat.width}x{splat.height} ({format}), " +
                         $"world {(world != null ? $"{world.width}x{world.height}" : "not created")}, " +
                         $"playable {playable.x:F0}x{playable.y:F0} m -> {metersPerTexel:F2} m/texel " +
                         $"({Tuning.PaintBrushSizeMeters:F0} m brush = " +
                         $"{Tuning.PaintBrushSizeMeters / math.max(metersPerTexel, 0.001f):F1} texels)");
        }

        /// <summary>True when any entity in the (event) query's chunks is one of our runs.
        /// Runs only on event frames; chunk-level iteration.</summary>
        private bool AnyMatchingPrefab(EntityQuery query)
        {
            var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (m_RunPrefabs.Contains(prefabRefs[i].m_Prefab))
                            return true;
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            return false;
        }

        // House pattern: fast PrefabID path + name-scan fallback, tolerate missing (the run
        // surfaces are NOT YET IMPORTED on some machines; the system logs its inertness once
        // per load and sleeps until they exist).
        private void ResolveRunPrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;

            foreach (string name in RunSurfaceNames)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID("SurfacePrefab", name), out PrefabBase prefabBase))
                    m_PrefabSystem.TryGetEntity(prefabBase, out prefabEntity);

                if (prefabEntity == Entity.Null)
                {
                    var candidates = m_PrefabScanQuery.ToEntityArray(Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            if (m_PrefabSystem.GetPrefabName(candidates[i]) == name)
                            {
                                prefabEntity = candidates[i];
                                break;
                            }
                        }
                    }
                    finally
                    {
                        candidates.Dispose();
                    }
                }

                if (prefabEntity == Entity.Null)
                {
                    Mod.Log.Info($"Splat: run surface prefab '{name}' not found (tolerated)");
                    continue;
                }
                m_RunPrefabs.Add(prefabEntity);
                Mod.Log.Info($"Splat: resolved run surface prefab '{name}'");
            }
        }
    }
}
