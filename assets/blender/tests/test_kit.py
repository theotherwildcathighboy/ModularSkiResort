"""
test_kit - builds every cs2_kit part once and checks the round-L contracts.

    blender --background --factory-startup --python tests\\test_kit.py

Asserts: every part has a name prefix from cs2_kit.PREFIXES (or the verbatim primitive
name), a valid obj["relief"], a UVMap layer, no zero-area faces; open_bottom on add_box /
add_wedge removes the -Z face and leaves the side-face bottom edges exactly on z = 0 with
nothing below (the engine-base rule); tube_along ring count on a 3-point path is
2 + bend_rings; and the whole kit joins with bpy.ops.object.join. Prints tri counts.
Exits 1 on the first failure, prints one PASS line otherwise.
"""

import math
import os
import sys
import traceback

import bpy
import bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_asset  # noqa: E402
import cs2_kit as K  # noqa: E402


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)


def check_part(obj, quad):
    me = obj.data
    assert obj.type == "MESH", obj.name
    assert obj["relief"] in K.RELIEF_KINDS, (obj.name, obj.get("relief"))
    assert quad in (0, 1, 2, 3), (obj.name, quad)
    assert me.uv_layers.get("UVMap") is not None, f"{obj.name}: no UVMap layer"
    assert len(me.polygons) > 0, f"{obj.name}: empty mesh"
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    centre = sum((v.co for v in bm.verts), Vector()) / len(bm.verts)
    outward = 0
    for f in bm.faces:
        assert f.calc_area() > 1e-9, f"{obj.name}: zero-area face"
        assert len(f.verts) <= 4, f"{obj.name}: n-gon ({len(f.verts)} verts)"
        if f.normal.dot(f.calc_center_median() - centre) >= -1e-6:
            outward += 1
    # inside-out shells show up as a majority of faces pointing at their own centroid
    assert outward >= 0.5 * len(bm.faces), f"{obj.name}: {outward}/{len(bm.faces)} faces outward"
    bm.free()
    return cs2_asset.tri_count(obj)


def wm(obj):
    """matrix_world after a depsgraph refresh (stale on freshly created objects)."""
    bpy.context.view_layer.update()
    return obj.matrix_world


def world_verts(obj):
    m = wm(obj)
    return [m @ v.co for v in obj.data.vertices]


def check_open_bottom(obj, expect_bottom_edges):
    """Engine-base rule: no -Z face, nothing below z=0, side bottom edges exactly on z=0."""
    me = obj.data
    W = world_verts(obj)
    m3 = wm(obj).to_3x3()
    minz = min(p.z for p in W)
    assert abs(minz) < 1e-6, f"{obj.name}: min z {minz} (must be 0)"
    for p in me.polygons:
        n = (m3 @ p.normal).normalized()
        assert n.z > -0.5, f"{obj.name}: bottom face survived (normal {tuple(n)})"
    # every edge with both verts in the band must have exactly one face, and that face
    # must rise above the band (no face entirely at/below it)
    edge_faces = {}
    for p in me.polygons:
        for ek in p.edge_keys:
            edge_faces.setdefault(ek, []).append(p)
    band = 0.01
    ground_edges = 0
    for (a, b), faces in edge_faces.items():
        if abs(W[a].z) <= band and abs(W[b].z) <= band:
            ground_edges += 1
            assert len(faces) == 1, f"{obj.name}: ground edge shared by {len(faces)} faces"
            zs = [W[i].z for i in faces[0].vertices]
            assert max(zs) > band, f"{obj.name}: ground edge on a face that does not rise"
            assert abs(W[a].z) < 1e-6 and abs(W[b].z) < 1e-6, f"{obj.name}: ground edge off 0"
    assert ground_edges == expect_bottom_edges, (obj.name, ground_edges, expect_bottom_edges)


def main():
    clear_scene()
    parts = []          # [(obj, quad)]
    counts = []

    # ---- primitives --------------------------------------------------------------
    cyl = K.add_cyl("tube_cyl", 0.3, 1.0, (0, 0, 2.0), segments=12, caps="both")
    parts.append((cyl, K.QUAD_PAINT))
    assert len(cyl.data.polygons) == 12 + 24, "12-seg fan-capped cylinder face count"
    cyl_nc = K.add_cyl("tube_cylnc", 0.02, 0.5, (1, 0, 2.0), segments=4, caps="none")
    parts.append((cyl_nc, K.QUAD_GALV))
    assert len(cyl_nc.data.polygons) == 4, "caps=none must leave only the wall"
    cyl_top = K.add_cyl("tube_cyltop", 0.1, 0.3, (2, 0, 2.0), segments=8, caps="top")
    ntop = sum(1 for p in cyl_top.data.polygons if p.normal.z > 0.9)
    nbot = sum(1 for p in cyl_top.data.polygons if p.normal.z < -0.9)
    assert (ntop, nbot) == (8, 0), f"caps=top: top {ntop} bottom {nbot}"
    parts.append((cyl_top, K.QUAD_GALV))
    cone = K.add_cyl("tube_cone", 0.2, 0.4, (3, 0, 2.0), segments=8, caps="bottom", r2=0.1)
    parts.append((cone, K.QUAD_GALV))

    pad = K.add_box("pad", (2.0, 1.5, 0.3), (0, 0, 0.15), open_bottom=True)
    parts.append((pad, K.QUAD_CONCRETE))
    assert len(pad.data.polygons) == 5, "open_bottom box must have 5 faces"
    check_open_bottom(pad, expect_bottom_edges=4)
    closed = K.add_box("crate", (0.5, 0.5, 0.5), (3, 3, 0.25))
    parts.append((closed, K.QUAD_PAINT))
    assert len(closed.data.polygons) == 6
    dropped = K.add_box("shelf", (0.5, 0.5, 0.5), (4, 3, 0.25), drop=("+X", "-Y"))
    parts.append((dropped, K.QUAD_PAINT))
    assert len(dropped.data.polygons) == 4, "drop= must delete the named faces"

    ramp = K.add_wedge("rampup", 2.0, 4.0, 1.5, 0.0, 0.3, open_bottom=True)
    parts.append((ramp, K.QUAD_CONCRETE))
    check_open_bottom(ramp, expect_bottom_edges=4)   # toe edge + two sides + heel edge
    ramp2 = K.add_wedge("rampdown", -4.0, -2.0, 1.5, 0.3, 0.02)
    parts.append((ramp2, K.QUAD_CONCRETE))
    assert len(ramp2.data.polygons) == 6, "closed wedge with two heights = 6 faces"
    assert min(v.co.z for v in ramp2.data.vertices) == 0.0

    # ---- tube_along ----------------------------------------------------------------
    tube = K.tube_along("tube_L", [(0, 5, 1), (1, 5, 1), (1, 5, 2)], 0.03, segments=8,
                        caps="none", bend_rings=8)
    parts.append((tube, K.QUAD_PAINT))
    tube_rings = tube["rings"]
    assert tube_rings == 2 + 8, tube_rings
    assert len(tube.data.vertices) == (2 + 8) * 8, len(tube.data.vertices)
    assert len(tube.data.polygons) == (2 + 8 - 1) * 8
    # every vertex sits at exactly `radius` from the ring center: check both ends
    for p in (Vector((0, 5, 1)), Vector((1, 5, 2))):
        near = [v for v in tube.data.vertices if (v.co - p).length < 0.03 + 1e-4]
        assert len(near) == 8, f"end ring at {tuple(p)} has {len(near)} verts"
    uvs = tube.data.uv_layers["UVMap"].data
    us = sorted({round(l.uv.x, 4) for l in uvs})
    assert us[0] == 0.0 and us[-1] == 1.0, f"u strip must span 0..1, got {us[0]}..{us[-1]}"
    vmax = max(l.uv.y for l in uvs)
    circ = 2 * math.pi * 0.03
    # v is arc length over circumference: between the chord (1.71 m) and the polyline (2 m)
    assert 1.70 / circ < vmax < 2.0 / circ, (vmax, 1.70 / circ, 2.0 / circ)
    tube_c = K.tube_along("tube_capped", [(0, 6, 1), (2, 6, 1)], 0.05, segments=6, caps="both",
                          bend_rings=8)
    parts.append((tube_c, K.QUAD_PAINT))
    assert tube_c["rings"] == 2 and len(tube_c.data.polygons) == 6 + 12
    tube_s = K.tube_along("tube_sweep", [(0, 7, 1), (0.3, 7, 1.1), (0.6, 7, 1.4), (0.7, 7, 1.9),
                                         (0.7, 7, 2.5)], 0.02, segments=8, caps="none", bend_rings=6)
    parts.append((tube_s, K.QUAD_PAINT))
    assert tube_s["rings"] == 2 + 3 * 6, tube_s["rings"]
    straight = K.tube_along("tube_collinear", [(0, 8, 1), (1, 8, 1), (2, 8, 1)], 0.02, segments=6)
    parts.append((straight, K.QUAD_PAINT))
    assert straight["rings"] == 2, "collinear interior point must add no ring"

    # ---- assemblies ------------------------------------------------------------------
    fl = K.flange("leg", 0.45, loc=(-3, 0, 0.315), bolt_geo=True)
    assert len(fl) == 1 + 8 and fl[0][0].name == "flange_leg" and fl[1][0].name.startswith("bolt_")
    assert fl[0][0]["relief"] == "geo"
    parts += fl
    fl2 = K.flange("collar", 0.30, t=0.02, segs=12, loc=(-3, 0, 3.0), axis="Y")
    assert len(fl2) == 1 and fl2[0][0]["relief"] == "map:bolts"
    parts += fl2

    g = K.gusset("portal", 0.4, 0.5, loc=(-3, 1, 1), rot=(0, 0, math.pi / 2))
    assert len(g) == 1 and g[0][0].name == "gusset_portal" and cs2_asset.tri_count(g[0][0]) == 8
    parts += g

    sr = K.stiffener_ring("rail", 0.30, loc=(-3, 2, 3), axis="X")
    assert sr[0][0].name == "stiff_rail" and cs2_asset.tri_count(sr[0][0]) == 6 * 16
    parts += sr

    hr = K.handrail("catwalk", [(-2, -3, 0.5), (1, -3, 0.5), (1, -1, 0.5)], h=1.10)
    names = [o.name for o, _ in hr]
    assert sum(n.startswith("post_") for n in names) == 5, names   # 3 m span: 3 posts, 2 m span: +2
    assert "rail_catwalk_top" in names and "rail_catwalk_knee" in names
    assert sum(n.startswith("kick_") for n in names) == 2
    top = next(o for o, _ in hr if o.name == "rail_catwalk_top")
    assert abs(max(v.co.z for v in top.data.vertices) - (0.5 + 1.10 + 0.028)) < 1e-4
    parts += hr

    gr = K.grating("catwalk", 3.0, 0.55, loc=(-0.5, -3, 0.47))
    assert gr[0][0].name == "grate_catwalk" and gr[0][0]["relief"] == "map:grate"
    plate_n = sorted(round(p.normal.z) for p in gr[0][0].data.polygons)
    assert plate_n == [-1, 1], plate_n
    assert gr[1][0].name == "grate_catwalk_lip" and cs2_asset.tri_count(gr[1][0]) == 24
    parts += gr

    cc = K.cable_clamp("tcable", 0.03, loc=(2, 2, 3), rot=(0, 0.3, 0.2))
    assert [o.name for o, _ in cc] == ["clamp_tcable_u", "clamp_tcable_l", "bolt_tcable_0", "bolt_tcable_1"]
    parts += cc

    sh = K.sheave("bank", 0.17, 0.13, (2, -2, 3), "Y", segs=12)
    assert [o.name for o, _ in sh] == ["sheave_bank_rim", "sheave_bank_hub"]
    rim = sh[0][0]
    assert cs2_asset.tri_count(rim) == 12 * 12, cs2_asset.tri_count(rim)
    # rim normals point away from the axle (outer flange faces radially outward)
    for p in rim.data.polygons:
        c = p.center
        if abs(c.xy.length - 0.17) < 1e-3 and abs(c.z) > 0.05:
            assert p.normal.dot(Vector((c.x, c.y, 0)).normalized()) > 0.9, "rim inside-out"
    assert (wm(sh[1][0]).to_3x3() @ Vector((0, 0, 1))).y > 0.99, "axis Y"
    parts += sh
    bw = K.sheave("bull", 1.80, 0.40, (6, 0, 3.5), (0, 0, 0), segs=32, hub=False)
    assert len(bw) == 1
    parts += bw

    d = K.door("hut", loc=(-4, -4, 0.15), facing="-X")
    names = [o.name for o, _ in d]
    assert names == ["door_hut_leaf", "door_hut_jamb0", "door_hut_jamb1", "door_hut_head",
                     "door_hut_handle", "door_hut_kick"], names
    leaf = d[0][0]
    assert leaf["relief"] == "map:seams"
    lo = min((wm(leaf) @ v.co).x for v in leaf.data.vertices)
    assert lo < -4.0 - 0.01, "door must stand proud on the -X side"
    assert min((wm(leaf) @ v.co).z for v in leaf.data.vertices) > 0.15 + 0.015
    parts += d

    ht = K.hatch("mach", 0.8, 1.3, loc=(-4, 4, 1.0), facing="+Y")
    names = [o.name for o, _ in ht]
    assert names == ["hatch_mach_panel", "hatch_mach_frame", "hatch_mach_hinge0", "hatch_mach_hinge1"]
    frame = ht[1][0]
    fn = [(wm(frame).to_3x3() @ p.normal).normalized() for p in frame.data.polygons]
    assert any(n.y > 0.9 for n in fn) and not any(n.y < -0.9 for n in fn), "frame faces outward"
    assert ht[0][0]["relief"] == "map:louvre"
    parts += ht

    roof = K.add_box("boxroof", (3.45, 2.65, 0.09), (0.3, 0, 7.245))
    parts.append((roof, K.QUAD_GALV))
    de = K.drip_edge(roof)
    assert [o.name for o, _ in de] == [f"drip_boxroof_{k}" for k in ("px", "nx", "py", "ny")]
    assert all(len(o.data.polygons) == 5 for o, _ in de)
    top_z = 7.245 + 0.045
    for o, _ in de:
        zs = [(wm(o) @ v.co).z for v in o.data.vertices]
        assert abs(max(zs) - (top_z + 0.01)) < 1e-4 and abs(min(zs) - (top_z - 0.06)) < 1e-4
    parts += de

    ac = K.axle_cap("sheave", 0.06, (2, -2.1, 3), "-Y")
    assert ac[0][0].name == "axlecap_sheave" and len(ac) == 5
    ys = [(wm(ac[0][0]) @ v.co).y for v in ac[0][0].data.vertices]
    assert abs(max(ys) - (-2.1)) < 1e-5 and abs(min(ys) - (-2.12)) < 1e-5, (max(ys), min(ys))
    parts += ac
    ac2 = K.axle_cap("plain", 0.06, (2, 2.1, 3), "Y", studs_geo=False)
    assert len(ac2) == 1 and ac2[0][0]["relief"] == "map:bolts"
    parts += ac2

    # ---- generic checks + tri table --------------------------------------------------
    total = 0
    seen = set()
    for obj, quad in parts:
        assert obj.name not in seen, f"duplicate name {obj.name}"
        seen.add(obj.name)
        n = check_part(obj, quad)
        counts.append((obj.name, n))
        total += n
    prefixed = [o for o, _ in parts if not o.name.startswith(K.PREFIXES)]
    # only the bare primitives built by name here are allowed outside the prefix families
    allowed = {"pad", "crate", "shelf", "rampup", "rampdown", "boxroof"}
    stray = [o.name for o in prefixed if o.name not in allowed]
    assert not stray, f"unprefixed assembly parts: {stray}"
    for name, n in counts:
        print(f"  {name:<28} {n:>5} tris")
    print(f"[kit] {len(parts)} parts, {total} tris total")
    assert total < 5000, total

    # ---- join --------------------------------------------------------------------------
    table = cs2_asset.tag_parts(parts)
    bpy.ops.object.select_all(action="DESELECT")
    for obj, _ in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    joined = bpy.context.active_object
    assert len(joined.data.polygons) > 0
    cs2_asset.store_part_table(joined, table)
    assert cs2_asset.tri_count(joined) == total, (cs2_asset.tri_count(joined), total)
    # the engine-base edges survived the join at world z = 0
    W = [wm(joined) @ v.co for v in joined.data.vertices]
    assert min(p.z for p in W) >= -1e-6, min(p.z for p in W)
    print(f"[kit] joined {len(parts)} parts -> {joined.name}: {cs2_asset.tri_count(joined)} tris, "
          f"{len(joined.data.vertices)} verts")
    print("PASS test_kit: every part built, no zero-area faces, open_bottom edges on z=0, "
          f"tube rings {tube_rings}, join ok ({total} tris)")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        print("FAIL test_kit")
        sys.exit(1)
    sys.exit(0)
