"""
Determinism gate for lift_terminal_detach.py (contract dq-terminal.md section 14 T1, the
test_terminal_determinism.py pattern).

For every role - DQ base, DQ top, gondola base, gondola top - the station is built TWICE in
one Blender process (geometry, bevels on, part tags, the per-part pack seed, the tiling UVs of
material_sources, join, remove_doubles, shading) and hashed: vertex coordinates (5 dp),
polygon vertex lists, the INT "part" and the material_index face attributes, the part table
and both UV layers (UVTile, UVSeed; 6 dp). Two builds must hash identically: the unique pack,
the AO bake and the LOD decimation downstream are all order-dependent. The first build of
each role also runs the authoring-frame geometry contracts (C1-C3, C6-C9, C13, and the
Revision 1 checks R1a-R1f of 2026-10-11: nose ladder, one underside ladder, gangplank, no
left-flank railing, sign band, housing windows; Revision 2 R2a-R2d of the second look: the
front ladder from an open-bottom ground footing, one 0.30 rung grid, an open ladder-way through
the landing, no cage, inboard of the hosted ETDQ; Revision 1c: on the base those run on the
ground flight beside the landing, and R2c gates the climbing room). Revision 1b (polish, 2026-10-11): the sign's
vector plate digits (bpy FONT curve -> triangles -> anti-aliased coverage, D.digit_coverage)
and its plate-edge relief / occlusion (D.sign_relief) are rebuilt from scratch twice per role
(caches cleared, a new FONT object each time) at the shipping 2048 density and must hash
identically too. Gondola stations draft 1 (2026-10-11): the gondola roles' first build runs
the G1-G8 contracts instead of R2 (portal silhouette vs the carrier boxes + 0.30, boarding
platforms, every walkable surface reachable from grade, nose-ladder climbing room and stair
headroom, glazing band, hosted-tower keep-out, portal-face sign, and the REAL shipped
SkiGondolaCabin8A swept through the hall with >= 0.30 m clearance). Exit 1 on any difference
or failed contract.

Detail pass 1 (2026-10-11, build/detail-pass-1-lifts-detach.md): the DQ builds now carry the dressing
(cs2_liftdress builders, label quads seeded at their reserved sheet rects, the pf hatch cut into the
soffit, the base's MC4 roof route) and their first build also runs R2e / R2f / R2g (one reachability
rule, MC3-MC5) and D8 / D9 (every dressing part and vanilla footprint box-tested, MC10); the gondola
roles carry the real door openings (G3) and the explicit G7 sign test. The hashes cover all of it
(the dressing's names, the seeded label rects and the ski colors by crc32 included).
Gondola revision 1 (2026-10-11, the long glazed hall, build/gondola-stations-rev1.md): the gondola
roles' first build runs G1-G10 (G5 the glazed hall and its room ids, G6 the real hosted entry tower,
G9 the 3 x 4 lot, G10 part clash + vanilla footprints); the DQ roles' hashes must not move.
DQ follow-up (2026-10-11, build/dq-stations-followup.md): ALL FOUR roles change on purpose (DQ: flood masts out,
SLOW boards on posts, the top's STAY SEATED before the unload line, D11 board order in the first build; gondola: the
doors' center post out, the door-head transom 2.60 -> 2.64, G3 clear-opening rays + the standable-top census).
The gate still compares two builds per role; the new hashes are recorded in the build report, none is pinned here.
Fine-detail step 3b (2026-10-12, build/fine-detail-step3b-stations.md): ALL FOUR roles change on purpose again
(FINE_DETAIL on by default: the kit's dressing leaves the host for "mod" rows, the DQ box lamps for vanilla rows, the
maze / gate / board posts and rods get their fine segments, the cabinets lose their bevel strips for a lid); the first
build also runs F1 (assert_kit_rows on the real kit meshes, which loads props/B FBXs mid-build and must leave the scene
as it was - the second build hashing identically proves it). The pre-pass (--no-fine) is proved byte-identical to the
approved exports by the build report's A/B, not here.
STAY SEATED removed at the top; DQ _Emissive dropped (2026-10-12, nineteenth look): the DQ TOP's geometry hash
moves on purpose (board_stay + 2 rods + label_stay leave the host: 331 -> 327 parts, 4614 -> 4578 vertices, 3778 -> 3759
polygons; the label sheet repacks without the STAY SEATED rect); the DQ BASE and both gondola roles must NOT move. This
gate PINS NO HASH (it compares two builds per role), so there is nothing to re-pin; the run lines are recorded in the
build report: before dq-base c7d8c71e519e, dq-top 3049bb5acf82, gd-base 6819b7548d41, gd-top db4042a8af7d; after
dq-base c7d8c71e519e (same), dq-top c2718cf4bdad (moved: STAY SEATED), gd-base 6819b7548d41 (same), gd-top db4042a8af7d
(same). Dropping the DQ _Emissive map is a bake / export change (has_emissive_map), invisible to this geometry hash.
Nitpick A3 -> A4 (2026-10-13, build/nitpick-a3-cabin-nose.md "Nose overlaps"): ONLY the DQ TOP moves, on purpose (the
guide beams and girder stubs 0.08 m outboard, the hanger boxes to their yokes' x; the same 348 parts, 4858 vertices,
4056 polygons; the first build now also runs C14 and the hanger gate of ST1). Recorded run lines (this gate pins no hash):
before dq-base c7d8c71e519e, dq-top cd00b1b8f115, gd-base 36ff32c45933, gd-top 90da6ee0826e; after dq-base c7d8c71e519e
(same), dq-top 42dab6a1e77c (moved), gd-base 36ff32c45933 (same), gd-top 90da6ee0826e (same). NOSE_A4_LEGACY = True
rebuilds the cd00b1b8f115 top.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_terminal_detach_determinism.py
"""
import hashlib
import json
import os
import sys
import time

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import lift_terminal  # noqa: E402
import lift_terminal_detach as D  # noqa: E402


def _arr(seq, attr, n, dtype, width=1):
    a = np.empty(n * width, dtype=dtype)
    seq.foreach_get(attr, a)
    return a


def build_hash(kind, gondola, check):
    lift_terminal.clear_scene()
    carrier = D.load_carrier("gd8" if gondola else "dq")
    p = D.params_for(kind, gondola, carrier)
    parts = D.build_station(p)
    t, table, _sources = D.assemble(parts, True, p)
    if check:
        D.assert_contracts(t, table, p)
    me = t.data
    h = hashlib.sha1()
    co = _arr(me.vertices, "co", len(me.vertices), np.float32, 3)
    h.update(("|".join("%.5f" % v for v in co)).encode())
    for pl in me.polygons:
        h.update((",".join(str(i) for i in pl.vertices) + ";").encode())
    h.update(D._face_parts(me).tobytes())
    h.update(_arr(me.polygons, "material_index", len(me.polygons), np.int32).tobytes())
    h.update(json.dumps(table).encode())
    for name in sorted(l.name for l in me.uv_layers):
        uv = _arr(me.uv_layers[name].data, "uv", len(me.loops), np.float32, 2)
        h.update(name.encode())
        h.update(("|".join("%.6f" % v for v in uv)).encode())
    return h.hexdigest()[:12], len(me.vertices), len(me.polygons), len(table)


def sign_raster_hash(kind, gondola):
    """Revision 1b: the sign rect's painted inputs at the shipping atlas, from scratch."""
    D._DIGIT_TRIS.clear()
    D._DIGIT_COV.clear()
    p = D.params_for(kind, gondola, D.load_carrier("gd8" if gondola else "dq"))
    lay = D.sign_layout(D.plate_digits(p))
    u0, v0, u1, v1 = D.sign_rects(p)["n"]
    w, h = int(round((u1 - u0) * D.ATLAS)), int(round((v1 - v0) * D.ATLAS))
    pu, pv = w / lay["W"], h / lay["H"]
    cov = D.digit_coverage(lay, w, h, pu, pv)
    height, occ = D.sign_relief(lay, h, w, pu, pv)
    hs = hashlib.sha1()
    hs.update(json.dumps(D.digit_triangles(lay)["tris"]).encode())
    for a in (cov, height, occ):
        hs.update(np.ascontiguousarray(a).tobytes())
    return hs.hexdigest()[:12], lay["digits"], round(float(cov.sum()), 1)


t0 = time.perf_counter()
ok = True
for kind, gondola in (("base", False), ("top", False), ("base", True), ("top", True)):
    role = ("gd-" if gondola else "dq-") + kind
    try:
        a = build_hash(kind, gondola, check=True)
        b = build_hash(kind, gondola, check=False)
    except Exception as e:  # noqa: BLE001 - a failed build or contract fails the gate
        import traceback
        traceback.print_exc()
        print(f"[det] {role}: FAILED {e!r}")
        ok = False
        continue
    same = a == b
    ok = ok and same
    print(f"[det] {role}: run1 {a} run2 {b} -> {'SAME' if same else 'DIFFERENT'}")
    try:
        sa, sb = sign_raster_hash(kind, gondola), sign_raster_hash(kind, gondola)
    except Exception as e:  # noqa: BLE001
        import traceback
        traceback.print_exc()
        print(f"[det] {role} sign raster: FAILED {e!r}")
        ok = False
        continue
    ok = ok and sa == sb
    print(f"[det] {role} sign raster (vector digits '{sa[1]}', {sa[2]} coverage texels, relief): "
          f"run1 {sa[0]} run2 {sb[0]} -> {'SAME' if sa == sb else 'DIFFERENT'}")
if not ok:
    print("FAIL test_terminal_detach_determinism: builds differ or a contract failed")
    sys.exit(1)
print(f"PASS test_terminal_detach_determinism: dq base/top and gondola base/top hash identically "
      f"across two builds (geometry + vector sign digits / plate edge), contracts hold "
      f"({time.perf_counter() - t0:.1f} s)")
