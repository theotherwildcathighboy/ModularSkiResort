// Pure core of the per-tower number tiles (no Unity.Entities; linked into tests/LiftBatch). The ECS side is
// TowerNumberTileSystem (route V1 of docs/research/dynamic-numbers.md).
//
// RULE: every line tower shows its own number, 1..200 in order from the base station along the line (LiftTowerNumbering),
// wrapping back to 1 after 200: tower 201 reads 1, 400 reads 200, 401 reads 1. A long line never overflows: the layout is
// at most 3 digits for every int.
//
// TILES (assets/blender/number_tiles.py is the source of every constant below): per plate class 11 one-quad prefabs,
// SkiNumTile<cls>Blank (the baked numeral's whole window) and SkiNumTile<cls>N0..N9 (one digit cell = window / 3). Per plate
// face a number is 1-3 digit tiles centered in the window (digit i of k at (i - (k-1)/2) cells from the window center); 1-2
// digit numbers put the Blank behind them (digits LayerM in front of it), 3 digits fill the window alone. So <= 3 tiles per
// face, 6 per tower.
//
// FRAME (measured from the shipped LOD0 FBXs, number_tiles.py --measure-plates): every numbered tower has two plates,
// center x 0, front quads facing +Z and -Z; the +Z face's viewer-right is -X, the -Z face's is +X. The tile prefab faces +Z
// with its text running along -X (number_tiles.py FRAME), so the +Z face takes it unrotated and the -Z face rotated 180 deg
// about Y. Plate rows hold the FRONT quad's center (6 mm proud of the host).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class TowerNumberTiles
    {
        public const int MaxNumber = 200;
        public const int MaxDigits = 3;
        public const int Blank = -1;
        public const int GlyphsPerClass = 11;          // Blank + 0..9
        public const float TileDepthM = 0.004f;        // number_tiles.py TILE_DEPTH_M: Blank / 3-digit tiles off the plate's front quad
        public const float LayerM = 0.003f;            // number_tiles.py LAYER_M: digits in front of the Blank
        public const float MatchTolM = 0.01f;          // an existing tile within 1 cm of its slot is kept

        /// <summary>One plate class (number_tiles.py CLASSES; win_u = the numeral window's center right of the plate center,
        /// cell = window width / 3, h = the window height).</summary>
        public sealed class TileClass
        {
            public readonly string Id;
            public readonly float WinU, CellW, WinH;
            public TileClass(string id, float winU, float winW, float winH)
            {
                Id = id;
                WinU = winU;
                CellW = winW / MaxDigits;
                WinH = winH;
            }
        }

        public static readonly TileClass[] Classes =
        {
            new TileClass("T1", 0.0740f, 0.200f, 0.148f),   // tube FG / DQ / GD: plate 0.36 x 0.16 (lift_tower.plate_raster)
            new TileClass("T2", 0.0900f, 0.240f, 0.188f),   // gondola lattice: plate 0.44 x 0.20 (tram_tower.plate_layout)
            new TileClass("T3", 0.1200f, 0.300f, 0.248f),   // tram lattice: plate 0.56 x 0.26 (tram_tower.plate_layout)
        };

        /// <summary>A numbered tower prefab's plates (prefab frame, meters): front-quad center y and the z of its +Z / -Z faces.</summary>
        public readonly struct TowerPlate
        {
            public readonly string Prefab;
            public readonly int Class;
            public readonly float Y, ZPos, ZNeg;
            public TowerPlate(string prefab, int cls, float y, float zPos, float zNeg)
            {
                Prefab = prefab;
                Class = cls;
                Y = y;
                ZPos = zPos;
                ZNeg = zNeg;
            }
        }

        // Measured from the shipped LOD0 FBXs (number_tiles.py --measure-plates; meshes FG/DQ/GD/Lattice letter P, tram B, as the
        // hand-made prefabs reference them). Hosted terminal towers (ET62 / ET68 / ETDQ / ETGD) carry a glyph-only plate: not numbered.
        public static readonly TowerPlate[] Plates =
        {
            new TowerPlate("SkiLiftTowerFG12", 0, 12.000f, 0.1760f, -0.1760f),
            new TowerPlate("SkiLiftTowerFG16", 0, 16.000f, 0.1760f, -0.1760f),
            new TowerPlate("SkiLiftTowerFG20", 0, 20.000f, 0.1760f, -0.1760f),
            new TowerPlate("SkiLiftTowerDQ10", 0, 9.808f, 0.1548f, -0.1573f),
            new TowerPlate("SkiLiftTowerDQ12", 0, 11.808f, 0.1548f, -0.1573f),
            new TowerPlate("SkiLiftTowerDQ14", 0, 13.808f, 0.1548f, -0.1573f),
            new TowerPlate("SkiLiftTowerDQ16", 0, 15.808f, 0.1548f, -0.1573f),
            new TowerPlate("SkiLiftTowerDQ18", 0, 17.808f, 0.1548f, -0.1573f),
            new TowerPlate("SkiLiftTowerDQ20", 0, 19.808f, 0.1548f, -0.1573f),
            new TowerPlate("SkiLiftTowerGD12", 0, 11.859f, 0.1560f, -0.1560f),
            new TowerPlate("SkiLiftTowerGD16", 0, 15.859f, 0.1560f, -0.1560f),
            new TowerPlate("SkiLiftTowerGD20", 0, 19.859f, 0.1560f, -0.1560f),
            new TowerPlate("SkiGondolaLattice25", 1, 24.880f, 0.1260f, -0.1260f),
            new TowerPlate("SkiGondolaLattice32", 1, 31.880f, 0.1260f, -0.1260f),
            new TowerPlate("SkiGondolaLattice40", 1, 39.880f, 0.1260f, -0.1260f),
            new TowerPlate("SkiTramTower30", 2, 29.850f, 1.6560f, -1.6560f),
            new TowerPlate("SkiTramTower45", 2, 44.850f, 1.6560f, -1.6560f),
            new TowerPlate("SkiTramTower60", 2, 59.850f, 1.6560f, -1.6560f),
        };

        /// <summary>The tile prefab name: SkiNumTile&lt;cls&gt;Blank or SkiNumTile&lt;cls&gt;N&lt;d&gt; (number_tiles.tile_name).</summary>
        public static string PrefabName(int cls, int glyph) =>
            "SkiNumTile" + Classes[cls].Id + (glyph == Blank ? "Blank" : "N" + glyph.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>Dense key of (class, glyph): cls * 11 + glyph + 1 (Blank = 0).</summary>
        public static int Key(int cls, int glyph) => cls * GlyphsPerClass + glyph + 1;

        public static bool TryPlate(string prefab, out TowerPlate plate)
        {
            for (int i = 0; i < Plates.Length; i++)
            {
                if (string.Equals(Plates[i].Prefab, prefab, StringComparison.Ordinal))
                {
                    plate = Plates[i];
                    return true;
                }
            }
            plate = default;
            return false;
        }

        /// <summary>1..200 for a rank >= 1 (201 -> 1, 400 -> 200); 0 for a rank &lt; 1 (no tiles).</summary>
        public static int Wrap(int rank) => rank < 1 ? 0 : (rank - 1) % MaxNumber + 1;

        /// <summary>The order rule (LiftTowerNumbering) with the wrap: one display number per tower.</summary>
        public static int[] DisplayNumbers(IList<float> alongFromBaseM, bool baseIsFirst = true)
        {
            int[] n = LiftTowerNumbering.Numbers(alongFromBaseM, baseIsFirst);
            for (int i = 0; i < n.Length; i++)
                n[i] = Wrap(n[i]);
            return n;
        }

        public struct Slot
        {
            public int Glyph;           // Blank or 0..9
            public float OffsetCells;   // from the window center, in cell widths, toward the viewer's right
            public bool Front;          // a digit layered in front of the Blank
        }

        /// <summary>The tiles of one face for display number n (wrapped first): [Blank] + its digits, left to right.</summary>
        public static void Layout(int number, List<Slot> into)
        {
            into.Clear();
            int n = Wrap(number);
            if (n == 0)
                return;
            string s = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int k = s.Length;
            bool blank = k < MaxDigits;
            if (blank)
                into.Add(new Slot { Glyph = Blank, OffsetCells = 0f, Front = false });
            for (int i = 0; i < k; i++)
                into.Add(new Slot { Glyph = s[i] - '0', OffsetCells = i - (k - 1) * 0.5f, Front = blank });
        }

        public struct TileSpec
        {
            public int Key;               // Key(cls, glyph)
            public int Class, Glyph;
            public float3 LocalPos;       // in the tower's prefab frame
            public quaternion LocalRot;
        }

        /// <summary>Both faces' tiles of a tower plate showing <paramref name="number"/>.</summary>
        public static void Desired(TowerPlate plate, int number, List<TileSpec> into, List<Slot> scratch = null)
        {
            into.Clear();
            scratch = scratch ?? new List<Slot>(4);
            Layout(number, scratch);
            TileClass c = Classes[plate.Class];
            for (int face = 0; face < 2; face++)
            {
                float s = face == 0 ? 1f : -1f;                 // +Z face, then -Z face
                float zFace = face == 0 ? plate.ZPos : plate.ZNeg;
                quaternion rot = face == 0 ? quaternion.identity : quaternion.RotateY(math.PI);
                for (int i = 0; i < scratch.Count; i++)
                {
                    Slot sl = scratch[i];
                    float u = c.WinU + sl.OffsetCells * c.CellW;   // toward the viewer's right: -X on +Z, +X on -Z
                    float depth = TileDepthM + (sl.Front ? LayerM : 0f);
                    into.Add(new TileSpec
                    {
                        Key = Key(plate.Class, sl.Glyph), Class = plate.Class, Glyph = sl.Glyph,
                        LocalPos = new float3(-s * u, plate.Y, zFace + s * depth), LocalRot = rot,
                    });
                }
            }
        }

        public struct Existing
        {
            public int Key;
            public float3 LocalPos;
        }

        /// <summary>The per-owner diff: an existing tile is kept when a desired tile of the same key sits within MatchTolM of it
        /// (each desired matches at most one, nearest first); everything else is deleted / created. A changed digit is delete +
        /// create, never an in-place PrefabRef swap (docs/research/dynamic-numbers.md §3).</summary>
        public static void Reconcile(IList<Existing> existing, IList<TileSpec> desired, List<int> deleteIdx, List<int> createIdx)
        {
            deleteIdx.Clear();
            createIdx.Clear();
            var used = new bool[existing.Count];
            for (int d = 0; d < desired.Count; d++)
            {
                int best = -1;
                float bestDist = MatchTolM;
                for (int e = 0; e < existing.Count; e++)
                {
                    if (used[e] || existing[e].Key != desired[d].Key)
                        continue;
                    float dist = math.distance(existing[e].LocalPos, desired[d].LocalPos);
                    if (dist <= bestDist)
                    {
                        bestDist = dist;
                        best = e;
                    }
                }
                if (best >= 0)
                    used[best] = true;
                else
                    createIdx.Add(d);
            }
            for (int e = 0; e < existing.Count; e++)
                if (!used[e])
                    deleteIdx.Add(e);
        }
    }
}
