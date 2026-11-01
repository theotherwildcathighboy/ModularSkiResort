"""
ski_bus_panel.py - the "SKI BUS" sign panel for the ski coach (research/ski-bus-visual-2026-10-31.md, Stage B).

SUPERSEDED as a board by ski_bus_decal.py, which paints paint_strip's art on the coach. paint_strip stays the art's one source.

MODELED STAND-IN: no vanilla prop carries this text, so this one small board is modeled (vanilla props first).

One mesh, SkiBusPanelA: a thin board, origin at its center, attached several times to the coach clone as sub-object
rows (left side, right side, roof), so fit is adjusted in the prefab without a reimport. Export: --out assets/import/vehicles/A.
A new look takes a new generation letter in NAME (PIPELINE section 0) and a fresh preview verdict first.

Frame (authoring meters, Z up, as welcome_sign.py): the front faces +X, the long axis is Y, and +Y is the viewer's
right. cs2_asset.orient_for_net + export_fbx + the game's x negation (PIPELINE section 4, frame rule) put the front on
game +Z, the letters' top on game +Y and the viewer's right on game -X: a handedness change, not a mirror, so the text
reads left to right from the front in game as in Blender (the same chain as cs2_signset.build_sign_set; in-game
confirmation is still open). Rows that turn the board use a rotation, never a negative scale: a mirror row would
mirror the text.

Texture: 1024^2, painted here (nothing baked, no AO, no weathering), the texture floor is 512 (PIPELINE section 1).
The 6 m face would get only 165 px/m as one strip on a square map, so it is two quads cut in the word gap, which is
plain field, and each half has its own row at PPM (the welcome sign's header split, cs2_signset "gate.header"). The
back and the edges share a third, plain row. The whole sheet is field color, so no island can bleed at any mip.
Maps: BaseColor (alpha 255), Normal flat (printed board, nothing under 5 cm to relieve), MaskMap (0, 0, 0, SMOOTH),
ControlMask 0 (never tinted: the panel keeps its paint on any coach color, like every sign set). No Emissive: the
board is unlit at night. One LOD by design, research Stage B (20 tris; PIPELINE LOD rules bind chains, and the cull
comes from the sign-set LodProperties pattern at surgery).

Type: Franklin Gothic Demi Cond, PIPELINE section 1 "Signage typefaces", rendered from the sha1-pinned installed font
into the bitmap through cs2_signset (the TTF is never copied). Mark: the two-peak silhouette of
ski_launcher_glyph.cand_c, the shipped launcher glyph that the welcome sign emblem also uses, without its snowflake
(the flake is 17 cm across here and closes up well before the street-zoom view).

CLI (headless only):
    blender --background --factory-startup --python-exit-code 1 --python ski_bus_panel.py --
        --out DIR [--preview [DIR]] [--face franklin_demi_cond] [--no-mark] [--texture-only] [--no-validate]
    --out DIR receives DIR/SkiBusPanel/ (FBX, four PNGs, settings.json, manifest), a scratch folder for the validator.
    --preview writes SkiBusPanel_<view>.png into assets/preview/ (or DIR). Exit 1 on any failure; last line PASS.
"""

import argparse
import math
import os
import sys
import time
import traceback

import bpy
import numpy as np
from mathutils import Matrix, Vector

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset             # noqa: E402
import cs2_signset as SS     # noqa: E402
import cs2_validate          # noqa: E402
import ski_launcher_glyph as G   # noqa: E402

REPO = os.path.normpath(os.path.join(_HERE, os.pardir, os.pardir))
PREVIEW_DIR = os.path.join(REPO, "assets", "preview")
NAME = "SkiBusPanelA"

# ---- board (meters) ---------------------------------------------------------------------------------------------
BOARD_W, BOARD_H, BOARD_T = 6.00, 0.60, 0.03     # along Y, along Z, along X
LOD0_CAP = 20                                    # 10 quads: two halves, front, back, top, bottom, plus two ends

# ---- paint ------------------------------------------------------------------------------------------------------
OXBLOOD_BYTES = (0x70, 0x26, 0x24)               # the coach's paint
CREAM_BYTES = (0xF2, 0xE8, 0xD5)
FACE = "franklin_demi_cond"                      # SS.NC_UTILITY_FACE, PIPELINE "Signage typefaces"
CAP_M = 0.44                                     # large letters beat decoration; margin (0.60 - 0.44) / 2 = 0.08 m
TRACKING = 0.06                                  # x the cap, as the sign sheet's name boards
WORD_GAP = 0.80                                  # x the cap, between SKI and BUS (the seam lives here)
MARK_H_M = 0.32                                  # small: the peaks stand on the letters' baseline, under cap height
MARK_GAP_M = 0.34                                # text ink to the mark
STRIPE_H_M = 0.045                               # two cream livery stripes from the mark out to the board margin
STRIPE_SPACE_M = 0.055                           # field between the two stripes
STRIPE_GAP_M = 0.20                              # mark to the stripes
MARGIN_M = (BOARD_H - CAP_M) / 2.0               # the even margin (top, bottom, stripe ends)
SMOOTH = SS.SMOOTH                               # the sign sheet's printed-board smoothness

# ---- sheet (texels, rows bottom-up) -----------------------------------------------------------------------------
TEX = 1024
PPM = 320.0                                      # px/m on every face; sign sheet 300, lift boards 400, vanilla 340-520
MARGIN_PX = TEX // 64                            # cs2_uv.default_margin_px / the props' mip-safe margin
MIN_STROKE_PX = 4.0                              # the sign sheet's SS3 floor
ROW_H = int(round(BOARD_H * PPM))
ROW_LEFT = (MARGIN_PX, MARGIN_PX)                # (x0, y0) of the row with the viewer's left half
ROW_RIGHT = (MARGIN_PX, MARGIN_PX * 2 + ROW_H)
ROW_PLAIN = (MARGIN_PX, MARGIN_PX * 3 + ROW_H * 2)
MAX_ROW_PX = TEX - 2 * MARGIN_PX
SEAM_CLEAR_PX = 8                                # plain field required each side of the seam

# ---- preview proxy (authoring frame: bus length along X, width along Y; NOT the real Bus01) -----------------------
PROXY_W, PROXY_H, PROXY_L = 2.55, 3.20, 12.00
PROXY_GAP = 0.01                                 # air between the board's back and the proxy
FOV_DEG = 45.0                                   # cs2_preview.VERTICAL_FOV_DEG; the game camera's own FOV is unverified
INSET_CROP = (240, 160)                          # center of the frame, drawn again at INSET_K x, nearest neighbor
INSET_K = 3                                      # 720 x 480 in the corner: clear of the proxy at 70 m and farther
VIEW_DIR = (-0.60, -0.75, 0.62)                  # 3/4 high, the -Y side and the front of the proxy
# (view, distance m, label, inset): the distances are this preview's choice, not measured game zoom levels
CONTEXT_SHOTS = (("context_street", 25.0, "street zoom", False), ("context_mid", 70.0, "mid zoom", True),
                 ("context_city", 140.0, "city zoom", True))


def parse_args(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="ski_bus_panel.py")
    ap.add_argument("--out", required=True, help="scratch folder; DIR/SkiBusPanel/ receives the asset folder")
    ap.add_argument("--preview", nargs="?", const="", default=None, metavar="DIR")
    ap.add_argument("--face", default=FACE, choices=sorted(SS.TYPEFACES))
    ap.add_argument("--no-mark", action="store_true", help="letters and stripe only")
    ap.add_argument("--texture-only", action="store_true", help="stop after the front preview (a look at the paint)")
    ap.add_argument("--no-validate", action="store_true")
    return ap.parse_args(argv)


def bytes_to_lin(rgb):
    return SS._srgb_to_lin(np.array(rgb, np.float32) / 255.0)


# ======================================================================================
# Paint
# ======================================================================================

def peaks_cov():
    """(square coverage, bottom-up rows; mark width m): the launcher glyph's two peaks, MARK_H_M tall."""
    fill = G.cand_c()[0]
    if fill[0] != "fill":
        raise RuntimeError("ski_launcher_glyph.cand_c no longer starts with its peaks")
    pts = np.array(fill[1], np.float64)
    lo, hi = pts.min(0), pts.max(0)
    k = (G.CANVAS - 8.0) / float((hi - lo).max())           # to the glyph canvas, 4 units clear of its edges
    c = (lo + hi) / 2.0
    prims = [("fill", [tuple(v) for v in (pts - c) * k + G.CANVAS / 2.0])]
    m_per_unit = MARK_H_M / (float(hi[1] - lo[1]) * k)
    s = int(round(G.CANVAS * m_per_unit * PPM))
    return G.render_alpha(prims, s, ss=8)[::-1], float(hi[0] - lo[0]) * k * m_per_unit   # top-down -> bottom-up


def paint_strip(face, mark=True):
    """The whole face as one coverage strip (rows bottom-up): -> (cov (ROW_H, W), layout dict). x runs along the
    viewer's left to right, which is authoring y from -BOARD_W / 2. seam_px is the column in the word gap."""
    tf = SS.typeface(face)
    track = SS.track_for(tf, TRACKING)
    W, H = BOARD_W, BOARD_H
    w, h = int(round(W * PPM)), ROW_H
    ski, ski_w, stem, thin = SS.ttf_line("SKI", CAP_M, tf, track)
    bus, bus_w, _s, _t = SS.ttf_line("BUS", CAP_M, tf, track)
    gap = WORD_GAP * CAP_M
    total = ski_w + gap + bus_w
    x_ski = W / 2.0 - total / 2.0 + ski_w / 2.0
    x_bus = W / 2.0 + total / 2.0 - bus_w / 2.0
    cov = SS.ttf_cov(ski, x_ski, H / 2.0, W, H, w, h)
    cov = np.maximum(cov, SS.ttf_cov(bus, x_bus, H / 2.0, W, H, w, h))
    seam_m = x_ski + ski_w / 2.0 + gap / 2.0
    seam_px = int(np.clip(round(seam_m * PPM), w - MAX_ROW_PX, MAX_ROW_PX))     # check_strip proves it stays in the gap

    def rect(x0, x1, y0, y1):
        c0, c1 = int(round(x0 * PPM)), int(round(x1 * PPM))
        r0, r1 = int(round(y0 * PPM)), int(round(y1 * PPM))
        cov[r0:r1, c0:c1] = 1.0

    mark_rec = None
    x_out = (W / 2.0 - total / 2.0 - 0.02, W / 2.0 + total / 2.0 + 0.02)     # where the stripes stop, inner end
    if mark:
        glyph, mark_w = peaks_cov()
        s = glyph.shape[0]
        ink_rows = np.where(glyph.max(axis=1) > 0.0)[0]
        glyph = glyph[ink_rows[0]:ink_rows[-1] + 1]          # the ink only; its bottom row goes on the baseline
        r0 = int(round(MARGIN_M * PPM))
        for sx in (-1.0, 1.0):
            cx = W / 2.0 + sx * (total / 2.0 + MARK_GAP_M + mark_w / 2.0)
            c0 = int(round(cx * PPM - s / 2.0))
            r1, c1 = r0 + glyph.shape[0], c0 + s
            cov[r0:r1, c0:c1] = np.maximum(cov[r0:r1, c0:c1], glyph)
        x_out = (W / 2.0 - total / 2.0 - MARK_GAP_M - mark_w, W / 2.0 + total / 2.0 + MARK_GAP_M + mark_w)
        mark_rec = {"glyph": "two peaks of ski_launcher_glyph.cand_c", "width_m": round(mark_w, 3), "height_m": MARK_H_M}
    stripes = None
    if STRIPE_H_M > 0.0:
        for sy in (-1.0, 1.0):
            yc = H / 2.0 + sy * (STRIPE_SPACE_M + STRIPE_H_M) / 2.0
            y0, y1 = yc - STRIPE_H_M / 2.0, yc + STRIPE_H_M / 2.0
            rect(MARGIN_M, x_out[0] - STRIPE_GAP_M, y0, y1)
            rect(x_out[1] + STRIPE_GAP_M, W - MARGIN_M, y0, y1)
        stripes = {"height_m": STRIPE_H_M, "space_m": STRIPE_SPACE_M,
                   "length_each_m": round(x_out[0] - STRIPE_GAP_M - MARGIN_M, 3)}
    layout = {"font": tf.name, "font_file": tf.file, "font_sha1": tf.sha1, "cap_m": CAP_M, "margin_m": round(MARGIN_M, 4),
              "tracking_cap": TRACKING, "ink_width_m": round(total, 4), "stem_px": round(stem * PPM, 2),
              "thin_px": round(thin * PPM, 2), "seam_y_m": round(seam_px / PPM - W / 2.0, 4), "seam_px": seam_px,
              "mark": mark_rec, "stripes": stripes}
    return cov, layout


def check_strip(cov, layout):
    """The paint contracts: strokes over the floor, the seam on plain field, both rows fit, an even margin."""
    if min(layout["stem_px"], layout["thin_px"]) < MIN_STROKE_PX:
        raise RuntimeError(f"stroke {layout['stem_px']} / {layout['thin_px']} px under {MIN_STROKE_PX} px")
    w = cov.shape[1]
    s = layout["seam_px"]
    if cov[:, s - SEAM_CLEAR_PX:s + SEAM_CLEAR_PX].max() > 0.0:
        raise RuntimeError(f"the seam at column {s} crosses ink")
    if max(s, w - s) > MAX_ROW_PX:
        raise RuntimeError(f"row {max(s, w - s)} px does not fit {MAX_ROW_PX} px: the word gap is too far off center")
    rows = np.where(cov.max(axis=1) > 0.5)[0]
    cols = np.where(cov.max(axis=0) > 0.5)[0]
    ink = {"bottom_m": round(rows[0] / PPM, 4), "top_m": round((cov.shape[0] - 1 - rows[-1]) / PPM, 4),
           "left_m": round(cols[0] / PPM, 4), "right_m": round((w - 1 - cols[-1]) / PPM, 4)}
    if min(ink.values()) < 0.05:
        raise RuntimeError(f"ink closer than 5 cm to the board edge: {ink}")
    return ink


def paint_maps(cov, layout):
    """{map: (A, A, 4) array}, rows bottom-up: the strip split into its two rows; the rest of the sheet is field."""
    A = TEX
    field, cream = bytes_to_lin(OXBLOOD_BYTES), bytes_to_lin(CREAM_BYTES)
    s = layout["seam_px"]
    c = np.zeros((A, A), np.float32)
    x, y = ROW_LEFT
    c[y:y + ROW_H, x:x + s] = cov[:, :s]
    x, y = ROW_RIGHT
    c[y:y + ROW_H, x:x + cov.shape[1] - s] = cov[:, s:]
    base = np.ones((A, A, 4), np.float32)
    base[..., :3] = SS._lin_to_srgb(field * (1.0 - c[..., None]) + cream * c[..., None])    # blended in linear
    nrm = np.zeros((A, A, 4), np.float32)
    nrm[..., :2] = 0.5
    nrm[..., 2:] = 1.0
    msk = np.zeros((A, A, 4), np.float32)
    msk[..., 3] = SMOOTH
    ctl = np.zeros((A, A, 4), np.float32)
    return {"BaseColor": (base, False), "Normal": (nrm, True), "MaskMap": (msk, True), "ControlMask": (ctl, True)}


def write_maps(maps, asset_dir):
    for m, (arr, is_data) in maps.items():
        cs2_asset._write_png_raw(os.path.join(asset_dir, f"{NAME}_{m}.png"), f"{NAME}{m}", arr, TEX, is_data)


def check_png_colors(asset_dir):
    """Read back what was written: the field and the letters are the asked colors, within one byte; alpha 255."""
    px = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{NAME}_BaseColor.png"))
    b = np.round(px * 255.0).astype(int)
    if b[..., 3].min() != 255:
        raise RuntimeError("BaseColor alpha under 255 flips the surface to cutout")
    x, y = ROW_PLAIN
    got = b[y + ROW_H // 2, x + 100, :3]
    if np.abs(got - np.array(OXBLOOD_BYTES)).max() > 1:
        raise RuntimeError(f"field reads {tuple(got)}, not {OXBLOOD_BYTES}: an sRGB encode error?")
    lum = b[..., :3].sum(axis=2)
    got = b[np.unravel_index(int(np.argmax(lum)), lum.shape)][:3]
    if np.abs(got - np.array(CREAM_BYTES)).max() > 1:
        raise RuntimeError(f"letters read {tuple(got)}, not {CREAM_BYTES}")
    return {"field": [int(v) for v in b[y + ROW_H // 2, x + 100, :3]], "letters": [int(v) for v in got]}


# ======================================================================================
# Mesh
# ======================================================================================

def build_mesh(seam_y):
    """The board in the authoring frame, origin at its center. Two boxes' worth of quads sharing the seam ring (no
    T-junction); all quads are wound outward. UV: the front halves on their rows (u along +y, v along +z, read from
    +X); the back halves, top, bottom and ends stacked on the plain row with the same scale, wound unmirrored."""
    hx, hy, hz = BOARD_T / 2.0, BOARD_W / 2.0, BOARD_H / 2.0
    ys = (-hy, seam_y, hy)
    A = float(TEX)
    verts, polys, uvs = [], [], []

    def quad(pts, uv):
        base = len(verts)
        verts.extend(pts)
        polys.append(tuple(range(base, base + 4)))
        uvs.extend(tuple(v / A for v in t) for t in uv)

    def row_uv(x0, y0, du_m, dv_m):
        # corners in (u+, v+) order around the quad: (0, 0), (du, 0), (du, dv), (0, dv)
        return [(x0, y0), (x0 + du_m * PPM, y0), (x0 + du_m * PPM, y0 + dv_m * PPM), (x0, y0 + dv_m * PPM)]

    px, py = ROW_PLAIN
    for i, row in enumerate((ROW_LEFT, ROW_RIGHT)):
        ya, yb = ys[i], ys[i + 1]
        L = yb - ya
        # front (+X): viewer's right is +y, up is +z
        quad([(hx, ya, -hz), (hx, yb, -hz), (hx, yb, hz), (hx, ya, hz)], row_uv(row[0], row[1], L, BOARD_H))
        # back (-X): viewer's right is -y
        quad([(-hx, yb, -hz), (-hx, ya, -hz), (-hx, ya, hz), (-hx, yb, hz)], row_uv(px, py, L, BOARD_H))
        # top (+Z), bottom (-Z)
        quad([(hx, ya, hz), (hx, yb, hz), (-hx, yb, hz), (-hx, ya, hz)], row_uv(px, py, L, BOARD_T))
        quad([(-hx, ya, -hz), (-hx, yb, -hz), (hx, yb, -hz), (hx, ya, -hz)], row_uv(px, py, L, BOARD_T))
    # ends: -Y and +Y
    quad([(-hx, -hy, -hz), (hx, -hy, -hz), (hx, -hy, hz), (-hx, -hy, hz)], row_uv(px, py, BOARD_T, BOARD_H))
    quad([(hx, hy, -hz), (-hx, hy, -hz), (-hx, hy, hz), (hx, hy, hz)], row_uv(px, py, BOARD_T, BOARD_H))

    me = bpy.data.meshes.new(NAME)
    me.from_pydata(verts, [], polys)
    me.update()
    lay = me.uv_layers.new(name="UVMap")
    for li in range(len(me.loops)):
        lay.data[li].uv = uvs[me.loops[li].vertex_index]
    ob = bpy.data.objects.new(NAME, me)
    bpy.context.scene.collection.objects.link(ob)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=1e-5)         # UV loops keep their islands; the shell closes
    bpy.ops.object.mode_set(mode="OBJECT")
    cs2_asset.shade_by_angle(ob, 66.0)
    return ob


def check_mesh(ob):
    """The geometry contracts: size, origin at the center, tris under the cap, closed, front facing +X, and the
    text unmirrored (u grows toward the viewer's right, v toward the top, on every front quad)."""
    me = ob.data
    co = np.array([tuple(v.co) for v in me.vertices])
    size = co.max(0) - co.min(0)
    if np.abs(size - np.array((BOARD_T, BOARD_W, BOARD_H))).max() > 1e-5:
        raise RuntimeError(f"board size {size}")
    if np.abs(co.max(0) + co.min(0)).max() > 1e-5:
        raise RuntimeError(f"origin off center: {co.min(0)} .. {co.max(0)}")
    tris = cs2_asset.tri_count(ob)
    if tris > LOD0_CAP:
        raise RuntimeError(f"{tris} tris > cap {LOD0_CAP}")
    edge_use = {}
    for p in me.polygons:
        for e in p.edge_keys:
            edge_use[e] = edge_use.get(e, 0) + 1
    if set(edge_use.values()) != {2}:
        raise RuntimeError(f"the shell is not closed: {sorted(set(edge_use.values()))}")
    lay = me.uv_layers["UVMap"].data
    fronts = 0
    for p in me.polygons:
        c = p.center
        if p.normal.dot(c) <= 0.0:          # a box around the origin: every outward normal points away from it
            raise RuntimeError(f"a face winds inward at {tuple(c)}")
        if p.normal.x > 0.5:
            fronts += 1
            uv = np.array([tuple(lay[li].uv) for li in p.loop_indices])
            pos = np.array([tuple(me.vertices[me.loops[li].vertex_index].co) for li in p.loop_indices])
            du = np.polyfit(pos[:, 1], uv[:, 0], 1)[0]
            dv = np.polyfit(pos[:, 2], uv[:, 1], 1)[0]
            if abs(du * TEX - PPM) > 0.01 or abs(dv * TEX - PPM) > 0.01:
                raise RuntimeError(f"front quad UV {du * TEX:.2f} / {dv * TEX:.2f} px/m: mirrored or off scale")
    if fronts != 2:
        raise RuntimeError(f"{fronts} front quads, want 2")
    return {"size_m": {"long": round(float(size[1]), 4), "high": round(float(size[2]), 4),
                       "thick": round(float(size[0]), 4)}, "tris": tris, "verts": len(me.vertices)}


# ======================================================================================
# Preview (the proxy exists only here; nothing below reaches an FBX)
# ======================================================================================

def panel_material(asset_dir, specular=None):
    mat = bpy.data.materials.new("PanelPreview")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(asset_dir, f"{NAME}_BaseColor.png"), check_existing=False)
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 1.0 - SMOOTH
    if specular is not None:
        bsdf.inputs["Specular IOR Level"].default_value = specular
    return mat


def flat_material(name, srgb_bytes=None, lin=None, emit=False):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    rgb = lin if lin is not None else [float(v) for v in bytes_to_lin(srgb_bytes)]
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.75
    if emit:                                # a flat color: no reflection of the scene on the caption
        em = nt.nodes.new("ShaderNodeEmission")
        em.inputs["Color"].default_value = (*rgb, 1.0)
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    return mat


def box_object(name, lo, hi, top_mat, side_mat):
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(v, [], f)
    me.update()
    me.materials.append(side_mat)
    me.materials.append(top_mat)
    me.polygons[1].material_index = 1
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def setup_render(sc, res, samples=64):
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGB"
    sc.view_settings.view_transform = "Standard"       # the paint as painted (AgX would desaturate the judged colors)
    sc.view_settings.look = "None"
    for cand in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            sc.render.engine = cand
            sc.eevee.taa_render_samples = samples
            return cand
        except TypeError:
            continue
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    return "CYCLES"


def world(sc, rgb, strength):
    w = bpy.data.worlds.new("PanelPreviewWorld")
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (*rgb, 1.0)
    bg.inputs[1].default_value = strength
    sc.world = w


def caption(cam, res, lines):
    """White text on a dark plate in the top-left corner of the frame, carried by the camera."""
    d = cam.data.clip_start * 2.0
    half_h = d * math.tan(math.radians(FOV_DEG) / 2.0)
    half_w = half_h * res[0] / res[1]
    text_h = half_h * 0.052
    white = flat_material("CaptionWhite", lin=(1.0, 1.0, 1.0), emit=True)
    dark = flat_material("CaptionDark", lin=(0.01, 0.01, 0.01), emit=True)
    longest = 0.0
    for i, line in enumerate(lines):
        cu = bpy.data.curves.new(f"Caption{i}", type="FONT")
        cu.body = line
        cu.size = text_h
        ob = bpy.data.objects.new(f"Caption{i}", cu)
        bpy.context.scene.collection.objects.link(ob)
        cu.materials.append(white)
        ob.parent = cam
        ob.visible_shadow = False           # it sits in the sun's path: no shadow of the caption on the ground
        ob.location = (-half_w * 0.97, half_h * 0.93 - text_h * 1.15 * (i + 0.8), -d)
        bpy.context.view_layer.update()
        longest = max(longest, ob.dimensions.x)
    w, h = longest + text_h * 0.8, text_h * (1.15 * len(lines) + 0.35)
    x0, y1 = -half_w * 0.97 - text_h * 0.4, half_h * 0.93
    me = bpy.data.meshes.new("CaptionPlate")
    me.from_pydata([(x0, y1 - h, -d * 1.01), (x0 + w, y1 - h, -d * 1.01), (x0 + w, y1, -d * 1.01), (x0, y1, -d * 1.01)],
                   [], [(0, 1, 2, 3)])
    me.materials.append(dark)
    pl = bpy.data.objects.new("CaptionPlate", me)
    bpy.context.scene.collection.objects.link(pl)
    pl.parent = cam
    pl.visible_shadow = False


def stamp_inset(path, res):
    """The frame's center, nearest-neighbor enlarged, in the bottom-right corner of the saved PNG (rows bottom-up):
    the real rendered pixels at a size a downscaled view of the 1080p frame still shows."""
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    if (w, h) != res:
        raise RuntimeError(f"inset: {path} is {w} x {h}, not {res}")
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    cw, ch = INSET_CROP
    crop = px[(h - ch) // 2:(h - ch) // 2 + ch, (w - cw) // 2:(w - cw) // 2 + cw].copy()
    big = crop.repeat(INSET_K, axis=0).repeat(INSET_K, axis=1)
    bw, bh, m, frame = big.shape[1], big.shape[0], 24, 3
    px[m - frame:m + bh + frame, w - m - bw - frame:w - m + frame, :3] = 1.0
    px[m:m + bh, w - m - bw:w - m] = big
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def clear_captions():
    for ob in list(bpy.data.objects):
        if ob.name.startswith("Caption"):
            bpy.data.objects.remove(ob, do_unlink=True)


def render_front(ob, out_dir):
    """Preview 1: the board alone, front on, orthographic, even light: the paint as painted."""
    sc = bpy.context.scene
    world(sc, (1.0, 1.0, 1.0), 1.0)
    res = (2400, 280)
    engine = setup_render(sc, res)
    cd = bpy.data.cameras.new("PanelFrontCam")
    cd.type = "ORTHO"
    cd.ortho_scale = BOARD_W * 1.02
    cam = bpy.data.objects.new("PanelFrontCam", cd)
    sc.collection.objects.link(cam)
    cam.location = (5.0, 0.0, 0.0)
    cam.rotation_euler = Vector((-1, 0, 0)).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    sc.render.film_transparent = False
    path = os.path.join(out_dir, f"{NAME}_front.png")
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path, engine


def render_context(ob, out_dir):
    """Preview 2: the same mesh three times on the labeled proxy, viewed from 3/4 high at three zooms."""
    sc = bpy.context.scene
    world(sc, (0.72, 0.80, 0.92), 0.55)
    res = (1920, 1080)
    engine = setup_render(sc, res)
    sun = bpy.data.lights.new("PanelSun", "SUN")
    sun.energy = 2.4
    sun.angle = math.radians(1.0)
    so = bpy.data.objects.new("PanelSun", sun)
    sc.collection.objects.link(so)
    so.rotation_euler = Vector((0.55, 0.65, -0.75)).normalized().to_track_quat("-Z", "Y").to_euler()

    body = flat_material("ProxyBody", lin=(0.55, 0.56, 0.57))
    roof = flat_material("ProxyRoof", srgb_bytes=OXBLOOD_BYTES)
    box_object("ProxyBus", (-PROXY_L / 2, -PROXY_W / 2, 0.0), (PROXY_L / 2, PROXY_W / 2, PROXY_H), roof, body)
    ground = flat_material("ProxyGround", lin=(0.22, 0.22, 0.22))
    box_object("ProxyGround", (-1500.0, -1500.0, -0.2), (1500.0, 1500.0, -0.01), ground, ground)

    # rotations, never a scale: the mesh is shared. Side -Y faces the camera; the roof board's top points to +Y.
    off = PROXY_W / 2 + PROXY_GAP + BOARD_T / 2
    side_z = PROXY_H - 0.05 - BOARD_H / 2
    rows = (("PanelSideNegY", (0.0, -off, side_z), Matrix.Rotation(math.radians(-90.0), 3, "Z")),
            ("PanelSidePosY", (0.0, off, side_z), Matrix.Rotation(math.radians(90.0), 3, "Z")),
            ("PanelRoof", (0.0, 0.0, PROXY_H + PROXY_GAP + BOARD_T / 2),
             Matrix(((0.0, 1.0, 0.0), (0.0, 0.0, 1.0), (1.0, 0.0, 0.0)))))
    ob.hide_render = True
    for nm, loc, rot in rows:
        o = bpy.data.objects.new(nm, ob.data)
        sc.collection.objects.link(o)
        o.matrix_world = Matrix.Translation(loc) @ rot.to_4x4()

    cd = bpy.data.cameras.new("ContextCam")
    cd.sensor_fit = "VERTICAL"
    cd.sensor_height = 24.0
    cd.lens = 12.0 / math.tan(math.radians(FOV_DEG) / 2.0)
    cd.clip_start = 0.5
    cd.clip_end = 6000.0
    cam = bpy.data.objects.new("ContextCam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    target = Vector((0.0, 0.0, PROXY_H * 0.6))
    d = Vector(VIEW_DIR).normalized()
    paths = []
    for view, dist, label, inset in CONTEXT_SHOTS:
        cam.location = target + d * dist
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        bpy.context.view_layer.update()
        px_m = res[1] / (2.0 * dist * math.tan(math.radians(FOV_DEG) / 2.0))
        caption(cam, res, ("PROXY BOX, NOT THE REAL BUS (2.55 x 3.20 x 12.00 m)",
                           f"{label}: {dist:.0f} m, {FOV_DEG:.0f} deg vertical FOV, letters about {px_m * CAP_M:.0f} px tall "
                           f"at 1080p" + (f"; inset: the same pixels x {INSET_K}" if inset else "")))
        path = os.path.join(out_dir, f"{NAME}_{view}.png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        if inset:
            stamp_inset(path, res)
        paths.append(path)
        clear_captions()
    return paths, engine


# ======================================================================================
# Run
# ======================================================================================

def run(args):
    t0 = time.time()
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    asset_dir = os.path.join(os.path.abspath(args.out), NAME)
    os.makedirs(asset_dir, exist_ok=True)
    for f in os.listdir(asset_dir):
        p = os.path.join(asset_dir, f)
        if os.path.isfile(p) and (f.startswith(NAME) or f == "settings.json"):
            os.remove(p)

    cov, layout = paint_strip(args.face, mark=not args.no_mark)
    ink = check_strip(cov, layout)
    write_maps(paint_maps(cov, layout), asset_dir)
    colors = check_png_colors(asset_dir)
    seam_y = layout["seam_px"] / PPM - BOARD_W / 2.0

    ob = build_mesh(seam_y)
    geo = check_mesh(ob)
    print(f"[panel] {geo}, seam y {seam_y:+.4f}, ink margins {ink}, {layout['font']} stem {layout['stem_px']} px, "
          f"colors {colors}")

    out_prev = None
    if args.preview is not None:
        out_prev = os.path.abspath(args.preview) if args.preview else PREVIEW_DIR
        os.makedirs(out_prev, exist_ok=True)
        ob.data.materials.clear()
        ob.data.materials.append(panel_material(asset_dir, specular=0.0))     # the paint, no sky sheen on it
        path, engine = render_front(ob, out_prev)
        print(f"[panel] preview {path} ({engine})")
        if args.texture_only:
            print(f"PASS ski_bus_panel: texture look only, {time.time() - t0:.1f} s")
            return
        ob.data.materials.clear()
        ob.data.materials.append(panel_material(asset_dir))
        paths, engine = render_context(ob, out_prev)
        for p in paths:
            print(f"[panel] preview {p} ({engine})")
        ob.data.materials.clear()

        for o in list(bpy.data.objects):        # the proxy rig is gone before anything is exported
            if o is not ob:
                bpy.data.objects.remove(o, do_unlink=True)

    # the scratch asset folder, for the validator only (never assets/import)
    ob.hide_render = False
    cs2_asset.write_settings(asset_dir, NAME)
    cs2_asset.orient_for_net(ob)
    cs2_asset.export_fbx(ob, os.path.join(asset_dir, f"{NAME}.fbx"))
    cs2_validate.write_manifest(asset_dir, NAME, {
        "generator": "ski_bus_panel.py", "kind": "hanging (hosted sub-object, origin at the board center)",
        "object_prefab": "SkiBusPanel", "tris": geo["tris"], "board_m": geo["size_m"], "tex": TEX,
        "ppm": PPM, "layout": layout, "ink_margins_m": ink, "paint_bytes": colors,
        "stand_in": "modeled: no vanilla prop carries this text"})
    verdict = "skipped"
    if not args.no_validate:
        # "hanging" is the declared kind (a hosted board, origin at its center, so no ground rule); the "prop" pass
        # adds the per-part density floor, which is the only thing wanted from it
        counts = {}
        for kind in ("hanging", "prop"):
            findings, summary = cs2_validate.validate_asset_folder_ex(
                asset_dir, NAME, {"kind": kind, "lod0_cap": LOD0_CAP, "expected_lods": (0,)})
            for lv, code, msg in findings:
                print(f"[validate {kind}] {lv} {code}: {msg}")
            counts[kind] = (sum(f[0] == "ERROR" for f in findings), sum(f[0] == "WARN" for f in findings))
            if counts[kind][0]:
                raise RuntimeError(f"{counts[kind][0]} validator errors as {kind}")
        verdict = f"(errors, warnings) {counts}, tris {summary.get('tris')}, tex {sorted(summary.get('tex') or [])}"
    print(f"PASS ski_bus_panel: {NAME} {geo['tris']} tris, {geo['size_m']}, tex {TEX}, {layout['font']}, validator "
          f"{verdict}, {time.time() - t0:.1f} s")


if __name__ == "__main__":
    try:
        run(parse_args())
    except Exception:   # noqa: BLE001 - --python-exit-code 1 makes it a non-zero exit
        traceback.print_exc()
        raise
