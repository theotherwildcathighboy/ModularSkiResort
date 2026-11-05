"""
test_dq_family_seams.py - cross-generator seams of the detachable-quad family (integration
2026-10-11, docs/research/art/assets-2026-10/build/dq-family-integration.md).

Each generator asserts its own contracts; nothing asserted the numbers one asset hands to
another. This test does, in two layers (layer 1 builds nothing; S8 builds the base station
and the ETDQ, about 10 s in all):

  1. CODE: the generators' own constants agree with each other and with
     catalog/carriers.json (lift_chair STYLES / CARRIERS, lift_terminal_detach.params_for,
     lift_tower.VARIANTS). A generator edited on one side of a seam fails here.
  2. EXPORTS: the shipped manifests under assets/import (chairs/M, stations/detachable/A,
     towers/O) agree with each other and with the code. A stale export fails here. A missing
     folder is skipped with a note (the import tree is shipping data, not a test input).

Seams (S#) as checked:
  S1 hosted entry tower rope = station lane: ETDQ axle == params_for("base").z_rope (3.70;
     dq-terminal-skeptic MUST-CHANGE 1), and the hosted z / rotation agree (-11.0, 180 deg).
  S2 gauge: carriers dq lane_x_m == station lane == every DQ tower gauge / 2 (2.6);
     tr lane 1.8 == every FG/ET tower gauge / 2; station turn radius == lane.
  S3 line lane height: carriers dq axle_m == DQ10 axle (the shortest line tower grounds at
     node elevation 0, so its axle IS the piece lane); tr axle_m == FG8 axle (7.04).
  S4 hang: lift_chair STYLES dq/tr carrier rows == carriers.json == the station's hang.
  S5 chair NetSubObjects y (AnchorTop grip-top rule) = axle + grip_top_above_rope + 0.03:
     dq 8.25 (station manifest), tr 7.15 (PIPELINE constants).
  S6 ETDQ sheave train clear of the station nose: inboard end <= -8.3 and the station's
     hosted-tower record names the same position. (A datum check only: it compares with the
     nose line and never saw the balcony - S8 is the geometry check.)
  S7 manifests: tower / chair / station numbers equal the code (axle, top, gauge, hang,
     rope_y_required, anchors y = lane rope y, wheel y).
  S8 hosted ETDQ vs the base station GEOMETRY (review F1, dq-family-review-geometry.md): the
     ETDQ placed at the station's hosted pose (0, 0, -11.0), 180 deg about y; BVH triangle
     overlap of ETDQ LOD0 / LOD1 against station LOD0 / LOD1 (all four pairings: the two
     switch at different distances), minus the station's modeled haul ropes (`haulrope_`;
     the drawn rope runs through the sheave centers by convention, review F10), must be
     EMPTY. Code layer: both generators build (bevels on; station LOD1 = LOD0 minus
     LOD1_DELETE, its target is the after-delete count; tower LOD1 = lod_chain_v2). Export
     layer: the shipped FBXs (cm, prefab frame), haul-rope faces found geometrically (every
     vertex within ROPE_R + 5 mm of a lane axis), the pose read from both manifests.
     Revision 1 (2026-10-11) puts new station geometry in this zone: the base nose landing
     (x -8.55..-8.00, |y| <= 1.30, 0.06 m short of the ETDQ's inboard end -8.612) and the
     gangplank out between the ETDQ deck strips (|y| <= 0.55 vs their inner edge 0.695, to
     x -10.10 vs the ETDQ ladder landing plates at -10.15), both with handrails. Revision 2
     (second look 3', 2026-10-11) adds the front ladder's run from the ground: footing
     x -8.57..-8.10 at z 0..0.10, rails / rungs at x -8.25..-8.19 through a ladder-way in the
     landing (nearest ETDQ geometry: its deck strip end x -8.612, 0.38 m from the rungs; its
     ground parts end at x -9.9, 1.33 m from the footing).
  S9 (nitpick A2, 2026-10-13; build/nitpick-a2-hanger-gondola.md) the dq CHAIR through every DQ sheave
     train on the stations' spin: lift_chair.carrier_sweep (the chair on lane s faces f = -s, its right
     outboard; grip top ON the lane, round-2 D8; 1 cm steps over the whole train, both lanes) +
     carrier_verdict (the family rule: nothing crossed but the F10 convention set - rims, liners, sub
     rockers + sub pins - and every other tower part, the rocker mains first, >= CARRIER_CLEAR_M).
     Code: the shipped chair (DQ_OUTBOARD -1) passes on DQ10 / 12 / 14 / 16 / 18 / 20 (the shipped
     ingest head) and the ETDQ, both lanes; the approved revision 3 (+1, hanger inboard on this spin)
     must FAIL on each (the springs through the rocker mains: review F2, measured). The DQ top's nose
     train is GATED since nitpick A3 (D1: its axle ON the lane, the towers' convention; the nitpick-A2
     rope-on-the-rims nose, axle 0.278 m under the lane, is a negative: the grip's wheels / axles cross
     its mains). Nitpick A4 adds the DOCUMENTED LIMIT: every number is a level lane at the axle's height; the
     same sweep with the lane pitched about the nose center / raised above the axle is re-measured and must
     equal lift_terminal_detach.NOSE_LANE_PITCH_LIMIT (the shipped manifest's sheave_trains.lane_pitch_limit;
     a NOTE carries the numbers). Exports: the shipped DQ4M LOD0 FBX is the code build (vertex multiset,
     0.1 mm) and its hanger's vertical run sits at FBX x < 0 (authoring -Y).
     Both x-sign branches: the meshes (chair, towers, stations) mirror together under probe X2, and the
     mover's up lane follows the stations (N as is, P with S1b's flip), so the chair's right is the
     outboard side in both - the sweep is branch-invariant.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_dq_family_seams.py
"""

import json
import math
import os
import sys
import time

import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER_DIR = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(BLENDER_DIR))
sys.path.insert(0, BLENDER_DIR)

import cs2_validate  # noqa: E402
import lift_chair as LC  # noqa: E402
import lift_terminal  # noqa: E402
import lift_terminal_detach as LTD  # noqa: E402
import lift_tower as LT  # noqa: E402

IMPORT = os.path.join(REPO, "assets", "import")
FAILS = []
NOTES = []
EPS = 1e-6


def check(cond, label, detail=""):
    if cond:
        print(f"  ok   {label} {detail}".rstrip())
    else:
        print(f"  FAIL {label} {detail}".rstrip())
        FAILS.append(label)


def close(a, b, eps=EPS):
    return a is not None and b is not None and abs(float(a) - float(b)) <= eps


def manifest(rel, name):
    p = os.path.join(IMPORT, rel, name, f"{name}.manifest.json")
    if not os.path.isfile(p):
        NOTES.append(f"skipped {rel}/{name}: no manifest")
        print(f"  skip {rel}/{name}: no manifest")
        return None
    with open(p, "r", encoding="utf-8") as fh:
        return json.load(fh)


def code_seams():
    print("[seams] 1. code")
    with open(LTD.CARRIERS_PATH, "r", encoding="utf-8") as fh:
        rows = json.load(fh)["carriers"]
    dq, tr, gd8 = rows["dq"], rows["tr"], rows["gd8"]
    carrier = LTD.load_carrier("dq")
    base = LTD.params_for("base", False, carrier)
    top = LTD.params_for("top", False, carrier)
    et = LT.VARIANTS["ETDQ"]
    # S1
    check(close(et.axle, base.z_rope), "S1 ETDQ axle == DQ base station lane rope y",
          f"({et.axle} vs {base.z_rope})")
    check(close(LT.DQ_ENTRY_ROPE_Y, base.z_rope), "S1 lift_tower.DQ_ENTRY_ROPE_Y == station z_rope",
          f"({LT.DQ_ENTRY_ROPE_Y})")
    check(base.hosted_tower and not top.hosted_tower, "S1 only the base station hosts the ETDQ")
    check(close(et.hosted_z, base.anchor_x), "S1 ETDQ hosted z == base station anchor z",
          f"({et.hosted_z} vs {base.anchor_x})")
    check(close(base.z_wheel, base.z_rope) and close(top.z_rope, base.z_rope),
          "S1 wheel y == lane y, base == top", f"({base.z_wheel}, {top.z_rope})")
    # S2
    lane = float(dq["lane_x_m"])
    check(close(base.lane_y, lane) and close(top.lane_y, lane), "S2 station lane == carriers dq lane",
          f"({base.lane_y})")
    check(close(base.rim_r, lane), "S2 station turn radius (bullwheel rim) == lane", f"({base.rim_r})")
    for k, s in sorted(LT.VARIANTS.items()):
        # Revision 1b: the gondola tubular family (GD) sits on the gd8 lanes (+-3.0)
        want = {"DQ": lane, "GD": float(gd8["lane_x_m"])}.get(s.family, float(tr["lane_x_m"]))
        check(close(s.gauge / 2.0, want), f"S2 {k} gauge/2 == {want}", f"({s.gauge / 2.0})")
    # S3
    check(close(LT.VARIANTS["DQ10"].axle, float(dq["axle_m"])), "S3 DQ10 axle == carriers dq axle_m",
          f"({LT.VARIANTS['DQ10'].axle})")
    check(close(base.line_axle, float(dq["axle_m"])), "S3 station line_axle == carriers dq axle_m")
    check(close(LT.VARIANTS["FG8"].axle, float(tr["axle_m"])), "S3 FG8 axle == carriers tr axle_m",
          f"({LT.VARIANTS['FG8'].axle})")
    # S4
    check(close(LC.CARRIERS["dq"]["hang_m"], dq["hang_m"]) and close(base.hang, dq["hang_m"]),
          "S4 dq hang: lift_chair == carriers == station", f"({dq['hang_m']})")
    check(close(LC.HEIGHT_CONTRACT, tr["hang_m"]), "S4 tr hang: lift_chair HEIGHT_CONTRACT == carriers",
          f"({tr['hang_m']})")
    check(LC.STYLES["dq"].carrier == "dq" and LC.STYLES["tr"].carrier == "tr", "S4 chair styles name their rows")
    # S5
    y_dq = round(float(dq["axle_m"]) + float(dq["grip_top_above_rope_m"]) + 0.03, 4)
    y_tr = round(float(tr["axle_m"]) + float(tr["grip_top_above_rope_m"]) + 0.03, 4)
    check(close(y_dq, 8.25) and close(y_tr, 7.15), "S5 AnchorTop chair rows dq 8.25 / tr 7.15",
          f"({y_dq} / {y_tr})")
    # S6
    check(LT.DQ_STATION_INBOARD_MAX <= base.x_nose - 0.30 + 1e-9,
          "S6 ETDQ inboard limit keeps 0.30 m off the station nose",
          f"(limit {LT.DQ_STATION_INBOARD_MAX}, nose {base.x_nose})")
    # S1n (art-601): the base without its hosted ETDQ (BASE_NOSE_TRAIN) keeps the anchors and the lane, drops the host and
    # carries the top's nose train with its outer rim tip at anchor_x - 0.10 and the axle on the lane
    LTD.BASE_NOSE_TRAIN = True
    try:
        nb = LTD.params_for("base", False, carrier)
        T = LTD.nose_train_layout(nb)
        check(not nb.hosted_tower and close(nb.anchor_x, base.anchor_x) and close(nb.z_rope, base.z_rope),
              "S1n nose-train base: no host, anchors and lane unchanged", f"(anchor {nb.anchor_x}, lane {nb.z_rope})")
        check(close(T["x_out"], nb.anchor_x - 0.10) and close(T["axle"], nb.z_rope) and T["x_in"] < nb.x_nose,
              "S1n nose train: outer rim tip at anchor - 0.10, axle on the lane, inboard end outside the nose",
              f"(x {T['x_out']}..{T['x_in']}, axle {T['axle']}, nose {nb.x_nose})")
    finally:
        LTD.BASE_NOSE_TRAIN = False
    return dq, tr, base, top


def export_seams(dq, tr, base, top):
    print("[seams] 2. exports")
    chair = manifest("chairs/M", "SkiLiftChairDQ4M")
    chair_tr = manifest("chairs/M", "SkiLiftChair6M")
    sb = manifest("stations/detachable/A", "SkiDQBaseStationA")
    st = manifest("stations/detachable/A", "SkiDQTopStationA")
    towers = {k: manifest("towers/O", f"SkiLiftTower{k}O") for k in sorted(LT.VARIANTS)}
    if chair:
        row = chair["carrier"]["dq"]
        check(close(row["hang_m"], dq["hang_m"]) and close(row["grip_top_above_rope_m"],
                                                           dq["grip_top_above_rope_m"]),
              "S7 chair DQ4M manifest carrier row == carriers.json")
        check(close(chair["verify"]["height_m"], dq["hang_m"], 1e-4), "S7 chair DQ4M verified hang",
              f"({chair['verify']['height_m']:.4f})")
    if chair_tr:
        check(close(chair_tr["verify"]["height_m"], tr["hang_m"], 1e-4), "S7 chair 6M verified hang",
              f"({chair_tr['verify']['height_m']:.4f})")
    for m, p in ((sb, base), (st, top)):
        if not m:
            continue
        c = m["contract"]
        tag = m["name"]
        check(close(c["lane_rope_y"], p.z_rope), f"S7 {tag} lane_rope_y == code", f"({c['lane_rope_y']})")
        check(close(c["wheel_local_prefab"][1], p.z_rope), f"S7 {tag} wheel y == lane")
        ys = sorted({round(a["a"][1], 6) for a in c["anchors_hv"]})
        xs = sorted({round(abs(a["a"][0]), 6) for a in c["anchors_hv"]})
        check(ys == [round(p.z_rope, 6)] and xs == [round(p.lane_y, 6)],
              f"S7 {tag} HV anchors start at (+-lane, lane y)", f"(x {xs}, y {ys})")
        check(close(c["net_sub_objects_y"]["y"], round(float(dq["axle_m"]) +
                                                         float(dq["grip_top_above_rope_m"]) + 0.03, 4)),
              f"S7 {tag} chair NetSubObjects y 8.25", f"({c['net_sub_objects_y']['y']})")
    et = towers.get("ETDQ")
    if et and sb:
        h, need = et["hosting"], sb["contract"]["hosted_tower"]
        check(close(h["rope_y"], need["rope_y_required"]),
              "S1 shipped ETDQ rope_y == base station rope_y_required",
              f"({h['rope_y']} vs {need['rope_y_required']})")
        check(h["position"] == need["position"] and h["rotation_quat"] == need["rotation_quat"],
              "S1 shipped ETDQ hosted pose == base station record", f"({h['position']})")
        check(h["station_inboard_z"] <= LT.DQ_STATION_INBOARD_MAX, "S6 shipped ETDQ inboard end",
              f"({h['station_inboard_z']:.3f})")
    for k, m in sorted(towers.items()):
        if not m:
            continue
        s = LT.VARIANTS[k]
        check(close(m["axle"], s.axle) and close(m["top_anchor_y"], s.top, 1e-3) and close(m["gauge"], s.gauge),
              f"S7 {m['name']} manifest axle/top/gauge == code",
              f"({m['axle']} / {m['top_anchor_y']} / {m['gauge']})")
        check(close(m["bounds_m"]["max_z"], s.top, 2e-3), f"S7 {m['name']} bounds top == spec top",
              f"({m['bounds_m']['max_z']:.4f})")
        if s.family == "DQ" and s.role == "line" and sb:
            ho = m.get("catalog_handoff", {})
            check(close(ho.get("piece_lanes", [None, None])[1], sb["contract"]["net_sub_objects_y"]["line_axle"]),
                  f"S3 {m['name']} piece lane == station line_axle", f"({ho.get('piece_lanes')})")


# --------------------------------------------------------------------------------------
# S8: the hosted ETDQ against the base station's geometry
# --------------------------------------------------------------------------------------

def _arrays(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3), [tuple(p.vertices) for p in me.polygons]


def _face_names(obj, table):
    me = obj.data
    q = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", q)
    return [table[i - 1] for i in q]


def _hosted(co_prefab, pos, quat):
    """ETDQ prefab coords -> station prefab coords: the station's hosted pose (Unity quat
    (x, y, z, w) = (0, 1, 0, 0) is 180 deg about y)."""
    if [round(float(c), 6) for c in quat] != [0.0, 1.0, 0.0, 0.0]:
        raise ValueError(f"hosted rotation {quat}: only 180 deg about y is modeled here")
    out = co_prefab * np.array([-1.0, 1.0, -1.0])
    return out + np.asarray(pos, dtype=np.float64)


def _overlap(st, et, label):
    """st / et = (coords, polys, names | None). Returns sorted {(station part, tower part)}."""
    sco, spolys, snames = st
    tco, tpolys, tnames = et
    if not spolys or not tpolys:
        return []
    a = BVHTree.FromPolygons([tuple(v) for v in sco], spolys, epsilon=0.0)
    b = BVHTree.FromPolygons([tuple(v) for v in tco], tpolys, epsilon=0.0)
    pairs = a.overlap(b)
    return sorted({((snames[i] if snames else f"face{i}"), (tnames[j] if tnames else f"face{j}"))
                   for i, j in pairs})


def _keep(co, polys, names, drop):
    ix = [k for k, n in enumerate(names) if not drop(n)]
    return co, [polys[k] for k in ix], [names[k] for k in ix]


def s8_code(base):
    """Both generators build in this process; overlaps reported by part name."""
    print("[seams] 3a. S8 code: base station (bevels on) x ETDQ (rebuild) at the hosted pose")
    lift_terminal.clear_scene()
    parts = LTD.build_station(base)
    st, stable, _src = LTD.assemble(parts, True, base)
    sco, spolys = _arrays(st)
    sco = sco[:, [1, 2, 0]]                      # authoring (x, y, z) -> prefab (y, z, x)
    snames = _face_names(st, stable)
    rope = lambda n: n.startswith("haulrope_")   # noqa: E731
    st_levels = {0: _keep(sco, spolys, snames, rope),
                 1: _keep(sco, spolys, snames,
                          lambda n: rope(n) or n.startswith(LTD.LOD1_DELETE))}
    LT.clear_scene()
    spec = LT.VARIANTS["ETDQ"]
    t, ttable, _s, _i = LT.build_tower_v2(spec, "rebuild", finish=False)
    lods = LT.lod_chain_v2(t, t.name, spec)
    et_levels = {}
    for lvl, obj in enumerate([t] + lods):
        co, polys = _arrays(obj)                 # net frame (x gauge, y = -travel, z up)
        pf = np.stack([co[:, 0], co[:, 2], -co[:, 1]], axis=1)   # export bake (x, z, -y)
        et_levels[lvl] = (_hosted(pf, (0.0, 0.0, spec.hosted_z), (0, 1, 0, 0)), polys,
                          _face_names(obj, ttable))
    zmax = float(et_levels[0][0][:, 2].max())
    check(abs(zmax - (spec.hosted_z + 2.388)) < 2e-3, "S8 code: placed ETDQ inboard end",
          f"(z {zmax:.3f})")
    for sl in (0, 1):
        for el in (0, 1):
            hits = _overlap(st_levels[sl], et_levels[el], f"st{sl} x et{el}")
            check(not hits, f"S8 code: station LOD{sl} x ETDQ LOD{el} overlap (minus haulrope_) "
                            f"is empty", f"({len(hits)} part pairs {hits[:6]})")
    LT.clear_scene()


def _import_prefab(path):
    """Shipped FBX -> (prefab coords in m, polys). The importer keeps the raw cm, prefab
    frame (x lateral, y up, z travel) with an identity matrix (checked)."""
    objs = cs2_validate.import_fbx_temp(path)
    try:
        meshes = [o for o in objs if o.type == "MESH"]
        if len(meshes) != 1:
            raise RuntimeError(f"{path}: {len(meshes)} meshes")
        o = meshes[0]
        if any(abs(o.matrix_world[i][j] - (1.0 if i == j else 0.0)) > 1e-9
               for i in range(4) for j in range(4)):
            raise RuntimeError(f"{path}: imported with a non-identity matrix")
        co, polys = _arrays(o)
        return co / 100.0, polys
    finally:
        cs2_validate.remove_objects(objs)


def s8_exports(base):
    print("[seams] 3b. S8 exports: shipped SkiDQBaseStationA x SkiLiftTowerETDQO")
    sdir = os.path.join(IMPORT, "stations", "detachable", "A", "SkiDQBaseStationA")
    tdir = os.path.join(IMPORT, "towers", "O", "SkiLiftTowerETDQO")
    sb = manifest("stations/detachable/A", "SkiDQBaseStationA")
    et = manifest("towers/O", "SkiLiftTowerETDQO")
    if not sb or not et:
        return
    pose = sb["contract"]["hosted_tower"]
    st_levels, et_levels = {}, {}
    for lvl, suf in ((0, ""), (1, "_LOD1")):
        fs = os.path.join(sdir, f"SkiDQBaseStationA{suf}.fbx")
        ft = os.path.join(tdir, f"SkiLiftTowerETDQO{suf}.fbx")
        if not (os.path.isfile(fs) and os.path.isfile(ft)):
            NOTES.append(f"S8 exports LOD{lvl}: FBX missing")
            print(f"  skip S8 exports LOD{lvl}: FBX missing")
            continue
        sco, spolys = _import_prefab(fs)
        lane, ry = base.lane_y, base.z_rope
        rr = sb.get("rope_r_m", LTD.ROPE_R) + 0.005            # per asset (art-601 --thin-rope)
        d = np.hypot(np.abs(sco[:, 0]) - lane, sco[:, 1] - ry)
        near = (d <= rr) & (sco[:, 2] < 0.0)      # the haul ropes run anchor -> wheel; the
        names = ["haulrope" if all(near[v] for v in pl) else "station"  # wheel is at z 9.6
                 for pl in spolys]
        kept = [pl for pl, n in zip(spolys, names) if n != "haulrope"]
        st_levels[lvl] = (sco, kept, None)       # None: hits are counted per face pair
        tco, tpolys = _import_prefab(ft)
        et_levels[lvl] = (_hosted(tco, pose["position"], pose["rotation_quat"]), tpolys, None)
        print(f"  info S8 exports LOD{lvl}: station {len(spolys)} faces "
              f"({names.count('haulrope')} haul-rope faces excluded), ETDQ {len(tpolys)} faces")
    if 0 in et_levels:
        zmax = float(et_levels[0][0][:, 2].max())
        check(abs(zmax - et["hosting"]["station_inboard_z"]) < 2e-3,
              "S8 exports: placed ETDQ inboard end == its manifest", f"(z {zmax:.3f})")
    for sl in sorted(st_levels):
        for el in sorted(et_levels):
            hits = _overlap(st_levels[sl], et_levels[el], f"st{sl} x et{el}")
            where = ""
            if hits:
                sco, spolys, _n = st_levels[sl]
                v = np.array([sco[k] for h in hits for k in spolys[int(h[0][4:])]])
                where = (f" at station x {v[:, 0].min():.2f}..{v[:, 0].max():.2f}, "
                         f"y {v[:, 1].min():.2f}..{v[:, 1].max():.2f}, "
                         f"z {v[:, 2].min():.2f}..{v[:, 2].max():.2f}")
            check(not hits, f"S8 exports: station LOD{sl} x ETDQ LOD{el} overlap (minus haul "
                            f"ropes) is empty", f"({len(hits)} face pairs{where})")


# --------------------------------------------------------------------------------------
# S9 (nitpick A2): the dq chair through every DQ sheave train on the stations' spin
# --------------------------------------------------------------------------------------

S9_TOWERS = (("DQ10", "ingest"), ("DQ12", "ingest"), ("DQ14", "ingest"), ("DQ16", "ingest"),
             ("DQ18", "ingest"), ("DQ20", "ingest"), ("ETDQ", "rebuild"))
S9 = {}


def _named(obj, table):
    co, polys = _arrays(obj)
    return co, polys, _face_names(obj, table)


def _s9_chair(outboard):
    """The dq chair LOD0 in code with lift_chair.DQ_OUTBOARD = outboard: (authoring coords, polys, names, hang)."""
    saved = LC.DQ_OUTBOARD
    LC.clear_scene()
    LC.DQ_OUTBOARD = outboard
    try:
        st = LC.STYLES["dq"]
        row = LC.CARRIERS["dq"]
        obj, info = LC.build_style_chair(st, row, f"S9Dq{'P' if outboard > 0 else 'N'}", shade=False)
        _co, polys, names = _named(obj, info["table"])
        return (LC.authoring_coords(obj), polys, names, float(row["hang_m"]))
    finally:
        LC.DQ_OUTBOARD = saved


def _s9_line(v):
    s = {k: (d["mains"], len(d["fail"])) for k, d in v.items()}
    return ", ".join(f"lane {k:+.0f} mains {m[0] if m else '-'} ({m[1] + '/' + m[2] if m else ''}) fails {n}"
                     for k, (m, n) in sorted(s.items()))


def s9_code(top):
    print("[seams] 4a. S9 code: the dq chair through every DQ train (stations' spin, grip top on the lane)")
    new, old = _s9_chair(LC.DQ_OUTBOARD), _s9_chair(1.0)
    check(LC.DQ_OUTBOARD < 0.0, "S9 lift_chair.DQ_OUTBOARD is -1 (the grip side on the riders' right)",
          f"({LC.DQ_OUTBOARD:+.0f})")
    for key, head in S9_TOWERS:
        LT.clear_scene()
        spec = LT.VARIANTS[key]
        t, ttable, _s, _i = LT.build_tower_v2(spec, head, finish=False)
        co, polys, names = _named(t, ttable)
        tg = (np.stack([-co[:, 1], co[:, 0], co[:, 2]], axis=1), polys, names)   # net -> authoring
        lane = spec.gauge / 2.0
        zl = (lambda x, a=spec.axle: a)
        vn = LC.carrier_verdict(LC.carrier_sweep(new, tg, lane, zl), "gantry")
        vo = LC.carrier_verdict(LC.carrier_sweep(old, tg, lane, zl), "gantry")
        S9[key] = {"new": vn, "old": vo}
        ok = all(not d["fail"] and d["mains"] is not None and d["mains"][0] >= LC.CARRIER_CLEAR_M
                 for d in vn.values())
        check(ok, f"S9 {key} ({head}): the shipped dq grip clears the rocker mains >= {LC.CARRIER_CLEAR_M} "
                  f"on both lanes, nothing but the F10 set crossed", f"({_s9_line(vn)})")
        bad = all(any(f[1].startswith("spring_") and LC.carrier_part_core(f[2]).startswith("rocker_main_")
                      for f in d["fail"]) for d in vo.values())
        check(bad, f"S9 {key}: negative - the approved revision-3 chair (+1, hanger inboard on this spin) "
                   f"puts its springs through the rocker mains", f"({_s9_line(vo)})")
    # nitpick A3 (ruling D1): the DQ top's nose train carries its AXLE ON THE LANE (the towers' convention):
    # GATED now - the shipped chair clears its mains like a tower's; the approved chair and the nitpick-A2 nose
    # (NOSE_AXLE_LEGACY: rope on the rims, axle 0.278 under the lane) are the negatives
    def nose_of(legacy):
        saved = LTD.NOSE_AXLE_LEGACY
        LTD.NOSE_AXLE_LEGACY = legacy
        try:
            LT.clear_scene()
            lift_terminal.clear_scene()
            parts = LTD.build_station(top)
            st, stable, _src = LTD.assemble(parts, True, top)
            co, polys, names = _named(st, stable)
            axle = LTD.nose_train_layout(top)["axle"]
        finally:
            LTD.NOSE_AXLE_LEGACY = saved
        k = [i for i, n in enumerate(names) if n.startswith(("sheave_guide_", "guideframe_"))]
        return (co, [polys[i] for i in k], [names[i] for i in k]), axle
    nose, axle = nose_of(False)
    zl = (lambda x, z=top.z_rope: z)
    vn = LC.carrier_verdict(LC.carrier_sweep(new, nose, top.lane_y, zl), "gantry")
    vo = LC.carrier_verdict(LC.carrier_sweep(old, nose, top.lane_y, zl), "gantry")
    S9["DQ top nose"] = {"new": vn, "old": vo}
    check(LTD.nose_axle_on_lane("DQ") and close(axle, top.z_rope),
          "S9 DQ top nose: the axle ON the lane (NOSE_AXLE_ON_LANE, the towers' convention)",
          f"(axle {axle:.3f}, lane {top.z_rope})")
    ok = all(not d["fail"] and d["mains"] is not None and d["mains"][0] >= LC.CARRIER_CLEAR_M for d in vn.values())
    check(ok, f"S9 DQ top nose train (axle {axle:.3f} = lane): the shipped dq grip clears the rocker mains >= "
              f"{LC.CARRIER_CLEAR_M} on both lanes, nothing but the F10 set crossed", f"({_s9_line(vn)})")
    bad = all(d["fail"] for d in vo.values())
    check(bad, "S9 DQ top nose: negative - the approved revision-3 chair (+1) crosses the nose mains",
          f"({_s9_line(vo)})")
    leg, axle_leg = nose_of(True)
    vl = LC.carrier_verdict(LC.carrier_sweep(new, leg, top.lane_y, zl), "gantry")
    S9["DQ top nose legacy"] = {"new": vl}
    bad = all(any(f[1].startswith(("wheel_", "axle_")) and LC.carrier_part_core(f[2]).startswith("rocker_main_")
                  for f in d["fail"]) for d in vl.values())
    check(bad, f"S9 DQ top nose: negative - the nitpick-A2 nose (NOSE_AXLE_LEGACY, rope on the rims, axle "
               f"{axle_leg:.3f}) puts the shipped grip's wheels / axles through its mains (D1)", f"({_s9_line(vl)})")
    # nitpick A4 (A3 check 1c): the DOCUMENTED LIMIT. Every number above is a LEVEL lane at the axle's height. The same
    # sweep with the lane pitched about the nose center / raised above the axle is re-measured here and must equal
    # lift_terminal_detach.NOSE_LANE_PITCH_LIMIT (the manifest's sheave_trains.lane_pitch_limit): a geometry or
    # sweep change that moves the limit fails here instead of leaving a stale note.
    lim = LTD.NOSE_LANE_PITCH_LIMIT
    x0 = LTD.nose_train_layout(top)["x0"]

    def nearest(zfn):
        v = LC.carrier_verdict(LC.carrier_sweep(new, nose, top.lane_y, zfn), "gantry")
        d = min(r["mains"][0] for r in v.values())
        return "crossed" if d < 0.0 else round(d, 4)

    def agree(got, want):
        return all((got[k] == w) if isinstance(w, str) else (not isinstance(got[k], str) and abs(got[k] - w) <= 1.5e-4)
                   for k, w in want.items())
    pitch = {"0 %": 0.0, "0.14 %": 0.0014, "0.5 %": 0.005, "1.0 %": 0.010, "-1.0 %": -0.010, "1.1 %": 0.011,
             "-1.1 %": -0.011, "25 %": 0.25}
    got_p = {k: nearest(lambda x, z=top.z_rope, s=s: z + s * (x - x0)) for k, s in pitch.items()}
    height = {"-0.010": -0.010, "0": 0.0, "+0.002": 0.002, "+0.010": 0.010, "+0.012": 0.012, "+0.030": 0.030}
    got_h = {k: nearest(lambda x, z=top.z_rope + dz: z) for k, dz in height.items()}
    S9["DQ top nose limit"] = {"pitch": got_p, "height": got_h}
    check(agree(got_p, lim["clearance_m_pitch_about_nose_centre"]),
          "S9 DQ top nose: the pitched-lane limit re-measured = NOSE_LANE_PITCH_LIMIT (manifest note)", f"({got_p})")
    check(agree(got_h, lim["clearance_m_lane_height_above_axle"]),
          "S9 DQ top nose: the lane-height limit re-measured = NOSE_LANE_PITCH_LIMIT (manifest note)", f"({got_h})")
    check(got_p["0 %"] >= LC.CARRIER_CLEAR_M and got_p["0.14 %"] >= LC.CARRIER_CLEAR_M - 1.5e-4
          and got_p["1.1 %"] == "crossed" and got_p["-1.1 %"] == "crossed" and got_h["+0.012"] == "crossed",
          "S9 DQ top nose: the gate holds on a level lane only - lost at 0.14 % pitch, crossed from 1.1 % / +12 mm")
    NOTES.append("S9 DQ top nose: LEVEL-LANE LIMIT (documented, nitpick A4): the 11.5 mm wheel / rocker_main gap is a "
                 f"level-lane, axle-height number - pitch about the nose center {got_p}; lane height above the axle "
                 f"{got_h}; the DQ towers share it; in game the lane through the nose train is level (the rigid "
                 "building, one straight in-station run; the stations do not flatten terrain) at 3.70..3.73 "
                 "(anchors a..d), see NOSE_LANE_PITCH_LIMIT['basis']")
    lift_terminal.clear_scene()


def s9_exports():
    print("[seams] 4b. S9 exports: the shipped SkiLiftChairDQ4M")
    path = os.path.join(IMPORT, "chairs", "M", "SkiLiftChairDQ4M", "SkiLiftChairDQ4M.fbx")
    if not os.path.isfile(path):
        NOTES.append("S9 exports: no DQ4M FBX")
        print("  skip S9 exports: no DQ4M FBX")
        return
    objs = cs2_validate.import_fbx_temp(path)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        raw = np.array([v.co[:] for v in o.data.vertices], dtype=np.float64) / 100.0
    finally:
        cs2_validate.remove_objects(objs)
    a = np.stack([raw[:, 2], raw[:, 0], raw[:, 1]], axis=1)           # FBX raw = authoring (y, z, x)
    run = a[(a[:, 2] > 1.8) & (a[:, 2] < 3.0) & (np.abs(a[:, 1]) > 0.15) & (np.abs(a[:, 1]) < 0.40)]
    check(len(run) > 0 and float(run[:, 1].max()) < 0.0,
          "S9 exports: the shipped DQ4M hanger run on authoring -Y (FBX x < 0)",
          f"(y {run[:, 1].min():.3f}..{run[:, 1].max():.3f})" if len(run) else "(no hanger run found)")
    code = _s9_chair(LC.DQ_OUTBOARD)[0]

    def srt(v):
        v = np.round(v, 4)
        return v[np.lexsort((v[:, 2], v[:, 1], v[:, 0]))]
    same = code.shape == a.shape and float(np.abs(srt(code) - srt(a)).max()) <= 1e-4
    check(same, "S9 exports: the shipped DQ4M LOD0 = the code build (vertex multiset, 0.1 mm)",
          f"({len(a)} / {len(code)} vertices)")
    LC.clear_scene()
    # nitpick A3 (D1): the shipped DQ top's manifest records the nose train with its axle on the lane
    mt = manifest("stations/detachable/A", "SkiDQTopStationA")
    if mt is not None:
        st = mt.get("sheave_trains") or {}
        check(st.get("axle_on_lane") is True and close(st.get("axle_z"), st.get("rope_z")),
              "S9 exports: the shipped SkiDQTopStationA nose train's axle on the lane (sheave_trains)",
              f"(axle {st.get('axle_z')}, rope {st.get('rope_z')}, rule {st.get('axle_rule')!r})")
        # nitpick A4: the shipped manifest carries the documented limit and the C14 numbers
        pc = st.get("part_clash") or {}
        check(st.get("lane_pitch_limit") == json.loads(json.dumps(LTD.NOSE_LANE_PITCH_LIMIT))
              and pc.get("undeclared") == 0 and pc.get("contained") == 0 and st.get("hanger_at_yoke") is True
              and "C14" in (mt.get("contracts") or {}),
              "S9 exports: the shipped SkiDQTopStationA manifest records the level-lane limit (sheave_trains."
              "lane_pitch_limit = NOSE_LANE_PITCH_LIMIT), C14 with 0 undeclared pairs, the hangers at their yokes",
              f"(C14 {pc.get('intersecting_pairs')} pairs = {pc.get('declared_pairs')} declared + "
              f"{len(pc.get('known_open') or [])} known-open)")


def main():
    t0 = time.perf_counter()
    dq, tr, base, top = code_seams()
    export_seams(dq, tr, base, top)
    s8_code(base)
    s8_exports(base)
    s9_code(top)
    s9_exports()
    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_dq_family_seams: {len(FAILS)} seam(s) broken: {FAILS}")
        sys.exit(1)
    worst = min(d["mains"][0] for k, v in S9.items() if k not in ("DQ top nose", "DQ top nose legacy", "DQ top nose limit")
                for d in v["new"].values())
    nose = min(d["mains"][0] for d in S9["DQ top nose"]["new"].values())
    print(f"PASS test_dq_family_seams: code + shipped manifests agree on rope 3.70 at the hosted "
          f"tower, gauge, lane height, hang and chair rows; the hosted ETDQ clears the base "
          f"station on LOD0/LOD1 in code and in the shipped FBXs (S8); S9 the shipped dq grip clears the "
          f"rocker mains of DQ10-DQ20 + ETDQ on both lanes (min {worst:.4f} m >= {LC.CARRIER_CLEAR_M}; the "
          f"approved +1 chair fails each) and the DQ top nose with its axle on the lane (nitpick A3 D1: "
          f"{nose:.4f} m; the nitpick-A2 rope-on-the-rims nose fails) ({len(NOTES)} note(s): {NOTES}, {dt:.1f} s)")


main()
