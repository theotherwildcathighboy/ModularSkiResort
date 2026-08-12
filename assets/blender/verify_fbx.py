"""
Round-trip check: import an FBX back into a clean Blender scene and report its size.

Purpose is to confirm what is actually IN the file, rather than trusting export settings.
CS2 reads FBX vertex data as centimetres, so a 10.5 m asset must contain ~1050 units.

    blender --background --factory-startup --python verify_fbx.py -- --fbx <path>
"""

import bpy
import sys
import argparse


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--fbx", required=True)
    return ap.parse_args(argv)


def main():
    args = parse_args()

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    # Blender 5.x ships a new importer at wm.fbx_import; fall back to the classic one.
    if hasattr(bpy.ops.wm, "fbx_import"):
        op, label = bpy.ops.wm.fbx_import, "wm.fbx_import"
    else:
        op, label = bpy.ops.import_scene.fbx, "import_scene.fbx"
    print(f"[verify] using {label}")
    op(filepath=args.fbx)

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        print("[verify] NO MESH IMPORTED")
        return

    for o in meshes:
        d = o.dimensions
        # Raw vertex extent, ignoring any node-level scale the importer may have applied
        xs = [v.co.x for v in o.data.vertices]
        ys = [v.co.y for v in o.data.vertices]
        zs = [v.co.z for v in o.data.vertices]
        print(f"[verify] object '{o.name}'  node scale {tuple(round(s, 4) for s in o.scale)}")
        print(f"[verify]   dimensions : {d.x:.3f} x {d.y:.3f} x {d.z:.3f}")
        print(f"[verify]   RAW vertex extent: "
              f"{max(xs)-min(xs):.3f} x {max(ys)-min(ys):.3f} x {max(zs)-min(zs):.3f}")
        for label, vals in (("x", xs), ("y", ys), ("z", zs)):
            lo, hi = min(vals), max(vals)
            # The UP axis of a ground-standing asset starts at ~0 and goes positive.
            # The other two straddle zero. That is how to identify it without guessing.
            hint = "  <-- UP (starts at ground)" if abs(lo) < 0.15 * (hi - lo) and hi > 0 else ""
            print(f"[verify]   raw {label} range: {lo:10.3f} .. {hi:10.3f}{hint}")
        print(f"[verify]   verts {len(o.data.vertices)}  polys {len(o.data.polygons)}")


if __name__ == "__main__":
    main()
