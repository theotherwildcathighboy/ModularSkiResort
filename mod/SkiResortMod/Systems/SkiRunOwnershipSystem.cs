// SkiRunOwnershipSystem — links drawn ski runs to their Ski Area Welcome Sign through OUR OWN
// serialized component SkiRunBinding (docs/research/run-binding-glade-spec.md §3.2-§3.4, Round O1;
// DECISIONS 121 DEFECT 1, which retired the entry-90 Owner + SubArea wiring). Created 2026-08
// (entry 90); rewritten 2026-10-01 for O1 (implementer I-1).
//
//   THE GAP (entry 90; amended for Round M1, docs/research/hub-u2-spec.md §6.1 e / OD-12): our run
//   SurfacePrefabs NO LONGER carry Game.Prefabs.ServiceUpgrade — M1 removes it, so runs are not in
//   the sign's upgrade strip any more and are launched from the Ski Resort hub only. Even while
//   they carried it, every downstream upgrade behaviour was Object-gated in vanilla: no owner (a
//   drawn run never gets Owner in game mode, AreaToolSystem.cs:2105-2111 editor-only, 1.6.2
//   :2112-2118), no cost (Temp.m_Cost is written on the object path only), no InstalledUpgrade,
//   no tether. Nothing in this mod ever read ServiceUpgrade on a run (runs are found by prefab
//   NAME, the *SurfaceNames below). This system supplies the link, the one-time bind charge and
//   the tether.
//
//   WHY NOT Owner(sign) ANY MORE (spec §3.1, field 2026-10-01): in game mode the bulldozer walks
//   an owned run's Owner to the sign (Game.Areas/RaycastJobs.cs:80-93, byte-identical in 1.6.2),
//   so bulldozing ONE run deleted the sign and — through the runtime SubArea buffer this system
//   used to add — every run (log 1622-1623, 2038). Removing Owner from runs is necessary and
//   sufficient for both the surface and the border-prop path (Game.Objects/RaycastJobs.cs:355-358).
//   So: NO Owner on runs, NO SubArea buffer on the sign, NO vanilla cascade. The sign→runs
//   cascade (entry 90, kept) is SkiRunGateCascadeSystem, keyed on SkiRunBinding.m_Gate.
//
//   ONE DECISION TABLE (SkiRunBindingRules.Decide, §3.3, pure, tested by tests/BindingBatch) is
//   used by both entry points below, so the load pass and the sweep can never disagree:
//     MigrateLegacy  Owner(our sign, or a dead owner) → drop the owner's SubArea entry FIRST
//                    (once Owner is gone, SubAreaReferencesSystem never removes it — its query is
//                    Owner-gated, Game.Areas/SubAreaReferencesSystem.cs:122-134, identical in
//                    1.6.2), remove Owner, add SkiRunBinding {owner if live else Null, Paid,
//                    cost}. NEVER charges: every Owner(sign) run was charged when the old sweep
//                    added Owner (Charge ran right after AddComponentData(Owner); the
//                    no-PlayerMoney skip was the only exception).
//     LeaveForeign   a LIVE non-sign Owner → untouched, counted (the old HEAL stripped any Owner).
//     KeepBound      binding to a live sign → nothing. A relocated sign keeps its runs at ANY
//                    distance (the tether is checked at bind time only).
//     RebindFree     binding to a dead sign + a sign within TetherM → m_Gate = nearest, no charge.
//     Orphan         binding to a dead sign, none in tether → m_Gate = Null (WARN on the
//                    transition only), FlagPaid kept: a "paid orphan" — unbound for snow, bills
//                    and score, re-links free later (D7). Reached only when our cascade missed
//                    the sign's delete (a "!!!" in SkiRunGateCascadeSystem) or the sign was gone
//                    at save time. NEVER DELETE A RUN ON AN INFERRED EVENT: a broken reference at
//                    load would otherwise wipe the resort.
//     RestoreFree    load pass only: no binding, no Owner, a sign in tether → binding {sign,
//                    Paid|Restored, 0}, no charge (D6: uninstall → save → reinstall re-links free;
//                    residual exploit worth at most one bind fee per run, accepted).
//     BindCharge     sweep only: no binding, no Owner, a sign in tether → binding {sign, Paid,
//                    cost} + Charge(cost), refused (counted unbound, WARN once) before this load's
//                    load pass completed (LoadPassDone).
//     LeaveUnbound   no sign in tether → nothing (unbound runs stay bare, bill nothing, score
//                    nothing: SkiSnowInjectionSystem.IsRunBound → IsBound below).
//
//   LOAD PASS — in OnGameLoadingComplete, after ResolvePrefabs(), before m_Armed = true (§3.4).
//   GameManager invokes onGameLoadingComplete from the load continuation
//   (Game.SceneFlow/GameManager.cs:1126, 1.6.2 :1136) while isGameLoading is still true
//   (:262-272, 1.6.2 :264; WorldReady is set at :1127, 1.6.2 :1137), and GameSystemBase routes it to
//   OnGameLoadingComplete inside a try/catch (Game/GameSystemBase.cs:29, :60-70). No sweep can
//   interleave: GameSimulation ticks zero times while loading (Game.Simulation/
//   SimulationSystem.cs:202-219, identical in 1.6.2), and SkiRunBindTriggerSystem /
//   SkiRunGateCascadeSystem guard isGameLoading. Structural EntityManager changes in that callback
//   have a vanilla precedent (Game.Tutorials/TutorialSystem.cs:301, :313-314, identical in 1.6.2).
//   Steps — COLLECT FIRST, MUTATE AFTER: (1) migrate legacy runs; (2) sign SubArea buffers:
//   remove entries pointing at one of our runs or at a non-existing entity, then remove the
//   buffer itself when it is empty AND the sign's prefab has no Game.Prefabs.SubArea buffer —
//   the vanilla load rebuilder appends every Area+Owner into an EXISTING buffer
//   (Game.Serialization/SubAreaSystem.cs:27-40, :75, identical in 1.6.2) and DestroySystem adds
//   one for rubble (Game.Objects/DestroySystem.cs:146-147, :182-185, identical in 1.6.2), so
//   non-run entries (rubble) are kept; whether the buffer's PRESENCE survives a load is
//   unverified (likely: Game.Areas.SubArea is IEmptySerializable, which ComponentSerializerLibrary
//   maps to EmptyComponentSerializer — type kept, contents dropped), the pass handles both;
//   (3) restore lost links free; (4) NO Updated is added (§3.4: the decal anchor and the height
//   mode re-derive on their own; a one-time decal rotate/shift on migrated runs is expected);
//   (5) publish Last* and LoadPassDone, log ONE "Binding: load pass" line every load, zeros
//   included. The pass NEVER calls Charge.
//   LOAD-PASS FAILURE (fixer round, finding save-safety-2; AMENDS spec §3.4 "a throw … sets
//   m_Disabled"). A run that still carries Owner(sign) keeps the one-run-bulldoze trap (DECISIONS
//   121 DEFECT 1): bulldozing it walks Owner to the sign (Game.Areas/RaycastJobs.cs:59-93) and
//   deletes the sign — and through SkiRunGateCascadeSystem every run bound to it. The old rule
//   (m_Disabled on any throw) also stopped the sweep's non-charging late migration, so one throw
//   left that trap live for the whole session. Now:
//     * each run's migration has its own try/catch (one bad run cannot stop the others; the
//       first failure per load logs "!!!" naming the run and the trap), and steps 2-3 still run;
//     * an exception anywhere in the pass (or any per-run migration failure) sets
//       m_LoadPassFailed and leaves LoadPassDone false: NO CHARGES this load (BindCharge is
//       refused before LoadPassDone, as before) and no RestoreFree (load pass only) — but NOT
//       m_Disabled: the sweep keeps running and executes only the non-charging rows (MigrateLegacy,
//       RebindFree, Orphan; BindCharge refused), plus step 2's sign-buffer cleanup, starting on
//       the FIRST frame after the load, paused too (SkiRunBindTriggerSystem fires SweepNow once
//       on LoadPassRetryPending), else on the next 64-frame sweep;
//     * the sweep keeps its own one-shot "!!!" (a throw there sets m_Disabled as before);
//     * MigrateRun adds the binding BEFORE it removes Owner, so a run interrupted mid-migration
//       carries Owner + binding (row 1 finishes it, no charge), never neither.
//   The cascade keeps working throughout (it reads PrefabSetsReady, not m_Disabled). Saves stay
//   consistent: the next load re-runs the pass (a run left with neither Owner nor binding
//   re-links free there, RestoreFree).
//
//   SWEEP — OnUpdate (GameSimulation, interval 64, anchored before SkiSnowInjectionSystem in
//   Mod.cs) and SweepNow(why) (SkiRunBindTriggerSystem at ModificationEnd, paused or not, on a
//   Created run of ours or a Created sign). First guard: GameManager.isGameLoading. Same table
//   with LoadPass = false; a legacy run found here is migrated with a WARN ("the load pass
//   missed this run", no charge). Census line `Ownership: B bound + U unbound + O orphaned
//   run(s), G gate(s)` (+ foreign, new-bind, re-link and hint suffixes), logged on any change, and
//   while unchanged at most every CensusHeartbeatS (120 s wall clock; Round F6, item D4 — the old
//   every-64th-sweep heartbeat wrote the same line every 17-25 s at 4×, 64 lines in field test 6,
//   log-audit Verification "Missed"). Edits never re-create a run (ApplyAreasSystem.cs:123-131,
//   :167-202: Node copy + Updated on the ORIGINAL entity, which keeps its SkiRunBinding), and
//   Charge() runs only right after the synchronous AddComponentData(SkiRunBinding) of a run
//   that had none, so the trigger and the cadence sweep cannot double-charge.
//   CollectGates/CollectRuns complete their queries' dependencies first: chunk reads through
//   type handles do not wait for running jobs (Entities semantics, not in Game.dll —
//   unverified here); required at ModificationEnd where Modification jobs are in flight.
//
//   DOWNGRADE HAZARD (SkiRunBinding header): a pre-O1 build loads a post-O1 save without error,
//   then re-binds and RE-CHARGES every run. Never open a save made by this build in an older one.
//
//   XP: deliberately none (a redraw would be an XP faucet; prefabs ship m_XPReward 0).
//
//   HELPERS (internal, main thread only): IsArmed (the sweep's state), PrefabSetsReady (sign set +
//   >= 1 run set resolved, independent of m_Disabled — the cascade's and, since the fixer round,
//   the draw feedback's gate), LoadPassDone, LoadPassFailed / BindsNewRuns / LoadPassRetryPending
//   (fixer round, save-safety-2 and r3-plumbing-02),
//   IsRunPrefab, IsGatePrefab, IsLiveGate, IsBound (THE bound test: binding + live sign),
//   TryGetBoundGate, TryNearestGate / TryNearestGateEntity (a managed scan of every building —
//   recompute or transition only, never per frame), RunFeeFor (E1), SweepNow. Temps of a run never
//   carry SkiRunBinding (GenerateAreasSystem.cs:256-273 builds them from the archetype plus
//   Owner/OwnerDefinition only): callers resolve Temp.m_Original before IsBound.
//   Round U1 (docs/research/resort-hub-understand.md §2.B.8, §5.2 item 4; implementer U1-B,
//   2026-10-01): KindOf(prefab) (0 groomed / 1 snowmaking / 2 night / 3 natural since Round E1 /
//   -1 not a run — the hub's
//   RunRow.kind) and BindGeneration, a plain counter bumped on EVERY binding write this system
//   makes (bind, restore, re-link, orphan transition, migration) and once per load. Binding writes
//   raise no Updated tag (they are our own component), so SkiRunCatalogSystem reads this counter
//   as part of its change signature instead. Both are plain field / HashSet reads, main thread.
//   Round U2 (docs/research/hub-u2-spec.md §2.1, 2026-10-03, implementer I-1): TetherMeters, the
//   one tether constant (Tuning.TetherM := TetherMeters) that SkiResortStatsSystem's lift / station
//   resort rule reads too; the flat bind fee fed the hub's build[].price (the player-facing run fee
//   once M1 drops the runs' ServiceUpgrade, header THE GAP).
//   Round E1 (D21, docs/research/econ-round/spec/ch2-runs.md §6; stream S2, 2026-10-04): FEES BY
//   AREA. The one-time fee is RunFeeFor(prefab, plan area) = SkiRunFees.Fee(KindOf(prefab),
//   Geometry.m_SurfaceArea) (SkiRunBindingRules.cs: base + per hectare, rounded half up to ₵10, area
//   capped at 2,000 ha; natural 500 + 250/ha, groomed 2000 + 1000/ha, snowmaking 4000 + 4000/ha, night
//   6000 + 7000/ha — at 2 ha the old flat fees). RunPlan carries AreaM2 from CollectRuns' chunk walk
//   (PlanRun copies it). BindCharge charges it and stores it in SkiRunBinding.m_PaidAmount (layout v1
//   unchanged); the load pass's MigrateLegacy computes it once, prints it and stores it as paid (never
//   charged); reshape: no re-charge, no refund (FlagPaid); delete: no refund. The old flat-fee method
//   is gone (e1-spec R7): the hub reads SkiRunFees.BaseFee / PerHaFee, the drawing tooltip
//   SkiRunFees.Fee — the same function the bind charges.
//
// SCOPE: Game mode only. House contract: "created" line; one ARMED line per load listing every
// constant; m_Armed/m_Disabled/m_ErrorLogged reset in OnGameLoadingComplete; one-shot "!!!" catch
// that disables binding until the next load; no per-frame work (the sweep is every 64 sim frames
// or on a trigger frame), so no per-frame allocation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunOwnershipSystem : GameSystemBase
    {
        /// <summary>U2 (hub-u2-spec.md §2.1): THE tether, one number — the run bind test below
        /// (Tuning.TetherM) and the hub's lift / station resort rule (SkiResortStatsSystem,
        /// SkiResortIndex.TryResolve) both read it.</summary>
        internal const float TetherMeters = 5000f;

        private static class Tuning
        {
            /// <summary>Max distance (m) from a run polygon's center to its sign at BIND time.
            /// The user's number: 5 km covers a full real-scale resort on the 14.3 km playable
            /// map. = TetherMeters (U2: one number for runs and lifts). The run prefabs' old
            /// ServiceUpgrade m_MaxPlacementDistance (5000) was a display-only mirror (header THE
            /// GAP: no vanilla tether for areas) and goes with ServiceUpgrade in Round M1.</summary>
            public const float TetherM = TetherMeters;

            // Round E1 (D21, econ-round spec ch2 §6): the flat bind-cost table (groomed 4000 /
            // snowmaking 12000 / night 20000) is gone. The one-time fee is SkiRunFees.Fee(kind, plan
            // area) (SkiRunBindingRules.cs; base + per hectare, rounded to ₵10); at 2 ha it equals the
            // old flat fee. The hub reads SkiRunFees.BaseFee / PerHaFee for the Build strip.

            /// <summary>Sweep cadence — same 64-frame family as injection; binding
            /// within one window of drawing is imperceptible.</summary>
            public const int SweepIntervalFrames = 64;

            /// <summary>Heartbeat: an UNCHANGED census line is written at most once per this many
            /// wall-clock seconds (F6 D4: was every 64th sweep — 17-25 s apart at 4×); any change
            /// logs at once.</summary>
            public const float CensusHeartbeatS = 120f;

            /// <summary>Per-run lines ([bind]/[rebind]/[orphan]/[migrate]/[restore]) written per
            /// pass before the rest are only counted (the census / load-pass line carries the
            /// totals). Guards the log against a mass event (e.g. a reinstall restoring 200
            /// runs).</summary>
            public const int RunLinesPerPassMax = 16;
        }

        private static readonly string[] GroomedSurfaceNames = { "SkiRunGroomedA" };
        private static readonly string[] SnowmakingSurfaceNames = { "SkiRunSnowmakingA" };
        private static readonly string[] NightSurfaceNames = { "SkiRunNightA" };
        // Round E1 (D16, econ-round spec ch2 §4.4.2): the natural run, RunKind.Natural = 3.
        private static readonly string[] NaturalSurfaceNames = { "SkiRunNaturalA" };
        // T54 probe 1 (cat-track-run.md §2): the cat track, RunKind.CatTrack = 4 (a SurfacePrefab whose AreaType is forged to
        // Space at load by SkiRunAreaTypeSystem so it can cross other runs).
        private static readonly string[] CatTrackSurfaceNames = { "SkiRunCatTrackA" };
        private static readonly string[] GateNames = { "SkiResortGate" };

        /// <summary>One run's collected facts and decision (collect first, mutate after).</summary>
        private struct RunPlan
        {
            public Entity Run;
            public Entity Prefab;
            public BindFacts Facts;
            public BindAction Action;
            public Entity Owner;          // valid when Facts.HasOwner
            public bool OwnerLive;        // the owner is a live sign of ours
            public SkiRunBinding Binding; // valid when Facts.HasBinding
            public int Nearest;           // index into m_Gates, -1 = no sign at all
            public float NearestDist;     // +inf when Nearest < 0
            public float2 Center;         // Geometry.m_CenterPosition.xz (the tether point)
            public float AreaM2;          // E1 (D21): Geometry.m_SurfaceArea (plan area) at collection time — the fee's area
        }

        private SimulationSystem m_SimulationSystem;
        private PrefabSystem m_PrefabSystem;
        private CitySystem m_CitySystem;

        private EntityQuery m_RunAreaQuery;
        private EntityQuery m_GateQuery;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_GroomedPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_SnowmakingPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_NightPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_NaturalPrefabs = new HashSet<Entity>();   // E1 (D16)
        private readonly HashSet<Entity> m_CatTrackPrefabs = new HashSet<Entity>();  // T54 probe 1
        private readonly HashSet<Entity> m_GatePrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;             // session cache, set only once both sets exist

        // Scratch, reused (cleared) every pass — no managed allocation once warmed up.
        private readonly List<Entity> m_Gates = new List<Entity>();
        private readonly List<float2> m_GatePos = new List<float2>();
        private readonly List<RunPlan> m_Plan = new List<RunPlan>();
        private readonly List<Entity> m_ScratchAreas = new List<Entity>();
        private readonly List<Entity> m_ScratchKeep = new List<Entity>();

        // Per-load state — reset at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_CityWarned;
        private bool m_RefuseWarned;
        private bool m_PrefabSetsReady;
        private bool m_LoadPassDone;
        // Fixer round (save-safety-2, header LOAD-PASS FAILURE).
        private bool m_LoadPassFailed;             // the pass threw or a run's migration failed: no charges this load
        private bool m_LoadPassRetryPending;       // one immediate sweep requested (trigger, first frame)
        private bool m_SignBuffersPending;         // step 2 still owed by the sweep after a failed pass
        private bool m_MigrateErrorLogged;         // one "!!!" per load for per-run migration failures
        private int m_MigrateFailures;             // per pass (load pass or sweep)
        private int m_LastMigrateFailures;         // sweep change detector
        private uint m_LastSweepFrame;
        private bool m_SweepFrameValid;
        private float m_LastCensusLineAt = float.NegativeInfinity;   // F6 D4: realtimeSinceStartup of the last census line
        private int m_RunLines;
        private int m_RunLinesSuppressed;
        // M6 / L5 (code review 2026-10-19): the timer sweep of a city with no run of ours and no known sign
        // does nothing; the gate walk then happens at most once per CensusHeartbeatS to keep the sign count
        // (and ResortPresence) honest. m_LastRunCount: runs of ours at the last load pass / sweep, -1 = unknown.
        private int m_LastRunCount = -1;
        private float m_LastGateWalkAt = float.NegativeInfinity;
        private const string TimerSweepWhy = "sweep";

        // Round U1: never reset (a monotonically increasing counter; readers compare for equality).
        private int m_BindGeneration;

        /// <summary>Last census (load pass, then every sweep). -1 until this load's load pass
        /// ran. Plain fields: the glade tool's activation line and the dev tabs read them.</summary>
        public int LastBound = -1, LastUnbound = -1, LastOrphaned = -1, LastForeign = -1, LastGates = -1;

        /// <summary>This load's load-pass census line ("-" before it ran).</summary>
        public string LastLoadPass = "-";

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();

            m_RunAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    // M6a (code review 2026-10-19): all four run kinds are SurfacePrefab areas
                    // (Game.Prefabs/SurfacePrefab.cs:21-23 adds Game.Areas.Surface); lots, districts
                    // and map tiles never enter this walk.
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_GateQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            Mod.Log.Info($"{nameof(SkiRunOwnershipSystem)} created");
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 64;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_CityWarned = false;
            m_RefuseWarned = false;
            m_PrefabSetsReady = false;
            m_LoadPassDone = false;
            m_LoadPassFailed = false;
            m_LoadPassRetryPending = false;
            m_SignBuffersPending = false;
            m_MigrateErrorLogged = false;
            m_MigrateFailures = 0;
            m_LastMigrateFailures = 0;
            m_SweepFrameValid = false;
            m_LastCensusLineAt = float.NegativeInfinity;
            m_LastRunCount = -1;
            m_LastGateWalkAt = float.NegativeInfinity;
            ResortPresence.Reset();   // before this system counts (the census sets its lifts at its first build)
            LastBound = -1;
            LastUnbound = -1;
            LastOrphaned = -1;
            LastForeign = -1;
            LastGates = -1;
            LastLoadPass = "-";
            m_BindGeneration++;   // Round U1: a new city is a new binding state for every reader

            if (mode != GameMode.Game)
                return;

            try
            {
                ResolvePrefabs();
                m_PrefabSetsReady = m_GatePrefabs.Count > 0 &&
                    (m_GroomedPrefabs.Count > 0 || m_SnowmakingPrefabs.Count > 0 ||
                     m_NightPrefabs.Count > 0 || m_NaturalPrefabs.Count > 0);
                if (!m_PrefabSetsReady)
                {
                    Mod.Log.Info("Ownership: sign or run prefabs missing — binding, load pass and " +
                                 "run cascade inert (expected pre-import)");
                    return;
                }

                Mod.Log.Info("Ownership: ARMED — runs carry SkiRunBinding v" +
                             $"{SkiRunBinding.CurrentVersion} (serialized: sign, flags, paid) to the " +
                             $"nearest Ski Area Welcome Sign within {Tuning.TetherM:F0} m of the run's " +
                             "centre at bind time; NO Owner and NO sign SubArea buffer (the bulldozer " +
                             "deletes one run; SkiRunGateCascadeSystem deletes a deleted sign's runs); " +
                             "one-time run fee by plan area (SkiRunFees.Fee, rounded to ₵" +
                             $"{SkiRunFees.RoundTo}): natural {SkiRunFees.NaturalBase} + {SkiRunFees.NaturalPerHa}/ha, " +
                             $"groomed {SkiRunFees.GroomedBase} + {SkiRunFees.GroomedPerHa}/ha, snowmaking " +
                             $"{SkiRunFees.SnowmakingBase} + {SkiRunFees.SnowmakingPerHa}/ha, night " +
                             $"{SkiRunFees.NightBase} + {SkiRunFees.NightPerHa}/ha (area capped at " +
                             $"{SkiRunFees.MaxAreaHa:0} ha), charged at bind, never in the load pass, never twice " +
                             "(FlagPaid), never on a reshape; load pass now: Owner(sign)->binding " +
                             "migration (fee kept), sign SubArea cleanup, free restore of lost links " +
                             $"(D6); sweep every {Tuning.SweepIntervalFrames} sim frames (census line on " +
                             $"change, else every {Tuning.CensusHeartbeatS:0} s wall clock) plus SweepNow on a drawn run or a " +
                             "placed sign (paused too); a run whose sign died without the cascade " +
                             "stays a paid orphan and re-links free (never deleted on an inferred " +
                             "event); a load-pass exception means no charges this load but the sweep " +
                             "keeps migrating legacy runs (no charge) from the first frame; " +
                             $"per-run lines capped at {Tuning.RunLinesPerPassMax} per pass; " +
                             $"prefab sets: {m_GatePrefabs.Count} sign + {m_GroomedPrefabs.Count} " +
                             $"groomed / {m_SnowmakingPrefabs.Count} snowmaking / " +
                             $"{m_NightPrefabs.Count} night / {m_NaturalPrefabs.Count} natural / " +
                             $"{m_CatTrackPrefabs.Count} cat track (groomed fee); " +
                             "DOWNGRADE HAZARD: a save made by this " +
                             "build opened in an older build re-charges every run");

                RunLoadPass();
                // Fixer round (save-safety-2): IsArmed stays TRUE when the pass threw — the sweep
                // must keep migrating; LoadPassDone stays false, so it never charges this load.
                m_Armed = true;
            }
            catch (Exception e)
            {
                m_Disabled = true;
                ResortPresence.MarkUnknown();
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunOwnershipSystem)} EXCEPTION during load " +
                                  $"setup — run binding disabled until the next game load:\n{e}");
                }
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                // First guard (§3.4): nothing binds while a load is in progress.
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;
                if (m_Disabled || !m_Armed)
                    return;

                uint frame = m_SimulationSystem.frameIndex;
                if (m_SweepFrameValid &&
                    frame - m_LastSweepFrame < (uint)Tuning.SweepIntervalFrames)
                    return;
                m_SweepFrameValid = true;
                m_LastSweepFrame = frame;

                Sweep(TimerSweepWhy);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                ResortPresence.MarkUnknown();
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunOwnershipSystem)} EXCEPTION — run " +
                                  "binding disabled until the next game load (the run cascade " +
                                  $"keeps working):\n{e}");
                }
            }
        }

        // ── entry point for SkiRunBindTriggerSystem + helpers ──────────────────────────────

        /// <summary>True while the SWEEP is live this load (prefabs resolved in Game mode, the
        /// load pass ran — completed OR failed, fixer round save-safety-2 — and no sweep
        /// exception has disabled binding). After a failed load pass the sweep runs but never
        /// charges: use BindsNewRuns for "a new run would bind". Plain field read.</summary>
        internal bool IsArmed => m_Armed && !m_Disabled;

        /// <summary>Sign set + at least one run set resolved this load (Game mode).
        /// Independent of m_Disabled: SkiRunGateCascadeSystem gates on this, so an exception
        /// in the sweep or the load pass never switches the cascade off.</summary>
        internal bool PrefabSetsReady => m_PrefabSetsReady;

        /// <summary>This load's load pass completed. BindCharge is refused before it.</summary>
        internal bool LoadPassDone => m_LoadPassDone;

        /// <summary>Fixer round (save-safety-2): this load's load pass threw or a run's migration
        /// failed. The sweep still runs (migration, re-link, orphan) but never charges.</summary>
        internal bool LoadPassFailed => m_LoadPassFailed;

        /// <summary>Fixer round (save-safety-2): a NEW run drawn now would be bound (and charged)
        /// by this load's sweep: armed, not disabled, load pass completed. False after any
        /// ownership exception this load. SkiRunDrawFeedbackSystem's new-run warning reads it.</summary>
        internal bool BindsNewRuns => m_Armed && !m_Disabled && m_LoadPassDone;

        /// <summary>Fixer round (save-safety-2): after a failed load pass, one immediate sweep is
        /// owed so legacy runs are migrated on the first frame (paused too).
        /// SkiRunBindTriggerSystem (ModificationEnd, every frame) reads this and calls SweepNow;
        /// any sweep clears it.</summary>
        internal bool LoadPassRetryPending => m_LoadPassRetryPending && m_Armed && !m_Disabled;

        /// <summary>True for one of our four run SurfacePrefab entities (natural since Round E1).</summary>
        internal bool IsRunPrefab(Entity prefab)
        {
            return m_GroomedPrefabs.Contains(prefab) ||
                   m_SnowmakingPrefabs.Contains(prefab) ||
                   m_NightPrefabs.Contains(prefab) ||
                   m_NaturalPrefabs.Contains(prefab) ||
                   m_CatTrackPrefabs.Contains(prefab);
        }

        /// <summary>True for the Ski Area Welcome Sign BuildingPrefab entity.</summary>
        internal bool IsGatePrefab(Entity prefab)
        {
            return m_GatePrefabs.Contains(prefab);
        }

        /// <summary>Round U1 (hub RunRow.kind, mod/SkiResortUI/src/bindings.ts RUN_KIND): 0 groomed,
        /// 1 snowmaking, 2 night, 3 natural (Round E1, = RunKind); -1 when <paramref name="prefab"/>
        /// is not one of our run prefabs (or the sets are not resolved yet this load). At most four
        /// HashSet reads, main thread.</summary>
        internal int KindOf(Entity prefab)
        {
            if (m_GroomedPrefabs.Contains(prefab))
                return 0;
            if (m_SnowmakingPrefabs.Contains(prefab))
                return 1;
            if (m_NightPrefabs.Contains(prefab))
                return 2;
            if (m_NaturalPrefabs.Contains(prefab))
                return (int)RunKind.Natural;
            if (m_CatTrackPrefabs.Contains(prefab))
                return (int)RunKind.CatTrack;
            return -1;
        }

        /// <summary>Round U1: bumped on every SkiRunBinding write of this system (bind with charge,
        /// free restore, re-link, orphan transition, migration) and once per load. Binding writes
        /// raise no tag, so the hub's run catalog compares this counter instead. Plain field read.</summary>
        internal int BindGeneration => m_BindGeneration;

        /// <summary>A live Ski Area Welcome Sign: not Null, exists, not Deleted, PrefabRef in the
        /// sign set. A Destroyed (disaster) sign is still live (D5: Rebuild keeps the entity).
        /// Main thread only.</summary>
        internal bool IsLiveGate(Entity gate)
        {
            return gate != Entity.Null &&
                   m_GatePrefabs.Count > 0 &&
                   EntityManager.Exists(gate) &&
                   !EntityManager.HasComponent<Deleted>(gate) &&
                   EntityManager.TryGetComponent(gate, out PrefabRef prefabRef) &&
                   m_GatePrefabs.Contains(prefabRef.m_Prefab);
        }

        /// <summary>THE bound test (§2): the run carries SkiRunBinding and its sign is live. A
        /// paid orphan (m_Gate Null or dead) is NOT bound. Temps never carry the binding —
        /// resolve Temp.m_Original first. Main thread only.</summary>
        internal bool IsBound(Entity run)
        {
            return EntityManager.TryGetComponent(run, out SkiRunBinding binding) &&
                   IsLiveGate(binding.m_Gate);
        }

        /// <summary>IsBound, returning the sign. gate = Entity.Null when false.</summary>
        internal bool TryGetBoundGate(Entity run, out Entity gate)
        {
            if (EntityManager.TryGetComponent(run, out SkiRunBinding binding) &&
                IsLiveGate(binding.m_Gate))
            {
                gate = binding.m_Gate;
                return true;
            }
            gate = Entity.Null;
            return false;
        }

        /// <summary>Immediate sweep, outside the 64-frame cadence (SkiRunBindTriggerSystem,
        /// ModificationEnd). Same guards and the same one-shot disable path as OnUpdate;
        /// deliberately leaves m_LastSweepFrame / m_SweepFrameValid alone so the cadence
        /// sweep keeps its own schedule. Main thread only.</summary>
        internal void SweepNow(string why)
        {
            try
            {
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;
                if (m_Disabled || !m_Armed)
                    return;

                Sweep(why);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                ResortPresence.MarkUnknown();
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunOwnershipSystem)} EXCEPTION — run " +
                                  "binding disabled until the next game load (the run cascade " +
                                  $"keeps working):\n{e}");
                }
            }
        }

        /// <summary>Nearest live Ski Area Welcome Sign to <paramref name="xz"/>: returns true
        /// when it lies within Tuning.TetherM (the same `d &lt;= TetherM` test the bind uses).
        /// <paramref name="distance"/> is the nearest sign's distance, or
        /// float.PositiveInfinity when there is none. A managed scan of m_GateQuery, which
        /// matches EVERY building (only the PrefabRef filter narrows it to signs), so call it
        /// on recompute or transition only, never per frame. Main thread only; never throws
        /// for a missing sign set (pre-import → false).</summary>
        internal bool TryNearestGate(float2 xz, out float distance)
        {
            return TryNearestGateEntity(xz, out Entity _, out distance);
        }

        /// <summary>TryNearestGate, also returning the nearest sign (Entity.Null when there is
        /// none at any distance; set even when it lies beyond the tether).</summary>
        internal bool TryNearestGateEntity(float2 xz, out Entity gate, out float distance)
        {
            gate = Entity.Null;
            distance = float.PositiveInfinity;
            if (m_GatePrefabs.Count == 0 || m_GateQuery.IsEmptyIgnoreFilter)
                return false;

            // The chunk reads below do not wait for running jobs on their own (Entities
            // semantics, unverified here) — complete the writers first.
            m_GateQuery.CompleteDependency();
            var entityManager = EntityManager;
            var entityHandle = GetEntityTypeHandle();
            var prefabRefHandle = entityManager.GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            var transformHandle =
                entityManager.GetComponentTypeHandle<Game.Objects.Transform>(isReadOnly: true);
            using (var gateChunks = m_GateQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < gateChunks.Length; c++)
                {
                    var entities = gateChunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = gateChunks[c].GetNativeArray(ref prefabRefHandle);
                    var transforms = gateChunks[c].GetNativeArray(ref transformHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (!m_GatePrefabs.Contains(prefabRefs[i].m_Prefab))
                            continue;
                        float d = math.distance(xz, transforms[i].m_Position.xz);
                        if (d < distance)
                        {
                            distance = d;
                            gate = entities[i];
                        }
                    }
                }
            }
            return distance <= Tuning.TetherM;
        }

        /// <summary>E1 (D21): the one-time fee of a run of this prefab and plan area (SkiRunFees.Fee);
        /// 0 for a prefab that is not one of our runs.</summary>
        internal int RunFeeFor(Entity runPrefab, float areaM2)
        {
            int kind = KindOf(runPrefab);
            return kind < 0 ? 0 : SkiRunFees.Fee(kind, areaM2);
        }

        // Second pass (e1-spec R7/R14, no dead code): RunFeeBase, RunFeePerHa and the old flat-fee
        // method are NOT kept. The hub reads SkiRunFees.BaseFee / PerHaFee directly (ch5 §4.5); the
        // bind line below calls SkiRunFees.BaseFee(KindOf(plan.Prefab)) / PerHaFee(...) inline.

        // ── the load pass ─────────────────────────────────────────────────────────────────

        private void RunLoadPass()
        {
            try
            {
                LoadPass();
                // Per-run failures were caught inside LoadPass (its line counts them): the state
                // is understood, but stay conservative — no charges this load.
                if (m_MigrateFailures > 0)
                    LoadPassFailedMode();
                else
                    m_LoadPassDone = true;
            }
            catch (Exception e)
            {
                // Fixer round (save-safety-2, header LOAD-PASS FAILURE): NOT m_Disabled.
                LoadPassFailedMode();
                m_SignBuffersPending = true;
                LastLoadPass = "FAILED — see the !!! line; no charges and no new binds this load; the sweep " +
                               "keeps migrating legacy runs (no charge) from the first frame";
                // Its own one-shot (the load pass runs once per load); m_ErrorLogged stays free for
                // the sweep's own "!!!".
                Mod.Log.Error($"!!! {nameof(SkiRunOwnershipSystem)} EXCEPTION in the load pass — no charges " +
                              "and no new binds this load (SkiRunGateCascadeSystem keeps working). The sweep " +
                              "still migrates runs left carrying Owner(sign) — no charge — on the first frame " +
                              "(paused too) and every 64 frames; until a run is migrated it keeps the " +
                              "one-run-bulldoze trap: bulldozing it deletes its Welcome Sign and, through the " +
                              "cascade, every run bound to that sign. Runs already migrated stay consistent; a " +
                              "run left with neither Owner nor binding re-links free at the next load:\n" + e);
            }
        }

        /// <summary>Fixer round (save-safety-2): the degraded mode after a load-pass failure.
        /// LoadPassDone stays false (BindCharge refused, the sweep never charges); the sweep keeps
        /// running and one immediate sweep is requested from the trigger.</summary>
        private void LoadPassFailedMode()
        {
            m_LoadPassFailed = true;
            m_LoadPassDone = false;
            m_LoadPassRetryPending = true;
        }

        /// <summary>Fixer round (save-safety-2): MigrateRun under its own try/catch, so one bad
        /// run cannot stop the others. On a throw: counted in m_MigrateFailures, the first one per
        /// load logs "!!!" naming the run and the trap it keeps; returns false. E1 (ch2 §6.2):
        /// <paramref name="fee"/> = RunFeeFor(plan.Prefab, plan.AreaM2), computed ONCE by the caller —
        /// stored as the binding's paid amount (never charged) and printed by the load pass's line.</summary>
        private bool TryMigrateRun(in RunPlan plan, int fee, string context, out int entries)
        {
            entries = 0;
            try
            {
                entries = MigrateRun(plan, fee);
                return true;
            }
            catch (Exception e)
            {
                m_MigrateFailures++;
                if (!m_MigrateErrorLogged)
                {
                    m_MigrateErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunOwnershipSystem)} EXCEPTION migrating run " +
                                  $"{RunLabel(plan.Run, plan.Prefab)} ({context}) — skipped, the other runs " +
                                  $"continue; it may still carry Owner({EntityLabel(plan.Owner)}): until migrated, " +
                                  "bulldozing it deletes that sign and every run bound to it. No charges this load; " +
                                  "every sweep retries it (further migration failures this load are only counted):\n" + e);
                }
                return false;
            }
        }

        /// <summary>§3.4 steps 1-5. Collect first, mutate after; never charges.</summary>
        private void LoadPass()
        {
            var timer = Stopwatch.StartNew();
            m_RunLines = 0;
            m_RunLinesSuppressed = 0;
            m_MigrateFailures = 0;

            CollectGates();
            CollectRuns(loadPass: true);

            int migrated = 0, migratedDead = 0, foreign = 0, restored = 0, rebound = 0;
            int bound = 0, orphaned = 0, unbound = 0, entries = 0, buffers = 0, unexpected = 0;

            // (1) Migrate — strictly before the sign-buffer step and the other actions. Each run
            // under its own try/catch (fixer round, save-safety-2): a failed run is counted and
            // left for the sweep; the others and steps 2-3 continue.
            for (int p = 0; p < m_Plan.Count; p++)
            {
                RunPlan plan = m_Plan[p];
                if (plan.Action != BindAction.MigrateLegacy)
                    continue;
                int fee = RunFeeFor(plan.Prefab, plan.AreaM2);   // E1: once — stored and printed
                if (!TryMigrateRun(plan, fee, "load pass", out int removedEntries))
                    continue;
                entries += removedEntries;
                migrated++;
                bool live = plan.Facts.HasBinding ? IsLiveGate(plan.Binding.m_Gate) : plan.OwnerLive;
                if (live)
                    bound++;
                else
                {
                    orphaned++;
                    migratedDead++;
                }
                if (RunLine())
                {
                    Mod.Log.Info($"Ownership [migrate] (load): run {RunLabel(plan.Run, plan.Prefab)} " +
                                 $"Owner({EntityLabel(plan.Owner)}) -> " +
                                 (plan.Facts.HasBinding
                                     ? "Owner removed, existing binding kept (half-migrated)"
                                     : (plan.OwnerLive
                                         ? $"binding to that sign, fee {fee} kept as paid"
                                         : $"paid orphan (sign dead), fee {fee} kept as paid")) +
                                 " — no charge");
                }
            }

            // (2) Sign SubArea buffers.
            for (int g = 0; g < m_Gates.Count; g++)
                entries += CleanSignBuffer(m_Gates[g], ref buffers);

            // (3) Restore, re-link, orphan — and the census of everything else.
            for (int p = 0; p < m_Plan.Count; p++)
            {
                RunPlan plan = m_Plan[p];
                switch (plan.Action)
                {
                    case BindAction.MigrateLegacy:
                        break;                                   // done and counted in (1)
                    case BindAction.LeaveForeign:
                        foreign++;
                        break;
                    case BindAction.KeepBound:
                        bound++;
                        break;
                    case BindAction.RebindFree:
                        Rebind(plan, "load");
                        rebound++;
                        bound++;
                        break;
                    case BindAction.Orphan:
                        Orphan(plan, "load");
                        orphaned++;
                        break;
                    case BindAction.RestoreFree:
                    {
                        Entity sign = m_Gates[plan.Nearest];
                        EntityManager.AddComponentData(plan.Run, new SkiRunBinding(sign,
                            (byte)(SkiRunBinding.FlagPaid | SkiRunBinding.FlagRestored), 0));
                        m_BindGeneration++;
                        restored++;
                        bound++;
                        if (RunLine())
                        {
                            Mod.Log.Info($"Ownership [restore] (load): run {RunLabel(plan.Run, plan.Prefab)} " +
                                         $"linked to sign {EntityLabel(sign)} {plan.NearestDist:F0} m away " +
                                         "— free (lost link restored, D6)");
                        }
                        break;
                    }
                    case BindAction.LeaveUnbound:
                        unbound++;
                        break;
                    default:
                        // BindCharge is impossible with LoadPass = true (§3.3 row 6 precedes row 7);
                        // never charge in the load pass, whatever the table says.
                        unbound++;
                        unexpected++;
                        Mod.Log.Warn($"Binding: load pass — unexpected action {plan.Action} for run " +
                                     $"{RunLabel(plan.Run, plan.Prefab)}; left unbound, NOT charged");
                        break;
                }
            }

            timer.Stop();
            LastBound = bound;
            LastUnbound = unbound;
            LastOrphaned = orphaned;
            LastForeign = foreign;
            LastGates = m_Gates.Count;
            m_LastRunCount = m_Plan.Count;
            m_LastGateWalkAt = UnityEngine.Time.realtimeSinceStartup;
            ResortPresence.SetSignsRuns(m_Gates.Count, m_Plan.Count);

            LastLoadPass = $"{migrated} migrated Owner(sign)→binding (fee kept, no charge), " +
                           $"{migratedDead} of them with a dead sign → paid orphan, {foreign} " +
                           $"foreign-owned left, {restored} restored free, {rebound} paid orphan(s) " +
                           $"re-linked free; sign SubArea: {entries} entr(ies) / {buffers} buffer(s) " +
                           $"removed; now {bound} bound / {orphaned} orphaned / {unbound} unbound " +
                           $"across {m_Gates.Count} sign(s); {timer.Elapsed.TotalMilliseconds:F1} ms" +
                           (unexpected > 0 ? $"; {unexpected} UNEXPECTED action(s), see WARN" : "") +
                           (m_MigrateFailures > 0
                               ? $"; {m_MigrateFailures} MIGRATION FAILURE(S) (see !!!) — those runs may keep " +
                                 "Owner(sign) and the one-run-bulldoze trap until the sweep migrates them; no " +
                                 "charges this load"
                               : "");
            Mod.Log.Info($"Binding: load pass — {LastLoadPass}");
            FlushSuppressedRunLines();
        }

        /// <summary>§3.4 step 1 / sweep row 1 (shared). (a) remove the owner's SubArea entries
        /// for this run BEFORE (b) removing Owner — once Owner is gone, SubAreaReferencesSystem
        /// (Owner-gated, :122-134) never removes them; (c) add the binding only when the run has
        /// none (a half-migrated run gets (a)+(b)). ORDER (fixer round, save-safety-2; the spec
        /// lists (b) before (c)): (a), (c), (b) — a throw between the two structural changes then
        /// leaves Owner + binding (row 1 finishes it, no charge), never a run with neither (which
        /// the sweep would treat as a NEW run to bind and charge once LoadPassDone).
        /// Never charges. Returns entries removed. E1: the binding stores <paramref name="fee"/> (the
        /// area fee, RunFeeFor) as its paid amount.</summary>
        private int MigrateRun(in RunPlan plan, int fee)
        {
            int removed = 0;
            Entity owner = plan.Owner;
            if (owner != Entity.Null && EntityManager.Exists(owner) &&
                EntityManager.HasBuffer<Game.Areas.SubArea>(owner))
            {
                DynamicBuffer<Game.Areas.SubArea> buffer =
                    EntityManager.GetBuffer<Game.Areas.SubArea>(owner);
                for (int i = buffer.Length - 1; i >= 0; i--)
                {
                    if (buffer[i].m_Area == plan.Run)
                    {
                        buffer.RemoveAt(i);
                        removed++;
                    }
                }
            }
            // (c) before (b) (summary). The buffer is not touched after these structural changes.
            if (!plan.Facts.HasBinding)
            {
                EntityManager.AddComponentData(plan.Run, new SkiRunBinding(
                    plan.OwnerLive ? owner : Entity.Null, SkiRunBinding.FlagPaid, fee));
                m_BindGeneration++;
            }
            EntityManager.RemoveComponent<Owner>(plan.Run);
            m_BindGeneration++;
            return removed;
        }

        /// <summary>§3.4 step 2 for one live sign: drop entries that point at one of our runs or
        /// at a non-existing entity (rubble and any other live non-run entry is kept); remove
        /// the buffer when it is then empty and the sign's PREFAB has no Game.Prefabs.SubArea
        /// buffer (runtime test). Collect first (entries copied out), mutate after. Returns
        /// entries removed; counts removed buffers.</summary>
        private int CleanSignBuffer(Entity sign, ref int buffersRemoved)
        {
            if (!EntityManager.HasBuffer<Game.Areas.SubArea>(sign))
                return 0;

            m_ScratchAreas.Clear();
            m_ScratchKeep.Clear();
            DynamicBuffer<Game.Areas.SubArea> read = EntityManager.GetBuffer<Game.Areas.SubArea>(sign, isReadOnly: true);
            for (int i = 0; i < read.Length; i++)
                m_ScratchAreas.Add(read[i].m_Area);

            int removed = 0;
            for (int i = 0; i < m_ScratchAreas.Count; i++)
            {
                Entity area = m_ScratchAreas[i];
                bool dangling = !EntityManager.Exists(area);
                bool ours = !dangling && EntityManager.TryGetComponent(area, out PrefabRef areaPrefab) &&
                            IsRunPrefab(areaPrefab.m_Prefab);
                if (dangling || ours)
                    removed++;
                else
                    m_ScratchKeep.Add(area);
            }

            if (removed > 0)
            {
                DynamicBuffer<Game.Areas.SubArea> write = EntityManager.GetBuffer<Game.Areas.SubArea>(sign);
                write.Clear();
                for (int i = 0; i < m_ScratchKeep.Count; i++)
                    write.Add(new Game.Areas.SubArea(m_ScratchKeep[i]));
            }

            if (m_ScratchKeep.Count == 0 &&
                EntityManager.TryGetComponent(sign, out PrefabRef signPrefab) &&
                !EntityManager.HasBuffer<Game.Prefabs.SubArea>(signPrefab.m_Prefab))
            {
                EntityManager.RemoveComponent<Game.Areas.SubArea>(sign);
                buffersRemoved++;
            }
            m_ScratchAreas.Clear();
            m_ScratchKeep.Clear();
            return removed;
        }

        // ── the sweep ───────────────────────────────────────────────────────────────────

        private void Sweep(string why)
        {
            // M6 / L5: a timer sweep in a city with no run of ours and no known sign has nothing to bind,
            // migrate or count; it only walks the buildings (to keep the sign count honest) once per
            // CensusHeartbeatS. Event sweeps (a drawn run, a placed sign, the load-pass retry) and any
            // city with a run always take the full path.
            float t0 = UnityEngine.Time.realtimeSinceStartup;
            if (why == TimerSweepWhy && m_LastRunCount == 0 && !AnyRunOfOurs() &&
                t0 - m_LastGateWalkAt < Tuning.CensusHeartbeatS)
                return;
            m_LastGateWalkAt = t0;
            var sweepTimer = Stopwatch.StartNew();

            m_RunLines = 0;
            m_RunLinesSuppressed = 0;
            m_MigrateFailures = 0;
            m_LoadPassRetryPending = false;   // any sweep serves the post-failure retry (save-safety-2)

            CollectGates();
            CollectRuns(loadPass: false);

            int bound = 0, unbound = 0, orphaned = 0, foreign = 0;
            int newBinds = 0, rebound = 0, newOrphans = 0, migrated = 0, refused = 0, unexpected = 0;
            int signEntries = 0, signBuffers = 0;
            long chargedThisSweep = 0;
            for (int p = 0; p < m_Plan.Count; p++)
            {
                RunPlan plan = m_Plan[p];
                switch (plan.Action)
                {
                    case BindAction.MigrateLegacy:
                    {
                        // Own try/catch per run (fixer round, save-safety-2): a run that fails is
                        // counted (census suffix) and retried next sweep; the others continue.
                        // E1: the area fee is stored as paid (never charged; this line prints none).
                        if (!TryMigrateRun(plan, RunFeeFor(plan.Prefab, plan.AreaM2), why, out int _))
                            break;
                        migrated++;
                        bool live = plan.Facts.HasBinding ? IsLiveGate(plan.Binding.m_Gate) : plan.OwnerLive;
                        if (live)
                            bound++;
                        else
                            orphaned++;
                        if (RunLine())
                        {
                            Mod.Log.Warn($"Ownership [migrate] ({why}): run {RunLabel(plan.Run, plan.Prefab)} " +
                                         $"still carried Owner({EntityLabel(plan.Owner)}) — late: " +
                                         (m_LoadPassFailed
                                             ? "this load's load pass failed before finishing it (see !!!)"
                                             : "the load pass missed this run") +
                                         "; migrated to " + (live ? "a binding" : "a paid orphan") + ", no charge");
                        }
                        break;
                    }
                    case BindAction.LeaveForeign:
                        foreign++;
                        break;
                    case BindAction.KeepBound:
                        bound++;
                        break;
                    case BindAction.RebindFree:
                        Rebind(plan, why);
                        rebound++;
                        bound++;
                        break;
                    case BindAction.Orphan:
                        if (Orphan(plan, why))
                            newOrphans++;
                        orphaned++;
                        break;
                    case BindAction.BindCharge:
                    {
                        if (!m_LoadPassDone)
                        {
                            // The sweep is not armed before the load pass; since the fixer round it
                            // DOES run after a failed one (save-safety-2), and this refusal is what
                            // keeps it from charging this load.
                            unbound++;
                            refused++;
                            if (!m_RefuseWarned)
                            {
                                m_RefuseWarned = true;
                                Mod.Log.Warn($"Ownership ({why}): bind refused — this load's load " +
                                             (m_LoadPassFailed
                                                 ? "pass FAILED (see !!!): no new binds or charges this load (a run " +
                                                   "drawn now is linked free by the next load's restore, D6); "
                                                 : "pass has not completed; ") +
                                             "run(s) counted unbound, NOT charged (warned once per load)");
                            }
                            break;
                        }
                        Entity sign = m_Gates[plan.Nearest];
                        // E1 (D21): the area fee, SkiRunFees.Fee(kind, plan area) — the same function
                        // the drawing tooltip shows (SkiRunTooltipSystem).
                        int cost = RunFeeFor(plan.Prefab, plan.AreaM2);
                        // Binding first, synchronously; Charge right after — the only Charge call.
                        EntityManager.AddComponentData(plan.Run,
                            new SkiRunBinding(sign, SkiRunBinding.FlagPaid, cost));
                        m_BindGeneration++;
                        Charge(cost);
                        chargedThisSweep += cost;
                        newBinds++;
                        bound++;
                        if (RunLine())
                        {
                            int kind = KindOf(plan.Prefab);
                            Mod.Log.Info($"Ownership [bind] ({why}): run {RunLabel(plan.Run, plan.Prefab)} " +
                                         $"bound to sign {EntityLabel(sign)} {plan.NearestDist:F0} m away " +
                                         $"— charged {cost} (one-time: {SkiRunFees.BaseFee(kind)} + " +
                                         $"{SkiRunFees.PerHaFee(kind)}/ha × {plan.AreaM2 / 10000f:F2} ha)");
                        }
                        break;
                    }
                    case BindAction.LeaveUnbound:
                        unbound++;
                        break;
                    default:
                        // RestoreFree is impossible with LoadPass = false (§3.3 row 6).
                        unbound++;
                        unexpected++;
                        Mod.Log.Warn($"Ownership ({why}): unexpected action {plan.Action} for run " +
                                     $"{RunLabel(plan.Run, plan.Prefab)}; left as is, NOT charged");
                        break;
                }
            }

            // §3.4 step 2, owed by the sweep after a load pass that threw (fixer round,
            // save-safety-2): drop sign SubArea entries that point at our runs or at dead
            // entities, and the empty buffer. Only once the step completes (a throw propagates to
            // the caller's one-shot "!!!" like any sweep exception).
            if (m_SignBuffersPending)
            {
                for (int g = 0; g < m_Gates.Count; g++)
                    signEntries += CleanSignBuffer(m_Gates[g], ref signBuffers);
                m_SignBuffersPending = false;
            }

            int gates = m_Gates.Count;
            int migrateFailures = m_MigrateFailures;
            // F6 D4 (header SWEEP): on any change at once; unchanged, a CensusHeartbeatS heartbeat.
            float now = UnityEngine.Time.realtimeSinceStartup;
            m_LastRunCount = m_Plan.Count;
            ResortPresence.SetSignsRuns(gates, m_Plan.Count);
            // L5: an unchanged census with everything at zero is not proof of life worth a line every 120 s.
            bool allZero = bound == 0 && unbound == 0 && orphaned == 0 && foreign == 0 && gates == 0;
            bool log = bound != LastBound || unbound != LastUnbound || orphaned != LastOrphaned ||
                       foreign != LastForeign || gates != LastGates || newBinds > 0 ||
                       rebound > 0 || newOrphans > 0 || migrated > 0 || refused > 0 ||
                       unexpected > 0 || signEntries > 0 || signBuffers > 0 ||
                       migrateFailures != m_LastMigrateFailures ||
                       (!allZero && !(now - m_LastCensusLineAt < Tuning.CensusHeartbeatS));
            m_LastMigrateFailures = migrateFailures;
            if (log)
            {
                m_LastCensusLineAt = now;
                Mod.Log.Info($"Ownership: {bound} bound + {unbound} unbound + {orphaned} orphaned " +
                             $"run(s), {gates} gate(s)" +
                             (foreign > 0 ? $", {foreign} foreign-owned" : "") +
                             (newBinds > 0 ? $", {newBinds} new bind(s) -{chargedThisSweep}" : "") +
                             (rebound > 0 ? $", {rebound} paid orphan(s) re-linked free" : "") +
                             (newOrphans > 0 ? $", {newOrphans} new paid orphan(s)" : "") +
                             (migrated > 0 ? $", {migrated} late migration(s)" : "") +
                             (migrateFailures > 0
                                 ? $", {migrateFailures} MIGRATION FAILURE(S) (still Owner(sign): one-run-bulldoze trap)"
                                 : "") +
                             (signEntries > 0 || signBuffers > 0
                                 ? $", sign SubArea {signEntries} entr(ies) / {signBuffers} buffer(s) removed"
                                 : "") +
                             (refused > 0
                                 ? (m_LoadPassFailed
                                     ? $", {refused} bind(s) refused (load pass FAILED this load)"
                                     : $", {refused} bind(s) refused before the load pass")
                                 : "") +
                             (m_LoadPassFailed ? " — LOAD PASS FAILED this load: migrate / re-link only, no charges" : "") +
                             (unbound > 0 && gates == 0
                                 ? " — unbound runs stay bare until a Ski Area Welcome " +
                                   $"Sign is placed within {Tuning.TetherM:F0} m"
                                 : "") +
                             (orphaned > 0 && gates == 0
                                 ? " — paid orphans re-link free when a sign is placed within " +
                                   $"{Tuning.TetherM:F0} m"
                                 : "") +
                             $" [{sweepTimer.Elapsed.TotalMilliseconds:F2} ms]");
            }
            LastBound = bound;
            LastUnbound = unbound;
            LastOrphaned = orphaned;
            LastForeign = foreign;
            LastGates = gates;
            FlushSuppressedRunLines();
        }

        /// <summary>§3.3 row 4: m_Gate = the nearest sign in tether; flags and paid amount kept;
        /// no charge.</summary>
        private void Rebind(in RunPlan plan, string why)
        {
            Entity sign = m_Gates[plan.Nearest];
            SkiRunBinding binding = plan.Binding;
            Entity old = binding.m_Gate;
            binding.m_Gate = sign;
            EntityManager.SetComponentData(plan.Run, binding);
            m_BindGeneration++;
            if (RunLine())
            {
                Mod.Log.Info($"Ownership [rebind] ({why}): run {RunLabel(plan.Run, plan.Prefab)} " +
                             $"re-linked to sign {EntityLabel(sign)} {plan.NearestDist:F0} m away " +
                             $"(was {(old == Entity.Null ? "none" : EntityLabel(old))}) — no charge " +
                             "(fee already paid)");
            }
        }

        /// <summary>§3.3 row 5: m_Gate = Null when it was not (the transition, WARN once per
        /// run); flags and paid amount kept. Returns true on the transition.</summary>
        private bool Orphan(in RunPlan plan, string why)
        {
            if (plan.Facts.GateNull)
                return false;                        // already a paid orphan: nothing to write
            SkiRunBinding binding = plan.Binding;
            Entity old = binding.m_Gate;
            binding.m_Gate = Entity.Null;
            EntityManager.SetComponentData(plan.Run, binding);
            m_BindGeneration++;
            if (RunLine())
            {
                Mod.Log.Warn($"Ownership [orphan] ({why}): run {RunLabel(plan.Run, plan.Prefab)} — " +
                             $"sign {EntityLabel(old)} gone without the cascade; kept as a paid " +
                             $"orphan (no snow, bills or score; re-links free to a sign within " +
                             $"{Tuning.TetherM:F0} m)");
            }
            return true;
        }

        // ── collection (read-only; no structural change happens while chunks are held) ──

        /// <summary>M6: any run of ours in the world. Empty query = no sync; otherwise one chunk scan of the
        /// Surface areas (a handful in a vanilla city). Main thread.</summary>
        private bool AnyRunOfOurs()
        {
            if (m_RunAreaQuery.IsEmptyIgnoreFilter)
                return false;
            m_RunAreaQuery.CompleteDependency();
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            using (var chunks = m_RunAreaQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (IsRunPrefab(prefabRefs[i].m_Prefab))
                            return true;
                    }
                }
            }
            return false;
        }

        private void CollectGates()
        {
            m_Gates.Clear();
            m_GatePos.Clear();
            if (m_GateQuery.IsEmptyIgnoreFilter)
                return;

            // Complete the writers of the query's components before the chunk reads below:
            // they go through type handles and do not wait for running jobs (Entities
            // semantics, not in Game.dll — unverified). Required when SweepNow runs at
            // ModificationEnd with Modification jobs in flight.
            m_GateQuery.CompleteDependency();
            var entityHandle = GetEntityTypeHandle();
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            var transformHandle = GetComponentTypeHandle<Game.Objects.Transform>(isReadOnly: true);
            using (var gateChunks = m_GateQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < gateChunks.Length; c++)
                {
                    var entities = gateChunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = gateChunks[c].GetNativeArray(ref prefabRefHandle);
                    var transforms = gateChunks[c].GetNativeArray(ref transformHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        if (!m_GatePrefabs.Contains(prefabRefs[i].m_Prefab))
                            continue;
                        m_Gates.Add(entities[i]);
                        m_GatePos.Add(transforms[i].m_Position.xz);
                    }
                }
            }
        }

        /// <summary>Fills m_Plan with every live run of ours, its facts and its decision. Call
        /// CollectGates first (SignInTether reads m_Gates). Two loops: the chunk walk only
        /// copies (entity, prefab, centre); the per-run EntityManager reads happen after the
        /// chunk arrays are released.</summary>
        private void CollectRuns(bool loadPass)
        {
            m_Plan.Clear();
            if (m_RunAreaQuery.IsEmptyIgnoreFilter)
                return;

            m_RunAreaQuery.CompleteDependency();
            var entityHandle = GetEntityTypeHandle();
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            var geometryHandle = GetComponentTypeHandle<Geometry>(isReadOnly: true);
            using (var runChunks = m_RunAreaQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < runChunks.Length; c++)
                {
                    var entities = runChunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = runChunks[c].GetNativeArray(ref prefabRefHandle);
                    var geometries = runChunks[c].GetNativeArray(ref geometryHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        Entity prefab = prefabRefs[i].m_Prefab;
                        if (!IsRunPrefab(prefab))
                            continue;
                        m_Plan.Add(new RunPlan
                        {
                            Run = entities[i],
                            Prefab = prefab,
                            Center = geometries[i].m_CenterPosition.xz,
                            AreaM2 = geometries[i].m_SurfaceArea,   // E1 (D21): the fee's plan area
                        });
                    }
                }
            }

            for (int p = 0; p < m_Plan.Count; p++)
                m_Plan[p] = PlanRun(m_Plan[p], loadPass);
        }

        /// <summary>Builds one run's BindFacts (definitions: SkiRunBindingRules header) and
        /// decides. Read-only.</summary>
        private RunPlan PlanRun(RunPlan seed, bool loadPass)
        {
            Entity run = seed.Run;
            float2 center = seed.Center;
            var plan = new RunPlan
            {
                Run = run,
                Prefab = seed.Prefab,
                Center = center,
                AreaM2 = seed.AreaM2,   // E1: a fresh plan — a forgotten copy would charge the 0-ha base fee
                Nearest = -1,
                NearestDist = float.PositiveInfinity,
            };
            BindFacts facts = default;
            facts.LoadPass = loadPass;

            if (EntityManager.TryGetComponent(run, out Owner owner))
            {
                facts.HasOwner = true;
                plan.Owner = owner.m_Owner;
                plan.OwnerLive = IsLiveGate(owner.m_Owner);
                // Our sign (live or Deleted) OR no longer existing (Null after the load remap,
                // or destroyed): legacy. A LIVE entity of any other prefab: foreign.
                facts.OwnerIsOurSign = plan.OwnerLive ||
                    owner.m_Owner == Entity.Null ||
                    !EntityManager.Exists(owner.m_Owner) ||
                    (EntityManager.TryGetComponent(owner.m_Owner, out PrefabRef ownerPrefab) &&
                     m_GatePrefabs.Contains(ownerPrefab.m_Prefab));
            }

            if (EntityManager.TryGetComponent(run, out SkiRunBinding binding))
            {
                facts.HasBinding = true;
                plan.Binding = binding;
                facts.GateNull = binding.m_Gate == Entity.Null;
                facts.GateLive = IsLiveGate(binding.m_Gate);
            }

            for (int g = 0; g < m_GatePos.Count; g++)
            {
                float d = math.distance(center, m_GatePos[g]);
                if (d < plan.NearestDist)
                {
                    plan.NearestDist = d;
                    plan.Nearest = g;
                }
            }
            facts.SignInTether = plan.Nearest >= 0 && plan.NearestDist <= Tuning.TetherM;

            plan.Facts = facts;
            plan.Action = SkiRunBindingRules.Decide(in facts);
            return plan;
        }

        // ── money ───────────────────────────────────────────────────────────────────────

        private void Charge(int amount)
        {
            if (amount <= 0)
                return;
            Entity city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.HasComponent<PlayerMoney>(city))
            {
                if (!m_CityWarned)
                {
                    m_CityWarned = true;
                    Mod.Log.Warn("Ownership: City entity has no PlayerMoney — bind charges " +
                                 "skipped (warned once per load)");
                }
                return;
            }
            PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);
            money.Subtract(amount);
            EntityManager.SetComponentData(city, money);
        }

        // ── logging helpers (event-time only, never per frame) ─────────────────────────

        private bool RunLine()
        {
            if (m_RunLines < Tuning.RunLinesPerPassMax)
            {
                m_RunLines++;
                return true;
            }
            m_RunLinesSuppressed++;
            return false;
        }

        private void FlushSuppressedRunLines()
        {
            if (m_RunLinesSuppressed > 0)
            {
                Mod.Log.Info($"Ownership: {m_RunLinesSuppressed} further per-run line(s) suppressed " +
                             $"this pass (cap {Tuning.RunLinesPerPassMax}); the census line above " +
                             "carries the totals");
            }
            m_RunLines = 0;
            m_RunLinesSuppressed = 0;
        }

        private string RunLabel(Entity run, Entity prefab)
        {
            return $"{m_PrefabSystem.GetPrefabName(prefab)} {run.Index}:{run.Version}";
        }

        private static string EntityLabel(Entity e)
        {
            return e == Entity.Null ? "null" : $"{e.Index}:{e.Version}";
        }

        // ── prefab resolution ───────────────────────────────────────────────────────────

        private void ResolvePrefabs()
        {
            // Session cache, but only once both sets exist — a pre-import session retries on
            // the next load instead of staying inert until restart (the cleanup precedent).
            if (m_PrefabsResolved)
                return;
            m_GroomedPrefabs.Clear();
            m_SnowmakingPrefabs.Clear();
            m_NightPrefabs.Clear();
            m_NaturalPrefabs.Clear();
            m_CatTrackPrefabs.Clear();
            m_GatePrefabs.Clear();
            ResolveSet(GroomedSurfaceNames, "SurfacePrefab", m_GroomedPrefabs);
            ResolveSet(SnowmakingSurfaceNames, "SurfacePrefab", m_SnowmakingPrefabs);
            ResolveSet(NightSurfaceNames, "SurfacePrefab", m_NightPrefabs);
            ResolveSet(NaturalSurfaceNames, "SurfacePrefab", m_NaturalPrefabs);
            ResolveSet(CatTrackSurfaceNames, "SurfacePrefab", m_CatTrackPrefabs);
            ResolveSet(GateNames, "BuildingPrefab", m_GatePrefabs);
            m_PrefabsResolved = m_GatePrefabs.Count > 0 &&
                (m_GroomedPrefabs.Count > 0 || m_SnowmakingPrefabs.Count > 0 || m_NightPrefabs.Count > 0 ||
                 m_NaturalPrefabs.Count > 0);
        }

        /// <summary>House pattern: fast PrefabID path + name-scan fallback, tolerate
        /// missing (pre-import states must not throw).</summary>
        private void ResolveSet(string[] names, string typeName, HashSet<Entity> set)
        {
            foreach (string name in names)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID(typeName, name),
                        out PrefabBase prefabBase))
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
                    Mod.Log.Info($"Ownership: prefab '{name}' not found (tolerated)");
                    continue;
                }
                set.Add(prefabEntity);
            }
        }
    }
}
