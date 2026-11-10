// The hub's side of the Resort Planner tool (code name Quick runs, contract 17 plus the v2 plan fields, bumped at the merge to main).
// It has the `quickRuns` value binding (bindings.ts QuickRunsState, written here from the tool's fields). The two triggers are
// quickRunsToggle (open / close, the glade toggle's pattern) and quickRunsSet(key, value) (every knob, button and tree setting:
// QuickRunsToolSystem.HubSet). The system pushes the binding only while the panel observes it, when the tool's HubRevision,
// availability or activity moved (the ArrayPush idea for one struct).
// Resort Planner v2 (stage A, task A7): the Lifts / Way home knobs (liftsKnob, home) and, while a plan is showing, its pods, lifts and
// summary (pods, planLifts, plan). Every one is empty or zero without a plan, so a Region step writes the same binding as before plus
// those empty fields.
// Contract 41 (Resort Planner round, task R1-d): the `sheet` field of the binding (written inside WriteQuickRuns, rule R2: the plan sheet's rows, the lift queue's
// progress, the lifts total, a notice) and the quickLiftsAct(action, key) trigger (buildAll, build, stop, clear: ResortLiftBuildSystem.HubAct).
// The rows come from the sheet, not from the planner's preview, so they stay after the run build and with the tool closed.
// Engine notes: EngineNotes/SkiResortHubUISystem.md (Quick runs)

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.UI.Binding;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiResortHubUISystem
    {
        private QuickRunsToolSystem m_QuickRunsTool;
        private ResortLiftBuildSystem m_LiftBuild;   // the plan sheet and the lift build queue (resolved lazily, GetExisting)
        private RawValueBinding m_QuickRuns;
        private int m_QuickRunsKey = int.MinValue;
        private int m_LiftsSheetRev = -1, m_LiftsQueueRev = -1;
        private bool m_LiftsNotice;
        private bool m_QuickRunsWasActive;

        private void OnQuickRunsToggle() => Trigger("quickRunsToggle", new Request { Kind = RequestKind.QuickRunsToggle });

        private void OnQuickRunsSet(string key, int value) =>
            Trigger("quickRunsSet", new Request { Kind = RequestKind.QuickRunsSet, Text = key, Int = value });

        /// <summary>Contract 41: the plan sheet's buttons. key: "buildAll", "build" (value = the row's key, a signed int), "stop", "clear".</summary>
        private void OnQuickLiftsAct(string action, int value) =>
            Trigger("quickLiftsAct", new Request { Kind = RequestKind.QuickLiftsAct, Text = action, Int = value });

        private ResortLiftBuildSystem LiftBuildSys()
        {
            if (m_LiftBuild == null)
                m_LiftBuild = World.GetExistingSystemManaged<ResortLiftBuildSystem>();
            return m_LiftBuild;
        }

        /// <summary>The plan sheet's four calls go to the lift build queue (ResortLiftBuildSystem.HubAct), which refuses with a notice and a
        /// log line when it cannot take the call. The Resort Planner tool with a scan, a preview or a build in progress blocks them (a lift
        /// build would take the tool from the planner and drop its preview).</summary>
        private void ApplyQuickLiftsAct(string action, int value, string source)
        {
            ResortLiftBuildSystem lb = LiftBuildSys();
            string what;
            if (lb == null)
                what = "refused: the lift build system does not exist";
            else
            {
                QuickRunsToolSystem t = m_QuickRunsTool;
                bool plannerBusy = t.Phase != QuickRunsToolSystem.ToolPhase.Region || t.Planting;
                what = lb.HubAct(action ?? "", unchecked((uint)value), plannerBusy);
            }
            Mod.Log.Info($"Hub [quickLifts] {action} {value} — {what} ({source})");
            LastTriggerText = $"quickLiftsAct({action}, {value}) → {what}";
        }

        /// <summary>The glade toggle's pattern: Deactivate when active, else TryActivate (which owns the gating and logs its refusal: the
        /// option off, not armed, disabled).</summary>
        private void ApplyQuickRunsToggle(string source)
        {
            if (m_QuickRunsTool.IsActive)
            {
                Mod.Log.Info($"Hub [quickRuns] toggle — deactivating the Quick runs tool ({source})");
                m_QuickRunsTool.Deactivate();
                LastTriggerText = "quickRunsToggle: off";
                return;
            }
            bool ok = m_QuickRunsTool.TryActivate("hub" + SourceSuffix(source));
            Mod.Log.Info($"Hub [quickRuns] toggle — activate {(ok ? "ok" : "refused: " + m_QuickRunsTool.UnavailableReasonKey)} ({source})");
            LastTriggerText = $"quickRunsToggle: {(ok ? "on" : "refused: " + m_QuickRunsTool.UnavailableReasonKey)}";
        }

        private void ApplyQuickRunsSet(string key, int value, string source)
        {
            string what = m_QuickRunsTool.HubSet(key ?? "", value);
            Mod.Log.Info($"Hub [quickRuns] set {key}={value} — {what} ({source})");
            LastTriggerText = $"quickRunsSet({key}, {value}) → {what}";
        }

        /// <summary>PushPanelData: push when the tool's state moved or the binding was (re)subscribed.</summary>
        private void PushQuickRuns(bool resubscribed)
        {
            QuickRunsToolSystem t = m_QuickRunsTool;
            int key = t.HubRevision * 8 + (t.UnavailableReasonKey == null ? 1 : 0) + (t.IsActive ? 2 : 0) + (t.CanPlanAround ? 4 : 0);
            // The plan sheet and the lift queue (contract 41) move on their own revisions, and a notice ends by time.
            ResortLiftBuildSystem lb = LiftBuildSys();
            int sheetRev = lb != null ? lb.Sheet.Revision : -1, queueRev = lb != null ? lb.QueueRevision : -1;
            bool notice = lb != null && lb.Notice != null;
            if (!resubscribed && key == m_QuickRunsKey && sheetRev == m_LiftsSheetRev && queueRev == m_LiftsQueueRev && notice == m_LiftsNotice)
                return;
            m_QuickRunsKey = key;
            m_LiftsSheetRev = sheetRev;
            m_LiftsQueueRev = queueRev;
            m_LiftsNotice = notice;
            m_QuickRuns.Update();
        }

        private static readonly string[] s_QuickPhaseNames = System.Enum.GetNames(typeof(QuickRunsToolSystem.ToolPhase));
        private static readonly string[] s_QuickRegionNames = System.Enum.GetNames(typeof(QuickRegionState));

        /// <summary>bindings.ts QuickRunsState (rule R2: these PropertyName literals are its fields).</summary>
        private void WriteQuickRuns(IJsonWriter writer)
        {
            QuickRunsToolSystem t = m_QuickRunsTool;
            string reason = t.UnavailableReasonKey;
            bool active = t.IsActive;
            QuickRegionReadout ro = t.Readout;
            QuickTotals tot = t.Totals;
            AutoPlanKnobs k = t.Knobs;
            bool preview = t.HasPreview;
            writer.TypeBegin("SkiResortMod.QuickRunsState");
            writer.PropertyName("available");
            writer.Write(reason == null);
            writer.PropertyName("active");
            writer.Write(active);
            writer.PropertyName("reason");
            writer.Write(reason ?? "");
            writer.PropertyName("phase");
            writer.Write(s_QuickPhaseNames[(int)t.Phase]);
            writer.PropertyName("corners");
            writer.Write(t.Corners);
            // The Region step's "click a lift station" line is true only when the census knows a station. The census arms after the
            // tool opens, so this can flip without a hub revision. PushQuickRuns folds it into its key.
            writer.PropertyName("canPlanAround");
            writer.Write(t.CanPlanAround);
            writer.PropertyName("areaM2");
            writer.Write(Finite(ro.AreaM2));
            writer.PropertyName("maxAreaM2");
            writer.Write(t.MaxAreaM2);
            writer.PropertyName("verticalM");
            writer.Write(Finite(ro.VerticalM));
            writer.PropertyName("regionState");
            writer.Write(s_QuickRegionNames[(int)ro.State]);
            writer.PropertyName("progress");
            writer.Write(Finite(t.ScanProgress));
            writer.PropertyName("mix");
            writer.Write((int)k.Mix);
            writer.PropertyName("density");
            writer.Write((int)k.Density);
            writer.PropertyName("kinds");
            writer.Write((int)k.Kinds);
            writer.PropertyName("runs");
            writer.Write(preview ? tot.Runs : 0);
            writer.PropertyName("enabled");
            writer.Write(preview ? tot.Enabled : 0);
            writer.PropertyName("count");
            WriteInts(writer, tot.Count, preview);
            writer.PropertyName("target");
            WriteInts(writer, tot.Target, preview);
            writer.PropertyName("fee");
            writer.Write(preview ? (int)System.Math.Min(tot.Fee, int.MaxValue) : 0);
            writer.PropertyName("skiers");
            writer.Write(preview ? (int)System.Math.Round(tot.SkiersPerDay) : 0);
            writer.PropertyName("variety");
            writer.Write(preview && tot.Variety);
            writer.PropertyName("status");
            writer.Write(t.Phase == QuickRunsToolSystem.ToolPhase.Region ? t.StatusKey ?? "" : "");
            writer.PropertyName("warnings");
            IReadOnlyList<string> warn = t.WarningKeys;
            writer.ArrayBegin((uint)warn.Count);
            for (int i = 0; i < warn.Count; i++)
                writer.Write(warn[i]);
            writer.ArrayEnd();
            writer.PropertyName("clusters");
            AutoPlanResult r = t.Result;
            int clusters = preview ? r.Clusters.Count : 0;
            writer.ArrayBegin((uint)clusters);
            for (int i = 0; i < clusters; i++)
            {
                writer.TypeBegin("SkiResortMod.QuickRunsCluster");
                writer.PropertyName("label");
                writer.Write(QuickRunsToolSystem.ClusterKey(r.Clusters[i]));
                writer.PropertyName("runs");
                writer.Write(r.Clusters[i].Runs);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            writer.PropertyName("batchRuns");
            writer.Write(t.BatchRuns);
            writer.PropertyName("notice");
            writer.Write(t.Notice ?? "");
            writer.PropertyName("trees");
            writer.Write(t.TreesOn);
            writer.PropertyName("treeLevel");
            writer.Write((int)t.TreeLevel);
            writer.PropertyName("treeLine");
            float line = t.TreeLine;
            writer.Write(float.IsInfinity(line) || float.IsNaN(line) ? -1 : (int)System.Math.Round(line));
            writer.PropertyName("groundMin");
            writer.Write((int)System.Math.Floor(Finite(t.GroundMin)));
            writer.PropertyName("groundMax");
            writer.Write((int)System.Math.Ceiling(Finite(t.GroundMax)));
            writer.PropertyName("group");
            writer.Write(t.TreeGroupIndex);
            writer.PropertyName("species");
            writer.Write(t.SpeciesPick);
            IReadOnlyList<QuickRunsToolSystem.SpeciesEntry> species = t.Species;
            writer.PropertyName("speciesNames");
            writer.ArrayBegin((uint)species.Count);
            for (int i = 0; i < species.Count; i++)
                writer.Write(species[i].Name ?? "");
            writer.ArrayEnd();
            writer.PropertyName("speciesGroups");
            writer.ArrayBegin((uint)species.Count);
            for (int i = 0; i < species.Count; i++)
                writer.Write((int)species[i].Group);
            writer.ArrayEnd();
            writer.PropertyName("plant");
            writer.Write(t.TreeToPlant);
            writer.PropertyName("remove");
            writer.Write(t.TreeRemove);
            writer.PropertyName("treesUndo");
            writer.Write(t.TreeBatchCount);
            writer.PropertyName("buildDone");
            writer.Write(t.BuildDone);
            writer.PropertyName("buildTotal");
            writer.Write(t.Building ? t.BuildTotal : 0);
            writer.PropertyName("planting");
            writer.Write(t.Planting);
            // Undo trees reuses the progress pair with planting false ("Removing trees N / M"). Same shape, contract unchanged.
            writer.PropertyName("treesDone");
            writer.Write(t.RemovingTrees ? t.RemoveDone : t.PlantDone);
            writer.PropertyName("treesTotal");
            writer.Write(t.Planting ? t.PlantTotal : t.RemovingTrees ? t.RemoveTotal : 0);
            // What Plant will cost (contract 18): the ready preview's Temp cost per tree times the trees to plant. 0 without a preview
            writer.PropertyName("treesCost");
            writer.Write(t.TreeCost);
            // Why lines were dropped (contract 20): the scan's reject histogram in QuickRunsRules.RejectBuckets buckets, only when the
            // scan has a result (a preview, or the refusal in the Region step). Zeros otherwise
            AutoPlanResult scan = t.Result;
            bool scanned = scan.Candidates > 0 && (preview || (t.Phase == QuickRunsToolSystem.ToolPhase.Region && t.StatusKey != null));
            writer.PropertyName("rejects");
            WriteInts(writer, QuickRunsRules.RejectCounts(scan.Rejects), scanned);
            // The site verdict (contract 21): the whole percent and its tier (QuickRunsRules.SiteTier, 0 = unknown, no line)
            int sitePct = QuickRunsRules.SitePercent(t.SiteGrade);
            writer.PropertyName("sitePercent");
            writer.Write(sitePct);
            writer.PropertyName("siteTier");
            writer.Write((int)QuickRunsRules.SiteTierOf(sitePct));
            ResortPlan plan = t.Plan;   // Resort Planner v2 (A7)
            QuickPodRow[] rows = plan != null ? t.PodRows : new QuickPodRow[0];
            writer.PropertyName("liftsKnob");
            writer.Write(k.Lifts);
            writer.PropertyName("home");
            writer.Write((int)k.Home);
            writer.PropertyName("pods");
            writer.ArrayBegin((uint)rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                PlanPod pod = plan.Pods[i];
                QuickPodRow row = rows[i];
                writer.TypeBegin("SkiResortMod.QuickRunsPod");
                writer.PropertyName("id");
                writer.Write(pod.Id);
                writer.PropertyName("key");
                writer.Write(unchecked((int)pod.Key));
                writer.PropertyName("letter");
                writer.Write(QuickRunsPlanRules.Letter(pod.Id));
                writer.PropertyName("classKey");
                writer.Write(ClassKey(pod.PrimaryClass));
                writer.PropertyName("runs");
                writer.Write(row.Runs);
                writer.PropertyName("enabledRuns");
                writer.Write(row.Kept);
                writer.PropertyName("count");
                WriteInts(writer, row.Count, true);
                writer.PropertyName("skiers");
                writer.Write((int)System.Math.Round(row.Skiers));
                writer.PropertyName("fee");
                writer.Write((int)System.Math.Min(row.Fee, int.MaxValue));
                writer.PropertyName("on");
                writer.Write(row.On);
                bool served = pod.Lift >= 0 && pod.Lift < plan.Lifts.Length;
                writer.PropertyName("liftId");
                writer.Write(served ? plan.Lifts[pod.Lift].Id : -1);
                writer.PropertyName("homeKey");
                writer.Write(served ? VerdictKey(plan.Lifts[pod.Lift].Home) : "");
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            int lifts = plan != null ? plan.Lifts.Length : 0;
            writer.PropertyName("planLifts");
            writer.ArrayBegin((uint)lifts);
            for (int i = 0; i < lifts; i++)
            {
                PlanLift lift = plan.Lifts[i];
                writer.TypeBegin("SkiResortMod.QuickRunsLift");
                writer.PropertyName("id");
                writer.Write(lift.Id);
                writer.PropertyName("key");
                writer.Write(unchecked((int)lift.Key));
                writer.PropertyName("origin");
                writer.Write((int)lift.Origin);
                writer.PropertyName("ordinal");
                writer.Write(QuickRunsPlanRules.Ordinal(plan, i));
                writer.PropertyName("typeKey");
                writer.Write(lift.TypeIndex < SkiCatalog.Lifts.Count ? SkiCatalog.Lifts[lift.TypeIndex].Id : "");
                writer.PropertyName("pod");
                writer.Write(lift.Pod);
                writer.PropertyName("lengthM");
                writer.Write(Finite(lift.LengthM));
                writer.PropertyName("riseM");
                writer.Write(Finite(lift.RiseM));
                writer.PropertyName("cost");
                writer.Write((int)System.Math.Min(System.Math.Max(lift.Cost, 0L), int.MaxValue));
                writer.PropertyName("serves");
                writer.Write(lift.Serves);
                writer.PropertyName("startsNearTop");
                writer.Write(lift.StartsNearTop);
                writer.PropertyName("homeKey");
                writer.Write(VerdictKey(lift.Home));
                writer.PropertyName("wantedKey");
                writer.Write(WantedKey(lift.Wanted));
                writer.PropertyName("on");
                writer.Write(t.IsLiftOn(i));   // the lift on AND its pod on
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            int proposed = 0, pinned = 0;
            for (int i = 0; i < lifts; i++)
                if (plan.Lifts[i].Origin == LiftOrigin.Pinned)
                    pinned++;
                else
                    proposed++;
            writer.PropertyName("plan");
            writer.TypeBegin("SkiResortMod.QuickRunsPlan");
            writer.PropertyName("pods");
            writer.Write(plan != null ? plan.Pods.Length : 0);
            writer.PropertyName("liftsProposed");
            writer.Write(proposed);
            writer.PropertyName("liftsPinned");
            writer.Write(pinned);
            writer.PropertyName("runKm");
            writer.Write(plan != null ? Finite(plan.RunKm) : 0f);
            writer.PropertyName("runKmPerKm2");
            writer.Write(plan != null ? Finite(plan.RunKmPerKm2) : 0f);
            writer.PropertyName("deadEnds");
            writer.Write(plan != null ? plan.DeadEnds : 0);
            // The terrain's honest mix as whole percents: [Flat, green, blue, black and double black together, 0]. The three shown shares
            // come from QuickRunsPlanRules.MixPercents, the same numbers the tooltip prints (one rounded value each, the last = the rest)
            int mixG = 0, mixB = 0, mixK = 0;
            if (plan != null)
                QuickRunsPlanRules.MixPercents(plan.MountainMix, out mixG, out mixB, out mixK);
            writer.PropertyName("mountainMix");
            writer.ArrayBegin(5u);
            writer.Write(plan != null ? (int)System.Math.Round(Finite(plan.MountainMix[0]) * 100f) : 0);
            writer.Write(mixG);
            writer.Write(mixB);
            writer.Write(mixK);
            writer.Write(0);
            writer.ArrayEnd();
            // The site verdict's detail line (v1 keeps sitePercent / siteTier, Resort adds this one line). Content: pods whose lift meets its wanted
            // way home (field name kept from stage A), of the pods a lift serves, and the largest bench in hectares (one decimal)
            writer.PropertyName("sitePods");
            writer.Write(plan != null ? plan.Site.Pods : 0);
            writer.PropertyName("siteGreenHome");
            writer.Write(plan != null ? plan.Site.PodsWithGreenHome : 0);
            writer.PropertyName("siteBenchHa");
            writer.Write(plan != null ? (float)System.Math.Round(Finite(plan.Site.LargestBenchHa), 1) : 0f);
            writer.TypeEnd();
            // Contract 41: the plan sheet and the lift queue (the rows come from the sheet, not from the preview; same function as the state, rule R2)
            ResortLiftBuildSystem lb = LiftBuildSys();
            ResortPlanSheet sheet = lb?.Sheet;
            int n = sheet != null ? sheet.Count : 0;
            writer.PropertyName("sheet");
            writer.TypeBegin("SkiResortMod.QuickRunsSheet");
            writer.PropertyName("rows");
            writer.ArrayBegin((uint)n);
            uint inFlight = lb != null ? lb.InFlightKey : 0u;
            IReadOnlyList<uint> pending = lb != null ? lb.PendingKeys : null;
            for (int i = 0; i < n; i++)
            {
                ResortPlanSheet.Row row = sheet.Rows[i];
                string step = row.Key == inFlight && lb.QueueRunning ? "building" : IsPending(pending, row.Key) ? "queued" : "";
                string group = ResortPlanSheet.ReasonGroup(row.ReasonKey);
                writer.TypeBegin("SkiResortMod.QuickRunsSheetRow");
                writer.PropertyName("key");
                writer.Write(unchecked((int)row.Key));
                writer.PropertyName("name");
                writer.Write(row.Name ?? "");
                writer.PropertyName("typeKey");
                writer.Write(row.TypeIndex < SkiCatalog.Lifts.Count ? SkiCatalog.Lifts[row.TypeIndex].Id : "");
                writer.PropertyName("lengthM");
                writer.Write(Finite(Unity.Mathematics.math.distance(row.Bottom.xz, row.Top.xz)));
                writer.PropertyName("riseM");
                writer.Write(Finite(row.Top.y - row.Bottom.y));
                writer.PropertyName("cost");
                writer.Write((int)System.Math.Min(System.Math.Max(row.Cost, 0L), int.MaxValue));
                writer.PropertyName("on");
                writer.Write(row.On);
                writer.PropertyName("state");
                writer.Write(s_SheetStateNames[(int)row.State]);
                writer.PropertyName("step");
                writer.Write(step);
                writer.PropertyName("reasonKey");
                writer.Write(group.Length == 0 ? "" : LiftWhyPrefix + group + "]");
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            bool running = lb != null && lb.QueueRunning;
            writer.PropertyName("running");
            writer.Write(running);
            writer.PropertyName("index");
            writer.Write(lb != null ? lb.QueueIndex : 0);
            writer.PropertyName("total");
            writer.Write(running ? lb.QueueTotal : 0);
            writer.PropertyName("stopping");
            writer.Write(lb != null && lb.QueueStopping);
            // The lifts total (apart from the runs total): the lifts switched on and not built, the lifts not built, the built ones and the
            // estimate of the first group (stations plus line, SkiCatalog; stations a row already has are not taken off)
            writer.PropertyName("toBuild");
            writer.Write(sheet != null ? sheet.CountToBuild() : 0);
            writer.PropertyName("unbuilt");
            writer.Write(sheet != null ? sheet.CountUnbuilt() : 0);
            writer.PropertyName("built");
            writer.Write(sheet != null ? sheet.CountIn(SheetLiftState.Built) : 0);
            writer.PropertyName("cost");
            writer.Write(sheet != null ? (int)System.Math.Min(sheet.CostToBuild(), int.MaxValue) : 0);
            writer.PropertyName("notice");
            writer.Write(lb?.Notice ?? "");
            writer.TypeEnd();
            writer.TypeEnd();
        }

        private static readonly string[] s_SheetStateNames = System.Enum.GetNames(typeof(SheetLiftState));
        private const string LiftWhyPrefix = "SkiResort.Tooltip.QUICK_LIFT_WHY[";

        private static bool IsPending(IReadOnlyList<uint> pending, uint key)
        {
            if (pending == null)
                return false;
            for (int i = 0; i < pending.Count; i++)
                if (pending[i] == key)
                    return true;
            return false;
        }

        private static readonly string[] s_TerrainBandNames = System.Enum.GetNames(typeof(TerrainBand));
        private static readonly string[] s_VerdictNames = System.Enum.GetNames(typeof(HomeVerdict));

        private static string ClassKey(TerrainBand band) => QuickRunsRules.TipPrefix + "CLASS[" + s_TerrainBandNames[(int)band] + "]";

        /// <summary>"" without a verdict (None, or a pod without a lift). Else the verdict's tooltip locale key.</summary>
        private static string VerdictKey(HomeVerdict v) => v == HomeVerdict.None ? "" : QuickRunsRules.TipPrefix + "VERDICT[" + s_VerdictNames[(int)v] + "]";

        /// <summary>The level the Home knob asked for at a lift (the hub's QUICK_HOME_* text). "" while Auto is unresolved.</summary>
        private static string WantedKey(HomeLevel w) =>
            w == HomeLevel.Green ? "SkiResort.Hub.QUICK_HOME_GREEN" : w == HomeLevel.Blue ? "SkiResort.Hub.QUICK_HOME_BLUE" : "";

        private static void WriteInts(IJsonWriter writer, int[] values, bool show)
        {
            writer.ArrayBegin((uint)values.Length);
            for (int i = 0; i < values.Length; i++)
                writer.Write(show ? values[i] : 0);
            writer.ArrayEnd();
        }

        private static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

        /// <summary>The DUMP line.</summary>
        private void AppendQuickRunsDump(StringBuilder sb)
        {
            QuickRunsToolSystem t = m_QuickRunsTool;
            sb.Append("  quickRuns: option ").Append(QuickRunsToolSystem.OptionOn ? "on" : "off").Append(", armed ").Append(t.IsArmed)
              .Append(", disabled ").Append(t.IsDisabled).Append(", active ").Append(t.IsActive).Append(", phase ").Append(t.Phase)
              .Append(", corners ").Append(t.Corners).Append(", last scan '").Append(t.LastScanText).Append("', last build '")
              .Append(t.LastBuildText).Append("', batch ").Append(t.BatchRuns).Append(" run(s), trees ").Append(t.TreesOn ? "on" : "off")
              .Append(" (").Append(t.TreeBatchCount).Append(" undoable), hub revision ")
              .Append(t.HubRevision.ToString(CultureInfo.InvariantCulture)).Append('\n');
            ResortLiftBuildSystem lb = LiftBuildSys();
            if (lb != null)
                sb.Append("  planSheet: ").Append(lb.Sheet.Count).Append(" lift(s), revision ").Append(lb.Sheet.Revision.ToString(CultureInfo.InvariantCulture))
                  .Append(", queue ").Append(lb.QueueRunning ? $"running {lb.QueueIndex} / {lb.QueueTotal}{(lb.QueueStopping ? ", stopping" : "")}" : "idle")
                  .Append(", to build ").Append(lb.Sheet.CountToBuild()).Append(" (about ").Append(lb.Sheet.CostToBuild().ToString(CultureInfo.InvariantCulture))
                  .Append(")\n");
        }
    }
}
