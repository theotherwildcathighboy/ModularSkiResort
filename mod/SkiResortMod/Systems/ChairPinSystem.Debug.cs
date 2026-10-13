// ChairPinSystem.Debug — the "Ski Resort" tab of the game's developer menu (L0 of the
// live-connection plan, DECISIONS 100/101).
//
// HOW IT REACHES THE SCREEN. The vanilla developer menu (--developerMode, Tab) is built
// from UnityEngine.Rendering.DebugUI widgets that Game.Debug.DebugSystem collects by
// reflection on EVERY menu open: it sweeps all loaded non-Unity assemblies for classes
// carrying [DebugContainer] and invokes each [DebugTab] method (DebugSystem.RegisterDebug,
// decompile 1.6.2:1025-1044). A tab method declared on a ComponentSystemBase is invoked on
// the live system instance (World.GetOrCreateSystemManaged, :1005-1009), which is why this
// is a partial of ChairPinSystem: it reads the mover's private state directly. Nothing is
// registered in Mod.cs — discovery is automatic, and the tab rebuilds on every open and on
// every save load, so it always reflects current state.
//
// HOW IT REACHES CLAUDE. The menu's widget tree is published to the UI as the
// `debug.children` binding (values included) and every Button is a `debug.invoke`; the
// gameface MCP (localhost:9444 with --uiDeveloperMode) can subscribe to the tree and click
// buttons, so this tab is readable and drivable from outside the game with no further
// installs (agents-plugins debug-menu research, "Driving the menu from outside").
//
// COST CONTRACT (DebugUISystem cost model): DebugUI.Value getters run at 10 Hz while the
// tab is open and must not allocate much, complete jobs, or run EntityQueries — the rows
// below read plain counters that OnUpdate/BuildLoopCache already maintain. The per-chair
// rows read a handful of components for a fixed small sample. Anything heavier runs on a
// Button press and goes to the mod log, where it is also readable after the fact.
//
// ROUND L1 (2026-10-03, implementer I-2): rows for LoopGeneration, the immediate-rebuild
// requests and the OD-7 seeding counters, plus a "Lift census (L1)" foldout reading
// LiftCensusSystem / LiftParkSystem / LiftReconnectSystem / SkiStationAlignSystem public
// counters (systems fetched with GetExistingSystemManaged, so a system the integrator did not
// register shows "not registered" instead of being created unregistered), and a button that
// dumps the census rows to the mod log.
//
// CATALOG PHASE A (2026-10-11; catalog-a/phase-a-contract.md §3.2 L2, D8 lifts the freeze): the prefab rows
// read SkiPrefabCatalogSystem (the chairlift line entity, the carrier count, its load / generation); the dump
// line names catalogLoad / catalogGeneration and adds the catalog's Describe() line.
//
// CATALOG PHASE B (2026-10-11; code-round-2/round-2-contract.md §5.2, lift group): the mover's clock is in seconds and
// each loop's phase is its speed × the clock, so the "Derived phase" row shows the clock and the fixed-grip phase, the
// expected-position sample uses the chair's loop phase, the dump prints each loop's type / speed / phase / up-lane
// verdict, and one row counts the loops that mix lift types, the Shuttle carriers skipped and the carriers riding a loop
// of another type.
//
// CATALOG PHASE C (2026-10-11; code-round-3/round-3-contract.md C17, §4.10, mover group): after the census foldout the tab
// holds the "Tram shuttle (Phase C)" foldout, built by TramShuttleSystem.AddDebugWidgets (TramShuttleSystem.Debug.cs; a
// "Tram shuttle: not registered" row when the system is absent), and DumpDiagnostics appends TramShuttleSystem.AppendDump.
// The tram's partial declares no [DebugTab]: DebugSystem.AddPanel replaces the widget list stored under a tab's name
// (Game.Debug/DebugSystem.cs:1085-1095), so a second "Ski Resort" tab would replace these rows.
//
// PHASE D (2026-10-11; code-round-4/round-4-contract.md §4.3, movers group): the power hold (ChairPinSystem.cs header PHASE
// D). The "Moving mode" foldout gains two rows — "Held loops (no power) / chairs held / power reads / last poll ms" and
// "Last hold change" — reading the counters PollHolds keeps (plain fields: the 10 Hz getter cost contract holds). The
// expected-position sample uses a held loop's frozen clock. The dump's "loop k:" lines gain "state=Running" or
// "state=Held(no power, frozen at t s, phase p m)", and a "hold:" block lists every station pair with its census row, the
// power read and the hold.

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Entities; // EntityManager.TryGetComponent extension
using Game.Common;
using Game.Debug;
using Game.Objects;
using Game.Rendering;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace SkiResortMod.Systems
{
    [DebugContainer]
    public sealed partial class ChairPinSystem
    {
        /// <summary>Chairs shown as live rows in the tab (rest are in the log dump).</summary>
        private const int DebugChairSampleCount = 6;

        /// <summary>Chairs written per log dump (cap keeps a 300-chair lift readable).</summary>
        private const int DebugDumpChairCap = 40;

        [DebugTab("Ski Resort", 0)]
        private List<DebugUI.Widget> BuildSkiResortDebugUI()
        {
            var list = new List<DebugUI.Widget>();

            // ── Gate: is the mover even allowed to run? ─────────────────────────────────
            list.Add(Val("Mode", () => MovingChairs ? "moving (milestone 2)" : "static (milestone 1)"));
            // Catalog Phase A (ext rows 1, 3; was m_ChairliftPrefab / m_ChairliftPrefabResolved / m_ChairPrefabs): the
            // prefab catalog's views. "unresolved" = no game / editor load yet (the catalog resolves per load).
            list.Add(Val("Chairlift net prefab", () =>
            {
                Entity line = ChairliftPrefab;
                return line != Entity.Null ? Ent(line) + " (" + SkiCatalog.Lifts[0].LinePrefab + ")"
                    : m_PrefabCatalog.Loads > 0 ? "NOT FOUND — moving mode inactive"
                    : "unresolved (no game loaded yet)";
            }));
            list.Add(Val("Chair prefabs resolved", () => m_PrefabCatalog.CarrierGripTops.Count));
            list.Add(Val("Prefab catalog load / generation", () => $"{m_PrefabCatalog.Loads} / {m_PrefabCatalog.Generation}"));
            list.Add(Val("System enabled", () => Enabled));
            list.Add(Val("Disabled by exception", () => m_Disabled));
            list.Add(Val("OnUpdate calls (this load)", () => m_DiagUpdateCalls));
            list.Add(Val("Frames since last OnUpdate", () =>
                m_DiagLastUpdateFrame < 0 ? "never" : ((long)m_RenderingSystem.frameIndex - m_DiagLastUpdateFrame).ToString()));
            list.Add(Val("Last OnUpdate exit", () => m_DiagLastExit));
            list.Add(Val("Last error", () => m_LastError == null ? "-" : FirstLine(m_LastError)));

            // ── Moving mode ─────────────────────────────────────────────────────────────
            var moving = new DebugUI.Foldout { displayName = "Moving mode", opened = true };
            moving.children.Add(Val("Render clock frameIndex / frameTime", () =>
                $"{m_RenderingSystem.frameIndex} / {m_RenderingSystem.frameTime:F3}"));
            // Catalog Phase B (round-2-contract §5.2 item 2): the clock is in seconds; each loop's phase = its speed × it.
            moving.children.Add(Val("Derived clock (s) / fixed-grip phase (m), last pass", () =>
                $"{m_DiagLastTimeS:F2} / {m_DiagLastTimeS * SkiCatalog.FixedGrip.SpeedMps:F2}"));
            moving.children.Add(Val("Loops mixing lift types / shuttle carriers skipped / carriers on another type's loop", () =>
                $"{m_DiagMixedLoops} / {m_DiagShuttleCarriers} / {m_DiagTypeMismatch}"));
            moving.children.Add(Val("Loops / lanes matched / lanes total", () =>
                $"{m_LoopCache.m_Loops.Count} / {m_DiagLanesMatched} / {m_DiagLaneTotal}"));
            moving.children.Add(Val("Chains / unclosed", () =>
                $"{m_LoopCache.m_ChainCount} / {m_LoopCache.m_UnclosedChainCount}"));
            moving.children.Add(Val("Stations found", () => m_DiagStations));
            moving.children.Add(Val("Chairs matched / riding / static-fallback", () =>
                $"{m_DiagChairsMatched} / {m_DiagLastRiding} / {m_StaticFallbackChairs}"));
            moving.children.Add(Val("Last pass moved / in place / missing", () =>
                $"{m_DiagLastMoved} / {m_DiagLastInPlace} / {m_DiagLastMissing}"));
            moving.children.Add(Val("Passes with moves / paused skips", () =>
                $"{m_DiagPassesWithMoves} / {m_DiagPausedSkips}"));
            moving.children.Add(Val("Change probe (updated chairs), last pass", () => m_DiagProbeUpdatedChairs));
            moving.children.Add(Val("Cache dirty / cooldown / empty-roster retries", () =>
                $"{m_LoopCacheDirty} / {m_RebuildCooldown} / {m_EmptyRosterRetries}"));
            moving.children.Add(Val("Rebuilds / last rebuild frame / enrolled", () =>
                $"{m_DiagRebuilds} / {(m_DiagLastRebuildFrame < 0 ? "-" : m_DiagLastRebuildFrame.ToString())} / {m_DiagEnrolled}"));
            moving.children.Add(Val("Last rebuild signature", () => m_LastBuildSignature ?? "-"));
            moving.children.Add(Val("Loop generation (L1)", () => LoopGeneration));
            moving.children.Add(Val("Immediate rebuilds: requested / bypassed cooldown / last reason", () =>
                $"{m_DiagImmediateRequests} / {m_DiagImmediateRebuilds} / {m_ImmediateWhy}"));
            moving.children.Add(Val("OD-7 seeding: loops seeded / chairs / grid loops", () =>
                $"{m_DiagSeededLoops} / {m_DiagSeededChairs} / {m_DiagGridLoops}"));
            // Phase D (round-4-contract §4.3): the power hold (PollHolds' counters; no query, no job).
            moving.children.Add(Val("Held loops (no power) / chairs held / power reads / last poll ms", () =>
                $"{m_HeldLoopCount} / {m_HeldChairCount} / {m_DiagHoldPowerReads} / {m_DiagHoldPollMs:F3}"));
            moving.children.Add(Val("Last hold change", () => m_HoldLastChange));
            list.Add(moving);

            // ── Round L1: lift census, park, reconnect episodes, alignment aid ─────────────
            var census = new DebugUI.Foldout { displayName = "Lift census (L1)", opened = false };
            census.children.Add(Val("Census armed / revision / builds", () =>
            {
                var c = World.GetExistingSystemManaged<LiftCensusSystem>();
                return c == null ? "not registered" : $"{c.IsArmed} / {c.Revision} / {c.Builds}";
            }));
            census.children.Add(Val("Last build ms / max ms", () =>
            {
                var c = World.GetExistingSystemManaged<LiftCensusSystem>();
                return c == null ? "-" : $"{c.LastBuildMs:F2} / {c.MaxBuildMs:F2}";
            }));
            census.children.Add(Val("Last event", () => World.GetExistingSystemManaged<LiftCensusSystem>()?.LastEvent ?? "-"));
            census.children.Add(Val("Park queue / events created", () =>
            {
                var c = World.GetExistingSystemManaged<LiftCensusSystem>();
                var p = World.GetExistingSystemManaged<LiftParkSystem>();
                return $"{(c == null ? "-" : c.ParkQueueCount.ToString())} / {(p == null ? "emitter not registered" : p.EventsCreated.ToString())}";
            }));
            census.children.Add(Val("Last moved station / frame", () =>
            {
                var c = World.GetExistingSystemManaged<LiftCensusSystem>();
                return c == null ? "-" : $"{Ent(c.LastMovedStation)} / {c.LastMovedFrame}";
            }));
            census.children.Add(Val("Reconnect episodes open / lines", () =>
            {
                var r = World.GetExistingSystemManaged<LiftReconnectSystem>();
                return r == null ? "not registered" : $"{r.OpenEpisodes} / {r.EpisodeLines}";
            }));
            census.children.Add(Val("Align: active / partner / aligned / offset m / yaw °", () =>
            {
                var a = World.GetExistingSystemManaged<SkiStationAlignSystem>();
                if (a == null) return "not registered";
                var m = a.Last;
                return $"{a.Active} / {(a.HasPartner ? Ent(a.Partner) : "-")} / {m.Aligned} / {m.LateralM:F2} / {m.YawDeg:F2}";
            }));
            census.children.Add(Val("Align: frames drawn / placements / moves logged", () =>
            {
                var a = World.GetExistingSystemManaged<SkiStationAlignSystem>();
                return a == null ? "-" : $"{a.FramesDrawn} / {a.PlacementsLogged} / {a.MovesLogged}";
            }));
            list.Add(census);

            // ── Catalog Phase C (round-3-contract C17, §4.10): the tram shuttle mover's foldout, built by
            // TramShuttleSystem.Debug.cs (that partial carries no [DebugTab]: a second "Ski Resort" tab would replace this
            // one's rows, DebugSystem.cs:1085-1095). Fetched with GetExistingSystemManaged, as the census foldout's systems.
            var tram = World.GetExistingSystemManaged<TramShuttleSystem>();
            if (tram != null)
                tram.AddDebugWidgets(list);
            else
                list.Add(Val("Tram shuttle", () => "not registered"));

            // ── Static mode (milestone 1 flow; also the fallback when the net prefab is missing)
            var stat = new DebugUI.Foldout { displayName = "Static mode", opened = false };
            stat.children.Add(Val("Full pass pending / label / retries", () =>
                $"{m_FullPassPending} / {m_PendingPassLabel} / {m_FullPassRetries}"));
            stat.children.Add(Val("Update path alive / zero-match logged", () =>
                $"{m_UpdateAliveLogged} / {m_ZeroMatchLogged}"));
            stat.children.Add(Val("Watchdog countdown", () => m_WatchdogCountdown));
            list.Add(stat);

            // ── Live chair rows: expected (loop sample) vs actual (Transform) ───────────
            var sample = CollectSampleChairs(DebugChairSampleCount);
            var rows = new DebugUI.Foldout { displayName = $"Chairs (first {sample.Count} of roster)", opened = true };
            if (sample.Count == 0)
                rows.children.Add(Val("(none)", () => "no chair in any loop roster — see Chairs matched"));
            for (int i = 0; i < sample.Count; i++)
            {
                var slot = sample[i];
                rows.children.Add(Val(Ent(slot.m_Chair), () => DescribeChair(slot)));
            }
            list.Add(rows);

            // ── Actions ─────────────────────────────────────────────────────────────────
            list.Add(new DebugUI.Button
            {
                displayName = "Force cache rebuild (full pass)",
                action = () =>
                {
                    RequestFullPass("debug tab");
                    m_RebuildCooldown = 0;
                    DebugSystem.Rebuild(BuildSkiResortDebugUI);
                }
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Dump diagnostics to mod log",
                action = () => DumpDiagnostics("debug tab")
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Dump lift census to mod log (L1)",
                action = () =>
                {
                    var c = World.GetExistingSystemManaged<LiftCensusSystem>();
                    if (c != null)
                        c.DumpCensus("debug tab");
                    else
                        Mod.Log.Info($"{nameof(ChairPinSystem)} [debug tab]: LiftCensusSystem is not registered");
                }
            });
            list.Add(new DebugUI.Button
            {
                displayName = MovingChairs ? "Switch to STATIC mode" : "Switch to MOVING mode",
                action = () =>
                {
                    MovingChairs = !MovingChairs;
                    RequestFullPass("mode toggle");
                    m_RebuildCooldown = 0;
                    Mod.Log.Info($"{nameof(ChairPinSystem)} [debug tab]: mode switched to {(MovingChairs ? "moving" : "static")}");
                    DebugSystem.Rebuild(BuildSkiResortDebugUI);
                }
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Select first sampled chair (vanilla entity section)",
                action = () =>
                {
                    var first = CollectSampleChairs(1);
                    if (first.Count > 0)
                        World.GetOrCreateSystemManaged<ToolSystem>().selected = first[0].m_Chair;
                }
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Refresh tab",
                action = () => DebugSystem.Rebuild(BuildSkiResortDebugUI)
            });

            return list;
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────

        private struct ChairSlot
        {
            public Entity m_Chair;
            public int m_LoopIndex;
            public double m_Offset;
            public float m_GripTop;
        }

        /// <summary>First <paramref name="count"/> roster chairs across loops (loop order,
        /// roster order). Snapshot at tab-build time; the rows read live state per tick.</summary>
        private List<ChairSlot> CollectSampleChairs(int count)
        {
            var result = new List<ChairSlot>(count);
            for (int l = 0; l < m_LoopCache.m_Loops.Count && result.Count < count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                for (int i = 0; i < loop.m_Chairs.Count && result.Count < count; i++)
                {
                    result.Add(new ChairSlot
                    {
                        m_Chair = loop.m_Chairs[i],
                        m_LoopIndex = l,
                        m_Offset = i < loop.m_Offsets.Count ? loop.m_Offsets[i] : 0.0,
                        m_GripTop = loop.m_GripTops[i],
                    });
                }
            }
            return result;
        }

        /// <summary>One line per chair: where the loop says it should be, where its
        /// Transform actually is, and the flags that decide whether it renders there.</summary>
        private string DescribeChair(ChairSlot slot)
        {
            try
            {
                Entity chair = slot.m_Chair;
                if (!EntityManager.Exists(chair))
                    return "gone (entity does not exist)";
                bool deleted = EntityManager.HasComponent<Deleted>(chair);
                bool temp = EntityManager.HasComponent<Temp>(chair);
                bool isStatic = EntityManager.HasComponent<Game.Objects.Static>(chair);
                bool updated = EntityManager.HasComponent<Updated>(chair);
                bool interp = EntityManager.HasComponent<InterpolatedTransform>(chair);
                if (!EntityManager.TryGetComponent<Transform>(chair, out var transform))
                    return "no Transform";

                string expected = "-";
                if (slot.m_LoopIndex < m_LoopCache.m_Loops.Count)
                {
                    LiftLoopCache.Loop loop = m_LoopCache.m_Loops[slot.m_LoopIndex];
                    if (loop.m_Length > 0.0)
                    {
                        // Catalog Phase B: the loop's own phase (its speed × the last mover clock). Phase D (§4.3): a held
                        // loop stands at its frozen clock.
                        LoopHold hold = slot.m_LoopIndex < m_HoldByLoop.Count ? m_HoldByLoop[slot.m_LoopIndex] : null;
                        double phase = hold != null && hold.Held
                            ? LiftLoopHold.FrozenPhase(loop.m_SpeedMps, hold.FrozenTimeS)
                            : PhaseOf(loop, m_LastMoveTimeS);
                        double s = (slot.m_Offset + phase) % loop.m_Length;
                        if (s < 0.0) s += loop.m_Length;
                        LiftLoopCache.Sample(loop, s, out float3 pos, out _);
                        pos.y -= slot.m_GripTop;
                        expected = $"s={s:F1} exp={Vec(pos)} d={math.distance(pos, transform.m_Position):F2}";
                    }
                }

                string interpPos = "-";
                if (interp)
                {
                    var it = EntityManager.GetComponentData<InterpolatedTransform>(chair);
                    interpPos = Vec(it.m_Position);
                }

                return $"loop {slot.m_LoopIndex} off {slot.m_Offset:F1} | pos {Vec(transform.m_Position)} | " +
                       $"{expected} | interp {(interp ? interpPos : "NONE")} | " +
                       $"static {YN(isStatic)} upd {YN(updated)} temp {YN(temp)} del {YN(deleted)}";
            }
            catch (Exception e)
            {
                return "err: " + FirstLine(e.ToString());
            }
        }

        /// <summary>Everything the tab shows plus the full roster and loop geometry, as one
        /// block in the mod log — readable by Richard and by Claude after the session.</summary>
        internal void DumpDiagnostics(string reason)
        {
            var sb = new StringBuilder(4096);
            try
            {
                sb.AppendLine($"{nameof(ChairPinSystem)} [dump: {reason}] ────────────────────────────");
                // Catalog Phase A (ext rows 1, 3): the catalog's views; resolvedAttempted (once per app run) is gone,
                // catalogLoad / catalogGeneration say which load's resolution these are.
                Entity chairlift = ChairliftPrefab;
                sb.AppendLine($"  mode={(MovingChairs ? "moving" : "static")} enabled={Enabled} disabled={m_Disabled} " +
                              $"chairPrefabs={m_PrefabCatalog.CarrierGripTops.Count} chairliftPrefab={(chairlift == Entity.Null ? "NULL" : Ent(chairlift))} " +
                              $"catalogLoad={m_PrefabCatalog.Loads} catalogGeneration={m_PrefabCatalog.Generation}");
                sb.AppendLine("  " + m_PrefabCatalog.Describe());
                sb.AppendLine($"  onUpdateCalls={m_DiagUpdateCalls} lastUpdateFrame={m_DiagLastUpdateFrame} " +
                              $"renderFrame={m_RenderingSystem.frameIndex} frameTime={m_RenderingSystem.frameTime:F3} " +
                              $"lastExit=\"{m_DiagLastExit}\"");
                // Catalog Phase B: the clock in seconds (phases are per loop, below); fgPhase = the fixed-grip phase the
                // former "phase=" printed.
                sb.AppendLine($"  timeS={m_DiagLastTimeS:F2} fgPhase={m_DiagLastTimeS * SkiCatalog.FixedGrip.SpeedMps:F2} " +
                              $"lastMoveTimeS={m_LastMoveTimeS:F2} boundsTimeS={m_LastBoundsTimeS:F2} maxLoopSpeed={m_MaxLoopSpeedMps:F2} " +
                              $"mixedLoops={m_DiagMixedLoops} shuttleCarriers={m_DiagShuttleCarriers} typeMismatch={m_DiagTypeMismatch} " +
                              $"spacingWarned={m_SpacingWarned.Count} upLanesLogged={m_UpLaneLogged.Count}");
                sb.AppendLine($"  " +
                              $"passesWithMoves={m_DiagPassesWithMoves} pausedSkips={m_DiagPausedSkips} " +
                              $"lastPass moved={m_DiagLastMoved} inPlace={m_DiagLastInPlace} missing={m_DiagLastMissing} riding={m_DiagLastRiding}");
                sb.AppendLine($"  cache dirty={m_LoopCacheDirty} cooldown={m_RebuildCooldown} retries={m_EmptyRosterRetries} " +
                              $"rebuilds={m_DiagRebuilds} lastRebuildFrame={m_DiagLastRebuildFrame} " +
                              $"lanesMatched={m_DiagLanesMatched} lanesTotal={m_DiagLaneTotal} stations={m_DiagStations} " +
                              $"chairsMatched={m_DiagChairsMatched} enrolled={m_DiagEnrolled} staticFallback={m_StaticFallbackChairs} " +
                              $"hadLanes={m_LastBuildHadLanes}");
                sb.AppendLine($"  chains={m_LoopCache.m_ChainCount} unclosed={m_LoopCache.m_UnclosedChainCount} loops={m_LoopCache.m_Loops.Count} " +
                              $"signature=\"{m_LastBuildSignature ?? "-"}\"");
                sb.AppendLine($"  static: fullPassPending={m_FullPassPending} label={m_PendingPassLabel} retries={m_FullPassRetries} " +
                              $"updateAlive={m_UpdateAliveLogged} zeroMatch={m_ZeroMatchLogged} watchdog={m_WatchdogCountdown}");
                sb.AppendLine($"  L1: loopGeneration={LoopGeneration} immediateRequests={m_DiagImmediateRequests} " +
                              $"immediateBypassed={m_DiagImmediateRebuilds} lastImmediate=\"{m_ImmediateWhy}\" " +
                              $"seededLoops={m_DiagSeededLoops} seededChairs={m_DiagSeededChairs} gridLoops={m_DiagGridLoops}");
                for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
                {
                    LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                    // Catalog Phase B: the loop's type, speed and phase, and the up-lane rule's verdict (addendum 1).
                    LiftTypeDef loopDef = SkiCatalog.LiftByIndex(loop.m_TypeIndex);
                    sb.AppendLine($"  loop {l} stations: {Ent(loop.m_BaseStation)} ↔ {Ent(loop.m_TopStation)}; type " +
                                  $"{(loopDef != null ? loopDef.Id : "mixed (not moving)")}, {loop.m_SpeedMps:F2} m/s, phase " +
                                  $"{PhaseOf(loop, m_LastMoveTimeS):F2} m; up lane right looking uphill " +
                                  $"({(loop.m_UpLaneDecided ? loop.m_UpLaneSwapped ? "swapped" : "as found" : "undecided")})");
                }
                // Phase D (round-4-contract §4.3): the power hold — the gate's inputs per station pair, as of the last poll.
                LiftCensusSystem holdCensus = World.GetExistingSystemManaged<LiftCensusSystem>();
                sb.AppendLine($"  hold: game={m_HoldInGame} editor={m_HoldInEditor} census={(holdCensus == null ? "not registered" : holdCensus.IsArmed ? $"armed, {holdCensus.Builds} build(s)" : "not armed")} " +
                              $"armLogged={m_HoldArmLogged} censusWarned={m_HoldCensusWarned} polls={m_DiagHoldPolls} powerReads={m_DiagHoldPowerReads} " +
                              $"lastPollMs={m_DiagHoldPollMs:F3} lastPollFrame={(m_HoldPollFrame == uint.MaxValue ? "-" : m_HoldPollFrame.ToString())} " +
                              $"changes={m_DiagHoldChanges} heldWrites={m_DiagHeldWrites} dropped={m_DiagHoldDropped} held={m_HeldLoopCount} loop(s) / " +
                              $"{m_HeldChairCount} chair(s); rows by pair={m_HoldRows.Count} (census revision {m_HoldRowsRevision}); " +
                              $"guard {LiftServedRule.PowerBackHoldFrames} sim frames; last change \"{m_HoldLastChange}\"");
                foreach (KeyValuePair<(Entity, Entity), LoopHold> kv in m_Holds)
                {
                    LoopHold h = kv.Value;
                    sb.AppendLine($"    pair {Ent(kv.Key.Item1)} ↔ {Ent(kv.Key.Item2)} (loop base {Ent(h.Base)} → top {Ent(h.Top)}): " +
                                  $"{(h.Held ? $"Held since {h.HeldSinceS:F1} s, frozen at {h.FrozenTimeS:F1} s" : "Running")}; " +
                                  $"census row {(h.RowKnown ? h.RowState.ToString() : "none")}, base read {(h.RowBase == Entity.Null ? "-" : Ent(h.RowBase))}, " +
                                  $"power {h.Power}; in the cache {YN(h.Seen)}");
                }
                if (m_LastError != null)
                    sb.AppendLine("  lastError=" + m_LastError);

                // Live query census (a button press, not a per-frame getter).
                sb.AppendLine($"  queries now: allChairQuery={m_AllChairQuery.CalculateEntityCount()} " +
                              $"(prefab-matched {CountMatchingChairs(m_AllChairQuery)}), " +
                              $"updatedChairQuery={m_UpdatedChairQuery.CalculateEntityCount()}, " +
                              $"tempChairQuery={m_TempChairQuery.CalculateEntityCount()}, " +
                              $"laneQuery={m_LaneQuery.CalculateEntityCount()}, stationQuery={m_StationQuery.CalculateEntityCount()}");

                // Topology census (entry 102): how each chain terminated and why pairs did
                // or did not close — the fork the first dump could not resolve.
                for (int s = 0; s < m_DiagStationLines.Count; s++)
                    sb.AppendLine("  " + m_DiagStationLines[s]);
                for (int c = 0; c < m_LoopCache.m_ChainDiags.Count; c++)
                {
                    var d = m_LoopCache.m_ChainDiags[c];
                    sb.AppendLine($"  chain {c}: {d.m_Links} link(s); start owner {d.m_StartOwner} → station {d.m_StartStation} " +
                                  $"[{d.m_StartHow}] at {Vec(d.m_StartPos)}; end owner {d.m_EndOwner} → station {d.m_EndStation} " +
                                  $"[{d.m_EndHow}] at {Vec(d.m_EndPos)}");
                }
                for (int n = 0; n < m_LoopCache.m_BuildNotes.Count; n++)
                    sb.AppendLine("  build: " + m_LoopCache.m_BuildNotes[n]);

                int written = 0;
                for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
                {
                    LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                    int arcs = 0;
                    for (int s = 0; s < loop.m_Segments.Count; s++)
                        if (loop.m_Segments[s].m_IsArc) arcs++;
                    // Phase D (round-4-contract §4.3): the loop's hold state.
                    LoopHold loopHold = l < m_HoldByLoop.Count ? m_HoldByLoop[l] : null;
                    string holdState = loopHold != null && loopHold.Held
                        ? $"Held({(loopHold.ForPower ? "no power" : "closed")}, frozen at {loopHold.FrozenTimeS:F1} s, " +
                          $"phase {FrozenPlace(loop, loopHold.FrozenTimeS):F1} m)"
                        : "Running";
                    sb.AppendLine($"  loop {l}: length={loop.m_Length:F1} m segments={loop.m_Segments.Count} (arcs {arcs}) " +
                                  $"members={loop.m_MemberOwners.Count} chairs={loop.m_Chairs.Count} offsets={loop.m_Offsets.Count} " +
                                  $"state={holdState}");
                    for (int i = 0; i < loop.m_Chairs.Count && written < DebugDumpChairCap; i++, written++)
                    {
                        var slot = new ChairSlot
                        {
                            m_Chair = loop.m_Chairs[i],
                            m_LoopIndex = l,
                            m_Offset = i < loop.m_Offsets.Count ? loop.m_Offsets[i] : 0.0,
                            m_GripTop = loop.m_GripTops[i],
                        };
                        sb.AppendLine($"    chair {Ent(slot.m_Chair)}: {DescribeChair(slot)}");
                    }
                }
                if (written >= DebugDumpChairCap)
                    sb.AppendLine($"    … (chair list capped at {DebugDumpChairCap})");

                // Catalog Phase C (round-3-contract §4.10): the tram shuttle mover's block.
                var tramSystem = World.GetExistingSystemManaged<TramShuttleSystem>();
                if (tramSystem != null)
                    tramSystem.AppendDump(sb);
                else
                    sb.AppendLine("  tram shuttle: TramShuttleSystem not registered");
            }
            catch (Exception e)
            {
                sb.AppendLine("  dump aborted: " + e);
            }
            Mod.Log.Info(sb.ToString().TrimEnd());
        }

        private static DebugUI.Value Val(string name, Func<object> getter)
        {
            return new DebugUI.Value
            {
                displayName = name,
                getter = () =>
                {
                    try { return getter(); }
                    catch (Exception e) { return "err: " + FirstLine(e.ToString()); }
                },
            };
        }

        private static string Ent(Entity e) => $"{e.Index}:{e.Version}";
        private static string YN(bool b) => b ? "Y" : "N";
        private static string Vec(float3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";

        private static string FirstLine(string s)
        {
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl).TrimEnd('\r');
        }
    }
}
