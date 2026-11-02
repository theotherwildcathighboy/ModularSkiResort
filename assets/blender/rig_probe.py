"""
rig_probe.py - AlignRigProbeA: the first SKINNED mesh of this project (lift alignment, 2026-11-02).

A dev probe, not a shipped asset (no `Ski` prefix). It proves that an Editor import of our FBX yields a
RenderPrefab with ProceduralAnimationProperties whose bones the game animates (bullwheel spin, grip / train
pitch). Import steps and the prefab surgery: assets/import/probes/A/README.md.

Parts (authoring frame: meters, Z up, origin = ground center; game frame = (-x, z, -y), PIPELINE.md rule T):
  Root   static: base beam + wheel post + train post
  Wheel  a horizontal wheel (rim, hub, 4 spokes; spoke +X is the orange marker with a block on the rim),
         hub at WHEEL_HUB; meant to spin about its own local Y (game up), the vanilla AC fan pattern
  Train  a horizontal 1.5 m bar along authoring Y (game Z) with an orange arrow on the authoring -Y end
         (= game +Z, object forward) and a pivot pin; meant to be pitched about its local X by our code
Every bone points UP with roll 0, so its rest rotation is identity in the game frame (export_fbx_skinned).
LOD0 and LOD1 (fewer segments, no pin) are skinned to the same three bones.

    blender --background --factory-startup --python-exit-code 1 --python rig_probe.py -- --out <letter folder>

Writes <out>/AlignRigProbeA/ (FBX x2, the four 512 maps, settings.json with useProceduralAnimation), then
re-imports both FBX files in an empty scene, prints / writes the round-trip report and renders two previews
(rest; wheel 45 deg + train 20 deg) into --preview. Run from assets/blender (cwd).
"""
import argparse
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cs2_asset as CA  # noqa: E402

REPO = os.path.dirname(os.path.dirname(HERE))
NAME = "AlignRigProbeA"
RIG = NAME + "Rig"
DEFAULT_OUT = os.path.join(REPO, "assets", "import", "probes", "A")
DEFAULT_PREVIEW = os.path.join(REPO, "assets", "preview", "rig-probe-A-2026-11-02")

BONES = ("Root", "Wheel", "Train")
WHEEL_HUB = (-0.65, 0.0, 1.00)
WHEEL_R = 0.60            # rim outer radius (1.2 m wheel)
RIM_W = 0.07              # rim radial width
RIM_H = 0.06              # rim height
TRAIN_PIVOT = (0.55, 0.0, 1.10)
BAR_LEN = 1.50
BAR_W = 0.12
BONE_LEN = 0.25

# 2x2 atlas, 256 per quad = a 512 set (the texture floor). No recolor slots.
QUAD_SETS = {
    0: {"rgb": (0.55, 0.56, 0.58), "metal": 0.5, "smooth": 0.45},   # root: galvanized gray
    1: {"rgb": (0.12, 0.30, 0.70), "metal": 0.0, "smooth": 0.40},   # train bar: blue
    2: {"rgb": (0.95, 0.38, 0.05), "metal": 0.0, "smooth": 0.40},   # markers: orange
    3: {"rgb": (0.92, 0.86, 0.35), "metal": 0.0, "smooth": 0.40},   # wheel: pale yellow
}
LOD_SEGS = {0: {"rim": 32, "hub": 16, "post": 16, "pin": 12},
            1: {"rim": 8, "hub": 6, "post": 6, "pin": 0}}


def args_():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=DEFAULT_OUT, help="letter folder; asset folder = <out>/AlignRigProbeA")
    ap.add_argument("--preview", default=DEFAULT_PREVIEW)
    ap.add_argument("--no-verify", action="store_true", help="skip the round trip and the previews")
    ap.add_argument("--res", type=int, nargs=2, default=(960, 720))
    return ap.parse_args(argv)


# --------------------------------------------------------------------------------------
# Geometry (bmesh primitives, one object per part, one bone per part)
# --------------------------------------------------------------------------------------

def _obj_from_bm(name, bm):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def box(name, center, size):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0] + center[0], v.co.y * size[1] + center[1], v.co.z * size[2] + center[2]))
    return _obj_from_bm(name, bm)


def cylinder(name, center, radius, depth, segs, axis="Z"):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=radius, radius2=radius,
                          depth=depth)
    rot = {"Z": Matrix.Identity(3), "X": Matrix.Rotation(math.radians(90.0), 3, "Y")}[axis]
    for v in bm.verts:
        v.co = rot @ v.co + Vector(center)
    return _obj_from_bm(name, bm)


def ring(name, center, r_out, r_in, height, segs):
    """Annulus in the XY plane: outer / inner walls, top and bottom faces."""
    bm = bmesh.new()
    cx, cy, cz = center
    rows = []
    for i in range(segs):
        a = 2.0 * math.pi * i / segs
        c, s = math.cos(a), math.sin(a)
        rows.append([bm.verts.new((cx + r * c, cy + r * s, cz + z))
                     for r, z in ((r_out, -height / 2), (r_out, height / 2), (r_in, height / 2), (r_in, -height / 2))])
    for i in range(segs):
        a, b = rows[i], rows[(i + 1) % segs]
        for k in range(4):
            k2 = (k + 1) % 4
            bm.faces.new((a[k], b[k], b[k2], a[k2]))
    return _obj_from_bm(name, bm)


def arrow(name, x, y_tip, y_base, half_w, z0, z1):
    """Triangular prism pointing toward y_tip."""
    bm = bmesh.new()
    tri = ((x, y_tip), (x - half_w, y_base), (x + half_w, y_base))
    lo = [bm.verts.new((px, py, z0)) for px, py in tri]
    hi = [bm.verts.new((px, py, z1)) for px, py in tri]
    bm.faces.new(lo)
    bm.faces.new(hi)
    for i in range(3):
        j = (i + 1) % 3
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    return _obj_from_bm(name, bm)


def build_parts(level):
    """[(object, bone, quad)] for one LOD level."""
    seg = LOD_SEGS[level]
    hx, hy, hz = WHEEL_HUB
    tx, ty, tz = TRAIN_PIVOT
    bar_top = tz + BAR_W / 2
    parts = [
        # Root: base beam on the ground, two posts
        (box("base", (-0.05, 0.0, 0.05), (1.50, 0.25, 0.10)), "Root", 0),
        (cylinder("wheelpost", (hx, hy, (0.10 + 0.96) / 2), 0.05, 0.86, seg["post"]), "Root", 0),
        (box("trainpost", (tx, ty, (0.10 + tz - BAR_W / 2 - 0.01) / 2), (0.10, 0.10, tz - BAR_W / 2 - 0.01 - 0.10)),
         "Root", 0),
        # Wheel: rim, hub, spokes (+X = the orange marker spoke), marker block on the rim
        (ring("rim", WHEEL_HUB, WHEEL_R, WHEEL_R - RIM_W, RIM_H, seg["rim"]), "Wheel", 3),
        (cylinder("hub", (hx, hy, hz + 0.015), 0.09, 0.11, seg["hub"]), "Wheel", 3),
    ]
    spoke_len = WHEEL_R - RIM_W / 2 - 0.06
    mid = 0.06 + spoke_len / 2
    for k, (dx, dy) in enumerate(((1, 0), (0, 1), (-1, 0), (0, -1))):
        marker = k == 0
        size = (spoke_len, 0.05, 0.06 if marker else 0.04) if dx else (0.05, spoke_len, 0.04)
        parts.append((box(f"spoke{k}", (hx + dx * mid, hy + dy * mid, hz + (0.01 if marker else 0.0)), size),
                      "Wheel", 2 if marker else 3))
    parts.append((box("markerblock", (hx + WHEEL_R - RIM_W / 2, hy, hz + 0.05), (0.14, 0.12, 0.06)), "Wheel", 2))
    # Train: bar along Y, arrow on the -Y end (game +Z), pivot pin across X
    parts.append((box("bar", TRAIN_PIVOT, (BAR_W, BAR_LEN, BAR_W)), "Train", 1))
    parts.append((arrow("arrow", tx, ty - BAR_LEN / 2 - 0.08, ty - BAR_LEN / 2 + 0.22, 0.11, bar_top,
                        bar_top + 0.03), "Train", 2))
    if seg["pin"]:
        parts.append((cylinder("pin", TRAIN_PIVOT, 0.03, BAR_W + 0.10, seg["pin"], axis="X"), "Train", 0))
    return parts


def build_mesh(level, name):
    parts = build_parts(level)
    for ob, bone, quad in parts:
        CA.unwrap_into_quadrant(ob, quad)
        vg = ob.vertex_groups.new(name=bone)
        vg.add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
    bpy.ops.object.select_all(action="DESELECT")
    for ob, _, _ in parts:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = ob.data.name = name
    CA.shade_by_angle(ob, 66.0, weighted=(level == 0))
    return ob


def build_rig():
    arm_data = bpy.data.armatures.new(RIG)
    arm = bpy.data.objects.new(RIG, arm_data)
    bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    heads = {"Root": (0.0, 0.0, 0.0), "Wheel": WHEEL_HUB, "Train": TRAIN_PIVOT}
    root = None
    for b in BONES:
        eb = arm_data.edit_bones.new(b)
        eb.head = heads[b]
        eb.tail = (heads[b][0], heads[b][1], heads[b][2] + BONE_LEN)
        eb.roll = 0.0
        eb.use_deform = True
        if b == "Root":
            root = eb
        else:
            eb.parent = root
            eb.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    for b in arm_data.bones:
        q = b.matrix_local.to_quaternion()
        # bone along +Z with roll 0: local X = +X, local Y = +Z (rotation +90 deg about X)
        want = Matrix.Rotation(math.radians(90.0), 4, "X").to_quaternion()
        if q.rotation_difference(want).angle > 1e-4:
            raise RuntimeError(f"bone {b.name}: rest {tuple(q)} is not the +90 X authoring frame")
    return arm


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


# --------------------------------------------------------------------------------------
# Round trip + previews
# --------------------------------------------------------------------------------------

def _import(path):
    before = set(bpy.data.objects.keys())
    bpy.ops.wm.fbx_import(filepath=path)
    return [bpy.data.objects[n] for n in bpy.data.objects.keys() if n not in before]


def roundtrip(path):
    """Re-import one FBX into the current (empty) scene; report rig + weights in the RAW file frame."""
    objs = _import(path)
    arms = [o for o in objs if o.type == "ARMATURE"]
    meshes = [o for o in objs if o.type == "MESH"]
    rep = {"file": os.path.basename(path), "objects": sorted(f"{o.type}:{o.name}" for o in objs)}
    if len(arms) != 1 or len(meshes) != 1:
        raise RuntimeError(f"{path}: expected 1 armature + 1 mesh, got {rep['objects']}")
    arm, me_ob = arms[0], meshes[0]
    rep["armature_matrix_world"] = [list(map(lambda x: round(x, 5), r)) for r in arm.matrix_world]
    rep["mesh_parent"] = me_ob.parent.name if me_ob.parent else None
    rep["mesh_modifiers"] = [(m.type, getattr(getattr(m, "object", None), "name", None)) for m in me_ob.modifiers]
    to_raw = arm.matrix_world  # importer frame -> world; the raw file frame is what the game reads
    bones = []
    for b in arm.data.bones:
        m = to_raw @ b.matrix_local
        head = m.to_translation()
        parent_m = (to_raw @ b.parent.matrix_local) if b.parent else Matrix.Identity(4)
        local = parent_m.inverted() @ m
        q = local.to_quaternion()
        lp = local.to_translation()
        bones.append({
            "name": b.name, "parent": b.parent.name if b.parent else None,
            "head_world": [round(x, 3) for x in head],
            "local_pos": [round(x, 3) for x in lp],
            "local_rot_xyzw": [round(q.x, 5), round(q.y, 5), round(q.z, 5), round(q.w, 5)],
            "world_rot_xyzw": [round(c, 5) for c in (m.to_quaternion().x, m.to_quaternion().y,
                                                      m.to_quaternion().z, m.to_quaternion().w)],
        })
    rep["bones"] = bones
    me = me_ob.data
    names = {vg.index: vg.name for vg in me_ob.vertex_groups}
    groups = {n: {"verts": 0, "wmin": 1e9, "wmax": 0.0, "bbox": None} for n in names.values()}
    unweighted, multi = 0, 0
    mw = me_ob.matrix_world
    for v in me.vertices:
        ws = [(names[g.group], g.weight) for g in v.groups if g.weight > 0]
        if not ws:
            unweighted += 1
        if len(ws) > 1:
            multi += 1
        co = mw @ v.co
        for n, w in ws:
            g = groups[n]
            g["verts"] += 1
            g["wmin"] = min(g["wmin"], w)
            g["wmax"] = max(g["wmax"], w)
            bb = g["bbox"] or [list(co), list(co)]
            g["bbox"] = [[min(a, c) for a, c in zip(bb[0], co)], [max(a, c) for a, c in zip(bb[1], co)]]
    for g in groups.values():
        if g["bbox"]:
            g["bbox"] = [[round(x, 2) for x in g["bbox"][0]], [round(x, 2) for x in g["bbox"][1]]]
    rep["mesh"] = {"name": me_ob.name, "verts": len(me.vertices), "tris": tri_count(me_ob),
                   "unweighted_verts": unweighted, "multi_bone_verts": multi, "groups": groups}
    return rep, arm, me_ob


def _look_at(cam, target):
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def render_previews(asset_dir, preview_dir, res):
    """LOD0 from the exported FBX, displayed in the authoring frame; rest and posed."""
    os.makedirs(preview_dir, exist_ok=True)
    objs = _import(os.path.join(asset_dir, f"{NAME}.fbx"))
    arm = next(o for o in objs if o.type == "ARMATURE")
    me_ob = next(o for o in objs if o.type == "MESH")
    if any(abs(a - b) > 1e-6 for r1, r2 in zip(arm.matrix_world, Matrix.Identity(4)) for a, b in zip(r1, r2)):
        raise RuntimeError(f"importer moved the armature node: {arm.matrix_world}")
    # raw file frame (cm, Y up) -> authoring frame (m, Z up): the inverse of the export bake
    arm.matrix_world = Matrix.Rotation(math.radians(90.0), 4, "X") @ Matrix.Scale(0.01, 4)
    CA.preview_material(me_ob, asset_dir, NAME)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    if sc.world is None:
        sc.world = bpy.data.worlds.new("World")
    sc.world.color = (0.78, 0.80, 0.84)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    sc.collection.objects.link(cam)
    cam.location = (2.9, -3.4, 2.7)
    _look_at(cam, (-0.05, 0.0, 0.75))
    cam.data.lens = 40
    sc.camera = cam
    ground = bpy.data.objects.new("Ground", bpy.data.meshes.new("Ground"))
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=2.5)
    bm.to_mesh(ground.data)
    bm.free()
    sc.collection.objects.link(ground)

    inv = arm.matrix_world.inverted()
    up = (inv.to_3x3() @ Vector((0, 0, 1))).normalized()
    lateral = (inv.to_3x3() @ Vector((1, 0, 0))).normalized()

    def pose(wheel_deg, train_deg):
        for b, axis, deg in (("Wheel", up, wheel_deg), ("Train", lateral, train_deg)):
            rest = arm.data.bones[b].matrix_local
            h = rest.to_translation()
            r = Matrix.Translation(h) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-h)
            arm.pose.bones[b].matrix = r @ rest
        bpy.context.view_layer.update()

    out = {}
    probe_v = {}
    for tag, wd, td in (("rest", 0.0, 0.0), ("wheel45-train20", 45.0, 20.0)):
        pose(wd, td)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = me_ob.evaluated_get(dg)
        co = [ev.matrix_world @ v.co for v in ev.data.vertices]
        probe_v[tag] = co
        path = os.path.join(preview_dir, f"{NAME}_{tag}.png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        out[tag] = path
    # which vertices moved, by bone group: proves the skin, not just the render
    names = {vg.index: vg.name for vg in me_ob.vertex_groups}
    moved = {}
    for v, a, b in zip(me_ob.data.vertices, probe_v["rest"], probe_v["wheel45-train20"]):
        g = next((names[x.group] for x in v.groups if x.weight > 0), None)
        d = (a - b).length
        m = moved.setdefault(g, [0, 0, 0.0])
        m[0] += 1
        m[1] += d > 1e-3
        m[2] = max(m[2], d)
    return out, {g: {"verts": m[0], "moved": m[1], "max_move_m": round(m[2], 3)} for g, m in moved.items()}


# --------------------------------------------------------------------------------------

def main():
    a = args_()
    CA.validate_asset_name(NAME)
    asset_dir = os.path.join(os.path.abspath(a.out), NAME)
    os.makedirs(asset_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)

    rig = build_rig()
    lod0 = build_mesh(0, NAME)
    lod1 = build_mesh(1, f"{NAME}_LOD1")
    t0, t1 = tri_count(lod0), tri_count(lod1)
    if t1 > 0.5 * t0:
        raise RuntimeError(f"LOD1 {t1} tris > 0.5 x LOD0 {t0}")
    for ob in (lod0, lod1):
        lo = [min(v.co[i] for v in ob.data.vertices) for i in range(3)]
        hi = [max(v.co[i] for v in ob.data.vertices) for i in range(3)]
        print(f"[rig] {ob.name}: {tri_count(ob)} tris, {len(ob.data.vertices)} verts, extent "
              f"{hi[0] - lo[0]:.2f} x {hi[1] - lo[1]:.2f} x {hi[2] - lo[2]:.2f} m, weights "
              f"{CA.check_skin_weights(ob, rig)}")

    CA.make_texture_set_atlas(asset_dir, NAME, QUAD_SETS, quad_size=256)
    mat = CA.assign_material(lod0, NAME, asset_dir)
    lod1.data.materials.clear()
    lod1.data.materials.append(mat)
    CA.export_fbx_skinned(lod0, rig, os.path.join(asset_dir, f"{NAME}.fbx"))
    CA.export_fbx_skinned(lod1, rig, os.path.join(asset_dir, f"{NAME}_LOD1.fbx"))
    CA.write_settings(asset_dir, NAME, supplied_lods=(1,), procedural_animation=True)
    print(f"[rig] exported {sorted(os.listdir(asset_dir))} to {asset_dir}")
    if a.no_verify:
        return

    report = {"asset": NAME, "lods": []}
    for fname in (f"{NAME}.fbx", f"{NAME}_LOD1.fbx"):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        rep, _, _ = roundtrip(os.path.join(asset_dir, fname))
        if rep["mesh"]["unweighted_verts"]:
            raise RuntimeError(f"{fname}: {rep['mesh']['unweighted_verts']} unweighted vertices after the round trip")
        if sorted(b["name"] for b in rep["bones"]) != sorted(BONES):
            raise RuntimeError(f"{fname}: bones {[b['name'] for b in rep['bones']]} != {BONES}")
        report["lods"].append(rep)
        print(f"[roundtrip] {json.dumps(rep, indent=1)}")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    pngs, moved = render_previews(asset_dir, a.preview, tuple(a.res))
    report["previews"] = pngs
    report["moved_by_pose"] = moved
    print(f"[preview] {pngs}")
    print(f"[preview] moved by the pose (wheel 45, train 20): {moved}")
    if moved.get("Root", {}).get("moved") or not moved.get("Wheel", {}).get("moved") \
            or not moved.get("Train", {}).get("moved"):
        raise RuntimeError(f"skin check failed: {moved}")
    os.makedirs(a.preview, exist_ok=True)
    with open(os.path.join(a.preview, f"{NAME}_roundtrip.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=1)


if __name__ == "__main__":
    main()
