"""
test_see_through - no hole in a building's shell from any play camera (T273, the see-through gate).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_see_through.py --
        (--asset <dir> [--asset <dir> ...] | --root <letter dir>) [--fast] [--lods 0,1] [--out <dir>]
        [--allow tests/data/see_through_allow.json] [--self-test] [--images worst|all|none]

What it checks. CS2 draws building meshes single-sided: a face seen from its back is culled, and the pixel shows whatever
lies behind it (field fact, 1.0.4 reel: the ground showed through the L1 slot between the stone wall cap and the upper
wall). So a camera ray whose FIRST surface is a back face looks through the shell: into the building (another part shows
where the wall should be) or right through it (ground or sky shows). Intended openings (porch bays, railings, open
canopies, deck gaps) do not trip this: through them a ray meets front faces (posts, the wall behind, the ground).

Method (per LOD, per camera):
  1. Cycles CPU, 1 sample, no bounces: one emission material, green on front faces, red on back faces (Backfacing);
     the ground plane at grade blue; the world black. The `_Gls` glass mesh is left out (the game draws it
     transparent); `_Win` window meshes are opaque and drawn. Pixels are point samples (box filter 0.01 px).
  2. Red pixels = candidates; 8-connected clusters.
  3. Every cluster is confirmed with exact BVH ray casts (mathutils) through up to 12 of its pixels (center + 4
     sub-pixel offsets each): the first face hit must be a back face, ahead of the ground plane, and no front face may
     lie within 3 mm behind it (that is coplanar z-fighting, reported as "coplanar", never a see-through). The walk then
     goes on past back faces to what the player sees there: "sky", "ground" or the front face of another part.
  4. Border: up to 16 pixels of the 2-px ring around the cluster are cast too; their front faces are the parts that
     border the hole (connected component of the export mesh: its tile-sheet key from the UV, its bounding box).
  5. Size in 1080p pixels: cluster pixels x confirmed share x (render px angle / 1080p px angle)^2. CS2's vertical
     field of view is 45 deg (the PIPELINE screen rule 1,304 / d px/m at 1080p), so a 1080p pixel spans 45/1080 deg.
  6. Holes merge across views in 3D (single linkage, 1.5 m, on the first back-face hit point).
A hole FAILS when its largest view shows >= FAIL_PX 1080p pixels and no allowlist box (data file, per asset name without
its generation letter, boxes in the asset frame below) holds its gap point. Smaller holes are listed as MINOR.

Cameras (asset frame: meters, Z up, grade z = 0, the export's Y-up cm baked back; arrival face toward -Y, bearing 0 =
camera on the arrival side, +90 = camera on +X). Distance = from the footprint box edge along the bearing; the camera
height = horizontal distance to the box center x tan(elevation); the camera aims at the box center and its field covers
the whole box (resolution set so a pixel spans <= REF_TAN_PX, the 4K angle, capped at MAX_PX on the long side):
  LOD0 full: 20 / 45 / 70 m x 16 bearings x elevation 8 / 22 / 40 deg (22 deg = the reel's arrival view, +18 m at 45 m)
  LOD0 --fast: 20 / 45 m x 12 bearings x 10 / 22 / 40 deg
  LOD1: 150 m x 16 bearings (12 fast) x 22 / 40 deg (the far views where LOD1 can show)

Output (--out, default the scratch dir): <asset>_<lod>.json (every hole: class, size, views, gap xyz, bearing, the
parts that border it, what shows through), see_through.tsv (one row per hole), and marked images
<asset>_<lod>_h<k>.png for each FAIL / MINOR hole (left: the worst view, Workbench, back faces culled as in the game, the
hole in magenta and ringed; right: a 4x crop). Exit code 1 when any asset FAILS (or the self-test does).

--self-test (also the first step of every run unless --no-self-test): a scratch house with the L1 defect (a stone wall
with a cap whose top face is missing behind the cap, under a recessed upper wall) must FAIL with the hole at the slot;
the same house closed must PASS; an allowlist box over the slot must turn the FAIL into ALLOWED.
"""
import argparse
import json
import math
import os
import re
import sys
import tempfile
import time
import traceback

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.dirname(os.path.dirname(HERE))
DEFAULT_ALLOW = os.path.join(HERE, "tests", "data", "see_through_allow.json")

FOV_1080_DEG = 45.0                                   # CS2 vertical field of view (1,304 / d px/m at 1080p)
TAN_1080_PX = 2.0 * math.tan(math.radians(FOV_1080_DEG / 2)) / 1080.0
REF_TAN_PX = TAN_1080_PX / 2.0                        # analysis pixel: the 4K angle
MAX_PX = 2400                                         # long side cap per render
FAIL_PX = 2.0                                         # 1080p pixels: a hole a 1080p player can see
FAIL_THICK = 0.75                                     # 1080p pixels across: thinner is a shimmering crack (MINOR)
COPLANAR_M = 0.003
MERGE_M = 1.5
GROUND_EPS = 0.005
RINGS = {
    "full": dict(lod0=dict(dist=(20.0, 45.0, 70.0), bearings=16, elev=(8.0, 22.0, 40.0)),
                 lod1=dict(dist=(150.0,), bearings=16, elev=(22.0, 40.0))),
    "fast": dict(lod0=dict(dist=(20.0, 45.0), bearings=12, elev=(10.0, 22.0, 40.0)),
                 lod1=dict(dist=(150.0,), bearings=12, elev=(22.0, 40.0))),
}


# ------------------------------------------------------------------ scene
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def _import_fbx(path):
    before = set(bpy.data.objects)
    op = bpy.ops.wm.fbx_import if hasattr(bpy.ops.wm, "fbx_import") else bpy.ops.import_scene.fbx
    op(filepath=path)
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    if not new:
        raise RuntimeError(f"no mesh in {path}")
    return new


def _frame_matrix(objs):
    """cs2_asset.export_fbx bakes cm and Y-up into the vertices: scale back to meters and rotate Z-up (tex_review rule)."""
    bpy.context.view_layer.update()
    W = np.concatenate([np.array([(o.matrix_world @ v.co)[:] for v in o.data.vertices]) for o in objs])
    sc = 0.01 if float((W.max(0) - W.min(0)).max()) > 200.0 else 1.0
    return Matrix.Rotation(math.radians(90.0), 4, "X") @ Matrix.Scale(sc, 4)


def _bake(o, M):
    o.matrix_world = M @ o.matrix_world
    o.data.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)
    o.data.update()


def load_lod(asset_dir, name, lod):
    """(opaque mesh objects, glass objects) of one LOD, in the asset frame. LOD0 = <name>.fbx (+ _Win), glass = _Gls."""
    stem = name if lod == 0 else f"{name}_LOD{lod}"
    main = os.path.join(asset_dir, stem + ".fbx")
    if not os.path.exists(main):
        return None, None
    opaque = _import_fbx(main)
    M = _frame_matrix(opaque)
    for o in opaque:
        _bake(o, M)
    glass = []
    for sfx, bucket in (("Win", opaque), ("Gls", glass)):
        p = os.path.join(asset_dir, f"{stem}_{sfx}.fbx")
        if os.path.exists(p):
            for o in _import_fbx(p):
                _bake(o, M)
                bucket.append(o)
    return opaque, glass


# ------------------------------------------------------------------ mesh tables (BVH, components, keys)
class MeshTable:
    """World triangles of the opaque objects: BVH, per-face component id, normal, tile key, component boxes."""

    def __init__(self, objs, glass, sheet=None):
        verts, faces, uvs = [], [], []
        for o in objs:
            me = o.data
            off = len(verts)
            verts.extend(v.co.copy() for v in me.vertices)
            uvl = me.uv_layers[0].data if me.uv_layers else None
            for p in me.polygons:
                faces.append([off + i for i in p.vertices])
                if uvl is not None:
                    u = sum(uvl[li].uv[0] for li in p.loop_indices) / p.loop_total
                    v = sum(uvl[li].uv[1] for li in p.loop_indices) / p.loop_total
                    uvs.append((u, v))
                else:
                    uvs.append(None)
        self.verts, self.faces = verts, faces
        self.bvh = BVHTree.FromPolygons(verts, faces, all_triangles=False, epsilon=0.0)
        self.normals = []
        for f in faces:
            a, b, c = verts[f[0]], verts[f[1]], verts[f[2]]
            n = (b - a).cross(c - a)
            self.normals.append(n.normalized() if n.length > 0 else Vector((0, 0, 0)))
        # components (vertex-connected; FBX import keeps split verts at seams, so also weld by position)
        key_of = {}
        rep = list(range(len(verts)))

        def find(i):
            while rep[i] != i:
                rep[i] = rep[rep[i]]
                i = rep[i]
            return i

        for i, v in enumerate(verts):
            k = (round(v.x, 4), round(v.y, 4), round(v.z, 4))
            j = key_of.setdefault(k, i)
            if j != i:
                rep[find(i)] = find(j)
        for f in faces:
            r0 = find(f[0])
            for i in f[1:]:
                ri = find(i)
                if ri != r0:
                    rep[ri] = r0
        roots = {}
        self.comp = [roots.setdefault(find(f[0]), len(roots)) for f in faces]
        n = len(roots)
        lo = [[1e9] * 3 for _ in range(n)]
        hi = [[-1e9] * 3 for _ in range(n)]
        keys = [dict() for _ in range(n)]
        for fi, f in enumerate(faces):
            c = self.comp[fi]
            for i in f:
                for a in range(3):
                    lo[c][a] = min(lo[c][a], verts[i][a])
                    hi[c][a] = max(hi[c][a], verts[i][a])
            k = sheet_key(sheet, uvs[fi])
            keys[c][k] = keys[c].get(k, 0) + 1
        self.comp_box = [(tuple(round(x, 2) for x in lo[c]), tuple(round(x, 2) for x in hi[c])) for c in range(n)]
        self.comp_key = [max(keys[c].items(), key=lambda kv: kv[1])[0] if keys[c] else "-" for c in range(n)]
        all_v = np.array([v[:] for v in verts]) if verts else np.zeros((1, 3))
        self.lo, self.hi = all_v.min(0), all_v.max(0)
        gv, gf = [], []
        for o in glass or []:
            off = len(gv)
            gv.extend(v.co.copy() for v in o.data.vertices)
            gf.extend([off + i for i in p.vertices] for p in o.data.polygons)
        self.glass = BVHTree.FromPolygons(gv, gf, all_triangles=False) if gf else None

    def describe(self, c):
        lo, hi = self.comp_box[c]
        return (f"#{c} {self.comp_key[c]} x {lo[0]:.2f}..{hi[0]:.2f} y {lo[1]:.2f}..{hi[1]:.2f} "
                f"z {lo[2]:.2f}..{hi[2]:.2f}")


def load_sheet(asset_dir):
    """The family sheet layout (tiles, palette, strip rects) next to the asset folder, if any: UV -> key names."""
    parent = os.path.dirname(os.path.abspath(asset_dir))
    for d in sorted(os.listdir(parent)) if os.path.isdir(parent) else []:
        if d.startswith("_") and d.endswith(".aux"):
            for f in os.listdir(os.path.join(parent, d)):
                if f.endswith(".layout.json"):
                    with open(os.path.join(parent, d, f), encoding="utf-8") as fh:
                        lay = json.load(fh)
                    A = float(lay.get("A", 4096))
                    rects = []
                    for grp in ("tiles", "strip", "palette"):
                        for k, t in (lay.get(grp) or {}).items():
                            r = t.get("rect_px")
                            if r and len(r) == 3:
                                p = t.get("pad", 0)
                                rects.append((k, (r[0] - p) / A, (r[0] + r[2] + p) / A,
                                              (r[1] - p) / A, (r[1] + r[2] + p) / A))
                    return rects
    return None


def sheet_key(rects, uv):
    if not rects or uv is None:
        return "-"
    u, v = uv[0] % 1.0 if uv[0] > 1.0 else uv[0], uv[1]
    for k, u0, u1, v0, v1 in rects:
        if u0 <= u <= u1 and v0 <= v <= v1:
            return k
    return "-"


# ------------------------------------------------------------------ render rig
def _emission(nt, color):
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = (*color, 1.0)
    e.inputs["Strength"].default_value = 1.0
    return e


def _flag_material():
    m = bpy.data.materials.new("st_flag")
    try:
        m.use_nodes = True
    except (AttributeError, TypeError):
        pass
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(geo.outputs["Backfacing"], mix.inputs[0])
    nt.links.new(_emission(nt, (0.0, 1.0, 0.0)).outputs[0], mix.inputs[1])
    nt.links.new(_emission(nt, (1.0, 0.0, 0.0)).outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])
    return m


def _plain_material(name, color):
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except (AttributeError, TypeError):
        pass
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(_emission(nt, color).outputs[0], out.inputs["Surface"])
    return m


class Rig:
    def __init__(self, opaque, ground_z=0.0, tmpdir=None):
        sc = bpy.context.scene
        self.scene = sc
        self.tmp = tmpdir or tempfile.mkdtemp(prefix="seethru_")
        flag = _flag_material()
        for o in opaque:
            o.data.materials.clear()
            o.data.materials.append(flag)
            for p in o.data.polygons:
                p.material_index = 0
            o.color = (0.78, 0.74, 0.66, 1.0)
        bpy.ops.mesh.primitive_plane_add(size=4000.0, location=(0.0, 0.0, ground_z - GROUND_EPS))
        g = bpy.context.active_object
        g.name = "st_ground"
        g.data.materials.append(_plain_material("st_ground", (0.0, 0.0, 1.0)))
        g.color = (0.36, 0.42, 0.33, 1.0)
        self.ground_z = ground_z - GROUND_EPS
        w = bpy.data.worlds.new("st_world")
        try:
            w.use_nodes = True
            bg = w.node_tree.nodes.get("Background")
            if bg is not None:
                bg.inputs[0].default_value = (0.0, 0.0, 0.0, 1.0)
                bg.inputs[1].default_value = 0.0
        except (AttributeError, TypeError):
            pass
        w.color = (0.0, 0.0, 0.0)                     # Cycles without a world node tree draws this: keep it black
        sc.world = w
        cam_data = bpy.data.cameras.new("st_cam")
        cam_data.sensor_fit = "HORIZONTAL"
        cam_data.clip_start = 0.5
        cam_data.clip_end = 3000.0
        self.cam = bpy.data.objects.new("st_cam", cam_data)
        sc.collection.objects.link(self.cam)
        sc.camera = self.cam
        r = sc.render
        r.engine = "CYCLES"
        r.use_persistent_data = True
        r.resolution_percentage = 100
        r.image_settings.file_format = "PNG"
        r.image_settings.color_mode = "RGB"
        r.image_settings.color_depth = "8"
        r.image_settings.compression = 0
        r.dither_intensity = 0.0
        r.threads_mode = "FIXED"
        r.threads = max(1, min(12, (os.cpu_count() or 8) - 2))
        try:
            sc.view_settings.view_transform = "Standard"
            sc.view_settings.look = "None"
            sc.view_settings.exposure = 0.0
            sc.view_settings.gamma = 1.0
        except TypeError:
            pass
        cy = sc.cycles
        cy.device = "CPU"
        cy.samples = 1
        for k, v in (("use_adaptive_sampling", False), ("use_denoising", False), ("max_bounces", 0),
                     ("diffuse_bounces", 0), ("glossy_bounces", 0), ("transmission_bounces", 0),
                     ("volume_bounces", 0), ("transparent_max_bounces", 0), ("caustics_reflective", False),
                     ("caustics_refractive", False), ("pixel_filter_type", "BOX"), ("filter_width", 0.01),
                     ("seed", 0), ("use_animated_seed", False), ("sample_offset", 0)):
            try:
                setattr(cy, k, v)
            except (AttributeError, TypeError):
                pass

    def place(self, eye, target, box_pts):
        """Aim at target; set FOV / resolution so the box fits and a pixel spans <= REF_TAN_PX."""
        d = (target - eye).normalized()
        rot = d.to_track_quat("-Z", "Y").to_matrix().to_4x4()
        self.cam.matrix_world = Matrix.Translation(eye) @ rot
        inv = rot.inverted()
        tx = ty = 1e-3
        for p in box_pts:
            q = inv @ (p - eye)
            z = -q.z
            if z <= 0.05:
                continue
            tx, ty = max(tx, abs(q.x / z)), max(ty, abs(q.y / z))
        tx, ty = tx * 1.04, ty * 1.04
        tan_px = REF_TAN_PX
        rx = int(math.ceil(2 * tx / tan_px))
        ry = int(math.ceil(2 * ty / tan_px))
        if max(rx, ry) > MAX_PX:
            s = MAX_PX / max(rx, ry)
            rx, ry = max(8, int(rx * s)), max(8, int(ry * s))
        tan_px = 2 * tx / rx
        ty = ry * tan_px / 2
        self.cam.data.angle = 2 * math.atan(tx)
        r = self.scene.render
        r.resolution_x, r.resolution_y = rx, ry
        self.eye, self.rot = eye, rot
        self.tx, self.ty, self.rx, self.ry, self.tan_px = tx, ty, rx, ry, tan_px

    def ray(self, px, py):
        """World ray through image point (px from the left, py from the bottom, pixel units, centers at +0.5)."""
        u = (px / self.rx) * 2 - 1
        v = (py / self.ry) * 2 - 1
        d = self.rot @ Vector((u * self.tx, v * self.ty, -1.0))
        return self.eye.copy(), d.normalized()

    def render(self, tag):
        path = os.path.join(self.tmp, f"{tag}.png")
        self.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(path, check_existing=False)
        w, h = img.size
        a = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(a)
        bpy.data.images.remove(img)
        try:
            os.remove(path)
        except OSError:
            pass
        return a.reshape(h, w, 4)


# ------------------------------------------------------------------ analysis
def clusters(mask):
    """8-connected clusters of a bool image: list of (rows, cols) arrays."""
    ys, xs = np.nonzero(mask)
    if len(ys) == 0:
        return []
    h, w = mask.shape
    idx = -np.ones(mask.shape, dtype=np.int64)
    idx[ys, xs] = np.arange(len(ys))
    parent = list(range(len(ys)))

    def find(i):
        r = i
        while parent[r] != r:
            r = parent[r]
        while parent[i] != r:
            parent[i], i = r, parent[i]
        return r

    for dy, dx in ((0, 1), (1, 0), (1, 1), (1, -1)):
        if dx >= 0:
            a = idx[0:h - dy, 0:w - dx]
            b = idx[dy:h, dx:w]
        else:
            a = idx[0:h - dy, -dx:w]
            b = idx[dy:h, 0:w + dx]
        sel = (a >= 0) & (b >= 0)
        for i, j in zip(a[sel].tolist(), b[sel].tolist()):
            ri, rj = find(i), find(j)
            if ri != rj:
                parent[max(ri, rj)] = min(ri, rj)
    roots = np.array([find(i) for i in range(len(ys))])
    out = []
    for r in np.unique(roots):
        s = roots == r
        out.append((ys[s], xs[s]))
    out.sort(key=lambda c: -len(c[0]))
    return out


def _front_coplanar(T, o, d, t_hit, loc):
    """A front face (n . d < 0) that the ray crosses within COPLANAR_M of t_hit (z-fighting, not a hole)."""
    for (_, _, fi, _) in T.bvh.find_nearest_range(loc, COPLANAR_M + 1e-4):
        if fi is None:
            continue
        n = T.normals[fi]
        nd = n.dot(d)
        if nd >= 0.0:
            continue
        f = T.faces[fi]
        a = T.verts[f[0]]
        t = n.dot(a - o) / nd
        if abs(t - t_hit) > COPLANAR_M:
            continue
        p = o + d * t
        for k in range(1, len(f) - 1):                # fan triangles: barycentric inside test
            b, c = T.verts[f[k]], T.verts[f[k + 1]]
            v0, v1, v2 = c - a, b - a, p - a
            d00, d01, d02, d11, d12 = v0.dot(v0), v0.dot(v1), v0.dot(v2), v1.dot(v1), v1.dot(v2)
            den = d00 * d11 - d01 * d01
            if abs(den) < 1e-18:
                continue
            u = (d11 * d02 - d01 * d12) / den
            v = (d00 * d12 - d01 * d02) / den
            if u >= -1e-4 and v >= -1e-4 and u + v <= 1 + 1e-4:
                return fi
    return None


def walk(T, o, d, ground_z, max_steps=32):
    """Follow a ray: (first kind, first point, first face, seen kind, seen point, seen face, through_glass).
    first kind: "front" / "back" / "coplanar" / "ground" / "sky"; seen = the first FRONT surface (or ground / sky) past
    the back faces."""
    inf = float("inf")
    tg = (ground_z - o.z) / d.z if d.z < -1e-9 else inf
    first = None                                       # (loc, face, t, glass)
    t = 0.0
    p = o.copy()
    for _ in range(max_steps):
        loc, _, fi, dist = T.bvh.ray_cast(p, d, 5000.0)
        if loc is None or t + dist > tg:
            kind = "ground" if tg < inf else "sky"
            pt = o + d * tg if tg < inf else None
            if first is None:
                return kind, pt, None, kind, pt, None, False
            return "back", first[0], first[1], kind, pt, None, first[3]
        t_hit = t + dist
        front = T.normals[fi].dot(d) < 0.0
        if first is None:
            if front:
                return "front", loc, fi, "front", loc, fi, False
            glass = T.glass is not None and T.glass.ray_cast(o, d, t_hit)[0] is not None
            q = _front_coplanar(T, o, d, t_hit, loc)
            if q is not None:
                return "coplanar", loc, fi, "front", loc, q, glass
            first = (loc, fi, t_hit, glass)
        elif front:
            return "back", first[0], first[1], "front", loc, fi, first[3]
        t = t_hit + 1e-4
        p = o + d * t
    if first is None:
        return "sky", None, None, "sky", None, None, False
    return "back", first[0], first[1], "sky", None, None, first[3]


def analyze_view(rig, T, img, view, max_clusters=60):
    red = (img[:, :, 0] > 0.5) & (img[:, :, 1] < 0.5) & (img[:, :, 2] < 0.5)
    green = img[:, :, 1] > 0.5
    holes = []
    n_red = int(red.sum())
    if n_red == 0:
        return holes, 0
    scale = (rig.tan_px / TAN_1080_PX) ** 2
    for ys, xs in clusters(red)[:max_clusters]:
        n = len(ys)
        k = min(12, n)
        pick = np.linspace(0, n - 1, k).astype(int)
        conf, firsts, seen, comps_in, glass_n, copl = 0, [], {}, {}, 0, 0
        for i in pick:
            cx, cy = xs[i] + 0.5, ys[i] + 0.5
            got = None
            for ox, oy in ((0, 0), (0.25, 0.25), (-0.25, 0.25), (0.25, -0.25), (-0.25, -0.25)):
                o, d = rig.ray(cx + ox, cy + oy)
                r = walk(T, o, d, rig.ground_z)
                if r[0] == "back":
                    got = r
                    break
                if r[0] == "coplanar" and got is None:
                    got = r
            if got is None:
                continue
            if got[0] == "coplanar":
                copl += 1
                continue
            conf += 1
            firsts.append(got[1])
            comps_in[T.comp[got[2]]] = comps_in.get(T.comp[got[2]], 0) + 1
            sk = got[3] if got[3] != "front" else f"part {T.comp[got[5]]}"
            seen[sk] = seen.get(sk, 0) + 1
            glass_n += 1 if got[6] else 0
        if conf == 0:
            if copl:
                holes.append(dict(kind="coplanar", px=n, view=view))
            continue
        # border ring: 2 px around the cluster
        y0, y1 = max(0, ys.min() - 3), min(img.shape[0], ys.max() + 4)
        x0, x1 = max(0, xs.min() - 3), min(img.shape[1], xs.max() + 4)
        sub = np.zeros((y1 - y0, x1 - x0), dtype=bool)
        sub[ys - y0, xs - x0] = True
        dil = sub.copy()
        for _ in range(2):
            d2 = dil.copy()
            d2[1:, :] |= dil[:-1, :]
            d2[:-1, :] |= dil[1:, :]
            d2[:, 1:] |= dil[:, :-1]
            d2[:, :-1] |= dil[:, 1:]
            dil = d2
        ring = dil & ~sub & green[y0:y1, x0:x1]
        ry_, rx_ = np.nonzero(ring)
        border, gap_pts = {}, []
        if len(ry_):
            for i in np.linspace(0, len(ry_) - 1, min(16, len(ry_))).astype(int):
                o, d = rig.ray(rx_[i] + x0 + 0.5, ry_[i] + y0 + 0.5)
                r = walk(T, o, d, rig.ground_z)
                if r[0] == "front":
                    c = T.comp[r[2]]
                    border[c] = border.get(c, 0) + 1
                    gap_pts.append(r[1])
        inside = np.median(np.array([p[:] for p in firsts]), axis=0)
        gap = np.median(np.array([p[:] for p in gap_pts]), axis=0) if gap_pts else inside
        share = conf / len(pick)
        # thickness: pixels / length along the cluster's principal axis, in 1080p pixels
        if n >= 3:
            P = np.stack([xs, ys], 1).astype(float)
            P -= P.mean(0)
            ev, evec = np.linalg.eigh(np.cov(P.T) + np.eye(2) * 1e-9)
            proj = P @ evec[:, -1]
            length = proj.max() - proj.min() + 1.0
        else:
            length = float(n)
        lin = rig.tan_px / TAN_1080_PX
        px1080 = n * share * scale
        thick1080 = (n * share / length) * lin
        holes.append(dict(kind="hole", px=n, px1080=round(px1080, 2), thick1080=round(thick1080, 2),
                          solid=bool(px1080 >= FAIL_PX and thick1080 >= FAIL_THICK), share=round(share, 2),
                          inside=[round(float(x), 3) for x in inside], gap=[round(float(x), 3) for x in gap],
                          pts=[[round(x, 3) for x in p] for p in firsts],
                          border=border, inner=comps_in, seen=seen, glass=glass_n,
                          center_px=[float(xs.mean()), float(ys.mean())], pix=(ys, xs),
                          extent_px=[int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())], view=view))
    return holes, n_red


def merge(holes):
    """Single linkage in 3D over every confirmed back-face point (MERGE_M)."""
    hs = [h for h in holes if h["kind"] == "hole"]
    parent = list(range(len(hs)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    P = [np.array(h["pts"]) for h in hs]
    for i in range(len(hs)):
        for j in range(i + 1, len(hs)):
            if find(i) == find(j):
                continue
            a, b = P[i], P[j]
            dmin = np.sqrt(((a[:, None, :] - b[None, :, :]) ** 2).sum(-1)).min()
            if dmin <= MERGE_M:
                parent[find(j)] = find(i)
    groups = {}
    for i, h in enumerate(hs):
        groups.setdefault(find(i), []).append(h)
    out = []
    for g in groups.values():
        g.sort(key=lambda h: (-int(h["solid"]), -h["px1080"]))
        pts = np.concatenate([np.array(h["pts"]) for h in g])
        border, seen, inner = {}, {}, {}
        for h in g:
            for dct, src in ((border, h["border"]), (seen, h["seen"]), (inner, h["inner"])):
                for k, v in src.items():
                    dct[k] = dct.get(k, 0) + v
        out.append(dict(worst=g[0], views=len(g), solid_views=sum(h["solid"] for h in g), px1080=g[0]["px1080"],
                        thick1080=max(h["thick1080"] for h in g), gap=g[0]["gap"],
                        box=[[round(float(x), 2) for x in pts.min(0)], [round(float(x), 2) for x in pts.max(0)]],
                        border=border, seen=seen, inner=inner, glass=sum(h["glass"] for h in g),
                        bearings=sorted({h["view"]["bearing"] for h in g}),
                        dists=sorted({h["view"]["dist"] for h in g}), elevs=sorted({h["view"]["elev"] for h in g})))
    out.sort(key=lambda m: (-int(m["solid_views"] > 0), -m["px1080"]))
    return out


def base_name(name):
    """Asset name without its generation letter: SkiLodgeAFrameW -> SkiLodgeAFrame (the allowlist key)."""
    return re.sub(r"[A-Z]$", "", name)


def load_allow(path):
    if not path or not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    return data.get("assets", {})


def allowed(allow, name, lod, m):
    for e in allow.get(base_name(name), []):
        if "lods" in e and lod not in e["lods"]:
            continue
        lo, hi = e["box"][:3], e["box"][3:]
        g = m["gap"]
        if all(lo[a] <= g[a] <= hi[a] for a in range(3)):
            if not e.get("why"):
                raise RuntimeError(f"allowlist entry for {base_name(name)} has no 'why': {e}")
            return e["why"]
    return None


# ------------------------------------------------------------------ cameras
def cameras(T, ring, bearing0=0.0):
    lo, hi = T.lo.copy(), T.hi.copy()
    lo[2] = max(lo[2], 0.0)
    c = Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2))
    hx, hy = (hi[0] - lo[0]) / 2, (hi[1] - lo[1]) / 2
    box = [Vector((x, y, z)) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]
    out = []
    for dist in ring["dist"]:
        for b in range(ring["bearings"]):
            bearing = (bearing0 + 360.0 * b / ring["bearings"]) % 360.0
            a = math.radians(bearing)
            u = Vector((math.sin(a), -math.cos(a), 0.0))      # bearing 0 = -Y (arrival side), 90 = +X
            t_edge = min(hx / abs(u.x) if abs(u.x) > 1e-9 else 1e9, hy / abs(u.y) if abs(u.y) > 1e-9 else 1e9)
            R = t_edge + dist
            for el in ring["elev"]:
                eye = Vector((c.x + u.x * R, c.y + u.y * R, max(1.7, R * math.tan(math.radians(el)))))
                out.append((dict(dist=dist, bearing=round(bearing, 1), elev=el), eye, c, box))
    return out


# ------------------------------------------------------------------ marked image
def marked_image(rig, opaque, view_holes, path):
    """Workbench render of the same camera with back faces culled (the game's look), holes in magenta and ringed."""
    sc = rig.scene
    keep = sc.render.engine
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "OBJECT"
    sh.show_backface_culling = True
    sh.show_cavity = False
    sh.show_shadows = False
    try:
        sc.display.render_aa = "8"
    except TypeError:
        pass
    sc.world.color = (0.55, 0.68, 0.82)
    sc.view_settings.view_transform = "Standard"
    img = rig.render("wb")
    sc.render.engine = keep
    sc.world.color = (0.0, 0.0, 0.0)
    rgb = img[:, :, :3].copy()
    H, W = rgb.shape[:2]
    for h in view_holes:
        x0, y0, x1, y1 = h["extent_px"]
        ys, xs = h["pix"]
        rgb[ys, xs] = (1.0, 0.0, 1.0)
        cx, cy = h["center_px"]
        rad = max(18.0, 0.6 * math.hypot(x1 - x0, y1 - y0) + 10)
        th = np.linspace(0, 2 * math.pi, 720)
        for dr in (0, 1, 2):
            px = np.clip((cx + (rad + dr) * np.cos(th)).astype(int), 0, W - 1)
            py = np.clip((cy + (rad + dr) * np.sin(th)).astype(int), 0, H - 1)
            rgb[py, px] = (1.0, 0.0, 0.0)
    # left: full view scaled to <= 1200 px wide; right: 4x crop around the worst hole (or 2x when large)
    step = max(1, int(math.ceil(W / 1200)))
    left = rgb[::step, ::step]
    h0 = view_holes[0]
    cx, cy = (int(v) for v in h0["center_px"])
    half = 75
    ya, yb = max(0, cy - half), min(H, cy + half)
    xa, xb = max(0, cx - half), min(W, cx + half)
    crop = rgb[ya:yb, xa:xb]
    z = 4
    crop = np.repeat(np.repeat(crop, z, 0), z, 1)
    hh = max(left.shape[0], crop.shape[0])
    canvas = np.ones((hh, left.shape[1] + 8 + crop.shape[1], 3), dtype=np.float32)
    canvas[hh - left.shape[0]:, :left.shape[1]] = left
    canvas[hh - crop.shape[0]:, left.shape[1] + 8:] = crop
    out = bpy.data.images.new("st_mark", canvas.shape[1], canvas.shape[0], alpha=False)
    rgba = np.concatenate([canvas, np.ones(canvas.shape[:2] + (1,), dtype=np.float32)], axis=2)
    out.pixels.foreach_set(rgba.ravel())
    out.filepath_raw = path
    out.file_format = "PNG"
    out.save()
    bpy.data.images.remove(out)


# ------------------------------------------------------------------ one asset
def check_asset(asset_dir, name, lod, ring, allow, outdir, images="worst", opaque=None, glass=None, sheet=None,
                quiet=False, shots=None):
    t0 = time.time()
    if opaque is None:
        reset()
        opaque, glass = load_lod(asset_dir, name, lod)
        if opaque is None:
            return None
        sheet = load_sheet(asset_dir)
    T = MeshTable(opaque, glass, sheet)
    rig = Rig(opaque, ground_z=0.0)
    views = cameras(T, ring)
    holes, red_total, renders = [], 0, 0
    imgs = {}
    for i, (view, eye, tgt, box) in enumerate(views):
        rig.place(eye, tgt, box)
        view = dict(view, res=[rig.rx, rig.ry], tan_px=rig.tan_px, eye=[round(x, 2) for x in eye])
        img = rig.render(f"v{i}")
        renders += 1
        hs, nr = analyze_view(rig, T, img, dict(view, i=i))
        red_total += nr
        if any(h["kind"] == "hole" for h in hs):
            imgs[i] = (eye, tgt, box)
        holes.extend(hs)
    merged = merge(holes)
    copl = [h for h in holes if h["kind"] == "coplanar"]
    rows = []
    for k, m in enumerate(merged):
        why = allowed(allow, name, lod, m)
        cls = "ALLOWED" if why else ("FAIL" if m["solid_views"] > 0 else "MINOR")
        m["class"], m["allow_why"] = cls, why
        m["border_desc"] = [T.describe(c) for c, _ in sorted(m["border"].items(), key=lambda kv: -kv[1])[:4]]
        m["inner_desc"] = [T.describe(c) for c, _ in sorted(m["inner"].items(), key=lambda kv: -kv[1])[:2]]
        m["seen_desc"] = {(T.describe(int(s.split()[1])) if s.startswith("part ") else s): v
                          for s, v in m["seen"].items()}
        rows.append(m)
    verdict = "FAIL" if any(m["class"] == "FAIL" for m in rows) else "PASS"
    # marked images: each FAIL / MINOR hole's worst view (the first 12)
    if images != "none" and outdir:
        for k, m in enumerate(rows):
            if m["class"] == "ALLOWED" and images != "all":
                continue
            if images == "worst" and k >= 12:
                break
            eye, tgt, box = imgs[m["worst"]["view"]["i"]]
            rig.place(eye, tgt, box)
            path = os.path.join(outdir, f"{name}_lod{lod}_h{k + 1}.png")
            marked_image(rig, opaque, [m["worst"]], path)
            m["image"] = path
    # named shots (e.g. a reel camera): every confirmed hole of that one view, marked on one image
    shot_rows = []
    for (bearing, dist, elev, label) in shots or []:
        (view, eye, tgt, box), = cameras(T, dict(dist=(dist,), bearings=1, elev=(elev,)), bearing0=bearing)
        rig.place(eye, tgt, box)
        img = rig.render(f"s_{label}")
        hs, _ = analyze_view(rig, T, img, dict(view, i=-1))
        hs = [h for h in hs if h["kind"] == "hole"]
        for h in hs:
            P = np.array(h["pts"])
            best, bd = None, 1e9
            for k, m in enumerate(rows):
                lo, hi = np.array(m["box"][0]), np.array(m["box"][1])
                dd = float(np.sqrt((np.maximum(0, np.maximum(lo - P, P - hi)) ** 2).sum(1)).min())
                if dd < bd:
                    best, bd = k, dd
            h["matches"] = f"h{best + 1}" if best is not None and bd <= MERGE_M else "-"
        path = None
        if outdir and hs:
            path = os.path.join(outdir, f"{name}_lod{lod}_shot_{label}.png")
            marked_image(rig, opaque, sorted(hs, key=lambda h: -h["px1080"]), path)
        shot_rows.append(dict(label=label, bearing=bearing, dist=dist, elev=elev, image=path,
                              holes=[dict(matches=h["matches"], px1080=h["px1080"], thick1080=h["thick1080"],
                                          solid=h["solid"], gap=h["gap"]) for h in hs]))
        if not quiet:
            print(f"   shot {label} (bearing {bearing}, {dist} m, elev {elev}): "
                  + (", ".join(f"{h['matches']} {h['px1080']:.0f} px / {h['thick1080']:.1f} thick"
                               + (" SOLID" if h["solid"] else "") for h in hs) or "no hole")
                  + (f"  -> {path}" if path else ""))
    rep = dict(asset=name, lod=lod, dir=asset_dir, verdict=verdict, views=len(views), renders=renders,
               tris=len(T.faces), comps=len(T.comp_box), red_px=red_total, coplanar_clusters=len(copl),
               fail_px=FAIL_PX, fail_thick=FAIL_THICK, seconds=round(time.time() - t0, 1), shots=shot_rows,
               holes=[{k: v for k, v in m.items() if k not in ("worst",)} | {"worst_view": m["worst"]["view"],
                                                                               "worst_px": m["worst"]["px"]}
                      for m in rows])
    if outdir:
        with open(os.path.join(outdir, f"{name}_lod{lod}.json"), "w", encoding="utf-8") as fh:
            json.dump(rep, fh, indent=1, default=str)
    if not quiet:
        print(f"[see-through] {name} LOD{lod}: {verdict}  views {len(views)}  holes {len(rows)} "
              f"(FAIL {sum(m['class'] == 'FAIL' for m in rows)}, MINOR {sum(m['class'] == 'MINOR' for m in rows)}, "
              f"ALLOWED {sum(m['class'] == 'ALLOWED' for m in rows)})  coplanar clusters {len(copl)}  "
              f"{rep['seconds']} s")
        for k, m in enumerate(rows):
            wv = m["worst"]["view"]
            seen = sorted(m["seen_desc"].items(), key=lambda kv: -kv[1])[:4]
            print(f"   h{k + 1} {m['class']:7s} {m['px1080']:8.1f} px1080, {m['thick1080']:.1f} px thick  gap {m['gap']}"
                  f"  box {m['box']}  worst bearing {wv['bearing']} dist {wv['dist']} elev {wv['elev']}  "
                  f"views {m['views']} (solid {m['solid_views']})  glass {m['glass']}")
            print(f"        border: {m['border_desc']}")
            print(f"        seen through: {seen}  inner: {m['inner_desc']}"
                  + (f"  allow: {m['allow_why']}" if m['allow_why'] else ""))
    return rep


# ------------------------------------------------------------------ self-test
def _box(bm, lo, hi, skip=()):
    """Closed box faces with outward normals; skip names in ('top', 'bottom', '+x', '-x', '+y', '-y')."""
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    v = [bm.verts.new(p) for p in ((x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
                                     (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1))]
    quads = {"bottom": (0, 3, 2, 1), "top": (4, 5, 6, 7), "-y": (0, 1, 5, 4), "+y": (2, 3, 7, 6),
             "-x": (0, 4, 7, 3), "+x": (1, 2, 6, 5)}
    for k, q in quads.items():
        if k not in skip:
            bm.faces.new([v[i] for i in q])


def scratch_house(open_slot):
    """The L1 defect in miniature: a 12 x 8 m stone wall 0-3 m high with a 0.3 m cap, the upper wall set back 0.6 m,
    a gable roof above. open_slot: the stone wall is open on top behind the cap (the slot the owner saw)."""
    reset()
    bm = bmesh.new()
    # stone wall (shell 0.4 m thick on the front): top face missing between cap and upper wall when open
    _box(bm, (-6, -4, -0.5), (6, 4, 3.0), skip=("bottom", "top") if open_slot else ("bottom",))
    _box(bm, (-6.1, -4.1, 3.0), (6.1, -3.6, 3.15))          # cap on the front edge only
    _box(bm, (-5.4, -3.4, 3.0), (5.4, 3.4, 6.0), skip=("bottom",))   # upper wall, set back 0.6 m
    # roof slab
    _box(bm, (-6.0, -4.0, 6.0), (6.0, 4.0, 6.3))
    me = bpy.data.meshes.new("SkiScratchHouseT")
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("SkiScratchHouseT", me)
    bpy.context.scene.collection.objects.link(o)
    return [o]


def self_test(outdir):
    ring = dict(dist=(45.0,), bearings=8, elev=(22.0, 40.0))
    ok = True
    rep = check_asset(None, "SkiScratchHouseT", 0, ring, {}, outdir, images="worst",
                      opaque=scratch_house(True), glass=[], quiet=True)
    fails = [h for h in rep["holes"] if h["class"] == "FAIL"]
    in_slot = [h for h in fails if -6.2 <= h["gap"][0] <= 6.2 and -4.2 <= h["gap"][1] <= 4.2
               and 2.5 <= h["gap"][2] <= 3.6]
    print(f"[self-test] open slot: verdict {rep['verdict']}, FAIL holes {len(fails)}, at the slot {len(in_slot)}; "
          f"worst {fails[0]['px1080'] if fails else 0} px1080")
    ok &= rep["verdict"] == "FAIL" and len(in_slot) >= 1
    rep2 = check_asset(None, "SkiScratchHouseT", 0, ring, {}, outdir, images="none",
                       opaque=scratch_house(False), glass=[], quiet=True)
    print(f"[self-test] closed: verdict {rep2['verdict']}, holes {len(rep2['holes'])}")
    ok &= rep2["verdict"] == "PASS" and not rep2["holes"]
    allow = {"SkiScratchHouse": [{"box": [-7, -5, 2.0, 7, 5, 4.0], "why": "self-test: the slot is declared"}]}
    rep3 = check_asset(None, "SkiScratchHouseT", 0, ring, allow, outdir, images="none",
                       opaque=scratch_house(True), glass=[], quiet=True)
    print(f"[self-test] open slot, allowlisted: verdict {rep3['verdict']}, "
          f"classes {sorted({h['class'] for h in rep3['holes']})}")
    ok &= rep3["verdict"] == "PASS" and any(h["class"] == "ALLOWED" for h in rep3["holes"])
    print(f"[self-test] {'PASS' if ok else 'FAIL'}")
    return ok


# ------------------------------------------------------------------ main
def find_assets(root):
    out = []
    for d in sorted(os.listdir(root)):
        p = os.path.join(root, d)
        if os.path.isdir(p) and not d.startswith("_") and os.path.exists(os.path.join(p, d + ".fbx")):
            out.append((p, d))
    return out


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="test_see_through")
    ap.add_argument("--asset", action="append", default=[])
    ap.add_argument("--root", action="append", default=[])
    ap.add_argument("--fast", action="store_true")
    ap.add_argument("--lods", default="0,1")
    ap.add_argument("--out", default=None)
    ap.add_argument("--allow", default=DEFAULT_ALLOW)
    ap.add_argument("--images", default="worst", choices=("worst", "all", "none"))
    ap.add_argument("--self-test", action="store_true", help="run only the self-test")
    ap.add_argument("--no-self-test", action="store_true")
    ap.add_argument("--shot", action="append", default=[],
                    help="bearing:dist:elev[:label] - also mark every hole of this one LOD0 view (e.g. a reel camera)")
    a = ap.parse_args(argv)
    shots = []
    for s in a.shot:
        p = s.split(":")
        shots.append((float(p[0]), float(p[1]), float(p[2]), p[3] if len(p) > 3 else f"b{p[0]}_d{p[1]}_e{p[2]}"))
    a.shots = shots
    return a


def main():
    a = parse()
    t0 = time.time()
    outdir = a.out or tempfile.mkdtemp(prefix="see_through_")
    os.makedirs(outdir, exist_ok=True)
    print(f"[see-through] out {outdir}")
    ok = True
    if a.self_test or not a.no_self_test:
        ok &= self_test(outdir)
        if a.self_test:
            return 0 if ok else 1
    allow = load_allow(a.allow)
    ring = RINGS["fast" if a.fast else "full"]
    assets = [(os.path.abspath(p), os.path.basename(os.path.normpath(p))) for p in a.asset]
    for r in a.root:
        assets += find_assets(os.path.abspath(r))
    if not assets:
        print("[see-through] no assets (give --asset <dir> or --root <letter dir>)")
        return 1
    lods = [int(x) for x in a.lods.split(",") if x != ""]
    reps = []
    for path, name in assets:
        for lod in lods:
            try:
                rep = check_asset(path, name, lod, ring["lod0" if lod == 0 else "lod1"], allow, outdir, a.images,
                                  shots=a.shots if lod == 0 else None)
            except Exception:                         # noqa: BLE001 - an asset that cannot be checked fails
                traceback.print_exc()
                rep = dict(asset=name, lod=lod, verdict="ERROR", holes=[])
            if rep is not None:
                reps.append(rep)
    with open(os.path.join(outdir, "see_through.tsv"), "w", encoding="utf-8") as fh:
        fh.write("asset\tlod\tverdict\thole\tclass\tpx1080\tthick1080\tgap_xyz\tbox\tworst_bearing\tworst_dist\t"
                 "worst_elev\tviews\tsolid_views\tseen\tborder\timage\n")
        for r in reps:
            if not r["holes"]:
                fh.write(f"{r['asset']}\t{r['lod']}\t{r['verdict']}\t-\t-\t0\t0\t-\t-\t-\t-\t-\t0\t0\t-\t-\t-\n")
            for k, h in enumerate(r["holes"]):
                wv = h["worst_view"]
                seen = sorted(h["seen_desc"].items(), key=lambda kv: -kv[1])[:3]
                fh.write(f"{r['asset']}\t{r['lod']}\t{r['verdict']}\th{k + 1}\t{h['class']}\t{h['px1080']}\t"
                         f"{h['thick1080']}\t{h['gap']}\t{h['box']}\t{wv['bearing']}\t{wv['dist']}\t{wv['elev']}\t"
                         f"{h['views']}\t{h['solid_views']}\t{seen}\t{' | '.join(h['border_desc'])}\t"
                         f"{h.get('image', '-')}\n")
    bad = [r for r in reps if r["verdict"] != "PASS"]
    names = ", ".join("%s LOD%s %s" % (r["asset"], r["lod"], r["verdict"]) for r in bad) or "none failed"
    print(f"[see-through] {len(reps) - len(bad)}/{len(reps)} PASS, {time.time() - t0:.0f} s  ({names})")
    return 0 if ok and not bad else 1


if __name__ == "__main__":
    try:
        rc = main()
    except Exception:                                 # noqa: BLE001
        traceback.print_exc()
        rc = 1
    sys.stdout.flush()
    if rc:
        sys.exit(rc)
