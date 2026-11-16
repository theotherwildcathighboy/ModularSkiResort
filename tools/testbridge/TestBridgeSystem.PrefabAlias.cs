// T228 migration aid (bridge only, never in the shipped mod): `prefabalias` maps dev-made prefab ids onto the loaded pack prefabs.
//
// Why: the game files a placed object under PrefabID(type, name, hash). A pack prefab hashes its platformID "167215"; the same prefab
// imported into ImportedData on the dev machine hashes its asset guid, the cid in tools/shipped-prefabs.json (decompile 1.6.4f1
// PrefabID.cs:15-37). A save built with ImportedData present stores the cid hash for objects that code or sub-objects placed, and with
// ImportedData parked those ids are Unknown (PrefabSystem.cs:966) and draw as placeholders.
//
// How: PrefabSystem.Deserialize resolves every saved id through TryGetPrefab, a lookup in the private Dictionary<PrefabID, int>
// m_PrefabIndices (PrefabSystem.cs:62, 870-910). For each row (type, name, cid) this op adds PrefabID(type, name, Hash128(cid)) ->
// the index of the loaded prefab of that type and name, the same thing an ObsoleteIdentifiers row does at AddPrefab (:164-177).
// Run it at the main menu, BEFORE the save loads. PrefabSystem.Serialize writes each prefab's own GetPrefabID (:826-862), so a re-save
// stores the pack ids and the save no longer needs the alias. The aliases live until the game exits.
//
//   {op:"prefabalias", list:"<abs path of tools/shipped-prefabs.json>", dry:true|false}   add (dry: report only)
//   {op:"prefabalias", action:"status"}                                                    rows added this session

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Prefabs;
using Newtonsoft.Json.Linq;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private readonly List<string> m_AliasAdded = new List<string>();

        private JObject PrefabAliasCommand(JObject args)
        {
            FieldInfo indicesField = typeof(PrefabSystem).GetField("m_PrefabIndices", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("PrefabSystem.m_PrefabIndices not found (game patch?)");
            FieldInfo prefabsField = typeof(PrefabSystem).GetField("m_Prefabs", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("PrefabSystem.m_Prefabs not found (game patch?)");
            var indices = (Dictionary<PrefabID, int>)indicesField.GetValue(m_Prefabs);
            var prefabs = (List<PrefabBase>)prefabsField.GetValue(m_Prefabs);

            if ((string)args["action"] == "status")
                return new JObject { ["added"] = new JArray(m_AliasAdded.ToArray()), ["count"] = m_AliasAdded.Count, ["indices"] = indices.Count };

            string listPath = (string)args["list"] ?? throw new ArgumentException("list: absolute path of tools/shipped-prefabs.json");
            bool dry = (bool?)args["dry"] ?? true;
            var rows = (JArray)JObject.Parse(File.ReadAllText(listPath))["prefabs"];

            // Loaded prefab by "type/name". Two prefabs with one type and name (pack + ImportedData) make the row ambiguous: refused.
            var byKey = new Dictionary<string, int>();
            var ambiguous = new HashSet<string>();
            for (int i = 0; i < prefabs.Count; i++)
            {
                PrefabBase p = prefabs[i];
                if (p == null)
                    continue;
                string key = p.GetType().Name + "/" + p.name;
                if (byKey.ContainsKey(key))
                    ambiguous.Add(key);
                else
                    byKey[key] = i;
            }

            int added = 0, present = 0;
            var missing = new JArray();
            var refused = new JArray();
            var badCid = new JArray();
            var sample = new JArray();
            foreach (JToken row in rows)
            {
                string type = (string)row["type"], name = (string)row["name"], cid = (string)row["cid"];
                string key = type + "/" + name;
                if (string.IsNullOrEmpty(cid) || !Colossal.Hash128.TryParse(cid, out Colossal.Hash128 hash))
                {
                    badCid.Add(key);
                    continue;
                }
                if (ambiguous.Contains(key))
                {
                    refused.Add(key + " (two loaded prefabs: is ImportedData parked?)");
                    continue;
                }
                if (!byKey.TryGetValue(key, out int index))
                {
                    missing.Add(key);
                    continue;
                }
                var alias = new PrefabID(type, name, hash);
                if (indices.TryGetValue(alias, out int existing))
                {
                    present++;
                    if (existing != index)
                        refused.Add(key + $" (id already maps to index {existing}, not {index})");
                    continue;
                }
                PrefabID own = prefabs[index].GetPrefabID();
                if (sample.Count < 3)
                    sample.Add($"{alias} -> {own}");
                if (!dry)
                {
                    indices.Add(alias, index);
                    m_AliasAdded.Add(alias.ToString());
                }
                added++;
            }
            if (!dry)
                Mod.Log.Info($"[prefabalias] added {added} aliases (present {present}, missing {missing.Count}, refused {refused.Count}, bad cid {badCid.Count})");
            return new JObject
            {
                ["dry"] = dry,
                ["rows"] = rows.Count,
                ["added"] = added,
                ["present"] = present,
                ["missing"] = missing,
                ["refused"] = refused,
                ["badCid"] = badCid,
                ["sample"] = sample,
                ["indices"] = indices.Count,
            };
        }
    }
}
