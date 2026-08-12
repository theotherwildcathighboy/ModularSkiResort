"""
Haul-rope mesh for the ski lift network.

This becomes the mesh on a `Net -> Lane` prefab, which the Power Line Prefab then strings
between towers. CS2 bends and repeats the lane mesh along the network curve, so this only needs
to be one straight segment running along +X.

Run headless:
    blender --background --factory-startup --python lift_cable.py -- --out ../import

Follows the same import contract as lift_tower.py (see SPEC 11.0): centimetres, Y-up, both baked
into the vertices, no underscore in the asset name, no material.

Orientation matches the tower generator: +X is the direction of travel, +Z up (converted to Y-up
on export). The rope sits centred on the origin so the lane can position it.
"""

import bpy
import bmesh
import math
import os
import sys
import argparse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402


DEFAULT_NAME = "SkiLiftCable"      # no underscore - cs2_asset.validate_asset_name


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def build_cable(length, radius, segments):
    """
    A single rope segment along +X.

    Real haul ropes are ~40-50 mm diameter. At CS2's viewing distance an 8-sided tube reads as
    round, and the silhouette matters far more than the cross-section.
    """
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=segments,
        radius=radius,
        depth=length,
        location=(0.0, 0.0, 0.0),
        rotation=(0.0, math.radians(90.0), 0.0),   # local Z -> world X
    )
    cable = bpy.context.active_object
    cable.name = DEFAULT_NAME

    # Bake the 90-degree rotation into the mesh so the object sits at identity. Generators should
    # never hand a rotated object to export_fbx - see the note in cs2_asset.export_fbx.
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # Drop the end caps: the segment is repeated along the line, so caps are never visible
    # and would only cost triangles.
    bm = bmesh.new()
    bm.from_mesh(cable.data)
    caps = [f for f in bm.faces if len(f.verts) > 4]
    if caps:
        bmesh.ops.delete(bm, geom=caps, context="FACES")
    bm.to_mesh(cable.data)
    bm.free()

    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

    # Must run along CS2's Z, or the lane never deforms to the segment length.
    cs2_asset.orient_for_net(cable)
    return cable


def unwrap(obj):
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")


def tri_count(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    n = len(bm.faces)
    bm.free()
    return n


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--name", default=DEFAULT_NAME)
    ap.add_argument("--length", type=float, default=8.0,
                    help="segment length in metres; tune once CS2's lane tiling is understood")
    ap.add_argument("--radius", type=float, default=0.024, help="rope radius in metres")
    ap.add_argument("--segments", type=int, default=8)
    return ap.parse_args(argv)


def main():
    args = parse_args()
    clear_scene()

    cable = build_cable(args.length, args.radius, args.segments)
    cable.name = args.name
    name = cs2_asset.validate_asset_name(cable.name)
    unwrap(cable)

    asset_dir = os.path.join(os.path.abspath(args.out), name)
    os.makedirs(asset_dir, exist_ok=True)

    d = cable.dimensions
    print(f"[cable] tris: {tri_count(cable)}")
    print(f"[cable] size (m): {d.x:.3f} x {d.y:.3f} x {d.z:.3f}")

    # Dark, slightly glossy steel rope. Greased haul rope is darker than the towers.
    cs2_asset.make_texture_set(
        asset_dir, name, size=256,
        base_rgb=(0.10, 0.10, 0.11), metallic=0.6, smoothness=0.45, noise=0.01,
    )
    cs2_asset.export_fbx(cable, os.path.join(asset_dir, f"{name}.fbx"))
    cs2_asset.write_settings(asset_dir, name)

    print(f"[cable] asset folder: {asset_dir}")


if __name__ == "__main__":
    main()
