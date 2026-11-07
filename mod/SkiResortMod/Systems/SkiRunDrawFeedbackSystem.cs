// Slope feedback while a ski run is drawn or edited with the vanilla area tool. It draws a heat-map of slope bands inside the Temp
// polygon and a white ring on the crux cell. It also publishes a Readout that SkiRunTooltipSystem shows. The rating is SkiRunRater
// (shared with the hub's rating cache). This system keeps the heights-ready check, ping-pong grids, step hysteresis and everything
// after Classify, and only publishes. Phase: UpdateBefore OverlayRenderSystem in Rendering (SystemOrder.cs:687). Citations are 1.6.0f1
// decompile. Its lists are cleared each frame, so draw every frame. Source of truth = the Temp Area entity. Grade comes from a
// world-anchored height lattice, never triangle planes. GetHeightData is read fresh per recompute, never cached (TerrainSystem.cs:2603,
// 3538-3552). StripDrawJob reads no ECS data: combine only the overlay dependencies, never base.Dependency. Game mode only. Read-only
// on the ECS side.
// Engine notes: EngineNotes/SkiRunDrawFeedbackSystem.md

using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Colossal.Mathematics;              // Line3
using Colossal.Serialization.Entities;   // Purpose
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunDrawFeedbackSystem : GameSystemBase
    {
        /// <summary>What the tooltip shows. Band is the display band (with SkiSlopeClassifier.DisplayHysteresis while the same Temp is
        /// live) of RatingGrade. RatingGrade = the crux of the easiest line (ring mode) or the k-th largest 20 m window (polyline mode).
        /// SteepestGrade = "the steepest patch, avoidable or not" (equal to RatingGrade in polyline mode, where a line has one path).
        /// Connected = false when the run's Mask split into parts and the rating used P90 instead. Grades are fractions (0.15 = 15 %), Drop in
        /// meters. Length = SlopeResult.Length, horizontal meters. GateInRange: new run = a live sign within the tether of the Temp's
        /// center. Edited run = ownership.IsBound(Temp.m_Original).</summary>
        public struct Readout
        {
            public bool Valid;
            public SlopeStatus Status;
            public SlopeBand Band;
            public float RatingGrade, MeanGrade, Drop;
            public float SteepestGrade;
            public bool Connected;
            public bool IsNewRun;
            public bool GateInRange;
            public float Length;
            // The fee inputs. The system sets them at every recompute whether or not the rating succeeds, and to -1 / 0 after Invalidate.
            public int Kind;          // SkiRunOwnershipSystem.KindOf(the Temp's prefab) = RunKind, -1 unknown
            public float AreaM2;      // plan area of the Temp's nodes (SkiRunRater.ShoelaceArea)
        }

        /// <summary>All tuning constants. Every one is logged in the per-load ARMED line.</summary>
        public static class Tuning
        {
            /// <summary>Minimum frames between two recomputes while nodes keep moving.</summary>
            public const int MinRecomputeFrames = 3;
            /// <summary>Seconds (wall clock) after which the last good grid's heights are re-verified (VerifyStep). It is also the maximum age of a
            /// reused height (engine notes, Recompute policy).</summary>
            public const double HeartbeatSeconds = 2.0;
            /// <summary>Need nodes re-sampled per verify frame (bit-exact compare).</summary>
            public const int VerifyNodesPerFrame = 4096;
            /// <summary>A node counts as moved when its xz moved at least this far (m).</summary>
            public const float NodeMoveEpsilonM = 0.1f;
            /// <summary>Duty-cycle throttle: a movement-only recompute may not start sooner than ThrottleFactor x the last recompute's ms
            /// after the previous one started. The next constant caps this delay.</summary>
            public const float ThrottleFactor = 8f;
            /// <summary>The cap of the throttle delay above, in ms.</summary>
            public const float MaxThrottleMs = 250f;
            /// <summary>Overlay fill alpha (inside the polygon only).</summary>
            public const float FillAlpha = 0.35f;
            /// <summary>Hard cap on Projected strips per frame (GPU overdraw, see OVERLAY).</summary>
            public const int MaxStrips = 3000;
            /// <summary>Width (m) of a polyline-mode segment strip.</summary>
            public const float PolylineWidthM = 4f;
            /// <summary>Below this heightmap width the rating is skipped.</summary>
            public const int MinHeightmapResolution = 4096;
            /// <summary>Log one WARN per load when a recompute exceeds this (4.0-5.0 ms under Mono).</summary>
            public const float SlowRecomputeWarnMs = 4f;
            /// <summary>Largest ring-mode row stride. When full row resolution exceeds MaxStrips, the smallest stride 2..MaxRowStride that
            /// fits draws one representative row per group of rows. The list is truncated only if this stride overflows.</summary>
            public const int MaxRowStride = 8;
            /// <summary>Log one WARN per load when the per-frame draw (GetBuffer + job schedule + AddBufferWriter) exceeds this.</summary>
            public const double SlowDrawWarnMs = 2.0;
            /// <summary>Crux ring diameter = max(CruxRingMinDiameterM, CruxRingStepFactor x step).</summary>
            public const float CruxRingMinDiameterM = 14f;
            public const float CruxRingStepFactor = 3f;
            /// <summary>Crux ring outline width passed to DrawCircle (meters assumed, unverified, see OVERLAY).</summary>
            public const float CruxRingOutlineM = 1f;
            /// <summary>At most one `RunFeedback [validation]` line per this many wall-clock seconds. The code folds a burst of changes into one
            /// summary (the LogWindow rule).</summary>
            public const float ValidationLogWindowS = 10f;
        }

        /// <summary>Band colors, index = (int)SlopeBand, alpha = FillAlpha.</summary>
        private static readonly UnityEngine.Color[] BandColors =
        {
            new UnityEngine.Color(0.60f, 0.60f, 0.60f, Tuning.FillAlpha),
            new UnityEngine.Color(0.20f, 0.75f, 0.30f, Tuning.FillAlpha),
            new UnityEngine.Color(0.20f, 0.45f, 0.95f, Tuning.FillAlpha),
            new UnityEngine.Color(0.08f, 0.08f, 0.08f, Tuning.FillAlpha),
            new UnityEngine.Color(0.85f, 0.20f, 0.10f, Tuning.FillAlpha),
        };

        /// <summary>Crux ring: white outline, no fill (fill = outline with alpha 0, the vanilla
        /// range-circle style, EffectRangeRenderSystem.cs:166-169).</summary>
        private static readonly UnityEngine.Color CruxOutlineColor = new UnityEngine.Color(1f, 1f, 1f, 0.9f);
        private static readonly UnityEngine.Color CruxFillColor = new UnityEngine.Color(1f, 1f, 1f, 0f);

        /// <summary>The tooltip's input. Returns true only when all of these hold as of this system's last Rendering-phase update (the
        /// previous frame for the UITooltip phase):
        /// - an eligible Temp run existed
        /// - the feature is on
        /// - the rating is usable (<c>readout.Valid</c>)
        /// The out value is always filled (Valid=false otherwise), so a caller may still read Status.</summary>
        public bool TryGetReadout(out Readout readout)
        {
            readout = m_Readout;
            var settings = Mod.Settings;
            if (!m_ReadoutLive || m_Disabled || !m_Armed || settings == null ||
                !settings.ShowRunGradeWhileDrawing)
            {
                readout.Valid = false;
                return false;
            }
            // Stale guard: the readout belongs to the last or current frame only. Rendering runs
            // after UIUpdate (SystemOrder.cs:67, 68), so a reader in UITooltip sees last frame's.
            if (UnityEngine.Time.frameCount - m_ReadoutFrame > 1)
            {
                readout.Valid = false;
                return false;
            }
            return m_Readout.Valid;
        }

        /// <summary>The fee inputs of the run being drawn. They are live and fresh exactly like the readout (same gates, same one-frame lag).
        /// They are independent of whether the rating succeeded (a too-large or no-terrain run still shows its fee).</summary>
        public bool TryGetFeeInputs(out int kind, out float areaM2, out bool isNewRun)
        {
            kind = m_Readout.Kind;
            areaM2 = m_Readout.AreaM2;
            isNewRun = m_Readout.IsNewRun;
            var settings = Mod.Settings;
            return m_ReadoutLive && !m_Disabled && m_Armed && settings != null && settings.ShowRunGradeWhileDrawing &&
                   UnityEngine.Time.frameCount - m_ReadoutFrame <= 1 && kind >= 0;
        }

        // Plain public fields for the dev tab (cheap getters). Only OnUpdate / the dump
        // button write them.
        public bool AreaToolActive;
        public Entity EligibleTemp;
        public int EligibleNodes;
        public bool EligibleIsNew;
        public bool EligibleComplete;
        public int TempCandidates;
        public SlopeStatus LastStatus;
        /// <summary>Constant strings only (no per-frame formatting): why there is or is not a
        /// rating right now.</summary>
        public string LastNote = "-";
        public SlopeBand RawBand;
        public float LastPlanMs, LastFillMs, LastClassifyMs, LastStripMs, LastRecomputeMs;
        public float PeakRecomputeMs;
        public int StepMultiple = 1;
        public float StepM;
        public int InsideCells, NeedCells, LatticeNx, LatticeNz;
        public int StripsBuilt, StripsDrawn;
        public bool StripCapHit;
        /// <summary>Ring-mode row stride of the current strips (1 = full row resolution).</summary>
        public int RowStride = 1;
        /// <summary>Per-frame draw cost, ms (GetBuffer + job schedule + AddBufferWriter). PeakDrawMs (reset per load) excludes the first
        /// draw of the PROCESS. FirstDrawMs reports it (not reset per load: the JIT it pays happens once per process).</summary>
        public double LastDrawMs, PeakDrawMs, FirstDrawMs;
        public long Recomputes;
        public string LastError = "-";
        /// <summary>P90 of the per-cell values (the disconnected-mask fallback rating).</summary>
        public float P90Grade;
        /// <summary>End-zone sizes in cells (top / bottom).</summary>
        public int TopZoneCells, BottomZoneCells;
        /// <summary>The last rated recompute's end-zone key (true = position along the mean fall line, false = node height). Also its
        /// connectivity rule (true = the diagonal rule, set here for step multiple >= 2).</summary>
        public bool ZoneByFallLine, DiagonalRule;
        /// <summary>The key gate's two inputs of the last rated ring recompute: |mean box gradient| (vs EndZoneMinMeanGrade) and
        /// FallCoherence (vs EndZoneMinCoherence). 0 in polyline mode.</summary>
        public float ZoneMeanGradient, ZoneFallCoherence;
        /// <summary>Crux cell (grid-local) of the last rated recompute. False in polyline mode.</summary>
        public bool CruxValid;
        public int CruxI, CruxJ;
        /// <summary>Last fill: Need heights copied from the previous grid / sampled from the terrain.</summary>
        public int HeightsReused, HeightsSampled;
        /// <summary>Current throttle window (ms) and, on a frame where a movement recompute is
        /// being deferred, the ms left until it may start (0 otherwise).</summary>
        public float ThrottleWindowMs, ThrottleRemainingMs;
        /// <summary>Movement recomputes deferred by the throttle since the load (frames).</summary>
        public long ThrottledFrames;
        /// <summary>JIT pre-warm duration of this load (ms).</summary>
        public float PrewarmMs;
        /// <summary>Full path of the last capture file written by "Dump to log", or a reason.</summary>
        public string LastCapturePath = "-";
        /// <summary>The last rated recompute's length split (m): equivalent-rectangle and
        /// fall-line extent (Readout.Length is their max in ring mode, the arc in polyline mode).</summary>
        public float LengthRect, AxisLength;

        /// <summary>The last recompute's NEW run read GateInRange false because ownership cannot bind new runs this load
        /// (ownership.BindsNewRuns false), not because of the distance. Reset at load and on every UpdateGateInRange.</summary>
        public bool NewRunBindingOff;
        /// <summary>Heartbeat verify: complete passes that confirmed the heights, passes that found a changed height (each forced a full
        /// resample), Need nodes compared on the last verify frame, and its / the peak cost (ms, Stopwatch timestamps, reset per
        /// load).</summary>
        public long Verifies, VerifyChanges;
        public int VerifyNodesLast;
        public float LastVerifyMs, PeakVerifyMs;
        /// <summary>Wall-clock time (realtimeSinceStartupAsDouble) the last good grid's heights
        /// were last known current. NegativeInfinity = never (or invalidated by a mismatch).
        /// Age = now − this (dev tab, DUMP).</summary>
        public double GoodVerifiedAt => m_GoodVerifiedAt;
        /// <summary>True between this load and its first rated recompute (DUMP: "rating: none
        /// since load").</summary>
        public bool NoRatingSinceLoad => m_GoodGrid == null;
        /// <summary>The game's verdict on the run being drawn, as of the last recompute ("-" while no run is on the cursor or none was
        /// read, "clear" = nothing reported). Also the changes logged (offered to the LogWindow) since the load. Written on a change only
        /// (event-time text).</summary>
        public string ValidationText = "-";
        public long ValidationChanges;
        public bool IsArmed => m_Armed;
        public bool IsDisabled => m_Disabled;
        /// <summary>The current readout regardless of liveness (dev tab only).</summary>
        public Readout CurrentReadout => m_Readout;
        public bool ReadoutLive => m_ReadoutLive;

        /// <summary>One Projected strip: segment a -> b, width (m), color.</summary>
        private struct Strip
        {
            public float3 A, B;
            public float Width;
            public UnityEngine.Color Color;
        }

        /// <summary>Writes Count strips and the optional crux ring into the overlay buffer.
        /// Field layout follows AreaBorderRenderSystem.cs:40-41, 97 (the Buffer field carries no
        /// attribute). Reads only our own NativeArray. No ECS data.</summary>
        [BurstCompile]
        private struct StripDrawJob : IJob
        {
            [ReadOnly] public NativeArray<Strip> Strips;
            public int Count;
            public bool DrawCrux;
            public float3 CruxPosition;
            public float CruxDiameter;
            public float CruxOutlineWidth;
            public UnityEngine.Color CruxOutline;
            public UnityEngine.Color CruxFill;
            public OverlayRenderSystem.Buffer Overlay;

            public void Execute()
            {
                var flags = OverlayRenderSystem.StyleFlags.Projected;
                var roundness = new float2(0f, 0f);
                int n = Count < Strips.Length ? Count : Strips.Length;
                for (int s = 0; s < n; s++)
                {
                    Strip st = Strips[s];
                    Overlay.DrawLine(st.Color, st.Color, 0f, flags, new Line3.Segment(st.A, st.B),
                        st.Width, roundness);   // OverlayRenderSystem.cs:207
                }
                if (DrawCrux)
                {
                    Overlay.DrawCircle(CruxOutline, CruxFill, CruxOutlineWidth, flags,
                        new float2(0f, 1f), CruxPosition, CruxDiameter);   // OverlayRenderSystem.cs:138
                }
            }
        }

        private ToolSystem m_ToolSystem;
        private AreaToolSystem m_AreaTool;
        private TerrainSystem m_TerrainSystem;
        private OverlayRenderSystem m_Overlay;
        private PrefabSystem m_PrefabSystem;
        private SkiRunOwnershipSystem m_Ownership;

        private EntityQuery m_TempQuery;

        // Per-load state.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_CapLogged;
        private bool m_SlowLogged;
        private bool m_HeightsLogged;
        private bool m_StrideLogged;
        private bool m_SlowDrawLogged;

        // Step 3 cache: AreaGeometryData.m_Type of the tool's prefab, re-read on reference change.
        private AreaPrefab m_CachedToolPrefab;
        private bool m_CachedToolPrefabIsSurface;

        // Frame counter (this system's updates, which run every frame, idle included) for the recompute policy (MinRecomputeFrames).
        private int m_Frame;
        private int m_LastRecomputeFrame;
        private bool m_RetryPending;          // heights not ready: retry after MinRecomputeFrames

        // Duty-cycle throttle (measure 2): earliest wall-clock time a movement recompute may start.
        private double m_NextMoveRecomputeTime;

        // Snapshot of the Temp the cached result was computed from.
        private Entity m_CachedTemp = Entity.Null;
        private int m_CachedCount;
        private bool m_CachedComplete;
        private float3[] m_CachedNodes = new float3[64];

        // This frame's node copy (reused).
        private float3[] m_Nodes = new float3[64];

        // Classifier I/O (reused, buffers grow, never shrink). Two grids ping-pong (measure 3):
        // m_Grid = the grid of the latest recompute (what BuildStrips and the dev tab read).
        // m_GoodGrid = the last FULLY FILLED and RATED grid (reuse source and capture source). The system never
        // plans into it, so it survives failed recomputes and Invalidate.
        private readonly SlopeGrid m_GridA = new SlopeGrid();
        private readonly SlopeGrid m_GridB = new SlopeGrid();
        private SlopeGrid m_Grid;
        private SlopeGrid m_GoodGrid;
        private readonly SlopeResult m_Result = new SlopeResult();

        // Heartbeat verify. m_GoodVerifiedAt = wall-clock time at which every height of m_GoodGrid was last known current:
        // - a full resample: its start
        // - a reuse recompute: inherited
        // - a complete verify pass: the pass's start
        // - a mismatch or a load: NegativeInfinity, so the reuse guard fails until a full resample
        // The cursor walks m_GoodGrid's Need runs: row j, run k within the row, column i within the run. m_VerifyRow < 0 = no pass in
        // progress. Reset whenever m_GoodGrid is swapped.
        // m_GoodStale = a pass found a changed height in m_GoodGrid. The system never verifies or reuses it again, only replaces it. So a Temp
        // that keeps failing to rate cannot alternate mismatch -> recompute -> mismatch every other frame against an old grid.
        private double m_GoodVerifiedAt = double.NegativeInfinity;
        private bool m_ForceFullResample;
        private bool m_GoodStale;
        private int m_VerifyRow = -1, m_VerifyRun, m_VerifyCol;
        private double m_VerifyPassStart;
        private int m_StepMultiple = 1;

        // Display band hysteresis (reset when the Temp entity changes).
        private bool m_ShownBandValid;
        private SlopeBand m_ShownBand;

        private Readout m_Readout;
        private bool m_ReadoutLive;
        private int m_ReadoutFrame = int.MinValue / 2;
        private Entity m_EligiblePrefab = Entity.Null; // the chosen Temp's prefab (the fee's kind)

        // Strips: built into a managed stage, then copied once into the persistent NativeArray
        // the draw job reads (allocated once, never grown).
        private readonly Strip[] m_StripStage = new Strip[Tuning.MaxStrips];
        private NativeArray<Strip> m_Strips;
        private int m_StripCount;
        private JobHandle m_DrawHandle;
        // Process-wide (one-time JIT of the draw-job schedule, see Draw): static on purpose.
        private static bool s_FirstDrawDone;

        // Crux ring (world position fixed at recompute).
        private bool m_CruxDraw;
        private float3 m_CruxPos;
        private float m_CruxDiameter;

        // Capture snapshot of the last rated polygon (m_GoodGrid holds its lattice + heights).
        private bool m_CapValid;
        private float3[] m_CapNodes = new float3[64];
        private int m_CapNodeCount;
        private float m_CapTexel;
        private int m_CapMultiple;
        private float m_CapRating, m_CapSteepest, m_CapMean, m_CapDrop;
        private SlopeBand m_CapBand;

        // The eligible Temp's validation state at the last recompute. Masks = bit (int)Game.Tools.ErrorType of each icon by priority.
        // Tags = ValTag*. m_ValSession = a state was read since the last Invalidate (a run is on the cursor). m_ValOff = the watch threw
        // this load.
        private const byte ValTagError = 1;     // the Temp carries Game.Tools.Error
        private const byte ValTagWarning = 2;   // the Temp carries Game.Tools.Warning (another Temp's error names it)
        private const byte ValTagOther = 4;     // an icon without a readable ToolErrorData type
        private bool m_ValSession;
        private bool m_ValOff;
        private uint m_ValErrors, m_ValWarnings, m_ValInfos;
        private byte m_ValTags;
        private uint m_ValFoldErrors, m_ValFoldWarnings, m_ValFoldInfos;   // union over the folded changes
        private readonly LogWindow m_ValLog = new LogWindow(Tuning.ValidationLogWindowS);
        // The other Error carriers' state (engine notes, Validation verdict).
        private EntityQuery m_ErrorQuery;
        private uint m_ValElseErrors;
        private int m_ValElseCount;
        private Entity m_ValElsePrefab;
        private uint m_ValFoldElse;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_AreaTool = World.GetOrCreateSystemManaged<AreaToolSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_Grid = m_GridA;
            m_Strips = new NativeArray<Strip>(Tuning.MaxStrips, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            // All{Area, Temp, Node, PrefabRef, Surface} None{Deleted}. Surface narrows to surface areas (every SurfacePrefab archetype
            // carries Game.Areas.Surface, Game.Prefabs/SurfacePrefab.cs:21-23). No RequireForUpdate: the system must run to invalidate the
            // readout when the tool goes idle.
            m_TempQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            // Register Geometry as a read type of this system so CompleteDependency() also waits
            // for GeometrySystem's 2B job (GeometrySystem.cs:810 base.Dependency). Standard
            // Entities behavior (type handles add reader dependencies). Unverified in Game.dll.
            // EntityManager.GetComponentData completes writers anyway.
            GetComponentTypeHandle<Game.Areas.Geometry>(isReadOnly: true);
            // The tool's own refusal set (ToolBaseSystem.cs:313, m_ErrorQuery). Read on recompute frames only (WatchValidation): one
            // IsEmptyIgnoreFilter, an entity array only when something carries Error.
            m_ErrorQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Tools.Error>());

            Mod.Log.Info($"{nameof(SkiRunDrawFeedbackSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // First, so nothing of the previous city survives into this one's DUMP or dev tab.
            ResetPublishedState();

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_CapLogged = false;
            m_SlowLogged = false;
            m_HeightsLogged = false;
            m_StrideLogged = false;
            m_SlowDrawLogged = false;
            RowStride = 1;
            LastDrawMs = 0.0;
            PeakDrawMs = 0.0;
            m_CachedToolPrefab = null;
            m_CachedToolPrefabIsSurface = false;
            m_StepMultiple = 1;
            StepMultiple = 1;
            PeakRecomputeMs = 0f;
            Recomputes = 0;
            StripCapHit = false;
            LastError = "-";
            // A new map: nothing from the previous load may be reused or captured.
            m_GoodGrid = null;
            m_CapValid = false;
            m_CapNodeCount = 0;
            m_NextMoveRecomputeTime = 0.0;
            HeightsReused = 0;
            HeightsSampled = 0;
            ThrottleWindowMs = 0f;
            ThrottleRemainingMs = 0f;
            ThrottledFrames = 0;
            PrewarmMs = 0f;
            CruxValid = false;
            m_CruxDraw = false;
            Invalidate("-"); // also writes a pending validation fold and ends the session
            m_ValOff = false;
            ValidationChanges = 0;
            m_ValLog.Reset();

            // HARD GATE: Game only (the run prefabs are only bound in game mode).
            if (mode != GameMode.Game)
                return;

            try
            {
                m_Armed = true;
                Mod.Log.Info(
                    "RunFeedback: ARMED — rating v2 + R2b (crux of the easiest line, spec §9 / §9.8: end zones " +
                    "along the mean fall line when |mean g| >= EndZoneMinMeanGrade OR coherence >= " +
                    "EndZoneMinCoherence, else by node height; " +
                    "diagonal rule at step multiple >= 2) heat-map + " +
                    "crux ring + tooltip readout while the vanilla area tool draws/edits a run " +
                    "(Temp area, Rendering before OverlayRenderSystem; strips + ring written by a " +
                    "Burst IJob registered with AddBufferWriter; height reuse over two ping-pong " +
                    "grids while the heights were verified < HeartbeatSeconds ago, full resample " +
                    "on a step/origin change or a verify mismatch; R3: no time-based recompute — " +
                    "a held run's heights are re-verified bit-exact, VerifyNodesPerFrame per frame, " +
                    "once HeartbeatSeconds old (wall clock); readout adds run length = max(equivalent " +
                    "rectangle, fall-line extent), horizontal m; gate in range for an edited run = " +
                    "ownership.IsBound(original)); " +
                    $"MinRecomputeFrames={Tuning.MinRecomputeFrames} HeartbeatSeconds={Tuning.HeartbeatSeconds} " +
                    $"VerifyNodesPerFrame={Tuning.VerifyNodesPerFrame} " +
                    $"NodeMoveEpsilonM={Tuning.NodeMoveEpsilonM} ThrottleFactor={Tuning.ThrottleFactor} " +
                    $"MaxThrottleMs={Tuning.MaxThrottleMs} FillAlpha={Tuning.FillAlpha} " +
                    $"MaxStrips={Tuning.MaxStrips} MaxRowStride={Tuning.MaxRowStride} " +
                    $"PolylineWidthM={Tuning.PolylineWidthM} " +
                    $"MinHeightmapResolution={Tuning.MinHeightmapResolution} " +
                    $"SlowRecomputeWarnMs={Tuning.SlowRecomputeWarnMs} " +
                    $"SlowDrawWarnMs={Tuning.SlowDrawWarnMs} " +
                    $"CruxRingMinDiameterM={Tuning.CruxRingMinDiameterM} " +
                    $"CruxRingStepFactor={Tuning.CruxRingStepFactor} " +
                    $"CruxRingOutlineM={Tuning.CruxRingOutlineM} " +
                    $"ValidationLogWindowS={Tuning.ValidationLogWindowS} (F7: the game's error / warning types on the " +
                    "run being drawn, read on recompute frames, logged once per change); classifier " +
                    $"Green>={SkiSlopeClassifier.GradeGreen} Blue>={SkiSlopeClassifier.GradeBlue} " +
                    $"Black>={SkiSlopeClassifier.GradeBlack} DoubleBlack>={SkiSlopeClassifier.GradeDoubleBlack} " +
                    $"EndZoneShare={SkiSlopeClassifier.EndZoneShare} EndZoneDepthM={SkiSlopeClassifier.EndZoneDepthM} " +
                    $"EndZoneMinMeanGrade={SkiSlopeClassifier.EndZoneMinMeanGrade} EndZoneMinCoherence={SkiSlopeClassifier.EndZoneMinCoherence} " +
                    $"EndZoneMaxM2={SkiSlopeClassifier.EndZoneMaxM2} (height key) " +
                    "zone key=fall line|height (per recompute) diagonal rule=step multiple>=2 " +
                    $"RatingBuckets={SkiSlopeClassifier.RatingBuckets} " +
                    $"WindowLength={SkiSlopeClassifier.WindowLength} RatingAreaM2={SkiSlopeClassifier.RatingAreaM2} " +
                    $"MinRatingCells={SkiSlopeClassifier.MinRatingCells} CellBudget={SkiSlopeClassifier.CellBudget} " +
                    $"Dilation={SkiSlopeClassifier.Dilation} DisplayHysteresis={SkiSlopeClassifier.DisplayHysteresis}; " +
                    "gated on Settings.ShowRunGradeWhileDrawing and the ownership prefab sets being ready " +
                    "(not on binding being armed: a binding exception no longer switches the feedback off; a " +
                    "new run then reads 'gate in range' false while binding is off for new runs)");

                PrewarmJit();
            }
            catch (Exception e)
            {
                HandleException(e, "load setup");
            }
        }

        /// <summary>Clears every PUBLISHED value of the previous load (the readout, the classifier result, the dev-tab / DUMP fields and
        /// the heartbeat state). So a DUMP after a load can never print the previous city's rating. Called FIRST in
        /// OnGameLoadingComplete. Field writes only (m_Result.Clear keeps its buffers).</summary>
        private void ResetPublishedState()
        {
            m_Readout = default(Readout);
            m_Readout.Status = SlopeStatus.NoCells;   // default(SlopeStatus) is Ok — never publish that for "nothing"
            m_Readout.Kind = -1;                      // E1: default 0 would read "groomed" — never publish a fee kind for "nothing"
            m_EligiblePrefab = Entity.Null;
            m_Result.Clear();
            RawBand = SlopeBand.Flat;
            LastStatus = SlopeStatus.NoCells;
            StepM = 0f;
            InsideCells = 0;
            NeedCells = 0;
            LatticeNx = 0;
            LatticeNz = 0;
            LastPlanMs = 0f;
            LastFillMs = 0f;
            LastClassifyMs = 0f;
            LastStripMs = 0f;
            LastRecomputeMs = 0f;
            LastDrawMs = 0.0;
            P90Grade = 0f;
            TopZoneCells = 0;
            BottomZoneCells = 0;
            ZoneByFallLine = false;
            DiagonalRule = false;
            ZoneMeanGradient = 0f;
            ZoneFallCoherence = 0f;
            CruxValid = false;
            CruxI = 0;
            CruxJ = 0;
            EligibleIsNew = false;
            EligibleComplete = false;
            TempCandidates = 0;
            LastCapturePath = "-";
            m_ShownBandValid = false;
            m_CachedCount = 0;
            m_CachedComplete = false;
            m_RetryPending = false;
            m_LastRecomputeFrame = m_Frame;
            StripsBuilt = 0;
            StripsDrawn = 0;
            LengthRect = 0f;
            AxisLength = 0f;
            NewRunBindingOff = false;
            m_GoodVerifiedAt = double.NegativeInfinity;
            m_ForceFullResample = false;
            m_GoodStale = false;
            ResetVerifyCursor();
            Verifies = 0;
            VerifyChanges = 0;
            VerifyNodesLast = 0;
            LastVerifyMs = 0f;
            PeakVerifyMs = 0f;
        }

        protected override void OnUpdate()
        {
            m_Frame++;
            StripsDrawn = 0;
            if (m_Disabled)
                return;
            try
            {
                UpdateCore();
            }
            catch (Exception e)
            {
                HandleException(e, "OnUpdate");
                Invalidate("disabled after an exception");
            }
        }

        protected override void OnStopRunning()
        {
            try
            {
                CompleteDraw(); // engine notes OVERLAY safety (c)
            }
            catch (Exception e)
            {
                HandleException(e, "OnStopRunning");
            }
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            try
            {
                CompleteDraw();   // never dispose m_Strips while a draw job may read it
            }
            catch (Exception e)
            {
                Mod.Log.Error($"!!! {nameof(SkiRunDrawFeedbackSystem)} EXCEPTION completing the draw " +
                              $"job in OnDestroy:\n{e}");
            }
            try
            {
                // quit to desktop raises no load, but the logger still lives here (LogWindow.cs header)
                if (m_ValLog.Count > 0)
                    WriteValidationSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception)
            {
                // teardown must never throw: a folded validation line is dropped
            }
            if (m_Strips.IsCreated)
                m_Strips.Dispose();
            base.OnDestroy();
        }

        /// <summary>Plan + Classify + BuildStrips of a synthetic 40 × 90 m ring and a synthetic 2-node 40 m line on a synthetic plane,
        /// into scratch objects. This pays the Mono JIT cost at load, not on the first real recompute. No terrain, overlay or ECS access.
        /// The strip stage is reset afterwards (the NativeArray the draw job reads is not touched). Also
        /// IJobExtensions.EarlyJobInit&lt;StripDrawJob&gt;(): the first job.Schedule of a process otherwise creates the job's reflection
        /// data and JIT-compiles JobStruct&lt;StripDrawJob&gt; inside the first Draw's timing window. EarlyJobInit calls
        /// JobStruct&lt;T&gt;.Initialize(), which calls JobsUtility.CreateJobReflectionData only while the SharedStatic
        /// jobReflectionData is IntPtr.Zero. Read with Cecil from UnityEngine.CoreModule. Process-wide, so later loads are already
        /// warm.</summary>
        private void PrewarmJit()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            IJobExtensions.EarlyJobInit<StripDrawJob>();
            var grid = new SlopeGrid();
            var result = new SlopeResult();
            var pts = new[]
            {
                new float3(100f, 0f, 100f),
                new float3(140f, 0f, 100f),
                new float3(140f, 0f, 190f),
                new float3(100f, 0f, 190f),
            };
            var origin = new double2(0.0, 0.0);
            const float step = 3.5f;

            SlopeStatus ring = SkiSlopeClassifier.Plan(pts, pts.Length, origin, step, grid);
            if (IsPlanOk(ring))
            {
                FillSynthetic(grid);
                ring = SkiSlopeClassifier.Classify(grid, result);
                if (IsRated(ring))
                    BuildStrips(grid, result);
            }
            SlopeStatus line = SkiSlopeClassifier.Plan(pts, 2, origin, step, grid);
            if (IsPlanOk(line))
            {
                FillSynthetic(grid);
                line = SkiSlopeClassifier.Classify(grid, result);
                if (IsRated(line))
                    BuildStrips(grid, result);
            }
            // Leave no trace in the live state (the stage is rebuilt before every publish).
            m_StripCount = 0;
            StripsBuilt = 0;
            RowStride = 1;
            StripCapHit = false;

            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            PrewarmMs = (float)((t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            Mod.Log.Info($"RunFeedback: JIT pre-warm {PrewarmMs:F2} ms (synthetic ring {ring}, " +
                         $"synthetic line {line}; Plan + Classify + strips into scratch objects, no " +
                         "terrain access; logged once per load)");
        }

        /// <summary>Synthetic heights for the pre-warm: a 20 % / 5 % plane at every Need node.</summary>
        private static void FillSynthetic(SlopeGrid g)
        {
            int nx = g.Nx, nz = g.Nz, n = nx * nz;
            if (g.Heights == null || g.Heights.Length < n || g.Need == null || g.Need.Length < n)
                return;
            for (int j = 0; j < nz; j++)
            {
                double wz = g.Origin.y + (g.J0 + j) * (double)g.Step;
                for (int i = 0; i < nx; i++)
                {
                    int q = j * nx + i;
                    if (g.Need[q] == 0)
                        continue;
                    double wx = g.Origin.x + (g.I0 + i) * (double)g.Step;
                    g.Heights[q] = (float)(0.20 * wz + 0.05 * wx);
                }
            }
        }

        private void UpdateCore()
        {
            // 1. Feature switch and arming (ours: Game mode. Ownership: its prefab sets resolved, because IsRunPrefab is meaningless
            // otherwise). Gated on ownership.PrefabSetsReady, NOT ownership.IsArmed: a load-pass or sweep exception disables binding
            // (IsArmed false), and that must not also switch off the slope feedback. The feedback needs only the prefab sets (IsRunPrefab) and the
            // binding-independent helpers (IsBound, TryNearestGate). The system reports separately whether NEW runs can bind this load
            // (UpdateGateInRange, ownership.BindsNewRuns).
            var settings = Mod.Settings;
            if (!m_Armed || settings == null || !settings.ShowRunGradeWhileDrawing ||
                m_Ownership == null || !m_Ownership.PrefabSetsReady)
            {
                AreaToolActive = false;
                Invalidate("off (setting, not armed, or ownership prefab sets not ready)");
                return;
            }

            // 2. Area tool active (plain field getter, ToolSystem.cs:84-99).
            if (m_ToolSystem.activeTool != m_AreaTool)
            {
                AreaToolActive = false;
                Invalidate("area tool not active");
                return;
            }
            AreaToolActive = true;

            // 3. Surface prefab selected (AreaToolSystem.prefab, :2637-2652, 1.6.2 :2648). Only Surface prefabs create or snap to our runs
            // (AreaToolSystem.cs:320-322). SkiRunAreaTypeSystem forges the cat-track prefab's m_Type Surface -> Space at every load. So
            // "Surface" alone rejected it, and the heat map / fee row / "too steep for a cat track" line never showed. The gate therefore
            // accepts Surface OR a prefab entity in our run-prefab set (the set the Temp filter uses in PickEligibleTemp). The Temp query
            // keeps its own Surface requirement.
            AreaPrefab toolPrefab = m_AreaTool.prefab;
            if (toolPrefab == null)
            {
                Invalidate("area tool has no prefab");
                return;
            }
            if (!ReferenceEquals(toolPrefab, m_CachedToolPrefab))
            {
                m_CachedToolPrefab = toolPrefab;
                m_CachedToolPrefabIsSurface =
                    m_PrefabSystem.TryGetComponentData(toolPrefab, out AreaGeometryData geo) &&
                    geo.m_Type == Game.Areas.AreaType.Surface;   // PrefabSystem.cs:730
                if (!m_CachedToolPrefabIsSurface &&
                    m_PrefabSystem.TryGetEntity(toolPrefab, out Entity toolPrefabEntity) &&
                    m_Ownership.IsRunPrefab(toolPrefabEntity))
                    m_CachedToolPrefabIsSurface = true; // the Space-forged cat track
            }
            if (!m_CachedToolPrefabIsSurface)
            {
                Invalidate("area tool prefab is not a Surface");
                return;
            }

            // 4. Any Temp surface at all.
            if (m_TempQuery.IsEmptyIgnoreFilter)
            {
                Invalidate("no Temp surface");
                return;
            }

            // 5. Complete the writers (GenerateAreasSystem 1, GeometrySystem 2B), pick the Temp.
            CompleteDependency();
            Entity temp = PickEligibleTemp(out int nodeCount, out Temp tempData, out bool complete);
            if (temp == Entity.Null)
            {
                // Never assert: mid-draw over UI the Temp can vanish.
                Invalidate("no eligible Temp run (>= 2 nodes, ours, not Delete/Hidden)");
                return;
            }
            EligibleTemp = temp;
            EligibleNodes = nodeCount;
            EligibleIsNew = tempData.m_Original == Entity.Null;
            EligibleComplete = complete;

            // 6. Recompute decision (duty-cycle throttle on movement-only recomputes). On a frame WITHOUT a recompute, the heartbeat verify
            // runs, only here, on the eligible-Temp path. m_GoodGrid survives Invalidate (capture), so an ungated verify would sample terrain in
            // normal play.
            if (NeedsRecompute(temp, nodeCount, complete))
            {
                Recompute(temp, nodeCount, complete, tempData);
                WatchValidation(temp, nodeCount, complete, EligibleIsNew); // recompute frames only
            }
            else
            {
                VerifyStep();
                FlushValidationFold(); // a fold waiting while the cursor rests (one int test when none)
            }

            // Readout is live for this frame (Valid says whether the rating is usable).
            m_ReadoutLive = true;
            m_ReadoutFrame = UnityEngine.Time.frameCount;

            // 8. Draw from the cached strips every frame (Burst job).
            Draw();
        }

        /// <summary>First eligible Temp with the most nodes. Copies its nodes into m_Nodes.</summary>
        private Entity PickEligibleTemp(out int nodeCount, out Temp tempData, out bool complete)
        {
            nodeCount = 0;
            tempData = default(Temp);
            complete = false;
            Entity best = Entity.Null;
            m_EligiblePrefab = Entity.Null;
            int candidates = 0;

            NativeArray<Entity> entities = m_TempQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int k = 0; k < entities.Length; k++)
                {
                    Entity e = entities[k];
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!m_Ownership.IsRunPrefab(prefab))
                        continue;
                    Temp t = EntityManager.GetComponentData<Temp>(e);
                    if ((t.m_Flags & (TempFlags.Delete | TempFlags.Hidden)) != 0)
                        continue;
                    DynamicBuffer<Game.Areas.Node> nodes =
                        EntityManager.GetBuffer<Game.Areas.Node>(e, isReadOnly: true);
                    int n = nodes.Length;
                    if (n < 2)
                        continue;   // 1-node cursor preview (AreaToolSystem.cs:1817-1893)
                    candidates++;
                    if (n <= nodeCount)
                        continue;   // ties: the first one wins
                    best = e;
                    m_EligiblePrefab = prefab; // the fee's kind (KindOf at the recompute)
                    nodeCount = n;
                    tempData = t;
                    complete = (EntityManager.GetComponentData<Game.Areas.Area>(e).m_Flags &
                                Game.Areas.AreaFlags.Complete) != 0;
                    EnsureCapacity(ref m_Nodes, n);
                    for (int i = 0; i < n; i++)
                        m_Nodes[i] = nodes[i].m_Position;
                }
            }
            finally
            {
                entities.Dispose();
            }
            TempCandidates = candidates;
            return best;
        }

        /// <summary>Immediate: node count / Complete / entity change, a verify mismatch (m_ForceFullResample). A heights-not-ready retry
        /// after MinRecomputeFrames. Movement needs MinRecomputeFrames AND the duty-cycle window (engine notes, Recompute policy). The moved
        /// test compares against the last recompute's snapshot. No time-based recompute.</summary>
        private bool NeedsRecompute(Entity temp, int nodeCount, bool complete)
        {
            ThrottleRemainingMs = 0f;
            int since = m_Frame - m_LastRecomputeFrame;
            if (temp != m_CachedTemp || nodeCount != m_CachedCount || complete != m_CachedComplete)
                return true;
            if (m_ForceFullResample)
                return true;
            if (since < Tuning.MinRecomputeFrames)
                return false;
            if (m_RetryPending)
                return true;
            const float eps2 = Tuning.NodeMoveEpsilonM * Tuning.NodeMoveEpsilonM;
            bool moved = false;
            for (int i = 0; i < nodeCount; i++)
            {
                if (math.distancesq(m_Nodes[i].xz, m_CachedNodes[i].xz) >= eps2)
                {
                    moved = true;
                    break;
                }
            }
            if (!moved)
                return false;
            double wait = m_NextMoveRecomputeTime - UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (wait > 0.0)
            {
                ThrottleRemainingMs = (float)(wait * 1000.0);
                ThrottledFrames++;
                return false;
            }
            return true;
        }

        private void Recompute(Entity temp, int nodeCount, bool complete, Temp tempData)
        {
            bool tempChanged = temp != m_CachedTemp;
            if (tempChanged)
            {
                m_ShownBandValid = false;   // hysteresis only while the same Temp is live
                m_StepMultiple = 1;         // step hysteresis restarts per Temp
            }

            // Snapshot first: the decision inputs are consumed even if the rating fails.
            m_CachedTemp = temp;
            m_CachedCount = nodeCount;
            m_CachedComplete = complete;
            EnsureCapacity(ref m_CachedNodes, nodeCount);
            Array.Copy(m_Nodes, m_CachedNodes, nodeCount);
            m_LastRecomputeFrame = m_Frame;
            m_RetryPending = false;
            Recomputes++;
            // A verify mismatch is a one-shot TRIGGER, consumed here whatever this recompute does (a failed Plan must not re-trigger every
            // frame). Instead, the reuse guard guarantees the full resample it asks for: VerifyStep set m_GoodVerifiedAt to
            // NegativeInfinity, which only a full resample re-stamps.
            bool forceFull = m_ForceFullResample;
            m_ForceFullResample = false;

            // Duty-cycle anchor = the START of this recompute. Until the timing below is known, a following movement recompute is not
            // throttled. Also the verification time a full resample stamps on its heights.
            double startTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            m_NextMoveRecomputeTime = startTime;

            bool isNew = tempData.m_Original == Entity.Null;
            m_Readout.IsNewRun = isNew;
            // The fee inputs, set here so they exist even when the heights are not ready and the rating returns early. ShoelaceArea is the
            // rater's public pure helper (plan area). For a simple outline it equals Geometry.m_SurfaceArea (sum of triangle areas), which
            // the bind charges. At the closing click the cursor node sits on the first node (a zero-area edge).
            m_Readout.Kind = m_Ownership.KindOf(m_EligiblePrefab);
            m_Readout.AreaM2 = (float)SkiRunRater.ShoelaceArea(m_Nodes, nodeCount);
            m_Readout.Valid = false;
            m_Readout.Length = 0f;
            LengthRect = 0f;
            AxisLength = 0f;
            m_StripCount = 0;
            StripsBuilt = 0;
            RowStride = 1;
            m_CruxDraw = false;
            CruxValid = false;
            P90Grade = 0f;
            TopZoneCells = 0;
            BottomZoneCells = 0;
            ZoneByFallLine = false;
            DiagonalRule = false;
            ZoneMeanGradient = 0f;
            ZoneFallCoherence = 0f;
            HeightsReused = 0;
            HeightsSampled = 0;

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

            // Fresh every time (TerrainSystem.cs:2603). Never cache across frames (:3538-3552).
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            if (!hd.isCreated || hd.resolution.x < Tuning.MinHeightmapResolution)
            {
                LastNote = "terrain heights not ready (heightmap < 4096 or not created)";
                m_RetryPending = true;
                if (!m_HeightsLogged)
                {
                    m_HeightsLogged = true;
                    Mod.Log.Info($"RunFeedback: terrain heights not usable (created {hd.isCreated}, " +
                                 $"resolution {hd.resolution.x}) — rating skipped, retrying " +
                                 "(logged once per load)");
                }
                UpdateGateInRange(temp, nodeCount, isNew, tempData);
                return;
            }

            float texel = 1f / hd.scale.x;                            // TerrainSystem.cs:2619-2620
            var origin = new double2(-hd.offset.x, -hd.offset.z);    // texel centers, TerrainUtils.cs:35-38

            // Ping-pong: plan into the grid that is NOT the last good one, so the reuse source and the capture stay intact whatever this
            // recompute does.
            SlopeGrid source = m_GoodGrid;
            SlopeGrid target = ReferenceEquals(source, m_GridA) ? m_GridB : m_GridA;
            m_Grid = target;

            // Rating runs in SkiRunRater.Rate (the hub's rating cache uses the same code, and SlopeBatch proves bit parity).
            // What stays here:
            // - the heights-ready check
            // - the ping-pong grids
            // - the step hysteresis (this Temp's m goes in as stepCurrent)
            // - the caller's half of the reuse guard: reuse only from a fully filled + rated grid verified less than HeartbeatSeconds ago.
            //   A verify mismatch and re-entry after an idle gap resample in full.
            // The rater adds the other half (identical Step and Origin).
            SlopeGrid reuseSource = !forceFull && source != null &&
                                    startTime - m_GoodVerifiedAt < Tuning.HeartbeatSeconds
                ? source
                : null;
            var sampler = new TerrainHeightSampler(hd);
            long tRate = System.Diagnostics.Stopwatch.GetTimestamp();
            SkiRunRater.Rate(m_Nodes, nodeCount, origin, ref sampler, texel, m_StepMultiple, target, reuseSource,
                m_Result, out int m, out float fillMs, out float classifyMs, out RateDetail detail);
            long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
            SlopeStatus status = detail.Status;
            bool reuse = detail.ReuseEnabled;
            HeightsReused = detail.Reused;
            HeightsSampled = detail.Sampled;
            if (m != m_StepMultiple && !tempChanged)
                Mod.Log.Info($"RunFeedback: step multiple {m_StepMultiple} → {m} " +
                             $"({m * texel:F1} m cells, polygon {detail.AreaM2:F0} m²)");
            m_StepMultiple = m;
            StepMultiple = m;
            float step = detail.StepM;
            StepM = step;

            InsideCells = target.InsideCells;
            NeedCells = target.NeedCells;
            LatticeNx = target.Nx;
            LatticeNz = target.Nz;
            LastStatus = status;

            if (IsRated(status))
            {
                bool polyline = target.PolylineMode;
                LastNote = "rated";
                RawBand = m_Result.Band;
                m_Readout.Valid = true;
                m_Readout.Status = status;
                m_Readout.RatingGrade = m_Result.RatingGrade;
                // Polyline: one path, so the steepest patch IS the rating, forced here as well so the tooltip's "avoidable" line can never show
                // for a line.
                m_Readout.SteepestGrade = polyline ? m_Result.RatingGrade : m_Result.SteepestGrade;
                m_Readout.Connected = m_Result.Connected;
                m_Readout.MeanGrade = m_Result.MeanGrade;
                m_Readout.Drop = m_Result.Drop;
                // horizontal run length (ring: max(LengthRect, AxisLength), polyline: arc length, EngineNotes/SkiSlopeClassifier.md "RUN LENGTH")
                m_Readout.Length = m_Result.Length;
                LengthRect = m_Result.LengthRect;
                AxisLength = m_Result.AxisLength;
                m_Readout.Band = DisplayBand(m_Result.RatingGrade);
                P90Grade = m_Result.P90Grade;
                TopZoneCells = m_Result.TopZoneCells;
                BottomZoneCells = m_Result.BottomZoneCells;
                ZoneByFallLine = m_Result.ZoneByFallLine;
                DiagonalRule = m_Result.DiagonalRule;
                ZoneMeanGradient = polyline ? 0f : m_Result.MeanGradient;   // internal, same assembly
                ZoneFallCoherence = polyline ? 0f : m_Result.FallCoherence;
                UpdateCrux(polyline);
                BuildStrips(m_Grid, m_Result);   // m_Grid == target: the CURRENT grid after the ping-pong
                PublishStrips();

                // The target is now fully filled and rated: the next reuse source and the capture. A full resample stamps its heights current
                // as of this recompute's START. A reuse recompute INHERITS m_GoodVerifiedAt (the age of the oldest height it copied). The grid
                // changed, so any verify pass restarts.
                if (!reuse)
                    m_GoodVerifiedAt = startTime;
                m_GoodGrid = target;
                m_GoodStale = false;
                ResetVerifyCursor();
                SnapshotCapture(nodeCount, texel, m);
            }
            else
            {
                LastNote = "classifier returned a non-rating status";
                m_Readout.Status = status;
            }
            long t4 = System.Diagnostics.Stopwatch.GetTimestamp();

            UpdateGateInRange(temp, nodeCount, isNew, tempData);

            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            // Plan = heights read + area + StepMultiple + Plan: the part before the rater plus the rater's own plan time.
            LastPlanMs = (float)((tRate - t0) * toMs) + detail.PlanMs;
            LastFillMs = fillMs;
            LastClassifyMs = classifyMs;
            LastStripMs = (float)((t4 - t3) * toMs);
            LastRecomputeMs = (float)((t4 - t0) * toMs);
            if (LastRecomputeMs > PeakRecomputeMs)
                PeakRecomputeMs = LastRecomputeMs;

            // The next movement-only recompute waits ThrottleFactor × this one.
            float window = math.clamp(Tuning.ThrottleFactor * LastRecomputeMs, 0f, Tuning.MaxThrottleMs);
            ThrottleWindowMs = window;
            m_NextMoveRecomputeTime = startTime + window * 0.001;

            if (LastRecomputeMs > Tuning.SlowRecomputeWarnMs && !m_SlowLogged)
            {
                m_SlowLogged = true;
                Mod.Log.Warn($"RunFeedback: slow recompute {LastRecomputeMs:F2} ms (plan " +
                             $"{LastPlanMs:F2} / fill {LastFillMs:F2} / classify {LastClassifyMs:F2} / " +
                             $"strips {LastStripMs:F2}) — {InsideCells} inside / {NeedCells} need cells " +
                             $"at {step:F1} m, heights reused {HeightsReused} / sampled {HeightsSampled}, " +
                             $"status {status} (logged once per load)");
            }
        }

        /// <summary>The two status tests live in SkiRunRater (one definition for the feedback system and the hub's rating
        /// cache).</summary>
        private static bool IsPlanOk(SlopeStatus s)
        {
            return SkiRunRater.IsPlanOk(s);
        }

        private static bool IsRated(SlopeStatus s)
        {
            return SkiRunRater.IsRated(s);
        }

        /// <summary>Band changes only when RatingGrade passes a threshold by &gt;= DisplayHysteresis while the same Temp is live. up =
        /// BandOf(r - H) is the band the rating clears by H. down = BandOf(r + H) is the highest band within H. The shown band is kept
        /// while it lies in [up, down].</summary>
        private SlopeBand DisplayBand(float rating)
        {
            SlopeBand raw = SkiSlopeClassifier.BandOf(rating);
            if (!m_ShownBandValid)
            {
                m_ShownBandValid = true;
                m_ShownBand = raw;
                return raw;
            }
            float h = SkiSlopeClassifier.DisplayHysteresis;
            SlopeBand up = SkiSlopeClassifier.BandOf(rating - h);
            SlopeBand down = SkiSlopeClassifier.BandOf(rating + h);
            if (m_ShownBand < up)
                m_ShownBand = up;
            else if (m_ShownBand > down)
                m_ShownBand = down;
            return m_ShownBand;
        }

        /// <summary>Crux marker: ring mode with CruxValid only. World position of grid-local cell (CruxI, CruxJ) = Origin + (I0 + i, J0 +
        /// j)·Step (the lattice node the cell is centered on). Diameter max(14 m, 3·step).</summary>
        private void UpdateCrux(bool polyline)
        {
            SlopeResult r = m_Result;
            m_CruxDraw = false;
            CruxValid = !polyline && r.CruxValid;
            CruxI = r.CruxI;
            CruxJ = r.CruxJ;
            if (!CruxValid)
                return;
            double s = r.Step;
            float wx = (float)(r.Origin.x + (r.I0 + r.CruxI) * s);
            float wz = (float)(r.Origin.y + (r.J0 + r.CruxJ) * s);
            float wy = math.isfinite(r.CruxHeight) ? r.CruxHeight : 0f;   // y is replaced for Projected (FitBox :300-310)
            m_CruxPos = new float3(wx, wy, wz);
            m_CruxDiameter = math.max(Tuning.CruxRingMinDiameterM, Tuning.CruxRingStepFactor * r.Step);
            m_CruxDraw = math.isfinite(wx) && math.isfinite(wz) && math.isfinite(m_CruxDiameter);
        }

        /// <summary>Records what the capture file needs about the last RATED polygon (its grid is m_GoodGrid): the exact nodes handed to
        /// Plan, texel, multiple and the rated numbers. O(nodes) per rated recompute, no allocation once m_CapNodes has grown.</summary>
        private void SnapshotCapture(int nodeCount, float texel, int multiple)
        {
            EnsureCapacity(ref m_CapNodes, nodeCount);
            Array.Copy(m_Nodes, m_CapNodes, nodeCount);
            m_CapNodeCount = nodeCount;
            m_CapTexel = texel;
            m_CapMultiple = multiple;
            m_CapRating = m_Result.RatingGrade;
            m_CapSteepest = m_Result.SteepestGrade;
            m_CapMean = m_Result.MeanGrade;
            m_CapDrop = m_Result.Drop;
            m_CapBand = m_Result.Band;
            m_CapValid = true;
        }

        /// <summary>New run: a live gate within the tether of the Temp's center (ownership.TryNearestGate, a managed scan, recompute
        /// only). Center = Geometry.m_CenterPosition (valid after 2B, the same point the bind sweep uses, GeometrySystem.cs:509-576) for
        /// &gt;= 3 nodes, else the node mean. Edited run: ownership.IsBound(Temp.m_Original). Called on recomputes only, so an edit Temp
        /// held still keeps the value of its last recompute. While ownership.BindsNewRuns is false a new run reads false whatever the
        /// distance, so the "will stay unbound" warning shows. NewRunBindingOff says why (DUMP), because that warning's text names the
        /// distance, not the cause.</summary>
        private void UpdateGateInRange(Entity temp, int nodeCount, bool isNew, Temp tempData)
        {
            NewRunBindingOff = false;
            if (!isNew)
            {
                Entity original = tempData.m_Original;
                m_Readout.GateInRange = EntityManager.Exists(original) && m_Ownership.IsBound(original);
                return;
            }
            if (!m_Ownership.BindsNewRuns)
            {
                NewRunBindingOff = true;
                m_Readout.GateInRange = false;
                return;
            }
            float2 centre;
            if (nodeCount >= 3 && EntityManager.HasComponent<Game.Areas.Geometry>(temp))
            {
                centre = EntityManager.GetComponentData<Game.Areas.Geometry>(temp).m_CenterPosition.xz;
            }
            else
            {
                float2 sum = float2.zero;
                for (int i = 0; i < nodeCount; i++)
                    sum += m_Nodes[i].xz;
                centre = sum / math.max(1, nodeCount);
            }
            m_Readout.GateInRange = m_Ownership.TryNearestGate(centre, out float _);
        }

        private void ResetVerifyCursor()
        {
            m_VerifyRow = -1;
            m_VerifyRun = -1;
            m_VerifyCol = 0;
            m_VerifyPassStart = 0.0;
        }

        /// <summary>Called only from UpdateCore's eligible-Temp path on a frame with no recompute (idle cost: one clock read and a
        /// compare). Once m_GoodGrid's heights are Tuning.HeartbeatSeconds old, a pass starts and walks m_GoodGrid's Need runs
        /// (SlopeGrid.NeedRuns / NeedRowStart) from a cursor. The pass re-samples at most Tuning.VerifyNodesPerFrame Need nodes per frame on a
        /// fresh GetHeightData() at exactly FillHeights' world point, and compares bit-exact (math.asint).
        /// - A mismatch: force a full resample (m_ForceFullResample triggers the recompute, m_GoodVerifiedAt = NegativeInfinity forbids
        ///   reusing these heights), mark the grid stale, stop.
        /// - A complete pass: m_GoodVerifiedAt = the pass's start.
        /// - A heightmap that is not readable this frame: retry next frame.
        /// - A lattice origin that no longer matches the grid's: force a full resample.
        /// No allocation, no formatting.</summary>
        private void VerifyStep()
        {
            VerifyNodesLast = 0;
            SlopeGrid g = m_GoodGrid;
            if (g == null || m_GoodStale)
                return;
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (m_VerifyRow < 0)
            {
                if (now - m_GoodVerifiedAt < Tuning.HeartbeatSeconds)
                    return;
                m_VerifyRow = 0;
                m_VerifyRun = -1;
                m_VerifyCol = 0;
                m_VerifyPassStart = now;
            }

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();   // fresh, never cached (engine notes TERRAIN READ)
            if (!hd.isCreated || hd.resolution.x < Tuning.MinHeightmapResolution)
                return;   // not readable now: the pass resumes next frame, the age keeps growing

            int nx = g.Nx, nz = g.Nz;
            long total = (long)nx * nz;
            byte[] need = g.Need;
            float[] heights = g.Heights;
            int[] runs = g.NeedRuns, rs = g.NeedRowStart;
            bool lattice = g.Origin.x == -hd.offset.x && g.Origin.y == -hd.offset.z;   // Recompute's origin expression
            bool shape = nx > 0 && nz > 0 && need != null && heights != null && need.Length >= total && heights.Length >= total &&
                         rs != null && rs.Length >= nz + 1 && rs[nz] >= 0 && (rs[nz] == 0 || (runs != null && runs.Length >= 2L * rs[nz]));
            if (!lattice || !shape)
            {
                VerifyMismatch();
                FinishVerifyTiming(t0, 0);
                return;
            }

            double step = g.Step, ox = g.Origin.x, oz = g.Origin.y;
            int budget = Tuning.VerifyNodesPerFrame, done = 0;
            int j = m_VerifyRow, k = m_VerifyRun, i = m_VerifyCol;
            while (j < nz)
            {
                if (k < rs[j])
                {
                    k = rs[j];
                    i = int.MinValue;
                }
                if (k >= rs[j + 1])
                {
                    j++;
                    k = -1;
                    continue;
                }
                int lo = runs[2 * k] < 0 ? 0 : runs[2 * k];
                int hi = runs[2 * k + 1] > nx - 1 ? nx - 1 : runs[2 * k + 1];
                if (i < lo)
                    i = lo;
                float wz = (float)(oz + (g.J0 + j) * step);   // FillHeights' expression
                int row = j * nx;
                while (i <= hi)
                {
                    if (done >= budget)
                    {
                        m_VerifyRow = j;
                        m_VerifyRun = k;
                        m_VerifyCol = i;
                        FinishVerifyTiming(t0, done);
                        return;
                    }
                    int q = row + i;
                    if (need[q] != 0)
                    {
                        float wx = (float)(ox + (g.I0 + i) * step);
                        float fresh = TerrainUtils.SampleHeight(ref hd, new float3(wx, 0f, wz));
                        done++;
                        if (math.asint(fresh) != math.asint(heights[q]))
                        {
                            VerifyMismatch();
                            FinishVerifyTiming(t0, done);
                            return;
                        }
                    }
                    i++;
                }
                k++;
                i = int.MinValue;
            }

            // Pass complete: every Need height equaled a sample taken at or after the pass start.
            m_GoodVerifiedAt = m_VerifyPassStart;
            Verifies++;
            ResetVerifyCursor();
            FinishVerifyTiming(t0, done);
        }

        /// <summary>A verified height changed (or the lattice no longer matches): the next frame
        /// recomputes, and in full.</summary>
        private void VerifyMismatch()
        {
            m_ForceFullResample = true;
            m_GoodVerifiedAt = double.NegativeInfinity;
            m_GoodStale = true;
            VerifyChanges++;
            ResetVerifyCursor();
        }

        private void FinishVerifyTiming(long t0, int nodes)
        {
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            VerifyNodesLast = nodes;
            LastVerifyMs = (float)((t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            if (LastVerifyMs > PeakVerifyMs)
                PeakVerifyMs = LastVerifyMs;
        }

        /// <summary>Called on a recompute frame with the eligible Temp (after CompleteDependency). Reads the verdict, and on a change of
        /// the (errors, warnings, infos, tags) set offers one line to the LogWindow. A session that starts clean says nothing. Allocates
        /// only on a change (the line). Never throws: a failure logs one `!!!` and disables only this watch until the next
        /// load.</summary>
        private void WatchValidation(Entity temp, int nodeCount, bool complete, bool isNew)
        {
            if (m_ValOff)
                return;
            try
            {
                ReadValidation(temp, out uint errors, out uint warnings, out uint infos, out byte tags);
                ReadElsewhere(temp, out uint elseErrors, out int elseCount, out Entity elsePrefab);
                float now = UnityEngine.Time.realtimeSinceStartup;
                bool had = m_ValSession;
                if (had && errors == m_ValErrors && warnings == m_ValWarnings && infos == m_ValInfos && tags == m_ValTags &&
                    elseErrors == m_ValElseErrors && elseCount == m_ValElseCount && elsePrefab == m_ValElsePrefab)
                {
                    if (m_ValLog.TrailingDue(now))
                        WriteValidationSummary(now);
                    return;
                }
                bool wasClear = !had || (m_ValErrors == 0 && m_ValWarnings == 0 && m_ValInfos == 0 && m_ValTags == 0 &&
                                         m_ValElseCount == 0);
                bool isClear = errors == 0 && warnings == 0 && infos == 0 && tags == 0 && elseCount == 0;
                string was = wasClear ? "clear"
                    : FullValidationState(m_ValErrors, m_ValWarnings, m_ValInfos, m_ValTags, m_ValElseErrors, m_ValElseCount,
                        m_ValElsePrefab);
                m_ValSession = true;
                m_ValErrors = errors;
                m_ValWarnings = warnings;
                m_ValInfos = infos;
                m_ValTags = tags;
                m_ValElseErrors = elseErrors;
                m_ValElseCount = elseCount;
                m_ValElsePrefab = elsePrefab;
                if (isClear)
                {
                    ValidationText = "clear";
                    if (wasClear)
                        return;   // a session that starts clean says nothing
                }
                string state = isClear ? "clear"
                    : FullValidationState(errors, warnings, infos, tags, elseErrors, elseCount, elsePrefab);
                ValidationText = state;
                ValidationChanges++;
                string body = $"{(isNew ? "new" : "edit")} run, {nodeCount} nodes, {(complete ? "complete" : "open")}: " +
                              $"the game reports {state} (was {was})";
                LogWindow.Verdict verdict = m_ValLog.Offer(now, false, body, nodeCount);
                if (verdict == LogWindow.Verdict.Plain)
                {
                    Mod.Log.Info("RunFeedback [validation] " + body);
                    return;
                }
                m_ValFoldErrors |= errors;
                m_ValFoldWarnings |= warnings;
                m_ValFoldInfos |= infos;
                m_ValFoldElse |= elseErrors;
                if (verdict == LogWindow.Verdict.Summary)
                    WriteValidationSummary(now);
            }
            catch (Exception e)
            {
                ValidationWatchFailed(e);
            }
        }

        private void ValidationWatchFailed(Exception e)
        {
            m_ValOff = true;
            Mod.Log.Error($"!!! {nameof(SkiRunDrawFeedbackSystem)} EXCEPTION in the validation watch — the game's " +
                          $"error types are no longer logged until the next game load (the heat-map keeps working):\n{e}");
        }

        /// <summary>Called on hold frames (eligible Temp, no recompute): with nothing folded it is one int test. Otherwise it runs the window
        /// test, and writes the summary once the window passed, so a fold does not wait for the cursor to move.</summary>
        private void FlushValidationFold()
        {
            if (m_ValLog.Count == 0 || m_ValOff)
                return;
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (m_ValLog.TrailingDue(now))
                    WriteValidationSummary(now);
            }
            catch (Exception e)
            {
                ValidationWatchFailed(e);
            }
        }

        /// <summary>The Game.Tools.Error carriers other than the eligible Temp: the union of their error types (ReadValidation's
        /// errors), their count and the prefab of the lowest-index one (stable across chunk order). Recompute frames only. When nothing
        /// carries Error: one IsEmptyIgnoreFilter, no allocation.</summary>
        private void ReadElsewhere(Entity temp, out uint errors, out int count, out Entity firstPrefab)
        {
            errors = 0u;
            count = 0;
            firstPrefab = Entity.Null;
            if (m_ErrorQuery.IsEmptyIgnoreFilter)
                return;
            NativeArray<Entity> carriers = m_ErrorQuery.ToEntityArray(Allocator.Temp);
            try
            {
                int lowest = int.MaxValue;
                for (int k = 0; k < carriers.Length; k++)
                {
                    Entity e = carriers[k];
                    if (e == temp)
                        continue;
                    count++;
                    ReadValidation(e, out uint other, out uint _, out uint _, out byte _);
                    errors |= other;
                    if (e.Index < lowest)
                    {
                        lowest = e.Index;
                        firstPrefab = EntityManager.HasComponent<PrefabRef>(e)
                            ? EntityManager.GetComponentData<PrefabRef>(e).m_Prefab
                            : Entity.Null;
                    }
                }
            }
            finally
            {
                carriers.Dispose();
            }
        }

        /// <summary>The eligible Temp's state (ValidationState), then the other carriers': "elsewhere: error (blocks the click)
        /// InvalidShape on 1 other entity (SkiRunNaturalA)". When the other carriers have no error type of their own (elseErrors == 0),
        /// they carry the game's Error tag for the eligible Temp's error. ValidationSystem.cs:1657-1664 tags the permanent entity an
        /// error names, for ANY error at Override or above. :1749-1752 gives the icon to the Temp only. So the line names that: "the
        /// run it overlaps" when the Temp's own errors include OverlapExisting, else "the run the game marks with this error" ("object"
        /// when the carrier is not one of our run prefabs).</summary>
        private string FullValidationState(uint errors, uint warnings, uint infos, byte tags, uint elseErrors, int elseCount,
            Entity elsePrefab)
        {
            string own = ValidationState(errors, warnings, infos, tags);
            if (elseCount == 0)
                return own;
            string name = elsePrefab != Entity.Null ? m_PrefabSystem.GetPrefabName(elsePrefab) : null;
            if (elseErrors == 0u)
            {
                var tagged = new StringBuilder(160);
                tagged.Append(own == "clear" ? "this run clear" : own).Append("; elsewhere: ");
                string what = m_Ownership.IsRunPrefab(elsePrefab) ? "run" : "object";
                bool overlap = (errors & (1u << (int)ErrorType.OverlapExisting)) != 0u;   // the eligible Temp's own errors
                if (overlap)
                    tagged.Append(elseCount == 1 ? "the " + what + " it overlaps" : elseCount + " objects it overlaps");
                else   // ValidationSystem.cs:1661-1664 tags the partner for ANY error at Override or above
                    tagged.Append(elseCount == 1 ? "the " + what + " the game marks with this error"
                                                 : elseCount + " objects the game marks with this error");
                if (!string.IsNullOrEmpty(name))
                    tagged.Append(", ").Append(name).Append(elseCount > 1 ? " and others" : "");
                tagged.Append(elseCount == 1 ? " (the game tags it too, no icon of its own)"
                                             : " (the game tags them too, no icons of their own)");
                return tagged.ToString();
            }
            var types = new StringBuilder(64);
            AppendTypes(types, "", elseErrors);   // an empty builder: no separator, just the names
            var sb = new StringBuilder(160);
            sb.Append(own == "clear" ? "this run clear" : own).Append("; elsewhere: error (blocks the click) ")
              .Append(types.ToString()) // elseErrors != 0 here (the untyped case is handled above)
              .Append(" on ").Append(elseCount).Append(elseCount == 1 ? " other entity" : " other entities");
            if (!string.IsNullOrEmpty(name))
                sb.Append(" (").Append(name).Append(elseCount > 1 ? " and others)" : ")");
            return sb.ToString();
        }

        /// <summary>The eligible Temp's verdict: the Error / Warning tags, then each icon of its IconElement buffer → PrefabRef →
        /// ToolErrorData.m_Error, sorted by Icon.m_Priority (&gt;= Error, &gt;= Warning, below). Core EntityManager reads only. The icon
        /// reads may complete the icon jobs of this frame (a recompute frame only).</summary>
        private void ReadValidation(Entity temp, out uint errors, out uint warnings, out uint infos, out byte tags)
        {
            errors = warnings = infos = 0u;
            tags = 0;
            if (EntityManager.HasComponent<Game.Tools.Error>(temp))
                tags |= ValTagError;
            if (EntityManager.HasComponent<Game.Tools.Warning>(temp))
                tags |= ValTagWarning;
            if (!EntityManager.HasBuffer<Game.Notifications.IconElement>(temp))
                return;
            DynamicBuffer<Game.Notifications.IconElement> icons =
                EntityManager.GetBuffer<Game.Notifications.IconElement>(temp, isReadOnly: true);
            for (int i = 0; i < icons.Length; i++)
            {
                Entity icon = icons[i].m_Icon;
                if (!EntityManager.Exists(icon) || !EntityManager.HasComponent<PrefabRef>(icon))
                {
                    tags |= ValTagOther;
                    continue;
                }
                Entity prefab = EntityManager.GetComponentData<PrefabRef>(icon).m_Prefab;
                if (prefab == Entity.Null || !EntityManager.HasComponent<ToolErrorData>(prefab))
                {
                    tags |= ValTagOther;
                    continue;
                }
                int type = (int)EntityManager.GetComponentData<ToolErrorData>(prefab).m_Error;
                if (type < 0 || type > 31)
                {
                    tags |= ValTagOther;
                    continue;
                }
                var priority = EntityManager.HasComponent<Game.Notifications.Icon>(icon)
                    ? EntityManager.GetComponentData<Game.Notifications.Icon>(icon).m_Priority
                    : Game.Notifications.IconPriority.Error;
                uint bit = 1u << type;
                if (priority >= Game.Notifications.IconPriority.Error)
                    errors |= bit;
                else if (priority >= Game.Notifications.IconPriority.Warning)
                    warnings |= bit;
                else
                    infos |= bit;
            }
        }

        /// <summary>"error (blocks the click) InvalidShape; warning OverlapExisting" — the ErrorType names
        /// (Game.Tools/ErrorType.cs) per severity, then the tags not explained by a type.</summary>
        private static string ValidationState(uint errors, uint warnings, uint infos, byte tags)
        {
            var sb = new StringBuilder(96);
            AppendTypes(sb, "error (blocks the click) ", errors);
            AppendTypes(sb, "warning (does not block) ", warnings);
            AppendTypes(sb, "info ", infos);
            if ((tags & ValTagError) != 0 && errors == 0)
                Separate(sb).Append("error (blocks the click), no type attached");
            if ((tags & ValTagWarning) != 0)
                Separate(sb).Append("warning tag (another area being drawn reports this run)");
            if ((tags & ValTagOther) != 0)
                Separate(sb).Append("an icon without a tool error type");
            return sb.Length == 0 ? "clear" : sb.ToString();
        }

        private static void AppendTypes(StringBuilder sb, string label, uint mask)
        {
            if (mask == 0u)
                return;
            Separate(sb).Append(label);
            bool first = true;
            for (int b = 0; b < 32; b++)
            {
                if ((mask & (1u << b)) == 0u)
                    continue;
                if (!first)
                    sb.Append(", ");
                sb.Append(((ErrorType)b).ToString());
                first = false;
            }
        }

        private static StringBuilder Separate(StringBuilder sb)
        {
            return sb.Length == 0 ? sb : sb.Append("; ");
        }

        /// <summary>One summary line for the folded changes (LogWindow rule), with the union of the types seen.</summary>
        private void WriteValidationSummary(float now)
        {
            string seen = ValidationState(m_ValFoldErrors, m_ValFoldWarnings, m_ValFoldInfos, 0);
            if (m_ValFoldElse != 0u)
            {
                // the other carriers' error types seen in the fold (FullValidationState's "elsewhere")
                var sb = new StringBuilder(seen == "clear" ? "" : seen);
                AppendTypes(sb, "elsewhere error ", m_ValFoldElse);
                seen = sb.ToString();
            }
            Mod.Log.Info($"RunFeedback [validation] {m_ValLog.Count} more change(s) in {m_ValLog.SpanS(now):F0} s, folded " +
                         $"(one line per ValidationLogWindowS={Tuning.ValidationLogWindowS:0} s): nodes {m_ValLog.Range(0, "F0")}, " +
                         $"types seen {seen} — last: {m_ValLog.LastText}");
            m_ValLog.Wrote(now);
            m_ValFoldErrors = m_ValFoldWarnings = m_ValFoldInfos = m_ValFoldElse = 0u;
        }

        /// <summary>The run left the cursor (Invalidate): write a pending fold now and forget the state, so the next
        /// run starts clean. Never throws (Invalidate runs on the exception path too).</summary>
        private void EndValidationSession()
        {
            m_ValSession = false;
            m_ValErrors = m_ValWarnings = m_ValInfos = 0u;
            m_ValTags = 0;
            m_ValElseErrors = 0u;
            m_ValElseCount = 0;
            m_ValElsePrefab = Entity.Null;
            ValidationText = "-";
            try
            {
                if (m_ValLog.Count > 0)
                    WriteValidationSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception)
            {
                // a lost summary line must not take the frame down. The next change logs again
            }
        }

        /// <summary>Builds the strips of (grid, r) into the managed stage m_StripStage (never into the NativeArray the draw job reads:
        /// PublishStrips copies them). Called with the CURRENT grid after the ping-pong (m_Grid == target) or with the pre-warm's scratch
        /// objects.</summary>
        private void BuildStrips(SlopeGrid grid, SlopeResult r)
        {
            m_StripCount = 0;
            RowStride = 1;
            bool capped = false;

            if (grid.PolylineMode)
            {
                // One segment per polyline segment, band color, PolylineWidthM wide.
                float3[] pts = grid.Points;
                int segs = r.SegmentCount;
                byte[] segBand = r.SegmentBand;
                if (pts != null && segBand != null)
                {
                    for (int s = 0; s < segs && s + 1 < grid.PointCount && s < segBand.Length; s++)
                    {
                        byte b = segBand[s];
                        if (b > (byte)SlopeBand.DoubleBlack)
                            continue;
                        if (m_StripCount >= Tuning.MaxStrips)
                        {
                            capped = true;
                            break;
                        }
                        m_StripStage[m_StripCount] = new Strip
                        {
                            A = pts[s],
                            B = pts[s + 1],
                            Width = Tuning.PolylineWidthM,
                            Color = BandColors[b],
                        };
                        m_StripCount++;
                    }
                }
            }
            else
            {
                // Full row resolution first. Above Tuning.MaxStrips, take the SMALLEST row stride 2..MaxRowStride whose strips fit, so the
                // heat-map stays complete at a coarser row resolution. Linear search, because the count at stride s is not monotone in s
                // (different representative rows). A failed pass stops at the first strip past the cap, and the buffers are simply rewritten.
                // The list is truncated (at that stride) only when even MaxRowStride overflows.
                int stride = 1;
                while (!BuildRingStrips(grid, r, stride))
                {
                    if (stride >= Tuning.MaxRowStride)
                    {
                        capped = true;
                        break;
                    }
                    stride++;
                }
                RowStride = stride;
            }

            StripsBuilt = m_StripCount;
            if (RowStride > 1 && !capped && !m_StrideLogged)
            {
                m_StrideLogged = true;
                Mod.Log.Info($"RunFeedback: row stride {RowStride} — full row resolution exceeds " +
                             $"strip cap {Tuning.MaxStrips}, heat-map drawn COMPLETE from one row per " +
                             $"{RowStride} lattice rows ({StripsBuilt} strips, {RowStride * StepM:F1} m " +
                             $"wide; {InsideCells} inside cells, {r.Nz} rows at {StepM:F1} m; " +
                             "not truncated; logged once per load)");
            }
            if (capped)
            {
                StripCapHit = true;
                if (!m_CapLogged)
                {
                    m_CapLogged = true;
                    Mod.Log.Info($"RunFeedback: strip cap {Tuning.MaxStrips} reached at row stride " +
                                 $"{RowStride} (max {Tuning.MaxRowStride}) — heat-map truncated " +
                                 $"({InsideCells} inside cells at {StepM:F1} m; logged once per load)");
                }
            }
        }

        /// <summary>Ring-mode strips at row stride s into the strip stage. Lattice rows are grouped by RowGroup. Each group is drawn from
        /// its MIDDLE row's same-band run-length merge, centered on the group's z-center, width = group rows × Step (s·Step). The last
        /// group may be shorter. s = 1 is the full-resolution layout (one group per row, width = Step, center = the row). Returns false
        /// when the strips do not fit Tuning.MaxStrips: the stage then holds the first MaxStrips strips (the truncated
        /// list).</summary>
        private bool BuildRingStrips(SlopeGrid grid, SlopeResult r, int s)
        {
            m_StripCount = 0;
            // Row run-length merge of same-band inside cells. Cell (i, j) is centered on the lattice node x = Origin.x + (I0 + i) * Step,
            // spanning ± Step / 2. A strip runs from the first cell's west edge to the last cell's east edge, y = mean of the two end
            // heights (at the representative row).
            byte[] cellBand = r.CellBand;
            int nx = r.Nx, nz = r.Nz;
            double step = r.Step;
            float[] heights = grid.Heights;
            bool geomMatch = grid.Nx == nx && grid.Nz == nz &&
                             grid.I0 == r.I0 && grid.J0 == r.J0;
            bool heightsMatch = heights != null && geomMatch && heights.Length >= nx * nz;
            // Walk only each row's Mask RUNS (SlopeGrid.MaskRuns / MaskRowStart from Plan) when the grid matches the result. CellBand is
            // BandOutside everywhere outside the Mask, and same-band merging never crosses an outside cell. So the strips are identical to a
            // full-row walk, and the walk is O(inside), not O(bbox) or O(row span) (a U- or C-shaped run's rows cross two arms). Otherwise
            // keep the full walk.
            int[] maskRuns = grid.MaskRuns, maskStart = grid.MaskRowStart;
            bool spans = geomMatch && maskStart != null && maskStart.Length >= nz + 1 && maskStart[nz] >= 0 &&
                         (maskStart[nz] == 0 || (maskRuns != null && maskRuns.Length >= 2L * maskStart[nz]));
            if (cellBand == null || cellBand.Length < nx * nz)
                return true;
            double half = step * 0.5;
            int groups = (nz + s - 1) / s;
            for (int g = 0; g < groups; g++)
            {
                RowGroup(g, s, nz, out int lo, out int hi, out int j);
                int row = j * nx;
                // Group z-center and height. s = 1 gives exactly the row center and Step.
                float wz = (float)(r.Origin.y + (r.J0 + 0.5 * (lo + hi)) * step);
                float width = (float)((hi - lo + 1) * step);
                int k0 = 0, k1 = 1;                  // full walk: one pseudo-run [0, nx - 1]
                if (spans)
                {
                    k0 = maskStart[j];
                    k1 = maskStart[j + 1];
                }
                for (int k = k0; k < k1; k++)
                {
                    int i = 0, iEnd = nx;
                    if (spans)
                    {
                        i = maskRuns[2 * k] < 0 ? 0 : maskRuns[2 * k];
                        iEnd = (maskRuns[2 * k + 1] > nx - 1 ? nx - 1 : maskRuns[2 * k + 1]) + 1;
                    }
                    while (i < iEnd)
                    {
                        byte b = cellBand[row + i];
                        if (b > (byte)SlopeBand.DoubleBlack)
                        {
                            i++;
                            continue;   // BandOutside (255) and anything invalid
                        }
                        int start = i;
                        while (i + 1 < iEnd && cellBand[row + i + 1] == b)
                            i++;
                        int end = i;
                        i++;
                        if (m_StripCount >= Tuning.MaxStrips)
                            return false;
                        float y = heightsMatch
                            ? 0.5f * (heights[row + start] + heights[row + end])
                            : 0f;
                        float xa = (float)(r.Origin.x + (r.I0 + start) * step - half);
                        float xb = (float)(r.Origin.x + (r.I0 + end) * step + half);
                        m_StripStage[m_StripCount] = new Strip
                        {
                            A = new float3(xa, y, wz),
                            B = new float3(xb, y, wz),
                            Width = width,
                            Color = BandColors[b],
                        };
                        m_StripCount++;
                    }
                }
            }
            return true;
        }

        /// <summary>Row group g at stride s over nz lattice rows: rows lo..hi, hi = lo + s - 1 clamped to the last row (the last group
        /// may be shorter). Representative = the middle row lo + (hi - lo) / 2 (lower middle for an even count). Group count = (nz + s -
        /// 1) / s. The groups cover rows 0..nz-1 exactly once.</summary>
        private static void RowGroup(int g, int s, int nz, out int lo, out int hi, out int rep)
        {
            lo = g * s;
            hi = lo + s - 1;
            if (hi > nz - 1)
                hi = nz - 1;
            rep = lo + ((hi - lo) >> 1);
        }

        /// <summary>Copies the staged strips into the NativeArray the draw job reads: the ONLY write to m_Strips, always after
        /// CompleteDraw() (engine notes OVERLAY safety (a)). One memcpy (NativeArray.Copy(T[], NativeArray, int)).</summary>
        private void PublishStrips()
        {
            CompleteDraw();
            int n = m_StripCount;
            if (n > 0 && m_Strips.IsCreated)
                NativeArray<Strip>.Copy(m_StripStage, m_Strips, n);
        }

        /// <summary>Waits for the last scheduled draw job. It is normally already finished: the
        /// OverlayRenderSystem completes every registered writer at the start of its OnUpdate,
        /// OverlayRenderSystem.cs:740-741, the same frame.</summary>
        private void CompleteDraw()
        {
            m_DrawHandle.Complete();
            m_DrawHandle = default(JobHandle);
        }

        private void Draw()
        {
            int count = m_StripCount;
            bool crux = m_CruxDraw;
            if (!m_Readout.Valid || (count == 0 && !crux) || !m_Strips.IsCreated)
                return;
            if (count > m_Strips.Length)
                count = m_Strips.Length;
            // Timed with raw timestamps (no Stopwatch object, no allocation): GetBuffer + schedule + AddBufferWriter only. The strip work
            // runs on a worker thread.
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle overlayDeps);   // :603-634
            var job = new StripDrawJob
            {
                Strips = m_Strips,
                Count = count,
                DrawCrux = crux,
                CruxPosition = m_CruxPos,
                CruxDiameter = m_CruxDiameter,
                CruxOutlineWidth = Tuning.CruxRingOutlineM,
                CruxOutline = CruxOutlineColor,
                CruxFill = CruxFillColor,
                Overlay = buffer,
            };
            // Depends on every earlier overlay writer, ours included (engine notes OVERLAY (b)).
            JobHandle handle = job.Schedule(overlayDeps);
            m_Overlay.AddBufferWriter(handle);   // :636-639. Completed at OverlayRenderSystem :740
            m_DrawHandle = handle;
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            StripsDrawn = count;
            LastDrawMs = (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (!s_FirstDrawDone)
            {
                // The first Draw of the PROCESS pays the one-time Mono JIT of Schedule<StripDrawJob> (PrewarmJit warms only the reflection
                // data). The code reports it on its own and keeps it out of PeakDrawMs and the slow-draw WARN, so those measure the steady-state schedule cost.
                s_FirstDrawDone = true;
                FirstDrawMs = LastDrawMs;
                Mod.Log.Info($"RunFeedback: first overlay draw of this process {LastDrawMs:F2} ms " +
                             $"({count} strips; one-time first call of the draw-job schedule, kept " +
                             "out of the draw peak and the slow-draw WARN; logged once per process)");
                return;
            }
            if (LastDrawMs > PeakDrawMs)
                PeakDrawMs = LastDrawMs;
            if (PeakDrawMs > Tuning.SlowDrawWarnMs && !m_SlowDrawLogged)
            {
                m_SlowDrawLogged = true;
                Mod.Log.Warn($"RunFeedback: slow overlay draw {PeakDrawMs:F2} ms (GetBuffer + " +
                             $"schedule of the draw job for {count} strips at row stride {RowStride}, " +
                             $"crux ring {crux}) — {InsideCells} inside cells at {StepM:F1} m " +
                             "(logged once per load)");
            }
        }

        /// <summary>"Dump to log": the state as text in the mod log, plus the capture file of the last rated polygon. Button handler (dev
        /// tab) or dump-hotkey handler (SkiGladeHotkeySystem: UIUpdate, before this system's Rendering update, so it reads the PREVIOUS
        /// frame's readout with the run still on the cursor). These are the only places this system does file IO. Each half has its own
        /// try/catch.</summary>
        public void DumpToLog()
        {
            try
            {
                Readout r = m_Readout;
                var sb = new StringBuilder(1280);
                sb.Append("RunFeedback: DUMP — armed ").Append(m_Armed)
                  .Append(", disabled ").Append(m_Disabled)
                  .Append(", ownership prefab sets ready ").Append(m_Ownership != null && m_Ownership.PrefabSetsReady)
                  .Append(" (the feedback gate), ownership armed ").Append(m_Ownership != null && m_Ownership.IsArmed)
                  .Append(" (binding sweep), binds new runs ").Append(m_Ownership != null && m_Ownership.BindsNewRuns)
                  .Append(", area tool active ").Append(AreaToolActive).Append('\n');
                // The "since load" test is the good grid, not Recomputes (a heights-not-ready recompute counts without a rating).
                if (m_GoodGrid == null)
                    sb.Append("  rating: none since load (published fields below are this load's, reset at load)\n");
                sb.Append("  temp ").Append(EntityText(EligibleTemp))
                  .Append(" nodes ").Append(EligibleNodes)
                  .Append(EligibleIsNew ? " new" : " edit")
                  .Append(EligibleComplete ? " complete" : " open")
                  .Append(", candidates ").Append(TempCandidates)
                  .Append(", live ").Append(m_ReadoutLive).Append('\n');
                // the game's verdict on that Temp at the last recompute
                sb.Append("  game validation (last recompute): ").Append(ValidationText ?? "-")
                  .Append(", changes since load ").Append(ValidationChanges)
                  .Append(m_ValOff ? ", watch OFF after an exception" : "")
                  .Append(", folded and not yet written ").Append(m_ValLog.Count).Append('\n');
                sb.Append("  note: ").Append(LastNote)
                  .Append("; status ").Append(LastStatus)
                  .Append(", raw band ").Append(RawBand)
                  .Append(", shown band ").Append(r.Band)
                  .Append(", rating (crux) ").Append((r.RatingGrade * 100f).ToString("F1"))
                  .Append(" %, steepest patch ").Append((r.SteepestGrade * 100f).ToString("F1"))
                  .Append(" %, P90 ").Append((P90Grade * 100f).ToString("F1"))
                  .Append(" %, mean ").Append((r.MeanGrade * 100f).ToString("F1"))
                  .Append(" %, drop ").Append(r.Drop.ToString("F1"))
                  .Append(" m, gate in range ").Append(r.GateInRange)
                  .Append(NewRunBindingOff ? " (forced false: binding is off for new runs this load — see the Ownership !!! / load-pass line)" : "")
                  .Append('\n');
                sb.Append("  R3 length: ").Append(r.Length.ToString("F1", CultureInfo.InvariantCulture))
                  .Append(" m horizontal (LengthRect ").Append(LengthRect.ToString("F1", CultureInfo.InvariantCulture))
                  .Append(", AxisLength ").Append(AxisLength.ToString("F1", CultureInfo.InvariantCulture))
                  .Append("); heartbeat ").Append(Tuning.HeartbeatSeconds.ToString("F1", CultureInfo.InvariantCulture))
                  .Append(" s, last verify age ");
                double age = UnityEngine.Time.realtimeSinceStartupAsDouble - m_GoodVerifiedAt;
                if (m_GoodGrid == null || double.IsInfinity(age))
                    sb.Append(m_GoodStale ? "- (stale: a height changed, full resample pending)" : "-");
                else
                    sb.Append(age.ToString("F1", CultureInfo.InvariantCulture)).Append(" s");
                sb.Append(", verifies ").Append(Verifies)
                  .Append(", changes found ").Append(VerifyChanges)
                  .Append(", pass in progress ").Append(m_VerifyRow >= 0)
                  .Append(", verify ms last ").Append(LastVerifyMs.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(" (").Append(VerifyNodesLast).Append(" nodes) / peak ")
                  .Append(PeakVerifyMs.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(", VerifyNodesPerFrame ").Append(Tuning.VerifyNodesPerFrame).Append('\n');
                sb.Append("  crux: connected ").Append(r.Connected)
                  .Append(", crux cell ");
                if (CruxValid)
                    sb.Append(CruxI).Append(',').Append(CruxJ)
                      .Append(" at (").Append(m_CruxPos.x.ToString("F1")).Append(", ")
                      .Append(m_CruxPos.z.ToString("F1")).Append(')');
                else
                    sb.Append("none");
                sb.Append(", end zones top ").Append(TopZoneCells)
                  .Append(" / bottom ").Append(BottomZoneCells).Append(" cells").Append('\n');
                sb.Append("  R2b: zone key ").Append(ZoneByFallLine ? "fall line" : "height")
                  .Append(" (|mean g| ").Append(ZoneMeanGradient.ToString("F4", CultureInfo.InvariantCulture))
                  .Append(", coherence ").Append(ZoneFallCoherence.ToString("F3", CultureInfo.InvariantCulture))
                  .Append("), diagonal rule ").Append(DiagonalRule)
                  .Append(" (EndZoneDepthM ").Append(SkiSlopeClassifier.EndZoneDepthM.ToString("F1", CultureInfo.InvariantCulture))
                  .Append(", EndZoneMinMeanGrade ").Append(SkiSlopeClassifier.EndZoneMinMeanGrade.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(", EndZoneMinCoherence ").Append(SkiSlopeClassifier.EndZoneMinCoherence.ToString("F2", CultureInfo.InvariantCulture))
                  .Append(", EndZoneMaxM2 ").Append(SkiSlopeClassifier.EndZoneMaxM2.ToString("F0", CultureInfo.InvariantCulture))
                  .Append(" on the height key)").Append('\n');
                sb.Append("  lattice: step x").Append(StepMultiple)
                  .Append(" = ").Append(StepM.ToString("F2"))
                  .Append(" m, ").Append(LatticeNx).Append('x').Append(LatticeNz)
                  .Append(", inside ").Append(InsideCells)
                  .Append(", need ").Append(NeedCells).Append('\n');
                sb.Append("  timing ms: plan ").Append(LastPlanMs.ToString("F3"))
                  .Append(" / fill ").Append(LastFillMs.ToString("F3"))
                  .Append(" / classify ").Append(LastClassifyMs.ToString("F3"))
                  .Append(" / strips ").Append(LastStripMs.ToString("F3"))
                  .Append(" = ").Append(LastRecomputeMs.ToString("F3"))
                  .Append(" (peak ").Append(PeakRecomputeMs.ToString("F3"))
                  .Append("), recomputes ").Append(Recomputes)
                  .Append(", pre-warm ").Append(PrewarmMs.ToString("F2")).Append('\n');
                sb.Append("  cost measures: heights reused ").Append(HeightsReused)
                  .Append(" / sampled ").Append(HeightsSampled)
                  .Append(", throttle window ").Append(ThrottleWindowMs.ToString("F1"))
                  .Append(" ms, deferred move frames ").Append(ThrottledFrames).Append('\n');
                sb.Append("  draw ms (schedule): last ").Append(LastDrawMs.ToString("F3"))
                  .Append(" / peak ").Append(PeakDrawMs.ToString("F3"))
                  .Append(" / first of process ").Append(FirstDrawMs.ToString("F3")).Append('\n');
                sb.Append("  strips built ").Append(StripsBuilt)
                  .Append(", drawn last frame ").Append(StripsDrawn)
                  .Append(", row stride ").Append(RowStride)
                  .Append(", cap hit ").Append(StripCapHit)
                  .Append("; last error: ").Append(LastError);
                Mod.Log.Info(sb.ToString());
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"RunFeedback: dump failed: {e.Message}");
            }

            // The log dump above stays for every player. The .skislope capture is the only file the mod writes outside its log and
            // settings, so it sits behind Options > Diagnostics (off by default).
            if (!SkiResortMod.Settings.SkiResortSettings.DiagnosticsOn)
            {
                LastCapturePath = "off (Options > Diagnostics > Measurement diagnostics)";
                Mod.Log.Info("RunFeedback: capture file not written (Options > Diagnostics > Measurement diagnostics is off)");
                return;
            }
            try
            {
                string path = WriteCapture(out int needCount);
                if (path == null)
                {
                    LastCapturePath = "none (no rated polygon since load)";
                    Mod.Log.Info("RunFeedback: capture skipped — no rated polygon since the load");
                }
                else
                {
                    LastCapturePath = path;
                    Mod.Log.Info($"RunFeedback: capture written to {Mod.ScrubUserPath(path)} ({m_CapNodeCount} nodes, " +
                                 $"{needCount} need heights, rated {m_CapBand} " +
                                 $"{(m_CapRating * 100f).ToString("F1", CultureInfo.InvariantCulture)} %)");
                }
            }
            catch (Exception e)
            {
                LastCapturePath = "failed: " + e.GetType().Name;
                Mod.Log.Warn($"RunFeedback: capture failed: {e.GetType().Name}: {Mod.ScrubUserPath(e.Message)}");
            }
        }

        /// <summary>Writes the last rated polygon (m_GoodGrid + the capture snapshot) as a `.skislope` file (tests/SlopeBatch parses it).
        /// Format: invariant culture, floats "G9" and doubles "G17" (both round-trip exactly, the .NET Framework's "R" has a known double
        /// round-trip defect, and the loader reads both), '\n' line ends, UTF-8 without BOM. Directory = Application.persistentDataPath +
        /// "/ModsData/SkiResortMod/captures" (created if missing). Name run-yyyyMMdd-HHmmss.skislope, with "-2", "-3", ... appended only
        /// when that name already exists (two dumps in one second). Need lines walk Plan's per-row Need runs in row-major order (j, then
        /// i). Returns the full path, or null when the system rated nothing since the load.</summary>
        private string WriteCapture(out int needCount)
        {
            needCount = 0;
            SlopeGrid g = m_GoodGrid;
            if (!m_CapValid || g == null)
                return null;
            int nx = g.Nx, nz = g.Nz;
            long total = (long)nx * nz;
            byte[] need = g.Need;
            float[] h = g.Heights;
            int[] runs = g.NeedRuns, rs = g.NeedRowStart;
            if (nx <= 0 || nz <= 0 || need == null || h == null || need.Length < total || h.Length < total ||
                rs == null || rs.Length < nz + 1 || rs[nz] < 0 || (rs[nz] > 0 && (runs == null || runs.Length < 2L * rs[nz])))
                throw new InvalidOperationException("captured grid is incomplete");

            // Pass 1: count (the engine notes line precedes the list). Plan's exact Need runs, row-major (j, then i ascending).
            for (int j = 0; j < nz; j++)
            {
                int row = j * nx;
                for (int k = rs[j]; k < rs[j + 1]; k++)
                {
                    int a = runs[2 * k] < 0 ? 0 : runs[2 * k];
                    int b = runs[2 * k + 1] > nx - 1 ? nx - 1 : runs[2 * k + 1];
                    for (int i = a; i <= b; i++)
                    {
                        if (need[row + i] != 0)
                            needCount++;
                    }
                }
            }

            CultureInfo ci = CultureInfo.InvariantCulture;
            string dir = UnityEngine.Application.persistentDataPath + "/ModsData/SkiResortMod/captures";
            System.IO.Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", ci);
            string path = dir + "/run-" + stamp + ".skislope";
            for (int k = 2; System.IO.File.Exists(path) && k < 1000; k++)
                path = dir + "/run-" + stamp + "-" + k.ToString(ci) + ".skislope";

            using (var w = new System.IO.StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.NewLine = "\n";
                w.WriteLine("SKISLOPE 1");
                w.WriteLine("step " + g.Step.ToString("G9", ci) + " texel " + m_CapTexel.ToString("G9", ci) +
                            " multiple " + m_CapMultiple.ToString(ci));
                w.WriteLine("origin " + g.Origin.x.ToString("G17", ci) + " " + g.Origin.y.ToString("G17", ci));
                w.WriteLine("grid " + g.I0.ToString(ci) + " " + g.J0.ToString(ci) + " " +
                            nx.ToString(ci) + " " + nz.ToString(ci));
                w.WriteLine("rated " + m_CapRating.ToString("G9", ci) + " " + m_CapSteepest.ToString("G9", ci) + " " +
                            m_CapMean.ToString("G9", ci) + " " + m_CapDrop.ToString("G9", ci) + " " +
                            m_CapBand.ToString());
                w.WriteLine("nodes " + m_CapNodeCount.ToString(ci));
                for (int k = 0; k < m_CapNodeCount; k++)
                {
                    float3 p = m_CapNodes[k];
                    w.WriteLine(p.x.ToString("G9", ci) + " " + p.y.ToString("G9", ci) + " " + p.z.ToString("G9", ci));
                }
                w.WriteLine("need " + needCount.ToString(ci));
                for (int j = 0; j < nz; j++)
                {
                    int row = j * nx;
                    string js = j.ToString(ci);
                    for (int k = rs[j]; k < rs[j + 1]; k++)
                    {
                        int a = runs[2 * k] < 0 ? 0 : runs[2 * k];
                        int b = runs[2 * k + 1] > nx - 1 ? nx - 1 : runs[2 * k + 1];
                        for (int i = a; i <= b; i++)
                        {
                            int q = row + i;
                            if (need[q] == 0)
                                continue;
                            w.Write(i.ToString(ci));
                            w.Write(' ');
                            w.Write(js);
                            w.Write(' ');
                            w.WriteLine(h[q].ToString("G9", ci));
                        }
                    }
                }
            }
            PruneCaptures(dir, path);
            return path;
        }

        private const int CapturesKept = 20;
        private static readonly Regex CaptureFileName = new Regex(@"^run-\d{8}-\d{6}(-\d+)?\.skislope$", RegexOptions.CultureInvariant);

        /// <summary>Keeps the newest <see cref="CapturesKept"/> run-*.skislope files in the capture folder. The code touches only names this
        /// system writes, so anything else a player puts there stays. Newest by write time, not name: the "-2" suffix of a
        /// same-second dump sorts before its base name. The file just written is never removed. A failure is logged and never fails
        /// the capture.</summary>
        private static void PruneCaptures(string dir, string justWritten)
        {
            try
            {
                string[] files = System.IO.Directory.GetFiles(dir, "run-*.skislope");
                var list = new System.Collections.Generic.List<System.IO.FileInfo>(files.Length);
                for (int i = 0; i < files.Length; i++)
                {
                    var fi = new System.IO.FileInfo(files[i]);
                    if (CaptureFileName.IsMatch(fi.Name) && (fi.Attributes & System.IO.FileAttributes.ReparsePoint) == 0)
                        list.Add(fi);
                }
                if (list.Count <= CapturesKept)
                    return;
                list.Sort((a, b) =>
                {
                    int c = b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
                    return c != 0 ? c : string.CompareOrdinal(b.Name, a.Name);
                });
                string keepName = System.IO.Path.GetFileName(justWritten);
                int removed = 0;
                for (int i = CapturesKept; i < list.Count; i++)
                {
                    if (string.Equals(list[i].Name, keepName, StringComparison.Ordinal))
                        continue;
                    list[i].Delete();
                    removed++;
                }
                Mod.Log.Info($"RunFeedback: removed {removed} old capture file(s), keeping the newest {CapturesKept}");
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"RunFeedback: capture cleanup failed: {e.GetType().Name}: {Mod.ScrubUserPath(e.Message)}");
            }
        }

        /// <summary>Drops the readout and the cache key, so re-entry recomputes. Cheap (field writes only), runs on every idle frame.
        /// Deliberately leaves m_GoodGrid and the capture snapshot alone: "Dump to log" must still see the last rated polygon after the
        /// tool is closed.</summary>
        private void Invalidate(string note)
        {
            if (m_ValSession)
                EndValidationSession(); // once, when the run leaves the cursor (one bool test idle)
            m_ReadoutLive = false;
            m_Readout.Valid = false;
            m_Readout.Kind = -1; // no stale fee after the tool leaves a Temp
            m_Readout.AreaM2 = 0f;
            m_CachedTemp = Entity.Null;
            m_StripCount = 0;
            m_CruxDraw = false;
            EligibleTemp = Entity.Null;
            EligibleNodes = 0;
            ThrottleRemainingMs = 0f;
            LastNote = note;
        }

        private static void EnsureCapacity(ref float3[] array, int count)
        {
            if (array.Length < count)
                array = new float3[math.max(count, array.Length * 2)];
        }

        private void HandleException(Exception e, string where)
        {
            m_Disabled = true;
            LastError = $"{where}: {e.GetType().Name}: {e.Message}";
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiRunDrawFeedbackSystem)} EXCEPTION in {where} — run " +
                              $"draw feedback disabled until the next game load:\n{e}");
            }
        }

        private static string EntityText(Entity e)
        {
            return e == Entity.Null ? "none" : $"{e.Index}:{e.Version}";
        }
    }

    /// <summary>The game's IHeightSampler for SkiRunRater: the 2-argument TerrainUtils.SampleHeight (TerrainUtils.cs:82-137, plain
    /// bilinear), at the world point the rater passes (FillHeights' expression). Holds a COPY of the TerrainHeightData the caller
    /// fetched this frame with GetHeightData() (TerrainSystem.cs:2603, 1.6.2 :2615). Never keep an instance across frames
    /// (EnsureCPUHeights can dispose and reallocate the heights, TerrainSystem.cs:3538-3552, engine notes TERRAIN READ). SampleHeight is a
    /// pure function of that data and the point, so copying the struct changes no value. Internal (not private):
    /// SkiRunCatalogSystem's rating cache reads terrain through the same struct.</summary>
    internal struct TerrainHeightSampler : IHeightSampler
    {
        private TerrainHeightData m_Data;

        public TerrainHeightSampler(TerrainHeightData data)
        {
            m_Data = data;
        }

        public float Sample(float3 world)
        {
            return TerrainUtils.SampleHeight(ref m_Data, world);
        }
    }
}
