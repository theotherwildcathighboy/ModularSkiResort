"""
safety_fence.py - the orange safety-net fence (twenty-fifth look, 2026-10-13: "ORANGE PLASTIC
FENCING that is ubiquitous at ski areas ... another FENCE TYPE ... use all the functions built in already
of fences"). Design: docs/research/art/assets-2026-10/design/safety-fence.md (sections 3, 4, 7).
Build report: docs/research/art/assets-2026-10/build/safety-fence.md. Prefab staging + S1 surgery:
safety_fence_stage.py (pure Python, no bpy).

    cd assets/blender
    blender --background --factory-startup --python-exit-code 1 --python safety_fence.py -- \
        --out ../import/fences/A --export [--preview ../preview/fences/A] [--review] [--stage]

THE ASSETS (prefab-local meters: x lateral, y up, z travel along the lane)
    SkiSafetyNetA       the LANE MESH tile the vanilla curve shader repeats along a drawn fence
                        (the FenceIndustrialPieceHigh01 Mesh pattern: posts baked into the tile).
                        One 4.000 m tile, z in [-2, +2], two 2 m bays, vertex columns every 0.5 m so
                        TilingCount 2 / 4 / 8 all clip on columns (clipping collapses vertices; it
                        cannot cut a triangle). Posts: 6-sided, circumradius 17.5 mm, y -0.15 .. 1.55
                        (15 cm in the snow), flats facing the travel axis, center 0.020 m after each
                        bay start (z = -1.98 and +0.02) so post AND cap lie inside [s, s + 0.040].
                        Caps: black 6-sided, circumradius 22.5 mm, y 1.52 .. 1.58. Net: one plane per
                        bay at x = +0.020 (tied to one side of the posts), hem y -0.10 (buried), top
                        y 1.25 at the posts sagging to 1.17 mid-bay (y = 1.25 - 0.08 (1 - (2u - 1)^2)),
                        interior columns bulge x += 0.02 sin(pi u); every net triangle has a coincident
                        opposite-normal twin (the Curved templates have no double-sided variant).
                        LOD0 96 tris (cap 128). LOD1 44 tris (cap 60, < 50 % of LOD0): same columns,
                        straight top 1.20, no bulge, 3-sided posts, no caps.
    SkiNetPostA         the optional dead-end post (UtilityLane.m_NodeObject -> SkiNetPost): post + cap,
                        y 0 .. 1.58 (prop ground rule), 32 tris, own 256 px set, no alpha.

TEXTURES (SkiSafetyNetA 1024 px; LOD1 shares LOD0's maps through sharedAssets)
    net region  u 0..1, v 0.3125..1 (rows 320..1023, 512 px/m across a 2 m bay): diamond mesh, 32 px
                cells (6.25 cm), strands 5.6 px (half-width 2.8 + 1 px soft edge), mean alpha 0.45;
                opaque 12 px selvedges top and bottom (the top edge vertices sit on v = 1, so the
                selvedge follows the sag). RGB of alpha-0 texels = strand RGB (no dark mip fringe).
    post strip  rows 0..319, alpha 255: post band (6 x 16 rows; LOD1 3 x 32), dark clip marks at
                y 0.20 / 1.20, black cap block.
    ControlMask net G = 1 (slot 2), posts B = 1 (slot 3), caps / clips 0, A = 0. MaskMap R = G = B = 0,
    A smoothness net 0.35 / posts 0.45 / caps 0.30. Normal flat. Recolor texels RGB 0.80
    (cs2_asset.RECOLOR_ALBEDO): the orange comes from the mesh prefab's ColorProperties (PAD_RGB).
    settings.json: sharedAssets (LOD1 -> LOD0 maps), LOD1 Skip and the Surface block
    {"materialTemplate": "CurvedHashedCutout", "needsAlphaClip": true, "alphaCutoff": 0.54}
    (cs2_asset.write_settings(surface=...)): a lane mesh must land on a Curved* template.

DETERMINISM
    Explicit vertex lists and hand-laid UVs (no smart_project, no unique repack, no Cycles bake),
    cs2_asset.tag_parts canonical order, numpy maps with a fixed seed. Two builds hash identically and
    two fresh processes write byte-identical PNGs and settings.json (tests/test_safety_fence.py).

No top-level side effects; the __main__ block runs only as a Blender --python script.
"""

import argparse
import json
import math
import os
import sys

import bmesh
import bpy
import mathutils
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import cs2_asset            # noqa: E402
import cs2_validate         # noqa: E402
import safety_fence_stage as STG   # noqa: E402

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))

# --------------------------------------------------------------------------------------
# Names, budgets
# --------------------------------------------------------------------------------------

NAME = "SkiSafetyNetA"            # lane mesh; the Editor import makes "SkiSafetyNetA Mesh" (+ _LOD1)
POST_NAME = "SkiNetPostA"         # node / end post mesh -> "SkiNetPostA Mesh"
A = 1024                          # net atlas px
A_POST = 256                      # post set px
LOD0_TRIS, LOD0_CAP = 96, 128
LOD1_TRIS, LOD1_CAP = 44, 60
POST_TRIS, POST_CAP = 32, 40
SURFACE = {"materialTemplate": "CurvedHashedCutout", "needsAlphaClip": True, "alphaCutoff": 0.54}
VALIDATE_SPEC = {"kind": "hanging", "cutout": True, "lod0_cap": LOD0_CAP, "expected_lods": (0, 1)}
VALIDATE_SPEC_POST = {"kind": "prop", "lod0_cap": POST_CAP, "min_texture": 128, "expected_lods": (0,)}

# --------------------------------------------------------------------------------------
# Geometry (authoring frame: x = travel along the lane, y = lateral, z = up; orient_for_net
# turns it into the net frame: Blender x = prefab x lateral, Blender -y = prefab z travel)
# --------------------------------------------------------------------------------------

TILE = 4.0
BAY = 2.0
COLS_PER_BAY = 4                  # 0.5 m columns
BAY_STARTS = (-2.0, 0.0)
NET_LAT = 0.020
BULGE = 0.020
HEM = -0.10
TOP = 1.25
SAG = 0.08
LOD1_TOP = 1.20
POST_R, POST_SIDES = 0.0175, 6
CAP_R = 0.0225
POST_Y = (-0.15, 1.55)
CAP_Y = (1.52, 1.58)
POST_AT = 0.020                   # post center after the bay start (cap flats: [0.0005, 0.0395])
POST_WINDOW = 0.040               # contract C4
LOD1_POST_SIDES = 3
NODE_POST_Y = (0.0, 1.55)         # SkiNetPostA body; its cap is CAP_Y

# --------------------------------------------------------------------------------------
# Atlas (texel rects x0, y0, x1, y1; y up = UV v = Image.pixels row order)
# --------------------------------------------------------------------------------------

R_NET = (0, 320, 1024, 1024)
SELVEDGE_PX = 12
CELL_PX = 32
STRAND_HALF_PX = 2.8
R_POST_BAND = (8, 16, 1016, 112)   # u = height (POST_Y), v = 6 strips x 16 rows (LOD1 3 x 32)
R_CAP = (16, 160, 112, 256)
CLIP_Y = (0.20, 1.20)              # dark clip bands on the posts (world height)
CLIP_HALF_PX = 8
R_POST_BAND_P = (4, 32, 252, 128)  # SkiNetPostA (256 px): u = height (NODE_POST_Y)
R_CAP_P = (16, 160, 80, 224)
CLIP_HALF_PX_P = 3
INSET = 0.5                        # texels: strip UVs sit on texel centers

RECOLOR = cs2_asset.RECOLOR_ALBEDO     # 0.80
STRAND_SHADE = 0.02                    # core +0.02 / edge -0.02 around RECOLOR
NOISE_AMP = 0.01
CAP_SRGB, CLIP_SRGB = 0.04, 0.06
SMOOTH = {"net": 0.35, "post": 0.45, "cap": 0.30, "clip": 0.30}
SEED = 20261013

PREVIEW_DISTANCES = (2.0, 10.0, 50.0)
PREVIEW_VIEWS = ("front", "oblique", "backlit")
#: preview-only A/B: a deeper "fluo" orange for slot 2 / 3 (the shipped default stays PAD_RGB;
#: the in-game VariationSet color space is unverified, so this is a JSON knob for review)
ALT_TINT = (1.0, 0.15, 0.02)


# --------------------------------------------------------------------------------------
# Scene + part helpers
# --------------------------------------------------------------------------------------

def clear_scene():
    """Delete every object and orphaned datablock (headless builds only)."""
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.cameras,
                 bpy.data.lights, bpy.data.worlds):
        for item in list(coll):
            if item.users == 0:
                coll.remove(item)


def _part(name, verts, faces, uvs, atlas):
    """Mesh object from explicit verts / faces and per-face texel UVs (layer "UVMap")."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    me.update()
    layer = me.uv_layers.new(name="UVMap")
    for poly, fuv in zip(me.polygons, uvs):
        if len(fuv) != poly.loop_total:
            raise RuntimeError(f"{name}: face {poly.index} has {poly.loop_total} loops, {len(fuv)} UVs")
        for k, li in enumerate(poly.loop_indices):
            layer.data[li].uv = (fuv[k][0] / atlas, fuv[k][1] / atlas)
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    return obj


def _strip_uv(rect, k, n, h0, h1, y_range, y):
    """Texel UV on strip k of n of `rect` (u = height y over y_range, v = strip rows, top edge
    for the face's first ring vertex)."""
    x0, y0, x1, y1 = rect
    rows = (y1 - y0) / n
    u = x0 + INSET + (y - y_range[0]) / (y_range[1] - y_range[0]) * (x1 - x0 - 2 * INSET)
    v_top = y0 + (k + 1) * rows - INSET
    v_bot = y0 + k * rows + INSET
    return u, (v_top if h0 else v_bot)


def _ring(cx, r, n, z, phase_deg):
    return [(cx + r * math.cos(math.radians(phase_deg + 360.0 * i / n)),
             r * math.sin(math.radians(phase_deg + 360.0 * i / n)), z) for i in range(n)]


def _prism_sides(name, cx, r, n, z0, z1, phase_deg, rect, y_range, atlas):
    """Outward n-gon prism sides (no caps); strip k of `rect` per side."""
    verts = _ring(cx, r, n, z0, phase_deg) + _ring(cx, r, n, z1, phase_deg)
    faces, uvs = [], []
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
        uvs.append([_strip_uv(rect, i, n, True, None, y_range, z0),
                    _strip_uv(rect, i, n, False, None, y_range, z0),
                    _strip_uv(rect, i, n, False, None, y_range, z1),
                    _strip_uv(rect, i, n, True, None, y_range, z1)])
    return _part(name, verts, faces, uvs, atlas)


def _cap(name, cx, rect, atlas):
    """Black 6-sided cap: sides + top + bottom (each hexagon = 2 planar quads)."""
    n = POST_SIDES
    z0, z1 = CAP_Y
    bot = _ring(cx, CAP_R, n, z0, 30.0)
    top = _ring(cx, CAP_R, n, z1, 30.0)
    verts = bot + top
    x0, y0, x1, y1 = rect
    w = (x1 - x0) / 2.0
    side_rect = (x0, y0, x1, y0 + w)
    faces, uvs = [], []
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
        uvs.append([_strip_uv(side_rect, i, n, True, None, (z0, z1), z0),
                    _strip_uv(side_rect, i, n, False, None, (z0, z1), z0),
                    _strip_uv(side_rect, i, n, False, None, (z0, z1), z1),
                    _strip_uv(side_rect, i, n, True, None, (z0, z1), z1)])

    def hex_uv(cxp, cyp, i, mirror):
        t = math.radians(30.0 + 60.0 * i)
        rr = 0.5 * w - 2.0
        return (cxp + (-1.0 if mirror else 1.0) * rr * math.cos(t), cyp + rr * math.sin(t))

    tc = (x0 + 0.5 * w, y0 + 1.5 * w)
    bc = (x0 + 1.5 * w, y0 + 1.5 * w)
    for quad in ((0, 1, 2, 3), (3, 4, 5, 0)):                      # top, CCW from above
        faces.append(tuple(n + q for q in quad))
        uvs.append([hex_uv(tc[0], tc[1], q, False) for q in quad])
    for quad in ((3, 2, 1, 0), (0, 5, 4, 3)):                      # bottom, CCW from below
        faces.append(quad)
        uvs.append([hex_uv(bc[0], bc[1], q, True) for q in quad])
    return _part(name, verts, faces, uvs, atlas)


def _net_bay(name, bay, side, lod):
    """One bay of net as explicit triangles. side 'a' faces -lateral, 'b' (+lateral) is the
    coincident twin with reversed winding and mirrored u (both keep CCW UV winding)."""
    xs = BAY_STARTS[bay]
    col_v = []
    for c in range(COLS_PER_BAY + 1):
        u = c / COLS_PER_BAY
        x = xs + u * BAY
        if lod == 0:
            top = TOP - SAG * (1.0 - (2.0 * u - 1.0) ** 2)
            lat = NET_LAT + BULGE * math.sin(math.pi * u)
        else:
            top, lat = LOD1_TOP, NET_LAT
        col_v.append(((x, lat, HEM), (x, lat, top), u))
    verts, faces, uvs = [], [], []
    for b, t, _u in col_v:
        verts += [b, t]

    def uv(u, top):
        uu = (u if side == "a" else 1.0 - u) * R_NET[2]
        return (uu, R_NET[3] if top else R_NET[1])

    for c in range(COLS_PER_BAY):
        b0, t0, b1, t1 = 2 * c, 2 * c + 1, 2 * c + 2, 2 * c + 3
        u0, u1 = col_v[c][2], col_v[c + 1][2]
        if side == "a":
            faces += [(b0, b1, t1), (b0, t1, t0)]
            uvs += [[uv(u0, False), uv(u1, False), uv(u1, True)],
                    [uv(u0, False), uv(u1, True), uv(u0, True)]]
        else:
            faces += [(b0, t1, b1), (b0, t0, t1)]
            uvs += [[uv(u0, False), uv(u1, True), uv(u1, False)],
                    [uv(u0, False), uv(u0, True), uv(u1, True)]]
    return _part(name, verts, faces, uvs, A)


def _join(parts, name):
    table = cs2_asset.tag_parts([(p, None) for p in parts])
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    cs2_asset.store_part_table(obj, table)
    return obj


def build_tile(name=NAME, lod=0):
    """The lane-mesh tile (LOD0 or LOD1), finished: tagged, joined, net-oriented, shaded."""
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    parts = []
    for bay in (0, 1):
        for side in ("a", "b"):
            parts.append(_net_bay(f"net_{side}{bay}", bay, side, lod))
    for k, s in enumerate(BAY_STARTS):
        cx = s + POST_AT
        if lod == 0:
            parts.append(_prism_sides(f"post_{k}", cx, POST_R, POST_SIDES, POST_Y[0], POST_Y[1], 30.0,
                                      R_POST_BAND, POST_Y, A))
            parts.append(_cap(f"cap_{k}", cx, R_CAP, A))
        else:
            parts.append(_prism_sides(f"post_{k}", cx, POST_R, LOD1_POST_SIDES, POST_Y[0], POST_Y[1],
                                      90.0, R_POST_BAND, POST_Y, A))
    obj = _join(parts, name)
    cs2_asset.orient_for_net(obj)
    cs2_asset.shade_by_angle(obj, 66.0, weighted=(lod == 0))
    return obj


def build_post(name=POST_NAME):
    """SkiNetPostA: post y 0 .. 1.55 + cap 1.52 .. 1.58, centered, net-oriented."""
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    parts = [_prism_sides("post_0", 0.0, POST_R, POST_SIDES, NODE_POST_Y[0], NODE_POST_Y[1], 30.0,
                          R_POST_BAND_P, NODE_POST_Y, A_POST),
             _cap("cap_0", 0.0, R_CAP_P, A_POST)]
    obj = _join(parts, name)
    cs2_asset.orient_for_net(obj)
    cs2_asset.shade_by_angle(obj, 66.0)
    return obj


# --------------------------------------------------------------------------------------
# Maps
# --------------------------------------------------------------------------------------

def _sl(rect):
    x0, y0, x1, y1 = rect
    return slice(int(y0), int(y1)), slice(int(x0), int(x1))


def _smooth_noise(rng, shape):
    n = rng.standard_normal(shape).astype(np.float32)
    for _ in range(3):
        n = (n + np.roll(n, 1, 0) + np.roll(n, -1, 0) + np.roll(n, 1, 1) + np.roll(n, -1, 1)) / 5.0
    return n / max(float(np.abs(n).max()), 1e-6)


def net_alpha():
    """(704, 1024) float alpha of the net region (rows bottom-up) and the strand distance field."""
    h = R_NET[3] - R_NET[1]
    w = R_NET[2] - R_NET[0]
    y, x = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5

    def dist(t):
        s = np.mod(t, CELL_PX)
        return np.minimum(s, CELL_PX - s) / math.sqrt(2.0)

    d = np.minimum(dist(x + y), dist(x - y))
    a = np.clip(STRAND_HALF_PX + 0.5 - d, 0.0, 1.0)
    a[:SELVEDGE_PX] = 1.0
    a[h - SELVEDGE_PX:] = 1.0
    d[:SELVEDGE_PX] = 0.0
    d[h - SELVEDGE_PX:] = 0.0
    return a.astype(np.float32), d.astype(np.float32)


def _post_band_px(rect, y_range, y):
    x0, _y0, x1, _y1 = rect
    return x0 + INSET + (y - y_range[0]) / (y_range[1] - y_range[0]) * (x1 - x0 - 2 * INSET)


def paint_net_maps():
    """{suffix: (rgba float32 (A, A, 4) bottom-up rows, is_data)} for SkiSafetyNetA."""
    rng = np.random.default_rng(SEED)
    noise = _smooth_noise(rng, (A, A))
    base = np.zeros((A, A, 4), np.float32)
    nrm = np.zeros((A, A, 4), np.float32)
    msk = np.zeros((A, A, 4), np.float32)
    ctl = np.zeros((A, A, 4), np.float32)
    nrm[..., 0] = 0.5
    nrm[..., 1] = 0.5
    nrm[..., 2] = 1.0
    nrm[..., 3] = 1.0
    # post strip background (rows 0..319): opaque, slot 2, like the selvedge it borders
    base[..., 0:3] = RECOLOR
    base[..., 3] = 1.0
    ctl[..., 1] = 1.0
    msk[..., 3] = SMOOTH["net"]
    # net region
    ys, xs = _sl(R_NET)
    a, d = net_alpha()
    core = 1.0 - 2.0 * np.clip(d / STRAND_HALF_PX, 0.0, 1.0)
    rgb = RECOLOR + STRAND_SHADE * core + NOISE_AMP * noise[ys, xs]
    rgb = np.where(a > 0.0, rgb, RECOLOR + NOISE_AMP * noise[ys, xs])
    for c in range(3):
        base[ys, xs, c] = rgb
    base[ys, xs, 3] = a
    # post band: slot 3, clip marks
    ys, xs = _sl(R_POST_BAND)
    for c in range(3):
        base[ys, xs, c] = RECOLOR + NOISE_AMP * noise[ys, xs]
    ctl[ys, xs, 1] = 0.0
    ctl[ys, xs, 2] = 1.0
    msk[ys, xs, 3] = SMOOTH["post"]
    for yc in CLIP_Y:
        cx = _post_band_px(R_POST_BAND, POST_Y, yc)
        cs = slice(int(round(cx - CLIP_HALF_PX)), int(round(cx + CLIP_HALF_PX)))
        base[ys, cs, 0:3] = CLIP_SRGB
        ctl[ys, cs, 0:3] = 0.0
        msk[ys, cs, 3] = SMOOTH["clip"]
    # cap block
    ys, xs = _sl(R_CAP)
    base[ys, xs, 0:3] = CAP_SRGB
    ctl[ys, xs, 0:3] = 0.0
    msk[ys, xs, 3] = SMOOTH["cap"]
    for arr in (base, nrm, msk, ctl):
        np.clip(arr, 0.0, 1.0, out=arr)
    msk[..., 0:3] = 0.0
    ctl[..., 3] = 0.0
    return {"BaseColor": (base, False), "Normal": (nrm, True), "MaskMap": (msk, True),
            "ControlMask": (ctl, True)}


def paint_post_maps():
    """{suffix: (rgba, is_data)} for SkiNetPostA (256 px, no alpha)."""
    rng = np.random.default_rng(SEED + 1)
    noise = _smooth_noise(rng, (A_POST, A_POST))
    base = np.zeros((A_POST, A_POST, 4), np.float32)
    nrm = np.zeros_like(base)
    msk = np.zeros_like(base)
    ctl = np.zeros_like(base)
    nrm[..., 0] = 0.5
    nrm[..., 1] = 0.5
    nrm[..., 2] = 1.0
    nrm[..., 3] = 1.0
    for c in range(3):
        base[..., c] = RECOLOR + NOISE_AMP * noise
    base[..., 3] = 1.0
    ctl[..., 2] = 1.0
    msk[..., 3] = SMOOTH["post"]
    ys, xs = _sl(R_POST_BAND_P)
    for yc in CLIP_Y:
        cx = _post_band_px(R_POST_BAND_P, NODE_POST_Y, yc)
        cs = slice(int(round(cx - CLIP_HALF_PX_P)), int(round(cx + CLIP_HALF_PX_P)))
        base[ys, cs, 0:3] = CLIP_SRGB
        ctl[ys, cs, 0:3] = 0.0
        msk[ys, cs, 3] = SMOOTH["clip"]
    ys, xs = _sl(R_CAP_P)
    base[ys, xs, 0:3] = CAP_SRGB
    ctl[ys, xs, 0:3] = 0.0
    msk[ys, xs, 3] = SMOOTH["cap"]
    for arr in (base, nrm, msk, ctl):
        np.clip(arr, 0.0, 1.0, out=arr)
    msk[..., 0:3] = 0.0
    ctl[..., 3] = 0.0
    return {"BaseColor": (base, False), "Normal": (nrm, True), "MaskMap": (msk, True),
            "ControlMask": (ctl, True)}


def write_maps(asset_dir, name, maps, size):
    out = []
    for suffix in ("BaseColor", "Normal", "MaskMap", "ControlMask"):
        arr, is_data = maps[suffix]
        path = os.path.join(asset_dir, f"{name}_{suffix}.png")
        cs2_asset._write_png_raw(path, f"{name}_{suffix}", arr, size, is_data)
        out.append(path)
    return out


# --------------------------------------------------------------------------------------
# Contracts (raise on failure; return the measured numbers)
# --------------------------------------------------------------------------------------

def _prefab_co(obj):
    """Vertices in prefab-local meters (x lateral, y up, z travel) = what export_fbx bakes."""
    co = np.empty(len(obj.data.vertices) * 3, dtype=np.float64)
    obj.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    return np.stack([co[:, 0], co[:, 2], -co[:, 1]], axis=1)


def _part_names(obj):
    table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    vals = np.zeros(len(obj.data.polygons), dtype=np.int64)
    obj.data.attributes["part"].data.foreach_get("value", vals)
    return [table[v - 1] for v in vals]


def _uv_faces(obj, atlas):
    layer = obj.data.uv_layers["UVMap"]
    out = []
    for p in obj.data.polygons:
        out.append(np.array([layer.data[li].uv[:] for li in p.loop_indices], dtype=np.float64) * atlas)
    return out


def _signed_area(pts):
    x, y = pts[:, 0], pts[:, 1]
    return 0.5 * float(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))


def tile_contracts(obj, lod, maps=None):
    """C2-C7 on a finished tile (meters; the FBX-side C2 is re-checked in the test)."""
    fails = []
    me = obj.data
    P = _prefab_co(obj)
    lo, hi = P.min(axis=0), P.max(axis=0)
    tris = cs2_asset.tri_count(obj)
    want_tris, cap = (LOD0_TRIS, LOD0_CAP) if lod == 0 else (LOD1_TRIS, LOD1_CAP)
    if tris != want_tris or tris > cap:
        fails.append(f"C5 tris {tris} (expected {want_tris}, cap {cap})")
    if lod == 1 and not tris < 0.5 * LOD0_TRIS:
        fails.append(f"C5 LOD1 {tris} not < 50 % of LOD0 {LOD0_TRIS}")
    top = CAP_Y[1] if lod == 0 else POST_Y[1]
    if abs(lo[2] + TILE / 2) > 1e-4 or abs(hi[2] - TILE / 2) > 1e-4:
        fails.append(f"C2 z extent {lo[2]:.5f} .. {hi[2]:.5f} != -2 .. +2")
    if abs(lo[1] - POST_Y[0]) > 1e-4 or abs(hi[1] - top) > 1e-4:
        fails.append(f"C2 y extent {lo[1]:.5f} .. {hi[1]:.5f} != {POST_Y[0]} .. {top}")
    if lo[0] < -0.025 - 1e-6 or hi[0] > 0.045 + 1e-6:
        fails.append(f"C2 x extent {lo[0]:.5f} .. {hi[0]:.5f} outside -0.025 .. +0.045")
    names = _part_names(obj)
    polys = list(me.polygons)
    vpart = {}
    for p, n in zip(polys, names):
        for v in p.vertices:
            vpart[v] = n
    net_v = [v for v, n in vpart.items() if n.startswith("net_")]
    cols = -2.0 + 0.5 * np.round((P[net_v, 2] + 2.0) / 0.5)
    off_col = int(np.count_nonzero(np.abs(P[net_v, 2] - cols) > 1e-3))
    if off_col:
        fails.append(f"C3 {off_col} net vertices off the 0.5 m columns")
    worst = (1.0, -1.0)
    bad_post = 0
    for v, n in vpart.items():
        if n.startswith(("post_", "cap_")):
            z = P[v, 2]
            s = min(BAY_STARTS, key=lambda b: abs(z - b))
            r = z - s
            worst = (min(worst[0], r), max(worst[1], r))
            if r < -1e-6 or r > POST_WINDOW + 1e-6:
                bad_post += 1
    if bad_post:
        fails.append(f"C4 {bad_post} post / cap vertices outside [s, s + {POST_WINDOW}] "
                     f"(range {worst[0]:.4f} .. {worst[1]:.4f})")
    # C6 twins
    keyed = {}
    for p, n in zip(polys, names):
        if n.startswith("net_"):
            key = tuple(sorted(tuple(np.round(P[v], 5)) for v in p.vertices))
            keyed.setdefault(key, []).append(p.normal.copy())
    no_twin = sum(1 for k, ns in keyed.items() if len(ns) != 2 or ns[0].dot(ns[1]) > -0.999)
    n_net = sum(1 for n in names if n.startswith("net_"))
    if no_twin or n_net != 2 * len(keyed):
        fails.append(f"C6 {no_twin} net faces without a coincident opposite twin ({n_net} faces)")
    # C7 UVs
    uvf = _uv_faces(obj, A)
    alpha = maps["BaseColor"][0][..., 3] if maps else None
    bad_uv = zero = mirrored = 0
    for p, n, pts in zip(polys, names, uvf):
        sa = _signed_area(pts)
        if abs(sa) < 1e-9:
            zero += 1
        elif sa < 0.0:
            mirrored += 1
        if (pts < -1e-6).any() or (pts > A + 1e-6).any():
            bad_uv += 1
            continue
        if n.startswith("net_"):
            if pts[:, 1].min() < R_NET[1] - 1e-6:
                bad_uv += 1
        else:
            if pts[:, 1].max() > R_NET[1] - 1.0:
                bad_uv += 1
            elif alpha is not None:
                c0, c1 = int(math.floor(pts[:, 0].min())), int(math.ceil(pts[:, 0].max()))
                r0, r1 = int(math.floor(pts[:, 1].min())), int(math.ceil(pts[:, 1].max()))
                if float(alpha[max(r0 - 1, 0):r1 + 1, max(c0 - 1, 0):c1 + 1].min()) < 1.0:
                    bad_uv += 1
    if bad_uv or zero or mirrored:
        fails.append(f"C7 UV faces: {bad_uv} outside their region / on alpha < 255, {zero} zero-area, "
                     f"{mirrored} mirrored")
    # outward normals on posts / caps
    inward = 0
    for p, n in zip(polys, names):
        if not n.startswith(("post_", "cap_")):
            continue
        c = np.array(p.center[:])
        k = int(n.split("_")[1])
        cx = BAY_STARTS[k] + POST_AT                      # authoring travel -> Blender -y
        axis = np.array([0.0, -cx])
        radial = np.array([c[0], c[1]]) - axis
        nrm = np.array(p.normal[:])
        if abs(nrm[2]) > 0.9:
            ok = (nrm[2] > 0) == (c[2] > CAP_Y[0] + 0.5 * (CAP_Y[1] - CAP_Y[0]))
        else:
            ok = float(np.dot(nrm[:2], radial)) > 0.0
        inward += 0 if ok else 1
    if inward:
        fails.append(f"{inward} post / cap faces point inward")
    if fails:
        raise RuntimeError(f"{obj.name}: contracts FAILED: " + "; ".join(fails))
    out = {"tris": tris, "verts": len(me.vertices), "faces": len(polys),
           "bounds_prefab_m": {"min": [round(float(v), 4) for v in lo],
                               "max": [round(float(v), 4) for v in hi]},
           "post_window_m": [round(float(worst[0]), 4), round(float(worst[1]), 4)],
           "net_twins": len(keyed)}
    print(f"[safety_fence] LOD{lod} contracts hold: {tris} tris, bounds {out['bounds_prefab_m']}, "
          f"post window {out['post_window_m']}, {len(keyed)} net twin pairs")
    return out


def post_contracts(obj, maps=None):
    fails = []
    P = _prefab_co(obj)
    lo, hi = P.min(axis=0), P.max(axis=0)
    tris = cs2_asset.tri_count(obj)
    if tris != POST_TRIS or tris > POST_CAP:
        fails.append(f"C5 post tris {tris} (expected {POST_TRIS}, cap {POST_CAP})")
    if abs(lo[1]) > 1e-6 or abs(hi[1] - CAP_Y[1]) > 1e-4:
        fails.append(f"C2 post y {lo[1]:.5f} .. {hi[1]:.5f} != 0 .. {CAP_Y[1]}")
    uvf = _uv_faces(obj, A_POST)
    bad = sum(1 for pts in uvf if (pts < -1e-6).any() or (pts > A_POST + 1e-6).any()
              or _signed_area(pts) <= 1e-9)
    if bad:
        fails.append(f"C7 {bad} post faces with bad UVs")
    if fails:
        raise RuntimeError(f"{obj.name}: contracts FAILED: " + "; ".join(fails))
    out = {"tris": tris, "verts": len(obj.data.vertices),
           "bounds_prefab_m": {"min": [round(float(v), 4) for v in lo],
                               "max": [round(float(v), 4) for v in hi]}}
    print(f"[safety_fence] {obj.name} contracts hold: {tris} tris, bounds {out['bounds_prefab_m']}")
    return out


def texture_contracts(maps, post_maps=None):
    """C8 / C9 on painted arrays (the test re-runs them on the written PNGs)."""
    fails = []
    base = maps["BaseColor"][0]
    ctl = maps["ControlMask"][0]
    msk = maps["MaskMap"][0]
    a = base[..., 3]
    ys, xs = _sl(R_NET)
    inside = np.zeros(a.shape, bool)
    inside[ys, xs] = True
    if int(np.count_nonzero((a < 1.0) & ~inside)):
        fails.append("C8 alpha < 255 outside the net region")
    mean = float(a[ys, xs].mean())
    if not 0.40 <= mean <= 0.50:
        fails.append(f"C8 net-region mean alpha {mean:.3f} outside 0.40 .. 0.50")
    h = R_NET[3] - R_NET[1]
    net_a = a[ys, xs]
    full = (net_a >= 1.0).all(axis=1)

    def leading(rows):
        return int(np.argmax(~rows)) if (~rows).any() else len(rows)

    top_rows, bot_rows = leading(full[::-1]), leading(full)
    if top_rows < 10 or bot_rows < 10:
        fails.append(f"C8 selvedge rows top {top_rows} / bottom {bot_rows} < 10")
    strand = net_a > 0.0
    strand_rgb = float(base[ys, xs, 0][strand].mean()) if strand.any() else 0.0
    holes = base[ys, xs, 0:3][~strand]
    hole_dev = float(np.abs(holes - strand_rgb).max()) if holes.size else 0.0
    if hole_dev > 0.02:
        fails.append(f"C8 alpha-0 texels' RGB deviate {hole_dev:.3f} > 0.02 from strand RGB {strand_rgb:.3f}")
    rec = (ctl[..., 1] >= 0.5) | (ctl[..., 2] >= 0.5)
    dev = float(np.abs(base[..., 0:3][rec] - RECOLOR).max())
    if dev > 0.05:
        fails.append(f"C8 recolor texels deviate {dev:.3f} > 0.05 from {RECOLOR}")
    if float(ctl[ys, xs, 1].min()) < 1.0:
        fails.append("C9 net region ControlMask G < 1")
    pys, pxs = _sl(R_POST_BAND)
    clip_cols = set()
    for yc in CLIP_Y:
        cx = _post_band_px(R_POST_BAND, POST_Y, yc)
        clip_cols.update(range(int(round(cx - CLIP_HALF_PX)), int(round(cx + CLIP_HALF_PX))))
    band_b = ctl[pys, pxs, 2]
    non_clip = np.array([c not in clip_cols for c in range(pxs.start, pxs.stop)])
    if float(band_b[:, non_clip].min()) < 1.0:
        fails.append("C9 post band ControlMask B < 1")
    cys, cxs = _sl(R_CAP)
    if float(ctl[cys, cxs, 0:3].max()) > 0.0:
        fails.append("C9 cap block ControlMask not 0")
    if float(ctl[..., 3].max()) > 0.0 or float((ctl[..., 0] + ctl[..., 1] + ctl[..., 2]).max()) > 1.0 + 1e-6:
        fails.append("C9 ControlMask A > 0 or R + G + B > 1")
    if float(msk[..., 2].max()) > 0.0:
        fails.append("C9 MaskMap B > 0")
    if post_maps is not None:
        pb, pc = post_maps["BaseColor"][0], post_maps["ControlMask"][0]
        if float(pb[..., 3].min()) < 1.0:
            fails.append("post BaseColor alpha < 255")
        if float(pc[..., 3].max()) > 0.0 or float(post_maps["MaskMap"][0][..., 2].max()) > 0.0:
            fails.append("post ControlMask A / MaskMap B not 0")
    if fails:
        raise RuntimeError("texture contracts FAILED: " + "; ".join(fails))
    out = {"net_alpha_mean": round(mean, 4), "selvedge_rows": {"top": top_rows, "bottom": bot_rows},
           "strand_rgb_mean": round(strand_rgb, 4), "hole_rgb_max_dev": round(hole_dev, 4),
           "net_alpha_zero_fraction": round(float((net_a <= 0.0).mean()), 4),
           "net_alpha_full_fraction": round(float((net_a >= 1.0).mean()), 4)}
    print(f"[safety_fence] texture contracts hold: {out}")
    return out


def bounds_record(obj):
    P = _prefab_co(obj)
    from cs2_uv import face_world_areas  # noqa: WPS433 (bpy-only module)
    return {"min": [round(float(v), 6) for v in P.min(axis=0)],
            "max": [round(float(v), 6) for v in P.max(axis=0)],
            "tris": cs2_asset.tri_count(obj), "verts": len(obj.data.vertices),
            "area_m2": round(float(face_world_areas(obj.data).sum()), 4)}


# --------------------------------------------------------------------------------------
# Runs (review pack + previews): copies of the tile bent along a path, posts kept plumb
# --------------------------------------------------------------------------------------

def path_point(s, radius, slope=0.0):
    """Arc in the net frame: starts at the origin heading -y (= prefab +z travel), turns toward
    +x. Returns (position, travel dir, lateral unit) as Vectors; z rises by slope * s."""
    if radius:
        th = s / radius
        pos = mathutils.Vector((radius * (1.0 - math.cos(th)), -radius * math.sin(th), slope * s))
        d = mathutils.Vector((math.sin(th), -math.cos(th), 0.0))
    else:
        pos = mathutils.Vector((0.0, -s, slope * s))
        d = mathutils.Vector((0.0, -1.0, 0.0))
    n = mathutils.Vector((-d.y, d.x, 0.0))
    return pos, d, n


def bend_copy(src, name, s0, radius, slope=0.0, offset=(0.0, 0.0, 0.0)):
    """A copy of the net-oriented tile `src` laid along the path from arc length s0 (vertex
    travel t = -y maps to s = s0 + t + 2). Plumb: up stays +z (the curve shader's frame on a
    slope is engine code, unverified - design R5)."""
    me = src.data.copy()
    me.name = name
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    out = np.empty_like(co)
    off = mathutils.Vector(offset)
    for i, (bx, by, bz) in enumerate(co):
        pos, _d, n = path_point(s0 + (-by) + TILE / 2.0, radius, slope)
        p = pos + n * bx + off
        out[i] = (p.x, p.y, p.z + bz)
    me.vertices.foreach_set("co", out.ravel())
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    return obj


def place_copy(src, name, s, radius, slope=0.0, offset=(0.0, 0.0, 0.0)):
    """A rigid copy of the post at arc length s, rotated to the path heading."""
    pos, d, _n = path_point(s, radius, slope)
    me = src.data.copy()
    me.name = name
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    yaw = math.atan2(d.y, d.x) - math.atan2(-1.0, 0.0)
    obj.matrix_world = (mathutils.Matrix.Translation(pos + mathutils.Vector(offset))
                        @ mathutils.Matrix.Rotation(yaw, 4, "Z"))
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def build_run(tile, n_tiles, name, radius, slope=0.0, offset=(0.0, 0.0, 0.0)):
    """n_tiles bent copies joined into one object (UVs and part tags kept), re-shaded."""
    copies = [bend_copy(tile, f"{name}_t{i}", TILE * i, radius, slope, offset) for i in range(n_tiles)]
    bpy.ops.object.select_all(action="DESELECT")
    for c in copies:
        c.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    if obj.data.has_custom_normals:            # the tile's normals were for the straight tile
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    cs2_asset.shade_by_angle(obj, 66.0, weighted=False)
    return obj


# --------------------------------------------------------------------------------------
# Review pack (cs2_review.write_pack + BaseColor alpha wired into the pack's materials)
# --------------------------------------------------------------------------------------

def _wire_alpha(mat):
    """Cutout look for a review / preview material: BaseColor alpha -> Principled Alpha, dithered
    (EEVEE's stochastic transparency ~ the game's hashed cutout), single-sided like the game."""
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    base = nt.nodes.get("BaseColor")
    if base is not None and getattr(base, "image", None) is not None:
        nt.links.new(base.outputs["Alpha"], bsdf.inputs["Alpha"])
    for attr, val in (("surface_render_method", "DITHERED"), ("use_backface_culling", True),
                      ("use_transparent_shadow", True)):
        if hasattr(mat, attr):
            try:
                setattr(mat, attr, val)
            except (TypeError, ValueError):
                pass
    return mat


def write_review(lod0, lod1, post, asset_dir, post_dir, args=None):
    """Two packs: SkiSafetyNetA (LOD0 / LOD1 + SUB: a 3-tile curved run 1.5 m to the side with
    SkiNetPostA at its dead end, so every node has a post) and SkiNetPostA."""
    import cs2_review
    run = build_run(lod0, 3, "SafetyNetRunThree", radius=20.0, offset=(1.5, 0.0, 0.0))
    end = place_copy(post, "SafetyNetRunEndPost", 3 * TILE, radius=20.0, offset=(1.5, 0.0, 0.0))
    orig = cs2_review.pbr_material

    def pbr_alpha(*a, **k):
        return _wire_alpha(orig(*a, **k))

    cs2_review.pbr_material = pbr_alpha
    try:
        meta = {"generator": "safety_fence.py", "args": list(args or []), "kind": "hanging (lane mesh)",
                "caps": {0: LOD0_CAP, 1: LOD1_CAP}, "letter": "A",
                "tint": {"G": cs2_asset.PAD_RGB, "B": cs2_asset.PAD_RGB}}
        net_pack = cs2_review.write_pack(
            NAME, asset_dir, {0: lod0, 1: lod1}, manifest={}, meta=meta,
            extra_meshes={"RunThree": (run, NAME, asset_dir),
                          "RunEndPost": (end, POST_NAME, post_dir)})
        post_pack = cs2_review.write_pack(
            POST_NAME, post_dir, {0: post}, manifest={},
            meta={"generator": "safety_fence.py", "args": list(args or []), "kind": "prop",
                  "caps": {0: POST_CAP}, "letter": "A", "tint": {"B": cs2_asset.PAD_RGB}})
    finally:
        cs2_review.pbr_material = orig
        for o in (run, end):
            me = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if me.users == 0:
                bpy.data.meshes.remove(me)
    return net_pack, post_pack


# --------------------------------------------------------------------------------------
# Previews: 2 / 10 / 50 m x front / oblique / backlit on a snow plane
# --------------------------------------------------------------------------------------

def _tinted_image(name, asset_dir, size, tint=None):
    """BaseColor x the default VariationSet (slot 2 / 3 = PAD_RGB unless `tint`, multiplied in
    linear space, cs2_preview convention), ALPHA KEPT (cs2_preview's tinted image forces alpha 1)."""
    import cs2_preview
    tint = tuple(tint or cs2_asset.PAD_RGB)
    base = cs2_preview._load_rgba(os.path.join(asset_dir, f"{name}_BaseColor.png"))
    ctl = cs2_preview._load_rgba(os.path.join(asset_dir, f"{name}_ControlMask.png"), is_data=True)
    lin = cs2_preview._srgb_to_linear(base[..., :3])
    for ch, rgb in ((1, tint), (2, tint)):
        m = np.clip(ctl[..., ch], 0.0, 1.0)[..., None]
        lin = lin * ((1.0 - m) + np.asarray(rgb, np.float32)[None, None, :] * m)
    out = np.empty_like(base)
    out[..., :3] = cs2_preview._linear_to_srgb(lin)
    out[..., 3] = base[..., 3]
    img = bpy.data.images.new(f"{name}_PreviewTinted", size, size, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.alpha_mode = "STRAIGHT"
    img.pixels.foreach_set(np.ascontiguousarray(out).ravel())
    return img


def _preview_material(name, asset_dir, size, tint=None):
    mat = bpy.data.materials.new(f"{name}_PreviewCutout")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.name = tex.label = "BaseColor"
    tex.image = _tinted_image(name, asset_dir, size, tint)
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    msk = bpy.data.images.load(os.path.join(asset_dir, f"{name}_MaskMap.png"))
    msk.colorspace_settings.name = "Non-Color"
    msk.alpha_mode = "CHANNEL_PACKED"
    mt = nt.nodes.new("ShaderNodeTexImage")
    mt.image = msk
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(mt.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
    return _wire_alpha(mat)


def _sky(elevation_deg, rotation_deg, sun_energy=4.0):
    """cs2_preview's Nishita sky + matched SUN lamp, with a free sky rotation."""
    import cs2_preview
    sc = bpy.context.scene
    world = bpy.data.worlds.new("FencePreviewSky")
    world.use_nodes = True
    nt = world.node_tree
    sky = nt.nodes.new("ShaderNodeTexSky")
    kinds = {i.identifier for i in sky.bl_rna.properties["sky_type"].enum_items}
    sky.sky_type = "MULTIPLE_SCATTERING" if "MULTIPLE_SCATTERING" in kinds else "NISHITA"
    sky.sun_disc = False
    sky.sun_elevation = math.radians(elevation_deg)
    sky.sun_rotation = math.radians(rotation_deg)
    sky.altitude = cs2_preview.SKY_ALTITUDE_M
    bg = nt.nodes["Background"]
    bg.inputs[1].default_value = cs2_preview.SKY_STRENGTH
    nt.links.new(sky.outputs[0], bg.inputs[0])
    sc.world = world
    ld = bpy.data.lights.new("FencePreviewSun", "SUN")
    ld.energy = sun_energy
    ld.angle = math.radians(0.53)
    sun = bpy.data.objects.new("FencePreviewSun", ld)
    sc.collection.objects.link(sun)
    r, e = math.radians(rotation_deg), math.radians(elevation_deg)
    d = mathutils.Vector((-math.cos(r) * math.cos(e), -math.sin(r) * math.cos(e), math.sin(e)))
    sun.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    return world, sun, d


def _sky_rotation_for(direction_xy):
    """Sky rotation whose sun azimuth points along `direction_xy` (inverse of _sky's formula)."""
    return math.degrees(math.atan2(-direction_xy[1], -direction_xy[0]))


def render_previews(out_dir, lod0, lod1, post, asset_dir, post_dir, resolution=(1200, 800),
                    samples=64):
    """Nine frames (+ an LOD1 frame at 50 m and a contact sheet) into out_dir. A 16-tile curved
    run (64 m) with end posts on a 400 m snow plane; the camera aims at bay mid-span 0.7 m up."""
    import cs2_preview
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    hidden = {o.name: o.hide_render for o in sc.objects}
    for o in sc.objects:
        o.hide_render = True
    made_objs, made_mats = [], []
    radius = 80.0
    n_tiles = 16
    run0 = build_run(lod0, n_tiles, "FencePreviewRunL0", radius)
    run1 = build_run(lod1, n_tiles, "FencePreviewRunL1", radius)
    ends = [place_copy(post, "FencePreviewPostA", 0.0, radius),
            place_copy(post, "FencePreviewPostB", n_tiles * TILE, radius)]
    made_objs += [run0, run1] + ends
    net_mat = _preview_material(NAME, asset_dir, A)
    post_mat = _preview_material(POST_NAME, post_dir, A_POST)
    alt_net = _preview_material(NAME, asset_dir, A, ALT_TINT)
    alt_post = _preview_material(POST_NAME, post_dir, A_POST, ALT_TINT)
    made_mats += [net_mat, post_mat, alt_net, alt_post]
    for o in (run0, run1):
        o.data.materials.clear()
        o.data.materials.append(net_mat)
    for o in ends:
        o.data.materials.clear()
        o.data.materials.append(post_mat)
    bpy.ops.mesh.primitive_plane_add(size=400.0, location=(0.0, -32.0, 0.0))
    ground = bpy.context.active_object
    ground.name = "FencePreviewSnow"
    gm = bpy.data.materials.new("FencePreviewSnow")
    gm.use_nodes = True
    gb = next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    gb.inputs["Base Color"].default_value = (0.85, 0.87, 0.90, 1.0)
    gb.inputs["Roughness"].default_value = 0.85
    ground.data.materials.append(gm)
    made_objs.append(ground)
    made_mats.append(gm)
    cam_data = bpy.data.cameras.new("FencePreviewCam")
    cam_data.sensor_fit = "VERTICAL"
    cam_data.sensor_height = 24.0
    cam_data.lens = 12.0 / math.tan(math.radians(cs2_preview.VERTICAL_FOV_DEG) / 2.0)
    cam_data.clip_end = 5000.0
    cam = bpy.data.objects.new("FencePreviewCam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    made_objs.append(cam)
    sc.render.resolution_x, sc.render.resolution_y = resolution
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGB"
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = "None"
    for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            sc.render.engine = eng
            break
        except TypeError:
            continue
    sc.eevee.taa_render_samples = samples
    s_mid = 8 * TILE + 1.0                       # mid-span of bay 0 of tile 8
    pos, d, n = path_point(s_mid, radius)
    target = pos + mathutils.Vector((0.0, 0.0, 0.7))
    elev = {2.0: 8.0, 10.0: 12.0, 50.0: 18.0}
    written = []
    # (view, distance, lod, tint tag): the 3 x 3 grid, LOD1 at 50 m, and the ALT_TINT A/B pair
    frames = [(v, dist, 0, None) for v in PREVIEW_VIEWS for dist in PREVIEW_DISTANCES]
    frames += [("oblique", 50.0, 1, None), ("oblique", 10.0, 0, "tintB"), ("oblique", 50.0, 0, "tintB")]
    obl = n * math.cos(math.radians(35)) + d * math.sin(math.radians(35))
    for view, dist, lod, tint_tag in frames:
        horiz = {"front": n.copy(), "oblique": obl.copy(), "backlit": -n}[view]
        horiz.normalize()
        e = math.radians(elev[dist])
        cdir = mathutils.Vector((horiz.x * math.cos(e), horiz.y * math.cos(e), math.sin(e)))
        cam.location = target + cdir * dist
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        if view == "backlit":
            sun_xy, sun_el = n, 18.0                          # sun behind the fence, low
        else:
            sun_xy = horiz * math.cos(math.radians(40)) + d * math.sin(math.radians(40))
            sun_el = 40.0
        world, sun, _sd = _sky(sun_el, _sky_rotation_for((sun_xy.x, sun_xy.y)))
        run0.hide_render = lod != 0
        run1.hide_render = lod != 1
        for o in ends + [ground]:
            o.hide_render = False
        nm, pm = (alt_net, alt_post) if tint_tag else (net_mat, post_mat)
        for o in (run0, run1):
            o.data.materials[0] = nm
        for o in ends:
            o.data.materials[0] = pm
        tag = view if lod == 0 else f"lod{lod}_{view}"
        if tint_tag:
            tag += f"_{tint_tag}"
        path = os.path.join(out_dir, f"{NAME}_{int(dist)}m_{tag}.png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
        print(f"[safety_fence] preview {os.path.basename(path)}")
        ld = sun.data
        bpy.data.objects.remove(sun, do_unlink=True)
        bpy.data.lights.remove(ld)
        sc.world = None
        bpy.data.worlds.remove(world)
    sheet = _contact_sheet(written[:9], os.path.join(out_dir, f"{NAME}_sheet.png"))
    written.append(sheet)
    # clean up everything the rig made
    for o in made_objs:
        data = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if data is not None and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Camera):
                bpy.data.cameras.remove(data)
    for m in made_mats:
        if m.users == 0:
            bpy.data.materials.remove(m)
    for o in sc.objects:
        if o.name in hidden:
            o.hide_render = hidden[o.name]
    return written


def _contact_sheet(paths, out_path, cell=(600, 400)):
    """3 x 3 grid (rows 2 / 10 / 50 m, columns front / oblique / backlit), nearest downscale."""
    import cs2_preview
    tiles = {}
    for p in paths:
        img = cs2_preview._load_rgba(p)
        h, w = img.shape[:2]
        ys = (np.arange(cell[1]) * h // cell[1])
        xs = (np.arange(cell[0]) * w // cell[0])
        tiles[os.path.basename(p)] = img[ys][:, xs]
    pad = 4
    W = 3 * cell[0] + 2 * pad
    H = 3 * cell[1] + 2 * pad
    sheet = np.full((H, W, 4), 0.5, np.float32)
    sheet[..., 3] = 1.0
    for r, dist in enumerate(PREVIEW_DISTANCES):
        for c, view in enumerate(PREVIEW_VIEWS):
            key = f"{NAME}_{int(dist)}m_{view}.png"
            if key not in tiles:
                continue
            y0 = H - (r + 1) * cell[1] - r * pad          # row 0 (2 m) at the TOP (bottom-up buffer)
            x0 = c * (cell[0] + pad)
            sheet[y0:y0 + cell[1], x0:x0 + cell[0]] = tiles[key]
    cs2_preview._write_rgba(out_path, sheet)
    return out_path


# --------------------------------------------------------------------------------------
# Export
# --------------------------------------------------------------------------------------

def _clean_asset_dir(asset_dir, name):
    for f in os.listdir(asset_dir):
        p = os.path.join(asset_dir, f)
        if os.path.isfile(p) and (f.startswith(name) or f == "settings.json"):
            os.remove(p)


def export(out_root, preview_dir=None, review=False, stage=False, args=None):
    """Build, check, write both asset folders (+ previews / review packs / staging). Returns
    (asset_dir, post_dir, objects dict, manifest data)."""
    out_root = os.path.abspath(out_root)
    asset_dir = os.path.join(out_root, NAME)
    post_dir = os.path.join(out_root, POST_NAME)
    for d, nm in ((asset_dir, NAME), (post_dir, POST_NAME)):
        os.makedirs(d, exist_ok=True)
        _clean_asset_dir(d, nm)
    maps = paint_net_maps()
    post_maps = paint_post_maps()
    tex = texture_contracts(maps, post_maps)
    lod0 = build_tile(NAME, 0)
    lod1 = build_tile(f"{NAME}_LOD1", 1)
    post = build_post(POST_NAME)
    c0 = tile_contracts(lod0, 0, maps)
    c1 = tile_contracts(lod1, 1, maps)
    cp = post_contracts(post, post_maps)
    write_maps(asset_dir, NAME, maps, A)
    write_maps(post_dir, POST_NAME, post_maps, A_POST)
    cs2_asset.write_settings(asset_dir, NAME, supplied_lods=(1,), surface=SURFACE)
    cs2_asset.write_settings(post_dir, POST_NAME)
    cs2_asset.export_fbx(lod0, os.path.join(asset_dir, f"{NAME}.fbx"))
    cs2_asset.export_fbx(lod1, os.path.join(asset_dir, f"{NAME}_LOD1.fbx"))
    cs2_asset.export_fbx(post, os.path.join(post_dir, f"{POST_NAME}.fbx"))
    results = {}
    for d, nm, spec, objs, kind in ((asset_dir, NAME, VALIDATE_SPEC, (lod0, lod1), "hanging"),
                                    (post_dir, POST_NAME, VALIDATE_SPEC_POST, (post,), "prop")):
        findings, summary = cs2_validate.validate_asset_folder_ex(d, nm, spec)
        for o in objs:
            findings += cs2_validate.validate_mesh(o, kind=kind)
        errors = [f for f in findings if f[0] == "ERROR"]
        warns = [f for f in findings if f[0] == "WARN"]
        if errors:
            raise RuntimeError(f"{nm}: validation errors: {errors}")
        results[nm] = {"errors": 0, "warnings": len(warns), "tris": summary.get("tris"),
                       "tex": sorted(summary.get("tex") or []),
                       "warn_list": [f"{c}: {m}" for _l, c, m in warns]}
    import cs2_preview
    lb = bounds_record(lod0)
    size = [lb["max"][i] - lb["min"][i] for i in range(3)]
    sw = cs2_preview.lod_switch_table(size, [LOD0_TRIS, LOD1_TRIS], area=lb["area_m2"])
    staged = STG.stage() if stage else None
    data = {
        "generator": "assets/blender/safety_fence.py",
        "args": list(args or []),
        "design": "docs/research/art/assets-2026-10/design/safety-fence.md",
        "kind": "hanging (lane mesh tile; cs2_validate has no lane kind: no ground rule)",
        "lane_mesh": True,
        "mesh_prefabs": [f"{NAME} Mesh", f"{NAME}_LOD1 Mesh"],
        "material_template": SURFACE["materialTemplate"],
        "surface_settings": SURFACE,
        "tiling_count": STG.TILING_COUNT, "override_length_m": STG.OVERRIDE_LENGTH,
        "tile_length_m": TILE, "bays": 2, "column_step_m": TILE / (2 * COLS_PER_BAY),
        "tris": {"LOD0": c0["tris"], "LOD1": c1["tris"]},
        "caps": {"LOD0": LOD0_CAP, "LOD1": LOD1_CAP},
        "texture_px": A, "maps": ["BaseColor", "Normal", "MaskMap", "ControlMask"],
        "alpha": {"mean_net_region": tex["net_alpha_mean"], "cell_px": CELL_PX,
                  "cell_m": round(CELL_PX / 512.0, 4), "strand_half_width_px": STRAND_HALF_PX,
                  "selvedge_px": SELVEDGE_PX, **{k: v for k, v in tex.items() if k != "net_alpha_mean"}},
        "texel_density_px_per_m": {"net_across": 512, "net_up_post_column": round(704 / (TOP - HEM), 1),
                                   "net_up_mid_bay": round(704 / (TOP - SAG - HEM), 1),
                                   "post_along": round((R_POST_BAND[2] - R_POST_BAND[0]) / (POST_Y[1] - POST_Y[0]), 1)},
        "atlas_px": {"net": R_NET, "post_band": R_POST_BAND, "cap": R_CAP, "clip_y_m": CLIP_Y},
        "geometry": {"post_circumradius_m": POST_R, "cap_circumradius_m": CAP_R, "post_y": POST_Y,
                     "cap_y": CAP_Y, "post_centre_after_bay_start_m": POST_AT, "net_x": NET_LAT,
                     "bulge_m": BULGE, "hem_y": HEM, "top_y": TOP, "sag_m": SAG, "lod1_top_y": LOD1_TOP,
                     "lod1_post_sides": LOD1_POST_SIDES},
        "prefab_bounds": {"LOD0": lb, "LOD1": bounds_record(lod1)},
        "contracts": {"LOD0": c0, "LOD1": c1},
        "lod_switch_formula": {"switch_m": {k: round(v, 1) for k, v in sw["switch"].items()},
                               "cull_m": round(sw["cull"], 1), "bias": round(sw["bias"], 3),
                               "note": "cs2_preview.lod_switch_table (object formula, lodFactor 652 "
                                       "unverified); lanes may select LODs differently: unverified"},
        "settings_json": {"LOD1": "Skip", "sharedAssets": "LOD1 -> LOD0 maps", "Surface": SURFACE},
        "validation": results,
        "staged_prefabs": None if staged is None else {n: r["cid"] for n, r in staged["prefabs"].items()},
        "post_asset": POST_NAME,
    }
    post_data = {
        "generator": "assets/blender/safety_fence.py", "args": list(args or []),
        "kind": "prop (UtilityLane.m_NodeObject end post; optional)",
        "object_prefab": STG.POST, "mesh_prefab": f"{POST_NAME} Mesh",
        "tris": {"LOD0": cp["tris"]}, "lod0_cap": POST_CAP, "texture_px": A_POST,
        "prefab_bounds": {"LOD0": bounds_record(post)}, "contracts": cp,
        "settings_json": {}, "validation": results[POST_NAME],
        "atlas_px": {"post_band": R_POST_BAND_P, "cap": R_CAP_P},
    }
    if preview_dir:
        data["previews"] = [os.path.relpath(p, REPO).replace("\\", "/") for p in
                            render_previews(os.path.abspath(preview_dir), lod0, lod1, post,
                                            asset_dir, post_dir)]
    if review:
        net_pack, post_pack = write_review(lod0, lod1, post, asset_dir, post_dir, args)
        data["review_pack"] = os.path.relpath(net_pack, REPO).replace("\\", "/")
        post_data["review_pack"] = os.path.relpath(post_pack, REPO).replace("\\", "/")
    cs2_validate.write_manifest(asset_dir, NAME, data)
    cs2_validate.write_manifest(post_dir, POST_NAME, post_data)
    print(f"[safety_fence] exported {asset_dir} (LOD0 {c0['tris']} / LOD1 {c1['tris']} tris, {A} px, "
          f"alpha mean {tex['net_alpha_mean']}) and {post_dir} ({cp['tris']} tris, {A_POST} px); "
          f"validation 0 errors / {results[NAME]['warnings'] + results[POST_NAME]['warnings']} warnings")
    return asset_dir, post_dir, {"lod0": lod0, "lod1": lod1, "post": post}, data


# --------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------

def parse_args(argv):
    ap = argparse.ArgumentParser(prog="safety_fence.py", description="orange safety-net fence")
    ap.add_argument("--out", default=None, help="letter folder, e.g. ../import/fences/A")
    ap.add_argument("--export", action="store_true", help="write both asset folders (needs --out)")
    ap.add_argument("--preview", default=None, help="preview folder (2 / 10 / 50 m frames)")
    ap.add_argument("--review", action="store_true",
                    help="write assets/preview/review/SkiSafetyNetA.blend + SkiNetPostA.blend")
    ap.add_argument("--stage", action="store_true",
                    help="also stage the prefab JSON (safety_fence_stage.stage)")
    return ap.parse_args(argv)


def main(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parse_args(argv)
    clear_scene()
    if args.export:
        if not args.out:
            raise SystemExit("--export needs --out")
        export(args.out, preview_dir=args.preview, review=args.review, stage=args.stage, args=argv)
        return
    maps = paint_net_maps()
    texture_contracts(maps, paint_post_maps())
    lod0 = build_tile(NAME, 0)
    tile_contracts(lod0, 0, maps)
    tile_contracts(build_tile(f"{NAME}_LOD1", 1), 1, maps)
    post_contracts(build_post(POST_NAME))
    print("[safety_fence] built (no export; pass --out <letter dir> --export)")


if __name__ == "__main__":
    main()
