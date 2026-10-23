// The "Ski Resort" tab of the game's developer menu, a partial of ChairPinSystem so it reads the mover's private state.
// DebugSystem.RegisterDebug sweeps all loaded non-Unity assemblies on every menu open for [DebugContainer] classes and
// invokes each [DebugTab] method (decompile 1.6.2:1025-1044) on the live system instance (:1005-1009), so nothing is
// registered in Mod.cs. The widget tree is published as the `debug.children` binding and every Button is a
// `debug.invoke`, so the gameface MCP (localhost:9444, --uiDeveloperMode) can read and drive it.
// Cost: DebugUI.Value getters run at 10 Hz while the tab is open, so rows read plain counters only (no allocation,
// jobs or EntityQueries); anything heavier is a Button that writes to the mod log.
// Other partials add foldouts through AddDebugWidgets: DebugSystem.AddPanel replaces the widget list stored under a
// tab name (Game.Debug/DebugSystem.cs:1085-1095), so a second "Ski Resort" tab would replace these rows.
// Systems are fetched with GetExistingSystemManaged, so an unregistered one shows "not registered" instead of being created.

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

            list.Add(Val("Mode", () => MovingChairs ? "moving (milestone 2)" : "static (milestone 1)"));
            // "unresolved" = no game / editor load yet (the catalog resolves per load)
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
            list.Add(Val("Last OnUpdate exit", () => LastExitText()));
            list.Add(Val("Last error", () => m_LastError == null ? "-" : FirstLine(m_LastError)));

            var moving = new DebugUI.Foldout { displayName = "Moving mode", opened = true };
            moving.children.Add(Val("Render clock frameIndex / frameTime", () =>
                $"{m_RenderingSystem.frameIndex} / {m_RenderingSystem.frameTime:F3}"));
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
            moving.children.Add(Val("Held loops (no power) / chairs held / power reads / last poll ms", () =>
                $"{m_HeldLoopCount} / {m_HeldChairCount} / {m_DiagHoldPowerReads} / {m_DiagHoldPollMs:F3}"));
            moving.children.Add(Val("Last hold change", () => m_HoldLastChange));
            list.Add(moving);

            // Mover at scale (docs/research/lifts/lift-scale-2026-10-22.md, ChairPinSystem.Mover.cs header). Plain fields only
            // (the 10 Hz cost contract); the percentile row sorts <= 600 floats into a reused list.
            var scale = new DebugUI.Foldout { displayName = "Mover scale (R-3 / R-4)", opened = false };
            scale.children.Add(Val("Path / Burst-compiled / stress / sync-measure", () =>
                $"{m_DiagMoverPath} / {m_DiagBurst} / x{s_StressFactor} / {(s_SyncMeasure ? "on" : "off")}"));
            scale.children.Add(Val("Far-loop pace", () => s_Pace.Enabled
                ? $"on: >= {s_Pace.FarMinM:F0} m, stride <= {s_Pace.MaxStride}, step <= {s_Pace.MaxStepRadians * 1000.0:F1} mrad"
                : "off (every loop every pass)"));
            scale.children.Add(Val("Last pass: riding / written / loops near / far / bounds tags", () =>
                $"{m_PassRiding} / {m_PassWritten} / {m_PassNear} / {m_PassFar} / {m_PassTags}"));
            scale.children.Add(Val("Main thread p50 / p95 / max (window)", () => MoverCostShort()));
            scale.children.Add(Val("Bounds Updated tags / s (window)", () =>
            {
                float seconds = m_TagWindowStart >= 0f ? UnityEngine.Time.realtimeSinceStartup - m_TagWindowStart : 0f;
                return seconds > 0f ? $"{m_TagTotal / seconds:F0} ({m_TagTotal} in {seconds:F1} s)" : "-";
            }));
            scale.children.Add(Val("Lane harvest: mode / owners / SubLane entries / ms / city utility lanes", () =>
                $"{m_DiagHarvestMode} / {m_DiagHarvestOwners} / {m_DiagHarvestVisited} / {m_DiagHarvestMs:F3} / {m_DiagLaneTotal}"));
            scale.children.Add(new DebugUI.Button
            {
                displayName = s_MoverUseJob ? "Mover: switch to the MANAGED path (A/B)" : "Mover: switch to the BURST job",
                action = () =>
                {
                    s_MoverUseJob = !s_MoverUseJob;
                    ResetMoverCostWindow();
                    Mod.Log.Info($"{nameof(ChairPinSystem)} [debug tab]: mover path = {(s_MoverUseJob ? "Burst job" : "managed")}");
                    DebugSystem.Rebuild(BuildSkiResortDebugUI);
                }
            });
            scale.children.Add(new DebugUI.Button
            {
                displayName = s_Pace.Enabled ? "Far-loop pace: turn OFF" : "Far-loop pace: turn ON",
                action = () =>
                {
                    s_Pace.Enabled = !s_Pace.Enabled;
                    ResetMoverCostWindow();
                    Mod.Log.Info($"{nameof(ChairPinSystem)} [debug tab]: far-loop pace {(s_Pace.Enabled ? "on" : "off")}");
                    DebugSystem.Rebuild(BuildSkiResortDebugUI);
                }
            });
            scale.children.Add(new DebugUI.Button
            {
                displayName = $"Far-loop pace: min distance {s_Pace.FarMinM:F0} m (cycle 250 / 500 / 1000 / 2000)",
                action = () =>
                {
                    s_Pace.FarMinM = s_Pace.FarMinM < 250f ? 250f : s_Pace.FarMinM < 500f ? 500f
                                   : s_Pace.FarMinM < 1000f ? 1000f : s_Pace.FarMinM < 2000f ? 2000f : 250f;
                    ResetMoverCostWindow();
                    Mod.Log.Info($"{nameof(ChairPinSystem)} [debug tab]: far-loop pace min distance {s_Pace.FarMinM:F0} m");
                    DebugSystem.Rebuild(BuildSkiResortDebugUI);
                }
            });
            scale.children.Add(new DebugUI.Button
            {
                displayName = $"Stress: x{s_StressFactor} (cycle 1 / 4 / 16)",
                action = () =>
                {
                    s_StressFactor = s_StressFactor <= 1 ? 4 : s_StressFactor <= 4 ? 16 : 1;
                    ResetMoverCostWindow();
                    Mod.Log.Info($"{nameof(ChairPinSystem)} [debug tab]: stress x{s_StressFactor} (the move work runs {s_StressFactor} " +
                                 "time(s) per pass; bounds tags are not multiplied)");
                    DebugSystem.Rebuild(BuildSkiResortDebugUI);
                }
            });
            scale.children.Add(new DebugUI.Button
            {
                displayName = s_SyncMeasure ? "Sync-measure: turn OFF" : "Sync-measure: turn ON (the job is timed on the main thread)",
                action = () =>
                {
                    s_SyncMeasure = !s_SyncMeasure;
                    ResetMoverCostWindow();
                    Mod.Log.Info($"{nameof(ChairPinSystem)} [debug tab]: sync-measure {(s_SyncMeasure ? "on" : "off")}");
                    DebugSystem.Rebuild(BuildSkiResortDebugUI);
                }
            });
            scale.children.Add(new DebugUI.Button
            {
                displayName = "Reset the cost window",
                action = () => ResetMoverCostWindow()
            });
            scale.children.Add(new DebugUI.Button
            {
                displayName = "Log mover cost now (mod log)",
                action = () => Mod.Log.Info(MoverCostLine("debug tab, current window"))
            });
            list.Add(scale);

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

            // Tram foldout, built by TramShuttleSystem.Debug.cs
            var tram = World.GetExistingSystemManaged<TramShuttleSystem>();
            if (tram != null)
                tram.AddDebugWidgets(list);
            else
                list.Add(Val("Tram shuttle", () => "not registered"));

            var stat = new DebugUI.Foldout { displayName = "Static mode", opened = false };
            stat.children.Add(Val("Full pass pending / label / retries", () =>
                $"{m_FullPassPending} / {m_PendingPassLabel} / {m_FullPassRetries}"));
            stat.children.Add(Val("Update path alive / zero-match logged", () =>
                $"{m_UpdateAliveLogged} / {m_ZeroMatchLogged}"));
            stat.children.Add(Val("Watchdog countdown", () => m_WatchdogCountdown));
            list.Add(stat);

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
                        // The loop's own phase (speed × the last mover clock); a held loop stands at its frozen clock
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

        /// <summary>Everything the tab shows plus the full roster and loop geometry, as one block in the mod log.</summary>
        internal void DumpDiagnostics(string reason)
        {
            var sb = new StringBuilder(4096);
            try
            {
                sb.AppendLine($"{nameof(ChairPinSystem)} [dump: {reason}] ────────────────────────────");
                // The catalog's views; catalogLoad / catalogGeneration say which load's resolution these are
                Entity chairlift = ChairliftPrefab;
                sb.AppendLine($"  mode={(MovingChairs ? "moving" : "static")} enabled={Enabled} disabled={m_Disabled} " +
                              $"chairPrefabs={m_PrefabCatalog.CarrierGripTops.Count} chairliftPrefab={(chairlift == Entity.Null ? "NULL" : Ent(chairlift))} " +
                              $"catalogLoad={m_PrefabCatalog.Loads} catalogGeneration={m_PrefabCatalog.Generation}");
                sb.AppendLine("  " + m_PrefabCatalog.Describe());
                sb.AppendLine($"  onUpdateCalls={m_DiagUpdateCalls} lastUpdateFrame={m_DiagLastUpdateFrame} " +
                              $"renderFrame={m_RenderingSystem.frameIndex} frameTime={m_RenderingSystem.frameTime:F3} " +
                              $"lastExit=\"{LastExitText()}\"");
                // Clock in seconds (phases are per loop, below); fgPhase is the fixed-grip phase
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
                    // The loop's type, speed and phase, and the up-lane rule's verdict
                    LiftTypeDef loopDef = SkiCatalog.LiftByIndex(loop.m_TypeIndex);
                    sb.AppendLine($"  loop {l} stations: {Ent(loop.m_BaseStation)} ↔ {Ent(loop.m_TopStation)}; type " +
                                  $"{(loopDef != null ? loopDef.Id : "mixed (not moving)")}, {loop.m_SpeedMps:F2} m/s, phase " +
                                  $"{PhaseOf(loop, m_LastMoveTimeS):F2} m; up lane right looking uphill " +
                                  $"({(loop.m_UpLaneDecided ? loop.m_UpLaneSwapped ? "swapped" : "as found" : "undecided")})");
                }
                // Power hold: the gate's inputs per station pair, as of the last poll
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

                // Topology census: how each chain terminated and why pairs did or did not close
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
                    // The loop's hold state
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

                // Tram shuttle mover's block
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
