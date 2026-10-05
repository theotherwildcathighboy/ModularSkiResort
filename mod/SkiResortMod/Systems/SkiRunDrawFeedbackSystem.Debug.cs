// SkiRunDrawFeedbackSystem.Debug — the "Ski Runs" tab of the game's developer menu
// (docs/research/run-feedback-spec.md §4 "Dev tab" + §9.5 R2 rows; the SkiGladeToolSystem.Debug.cs
// pattern). R2 adds (DECISIONS 116): crux % (the rating), steepest patch %, P90 %, connected, crux
// cell, end-zone cells, heights reused / sampled, throttle window / ms until the next move
// recompute, pre-warm ms, last capture path; "Dump to log" now also writes the .skislope capture
// (SkiRunDrawFeedbackSystem.WriteCapture — the button is the only file-IO path). The R2 fixer
// round adds the first draw of the process (kept out of the draw peak; see Draw). R2b (§9.8)
// adds the end-zone key (fall line / height), the diagonal rule (step multiple >= 2) and the
// constants EndZoneDepthM / EndZoneMinMeanGrade; the R2b fixer round the key gate's inputs
// (|mean g|, coherence) and EndZoneMinCoherence. R3 (docs/research/run-binding-glade-spec.md
// §5.1, §5.3) adds the run length (Length / LengthRect / AxisLength) and the wall-clock heartbeat
// (HeartbeatSeconds, last verify age, verifies, changes found, verify ms) — field test step 18:
// "hold a run still for 10 s: recomputes stay flat, verifies grow". The "Dump to log" button is
// now one of two dump paths (the other: the dump hotkey, SkiGladeHotkeySystem). F7
// (docs/research/field-test-7-fixes.md C2) adds one row: the game's error / warning types on the
// run being drawn at the last recompute (ValidationText, a field written on a change only).
//
// HOW IT REACHES THE SCREEN. DebugSystem.RegisterDebug sweeps every [DebugContainer] type and
// invokes each [DebugTab] method (DebugSystem.cs:1026-1045). A tab method declared on a
// ComponentSystemBase runs on the LIVE system instance (ExecuteMethod, DebugSystem.cs:996-1024;
// GetOrCreateSystemManaged at :1006-1009), which is why this file is a partial of the system.
// Nothing is registered in Mod.cs for the tab.
//
// TAB NAME IS LOAD-BEARING: "Ski Runs", NOT "Ski Resort" (ChairPinSystem.Debug.cs) or "Ski
// Glades" (SkiGladeToolSystem.Debug.cs). AddPanel keys widget lists by tab name and REMOVES the
// previously registered list under the same name (DebugSystem.cs:1085-1095; same body at the same
// lines in 1.6.2, Rebuild at :971 likewise).
//
// COST CONTRACT: DebugUI.Value getters run at 10 Hz while the tab is open. Every row reads a plain
// field of this system, of SkiRunContourSystem (StateText) or of SkiRunBindTriggerSystem
// (LastReason, Triggers): no EntityQuery, no job completion, no allocation beyond the display
// string. The two sibling systems are resolved once per tab build with
// World.GetExistingSystemManaged (null when not registered — the rows then say so). Heavier work
// (DumpToLog) runs on a button press and goes to the mod log plus the capture file.

using System;
using System.Collections.Generic;
using Game.Debug;
using Unity.Entities;
using UnityEngine.Rendering;

namespace SkiResortMod.Systems
{
    [DebugContainer]
    public sealed partial class SkiRunDrawFeedbackSystem
    {
        [DebugTab("Ski Runs", 2)]
        private List<DebugUI.Widget> BuildSkiRunsDebugUI()
        {
            var list = new List<DebugUI.Widget>();

            // ── State (plain-field getters only) ─────────────────────────────────────────────
            list.Add(Val("Armed / disabled", () => $"{IsArmed} / {IsDisabled}"));
            list.Add(Val("Area tool active", () => AreaToolActive));
            list.Add(Val("Eligible Temp (idx:ver, nodes, new/edit, complete)", () =>
                EligibleTemp == Entity.Null
                    ? "none"
                    : $"{RunDebugEnt(EligibleTemp)}, {EligibleNodes} nodes, " +
                      $"{(EligibleIsNew ? "new" : "edit")}, {(EligibleComplete ? "complete" : "open")}"));
            list.Add(Val("Temp candidates (ours, >= 2 nodes)", () => TempCandidates));
            // F7 C2: the game's error / warning types on that Temp at the last recompute (a field read).
            list.Add(Val("Game validation (last recompute), changes", () => $"{ValidationText ?? "-"}, {ValidationChanges}"));
            list.Add(Val("Note", () => LastNote ?? "-"));
            list.Add(Val("Status", () => LastStatus.ToString()));
            list.Add(Val("Band (shown / raw)", () => $"{CurrentReadout.Band} / {RawBand}"));
            list.Add(Val("Crux % (rating: hardest unavoidable pitch)", () => (CurrentReadout.RatingGrade * 100f).ToString("F1")));
            list.Add(Val("Steepest patch % (avoidable or not)", () => (CurrentReadout.SteepestGrade * 100f).ToString("F1")));
            list.Add(Val("P90 %", () => (P90Grade * 100f).ToString("F1")));
            list.Add(Val("Connected", () => CurrentReadout.Connected));
            list.Add(Val("Crux cell (i, j)", () => CruxValid ? $"{CruxI}, {CruxJ}" : "none"));
            list.Add(Val("End-zone cells (top / bottom)", () => $"{TopZoneCells} / {BottomZoneCells}"));
            // R2b (spec §9.8): zone key, connectivity rule, and the new constants; the R2b fixer
            // round adds the key gate's inputs and its coherence clause.
            list.Add(Val("End-zone key (fall line / height)", () => ZoneByFallLine ? "fall line" : "height"));
            list.Add(Val("Key gate inputs (|mean g| % / coherence)", () =>
                $"{ZoneMeanGradient * 100f:F2} % / {ZoneFallCoherence:F3}"));
            list.Add(Val("Diagonal rule (step multiple >= 2)", () => DiagonalRule));
            list.Add(Val("EndZoneDepthM / EndZoneMinMeanGrade / EndZoneMinCoherence", () =>
                $"{SkiSlopeClassifier.EndZoneDepthM:F0} m / {SkiSlopeClassifier.EndZoneMinMeanGrade * 100f:F1} % / {SkiSlopeClassifier.EndZoneMinCoherence:F2}"));
            list.Add(Val("Mean %", () => (CurrentReadout.MeanGrade * 100f).ToString("F1")));
            list.Add(Val("Drop m", () => CurrentReadout.Drop.ToString("F1")));
            // R3 (§5.1): horizontal run length = max(equivalent rectangle, fall-line extent).
            list.Add(Val("Length m (rect / axis)", () =>
                $"{CurrentReadout.Length:F1} ({LengthRect:F1} / {AxisLength:F1})"));
            list.Add(Val("Readout (live / valid)", () => $"{ReadoutLive} / {CurrentReadout.Valid}"));
            list.Add(Val("Step multiple (cell m)", () => $"{StepMultiple} ({StepM:F1} m)"));
            list.Add(Val("Inside / need cells", () => $"{InsideCells} / {NeedCells} ({LatticeNx}x{LatticeNz})"));
            list.Add(Val("Recompute ms (plan / fill / classify)", () =>
                $"{LastPlanMs:F3} / {LastFillMs:F3} / {LastClassifyMs:F3}"));
            list.Add(Val("Recompute ms (strips / total / peak)", () =>
                $"{LastStripMs:F3} / {LastRecomputeMs:F3} / {PeakRecomputeMs:F3}"));
            list.Add(Val("Recomputes", () => Recomputes));
            // R3 (§5.3): wall-clock heartbeat — a held run is re-verified, not re-rated.
            list.Add(Val("Heartbeat s / last verify age s", () =>
            {
                double age = UnityEngine.Time.realtimeSinceStartupAsDouble - GoodVerifiedAt;
                string ageText = NoRatingSinceLoad || double.IsInfinity(age) || double.IsNaN(age) ? "-" : age.ToString("F1");
                return $"{Tuning.HeartbeatSeconds:F1} / {ageText}";
            }));
            list.Add(Val("Verifies / changes found", () => $"{Verifies} / {VerifyChanges}"));
            list.Add(Val("Verify ms (last / peak), nodes last frame", () =>
                $"{LastVerifyMs:F3} / {PeakVerifyMs:F3}, {VerifyNodesLast} (max {Tuning.VerifyNodesPerFrame})"));
            list.Add(Val("Heights reused / sampled", () => $"{HeightsReused} / {HeightsSampled}"));
            list.Add(Val("Throttle ms (window / until next move recompute)", () =>
                $"{ThrottleWindowMs:F0} / {ThrottleRemainingMs:F0}"));
            list.Add(Val("Throttled move frames", () => ThrottledFrames));
            list.Add(Val("Pre-warm ms", () => PrewarmMs.ToString("F2")));
            list.Add(Val("Strips (drawn / built, cap hit)", () => $"{StripsDrawn} / {StripsBuilt}, {StripCapHit}"));
            list.Add(Val("Row stride", () => RowStride));
            list.Add(Val("Draw ms, schedule only (last / peak)", () => $"{LastDrawMs:F3} / {PeakDrawMs:F3}"));
            list.Add(Val("First draw ms of the process (excluded from peak)", () => FirstDrawMs.ToString("F3")));
            list.Add(Val("Gate in range", () => CurrentReadout.GateInRange));
            list.Add(Val("Last capture path", () => LastCapturePath ?? "-"));
            list.Add(Val("Last error", () => LastError == null ? "-" : RunDebugFirstLine(LastError)));

            // Sibling systems (F3 / F4), resolved once per build, read field-only.
            SkiRunContourSystem contour = World.GetExistingSystemManaged<SkiRunContourSystem>();
            list.Add(Val("Contour state", () =>
                contour == null ? "contour system not registered" : (contour.StateText ?? "-")));
            SkiRunBindTriggerSystem bind = World.GetExistingSystemManaged<SkiRunBindTriggerSystem>();
            list.Add(Val("Bind trigger (last reason, count)", () =>
                bind == null ? "bind trigger not registered" : $"{bind.LastReason ?? "-"}, {bind.Triggers}"));

            // ── Actions ─────────────────────────────────────────────────────────────────────
            list.Add(new DebugUI.Button
            {
                // Log text + .skislope capture of the last rated polygon (survives closing the
                // tool; spec §9.5 "Capture"). Pressing it moves the cursor off the map, so the
                // live Temp is gone — use the dump hotkey (default Ctrl+Alt+G, R3 §5.2) to dump
                // with a run on the cursor.
                displayName = "Dump to log",
                action = () => RunDebugAction("dump", DumpToLog)
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Refresh",
                action = () => DebugSystem.Rebuild(BuildSkiRunsDebugUI)   // DebugSystem.cs:971-974
            });

            return list;
        }

        // ── helpers (prefixed so they cannot collide with the main partial) ─────────────────

        /// <summary>Button bodies run inside the debug UI's invoke path. A throw there must not
        /// escape into the menu, and must not be silent either.</summary>
        private static void RunDebugAction(string label, Action body)
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"RunFeedback: [debug tab] '{label}' failed:\n{e}");
            }
        }

        /// <summary>Copied from ChairPinSystem.Debug.cs:323-334 (via SkiGladeToolSystem.Debug.cs):
        /// a getter that cannot throw into the 10 Hz refresh.</summary>
        private static DebugUI.Value Val(string name, Func<object> getter)
        {
            return new DebugUI.Value
            {
                displayName = name,
                getter = () =>
                {
                    try { return getter(); }
                    catch (Exception e) { return "err: " + RunDebugFirstLine(e.ToString()); }
                },
            };
        }

        private static string RunDebugEnt(Entity e) => $"{e.Index}:{e.Version}";

        private static string RunDebugFirstLine(string s)
        {
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl).TrimEnd('\r');
        }
    }
}
