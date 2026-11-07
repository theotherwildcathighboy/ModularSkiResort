"""
test_submesh.py - cs2_submesh contract test (headless Blender 5.2).

    blender --background --factory-startup --python tests\test_submesh.py

Scene: a 2 m hut box + a "hutwin" pane box (as lift_terminal builds them), tagged with
cs2_asset.tag_parts and joined into "ExampleX". split_submesh moves hutwin off as ExampleX_Win into
room cell (2, 1). Asserts:
- The parent lost exactly the hutwin faces (and nothing else).
- The sub has no |n.z| > 0.9 faces.
- Every sub UV lies strictly inside the cell.
- No two pane islands overlap.
- Each pane's top edge maps to the higher v.
- A _Gls split unwraps non-overlapping.

Then the test exports parent + _Win + _LOD1_Win, re-imports the three FBXs and asserts names, raw
extent = Blender extent x100, and identity transforms. It prints one PASS line and exits 0, or exits
1.
"""

import math
import os
import sys
import tempfile
import traceback

import bpy
import bmesh
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import cs2_asset       # noqa: E402
import cs2_submesh     # noqa: E402

CELL = (2, 1)
CELL_RECT = (CELL[0] / 5.0, CELL[1] / 5.0, (CELL[0] + 1) / 5.0, (CELL[1] + 1) / 5.0)


def add_box(name, size, centre):
    """Axis-aligned box with vertices offset in mesh space. Object stays at the origin."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0] + centre[0], v.co.y * size[1] + centre[1],
                       v.co.z * size[2] + centre[2]))
    bm.to_mesh(me)
    bm.free()
    me.uv_layers.new(name="UVAtlas")   # every real part carries an atlas layer
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    return obj


def clear_scene():
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m, do_unlink=True)


def build_scene(win_name="hutwin"):
    clear_scene()
    hut = add_box("hut", (2.0, 2.0, 2.4), (0.0, 0.0, 1.2))
    win = add_box(win_name, (2.05, 2.05, 0.7), (0.0, 0.0, 1.6))
    parts = [(hut, 0), (win, 2)]
    table = cs2_asset.tag_parts(parts)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = hut
    bpy.ops.object.join()
    parent = bpy.context.active_object
    parent.name = "ExampleX"
    parent.data.name = "ExampleX"
    cs2_asset.store_part_table(parent, table)
    cs2_asset.orient_for_net(parent)
    return parent, table


def part_counts(obj, table):
    attr = obj.data.attributes["part"]
    counts = {}
    for v in attr.data:
        n = table[v.value - 1]
        counts[n] = counts.get(n, 0) + 1
    return counts


def extent(obj):
    xs = [v.co.x for v in obj.data.vertices]
    ys = [v.co.y for v in obj.data.vertices]
    zs = [v.co.z for v in obj.data.vertices]
    return (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))


def rasterise_overlap(me, uv_name, res=256):
    """Count texels covered by more than one face (non-overlap check, 3.9 validator style)."""
    uv = me.uv_layers[uv_name].data
    cover = {}
    for poly in me.polygons:
        pts = [uv[i].uv for i in poly.loop_indices]
        us = [p.x for p in pts]
        vs = [p.y for p in pts]
        x0, x1 = int(min(us) * res), int(math.ceil(max(us) * res))
        y0, y1 = int(min(vs) * res), int(math.ceil(max(vs) * res))
        for y in range(y0, y1):
            for x in range(x0, x1):
                p = Vector(((x + 0.5) / res, (y + 0.5) / res))
                if _point_in_poly(p, pts):
                    cover[(x, y)] = cover.get((x, y), 0) + 1
    return sum(1 for c in cover.values() if c > 1), len(cover)


def _point_in_poly(p, pts):
    inside = False
    n = len(pts)
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]
        if (a.y > p.y) != (b.y > p.y):
            x = a.x + (p.y - a.y) * (b.x - a.x) / (b.y - a.y)
            if p.x < x:
                inside = not inside
    return inside


def check_win_split(parent, table):
    before = part_counts(parent, table)
    tris_before = cs2_asset.tri_count(parent)
    sub = cs2_submesh.split_submesh(parent, table, ("hutwin",), "Win", win_cell=CELL)

    assert sub.name == "ExampleX_Win", sub.name
    assert sub.data.name == "ExampleX_Win", sub.data.name
    after = part_counts(parent, table)
    assert "hutwin" not in after, f"parent still carries hutwin faces: {after}"
    assert after.get("hut") == before["hut"], (before, after)
    assert cs2_asset.tri_count(parent) == tris_before - 12, "parent lost the wrong tri count"
    assert len(sub.data.materials) == 0
    assert len(sub.data.uv_layers) == 1, [l.name for l in sub.data.uv_layers]
    assert sub.matrix_world == parent.matrix_world

    me = sub.data
    assert len(me.polygons) == 4, f"expected 4 side panes, got {len(me.polygons)}"
    for p in me.polygons:
        assert abs(p.normal.z) <= 0.9, f"buried pane face survived: n={tuple(p.normal)}"
    uv = me.uv_layers[0].data
    u0, v0, u1, v1 = CELL_RECT
    for p in me.polygons:
        for li in p.loop_indices:
            u, v = uv[li].uv
            assert u0 < u < u1 and v0 < v < v1, f"UV ({u:.4f},{v:.4f}) outside cell {CELL_RECT}"
            # importer room id of this vertex
            assert int(v * 5) * 5 + int(u * 5) == CELL[1] * 5 + CELL[0]
        zs = [(me.vertices[me.loops[li].vertex_index].co.z, uv[li].uv.y) for li in p.loop_indices]
        top_v = min(v for z, v in zs if z > 1.6)
        bot_v = max(v for z, v in zs if z < 1.6)
        assert top_v > bot_v + 0.01, f"pane top edge not at the higher v: {zs}"
    overlap, covered = rasterise_overlap(me, me.uv_layers[0].name)
    assert overlap == 0, f"{overlap} of {covered} texels covered twice on the _Win sub"
    assert covered > 0
    print(f"[test] _Win: 4 panes in cell {CELL}, {covered} texels covered, 0 overlapping")
    return sub


def check_gls_split():
    parent, table = build_scene(win_name="hutglass")
    sub = cs2_submesh.split_submesh(parent, table, ("hutglass",), "Gls")
    assert sub.name == "ExampleX_Gls"
    assert len(sub.data.polygons) == 6
    overlap, covered = rasterise_overlap(sub.data, sub.data.uv_layers[0].name)
    assert overlap == 0, f"_Gls unwrap overlaps on {overlap} texels"
    assert covered > 0
    print(f"[test] _Gls: 6 faces smart-projected, {covered} texels covered, 0 overlapping")


def check_names():
    assert cs2_submesh.submesh_name("ExampleX", "Win") == "ExampleX_Win"
    for bad in ("Veg", "Base", "win", "Foo"):
        try:
            cs2_submesh.submesh_name("ExampleX", bad)
        except cs2_submesh.SubmeshError:
            pass
        else:
            raise AssertionError(f"suffix {bad!r} accepted")
    try:
        cs2_submesh.submesh_name("Example_X", "Win")
    except cs2_asset.AssetNameError:
        pass
    else:
        raise AssertionError("underscore base name accepted")


def check_export(parent, sub, out_dir):
    ext_parent = extent(parent)
    ext_sub = extent(sub)
    parent_path = os.path.join(out_dir, "ExampleX.fbx")
    cs2_asset.export_fbx(parent, parent_path)
    paths = cs2_submesh.export_submeshes(sub, "ExampleX", "Win", out_dir, lods=(1,))
    assert sub.name == "ExampleX_Win", "export_submeshes did not restore the sub name"
    assert [os.path.basename(p) for p in paths] == ["ExampleX_Win.fbx", "ExampleX_LOD1_Win.fbx"]
    for p in paths + [parent_path]:
        assert os.path.isfile(p) and os.path.getsize(p) > 0, p

    clear_scene()
    expect = {"ExampleX": ext_parent, "ExampleX_Win": ext_sub, "ExampleX_LOD1_Win": ext_sub}
    for p in [parent_path] + paths:
        bpy.ops.object.select_all(action="DESELECT")
        bpy.ops.wm.fbx_import(filepath=p)
    objs = {o.name: o for o in bpy.context.scene.objects if o.type == "MESH"}
    assert set(objs) == set(expect), f"imported names {sorted(objs)} != {sorted(expect)}"
    for name, ext in expect.items():
        o = objs[name]
        # Geometry node = "<stem>.001" in every export_fbx file (shipped towers too). CS2 pairs
        # textures by the Model node, which is the object name.
        assert o.data.name.split(".")[0] == name, (name, o.data.name)
        raw = extent(o)
        # Blender z -> CS2 y (up). The importer with axis identity keeps raw vertex data.
        want = sorted(e * cs2_asset.CS2_UNIT_SCALE for e in ext)
        got = sorted(raw)
        assert all(abs(a - b) < 1e-2 for a, b in zip(want, got)), (name, want, got)
        ident = Matrix.Identity(4)
        diff = max(abs(o.matrix_world[i][j] - ident[i][j]) for i in range(4) for j in range(4))
        assert diff < 1e-6, f"{name}: node transform not identity\n{o.matrix_world}"
        assert o.data.has_custom_normals, name
        assert len(o.data.uv_layers) == 1, name
        print(f"[test] re-import {name}: raw extent {tuple(round(r, 2) for r in raw)} "
              f"(x{cs2_asset.CS2_UNIT_SCALE:.0f} of {tuple(round(e, 3) for e in ext)}), identity transform")
    assert objs["ExampleX_Win"].matrix_world == objs["ExampleX"].matrix_world
    assert len(objs["ExampleX_Win"].data.polygons) == len(objs["ExampleX_LOD1_Win"].data.polygons)


def main():
    out_dir = os.environ.get("CS2_TEST_OUT") or os.path.join(tempfile.gettempdir(), "cs2_submesh_test")
    os.makedirs(out_dir, exist_ok=True)
    check_names()
    parent, table = build_scene()
    sub = check_win_split(parent, table)
    check_export(parent, sub, out_dir)
    check_gls_split()
    print("PASS test_submesh: _Win cell mapping, _Gls unwrap, export/re-import names + x100 + identity")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        print("FAIL test_submesh")
        sys.exit(1)
    sys.exit(0)
