"""
test_detail_detach.py - detail pass 1, DETACH group (design/detail-pass-1-lifts.md section 9.2 "detach" as
amended by design/detail-pass-1-lifts-skeptic.md MC1-MC11; build report build/detail-pass-1-lifts-detach.md).

  D1  counts per dressing family on a blockout build (base / top), the dressing's LOD policy (every new family
      leaves at LOD1; nothing new at LOD2). DQ follow-up (2026-10-11, build/dq-stations-followup.md): no flood mast
      (vanilla LightpoleParkRandom01 rows instead), the SLOW boards on two posts each, the shared rack gear = one
      part per matkey (ski_b_ x 5).
  D11 the board order along the chair's travel (assert_board_order, measured on the build): base WAIT HERE ->
      carpet -> load line -> LOWER BAR -> SKI TIPS UP; top RAISE BAR -> UNLOAD at the unload line, nothing past it.
      2026-10-12 (Richard, nineteenth look: "We don't need the STAY SEATED at the top DQ station"): the top has NO
      STAY SEATED board (post / rods / panel / label / atlas rect), in both FINE_DETAIL modes; the base is unchanged.
      Negatives raise D11 - a STAY SEATED on the top at its old slot (x -2.50), at the detail pass's x 2.20, and as a
      free-standing post board (any mount), the top's RAISE BAR / UNLOAD swapped, and a swapped base; the shipped
      top manifest records `forbidden` STAY SEATED and its label table has no `stay`; two badly placed lamp posts
      raise D8. Follow-up resume (2026-10-12): the first attempt's two apron lamps
      (y -5.40 facing +Y, heads 0.09 m off band_n) raise D8 (lamp-head standoff D1_LAMP_HEAD_CLEAR 0.30 m), a post
      off the -Y deck edge raises D9 (no deck face under its origin, C16-style).
  D6  the frozen prefab numbers (design 9.1, read from the pre-pass manifests and hard-coded here) out of
      prefab_numbers + the authoring bounds; the LOD0 targets of this pass (11,000 / 8,000).
  D7  access, ONE rule (MC3): R2e (roof route), R2f (pf ladder off the spine, MC5), R2g (ground flight corridor)
      pass inside assert_contracts; negatives - the 1c front ladder (ROOF_ROUTE_D1 off: y -0.70, the closed outer
      rail) raises R2e, a box in the base front ladder's zone raises R2e, the pf ladder pushed into the spine
      (PF_Y 0.45) raises R2f, the top's draft ladder-way 0.60 raises R2c (gate 0.61); and the REAL hosted ETDQ
      (lift_tower rebuild, at its hosted pose) puts no face in the base front ladder's zone above its deck top -
      its -Y deck strip (top 3.404) is the bottom stop's standing surface.
  D8  MC10: assert_detail1 passes (every dressing part on its real faces, every vanilla footprint with the
      variant-union bounds); negatives - the design's DB6 slot (x -6.50) raises D8 (commcable_nose), DT4 at the
      design's x 2.60 raises D8 (crosshead_r), the design's ParkInfosign spot (13.45, 2.70) raises D8 (C8 bench).
  D9  sub-object rows: verified GUIDs (cs2_liftdress.SUBOBJ), game-local positions, yaw-only quaternions, parent 0.
  D4  weathering on vs off (DQ base, two 512 bakes through main()): ControlMask byte-identical, every reserved rect
      (sign + labels) identical in BaseColor / Normal / MaskMap / ControlMask, BaseColor differs elsewhere (and no
      Emissive map in either: 2026-10-12, the DQ ships four maps).
  SHIPPED (assets/import/stations/detachable/A at 2048, the export of this pass):
      budgets - LOD0 <= 11,000 / 8,000, LOD1 <= 5,000 / 3,600 and <= 50 % of LOD0, LOD2 < 500, batches 9;
      D10 (MC11 floors from the 1c baselines measured on the shipped 1c packs, 2026-10-11): w1 >= 0.95 x baseline, no
      visible class under 14 px/m, deck >= 1.10 x baseline, ped / crosshead / spine >= 40 px/m (a 0.025 m bolt head
      is 2 texels), roof >= 0.95 x baseline, the compose log drew bolts and gratings, bay sharing on;
      D3 labels 400 px/m (>= 380 per axis), T2 everywhere, T1 where claimed, ControlMask 0 under each label;
      D2 emissive lit texels >= the 1c baseline and <= 1.5 % of the atlas, no cool flood-head texels (follow-up);
      D9 10 + 2 / 7 + 2 rows legacy, 14 + 7 / 11 + 6 fine (T17-DQ 2026-10-16: no lamp posts, 2 SkiPisteFloodlightLow each); D11 recorded;
      weathering at the highest budget-clean step (vanilla ok, snow 0, ControlMask untouched, protected rects kept).

  FINE-DETAIL step 3b (2026-10-12, build/fine-detail-step3b-stations.md; lift_terminal_detach.FINE_DETAIL, default on;
  every group runs on the DQ base / top AND the gondola base / top where it applies):
  F0  the switch: D1 counts with FINE_DETAIL off = the pre-pass table (D1_COUNTS_LEGACY: the approved build), on = D1_COUNTS
      (the kit's families leave the host, the DQ box lamps go); D6 frozen numbers identical both ways; the weather mast wx*
      vertex-identical both ways (FS1) and the LIFT boards / labels untouched (FS5: same label table, D11 order).
  F1  assert_kit_rows passes on all four; negatives raise F1: a kit hose reel pushed 5 cm into the DQ top cabin wall, the DQ
      base reel at its OLD spot beside the engine-room door (its 0.70 m plate over the door / past the corner), the DQ base
      reel on the engine room's -Y face behind the cabinets (no access, FD_ACCESS), the DQ base
      tools 3 cm off the wall (touch nothing), a kit e-stop in the C8 bench envelope, the gondola tools on the stone face
      without the ledge offset (they cut the coping), the DQ base rack pushed into the ParkInfosign's box, a kit CID drift.
  F2  H1 placement against the removed parts (a FINE_DETAIL-off build): the kit wall rack's frame = the removed one-sided
      frame vertex for vertex (<= 1 mm after the measured foot drop), the e-stop post axis and foot, the extinguisher body
      axis and bottom, the hose-reel plate (same size: corners <= 1 mm; DQ base moved / gondola resized: centre reported),
      the tools = the removed pair + the foot-line move (n x 0.0725 m on a plain wall; the gondola ledge case reported)
      vertex for vertex.
  F3  fine geometry: maze posts 24 tris (8-seg, fan cap; was 12), ropes 12 (6-sided, was 8), gate stiles 24, the SLOW posts 36
      (12-seg; was 18), the hang rods 6-seg; the control cabinets without finish bevel strips (bevel_prefixes) + a 12-tri lid; no
      lamp_ part on the DQ (lamp_s0-3 / cab / eng are RoofLight / WallLight rows: 4 new soffit rows, the 2 / 1 wall-light
      rows moved onto the old lenses).
  SHIPPED (fine): the rows 14 + 7 / 11 + 6 (vanilla + mod, T17-DQ 2026-10-16: kit 5 / 4 + 2 floodlights) with the manifest's fine_detail block, D2 emissive: NO
      _Emissive map on the DQ (no lamp_ part; 2026-10-12: not on disk, not in the manifest's files, 0 lit texels and the
      reason in detail1.emissive, contract.emissive map / purpose / intensity null, settings.json lists no Emissive),
      the gondola rows 16 + 9 / 15 + 9 (the gondola keeps its map: lamp_eave*).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_detail_detach.py
"""
import json
import math
import os
import shutil
import sys
import tempfile
import time

import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_asset  # noqa: E402
import cs2_liftdress as LD  # noqa: E402
import cs2_validate  # noqa: E402
import lift_terminal  # noqa: E402
import lift_terminal_detach as D  # noqa: E402

REPO = D.REPO
SHIPPED = os.path.join(REPO, "assets", "import", "stations", "detachable", "A")
FAILS = []
# the 1c baselines (MC11): measured 2026-10-11 on the shipped 1c review packs / exports before this pass
BASELINE = {"base": {"w1": 32.51, "deck": 16.2, "roof": 48.7, "lit": 4418},
            "top": {"w1": 33.61, "deck": 16.8, "roof": 50.4, "lit": 4054}}
LOD1_CAP = {"base": 5000, "top": 3600}
# design 9.1 frozen numbers (authoring bounds via prefab_numbers; the pre-pass manifests)
FROZEN = {
    "base": {"wheel_local_prefab": [0.0, 3.7, 9.6], "lane_rope_y": 3.7, "deck_top_y": 0.3, "soffit_y": 3.35,
             "band_top_or_eave_y": 6.15, "crown_y": 8.4422, "stub_tip_local_z": -16.0,
             "stub_hv": [[0.0, 0.0, -13.0], [0.0, 0.0, -16.0]], "stub_lv": [[6.0, 0.0, -13.0], [6.0, 0.0, -16.0]],
             "lv_pole": [6.0, 0.0, -16.0], "lot": [2, 4],
             "bounds_prefab": {"min": [-7.5, 0.0, -11.0], "max": [7.97, 9.6422, 16.0]},
             "anchor_comm_a": [0.0, 5.85, -8.275], "hosted": [0.0, 0.0, -11.0], "nso_y": 8.25},
    "top": {"wheel_local_prefab": [0.0, 3.7, 5.6], "lane_rope_y": 3.7, "deck_top_y": 0.3, "soffit_y": 3.35,
            "band_top_or_eave_y": 5.75, "crown_y": 8.0422, "stub_tip_local_z": -16.0,
            "stub_hv": [[0.0, 0.0, -13.0], [0.0, 0.0, -16.0]], "stub_lv": None, "lv_pole": None, "lot": [2, 4],
            # nitpick A (2026-10-13, Richard S-1): the top's operator cabin moved from the +Y side (its roof + drips
            # reached 7.97) onto the -Y apron beside the unload lane: prefab x max = the deck's +Y edge 5.50 (the
            # bounds are the mesh's; recorded deviation, build/nitpick-a-stations.md)
            "bounds_prefab": {"min": [-7.5, 0.0, -12.1], "max": [5.5, 9.2422, 12.0]},
            "anchor_comm_a": [0.0, 5.45, -10.275], "hosted": None, "nso_y": 8.25},
}
D1_COUNTS_LEGACY = {   # FINE_DETAIL off (the approved pre-pass): family prefix -> (base, top) part counts
    # DQ follow-up: the flood masts are gone (flood_ / lamp_flood 0; vanilla LightpoleParkRandom01 rows
    # instead), the SLOW boards stand on two posts each (board_ +2), the shared rack gear (cs2_bldg.ski_gear,
    # bindings off) makes ONE part per matkey: ski_b_K / W / A / B / R (5, was one per ski: 9)
    "maze_post_": (9, 0), "maze_rope_": (14, 0), "maze_flag_": (11, 0), "gate_ld_": (5, 0), "carpet_": (3, 0),
    # 2026-10-12 STAY SEATED removed at the top (board_stay + 2 rods + label_stay): label_ 6 -> 5, board_ 13 -> 10 (both modes)
    "label_": (9, 5), "board_": (11, 10), "label_stay": (0, 0), "board_stay": (0, 0),
    "flood_": (0, 0), "lamp_flood": (0, 0), "estop_": (3, 3), "ext_": (3, 3),
    "hosereel_": (4, 4), "tool_": (2, 2), "skirack_": (6, 0), "ski_b_": (5, 0), "chev_": (10, 6), "stripe_": (3, 3),
    "hatch_sofpf": (2, 2), "gauge_": (0, 2), "doorlite_": (1, 1), "sill_": (1, 1), "lamp_": (6, 5),
    "cabinet_": (3, 3), "wx": (3, 3),
}
# fine-detail step 3b (FINE_DETAIL on): the e-stop, extinguisher, hose reel, tools and the one-sided rack (frame +
# gear) are kit rows; the box lamps are vanilla rows; every cabinet gets a lid part; the weather mast stays (FS1)
D1_COUNTS = dict(D1_COUNTS_LEGACY, **{"estop_": (0, 0), "ext_": (0, 0), "hosereel_": (0, 0), "tool_": (0, 0),
                                      "skirack_": (0, 0), "ski_b_": (0, 0), "lamp_": (0, 0), "cabinet_": (6, 6)})
# D11 the board order along the chair's travel, as the shipped contract (2026-10-12: the top lost STAY SEATED)
D11_SEQ = {"base": ("waithere", "carpet", "load", "lowerbar", "tipsup"), "top": ("raisebar", "unload")}
# T17-DQ 2026-10-16: lamp posts -> SkiPisteFloodlightLow (Richard): the 4 vanilla LightpoleParkRandom01 rows per DQ /
# gondola station are gone; 2 SkiPisteFloodlightLow "mod" rows stand per station (so vanilla -4, "kit" (mod) +2)
D9_ROWS_LEGACY = {"base": (10, 2), "top": (7, 2)}   # (vanilla, mod) - detail pass 10 / 7 vanilla rows + 2 floodlights
# fine-detail: (vanilla, mod) - + 4 RoofLightRandom01 on the soffit lenses each; the wall lights moved, not added;
# the mod count = the dressing kit rows (base 5, top 4) + the 2 floodlights
D9_ROWS = {"base": (14, 7), "top": (11, 6)}
GD_ROWS = {"base": (16, 9), "top": (15, 9)}   # gondola: vanilla 20 / 19 - 4 lamp posts, kit 7 + 2 floodlights
D9_LAMPS = 0
FLOOD_PREFAB = "SkiPisteFloodlightLow"
FLOOD_CID = "CID:6827e281427c087a3b7ef19c824ca28b"
FLOOD_MESH = "SkiPisteFloodlightLowA Mesh"
# literal rows (game local pos, yaw quaternion (x, y, z, w)): yaw 180 turned toward the line axis by
# atan(|x| / 17 m) rounded to 5 degrees (d1_flood_yaw): x -5.1 -> 165, 7.1 -> 205, 5.3 -> 195, +-7.2 -> 155 / 205
D9_FLOODS = {"base": [((-5.1, 0.3, 2.4), (0.0, 0.9914449, 0.0, 0.1305262)),
                      ((7.1, 0.3, 5.2), (0.0, 0.976296, 0.0, -0.2164396))],
             "top": [((5.3, 0.3, -9.6), (0.0, 0.9914449, 0.0, -0.1305262)),
                     ((7.1, 0.3, 0.0), (0.0, 0.976296, 0.0, -0.2164396))]}
# gondola: the portal-face corner pair (x_nose - 1.0 = -9 / -11), on grade; authoring y -7.2 / +7.2 = game x +7.2 / -7.2
GD_FLOODS = {"base": [((7.2, 0.0, -9.0), (0.0, 0.976296, 0.0, -0.2164396)),
                      ((-7.2, 0.0, -9.0), (0.0, 0.976296, 0.0, 0.2164396))],
             "top": [((7.2, 0.0, -11.0), (0.0, 0.976296, 0.0, -0.2164396)),
                     ((-7.2, 0.0, -11.0), (0.0, 0.976296, 0.0, 0.2164396))]}


def flood_rows_ok(objs_all, want):
    """The SkiPisteFloodlightLow rows of a manifest block == the literal `want` [(pos, quat)] (CID, mesh, parent 0,
    mod kind), and no vanilla LightpolePark row."""
    fl = [o for o in objs_all if o["prefab"] == FLOOD_PREFAB]
    return (len(fl) == len(want)
            and not any(str(o["prefab"]).startswith("LightpolePark") for o in objs_all)
            and all(o.get("kind") == "mod" and o["guid"] == FLOOD_CID and o["mesh"] == FLOOD_MESH and o["parent"] == 0
                    and all(abs(a - b) < 1e-6 for a, b in zip(o["pos"], pw))
                    and all(abs(a - b) < 1e-6 for a, b in zip(o["rot"], qw))
                    for o, (pw, qw) in zip(fl, want)))
KIT_H1_TOL = 0.001                  # F2: kit vs removed part, metres


def check(cond, label):
    print(f"[d1-test] {'ok  ' if cond else 'FAIL'} {label}")
    if not cond:
        FAILS.append(label)
    return cond


def build(kind, finish=False, gondola=False, fine=True):
    lift_terminal.clear_scene()
    saved = D.FINE_DETAIL
    D.FINE_DETAIL = fine
    try:
        p = D.params_for(kind, gondola, D.load_carrier("gd8" if gondola else "dq"))
        parts = D.build_station(p)
        t, table, _src = D.assemble(parts, finish, p)
    finally:
        D.FINE_DETAIL = saved
    return p, parts, t, table


def raises(label, fn, want):
    try:
        fn()
    except D.ContractError as e:
        return check(str(e).startswith(want), f"{label} raises ({str(e)[:130]})")
    return check(False, f"{label} did NOT raise")


def raises_has(label, fn, want, needle):
    """raises(), and the message must also name the cause (`needle`)."""
    try:
        fn()
    except D.ContractError as e:
        return check(str(e).startswith(want) and needle in str(e), f"{label} raises ({str(e)[:150]})")
    return check(False, f"{label} did NOT raise")


# --------------------------------------------------------------------------------------
# D1 / D6 / D9 on blockouts
# --------------------------------------------------------------------------------------

def counts_and_frozen():
    for kind in ("base", "top"):
        for fine in (True, False):
            counts_and_frozen_one(kind, fine)


def counts_and_frozen_one(kind, fine):
    saved = D.FINE_DETAIL
    try:
        D.FINE_DETAIL = fine
        _counts_and_frozen_one(kind, fine)
    finally:
        D.FINE_DETAIL = saved


def _counts_and_frozen_one(kind, fine):
    if True:
        col = 0 if kind == "base" else 1
        p, parts, t, table = build(kind, fine=fine)
        kind_ = kind
        kind = f"{kind_} {'fine' if fine else 'legacy'}"
        names = [o for o in table]
        for pre, want in sorted((D1_COUNTS if fine else D1_COUNTS_LEGACY).items()):
            n = sum(1 for x in names if x.startswith(pre))
            check(n == want[col], f"{kind} D1 {pre} x {n} (want {want[col]})")
        dress = D._dress_names(p, table)
        lod1 = [n for n in dress if not n.startswith(D.LOD1_DELETE)]
        lod2 = [n for n in dress if not n.startswith(D.LOD2_DELETE)]
        check(not lod1 and not lod2,
              f"{kind} D1 LOD policy: every dressing part leaves at LOD1 ({sorted(lod1)} kept; the flood shafts "
              f"that stayed to LOD2 are gone with the masts), nothing new at LOD2")
        co = D._co(t.data)
        nums = D.prefab_numbers(p, (co.min(axis=0), co.max(axis=0)))
        fz = FROZEN[kind_]
        for k in ("wheel_local_prefab", "lane_rope_y", "deck_top_y", "soffit_y", "band_top_or_eave_y",
                  "crown_y", "stub_tip_local_z", "stub_hv", "stub_lv", "lv_pole", "lot"):
            got = nums[k]
            ok = (got == fz[k]) if not isinstance(got, float) else abs(got - fz[k]) < 1e-3
            check(ok, f"{kind} D6 frozen {k} {got}")
        bp = nums["bounds_prefab"]
        check(np.allclose(bp["min"], fz["bounds_prefab"]["min"], atol=1e-3)
              and np.allclose(bp["max"], fz["bounds_prefab"]["max"], atol=1e-3),
              f"{kind} D6 frozen bounds {bp}")
        check(np.allclose(nums["anchor_comm"]["a"], fz["anchor_comm_a"], atol=1e-3),
              f"{kind} D6 frozen LV comm anchor a {nums['anchor_comm']['a']}")
        ht = nums["hosted_tower"]
        check((ht is None and fz["hosted"] is None) or (ht and ht["position"] == fz["hosted"]
                                                        and ht["rotation_quat"] == [0, 1, 0, 0]),
              f"{kind} D6 frozen hosted tower {ht and ht['position']}")
        check(abs(nums["net_sub_objects_y"]["y"] - fz["nso_y"]) < 1e-6, f"{kind} D6 NSO y 8.25")
        check(D.expected_room_ids(p) == [7, 13], f"{kind} D6 _Win room ids [7, 13]")
        # D9 rows (fine: vanilla + the kit's mod rows by staged CID)
        rows = D.subobject_rows(p, p.stem + "A")
        guids = {"UnityGUID:" + v["guid"] for v in LD.SUBOBJ.values()}
        objs_all = rows["objects"]
        objs = [o for o in objs_all if o.get("kind") != "mod"]
        mods = [o for o in objs_all if o.get("kind") == "mod"]
        yaw_only = all(abs(o["rot"][0]) < 1e-9 and abs(o["rot"][2]) < 1e-9 for o in objs_all)
        want_v, want_m = D9_ROWS[kind_] if fine else D9_ROWS_LEGACY[kind_]
        check(len(objs) == want_v and len(mods) == want_m and all(o["guid"] in guids for o in objs)
              and yaw_only and all(o["parent"] == 0 for o in objs_all)
              and rows["target_prefab"] == p.stem + "Building"
              # T17-DQ 2026-10-16: the floodlight "mod" rows make the block fine=True in both modes
              and rows.get("kinds") == {"vanilla": want_v, "mod": want_m},
              f"{kind} D9 {len(objs)} vanilla + {len(mods)} kit rows (verified GUIDs / CIDs, yaw only, parent 0) -> "
              f"{rows['target_prefab']}; kinds {rows.get('kinds')}")
        if fine:
            import fine_detail as FD
            # kit colour variants (2026-10-12): a coloured prop's row names its colour-set variant's CID
            # (T17-DQ 2026-10-16: the SkiPisteFloodlightLow "mod" rows are not kit props; asserted literally above)
            kit_mods = [o for o in mods if o["prefab"] != FLOOD_PREFAB]
            check(all(o["guid"] == "CID:" + FD.kit_cid(o["prefab"], o.get("colour_set"))
                      == "CID:" + str(FD.staged_kit_cid(o["prefab"], o.get("colour_set")))
                      for o in kit_mods) and "x_convention" in rows,
                  f"{kind} F0 kit rows by staged CID {sorted(o['prefab'] for o in kit_mods)}, frame stated")
            soff = [o for o in objs if o["prefab"] == "RoofLightRandom01" and abs(abs(o["pos"][0]) - 3.6) < 1e-6]
            wl = [o for o in objs if o["prefab"] == "WallLightShortRangeRandom01"]
            check(len(soff) == 4 and all(abs(o["pos"][1] - p.z_soffit) < 1e-6 for o in soff)
                  and sorted(round(o["pos"][2], 3) for o in soff) == sorted(round(p.x_wheel - dx, 3) for dx in (1.5, 1.5, 4.5, 4.5))
                  and len(wl) == (2 if kind_ == "base" else 1)
                  # nitpick A (2026-10-13): the top's cabin (and its door lamp row) moved onto the -Y apron's deck:
                  # the lens over the moved door (cab_layout door_y), 2.23 over the cabin floor (deck 0.30)
                  and all(abs(o["pos"][1] - (2.38 + D.cab_layout(p)["zf"] - 0.15)) < 1e-6 for o in wl)
                  # T14 x-sign N (2026-10-16): the rows are in the game frame, x -> -x (rule T); design door_y -> -door_y
                  and sorted(round(o["pos"][0], 3) for o in wl) == ([-6.7, -6.35] if kind_ == "base"
                                                                     else [round(-D.cab_layout(p)["door_y"], 3)]),
                  f"{kind} F3 lamps: 4 RoofLight rows on the old soffit lenses (game x +-3.6, y {p.z_soffit}, z "
                  f"{sorted(round(o['pos'][2], 2) for o in soff)}), the wall-light rows moved onto lamp_cab / lamp_eng "
                  f"{[o['pos'] for o in wl]}")
        # T17-DQ 2026-10-16: lamp posts -> SkiPisteFloodlightLow (Richard): no vanilla LightpoleParkRandom01 row,
        # two floodlight "mod" rows on the deck top, no modelled flood mast
        lamps = [o for o in objs if o["prefab"] == "LightpoleParkRandom01"]
        floods = [o for o in objs_all if o["prefab"] == FLOOD_PREFAB]
        check(len(lamps) == D9_LAMPS and flood_rows_ok(objs_all, D9_FLOODS[kind_])
              and all(abs(o["pos"][1] - D.DECK_H) < 1e-6 for o in floods)
              and not any(n.startswith(("flood", "lamp_flood")) for n in table),
              f"{kind} T17-DQ: {len(lamps)} vanilla LightpoleParkRandom01 rows, {len(floods)} SkiPisteFloodlightLow rows "
              f"on the deck top (game local {[o['pos'] for o in floods]}, yaw rule R), no modelled flood mast left")
        # D11 the board order along the chair's travel (measured on the build)
        me = t.data
        try:
            r11, n11 = D.assert_board_order(me, table, D._face_parts(me), D._co(me), p)
            seq = [s_["item"] for s_ in n11["sequence"]]
            # hard-coded (not read back from D1_TRAVEL): 2026-10-12 the top's sequence is RAISE BAR -> UNLOAD
            check(seq == list(D11_SEQ[kind_]) == list(D.D1_TRAVEL[kind_]["seq"]),
                  f"{kind} D11 board order {seq} (want {D11_SEQ[kind_]}): {r11['D11']}")
            words = [s_["words"] for s_ in n11["sequence"]]
            check("STAY SEATED" not in words and (kind_ == "base" or n11.get("forbidden") == [
                {"item": "stay", "words": "STAY SEATED"}]),
                  f"{kind} D11 no STAY SEATED in the sequence {words}; top forbids it ({n11.get('forbidden')})")
        except D.ContractError as e:
            check(False, f"{kind} D11 board order raised: {e}")
        for pf, pos, facing, mount in D.dq_subobjects(p):
            if mount == "deck":
                check(abs(pos[2] - D.DECK_H) < 0.01, f"{kind} D9 {pf} on the deck top")


# --------------------------------------------------------------------------------------
# D7 / D8 negatives and the real hosted ETDQ
# --------------------------------------------------------------------------------------

def _contract_on(kind, edit=None, fn="access"):
    p, parts, t, table = (None, None, None, None)
    lift_terminal.clear_scene()
    p = D.params_for(kind, False, D.load_carrier("dq"))
    parts = D.build_station(p)
    if edit is not None:
        parts = edit(p, parts)
    t, table, _src = D.assemble(parts, False, p)
    me = t.data
    args = (t, me, table, D._face_parts(me), D._co(me), p)
    if fn == "access":
        return D.assert_access_d1(*args)
    if fn == "detail":
        return D.assert_detail1(*args)
    if fn == "order":
        return D.assert_board_order(t.data, table, args[3], args[4], p)
    return D.assert_revision2(*args)


def access_negatives():
    def flag(name, value, kind, fn, want, label):
        saved = getattr(D, name)
        setattr(D, name, value)
        try:
            raises(label, lambda: _contract_on(kind, fn=fn), want)
        finally:
            setattr(D, name, saved)

    flag("ROOF_ROUTE_D1", False, "base", "access", "R2e",
         "D7 base: the 1c front ladder (y -0.70, 0.56 wide, the outer rail segment closed)")
    flag("PF_Y", 0.45, "base", "access", "R2f", "D7 base: the pf ladder pushed into spine_main (PF_Y 0.45)")
    flag("WELL_DEPTH_MAX", 0.60, "top", "r2", "R2c", "D7 top: the draft 0.60 ladder-way (gate 0.61)")

    def box_in_zone(p, parts):
        lx, ly = D.nose_access(p)[2]
        return parts + [(D._box("cabinet_r2eprobe", lx - 0.45, lx - 0.35, ly - 0.05, ly + 0.05, 5.0, 5.1),
                         "galv")]
    raises("D7 base: a box in the front ladder's climbing zone", lambda: _contract_on("base", box_in_zone), "R2e")
    # D8 (MC10): the design's slots that the skeptic found colliding
    LDm = LD

    def hang_board(name, x, side, facing):
        def edit(p, parts):
            return parts + LDm.board(name, (x, side * 1.65, 2.575), facing, 0.70, 0.55, mount="hang",
                                     rods_to=p.z_soffit)
        return edit
    raises("D8 base: the design's DB6 slot x -6.50", lambda: _contract_on("base", hang_board("db6", -6.50, 1.0, "+X"),
                                                                          fn="detail"), "D8")
    raises("D8 top: DT4 at the design's x 2.60", lambda: _contract_on("top", hang_board("dt4", 2.60, -1.0, "-X"),
                                                                      fn="detail"), "D8")
    saved = D.dq_subobjects

    def bad_rows(p):
        rows = saved(p)
        return [r if r[0] != "ParkInfosignRandom01" else ("ParkInfosignRandom01", (13.45, 2.70, D.DECK_H), "+X", "deck")
                for r in rows]
    D.dq_subobjects = bad_rows
    try:
        raises("D8 base: the design's ParkInfosign spot (13.45, 2.70)", lambda: _contract_on("base", fn="detail"), "D8")
    finally:
        D.dq_subobjects = saved
    # DQ follow-up: the lamp-post rows are box-tested too (the union box incl. the arm + head)
    for label, row in (("a lamp post whose head reaches under the band (2.40, 5.10) facing -Y",
                        ("LightpoleParkRandom01", (2.40, 5.10, D.DECK_H), "-Y", "deck")),
                       ("a lamp post facing -X into the corral end (13.75, 5.20)",
                        ("LightpoleParkRandom01", (13.75, 5.20, D.DECK_H), "-X", "deck"))):
        D.dq_subobjects = (lambda p, row=row: saved(p) + [row])
        try:
            raises(f"D8 base: {label}", lambda: _contract_on("base", fn="detail"), "D8")
        finally:
            D.dq_subobjects = saved
    # follow-up resume (2026-10-12): the lamp-head standoff (D1_LAMP_HEAD_CLEAR) and the C16-style deck support -
    # the first attempt's apron spots (y -5.40, facing +Y: heads 0.09 m off band_n's face) and a post off the
    # -Y deck edge (the deck ends at y -7.50)
    for kind, label, row, want in (
            ("base", "the first attempt's apron lamp (5.20, -5.40) facing +Y, its head 0.09 m off band_n",
             ("LightpoleParkRandom01", (5.20, -5.40, D.DECK_H), "+Y", "deck"), "D8"),
            ("top", "the first attempt's unload-line lamp (0.00, -5.40) facing +Y, its head 0.09 m off band_n",
             ("LightpoleParkRandom01", (0.00, -5.40, D.DECK_H), "+Y", "deck"), "D8"),
            ("base", "a lamp post off the -Y deck edge (5.20, -7.80): no deck face under its origin",
             ("LightpoleParkRandom01", (5.20, -7.80, D.DECK_H), "+Y", "deck"), "D9")):
        D.dq_subobjects = (lambda p, row=row: saved(p) + [row])
        try:
            raises(f"{want} {kind}: {label}", lambda kind=kind: _contract_on(kind, fn="detail"), want)
        finally:
            D.dq_subobjects = saved
    # D11 negatives. 2026-10-12 (nineteenth look): the DQ top has NO STAY SEATED, so a STAY SEATED on the top fails -
    # put back through the generator's own tables (D1_HANG_BOARDS + D1_LABELS, the regression an editor would make) at
    # the old slot x -2.50 and at the detail pass's x 2.20 (past the unload line), and as a free-standing post board
    # (any mount: the `forbid`); the top's RAISE BAR / UNLOAD swapped; a base with SKI TIPS UP before LOWER BAR
    hb, lb = D.D1_HANG_BOARDS, D.D1_LABELS
    lb_stay = {**lb, "top": {**lb["top"], "label_stay": "stay"}}
    for kind, table_, label, needle in (
            ("top", {**hb, "top": hb["top"][:1] + (("stay", -2.50, -1.0, "-X"),) + hb["top"][1:]},
             "D11 top: STAY SEATED put back at the old slot x -2.50 (RAISE BAR -> STAY SEATED -> UNLOAD)",
             "STAY SEATED"),
            ("top", {**hb, "top": hb["top"] + (("stay", 2.20, -1.0, "-X"),)},
             "D11 top: STAY SEATED at the detail pass's x 2.20 (UNLOAD, then STAY SEATED past the line)",
             "STAY SEATED"),
            ("top", {**hb, "top": (("raisebar", -0.20, -1.0, "-X"), ("unload", -7.60, -1.0, "-X"))},
             "D11 top: RAISE BAR after UNLOAD (swapped)", "order along the travel broken"),
            ("base", {**hb, "base": (("lowerbar", -1.50, 1.0, "+X"), ("tipsup", 0.60, 1.0, "+X"))},
             "D11 base: SKI TIPS UP before LOWER BAR", "order along the travel broken")):
        D.D1_HANG_BOARDS = table_
        D.D1_LABELS = lb_stay
        try:
            raises_has(label, lambda kind=kind: _contract_on(kind, fn="order"), "D11", needle)
        finally:
            D.D1_HANG_BOARDS, D.D1_LABELS = hb, lb

    def post_stay(p, parts):
        # a free-standing STAY SEATED on a post past the unload apron's end (any mount is barred, not only a hanging one)
        return parts + LDm.board("stay", (4.0, -4.6, 1.5), "-X", 0.70, 0.55, mount="post", post_z0=D.DECK_H)
    raises_has("D11 top: STAY SEATED as a free-standing post board (not hanging)",
               lambda: _contract_on("top", post_stay, fn="order"), "D11", "STAY SEATED")
    # the shipped top's own tables carry no STAY SEATED
    check(all(b[0] != "stay" for b in hb["top"]) and "label_stay" not in lb["top"]
          and "stay" not in D.D1_TRAVEL["top"]["seq"] and "stay" in D.D1_TRAVEL["top"]["forbid"],
          "D11 the generator's tables: no STAY SEATED hang slot, no label_stay (no atlas rect), forbidden on the top")
    p_top = D.params_for("top", False, D.load_carrier("dq"))
    p_base = D.params_for("base", False, D.load_carrier("dq"))
    check("label_stay" not in D.label_rects(p_top) and len(D.label_rects(p_top)) == 1 + 5
          and "stay" not in {k for k in D.d1_labels(p_top).values()},
          f"D11 the top's atlas has no STAY SEATED rect: {sorted(D.label_rects(p_top))}")
    check(len(D.label_rects(p_base)) == 1 + 9, f"D11 the base keeps its 9 label rects + the sign "
                                               f"({len(D.label_rects(p_base))})")


def hosted_etdq_zone():
    """The base front ladder's climbing zone (corridor 0.61 x 0.61, from the ETDQ deck top + 0.01 to the crown
    + 1.80) against the REAL hosted ETDQ (lift_tower rebuild at (0, 0, -11), 180 deg; S8's transform)."""
    import lift_tower as LT
    p = D.params_for("base", False, D.load_carrier("dq"))
    lx, ly = D.nose_access(p)[2]
    LT.clear_scene()
    spec = LT.VARIANTS["ETDQ"]
    t, ttable, _s, _i = LT.build_tower_v2(spec, "rebuild", finish=False)
    me = t.data
    co = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    pf = np.stack([co[:, 0], co[:, 2], -co[:, 1]], axis=1) * np.array([-1.0, 1.0, -1.0]) + \
        np.array([0.0, 0.0, spec.hosted_z])
    auth = pf[:, [2, 0, 1]]                                   # prefab (X, Y, Z) -> authoring (Z, X, Y)
    q = np.zeros(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", q)
    names = [ttable[i - 1] for i in q]
    deck_top = float(max(auth[[v for pl, n in zip(me.polygons, names) if n.startswith("grate_deck")
                               for v in pl.vertices], 2]))
    z0, z1 = deck_top + 0.01, p.z_crown + D.CLIMB_TERMINUS_HEAD
    lo = np.array([lx - D.CLIMB_MIN, ly - D.CLIMB_WIDTH / 2.0, z0])
    hi = np.array([lx, ly + D.CLIMB_WIDTH / 2.0, z1])
    tree = BVHTree.FromPolygons([tuple(v) for v in auth], [tuple(pl.vertices) for pl in me.polygons], epsilon=0.0)
    corners = [(x, y, z) for z in (lo[2], hi[2]) for x, y in ((lo[0], lo[1]), (hi[0], lo[1]), (hi[0], hi[1]),
                                                              (lo[0], hi[1]))]
    quads = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    hits = sorted({names[i] for i, _j in tree.overlap(BVHTree.FromPolygons(corners, quads, epsilon=0.0))})
    inside = sorted({names[i] for i, pl in enumerate(me.polygons)
                     if ((auth[list(pl.vertices)] > lo) & (auth[list(pl.vertices)] < hi)).all(axis=1).any()})
    deck_under = [n for n in names if n.startswith("grate_deck")]
    check(not hits and not inside and abs(deck_top - (p.z_rope + D.DQ_DECK_TOP_BELOW_ROPE)) < 0.01 and deck_under,
          f"D7 the real hosted ETDQ has no face in the base front ladder's zone above its deck top {deck_top:.3f} "
          f"(= the stop {p.z_soffit} + {deck_top - p.z_soffit:.3f}: its deck strip is the stop-level standing "
          f"surface) - hits {hits[:4]}, inside {inside[:4]}")
    LT.clear_scene()


# --------------------------------------------------------------------------------------
# D4 weathering on vs off (512 smoke bakes)
# --------------------------------------------------------------------------------------

def weather_on_off(work):
    pngs = {}
    for w in ("off", "on"):
        out = os.path.join(work, w)
        D.main(["--kind", "base", "--out", out, "--finish", "--lods", "--atlas", "512", "--ao-samples", "4",
                "--no-preview", "--no-validate", "--weather", w])
        name = "SkiDQBaseStationA"
        pngs[w] = {m: cs2_validate.load_png_pixels(os.path.join(out, name, f"{name}_{m}.png"))
                   for m in ("BaseColor", "Normal", "MaskMap", "ControlMask")}
        # 2026-10-12: the fine-detail DQ has no lamp_ part, so no _Emissive map is baked or shipped
        check(not os.path.exists(os.path.join(out, name, f"{name}_Emissive.png")),
              f"D4 weather {w}: no {name}_Emissive.png (no lamp_ part)")
    p = D.params_for("base", False, D.load_carrier("dq"))
    check(np.array_equal(pngs["on"]["ControlMask"], pngs["off"]["ControlMask"]),
          "D4 ControlMask byte-identical with the weathering on vs off")
    A = pngs["on"]["BaseColor"].shape[0]
    bad = []
    for nm, r in sorted(D.label_rects(p).items()):
        u0, v0, u1, v1 = r
        sl = (slice(int(round(v0 * A)), int(round(v1 * A))), slice(int(round(u0 * A)), int(round(u1 * A))))
        for m in pngs["on"]:
            if not np.array_equal(pngs["on"][m][sl], pngs["off"][m][sl]):
                bad.append((nm, m))
    check(not bad, f"D4 every reserved rect (sign + {len(D.label_rects(p)) - 1} labels) identical in all 4 maps "
                   f"({bad[:4]})")
    diff = np.abs(pngs["on"]["BaseColor"][..., :3] - pngs["off"]["BaseColor"][..., :3]).max(axis=-1)
    check(float((diff > 1.5 / 255).mean()) > 0.05, f"D4 the layer changes BaseColor elsewhere "
                                                     f"({float((diff > 1.5 / 255).mean()):.1%} of texels)")
    check(np.array_equal(pngs["on"]["MaskMap"][..., 1:3], pngs["off"]["MaskMap"][..., 1:3]),
          "D4 MaskMap G / B untouched")


# --------------------------------------------------------------------------------------
# the shipped exports (2048)
# --------------------------------------------------------------------------------------

def shipped():
    for kind in ("base", "top"):
        p = D.params_for(kind, False, D.load_carrier("dq"))
        name = p.stem + "A"
        folder = os.path.join(SHIPPED, name)
        with open(os.path.join(folder, f"{name}.manifest.json"), encoding="utf-8") as fh:
            m = json.load(fh)
        d = m.get("detail1") or {}
        tr = {int(k): v for k, v in m["tris"].items()}
        check(m["atlas_px"] == D.ATLAS and m["finish"] == "shipping", f"{kind} shipped at {m['atlas_px']}, shipping finish")
        check(tr[0] <= D.DETAIL_LOD0_TARGET[kind] and tr[1] <= LOD1_CAP[kind] and tr[1] <= 0.5 * tr[0]
              and tr[2] < 500 and d.get("batches") == 9,
              f"{kind} budgets: LOD0 {tr[0]} <= {D.DETAIL_LOD0_TARGET[kind]}, LOD1 {tr[1]} <= {LOD1_CAP[kind]} "
              f"and 50 %, LOD2 {tr[2]} < 500, batches 9")
        b = BASELINE[kind]
        dn = d.get("density") or {}
        cl = dn.get("classes") or {}
        check(dn.get("w1_px_per_m", 0) >= 0.95 * b["w1"] and not dn.get("starving_lt14"),
              f"{kind} D10 w1 {dn.get('w1_px_per_m')} >= 0.95 x 1c {b['w1']}; starving {dn.get('starving_lt14')}")
        check(cl.get("deck", {}).get("px_per_m", 0) >= 1.10 * b["deck"]
              and cl.get("roof_", {}).get("px_per_m", 0) >= 0.95 * b["roof"]
              and min(cl.get(k, {}).get("px_per_m", 0) for k in ("ped_", "crosshead_", "spine_")) >= 40.0,
              f"{kind} D10 deck {cl.get('deck', {}).get('px_per_m')} >= {1.10 * b['deck']:.1f}, roof "
              f"{cl.get('roof_', {}).get('px_per_m')} >= {0.95 * b['roof']:.1f}, ped / crosshead / spine "
              f"{[cl.get(k, {}).get('px_per_m') for k in ('ped_', 'crosshead_', 'spine_')]} >= 40")
        ds = (d.get("detail_stats") or {}).get("drawn") or {}
        check(ds.get("bolts", 0) > 0 and ds.get("grate", 0) > 0 and (d.get("shared_faces") or 0) > 0,
              f"{kind} D10 drawn {ds}; shared faces {d.get('shared_faces')}")
        labs = d.get("labels") or {}
        lab_ok = all(min(v["px_per_m"]) >= 380.0 and math.sqrt(v["px_per_m"][0] * v["px_per_m"][1]) >= 400.0 - 1e-6
                     and v.get("t2_ok") in (True, None) for v in labs.values())
        # 2026-10-12 lift-board redraw (build/lift-boards-redraw.md): the instruction boards are real-size portrait
        # placards on the backer plate - T1's 0.40 m pictogram is no longer claimed for them (read_m, design L3's
        # basis, >= 25 m, and >= 4-texel pictogram strokes instead); SLOW keeps T1 (0.28 m cap)
        t1 = {k: v.get("t1_ok") for k, v in labs.items() if v["kind"] in ("slow",)}
        plac = {k: (v.get("read_m"), v.get("picto_stroke_px")) for k, v in labs.items()
                if v["kind"] in ("lowerbar", "tipsup", "raisebar", "unload", "stay")}
        plac_ok = all(r_ is not None and r_ >= 25.0 and s_ is not None and s_ >= 4.0 for r_, s_ in plac.values())
        check(sorted(labs) == sorted(D.d1_labels(p)) and lab_ok and all(t1.values()) and plac_ok,
              f"{kind} D3 {len(labs)} labels at 400 px/m (>= 380 per axis), T2, T1 where claimed {t1}, placards "
              f"(read m, picto stroke px) {plac}")
        ctl = cs2_validate.load_png_pixels(os.path.join(folder, f"{name}_ControlMask.png"))
        under = max(float(ctl[r0:r1, c0:c1, 0:3].max()) for r0, r1, c0, c1 in (v["rect"] for v in labs.values()))
        check(under < 1e-6, f"{kind} D3 ControlMask 0 under every label ({under})")
        em = d.get("emissive") or {}
        fd = m.get("fine_detail")
        if fd:
            # fine-detail step 3b: no modelled lamp left (lamp_s0-3 -> RoofLight rows, lamp_cab / lamp_eng -> the
            # moved WallLightShortRange rows): the _Emissive was all black. 2026-10-12: the map is DROPPED - not on
            # disk, not in the manifest's files, the blocks say "no map", settings.json never lists Emissive
            emis_png = os.path.join(folder, f"{name}_Emissive.png")
            ce = (m.get("contract") or {}).get("emissive", {})
            with open(os.path.join(folder, "settings.json"), encoding="utf-8") as fh:
                st_txt = fh.read()
            check(not os.path.exists(emis_png) and f"{name}_Emissive.png" not in (m.get("files") or {})
                  and not [f for f in os.listdir(folder) if "Emissive" in f] and "Emissive" not in st_txt
                  and em.get("map") is None and em.get("lit_texels", 1) == 0 and em.get("cool_texels", 1) == 0
                  and not fd.get("lamp_parts_left") and "emissive_map" in fd
                  and ce.get("lamps") == 0 and ce.get("map") is None and ce.get("purpose") is None
                  and ce.get("intensity") is None and ce.get("was") == {"purpose": 72, "intensity": 0.6},
                  f"{kind} D2 fine: NO {name}_Emissive.png (folder {len(os.listdir(folder))} files), manifest "
                  f"files {len(m.get('files') or {})}, emissive block {em.get('map')} / lit {em.get('lit_texels')}, "
                  f"contract.emissive {ce.get('map')} / purpose {ce.get('purpose')}, lamp_ parts "
                  f"{fd.get('lamp_parts_left')}")
            check(sorted(f for f in os.listdir(folder) if f.endswith(".png"))
                  == sorted(f"{name}_{mm}.png" for mm in ("BaseColor", "Normal", "MaskMap", "ControlMask"))
                  and set(m.get("files") or {}) >= {f"{name}_{mm}.png" for mm in ("BaseColor", "Normal", "MaskMap",
                                                                                 "ControlMask")},
                  f"{kind} D2 four maps shipped: {sorted(f for f in os.listdir(folder) if f.endswith('.png'))}")
        else:
            check(b["lit"] <= em.get("lit_texels", 0) <= 0.015 * D.ATLAS * D.ATLAS and em.get("cool_texels", 1) == 0,
                  f"{kind} D2 emissive lit {em.get('lit_texels')} in [{b['lit']}, {int(0.015 * D.ATLAS ** 2)}], "
                  f"no cool flood heads any more (follow-up: the masts are vanilla lamp posts) - cool {em.get('cool_texels')}")
        w = d.get("weather") or {}
        lad = w.get("ladder") or []
        steps = [round(s_, 2) for s_ in LD.INTENSITY_STEPS]
        clean = [r["intensity"] for r in lad if r["ok"]]
        idx = steps.index(round(w.get("intensity", -1), 2)) if round(w.get("intensity", -1), 2) in steps else -9
        highest = idx == len(steps) - 1 or (len(lad) > idx + 1 >= 1 and not lad[idx + 1]["ok"])
        check(w.get("intensity") == (max(clean) if clean else None) and highest and (w.get("vanilla") or {}).get("ok")
              and w.get("snow") == 0.0 and (w.get("guards") or {}).get("G5_ctl_identity")
              and (w.get("guards") or {}).get("G6_protect_identical"),
              f"{kind} weathering at the highest budget-clean step {w.get('intensity')} "
              f"({[(r['intensity'], r['ok']) for r in lad]}), vanilla {(w.get('vanilla') or {}).get('VL1_mean_rel_dL')}")
        sub = (d.get("sub_objects") or {}).get("objects") or []
        nv = sum(1 for o in sub if o.get("kind") != "mod")
        nm = sum(1 for o in sub if o.get("kind") == "mod")
        # T17-DQ 2026-10-16: valid only after the export is regenerated (the shipped manifests predate the swap)
        want = D9_ROWS[kind] if fd else D9_ROWS_LEGACY[kind]
        # DECISIONS 251 (T17-DQ) / T53: a shipped manifest from before the next export still has the 4 vanilla
        # LightpolePark rows and no floodlights (vanilla +4, mod -2): SKIP (loud). Any other count still fails.
        t17_stale = (nv, nm) == (want[0] + 4, want[1] - 2) and not any(o["prefab"] == FLOOD_PREFAB for o in sub)
        if t17_stale:
            print(f"SKIP {kind} D9 shipped sub-object rows: stale until the next export (pre-T17-DQ lamp posts, "
                  f"DECISIONS 251 / T53)")
        check(t17_stale or ((nv, nm) == tuple(want) and sum(1 for o in sub if o["prefab"] == "LightpoleParkRandom01") == D9_LAMPS
                            and flood_rows_ok(sub, D9_FLOODS[kind])),
              f"{kind} D9 shipped sub-object rows {nv} vanilla + {nm} mod (want {want}; {D9_LAMPS} lamp posts, "
              f"2 SkiPisteFloodlightLow)")
        c_ = m.get("contracts") or {}
        check("D11" in c_, f"{kind} D11 shipped board order recorded: {c_.get('D11')}")
        # 2026-10-12: the shipped top reads RAISE BAR -> UNLOAD and carries no STAY SEATED anywhere (label table,
        # atlas rects, board order record); the base keeps WAIT HERE -> carpet -> load line -> LOWER BAR -> SKI TIPS UP
        bo = ((m.get("front_ladder") or {}).get("detail1_board_order") or {}).get("sequence") or []
        sw = [s_["words"] for s_ in bo]
        # (the recorded text ends "| no STAY SEATED board (any mount)" on the top: only its sequence part is read)
        check([s_["item"] for s_ in bo] == list(D11_SEQ[kind])
              and "STAY SEATED" not in str(c_.get("D11")).split(" | ")[0]
              and "label_stay" not in labs and (kind == "base" or "STAY SEATED" not in sw)
              and (kind == "base" or ((m.get("front_ladder") or {}).get("detail1_board_order") or {}).get("forbidden")
                   == [{"item": "stay", "words": "STAY SEATED"}]),
              f"{kind} D11 shipped sequence {sw}; labels {sorted(labs)}")
        c = m.get("contracts") or {}
        check(all(k in c for k in ("R2e", "R2f", "D8", "D9")) and (kind == "top" or "R2g" in c)
              and str(c.get("C8", "")).startswith(("252", "228")) and (not fd or "F1" in c),
              f"{kind} shipped contracts R2e / R2f / R2g / D8 / D9{' / F1' if fd else ''} recorded; {c.get('C8')}")
        if fd:
            shipped_fine(kind, m, fd, sub)
    # the gondola stations (fine-detail step 3b: kit rows on the revision-1 hall)
    for kind in ("base", "top"):
        p = D.params_for(kind, True, D.load_carrier("gd8"))
        name = p.stem + "A"
        with open(os.path.join(REPO, "assets", "import", "stations", "gondola", "A", name, f"{name}.manifest.json"),
                  encoding="utf-8") as fh:
            m = json.load(fh)
        fd = m.get("fine_detail")
        sub = ((m.get("rev1") or {}).get("sub_objects") or {}).get("objects") or []
        nv = sum(1 for o in sub if o.get("kind") != "mod")
        nm = sum(1 for o in sub if o.get("kind") == "mod")
        tr = {int(k): v for k, v in m["tris"].items()}
        # DECISIONS 251 (T17-DQ) / T53: stale shipped manifest (4 lamp posts, no floodlights: vanilla +4, mod -2) is
        # SKIPPED (loud) until the next export; any other count still fails.
        t17_stale = (nv, nm) == (GD_ROWS[kind][0] + 4, GD_ROWS[kind][1] - 2) and not any(o["prefab"] == FLOOD_PREFAB for o in sub)
        if t17_stale:
            print(f"SKIP gondola {kind} shipped fine row counts: stale until the next export (pre-T17-DQ lamp posts, "
                  f"DECISIONS 251 / T53)")
        check(fd is not None and "F1" in (m.get("contracts") or {})
              and (t17_stale or ((nv, nm) == GD_ROWS[kind] and flood_rows_ok(sub, GD_FLOODS[kind])))
              and tr[0] <= D.GD_LOD0_TARGET[kind],
              f"gondola {kind} shipped fine: {nv} vanilla + {nm} kit rows (want {GD_ROWS[kind]}), F1 recorded, LOD0 "
              f"{tr[0]} <= {D.GD_LOD0_TARGET[kind]}")
        if fd:
            shipped_fine(f"gondola {kind}", m, fd, sub)


def shipped_fine(tag, m, fd, sub):
    """The shipped fine_detail block: the kit rows in it = the sub-object block's mod rows (same pose), each with
    its F1 numbers, the frame stated."""
    all_mods = [o for o in sub if o.get("kind") == "mod"]
    mods = [o for o in all_mods if o["prefab"] != FLOOD_PREFAB]     # T17-DQ 2026-10-16: kit rows only
    kr = fd.get("kit_rows") or []
    same = len(kr) == len(mods) and all(
        a["prefab"] == b["prop"] and np.allclose(a["pos"], b["row_pos"], atol=1e-4)
        and np.allclose(a["rot"], b["row_rot"], atol=1e-6) for a, b in zip(mods, kr))
    check(same and fd.get("x_convention") and fd.get("rows", {}).get("mod") == len(all_mods)
          and all(r.get("c8_gap_m") is not None and r["c8_gap_m"] > 0.0 for r in kr),
          f"{tag} fine_detail block: {len(kr)} kit rows = the block's mod rows, F1 numbers (nearest C8 "
          f"{min((r['c8_gap_m'] for r in kr), default=None)} m), frame stated")


# --------------------------------------------------------------------------------------
# Fine-detail step 3b: F1 negatives, F2 H1 placement, F3 fine geometry
# --------------------------------------------------------------------------------------

def _kit_contract(kind, gondola=False, patch=None):
    """assert_kit_rows on a fine blockout of one station; `patch(p, specs) -> specs` edits the kit specs."""
    saved = D.fd_kit_specs
    if patch is not None:
        D.fd_kit_specs = lambda p, _s=saved: patch(p, [dict(x) for x in _s(p)])
    try:
        p, parts, t, table = build(kind, gondola=gondola, fine=True)
        me = t.data
        saved_f = D.FINE_DETAIL
        D.FINE_DETAIL = True
        try:
            return D.assert_kit_rows(t, me, table, D._face_parts(me), D._co(me), p)
        finally:
            D.FINE_DETAIL = saved_f
    finally:
        D.fd_kit_specs = saved


def _edit_spec(prop, **kw):
    def patch(p, specs):
        out = []
        for s in specs:
            if s["prop"] == prop or (prop == "rack" and s["prop"].startswith("SkiDressSkiRack")):
                s = dict(s)
                for k, v in kw.items():
                    if k == "dpos":
                        s["pos"] = tuple(a + b for a, b in zip(s["pos"], v))
                    elif v is None:
                        s.pop(k, None)
                    else:
                        s[k] = v(p) if callable(v) else v
            out.append(s)
        return out
    return patch


def kit_contracts():
    """F1 positives on all four stations + the negatives (each must raise F1)."""
    for gondola in (False, True):
        for kind in ("base", "top"):
            tag = f"{'gondola' if gondola else 'DQ'} {kind}"
            try:
                r, nums = _kit_contract(kind, gondola)
                check("F1" in r and nums and len(nums["rows"]) == len(D.fd_kit_specs(
                    D.params_for(kind, gondola, D.load_carrier("gd8" if gondola else "dq")))),
                      f"{tag} F1 passes: {len(nums['rows'])} kit rows ({r['F1'][:90]}...)")
            except D.ContractError as e:
                check(False, f"{tag} F1 raised on the shipping layout: {e}")
    import dataclasses
    import fine_detail as FD
    cases = (
        ("top", False, _edit_spec("SkiDressHoseReel", dpos=(0.05, 0.0, 0.0)),
         "a kit hose reel pushed 5 cm into the DQ top cabin wall"),
        ("base", False, _edit_spec("SkiDressHoseReel", pos=lambda p: (p.x_wheel - 6.6, 7.49, 0.90), facing="-X"),
         "the DQ base reel at its OLD spot beside the engine-room door (0.70 m plate, 0.62 m of wall)"),
        ("base", False, _edit_spec("SkiDressHoseReel", pos=lambda p: (p.x_wheel - 4.80, 5.60, 0.90), facing="-Y"),
         "the DQ base reel on the engine room's -Y face behind cabinet_0 / _1 (no access: the first fine-detail spot)"),
        ("base", False, _edit_spec("SkiDressTools", dpos=(0.0, -0.03, 0.0)),
         "the DQ base tools 3 cm off the engine-room wall (they touch nothing)"),
        ("base", False, _edit_spec("SkiDressEStop", pos=(1.50, 1.50, D.DECK_H)),
         "a kit e-stop in the C8 bench envelope (1.50, 1.50: off the carpet, in the +Y lane)"),
        ("base", True, _edit_spec("SkiDressTools", pos=lambda p: (round(D.gd_hall_layout(p)["xd"] + 5.925, 4),
                                                                  -(p.w_half + D.STONE_T), 0.0), ledge=None),
         "the gondola tools on the stone face without the ledge offset (the coping stands 0.05 proud)"),
        ("base", False, _edit_spec("rack", dpos=(0.0, 0.10, 0.0)),
         "the DQ base rack pushed 0.10 m toward +Y onto the ParkInfosign's box (0.068 m apart as shipped)"),
    )
    for kind, gondola, patch, why in cases:
        try:
            _kit_contract(kind, gondola, patch)
        except D.ContractError as e:
            check(str(e).startswith("F1"), f"F1 negative raises: {why} ({str(e)[:150]})")
            continue
        check(False, f"F1 negative did NOT raise: {why}")
    k = FD.KIT["SkiDressEStop"]
    FD.KIT["SkiDressEStop"] = dataclasses.replace(k, cid="0" * 32)
    try:
        _kit_contract("base", False)
        check(False, "F1 negative did NOT raise: a kit CID drift (KIT vs the staged .cid)")
    except (D.ContractError, ValueError) as e:
        check(str(e).startswith("F1"), f"F1 negative raises: a kit CID drift ({str(e)[:110]})")
    finally:
        FD.KIT["SkiDressEStop"] = k


def _part_verts(me, table, fparts, co, test):
    return co[D._vidx(me, table, fparts, test)]


def _nearest_dev(old, new):
    """max over `old` of the distance to the nearest vertex of `new`."""
    d = 0.0
    for v in old:
        d = max(d, float(np.min(np.linalg.norm(new - v, axis=1))))
    return d


def _best_dz(old, new):
    """The vertical offset that best maps `old` onto `new` (candidates from the lowest old vertex's xy match)."""
    v0 = old[np.argmin(old[:, 2])]
    near = new[np.linalg.norm(new[:, :2] - v0[:2], axis=1) < 0.003]
    cands = sorted({round(float(a[2] - v0[2]), 6) for a in near}) or [float(new[:, 2].min() - old[:, 2].min())]
    best = min(cands, key=lambda dz: _nearest_dev(old + np.array([0.0, 0.0, dz]), new))
    return best, _nearest_dev(old + np.array([0.0, 0.0, best]), new)


def kit_h1():
    """F2: every kit row against the parts it replaces (a FINE_DETAIL-off blockout of the same station)."""
    out = {}
    for gondola in (False, True):
        for kind in ("base", "top"):
            tag = f"{'gondola' if gondola else 'DQ'} {kind}"
            p, _parts, t, table = build(kind, gondola=gondola, fine=False)
            me = t.data
            fparts, co = D._face_parts(me), D._co(me)
            saved = D.FINE_DETAIL
            D.FINE_DETAIL = True
            try:
                specs, rows = D.fd_kit_specs(p), D.fd_kit_rows(p)
            finally:
                D.FINE_DETAIL = saved
            for s, r in zip(specs, rows):
                A = D.kit_to_authoring(D.load_kit_mesh(r["mesh"])["V"], r)
                pre = s["removed"]
                rtag = f"{tag} {s['prop']}"
                n3 = LD._dir3(s["facing"])
                nv = np.array([n3.x, n3.y, 0.0])
                if s["prop"].startswith("SkiDressSkiRack"):
                    old = _part_verts(me, table, fparts, co, lambda n: n.startswith(pre[0]))     # the frame bars
                    dz, dev = _best_dz(old, A)
                    out[rtag] = {"frame_dev_mm": round(dev * 1000, 3), "foot_dz_m": round(dz, 4)}
                    check(dev <= KIT_H1_TOL, f"{rtag} F2 the kit wall rack's frame = the removed one-sided frame vertex "
                                             f"for vertex: max {dev * 1000:.3f} mm after a foot drop of {dz:.4f} m "
                                             f"({len(old)} frame vertices)")
                elif s["prop"] == "SkiDressEStop":
                    post = pre[0] + "post"
                    old = _part_verts(me, table, fparts, co, lambda n: n == post)
                    ring = A[np.abs(A[:, 2] - (A[:, 2].min() + 0.01)) < 1e-3]
                    da = float(np.linalg.norm(ring[:, :2].mean(axis=0) - old[:, :2].mean(axis=0)))
                    df = float(A[:, 2].min() - old[:, 2].min())
                    out[rtag] = {"axis_dev_mm": round(da * 1000, 3), "foot_dz_m": round(df, 4)}
                    check(da <= KIT_H1_TOL and abs(df) <= KIT_H1_TOL,
                          f"{rtag} F2 the kit post axis {da * 1000:.3f} mm off the removed post's, foot {df:+.4f} m")
                elif s["prop"] == "SkiDressExtinguisher":
                    old = _part_verts(me, table, fparts, co, lambda n: n == pre[0])
                    ob = old[np.abs(old[:, 2] - old[:, 2].min()) < 1e-4]
                    kb = A[np.abs(A[:, 2] - A[:, 2].min()) < 1e-4]
                    da = float(np.linalg.norm(kb[:, :2].mean(axis=0) - ob[:, :2].mean(axis=0)))
                    df = float(A[:, 2].min() - old[:, 2].min())
                    out[rtag] = {"axis_dev_mm": round(da * 1000, 3), "bottom_dz_m": round(df, 4)}
                    check(da <= KIT_H1_TOL and abs(df) <= KIT_H1_TOL,
                          f"{rtag} F2 the kit body axis {da * 1000:.3f} mm off the removed body's, bottom {df:+.4f} m")
                elif s["prop"] == "SkiDressHoseReel":
                    old = _part_verts(me, table, fparts, co, lambda n: n.startswith(pre[0]) and n.endswith("_plate"))
                    ob = old[np.abs(old @ nv - (old @ nv).min()) < 1e-4]            # the plate's back corners
                    kb = A[np.abs(A @ nv - (A @ nv).min()) < 1e-4]
                    ck = (kb.min(axis=0) + kb.max(axis=0)) / 2.0
                    co_ = (ob.min(axis=0) + ob.max(axis=0)) / 2.0
                    size_o = round(float(ob[:, 2].max() - ob[:, 2].min()), 3)
                    size_k = round(float(kb[:, 2].max() - kb[:, 2].min()), 3)
                    rec = {"plate_m": [size_o, size_k], "centre_move_m": [round(float(v), 4) for v in ck - co_]}
                    if abs(size_o - size_k) < 1e-3 and float(np.linalg.norm(ck - co_)) < 0.01:
                        dev = _nearest_dev(ob, kb)
                        rec["corner_dev_mm"] = round(dev * 1000, 3)
                        check(dev <= KIT_H1_TOL, f"{rtag} F2 the kit plate's back corners = the removed plate's: "
                                                 f"max {dev * 1000:.3f} mm")
                    else:
                        check("MOVED" in s["was"] or "same centre" in s["was"],
                              f"{rtag} F2 plate {size_o} -> {size_k} m, centre moved {rec['centre_move_m']} "
                              f"(declared: {s['was'][:70]})")
                        if "same centre" in s["was"]:
                            check(float(np.linalg.norm(ck - co_)) <= KIT_H1_TOL,
                                  f"{rtag} F2 the resized plate keeps the removed plate's centre "
                                  f"({float(np.linalg.norm(ck - co_)) * 1000:.3f} mm)")
                    out[rtag] = rec
                elif s["prop"] == "SkiDressTools":
                    # T14 x-sign N (2026-10-16), DECISIONS 245 / T53: the row is rule-T mirrored (game x negated) but the
                    # prop keeps its own mesh handedness, so the asymmetric tool pair stands left-right swapped about the
                    # row's lateral axis; the expectation is the removed pair mirrored in that axis (measured 0.001 mm;
                    # un-mirrored it is 201.6 mm off). The foot-line shift and the z offset are unchanged.
                    lat = np.array([-nv[1], nv[0], 0.0])
                    c_row = np.array([r["position"][2], -r["position"][0], 0.0])    # the row in authoring xy
                    A = A - 2.0 * np.outer((A - c_row) @ lat, lat)
                    old = _part_verts(me, table, fparts, co, lambda n: n.startswith(pre[0]))
                    of = old[old[:, 2] <= old[:, 2].min() + 0.002]
                    kf = A[A[:, 2] <= A[:, 2].min() + 0.002]
                    shift = np.array([*(kf[:, :2].mean(axis=0) - of[:, :2].mean(axis=0)), 0.0])
                    dz, dev = _best_dz(old + shift, A)
                    out[rtag] = {"foot_line_move_m": round(float(shift @ nv), 4), "lateral_m":
                                 round(float(np.linalg.norm(shift - nv * (shift @ nv))), 4), "dz_m": round(dz, 4),
                                 "vertex_dev_mm": round(dev * 1000, 3)}
                    want = 0.3925 - 0.32 if not s.get("ledge") else None
                    check(dev <= KIT_H1_TOL and (want is None or abs(float(shift @ nv) - want) < 1e-3)
                          and float(np.linalg.norm(shift - nv * (shift @ nv))) < 1e-3,
                          f"{rtag} F2 the kit tools = the removed pair moved {float(shift @ nv):.4f} m out from the wall "
                          f"(the foot-line rule{', ledge' if s.get('ledge') else ': 0.3925 - 0.32'}), {dz:+.4f} m in "
                          f"z, max {dev * 1000:.3f} mm")
    print("[d1-test] F2 H1 numbers: " + json.dumps(out))
    return out


def _tris_of_part(me, table, fparts, name):
    return int(sum(len(pl.vertices) - 2 for pl, q in zip(me.polygons, fparts) if table[q - 1] == name))


def fine_geometry():
    """F3 (+ F0's FS1 / FS5 halves): per-part tris fine vs legacy, the cabinets' bevel strips, the weather mast and
    the label quads vertex-identical, the DQ lamp parts gone."""
    for kind in ("base", "top"):
        want = [("cabinet_0", 10, 12), ("cabinet_0_lid", 12, 0), ("cabinet_comm", 10, 12)]
        if kind == "base":
            want += [("maze_post_ld_0", 24, 12), ("maze_rope_ld_0_0_lo", 12, 8), ("gate_ld_hinge", 24, 12),
                     ("gate_ld_top", 12, 8), ("board_slow_post0", 36, 18), ("board_lowerbar_rod0", 12, 8)]
        else:
            want += [("board_slow_post0", 36, 18), ("board_unload_rod0", 12, 8)]
        pres = ("wx", "label_", "signband_", "lrail_")
        res = {}
        for fine in (True, False):
            # read everything out before the next build (clear_scene frees the mesh)
            p, _parts, t, table = build(kind, fine=fine)
            me = t.data
            fp, co = D._face_parts(me), D._co(me)
            res[fine] = {"tris": {n: _tris_of_part(me, table, fp, n) for n, _a, _b in want},
                         "verts": {pre: np.unique(np.round(_part_verts(me, table, fp, co,
                                                                       lambda n, pre=pre: n.startswith(pre)), 6), axis=0)
                                   for pre in pres},
                         "lamps": sorted(n for n in table if n.startswith("lamp_"))}
        got = [(n, res[True]["tris"][n], res[False]["tris"][n]) for n, _a, _b in want]
        check(got == want, f"DQ {kind} F3 tris fine / legacy {got} (want {want})")
        for pre in pres:
            a, b = res[True]["verts"][pre], res[False]["verts"][pre]
            check(a.shape == b.shape and np.array_equal(a, b),
                  f"DQ {kind} F0 {pre}* vertex-identical fine vs legacy ({len(a)} vertices; FS1 weather mast / FS5 boards)")
        check(not res[True]["lamps"] and len(res[False]["lamps"]) == (6 if kind == "base" else 5),
              f"DQ {kind} F3 lamp_ parts: fine {res[True]['lamps']}, legacy {res[False]['lamps']}")
    saved = D.FINE_DETAIL
    try:
        D.FINE_DETAIL = True
        fine_bev = D.bevel_prefixes()
        D.FINE_DETAIL = False
        old_bev = D.bevel_prefixes()
    finally:
        D.FINE_DETAIL = saved
    check("cabinet_" not in fine_bev and old_bev == D.BEVEL_PREFIXES and set(old_bev) - set(fine_bev) == {"cabinet_"},
          "F3 the control cabinets carry no finish bevel strips (bevel_prefixes drops cabinet_ only)")
    # the finish bevel on a real assemble (no bake): a bevelled box vs the plain box + its lid
    p, _parts, t, table = build("top", finish=True, fine=True)
    me = t.data
    fp = D._face_parts(me)
    a = (_tris_of_part(me, table, fp, "cabinet_1"), _tris_of_part(me, table, fp, "cabinet_1_lid"))
    p, _parts, t, table = build("top", finish=True, fine=False)
    me = t.data
    fp = D._face_parts(me)
    b = _tris_of_part(me, table, fp, "cabinet_1")
    check(a == (10, 12) and b > 12, f"F3 finish build: cabinet_1 fine {a[0]} + lid {a[1]} tris (no bevel strips), legacy "
                                    f"{b} (bevelled)")



# --------------------------------------------------------------------------------------
# Nitpick A (2026-10-13; Richard's twenty-fifth look S-1 / S-2; build/nitpick-a-stations.md)
# --------------------------------------------------------------------------------------

def _nk_contract(kind, gondola=False, fn="orient", layout=None):
    """Build a blockout (fine on) and run O1 ("orient") or ST1 ("train"); `layout` patches nose_train_layout's
    result for the BUILD only (the contract re-reads the unpatched one)."""
    lift_terminal.clear_scene()
    p = D.params_for(kind, gondola, D.load_carrier("gd8" if gondola else "dq"))
    saved = D.nose_train_layout
    if layout is not None:
        D.nose_train_layout = lambda p_, _s=saved: layout(_s(p_))
    try:
        parts = D.build_station(p)
    finally:
        D.nose_train_layout = saved
    t, table, _src = D.assemble(parts, False, p)
    me = t.data
    args = (me, table, D._face_parts(me), D._co(me), p)
    if fn == "clash":                    # nitpick A4: C14
        return D.assert_nose_clash(*args)
    return D.assert_orientation_dq(*args) if fn == "orient" else D.assert_nose_train(*args)


def _bbox_dims(o):
    import numpy as _np
    bpy.context.view_layer.update()
    M = o.matrix_world        # the tower's kit parts still carry their object rotation before build_tower_v2 bakes it
    v = _np.array([tuple(M @ x.co) for x in o.data.vertices])
    return tuple(round(float(d), 4) for d in (v.max(axis=0) - v.min(axis=0)))


def nitpick_a():
    """O1: both DQ stations laid out round the one up lane (cabin, hang boards, load carpet + line / unload stripe);
    negatives - the pre-2026-10-13 top cabin on +Y (TOP_CAB_LEGACY) and OP_SIDE_Y flipped. ST1: the top's nose
    trains (DQ + gondola) are the towers' gantry train, the DQ one with its axle ON the lane (nitpick A3 D1, the towers'
    convention), the gondola one with the rope tangent on the rims; negatives - the sheaves 1 cm low, the
    carriers' gate tightened past the towers' pins, the train's outer tip past the frozen z min. The station train's
    parts have the TOWER's shapes (cs2_sheaves on both: rocker / yoke / hanger / pin boxes of a DQ12 rebuild tower)."""
    print("[d1-test] nitpick A: O1 orientation, ST1 the towers' sheave train at the nose")
    for kind in ("base", "top"):
        r, nums = _nk_contract(kind)
        check("O1" in r and nums["up_lane_y_authoring"] == D.D1_TRAVEL[kind]["lane"]
              and all(v * nums["up_lane_y_authoring"] > 0 for v in nums["measured_centre_y"].values()),
              f"O1 {kind}: {r.get('O1')} {nums['measured_centre_y']}")
    check(D.cab_layout(D.params_for("top", False, D.load_carrier("dq")))["y1"] <= -5.0,
          "O1 top: the cabin stands on the -Y apron (cab_layout)")
    saved = D.TOP_CAB_LEGACY
    D.TOP_CAB_LEGACY = True
    try:
        raises_has("O1 top: the pre-2026-10-13 cabin on +Y (across the bullwheel from the unload)",
                   lambda: _nk_contract("top"), "O1", "cab_walls")
    finally:
        D.TOP_CAB_LEGACY = saved
    saved = dict(D.OP_SIDE_Y)
    D.OP_SIDE_Y["top"] = 1.0
    try:
        raises("O1 top: OP_SIDE_Y flipped against the up lane", lambda: _nk_contract("top"), "O1")
    finally:
        D.OP_SIDE_Y.clear()
        D.OP_SIDE_Y.update(saved)
    for gd in (False, True):
        tag = "gondola top" if gd else "DQ top"
        r, nums = _nk_contract("top", gondola=gd, fn="train")
        # nitpick A3 (D1): the DQ head carries its axle ON the lane (the towers' convention), the GD head keeps the
        # rope tangent on the rims
        on_lane = not gd
        err = nums.get("axle_on_lane_err_m" if on_lane else "tangent_err_m")
        p_ = D.params_for("top", gd, D.load_carrier("gd8" if gd else "dq"))
        want_axle = p_.z_rope if on_lane else p_.z_rope - D.ROPE_R - nums["sheave_r_m"]
        check("ST1" in r and nums["per_lane"] == D.NOSE_TRAIN_N and err is not None and err <= 0.002
              and nums["axle_on_lane"] is on_lane and abs(nums["axle_z"] - want_axle) <= 1e-6
              and nums["carrier_max_dy_from_lane_m"] <= -D.NOSE_CARRIER_IN and nums["x_extent"][0] >= -12.1 - 1e-6,
              f"ST1 {tag}: {r.get('ST1')} (axle {nums['axle_z']}, want {want_axle:.4f})")
        raises_has(f"ST1 {tag}: the train 1 cm low ({'the axle under the lane' if on_lane else 'the rope floats above the sheaves'})",
                   lambda gd=gd: _nk_contract("top", gondola=gd, fn="train",
                                              layout=lambda L: dict(L, axle=L["axle"] - 0.01)),
                   "ST1", "not on the lane" if on_lane else "not tangent")
        raises_has(f"ST1 {tag}: the outer tip 0.10 m past the frozen z min",
                   lambda gd=gd: _nk_contract("top", gondola=gd, fn="train",
                                              layout=lambda L: dict(L, x0=L["x0"] - 0.10)),
                   "ST1", "frozen bounds")
    saved = D.NOSE_CARRIER_IN
    D.NOSE_CARRIER_IN = 0.05
    try:
        raises_has("ST1 DQ top: the carriers' gate at lane - 0.05 (the DQ pins end at lane - 0.039)",
                   lambda: _nk_contract("top", fn="train"), "ST1", "carriers stay inboard")
    finally:
        D.NOSE_CARRIER_IN = saved
    # the same builder, the same shapes: the station's DQ train parts vs the DQ12 rebuild tower's
    import lift_tower as LTW
    lift_terminal.clear_scene()
    tw, _i = LTW.dq_parts(LTW.VARIANTS["DQ12"], "rebuild")
    tower = {o.name: _bbox_dims(o) for o, _k in tw if o.name.startswith(("rocker_", "yoke_", "hanger_", "pivot_"))}
    lift_terminal.clear_scene()
    st = D.build_nose_train(D.params_for("top", False, D.load_carrier("dq")))
    pre = D.NOSE_TRAIN_PREFIX
    station = {o.name[len(pre):]: _bbox_dims(o) for o, _k in st if o.name.startswith(pre)}
    same = [n for n in ("rocker_sub_n_0", "rocker_main_n_0", "pivot_n_s0", "pivot_n_m0")
            if n in station and n in tower and station[n] == tower[n]]
    check(len(same) == 4, f"ST1 the station's DQ train parts have the DQ12 tower's shapes {same} "
                          f"(station {[station.get(n) for n in ('rocker_sub_n_0', 'rocker_main_n_0')]})")


def _obj_pairs(objs):
    """{(a, b)} sorted name pairs of mesh objects (world space) whose triangles intersect."""
    bpy.context.view_layer.update()
    trees = {}
    for o in objs:
        M = o.matrix_world
        trees[o.name] = BVHTree.FromPolygons([tuple(M @ v.co) for v in o.data.vertices],
                                             [tuple(p.vertices) for p in o.data.polygons], epsilon=0.0)
    names = sorted(trees)
    return {(a, b) for i, a in enumerate(names) for b in names[i + 1:] if trees[a].overlap(trees[b])}


def nitpick_a4():
    """Nitpick A4 (2026-10-13; build/nitpick-a3-check.md 1d / N3): C14 - the DQ top's nose zone has no undeclared
    part clash. Shipped layout: ST1 + C14 pass, the hangers stand at their yokes, the beams clear the comm clamps and the
    cable. Negatives - the hangers back at the tower's x 0 (ST1 'not at its yoke'; C14: the detach rails cross them),
    the beams back at lane - 0.60 (C14: clamp_comm0 / the cable inside the beam), a part wholly inside another's volume,
    the nitpick-A3 flag (NOSE_A4_LEGACY) finds exactly the five pairs. The rope rule is lift_chair's F10 set; the train's
    internal joints that C14 declares are the DQ12 tower's own (same builder: the pair sets are equal)."""
    print("[d1-test] nitpick A4: C14 nose clashes, the hanger at its yoke, the beams clear of the comm clamps")
    r, nums = _nk_contract("top", fn="clash")
    check("C14" in r and nums["undeclared"] == 0 and nums["contained"] == 0 and nums["nose_parts"] >= 40
          and nums["intersecting_pairs"] == nums["declared_pairs"] + len(nums["known_open"]),
          f"C14 DQ top: {r.get('C14')}")
    check(len(nums["known_open"]) == 3 and all("commcable_nose" in k for k in nums["known_open"])
          and all(d >= 1 for d in nums["declared_by_rule"].values()),
          f"C14 DQ top: the known-open pairs are the comm cable's corner fillet only {nums['known_open']}; "
          f"{len(nums['declared_by_rule'])} rules used")
    r1, n1 = _nk_contract("top", fn="train")
    check(n1["hanger_at_yoke"] is True and n1["hanger_x_err_m"] <= 0.001 and abs(n1["hanger_x"] + 10.992) < 1e-6
          and n1["guide_beam_abs_y"] == [2.02, 2.14],
          f"ST1 DQ top: hangers at their yokes (x {n1['hanger_x']}, err {n1['hanger_x_err_m']} m), beams |y| "
          f"{n1['guide_beam_abs_y']}")
    gd, gn = _nk_contract("top", gondola=True, fn="train")
    check("hanger_at_yoke" not in gn and not D.nose_hanger_at_yoke("GD") and D.NOSE_BEAM_IN["GD"] == D.NOSE_BEAM_IN_A3["GD"],
          "ST1 gondola top: untouched by A4 (no hanger / beam keys in its numbers, its beam line unchanged)")
    raises_has("ST1 DQ top: the hangers back at the tower's x 0",
               lambda: _nk_contract("top", fn="train", layout=lambda L: dict(L, hanger_x=0.0)), "ST1", "not at its yoke")
    raises_has("C14 DQ top: the hangers back at x 0 (the detach rails cross them)",
               lambda: _nk_contract("top", fn="clash", layout=lambda L: dict(L, hanger_x=0.0)), "C14", "detrail_n_1")
    old_beam = lambda L: dict(L, beam=dict(L["beam"], y0=L["beam"]["y0"] - 0.08, y1=L["beam"]["y1"] - 0.08))   # noqa: E731
    raises_has("C14 DQ top: the beams back at lane - 0.60 (the comm clamp inside them)",
               lambda: _nk_contract("top", fn="clash", layout=old_beam), "C14", "clamp_comm0_l | guideframe_p")
    raises_has("C14 DQ top: the beams back at lane - 0.60 (the comm cable through them)",
               lambda: _nk_contract("top", fn="clash", layout=old_beam), "C14", "commcable_nose | guideframe_p")
    saved = D.NOSE_A4_LEGACY
    D.NOSE_A4_LEGACY = True
    try:
        try:
            _nk_contract("top", fn="clash")
            check(False, "C14 DQ top: the nitpick-A3 layout (NOSE_A4_LEGACY) did NOT raise")
        except D.ContractError as e:
            want = ("clamp_comm0_l | guideframe_p", "clamp_comm0_u | guideframe_p", "commcable_nose | guideframe_p",
                    "detrail_n_1 | sheave_guide_hanger_n", "detrail_p_1 | sheave_guide_hanger_p")
            check(str(e).startswith("C14 5 undeclared") and all(w in str(e) for w in want),
                  f"C14 DQ top: the nitpick-A3 layout (NOSE_A4_LEGACY) raises exactly the five pairs ({str(e)[:90]}...)")
        r_leg, n_leg = _nk_contract("top", fn="train")
        check(n_leg["hanger_x"] == 0.0 and n_leg["guide_beam_abs_y"] == [1.94, 2.06] and n_leg["hanger_at_yoke"] is False,
              f"ST1 nitpick-A3 layout: hangers at x {n_leg['hanger_x']}, beams {n_leg['guide_beam_abs_y']} "
              f"(the legacy flag reads its own layout)")
    finally:
        D.NOSE_A4_LEGACY = saved
    # a nose part wholly inside another part's closed volume raises C14 'inside'
    lift_terminal.clear_scene()
    p = D.params_for("top", False, D.load_carrier("dq"))
    parts = D.build_station(p)
    parts.append((D.cs2_kit.add_box("clamp_comm9_l", (0.04, 0.04, 0.04), (-10.15, 0.0, 5.45)), "galv"))
    t, table, _src = D.assemble(parts, False, p)
    me = t.data
    raises_has("C14 DQ top: a nose part wholly inside the comm bracket's volume (no triangle pair to see it)",
               lambda: D.assert_nose_clash(me, table, D._face_parts(me), D._co(me), p), "C14", "clamp_comm9_l in commbracket_nose")
    # the rope rule IS the chair's F10 convention set
    import lift_chair as LC
    check(tuple(D.NOSE_ROPE_PASS) == tuple(LC.CARRIER_PASS["gantry"]),
          f"C14 the rope may cross exactly lift_chair.CARRIER_PASS['gantry'] {D.NOSE_ROPE_PASS}")
    # the declared train-internal joints are the TOWER's own: lift_tower._dq_train (DQ12's spec cut to the station's
    # 4 sheaves a lane, sag 0, so one main as at the nose; finished as build_tower_v2 finishes it) has the same
    # intersecting pair set, by core name, as the station's train parts (the girder stub and the rope excluded)
    import dataclasses
    import lift_tower as LTW
    import cs2_sheaves as CSS
    lift_terminal.clear_scene()
    spec4 = dataclasses.replace(LTW.VARIANTS["DQ12"], sheaves=D.NOSE_TRAIN_N, sag=0.0)
    tw = LTW._dq_train(spec4)
    CSS.finish_train(tw, "DQ")
    tower_pairs = _obj_pairs([o for o, _k in tw])
    lift_terminal.clear_scene()
    st = D.build_nose_train(p)
    pre = D.NOSE_TRAIN_PREFIX
    objs = [o for o, _k in st if o.name.startswith(pre) and not o.name.startswith(pre + "girder_")]
    station_pairs = {(a[len(pre):], b[len(pre):]) for a, b in _obj_pairs(objs)}
    import re
    lanes = lambda pairs: {pr for pr in pairs if all(re.search(r"_n(?:_|$)", x) for x in pr)}   # noqa: E731
    only_t, only_s = sorted(lanes(tower_pairs) - lanes(station_pairs)), sorted(lanes(station_pairs) - lanes(tower_pairs))
    check(len(lanes(station_pairs)) == 8 and not only_t and not only_s,
          f"C14 the station train's internal joints (lane n: {len(lanes(station_pairs))} pairs) are the DQ12 tower's "
          f"own (tower-only {only_t[:3]}, station-only {only_s[:3]})")
    lift_terminal.clear_scene()

def main():
    t0 = time.perf_counter()
    work = tempfile.mkdtemp(prefix="detail_detach_")
    try:
        counts_and_frozen()
        access_negatives()
        hosted_etdq_zone()
        kit_contracts()
        kit_h1()
        fine_geometry()
        nitpick_a()
        nitpick_a4()
        shipped()
        weather_on_off(work)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    if FAILS:
        print(f"FAIL test_detail_detach: {len(FAILS)} failure(s): {FAILS[:6]}")
        sys.exit(1)
    print(f"PASS test_detail_detach: D1 counts + LOD policy, D6 frozen numbers, D11 board order (top RAISE BAR -> UNLOAD, "
          f"no STAY SEATED; base unchanged) + negatives, the lamp-post rows, D7 one access rule (R2e / R2f / R2g) "
          f"+ negatives + the real hosted ETDQ clear of the roof route, D8 / D9 MC10 box tests + negatives, D4 weathering "
          f"on vs off, shipped budgets / D10 floors / D3 labels / D2 no DQ _Emissive map / weathering, nitpick A O1 orientation + "
          f"ST1 the towers' sheave train (+ negatives), nitpick A4 C14 (no undeclared part clash in the DQ top's nose zone, the "
          f"train's joints = the tower's own) + the hanger at its yoke + the beams clear of the comm clamps (+ negatives, "
          f"the A3 layout raises exactly five pairs) ({time.perf_counter() - t0:.0f} s)")


try:
    main()
except SystemExit:
    raise
except Exception:  # noqa: BLE001
    import traceback
    traceback.print_exc()
    print("FAIL test_detail_detach: exception")
    sys.exit(1)
