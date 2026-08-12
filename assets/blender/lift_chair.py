"""
Chairlift chair generator.

Hangs from the haul rope as a NetSubObject with m_Placement = 2 (EdgeMiddle) and a spacing,
the same mechanism MarinaPierNetwork uses to distribute boats along a pier.

Run headless:
    blender --background --factory-startup --python lift_chair.py -- --out ../import

ORIGIN IS AT THE GRIP (top), not the seat. NetSubObjectInfo.m_Position places the object's
origin, and we want that point on the cable with the chair hanging below it. So all geometry
is built at negative Z.

Axis convention matches lift_tower.py: +X = travel/facing, +Y = chair width, +Z = up.
cs2_asset.orient_for_net() converts to CS2's X-lateral / Y-up / Z-forward on export.

Dimensions are real fixed-grip figures: ~0.6 m of seat width per rider, 2.2 m from rope to
seat, backrest ~0.6 m. Chair spacing along the rope is ~15 m, derived from a double chair
moving ~1200 people/hour at 2.5 m/s (600 chairs/hour = one every 6 s = 15 m).
"""

import bpy
import bmesh
import math
import os
import sys
import argparse
from dataclasses import dataclass

import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402


DEFAULT_NAME = "SkiLiftChair"


@dataclass
class ChairParams:
    seats: int = 3                  # double / triple / quad
    seat_width_each: float = 0.6    # metres of bench per rider

    hanger_len: float = 2.2         # rope to seat centre height (kept - J stem reaches it)
    hanger_radius: float = 0.045    # the swan-neck stem tube
    grip_size: float = 0.16         # the clamp that grips the haul rope

    seat_depth: float = 0.52
    seat_thick: float = 0.06
    back_height: float = 0.50       # seat top to backrest top rail
    rail_radius: float = 0.028      # frame tubing

    @property
    def width(self):
        return self.seats * self.seat_width_each


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def add_box(name, size, location):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return o


def add_tube(name, radius, depth, location, rotation=(0, 0, 0), segments=8):
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments, radius=radius, depth=depth,
                                        location=location, rotation=rotation)
    o = bpy.context.active_object
    o.name = name
    return o


# Atlas quadrants (cs2_asset.lift_atlas_quads layout, shared with towers and stations)
QUAD_GALV, QUAD_PAINT = 1, 3


def add_link(name, p0, p1, radius, segments=6, extend=0.03):
    """
    Tube spanning two arbitrary 3D points, lengthened `extend` past each end so angled
    segments overlap at their joints instead of leaving wedge gaps on the outside of a
    bend. That overlap is what lets 4 straight cylinders read as one curved swan-neck.
    """
    a, b = mathutils.Vector(p0), mathutils.Vector(p1)
    d = b - a
    o = add_tube(name, radius, d.length + 2.0 * extend, tuple((a + b) / 2.0),
                 segments=segments)
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    return o


def build_chair_parts(p: ChairParams, offset=(0, 0, 0), prefix="", facing=1):
    """
    Build the chair's parts, each tagged with its atlas quadrant: [(obj, quad), ...].

    J-generation redesign after the user's Yan chair drawing and the Pali photos
    (liftblog img_6862/6880/6914): the hanger is a single J-CURVED STEM (swan-neck).
    Its vertical run drops BEHIND the backrest; at the top it sweeps forward and up to
    the grip, so the grip rides the rope above the front half of the bench - exactly
    the candy-cane silhouette every photo shows. The curve is approximated with four
    straight tube segments (add_link overlaps their joints). The bench is tube steel:
    thin vertical back bars under a top rail, armrest tubes with open ends, and the
    footrest bar hung below the seat front on two links.

    HEIGHT CONTRACT (hard invariant - the game positions chairs by this): grip top to
    lowest point is EXACTLY 2.40 m. Grip box top sits at offset.z + 0.08; the footrest
    bar bottom sits at offset.z - 2.32. Nothing may hang below the footrest bar.

    Geometry hangs BELOW the grip: the grip sits at `offset`. Standalone export
    re-pivots afterwards; the terminal generator passes `offset` directly so the grip
    lands on its modelled rope. `facing` is +1 (riders face +X, the travel direction)
    or -1 (mirrored along X). `prefix` keeps part names unique when several chairs
    share one scene.
    """
    ox, oy, oz = offset
    f = facing
    parts = []
    w = p.width
    seat_z = oz - p.hanger_len                      # bench centre height (-2.20)

    def X(relx):
        """Author in the facing=+1 frame; mirror along X for facing=-1."""
        return ox + f * relx

    back_x = -0.45                                  # backrest plane, behind the grip
    seat_cx = back_x + p.seat_depth / 2.0           # bench centre: -0.19
    back_top_z = seat_z + p.seat_thick / 2.0 + p.back_height   # backrest top rail

    # Grip on the rope - top face at oz + grip_size/2 = +0.08, the height datum
    parts.append((add_box(f"{prefix}grip", (p.grip_size * 1.6, p.grip_size, p.grip_size),
                          (ox, oy, oz)), QUAD_GALV))

    # THE SWAN-NECK. Four segments from just under the grip, sweeping back and down,
    # then a straight vertical run behind the backrest to the top rail it clamps.
    stem = [(0.00, oz - 0.055),
            (-0.17, oz - 0.18),
            (-0.39, oz - 0.48),
            (-0.52, oz - 0.90),
            (-0.52, back_top_z)]
    for i in range(len(stem) - 1):
        (xa, za), (xb, zb) = stem[i], stem[i + 1]
        parts.append((add_link(f"{prefix}stem{i}", (X(xa), oy, za), (X(xb), oy, zb),
                               p.hanger_radius, segments=8), QUAD_PAINT))

    # Backrest: top rail spanning the bench, thin vertical bars down to the seat
    # (drawing: tube hoops; four bars flanking the centre stem read the same at scale)
    parts.append((add_tube(f"{prefix}backrail", p.rail_radius, w * 0.94,
                           (X(-0.50), oy, back_top_z),
                           rotation=(math.radians(90), 0, 0), segments=8), QUAD_PAINT))
    for i, by in enumerate((-0.78, -0.36, 0.36, 0.78)):
        parts.append((add_link(f"{prefix}backbar{i}",
                               (X(-0.49), oy + by, back_top_z - 0.02),
                               (X(-0.44), oy + by, seat_z + 0.02),
                               0.016, segments=6, extend=0.01), QUAD_PAINT))

    # Bench: seat plank plus a front edge tube (tube-steel bench per the drawing)
    parts.append((add_box(f"{prefix}seat", (p.seat_depth, w, p.seat_thick),
                          (X(seat_cx), oy, seat_z)), QUAD_PAINT))
    parts.append((add_tube(f"{prefix}seattube", 0.026, w * 0.96,
                           (X(seat_cx + p.seat_depth / 2.0 - 0.02), oy, seat_z - 0.01),
                           rotation=(math.radians(90), 0, 0), segments=6), QUAD_PAINT))

    # Armrests: a horizontal tube each end, open end forward, with a front post down
    # to the seat edge
    arm_z = seat_z + 0.28
    for side in (-1, 1):
        y = oy + side * (w / 2.0 - 0.03)
        parts.append((add_link(f"{prefix}armrest_{side}",
                               (X(-0.48), y, arm_z), (X(-0.02), y, arm_z),
                               0.022, segments=6, extend=0.0), QUAD_PAINT))
        parts.append((add_link(f"{prefix}armpost_{side}",
                               (X(-0.05), y, arm_z - 0.01),
                               (X(-0.05), y, seat_z + 0.02),
                               0.018, segments=6, extend=0.0), QUAD_PAINT))

    # Footrest bar on two hanger links below the seat front (drawing: the footrest
    # loop). Bar bottom at oz - 2.32 CLOSES the 2.40 m height contract; the links stop
    # just short of the bar axis so nothing pokes past it.
    bar_z = oz - 2.32 + 0.025
    parts.append((add_tube(f"{prefix}footbar", 0.025, w * 0.72,
                           (X(0.14), oy, bar_z),
                           rotation=(math.radians(90), 0, 0), segments=6), QUAD_GALV))
    for side in (-1, 1):
        y = oy + side * w * 0.29
        parts.append((add_link(f"{prefix}footlink_{side}",
                               (X(0.02), y, seat_z - p.seat_thick / 2.0 + 0.01),
                               (X(0.13), y, bar_z + 0.012),
                               0.016, segments=6, extend=0.0), QUAD_GALV))
    return parts


def build_chair(p: ChairParams):
    parts = build_chair_parts(p)

    # Per-part UVs into the shared atlas quadrants BEFORE joining
    for obj, quad in parts:
        cs2_asset.unwrap_into_quadrant(obj, quad)

    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()

    chair = bpy.context.active_object
    chair.name = DEFAULT_NAME

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")

    # BASE-PIVOT: shift the whole chair up so its lowest point sits at z = 0.
    #
    # The first version put the origin at the grip, so the chair hung entirely BELOW its own
    # origin. In game only the 8 cm grip rendered - everything below the origin was treated as
    # underground and clipped. Every CS2 object we have inspected (the vanilla 47 m pylon, the
    # 8 m pole, our tower) is base-pivoted with geometry at y >= 0, and the engine relies on it.
    #
    # The grip therefore ends up at the TOP of the object, at z = height, and the NetSubObjects
    # m_Position.y must be lowered by that height so the grip still lands on the rope.
    bpy.ops.object.select_all(action="DESELECT")
    chair.select_set(True)
    bpy.context.view_layer.objects.active = chair
    lo = min((chair.matrix_world @ v.co).z for v in chair.data.vertices)
    chair.location.z -= lo
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

    cs2_asset.orient_for_net(chair)
    return chair


def unwrap(obj):
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")


def tri_count(obj):
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    n = len(bm.faces); bm.free(); return n


def render_previews(chair, out_dir, stem):
    """
    Framed views via lift_tower's camera helpers. The profile view is the one that
    matters for the J round: it shows the swan-neck curve edge-on.
    """
    import lift_tower
    scene = bpy.context.scene
    scene.render.engine = lift_tower.pick_engine()
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.60, 0.66, 0.76, 1.0)
    scene.world = world

    bpy.ops.object.camera_add(location=(0, 0, 0))
    cam = bpy.context.active_object
    cam.data.lens = 50
    scene.camera = cam

    bpy.ops.object.light_add(type="SUN", location=(10, -12, 20))
    key = bpy.context.active_object
    key.data.energy = 5.5
    key.rotation_euler = (math.radians(50), 0, math.radians(35))
    bpy.ops.object.light_add(type="SUN", location=(-12, 8, 12))
    fill = bpy.context.active_object
    fill.data.energy = 2.2
    fill.rotation_euler = (math.radians(65), 0, math.radians(215))

    # Post-orient_for_net the travel axis is Blender -Y, so the PROFILE (swan-neck
    # silhouette) is seen looking along X.
    views = [("three_quarter", mathutils.Vector((1.0, -1.1, 0.35)), (1000, 1200)),
             ("profile",       mathutils.Vector((1.0, 0.05, 0.10)), (1000, 1200)),
             ("front",         mathutils.Vector((0.05, -1.0, 0.12)), (1200, 1000))]
    written = []
    for vname, direction, res in views:
        scene.render.resolution_x, scene.render.resolution_y = res
        lift_tower.frame_camera(cam, chair, direction, res)
        path = os.path.join(out_dir, f"{stem}_{vname}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
    return written


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--name", default=DEFAULT_NAME)
    ap.add_argument("--seats", type=int, default=3)
    ap.add_argument("--preview", default=None)
    ap.add_argument("--basecolor", type=float, nargs=3, default=[0.13, 0.16, 0.22])
    ap.add_argument("--metallic", type=float, default=0.1)
    ap.add_argument("--smoothness", type=float, default=0.35)
    return ap.parse_args(argv)


def main():
    args = parse_args()
    clear_scene()

    p = ChairParams(seats=args.seats)
    chair = build_chair(p)
    chair.name = args.name
    name = cs2_asset.validate_asset_name(chair.name)
    # UVs were already laid into atlas quadrants per part inside build_chair - no re-unwrap.

    asset_dir = os.path.join(os.path.abspath(args.out), name)
    os.makedirs(asset_dir, exist_ok=True)

    d = chair.dimensions
    grip_h = d.z                     # base-pivoted, so the grip is at the very top
    rope_y = 7.04                    # lane height, from TowerParams.sheave_axle_height
    print(f"[chair] seats: {p.seats}   bench width: {p.width:.2f} m")
    print(f"[chair] tris: {tri_count(chair)}")
    print(f"[chair] size (m): {d.x:.3f} x {d.y:.3f} x {d.z:.3f}")
    print(f"[chair] base-pivoted: geometry runs z 0 .. {grip_h:.2f}, grip at the top")
    if abs(grip_h - 2.40) > 0.005:
        raise RuntimeError(
            f"HEIGHT CONTRACT BROKEN: chair is {grip_h:.4f} m grip-top to lowest point, "
            f"must be exactly 2.40 (the game positions chairs by this height).")
    print(f"[chair] ---- CS2 NetSubObjects settings ----")
    print(f"[chair]   m_Placement 2 (EdgeMiddle), m_Spacing 15")
    print(f"[chair]   m_Position  (+/-1.80, {rope_y - grip_h:.2f}, 0)")
    print(f"[chair]   = rope {rope_y} minus chair height {grip_h:.2f}, so the grip lands on the rope")

    # Shared lift atlas (dark painted steel recolourable, galvanized hardware) at half
    # resolution - the chair is a 2 m object, 512-px quads are plenty.
    tex_root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "textures")
    quad_sets, control_quads = cs2_asset.lift_atlas_quads(tex_root)
    cs2_asset.make_texture_set_atlas(asset_dir, name, quad_sets, quad_size=512,
                                     control_quads=control_quads)
    cs2_asset.export_fbx(chair, os.path.join(asset_dir, f"{name}.fbx"))
    cs2_asset.write_settings(asset_dir, name)
    print(f"[chair] asset folder: {asset_dir}")

    if args.preview:
        prev = os.path.abspath(args.preview)
        os.makedirs(prev, exist_ok=True)
        cs2_asset.preview_material(chair, asset_dir, name)
        for png in render_previews(chair, prev, name):
            print(f"[chair] preview -> {png}")
        chair.data.materials.clear()


if __name__ == "__main__":
    main()
