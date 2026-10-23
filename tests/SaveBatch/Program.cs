// Save-format gate, run from the repo root after `dotnet build -t:Compile` in mod/SkiResortMod (SL-A reads the built DLL):
//   dotnet run --project tests\SaveBatch\SaveBatch.csproj -c Release
// Prints `FAIL  where: msg` lines and one SUMMARY line; Environment.ExitCode = failures. Checks: SL-T the SaveLayouts table (versions 1..n
// consecutive, bytes strictly growing, each entry's bytes == 1 + its field sizes, the shipped snapshot still verbatim); SL-L per persisted
// type (CurrentVersion == last entry; Serialize writes exactly the entry's bytes, version byte first; Deserialize consumes exactly those
// bytes; every older version's blob still loads whole); SL-A the built DLL (every ISerializable / IEmptySerializable type has a SaveLayouts
// row and exactly one [FormerlySerializedAs] == SaveLayouts.Alias(row); no duplicate alias key, as in UpdateTypeTable :113-121; the saved
// AQN with any version strips to its own type through DeserializeType :51-65); SL-AV AssemblyVersion == PinnedAssemblyVersion; SL-CL: ChangelogTests.

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

        /// <summary>The ModularSkiResort root: first directory upwards (from the working directory, then the binary) holding
        /// mod/SkiResortMod/SkiResortMod.csproj.</summary>
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
