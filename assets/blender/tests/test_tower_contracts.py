"""
Geometry contracts for the lift_tower v2 port (towers-v2.md section 14.2-14.4, with the
skeptic's MUST-CHANGE items), checked INDEPENDENTLY of the generator's own asserts:

1. All eleven variants - Revision 1 added FG20 / DQ16 (verdict 5) - (DQ line variants
   with both heads when the Kagelok .blend is present),
   no bake: axle = mast - 0.96 (FG), lanes from the sheave centers in the NET frame
   (gauge = X: +-1.8 / +-2.6), bounds top per variant from a hard-coded table (the
   top-anchor spec y; DQ ingest 1.9501 above the axle, M7), bounds min = -pier, post-orient
   lateral +-1.91 FG / +-2.993 DQ (+-0.01), LOD1 = 0 and LOD2 bounds-top drift (FG 0 with
   rail_catwalk_top kept - M5; DQ 0.024..0.031 = beam top, endplates deleted), LOD budgets,
   pad clearance (crash pad inner r >= mast r + 0.03; no flange / bolt vertex inside the
   shell - M8), names (validate_asset_name + cs2_validate.lint_asset_name), ETDQ hosted
   inboard end <= -8.3 (M4); DQ: no grate_/post_/rail_/kick_ vertex in the hanger column
   |gauge| > lane - 0.45 between rope - 1.70 and rope + 0.22 on every LOD (review F2).
   Revision 1: review F3 - the crash pad sits 5 mm above the base-flange plate and no flange /
   bolt triangle crosses a pad triangle on LOD0 / LOD1 (BVH); review F5 - DQ LOD2 keeps both
   deck plates and, on rebuild heads, the four A-braces; node-elevation scoring (pillar-system
   Q1) over FG8-20 / DQ10-16 switches at 8/3, 20/3, 32/3 and 4/3, 10/3, 16/3 m and grounds up
   to 16 / 12 m, and lift_tower.pillar_scoring (the manifest rows) agrees.
2. Wrong builds FAIL: flange r 0.70 (M8 / C14), rocker_sub back at 2.517 (F9), rails
   unsplit through the portal (F10 / C17), a spec top 1 cm off (C3), ETDQ with 10 sheaves
   (M4 / C13), FG LOD2 deleting rail_catwalk_top (M5 / C12), DQ decks 2.492 + rails 2.30 and
   rails 2.30 alone in the hanger column (review F2 / C18), the pre-F3 pad from z 0.15 on FG8 and
   DQ12 (C14 triangle test), the pre-F5 ETDQ LOD2 list (C12 F5).
3. Bake (FG8 at A = 512 through cs2_bake, AO 8 samples, temp dir): 4 PNGs, BaseColor A 255,
   MaskMap B 0, ControlMask R + G <= 1 + 1e-3 and A 0, 95th percentile BaseColor under ctl
   R in [0.78, 0.82] (RECOLOR_ALBEDO), warning-triangle texels (BaseColor >= 0.85 inside the
   crashpad_outer island) have ctl R = G = 0.
4. main_v2 (towers-v2 section 14.6, pipeline review 7): FG8 with and without --review at
   --atlas 512 --ao-samples 4 in one process; every asset file byte-identical (FBX clock frozen),
   manifests equal without written_utc, the pack outside the asset folder.
5. Revision 1b (revision1-check-towers item 6): the gondola tubular family GD12 / GD16 / GD20
   (rebuild head) through every check above, plus: axle = top - 2.000 and tube top = axle + 0.50
   (the lattice's H_top_GD, NOT the FG mast - 0.96), comm = axle + 1.20, lanes +-3.0; the gd8
   hanger column (|x| <= lane - 0.81 from rope + 0.08 down to rope - 2.10, <= lane - 1.10 over the
   cabin body rope - 1.92..- 4.50) on every LOD; the gondola carrier boxes swept on both lanes
   with the hanger outboard (only the jaw may pass the sheaves) and the cabins.md 8.3 head rule;
   the comm wire's corridor empty; lift_tower's GD constants == tram_tower.GD / CARRIER_GD /
   GD_HEAD_RULE / SHEAVE / STUB_LANES == catalog/carriers.json gd8; GD-group scoring with the
   lattice 25 / 32 / 40 (switches 8/3, 20/3, 34/3, 53/3, 76/3; tubular float <= 10/3 < pier 4).
   Wrong builds: the GD deck at the DQ rule lane - 0.45 (C18), the carriers at the plain 0.40
   stretch (C20 head rule), the comm wire into the tube top (C19), the 0.56 pad on the 0.50 mast
   (C8), a GD top 1 cm off (C3).
6. Revision 1c (revision1b-check-gd-towers section B): every sub-rocker HANGS - each sub pivot
   pin's two ends sit in a rocker_ lug (its own sub outboard; a main or, for the lone middle sub
   of a GD train, a rocker_link_ inboard) and that inboard lug reaches the yoke (a link buried in
   it, a main within the 0.05 m its own pin bridges), measured here on the net-frame part boxes
   (independent of lift_tower.check_sub_hangers / C21). Wrong builds: GD12 without the link (the
   shipped-before middle sub, 0.125 m under the yoke with an open pin) and a link stopping 3 cm
   short of the yoke - both must fail on C21.
7. Detail pass 1 (design/detail-pass-1-towers.md section 6 with the skeptic's MUST-CHANGE items;
   DECISIONS 171): every variant incl. the new DQ18 and ETGD - DT-C2 LOD0 == the measured ledger
   (and within 1 % of the design ledger), DT-C3 same-facing coplanar <= 1 cm2 per part pair on
   LOD0 / 1 / 2 and DT-C4 no hidden contact triangle (an independent scanner, the cabin G12
   tolerances), DT-C5 texel anisotropy (area-weighted median <= 1.05, p95 <= 1.35 on "UVMap"),
   DT-C6 detail parts (counts, LOD fates, plates 4-8 mm proud with rims 1-3 mm in, BVH: no
   non-host overlap), M1 (mast islands fit, the FG16 / FG20 split), M3 (each plate's +u = the
   viewer's right), M9 (conduit standoff / foot); ETGD inside the gondola base station's keep-out,
   on its rope / lane / pose, and clear of its geometry; DQ18 scoring (post-freeze family).
   Wrong builds: the pre-pass FG hanger top and GD12 brace a 0.186 (DT-C3), a create_cone mast
   UV (DT-C5). Bake (FG8 / DQ16 / GD20 at the shipping atlas): DT-C7 stamps on the PNGs, DT-C8 /
   C9 / C10 from the manifest, DT-C9 decorrelated twins, G5 ControlMask weather on == off.
8. Follow-up 2026-10-11 (DQ20 + sub-texel faces). (a) DQ20 (top 19.950, axle 18.0, comm 19.2, pier 6,
   lanes +-2.6) ships ALONGSIDE DQ18 under the per-family LOD0 cap table LOD0_CAP (DQ 3,600 after
   the "no need to hold the cap on the towers"; FG / GD stay 3,500): the table itself, every
   spec's cap, DQ20's measured 3,554 / 3,534 against it, scoring with both members (stage 2: switch
   points 4/3 .. 28/3, DQ20 wins 9.333-16, grounds to 16 = the FG line) while the DQ18 stage-1 family
   stays frozen. Wrong build: DQ20 held to the old 3,500 cap (C11).
   (b) F3: no triangle of the WRITTEN FBX with a UV area under 4 texel2 (every face
   under 1 texel included) samples black in the written BaseColor, measured on the FBX import and
   the PNG (independent of lift_tower.uv_triangles / subtexel_black_faces) on DQ18 and GD16 (the
   worst: 24 / 32 black before), DQ20 and every other bake; the fill stays out of variants without
   the defect (FG8 fills 0). Wrong build: the fill disabled -> the generator's DT-F3 gate raises AND
   the independent counter flags >= 20 black triangles on DQ18.
9. F3b (2026-10-11, the checker's finding F-1; supersedes the "under 4 texel2" class of 8b): the
   contract has NO UV-area limit. DQ18 / GD16 / DQ20 / FG8 / ET68 are baked WITH the LOD chain and
   the written LOD0, LOD1 and LOD2 FBX are imported: no triangle of any UV area has a black sample
   (sRGB max < 0.02) among the checker's 400 (a 20 x 20 midpoint grid folded into the triangle) on
   the written BaseColor; the generator agrees (black_after 0 at all three LODs, nothing
   unfillable); the F3 footprint pass and the F3b any-area pass each ran exactly where there was
   something to fill (FG8 only F3b: its 16 platerim slivers; ET68, no defect, neither). Wrong
   builds: the fill disabled (DQ18, >= 20 flagged) and ONLY the F3 pass running (DQ20: the long
   fall-arrest channel slivers stay black, flagged by the generator's gate and the counter).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_tower_contracts.py
"""
import collections
import dataclasses
import json
import math
import os
import shutil
import sys
import tempfile
import time

import bpy
import mathutils
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_asset  # noqa: E402
import cs2_compose  # noqa: E402
import cs2_validate  # noqa: E402
import lift_tower as LT  # noqa: E402

TOPS = {"FG8": 9.014, "FG12": 13.014, "FG16": 17.014, "FG20": 21.014, "ET62": 7.214, "ET68": 7.814,
        "DQ10": 9.950, "DQ12": 11.950, "DQ14": 13.950, "DQ16": 15.950, "ETDQ": 5.650,
        "GD12": 12.0, "GD16": 16.0, "GD20": 20.0,
        "DQ18": 17.950, "ETGD": 6.850, "DQ20": 19.950}
# detail pass 1 (DECISIONS 171): DQ18 (the transfer audit's fallback - DQ20 measured 3,554 tris > 3,500)
# and the gondola entry compression tower ETGD (rope 4.85, H_top 2.000)
# ETDQ 3.70 = the DQ base station lane (integration 2026-10-11; was 3.95, see lift_tower DQ_ENTRY_ROPE_Y)
# FG20 / DQ16: Revision 1 (verdict 5) - one taller line member per family
# GD12/16/20: Revision 1b - axle = top - H_top_GD 2.000 (tram_tower.GD; the gondola piece lane 10.0)
AXLES = {"FG8": 7.04, "FG12": 11.04, "FG16": 15.04, "FG20": 19.04, "ET62": 5.24, "ET68": 5.84,
         "DQ10": 8.0, "DQ12": 10.0, "DQ14": 12.0, "DQ16": 14.0, "ETDQ": 3.70,
         "GD12": 10.0, "GD16": 14.0, "GD20": 18.0, "DQ18": 16.0, "ETGD": 4.85, "DQ20": 18.0}
PIERS = {"FG": 4.0, "DQ": 6.0, "ETDQ": 4.0, "GD": 4.0}
LATERAL = {"FG": 1.91, "DQ": 2.993, "GD": 3.393}      # GD: end plates 2.957 + 0.40 + 0.036
# review F3: the crash pad sits 5 mm above the base-flange plate top (FG plate 0.15-0.19, DQ 0.15-0.20)
PAD_Z0 = {"FG": 0.195, "DQ": 0.205, "GD": 0.205}
PAD_R = {"FG": 0.56, "DQ": 0.56, "GD": 0.61}          # crash-pad shell outer r (t 0.06)
# hanger column bands per family: (lateral half-width, rope + z0, rope + z1); GD from the gd8 cabin
COLUMN = {"DQ": [(0.45, -1.70, 0.22)], "GD": [(0.81, -2.10, 0.08), (1.10, -4.50, -1.92)]}
DECK_Y1 = {"DQ": 2.15, "GD": 2.19}
# pillar-system Q1 scoring with spec y = the shortest member's top: expected switch points and
# the node elevation up to which the tallest member's pier still reaches the terrain
SWITCH = {"FG": [8 / 3, 20 / 3, 32 / 3], "DQ": [4 / 3, 10 / 3, 16 / 3],
          "GD": [8 / 3, 20 / 3, 34 / 3, 53 / 3, 76 / 3]}
GROUNDED = {"FG": 16.0, "DQ": 12.0, "GD": 36.0}
# detail pass 1: the DQ family WITH DQ18 (the DQ18 manifest's placeholder_row_new; the frozen
# manifests keep the scoring above)
SWITCH_DQ18 = [4 / 3, 10 / 3, 16 / 3, 22 / 3]
GROUNDED_DQ18 = 14.0
# follow-up 2026-10-11: the DQ family with DQ18 AND DQ20 (the DQ20 manifest, stage 2): DQ20 wins 9.333-16
SWITCH_DQ20 = [4 / 3, 10 / 3, 16 / 3, 22 / 3, 28 / 3]
GROUNDED_DQ20 = 16.0
# per-family LOD0 caps (lift_tower.LOD0_CAP): RS section 4 3,500; DQ raised to 3,600 for DQ20
CAPS = {"FG": 3800, "DQ": 4250, "GD": 5000}     # DQ 4,200 -> 4,250 (family round: skinned DQ20 + comm hanger 4,250); GD 4,800 -> 5,000 (skinned GD20 at 10 a side 4,938); FG 3,700 -> 3,800 (FG20 3,762); T62 round 3 (<= 120-tri wheels) 3,700 / 4,200 / 4,800; round 2 4,600 / 6,400 / 7,500, round 1 3,500 / 3,600 / 3,500
# LOD0 after detail pass 1 (shipped heads; deterministic) and the design ledger (section 3.2 + M1)
LEDGER = {"FG8": 2762, "FG12": 2946, "FG16": 3170, "FG20": 3258, "ET62": 2722, "ET68": 2730,
          "DQ10": 3138, "DQ12": 3290, "DQ14": 3346, "DQ16": 3394, "DQ18": 3498, "ETDQ": 3010,
          "GD12": 3150, "GD16": 3254, "GD20": 3462, "ETGD": 2610,
          "DQ20": 3554}                     # follow-up: ingest head (rebuild 3,534), cap 3,600
DESIGN_LEDGER = {"FG8": 2762, "FG12": 2946, "FG16": 3170, "FG20": 3258, "ET62": 2722, "ET68": 2730,
                 "DQ10": 3148, "DQ12": 3300, "DQ14": 3356, "DQ16": 3404, "ETDQ": 3034,
                 "GD12": 3154, "GD16": 3258, "GD20": 3466}
# T62 (2026-10-18, build/T62-sheave-wheels.md): with cs2_sheaves.WHEELS "v2" (the default) every wheel is the round-3
# wheel: 76 tris + 22 per SEEN hub boss (cs2_sheaves.wheel_tris; measured by cull_hidden_bosses): the FG / ET wheels show
# both faces (120), the DQ / GD gantry wheels hide the inboard face behind the sub rocker (98). v1 built FG 72 + a 24-tri
# kit axle cap, DQ 72, GD 44: + 2 x sheaves x (new - v1) per tower. `--wheels v1` keeps the ledgers above.
T62_V1_WHEEL = {"FG": 96, "DQ": 72, "GD": 44}
T62_SEEN_BOSSES = {"FG": 2, "DQ": 1, "GD": 1}


def _t62(table):
    import cs2_sheaves
    if not cs2_sheaves.wheels_v2():
        return table
    fam = lambda k: LT.VARIANTS[k].family   # noqa: E731
    return {k: v + 2 * LT.VARIANTS[k].sheaves * (cs2_sheaves.wheel_tris(T62_SEEN_BOSSES[fam(k)]) - T62_V1_WHEEL[fam(k)])
            for k, v in table.items()}


LEDGER, DESIGN_LEDGER = _t62(LEDGER), _t62(DESIGN_LEDGER)
# the gondola placeholder group's lattice members (tram_tower.py, shipped in towers/O)
GD_LATTICE_TOPS = {"Lattice25": 25.0, "Lattice32": 32.0, "Lattice40": 40.0}
NAMES = {k: f"SkiLiftTower{k}O" for k in TOPS}
FAILS = []


def check(cond, label):
    print(f"[contracts] {'ok  ' if cond else 'FAIL'} {label}")
    if not cond:
        FAILS.append(label)


def coords(me):
    buf = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", buf)
    return buf.reshape(-1, 3).astype(np.float64)


def tri_overlap(obj, table, prefixes_a, prefixes_b):
    """Sorted {(part a, part b)} whose faces intersect (BVH, independent of lift_tower's helper)."""
    from mathutils.bvhtree import BVHTree
    me = obj.data
    q = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", q)
    co = [tuple(v) for v in coords(me)]
    sets = []
    for pre in (prefixes_a, prefixes_b):
        polys, names = [], []
        for p, k in zip(me.polygons, q):
            if table[k - 1].startswith(pre):
                polys.append(tuple(p.vertices))
                names.append(table[k - 1])
        sets.append((BVHTree.FromPolygons(co, polys, epsilon=0.0) if polys else None, names))
    (a, an), (b, bn) = sets
    if a is None or b is None:
        return []
    return sorted({(an[i], bn[j]) for i, j in a.overlap(b)})


def scoring_case():
    """Revision 1 (verdict 5): the line members are scored per node elevation (pillar-system Q1,
    SubObjectSystem PillarType.Standalone): s = size.y - (e + y), size.y = bounds top, y = the
    line NetSubObjects spec y (the shortest member's top), score 1 / (1 + (s >= 0 ? 2 s : -s)).
    Brute force over e in [0, 20] m: the winner changes at the hard-coded switch points, the
    tallest member's pier reaches the terrain up to GROUNDED, and lift_tower.pillar_scoring
    (the manifest rows) agrees."""
    tops = dict(TOPS, **GD_LATTICE_TOPS)
    for fam, keys in (("FG", ("FG8", "FG12", "FG16", "FG20")), ("DQ", ("DQ10", "DQ12", "DQ14", "DQ16")),
                      ("GD", ("GD12", "GD16", "GD20", "Lattice25", "Lattice32", "Lattice40"))):
        y = tops[keys[0]]
        es = np.arange(0.0, 32.0, 0.0005)
        scores = []
        for k in keys:
            s = tops[k] - (es + y)
            scores.append(1.0 / (1.0 + np.where(s >= 0.0, 2.0 * s, -s)))
        win = np.argmax(np.array(scores), axis=0)
        flips = [float(es[i]) for i in np.nonzero(np.diff(win))[0] + 1]
        check(len(flips) == len(SWITCH[fam]) and all(abs(a - b) < 1e-3 for a, b in zip(flips, SWITCH[fam])),
              f"scoring {fam}: winners {[keys[i] for i in sorted(set(win.tolist()))]} switch at "
              f"{[round(f, 3) for f in flips]} = {[round(f, 3) for f in SWITCH[fam]]}")
        top = keys[-1]
        pier = 8.0 if fam == "GD" else PIERS[fam]       # GD: the lattice 40's 8 m pier
        reach = tops[top] - y + pier                     # base at terrain + e - (top - y); pier below
        check(abs(reach - GROUNDED[fam]) < 1e-6 and win[-1] == len(keys) - 1,
              f"scoring {fam}: {top} wins above {SWITCH[fam][-1]:.3f} m and grounds nodes up to {reach:.1f} m")
        if fam == "GD":
            # every tubular member's base floats at most (switch out - h) above terrain: the 4 m pier covers it
            floats = [SWITCH["GD"][i] - (tops[k] - y) for i, k in enumerate(keys[:3])]
            check(max(floats) <= PIERS["GD"] and abs(max(floats) - 10 / 3) < 1e-9,
                  f"scoring GD: tubular members float <= {max(floats):.3f} m <= pier {PIERS['GD']}")
        sc = LT.pillar_scoring(fam)
        rows = {m["variant"]: m for m in sc["members"]}
        check(abs(sc["net_subobject_y"] - y) < 1e-9 and sorted(rows) == sorted(keys)
              and all(abs(rows[k]["top_anchor_y"] - tops[k]) < 1e-9 for k in keys)
              and all(abs(a - b) < 1e-3 for a, b in zip(sc["switch_points_e_m"], SWITCH[fam]))
              and abs(sc["grounded_up_to_e_m"] - GROUNDED[fam]) < 1e-6,
              f"pillar_scoring({fam}) rows {[(k, rows[k]['top_anchor_y']) for k in keys]}, "
              f"spec y {sc['net_subobject_y']}, switch {sc['switch_points_e_m']}")
    # detail pass 1: the DQ family with DQ18 (post_freeze; the frozen manifests keep the above)
    sc = LT.pillar_scoring("DQ", post_freeze=True)
    keys = [m["variant"] for m in sc["members"]]
    check(keys == ["DQ10", "DQ12", "DQ14", "DQ16", "DQ18"]
          and all(abs(a - b) < 1e-3 for a, b in zip(sc["switch_points_e_m"], SWITCH_DQ18))
          and abs(sc["grounded_up_to_e_m"] - GROUNDED_DQ18) < 1e-6
          and LT.pillar_scoring("DQ")["switch_points_e_m"] == [round(v, 3) for v in SWITCH["DQ"]],
          f"pillar_scoring(DQ, post_freeze) {keys}: switch {sc['switch_points_e_m']} grounds to "
          f"{sc['grounded_up_to_e_m']} (DQ18 wins e 7.333-14); frozen DQ string unchanged")
    # follow-up 2026-10-11: DQ20 beside DQ18 (stage 2). Brute force over the node elevation first
    tops = {k: TOPS[k] for k in ("DQ10", "DQ12", "DQ14", "DQ16", "DQ18", "DQ20")}
    ks = list(tops)
    y = tops["DQ10"]
    es = np.arange(0.0, 24.0, 0.0005)
    scs = [1.0 / (1.0 + np.where(tops[k] - (es + y) >= 0.0, 2.0 * (tops[k] - (es + y)), -(tops[k] - (es + y))))
           for k in ks]
    win = np.argmax(np.array(scs), axis=0)
    flips = [float(es[i]) for i in np.nonzero(np.diff(win))[0] + 1]
    check(len(flips) == len(SWITCH_DQ20) and all(abs(a - b) < 1e-3 for a, b in zip(flips, SWITCH_DQ20))
          and abs(tops["DQ20"] - y + 6.0 - GROUNDED_DQ20) < 1e-9 and win[-1] == len(ks) - 1,
          f"scoring DQ with DQ18 + DQ20: switches {[round(f, 3) for f in flips]}, DQ20 wins above "
          f"{SWITCH_DQ20[-1]:.3f} and grounds nodes up to {tops['DQ20'] - y + 6.0:.1f} m (= the FG line)")
    sc2 = LT.pillar_scoring("DQ", post_freeze=2)
    keys2 = [m["variant"] for m in sc2["members"]]
    rows = {m["variant"]: m for m in sc2["members"]}
    check(keys2 == ks and all(abs(a - b) < 1e-3 for a, b in zip(sc2["switch_points_e_m"], SWITCH_DQ20))
          and abs(sc2["grounded_up_to_e_m"] - GROUNDED_DQ20) < 1e-6
          and rows["DQ20"]["wins_e_m"] == [9.333, 16.0] and rows["DQ18"]["wins_e_m"] == [7.333, 9.333]
          and rows["DQ20"]["utility_object_y"] == 18.0 and rows["DQ20"]["lv_comm_y"] == 19.2
          and rows["DQ20"]["base_above_terrain_m"] == [-0.667, 6.0],
          f"pillar_scoring(DQ, post_freeze=2) {keys2}: switch {sc2['switch_points_e_m']}, DQ20 wins "
          f"{rows['DQ20']['wins_e_m']}, base {rows['DQ20']['base_above_terrain_m']}, grounds to "
          f"{sc2['grounded_up_to_e_m']}")
    # the DQ18 manifest's stage-1 family and the frozen stage 0 are NOT changed by DQ20 joining VARIANTS
    check([m["variant"] for m in LT.pillar_scoring("DQ", post_freeze=True)["members"]] == keys
          and LT.pillar_scoring("DQ", post_freeze=True)["grounded_up_to_e_m"] == GROUNDED_DQ18
          and LT.pillar_scoring("DQ")["grounded_up_to_e_m"] == GROUNDED["DQ"]
          and LT.switch_points_note(None, True).endswith("DQ18 adds 22/3; the other manifests keep the frozen string)")
          and "DQ20 adds 28/3" in LT.switch_points_note(None, 2),
          "DQ18 (stage 1) and the frozen (stage 0) families unchanged by DQ20; the DQ20 note names 28/3")


def free(objs):
    for o in objs:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(me)


def variant_case(key, head=None):
    LT.clear_scene()
    spec = LT.VARIANTS[key]
    t, table, _s, info = LT.build_tower_v2(spec, head, finish=False)
    lods = LT.lod_chain_v2(t, t.name, spec)
    budget = LT.assert_budgets(t, lods, spec)
    tag = f"{key}/{head or 'yan'}"
    if (head or spec.heads[0]) == spec.heads[0] or spec.family == "FG":
        n0 = budget["tris"][0]
        check(n0 == LEDGER[key] and n0 <= spec.cap,
              f"{tag} DT-C2 LOD0 {n0} == ledger {LEDGER[key]} <= cap {spec.cap}")
        if key in DESIGN_LEDGER:
            check(abs(n0 - DESIGN_LEDGER[key]) <= 0.01 * DESIGN_LEDGER[key],
                  f"{tag} DT-C2 LOD0 within 1 % of the design ledger {DESIGN_LEDGER[key]}")
    co = coords(t.data)
    pv = LT.part_vertices(t, table)                  # NET frame (after orient_for_net)
    # names
    check(cs2_asset.validate_asset_name(NAMES[key]) == NAMES[key] and t.name == NAMES[key],
          f"{tag} name {NAMES[key]} valid and used")
    rep = cs2_validate._Report(NAMES[key])
    cs2_validate.lint_asset_name(NAMES[key], rep)
    check(not any(f[0] == "ERROR" for f in rep), f"{tag} lint_asset_name clean")
    # axle + lanes from the sheave centers (net frame: gauge = X, travel = -Y)
    rims = [(n, (v.min(axis=0) + v.max(axis=0)) / 2.0) for n, v in pv.items()
            if n.startswith("sheave_") and n.endswith("_rim")]
    check(len(rims) == 2 * spec.sheaves, f"{tag} {len(rims)} sheave rims = 2 x {spec.sheaves}")
    lane = spec.gauge / 2.0
    check(max(abs(abs(c[0]) - lane) for _n, c in rims) < 1e-4, f"{tag} lanes +-{lane} (net X)")
    check(abs(spec.axle - AXLES[key]) < 1e-9, f"{tag} spec axle {spec.axle} = {AXLES[key]}")
    for side in (-1, 1):
        pair = sorted([c for _n, c in rims if np.sign(c[0]) == side], key=lambda c: abs(c[1]))[:2]
        mz = (pair[0][2] + pair[1][2]) / 2.0
        tol = 1e-4 if spec.sag == 0 else 2e-3
        check(abs(mz - AXLES[key]) <= tol, f"{tag} center-pair sheave Z side {side:+d} = axle ({mz:.5f})")
    if spec.family == "FG":
        check(abs(AXLES[key] - (spec.tube_top - 0.96)) < 1e-9, f"{tag} FG axle = mast - 0.96")
    # bounds
    top = co[:, 2].max()
    want = TOPS[key] + (0.0001 if head == "ingest" else 0.0)
    check(abs(top - want) <= (0.0005 if head == "ingest" else 1e-4),
          f"{tag} bounds top {top:.5f} = {want:.4f}")
    pier = PIERS["ETDQ"] if key == "ETDQ" else PIERS[spec.family]
    check(abs(co[:, 2].min() + pier) < 1e-4, f"{tag} bounds min -{pier}")
    lat = np.abs(co[:, 0]).max()
    check(abs(lat - LATERAL[spec.family]) <= 0.01, f"{tag} post-orient lateral +-{lat:.4f}")
    if spec.family == "GD":
        # Revision 1b: the gondola contract (tram_tower.GD), measured on the mesh
        mast_top = pv["mast"][:, 2].max()
        check(abs(TOPS[key] - AXLES[key] - 2.0) < 1e-9 and abs(mast_top - (AXLES[key] + 0.50)) < 1e-4,
              f"{tag} GD axle = top - 2.000, tube top = axle + 0.50 ({mast_top:.4f})")
        check((spec.role != "line" or abs(spec.comm_y - (AXLES[key] + 1.20)) < 1e-9)
              and abs(spec.gauge / 2.0 - 3.0) < 1e-9
              and abs(np.hypot(pv["mast"][:, 0], pv["mast"][:, 1]).max() - 0.50) < 1e-3,
              f"{tag} GD comm {spec.comm_y} = axle + 1.20 (line), lanes +-3.0, mast r 0.50")
    if spec.family == "FG":
        check(abs(np.abs(co[:, 1]).max() - 0.97) <= 0.01, f"{tag} FG travel +-0.97")
    # LOD drift + budgets
    d1, d2 = budget["drift"][1], budget["drift"][2]
    check(abs(d1) <= 0.01, f"{tag} LOD1 top drift {d1:.4f} <= 0.01")
    if spec.family == "FG":
        check(abs(d2) <= 0.01, f"{tag} LOD2 top drift {d2:.4f} ~ 0 (M5 rail kept)")
    else:
        check(0.014 <= d2 <= 0.041, f"{tag} LOD2 top drift {d2:.4f} ~ 0.024 (beam top)")
    n = budget["tris"]
    check(spec.cap == CAPS[spec.family] == LT.LOD0_CAP[spec.family]
          and n[0] <= spec.cap and n[1] <= 0.5 * n[0] and n[2] <= spec.lod2 * 1.10,
          f"{tag} budgets {n} (cap {spec.cap} = the {spec.family} family cap, LOD1 <= 50 %, "
          f"LOD2 <= {spec.lod2 * 1.1:.0f})")
    # pad clearance (rotation about Z keeps radii)
    cp = np.concatenate([pv["crashpad"], pv["crashpad_outer"]])
    r_in = np.hypot(cp[:, 0], cp[:, 1]).min()
    check(r_in >= spec.mast_r + 0.03 - 1e-6, f"{tag} crash pad inner r {r_in:.3f} >= mast r + 0.03")
    fb = np.concatenate([v for nm, v in pv.items() if nm.startswith(("flange_mastbase", "bolt_mastbase"))])
    r = np.hypot(fb[:, 0], fb[:, 1])
    cpd = cs2_asset.CRASHPAD
    fam = spec.family
    pr = PAD_R[fam]
    pz0, pz1 = cp[:, 2].min(), cp[:, 2].max()
    check(abs(np.hypot(cp[:, 0], cp[:, 1]).max() - pr) < 1e-4, f"{tag} crash pad outer r {pr}")
    shell = (r > pr - cpd["thickness"]) & (r < pr) & (fb[:, 2] >= pz0) & (fb[:, 2] <= pz1)
    check(not shell.any(), f"{tag} no flange/bolt vertex in the pad shell radial band (max bolt r "
                           f"{r[r < pr - cpd['thickness']].max():.4f})")
    # review F3: the pad sits on the flange plate, and no flange / bolt TRIANGLE crosses a pad
    # triangle on any LOD that keeps both (BVH; the plate has no vertex inside the shell)
    plate_top = pv["flange_mastbase"][:, 2].max()
    check(abs(pz0 - PAD_Z0[fam]) < 1e-5 and abs(pz0 - plate_top - 0.005) < 1e-5,
          f"{tag} crash pad bottom {pz0:.4f} = plate top {plate_top:.4f} + 0.005 (F3)")
    for lvl, obj in enumerate([t] + list(lods)):
        hits = tri_overlap(obj, table, ("flange_mastbase", "bolt_mastbase"), ("crashpad",))
        check(not hits, f"{tag} LOD{lvl} no flange/bolt triangle crosses a crash-pad triangle (F3) {hits[:3]}")
    if key in ("ETDQ", "ETGD"):
        lim = -8.3 if key == "ETDQ" else -8.4        # ETGD: the gondola station's keep-out (x_nose - 0.40)
        inboard = spec.hosted_z + np.abs(co[:, 1]).max()
        check(inboard <= lim, f"{tag} hosted inboard end z {inboard:.3f} <= {lim}")
    if spec.family in ("DQ", "GD"):
        # review F2 (independent of C18): no grate_/post_/rail_/kick_ vertex in the chair's /
        # cabin's hanger column (net frame: gauge = X) - DQ: |x| > lane - 0.45 from 1.70 below the
        # rope to the dq-chair Q1 grip top 0.22 above it; GD (Revision 1b): the gd8 bands, lane -
        # 0.81 from rope + 0.08 to rope - 2.10 and lane - 1.10 over the body (+- the train sag),
        # on any LOD
        for half, dz0, dz1 in COLUMN[spec.family]:
            z0, z1 = AXLES[key] + dz0 - abs(spec.sag), AXLES[key] + dz1 + abs(spec.sag)
            for lvl, (obj, tb) in enumerate([(t, table)] + [(l, table) for l in lods]):
                dv = [v for nm, v in LT.part_vertices(obj, tb).items()
                      if nm.startswith(("grate_", "post_", "rail_", "kick_"))]
                if not dv:
                    continue
                d = np.concatenate(dv)
                band = d[(d[:, 2] > z0) & (d[:, 2] < z1)]
                worst = float(np.abs(band[:, 0]).max()) if len(band) else 0.0
                check(worst <= lane - half + 1e-4,
                      f"{tag} LOD{lvl} deck parts outside the hanger column (rope {dz0:+.2f}..{dz1:+.2f}): "
                      f"max |x| {worst:.4f} <= {lane - half:.2f} (F2)")
        if spec.family == "GD":
            gd_sweep_case(tag, t, table, spec)
        sub_hanger_case(tag, pv, spec)
        # review F5: LOD2 keeps both deck plates (up-facing area at the deck top) and, on rebuild
        # heads, all four A-braces at full height
        l2 = lods[1]
        part = np.zeros(len(l2.data.polygons), dtype=np.int32)
        l2.data.attributes["part"].data.foreach_get("value", part)
        deck_z = AXLES[key] - 0.296
        area = sum(p.area for p, q in zip(l2.data.polygons, part)
                   if table[q - 1] in ("grate_deckn", "grate_deckp") and p.normal.z > 0.9
                   and abs(p.center.z - deck_z) < 2e-3)
        L = max(4.30, (spec.sheaves - 1) * spec.pitch + 2 * spec.sheave_r + 0.20)
        want = 2 * (L - 0.06) * (DECK_Y1[spec.family] - 0.695 - 0.06)
        check(area >= 0.95 * want, f"{tag} LOD2 deck tops kept {area:.3f} of {want:.3f} m2 (F5)")
        b0 = {nm: v for nm, v in pv.items() if nm.startswith("gantry_brace")}
        b2 = {nm: v for nm, v in LT.part_vertices(l2, table).items() if nm.startswith("gantry_brace")}
        if head == "rebuild":
            dz = lambda d: float(np.ptp(np.concatenate(list(d.values()))[:, 2])) if d else 0.0  # noqa: E731
            check(len(b0) == 4 and sorted(b2) == sorted(b0) and dz(b2) >= 0.9 * dz(b0),
                  f"{tag} LOD2 keeps the 4 A-braces ({sorted(b2)}, z extent {dz(b2):.3f} of {dz(b0):.3f}) (F5)")
    detail_case(key, head, tag, t, table, lods, spec, info)
    free(lods)
    return n


def sub_hanger_case(tag, pv, spec):
    """Revision 1c, independent of C21: in the NET frame (gauge = X, travel = -Y), every
    sub pivot pin's outboard end lies in its rocker_sub_ box and its inboard end in a
    rocker_main_ / rocker_link_ box (each within 2 cm laterally, the pin center inside the box
    in travel and height), and that inboard box reaches its side's yoke (a link overlapping
    it, a main within the 0.05 m gap its own pivot pin bridges)."""
    def bb(n):
        v = pv[n]
        return v.min(axis=0), v.max(axis=0)

    def holds(name, end_x, tc, zc):
        lo, hi = bb(name)
        g0, g1 = sorted((abs(lo[0]), abs(hi[0])))
        return (g0 - 0.02 <= end_x <= g1 + 0.02 and lo[1] <= tc <= hi[1] and lo[2] <= zc <= hi[2])

    n_ok, n_pins, n_link = 0, 0, 0
    for side in ("n", "p"):
        ylo, _yhi = bb(f"yoke_{side}")
        lugs = [n for n in pv if n.startswith((f"rocker_main_{side}_", f"rocker_link_{side}_"))]
        for pn in sorted(n for n in pv if n.startswith(f"pivot_{side}_s")):
            n_pins += 1
            lo, hi = bb(pn)
            g0, g1 = sorted((abs(lo[0]), abs(hi[0])))
            tc, zc = (lo[1] + hi[1]) / 2.0, (lo[2] + hi[2]) / 2.0
            out_ok = holds(f"rocker_sub_{side}_{pn.split('_s')[-1]}", g1, tc, zc)
            inner = [n for n in lugs if holds(n, g0, tc, zc)]
            reach = any(bb(n)[1][2] >= ylo[2] + (0.005 if n.startswith("rocker_link_") else -0.051)
                        for n in inner)
            ok = out_ok and len(inner) == 1 and reach
            n_ok += ok
            n_link += sum(1 for n in inner if n.startswith("rocker_link_"))
            if not ok:
                check(False, f"{tag} sub pin {pn} hangs (outboard in its sub {out_ok}, inboard lugs "
                             f"{inner}, reaches the yoke {reach})")
    want = spec.sheaves
    links = want // 2 % 2 * 2              # one link per side when a side has an odd sub count
    check(n_pins == want and n_ok == want and n_link == links,
          f"{tag} every sub-rocker hangs: {n_ok}/{n_pins} pins in a lug at both ends, {n_link} links "
          f"(Revision 1c, independent of C21)")


def gd_sweep_case(tag, t, table, spec):
    """Revision 1b, independent of lift_tower.check_gd_clearances: in the NET frame (gauge = X,
    travel = Y), the gondola carrier boxes of tram_tower.CARRIER_GD (lane frame dy outboard +,
    dz from the rope) swept along the whole travel extent on both lanes, shrunk 5 mm, plus the
    cabins.md 8.3 head rule, cross no tower triangle and hold no tower vertex - except the jaw
    over the sheaves; and the comm wire corridor (x 0, z comm +- 0.10) is empty."""
    from mathutils.bvhtree import BVHTree
    import tram_tower as TT
    me = t.data
    co = coords(me)
    q = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", q)
    names = [table[k - 1] for k in q]
    tree = BVHTree.FromPolygons([tuple(v) for v in co], [tuple(p.vertices) for p in me.polygons], epsilon=0.0)
    pv = LT.part_vertices(t, table)
    yt = float(np.abs(co[:, 1]).max()) + 0.5
    A, lane = spec.axle, spec.gauge / 2.0

    def hits(lo, hi, allowed=()):
        c = [(x, y, z) for z in (lo[2], hi[2]) for x, y in ((lo[0], lo[1]), (hi[0], lo[1]), (hi[0], hi[1]), (lo[0], hi[1]))]
        box = BVHTree.FromPolygons(c, [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6),
                                       (3, 0, 4, 7)], epsilon=0.0)
        h = {names[i] for i, _j in tree.overlap(box)}
        h |= {n for n, v in pv.items() if ((v > np.array(lo)) & (v < np.array(hi))).all(axis=1).any()}
        return sorted(n for n in h if not n.startswith(allowed))
    zones = [(n, dy, dz, al) for n, _x, dy, dz, al in TT.CARRIER_GD] + list(TT.GD_HEAD_RULE)
    bad = []
    for s in (-1.0, 1.0):
        for nm, (dy0, dy1), (dz0, dz1), allowed in zones:
            allowed = tuple(allowed) + (("liner_",) if "sheave_" in allowed else ())   # detail pass 1
            xs = sorted((s * (lane + dy0), s * (lane + dy1)))
            bad += [(int(s), nm, h) for h in hits((xs[0] + 0.005, -yt, A + dz0 + 0.005),
                                                   (xs[1] - 0.005, yt, A + dz1 - 0.005), allowed)]
    check(not bad, f"{tag} gondola carrier boxes + head rule (tram_tower) clear, hanger outboard {bad[:4]}")
    if spec.comm_y is not None:
        comm = hits((-0.10, -yt, spec.comm_y - 0.10), (0.10, yt, spec.comm_y + 0.10))
        check(not comm, f"{tag} comm wire corridor x 0, z {spec.comm_y} +- 0.10 empty {comm[:4]}")


def gd_seam_case():
    """Revision 1b: lift_tower's GD numbers are the ones the shipped lattice towers and the gondola
    cabin already assume (tram_tower.GD / CARRIER_GD / GD_HEAD_RULE / SHEAVE / STUB_LANES,
    catalog/carriers.json gd8, the lattice manifests in towers/O)."""
    import tram_tower as TT
    with open(os.path.join(LT._REPO, "catalog", "carriers.json"), encoding="utf-8") as fh:
        gd8 = json.load(fh)["carriers"]["gd8"]
    g = TT.GD
    gd = {k: s for k, s in LT.VARIANTS.items() if s.family == "GD" and s.role == "line"}   # ETGD: hosted
    check(sorted(s.top for s in gd.values()) == list(g["tubular_tops"]),
          f"GD tops {sorted(s.top for s in gd.values())} == tram_tower.GD tubular_tops {g['tubular_tops']}")
    check(LT.H_TOP["GD"] == g["h_top"] and all(abs(s.top - s.axle - g["h_top"]) < 1e-9 for s in gd.values()),
          f"GD H_top {LT.H_TOP['GD']} == tram_tower.GD h_top {g['h_top']}")
    check(all(abs(s.tube_top - s.axle - g["tube_top_dA"]) < 1e-9 and s.mast_r == g["mast_r"]
              and abs(s.comm_y - s.axle - g["comm_dA"]) < 1e-9 for s in gd.values()),
          f"GD tube top A + {g['tube_top_dA']}, mast r {g['mast_r']}, comm A + {g['comm_dA']} == tram_tower.GD")
    check(all(abs(s.gauge / 2.0 - g["lane_y"]) < 1e-9 for s in gd.values()) and g["lane_y"] == gd8["lane_x_m"],
          f"GD lanes +-{g['lane_y']} == carriers gd8 lane_x_m {gd8['lane_x_m']}")
    check(abs(gd["GD12"].axle - gd8["axle_m"]) < 1e-9,
          f"GD12 axle {gd['GD12'].axle} == carriers gd8 axle_m {gd8['axle_m']} (grounds at node elevation 0)")
    check(abs(LT.GD_HANGER_COL[0]["z1"] - gd8["grip_top_above_rope_m"]) < 1e-9
          and abs(LT.GD_HANGER_COL[1]["half"] - max(abs(v) for v in gd8["bounds_lat_m"])) < 1e-9
          and abs(LT.GD_HANGER_COL[1]["z0"] + gd8["hang_m"]) < 1e-9,
          f"GD hanger column: grip top +{gd8['grip_top_above_rope_m']}, body half "
          f"{max(abs(v) for v in gd8['bounds_lat_m'])}, underside -{gd8['hang_m']} == carriers gd8")
    stalk = max(max(abs(dy[0]), abs(dy[1])) for n, _x, dy, _dz, _a in TT.CARRIER_GD if n != "body")
    low = min(dz[0] for n, _x, _dy, dz, _a in TT.CARRIER_GD if n != "body")
    check(abs(LT.GD_HANGER_COL[0]["half"] - stalk) < 1e-9 and abs(LT.GD_HANGER_COL[0]["z0"] - low) < 1e-9,
          f"GD hanger column half {LT.GD_HANGER_COL[0]['half']} / bottom {LT.GD_HANGER_COL[0]['z0']} == "
          f"tram_tower.CARRIER_GD hanger boxes |dy| {stalk} / dz {low}")
    check(tuple(LT.GD_CARRIER) == tuple(TT.CARRIER_GD) and tuple(LT.GD_HEAD_RULE) == tuple(TT.GD_HEAD_RULE),
          "lift_tower.GD_CARRIER / GD_HEAD_RULE == tram_tower.CARRIER_GD / GD_HEAD_RULE")
    check(all(getattr(s, a) == TT.SHEAVE[b] for s in gd.values() for a, b in
              (("sheave_r", "r"), ("sheave_w", "w"), ("pitch", "pitch"))),
          f"GD sheave r / w / pitch {LT.GD_SHEAVE} == tram_tower.SHEAVE {TT.SHEAVE}")
    # tessellation is cosmetic (not a contract): reported, never a failure
    same = all(LT.GD_SHEAVE.get(k) == TT.SHEAVE.get(k) for k in ("segs", "groove"))
    print(f"[contracts] {'note' if not same else 'ok  '} GD sheave segs / groove "
          f"{LT.GD_SHEAVE['segs']} / {LT.GD_SHEAVE['groove']} vs lattice {TT.SHEAVE.get('segs')} / "
          f"{TT.SHEAVE.get('groove')}{'' if same else ' (differs: cosmetic only)'}")
    check(LT.GD_STUB_LANES["hv"] == TT.STUB_LANES["gd_haul"] and LT.GD_STUB_LANES["lv"] == TT.STUB_LANES["gd_comm"],
          "GD stub lanes == tram_tower.STUB_LANES gd_haul / gd_comm")
    for key, top, axle, comm, pier in LT.GD_LATTICE:
        nm = f"SkiGondola{key}O"
        p = os.path.join(LT._REPO, "assets", "import", "towers", "O", nm, f"{nm}.manifest.json")
        if not os.path.isfile(p):
            print(f"[contracts] SKIP {nm}: no shipped manifest")
            continue
        with open(p, encoding="utf-8") as fh:
            m = json.load(fh)["prefab"]
        stubs = {tuple(s["pos"]) for s in m["stubs"]}
        check(abs(m["bounds_top"] - top) < 1e-6 and abs(m["utility_object"]["position"][1] - axle) < 1e-6
              and (0.0, comm, 0.0) in stubs and abs(m["h_top_gd"] - LT.H_TOP["GD"]) < 1e-9
              and abs(m["top_anchor_y"] - LT.VARIANTS["GD12"].top) < 1e-9,
              f"{nm} manifest: top {m['bounds_top']}, axle {m['utility_object']['position'][1]}, comm {comm}, "
              f"H_top {m['h_top_gd']}, family spec y {m['top_anchor_y']} == lift_tower GD_LATTICE / GD12")


# --------------------------------------------------------------------------------------
# Detail pass 1 (design/detail-pass-1-towers.md section 6 with the skeptic's M1-M4 / M9-M11):
# DT-C2..C6 + M1 / M3 on the no-bake builds, independent of lift_tower's own asserts
# --------------------------------------------------------------------------------------

DP1_LOD0_ONLY = ("plate_", "platerim_", "conduit_", "jbox_", "arrest_", "gusset_apex")
DP1_HOSTS = {"conduit_": ("mast", "jbox_", "crashpad"), "jbox_": ("mast", "conduit_"),
             "arrest_": ("ladder_rung_", "ladder_standoff_"),
             "gusset_apex": ("gantry_beam", "gantry_brace"),
             "plate_": ("platerim_", "crossarm", "gantry_beam", "gantry_00"),
             "platerim_": ("plate_", "crossarm", "gantry_beam", "gantry_00")}


def _tri_arrays(obj):
    me = obj.data
    me.calc_loop_triangles()
    nt = len(me.loop_triangles)
    tv = np.zeros(nt * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("vertices", tv)
    tp = np.zeros(nt, dtype=np.int64)
    me.loop_triangles.foreach_get("polygon_index", tp)
    pid = np.zeros(len(me.polygons), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", pid)
    return coords(me), tv.reshape(-1, 3), pid[tp]


def _clip(subj, clip):
    """Sutherland-Hodgman: convex polygon `subj` clipped by convex CCW `clip` (2D)."""
    out = list(subj)
    for i in range(len(clip)):
        a, b = clip[i], clip[(i + 1) % len(clip)]
        inp, out = out, []
        if not inp:
            break

        def side(p):
            return (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])
        for j in range(len(inp)):
            p, q = inp[j], inp[(j + 1) % len(inp)]
            sp, sq = side(p), side(q)
            if sp >= 0:
                out.append(p)
            if (sp >= 0) != (sq >= 0):
                t = sp / (sp - sq)
                out.append((p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1])))
    return out


def _area2(poly):
    return 0.5 * abs(sum(poly[i][0] * poly[(i + 1) % len(poly)][1] - poly[(i + 1) % len(poly)][0] * poly[i][1]
                         for i in range(len(poly)))) if len(poly) >= 3 else 0.0


def coplanar_scan(obj, table, tol_d=0.0005, ang_deg=1.0):
    """(same {"a|b": cm2}, hidden [tri index]) of a mesh: same-facing coplanar overlap per part
    pair (the cabin G12 tolerances: 1 deg, 0.5 mm; pairs sharing an edge skipped) and the
    triangles >= 99.5 % covered by OPPOSED coplanar triangles of another part (DT-C3 / DT-C4)."""
    co, tv, tpart = _tri_arrays(obj)
    q = np.round(co / 1e-5).astype(np.int64)
    _u, wid = np.unique(q, axis=0, return_inverse=True)
    vid = wid.reshape(-1)[tv]
    P = co[tv]
    fn = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0])
    ln = np.linalg.norm(fn, axis=1)
    keep = np.where(ln > 2e-10)[0]
    n = fn[keep] / ln[keep, None]
    d = np.einsum("ij,ij->i", n, P[keep, 0])
    lo, hi = P[keep].min(axis=1), P[keep].max(axis=1)
    area = 0.5 * ln
    cosmax = math.cos(math.radians(ang_deg))
    same = collections.defaultdict(float)
    cover = collections.defaultdict(float)
    M, B = len(keep), 512
    for s0 in range(0, M, B):
        s1 = min(M, s0 + B)
        dot = n[s0:s1] @ n.T
        box = np.all((lo[s0:s1, None, :] <= hi[None, :, :] + 1e-4)
                     & (lo[None, :, :] <= hi[s0:s1, None, :] + 1e-4), axis=2)
        for kind, mask in (("same", (dot > cosmax) & (np.abs(d[s0:s1, None] - d[None, :]) <= tol_d)),
                           ("opp", (dot < -cosmax) & (np.abs(d[s0:s1, None] + d[None, :]) <= tol_d))):
            ii, jj = np.nonzero(mask & box)
            for a, b in zip((ii + s0).tolist(), jj.tolist()):
                if a == b or (kind == "same" and b < a):
                    continue
                ta, tb = int(keep[a]), int(keep[b])
                if kind == "opp" and tpart[ta] == tpart[tb]:
                    continue
                if len(set(vid[ta].tolist()) & set(vid[tb].tolist())) >= 2:
                    continue
                o, ex = P[ta, 0], P[ta, 1] - P[ta, 0]
                ex = ex / np.linalg.norm(ex)
                ey = np.cross(n[a], ex)
                t1 = [((p - o) @ ex, (p - o) @ ey) for p in P[ta]]
                t2 = [((p - o) @ ex, (p - o) @ ey) for p in P[tb]]
                if _area2(t1) <= 0:
                    continue
                if (t1[1][0] - t1[0][0]) * (t1[2][1] - t1[0][1]) - (t1[1][1] - t1[0][1]) * (t1[2][0] - t1[0][0]) < 0:
                    t1 = t1[::-1]
                if (t2[1][0] - t2[0][0]) * (t2[2][1] - t2[0][1]) - (t2[1][1] - t2[0][1]) * (t2[2][0] - t2[0][0]) < 0:
                    t2 = t2[::-1]
                ar = _area2(_clip(t2, t1))
                if ar <= 1e-7:
                    continue
                if kind == "same":
                    pa, pb = sorted((table[tpart[ta] - 1], table[tpart[tb] - 1]))
                    same[f"{pa}|{pb}"] += ar * 1e4
                else:
                    cover[ta] += ar
    hidden = [t for t, ar in cover.items() if ar >= 0.995 * area[t]]
    return dict(same), hidden


def uv_isotropy(obj, layer="UVMap", min_area=1e-4, min_w=0.02):
    """DT-C5: area-weighted texel anisotropy (max / min singular value of the UV -> 3D map) over
    the polygons >= 1 cm2 and not narrower than 0.02 m: (median, p95, worst class)."""
    me = obj.data
    me.calc_loop_triangles()
    uvl = me.uv_layers[layer].data
    co = coords(me)
    pid = np.zeros(len(me.polygons), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", pid)
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    pa = np.array([p.area for p in me.polygons])
    an, w, cl = [], [], []
    for lt in me.loop_triangles:
        if pa[lt.polygon_index] < min_area:
            continue
        P = co[list(lt.vertices)]
        Q = np.array([uvl[l].uv[:] for l in lt.loops])
        e1, e2 = P[1] - P[0], P[2] - P[0]
        a3 = 0.5 * np.linalg.norm(np.cross(e1, e2))
        L = max(np.linalg.norm(e1), np.linalg.norm(e2), np.linalg.norm(P[2] - P[1]))
        if a3 < 1e-9 or 2.0 * a3 / L < min_w:
            continue
        F = np.array([[Q[1][0] - Q[0][0], Q[2][0] - Q[0][0]], [Q[1][1] - Q[0][1], Q[2][1] - Q[0][1]]])
        if abs(np.linalg.det(F)) < 1e-14:
            continue
        J = np.stack([e1, e2], axis=1) @ np.linalg.inv(F)
        s = np.linalg.svd(J, compute_uv=False)
        an.append(s[0] / max(s[1], 1e-12))
        w.append(a3)
        cl.append(table[pid[lt.polygon_index] - 1])
    an, w = np.array(an), np.array(w)
    o = np.argsort(an)
    cw = np.cumsum(w[o]) / w.sum()
    med = float(an[o][np.searchsorted(cw, 0.5)])
    p95 = float(an[o][min(len(o) - 1, np.searchsorted(cw, 0.95))])
    worst = cl[int(np.argmax(an))]
    return med, p95, worst


def _faces_of(obj, table, prefixes):
    me = obj.data
    q = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", q)
    return [p for p, k in zip(me.polygons, q) if table[k - 1].startswith(prefixes)]


def _nonhost_hits(obj, table, prefix, hosts):
    """Part names (not `prefix`, not a host) whose faces intersect the `prefix` part (BVH)."""
    from mathutils.bvhtree import BVHTree
    me = obj.data
    q = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", q)
    co = [tuple(v) for v in coords(me)]
    A, An, Bp, Bn = [], [], [], []
    for p, k in zip(me.polygons, q):
        nm = table[k - 1]
        if nm.startswith(prefix):
            A.append(tuple(p.vertices))
            An.append(nm)
        elif not nm.startswith(hosts):
            Bp.append(tuple(p.vertices))
            Bn.append(nm)
    if not A or not Bp:
        return []
    ta = BVHTree.FromPolygons(co, A, epsilon=0.0)
    tb = BVHTree.FromPolygons(co, Bp, epsilon=0.0)
    return sorted({Bn[j] for _i, j in ta.overlap(tb)})


def detail_case(key, head, tag, t, table, lods, spec, info):
    """DT-C3 / C4 / C5 / C6 + M1 / M3 / M4 on one no-bake build (NET frame)."""
    pv = LT.part_vertices(t, table)
    names = set(table)
    # DT-C6 counts
    rebuild = (head or spec.heads[0]) == "rebuild"
    want = {"plate_n", "plate_p", "platerim_n", "platerim_p", "conduit_0", "jbox_0", "arrest_0"}
    if spec.family != "FG" and rebuild:
        want |= {"gusset_apex_n", "gusset_apex_p"}
    liners = [n for n in names if n.startswith("liner_")]
    check(want <= names and len(liners) == 2 * spec.sheaves
          and (rebuild or spec.family == "FG" or not any(n.startswith("gusset_apex") for n in names)),
          f"{tag} DT-C6 detail parts present ({sorted(want - names)} missing; {len(liners)} liners)")
    # DT-C6 LOD fates
    for lvl, lod in enumerate(lods, start=1):
        left = {table[k - 1] for k in _part_ids(lod)}
        bad = sorted(n for n in left if n.startswith(DP1_LOD0_ONLY) or (lvl == 2 and n.startswith("liner_")))
        check(not bad, f"{tag} DT-C6 LOD{lvl} has no LOD0-only detail part{' or liner' if lvl == 2 else ''} {bad[:4]}")
    # DT-C6 plates: 4-8 mm proud, rims 1-3 mm into the host; M3 the u axis = the viewer's right
    for side in ("n", "p"):
        f = _faces_of(t, table, (f"plate_{side}",))
        nrm = f[0].normal
        front = float(np.mean([t.data.vertices[i].co.dot(nrm) for i in f[0].vertices]))
        rim = pv[f"platerim_{side}"]
        back = float((rim @ np.array(nrm)).min())
        host = [p for p in _faces_of(t, table, ("crossarm", "gantry_beam", "gantry_00"))
                if p.normal.dot(nrm) > 0.99 and abs(p.center.z - f[0].center.z) < 0.15]
        hz = max(p.center.dot(nrm) for p in host)
        uvl = t.data.uv_layers["UVMap"].data
        P = [t.data.vertices[t.data.loops[li].vertex_index].co for li in f[0].loop_indices]
        Q = [uvl[li].uv for li in f[0].loop_indices]
        du = mathutils.Vector((0.0, 0.0, 0.0))
        for i in range(len(P)):                     # least-squares direction of +u in 3D
            for j in range(len(P)):
                if Q[j][0] - Q[i][0] > 1e-6:
                    du += (P[j] - P[i]) * (Q[j][0] - Q[i][0])
        right = mathutils.Vector((0.0, 0.0, 1.0)).cross(nrm)
        check(0.004 <= front - hz <= 0.008 and 0.001 <= hz - back <= 0.003 and du.normalized().dot(right) > 0.99,
              f"{tag} DT-C6 plate_{side} {1000 * (front - hz):.1f} mm proud, rim {1000 * (hz - back):.1f} mm "
              f"into the host; M3 +u = the viewer's right ({du.normalized().dot(right):+.3f})")
    # DT-C6 non-host overlap (BVH)
    for pre, hosts in DP1_HOSTS.items():
        if not any(n.startswith(pre) for n in names):
            continue
        hits = _nonhost_hits(t, table, pre, hosts)
        check(not hits, f"{tag} DT-C6 {pre}* overlaps no non-host part {hits[:4]}")
    # conduit standoff (M9b): clear of the joint rings by >= 9 mm, foot at the rim / in the gap
    c = pv["conduit_0"]
    rc = np.hypot(c[:, 0], c[:, 1])
    check(rc.min() >= spec.mast_r + 0.04 + 0.009 and
          (c[:, 2].min() < cs2_asset.CRASHPAD["z1"] if spec.family == "FG"
           else abs(c[:, 2].min() - cs2_asset.CRASHPAD["z1"]) < 0.005),
          f"{tag} M9 conduit r {rc.min():.3f}..{rc.max():.3f} (rings r {spec.mast_r + 0.04:.2f}), foot "
          f"z {c[:, 2].min():.3f} (rim {cs2_asset.CRASHPAD['z1']})")
    # DT-C5 isotropy (whole mesh, pre-pack "UVMap")
    med, p95, worst = uv_isotropy(t)
    check(med <= 1.05 and p95 <= 1.35, f"{tag} DT-C5 texel anisotropy median {med:.3f} <= 1.05, p95 {p95:.3f} "
                                       f"<= 1.35 (worst tri in {worst})")
    # M1: the FG16 / FG20 mast metric island split under a joint ring; every mast island fits
    me = t.data
    uvl = me.uv_layers["UVMap"].data
    mf = _faces_of(t, table, ("mast",))
    side = [p for p in mf if abs(p.normal.z) < 0.5]
    us = sorted({round(uvl[li].uv[0] // 500.0) for p in side for li in p.loop_indices})
    vlen = []
    for k in us:
        vv = [uvl[li].uv[1] for p in side for li in p.loop_indices if round(uvl[li].uv[0] // 500.0) == k]
        vlen.append(max(vv) - min(vv))
    est = LT.DP1_MAST_PPM_EST[spec.family]
    cut = info.get("mast_cut")
    check(max(vlen) * est <= LT.DP1_ISLAND_MAX_PX and (len(vlen) == 2) == (cut is not None),
          f"{tag} M1 mast islands {[round(v, 2) for v in vlen]} m x {est} px/m <= {LT.DP1_ISLAND_MAX_PX} "
          f"(cut {cut})")
    # DT-C3 coplanar (LOD0 / 1 / 2) and DT-C4 hidden contact (LOD0)
    for lvl, obj in enumerate([t] + list(lods)):
        same, hidden = coplanar_scan(obj, table)
        bad = {k: round(v, 2) for k, v in same.items() if v > 1.0}
        check(not bad, f"{tag} DT-C3 LOD{lvl} same-facing coplanar <= 1 cm2 per pair {dict(list(bad.items())[:4])}")
        if lvl == 0:
            check(not hidden, f"{tag} DT-C4 no triangle >= 99.5 % covered by an opposed part ({len(hidden)})")


def _part_ids(obj):
    q = np.zeros(len(obj.data.polygons), dtype=np.int32)
    obj.data.attributes["part"].data.foreach_get("value", q)
    return set(int(x) for x in q)


def etgd_station_case():
    """ETGD vs the gondola base station (the hosted pose (0, 0, -11.0) rot 180 deg):
    (a) inside lift_terminal_detach.GD_TOWER_KEEPOUT (station frame x < x_nose - 0.40, |y| <=
    4.60, z < 7.0); (b) the station's rope y / gauge == the tower's axle / gauge; (c) triangle
    overlap of tower LOD0 against the station LOD0 minus its haul ropes is empty. A station build
    error (another group's module) is printed as a NOTE, the keep-out check still runs."""
    import lift_terminal_detach as LTD
    spec = LT.VARIANTS["ETGD"]
    LT.clear_scene()
    t, table, _s, _i = LT.build_tower_v2(spec, "rebuild", finish=False)
    co = coords(t.data)                                  # net frame: x gauge, y = -travel, z up
    st = np.stack([-co[:, 1] + 0.0, co[:, 0], co[:, 2]], axis=1)   # tower authoring (x travel, y gauge)
    # hosted 180 deg: station authoring x = hosted_z - tower travel, y = -tower gauge
    sx = spec.hosted_z - st[:, 0]
    sy = -st[:, 1]
    k = LTD.GD_TOWER_KEEPOUT
    p = LTD.params_for("base", True, LTD.load_carrier("gd8"))
    xmax = p.x_nose - k["dx"]
    check(sx.max() <= xmax + 1e-6 and np.abs(sy).max() <= k["y"] and co[:, 2].max() < k["z"],
          f"ETGD inside the gondola station keep-out: x {sx.min():.3f}..{sx.max():.3f} <= {xmax:.2f}, "
          f"|y| {np.abs(sy).max():.3f} <= {k['y']}, top {co[:, 2].max():.3f} < {k['z']}")
    check(abs(p.z_rope - spec.axle) < 1e-9 and abs(p.lane_y - spec.gauge / 2.0) < 1e-9
          and abs(p.anchor_x - spec.hosted_z) < 1e-9,
          f"ETGD rope {spec.axle} / lane {spec.gauge / 2.0} / pose {spec.hosted_z} == station "
          f"z_rope {p.z_rope} / lane {p.lane_y} / anchor_x {p.anchor_x}")
    try:
        from mathutils.bvhtree import BVHTree
        LT.clear_scene()
        parts = LTD.build_station(p)
        sobj, stable, _src = LTD.assemble(parts, True, p)
        sco = coords(sobj.data)
        q = np.zeros(len(sobj.data.polygons), dtype=np.int32)
        sobj.data.attributes["part"].data.foreach_get("value", q)
        spolys = [tuple(pp.vertices) for pp, kk in zip(sobj.data.polygons, q)
                  if not stable[kk - 1].startswith("haulrope_")]
        LT_t = LT.build_tower_v2(spec, "rebuild", finish=False)[0]
        tco = coords(LT_t.data)
        ta = np.stack([-tco[:, 1], tco[:, 0], tco[:, 2]], axis=1)
        placed = np.stack([spec.hosted_z - ta[:, 0], -ta[:, 1], ta[:, 2]], axis=1)
        tp = [tuple(pp.vertices) for pp in LT_t.data.polygons]
        a = BVHTree.FromPolygons([tuple(v) for v in sco], spolys, epsilon=0.0)
        b = BVHTree.FromPolygons([tuple(v) for v in placed], tp, epsilon=0.0)
        hits = a.overlap(b)
        check(not hits, f"ETGD LOD0 x gondola base station LOD0 (minus haul ropes): {len(hits)} overlaps")
    except Exception as e:  # noqa: BLE001
        print(f"[contracts] NOTE ETGD x gondola station geometry not checked: {e!r}")


def dp1_negative_cases():
    """Detail-pass wrong builds must FAIL their checks: the pre-pass FG hanger top (DT-C3), brace
    a at 0.186 on GD12 (M4 / DT-C3), a create_cone-UV mast (DT-C5)."""
    def cop(spec, head):
        LT.clear_scene()
        t, table, _s, _i = LT.build_tower_v2(spec, head, finish=False)
        same, _h = coplanar_scan(t, table)
        return {k: v for k, v in same.items() if v > 1.0}
    old = dict(LT.DP1_INSET)
    try:
        LT.DP1_INSET["fg_hanger_top"] = 0.0
        bad = cop(LT.VARIANTS["FG8"], None)
        check(any("crossarm" in k and "hanger" in k for k in bad),
              f"wrong build fails: the pre-pass FG hanger top z-fights the crossarm (DT-C3) {list(bad)[:3]}")
        LT.DP1_INSET.update(old)
        LT.DP1_INSET["brace_a_w"] = 0.186
        bad = cop(LT.VARIANTS["GD12"], "rebuild")
        check(any("gantry_brace" in k for k in bad),
              f"wrong build fails: GD12 brace a 0.186 z-fights brace b (M4 / DT-C3) {list(bad)[:3]}")
    finally:
        LT.DP1_INSET.clear()
        LT.DP1_INSET.update(old)
    orig = LT.uv_rule
    try:
        LT.uv_rule = lambda n, s, o=None: None if n == "mast" else orig(n, s, o)
        LT.clear_scene()
        t, _tb, _s, _i = LT.build_tower_v2(LT.VARIANTS["FG12"], None, finish=False)
        med, p95, worst = uv_isotropy(t)
        check(p95 > 1.35, f"wrong build fails: FG12 with the create_cone mast UV breaks DT-C5 (p95 {p95:.2f}, {worst})")
    finally:
        LT.uv_rule = orig


WRONG = []


def expect_failure(label, fn):
    WRONG.append(label)
    try:
        fn()
    except (LT.TowerContractError, AssertionError) as e:
        check(True, f"wrong build fails: {label} ({str(e).splitlines()[0][:90]})")
        return
    check(False, f"wrong build fails: {label} (it PASSED)")


def negative_cases():
    def build(spec, head=None):
        LT.clear_scene()
        LT.build_tower_v2(spec, head, finish=False)

    def with_const(name, value, fn):
        old = getattr(LT, name)
        setattr(LT, name, value)
        try:
            fn()
        finally:
            setattr(LT, name, old)

    dq12, etdq, fg8 = LT.VARIANTS["DQ12"], LT.VARIANTS["ETDQ"], LT.VARIANTS["FG8"]
    expect_failure("DQ flange r 0.70 pierces the pad (C14)",
                   lambda: with_const("DQ_FLANGE_R", 0.70, lambda: build(dq12, "rebuild")))
    expect_failure("rocker_sub at 2.517 touches the sheave (F9)",
                   lambda: with_const("DQ_SUB_Y", 2.517, lambda: build(dq12, "rebuild")))
    expect_failure("deck rails through the portal (F10 / C17)",
                   lambda: with_const("DQ_RAIL_GAP", -0.5, lambda: build(dq12, "rebuild")))
    expect_failure("spec top 1 cm off (C3)",
                   lambda: build(dataclasses.replace(dq12, top=dq12.top + 0.01), "rebuild"))
    expect_failure("ETDQ with 10 sheaves per side (M4 / C13)",
                   lambda: build(dataclasses.replace(etdq, sheaves=10), "rebuild"))
    # review F2: the shipped-before decks (|Y| 2.492) and rails (2.30) cut the hanger column
    expect_failure("DQ decks to 2.492 / rails 2.30 in the hanger column (F2 / C18)",
                   lambda: with_const("DQ_DECK_Y1", 2.492, lambda: with_const(
                       "DQ_RAIL_Y", 2.30, lambda: build(etdq, "rebuild"))))
    expect_failure("DQ rails alone at 2.30 in the hanger column (F2 / C18)",
                   lambda: with_const("DQ_RAIL_Y", 2.30, lambda: build(dq12, "rebuild")))

    def fg_lod2_rail():
        LT.clear_scene()
        t, _tb, _s, _i = LT.build_tower_v2(fg8, None, finish=False)
        lods = LT.lod_chain_v2(t, t.name, fg8)
        try:
            LT.assert_budgets(t, lods, fg8)
        finally:
            free(lods)
    expect_failure("FG LOD2 deleting rail_catwalk_top drifts the top (M5 / C12)",
                   lambda: with_const("FG_LOD2", LT.FG_LOD2 + ("rail_catwalk_top",), fg_lod2_rail))

    # follow-up 2026-10-11: DQ20 measures 3,554 (ingest) / 3,534 (rebuild) tris; held to the old 3,500
    # DQ cap it must fail C11, and it fits the raised 3,600 cap
    dq20 = LT.VARIANTS["DQ20"]
    head20 = "ingest" if os.path.isfile(LT.KAGELOK_BLEND) else "rebuild"

    def dq20_old_cap():
        spec = dataclasses.replace(dq20, cap=3500)
        LT.clear_scene()
        t, _tb, _s, _i = LT.build_tower_v2(spec, head20, finish=False)
        lods = LT.lod_chain_v2(t, t.name, spec)
        try:
            LT.assert_budgets(t, lods, spec)
        finally:
            free(lods)
    expect_failure("DQ20 held to the old DQ cap 3,500 (C11)", dq20_old_cap)

    # review F3: the pre-Revision-1 pad (bottom 0.15 = the plate bottom) must fail C14's
    # TRIANGLE test - the M8 vertex test alone passed it
    def pre_f3(spec):
        old = LT.crashpad_z0
        LT.crashpad_z0 = lambda s: 0.15
        try:
            build(spec, "rebuild" if spec.family == "DQ" else None)
        except LT.TowerContractError as e:
            check("C14" in str(e) and "triangle" in str(e),
                  f"pre-F3 pad on {spec.key} fails on the C14 triangle test ({str(e)[:70]})")
            raise
        finally:
            LT.crashpad_z0 = old
    expect_failure("FG8 pad from z 0.15 through the flange plate (F3 / C14)", lambda: pre_f3(fg8))
    expect_failure("DQ12 pad from z 0.15 through the flange plate (F3 / C14)", lambda: pre_f3(dq12))

    # review F5: the pre-Revision-1 DQ_LOD2 (braces and every grating deleted) must fail C12 (F5)
    def etdq_lod2():
        LT.clear_scene()
        t, _tb, _s, _i = LT.build_tower_v2(etdq, "rebuild", finish=False)
        lods = LT.lod_chain_v2(t, t.name, etdq)
        try:
            LT.assert_budgets(t, lods, etdq)
        finally:
            free(lods)
    old_lod2 = LT.DQ_LOD1 + ("rail_", "grate_", "rocker_sub_", "ladder_rail_", "flange_",
                             "gantry_brace", "endplate_", "sheave_")
    expect_failure("ETDQ LOD2 deleting the A-braces and deck plates (F5 / C12)",
                   lambda: with_const("DQ_LOD2", old_lod2, etdq_lod2))

    # Revision 1b: the gondola tubular family. Each must fail on ITS contract label.
    gd12, gd20 = LT.VARIANTS["GD12"], LT.VARIANTS["GD20"]

    def fails_on(label, fn):
        try:
            fn()
        except LT.TowerContractError as e:
            check(label in str(e), f"GD wrong build fails on {label} ({str(e)[:80]})")
            raise
    expect_failure("GD deck at the DQ rule lane - 0.45 = 2.55 in the gd8 hanger column (C18)",
                   lambda: fails_on("C18", lambda: _gd_deck_wide(build, gd12)))
    expect_failure("GD carriers at the plain 0.40 stretch break the gondola head rule (C20)",
                   lambda: fails_on("C20", lambda: with_const("GD_CAR_DY", LT.GD_DY,
                                                              lambda: build(gd12, "rebuild"))))
    expect_failure("GD comm wire at axle + 0.45, into the tube top (C19)",
                   lambda: fails_on("C19", lambda: with_const("GD_COMM_DA", 0.45, lambda: build(
                       dataclasses.replace(gd20, comm_y=gd20.axle + 0.45), "rebuild"))))
    expect_failure("GD crash pad at the 0.56 default on the r 0.50 mast (C8)",
                   lambda: fails_on("C8", lambda: with_const("GD_PAD_R", 0.56, lambda: build(gd12, "rebuild"))))
    expect_failure("GD spec top 1 cm off (C3)",
                   lambda: fails_on("C3", lambda: build(dataclasses.replace(gd20, top=gd20.top + 0.01), "rebuild")))
    # Revision 1c (revision1b-check-gd-towers B): the shipped-before GD train - the middle sub
    # 0.125 m under the yoke, its pin's inboard end in air - and a link short of the yoke
    expect_failure("GD12 middle sub-rocker without its link: the pin ends in air (C21)",
                   lambda: fails_on("C21", lambda: with_const("DQ_LINK", None, lambda: build(gd12, "rebuild"))))
    expect_failure("GD20 middle-sub link stopping 3 cm under the yoke (C21)",
                   lambda: fails_on("C21", lambda: with_const("DQ_LINK", dict(LT.DQ_LINK, bury=-0.03),
                                                              lambda: build(gd20, "rebuild"))))


def _gd_deck_wide(build, spec):
    """The GD deck strip at the DQ rule (outer edge lane - 0.45 = 2.55, rails 2.45) against the
    real gd8 column: deck_y / rail_y are patched, the column is the shipped one."""
    old_d, old_r = LT.deck_y, LT.rail_y
    LT.deck_y = lambda s: (LT.DQ_DECK_Y0, 2.55) if s.family == "GD" else old_d(s)
    LT.rail_y = lambda s: 2.45 if s.family == "GD" else old_r(s)
    try:
        build(spec, "rebuild")
    finally:
        LT.deck_y, LT.rail_y = old_d, old_r


def _bake_variant(key, out, extra=(), lods=False):
    """main_v2 at the SHIPPING atlas (1024; the DT-C7 / C8 / C10 gates are defined there), AO 8
    samples, no LODs (unless `lods`) / validation; returns (asset dir, manifest, the baked tower object)."""
    spec = LT.VARIANTS[key]
    name = spec.name_stem + LT.V2_LETTER
    LT.main_v2(["--variant", key, "--out", out, "--ao-samples", "8"] + ([] if lods else ["--no-lods"])
               + ["--no-validate"] + list(extra))
    d = os.path.join(out, name)
    with open(os.path.join(d, f"{name}.manifest.json"), encoding="utf-8") as fh:
        m = json.load(fh)
    obj = bpy.data.objects.get(name + "Src") or bpy.data.objects.get(name)
    return d, m, obj, name


def bake_case():
    """Detail pass 1 bake smoke (design 6.2: FG8 + DQ16 + GD20) at the shipping atlas, checked on
    the written PNGs: the channel rules; DT-C7 plates (ControlMask R + G = 0 on both plate rects,
    >= 300 px/m), the warning triangle (R = G = 0 where its coverage >= 0.99, anti-aliased edge),
    the hazard band (yellow and black texels, R = G = 0); DT-C8 / C9 / C10 from the manifest
    (main_v2 raises on them too); DT-C9 b: two translation-identical paint members (rocker_sub
    0 / 1) correlate < 0.9 in BaseColor; DT-C10 / G5: FG8's ControlMask PNG is byte-identical to a
    --weather off bake. The slot-1 albedo: tone and weathering vary it by design (DECISIONS
    171, free slot wear): the p95 under ControlMask R stays at RECOLOR_ALBEDO (0.78-0.84, the
    pre-pass bound + chalk), the median sits lower (AO + grime: 0.64-0.68 measured)."""
    import hashlib
    work = tempfile.mkdtemp(prefix="tower_bake_")
    try:
        for key in ("FG8", "DQ16", "GD20"):
            d, m, obj, name = _bake_variant(key, os.path.join(work, key))
            dp = m["detail_pass_1"]
            tag = f"bake {key}"
            files = sorted(f for f in os.listdir(d) if f.endswith(".png"))
            check(files == [f"{name}_{x}.png" for x in ("BaseColor", "ControlMask", "MaskMap", "Normal")],
                  f"{tag}: the 4 maps {files}")
            base = cs2_validate.load_png_pixels(os.path.join(d, f"{name}_BaseColor.png"))
            msk = cs2_validate.load_png_pixels(os.path.join(d, f"{name}_MaskMap.png"))
            ctl = cs2_validate.load_png_pixels(os.path.join(d, f"{name}_ControlMask.png"))
            check(base[..., 3].min() > 0.999 and msk[..., 2].max() < 1e-6 and ctl[..., 3].max() < 1e-6
                  and (ctl[..., 0] + ctl[..., 1]).max() <= 1.0 + 1e-3,
                  f"{tag}: BaseColor A 255, MaskMap B 0, ControlMask A 0, R + G <= 1")
            lum = base[..., 0:3].mean(axis=-1)
            sel = ctl[..., 0] > 0.5
            med, p95 = float(np.median(lum[sel])), float(np.percentile(lum[sel], 95))
            check(0.60 <= med <= 0.82 and 0.78 <= p95 <= 0.84,
                  f"{tag}: BaseColor under ControlMask R p95 {p95:.3f} in [0.78, 0.84] (RECOLOR_ALBEDO 0.80 + "
                  f"tone / chalk), median {med:.3f} in [0.60, 0.82] (AO, weather grime)")
            # DT-C7 plates
            for p in dp["plates"]:
                r0, r1, c0, c1 = p["rect"]
                cc = ctl[r0:r1, c0:c1, 0:2]
                check(cc.max() < 1e-6 and p["ppm"] >= 300 and p["glyph_px"] >= 44
                      and (p["numeral"] is None or p["cap_px"] >= 36),
                      f"{tag}: DT-C7 plate rect {p['rect']} ControlMask R + G 0, {p['ppm']} px/m, glyph "
                      f"{p['glyph_px']}, cap {p['cap_px']} ('{p['numeral']}')")
            # DT-C7 pad stamps (coverage recomputed independently of the painter)
            table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
            A = base.shape[0]
            r0, r1, c0, c1 = cs2_compose.uv_bbox_px(obj.data, table, "crashpad_outer", A)
            tri, _mark = LT.triangle_coverage(r1 - r0, c1 - c0)
            pad_h = cs2_asset.CRASHPAD["z1"] - LT.crashpad_z0(LT.VARIANTS[key])
            band, yc = LT.hazard_coverage(r1 - r0, c1 - c0, pad_h)
            cg = ctl[r0:r1, c0:c1, 0:2]
            full = (tri >= 0.99) | (band >= 0.99)
            share = LT._edge_fraction(tri)
            core = (tri >= 0.99) & (_mark < 0.01)
            lc = lum[r0:r1, c0:c1][core]
            check(cg[full].max() < 1e-6 and share >= 0.20 and float(np.percentile(lc, 5)) >= 0.85,
                  f"{tag}: DT-C7 ControlMask R + G 0 under the triangle + band ({int(full.sum())} texels), "
                  f"triangle edge {share:.2f} fractional (manifest {dp['pad']['triangle_edge_fractional']}), "
                  f"core white p5 {float(np.percentile(lc, 5)):.3f}")
            bc = base[r0:r1, c0:c1, 0:3][band >= 0.99]
            yel = (bc[:, 0] > 0.6) & (bc[:, 2] < 0.35)
            blk = bc.max(axis=1) < 0.25
            check(yel.sum() > 50 and blk.sum() > 50, f"{tag}: DT-C7 hazard band yellow {int(yel.sum())} / black "
                                                      f"{int(blk.sum())} texels")
            # DT-C8 / C9 / C10 (manifest)
            w = dp["weather"]
            check(not (dp["relief"] or {}).get("skipped") and dp["tone_gate_ok"] and w["ok"]
                  and w["G5_ctl_identity"] and w["G6_protect_identical"] and w["slot_wear"] == "free"
                  and abs(w["intensity"] - LT.WEATHER_I[LT.VARIANTS[key].family]) < 1e-9,
                  f"{tag}: DT-C8 0 skips, DT-C9 tone gate {dp['tone_gate']['material']}, DT-C10 weather ok at "
                  f"{w['intensity']} (VL1 {w['VL1']}, VL2 {w['VL2']}), G5 / G6 guards")
            # DT-C9 b: translation-identical paint members decorrelated
            ra = cs2_compose.uv_bbox_px(obj.data, table, "rocker_sub_n_0", A)
            rb = cs2_compose.uv_bbox_px(obj.data, table, "rocker_sub_n_1", A)
            h = min(ra[1] - ra[0], rb[1] - rb[0])
            wd = min(ra[3] - ra[2], rb[3] - rb[2])
            la = lum[ra[0]:ra[0] + h, ra[2]:ra[2] + wd].ravel()
            lb = lum[rb[0]:rb[0] + h, rb[2]:rb[2] + wd].ravel()
            cm = (la > 0.05) & (lb > 0.05)
            corr = float(np.corrcoef(la[cm], lb[cm])[0, 1]) if cm.sum() > 50 else 0.0
            check(corr < 0.9, f"{tag}: DT-C9 rocker_sub_n_0 / _n_1 BaseColor correlation {corr:.3f} < 0.9")
            LT.clear_scene()
        # DT-C10 / G5: ControlMask with and without the weather layer
        hs = []
        for wtag in ("on", "off"):
            d, _m, _o, name = _bake_variant("FG8", os.path.join(work, f"g5{wtag}"), ("--weather", wtag))
            with open(os.path.join(d, f"{name}_ControlMask.png"), "rb") as fh:
                hs.append(hashlib.sha1(fh.read()).hexdigest())
            LT.clear_scene()
        check(hs[0] == hs[1], f"bake FG8: DT-C10 ControlMask PNG weather on == off ({hs[0][:12]} / {hs[1][:12]})")
    finally:
        shutil.rmtree(work, ignore_errors=True)


def fbx_black_tris(asset_dir, name, lod=0):
    """The F3 / F3b contract, INDEPENDENT of lift_tower.uv_triangles / subtexel_black_faces / the overlap
    test: import the WRITTEN LODn FBX (already triangles) and read the written BaseColor; lay the
    checker's 400 samples on every triangle (a 20 x 20 midpoint grid folded into the triangle) and look
    each up in the PNG. A triangle FAILS if ANY of its 400 samples is black (sRGB max < 0.02, the
    lattice test's black_faces) - whatever its UV area (the first F3 contract stopped at 4 texel2 and
    missed the long one-texel slivers). Returns ([(triangle index, uv area texel2, black samples)],
    triangle count); the imported object is removed again."""
    fn = f"{name}.fbx" if lod == 0 else f"{name}_LOD{lod}.fbx"
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=os.path.join(asset_dir, fn))
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    base = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{name}_BaseColor.png"))
    A = base.shape[0]
    mx = base[..., :3].max(axis=-1)
    g = (np.arange(20) + 0.5) / 20.0
    sa, sb = np.meshgrid(g, g)
    sa, sb = sa.ravel(), sb.ravel()
    fold = (sa + sb) > 1.0
    sa, sb = np.where(fold, 1.0 - sa, sa), np.where(fold, 1.0 - sb, sb)
    out = []
    try:
        me = new[0].data
        me.calc_loop_triangles()
        n_tri = len(me.loop_triangles)
        tl = np.zeros(n_tri * 3, np.int64)
        me.loop_triangles.foreach_get("loops", tl)
        uv = np.zeros(len(me.loops) * 2)
        me.uv_layers.active.data.foreach_get("uv", uv)
        Q = uv.reshape(-1, 2)[tl.reshape(-1, 3)] * A                       # (n, 3, 2) texels
        e1, e2 = Q[:, 1] - Q[:, 0], Q[:, 2] - Q[:, 0]
        area = 0.5 * np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0])
        S = Q[:, 0, None, :] + sa[None, :, None] * e1[:, None, :] + sb[None, :, None] * e2[:, None, :]
        sx = np.clip(np.floor(S[..., 0]).astype(np.int64), 0, A - 1)
        sy = np.clip(np.floor(S[..., 1]).astype(np.int64), 0, A - 1)
        nblk = (mx[sy, sx] < 0.02).sum(axis=1)
        out = [(int(i), round(float(area[i]), 3), int(nblk[i])) for i in np.nonzero(nblk > 0)[0]]
    finally:
        for o in new:
            me_ = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.meshes.remove(me_)
    return out, n_tri


# F3 / F3b cases: (variant, F3 footprint pass fills something, F3b any-area pass fills something). The
# 18:01-18:10 exports had black triangles on DQ18 (24 small + 2 slivers), GD16 (32 + 22), DQ20 (new,
# 2 slivers after the F3 pass); FG8 has only the 16 platerim slivers (whose centers sit ON an edge, so the
# part mean of its covered texels is black); ET68 has no defect at all and must stay untouched.
# T62 round 3: the round-3 wheel's UV layout moved DQ18's long slivers away (its F3b pass now has nothing to fill)
F3_CASES = (("DQ18", True, False), ("GD16", True, True), ("DQ20", True, True), ("FG8", False, True),
            ("ET68", False, False))


def f3_case():
    """Follow-up 2026-10-11, findings F3 + F3b (black-sampled faces): DQ18, GD16, DQ20, FG8 and ET68 are
    baked at the shipping atlas WITH the LOD chain; no triangle of the written LOD0 / LOD1 / LOD2 FBX,
    of ANY UV area, has a black sample among 400 on the written BaseColor (independent count), the
    generator reports the same (black_after 0 at LOD0 / 1 / 2, nothing unfillable), and each fill pass
    ran exactly where there was something to fill (ET68, no defect, fills nothing). Wrong builds: the
    fill replaced by a no-op (DQ18) and only the F3b pass disabled (DQ20 keeps the pre-F3b long slivers)
    -> the generator's DT-F3 gate raises and the independent counter flags the triangles."""
    work = tempfile.mkdtemp(prefix="tower_f3_")
    try:
        for key, f3_fills, f3b_fills in F3_CASES:
            d, m, obj, name = _bake_variant(key, os.path.join(work, key), lods=True)
            sf = m["detail_pass_1"]["subtexel_fill"]
            res = {lod: fbx_black_tris(d, name, lod) for lod in (0, 1, 2)}
            bad0, n_tri = res[0]
            check(all(not r[0] for r in res.values()) and n_tri == LEDGER[key] and sf["black_after"] == 0
                  and not any(sf["black_after_lods"].values()) and not sf["slivers_unfilled"],
                  f"F3/F3b {key}: 0 of {n_tri} / {res[1][1]} / {res[2][1]} written LOD0 / 1 / 2 triangles have a black "
                  f"sample among 400 at any UV area (independent count {[len(r[0]) for r in res.values()]}, generator "
                  f"black_after {sf['black_after']} / LODs {sf['black_after_lods']}); F3 filled {sf['faces']} triangles / "
                  f"{sf['texels']} texels {sf['by_class']}; F3b filled {sf['f3b_faces']} / {sf['f3b_texels']} texels "
                  f"{sf['f3b_by_class']}; unfillable {sf['slivers_unfilled']}")
            check((sf["faces"] > 0) == f3_fills and (sf["f3b_faces"] > 0) == f3b_fills,
                  f"F3/F3b {key}: the F3 pass {'ran on' if f3_fills else 'stayed out of'} the variant ({sf['faces']}), the "
                  f"F3b pass {'ran on' if f3b_fills else 'stayed out of'} it ({sf['f3b_faces']})")
            LT.clear_scene()
        orig_fill, orig_assert, orig_lin = LT.fill_subtexel_faces, LT.assert_detail_pass, LT.SUBTEXEL_FILL_LIN
        seen = []

        def no_fill(*_a, **_k):
            return {"faces": 0, "texels": 0, "by_class": {}, "f3b_faces": 0, "f3b_texels": 0, "f3b_by_class": {},
                    "slivers_unfilled": {}}

        def recording_assert(spec, rep, **kw):
            try:
                orig_assert(spec, rep, **kw)
            except LT.TowerContractError as e:
                seen.append(str(e))
        # wrong build 1: no fill at all (the generator's gate recorded instead of aborting the export)
        LT.fill_subtexel_faces, LT.assert_detail_pass = no_fill, recording_assert
        try:
            d, m, obj, name = _bake_variant("DQ18", os.path.join(work, "nofill"))
        finally:
            LT.fill_subtexel_faces, LT.assert_detail_pass = orig_fill, orig_assert
        bad, _n = fbx_black_tris(d, name)
        WRONG.append("F3: the sub-texel fill disabled on DQ18")
        check(any("DT-F3" in e for e in seen) and len(bad) >= 20 and m["detail_pass_1"]["subtexel_fill"]["black_after"] >= 20,
              f"wrong build fails: DQ18 without the fill -> the generator's DT-F3 gate raises ({len(seen)}), the "
              f"independent counter flags {len(bad)} black triangles (generator "
              f"{m['detail_pass_1']['subtexel_fill']['black_after']})")
        LT.clear_scene()
        # wrong build 2: the F3 pass runs, the F3b pass is inert (no texel counts as black for it) -> the
        # long one-texel slivers stay black, as in the 18:01-21:00 exports (DQ20: the fall-arrest channel walls)
        seen.clear()
        LT.assert_detail_pass, LT.SUBTEXEL_FILL_LIN = recording_assert, 0.0
        try:
            d, m, obj, name = _bake_variant("DQ20", os.path.join(work, "nof3b"))
        finally:
            LT.assert_detail_pass, LT.SUBTEXEL_FILL_LIN = orig_assert, orig_lin
        bad, _n = fbx_black_tris(d, name)
        sf = m["detail_pass_1"]["subtexel_fill"]
        WRONG.append("F3b: only the F3 footprint pass (long slivers left black) on DQ20")
        check(any("DT-F3" in e for e in seen) and len(bad) >= 2 and sf["black_after"] >= 2 and sf["f3b_faces"] == 0
              and all(a >= LT.SUBTEXEL_TEXEL2 for _i, a, _k in bad),
              f"wrong build fails: DQ20 with the F3 pass only -> the generator's DT-F3 gate raises ({len(seen)}), the "
              f"independent counter flags {len(bad)} black long slivers (UV area {[a for _i, a, _k in bad]} texel2; "
              f"generator {sf['black_after']})")
        LT.clear_scene()
    finally:
        shutil.rmtree(work, ignore_errors=True)


def review_case():
    """towers-v2 section 14.6 (pipeline review 7): FG8 through main_v2 - export, settings.json,
    manifest, validation and the review hook - twice in this process, without and with
    --review, at --atlas 512 --ao-samples 4 (512 = the validator floor). Every file in the asset folder must be
    byte-identical (FBX header clock frozen as tests/test_review_side_effects.py; same process
    = same FBX UID salt); the manifests equal without written_utc; the pack lands outside."""
    import datetime
    import hashlib
    import types
    import io_scene_fbx.export_fbx_bin as fbxbin

    class _Frozen(datetime.datetime):
        @classmethod
        def now(cls, tz=None):
            return datetime.datetime(2026, 1, 1, 0, 0, 0)

    saved = fbxbin.datetime
    fbxbin.datetime = types.SimpleNamespace(datetime=_Frozen)
    work = tempfile.mkdtemp(prefix="tower_review_")
    name = "SkiLiftTowerFG8O"

    def run(out, review=None):
        LT.main_v2(["--variant", "FG8", "--out", out, "--atlas", "512", "--ao-samples", "4"]
                   + (["--review", review] if review else []))
        d = os.path.join(out, name)
        hs = {}
        for fn in sorted(os.listdir(d)):
            with open(os.path.join(d, fn), "rb") as fh:
                hs[fn] = hashlib.sha1(fh.read()).hexdigest()
        return d, hs

    try:
        a, ha = run(os.path.join(work, "plain"))
        pack = os.path.join(work, "pack")
        b, hb = run(os.path.join(work, "reviewed"), pack)
        check(sorted(ha) == sorted(hb) and len(ha) == 9,
              f"main_v2 FG8 --review: same 9 files ({sorted(ha)})")
        mf = f"{name}.manifest.json"
        diff = [fn for fn in sorted(ha) if fn != mf and ha[fn] != hb.get(fn)]
        check(not diff, f"main_v2 FG8 --review: every file byte-identical (differ: {diff})")
        cores = []
        for d in (a, b):
            with open(os.path.join(d, mf), encoding="utf-8") as fh:
                m = json.load(fh)
            m.pop("written_utc", None)
            cores.append(m)
        check(cores[0] == cores[1], "main_v2 FG8 --review: manifests equal without written_utc")
        check(os.path.isfile(os.path.join(pack, f"{name}.blend"))
              and not any(f.endswith(".blend") for f in os.listdir(b)),
              "main_v2 FG8 --review: pack written outside the asset folder")
    finally:
        fbxbin.datetime = saved
        shutil.rmtree(work, ignore_errors=True)


def main():
    t0 = time.perf_counter()
    have_kag = os.path.isfile(LT.KAGELOK_BLEND)
    counts = {}
    for key in ("FG8", "FG12", "FG16", "FG20", "ET62", "ET68"):
        counts[key] = variant_case(key)
    for key in ("DQ10", "DQ12", "DQ14", "DQ16", "DQ18", "DQ20"):
        counts[f"{key}/rebuild"] = variant_case(key, "rebuild")
        if have_kag:
            counts[f"{key}/ingest"] = variant_case(key, "ingest")
        else:
            print(f"[contracts] SKIP {key} ingest: Kagelok .blend absent")
    counts["ETDQ"] = variant_case("ETDQ", "rebuild")
    for key in ("GD12", "GD16", "GD20"):              # Revision 1b: the gondola tubular family
        counts[f"{key}/rebuild"] = variant_case(key, "rebuild")
    counts["ETGD"] = variant_case("ETGD", "rebuild")  # detail pass 1: the gondola entry tower
    etgd_station_case()
    gd_seam_case()
    scoring_case()
    check(LT.LOD0_CAP == CAPS and all(sp.cap == CAPS[sp.family] for sp in LT.VARIANTS.values()),
          f"per-family LOD0 caps {LT.LOD0_CAP}: every spec carries its family's cap; DQ18 margin "
          f"{CAPS['DQ'] - LEDGER['DQ18']}, DQ20 margin {CAPS['DQ'] - LEDGER['DQ20']} tris")
    negative_cases()
    dp1_negative_cases()
    bake_case()
    f3_case()
    review_case()
    print("[contracts] tris " + json.dumps({k: list(v.values()) for k, v in counts.items()}))
    if FAILS:
        print(f"FAIL test_tower_contracts: {len(FAILS)} failure(s): {FAILS[:6]}")
        sys.exit(1)
    print(f"PASS test_tower_contracts: {len(counts)} variant builds hold every contract (GD12/16/20 "
          f"incl.), {len(WRONG)} wrong builds fail, FG/DQ/GD node-elevation scoring (FG20/DQ16, GD + "
          f"lattice) OK, GD == tram_tower / carriers gd8, FG8 bake channel rules + "
          f"warning triangle OK, main_v2 FG8 --review byte-identical; detail pass 1: DQ18 + ETGD, DT-C2..C10 "
          f"+ M1 / M3 / M4 / M9 checks, ETGD station seam, FG8 / DQ16 / GD20 shipping-atlas bakes; "
          f"follow-up: DQ20 beside DQ18 under the 3,600 DQ cap (scoring stage 2), F3 + F3b no "
          f"black-sampled triangle of any UV area at LOD0 / 1 / 2 (DQ18 / GD16 / DQ20 / FG8 / ET68 bakes "
          f"+ the no-fill and the F3-only negatives) "
          f"({time.perf_counter() - t0:.0f} s)")


try:
    main()
except SystemExit:
    raise
except Exception:  # noqa: BLE001
    import traceback
    traceback.print_exc()
    print("FAIL test_tower_contracts: exception")
    sys.exit(1)
