// The hub's side of the Quick runs tool (contract 17): the `quickRuns` value binding (bindings.ts QuickRunsState, written here from the
// tool's fields) and the two triggers, quickRunsToggle (open / close, the glade toggle's pattern) and quickRunsSet(key, value) (every
// knob, button and tree setting: QuickRunsToolSystem.HubSet). Pushed only while the panel observes it, when the tool's HubRevision,
// availability or activity moved (the ArrayPush idea for one struct).
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
        private RawValueBinding m_QuickRuns;
        private int m_QuickRunsKey = int.MinValue;
        private bool m_QuickRunsWasActive;

        private void OnQuickRunsToggle() => Trigger("quickRunsToggle", new Request { Kind = RequestKind.QuickRunsToggle });

        private void OnQuickRunsSet(string key, int value) =>
            Trigger("quickRunsSet", new Request { Kind = RequestKind.QuickRunsSet, Text = key, Int = value });

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
            int key = t.HubRevision * 4 + (t.UnavailableReasonKey == null ? 1 : 0) + (t.IsActive ? 2 : 0);
            if (!resubscribed && key == m_QuickRunsKey)
                return;
            m_QuickRunsKey = key;
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
            // Undo trees reuses the progress pair with planting false ("Removing trees N / M"; same shape, contract unchanged)
            writer.PropertyName("treesDone");
            writer.Write(t.RemovingTrees ? t.RemoveDone : t.PlantDone);
            writer.PropertyName("treesTotal");
            writer.Write(t.Planting ? t.PlantTotal : t.RemovingTrees ? t.RemoveTotal : 0);
            writer.TypeEnd();
        }

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
        }
    }
}
