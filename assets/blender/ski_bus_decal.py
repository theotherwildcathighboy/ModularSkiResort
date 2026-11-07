"""
ski_bus_decal.py - the "SKI BUS" livery for the ski coach, painted on with a decal instead of a
board.

MODELED STAND-IN: no vanilla prop carries this text. One mesh, SkiBusDecalA: the 12-tri projection
box of the game's DefaultDecal template, origin at its center, attached to the coach clone as
sub-object rows (left side, right side, roof), so fit is adjusted in the prefab without a reimport.
The art is ski_bus_panel.paint_strip without the board: letters, peaks and stripes in cream, nothing
else, so the bus's own paint shows between them.

Import contract (AssetImportPipeline.SetupDecalComponent, 1.6.2f1:2659-2707): the "Surface" settings
key sets the template (cs2_asset.write_settings `surface`). m_TextureArea comes from the UVs of the
vertices whose normal is exactly up, so the box is flat shaded and the projection runs along local
game Y. The rows point local Y out of the bus body.

Frame: export_fbx bakes (x, y, z) -> (x, z, -y) and the game negates x, so without orient_for_net
game = (-bx, bz, -by). Viewed from game +Y, the texture's u runs along game +X and v along game +Z,
which reads unmirrored.

Texture: 2048^2 (decals want a power of two), the strip sits in a centered rect at
ski_bus_panel.PPM. BaseColor RGB is cream everywhere with alpha = coverage (no dark fringe at the
clip edge). Normal flat. MaskMap RGB black, A gloss. No ControlMask: a decal is never tinted.
cs2_validate does not run here: its alpha rule forbids exactly what a decal is.

CLI (headless only):
    blender --background --factory-startup --python-exit-code 1 --python ski_bus_decal.py -- --out DIR
    --out DIR receives DIR/SkiBusDecalA/ (FBX, three PNGs, settings.json). Exit 1 on any failure. The last line is PASS.
"""

import argparse
import os
import sys
import traceback

import bpy
import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset             # noqa: E402
import cs2_signset as SS     # noqa: E402
import ski_bus_panel as P    # noqa: E402

NAME = "SkiBusDecalA"
TEX = 2048
HALF_X, HALF_Z = P.BOARD_W / 2.0, P.BOARD_H / 2.0   # the art, game x and z
HALF_Y = 0.20                                       # projection depth each way along the row's outward axis
SMOOTH = 0.35                                       # a painted coat, close to the body's own sheen


def parse_args(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="ski_bus_decal.py")
    ap.add_argument("--out", required=True, help="DIR/SkiBusDecalA/ receives the asset folder")
    return ap.parse_args(argv)


def paint_maps(cov):
    """{map: (A, A, 4) array}, rows bottom-up, and the strip's pixel rect (x0, y0, x1, y1)."""
    h, w = cov.shape
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


def build_mesh(rect):
    """The box, flat shaded, wound outward. Every vertex carries the top-down projection UV, so the top face spans the
    rect exactly and the sides are degenerate strips the decal shader never samples."""
    u0, v0, u1, v1 = (t / float(TEX) for t in rect)
    gx, gy, gz = HALF_X, HALF_Y, HALF_Z
    verts = [(sx * gx, sy * gz, sz * gy) for sz in (-1, 1) for sy in (-1, 1) for sx in (-1, 1)]
    polys = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    me = bpy.data.meshes.new(NAME)
    me.from_pydata(verts, [], polys)
    me.update()
    lay = me.uv_layers.new(name="UVMap")
    for li, loop in enumerate(me.loops):
        bx, by, _bz = verts[loop.vertex_index]
        # game x = -bx, game z = -by: u along game +X, v along game +Z
        lay.data[li].uv = (u0 + (u1 - u0) * (HALF_X - bx) / (2.0 * HALF_X),
                           v0 + (v1 - v0) * (HALF_Z - by) / (2.0 * HALF_Z))
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(NAME, me)
    bpy.context.scene.collection.objects.link(ob)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    return ob


def check_mesh(ob, rect):
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
    return {"tris": sum(len(p.vertices) - 2 for p in me.polygons), "ppm": (rect[2] - rect[0]) / (2.0 * HALF_X)}


def run(args):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    asset_dir = os.path.join(os.path.abspath(args.out), NAME)
    os.makedirs(asset_dir, exist_ok=True)
    cov, layout = P.paint_strip(P.FACE)
    ink = P.check_strip(cov, layout)
    maps, rect = paint_maps(cov)
    for m, (arr, is_data) in maps.items():
        cs2_asset._write_png_raw(os.path.join(asset_dir, f"{NAME}_{m}.png"), f"{NAME}{m}", arr, TEX, is_data)
    ob = build_mesh(rect)
    geo = check_mesh(ob, rect)
    cs2_asset.write_settings(asset_dir, NAME, surface={"materialTemplate": "DefaultDecal"})
    cs2_asset.export_fbx(ob, os.path.join(asset_dir, f"{NAME}.fbx"))
    print(f"PASS ski_bus_decal: {NAME} {geo}, rect {rect}, ink margins {ink}, {layout['font']}")


if __name__ == "__main__":
    try:
        run(parse_args())
    except Exception:   # noqa: BLE001 - --python-exit-code 1 makes it a non-zero exit
        traceback.print_exc()
        raise
