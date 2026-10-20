// SkiRunDrawFeedbackSystem — slope feedback while a ski run is drawn or edited with the vanilla
// area tool (docs/research/run-feedback-spec.md §4 as amended by §9 "Round R2" and §9.8 "Round
// R2b"; §9 wins where they disagree; DECISIONS 113 item 2, 116, 118; Round R3 below:
// docs/research/run-binding-glade-spec.md §3.8, §5.1, §5.3, §5.4, DECISIONS 121). A translucent heat-map of per-cell slope bands drawn INSIDE
// the Temp polygon, a white ring on the CRUX cell (the pitch that sets the rating), and a published
// Readout (band, crux grade = the hardest pitch a skier cannot avoid, steepest avoidable patch,
// mean grade, vertical drop, run length, connected, new/edit, gate in range) that SkiRunTooltipSystem shows at
// the mouse. Every engine citation is `Game.X/File.cs:line` in tools/decompile/1.6.0f1; "1.6.2 :N"
// notes a line shift in the installed 1.6.2f1 tree (OverlayRenderSystem.cs and
// AreaBorderRenderSystem.cs are unchanged in 1.6.2: not in diff-1.6.0-to-1.6.2/changed-files.txt).
//
// WHAT CHANGED IN U2 (docs/research/hub-u2-spec.md §3.5, 2026-10-03, implementer I-1): the rating
// path itself — ShoelaceArea, StepMultiple, DiagonalRule, Plan, FillHeights (height reuse included),
// Classify — moved into the pure SkiRunRater, so the hub's built-run rating cache
// (SkiRunCatalogSystem) rates with exactly this code. Behavior and numbers are unchanged: the
// bodies moved verbatim, the terrain read is TerrainHeightSampler (bottom of this file, the same
// 2-arg SampleHeight at the same world point), and tests/SlopeBatch proves bit parity on every
// field capture and against a verbatim copy of the old FillHeights. This system keeps the
// heights-ready check, the ping-pong grids, the per-Temp step hysteresis (passed as stepCurrent),
// its half of the reuse guard (verify age, forced resample — the rater checks Step and Origin) and
// everything after Classify. The step-change log line is now written after the rating instead of
// before Plan (same text). LastPlanMs = the time before the rater + the rater's plan time.
//
// ROUND E1 (D21, docs/research/econ-round/spec/ch2-runs.md §6.4; stream S2, 2026-10-04): the FEE
// INPUTS for the drawing tooltip's "Run fee (one-time)" row. Readout gains Kind (KindOf the chosen
// Temp's prefab, remembered by PickEligibleTemp in m_EligiblePrefab) and AreaM2 (SkiRunRater
// .ShoelaceArea of the Temp's nodes = the plan area Geometry.m_SurfaceArea gives at bind, for a simple
// outline). Both are set at every Recompute right after IsNewRun — before the heights-ready return, so
// a run that cannot be rated still has its fee — and cleared by Invalidate (Kind -1, AreaM2 0).
// TryGetFeeInputs reads them under the readout's own gates and one-frame lag, independent of
// Readout.Valid. Known limit: like the whole feedback system the row follows the "Show run grade while
// drawing" setting (UpdateCore step 1); with it off the fee shows only in the hub.
//
// ROUND F7 (docs/research/field-test-7-fixes.md item C2; research docs/research/field/2026-10-04/
// findings/run-collisions.md with its Verification; implementer 2, 2026-10-04):
//  * C2 — SAY WHAT THE GAME REFUSED. Field test 7 could not tell "invalid shape" from "overlaps" (the log had
//    no line about the area tool's errors). How the verdict is attached (1.6.0f1, every cited file identical
//    in 1.6.2f1): ValidationSystem (ModificationEnd, Game.Common/SystemOrder.cs:264) validates every Temp with
//    Updated (ValidationSystem.cs:1917-1919, :1926-2050); ProcessValidationResultsJob turns each ErrorData
//    (Game.Tools/ErrorData.cs: m_ErrorType, m_ErrorSeverity, m_TempEntity = our Temp, m_PermanentEntity =
//    the other area) into (a) a notification icon owned by the Temp — IconKey owner = m_TempEntity, prefab =
//    the ToolErrorData prefab of that ErrorType, priority Warning or Error (:1747-1785) — which
//    IconCommandSystem (ModificationEnd, :271) creates as an entity with PrefabRef, Icon and Owner and lists
//    in the Temp's IconElement buffer (Game.Notifications/IconCommandSystem.cs:293-336), and (b) the Error
//    TAG on the Temp for an Error (:1657-1660; a Warning tags only the OTHER area, :1661-1664). Both play back
//    at ModificationEndBarrier, so by Rendering the Temp of this frame carries them. So the type IS reachable
//    cheaply: the eligible Temp's IconElement → icon PrefabRef → ToolErrorData.m_Error (Game.Prefabs/
//    ToolErrorData.cs), with Icon.m_Priority for the severity (>= Error: blocks the click, ToolBaseSystem.cs:
//    533-540; >= Warning: does not). WatchValidation reads it on RECOMPUTE frames only (no new per-frame
//    work: a node change, a Temp change or a deferred move recompute after the cursor stops — the state at a
//    refused click is read within the throttle window), and logs `RunFeedback [validation] …` once per CHANGE
//    of the (error types, warning types, tags) set — the type names, new/edit, node count, open/complete,
//    and what it was before; a clean start says nothing, a return to clean says "clear". The lines go through
//    a LogWindow (ValidationLogWindowS): a cursor sliding along a neighbor can flip it every recompute, so a
//    burst is folded into one summary with the UNION of the types seen; the fold is written when the window
//    passes, when the run leaves the cursor (Invalidate) and in OnDestroy — silence is impossible. The
//    DUMP and the dev tab show the current state. Known limit: like the whole system it follows "Show run
//    difficulty while drawing" (UpdateCore step 1). A throw in the watch logs one `!!!` and switches only the
//    watch off until the next load; the heat-map keeps working.
//  * C2, F7 fixer (review of the round): (1) ELSEWHERE. The click is refused when ANY entity carries Error
//    (ToolBaseSystem.cs:313, :533-540), and moving a corner shared with a neighbor run gives that neighbor its
//    own Modify Temp (one definition per area on the moved position, AreaToolSystem.cs:1542-1560), whose
//    InvalidShape names only the neighbor (m_TempEntity = it, no m_PermanentEntity; ValidationHelpers.cs:887-943).
//    The eligible Temp (the one with most nodes) then reads clear while the click is refused. So, on the same
//    recompute frames, the watch also reads every other Error carrier (m_ErrorQuery = the tool's own query; one
//    IsEmptyIgnoreFilter when none): the union of their error types, their count and the lowest-index one's prefab
//    join the change key, and the line says "…; elsewhere: error (blocks the click) InvalidShape on 1 other entity
//    (SkiRunNaturalA)". Round S1 F3 (ski-days spec ch3 §5): a carrier with no error type of its own carries the
//    game's Error tag for the eligible Temp's error (ValidationSystem.cs:1657-1664; the icon stays on the Temp,
//    :1749-1752), so the line names it: "error (blocks the click) OverlapExisting; elsewhere: the run it overlaps,
//    SkiRunSnowmakingA (the game tags it too, no icon of its own)" — "it overlaps" only when the Temp's own errors
//    include OverlapExisting, else "the run the game marks with this error". (2) TRAILING FOLD. The fold's TrailingDue is also tested on hold frames (eligible Temp, no
//    recompute; one int test while nothing is folded), so a folded summary is written once its window passes even
//    while the cursor rests — not only at the next recompute, Invalidate, load or quit.
//  * C3 (a double-black color change) was CANCELED by the owner (2026-10-04, field-test-7-notes-OWNER.md):
//    double black stays red; BandColors are unchanged from the pre-round build.
//
// WHAT CHANGED IN R3 AND WHY (run-binding-glade-spec.md; field test 2, DECISIONS 121):
//  * GATE IN RANGE, edited run (§3.8, O1 re-key): runs no longer carry the vanilla Owner (it let
//    the bulldozer delete the whole resort through one run); the run→sign link is our own
//    SkiRunBinding. The edit branch is now ownership.IsBound(Temp.m_Original) — a binding whose
//    sign is LIVE (the old Owner test did not check the sign). Temps never carry SkiRunBinding
//    (Game.Tools/GenerateAreasSystem.cs:256-273 builds them from the archetype plus
//    Owner/OwnerDefinition only), so the test always resolves the Temp's original first. The
//    new-run branch is unchanged (TryNearestGate on the Temp's center), except (fixer round,
//    finding r3-plumbing-02) that it reads false while ownership cannot bind a new run this
//    load (ownership.BindsNewRuns), so the "will stay unbound" warning shows; DUMP says why.
//  * FEEDBACK GATE (fixer round, r3-plumbing-02): step 1 requires ownership.PrefabSetsReady, not
//    ownership.IsArmed. An ownership exception (sweep, or the load pass before the fixer round)
//    disables binding for the load; it used to switch the heat-map, crux ring and tooltip
//    (Length line included) off with it, although nothing here needs binding — the run cascade
//    made the same choice (spec §3.6).
//  * WALL-CLOCK HEARTBEAT (§5.3, F7). R2's RefreshFrames = 30 heartbeat forced a FULL resample +
//    classify every 30 frames even with nothing moving (reuse required m_Frame − m_GoodEpoch <
//    30, which the heartbeat itself could never satisfy): 10.86 ms every 30 frames on the field's
//    largest run (log 1150-1152: 16,532 sampled, 0 reused). Replaced by an incremental VERIFY:
//    on hold frames (eligible Temp, no recompute) once m_GoodGrid's heights are HeartbeatSeconds
//    (2 s, wall clock) old, VerifyStep re-samples up to VerifyNodesPerFrame (4,096) of its Need
//    nodes per frame with the same 2-argument SampleHeight and compares them BIT-EXACT with the
//    stored heights; a mismatch forces a full resample, a complete pass re-stamps
//    m_GoodVerifiedAt with the pass's START time. NeedsRecompute has no time-based trigger any
//    more. Why sampling and not a terrain version: the only player height writer is the
//    terraform brush (TerrainSystem.ApplyBrush, TerrainSystem.cs:3942-3953; 1.6.2 :3968), which
//    cannot run while the area tool is active (one active tool, ToolSystem.cs:84-98, same in
//    1.6.2); the simulation can still re-shape terrain under building lots, stamps, pillars and
//    nets (TerrainSystem's change queries m_BuildingsChanged / m_RoadsChanged / m_AreasQuery,
//    TerrainSystem.cs:2728-2853; 1.6.2 :2741-2880); the ready-update lists that would give a
//    version are private (GroundHeightSystem.cs:1015-1021, same in 1.6.2; reflection banned).
//    Bit-exact is sound because a stored height IS a SampleHeight result of the same world
//    point (FillHeights) or a copy of one (reuse), and SampleHeight is a pure function of the CPU
//    heights and the point (TerrainUtils.cs:82-137). Cost: <= VerifyNodesPerFrame bilinear
//    samples on a verify frame (the spec estimates <= ~1.2 ms under Mono, ~4-5 frames every 2 s
//    on the field's largest run; unmeasured until the field test — LastVerifyMs / PeakVerifyMs).
//  * STALE STATE AT LOAD (§5.4, F7): a DUMP 21 s after a load printed the previous city's rating
//    and step (field log 1123-1132). ResetPublishedState() now clears every published field
//    first in OnGameLoadingComplete, and the DUMP says "rating: none since load" while
//    m_GoodGrid is null.
//  * RUN LENGTH (§5.1, D14): Readout.Length = SlopeResult.Length (horizontal meters; ring =
//    max(equivalent rectangle, fall-line extent), polyline = arc length — SkiSlopeClassifier
//    header "RUN LENGTH"); DUMP prints Length / LengthRect / AxisLength; dev-tab row.
//  * DUMP HOTKEY (§5.2): SkiGladeHotkeySystem calls DumpToLog() from UIUpdate (Ctrl+Alt+G by
//    default) while the area tool is active, so the dump sees the live readout with the run on
//    the cursor (the dev-tab button lost the Temp every time: field log 451-452 … 1146-1147
//    "temp none … live False"). File IO now happens in the button OR the hotkey handler.
//
// WHAT CHANGED IN R2 AND WHY (DECISIONS 116; spec §9.1 field evidence from deploy 9FA27F71):
//  * Rating v2 lives entirely in SkiSlopeClassifier (§9.2 bands 6/25/40/60 %, §9.3 rating = the
//    crux of the easiest line from the top zone to the bottom zone; the R1 statistic survives as
//    SteepestGrade, "the steepest patch, avoidable or not"). This system only PUBLISHES the new
//    outputs (Readout.SteepestGrade / .Connected, the crux ring, dev-tab rows, the capture file);
//    it re-derives none of them.
//  * Mono cost. The field measured 4.0-5.0 ms per recompute (fill 1.8-3.8, classify 1.1-2.1;
//    SlopeBatch on CoreCLR said 0.5-0.9 ms, so Mono ≈ 5-8×, not the assumed 2-3×), 17.9-19.9 ms
//    on the first call of a load (JIT suspected), and a main-thread per-frame draw of 0.59 ms at
//    358 strips / 2.78 ms at 1,732. Four measures (§9.5):
//      1. JIT PRE-WARM at arm time: one Plan + Classify (+ strip build) of a synthetic ring and a
//         synthetic 2-node line on synthetic heights into scratch objects — no terrain access, no
//         overlay access — plus IJobExtensions.EarlyJobInit<StripDrawJob>() (the draw job's
//         reflection data; R2 fixer round, see PrewarmJit). Duration logged once per load
//         (PrewarmMs). The first Draw of the PROCESS still pays the first-call JIT of the job
//         schedule: logged once as FirstDrawMs and kept out of PeakDrawMs and the slow-draw WARN.
//      2. DUTY-CYCLE THROTTLE: a recompute caused only by node MOVEMENT may not start sooner than
//         clamp(ThrottleFactor · LastRecomputeMs, 0, MaxThrottleMs) after the START of the
//         previous one (wall clock: UnityEngine.Time.realtimeSinceStartupAsDouble), on top of
//         MinRecomputeFrames — so recompute time is <= 1/8 of wall time while the cursor moves.
//         Node-count / Complete / Temp-entity changes and (R3) a verify mismatch
//         (m_ForceFullResample) stay immediate. A deferred move recompute still fires once the
//         time has passed even if the cursor stopped (the moved test compares against the LAST
//         RECOMPUTE's node snapshot).
//      3. HEIGHT REUSE with two ping-pong SlopeGrid objects. Plan poisons every Need height with
//         NaN (SkiSlopeClassifier.Plan), so reuse COPIES from the previous grid object into the
//         freshly planned one: a Need node whose world lattice index (I0 + i, J0 + j) was a Need
//         node of the last fully filled and rated grid, with a finite height, copies it instead of
//         calling SampleHeight. The lattice is world-anchored (header GRADE), so the copied value
//         is the one SampleHeight would return at the same world point for the same terrain.
//         Guards: same Step and same lattice Origin (exact compare), and (R3, replacing R2's
//         frame-age guard) the source's heights were last VERIFIED less than
//         Tuning.HeartbeatSeconds ago (m_GoodVerifiedAt: a full resample stamps its start, a reuse
//         recompute inherits it — the age of the oldest copied height — and a complete
//         VerifyStep pass re-stamps it with the pass's start) — so a verify mismatch, any
//         step/origin change, and re-entry after an idle gap all resample in full (terrain edits
//         surface within about HeartbeatSeconds plus one verify pass). Planning always goes into
//         the grid that is NOT the last good one, so the last rated polygon survives failed
//         recomputes and Invalidate (the dump needs it after the tool is closed).
//      4. DRAW IN A BURST IJob: strips live in a persistent NativeArray<Strip>; StripDrawJob writes
//         them (and the crux ring) into the OverlayRenderSystem.Buffer, scheduled on GetBuffer's
//         dependencies and registered with AddBufferWriter (header OVERLAY). LastDrawMs now times
//         GetBuffer + schedule only.
//  * CAPTURE: "Dump to log" (dev tab) also writes the last rated polygon as a `.skislope` text
//    file under <Application.persistentDataPath>/ModsData/SkiResortMod/captures/ (format §9.5,
//    invariant culture; R2b: floats "G9", doubles "G17") that tests/SlopeBatch replays as a
//    regression fixture (§9.4 R2-k). File IO happens only inside that button handler (R3: or the
//    dump hotkey's handler, SkiGladeHotkeySystem).
//
// WHAT CHANGED IN R2b AND WHY (spec §9.8, DECISIONS 118; the rule lives in SkiSlopeClassifier,
// whose header has the numbers). R2's end zones were ranked by raw node height and capped by
// AREA, so on any polygon with side ground (valley walls, bowl rims, ridge flanks, a side bank, a
// big corner poke) the zones sat on that side ground and the easiest line had to cross it
// (valley 600 × 120 at 15 % rated Blue 31.8-33.6 %, the F116 meadow with an 80 × 100 m poke
// Black 42-43 %), and narrow trails lost 100-200 m at each end; at step multiple >= 2 an
// 8-connected path slipped diagonally through one-cell-wide bands (a full-width 12 m bank read up
// to 8 % low). R2b ranks the zones along the mean fall line (height fallback below 3 % mean
// gradient when the gradients also disagree — R2b fixer round, below), caps each at ~20 m of run
// at the run's mean width, and uses the diagonal rule at m >= 2. This system's part:
//  * it sets SlopeGrid.DiagonalRule = (step multiple >= 2) on every recompute (the classifier
//    does not know m), right before Plan, on the ping-pong target grid;
//  * it publishes the result's ZoneByFallLine / DiagonalRule (dev tab, DUMP) and logs the new
//    constants EndZoneDepthM / EndZoneMinMeanGrade in the ARMED line;
//  * the capture writer uses "G9" / "G17" instead of "R" (the .NET Framework's "R" has a known
//    double round-trip defect); SlopeBatch's loader reads both, and replays a capture with the
//    diagonal rule when its multiple >= 2, exactly as this system rated it.
// R2b FIXER ROUND: the classifier's key gate gained a second clause (fall-line key also when the
// box gradients agree, FallCoherence >= EndZoneMinCoherence = 0.5: a long gentle run below 3 %
// mean gradient no longer falls back to the height key and skips a pitch near an end). This
// system publishes the gate's two inputs of the last rated recompute (ZoneMeanGradient,
// ZoneFallCoherence: dev tab, DUMP) and logs EndZoneMinCoherence in the ARMED line.
//
// PHASE. Registered UpdateBefore OverlayRenderSystem in Rendering (SystemOrder.cs:687; 1.6.2
// :688). MainLoop runs ToolSystem → ModificationSystem → UIUpdate → Rendering, paused or not
// (SystemOrder.cs:58, 60, 67, 68). The Temp area is (re)built in Modification1
// (GenerateAreasSystem, SystemOrder.cs:98) and triangulated in Modification2B
// (GeometrySystem.cs:752, 780; SystemOrder.cs:124), so by Rendering it is the frame's final
// polygon. The overlay lists we write are consumed by OverlayRenderSystem.OnUpdate later in the
// same phase and cleared there (OverlayRenderSystem.cs:781, 790), so we draw every frame.
//
// SOURCE OF TRUTH = the Temp Area entity, never the tool's control points or tool.prefab:
//   * The Temp's PrefabRef is the ORIGINAL run's prefab when editing (GenerateAreasSystem.cs:
//     172-175; new entity :258-260), whatever Surface prefab the player selected — the tool
//     snaps to any area of the same AreaType (AreaToolSystem.cs:320-322). So the run test is on
//     the Temp's own PrefabRef (ownership.IsRunPrefab).
//   * A 1-node Temp (TempFlags.Create) exists whenever the tool is active over terrain
//     (AreaToolSystem.cs:1817-1893), so we require >= 2 nodes.
//   * Hovering a node/edge of an existing run yields a full Complete copy with TempFlags.Modify
//     and m_Original = run (AreaToolSystem.cs:1590-1595; GenerateAreasSystem.cs:172-175). We DO
//     show feedback for it (it answers "what is this run rated").
//   * Nodes are terrain-snapped and the closing duplicate is stripped with Complete set
//     (GenerateAreasSystem.cs:282-316). An open polygon of >= 3 nodes is triangulated as if
//     closed (GeometrySystem.cs:99-126), which is what the classifier's ring mode also does.
//   * Last emit's unrecycled Temps still carry Deleted during Rendering (Cleanup runs after the
//     MainLoop) — excluded by the query. Dragging a shared vertex yields several Temps — we take
//     the first eligible one with the most nodes.
//   * The Temp persists on still frames (vanilla emit-and-hold, AreaToolSystem.cs:3700-3703;
//     1.6.2 :3711-3714), so a cache keyed on (entity, node copy) is enough.
//
// GRADE comes from height differences on a lattice, never from triangle planes: area triangles
// are not terrain-conformed between nodes (GeometrySystem.cs:101-113) and the 3-argument
// SampleHeight normal is in raw heightmap units (TerrainUtils.cs:139-157). The lattice is
// anchored to the WORLD heightmap: texel center i sits at world i / scale.x - offset.x
// (TerrainUtils.cs:35-38; offset includes the half-texel shift, TerrainSystem.cs:2621-2622), so
// origin = -hd.offset.xz and texel = 1 / hd.scale.x = 3.5 m on a 4096-wide heightmap
// (TerrainSystem.cs:2618-2620; 1.6.2 :2630-2632). Step = integer multiple m of the texel
// (SkiSlopeClassifier.StepMultiple, spec §3 "Step"). Cells away from the moving edge therefore
// keep bit-identical values while the player draws (no shimmer) — and measure 3 relies on it.
//
// TERRAIN READ. GetHeightData(waitForPending: false) (TerrainSystem.cs:2603; 1.6.2 :2615) is
// called fresh on every recompute — never cached across frames, EnsureCPUHeights can dispose and
// reallocate the array (TerrainSystem.cs:3538-3552; 1.6.2 :3564). Heights are read with the
// 2-argument TerrainUtils.SampleHeight (TerrainUtils.cs:82-137, plain bilinear). A synchronous
// main-thread read needs no AddCPUHeightReader: m_CPUHeights is written only on the main thread
// after completing readers; vanilla precedent LandValueTooltipSystem.cs:252-265 and
// SelectionToolSystem.cs:1151 (digest cluster 2 Q4; the Unity-side guarantees are unverified).
// A heightmap narrower than 4096 (including the (2,2,2) pre-readback fallback,
// TerrainSystem.cs:2618) skips the rating with a status note. Height reuse copies only values
// that came from this same read path (no new engine assumption beyond "SampleHeight is a pure
// function of the CPU heights and the world point", which TerrainUtils.cs:82-137 shows).
//
// OVERLAY (R2: Burst job, spec §9.5 measure 4). Verified in OverlayRenderSystem.cs:
//   * GetBuffer(out dependencies) (:603-634) hands out a Buffer over the shared NativeLists and
//     returns m_BufferWriters as the dependency (:632); AddBufferWriter(handle) folds a job into
//     m_BufferWriters (:636-639); OverlayRenderSystem.OnUpdate COMPLETES m_BufferWriters and
//     resets it FIRST (:740-741), before it uploads and clears the lists (:774-790). So every
//     registered writer has finished by the end of the same frame's Rendering phase.
//   * Vanilla job pattern, AreaBorderRenderSystem.cs: the [BurstCompile] IJob (:40-41) declares
//     `public OverlayRenderSystem.Buffer m_OverlayBuffer;` with NO attribute (:97); OnUpdate fills
//     it with GetBuffer(out dependencies) (:704), schedules on
//     CombineDependencies(base.Dependency, chunk-list handle, dependencies) (:705), registers the
//     handle with AddBufferWriter (:707) and stores it as base.Dependency (:708). We combine only
//     the overlay dependencies: StripDrawJob reads no ECS data (only our own NativeArray), so
//     base.Dependency is neither an input nor assigned — assigning it would make later writers
//     of the Temp-area component types wait on a job that never reads them.
//   * SAFETY of m_Strips: (a) it is written only in PublishStrips, after CompleteDraw(); (b) every
//     new job depends on our previous one — GetBuffer returns m_BufferWriters, which holds our
//     previous handle until OverlayRenderSystem.OnUpdate completes and resets it (:632, :638,
//     :740-741) — so completing the newest handle (m_DrawHandle) completes all of ours;
//     (c) OnStopRunning and OnDestroy complete m_DrawHandle, and OnDestroy disposes m_Strips only
//     after that. The job copies Count and the crux fields by value at schedule time.
//   * DrawLine overload :207 (→ DrawCurveImpl :267-298, drops lines shorter than 0.01 m at :269)
//     with StyleFlags.Projected (:99-104): a Projected primitive is a box spanning the whole
//     terrain height range (FitBox :321-330 for curves, :300-310 for circles; Y range from
//     GetBuffer :633), so the cost is count × footprint of GPU overdraw — hence row run-length
//     strips and Tuning.MaxStrips, enforced by row decimation (stride 2..Tuning.MaxRowStride)
//     before any truncation. We draw INSIDE the polygon only at low alpha; the border and node
//     discs are vanilla's (AreaBorderRenderSystem.cs:363-427) and are not redrawn here (F7: the
//     wider copy of them is SkiRunOutlineSystem's, from the same Temp areas). Whether
//     roundness (0,0) gives perfectly square strip ends is unverified (shader not traced).
//   * Crux ring: DrawCircle overload :138 (→ DrawCircleImpl :159-187; Projected → FitBox
//     :300-310, which replaces the center's y by the terrain range, so the ring's y does not
//     matter). Ring style = vanilla range circles: fill = the outline color with alpha 0
//     (EffectRangeRenderSystem.cs:166-169; GuideLinesSystem.cs:3124-3128); direction (0, 1) as
//     AreaBorderRenderSystem.cs:517. That the outline width is in meters is unverified (shader
//     not traced; vanilla passes meter-scaled values, e.g. radius · 0.02 at
//     EffectRangeRenderSystem.cs:169).
//   * BURST. Vanilla runs the same DrawLine / DrawCircle (including Color.linear → the
//     Mathf.GammaToLinearSpace internal call, checked in UnityEngine.CoreModule with Cecil) inside
//     [BurstCompile] jobs (AreaBorderRenderSystem.cs:40-41). Whether the mod post-processor's
//     Burst AOT compiles StripDrawJob is unverified until the integrator's build; the
//     SkiSnowInjectionSystem convention is that [BurstCompile] is best-effort and the job then
//     runs as managed code on a worker thread (also unverified, but either way off the main
//     thread).
//
// COST CONTRACT (R2, R3). Idle: a settings bool, two flags and one reference compare. Hold frames:
// one small query, the throttle/move test, the draw = GetBuffer + one IJob schedule +
// AddBufferWriter (LastDrawMs / PeakDrawMs, raw Stopwatch timestamps; WARN once per load above
// Tuning.SlowDrawWarnMs), and (R3) one clock read for the verify — plus, once the good grid is
// Tuning.HeartbeatSeconds old, a verify step of <= Tuning.VerifyNodesPerFrame SampleHeight calls
// (LastVerifyMs / PeakVerifyMs; no allocation, no formatting). Recompute (plan + fill + classify +
// strips, Stopwatch-timed into public fields): at most every Tuning.MinRecomputeFrames frames AND
// at most a 1/ThrottleFactor duty cycle while nodes move; R3 removed the time-based recompute (a
// still run is re-verified, not re-rated); the fill samples only Need
// nodes the previous grid did not already hold (HeightsReused / HeightsSampled). The fill, the
// strip build and the capture writer walk Plan's per-row Need / Mask RUN LISTS (R2 fixer round),
// so their managed work is O(Need) / O(inside) even for U- or C-shaped runs whose rows cross two
// arms (R1's first/last spans walked the outside gap between them). Zero managed
// allocation in steady state: reused node arrays, two SlopeGrids + one SlopeResult (grow, never
// shrink), fixed-size managed strip stage + persistent NativeArray allocated once in OnCreate,
// copied with NativeArray.Copy (one memcpy). Per load: the pre-warm's scratch objects. Per dump:
// the log text and the capture file (button or dump-hotkey handler only). Mono costs of every
// measure are unmeasured until Richard's field test (§9.7 step 5; R3: spec §7 step 18).
//
// SCOPE: Game mode only (armed in OnGameLoadingComplete). Read-only on the ECS side: never writes
// a component, never adds Updated to a run, never touches ownership. Defensive contract per
// house rules: ARMED line per load, one-shot "!!!" catch that disables the feature until the
// next load.

using System;
using System.Globalization;
using System.Text;
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
        // ─────────────────────────────────────────────────────────────────────────────────────
        // Public surface (spec §2 + §9.5 — SkiRunTooltipSystem compiles against these; do not
        // rename)
        // ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>What the tooltip shows. Band is the DISPLAY band (with
        /// SkiSlopeClassifier.DisplayHysteresis while the same Temp is live) of RatingGrade.
        /// RatingGrade = the crux of the easiest line (ring mode, spec §9.3) or the k-th largest
        /// 20 m window (polyline mode); SteepestGrade = the R1 statistic, "the steepest patch,
        /// avoidable or not" (set equal to RatingGrade in polyline mode, where a line has one
        /// path); Connected = false when the run's Mask fell apart and the rating fell back to
        /// P90. Grades are fractions (0.15 = 15 %), Drop in meters. Length (R3,
        /// run-binding-glade-spec.md §5.1) = SlopeResult.Length, horizontal meters. GateInRange:
        /// new run = a live sign within the tether of the Temp's center; edited run (R3 §3.8) =
        /// ownership.IsBound(Temp.m_Original).</summary>
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
            // Round E1 (D21, econ-round spec ch2 §6.4): the fee inputs, set at every recompute whether
            // or not the rating succeeds; -1 / 0 after Invalidate.
            public int Kind;          // SkiRunOwnershipSystem.KindOf(the Temp's prefab) = RunKind; -1 unknown
            public float AreaM2;      // plan area of the Temp's nodes (SkiRunRater.ShoelaceArea)
        }

        /// <summary>All tuning constants; every one is logged in the per-load ARMED line.</summary>
        public static class Tuning
        {
            /// <summary>Minimum frames between two recomputes while nodes keep moving.</summary>
            public const int MinRecomputeFrames = 3;
            /// <summary>R3 (run-binding-glade-spec.md §5.3; replaces R2's RefreshFrames = 30 frame
            /// heartbeat): wall-clock seconds (UnityEngine.Time.realtimeSinceStartupAsDouble, the
            /// throttle's clock) after which the last good grid's heights are re-verified
            /// (VerifyStep) — and the maximum age of a reused height: a recompute reuses heights
            /// only while now − m_GoodVerifiedAt is below it. No terrain-version signal exists
            /// (header R3).</summary>
            public const double HeartbeatSeconds = 2.0;
            /// <summary>R3 §5.3: Need nodes re-sampled per verify frame (bit-exact compare).</summary>
            public const int VerifyNodesPerFrame = 4096;
            /// <summary>A node counts as moved when its xz moved at least this far (m).</summary>
            public const float NodeMoveEpsilonM = 0.1f;
            /// <summary>Duty-cycle throttle (spec §9.5 measure 2): a movement-only recompute may
            /// not start sooner than ThrottleFactor × the last recompute's ms after the previous
            /// one started …</summary>
            public const float ThrottleFactor = 8f;
            /// <summary>… capped at this many ms.</summary>
            public const float MaxThrottleMs = 250f;
            /// <summary>Overlay fill alpha (inside the polygon only).</summary>
            public const float FillAlpha = 0.35f;
            /// <summary>Hard cap on Projected strips per frame (GPU overdraw, see header).</summary>
            public const int MaxStrips = 3000;
            /// <summary>Width (m) of a polyline-mode segment strip.</summary>
            public const float PolylineWidthM = 4f;
            /// <summary>Below this heightmap width the rating is skipped (spec §4 step 7).</summary>
            public const int MinHeightmapResolution = 4096;
            /// <summary>Log one WARN per load when a recompute exceeds this (field, R1 deploy:
            /// 4.0-5.0 ms under Mono; DECISIONS 116).</summary>
            public const float SlowRecomputeWarnMs = 4f;
            /// <summary>Largest ring-mode row stride: when full row resolution exceeds MaxStrips,
            /// the smallest stride 2..MaxRowStride that fits draws one representative row per
            /// group of rows (spec §4 "Strips"); only if this stride overflows is the list
            /// truncated.</summary>
            public const int MaxRowStride = 8;
            /// <summary>Log one WARN per load when the per-frame draw (GetBuffer + job schedule +
            /// AddBufferWriter since R2) exceeds this.</summary>
            public const double SlowDrawWarnMs = 2.0;
            /// <summary>Crux ring diameter = max(CruxRingMinDiameterM, CruxRingStepFactor ×
            /// step) (spec §9.5 "Crux marker").</summary>
            public const float CruxRingMinDiameterM = 14f;
            public const float CruxRingStepFactor = 3f;
            /// <summary>Crux ring outline width passed to DrawCircle (meters assumed, unverified —
            /// see header OVERLAY).</summary>
            public const float CruxRingOutlineM = 1f;
            /// <summary>F7 C2 (header ROUND F7): at most one `RunFeedback [validation]` line per this many
            /// wall-clock seconds; a burst of changes is folded into one summary (the LogWindow rule, 10 s).</summary>
            public const float ValidationLogWindowS = 10f;
        }

        /// <summary>Band colors, index = (int)SlopeBand (spec §4 "Strips"), alpha = FillAlpha.</summary>
        private static readonly UnityEngine.Color[] BandColors =
        {
            new UnityEngine.Color(0.60f, 0.60f, 0.60f, Tuning.FillAlpha),   // Flat
            new UnityEngine.Color(0.20f, 0.75f, 0.30f, Tuning.FillAlpha),   // Green
            new UnityEngine.Color(0.20f, 0.45f, 0.95f, Tuning.FillAlpha),   // Blue
            new UnityEngine.Color(0.08f, 0.08f, 0.08f, Tuning.FillAlpha),   // Black
            new UnityEngine.Color(0.85f, 0.20f, 0.10f, Tuning.FillAlpha),   // DoubleBlack
        };

        /// <summary>Crux ring: white outline, no fill (fill = outline with alpha 0, the vanilla
        /// range-circle style, EffectRangeRenderSystem.cs:166-169).</summary>
        private static readonly UnityEngine.Color CruxOutlineColor = new UnityEngine.Color(1f, 1f, 1f, 0.9f);
        private static readonly UnityEngine.Color CruxFillColor = new UnityEngine.Color(1f, 1f, 1f, 0f);

        /// <summary>The tooltip's input. Returns true only when, as of this system's last
        /// Rendering-phase update (the previous frame for the UITooltip phase, spec §5), an
        /// eligible Temp run existed, the feature is on and the rating is usable
        /// (<c>readout.Valid</c>). The out value is always filled (Valid=false otherwise), so
        /// a caller may still read Status.</summary>
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

        /// <summary>E1 (ch2 §6.4): the fee inputs of the run being drawn — live and fresh exactly like the
        /// readout (same gates, same one-frame lag), but independent of whether the rating succeeded
        /// (a too-large or no-terrain run still shows its fee).</summary>
        public bool TryGetFeeInputs(out int kind, out float areaM2, out bool isNewRun)
        {
            kind = m_Readout.Kind;
            areaM2 = m_Readout.AreaM2;
            isNewRun = m_Readout.IsNewRun;
            var settings = Mod.Settings;
            return m_ReadoutLive && !m_Disabled && m_Armed && settings != null && settings.ShowRunGradeWhileDrawing &&
                   UnityEngine.Time.frameCount - m_ReadoutFrame <= 1 && kind >= 0;
        }

        // Plain public fields for the dev tab (cheap getters; written by OnUpdate / the dump
        // button only).
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
        /// <summary>Per-frame draw cost, ms (R2: GetBuffer + job schedule + AddBufferWriter).
        /// PeakDrawMs (reset per load) excludes the first draw of the PROCESS, reported as
        /// FirstDrawMs (not reset per load: the JIT it pays happens once per process).</summary>
        public double LastDrawMs, PeakDrawMs, FirstDrawMs;
        public long Recomputes;
        public string LastError = "-";
        // R2 (spec §9.5 dev-tab additions).
        /// <summary>P90 of the per-cell values (the disconnected-mask fallback rating).</summary>
        public float P90Grade;
        /// <summary>End-zone sizes in cells (top / bottom, spec §9.3).</summary>
        public int TopZoneCells, BottomZoneCells;
        /// <summary>R2b (spec §9.8): the last rated recompute's end-zone key (true = position
        /// along the mean fall line, false = node height) and connectivity rule (true = the
        /// diagonal rule, set here for step multiple >= 2).</summary>
        public bool ZoneByFallLine, DiagonalRule;
        /// <summary>R2b fixer round: the key gate's two inputs of the last rated ring recompute —
        /// |mean box gradient| (vs EndZoneMinMeanGrade) and FallCoherence (vs
        /// EndZoneMinCoherence); 0 in polyline mode.</summary>
        public float ZoneMeanGradient, ZoneFallCoherence;
        /// <summary>Crux cell (grid-local) of the last rated recompute; false in polyline mode.</summary>
        public bool CruxValid;
        public int CruxI, CruxJ;
        /// <summary>Last fill: Need heights copied from the previous grid / sampled from the
        /// terrain (spec §9.5 measure 3).</summary>
        public int HeightsReused, HeightsSampled;
        /// <summary>Current throttle window (ms) and, on a frame where a movement recompute is
        /// being deferred, the ms left until it may start (0 otherwise).</summary>
        public float ThrottleWindowMs, ThrottleRemainingMs;
        /// <summary>Movement recomputes deferred by the throttle since the load (frames).</summary>
        public long ThrottledFrames;
        /// <summary>JIT pre-warm duration of this load (ms; spec §9.5 measure 1).</summary>
        public float PrewarmMs;
        /// <summary>Full path of the last capture file written by "Dump to log", or a reason.</summary>
        public string LastCapturePath = "-";
        // R3 (run-binding-glade-spec.md §5.1, §5.3).
        /// <summary>The last rated recompute's length split (m): equivalent-rectangle and
        /// fall-line extent (Readout.Length is their max in ring mode, the arc in polyline mode).</summary>
        public float LengthRect, AxisLength;

        /// <summary>Fixer round (r3-plumbing-02): the last recompute's NEW run read GateInRange
        /// false because ownership cannot bind new runs this load (ownership.BindsNewRuns false),
        /// not because of the distance. Reset at load and on every UpdateGateInRange.</summary>
        public bool NewRunBindingOff;
        /// <summary>Heartbeat verify (§5.3): complete passes that confirmed the heights, passes
        /// that found a changed height (each forced a full resample), Need nodes compared on the
        /// last verify frame, and its / the peak cost (ms, Stopwatch timestamps; reset per load).</summary>
        public long Verifies, VerifyChanges;
        public int VerifyNodesLast;
        public float LastVerifyMs, PeakVerifyMs;
        /// <summary>Wall-clock time (realtimeSinceStartupAsDouble) the last good grid's heights
        /// were last known current; NegativeInfinity = never (or invalidated by a mismatch).
        /// Age = now − this (dev tab, DUMP).</summary>
        public double GoodVerifiedAt => m_GoodVerifiedAt;
        /// <summary>True between this load and its first rated recompute (DUMP: "rating: none
        /// since load").</summary>
        public bool NoRatingSinceLoad => m_GoodGrid == null;
        /// <summary>F7 C2 (header ROUND F7): the game's verdict on the run being drawn, as of the last
        /// recompute ("-" while no run is on the cursor or none was read; "clear" = nothing reported), and the
        /// changes logged (offered to the LogWindow) since the load. Written on a change only (event-time text).</summary>
        public string ValidationText = "-";
        public long ValidationChanges;
        public bool IsArmed => m_Armed;
        public bool IsDisabled => m_Disabled;
        /// <summary>The current readout regardless of liveness (dev tab only).</summary>
        public Readout CurrentReadout => m_Readout;
        public bool ReadoutLive => m_ReadoutLive;

        // ─────────────────────────────────────────────────────────────────────────────────────
        // Draw job (spec §9.5 measure 4; header OVERLAY)
        // ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>One Projected strip: segment a → b, width (m), color (spec §9.5 "a, b,
        /// width, color").</summary>
        private struct Strip
        {
            public float3 A, B;
            public float Width;
            public UnityEngine.Color Color;
        }

        /// <summary>Writes Count strips and the optional crux ring into the overlay buffer.
        /// Field layout follows AreaBorderRenderSystem.cs:40-41, 97 (the Buffer field carries no
        /// attribute). Reads only our own NativeArray; no ECS data.</summary>
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

        // ─────────────────────────────────────────────────────────────────────────────────────
        // Systems, queries, caches
        // ─────────────────────────────────────────────────────────────────────────────────────

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

        // Gate 3 cache: AreaGeometryData.m_Type of the tool's prefab, re-read on reference change.
        private AreaPrefab m_CachedToolPrefab;
        private bool m_CachedToolPrefabIsSurface;

        // Frame counter (this system's updates; runs every frame, idle included) for the
        // recompute policy (MinRecomputeFrames). R3: the height-reuse age is wall clock now
        // (m_GoodVerifiedAt), one clock only.
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

        // Classifier I/O (reused; buffers grow, never shrink). Two grids ping-pong (measure 3):
        // m_Grid = the grid of the latest recompute (what BuildStrips and the dev tab read);
        // m_GoodGrid = the last FULLY FILLED and RATED grid (reuse source and capture source; never
        // planned into, so it survives failed recomputes and Invalidate).
        private readonly SlopeGrid m_GridA = new SlopeGrid();
        private readonly SlopeGrid m_GridB = new SlopeGrid();
        private SlopeGrid m_Grid;
        private SlopeGrid m_GoodGrid;
        private readonly SlopeResult m_Result = new SlopeResult();

        // R3 heartbeat verify (spec §5.3; header R3). m_GoodVerifiedAt = wall-clock time at which
        // every height of m_GoodGrid was last known current (a full resample: its start; a reuse
        // recompute: inherited; a complete verify pass: the pass's start; a mismatch or a load:
        // NegativeInfinity, so the reuse guard fails until a full resample). The cursor walks
        // m_GoodGrid's Need runs: row j, run k within the row, column i within the run;
        // m_VerifyRow < 0 = no pass in progress. Reset whenever m_GoodGrid is swapped.
        // m_GoodStale = a pass found a changed height in m_GoodGrid: it is never verified (or
        // reused) again — only replaced — so a Temp that keeps failing to rate cannot alternate
        // mismatch → recompute → mismatch every other frame against an old grid.
        private double m_GoodVerifiedAt = double.NegativeInfinity;
        private bool m_ForceFullResample;
        private bool m_GoodStale;
        private int m_VerifyRow = -1, m_VerifyRun, m_VerifyCol;
        private double m_VerifyPassStart;
        private int m_StepMultiple = 1;

        // Display band hysteresis (reset when the Temp entity changes).
        private bool m_ShownBandValid;
        private SlopeBand m_ShownBand;

        // Readout.
        private Readout m_Readout;
        private bool m_ReadoutLive;
        private int m_ReadoutFrame = int.MinValue / 2;
        private Entity m_EligiblePrefab = Entity.Null;   // E1 (ch2 §6.4): the chosen Temp's prefab (the fee's kind)

        // Strips: built into a managed stage, then copied once into the persistent NativeArray
        // the draw job reads (allocated once; never grown).
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

        // F7 C2 (header ROUND F7): the eligible Temp's validation state at the last recompute. Masks = bit
        // (int)Game.Tools.ErrorType of each icon by priority; tags = ValTag*. m_ValSession = a state was read
        // since the last Invalidate (a run is on the cursor); m_ValOff = the watch threw this load.
        private const byte ValTagError = 1;     // the Temp carries Game.Tools.Error
        private const byte ValTagWarning = 2;   // the Temp carries Game.Tools.Warning (another Temp's error names it)
        private const byte ValTagOther = 4;     // an icon without a readable ToolErrorData type
        private bool m_ValSession;
        private bool m_ValOff;
        private uint m_ValErrors, m_ValWarnings, m_ValInfos;
        private byte m_ValTags;
        private uint m_ValFoldErrors, m_ValFoldWarnings, m_ValFoldInfos;   // union over the folded changes
        private readonly LogWindow m_ValLog = new LogWindow(Tuning.ValidationLogWindowS);
        // F7 fixer (header ROUND F7, "elsewhere"): the click is refused when ANY entity carries Game.Tools.Error
        // (ToolBaseSystem.cs:313, :533-540), and a moved shared corner gives the neighbor run its own Temp whose
        // InvalidShape names only that neighbor (AreaToolSystem.cs:1542-1560; ValidationHelpers.cs:887-943). So the
        // watch also reads the Error carriers other than the eligible Temp: their error types (union), their count
        // and the prefab of the lowest-index one — part of the change key and of the line.
        private EntityQuery m_ErrorQuery;
        private uint m_ValElseErrors;
        private int m_ValElseCount;
        private Entity m_ValElsePrefab;
        private uint m_ValFoldElse;

        // ─────────────────────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────────────────────

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

            // Spec §4: All{Area, Temp, Node, PrefabRef, Surface} None{Deleted}. Surface narrows to
            // SURFACE areas (every SurfacePrefab archetype carries Game.Areas.Surface,
            // Game.Prefabs/SurfacePrefab.cs:21-23). No RequireForUpdate: the system must run to
            // invalidate the readout when the tool goes idle.
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
            // Entities behavior (type handles add reader dependencies), unverified in Game.dll;
            // EntityManager.GetComponentData completes writers anyway.
            GetComponentTypeHandle<Game.Areas.Geometry>(isReadOnly: true);
            // F7 fixer: the tool's own refusal set (ToolBaseSystem.cs:313, m_ErrorQuery). Read on recompute frames
            // only (WatchValidation): one IsEmptyIgnoreFilter, an entity array only when something carries Error.
            m_ErrorQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Tools.Error>());

            Mod.Log.Info($"{nameof(SkiRunDrawFeedbackSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // R3 (spec §5.4): first, so nothing of the previous city survives into this one's
            // DUMP or dev tab (field log 1123-1132).
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
            Invalidate("-");   // F7 C2: also writes a pending validation fold and ends the session
            m_ValOff = false;
            ValidationChanges = 0;
            m_ValLog.Reset();

            // HARD GATE: Game only (house rule; the run prefabs are only bound in game mode).
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

        /// <summary>R3 (run-binding-glade-spec.md §5.4): clears every PUBLISHED value of the
        /// previous load — the readout, the classifier result, the dev-tab / DUMP fields and the
        /// heartbeat state — so a DUMP after a load can never print the previous city's rating
        /// (field log 1123-1132: 21 s after a load, the previous city's band and "step x1 = 7.00
        /// m"). Called FIRST in OnGameLoadingComplete; field writes only (m_Result.Clear keeps
        /// its buffers).</summary>
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
            // Heartbeat (§5.3).
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
                CompleteDraw();   // header OVERLAY safety (c)
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
                // F7 C2: quit to desktop raises no load, but the logger still lives here (LogWindow.cs header).
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

        // ─────────────────────────────────────────────────────────────────────────────────────
        // JIT pre-warm (spec §9.5 measure 1)
        // ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Plan + Classify + BuildStrips of a synthetic 40 × 90 m ring and a synthetic
        /// 2-node 40 m line on a synthetic plane, into scratch objects, so the Mono JIT cost of
        /// the classifier (ring incl. the crux pass, and polyline) and the strip builder is paid
        /// at load, not on the first real recompute. No terrain, no overlay, no ECS access; the
        /// strip stage is reset afterwards (the NativeArray the draw job reads is not touched).
        /// Allocates its scratch objects once per load.
        ///
        /// Also IJobExtensions.EarlyJobInit&lt;StripDrawJob&gt;() (R2 fixer round): the first
        /// job.Schedule of a process otherwise creates the job's reflection data and JIT-compiles
        /// JobStruct&lt;StripDrawJob&gt; INSIDE the first Draw's timing window. Read with Cecil
        /// from the installed UnityEngine.CoreModule (game 1.6.2f1, Unity 2022.3.71f1; not part
        /// of the Game.dll decompile): EarlyJobInit&lt;T&gt; (public static, T : struct, IJob) calls
        /// JobStruct&lt;T&gt;.Initialize(), which calls JobsUtility.CreateJobReflectionData only
        /// while the SharedStatic jobReflectionData is IntPtr.Zero; Schedule&lt;T&gt; calls
        /// GetReflectionData&lt;T&gt; → the same Initialize. It schedules nothing and touches no
        /// container. Process-wide (the SharedStatic and the JIT outlive a load), so later loads
        /// are already warm. Whether the Burst function lookup happens inside
        /// CreateJobReflectionData is unverified; the remaining first-call JIT of
        /// Schedule&lt;StripDrawJob&gt; itself is kept out of PeakDrawMs by Draw (first draw of
        /// the process).</summary>
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

        // ─────────────────────────────────────────────────────────────────────────────────────
        // Per-frame gate ladder (spec §4 steps 1-8, cheapest first; no EntityQuery work idle)
        // ─────────────────────────────────────────────────────────────────────────────────────

        private void UpdateCore()
        {
            // 1. Feature switch and arming (ours: Game mode; ownership: its PREFAB SETS resolved —
            //    IsRunPrefab is meaningless otherwise). Fixer round (r3-plumbing-02): gated on
            //    ownership.PrefabSetsReady, NOT ownership.IsArmed — the choice the run cascade made
            //    (spec §3.6): since O1 a load-pass or sweep exception disables binding (IsArmed
            //    false), and that must not also switch off the slope feedback, which needs only the
            //    prefab sets (IsRunPrefab) and the binding-independent helpers (IsBound,
            //    TryNearestGate). Whether NEW runs can bind this load is reported separately
            //    (UpdateGateInRange, ownership.BindsNewRuns).
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

            // 3. Surface prefab selected (AreaToolSystem.prefab, :2637-2652; 1.6.2 :2648). Only
            //    Surface prefabs create or snap to our runs (AreaToolSystem.cs:320-322). H1
            //    (code review 2026-10-19): SkiRunAreaTypeSystem forges the cat-track prefab's
            //    m_Type Surface -> Space at every load, so "Surface" alone rejected it and the heat
            //    map / fee row / "too steep for a cat track" line never showed. The gate therefore
            //    accepts Surface OR a prefab entity in our run-prefab set (the set the Temp filter
            //    uses in PickEligibleTemp); the Temp query keeps its own Surface requirement.
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
                    m_CachedToolPrefabIsSurface = true;   // H1: the Space-forged cat track
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
                // Never assert: mid-draw over UI the Temp can vanish (digest cluster 2 pitfalls).
                Invalidate("no eligible Temp run (>= 2 nodes, ours, not Delete/Hidden)");
                return;
            }
            EligibleTemp = temp;
            EligibleNodes = nodeCount;
            EligibleIsNew = tempData.m_Original == Entity.Null;
            EligibleComplete = complete;

            // 6. Recompute decision (R2: duty-cycle throttle on movement-only recomputes). R3
            //    (spec §5.3): on a frame WITHOUT a recompute, the heartbeat verify — only here, on
            //    the eligible-Temp path: m_GoodGrid survives Invalidate (capture), so an ungated
            //    verify would sample terrain in normal play.
            if (NeedsRecompute(temp, nodeCount, complete))
            {
                Recompute(temp, nodeCount, complete, tempData);
                WatchValidation(temp, nodeCount, complete, EligibleIsNew);   // F7 C2: recompute frames only
            }
            else
            {
                VerifyStep();
                FlushValidationFold();   // F7 fixer: a fold waiting while the cursor rests (one int test when none)
            }

            // Readout is live for this frame (Valid says whether the rating is usable).
            m_ReadoutLive = true;
            m_ReadoutFrame = UnityEngine.Time.frameCount;

            // 8. Draw from the cached strips every frame (Burst job).
            Draw();
        }

        /// <summary>First eligible Temp with the most nodes; copies its nodes into m_Nodes.</summary>
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
                    m_EligiblePrefab = prefab;   // E1: the fee's kind (KindOf at the recompute)
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

        /// <summary>Spec §4 step 6 + §9.5 measure 2, as amended by R3 (run-binding-glade-spec.md
        /// §5.3). Immediate: node count / Complete / entity change, a verify mismatch
        /// (m_ForceFullResample, set by VerifyStep). A heights-not-ready retry after
        /// MinRecomputeFrames. Movement (any node moved >= 0.1 m in xz since the LAST RECOMPUTE)
        /// needs MinRecomputeFrames AND the duty-cycle window: not before m_NextMoveRecomputeTime
        /// (start of the previous recompute + clamp(ThrottleFactor · LastRecomputeMs, 0,
        /// MaxThrottleMs)). Because the moved test compares against the last recompute's snapshot,
        /// a deferred move still fires once the window has passed even if the cursor stopped.
        /// No time-based recompute (R2's RefreshFrames heartbeat is gone: VerifyStep).</summary>
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

        // ─────────────────────────────────────────────────────────────────────────────────────
        // Recompute (step 7)
        // ─────────────────────────────────────────────────────────────────────────────────────

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
            // R3 (§5.3): a verify mismatch is a one-shot TRIGGER, consumed here whatever this
            // recompute does (a failed Plan must not re-trigger every frame). The full resample it
            // asks for is guaranteed by the reuse guard instead: VerifyStep set m_GoodVerifiedAt
            // to NegativeInfinity, which only a full resample re-stamps.
            bool forceFull = m_ForceFullResample;
            m_ForceFullResample = false;

            // Duty-cycle anchor = the START of this recompute (measure 2). Until the timing below
            // is known, a following movement recompute is not throttled. R3: also the
            // verification time a full resample stamps on its heights.
            double startTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            m_NextMoveRecomputeTime = startTime;

            bool isNew = tempData.m_Original == Entity.Null;
            m_Readout.IsNewRun = isNew;
            // E1 (ch2 §6.4): the fee inputs, set here so they exist even when the heights are not
            // ready and the rating returns early. ShoelaceArea is the rater's public pure helper (plan
            // area); for a simple outline it equals Geometry.m_SurfaceArea (Σ triangle areas), which the
            // bind charges; at the closing click the cursor node sits on the first node (a zero-area edge).
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

            // Fresh every time (TerrainSystem.cs:2603; never cache across frames, :3538-3552).
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

            // Ping-pong (measure 3): plan into the grid that is NOT the last good one, so the
            // reuse source and the capture stay intact whatever this recompute does.
            SlopeGrid source = m_GoodGrid;
            SlopeGrid target = ReferenceEquals(source, m_GridA) ? m_GridB : m_GridA;
            m_Grid = target;

            // U2 (hub-u2-spec.md §3.5): StepMultiple → DiagonalRule → Plan → fill → Classify now
            // run in SkiRunRater.Rate, the same code the hub's built-run rating cache uses; the
            // bodies moved unchanged (SkiRunRater header; SlopeBatch proves bit parity). What stays
            // here: the heights-ready check above, the ping-pong grids, the step hysteresis (this
            // Temp's m goes in as stepCurrent) and the caller's half of the reuse guard. Reuse only
            // from a fully filled + rated grid (Classify validated every Need height finite) whose
            // heights were last verified less than HeartbeatSeconds ago (R3, wall clock; replaces
            // R2's frame-age guard) — so a verify mismatch (forceFull, and m_GoodVerifiedAt = -inf)
            // and re-entry after an idle gap resample in full; the rater adds the other half
            // (identical Step and Origin, so any step or origin change resamples in full too).
            // Terrain read: TerrainHeightSampler = the 2-arg SampleHeight (TerrainUtils.cs:82-137)
            // at FillHeights' world point, as before.
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
                // Polyline: one path, so the steepest patch IS the rating (spec §9.3) — forced
                // here as well, so the tooltip's "avoidable" line can never show for a line.
                m_Readout.SteepestGrade = polyline ? m_Result.RatingGrade : m_Result.SteepestGrade;
                m_Readout.Connected = m_Result.Connected;
                m_Readout.MeanGrade = m_Result.MeanGrade;
                m_Readout.Drop = m_Result.Drop;
                // R3 (§5.1): horizontal run length (ring: max(LengthRect, AxisLength); polyline:
                // arc length — SkiSlopeClassifier header "RUN LENGTH").
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

                // The target is now fully filled and rated: the next reuse source and the
                // capture. R3 (§5.3): a full resample stamps its heights current as of this
                // recompute's START; a reuse recompute INHERITS m_GoodVerifiedAt (the age of the
                // oldest height it copied). The grid changed, so any verify pass restarts.
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
            // Plan = heights read + area + StepMultiple + Plan (the old t0..t1 window): the part
            // before the rater plus the rater's own plan time.
            LastPlanMs = (float)((tRate - t0) * toMs) + detail.PlanMs;
            LastFillMs = fillMs;
            LastClassifyMs = classifyMs;
            LastStripMs = (float)((t4 - t3) * toMs);
            LastRecomputeMs = (float)((t4 - t0) * toMs);
            if (LastRecomputeMs > PeakRecomputeMs)
                PeakRecomputeMs = LastRecomputeMs;

            // Measure 2: the next movement-only recompute waits ThrottleFactor × this one.
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

        /// <summary>U2: the two status tests live in SkiRunRater (one definition for the feedback
        /// system and the hub's rating cache).</summary>
        private static bool IsPlanOk(SlopeStatus s)
        {
            return SkiRunRater.IsPlanOk(s);
        }

        private static bool IsRated(SlopeStatus s)
        {
            return SkiRunRater.IsRated(s);
        }

        /// <summary>Spec §4 step 7: band changes only when RatingGrade passes a threshold by
        /// >= DisplayHysteresis while the same Temp is live. up = BandOf(r - H) is the band the
        /// rating has cleared by H, down = BandOf(r + H) the highest band within H; the shown
        /// band is kept while it lies in [up, down].</summary>
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

        /// <summary>Crux marker (spec §9.5): ring mode with CruxValid only. World position of
        /// grid-local cell (CruxI, CruxJ) = Origin + (I0 + i, J0 + j)·Step (the lattice node the
        /// cell is centered on, spec §2); diameter max(14 m, 3·step).</summary>
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

        /// <summary>Records what the capture file needs about the last RATED polygon (its grid is
        /// m_GoodGrid): the exact nodes handed to Plan, texel, multiple and the rated numbers.
        /// O(nodes) per rated recompute, no allocation once m_CapNodes has grown.</summary>
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

        /// <summary>New run: a live gate within the tether of the Temp's center
        /// (ownership.TryNearestGate, a managed scan — recompute only). Center =
        /// Geometry.m_CenterPosition (valid after 2B, the same point the bind sweep uses,
        /// GeometrySystem.cs:509-576) for >= 3 nodes, else the node mean. Edited run (R3,
        /// run-binding-glade-spec.md §3.8; was "the original has Owner", spec §4 step 7):
        /// ownership.IsBound(Temp.m_Original) — a SkiRunBinding whose sign is live (the old
        /// Owner test never checked the sign). Resolved through m_Original because a Temp never
        /// carries SkiRunBinding (Game.Tools/GenerateAreasSystem.cs:256-273). Called on
        /// recomputes only (no time-based recompute since R3), so an edit Temp held still keeps
        /// the value of its last recompute. Fixer round (r3-plumbing-02): while ownership cannot
        /// bind a NEW run this load (ownership.BindsNewRuns false — binding disabled by an
        /// exception, or the load pass failed, so BindCharge is refused), a new run reads
        /// GateInRange = false whatever the distance, so the tooltip's existing "will stay
        /// unbound" warning shows instead of nothing; NewRunBindingOff says why (DUMP), because
        /// that warning's text names the distance, not the cause.</summary>
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

        // ─────────────────────────────────────────────────────────────────────────────────────
        // Heartbeat verify (R3, run-binding-glade-spec.md §5.3; header R3)
        // ─────────────────────────────────────────────────────────────────────────────────────

        private void ResetVerifyCursor()
        {
            m_VerifyRow = -1;
            m_VerifyRun = -1;
            m_VerifyCol = 0;
            m_VerifyPassStart = 0.0;
        }

        /// <summary>R3 §5.3. Called ONLY from UpdateCore's eligible-Temp path on a frame with no
        /// recompute. Idle cost: one clock read and a compare. Once m_GoodGrid's heights are
        /// Tuning.HeartbeatSeconds old (wall clock), a pass starts (its start time recorded) and
        /// walks m_GoodGrid's Need runs (SkiSlopeClassifier SlopeGrid.NeedRuns / NeedRowStart)
        /// from a cursor, re-sampling at most Tuning.VerifyNodesPerFrame Need nodes per frame on a
        /// FRESH GetHeightData() (TerrainSystem.cs:2603; 1.6.2 :2615) with the 2-argument
        /// TerrainUtils.SampleHeight at exactly FillHeights' world point (TerrainUtils.cs:82-137),
        /// and compares bit-exact (math.asint) with the stored height. A mismatch: force a full
        /// resample (m_ForceFullResample triggers the recompute, m_GoodVerifiedAt =
        /// NegativeInfinity forbids reusing any of these heights), mark the grid stale, stop. A
        /// complete pass: m_GoodVerifiedAt = the pass's START (every height is at least that
        /// current). A heightmap that is not readable this frame, or whose lattice origin no
        /// longer matches the grid's, never counts as verified: the former retries next frame,
        /// the latter forces a full resample. No allocation, no formatting.</summary>
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
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();   // fresh, never cached (header TERRAIN READ)
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

        // ─────────────────────────────────────────────────────────────────────────────────────
        // The game's validation verdict (F7 C2; header ROUND F7)
        // ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Called on a recompute frame with the eligible Temp (after CompleteDependency). Reads the
        /// verdict, and on a change of the (errors, warnings, infos, tags) set offers one line to the LogWindow.
        /// A session that starts clean says nothing. Allocates only on a change (the line). Never throws: a
        /// failure logs one `!!!` and switches only this watch off until the next load.</summary>
        private void WatchValidation(Entity temp, int nodeCount, bool complete, bool isNew)
        {
            if (m_ValOff)
                return;
            try
            {
                ReadValidation(temp, out uint errors, out uint warnings, out uint infos, out byte tags);
                ReadElsewhere(temp, out uint elseErrors, out int elseCount, out Entity elsePrefab);   // F7 fixer
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

        /// <summary>F7 fixer (LogWindow rule: TrailingDue on every pass that offered nothing). Called on hold frames
        /// (eligible Temp, no recompute): with nothing folded it is one int test; else the window test, and the
        /// summary once the window has passed, so a fold no longer waits for the cursor to move.</summary>
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

        /// <summary>F7 fixer (header ROUND F7, "elsewhere"): the Game.Tools.Error carriers other than the eligible
        /// Temp — the union of their error types (ReadValidation's errors), their count and the prefab of the
        /// lowest-index one (stable across chunk order). Recompute frames only; nothing carries Error: one
        /// IsEmptyIgnoreFilter, no allocation.</summary>
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

        /// <summary>The eligible Temp's state (ValidationState), then the other carriers': "elsewhere: error (blocks the
        /// click) InvalidShape on 1 other entity (SkiRunNaturalA)". Round S1 F3 (ski-days spec ch3 §5, refutation R11):
        /// when the other carriers have no error type of their own (elseErrors == 0), they carry the game's Error tag
        /// for the eligible Temp's error — ValidationSystem.cs:1657-1664 tags the permanent entity an error names, for
        /// ANY error at Override or above, and :1749-1752 gives the icon to the Temp only (ft8 V.1 #21) — so the line
        /// names that: "the run it overlaps" when the Temp's own errors include OverlapExisting, else "the run the game
        /// marks with this error" ("object" when the carrier is not one of our run prefabs).</summary>
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
              .Append(types.ToString())   // elseErrors != 0 here (Round S1 F3 took the untyped case above)
              .Append(" on ").Append(elseCount).Append(elseCount == 1 ? " other entity" : " other entities");
            if (!string.IsNullOrEmpty(name))
                sb.Append(" (").Append(name).Append(elseCount > 1 ? " and others)" : ")");
            return sb.ToString();
        }

        /// <summary>The eligible Temp's verdict (header ROUND F7): the Error / Warning tags, then each icon of its
        /// IconElement buffer → PrefabRef → ToolErrorData.m_Error, sorted by Icon.m_Priority (>= Error, >=
        /// Warning, below). Core EntityManager reads only; the icon reads may complete the icon jobs of this
        /// frame (a recompute frame only).</summary>
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
                // F7 fixer: the other carriers' error types seen in the fold (FullValidationState's "elsewhere").
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
                // a lost summary line must not take the frame down; the next change logs again
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        // Strips (spec §4 "Strips")
        // ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Builds the strips of (grid, r) into the managed stage m_StripStage (never
        /// into the NativeArray the draw job reads — PublishStrips copies them). Called with the
        /// CURRENT grid after the ping-pong (m_Grid == target) or with the pre-warm's scratch
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
                // Full row resolution first. Above Tuning.MaxStrips take the SMALLEST row stride
                // 2..MaxRowStride whose strips fit, so the heat-map stays complete at a coarser
                // row resolution (spec §4 "Strips"). Linear search, because the count at stride s
                // is not monotone in s (different representative rows); a failed pass stops at
                // the first strip past the cap and the buffers are simply rewritten. Only when
                // even MaxRowStride overflows is the list truncated (at that stride).
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

        /// <summary>Ring-mode strips at row stride s into the strip stage. Lattice rows are
        /// grouped by RowGroup; each group is drawn from its MIDDLE row's same-band run-length
        /// merge, centered on the group's z-center, width = group rows × Step (s·Step; the last
        /// group may be shorter). s = 1 is the full-resolution layout (one group per row, width =
        /// Step, center = the row). Returns false when the strips do not fit Tuning.MaxStrips —
        /// the stage then holds the first MaxStrips strips (the truncated list).</summary>
        private bool BuildRingStrips(SlopeGrid grid, SlopeResult r, int s)
        {
            m_StripCount = 0;
            // Row run-length merge of same-band inside cells. Cell (i, j) is centered on the
            // lattice node x = Origin.x + (I0 + i) * Step (spec §2), spanning ± Step / 2; a
            // strip runs from the first cell's west edge to the last cell's east edge,
            // y = mean of the two end heights (at the representative row).
            byte[] cellBand = r.CellBand;
            int nx = r.Nx, nz = r.Nz;
            double step = r.Step;
            float[] heights = grid.Heights;
            bool geomMatch = grid.Nx == nx && grid.Nz == nz &&
                             grid.I0 == r.I0 && grid.J0 == r.J0;
            bool heightsMatch = heights != null && geomMatch && heights.Length >= nx * nz;
            // Walk only each row's Mask RUNS (SlopeGrid.MaskRuns / MaskRowStart from Plan, R2
            // fixer round) when the grid matches the result: CellBand is BandOutside everywhere
            // outside the Mask (spec §3 invariant 14) and same-band merging never crosses an
            // outside cell, so the strips are identical to a full-row walk and the walk is
            // O(inside), not O(bbox) or O(row span) (a U- or C-shaped run's rows cross two arms).
            // Otherwise keep the full walk.
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
                // Group z-center and height; s = 1 gives exactly the row center and Step.
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

        /// <summary>Row group g at stride s over nz lattice rows: rows lo..hi, hi = lo + s - 1
        /// clamped to the last row (the last group may be shorter), representative = the middle
        /// row lo + (hi - lo) / 2 (lower middle for an even count). Group count = (nz + s - 1) / s;
        /// the groups cover rows 0..nz-1 exactly once. Ported verbatim by the scratch check.</summary>
        private static void RowGroup(int g, int s, int nz, out int lo, out int hi, out int rep)
        {
            lo = g * s;
            hi = lo + s - 1;
            if (hi > nz - 1)
                hi = nz - 1;
            rep = lo + ((hi - lo) >> 1);
        }

        /// <summary>Copies the staged strips into the NativeArray the draw job reads — the ONLY
        /// write to m_Strips, always after CompleteDraw() (header OVERLAY safety (a)). One
        /// memcpy (NativeArray.Copy(T[], NativeArray, int), checked in UnityEngine.CoreModule
        /// with Cecil).</summary>
        private void PublishStrips()
        {
            CompleteDraw();
            int n = m_StripCount;
            if (n > 0 && m_Strips.IsCreated)
                NativeArray<Strip>.Copy(m_StripStage, m_Strips, n);
        }

        /// <summary>Waits for the last scheduled draw job (normally already finished: the
        /// OverlayRenderSystem completes every registered writer at the start of its OnUpdate,
        /// OverlayRenderSystem.cs:740-741, the same frame).</summary>
        private void CompleteDraw()
        {
            m_DrawHandle.Complete();
            m_DrawHandle = default(JobHandle);
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        // Draw (step 8; every frame, the lists are cleared each frame) — Burst job since R2
        // ─────────────────────────────────────────────────────────────────────────────────────

        private void Draw()
        {
            int count = m_StripCount;
            bool crux = m_CruxDraw;
            if (!m_Readout.Valid || (count == 0 && !crux) || !m_Strips.IsCreated)
                return;
            if (count > m_Strips.Length)
                count = m_Strips.Length;
            // Timed with raw timestamps (no Stopwatch object, no allocation): GetBuffer + schedule
            // + AddBufferWriter only — the strip work runs on a worker thread.
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
            // Depends on every earlier overlay writer, ours included (header OVERLAY (b)).
            JobHandle handle = job.Schedule(overlayDeps);
            m_Overlay.AddBufferWriter(handle);   // :636-639; completed at OverlayRenderSystem :740
            m_DrawHandle = handle;
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            StripsDrawn = count;
            LastDrawMs = (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (!s_FirstDrawDone)
            {
                // The first Draw of the PROCESS pays the one-time Mono JIT of
                // Schedule<StripDrawJob> (PrewarmJit warms only the reflection data): reported on
                // its own, kept out of PeakDrawMs and the slow-draw WARN, so those measure the
                // steady-state schedule cost (spec §9.7 step 5).
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

        // ─────────────────────────────────────────────────────────────────────────────────────
        // Diagnostics (dev-tab button or dump hotkey only)
        // ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>"Dump to log": the state as text in the mod log, plus the capture file of the
        /// last rated polygon (spec §9.5 "Capture"). Button handler (dev tab) or dump-hotkey
        /// handler (SkiGladeHotkeySystem, R3 §5.2: UIUpdate, before this system's Rendering
        /// update, so it reads the PREVIOUS frame's readout with the run still on the cursor) —
        /// the only places this system does file IO. Each half has its own try/catch.</summary>
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
                // R3 (§5.4): the "since load" test is the good grid, not Recomputes (a
                // heights-not-ready recompute counts without a rating).
                if (m_GoodGrid == null)
                    sb.Append("  rating: none since load (published fields below are this load's, reset at load)\n");
                sb.Append("  temp ").Append(EntityText(EligibleTemp))
                  .Append(" nodes ").Append(EligibleNodes)
                  .Append(EligibleIsNew ? " new" : " edit")
                  .Append(EligibleComplete ? " complete" : " open")
                  .Append(", candidates ").Append(TempCandidates)
                  .Append(", live ").Append(m_ReadoutLive).Append('\n');
                // F7 C2 (header ROUND F7): the game's verdict on that Temp at the last recompute.
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

            // D9 (code review 2026-10-19): the log dump above stays for every player; the .skislope capture is the only file the
            // mod writes outside its log and settings, so it sits behind Options > Diagnostics (off by default).
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
                    Mod.Log.Info($"RunFeedback: capture written to {path} ({m_CapNodeCount} nodes, " +
                                 $"{needCount} need heights, rated {m_CapBand} " +
                                 $"{(m_CapRating * 100f).ToString("F1", CultureInfo.InvariantCulture)} %)");
                }
            }
            catch (Exception e)
            {
                LastCapturePath = "failed: " + e.GetType().Name;
                Mod.Log.Warn($"RunFeedback: capture failed: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Writes the last rated polygon (m_GoodGrid + the capture snapshot) as a
        /// `.skislope` file in exactly the spec §9.5 text format as amended by §9.8 (R2b;
        /// tests/SlopeBatch parses it): invariant culture, floats "G9" and doubles "G17" (both
        /// round-trip exactly; replaces "R" — the .NET Framework's "R" has a known double
        /// round-trip defect; the loader reads both), '\n' line ends, UTF-8 without BOM. Directory = Application.persistentDataPath + "/ModsData/SkiResortMod/captures"
        /// (created if missing); name run-yyyyMMdd-HHmmss.skislope, with "-2", "-3", … appended
        /// only when that name already exists (two dumps in one second). Need lines walk Plan's
        /// per-row Need runs in row-major order (j, then i). Returns the full path, or null when
        /// nothing has been rated since the load.</summary>
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

            // Pass 1: count (the header line precedes the list). Plan's exact Need runs, row-major
            // (j, then i ascending) — the same order and set as before the run lists.
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
            return path;
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Drops the readout and the cache key, so re-entry recomputes. Cheap (field
        /// writes only) — runs on every idle frame. Deliberately leaves m_GoodGrid and the
        /// capture snapshot alone: "Dump to log" must still see the last rated polygon after the
        /// tool is closed (spec §9.5).</summary>
        private void Invalidate(string note)
        {
            if (m_ValSession)
                EndValidationSession();   // F7 C2: once, when the run leaves the cursor (one bool test idle)
            m_ReadoutLive = false;
            m_Readout.Valid = false;
            m_Readout.Kind = -1;      // E1 (ch2 §6.4): no stale fee after the tool leaves a Temp
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

    /// <summary>U2 (hub-u2-spec.md §3.5): the game's IHeightSampler for SkiRunRater — the 2-argument
    /// TerrainUtils.SampleHeight (TerrainUtils.cs:82-137, plain bilinear, identical in 1.6.2), i.e.
    /// exactly the call the feedback system's FillHeights made before the extraction, at the world
    /// point the rater passes (FillHeights' expression). Holds a COPY of the TerrainHeightData the
    /// caller fetched this frame with GetHeightData() (TerrainSystem.cs:2603; 1.6.2 :2615): never
    /// keep an instance across frames (EnsureCPUHeights can dispose and reallocate the heights,
    /// TerrainSystem.cs:3538-3552; 1.6.2 :3564 — header TERRAIN READ). SampleHeight is a pure
    /// function of that data and the point, so copying the struct changes no value. Internal (not
    /// private): SkiRunCatalogSystem's rating cache reads terrain through the same struct.</summary>
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
