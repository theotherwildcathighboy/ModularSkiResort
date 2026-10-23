// Binds a ski run the frame it is drawn, and waiting runs (unbound runs and paid orphans) the frame a Ski Area Welcome Sign is placed,
// paused or not. No binding logic of its own: on a qualifying frame it calls SkiRunOwnershipSystem.SweepNow, the same idempotent sweep
// (SkiRunBindingRules table) the 64-sim-frame cadence runs. Setting off = cadence-only behavior. Runs at ModificationEnd (Mod.cs
// registration), after every vanilla ModificationEnd system, so Geometry.m_CenterPosition (the sweep's tether test) is final; SkiRunBinding
// raises no tag, so vanilla sees nothing. SweepNow is main thread; no double charge (the binding is added right before the one Charge
// call). Both queries also match vanilla entities, so results are filtered in managed code. Also names new signs "Ski area N" (queued
// here, written at UIUpdate by SkiSignNameSystem). One-shot "!!!" catch disables the trigger until the next load. Game mode only.
// Engine notes: EngineNotes/SkiRunBindTriggerSystem.md

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

        // Sign auto-name (engine notes "Sign auto-name").
        private const string ResortDefaultKey = "SkiResort.Hub.RESORT_DEFAULT";
        private const string ResortDefaultEnglish = "Ski area {N}";
        private const string OrdinalPlaceholder = "{N}";
        /// <summary>Updates after a completed load in which a Created sign is not auto-named (load
        /// guard, engine notes). Tunable; a player cannot place a sign this fast.</summary>
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

        // Auto-name queue (engine notes "Written at UIUpdate"): filled at ModificationEnd, drained by SkiSignNameSystem at UIUpdate the same frame.
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
            // Created only (engine notes "Queries").
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

                // Auto-name (engine notes "Sign auto-name"): before the binding gates, since naming needs only the prefab sets, not binding being armed,
                // and not the bind-on-draw setting.
                GameManager gmName = GameManager.instance;
                if (gmName != null && !gmName.isGameLoading)
                {
                    if (m_UpdatesSinceLoad < int.MaxValue)
                        m_UpdatesSinceLoad++;
                    if (m_PendingNameSigns.Count > 0 && !m_NameStallLogged)
                    {
                        // Engine notes "Written at UIUpdate": queued last frame, still here, so the writer is not running.
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

                // Engine notes "Load-pass retry": before the setting gate (migration is not bind-on-draw).
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
                        // Engine notes "Written at UIUpdate": SetCustomName would throw here (EndFrameBarrier not allowed at ModificationEnd); SkiSignNameSystem
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

        /// <summary>Engine notes WRITTEN AT UIUpdate: writes every queued name through NameSystem.SetCustomName
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

    // SkiSignNameSystem: writes the sign auto-names SkiRunBindTriggerSystem queues (see EngineNotes/SkiRunBindTriggerSystem.md,
    // "Written at UIUpdate", for why it exists). The LiftParkSystem pattern.
    // REGISTRATION (Mod.cs): updateSystem.UpdateAt<SkiSignNameSystem>(SystemUpdatePhase.UIUpdate), added BEFORE SkiRunCatalogSystem /
    // SkiResortStatsSystem. Load-bearing twice: UIUpdate is driven by UIUpdateSystem in MainLoop (Game.Common/SystemOrder.cs:67),
    // AFTER AllowBarrier<EndFrameBarrier> (:62), so NameSystem.SetCustomName's EndFrameBarrier.CreateCommandBuffer is allowed here
    // (Game/SafeCommandBufferSystem.cs:16-23; same in 1.6.2); and a plain UpdateAt runs in add order (UpdateSystem.cs:158-161), so the
    // stats rebuild of the same frame already reads the new name (the dictionary is written at once, NameSystem.cs:236).
    // House rules: "created" line; one ARM line per load; Game mode only; one-shot "!!!" catch that disables the writer until the next load.
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
