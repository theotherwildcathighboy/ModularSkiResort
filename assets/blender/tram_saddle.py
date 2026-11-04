"""
tram_saddle.py - the tram saddle round (DECISIONS 597; docs/research/art/tram-saddle-2026-11/build.md).

The tram towers' saddles are already articulated (DECISIONS 573 option C, letter C: two saddle halves hinged on the track
rope's anchor line, two haul-roller halves on the haul anchor line 0.40 below). What the planner asks of them is more
than C clears: on the TS-1 sweep (tests/LiftBatch TramSaddleTests: 242 tram line towers on 132 planned lines) only 108
towers lie inside C's limits (each half <= 30 deg, half-break <= 20 deg); the rest clamp and the rope cuts through the
girder (up to ~1 m at a half's outer end). Letter D keeps C's topology and bones and widens the joint so each half
follows its rope to SADDLE_MAX (60 deg: the static head's rope reach, first hit 61 deg on the lugs) with a half-break to
HALF_BREAK (30 deg): 202 of 242 towers. The changes (tram_tower.SKIN_TRAM hooks, the defaults stay C):
- notch_deg 22 -> 32: the halves' V-notch faces clear a 2 x 30 deg relative fold;
- joint_d 0.45: the halves' depth at the V-notch (a haunch to the 0.70 box at xc): the open V stays as wide as C's;
- bar_gap 0.11 -> 0.20 and roller_out 0.10: the haul bars' and inner rollers' corners clear the 30 deg break;
- arm_dz1 -1.55 -> -2.40: the central crossbeam under the saddle drops below the halves' sweep at 60 deg (the rocker
  legs lengthen with it).
Texture: 0 MB. The topology is C's, so every loop takes C's shipped UV: C is rebuilt (the shipping code path, no
finish), its shipped LOD0 FBX is matched vertex for vertex (<= 0.2 mm) and the per-loop UVs land on D's identical loops
(raises on any topology difference). Previews stage C's maps under D's name.

Gates (raise): C rebuild = shipped C (vertex match), D topology = C topology, tram_tower contracts (authoring + net), G6
over cs2_skin.joint_grid(SADDLE_MAX, HALF_BREAK) on LOD0 (no overlap, clearance >= 5 mm, every rope clear and seated on
every crown), G6 at the planner's poses (TS-1, clamped as LiftAlignMath.HalfPitches clamps them), the rope reach (clear
to SADDLE_MAX), the negative (C's geometry at D's grid must fail), C9 budgets with the 5 % cap rule, the code constants
(LiftAlignMath.TramSaddlePitchLimitDeg / TramHalfBreakMaxDeg) = SADDLE_MAX / HALF_BREAK.

Usage (headless; scratch folders only, nothing exported):
  blender --background --factory-startup --python-exit-code 1 --python tram_saddle.py -- --heights 30
      --out <scratch> [--preview <dir>] [--before-after] [--negative] [--report <json>]
  blender ... --python tram_saddle.py -- --sheets <dir>        (the sheets from the raw frames of earlier runs)
"""
import argparse
import json
import math
import os
import re
import sys
import time

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402
import cs2_sheaves as S  # noqa: E402
import cs2_skin  # noqa: E402
import tram_tower as TT  # noqa: E402

_REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
LETTER, SOURCE = "D", "C"
HEIGHTS = (30, 45, 60)
TYPE_MAX = 68.2                 # deg: the planner's tram max slope (atan 2.5)
SADDLE_MAX = 60.0               # deg: each half's clamp = LiftAlignMath.TramSaddlePitchLimitDeg (the rope reach)
HALF_BREAK = 30.0               # deg: the half-break clamp = LiftAlignMath.TramHalfBreakMaxDeg
C_LIMITS = (TT.SKIN_TRAM["theta_max"], TT.SKIN_TRAM["half_break"])     # the shipped C: 30 / 20
C_DESIGN = dict(TT.SKIN_TRAM)
D_DESIGN = dict(theta_max=SADDLE_MAX, half_break=HALF_BREAK, notch_deg=32.0, joint_d=0.45, bar_gap=0.20,
                roller_out=0.10, arm_dz1=-2.40)
# The tower-lean round: the tram mast leans up to LEAN_MAX and the planner caps the rope pitch (the mean of the two
# chords) at PITCH_CAP, so the saddle's mean stays within MEAN_REL_MAX = PITCH_CAP - LEAN_MAX (30) of the mast and each half
# within that +- the half-break (30): one half reaches 60 relative to the mast at the cap corner (mean 55, half-break 30,
# lean 25), so D's whole range stays needed (a letter E with C's crossbeam failed at those poses: tower-lean build.md).
PITCH_CAP, LEAN_MAX = 55.0, 25.0           # deg: LiftAlignMath.PitchCapDeg("tram"), the tram mast's max lean
MEAN_REL_MAX = PITCH_CAP - LEAN_MAX        # deg: the saddle's mean relative to the mast (the code must clamp it there)
CAP_RULE = 0.05                 # a LOD0 / LOD2 overage up to 5 % raises the cap in-round (recorded); more is an owner call
POSES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tests", "data", "tram_saddle_poses.json")
ALIGN_CS = os.path.join(_REPO, "mod", "SkiResortMod", "Systems", "LiftAlignMath.cs")
MAPS = ("BaseColor", "Normal", "MaskMap", "ControlMask")


class SaddleError(RuntimeError):
    pass


def _ok(cond, label, detail=""):
    if not cond:
        raise SaddleError(f"{label} {detail}".strip())
    print(f"[saddle] ok {label} {detail}".rstrip())


def use(design):
    """tram_tower.SKIN_TRAM = the shipped C, or C + D_DESIGN."""
    TT.SKIN_TRAM.clear()
    TT.SKIN_TRAM.update(C_DESIGN)
    if design == LETTER:
        TT.SKIN_TRAM.update(D_DESIGN)


def lean_grid():
    """[(front, back)] Blender deg relative to a leaned mast: the mean over +-MEAN_REL_MAX, the half-break over
    +-HALF_BREAK (lift_head.rel_grid), each half clamped to SADDLE_MAX."""
    import lift_head
    return lift_head.rel_grid(MEAN_REL_MAX, HALF_BREAK, SADDLE_MAX)


def lean_rows(rows, rule="min"):
    """TS-1 rows (code deg) relative to a leaned mast: towers past PITCH_CAP drop (the planner repairs them now). rule
    "min": the least lean that brings the mean within MEAN_REL_MAX (max(0, |mean| - 30): the worst case for the art);
    "max": the mast takes the mean up to LEAN_MAX."""
    out = []
    for lab, f, b in rows:
        m = 0.5 * (f + b)
        if abs(m) > PITCH_CAP + 1e-9:
            continue
        lean = min(LEAN_MAX, max(0.0, abs(m) - MEAN_REL_MAX) if rule == "min" else abs(m))
        lean = math.copysign(lean, m)
        out.append((lab, f - lean, b - lean))
    return out


def code_limits():
    """(TramSaddlePitchLimitDeg, TramHalfBreakMaxDeg) parsed from LiftAlignMath.cs (the code clamps the halves there)."""
    with open(ALIGN_CS, encoding="utf-8") as f:
        src = f.read()
    got = []
    for k in ("TramSaddlePitchLimitDeg", "TramHalfBreakMaxDeg"):
        m = re.search(rf"const float {k} = ([0-9.]+)f;", src)
        if not m:
            raise SaddleError(f"LiftAlignMath.cs: no const float {k}")
        got.append(float(m.group(1)))
    return tuple(got)


def half_pitches(front, back, pmax, hb):
    """LiftAlignMath.HalfPitches (deg): each half to +-pmax, the half-break to hb about the mean, each half again."""
    f, b = max(-pmax, min(pmax, front)), max(-pmax, min(pmax, back))
    mid, half = 0.5 * (f + b), 0.5 * (b - f)
    if abs(half) > hb:
        half = math.copysign(hb, half)
        f, b = mid - half, mid + half
    return max(-pmax, min(pmax, f)), max(-pmax, min(pmax, b))


def planner_poses():
    """[(label, front, back)] code degrees (nose up along +z) from the TS-1 sweep file."""
    with open(POSES, encoding="utf-8") as f:
        return [tuple(r) for r in json.load(f)["rows"]]


def rope_offsets(rows, pmax, hb, half_len):
    """Per tower the larger of |rope - half| over both halves (deg) and the rope's offset at the half's outer end
    (half_len x sin), at limits pmax / hb. Returns (inside count, worst row, worst offset m)."""
    inside, worst = 0, (None, 0.0, 0.0)
    for lab, f, b in rows:
        pf, pb = half_pitches(f, b, pmax, hb)
        d = max(abs(f - pf), abs(b - pb))
        if d <= 0.5:                                     # LiftAlignMath.TrainWarnDeg
            inside += 1
        off = half_len * math.sin(math.radians(d))
        if off > worst[2]:
            worst = (lab, d, off)
    return inside, worst


def blender_pairs(rows, pmax, hb):
    """The planner poses clamped as the code clamps them, in the Blender sign (theta = -code), deduplicated at 0.1."""
    out = []
    for _lab, f, b in rows:
        pf, pb = half_pitches(f, b, pmax, hb)
        p = (round(-pf, 1), round(-pb, 1))
        if p not in out:
            out.append(p)
    return out


def c_args(H):
    """The shipped C's build args (its manifest) as a namespace for tram_tower.build_variant."""
    with open(os.path.join(c_folder(H), f"SkiTramTower{H}{SOURCE}.manifest.json"), encoding="utf-8") as f:
        return argparse.Namespace(**json.load(f)["args"])


def c_folder(H):
    return os.path.join(_REPO, "assets", "import", "tramtowers", SOURCE, f"SkiTramTower{H}{SOURCE}")


def build(H, design, args):
    """One tower at `design` (C or D): tram_tower's skinned LOD0, joined, contracts in both frames, net frame."""
    use(design)
    t, table, spec, parts, lay, _src = TT.build_variant("tram", H, args)
    rep_a, _mv = TT.check_contracts(t, table, spec, parts, "authoring")
    cs2_asset.orient_for_net(t)
    rep_n, _mv = TT.check_contracts(t, table, spec, parts, "net")
    return t, table, spec, parts, {"authoring": rep_a, "net": rep_n}


DESIGN_PARTS = ("saddle_", "haulroll_", "sbrk_p_c", "sbrk_n_c", "sbrk_p_k", "sbrk_n_k")   # the parts D_DESIGN moves


def topology(t):
    me = t.data
    lv = np.zeros(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    lt = np.zeros(len(me.polygons), dtype=np.int64)
    me.polygons.foreach_get("loop_total", lt)
    part = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    return len(me.vertices), lv, lt, part, cs2_skin.coords(t).copy()


def same_loops(a, b, table):
    """C's per-loop UVs fit D's loops when: the same vertex / polygon counts, polygon order, loop totals and part per
    polygon; every loop of a part D_DESIGN does not touch at C's position (1e-5 m: the joined mesh numbers welded
    vertices by a global coordinate sort, so indices shift, positions do not); every part it moves (DESIGN_PARTS) with
    C's part-local loop structure (the same corners in the same order). Returns (ok, report)."""
    if a[0] != b[0] or len(a[1]) != len(b[1]) or not np.array_equal(a[2], b[2]) or not np.array_equal(a[3], b[3]):
        return False, {"counts": [a[0], b[0], len(a[1]), len(b[1])]}
    starts = np.r_[0, np.cumsum(a[2])[:-1]]
    moved, stray, shuffled = [], [], []
    for k in range(1, len(table) + 1):
        pp = np.nonzero(a[3] == k)[0]
        if not len(pp):
            continue
        la = np.concatenate([a[1][starts[p]:starts[p] + a[2][p]] for p in pp])
        lb = np.concatenate([b[1][starts[p]:starts[p] + b[2][p]] for p in pp])
        d = float(np.abs(a[4][la] - b[4][lb]).max())
        if d <= 1e-5:
            continue
        name = table[k - 1]
        if not name.startswith(DESIGN_PARTS):
            stray.append((name, round(d, 4)))
        elif not np.array_equal(np.unique(la, return_inverse=True)[1], np.unique(lb, return_inverse=True)[1]):
            shuffled.append(name)
        else:
            moved.append(name)
    return not stray and not shuffled, {"moved_parts": len(moved), "stray": stray[:8], "shuffled": shuffled[:8]}


def shipped_uvs(t, H):
    """The shipped C LOD0's UVs on the C rebuild `t` (net frame): every FBX triangle's corners land on one rebuilt
    polygon (vertex match <= 0.2 mm, as lift_tower.twin_atlas). Returns (uv per loop (n, 2), report)."""
    from mathutils.kdtree import KDTree
    name = f"SkiTramTower{H}{SOURCE}"
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(c_folder(H), name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    imp = next(o for o in new if o.type == "MESH")
    mw = imp.matrix_world.copy()
    ico = np.array([tuple(mw @ v.co) for v in imp.data.vertices], dtype=np.float64)
    me = t.data
    rco = cs2_skin.coords(t)
    kd = KDTree(len(rco))
    for i, c in enumerate(rco):
        kd.insert(c, i)
    kd.balance()
    best = None
    for sg in (1.0, -1.0):
        mp = np.c_[ico[:, 0] / 100.0, sg * ico[:, 2] / 100.0, ico[:, 1] / 100.0]
        err = max(kd.find(tuple(p))[2] for p in mp[:: max(1, len(mp) // 200)])
        if best is None or err < best[0]:
            best = (err, sg, mp)
    _err, sg, mp = best
    vmap = np.empty(len(mp), dtype=np.int64)
    dmax = 0.0
    for i, p in enumerate(mp):
        _c, j, d = kd.find(tuple(p))
        vmap[i], dmax = j, max(dmax, d)
    _ok(dmax <= 2e-4 and len(rco) == len(ico), f"[{name}] the C rebuild matches the shipped LOD0",
        f"({len(rco)} / {len(ico)} vertices, worst {dmax * 1000:.3f} mm)")
    vpolys = {}
    for p in me.polygons:
        for v in p.vertices:
            vpolys.setdefault(v, set()).add(p.index)
    layers = [l.name for l in imp.data.uv_layers]
    uv_in = imp.data.uv_layers.get("UVUnique") or imp.data.uv_layers[0]
    uv = np.full((len(me.loops), 2), np.nan)
    loops_of = {p.index: {me.loops[li].vertex_index: li for li in p.loop_indices} for p in me.polygons}
    amb = conflict = 0
    for tri in imp.data.polygons:
        vs = [int(vmap[imp.data.loops[li].vertex_index]) for li in tri.loop_indices]
        cand = set.intersection(*(vpolys.get(v, set()) for v in vs))
        if len(cand) != 1:
            amb += 1
            if not cand:
                continue
        pi = min(cand)
        for li, v in zip(tri.loop_indices, vs):
            dst = loops_of[pi].get(v)
            u = tuple(uv_in.data[li].uv)
            if dst is None:
                continue
            if not np.isnan(uv[dst, 0]) and np.abs(uv[dst] - u).max() > 1e-5:
                conflict += 1
            uv[dst] = u
    missing = int(np.isnan(uv[:, 0]).sum())
    _ok(missing == 0 and conflict == 0, f"[{name}] every rebuilt loop has one shipped UV",
        f"({missing} missing, {conflict} conflicts, {amb} ambiguous triangles; FBX layers {layers})")
    for o in new:
        data, kind = o.data, o.type
        bpy.data.objects.remove(o, do_unlink=True)
        if data is not None and data.users == 0 and kind in ("MESH", "ARMATURE"):
            (bpy.data.meshes if kind == "MESH" else bpy.data.armatures).remove(data)
    return uv, {"mesh": name, "verts": int(len(rco)), "worst_vertex_mm": round(dmax * 1000.0, 4),
                "fbx_lateral_sign": sg, "ambiguous_tris": amb, "fbx_uv_layers": layers}


def set_uvs(t, uv):
    """`uv` (per loop) as the UVUnique layer, the only one (the shipped mesh's layer; the bake's box UVMap goes)."""
    me = t.data
    lay = me.uv_layers.get("UVUnique") or me.uv_layers.new(name="UVUnique")
    lay.data.foreach_set("uv", uv.astype(np.float32).ravel())
    for other in [l.name for l in me.uv_layers if l.name != "UVUnique"]:
        me.uv_layers.remove(me.uv_layers[other])
    me.uv_layers.active = me.uv_layers["UVUnique"]
    me.uv_layers["UVUnique"].active_render = True


def stage_textures(H, asset_dir, name):
    """Preview staging only: C's shipped maps under D's name (nothing baked; D's loops carry C's UVs)."""
    import shutil
    os.makedirs(asset_dir, exist_ok=True)
    src = f"SkiTramTower{H}{SOURCE}"
    for m in MAPS:
        p = os.path.join(c_folder(H), f"{src}_{m}.png")
        if os.path.exists(p):
            shutil.copyfile(p, os.path.join(asset_dir, f"{name}_{m}.png"))


def budgets(spec, tris):
    """C9 with the 5 % rule: (cap, raised_to or None) per LOD0 / LOD2; LOD1 target and the 50 % rule as tram_tower."""
    b = TT.budget(spec)
    out = {"tris": tris, "lod0_cap": b["lod0_cap"], "lod2_cap": b["lod2_cap"], "raised": {}}
    for lvl, key in ((0, "lod0_cap"), (2, "lod2_cap")):
        cap = b[key]
        if tris[lvl] > cap:
            if tris[lvl] > cap * (1.0 + CAP_RULE):
                raise SaddleError(f"LOD{lvl} {tris[lvl]} > cap {cap} by more than 5 %: an owner call")
            out["raised"][f"LOD{lvl}"] = tris[lvl]
    _ok(tris[1] <= 0.5 * tris[0] and tris[1] <= b["lod1_target"], f"tram{spec.H} C9 LOD1",
        f"{tris[1]} (<= 50 % of {tris[0]}, <= {b['lod1_target']})")
    return out


# -- previews --------------------------------------------------------------------------------------------------------------

def rope_mat(name):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (0.03, 0.03, 0.03, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.7
    bsdf.inputs["Roughness"].default_value = 0.45
    return mat


def render(t, arm, spec, pose, rope_pose, asset_dir, name, raw, views):
    """`t` posed at `pose` (Blender deg) with each half's ropes drawn as the game draws them at `rope_pose` (the true rope
    angles: equal to `pose` when the halves follow, the unclamped ones on a clamped tower)."""
    import lift_tower as LT
    piv = TT.skin_pivots(spec)
    objs = [cs2_skin.posed_copy(t, arm, pose, piv, f"{name}Pose")]
    mat = rope_mat(name + "_PreviewRope")
    for v, f in TT.skin_rope_runs(spec, rope_pose, run=3.0):
        me = bpy.data.meshes.new(f"{name}Rope")
        me.from_pydata(v, [], f)
        me.materials.append(mat)
        objs.append(bpy.data.objects.new(me.name, me))
        bpy.context.collection.objects.link(objs[-1])
    try:
        return LT.render_tower_views(objs[0], asset_dir, name, raw, views, resolution=(1000, 760), extras=objs[1:],
                                     light=LT.SKIN_LIGHT)
    finally:
        for ob in objs:
            me = ob.data
            bpy.data.objects.remove(ob, do_unlink=True)
            bpy.data.meshes.remove(me)


def views(spec, tag):
    """Side at rope height (outboard of the +X lane: saddle, rocker, haul row), the head 3/4, the joint close-up, from
    below; net frame."""
    z0 = spec.z0
    k = 50.0 / 15.6
    tc = (TT.TRAM["lane_y"], 0.0, z0 - 0.7)
    return {"side": dict(name=f"side_{tag}", dir=(1.0, 0.0, 0.06), target=tc, lens=50.0, dist=3.7 * k),
            "q34": dict(name=f"q34_{tag}", dir=(1.0, -1.1, 0.35), target=(0.0, 0.0, z0 - 0.6), lens=35.0,
                        dist=7.8 * 35.0 / 15.6),
            "near": dict(name=f"near_{tag}", dir=(1.0, 0.0, 0.06), target=(TT.TRAM["lane_y"], 0.0, z0 - 0.55), lens=50.0,
                         dist=7.0),
            "joint": dict(name=f"joint_{tag}", dir=(1.0, -0.40, 0.18), lens=50.0, dist=3.6,
                          target=(TT.TRAM["lane_y"] + 0.1, 0.0, z0 - 0.55)),
            "under": dict(name=f"under_{tag}", dir=(0.55, -0.35, -1.0), lens=35.0, dist=5.0,
                          target=(TT.TRAM["lane_y"], 0.0, z0 - 0.9), ground=False)}


def lineup_poses(rows):
    """The lineup's columns (code degrees): level, the TS-1 median crest, the steepest pitch and the widest break the
    D limits follow, each a real planner tower."""
    inside = [(lab, f, b) for lab, f, b in rows
              if max(abs(f), abs(b)) <= SADDLE_MAX + 0.5 and abs(b - f) / 2.0 <= HALF_BREAK + 0.5]
    by_hb = sorted(inside, key=lambda r: abs(r[2] - r[1]))
    med = by_hb[len(by_hb) // 2]
    steep = max(inside, key=lambda r: max(abs(r[1]), abs(r[2])))
    wide = max(inside, key=lambda r: (abs(r[2] - r[1]), max(abs(r[1]), abs(r[2]))))
    return [("level", "level", 0.0, 0.0), ("crest", med[0], med[1], med[2]), ("steep", steep[0], steep[1], steep[2]),
            ("break", wide[0], wide[1], wide[2])]


def before_after_pose(rows):
    """The before / after tower: of the crests past C's half-break (> 20 deg) that D follows in full, the one C misses
    most (code degrees)."""
    inside = [(lab, f, b) for lab, f, b in rows
              if max(abs(f), abs(b)) <= SADDLE_MAX + 0.5 and C_LIMITS[1] < (b - f) / 2.0 <= HALF_BREAK + 0.5]
    return max(inside, key=lambda r: max(abs(r[1] - half_pitches(r[1], r[2], *C_LIMITS)[0]),
                                         abs(r[2] - half_pitches(r[1], r[2], *C_LIMITS)[1])))


def bpose(f, b):
    """Code (front, back) -> the Blender pose dict (theta = -code)."""
    return TT.skin_pose(-f, -b)


def montage(paths_grid, out_path):
    import lift_head
    return lift_head.montage(paths_grid, out_path)


# -- one height ------------------------------------------------------------------------------------------------------------

def run_height(H, out, preview=None, before_after=False, negative=False):
    t0 = time.perf_counter()
    args = c_args(H)
    S.set_wheels(args.wheels)
    S.set_wheel_dress(True)
    TT.set_skinned(True)
    rows = planner_poses()
    TT.clear_scene()
    # C: the rebuild = the shipped LOD0; its loops' UVs
    tc, table_c, spec_c, _parts_c, _rc = build(H, SOURCE, args)
    uv, arep = shipped_uvs(tc, H)
    topo_c = topology(tc)
    rep = {"H": H, "atlas": arep}
    raw = os.path.join(preview, "raw") if preview else None
    if negative:
        pb_c = TT.skin_part_bones(tc, table_c)
        TT.SKIN_TRAM.update(theta_max=SADDLE_MAX, half_break=HALF_BREAK)
        neg = TT.skin_sweep(tc, table_c, spec_c, pb_c, gate=False, label=f"tram{H}C@D")
        use(SOURCE)
        _ok(neg["failed"] is not None, f"tram{H} negative: C's geometry at the D grid ({SADDLE_MAX:g} / {HALF_BREAK:g}) "
            "fails G6", f"({str(neg['failed'])[:160]})")
        rep["negative"] = str(neg["failed"])[:400]
    if before_after and preview:
        cname = f"SkiTramTower{H}{SOURCE}"
        set_uvs(tc, uv)
        cdir = os.path.join(out, cname)
        os.makedirs(cdir, exist_ok=True)
        for m in MAPS:
            p = os.path.join(c_folder(H), f"{cname}_{m}.png")
            if os.path.exists(p):
                import shutil
                shutil.copyfile(p, os.path.join(cdir, f"{cname}_{m}.png"))
        arm_c, _skc = TT.skin_stage(tc, [], table_c, spec_c, cname, sweep=False)
        lab, f, b = before_after_pose(rows)
        cf, cb = half_pitches(f, b, *C_LIMITS)
        vv = views(spec_c, "before")
        rep["before_after"] = {"tower": lab, "rope_code": [f, b], "c_halves_code": [round(cf, 2), round(cb, 2)]}
        rep["frames_before"] = render(tc, arm_c, spec_c, bpose(cf, cb), bpose(f, b), cdir, cname, raw,
                                      [vv["side"], vv["joint"]])
        bpy.data.objects.remove(arm_c, do_unlink=True)
    me = tc.data
    bpy.data.objects.remove(tc, do_unlink=True)
    bpy.data.meshes.remove(me)
    # D: C's topology at the D design, C's UVs on every loop
    name = f"SkiTramTower{H}{LETTER}"
    t, table, spec, parts, contracts = build(H, LETTER, args)
    t.name = t.data.name = name
    topo_d = topology(t)
    same, trep = same_loops(topo_c, topo_d, table)
    _ok(same and table == table_c, f"[{name}] topology = {SOURCE}'s (vertices, polygons, loops, parts; static loops at "
        f"C's positions): C's UVs map 1:1, 0 MB", f"({topo_d[0]} vertices, {len(topo_d[1])} loops, {trep})")
    rep["topology"] = trep
    set_uvs(t, uv)
    l1p, l2p = TT.lod_lists("tram")
    n1, n2 = TT.count_after_delete(t, table, l1p), TT.count_after_delete(t, table, l2p)
    lods = TT.make_lods(t, name, table, parts, "tram", n1, n2)
    tris = [cs2_asset.tri_count(o) for o in [t] + lods]
    bud = budgets(spec, tris)
    arm, sk = TT.skin_stage(t, lods, table, spec, name)          # G6 at joint_grid(60, 30) + the rope reach: raise
    _ok(sk["rope_reach"]["clear_to_deg"] >= SADDLE_MAX, f"[{name}] rope reach",
        f"clear to {sk['rope_reach']['clear_to_deg']} deg (>= {SADDLE_MAX:g}), first hit {sk['rope_reach']['first_hit']}")
    pairs = blender_pairs(rows, SADDLE_MAX, HALF_BREAK)
    pl = TT.skin_sweep(t, table, spec, sk["part_bone"], pairs=pairs, label=f"tram{H}D planner")
    # the tower-lean round: the poses relative to a leaned mast (lean grid; the TS-1 towers under the 55 deg cap, the
    # least lean) are inside D's grid; gated all the same
    lg = TT.skin_sweep(t, table, spec, sk["part_bone"], pairs=lean_grid(), label=f"tram{H}D lean grid")
    lrows = lean_rows(rows)
    lp = TT.skin_sweep(t, table, spec, sk["part_bone"], pairs=blender_pairs(lrows, SADDLE_MAX, HALF_BREAK),
                       label=f"tram{H}D lean planner")
    lim = code_limits()
    _ok(lim == (SADDLE_MAX, HALF_BREAK), "the code's tram limits = the art's",
        f"(LiftAlignMath {lim}, art {(SADDLE_MAX, HALF_BREAK)})")
    half_len = spec.saddle_l / 2.0
    cov_c = rope_offsets(rows, *C_LIMITS, half_len)
    cov_d = rope_offsets(rows, SADDLE_MAX, HALF_BREAK, half_len)
    rep.update({
        "name": name, "tris": tris, "budget": bud, "contracts": sorted(contracts),
        "design": dict(D_DESIGN), "limits": {"saddle_max_deg": SADDLE_MAX, "half_break_deg": HALF_BREAK,
                                            "code": lim},
        "g6": {"poses": len(sk["g6"]["poses"]), "min_clear": sk["g6"]["min_clear"], "seat_tops": sk["g6"]["seat_tops"],
               "failed": sk["g6"]["failed"]},
        "g6_planner": {"poses": len(pl["poses"]), "min_clear": pl["min_clear"], "failed": pl["failed"]},
        "g6_lean": {"grid_poses": len(lg["poses"]), "grid_min_clear": lg["min_clear"], "grid_failed": lg["failed"],
                    "planner_towers": len(lrows), "planner_poses": len(lp["poses"]), "planner_min_clear": lp["min_clear"],
                    "planner_failed": lp["failed"],
                    "coverage": {r: {"C": rope_offsets(lean_rows(rows, r), *C_LIMITS, spec.saddle_l / 2.0)[0],
                                     "D": rope_offsets(lean_rows(rows, r), SADDLE_MAX, HALF_BREAK,
                                                       spec.saddle_l / 2.0)[0]} for r in ("min", "max")}},
        "rope_reach": sk["rope_reach"], "weights": sk["weights"],
        "coverage": {"towers": len(rows),
                     "C": {"limits": list(C_LIMITS), "inside": cov_c[0], "worst": list(cov_c[1])},
                     "D": {"limits": [SADDLE_MAX, HALF_BREAK], "inside": cov_d[0], "worst": list(cov_d[1])}},
        "textures_added_mb": 0.0,
    })
    print(f"[saddle] {name}: LOD0/1/2 {tris}; G6 {rep['g6']['poses']} poses min {sk['g6']['min_clear']['m'] * 1000:.1f} mm; "
          f"planner {len(pl['poses'])} poses min {pl['min_clear']['m'] * 1000:.1f} mm; reach {sk['rope_reach']['clear_to_deg']}; "
          f"TS-1 inside C {cov_c[0]} / D {cov_d[0]} of {len(rows)}; worst offset C {cov_c[1][2]:.2f} m, D {cov_d[1][2]:.2f} m")
    if preview:
        asset_dir = os.path.join(out, name)
        stage_textures(H, asset_dir, name)
        frames = {"lineup": {}}
        for col, lab, f, b in lineup_poses(rows):
            vv = views(spec, col)
            frames["lineup"][col] = {"tower": lab, "code": [f, b],
                                     "frames": render(t, arm, spec, bpose(f, b), bpose(f, b), asset_dir, name, raw,
                                                      [vv["side"], vv["q34"]])}
        if before_after:
            lab, f, b = before_after_pose(rows)
            vv = views(spec, "after")
            frames["after"] = render(t, arm, spec, bpose(f, b), bpose(f, b), asset_dir, name, raw,
                                     [vv["side"], vv["joint"]])
            lab3, f3, b3 = lineup_poses(rows)[3][1:]      # the widest break: the V-notch and the haul row open
            vv = views(spec, "close")
            frames["close"] = render(t, arm, spec, bpose(f3, b3), bpose(f3, b3), asset_dir, name, raw,
                                     [vv["near"], vv["under"]])
            lab2, f2, b2 = lineup_poses(rows)[2][1:]
            vv = views(spec, "closesteep")
            frames["close"] += render(t, arm, spec, bpose(f2, b2), bpose(f2, b2), asset_dir, name, raw,
                                      [vv["joint"], vv["q34"]])
        lab, f, b = lineup_poses(rows)[1][1:]
        lv = []
        for level, ob in enumerate([t] + list(lods)):
            vv = views(spec, f"lod{level}")
            lv += render(ob, arm, spec, bpose(f, b), bpose(f, b), asset_dir, name, raw, [vv["q34"]])
        frames["lod"] = lv
        rep["frames"] = frames
    bpy.data.objects.remove(arm, do_unlink=True)
    for o in lods:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(me)
    use(SOURCE)
    rep["seconds"] = round(time.perf_counter() - t0, 1)
    print(f"[saddle] done {name} in {rep['seconds']} s")
    return rep


def sheets(prev):
    """The preview sheets from the raw frames (<prev>/raw/<name>_view_<view>.png): lineup side / 3/4 (rows 30 / 45 / 60,
    columns level / crest / steep / break), close-ups, before / after (rows C / D), LODs (rows 30 / 45 / 60)."""
    raw = os.path.join(prev, "raw")

    def p(H, letter, view):
        f = os.path.join(raw, f"SkiTramTower{H}{letter}_view_{view}.png")
        return f if os.path.exists(f) else None
    cols = ("level", "crest", "steep", "break")
    out = []
    for v in ("side", "q34"):
        out.append(montage([[p(H, LETTER, f"{v}_{c}") for c in cols] for H in HEIGHTS],
                           os.path.join(prev, f"lineup_{v}.png")))
    out.append(montage([[p(60, LETTER, "near_close"), p(60, LETTER, "under_close")],
                        [p(60, LETTER, "joint_closesteep"), p(60, LETTER, "q34_closesteep")]],
                       os.path.join(prev, "saddle_closeups.png")))
    out.append(montage([[p(60, SOURCE, "side_before"), p(60, SOURCE, "joint_before")],
                        [p(60, LETTER, "side_after"), p(60, LETTER, "joint_after")]],
                       os.path.join(prev, "before_after.png")))
    out.append(montage([[p(H, LETTER, f"q34_lod{k}") for k in range(3)] for H in HEIGHTS], os.path.join(prev, "lods.png")))
    for o in out:
        print(f"[saddle] sheet {o}")
    return out


def parse(argv):
    ap = argparse.ArgumentParser(prog="tram_saddle.py")
    ap.add_argument("--heights", default="30", help="comma list of 30, 45, 60 (or 'all')")
    ap.add_argument("--out", default=None, help="scratch folder (never assets/import)")
    ap.add_argument("--preview", default=None, help="preview folder (raw frames under <preview>/raw)")
    ap.add_argument("--before-after", dest="before_after", action="store_true",
                    help="also the before (C clamped) / after (D) frames and the saddle close-ups")
    ap.add_argument("--negative", action="store_true", help="also C's geometry at the D grid (must fail G6)")
    ap.add_argument("--report", default=None)
    ap.add_argument("--sheets", default=None, help="only build the sheets in this preview folder")
    return ap.parse_args(argv)


def main_cli():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parse(argv)
    if args.sheets:
        sheets(os.path.abspath(args.sheets))
        print("PASS tram_saddle: sheets")
        return
    if not args.out:
        raise SystemExit("--out is required")
    out = os.path.abspath(args.out)
    if os.path.normcase(out).startswith(os.path.normcase(os.path.join(_REPO, "assets", "import"))):
        raise SystemExit("--out is in the shipping tree assets/import: this round exports nothing")
    heights = HEIGHTS if args.heights == "all" else tuple(int(h) for h in args.heights.split(","))
    reps = []
    for H in heights:
        if H not in HEIGHTS:
            raise SystemExit(f"unknown height {H}")
        reps.append(run_height(H, out, os.path.abspath(args.preview) if args.preview else None,
                               before_after=args.before_after, negative=args.negative))
        if args.report:
            with open(args.report, "w", encoding="utf-8", newline="\n") as f:
                json.dump(reps, f, indent=1, default=lambda o: o.item() if hasattr(o, "item") else str(o))
    print("PASS tram_saddle: " + ", ".join(f"{r['name']} {r['tris']}" for r in reps))


if __name__ == "__main__":
    try:
        main_cli()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
