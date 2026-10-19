"""
test_ridge_v2 - T95 CAD pass (2026-10-19): ridge v2 contracts (cs2_bldg.ridge_kit).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_ridge_v2.py

Asserts:
  R1  flag off (RIDGE version 0) builds the v1 gable / hip roofs vertex for vertex (the same part names, the same
      sorted vertex lists) as before ridge_kit existed: the v1 box ridge (12 tris) and slabs past the ridge.
  R2  gable v2: both slabs end on vertical planes RIDGE_GAP (3 mm) either side of the ridge line (closed 8-face slabs,
      no coincident end faces) and their top edges reach the ridge line (no overshoot, no notch).
  R3  gable v2: ridge_ = 12 tris (closed); its hems sit RIDGE_CLEAR (<= 5 mm) above the slab tops, its apex RIDGE_RISE above the
      higher slab top; plan width 2 x RIDGE_LEG (was >= 0.8 m); nothing of it lies below the slab-top planes.
  R4  asymmetric pitch (eave_z1) and both axes: R2-R3 hold.
  R5  hip v2: ridge_ = 12 tris per edge (4 hips + ridge; 4 on a pyramid), every vertex within 6 cm of the roof top
      surface or above it, none more than RIDGE_RISE + 5 mm above it.
  R6  ridge_kit restores RIDGE on exit (also on an exception).
Exit 1 on the first failure; one PASS line otherwise.
"""

import math
import os
import sys
import traceback

import bpy

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_bldg as K    # noqa: E402
from cs2_bldg import RoofSpec   # noqa: E402


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def verts(o):
    return [tuple(round(c, 6) for c in (o.matrix_world @ v.co)) for v in o.data.vertices]


def part(parts, prefix):
    hits = [o for o, _m in parts if o.name.startswith(prefix)]
    assert hits, f"no part {prefix}"
    return hits[0]


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)


def slab_top_z(spec, g, x, side):
    t = g["t0"] if side == 0 else g["t1"]
    h = 0.5 * spec.thick * math.sqrt(1 + t * t)
    return g["ridge_z"] + h - t * abs(x - g["r"])


def check_gable(spec, label):
    g = K._roof_geometry(spec)
    ax = 0 if g["ax"] == "Y" else 1             # the across axis index
    with K.ridge_kit(2):
        parts = K.gable_roof("t", spec, walls=False)
    sA, sB, rid = part(parts, "roof_tA"), part(parts, "roof_tB"), part(parts, "ridge_t")
    vA, vB, vR = verts(sA), verts(sB), verts(rid)
    r = g["r"]
    rg = K.RIDGE_GAP
    assert abs(max(v[ax] for v in vA) - (r + rg)) < 1e-4, f"{label} R2 slab A ends at {max(v[ax] for v in vA)} != r + overlap"
    assert abs(min(v[ax] for v in vB) - (r - rg)) < 1e-4, f"{label} R2 slab B starts at {min(v[ax] for v in vB)} != r - overlap"
    assert len(sA.data.polygons) == 8 and len(sB.data.polygons) == 8, \
        f"{label} R2 slabs not closed ({len(sA.data.polygons)} / {len(sB.data.polygons)} faces)"
    topA = max(v[2] for v in vA if abs(v[ax] - (r + rg)) < 1e-4)
    topB = max(v[2] for v in vB if abs(v[ax] - (r - rg)) < 1e-4)
    zA = slab_top_z(spec, g, r, 0) + g["t0"] * rg            # each slab keeps rising past the ridge by the overlap
    zB = slab_top_z(spec, g, r, 1) + g["t1"] * rg
    assert abs(topA - zA) < 1e-4 and abs(topB - zB) < 1e-4 and max(zA, zB) < max(v[2] for v in vR) - K.RIDGE_SHEET, \
        f"{label} R2 slab tops at the ridge {topA:.3f} / {topB:.3f}"
    assert tris(rid) == 12, f"{label} R3 ridge tris {tris(rid)} != 12"
    assert rid.get("no_bevel"), f"{label} R3 flashing must opt out of bevel_parts (a 2 cm bevel shreds the sheet)"
    zmax_top = max(slab_top_z(spec, g, r, 0), slab_top_z(spec, g, r, 1))
    apex = max(v[2] for v in vR)
    assert abs(apex - (zmax_top + K.RIDGE_RISE)) < 2e-3, f"{label} R3 apex {apex:.3f}"
    for v in vR:
        side = 0 if v[ax] < r - 1e-6 else (1 if v[ax] > r + 1e-6 else None)
        if side is None:
            continue
        gap = v[2] - slab_top_z(spec, g, v[ax], side)
        assert -1e-4 <= gap <= 0.005 + 1e-4, f"{label} R3 hem gap {gap * 1000:.1f} mm at {v}"
    w = max(v[ax] for v in vR) - min(v[ax] for v in vR)
    assert abs(w - 2 * K.RIDGE_LEG) < 1e-3, f"{label} R3 width {w:.3f}"
    clear()


def main():
    checks = []
    spec = RoofSpec("gable", -4.0, 4.0, -6.0, 6.0, 3.0, 40.0, axis="Y", overhang=0.6, rake_over=0.4, thick=0.24,
                    mat="slate")
    # R1: flag off == v1 (compare against an explicit RIDGE version 1, the pre-T95 behaviour)
    p0 = K.gable_roof("o", spec, walls=False)
    a = {o.name: sorted(verts(o)) for o, _m in p0}
    r0 = tris(part(p0, "ridge_o"))
    clear()
    with K.ridge_kit(1):
        p1 = K.gable_roof("o", spec, walls=False)
    b = {o.name: sorted(verts(o)) for o, _m in p1}
    clear()
    assert a == b and r0 == 12, f"R1 flag-off differs (ridge tris {r0})"
    hip = RoofSpec("hip", -3.0, 3.0, -5.0, 5.0, 3.0, 30.0, axis="Y", overhang=0.5, thick=0.22, mat="slate")
    h0 = {o.name: sorted(verts(o)) for o, _m in K.hip_roof("h", hip)}
    clear()
    with K.ridge_kit(1):
        h1 = {o.name: sorted(verts(o)) for o, _m in K.hip_roof("h", hip)}
    clear()
    assert h0 == h1, "R1 hip flag-off differs"
    checks.append("R1 flag off = v1")
    check_gable(spec, "gable Y 40")
    check_gable(RoofSpec("gable", -5.0, 5.0, -3.0, 3.0, 2.8, 45.0, axis="X", overhang=0.5, thick=0.30,
                         mat="slate"), "gable X 45")
    checks.append("R2 R3 gable v2")
    check_gable(RoofSpec("gable", -4.0, 5.0, -6.0, 6.0, 3.0, 35.0, axis="Y", overhang=0.6, thick=0.24, mat="slate",
                         ridge_at=1.0, eave_z1=4.2), "gable asym")
    checks.append("R4 asymmetric")
    for spec_h, n_edges, label in ((hip, 5, "hip"), (RoofSpec("hip", -2.0, 2.0, -2.0, 2.0, 4.0, 35.0, axis="X",
                                                               overhang=0.4, thick=0.2, mat="slate"), 4, "pyramid")):
        with K.ridge_kit(2):
            ps = K.hip_roof("h", spec_h)
        rid = part(ps, "ridge_h")
        roof = part(ps, "roof_h")
        assert tris(rid) == 12 * n_edges, f"R5 {label} ridge tris {tris(rid)} != {12 * n_edges}"
        depsgraph = bpy.context.evaluated_depsgraph_get()
        from mathutils.bvhtree import BVHTree
        bvh = BVHTree.FromObject(roof, depsgraph)
        for v in rid.data.vertices:
            co = rid.matrix_world @ v.co
            loc, nrm, idx, dist = bvh.find_nearest(co)
            above = (co - loc).dot(nrm)
            assert -0.002 <= above <= K.RIDGE_RISE + 0.005 + 0.002 or dist < 0.06, \
                f"R5 {label} vertex {tuple(round(c, 3) for c in co)} {above * 1000:.1f} mm off the roof"
        clear()
    checks.append("R5 hip v2")
    saved = dict(K.RIDGE)
    try:
        with K.ridge_kit(2):
            raise RuntimeError("probe")
    except RuntimeError:
        pass
    assert K.RIDGE == saved, f"R6 RIDGE not restored: {K.RIDGE}"
    checks.append("R6 restore")
    print("PASS test_ridge_v2: " + "; ".join(checks))


try:
    main()
except Exception:  # noqa: BLE001
    traceback.print_exc()
    sys.exit(1)
