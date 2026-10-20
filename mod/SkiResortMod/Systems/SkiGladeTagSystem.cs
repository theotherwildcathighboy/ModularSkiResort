// SkiGladeTagSystem — puts the SkiRunTreeMember tag on the trees the glade tool plants, on
// the Apply frame, BEFORE vanilla turns the Temp trees into permanent ones
// (docs/research/glade-tool-spec.md §6; DECISIONS 109/110).
//
// MECHANISM (1.6.0f1 decompile; 1.6.2f1 line in brackets where it moved):
//   * Frame order: ToolSystem runs in MainLoop (Game.Common/SystemOrder.cs:58) and does
//     PreTool -> ToolUpdate -> PostTool (Game.Tools/ToolSystem.cs:251-258, :327). Inside
//     ToolUpdate, after the active tool's OnUpdate, ToolOutputSystem runs the ApplyTool
//     phase ONLY when ToolSystem.applyMode == Apply (Game.Tools/ToolOutputSystem.cs:20-31;
//     registered UpdateAfter in ToolUpdate, SystemOrder.cs:694 [695]), and
//     ToolSystem.applyMode is the LAST-UPDATED tool's applyMode (ToolSystem.cs:147-157).
//     So this system ticks on exactly the frame SkiGladeToolSystem set applyMode = Apply,
//     and on Apply frames of every OTHER tool too — hence the three-way guard below.
//   * Registration (integrator, Mod.cs): UpdateBefore<SkiGladeTagSystem,
//     Game.Tools.ApplyObjectsSystem>(SystemUpdatePhase.ApplyTool) — ApplyObjectsSystem is
//     at ApplyTool (SystemOrder.cs:714 [715]). Our EntityManager change is immediate (main
//     thread) and lands before ApplyObjectsSystem schedules its job over Temp+Object
//     (ApplyObjectsSystem.cs:922) — its command buffer is the ToolOutputBarrier (:1001),
//     played back after ToolOutputSystem (SystemOrder.cs:695 [696]).
//   * Why the tag survives Apply: for a Temp with m_Original == Null and no Cancel/Delete
//     flag, ApplyObjectsSystem takes the create path (:414-432) and Create (:670-687) KEEPS
//     THE SAME ENTITY — it removes Temp/Animation/BackSide (:672, set :925), adds
//     Applied/Created/Updated (:686, set :924) and Recent when m_Cost > 0 (:677-685).
//     Any component we add to the Temp tree is therefore on the permanent tree.
//     (ApplyObjectsSystem.cs: dispatch :337, Create :670 and the call :432 are unchanged
//     in 1.6.2f1 — checked.)
//   * Which Temps — Round G3 (run-binding-glade-spec.md §8.1 defect; the full diagnosis is in
//     the SkiGladeToolSystem header): EXACTLY the Temps ApplyObjectsSystem will CREATE, decided
//     in its own order (HandleTempEntitiesJob, ApplyObjectsSystem.cs:333-438): TempFlags.Cancel
//     first → Cancel() adds Deleted, nothing is created (:337-340, :474-482); then Delete →
//     the original is deleted (:342-352, :484-504); then an original that HAS PrefabRef → the
//     Update path changes the original and deletes the Temp (:354-413); everything else →
//     Create keeps the Temp entity (:414-432, :670-687). Query All{Temp, Game.Objects.Tree,
//     PrefabRef} (Tree from the TreeObject archetype, Game.Prefabs/TreeObject.cs:27) WITHOUT a
//     Deleted filter, because ApplyObjectsSystem's own query (Temp + Object, :922) has none; a
//     Temp that already carries Deleted is counted ("already deleted") but not tagged — CleanUp
//     destroys it at frame end (Game.Common/CleanUpSystem.cs:48-52) and SearchSystem never adds a
//     Deleted chunk (SearchSystem.cs:90-95). G2 tested only "m_Original == Null and no
//     Cancel/Delete"; the Update-path test (original WITH PrefabRef) makes the create set exact
//     for a stale original too (none of our definitions produces one). While our tool is the
//     active tool it is the only producer of object definitions, so every create-path Temp tree
//     is one of our planted trees. The counts of every class are published (LastCreatePath,
//     LastTagged, LastCancelled, LastDeleteTemps, LastUpdatePath, LastAlreadyDeleted with
//     LastApplyRun / LastApplyFrame) and the tool's APPLY CHECK compares them with the census
//     taken on the next frame.
//   * Guard: our tool is ToolSystem.activeTool (ToolSystem.cs:84-98), both ToolSystem and
//     our tool report ApplyMode.Apply, and tool.ApplyingRun != Entity.Null (spec §2: non-
//     Null only on the frame the tool applies). The activeTool/applyMode checks stop a
//     stale ApplyingRun (tool switched away right after an Apply) from tagging a vanilla
//     tool's trees.
//   * Decoration forced ON (spec §4.4: planted trees never age and count no wood).
//     ObjectDefinition.m_IsDecoration = true only matters on the NEW-entity path: there
//     GenerateObjectsSystem disables Decoration iff !m_IsDecoration
//     (Game.Tools/GenerateObjectsSystem.cs:1311-1317 [1.6.2f1 :1314-1316]). The REUSE path
//     (oldEntity != Null, :1112-1219, same line in 1.6.2f1) re-sets Transform/Temp/Elevation/
//     Tree state but never touches the Decoration bit, and old Temps are matched by
//     prefab/original/owner only (:56-73, :89-140) — so a vanilla object-tool ghost of the
//     same species (decorationMode defaults false, ObjectToolSystem.cs:2651) can be reused
//     for one of our trees with Decoration DISABLED. We therefore set the bit on every
//     planted Temp here; ApplyObjectsSystem.Create keeps the entity (:670-687), so the bit
//     carries into the permanent tree. Decoration is IEnableableComponent
//     (Game.Objects/Decoration.cs:8) in the TreeObject archetype (Game.Prefabs/TreeObject.cs:29);
//     TreeGrowthSystem only ages trees WithDisabled<Decoration> (TreeGrowthSystem.cs:268-273
//     [1.6.2f1 :271]). Also done in the dead-run branch (untagged trees must not grow either).
//     KNOWN LIMIT (not fixable here): the reverse case — the player switches from a pending
//     glade preview straight to the vanilla object tool with the same species and vanilla
//     reuses our Decoration-enabled Temp — leaves that one vanilla tree non-growing
//     (DECISIONS 112).
//
// WHAT IS WRITTEN — layout v2 (Round G2, docs/research/run-binding-glade-spec.md §4.5; Round G3
// §8.2): every planted tree gets new SkiRunTreeMember(run, tool.ApplyingLayer,
// tool.ApplyingVariant, SkiGladeCompose.PlanGeneration). Every plan is SINGLE-LAYER (run
// interior → layer 0, tree line → layer 1), so layer and variant are per-plan values read once
// per apply. Round G3: the variant is always the tool's NewPlantingVariant (0) — no adoption, no
// roll (spec §8.2); members of another variant / generation read as "other" and the next apply
// of the layer removes them. Guards: a layer other than LayerGlade/LayerBelt tags NOTHING — an
// un-removable member with a bogus layer would be worse than a plain tree — WARN once per load,
// Decoration still forced on (and the apply check then reports created ≠ tagged). A variant of
// VariantUnknown is written as given and WARNed once per load. The layer / VariantUnknown
// constants of the tag and of the pure SkiGladeCompose must agree: checked at COMPILE time
// (LayerContract below).
//
// OUTPUT: one census line per Apply — "Glade: tagged N trees for run X — Interior L4 (Open
// glade), glade layer, variant 0, gen …; ApplyObjectsSystem create path N, canceled C (not
// created), delete D, update U, already deleted K" — the apply census source for planted trees
// (spec §6, §8.4 "tag lines name the level"); also kept in LastCensus for the dev tab. A create
// path count that differs from the tagged count is a WARN on the spot.
//
// SCOPE + DEFENSIVE CONTRACT (house pattern, SkiRunOwnershipSystem precedent): Game mode
// only; ARMED line per load (lists the layout version, PlanGeneration and the layer/variant
// constants); m_Armed/m_Disabled/m_ErrorLogged and the warn-once flags reset in
// OnGameLoadingComplete; one-shot "!!!" catch that disables tagging until the next load
// (the tool keeps planting; those trees are simply untagged, i.e. plain trees). No managed
// allocation outside Apply frames (OnUpdate returns on the guards first).
//
// Created 2026-09-30 (G1); v2 tags 2026-10-01 (Round G2, implementer I-2); Round G3 2026-10-01
// (implementer G3-B): exact ApplyObjectsSystem decision order, published counts, level in the line.

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
    public sealed partial class SkiGladeTagSystem : GameSystemBase
    {
        // Compile-time contract (header): division by constant zero (CS0020) if the persisted
        // tag's constants and the pure composition's constants ever drift apart.
        private const int LayerContract = 1 / ((SkiRunTreeMember.LayerGlade == SkiGladeCompose.LayerGlade &&
                                                SkiRunTreeMember.LayerBelt == SkiGladeCompose.LayerBelt &&
                                                SkiRunTreeMember.VariantUnknown == SkiGladeCompose.VariantUnknown) ? 1 : 0);

        private ToolSystem m_ToolSystem;
        private SkiGladeToolSystem m_Tool;
        private PrefabSystem m_PrefabSystem;

        private EntityQuery m_TempTreeQuery;

        // Per-load state — reset and re-armed at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_DeadRunWarned;
        private bool m_BadLayerWarned;
        private bool m_UnknownVariantWarned;

        /// <summary>Last apply census line (plain field; dev-tab safe at 10 Hz).</summary>
        public string LastCensus = "no apply yet";

        /// <summary>Trees tagged by the last apply (plain field).</summary>
        public int LastTagged;

        // Round G3 (header "Which Temps"): the last apply's ApplyObjectsSystem classes, published
        // for the tool's apply check. Valid when LastApplyRun / LastApplyFrame match.
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
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            // No Deleted filter (header "Which Temps"): ApplyObjectsSystem's query has none either.
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
                         "published for the tool's apply check; created ≠ tagged is a WARN");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;

                // Three-way guard (header): ours is the active tool, the frame is an Apply
                // frame for ToolSystem AND for our tool, and the tool names a run.
                if (m_ToolSystem.activeTool != m_Tool ||
                    m_ToolSystem.applyMode != ApplyMode.Apply ||
                    m_Tool.applyMode != ApplyMode.Apply)
                    return;
                Entity run = m_Tool.ApplyingRun;
                if (run == Entity.Null)
                    return;

                // Round G3: one classification pass in ApplyObjectsSystem's order (header "Which
                // Temps"), published whatever happens next.
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

                    if (!EntityManager.Exists(run) || EntityManager.HasComponent<Deleted>(run))
                    {
                        // Defensive: the run vanished between hover and apply. Plant untagged
                        // rather than write a dangling reference (cleanup would strip it anyway),
                        // but still force Decoration on (header: reused Temps keep a disabled bit).
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

                    // Per-plan values (spec §2: valid while ApplyingRun != Null).
                    byte layer = m_Tool.ApplyingLayer;
                    byte variant = m_Tool.ApplyingVariant;
                    if (layer != SkiRunTreeMember.LayerGlade && layer != SkiRunTreeMember.LayerBelt)
                    {
                        // Every Round G3 plan has a layer; planted Temps under any other value are
                        // unexpected. Never persist a bogus layer (header): plain trees instead.
                        ForceDecoration(create);
                        if (create.Length == 0)
                        {
                            LastCensus = $"nothing to tag for run {RunLabel(run)} (plan layer {layer}; " +
                                         $"{k.Delete} delete Temp(s), {k.Cancelled} cancelled)";
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

                    TagApplyingTrees(run, layer, variant, create, k);
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

        /// <summary>Round G3 (header "Which Temps"): one pass over every Temp tree in
        /// ApplyObjectsSystem.HandleTempEntitiesJob's order (ApplyObjectsSystem.cs:337-432):
        /// Cancel → not created; Delete → original deleted; original WITH PrefabRef → Update path
        /// (:354); else → Create (:432). Collects the create-path entities (all chunks, Deleted
        /// ones counted apart — ApplyObjectsSystem's query has no Deleted filter, :922, but CleanUp
        /// destroys them). Reads only; the caller mutates afterwards (structural changes
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

        private void TagApplyingTrees(Entity run, byte layer, byte variant, NativeList<Entity> create, Classes k)
        {
            int retagged = 0;
            var toAdd = new NativeList<Entity>(create.Length > 0 ? create.Length : 1, Allocator.Temp);
            var toSet = new NativeList<Entity>(4, Allocator.Temp);
            try
            {
                for (int i = 0; i < create.Length; i++)
                {
                    Entity e = create[i];
                    // A reused Temp entity could in principle already carry the tag (old Temps
                    // are collected for reuse by Game.Tools/GenerateObjectsSystem.cs:89-140
                    // FillOldObjectsJob; unverified that a tagged Temp can ever survive to here);
                    // set instead of add so the add batch never meets a duplicate.
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
                // One value for the whole apply: every plan is single-layer (header).
                var tag = new SkiRunTreeMember(run, layer, variant, SkiGladeCompose.PlanGeneration);
                if (toAdd.Length > 0)
                    EntityManager.AddComponent<SkiRunTreeMember>(toAdd.AsArray());
                for (int i = 0; i < toAdd.Length; i++)
                    EntityManager.SetComponentData(toAdd[i], tag);
                for (int i = 0; i < toSet.Length; i++)
                    EntityManager.SetComponentData(toSet[i], tag);

                // Spec §4.4 state on the permanent tree whatever Temp entity was reused
                // (header: GenerateObjectsSystem.cs:1311-1317 applies m_IsDecoration only on
                // the new-entity path; the reuse path :1112-1219 never touches the bit).
                int decorationFixed = 0;
                for (int i = 0; i < toAdd.Length; i++)
                    decorationFixed += EnsureDecoration(toAdd[i]);
                for (int i = 0; i < toSet.Length; i++)
                    decorationFixed += EnsureDecoration(toSet[i]);

                // Count what actually carries the tag now (not what was asked): a check, not
                // an echo — the line below WARNs when it differs from the create path.
                int tagged = 0;
                for (int i = 0; i < create.Length; i++)
                {
                    if (EntityManager.HasComponent<SkiRunTreeMember>(create[i]))
                        tagged++;
                }
                LastTagged = tagged;
                byte level = m_Tool.ApplyingLevel;
                LastCensus = $"tagged {tagged} trees for run {RunLabel(run)} — " +
                             (m_Tool.ApplyingTreeLine ? "Tree line" : "Interior") +
                             $" L{level} ({SkiGladeToolSystem.LevelName(level)}), " +
                             (layer == SkiRunTreeMember.LayerBelt ? "belt" : "glade") +
                             $" layer, variant {variant}, gen {SkiGladeCompose.PlanGeneration}, layout " +
                             $"v{SkiRunTreeMember.CurrentVersion}; ApplyObjectsSystem create path {create.Length}, " +
                             $"cancelled {k.Cancelled} (not created), delete {k.Delete}, update {k.Update}, " +
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

        /// <summary>Sets Decoration enabled on a planted Temp tree; returns 1 if the bit was
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
