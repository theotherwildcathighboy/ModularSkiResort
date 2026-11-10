// Headless tests for the Resort Planner: the runs-first planner (RunsFirstPlanner*.cs), its realizer (RunsFirstRealizer*.cs), the
// tool's scan job (QuickRunsScanJob.cs) and the pieces they share (HeightSnapshot, PlanGeometry, PlanContract, the tool rules).
// The v1 / v2 planner (SkiRunAutoPlanner) and its suites were deleted in port round R2-d (plan 7.7).
// The default run covers:
// - the snapshot sampler (the SampleHeightInternal shape) and the region seed;
// - the tool rules (QuickRunsTests);
// - the fixtures (Heightmap, V2Fixtures) and the plan gate metrics on hand-made plans (V2MetricsTests);
// - the scan job (WorkerTests), the runs-first golden (RunsFirstGolden), the frozen scorecard probes (RunsFirstScoreTests) and the
//   realizer suite (RealizerTests).
// The rf* modes (RunsFirst.Host.cs, ScanHost.cs, RealizerHost.cs) plan, replay and render. They print numbers and run no checks.
// Run from the repo root: dotnet run --project tests/AutoGenBatch/AutoGenBatch.csproj -c Release
// Prints `FAIL  where: msg` lines and one SUMMARY line. Environment.ExitCode = failures (the LiftBatch convention).

using System;
using System.Globalization;
using System.IO;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static int s_Failures;
        private static long s_Checks;

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            // RF_TIES=reverse|sorts|queue (debug, host only): equal keys in the planner's sorts and queues go in reverse order (RunsFirstPlanner.Order.cs).
            // A plan hash that changes proves a tie that matters (port round 2b-f).
            string ties = Environment.GetEnvironmentVariable("RF_TIES");
            RunsFirstPlanner.ReverseTies = ties == "reverse" ? 3 : ties == "sorts" ? 1 : ties == "queue" ? 2 : 0;
            if (args.Length > 0 && args[0] == "rf-game-plans")
                return GamePlans(args);   // the gate on the game's own dumped plans (GamePlans.cs)
            if (args.Length > 0 && (args[0] == "rf" || args[0] == "rf-holdout" || args[0] == "rf-golden" || args[0] == "rf-cancel"))
                return RunsFirst.Run(args);   // the runs-first planner on the field test regions: renders, no checks
            if (args.Length > 0 && (args[0] == "rf-replay" || args[0] == "rf-scan" || args[0] == "rf-liftcheck")) return ScanHost.Run(args);   // the tool's scan job: replay a game dump, density presets (ScanHost.cs)
            if (args.Length > 0 && args[0] == "rf-realize")
                return RealizerHost.Run(args);   // the runs-first realizer on the real sets (RealizerHost.cs)
            ToRepoRoot();
            SamplerTests();
            SeedTests();
            QuickRunsTests();
            HeightmapTests();
            V2FixtureTests();
            V2MetricsTests();
            WorkerTests();
            RunsFirstGoldenTests();
            RunsFirstScoreTests();
            RealizerTests();
            Console.WriteLine($"SUMMARY AutoGenBatch: {s_Checks} checks, {s_Failures} failure(s)");
            Environment.ExitCode = s_Failures;
            return s_Failures;
        }

        // ---------------------------------------------------------------- plumbing

        /// <summary>When the current directory has no gamedata-mirror, this method moves to the first parent of AppContext.BaseDirectory
        /// (tests/AutoGenBatch/bin/&lt;cfg&gt;/net8.0/) that has one. So a run started from tests/ or mod/SkiResortUI checks the same files
        /// as a run from the root. Only the check run moves. The rf modes keep the caller's directory for their relative output paths.</summary>
        private static void ToRepoRoot()
        {
            if (Directory.Exists("gamedata-mirror"))
                return;
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "gamedata-mirror")))
                {
                    Directory.SetCurrentDirectory(d.FullName);
                    return;
                }
        }

        private static void Check(bool ok, string where, string msg)
        {
            s_Checks++;
            if (!ok)
            {
                s_Failures++;
                Console.WriteLine($"FAIL  {where}: {msg}");
            }
        }

        // ---------------------------------------------------------------- snapshot sampler

        private static void SamplerTests()
        {
            float2[] region = Terrain.Square(0f, 0f, 200f);
            HeightSnapshot s = Terrain.Snapshot(Terrain.Fbm(3u, 0.3, 40.0), region);
            Check(s.Valid, "sampler", "snapshot not valid");
            int k = s.X0 + 10, l = s.Z0 + 12;
            float x = (float)(s.Origin.x + k * (double)s.TexelM), z = (float)(s.Origin.y + l * (double)s.TexelM);
            s.TryRaw(k, l, out ushort raw);
            float at = s.Sample(new float3(x, 0f, z));
            Check(Math.Abs(at - s.ToWorld(raw)) < 1e-3f, "sampler", $"texel center {at} vs raw {s.ToWorld(raw)}");
            s.TryRaw(k + 1, l, out ushort raw1);
            float mid = s.Sample(new float3(x + 0.5f * s.TexelM, 0f, z));
            Check(Math.Abs(mid - 0.5f * (s.ToWorld(raw) + s.ToWorld(raw1))) < 2e-3f, "sampler", "half-texel bilinear");
            Check(float.IsNaN(s.Sample(new float3(x - 4000f, 0f, z))), "sampler", "outside the rectangle is not NaN");
            Check(!s.CoversClamped(s.X0 - 1, s.Z0, s.X0 + 1, s.Z0), "sampler", "coverage left of the rectangle");
        }

        private static void SeedTests()
        {
            float2[] a = { new float2(0, 0), new float2(100, 0), new float2(100, 50), new float2(0, 60) };
            float2[] rot = { a[2], a[3], a[0], a[1] };
            float2[] rev = { a[3], a[2], a[1], a[0] };
            uint s = PlanGeometry.SeedOf(a, 0);
            Check(s == PlanGeometry.SeedOf(rot, 0) && s == PlanGeometry.SeedOf(rev, 0), "seed", "start vertex or direction changes the seed");
            Check(s != PlanGeometry.SeedOf(a, 1), "seed", "shuffle does not change the seed");
            Check((s & 1u) == 1u, "seed", "seed is even (Random needs nonzero)");
        }

        // ---------------------------------------------------------------- PNG writer (the rf render)

        internal static void WritePngRgb(string path, int w, int h, byte[] rgb) => WritePng(path, w, h, rgb);

        private static void WritePng(string path, int w, int h, byte[] rgb)
        {
            var raw = new MemoryStream();
            for (int j = 0; j < h; j++)
            {
                raw.WriteByte(0);
                raw.Write(rgb, j * w * 3, w * 3);
            }
            var z = new MemoryStream();
            using (var zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, true))
                raw.WriteTo(zs);
            using var fs = File.Create(path);
            fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            void Chunk(string type, byte[] data)
            {
                var len = BitConverter.GetBytes(data.Length);
                Array.Reverse(len);
                fs.Write(len);
                var td = new byte[4 + data.Length];
                System.Text.Encoding.ASCII.GetBytes(type).CopyTo(td, 0);
                data.CopyTo(td, 4);
                fs.Write(td);
                uint crc = 0xFFFFFFFFu;
                foreach (byte b in td)
                {
                    crc ^= b;
                    for (int k = 0; k < 8; k++)
                        crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
                var cb = BitConverter.GetBytes(~crc);
                Array.Reverse(cb);
                fs.Write(cb);
            }
            var ihdr = new byte[13];
            var wb = BitConverter.GetBytes(w);
            var hb = BitConverter.GetBytes(h);
            Array.Reverse(wb);
            Array.Reverse(hb);
            wb.CopyTo(ihdr, 0);
            hb.CopyTo(ihdr, 4);
            ihdr[8] = 8;
            ihdr[9] = 2;
            Chunk("IHDR", ihdr);
            Chunk("IDAT", z.ToArray());
            Chunk("IEND", new byte[0]);
        }
    }
}
