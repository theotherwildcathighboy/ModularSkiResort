// SkiRunCatalogSystem — the Ski Resort hub's in-memory list of our ski runs plus the resort totals
// (docs/research/resort-hub-understand.md §5.2 item 4 and §5.3 `summary` / `runs`, which win over
// §2.B.3 and §3 U1; §2.B.4 "drop"/"length", §2.B.5 run names, §2.B.7 data model). Created
// 2026-10-01 (Round U1, implementer U1-B); Round U2 (docs/research/hub-u2-spec.md §2.1, §3.1,
// §3.5; 2026-10-03, implementer I-1) added the resort link (Gate), per-resort numbering and the
// built-run RATING CACHE below. Round E1 (docs/research/econ-round/spec/ch2-runs.md §2, decision D12;
// stream S2, 2026-10-04) made it ALWAYS ON and gave the simulation a read API (below). Round M2 (D9,
// 2026-10-10, builder B3): with the hub open it rebuilds only for our runs (coalesced), and the DUMP counts the
// rebuilds by reason (header CONTRACT). Read by SkiResortHubUISystem, SkiResortStatsSystem and (E1)
// SkiResortAttractivenessSystem; this system owns no binding and writes nothing to the world.
//
// ALWAYS ON (Round E1, D12: the simulation scores runs by these ratings, so they exist with the hub
// closed). One service, two cadences: the same Rebuild, RateStep, RateOne, queue rules
// (SkiRunRatingRules), slow-rating WARN and log windows run in both; only the triggers, the
// background throttle and the backstop interval change with the panel. Demand is set by the hub every
// frame (U2: "any of resorts, runs, lifts, lodges, city, build observed", hub-u2-spec.md §3.6;
// Colossal.UI.Binding EventBindingBase.observerCount/active, EventBindingBase.cs:14-16 [scratch
// decompile of the installed 1.6.2f1 DLL]):
//     |                     | panel open (Demand true) — Round M2 D9   | panel closed (Demand false) — E1      |
//     | rebuild triggers    | the CHANGE SIGNATURE below, 2 s heartbeat | one of OUR BOUND runs (SkiRunBinding)  |
//     |                     |                                          | Created/Updated/Deleted this frame     |
//     |                     |                                          | (one IsEmptyIgnoreFilter; E1 fixer:    |
//     |                     |                                          | vanilla building surfaces excluded) or |
//     |                     |                                          | BindGeneration moved,                  |
//     |                     |                                          | COALESCED to one rebuild per           |
//     |                     |                                          | ClosedMinRebuildIntervalS (0.5 s);     |
//     |                     |                                          | a ClosedHeartbeatS (10 s) heartbeat    |
//     | sign count          | CountSigns (walks every building)        | skipped: Signs keeps its last value    |
//     |                     |                                          | (it feeds only the hub; "hub opened"   |
//     |                     |                                          | recounts)                              |
//     | front ratings       | one per UIUpdate frame, at once          | the same                               |
//     | background ratings  | duty factor DutyFactorOpen 8             | DutyFactorClosed 32, and at least      |
//     |                     |                                          | ClosedMinGapS 0.25 s apart             |
//     | backstop            | BackstopS 60 s (F7; was 10 s)            | ClosedBackstopS 600 s (a safety net)   |
//   Per-frame cost with the panel closed: the frame count, the epoch compare, one IsEmptyIgnoreFilter,
//   one int compare and a few float compares; nothing is allocated. An explicit RequestRebuild (dev
//   tab, load) is served in both states. While PAUSED the service keeps working (UIUpdate runs paused,
//   SystemOrder.cs:67; throttles use realtimeSinceStartup), so a run drawn while paused binds
//   (SkiRunBindTriggerSystem.SweepNow at ModificationEnd) and is rated the next frame; the simulation
//   reads the finished ratings when it resumes. AFTER A LOAD: OnGameLoadingComplete clears everything,
//   sets ServiceState = WarmingUp, the arm time and a "load" rebuild request; the warm-up starts after
//   WarmupFrames and the heights-ready check, hub or no hub; ServiceState turns Ready when the seeded
//   warm-up set is done (LogWarmupDone) or SkiRunRatingRules.WarmupDeadlineS (90 s) after the load
//   (one WARN), so the simulation never waits forever. The one-shot "!!!" sets ServiceState = Failed:
//   the attraction model stops holding its first provider write (ch1 §9) and unrated runs count 0.
//   Both panel-closed costs are measured and logged once per load ("Catalog [service]", after
//   ServiceCostLogAfterRebuilds = 32 closed rebuilds).
//
// COST LINE AND BACKSTOP F7 (Round F7, item E3; docs/research/field-test-7-fixes.md, log-audit.md D5 / D11):
//   * "Catalog [service]" counts only panel-closed rebuilds that had at least one run row to walk
//     (m_CostRebuilds*; the line fired 34 minutes before the first run in field test 7 and measured an
//     empty list). Rebuilds of an empty list are still counted in m_ClosedRebuilds for the DUMP, and the
//     line says how many it left out. Ratings exist only with runs, so their counters are unchanged.
//   * BackstopS 10 → 60 s with the hub open: one main-thread rating of 2-9.5 ms every 10 s while the hub
//     was open (39 by the field test's DUMP). The "skip unchanged runs" alternative is not possible
//     cheaply: the backstop exists for the one change that moves no key — terrain under the outline changed
//     by roads and buildings (no readable terrain version, header INVALIDATION) — while node moves and
//     re-snaps already re-rate through Key / YKey. So the backstop keeps re-rating the oldest-rated run,
//     every 60 s instead of 10 s.
//
// READ API (Round E1, ch2 §2.4; e1-spec I-11/I-12, R12 — RatingServiceState and RunFacts are NESTED
// here; main thread only): ServiceState; RatingRevision (++ whenever a RunFacts field of any run
// changed — an FNV-1a over Entity, Gate, Kind, RateState|Band, AreaM2, LengthM, DropM, TopM, BottomM
// and NodeTopPos per row plus the row count; names, ordinals, grades and the sign count are NOT inputs,
// so a rename does not move it — a run arrived or left, the service state changed, or a load
// happened; compare for equality); TryGetRunFacts (the row's simulation facts; TopNode = the highest
// outline node, the lift-served test point of LiftServedRule); TryGetRating (chapter 1 §18's shape:
// true only for a CURRENT rating — the run's outline key matched at the last rebuild, RateState 1-4).
//
// CONTRACT
//   * While Demand is true, a rebuild runs when the CHANGE SIGNATURE moves (Round M2 D9, docs/research/round-m2/
//     understand/measure-core.md 2.8 and build-contract.md "Decided here" 11: the panel-open path listens only to OUR
//     runs — field test 10 run 2b rebuilt about once per wall second with the hub open, at least half of it from the
//     building, surface-area and area-edit triggers that fire for vanilla buildings and areas; those three are gone):
//       - one of OUR BOUND runs Created / Updated / Deleted this frame (m_RunChangeQuery, the panel-closed
//         query: an edited run is tagged Updated on its ORIGINAL entity by ApplyAreasSystem,
//         ApplyAreasSystem.cs:167-202; the tag lives until CleanUpSystem at Cleanup, CleanUpSystem.cs:53, and
//         UIUpdate runs before it: SystemOrder.cs:67 UIUpdateSystem inside MainLoop, Cleanup after it — same
//         lines in 1.6.2) — COALESCED like the closed path to one rebuild per ClosedMinRebuildIntervalS
//         (a drawing stroke must not rebuild every frame);
//       - SkiRunOwnershipSystem.BindGeneration (bind / re-link / orphan / migration write our own
//         SkiRunBinding and raise no tag, so a counter stands in for the tag; a newly drawn run is caught here);
//       - the active localization dictionary changed (default names are localized);
//       - or a 2 s wall-clock heartbeat (UnityEngine.Time.realtimeSinceStartup — NOT UIUpdateState,
//         which counts simulation frames and freezes while paused, research §4.1-3), which catches
//         what the signature cannot see (a sign moved, a run re-snapped by a terrain edit) and, since Round
//         M2, a Welcome Sign placed or bulldozed (shown within 2 s instead of 0.5 s; the heartbeat recounts the
//         signs) and a newly drawn run that is not bound to a sign (no SkiRunBinding).
//     The rising edge of Demand (the panel opened) rebuilds at once. Each rebuild is counted by reason
//     (m_RebuildsByReason, literal reasons; the DUMP prints them).
//   * A rebuild whose result equals the previous one (a content hash over every row field and the
//     totals) does NOT bump Revision, so the hub re-sends nothing; it is counted, not logged. A
//     signature-driven rebuild that DID change something is logged at most RebuildLinesPerWindow times
//     per RebuildLineWindowS (fixer round, finding u2-data-01: a terrain brush stroke re-snaps the nodes
//     of an unrated run every pass, and its node estimates move with them); the rest are counted and
//     the next logged line names how many were not — the heartbeat writes it once the window passed,
//     so the final state is never silent.
//   * Per run, U1 cheap numbers (kept as the fallback while a run is not rated — RunRow.Approx):
//       areaM2  = Geometry.m_SurfaceArea (PLAN area: Σ triangle.xz areas, Game.Areas/GeometrySystem.cs:517);
//       lengthM = (P + sqrt(max(0, P² − 16A))) / 4 with P = the horizontal perimeter of the node ring
//                 (closing edge included) and A = |shoelace area| of the ring: the classifier's
//                 LengthRect formula (SkiSlopeClassifier.EquivalentRectLength, R3), here on the
//                 area's Node buffer instead of the sanitized grid ring. Exact for a constant-width
//                 band; a bowl over-reads, a diamond under-reads (SkiSlopeClassifier header L1/L2);
//       dropM   = max − min Node.m_Position.y: NODE heights only, so a peak or hollow inside the
//                 outline is missed (research §2.B.4) — the rating below replaces it.
//     bound = SkiRunOwnershipSystem.TryGetBoundGate (THE bound test: SkiRunBinding to a live sign);
//     U2: Gate = that sign (Entity.Null = not linked) — the run's resort (§3.1: sticky from bind
//     time, re-links free only when its sign died).
//   * Names (OD7, §5.1): a custom name from vanilla NameSystem.TryGetCustomName
//     (Game.UI/NameSystem.cs:209-212, same in 1.6.2) wins; otherwise the default
//     "Groomed run N" / "Snowmaking run N" / "Night run N" from the ACTIVE dictionary's
//     SkiResort.Hub.RUN_DEFAULT[<kind>] with "{N}" replaced (vanilla precedent for reading the
//     active dictionary: NameSystem.cs:250, same in 1.6.2), English if the key is missing.
//     U2 (OD-3): N counts per (Gate, kind) — "Groomed run 1" in each resort; Not linked runs are
//     numbered among themselves — ordered by Geometry.m_Bounds.max.y descending, then centre x,
//     then centre z, then entity index/version (research §2.B.5: values that exist immediately;
//     numbers shift when a run above is added or removed — accepted, OD7). Formatted names are
//     cached per (kind, N) and the cache is dropped when the dictionary changes
//     (LocalizationManager.onActiveDictionaryChanged, subscribed in OnCreate and removed in
//     OnDestroy — vanilla precedent AreaBufferSystem.cs:661).
//   * Row order sent to the UI: runs linked to a sign first, unlinked runs last (contract §5.2 item 3
//     "unbound runs grouped last"); inside each group highest top first.
//   * Summary (U1, kept for the U1 hub code; the `summary` binding is removed in contract 2):
//     signs = live Ski Area Welcome Signs (a chunk walk of every building checked against the
//     ownership system's sign prefab set), runsBound / runsUnbound, areaM2 = the sum over ALL our
//     runs. LiftsUnknown (-1) is RETIRED by U2 (hub-u2-spec.md §8.4: lifts come from the lift census
//     through SkiResortStatsSystem); the constant stays only so un-updated callers compile.
//
// U2 RATING CACHE (hub-u2-spec.md §3.5) — built runs get the drawing tooltip's rating:
//   * WHAT: SkiRunRater.Rate (the Plan → Fill → Classify path moved out of SkiRunDrawFeedbackSystem,
//     bit-identical there — tests/SlopeBatch) on the run's Node ring, terrain read through
//     TerrainHeightSampler (the feedback system's 2-arg TerrainUtils.SampleHeight), into this
//     system's own SlopeGrid / SlopeResult, stepCurrent 0 (the deterministic step: the smallest m
//     within CellBudget, SkiSlopeClassifier.cs:729-741) and NO reuse source — so a given polygon on
//     a given terrain always rates the same. Lattice = the feedback system's: texel 1/hd.scale.x,
//     origin −hd.offset.xz (TerrainSystem.cs:2618-2622, TerrainUtils.cs:35-38). The heights-ready
//     check is the feedback system's (GetHeightData() fresh, TerrainSystem.cs:2603, 1.6.2 :2615;
//     created and >= SkiRunDrawFeedbackSystem.Tuning.MinHeightmapResolution wide).
//   * FIELD PARITY: a run drawn and bound shows the same band in the tooltip and in the
//     "Catalog [rate]" line, except (a) when the feedback system's per-Temp step hysteresis held a
//     larger m than the deterministic one (the polygon grew past a budget step and shrank back), and
//     (b) within DisplayHysteresis (0.5 %) of a band threshold, where the tooltip shows its
//     hysteresis band and the cache shows BandOf(RatingGrade). Both are by design (SlopeBatch prints
//     the deterministic m of every capture: all nine equal the captured m).
//   * CACHE: Dictionary<Entity, RunRating> — State (1 rated / 2 no terrain / 3 too large / 4 failed),
//     Band, RatingGrade, SteepestGrade, MeanGrade, Connected, DropM (SlopeResult.Drop), TopM / BottomM
//     (SlopeResult.MaxHeight / MinHeight), LengthM (SlopeResult.Length, horizontal), StepMultiple,
//     RateMs, Key, YKey, TerrainEpoch. AMENDED in the fixer round (finding u2-data-01; a deviation from
//     spec §3.5 "Key (FNV-1a over node xyz bits and count)", reported): Key = FNV-1a over every node's
//     x and z bits and the node count — the rating's real inputs (SkiRunRatingRules header: the
//     classifier reads node y only for a finiteness check, SkiSlopeClassifier.cs:803-805) — and YKey
//     = the same over the y bits. A GroundHeightSystem re-snap (y only: AreaUtils.cs:672-677, applied
//     by GroundHeightSystem.cs:777-791) moves YKey and never Key, so the row KEEPS its rating, values
//     and hash and the run is re-rated in the background as "terrain".
//   * QUEUE (each rebuild computes Key and YKey per row, O(nodes); SkiRunRatingRules.NeedsRating /
//     Replaces): a row with no entry is queued — at the BACK as "warm-up" while this load's warm-up is
//     being seeded, else at the FRONT as "bind" (a run that appeared after the warm-up: drawn, or
//     linked); a Key mismatch at the FRONT as "edit"; a YKey mismatch or an entry from an older
//     terrain epoch at the BACK as "terrain". A row already queued as WARM-UP is never moved to the
//     front by "bind" (fixer round, finding behaviour-2: every rebuild after the seed re-asks every
//     unrated row as bind, and the promotion used to drain the whole warm-up at the front with no duty
//     cycle); an edit still promotes it. At most ONE rating per UIUpdate frame; BACKGROUND ratings
//     (warm-up, terrain, backstop) also obey a duty cycle: they start only when now >= last start +
//     factor × last duration (wall clock; SkiRunRatingRules.NextBackgroundStart: factor 8 with the
//     hub open, 32 and at least 0.25 s with it closed). Round E1: ratings run hub open OR closed
//     (header ALWAYS ON).
//   * ROW BAND (fixer round, findings u2-data-05 / u2-panel-5): -1 while pending, -2 when the rating
//     ended in no terrain / too large / failed (SkiRunRatingRules.RowBand) — a final state the hub
//     shows as "cannot be rated" instead of "being rated" forever. Spec §2.3 knew only -1 (reported).
//   * WARM-UP: no rating before WarmupFrames UIUpdate frames after the load AND the heights-ready
//     check (the frame count is unverified as sufficient, spec §3.5); the first rebuild after that
//     seeds every row as warm-up. One "Catalog [rate-warmup]" line when the seeded set is done.
//   * INVALIDATION: Key change (front); YKey change (back, terrain); terrain epoch++ when the active tool LEAVES TerrainToolSystem
//     (ToolSystem.EventToolChanged, a public Action field, ToolSystem.cs:30, fired from the activeTool
//     setter :84-98; the same in 1.6.2), applied TerrainEpochDelayFrames later so the heightmap
//     readback has landed (tunable, unmeasured); a backstop re-rates the oldest-rated run every
//     BackstopS with the hub open, every ClosedBackstopS with it closed (E1) — the only path that
//     catches terraforming by roads and buildings (no
//     terrain version is readable: GroundHeightSystem's update lists are private,
//     GroundHeightSystem.cs:1015-1021, header TERRAIN READ of the feedback system). An EDITED run
//     (its outline's x/z moved) reads "pending" (RateState 0, U1 numbers) until re-rated (one frame
//     later in practice); a run waiting for a terrain or backstop re-rate (its nodes were only
//     re-snapped in height) keeps its old rating meanwhile. Rows that left are dropped from the cache;
//     OnGameLoadingComplete clears everything.
//   * MEMORY: after a rating whose lattice exceeded ShrinkCells (2^19) cells, the grid / result pair
//     is replaced by fresh small instances (the classifier's 2^21 bbox cap, SkiSlopeClassifier.cs:651,
//     lets the pair grow to tens of MB — an estimate, not measured).
//   * LOGS: `Catalog [rate] run 'Groomed run 2' 95601:11: Blue 23.1 % (steepest 31.0 %), drop 162 m,
//     length 540 m, m=2, 4.8 ms (bind|edit)` for every bind and edit rating (at most RateLinesPerWindow
//     per RateLineWindowS window — a terraform that re-snaps many runs must not flood; the next logged
//     line says how many were not logged); warm-up, terrain and backstop ratings are counted, not
//     logged; one `Catalog [rate-warmup] N run(s) in F frames, total T ms, max M ms … — rating
//     service ready` per load — when the seeded warm-up set has really been rated (behaviour-2) —
//     or, E1, one `Catalog [rate-warmup] WARN: warm-up not finished 90 s after the load …` first.
//     E1 also writes the panel-closed `Catalog [rebuild] (run changed (panel closed))` lines (the
//     same window as every signature rebuild) and `Catalog [rate] … (bind)` lines with the hub closed.
//   * SLOW RATING (Round F6, item D6; log-audit §6 / defect 6: built-run ratings took 4-11 ms on the
//     main thread — 8.1 ms at a bind, max 10.9 ms — with no budget and no WARN, while the drawing
//     tooltip WARNs on the same Plan → Fill → Classify work above 4 ms): ONE `Catalog [rate] slow
//     rating …` WARN per load when a rating takes longer than SkiRunDrawFeedbackSystem.Tuning
//     .SlowRecomputeWarnMs, with the run, the reason, the node count, the area, the step, the lattice
//     size, the plan time and the heights sampled. Log only: the ratings, their order and the duty
//     cycle are unchanged.
//
// PHASE (Mod.cs): UpdateAt UIUpdate, registered BEFORE SkiResortStatsSystem and
// SkiResortHubUISystem (hub-u2-spec.md §3.6). UIUpdate is driven by UIUpdateSystem in MainLoop every
// frame, paused included (UIUpdateSystem.cs:17-20, SystemOrder.cs:67), after ToolSystem and
// ModificationSystem (SystemOrder.cs:58, 60), so this frame's Created/Updated/Deleted tags are
// visible; a plain UpdateAt runs in add order (UpdateSystem.cs:158-161) and update intervals are
// ignored in this phase (UpdateSystem.cs:183-221). All identical in 1.6.2. So each frame: catalog
// (reads the Demand the hub set last frame) → stats → hub (pushes this frame's revisions).
//
// HOUSE CONTRACT: "created" line; one "Catalog [arm]" line per load listing every constant; a
// one-shot "!!!" catch that stops the catalog until the next load (the hub then shows the last rows;
// E1: the rating service reports Failed); no per-frame managed allocation or string formatting (rows,
// names and log lines are built only in a rebuild or a rating, and a rebuild is at most every 2 s
// open / 10 s closed unless the world changed — closed, change-driven ones at most every 0.5 s; one
// rating per frame at most). Game mode only. Every rebuild that changed something (windowed), and
// every requested one, writes ONE line.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunCatalogSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>Wall-clock heartbeat (s) while the hub listens (contract §5.2 item 4).</summary>
            public const float HeartbeatS = 2f;

            /// <summary>Rows written by the hub's DUMP (the census line carries the totals).</summary>
            public const int DumpRowsMax = 64;

            // ── U2 rating cache (hub-u2-spec.md §3.5) ──
            /// <summary>UIUpdate frames after a load before the first rating (plus the
            /// heights-ready check). Unverified as sufficient (spec §3.5, §7.2 item 13).</summary>
            public const int WarmupFrames = 120;

            /// <summary>Background duty factor while the hub is open (= SkiRunRatingRules.DutyFactorOpen).</summary>
            public const float BackgroundDutyFactor = SkiRunRatingRules.DutyFactorOpen;   // 8

            /// <summary>Frames between leaving the terrain tool and the terrain epoch bump
            /// (tunable, unmeasured).</summary>
            public const int TerrainEpochDelayFrames = 30;

            /// <summary>Wall-clock seconds between backstop re-ratings of the oldest-rated run
            /// while the hub is open. F7 E3 (header COST LINE AND BACKSTOP F7): 10 → 60 s.</summary>
            public const float BackstopS = 60f;

            /// <summary>Lattice cells above which the rating grid / result pair is replaced by
            /// fresh small instances after the rating (tunable).</summary>
            public const int ShrinkCells = 1 << 19;

            /// <summary>Bind / edit "[rate]" lines per window before the rest are only counted.</summary>
            public const int RateLinesPerWindow = 8;
            public const float RateLineWindowS = 10f;

            // ── fixer round ── (BuildingCountMinIntervalS retired in Round M2 D9: the building count is no
            // longer a panel-open trigger; our runs' changes coalesce to ClosedMinRebuildIntervalS instead)
            /// <summary>Changed signature-driven "[rebuild]" lines per window before the rest are only
            /// counted (finding u2-data-01: a terrain stroke must not write a line per frame).</summary>
            public const int RebuildLinesPerWindow = 6;
            public const float RebuildLineWindowS = 10f;

            // ── Round E1 (ch2 §2): the panel-closed cadence of the rating service ──
            /// <summary>A change-driven rebuild on one of our runs runs at most once per this many wall-clock s:
            /// panel closed (E1) and, since Round M2 D9, panel open too.</summary>
            public const float ClosedMinRebuildIntervalS = 0.5f;
            /// <summary>Panel-closed wall-clock heartbeat (s).</summary>
            public const float ClosedHeartbeatS = 10f;
            /// <summary>Panel-closed backstop: re-rate the oldest-rated run every this many wall-clock s
            /// (second pass: 600, a safety net only — events, binds and the terrain epoch catch the real changes).</summary>
            public const float ClosedBackstopS = 600f;
            /// <summary>The one-time "Catalog [service]" cost line is written after this many panel-closed rebuilds
            /// that had at least one run to walk (F7 E3: a rebuild of an empty list measures nothing).</summary>
            public const int ServiceCostLogAfterRebuilds = 32;
        }

        /// <summary>summary.lifts while the lift count was not cheaply known (contract §5.3).
        /// RETIRED by U2 (hub-u2-spec.md §8.4): kept only so an un-updated caller compiles.</summary>
        public const int LiftsUnknown = -1;

        /// <summary>RunRow.RateState values (hub-u2-spec.md §2.1; = SkiRunRatingRules).</summary>
        public const byte RatePending = SkiRunRatingRules.RatePending, RateRated = SkiRunRatingRules.RateRated,
            RateNoTerrain = SkiRunRatingRules.RateNoTerrain, RateTooLarge = SkiRunRatingRules.RateTooLarge,
            RateFailed = SkiRunRatingRules.RateFailed;

        /// <summary>Why-text of a heartbeat rebuild (an unchanged heartbeat rebuild is not logged).</summary>
        private const string WhyHeartbeat = "heartbeat";

        /// <summary>E1: why-text of a coalesced panel-closed rebuild (a signature reason: unchanged results
        /// are counted, changed ones obey the RebuildLinesPerWindow window).</summary>
        private const string WhyRunChangedClosed = "run changed (panel closed)";

        /// <summary>Round M2 D9: why-text of a coalesced panel-open rebuild on one of our bound runs (a signature
        /// reason, like the closed one).</summary>
        private const string WhyRunChanged = "run changed";

        /// <summary>One run of ours, as the hub sends it (mod/SkiResortUI/src/bindings.ts RunRow) plus
        /// the ordering keys. U1 fields keep their meaning, except that LengthM / DropM are the RATED
        /// values when Approx is false (hub-u2-spec.md §2.3).</summary>
        public struct RunRow
        {
            public Entity Entity;
            public string Name;      // custom name, or the default "Groomed run N"
            public int Kind;         // 0 groomed / 1 snowmaking / 2 night / 3 natural (= RunKind)
            public bool Bound;       // SkiRunOwnershipSystem.IsBound (= Gate != Entity.Null)
            public bool CustomName;  // Name came from NameSystem
            public int Ordinal;      // N of the default name — U2: per (Gate, Kind), top height descending (OD-3)
            public float LengthM;    // rated: SlopeResult.Length (horizontal); else LengthRect over the node ring
            public float DropM;      // rated: SlopeResult.Drop (lattice heights); else max − min node height
            public float AreaM2;     // Geometry.m_SurfaceArea (plan)
            public float TopY;       // Geometry.m_Bounds.max.y (ordering)
            public float CenterX;    // Geometry.m_CenterPosition (ordering, tie-breaks)
            public float CenterZ;

            // ── U2 (hub-u2-spec.md §2.1) ──
            public Entity Gate;          // TryGetBoundGate; Entity.Null = not linked
            public byte RateState;       // 0 pending / 1 rated / 2 no terrain / 3 too large / 4 failed
            public sbyte Band;           // -1 pending, -2 cannot be rated (states 2-4; fixer round), else SlopeBand 0..4 (SkiSlopeClassifier.cs:441-448)
            public float GradePct, SteepestPct;     // -1 not rated (RatingGrade / SteepestGrade × 100)
            public float TopM, BottomM;             // rated: SlopeResult.MaxHeight / MinHeight; else node max / min
            public bool Approx;                     // true = LengthM / DropM / TopM / BottomM are U1 node estimates

            // Internal: the U1 node estimates (the fallback) and the cache keys of this rebuild
            // (SkiRunRatingRules: Key = node x/z + count, YKey = node y + count).
            internal float NodeLengthM, NodeDropM, NodeTopM, NodeBottomM;
            internal ulong Key, YKey;
            internal float3 NodeTopPos;   // E1: the highest outline node (first one on a tie), x/y/z — the lift-served test point
        }

        /// <summary>One cached rating (hub-u2-spec.md §3.5 "Cache").</summary>
        private struct RunRating
        {
            public byte State;
            public sbyte Band;
            public float RatingGrade, SteepestGrade, MeanGrade;
            public bool Connected;
            public float DropM, TopM, BottomM, LengthM;
            public int StepMultiple;
            public float RateMs;
            public ulong Key, YKey;
            public int TerrainEpoch;
            public float RatedAt;    // realtimeSinceStartup (backstop: oldest first)
        }

        // Queue reasons: SkiRunRatingRules (pure, tested in tests/ResortBatch).
        private const byte ReasonWarmup = SkiRunRatingRules.ReasonWarmup, ReasonBind = SkiRunRatingRules.ReasonBind,
            ReasonEdit = SkiRunRatingRules.ReasonEdit, ReasonTerrain = SkiRunRatingRules.ReasonTerrain,
            ReasonBackstop = SkiRunRatingRules.ReasonBackstop;
        private static readonly string[] ReasonNames = { "?", "warm-up", "bind", "edit", "terrain", "backstop" };

        private struct QueueEntry
        {
            public Entity Run;
            public byte Reason;
        }

        // Kind tables, indexed by RunKind (Round E1, econ-round spec ch2 §2.3 / §4.4.4: natural = 3
        // appended). m_Templates takes its length from DefaultNameKeys so the tables cannot drift apart
        // (a longer key table with a 3-slot m_Templates would throw in RefreshTemplates, UIUpdate).
        private static readonly string[] KindNames = { "Groomed", "Snowmaking", "Night", "Natural" };
        private static readonly string[] BandNames = { "Flat", "Green", "Blue", "Black", "Double black" };
        private static readonly string[] DefaultNameKeys =
        {
            "SkiResort.Hub.RUN_DEFAULT[Groomed]", "SkiResort.Hub.RUN_DEFAULT[Snowmaking]",
            "SkiResort.Hub.RUN_DEFAULT[Night]", "SkiResort.Hub.RUN_DEFAULT[Natural]",
        };
        private static readonly string[] DefaultNameEnglish =
            { "Groomed run {N}", "Snowmaking run {N}", "Night run {N}", "Natural run {N}" };
        private const string OrdinalPlaceholder = "{N}";

        private SkiRunOwnershipSystem m_Ownership;
        private NameSystem m_NameSystem;
        private TerrainSystem m_TerrainSystem;
        private ToolSystem m_ToolSystem;

        private EntityQuery m_RunQuery;        // every live surface area (our runs are among them)
        private EntityQuery m_BuildingQuery;   // every live building (the signs are among them; CountSigns)
        // (m_AreaEditQuery, "areas tagged Updated this frame", retired in Round M2 D9: no reader once the panel-open
        // path listens to m_RunChangeQuery)
        private EntityQuery m_RunChangeQuery;  // E1: our bound runs Created/Updated/Deleted this frame (both panel states since Round M2 D9)

        // Rows: m_Rows is what the hub reads; a rebuild fills m_Scratch and swaps (no allocation once
        // both lists reached the city's run count). m_RowIndex: entity → index in m_Rows.
        private List<RunRow> m_Rows = new List<RunRow>(64);
        private List<RunRow> m_Scratch = new List<RunRow>(64);
        private readonly Dictionary<Entity, int> m_RowIndex = new Dictionary<Entity, int>();
        private readonly Stopwatch m_Timer = new Stopwatch();

        // Default-name templates (one per kind) and the formatted names, keyed (kind << 16) | N.
        private readonly string[] m_Templates = new string[DefaultNameKeys.Length];
        private readonly Dictionary<int, string> m_NameCache = new Dictionary<int, string>();
        private int m_NamesGeneration;            // bumped by the dictionary-changed event
        private int m_TemplatesGeneration = -1;   // the generation m_Templates were read at
        private Action m_DictionaryChangedHandler;
        private Action<ToolBaseSystem> m_ToolChangedHandler;

        // U2 rating cache state.
        private readonly Dictionary<Entity, RunRating> m_Ratings = new Dictionary<Entity, RunRating>();
        private readonly Queue<QueueEntry> m_Front = new Queue<QueueEntry>();
        private readonly Queue<QueueEntry> m_Back = new Queue<QueueEntry>();
        private readonly Dictionary<Entity, byte> m_Queued = new Dictionary<Entity, byte>();   // entity → its current reason
        private readonly List<Entity> m_ScratchEntities = new List<Entity>();
        private SlopeGrid m_RateGrid = new SlopeGrid();
        private SlopeResult m_RateResult = new SlopeResult();
        private float3[] m_RateNodes = new float3[64];
        private int m_Frame;                       // UIUpdate frames since this load (every OnUpdate)
        private int m_TerrainEpoch;
        private int m_TerrainEpochDueFrame = -1;   // >= 0: bump the epoch at this frame
        private ToolBaseSystem m_LastTool;
        private bool m_WarmupSeeded;
        private bool m_WarmupLogged;
        private bool m_HeightsLogged;
        private int m_WarmupQueued, m_WarmupDone, m_WarmupStartFrame;
        private float m_WarmupTotalMs, m_WarmupMaxMs;
        private float m_NextBackgroundStart;
        private float m_NextBackstop;
        private float m_RateLineWindowStart;
        private int m_RateLinesInWindow, m_RateLinesSuppressed;
        private bool m_SlowRateWarned;             // F6 D6: one slow-rating WARN per load (header SLOW RATING)

        // Per-load state.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_WasDemanded;
        private string m_RebuildRequest;          // non-null: rebuild on the next OnUpdate, with this why
        private int m_SigBindGeneration = int.MinValue, m_SigNames = -1;   // Round M2 D9: surface / building counts retired
        private float m_NextHeartbeat;
        private ulong m_LastHash;
        private bool m_OpenChangePending;         // Round M2 D9: a panel-open run change seen, rebuild owed (coalesced)
        // Round M2 D9: rebuilds this load by reason (literal reasons only: no allocation per rebuild after first sight).
        private readonly Dictionary<string, int> m_RebuildsByReason = new Dictionary<string, int>();
        // Fixer round: the last rebuild's time (change coalescing) and the changed-rebuild line window (u2-data-01).
        private float m_LastRebuildTime = float.NegativeInfinity;
        private float m_RebuildLineWindowStart = float.NegativeInfinity;
        private int m_RebuildLinesInWindow, m_RebuildLinesSuppressed;
        // Round E1 (ch2 §2.3): the always-on service.
        private bool m_ClosedChangePending;       // E1: a closed-panel change seen, rebuild owed (coalesced)
        private float m_ArmedAt;                  // E1: realtimeSinceStartup at OnGameLoadingComplete (warm-up deadline)
        private bool m_WarmupDeadlineHit;         // E1: the service turned Ready by its deadline (WARN written)
        private ulong m_LastFactsHash;            // E1: hash of every RunFacts field (RatingRevision)
        private int m_ClosedRebuilds, m_ClosedRatings;            // E1: panel-closed counts this load
        private float m_ClosedRebuildMsSum, m_ClosedRebuildMsMax; // E1
        private float m_ClosedRatingMsSum, m_ClosedRatingMsMax;   // E1
        private float m_ClosedSince = -1f;        // E1: realtimeSinceStartup of the first closed rebuild this load
        private bool m_ServiceCostLogged;         // E1: "Catalog [service]" written this load
        // F7 E3 (header COST LINE AND BACKSTOP F7): the cost line's own record — closed rebuilds with >= 1 row.
        private int m_CostRebuilds;
        private float m_CostRebuildMsSum, m_CostRebuildMsMax;
        private float m_CostSince = -1f;          // realtimeSinceStartup of the first such rebuild this load

        /// <summary>Set by SkiResortHubUISystem every frame: the hub has an observer on any of the
        /// bindings that need the catalog (U2: resorts, runs, lifts, lodges, city, build). Plain field.</summary>
        public bool Demand;

        /// <summary>Bumped when a rebuild or a rating produced different rows or totals; the hub (and
        /// SkiResortStatsSystem) re-read when it differs from the revision they last saw.</summary>
        public int Revision { get; private set; }

        /// <summary>Totals of the last rebuild (contract §5.3 `summary`). Plain fields.</summary>
        public int Signs, RunsBound, RunsUnbound;
        public float AreaM2;

        // Dev tab / DUMP (plain fields, written by a rebuild).
        public string LastRebuildWhy = "-";
        public float LastRebuildMs, MaxRebuildMs;
        public long Rebuilds, RebuildsChanged;

        // ── U2 rating cache (hub-u2-spec.md §2.1) ──
        /// <summary>Rows with RateState 1 (rated) in the current rows.</summary>
        public int RatedCount { get; private set; }

        /// <summary>Ratings queued and not yet done (warm-up, bind, edit, terrain, backstop).</summary>
        public int RatingsPending => m_Queued.Count;

        /// <summary>Last / maximum rating time this load (ms).</summary>
        public float LastRateMs, MaxRateMs;

        /// <summary>The last rating's text ("-" before the first), logged or not. A PROPERTY, not the
        /// §2.1 field: it is formatted lazily on read (dev tab, DUMP), so warm-up bursts — up to one
        /// rating per frame — format nothing.</summary>
        public string LastRateLine
        {
            get
            {
                if (m_LastRateDirty)
                {
                    m_LastRateDirty = false;
                    m_LastRateLine = RateText(m_LastRateName, m_LastRateEntity, m_LastRating, m_LastRateDetail, m_LastRateReason);
                }
                return m_LastRateLine;
            }
        }

        private string m_LastRateLine = "-";
        private bool m_LastRateDirty;
        private string m_LastRateName;
        private Entity m_LastRateEntity;
        private RunRating m_LastRating;
        private RateDetail m_LastRateDetail;
        private string m_LastRateReason;

        /// <summary>Ratings done this load (all reasons) and the current terrain epoch (dev tab).</summary>
        public long Ratings;
        public int TerrainEpoch => m_TerrainEpoch;

        public bool IsArmed => m_Armed;
        public bool IsDisabled => m_Disabled;
        public int RowCount => m_Rows.Count;

        /// <summary>Row i of the last rebuild, in the order the UI shows them. Main thread.</summary>
        public RunRow GetRow(int i) => m_Rows[i];

        /// <summary>The row of <paramref name="run"/> in the last rebuild (dictionary lookup).</summary>
        public bool TryGetRow(Entity run, out RunRow row)
        {
            if (m_RowIndex.TryGetValue(run, out int i) && i >= 0 && i < m_Rows.Count && m_Rows[i].Entity == run)
            {
                row = m_Rows[i];
                return true;
            }
            row = default;
            return false;
        }

        /// <summary>Rebuild on the next OnUpdate even while nobody listens (dev tab, DUMP).</summary>
        public void RequestRebuild(string why)
        {
            m_RebuildRequest = why;
        }

        // ── Round E1 read API (econ-round spec ch2 §2.4; e1-spec I-11, I-12, R12: both types are
        // NESTED here; header READ API). Stage 0 declared the signatures; stream S2 filled them (the
        // always-on rating service, D12). Main thread only.

        /// <summary>Round E1 (ch2 §2): the rating service's state (= SkiRunRatingRules.Service*).</summary>
        public enum RatingServiceState : byte
        {
            Off = 0,        // not armed (menu, editor, before the first load): nothing to read
            WarmingUp = 1,  // armed; this load's warm-up not finished
            Ready = 2,      // warm-up finished, or WarmupDeadlineS passed (WARN written)
            Failed = 3,     // the one-shot "!!!" disabled the catalog until the next load
        }

        /// <summary>Round E1 (ch2 §2.4): what the simulation may read about one of our runs.</summary>
        public struct RunFacts
        {
            public Entity Run;
            public Entity Gate;       // the run's ski area (TryGetBoundGate); Entity.Null = not linked
            public int Kind;          // RunKind: 0 groomed, 1 snowmaking, 2 night, 3 natural
            public byte RateState;    // SkiRunRatingRules.Rate*: 0 pending, 1 rated, 2 no terrain, 3 too large, 4 failed
            public sbyte Band;        // SlopeBand 0..4 (Flat, Green, Blue, Black, Double black) when RateState 1;
                                      // -1 pending; -2 cannot be rated (RateState 2-4)
            public float AreaM2;      // Geometry.m_SurfaceArea (plan area), always valid
            public float LengthM;     // rated: SlopeResult.Length (horizontal m); else the node estimate
            public float DropM;       // rated: SlopeResult.Drop; else max - min node height
            public float TopM;        // rated: SlopeResult.MaxHeight; else the highest node's y
            public float BottomM;     // rated: SlopeResult.MinHeight; else the lowest node's y
            public float3 TopNode;    // the highest outline node (x, y, z), always valid: the lift-served test point (ch2 §3)
            public bool Approx;       // LengthM / DropM / TopM / BottomM are node estimates (not rated yet)
        }

        /// <summary>Round E1: the rating service's state (header ALWAYS ON). Off outside Game mode and
        /// before the first load; WarmingUp from a load until the warm-up set is rated (or the 90 s
        /// deadline); Ready; Failed after the one-shot "!!!" until the next load.</summary>
        public RatingServiceState ServiceState { get; private set; }

        /// <summary>++ whenever any RunFacts field of any run changed, a run arrived or left, the service
        /// state changed, or a load happened. Compare for equality (it never resets).</summary>
        public int RatingRevision { get; private set; }

        /// <summary>The facts of one run from the last rebuild / rating (dictionary lookup).</summary>
        public bool TryGetRunFacts(Entity run, out RunFacts facts)
        {
            if (!TryGetRow(run, out RunRow row))
            {
                facts = default;
                return false;
            }
            facts = new RunFacts
            {
                Run = row.Entity,
                Gate = row.Gate,
                Kind = row.Kind,
                RateState = row.RateState,
                Band = row.Band,
                AreaM2 = row.AreaM2,
                LengthM = row.LengthM,
                DropM = row.DropM,
                TopM = row.TopM,
                BottomM = row.BottomM,
                TopNode = row.NodeTopPos,
                Approx = row.Approx,
            };
            return true;
        }

        /// <summary>Chapter 1 §18's read (an adapter over the row): true when the run has a CURRENT
        /// rating (state 1-4); band = SlopeBand 0..4 when state 1, else -2; topM / bottomM =
        /// SlopeResult.MaxHeight / MinHeight when state 1, else NaN. False when the run is unknown or
        /// pending. Works whether or not the hub was ever opened (D12). "Current" = the rating's
        /// outline key matched the row's key at the last rebuild (ApplyRating); an outline edited since
        /// that rebuild is seen at the next one (hub open: that frame; closed: within
        /// ClosedMinRebuildIntervalS).</summary>
        public bool TryGetRating(Entity run, out byte state, out sbyte band, out float topM, out float bottomM)
        {
            if (TryGetRow(run, out RunRow row) && row.RateState != RatePending)
            {
                bool rated = row.RateState == RateRated;
                state = row.RateState;
                band = rated ? row.Band : SkiRunRatingRules.BandUnrateable;
                topM = rated ? row.TopM : float.NaN;
                bottomM = rated ? row.BottomM : float.NaN;
                return true;
            }
            state = RatePending;
            band = SkiRunRatingRules.BandPending;
            topM = float.NaN;
            bottomM = float.NaN;
            return false;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();

            m_RunQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),   // every SurfacePrefab area (SurfacePrefab.cs:21)
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                    ComponentType.ReadOnly<Game.Areas.Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            m_BuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            // E1 (ch2 §2.3): the panel-closed trigger (and, since Round M2 D9, the panel-open one too, coalesced
            // the same way). The tags live until CleanUpSystem (Cleanup, after
            // UIUpdate), so UIUpdate sees this frame's tags (CleanUpSystem.cs:53, SystemOrder.cs:67).
            // E1 fixer (e1-spec §8.9 F3): scoped to OUR BOUND runs by SkiRunBinding. Without it the query
            // matched every SurfacePrefab area, and vanilla buildings carry Surface sub-areas (grass,
            // pavement, tiles placeholders in ObjectSubAreas), so every building spawn, upgrade or
            // demolition in a growing city owed a closed rebuild every 0.5 s for the whole session. A new
            // run is bound by a SkiRunBinding write, caught by BindGeneration below; a Deleted run still
            // carries its components this frame; an unbound run scores nothing and is picked up by the
            // ClosedHeartbeatS heartbeat (or "hub opened").
            m_RunChangeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),   // our runs are SurfacePrefab areas (SurfacePrefab.cs:21)
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<SkiRunBinding>(),        // only our bound runs carry it
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });

            // Localized default names: drop the cache when the language changes (the handler only
            // bumps a counter; the next rebuild re-reads the templates).
            m_DictionaryChangedHandler = OnDictionaryChanged;
            try
            {
                GameManager.instance.localizationManager.onActiveDictionaryChanged += m_DictionaryChangedHandler;
            }
            catch (Exception e)
            {
                m_DictionaryChangedHandler = null;
                Mod.Log.Warn($"Catalog: could not subscribe to language changes ({e.GetType().Name}: {e.Message}) — " +
                             "default run names keep the language of their first rebuild until the next load");
            }

            // U2 terrain epoch: ToolSystem.EventToolChanged (public Action field, ToolSystem.cs:30;
            // invoked from the activeTool setter only when the tool changes, :84-98).
            m_ToolChangedHandler = OnToolChanged;
            m_ToolSystem.EventToolChanged += m_ToolChangedHandler;

            Mod.Log.Info($"{nameof(SkiRunCatalogSystem)} created");
        }

        protected override void OnDestroy()
        {
            try
            {
                if (m_DictionaryChangedHandler != null && GameManager.instance != null &&
                    GameManager.instance.localizationManager != null)
                    GameManager.instance.localizationManager.onActiveDictionaryChanged -= m_DictionaryChangedHandler;
                if (m_ToolChangedHandler != null && m_ToolSystem != null)
                    m_ToolSystem.EventToolChanged -= m_ToolChangedHandler;
            }
            catch (Exception)
            {
                // shutting down; nothing to report to
            }
            base.OnDestroy();
        }

        private void OnDictionaryChanged()
        {
            m_NamesGeneration++;
        }

        /// <summary>U2: leaving the terrain tool schedules a terrain epoch bump (applied
        /// TerrainEpochDelayFrames later, in OnUpdate). Field writes only; never throws.</summary>
        private void OnToolChanged(ToolBaseSystem tool)
        {
            if (m_LastTool is TerrainToolSystem && !(tool is TerrainToolSystem))
                m_TerrainEpochDueFrame = m_Frame + Tuning.TerrainEpochDelayFrames;
            m_LastTool = tool;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_WasDemanded = false;
            m_RebuildRequest = null;
            m_SigBindGeneration = int.MinValue;
            m_SigNames = -1;
            m_NextHeartbeat = 0f;
            m_LastHash = 0;
            m_OpenChangePending = false;      // Round M2 D9
            m_RebuildsByReason.Clear();
            m_LastRebuildTime = float.NegativeInfinity;
            m_RebuildLineWindowStart = float.NegativeInfinity;
            m_RebuildLinesInWindow = 0;
            m_RebuildLinesSuppressed = 0;
            m_Rows.Clear();
            m_Scratch.Clear();
            m_RowIndex.Clear();
            m_NameCache.Clear();
            m_TemplatesGeneration = -1;
            Signs = 0;
            RunsBound = 0;
            RunsUnbound = 0;
            AreaM2 = 0f;
            Revision++;   // the previous city's rows are gone: the hub re-sends
            LastRebuildWhy = "-";
            LastRebuildMs = 0f;
            MaxRebuildMs = 0f;
            Rebuilds = 0;
            RebuildsChanged = 0;
            Demand = false;
            // U2 rating cache: nothing of the previous city survives.
            m_Ratings.Clear();
            m_Front.Clear();
            m_Back.Clear();
            m_Queued.Clear();
            m_RateGrid = new SlopeGrid();
            m_RateResult = new SlopeResult();
            m_Frame = 0;
            m_TerrainEpoch = 0;
            m_TerrainEpochDueFrame = -1;
            m_LastTool = m_ToolSystem.activeTool;
            m_WarmupSeeded = false;
            m_WarmupLogged = false;
            m_HeightsLogged = false;
            m_WarmupQueued = 0;
            m_WarmupDone = 0;
            m_WarmupStartFrame = 0;
            m_WarmupTotalMs = 0f;
            m_WarmupMaxMs = 0f;
            m_NextBackgroundStart = 0f;
            m_NextBackstop = 0f;
            m_RateLineWindowStart = 0f;
            m_RateLinesInWindow = 0;
            m_RateLinesSuppressed = 0;
            m_SlowRateWarned = false;
            RatedCount = 0;
            LastRateMs = 0f;
            MaxRateMs = 0f;
            m_LastRateLine = "-";
            m_LastRateDirty = false;
            Ratings = 0;
            // Round E1 (ch2 §2.3): the always-on service starts Off; Game mode arms it below.
            m_ClosedChangePending = false;
            m_ArmedAt = 0f;
            m_WarmupDeadlineHit = false;
            m_LastFactsHash = 0;
            m_ClosedRebuilds = 0;
            m_ClosedRatings = 0;
            m_ClosedRebuildMsSum = 0f;
            m_ClosedRebuildMsMax = 0f;
            m_ClosedRatingMsSum = 0f;
            m_ClosedRatingMsMax = 0f;
            m_ClosedSince = -1f;
            m_ServiceCostLogged = false;
            m_CostRebuilds = 0;                 // F7 E3
            m_CostRebuildMsSum = 0f;
            m_CostRebuildMsMax = 0f;
            m_CostSince = -1f;
            ServiceState = RatingServiceState.Off;

            if (mode != GameMode.Game)
            {
                RatingRevision++;   // a load happened (the rows are gone)
                return;
            }

            m_Armed = true;
            ServiceState = RatingServiceState.WarmingUp;
            m_ArmedAt = UnityEngine.Time.realtimeSinceStartup;
            m_RebuildRequest = "load";   // the first rebuild writes one "Catalog [rebuild] (load)" line, hub or no hub
            RatingRevision++;            // a load happened
            Mod.Log.Info("Catalog [arm] — run list, resort links and built-run ratings, ALWAYS ON (Round E1, D12: the " +
                         "simulation scores runs by these ratings). Panel open: rebuild at once when it opens, then on " +
                         "one of our bound runs Created/Updated/Deleted (coalesced to " +
                         $"{Tuning.ClosedMinRebuildIntervalS:0.##} s), BindGeneration, a language change, a " +
                         $"{Tuning.HeartbeatS:0.##} s heartbeat (wall clock; Round M2: no building, surface-area or area-edit " +
                         "trigger). Panel closed: rebuild when one of our bound runs is " +
                         "Created/Updated/Deleted or BindGeneration moves (at most every " +
                         $"ClosedMinRebuildIntervalS={Tuning.ClosedMinRebuildIntervalS:0.##} s) or every " +
                         $"ClosedHeartbeatS={Tuning.ClosedHeartbeatS:0.##} s; no sign count, no language trigger. Changed " +
                         $"signature rebuilds logged at most RebuildLinesPerWindow={Tuning.RebuildLinesPerWindow} per " +
                         $"RebuildLineWindowS={Tuning.RebuildLineWindowS:0.##} s (the rest counted). Per run: " +
                         "area = Geometry.m_SurfaceArea (plan), U1 estimates length = (P + sqrt(max(0, P² − 16A))) / 4 over the " +
                         "node ring and drop = max − min node height until rated; Gate = TryGetBoundGate; kinds " +
                         $"[{string.Join(", ", KindNames)}]; default names [{string.Join(", ", DefaultNameEnglish)}] numbered " +
                         "per (resort, kind) by top height (OD-3), custom names from NameSystem win; rows sent linked first, " +
                         "unlinked last. RATING CACHE (hub-u2-spec §3.5): SkiRunRater on the built node ring, stepCurrent 0, " +
                         "no height reuse; key = FNV-1a of node x/z + count (edit → front), y-key = node y + count (re-snap → " +
                         "background 'terrain'); warm-up entries never promoted by 'bind'; band -1 pending, -2 cannot be rated; " +
                         $"WarmupFrames={Tuning.WarmupFrames} (+ heights ready, MinHeightmapResolution=" +
                         $"{SkiRunDrawFeedbackSystem.Tuning.MinHeightmapResolution}); one rating per UIUpdate frame, paused " +
                         "included; bind/edit at once; warm-up/terrain/backstop duty factor " +
                         $"DutyFactorOpen={SkiRunRatingRules.DutyFactorOpen:0.##} open, " +
                         $"DutyFactorClosed={SkiRunRatingRules.DutyFactorClosed:0.##} and " +
                         $"ClosedMinGapS={SkiRunRatingRules.ClosedMinGapS:0.##} s closed; backstop " +
                         $"BackstopS={Tuning.BackstopS:0.##} s open, ClosedBackstopS={Tuning.ClosedBackstopS:0.##} s closed; " +
                         $"warm-up deadline WarmupDeadlineS={SkiRunRatingRules.WarmupDeadlineS:0.##} s; RatingRevision moves " +
                         "when a run fact the simulation reads changes (gate, kind, area, rate state, band, length, drop, top, " +
                         "bottom, top node); terrain epoch on leaving the terrain tool + " +
                         $"TerrainEpochDelayFrames={Tuning.TerrainEpochDelayFrames}; ShrinkCells={Tuning.ShrinkCells}; [rate] " +
                         $"lines for bind / edit, at most RateLinesPerWindow={Tuning.RateLinesPerWindow} per " +
                         $"RateLineWindowS={Tuning.RateLineWindowS:0.##} s; one slow-rating WARN per load above the drawing " +
                         $"tooltip's SlowRecomputeWarnMs={SkiRunDrawFeedbackSystem.Tuning.SlowRecomputeWarnMs:0.##} ms; " +
                         $"DumpRowsMax={Tuning.DumpRowsMax}");
        }

        protected override void OnUpdate()
        {
            try
            {
                m_Frame++;
                // U2 terrain epoch: one int compare per frame, hub open or not.
                if (m_TerrainEpochDueFrame >= 0 && m_Frame >= m_TerrainEpochDueFrame)
                {
                    m_TerrainEpochDueFrame = -1;
                    m_TerrainEpoch++;
                }

                if (m_Disabled || !m_Armed)
                    return;
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;

                // Round E1 (D12, header ALWAYS ON): one service, two cadences — the hub's Demand picks
                // the triggers, the sign count, the background throttle and the backstop interval.
                bool demand = Demand;
                float now = UnityEngine.Time.realtimeSinceStartup;
                string why = m_RebuildRequest;
                m_RebuildRequest = null;
                if (demand && !m_WasDemanded && why == null)
                    why = "hub opened";
                m_WasDemanded = demand;   // read by LogWarmupDone (the factor in force) this frame
                if (why == null)
                    why = demand ? OpenSignatureWhy(now) : ClosedSignatureWhy(now);
                if (why != null)
                {
                    Rebuild(why, now, countSigns: demand);
                    m_LastRebuildTime = now;
                    m_NextHeartbeat = now + (demand ? Tuning.HeartbeatS : Tuning.ClosedHeartbeatS);
                }
                RateStep(now, demand);       // both modes (D12): at most one rating per frame
                ServiceTick(now);            // warm-up deadline + the one-time cost line
            }
            catch (Exception e)
            {
                HandleException(e, "OnUpdate");
            }
        }

        /// <summary>The panel-open change signature, Round M2 D9 (header CONTRACT; measure-core.md 2.8, build-contract
        /// "Decided here" 11): in order "run changed" (one of our bound runs Created / Updated / Deleted, coalesced to
        /// ClosedMinRebuildIntervalS like the closed path), "bind generation", "language", the heartbeat. Per frame:
        /// one IsEmptyIgnoreFilter, two int compares, float compares; no allocation. Null when nothing moved.</summary>
        private string OpenSignatureWhy(float now)
        {
            if (!m_RunChangeQuery.IsEmptyIgnoreFilter)
                m_OpenChangePending = true;
            if (m_OpenChangePending && now - m_LastRebuildTime >= Tuning.ClosedMinRebuildIntervalS)
                return WhyRunChanged;   // Rebuild clears the pending flag
            if (m_Ownership.BindGeneration != m_SigBindGeneration)
                return "bind generation";
            if (m_NamesGeneration != m_SigNames)
                return "language";
            return now >= m_NextHeartbeat ? WhyHeartbeat : null;
        }

        /// <summary>E1: the panel-closed triggers (header ALWAYS ON). An event on one of our bound run areas
        /// (m_RunChangeQuery carries SkiRunBinding; vanilla surface sub-areas of buildings do not match) or a
        /// binding write marks a rebuild owed; it runs at most once per ClosedMinRebuildIntervalS. Else the
        /// heartbeat. Per frame: one IsEmptyIgnoreFilter, one int compare, two float compares.</summary>
        private string ClosedSignatureWhy(float now)
        {
            if (!m_RunChangeQuery.IsEmptyIgnoreFilter || m_Ownership.BindGeneration != m_SigBindGeneration)
                m_ClosedChangePending = true;
            if (m_ClosedChangePending && now - m_LastRebuildTime >= Tuning.ClosedMinRebuildIntervalS)
            {
                m_ClosedChangePending = false;
                return WhyRunChangedClosed;
            }
            return now >= m_NextHeartbeat ? WhyHeartbeat : null;
        }

        // ── rebuild ─────────────────────────────────────────────────────────────────────────────

        private void Rebuild(string why, float now, bool countSigns)
        {
            m_Timer.Reset();
            m_Timer.Start();

            // The signature this rebuild answers (read before the walk, so a change during it re-runs). Round M2 D9:
            // the surface-area and building counts are no longer read (not triggers); any rebuild answers a pending
            // panel-open run change (it walks every run).
            m_SigBindGeneration = m_Ownership.BindGeneration;
            m_SigNames = m_NamesGeneration;
            m_OpenChangePending = false;

            m_Scratch.Clear();
            int signs = 0;
            bool ready = m_Ownership.PrefabSetsReady;   // sign + run prefab sets resolved this load
            if (ready)
            {
                // E1 (header ALWAYS ON): the building walk only while the hub listens; closed, Signs
                // keeps its last value (it feeds only the hub, and "hub opened" recounts).
                signs = countSigns ? CountSigns() : Signs;
                CollectRuns();
            }

            // Ordinals (OD-3): per (Gate, kind), top height descending.
            m_Scratch.Sort(OrdinalOrder.Instance);
            Entity lastGate = new Entity { Index = -1, Version = -1 };
            int lastKind = -1, n = 0;
            for (int r = 0; r < m_Scratch.Count; r++)
            {
                RunRow row = m_Scratch[r];
                if (row.Kind != lastKind || row.Gate != lastGate)
                {
                    lastKind = row.Kind;
                    lastGate = row.Gate;
                    n = 0;
                }
                row.Ordinal = ++n;
                m_Scratch[r] = row;
            }

            // Names + the rating cache's view of each row.
            RefreshTemplates();
            for (int r = 0; r < m_Scratch.Count; r++)
            {
                RunRow row = m_Scratch[r];
                if (m_NameSystem.TryGetCustomName(row.Entity, out string custom) && !string.IsNullOrEmpty(custom))
                {
                    row.Name = custom;
                    row.CustomName = true;
                }
                else
                {
                    row.Name = DefaultName(row.Kind, row.Ordinal);
                    row.CustomName = false;
                }
                ApplyRating(ref row);
                m_Scratch[r] = row;
            }

            // Display order: linked first, unlinked last; highest top first.
            m_Scratch.Sort(DisplayOrder.Instance);

            int bound = 0, unbound = 0;
            float area = 0f;
            for (int r = 0; r < m_Scratch.Count; r++)
            {
                RunRow row = m_Scratch[r];
                if (row.Bound)
                    bound++;
                else
                    unbound++;
                area += row.AreaM2;
            }
            ulong hash = ComputeHash(m_Scratch, signs, out int rated);

            bool changed = hash != m_LastHash || m_Scratch.Count != m_Rows.Count || Rebuilds == 0;
            List<RunRow> swap = m_Rows;
            m_Rows = m_Scratch;
            m_Scratch = swap;
            m_RowIndex.Clear();
            for (int r = 0; r < m_Rows.Count; r++)
                m_RowIndex[m_Rows[r].Entity] = r;
            Signs = signs;
            RunsBound = bound;
            RunsUnbound = unbound;
            AreaM2 = area;
            RatedCount = rated;
            m_LastHash = hash;
            if (changed)
            {
                Revision++;
                RebuildsChanged++;
            }
            Rebuilds++;
            // Round M2 D9: per reason (literal why-texts; a key is added once, then only its count moves).
            m_RebuildsByReason.TryGetValue(why, out int byWhy);
            m_RebuildsByReason[why] = byWhy + 1;

            // U2: queue what needs a rating, drop cache entries of runs that left.
            UpdateQueue();
            // E1: the simulation's facts (RatingRevision) after the rows and the queue settled.
            UpdateFactsRevision();

            m_Timer.Stop();
            float ms = (float)m_Timer.Elapsed.TotalMilliseconds;
            LastRebuildMs = ms;
            if (ms > MaxRebuildMs)
                MaxRebuildMs = ms;
            LastRebuildWhy = why;
            if (!countSigns)
            {
                // E1: the panel-closed rebuild cost ("Catalog [service]", once per load).
                if (m_ClosedRebuilds == 0)
                    m_ClosedSince = now;
                m_ClosedRebuilds++;
                m_ClosedRebuildMsSum += ms;
                if (ms > m_ClosedRebuildMsMax)
                    m_ClosedRebuildMsMax = ms;
                // F7 E3: the cost line's record counts only rebuilds that had a run to walk.
                if (m_Rows.Count > 0)
                {
                    if (m_CostRebuilds == 0)
                        m_CostSince = now;
                    m_CostRebuilds++;
                    m_CostRebuildMsSum += ms;
                    if (ms > m_CostRebuildMsMax)
                        m_CostRebuildMsMax = ms;
                }
            }

            bool requested = !ReferenceEquals(why, WhyHeartbeat) && !IsSignatureWhy(why);
            bool log = requested;
            if (changed && !requested)
            {
                // Header: changed signature / heartbeat rebuilds are windowed (finding u2-data-01).
                if (now - m_RebuildLineWindowStart >= Tuning.RebuildLineWindowS)
                {
                    m_RebuildLineWindowStart = now;
                    m_RebuildLinesInWindow = 0;
                }
                if (m_RebuildLinesInWindow < Tuning.RebuildLinesPerWindow)
                {
                    m_RebuildLinesInWindow++;
                    log = true;
                }
                else
                {
                    m_RebuildLinesSuppressed++;
                }
            }
            else if (!log && m_RebuildLinesSuppressed > 0 && now - m_RebuildLineWindowStart >= Tuning.RebuildLineWindowS)
            {
                // Trailing line once the window passed: the final state is never silent.
                m_RebuildLineWindowStart = now;
                m_RebuildLinesInWindow = 1;
                log = true;
            }
            if (log)
            {
                string suffix = m_RebuildLinesSuppressed > 0
                    ? $" (+{m_RebuildLinesSuppressed} earlier changed rebuild(s) not logged: more than " +
                      $"{Tuning.RebuildLinesPerWindow} per {Tuning.RebuildLineWindowS:0} s)"
                    : "";
                m_RebuildLinesSuppressed = 0;
                // Round M1 (D8): with the hub closed the signs are not counted (Signs keeps its last value, 0 after a
                // load), so the line says so instead of printing a count that reads as "no sign".
                string signText = countSigns ? $"{signs} Welcome Sign(s)" : "Welcome Signs not counted (hub closed)";
                Mod.Log.Info($"Catalog [rebuild] ({why}): {m_Rows.Count} run(s) — {bound} linked to a sign, " +
                             $"{unbound} not linked; {signText}; area {area:F0} m²; rated {rated}, " +
                             $"{RatingsPending} rating(s) queued; {ms:F2} ms; " +
                             $"revision {Revision} ({(changed ? "changed" : "unchanged")})" +
                             (ready ? "" : " — ownership prefab sets not ready (pre-import?): empty list") + suffix);
            }
        }

        /// <summary>Signature-driven rebuilds with an unchanged result are counted, not logged (Round M2 D9: the
        /// surface-area, building-count and area-edited reasons are gone; "run changed" joins).</summary>
        private static bool IsSignatureWhy(string why)
        {
            return why == WhyRunChanged || why == "bind generation" || why == "language" || why == WhyRunChangedClosed;
        }

        /// <summary>E1 (ch2 §2.3): RatingRevision++ when the hash of every RunFacts field moved.</summary>
        private void UpdateFactsRevision()
        {
            ulong f = ComputeFactsHash(m_Rows);
            if (f != m_LastFactsHash)
            {
                m_LastFactsHash = f;
                RatingRevision++;
            }
        }

        /// <summary>E1: FNV-1a (SkiRunRatingRules.Mix) over, per row in row order, Entity, Gate, Kind,
        /// RateState | Band &lt;&lt; 8 and the bits of AreaM2, LengthM, DropM, TopM, BottomM and NodeTopPos;
        /// then the row count. Names, ordinals, grades and the sign count are not inputs (a rename does
        /// not move RatingRevision; the hub's Revision covers those).</summary>
        private static ulong ComputeFactsHash(List<RunRow> rows)
        {
            ulong hash = SkiRunRatingRules.FnvOffset;
            for (int r = 0; r < rows.Count; r++)
            {
                RunRow row = rows[r];
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Entity.Index);
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Entity.Version);
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Gate.Index);
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Gate.Version);
                hash = SkiRunRatingRules.Mix(hash, (uint)row.Kind);
                hash = SkiRunRatingRules.Mix(hash, (uint)row.RateState | ((uint)(byte)row.Band << 8));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.AreaM2));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.LengthM));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.DropM));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.TopM));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.BottomM));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.NodeTopPos.x));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.NodeTopPos.y));
                hash = SkiRunRatingRules.Mix(hash, math.asuint(row.NodeTopPos.z));
            }
            return SkiRunRatingRules.Mix(hash, (uint)rows.Count);
        }

        /// <summary>FNV-1a over every row field the hub or the stats can see, plus the sign count.
        /// U2 adds Gate (without it a free re-link to another sign would change nothing the hub
        /// sees), Ordinal, RateState, Band, GradePct, SteepestPct, TopM, BottomM, Approx (§3.5 "Hash").</summary>
        private static ulong ComputeHash(List<RunRow> rows, int signs, out int rated)
        {
            rated = 0;
            ulong hash = 14695981039346656037UL;
            hash = Mix(hash, (uint)signs);
            for (int r = 0; r < rows.Count; r++)
            {
                RunRow row = rows[r];
                if (row.RateState == RateRated)
                    rated++;
                hash = Mix(hash, (uint)row.Entity.Index);
                hash = Mix(hash, (uint)row.Entity.Version);
                hash = Mix(hash, (uint)row.Kind);
                hash = Mix(hash, row.Bound ? 1u : 0u);
                hash = Mix(hash, math.asuint(row.LengthM));
                hash = Mix(hash, math.asuint(row.DropM));
                hash = Mix(hash, math.asuint(row.AreaM2));
                hash = Mix(hash, (uint)(row.Name == null ? 0 : row.Name.GetHashCode()));
                hash = Mix(hash, (uint)row.Gate.Index);
                hash = Mix(hash, (uint)row.Gate.Version);
                hash = Mix(hash, (uint)row.Ordinal);
                hash = Mix(hash, (uint)row.RateState | ((uint)(byte)row.Band << 8) | (row.Approx ? 1u << 16 : 0u));
                hash = Mix(hash, math.asuint(row.GradePct));
                hash = Mix(hash, math.asuint(row.SteepestPct));
                hash = Mix(hash, math.asuint(row.TopM));
                hash = Mix(hash, math.asuint(row.BottomM));
            }
            return Mix(hash, (uint)rows.Count);
        }

        /// <summary>Live Welcome Signs: a chunk walk of every building, PrefabRef checked against the
        /// ownership system's sign set (the same test as SkiRunOwnershipSystem.CollectGates). Chunk
        /// reads through type handles do not wait for running jobs (Entities semantics, unverified
        /// here — the ownership header), so the query's writers are completed first.</summary>
        private int CountSigns()
        {
            if (m_BuildingQuery.IsEmptyIgnoreFilter)
                return 0;
            m_BuildingQuery.CompleteDependency();
            int count = 0;
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            using (var chunks = m_BuildingQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (m_Ownership.IsGatePrefab(prefabRefs[i].m_Prefab))
                            count++;
                    }
                }
            }
            return count;
        }

        /// <summary>Fills m_Scratch with every live run of ours. Two loops (the ownership pattern):
        /// the chunk walk copies entity, kind and geometry; the node buffers and the bound test are
        /// read after the chunk array is released.</summary>
        private void CollectRuns()
        {
            if (m_RunQuery.IsEmptyIgnoreFilter)
                return;
            m_RunQuery.CompleteDependency();
            var entityHandle = GetEntityTypeHandle();
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            var geometryHandle = GetComponentTypeHandle<Game.Areas.Geometry>(isReadOnly: true);
            using (var chunks = m_RunQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var geometries = chunks[c].GetNativeArray(ref geometryHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        int kind = m_Ownership.KindOf(prefabRefs[i].m_Prefab);
                        if (kind < 0)
                            continue;
                        Game.Areas.Geometry g = geometries[i];
                        m_Scratch.Add(new RunRow
                        {
                            Entity = entities[i],
                            Kind = kind,
                            AreaM2 = Finite(g.m_SurfaceArea),
                            TopY = Finite(g.m_Bounds.max.y),
                            CenterX = Finite(g.m_CenterPosition.x),
                            CenterZ = Finite(g.m_CenterPosition.z),
                        });
                    }
                }
            }

            for (int r = 0; r < m_Scratch.Count; r++)
            {
                RunRow row = m_Scratch[r];
                if (EntityManager.HasBuffer<Game.Areas.Node>(row.Entity))
                {
                    DynamicBuffer<Game.Areas.Node> nodes =
                        EntityManager.GetBuffer<Game.Areas.Node>(row.Entity, isReadOnly: true);
                    RingMetrics(nodes, out row.NodeLengthM, out row.NodeDropM, out row.NodeTopM, out row.NodeBottomM,
                        out row.Key, out row.YKey, out row.NodeTopPos);
                }
                // THE bound test, returning the sign (U2: the run's resort, §3.1).
                row.Bound = m_Ownership.TryGetBoundGate(row.Entity, out row.Gate);
                m_Scratch[r] = row;
            }
        }

        /// <summary>LengthRect (SkiSlopeClassifier.EquivalentRectLength, the same arithmetic in
        /// double), the node height span and extremes over an area's Node ring, and (U2, amended in
        /// the fixer round) the rating cache keys: Key = FNV-1a over every node's x and z float bits,
        /// YKey over the y bits, each closed with the node count (SkiRunRatingRules — the same bytes in
        /// the same order as KeyOf). The ring is implicitly closed (the last node connects to the
        /// first); a duplicated vertex adds nothing to P or A. Non-finite results read 0.
        /// Round E1 (ch2 §2.3): <paramref name="topNode"/> = the node whose y set the maximum (the
        /// first one on a tie), x/y/z as stored — the lift-served test point (RunFacts.TopNode);
        /// float3.zero for an empty ring.</summary>
        private static void RingMetrics(DynamicBuffer<Game.Areas.Node> nodes, out float length, out float drop,
            out float top, out float bottom, out ulong key, out ulong yKey, out float3 topNode)
        {
            length = 0f;
            drop = 0f;
            top = 0f;
            bottom = 0f;
            topNode = float3.zero;
            SkiRunRatingRules.Begin(out key, out yKey);
            int m = nodes.Length;
            if (m < 1)
            {
                SkiRunRatingRules.Finish(ref key, ref yKey, 0);
                return;
            }
            float minY = nodes[0].m_Position.y, maxY = minY;
            int topIndex = 0;
            double x0 = nodes[0].m_Position.x, z0 = nodes[0].m_Position.z;
            double per = 0.0, s2 = 0.0;
            for (int k = 0; k < m; k++)
            {
                float3 pk = nodes[k].m_Position;
                float3 pl = nodes[k + 1 < m ? k + 1 : 0].m_Position;
                SkiRunRatingRules.AddNode(ref key, ref yKey, pk);
                if (pk.y < minY) minY = pk.y;
                if (pk.y > maxY)
                {
                    maxY = pk.y;
                    topIndex = k;   // strict: the first node on a tie keeps it
                }
                double ax = pk.x - x0, az = pk.z - z0, bx = pl.x - x0, bz = pl.z - z0;
                double dx = bx - ax, dz = bz - az;
                per += Math.Sqrt(dx * dx + dz * dz);
                s2 += ax * bz - bx * az;
            }
            SkiRunRatingRules.Finish(ref key, ref yKey, m);
            double area = Math.Abs(0.5 * s2);
            double disc = per * per - 16.0 * area;
            if (!(disc > 0.0))
                disc = 0.0;
            double len = (per + Math.Sqrt(disc)) * 0.25;
            length = Finite((float)len);
            drop = Finite(maxY - minY);
            top = Finite(maxY);
            bottom = Finite(minY);
            topNode = nodes[topIndex].m_Position;
        }


        private static float Finite(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
        }

        private static ulong Mix(ulong hash, uint value)
        {
            // FNV-1a over the four bytes of value.
            for (int b = 0; b < 4; b++)
            {
                hash ^= (value >> (8 * b)) & 0xFFu;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        // ── U2 rating cache ─────────────────────────────────────────────────────────────────────

        /// <summary>Copies the cached rating into a row when it belongs to the row's CURRENT outline
        /// (same x/z Key); otherwise the row is pending and shows the U1 node estimates. A rating from
        /// an older terrain epoch or older node heights (YKey) is still shown until its re-rate lands
        /// (header INVALIDATION). A final "cannot be rated" state shows band -2 (header ROW BAND).</summary>
        private void ApplyRating(ref RunRow row)
        {
            if (m_Ratings.TryGetValue(row.Entity, out RunRating r) && r.Key == row.Key)
            {
                row.RateState = r.State;
                if (r.State == RateRated)
                {
                    row.Band = SkiRunRatingRules.RowBand(r.State, r.Band);
                    row.GradePct = r.RatingGrade * 100f;
                    row.SteepestPct = r.SteepestGrade * 100f;
                    row.TopM = r.TopM;
                    row.BottomM = r.BottomM;
                    row.LengthM = r.LengthM;
                    row.DropM = r.DropM;
                    row.Approx = false;
                    return;
                }
            }
            else
            {
                row.RateState = RatePending;
            }
            row.Band = SkiRunRatingRules.RowBand(row.RateState, -1);   // -1 pending, -2 cannot be rated
            row.GradePct = -1f;
            row.SteepestPct = -1f;
            row.TopM = row.NodeTopM;
            row.BottomM = row.NodeBottomM;
            row.LengthM = row.NodeLengthM;
            row.DropM = row.NodeDropM;
            row.Approx = true;
        }

        /// <summary>Rebuild-time queue maintenance (header QUEUE / INVALIDATION): drops cache and
        /// queue entries of runs that left, and queues every row that needs a (re-)rating. Before
        /// the warm-up gate opens nothing is queued (RateStep seeds the warm-up then).</summary>
        private void UpdateQueue()
        {
            // Rows that left: drop their cache entries (queued entries are skipped on dequeue).
            m_ScratchEntities.Clear();
            foreach (var pair in m_Ratings)
            {
                if (!m_RowIndex.ContainsKey(pair.Key))
                    m_ScratchEntities.Add(pair.Key);
            }
            for (int i = 0; i < m_ScratchEntities.Count; i++)
                m_Ratings.Remove(m_ScratchEntities[i]);
            m_ScratchEntities.Clear();
            foreach (var pair in m_Queued)
            {
                if (!m_RowIndex.ContainsKey(pair.Key))
                    m_ScratchEntities.Add(pair.Key);
            }
            for (int i = 0; i < m_ScratchEntities.Count; i++)
            {
                bool warmup = m_Queued.TryGetValue(m_ScratchEntities[i], out byte reason) && reason == ReasonWarmup;
                m_Queued.Remove(m_ScratchEntities[i]);
                if (warmup)
                    WarmupProgress(0f);   // a warm-up entry whose run is gone counts as done
            }
            if (!m_WarmupSeeded)
                return;
            for (int r = 0; r < m_Rows.Count; r++)
                QueueRowIfNeeded(m_Rows[r], ReasonBind);
        }

        /// <summary>Queues one row (SkiRunRatingRules.NeedsRating): no cache entry →
        /// <paramref name="missingReason"/> (bind after the warm-up, warm-up while seeding); x/z Key
        /// mismatch → edit (front); YKey mismatch or older terrain epoch → terrain (back). A row
        /// already queued keeps its place unless SkiRunRatingRules.Replaces says the new reason takes
        /// over (a front reason over a background one, never bind over warm-up; the stale back entry
        /// is skipped on dequeue).</summary>
        private void QueueRowIfNeeded(RunRow row, byte missingReason)
        {
            bool cached = m_Ratings.TryGetValue(row.Entity, out RunRating r);
            byte reason = SkiRunRatingRules.NeedsRating(cached, r.Key, r.YKey, r.TerrainEpoch, row.Key, row.YKey,
                m_TerrainEpoch, missingReason);
            if (reason == SkiRunRatingRules.ReasonNone)
                return;
            Enqueue(row.Entity, reason);
        }

        /// <summary><paramref name="requeue"/> = RateOne putting an entry back (heights not readable
        /// this frame): it was counted when first queued.</summary>
        private void Enqueue(Entity run, byte reason, bool requeue = false)
        {
            bool front = SkiRunRatingRules.IsFront(reason);
            bool promoted = false;
            if (m_Queued.TryGetValue(run, out byte queued))
            {
                if (!SkiRunRatingRules.Replaces(queued, reason))
                    return;
                promoted = queued == ReasonWarmup;
            }
            m_Queued[run] = reason;
            if (reason == ReasonWarmup && !requeue)
                m_WarmupQueued++;
            (front ? m_Front : m_Back).Enqueue(new QueueEntry { Run = run, Reason = reason });
            if (promoted)
                WarmupProgress(0f);   // promoted out of the warm-up set: counts as done there
        }

        /// <summary>One rating at most (header QUEUE). Seeds the warm-up once the gate opens; runs the
        /// backstop timer; picks the front queue first, the back queue only within the duty cycle.
        /// Round E1: runs hub open or closed; <paramref name="open"/> picks the backstop interval and
        /// the duty factor (header ALWAYS ON).</summary>
        private void RateStep(float now, bool open)
        {
            if (m_Frame < Tuning.WarmupFrames)
                return;
            if (!m_WarmupSeeded)
            {
                if (!HeightsReady(out TerrainHeightData hd0))
                {
                    if (!m_HeightsLogged)
                    {
                        m_HeightsLogged = true;
                        Mod.Log.Info($"Catalog [rate] terrain heights not usable yet (created {hd0.isCreated}, resolution " +
                                     $"{hd0.resolution.x}, need {SkiRunDrawFeedbackSystem.Tuning.MinHeightmapResolution}) — runs " +
                                     "stay 'being rated' until they are; retried every frame, hub open or closed (logged once per load)");
                    }
                    return;
                }
                m_WarmupSeeded = true;
                m_WarmupStartFrame = m_Frame;
                for (int r = 0; r < m_Rows.Count; r++)
                    QueueRowIfNeeded(m_Rows[r], ReasonWarmup);
                if (m_WarmupQueued == 0)
                    LogWarmupDone();
            }

            // Backstop: the oldest-rated run, when nothing else is waiting. E1: every BackstopS with the
            // hub open, every ClosedBackstopS with it closed; a timer armed while closed is pulled in when
            // the hub opens, so the open cadence stays the U2 one.
            float backstopS = open ? Tuning.BackstopS : Tuning.ClosedBackstopS;
            if (open && m_NextBackstop > now + Tuning.BackstopS)
                m_NextBackstop = now + Tuning.BackstopS;
            if (now >= m_NextBackstop)
            {
                m_NextBackstop = now + backstopS;
                if (m_Queued.Count == 0)
                    QueueOldestRated();
            }

            QueueEntry entry;
            bool background;
            if (!TryDequeue(m_Front, out entry))
            {
                if (now < m_NextBackgroundStart || !TryDequeue(m_Back, out entry))
                    return;
                background = true;
            }
            else
            {
                background = false;
            }
            RateOne(entry, background, now, open);
        }

        /// <summary>Next live entry of a queue: skips entries whose run left the rows or whose queued
        /// reason changed (promoted to the front). Removes the run from m_Queued on success.</summary>
        private bool TryDequeue(Queue<QueueEntry> queue, out QueueEntry entry)
        {
            while (queue.Count > 0)
            {
                entry = queue.Dequeue();
                if (!m_Queued.TryGetValue(entry.Run, out byte reason) || reason != entry.Reason)
                    continue;
                m_Queued.Remove(entry.Run);
                if (!m_RowIndex.ContainsKey(entry.Run))
                {
                    if (entry.Reason == ReasonWarmup)
                        WarmupProgress(0f);
                    continue;
                }
                return true;
            }
            entry = default;
            return false;
        }

        private void QueueOldestRated()
        {
            Entity oldest = Entity.Null;
            float oldestAt = float.MaxValue;
            foreach (var pair in m_Ratings)
            {
                if (pair.Value.RatedAt < oldestAt && m_RowIndex.ContainsKey(pair.Key))
                {
                    oldestAt = pair.Value.RatedAt;
                    oldest = pair.Key;
                }
            }
            if (oldest != Entity.Null)
                Enqueue(oldest, ReasonBackstop);
        }

        private bool HeightsReady(out TerrainHeightData hd)
        {
            hd = m_TerrainSystem.GetHeightData();   // fresh, never cached (feedback header TERRAIN READ)
            return hd.isCreated && hd.resolution.x >= SkiRunDrawFeedbackSystem.Tuning.MinHeightmapResolution;
        }

        /// <summary>Rates one run, stores the result, patches its row, logs (bind / edit) or counts
        /// (warm-up / terrain / backstop). E1: the duty cycle and the closed-panel cost counters follow
        /// <paramref name="open"/>; RatingRevision follows the patched row.</summary>
        private void RateOne(QueueEntry entry, bool background, float now, bool open)
        {
            int idx = m_RowIndex[entry.Run];
            RunRow row = m_Rows[idx];
            if (!HeightsReady(out TerrainHeightData hd))
            {
                // Not readable this frame: put it back (at the end of its queue); retried later.
                Enqueue(entry.Run, entry.Reason, requeue: true);
                return;
            }

            long t0 = Stopwatch.GetTimestamp();
            int n = 0;
            if (EntityManager.HasBuffer<Game.Areas.Node>(entry.Run))
            {
                DynamicBuffer<Game.Areas.Node> nodes = EntityManager.GetBuffer<Game.Areas.Node>(entry.Run, isReadOnly: true);
                n = nodes.Length;
                if (m_RateNodes.Length < n)
                    m_RateNodes = new float3[math.max(n, m_RateNodes.Length * 2)];
                for (int k = 0; k < n; k++)
                    m_RateNodes[k] = nodes[k].m_Position;
            }

            float texel = 1f / hd.scale.x;                            // TerrainSystem.cs:2619-2620
            var origin = new double2(-hd.offset.x, -hd.offset.z);    // texel centres, TerrainUtils.cs:35-38
            var sampler = new TerrainHeightSampler(hd);
            RateStatus status = SkiRunRater.Rate(m_RateNodes, n, origin, ref sampler, texel, 0, m_RateGrid, null, m_RateResult,
                out int m, out float _, out float _, out RateDetail detail);
            long t1 = Stopwatch.GetTimestamp();
            float ms = (float)((t1 - t0) * 1000.0 / Stopwatch.Frequency);

            SlopeResult res = m_RateResult;
            // The keys of the nodes actually rated (an edit since the last rebuild shows up as a
            // mismatch at the next rebuild and is re-rated then, as "edit" or "terrain").
            SkiRunRatingRules.Keys(m_RateNodes, n, out ulong rateKey, out ulong rateYKey);
            var rating = new RunRating
            {
                Key = rateKey,
                YKey = rateYKey,
                TerrainEpoch = m_TerrainEpoch,
                StepMultiple = m,
                RateMs = ms,
                RatedAt = now,
                Band = -1,
            };
            switch (status)
            {
                case RateStatus.Rated:
                    rating.State = RateRated;
                    rating.Band = (sbyte)res.Band;
                    rating.RatingGrade = res.RatingGrade;
                    rating.SteepestGrade = res.SteepestGrade;   // = RatingGrade in polyline mode (Classify)
                    rating.MeanGrade = res.MeanGrade;
                    rating.Connected = res.Connected;
                    rating.DropM = res.Drop;
                    rating.TopM = res.MaxHeight;
                    rating.BottomM = res.MinHeight;
                    rating.LengthM = res.Length;
                    break;
                case RateStatus.NoTerrain:
                    rating.State = RateNoTerrain;
                    break;
                case RateStatus.TooLarge:
                    rating.State = RateTooLarge;
                    break;
                default:
                    rating.State = RateFailed;
                    break;
            }
            m_Ratings[entry.Run] = rating;
            Ratings++;
            LastRateMs = ms;
            if (ms > MaxRateMs)
                MaxRateMs = ms;
            if (background)
                m_NextBackgroundStart = SkiRunRatingRules.NextBackgroundStart(now, ms, open);   // open: = now + 8 × ms (U2)
            if (!open)
            {
                // E1: the panel-closed rating cost ("Catalog [service]", once per load).
                m_ClosedRatings++;
                m_ClosedRatingMsSum += ms;
                if (ms > m_ClosedRatingMsMax)
                    m_ClosedRatingMsMax = ms;
            }

            // F6 D6 (header SLOW RATING): once per load, with the numbers; read before the shrink below.
            if (ms > SkiRunDrawFeedbackSystem.Tuning.SlowRecomputeWarnMs && !m_SlowRateWarned)
            {
                m_SlowRateWarned = true;
                string reasonName = entry.Reason < ReasonNames.Length ? ReasonNames[entry.Reason] : "?";
                string lattice = SkiRunRater.IsPlanOk(detail.PlanStatus)
                    ? $"lattice {m_RateGrid.Nx}×{m_RateGrid.Nz} = {(long)m_RateGrid.Nx * m_RateGrid.Nz} cells"
                    : $"no lattice (plan {detail.PlanStatus})";
                Mod.Log.Warn($"Catalog [rate] slow rating {ms:F1} ms (above the drawing tooltip's " +
                             $"SlowRecomputeWarnMs={SkiRunDrawFeedbackSystem.Tuning.SlowRecomputeWarnMs} ms): run '{row.Name}' " +
                             $"{entry.Run.Index}:{entry.Run.Version} ({reasonName}{(background ? ", background" : "")}), " +
                             $"{n} node(s), {detail.AreaM2:F0} m², step {detail.StepM:F1} m (m={m}), {lattice}, " +
                             $"plan {detail.PlanMs:F2} ms, {detail.Sampled} height(s) sampled, status {detail.Status} — " +
                             "main thread, one rating per UIUpdate frame, hub open or closed (Round E1); the " +
                             $"backstop re-rates the oldest run every BackstopS={Tuning.BackstopS:0} s open, " +
                             $"ClosedBackstopS={Tuning.ClosedBackstopS:0} s closed; logged once per load");
            }

            // Memory (header MEMORY): a huge lattice is not kept around.
            if ((long)m_RateGrid.Nx * m_RateGrid.Nz > Tuning.ShrinkCells)
            {
                m_RateGrid = new SlopeGrid();
                m_RateResult = new SlopeResult();
            }

            // Patch the row in place (no rebuild): rating fields only; the hash decides the revision.
            ApplyRating(ref row);
            m_Rows[idx] = row;
            ulong hash = ComputeHash(m_Rows, Signs, out int rated);
            RatedCount = rated;
            if (hash != m_LastHash)
            {
                m_LastHash = hash;
                Revision++;
            }
            UpdateFactsRevision();   // E1: the simulation's facts (state, band, heights) of this row

            // The text is formatted only when logged or read (LastRateLine is lazy).
            m_LastRateName = row.Name;
            m_LastRateEntity = row.Entity;
            m_LastRating = rating;
            m_LastRateDetail = detail;
            m_LastRateReason = entry.Reason < ReasonNames.Length ? ReasonNames[entry.Reason] : "?";
            m_LastRateDirty = true;
            if (entry.Reason == ReasonBind || entry.Reason == ReasonEdit)
                LogRateLine(now);
            if (entry.Reason == ReasonWarmup)
                WarmupProgress(ms);
        }

        private static string RateText(string name, Entity run, RunRating r, RateDetail detail, string reason)
        {
            string head = $"run '{name}' {run.Index}:{run.Version}: ";
            if (r.State != RateRated)
                return head + $"not rated — {detail.Status} (plan {detail.PlanStatus}), m={r.StepMultiple}, {r.RateMs:F1} ms ({reason})";
            string band = r.Band >= 0 && r.Band < BandNames.Length ? BandNames[r.Band] : "?";
            return head + $"{band} {r.RatingGrade * 100f:F1} % (steepest {r.SteepestGrade * 100f:F1} %), drop {r.DropM:F0} m, " +
                   $"length {r.LengthM:F0} m, m={r.StepMultiple}, {r.RateMs:F1} ms ({reason})" +
                   (r.Connected ? "" : " — outline broken, P90 fallback");
        }

        /// <summary>Writes LastRateLine as "Catalog [rate] …", at most RateLinesPerWindow per
        /// RateLineWindowS window; suppressed lines are counted and reported with the next logged one.</summary>
        private void LogRateLine(float now)
        {
            if (now - m_RateLineWindowStart >= Tuning.RateLineWindowS)
            {
                m_RateLineWindowStart = now;
                m_RateLinesInWindow = 0;
            }
            if (m_RateLinesInWindow >= Tuning.RateLinesPerWindow)
            {
                m_RateLinesSuppressed++;
                return;
            }
            m_RateLinesInWindow++;
            string suffix = m_RateLinesSuppressed > 0
                ? $" (+{m_RateLinesSuppressed} earlier bind/edit rating(s) not logged: more than {Tuning.RateLinesPerWindow} per {Tuning.RateLineWindowS:0} s)"
                : "";
            m_RateLinesSuppressed = 0;
            Mod.Log.Info("Catalog [rate] " + LastRateLine + suffix);
        }

        private void WarmupProgress(float ms)
        {
            m_WarmupDone++;
            m_WarmupTotalMs += ms;
            if (ms > m_WarmupMaxMs)
                m_WarmupMaxMs = ms;
            if (!m_WarmupLogged && m_WarmupDone >= m_WarmupQueued)
                LogWarmupDone();
        }

        /// <summary>The one "[rate-warmup]" line per load. E1: the factor shown is the panel state's at
        /// the moment of writing (m_WasDemanded = this frame's Demand); the tail says the service is
        /// ready, or that the 90 s deadline had already turned it ready (ServiceTick).</summary>
        private void LogWarmupDone()
        {
            m_WarmupLogged = true;
            float factor = m_WasDemanded ? SkiRunRatingRules.DutyFactorOpen : SkiRunRatingRules.DutyFactorClosed;
            Mod.Log.Info($"Catalog [rate-warmup] {m_WarmupQueued} run(s) in {m_Frame - m_WarmupStartFrame} frames, total " +
                         $"{m_WarmupTotalMs:F1} ms, max {m_WarmupMaxMs:F1} ms (background duty cycle ×{factor:0.##}; " +
                         $"{RatedCount} of {m_Rows.Count} row(s) rated)" +
                         (m_WarmupDeadlineHit ? " — warm-up finished after the deadline" : " — rating service ready"));
        }

        /// <summary>E1 (ch2 §2.3): the service state machine (SkiRunRatingRules.NextServiceState: warm-up
        /// done, or WarmupDeadlineS after the load with one WARN) and the one-time "Catalog [service]"
        /// cost line after ServiceCostLogAfterRebuilds panel-closed rebuilds. Per frame: one call, one
        /// byte compare, one int compare.</summary>
        private void ServiceTick(float now)
        {
            byte next = SkiRunRatingRules.NextServiceState((byte)ServiceState, m_WarmupLogged, now, m_ArmedAt);
            if (next != (byte)ServiceState)
            {
                if (!m_WarmupLogged)
                {
                    m_WarmupDeadlineHit = true;
                    bool heightsReady = HeightsReady(out TerrainHeightData _);
                    Mod.Log.Warn($"Catalog [rate-warmup] WARN: warm-up not finished {SkiRunRatingRules.WarmupDeadlineS:0} s " +
                                 $"after the load ({m_WarmupDone} of {m_WarmupQueued} run(s) rated, {RatingsPending} queued, " +
                                 $"heights ready {(heightsReady ? "true" : "false")}) — the rating service reports ready; ski " +
                                 "areas whose runs are still pending keep their saved value until the attraction model's own " +
                                 "cap (1024 ticks)");
                }
                ServiceState = (RatingServiceState)next;
                RatingRevision++;
            }
            if (!m_ServiceCostLogged && m_CostRebuilds >= Tuning.ServiceCostLogAfterRebuilds)
            {
                // F7 E3 (header COST LINE AND BACKSTOP F7): rebuilds with at least one run only.
                m_ServiceCostLogged = true;
                float spanS = m_CostSince >= 0f ? now - m_CostSince : 0f;
                float rebuildMean = m_CostRebuilds > 0 ? m_CostRebuildMsSum / m_CostRebuilds : 0f;
                float ratingMean = m_ClosedRatings > 0 ? m_ClosedRatingMsSum / m_ClosedRatings : 0f;
                Mod.Log.Info($"Catalog [service] panel closed: {m_CostRebuilds} rebuild(s) with runs in {spanS:F0} s, mean " +
                             $"{rebuildMean:F2} ms, max {m_CostRebuildMsMax:F2} ms ({m_ClosedRebuilds - m_CostRebuilds} of an empty " +
                             $"list not counted); {m_ClosedRatings} rating(s) with the " +
                             $"panel closed, mean {ratingMean:F1} ms, max {m_ClosedRatingMsMax:F1} ms " +
                             $"(ClosedHeartbeatS={Tuning.ClosedHeartbeatS:0.##}, ClosedBackstopS={Tuning.ClosedBackstopS:0.##}, " +
                             $"DutyFactorClosed={SkiRunRatingRules.DutyFactorClosed:0.##}, " +
                             $"ClosedMinGapS={SkiRunRatingRules.ClosedMinGapS:0.##}) — logged once per load");
            }
        }

        // ── names ───────────────────────────────────────────────────────────────────────────────

        private void RefreshTemplates()
        {
            if (m_TemplatesGeneration == m_NamesGeneration && m_Templates[0] != null)
                return;
            m_TemplatesGeneration = m_NamesGeneration;
            m_NameCache.Clear();
            var dictionary = GameManager.instance?.localizationManager?.activeDictionary;
            for (int k = 0; k < m_Templates.Length; k++)
            {
                string template = null;
                if (dictionary != null && dictionary.TryGetValue(DefaultNameKeys[k], out string value) &&
                    !string.IsNullOrEmpty(value))
                    template = value;
                m_Templates[k] = template ?? DefaultNameEnglish[k];
            }
        }

        private string DefaultName(int kind, int ordinal)
        {
            if (kind < 0 || kind >= m_Templates.Length)
                return "?";
            int key = (kind << 16) | (ordinal & 0xFFFF);
            if (m_NameCache.TryGetValue(key, out string cached))
                return cached;
            string number = ordinal.ToString(CultureInfo.InvariantCulture);
            string template = m_Templates[kind];
            string name = template.IndexOf(OrdinalPlaceholder, StringComparison.Ordinal) >= 0
                ? template.Replace(OrdinalPlaceholder, number)
                : template + " " + number;
            m_NameCache[key] = name;
            return name;
        }

        // ── ordering ────────────────────────────────────────────────────────────────────────────

        /// <summary>Top height descending, then centre x, centre z, entity index, version.</summary>
        private static int Spatial(in RunRow a, in RunRow b)
        {
            int c = b.TopY.CompareTo(a.TopY);
            if (c != 0) return c;
            c = a.CenterX.CompareTo(b.CenterX);
            if (c != 0) return c;
            c = a.CenterZ.CompareTo(b.CenterZ);
            if (c != 0) return c;
            c = a.Entity.Index.CompareTo(b.Entity.Index);
            if (c != 0) return c;
            return a.Entity.Version.CompareTo(b.Entity.Version);
        }

        /// <summary>Gate (Index, Version; Null first), kind, then Spatial: the OD-3 numbering order.</summary>
        private sealed class OrdinalOrder : IComparer<RunRow>
        {
            public static readonly OrdinalOrder Instance = new OrdinalOrder();

            public int Compare(RunRow a, RunRow b)
            {
                if (a.Gate.Index != b.Gate.Index)
                    return a.Gate.Index < b.Gate.Index ? -1 : 1;
                if (a.Gate.Version != b.Gate.Version)
                    return a.Gate.Version < b.Gate.Version ? -1 : 1;
                if (a.Kind != b.Kind)
                    return a.Kind < b.Kind ? -1 : 1;
                return Spatial(a, b);
            }
        }

        /// <summary>Linked first, then Spatial, then kind: the order the UI shows.</summary>
        private sealed class DisplayOrder : IComparer<RunRow>
        {
            public static readonly DisplayOrder Instance = new DisplayOrder();

            public int Compare(RunRow a, RunRow b)
            {
                if (a.Bound != b.Bound)
                    return a.Bound ? -1 : 1;
                int c = Spatial(a, b);
                if (c != 0) return c;
                return a.Kind.CompareTo(b.Kind);
            }
        }

        // ── diagnostics ─────────────────────────────────────────────────────────────────────────

        /// <summary>Multi-line description for the hub's DUMP (event time only).</summary>
        public void AppendDump(StringBuilder sb)
        {
            sb.Append("  catalog: armed ").Append(m_Armed)
              .Append(", disabled ").Append(m_Disabled)
              .Append(", demand ").Append(Demand)
              .Append(", revision ").Append(Revision)
              .Append(", rebuilds ").Append(Rebuilds).Append(" (").Append(RebuildsChanged).Append(" changed; by reason:");
            // Round M2 D9 (build-contract.md §6.8): ", rebuilds {n} ({c} changed; by reason: {why} {k}, …)".
            bool anyReason = false;
            foreach (var pair in m_RebuildsByReason)
            {
                sb.Append(anyReason ? ", " : " ").Append(pair.Key).Append(' ').Append(pair.Value);
                anyReason = true;
            }
            if (!anyReason)
                sb.Append(" none");
            sb.Append(')')
              .Append(", last '").Append(LastRebuildWhy).Append("' ").Append(LastRebuildMs.ToString("F2", CultureInfo.InvariantCulture))
              .Append(" ms (max ").Append(MaxRebuildMs.ToString("F2", CultureInfo.InvariantCulture)).Append(')')
              .Append("; signs ").Append(Signs)
              .Append(", runs ").Append(m_Rows.Count).Append(" (").Append(RunsBound).Append(" linked, ")
              .Append(RunsUnbound).Append(" not linked)")
              .Append(", area ").Append(AreaM2.ToString("F0", CultureInfo.InvariantCulture)).Append(" m²\n");
            sb.Append("  ratings: ").Append(RatedCount).Append(" rated of ").Append(m_Rows.Count)
              .Append(", queued ").Append(RatingsPending).Append(" (front ").Append(m_Front.Count).Append(", back ").Append(m_Back.Count).Append(')')
              .Append(", done ").Append(Ratings)
              .Append(", last ").Append(LastRateMs.ToString("F1", CultureInfo.InvariantCulture))
              .Append(" ms (max ").Append(MaxRateMs.ToString("F1", CultureInfo.InvariantCulture)).Append(')')
              .Append(", terrain epoch ").Append(m_TerrainEpoch)
              .Append(", warm-up ").Append(m_WarmupSeeded ? (m_WarmupLogged ? "done" : "running") : "not started")
              .Append(' ').Append(m_WarmupDone).Append('/').Append(m_WarmupQueued)
              .Append(", frame ").Append(m_Frame)
              .Append("; last: ").Append(LastRateLine).Append('\n');
            // E1 (ch2 §2.3): the always-on service.
            sb.Append("  rating service: ").Append(ServiceState)
              .Append(", rating revision ").Append(RatingRevision)
              .Append(", panel ").Append(Demand ? "open" : "closed")
              .Append(", closed rebuilds ").Append(m_ClosedRebuilds)
              .Append(" (mean ").Append((m_ClosedRebuilds > 0 ? m_ClosedRebuildMsSum / m_ClosedRebuilds : 0f).ToString("F2", CultureInfo.InvariantCulture))
              .Append(" ms, max ").Append(m_ClosedRebuildMsMax.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms)")
              .Append(", closed ratings ").Append(m_ClosedRatings)
              .Append(" (mean ").Append((m_ClosedRatings > 0 ? m_ClosedRatingMsSum / m_ClosedRatings : 0f).ToString("F1", CultureInfo.InvariantCulture))
              .Append(" ms, max ").Append(m_ClosedRatingMsMax.ToString("F1", CultureInfo.InvariantCulture)).Append(" ms)")
              .Append(", warm-up deadline ").Append(m_WarmupDeadlineHit ? "hit" : "not hit").Append('\n');
            int shown = Math.Min(m_Rows.Count, Tuning.DumpRowsMax);
            for (int i = 0; i < shown; i++)
            {
                RunRow row = m_Rows[i];
                sb.Append("    ").Append(i + 1).Append(". \"").Append(row.Name).Append('"')
                  .Append(row.CustomName ? " (custom)" : "")
                  .Append(' ').Append(row.Kind >= 0 && row.Kind < KindNames.Length ? KindNames[row.Kind] : "?")
                  .Append(" #").Append(row.Ordinal)
                  .Append(", ").Append(row.Bound ? "sign " + row.Gate.Index + ":" + row.Gate.Version : "NOT linked")
                  .Append(", ").Append(row.RateState == RateRated
                      ? (row.Band >= 0 && row.Band < BandNames.Length ? BandNames[row.Band] : "?") + " " +
                        row.GradePct.ToString("F1", CultureInfo.InvariantCulture) + " % (steepest " +
                        row.SteepestPct.ToString("F1", CultureInfo.InvariantCulture) + " %)"
                      : "rate state " + row.RateState)
                  .Append(", length ").Append(row.LengthM.ToString("F0", CultureInfo.InvariantCulture))
                  .Append(" m, drop ").Append(row.DropM.ToString("F0", CultureInfo.InvariantCulture))
                  .Append(row.Approx ? " m (node estimates)" : " m")
                  .Append(", area ").Append(row.AreaM2.ToString("F0", CultureInfo.InvariantCulture))
                  .Append(" m², top ").Append(row.TopM.ToString("F0", CultureInfo.InvariantCulture))
                  .Append(" m, bottom ").Append(row.BottomM.ToString("F0", CultureInfo.InvariantCulture))
                  .Append(" m, entity ").Append(row.Entity.Index).Append(':').Append(row.Entity.Version).Append('\n');
            }
            if (m_Rows.Count > shown)
                sb.Append("    … ").Append(m_Rows.Count - shown).Append(" more row(s) not listed (DumpRowsMax)\n");
        }

        private void HandleException(Exception e, string where)
        {
            m_Disabled = true;
            if (ServiceState != RatingServiceState.Failed)
            {
                // E1 (ch2 §2.2 "On a failure"): a soft trigger for the attraction model, whose next
                // recompute sees Failed and releases every held area (unrated runs count 0, ch1 I4).
                ServiceState = RatingServiceState.Failed;
                RatingRevision++;
            }
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiRunCatalogSystem)} EXCEPTION in {where} — the run list, resort links and " +
                              "run ratings stop updating until the next game load (the hub keeps the last rows; the rating " +
                              "service reports Failed: the attraction model stops waiting and runs that were not rated " +
                              $"count 0):\n{e}");
            }
        }
    }
}
