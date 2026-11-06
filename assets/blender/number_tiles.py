"""
number_tiles.py — per-tower number tiles (T123, DECISIONS 358; route V1 of docs/research/dynamic-numbers.md).

    blender --background --factory-startup --python-exit-code 1 --python number_tiles.py -- \
            --preview <dir>                       # PREVIEW GATE: tiles composed over each class's plate, no FBX
    blender ... --python number_tiles.py -- --measure-plates <out.json>     # re-measure the shipped plates
    blender ... --python number_tiles.py -- --export --out ../import/numtiles/<LETTER>   # after the owner's verdict ONLY

WHAT IT MAKES. Per tower plate class, 11 one-quad tiles: `SkiNumTile<cls>Blank` (covers the baked numeral's whole
window) and `SkiNumTile<cls>N0..N9` (one digit cell, a third of the window wide). The code mod (TowerNumberTileSystem)
hangs them on every line tower as Secondary sub-objects: per plate face a number 1..200 is drawn as 1-3 digit tiles
centered in the window; for 1-2 digits the Blank tile sits behind them (digits LAYER_M in front of it), 3 digits fill
the window on their own. So at most 3 tiles per face, exact centering for every count, and the baked plate (glyph +
height numeral) is the fallback when the tiles are off or the prefabs are missing.

CLASSES (the shipped plates; frames and numbers MEASURED from the shipped LOD0 FBXs with --measure-plates, 2026-10-20,
docs/research/lifts/tower-numbers-build-2026-10-20.md §1):
    T1 tube     FG8/12/16/20, DQ10-20, GD12/16/20 and the 16 B2 head twins   plate 0.36 x 0.16  (lift_tower.py DP1_PLATE,
                plate_raster; FG8 and the twins measured 2026-11-04 on their shipped Q / R meshes, SHIPPED_T1)
    T2 lattice  SkiGondolaLattice25/32/40          plate 0.44 x 0.20  (tram_tower.py PLATE["gondola"], plate_layout)
    T3 lattice  SkiTramTower30/45/60               plate 0.56 x 0.26  (tram_tower.py PLATE["tram"], plate_layout)
Hosted terminal towers (ET62 / ET68 / ETDQ / ETGD) are not numbered (glyph-only plate, lift_tower.py plate_numeral).
Chairs (T124) will add a class C here later; the tile system is class-agnostic.

FIELD / INK / FONT = the baked plate's, measured on the shipped textures (FG12P / GD16P / Lattice25P / Tram30B plate
texels): field sRGB 0.851, ink sRGB 0.051 (T1) / 0.039 (T2, T3), MaskMap R (metal) 0.0 (T1) / 0.349 (T2, T3), A
(smoothness) 0.555; ControlMask 0 (fixed colors, never recolored). Font = Blender's built-in Bfont, the one
welcome_sign.text_triangles / tram_tower._text_coverage paint the baked numeral with.

FRAME (load-bearing). The tile quad is authored in Blender's XZ plane facing -Y, text left -> right along +X, up +Z,
origin at the quad center, and exported WITHOUT orient_for_net. export_fbx maps (x, y, z) -> (x, z, -y) and the game
negates FBX x (PIPELINE "Frame rule", DECISIONS 245), so in the prefab frame the tile faces +Z, reads left -> right
along -X, up +Y: exactly the shipped plate's +Z face (measured: normal (0, 0, 1), viewer-right (-1, 0, 0)). The -Z
face takes the tile rotated 180 deg about Y.
"""

import argparse
import json
import math
import os
import sys
from types import SimpleNamespace

import bmesh
import bpy
import numpy as np

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402

# -- the classes ---------------------------------------------------------------------------------------------------
# window = the baked numeral's area, meters in the plate's own frame (u = viewer-right from the plate center, v = up):
#   T1: plate_raster puts the glyph square in [b + 1 px, b + inner_h - 1 px] and centers the numeral in
#       [glyph right + 2 px, w - b - 2 px] (b = 0.006, inner_h = 0.148, px = 1 / 362 m measured on FG12P):
#       numeral span 0.157 .. 0.351 from the left edge, center 0.254 -> u +0.074; the tiles span 0.200 (to the border's
#       inner edge minus 1 mm) x the inner height 0.148.
#   T2 / T3: tram_tower.plate_layout centers the numeral in avail = [b + pad + gs + pad, W - b - pad] (b 0.006, pad
#       0.008, gs = H - 2 (b + pad)): gondola 0.194 .. 0.426 (center u +0.090), tram 0.254 .. 0.546 (center u +0.120);
#       the tiles span avail + 4 mm each side x (H - 2 b).
# cap_max = the baked numeral's own cap rule (T1 inner_h - 6 px; T2 / T3 0.85 * gs) - the tiles never print larger.
CLASSES = {
    "T1": dict(plate=(0.36, 0.16), win_u=0.0740, win_w=0.200, win_h=0.148, cap_max=0.131,
               field=0.851, ink=0.051, metal=0.0, smooth=0.555, src="lift_tower.plate_raster"),
    "T2": dict(plate=(0.44, 0.20), win_u=0.0900, win_w=0.240, win_h=0.188, cap_max=0.146,
               field=0.851, ink=0.039, metal=0.349, smooth=0.550, src="tram_tower.plate_layout gondola"),
    "T3": dict(plate=(0.56, 0.26), win_u=0.1200, win_w=0.300, win_h=0.248, cap_max=0.197,
               field=0.851, ink=0.039, metal=0.349, smooth=0.555, src="tram_tower.plate_layout tram"),
}
# The towers each class numbers (prefab names; the code mod's TowerNumberPlates table holds their measured plate rows).
CLASS_TOWERS = {
    "T1": ["SkiLiftTower" + k for k in ("FG12", "FG16", "FG20", "DQ10", "DQ12", "DQ14", "DQ16", "DQ18", "DQ20",
                                       "GD12", "GD16", "GD20")],
    "T2": ["SkiGondolaLattice25", "SkiGondolaLattice32", "SkiGondolaLattice40"],
    "T3": ["SkiTramTower30", "SkiTramTower45", "SkiTramTower60"],
}
MEASURE_SRC = {   # shipped LOD0 mesh folder per class tower (assets/import/...): what --measure-plates reads
    **{t: f"towers/P/{t}P/{t}P.fbx" for t in CLASS_TOWERS["T1"] + CLASS_TOWERS["T2"]},
    **{t: f"tramtowers/B/{t}B/{t}B.fbx" for t in CLASS_TOWERS["T3"]},
}
# The 8 m FG member (prefab SkiLiftTowerFG2, mesh FG8Q) and the B2 head twins (DECISIONS 592), measured on the meshes their
# prefabs ship: towers/<letter>/<mesh>/<mesh>.fbx, the twin beside its member's letter (lift_tower.py TWIN_ATLAS).
SHIPPED_T1 = {
    "SkiLiftTowerFG2": "towers/Q/SkiLiftTowerFG8Q/SkiLiftTowerFG8Q.fbx",
    **{f"SkiLiftTower{k}": f"towers/{letter}/SkiLiftTower{k}/SkiLiftTower{k}.fbx" for letter, keys in (
        ("Q", ("FG8HoldA", "FG8ComboA", "FG8BreakA", "FG16BreakA", "FG20BreakA", "DQ10HoldA", "DQ10ComboA",
               "GD16HoldA", "GD16ComboA")),
        ("R", ("FG12HoldA", "FG12ComboA", "FG12BreakA", "DQ12HoldA", "DQ12ComboA", "GD12HoldA", "GD12ComboA")),
    ) for k in keys},
    # art-601: the taller twins; the DQ / GD prefabs ship the headed mesh letter T (lift_head), FG the twin's own A
    **{f"SkiLiftTower{k}A": f"towers/Q/SkiLiftTower{k}{m}/SkiLiftTower{k}{m}.fbx" for k, m in (
        ("FG16Hold", "A"), ("FG16Combo", "A"), ("DQ16Hold", "T"), ("DQ20Hold", "T"), ("GD20Hold", "T"))},
}
CLASS_TOWERS["T1"] += list(SHIPPED_T1)
MEASURE_SRC.update(SHIPPED_T1)
BAKED_NUMERAL = {"T1": "12", "T2": "25", "T3": "30"}   # what the preview's "before" plate shows

MAX_DIGITS = 3            # numbers 1..200 (DECISIONS 358); the code wraps 201 -> 1
TILE_DEPTH_M = 0.004      # Blank / 3-digit tiles: in front of the plate's FRONT face (dynamic-numbers.md §5)
LAYER_M = 0.003           # digits in front of the Blank tile (1-2 digit numbers)
CONDENSE = 0.82           # glyph x scale: verdict B (2026-10-20, DECISIONS 361: "B for tiles"); 1.0 = the baked font (A)
ATLAS = 512               # texture floor (PIPELINE / blender-build: 512^2)
COLS, ROWS = 6, 2         # cells 0-9 = digits, 10 = Blank (plain field), 11 = spare (plain field)
SS = 4                    # coverage supersampling


def tile_name(cls, glyph):
    return f"SkiNumTile{cls}{'Blank' if glyph is None else 'N' + str(glyph)}"


# -- font ------------------------------------------------------------------------------------------------------------
_FONT = {}


def _font_tris(body):
    """Bfont triangles of `body`, LEFT aligned, size 1 (em units, baseline y = 0)."""
    cu = bpy.data.curves.new("NumTileText", type="FONT")
    cu.body = body
    cu.size = 1.0
    cu.align_x = "LEFT"
    ob = bpy.data.objects.new("NumTileText", cu)
    bpy.context.scene.collection.objects.link(ob)
    try:
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        tris = [tuple((v.co.x, v.co.y) for v in f.verts) for f in bm.faces]
        bm.free()
        bpy.data.meshes.remove(me)
    finally:
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.curves.remove(cu)
    return tris


def font_metrics():
    """Tabular digit advance (em), the digits' common y band (em) and per-digit triangles (em, LEFT, baseline 0)."""
    if _FONT:
        return _FONT
    one = np.array([p for t in _font_tris("0") for p in t])
    two = np.array([p for t in _font_tris("00") for p in t])
    adv = float(two[:, 0].max() - one[:, 0].max())
    allp = np.array([p for t in _font_tris("0123456789") for p in t])
    _FONT.update(adv=adv, y0=float(allp[:, 1].min()), y1=float(allp[:, 1].max()),
                 tris={d: _font_tris(str(d)) for d in range(10)})
    widths = {d: float(np.ptp(np.array([p for t in _FONT["tris"][d] for p in t])[:, 0])) for d in range(10)}
    _FONT["max_ink"] = max(widths.values())
    return _FONT


def layout(cls, condense=CONDENSE):
    """Cell width (window / 3), digit cap (m) and scale for one class: the cap is the largest at which three tabular
    digits fit the window at the font's own advance (so 200 fits), never above the baked cap rule."""
    c = CLASSES[cls]
    f = font_metrics()
    cell_w = c["win_w"] / MAX_DIGITS
    digit_h_em = f["y1"] - f["y0"]
    cap = min(cell_w / (f["adv"] / digit_h_em * condense), c["cap_max"])
    return dict(cell_w=cell_w, cap=cap, em=cap / digit_h_em, gap_m=cell_w - f["max_ink"] * cap / digit_h_em * condense)


# -- rasters (rows bottom-up, linear coverage) -------------------------------------------------------------------------
def _cov(tris, w, h, x0_m, y0_m, ppm):
    """Coverage (h, w) of triangles given in meters; pixel (0, 0) bottom-left sits at (x0_m, y0_m)."""
    Ws, Hs = w * SS, h * SS
    xs = x0_m + (np.arange(Ws) + 0.5) / (SS * ppm)
    ys = y0_m + (np.arange(Hs) + 0.5) / (SS * ppm)
    cov = np.zeros((Hs, Ws), np.float32)
    for a, b, cc in tris:
        lo = np.minimum(np.minimum(a, b), cc)
        hi = np.maximum(np.maximum(a, b), cc)
        j0, j1 = np.searchsorted(xs, lo[0]), np.searchsorted(xs, hi[0], side="right")
        i0, i1 = np.searchsorted(ys, lo[1]), np.searchsorted(ys, hi[1], side="right")
        if j1 <= j0 or i1 <= i0:
            continue
        X, Y = np.meshgrid(xs[j0:j1], ys[i0:i1])
        d = (b[1] - cc[1]) * (a[0] - cc[0]) + (cc[0] - b[0]) * (a[1] - cc[1])
        if abs(d) < 1e-14:
            continue
        l1 = ((b[1] - cc[1]) * (X - cc[0]) + (cc[0] - b[0]) * (Y - cc[1])) / d
        l2 = ((cc[1] - a[1]) * (X - cc[0]) + (a[0] - cc[0]) * (Y - cc[1])) / d
        cov[i0:i1, j0:j1][(l1 >= 0) & (l2 >= 0) & (l1 + l2 <= 1)] = 1.0
    return cov.reshape(h, SS, w, SS).mean(axis=(1, 3))


def digit_tris_m(cls, d, condense=CONDENSE):
    """Digit d's triangles in meters, centered in its cell: x by the tabular advance box, y by the digits' common band."""
    f = font_metrics()
    L = layout(cls, condense)
    s = L["em"]
    cx, cy = f["adv"] / 2.0, (f["y0"] + f["y1"]) / 2.0
    return [tuple(np.array(((p[0] - cx) * s * condense, (p[1] - cy) * s)) for p in t) for t in f["tris"][d]]


def digit_ink(cls, d, w, h, condense=CONDENSE):
    """Ink coverage (h, w) of a digit cell rendered at w x h px (cell_w x win_h meters)."""
    L = layout(cls, condense)
    ppm = w / L["cell_w"]
    return _cov(digit_tris_m(cls, d, condense), w, h, -L["cell_w"] / 2.0, -CLASSES[cls]["win_h"] / 2.0, ppm)


def cell_px(cls):
    L = layout(cls)
    w = ATLAS // COLS
    h = int(round(w * CLASSES[cls]["win_h"] / L["cell_w"]))
    if ROWS * h > ATLAS:
        raise ValueError(f"{cls}: {ROWS} rows of {h} px do not fit the {ATLAS} atlas")
    return w, h


def cell_rect(cls, k):
    """(c0, r0, w, h) px of cell k, rows bottom-up (row 0 = the TOP band of the atlas, as a reader sees it)."""
    w, h = cell_px(cls)
    col, row = k % COLS, k // COLS
    return col * w, ATLAS - (row + 1) * h, w, h


def atlas_maps(cls):
    """BaseColor (sRGB-encoded floats), MaskMap, Normal, ControlMask (ATLAS^2 x 4, rows bottom-up)."""
    c = CLASSES[cls]
    ink = np.zeros((ATLAS, ATLAS), np.float32)
    for d in range(10):
        c0, r0, w, h = cell_rect(cls, d)
        ink[r0:r0 + h, c0:c0 + w] = digit_ink(cls, d, w, h)
    lin = lambda v: v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    fl, il = lin(c["field"]), lin(c["ink"])
    col_lin = fl * (1.0 - ink) + il * ink                                  # blend in linear light, as the painters do
    srgb = np.where(col_lin <= 0.0031308, col_lin * 12.92, 1.055 * np.power(col_lin, 1 / 2.4) - 0.055)
    base = np.ones((ATLAS, ATLAS, 4), np.float32)
    base[..., 0:3] = srgb[..., None]
    msk = np.zeros((ATLAS, ATLAS, 4), np.float32)
    msk[..., 0], msk[..., 3] = c["metal"], c["smooth"]
    nrm = np.zeros((ATLAS, ATLAS, 4), np.float32)
    nrm[..., 0], nrm[..., 1], nrm[..., 2], nrm[..., 3] = 0.5, 0.5, 1.0, 1.0
    ctl = np.zeros((ATLAS, ATLAS, 4), np.float32)
    return {"BaseColor": base, "MaskMap": msk, "Normal": nrm, "ControlMask": ctl}, ink


# -- meshes ----------------------------------------------------------------------------------------------------------
def tile_size(cls, glyph):
    c, L = CLASSES[cls], layout(cls)
    return (c["win_w"] if glyph is None else L["cell_w"]), c["win_h"]


def build_tile(cls, glyph, name=None):
    """One quad, Blender XZ plane facing -Y (see FRAME), origin at its center, UVs on its atlas cell."""
    w, h = tile_size(cls, glyph)
    c0, r0, cw, chh = cell_rect(cls, 10 if glyph is None else glyph)
    inset = 4.0 if glyph is None else 0.0           # the Blank samples the plain field well inside its cell
    u0, u1 = (c0 + inset) / ATLAS, (c0 + cw - inset) / ATLAS
    v0, v1 = (r0 + inset) / ATLAS, (r0 + chh - inset) / ATLAS
    name = name or tile_name(cls, glyph)        # the FBX mesh name = the asset name (textures bind by it)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    vs = [bm.verts.new((x, 0.0, z)) for x, z in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2))]
    face = bm.faces.new(vs)
    face.normal_update()
    if face.normal.y > 0:                         # must face -Y
        face.normal_flip()
    for lp in face.loops:
        x, z = lp.vert.co.x, lp.vert.co.z
        lp[uv].uv = (u0 if x < 0 else u1, v0 if z < 0 else v1)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    n = me.polygons[0].normal
    if n.y > -0.999:
        raise RuntimeError(f"{name}: front normal {tuple(n)} is not -Y")
    return ob


# -- previews --------------------------------------------------------------------------------------------------------
def baked_plate(cls, ppm, numeral):
    """The shipped plate's raster (sRGB floats, rows bottom-up) at `ppm`, painted by its own generator."""
    pw, ph = CLASSES[cls]["plate"]
    w, h = int(round(pw * ppm)), int(round(ph * ppm))
    if cls == "T1":
        import lift_tower as LT
        spec = SimpleNamespace(family="FG", role="line", key="FG" + numeral)
        rgb, _inf = LT.plate_raster(spec, h, w)
        lin = rgb[..., 0]
    else:
        import tram_tower as TT
        field, ink, _rep = TT.plate_layout(w, h, ppm, numeral)
        white, black = float(TT._srgb_to_lin(np.float64(0.85))), float(TT._srgb_to_lin(np.float64(0.04)))
        col = black + (white - black) * field
        lin = col * (1.0 - ink) + black * ink
    return np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(np.clip(lin, 0, 1), 1 / 2.4) - 0.055)


def number_tiles(n):
    """The tiles of number n (1..200 after the wrap) on one face: [(glyph or None, u offset from the window center)].
    Mirrors TowerNumberTiles.Layout in the code mod (LiftBatch tests pin the C# side)."""
    s = str(n)
    out = [] if len(s) == MAX_DIGITS else [(None, 0.0)]
    k = len(s)
    for i, ch in enumerate(s):
        out.append((int(ch), (i - (k - 1) / 2.0)))     # in cell widths
    return out


def compose(cls, n, ppm, condense=CONDENSE, tile_ppm=None):
    """The plate with number n's tiles over it (sRGB, rows bottom-up), viewed square-on. tile_ppm = the texel density
    the tiles are sampled at (None = ppm: the design view; the atlas density gives the in-game texel view)."""
    c = CLASSES[cls]
    L = layout(cls, condense)
    img = baked_plate(cls, ppm, BAKED_NUMERAL[cls]).copy()
    H, W = img.shape
    pw, ph = c["plate"]
    lin = lambda v: v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    fl, il = lin(c["field"]), lin(c["ink"])
    to_s = lambda x: np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)
    for glyph, off in number_tiles(n):
        tw = c["win_w"] if glyph is None else L["cell_w"]
        th = c["win_h"]
        cu = pw / 2.0 + c["win_u"] + off * L["cell_w"]          # tile center from the plate's left edge
        x0, x1 = int(round((cu - tw / 2) * ppm)), int(round((cu + tw / 2) * ppm))
        y0, y1 = int(round((ph - th) / 2 * ppm)), int(round((ph + th) / 2 * ppm))
        if glyph is None:
            ink = np.zeros((y1 - y0, x1 - x0), np.float32)
        else:
            tp = tile_ppm or ppm
            tw_px, th_px = max(1, int(round(tw * tp))), max(1, int(round(th * tp)))
            small = digit_ink(cls, glyph, tw_px, th_px, condense)
            ri = (np.arange(y1 - y0) * th_px // max(1, y1 - y0)).clip(0, th_px - 1)
            ci = (np.arange(x1 - x0) * tw_px // max(1, x1 - x0)).clip(0, tw_px - 1)
            ink = small[ri][:, ci]
        img[y0:y1, x0:x1] = to_s(fl * (1 - ink) + il * ink)
    return img


def _save_rgb(path, img_rows_bottom_up):
    h, w = img_rows_bottom_up.shape[:2]
    rgba = np.ones((h, w, 4), np.float32)
    rgba[..., 0:3] = img_rows_bottom_up[..., None] if img_rows_bottom_up.ndim == 2 else img_rows_bottom_up
    im = bpy.data.images.new(os.path.basename(path), width=w, height=h, alpha=True, is_data=True)
    im.colorspace_settings.name = "Non-Color"
    im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)


def _label(img, text, x, y, h_px, val=0.0):
    """Burn a short label (Bfont) into an sRGB image at (x, y) bottom-left px."""
    tris = _font_tris(text)
    if not tris:
        return
    pts = np.array([p for t in tris for p in t])
    s = h_px / (pts[:, 1].max() - pts[:, 1].min())
    w = int(np.ptp(pts[:, 0]) * s) + 4
    cov = _cov([tuple(np.array(((p[0] - pts[:, 0].min()) * s, (p[1] - pts[:, 1].min()) * s)) for p in t) for t in tris],
               w, int(h_px) + 2, 0.0, 0.0, 1.0)
    hh, ww = min(cov.shape[0], img.shape[0] - y), min(cov.shape[1], img.shape[1] - x)
    img[y:y + hh, x:x + ww] = img[y:y + hh, x:x + ww] * (1 - cov[:hh, :ww]) + val * cov[:hh, :ww]


def preview(out_dir):
    """PREVIEW GATE sheets, one per class: rows = the baked plate (today) / tiles A (font unchanged) / tiles B
    (condensed 0.82), columns = 7, 42, 128, 200; each panel at 2,000 px/m (design) with the in-game texel view
    beside it (baked plate at its shipped ~340 px/m, tiles at the atlas density, both upscaled nearest)."""
    os.makedirs(out_dir, exist_ok=True)
    nums = (7, 42, 128, 200)
    report = {}
    for cls in CLASSES:
        c = CLASSES[cls]
        pw, ph = c["plate"]
        P = 2000.0 if cls == "T1" else (1600.0 if cls == "T2" else 1300.0)
        game_ppm = {"T1": 362.0, "T2": 340.0, "T3": 327.0}[cls]       # measured on FG12P / Lattice25P / Tram30B
        Wp, Hp = int(round(pw * P)), int(round(ph * P))
        gap, lab = 24, 40
        sheet = np.full((lab + 3 * (Hp + gap + lab), lab + 2 * len(nums) * (Wp + gap)), 0.30, np.float32)
        variants = [("today (baked)", None), ("A font as baked", 1.0), ("B condensed 0.82", 0.82)]
        for vi, (vname, cond) in enumerate(variants):
            ytop = sheet.shape[0] - lab - vi * (Hp + gap + lab)
            _label(sheet, f"{cls} {vname}", 10, ytop - lab + 4, 26, 0.95)
            for ni, n in enumerate(nums):
                for view in (0, 1):
                    if cond is None:
                        if view == 0:
                            img = baked_plate(cls, P, BAKED_NUMERAL[cls])
                        else:
                            small = baked_plate(cls, game_ppm, BAKED_NUMERAL[cls])
                            ri = (np.arange(Hp) * small.shape[0] // Hp)
                            ci = (np.arange(Wp) * small.shape[1] // Wp)
                            img = small[ri][:, ci]
                    else:
                        if view == 0:
                            img = compose(cls, n, P, cond)
                        else:
                            tile_ppm = cell_px(cls)[0] / layout(cls, cond)["cell_w"]
                            small = compose(cls, n, game_ppm, cond, tile_ppm=tile_ppm)
                            ri = (np.arange(Hp) * small.shape[0] // Hp)
                            ci = (np.arange(Wp) * small.shape[1] // Wp)
                            img = small[ri][:, ci]
                    x0 = lab + (2 * ni + view) * (Wp + gap)
                    y0 = ytop - lab - Hp
                    sheet[y0:y0 + Hp, x0:x0 + Wp] = img
                caption = (f"{n}: design | in-game texels" if cond is not None
                           else f"baked {BAKED_NUMERAL[cls]}: design | in-game texels")
                _label(sheet, caption, lab + 2 * ni * (Wp + gap) + (300 if ni == 0 else 0), ytop - lab + 4, 22, 0.95)
        path = os.path.join(out_dir, f"numtiles_{cls}_sheet.png")
        _save_rgb(path, sheet)
        LA, LB = layout(cls, 1.0), layout(cls, 0.82)
        report[cls] = {"sheet": path, "cell_w_m": round(LA["cell_w"], 4), "cap_A_m": round(LA["cap"], 4),
                       "cap_B_m": round(LB["cap"], 4), "cap_baked_rule_m": c["cap_max"],
                       "atlas_cell_px": list(cell_px(cls)),
                       "atlas_ppm": round(cell_px(cls)[0] / LA["cell_w"], 1), "towers": CLASS_TOWERS[cls]}
        maps, _ink = atlas_maps(cls)
        _save_rgb(os.path.join(out_dir, f"numtiles_{cls}_atlas_BaseColor.png"), maps["BaseColor"][..., 0])
        print(f"[number_tiles] {cls}: {json.dumps(report[cls])}")
    f = font_metrics()
    report["font"] = {"advance_em": round(f["adv"], 4), "digit_band_em": [round(f["y0"], 4), round(f["y1"], 4)],
                      "max_ink_em": round(f["max_ink"], 4)}
    with open(os.path.join(out_dir, "numtiles_preview.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=1)
    return report


# -- measurement (the code mod's plate table) ------------------------------------------------------------------------
def measure_plates(import_root, out_json, only=None):
    """Find both plate FRONT quads of every class tower in its shipped LOD0 FBX: center, normal and viewer-right in the
    PREFAB frame (game = (-fx, fy, fz) / 100: FBX centimeters, the game negates FBX x). `only`: a set of prefab names to
    measure (the rest are skipped, e.g. when their FBX is not exported in this checkout)."""
    from mathutils import Vector
    res = {}
    if only:
        unknown = sorted(set(only) - set(MEASURE_SRC))
        if unknown:
            raise SystemExit(f"--towers: unknown tower(s) {unknown}")
    for cls, towers in CLASS_TOWERS.items():
        pw, ph = CLASSES[cls]["plate"]
        for t in towers:
            if only and t not in only:
                continue
            bpy.ops.wm.read_factory_settings(use_empty=True)
            bpy.ops.import_scene.fbx(filepath=os.path.join(import_root, MEASURE_SRC[t]), axis_forward="Y", axis_up="Z",
                                     global_scale=1.0)
            ob = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
            me = ob.data
            uvl = me.uv_layers.active.data
            tgt = pw * ph * 1e4 / 2.0
            tris = []
            for p in me.polygons:
                if abs(p.normal.y) > 0.05 or abs(p.area - tgt) / tgt > 0.03:
                    continue
                tris.append(([ob.matrix_world @ me.vertices[i].co for i in p.vertices], p.normal.copy(),
                             [Vector(uvl[li].uv) for li in p.loop_indices]))
            plates, used = [], set()
            for i, A in enumerate(tris):
                for j, B in enumerate(tris):
                    if j <= i or i in used or j in used or (A[1] - B[1]).length > 0.01:
                        continue
                    pts = A[0] + B[0]
                    uniq = []
                    for q in pts:
                        if all((q - u).length > 0.01 for u in uniq):
                            uniq.append(q)
                    if len(uniq) != 4 or abs(max(q.y for q in uniq) - min(q.y for q in uniq) - ph * 100) > 0.5:
                        continue
                    used |= {i, j}
                    cq = sum(uniq, Vector()) / 4.0
                    (p0, p1, p2), (t0, t1, t2) = A[0], A[2]
                    e1, e2, d1, d2 = p1 - p0, p2 - p0, t1 - t0, t2 - t0
                    T = ((e1 * d2.y - e2 * d1.y) / (d1.x * d2.y - d2.x * d1.y)).normalized()
                    g = lambda v, k=1.0: [round(-v.x * k, 4) + 0.0, round(v.y * k, 4) + 0.0, round(v.z * k, 4) + 0.0]
                    plates.append({"centre": g(cq, 0.01), "normal": g(A[1]), "right": g(T)})
            if len(plates) != 2:
                raise RuntimeError(f"{t}: found {len(plates)} plate front quads, expected 2")
            res[t] = {"class": cls, "plates": sorted(plates, key=lambda p: -p["normal"][2])}
            print(f"[number_tiles] {t}: {json.dumps(res[t])}")
    with open(out_json, "w", encoding="utf-8") as fh:
        json.dump(res, fh, indent=1)
    return res


# -- export (after the verdict) --------------------------------------------------------------------------------------
def manifest_path(out_dir):
    """numtiles/<LETTER>.manifest.json: BESIDE the letter folder, never inside it (the Editor's Assets folder holds asset
    folders only, like every other letter folder)."""
    d = os.path.normpath(out_dir)
    return os.path.join(os.path.dirname(d), os.path.basename(d) + ".manifest.json")


def export(out_dir):
    """One import folder: per class the Blank tile owns the atlas (4 PNG), the ten digits share it (settings.json
    sharedAssets, DECISIONS 298: import the whole folder in ONE action). LOD0 only (2 tris). Mesh asset names carry the
    generation letter (= the folder name, e.g. SkiNumTileT1N7A); the hand-made prefabs (surgery) drop it (SkiNumTileT1N7)."""
    os.makedirs(out_dir, exist_ok=True)
    letter = os.path.basename(os.path.normpath(out_dir))
    if not (len(letter) == 1 and letter.isupper()):
        raise SystemExit(f"--out must end in a generation letter folder, got {letter!r}")
    rows = []
    for cls in CLASSES:
        maps, _ink = atlas_maps(cls)
        owner = tile_name(cls, None) + letter
        for glyph in [None] + list(range(10)):
            name = tile_name(cls, glyph) + letter
            cs2_asset.validate_asset_name(name)
            d = os.path.join(out_dir, name)
            os.makedirs(d, exist_ok=True)
            ob = build_tile(cls, glyph, name)
            cs2_asset.export_fbx(ob, os.path.join(d, name + ".fbx"))
            if glyph is None:
                for m, arr in maps.items():
                    rgba = arr if arr.ndim == 3 else np.repeat(arr[..., None], 4, axis=2)
                    # rows here are bottom-up (UV v = 0 at row 0); _write_png flips a TOP-DOWN array, so hand it one
                    cs2_asset._write_png(os.path.join(d, f"{name}_{m}.png"), f"{name}_{m}", np.flipud(rgba), ATLAS, is_data=True)
                cs2_asset.write_settings(d, name)
            else:
                cs2_asset.write_settings(d, name, shared_from=f"../{owner}/{owner}")
            w, h = tile_size(cls, glyph)
            rows.append({"name": name, "prefab": tile_name(cls, glyph), "class": cls, "glyph": glyph,
                         "w": round(w, 4), "h": round(h, 4), "shares": None if glyph is None else owner})
    with open(manifest_path(out_dir), "w", encoding="utf-8") as fh:
        json.dump({"tiles": rows, "classes": {k: {**v, **{kk: round(vv, 4) for kk, vv in layout(k).items()}}
                                               for k, v in CLASSES.items()},
                   "condense": CONDENSE, "tile_depth_m": TILE_DEPTH_M, "layer_m": LAYER_M}, fh, indent=1)
    return rows


def verify_export(out_dir):
    """Round trip of the written folder (raises on a miss): every FBX re-imported faces +Z in the PREFAB frame with its text
    (UV u) running along -X and the measured tile size; every owner's BaseColor reads the field byte 217 (sRGB 0.851) at a
    Blank texel and ink in a digit cell. The same frame rule the plate measurement used: game = (-fx, fy, fz) / 100."""
    from mathutils import Vector
    with open(manifest_path(out_dir), encoding="utf-8") as fh:
        man = json.load(fh)
    out = []
    for row in man["tiles"]:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=os.path.join(out_dir, row["name"], row["name"] + ".fbx"), axis_forward="Y",
                                 axis_up="Z", global_scale=1.0)
        ob = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
        me = ob.data
        if len(me.polygons) != 2 and len(me.polygons) != 1:
            raise RuntimeError(f"{row['name']}: {len(me.polygons)} faces")
        p = me.polygons[0]
        uvl = me.uv_layers.active.data
        vs = [ob.matrix_world @ me.vertices[i].co for i in p.vertices]
        t = [Vector(uvl[li].uv) for li in p.loop_indices]
        n = (ob.matrix_world.to_3x3() @ p.normal).normalized()
        e1, e2, d1, d2 = vs[1] - vs[0], vs[2] - vs[0], t[1] - t[0], t[2] - t[0]
        T = ((e1 * d2.y - e2 * d1.y) / (d1.x * d2.y - d2.x * d1.y)).normalized()
        g = lambda v: Vector((-v.x, v.y, v.z))
        allv = [ob.matrix_world @ v.co for v in me.vertices]
        w = (max(v.x for v in allv) - min(v.x for v in allv)) / 100.0
        h = (max(v.y for v in allv) - min(v.y for v in allv)) / 100.0
        ok = g(n).z > 0.999 and g(T).x < -0.999 and abs(w - row["w"]) < 1e-3 and abs(h - row["h"]) < 1e-3
        if not ok:
            raise RuntimeError(f"{row['name']}: normal {tuple(g(n))} text axis {tuple(g(T))} size {w:.4f} x {h:.4f} "
                               f"(want +Z, -X, {row['w']} x {row['h']})")
        out.append(row["name"])
    for cls in CLASSES:
        owner = next(r["name"] for r in man["tiles"] if r["class"] == cls and r["glyph"] is None)
        im = bpy.data.images.load(os.path.join(out_dir, owner, owner + "_BaseColor.png"))
        a = np.array(im.pixels[:], np.float32).reshape(ATLAS, ATLAS, 4)      # Blender: rows bottom-up
        bpy.data.images.remove(im)
        c0, r0, cw, chh = cell_rect(cls, 10)
        field = a[r0 + chh // 2, c0 + cw // 2, 0]
        c0, r0, cw, chh = cell_rect(cls, 8)
        ink = a[r0:r0 + chh, c0:c0 + cw, 0].min()
        if abs(field * 255 - 217) > 1.0 or ink > 0.10:
            raise RuntimeError(f"{owner}_BaseColor: Blank texel {field * 255:.1f} (want 217), digit-8 cell min {ink:.3f}")
        out.append(f"{owner}_BaseColor field {field * 255:.0f} ink {ink * 255:.0f}")
    print(f"[number_tiles] VERIFY PASS: {len(man['tiles'])} FBX (+Z, text along -X, sizes) and {len(CLASSES)} atlases")
    return out


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="number_tiles.py")
    ap.add_argument("--preview", metavar="DIR")
    ap.add_argument("--measure-plates", metavar="OUT_JSON")
    ap.add_argument("--towers", metavar="NAMES", help="with --measure-plates: comma-separated prefab names to measure "
                                                     "(default all; 'shipped-t1' = the FG8 member and the B2 twins)")
    ap.add_argument("--import-root", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "import"))
    ap.add_argument("--export", action="store_true")
    ap.add_argument("--out", metavar="DIR")
    a = ap.parse_args(argv)
    if a.measure_plates:
        only = None
        if a.towers:
            only = set(SHIPPED_T1) if a.towers == "shipped-t1" else {t.strip() for t in a.towers.split(",") if t.strip()}
        measure_plates(a.import_root, a.measure_plates, only)
    if a.preview:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        preview(a.preview)
    if a.export:
        if not a.out:
            raise SystemExit("--export needs --out <letter folder>")
        bpy.ops.wm.read_factory_settings(use_empty=True)
        export(a.out)
        verify_export(a.out)
    if not (a.measure_plates or a.preview or a.export):
        ap.print_help()


if __name__ == "__main__":
    main()
