// Quick runs tool: the tree filler (DECISIONS 412-415). The region filled with trees below a tree line, outside every run (ours,
// existing and the enabled candidates) by a margin: the glade tool's compose path with a fill mask (SkiGladeCompose.Compose with
// ComposeInput.Fill = GladeMask, layer LayerFiller), its census idiom (static search tree, TreeBoundsIterator), its definitions
// (SkiGladeToolSystem.EmitPlantDefinition / EmitThinDefinition) and its tag system (this tool is the second IGladeTagSource: run Null,
// LayerFiller). Density = the glade levels; the tree line defaults to 95 % of the map's own trees (SkiGladeTreeLine.Estimate), the hub
// steps it (10 / 50 m), the overlay draws its contour (SnowLineRules.MarchingSquares). Species: group chips (Conifer default, Deciduous, Palm, All
// trees) or one species from every tree prefab with wood (SkiGladeSpecies). Plant runs in slices of FillerSliceTrees, one Apply each, and
// continues by itself until the slope is complete ("Planting trees 2,400 / 9,000"; right click stops after the slice in flight, every
// applied slice stays planted and undoable). Undo in the same session deletes the planted batch with the bulldoze definition (vanilla
// refunds Recent trees) in slices of the glade cap MaxRemovePerApply, one Apply each ("Removing trees 4,000 / 9,000"; right click stops
// after the slice in flight, the rest stays undoable). While the preview is on, an edit (tree line, species, level, corners) only marks
// the plan dirty: the compose runs TreeDebounceFrames after the last edit; the planting slices, trees on and Plant re-plan at once.
// Engine notes: EngineNotes/QuickRunsToolSystem.md (Tree filler)

using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using SkiResortMod.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class QuickRunsToolSystem
    {
        private const float HoleMarginM = 3f;    // package C's margin (BUILD-BRIEF, filler mask)
        private const int ContourSide = 65;      // contour grid (64 x 64 cells over the region's bounds)
        /// <summary>Trees per Apply while planting (within the glade caps): smoothness over speed, the slices continue by themselves.</summary>
        internal const int FillerSliceTrees = 2000;
        /// <summary>Frames after the last edit before the preview re-composes (one compose per burst of clicks, not one per click).</summary>
        internal const int TreeDebounceFrames = 15;

        /// <summary>Group chip indices the hub sends (hubText QUICK_GROUP[*]).</summary>
        internal const int GroupConifer = 0, GroupDeciduous = 1, GroupPalm = 2, GroupAll = 3;

        internal struct SpeciesEntry
        {
            public string Name;
            public Entity Prefab;
            public float OffsetY;
            public TreeGroup Group;
        }

        /// <summary>The ground field of the fill mask over the scan's snapshot (package C's adapter seam).</summary>
        private sealed class SnapshotGround : GladeHeightField
        {
            public HeightSnapshot Snap;
            public ulong Print;
            public override float HeightAt(double x, double z) => Snap.Sample(new float3((float)x, 0f, (float)z));
            public override ulong Fingerprint => Print;
        }

        private bool m_TreesOn, m_TreesDirty, m_TreeSnapStale = true, m_TreeLineStale = true, m_TreeLineAuto = true;
        private bool m_TreesEmitPending, m_TreesPreviewReady, m_TreeApplyRequested, m_ApplyingTrees, m_TreeBatchFresh;
        private int m_TreeUndoStage;   // 0 none, 1 thin definitions of a slice emitted (apply next frame), 2 emit the next slice
        private bool m_Removing;       // Undo trees: slices of m_TreeBatch[m_RemoveCursor..] apply until the batch is gone or stopped
        private int m_RemoveCursor, m_RemoveSliceEnd, m_RemoveSliceEmitted, m_RemoveDone, m_RemoveTotal, m_RemoveSlices;
        private long m_RemoveStartTicks;
        private int m_TreesDirtyFrame, m_TreesCleanFrame;   // frame of the last debounced edit / of the last compose
        private bool m_TreesComposeNow;                     // dirty without debounce (trees on, Plant, the planting slices)
        private bool m_PlantAfterCompose;               // Plant pressed while dirty: plant once the fresh plan is emitted
        private byte m_TreeLevel = SkiGladeCompose.DefaultLevel, m_ApplyingTreeLevel;
        private float m_TreeLine = float.PositiveInfinity, m_GroundMin, m_GroundMax;
        private int m_TreeGroup = GroupConifer, m_SpeciesPick = -1;
        private bool m_SpeciesResolved;
        private readonly List<SpeciesEntry> m_Species = new List<SpeciesEntry>();
        private readonly List<int> m_Pool = new List<int>();
        private TreeLineEstimate m_TreeLineEst;

        private readonly ComposeInput m_TreeInput = new ComposeInput();
        private readonly ComposeResult m_TreeResult = new ComposeResult();
        private readonly GladePlanCache m_TreeCache = new GladePlanCache(SkiGladeToolSystem.Tuning.PlanCacheEntries);
        private readonly SnapshotGround m_Ground = new SnapshotGround();
        private readonly Dictionary<ulong, HashSet<int>> m_TreeSkip = new Dictionary<ulong, HashSet<int>>();
        private readonly Dictionary<long, int> m_TreeSlots = new Dictionary<long, int>();   // planted position key -> slot (skip learning)
        private ulong m_TreePlanKey, m_AppliedPlanKey;
        private readonly List<float3> m_Existing = new List<float3>();
        private readonly List<ComposeMember> m_OwnMembers = new List<ComposeMember>();
        private readonly List<Entity> m_OwnEntities = new List<Entity>();
        private readonly List<Entity> m_TreeBatch = new List<Entity>();
        private int m_TreePlant, m_TreeRemove, m_TreeTarget, m_TreeNow, m_TreeWild;
        private GladeStatus m_TreeStatus;
        private bool m_Planting;   // Plant pressed: apply slice after slice until complete or stopped
        private int m_PlantStartNow, m_PlantTotal, m_PlantSlices, m_PlantPlanted, m_SliceFrame;
        private long m_PlantStartTicks, m_SliceTicks;
        private SnowLineRules.Segment[] m_Contour = new SnowLineRules.Segment[2 * (ContourSide - 1) * (ContourSide - 1)];
        private int m_ContourCount;
        private float[] m_ContourHeights = new float[ContourSide * ContourSide];

        internal bool TreesOn => m_TreesOn;
        internal byte TreeLevel => m_TreeLevel;
        internal float TreeLine => m_TreeLine;
        internal float GroundMin => m_GroundMin;
        internal float GroundMax => m_GroundMax;
        internal TreeLineEstimate TreeLineEstimate => m_TreeLineEst;
        internal int TreeGroupIndex => m_TreeGroup;
        internal int SpeciesPick => m_SpeciesPick;
        internal IReadOnlyList<SpeciesEntry> Species => m_Species;
        internal int TreePlant => m_TreePlant;
        /// <summary>Trees still to plant for the target (the whole slope, not one slice); the hub's and the tooltip's count.</summary>
        internal int TreeToPlant => math.max(m_TreePlant, m_TreeTarget - m_TreeNow);
        internal bool Planting => m_Planting;
        /// <summary>"Removing trees N / M": the undo in flight (trees removed / trees it set out to remove).</summary>
        internal bool RemovingTrees => m_Removing;
        internal int RemoveDone => m_RemoveDone;
        internal int RemoveTotal => math.max(m_RemoveTotal, m_RemoveDone);
        /// <summary>"Planting trees N / M": trees planted by this Plant / trees it set out to plant.</summary>
        internal int PlantDone => math.clamp(m_PlantPlanted, 0, math.max(m_PlantTotal, m_PlantPlanted));
        internal int PlantTotal => math.max(m_PlantTotal, m_PlantPlanted);
        internal int TreeRemove => m_TreeRemove;
        internal int TreeTarget => m_TreeTarget;
        internal int TreeNow => m_TreeNow;
        internal int TreeBatchCount => m_TreeBatch.Count;
        internal bool TreesReady => m_TreesPreviewReady;
        internal GladeStatus TreeStatus => m_TreeStatus;

        // IGladeTagSource (explicit: the tag system reads it in the ApplyTool phase of our Apply frame)
        bool IGladeTagSource.ApplyingNow => m_ApplyingTrees;
        Entity IGladeTagSource.ApplyingRun => Entity.Null;
        byte IGladeTagSource.ApplyingLayer => SkiGladeCompose.LayerFiller;
        byte IGladeTagSource.ApplyingVariant => 0;   // new plantings are variant 0 (the glade tool's NewPlantingVariant)
        /// <summary>The level of the filler Apply in flight (the tag system's census line).</summary>
        internal byte ApplyingLevel => m_ApplyingTreeLevel;

        void IGladeTagSource.OnTagged(NativeList<Entity> created)
        {
            // The batch for undo, and the skip set: emitted slots that created no tree were refused by validation (Optional -> canceled)
            var hit = new HashSet<int>();
            for (int i = 0; i < created.Length; i++)
            {
                Entity e = created[i];
                m_TreeBatch.Add(e);
                if (EntityManager.TryGetComponent(e, out Game.Objects.Transform tr) &&
                    m_TreeSlots.TryGetValue(SkiGladeToolSystem.PositionKey(tr.m_Position), out int slot))
                    hit.Add(slot);
            }
            int blocked = 0;
            if (!m_TreeSkip.TryGetValue(m_AppliedPlanKey, out HashSet<int> skip))
                m_TreeSkip[m_AppliedPlanKey] = skip = new HashSet<int>();
            foreach (int slot in m_TreeSlots.Values)
            {
                if (hit.Contains(slot))
                    continue;
                if (skip.Add(slot))
                    blocked++;
            }
            m_TreeBatchFresh = created.Length > 0;
            if (m_Planting)
                m_PlantPlanted += created.Length;
            Mod.Log.Info($"QuickRuns [trees]: APPLIED — {created.Length} filler tree(s) planted and tagged, {blocked} spot(s) refused by the " +
                         $"game learned as blocked; {m_TreeBatch.Count} tree(s) undoable this session");
            Bump();
        }


        // ---------------------------------------------------------------- hub / Enter

        private void SetTrees(bool on, string why)
        {
            if (on == m_TreesOn)
                return;
            if (on)
            {
                if (m_Edit.Count < 3)
                {
                    Say($"QuickRuns [trees]: needs a region ({why}) — click at least three corners", NoticeKey("NEED_CORNERS"));
                    return;
                }
                m_TreesOn = true;
                MarkTreesDirty(false);
                m_TreeLineStale = true;
                ResolveSpecies();
                Say($"QuickRuns [trees]: ON ({why}) — {SpeciesText()}, level {m_TreeLevel}");
            }
            else
            {
                if (m_Planting)
                    FinishPlanting("stopped: trees turned off");
                if (m_Removing)
                    FinishRemoving("stopped: trees turned off", cancelInFlight: true);
                ClearTreePreview();
                m_TreesOn = false;
                Say($"QuickRuns [trees]: off ({why})");
            }
            Bump();
        }

        /// <summary>Trees off without logging (tool reset).</summary>
        private void SetTreesOff()
        {
            m_TreesOn = m_TreesDirty = m_TreesEmitPending = m_TreesPreviewReady = m_TreeApplyRequested = m_Planting = false;
            m_TreeUndoStage = 0;
            m_Removing = m_PlantAfterCompose = false;
            m_TreesComposeNow = false;
            m_TreeSnapStale = m_TreeLineStale = true;
            m_TreeLineAuto = true;
            m_ContourCount = 0;
            m_TreePlant = m_TreeRemove = m_TreeTarget = m_TreeNow = 0;
        }

        private void ClearTreePreview()
        {
            if (m_TreesEmitPending || m_TreesPreviewReady)
            {
                applyMode = ApplyMode.Clear;
                m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            }
            m_TreesEmitPending = m_TreesPreviewReady = m_TreeApplyRequested = false;
            m_TreePlant = m_TreeRemove = 0;
        }

        private void RequestPlant(string why)
        {
            if (!m_TreesOn)
            {
                Say($"QuickRuns [trees]: turn trees on first ({why})", NoticeKey("TREES_OFF"));
                return;
            }
            if (m_Planting)
                return;   // already planting: the slices continue by themselves
            if (m_Removing)
            {
                Say($"QuickRuns [trees]: plant refused ({why}) — trees are being removed (Stop first)");
                return;
            }
            if (m_TreesDirty)
            {
                // The preview on screen is older than the last edit (debounce) or not validated yet: plant from the fresh plan
                m_PlantAfterCompose = true;
                MarkTreesDirty(false);
                Say($"QuickRuns [trees]: plant ({why}) waits for the re-plan of the last edit");
                return;
            }
            if (m_TreePlant + m_TreeRemove == 0)
            {
                Say($"QuickRuns [trees]: nothing to plant ({why}) — the region already has this density", NoticeKey("TREES_COMPLETE"));
                return;
            }
            m_Planting = true;
            m_PlantStartNow = m_TreeNow;
            m_PlantTotal = TreeToPlant;
            m_PlantPlanted = m_PlantSlices = 0;
            m_PlantStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            m_TreeApplyRequested = true;
            Say($"QuickRuns [trees]: PLANT ({why}) — about {m_PlantTotal} tree(s) in slices of {FillerSliceTrees}; right click stops after a slice");
            Bump();
        }

        /// <summary>Right click or the hub's Stop while planting: the slice in flight finishes, no further slice is applied.</summary>
        private void StopPlanting(string why)
        {
            if (m_Removing)
                FinishRemoving("stopped (" + why + ")", cancelInFlight: false);   // the slice in flight still applies
            if (!m_Planting)
                return;
            m_TreeApplyRequested = false;
            FinishPlanting("stopped (" + why + ")");
        }

        /// <summary>Ends a planting and logs its wall time; <paramref name="notice"/> false keeps the notice a refusal just set.</summary>
        private void FinishPlanting(string how, bool notice = true)
        {
            m_Planting = false;
            bool stopped = how.StartsWith("stopped", StringComparison.Ordinal);
            Say(string.Format(CultureInfo.InvariantCulture,
                "QuickRuns [trees]: PLANTING {0} — {1} tree(s) planted in {2} slice(s), {3:F0} ms wall; {4} tree(s) undoable this session",
                how.ToUpperInvariant(), m_PlantPlanted, m_PlantSlices, QuickRunsRules.MsSince(m_PlantStartTicks), m_TreeBatch.Count),
                notice ? NoticeKey(stopped ? "TREES_STOPPED" : "TREES_PLANTED") : null);
            Bump();
        }

        private void RequestTreeUndo(string why)
        {
            if (m_Removing)
                return;   // already removing: the slices continue by themselves
            if (m_Planting)
                StopPlanting("undo");
            int alive = 0;
            foreach (Entity e in m_TreeBatch)
                if (EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e))
                    alive++;
            if (alive == 0)
            {
                m_TreeBatch.Clear();
                Say($"QuickRuns [trees]: nothing to undo ({why})", NoticeKey("NOTHING_TO_UNDO"));
                return;
            }
            ClearTreePreview();
            m_PlantAfterCompose = false;
            m_Removing = true;
            m_RemoveCursor = m_RemoveDone = m_RemoveSlices = 0;
            m_RemoveTotal = alive;
            m_RemoveStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            Say($"QuickRuns [trees]: undo ({why}) — {alive} planted tree(s) to remove in slices of {SkiGladeToolSystem.Tuning.MaxRemovePerApply}; " +
                "right click stops after a slice");
            EmitRemoveSlice();
        }

        /// <summary>Thin definitions for the next MaxRemovePerApply live trees of the batch from m_RemoveCursor (the glade cap compose
        /// uses); they apply on the next frame (UpdateTreesCycle stage 1).</summary>
        private void EmitRemoveSlice()
        {
            applyMode = ApplyMode.Clear;
            m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            EntityCommandBuffer ecb = m_Barrier.CreateCommandBuffer();
            int cap = SkiGladeToolSystem.Tuning.MaxRemovePerApply, emitted = 0, i = m_RemoveCursor;
            for (; i < m_TreeBatch.Count && emitted < cap; i++)
                if (SkiGladeToolSystem.EmitThinDefinition(EntityManager, ecb, m_TreeBatch[i], out float3 _))
                    emitted++;
            m_RemoveSliceEnd = i;
            m_RemoveSliceEmitted = emitted;
            m_TreeUndoStage = 1;
            if (SkiResortSettings.DiagnosticsOn)
                Mod.Log.Info($"QuickRuns [probe] removing slice {m_RemoveSlices + 1}: {emitted} removal(s) emitted (batch {m_RemoveCursor}..{i} of " +
                             $"{m_TreeBatch.Count}), applying next frame");
        }

        /// <summary>Ends an undo. The applied part leaves the batch; the rest stays undoable. <paramref name="cancelInFlight"/>: the
        /// slice emitted but not applied is dropped (its definitions cleared) instead of applying.</summary>
        private void FinishRemoving(string how, bool cancelInFlight)
        {
            m_Removing = false;
            if (m_TreeUndoStage == 1 && !cancelInFlight)
                return;   // the slice in flight applies next frame; UpdateTreesCycle then closes (m_Removing is false)
            if (m_TreeUndoStage == 1)
            {
                // Nothing of that slice applied: its definitions go (the ClearTreePreview idiom) and it stays in the batch
                applyMode = ApplyMode.Clear;
                m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            }
            CloseRemoving(how);   // stage 2 (between slices) or 0: nothing in flight
        }

        private void CloseRemoving(string how)
        {
            m_Removing = false;
            m_TreeUndoStage = 0;
            m_TreeBatch.RemoveRange(0, math.min(m_RemoveCursor, m_TreeBatch.Count));
            m_RemoveCursor = 0;
            if (m_TreeBatch.Count == 0)
                m_TreeBatchFresh = false;
            if (m_TreesOn)
                MarkTreesDirty(true);
            bool stopped = how.StartsWith("stopped", StringComparison.Ordinal);
            Say(string.Format(CultureInfo.InvariantCulture,
                "QuickRuns [trees]: {0} — {1} planted tree(s) removed in {2} slice(s), {3:F0} ms wall (vanilla refunds recently placed trees); " +
                "{4} tree(s) still undoable this session", stopped ? "UNDO " + how.ToUpperInvariant() : "UNDONE", m_RemoveDone, m_RemoveSlices,
                QuickRunsRules.MsSince(m_RemoveStartTicks), m_TreeBatch.Count), stopped ? null : NoticeKey("TREES_UNDONE"));   // a stopped undo: the hub's Undo trees count shows what is left
            Bump();
        }

        /// <summary>Marks the tree plan dirty. <paramref name="debounce"/>: an edit while the preview is on (compose TreeDebounceFrames
        /// after the last one); false: compose on the next frame (trees on, Plant, the planting slices).</summary>
        private void MarkTreesDirty(bool debounce)
        {
            m_TreesDirty = true;
            if (debounce)
                m_TreesDirtyFrame = m_Frame;
            else
                m_TreesComposeNow = true;
        }

        /// <summary>The dirty plan composes this frame: never during an undo; at once unless debounced; else TreeDebounceFrames after
        /// the last edit.</summary>
        private bool TreeComposeDue()
        {
            if (!m_TreesDirty || m_Removing || m_TreeUndoStage != 0)
                return false;
            if (m_Planting || m_TreesComposeNow)
                return true;
            if (m_TreesDirtyFrame <= m_TreesCleanFrame)
                m_TreesDirtyFrame = m_Frame;   // marked dirty elsewhere (a corner edit, RegionChanged): the window starts now
            return m_Frame - m_TreesDirtyFrame >= TreeDebounceFrames;
        }

        private void SetTreeLine(int meters, string why)
        {
            m_TreeLineAuto = false;
            m_TreeLine = QuickRunsRules.ClampTreeLine(meters, m_GroundMin, m_GroundMax);
            MarkTreesDirty(true);
            Say(string.Format(CultureInfo.InvariantCulture, "QuickRuns [trees]: tree line {0:F0} m ({1}; ground {2:F0}-{3:F0} m)",
                m_TreeLine, why, m_GroundMin, m_GroundMax));
            Bump();
        }

        private void SetSpeciesGroup(int group, string why)
        {
            m_TreeGroup = math.clamp(group, GroupConifer, GroupAll);
            m_SpeciesPick = -1;
            BuildPool();
            MarkTreesDirty(true);
            Say($"QuickRuns [trees]: species {SpeciesText()} ({why})");
            Bump();
        }

        private void SetSpecies(int index, string why)
        {
            ResolveSpecies();
            m_SpeciesPick = index >= 0 && index < m_Species.Count ? index : -1;
            BuildPool();
            MarkTreesDirty(true);
            Say($"QuickRuns [trees]: species {SpeciesText()} ({why})");
            Bump();
        }

        private void SetTreeLevel(int level, string why)
        {
            m_TreeLevel = (byte)math.clamp(level, 1, SkiGladeCompose.Levels);
            MarkTreesDirty(true);
            Say($"QuickRuns [trees]: level {m_TreeLevel} ({SkiGladeToolSystem.LevelName(m_TreeLevel)}) ({why})");
            Bump();
        }


        // ---------------------------------------------------------------- the per-frame cycle

        /// <summary>Preview-ready flip, the Apply frame and the undo's apply frame. True when this frame applied (no other input).</summary>
        private bool UpdateTreesCycle()
        {
            if (m_TreeUndoStage == 2)
            {
                EmitRemoveSlice();   // the next undo slice (emit and apply never share a frame, the planting idiom)
                return true;
            }
            if (m_TreeUndoStage == 1)
            {
                m_TreeUndoStage = 0;
                applyMode = ApplyMode.Apply;   // the removals (Temp Delete) apply; ApplyingNow stays false: nothing to tag
                m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
                m_RemoveDone += m_RemoveSliceEmitted;
                m_RemoveCursor = m_RemoveSliceEnd;
                m_RemoveSlices++;
                if (m_Removing && m_RemoveCursor < m_TreeBatch.Count)
                    m_TreeUndoStage = 2;
                else
                    CloseRemoving(m_Removing ? "complete" : "stopped after the slice in flight");
                Bump();
                return true;
            }
            if (!m_TreesOn)
                return false;
            if (m_Planting && !m_TreesDirty && !m_TreesEmitPending && !m_TreesPreviewReady && !m_TreeApplyRequested)
                FinishPlanting("stopped: no plan for the next slice", false);   // the compose refused (its own notice says why)
            bool flipped = false;
            if (m_TreesEmitPending)
            {
                m_TreesEmitPending = false;
                m_TreesPreviewReady = true;   // last frame's definitions were generated and validated (the glade tool's ready flip)
                flipped = true;
                if (m_Planting)
                    m_TreeApplyRequested = true;   // the next slice applies on the next frame
                Bump();
            }
            if (m_TreeApplyRequested && m_TreesPreviewReady && !flipped)
            {
                m_TreeApplyRequested = false;
                if (!GetAllowApply())
                {
                    int errors = m_ErrorQuery.IsEmptyIgnoreFilter ? 0 : m_ErrorQuery.CalculateEntityCount();
                    if (m_Planting)
                        FinishPlanting("stopped: blocked by the game", false);   // the blocked notice below is the one shown
                    Say($"QuickRuns [trees]: apply blocked by {errors} error(s)", NoticeKey("TREES_BLOCKED"));
                    return false;
                }
                if (m_Planting)
                {
                    m_PlantSlices++;
                    if (SkiResortSettings.DiagnosticsOn && m_PlantSlices > 1)
                        Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                            "QuickRuns [probe] planting slice {0}: {1} planted so far of about {2}; {3:F0} ms wall over {4} frame(s) since the last slice",
                            m_PlantSlices, m_PlantPlanted, m_PlantTotal, QuickRunsRules.MsSince(m_SliceTicks), m_Frame - m_SliceFrame));
                    m_SliceTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    m_SliceFrame = m_Frame;
                }
                applyMode = ApplyMode.Apply;
                m_ApplyingTrees = true;
                m_ApplyingTreeLevel = m_TreeLevel;
                m_AppliedPlanKey = m_TreePlanKey;
                m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
                Say($"QuickRuns [trees]: APPLY — plant {m_TreePlant}, remove {m_TreeRemove} (level {m_TreeLevel}, tree line " +
                    $"{(float.IsPositiveInfinity(m_TreeLine) ? "none" : m_TreeLine.ToString("F0", CultureInfo.InvariantCulture) + " m")}, {SpeciesText()})");
                m_TreesPreviewReady = false;
                m_TreePlant = m_TreeRemove = 0;
                MarkTreesDirty(false);   // the next frame re-composes: Complete, or the next slice (no debounce)
                Bump();
                return true;
            }
            return false;
        }


        // ---------------------------------------------------------------- compose

        /// <summary>Census, mask, compose, emit (the glade tool's PlanAndEmit for a region). The previous preview is cleared in the same
        /// frame (the glade tool's Replan idiom).</summary>
        private void ComposeTrees(string why)
        {
            m_TreesDirty = m_TreesComposeNow = false;
            m_TreesCleanFrame = m_Frame;
            bool plantAfter = m_PlantAfterCompose;   // dropped when this compose stops early (its notice says why)
            m_PlantAfterCompose = false;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            ClearTreePreview();
            float2[] region = CornerArray();
            if (region == null || region.Length < 3)
                return;
            if (m_TreeSnapStale || m_Snap == null)
            {
                if (!TakeSnapshot(region, out string failure))
                {
                    Say($"QuickRuns [trees]: no plan — {failure}", QuickRunsRules.StatusKey(AutoPlanStatus.BadInput));
                    return;
                }
                m_TreeSnapStale = false;
                m_TreeLineStale = true;
            }
            ResolveSpecies();
            if (m_Pool.Count == 0)
            {
                Say($"QuickRuns [trees]: no tree species in {SpeciesText()}", NoticeKey("NO_SPECIES"));
                return;
            }
            BuildContourGrid(region);
            float3[] ring = Ring3(region);
            if (m_TreeLineStale)
            {
                m_TreeLineStale = false;
                m_TreeLineEst = SkiGladeTreeLine.Estimate(ring, WildTreesAround(region), m_GroundMin, m_GroundMax);
                if (m_TreeLineAuto)
                    m_TreeLine = QuickRunsRules.ClampTreeLine(m_TreeLineEst.Height, m_GroundMin, m_GroundMax);
                Say(string.Format(CultureInfo.InvariantCulture,
                    "QuickRuns [trees]: tree line estimate {0:F0} m ({1}; {2} tree(s) inside, {3} around), ground {4:F0}-{5:F0} m; using {6}",
                    m_TreeLineEst.Height, m_TreeLineEst.Source, m_TreeLineEst.TreesInside, m_TreeLineEst.TreesAround, m_GroundMin, m_GroundMax,
                    m_TreeLineAuto ? "the estimate" : "the slider"));
            }
            March();

            CensusRegion(region);
            GladeParams p = SkiGladeToolSystem.BuildParams(false, m_Pool.Count);
            SkiGladeSpecies.ApplyPool(ref p, m_Pool.Count);
            m_Ground.Snap = m_Snap;
            m_Ground.Print = m_SnapPrint;
            var mask = new GladeMask
            {
                Holes = Holes(region),
                HoleMargin = HoleMarginM,
                Ground = m_Ground,
                TreeLine = m_TreeLine >= m_GroundMax + QuickRunsRules.TreeLineStepM ? float.PositiveInfinity : m_TreeLine,
            };
            m_TreeInput.Ring = ring;
            m_TreeInput.Triangles = null;   // the planner ear-clips (SkiGladePlanner, no triangle buffer for a region)
            m_TreeInput.Params = p;
            m_TreeInput.Level = m_TreeLevel;
            m_TreeInput.TreeLine = false;
            m_TreeInput.Variant = 0;
            m_TreeInput.Obstacles = null;
            m_TreeInput.ExclusionRings = null;
            m_TreeInput.Fill = mask;
            m_TreeInput.Existing = m_Existing.ToArray();
            m_TreeInput.Thinnable = null;   // the filler never fells
            m_TreeInput.Own = m_OwnMembers.ToArray();
            m_TreeInput.Caps = new ComposeCaps
            {
                MaxPlant = math.min(FillerSliceTrees, SkiGladeToolSystem.Tuning.MaxPlantPerApply),
                MaxFell = 0,
                MaxRemove = SkiGladeToolSystem.Tuning.MaxRemovePerApply,
            };
            m_TreeInput.SkipSlots = null;
            if (m_TreeSkip.Count > 0)
            {
                ulong key = SkiGladeCompose.PlanKey(m_TreeInput);
                if (m_TreeSkip.TryGetValue(key, out HashSet<int> known) && known.Count > 0)
                    m_TreeInput.SkipSlots = known;
            }
            m_TreeStatus = SkiGladeCompose.Compose(m_TreeInput, m_TreeResult, m_TreeCache);
            ComposeResult r = m_TreeResult;
            m_TreePlanKey = r.PlanKey;
            m_TreeTarget = r.Target;
            m_TreeNow = r.Inside;
            m_TreeWild = r.WildInside;
            int planted = 0, removed = 0;
            m_TreeSlots.Clear();
            if ((m_TreeStatus == GladeStatus.Ok || m_TreeStatus == GladeStatus.Clamped) && !r.Complete)
            {
                EntityCommandBuffer ecb = m_Barrier.CreateCommandBuffer();
                for (int k = 0; k < r.Remove.Count; k++)
                {
                    int idx = r.Remove[k];
                    if (idx >= 0 && idx < m_OwnEntities.Count &&
                        SkiGladeToolSystem.EmitThinDefinition(EntityManager, ecb, m_OwnEntities[idx], out float3 _))
                        removed++;
                }
                if (r.Plant.Count > 0)
                {
                    TerrainHeightData hd = m_TerrainSystem.GetHeightData();
                    List<PlantedTree> list = r.Plan.Planted;
                    for (int k = 0; k < r.Plant.Count; k++)
                    {
                        int slot = r.Plant[k];
                        if (slot < 0 || slot >= list.Count)
                            continue;
                        PlantedTree tree = list[slot];
                        int s = tree.Species >= 0 && tree.Species < m_Pool.Count ? tree.Species : 0;
                        SpeciesEntry sp = m_Species[m_Pool[s]];
                        if (SkiGladeToolSystem.EmitPlantDefinition(ecb, ref hd, sp.Prefab, sp.OffsetY, tree, out float3 pos))
                        {
                            m_TreeSlots[SkiGladeToolSystem.PositionKey(pos)] = slot;
                            planted++;
                        }
                    }
                }
            }
            m_TreePlant = planted;
            m_TreeRemove = removed;
            m_TreesEmitPending = planted + removed > 0;
            if (m_Planting && planted + removed == 0)
                FinishPlanting(r.Complete ? "complete" : "done (" + m_TreeStatus + ")");
            m_TreesPreviewReady = false;
            double ms = QuickRunsRules.MsSince(t0);
            Say(string.Format(CultureInfo.InvariantCulture,
                "QuickRuns [trees]: plan ({0}) — status {1}, level {2}, target {3}, now {4} (wild {5}, own {6}), plant {7}, remove {8}{9}; " +
                "{10} hole(s), tree line {11}; {12}; {13:F1} ms",
                why, m_TreeStatus, m_TreeLevel, r.Target, r.Inside, r.WildInside, r.OwnInside, planted, removed,
                r.Complete ? " (complete)" : r.Clamped ? " (clamped: apply again for the rest)" : string.Empty, mask.Holes?.Length ?? 0,
                float.IsPositiveInfinity(mask.TreeLine) ? "none" : mask.TreeLine.ToString("F0", CultureInfo.InvariantCulture) + " m",
                SpeciesText(), ms));
            Bump();
            if (plantAfter)
                RequestPlant("after the re-plan");
        }

        private float3[] Ring3(float2[] region)
        {
            var ring = new float3[region.Length];
            for (int i = 0; i < region.Length; i++)
            {
                float y = m_Snap.Sample(new float3(region[i].x, 0f, region[i].y));
                ring[i] = new float3(region[i].x, math.isfinite(y) ? y : 0f, region[i].y);
            }
            return ring;
        }

        /// <summary>Holes: every run outline of ours in the region's bounds (existing runs and cat tracks), plus the enabled candidates
        /// while a run preview shows (so a later build finds its ground free).</summary>
        private float3[][] Holes(float2[] region)
        {
            var holes = new List<float3[]>();
            SkiRunAutoPlanner.BBox(region, out float2 rbMin, out float2 rbMax);
            var rb = new Bounds2(rbMin, rbMax);
            using (var areas = m_AreaQuery.ToEntityArray(Allocator.Temp))
            using (var prefabs = m_AreaQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < areas.Length; i++)
                {
                    if (m_Ownership.KindOf(prefabs[i].m_Prefab) < 0)
                        continue;
                    float3[] ring = RunAreaEmit.ReadRing3(EntityManager, areas[i], out Bounds2 b);
                    if (ring != null && MathUtils.Intersect(new Bounds2(b.min - HoleMarginM, b.max + HoleMarginM), rb))
                        holes.Add(ring);
                }
            }
            if (m_Phase == ToolPhase.Preview)
            {
                AutoPlanResult r = m_Planner.Result;
                for (int i = 0; i < r.Runs.Count; i++)
                    if (IsEnabled(i) && r.Runs[i].Nodes != null)
                        holes.Add(r.Runs[i].Nodes);
            }
            return holes.Count > 0 ? holes.ToArray() : null;
        }

        /// <summary>Census of the region (+ GladeMinSpacing band, the glade interior's band): filler members (LayerFiller, run Null) go to
        /// Own, every other tree to Existing (wild trees count as occupants: the filler never fells). The glade tool's CensusTrees idiom:
        /// static search tree, IsTree mask, Overridden trees included; burning or owned filler members are fixed occupants.</summary>
        private void CensusRegion(float2[] region)
        {
            m_Existing.Clear();
            m_OwnMembers.Clear();
            m_OwnEntities.Clear();
            float band = SkiGladeToolSystem.Tuning.GladeMinSpacing;
            SkiRunAutoPlanner.BBox(region, out float2 rbMin, out float2 rbMax);
            var rb = new Bounds2(rbMin, rbMax);
            var found = new NativeList<Entity>(256, Allocator.Temp);
            try
            {
                SearchTrees(new Bounds2(rb.min - band, rb.max + band), found);
                for (int k = 0; k < found.Length; k++)
                {
                    Entity e = found[k];
                    if (EntityManager.HasComponent<Deleted>(e) || !EntityManager.TryGetComponent(e, out Game.Objects.Transform tr))
                        continue;
                    float2 xz = tr.m_Position.xz;
                    if (!GradeRaster.Inside(region, xz) && SkiRunAutoPlanner.PointRingDistance(region, xz) > band)
                        continue;
                    bool filler = EntityManager.TryGetComponent(e, out SkiRunTreeMember m) && m.m_Layer == SkiRunTreeMember.LayerFiller &&
                                  m.m_Run == Entity.Null;
                    bool fixedTree = EntityManager.HasComponent<Owner>(e) || EntityManager.HasComponent<Game.Events.OnFire>(e);
                    if (filler && !fixedTree)
                    {
                        m_OwnMembers.Add(new ComposeMember { Position = tr.m_Position, Layer = m.m_Layer, Variant = m.m_Variant, PlanGen = m.m_PlanGen });
                        m_OwnEntities.Add(e);
                    }
                    else
                        m_Existing.Add(tr.m_Position);
                }
            }
            finally
            {
                found.Dispose();
            }
        }

        /// <summary>Wild trees (no member tag, no owner) within SkiGladeTreeLine.AroundM of the region: the tree line estimate's input.</summary>
        private float3[] WildTreesAround(float2[] region)
        {
            SkiRunAutoPlanner.BBox(region, out float2 rbMin, out float2 rbMax);
            var rb = new Bounds2(rbMin, rbMax);
            float around = SkiGladeTreeLine.AroundM;
            var found = new NativeList<Entity>(1024, Allocator.Temp);
            var trees = new List<float3>();
            try
            {
                SearchTrees(new Bounds2(rb.min - around, rb.max + around), found);
                for (int k = 0; k < found.Length; k++)
                {
                    Entity e = found[k];
                    if (EntityManager.HasComponent<SkiRunTreeMember>(e) || EntityManager.HasComponent<Owner>(e) ||
                        EntityManager.HasComponent<Deleted>(e) || !EntityManager.TryGetComponent(e, out Game.Objects.Transform tr))
                        continue;
                    trees.Add(tr.m_Position);
                }
            }
            finally
            {
                found.Dispose();
            }
            return trees.ToArray();
        }

        private void SearchTrees(Bounds2 area, NativeList<Entity> results)
        {
            // Synchronous read (the glade tool's CensusTrees idiom, SearchSystem.cs:421-425)
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = m_ObjectSearch.GetStaticSearchTree(readOnly: true, out JobHandle deps);
            deps.Complete();
            var it = new SkiGladeToolSystem.TreeBoundsIterator { m_Bounds = area, m_UseTriangle = false, m_Results = results };
            tree.Iterate(ref it);
        }


        // ---------------------------------------------------------------- tree line contour

        private float m_ContourOriginX, m_ContourOriginZ, m_ContourCell;

        /// <summary>Snapshot heights on a ContourSide grid over the region's bounds; the ground range over the grid points inside the region.</summary>
        private void BuildContourGrid(float2[] region)
        {
            SkiRunAutoPlanner.BBox(region, out float2 rbMin, out float2 rbMax);
            var rb = new Bounds2(rbMin, rbMax);
            float2 size = rb.max - rb.min;
            m_ContourCell = math.max(math.max(size.x, size.y) / (ContourSide - 1), 1f);
            m_ContourOriginX = rb.min.x;
            m_ContourOriginZ = rb.min.y;
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            for (int j = 0; j < ContourSide; j++)
            {
                for (int i = 0; i < ContourSide; i++)
                {
                    float x = m_ContourOriginX + i * m_ContourCell, z = m_ContourOriginZ + j * m_ContourCell;
                    float h = m_Snap.Sample(new float3(x, 0f, z));
                    m_ContourHeights[j * ContourSide + i] = h;
                    if (math.isfinite(h) && GradeRaster.Inside(region, new float2(x, z)))
                    {
                        lo = math.min(lo, h);
                        hi = math.max(hi, h);
                    }
                }
            }
            if (!math.isfinite(lo))
            {
                for (int i = 0; i < region.Length; i++)
                {
                    float h = m_Snap.Sample(new float3(region[i].x, 0f, region[i].y));
                    if (math.isfinite(h))
                    {
                        lo = math.min(lo, h);
                        hi = math.max(hi, h);
                    }
                }
            }
            m_GroundMin = math.isfinite(lo) ? lo : 0f;
            m_GroundMax = math.isfinite(hi) ? hi : 0f;
        }

        private void March()
        {
            m_ContourCount = 0;
            if (float.IsPositiveInfinity(m_TreeLine) || m_TreeLine >= m_GroundMax + QuickRunsRules.TreeLineStepM)
                return;
            m_ContourCount = SnowLineRules.MarchingSquares(m_ContourHeights, ContourSide, m_ContourOriginX, m_ContourOriginZ, m_ContourCell,
                m_TreeLine, m_Contour);
            m_DrawDirty = true;
        }


        // ---------------------------------------------------------------- species

        /// <summary>Every tree prefab with wood (TreeData.m_WoodAmount > 0; bushes and placeholders out), vanilla and assets, grouped by
        /// SkiGladeSpecies.GroupOf; sorted by name. Once per load (prefabs do not change mid-game).</summary>
        private void ResolveSpecies()
        {
            if (m_SpeciesResolved)
                return;
            m_SpeciesResolved = true;
            m_Species.Clear();
            using (var prefabs = m_TreePrefabQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < prefabs.Length; i++)
                {
                    Entity e = prefabs[i];
                    if (EntityManager.HasComponent<PlaceholderObjectData>(e))
                        continue;
                    TreeData td = EntityManager.GetComponentData<TreeData>(e);
                    if (!(td.m_WoodAmount > 0f))
                        continue;
                    if (!m_PrefabSystem.TryGetPrefab(e, out PrefabBase prefab) || prefab == null)
                        continue;
                    float offsetY = EntityManager.TryGetComponent(e, out PlaceableObjectData placeable) ? placeable.m_PlacementOffset.y : 0f;
                    m_Species.Add(new SpeciesEntry
                    {
                        Name = prefab.name, Prefab = e, OffsetY = offsetY, Group = SkiGladeSpecies.GroupOf(prefab.name, td.m_WoodAmount),
                    });
                }
            }
            m_Species.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            BuildPool();
            int conifer = 0, deciduous = 0, palm = 0;
            foreach (SpeciesEntry s in m_Species)
            {
                if (s.Group == TreeGroup.Conifer) conifer++;
                else if (s.Group == TreeGroup.Deciduous) deciduous++;
                else if (s.Group == TreeGroup.Palm) palm++;
            }
            Mod.Log.Info($"QuickRuns [trees]: {m_Species.Count} tree species with wood (conifer {conifer}, deciduous {deciduous}, palm {palm}, " +
                         $"other {m_Species.Count - conifer - deciduous - palm})");
            Bump();
        }

        private void BuildPool()
        {
            m_Pool.Clear();
            if (m_SpeciesPick >= 0 && m_SpeciesPick < m_Species.Count)
            {
                m_Pool.Add(m_SpeciesPick);
                return;
            }
            for (int i = 0; i < m_Species.Count; i++)
            {
                TreeGroup g = m_Species[i].Group;
                bool take = m_TreeGroup == GroupAll ? g != TreeGroup.Bush
                    : m_TreeGroup == GroupConifer ? g == TreeGroup.Conifer
                    : m_TreeGroup == GroupDeciduous ? g == TreeGroup.Deciduous
                    : g == TreeGroup.Palm;
                if (take)
                    m_Pool.Add(i);
            }
        }

        private string SpeciesText()
        {
            if (m_SpeciesPick >= 0 && m_SpeciesPick < m_Species.Count)
                return "species " + m_Species[m_SpeciesPick].Name;
            string g = m_TreeGroup == GroupConifer ? "conifer" : m_TreeGroup == GroupDeciduous ? "deciduous" : m_TreeGroup == GroupPalm ? "palm" : "all trees";
            return $"group {g} ({m_Pool.Count} species)";
        }

        private void ClearTreeBatch(string why)
        {
            m_TreeBatch.Clear();
            m_TreeBatchFresh = false;
            m_TreeUndoStage = 0;
            m_Removing = false;
            m_RemoveCursor = 0;
            if (why == "load")
            {
                m_TreeSkip.Clear();
                m_TreeCache.Clear();
            }
        }
    }
}
