// Quick runs tool: the scan dump (port round R2-a, plan 7.4 and the round's cross-runtime gate). The tool writes the exact input of its
// last scan (snapshot with its wet layer, region, obstacles, existing lifts, access points, knobs, the planner options, the bare flag)
// and the hashes it got. The test host reads the file (mode rf-replay), runs the same QuickRunsScanJob and prints both hashes. Equal
// hashes prove that the game plans what the host scores.
// Binary, little endian (BinaryWriter): floats keep their bits. Options by field name, so a file stays readable when an option is
// added (a field the file lacks keeps its default, a field the build lacks is reported and skipped).
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
        private const string Magic = "RFSCAN01";

        /// <summary>What a dump holds besides the input: the game's results and how it planned.</summary>
        public sealed class Header
        {
            public string Tag = string.Empty;
            public bool Bare;
            public ulong PlanHash, ResultHash, OptionsPrint;
            public string Status = string.Empty;
            public readonly List<string> UnknownOptions = new List<string>();
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
            }
        }

        public static AutoPlanInput Read(Stream stream, out RunsFirstOptions options, out Header head)
        {
            using (var r = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (r.ReadString() != Magic)
                    throw new InvalidDataException("not a scan dump (magic)");
                head = new Header
                {
                    Tag = r.ReadString(), Bare = r.ReadBoolean(), PlanHash = r.ReadUInt64(), ResultHash = r.ReadUInt64(), OptionsPrint = r.ReadUInt64(),
                    Status = r.ReadString(),
                };
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
                return input;
            }
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
