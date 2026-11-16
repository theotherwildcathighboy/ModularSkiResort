"""
ski_bus_roof_decal.py - the coach's roof mark (T219, 2026-11-16): one text-free decal instead of the two "SKI BUS" roof rows.

Why: SkiCoachBus.Prefab carried SkiBusDecalA twice on the roof (x +-0.62, turned +-90 degrees about Y), each row reading from
its own side of the street. From any camera one of the two reads upside down (the owner's 2026-11-15 photo: "twice, mirrored
or upside down"). Text on a roof can only read one way up. A mark can read from every side: this decal is the two-peak mark
of the shipped launcher glyph (ski_launcher_glyph.cand_c, as on the side livery and the welcome sign) inside a cream
ring, its peaks pointing to the coach's front. The ring makes it a badge from any side (the bare peaks read as an arrowhead
from the side, critic pass 2026-11-16). The side rows keep "SKI BUS".

MODELED STAND-IN: no vanilla prop carries this mark. One mesh, SkiBusRoofDecalA, built and exported exactly like
SkiBusDecalA (ski_bus_decal.py, the import contract and frame notes there): the 12-tri projection box of the DefaultDecal
template, flat shaded, the top face's UVs = the art rect, projection along local game Y. The prefab row needs NO rotation:
local game +Z (the art's top) is the coach's front. Fit is adjusted in the prefab row without a reimport.

Texture: 1024^2 (power of two), the mark centered at PPM. BaseColor cream with alpha = coverage, Normal flat, MaskMap A
gloss (ski_bus_decal.paint_maps conventions). No ControlMask: a decal is never tinted.

--preview DIR renders the options on a labeled PROXY coach (a box with approximated roof units, NOT the vanilla Bus01 mesh)
from five game-space views, through the same game -> Blender frame map as the export, so a row that reads mirrored in the
game reads mirrored here: before (the shipped two roof rows), A (this mark), B (no roof decal), C (one text row).

CLI (headless only):
    blender --background --factory-startup --python-exit-code 1 --python ski_bus_roof_decal.py -- --out DIR [--preview DIR]
    --out DIR receives DIR/SkiBusRoofDecalA/ (FBX, three PNGs, settings.json). Exit 1 on any failure. The last line is PASS.
"""

import argparse
import math
import os
import sys
import traceback

import bpy
import numpy as np
from mathutils import Vector

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset              # noqa: E402
import cs2_signset as SS      # noqa: E402
import ski_bus_panel as P     # noqa: E402
import ski_launcher_glyph as G  # noqa: E402

NAME = "SkiBusRoofDecalA"
TEX = 1024
MARK_W = 1.50                 # m, the roundel's diameter (game x and z). The roof's flat crown is about 2.2 m of the 2.55 m body
RING_W = 0.10                 # m, the ring's stroke
PEAK_SHARE = 0.62             # the peaks' width as a share of the diameter
PEAK_DROP = 0.04              # m the peaks sit below the center (their mass is low, the eye wants them centered)
EDGE = 0.06                   # m of clear field around the mark inside the box
PPM = 560.0                   # px/m: (1.50 + 2 x 0.06) x 560 = 907 px of the 1024 sheet, mip-safe margin left
HALF_Y = 0.20                 # projection depth each way (ski_bus_decal.HALF_Y)
SMOOTH = 0.35                 # ski_bus_decal.SMOOTH

# the shipped coach (gamedata-mirror SkiCoachBus.Prefab ObjectSubObjects, game space: x right, y up, z = the coach's front)
SIDE_ROWS = [((1.27, 2.42, 0.0), (-0.5, -0.5, -0.5, 0.5)), ((-1.27, 2.42, 0.0), (-0.5, 0.5, 0.5, 0.5))]
ROOF_ROWS_SHIPPED = [((0.62, 3.2, 0.0), (0.0, -0.7071068, 0.0, 0.7071068)), ((-0.62, 3.2, 0.0), (0.0, 0.7071068, 0.0, 0.7071068))]
# z +0.6: the middle of the clear roof span between the two hatches. Measured on the owner's 2026-11-15 4K photo along the
# roof axis (about 100 px/m): front unit about z +4.0, hatches about +2.5 and -1.3, rear unit about -4.4. In-game photo sets it.
ROOF_ROW_A = ((0.0, 3.2, 0.6), (0.0, 0.0, 0.0, 1.0))
ROOF_ROW_C = ROOF_ROWS_SHIPPED[0]


def parse_args(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="ski_bus_roof_decal.py")
    ap.add_argument("--out", required=True, help="DIR/SkiBusRoofDecalA/ receives the asset folder")
    ap.add_argument("--preview", default=None, metavar="DIR", help="render the before / A / B / C option sheets into DIR")
    return ap.parse_args(argv)


# ======================================================================================
# Paint
# ======================================================================================

def mark_cov():
    """(coverage (h, w) rows bottom-up, half_x m, half_z m): a cream roundel, MARK_W across, with the two peaks inside,
    peaks toward +v (the front). The ring makes the badge rotation-neutral: the bare peaks, seen from the side, read as
    an arrowhead (critic pass 2026-11-16)."""
    peaks = peaks_cov(MARK_W * PEAK_SHARE)
    d = int(round(MARK_W * PPM))
    yy, xx = np.mgrid[0:d, 0:d].astype(np.float64)
    r = np.hypot(xx + 0.5 - d / 2.0, yy + 0.5 - d / 2.0)
    r_out, r_in = d / 2.0, d / 2.0 - RING_W * PPM
    ring = np.clip(r_out - r, 0.0, 1.0) * np.clip(r - r_in, 0.0, 1.0)
    cov = ring.astype(np.float32)
    ph, pw = peaks.shape
    y0 = int(round(d / 2.0 - ph / 2.0 - PEAK_DROP * PPM))        # optical center: the base sits a little low
    x0 = (d - pw) // 2
    cov[y0:y0 + ph, x0:x0 + pw] = np.maximum(cov[y0:y0 + ph, x0:x0 + pw], peaks)
    e = int(round(EDGE * PPM))
    out = np.zeros((d + 2 * e, d + 2 * e), np.float32)
    out[e:e + d, e:e + d] = cov
    return out, out.shape[1] / PPM / 2.0, out.shape[0] / PPM / 2.0


def peaks_cov(width_m):
    """The two peaks alone, width_m wide, rows bottom-up, cropped to the ink."""
    fill = G.cand_c()[0]
    if fill[0] != "fill":
        raise RuntimeError("ski_launcher_glyph.cand_c no longer starts with its peaks")
    pts = np.array(fill[1], np.float64)
    lo, hi = pts.min(0), pts.max(0)
    w_units, h_units = float(hi[0] - lo[0]), float(hi[1] - lo[1])
    k = (G.CANVAS - 8.0) / max(w_units, h_units)
    c = (lo + hi) / 2.0
    prims = [("fill", [tuple(v) for v in (pts - c) * k + G.CANVAS / 2.0])]
    m_per_unit = width_m / (w_units * k)
    s = int(round(G.CANVAS * m_per_unit * PPM))                   # the glyph canvas in px at PPM
    glyph = G.render_alpha(prims, s, ss=8)[::-1]                  # top-down -> bottom-up (row 0 = -v = the rear)
    rows = np.where(glyph.max(axis=1) > 0.0)[0]
    cols = np.where(glyph.max(axis=0) > 0.0)[0]
    return glyph[rows[0]:rows[-1] + 1, cols[0]:cols[-1] + 1].astype(np.float32)


def paint_maps(cov):
    h, w = cov.shape
    if h > TEX - 32 or w > TEX - 32:
        raise RuntimeError(f"mark {w} x {h} px does not fit the {TEX} sheet with a mip-safe margin")
    x0, y0 = (TEX - w) // 2, (TEX - h) // 2
    c = np.zeros((TEX, TEX), np.float32)
    c[y0:y0 + h, x0:x0 + w] = cov
    base = np.ones((TEX, TEX, 4), np.float32)
    base[..., :3] = SS._lin_to_srgb(P.bytes_to_lin(P.CREAM_BYTES))
    base[..., 3] = c
    nrm = np.zeros((TEX, TEX, 4), np.float32)
    nrm[..., :2] = 0.5
    nrm[..., 2:] = 1.0
    msk = np.zeros((TEX, TEX, 4), np.float32)
    msk[..., 3] = SMOOTH
    return {"BaseColor": (base, False), "Normal": (nrm, True), "MaskMap": (msk, True)}, (x0, y0, x0 + w, y0 + h)


# ======================================================================================
# Mesh (ski_bus_decal.build_mesh with this asset's half sizes)
# ======================================================================================

def build_box(name, rect, half_x, half_z, tex=TEX):
    u0, v0, u1, v1 = (t / float(tex) for t in rect)
    gx, gy, gz = half_x, HALF_Y, half_z
    verts = [(sx * gx, sy * gz, sz * gy) for sz in (-1, 1) for sy in (-1, 1) for sx in (-1, 1)]
    polys = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], polys)
    me.update()
    lay = me.uv_layers.new(name="UVMap")
    for li, loop in enumerate(me.loops):
        bx, by, _bz = verts[loop.vertex_index]
        # game x = -bx, game z = -by: u along game +X, v along game +Z
        lay.data[li].uv = (u0 + (u1 - u0) * (half_x - bx) / (2.0 * half_x), v0 + (v1 - v0) * (half_z - by) / (2.0 * half_z))
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def check_mesh(ob, rect, half_x):
    me = ob.data
    if len(me.polygons) != 6 or len(me.vertices) != 8:
        raise RuntimeError(f"{len(me.polygons)} faces, {len(me.vertices)} verts: want a box")
    for p in me.polygons:
        if p.normal.dot(p.center) <= 0.0:
            raise RuntimeError(f"a face winds inward at {tuple(p.center)}")
    lay = me.uv_layers["UVMap"].data
    tops = [p for p in me.polygons if p.normal.z > 0.5]
    if len(tops) != 1:
        raise RuntimeError(f"{len(tops)} top faces")
    uv = np.array([tuple(lay[li].uv) for li in tops[0].loop_indices]) * TEX
    got = (uv[:, 0].min(), uv[:, 1].min(), uv[:, 0].max(), uv[:, 1].max())
    if np.abs(np.array(got) - np.array(rect)).max() > 0.01:
        raise RuntimeError(f"top UV {got} is not the art rect {rect}")
    for li in tops[0].loop_indices:
        co = me.vertices[me.loops[li].vertex_index].co
        if (lay[li].uv.x > (rect[0] + rect[2]) / (2.0 * TEX)) != (co.x < 0.0):
            raise RuntimeError("u does not grow along game +X: the art would mirror")
        if (lay[li].uv.y > (rect[1] + rect[3]) / (2.0 * TEX)) != (co.y < 0.0):
            raise RuntimeError("v does not grow along game +Z: the peaks would point to the rear")
    return {"tris": sum(len(p.vertices) - 2 for p in me.polygons), "ppm": (rect[2] - rect[0]) / (2.0 * half_x)}


# ======================================================================================
# Preview (proxy only; nothing below reaches an FBX)
# ======================================================================================

def g2b(v):
    """game (x right, y up, z front) -> Blender authoring (the inverse of export_fbx + the game's x negation)."""
    return Vector((-v[0], -v[2], v[1]))


def qrot(q, v):
    x, y, z, w = q
    u = np.array([x, y, z])
    v = np.asarray(v, np.float64)
    return 2.0 * np.dot(u, v) * u + (w * w - np.dot(u, u)) * v + 2.0 * w * np.cross(u, v)


def mat_tex(name, png):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bs = nt.nodes["Principled BSDF"]
    tx = nt.nodes.new("ShaderNodeTexImage")
    tx.image = bpy.data.images.load(png, check_existing=True)
    tx.interpolation = "Cubic"
    nt.links.new(tx.outputs["Color"], bs.inputs["Base Color"])
    nt.links.new(tx.outputs["Alpha"], bs.inputs["Alpha"])
    bs.inputs["Roughness"].default_value = 0.6
    return m


def mat_flat(name, rgb, rough=0.55, emit=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bs = m.node_tree.nodes["Principled BSDF"]
    bs.inputs["Base Color"].default_value = (*rgb, 1.0)
    bs.inputs["Roughness"].default_value = rough
    if emit is not None:
        bs.inputs["Emission Color"].default_value = (*emit, 1.0)
        bs.inputs["Emission Strength"].default_value = 1.2
    return m


def game_box(name, lo, hi, mat, coll):
    """An axis-aligned box given in game space."""
    bl = [g2b(v) for v in ((lo[0], lo[1], lo[2]), (hi[0], hi[1], hi[2]))]
    mn = Vector((min(bl[0].x, bl[1].x), min(bl[0].y, bl[1].y), min(bl[0].z, bl[1].z)))
    mx = Vector((max(bl[0].x, bl[1].x), max(bl[0].y, bl[1].y), max(bl[0].z, bl[1].z)))
    me = bpy.data.meshes.new(name)
    vs = [(x, y, z) for z in (mn.z, mx.z) for y in (mn.y, mx.y) for x in (mn.x, mx.x)]
    me.from_pydata(vs, [], [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)])
    me.update()
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    return ob


def decal_face(name, src_ob, row, mat, coll, lift=0.004):
    """The decal box's top face (the art) placed by a prefab row (game pos, game quaternion), laid on the surface it
    projects onto: the face sits at the row's origin, pushed `lift` m out along the row's projection axis."""
    pos, q = row
    me_src = src_ob.data
    top = [p for p in me_src.polygons if p.normal.z > 0.5][0]
    lay = me_src.uv_layers["UVMap"].data
    vs, uvs = [], []
    for li in top.loop_indices:
        co = me_src.vertices[me_src.loops[li].vertex_index].co
        local_game = np.array([-co.x, 0.0, -co.y])                 # the face's plane through the row origin
        gpos = qrot(q, local_game) + np.array(pos) + qrot(q, np.array([0.0, lift, 0.0]))
        vs.append(tuple(g2b(gpos)))
        uvs.append(tuple(lay[li].uv))
    me = bpy.data.meshes.new(name)
    me.from_pydata(vs, [], [list(range(len(vs)))])
    me.update()
    ul = me.uv_layers.new(name="UVMap")
    for i, lp in enumerate(me.loops):
        ul.data[i].uv = uvs[lp.vertex_index]
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    return ob


def proxy_coach(coll):
    red = mat_flat("ProxyBody", (0.22, 0.025, 0.025), 0.45)
    glass = mat_flat("ProxyGlass", (0.03, 0.035, 0.04), 0.15)
    unit = mat_flat("ProxyRoofUnit", (0.62, 0.63, 0.64), 0.5)
    dark = mat_flat("ProxyDark", (0.02, 0.02, 0.02), 0.8)
    tail = mat_flat("ProxyTail", (0.5, 0.02, 0.02), 0.4, emit=(1.0, 0.05, 0.03))
    game_box("body", (-1.25, 0.40, -6.0), (1.25, 3.18, 6.0), red, coll)
    for sx in (-1, 1):
        game_box(f"glass{sx}", (sx * 1.255 - 0.004, 1.35, -4.6), (sx * 1.255 + 0.004, 2.65, 4.4), glass, coll)
    game_box("windshield", (-1.1, 1.0, 6.0), (1.1, 2.8, 6.01), glass, coll)
    game_box("tail_l", (-1.15, 0.9, -6.01), (-0.95, 1.2, -6.0), tail, coll)
    game_box("tail_r", (0.95, 0.9, -6.01), (1.15, 1.2, -6.0), tail, coll)
    game_box("roof_front_ac", (-0.80, 3.18, 3.30), (0.80, 3.42, 4.90), unit, coll)
    game_box("roof_rear_ac", (-0.90, 3.18, -5.60), (0.90, 3.46, -3.40), unit, coll)
    game_box("hatch_front", (-0.40, 3.18, 2.10), (0.40, 3.25, 2.90), unit, coll)     # positions: see ROOF_ROW_A
    game_box("hatch_mid", (-0.40, 3.18, -1.70), (0.40, 3.25, -0.90), unit, coll)
    for gz in (-4.2, 4.2):
        for sx in (-1, 1):
            game_box(f"wheel{gz}{sx}", (sx * 1.26 - 0.15, 0.0, gz - 0.5), (sx * 1.26 + 0.15, 1.0, gz + 0.5), dark, coll)


VIEWS = (   # (label, camera direction FROM the coach, game space, unnormalized)
    ("front-left, high", (-1.0, 1.25, 1.0)),
    ("rear-right, high", (1.0, 1.25, -1.0)),
    ("left side, high", (-1.0, 1.1, 0.15)),
    ("right side, high", (1.0, 1.1, -0.15)),
    ("top-down, front up", (0.0, 1.0, 0.001)),
    ("close: roof from the right rear", (1.0, 1.6, -0.6)),
)
CLOSE_ORTHO = 6.0   # m across the close view (the others: 15)


def render_option(label, rows_side, rows_roof, side_ob, side_mat, roof_ob, roof_mat, out_png, res=(560, 400)):
    sc = bpy.context.scene
    coll = bpy.data.collections.new("opt")
    sc.collection.children.link(coll)
    proxy_coach(coll)
    for i, r in enumerate(rows_side):
        decal_face(f"side{i}", side_ob, r, side_mat, coll)
    for i, (ob, mat, r) in enumerate(rows_roof):
        decal_face(f"roof{i}", ob, r, mat, coll)
    sun_d = bpy.data.lights.new("sun", "SUN")
    sun_d.energy = 3.5
    sun = bpy.data.objects.new("sun", sun_d)
    coll.objects.link(sun)
    sun.rotation_euler = (math.radians(40), 0.0, math.radians(30))
    tiles = []
    for vlabel, d in VIEWS:
        dv = g2b(np.array(d, np.float64) / np.linalg.norm(d))
        cam_d = bpy.data.cameras.new("cam")
        cam_d.type = "ORTHO"
        close = vlabel.startswith("close")
        cam_d.ortho_scale = CLOSE_ORTHO if close else 15.0
        cam = bpy.data.objects.new("cam", cam_d)
        coll.objects.link(cam)
        tgt = g2b((0.0, 3.2, 0.0)) if close else g2b((0.0, 1.8, 0.0))
        cam.location = tgt + dv * 60.0
        up = g2b((0.0, 0.0, 1.0)) if vlabel.startswith("top") else Vector((0, 0, 1))
        cam.rotation_euler = (-dv).to_track_quat("-Z", "Y").to_euler()
        if vlabel.startswith("top"):
            # screen up = the coach's front (game +z)
            fwd = (-dv).normalized()
            right = fwd.cross(up).normalized()
            upv = right.cross(fwd).normalized()
            from mathutils import Matrix
            m = Matrix((right, upv, -fwd)).transposed()
            cam.rotation_euler = m.to_euler()
        cam_d.clip_end = 500.0
        sc.camera = cam
        sc.render.resolution_x, sc.render.resolution_y = res
        p = out_png[:-4] + f"_{len(tiles)}.png"
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        tiles.append((vlabel, p))
        bpy.data.objects.remove(cam)
    # one sheet: five tiles in a row
    imgs = []
    for _vl, p in tiles:
        im = bpy.data.images.load(p)
        a = np.array(im.pixels[:], np.float32).reshape(res[1], res[0], 4)
        imgs.append(a)
        bpy.data.images.remove(im)
        os.remove(p)
    sheet = np.concatenate(imgs, axis=1)
    out = bpy.data.images.new(label, sheet.shape[1], sheet.shape[0], alpha=True)
    out.pixels[:] = sheet.reshape(-1)
    out.filepath_raw = out_png
    out.file_format = "PNG"
    out.save()
    bpy.data.images.remove(out)
    for o in list(coll.objects):
        bpy.data.objects.remove(o)
    bpy.data.collections.remove(coll)
    return [t[0] for t in tiles]


def preview(args, roof_ob, asset_dir):
    out = os.path.abspath(args.preview)
    os.makedirs(out, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "OPTIX"
        prefs.get_devices()
        for dv in prefs.devices:
            dv.use = True
        sc.cycles.device = "GPU"
    except Exception as e:  # noqa: BLE001 - CPU fallback
        print("GPU off:", e)
    sc.cycles.samples = 48
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "AgX"
    w = bpy.data.worlds.new("w")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.72, 1.0)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    sc.world = w
    # the shipped side / roof art: SkiBusDecalA's own sheet and box (ski_bus_decal), rebuilt here for its rect
    import ski_bus_decal as D
    cov, _layout = P.paint_strip(P.FACE)
    maps, rect = D.paint_maps(cov)
    side_png = os.path.join(out, "_SkiBusDecalA_BaseColor.png")
    cs2_asset._write_png_raw(side_png, "sideBC", maps["BaseColor"][0], D.TEX, False)
    side_ob = D.build_mesh(rect)
    side_mat = mat_tex("SideDecal", side_png)
    side_ob.hide_render = True      # the projection boxes themselves never render: only their art faces (decal_face)
    roof_ob.hide_render = True
    roof_mat = mat_tex("RoofMark", os.path.join(asset_dir, f"{NAME}_BaseColor.png"))
    opts = [
        ("before", "shipped 1.0.1: two SKI BUS roof rows", [(side_ob, side_mat, r) for r in ROOF_ROWS_SHIPPED]),
        ("A", "A (recommended): one roof roundel with the peaks", [(roof_ob, roof_mat, ROOF_ROW_A)]),
        ("B", "B: no roof decal (prefab only, no import)", []),
        ("C", "C: one SKI BUS roof row (reads upside down from the left)", [(side_ob, side_mat, ROOF_ROW_C)]),
    ]
    for key, label, roof in opts:
        p = os.path.join(out, f"T219_roof_{key}.png")
        views = render_option(label, SIDE_ROWS, roof, side_ob, side_mat, roof_ob, roof_mat, p)
        print(f"[roof] preview {key}: {p} views {views}")


def run(args):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    asset_dir = os.path.join(os.path.abspath(args.out), NAME)
    os.makedirs(asset_dir, exist_ok=True)
    cov, half_x, half_z = mark_cov()
    maps, rect = paint_maps(cov)
    for m, (arr, is_data) in maps.items():
        cs2_asset._write_png_raw(os.path.join(asset_dir, f"{NAME}_{m}.png"), f"{NAME}{m}", arr, TEX, is_data)
    ob = build_box(NAME, rect, half_x, half_z)
    geo = check_mesh(ob, rect, half_x)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    cs2_asset.write_settings(asset_dir, NAME, surface={"materialTemplate": "DefaultDecal"})
    cs2_asset.export_fbx(ob, os.path.join(asset_dir, f"{NAME}.fbx"))
    ink = float((cov > 0.5).mean())
    print(f"[roof] box {2 * half_x:.3f} x {2 * half_z:.3f} m (game x, z), rect {rect}, ink share {ink:.3f}")
    if args.preview is not None:
        preview(args, ob, asset_dir)
    print(f"PASS ski_bus_roof_decal: {NAME} {geo}, rect {rect}, mark {MARK_W} m wide")


if __name__ == "__main__":
    try:
        run(parse_args())
    except Exception:   # noqa: BLE001 - --python-exit-code 1 makes it a non-zero exit
        traceback.print_exc()
        raise
