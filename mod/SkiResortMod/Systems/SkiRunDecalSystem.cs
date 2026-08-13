// SkiRunDecalSystem — run-rendering Layer 2, Tier 1: upgrades the LIVE material behind our
// two ski-run decal surfaces at runtime. No Editor re-import, no custom shader, no shared
// asset touched. AreaBatchSystem clones the shared AreaDecalShader material once per surface
// prefab and names the clone "Area batch (<prefab name>)" (fullsrc AreaBatchSystem.cs
// 2094–2097); everything the RenderedArea component does NOT expose — the shader's built-in
// snow slots, the _MATERIAL_AFFECTS_* keywords, the _Affect* toggles — is reachable only by
// mutating that clone (docs/research/area-material-override.md §3 Tier 1, the pattern
// SkiRunSplatSystem.ApplyDecalVisibility already proves in ship). Mutating OUR clone cannot
// leak to other surfaces: the game clones per batch and we never touch the shared
// m_RenderedAreaMaterial, the map, or any other prefab's clone.
//
// ─────────────────────────────────────────────────────────────────────────────────────────
// WHAT RUNS EACH LOAD (Game AND Editor — the decal look is our own asset, no map content
// is involved, so both modes are safe):
//
//   1. PROBE FIRST (test T1 — it gates the interpretation of everything after it): on first
//      sight of each live clone, dump shader name + EVERY shader property (textures, floats,
//      vectors, colors, keywords) with its CURRENT pre-mutation value, one comprehensive
//      block per material. That log block is the deliverable the user's test session brings
//      home; the post-probe round (keyword flips, _Snow semantics) is decided from it.
//   2. APPLY, safe defaults only:
//        * our new decal texture pair -> _BaseColorMap/_NormalMap (live material ONLY — the
//          prefab's m_BaseColorMap/m_NormalMap are AssetReference<TextureAsset> fields, a
//          runtime Texture2D cannot go there; live-only is correct, not a shortcut),
//        * groomed-corduroy winter pair -> the shader's _Snow_BaseColorMap/_Snow_Normal
//          slots (fed by the game's global _SnowMap — SnowSystem.cs 277/369),
//        * conservative scalars the research doc marks safe (Tier 0 list): _NormalOpacity 1,
//          _NormalAlphaSource 1 (normal strength decoupled from BaseColor-alpha coverage),
//          _Smoothness 0.4 (snow sparkle without gloss) — each mirrored onto the managed
//          RenderedArea field of the same name so batches built LATER this session agree.
//   3. RE-ASSERT (test T8): EnabledShadersUpdated/texture-quality changes re-Load the
//      prefab's texture assets into the live clone (AreaBatchSystem.UpdateTextures, 1964),
//      and batch rebuilds Destroy our mutated clone outright — a cheap periodic sentinel
//      verifies our values still stand and re-applies (loudly) when the game overwrote them.
//
// DEFERRED TO THE POST-PROBE ROUND (scaffolded, not flipped):
//   * _MATERIAL_AFFECTS_ALBEDO stays AS-IS — the alpha-coverage BaseColor already modulates
//     albedo contribution; normal-only summer mode (T3) is one Tuning bool away.
//   * _Snow float untouched — T5 must first answer whether it multiplies the global
//     _SnowMap sample or overrides it. Swapping only the snow TEXTURES is safe either way:
//     the vanilla material already whitens in winter, we only change what the white is.
//   * _Wetness/_Porosity polish, _BaseColor alpha (T2b) — probe data first.
//
// SEASONAL — DELIBERATELY ABSENT: this system does NO seasonal texture swapping. The snow
// slots make the winter look ride the game's own snow state (melting at the snow line,
// accumulating in storms, INCLUDING Layer 1's injected snow) with zero seasonal code of
// ours. That is the design (run-rendering-synthesis.md Layer 2), not an omission.
//
// THE m_Version GOTCHA (area-material-override.md §2): AreaBatchSystem mutates
// RenderedArea.m_Version to OverrideMaterial in place the first time a batch builds with
// m_Material == null, so a mod assigning m_Material mid-session sees different behavior
// than at first load. We never assign m_Material (Tier 2 stays in the pocket), so the
// gotcha cannot arise here — recorded so a future Tier 2 escalation trips over this
// comment before it trips over the bug.
//
// SCHEDULING: PreCulling, anchored AFTER the vanilla AreaBatchSystem — batch clones build
// lazily on prefab Created, and anchoring after the builder means the frame a clone exists
// our find-retry can see it. PreCulling runs every frame in Game AND Editor, paused
// included, so the retry countdown and the re-assert sentinel actually tick.
//
// OBSERVABILITY + DEFENSIVE CONTRACT (v1.2.1 discipline — silence must be impossible):
// per-load resolved-state logging, the probe blocks, one line per applied property with
// old -> new values, a one-time WARN with the ACTUAL property list if an expected property
// is absent (that IS a probe result), an activation summary line, and the ChairPinSystem
// error pattern: never throw into the game loop — top-level try/catch, one-shot "!!!" with
// the stack, disabled for the current load only, re-armed at OnGameLoadingComplete.
//
// COORDINATION NOTE: SkiRunSplatSystem.ApplyDecalVisibility finds the same live clones by
// the same name to toggle colossal_DecalLayerMask. This system SUPERSEDES that material-
// finding approach (retry-until-found vs find-once); reconciliation is the coordinator's —
// per instructions, SkiRunSplatSystem.cs is left untouched by this change.
// ─────────────────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        // ── Tuning — the single block ────────────────────────────────────────────────────
        private static class Tuning
        {
            // Batch clones build lazily (prefab Created -> AreaBatchSystem.UpdatePrefabs);
            // scan every RetryIntervalFrames until found, give up loudly after the deadline.
            public const int BatchFindDeadlineFrames = 300;
            public const int FindRetryIntervalFrames = 15;

            // T8 sentinel cadence: verify our live values still stand (texture-quality
            // changes and EnabledShadersUpdated re-Load prefab assets over them; batch
            // rebuilds Destroy the clone). Two materials, a handful of Get* calls — cheap.
            public const int ReassertCheckFrames = 256;

            // Doc-safe scalars (area-material-override.md §3 Tier 0). Each is mirrored to
            // the managed RenderedArea field of the same name (item lists at RenderedArea.cs
            // 58–76) so later-built batches agree with the live mutation.
            public const float NormalOpacity = 1f;        // corduroy relief at full strength
            public const float NormalAlphaSource = 1f;    // 1 = normal masked by its OWN
                                                          // alpha, not BaseColor coverage —
                                                          // relief survives albedo fade
            public const float Smoothness = 0.4f;         // doc: 0.3–0.45, sparkle w/o gloss

            // FUTURE FLIP (post-probe round, test T3): normal-only summer mode — terrain
            // color untouched, corduroy relief only. Requires BOTH the keyword and the float
            // (HDRP gates on the keyword; the float is the inspector mirror). readonly, not
            // const, so flipping it is a one-token change without unreachable-code warnings.
            public static readonly bool ApplyNormalOnlyMode = false;

            // FIELD-DISABLED 2026-08-12: binding textures into the previously-EMPTY snow
            // slots switched the decal's snow layer permanently ON — runs rendered solid
            // white corduroy immune to 50 °C, the dev remove-snow button, and every
            // snow-mask change (two calibration rounds were judged against this artifact).
            // The layer is evidently not snow-map-driven by texture presence alone; the
            // T5 probe must settle the _Snow scalar/keyword semantics before re-enabling.
            public static readonly bool ApplySnowSlots = false;
        }

        // Run surfaces — same tolerant name resolution as the sibling systems.
        private const string GroomedPrefabName = "SkiRunGroomedA";
        private const string SnowmakingPrefabName = "SkiRunSnowmakingA";

        // Texture file contract (parallel texture agent delivers these; missing files are
        // logged once per load and their applications skipped — retried next load).
        // Resolved under <mod dir>\Textures\, same folder the splat system uses.
        private const string TexturesFolderName = "Textures";
        private const string GroomedBaseColorFile = "SkiRunDecalGroomed_BaseColor.png";
        private const string GroomedNormalFile = "SkiRunDecalGroomed_Normal.png";
        private const string SnowmakingBaseColorFile = "SkiRunDecalSnowmaking_BaseColor.png";
        private const string SnowmakingNormalFile = "SkiRunDecalSnowmaking_Normal.png";
        // Snow slots reuse the EXISTING splat winter pair — one corduroy source of truth.
        private const string SnowBaseColorFile = "SkiTerrainSnowCorduroy_BaseColor.png";
        private const string SnowNormalFile = "SkiTerrainSnowCorduroy_Normal.png";

        // Shader property names (area-material-override.md §1.1 inventory; every use below
        // is gated on the live shader's ACTUAL property table, so a game patch renaming one
        // degrades to a logged skip, never an exception or a silent misapply).
        private const string BatchMaterialPrefix = "Area batch (";
        private const string BaseColorMapProperty = "_BaseColorMap";
        private const string NormalMapProperty = "_NormalMap";
        private const string SnowBaseColorMapProperty = "_Snow_BaseColorMap";
        // The doc says _Snow_Normal (no "Map" suffix — verified against the serialized
        // shader's property table); the alternate spelling is tried defensively anyway.
        private static readonly string[] SnowNormalPropertyCandidates =
            { "_Snow_Normal", "_Snow_NormalMap" };
        private const string NormalOpacityProperty = "_NormalOpacity";
        private const string NormalAlphaSourceProperty = "_NormalAlphaSource";
        private const string SmoothnessProperty = "_Smoothness";
        // Post-probe constants (T3 normal-only mode) — wired behind Tuning.ApplyNormalOnlyMode.
        private const string AffectAlbedoProperty = "_AffectAlbedo";
        private const string AffectsAlbedoKeyword = "_MATERIAL_AFFECTS_ALBEDO";

        /// <summary>One run surface prefab + its live batch clone + what we applied to it.</summary>
        private sealed class Target
        {
            public readonly string PrefabName;
            public readonly string BaseColorFileName;
            public readonly string NormalFileName;

            // Session-cached (prefabs and loaded textures outlive individual loads).
            public bool Resolved;
            public string BatchMaterialName;   // "Area batch (<resolved prefab name>)"
            public RenderedArea Rendered;      // managed prefab component (scalar mirror)
            public Texture2D BaseColorTex;
            public Texture2D NormalTex;

            // Per-load.
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

            public Target(string prefabName, string baseColorFileName, string normalFileName)
            {
                PrefabName = prefabName;
                BaseColorFileName = baseColorFileName;
                NormalFileName = normalFileName;
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

        // Session-cached.
        private bool m_PrefabsResolved;
        private Texture2D m_SnowBaseColorTex;
        private Texture2D m_SnowNormalTex;

        // Per-load state.
        private bool m_Active;
        private bool m_Disabled;                // tripped by the catch; re-armed per load
        private bool m_ErrorLogged;             // one "!!!" with stack per load
        private bool m_SummaryLogged;
        private bool m_PropertyWarnLogged;      // one property-list WARN per load
        private int m_PendingCount;
        private int m_AppliedCount;
        private int m_FramesUntilGiveUp;
        private int m_FramesUntilReassert;
        private readonly HashSet<int> m_ProbedInstances = new HashSet<int>();
        private readonly List<string> m_LastScanBatchNames = new List<string>();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            m_Targets = new[]
            {
                new Target(GroomedPrefabName, GroomedBaseColorFile, GroomedNormalFile),
                new Target(SnowmakingPrefabName, SnowmakingBaseColorFile, SnowmakingNormalFile),
            };
            Mod.Log.Info($"{nameof(SkiRunDecalSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Re-arm and reset per load, whatever the mode — the batch clones this system
            // mutated last load are gone (prefab Deleted/Created rebuilds them per load).
            m_Active = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_SummaryLogged = false;
            m_PropertyWarnLogged = false;
            m_PendingCount = 0;
            m_AppliedCount = 0;
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

                // Even with EVERY texture file missing the probe + scalars still justify
                // activation — the loader logs precisely what will be skipped.
                LoadTextures();

                m_Active = true;
                m_PendingCount = resolved;
                m_FramesUntilGiveUp = Tuning.BatchFindDeadlineFrames;
                m_FramesUntilReassert = Tuning.ReassertCheckFrames;
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
                    // Countdown starts at the deadline, which is a multiple of the retry
                    // interval — so the very first update already scans.
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
            }
            catch (Exception e)
            {
                // Never throw into the game loop. Unmissable, with the full stack, once per
                // load; session caches are KEPT so the next load re-arms automatically.
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunDecalSystem)} EXCEPTION — decal upgrade " +
                                  $"disabled until the next game load. Please report this stack:\n{e}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Find + probe + apply.
        // ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>One pass over all loaded materials; matches pending targets by the exact
        /// clone name AreaBatchSystem assigns. Also keeps the list of every "Area batch"
        /// material seen — that inventory IS the diagnostic if ours never appears.</summary>
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
                    ProbeIfNewInstance(material);   // ALWAYS before mutation — the dump must
                                                    // show the pristine vanilla values
                    ApplyToMaterial(target);
                    m_PendingCount--;
                    m_AppliedCount++;
                }
            }
            if (m_PendingCount == 0)
                LogActivationSummary();
        }

        /// <summary>The one-time WARN when the deadline expires with targets unfound —
        /// includes what DOES exist, because "our name never appeared" plus the actual
        /// inventory is a complete bug report.</summary>
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
                m_Active = false; // nothing applied, nothing to re-assert — fully idle
        }

        /// <summary>T1 — the probe dump. One comprehensive block per material instance,
        /// logged BEFORE any mutation so every value is vanilla. Keyed on instance id so a
        /// mid-session batch rebuild (a genuinely new clone) probes again, but re-finds of
        /// the same clone do not spam.</summary>
        private void ProbeIfNewInstance(Material material)
        {
            int instanceId = material.GetInstanceID();
            if (!m_ProbedInstances.Add(instanceId))
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

        /// <summary>Applies the full safe set to one live clone. Idempotent — the re-assert
        /// path calls it again verbatim; every Set is logged with old -> new.</summary>
        private void ApplyToMaterial(Target target)
        {
            Material material = target.Material;
            Dictionary<string, ShaderPropertyType> props = CollectShaderProperties(material.shader);
            target.AppliedTextures.Clear();
            target.AppliedFloats.Clear();

            // 1) Our decal texture pair — live material ONLY (prefab fields are asset
            //    references; see header). '&' not '&&': both attempts must always run.
            target.BasePairSet =
                TrySetTexture(target, props, BaseColorMapProperty, target.BaseColorTex) &
                TrySetTexture(target, props, NormalMapProperty, target.NormalTex);

            // 2) Snow slots — DISABLED pending T5 (Tuning.ApplySnowSlots): binding textures
            //    into empty slots turned the layer permanently on (solid white immune to
            //    temperature and the dev remove-snow — the 2026-08-12 field artifact).
            bool snowBase = false, snowNormal = false;
            if (Tuning.ApplySnowSlots)
            {
                snowBase = TrySetTexture(target, props, SnowBaseColorMapProperty, m_SnowBaseColorTex);
                snowNormal = TrySetTextureAny(target, props, SnowNormalPropertyCandidates, m_SnowNormalTex);
            }
            target.SnowPairSet = snowBase & snowNormal;

            // 3) Doc-safe scalars, live + prefab mirror (RenderedArea fields exist for all
            //    three — item 7's "set both" rule; the mirror is runtime-only, nothing in
            //    this chain ever calls asset.Save).
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

            // 4) Keyword scaffolding — OFF this round (see header: deferred to probes).
            //    When flipped, float AND keyword move together (HDRP gates on the keyword).
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
                         $"re-assert sentinel every {Tuning.ReassertCheckFrames} frames");
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // T8 sentinel: our mutations must survive texture-quality changes (UpdateTextures
        // re-Loads prefab assets over the live clone) and batch rebuilds (the clone is
        // Destroyed and rebuilt). Verify cheaply, re-apply loudly — each re-apply line in
        // the log is T8 evidence, not noise.
        // ─────────────────────────────────────────────────────────────────────────────────
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
                    Mod.Log.Info($"Decal [reassert]: '{target.BatchMaterialName}' was rebuilt by " +
                                 "the game — re-probing and re-applying (T8 path)");
                    target.Material = found;
                    target.RebuildWarned = false;
                    ProbeIfNewInstance(found);
                    ApplyToMaterial(target);
                    continue;
                }

                string drift = DetectDrift(target);
                if (drift != null)
                {
                    Mod.Log.Info($"Decal [reassert]: '{target.BatchMaterialName}' drifted ({drift}) — " +
                                 "re-applying (T8 evidence: the game re-loaded prefab values over " +
                                 "our live set)");
                    ApplyToMaterial(target);
                }
            }
        }

        /// <summary>Returns a human-readable description of the first applied value the live
        /// material no longer carries, or null when everything still stands.</summary>
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

        // ─────────────────────────────────────────────────────────────────────────────────
        // Defensive property setters — gated on the live shader's ACTUAL property table
        // (enumerated via Shader.GetPropertyCount, the same API the probe uses), never on
        // assumptions. A missing property degrades to a one-time WARN carrying the real
        // list — which is itself a probe result.
        // ─────────────────────────────────────────────────────────────────────────────────

        private bool TrySetTexture(Target target, Dictionary<string, ShaderPropertyType> props,
            string propertyName, Texture2D texture)
        {
            return TrySetTextureAny(target, props, new[] { propertyName }, texture);
        }

        private bool TrySetTextureAny(Target target, Dictionary<string, ShaderPropertyType> props,
            string[] candidateNames, Texture2D texture)
        {
            if (texture == null)
                return false; // file missing/undecodable — already logged by the loader

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
            Mod.Log.Info($"Decal: '{material.name}': {propertyName} <- '{texture.name}' " +
                         $"({texture.width}x{texture.height}), was '{(old != null ? old.name : "null")}'");
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
            Mod.Log.Info($"Decal: '{material.name}': {propertyName} {old:R} -> {value:R}" +
                         (mirrored ? " (+ prefab RenderedArea mirror)" : ""));
            return true;
        }

        /// <summary>One WARN per load, carrying the live shader's real property table —
        /// exactly the data the post-probe round needs when a doc-inventoried name is gone.</summary>
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

        // ─────────────────────────────────────────────────────────────────────────────────
        // Textures: PNG files -> readable Texture2Ds, session-cached. Same loader settings
        // as SkiRunSplatSystem (Repeat wrap — the decal UV tiles world-XZ via
        // colossal_UVScale, Clamp would smear the last texel row; trilinear, aniso 8).
        // BaseColor is sRGB, normals are linear data; plain-RGB PNG normals decode
        // correctly through HDRP's RGorAG unpack because their alpha is opaque.
        // No TextureAssets here — live-material SetTexture needs only the Texture2D.
        // ─────────────────────────────────────────────────────────────────────────────────
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
                    target.BaseColorTex = LoadPng(dir, target.BaseColorFileName, linear: false, missing);
                if (target.NormalTex == null)
                    target.NormalTex = LoadPng(dir, target.NormalFileName, linear: true, missing);
            }
            if (m_SnowBaseColorTex == null)
                m_SnowBaseColorTex = LoadPng(dir, SnowBaseColorFile, linear: false, missing);
            if (m_SnowNormalTex == null)
                m_SnowNormalTex = LoadPng(dir, SnowNormalFile, linear: true, missing);

            if (missing.Count > 0)
                Mod.Log.Warn($"Decal: {missing.Count} of 6 texture file(s) unavailable under " +
                             $"'{dir}': {string.Join(", ", missing)} — those applications are " +
                             "skipped this load (retried next load); everything else proceeds");
            else
                Mod.Log.Info($"Decal: 6 texture file(s) ready under '{dir}' (cached across loads)");
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

        // ─────────────────────────────────────────────────────────────────────────────────
        // Prefab resolution — house pattern: fast PrefabID path + name-scan fallback,
        // tolerate missing (the run surfaces are NOT YET IMPORTED on some machines; the
        // system logs its inertness once per load and sleeps until they exist).
        // ─────────────────────────────────────────────────────────────────────────────────
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
                // The batch clone is named after the MANAGED prefab object, so build the
                // match string from it, not from our constant (tolerant-by-name discipline).
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
