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
            list.Add(Val("Chairlift net prefab", () =>
                m_ChairliftPrefab != Entity.Null ? Ent(m_ChairliftPrefab) + " (SkiChairliftHV)"
                : m_ChairliftPrefabResolved ? "NOT FOUND — moving mode inactive"
                : "unresolved (no game loaded yet)"));
            list.Add(Val("Chair prefabs resolved", () => m_ChairPrefabs.Count));
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
            moving.children.Add(Val("Derived phase (m), last pass", () => $"{m_DiagLastPhase:F2}"));
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
            list.Add(moving);

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
                        double s = (slot.m_Offset + m_LastMovePhase) % loop.m_Length;
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
                sb.AppendLine($"  mode={(MovingChairs ? "moving" : "static")} enabled={Enabled} disabled={m_Disabled} " +
                              $"chairPrefabs={m_ChairPrefabs.Count} chairliftPrefab={(m_ChairliftPrefab == Entity.Null ? "NULL" : Ent(m_ChairliftPrefab))} " +
                              $"resolvedAttempted={m_ChairliftPrefabResolved}");
                sb.AppendLine($"  onUpdateCalls={m_DiagUpdateCalls} lastUpdateFrame={m_DiagLastUpdateFrame} " +
                              $"renderFrame={m_RenderingSystem.frameIndex} frameTime={m_RenderingSystem.frameTime:F3} " +
                              $"lastExit=\"{m_DiagLastExit}\"");
                sb.AppendLine($"  phase={m_DiagLastPhase:F2} lastMovePhase={m_LastMovePhase:F2} boundsPhase={m_LastBoundsPhase:F2} " +
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
                if (m_LastError != null)
                    sb.AppendLine("  lastError=" + m_LastError);

                // Live query census (a button press, not a per-frame getter).
                sb.AppendLine($"  queries now: allChairQuery={m_AllChairQuery.CalculateEntityCount()} " +
                              $"(prefab-matched {CountMatchingChairs(m_AllChairQuery)}), " +
                              $"updatedChairQuery={m_UpdatedChairQuery.CalculateEntityCount()}, " +
                              $"tempChairQuery={m_TempChairQuery.CalculateEntityCount()}, " +
                              $"laneQuery={m_LaneQuery.CalculateEntityCount()}, stationQuery={m_StationQuery.CalculateEntityCount()}");

                int written = 0;
                for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
                {
                    LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                    int arcs = 0;
                    for (int s = 0; s < loop.m_Segments.Count; s++)
                        if (loop.m_Segments[s].m_IsArc) arcs++;
                    sb.AppendLine($"  loop {l}: length={loop.m_Length:F1} m segments={loop.m_Segments.Count} (arcs {arcs}) " +
                                  $"members={loop.m_MemberOwners.Count} chairs={loop.m_Chairs.Count} offsets={loop.m_Offsets.Count}");
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
