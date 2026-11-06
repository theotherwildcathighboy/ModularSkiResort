# TowerNumberTiles: the tower number rule, tile prefab layout and the plate frame the tiles are placed in

Pure core of the per-tower number tiles (no Unity.Entities; linked into tests/LiftBatch). The ECS side is `TowerNumberTileSystem`.

## Rule

Every line tower shows its own number, 1..200 in order from the base station along the line (`LiftTowerNumbering`), wrapping back to 1
after 200: tower 201 reads 1, 400 reads 200, 401 reads 1. A long line never overflows: the layout is at most 3 digits for every int.

## Tiles

assets/blender/number_tiles.py is the source of every constant in `TowerNumberTiles`. Per plate class there are 11 one-quad prefabs,
SkiNumTile<cls>Blank (the baked numeral's whole window) and SkiNumTile<cls>N0..N9 (one digit cell = window / 3). Per plate face a number
is 1-3 digit tiles centered in the window (digit i of k at (i - (k-1)/2) cells from the window center); 1-2 digit numbers put the Blank
behind them (digits LayerM in front of it), 3 digits fill the window alone. So <= 3 tiles per face, 6 per tower.

## Frame

Measured from the shipped LOD0 FBXs (number_tiles.py --measure-plates): every numbered tower has two plates, center x 0, front quads
facing +Z and -Z; the +Z face's viewer-right is -X, the -Z face's is +X. The tile prefab faces +Z with its text running along -X
(number_tiles.py FRAME), so the +Z face takes it unrotated and the -Z face rotated 180 deg about Y. Plate rows hold the FRONT quad's
center (6 mm proud of the host).

Twin rows (B2): a twin head (LiftTowerPlanner.md "Heads") keeps its member's mast and plates, so its row is the member's row raised by
the twin's crown change (`TwinPlateRaise`: FG twins 0, DQ Hold +0.3258 / Combo +0.0669, GD Hold +0.427 / Combo +0.143; LiftBatch TN-9).
These 13 rows are derived, not measured: measure the imported twins (number_tiles.py --measure-plates) and replace them. The FG8 twins
have no row because their member (SkiLiftTowerFG2) has none.
