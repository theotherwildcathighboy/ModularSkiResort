"""
test_fittings - fittings v1 contracts (docs/research/art/fittings-2026-10-17.md).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_fittings.py

Asserts:
  1. downspout() outside cs2_bldg.fittings() is the pre-fittings pipe vertex for vertex (no shoe, nothing
     registered); inside it the pipe loses its bottom face and gains the 10-tri shoe (+8), the outlet is
     registered in FIT_SHOES, kicks out along `normal` (or `shoe_dir`), and shoe=False builds no shoe; the
     context restores the off state;
  2. snow_guard() brackets inside fittings(): the top face is dropped and the whole bracket stays inside the
     rail's inscribed circle at its top (nothing that was visible is removed); 8 tris per bracket (was 10);
  3. snow_pads(): 6 tris per pad, staggered rows, every pad on the slab top (its foot within 6 mm of the
     roof's top plane); chimney_shroud(): 48 tris, above the crown;
  4. cs2_fittings.design_row: the facing quaternion matches the shipped rows after rule T (school wingL lamp
     on a -y wall = identity; L3 Bn lamp on a +x wall = game (0, -0.7071, 0, 0.7071)); FINAL <-> authoring
     round-trips for yaw 0 / 180.
Exit 1 on the first failure; one PASS line otherwise.
"""

import math
import os
import sys
import time
import traceback

import bpy
import bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_bldg as K                     # noqa: E402
import cs2_fittings as FIT               # noqa: E402
import fine_detail as FD                 # noqa: E402

CHECKS = []


def ok(msg):
    CHECKS.append(msg)
    print("  ok", msg)


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)


def tris(parts):
    return sum(len(p.vertices) - 2 for o, _k in parts for p in o.data.polygons)


def verts(parts):
    return sorted(tuple(round(c, 6) for c in v.co) for o, _k in parts for v in o.data.vertices)


def check_shoe():
    clear()
    off = K.downspout("t", 1.0, 2.0, 4.0, "+X", wall_gap=0.5, z_bottom=0.05)
    n_off, v_off = tris(off), verts(off)
    assert not K.FIT_SHOES, K.FIT_SHOES
    with K.fittings(version=1):
        on = K.downspout("t2", 1.0, 2.0, 4.0, "+X", wall_gap=0.5, z_bottom=0.05)
        assert len(K.FIT_SHOES) == 1, K.FIT_SHOES
        nm, p_out, d = K.FIT_SHOES[0]
        assert nm == "downspout_t2" and p_out[0] > 1.15 and abs(p_out[1] - 2.0) < 1e-6, (nm, p_out)
        sd = K.downspout("t3", 1.0, 2.0, 4.0, "-X", wall_gap=0.06, z_bottom=0.2, shoe_dir="+X")
        assert K.FIT_SHOES[-1][1][0] > 1.15, K.FIT_SHOES[-1]
        none = K.downspout("t4", 1.0, 2.0, 4.0, "+X", wall_gap=0.5, z_bottom=0.05, shoe=False)
        assert len(K.FIT_SHOES) == 2 and tris(none) == n_off, (len(K.FIT_SHOES), tris(none), n_off)
        lead = K.downspout("t5", 1.0, 2.0, 4.0, "-Y", z_bottom=0.05, leader=(0.5, 2.5))
        assert len(K.FIT_SHOES) == 3 and K.FIT_SHOES[-1][1][1] < 2.5 - 0.15, K.FIT_SHOES[-1]
    assert tris(on) == n_off + 8, (tris(on), n_off)
    assert K.FITTINGS["version"] == 0, "fittings() did not restore the off state"
    again = K.downspout("t6", 1.0, 2.0, 4.0, "+X", wall_gap=0.5, z_bottom=0.05)
    assert verts(again) == v_off, "downspout off is not vertex-identical after a fittings() block"
    ok(f"downspout: off {n_off} tris, vertex-identical before / after a fittings() block; on +8 (shoe 10, pipe "
       f"bottom -2), outlet registered and kicking out along normal / shoe_dir / the leader's normal; shoe=False none")


def _spec():
    return K.RoofSpec("gable", 0.0, 8.0, 0.0, 10.0, 3.0, 30.0, axis="Y", overhang=0.6, thick=0.24, mat="slate")


def check_brackets_and_pads():
    clear()
    spec = _spec()
    off = K.snow_guard("s1", spec, "+X")
    with K.fittings(version=1):
        on = K.snow_guard("s2", spec, "+X")
        pads = K.snow_pads("p", spec, "+X", offsets=(1.4, 2.0), every=0.75)
    b_off = [o for o, _k in off if o.name == "snowguard_s1b"][0]
    b_on = [o for o, _k in on if o.name == "snowguard_s2b"][0]
    rail = [o for o, _k in on if o.name == "snowguard_s2"][0]
    nb = len(b_on.data.polygons) // 4
    assert len(b_off.data.polygons) == 5 * nb and len(b_on.data.polygons) == 4 * nb, \
        (len(b_off.data.polygons), len(b_on.data.polygons))
    # rail axis and radius (6-seg r 0.02: inscribed 0.0173); every bracket vertex at its top must sit inside it
    rv = [v.co for v in rail.data.vertices]
    n, up, along = K.roof_frame(spec, "+X")
    c0 = sum(rv, Vector()) / len(rv)
    worst = 0.0
    for v in b_on.data.vertices:
        d = v.co - c0
        h = d.dot(n)
        if h > -1e-6:                      # the top ring of the bracket
            r = math.hypot(d.dot(up), h)
            worst = max(worst, r)
    assert worst <= 0.02 * math.cos(math.pi / 6) + 1e-6, worst
    # pads: 6 tris each, feet on the slab top plane
    po = pads[0][0]
    npad = len(po.data.polygons) // 4
    assert tris(pads) == 6 * npad and npad > 20, (tris(pads), npad)
    g = K._roof_geometry(spec)
    et, zt = K._eave_tip(spec, g, 1)
    top_off = 0.5 * spec.thick * math.sqrt(1 + g["t1"] ** 2)
    plane_pt = Vector((et, 0.0, zt + top_off))
    feet = [v.co for v in po.data.vertices if abs((v.co - plane_pt).dot(n)) < 0.02]
    assert feet and max(abs((p - plane_pt).dot(n)) for p in feet) <= 0.006, "pad feet off the slab top"
    ok(f"snow guard brackets 10 -> 8 tris ({nb} brackets), the top inside the rail (max {worst * 1000:.1f} mm of "
       f"{0.02 * math.cos(math.pi / 6) * 1000:.1f}); {npad} pads x 6 tris on the slab top")
    sh = K.chimney_shroud("c", (0.0, 0.0, 9.0), (1.4, 1.8, 9.28))
    zmin = min(v.co.z for o, _k in sh for v in o.data.vertices)
    assert tris(sh) == 48 and zmin >= 9.28 - 1e-6 and sh[0][0].name == "fit_shroudc", (tris(sh), zmin)
    ok("chimney shroud: 48 tris, standing on the crown, part fit_shroud<name> (flat, LOD1-deleted)")
    assert "fit_" in K.LOD1_DELETE


class _R:
    def __init__(self, yaw, key="t"):
        self.yaw_deg, self.key = yaw, key


def check_rows():
    # school wingL lamp: FINAL wall facing -y -> identity; game row after rule T stays identity
    r = FIT.design_row("WallLightShortRangeRandom01", "x" * 32, (-3.5, -16.0, 3.02), (0.0, -1.0))
    assert r[3] == (0.0, 0.0, 0.0, 1.0) and FD.rule_t_pos(r[2]) == (3.5, 3.02, 16.0), r
    # L3 Bn lamp: FINAL wall facing +x -> game q (0, -0.7071068, 0, 0.7071068) (the shipped row)
    r = FIT.design_row("WallLightShortRangeRandom01", "x" * 32, (5.0, -6.0, 2.3), (1.0, 0.0))
    g = FD.rule_t_rot(r[3])
    assert abs(g[1] + 0.7071068) < 1e-6 and abs(g[3] - 0.7071068) < 1e-6, g
    assert FD.rule_t_pos(r[2]) == (-5.0, 2.3, 6.0), FD.rule_t_pos(r[2])
    for yaw in (0.0, 180.0):
        rec = _R(yaw)
        p = (3.0, -7.0, 1.0)
        q = FIT.final_to_auth(rec, FIT.auth_to_final(rec, p))
        assert all(abs(a - b) < 1e-9 for a, b in zip(p, q)), (yaw, q)
    ok("design rows: facing q and position match the shipped school / L3 rows through rule T; frames round-trip")


def main():
    t0 = time.perf_counter()
    check_shoe()
    check_brackets_and_pads()
    check_rows()
    print(f"PASS test_fittings: {len(CHECKS)} checks ({time.perf_counter() - t0:.1f} s)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_fittings")
        sys.exit(1)
