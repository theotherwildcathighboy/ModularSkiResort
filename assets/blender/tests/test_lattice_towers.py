"""
Gate for tram_tower.py (tram 30 / 45 / 60 + gondola lattice 25 / 32 / 40): one headless process,
exit 1 on any failure. Nothing is written into the repo (the bake smoke uses a temp folder).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_lattice_towers.py

Groups (lattice-towers.md section 14, adapted to the R27-R31 rulings):
  1 determinism   tram 60 and gondola 40 built twice: vertex coords, faces, UVMap loops, part table
  2 contracts     every variant, authoring + net frame: bounds top / bottom, H_top, saddle crown /
                  tips (no burial), haul-roller tops, lane offsets (+-5.5 / +-5.0 / +-3.0), axle =
                  datum, R30 + carrier zones, rope corridors, leg feet, ladder, symmetry; LOD chain by
                  deletion (targets exact, drift 0, budgets), footings open-bottom at z 0
  3 negatives     a part in the R30 band, a part in the carriage band, a closed-bottom pedestal and a
                  buried saddle must each FAIL
  4 scoring       D13 variant pick: tram switches 10 / 25, float <= 10, burial <= 5, stub = node + 26.25;
                  gondola family 12/16/20 (provisional tubular) + 25/32/40
  5 bake smoke    tram 30 through cs2_bake at 256 px with --beacon (emissive only on the lens) and the
                  shipping path (no beacon, no _Emissive) on gondola 25: maps, channel rules (R + G + B
                  <= 1), recolour level p95 0.78-0.82 under every present mask; temp folder removed

Revision 1 (Richard's verdicts 7 / 8, 2026-10-11; build/tram_tower.md "Revision 1"):
  2r ladder       both families: rails from below grade (revision 1c: the pier depth with rungs on the
                  variants that float - 30A / 45A 10.0, 25O 4.66, 32O 5.33 m - else LADDER_FAM sink
                  2.2 / 3.0, deeper than the face-normal drop at the piers' grade allowance, with rungs
                  down to -1.85 (60A) / -2.15 (40O, its 4,500-tri cap) - revision 1d; first
                  above-grade rung still 0.234) to >= arrival + 1.0, flights offset
                  >= 0.70, one side basket per flight break (never over the flight below: corridor
                  clear), flights <= 10.5; C11 exempts only the ladder prefixes
  2r tram head    curved saddle girder (L 4.8, tip drop >= 0.20, depth 0.70); >= 6 haul sheaves per
                  saddle on the haul line, no straight roller beam; upper stabilising frame: 4 hanger
                  posts crossarm -> top beam, main diagonals inner-post top -> crossarm end, top-beam
                  tips overhang each lane with a lug centred on it; a crane jib per side reaching over
                  its lane with its hook block centred over it
  2r gondola head lugs under both gantry-beam tips, centred on the lanes
  2r frozen       bounds lateral / travel within +-0.2 of the integration table; m_LegSize as shipped
  3r negatives    a centred landing over a flight, flights not offset, a ladder that stops short of
                  the deck, rails that end at grade (the revision-1 build, F1: tram 60 and, revision
                  1c N7, gondola 40), the 1b ladder on a floatable 30A / 32O (rails to the 1b sink only)
                  and a floatable 30A with bare rails below grade (no rungs) must each FAIL C7

Detail pass 1 (design/detail-pass-1-towers.md 6.1 DT-C1..C12 + skeptic M1-M11; build report
build/detail-pass-1-towers-lattice.md):
  1d determinism  two builds: vertices, faces, every UV layer, part table, corner normals, LOD1 / LOD2
  2d per variant  DT-C1 frozen prefab / carving blocks (canonical-JSON sha1 = the revision-1d
                  manifests), bounds +-0.5 mm, HEIGHTS / TRAM / GD repr hashes; DT-C2 LOD0 = the design
                  ledger +-1 % (60A minus the 52-tri lens, M8), LOD1 = 1d - the hidden-face reclaim,
                  LOD2 = 1d; DT-C3..C6 (check_dp1_geometry: coplanar <= 1 cm2 per pair on LOD0 / 1 / 2,
                  no hidden contact tri, texel anisotropy whole + per class >= 0.5 m2, detail part counts,
                  plate offsets, arrest front <= 0.036, collars, BVH); M6 smooth members keep ONE normal
                  per vertex on every LOD; DT-C12 night rows (60A only, the WarningLight on gantryx_1)
  3d negatives    headx_ 0.22 (DT-C3), a buried box face (DT-C4), create_cone post UVs (DT-C5), an
                  arrest rail beyond rung plane + 0.036 / collars 0.08 proud (DT-C6), an UNREGISTERED
                  arrest rail (C7 via M5), the stock make_lod_chain LODs (faceted members, M6)
  5d shipping bake gondola 25 at 1024 (weather on, family intensity): DT-C7 stamps (sizes, ControlMask
                  0, M3 unmirrored UVs on both plates), no face samples a black texel (sub-texel fill), DT-C8 relief drawn / no floor skips, DT-C9 tone
                  (restated by M7), DT-C10 weathering (budget ok, guards, free slots, no snow; ControlMask
                  PNG byte-identical to a --weather off bake; plates byte-identical); DT-C11 a child
                  Blender process bakes the same folder byte-identically (PNG + settings.json)
"""
import hashlib
import json
import math
import os
import subprocess
import sys
import tempfile
import time
from types import SimpleNamespace

import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import cs2_asset  # noqa: E402
import cs2_kit  # noqa: E402
import tram_tower as tt  # noqa: E402

FAILS = []
OKS = []
T0 = time.perf_counter()
# gondola-tram-integration.md 10.4 (the contract numbers Revision 1 must keep): (lateral, travel)
# bound half extents in the authoring frame (y, x) and StandingObject m_LegSize
BOUNDS_INT = {("tram", 30): (6.0, 5.7), ("tram", 45): (6.2, 6.2), ("tram", 60): (6.7, 6.7),
              ("gondola", 25): (3.32, 2.44), ("gondola", 32): (3.32, 3.06), ("gondola", 40): (3.32, 3.68)}
# revision 1c (revision1b-check-tram N1): how far each variant's base floats when the D13 score picks
# it, over the family's elevation range (tram 0..30, gondola 0..28; 0.01 sweep)
FLOAT_WANT = {("tram", 30): 10.0, ("tram", 45): 10.0, ("tram", 60): 0.0,
              ("gondola", 25): 4.66, ("gondola", 32): 5.33, ("gondola", 40): 0.0}
# revision 1d (revision1c-check-tram-ladder R2): lowest below-grade rung centre on the never-floaters
SINK_RUNG_WANT = {("tram", 60): -1.85, ("gondola", 40): -2.15}
LEGSIZE = {("tram", 30): [11.4, 17.35, 11.4], ("tram", 45): [12.4, 29.2, 12.4], ("tram", 60): [13.4, 44.2, 13.4],
           ("gondola", 25): [4.5, 17.5, 4.5], ("gondola", 32): [5.3, 24.5, 5.3], ("gondola", 40): [6.1, 32.5, 6.1]}


def check(ok, msg):
    print(("ok   " if ok else "FAIL ") + msg)
    (OKS if ok else FAILS).append(msg)


def expect_fail(fn, code, label):
    try:
        fn()
    except tt.ContractError as e:
        check(str(e).startswith(code), f"negative {label}: raised {str(e)[:110]}")
        return
    check(False, f"negative {label}: no ContractError raised")


def geom_hash(t, table):
    """Vertices, faces, EVERY UV layer, the part table and the corner normals (DT-C11)."""
    me = t.data
    h = hashlib.sha1()
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    h.update(" ".join("%.5f" % v for v in co).encode())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    for lay in sorted(l.name for l in me.uv_layers):
        uv = np.empty(len(me.loops) * 2, dtype=np.float64)
        me.uv_layers[lay].data.foreach_get("uv", uv)
        h.update((lay + ":" + " ".join("%.6f" % v for v in uv)).encode())
    cn = np.empty(len(me.loops) * 3, dtype=np.float64)
    me.corner_normals.foreach_get("vector", cn)
    h.update(" ".join("%.4f" % v for v in cn).encode())
    h.update("|".join(table).encode())
    return h.hexdigest()[:12], len(me.vertices), len(me.polygons)


def smooth_member_normals(obj, table, family):
    """M6: the largest number of distinct corner normals at one vertex of a smooth lattice member's
    side faces (1 = smooth round-tube read; 2 = faceted), over every SMOOTH_MEMBERS part present."""
    me = obj.data
    pid = np.zeros(len(me.polygons), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", pid)
    cn = np.empty(len(me.loops) * 3, dtype=np.float64)
    me.corner_normals.foreach_get("vector", cn)
    cn = cn.reshape(-1, 3)
    smooth = {i + 1 for i, nm in enumerate(table) if nm.startswith(tt.SMOOTH_MEMBERS[family])}
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    axis = {}
    for pi_ in smooth:                       # the member axis = the principal direction of its vertices
        vs = sorted({v for p in me.polygons if pid[p.index] == pi_ for v in p.vertices})
        if len(vs) < 3:
            continue
        X = co[vs] - co[vs].mean(axis=0)
        axis[pi_] = np.linalg.svd(X, full_matrices=False)[2][0]
    per = {}
    for p in me.polygons:
        pi_ = int(pid[p.index])
        if pi_ not in axis or abs(float(np.dot(p.normal, axis[pi_]))) > 0.3:
            continue                         # caps / mitre ends stay sharp by design
        for li in range(p.loop_start, p.loop_start + p.loop_total):
            key = (int(pid[p.index]), me.loops[li].vertex_index)
            per.setdefault(key, set()).add(tuple(np.round(cn[li], 3)))
    return max(len(v) for v in per.values()) if per else 0


def build(family, H, args=None):
    tt.clear_scene()
    t, table, spec, parts, lay, _src = tt.build_variant(family, H, args, finish=False)
    return t, table, spec, parts, lay


# --------------------------------------------------------------------------------------
# 1. determinism
# --------------------------------------------------------------------------------------
def lod_hashes(family, H):
    t, table, spec, parts, lay = build(family, H)
    h0 = geom_hash(t, table)
    cs2_asset.orient_for_net(t)
    l1p, l2p = tt.lod_lists(family)
    n1, n2 = tt.count_after_delete(t, table, l1p), tt.count_after_delete(t, table, l2p)
    lods = tt.make_lods(t, tt.variant_name(family, H), table, parts, family, n1, n2)
    out = (h0,) + tuple(geom_hash(l, table) for l in lods)
    for l in lods:
        me = l.data
        bpy.data.objects.remove(l, do_unlink=True)
        bpy.data.meshes.remove(me)
    return out


for family, H in (("tram", 60), ("gondola", 40)):
    a = lod_hashes(family, H)
    b = lod_hashes(family, H)
    check(a == b, f"determinism {family} {H} (LOD0 / 1 / 2: vertices, faces, UV layers, part table, corner "
                  f"normals; DT-C11): {a} vs {b}")

# detail pass 1 golden numbers (the revision-1d shipped manifests, read 2026-10-11 before the pass)
GOLDEN = {("gondola", 25): ("0222115d5dec", "c8377be6e02c", [-2.44, -3.32, -8.0], [2.44, 3.32, 25.0]),
          ("gondola", 32): ("88b8646a55f5", "c8377be6e02c", [-3.06, -3.32, -8.0], [3.06, 3.32, 32.0]),
          ("gondola", 40): ("62ebbac52624", "c8377be6e02c", [-3.68, -3.32, -8.0], [3.68, 3.32, 40.0]),
          ("tram", 30): ("1470792325b5", "0e3abc98730b", [-5.7, -6.0, -12.0], [5.7, 6.0, 30.0]),
          ("tram", 45): ("940a281ed2c0", "0e3abc98730b", [-6.2, -6.2, -12.0], [6.2, 6.2, 45.0]),
          ("tram", 60): ("a771ebdc4f6c", "0e3abc98730b", [-6.7, -6.7, -12.0], [6.7, 6.7, 60.0])}
FROZEN_REPR = {"HEIGHTS": "ce12d43af54f", "TRAM": "361080e8b49c", "GD": "35d6b1fb03df"}
# design ledgers 4.1 / 5.1 (60A: 6,454 minus the 52 tris of the lens + ring the design kept there;
# M8 option 1 drops them); +-1 %
DP1_LEDGER = {("gondola", 25): 3316, ("gondola", 32): 3986, ("gondola", 40): 4490,
              ("tram", 30): 4606, ("tram", 45): 5666, ("tram", 60): 6402}
PRE_LOD12 = {("gondola", 25): (938, 302), ("gondola", 32): (1058, 334), ("gondola", 40): (1114, 334),
             ("tram", 30): (1748, 456), ("tram", 45): (2228, 456), ("tram", 60): (2516, 488)}
# hidden-contact reclaim on parts that LOD1 keeps (trans_ ends 16, rocksub inner faces 2 per sub,
# gdstring tops 8; tram: the deck lip strips on the chords 4); LOD2 keeps none of them
LOD1_RECLAIM = {("gondola", 25): 40, ("gondola", 32): 44, ("gondola", 40): 48,
                ("tram", 30): 4, ("tram", 45): 4, ("tram", 60): 4}


def canon(block):
    return hashlib.sha1(json.dumps(json.loads(json.dumps(block, default=str)), sort_keys=True,
                                   separators=(",", ":")).encode()).hexdigest()[:12]


for nm, want in FROZEN_REPR.items():
    got = hashlib.sha1(repr(getattr(tt, nm)).encode()).hexdigest()[:12]
    check(got == want, f"DT-C1 repr hash of {nm} {got} unchanged ({want})")

# --------------------------------------------------------------------------------------
# 2. contracts per variant
# --------------------------------------------------------------------------------------
TOPS = {}
for family in tt.FAMILIES:
    for H in tt.HEIGHTS[family]:
        t, table, spec, parts, lay = build(family, H)
        name = tt.variant_name(family, H)
        t.name = name
        rep, mv = tt.check_contracts(t, table, spec, parts, "authoring")
        V = mv.V
        TOPS[(family, H)] = float(V[:, 2].max())
        # ---- detail pass 1: DT-C1 frozen, DT-C3..C6 geometry, DT-C12 night ----
        gp, gc, bmin, bmax = GOLDEN[(family, H)]
        check(canon(tt.prefab_block(spec, lay)) == gp and canon(tt.carving_block(spec)) == gc,
              f"{name} DT-C1 prefab / carving blocks canonical-JSON equal to revision 1d ({gp} / {gc})")
        dmin = np.abs(V.min(axis=0) - np.array(bmin)).max()
        dmax = np.abs(V.max(axis=0) - np.array(bmax)).max()
        check(dmin <= 5e-4 and dmax <= 5e-4,
              f"{name} DT-C1 bounds within 0.5 mm of revision 1d (min {dmin * 1000:.3f} / max {dmax * 1000:.3f} mm)")
        try:
            g = tt.check_dp1_geometry(t, table, spec, parts, lay, uv_layer="UVMap", net=False)
            check(True, f"{name} DT-C3..C6 LOD0: coplanar {g['coplanar_same_cm2']['total']} cm2 (worst pair "
                        f"{g['coplanar_same_cm2']['worst_pair']}), hidden {g['hidden_contact_tris']}, aniso "
                        f"{g['anisotropy']['median']} / {g['anisotropy']['p95']}, parts {g['detail_parts']}, plates "
                        f"{g['plate_p_mm']} mm, arrest front {g.get('arrest_front_m')}, collars "
                        f"{g.get('collar_proud_m')}, BVH clashes {len(g['bvh_clashes'])}")
        except tt.ContractError as e:
            check(False, f"{name} DT-C3..C6: {e}")
        night = tt.night_rows(spec, t, table)
        if family == "tram" and H == 60:
            check(night is not None and len(night) == 1 and night[0]["guid"] == "6b6861547d4e1964aa977434af734126"
                  and abs(night[0]["position_net_m"][1] - 59.96) < 1e-3 and night[0]["position_net_m"][0] == 0.0,
                  f"{name} DT-C12 one WarningLightRandom01 row at the top centre on gantryx_1 "
                  f"({night[0]['position_net_m'] if night else None})")
        else:
            check(night is None, f"{name} DT-C12 no night sub-object")
        check(not any(n.startswith("beacon") for n in table), f"{name} DT-C12 no beacon_ parts (M8: the lens is dropped)")
        check(abs(V[:, 2].max() - H) < 1e-3, f"{name} bounds top {V[:, 2].max():.4f} == {H}")
        pier = tt.PIER_TR if family == "tram" else tt.PIER_GD
        check(abs(V[:, 2].min() + pier) < 1e-4, f"{name} bounds bottom {V[:, 2].min():.4f} == -{pier}")
        if family == "tram":
            z0 = spec.z0
            check(abs(z0 - (H - 3.75)) < 1e-9, f"{name} track-rope datum {z0:.2f} = H - 3.75")
            for tag, s in (("p", 1), ("n", -1)):
                sv = mv.verts(f"saddle_{tag}")
                yc = (sv[:, 1].min() + sv[:, 1].max()) / 2
                check(abs(yc - s * 5.5) < 1e-3, f"{name} saddle_{tag} on the HV lane y {yc:+.3f} (R28 +-5.5)")
                crown = sv[np.abs(sv[:, 0]) < 1e-4][:, 2].max()
                check(abs(crown - (z0 - 0.10)) < 1e-4, f"{name} saddle_{tag} crown {crown:.4f} = rope - 0.10")
                for kk, x in enumerate(tt.ROLLER["x"]):
                    rv = mv.verts(f"haulroll_{tag}_{kk}")
                    want = z0 - 0.40 - 0.06 - spec.s0 * abs(x)
                    check(abs(rv[:, 2].max() - want) < 5e-3 and
                          abs((rv[:, 1].min() + rv[:, 1].max()) / 2 - s * 5.0) < 1e-3,
                          f"{name} haulroll_{tag}_{kk} top {rv[:, 2].max():.4f} = haul rope bottom, y +-5.0")
            # head clearance (R30): nothing outboard of lane + 0.36 at dz -0.80..+0.60, + 0.80 below
            for s in (1, -1):
                dy = s * V[:, 1] - 5.5
                dz = V[:, 2] - z0
                bad_a = ((dy > 0.36 + 1e-4) & (dz > -0.80) & (dz < 0.60)).sum()
                bad_b = ((dy > 0.80 + 1e-4) & (dz > -3.80) & (dz < -0.70)).sum()
                check(bad_a == 0 and bad_b == 0, f"{name} R30 head clearance side {s:+d}: "
                                                 f"{bad_a} / {bad_b} vertices outside")
            # body band (R27: underside rope - 10.00, |dy| <= 1.90, dz -10..-7): empty at level
            inb = 0
            for s in (1, -1):
                dy = s * V[:, 1] - 5.5
                dz = V[:, 2] - z0
                inb += int(((np.abs(dy) < 1.90) & (dz > -10.0) & (dz < -7.0)).sum())
            check(inb == 0, f"{name} cabin body band empty ({inb} vertices)")
            grate = [n for n in table if n.startswith("grate_deck")]
            rails = [n for n in table if n.startswith("rail_deck")]
            hrail = max(mv.verts(n)[:, 2].max() for n in rails) - (z0 - 1.17)
            check(bool(grate) and abs(hrail - 0.85 - 0.025) < 0.01,
                  f"{name} walkway + handrail {hrail - 0.025:.3f} m (0.85)")
            # Revision 1, V8b: one long curved saddle girder per lane + a haul-sheave row (JH-15)
            for tag in ("p", "n"):
                sv = mv.verts(f"saddle_{tag}")
                L = sv[:, 0].max() - sv[:, 0].min()
                mid = sv[np.abs(sv[:, 0]) < 1e-4]
                end = sv[np.abs(sv[:, 0] - sv[:, 0].max()) < 1e-4]
                drop = mid[:, 2].max() - end[:, 2].max()
                depth = mid[:, 2].max() - mid[:, 2].min()
                check(abs(L - tt.SADDLE_L_DEFAULT) < 1e-3 and drop >= 0.20 and abs(depth - tt.SADDLE["d"]) < 1e-3,
                      f"{name} saddle_{tag} one curved box girder: L {L:.2f}, crown - tip {drop:.3f} >= 0.20, "
                      f"depth {depth:.2f}")
            nsh = len([n for n in table if n.startswith("haulroll_p_")])
            check(nsh >= 6 and not any(n.startswith("haulbeam_") for n in table),
                  f"{name} haul rope on a row of {nsh} sheaves per saddle (JH-15: >= 6), no straight roller beam")
            # V7b upper stabilising frame
            hang = [n for n in table if n.startswith("sbrkhang_") and not n.startswith("sbrkhang_t")]
            ok = len(hang) == 4
            for n in hang:
                hv = mv.verts(n)
                ok &= abs(hv[:, 2].min() - (z0 - 1.20)) < 1e-3 and abs(hv[:, 2].max() - (z0 + 3.45)) < 1e-3
                ok &= abs(abs(hv[:, 1].mean()) - tt.UPPER["hang_y"]) < 1e-3
            check(ok, f"{name} 4 hanger posts from the crossarm ends (z0 - 1.20) to the top beams (z0 + 3.45) "
                      f"at y +-{tt.UPPER['hang_y']}")
            gb = np.concatenate([mv.verts(n) for n in table if n.startswith("gantrybeam_")])
            lugs = [mv.verts(n) for n in table if n.startswith("lug_")]
            ok = gb[:, 1].max() >= 5.5 + 0.30 and gb[:, 1].min() <= -5.8 and len(lugs) == 4
            for lv in lugs:
                ok &= abs(abs(lv[:, 1].mean()) - 5.5) < 1e-3 and abs(lv[:, 2].max() - (z0 + 3.45)) < 1e-3
            check(ok, f"{name} top beams overhang both lanes (tips y +-{gb[:, 1].max():.2f}), a lifting lug centred "
                      f"over each lane under each tip")
            check(len([n for n in table if n.startswith("sbrkdiag_")]) == 4,
                  f"{name} 4 main diagonals inner-post top -> crossarm end")
            # V8c evacuation cranes: jib over its lane, hook block centred over the lane, above the carriage
            ok = True
            for tag, s in (("p", 1), ("n", -1)):
                jv = mv.verts(f"cranejib_{tag}")
                hb = mv.verts(f"cranehook_b{tag}")
                ok &= (s * jv[:, 1]).max() >= 5.5 + 0.30 and abs(hb[:, 1].mean() - s * 5.5) < 1e-3
                ok &= hb[:, 2].min() >= z0 + 2.5 and jv[:, 2].max() <= H + 1e-6
            check(ok, f"{name} a slewing jib crane per side reaching over its lane, hook block over the lane "
                      f">= z0 + 2.5")
        else:
            A = spec.z0
            check(abs(A - (H - 2.0)) < 1e-9, f"{name} axle datum {A:.2f} = H - 2.0")
            for tag, s in (("p", 1), ("n", -1)):
                names = mv.names(f"sheave_{tag}_")
                check(len(names) == tt.SHEAVES[H] and 8 <= len(names) <= 12,
                      f"{name} side {tag}: {len(names)} sheaves (8-12)")
                ok = True
                for nm in names:
                    v = mv.verts(nm)
                    c = (v.min(axis=0) + v.max(axis=0)) / 2
                    ok &= abs(c[2] - A) < 1e-3 and abs(c[1] - s * 3.0) < 1e-3
                check(ok, f"{name} side {tag}: every sheave axle at z {A:.2f} (= datum), y {s * 3.0:+.1f}")
            inb = 0
            for s in (1, -1):
                dy = s * V[:, 1] - 3.0
                dz = V[:, 2] - A
                inb += int(((dy > -1.05) & (dy < 1.10) & (dz > -4.50) & (dz < -1.92)).sum())
            check(inb == 0, f"{name} cabin body band empty at level ({inb} vertices)")
            ok = True
            for tag, s in (("p", 1), ("n", -1)):
                lv = mv.verts(f"lug_{tag}")
                ok &= abs(lv[:, 1].mean() - s * 3.0) < 1e-3 and abs(lv[:, 2].max() - (A + 1.76)) < 1e-3
            check(ok, f"{name} lifting lugs under both gantry-beam tips, centred on the lanes (JH-15 lesson)")
        # ---- Revision 1 (verdicts 7 / 8): the ladder system, both families ----
        lad, plan = rep["ladder"], parts.ladder
        z_arr = tt.ladder_arrival(spec)
        r0 = np.concatenate([mv.verts(f"lrail_0_{t_}") for t_ in "pn"])
        rl = np.concatenate([mv.verts(f"lrail_{len(plan['flights']) - 1}_{t_}") for t_ in "pn"])
        # revision 1c (revision1b-check-tram N1 / N2): a variant the score floats runs rails AND rungs
        # to the pier depth; the others keep rails to the (raised) sink - and, revision 1d
        # (revision1c-check-tram-ladder R2), rungs down it to SINK_RUNG_WANT (centre; vertex 0.016 lower)
        fl_want = FLOAT_WANT[(family, H)]
        depth = pier if fl_want > 0 else tt.LADDER_FAM[family]["sink"]
        rung_lo = min(float(mv.verts(n_)[:, 2].min()) for n_ in mv.names("rung_0_"))
        rung_up = float(mv.verts("rung_0_0")[:, 2].min())
        check(abs(lad["variant_float_m"] - fl_want) < 1e-6 and abs(r0[:, 2].min() + depth) < 1e-6
              and lad["rail_foot_z"] <= -min(lad["foot_need_m"], pier) + 1e-6
              and (rung_lo <= -depth + 0.25 + 0.30 + 1e-6 if fl_want > 0
                   else abs(rung_lo - (SINK_RUNG_WANT[(family, H)] - 0.016)) < 2e-3)
              and abs(rung_up - 0.234) < 2e-3 and rl[:, 2].max() >= z_arr + 1.0 - 1e-6,
              f"{name} ladder rails from z {r0[:, 2].min():.3f}, rungs from {rung_lo:.3f} (revision 1c: max float "
              f"{lad['variant_float_m']} + face-normal drop {lad['foot_drop_at_pier_grade_m']} = "
              f"{lad['foot_need_m']} m, pier {pier}, short {lad['foot_short_at_pier_bound_m']}; first "
              f"above-grade rung {rung_up:.3f}) to {rl[:, 2].max():.2f} >= arrival {z_arr:.2f} + 1.0 "
              f"({plan['face']} face, verdict 7)")
        lines = [f["line"] for f in plan["flights"]]
        offs = [round(abs(a - b), 3) for a, b in zip(lines, lines[1:])]
        check(lad["baskets"] == lad["flights"] - 1 and lad["baskets"] >= 1 and min(offs) >= 0.70 - 1e-9,
              f"{name} {lad['flights']} flights / {lad['baskets']} side baskets, flight offsets {offs} >= 0.70 "
              f"(verdict 8)")
        check(max(lad["lengths_m"]) <= 10.5 + 1e-9 and lad["corridor_margin_m"] >= 0.0,
              f"{name} flights {lad['lengths_m']} <= 10.5, every climbing corridor clear "
              f"(margin {lad['corridor_margin_m']} m)")
        check(set(rep["symmetry"]["exempt_prefixes"]) == set(tt.LADDER_ASYM),
              f"{name} C11 exempts only the ladder system {rep['symmetry']['exempt_part_kinds']}")
        # frozen prefab numbers (gondola-tram-integration 10.4): bounds within +-0.2, leg size, datum
        lat, trav = BOUNDS_INT[(family, H)]
        blat, btrav = float(np.abs(V[:, 1]).max()), float(np.abs(V[:, 0]).max())
        check(abs(blat - lat) <= 0.2 and abs(btrav - trav) <= 0.2,
              f"{name} bounds lateral {blat:.3f} (integration {lat}) / travel {btrav:.3f} ({trav}) within +-0.2")
        pf = tt.prefab_block(spec, lay)
        check(pf["standing_object"]["m_LegSize"] == LEGSIZE[(family, H)]
              and pf["utility_object"]["position"] == [0.0, round(spec.z0, 3), 0.0],
              f"{name} m_LegSize {pf['standing_object']['m_LegSize']}, UtilityObject y {round(spec.z0, 3)} unchanged")
        check(rep["zones"]["hits"] == 0, f"{name} carrier zones + R30/head rule clean "
                                          f"({len(rep['zones']['zones'])} zones)")
        corr = rep["zones"]["corridors"]
        check(len(corr) == 2, f"{name} rope corridors clear of non-support parts "
                              f"{ {k: v['min_clearance_m'] for k, v in corr.items()} }")
        check(set(rep["footings"].values()) == {"open bottom at z 0"}, f"{name} 4 footings open-bottom at z 0")
        # footings, explicitly: no pedestal face at z 0, side edges end at z 0
        flat0 = 0
        for i in range(4):
            for fi in mv.faces_of(f"ped_{i}"):
                if np.abs(mv.face_coords(fi)[:, 2]).max() < 1e-5:
                    flat0 += 1
        check(flat0 == 0, f"{name} pedestals have no bottom face ({flat0})")
        check(rep["symmetry"]["y_mirror_worst_m"] < 1e-4, f"{name} y-mirror symmetry {rep['symmetry']}")
        sweep = tt.grade_sweep(mv, parts, spec)
        check(sweep["structure_level_carrier"] is None or
              sweep["structure_level_carrier"] >= tt.G_FLOOR[family],
              f"{name} grade sweep: level-carrier structure g_clear {sweep['structure_level_carrier']} "
              f">= {tt.G_FLOOR[family]}; support {sweep['rope_support']}")
        # net frame
        cs2_asset.orient_for_net(t)
        rep_n, mvn = tt.check_contracts(t, table, spec, parts, "net")
        check(abs(mvn.V[:, 2].max() - H) < 1e-3, f"{name} net frame contracts pass (top {mvn.V[:, 2].max():.3f})")
        # LOD chain by deletion
        l1p, l2p = tt.lod_lists(family)
        n1, n2 = tt.count_after_delete(t, table, l1p), tt.count_after_delete(t, table, l2p)
        lods = tt.make_lods(t, name, table, parts, family, n1, n2)
        c9 = tt.check_c9(t, lods, spec)
        led = DP1_LEDGER[(family, H)]
        p1, p2 = PRE_LOD12[(family, H)]
        check(abs(c9["tris"][0] - led) <= 0.01 * led and c9["tris"][1] == p1 - LOD1_RECLAIM[(family, H)]
              and c9["tris"][2] == p2,
              f"{name} DT-C2 tris {c9['tris']}: LOD0 = ledger {led} +-1 %, LOD1 = 1d {p1} - "
              f"{LOD1_RECLAIM[(family, H)]} reclaim, LOD2 = 1d {p2}")
        cops = [max(tt.coplanar_overlaps(o).values(), default=0.0) for o in [t] + lods]
        check(max(cops) <= tt.COPLANAR_MAX_CM2, f"{name} DT-C3 worst same-facing pair per LOD {cops} cm2 <= 1")
        sm = [smooth_member_normals(o, table, family) for o in [t] + lods]
        check(max(sm) == 1, f"{name} M6 smooth members one normal per vertex on LOD0 / 1 / 2 ({sm})")
        b = tt.budget(spec)
        check(c9["tris"][1] == n1 and c9["tris"][2] == n2, f"{name} LOD deletion counts {c9['tris']}")
        check(max(c9["drift_m"]) <= 1e-6, f"{name} LOD bounds drift {c9['drift_m']} (top anchor)")
        check(c9["tris"][0] <= b["lod0_cap"] and c9["tris"][1] <= b["lod1_target"] and
              c9["tris"][2] <= b["lod2_cap"], f"{name} budgets {c9['tris']} vs cap {b['lod0_cap']} / "
                                              f"{b['lod1_target']} / {b['lod2_cap']}")
        lod2_names = {table[i] for i in range(len(table)) if tt.lod_fate(table[i], family) == 0}
        legs = sum(1 for n in lod2_names if n.startswith("leg_"))
        head = [n for n in lod2_names if n.startswith(("headchord_", "gdgirder_", "gdbeam_", "gantrybeam_"))]
        check(legs == 4 and len(head) >= 1, f"{name} LOD2 reads as a lattice: {legs} legs + head {sorted(head)[:4]}")
        for l in lods:
            me = l.data
            bpy.data.objects.remove(l, do_unlink=True)
            bpy.data.meshes.remove(me)

# --------------------------------------------------------------------------------------
# 3. negatives
# --------------------------------------------------------------------------------------
def neg_box(family, H, name, size, loc):
    def run():
        tt.clear_scene()
        spec = tt.make_spec(family, H)
        parts, _lay = tt.build_parts(spec)
        parts.box(name, size, loc)
        t, table, _src = tt.assemble(parts, family)
        tt.check_contracts(t, table, spec, parts, "authoring")
    return run


z0 = tt.make_spec("tram", 30).z0
expect_fail(neg_box("tram", 30, "headx_99", (0.3, 0.3, 0.3), (0.0, 6.3, z0 - 1.5)), "C8", "part in R30 band")
expect_fail(neg_box("tram", 30, "lug_99", (0.3, 0.3, 0.3), (0.0, 5.5, z0 + 0.25)), "C8", "part in carriage band")
A40 = tt.make_spec("gondola", 40).z0
expect_fail(neg_box("gondola", 40, "gdcap_99", (0.3, 0.3, 0.3), (0.0, 3.4, A40 - 0.8)), "C8",
            "part in the gondola hanger band")


def neg_closed_pedestal():
    tt.clear_scene()
    spec = tt.make_spec("tram", 30)
    parts, _lay = tt.build_parts(spec)
    for i, (o, m) in enumerate(parts.items):
        if o.name == "ped_0":
            me = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.meshes.remove(me)
            closed = cs2_kit.add_box("ped_0", (tt.PW_TR, tt.PW_TR, tt.PED_H_TR),
                                     (spec.B / 2, spec.B / 2, tt.PED_H_TR / 2))
            parts.items[i] = (closed, m)
            break
    t, table, _src = tt.assemble(parts, "tram")
    tt.check_contracts(t, table, spec, parts, "authoring")


expect_fail(neg_closed_pedestal, "FOOT", "closed-bottom pedestal")

_orig = tt.saddle_profile


def neg_buried_saddle():
    tt.saddle_profile = lambda spec: [(x, z + 0.03) for x, z in _orig(spec)]
    try:
        tt.clear_scene()
        spec = tt.make_spec("tram", 30)
        parts, _lay = tt.build_parts(spec)
        t, table, _src = tt.assemble(parts, "tram")
        tt.check_contracts(t, table, spec, parts, "authoring")
    finally:
        tt.saddle_profile = _orig


expect_fail(neg_buried_saddle, "C3", "saddle above the rope (burial)")


# Revision 1 negatives (verdicts 7 / 8): each must FAIL C7
def neg_landing_over_flight():
    """The verdict-8 defect: a centred 1.4 x 1.4 landing across the flight below."""
    tt.clear_scene()
    spec = tt.make_spec("tram", 30)
    parts, _lay = tt.build_parts(spec)
    f = parts.ladder["flights"][0]
    z = f["f0"] + 3.0
    n = float(np.interp(z, [p[1] for p in f["pts"]], [p[0] for p in f["pts"]])) - 0.40
    parts.kit(cs2_kit.grating("bskt99", 1.4, 1.4, t=0.03, loc=(n, f["line"], z)), "galv", aabb=True)
    t, table, _src = tt.assemble(parts, "tram")
    tt.check_contracts(t, table, spec, parts, "authoring")


def neg_flights_not_offset():
    lines = tt.LADDER_FAM["tram"]["lines"]
    tt.LADDER_FAM["tram"]["lines"] = (0.0, 0.0)
    try:
        tt.clear_scene()
        spec = tt.make_spec("tram", 45)
        parts, _lay = tt.build_parts(spec)
        t, table, _src = tt.assemble(parts, "tram")
        tt.check_contracts(t, table, spec, parts, "authoring")
    finally:
        tt.LADDER_FAM["tram"]["lines"] = lines


_arr = tt.ladder_arrival


def neg_ladder_short():
    """A ladder system planned 3 m short of the head deck (the check reads the arrival from the spec)."""
    tt.ladder_arrival = lambda spec: _arr(spec) - 3.0
    try:
        tt.clear_scene()
        spec = tt.make_spec("tram", 30)
        parts, _lay = tt.build_parts(spec)
    finally:
        tt.ladder_arrival = _arr
    t, table, _src = tt.assemble(parts, "tram")
    tt.check_contracts(t, table, spec, parts, "authoring")


expect_fail(neg_landing_over_flight, "C7", "centred landing over the flight below (verdict 8)")
expect_fail(neg_flights_not_offset, "C7", "flights not offset (verdict 8)")
expect_fail(neg_ladder_short, "C7", "ladder stops short of the head deck (verdict 7)")


def neg_sink(family, H, sink_bad):
    """Flight-0 rails of a variant that never floats (60A / 40O) end `sink_bad` below grade: 0 = the
    revision-1 build (revision1-check-tram-towers F1); 2.0 / 2.7 = the 1b sinks, short of the
    face-normal drop 2.14 / 2.95 (revision1b-check-tram N2)."""
    def fn():
        sink = tt.LADDER_FAM[family]["sink"]
        tt.LADDER_FAM[family]["sink"] = sink_bad
        try:
            tt.clear_scene()
            spec = tt.make_spec(family, H)
            parts, _lay = tt.build_parts(spec)
        finally:
            tt.LADDER_FAM[family]["sink"] = sink
        t, table, _src = tt.assemble(parts, family)
        tt.check_contracts(t, table, spec, parts, "authoring")
    return fn


expect_fail(neg_sink("tram", 60, 0.0), "C7", "tram 60 ladder rails end at grade: downhill foot in the air on a slope (F1)")
expect_fail(neg_sink("gondola", 40, 0.0), "C7", "gondola 40 ladder rails end at grade (F1; revision1b-check-tram N7)")
expect_fail(neg_sink("tram", 60, 2.0), "C7", "tram 60 on the 1b sink 2.0 < face-normal drop 2.14 (N2)")
expect_fail(neg_sink("gondola", 40, 2.7), "C7", "gondola 40 on the 1b sink 2.7 < face-normal drop 2.95 (N2)")


def neg_floater_1b(family, H, sink_1b):
    """The 1b ladder on a variant the score floats (revision1b-check-tram N1): planned as if it never
    floated (rails to the 1b sink, no rungs below grade); C7 reads the real float."""
    def fn():
        vf, sink = tt.variant_float, tt.LADDER_FAM[family]["sink"]
        tt.variant_float, tt.LADDER_FAM[family]["sink"] = (lambda spec, step=0.01: 0.0), sink_1b
        try:
            tt.clear_scene()
            spec = tt.make_spec(family, H)
            parts, _lay = tt.build_parts(spec)
        finally:
            tt.variant_float, tt.LADDER_FAM[family]["sink"] = vf, sink
        t, table, _src = tt.assemble(parts, family)
        tt.check_contracts(t, table, spec, parts, "authoring")
    return fn


expect_fail(neg_floater_1b("tram", 30, 2.0), "C7", "tram 30 floated 10 m with the 1b ladder (foot 8 m in the air, N1)")
expect_fail(neg_floater_1b("gondola", 32, 2.7), "C7", "gondola 32 floated 5.33 m with the 1b ladder (N1)")


def neg_floater_bare_rails():
    """Tram 30: rails to the pier depth but no rungs below grade - a bare rail pair on the exposed piers."""
    _plan = tt.ladder_plan

    def plan_no_rungs(spec, lay):
        p = _plan(spec, lay)
        p["flights"][0]["rungs_below"] = 0
        return p
    tt.ladder_plan = plan_no_rungs
    try:
        tt.clear_scene()
        spec = tt.make_spec("tram", 30)
        parts, _lay = tt.build_parts(spec)
    finally:
        tt.ladder_plan = _plan
    t, table, _src = tt.assemble(parts, "tram")
    tt.check_contracts(t, table, spec, parts, "authoring")


expect_fail(neg_floater_bare_rails, "C7", "tram 30 floated with bare rails below grade (N1)")


# Detail pass 1 negatives
def dp1_geo(family, H, before=None):
    """Build, then check_dp1_geometry (authoring frame); `before(parts, spec)` mutates the parts first."""
    tt.clear_scene()
    spec = tt.make_spec(family, H)
    parts, lay = tt.build_parts(spec)
    if before:
        before(parts, spec)
    t, table, _src = tt.assemble(parts, family, H=H)
    tt.check_dp1_geometry(t, table, spec, parts, lay, uv_layer="UVMap", net=False)


def with_patch(obj, key, value, fn):
    def run():
        old = obj[key] if isinstance(obj, dict) else getattr(obj, key)
        if isinstance(obj, dict):
            obj[key] = value
        else:
            setattr(obj, key, value)
        try:
            fn()
        finally:
            if isinstance(obj, dict):
                obj[key] = old
            else:
                setattr(obj, key, old)
    return run


expect_fail(with_patch(tt.DP1, "headx_w", 0.22, lambda: dp1_geo("tram", 30)), "DT-C3",
            "pre-pass headx_ 0.22 (headvert_ | headx_ z-fight)")


def _buried_box(parts, spec):
    ztop = spec.z0 + tt.GD_DECK["top_dA"]
    parts.box("gdcap_99", (0.20, 0.20, 0.05), (0.0, 1.20, ztop - 0.03 - 0.025))     # top face ON the grating bottom


expect_fail(lambda: dp1_geo("gondola", 25, _buried_box), "DT-C4", "a box whose top face lies under the deck grating")
expect_fail(with_patch(tt, "_metric_post_uv", lambda obj: None, lambda: dp1_geo("gondola", 25)), "DT-C5",
            "create_cone post / flange UVs (12x / 68x anisotropy)")
expect_fail(with_patch(tt.ARREST, "into", -0.003, lambda: dp1_geo("gondola", 40)), "DT-C6",
            "an arrest rail front 5 mm beyond rung plane + 0.036")
expect_fail(with_patch(tt.COLLAR, "w", 0.56, lambda: dp1_geo("tram", 45)), "DT-C6",
            "collars 0.08 proud of the leg faces")


def neg_unregistered_arrest():
    """M5: an arrest rail NOT registered as a member axis is AABB-tested by C7, and its AABB spans the batter."""
    real_add = tt.Parts.add

    def add(self, obj, mat, axis=None, w=None, aabb=False):
        if obj.name.startswith("arrest_"):
            axis, w = None, None
        return real_add(self, obj, mat, axis=axis, w=w, aabb=aabb)
    tt.Parts.add = add
    try:
        tt.clear_scene()
        spec = tt.make_spec("tram", 45)
        parts, _lay = tt.build_parts(spec)
    finally:
        tt.Parts.add = real_add
    t, table, _src = tt.assemble(parts, "tram")
    tt.check_contracts(t, table, spec, parts, "authoring")


expect_fail(neg_unregistered_arrest, "C7", "M5: an arrest rail without its member axis blocks the C7 corridor")


def neg_stock_lods():
    """M6: cs2_asset.make_lod_chain alone re-marks every 90 deg member edge sharp (faceted LODs)."""
    t, table, spec, parts, lay = build("gondola", 25)
    cs2_asset.orient_for_net(t)
    l1p, l2p = tt.lod_lists("gondola")
    n1, n2 = tt.count_after_delete(t, table, l1p), tt.count_after_delete(t, table, l2p)
    lods = cs2_asset.make_lod_chain(t, "NegLods", targets=(n1, n2), delete_prefixes=(l1p, l2p), tol=0.02,
                                    shade_angle=tt.SHADE_ANGLE)
    worst = max(smooth_member_normals(l, table, "gondola") for l in lods)
    for l in lods:
        me = l.data
        bpy.data.objects.remove(l, do_unlink=True)
        bpy.data.meshes.remove(me)
    if worst > 1:
        raise tt.ContractError(f"M6: stock LOD chain faceted the smooth members ({worst} normals per vertex)")


expect_fail(neg_stock_lods, "M6", "the stock make_lod_chain LODs (members faceted again)")

# --------------------------------------------------------------------------------------
# 4. variant scoring (pure Python)
# --------------------------------------------------------------------------------------
tram_sizes = [TOPS[("tram", h)] for h in tt.HEIGHTS["tram"]]
sw = tt.scoring_sweep(tram_sizes, tram_sizes[0])
sw_e = [e for e, _s in sw["switches"][1:]]
check(len(sw_e) == 2 and abs(sw_e[0] - 10.0) <= 0.011 and abs(sw_e[1] - 25.0) <= 0.011,
      f"tram switches at e {sw_e} (10 / 25)")
check(sw["max_float"] <= 10.0 + 1e-6 and sw["max_bury"] <= 5.0 + 1e-6,
      f"tram max float {sw['max_float']} <= 10, burial {sw['max_bury']} <= 5 (pier 12 = 10 + 2)")
ok = True
for i in range(0, 3001, 7):
    e = i * 0.01
    s = tt.pick_variant(e, tram_sizes, tram_sizes[0])
    stub = e + tram_sizes[0] - s + (s - 3.75)
    ok &= abs(stub - (e + 26.25)) < 1e-3
check(ok, "tram stub world height = node + 26.25 for every picked variant")
gd_sizes = list(tt.GD["tubular_tops"]) + [TOPS[("gondola", h)] for h in tt.HEIGHTS["gondola"]]
swg = tt.scoring_sweep(gd_sizes, gd_sizes[0])
gd_e = [e for e, _s in swg["switches"][1:]]
want = [2.67, 6.67, 11.34, 17.67, 25.34]
check(len(gd_e) == 5 and all(abs(a - b) <= 0.011 for a, b in zip(gd_e, want)),
      f"gondola family switches {gd_e} ({want})")
check(swg["max_float"] <= 5.34 and swg["max_bury"] <= 2.67, f"gondola float {swg['max_float']}, burial {swg['max_bury']}")
check(swg["max_float"] + 2.0 <= tt.PIER_GD + 1e-6, "gondola pier 8.0 covers the max float + 2 m")

# --------------------------------------------------------------------------------------
# 5. bake smoke into a temp folder (removed at the end): tram 30 with --beacon, and the SHIPPING
#    path (no beacon, no _Emissive) on the gondola 25 (review minor 6: the shipped towers have no
#    beacon and the 1024 lattice set was never baked in a test)
# --------------------------------------------------------------------------------------
import shutil  # noqa: E402


def load(path):
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)


def bake_args(tmp, family, H, beacon=False, atlas=256, ao=4, weather="off"):
    return SimpleNamespace(family=family, height=H, all=False, out=tmp, letter=None, finish=True,
                           lods=True, export=True, review=None, preview=None, no_preview=True,
                           beacon=beacon, atlas=atlas, ao_samples=ao, saddle_l=tt.SADDLE_L_DEFAULT,
                           lattice_ladder=False, no_lattice_ladder=False, no_validate=False,
                           weather=weather, weather_probe=False, lineup=False)


def bake_smoke(tmp, family, H, beacon):
    args = bake_args(tmp, family, H, beacon=beacon)
    # no_validate False: cs2_validate.validate_asset_folder RAISES on any ERROR, so a folder
    # that fails validation fails this test (and every shipping run exits 1)
    res = tt.run_variant(family, H, args, [])
    d, name = res["asset_dir"], res["name"]
    tag = f"bake smoke {name}{' --beacon' if beacon else ''}"
    files = sorted(os.listdir(d))
    need = [f"{name}{s}" for s in (".fbx", "_LOD1.fbx", "_LOD2.fbx", "_BaseColor.png", "_Normal.png",
                                   "_MaskMap.png", "_ControlMask.png")] + ["settings.json"]
    check(all(f in files for f in need), f"{tag}: files {files}")
    check((f"{name}_Emissive.png" in files) == beacon,
          f"{tag}: _Emissive {'present' if beacon else 'absent (shipping path)'}")
    base = load(os.path.join(d, f"{name}_BaseColor.png"))
    msk = load(os.path.join(d, f"{name}_MaskMap.png"))
    ctl = load(os.path.join(d, f"{name}_ControlMask.png"))
    check(base[..., 3].min() > 0.999, f"{tag}: BaseColor alpha 255 everywhere")
    pngs = [f for f in files if f.endswith(".png")]
    check(len(pngs) == (5 if beacon else 4), f"{tag}: no texture map added (DT1: {pngs})")
    check(msk[..., 2].max() < 1e-3, f"{tag}: MaskMap B 0")
    check(ctl[..., :3].sum(axis=-1).max() <= 1.0 + 1e-3 and ctl[..., 3].max() < 1e-3,
          f"{tag}: ControlMask R + G + B <= 1, A 0")
    lum = base[..., :3].mean(axis=-1)
    for ch, slot in ((0, "R"), (1, "G"), (2, "B")):
        under = lum[ctl[..., ch] > 0.5]
        if under.size < 50:
            continue
        p95 = float(np.percentile(under, 95))
        check(0.78 <= p95 <= 0.82, f"{tag}: BaseColor 95th percentile under mask {slot} = {p95:.3f} "
                                   f"(0.78..0.82, {under.size} texels)")
    if beacon:
        emi = load(os.path.join(d, f"{name}_Emissive.png"))
        lit = (emi[..., :3].max(axis=-1) > 0.02)
        check(0 < lit.mean() < 0.02, f"{tag}: Emissive only on the beacon lens "
                                     f"({100 * lit.mean():.3f} % of texels)")
    return res


def sha1_file(p):
    with open(p, "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()


def plate_uv_reads(name):
    """M3 / DT-C7: on the baked mesh (UVUnique, plates stacked on one island) each plate's UV u runs
    viewer-right and v up, as seen from outside its own travel face (unmirrored glyph + numeral)."""
    o = bpy.data.objects.get(name + "Src") or bpy.data.objects.get(name)
    me = o.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    pid = np.zeros(len(me.polygons), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", pid)
    uv = me.uv_layers["UVUnique"]
    out = {}
    for side in ("n", "p"):
        f = [p for p in me.polygons if table[pid[p.index] - 1] == f"plate_{side}"][0]
        lis = list(range(f.loop_start, f.loop_start + 3))
        P = np.array([me.vertices[me.loops[li].vertex_index].co[:] for li in lis])
        P = np.stack([-P[:, 1], P[:, 0], P[:, 2]], axis=1)            # net -> authoring
        Q = np.array([uv.data[li].uv[:] for li in lis])
        E = np.stack([P[1] - P[0], P[2] - P[0]], axis=1)
        F = np.array([[Q[1, 0] - Q[0, 0], Q[2, 0] - Q[0, 0]], [Q[1, 1] - Q[0, 1], Q[2, 1] - Q[0, 1]]])
        J = E @ np.linalg.inv(F)                                      # columns: d3D/du, d3D/dv
        n = np.array([1.0 if side == "p" else -1.0, 0.0, 0.0])
        right = np.cross([0.0, 0.0, 1.0], n)
        out[side] = (round(float(J[:, 0] @ right / np.linalg.norm(J[:, 0])), 4),
                     round(float(J[2, 1] / np.linalg.norm(J[:, 1])), 4))
    return out


def black_faces(name, png):
    """Faces of the baked mesh whose UV-centroid texel is black in the shipped BaseColor (a sub-texel
    island the EMIT bake never sampled; fill_subtexel_faces paints them)."""
    o = bpy.data.objects.get(name + "Src") or bpy.data.objects.get(name)
    me = o.data
    img = load(png)
    A = img.shape[0]
    uv = me.uv_layers["UVUnique"]
    n = 0
    for p in me.polygons:
        q = np.array([uv.data[li].uv[:] for li in range(p.loop_start, p.loop_start + p.loop_total)]) * A
        c = q.mean(axis=0)
        if img[min(A - 1, int(c[1])), min(A - 1, int(c[0])), :3].max() < 0.02:
            n += 1
    return n


def tone_decorrelated(family, H, a, b, prefix, cell, stretch):
    """DT-C9 (second clause): the tone field is world-anchored, so two TRANSLATION-identical members
    (not stacked hardware) carry different mottle - the fbm the bake evaluates (cs2_compose.detail_tone:
    seed crc32(prefix), cell across, cell x stretch along the member) sampled at corresponding points
    of `a` and `b` correlates < 0.9."""
    import zlib
    import cs2_compose
    t, table, spec, parts, lay = build(family, H)
    mv = tt.MeshView(t, table)
    va, vb = mv.verts(a), mv.verts(b)
    d = vb.mean(axis=0) - va.mean(axis=0)
    ax = va.max(axis=0) - va.min(axis=0)
    ax = ax / np.linalg.norm(ax)
    lo, hi = va.min(axis=0), va.max(axis=0)
    g = np.stack(np.meshgrid(*[np.linspace(lo[k], hi[k], 9) for k in range(3)], indexing="ij"), -1).reshape(-1, 3)
    seed = zlib.crc32(prefix.encode("utf-8")) & 0xFFFFFFFF

    def tone(p):
        x = (p + (1.0 / stretch - 1.0) * (p @ ax)[:, None] * ax) / cell
        return cs2_compose._fbm2(x, seed)
    na, nb = tone(g), tone(g + d)
    return float(np.corrcoef(na, nb)[0, 1])


tmp = tempfile.mkdtemp(prefix="lattice_smoke_")
try:
    bake_smoke(tmp, "tram", 30, True)
    bake_smoke(tmp, "gondola", 25, False)
    for fam_, H_, a_, b_, pre_ in (("gondola", 25, "trans_4", "trans_5", "trans_"),
                                   ("tram", 30, "headvert_0", "headvert_4", "headvert_")):
        mat_ = tt.prefix_of(a_)[1]
        cell_, st_ = tt.TONE[fam_][mat_][0], tt.TONE[fam_][mat_][1]
        r_ = tone_decorrelated(fam_, H_, a_, b_, pre_, cell_, st_)
        check(r_ < 0.9, f"DT-C9 translation-identical {a_} / {b_} tone correlation {r_:+.3f} < 0.9")
    # ---- detail pass 1: the shipping-size bake (DT-C7..C10 also raise inside run_variant) ----
    ship = os.path.join(tmp, "ship")
    off = os.path.join(tmp, "off")
    child = os.path.join(tmp, "child")
    res = tt.run_variant("gondola", 25, bake_args(ship, "gondola", 25, atlas=1024, ao=8, weather="on"), [])
    reads = plate_uv_reads(res["name"])
    nb = black_faces(res["name"], os.path.join(res["asset_dir"], f"{res['name']}_BaseColor.png"))
    check(nb <= 2, f"sub-texel fill: {nb} face(s) sample a black texel at their UV centroid (1d shipped: 131 on 40O)")
    check(all(a > 0.99 and b > 0.99 for a, b in reads.values()),
          f"M3 / DT-C7 both plates read unmirrored from outside (u . right, v . up = {reads})")
    d = res["asset_dir"]
    n = res["name"]
    man = json.load(open(os.path.join(d, f"{n}.manifest.json"), encoding="utf-8"))
    dp = man["detail_pass"]["texture"]
    w = man["weather"]
    check(all(p["px_per_m"] >= 300 and p["glyph_px"] >= 56 and p["numeral_cap_px"] >= 48 and p["ctl_RGB_max"] == 0.0
              for p in dp["plates"].values()),
          f"DT-C7 plates {[(k, v['px_per_m'], v['glyph_px'], v['numeral_cap_px']) for k, v in dp['plates'].items()]}")
    check(not dp["relief"]["skipped"] and {"edge_round", "marks:plate", "marks:band", "marks:ring", "marks:bolts",
                                            "grate"} <= set(dp["relief"]["drawn"]),
          f"DT-C8 relief drawn {dp['relief']['drawn']}, skipped {dp['relief']['skipped']}")
    check(not dp["tone"]["skipped"] and not dp["tone_low"], f"DT-C9 tone drawn {dp['tone']['drawn']}, low {dp['tone_low']}")
    check(w["vanilla"]["ok"] and abs(w["intensity"] - tt.WEATHER_I["gondola"]) < 1e-9 and w["slot_wear"] == "free"
          and float(w["snow"]) == 0.0 and w["guards"]["G5_ctl_identity"] and w["guards"]["G6_protect_identical"],
          f"DT-C10 weather at {w['intensity']}: ok {w['vanilla']['ok']}, VL1 {w['vanilla']['VL1_mean_rel_dL']:.3f}, "
          f"guards G5 / G6 {w['guards']['G5_ctl_identity']} / {w['guards']['G6_protect_identical']}")
    check(w["roles"].get("masonry", 0) > 0 and "pad" not in w["roles"],
          f"DT-C10 concrete -> masonry, no pad role on a lattice ({w['roles']})")
    tt.run_variant("gondola", 25, bake_args(off, "gondola", 25, atlas=1024, ao=8, weather="off"), [])
    c_on, c_off = (sha1_file(os.path.join(x, n, f"{n}_ControlMask.png")) for x in (ship, off))
    check(c_on == c_off, f"DT-C10 ControlMask byte-identical with / without weathering (G5: {c_on[:12]} / {c_off[:12]})")
    b_on, b_off = (load(os.path.join(x, n, f"{n}_BaseColor.png")) for x in (ship, off))
    r0, r1, c0, c1 = dp["plates"]["plate_n"]["rect"]
    check(np.array_equal(b_on[r0:r1, c0:c1], b_off[r0:r1, c0:c1]), "DT-C10 the plate rect unweathered (stamped, M3)")
    # DT-C11 fresh process: a child Blender bakes the same variant into its own folder
    cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1", "--python",
           os.path.join(HERE, "tram_tower.py"), "--", "--family", "gondola", "--height", "25", "--finish",
           "--lods", "--export", "--no-preview", "--atlas", "1024", "--ao-samples", "8", "--out", child]
    cp = subprocess.run(cmd, capture_output=True, text=True, timeout=1800)
    same = cp.returncode == 0
    for f in sorted(os.listdir(os.path.join(ship, n))):
        if f.endswith(".png") or f == "settings.json":
            same = same and sha1_file(os.path.join(ship, n, f)) == sha1_file(os.path.join(child, n, f))
    check(same, f"DT-C11 a fresh Blender process bakes byte-identical PNGs + settings.json (exit {cp.returncode})")
finally:
    shutil.rmtree(tmp, ignore_errors=True)

dt = time.perf_counter() - T0
if FAILS:
    print(f"FAIL test_lattice_towers: {len(FAILS)} check(s) failed in {dt:.1f} s")
    for f in FAILS:
        print("   - " + f)
    sys.exit(1)
print(f"PASS test_lattice_towers: determinism, contracts (6 variants x 2 frames), Revision 1 (ladder from "
      f"below grade (1c: pier depth + rungs on the floatable variants; 1d: rungs down the sink on 60A / 40O), "
      f"side baskets + offset flights, "
      f"curved saddles + haul sheave row, upper frame, cranes, lugs, frozen prefab numbers), LOD drift, footings, "
      f"negatives, scoring, bake smoke; detail pass 1 DT-C1..C12 + M3 / M5 / M6 (+ negatives), shipping-size bake, "
      f"fresh-process determinism - {len(OKS)} checks passed in {dt:.1f} s")
