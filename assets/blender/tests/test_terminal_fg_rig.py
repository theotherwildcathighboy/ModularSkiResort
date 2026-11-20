"""
test_terminal_fg_rig.py - T256 (r4 lift motion): the RIGGED fixed-grip stations (lift_terminal --rig, M_RIG, letter P).

- R0 flags off: set_m_rig(False) restores every table (M_SPECS, the rope radius, the M3 classes) and an O base built after a
  rigged build is the O base built before it (mesh hash); no rig part exists without the flag.
- R1 bones (contract a): base Root / Bullwheel / NoseTrain, top Root / Bullwheel; heads game (0, 3.55, 0.20), (0, 5.84,
  -7.025), top (0, 3.75, 0); the bullwheel parts; the nose train's outer half (12 parts a lane) on NoseTrain; every vertex
  on one bone on every LOD; the skinned FBXs re-imported (cs2_skin.roundtrip: bones, heads, identity rests, one bone per
  vertex); a posed copy equals the contracts' pose. Negative: a vertex shared by the rim and a Root part.
- R2 rope on the carrier path within 1 mm (contract b): the chord from the LANE END to the tangent, the level half torus.
  Negative: a chord 2 mm off.
- R3 entry sheaves (contract d): the rim touches the rope at the tangent point (center, both axes, rim low on the rope top)
  and the turning bullwheel's envelope clears every static part by 5 mm. Negatives: a sheave 3 mm off, a static box 3 mm
  from the rim.
- R4 nose train (contract c, base): the joint sweep +-40 deg clear by 5 mm, the chair on the posed line rope. Negative: a
  static box in the outer half's swept path. Round 2 (S4): every nose-train bogie pin hangs in its straps (its part
  overlaps the half beam and the bogie bar).
- R2b round 2 (critic pass 1): S2 / C6 the entry sheave hangers (the top's outboard posts, the cheek gussets) clear the
  turning wheel's envelope, the chair body sweep and the lane band under the rope; S3 the yellow spoke on the Bullwheel
  bone (in R1); C1 the knuckle on each lane end; C2 the nose braces under 60 tris, into boom and beam. Round 2b (critic
  pass 2): the top posts r 0.09 with the black wrap, the braces >= 25 deg to their booms.
- R6 the shipped letters' flags (SHIPPED_FLAGS): the O base without --thin-rope and the O top with --rig stop naming the
  flag; the shipped set (preview / review flags included) passes; --allow-flag-drift builds and reports.
- R5 every generation-M contract (M1-M21, M3 with the real chair) on both rigged stations, and the budgets (LOD0 caps,
  the LOD1 ratio with the rope kept un-decimated).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_terminal_fg_rig.py
"""
import hashlib
import json
import math
import os
import shutil
import sys
import tempfile
import time

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_asset  # noqa: E402
import cs2_sheaves as S  # noqa: E402
import lift_terminal as LT  # noqa: E402

FAILS = []
N_OK = [0]


def check(cond, label, detail=""):
    if cond:
        N_OK[0] += 1
        print(f"  ok   {label} {detail}".rstrip())
    else:
        print(f"  FAIL {label} {detail}".rstrip())
        FAILS.append(label)


def expect(code, fn, label):
    try:
        fn()
    except (LT.MContractError, ValueError, RuntimeError) as e:
        msg = str(e)
        check(code in msg, f"{label} -> {code}", f"({msg[:160]})")
        return
    check(False, f"{label} -> {code}", "(no error raised)")


def mesh_hash(t):
    me = t.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    lv = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", lv)
    h = hashlib.sha256(co.tobytes())
    h.update(lv.tobytes())
    h.update(str(me.get(cs2_asset.PART_TABLE_PROP)).encode())
    return h.hexdigest()


def build_o(kind):
    """The shipped O station (thin rope; the base without its hosted tower), assembled without the finish."""
    LT.set_m_rig(False)
    S.set_wheels("v2")
    LT.FINE_DETAIL_M = True
    LT.M_NOSE_TRAIN = kind == "drive"
    LT.M_ROPE_R = LT.THIN_ROPE_R
    LT.clear_scene()
    parts = LT.build_station_m(kind)
    t, table, _src = LT.assemble_m(parts, False, kind)
    return t, table


LAST_MATS = {}      # {part: material key} of the last build_rig


def build_rig(kind, mutate=None):
    S.set_wheels("v2")
    LT.FINE_DETAIL_M = True
    LT.M_NOSE_TRAIN = False
    LT.M_ROPE_R = LT.M_ROPE_R_SHIPPED
    LT.set_m_rig(True, kind)
    LT.clear_scene()
    parts = LT.build_station_m(kind)
    if mutate:
        parts = mutate(parts)
    LAST_MATS.clear()
    LAST_MATS.update({o.name: k for o, k in parts})
    t, table, _src = LT.assemble_m(parts, False, kind)
    co, tris, names, _ = LT.m_arrays(t)
    return t, table, co, tris, names


def add_box(name, x0, x1, y0, y1, z0, z1, mat="paint1"):
    def f(parts):
        return parts + [(LT._mbox(name, x0, x1, y0, y1, z0, z1), mat)]
    return f


def move(prefix, d):
    def f(parts):
        for o, _m in parts:
            if o.name.startswith(prefix):
                o.location = (o.location[0] + d[0], o.location[1] + d[1], o.location[2] + d[2])
        return parts
    return f


def r0_flags_off():
    print("R0 flags off")
    t, _tb = build_o("drive")
    h0 = mesh_hash(t)
    names0 = set(json.loads(t.data[cs2_asset.PART_TABLE_PROP]))
    build_rig("drive")
    t, _tb = build_o("drive")
    check(mesh_hash(t) == h0, "R0 the O base after a rigged build is the O base before it (mesh hash)")
    check(all(LT.M_SPECS[k] == LT.M_SPECS_SHIPPED[k] for k in LT.M_SPECS), "R0 M_SPECS restored")
    check(LT.M_GRIP_OK == ("wheelrope", "ropechord_", "sheave_bull", "sheave_hold_", "spoke_", "wheelhub"),
          "R0 M_GRIP_OK restored", f"({LT.M_GRIP_OK})")
    rigp = sorted(n for n in names0 if n.startswith(("entryarm", "entrypost", "sheave_entry", "spoke_mark",
                                                     "wheelhub_cap", "ropechord_knuckle", "holdbrace")))
    check(not rigp, "R0 no rig part without the flag", f"({rigp[:4]})")
    check(sum(1 for n in names0 if n.startswith("spoke_")) == 12, "R0 the O wheel keeps its 12 spokes")


def r1_bones(kind, t, table):
    print(f"R1 bones {kind}")
    want = LT.RIG_BONES[kind]
    h = LT.rig_heads_m(kind)
    game = {b: (round(-v[0], 4), round(v[2], 4), round(-v[1], 4)) for b, v in h.items()}
    check(tuple(h) == want, f"R1 {kind} bones {want}")
    gw = {"drive": {"Bullwheel": (0.0, 3.55, 0.20), "NoseTrain": (0.0, 5.84, -7.025)},
          "return": {"Bullwheel": (0.0, 3.75, 0.0)}}[kind]
    check(all(abs(a - b) < 1e-6 for k, v in gw.items() for a, b in zip(game[k], v)), f"R1 {kind} heads game", f"({game})")
    pb = LT.rig_part_bones_m(table, kind)
    bw = sorted(p for p, b in pb.items() if b == "Bullwheel")
    check(len([p for p in bw if p.startswith("spoke_")]) == LT.RIG_WHEEL["spokes"]
          and "sheave_bull_rim" in bw and "wheelhub_cap" in bw and "spoke_mark" not in bw,
          f"R1 {kind} Bullwheel = rim + {LT.RIG_WHEEL['spokes']} spokes + hub + cap", f"({len(bw)} parts)")
    # S3 (round 2): ONE spoke is the yellow mark, on the Bullwheel bone; the rest are galv
    mk = f"spoke_{LT.RIG_WHEEL['mark_spoke']:02d}"
    spk = {p: LAST_MATS.get(p) for p in bw if p.startswith("spoke_")}
    check(pb.get(mk) == "Bullwheel" and spk.get(mk) == "safety_yellow"
          and all(m == "galv" for p, m in spk.items() if p != mk),
          f"R1 {kind} S3 {mk} is safety_yellow on the Bullwheel bone, the other spokes galv", f"({spk})")
    if kind == "drive":
        nt = sorted(p for p, b in pb.items() if b == "NoseTrain")
        per = {s: [p for p in nt if f"_{s}_" in p or p.endswith(f"_{s}_b") or p.endswith(f"_{s}_0")] for s in "np"}
        check(len(nt) >= 24 and all(len(v) >= 12 for v in per.values()),
              "R1 NoseTrain = the outer half of each lane (12+ parts a lane)", f"({len(nt)})")
        check(not any("hanger_" in p or "pivot_main_" in p for p in nt), "R1 hanger and main pin stay on Root")
    _n, _fb, vb, bones, _pb = LT._rig_bone_masks_m(t, table, kind)
    check(all((vb == i).sum() > 0 for i in range(len(bones))), f"R1 {kind} every bone has vertices",
          f"({[(b, int((vb == i).sum())) for i, b in enumerate(bones)]})")


def r1_skin(kind, t, table):
    """Orient, LOD chain, skin, skinned FBXs, round trip, posed copies."""
    name = "SkiLiftBaseStationP" if kind == "drive" else "SkiLiftTopStationP"
    rope = LT.rig_rope_tris_m(t, table)
    cs2_asset.orient_for_net(t)
    LT.split_windows(t, name, table, cells=LT.M_WIN_CELLS[kind])
    n0 = cs2_asset.tri_count(t)
    fx = LT.RigLodFixed(S.lod_fixed())
    lods = cs2_asset.make_lod_chain(t, name, targets=(int(round(LT.LOD1_RATIO_M * n0)) - rope, LT.LOD2_TARGET_M[kind]),
                                    delete_prefixes=(LT.lod1_drop_m(table), LT.lod2_drop_m(table)),
                                    shade_angle=LT.M_SHADE, fixed=fx)
    tris = [n0] + [cs2_asset.tri_count(l) for l in lods]
    cap = LT.RIG_LOD0_CAP[kind]
    check(n0 <= cap, f"R5 {kind} LOD0 {n0} <= cap {cap}")
    check(tris[1] <= 0.50 * n0, f"R5 {kind} LOD1 {tris[1]} <= 0.50 LOD0 (rope kept: {fx.report.get(1)})",
          f"(ratio {tris[1] / n0:.3f})")
    check(tris[2] <= LT.LOD2_TARGET_M[kind] * 1.10, f"R5 {kind} LOD2 {tris[2]} <= {LT.LOD2_TARGET_M[kind]} + 10 %")
    check(fx.report.get(1, {}).get("rope_tris") == rope, f"R5 {kind} LOD1 keeps the rope un-decimated",
          f"({fx.report.get(1)} vs LOD0 {rope})")
    arm, w = LT.rig_skin_m(t, lods, table, kind, name)
    check(all(sum(v.values()) > 0 for v in w.values()), f"R1 {kind} weights on every level", f"({w})")
    tmp = tempfile.mkdtemp(prefix="t256rig_")
    try:
        cs2_asset.export_fbx_skinned(t, arm, os.path.join(tmp, f"{name}.fbx"))
        for i, lod in enumerate(lods, start=1):
            cs2_asset.export_fbx_skinned(lod, arm, os.path.join(tmp, f"{name}_LOD{i}.fbx"))
        rt = LT.rig_roundtrip_m(tmp, name, len(lods), kind)
        check(sorted(rt) == ["0", "1", "2"], f"R1 {kind} round trip of LOD0-2",
              f"({ {k: v['local_pos_cm'] for k, v in rt.items()} })")
    except LT.MContractError as e:
        check(False, f"R1 {kind} round trip", f"({e})")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    poses = [{"Bullwheel": 30.0}] + ([{"NoseTrain": 40.0}, {"NoseTrain": -20.0}] if kind == "drive" else [])
    for ang in poses:
        try:
            ob = LT.rig_posed_copy_m(t, arm, ang, kind, "PoseProbe")
            bpy.data.objects.remove(ob, do_unlink=True)
            check(True, f"R1 {kind} posed copy {ang} = the contracts' pose")
        except LT.MContractError as e:
            check(False, f"R1 {kind} posed copy {ang}", f"({e})")
    for l in lods:
        bpy.data.objects.remove(l, do_unlink=True)


def r4_pin_links(co, tris, names):
    """S4 (round 2): every nose-train bogie pin hangs in its straps (cs2_sheaves pin_links): the pin's part overlaps its half
    beam and its bogie bar (the straps' buried ends; the bare pin at (main_z + sub_z) / 2 touches neither)."""
    pre = LT.M_HOLD["prefix"]
    bad, sizes = [], {}
    for tag in ("n", "p"):
        for si, half in ((0, "b"), (1, "f")):
            sel = [tris[[x == n for x in names]] for n in (f"{pre}pivot_{tag}_{si}", f"{pre}rocker_main_{tag}_{half}",
                                                           f"{pre}rocker_sub_{tag}_{si}")]
            pin, beam, bar = sel
            sizes[f"{tag}{si}"] = len(pin)
            bp = LT._bvh(co, pin)
            if not (len(beam) and len(bar) and bp.overlap(LT._bvh(co, beam)) and bp.overlap(LT._bvh(co, bar))):
                bad.append((tag, si))
    check(not bad, "R4 S4 the nose train's bogie pins hang in straps (into the half beam and the bogie bar)",
          f"(failing {bad}, pin part tris {sizes})")


def _part(co, tris, names, pred):
    sel = tris[[pred(n) for n in names]]
    return sel, (co[np.unique(sel)] if len(sel) else np.zeros((0, 3)))


def r2_round2(kind, t, table, co, tris, names):
    """Round 2 (critic pass 1):
    - S2 / C6: the entry sheave hangers (entryarm_*, entrypost_*: arms, cheeks, gussets, the top's posts and plates) clear the
      turning bullwheel's revolved envelope by RIG_ENTRY["clear"] (meridian-plane distance to the wheel's edge samples), the
      chair body cloud of the M3 sweep by M_CLEAR_STRUCT, and no hanger point inside the chairs' lane band (|y| < lane + the
      chair half width) is at or below the rope's top. The top has no hub post and two outboard posts.
    - C1: ropechord_knuckle_<side> is centered on the lane end on the chord axis (1 mm), r RIG_KNUCKLE, the cap on the line
      side only, on Root.
    - C2 (base): holdbrace_<tag> under 60 tris for both lanes, into its boom and the front portal beam, on Root.
    Round 2b (critic pass 2): P2-S1 the top posts at RIG_TOP_POST r with the stakes' black wrap (rubber, its band over the
    crest, 16 tris); P2-S4 each brace meets its boom at >= 25 deg (principal axes)."""
    print(f"R2b round 2 {kind}")
    spec = LT.M_SPECS[kind]
    wx, _wy, wz = spec.wheel
    pb = LT.rig_part_bones_m(table, kind)
    _n, _fb, vb, _b, _pb = LT._rig_bone_masks_m(t, table, kind)
    hang = lambda n: n.startswith(("entryarm", "entrypost"))
    htris, _hv = _part(co, tris, names, hang)
    HP = LT._edge_points(co, htris, 0.01)
    bw_t = tris[(vb[tris[:, 0]] == 1)]
    BP = LT._edge_points(co, bw_t, 0.01)
    rb, zb = np.hypot(BP[:, 0] - wx, BP[:, 1]), BP[:, 2]
    rh, zh = np.hypot(HP[:, 0] - wx, HP[:, 1]), HP[:, 2]
    dmin = 9.9
    for i in range(0, len(HP), 2000):
        d = np.hypot(rh[i:i + 2000, None] - rb[None, :], zh[i:i + 2000, None] - zb[None, :]).min(axis=1)
        dmin = min(dmin, float(d.min()))
    check(dmin >= LT.RIG_ENTRY["clear"], f"R2b {kind} S2 the hangers clear the turning wheel's envelope",
          f"(min {dmin:.4f} m >= {LT.RIG_ENTRY['clear']})")
    try:
        rep = LT.chair_sweep_m(co, tris, names, spec, levels=(0,), collect=True)
        cloud = rep.pop("_cloud")
        lo, hi = HP.min(axis=0) - 0.5, HP.max(axis=0) + 0.5
        cloud = cloud[np.all((cloud >= lo) & (cloud <= hi), axis=1)]
        bvh = LT._bvh(co, htris)
        cmin = min((bvh.find_nearest(tuple(p))[3] for p in cloud), default=9.9)
        check(cmin >= LT.M_CLEAR_STRUCT, f"R2b {kind} S2 the hangers clear the chair body sweep",
              f"(min {cmin:.3f} m >= {LT.M_CLEAR_STRUCT}, {len(cloud)} body samples near them)")
    except LT.MContractError as e:
        check(False, f"R2b {kind} S2 chair sweep", f"({e})")
    band = LT.FG_LANE + 0.90
    low = HP[(np.abs(HP[:, 1]) < band) & (HP[:, 2] <= wz + LT.M_ROPE_R)]
    check(not len(low), f"R2b {kind} S2 no hanger below the rope's top inside the lane band |y| < {band:.2f}",
          f"({len(low)} points{'' if not len(low) else ', e.g. ' + str(np.round(low[0], 3))})")
    hp = sorted({n for n in names if hang(n)})
    if kind == "return":
        P = LT.RIG_TOP_POST
        posts = [n for n in hp if n in ("entrypost_n", "entrypost_p")]
        ok = len(posts) == 2 and "entrypost" not in hp
        for s, tag in ((-1.0, "n"), (1.0, "p")):
            _s, v = _part(co, tris, names, lambda n, tag=tag: n == f"entrypost_{tag}")
            c = (v.min(axis=0) + v.max(axis=0)) / 2.0 if len(v) else np.zeros(3)
            ok = ok and abs(c[0] - P["x"]) < 1e-3 and abs(c[1] - s * P["y"]) < 1e-3
        check(ok, "R2b return S2 two outboard posts at RIG_TOP_POST, no hub post", f"({hp})")
        check("maze_post_crest_1" not in set(names) and "maze_post_crest_0" in set(names),
              "R2b return S2 the +y post stands in place of the crest maze's middle post")
        # round 2b (critic pass 2, P2-S1): post r 0.09 and the crest stakes' black wrap on its lower part
        wo, wz0, wz1 = P["wrap"]
        cz = LT.M_TOP["crest_z"]
        for s, tag in ((-1.0, "n"), (1.0, "p")):
            _s, pv = _part(co, tris, names, lambda n, tag=tag: n == f"entrypost_{tag}")
            wt, wv = _part(co, tris, names, lambda n, tag=tag: n == f"entrypost_{tag}_wrap")
            pr = np.hypot(pv[:, 0] - P["x"], pv[:, 1] - s * P["y"]).max() if len(pv) else 0.0
            wr = np.hypot(wv[:, 0] - P["x"], wv[:, 1] - s * P["y"]) if len(wv) else np.zeros(1)
            ok = (len(wt) == 2 * P["segs"] and LAST_MATS.get(f"entrypost_{tag}_wrap") == "rubber"
                  and abs(pr - P["r"]) < 1e-3 and float(np.abs(wr - (P["r"] + wo)).max()) < 1e-3
                  and abs(float(wv[:, 2].min()) - (cz + wz0)) < 1e-3 and abs(float(wv[:, 2].max()) - (cz + wz1)) < 1e-3)
            check(ok, f"R2b return P2-S1 entrypost_{tag} r {P['r']} with the stakes' black wrap",
                  f"(post r {pr:.4f}, wrap r {wr.min():.4f}..{wr.max():.4f}, z {wv[:, 2].min() - cz if len(wv) else 0:.3f}.."
                  f"{wv[:, 2].max() - cz if len(wv) else 0:.3f} over the crest, {len(wt)} tris, "
                  f"{LAST_MATS.get(f'entrypost_{tag}_wrap')})")
    gus = [n for n in hp if n.endswith("_gusset") and n.startswith("entryarm_")]
    check(len(gus) == 2, f"R2b {kind} C6 a gusset hangs each cheek from its arm", f"({gus})")
    # C1
    ax, az = spec.anchor_in
    u = np.array([wx - ax, 0.0, wz - az])
    u /= np.linalg.norm(u)
    K = LT.RIG_KNUCKLE
    for s, side in ((-1.0, "n"), (1.0, "p")):
        nm = f"ropechord_knuckle_{side}"
        kt, kv = _part(co, tris, names, lambda n, nm=nm: n == nm)
        e = np.array([ax, s * LT.FG_LANE, az])
        if not len(kv):
            check(False, f"R2b {kind} C1 {nm} exists")
            continue
        tt = (kv - e) @ u
        rr = np.linalg.norm((kv - e) - np.outer(tt, u), axis=1)
        vt = {int(i): float(((co[i] - e) @ u)) for i in np.unique(kt)}
        caps = [tri for tri in kt if all(abs(vt[int(i)] + K["length"] / 2.0) < 1e-4 for i in tri)]
        caps_far = [tri for tri in kt if all(abs(vt[int(i)] - K["length"] / 2.0) < 1e-4 for i in tri)]
        ok = (abs(float(tt.min()) + K["length"] / 2.0) < 1e-3 and abs(float(tt.max()) - K["length"] / 2.0) < 1e-3
              and float(np.abs(rr - K["r"]).max()) < 1e-3 and len(caps) == LT.RIG_ROPE_SIDES - 2 and not caps_far
              and nm not in pb)
        check(ok, f"R2b {kind} C1 {nm} on the lane end on the chord axis, capped on the line side, Root",
              f"(t {tt.min():.4f}..{tt.max():.4f}, r {rr.min():.4f}..{rr.max():.4f}, caps {len(caps)} / far {len(caps_far)}, "
              f"{len(kt)} tris, bone {pb.get(nm, 'Root')})")
    if kind != "drive":
        return
    # C2
    n_tris = 0
    for tag in ("n", "p"):
        nm = f"holdbrace_{tag}"
        bt, _bv = _part(co, tris, names, lambda n, nm=nm: n == nm)
        n_tris += len(bt)
        boom, _ = _part(co, tris, names, lambda n, tag=tag: n == f"holdboom_{tag}")
        beam, _ = _part(co, tris, names, lambda n: n == "beam_front")
        bb = LT._bvh(co, bt) if len(bt) else None
        ok = bb is not None and len(boom) and len(beam) and bb.overlap(LT._bvh(co, boom)) and bb.overlap(LT._bvh(co, beam))
        check(bool(ok) and nm not in pb, f"R2b drive C2 {nm} into its boom and the front portal beam, Root",
              f"({len(bt)} tris)")
        # round 2b (critic pass 2, P2-S4): the brace opens a triangle with the boom (it read as a second boom at 12.9 deg)
        ang = 0.0
        if len(bt) and len(boom):
            def axis(t_):
                v = co[np.unique(t_)]
                return np.linalg.svd(v - v.mean(axis=0))[2][0]
            ang = math.degrees(math.acos(min(1.0, abs(float(axis(bt) @ axis(boom))))))
        check(ang >= 25.0, f"R2b drive P2-S4 {nm} meets its boom at >= 25 deg", f"({ang:.1f} deg)")
    check(0 < n_tris < 60, f"R2b drive C2 the nose braces {n_tris} tris < 60 for both lanes")


def r6_shipped_flags():
    """The shipped letters build with their shipped flags (SHIPPED_FLAGS, orchestrator finding 2026-11-19): the O base without
    --thin-rope stops naming the flag, the O top with --rig stops, the shipped set passes, --allow-flag-drift passes."""
    print("R6 shipped flags")
    ap = LT.build_parser()

    def args(*a):
        return ap.parse_args(["--out", "x"] + list(a))
    base = ["--name", "SkiLiftBaseStationO", "--kind", "drive", "--finish", "--lods"]
    for label, a, want in (("the O base without --thin-rope", base + ["--no-hosted-tower"], "thin_rope"),
                           ("the O top with --rig", ["--name", "SkiLiftTopStationO", "--kind", "return", "--finish",
                                                     "--lods", "--thin-rope", "--rig"], "rig")):
        try:
            LT.check_shipped_flags(args(*a), ap)
            check(False, f"R6 {label} stops", "(no SystemExit)")
        except SystemExit as e:
            check(want in str(e) and "SkiLift" in str(e), f"R6 {label} stops naming the flag", f"({str(e)[:140]})")
    ok = args(*(base + ["--thin-rope", "--no-hosted-tower", "--preview", "p", "--review"]))
    check(LT.check_shipped_flags(ok, ap) == {}, "R6 the shipped O base flags pass (preview / review never count)")
    drift = args(*(base + ["--no-hosted-tower", "--allow-flag-drift"]))
    check(set(LT.check_shipped_flags(drift, ap)) == {"thin_rope"}, "R6 --allow-flag-drift builds and reports the drift")
    check(LT.flag_drift(args("--name", "SkiLiftBaseStationP", "--rig"), ap) == {}, "R6 a new letter is not checked")


def run_kind(kind):
    print(f"== {kind}")
    t, table, co, tris, names = build_rig(kind)
    spec = LT.M_SPECS[kind]
    r1_bones(kind, t, table)
    r2_round2(kind, t, table, co, tris, names)
    if kind == "drive":
        r4_pin_links(co, tris, names)
    try:
        rig = LT.rig_contracts_m(t, table, kind)
        check(True, f"R2 {kind} rope on the carrier path", f"({ {k: rig['rope'][k] for k in ('chord_radius_dev_m', 'chord_end_dev_m', 'ring_center_dev_m', 'ring_radius_dev_m', 'torus_sagitta_m')} })")
        check(True, f"R3 {kind} entry sheaves on the tangent point, the wheel envelope clear",
              f"({rig['entry']['lanes']}, near {rig['entry']['bullwheel_envelope_min_m']})")
        if kind == "drive":
            ns = rig["nose_sweep"]
            check(ns["clear_min_m"] >= LT.RIG_NOSE["clear"], "R4 NoseTrain joint sweep +-40 deg",
                  f"(min {ns['clear_min_m']} at {ns['where']})")
            env = ns["chair_on_posed_line"]["envelope_deg"]
            g = LT.RIG_NOSE["chair_tmax"]
            check(env is not None and env[0] <= -g and env[1] >= g, f"R4 chair on the posed line clears +-{g:.0f} deg",
                  f"(envelope {env}; per pitch machine min "
                  f"{ {k: v['min']['machine'] for k, v in ns['chair_on_posed_line'].items() if isinstance(v, dict)} })")
    except LT.MContractError as e:
        check(False, f"R2-R4 {kind} rig contracts", f"({e})")
    try:
        rep = LT.contracts_m(t, table, kind, sweep=True)
        check(True, f"R5 {kind} M1-M21 under the rig", f"(M3 {rep['M3']['min']}, grip {rep['M3']['grip_min']}, "
                                                        f"M5 {rep['M5']})")
    except LT.MContractError as e:
        check(False, f"R5 {kind} M1-M21 under the rig", f"({e})")
    r1_skin(kind, t, table)


def negatives():
    print("== negatives")
    spec = LT.M_SPECS_SHIPPED["drive"]

    def rope_off():
        t, table, co, tris, names = build_rig("drive", mutate=move("ropechord_n", (0.0, 0.0, 0.002)))
        LT.rig_rope_path_m(co, tris, names, LT.M_SPECS["drive"])
    expect("RIG-rope", rope_off, "R2 negative: ropechord_n 2 mm high")

    def entry_off():
        t, table, co, tris, names = build_rig("return", mutate=move("sheave_entry_", (0.003, 0.0, 0.0)))
        _n, _fb, vb, _b, _pb = LT._rig_bone_masks_m(t, table, "return")
        LT.rig_entry_m(co, tris, names, LT.M_SPECS["return"], vb, "return")
    expect("RIG-entry", entry_off, "R3 negative: the entry sheaves 3 mm off")

    def wheel_box():
        wx, _wy, wz = spec.wheel
        t, table, co, tris, names = build_rig("drive", mutate=add_box("probebox", wx - 1.80, wx - 1.743, -0.02, 0.02,
                                                                       wz - 0.15, wz - 0.10))
        _n, _fb, vb, _b, _pb = LT._rig_bone_masks_m(t, table, "drive")
        LT.rig_entry_m(co, tris, names, LT.M_SPECS["drive"], vb, "drive")
    expect("RIG-entry", wheel_box, "R3 negative: a static box 3 mm from the turning rim")

    def nose_box():
        # the outer bogie pivot of lane n at +20 deg (local (-0.63 -> rotated), |y| 1.45): clear at rest, hit when pitched
        sp = LT.M_SPECS_SHIPPED["drive"]
        rs = LT._dc.replace(sp, anchor_in=sp.anchor_out)
        T = LT.hold_train_layout_m(rs)
        _O, u, nv, M = LT.hold_train_frame_m(rs, sp.anchor_out[0])
        ax = -(T["main_span"] / 2.0 + LT.RIG_NOSE["shift"])
        az = (T["main_z"] + T["sub_z"]) / 2.0
        c, s = math.cos(math.radians(20.0)), math.sin(math.radians(20.0))
        p = M @ LT._V((c * ax + s * az, -1.45, -s * ax + c * az))
        t, table, co, tris, names = build_rig("drive", mutate=add_box("probebox", p.x - 0.02, p.x + 0.02, p.y - 0.02,
                                                                       p.y + 0.02, p.z - 0.02, p.z + 0.02))
        _n, _fb, vb, _b, _pb = LT._rig_bone_masks_m(t, table, "drive")
        LT.rig_nose_sweep_m(co, tris, names, LT.M_SPECS["drive"], vb, LT.rig_heads_m("drive"))
    expect("RIG-nose", nose_box, "R4 negative: a static box in the outer half's path at +20 deg")

    def shared_vertex():
        def f(parts):
            o = LT._mbox("probebox", 0.0, 0.1, -0.05, 0.05, 0.0, 0.1)
            rim = [ob for ob, _m in parts if ob.name == "sheave_bull_rim"][0]
            w = rim.matrix_world @ rim.data.vertices[0].co
            o.location = (w.x - 0.05, w.y - 0.05, w.z - 0.05)     # its +++ corner on a rim vertex: remove_doubles welds them
            return parts + [(o, "paint1")]
        t, table, co, tris, names = build_rig("drive", mutate=f)
        LT._rig_bone_masks_m(t, table, "drive")
    expect("RIG one bone per vertex", shared_vertex, "R1 negative: a Root box welded to a rim vertex")


def main():
    t0 = time.perf_counter()
    r6_shipped_flags()
    r0_flags_off()
    run_kind("drive")
    run_kind("return")
    negatives()
    LT.set_m_rig(False)
    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_terminal_fg_rig: {len(FAILS)} failed: {FAILS}")
        sys.exit(1)
    print(f"PASS test_terminal_fg_rig: R0 flags off, R1 bones / weights / round trip / posed copies, R2 rope, R2b round 2 "
          f"(S2 / C6 hangers, S3 mark, C1 knuckle, C2 brace), R3 entry "
          f"sheaves, R4 nose joint + pin straps + chair sweep, R5 M1-M21 + budgets on both rigged stations, R6 shipped flags, "
          f"every negative raises "
          f"({N_OK[0]} ok, {dt:.0f} s)")


main()
