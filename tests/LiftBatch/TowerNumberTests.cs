// Headless tests for the pure tower number tile core (TowerNumberTiles.cs), cases TN-1..TN-9: wrap to 1..200,
// tile layout per digit count, display numbers by rank (reversed when the base is last), plate geometry per face,
// reconcile (minimal deletes and creates), renumbering after a section edit, name and plate tables, a 1,000-tower
// line, and the plate table against docs/research/lifts/tower-numbers-plates-2026-10-20.json (measured from the
// shipped FBXs: a re-export that moves a plate fails here until the table follows).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    internal static class TowerNumberTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private static bool Check(bool ok, string where, string msg) => s_Check(ok, where, msg);

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Wrap();
            Layout();
            Display();
            Geometry();
            Reconcile();
            RenumberAfterEdit();
            Tables();
            LongLine();
            PlatesMatchMeasurement();
        }

        private static void Wrap()
        {
            const string W = "TN-1 wrap";
            Check(TowerNumberTiles.Wrap(1) == 1 && TowerNumberTiles.Wrap(199) == 199 && TowerNumberTiles.Wrap(200) == 200, W, "1..200 as is");
            Check(TowerNumberTiles.Wrap(201) == 1 && TowerNumberTiles.Wrap(202) == 2, W, "201 -> 1, 202 -> 2");
            Check(TowerNumberTiles.Wrap(400) == 200 && TowerNumberTiles.Wrap(401) == 1, W, "400 -> 200, 401 -> 1");
            int big = TowerNumberTiles.Wrap(int.MaxValue);
            Check(big >= 1 && big <= 200, W, "int.MaxValue -> " + big);
            Check(TowerNumberTiles.Wrap(0) == 0 && TowerNumberTiles.Wrap(-7) == 0, W, "< 1 -> 0");
        }

        private static string Glyphs(List<TowerNumberTiles.Slot> s)
        {
            var parts = new List<string>();
            foreach (TowerNumberTiles.Slot x in s)
                parts.Add((x.Glyph == TowerNumberTiles.Blank ? "B" : x.Glyph.ToString()) + "@" + x.OffsetCells.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + (x.Front ? "f" : ""));
            return string.Join(" ", parts);
        }

        private static void Layout()
        {
            const string W = "TN-2 layout";
            var s = new List<TowerNumberTiles.Slot>();
            TowerNumberTiles.Layout(7, s);
            Check(Glyphs(s) == "B@0.0 7@0.0f", W, "7: " + Glyphs(s));
            TowerNumberTiles.Layout(42, s);
            Check(Glyphs(s) == "B@0.0 4@-0.5f 2@0.5f", W, "42: " + Glyphs(s));
            TowerNumberTiles.Layout(128, s);
            Check(Glyphs(s) == "1@-1.0 2@0.0 8@1.0", W, "128: " + Glyphs(s));
            TowerNumberTiles.Layout(200, s);
            Check(Glyphs(s) == "2@-1.0 0@0.0 0@1.0", W, "200: " + Glyphs(s));
            TowerNumberTiles.Layout(201, s);
            Check(Glyphs(s) == "B@0.0 1@0.0f", W, "201 shows 1: " + Glyphs(s));
            TowerNumberTiles.Layout(0, s);
            Check(s.Count == 0, W, "0: no tiles");
            int bad = 0;
            for (int n = 1; n <= 2000; n++)
            {
                TowerNumberTiles.Layout(n, s);
                int digits = TowerNumberTiles.Wrap(n).ToString().Length;
                bool hasBlank = s.Count > 0 && s[0].Glyph == TowerNumberTiles.Blank;
                bool ok = s.Count <= 3 && hasBlank == (digits < 3) && s.Count == digits + (hasBlank ? 1 : 0);
                foreach (TowerNumberTiles.Slot x in s)
                    ok &= Math.Abs(x.OffsetCells) + 0.5f <= 1.5f + 1e-5f;
                if (!ok)
                    bad++;
            }
            Check(bad == 0, W, bad + " of 2000 numbers break the <= 3 tiles / inside-window / Blank rules");
        }

        private static void Display()
        {
            const string W = "TN-3 display";
            var rng = new System.Random(123);
            int n = 450;
            var along = new float[n];
            var order = new int[n];
            for (int i = 0; i < n; i++)
                order[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            for (int i = 0; i < n; i++)
                along[order[i]] = 10f + 30f * i;      // input index order[i] is rank i + 1
            int[] num = TowerNumberTiles.DisplayNumbers(along);
            int bad = 0;
            for (int i = 0; i < n; i++)
                if (num[order[i]] != TowerNumberTiles.Wrap(i + 1))
                    bad++;
            Check(bad == 0, W, bad + " of 450 towers show the wrong number");
            int[] rev = TowerNumberTiles.DisplayNumbers(along, baseIsFirst: false);
            Check(rev[order[n - 1]] == 1 && rev[order[0]] == TowerNumberTiles.Wrap(n), W,
                  "base last: the farthest tower is 1, the nearest " + TowerNumberTiles.Wrap(n) + " (got " + rev[order[n - 1]] + " / " + rev[order[0]] + ")");
            Check(num[order[200]] == 1 && num[order[199]] == 200, W, "tower 201 reads 1, tower 200 reads 200");
        }

        private static void Geometry()
        {
            const string W = "TN-4 geometry";
            var tiles = new List<TowerNumberTiles.TileSpec>();
            int bad = 0;
            string first = null;
            foreach (TowerNumberTiles.TowerPlate p in TowerNumberTiles.Plates)
            {
                TowerNumberTiles.TileClass c = TowerNumberTiles.Classes[p.Class];
                foreach (int n in new[] { 7, 42, 128, 200 })
                {
                    TowerNumberTiles.Desired(p, n, tiles);
                    int perFace = n < 100 ? (n < 10 ? 2 : 3) : 3;
                    bool ok = tiles.Count == 2 * perFace;
                    for (int i = 0; i < tiles.Count; i++)
                    {
                        TowerNumberTiles.TileSpec t = tiles[i];
                        bool front = i < perFace;
                        float s = front ? 1f : -1f;
                        float zFace = front ? p.ZPos : p.ZNeg;
                        bool layered = n < 100 && t.Glyph != TowerNumberTiles.Blank;
                        float depth = TowerNumberTiles.TileDepthM + (layered ? TowerNumberTiles.LayerM : 0f);
                        ok &= Math.Abs(t.LocalPos.z - (zFace + s * depth)) < 1e-5f;
                        ok &= Math.Abs(t.LocalPos.y - p.Y) < 1e-5f;
                        float w = t.Glyph == TowerNumberTiles.Blank ? 3f * c.CellW : c.CellW;
                        float u = -s * t.LocalPos.x;                               // along the face's viewer-right
                        ok &= u - w / 2f >= c.WinU - 1.5f * c.CellW - 1e-5f && u + w / 2f <= c.WinU + 1.5f * c.CellW + 1e-5f;
                        float3 fwd = math.mul(t.LocalRot, new float3(0f, 0f, 1f));
                        float3 right = math.mul(t.LocalRot, new float3(-1f, 0f, 0f));   // the tile's text axis
                        ok &= math.distance(fwd, new float3(0f, 0f, s)) < 1e-4f && math.distance(right, new float3(-s, 0f, 0f)) < 1e-4f;
                    }
                    if (!ok)
                    {
                        bad++;
                        first = first ?? (p.Prefab + " #" + n);
                    }
                }
            }
            Check(bad == 0, W, bad + " plate x number layouts off (first " + first + ")");
            // the window center of a 1-digit number sits WinU right of the plate center on both faces
            TowerNumberTiles.Desired(TowerNumberTiles.Plates[0], 7, tiles);
            Check(Math.Abs(tiles[1].LocalPos.x + TowerNumberTiles.Classes[0].WinU) < 1e-5f && Math.Abs(tiles[3].LocalPos.x - TowerNumberTiles.Classes[0].WinU) < 1e-5f,
                  W, "7 on FG12: +Z digit at x -WinU, -Z digit at x +WinU");
        }

        private static List<TowerNumberTiles.Existing> AsExisting(List<TowerNumberTiles.TileSpec> d, float3 shift = default)
        {
            var e = new List<TowerNumberTiles.Existing>();
            foreach (TowerNumberTiles.TileSpec t in d)
                e.Add(new TowerNumberTiles.Existing { Key = t.Key, LocalPos = t.LocalPos + shift });
            return e;
        }

        private static void Diff(TowerNumberTiles.TowerPlate p, int from, int to, out int del, out int add, float3 shift = default)
        {
            var a = new List<TowerNumberTiles.TileSpec>();
            var b = new List<TowerNumberTiles.TileSpec>();
            TowerNumberTiles.Desired(p, from, a);
            TowerNumberTiles.Desired(p, to, b);
            var d = new List<int>();
            var c = new List<int>();
            TowerNumberTiles.Reconcile(AsExisting(a, shift), b, d, c);
            del = d.Count;
            add = c.Count;
        }

        private static void Reconcile()
        {
            const string W = "TN-5 reconcile";
            TowerNumberTiles.TowerPlate p = TowerNumberTiles.Plates[3];   // asymmetric faces
            Diff(p, 42, 42, out int d, out int a);
            Check(d == 0 && a == 0, W, "42 -> 42: -" + d + " +" + a);
            Diff(p, 42, 43, out d, out a);
            Check(d == 2 && a == 2, W, "42 -> 43: one digit per face (-" + d + " +" + a + ")");
            Diff(p, 99, 100, out d, out a);
            Check(d == 6 && a == 6, W, "99 -> 100: Blank + two digits go, three come, per face (-" + d + " +" + a + ")");
            Diff(p, 7, 8, out d, out a);
            Check(d == 2 && a == 2, W, "7 -> 8: the Blank stays (-" + d + " +" + a + ")");
            Diff(p, 42, 42, out d, out a, new float3(0.02f, 0f, 0f));
            Check(d == 6 && a == 6, W, "a 2 cm shift re-makes every tile (-" + d + " +" + a + ")");
            Diff(p, 42, 42, out d, out a, new float3(0.004f, 0f, 0f));
            Check(d == 0 && a == 0, W, "a 4 mm shift is within tolerance (-" + d + " +" + a + ")");
            var want = new List<TowerNumberTiles.TileSpec>();
            TowerNumberTiles.Desired(p, 5, want);
            List<TowerNumberTiles.Existing> ex = AsExisting(want);
            ex.AddRange(AsExisting(want));                                  // every tile twice
            var dl = new List<int>();
            var cr = new List<int>();
            TowerNumberTiles.Reconcile(ex, want, dl, cr);
            Check(dl.Count == want.Count && cr.Count == 0, W, "duplicates: the extra copy of each is deleted (-" + dl.Count + " +" + cr.Count + ")");
            TowerNumberTiles.Reconcile(new List<TowerNumberTiles.Existing>(), want, dl, cr);
            Check(dl.Count == 0 && cr.Count == want.Count, W, "a fresh tower: every tile created");
            TowerNumberTiles.Reconcile(AsExisting(want), new List<TowerNumberTiles.TileSpec>(), dl, cr);
            Check(dl.Count == want.Count && cr.Count == 0, W, "nothing wanted: every tile deleted");
        }

        private static void RenumberAfterEdit()
        {
            const string W = "TN-6 renumber after an edit";
            var before = new List<float>();
            for (int i = 0; i < 10; i++)
                before.Add(40f * (i + 1));
            var after = new List<float>(before) { 135f };                   // between tower 3 (120) and tower 4 (160)
            int[] nb = TowerNumberTiles.DisplayNumbers(before);
            int[] na = TowerNumberTiles.DisplayNumbers(after);
            TowerNumberTiles.TowerPlate p = TowerNumberTiles.Plates[0];
            int changed = 0, wrong = 0;
            for (int i = 0; i < 10; i++)
            {
                Diff(p, nb[i], na[i], out int d, out int a);
                if (d + a > 0)
                {
                    changed++;
                    if (i < 3)
                        wrong++;
                }
            }
            Check(changed == 7 && wrong == 0 && na[10] == 4, W, "towers 4..10 renumbered, 1..3 untouched, the new one is 4 (changed " +
                  changed + ", upstream touched " + wrong + ", new " + na[10] + ")");
        }

        private static void Tables()
        {
            const string W = "TN-7 tables";
            Check(TowerNumberTiles.PrefabName(0, TowerNumberTiles.Blank) == "SkiNumTileT1Blank" && TowerNumberTiles.PrefabName(2, 9) == "SkiNumTileT3N9",
                  W, "names " + TowerNumberTiles.PrefabName(0, TowerNumberTiles.Blank) + " / " + TowerNumberTiles.PrefabName(2, 9));
            var keys = new HashSet<int>();
            var names = new HashSet<string>();
            for (int c = 0; c < TowerNumberTiles.Classes.Length; c++)
                for (int g = TowerNumberTiles.Blank; g <= 9; g++)
                {
                    keys.Add(TowerNumberTiles.Key(c, g));
                    names.Add(TowerNumberTiles.PrefabName(c, g));
                }
            int total = TowerNumberTiles.Classes.Length * TowerNumberTiles.GlyphsPerClass;
            bool dense = keys.Count == total && keys.Contains(0) && keys.Contains(total - 1);
            Check(dense && names.Count == total, W, "keys 0.." + (total - 1) + " dense and unique, names unique");
            var rows = new HashSet<string>();
            bool ok = true;
            foreach (TowerNumberTiles.TowerPlate p in TowerNumberTiles.Plates)
                ok &= rows.Add(p.Prefab) && p.ZPos > 0f && p.ZNeg < 0f && p.Class >= 0 && p.Class < TowerNumberTiles.Classes.Length && p.Y > 5f;
            Check(ok && rows.Count == 18, W, "18 unique plate rows, +Z / -Z faces, valid class");
            Check(TowerNumberTiles.TryPlate("SkiLiftTowerGD16", out TowerNumberTiles.TowerPlate gd) && gd.Class == 0 &&
                  !TowerNumberTiles.TryPlate("SkiLiftTerminalTowerBase", out _), W, "lookup: GD16 is T1, a hosted terminal tower has no row");
        }

        private static void LongLine()
        {
            const string W = "TN-8 long line";
            var along = new List<float>();
            for (int i = 0; i < 1000; i++)
                along.Add(12f * i);
            int[] num = TowerNumberTiles.DisplayNumbers(along);
            var tiles = new List<TowerNumberTiles.TileSpec>();
            bool ok = true;
            int maxTiles = 0;
            for (int i = 0; i < num.Length; i++)
            {
                ok &= num[i] >= 1 && num[i] <= 200;
                TowerNumberTiles.Desired(TowerNumberTiles.Plates[i % TowerNumberTiles.Plates.Length], num[i], tiles);
                maxTiles = Math.Max(maxTiles, tiles.Count);
            }
            Check(ok && maxTiles == 6 && num[999] == TowerNumberTiles.Wrap(1000), W, "1,000 towers: numbers in 1..200, at most " + maxTiles + " tiles per tower");
        }

        private static void PlatesMatchMeasurement()
        {
            const string W = "TN-9 plates = measurement";
            string path = CatalogTests.RepoFile("docs/research/lifts/tower-numbers-plates-2026-10-20.json");
            if (!Check(path != null, W, "tower-numbers-plates-2026-10-20.json not found from " + Directory.GetCurrentDirectory()))
                return;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            int bad = 0, seen = 0;
            string first = null;
            foreach (TowerNumberTiles.TowerPlate p in TowerNumberTiles.Plates)
            {
                if (!doc.RootElement.TryGetProperty(p.Prefab, out JsonElement row))
                {
                    bad++;
                    first = first ?? p.Prefab + " missing";
                    continue;
                }
                seen++;
                JsonElement plates = row.GetProperty("plates");
                JsonElement pos = plates[0].GetProperty("centre"), neg = plates[1].GetProperty("centre");
                bool ok = row.GetProperty("class").GetString() == TowerNumberTiles.Classes[p.Class].Id &&
                          plates[0].GetProperty("normal")[2].GetDouble() > 0.99 && plates[1].GetProperty("normal")[2].GetDouble() < -0.99 &&
                          plates[0].GetProperty("right")[0].GetDouble() < -0.99 && plates[1].GetProperty("right")[0].GetDouble() > 0.99 &&
                          Math.Abs(pos[0].GetDouble()) < 1e-3 && Math.Abs(neg[0].GetDouble()) < 1e-3 &&
                          Math.Abs(pos[1].GetDouble() - p.Y) < 1e-3 && Math.Abs(neg[1].GetDouble() - p.Y) < 1e-3 &&
                          Math.Abs(pos[2].GetDouble() - p.ZPos) < 1e-3 && Math.Abs(neg[2].GetDouble() - p.ZNeg) < 1e-3;
                if (!ok)
                {
                    bad++;
                    first = first ?? p.Prefab;
                }
            }
            Check(bad == 0 && seen == 18, W, bad + " plate row(s) differ from the measurement (first " + first + "), " + seen + " compared");
        }
    }
}
