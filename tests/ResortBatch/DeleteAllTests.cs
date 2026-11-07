// The Options button "Delete all ski resort content" (DeleteAllRules.cs, SkiDeleteAllSystem). `public static class` with
// `Run(Func<bool, string, string, bool> check)`. Main passes its Check. Run from the ModularSkiResort root (reads tools/ and
// gamedata-mirror/).
// DA-1 the target set: every NAMES row but the carriers and their grips (the carrier grip round), plus the LV marker extra.
// DA-2 ours only: every target is a shipped prefab of ours with the same PrefabID type (tools/shipped-prefabs.json, the SHIPPED gate's
// list), no vanilla name.
// DA-3 the target set misses nothing placeable: every gamedata-mirror prefab with a UIObject (what a player can place) is a target.
//      Every target but the markers has a UIObject.
// DA-4 the node verdict never deletes a node a foreign edge still uses.
// DA-5 the button gate.
// DA-6 the kind maps.
// DA-7 the summary line.
// Plan: docs/research/delete-all-2026-10-29.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class DeleteAllTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            TargetSet();
            OursOnly();
            PlaceablesCovered();
            NodeVerdicts();
            ButtonGate();
            KindMaps();
            SummaryLine();
        }

        private static Dictionary<string, string> TargetTypes()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DeleteAllRules.Target t in DeleteAllRules.Targets())
                map[t.Name] = t.TypeName;
            return map;
        }

        private static void TargetSet()
        {
            List<DeleteAllRules.Target> targets = DeleteAllRules.Targets();
            int names = 0, carriers = 0, grips = 0;
            foreach (CatalogName n in SkiCatalog.AllPrefabNames())
            {
                names++;
                if (n.Kind == CatalogKind.Carrier)
                    carriers++;
                else if (n.Kind == CatalogKind.Grip)
                    grips++;
            }
            s_Check(targets.Count == names - carriers - grips + 1, "DA-1a",
                    $"targets = NAMES {names} - carriers {carriers} - grips {grips} + LV marker = {targets.Count}");
            bool noCarrier = true, uniq = true;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DeleteAllRules.Target t in targets)
            {
                if (t.Kind == CatalogKind.Carrier || t.Kind == CatalogKind.Grip)
                    noCarrier = false;
                if (!seen.Add(t.Name))
                    uniq = false;
            }
            s_Check(noCarrier, "DA-1b", "no carrier or grip is a target (they go with their line / station)");
            s_Check(uniq, "DA-1c", "each target once");
            Dictionary<string, string> map = TargetTypes();
            s_Check(map.TryGetValue("SkiLiftMarkerLV", out string lv) && lv == "PowerLinePrefab", "DA-1d", "SkiLiftMarkerLV is a PowerLinePrefab target");
            // Literal oracles: one of each kind
            string[] must = { "SkiChairliftHV", "SkiTramHV", "SkiLiftMarker", "SkiBaseStationBuilding", "SkiTramTopStationBuilding",
                              "SkiLodgeGrand", "SkiSchool", "SkiPatrolHQ", "SkiResortGate", "SkiRunCatTrackA",
                              "SkiRunNightA", "SkiSafetyFence" };
            foreach (string m in must)
                s_Check(map.ContainsKey(m), "DA-1e", "target " + m);
            s_Check(!map.ContainsKey("SkiLiftChair4") && !map.ContainsKey("SkiTramCabin"), "DA-1f", "carriers SkiLiftChair4 / SkiTramCabin are not targets");
            s_Check(!map.ContainsKey("SkiLiftChair4Grip") && !map.ContainsKey("SkiTramCarriage"), "DA-1f", "grips SkiLiftChair4Grip / SkiTramCarriage are not targets");
        }

        private static void OursOnly()
        {
            string path = Path.Combine("tools", "shipped-prefabs.json");
            if (!s_Check(File.Exists(path), "DA-2a", path + " exists (run from the repo root)"))
                return;
            var shipped = new Dictionary<string, string>(StringComparer.Ordinal);
            var rx = new Regex("\"name\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"type\"\\s*:\\s*\"([^\"]+)\"");
            foreach (Match m in rx.Matches(File.ReadAllText(path)))
                shipped[m.Groups[1].Value] = m.Groups[2].Value;
            s_Check(shipped.Count > 100, "DA-2b", $"shipped list parsed ({shipped.Count} prefabs)");
            foreach (KeyValuePair<string, string> t in TargetTypes())
            {
                bool ok = shipped.TryGetValue(t.Key, out string type) && type == t.Value;
                s_Check(ok, "DA-2c", $"target {t.Key} ({t.Value}) is a shipped prefab of ours" + (ok ? "" : $" (shipped type: {type ?? "none"})"));
            }
        }

        private static void PlaceablesCovered()
        {
            string root = Path.Combine("gamedata-mirror", "ImportedData");
            if (!s_Check(Directory.Exists(root), "DA-3a", root + " exists"))
                return;
            Dictionary<string, string> map = TargetTypes();
            var placeable = new HashSet<string>(StringComparer.Ordinal);
            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                string name = Path.GetFileName(dir);
                string file = Path.Combine(dir, name + ".Prefab");
                if (!File.Exists(file))
                    continue;
                string text = File.ReadAllText(file);
                // A vehicle's UIObject is only its selected-object icon: SkiCoachSystem spawns the coach and removes it with its depot.
                if (Regex.IsMatch(text, "\\|Game\\.Prefabs\\.UIObject\\b") && !Regex.IsMatch(text, "\\|Game\\.Prefabs\\.CarPrefab\\b"))
                    placeable.Add(name);
            }
            s_Check(placeable.Count >= 27, "DA-3b", $"{placeable.Count} placeable prefabs in the mirror (27 on 2026-10-30)");
            foreach (string p in placeable)
                s_Check(map.ContainsKey(p), "DA-3c", $"placeable {p} is a target");
            foreach (KeyValuePair<string, string> t in map)
            {
                if (t.Key == "SkiLiftMarker" || t.Key == DeleteAllRules.ExtraMarkerLV)
                    continue;
                s_Check(placeable.Contains(t.Key), "DA-3d", $"target {t.Key} is placeable (else it is a sub-element vanilla deletes)");
            }
        }

        private static void NodeVerdicts()
        {
            s_Check(DeleteAllRules.NodeVerdict(false, false, 2, 2) == DeleteNodeVerdict.Skip, "DA-4a", "a vanilla node is never deleted, even with all edges going");
            s_Check(DeleteAllRules.NodeVerdict(true, true, 1, 1) == DeleteNodeVerdict.Skip, "DA-4b", "an owned stub node is left to vanilla");
            s_Check(DeleteAllRules.NodeVerdict(true, false, 0, 0) == DeleteNodeVerdict.Delete, "DA-4c", "a loose node of ours goes");
            s_Check(DeleteAllRules.NodeVerdict(true, false, 2, 2) == DeleteNodeVerdict.Delete, "DA-4d", "a node of ours with only our edges goes");
            s_Check(DeleteAllRules.NodeVerdict(true, false, 2, 1) == DeleteNodeVerdict.KeepShared, "DA-4e", "a node of ours a vanilla edge still uses is kept");
            s_Check(DeleteAllRules.NodeVerdict(true, false, 1, 0) == DeleteNodeVerdict.KeepShared, "DA-4f", "a node of ours on a foreign edge only is kept");
            s_Check(DeleteAllRules.StubBecomesPlain(2, 2), "DA-4g", "a stub with only foreign edges becomes a plain node");
            s_Check(!DeleteAllRules.StubBecomesPlain(2, 1), "DA-4h", "one of our edges on it keeps it ours (a lift line's loose end)");
            s_Check(!DeleteAllRules.StubBecomesPlain(0, 0), "DA-4i", "no edges: nothing to become");
        }

        private static void ButtonGate()
        {
            s_Check(DeleteAllRules.ButtonEnabled(true, false), "DA-5a", "enabled in a loaded Game-mode city");
            s_Check(!DeleteAllRules.ButtonEnabled(false, false), "DA-5b", "disabled in the main menu / Editor");
            s_Check(!DeleteAllRules.ButtonEnabled(true, true), "DA-5c", "disabled while a city loads");
        }

        private static void KindMaps()
        {
            CatalogKind[] buildings = { CatalogKind.Station, CatalogKind.Lodge, CatalogKind.School, CatalogKind.Patrol, CatalogKind.Sign, CatalogKind.Depot };
            foreach (CatalogKind k in buildings)
                s_Check(DeleteAllRules.BuildingKindOf(k, out DeleteKind _) && !DeleteAllRules.IsNetKind(k), "DA-6a", $"{k} is a building kind");
            foreach (CatalogKind k in new[] { CatalogKind.Line, CatalogKind.Marker, CatalogKind.Fence })
                s_Check(DeleteAllRules.IsNetKind(k) && !DeleteAllRules.BuildingKindOf(k, out DeleteKind _), "DA-6b", $"{k} is a net kind");
            s_Check(!DeleteAllRules.IsNetKind(CatalogKind.Run) && !DeleteAllRules.BuildingKindOf(CatalogKind.Run, out DeleteKind _), "DA-6c", "Run is an area kind");
            s_Check(!DeleteAllRules.IsNetKind(CatalogKind.Carrier) && !DeleteAllRules.BuildingKindOf(CatalogKind.Carrier, out DeleteKind _), "DA-6d", "Carrier is no kind");
            s_Check(Enum.GetValues(typeof(DeleteKind)).Length == DeleteAllRules.KindCount, "DA-6e", "KindCount == DeleteKind values");
            s_Check(DeleteAllRules.BuildingKindOf(CatalogKind.Depot, out DeleteKind depot) && depot == DeleteKind.Depots, "DA-6f", "a bus depot counts as a bus depot, in no lodge count");
        }

        private static void SummaryLine()
        {
            string zero = DeleteAllRules.Summary(new int[DeleteAllRules.KindCount]);
            s_Check(zero.StartsWith("DeleteAll: done — 0 lift edges, 0 lift nodes, 0 stations", StringComparison.Ordinal) && zero.EndsWith("(deleted 0)", StringComparison.Ordinal),
                    "DA-7a", "zero line: " + zero);
            var c = new int[DeleteAllRules.KindCount];
            c[(int)DeleteKind.LiftEdges] = 12;
            c[(int)DeleteKind.Runs] = 5;
            c[(int)DeleteKind.SeasonPasses] = 300;
            c[(int)DeleteKind.KeptSharedNodes] = 1;
            string s = DeleteAllRules.Summary(c);
            s_Check(s.Contains("12 lift edges") && s.Contains("5 runs") && s.Contains("300 season passes removed") && s.Contains("1 shared nodes kept") &&
                    s.EndsWith("(deleted 17)", StringComparison.Ordinal), "DA-7b", "counts and total (passes / kept not deleted): " + s);
            s_Check(DeleteAllRules.Summary(null).EndsWith("(deleted 0)", StringComparison.Ordinal), "DA-7c", "null counts read as zero");
            var d = new int[DeleteAllRules.KindCount];
            d[(int)DeleteKind.Depots] = 2;
            string ds = DeleteAllRules.Summary(d);
            s_Check(ds.Contains("2 bus depots") && ds.EndsWith("(deleted 2)", StringComparison.Ordinal), "DA-7d", "depots are counted and removed: " + ds);
        }
    }
}
