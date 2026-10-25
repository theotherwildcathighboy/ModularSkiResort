"""
fine_probes.py - PROBES B for import session S1 (fine-detail pass, design section F as cut
down by the skeptic: F1 + F4 + the FS7 lodge rack row; FS2 / FS8 / FS9 applied).

Design: docs/research/art/assets-2026-10/design/fine-detail-pass.md section F (+ C.3, C.6) and
design/fine-detail-pass-skeptic.md FS7 / FS8 / FS9 / FS12 / FS15 (the skeptic wins).
Build report: docs/research/art/assets-2026-10/build/probes-b.md.
S1 steps, surgery JSON and outcome table: docs/research/art/assets-2026-10/surgery/probes-B.md
(+ surgery/probes-B.data/).

    cd assets/blender
    blender --background --factory-startup --python-exit-code 1 --python fine_probes.py -- \
        --out ../import/probes/B --export [--preview ../preview/probes/B]
    blender ... --python fine_probes.py -- --measure [--json <path>]
        re-measures the two sign-card wall spots on the CURRENT SkiDQBaseStationA FBX and the four
        ground spots in front of the deployed SkiAFrameDayLodge FBX; exit 1 if any spot is not OK.
        Run it again before the import if the DQ base was re-exported (the sign set is authored in
        the host's own frame: a moved wall means a re-export of SkiProbeSignSetB first).

THE FIVE PROBE ASSETS (assets/import/probes/B/<Name>/; letter B, PIPELINE.md:21 / FS15)
  F1  cross-asset sharedAssets: does a sharer get the owner's texture or a copy, within one import
      batch and across batches (FS8)?
      SkiProbeSheetB      OWNER: a 1.0 x 1.0 m panel + a full 2048^2 four-map set (the sheet).
                          Tiles: A lift boards (UNLOAD 900 / 400 px/m, LIFT OPS 450 px/m),
                          B SKI SCHOOL vector 300 px/m above 5x7 71 px/m, C "USER B", D "LATE B".
      SkiProbeSheetUserB  SHARER, import batch 1 with the owner: a 2.4 x 1.2 m two-post billboard;
                          front = tile B at true density (vector over 5x7), back = tile C.
      SkiProbeSheetLateB  SHARER, import batch 2 ALONE: a 0.9 m A-frame sandwich board; front =
                          tile D, back = tile A. No own PNGs; settings.json sharedAssets ->
                          ../SkiProbeSheetB/SkiProbeSheetB_<map>.png (4 keys).
      Three different shapes, three different UV placements on one sheet: whichever texture each
      .Surface binds is readable at a glance (and by GUID: tools/probe-surfaces.ps1).
  F4  SkiProbeSignSetB    an identity-row sign-set sub-object on SkiDQBaseStationBuilding: two
                          cards authored in SkiDQBaseStationA's game-local frame, on walls measured
                          on the shipped FBX: card A "PROBE B" (1.20 x 0.40, lit lettering,
                          EmissiveProperties intensity 0.2 at surgery) on the engine-room wall
                          x = 5.600 beside LIFT OPS; card B a 1.000 m ruler (10 x 0.100 m bands)
                          on the -x fascia x = -4.500 (the known 1 m feature for the min-zoom
                          shot). Bounds ~10.1 x 2.4 x 6.1 m. Own 512^2 five-map set.
  FS7 SkiProbeRackB       a free-standing ski-rack proxy whose skis are ControlMask G (slot 2);
                          surgery gives its mesh ColorProperties m_ExternalColorSource = Parent with
                          channel 1 external. Hosted on the deployed lodge SkiAFrameDayLodge (a
                          ServiceUpgrade of SkiResortGate): orange skis = Parent resolved to the
                          lodge, green = its own fallback (no source).

FRAMES (measured 2026-10-12, see build/probes-b.md section 2)
    FBX-raw meters = what export_fbx writes: (x_B, z_B, -y_B) of the Blender authoring frame, y up.
    THE GAME NEGATES X ON IMPORT: the deployed mesh prefabs' m_Bounds read x in -(raw x range) with
    z and y unchanged (SkiLiftBaseStationK raw x -3.80..6.10 -> m_Bounds -6.10..3.80; SkiAFrameDayLodge
    raw -22.12..22.75 -> -22.75..22.12; both FBX older than their import, untouched since). So the
    in-game prefab frame is (-x_B, z_B, -y_B): the same physical object (Unity is left-handed), not a
    mirror. Consequences here: (1) the sign set is authored in the HOST's raw frame and placed by
    the identity row (0,0,0) / (0,0,0,1), m_ParentMesh 0, so host and set flip together and every
    card lands where it was measured; (2) the lodge rows' m_Position are IN-GAME values, so they are
    converted to raw (x negated) before ray-casting the lodge FBX and for the previews.

DETERMINISM
    Explicit vertex lists, hand-laid texel UVs, no RNG, Bfont text through cs2_liftdress._text_cov
    (read-only), cs2_asset.tag_parts canonical order. Two builds hash identically; two exports write
    byte-identical PNGs and settings.json (tests/test_fine_probes.py). The FBX carries the clock.

Read-only users of shared code: cs2_asset (export_fbx, tag_parts, shade_by_angle, _write_png_raw,
tri_count), cs2_validate, cs2_liftdress (paint_board, _text_cov), ski_launcher_glyph.FONT,
cs2_preview (preview rig). Nothing shared is edited. No top-level side effects.
"""

import argparse
import hashlib
import json
import math
import os
import shutil
import sys
import tempfile

import bmesh
import bpy
import mathutils
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import cs2_asset     # noqa: E402
import cs2_validate  # noqa: E402

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
DATA_DIR = os.path.join(REPO, "docs", "research", "art", "assets-2026-10", "surgery", "probes-B.data")

# --------------------------------------------------------------------------------------
# Names, prefabs, batches
# --------------------------------------------------------------------------------------

OWNER = "SkiProbeSheetB"
USER = "SkiProbeSheetUserB"
LATE = "SkiProbeSheetLateB"
SIGNSET = "SkiProbeSignSetB"
RACK = "SkiProbeRackB"
ALL = (OWNER, USER, LATE, SIGNSET, RACK)
SHARERS = (USER, LATE)
OBJECT_PREFAB = {OWNER: "SkiProbeSheet", USER: "SkiProbeSheetUser", LATE: "SkiProbeSheetLate",
                 SIGNSET: "SkiProbeSignSet", RACK: "SkiProbeRack"}
BATCH = {OWNER: 1, USER: 1, SIGNSET: 1, RACK: 1, LATE: 2}
SHARE_KEY_ORDER = ("BaseColor", "ControlMask", "MaskMap", "Normal")   # cs2_asset.write_settings order
MAPS4 = ("BaseColor", "Normal", "MaskMap", "ControlMask")
LOD0_CAP = {OWNER: 20, USER: 60, LATE: 20, SIGNSET: 40, RACK: 200}

# --------------------------------------------------------------------------------------
# Hosts (read-only inputs)
# --------------------------------------------------------------------------------------

DQ_NAME = "SkiDQBaseStationA"
DQ_DIR = os.path.join(REPO, "assets", "import", "stations", "detachable", "A", DQ_NAME)
DQ_FBX = os.path.join(DQ_DIR, DQ_NAME + ".fbx")
DQ_PACK = os.path.join(REPO, "assets", "preview", "review", DQ_NAME + ".blend")
LODGE_NAME = "SkiAFrameDayLodge"
LODGE_DIR = os.path.join(REPO, "assets", "import", "lodges", "A", LODGE_NAME)
LODGE_FBX = os.path.join(LODGE_DIR, LODGE_NAME + ".fbx")
# the deployed mesh prefab's m_Bounds (ImportedData c030ee3ff256df91/SkiAFrameDayLodge, min y clamped)
LODGE_BOUNDS_DEPLOYED = ((-22.75, -0.01, -18.4), (22.12, 17.86, 14.2))
LODGE_LOT_HALF = (24.0, 20.0)       # m_LotWidth 6 x m_LotDepth 5 cells of 8 m

# --------------------------------------------------------------------------------------
# Sign-set cards (game frame; walls measured on the shipped DQ base FBX)
#   axis 0 = wall normal along prefab x; outward -1 = the wall faces -x
# --------------------------------------------------------------------------------------

CARD_T = 0.020                      # = board_ops thickness (0.020): card front flush with LIFT OPS front
CARDS = (
    dict(key="card_a_lettered", label="PROBE B (lit)", wall_x=5.600, outward=-1.0,
         y=2.525, z=4.500, w=1.20, h=0.40, part_hint="engroom_walls",
         note="engine-room wall, same plane as board_ops (cab wall x 5.600, its center z 7.500, "
              "y 2.525); 3.0 m along the wall (1.6 m lands on the 45-deg corner at z 6.0)"),
    dict(key="card_b_ruler", label="1.000 m ruler", wall_x=-4.500, outward=-1.0,
         y=4.600, z=9.500, w=1.000, h=0.200, part_hint="band_n_2",
         note="-x fascia band (y 3.9-6.15 vertical at x -4.500), the station's outside face; the "
              "1 m feature for the minimum-zoom screenshot"),
)
WALL_TOL = 0.003                    # every footprint ray hits the wall within +-3 mm of its median
NORMAL_TOL_DEG = 2.0
CLEAR_M = 2.0                       # nothing in front of a card within 2 m (first hit = the wall)

# --------------------------------------------------------------------------------------
# Lodge rows (IN-GAME prefab frame = the m_Position values; ground; facing +z = the road side)
# FS7 rack + F1 visual. raw_of() converts to the FBX-raw frame (x negated) for casts / previews.
# --------------------------------------------------------------------------------------

LODGE_ROWS = (
    (OWNER, (-8.00, 0.0, 17.00), (1.00, 0.04)),     # (asset, position, footprint x, z)
    (USER, (-4.50, 0.0, 17.00), (2.40, 0.20)),
    (LATE, (-1.00, 0.0, 17.00), (0.90, 0.70)),
    (RACK, (3.00, 0.0, 17.00), (1.25, 0.60)),
)


def raw_of(p_game):
    """In-game prefab (x, y, z) -> FBX-raw (x negated): the game negates x on import."""
    return (-p_game[0], p_game[1], p_game[2])

# --------------------------------------------------------------------------------------
# Sheet (owner) layout: 2048^2, texel rects (x0, y0, x1, y1), y up = UV v
# --------------------------------------------------------------------------------------

S = 2048
FULL = (0, 0, S, S)
GREY = (0, 0, 64, 64)                               # edges / posts / sides (inside tile C's corner)
TILE_A = (0, 1024, 1024, 2048)                      # lift boards
TILE_B = (1024, 1024, 2048, 2048)                   # building-sign densities
TILE_C = (0, 0, 1024, 1024)                         # USER B
TILE_D = (1024, 0, 2048, 1024)                      # LATE B
R_UNLOAD900 = (40, 1500, 670, 1995)                 # 0.70 x 0.55 m at 900 px/m = 630 x 495
R_UNLOAD400 = (700, 1500, 980, 1720)                # 0.70 x 0.55 m at 400 px/m (today's boards)
R_OPS450 = (40, 1300, 580, 1458)                    # 1.20 x 0.35 m at 450 px/m = 540 x 158
R_SCHOOL_VEC = (1100, 1800, 1820, 1980)             # 2.40 x 0.60 m at 300 px/m = 720 x 180
R_SCHOOL_5X7 = (1100, 1700, 1270, 1743)             # 2.40 x 0.60 m at ~71 px/m = 170 x 43
SCHOOL_BOARD_M = (2.40, 0.60)

# Sign set: 512^2
SA = 512
R_CARD_A = (16, 336, 496, 496)                      # 1.20 x 0.40 m at 400 px/m
R_CARD_B = (16, 224, 416, 304)                      # 1.000 x 0.200 m at 400 px/m
R_EDGE = (16, 16, 80, 80)
CARD_PPM = 400.0
LETTER_CAP_M = 0.20
EMISSIVE_SRGB = (1.00, 0.90, 0.70)                  # warm white lettering (emitted color)
EMISSIVE_INTENSITY_SURGERY = 0.2                    # vanilla shop sign (design C.2)

# Rack: 256^2
RA = 256
R_GALV = (0, 0, 256, 128)
R_ACCENT = (0, 128, 256, 256)
RACK_OWN_SLOT2 = (0.15, 0.75, 0.20)                 # fallback color: GREEN = Parent not resolved
LODGE_SLOT2_DEFAULT = (0.85, 0.25, 0.10)            # deployed lodge mesh VariationSet channel 1

INSET = 0.5

# --------------------------------------------------------------------------------------
# Small helpers
# --------------------------------------------------------------------------------------


def clear_scene():
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.cameras,
                 bpy.data.lights, bpy.data.curves):
        for item in list(coll):
            if item.users == 0:
                coll.remove(item)


def g2b(p):
    """Game / prefab (x, y up, z) -> Blender authoring (x, -z, y)."""
    return mathutils.Vector((p[0], -p[2], p[1]))


def b2g(v):
    return (float(v[0]), float(v[2]), float(-v[1]))


def _uvr(rect, fu, fv):
    x0, y0, x1, y1 = rect
    return (x0 + INSET + fu * (x1 - x0 - 2.0 * INSET), y0 + INSET + fv * (y1 - y0 - 2.0 * INSET))


class _MeshBuf:
    """Collects faces (Blender frame) with per-corner texel UVs for one part."""

    def __init__(self, name, A):
        self.name, self.A = name, A
        self.verts, self.faces, self.uvs = [], [], []

    def quad(self, c, n, up, hw, hh, rect):
        """Planar quad centered on c, outward normal n, 'up' in-plane, half sizes; UV rect
        laid upright as seen from outside (never mirrored)."""
        c, n, up = (mathutils.Vector(v) for v in (c, n, up))
        right = (-n).cross(up)
        corners = [c - right * hw - up * hh, c + right * hw - up * hh,
                   c + right * hw + up * hh, c - right * hw + up * hh]
        base = len(self.verts)
        self.verts.extend(tuple(p) for p in corners)
        self.faces.append((base, base + 1, base + 2, base + 3))
        self.uvs.append([_uvr(rect, 0, 0), _uvr(rect, 1, 0), _uvr(rect, 1, 1), _uvr(rect, 0, 1)])

    def tri(self, pts, rect):
        base = len(self.verts)
        self.verts.extend(tuple(p) for p in pts)
        self.faces.append((base, base + 1, base + 2))
        self.uvs.append([_uvr(rect, 0, 0), _uvr(rect, 1, 0), _uvr(rect, 0.5, 1)])

    def box(self, c, size, rects, skip=()):
        """Axis-aligned box (Blender frame) centered c, size (sx, sy, sz); rects: face -> rect
        ('px','nx','py','ny','pz','nz', or 'all'); skip: faces to omit (e.g. 'nz' bottom)."""
        c = mathutils.Vector(c)
        sx, sy, sz = (s / 2.0 for s in size)
        Z, Y = (0, 0, 1), (0, 1, 0)
        faces = {"px": ((1, 0, 0), Z, sy, sz, sx), "nx": ((-1, 0, 0), Z, sy, sz, sx),
                 "py": ((0, 1, 0), Z, sx, sz, sy), "ny": ((0, -1, 0), Z, sx, sz, sy),
                 "pz": ((0, 0, 1), Y, sx, sy, sz), "nz": ((0, 0, -1), (0, -1, 0), sx, sy, sz)}
        for key in ("px", "nx", "py", "ny", "pz", "nz"):
            if key in skip:
                continue
            n, up, hw, hh, off = faces[key]
            rect = rects.get(key, rects.get("all"))
            self.quad(c + mathutils.Vector(n) * off, n, up, hw, hh, rect)

    def oriented_box(self, c, axis_u, axis_n, size_w, size_l, size_t, rect):
        """Box along unit axes: u (length), n (thickness), w = n x u ... every face -> rect."""
        u = mathutils.Vector(axis_u).normalized()
        n = mathutils.Vector(axis_n).normalized()
        w = n.cross(u)
        c = mathutils.Vector(c)
        hl, ht, hw = size_l / 2.0, size_t / 2.0, size_w / 2.0
        self.quad(c + n * ht, n, u, hw, hl, rect)          # front
        self.quad(c - n * ht, -n, u, hw, hl, rect)         # back
        self.quad(c + w * hw, w, u, ht, hl, rect)          # sides
        self.quad(c - w * hw, -w, u, ht, hl, rect)
        self.quad(c + u * hl, u, n, hw, ht, rect)          # ends
        self.quad(c - u * hl, -u, n, hw, ht, rect)

    def to_object(self):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata(self.verts, [], self.faces)
        me.update()
        layer = me.uv_layers.new(name="UVMap")
        for poly, fuv in zip(me.polygons, self.uvs):
            for k, li in enumerate(poly.loop_indices):
                layer.data[li].uv = (fuv[k][0] / self.A, fuv[k][1] / self.A)
        obj = bpy.data.objects.new(self.name, me)
        bpy.context.collection.objects.link(obj)
        return obj


def _finish(parts, name):
    """Tag, join, weld, shade -> the LOD0 object (meters, Z up)."""
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    table = cs2_asset.tag_parts([(p, None) for p in parts])
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0002)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    cs2_asset.store_part_table(obj, table)
    cs2_asset.shade_by_angle(obj, 66.0, weighted=False)
    return obj


def mesh_hash(obj):
    """Geometry + UV hash (verts %.5f, faces, UV loops %.6f, part table)."""
    me = obj.data
    h = hashlib.sha1()
    for v in me.vertices:
        h.update(("%.5f %.5f %.5f;" % tuple(v.co)).encode())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    uv = me.uv_layers.active.data
    for lp in uv:
        h.update(("%.6f %.6f;" % tuple(lp.uv)).encode())
    h.update(me.get(cs2_asset.PART_TABLE_PROP, "").encode())
    return h.hexdigest()


def bounds_prefab(obj):
    co = np.array([tuple(v.co) for v in obj.data.vertices], dtype=np.float64)
    pre = np.stack([co[:, 0], co[:, 2], -co[:, 1]], axis=1)
    return {"min": [round(float(v), 4) for v in pre.min(0)],
            "max": [round(float(v), 4) for v in pre.max(0)]}


# --------------------------------------------------------------------------------------
# Color / painting helpers (arrays (A, A, 4), rows bottom-up = UV v)
# --------------------------------------------------------------------------------------

def srgb_to_lin(c):
    c = np.asarray(c, np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4).astype(np.float32)


def lin_to_srgb(c):
    c = np.clip(np.asarray(c, np.float32), 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1.0 / 2.4) - 0.055).astype(np.float32)


def _sl(rect):
    x0, y0, x1, y1 = rect
    return slice(y0, y1), slice(x0, x1)


def _fill(arr, rect, rgb):
    ys, xs = _sl(rect)
    arr[ys, xs, 0:3] = np.asarray(rgb, np.float32)


def _over(arr, rect, cov, rgb):
    ys, xs = _sl(rect)
    sub = arr[ys, xs, 0:3]
    sub[...] = sub * (1.0 - cov[..., None]) + np.asarray(rgb, np.float32) * cov[..., None]


def text_cov(rect, body, cap_px, cx_frac=0.5, cy_frac=0.5, fit=0.92):
    """(h, w) coverage of Bfont `body` (cs2_liftdress._text_cov, 8x8 supersampled) in a texel
    rect, cap height cap_px texels (shrunk to fit `fit` of the rect width), 1 texel = 1 mm."""
    import cs2_liftdress
    x0, y0, x1, y1 = rect
    w, h = x1 - x0, y1 - y0
    W, H = w / 1000.0, h / 1000.0
    cap = cap_px / 1000.0
    tw = cs2_liftdress.text_width(body, cap)
    if tw > fit * W:
        cap *= fit * W / tw
    return cs2_liftdress._text_cov(body, cap, cx_frac * W, cy_frac * H, 1.0, W, H, w, h), cap * 1000.0


def text5x7_cov(rect, body):
    """(h, w) area coverage of the 5x7 bitmap font (ski_launcher_glyph.FONT, the table the
    building signs use today), laid out as cs2_bldg.paint_text5x7: cell = min(w / cols, h / 9)."""
    import ski_launcher_glyph as slg
    x0, y0, x1, y1 = rect
    w, h = x1 - x0, y1 - y0
    glyphs = [slg.FONT.get(ch.upper(), slg.FONT[" "]) for ch in body]
    cols = len(glyphs) * 6 - 1
    cell = min(w / cols, h / 9.0)
    gx0 = (w - cols * cell) / 2.0
    gy0 = (h - 7 * cell) / 2.0
    ss = 8
    xs = (np.arange(w * ss) + 0.5) / ss
    ys = (np.arange(h * ss) + 0.5) / ss
    X, Y = np.meshgrid(xs, ys)
    gx = np.floor((X - gx0) / cell).astype(np.int64)
    gyb = np.floor((Y - gy0) / cell).astype(np.int64)           # 0 = bottom row of the cell box
    ok = (gx >= 0) & (gx < cols) & (gyb >= 0) & (gyb < 7) & (gx % 6 < 5)
    bits = np.array([[[b == "1" for b in row] for row in g] for g in glyphs], bool)   # (n, 7, 5)
    hit = np.zeros(X.shape, bool)
    gi = np.where(ok, gx // 6, 0)
    col = np.where(ok, gx % 6, 0)
    row = np.where(ok, 6 - gyb, 0)                                # font row 0 = top
    hit[ok] = bits[gi[ok], row[ok], col[ok]]
    return hit.reshape(h, ss, w, ss).mean(axis=(1, 3)).astype(np.float32), cell


def write_set(asset_dir, name, maps, A, suffixes):
    out = []
    for suffix in suffixes:
        arr, is_data = maps[suffix]
        path = os.path.join(asset_dir, f"{name}_{suffix}.png")
        cs2_asset._write_png_raw(path, f"{name}_{suffix}", arr, A, is_data)
        out.append(path)
    return out


def _blank_maps(A, smooth):
    base = np.zeros((A, A, 4), np.float32)
    base[..., 3] = 1.0
    nrm = np.zeros((A, A, 4), np.float32)
    nrm[..., 0] = 0.5
    nrm[..., 1] = 0.5
    nrm[..., 2] = 1.0
    nrm[..., 3] = 1.0
    msk = np.zeros((A, A, 4), np.float32)
    msk[..., 3] = smooth
    ctl = np.zeros((A, A, 4), np.float32)
    return base, nrm, msk, ctl


# --------------------------------------------------------------------------------------
# F1: the owner sheet (2048^2, four maps)
# --------------------------------------------------------------------------------------

def paint_sheet():
    """{suffix: (array, is_data)}; BaseColor painted LINEAR then encoded sRGB (as the importer
    reads the PNG), plus the metrics of every density sample."""
    import cs2_liftdress
    base_l = np.zeros((S, S, 4), np.float32)          # linear working copy
    base_l[..., 3] = 1.0
    ctl = np.zeros((S, S, 4), np.float32)
    lin = lambda c: srgb_to_lin(c)                     # noqa: E731
    _fill(base_l, FULL, lin((0.45, 0.45, 0.45)))
    _fill(base_l, TILE_A, lin((0.20, 0.30, 0.45)))
    _fill(base_l, TILE_B, lin((0.24, 0.38, 0.24)))
    _fill(base_l, TILE_C, lin((0.85, 0.45, 0.10)))
    _fill(base_l, TILE_D, lin((0.45, 0.20, 0.60)))
    _fill(base_l, GREY, lin((0.40, 0.40, 0.40)))
    white, cream, dark = lin((0.95, 0.95, 0.93)), lin((0.95, 0.92, 0.80)), lin((0.06, 0.06, 0.07))
    metrics = {}

    def board(kind, rect, ppm):
        x0, y0, x1, y1 = rect
        m = cs2_liftdress.paint_board(kind, (y0, y1, x0, x1), ppm, base_l, ctl)
        metrics[f"{kind}@{ppm:.0f}"] = {k: m[k] for k in ("px_per_m", "size_m", "cap_m",
                                                          "stroke_px", "lines")}

    board("unload", R_UNLOAD900, 900)
    board("unload", R_UNLOAD400, 400)
    board("ops", R_OPS450, 450)

    # SKI SCHOOL: vector 300 px/m above 5x7 at ~71 px/m, same 2.40 x 0.60 m board
    brown = lin((0.36, 0.24, 0.14))
    for rect in (R_SCHOOL_VEC, R_SCHOOL_5X7):
        _fill(base_l, rect, brown)
    cov, cap_px = text_cov(R_SCHOOL_VEC, "SKI SCHOOL", 0.30 * 300.0, fit=0.90)
    _over(base_l, R_SCHOOL_VEC, cov, cream)
    vec_ppm = (R_SCHOOL_VEC[2] - R_SCHOOL_VEC[0]) / SCHOOL_BOARD_M[0]
    metrics["school_vector"] = {"px_per_m": round(vec_ppm, 2), "cap_m": round(cap_px / vec_ppm, 4),
                                "rect": list(R_SCHOOL_VEC), "font": "Bfont (cs2_liftdress._text_cov)"}
    cov5, cell = text5x7_cov(R_SCHOOL_5X7, "SKI SCHOOL")
    _over(base_l, R_SCHOOL_5X7, cov5, cream)
    p5 = (R_SCHOOL_5X7[2] - R_SCHOOL_5X7[0]) / SCHOOL_BOARD_M[0]
    metrics["school_5x7"] = {"px_per_m": round(p5, 2), "cell_px": round(cell, 3),
                             "cell_m": round(cell / p5, 4), "rect": list(R_SCHOOL_5X7),
                             "font": "ski_launcher_glyph.FONT 5x7"}

    # tile identifiers (1 texel = 1 mm for the text helper)
    def label(rect, body, cap_px, cy=0.5, rgb=white, fit=0.85):
        c, _ = text_cov(rect, body, cap_px, 0.5, cy, fit)
        _over(base_l, rect, c, rgb)

    label((0, 1024, 1024, 1290), "TILE A  LIFT 900 / 450 / 400", 70, fit=0.92)
    label((1024, 1024, 2048, 1300), "TILE B  SCHOOL 300 VS 5X7", 70, fit=0.92)
    label((1024, 1500, 2048, 1690), "VECTOR 300 PX/M ABOVE, 5X7 71 PX/M BELOW", 40, fit=0.92)
    label((64, 380, 1024, 760), "USER B", 300)
    label((64, 120, 1024, 340), "BATCH 1 SHARER", 110)
    label((1024, 380, 2048, 760), "LATE B", 300)
    label((1024, 120, 2048, 340), "BATCH 2 SHARER", 110)
    label((0, 1990, 2048, 2048), "SKIPROBESHEETB OWNER 2048", 40, rgb=dark, fit=0.5)

    base = np.empty_like(base_l)
    base[..., 0:3] = lin_to_srgb(base_l[..., 0:3])
    base[..., 3] = 1.0
    _b, nrm, msk, _c = _blank_maps(S, 0.30)
    ctl[...] = 0.0                                     # never tinted (paint_board writes 0 too)
    return {"BaseColor": (base, False), "Normal": (nrm, True), "MaskMap": (msk, True),
            "ControlMask": (ctl, True)}, metrics


# --------------------------------------------------------------------------------------
# F1 meshes
# --------------------------------------------------------------------------------------

def build_owner(name=OWNER):
    """1.00 x 1.00 x 0.04 m panel, bottom at y 0; front (+z game) = the whole sheet."""
    m = _MeshBuf("panel", S)
    m.box((0.0, 0.0, 0.5), (1.00, 0.04, 1.00),
          {"ny": FULL, "all": GREY}, skip=("nz",))
    return _finish([m.to_object()], name)


def build_user(name=USER):
    """Two-post billboard: board 2.40 x 1.20 x 0.05 m at y 0.60-1.80; front upper half = SKI
    SCHOOL vector (true 300 px/m), lower half = 5x7 (true ~71 px/m); back = tile C."""
    bd = _MeshBuf("board", S)
    yc_up, yc_lo = 1.50, 0.90                       # game y centers of the two front halves
    front = (0.0, -1.0, 0.0)                        # +z game
    bd.quad((0.0, -0.025, yc_up), front, (0, 0, 1), 1.20, 0.30, R_SCHOOL_VEC)
    bd.quad((0.0, -0.025, yc_lo), front, (0, 0, 1), 1.20, 0.30, R_SCHOOL_5X7)
    bd.quad((0.0, 0.025, 1.20), (0, 1, 0), (0, 0, 1), 1.20, 0.60, TILE_C)
    bd.quad((1.20, 0.0, 1.20), (1, 0, 0), (0, 0, 1), 0.025, 0.60, GREY)
    bd.quad((-1.20, 0.0, 1.20), (-1, 0, 0), (0, 0, 1), 0.025, 0.60, GREY)
    bd.quad((0.0, 0.0, 1.80), (0, 0, 1), (0, 1, 0), 1.20, 0.025, GREY)
    bd.quad((0.0, 0.0, 0.60), (0, 0, -1), (0, -1, 0), 1.20, 0.025, GREY)
    posts = []
    for side, x in (("l", -1.05), ("r", 1.05)):
        p = _MeshBuf(f"post_{side}", S)
        p.box((x, 0.065, 0.925), (0.08, 0.08, 1.85), {"all": GREY}, skip=("nz",))
        posts.append(p.to_object())
    return _finish([bd.to_object()] + posts, name)


def build_late(name=LATE):
    """A-frame sandwich board 0.90 wide, 1.10 tall, base 0.70 deep: front = tile D, back =
    tile A; triangular sides gray; open bottom."""
    m = _MeshBuf("aframe", S)
    hw, H, D = 0.45, 1.10, 0.35
    L = math.hypot(H, D)
    nf = mathutils.Vector((0.0, -H, D)) / L          # Blender: outward of the +z-game panel
    uf = mathutils.Vector((0.0, D, H)) / L
    m.quad((0.0, -D / 2.0, H / 2.0), nf, uf, hw, L / 2.0, TILE_D)
    nb = mathutils.Vector((0.0, H, D)) / L
    ub = mathutils.Vector((0.0, -D, H)) / L
    m.quad((0.0, D / 2.0, H / 2.0), nb, ub, hw, L / 2.0, TILE_A)
    m.tri([(hw, -D, 0.0), (hw, D, 0.0), (hw, 0.0, H)], GREY)
    m.tri([(-hw, D, 0.0), (-hw, -D, 0.0), (-hw, 0.0, H)], GREY)
    return _finish([m.to_object()], name)


# --------------------------------------------------------------------------------------
# F4: sign set (measured walls) and its maps
# --------------------------------------------------------------------------------------

def _load_fbx_bvh(path):
    """BVH of an exported FBX in PREFAB meters (raw cm Y-up vertices / 100, read as stored)."""
    from mathutils.bvhtree import BVHTree
    objs = cs2_validate.import_fbx_temp(path)
    try:
        meshes = [o for o in objs if o.type == "MESH"]
        verts, polys, off = [], [], 0
        for o in meshes:
            me = o.data
            co = np.empty(len(me.vertices) * 3, dtype=np.float64)
            me.vertices.foreach_get("co", co)
            co = co.reshape(-1, 3) / cs2_asset.CS2_UNIT_SCALE
            verts.append(co)
            polys.extend(tuple(off + i for i in p.vertices) for p in me.polygons)
            off += len(co)
        V = np.concatenate(verts, axis=0)
        bvh = BVHTree.FromPolygons([tuple(v) for v in V], polys)
        return bvh, V.min(0), V.max(0)
    finally:
        cs2_validate.remove_objects(objs)


def _pack_part_lookup():
    """(BVH in prefab frame, face -> part name) from the DQ base review pack, or None."""
    if not os.path.isfile(DQ_PACK):
        return None
    from mathutils.bvhtree import BVHTree
    with bpy.data.libraries.load(DQ_PACK, link=False) as (src, dst):
        want = [n for n in src.meshes if n == f"CS2R_{DQ_NAME}_LOD0"]
        dst.meshes = want
    if not dst.meshes:
        return None
    me = dst.meshes[0]
    try:
        table = json.loads(me[cs2_asset.PART_TABLE_PROP])
        attr = me.attributes["part"]
        names = [table[a.value - 1] for a in attr.data]
        verts = [(v.co.x, v.co.z, -v.co.y) for v in me.vertices]      # pack (gx, -gz, gy)
        polys = [tuple(p.vertices) for p in me.polygons]
        return BVHTree.FromPolygons(verts, polys), names
    finally:
        bpy.data.meshes.remove(me)


def _sha1(path):
    h = hashlib.sha1()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def measure_card(bvh, card, names=None):
    """Ray-cast a 9 x 5 grid over the card footprint from 0.30 m (plane) and CLEAR_M (clearance)
    in front of the expected wall. Returns the row dict; 'ok' False with reasons on failure."""
    V = mathutils.Vector
    out = -1.0 if card["outward"] < 0 else 1.0
    d_in = V((-out, 0.0, 0.0))                     # toward the wall (prefab frame)
    hits, normals, firsts, parts, reasons = [], [], [], set(), []
    for i in range(9):
        for j in range(5):
            z = card["z"] - card["w"] / 2.0 + 0.01 + (card["w"] - 0.02) * i / 8.0
            y = card["y"] - card["h"] / 2.0 + 0.01 + (card["h"] - 0.02) * j / 4.0
            o = V((card["wall_x"] + out * 0.30, y, z))
            loc, nrm, idx, dist = bvh.ray_cast(o, d_in, 1.0)
            if loc is None:
                reasons.append(f"miss at y {y:.3f} z {z:.3f}")
                continue
            hits.append(loc.x)
            normals.append(nrm.normalized())
            o2 = V((card["wall_x"] + out * CLEAR_M, y, z))
            loc2, _n2, _i2, _d2 = bvh.ray_cast(o2, d_in, CLEAR_M + 0.5)
            firsts.append(loc2.x if loc2 is not None else None)
            if names is not None:
                loc3, _n3, idx3, _d3 = names[0].ray_cast(o, d_in, 1.0)
                if loc3 is not None:
                    parts.add(names[1][idx3])
    if not hits:
        return {"key": card["key"], "ok": False, "reasons": reasons or ["no hits"]}
    xs = np.array(hits)
    med = float(np.median(xs))
    flat = float(xs.max() - xs.min())
    want_n = V((out, 0.0, 0.0))
    worst = max(math.degrees(n.angle(want_n)) for n in normals)
    if flat > 2 * WALL_TOL:
        reasons.append(f"wall not flat over the footprint: {flat * 1000:.1f} mm")
    if abs(med - card["wall_x"]) > 0.05:
        reasons.append(f"wall at x {med:.4f}, expected {card['wall_x']:.3f} (moved > 5 cm)")
    if worst > NORMAL_TOL_DEG:
        reasons.append(f"wall normal off by {worst:.2f} deg")
    blocked = [f for f in firsts if f is None or abs(f - med) > WALL_TOL]
    if blocked:
        reasons.append(f"{len(blocked)} of {len(firsts)} clearance rays hit something within "
                       f"{CLEAR_M} m in front of the wall")
    return {"key": card["key"], "ok": not reasons, "reasons": reasons,
            "wall_x_measured": round(med, 4), "flat_mm": round(flat * 1000.0, 2),
            "normal_err_deg": round(worst, 3), "rays": len(hits),
            "clear_m": CLEAR_M, "parts_hit": sorted(parts) if parts else None,
            "frame": "FBX-raw (host and set flip together; in game x is negated)",
            "card_centre_raw": [round(med + out * CARD_T / 2.0, 4), card["y"], card["z"]],
            "front_plane_x_raw": round(med + out * CARD_T, 4),
            "card_centre_in_game": [round(-(med + out * CARD_T / 2.0), 4), card["y"], card["z"]]}


def measure_lodge(bvh):
    """Down-casts at the corners + center of each lodge row footprint: miss, or ground <= 0.05."""
    V = mathutils.Vector
    rows = []
    for asset, pos, (fx, fz) in LODGE_ROWS:
        reasons = []
        rp = raw_of(pos)
        for dx in (-0.5, 0.0, 0.5):
            for dz in (-0.5, 0.0, 0.5):
                x, z = rp[0] + dx * fx, rp[2] + dz * fz
                loc, _n, _i, _d = bvh.ray_cast(V((x, 40.0, z)), V((0.0, -1.0, 0.0)), 50.0)
                if loc is not None and loc.y > 0.05:
                    reasons.append(f"lodge geometry at ({x:.2f}, {loc.y:.2f}, {z:.2f})")
        inside = (abs(pos[0]) + fx / 2.0 <= LODGE_LOT_HALF[0]) and (abs(pos[2]) + fz / 2.0 <= LODGE_LOT_HALF[1])
        if not inside:
            reasons.append("outside the 6 x 5 lot")
        rows.append({"asset": asset, "object_prefab": OBJECT_PREFAB[asset], "m_Position_in_game": list(pos),
                     "position_fbx_raw": list(rp), "m_ParentMesh": -1, "m_Rotation": [0, 0, 0, 1],
                     "footprint_xz": [fx, fz], "ok": not reasons, "reasons": reasons})
    return rows


def measure_all():
    """Sign-card walls on the shipped DQ base FBX + lodge ground spots. Returns the dict."""
    res = {"dq_fbx": os.path.relpath(DQ_FBX, REPO).replace("\\", "/"),
           "dq_fbx_sha1": _sha1(DQ_FBX) if os.path.isfile(DQ_FBX) else None,
           "lodge_fbx": os.path.relpath(LODGE_FBX, REPO).replace("\\", "/"),
           "lodge_fbx_sha1": _sha1(LODGE_FBX) if os.path.isfile(LODGE_FBX) else None}
    bvh, lo, hi = _load_fbx_bvh(DQ_FBX)
    names = None
    try:
        names = _pack_part_lookup()
    except Exception as e:  # noqa: BLE001  (the pack only labels parts; never load-bearing)
        print(f"[fine_probes] review pack part lookup skipped: {e!r}")
    res["dq_fbx_bounds"] = {"min": [round(float(v), 4) for v in lo], "max": [round(float(v), 4) for v in hi]}
    res["cards"] = [measure_card(bvh, c, names) for c in CARDS]
    lbvh, llo, lhi = _load_fbx_bvh(LODGE_FBX)
    res["lodge_fbx_bounds"] = {"min": [round(float(v), 4) for v in llo], "max": [round(float(v), 4) for v in lhi]}
    res["lodge_bounds_deployed"] = {"min": list(LODGE_BOUNDS_DEPLOYED[0]), "max": list(LODGE_BOUNDS_DEPLOYED[1])}
    drift = max(abs(a - b) for a, b in zip(list(llo) + list(lhi),
                                           list(LODGE_BOUNDS_DEPLOYED[0]) + list(LODGE_BOUNDS_DEPLOYED[1])))
    res["lodge_fbx_vs_deployed_bounds_m"] = round(float(drift), 4)
    res["lodge_rows"] = measure_lodge(lbvh)
    res["ok"] = all(c["ok"] for c in res["cards"]) and all(r["ok"] for r in res["lodge_rows"])
    return res


def build_signset(meas, name=SIGNSET):
    """Two cards on the measured walls (front quad + 4 edges each; no back: it sits on the
    wall). Returns the object (Blender frame = the host's frame)."""
    parts = []
    rows = {r["key"]: r for r in meas["cards"]}
    for card, rect in zip(CARDS, (R_CARD_A, R_CARD_B)):
        r = rows[card["key"]]
        if not r["ok"]:
            raise RuntimeError(f"{card['key']}: wall spot not OK: {r['reasons']}")
        out = card["outward"]
        wall = r["wall_x_measured"]
        xc = wall + out * CARD_T / 2.0
        c = g2b((xc, card["y"], card["z"]))
        n = mathutils.Vector((out, 0.0, 0.0))        # game -x = Blender -x
        up = mathutils.Vector((0.0, 0.0, 1.0))
        m = _MeshBuf(card["key"], SA)
        hw, hh, ht = card["w"] / 2.0, card["h"] / 2.0, CARD_T / 2.0
        m.quad(c + n * ht, n, up, hw, hh, rect)
        right = (-n).cross(up)
        m.quad(c + right * hw, right, up, ht, hh, R_EDGE)
        m.quad(c - right * hw, -right, up, ht, hh, R_EDGE)
        m.quad(c + up * hh, up, -n, hw, ht, R_EDGE)
        m.quad(c - up * hh, -up, n, hw, ht, R_EDGE)
        parts.append(m.to_object())
    return _finish(parts, name)


def paint_signset():
    base_l = np.zeros((SA, SA, 4), np.float32)
    base_l[..., 3] = 1.0
    lin = lambda c: srgb_to_lin(c)                     # noqa: E731
    _fill(base_l, (0, 0, SA, SA), lin((0.30, 0.30, 0.30)))
    _fill(base_l, R_EDGE, lin((0.18, 0.18, 0.19)))
    # card A: navy field, 15 mm white border, "PROBE B" white, lit
    navy, white = lin((0.08, 0.16, 0.32)), lin((0.95, 0.95, 0.93))
    _fill(base_l, R_CARD_A, white)
    b = int(round(0.015 * CARD_PPM))
    x0, y0, x1, y1 = R_CARD_A
    inner = (x0 + b, y0 + b, x1 - b, y1 - b)
    _fill(base_l, inner, navy)
    cov, cap_px = text_cov(inner, "PROBE B", LETTER_CAP_M * CARD_PPM, fit=0.86)
    _over(base_l, inner, cov, white)
    emi = np.zeros((SA, SA, 4), np.float32)
    emi[..., 3] = 1.0
    ys, xs = _sl(inner)
    for k in range(3):
        emi[ys, xs, k] = EMISSIVE_SRGB[k] * cov
    # card B: 10 bands of 0.100 m, black first
    bx0, by0, bx1, by1 = R_CARD_B
    band = (bx1 - bx0) / 10.0
    for k in range(10):
        rgb = lin((0.03, 0.03, 0.03)) if k % 2 == 0 else lin((0.92, 0.92, 0.90))
        _fill(base_l, (int(round(bx0 + k * band)), by0, int(round(bx0 + (k + 1) * band)), by1), rgb)
    base = np.empty_like(base_l)
    base[..., 0:3] = lin_to_srgb(base_l[..., 0:3])
    base[..., 3] = 1.0
    _b, nrm, msk, ctl = _blank_maps(SA, 0.35)
    metrics = {"card_a": {"rect": list(R_CARD_A), "px_per_m": CARD_PPM, "cap_m": round(cap_px / CARD_PPM, 4),
                          "border_m": 0.015, "lit_texels": int(np.count_nonzero(cov > 0.5))},
               "card_b": {"rect": list(R_CARD_B), "px_per_m": CARD_PPM, "band_px": band, "bands": 10}}
    return {"BaseColor": (base, False), "Normal": (nrm, True), "MaskMap": (msk, True),
            "ControlMask": (ctl, True), "Emissive": (emi, False)}, metrics


# --------------------------------------------------------------------------------------
# FS7: rack proxy
# --------------------------------------------------------------------------------------

SKI_X = (-0.44, -0.22, 0.0, 0.22, 0.44)


def build_rack(name=RACK):
    parts = []
    for side, x in (("l", -0.60), ("r", 0.60)):
        p = _MeshBuf(f"post_{side}", RA)
        p.box((x, 0.0, 0.525), (0.05, 0.05, 1.05), {"all": R_GALV}, skip=("nz",))
        parts.append(p.to_object())
        f = _MeshBuf(f"foot_{side}", RA)
        f.box((x, -0.20, 0.02), (0.06, 0.55, 0.04), {"all": R_GALV}, skip=("nz",))
        parts.append(f.to_object())
    for key, c in (("rail_top", (0.0, 0.0, 1.00)), ("rail_kick", (0.0, -0.40, 0.065))):
        r = _MeshBuf(key, RA)
        r.box(c, (1.25, 0.05, 0.05), {"all": R_GALV})
        parts.append(r.to_object())
    # skis lean from (y 0.01, z 0.36) to (y 1.61, z -0.08) game; Blender (x, -z, y)
    for i, x in enumerate(SKI_X):
        a = g2b((x, 0.01, 0.36))
        b = g2b((x, 1.61, -0.08))
        u = (b - a)
        L = u.length
        u.normalize()
        n = mathutils.Vector((1.0, 0.0, 0.0)).cross(u)            # in the y-z plane, facing +z game
        if n.y > 0:
            n = -n
        s = _MeshBuf(f"ski_{i}", RA)
        s.oriented_box((a + b) / 2.0, u, n, 0.07, L, 0.02, R_ACCENT)
        parts.append(s.to_object())
    return _finish(parts, name)


def paint_rack():
    base, nrm, msk, ctl = _blank_maps(RA, 0.45)
    _fill(base, R_GALV, (0.35, 0.36, 0.37))
    _fill(base, R_ACCENT, (cs2_asset.RECOLOR_ALBEDO,) * 3)          # light neutral under slot 2
    ys, xs = _sl(R_GALV)
    msk[ys, xs, 0] = 1.0
    msk[ys, xs, 3] = 0.50
    ys, xs = _sl(R_ACCENT)
    ctl[ys, xs, 1] = 1.0                                            # G = slot 2 (channel 1)
    return {"BaseColor": (base, False), "Normal": (nrm, True), "MaskMap": (msk, True),
            "ControlMask": (ctl, True)}


# --------------------------------------------------------------------------------------
# Settings, validation, export
# --------------------------------------------------------------------------------------

def shared_settings(name, owner=OWNER):
    """{"sharedAssets": {<name>_<map>.png: ../<owner>/<owner>_<map>.png}} (4 keys, the
    cs2_asset.write_settings key order). Written here, so cs2_asset stays untouched (FS12)."""
    return {"sharedAssets": {f"{name}_{m}.png": f"../{owner}/{owner}_{m}.png" for m in SHARE_KEY_ORDER}}


def write_json(path, data):
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(data, fh, indent=2)
    return path


def _clean_asset_dir(asset_dir, name):
    for f in os.listdir(asset_dir):
        p = os.path.join(asset_dir, f)
        if os.path.isfile(p) and (f.startswith(name) or f == "settings.json"):
            os.remove(p)


def _spec(name, lod0_cap, min_tex):
    return {"kind": "prop", "lod0_cap": lod0_cap, "min_texture": min_tex, "expected_lods": (0,)}


def validate_folder(asset_dir, name, spec):
    findings, summary = cs2_validate.validate_asset_folder_ex(asset_dir, name, spec)
    err = [f"{c}: {m}" for lv, c, m in findings if lv == "ERROR"]
    warn = [f"{c}: {m}" for lv, c, m in findings if lv == "WARN"]
    return {"errors": len(err), "warnings": len(warn), "error_list": err, "warn_list": warn,
            "tris": summary.get("tris"), "tex": sorted(summary.get("tex") or [])}


def validate_sharer_staged(root, name):
    """Stage the sharer as the importer should see it after sharedAssets resolution (owner PNGs
    under the sharer's names, settings {}) and run the folder validator on that."""
    tmp = tempfile.mkdtemp(prefix="fine_probes_stage_")
    try:
        d = os.path.join(tmp, name)
        os.makedirs(d)
        shutil.copy2(os.path.join(root, name, name + ".fbx"), d)
        for m in MAPS4:
            shutil.copy2(os.path.join(root, OWNER, f"{OWNER}_{m}.png"), os.path.join(d, f"{name}_{m}.png"))
        write_json(os.path.join(d, "settings.json"), {})
        return validate_folder(d, name, _spec(name, LOD0_CAP[name], 128))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def mesh_checks(obj, kind):
    f = cs2_validate.validate_mesh(obj, kind=kind)
    return {"errors": [f"{c}: {m}" for lv, c, m in f if lv == "ERROR"],
            "warnings": [f"{c}: {m}" for lv, c, m in f if lv == "WARN"]}


def object_cids():
    p = os.path.join(DATA_DIR, "object-cids.json")
    if not os.path.isfile(p):
        return {}
    with open(p, "r", encoding="utf-8") as fh:
        return {k: v for k, v in json.load(fh).items() if k != "note"}


def export(out_root, preview_dir=None, args=None, meas=None):
    """Build and write all five folders. Returns {name: manifest data}."""
    clear_scene()
    root = os.path.abspath(out_root)
    os.makedirs(root, exist_ok=True)
    meas = meas or measure_all()
    if not meas["ok"]:
        raise RuntimeError("measure failed: " + json.dumps([c for c in meas["cards"] if not c["ok"]]
                                                           + [r for r in meas["lodge_rows"] if not r["ok"]]))
    cids = object_cids()
    common = {"generator": "assets/blender/fine_probes.py", "args": list(args or []),
              "round": "probes B, import session S1 (fine-detail pass F1 / F4 / FS7)",
              "lods": "LOD0 only (settings: no LOD keys)"}
    out = {}

    def folder(name):
        d = os.path.join(root, name)
        os.makedirs(d, exist_ok=True)
        _clean_asset_dir(d, name)
        return d

    # ---- F1 owner
    sheet_maps, sheet_metrics = paint_sheet()
    d = folder(OWNER)
    write_set(d, OWNER, sheet_maps, S, MAPS4)
    write_json(os.path.join(d, "settings.json"), {})
    o = build_owner()
    cs2_asset.export_fbx(o, os.path.join(d, OWNER + ".fbx"))
    out[OWNER] = dict(common, kind="F1 sheet OWNER (static prop)", batch=BATCH[OWNER],
                      object_prefab=OBJECT_PREFAB[OWNER], object_cid=cids.get(OBJECT_PREFAB[OWNER]),
                      mesh_prefab=f"{OWNER} Mesh", tris={"LOD0": cs2_asset.tri_count(o)},
                      bounds_m=bounds_prefab(o), texture_px=S, maps=list(MAPS4),
                      vram_mb_raw_bc=round(4 * S * S * 4 / 3 / 2 ** 20, 2),
                      sheet_layout={"tile_A_lift": TILE_A, "tile_B_school": TILE_B, "tile_C_user": TILE_C,
                                    "tile_D_late": TILE_D, "grey_edges": GREY},
                      density_samples=sheet_metrics, mesh_hash=mesh_hash(o),
                      validation=validate_folder(d, OWNER, _spec(OWNER, LOD0_CAP[OWNER], 2048)),
                      mesh_validation=mesh_checks(o, "prop"))

    # ---- F1 sharers
    for name, builder, uvs in ((USER, build_user, {"front_upper": R_SCHOOL_VEC, "front_lower": R_SCHOOL_5X7,
                                                   "back": TILE_C, "edges_posts": GREY}),
                               (LATE, build_late, {"front": TILE_D, "back": TILE_A, "sides": GREY})):
        d = folder(name)
        settings = shared_settings(name)
        write_json(os.path.join(d, "settings.json"), settings)
        s = builder()
        cs2_asset.export_fbx(s, os.path.join(d, name + ".fbx"))
        resolved = {k: os.path.isfile(os.path.normpath(os.path.join(d, v)))
                    for k, v in settings["sharedAssets"].items()}
        out[name] = dict(common, kind="F1 sheet SHARER (static prop, no own maps)", batch=BATCH[name],
                         object_prefab=OBJECT_PREFAB[name], object_cid=cids.get(OBJECT_PREFAB[name]),
                         mesh_prefab=f"{name} Mesh", tris={"LOD0": cs2_asset.tri_count(s)},
                         bounds_m=bounds_prefab(s), shared_from=OWNER, sharedAssets=settings["sharedAssets"],
                         shared_targets_exist=resolved, uv_rects=uvs, mesh_hash=mesh_hash(s),
                         validation_as_shipped=validate_folder(d, name, _spec(name, LOD0_CAP[name], 128)),
                         validation_staged_with_owner_maps=validate_sharer_staged(root, name),
                         mesh_validation=mesh_checks(s, "prop"),
                         note="assets-check / the folder validator cannot follow a cross-asset "
                              "sharedAssets path: the as-shipped run reports the missing-target / "
                              "no-maps errors by design; the staged run is the importer's view")

    # ---- F4 sign set
    ss_maps, ss_metrics = paint_signset()
    d = folder(SIGNSET)
    write_set(d, SIGNSET, ss_maps, SA, MAPS4 + ("Emissive",))
    write_json(os.path.join(d, "settings.json"), {})
    so = build_signset(meas)
    cs2_asset.export_fbx(so, os.path.join(d, SIGNSET + ".fbx"))
    out[SIGNSET] = dict(common, kind="F4 sign-set sub-object (identity row, host frame)", batch=BATCH[SIGNSET],
                        object_prefab=OBJECT_PREFAB[SIGNSET], object_cid=cids.get(OBJECT_PREFAB[SIGNSET]),
                        mesh_prefab=f"{SIGNSET} Mesh", tris={"LOD0": cs2_asset.tri_count(so)},
                        bounds_m=bounds_prefab(so), texture_px=SA, maps=list(MAPS4) + ["Emissive"],
                        host={"gameplay_prefab": "SkiDQBaseStationBuilding", "mesh": DQ_NAME,
                              "fbx_sha1_measured": meas["dq_fbx_sha1"]},
                        cards=meas["cards"], card_t_m=CARD_T, paint=ss_metrics,
                        row={"m_Position": [0, 0, 0], "m_Rotation": [0, 0, 0, 1], "m_ParentMesh": 0,
                             "m_GroupIndex": 0, "m_Probability": 100},
                        emissive_surgery={"importer_writes": "purpose 34 (DecorativeLight), intensity 5, LOD0",
                                          "set_intensity": EMISSIVE_INTENSITY_SURGERY,
                                          "emitted_colour_srgb_in_map": EMISSIVE_SRGB},
                        mesh_hash=mesh_hash(so),
                        validation=validate_folder(d, SIGNSET, _spec(SIGNSET, LOD0_CAP[SIGNSET], 128)),
                        mesh_validation=mesh_checks(so, "hosted"))

    # ---- FS7 rack
    d = folder(RACK)
    write_set(d, RACK, paint_rack(), RA, MAPS4)
    write_json(os.path.join(d, "settings.json"), {})
    ro = build_rack()
    cs2_asset.export_fbx(ro, os.path.join(d, RACK + ".fbx"))
    out[RACK] = dict(common, kind="FS7 rack proxy (static prop, slot-2 skis)", batch=BATCH[RACK],
                     object_prefab=OBJECT_PREFAB[RACK], object_cid=cids.get(OBJECT_PREFAB[RACK]),
                     mesh_prefab=f"{RACK} Mesh", tris={"LOD0": cs2_asset.tri_count(ro)},
                     bounds_m=bounds_prefab(ro), texture_px=RA, maps=list(MAPS4),
                     controlmask={"G_slot2": "skis (accent rect)", "R": 0, "B": 0},
                     colorproperties_surgery={"m_ExternalColorSource": 1, "meaning": "1 = Parent (ColorSourceType: Brand 0, Parent 1)",
                                              "variation_colors": [[0.25, 0.26, 0.275, 1], list(RACK_OWN_SLOT2) + [1], [1, 1, 1, 1]],
                                              "bindings_can_be_modified_by_external": [False, True, False],
                                              "m_VariationRanges": [0, 0, 0]},
                     host={"gameplay_prefab": "SkiAFrameDayLodge", "row_position": list(LODGE_ROWS[3][1]),
                           "lodge_slot2_default": list(LODGE_SLOT2_DEFAULT)},
                     mesh_hash=mesh_hash(ro),
                     validation=validate_folder(d, RACK, _spec(RACK, LOD0_CAP[RACK], 128)),
                     mesh_validation=mesh_checks(ro, "prop"))
    rows = {r["asset"]: r for r in meas["lodge_rows"]}
    for name in (OWNER, USER, LATE, RACK):
        out[name]["lodge_row"] = rows[name]
    out[OWNER]["measure"] = {k: v for k, v in meas.items() if k not in ("cards",)}

    if preview_dir:
        try:
            paths = render_previews(root, preview_dir, meas)
            for n in out:
                out[n]["previews"] = [os.path.relpath(p, REPO).replace("\\", "/") for p in paths]
        except Exception as e:  # noqa: BLE001  previews never block the export
            print(f"[fine_probes] preview failed: {e!r}")
            for n in out:
                out[n]["previews_error"] = repr(e)

    for name, data in out.items():
        cs2_validate.write_manifest(os.path.join(root, name), name, data)
        v = data.get("validation") or data.get("validation_staged_with_owner_maps")
        print(f"[fine_probes] {name}: {data['tris']['LOD0']} tris, bounds {data['bounds_m']}, "
              f"validation {v['errors']} errors / {v['warnings']} warnings")
    bad = [n for n, dta in out.items()
           if (dta.get("validation") or dta["validation_staged_with_owner_maps"])["errors"]
           or dta["mesh_validation"]["errors"]]
    if bad:
        raise RuntimeError(f"validation errors in {bad}")
    return out


# --------------------------------------------------------------------------------------
# Previews (scratch scenes; nothing exported)
# --------------------------------------------------------------------------------------

def _import_metres(fbx):
    objs = cs2_validate.import_fbx_temp(fbx)
    meshes = [o for o in objs if o.type == "MESH"]
    for o in objs:
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    t = meshes[0]
    t.data.transform(cs2_validate._import_matrix())
    t.matrix_world = mathutils.Matrix.Identity(4)
    t.data.update()
    return t


def render_previews(root, out_dir, meas):
    """Scale previews: the sign set on a scratch copy of the DQ base; the lodge-front row of
    F1 cards + rack on a scratch copy of the deployed lodge. Returns written paths."""
    import cs2_preview as P
    out_dir = os.path.abspath(out_dir)
    os.makedirs(out_dir, exist_ok=True)
    scratch = tempfile.mkdtemp(prefix="fine_probes_prev_")
    written = []
    try:
        # material folders (sharers borrow the owner maps under their own names)
        for name in SHARERS:
            sd = os.path.join(scratch, name)
            os.makedirs(sd)
            for m in MAPS4:
                shutil.copy2(os.path.join(root, OWNER, f"{OWNER}_{m}.png"), os.path.join(sd, f"{name}_{m}.png"))
        for host_dir, host in ((DQ_DIR, DQ_NAME), (LODGE_DIR, LODGE_NAME)):
            hd = os.path.join(scratch, host)
            os.makedirs(hd)
            for f in os.listdir(host_dir):
                if f.startswith(host) and "_LOD" not in f and f.endswith((".fbx", ".png")) and "_Win" not in f:
                    shutil.copy2(os.path.join(host_dir, f), hd)

        def mat_dir(name):
            return os.path.join(scratch, name) if name in SHARERS else os.path.join(root, name)

        # -- sign set on the DQ base
        signs = build_signset(meas, name="SignSetPreview")
        host = _import_metres(os.path.join(scratch, DQ_NAME, DQ_NAME + ".fbx"))
        rig = P._Rig(signs, None)
        try:
            rig.track(host)
            host.hide_render = False
            cam = P._make_camera(rig)
            P._assign(host, P._pbr_material(rig, os.path.join(scratch, DQ_NAME), DQ_NAME, None))
            smat = P._pbr_material(rig, os.path.join(root, SIGNSET), SIGNSET, None, mat_name="SignSetPrev")
            P._assign(signs, smat)
            bsdf = next(n for n in smat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            sc = rig.scene
            ca = g2b((CARDS[0]["wall_x"], CARDS[0]["y"], CARDS[0]["z"] + 1.5))   # between card A and LIFT OPS
            cb = g2b((CARDS[1]["wall_x"], CARDS[1]["y"], CARDS[1]["z"]))

            def shot(label, world, lit, target, direction, dist, res=(900, 600)):
                _w, sun = P._make_world(rig, **world)
                gnd = P._make_ground(rig)
                bsdf.inputs["Emission Strength"].default_value = P.EMISSIVE_STRENGTH if lit else 0.0
                sc.render.resolution_x, sc.render.resolution_y = res
                P._aim(cam, target, mathutils.Vector(direction), dist)
                path = os.path.join(out_dir, f"{SIGNSET}_on{DQ_NAME}_{label}.png")
                written.append(P._render(rig, path, engine="EEVEE"))
                print(f"[fine_probes] preview -> {path}")
                for ob in (gnd, sun):
                    rig.objects.remove(ob)
                    bpy.data.objects.remove(ob, do_unlink=True)
                sc.world = None

            shot("cardA_with_LIFTOPS_noon", P.NOON, False, ca, (-4.4, 1.0, -0.5), 4.6)   # camera ~(1.0, 2.0, 5.0) raw, under the soffit
            shot("cardA_dusk_lit", P.DUSK, True, ca, (-4.4, 1.0, -0.5), 4.6)
            shot("cardB_ruler_noon", P.NOON, False, cb, (-1.0, 0.15, 0.05), 4.0)
            shot("wide_noon", P.NOON, False, g2b((0.5, 3.0, 7.0)), (-1.0, 0.9, 0.55), 26.0, res=(1050, 675))
        finally:
            rig.restore()
            for ob in (signs, host):
                try:
                    me = ob.data
                    bpy.data.objects.remove(ob, do_unlink=True)
                    if me.users == 0:
                        bpy.data.meshes.remove(me)
                except ReferenceError:
                    pass

        # -- lodge front: F1 trio + rack at their row positions
        builders = {OWNER: build_owner, USER: build_user, LATE: build_late, RACK: build_rack}
        objs = []
        for name, pos, _fp in LODGE_ROWS:
            ob = builders[name](name=name + "Prev")
            ob.location = g2b(raw_of(pos))
            objs.append((name, ob))
        lodge = _import_metres(os.path.join(scratch, LODGE_NAME, LODGE_NAME + ".fbx"))
        rig = P._Rig(objs[0][1], None)
        try:
            rig.track(lodge)
            lodge.hide_render = False
            for _n, ob in objs[1:]:
                ob.hide_render = False
            cam = P._make_camera(rig)
            P._assign(lodge, P._pbr_material(rig, os.path.join(scratch, LODGE_NAME), LODGE_NAME, None))
            for name, ob in objs:
                tint = {"G": LODGE_SLOT2_DEFAULT} if name == RACK else None
                P._assign(ob, P._pbr_material(rig, mat_dir(name), name, tint, mat_name=f"{name}Prev"))
            sc = rig.scene
            for label, target, direction, dist in (
                    ("front_noon", g2b(raw_of((-2.5, 1.0, 17.0))), (-0.15, -1.0, 0.25), 13.0),
                    ("wide_noon", g2b(raw_of((-2.5, 3.0, 10.0))), (-0.35, -1.0, 0.55), 42.0),
                    ("school_board_3m_noon", g2b(raw_of((-4.5, 1.2, 17.0))), (0.0, -1.0, 0.05), 3.0)):
                _w, sun = P._make_world(rig, **P.NOON)
                gnd = P._make_ground(rig)
                sc.render.resolution_x, sc.render.resolution_y = (1050, 600)
                P._aim(cam, target, mathutils.Vector(direction), dist)
                path = os.path.join(out_dir, f"probesB_on{LODGE_NAME}_{label}.png")
                written.append(P._render(rig, path, engine="EEVEE"))
                print(f"[fine_probes] preview -> {path}")
                for ob in (gnd, sun):
                    rig.objects.remove(ob)
                    bpy.data.objects.remove(ob, do_unlink=True)
                sc.world = None
        finally:
            rig.restore()
            for _n, ob in objs + [(None, lodge)]:
                try:
                    me = ob.data
                    bpy.data.objects.remove(ob, do_unlink=True)
                    if me.users == 0:
                        bpy.data.meshes.remove(me)
                except ReferenceError:
                    pass
    finally:
        shutil.rmtree(scratch, ignore_errors=True)
    return written


# --------------------------------------------------------------------------------------
# Command line
# --------------------------------------------------------------------------------------

def parse_args(argv):
    ap = argparse.ArgumentParser(prog="fine_probes.py", description="Probes B (S1) generator")
    ap.add_argument("--out", default=None, help="letter folder, e.g. ../import/probes/B")
    ap.add_argument("--export", action="store_true", help="write the five asset folders (needs --out)")
    ap.add_argument("--preview", default=None, help="scale previews folder")
    ap.add_argument("--measure", action="store_true", help="print the wall / ground spot table and exit")
    ap.add_argument("--json", default=None, help="--measure: write the table here")
    return ap.parse_args(argv)


def main(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    a = parse_args(argv)
    clear_scene()
    if a.measure:
        res = measure_all()
        txt = json.dumps(res, indent=2)
        print(txt)
        if a.json:
            with open(a.json, "w", encoding="utf-8") as fh:
                fh.write(txt)
        print(f"[fine_probes] measure: {'OK' if res['ok'] else 'NOT OK'}")
        return 0 if res["ok"] else 1
    if a.export:
        if not a.out:
            raise SystemExit("--export needs --out")
        export(a.out, a.preview, args=argv)
        print("[fine_probes] export done")
        return 0
    raise SystemExit("nothing to do: --export --out <dir> or --measure")


if __name__ == "__main__":
    rc = main()
    if rc:
        sys.exit(rc)
