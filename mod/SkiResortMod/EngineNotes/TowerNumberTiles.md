# TowerNumberTiles: the tower number rule, tile prefab layout and the plate frame the tiles are placed in

Pure core of the per-tower number tiles (no Unity.Entities). It is linked into tests/LiftBatch. The ECS side is `TowerNumberTileSystem`.

## Rule

Every line tower shows its own number, 1..200 in order from the base station along the line (`LiftTowerNumbering`). The number wraps back to 1
after 200: tower 201 reads 1, 400 reads 200, 401 reads 1. A long line never overflows, because the layout is at most 3 digits for every int.

## Tiles

assets/blender/number_tiles.py is the source of every constant in `TowerNumberTiles`. Per plate class there are 11 one-quad prefabs:
- SkiNumTile<cls>Blank is the baked numeral's whole window.
- SkiNumTile<cls>N0..N9 is one digit cell (window / 3).

Per plate face, a number is 1-3 digit tiles centered in the window (digit i of k at (i - (k-1)/2) cells from the window center). A 1-2 digit
number puts the Blank behind the digits (digits LayerM in front of it). A 3 digit number fills the window alone. So a face has <= 3 tiles
and a tower has 6.

## Frame

The frame was measured from the shipped LOD0 FBXs (number_tiles.py --measure-plates). Every numbered tower has two plates with center x 0.
The front quads face +Z and -Z. The +Z face's viewer-right is -X. The -Z face's viewer-right is +X. The tile prefab faces +Z with its text
running along -X (number_tiles.py FRAME). So the +Z face takes the tile unrotated, and the -Z face takes it rotated 180 deg about Y.
Plate rows hold the FRONT quad's center (6 mm proud of the host).

Twin rows (B2): a twin head (LiftTowerPlanner.md "Heads") keeps its member's mast and plates. So its row is the member's row raised by
the twin's crown change (`TwinPlateRaise`: FG twins 0, DQ Hold +0.3258 / Combo +0.0669, GD Hold +0.427 / Combo +0.143. See LiftBatch TN-9).
These 13 rows are derived, not measured. Measure the imported twins (number_tiles.py --measure-plates) and replace them. The FG8 twins
have no row because their member (SkiLiftTowerFG2) has none.
