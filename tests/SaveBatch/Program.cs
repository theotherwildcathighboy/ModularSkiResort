// SaveBatch — the save-format gate (T74, 2026-10-19; docs/research/release-scaffolds-2026-10-19.md; constraints doc
// docs/research/release-update-constraints-2026-10-18.md §3 rules 5 / 6 / 9, §4 items 3 / 4 / 5 / 9).
// Run (from the ModularSkiResort root, AFTER `dotnet build -t:Compile` in mod/SkiResortMod: SL-A reads the built DLL):
//   dotnet run --project tests\SaveBatch\SaveBatch.csproj -c Release
// Prints `FAIL  where: msg` lines, `OK` / `INFO` lines and one SUMMARY line; Environment.ExitCode = failures (the
// ResortBatch convention).
//   SL-T  the table itself (LayoutTests): versions 1..n consecutive, bytes strictly growing, each entry's bytes == 1 + the
//         sizes of its fields; the SHIPPED snapshot (entries released to players) still appears verbatim (never edited).
//   SL-L  per persisted type (LayoutTests): CurrentVersion == the table's last entry; Serialize writes exactly the entry's
//         bytes, version byte first (= CurrentVersion, whatever m_Version holds), every field at the table's offset with its
//         kind; struct fields (reflection) == the table's fields (a field added without a new entry FAILS); round trip:
//         Deserialize consumes exactly those bytes, every field comes back, a rewrite is byte-identical; every OLDER version's
//         blob still loads whole (rule 5: keep old versions loadable).
//   SL-A  the built DLL (DllAliasTests, System.Reflection.Metadata, no engine load): every ISerializable / IEmptySerializable
//         type has a SaveLayouts row (and every row a type) of the same kind, exactly one [FormerlySerializedAs] whose value
//         == SaveLayouts.Alias(row); the alias table built the way ComponentSerializerLibrary.UpdateTypeTable builds it
//         (Dictionary.Add, :113-121) has no duplicate; the saved AQN, with ANY version, strips down to its own type through
//         ComponentSerializer.DeserializeType's loop (:51-65). SL-AV: AssemblyVersion == SaveLayouts.PinnedAssemblyVersion.
//   SL-CL release/CHANGELOG.md (ChangelogTests): every release entry carries the three fixed lines; the newest entry's
//         "Layout versions:" line == SaveLayouts.ChangelogLine; a layout change between two entries says "Do not load in older
//         versions: yes".
// Oracles: the byte codec is OUR reading of Colossal BinaryWriter.cs (1.6.2f1, raw little-endian in call order, Entity =
// int32 saved index); the in-game save / load is Richard's test (docs/research/field/old-save-checklist.md).

using System;
using System.Globalization;
using System.IO;

namespace SaveBatch
{
    internal static class Program
    {
        private const int MaxPrinted = 250;
        private static int s_Failures;
        private static long s_Checks;
        private static int s_Printed;

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            string root = FindRepoRoot();
            if (root == null)
            {
                Console.WriteLine("FAIL  setup: ModularSkiResort root not found (run from the repo root)");
                Environment.ExitCode = 1;
                return 1;
            }
            LayoutTests.Run(Check);
            DllAliasTests.Run(Check, root);
            ChangelogTests.Run(Check, root);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "SUMMARY  SaveBatch checks={0} failures={1}", s_Checks, s_Failures));
            Environment.ExitCode = s_Failures;
            return s_Failures;
        }

        /// <summary>The ModularSkiResort root: the first directory upwards (from the working directory, then from the binary)
        /// that holds mod/SkiResortMod/SkiResortMod.csproj.</summary>
        private static string FindRepoRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var d = new DirectoryInfo(start);
                while (d != null)
                {
                    if (File.Exists(Path.Combine(d.FullName, "mod", "SkiResortMod", "SkiResortMod.csproj")))
                        return d.FullName;
                    d = d.Parent;
                }
            }
            return null;
        }

        private static bool Check(bool ok, string where, string msg)
        {
            s_Checks++;
            if (!ok)
            {
                s_Failures++;
                if (s_Printed++ < MaxPrinted)
                    Console.WriteLine("FAIL  " + where + ": " + msg);
            }
            return ok;
        }

        internal static void Info(string line) => Console.WriteLine("INFO  " + line);
    }
}
