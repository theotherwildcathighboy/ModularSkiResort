// Binds a ski run the frame it is drawn, and waiting runs (unbound runs and paid orphans) the frame a Ski Area Welcome Sign is placed,
// paused or not (docs/research/run-feedback-spec.md section 6). It owns no binding logic: on a qualifying frame it calls
// SkiRunOwnershipSystem.SweepNow, the same idempotent sweep (SkiRunBindingRules table) the 64-sim-frame cadence runs. Setting off =
// exactly the cadence-only behavior.
//
// Frame anatomy (1.6.0f1; 1.6.2 shifts noted). The click that finishes a run runs ApplyAreasSystem.Create inside ToolUpdate's
// ApplyTool phase (ApplyAreasSystem registered at SystemOrder.cs:717, 1.6.2 :718; ToolOutputSystem.cs:27-28): the Temp entity is kept,
// Temp removed, Applied + Created + Updated added (ApplyAreasSystem.cs:204-208, :272) through the ToolOutputBarrier (:297-299), which
// plays back inside ToolUpdate (SystemOrder.cs:694-695, 1.6.2 :695-696), i.e. before ModificationSystem in the same MainLoop
// (SystemOrder.cs:58, 60). GeometrySystem re-triangulates every Area+Updated at Modification2B (SystemOrder.cs:124; GeometrySystem.cs:752,
// 780), so Geometry.m_CenterPosition, what the sweep's tether test reads, is final by ModificationEnd. `Created` lives until Cleanup,
// after the whole MainLoop (CleanUpSystem.cs:53); the GameSimulation sweep never sees it. In game mode a freshly drawn area has no
// Owner (AreaToolSystem.cs:1928, 2108-2111) and our runs never get one either: the link is SkiRunBinding.
//
// Why ModificationEnd (registration in Mod.cs): MainLoop runs paused or not; ModificationSystem runs ModificationEnd last
// (ModificationSystem.cs:19-26). Mods register after SystemOrder.Initialize (GameManager.cs:615 CreateSystems -> :2381, then :618
// InitializeModManager; 1.6.2 :621/:2396/:624), and a phase is sorted by add index (UpdateSystem.cs:39-47, 158-161), so a plain
// UpdateAt lands after every vanilla UpdateAt of ModificationEnd (ValidationSystem :264 included) and before its UpdateAfter members
// (ModificationEndBarrier :93, TerrainSystem :303, 1.6.2 :304). Every vanilla Modification system has therefore already processed the
// frame; SkiRunBinding is a structural change that raises no tag (no Updated/BatchesUpdated), so vanilla systems still see nothing,
// exactly as with the cadence sweep. No update interval: ModificationSystem calls the non-indexed UpdateSystem.Update(phase), which
// ignores intervals (UpdateSystem.cs:183-221 vs :223-241).
//
// Queries:
//   m_NewRuns     All{Area, Geometry, PrefabRef, Created} None{Temp, Deleted, Owner, SkiRunBinding}: edits never re-create a run
//                 (ApplyAreasSystem.cs:123-131, 167-202: Node copy + Updated on the original, which keeps its binding), so an edit
//                 never fires this. Owner stays in None: it keeps vanilla owned lots (every new building's lot is a Created area with
//                 Owner) out of a query that would otherwise match on most building-growth frames.
//   m_ChangedGates All{Building, Transform, PrefabRef, Created} None{Temp, Deleted}: Created only. A placed sign is the former Temp
//                 entity with Applied/Created/Updated (ApplyObjectsSystem.cs:670-686). An Any{Created, Updated} arm would fire
//                 SweepNow on nearly every frame of a sign relocate (>= 128 full scans of every building and run in ~1.5 s; which
//                 vanilla writer stamps Updated every frame is unverified). Cost of not having it: unbound runs (and paid orphans)
//                 brought into tether by a relocated sign bind on the next unpaused 64-frame sweep instead of at once.
// Both match vanilla entities too (m_NewRuns any new un-owned area, e.g. a vanilla surface the player draws; m_ChangedGates every new
// building), so the result is filtered in managed code against our prefab sets; IsEmptyIgnoreFilter keeps idle frames free of any job
// sync.
//
// Safety: SweepNow writes from the main thread: EntityManager structural changes and Get/SetComponentData complete running jobs
// (Entities semantics, unverified here); the sweep completes both of its queries' dependencies first. Double charge is impossible:
// BindCharge is decided only for a run without a binding, and the sweep adds the binding synchronously right before the one Charge
// call, so the cadence sweep and this trigger can never charge the same run twice. Charging while paused matches vanilla
// (ToolApplySystem charges PlayerMoney from an ApplyTool job in MainLoop, ToolApplySystem.cs:68-72, scheduled :135).
//
// Load-pass retry (SkiRunOwnershipSystem header, load-pass failure): after a load pass that threw, ownership keeps its sweep running
// in a no-charge mode and raises LoadPassRetryPending; this system, the only ownership caller that ticks on the first frame after a
// load, paused or not, then calls SweepNow once ("load-pass retry"), whatever the bind-on-draw setting, so runs still carrying
// Owner(sign) lose the one-run-bulldoze trap on that frame instead of at the first unpaused 64-frame sweep. Any sweep clears the
// request. One bool read per frame.
//
// Sign auto-name: this system already sees placed signs (m_ChangedGates, Created only), so it also names them: a Created, non-Temp
// Welcome Sign with no custom name, in Game mode, after the load is complete, gets "Ski area N" (SkiResort.Hub.RESORT_DEFAULT from the
// active dictionary, vanilla precedent NameSystem.cs:250; English "Ski area {N}" when the key is missing) with N the smallest integer
// >= 1 such that no live sign's current custom name equals that text. The write is NameSystem.SetCustomName (Game.UI/NameSystem.cs:
// 214-241, same in 1.6.2): it stores the name in NameSystem's dictionary at once and adds CustomName + BatchesUpdated through
// NameSystem's own EndFrameBarrier command buffer (the vanilla title field's call, TitleSection.cs:40, :48-52), so the vanilla panel
// and the hub show one name, it is saved with the city (:549-575) and it stays a plain string after an uninstall. Names are stored
// already localized (a later language change does not rename them). One line per name: `Resort [name] new Welcome Sign 124274:35
// named 'Ski area 1'`. Independent of the bind-on-draw setting (naming is not binding). Cost: on a frame where a building is Created,
// the PrefabRef scan AnyGatePrefab already does, and only when a new sign is among them a scan of every live building for the current
// sign names (event time).
// Written at UIUpdate, not here: SetCustomName calls EndFrameBarrier.CreateCommandBuffer (NameSystem.cs:237), and that throws at
// ModificationEnd: SafeCommandBufferSystem.CreateCommandBuffer refuses while m_IsAllowed is false (Game/SafeCommandBufferSystem.cs:
// 16-23), EndFrameBarrier clears it in its own OnUpdate (:25-30) as the first MainLoop system (Game.Common/SystemOrder.cs:49) and only
// AllowBarrier<EndFrameBarrier> (:62) re-allows it, after ModificationSystem (:60), which runs ModificationEnd. Same order in 1.6.2
// (LiftParkSystem hits the same trap). So this system only picks the name and queues it; SkiSignNameSystem (UIUpdate, after :62;
// UIUpdateSystem :67) calls SetCustomName the same frame. A queue still full at the next ModificationEnd means the writer is not
// running: one "!!!" line per load.
// The pick is shared: SkiResortAggregate.SmallestFreeOrdinal (pure, tested in tests/ResortBatch) picks N here and in PickSignName,
// which the hub calls when the player renames a sign to "": the hub then writes a fresh "Ski area N" instead of removing the name, so
// the vanilla panel and the hub keep showing one name (removing it made the panel show the vanilla default while the hub showed a
// positional label, and that label could equal another sign's auto-name).
// Load guard: whether loaded entities still carry Created on the first frames after a load is unverified; a sign Created within
// AutoNameLoadGuardFrames updates of the load completing is not named (one line says so): a player cannot place a sign that fast, and
// an old unnamed sign keeps the hub's display-only fallback label instead of being renamed by a load.
//
// Defensive contract (house pattern, SkiRunOwnershipSystem): "created" line, ARMED line per load, m_Armed/m_Disabled/m_ErrorLogged
// reset in OnGameLoadingComplete, one-shot "!!!" catch that disables the trigger until the next game load (the cadence sweep keeps
// binding). Game mode only. GameManager.isGameLoading guard (GameManager.cs:262-272, 1.6.2 :264-274).

using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using Game.UI;
using SkiResortMod.Settings;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunBindTriggerSystem : GameSystemBase
    {
        private const string ReasonRun = "run drawn";
        private const string ReasonGate = "sign placed";
        private const string ReasonBoth = "run drawn + sign placed";
        private const string ReasonLoadPassRetry = "load-pass retry";

        // Sign auto-name (header "Sign auto-name").
        private const string ResortDefaultKey = "SkiResort.Hub.RESORT_DEFAULT";
        private const string ResortDefaultEnglish = "Ski area {N}";
        private const string OrdinalPlaceholder = "{N}";
        /// <summary>Updates after a completed load in which a Created sign is not auto-named (load
        /// guard, header). Tunable; a player cannot place a sign this fast.</summary>
        private const int AutoNameLoadGuardFrames = 2;
        /// <summary>Upper bound of the N search (a city never holds this many signs).</summary>
        private const int AutoNameMaxN = 10000;

        private SkiRunOwnershipSystem m_Ownership;
        private NameSystem m_NameSystem;
        private EntityQuery m_NewRuns;
        private EntityQuery m_ChangedGates;
        private EntityQuery m_AllBuildings;   // live signs' current names (auto-name, event time only)
        private readonly HashSet<string> m_TakenNames = new HashSet<string>(StringComparer.Ordinal);

        // Per-load state, reset at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_FirstTriggerLogged;
        private int m_UpdatesSinceLoad;
        private bool m_GuardLogged;
        private bool m_NameDisabled;      // the auto-name's own one-shot (binding keeps working)
        private bool m_NameErrorLogged;
        private bool m_NameStallLogged;   // the UIUpdate writer did not drain the queue
        private bool m_PickErrorLogged;   // PickSignName's own one-shot line

        // The "Ski area N" template of the current pick and its cached formatter (the shared SkiResortAggregate.SmallestFreeOrdinal takes a
        // Func; one delegate, created in OnCreate).
        private string m_Template = ResortDefaultEnglish;
        private Func<int, string> m_FormatOrdinal;

        // Auto-name queue (header "Written at UIUpdate"): filled at ModificationEnd, drained by SkiSignNameSystem at UIUpdate the same frame.
        // Event-time only; never shrunk.
        private readonly List<Entity> m_PendingNameSigns = new List<Entity>(2);
        private readonly List<string> m_PendingNames = new List<string>(2);

        /// <summary>Signs auto-named this load (dev tab / DUMP).</summary>
        public int SignsNamed;

        /// <summary>Reason of the last SweepNow call ("none" until one fires this load). Plain
        /// field for the "Ski Runs" developer tab.</summary>
        public string LastReason = "none";

        /// <summary>SweepNow calls made this load. Plain field for the developer tab.</summary>
        public int Triggers;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_FormatOrdinal = n => FormatOrdinal(m_Template, n);
            // Auto-name: every live building (signs are filtered by PrefabRef in managed code).
            m_AllBuildings = GetEntityQuery(new EntityQueryDesc
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

            m_NewRuns = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Created>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Owner>(),
                    ComponentType.ReadOnly<SkiRunBinding>(),
                },
            });
            // Created only (header "Queries").
            m_ChangedGates = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Created>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });

            Mod.Log.Info($"{nameof(SkiRunBindTriggerSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_FirstTriggerLogged = false;
            m_UpdatesSinceLoad = 0;
            m_GuardLogged = false;
            m_NameDisabled = false;
            m_NameErrorLogged = false;
            m_NameStallLogged = false;
            m_PickErrorLogged = false;
            m_PendingNameSigns.Clear();
            m_PendingNames.Clear();
            SignsNamed = 0;
            LastReason = "none";
            Triggers = 0;

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            // Ownership's own ARMED line follows or precedes this one (OnGameLoadingComplete
            // order between systems is not ours to rely on); IsArmed is re-read every frame.
            Mod.Log.Info("Ownership: bind-on-draw trigger ARMED — ModificationEnd every frame " +
                         "(paused included); fires SweepNow on a Created run of ours (no Owner, " +
                         "no SkiRunBinding) or a placed (Created) Ski Area Welcome Sign — a " +
                         "RELOCATED sign is not a trigger (its runs stay bound; unbound runs it " +
                         "brings into tether bind on the next unpaused 64-frame sweep); reasons " +
                         $"'{ReasonRun}' / '{ReasonGate}' / '{ReasonBoth}'; gated per frame on the " +
                         $"'{nameof(SkiResortSettings.EnableBindOnDraw)}' setting " +
                         $"(now {(BindOnDrawEnabled() ? "on" : "off")}) and ownership armed; the " +
                         "64-frame sweep stays the backstop; after a FAILED load pass one immediate " +
                         $"'{ReasonLoadPassRetry}' sweep (migrate / re-link only, no charge) on the first " +
                         "frame, whatever the setting. U2: a placed (Created) Welcome Sign with no custom name is " +
                         $"named '{ResortDefaultEnglish}' (key {ResortDefaultKey}, active dictionary) with the smallest " +
                         "N no live sign's name uses, whatever the bind setting — picked here, written through " +
                         "NameSystem.SetCustomName by SkiSignNameSystem at UIUpdate (EndFrameBarrier is not allowed at " +
                         $"ModificationEnd); not within AutoNameLoadGuardFrames={AutoNameLoadGuardFrames} update(s) of a load; " +
                         $"AutoNameMaxN={AutoNameMaxN}");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;

                // Auto-name (header "Sign auto-name"): before the binding gates, since naming needs only the prefab sets, not binding being armed,
                // and not the bind-on-draw setting.
                GameManager gmName = GameManager.instance;
                if (gmName != null && !gmName.isGameLoading)
                {
                    if (m_UpdatesSinceLoad < int.MaxValue)
                        m_UpdatesSinceLoad++;
                    if (m_PendingNameSigns.Count > 0 && !m_NameStallLogged)
                    {
                        // Header "Written at UIUpdate": queued last frame, still here, so the writer is not running.
                        m_NameStallLogged = true;
                        Mod.Log.Error($"!!! Resort [name] {m_PendingNameSigns.Count} queued sign name(s) were not written by " +
                                      "SkiSignNameSystem (UIUpdate) — it is not running (not registered, or disabled by an " +
                                      "exception): new Welcome Signs stay unnamed this load (the hub shows its fallback label); " +
                                      "logged once per load");
                    }
                    if (!m_NameDisabled && m_Ownership.PrefabSetsReady && !m_ChangedGates.IsEmptyIgnoreFilter)
                        AutoNameNewSigns();
                }

                if (!m_Ownership.IsArmed)
                    return;

                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading)
                    return;

                // Header "Load-pass retry": before the setting gate (migration is not bind-on-draw).
                if (m_Ownership.LoadPassRetryPending)
                {
                    LastReason = ReasonLoadPassRetry;
                    Triggers++;
                    Mod.Log.Info("Ownership: bind-on-draw trigger — this load's load pass FAILED: immediate " +
                                 $"'{ReasonLoadPassRetry}' sweep (migrate / re-link only, no charge)");
                    m_Ownership.SweepNow(ReasonLoadPassRetry);   // clears the request (or disables binding on a throw)
                    return;
                }

                if (!BindOnDrawEnabled())
                    return;

                // Cheapest first: IsEmptyIgnoreFilter syncs nothing. m_NewRuns is rarely non-empty: only new areas with neither Owner nor
                // SkiRunBinding match, and building lots carry Owner. m_ChangedGates is non-empty on frames where any building is created: every
                // building is born with Created (BuildingPrefab.cs:73, RefreshArchetype), so a growing city still hits it, but not on the Updated
                // stamps of BuildingConstructionSystem.cs:225, BuildingUpkeepSystem.cs:485, ParkAISystem.cs:100 and others. On those frames the
                // managed PrefabRef scan runs, and its only sync is PrefabRef's writers (see AnyRunPrefab).
                bool runs = !m_NewRuns.IsEmptyIgnoreFilter && AnyRunPrefab(m_NewRuns);
                bool gates = !m_ChangedGates.IsEmptyIgnoreFilter && AnyGatePrefab(m_ChangedGates);
                if (!runs && !gates)
                    return;

                string reason = runs ? (gates ? ReasonBoth : ReasonRun) : ReasonGate;
                LastReason = reason;
                Triggers++;
                if (!m_FirstTriggerLogged)
                {
                    // Once per load: proves the trigger is live; the sweep logs every bind
                    // itself ("Ownership [bind] (<reason>)").
                    m_FirstTriggerLogged = true;
                    Mod.Log.Info($"Ownership: bind-on-draw trigger fired ({reason}) — first " +
                                 "this load; later triggers are counted on the 'Ski Runs' tab");
                }
                m_Ownership.SweepNow(reason);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    LastReason = "DISABLED by exception (see mod log)";
                    Mod.Log.Error($"!!! {nameof(SkiRunBindTriggerSystem)} EXCEPTION — bind on " +
                                  "draw disabled until the next game load (the 64-frame sweep " +
                                  $"still binds):\n{e}");
                }
            }
        }


        /// <summary>Names every Created Welcome Sign of this frame that has no custom name. Its own
        /// try/catch and one-shot "!!!": a naming failure never stops bind-on-draw.</summary>
        private void AutoNameNewSigns()
        {
            try
            {
                // Same sync contract as AnyGatePrefab: entities + PrefabRef only.
                using (var entities = m_ChangedGates.ToEntityArray(Allocator.Temp))
                using (var prefabRefs = m_ChangedGates.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                {
                    bool collected = false;
                    string template = null;
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!m_Ownership.IsGatePrefab(prefabRefs[i].m_Prefab))
                            continue;
                        Entity sign = entities[i];
                        if (m_NameSystem.TryGetCustomName(sign, out string existing) && !string.IsNullOrEmpty(existing))
                            continue;   // already named (a copy of a named sign, or named elsewhere)
                        if (m_UpdatesSinceLoad <= AutoNameLoadGuardFrames)
                        {
                            if (!m_GuardLogged)
                            {
                                m_GuardLogged = true;
                                Mod.Log.Info($"Resort [name] Welcome Sign {sign.Index}:{sign.Version} carries Created within " +
                                             $"{AutoNameLoadGuardFrames} update(s) of the load — treated as loaded, not auto-named " +
                                             "(the hub shows its fallback label; rename it in the hub or the sign's panel); " +
                                             "logged once per load");
                            }
                            continue;
                        }
                        if (!collected)
                        {
                            collected = true;
                            template = ResortTemplate();
                            m_Template = template;
                            CollectSignNames(Entity.Null);
                        }
                        // The pick (SkiResortAggregate.SmallestFreeOrdinal, shared with the hub's empty rename).
                        int free = SkiResortAggregate.SmallestFreeOrdinal(m_TakenNames, m_FormatOrdinal, AutoNameMaxN);
                        string name = free > 0 ? FormatOrdinal(template, free) : null;
                        if (name == null)
                        {
                            Mod.Log.Warn($"Resort [name] new Welcome Sign {sign.Index}:{sign.Version}: every '{template}' up to " +
                                         $"N={AutoNameMaxN} is taken — left unnamed");
                            continue;
                        }
                        // Header "Written at UIUpdate": SetCustomName would throw here (EndFrameBarrier not allowed at ModificationEnd); SkiSignNameSystem
                        // writes and logs it this frame.
                        if (m_PendingNameSigns.Contains(sign))
                            continue;
                        m_PendingNameSigns.Add(sign);
                        m_PendingNames.Add(name);
                        m_TakenNames.Add(name);
                    }
                }
            }
            catch (Exception e)
            {
                m_NameDisabled = true;
                if (!m_NameErrorLogged)
                {
                    m_NameErrorLogged = true;
                    Mod.Log.Error("!!! " + nameof(SkiRunBindTriggerSystem) + " EXCEPTION in the sign auto-name — new Welcome " +
                                  "Signs stay unnamed until the next game load (the hub shows its fallback label; bind on " +
                                  "draw keeps working):\n" + e);
                }
            }
        }

        /// <summary>The current custom names of every live Welcome Sign except <paramref name="except"/>
        /// (event time: a scan of every live building's PrefabRef; only on a frame where a new unnamed
        /// sign was placed, or on a hub rename to "").</summary>
        private void CollectSignNames(Entity except)
        {
            m_TakenNames.Clear();
            for (int i = 0; i < m_PendingNames.Count; i++)
            {
                if (m_PendingNameSigns[i] != except)
                    m_TakenNames.Add(m_PendingNames[i]);   // queued, not yet in NameSystem
            }

            if (m_AllBuildings.IsEmptyIgnoreFilter)
                return;
            using (var entities = m_AllBuildings.ToEntityArray(Allocator.Temp))
            using (var prefabRefs = m_AllBuildings.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (entities[i] != except && m_Ownership.IsGatePrefab(prefabRefs[i].m_Prefab) &&
                        m_NameSystem.TryGetCustomName(entities[i], out string name) && !string.IsNullOrEmpty(name))
                        m_TakenNames.Add(name);
                }
            }
        }

        /// <summary>The fresh auto-name a hub rename to "" writes on <paramref name="sign"/>: the smallest "Ski area N" no other live sign's
        /// custom name uses (the auto-name's own rule, so the vanilla panel and the hub keep showing one name instead of the vanilla
        /// default vs. a hub fallback label). Null when none is free or on an exception (then the caller removes the name, the vanilla
        /// behavior; one "!!!" line per load). Event time only, main thread (the hub's UIUpdate).</summary>
        internal string PickSignName(Entity sign)
        {
            try
            {
                if (!m_Ownership.PrefabSetsReady)
                    return null;
                m_Template = ResortTemplate();
                CollectSignNames(sign);
                int free = SkiResortAggregate.SmallestFreeOrdinal(m_TakenNames, m_FormatOrdinal, AutoNameMaxN);
                return free > 0 ? FormatOrdinal(m_Template, free) : null;
            }
            catch (Exception e)
            {
                if (!m_PickErrorLogged)
                {
                    m_PickErrorLogged = true;
                    Mod.Log.Error("!!! " + nameof(SkiRunBindTriggerSystem) + " EXCEPTION picking a sign name for a hub rename " +
                                  "— the rename removes the name instead (the vanilla default shows):\n" + e);
                }
                return null;
            }
        }

        /// <summary>SkiResort.Hub.RESORT_DEFAULT from the ACTIVE dictionary (NameSystem.cs:250
        /// precedent), else the English text.</summary>
        private static string ResortTemplate()
        {
            var dictionary = GameManager.instance?.localizationManager?.activeDictionary;
            return dictionary != null && dictionary.TryGetValue(ResortDefaultKey, out string value) && !string.IsNullOrEmpty(value)
                ? value
                : ResortDefaultEnglish;
        }

        private static string FormatOrdinal(string template, int n)
        {
            string number = n.ToString(CultureInfo.InvariantCulture);
            return template.IndexOf(OrdinalPlaceholder, StringComparison.Ordinal) >= 0
                ? template.Replace(OrdinalPlaceholder, number)
                : template + " " + number;
        }

        /// <summary>Queued names not yet written (SkiSignNameSystem reads it every UIUpdate).</summary>
        internal int PendingNameCount => m_PendingNameSigns.Count;

        /// <summary>Header WRITTEN AT UIUpdate: writes every queued name through NameSystem.SetCustomName
        /// (allowed at UIUpdate: AllowBarrier&lt;EndFrameBarrier&gt; ran at SystemOrder.cs:62, before
        /// UIUpdateSystem :67) and logs one line per name. Called only by SkiSignNameSystem. Uses the
        /// auto-name's own one-shot: a failure here never stops bind-on-draw.</summary>
        internal void FlushPendingNames()
        {
            if (m_PendingNameSigns.Count == 0)
                return;
            try
            {
                if (!m_NameDisabled)
                {
                    for (int i = 0; i < m_PendingNameSigns.Count; i++)
                    {
                        Entity sign = m_PendingNameSigns[i];
                        string name = m_PendingNames[i];
                        if (!EntityManager.Exists(sign) || EntityManager.HasComponent<Deleted>(sign))
                        {
                            Mod.Log.Info($"Resort [name] Welcome Sign {sign.Index}:{sign.Version} was removed before its name " +
                                         $"'{name}' was written — skipped");
                            continue;
                        }
                        if (m_NameSystem.TryGetCustomName(sign, out string existing) && !string.IsNullOrEmpty(existing))
                            continue;   // named meanwhile (same frame): keep the player's name
                        m_NameSystem.SetCustomName(sign, name);   // NameSystem.cs:214-241 (EndFrameBarrier inside)
                        SignsNamed++;
                        Mod.Log.Info($"Resort [name] new Welcome Sign {sign.Index}:{sign.Version} named '{name}'");
                    }
                }
            }
            catch (Exception e)
            {
                m_NameDisabled = true;
                if (!m_NameErrorLogged)
                {
                    m_NameErrorLogged = true;
                    Mod.Log.Error("!!! " + nameof(SkiRunBindTriggerSystem) + " EXCEPTION writing a sign auto-name — new Welcome " +
                                  "Signs stay unnamed until the next game load (the hub shows its fallback label; bind on " +
                                  "draw keeps working):\n" + e);
                }
            }
            finally
            {
                m_PendingNameSigns.Clear();
                m_PendingNames.Clear();
            }
        }


        /// <summary>Null-tolerant read (the SkiRunSplatSystem pattern): Mod.Settings is
        /// assigned before registration, so null means "not loaded" → the default (on).</summary>
        private static bool BindOnDrawEnabled()
        {
            var settings = Mod.Settings;
            return settings == null || settings.EnableBindOnDraw;
        }

        /// <summary>Managed PrefabRef scan of a small result. ToComponentDataArray&lt;PrefabRef&gt;
        /// completes PrefabRef's write dependency itself (Unity.Entities
        /// EntityQueryImpl.ToComponentDataArray → DependencyManager.CompleteWriteDependency(typeIndex);
        /// Entities package source, not in Game.dll — unverified against the shipped build).
        /// EntityQuery.CompleteDependency would also wait on every in-flight writer of
        /// Building/Transform/Area/Geometry/Created/Updated
        /// (CompleteDependenciesNoChecks(ReaderTypes, WriterTypes)), so do not add it.</summary>
        private bool AnyRunPrefab(EntityQuery query)
        {
            using (var prefabRefs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < prefabRefs.Length; i++)
                {
                    if (m_Ownership.IsRunPrefab(prefabRefs[i].m_Prefab))
                        return true;
                }
            }
            return false;
        }

        private bool AnyGatePrefab(EntityQuery query)
        {
            // Same sync contract as AnyRunPrefab: no query-wide CompleteDependency.
            using (var prefabRefs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < prefabRefs.Length; i++)
                {
                    if (m_Ownership.IsGatePrefab(prefabRefs[i].m_Prefab))
                        return true;
                }
            }
            return false;
        }
    }

    // SkiSignNameSystem: writes the sign auto-names SkiRunBindTriggerSystem queues; see that header "Written at UIUpdate" for why it
    // exists. The LiftParkSystem pattern.
    //
    // REGISTRATION (Mod.cs): updateSystem.UpdateAt<SkiSignNameSystem>(SystemUpdatePhase.UIUpdate), added
    // BEFORE SkiRunCatalogSystem / SkiResortStatsSystem. Load-bearing twice: UIUpdate is driven by
    // UIUpdateSystem in MainLoop (Game.Common/SystemOrder.cs:67), AFTER AllowBarrier<EndFrameBarrier>
    // (:62), so NameSystem.SetCustomName's EndFrameBarrier.CreateCommandBuffer is allowed here
    // (Game/SafeCommandBufferSystem.cs:16-23; same in 1.6.2); and a plain UpdateAt runs in add order
    // (UpdateSystem.cs:158-161), so the stats rebuild of the same frame already reads the new name (the
    // dictionary is written at once, NameSystem.cs:236).
    //
    // House rules: "created" line; one ARM line per load; Game mode only; one-shot "!!!" catch that
    // disables the writer until the next load (the bind trigger then reports the stalled queue once).
    // Per frame: one int read; nothing allocated.
    public sealed partial class SkiSignNameSystem : GameSystemBase
    {
        private SkiRunBindTriggerSystem m_BindTrigger;
        private bool m_Armed, m_Disabled, m_ErrorLogged;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_BindTrigger = World.GetOrCreateSystemManaged<SkiRunBindTriggerSystem>();
            Mod.Log.Info($"{nameof(SkiSignNameSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_ErrorLogged = false;
            if (m_Armed)
                Mod.Log.Info("SignName: ARMED — writes the Welcome Sign auto-names queued by SkiRunBindTriggerSystem " +
                             "(ModificationEnd) through NameSystem.SetCustomName at UIUpdate, where EndFrameBarrier is allowed");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (!m_Armed || m_Disabled || m_BindTrigger.PendingNameCount == 0)
                    return;
                m_BindTrigger.FlushPendingNames();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiSignNameSystem)} EXCEPTION — new Welcome Signs are no longer auto-named " +
                                  $"until the next game load. Please report this stack:\n{e}");
                }
            }
        }
    }
}
