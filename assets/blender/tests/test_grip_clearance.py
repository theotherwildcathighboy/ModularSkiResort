"""
Grip clearance gate (breakover / hold-down round v2, 2026-11-03; docs/research/art/breakover-twins-2026-11/v2/): every
carrier, grip and body, past every tower sheave train, on both lanes, along the rope, at every train pose.

Why the rule reads this way: on a real lift the grip passes the support, hold-down and compression sheaves on its
jaws, which sit nearly flush with the rope and stand a limited, smooth amount above it while the sheave battery rocks
over them; the grip body, springs and hanger stay outboard of the wheels (Poma US4441430, US4691643;
docs/research/art/breakover-twins-2026-11/refs/SOURCES.md "Grip passage under compression").

The rule (gated, no exceptions):
  W1  a wheel's rim or liner (`sheave_*_rim`, `liner_*`) may touch the carrier ONLY inside the grip's smooth jaw
      envelope: the grip surface within |z| <= lift_chair.ENVELOPE_Z (0.06 m) of the rope center, in the grip's own
      frame. The grip clipped to |z| > ENVELOPE_Z + ENV_TOL (0.1 mm: a jaw top drawn AT the band edge is inside it,
      float32 after the FBX round trip) and the whole body cross no rim / liner face.
  W2  every other tower part (hubs, bosses, axle caps, sheave arms, rockers, yokes, pins, hangers, tongues, the head,
      the deck, the mast): no face crosses the carrier, and the distance is >= lift_chair.CARRIER_CLEAR_M (0.010 m).
How the carrier rides (CarrierPose.Pose at zero swing, mod/SkiResortMod/Systems/CarrierPose.cs): the grip's rope point
(the split row's grip_rope_m) ON the crown lane (lift_tower.lane_y), the grip pitched with its half's rope run, the body
plumb from the pin (body = pin - BodyPin). CS2 corners the rope at the anchor (cs2_skin), so per half one straight run
from the pivot and the grip switches tangent there. Lanes: n (the grip as exported: -x outboard, away from the mast) and
p (the carrier turned 180 deg about z); both travel directions (the carrier also mirrored along travel, a superset of
either spin). Stops every STEP m from the pivot out past each half's last wheel. Distances: vertices plus edge samples
every SAMPLE m on both sides (a reported distance overstates the true one by at most SAMPLE / 2); an overlap is a BVH
triangle intersection (mathutils BVHTree.overlap), tested wherever a sample comes within OVERLAP_PROBE.
Trains: FG8..FG20 rebuilt through the shipping path and held to their shipped LOD0 vertex for vertex (lift_tower.twin_atlas,
0.2 mm), the FG twins built by lift_tower.build_twin; every DQ / GD member (DQ10..DQ20, GD12..GD20) and its Hold / Combo
twins built headed by lift_head.build_headed (the Head round, DECISIONS 594: the head turns about the rope line with the
mean of the halves). Poses (kind_poses): the G6 grid (cs2_skin.joint_grid: rest, uniform +-theta, the half-breaks at
every mean; theta = SKIN_THETA_MAX for FG, the planner max SKIN_TYPE_MAX_SLOPE DQ 42 / GD 50.2 for DQ / GD with Head =
(front + back) / 2) on the kind's side of the fold, the planner's kind rule (breakover-holddown plan 2.2): support and
Break convex to the art fold 40 deg, Hold concave to 40, Combo +-COMBO_FOLD (its drawn |d| stays under 7.3), so the fold
extremes of each kind are in. LOD0 carrier x LOD0 tower.
Carriers: catalog/carriers.json split rows (tr = FG, dq = DQ, gd8 = GD), meshes found under assets/import, or --carriers
<json> (the same rows; a row's "fbx": {"grip": {"lod0": path}, "body": {"lod0": path} | "unchanged: <repo dir>"}
names the files: a round's carriers before their import).
Negatives (always run): the FG8 member with its carrier moved 0.12 m inboard must fail W1 and W2; every DQ / GD member
with its head held level at uniform +-planner max must fail W2 (the deck end meets the carrier body).
Report only (not gated): --entry sweeps the station-entry compression heads ETDQ / ETGD at rest; --art sweeps every
train over its whole G6 grid both ways (the folds the code can draw, past what the planner gives the kind).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_grip_clearance.py -- [--carriers <json>]
        [--only FG8,FG8Hold] [--shard 1/3] [--report <json>] [--entry] [--art]
"""
import argparse
import json
import math
import os
import sys
import time

import bpy
import mathutils
import numpy as np
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_asset  # noqa: E402
import cs2_sheaves as S  # noqa: E402
import cs2_skin  # noqa: E402
import lift_chair as LC  # noqa: E402
import lift_tower as LT  # noqa: E402
import lift_head as LH  # noqa: E402

REPO = LT._REPO
ENV = LC.ENVELOPE_Z                 # m, the jaw band about the rope center
# m, applied ONLY at the band edge (the clip plane |z| = ENV): a jaw top drawn AT 0.06 m lands up to ~1e-7 m above it after
# the FBX round trip (Blender keeps vertex coordinates as float32; the FBX is in cm), so the edge face would read as
# "past the band" and every compression wheel would fail W1 on it. 0.1 mm is 1000x that error and far under any real gap.
ENV_TOL = 1e-4
CLEAR = LC.CARRIER_CLEAR_M          # m, every non-wheel tower part
STEP = 0.01                         # m between stops along the rope
SAMPLE = 0.003                      # m, edge sample spacing (distance error <= SAMPLE / 2)
PROBE = 0.05                        # m, distances are measured up to here ("> PROBE" beyond)
OVERLAP_PROBE = 0.005               # m, a sample this close runs the exact triangle test
CELL = 0.06                         # m, broad-phase grid cell (>= PROBE: a near pair sits in adjacent cells)
CARRIER_OF = {"FG": "tr", "DQ": "dq", "GD": "gd8"}
# the deployed member meshes (gamedata-mirror prefab -> mesh CID, 2026-11-03): the R pilots, the Q family elsewhere
SHIPPED = {"FG8": "Q/SkiLiftTowerFG8Q", "FG12": "R/SkiLiftTowerFG12R", "FG16": "Q/SkiLiftTowerFG16Q",
           "FG20": "Q/SkiLiftTowerFG20Q", "DQ10": "Q/SkiLiftTowerDQ10Q", "DQ12": "R/SkiLiftTowerDQ12R",
           "DQ14": "Q/SkiLiftTowerDQ14Q", "DQ16": "Q/SkiLiftTowerDQ16Q", "DQ18": "Q/SkiLiftTowerDQ18Q",
           "DQ20": "Q/SkiLiftTowerDQ20Q", "GD12": "R/SkiLiftTowerGD12R", "GD16": "Q/SkiLiftTowerGD16Q",
           "GD20": "Q/SkiLiftTowerGD20Q"}
ENTRY = {"ETDQ": "rebuild", "ETGD": "rebuild"}
COMBO_FOLD = {"FG": 8.0, "DQ": 16.0, "GD": 16.0}    # deg, the push-pull caps (plan 2.2: FG 2S2C 8, DQ / GD 4S4C 16)
BONES = list(LT.SKIN_BONES)
# DQ / GD: the Head round (DECISIONS 594, lift_head.py). The head (gantry, deck, rails, hangers, plates) is skinned to a
# Head bone that TrainPitchSystem turns to the mean of the halves (clamped to the planner max), the halves to head +-20;
# the head no longer stays level while a half pitches, so the carrier body no longer rises into the deck end (the
# limit before this round: DQ 20 / GD 17.5 deg end-raised). DQ / GD trains here are the headed builds, swept to the
# planner max (lift_tower.SKIN_TYPE_MAX_SLOPE: DQ 42 / GD 50.2) with Head = (front + back) / 2. FG has no head.
HEADED = ("DQ", "GD")


def _fail(msg):
    raise AssertionError(msg)


def is_wheel(name):
    """The rope's running surface: rims and liners (hubs, bosses and axle caps are not)."""
    return name.startswith("liner_") or (name.startswith("sheave_") and name.endswith("_rim") and "hub" not in name)


# --------------------------------------------------------------------------------------
# geometry helpers
# --------------------------------------------------------------------------------------

def _tris(polys):
    out = []
    for p in polys:
        for k in range(1, len(p) - 1):
            out.append((p[0], p[k], p[k + 1]))
    return np.array(out, dtype=np.int64).reshape(-1, 3)


def _samples(co, tris):
    """(points, owning triangle) = every vertex used plus edge samples every <= SAMPLE m."""
    e = np.sort(np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]]), axis=1)
    own = np.tile(np.arange(len(tris)), 3)
    e, first = np.unique(e, axis=0, return_index=True)
    own_e = own[first]
    a, b = co[e[:, 0]], co[e[:, 1]]
    n = np.maximum(1, np.ceil(np.linalg.norm(b - a, axis=1) / SAMPLE).astype(np.int64))
    rep = np.repeat(np.arange(len(e)), n - 1)
    if len(rep):
        start = np.repeat(np.cumsum(n - 1) - (n - 1), n - 1)
        k = np.arange(len(rep)) - start + 1
        t = (k / n[rep])[:, None]
        mid = a[rep] + (b[rep] - a[rep]) * t
    else:
        mid = np.zeros((0, 3))
    vt = np.full(len(co), -1, dtype=np.int64)
    vt[tris[:, 0]] = np.arange(len(tris))
    vt[tris[:, 1]] = np.arange(len(tris))
    vt[tris[:, 2]] = np.arange(len(tris))
    used = np.nonzero(vt >= 0)[0]
    pts = np.concatenate([co[used], mid])
    owner = np.concatenate([vt[used], own_e[rep]])
    return pts, owner


def _clip(poly, c, above):
    """Sutherland-Hodgman: the part of the convex polygon `poly` (list of 3-vectors) with z >= c (above) or z <= c."""
    out = []
    for i in range(len(poly)):
        a, b = poly[i], poly[(i + 1) % len(poly)]
        ia, ib = (a[2] >= c) if above else (a[2] <= c), (b[2] >= c) if above else (b[2] <= c)
        if ia:
            out.append(a)
        if ia != ib:
            out.append(a + (b - a) * ((c - a[2]) / (b[2] - a[2])))
    return out


def forbidden_part(co, tris):
    """The grip's triangles clipped to |z| > ENV + ENV_TOL (the part no rim / liner may touch): (coords, tris)."""
    vs, ts = [], []
    lim = ENV + ENV_TOL
    for t in co[tris]:
        if np.abs(t[:, 2]).max() <= lim:
            continue
        for poly in (_clip(list(t), lim, True), _clip(list(t), -lim, False)):
            if len(poly) < 3:
                continue
            base = len(vs)
            vs += [np.asarray(p, dtype=np.float64) for p in poly]
            ts += [(base, base + k, base + k + 1) for k in range(1, len(poly) - 1)]
    return np.array(vs).reshape(-1, 3), np.array(ts, dtype=np.int64).reshape(-1, 3)


def _bvh(co, tris):
    return BVHTree.FromPolygons([tuple(v) for v in co], [tuple(int(i) for i in t) for t in tris], epsilon=0.0)


class Grid:
    """Broad phase: the cells (size CELL) any triangle's box touches, dilated by one cell (CELL >= PROBE, so a point
    within PROBE of a triangle lies in a marked cell)."""

    def __init__(self, co, tris, lo=None, hi=None):
        t = co[tris]
        flo, fhi = t.min(axis=1), t.max(axis=1)
        self.lo = (flo.min(axis=0) if lo is None else lo) - 2 * CELL
        top = (fhi.max(axis=0) if hi is None else hi) + 2 * CELL
        self.shape = np.maximum(1, np.ceil((top - self.lo) / CELL).astype(np.int64) + 1)
        occ = np.zeros(tuple(self.shape), dtype=bool)
        i0 = np.floor((flo - self.lo) / CELL).astype(np.int64)
        i1 = np.floor((fhi - self.lo) / CELL).astype(np.int64)
        span = (i1 - i0).max(axis=1)
        small = span <= 3
        for a in range(4):
            for b in range(4):
                for c in range(4):
                    d = np.array([a, b, c])
                    m = small & np.all(i0 + d <= i1, axis=1)
                    j = i0[m] + d
                    occ[j[:, 0], j[:, 1], j[:, 2]] = True
        for k in np.nonzero(~small)[0]:
            occ[i0[k, 0]:i1[k, 0] + 1, i0[k, 1]:i1[k, 1] + 1, i0[k, 2]:i1[k, 2] + 1] = True
        for ax in range(3):
            o = occ.copy()
            sl = [slice(None)] * 3
            sl2 = [slice(None)] * 3
            sl[ax], sl2[ax] = slice(1, None), slice(None, -1)
            o[tuple(sl)] |= occ[tuple(sl2)]
            o[tuple(sl2)] |= occ[tuple(sl)]
            occ = o
        self.occ = occ
        self.xz = occ.any(axis=1)

    def near_xz(self, pts):
        i = np.floor((pts - self.lo) / CELL).astype(np.int64)
        ok = (i[:, 0] >= 0) & (i[:, 0] < self.shape[0]) & (i[:, 2] >= 0) & (i[:, 2] < self.shape[2])
        out = np.zeros(len(pts), dtype=bool)
        out[ok] = self.xz[i[ok, 0], i[ok, 2]]
        return out

    def cells(self, pts):
        """(x index, z index) of points already known in the x-z footprint."""
        i = np.floor((pts - self.lo) / CELL).astype(np.int64)
        return i[:, 0], i[:, 2], pts[:, 1]

    def near(self, ix, iz, y):
        j = np.floor((y - self.lo[1]) / CELL).astype(np.int64)
        ok = (j >= 0) & (j < self.shape[1])
        out = np.zeros(len(y), dtype=bool)
        out[ok] = self.occ[ix[ok], j[ok], iz[ok]]
        return out


# --------------------------------------------------------------------------------------
# carriers
# --------------------------------------------------------------------------------------

def _import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    ms = [o for o in new if o.type == "MESH"]
    if len(ms) != 1:
        _fail(f"{path}: {len(ms)} meshes (want 1)")
    g = ms[0]
    mw = g.matrix_world.copy()
    co = np.array([tuple(mw @ v.co) for v in g.data.vertices], dtype=np.float64)
    tris = _tris([tuple(p.vertices) for p in g.data.polygons])
    for o in new:
        me = o.data if o.type == "MESH" else None
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
    return co, tris


def _find_fbx(mesh, ref=None):
    if isinstance(ref, dict) and ref.get("lod0"):
        return ref["lod0"]
    if isinstance(ref, str) and ref.startswith("unchanged:"):
        d = os.path.join(REPO, *ref.split(":", 1)[1].strip().split("/"))
        return os.path.join(d, os.path.basename(d) + ".fbx")
    for sub in ("chairs", "cabins"):
        for root, _d, files in os.walk(os.path.join(REPO, "assets", "import", sub)):
            if mesh + ".fbx" in files:
                return os.path.join(root, mesh + ".fbx")
    _fail(f"{mesh}.fbx not found under assets/import")


def load_carriers(path):
    with open(path, encoding="utf-8") as f:
        cat = json.load(f)
    rows = cat.get("carriers", cat)
    out = {}
    for fam, key in CARRIER_OF.items():
        r = rows[key]
        sp = r["split"]
        fb = r.get("fbx") or {}
        out[fam] = {"key": key, "split": sp, "grip_fbx": _find_fbx(sp["grip_mesh"], fb.get("grip")),
                    "body_fbx": _find_fbx(sp["body_mesh"], fb.get("body"))}
    return out


class Carrier:
    """A carrier in the net frame about its rope point at zero pitch / swing (x lateral, -x outboard, y travel, z up):
    the grip, the body hung from the pin, the forbidden part (grip beyond the jaw band + the whole body)."""

    def __init__(self, fam, row, sign, shift_x=0.0):
        sp = row["split"]
        gr, gp, bp = (np.array(sp[k], dtype=np.float64) for k in ("grip_rope_m", "grip_pin_m", "body_pin_m"))

        def to_net(c, off):
            m = c / 100.0 - off
            return np.c_[m[:, 0] + shift_x, sign * m[:, 2], m[:, 1]]
        gco, gtri = _import_fbx(row["grip_fbx"])
        bco, btri = _import_fbx(row["body_fbx"])
        self.fam, self.key, self.row = fam, row["key"], row
        self.grip = (to_net(gco, gr), gtri)
        self.body = (to_net(bco, bp - gp + gr), btri)
        self.pin = to_net(np.array([gp]) * 100.0, gr)[0]
        self.forb_grip = forbidden_part(*self.grip)
        ct17 = bp + gr - gp
        self.info = {"grip_mesh": sp["grip_mesh"], "body_mesh": sp["body_mesh"],
                     "grip_fbx": os.path.relpath(row["grip_fbx"], REPO) if row["grip_fbx"].startswith(REPO)
                     else os.path.basename(row["grip_fbx"]),
                     "body_fbx": os.path.relpath(row["body_fbx"], REPO) if row["body_fbx"].startswith(REPO)
                     else os.path.basename(row["body_fbx"]),
                     "ct17": [round(float(v), 4) for v in ct17], "shift_x": shift_x,
                     "grip_box": [[round(float(v), 3) for v in self.grip[0].min(axis=0)],
                                  [round(float(v), 3) for v in self.grip[0].max(axis=0)]],
                     "body_top_z": round(float(self.body[0][:, 2].max()), 3),
                     "pin": [round(float(v), 4) for v in self.pin]}
        _check_ct17(self, ct17)
        self._cache = {}

    def posed(self, theta, lane_sign, rev):
        """The carrier in the frame of a half pitched theta (the grip at rest in it, the body plumb in the world, so
        turned -theta about the pin), on lane_sign (-1 n: as exported, +1 p: turned 180 deg about z), rev = mirrored
        along travel. Returns the dict of coords / tris / samples / BVHs / broad-phase grids (cached)."""
        k = (round(theta, 4), lane_sign, rev)
        if k in self._cache:
            return self._cache[k]
        sx, sy = (1.0, 1.0) if lane_sign < 0 else (-1.0, -1.0)
        if rev:
            sy = -sy
        L = np.array([sx, sy, 1.0])
        pin = self.pin * L
        gco = self.grip[0] * L
        bco = cs2_skin.rotate_x(self.body[0] * L, -theta, (pin[1], pin[2]))
        fco = self.forb_grip[0] * L
        full_co = np.concatenate([gco, bco])
        full_tri = np.concatenate([self.grip[1], self.body[1] + len(gco)])
        forb_co = np.concatenate([fco, bco]) if len(fco) else bco
        forb_tri = np.concatenate([self.forb_grip[1], self.body[1] + len(fco)]) if len(fco) else self.body[1]
        ngrip = len(self.grip[1])
        nfg = len(self.forb_grip[1])
        out = {"full": _side(full_co, full_tri, lambda i: "grip" if i < ngrip else "body"),
               "forb": _side(forb_co, forb_tri, lambda i: "grip>jaw" if i < nfg else "body"),
               "half_y": float(np.abs(full_co[:, 1]).max())}
        self._cache[k] = out
        return out


def _check_ct17(car, ct17):
    want = float(car.row["split"]["grip_rope_m"][1])       # ct17 = (0, drop, 0): x and z zero
    if abs(ct17[0]) > 1e-4 or abs(ct17[2]) > 1e-4:
        _fail(f"{car.key}: CT-17 body_pin + grip_rope - grip_pin = {ct17.tolist()} (want (0, drop, 0))")
    return want


def _side(co, tris, label_of):
    pts, own = _samples(co, tris)
    return {"co": co, "tris": tris, "bvh": _bvh(co, tris), "pts": pts, "own": own, "grid": Grid(co, tris),
            "label": label_of, "lo": co.min(axis=0), "hi": co.max(axis=0)}


# --------------------------------------------------------------------------------------
# the sweep
# --------------------------------------------------------------------------------------

class Tower:
    """A rigged train (net frame) split for the sweep: per-face bone and part, wheel / other classes."""

    def __init__(self, label, t, spec, part_bone, poses, kind, bones=None):
        table = json.loads(t.data[cs2_asset.PART_TABLE_PROP])
        self.bones = list(bones or BONES)
        try:
            names, fb, vb = cs2_skin.face_bones(t, table, part_bone, self.bones)
        except cs2_skin.SkinError as e:
            _fail(f"{label}: {e}")
        polys = [tuple(p.vertices) for p in t.data.polygons]
        tri_face = np.repeat(np.arange(len(polys)), [len(p) - 2 for p in polys])
        self.label, self.spec, self.kind, self.poses = label, spec, kind, poses
        self.co = cs2_skin.coords(t)
        self.tris = _tris(polys)
        self.part = [names[i] for i in tri_face]
        self.bone = fb[tri_face].astype(np.int64)
        self.vb = vb.astype(np.int64)
        self.zp = LT.lane_y(spec, True)
        self.piv = LT.skin_pivot(spec)
        self.wheel = np.array([is_wheel(n) for n in self.part])
        hubs = sorted({n for n in self.part if "hub" in n or n.startswith(("axlecap", "boss"))})
        if any(is_wheel(n) for n in hubs):
            _fail(f"{label}: a hub / boss / axle cap classed as wheel")
        self.wheel_names = sorted({n for n, w in zip(self.part, self.wheel) if w})
        self.gauge = spec.gauge

    def others(self, own):
        """The moving bones other than half `own`: the other half and (DQ / GD) the Head."""
        return [b for b in range(1, len(self.bones)) if b != own]

    def frame(self, theta_own, own, pose, with_other=True):
        """Coords in the frame of half `own` (bone index) pitched theta_own: own half at rest, Root turned -theta_own,
        every other moving bone (the other half, the Head) turned pose[bone] - theta_own (about the one pivot line; a
        bone missing from `pose` is at 0). Faces of the other moving bones dropped unless with_other."""
        pc = self.co.copy()
        m = self.vb == 0
        if theta_own:
            pc[m] = cs2_skin.rotate_x(self.co[m], -theta_own, self.piv)
        for b in self.others(own):
            a = pose.get(self.bones[b], 0.0) - theta_own
            m = self.vb == b
            if a and m.any():
                pc[m] = cs2_skin.rotate_x(self.co[m], a, self.piv)
        keep = np.ones(len(self.tris), dtype=bool) if with_other else ~np.isin(self.bone, self.others(own))
        return pc, keep


def _class_side(pc, tris, keep, parts, cls):
    idx = np.nonzero(keep & cls)[0]
    if not len(idx):
        return None
    sub = tris[idx]
    pts, own = _samples(pc, sub)
    return {"co": pc, "tris": sub, "idx": idx, "bvh": _bvh(pc, sub), "pts": pts, "own": own, "grid": Grid(pc, sub),
            "label": lambda i, _i=idx: parts[_i[i]]}


def _probe(a_pts, a_label_own, b_bvh, b_label, shift, best):
    """Nearest distances (< PROBE) from points a_pts (+ shift) to tree b; updates best = [d, a part, b part]."""
    for p, o in zip(a_pts, a_label_own):
        hit = b_bvh.find_nearest(mathutils.Vector((p[0] + shift[0], p[1] + shift[1], p[2] + shift[2])), PROBE)
        if hit[0] is not None and hit[3] < best[0]:
            best[0], best[1], best[2] = hit[3], o, b_label(hit[2])


def sweep_stops(tower_side, car_side, lane_x, sgn, zp, ds, stats, where, gate):
    """One tower class vs one carrier side over stops ds (rope point at (lane_x, sgn * d, zp) in the half frame).
    Updates stats[gate] {min, at, overlaps, overlap_at}."""
    if tower_side is None:
        return
    # static per (tower config, carrier config): the x-z footprints
    off0 = np.array([lane_x, 0.0, zp])
    cp = car_side["pts"] + off0
    cm = tower_side["grid"].near_xz(cp)
    c_pts, c_own = cp[cm], car_side["own"][cm]
    cix, ciz, cy = tower_side["grid"].cells(c_pts)
    tp = tower_side["pts"] - off0
    tm = car_side["grid"].near_xz(tp)
    t_pts, t_own = tp[tm], tower_side["own"][tm]
    tix, tiz, ty = car_side["grid"].cells(t_pts)
    if not len(c_pts) and not len(t_pts):
        return
    st = stats[gate]
    for d in ds:
        oy = sgn * d
        best = [9.0, None, None]
        if len(c_pts):
            sel = tower_side["grid"].near(cix, ciz, cy + oy)
            if sel.any():
                _probe(c_pts[sel], [car_side["label"](i) for i in c_own[sel]], tower_side["bvh"],
                       tower_side["label"], (0.0, oy, 0.0), best)
        if len(t_pts):
            sel = car_side["grid"].near(tix, tiz, ty - oy)
            if sel.any():
                hb = [9.0, None, None]
                _probe(t_pts[sel], [tower_side["label"](i) for i in t_own[sel]], car_side["bvh"],
                       car_side["label"], (0.0, -oy, 0.0), hb)
                if hb[0] < best[0]:
                    best = [hb[0], hb[2], hb[1]]
        if best[1] is None:
            continue
        if best[0] < st["min"]:
            st["min"], st["at"] = best[0], dict(where, d=round(float(d), 3), carrier=best[1], tower=best[2])
        if best[0] < CLEAR:
            pk = json.dumps(where["pose"]) if where["pose"] != "any" else f"any {where['half']} {where['theta']}"
            st["by_pose"][pk] = round(min(st["by_pose"].get(pk, 9.0), best[0]), 4)
        if best[0] < OVERLAP_PROBE:
            off = np.array([lane_x, oy, zp])
            cb = _bvh(car_side["co"] + off, car_side["tris"])
            pairs = cb.overlap(tower_side["bvh"])
            if pairs:
                st["overlaps"] += 1
                if st["overlap_at"] is None or len(st["overlap_at"]) < 6:
                    parts = sorted({(car_side["label"](i), tower_side["label"](j)) for i, j in pairs})
                    st["overlap_at"] = (st["overlap_at"] or []) + [dict(where, d=round(float(d), 3), pairs=parts[:4])]


def sweep_train(tw, car, label, report=False):
    """W1 / W2 over every pose, lane, travel direction and stop. Returns the row."""
    t0 = time.perf_counter()
    spec = tw.spec
    stats = {g: {"min": 9.0, "at": None, "overlaps": 0, "overlap_at": None, "by_pose": {}} for g in ("W1", "W2")}
    halves = ((1, "TrainFront", cs2_skin.FRONT_SIGN_NET), (2, "TrainBack", -cs2_skin.FRONT_SIGN_NET))
    nstops = 0
    # the lateral corridor (pose-invariant: the halves turn about x): faces within the carrier's reach of a lane
    W = float(np.abs(car.posed(0.0, -1.0, False)["full"]["co"][:, 0]).max()) + PROBE + CELL
    fx = tw.co[tw.tris][:, :, 0]
    flo, fhi = fx.min(axis=1), fx.max(axis=1)
    lanes = (-spec.gauge / 2.0, spec.gauge / 2.0)
    corr = np.zeros(len(tw.tris), dtype=bool)
    vlat = np.zeros(len(tw.co), dtype=bool)
    for lx in lanes:
        corr |= (flo <= lx + W) & (fhi >= lx - W)
        vlat |= np.abs(tw.co[:, 0] - lx) <= W
    for own, bname, sgn in halves:
        moving = np.isin(tw.vb, tw.others(own))
        thetas = sorted({round(p.get(bname, 0.0), 4) for p in tw.poses})
        for th in thetas:
            mine = [p for p in tw.poses if abs(p.get(bname, 0.0) - th) < 1e-6]
            hy = car.posed(th, -1.0, False)["half_y"]          # |y| extent: the same on every lane / direction
            # far stops: the other half and the head out of reach at every pose with this theta (measured)
            reach = -9.0
            for p in mine:
                pc, _k = tw.frame(th, own, p)
                m = moving & vlat
                if m.any():
                    reach = max(reach, float((sgn * pc[m, 1]).max()))
            d_far = max(0.0, reach + hy + PROBE + STEP)
            pc0, _k = tw.frame(th, own, {})
            u = sgn * pc0[vlat & ~moving, 1]
            d_end = (float(u.max()) if len(u) else 0.0) + hy + PROBE + STEP
            d_end = max(d_end, d_far)                       # the head may reach past the own half's last wheel
            ds_far = np.arange(math.ceil(d_far / STEP) * STEP, d_end + 1e-9, STEP)
            ds_near = np.arange(0.0, min(d_far, d_end) + 1e-9, STEP)
            configs = []
            if len(ds_far):
                pc, keep = tw.frame(th, own, {}, with_other=False)
                configs.append(("any", ds_far, {g: _class_side(pc, tw.tris, keep & corr, tw.part, cls)
                                                for g, cls in (("W2", ~tw.wheel), ("W1", tw.wheel))}))
            for p in mine:
                pc, keep = tw.frame(th, own, p)
                configs.append(({k: round(v, 2) for k, v in p.items()}, ds_near,
                                {g: _class_side(pc, tw.tris, keep & corr, tw.part, cls)
                                 for g, cls in (("W2", ~tw.wheel), ("W1", tw.wheel))}))
            for lane_sign in (-1.0, 1.0):
                lane_x = lane_sign * spec.gauge / 2.0
                for rev in (False, True):
                    cp = car.posed(th, lane_sign, rev)
                    where0 = {"half": bname, "theta": th, "lane": "n" if lane_sign < 0 else "p",
                              "travel": "rev" if rev else "fwd"}
                    for pose, ds, sides in configs:
                        for gate, side in (("W2", "full"), ("W1", "forb")):
                            sweep_stops(sides[gate], cp[side], lane_x, sgn, tw.zp, ds, stats,
                                        dict(where0, pose=pose), gate)
                        nstops += len(ds)
    w1, w2 = stats["W1"], stats["W2"]
    ok = w1["overlaps"] == 0 and w2["overlaps"] == 0 and w2["min"] >= CLEAR
    row = {"train": label, "kind": tw.kind, "carrier": car.key, "poses": len(tw.poses), "stops": nstops,
           "W1_overlaps": w1["overlaps"], "W1_min_m": round(w1["min"], 4) if w1["min"] < 9 else None,
           "W1_at": w1["at"], "W1_overlap_at": w1["overlap_at"],
           "W2_overlaps": w2["overlaps"], "W2_min_m": round(w2["min"], 4) if w2["min"] < 9 else None,
           "W2_at": w2["at"], "W2_overlap_at": w2["overlap_at"], "wheel_parts": len(tw.wheel_names),
           "W1_under_clear_by_pose": w1["by_pose"] or None, "W2_under_clear_by_pose": w2["by_pose"] or None,
           "folds": sorted({round(p.get("TrainFront", 0.0) - p.get("TrainBack", 0.0), 2) for p in tw.poses}),
           "pass": bool(ok), "seconds": round(time.perf_counter() - t0, 1)}
    w2s = f"{w2['min'] * 1000:.1f} mm" if w2["min"] < 9 else f"> {PROBE * 1000:.0f} mm"
    w1s = f"{w1['min'] * 1000:.1f} mm" if w1["min"] < 9 else f"> {PROBE * 1000:.0f} mm"
    print(f"[grip] {'ok  ' if ok else ('CLSH' if report else 'FAIL')} [{label}] {car.key}: {len(tw.poses)} poses, {nstops} stops x 2 lanes "
          f"x 2 travel; W2 non-wheel overlaps {w2['overlaps']}, min {w2s} ({w2['at'] and w2['at']['tower']} / "
          f"{w2['at'] and w2['at']['carrier']}); W1 rim / liner vs grip past the jaw band + body: overlaps "
          f"{w1['overlaps']}, min {w1s} ({w1['at'] and w1['at']['tower']}) [{row['seconds']} s]")
    if not ok and not report:
        print(f"[grip]      W2 at {w2['at']}; under {CLEAR} m by pose {w2['by_pose']}; W1 overlaps by pose "
              f"{w1['by_pose']}; first overlaps W2 {w2['overlap_at'][:2] if w2['overlap_at'] else None} W1 "
              f"{w1['overlap_at'][:2] if w1['overlap_at'] else None}")
    return row


# --------------------------------------------------------------------------------------
# trains
# --------------------------------------------------------------------------------------

def kind_poses(fam, kind, art=False):
    """The poses a kind meets in the game (fold = front - back, + = crest): the G6 grid (uniform pitch to +-theta_max,
    the half-breaks at every mean) on the kind's side of the fold. The planner gives a tower its kind from the two load
    cases (breakover-holddown-plan 2.2): support (members, Break) only where d(h / 1.4) >= +1 deg, so the drawn fold is
    convex, up to the art fold 2 x 20 (field: FG towers by a low ground end reach it); Hold only where d(1.5625 h) <= -1
    deg, concave to the art fold; push-pull (Combo) between, its drawn |d| under 7.3 deg: COMBO_FOLD. DQ / GD (headed,
    DECISIONS 594): the grid to the planner max (SKIN_TYPE_MAX_SLOPE, DQ 42 / GD 50.2) with Head = (front + back) / 2.
    art=True: the whole G6 grid both ways (report only)."""
    headed = fam in HEADED
    tmax = LT.SKIN_TYPE_MAX_SLOPE[fam] if headed else LT.SKIN_THETA_MAX[fam]
    if kind == "support":
        grid = cs2_skin.joint_grid(tmax)
    elif kind == "Combo" and not art:
        grid = cs2_skin.joint_grid(tmax, COMBO_FOLD[fam] / 2.0)
    else:
        grid = cs2_skin.joint_grid(tmax, LT.TWIN_HALF_BREAK[kind])
    if not art:
        side = {"support": 1, "Break": 1, "Hold": -1, "Combo": 0}[kind]
        grid = [(f, b) for f, b in grid if side == 0 or side * (f - b) >= -1e-9]
    if headed:
        return [{"TrainFront": f, "TrainBack": b, LH.HEAD_BONE: round((f + b) / 2.0, 4)} for f, b in grid]
    return [{"TrainFront": f, "TrainBack": b} for f, b in grid]


def head_negative(tw, car, label):
    """The headed negative (always run on a DQ / GD member): the same train with the head held level at uniform
    +-planner max must fail W2 (the deck end meets the carrier body: the clash the Head bone removes)."""
    fam = tw.spec.family
    tm = LT.SKIN_TYPE_MAX_SLOPE[fam]
    keep = tw.poses
    tw.poses = [{"TrainFront": s * tm, "TrainBack": s * tm, LH.HEAD_BONE: 0.0} for s in (1.0, -1.0)]
    try:
        r = sweep_train(tw, car, f"{label} negative: head held level at +-{tm:g} deg", report=True)
    finally:
        tw.poses = keep
    if r["pass"] or not r["W2_overlaps"]:
        _fail(f"negative: {label} with the head held level at +-{tm:g} deg passed W2 ({r})")
    print(f"[grip] ok negative: {label} with the head held level at +-{tm:g} deg fails W2 ({r['W2_overlaps']} stops, "
          f"{r['W2_at'] and r['W2_at']['tower']})")


def headed_train(key, head, atlas):
    """A DQ / GD member or twin built by lift_head (the Head round): (obj, spec, part_bone)."""
    hw = LH.headed_spec(key)
    t, _table, info = LH.build_headed(hw, head, atlas, LH.head_name(hw) + "G")
    return t, hw["twin"], info["skin_part_bone"]


def _drop(t):
    me = t.data
    bpy.data.objects.remove(t, do_unlink=True)
    bpy.data.meshes.remove(me)


def run_member(member, twins, carriers, rows, negative=False):
    """The member and its twins, each swept with its family's carrier. FG: the member held to its shipped LOD0, the twins
    by lift_tower.build_twin. DQ / GD: the headed builds (lift_head.build_headed) for the member and every twin, and
    the head-level negative on every member."""
    fam = LT.VARIANTS[member].family
    head = LT.TWIN_HEAD[fam]
    headed = fam in HEADED
    atlas, arep, ref = LT.twin_atlas(member, head, keep_ref=True)
    spec = LT.skin_spec(LT.VARIANTS[member])
    sign = arep["fbx_lateral_sign"]
    table = json.loads(ref.data[cs2_asset.PART_TABLE_PROP])
    pb = LT.skin_part_bones(ref, table, spec)
    car = Carrier(fam, carriers[fam], sign)
    bones = list(LH.HEAD_BONES) if headed else BONES
    out = []
    if member in WANT:
        if headed:
            t, hspec, hpb = headed_train(member, head, atlas)
            tw = Tower(member, t, hspec, hpb, kind_poses(fam, "support", ART), "support", bones)
        else:
            tw = Tower(member, ref, spec, pb, kind_poses(fam, "support", ART), "support")
        rows.append(dict(sweep_train(tw, car, member), member=member, shipped=SHIPPED[member],
                         headed=LH.head_name(LH.headed_spec(member)) if headed else None,
                         atlas_worst_mm=arep["worst_vertex_mm"], carrier_info=car.info))
        out.append(tw)
        if headed and not ART:
            head_negative(tw, car, member)
        if negative:
            bad = Carrier(fam, carriers[fam], sign, shift_x=0.12)
            tw0 = Tower(member + " (negative)", ref, spec, pb, [{"TrainFront": 0.0, "TrainBack": 0.0}], "support")
            r = sweep_train(tw0, bad, member + " negative: carrier 0.12 m inboard")
            if r["pass"] or not r["W1_overlaps"] or not r["W2_overlaps"]:
                _fail(f"negative: the carrier moved 0.12 m inboard passed on {member} ({r})")
            print(f"[grip] ok negative: the carrier 0.12 m inboard fails W1 ({r['W1_overlaps']} stops) and W2 "
                  f"({r['W2_overlaps']} stops) on {member}")
    for key in twins:
        if key not in WANT:
            continue
        kind = LT.TWINS[key][1]
        if headed and key in LH.HEAD_KEYS:
            t, tspec, tpb = headed_train(key, head, atlas)
        else:
            tw = LT.twin_spec(key)
            name = cs2_asset.validate_asset_name(f"{tw['twin'].name_stem}T")
            t, _ttable, info = LT.build_twin(tw, head, atlas, name)
            tspec, tpb = tw["twin"], info["skin_part_bone"]
        trow = Tower(key, t, tspec, tpb, kind_poses(fam, kind, ART), kind, bones if headed else BONES)
        rows.append(dict(sweep_train(trow, car, key), member=member, carrier_info=car.info,
                         lods_tris0=cs2_asset.tri_count(t)))
        _drop(t)
    return out


def run_entry(carriers, rows):
    """Report only: ETDQ / ETGD (station-entry compression heads) at rest, and their part boxes about the n lane."""
    for key, head in ENTRY.items():
        LT.clear_scene()
        # the entry heads are rigid (no skinned recipe for a compression head): built as shipped (unskinned,
        # ROPE_ON_CROWN off: T27 would lift the ETGD train a crown seat through the station keep-out, C13 top < 7.0),
        # the v2 wheels without the dress; swept with the rope on the crowns (lane_y(spec, True) = axle - seat)
        LT.set_skinned(False)
        LT.set_rope_on_crown(False)
        S.set_wheel_dress(False)
        try:
            spec0 = LT.VARIANTS[key]
            t, table, _src, info = LT.build_tower_v2(spec0, head, finish=False)
        finally:
            S.set_wheel_dress(True)
            LT.set_skinned(True)
        spec = info.get("spec", spec0)
        pb = info.get("skin_part_bone") or {}
        fam = spec.family
        car = Carrier(fam, carriers[fam], 1.0)
        tw = Tower(key, t, spec, pb, [{"TrainFront": 0.0, "TrainBack": 0.0}], spec.kind)
        r = sweep_train(tw, car, key + " (entry, report only)")
        lx, zl = -spec.gauge / 2.0, LT.lane_y(spec, True)
        boxes = []
        for n, v in LT.part_vertices(t, table).items():
            rx, rz = v[:, 0] - lx, v[:, 2] - zl
            if v[:, 0].mean() > 0 or rx.max() < -0.6 or rx.min() > 0.6 or rz.min() > 1.0 or rz.max() < -0.8:
                continue
            boxes.append([n, round(float(rx.min()), 3), round(float(rx.max()), 3), round(float(rz.min()), 3),
                          round(float(rz.max()), 3)])
        r["parts_about_n_lane"] = sorted(boxes, key=lambda b: -b[4])
        r["frame_below_rope"] = sorted({b[0] for b in boxes if not is_wheel(b[0]) and "hub" not in b[0]
                                        and b[4] < 0.0 and b[1] < 0.3})
        print(f"[grip] entry {key}: kind {spec.kind}, lane {zl}; non-wheel parts wholly under the rope within 0.3 m "
              f"inboard: {r['frame_below_rope']}")
        rows.append(dict(r, report_only=True))


WANT = set()
ART = False                         # --art: every train over its whole G6 grid both ways (report only)


def parse(argv):
    ap = argparse.ArgumentParser(prog="test_grip_clearance.py")
    ap.add_argument("--carriers", default=os.path.join(REPO, "catalog", "carriers.json"))
    ap.add_argument("--only", default=None, help="comma list of members / twins (default: all)")
    ap.add_argument("--shard", default=None, help="i/n: every n-th member from the i-th (parallel runs)")
    ap.add_argument("--report", default=None, help="write the rows as JSON here")
    ap.add_argument("--entry", action="store_true", help="also sweep ETDQ / ETGD at rest (report only)")
    ap.add_argument("--art", action="store_true",
                    help="report only: every train over its whole G6 grid, both fold directions (exits 0)")
    return ap.parse_args(argv)


def main():
    t0 = time.perf_counter()
    global ART
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parse(argv)
    ART = args.art
    S.set_wheels("v2")
    S.set_wheel_dress(True)
    LT.set_skinned(True)
    LT.TWIN_ATLAS.update(SHIPPED)
    if not os.path.isfile(LT.KAGELOK_BLEND):
        _fail(f"{LT.KAGELOK_BLEND} absent: the DQ members are built on the Kagelok ingest head (gitignored original)")
    carriers = load_carriers(args.carriers)
    print(f"[grip] carriers from {args.carriers}: " + ", ".join(
        f"{c['key']} {c['split']['grip_mesh']} + {c['split']['body_mesh']}" for c in carriers.values()))
    members = list(SHIPPED)
    twins_of = {m: [k for k, (mm, _kd) in LT.TWINS.items() if mm == m] for m in members}
    every = set(members) | set(LT.TWINS)
    WANT.update(every if not args.only else set(args.only.split(",")))
    if not WANT <= every:
        _fail(f"--only: unknown {sorted(WANT - every)}")
    todo = [m for m in members if m in WANT or any(k in WANT for k in twins_of[m])]
    if args.shard:
        i, n = (int(v) for v in args.shard.split("/"))
        todo = todo[i - 1::n]
    rows = []
    for m in todo:
        run_member(m, twins_of[m], carriers, rows, negative=(m == "FG8"))
    if args.entry:
        run_entry(carriers, rows)
    gated = [r for r in rows if not r.get("report_only")]
    bad = [r for r in gated if not r["pass"]]
    if args.report:
        with open(args.report, "w", encoding="utf-8", newline="\n") as f:
            json.dump({"carriers": args.carriers, "step_m": STEP, "sample_m": SAMPLE, "env_m": ENV, "clear_m": CLEAR,
                       "rows": rows}, f, indent=1, default=float)
    def mm(v):
        return f"{v * 1000:9.1f}" if v is not None else f"{'>' + str(int(PROBE * 1000)):>9s}"
    print("[grip] train                carrier poses  stops  W2 min mm W2 ovl  W1 min mm W1 ovl")
    for r in rows:
        print(f"[grip] {r['train'][:20]:20s} {r['carrier']:7s} {r['poses']:5d} {r['stops']:6d} {mm(r['W2_min_m'])} "
              f"{r['W2_overlaps']:6d} {mm(r['W1_min_m'])} {r['W1_overlaps']:6d}"
              f"{'  (report only)' if r.get('report_only') else ''}")
    if ART:
        print(f"[grip] REPORT (--art, not the gate): {len(bad)} of {len(gated)} trains under the rule somewhere on "
              f"their whole G6 grid: {[r['train'] for r in bad]}")
        return
    if bad:
        _fail(f"{len(bad)} of {len(gated)} trains fail the grip gate: {[r['train'] for r in bad]}")
    print(f"PASS test_grip_clearance: {len(gated)} trains x their kind's poses x 2 lanes x 2 travel directions, W1 (rims / "
          f"liners touch only the jaw band +-{ENV} m) and W2 (every other part 0 overlap, >= {CLEAR} m); DQ / GD headed "
          f"to the planner max (DQ {LT.SKIN_TYPE_MAX_SLOPE['DQ']:g} / GD {LT.SKIN_TYPE_MAX_SLOPE['GD']:g} deg, Head = "
          f"the mean) ({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
