"""
carrier_split_preview.py - pose previews of the split carriers (grip + body, 2026-11-03).

Plan: docs/research/carrier-pivot-swing-plan-2026-11-03.md; brief: docs/research/art/carrier-split-2026-11/
BRIEF.md (critic flow step 3). Run headless after the four split builds:

    blender --background --factory-startup --python-exit-code 1 --python carrier_split_preview.py -- \\
        [--carrier tr dq gondola tram] [--out assets/preview/carrier-split]

Re-imports each pair's EXPORTED LOD0 FBXs (assets/import/chairs/N, assets/import/cabins/B) with their finished
maps, assembles them from the body manifest's pivot numbers (the constants the code reads) and renders, per
carrier, into --out:

    <key>_zero_3q / _zero_side       the assembled carrier at zero (three-quarter; side = along the pin axis)
    <key>_pitch+30_side / -30_side   the grip pitched +-30 deg about its rope point (the rope slope; + = nose
                                     down), the body hanging PLUMB from the moved pin
    <key>_swing8_front               max cross-wind sway: grip + body rolled TOGETHER about the rope point
    (tram: _swing2_front)            (carrier_split.ASSEMBLY_ROLL_DEG 8 deg; tram TRAM_ROLL_DEG 2 deg about the
                                     track rope); nothing turns at the pin (swing convention, critique
                                     2026-11-03: the pin does pitch + fore-aft only)
    <key>_joint_zero / _joint_pitch+30   close-ups of the joint (tram: the -y lug)
    <key>_grip / <key>_body          each mesh alone

The FBX raw frame is the export bake undone by hand (cm, Y up: (X, Y, Z) -> (X, -Z, Y) / 100 = the net frame:
x lateral = authoring y, y = -travel, z up); the pivots are authoring (x travel, y lateral, z up) and are mapped
the same way. Renders only: nothing is exported, nothing goes near the game.
"""

import argparse
import json
import math
import os
import sys

import bpy
import mathutils
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import carrier_split as CS  # noqa: E402
import cs2_preview  # noqa: E402

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
DEFAULT_OUT = os.path.join(REPO, "assets", "preview", "carrier-split")

#: key -> (body, grip, letter folder, close-up half-size m, tint source)
CARRIERS = {
    "tr": ("SkiLiftChair6N", "SkiLiftChair6GripN", os.path.join("chairs", "N"), 0.20, ("chair", "tr")),
    "dq": ("SkiLiftChairDQ4N", "SkiLiftChairDQ4GripN", os.path.join("chairs", "N"), 0.25, ("chair", "dq")),
    "gondola": ("SkiGondolaCabin8B", "SkiGondolaCabin8GripB", os.path.join("cabins", "B"), 0.28,
                ("cabin", "gondola")),
    "tram": ("SkiTramCabinB", "SkiTramCarriageB", os.path.join("cabins", "B"), 0.45, ("cabin", "tram")),
}
PITCH_DEG = 30.0
RES = (1000, 1200)
RES_CLOSE = (1100, 1000)
#: net-frame camera directions (net x = authoring y lateral, net -y = travel forward): the outboard side
#: (authoring -Y = net -x) from the front
VIEW_3Q = (-1.0, -1.2, 0.55)
VIEW_SIDE = (-1.0, 0.0, 0.12)
VIEW_FRONT = (0.0, -1.0, 0.12)
VIEW_CLOSE = (-1.0, -0.8, 0.35)

# authoring (x travel, y lateral, z up) -> net (x = y, y = -x, z)
A2N = Matrix(((0.0, 1.0, 0.0, 0.0), (-1.0, 0.0, 0.0, 0.0), (0.0, 0.0, 1.0, 0.0), (0.0, 0.0, 0.0, 1.0)))


def a2n(p):
    return Vector((float(p[1]), -float(p[0]), float(p[2])))


def tint_for(src):
    kind, key = src
    if kind == "chair":
        import lift_chair
        return dict(lift_chair.STYLES[key].tint)
    import lift_cabin
    s = (lift_cabin.G_SETS if key == "gondola" else lift_cabin.T_SETS)[0]
    return {"R": s[1], "G": s[2], "B": s[3]}


def import_raw(fbx_path, name):
    """Import an exported FBX and place it in the net frame (meters) through matrix_world only (the data
    and its custom normals stay as exported)."""
    before = set(bpy.data.objects)
    op = bpy.ops.wm.fbx_import if hasattr(bpy.ops.wm, "fbx_import") else bpy.ops.import_scene.fbx
    op(filepath=fbx_path)
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    if len(new) != 1:
        raise RuntimeError(f"{fbx_path}: expected one mesh, got {len(new)}")
    o = new[0]
    o.name = name
    o.parent = None
    bake_inv = Matrix(((0.01, 0.0, 0.0, 0.0), (0.0, 0.0, -0.01, 0.0), (0.0, 0.01, 0.0, 0.0),
                       (0.0, 0.0, 0.0, 1.0)))
    o["bake_inv"] = [list(r) for r in bake_inv]
    o.matrix_world = bake_inv
    return o


def place(o, pose):
    """world = pose (net frame) @ the bake inverse."""
    o.matrix_world = pose @ Matrix([list(r) for r in o["bake_inv"]])


def z_range(o):
    zs = [(o.matrix_world @ v.co).z for v in o.data.vertices]
    return min(zs), max(zs)


def rot_about(axis, deg, center_net):
    """Rotation by deg about a NET-frame axis through center_net."""
    c = Vector(center_net)
    return Matrix.Translation(c) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-c)


def load_pair(key, root):
    body, grip, sub, half, tsrc = CARRIERS[key]
    bdir = os.path.join(root, sub, body)
    gdir = os.path.join(root, sub, grip)
    with open(os.path.join(bdir, f"{body}.manifest.json"), encoding="utf-8") as fh:
        man = json.load(fh)
    sp = man["split"]
    piv = sp.get("pivots") or sp.get("pivot")
    gz0 = float(piv["grip_origin_in_assembled_m"][2])
    pin = [float(c) for c in piv["body_pin_center_m"]["authoring"]]
    rope_g = [float(c) for c in piv["grip_rope_point_m"]["authoring"]]
    rope = (rope_g[0], rope_g[1], rope_g[2] + gz0)
    lug_y = piv.get("pin_lugs_lateral_m")
    # tram: the -y lug, the one facing the outboard-side camera (the +y lug hides behind the carriage)
    close = (pin[0], lug_y[0], pin[2]) if lug_y else tuple(pin)
    b = import_raw(os.path.join(bdir, f"{body}.fbx"), body)
    g = import_raw(os.path.join(gdir, f"{grip}.fbx"), grip)
    b0, b1 = z_range(b)
    g0, g1 = z_range(g)
    hang = float(piv["hang_m"])
    if abs(b0) > 1e-3 or abs(g0) > 1e-3 or abs(g1 + gz0 - hang) > 1e-3:
        raise RuntimeError(f"{key}: imported z body {b0:.4f}..{b1:.4f}, grip {g0:.4f}..{g1:.4f} (+{gz0}) - "
                           f"want base pivots at 0 and the grip top at hang {hang}")
    return {"key": key, "body": b, "grip": g, "bdir": bdir, "gdir": gdir, "bname": body, "gname": grip,
            "gz0": gz0, "pin": a2n(pin), "rope": a2n(rope), "close": a2n(close), "half": half,
            "tint": tint_for(tsrc), "tram": key == "tram", "hang": hang}


def pose(P, mode):
    """Set both objects' world matrices for a pose; returns the joint center (net) for close-ups."""
    lift = Matrix.Translation((0.0, 0.0, P["gz0"]))
    if mode == "zero":
        place(P["grip"], lift)
        place(P["body"], Matrix.Identity(4))
        return P["close"]
    if mode.startswith("pitch"):
        deg = float(mode[len("pitch"):])
        # + = nose down: about authoring +y (x -> -z) = net +x with the same sense
        R = rot_about("X", deg, P["rope"])
        place(P["grip"], R @ lift)
        moved = R @ P["pin"]
        place(P["body"], Matrix.Translation(moved - P["pin"]))     # plumb: translated only
        return R @ P["close"]
    if mode == "swing":
        # the whole assembly rolls about the rope point (travel axis = authoring x = net -y)
        R = rot_about("Y", swing_deg(P), P["rope"])
        place(P["grip"], R @ lift)
        place(P["body"], R)
        return R @ P["close"]
    raise ValueError(mode)


def swing_deg(P):
    return CS.TRAM_ROLL_DEG if P["tram"] else CS.ASSEMBLY_ROLL_DEG


def frame_helper(objs, center=None, half=None):
    """A face-less mesh spanning the posed objects (or a cube round `center`) to frame the camera on."""
    if center is not None:
        c = Vector(center)
        pts = [c + Vector((sx * half, sy * half, sz * half)) for sx in (-1, 1) for sy in (-1, 1)
               for sz in (-1, 1)]
    else:
        pts = []
        for o in objs:
            pts += [o.matrix_world @ Vector(cc) for cc in o.bound_box]
    me = bpy.data.meshes.new("FrameHelper")
    me.from_pydata([tuple(p) for p in pts], [], [])
    h = bpy.data.objects.new("FrameHelper", me)
    bpy.context.collection.objects.link(h)
    h.hide_render = True
    return h


def drop(o):
    me = o.data
    bpy.data.objects.remove(o, do_unlink=True)
    if me is not None and me.users == 0:
        bpy.data.meshes.remove(me)


def render_carrier(P, out_dir):
    b, g = P["body"], P["grip"]
    rig = cs2_preview._Rig(b, None)
    written = []
    try:
        cam = cs2_preview._make_camera(rig)
        cs2_preview._make_world(rig, **cs2_preview.NOON)
        cs2_preview._assign(b, cs2_preview._pbr_material(rig, P["bdir"], P["bname"], P["tint"]))
        cs2_preview._assign(g, cs2_preview._pbr_material(rig, P["gdir"], P["gname"], P["tint"]))
        sc = rig.scene

        def shot(tag, mode, view, show=(b, g), close=False):
            jc = pose(P, mode)
            for o in (b, g):
                o.hide_render = o not in show
            h = frame_helper(show, jc if close else None, P["half"] if close else None)
            res = RES_CLOSE if close else RES
            try:
                sc.render.resolution_x, sc.render.resolution_y = res
                cs2_preview._frame(cam, h, view, res)
                path = os.path.join(out_dir, f"{P['key']}_{tag}.png")
                written.append(cs2_preview._render(rig, path, engine="EEVEE"))
                print(f"[split-preview] {P['key']} {tag} -> {path}")
            finally:
                drop(h)

        shot("zero_3q", "zero", VIEW_3Q)
        shot("zero_side", "zero", VIEW_SIDE)
        shot(f"pitch+{PITCH_DEG:.0f}_side", f"pitch{PITCH_DEG}", VIEW_SIDE)
        shot(f"pitch-{PITCH_DEG:.0f}_side", f"pitch{-PITCH_DEG}", VIEW_SIDE)
        shot(f"swing{swing_deg(P):g}_front", "swing", VIEW_FRONT)
        shot("joint_zero", "zero", VIEW_CLOSE, close=True)
        shot(f"joint_pitch+{PITCH_DEG:.0f}", f"pitch{PITCH_DEG}", VIEW_CLOSE, close=True)
        shot("grip", "zero", VIEW_3Q, show=(g,))
        shot("body", "zero", VIEW_3Q, show=(b,))
    finally:
        rig.restore()
    return written


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="carrier_split_preview.py")
    ap.add_argument("--carrier", nargs="+", default=list(CARRIERS), choices=list(CARRIERS))
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--import-root", default=os.path.join(REPO, "assets", "import"))
    args = ap.parse_args(argv)
    if not bpy.app.background:
        raise RuntimeError("carrier_split_preview runs headless only")
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    total = 0
    for key in args.carrier:
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        P = load_pair(key, os.path.abspath(args.import_root))
        total += len(render_carrier(P, out))
    print(f"PASS carrier_split_preview: {total} renders for {args.carrier} -> {out}")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
