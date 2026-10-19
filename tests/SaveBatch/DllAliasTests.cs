// DllAliasTests — SL-A / SL-AV: the BUILT mod DLL (mod/SkiResortMod/obj/<cfg>/net48/SkiResortMod.dll, what
// `dotnet build -t:Compile` writes) read as metadata only (System.Reflection.Metadata; nothing of the game is loaded).
// Colossal.Core 1.6.2f1 mechanism replayed here (Colossal.Serialization.Entities/, decompiled; constraints doc tag C162):
//   * save: the type is written as GetComponentType().AssemblyQualifiedName (ComponentSerializer.cs:33-41);
//   * boot: ComponentSerializerLibrary.Initialize registers every ISerializable / IEmptySerializable TypeManager type
//     (:45-98) and UpdateTypeTable adds each [FormerlySerializedAs] oldName -> type with Dictionary.Add (:113-121: a duplicate
//     key THROWS inside Initialize);
//   * load: DeserializeType tries Type.GetType(saved); on null it strips the last ", ..." segment until the table hits
//     (:51-65); a miss logs "Not serializable type" and the block is skipped (:66-73).
// So a self-alias "Ns.Type, Assembly" is reached from ANY saved "Ns.Type, Assembly, Version=x, Culture=y, PublicKeyToken=z".

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SkiResortMod.Systems;

namespace SaveBatch
{
    public static class DllAliasTests
    {
        private const string ColossalNs = "Colossal.Serialization.Entities";

        /// <summary>A type DECLARATION that lists ISerializable / IEmptySerializable (comments that only mention it do not count:
        /// other sessions edit those files without touching the save format).</summary>
        private static readonly System.Text.RegularExpressions.Regex PersistedDecl = new System.Text.RegularExpressions.Regex(
            @"^\s*(public|internal)\s[^\n/]*\b(struct|class)\s+\w+\s*:[^\n{]*\bI(Empty)?Serializable\b",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        private sealed class Persisted
        {
            public string FullName;
            public bool IsComponent, IsBuffer;
            public readonly List<string> Aliases = new List<string>();
            public readonly List<string> ForeignAliasAttrs = new List<string>();
        }

        public static void Run(Func<bool, string, string, bool> check, string root)
        {
            string modDir = Path.Combine(root, "mod", "SkiResortMod");
            string objDir = Path.Combine(modDir, "obj");
            string dll = Directory.Exists(objDir)
                ? Directory.GetFiles(objDir, "SkiResortMod.dll", SearchOption.AllDirectories)
                    .Where(p => p.Replace('\\', '/').Contains("/net48/"))
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            if (!check(dll != null, "SL-A setup", "no built DLL under mod/SkiResortMod/obj/*/net48: run `dotnet build -t:Compile` there first"))
                return;
            DateTime built = File.GetLastWriteTimeUtc(dll);
            Program.Info("SL-A reading " + dll.Substring(root.Length + 1) + " (" + built.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + ")");

            // Stale DLL = the test would vouch for code that is not in it. Inputs: the csproj, SaveLayouts.cs and every source
            // that declares a persisted type or an alias.
            var inputs = new List<string> { Path.Combine(modDir, "SkiResortMod.csproj") };
            foreach (string f in Directory.GetFiles(modDir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = f.Substring(modDir.Length + 1).Replace('\\', '/');
                if (rel.StartsWith("obj/") || rel.StartsWith("bin/"))
                    continue;
                string text = File.ReadAllText(f);
                if (PersistedDecl.IsMatch(text) || text.Contains("[FormerlySerializedAs(") || rel.EndsWith("SaveLayouts.cs"))
                    inputs.Add(f);
            }
            foreach (string f in inputs)
                check(File.GetLastWriteTimeUtc(f) <= built, "SL-A stale",
                      f.Substring(root.Length + 1) + " is newer than the DLL: run `dotnet build -t:Compile` in mod/SkiResortMod, then this test");

            using (FileStream fs = File.OpenRead(dll))
            using (var pe = new PEReader(fs))
            {
                MetadataReader md = pe.GetMetadataReader();
                AssemblyDefinition asm = md.GetAssemblyDefinition();
                string asmName = md.GetString(asm.Name);
                string asmVer = asm.Version.ToString();
                check(asmName == SaveLayouts.AssemblyName, "SL-AV", "assembly name '" + asmName + "', want '" + SaveLayouts.AssemblyName + "' (the save stores it)");
                check(asmVer == SaveLayouts.PinnedAssemblyVersion, "SL-AV",
                      "AssemblyVersion " + asmVer + ", want the pinned " + SaveLayouts.PinnedAssemblyVersion + " (SkiResortMod.csproj SAVE IDENTITY: never changes)");
                string culture = asm.Culture.IsNil || md.GetString(asm.Culture).Length == 0 ? "neutral" : md.GetString(asm.Culture);
                string token = asm.PublicKey.IsNil || md.GetBlobBytes(asm.PublicKey).Length == 0 ? "null" : "(signed)";
                string tail = ", " + asmName + ", Version=" + asmVer + ", Culture=" + culture + ", PublicKeyToken=" + token;

                List<Persisted> found = Scan(md);
                Program.Info("SL-A " + found.Count + " ISerializable type(s) in the DLL: " + string.Join(", ", found.Select(p => p.FullName)));

                // rows <-> types, same kind
                var byName = found.ToDictionary(p => p.FullName);
                foreach (SaveLayouts.Layout row in SaveLayouts.All)
                {
                    string full = SaveLayouts.Namespace + "." + row.TypeName;
                    if (!check(byName.ContainsKey(full), "SL-A " + row.TypeName, "SaveLayouts row has no ISerializable type " + full + " in the DLL"))
                        continue;
                    Persisted p = byName[full];
                    bool kindOk = row.Kind == SaveLayouts.KindBuffer ? p.IsBuffer && !p.IsComponent : p.IsComponent && !p.IsBuffer;
                    check(kindOk, "SL-A " + row.TypeName, "kind changed: the table says " + row.Kind + " (component <-> buffer is 'Serializer type mismatch', data lost; constraints row 2c)");
                    string self = SaveLayouts.Alias(row);
                    check(p.Aliases.Contains(self), "SL-A " + row.TypeName,
                          "no [FormerlySerializedAs(\"" + self + "\")] (has: " + (p.Aliases.Count == 0 ? "none" : string.Join(" | ", p.Aliases)) + ")");
                }
                var rowNames = new HashSet<string>(SaveLayouts.All.Select(r => SaveLayouts.Namespace + "." + r.TypeName));
                foreach (Persisted p in found)
                {
                    check(rowNames.Contains(p.FullName), "SL-A " + p.FullName,
                          "persisted type without a SaveLayouts row (a new save-format contract: add a row, a self-alias, a codec in LayoutTests.CodecOf)");
                    foreach (string a in p.ForeignAliasAttrs)
                        check(false, "SL-A " + p.FullName, "[FormerlySerializedAs] from " + a + ", not " + ColossalNs + ": the save code never reads it");
                }

                // the alias table as UpdateTypeTable builds it (Dictionary.Add: a duplicate throws at boot)
                var table = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Persisted p in found)
                    foreach (string a in p.Aliases)
                    {
                        if (check(!table.ContainsKey(a), "SL-A table", "alias '" + a + "' on both " + (table.ContainsKey(a) ? table[a] : "?") + " and " + p.FullName + " (Dictionary.Add throws in ComponentSerializerLibrary.Initialize)"))
                            table.Add(a, p.FullName);
                    }
                Program.Info("SL-A alias table: " + table.Count + " key(s)");

                // DeserializeType's strip loop, for the AQN this DLL writes AND for one with another version (Type.GetType failed)
                foreach (Persisted p in found.Where(q => rowNames.Contains(q.FullName)))
                {
                    foreach (string saved in new[] { p.FullName + tail, p.FullName + ", " + asmName + ", Version=0.9.8.7, Culture=neutral, PublicKeyToken=null" })
                    {
                        string hit = Strip(table, saved, out string key);
                        check(hit == p.FullName, "SL-A " + p.FullName, "saved '" + saved + "' resolves to " + (hit ?? "NOTHING ('Not serializable type', block skipped)"));
                        if (hit == p.FullName && saved.EndsWith(tail))
                            Program.Info("SL-A " + p.FullName + ": '" + saved + "' -> key '" + key + "'");
                    }
                }
            }
        }

        /// <summary>ComponentSerializer.DeserializeType's fallback (:55-64), verbatim in shape.</summary>
        private static string Strip(Dictionary<string, string> table, string saved, out string key)
        {
            string text = saved;
            string value;
            while (!table.TryGetValue(text, out value))
            {
                int num = text.LastIndexOf(',');
                if (num < 0)
                    break;
                text = text.Substring(0, num);
            }
            key = value != null ? text : null;
            return value;
        }

        private static void NameOf(MetadataReader md, EntityHandle h, out string ns, out string name)
        {
            ns = ""; name = "";
            if (h.Kind == HandleKind.TypeReference)
            {
                TypeReference tr = md.GetTypeReference((TypeReferenceHandle)h);
                ns = md.GetString(tr.Namespace); name = md.GetString(tr.Name);
            }
            else if (h.Kind == HandleKind.TypeDefinition)
            {
                TypeDefinition td = md.GetTypeDefinition((TypeDefinitionHandle)h);
                ns = md.GetString(td.Namespace); name = md.GetString(td.Name);
            }
        }

        private static string FullNameOf(MetadataReader md, TypeDefinition td)
        {
            string name = md.GetString(td.Name);
            TypeDefinitionHandle outer = td.GetDeclaringType();
            if (!outer.IsNil)
                return FullNameOf(md, md.GetTypeDefinition(outer)) + "+" + name;   // AQN nesting separator
            string ns = md.GetString(td.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        private static List<Persisted> Scan(MetadataReader md)
        {
            var list = new List<Persisted>();
            foreach (TypeDefinitionHandle h in md.TypeDefinitions)
            {
                TypeDefinition td = md.GetTypeDefinition(h);
                bool ser = false, comp = false, buf = false;
                foreach (InterfaceImplementationHandle ih in td.GetInterfaceImplementations())
                {
                    NameOf(md, md.GetInterfaceImplementation(ih).Interface, out string ns, out string name);
                    if (ns == ColossalNs && (name == "ISerializable" || name == "IEmptySerializable")) ser = true;
                    if (ns == "Unity.Entities" && name == "IComponentData") comp = true;
                    if (ns == "Unity.Entities" && name == "IBufferElementData") buf = true;
                }
                if (!ser)
                    continue;
                var p = new Persisted { FullName = FullNameOf(md, td), IsComponent = comp, IsBuffer = buf };
                foreach (CustomAttributeHandle ch in td.GetCustomAttributes())
                {
                    CustomAttribute ca = md.GetCustomAttribute(ch);
                    string ans = "", aname = "";
                    if (ca.Constructor.Kind == HandleKind.MemberReference)
                        NameOf(md, md.GetMemberReference((MemberReferenceHandle)ca.Constructor).Parent, out ans, out aname);
                    else if (ca.Constructor.Kind == HandleKind.MethodDefinition)
                        NameOf(md, md.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor).GetDeclaringType(), out ans, out aname);
                    if (aname != "FormerlySerializedAsAttribute")
                        continue;
                    if (ans != ColossalNs) { p.ForeignAliasAttrs.Add(ans); continue; }
                    BlobReader br = md.GetBlobReader(ca.Value);
                    ushort prolog = br.ReadUInt16();   // ECMA-335 II.23.3: 0x0001, then the SerString argument
                    p.Aliases.Add(prolog == 1 ? br.ReadSerializedString() : null);
                }
                list.Add(p);
            }
            return list.OrderBy(p => p.FullName, StringComparer.Ordinal).ToList();
        }
    }
}
