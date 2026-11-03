"""
Lift alignment phase 2 gate, round 2 (2026-11-02; docs/research/field/2026-11-02/liftalign/articulation-design-note.md,
option C): hinged HALF trains. The SKINNED towers: FG12 (Yan), DQ12 (Kagelok ingest), GD12 (rebuild, SKIN_GD sheaves a
side) through lift_tower.set_skinned, and the tram SkiTramTower30A through tram_tower.set_skinned (saddle halves +
haul-row halves on their own anchor bones).

Each build RAISES unless:

  K1  G6 held (cs2_skin.sweep over cs2_skin.joint_grid(theta_max): both halves in {-t, -t/2, 0, t/2, t} with
      |front - back| <= 40, plus the +-20 deg half-breaks at every mean): no overlap half vs head / half vs half,
      clearance >= SKIN_CLEAR_MIN, the main-pin gap <= SKIN_PIN_GAP_MAX, the rope runs clear, every half's rope seated
      on all its crowns (SEAT_GAP) at every pose; the pose count is the grid's;
  K2  every level (LOD0, LOD1, LOD2) skinned: vertex groups exactly the bones, every vertex 1.0 on one bone
      (cs2_asset.check_skin_weights), both main halves non-empty at LOD0;
  K3  the rig: Root at the origin, every half bone under Root at its pivot with the up / roll 0 rest; the
      Armature-modifier pose at a crest (front +15 / back -15) and a dip is the sweep's pose (cs2_skin.posed_copy);
  K4  export: cs2_asset.export_fbx_skinned of the three levels re-imports (cs2_skin.roundtrip) with exactly the bones at
      the pivots (game cm), identity rests, one bone per vertex, and THE AXIS TEST: in the raw file frame (z = game z)
      every TrainFront* group's bbox center at z > 0 and every TrainBack* group's at z < 0 (game +z = net -Y =
      authoring +X: cs2_asset.py:40, :1484-1487, PIPELINE rule T);
  K5  deterministic (two skinned builds hash alike: vertices, faces, part table);
  K6  negatives: a skinned DQ12 rebuild head refuses (no recipe); G6 fails on the DQ12 yoke at its unskinned top
      (yoke_above_pin 0.11); a flipped travel sign (cs2_skin.FRONT_SIGN_NET +1) trips the build-time axis test
      (skin_part_bones: the _f halves must ride TrainFront); seat_half refuses ray counts that miss the rope's corners;
  K7  off is off: unskinned FG12 and tram30 carry no main pin / saddle halves, no `skin` report, no vertex groups.

The DQ12 cases need the gitignored Kagelok .blend; when it is absent they SKIP with a printed line.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_tower_skinned.py
"""
import hashlib
import json
import math
import os
import sys
import tempfile
import time

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_asset  # noqa: E402
import cs2_skin  # noqa: E402
import lift_tower as LT  # noqa: E402
import tram_tower as TT  # noqa: E402

LIFTS = (("FG12", None), ("DQ12", "ingest"), ("GD12", "rebuild"))


def _fail(msg):
    raise AssertionError(msg)


def _hash(t, table):
    h = hashlib.sha1()
    for v in t.data.vertices:
        h.update(("%.5f %.5f %.5f;" % tuple(v.co)).encode())
    for p in t.data.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    h.update(json.dumps(table).encode())
    return h.hexdigest()


# --------------------------------------------------------------------------------------
# lift towers
# --------------------------------------------------------------------------------------

def build_lift(key, head, sweep=True):
    LT.clear_scene()
    LT.set_skinned(True)
    real = LT.skin_sweep
    if not sweep:
        LT.skin_sweep = lambda *a, **k: {}
    try:
        t, table, _src, info = LT.build_tower_v2(LT.VARIANTS[key], head, finish=False)
    finally:
        LT.skin_sweep = real
    return info["spec"], t, table, info


def k1_lift(spec, info):
    sk = info.get("skin") or _fail(f"{spec.key}: no skin report")
    want = len(cs2_skin.joint_grid(LT.SKIN_THETA_MAX[spec.family]))
    if len(sk["poses"]) != want:
        _fail(f"{spec.key} K1: {len(sk['poses'])} poses, the grid has {want}")
    try:
        cs2_skin.check_rows(sk["poses"], spec.key, LT.SKIN_CLEAR_MIN, LT.SKIN_PIN_GAP_MAX, seat_tops=2 * spec.sheaves,
                            seat_gap=LT.SKIN_SEAT_GAP)
    except cs2_skin.SkinError as e:
        _fail(f"{spec.key} K1: {e}")
    tp = sk["train_parts"]
    if not tp["TrainFront"] or len(tp["TrainFront"]) != len(tp["TrainBack"]):
        _fail(f"{spec.key} K1: halves {[(b, len(v)) for b, v in tp.items()]}")
    bad = [n for v in tp.values() for n in v if not n.startswith(LT.SKIN_TRAIN[spec.family])
           or n.startswith(LT.SKIN_ROOT_FIRST)]
    if bad:
        _fail(f"{spec.key} K1: train parts outside the train prefixes {bad[:6]}")
    print(f"[skin] K1 {spec.key}: G6 {len(sk['poses'])} poses ok, min clearance {sk['min_clear']['m'] * 1000:.1f} mm "
          f"({sk['min_clear']['parts']}), {len(tp['TrainFront'])} + {len(tp['TrainBack'])} half parts")


def k2_k4_lift(spec, t, table, info):
    lods = LT.lod_chain_v2(t, t.name, spec)
    arm = LT.build_skin_armature(spec, t.name + "Rig")
    for i, o in enumerate([t] + lods):
        w = LT.skin_assign(o, table, info["skin_part_bone"])
        if sorted(vg.name for vg in o.vertex_groups) != sorted(LT.SKIN_BONES) or (i == 0 and not (
                w["TrainFront"] and w["TrainBack"] and w["Root"])):
            _fail(f"{spec.key} K2 {o.name}: groups {[vg.name for vg in o.vertex_groups]} weights {w}")
        cs2_asset.check_skin_weights(o, arm)
    heads = LT.skin_heads(spec)
    _rig(spec.key, arm, heads)
    piv = {b: LT.skin_pivot(spec) for b in LT.SKIN_BONES[1:]}
    for pose in ({"TrainFront": 15.0, "TrainBack": -15.0}, {"TrainFront": -15.0, "TrainBack": 15.0}):
        ob = cs2_skin.posed_copy(t, arm, pose, piv, "K3Pose")      # raises unless it is the G6 pose
        me = ob.data
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.meshes.remove(me)
    rt = _export_roundtrip(t, lods, arm, lambda d, n, lv: LT.skin_roundtrip(d, n, lv, spec))
    print(f"[skin] K2-K4 {spec.key}: {len(lods) + 1} levels skinned, posed copies = G6 poses, round trip + axis "
          f"{ {k: v['group_z_center_cm'] for k, v in rt.items()} }")


def _rig(label, arm, heads):
    b = arm.data.bones
    if [x.name for x in b] != list(heads):
        _fail(f"{label} K3: bones {[x.name for x in b]} != {list(heads)}")
    for nm, h in heads.items():
        if max(abs(a - c) for a, c in zip(b[nm].head_local, h)) > 1e-5:
            _fail(f"{label} K3: {nm} head {tuple(b[nm].head_local)} != {h}")
        if nm != "Root" and (b[nm].parent is None or b[nm].parent.name != "Root"):
            _fail(f"{label} K3: {nm} not under Root")


def _export_roundtrip(t, lods, arm, roundtrip):
    d = tempfile.mkdtemp(prefix="skin_k4_")
    name = t.name
    cs2_asset.export_fbx_skinned(t, arm, os.path.join(d, f"{name}.fbx"))
    for i, lod in enumerate(lods, start=1):
        cs2_asset.export_fbx_skinned(lod, arm, os.path.join(d, f"{name}_LOD{i}.fbx"))
    rt = roundtrip(d, name, len(lods))                            # raises on a failed level (axis test included)
    for lv in rt.values():
        for g, z in lv["group_z_center_cm"].items():
            if (g.startswith("TrainFront") and not z > 0.0) or (g.startswith("TrainBack") and not z < 0.0):
                _fail(f"K4 axis: {lv['file']} group {g} raw z center {z} cm")
    return rt


# --------------------------------------------------------------------------------------
# tram
# --------------------------------------------------------------------------------------

def build_tram(sweep=True):
    TT.set_skinned(True)
    TT.clear_scene()
    t, table, spec, parts, lay, _s = TT.build_variant("tram", 30)
    t.name = "SkiTramTower30A"
    TT.check_contracts(t, table, spec, parts, "authoring")
    cs2_asset.orient_for_net(t)
    TT.check_contracts(t, table, spec, parts, "net")
    pb = TT.skin_part_bones(t, table)
    rep = TT.skin_sweep(t, table, spec, pb) if sweep else None
    return spec, t, table, parts, pb, rep


def k1_tram(spec, rep, pb):
    want = len(cs2_skin.joint_grid(TT.SKIN_TRAM["theta_max"]))
    if len(rep["poses"]) != want or rep["failed"]:
        _fail(f"tram K1: {len(rep['poses'])} poses (grid {want}), failed {rep['failed']}")
    if rep["seat_tops"] != 4 + 2 * len(TT.ROLLER["x"]):
        _fail(f"tram K1: {rep['seat_tops']} seat tops (want 2 lanes x (2 saddle halves + {len(TT.ROLLER['x'])} rollers))")
    cnt = {b: sum(1 for v in pb.values() if v == b) for b in TT.SKIN_BONES[1:]}
    if not (cnt["TrainFront"] == cnt["TrainBack"] > 0 and cnt["TrainFrontHaul"] == cnt["TrainBackHaul"] > 0):
        _fail(f"tram K1: halves {cnt}")
    if any(not n.startswith(("saddle_", "haulroll_")) for n in pb):
        _fail(f"tram K1: half parts outside saddle_ / haulroll_ {[n for n in pb if not n.startswith(('saddle_', 'haulroll_'))]}")
    print(f"[skin] K1 tram30: G6 {len(rep['poses'])} poses ok to theta_max {TT.SKIN_TRAM['theta_max']}, min clearance "
          f"{rep['min_clear']['m'] * 1000:.1f} mm ({rep['min_clear']['parts']}), seat {rep['seat_tops']} crowns, {cnt}")


def k2_k4_tram(spec, t, table, parts, pb):
    l1p, l2p = TT.lod_lists("tram")
    n1, n2 = TT.count_after_delete(t, table, l1p), TT.count_after_delete(t, table, l2p)
    lods = TT.make_lods(t, t.name, table, parts, "tram", n1, n2)
    arm = TT.build_skin_armature(spec, t.name + "Rig")
    for i, o in enumerate([t] + lods):
        w = TT.skin_assign(o, table, pb)
        if sorted(vg.name for vg in o.vertex_groups) != sorted(TT.SKIN_BONES) or (i == 0 and not all(w.values())):
            _fail(f"tram K2 {o.name}: groups {[vg.name for vg in o.vertex_groups]} weights {w}")
        if i > 0 and (w["TrainFrontHaul"] or w["TrainBackHaul"]):
            _fail(f"tram K2 {o.name}: haul halves at LOD{i} (the rollers leave at LOD1) {w}")
        cs2_asset.check_skin_weights(o, arm)
    _rig("tram30", arm, TT.skin_heads(spec))
    piv = TT.skin_pivots(spec)
    for pose in (TT.skin_pose(15.0, -15.0), TT.skin_pose(TT.SKIN_TRAM["theta_max"], TT.SKIN_TRAM["theta_max"])):
        ob = cs2_skin.posed_copy(t, arm, pose, piv, "K3Pose")
        me = ob.data
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.meshes.remove(me)
    rt = _export_roundtrip(t, lods, arm, lambda d, n, lv: TT.skin_roundtrip(d, n, lv, spec))
    print(f"[skin] K2-K4 tram30: {len(lods) + 1} levels skinned (5 bones), posed copies = G6 poses, round trip + axis "
          f"{ {k: v['group_z_center_cm'] for k, v in rt.items()} }")


# --------------------------------------------------------------------------------------
# negatives, off is off
# --------------------------------------------------------------------------------------

def k6_negatives(have_kagelok):
    if have_kagelok:
        LT.clear_scene()
        LT.set_skinned(True)
        try:
            LT.build_tower_v2(LT.VARIANTS["DQ12"], "rebuild", finish=False)
        except LT.TowerContractError as e:
            print(f"[skin] K6 DQ12 rebuild head refused: {str(e)[:90]}")
        else:
            _fail("K6: a skinned DQ12 rebuild head built")
        old = dict(LT.SKIN_DQ)
        LT.SKIN_DQ["yoke_above_pin"] = 0.11
        try:
            build_lift("DQ12", "ingest")
        except LT.TowerContractError as e:
            print(f"[skin] K6 the unskinned yoke top fails G6: {str(e)[:110]}")
        else:
            _fail("K6: G6 passed the DQ12 yoke at its unskinned top")
        finally:
            LT.SKIN_DQ.clear()
            LT.SKIN_DQ.update(old)
    old = cs2_skin.FRONT_SIGN_NET
    cs2_skin.FRONT_SIGN_NET = -old
    try:
        build_lift("FG12", None, sweep=False)
    except LT.TowerContractError as e:
        print(f"[skin] K6 flipped travel sign trips the axis test: {str(e)[:110]}")
    else:
        _fail("K6: a flipped FRONT_SIGN_NET built (the axis test did not fire)")
    finally:
        cs2_skin.FRONT_SIGN_NET = old
    try:
        cs2_skin.seat_half(np.zeros((1, 3)), [], [0], (0.0, 0.0), 0.0, 0.0, 1.0, 1.0, 0.1, 0.0866, nx=15)
    except ValueError as e:
        print(f"[skin] K6 seat_half nx 15 refused: {str(e)[:80]}")
    else:
        _fail("K6: seat_half took nx 15 (misses the rope's bottom corners)")


def k7_off():
    LT.clear_scene()
    LT.set_skinned(False)
    LT.set_rope_on_crown(False)
    t, table, _src, info = LT.build_tower_v2(LT.VARIANTS["FG12"], None, finish=False)
    if any(n.startswith(LT.SKIN_ROOT_FIRST) for n in table) or "skin" in info or t.vertex_groups:
        _fail(f"K7: unskinned FG12 carries skin pieces ({[n for n in table if n.startswith('pivot_main')]})")
    TT.set_skinned(False)
    TT.clear_scene()
    t, table, *_r = TT.build_variant("tram", 30)
    if any(n.endswith(("_f", "_b")) and n.startswith(("saddle", "haulroll_bar")) for n in table) or t.vertex_groups:
        _fail("K7: unskinned tram30 carries saddle / haul halves")
    if "saddle_p" not in table or "sbrk_p_0" not in table:
        _fail("K7: unskinned tram30 lost its one-piece saddle / seat posts")
    print(f"[skin] K7 unskinned FG12 / tram30: no main pin, no halves, no skin report, no vertex groups")


def main():
    t0 = time.perf_counter()
    have_k = os.path.isfile(LT.KAGELOK_BLEND)
    done = []
    for key, head in LIFTS:
        if head == "ingest" and not have_k:
            print(f"[skin] SKIP {key} ingest: {LT.KAGELOK_BLEND} absent (gitignored original)")
            continue
        spec, t, table, info = build_lift(key, head)
        k1_lift(spec, info)
        h1 = _hash(t, table)
        k2_k4_lift(spec, t, table, info)
        if key == "FG12":
            _s, t2, table2, _i = build_lift(key, head, sweep=False)
            if _hash(t2, table2) != h1:
                _fail(f"{key} K5: two skinned builds differ")
            print(f"[skin] K5 {key}: deterministic ({h1[:12]})")
        done.append(spec.key)
    spec, t, table, parts, pb, rep = build_tram()
    k1_tram(spec, rep, pb)
    h1 = _hash(t, table)
    k2_k4_tram(spec, t, table, parts, pb)
    _s, t2, table2, *_r = build_tram(sweep=False)
    if _hash(t2, table2) != h1:
        _fail("tram30 K5: two skinned builds differ")
    print(f"[skin] K5 tram30: deterministic ({h1[:12]})")
    done.append("tram30")
    k6_negatives(have_k)
    k7_off()
    print(f"PASS test_tower_skinned: {done} skinned (G6 joint sweep, three skinned levels, round trip + axis test, "
          f"determinism), negatives, off is off ({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
