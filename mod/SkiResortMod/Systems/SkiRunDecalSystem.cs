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
//        * groomed-corduroy pair -> the shader's _Snow_BaseColorMap/_Snow_Normal slots
//          (fed by the game's global _SnowMap — SnowSystem.cs 277/369; GROOMED surface
//          only, round 5 — the T5 texture-swap probe, see Tuning.ApplySnowSlots),
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
//   * _Snow FLOAT untouched, always — round 5 swaps only the snow TEXTURES (groomed), which
//     is safe either way the T5 scalar question resolves: the vanilla material already
//     whitens in winter, we only change what the white is.
//   * _Wetness/_Porosity polish, _BaseColor alpha (T2b) — probe data first.
//
// SEASONAL DRESS (rounds 3-5; supersedes the original "no seasonal code" design): the
// active texture pair follows SNOW PRESENCE on the runs (injection system's measured mean
// depth, hysteresis; temperature only as pre-measurement fallback) — winter wears the
// per-run wear/corduroy set at per-type alpha (snowmaking near-clear: its winter body is
// Layer 1's injected snow; groomed keeps a visible corduroy lane), summer the shared
// faded worn-lane green. Prefab texture references are re-pointed per season too, so the
// game's own batch re-derivations stay in season (the tug-of-war fix, v2).
// ROUND E1 (2026-10-04, stream S3; econ-round spec ch3 §8.2): the depth comes from
// SkiSnowInjectionSystem.TryGetDressDepth — the managed mean (bound injected runs, as before) or,
// in a city with no managed measurement this load, the natural runs' measured mean depth; the
// fallback is the 24-hour mean temperature (SnowLineSystem.GetDayMeanTemperature), not the
// instantaneous ambient, which flipped the dress daily and read 0 °C before the first tick of a
// session (the WINTER-in-June first load). Thresholds and hysteresis are unchanged. The two
// `Decal [season]` lines name the source.
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
// ROUND M2 (2026-10-10, builder B6; measure-core D16d, docs/research/round-m2/build-contract.md §6.7): a property
// write is LOGGED only when it changes the slot (texture: not the object already bound, by UnityEngine.Object's
// != — see TrySetTextureAny; float: a different value). Every write still happens and is still recorded for the
// re-assert sentinel. The writes are counted (m_ChangedWrites / m_SameWrites): the activation summary ends with
// "; {changed} change(s), {same} already in place" for the load application, and a season re-dress writes
// `Decal [season]: re-dress wrote {changed} change(s), {same} already in place` after its loop (field test 10's
// re-dress logged 17 property lines, 11 of them "1 -> 1" or the same texture, L 954-970).
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

            // T5 PROBE, RE-ENABLED ROUND 5 (was FIELD-DISABLED 2026-08-12): the first
            // enable bound textures into what we believed were EMPTY snow slots and the
            // layer locked permanently ON — solid white immune to 50 °C and remove-snow
            // (two calibration rounds were judged against that artifact). Ledger 62 then
            // showed vanilla itself ships these slots BOUND (snowalbedo2 defaults) with
            // _Snow = 0 and no artifact — so texture PRESENCE is not the driver, and
            // SWAPPING the already-bound textures while touching neither the _Snow float
            // nor any keyword is the vanilla-parity operation. Re-enabled for the GROOMED
            // surface only (Target.UseSnowPair): corduroy renders in the snow layer
            // itself — the run's deep-winter identity, which depth cannot provide (white
            // saturates at cm scale; field round 4: "impossible to tell it exists").
            // FIELD DISCRIMINATOR: if groomed runs go solid white and ignore 50 °C /
            // remove-snow again, THIS flag is the culprit — flip false and report.
            public static readonly bool ApplySnowSlots = true;

            // Seasonal dress (2026-08-12 user round: "trails look absurd in summer"):
            // summer wears the green mowed-grass set, winter the wear/corduroy set.
            // Field round 4: the dress is driven by SNOW PRESENCE on the runs (injection
            // system's measured mean depth) — temperature flipped the trails green while
            // the world was still white ("green trails in white snow"). Temperature
            // hysteresis remains only as the fallback when no measurement exists yet.
            public const int SeasonCheckFrames = 1024;
            public const float WinterLookOnC = 2f;    // fallback only
            public const float WinterLookOffC = 3f;   // fallback only
            public const float DressSnowyDepthM = 0.02f;  // runs this snowy -> winter dress
            public const float DressBareDepthM = 0.005f;  // runs this bare -> summer dress

            // Field round 4 ("painted a golf course on the mountains"): the summer green
            // ships as an opaque terrain texture; as a DECAL it must be a faint worn lane,
            // not paint. Alpha and RGB are scaled in-place at load. Round 5: 0.40 still
            // read as paint with a vector edge — the tree-cleared corridor carries the
            // run's summer identity on its own (user timelapse), the decal only whispers.
            public const float SummerAlphaScale = 0.20f;
            public const float SummerRgbScale = 0.88f;

            // Round 5 winter dress, PER RUN TYPE — the two runs had opposite failures:
            // snowmaking's decal edge fought its own injected snow ("vector crisp"), so
            // its winter body is now Layer 1's snow alone (soft feathered edges through
            // the real terrain pipeline) and the decal fades to a whisper; groomed was
            // "impossible to tell it exists", so it KEEPS a visible corduroy lane — and
            // a machine-cut edge is what grooming actually looks like. RGB untouched;
            // alpha scaled in-place at load, same mechanism as the summer set.
            // T23 (Ash Flat item 91, 2026-10-16): Richard read the groomed run as grey next to the whiter snowmaking
            // run. The groomed decal's mean RGB is 153/149/143 (beige) against snowmaking's 186/190/198, so at 0.55
            // the beige showed through the snow; 0.35 keeps the corduroy lane readable while the pack's white wins.
            // The prefab tint (SkiRunGroomedA m_BaseColor) was raised too; if still grey, the bake (ski_decal.py
            // build_groomed `beige`) is the next knob (build/T17-T23-T25-dressing.md).
            public const float GroomedWinterAlphaScale = 0.35f;
            public const float SnowmakingWinterAlphaScale = 0.10f;
        }

        // Run surfaces — same tolerant name resolution as the sibling systems.
        // E1 (D16): SkiRunNaturalA is left out on purpose — a natural run draws nothing (its
        // RenderedArea is invisible, e1-spec DV20), so there is no decal to dress; its measured
        // snow may still drive the dress (SkiSnowInjectionSystem.TryGetDressDepth, ch3 §8).
        private const string GroomedPrefabName = "SkiRunGroomedA";
        private const string SnowmakingPrefabName = "SkiRunSnowmakingA";
        private const string NightPrefabName = "SkiRunNightA";
        // T54 probe 1 (cat-track-run.md §2): the cat track wears the GROOMED pair (textures, winter alpha, snow slots) until it
        // gets its own pair and summer set; its prefab has RendererPriority 11 so its decal draws over a run it crosses.
        private const string CatTrackPrefabName = "SkiRunCatTrackA";

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
        // Shared summer dress (both run types): green mowed-grass stripes — the same set
        // the splat route uses, so the two rendering paths agree on what summer looks like.
        private const string SummerBaseColorFile = "SkiTerrainSummerRun_BaseColor.png";
        private const string SummerNormalFile = "SkiTerrainSummerRun_Normal.png";

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
            // Round 5 per-type dress: winter decal alpha (scaled once at load) and
            // whether this surface binds the corduroy pair into the shader's snow slots.
            public readonly float WinterAlphaScale;
            public readonly bool UseSnowPair;

            // Session-cached (prefabs and loaded textures outlive individual loads).
            public bool Resolved;
            public string BatchMaterialName;   // "Area batch (<resolved prefab name>)"
            public RenderedArea Rendered;      // managed prefab component (scalar mirror)
            public Texture2D BaseColorTex;
            public Texture2D NormalTex;
            // v2 (the tug-of-war fix): the same textures wrapped as user-db TextureAssets,
            // written into the PREFAB's texture references so the game's own batch
            // re-derivation propagates OUR textures instead of re-binding the originals
            // (field-proven 2026-08-12: the game re-bound the Editor whites twice in
            // 2.5 min; the live-material swap perpetually lost the race).
            public TextureAsset BaseColorAsset;
            public TextureAsset NormalAsset;
            public AssetReference<TextureAsset> OrigBaseColorRef;
            public AssetReference<TextureAsset> OrigNormalRef;
            public bool OrigRefsCaptured;

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

        // Session-cached.
        private bool m_PrefabsResolved;
        private Texture2D m_SnowBaseColorTex;
        private Texture2D m_SnowNormalTex;
        private Texture2D m_SummerBaseColorTex;
        private Texture2D m_SummerNormalTex;
        private TextureAsset m_SummerBaseColorAsset;
        private TextureAsset m_SummerNormalAsset;
        private SnowLineSystem m_SnowLineSystem;
        private SkiSnowInjectionSystem m_SnowInjectionSystem;

        // Seasonal look state (per-load evaluated, hysteresis in EvaluateWinterLook).
        private bool m_WinterLook;
        private int m_FramesUntilSeasonCheck;
        // E1 ch3 §8.2: what the last EvaluateWinterLook read — for the two `Decal [season]` lines only
        // (no string is built in the check itself).
        private RunDressSource m_DressSource;
        private float m_DressValue;

        // Per-load state.
        private bool m_GameMode;                // prefab-ref patching is Game-mode-only
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
        // Round M2 D16d: property writes since the last count line (reset at load, before and after a season
        // re-dress, and after the activation summary). A re-assert's writes are not reported by count (its own
        // [reassert] line and the changed writes say what happened); the next season re-dress starts from zero.
        private int m_ChangedWrites;
        private int m_SameWrites;

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
                new Target(SnowmakingPrefabName, SnowmakingBaseColorFile, SnowmakingNormalFile,
                    Tuning.SnowmakingWinterAlphaScale, useSnowPair: false),
                // Night runs wear snowmaking's dress — same textures, same near-clear
                // winter alpha. A night run's snow IS snowmaking snow (§4.1); the run's
                // identity comes from its floodlight masts, not a different decal.
                new Target(NightPrefabName, SnowmakingBaseColorFile, SnowmakingNormalFile,
                    Tuning.SnowmakingWinterAlphaScale, useSnowPair: false),
                // T54 probe 1: the cat track reuses the groomed pair (the brief: "reuse the groomed pair for now").
                new Target(CatTrackPrefabName, GroomedBaseColorFile, GroomedNormalFile,
                    Tuning.GroomedWinterAlphaScale, useSnowPair: true),
            };
            Mod.Log.Info($"{nameof(SkiRunDecalSystem)} created");
        }

        /// <summary>Editor safety (v2): the prefab texture-reference patch must never be
        /// captured by an editor save of the surface asset — restore the JSON-original
        /// references before any editor session (same anti-contamination doctrine as
        /// SkiRunSplatSystem's claim reversion).</summary>
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

            // Re-arm and reset per load, whatever the mode — the batch clones this system
            // mutated last load are gone (prefab Deleted/Created rebuilds them per load).
            m_Active = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_SummaryLogged = false;
            m_PropertyWarnLogged = false;
            m_PendingCount = 0;
            m_AppliedCount = 0;
            m_ChangedWrites = 0;              // Round M2 D16d: the load application's count starts here
            m_SameWrites = 0;
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

                // Seasonal dress check — cheap, hysteresis-guarded; on a flip every
                // applied material gets re-dressed with the season-active pair (prefab
                // references included, so the game's own re-binds stay in season too).
                if (m_AppliedCount > 0 && --m_FramesUntilSeasonCheck <= 0)
                {
                    m_FramesUntilSeasonCheck = Tuning.SeasonCheckFrames;
                    bool winter = EvaluateWinterLook();
                    if (winter != m_WinterLook)
                    {
                        m_WinterLook = winter;
                        Mod.Log.Info($"Decal [season]: {(winter ? "-> WINTER dress (wear/corduroy set)" : "-> SUMMER dress (green mowed-grass set)")} — re-dressing {m_AppliedCount} material(s)" +
                                     DressSourceText());
                        m_ChangedWrites = 0;      // Round M2 D16d: count this re-dress only
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
        /// path calls it again verbatim; every Set that changes the slot is logged with old -> new
        /// (Round M2 D16d: one already in place is only counted).</summary>
        private void ApplyToMaterial(Target target)
        {
            Material material = target.Material;
            Dictionary<string, ShaderPropertyType> props = CollectShaderProperties(material.shader);
            target.AppliedTextures.Clear();
            target.AppliedFloats.Clear();

            // 1) Our decal texture pair — live material for immediacy, AND (v2, the
            //    tug-of-war fix) the PREFAB's texture references via runtime TextureAssets:
            //    AreaBatchSystem re-derives batch material state from the prefab component,
            //    so a live-only swap perpetually loses to the game's re-binds (field log
            //    2026-08-12: original whites re-bound twice in 2.5 min). With the prefab
            //    references patched, the game's own re-binds propagate OUR textures; the
            //    re-assert sentinel from here on is pure verification. Game mode only —
            //    editor saves must never capture runtime user-db references (OnGamePreload
            //    restores the originals before any editor session).
            // Season-active pair (2026-08-12 seasonal dress): winter = the per-run
            // wear/corduroy set, summer = the shared green mowed-grass set (falls back to
            // the winter pair if the summer files are missing).
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

            // 2) Snow slots — T5 probe live for the GROOMED surface only (round 5): swap
            //    the vanilla-bound snowalbedo2 pair for our corduroy, touching neither the
            //    _Snow float nor any keyword (the vanilla-parity operation — see the
            //    Tuning.ApplySnowSlots note and its field discriminator). Live-material
            //    only (RenderedArea exposes no snow refs); the re-assert sentinel restores
            //    it after the game's re-derivations.
            bool snowBase = false, snowNormal = false;
            if (Tuning.ApplySnowSlots && target.UseSnowPair)
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
                         $"re-assert sentinel every {Tuning.ReassertCheckFrames} frames" +
                         // Round M2 D16d (contract §6.7): the load application's writes, changed and already in place.
                         $"; {m_ChangedWrites} change(s), {m_SameWrites} already in place");
            m_ChangedWrites = 0;
            m_SameWrites = 0;
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
            // Round M2 D16d: log only a change, by UnityEngine.Object's != (measure-core D16d's test): the same live
            // texture reads as equal, a destroyed old one as null, so it counts as a change — Unity's documented
            // equality, UNVERIFIED here (no UnityEngine decompile in tools/; DetectDrift's ReferenceEquals is the
            // stricter twin, and a stricter miss would only log one extra line). The write and the sentinel record
            // above happen either way.
            if (old != texture)
            {
                m_ChangedWrites++;
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
            // Round M2 D16d: log only a change (float.Equals: the exact value, NaN equal to NaN). The write, the
            // sentinel record and the caller's RenderedArea mirror happen either way.
            if (!old.Equals(value))
            {
                m_ChangedWrites++;
                Mod.Log.Info($"Decal: '{material.name}': {propertyName} {old:R} -> {value:R}" +
                             (mirrored ? " (+ prefab RenderedArea mirror)" : ""));
            }
            else
            {
                m_SameWrites++;
            }
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
                {
                    target.BaseColorTex = LoadPng(dir, target.BaseColorFileName, linear: false, missing);
                    // Round 5 per-type winter fade — once, before asset registration pins
                    // the texture (same ordering rule as the summer set below).
                    if (target.BaseColorTex != null && target.WinterAlphaScale < 1f)
                        ScaleColors(target.BaseColorTex, 1f, target.WinterAlphaScale);
                    // Night round: two targets may LOAD THE SAME FILE (night wears
                    // snowmaking's dress). RegisterTextureAsset keys the user-db entry on
                    // texture.name, which comes from the FILENAME — so without a
                    // per-target prefix the second target's asset would resolve to the
                    // FIRST target's texture instance, and the drift sentinel would
                    // re-apply forever (its own instance vs the shared asset's). Names
                    // are runtime-only, never saved.
                    if (target.BaseColorTex != null)
                        target.BaseColorTex.name = target.PrefabName + target.BaseColorTex.name;
                }
                if (target.NormalTex == null)
                {
                    target.NormalTex = LoadPng(dir, target.NormalFileName, linear: true, missing);
                    if (target.NormalTex != null)
                        target.NormalTex.name = target.PrefabName + target.NormalTex.name;
                }

                // v2: wrap the same textures as user-db TextureAssets (in-memory index
                // entry only, never Saved — the SkiRunSplatSystem claim pattern) so the
                // PREFAB's texture references can point at them (ApplyToMaterial).
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
                // Round 4: fade the opaque terrain green into a see-through worn lane
                // BEFORE the asset registration pins the texture.
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
                             $"'{dir}': {string.Join(", ", missing)} — those applications are " +
                             "skipped this load (retried next load); everything else proceeds");
            else
                Mod.Log.Info($"Decal: 8 texture file(s) ready under '{dir}' (cached across loads)");
        }

        /// <summary>Winter-look decision (round 4): primary driver is MEASURED snow on the
        /// runs (injection system's per-pass mean over run footprints, hysteresis 0.5-2 cm)
        /// so green can never show while trails are white. Temperature hysteresis is the
        /// fallback for loads where no measurement has happened yet.
        /// E1 ch3 §8.2: the measured depth is TryGetDressDepth's (managed, else the natural runs'
        /// measured mean); the fallback is the 24-hour mean (was the instantaneous ambient, which flips
        /// daily). Records what it read for the log lines only.</summary>
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
            // E1 ch3 §8: the fallback is the 24-hour mean (was the instantaneous ambient, which flips daily).
            float dayMean = m_SnowLineSystem.GetDayMeanTemperature();
            float freezing = m_SnowLineSystem.GetFreezingTemperature();
            m_DressSource = RunDressSource.None;
            m_DressValue = dayMean;
            return m_WinterLook
                ? dayMean <= freezing + Tuning.WinterLookOffC
                : dayMean <= freezing + Tuning.WinterLookOnC;
        }

        /// <summary>ch3 §10 L-V1: the suffix of the two `Decal [season]` lines — what the last
        /// EvaluateWinterLook read.</summary>
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

        /// <summary>In-place RGB/alpha scale (round 4, summer set only). LoadImage textures
        /// stay CPU-readable; Apply(true) regenerates mips, readable is kept so a future
        /// pass could re-scale.</summary>
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

        /// <summary>User-db TextureAsset wrapping an already-loaded texture (SkiRunSplatSystem
        /// pattern): AddAsset creates an in-memory index entry only, SetData-pinned by the
        /// AddAsset overload, nothing ever written to disk. Reuse-safe across loads.</summary>
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
