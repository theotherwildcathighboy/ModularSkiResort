"""
Tram saddle round gate (DECISIONS 597; docs/research/art/tram-saddle-2026-11/build.md): the letter D tram towers
(tram_saddle.py) on tram_tower's SKINNED path with the D design.

Each case RAISES unless:

  SD1  tram_saddle.run_height holds every gate of the round for the height(s): the C rebuild matches the shipped C LOD0
       (<= 0.2 mm, every loop one UV), D's topology = C's (vertices, loops, polygons, parts: C's UVs 1:1, 0 MB), the
       tram_tower contracts in both frames, G6 over cs2_skin.joint_grid(60, 30) (the grid's pose count, no overlap,
       clearance >= 5 mm, every rope clear and seated on all 20 crowns), G6 at the planner's TS-1 poses (clamped as the
       code clamps them), the rope reach (clear to 60), the negative (C's geometry at the D grid fails G6), C9 budgets
       (the 5 % cap rule), the code's limits (LiftAlignMath.TramSaddlePitchLimitDeg / TramHalfBreakMaxDeg) = the art's;
       and the TS-1 coverage: D follows >= 200 of the 242 towers, C 108.
  SD2  the hooks' defaults rebuild C: tram_tower.SKIN_TRAM = the shipped C manifest's skin design on every key the
       manifest has; half_break 20, joint_d None, roller_out 0 (D's values live in tram_saddle.D_DESIGN only).
  SD3  deterministic: two D builds hash alike (vertices, loops, part table).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_tram_saddle.py [-- --heights all]
"""
import hashlib
import json
import os
import sys
import tempfile

import bpy  # noqa: F401
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_skin  # noqa: E402
import tram_saddle as TS  # noqa: E402
import tram_tower as TT  # noqa: E402


def _fail(msg):
    raise RuntimeError(f"test_tram_saddle FAIL: {msg}")


def sd1(H, out):
    rep = TS.run_height(H, out, negative=True)
    want = len(cs2_skin.joint_grid(TS.SADDLE_MAX, TS.HALF_BREAK))
    g6 = rep["g6"]
    if g6["poses"] != want or g6["failed"] or g6["seat_tops"] != 4 + 2 * len(TT.ROLLER["x"]):
        _fail(f"SD1 tram{H}: G6 {g6['poses']} poses (grid {want}), seat tops {g6['seat_tops']}, failed {g6['failed']}")
    if rep["g6_planner"]["failed"]:
        _fail(f"SD1 tram{H}: planner poses {rep['g6_planner']}")
    cov = rep["coverage"]
    if cov["D"]["inside"] < 200 or cov["C"]["inside"] != 108 or cov["towers"] != 242:
        _fail(f"SD1 tram{H}: coverage {cov}")
    if rep["textures_added_mb"] != 0.0 or not rep.get("negative"):
        _fail(f"SD1 tram{H}: textures {rep['textures_added_mb']} MB, negative {rep.get('negative')}")
    print(f"[saddle-test] SD1 tram{H}: LOD0/1/2 {rep['tris']}, G6 {g6['poses']} poses min "
          f"{g6['min_clear']['m'] * 1000:.1f} mm, planner {rep['g6_planner']['poses']} poses, reach "
          f"{rep['rope_reach']['clear_to_deg']}, coverage C {cov['C']['inside']} / D {cov['D']['inside']}, raised "
          f"{rep['budget']['raised']}")
    return rep


def sd2():
    with open(os.path.join(TS.c_folder(30), "SkiTramTower30C.manifest.json"), encoding="utf-8") as f:
        design = json.load(f)["skin"]["design"]
    TS.use(TS.SOURCE)
    bad = {k: (v, TT.SKIN_TRAM.get(k)) for k, v in design.items()
           if (list(v) if isinstance(v, (list, tuple)) else v) !=
           (list(TT.SKIN_TRAM.get(k)) if isinstance(TT.SKIN_TRAM.get(k), (list, tuple)) else TT.SKIN_TRAM.get(k))}
    hooks = {k: TT.SKIN_TRAM[k] for k in ("half_break", "joint_d", "roller_out")}
    if bad or hooks != {"half_break": cs2_skin.HALF_BREAK_MAX, "joint_d": None, "roller_out": 0.0}:
        _fail(f"SD2: SKIN_TRAM differs from the shipped C design {bad} or the hooks' defaults {hooks}")
    print(f"[saddle-test] SD2: SKIN_TRAM = the shipped C design on {len(design)} keys, hooks at C {hooks}")


def _hash(t, table):
    me = t.data
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    lv = np.zeros(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    h = hashlib.sha256(np.round(co, 6).tobytes())
    h.update(lv.tobytes())
    h.update("|".join(table).encode())
    return h.hexdigest()


def sd3(H):
    args = TS.c_args(H)
    hs = []
    for _ in range(2):
        TT.clear_scene()
        t, table, _spec, _parts, _c = TS.build(H, TS.LETTER, args)
        hs.append(_hash(t, table))
    TS.use(TS.SOURCE)
    if hs[0] != hs[1]:
        _fail(f"SD3 tram{H}: two D builds differ {hs}")
    print(f"[saddle-test] SD3 tram{H}: two D builds hash alike {hs[0][:16]}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    heights = (30,)
    if "--heights" in argv:
        v = argv[argv.index("--heights") + 1]
        heights = TS.HEIGHTS if v == "all" else tuple(int(h) for h in v.split(","))
    sd2()
    out = tempfile.mkdtemp(prefix="tram_saddle_test_")
    for H in heights:
        sd1(H, out)
    sd3(heights[0])
    print(f"PASS test_tram_saddle: SD1 ({', '.join(f'tram{H}' for H in heights)}), SD2, SD3")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        import traceback
        traceback.print_exc()
        sys.exit(1)
