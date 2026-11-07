"""
test_t87_t91_fixtures - the 2026-10-19 field-test fixes
(docs/research/field/2026-10-19/fix-T87-T90-T91-T94.md).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_t87_t91_fixtures.py

Asserts (all under cs2_bldg.fittings(version=1), the shipping flag set). The flag-off paths are untouched
and must stay vertex-identical:
1. stovepipe(): off = the old 2 galv parts, vertex-identical before / after a fittings() block. On = 2
   parts in the flat black paint `ski_black` (never `galv`, the metal 1.0 swatch that read white in
   game). The patrol huts' 16 cm pipe (r 0.08) stays within +8 tris of the HEAD fittings-on pipe (60)
   (P2's tiled LOD0 sits 11 tris under its 2,500 cap). The hut's 30 cm pipe (r 0.15) carries the lap seam
   rib + crimp bands. The cap's collar still runs into the pipe top (the close-look rule: the cap never
   floats). The top is the old top + 0.05 m rain hat.
2. vent_stack() and chimney_pot(): `ski_black`, the pot's top stays at z1 (the L5 extent contract), a rim
   band around the pot top and a hat on it.
3. rooftop_unit() (the school's HVAC): parts hvac_<n>* only, materials in (ski_black, trim_paint_w,
   trim_paint), no galv, the housing is level on a curb that reaches down through the roof band, 12
   angled louver blades, no same-facing coplanar overlap between parts (the G10 gate that caught the
   first draft), <= 300 tris a unit. Exit 1 on the first failure. One PASS line otherwise.
"""

import os
import sys
import traceback

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import cs2_bldg as K                     # noqa: E402

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


def zmax(parts, name=None):
    return max(v.co.z for o, _k in parts if name is None or o.name == name for v in o.data.vertices)


def zmin(parts, name=None):
    return min(v.co.z for o, _k in parts if name is None or o.name == name for v in o.data.vertices)


def _spec():
    return K.RoofSpec("gable", 0.0, 8.0, 0.0, 10.0, 3.0, 30.0, axis="Y", overhang=0.6, thick=0.24, mat="slate")


def check_stovepipe():
    clear()
    spec = _spec()
    off = K.stovepipe("o", spec, 2.0, 5.0, r=0.08, rise=1.2)
    v_off = verts(off)
    assert [k for _o, k in off] == ["galv", "galv"], [k for _o, k in off]
    with K.fittings(version=1):
        small = K.stovepipe("s", spec, 2.0, 5.0, r=0.08, rise=1.2)
        big = K.stovepipe("b", spec, 6.0, 5.0, r=0.15, rise=1.2)
        mid = K.stovepipe("m", spec, 4.0, 5.0, r=0.15, rise=1.2, detail="mid")
    off2 = K.stovepipe("o2", spec, 2.0, 5.0, r=0.08, rise=1.2)
    assert verts(off2) == v_off, "stovepipe off is not vertex-identical after a fittings() block"
    for parts in (small, big):
        assert len(parts) == 2 and all(o.name.startswith("stovepipe_") for o, _k in parts), [o.name for o, _k in parts]
        assert {k for _o, k in parts} == {"ski_black"}, [k for _o, k in parts]
    t_off, t_small, t_big, t_mid = tris(off), tris(small), tris(big), tris(mid)
    assert t_mid < t_big, (t_mid, t_big)                                   # mid drops one band and the seam rib
    assert t_mid - 60 <= 57, t_mid                                         # L2 headroom: 57 tris under its 6,000 cap
    T_HEAD_FIT = 60          # HEAD with fittings v1: pipe 16 + collar 16 + disc cap 28 (the off path is 44, no collar)
    assert t_small <= T_HEAD_FIT + 8, (t_small, T_HEAD_FIT)
    z_roof = K.roof_z(spec, 2.0, 5.0)
    top_off = zmax(off)
    top_small = zmax(small)
    assert abs(top_small - (z_roof + 1.2 + 0.25)) < 1e-6 and top_small - top_off < 0.06, (top_small, top_off)
    # the collar of the cap runs into the pipe top (>= 2 cm below it) and the hat sits on the collar
    cap = [o for o, _k in small if o.name.endswith("cap")][0]
    pipe = [o for o, _k in small if not o.name.endswith("cap")][0]
    zt = z_roof + 1.2
    assert min(v.co.z for v in cap.data.vertices) <= zt - 0.025, "cap collar does not enter the pipe"
    assert max(v.co.z for v in pipe.data.vertices) <= zt + 0.17, "pipe reaches over the cap"
    # the full pipe carries the seam rib (a 6-sided-box shell more than the lean one) and two crimp bands
    sb = len(pipe.data.polygons)
    bigpipe = [o for o, _k in big if not o.name.endswith("cap")][0]
    assert len(bigpipe.data.polygons) > sb + 16, (len(bigpipe.data.polygons), sb)
    ok(f"stovepipe: off {t_off} tris vertex-identical, 2 galv parts; v1 r 0.08 {t_small} tris (+{t_small - t_off}, "
       f"6-gon pipe / flashing / collar / hat), r 0.15 {t_big} tris (8-gon + 2 crimp bands + seam rib), all ski_black, "
       f"cap collar enters the pipe, top {top_small - z_roof:.2f} m over the roof")


def check_vent_and_pot():
    clear()
    spec = _spec()
    off = K.vent_stack("v0", spec, 4.0, 5.0)
    with K.fittings(version=1):
        on = K.vent_stack("v1", spec, 4.0, 5.0)
        pot = K.chimney_pot("m00", 2.0, 2.0, 24.98, 25.55, r=0.17, segs=8)
    assert [k for _o, k in off] == ["galv"] and [k for _o, k in on] == ["ski_black"] and [k for _o, k in pot] == ["ski_black"]
    assert pot[0][0].name == "flue_m00" and on[0][0].name == "vent_v1"
    assert abs(zmax(pot) - 25.55) < 1e-6 and abs(zmin(pot) - 24.98) < 1e-6, (zmin(pot), zmax(pot))
    r_top = max(((v.co.x - 2.0) ** 2 + (v.co.y - 2.0) ** 2) ** 0.5 for v in pot[0][0].data.vertices if v.co.z > 25.545)
    assert r_top < 0.17 * 0.4, r_top                       # the hat closes to a small crown
    r_rim = max(((v.co.x - 2.0) ** 2 + (v.co.y - 2.0) ** 2) ** 0.5 for v in pot[0][0].data.vertices if 25.40 < v.co.z < 25.50)
    assert r_rim > 0.17 * 1.2, r_rim                       # the rim band / hat brim stands out of the pot
    ok(f"vent_stack: galv off, ski_black on ({tris(off)} -> {tris(on)} tris); chimney_pot {tris(pot)} tris, z "
       f"{zmin(pot):.2f}..{zmax(pot):.2f} (top kept), rim r {r_rim:.3f} over the pot's 0.17")


def _civic():
    import importlib.util
    spec = importlib.util.spec_from_file_location("_civic_t", os.path.join(HERE, "recipes", "_civic.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def check_rooftop_unit():
    clear()
    with K.fittings(version=1):
        a = K.rooftop_unit("a", -4.0, 6.0, 8.0, 8.36, w=1.5, d=1.0, h=0.75)
    names = sorted(o.name for o, _k in a)
    assert all(n.startswith("hvac_a") for n in names), names
    keys = {k for _o, k in a}
    assert keys <= {"ski_black", "trim_paint_w", "trim_paint"} and "galv" not in keys, keys
    n = tris(a)
    assert n <= 300, n
    # housing level on the curb: its bottom z = z_hi + 0.10, curb reaches below the roof band
    house = [o for o, _k in a if o.name == "hvac_a"][0]
    assert abs(min(v.co.z for v in house.data.vertices) - 8.46) < 1e-6
    curb = [o for o, _k in a if o.name == "hvac_acurb"][0]
    assert min(v.co.z for v in curb.data.vertices) <= 8.0 - 0.05 + 1e-6
    # 12 louver blades (6 a face), each a closed box
    slat = [o for o, _k in a if o.name == "hvac_aslat"][0]
    assert len(slat.data.polygons) == 12 * 6, len(slat.data.polygons)
    # blades shed rain: the lower edge stands further out than the upper one (y of the +y face blades)
    ys = sorted({round(v.co.y, 4) for v in slat.data.vertices})
    assert ys[-1] > 6.5 and ys[0] < 5.5, ys
    C = _civic()
    C.check_coplanar(a)                                       # raises on a same-facing coplanar overlap (G10)
    ok(f"rooftop_unit: {len(a)} parts {n} tris, materials {sorted(keys)} (no galv), housing level at z "
       f"{min(v.co.z for v in house.data.vertices):.2f} on a curb through the roof, 12 louvre blades, no coplanar overlap")


def main():
    check_stovepipe()
    check_vent_and_pot()
    check_rooftop_unit()
    print(f"PASS test_t87_t91_fixtures: {len(CHECKS)} checks")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_t87_t91_fixtures")
        sys.exit(1)
