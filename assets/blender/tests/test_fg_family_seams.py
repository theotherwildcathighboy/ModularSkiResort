"""
test_fg_family_seams.py - cross-generator seams of the FIXED-GRIP TRIPLE family (generation M stations, 2026-10-11, the
tests/test_dq_family_seams.py pattern). Each generator asserts its own contracts. This test asserts the numbers one
asset hands to another:

1. CODE: catalog/carriers.json row tr == lift_chair (HEIGHT_CONTRACT, CARRIERS) == the station generator (lift_terminal
   FG_HANG / FG_LANE / FG_GRIP_ABOVE / seats) == lift_tower (every FG / ET gauge / 2 == the lane, FG8 axle == the line
   axle, ET62 / ET68 axle == the top / base HV sub-lane outboard end y, the inboard end = axle + 0.03) == SkiCatalog.cs
   (WheelLocal, StubTipLocalZ) == the deployed K building prefabs (HV ends, hosted tower pose).

2. EXPORTS: the manifests of the generation the GAME DRAWS agree with each other and with the code. The test resolves
   that generation and never names it. Each deployed prefab in gamedata-mirror (SkiBaseStationBuilding /
   SkiTopStationBuilding, their hosted SkiLiftTerminalTowerBase / Top, the chair object prefab of catalog/carriers.json
   tr and its grip prefab) names its mesh by CID. The mesh prefab's name is the export folder under assets/import (N
   stations, P towers, N chair + grip when this was written), so the next letter cannot strand the test. The one check
   that is explicitly about generation M is M1: the M station manifests' frozen block is the deployed prefab, re-parsed
   now (sha256 of the canonical JSON). tools/check-lift-align.js MANIFESTSHA pins the same sha at preflight. The test
   skips a missing folder with a note.

3. GEOMETRY on the LIVE generation's FBXs (cm, raw frame (y, z, x) of authoring). The chair is the split pair: the body
   plus the grip placed at its assembled origin.
- H1 hang: the chair's top - lowest point == 2.400 (+-1e-4). Its cushion is 0.15 over the lowest.
- H2: the station's rope torus center == WheelLocal (+-2 mm) and r == lane (+-5 mm).
- H3 seat height: the shipped chair at the UP-lane tangent (grip top = the lane, ChairPinSystem) has its cushion over
  the shipped surface below it (base: carpet, top: crest) in [0.45, 0.55].
- S8 hosted seam: the live hosted ET68 / ET62 tower (LOD0, LOD1) at the hosted pose (0, 0, -6.9 / -4.9), rot 180,
  against the shipped station LOD0 / LOD1 minus its rope faces (every vertex within rope r + 5 mm of a chord / the
  torus). No face lies within 0.10 m.
- S9 (nitpick A2, 2026-10-13, build/nitpick-a2-hanger-gondola.md): the tr chair (code) goes through every Yan sheave
  tree: FG8 / FG12 / FG16 / FG20, ET62, ET68 and the Pali base's hold-down trains (built in the code chord's frame, the
  lane = lift_terminal.chord_z). The test runs lift_chair.carrier_sweep on BOTH spins (facing -s: the stations' spin,
  its right outboard. Facing +s: the opposite) with carrier_verdict "yan" (nothing crossed but the F10 set - rims,
  liners, sheave arms, axle caps - and every other part >= CARRIER_CLEAR_M). The fixed grip is centered (only its
  spring housing reaches +Y), so it passes either way. The 6M keeps its frame (no mirror).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_fg_family_seams.py
"""
import hashlib
import json
import math
import os
import re
import sys
import time

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_validate  # noqa: E402
import lift_chair as LC  # noqa: E402
import lift_terminal as LT  # noqa: E402
import lift_tower as LTW  # noqa: E402

IMPORT = LT.IMPORT_M
FAILS, NOTES = [], []


def check(cond, label, detail=""):
    print(f"  {'ok  ' if cond else 'FAIL'} {label} {detail}".rstrip())
    if not cond:
        FAILS.append(label)


def close(a, b, eps=1e-6):
    return a is not None and b is not None and abs(float(a) - float(b)) <= eps


def manifest(rel, name):
    p = os.path.join(IMPORT, rel, name, f"{name}.manifest.json")
    if not os.path.isfile(p):
        NOTES.append(f"skipped {rel}/{name}")
        print(f"  skip {rel}/{name}: no manifest")
        return None
    with open(p, encoding="utf-8") as fh:
        return json.load(fh)


_MESH_RE = re.compile(r"^(?P<n>.+?) Mesh_[0-9a-f]{32}.Prefab.cid$")
_MESH_BY_CID = {}


def mesh_by_cid():
    """CID -> the LOD0 mesh prefab's folder name, from every '<Name> Mesh_<hash>.Prefab.cid' in gamedata-mirror."""
    if not _MESH_BY_CID:
        for dp, _dn, fns in os.walk(LT.GAMEDATA_M):
            for f in fns:
                m = _MESH_RE.match(f)
                if m and "_LOD" not in m.group("n"):
                    with open(os.path.join(dp, f), encoding="utf-8") as fh:
                        _MESH_BY_CID[fh.read().strip().lower()] = m.group("n")
    return _MESH_BY_CID


def live_name(prefab_path):
    """The mesh name a deployed (mirror) prefab draws: m_Meshes[0].m_Mesh CID -> the mesh prefab's name."""
    ref = LT._prefab_json(prefab_path)["m_Meshes"]["$rcontent"][0]["m_Mesh"]
    cid = ref.split("CID:")[-1].lower()
    name = mesh_by_cid().get(cid)
    if name is None:
        raise AssertionError(f"{os.path.basename(prefab_path)}: m_Mesh CID {cid} names no mesh prefab in gamedata-mirror")
    return name


def export_dir(name):
    """assets/import/<kind>/<letter>/<name> for an export folder name (archive/ skipped). None when not exported."""
    for dp, dn, _f in os.walk(IMPORT):
        dn[:] = [x for x in dn if x != "archive"]
        if name in dn:
            return os.path.join(dp, name)
    return None


def mirror_prefab(name):
    """The mirror's .Prefab of a gameplay prefab: <name>/<name>.Prefab at the top, else the library layout."""
    p = os.path.join(LT.GAMEDATA_M, name, name + ".Prefab")
    if os.path.isfile(p):
        return p
    for dp, dn, fns in os.walk(LT.GAMEDATA_M):
        for f in fns:
            if f.startswith(name + "_") and f.endswith(".Prefab") and os.path.basename(dp) == name:
                return os.path.join(dp, f)
    raise AssertionError(f"no {name} prefab in gamedata-mirror")


LIVE = {}


def resolve_live(tr):
    """The generation the game draws, from the live m_Mesh CIDs (never a hard-coded letter)."""
    for kind in ("drive", "return"):
        sp = LT.M_SPECS[kind]
        LIVE[kind] = {"station": live_name(mirror_prefab(sp.building)), "tower": live_name(mirror_prefab(sp.tower_prefab))}
    LIVE["chair"] = live_name(mirror_prefab(tr["object_prefab"]))
    LIVE["grip"] = live_name(mirror_prefab(tr["object_prefab"] + "Grip"))
    print("[fg-seams] live generation (gamedata-mirror m_Mesh -> assets/import):",
          {k: (v if isinstance(v, str) else dict(v)) for k, v in LIVE.items()})


def live_manifest(name):
    d = export_dir(name)
    p = None if d is None else os.path.join(d, f"{name}.manifest.json")
    if p is None or not os.path.isfile(p):
        NOTES.append(f"skipped live {name}")
        print(f"  skip {name}: no manifest")
        return None
    with open(p, encoding="utf-8") as fh:
        return json.load(fh)


def live_fbx(name, suffix=""):
    d = export_dir(name)
    return None if d is None else os.path.join(d, f"{name}{suffix}.fbx")


def raw_mesh(path):
    """Shipped FBX -> (authoring coords m (fz, fx, fy), triangles)."""
    objs = cs2_validate.import_fbx_temp(path)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        if any(abs(o.matrix_world[i][j] - (1.0 if i == j else 0.0)) > 1e-9 for i in range(4) for j in range(4)):
            raise RuntimeError(f"{path}: non-identity import matrix")
        co = np.array([v.co[:] for v in o.data.vertices], dtype=np.float64) / 100.0
        tris = []
        for p in o.data.polygons:
            v = list(p.vertices)
            tris += [(v[0], v[j], v[j + 1]) for j in range(1, len(v) - 1)]
    finally:
        cs2_validate.remove_objects(objs)
    return np.stack([co[:, 2], co[:, 0], co[:, 1]], axis=1), np.array(tris, dtype=np.int64)


def code_seams():
    print("[fg-seams] 1. code")
    with open(LT.CARRIERS_JSON, encoding="utf-8") as fh:
        tr = json.load(fh)["carriers"]["tr"]
    check(close(tr["hang_m"], 2.400) and close(LC.HEIGHT_CONTRACT, tr["hang_m"]) and close(LT.FG_HANG, tr["hang_m"])
          and close(LC.CARRIERS["tr"]["hang_m"], tr["hang_m"]),
          "H0 hang 2.400: carriers == lift_chair HEIGHT_CONTRACT / CARRIERS == lift_terminal FG_HANG")
    check(close(LT.FG_LANE, tr["lane_x_m"]) and close(LT.FG_GRIP_ABOVE, tr["grip_top_above_rope_m"])
          and int(LT.FG_SEATS_DIGITS) == int(tr["seats"]), "lane 1.8, grip 0.080, seats 3 == carriers tr")
    for k, s in sorted(LTW.VARIANTS.items()):
        if s.family == "FG":
            check(close(s.gauge / 2.0, tr["lane_x_m"]), f"S2 {k} gauge / 2 == lane", f"({s.gauge / 2.0})")
    check(close(LTW.VARIANTS["FG8"].axle, tr["axle_m"]), "S3 FG8 axle == carriers axle_m", f"({tr['axle_m']})")
    for kind, et in (("drive", "ET68"), ("return", "ET62")):
        sp = LT.M_SPECS[kind]
        ax = LTW.VARIANTS[et].axle
        check(close(sp.anchor_out[1], ax) and close(sp.anchor_in[1], ax + 0.03, 1e-6),
              f"S1 {et} axle {ax} == the {kind} HV sub-lane ends {sp.anchor_out[1]} / {sp.anchor_in[1]} (+0.03)")
        rep = LT.m1_frozen(kind)      # raises on SkiCatalog / carriers / deployed-prefab drift
        if rep["hosted_tower"] is None:   # art-601: the drive base ships without its ET68 (the nose train holds the rope)
            check(kind == "drive", f"S1 {kind}: no hosted {et} in the deployed prefab (art-601 nose-train base)")
        else:
            check(rep["hosted_tower"]["position"][2] == sp.tower_x, f"S1 {kind} hosted {et} at z {sp.tower_x} "
                                                                      f"(deployed prefab, rot 180)")
        check(close(rep["code"]["WheelLocal_game"][1], sp.wheel[2]) and close(rep["code"]["WheelLocal_game"][2],
                                                                               sp.wheel[0]),
              f"S1 {kind} WheelLocal {rep['code']['WheelLocal_game']} == the station rope datum")
    return tr


def export_seams(tr):
    print("[fg-seams] 2. exports (the live generation; M1 is the one generation-M check)")
    cn, gn = LIVE["chair"], LIVE["grip"]
    ch, gr = live_manifest(cn), live_manifest(gn)
    if ch and gr:
        # the live chair is a split pair: the body's own height is less than the hang. The assembled pair's height is
        # grip origin (in the assembled frame) + grip height, and both manifests carry the same pivots
        pv = ch["split"]["pivot"]
        check(pv == gr["split"]["pivot"], f"H1 {cn} / {gn} manifests carry the same pivots")
        hang = pv["grip_origin_in_assembled_m"][2] + pv["grip_height_m"]
        check(close(hang, tr["hang_m"], 1e-4) and close(pv["hang_m"], tr["hang_m"], 1e-4),
              f"H1 live {cn} + {gn} assembled hang == 2.400", f"({hang:.5f}; body {ch['verify']['height_m']:.5f})")
    for kind in ("drive", "return"):
        et, st = LIVE[kind]["tower"], LIVE[kind]["station"]
        t = live_manifest(et)
        sp = LT.M_SPECS[kind]
        if t:
            check(close(t["axle"], sp.anchor_out[1]) and close(t["gauge"], 2 * tr["lane_x_m"]),
                  f"S1 live {et} axle {t['axle']} / gauge {t['gauge']} == the station")
        st_m = {"drive": "SkiLiftBaseStationM", "return": "SkiLiftTopStationM"}[kind]
        m = manifest("stations/fixedgrip/M", st_m)
        if m:
            pc = m["prefab_contract"]
            fz, _old, sha, _p = LT.frozen_block_m(kind)
            canon = json.dumps(fz, sort_keys=True).encode("utf-8")
            check(json.dumps(pc["frozen"], sort_keys=True).encode("utf-8") == canon and
                  pc["frozen_sha256"] == hashlib.sha256(canon).hexdigest() and pc["source_sha256"] == sha,
                  f"M1 {st_m} (generation M) frozen block == the deployed {sp.building} (byte-identical canonical "
                  f"JSON, file sha256 {sha[:12]})")
        lm = live_manifest(st)
        if lm:
            c4 = lm["contracts"]["M4"]
            check(0.45 <= c4["cushion_over"] <= 0.55, f"H3 live {st} manifest cushion {c4['cushion_over']} in window")
            c2 = lm["contracts"]["M2"]
            check(close(c2["torus_z"], sp.wheel[2], 0.002) and close(c2["torus_R"], tr["lane_x_m"], 0.005),
                  f"H2 live {st} manifest torus {c2['torus_R']} at {c2['torus_z']}")


def rope_faces(co, tris, spec, rope_r=None):
    """Faces whose every vertex lies within rope r + 5 mm of a chord line or of the torus (rope_r: the asset's own radius
    from its manifest, art-601 --thin-rope. Default: the shipped LT.M_ROPE_R)."""
    wx, _wy, wz = spec.wheel
    ax, az = spec.anchor_in
    near = np.zeros(len(co), dtype=bool)
    rr = (LT.M_ROPE_R if rope_r is None else rope_r) + 0.005
    for s in (-1.0, 1.0):
        a = np.array([ax, s * LT.FG_LANE, az])
        b = np.array([wx, s * LT.FG_LANE, wz])
        u = (b - a) / np.linalg.norm(b - a)
        v = co - a
        t = np.clip(v @ u, -0.2, np.linalg.norm(b - a) + 0.2)
        near |= np.linalg.norm(v - np.outer(t, u), axis=1) <= rr
    ring = np.hypot(np.hypot(co[:, 0] - wx, co[:, 1]) - LT.M_TORUS_R, co[:, 2] - wz)
    near |= (ring <= rr) & (co[:, 0] >= wx - 0.01)
    return np.all(near[tris], axis=1)


def geometry_seams(tr):
    print("[fg-seams] 3. geometry on the live generation's FBXs")
    cf, gf = live_fbx(LIVE["chair"]), live_fbx(LIVE["grip"])
    if not (cf and gf and os.path.isfile(cf) and os.path.isfile(gf)):
        NOTES.append("geometry chair skipped")
        print(f"  skip chair {LIVE['chair']} + {LIVE['grip']}: not exported")
        ch_co = None
    else:
        # the split pair assembled: the grip FBX sits at its assembled origin (the manifests' pivot, authoring z up)
        with open(os.path.join(export_dir(LIVE["chair"]), LIVE["chair"] + ".manifest.json"), encoding="utf-8") as fh:
            gz = json.load(fh)["split"]["pivot"]["grip_origin_in_assembled_m"]
        body_co, _bt = raw_mesh(cf)
        grip_co, _gt = raw_mesh(gf)
        ch_co = np.concatenate([body_co, grip_co + np.array(gz)])
    if ch_co is not None:
        geometry_chair(ch_co, tr)
    geometry_stations(tr)


def geometry_chair(ch_co, tr):
    hang = float(ch_co[:, 2].max() - ch_co[:, 2].min())
    seat = ch_co[(np.abs(ch_co[:, 1]) < 0.70) & (ch_co[:, 2] < 0.25)]
    cushion = float(seat[:, 2].max() - ch_co[:, 2].min())
    check(close(hang, tr["hang_m"], 1e-4), f"H1 live {LIVE['chair']} + {LIVE['grip']} top - lowest == 2.400", f"({hang:.5f})")
    check(close(cushion, LT.CHAIR_CUSHION, 0.002), f"H1 live {LIVE['chair']} cushion 0.15 over its lowest point", f"({cushion:.4f})")
    LIVE["cushion"] = cushion


def geometry_stations(tr):
    cushion = LIVE.get("cushion", LT.CHAIR_CUSHION)
    for kind in ("drive", "return"):
        st, tw = LIVE[kind]["station"], LIVE[kind]["tower"]
        sp = LT.M_SPECS[kind]
        d, td = export_dir(st), export_dir(tw)
        if d is None or td is None or not os.path.isfile(os.path.join(d, f"{st}.fbx")) or                 not os.path.isfile(os.path.join(td, f"{tw}.fbx")):
            NOTES.append(f"geometry {st} skipped")
            print(f"  skip {st} / {tw}: not exported")
            continue
        co, tris = raw_mesh(os.path.join(d, f"{st}.fbx"))
        rr = (live_manifest(st) or {}).get("rope_r_m")         # per asset (art-601 --thin-rope)
        rf = rope_faces(co, tris, sp, rr)
        T = co[tris[rf]].reshape(-1, 3)
        torus = T[(np.abs(T[:, 2] - sp.wheel[2]) < 0.07) & (T[:, 0] > sp.wheel[0] + 0.5)]
        R = np.hypot(torus[:, 0] - sp.wheel[0], torus[:, 1])
        check(len(torus) > 0 and close(0.5 * (R.max() + R.min()), LT.M_TORUS_R, 0.005)
              and close(0.5 * (torus[:, 2].max() + torus[:, 2].min()), sp.wheel[2], 0.002),
              f"H2 {st} shipped rope torus r {0.5 * (R.max() + R.min()):.4f} at "
              f"{0.5 * (torus[:, 2].max() + torus[:, 2].min()):.4f} == WheelLocal")
        # H3: the chair at the up-lane tangent over the shipped surface below its cushion
        bvh = BVHTree.FromPolygons(co.tolist(), tris[~rf].tolist(), all_triangles=True)
        y_up = LT.UP_LANE_Y[kind] * LT.FG_LANE
        cz = sp.wheel[2] - LT.FG_HANG + cushion
        hit = bvh.ray_cast(Vector((sp.wheel[0], y_up, cz)), Vector((0, 0, -1)), 5.0)
        over = None if hit[0] is None else cz - hit[0].z
        check(over is not None and 0.45 <= over <= 0.55, f"H3 {st}: cushion over the shipped surface at the "
                                                          f"up-lane tangent in [0.45, 0.55]",
              f"({None if over is None else round(over, 4)})")
        # S8: the hosted tower vs the station, both LODs (art-601: a deployed base without the hosted row has no ET68
        # to clear. Its nose train sits where the tower stood)
        if LT.hosted_row_m(kind) is None:
            print(f"  S8 {st}: no hosted {tw} row in the deployed {sp.building}, skipped")
            continue
        for sl in ("", "_LOD1"):
            sco, stris = raw_mesh(os.path.join(d, f"{st}{sl}.fbx"))
            keep = stris[~rope_faces(sco, stris, sp, rr)]
            sb = BVHTree.FromPolygons(sco.tolist(), keep.tolist(), all_triangles=True)
            for tl in ("", "_LOD1"):
                tco, ttris = raw_mesh(os.path.join(td, f"{tw}{tl}.fbx"))
                tco = np.stack([-tco[:, 0] + sp.tower_x, -tco[:, 1], tco[:, 2]], axis=1)
                tb = BVHTree.FromPolygons(tco.tolist(), ttris.tolist(), all_triangles=True)
                worst = 9.9
                if sb.overlap(tb):
                    worst = 0.0
                lo, hi = sco[np.unique(keep)].min(axis=0) - 0.2, sco[np.unique(keep)].max(axis=0) + 0.2
                P = tco[np.all((tco >= lo) & (tco <= hi), axis=1)]
                for p in P:
                    h = sb.find_nearest(Vector(p.tolist()), 0.15)
                    if h[0] is not None:
                        worst = min(worst, h[3])
                tl0, th0 = tco.min(axis=0) - 0.2, tco.max(axis=0) + 0.2
                Q = sco[np.unique(keep)]
                Q = Q[np.all((Q >= tl0) & (Q <= th0), axis=1)]
                for q in Q:
                    h = tb.find_nearest(Vector(q.tolist()), 0.15)
                    if h[0] is not None:
                        worst = min(worst, h[3])
                check(worst >= 0.10 - 1e-6, f"S8 {st}{sl or ' LOD0'} x {tw}{tl or ' LOD0'}: no face within "
                                             f"0.10 (rope faces excluded)", f"({'> 0.15' if worst > 9 else round(worst, 3)})")


S9 = {}


def _s9_named(obj, table):
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    q = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", q)
    return co.reshape(-1, 3), [tuple(p.vertices) for p in me.polygons], [table[i - 1] for i in q]


def _s9_gate(tag, chair, target, lane, zl):
    for spin, fac in (("spin", -1.0), ("opposite", 1.0)):
        v = LC.carrier_verdict(LC.carrier_sweep(chair, target, lane, zl, facing=fac), "yan")
        S9[(tag, spin)] = v
        near = {k: (d["gated"][0], d["gated"][1] + "/" + LC.carrier_part_core(d["gated"][2])) if d["gated"] else
                ("> 0.25", "-") for k, d in sorted(v.items())}
        check(all(not d["fail"] for d in v.values()),
              f"S9 {tag} ({spin}): the tr grip crosses nothing but the F10 set, every other part >= "
              f"{LC.CARRIER_CLEAR_M}", f"(nearest gated per lane {near})")


def s9_code():
    print("[fg-seams] 4. S9 code: the tr chair through every Yan tree, both spins (grip top on the lane)")
    LC.clear_scene()
    st, row = LC.STYLES["tr"], LC.CARRIERS["tr"]
    obj, info = LC.build_style_chair(st, row, "S9Tr", shade=False)
    _co, polys, names = _s9_named(obj, info["table"])
    chair = (LC.authoring_coords(obj), polys, names, float(row["hang_m"]))
    for key in ("FG8", "FG12", "FG16", "FG20", "ET62", "ET68"):
        LTW.clear_scene()
        spec = LTW.VARIANTS[key]
        t, ttable, _s, _i = LTW.build_tower_v2(spec, "yan", finish=False)
        co, tp, tn = _s9_named(t, ttable)
        _s9_gate(key, chair, (np.stack([-co[:, 1], co[:, 0], co[:, 2]], axis=1), tp, tn), spec.gauge / 2.0,
                 lambda x, a=spec.axle: a)
    LT.clear_scene()
    t, table, _src = LT.assemble_m(LT.build_base_m(), False, "drive")
    co, tris, names, _ = LT.m_arrays(t)
    k = [i for i, n in enumerate(names) if n.startswith(("sheave_hold_", "holdboom_", "holdhang_"))]
    spec = LT.M_SPECS["drive"]
    _s9_gate("Pali base hold-down", chair, (co, [tuple(int(v) for v in tris[i]) for i in k], [names[i] for i in k]),
             LT.FG_LANE, lambda x, s=spec: LT.chord_z(s, x))
    LT.clear_scene()
    # art-601: the base without its hosted end tower (M_NOSE_TRAIN): the train at the sub-lane end, its king post + tie
    LT.M_NOSE_TRAIN = True
    try:
        t, table, _src = LT.assemble_m(LT.build_base_m(), False, "drive")
        co, tris, names, _ = LT.m_arrays(t)
        k = [i for i, n in enumerate(names) if n.startswith(("sheave_hold_", "holdboom_", "holdhang_", "holdweb_",
                                                              "holdclamp_", "holdplate_"))]
        check(any(names[i].startswith("holdweb_") for i in k), "S9 nose-train base builds its holdweb_ parts")
        _s9_gate("Pali base nose train (art-601)", chair,
                 (co, [tuple(int(v) for v in tris[i]) for i in k], [names[i] for i in k]),
                 LT.FG_LANE, lambda x, s=spec: LT.chord_z(s, x))
    finally:
        LT.M_NOSE_TRAIN = False
        LT.clear_scene()


def main():
    t0 = time.perf_counter()
    tr = code_seams()
    resolve_live(tr)
    export_seams(tr)
    geometry_seams(tr)
    s9_code()
    if FAILS:
        print(f"FAIL test_fg_family_seams: {len(FAILS)} seam(s) broken: {FAILS}")
        sys.exit(1)
    print(f"PASS test_fg_family_seams: hang 2.400 / lane 1.8 / grip 0.080 agree in carriers, chair, towers, "
          f"stations, SkiCatalog and the deployed K prefabs; the live chair (split pair) sits in the seat window and the "
          f"live hosted towers clear the live stations (S8); S9 the tr grip clears every Yan tree "
          f"(FG8-FG20, ET62 / ET68, the Pali base hold-down and its art-601 nose train) on both spins - no mirror ({len(NOTES)} skipped, "
          f"{time.perf_counter() - t0:.1f} s)")


main()
