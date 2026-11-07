// Puts the SkiRunTreeMember tag on the trees the glade tool plants, on the Apply frame, before vanilla turns the Temp trees into
// permanent ones.
// Registered UpdateBefore<SkiGladeTagSystem, Game.Tools.ApplyObjectsSystem> in ApplyTool. A component added to a Temp tree here
// survives into the permanent tree (ApplyObjectsSystem.cs:670-687, Create keeps the entity).
// Guard: all three conditions hold.
// - One of our planting tools (IGladeTagSource: the glade tool, the Quick runs tool's tree filler) is activeTool.
// - Both ToolSystem and the tool report ApplyMode.Apply.
// - The source says this is its planting Apply frame.
// The system forces Decoration on planted trees.
// Defensive: Game mode only. A one-shot catch disables tagging until the next load. No managed allocation outside Apply frames.
// Engine notes: EngineNotes/SkiGladeTagSystem.md

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    /// <summary>A tool whose Apply frame plants trees this system tags (the second source of DECISIONS 415, so there is one tag system).
    /// The glade tool: ApplyingNow = ApplyingRun != Null, a glade or belt layer. The Quick runs tool's filler: run Null, LayerFiller.</summary>
    internal interface IGladeTagSource
    {
        /// <summary>This is the source's planting Apply frame (read in the ApplyTool phase of that frame).</summary>
        bool ApplyingNow { get; }
        /// <summary>The run the planted trees belong to. Entity.Null only for LayerFiller (region-level tags).</summary>
        Entity ApplyingRun { get; }
        byte ApplyingLayer { get; }
        byte ApplyingVariant { get; }
        /// <summary>The create-path Temps of this Apply, after tagging (the filler records them for its undo).</summary>
        void OnTagged(NativeList<Entity> created);
    }

    public sealed partial class SkiGladeTagSystem : GameSystemBase
    {
        // Compile-time contract (engine notes): division by constant zero (CS0020) if the persisted
        // tag's constants and the pure composition's constants ever drift apart.
        private const int LayerContract = 1 / ((SkiRunTreeMember.LayerGlade == SkiGladeCompose.LayerGlade &&
                                                SkiRunTreeMember.LayerBelt == SkiGladeCompose.LayerBelt &&
                                                SkiRunTreeMember.LayerFiller == SkiGladeCompose.LayerFiller &&
                                                SkiRunTreeMember.VariantUnknown == SkiGladeCompose.VariantUnknown) ? 1 : 0);

        private ToolSystem m_ToolSystem;
        private SkiGladeToolSystem m_Tool;
        private QuickRunsToolSystem m_QuickRuns;   // the second source (tree filler)
        private PrefabSystem m_PrefabSystem;

        private EntityQuery m_TempTreeQuery;

        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_DeadRunWarned;
        private bool m_BadLayerWarned;
        private bool m_UnknownVariantWarned;

        public string LastCensus = "no apply yet";

        public int LastTagged;

        // The last apply's ApplyObjectsSystem classes (engine notes "Which Temps"), published for the tool's apply check. Valid when
        // LastApplyRun / LastApplyFrame match.
        public Entity LastApplyRun;
        public int LastApplyFrame = -1;            // UnityEngine.Time.frameCount of that Apply frame
        public int LastCreatePath, LastCancelled, LastDeleteTemps, LastUpdatePath, LastAlreadyDeleted;

        private struct Classes
        {
            public int Create, Cancelled, Delete, Update, AlreadyDeleted;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_Tool = World.GetOrCreateSystemManaged<SkiGladeToolSystem>();
            m_QuickRuns = World.GetOrCreateSystemManaged<QuickRunsToolSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            // No Deleted filter (engine notes "Which Temps"): ApplyObjectsSystem's query has none either.
            m_TempTreeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Game.Objects.Tree>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
            });

            Mod.Log.Info($"{nameof(SkiGladeTagSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_DeadRunWarned = false;
            m_BadLayerWarned = false;
            m_UnknownVariantWarned = false;
            LastCensus = "no apply yet";
            LastTagged = 0;
            LastApplyRun = Entity.Null;
            LastApplyFrame = -1;
            LastCreatePath = 0;
            LastCancelled = 0;
            LastDeleteTemps = 0;
            LastUpdatePath = 0;
            LastAlreadyDeleted = 0;

            // HARD GATE: Game only — the glade tool is a Game-mode tool.
            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            Mod.Log.Info("Glade: tag ARMED — on the glade tool's Apply frame (ApplyTool phase, " +
                         "before ApplyObjectsSystem) every Temp tree on ApplyObjectsSystem's CREATE path " +
                         "(its own order: Cancel → not created, Delete → original deleted, original with " +
                         "PrefabRef → update, else create; ApplyObjectsSystem.cs:337-432; Round G3) gets " +
                         "SkiRunTreeMember(run, tool.ApplyingLayer, tool.ApplyingVariant = " +
                         SkiGladeToolSystem.NewPlantingVariant + ", PlanGeneration) — layout v" + SkiRunTreeMember.CurrentVersion +
                         " (version | run | layer | variant | planGen, 8 bytes; v1 tags load as variant " +
                         SkiRunTreeMember.VariantUnknown + " / gen 0), PlanGeneration=" +
                         SkiGladeCompose.PlanGeneration + ", LayerGlade=" + SkiRunTreeMember.LayerGlade +
                         ", LayerBelt=" + SkiRunTreeMember.LayerBelt + ", contract check " + LayerContract +
                         "; a plan whose layer is neither tags nothing (WARN once); " +
                         "ApplyObjectsSystem.Create keeps the entity, so the tag persists (serialized; " +
                         "uninstall-safe; a pre-G2 build cannot read v2 tags); per-apply class counts " +
                         "published for the tool's apply check; created ≠ tagged is a WARN. Second source: the Quick runs " +
                         "tree filler (run Null, LayerFiller=" + SkiRunTreeMember.LayerFiller + ", region-level tags)");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;

                // Three-way guard (engine notes):
                // 1. One of our planting tools is the active tool.
                // 2. The frame is an Apply frame for ToolSystem AND for that tool.
                // 3. The tool says it is planting (the glade tool: it names a run).
                ToolBaseSystem active = m_ToolSystem.activeTool;
                IGladeTagSource source = active == m_Tool ? m_Tool : active == m_QuickRuns ? m_QuickRuns : (IGladeTagSource)null;
                if (source == null ||
                    m_ToolSystem.applyMode != ApplyMode.Apply ||
                    active.applyMode != ApplyMode.Apply)
                    return;
                if (!source.ApplyingNow)
                    return;
                Entity run = source.ApplyingRun;
                bool glade = source == m_Tool;

                // One classification pass in ApplyObjectsSystem's order (engine notes "Which Temps"), published whatever happens next.
                var create = new NativeList<Entity>(64, Allocator.Temp);
                try
                {
                    Classes k = Classify(create);
                    LastApplyRun = run;
                    LastApplyFrame = UnityEngine.Time.frameCount;
                    LastCreatePath = create.Length;
                    LastCancelled = k.Cancelled;
                    LastDeleteTemps = k.Delete;
                    LastUpdatePath = k.Update;
                    LastAlreadyDeleted = k.AlreadyDeleted;
                    LastTagged = 0;

                    if (run != Entity.Null && (!EntityManager.Exists(run) || EntityManager.HasComponent<Deleted>(run)))
                    {
                        // Defensive: the run vanished between hover and apply. Plant untagged
                        // rather than write a dangling reference (cleanup would strip it anyway).
                        // Still force Decoration on (engine notes: reused Temps keep a disabled bit).
                        ForceDecoration(create);
                        LastCensus = $"apply skipped tagging — run {run.Index}:{run.Version} is gone; " +
                                     $"{create.Length} created tree(s) stay untagged";
                        if (!m_DeadRunWarned)
                        {
                            m_DeadRunWarned = true;
                            Mod.Log.Warn($"Glade: {LastCensus} (warned once per load)");
                        }
                        else
                        {
                            Mod.Log.Info($"Glade: {LastCensus}");
                        }
                        return;
                    }

                    // Per-plan values (valid while ApplyingRun != Null).
                    byte layer = source.ApplyingLayer;
                    byte variant = source.ApplyingVariant;
                    // Glade / belt layers need a run. The filler layer is region-level (run Null) and nothing else.
                    bool layerOk = layer == SkiRunTreeMember.LayerFiller
                        ? run == Entity.Null
                        : run != Entity.Null && (layer == SkiRunTreeMember.LayerGlade || layer == SkiRunTreeMember.LayerBelt);
                    if (!layerOk)
                    {
                        // Every plan has a layer. Planted Temps under any other value are unexpected. Never persist a bogus layer (engine notes): plain trees
                        // instead.
                        ForceDecoration(create);
                        if (create.Length == 0)
                        {
                            LastCensus = $"nothing to tag for run {RunLabel(run)} (plan layer {layer}; " +
                                         $"{k.Delete} delete Temp(s), {k.Cancelled} canceled)";
                            Mod.Log.Info($"Glade: {LastCensus}");
                        }
                        else
                        {
                            LastCensus = $"apply on run {RunLabel(run)} carried layer {layer} — " +
                                         $"{create.Length} created tree(s) left untagged (plain trees)";
                            if (!m_BadLayerWarned)
                            {
                                m_BadLayerWarned = true;
                                Mod.Log.Warn($"Glade: {LastCensus} (WARN once per load; later ones INFO)");
                            }
                            else
                            {
                                Mod.Log.Info($"Glade: {LastCensus}");
                            }
                        }
                        return;
                    }
                    if (variant == SkiRunTreeMember.VariantUnknown && !m_UnknownVariantWarned)
                    {
                        m_UnknownVariantWarned = true;
                        Mod.Log.Warn($"Glade: apply on run {RunLabel(run)} reports variant " +
                                     $"{variant} (VariantUnknown) — tags written as given; the next " +
                                     "apply of this layer replaces them (warned once per load)");
                    }

                    TagApplyingTrees(run, layer, variant, create, k, glade);
                    source.OnTagged(create);
                }
                finally
                {
                    create.Dispose();
                }
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiGladeTagSystem)} EXCEPTION — glade tree " +
                                  $"tagging disabled until the next game load:\n{e}");
                }
            }
        }

        /// <summary>One pass over every Temp tree in ApplyObjectsSystem.HandleTempEntitiesJob's order (ApplyObjectsSystem.cs:337-432):
        /// Cancel → not created. Delete → original deleted. Original WITH PrefabRef → Update path (:354). Else → Create (:432).
        /// The pass collects the create-path entities (all chunks). It counts Deleted ones apart: ApplyObjectsSystem's query has no
        /// Deleted filter (:922), but CleanUp destroys them. The pass only reads. The caller mutates afterwards (structural changes
        /// invalidate chunks).</summary>
        private Classes Classify(NativeList<Entity> create)
        {
            Classes k = default;
            if (m_TempTreeQuery.IsEmptyIgnoreFilter)
                return k;
            using (var chunks = m_TempTreeQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                var entityHandle = GetEntityTypeHandle();
                var tempHandle = GetComponentTypeHandle<Temp>(isReadOnly: true);
                var deletedHandle = GetComponentTypeHandle<Deleted>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var temps = chunks[c].GetNativeArray(ref tempHandle);
                    bool chunkDeleted = chunks[c].Has(ref deletedHandle);
                    for (int i = 0; i < temps.Length; i++)
                    {
                        Temp temp = temps[i];
                        if ((temp.m_Flags & TempFlags.Cancel) != 0)
                        {
                            k.Cancelled++;            // :337-340 → Cancel(): Deleted, never created
                            continue;
                        }
                        if ((temp.m_Flags & TempFlags.Delete) != 0)
                        {
                            k.Delete++;               // :342-352 → the original is deleted
                            continue;
                        }
                        if (temp.m_Original != Entity.Null && EntityManager.Exists(temp.m_Original) &&
                            EntityManager.HasComponent<PrefabRef>(temp.m_Original))
                        {
                            k.Update++;               // :354-413 → changes the original, Temp deleted
                            continue;
                        }
                        if (chunkDeleted)
                        {
                            k.AlreadyDeleted++;       // created then destroyed by CleanUp this frame
                            continue;
                        }
                        k.Create++;
                        create.Add(entities[i]);      // :414-432 → Create keeps THIS entity (:670-687)
                    }
                }
            }
            return k;
        }

        private void TagApplyingTrees(Entity run, byte layer, byte variant, NativeList<Entity> create, Classes k, bool glade)
        {
            int retagged = 0;
            var toAdd = new NativeList<Entity>(create.Length > 0 ? create.Length : 1, Allocator.Temp);
            var toSet = new NativeList<Entity>(4, Allocator.Temp);
            try
            {
                for (int i = 0; i < create.Length; i++)
                {
                    Entity e = create[i];
                    // A reused Temp entity could in principle already carry the tag.
                    // FillOldObjectsJob collects old Temps for reuse (Game.Tools/GenerateObjectsSystem.cs:89-140).
                    // Unverified that a tagged Temp can ever survive to here.
                    // The code uses set instead of add, so the add batch never meets a duplicate.
                    if (EntityManager.HasComponent<SkiRunTreeMember>(e))
                    {
                        toSet.Add(e);
                        retagged++;
                    }
                    else
                    {
                        toAdd.Add(e);
                    }
                }

                // One structural change for the whole batch (vanilla precedent for the
                // array overload: Game.Debug/DebugSystem.cs:2900 [2908]), then plain sets.
                // One value for the whole apply: every plan is single-layer (engine notes).
                var tag = new SkiRunTreeMember(run, layer, variant, SkiGladeCompose.PlanGeneration);
                if (toAdd.Length > 0)
                    EntityManager.AddComponent<SkiRunTreeMember>(toAdd.AsArray());
                for (int i = 0; i < toAdd.Length; i++)
                    EntityManager.SetComponentData(toAdd[i], tag);
                for (int i = 0; i < toSet.Length; i++)
                    EntityManager.SetComponentData(toSet[i], tag);

                // Decoration state on the permanent tree whatever Temp entity was reused (engine notes: GenerateObjectsSystem.cs:1311-1317 applies
                // m_IsDecoration only on the new-entity path. The reuse path :1112-1219 never touches the bit).
                int decorationFixed = 0;
                for (int i = 0; i < toAdd.Length; i++)
                    decorationFixed += EnsureDecoration(toAdd[i]);
                for (int i = 0; i < toSet.Length; i++)
                    decorationFixed += EnsureDecoration(toSet[i]);

                // Count what actually carries the tag now (not what was asked): the line below WARNs when it differs from the create path.
                int tagged = 0;
                for (int i = 0; i < create.Length; i++)
                {
                    if (EntityManager.HasComponent<SkiRunTreeMember>(create[i]))
                        tagged++;
                }
                LastTagged = tagged;
                byte level = glade ? m_Tool.ApplyingLevel : m_QuickRuns.ApplyingLevel;
                LastCensus = (glade
                                 ? $"tagged {tagged} trees for run {RunLabel(run)} — " +
                                   (m_Tool.ApplyingTreeLine ? "Tree line" : "Interior")
                                 : $"tagged {tagged} filler trees (Quick runs region, run Null) — Fill") +
                             $" L{level} ({SkiGladeToolSystem.LevelName(level)}), " +
                             (layer == SkiRunTreeMember.LayerBelt ? "belt" : layer == SkiRunTreeMember.LayerFiller ? "filler" : "glade") +
                             $" layer, variant {variant}, gen {SkiGladeCompose.PlanGeneration}, layout " +
                             $"v{SkiRunTreeMember.CurrentVersion}; ApplyObjectsSystem create path {create.Length}, " +
                             $"canceled {k.Cancelled} (not created), delete {k.Delete}, update {k.Update}, " +
                             $"already deleted {k.AlreadyDeleted}" +
                             (retagged > 0 ? $"; {retagged} re-tagged" : "") +
                             (decorationFixed > 0 ? $"; Decoration re-enabled on {decorationFixed} reused Temp(s)" : "");
                if (tagged == create.Length)
                {
                    Mod.Log.Info($"Glade: {LastCensus}");
                }
                else
                {
                    Mod.Log.Warn($"Glade: {LastCensus} — CREATED ≠ TAGGED: {create.Length - tagged} tree(s) " +
                                 "will be created without the member tag");
                }
            }
            finally
            {
                toAdd.Dispose();
                toSet.Dispose();
            }
        }

        /// <summary>Sets Decoration enabled on a planted Temp tree. Returns 1 if the bit was
        /// off (a reused foreign Temp), else 0. HasComponent ignores the enabled state, so a
        /// disabled bit still passes the guard (Decoration is IEnableableComponent,
        /// Game.Objects/Decoration.cs:8).</summary>
        private int EnsureDecoration(Entity e)
        {
            if (!EntityManager.HasComponent<Game.Objects.Decoration>(e))
                return 0;
            if (EntityManager.IsComponentEnabled<Game.Objects.Decoration>(e))
                return 0;
            EntityManager.SetComponentEnabled<Game.Objects.Decoration>(e, true);
            return 1;
        }

        /// <summary>Dead-run and bad-layer branches: the create-path Temps (Classify) get no tag —
        /// only the Decoration bit.</summary>
        private void ForceDecoration(NativeList<Entity> create)
        {
            for (int i = 0; i < create.Length; i++)
                EnsureDecoration(create[i]);
        }

        private string RunLabel(Entity run)
        {
            string name = "?";
            if (EntityManager.HasComponent<PrefabRef>(run))
                name = m_PrefabSystem.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(run).m_Prefab);
            return $"{name} {run.Index}:{run.Version}";
        }
    }
}
