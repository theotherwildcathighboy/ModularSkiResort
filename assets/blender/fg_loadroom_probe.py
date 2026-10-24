"""
fg_loadroom_probe.py - T17 (Ash Flat 2026-10-15 item 16): the fixed-grip BASE station's loading room, measured.

Shot 17.44.53.17: "The entire rear support posts need to be moved back (and thus the weight and such; we can
have a bigger empty platform up top) so that there is more room for the skiers to get into the lift load spot."
lift_terminal.M_BASE_LOADROOM switches the redesign (False = the pre-T17 base byte for byte). This probe:

  --hash      the determinism-style hash (geometry + part table + UVSeed/UVTile + the row block) of the drive station,
              fine-detail on and --no-fine, at the CURRENT flag value (or --flag on|off): the flag-off build must
              reproduce the pre-T17 hashes recorded in the build note.
  --measure   the loading-room numbers (plan, hip band 0.30-1.80 m over the pad = z 0.60-2.10): rear-leg clearance to
              the swept 6M chair, the bay length between the leg faces, the load line -> first obstacle behind, the
              approach route widths (side / behind -> the ramp foot, 2 x the bottleneck clearance, cap 2.0), the free
              area behind the load line; obstacles = the station mesh + the dressing-kit rows' real meshes + the
              deployed vanilla / mod rows (trash bin box, the SkiPisteFloodlightLow pole) that the M1 frozen block
              keeps where they are.  Writes JSON to --json.
  --render ASSET_DIR TAG   the preview set of a shipped-format asset folder (EEVEE, Nishita sky, AgX):
              the screenshot angle, the skier's eye on the
              ramp, a plan (lot outline, the chair sweep, the legs) and 20 m.

    blender --background --factory-startup --python-exit-code 1 --python fg_loadroom_probe.py -- --measure --flag on
Run from assets/blender (cwd) so no stray folder lands in the repo root.
"""
import argparse
import hashlib
import heapq
import json
import math
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cs2_asset  # noqa: E402
import lift_terminal as LT  # noqa: E402

FLOOD_FBX = os.path.join(LT.REPO_M, "assets", "import", "props", "A", "SkiPisteFloodlightLowA",
                         "SkiPisteFloodlightLowA.fbx")
FLOOD_AUTH = (5.60, -3.40)          # the deployed row (3.4, 0.3, 5.6) game -> authoring (gz, -gx)
FLOOR = ("pad", "loaddeck", "loadramp", "carpet_", "stripe_", "label_seat", "cwfoot", "hutbase")
BAND = (0.60, 2.10)                 # hip band over the pad top (0.30): 0.30 .. 1.80 m
STEP = 0.05


def args_():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--hash", action="store_true")
    ap.add_argument("--measure", action="store_true")
    ap.add_argument("--render", nargs=2, default=None, metavar=("ASSET_DIR", "TAG"))
    ap.add_argument("--out", default=os.path.join(LT.REPO_M, "assets", "preview", "t17-fg-loadroom-2026-10-17"))
    ap.add_argument("--views", default=None)
    ap.add_argument("--res", default=None)
    ap.add_argument("--flag", choices=["on", "off", "asis"], default="asis")
    ap.add_argument("--json", default=None)
    ap.add_argument("--contracts", action="store_true")
    return ap.parse_args(argv)


def set_flag(v):
    if v != "asis" and hasattr(LT, "M_BASE_LOADROOM"):
        LT.M_BASE_LOADROOM = (v == "on")
    return getattr(LT, "M_BASE_LOADROOM", None)


# ---------------------------------------------------------------- hash (test_terminal_determinism.mesh_hash) ----

def mesh_hash(me, layers):
    h = hashlib.sha1()
    for v in me.vertices:
        h.update(("%.5f %.5f %.5f;" % tuple(v.co)).encode())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    for name in layers:
        for lp in me.uv_layers[name].data:
            h.update(("%.6f %.6f;" % tuple(lp.uv)).encode())
    h.update(me.get(cs2_asset.PART_TABLE_PROP, "").encode())
    return h.hexdigest()[:12], len(me.vertices), len(me.polygons)


def hash_drive(fine):
    saved = LT.FINE_DETAIL_M
    LT.FINE_DETAIL_M = fine
    try:
        LT.clear_scene()
        parts = LT.build_station_m("drive")
        t, _tb, _src = LT.assemble_m(parts, True, "drive")
        rows = hashlib.sha1(json.dumps(LT.subobject_rows_m("drive"), sort_keys=True).encode()).hexdigest()[:12]
        return list(mesh_hash(t.data, sorted(l.name for l in t.data.uv_layers))) + [rows]
    finally:
        LT.FINE_DETAIL_M = saved


# ---------------------------------------------------------------- measure ----------------------------------------

def flood_radius():
    """Plan radius of the SkiPisteFloodlightLow below 2.2 m (the pole + its base) from the shipped FBX."""
    import cs2_validate
    objs = cs2_validate.import_fbx_temp(FLOOD_FBX)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        M = o.matrix_world
        V = np.array([(M @ v.co)[:] for v in o.data.vertices]) / 100.0
    finally:
        cs2_validate.remove_objects(objs)
    # raw FBX: cm, y up (lift_terminal.load_chair_m's reading); the pole stands on y 0
    low = V[V[:, 1] < 2.2]
    r = float(np.hypot(low[:, 0], low[:, 2]).max())
    return r, float(V[:, 1].max()), float(np.hypot(V[:, 0], V[:, 2]).max())


def sweep_cloud(spec):
    """The 6M chair BODY swept along the code path (LOD0, both lanes, both chain ends, the arc), plan + z."""
    ch = LT.load_chair_m(0)
    bt = ch["tris"][ch["body"]]
    loc = ch["co"][np.unique(bt)]
    pts = []
    for _lab, q, d, _f in LT.sweep_poses_m(spec, 0.10, 2.0):
        up = np.array([0.0, 0.0, 1.0])
        dd = d / np.linalg.norm(d)
        R = np.stack([dd, np.cross(up, dd), up], axis=1)
        pts.append(loc @ R.T + (np.asarray(q) - np.array([0.0, 0.0, LT.FG_HANG])))
    return np.concatenate(pts)


def obstacle_points(co, tris, names, extra):
    """Plan points of everything in the hip band (the M18 band footprint, rasterized every STEP / 2)."""
    keep = [i for i, n in enumerate(names) if not n.startswith(FLOOR)]
    A, B, C, who = LT._band_footprints_m(co, tris[keep], [names[i] for i in keep], BAND[0], BAND[1],
                                         (-30.0, 30.0, -30.0, 30.0))
    s = STEP / 2.0
    pts, own = [], []
    for a, b, c, w in zip(A, B, C, who):
        L = max(np.linalg.norm(b - a), np.linalg.norm(c - b), np.linalg.norm(a - c))
        n = max(1, int(math.ceil(L / s)))
        u, v = np.meshgrid(np.linspace(0, 1, n + 1), np.linspace(0, 1, n + 1))
        m = (u + v) <= 1.0 + 1e-9
        P = a[None, :] + u[m][:, None] * (b - a)[None, :] + v[m][:, None] * (c - a)[None, :]
        pts.append(P)
        own += [w] * len(P)
    for nm, P in extra:
        pts.append(P)
        own += [nm] * len(P)
    return np.concatenate(pts), own


def circle_pts(c, r):
    out = []
    for rr in np.arange(0.0, r + 1e-9, STEP / 2.0):
        n = max(1, int(2 * math.pi * rr / (STEP / 2.0)))
        a = np.linspace(0, 2 * math.pi, n, endpoint=False)
        out.append(np.stack([c[0] + rr * np.cos(a), c[1] + rr * np.sin(a)], axis=1))
    return np.concatenate(out)


def box_pts(lo, hi):
    xs = np.arange(lo[0], hi[0] + 1e-9, STEP / 2.0)
    ys = np.arange(lo[1], hi[1] + 1e-9, STEP / 2.0)
    X, Y = np.meshgrid(xs, ys)
    return np.stack([X.ravel(), Y.ravel()], axis=1)


def widest(clr, ok, src_mask, tgt):
    """Max-bottleneck path (8-connected) from any source cell to tgt: returns the bottleneck clearance and path."""
    H, W = clr.shape
    best = np.full(clr.shape, -1.0)
    prev = {}
    pq = []
    for i, j in zip(*np.nonzero(src_mask & ok)):
        best[i, j] = clr[i, j]
        heapq.heappush(pq, (-clr[i, j], i, j))
    while pq:
        nb, i, j = heapq.heappop(pq)
        nb = -nb
        if nb < best[i, j]:
            continue
        if (i, j) == tgt:
            break
        for di in (-1, 0, 1):
            for dj in (-1, 0, 1):
                a, c = i + di, j + dj
                if (di or dj) and 0 <= a < H and 0 <= c < W and ok[a, c]:
                    v = min(nb, clr[a, c])
                    if v > best[a, c]:
                        best[a, c] = v
                        prev[(a, c)] = (i, j)
                        heapq.heappush(pq, (-v, a, c))
    path, k = [], tgt
    while k in prev:
        path.append(k)
        k = prev[k]
    path.append(k)
    return float(best[tgt]), path[::-1]


def measure():
    import cs2_kitrows as KR
    from mathutils import kdtree
    B, spec = LT.m_base(), LT.M_SPECS["drive"]
    LT.clear_scene()
    parts = LT.build_station_m("drive")
    t, table, _src = LT.assemble_m(parts, True, "drive")
    co, tris, names, _ = LT.m_arrays(t)
    tri_lod0 = cs2_asset.tri_count(t)
    kit = LT.fd_kit_rows_m("drive")
    aco, atris, anames = KR.augment(co, tris, names, kit, LT.m_host) if kit else (co, tris, names)
    fr, fz, freach = flood_radius()
    lo, hi = LT.m_subobj_box("TrashbinRandom01", (5.60, 2.70, B["pad_h"]), None)
    extra = [("vanilla TrashbinRandom01 (frozen row)", box_pts(lo[:2], hi[:2])),
             ("mod SkiPisteFloodlightLow pole (frozen row)", circle_pts(FLOOD_AUTH, fr))]
    P, own = obstacle_points(aco, atris, anames, extra)
    kd = kdtree.KDTree(len(P))
    for k, p in enumerate(P):
        kd.insert((p[0], p[1], 0.0), k)
    kd.balance()
    hx, hy = LT.M_LOT_HALF[0] - LT.M_LOT_MARGIN, LT.M_LOT_HALF[1] - LT.M_LOT_MARGIN
    xs = np.arange(-hx, hx + 1e-9, STEP)
    ys = np.arange(-hy, hy + 1e-9, STEP)
    clr = np.zeros((len(xs), len(ys)))
    for i, x in enumerate(xs):
        for j, y in enumerate(ys):
            _c, _k, d = kd.find((x, y, 0.0))
            clr[i, j] = min(d, 3.0)
    # the swept chair (plan) and the legs
    cloud = sweep_cloud(spec)
    cxy = cloud[:, :2]
    legs = {}
    back_x = B["back_x"]
    for tag, lx in (("front", B["front_x"]), ("rear", back_x)):
        for s in (-1.0, 1.0):
            c = np.array([lx, s * B["leg_y"]])
            legs[f"{tag}_{'p' if s > 0 else 'n'}"] = round(float(np.linalg.norm(cxy - c, axis=1).min() - B["leg_r"]), 3)
    wx = spec.wheel[0]
    # load line -> the first hip-band obstacle behind it (+x), per strip
    ahead = {}
    for lab, (y0, y1) in (("deck/ramp lane y 0.60-3.00", (0.60, 3.00)), ("pad edge strip y 3.00-3.85", (3.00, 3.85))):
        m = (P[:, 0] > wx + 0.05) & (P[:, 1] >= y0) & (P[:, 1] <= y1)
        k = int(np.argmin(np.where(m, P[:, 0], 1e9)))
        ahead[lab] = {"x": round(float(P[k, 0]), 3), "dist_m": round(float(P[k, 0] - wx), 3), "part": own[k]}
    # ramp foot -> the first obstacle behind it in the ramp's lane
    rf = B["ramp_x"]
    m = (P[:, 0] > rf) & (P[:, 1] >= B["deck"][2]) & (P[:, 1] <= B["deck"][3])
    k = int(np.argmin(np.where(m, P[:, 0], 1e9)))
    runout = {"ramp_foot_x": rf, "first_x": round(float(P[k, 0]), 3), "dist_m": round(float(P[k, 0] - rf), 3),
              "part": own[k]}
    # approach routes to the ramp foot (0.40 m past the toe, on the load lane)
    ok = np.ones_like(clr, dtype=bool)
    ti = int(round((rf + 0.40 + hx) / STEP))
    tj = int(round((spec.wheel[1] + LT.FG_LANE + hy) / STEP))
    X, Y = np.meshgrid(xs, ys, indexing="ij")
    routes = {}
    for lab, src in (("side (+y lot edge)", Y >= hy - 0.01), ("behind (+x lot edge)", X >= hx - 0.01)):
        b, path = widest(clr, ok, src, (ti, tj))
        cells = [(xs[i], ys[j], clr[i, j]) for i, j in path]
        pinch = min(cells, key=lambda c: c[2]) if cells else None
        pk = None
        if pinch:
            _c, kk, _d = kd.find((pinch[0], pinch[1], 0.0))
            pk = own[kk]
        routes[lab] = {"width_m": round(2.0 * b, 3), "cap_m": 6.0,
                       "pinch_at": [round(pinch[0], 2), round(pinch[1], 2)] if pinch else None, "pinch_part": pk}
    # free area behind the load line (fixed box so before / after compare), clearance >= 0.35 (a 0.70 m person)
    box = (X >= wx) & (X <= 7.0) & (Y >= 0.60) & (Y <= 3.85)
    free = float(((clr >= 0.35) & box).sum() * STEP * STEP)
    # the side opening: the longest x run along y 3.30-3.85 (the pad's +y edge strip) with clearance >= 0.35
    runs = {}
    for yv in (3.40, 3.60):
        j = int(round((yv + hy) / STEP))
        col = (clr[:, j] >= 0.35) & (xs >= wx) & (xs <= B["pad"][1])
        best_, cur, start, bs = 0, 0, None, None
        for i, v in enumerate(col):
            if v:
                if cur == 0:
                    start = i
                cur += 1
                if cur > best_:
                    best_, bs = cur, start
            else:
                cur = 0
        runs[f"y {yv}"] = {"len_m": round(best_ * STEP, 2),
                           "x": [round(float(xs[bs]), 2), round(float(xs[bs + best_ - 1]), 2)] if bs is not None else None}
    sweep = {"x_max": round(float(cloud[:, 0].max()), 3), "y_abs_max": round(float(np.abs(cloud[:, 1]).max()), 3)}
    out = {"flag": getattr(LT, "M_BASE_LOADROOM", None), "lod0_tris_assembled": tri_lod0,
           "M_BASE": {k: v for k, v in B.items()}, "leg_to_sweep_m": legs,
           "bay_length_leg_faces_m": round((back_x - B["leg_r"]) - (B["front_x"] + B["leg_r"]), 3),
           "loadline_to_first_obstacle_behind": ahead, "ramp_runout": runout, "approach_routes": routes,
           "free_area_m2_x0.2-7.0_y0.6-3.85": round(free, 2), "side_opening_runs": runs, "chair_sweep": sweep,
           "floodlight": {"pole_r_below_2.2m": round(fr, 3), "top_z": round(fz, 2), "reach_r": round(freach, 2)},
           "band_z": BAND, "grid_step": STEP}
    return out, (xs, ys, clr, cloud, P)


# ---------------------------------------------------------------- render -----------------------------------------

def render(asset_dir, tag, out_dir, views=("1_shot_angle", "2a_ramp_eye", "2b_approach_eye", "3_plan", "4_20m"),
           res=(1600, 1000)):
    """The shipped-format asset in `asset_dir` (FBX + _Win + the four maps, as main_m writes it) back in the
    authoring frame, with what stands beside it in game: the dressing-kit rows (real meshes, flat colors), the frozen
    SkiPisteFloodlightLow row (its shipped FBX + maps), the vanilla trash bin (a box of its union bounds: stand-in),
    two 6M chairs (shipped LOD0 + maps: at the load tangent and at the back of the arc) and 1.8 m skier stand-ins.
    The cs2_preview rig: EEVEE, Nishita sky (noon), the 4.0 sun, AgX, 60 m ground."""
    import mathutils
    from mathutils import Vector
    import cs2_kitrows as KR
    import cs2_preview as CP
    B, spec = LT.m_base(), LT.M_SPECS["drive"]
    asset_dir = os.path.abspath(asset_dir)
    os.makedirs(out_dir, exist_ok=True)
    LT.clear_scene()
    name = os.path.basename(os.path.normpath(asset_dir))
    def raw_of(o):
        """The FBX's own frame in meters (x, y up, z): cs2_preview.import_generation un-bakes it only when its
        y-up guess fires (it does not on this station), so read the up axis from the data (min z or min y = 0)."""
        V = np.array([v.co[:] for v in o.data.vertices])
        return V if abs(V[:, 1].min()) <= abs(V[:, 2].min()) else np.stack([V[:, 0], V[:, 2], -V[:, 1]], axis=1)

    def set_co(o, A):
        for v, p in zip(o.data.vertices, A):
            v.co = tuple(float(c) for c in p)
        o.data.update()

    def to_auth(o):
        """export bake: raw = (oriented x, z, -y), oriented = (authoring y, -x, z) -> authoring = (raw z, raw x, raw y)"""
        R = raw_of(o)
        set_co(o, np.stack([R[:, 2], R[:, 0], R[:, 1]], axis=1))
        return o

    obj = to_auth(CP.import_generation(os.path.join(asset_dir, f"{name}.fbx")))
    obj.name = "station"
    rig = CP._Rig(obj, 48)
    sc = rig.scene
    sc.render.resolution_x, sc.render.resolution_y = res
    CP._assign(obj, CP._pbr_material(rig, asset_dir, name, LT.M_TINT))
    win_p = os.path.join(asset_dir, f"{name}_Win.fbx")
    mats = {}

    def flat(nm, rgb, rough=0.6, metal=0.0, alpha=1.0, emit=0.0):
        if nm in mats:
            return mats[nm]
        m = rig.track_data(bpy.data.materials, bpy.data.materials.new(nm))
        m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (*rgb, 1.0)
        b.inputs["Roughness"].default_value = rough
        b.inputs["Metallic"].default_value = metal
        if emit:
            b.inputs["Emission Color"].default_value = (*rgb, 1.0)
            b.inputs["Emission Strength"].default_value = emit
        if alpha < 1.0:
            b.inputs["Alpha"].default_value = alpha
            if hasattr(m, "surface_render_method"):
                m.surface_render_method = "BLENDED"
        mats[nm] = m
        return m

    def mesh_obj(nm, V, T, mat):
        me = rig.track_data(bpy.data.meshes, bpy.data.meshes.new(nm))
        me.from_pydata([tuple(float(c) for c in v) for v in V], [], [tuple(int(c) for c in t) for t in T])
        me.update()
        o = rig.track(bpy.data.objects.new(nm, me))
        sc.collection.objects.link(o)
        o.data.materials.append(mat)
        return o

    if os.path.exists(win_p):
        w = rig.track(to_auth(CP.import_generation(win_p)))
        CP._assign(w, flat("pv_glass", (0.10, 0.13, 0.16), rough=0.05, metal=0.3))
    # dressing-kit rows (the real kit meshes at their rows)
    for i, r in enumerate(LT.fd_kit_rows_m("drive")):
        A, kt, _km = KR.host_mesh(r, LT.m_host)
        pf = r["prefab"]
        col = (0.80, 0.62, 0.08) if "Rack" in pf else (0.75, 0.10, 0.08) if ("Ext" in pf or "EStop" in pf) \
            else (0.35, 0.35, 0.36)
        mesh_obj(f"kit_{i}", A, kt, flat(f"pv_kit{i}", col))
    # the frozen floodlight row (game (3.4, 0.3, 5.6), yaw 190) with its own maps
    fo = rig.track(CP.import_generation(FLOOD_FBX))
    pre = raw_of(fo)                                            # prefab (x, y up, z), meters
    G = KR.place_game(pre, {"rotation": [0.0, 0.9961947, 0.0, -0.0871557], "position": [3.4, 0.3, 5.6]})
    set_co(fo, LT.m_host(G))
    CP._assign(fo, CP._pbr_material(rig, os.path.dirname(FLOOD_FBX), "SkiPisteFloodlightLowA", None,
                                    mat_name="pv_flood"))
    # the frozen vanilla trash bin: a box of its union bounds (stand-in)
    lo, hi = LT.m_subobj_box("TrashbinRandom01", (5.60, 2.70, B["pad_h"]), None)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=tuple(float(c) for c in (lo + hi) / 2.0))
    tb = rig.track(bpy.context.active_object)
    tb.scale = tuple(float(c) for c in hi - lo)
    tb.data.materials.append(flat("pv_bin", (0.12, 0.25, 0.14)))
    # two chairs: the shipped 6M LOD0 placed by lift_terminal.place_chair (geometry from load_chair_m), flat steel
    ch = LT.load_chair_m(0)
    for nm, q, d in (("chair_load", (spec.wheel[0], LT.FG_LANE, spec.wheel[2]), (-1.0, 0.0, 0.0)),
                     ("chair_arc", (spec.wheel[0] + LT.FG_LANE, 0.0, spec.wheel[2]), (0.0, 1.0, 0.0))):
        mesh_obj(nm, LT.place_chair(ch, q, d), ch["tris"], flat("pv_chair", (0.62, 0.63, 0.66), rough=0.35,
                                                                 metal=0.7))
    # skier stand-ins (1.8 m): three on the load line, two on the approach behind the ramp
    ph, dz = B["pad_h"], B["deck"][4]
    sk = [(spec.wheel[0] + 0.35, y, dz + 0.03) for y in (1.20, 1.80, 2.40)]
    sk += [(B["ramp_x"] + 0.8, 1.6, ph), (B["ramp_x"] + 1.8, 2.5, ph)]
    skiers = []
    for k, (x, y, z) in enumerate(sk):
        bpy.ops.mesh.primitive_cylinder_add(radius=0.22, depth=1.8, location=(x, y, z + 0.9), vertices=16)
        o = rig.track(bpy.context.active_object)
        skiers.append(o)
        o.name = f"skier_{k}"
        o.data.materials.append(flat("pv_skier", (0.80, 0.12, 0.08)))
    # plan overlays: the lot (red), the 8 m cell lines (pink), the swept chair body (blue), above everything
    hx, hy = LT.M_LOT_HALF
    zt = 9.6
    ov = []
    quad = [(0, 1, 2), (0, 2, 3)]

    def strip(nm, x0, x1, y0, y1, mat):
        ov.append(mesh_obj(nm, [(x0, y0, zt), (x1, y0, zt), (x1, y1, zt), (x0, y1, zt)], quad, mat))
    red, pink = flat("pv_lot", (0.9, 0.05, 0.05), emit=1.0), flat("pv_cell", (0.95, 0.55, 0.6), emit=0.6)
    strip("lot_s", -hx, hx, -hy - 0.08, -hy + 0.08, red)
    strip("lot_n", -hx, hx, hy - 0.08, hy + 0.08, red)
    strip("lot_w", -hx - 0.08, -hx + 0.08, -hy, hy, red)
    strip("lot_e", hx - 0.08, hx + 0.08, -hy, hy, red)
    for x in (-4.0, 4.0):
        strip(f"cell_x{x:+.0f}", x - 0.03, x + 0.03, -hy, hy, pink)
    strip("cell_y0", -hx, hx, -0.03, 0.03, pink)
    cloud = sweep_cloud(spec)
    pts2 = np.unique(np.round(cloud[:, :2] / 0.10) * 0.10, axis=0)
    V, F = [], []
    for x, y in pts2:
        b = len(V)
        V += [(x - 0.05, y - 0.05, zt - 0.05), (x + 0.05, y - 0.05, zt - 0.05), (x + 0.05, y + 0.05, zt - 0.05),
              (x - 0.05, y + 0.05, zt - 0.05)]
        F += [(b, b + 1, b + 2), (b, b + 2, b + 3)]
    ov.append(mesh_obj("sweep", V, F, flat("pv_sweep", (0.10, 0.35, 0.95), alpha=0.35, emit=0.4)))
    CP._make_world(rig, CP.NOON["elevation_deg"], CP.NOON["sun_energy"])
    CP._make_ground(rig)
    cam = CP._make_camera(rig)
    cam.data.sensor_fit = "HORIZONTAL"
    cam.data.sensor_width = 36.0
    shots = {
        # (1) the 17.44.53.17 shot angle: low, from the hut (+y) side, the counterweight on the left
        "1_shot_angle": dict(loc=(4.6, 15.5, 4.3), target=(1.2, 0.0, 2.7), lens=22.0),
        # (2a) a skier standing on the load ramp (mid-ramp, eye 1.6 m over it) looking at the load line
        "2a_ramp_eye": dict(loc=(2.30, 2.05, 0.55 + 1.60), target=(spec.wheel[0], 1.80, 1.30), lens=16.0,
                            skiers=False),
        # (2b) the approach: a skier 5 m behind the load line on the load lane, eye 1.6 m, looking at the line
        "2b_approach_eye": dict(loc=(5.20, 2.30, B["pad_h"] + 1.60), target=(spec.wheel[0], 1.80, 1.30), lens=16.0,
                                skiers=False),
        # (3) plan: ortho top-down, the lot (red, the 8 m cells pink) and the swept 6M chair body (blue)
        "3_plan": dict(loc=(0.0, 0.0, 40.0), target=None, ortho=26.5, overlay=True),
        # (4) 20 m, 3/4 from the hut side and the back
        "4_20m": dict(loc=tuple(Vector((1.5, 0.0, 2.5)) + Vector((0.55, 0.85, 0.42)).normalized() * 20.0),
                      target=(1.5, 0.0, 2.5), lens=30.0),
    }
    written = []
    for v in views:
        s = shots[v]
        cam.location = s["loc"]
        if s["target"] is None:
            cam.rotation_euler = (0.0, 0.0, 0.0)
        else:
            cam.rotation_euler = (Vector(s["target"]) - Vector(s["loc"])).to_track_quat("-Z", "Y").to_euler()
        if s.get("ortho"):
            cam.data.type = "ORTHO"
            cam.data.ortho_scale = s["ortho"]
        else:
            cam.data.type = "PERSP"
            cam.data.lens = s["lens"]
        for o in ov:
            o.hide_render = not s.get("overlay")
        for o in skiers:
            o.hide_render = not s.get("skiers", True)
        written.append(CP._render(rig, os.path.join(out_dir, f"{tag}_{v}.png")))
        print(f"[loadroom] wrote {written[-1]}")
    return written


def main():
    a = args_()
    flag = set_flag(a.flag)
    res = {"flag": flag}
    if a.hash:
        res["hash_fine"] = hash_drive(True)
        res["hash_nofine"] = hash_drive(False)
        print(f"[loadroom] hash flag {flag}: fine {res['hash_fine']} / no-fine {res['hash_nofine']}")
    if a.contracts:
        LT.clear_scene()
        t, table, _s = LT.assemble_m(LT.build_station_m("drive"), True, "drive")
        rep = LT.contracts_m(t, table, "drive", sweep=True)
        co, tris, names, _ = LT.m_arrays(t)
        rep["M10"] = LT.windows_m(co, tris, names, "drive")
        res["contracts"] = {k: (v if k != "M1" else "ok") for k, v in rep.items()}
        res["lod0_assembled"] = cs2_asset.tri_count(t)
        print(f"[loadroom] contracts ok: {sorted(rep)}; LOD0 assembled {res['lod0_assembled']}")
    if a.measure:
        m, _grid = measure()
        res["measure"] = m
        print("[loadroom] " + json.dumps(m, default=str)[:4000])
    if a.render:
        kw = {"views": tuple(a.views.split(","))} if a.views else {}
        if a.res:
            kw["res"] = tuple(int(v) for v in a.res.split("x"))
        render(a.render[0], a.render[1], a.out, **kw)
    if a.json:
        os.makedirs(os.path.dirname(os.path.abspath(a.json)), exist_ok=True)
        with open(a.json, "w", encoding="utf-8") as fh:
            json.dump(res, fh, indent=1, default=str)
    print("PASS fg_loadroom_probe")


main()
