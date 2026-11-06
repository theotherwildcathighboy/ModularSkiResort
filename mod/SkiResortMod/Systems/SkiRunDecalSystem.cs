// SkiRunDecalSystem - upgrades the live material behind our ski-run decal surfaces at runtime: no Editor re-import, no custom
// shader, no shared asset touched. AreaBatchSystem clones the shared AreaDecalShader material once per surface prefab and names the
// clone "Area batch (<prefab name>)" (AreaBatchSystem.cs 2094-2097); what RenderedArea does not expose is reachable only by mutating
// that clone, which cannot leak: we never touch the shared m_RenderedAreaMaterial or another prefab's clone. We never assign
// m_Material (AreaBatchSystem would flip RenderedArea.m_Version to OverrideMaterial in place). Scheduling: PreCulling, after
// AreaBatchSystem (clones build lazily on prefab Created), every frame in Game and Editor, paused included, so the retry countdown and
// the re-assert sentinel tick. Clones are found by name, retried until found.
// Engine notes: EngineNotes/SkiRunDecalSystem.md

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunDecalSystem : GameSystemBase
    {
        private static class Tuning
        {
            // Batch clones build lazily (prefab Created -> AreaBatchSystem.UpdatePrefabs); scan every RetryIntervalFrames until found,
            // give up loudly after the deadline
            public const int BatchFindDeadlineFrames = 300;
            public const int FindRetryIntervalFrames = 15;

            // Re-assert sentinel cadence: verify our live values still stand (texture-quality changes and EnabledShadersUpdated re-Load
            // prefab assets over them; batch rebuilds Destroy the clone). Two materials, a handful of Get* calls: cheap.
            public const int ReassertCheckFrames = 256;
            // Sentinel lines per load; a mod that keeps rewriting these values would otherwise log on every check
            public const int ReassertLinesMax = 6;

            // Doc-safe scalars (area-material-override.md section 3 Tier 0). Each is mirrored to the managed RenderedArea field of the
            // same name (RenderedArea.cs 58-76) so later-built batches agree with the live mutation.
            public const float NormalOpacity = 1f;
            public const float NormalAlphaSource = 1f;    // 1 = normal masked by its own alpha, not BaseColor coverage: relief survives albedo fade
            public const float Smoothness = 0.4f;         // 0.3-0.45: sparkle without gloss

            // Normal-only summer mode: terrain color untouched, corduroy relief only. Requires both the keyword and the float (HDRP
            // gates on the keyword; the float is the inspector mirror). readonly, not const, so flipping it is a one-token change
            // without unreachable-code warnings.
            public static readonly bool ApplyNormalOnlyMode = false;

            // Binds the corduroy pair into the shader's snow slots for the groomed surface only (Target.UseSnowPair). An earlier
            // enable bound textures into what were believed to be empty snow slots and the layer locked on, solid white and immune to
            // 50 C and remove-snow. Vanilla itself ships these slots bound (snowalbedo2 defaults) with _Snow = 0 and no artifact, so
            // texture presence is not the driver; swapping the already-bound textures while touching neither the _Snow float nor any
            // keyword is the vanilla-parity operation. Corduroy renders in the snow layer itself, the run's deep-winter identity, which
            // depth cannot provide (white saturates at cm scale).
            // If groomed runs go solid white and ignore 50 C / remove-snow again, this flag is the culprit.
            public static readonly bool ApplySnowSlots = true;

            // Summer wears the green mowed-grass set, winter the wear/corduroy set. The dress is driven by snow presence on the runs
            // (injection system's measured mean depth): temperature flipped the trails green while the world was still white.
            // Temperature hysteresis remains only as the fallback when no measurement exists yet.
            public const int SeasonCheckFrames = 1024;
            public const float WinterLookOnC = 2f;    // fallback only
            public const float WinterLookOffC = 3f;   // fallback only
            public const float DressSnowyDepthM = 0.02f;  // runs this snowy -> winter dress
            public const float DressBareDepthM = 0.005f;  // runs this bare -> summer dress

            // The summer green ships as an opaque terrain texture; as a decal it must be a faint worn lane, not paint. 0.40 still read
            // as paint with a vector edge; the tree-cleared corridor carries the run's summer identity on its own, the decal only
            // whispers. Alpha and RGB are scaled in-place at load.
            public const float SummerAlphaScale = 0.20f;
            public const float SummerRgbScale = 0.88f;

            // Winter dress per run type: snowmaking's decal edge fought its own injected snow ("vector crisp"), so its winter body is
            // Layer 1's snow alone (soft feathered edges through the real terrain pipeline) and the decal fades to a whisper; groomed
            // keeps a visible corduroy lane, and a machine-cut edge is what grooming actually looks like. RGB untouched; alpha scaled
            // in-place at load, same mechanism as the summer set.
            // The groomed decal's mean RGB is 153/149/143 (beige) against snowmaking's 186/190/198, so at 0.55 the beige showed through
            // the snow and the run read gray; 0.35 keeps the corduroy lane readable while the pack's white wins. The prefab tint
            // (SkiRunGroomedA m_BaseColor) was raised too; if still gray, the bake (ski_decal.py build_groomed `beige`) is the next knob.
            public const float GroomedWinterAlphaScale = 0.35f;
        }

        // Run surfaces, with the same tolerant name resolution as the sibling systems. SkiRunNaturalA is left out on purpose: a
        // natural run draws nothing (its RenderedArea is invisible), so there is no decal to dress; its measured snow may still
        // drive the dress (SkiSnowInjectionSystem.TryGetDressDepth).
        private const string GroomedPrefabName = "SkiRunGroomedA";
        private const string SnowmakingPrefabName = "SkiRunSnowmakingA";
        private const string NightPrefabName = "SkiRunNightA";
        // The cat track wears the groomed pair (textures, winter alpha, snow slots) until it gets its own; its prefab has
        // RendererPriority 11 so its decal draws over a run it crosses.
        private const string CatTrackPrefabName = "SkiRunCatTrackA";

        // Texture file contract: missing files are logged once per load and their applications skipped (retried next load).
        // Resolved under <mod dir>\Textures\.
        private const string TexturesFolderName = "Textures";
        private const string GroomedBaseColorFile = "SkiRunDecalGroomed_BaseColor.png";
        private const string GroomedNormalFile = "SkiRunDecalGroomed_Normal.png";
        // Snow slots: the corduroy winter pair (assets/blender/ski_terrain.py)
        private const string SnowBaseColorFile = "SkiTerrainSnowCorduroy_BaseColor.png";
        private const string SnowNormalFile = "SkiTerrainSnowCorduroy_Normal.png";
        // Shared summer dress (both run types): green mowed-grass stripes (assets/blender/ski_terrain.py)
        private const string SummerBaseColorFile = "SkiTerrainSummerRun_BaseColor.png";
        private const string SummerNormalFile = "SkiTerrainSummerRun_Normal.png";

        // Shader property names (area-material-override.md section 1.1). Every use below is gated on the live shader's actual
        // property table, so a game patch renaming one degrades to a logged skip, never an exception or a silent misapply.
        private const string BatchMaterialPrefix = "Area batch (";
        private const string BaseColorMapProperty = "_BaseColorMap";
        private const string NormalMapProperty = "_NormalMap";
        private const string SnowBaseColorMapProperty = "_Snow_BaseColorMap";
        // _Snow_Normal (no "Map" suffix) per the serialized shader's property table; the alternate spelling is tried defensively
        private static readonly string[] SnowNormalPropertyCandidates =
            { "_Snow_Normal", "_Snow_NormalMap" };
        private const string NormalOpacityProperty = "_NormalOpacity";
        private const string NormalAlphaSourceProperty = "_NormalAlphaSource";
        private const string SmoothnessProperty = "_Smoothness";
        // Normal-only mode (wired behind Tuning.ApplyNormalOnlyMode)
        private const string AffectAlbedoProperty = "_AffectAlbedo";
        private const string AffectsAlbedoKeyword = "_MATERIAL_AFFECTS_ALBEDO";

        /// <summary>One run surface prefab + its live batch clone + what we applied to it</summary>
        private sealed class Target
        {
            public readonly string PrefabName;
            public readonly string BaseColorFileName;
            public readonly string NormalFileName;
            // Winter decal alpha (scaled once at load) and whether this surface binds the corduroy pair into the shader's snow slots
            public readonly float WinterAlphaScale;
            public readonly bool UseSnowPair;

            // Session-cached (prefabs and loaded textures outlive individual loads)
            public bool Resolved;
            public string BatchMaterialName;
            public RenderedArea Rendered;
            public Texture2D BaseColorTex;
            public Texture2D NormalTex;
            // The same textures wrapped as user-db TextureAssets, written into the prefab's texture references so the game's own batch
            // re-derivation propagates our textures instead of re-binding the originals (the game re-bound the Editor whites twice in
            // 2.5 min; the live-material swap perpetually lost the race).
            public TextureAsset BaseColorAsset;
            public TextureAsset NormalAsset;
            public AssetReference<TextureAsset> OrigBaseColorRef;
            public AssetReference<TextureAsset> OrigNormalRef;
            public bool OrigRefsCaptured;

            // Per-load
            public Material Material;          // the live clone (Destroyed on batch rebuild)
            public bool Applied;
            public bool GivenUp;
            public bool RebuildWarned;
            public bool BasePairSet;
            public bool SnowPairSet;
            public readonly List<(int id, string name, Texture tex)> AppliedTextures =
                new List<(int, string, Texture)>();
            public readonly List<(int id, string name, float value)> AppliedFloats =
                new List<(int, string, float)>();

            public Target(string prefabName, string baseColorFileName, string normalFileName,
                float winterAlphaScale, bool useSnowPair)
            {
                PrefabName = prefabName;
                BaseColorFileName = baseColorFileName;
                NormalFileName = normalFileName;
                WinterAlphaScale = winterAlphaScale;
                UseSnowPair = useSnowPair;
            }

            public void ResetPerLoad()
            {
                Material = null;
                Applied = false;
                GivenUp = false;
                RebuildWarned = false;
                BasePairSet = false;
                SnowPairSet = false;
                AppliedTextures.Clear();
                AppliedFloats.Clear();
            }
        }

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_PrefabScanQuery;
        private Target[] m_Targets;

        // Session-cached
        private bool m_PrefabsResolved;
        private Texture2D m_SnowBaseColorTex;
        private Texture2D m_SnowNormalTex;
        private Texture2D m_SummerBaseColorTex;
        private Texture2D m_SummerNormalTex;
        private TextureAsset m_SummerBaseColorAsset;
        private TextureAsset m_SummerNormalAsset;
        private SnowLineSystem m_SnowLineSystem;
        private SkiSnowInjectionSystem m_SnowInjectionSystem;

        // Seasonal look state (per-load evaluated, hysteresis in EvaluateWinterLook)
        private bool m_WinterLook;
        private int m_FramesUntilSeasonCheck;
        // What the last EvaluateWinterLook read, for the two `Decal [season]` lines only (no string is built in the check itself)
        private RunDressSource m_DressSource;
        private float m_DressValue;

        // Per-load state
        private bool m_GameMode;
        private bool m_Active;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_SummaryLogged;
        private bool m_PropertyWarnLogged;
        private int m_PendingCount;
        private int m_AppliedCount;
        private int m_FramesUntilGiveUp;
        private int m_FramesUntilReassert;
        private readonly HashSet<int> m_ProbedInstances = new HashSet<int>();
        private readonly List<string> m_LastScanBatchNames = new List<string>();
        // Property writes since the last count line (reset at load, before and after a season re-dress, and after the activation
        // summary). A re-assert's writes are not reported by count (its own [reassert] line and the changed writes say what
        // happened); the next season re-dress starts from zero.
        private int m_ChangedWrites;
        private int m_SameWrites;
        private int m_ReassertLines;
        // True while a re-apply past the line cap runs: ApplyToMaterial counts its writes but logs nothing
        private bool m_ReassertQuiet;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SnowLineSystem = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_SnowInjectionSystem = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            m_Targets = new[]
            {
                new Target(GroomedPrefabName, GroomedBaseColorFile, GroomedNormalFile,
                    Tuning.GroomedWinterAlphaScale, useSnowPair: true),
                // Two looks (DECISIONS 577): the snowmaking and night presets are groomed runs with guns or lights on, so they wear the
                // groomed dress; guns and masts are props, not a surface.
                new Target(SnowmakingPrefabName, GroomedBaseColorFile, GroomedNormalFile,
                    Tuning.GroomedWinterAlphaScale, useSnowPair: true),
                new Target(NightPrefabName, GroomedBaseColorFile, GroomedNormalFile,
                    Tuning.GroomedWinterAlphaScale, useSnowPair: true),
                // The cat track reuses the groomed pair
                new Target(CatTrackPrefabName, GroomedBaseColorFile, GroomedNormalFile,
                    Tuning.GroomedWinterAlphaScale, useSnowPair: true),
            };
            Mod.Log.Info($"{nameof(SkiRunDecalSystem)} created");
        }

        /// <summary>Editor safety: the prefab texture-reference patch must never be captured by an editor save of the surface
        /// asset, so the original references are restored before any editor session (anti-contamination: no mod state is ever
        /// saved into an asset).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            if (mode != GameMode.Editor || m_Targets == null)
                return;
            foreach (Target target in m_Targets)
            {
                if (!target.OrigRefsCaptured || target.Rendered == null)
                    continue;
                target.Rendered.m_BaseColorMap = target.OrigBaseColorRef;
                target.Rendered.m_NormalMap = target.OrigNormalRef;
                Mod.Log.Info($"Decal: restored '{target.PrefabName}' original texture " +
                             "references ahead of an editor load (editor saves must never " +
                             "capture runtime user-db assets)");
            }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_GameMode = mode == GameMode.Game;

            // Re-arm and reset per load, whatever the mode: the batch clones this system mutated last load are gone (prefab
            // Deleted/Created rebuilds them per load)
            m_Active = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_SummaryLogged = false;
            m_PropertyWarnLogged = false;
            m_PendingCount = 0;
            m_AppliedCount = 0;
            m_ChangedWrites = 0;
            m_SameWrites = 0;
            m_ReassertLines = 0;
            m_ReassertQuiet = false;
            m_ProbedInstances.Clear();
            m_LastScanBatchNames.Clear();
            foreach (Target target in m_Targets)
                target.ResetPerLoad();

            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;

            try
            {
                ResolveRunPrefabs();
                int resolved = 0;
                foreach (Target target in m_Targets)
                {
                    if (target.Resolved)
                        resolved++;
                }
                if (resolved == 0)
                {
                    Mod.Log.Info("Decal: no run surface prefab present — live-material " +
                                 $"upgrade inert until '{GroomedPrefabName}'/'{SnowmakingPrefabName}' " +
                                 "is imported (expected pre-import)");
                    return;
                }

                // Even with every texture file missing the probe + scalars still justify activation; the loader logs precisely what will
                // be skipped.
                LoadTextures();

                m_Active = true;
                m_PendingCount = resolved;
                m_FramesUntilGiveUp = Tuning.BatchFindDeadlineFrames;
                m_FramesUntilReassert = Tuning.ReassertCheckFrames;
                m_FramesUntilSeasonCheck = Tuning.SeasonCheckFrames;
                m_WinterLook = EvaluateWinterLook();
                Mod.Log.Info($"Decal [season]: initial dress {(m_WinterLook ? "WINTER (wear/corduroy)" : "SUMMER (green mowed-grass)")}" +
                             DressSourceText());
                Mod.Log.Info($"Decal: armed — watching for {resolved} live batch material(s) " +
                             $"(retry every {Tuning.FindRetryIntervalFrames} frames, deadline " +
                             $"~{Tuning.BatchFindDeadlineFrames} frames; probe dump on first find)");
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunDecalSystem)} EXCEPTION during load setup — " +
                                  $"decal upgrade disabled until the next game load. Please report " +
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

                if (m_PendingCount > 0)
                {
                    // The countdown starts at the deadline, which is a multiple of the retry interval, so the very first update already scans
                    if (m_FramesUntilGiveUp % Tuning.FindRetryIntervalFrames == 0)
                        TryFindAndApply();
                    if (--m_FramesUntilGiveUp <= 0 && m_PendingCount > 0)
                        GiveUp();
                }
                else if (m_AppliedCount > 0 && --m_FramesUntilReassert <= 0)
                {
                    m_FramesUntilReassert = Tuning.ReassertCheckFrames;
                    Reassert();
                }

                // Seasonal dress check: cheap, hysteresis-guarded; on a flip every applied material gets re-dressed with the season-active
                // pair (prefab references included, so the game's own re-binds stay in season too)
                if (m_AppliedCount > 0 && --m_FramesUntilSeasonCheck <= 0)
                {
                    m_FramesUntilSeasonCheck = Tuning.SeasonCheckFrames;
                    bool winter = EvaluateWinterLook();
                    if (winter != m_WinterLook)
                    {
                        m_WinterLook = winter;
                        Mod.Log.Info($"Decal [season]: {(winter ? "-> WINTER dress (wear/corduroy set)" : "-> SUMMER dress (green mowed-grass set)")} — re-dressing {m_AppliedCount} material(s)" +
                                     DressSourceText());
                        m_ChangedWrites = 0;
                        m_SameWrites = 0;
                        foreach (Target target in m_Targets)
                        {
                            if (target.Applied && target.Material != null)
                                ApplyToMaterial(target);
                        }
                        Mod.Log.Info($"Decal [season]: re-dress wrote {m_ChangedWrites} change(s), {m_SameWrites} already in place");
                        m_ChangedWrites = 0;
                        m_SameWrites = 0;
                    }
                }
            }
            catch (Exception e)
            {
                // Never throw into the game loop. Unmissable, with the full stack, once per load; session caches are kept so the next
                // load re-arms automatically.
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunDecalSystem)} EXCEPTION — decal upgrade " +
                                  $"disabled until the next game load. Please report this stack:\n{e}");
                }
            }
        }


        /// <summary>One pass over all loaded materials; matches pending targets by the exact clone name AreaBatchSystem assigns.
        /// Also keeps the list of every "Area batch" material seen: that inventory is the diagnostic if ours never appears.</summary>
        private void TryFindAndApply()
        {
            Material[] all = Resources.FindObjectsOfTypeAll<Material>();
            m_LastScanBatchNames.Clear();
            foreach (Material material in all)
            {
                if (material == null)
                    continue; // destroyed-but-listed
                string name = material.name;
                if (!name.StartsWith(BatchMaterialPrefix, StringComparison.Ordinal))
                    continue;
                m_LastScanBatchNames.Add(name);
                foreach (Target target in m_Targets)
                {
                    if (!target.Resolved || target.Applied || target.GivenUp ||
                        name != target.BatchMaterialName)
                        continue;
                    target.Material = material;
                    ProbeIfNewInstance(material);
                                                    // Probe always runs before mutation so the dump shows the pristine vanilla values
                    ApplyToMaterial(target);
                    m_PendingCount--;
                    m_AppliedCount++;
                }
            }
            if (m_PendingCount == 0)
                LogActivationSummary();
        }

        /// <summary>The one-time WARN when the deadline expires with targets unfound; includes what does exist, because "our name
        /// never appeared" plus the actual inventory is a complete bug report.</summary>
        private void GiveUp()
        {
            var missing = new List<string>();
            foreach (Target target in m_Targets)
            {
                if (target.Resolved && !target.Applied && !target.GivenUp)
                {
                    target.GivenUp = true;
                    missing.Add($"'{target.BatchMaterialName}'");
                }
            }
            m_PendingCount = 0;
            Mod.Log.Warn($"Decal: {missing.Count} batch material(s) never appeared within " +
                         $"~{Tuning.BatchFindDeadlineFrames} frames: {string.Join(", ", missing)} — " +
                         $"live-material upgrade skipped for them this load. Area batch materials " +
                         $"that DO exist: [{string.Join(", ", m_LastScanBatchNames)}]");
            if (m_AppliedCount > 0)
                LogActivationSummary();
            else
                m_Active = false; // nothing applied, nothing to re-assert
        }

        /// <summary>The probe dump: one block per material instance, logged before any mutation so every value is vanilla. Keyed
        /// on instance id so a mid-session batch rebuild (a genuinely new clone) probes again, but re-finds of the same clone do
        /// not spam.</summary>
        private void ProbeIfNewInstance(Material material)
        {
            int instanceId = material.GetInstanceID();
            if (!m_ProbedInstances.Add(instanceId))
                return;
            // A developer dump of every shader property (about 50 lines per material, four per load)
            if (!SkiResortMod.Settings.SkiResortSettings.DiagnosticsOn)
                return;

            Shader shader = material.shader;
            var sb = new StringBuilder(4096);
            sb.AppendLine($"Decal [probe] material '{material.name}' (instance {instanceId}) — " +
                          "T1 deliverable, values are PRE-mutation:");
            sb.AppendLine($"  shader '{shader.name}' | material renderQueue {material.renderQueue} | " +
                          $"shader renderQueue {shader.renderQueue} | passCount {material.passCount}");

            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                int id = shader.GetPropertyNameId(i);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Texture:
                        Texture t = material.GetTexture(id);
                        sb.AppendLine(t != null
                            ? $"  Texture {name,-26} = '{t.name}' ({t.width}x{t.height}, {t.graphicsFormat}, wrap {t.wrapMode})"
                            : $"  Texture {name,-26} = null");
                        break;
                    case ShaderPropertyType.Float:
                        sb.AppendLine($"  Float   {name,-26} = {material.GetFloat(id):R}");
                        break;
                    case ShaderPropertyType.Range:
                        sb.AppendLine($"  Range   {name,-26} = {material.GetFloat(id):R}");
                        break;
                    case ShaderPropertyType.Int:
                        sb.AppendLine($"  Int     {name,-26} = {material.GetInteger(id)}");
                        break;
                    case ShaderPropertyType.Color:
                        Color c = material.GetColor(id);
                        sb.AppendLine($"  Color   {name,-26} = ({c.r:F3}, {c.g:F3}, {c.b:F3}, {c.a:F3})");
                        break;
                    case ShaderPropertyType.Vector:
                        Vector4 v = material.GetVector(id);
                        sb.AppendLine($"  Vector  {name,-26} = ({v.x:F3}, {v.y:F3}, {v.z:F3}, {v.w:F3})");
                        break;
                }
            }

            sb.Append("  Keywords declared ([ON] = enabled on this material):");
            foreach (LocalKeyword keyword in shader.keywordSpace.keywords)
                sb.Append($" {keyword.name}{(material.IsKeywordEnabled(keyword.name) ? "[ON]" : "")}");
            sb.AppendLine();
            sb.Append($"  Keywords enabled list: [{string.Join(", ", material.shaderKeywords)}]");
            Mod.Log.Info(sb.ToString());
        }

        /// <summary>Applies the full safe set to one live clone. Idempotent; the re-assert path calls it again verbatim. Every Set
        /// that changes the slot is logged with old -> new; one already in place is only counted.</summary>
        private void ApplyToMaterial(Target target)
        {
            Material material = target.Material;
            Dictionary<string, ShaderPropertyType> props = CollectShaderProperties(material.shader);
            target.AppliedTextures.Clear();
            target.AppliedFloats.Clear();

            // 1) Our decal texture pair: the live material for immediacy, and the prefab's texture references via runtime TextureAssets.
            //    AreaBatchSystem re-derives batch material state from the prefab component, so a live-only swap perpetually loses to the
            //    game's re-binds. With the prefab references patched, the game's own re-binds propagate our textures; the re-assert
            //    sentinel is then pure verification. Game mode only: editor saves must never capture runtime user-db references
            //    (OnGamePreload restores the originals before any editor session).
            //    Season-active pair: winter = the per-run wear/corduroy set, summer = the shared green mowed-grass set (falls back to the
            //    winter pair if the summer files are missing).
            Texture2D activeBaseTex = m_WinterLook || m_SummerBaseColorTex == null
                ? target.BaseColorTex : m_SummerBaseColorTex;
            Texture2D activeNormalTex = m_WinterLook || m_SummerNormalTex == null
                ? target.NormalTex : m_SummerNormalTex;
            TextureAsset activeBaseAsset = m_WinterLook || m_SummerBaseColorAsset == null
                ? target.BaseColorAsset : m_SummerBaseColorAsset;
            TextureAsset activeNormalAsset = m_WinterLook || m_SummerNormalAsset == null
                ? target.NormalAsset : m_SummerNormalAsset;

            target.BasePairSet =
                TrySetTexture(target, props, BaseColorMapProperty, activeBaseTex) &
                TrySetTexture(target, props, NormalMapProperty, activeNormalTex);
            if (m_GameMode && target.Rendered != null &&
                activeBaseAsset != null && activeNormalAsset != null)
            {
                if (!target.OrigRefsCaptured)
                {
                    target.OrigBaseColorRef = target.Rendered.m_BaseColorMap;
                    target.OrigNormalRef = target.Rendered.m_NormalMap;
                    target.OrigRefsCaptured = true;
                }
                target.Rendered.m_BaseColorMap = activeBaseAsset;
                target.Rendered.m_NormalMap = activeNormalAsset;
            }

            // 2) Snow slots, groomed surface only: swap the vanilla-bound snowalbedo2 pair for our corduroy, touching neither the _Snow
            //    float nor any keyword (see the Tuning.ApplySnowSlots note). Live material only (RenderedArea exposes no snow refs); the
            //    re-assert sentinel restores it after the game's re-derivations.
            bool snowBase = false, snowNormal = false;
            if (Tuning.ApplySnowSlots && target.UseSnowPair)
            {
                snowBase = TrySetTexture(target, props, SnowBaseColorMapProperty, m_SnowBaseColorTex);
                snowNormal = TrySetTextureAny(target, props, SnowNormalPropertyCandidates, m_SnowNormalTex);
            }
            target.SnowPairSet = snowBase & snowNormal;

            // 3) Doc-safe scalars, live + prefab mirror (RenderedArea fields exist for all three; set both). The mirror is
            //    runtime-only, nothing in this chain ever calls asset.Save.
            RenderedArea rendered = target.Rendered;
            if (TrySetFloat(target, props, NormalOpacityProperty, Tuning.NormalOpacity, rendered != null) &&
                rendered != null)
                rendered.m_NormalOpacity = Tuning.NormalOpacity;
            if (TrySetFloat(target, props, NormalAlphaSourceProperty, Tuning.NormalAlphaSource, rendered != null) &&
                rendered != null)
                rendered.m_NormalAlphaSource = Tuning.NormalAlphaSource;
            if (TrySetFloat(target, props, SmoothnessProperty, Tuning.Smoothness, rendered != null) &&
                rendered != null)
                rendered.m_Smoothness = Tuning.Smoothness;

            // 4) Keyword scaffolding, off (see engine notes). When flipped, float and keyword move together (HDRP gates on the keyword).
            if (Tuning.ApplyNormalOnlyMode)
            {
                material.DisableKeyword(AffectsAlbedoKeyword);
                TrySetFloat(target, props, AffectAlbedoProperty, 0f, mirrored: false);
                Mod.Log.Info($"Decal: '{material.name}': keyword {AffectsAlbedoKeyword} disabled " +
                             "(normal-only mode — terrain color untouched, corduroy relief only)");
            }

            target.Applied = true;
        }

        private void LogActivationSummary()
        {
            if (m_SummaryLogged)
                return;
            m_SummaryLogged = true;

            int resolved = 0, basePairs = 0, snowPairs = 0;
            var names = new List<string>();
            foreach (Target target in m_Targets)
            {
                if (!target.Resolved)
                    continue;
                resolved++;
                if (target.Applied)
                    names.Add(target.PrefabName);
                if (target.BasePairSet)
                    basePairs++;
                if (target.SnowPairSet)
                    snowPairs++;
            }
            Mod.Log.Info($"Decal: ACTIVE — {m_AppliedCount}/{resolved} run surface material(s) " +
                         $"upgraded live ({string.Join(", ", names)}): decal pair on {basePairs}, " +
                         $"snow slots on {snowPairs}, scalars {NormalOpacityProperty}={Tuning.NormalOpacity} " +
                         $"{NormalAlphaSourceProperty}={Tuning.NormalAlphaSource} " +
                         $"{SmoothnessProperty}={Tuning.Smoothness}, normal-only mode " +
                         $"{(Tuning.ApplyNormalOnlyMode ? "ON" : "OFF (probe-gated)")}; winter look " +
                         "rides the game's snow sim from here — no seasonal decal code, by design; " +
                         $"re-assert sentinel every {Tuning.ReassertCheckFrames} frames" +
                         $"; {m_ChangedWrites} change(s), {m_SameWrites} already in place");
            m_ChangedWrites = 0;
            m_SameWrites = 0;
        }

        /// <summary>True while the per-load line budget lasts; the last allowed line carries a note that later ones are dropped.</summary>
        private bool NextReassertLine(out string capNote)
        {
            capNote = string.Empty;
            if (m_ReassertLines >= Tuning.ReassertLinesMax)
                return false;
            m_ReassertLines++;
            if (m_ReassertLines == Tuning.ReassertLinesMax)
                capNote = $" — further re-assert lines this load are not written (cap {Tuning.ReassertLinesMax})";
            return true;
        }

        // Re-assert sentinel: our mutations must survive texture-quality changes (UpdateTextures re-Loads prefab assets over the
        // live clone) and batch rebuilds (the clone is Destroyed and rebuilt). Verify cheaply, re-apply loudly; each re-apply line
        // in the log is evidence, not noise (up to ReassertLinesMax per load).
        private void Reassert()
        {
            Material[] scan = null;
            foreach (Target target in m_Targets)
            {
                if (!target.Applied)
                    continue;

                if (target.Material == null) // Unity-destroyed: the batch was rebuilt
                {
                    if (scan == null)
                        scan = Resources.FindObjectsOfTypeAll<Material>();
                    Material found = null;
                    foreach (Material material in scan)
                    {
                        if (material != null && material.name == target.BatchMaterialName)
                        {
                            found = material;
                            break;
                        }
                    }
                    if (found == null)
                    {
                        if (!target.RebuildWarned)
                        {
                            target.RebuildWarned = true;
                            Mod.Log.Warn($"Decal: '{target.BatchMaterialName}' was destroyed and " +
                                         "has not been rebuilt — still watching (logged once per load)");
                        }
                        continue;
                    }
                    m_ReassertQuiet = !NextReassertLine(out string rebuiltCap);
                    if (!m_ReassertQuiet)
                        Mod.Log.Info($"Decal [reassert]: '{target.BatchMaterialName}' was rebuilt by " +
                                     "the game — re-probing and re-applying (T8 path)" + rebuiltCap);
                    target.Material = found;
                    target.RebuildWarned = false;
                    ProbeIfNewInstance(found);
                    ApplyToMaterial(target);
                    m_ReassertQuiet = false;
                    continue;
                }

                string drift = DetectDrift(target);
                if (drift != null)
                {
                    m_ReassertQuiet = !NextReassertLine(out string driftCap);
                    if (!m_ReassertQuiet)
                        Mod.Log.Info($"Decal [reassert]: '{target.BatchMaterialName}' drifted ({drift}) — " +
                                     "re-applying (T8 evidence: the game re-loaded prefab values over " +
                                     "our live set)" + driftCap);
                    ApplyToMaterial(target);
                    m_ReassertQuiet = false;
                }
            }
        }

        /// <summary>A human-readable description of the first applied value the live material no longer carries, or null when
        /// everything still stands</summary>
        private static string DetectDrift(Target target)
        {
            Material material = target.Material;
            foreach ((int id, string name, Texture tex) in target.AppliedTextures)
            {
                Texture current = material.GetTexture(id);
                if (!ReferenceEquals(current, tex))
                    return $"{name} is now '{(current != null ? current.name : "null")}' " +
                           $"instead of '{tex.name}'";
            }
            foreach ((int id, string name, float value) in target.AppliedFloats)
            {
                float current = material.GetFloat(id);
                if (Math.Abs(current - value) > 0.001f)
                    return $"{name} is now {current:R} instead of {value:R}";
            }
            return null;
        }

        // Defensive property setters, gated on the live shader's actual property table (enumerated via Shader.GetPropertyCount,
        // the same API the probe uses), never on assumptions. A missing property degrades to a one-time WARN carrying the real
        // list.

        private bool TrySetTexture(Target target, Dictionary<string, ShaderPropertyType> props,
            string propertyName, Texture2D texture)
        {
            return TrySetTextureAny(target, props, new[] { propertyName }, texture);
        }

        private bool TrySetTextureAny(Target target, Dictionary<string, ShaderPropertyType> props,
            string[] candidateNames, Texture2D texture)
        {
            if (texture == null)
                return false; // file missing/undecodable: already logged by the loader

            string propertyName = null;
            foreach (string candidate in candidateNames)
            {
                if (props.TryGetValue(candidate, out ShaderPropertyType type) &&
                    type == ShaderPropertyType.Texture)
                {
                    propertyName = candidate;
                    break;
                }
            }
            if (propertyName == null)
            {
                WarnMissingProperty(string.Join("|", candidateNames), target.Material, props);
                return false;
            }

            Material material = target.Material;
            int id = Shader.PropertyToID(propertyName);
            Texture old = material.GetTexture(id);
            material.SetTexture(id, texture);
            target.AppliedTextures.Add((id, propertyName, texture));
            // Log only a change, by UnityEngine.Object's !=: the same live texture reads as equal, a destroyed old one as null, so it
            // counts as a change. Unity's documented equality, unverified here (no UnityEngine decompile in tools/; DetectDrift's
            // ReferenceEquals is the stricter twin, and a stricter miss would only log one extra line). The write and the sentinel
            // record above happen either way.
            if (old != texture)
            {
                m_ChangedWrites++;
                if (!m_ReassertQuiet)
                    Mod.Log.Info($"Decal: '{material.name}': {propertyName} <- '{texture.name}' " +
                             $"({texture.width}x{texture.height}), was '{(old != null ? old.name : "null")}'");
            }
            else
            {
                m_SameWrites++;
            }
            return true;
        }

        private bool TrySetFloat(Target target, Dictionary<string, ShaderPropertyType> props,
            string propertyName, float value, bool mirrored)
        {
            if (!props.TryGetValue(propertyName, out ShaderPropertyType type) ||
                (type != ShaderPropertyType.Float && type != ShaderPropertyType.Range))
            {
                WarnMissingProperty(propertyName, target.Material, props);
                return false;
            }

            Material material = target.Material;
            int id = Shader.PropertyToID(propertyName);
            float old = material.GetFloat(id);
            material.SetFloat(id, value);
            target.AppliedFloats.Add((id, propertyName, value));
            // Log only a change (float.Equals: the exact value, NaN equal to NaN). The write, the sentinel record and the caller's
            // RenderedArea mirror happen either way.
            if (!old.Equals(value))
            {
                m_ChangedWrites++;
                if (!m_ReassertQuiet)
                    Mod.Log.Info($"Decal: '{material.name}': {propertyName} {old:R} -> {value:R}" +
                             (mirrored ? " (+ prefab RenderedArea mirror)" : ""));
            }
            else
            {
                m_SameWrites++;
            }
            return true;
        }

        /// <summary>One WARN per load, carrying the live shader's real property table: exactly the data needed when an inventoried
        /// name is gone</summary>
        private void WarnMissingProperty(string propertyName, Material material,
            Dictionary<string, ShaderPropertyType> props)
        {
            if (m_PropertyWarnLogged)
                return;
            m_PropertyWarnLogged = true;

            var textures = new List<string>();
            var others = new List<string>();
            foreach (KeyValuePair<string, ShaderPropertyType> entry in props)
            {
                if (entry.Value == ShaderPropertyType.Texture)
                    textures.Add(entry.Key);
                else
                    others.Add(entry.Key);
            }
            Mod.Log.Warn($"Decal: expected property '{propertyName}' not on '{material.name}' " +
                         $"(shader '{material.shader.name}') — application skipped. The live " +
                         $"property table (THIS is a probe result): textures " +
                         $"[{string.Join(", ", textures)}]; floats/vectors/ints " +
                         $"[{string.Join(", ", others)}] (further misses this load stay silent)");
        }

        private static Dictionary<string, ShaderPropertyType> CollectShaderProperties(Shader shader)
        {
            int count = shader.GetPropertyCount();
            var props = new Dictionary<string, ShaderPropertyType>(count, StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
                props[shader.GetPropertyName(i)] = shader.GetPropertyType(i);
            return props;
        }

        // Textures: PNG files -> readable Texture2Ds, session-cached. Loader settings: Repeat wrap (the
        // decal UV tiles world-XZ via colossal_UVScale, Clamp would smear the last texel row; trilinear, aniso 8). BaseColor is
        // sRGB, normals are linear data; plain-RGB PNG normals decode correctly through HDRP's RGorAG unpack because their alpha is
        // opaque. No TextureAssets here: live-material SetTexture needs only the Texture2D.
        private void LoadTextures()
        {
            string root = Mod.ModRootPath;
            if (string.IsNullOrEmpty(root))
            {
                Mod.Log.Warn("Decal: mod root path unknown (TryGetExecutableAsset failed at " +
                             "OnLoad) — all texture applications skipped this load; probe and " +
                             "scalars still run");
                return;
            }
            string dir = Path.Combine(root, TexturesFolderName);

            var missing = new List<string>();
            foreach (Target target in m_Targets)
            {
                if (target.BaseColorTex == null)
                {
                    target.BaseColorTex = LoadPng(dir, target.BaseColorFileName, linear: false, missing);
                    // Winter fade per type, once, before asset registration pins the texture (same ordering rule as the summer set below)
                    if (target.BaseColorTex != null && target.WinterAlphaScale < 1f)
                        ScaleColors(target.BaseColorTex, 1f, target.WinterAlphaScale);
                    // Two targets may load the same file (night wears snowmaking's dress). RegisterTextureAsset keys the user-db entry on
                    // texture.name, which comes from the filename, so without a per-target prefix the second target's asset would resolve to
                    // the first target's texture instance, and the drift sentinel would re-apply forever (its own instance vs the shared
                    // asset's). Names are runtime-only, never saved.
                    if (target.BaseColorTex != null)
                        target.BaseColorTex.name = target.PrefabName + target.BaseColorTex.name;
                }
                if (target.NormalTex == null)
                {
                    target.NormalTex = LoadPng(dir, target.NormalFileName, linear: true, missing);
                    if (target.NormalTex != null)
                        target.NormalTex.name = target.PrefabName + target.NormalTex.name;
                }

                // Wrap the same textures as user-db TextureAssets (in-memory index entry only, never Saved) so the prefab's texture references can point at them (ApplyToMaterial)
                if (target.BaseColorAsset == null && target.BaseColorTex != null)
                    target.BaseColorAsset = RegisterTextureAsset(target.BaseColorTex);
                if (target.NormalAsset == null && target.NormalTex != null)
                    target.NormalAsset = RegisterTextureAsset(target.NormalTex);
            }
            if (m_SnowBaseColorTex == null)
                m_SnowBaseColorTex = LoadPng(dir, SnowBaseColorFile, linear: false, missing);
            if (m_SnowNormalTex == null)
                m_SnowNormalTex = LoadPng(dir, SnowNormalFile, linear: true, missing);
            if (m_SummerBaseColorTex == null)
            {
                m_SummerBaseColorTex = LoadPng(dir, SummerBaseColorFile, linear: false, missing);
                // Fade the opaque terrain green into a see-through worn lane before the asset registration pins the texture
                if (m_SummerBaseColorTex != null)
                    ScaleColors(m_SummerBaseColorTex, Tuning.SummerRgbScale, Tuning.SummerAlphaScale);
            }
            if (m_SummerNormalTex == null)
                m_SummerNormalTex = LoadPng(dir, SummerNormalFile, linear: true, missing);
            if (m_SummerBaseColorAsset == null && m_SummerBaseColorTex != null)
                m_SummerBaseColorAsset = RegisterTextureAsset(m_SummerBaseColorTex);
            if (m_SummerNormalAsset == null && m_SummerNormalTex != null)
                m_SummerNormalAsset = RegisterTextureAsset(m_SummerNormalTex);

            if (missing.Count > 0)
                Mod.Log.Warn($"Decal: {missing.Count} of 8 texture file(s) unavailable under " +
                             $"'{Mod.ScrubUserPath(dir)}': {string.Join(", ", missing)} — those applications are " +
                             "skipped this load (retried next load); everything else proceeds");
            else
                Mod.Log.Info($"Decal: 8 texture file(s) ready under '{Mod.ScrubUserPath(dir)}' (cached across loads)");
        }

        /// <summary>Winter-look decision: the primary driver is measured snow on the runs (TryGetDressDepth: the injection
        /// system's managed mean, else the natural runs' measured mean; hysteresis 0.5-2 cm) so green can never show while trails
        /// are white. The fallback, for loads where no measurement has happened yet, is temperature hysteresis on the 24-hour mean
        /// (the instantaneous ambient flips daily). Records what it read for the log lines only.</summary>
        private bool EvaluateWinterLook()
        {
            if (m_SnowInjectionSystem != null &&
                m_SnowInjectionSystem.TryGetDressDepth(out float runDepth, out RunDressSource source))
            {
                m_DressSource = source;
                m_DressValue = runDepth;
                return m_WinterLook
                    ? runDepth > Tuning.DressBareDepthM
                    : runDepth > Tuning.DressSnowyDepthM;
            }
            // The fallback is the 24-hour mean (the instantaneous ambient flips daily)
            float dayMean = m_SnowLineSystem.GetDayMeanTemperature();
            float freezing = m_SnowLineSystem.GetFreezingTemperature();
            m_DressSource = RunDressSource.None;
            m_DressValue = dayMean;
            return m_WinterLook
                ? dayMean <= freezing + Tuning.WinterLookOffC
                : dayMean <= freezing + Tuning.WinterLookOnC;
        }

        /// <summary>The suffix of the two `Decal [season]` lines: what the last EvaluateWinterLook read</summary>
        private string DressSourceText()
        {
            switch (m_DressSource)
            {
                case RunDressSource.Managed:
                    return $" (source: managed run snow {m_DressValue:F3} m)";
                case RunDressSource.NaturalMeasured:
                    return $" (source: natural runs' measured snow {m_DressValue:F3} m)";
                default:
                    return $" (source: day-mean temperature {m_DressValue:F1} C, no snow measured yet)";
            }
        }

        /// <summary>In-place RGB/alpha scale (summer set only). LoadImage textures stay CPU-readable; Apply(true) regenerates mips,
        /// readable is kept so a future pass could re-scale.</summary>
        private static void ScaleColors(Texture2D texture, float rgbScale, float alphaScale)
        {
            Color32[] pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                p.r = (byte)(p.r * rgbScale);
                p.g = (byte)(p.g * rgbScale);
                p.b = (byte)(p.b * rgbScale);
                p.a = (byte)(p.a * alphaScale);
                pixels[i] = p;
            }
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        }

        /// <summary>User-db TextureAsset wrapping an already-loaded texture: AddAsset creates an
        /// in-memory index entry only, SetData-pinned by the AddAsset overload, nothing ever written to disk. Reuse-safe across loads.</summary>
        private static TextureAsset RegisterTextureAsset(Texture2D texture)
        {
            ILocalAssetDatabase db = AssetDatabase.user;
            AssetDataPath assetPath = AssetDataPath.Create(nameof(SkiResortMod), texture.name);
            if (db.Exists(assetPath, out TextureAsset existing) && existing != null)
                return existing;
            return db.AddAsset<TextureAsset, Texture>(assetPath, texture);
        }

        private static Texture2D LoadPng(string dir, string fileName, bool linear, List<string> missing)
        {
            string path = Path.Combine(dir, fileName);
            if (!File.Exists(path))
            {
                missing.Add(fileName);
                return null;
            }
            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear: linear)
            {
                name = Path.GetFileNameWithoutExtension(fileName),
            };
            if (!ImageConversion.LoadImage(texture, bytes))
            {
                UnityEngine.Object.Destroy(texture);
                missing.Add(fileName + " (decode FAILED)");
                return null;
            }
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            return texture;
        }

        // Prefab resolution: fast PrefabID path + name-scan fallback, tolerating missing prefabs (the run surfaces may not be
        // imported yet; the system logs its inertness once per load and sleeps until they exist)
        private void ResolveRunPrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;

            foreach (Target target in m_Targets)
            {
                PrefabBase prefabBase = null;
                if (!m_PrefabSystem.TryGetPrefab(new PrefabID("SurfacePrefab", target.PrefabName),
                        out prefabBase) || prefabBase == null)
                {
                    var candidates = m_PrefabScanQuery.ToEntityArray(Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            if (m_PrefabSystem.GetPrefabName(candidates[i]) == target.PrefabName &&
                                m_PrefabSystem.TryGetPrefab(candidates[i], out PrefabBase scanned) &&
                                scanned != null)
                            {
                                prefabBase = scanned;
                                break;
                            }
                        }
                    }
                    finally
                    {
                        candidates.Dispose();
                    }
                }

                if (prefabBase == null)
                {
                    Mod.Log.Info($"Decal: run surface prefab '{target.PrefabName}' not found (tolerated)");
                    continue;
                }

                target.Resolved = true;
                // The batch clone is named after the managed prefab object, so build the match string from it, not from our constant
                target.BatchMaterialName = BatchMaterialPrefix + prefabBase.name + ")";
                target.Rendered = prefabBase.GetComponent<RenderedArea>();
                if (target.Rendered == null)
                    Mod.Log.Warn($"Decal: '{prefabBase.name}' has no RenderedArea component — " +
                                 "scalar prefab mirroring skipped (live material still upgraded)");
                Mod.Log.Info($"Decal: resolved run surface prefab '{prefabBase.name}' " +
                             $"(watching for '{target.BatchMaterialName}')");
            }
        }
    }
}
