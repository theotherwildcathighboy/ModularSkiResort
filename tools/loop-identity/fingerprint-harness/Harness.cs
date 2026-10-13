// Round-2 integrator: T1-T3 / W1-W3 FG-only fingerprint. The SAME source is compiled against
// (A) git HEAD's SkiResortScore.cs (numerically = Phase A: Phase A changed only LiftValue 6.0 -> the const alias 6.0)
// and (B) the working tree's SkiResortScore.cs (+ SkiCatalog.cs). Inputs = tests/ResortBatch/ScoreTests.cs builders,
// copied verbatim. Every public field of Result and of each RunPart is printed as raw bits (doubles via
// BitConverter.DoubleToInt64Bits), sorted by name; the two outputs are then diffed (B may only ADD lines: the
// Phase B fields) and an FNV-1a 64 is taken over the lines common to both field sets.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using SkiResortMod.Systems;
using Score = SkiResortMod.Systems.SkiResortScore;

public static class Harness
{
    private const byte Gr = Score.KindGroomed, Sm = Score.KindSnowmaking, Ni = Score.KindNight, Na = Score.KindNatural;
    private const byte Flat = Score.BandFlat, Green = Score.BandGreen, Blue = Score.BandBlue,
                       Black = Score.BandBlack, DBlack = Score.BandDoubleBlack;

    private static Score.RunInput R(byte kind, byte band, double areaM2, double topM, double bottomM)
    {
        return new Score.RunInput
        {
            Kind = kind, State = Score.BandState.Rated, Band = band, AreaM2 = areaM2, Served = true,
            OpenFactor = 1.0, OpenEstimated = false, TopM = topM, BottomM = bottomM,
        };
    }

    private static void Add(List<Score.RunInput> runs, int count, Score.RunInput run)
    {
        for (int i = 0; i < count; i++) runs.Add(run);
    }

    private static List<Score.RunInput> Small()
    {
        return new List<Score.RunInput> { R(Gr, Green, 20000, 1150, 1000), R(Gr, Blue, 25000, 1250, 1000), R(Gr, Blue, 20000, 1250, 1050) };
    }

    private static List<Score.RunInput> Medium()
    {
        var runs = new List<Score.RunInput>();
        Add(runs, 3, R(Gr, Green, 25000, 1250, 1000));
        Add(runs, 3, R(Gr, Blue, 30000, 1400, 1000));
        Add(runs, 1, R(Sm, Blue, 30000, 1400, 1000));
        Add(runs, 1, R(Ni, Blue, 25000, 1300, 1000));
        Add(runs, 2, R(Na, Black, 30000, 1500, 1100));
        return runs;
    }

    private static List<Score.RunInput> Large()
    {
        var runs = new List<Score.RunInput>();
        Add(runs, 6, R(Gr, Green, 30000, 1300, 1000));
        Add(runs, 5, R(Gr, Blue, 40000, 1500, 1000));
        Add(runs, 3, R(Sm, Blue, 40000, 1500, 1000));
        Add(runs, 2, R(Ni, Blue, 30000, 1300, 1000));
        Add(runs, 5, R(Na, Black, 40000, 1800, 1200));
        Add(runs, 1, R(Sm, Black, 40000, 1700, 1100));
        Add(runs, 3, R(Na, DBlack, 40000, 1800, 1300));
        return runs;
    }

    private static List<Score.LiftInput> Lifts(int count)
    {
        var lifts = new List<Score.LiftInput>();
        for (int i = 0; i < count; i++) lifts.Add(new Score.LiftInput { Complete = true, Powered = true });
        return lifts;
    }

    private static Score.AreaInput Area(int lodges)
    {
        return new Score.AreaInput
        {
            ActiveLodges = lodges, InactiveLodges = 0, Efficiency = 1.0,
            Settings = new Score.ScoreSettings { K = 4, BackcountryPercent = 40, SnowGating = true },
        };
    }

    private static List<Score.RunInput> WithOpen(List<Score.RunInput> runs, double open)
    {
        var copy = new List<Score.RunInput>(runs.Count);
        for (int i = 0; i < runs.Count; i++) { Score.RunInput r = runs[i]; r.OpenFactor = open; copy.Add(r); }
        return copy;
    }

    private static string Bits(object v)
    {
        if (v is double d) return "0x" + BitConverter.DoubleToInt64Bits(d).ToString("X16") + " (" + d.ToString("R", CultureInfo.InvariantCulture) + ")";
        if (v is float f) return "0x" + BitConverter.SingleToInt32Bits(f).ToString("X8") + " (" + f.ToString("R", CultureInfo.InvariantCulture) + ")";
        if (v is Enum) return Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture) + " (" + v + ")";
        return Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    private static void Dump(string row, object value, List<string> lines)
    {
        FieldInfo[] fs = value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
        var named = new List<string>();
        foreach (FieldInfo f in fs) named.Add(row + "." + f.Name + " = " + Bits(f.GetValue(value)));
        named.Sort(StringComparer.Ordinal);
        lines.AddRange(named);
    }

    public static void Main()
    {
        var lines = new List<string>();
        var rows = new List<Tuple<string, Score.AreaInput, List<Score.RunInput>, List<Score.LiftInput>>>
        {
            Tuple.Create("T1", Area(1), Small(), Lifts(1)),
            Tuple.Create("T2", Area(2), Medium(), Lifts(3)),
            Tuple.Create("T3", Area(3), Large(), Lifts(6)),
            Tuple.Create("W1", Area(1), WithOpen(Small(), 0), Lifts(1)),
            Tuple.Create("W2", Area(2), WithOpen(Medium(), 0), Lifts(3)),
            Tuple.Create("W3", Area(3), WithOpen(Large(), 0), Lifts(6)),
        };
        foreach (var row in rows)
        {
            var parts = new List<Score.RunPart>();
            Score.Result r = Score.Compute(row.Item2, row.Item3, row.Item4, parts);
            Dump(row.Item1 + ".Result", r, lines);
            for (int i = 0; i < parts.Count; i++)
                Dump(row.Item1 + ".Part" + i.ToString("00", CultureInfo.InvariantCulture), parts[i], lines);
        }
        foreach (string l in lines) Console.WriteLine(l);
    }
}
