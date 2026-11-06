"""
test_carrier_station_sweep.py - the B2 v2 split carriers (lift_chair --split --envelope, generation O) through the
stations (import prep 2026-11-03, docs/research/art/breakover-twins-2026-11/v2/import-prep.md). The towers' gate is
test_grip_clearance; this is the stations' side. Reads the export folders under assets/import (chairs/M, N, O): a
missing folder FAILS (this gate exists to check those files before an import).

  CS1 tr, generation-M fixed-grip stations (lift_terminal M3 as it stands: chair_sweep_m + assert_sweep_m, LOD0 +
      LOD1, both lane ends + the top's line-first ends; the chair pair assembled in the chair's authoring frame: the
      body at its pivot, the grip raised by the pin offset 2.32 - 0.1955 = 2.1245, grip top 2.40 = FG_HANG; body =
      z < CHAIR_BODY_Z). The shipped one-piece SkiLiftChair6M and the split N pair in BOTH facings (as M3 runs the
      6M); the O pair in the REAL facing only: the up lane is chain A, the right one looking uphill
      (LiftLoopTopology, StationAlign.UpLaneIsSecond; O1, branch N settled in the 2026-10-15 playtest), so the loop
      turns left at both stations and the turn center is on the chair's LEFT = chair-local +Y (place_chair: y ->
      up x travel) on both lanes and round the wheel: lane s faces f = -s, the arc f = +1. The O pair keeps its clamp
      block and the stem's top run at -Y (outboard, lift_chair ENVELOPE_TR: the towers' wheel envelope), so its
      reverse facing is a pose the game never makes. Negative: the O pair in the reverse facing fails M3 at the
      bullwheel (body -> rope machine).
  CS2 dq, detachable stations (lift_terminal_detach, the letter-C rail DQ_RAIL): SkiLiftChairDQ4GripO +
      SkiLiftChairDQ4N (the dq body keeps generation N) swept along both stations' carrier path in the real facing
      (straights anchor -> wheel tangent every 0.10 m on both lanes, the U every 2 deg; blockout build), distances by
      station class (point-to-triangle both ways, edge samples every 2-3 cm; a triangle overlap = 0): the running
      wheels' tops ON the rail's underside (<= 2 mm), every other grip part >= 0.02 m from the rail and its hangers,
      the tires >= 0.04 m over the carriage, the body >= 0.10 m from rail, hangers, tires and their banks, the body ->
      structure no closer than on the letter-B rail. Negatives: the letter-B rail (DQ_RAIL_LEGACY) leaves the O
      wheels >= 0.20 m below it; the shipped N grip crosses the C rail (the C stations ship with the O grips).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_carrier_station_sweep.py
"""
import math
import os
import sys
import time

import bpy  # noqa: F401
import bmesh
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_validate  # noqa: E402
import cs2_kitrows as KR  # noqa: E402
import lift_terminal as LT  # noqa: E402
import lift_terminal_detach as D  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
CHAIRS = os.path.join(REPO, "assets", "import", "chairs")
FAILS = []
N_OK = [0]
TR_GRIP_Z = 2.32 - 0.1955          # body rope - grip rope (carriers.json tr split; SkiCatalog Pivots, CT-17)
DQ_ROPE = {"grip": 0.3155, "body": 3.08}
PAIRS_TR = {"6M": ("M", "SkiLiftChair6M", None), "N": ("N", "SkiLiftChair6N", "SkiLiftChair6GripN"),
            "O": ("O", "SkiLiftChair6O", "SkiLiftChair6GripO")}
DQ_CLASSES = (("rail", ("detrail_",)), ("hanger", ("railhang_",)), ("tirebank", ("tyrebank_",)), ("tire", ("tyre_",)),
              ("machine", ("sheave_bull", "spoke_", "wheelhub", "wheelrope", "haulrope_", "drive_", "tension_")),
              ("nose", ("sheave_guide", "guideframe_")))


def check(cond, label, detail=""):
    if cond:
        N_OK[0] += 1
        print(f"  ok   {label} {detail}".rstrip())
    else:
        print(f"  FAIL {label} {detail}".rstrip())
        FAILS.append(label)


def read_fbx(path):
    """FBX raw cm -> the chair's authoring frame m: (fz, fx, fy) (x travel, y lateral, z up; lift_terminal S1)."""
    if not os.path.isfile(path):
        raise FileNotFoundError(path)
    objs = cs2_validate.import_fbx_temp(path)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.transform(o.matrix_world)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        raw = np.array([v.co[:] for v in bm.verts], dtype=np.float64) / 100.0
        tris = np.array([[v.index for v in f.verts] for f in bm.faces], dtype=np.int64)
        bm.free()
    finally:
        cs2_validate.remove_objects(objs)
    return np.stack([raw[:, 2], raw[:, 0], raw[:, 1]], axis=1), tris


def fbx(letter, name, level=0):
    return os.path.join(CHAIRS, letter, name, f"{name}{'' if level == 0 else f'_LOD{level}'}.fbx")


# --------------------------------------------------------------------------------------
# CS1 tr
# --------------------------------------------------------------------------------------

def tr_loader(tag):
    letter, body, grip = PAIRS_TR[tag]
    cache = {}

    def load(level=0):
        if level in cache:
            return cache[level]
        co, tris = read_fbx(fbx(letter, body, level))
        if grip:
            cg, tg = read_fbx(fbx(letter, grip, level))
            cg[:, 2] += TR_GRIP_Z
            tris = np.vstack([tris, tg + len(co)])
            co = np.vstack([co, cg])
        seat = co[(np.abs(co[:, 1]) < 0.70) & (co[:, 2] < 0.25)]
        cache[level] = {"co": co, "tris": tris, "body": (co[tris][:, :, 2] < LT.CHAIR_BODY_Z).all(axis=1),
                        "lowest": float(co[:, 2].min()), "cushion": float(seat[:, 2].max()),
                        "top": float(co[:, 2].max()), "path": fbx(letter, body, level)}
        return cache[level]
    return load


def facing_poses(real):
    """sweep_poses_m filtered to one facing: real = the turn center on the chair's left (lane s: f = -s, arc f = +1),
    else the reverse."""
    orig = LT.sweep_poses_m

    def poses(spec, step=0.10, dang=2.0, anchors=None):
        out = []
        for label, q, d, f in orig(spec, step, dang, anchors):
            s = 1.0 if label.endswith("lane+1") else (-1.0 if label.endswith("lane-1") else None)
            ok = (f > 0) if s is None else (f == -s)
            if ok == real:
                out.append((label, q, d, f))
        return out
    return orig, poses


def tr_sweep(kind, co, tris, names, tag, facing):
    """M3 with the `tag` chair: facing None = both (as M3), True = real, False = reverse. Returns (rep, error)."""
    spec = LT.M_SPECS[kind]
    extra = [(-9.0, 7.04), (-12.0, 7.04)] if kind == "return" else []
    saved_load, saved_poses = LT.load_chair_m, LT.sweep_poses_m
    LT.load_chair_m = tr_loader(tag)
    if facing is not None:
        LT.sweep_poses_m = facing_poses(facing)[1]
    try:
        rep = LT.chair_sweep_m(co, tris, names, spec, extra_anchors=extra)
        try:
            LT.assert_sweep_m(co, tris, names, spec, rep)
            err = None
        except LT.MContractError as e:
            err = str(e)
    finally:
        LT.load_chair_m, LT.sweep_poses_m = saved_load, saved_poses
    return rep, err


def cs1():
    print("[cs] CS1 tr: the split pairs through the generation-M stations (M3)")
    out = {}
    for kind in ("drive", "return"):
        LT.clear_scene()
        parts = LT.build_base_m() if kind == "drive" else LT.build_top_m()
        t, _table, _src = LT.assemble_m(parts, True, kind)
        co, tris, names, _ = LT.m_arrays(t)
        kit = LT.fd_kit_rows_m(kind)
        if kit:
            co, tris, names = KR.augment(co, tris, names, kit, LT.m_host)
        for tag, facing in (("6M", None), ("N", None), ("O", True)):
            rep, err = tr_sweep(kind, co, tris, names, tag, facing)
            out[(kind, tag)] = rep
            check(err is None and rep["poses"] > 700,
                  f"CS1 {kind}: {tag} ({'both facings' if facing is None else 'real facing'}) passes M3",
                  f"(body -> structure {rep['min'].get('struct')}, dress {rep['min'].get('dress')}, machine "
                  f"{rep['min'].get('machine')} (capped 0.20); grip zone {rep['grip_min']}; {rep['poses']} poses)"
                  + (f" {err[:200]}" if err else ""))
        rep, err = tr_sweep(kind, co, tris, names, "O", False)
        check(err is not None and "rope machine" in err,
              f"CS1 {kind}: negative - the O pair in the reverse facing (block toward the wheel) fails M3",
              f"(machine {rep['min'].get('machine')} at {rep['where'].get('machine', {}).get('pose')})")
    check(out[("drive", "O")]["min"]["machine"] >= out[("drive", "N")]["min"]["machine"] - 1e-6
          and out[("return", "O")]["min"]["machine"] >= out[("return", "N")]["min"]["machine"] - 1e-6,
          "CS1 the O body keeps at least the N pair's bullwheel clearance on both stations",
          f"(drive {out[('drive', 'O')]['min']['machine']} vs {out[('drive', 'N')]['min']['machine']}, return "
          f"{out[('return', 'O')]['min']['machine']} vs {out[('return', 'N')]['min']['machine']})")


# --------------------------------------------------------------------------------------
# CS2 dq
# --------------------------------------------------------------------------------------

def dq_carrier(grip_letter, grip_name):
    g, gt = read_fbx(fbx(grip_letter, grip_name))
    g[:, 2] -= DQ_ROPE["grip"]
    b, bt = read_fbx(fbx("N", "SkiLiftChairDQ4N"))
    b[:, 2] -= DQ_ROPE["body"]
    # the running wheels: the two 16-gon wheels at travel +-0.20 (r 0.080, centers rope + 0.0285), their lateral band
    # found on the mesh from the vertices over the wheel centers only (the lever roller, 0.175..0.205 out, sits at
    # travel -0.172..-0.108; lift_chair DQ_GRIP wheel 0.050 wide; O: 0.098..0.148 outboard = authoring -Y)
    band = (np.abs(np.abs(g[:, 0]) - 0.20) <= 0.081) & (g[:, 2] >= -0.052) & (g[:, 2] <= 0.110)
    lat = g[band & (g[:, 2] >= 0.105) & (np.abs(np.abs(g[:, 0]) - 0.20) <= 0.02), 1]
    lo, hi = float(lat.min()), float(lat.max())
    wheel = band & (g[:, 1] >= lo - 0.002) & (g[:, 1] <= hi + 0.002)
    return {"g": g, "gt": gt, "b": b, "bt": bt, "wtop": g[wheel & (g[:, 2] >= 0.105)],
            "gnw": gt[~wheel[gt].all(axis=1)], "wheel_lat": (round(lo, 4), round(hi, 4)),
            "wheel_top": round(float(g[wheel, 2].max()), 4)}


def dq_station(kind, legacy):
    saved = D.DQ_RAIL_LEGACY
    D.DQ_RAIL_LEGACY = legacy
    try:
        LT.clear_scene()
        p = D.params_for(kind, False, D.load_carrier("dq"))
        t, _table, _src = D.assemble(D.build_station(p), False, p)
        lay = D.detach_rail(p)
    finally:
        D.DQ_RAIL_LEGACY = saved
    co, tris, names, _ = LT.m_arrays(t)
    cls = np.array([next((c for c, pre in DQ_CLASSES if n.startswith(pre)), "other") for n in names])
    groups = {c: {"bvh": LT._bvh(co, tris[cls == c]), "pts": LT._edge_points(co, tris[cls == c], 0.03)}
              for c in ("rail", "hanger", "tirebank", "tire", "machine", "nose", "other")}
    return p, lay, groups


def dq_poses(p, step=0.10, dang=2.0):
    """The carrier path in the station's authoring frame, real facing (travel -s x on lane s; the U counterclockwise)."""
    R, X, z = p.lane_y, p.x_wheel, p.z_rope
    out = []
    for s in (-1.0, 1.0):
        n = int((X - p.anchor_x) / step) + 1
        for i in range(n + 1):
            out.append((f"lane{int(s):+d}", np.array([min(p.anchor_x + i * step, X), s * R, z]), np.array([-s, 0.0, 0.0])))
    for a in np.arange(-90.0, 90.0 + 1e-6, dang):
        r = math.radians(a)
        out.append(("arc", np.array([X + R * math.cos(r), R * math.sin(r), z]),
                    np.array([-math.sin(r), math.cos(r), 0.0])))
    return out


def _place(co, q, d):
    up = np.array([0.0, 0.0, 1.0])
    return co @ np.stack([d, np.cross(up, d), up], axis=1).T + q


def dq_sweep(p, groups, car):
    LIM = 0.40
    gp, gnw, bp = (LT._edge_points(car["g"], car["gt"], 0.02), LT._edge_points(car["g"], car["gnw"], 0.02),
                   LT._edge_points(car["b"], car["bt"], 0.03))
    rep = {}

    def upd(key, d, where):
        if d < rep.get(key, (9.9,))[0]:
            rep[key] = (round(float(d), 4), where)
    for label, q, d in dq_poses(p):
        where = f"{label} {np.round(q, 2).tolist()}"
        G, B = _place(car["g"], q, d), _place(car["b"], q, d)
        gb, bb = LT._bvh(G, car["gt"]), LT._bvh(B, car["bt"])
        lo, hi = np.minimum(G.min(0), B.min(0)) - 0.5, np.maximum(G.max(0), B.max(0)) + 0.5
        for c, grp in groups.items():
            if grp["bvh"] is None:
                continue
            S = grp["pts"]
            S = S[np.all((S >= lo) & (S <= hi), axis=1)] if len(S) else S
            for who, pts, own in (("grip", _place(gp, q, d), gb), ("body", _place(bp, q, d), bb)):
                dd, _i = LT._min_dist(grp["bvh"], pts, LIM)
                if len(S):
                    dd = min(dd, LT._min_dist(own, S, LIM)[0])
                if grp["bvh"].overlap(own):
                    dd = 0.0
                upd(f"{who}->{c}", dd, where)
            if c == "rail":
                upd("wheeltops->rail", LT._min_dist(grp["bvh"], _place(car["wtop"], q, d), LIM)[0], where)
                upd("grip-wheels->rail", LT._min_dist(grp["bvh"], _place(gnw, q, d), LIM)[0], where)
            if c == "hanger":
                upd("grip-wheels->hanger", LT._min_dist(grp["bvh"], _place(gnw, q, d), LIM)[0], where)
    return rep


def cs2():
    print("[cs] CS2 dq: SkiLiftChairDQ4GripO + SkiLiftChairDQ4N through the DQ stations (letter-C rail)")
    car_o = dq_carrier("O", "SkiLiftChairDQ4GripO")
    car_n = dq_carrier("N", "SkiLiftChairDQ4GripN")
    for kind in ("base", "top"):
        p, lay, gc = dq_station(kind, False)
        r_ = (lay["rr"] - p.lane_y, lay["z0"] - p.z_rope)
        lo, hi = car_o["wheel_lat"]
        check(abs(r_[1] - car_o["wheel_top"]) <= 0.002 and -hi - 1e-3 >= r_[0] - lay["half"] and -lo + 1e-3 <= r_[0] + lay["half"],
              f"CS2 {kind}: the rail on the O wheel plane (DQ_RAIL vs the mesh)",
              f"(rail lane + {r_[0]:.3f} +- {lay['half']}, underside rope + {r_[1]:.4f}; O wheels {-hi:.3f}..{-lo:.3f} "
              f"outboard, tops rope + {car_o['wheel_top']})")
        rc = dq_sweep(p, gc, car_o)
        _pb, _lb, gb = dq_station(kind, True)
        rb = dq_sweep(p, gb, car_o)
        v = {k: x[0] for k, x in rc.items()}
        check(v["wheeltops->rail"] <= 0.002, f"CS2 {kind}: the O running wheels' tops ON the rail (<= 2 mm)",
              f"({v['wheeltops->rail']} m, {rc['wheeltops->rail'][1]}; letter B {rb['wheeltops->rail'][0]})")
        check(v["grip-wheels->rail"] >= 0.02 and v["grip-wheels->hanger"] >= 0.02,
              f"CS2 {kind}: every other grip part >= 0.02 m from the rail and its hangers",
              f"(rail {v['grip-wheels->rail']} at {rc['grip-wheels->rail'][1]}, hangers {v['grip-wheels->hanger']} at "
              f"{rc['grip-wheels->hanger'][1]})")
        check(v["grip->tire"] >= 0.04 and v["grip->tirebank"] >= 0.04,
              f"CS2 {kind}: the tires and their banks >= 0.04 m over the carriage",
              f"(tires {v['grip->tire']}, banks {v['grip->tirebank']})")
        check(min(v[f"body->{c}"] for c in ("rail", "hanger", "tire", "tirebank")) >= 0.10
              and v["body->other"] >= rb["body->other"][0] - 1e-4 and v["body->machine"] >= rb["body->machine"][0] - 1e-4,
              f"CS2 {kind}: the body >= 0.10 m from rail / hangers / tires, structure and rope machine as on letter B",
              f"(rail {v['body->rail']}, hangers {v['body->hanger']}, tires {v['body->tire']}, structure "
              f"{v['body->other']} (B {rb['body->other'][0]}), machine {v['body->machine']})")
        check(rb["wheeltops->rail"][0] >= 0.20, f"CS2 {kind}: negative - the letter-B rail leaves the O wheels below it",
              f"({rb['wheeltops->rail'][0]} m)")
        rn = dq_sweep(p, gc, car_n)
        check(rn["grip->rail"][0] == 0.0, f"CS2 {kind}: negative - the shipped N grip crosses the letter-C rail",
              f"(at {rn['grip->rail'][1]}: the C stations ship with the O grips)")


def main():
    t0 = time.perf_counter()
    cs1()
    cs2()
    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_carrier_station_sweep: {len(FAILS)} failed: {FAILS}")
        sys.exit(1)
    print(f"PASS test_carrier_station_sweep: CS1 tr (6M / N both facings, O real facing; reverse-facing negative) on both "
          f"generation-M stations, CS2 dq (O grip on the letter-C rail, both DQ stations; B-rail and N-grip negatives) "
          f"({N_OK[0]} ok, {dt:.0f} s)")


main()
