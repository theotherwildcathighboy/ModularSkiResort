// Quick runs tool: the scan dump (port round R2-a, plan 7.4 and the round's cross-runtime gate). The tool writes the exact input of its
// last scan (snapshot with its wet layer, region, obstacles, existing lifts, access points, knobs, the planner options, the bare flag)
// and the hashes it got. The test host reads the file (mode rf-replay), runs the same QuickRunsScanJob and prints both hashes. Equal
// hashes prove that the game plans what the host scores.
// Binary, little endian (BinaryWriter): floats keep their bits. Options by field name, so a file stays readable when an option is
// added (a field the file lacks keeps its default, a field the build lacks is reported and skipped).
// Version 2 (port round 2b-f) appends the game's per-pass prints (RunsFirstPrint), the realizer's stage prints and the game's own plan
// (every RunsFirstResult field), so rf-replay names the first pass where the runtimes split and rf-game-plans scores the game's plan.
// Read accepts version 1 (no prints, no plan).
// Pure: System, System.IO streams (the caller opens the file), Unity.Mathematics.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class QuickRunsScanDump
    {
        private const string MagicV1 = "RFSCAN01", Magic = "RFSCAN02";

        /// <summary>What a dump holds besides the input: the game's results and how it planned.</summary>
        public sealed class Header
        {
            public string Tag = string.Empty;
            public bool Bare;
            public ulong PlanHash, ResultHash, OptionsPrint;
            public string Status = string.Empty;
            public readonly List<string> UnknownOptions = new List<string>();
            // version 2 (port round 2b-f): the game's per-pass prints, realizer stage prints and its own plan. Null in a version 1 file.
            public int Version = 2;
            public List<RunsFirstPrint> Passes, Stages;
            public RunsFirstResult Plan;
        }

        public static void Write(Stream stream, Header head, AutoPlanInput input, RunsFirstOptions options)
        {
            using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                w.Write(Magic);
                w.Write(head.Tag ?? string.Empty);
                w.Write(head.Bare);
                w.Write(head.PlanHash);
                w.Write(head.ResultHash);
                w.Write(head.OptionsPrint);
                w.Write(head.Status ?? string.Empty);
                HeightSnapshot s = input.Snapshot;
                w.Write(s.X0);
                w.Write(s.Z0);
                w.Write(s.Width);
                w.Write(s.Depth);
                w.Write(s.ResX);
                w.Write(s.ResZ);
                F3(w, s.Scale);
                F3(w, s.Offset);
                int n = s.Width * s.Depth;
                for (int i = 0; i < n; i++)
                    w.Write(s.Heights[i]);
                w.Write(s.Wet != null);
                if (s.Wet != null)
                    w.Write(s.Wet, 0, n);
                Pts(w, input.Region);
                w.Write(input.Obstacles?.Count ?? -1);
                if (input.Obstacles != null)
                    foreach (AutoObstacle ob in input.Obstacles)
                    {
                        w.Write((byte)ob.Kind);
                        Pts(w, ob.Ring);
                    }
                w.Write(input.Pinned?.Count ?? -1);
                if (input.Pinned != null)
                    foreach (PinnedLift l in input.Pinned)
                    {
                        w.Write(l.Key);
                        w.Write(l.TypeIndex);
                        F3(w, l.Bottom);
                        F3(w, l.Top);
                        Pts(w, l.Chords);
                        w.Write(l.Complete);
                    }
                Pts(w, input.Access);
                w.Write(input.TreeLineY);
                w.Write(input.MaxAreaM2);
                w.Write(input.MinAreaM2);
                w.Write(input.MinVerticalM);
                w.Write((byte)input.Mode);
                AutoPlanKnobs k = input.Knobs;
                w.Write((byte)k.Mix);
                w.Write((byte)k.Density);
                w.Write((byte)k.Kinds);
                w.Write(k.RunCap);
                w.Write(k.Seed);
                w.Write(k.Lifts);
                w.Write((byte)k.Home);
                FieldInfo[] fields = OptionFields();
                w.Write(fields.Length);
                foreach (FieldInfo f in fields)
                {
                    w.Write(f.Name);
                    object v = f.GetValue(options);
                    if (v is float fl)
                    {
                        w.Write((byte)'f');
                        w.Write(fl);
                    }
                    else if (v is int i)
                    {
                        w.Write((byte)'i');
                        w.Write(i);
                    }
                    else if (v is bool b)
                    {
                        w.Write((byte)'b');
                        w.Write(b);
                    }
                    else
                        throw new InvalidOperationException($"RunsFirstOptions.{f.Name}: type {f.FieldType.Name} has no dump code");
                }
                Prints(w, head.Passes);
                Prints(w, head.Stages);
                w.Write(head.Plan != null);
                if (head.Plan != null)
                    WritePlan(w, head.Plan);
            }
        }

        public static AutoPlanInput Read(Stream stream, out RunsFirstOptions options, out Header head)
        {
            using (var r = new BinaryReader(stream, Encoding.UTF8, true))
            {
                string magic = r.ReadString();
                if (magic != Magic && magic != MagicV1)
                    throw new InvalidDataException("not a scan dump (magic)");
                head = new Header
                {
                    Tag = r.ReadString(), Bare = r.ReadBoolean(), PlanHash = r.ReadUInt64(), ResultHash = r.ReadUInt64(), OptionsPrint = r.ReadUInt64(),
                    Status = r.ReadString(),
                };
                head.Version = magic == Magic ? 2 : 1;
                int x0 = r.ReadInt32(), z0 = r.ReadInt32(), w = r.ReadInt32(), d = r.ReadInt32(), resX = r.ReadInt32(), resZ = r.ReadInt32();
                float3 scale = F3(r), offset = F3(r);
                HeightSnapshot s = HeightSnapshot.Allocate(x0, z0, w, d, resX, resZ, scale, offset, false);
                for (int i = 0; i < w * d; i++)
                    s.Heights[i] = r.ReadUInt16();
                if (r.ReadBoolean())
                    s.Wet = r.ReadBytes(w * d);
                var input = new AutoPlanInput { Snapshot = s, Region = Pts(r) };
                int no = r.ReadInt32();
                if (no >= 0)
                {
                    input.Obstacles = new List<AutoObstacle>(no);
                    for (int i = 0; i < no; i++)
                        input.Obstacles.Add(new AutoObstacle { Kind = (AutoObstacleKind)r.ReadByte(), Ring = Pts(r) });
                }
                int np = r.ReadInt32();
                if (np >= 0)
                {
                    input.Pinned = new List<PinnedLift>(np);
                    for (int i = 0; i < np; i++)
                        input.Pinned.Add(new PinnedLift
                        {
                            Key = r.ReadUInt32(), TypeIndex = r.ReadByte(), Bottom = F3(r), Top = F3(r), Chords = Pts(r), Complete = r.ReadBoolean(),
                        });
                }
                input.Access = Pts(r);
                input.TreeLineY = r.ReadSingle();
                input.MaxAreaM2 = r.ReadSingle();
                input.MinAreaM2 = r.ReadSingle();
                input.MinVerticalM = r.ReadSingle();
                input.Mode = (AutoPlanMode)r.ReadByte();
                var k = new AutoPlanKnobs
                {
                    Mix = (AutoMix)r.ReadByte(), Density = (AutoDensity)r.ReadByte(), Kinds = (AutoKinds)r.ReadByte(), RunCap = r.ReadInt32(),
                    Seed = r.ReadUInt32(), Lifts = r.ReadInt32(), Home = (HomeLevel)r.ReadByte(),
                };
                input.Knobs = k;
                options = new RunsFirstOptions();
                var byName = new Dictionary<string, FieldInfo>();
                foreach (FieldInfo f in OptionFields())
                    byName[f.Name] = f;
                int nf = r.ReadInt32();
                for (int i = 0; i < nf; i++)
                {
                    string name = r.ReadString();
                    byte code = r.ReadByte();
                    object v = code == 'f' ? r.ReadSingle() : code == 'i' ? r.ReadInt32() : code == 'b' ? (object)r.ReadBoolean()
                        : throw new InvalidDataException($"option {name}: type code {code}");
                    if (byName.TryGetValue(name, out FieldInfo f) && f.FieldType == v.GetType())
                        f.SetValue(options, v);
                    else
                        head.UnknownOptions.Add(name);
                }
                if (head.Version >= 2)
                {
                    head.Passes = Prints(r);
                    head.Stages = Prints(r);
                    if (r.ReadBoolean())
                        head.Plan = ReadPlan(r);
                }
                return input;
            }
        }

        private static void Prints(BinaryWriter w, List<RunsFirstPrint> prints)
        {
            w.Write(prints?.Count ?? -1);
            if (prints == null)
                return;
            foreach (RunsFirstPrint p in prints)
            {
                w.Write(p.Pass ?? string.Empty);
                w.Write(p.Print);
                w.Write(p.Parts.Length);
                for (int i = 0; i < p.Parts.Length; i++)
                {
                    w.Write(p.Parts[i]);
                    w.Write(p.Counts[i]);
                    ulong[] it = p.Items != null && i < p.Items.Length ? p.Items[i] : null;
                    w.Write(it?.Length ?? -1);
                    if (it != null)
                        foreach (ulong e in it)
                            w.Write(e);
                }
            }
        }

        private static List<RunsFirstPrint> Prints(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0)
                return null;
            var l = new List<RunsFirstPrint>(n);
            for (int i = 0; i < n; i++)
            {
                var p = new RunsFirstPrint { Pass = r.ReadString(), Print = r.ReadUInt64() };
                int k = r.ReadInt32();
                p.Parts = new ulong[k];
                p.Counts = new int[k];
                p.Items = new ulong[k][];
                for (int j = 0; j < k; j++)
                {
                    p.Parts[j] = r.ReadUInt64();
                    p.Counts[j] = r.ReadInt32();
                    int ni = r.ReadInt32();
                    if (ni >= 0)
                    {
                        p.Items[j] = new ulong[ni];
                        for (int e = 0; e < ni; e++)
                            p.Items[j][e] = r.ReadUInt64();
                    }
                }
                l.Add(p);
            }
            return l;
        }

        private static void Line(BinaryWriter w, List<float2> p)
        {
            w.Write(p?.Count ?? -1);
            if (p != null)
                foreach (float2 q in p)
                {
                    w.Write(q.x);
                    w.Write(q.y);
                }
        }

        private static List<float2> Line(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0)
                return null;
            var p = new List<float2>(n);
            for (int i = 0; i < n; i++)
                p.Add(new float2(r.ReadSingle(), r.ReadSingle()));
            return p;
        }

        /// <summary>The game's plan, every field of RunsFirstResult (floats keep their bits), so the host can check and score it.</summary>
        private static void WritePlan(BinaryWriter w, RunsFirstResult p)
        {
            w.Write(p.Runs.Count);
            foreach (RunsFirstResult.Run run in p.Runs)
            {
                Line(w, run.Pts);
                w.Write((byte)run.Band);
                w.Write(run.Kind ?? string.Empty);
                w.Write(run.Graded);
                w.Write(run.Groomed);
                w.Write(run.Lift);
            }
            w.Write(p.Connectors.Count);
            foreach (List<float2> c in p.Connectors)
                Line(w, c);
            w.Write(p.Links.Count);
            foreach (List<float2> c in p.Links)
                Line(w, c);
            w.Write(p.Lifts.Count);
            foreach (RunsFirstResult.Lift l in p.Lifts)
            {
                w.Write(l.Id);
                w.Write(l.Type ?? string.Empty);
                w.Write(l.Kind ?? string.Empty);
                F3(w, l.Bottom);
                F3(w, l.Top);
                w.Write(l.RidesDown);
                w.Write((byte)l.ToBottom);
                w.Write(l.Base);
            }
            w.Write(p.Log.Count);
            foreach (string s in p.Log)
                w.Write(s ?? string.Empty);
        }

        private static RunsFirstResult ReadPlan(BinaryReader r)
        {
            var p = new RunsFirstResult();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                p.Runs.Add(new RunsFirstResult.Run
                {
                    Pts = Line(r), Band = (SlopeBand)r.ReadByte(), Kind = r.ReadString(), Graded = r.ReadBoolean(), Groomed = r.ReadBoolean(), Lift = r.ReadInt32(),
                });
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                p.Connectors.Add(Line(r));
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                p.Links.Add(Line(r));
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                p.Lifts.Add(new RunsFirstResult.Lift
                {
                    Id = r.ReadInt32(), Type = r.ReadString(), Kind = r.ReadString(), Bottom = F3(r), Top = F3(r), RidesDown = r.ReadBoolean(),
                    ToBottom = (RunsFirstBottom)r.ReadByte(), Base = r.ReadBoolean(),
                });
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                p.Log.Add(r.ReadString());
            return p;
        }

        private static FieldInfo[] OptionFields()
        {
            FieldInfo[] fields = typeof(RunsFirstOptions).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            return fields;
        }

        private static void F3(BinaryWriter w, float3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        private static float3 F3(BinaryReader r) => new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static void Pts(BinaryWriter w, float2[] p)
        {
            w.Write(p?.Length ?? -1);
            if (p != null)
                foreach (float2 q in p)
                {
                    w.Write(q.x);
                    w.Write(q.y);
                }
        }

        private static float2[] Pts(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0)
                return null;
            var p = new float2[n];
            for (int i = 0; i < n; i++)
                p[i] = new float2(r.ReadSingle(), r.ReadSingle());
            return p;
        }
    }
}
