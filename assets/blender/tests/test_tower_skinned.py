"""
Lift alignment phase 2 gate (2026-11-02, DECISIONS 570; docs/research/lift-alignment-2026-11-02.md): the SKINNED line
towers (lift_tower.set_skinned / --skinned), pilot FG12 (Yan head) and DQ12 (Kagelok ingest head).

Builds each pilot skinned (no finish; G6 runs inside build_tower_v2 and raises on a failed pose) and RAISES unless:

  K1  G6 held: five poses (0, +-theta_max / 2, +-theta_max), no train / head overlap, clearance >= SKIN_CLEAR_MIN, the
      main-pin gap <= SKIN_PIN_GAP_MAX with the pin inside its lug, the rope clear; the rope rests on every wheel top
      (2 x sheaves tops, gap SKIN_SEAT_GAP); the Train parts are the family's train prefixes only (no hanger, no main
      pin, no head part) and the main pins sit on the pivot line;
  K2  every level (LOD0, LOD1, LOD2) skinned: vertex groups exactly Root / Train, every vertex 1.0 on one bone
      (cs2_asset.check_skin_weights), Train non-empty on each level;
  K3  the rig: Root at the origin, Train at (0, 0, crown lane y) under Root, both rests the up / roll 0 frame; the
      Armature-modifier pose at +-theta_max is skin_rotate on the Train vertices and leaves the Root vertices
      (skin_posed_copy);
  K4  export: cs2_asset.export_fbx_skinned of the three levels re-imports (skin_roundtrip) with bones Root / Train, Train
      at (0, lane y x 100, 0) cm, identity rests, no unweighted / multi-bone vertex;
  K5  the build is deterministic (two skinned builds hash alike: vertices, faces, part table);
  K6  negatives: a skinned DQ12 with the REBUILD head refuses (no recipe); G6 fails on the DQ12 yoke at its unskinned
      top (SKIN_DQ yoke_above_pin 0.11: the yoke's swept top meets the Kagelok gantry's slanted posts at 42 deg);
  K7  off is off: an unskinned FG12 (set_skinned(False), set_rope_on_crown(False)) has no main pin part and no
      `skin` report.

The DQ12 cases need the gitignored Kagelok .blend; when it is absent they SKIP with a printed line.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_tower_skinned.py
"""
import hashlib
import json
import os
import sys
import tempfile
import time

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_asset  # noqa: E402
import lift_tower as LT  # noqa: E402

PILOTS = (("FG12", None), ("DQ12", "ingest"))


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


def build(key, head):
    LT.clear_scene()
    LT.set_skinned(True)
    spec = LT.VARIANTS[key]
    t, table, _src, info = LT.build_tower_v2(spec, head, finish=False)
    return spec, t, table, info


def k1_g6(spec, info):
    sk = info.get("skin") or _fail(f"{spec.key}: no skin report")
    if len(sk["poses"]) != 5:
        _fail(f"{spec.key} K1: {len(sk['poses'])} poses")
    for r in sk["poses"]:
        if r["overlaps"] or r["clear_m"] < LT.SKIN_CLEAR_MIN or r["pin_gap_m"] > LT.SKIN_PIN_GAP_MAX \
                or r["pin_embed_m"] <= 0.0 or r["rope_overlaps"]:
            _fail(f"{spec.key} K1: pose {r}")
    gaps = [g for runs in sk["seat_gaps_m"].values() for _y, g in runs]
    if len(gaps) != 2 * spec.sheaves or not LT.SKIN_SEAT_GAP[0] <= min(gaps) <= max(gaps) <= LT.SKIN_SEAT_GAP[1]:
        _fail(f"{spec.key} K1: seat {sk['seat_gaps_m']}")
    bad = [n for n in sk["train_parts"] if not n.startswith(LT.SKIN_TRAIN[spec.family])
           or n.startswith(LT.SKIN_ROOT_FIRST + ("hanger",))]
    if bad:
        _fail(f"{spec.key} K1: train parts outside the train prefixes {bad}")
    zp = LT.lane_y(spec, True)
    for nm, p in sk["pins"].items():
        if p["offset_m"] > 1e-4 or abs(p["axis_net"][1] - zp) > 1e-4:
            _fail(f"{spec.key} K1: {nm} off the pivot line {p}")
    if len(sk["pins"]) != 2:
        _fail(f"{spec.key} K1: main pins {sorted(sk['pins'])}")
    print(f"[skin] K1 {spec.key}: G6 ok, clearance min {min(r['clear_m'] for r in sk['poses']) * 1000:.1f} mm, "
          f"seat {min(gaps) * 1000:.2f}..{max(gaps) * 1000:.2f} mm, {len(sk['train_parts'])} train parts")


def k2_k3_k4(spec, t, table):
    lods = LT.lod_chain_v2(t, t.name, spec)
    arm = LT.build_skin_armature(spec, t.name + "Rig")
    for o in [t] + lods:
        w = LT.skin_assign(o, table, spec.family)
        if sorted(vg.name for vg in o.vertex_groups) != sorted(LT.SKIN_BONES) or not w["Train"] or not w["Root"]:
            _fail(f"{spec.key} K2 {o.name}: groups {[vg.name for vg in o.vertex_groups]} weights {w}")
        cs2_asset.check_skin_weights(o, arm)
    zp = LT.lane_y(spec, True)
    b = arm.data.bones
    if (tuple(b["Root"].head_local) != (0.0, 0.0, 0.0) or abs(b["Train"].head_local.z - zp) > 1e-5
            or b["Train"].parent is None or b["Train"].parent.name != "Root"):
        _fail(f"{spec.key} K3: rig {[(x.name, tuple(x.head_local)) for x in b]}")
    for th in (LT.SKIN_THETA_MAX[spec.family], -LT.SKIN_THETA_MAX[spec.family]):
        ob = LT.skin_posed_copy(t, arm, th, spec, "K3Pose")       # raises unless it is the G6 pose
        me = ob.data
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.meshes.remove(me)
    d = tempfile.mkdtemp(prefix="skin_k4_")
    name = t.name
    cs2_asset.export_fbx_skinned(t, arm, os.path.join(d, f"{name}.fbx"))
    for i, lod in enumerate(lods, start=1):
        cs2_asset.export_fbx_skinned(lod, arm, os.path.join(d, f"{name}_LOD{i}.fbx"))
    rt = LT.skin_roundtrip(d, name, len(lods), spec)              # raises on a failed level
    print(f"[skin] K2-K4 {spec.key}: {len(lods) + 1} levels skinned, posed copies = G6 pose, round trip "
          f"{ {k: v['groups'] for k, v in rt.items()} }")


def k6_negatives():
    LT.clear_scene()
    LT.set_skinned(True)
    try:
        LT.build_tower_v2(LT.VARIANTS["DQ12"], "rebuild", finish=False)
    except LT.TowerContractError as e:
        print(f"[skin] K6 rebuild head refused: {str(e)[:90]}")
    else:
        _fail("K6: a skinned DQ12 rebuild head built")
    old = dict(LT.SKIN_DQ)
    LT.SKIN_DQ["yoke_above_pin"] = 0.11
    try:
        build("DQ12", "ingest")
    except LT.TowerContractError as e:
        print(f"[skin] K6 the unskinned yoke top fails G6: {str(e)[:110]}")
    else:
        _fail("K6: G6 passed the DQ12 yoke at its unskinned top")
    finally:
        LT.SKIN_DQ.clear()
        LT.SKIN_DQ.update(old)


def k7_off():
    LT.clear_scene()
    LT.set_skinned(False)
    LT.set_rope_on_crown(False)
    t, table, _src, info = LT.build_tower_v2(LT.VARIANTS["FG12"], None, finish=False)
    if any(n.startswith(LT.SKIN_ROOT_FIRST) for n in table) or "skin" in info or t.vertex_groups:
        _fail(f"K7: unskinned FG12 carries skin pieces ({[n for n in table if n.startswith('pivot_main')]})")
    print(f"[skin] K7 unskinned FG12: {len(table)} parts, no main pin, no skin report")


def main():
    t0 = time.perf_counter()
    done = []
    for key, head in PILOTS:
        if head == "ingest" and not os.path.isfile(LT.KAGELOK_BLEND):
            print(f"[skin] SKIP {key} ingest: {LT.KAGELOK_BLEND} absent (gitignored original)")
            continue
        spec, t, table, info = build(key, head)
        k1_g6(spec, info)
        h1 = _hash(t, table)
        k2_k3_k4(spec, t, table)
        _s, t2, table2, _i = build(key, head)
        if _hash(t2, table2) != h1:
            _fail(f"{key} K5: two skinned builds differ")
        print(f"[skin] K5 {key}: deterministic ({h1[:12]})")
        done.append(key)
    if os.path.isfile(LT.KAGELOK_BLEND):
        k6_negatives()
    k7_off()
    print(f"PASS test_tower_skinned: {done} skinned (G6, three skinned levels, round trip, determinism), negatives, "
          f"off is off ({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
