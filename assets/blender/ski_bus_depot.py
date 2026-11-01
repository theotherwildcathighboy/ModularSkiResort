"""
ski_bus_depot.py - the Ski Bus Depot shelter -> mesh SkiBusDepotA (the art for the SkiBusDepot prefab).

Design: docs/research/ski-bus-depot-art-brief-2026-10-31.md. One architect, one palette: oxblood steel, cedar,
corten, pale concrete; yellow only on the hooks and the bench, red only on the sign.

Form: welcome_sign.py's route (kit parts -> unique bake -> LOD chain), with the hut pilot's texture method
(docs/PIPELINE.md section 1b, DECISIONS 459): CC0 scans at real-world scale, tinted to the palette (target_srgb),
the swatch controls chroma / flatten_m / contrast / normal_gain, low-frequency weathering only. A building off the
family sheet bakes its own atlas, so none of this is a family change. The controls live on the unmerged
`hut-textures` branch; apply_controls below is a local copy of their order and math.

Frame: authoring meters, +X = the road, Y along the shelter, Z up, lot center at the origin (orient_for_net sends
+X to game +Z). Lot 2 x 1 cells, flattened by the building's own terraform.

Vanilla first: bench, trash bin, planter rocks, young trees and the lamp are NOT in the mesh. SUB_OBJECTS lists them;
--preview shows each as a plain ph_ placeholder that exists only in the render scene.

CLI (headless only, never into assets/import):
    blender --background --factory-startup --python-exit-code 1 --python ski_bus_depot.py --
        --out DIR [--preview DIR] [--shots a,b] [--samples 48] [--tex 2048] [--ao-samples 32] [--blockout]
Last line: PASS ski_bus_depot: <Name> LOD0 n, chain [n1, n2], tex A, ...
"""

import argparse
import json
import math
import os
import random
import re
import sys
import time
import traceback
import types
from dataclasses import replace

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset            # noqa: E402
import cs2_bake             # noqa: E402
import cs2_bldg as K        # noqa: E402
import cs2_compose          # noqa: E402
import cs2_signset          # noqa: E402
import cs2_submesh          # noqa: E402
import cs2_uv               # noqa: E402
import cs2_validate         # noqa: E402
import welcome_sign         # noqa: E402  (clear_scene, _load_civic: the recipe-local part helpers)
from bldg_recipes import Budget   # noqa: E402

C = welcome_sign.C

NAME = "SkiBusDepotA"
PREFAB = "SkiBusDepot"
LOT = (2, 1)
HALF_X, HALF_Y = 4.0 * LOT[1], 4.0 * LOT[0]
SEED = 20261031

# ======================================================================================
# Dimensions (brief: 3 bays, about 8 x 3 m, 3 m at the eaves, the tower about 6 m)
# ======================================================================================

XR, XB = 2.0, -1.0                     # post centers, road row / back row
YS = (-4.0, -4.0 / 3.0, 4.0 / 3.0, 4.0)
PAD = (-1.6, -6.75, 3.7, 5.05, 0.10)   # x0, y0, x1, y1, top z
PZ = PAD[4]
POST = 0.18
PLATE, PLATE_T = 0.36, 0.022
BEAM_W, BEAM_D = 0.14, 0.30
BEAM_TOP_R, BEAM_TOP_B = 3.22, 2.82
SLOPE = (BEAM_TOP_R - BEAM_TOP_B) / (XR - XB)
EAVE_R, EAVE_B = XR + 0.55, XB - 0.48  # roof edges in x
ROOF_Y = 4.48
RAFTER = (0.09, 0.20)                  # width, depth
RAFTER_Y = tuple(-4.0 + i * 4.0 / 3.0 for i in range(7))
CROSS = (0.11, 0.22)
CEIL_ABOVE, ROOF_ABOVE = 0.20, 0.25    # over the beam-top line
BOARD_H, BOARD_T, BOARD_N, BOARD_Z0 = 0.185, 0.028, 9, 0.17
PANEL_Z = (0.44, 2.84)                 # road side frames (open gap at the ground)
END_PANEL_Z = (0.44, 2.44)             # end wall frame, under the back beam
FRAME = (0.08, 0.05)                   # face width, depth
PANEL_ROWS = 3
TOWER_Y = (-6.35, -4.55)               # broad faces to the road and the back
TOWER_X = (1.62, 1.88)
TOWER_TOP = 6.20
TOWER_POST = 0.10
TOWER_RAILS = (1.72, 3.32, 5.42)
LED_Y = (TOWER_Y[0] + TOWER_POST - 0.005, TOWER_Y[1] - TOWER_POST + 0.005)   # the clear width between the frame posts
LED_Z = (3.56, 5.18)                   # two 0.81 m rows: SKI over BUS
LED_T = 0.035
TEXT_CAP_M = 0.64
SCREEN_X = (0.02, 0.98)                # 0.96 x 0.42 m, hung under the cross beam at SCREEN_Y
SCREEN_Y = YS[2]
SCREEN_Z = (2.18, 2.60)
SCREEN_T = 0.07
HOOK_ROWS = ((1.46, (1.72, 2.20, 2.68, 3.16, 3.64)), (0.98, (1.96, 2.44, 2.92, 3.40)))
PLANTER = (-0.75, 5.30, 2.75, 7.35, 0.46)   # x0, y0, x1, y1, top z (low corten box)
PLANTER_T = 0.05
PLANTER_BED_Z = 0.36

# ======================================================================================
# Materials: hut-pilot method, one local key per real thing (a key never means two things)
# ======================================================================================

# Palette targets are encoded sRGB, read off the reference photos by eye, never sampled into a map.
# Planks021: 10 boards per image, so a 1.85 m tile is 18.5 cm boards, the plank height (one scan board per plank).
BOARD_TILE = 1.85
CORR = cs2_compose.DetailSpec(corrugation_m=0.11, corr_depth_m=0.012)
LOCAL_MATS = {
    "cedar": K.BldgMat("Planks021", (BOARD_TILE, BOARD_TILE), (0.74, 0.42, 0.185), smooth=0.32),
    "cedar_ceil": K.BldgMat("Planks021", (BOARD_TILE, BOARD_TILE), (0.64, 0.35, 0.155), smooth=0.28),
    "steel_ox": K.BldgMat("Metal032", (1.5, 1.5), (0.365, 0.175, 0.105), smooth=0.44),
    "corten": K.BldgMat("Metal038", (1.6, 1.6), (0.385, 0.200, 0.120), smooth=0.16),
    "roof_metal": K.BldgMat("Metal038", (1.6, 1.6), (0.56, 0.56, 0.55), smooth=0.42, metal=0.35, detail=CORR,
                            proj="inplane"),
    "pad_concrete": K.BldgMat("Concrete034", (3.0, 1.5), (0.70, 0.69, 0.66), proj="inplane"),
    "bed_rock": K.BldgMat("Rock030", (2.5, 2.5), (0.42, 0.40, 0.37), proj="inplane"),
    "hook_yellow": K.BldgMat(None, None, (0.93, 0.68, 0.06), smooth=0.45),
    "hook_black": K.BldgMat(None, None, (0.05, 0.05, 0.055), smooth=0.35),
    "case_dark": K.BldgMat(None, None, (0.075, 0.075, 0.08), smooth=0.45),
}
# Scan controls, order and math as cs2_tilesheet.texpass_apply on hut-textures.
CONTROLS = {
    "steel_ox": {"contrast": 1.35, "normal_gain": 2.0},
    "cedar": {"normal_gain": 1.6},
    "cedar_ceil": {"contrast": 0.85, "normal_gain": 1.3},
    "corten": {"chroma": 0.4, "contrast": 1.5, "normal_gain": 1.6},
    "roof_metal": {"chroma": 0.3, "contrast": 1.2},
    "pad_concrete": {"chroma": 0.4, "flatten_m": 0.5, "contrast": 0.8},
}
BAND_KEY = "band"
GLASS_KEY = "glass_dark"
K.BLDG_MATERIALS.update(LOCAL_MATS)

# Band px at A = 1024, multiples of 8 (welcome_sign's rule). LED rows 256 px/m at 2048.
BAND_PX_1024 = {"ski": (8, 8, 200, 104), "bus": (208, 8, 400, 104), "screen": (408, 8, 624, 104),
                "yellow": (632, 8, 680, 104), "black": (688, 8, 736, 104), "dark": (744, 8, 792, 104)}
FLAT_SWATCH = {"hook_yellow": "yellow", "hook_black": "black", "case_dark": "dark"}

BUDGET = Budget(2200, 2500, 1000, 300, 500)     # no PIPELINE row for this family: the analog is P2 2.5k
GROUND = ("pad_", "planterwall_")
TEX, AO_SAMPLES = 2048, 32
WELD_M, SHADE_ANGLE = 0.0005, 66.0
CHAMFER = 0.008                         # real chamfers on the steel bars: no razor edges, but wider ones read as soft plastic
EDGE_ROUND = cs2_compose.DetailSpec(edge_round_m=0.012)
EDGE_ROUND_PREFIXES = ("towerrail_", "wallcap_", "pfs_", "pfr_", "plate_", "towerplate_", "towercap_")
DENSITY = {"plank_": 1.70, "wallcap_": 1.0, "ceil_": 1.0, "roof_": 0.85, "roofedge_": 0.5, "pad_": 0.65,
           "post_": 1.2, "beam_": 1.0, "rafter_": 1.1, "crossb_": 1.1, "plate_": 2.0, "tower_f": 1.25, "tower_b": 0.9,
           "towerpost_": 1.0, "towerrail_": 1.0, "towercap_": 0.8, "pfs_": 2.0, "pfr_": 2.0, "hook": 1.0,
           "planter": 0.8, "ledcase_": 0.6, "screencase_": 0.8, "screenhang_": 0.8}


# Back wall rows per bay -> the canonical board that bay 0 owns: the low rows (the splash band) stay on their row, the
# rest shuffle, so bays never repeat as a block while nine boards carry all three bays (texel density, PIPELINE sec. 1).
def _plank_canon():
    rng = random.Random(SEED + 21)
    out = {}
    for b in range(len(YS) - 1):
        upper = list(range(3, BOARD_N))
        if b:
            rng.shuffle(upper)
        for n, c in enumerate(list(range(3)) + upper):
            out[(b, n)] = c
    return out


PLANK_CANON = _plank_canon()
HOOK_BOARDS = {(2, n) for n in (3, 4, 5, 6, 7)}     # the boards the two hook rows sit on


def share_key(n):
    """Identical members stack on one island (cs2_uv share): their weathering is by height or slope position only.
    The key groups names; share_groups proves every member is a translation of the first and raises otherwise."""
    m = re.match(r"^plank_b(\d)_(\d+)$", n)
    if m:
        if (int(m[1]), int(m[2])) in HOOK_BOARDS:   # a stacked texel shows the last copy's AO: the hook shadows must not repeat
            return None
        return f"plank{PLANK_CANON[(int(m[1]), int(m[2]))]}"
    m = re.match(r"^(roof_c|ceil_)(\d+)$", n)
    if m:
        i = int(m[2])
        if i in (0, len(RAFTER_Y)):              # the two narrow overhang cells
            return f"{m[1]}edge"
        return f"{m[1]}{i % (2 if m[1] == 'roof_c' else 3)}"
    for pat, key in ((r"^post_r\d$", "post_r"), (r"^post_b\d$", "post_b"), (r"^rafter_\d$", "rafter"),
                     (r"^crossb_\d$", "crossb"), (r"^plate_\d$", "plate"),
                     (r"^towerplate_\d$", "towerplate"), (r"^towerrail_\d$", "towerrail"), (r"^towerpost_\d$", "towerpost"),
                     (r"^pfs_r\d[ab]$", "pfs_r"), (r"^pfs_e\d[ab]$", "pfs_e"), (r"^pfr_r\d\d$", "pfr_r"),
                     (r"^pfr_e\d\d$", "pfr_e")):
        if re.match(pat, n):
            return key
    return None


DELETE1 = ("plate_", "towerplate_", "hook", "screenhang_", "wallcap_", "towerrail_", "planterin_")
DELETE2 = DELETE1 + ("crossb_", "pfs_", "pfr_", "screencase_", "screenface_", "planterbed_", "ledcase_")

# The vanilla sub-objects this art expects (authoring x, y, z; rows are written at the surgery round, not here).
# GUIDs in subobj_bounds.json where present.
SUB_OBJECTS = (
    dict(prop="GardenBenchRandom01", guid="8df0703be09426948b5bc3262d86bc04", at=(XB + 0.42, -1.2, PZ),
         note="against the back wall; vanilla variants vary, the yellow slat bench of the reference may not exist"),
    dict(prop="TrashbinRandom01", guid="41e3c9ba6e1d6b948a6b1bc3499a5397", at=(-0.55, 4.62, PZ), note="pad corner at the open end"),
    dict(prop="LightpoleParkRandom01", guid="c987b53e24a01e744b4a00ee5b1f8a08", at=(3.25, 6.3, 0.0),
         note="beside the planter"),
    dict(prop="TreeWildRandom01 x 2", guid="f7955fd41c1617847b4a4138acabcbcb", at=(0.3, 6.0, PLANTER_BED_Z), note="in the planter"),
    dict(prop="RockRandom01 x 3", guid="3f3008bdf841128428290d4f3678baa7", at=(1.0, 6.3, PLANTER_BED_Z), note="in the planter"),
)


def roof_line(x):
    """Beam-top line of the shed roof at x (the plane the rafters sit on)."""
    return BEAM_TOP_R + (x - XR) * SLOPE


# ======================================================================================
# Parts
# ======================================================================================

def build_parts():
    P = []
    x0, y0, x1, y1, zt = PAD
    P += K.box_part("pad_main", (x0, y0, 0.0), (x1, y1, zt), "pad_concrete", ground=True)
    # ---- posts, foot plates ------------------------------------------------------------
    k = 0
    for tag, x, top in (("r", XR, BEAM_TOP_R - BEAM_D), ("b", XB, BEAM_TOP_B - BEAM_D)):
        for i, y in enumerate(YS):
            P += bar(f"post_{tag}{i}", (x, y, PZ + PLATE_T - 0.005), (x, y, top + 0.01), POST, POST, "steel_ox", up=(1, 0, 0))
            s = PLATE / 2
            P += K.box_part(f"plate_{k}", (x - s, y - s, PZ), (x + s, y + s, PZ + PLATE_T), "steel_ox", drop=("-Z",))
            k += 1
    # ---- long beams, cross beams, rafters ----------------------------------------------
    for tag, x, top in (("r", XR, BEAM_TOP_R), ("b", XB, BEAM_TOP_B)):
        z = top - BEAM_D / 2
        P += bar(f"beam_{tag}", (x, -ROOF_Y + 0.08, z), (x, ROOF_Y - 0.08, z), BEAM_W, BEAM_D, "steel_ox", caps=(True, True))
    cw, cd = CROSS
    for i, y in enumerate(YS):
        P += bar(f"crossb_{i}", (XB + 0.03, y, roof_line(XB + 0.03) - cd / 2), (XR - 0.03, y, roof_line(XR - 0.03) - cd / 2), cw, cd, "steel_ox")
    rw, rd = RAFTER
    for i, y in enumerate(RAFTER_Y):
        P += bar(f"rafter_{i}", (EAVE_B + 0.02, y, roof_line(EAVE_B + 0.02) + rd / 2),
                 (EAVE_R - 0.02, y, roof_line(EAVE_R - 0.02) + rd / 2), rw, rd, "steel_ox", caps=(True, True))
    # ---- cedar ceiling and roof sheets: one quad per rafter bay, a few variants stacked (share_key) -------
    ys = (-ROOF_Y,) + RAFTER_Y + (ROOF_Y,)
    tb, tr = roof_line(EAVE_B) + ROOF_ABOVE, roof_line(EAVE_R) + ROOF_ABOVE
    cb, cr = roof_line(EAVE_B) + CEIL_ABOVE - 0.015, roof_line(EAVE_R) + CEIL_ABOVE - 0.015
    for i, (a, b) in enumerate(zip(ys, ys[1:])):
        pts = [(EAVE_B, a, roof_line(EAVE_B) + CEIL_ABOVE), (EAVE_R, a, roof_line(EAVE_R) + CEIL_ABOVE),
               (EAVE_R, b, roof_line(EAVE_R) + CEIL_ABOVE), (EAVE_B, b, roof_line(EAVE_B) + CEIL_ABOVE)]
        P += C.quads_part(f"ceil_{i}", [(pts, (0, 0, -1))], "cedar_ceil")
        P += C.quads_part(f"roof_c{i}", [([(EAVE_B, a, tb), (EAVE_R, a, tr), (EAVE_R, b, tr), (EAVE_B, b, tb)], (0, 0, 1))],
                          "roof_metal")
    P += C.quads_part("roofedge_all", [
        ([(EAVE_R, -ROOF_Y, cr), (EAVE_R, ROOF_Y, cr), (EAVE_R, ROOF_Y, tr), (EAVE_R, -ROOF_Y, tr)], (1, 0, 0)),
        ([(EAVE_B, -ROOF_Y, cb), (EAVE_B, ROOF_Y, cb), (EAVE_B, ROOF_Y, tb), (EAVE_B, -ROOF_Y, tb)], (-1, 0, 0)),
        ([(EAVE_B, -ROOF_Y, cb), (EAVE_R, -ROOF_Y, cr), (EAVE_R, -ROOF_Y, tr), (EAVE_B, -ROOF_Y, tb)], (0, -1, 0)),
        ([(EAVE_B, ROOF_Y, cb), (EAVE_R, ROOF_Y, cr), (EAVE_R, ROOF_Y, tr), (EAVE_B, ROOF_Y, tb)], (0, 1, 0))],
        "steel_ox")
    # ---- back wall: separate cedar boards with real gaps, a steel cap per bay -----------------
    rng = random.Random(SEED)
    gap_list = [rng.choice((0.003, 0.004, 0.004, 0.008, 0.012)) for _n in range(BOARD_N - 1)]
    t = BOARD_T / 2
    wall_top = 0.0
    for b, (ya, yb) in enumerate(zip(YS, YS[1:])):
        gaps = list(gap_list)                     # same total in every bay, so the caps line up
        if b:
            rng.shuffle(gaps)
        z = BOARD_Z0
        for n in range(BOARD_N):
            ya2, yb2 = ya + POST / 2 - 0.005, yb - POST / 2 + 0.005
            # two faces, no edges: the gaps between boards are real and stay light; ends die into the posts
            P += C.quads_part(f"plank_b{b}_{n}", [
                ([(XB + t, ya2, z), (XB + t, yb2, z), (XB + t, yb2, z + BOARD_H), (XB + t, ya2, z + BOARD_H)], (1, 0, 0)),
                ([(XB - t, ya2, z), (XB - t, yb2, z), (XB - t, yb2, z + BOARD_H), (XB - t, ya2, z + BOARD_H)], (-1, 0, 0))],
                "cedar")
            z += BOARD_H + (gaps[n] if n < BOARD_N - 1 else 0.0)
        wall_top = z
        zc = wall_top + 0.025
        P += bar(f"wallcap_{b}", (XB, ya + POST / 2 - 0.005, zc), (XB, yb - POST / 2 + 0.005, zc), 0.06, 0.05, "steel_ox", chamfer=0.0)
    # ---- ski hooks on the back wall -----------------------------------------------------------
    face_x = XB + t
    n = 0
    for z, ys_ in HOOK_ROWS:
        for y in ys_:
            P += hook_parts(n, face_x, y, z)
            n += 1
    # ---- clear panels: road side bays 0 and 1, the -Y end wall; bay 2 and the +Y end stay open
    bays = (("r0", "X", XR + POST / 2, YS[0], YS[1], PANEL_Z), ("r1", "X", XR + POST / 2, YS[1], YS[2], PANEL_Z),
            ("e0", "Y", YS[0] - POST / 2, XB + 0.02, XR - 0.02, END_PANEL_Z))
    for tag, axis, plane, a, b, zr in bays:
        P += panel_parts(tag, axis, plane, a, b, zr)
    # ---- tower: corten slab in a steel frame, rails, LED case on both faces ------------------
    xa, xb = TOWER_X
    ya, yb = TOWER_Y
    p = TOWER_POST
    # corten faces: above and below the LED case, nothing behind it (+X road side, -X back)
    u0, u1 = ya + p - 0.005, yb - p + 0.005
    z_top = TOWER_TOP - 0.02
    for tag, fx, out in (("f", xb, (1, 0, 0)), ("b", xa, (-1, 0, 0))):
        quads = [([(fx, u0, c_), (fx, u1, c_), (fx, u1, d_), (fx, u0, d_)], out) for c_, d_ in ((PZ, LED_Z[0]), (LED_Z[1], z_top))]
        P += C.quads_part(f"tower_{tag}", quads, "corten")
    xm = 0.5 * (xa + xb)
    for i, (u, v) in enumerate(((ya, ya + p), (yb - p, yb))):
        y = 0.5 * (u + v)
        P += bar(f"towerpost_{i}", (xm, y, PZ + PLATE_T - 0.005), (xm, y, TOWER_TOP - 0.015), (xb - xa) + 0.06, p, "steel_ox", up=(0, 1, 0), chamfer=0.010)
        s = 0.17
        P += K.box_part(f"towerplate_{i}", (xm - 0.27, y - s, PZ), (xm + 0.27, y + s, PZ + PLATE_T), "steel_ox", drop=("-Z",))
    P += K.box_part("towercap_0", (xa - 0.05, ya - 0.02, TOWER_TOP - 0.02), (xb + 0.05, yb + 0.02, TOWER_TOP + 0.04), "steel_ox")
    for i, z in enumerate(TOWER_RAILS):
        P += bar(f"towerrail_{i}", (xm, ya + p - 0.005, z + 0.025), (xm, yb - p + 0.005, z + 0.025), (xb - xa) + 0.04, 0.05, "steel_ox", chamfer=0.0)
    la, lb = LED_Y
    z0, z1 = LED_Z
    zm = 0.5 * (z0 + z1)
    fr, bk = xb + LED_T, xa - LED_T
    P += K.box_part("ledcase_0", (bk, la, z0), (fr, lb, z1), "case_dark", drop=("-X", "+X"))
    # one quad per LED row, on each face (+X road, -X back), both drawing the same band rects
    for rect, a, b in (("ski", zm, z1), ("bus", z0, zm)):
        P += C.quads_part(f"ledface_{rect}_f", [([(fr, la, a), (fr, lb, a), (fr, lb, b), (fr, la, b)], (1, 0, 0))], BAND_KEY)
        P += C.quads_part(f"ledface_{rect}_b", [([(bk, la, a), (bk, lb, a), (bk, lb, b), (bk, la, b)], (-1, 0, 0))], BAND_KEY)
    # ---- arrival screen hung under the cross beam ---------------------------------------------
    sa, sb = SCREEN_X
    s = SCREEN_T / 2
    za, zb = SCREEN_Z
    P += K.box_part("screencase_0", (sa, SCREEN_Y - s, za), (sb, SCREEN_Y + s, zb), "case_dark", drop=("-Y", "+Y"))
    P += C.quads_part("screenface_f", [([(sa, SCREEN_Y + s, za), (sb, SCREEN_Y + s, za), (sb, SCREEN_Y + s, zb), (sa, SCREEN_Y + s, zb)], (0, 1, 0))], BAND_KEY)
    P += C.quads_part("screenface_b", [([(sa, SCREEN_Y - s, za), (sb, SCREEN_Y - s, za), (sb, SCREEN_Y - s, zb), (sa, SCREEN_Y - s, zb)], (0, -1, 0))], BAND_KEY)
    for i, x in enumerate((sa + 0.18, sb - 0.18)):
        top = roof_line(x) - CROSS[1] + 0.02
        P += K.box_part(f"screenhang_{i}", (x - 0.02, SCREEN_Y - 0.02, zb - 0.01), (x + 0.02, SCREEN_Y + 0.02, top), "case_dark",
                        drop=("-Z", "+Z"))
    # ---- corten planter (modeled: vanilla has no match); rocks and trees are vanilla rows ----
    x0, y0, x1, y1, zt = PLANTER
    th = PLANTER_T
    zb_ = PLANTER_BED_Z
    for i, (lo, hi, inner) in enumerate((((x0, y0, 0.0), (x1, y0 + th, zt), "+Y"), ((x0, y1 - th, 0.0), (x1, y1, zt), "-Y"),
                                         ((x0, y0 + th, 0.0), (x0 + th, y1 - th, zt), "+X"), ((x1 - th, y0 + th, 0.0), (x1, y1 - th, zt), "-X"))):
        P += K.box_part(f"planterwall_{i}", lo, hi, "corten", drop=(inner,) + (("-Y", "+Y") if i > 1 else ()), ground=True)
        # only the strip above the bed shows inside
        ax = {"+Y": 1, "-Y": 1, "+X": 0, "-X": 0}[inner]
        c = hi[ax] if inner[0] == "+" else lo[ax]
        if ax == 1:
            pts = [(lo[0], c, zb_), (hi[0], c, zb_), (hi[0], c, zt), (lo[0], c, zt)]
            out = (0, 1 if inner[0] == "+" else -1, 0)
        else:
            pts = [(c, lo[1], zb_), (c, hi[1], zb_), (c, hi[1], zt), (c, lo[1], zt)]
            out = (1 if inner[0] == "+" else -1, 0, 0)
        P += C.quads_part(f"planterin_{i}", [(pts, out)], "corten")
    bed = [(x0 + th, y0 + th, PLANTER_BED_Z), (x1 - th, y0 + th, PLANTER_BED_Z), (x1 - th, y1 - th, PLANTER_BED_Z),
           (x0 + th, y1 - th, PLANTER_BED_Z)]
    P += C.quads_part("planterbed_0", [(bed, (0, 0, 1))], "bed_rock")
    return P


def bar(name, p0, p1, w, h, mat, up=(0.0, 0.0, 1.0), chamfer=CHAMFER, caps=(False, False)):
    """A steel bar from p0 to p1: w across side = axis x up, h along the up direction, section corners chamfered
    (8 faces around; chamfer 0 = 4). caps = (at p0, at p1). uv_prisms unwraps it as one strip."""
    def build(bm):
        P0, P1 = Vector(p0), Vector(p1)
        ax = (P1 - P0).normalized()
        side = ax.cross(Vector(up)).normalized()
        upv = side.cross(ax).normalized()
        a, b, ch = w / 2, h / 2, chamfer
        sec = ([(a - ch, b), (a, b - ch), (a, -b + ch), (a - ch, -b), (-a + ch, -b), (-a, -b + ch), (-a, b - ch), (-a + ch, b)]
               if ch > 0 else [(a, b), (a, -b), (-a, -b), (-a, b)])
        r0 = [P0 + side * s + upv * t for s, t in sec]
        r1 = [P1 + side * s + upv * t for s, t in sec]
        n = len(sec)
        c = 0.5 * (P0 + P1)
        for i in range(n):
            j = (i + 1) % n
            m = 0.25 * (r0[i] + r0[j] + r1[i] + r1[j])
            C.face(bm, [r0[i], r0[j], r1[j], r1[i]], tuple(m - c - ax * ax.dot(m - c)))
        if caps[0]:
            C.face(bm, r0, tuple(-ax))
        if caps[1]:
            C.face(bm, r1, tuple(ax))
    parts = C.new_part(name, build, mat)
    parts[0][0]["prism_axis"] = tuple((Vector(p1) - Vector(p0)).normalized())
    return parts


def hook_parts(n, x, y, z):
    """One ski hook, proud of the board face (+X): a black half-moon base and two yellow prongs, about 30 cm wide."""
    def base(bm):
        d = 0.028
        ring = [(-0.13, -0.01), (-0.115, -0.055), (-0.07, -0.075), (0.07, -0.075), (0.115, -0.055), (0.13, -0.01),
                (0.105, 0.04), (0.06, 0.062), (-0.06, 0.062), (-0.105, 0.04)]
        back = [(x + 0.002, y + u, z + v) for u, v in ring]
        front = [(x + d, y + 0.9 * u, z + 0.9 * v) for u, v in ring]
        C.face(bm, front, (1, 0, 0))
        for a in range(len(ring)):
            b = (a + 1) % len(ring)
            C.face(bm, [back[a], back[b], front[b], front[a]], None)
        bm.normal_update()
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    parts = C.new_part(f"hookbase_{n}", base, "hook_black")

    def prong(sign):
        def build(bm):
            # a thumb: wide on the base, dipping, then narrowing and curving up to a blunt tip
            y0, y1 = y + sign * 0.012, y + sign * 0.118
            # every quad stays planar: a ring keeps one y pair and one z pair (a twisted quad creases when triangulated)
            def ring(xa_, xb_, za_, zb_, inset):
                ya_, yb_ = y0 + sign * inset, y1 - sign * inset
                return [(x + xa_, ya_, z + za_), (x + xa_, yb_, z + za_), (x + xb_, yb_, z + zb_), (x + xb_, ya_, z + zb_)]
            root = ring(0.026, 0.026, -0.042, 0.024, 0.0)
            mid = ring(0.080, 0.086, -0.048, 0.008, 0.008)
            tip = ring(0.128, 0.138, 0.002, 0.054, 0.024)
            for r0_, r1_ in ((root, mid), (mid, tip)):
                for a, b in ((0, 1), (1, 2), (2, 3), (3, 0)):
                    C.face(bm, [r0_[a], r0_[b], r1_[b], r1_[a]], None)
            C.face(bm, tip, None)
            bm.normal_update()
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        return build
    parts += C.new_part(f"hookprong_{n}a", prong(1.0), "hook_yellow")
    parts += C.new_part(f"hookprong_{n}b", prong(-1.0), "hook_yellow")
    return parts


def panel_parts(tag, axis, plane, a, b, zr):
    """One bay of clear panel: two stiles + PANEL_ROWS + 1 rails bolted on the post faces, one pane per row.
    axis "X": the frame's plane is x = plane, outward +X, running a..b in y. axis "Y": y = plane, outward -Y."""
    fw, fd = FRAME
    z0, z1 = zr
    P = []

    def stile(name, ua, ub):
        m = 0.5 * (ua + ub)
        if axis == "X":
            return bar(name, (plane + fd / 2, m, z0), (plane + fd / 2, m, z1), fd, ub - ua, "steel_ox", up=(0, 1, 0), chamfer=0.0, caps=(True, True))
        return bar(name, (m, plane - fd / 2, z0), (m, plane - fd / 2, z1), fd, ub - ua, "steel_ox", up=(1, 0, 0), chamfer=0.0, caps=(True, True))

    def rail(name, ua, ub, zm_):
        if axis == "X":
            return bar(name, (plane + fd / 2, ua, zm_), (plane + fd / 2, ub, zm_), fd, fw, "steel_ox", chamfer=0.0)
        return bar(name, (ua, plane - fd / 2, zm_), (ub, plane - fd / 2, zm_), fd, fw, "steel_ox", chamfer=0.0)
    a2, b2 = a + 0.012, b - 0.012
    for i, (ua, ub) in enumerate(((a2, a2 + fw), (b2 - fw, b2))):
        P += stile(f"pfs_{tag}{'ab'[i]}", ua, ub)
    h = (z1 - z0 - fw) / PANEL_ROWS
    for r in range(PANEL_ROWS + 1):
        zr0 = z0 + r * h
        P += rail(f"pfr_{tag}{r}", a2 + fw, b2 - fw, zr0 + fw / 2)
    mid = plane + fd / 2 if axis == "X" else plane - fd / 2
    for r in range(PANEL_ROWS):
        za, zb = z0 + r * h + fw, z0 + (r + 1) * h
        ua, ub = a2 + fw, b2 - fw
        if axis == "X":
            pts = [(mid, ua, za), (mid, ub, za), (mid, ub, zb), (mid, ua, zb)]
            out = (1, 0, 0)
        else:
            pts = [(ua, mid, za), (ub, mid, za), (ub, mid, zb), (ua, mid, zb)]
            out = (0, -1, 0)
        P += C.quads_part(f"glass_{tag}{r}", [(pts, out)], GLASS_KEY)
    return P


# ======================================================================================
# Band: SKI / BUS in red LEDs, the arrival screen
# ======================================================================================

def band_rects(A):
    out = {}
    for k, px in BAND_PX_1024.items():
        q = [p * A / 1024.0 for p in px]
        if any(abs(p - round(p)) > 1e-9 for p in q):
            raise ValueError(f"band rect {k} not integral at A = {A}: {q}")
        out[k] = tuple(round(p) / float(A) for p in q)
    return out


def text_cov(body, tf, cap_m, w, h, ppu, ppv, cx=0.0, cy=0.0, track=1.0, stretch=1.0):
    """Coverage (h, w) bottom-up of one all-caps line, cap height cap_m, centered at (cx, cy) meters from the rect center."""
    tris = cs2_signset._ttf_font_tris(body, tf, track)
    pts = np.array([p for t in tris for p in t], np.float64)
    lo, hi = pts.min(0), pts.max(0)
    s = cap_m / (hi[1] - lo[1])
    c = 0.5 * (lo + hi)
    out = [tuple(((p[0] - c[0]) * s * stretch + cx, (p[1] - c[1]) * s + cy) for p in t) for t in tris]
    width = float((hi[0] - lo[0]) * s * stretch)
    return welcome_sign._tri_coverage(out, w, h, ppu, ppv), width


def paint_band(A, out_dir, name):
    rects = band_rects(A)
    base = np.zeros((A, A, 4), np.float32)
    nrm = np.zeros((A, A, 4), np.float32)
    msk = np.zeros((A, A, 4), np.float32)
    ctl = np.zeros((A, A, 4), np.float32)
    emis = np.zeros((A, A, 4), np.float32)
    base[..., 3] = emis[..., 3] = 1.0
    nrm[...] = (0.5, 0.5, 1.0, 1.0)
    rep = {}
    led_w, led_h = LED_Y[1] - LED_Y[0], 0.5 * (LED_Z[1] - LED_Z[0])
    tf = cs2_signset.typeface("bahnschrift_semibold")
    red = np.array((0.98, 0.06, 0.035), np.float32)
    pitch = 0.030
    for key, body in (("ski", "SKI"), ("bus", "BUS")):
        sl = cs2_compose._rect_slices(rects[key], A)
        h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
        ppu, ppv = w / led_w, h / led_h
        cov, width = text_cov(body, tf, TEXT_CAP_M, w, h, ppu, ppv, track=1.05)
        limit = led_w - 0.14
        if width > limit:           # narrow the line to the row, never the cap height
            cov, width = text_cov(body, tf, TEXT_CAP_M, w, h, ppu, ppv, track=1.05, stretch=limit / width)
        # Solid letters with the true anti-aliased edge and only a shallow dot grid inside: a real grid of dots averages to a
        # dim, stair-stepped smear in the mips, which is the distance this sign is read from.
        X, Y = np.meshgrid((np.arange(w) + 0.5) / ppu, (np.arange(h) + 0.5) / ppv)
        dx = X - (np.floor(X / pitch) + 0.5) * pitch
        dy = Y - (np.floor(Y / pitch) + 0.5) * pitch
        dot = np.clip((0.0135 - np.hypot(dx, dy)) * ppu + 0.5, 0.0, 1.0)
        led = (cov * (0.84 + 0.16 * dot)).astype(np.float32)
        field = np.array((0.035, 0.033, 0.033), np.float32)
        # sRGB-encoded here (written as a color PNG); dark unlit field with a hint of the dot grid
        c = np.broadcast_to(field, (h, w, 3)).copy() + 0.015 * dot[..., None]
        c = c * (1.0 - cov[..., None]) + red * led[..., None]
        base[sl][..., 0:3] = c
        msk[sl][..., 3] = 0.38      # a matte face: a glossy one takes the sky's reflection and washes the red to salmon
        emis[sl][..., 0:3] = red * led[..., None]
        rep[key] = dict(px=(w, h), ppm=(round(ppu, 1), round(ppv, 1)), width_m=round(width, 3), cap_m=TEXT_CAP_M)
    # screen: a dark panel with a route line and a minute count, nothing a player must read
    sl = cs2_compose._rect_slices(rects["screen"], A)
    h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
    sw, sh = SCREEN_X[1] - SCREEN_X[0], SCREEN_Z[1] - SCREEN_Z[0]
    ppu, ppv = w / sw, h / sh
    amber = np.array((0.98, 0.72, 0.18), np.float32)
    white = np.array((0.88, 0.90, 0.92), np.float32)
    c1, _ = text_cov("SKI BUS", tf, 0.125, w, h, ppu, ppv, cx=0.0, cy=0.085)
    c2, _ = text_cov("5 MIN", tf, 0.125, w, h, ppu, ppv, cx=0.0, cy=-0.085)
    c = np.broadcast_to(np.array((0.05, 0.055, 0.075), np.float32), (h, w, 3)).copy()
    c = c * (1 - c1[..., None]) + amber * c1[..., None]
    c = c * (1 - c2[..., None]) + white * c2[..., None]
    # bezel 2 cm
    bz = int(0.02 * ppu)
    c[:bz], c[-bz:], c[:, :bz], c[:, -bz:] = 0.03, 0.03, 0.03, 0.03
    base[sl][..., 0:3] = c
    msk[sl][..., 3] = 0.72
    emis[sl][..., 0:3] = np.maximum(amber * c1[..., None], white * c2[..., None]) * 0.8 + 0.03
    # flat swatches: tiny parts stack here instead of costing a mip margin each (u = root to tip shading)
    for sw, lo, hi, smooth in (("yellow", (0.60, 0.42, 0.03), (0.97, 0.74, 0.08), 0.5), ("black", (0.018, 0.018, 0.02), (0.075, 0.075, 0.08), 0.35),
                               ("dark", (0.045, 0.045, 0.05), (0.085, 0.085, 0.09), 0.45)):
        sl = cs2_compose._rect_slices(rects[sw], A)
        w = sl[1].stop - sl[1].start
        t = np.linspace(0.0, 1.0, w, dtype=np.float32)[None, :, None]
        base[sl][..., 0:3] = np.array(lo, np.float32) * (1 - t) + np.array(hi, np.float32) * t
        msk[sl][..., 3] = smooth
    os.makedirs(out_dir, exist_ok=True)
    paths = {}
    for arr, m, is_data in ((base, "BaseColor", False), (nrm, "Normal", True), (msk, "MaskMap", True), (ctl, "ControlMask", True)):
        p = os.path.join(out_dir, f"{name}_band_{m}.png")
        cs2_asset._write_png_raw(p, f"{name}Band{m}", arr, A, is_data)
        paths[m] = p
    return paths, emis, rep


def set_band_uvs(obj, table, A, part_keys):
    """LED / screen faces -> their rect, each read the right way round from its own side (authoring frame); every
    other island moved by whole tiles so nothing lands in a reserved rect (welcome_sign's rule)."""
    rects = band_rects(A)
    me = obj.data
    lay = me.uv_layers[cs2_bake.TILE_LAYER]
    uv = cs2_uv.loop_uvs(lay)
    pv = cs2_compose.part_indices(me)
    names = {i + 1: n for i, n in enumerate(table)}
    la, lb = LED_Y
    z0, z1 = LED_Z
    zm = 0.5 * (z0 + z1)
    band_faces = []
    for p in me.polygons:
        n = names.get(int(pv[p.index]), "")
        flat = FLAT_SWATCH.get(part_keys.get(n))
        if flat:
            band_faces.append(p.index)
            u0, v0, u1, v1 = rects[flat]
            du, dv = 0.12 * (u1 - u0), 0.12 * (v1 - v0)
            for j, li in enumerate(range(p.loop_start, p.loop_start + p.loop_total)):
                co = me.vertices[me.loops[li].vertex_index].co
                # hooks: root (the wall) dark, tip bright; cases: a soft top light. The small circle by loop index keeps
                # every face a real area on the ramp (a face at one x or one z would otherwise collapse to a line).
                s = np.clip((co.x - XB) / 0.13, 0.0, 1.0) if n.startswith("hook") else np.clip(0.5 + 0.5 * p.normal.z, 0.0, 1.0)
                a_ = 2.0 * math.pi * j / p.loop_total
                t = 0.5 + 0.4 * math.sin(a_)
                s = min(max(float(s) + 0.05 * math.cos(a_), 0.0), 1.0)
                uv[li] = (u0 + du + s * (u1 - u0 - 2 * du), v0 + dv + t * (v1 - v0 - 2 * dv))
            continue
        if not (n.startswith("ledface_") or n.startswith("screenface_")):
            continue
        band_faces.append(p.index)
        for li in range(p.loop_start, p.loop_start + p.loop_total):
            co = me.vertices[me.loops[li].vertex_index].co
            if n.startswith("ledface_"):
                key = n.split("_")[1]
                u0, v0, u1, v1 = rects[key]
                s = (co.y - la) / (lb - la)
                if n.endswith("_b"):
                    s = 1.0 - s
                za, zb = (zm, z1) if key == "ski" else (z0, zm)
                t = (co.z - za) / (zb - za)
            else:
                u0, v0, u1, v1 = rects["screen"]
                s = (co.x - SCREEN_X[0]) / (SCREEN_X[1] - SCREEN_X[0])
                if n.endswith("_f"):            # faces +Y: seen from +Y, image right is -X
                    s = 1.0 - s
                t = (co.z - SCREEN_Z[0]) / (SCREEN_Z[1] - SCREEN_Z[0])
            uv[li] = (u0 + s * (u1 - u0), v0 + t * (v1 - v0))
    cs2_uv.set_loop_uvs(lay, uv)
    others = sorted(set(range(len(me.polygons))) - set(band_faces))
    start, total = cs2_uv.face_loop_table(me)
    for isl in cs2_uv.uv_islands(me, lay, faces=others):
        idx = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in isl])
        uv[idx, 0] += math.ceil(2.0 - float(uv[idx, 0].min()))
    cs2_uv.set_loop_uvs(lay, uv)
    pinned = sorted(cs2_uv.pinned_faces(me, lay, list(rects.values())))
    if pinned != sorted(band_faces):
        raise RuntimeError(f"pinned faces {len(pinned)} != band faces {len(band_faces)}")
    return rects


# ======================================================================================
# Scan handling: one scan board per plank, the swatch controls
# ======================================================================================

def scan_board_rows(n_boards=10):
    """v ranges (0..1, bottom-up) of the scan's boards, measured from the dark seam rows of Planks021's Color map."""
    path = cs2_bake.find_set_map(cs2_bake._set_dir("Planks021"), "Color")
    im = bpy.data.images.load(path)
    w, h = im.size
    a = np.empty(w * h * 4, np.float32)
    im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    lum = a.reshape(h, w, 4)[..., :3].mean(-1).mean(1)
    # high-pass the row profile, then take the n darkest rows at least 60 % of a board apart (cyclic)
    k = int(h / n_boards)
    ker = np.ones(k) / k
    hp = lum - np.real(np.fft.ifft(np.fft.fft(lum) * np.fft.fft(np.roll(np.pad(ker, (0, h - k)), -k // 2))))
    order = np.argsort(hp)
    picks = []
    for r in order:
        if all(min(abs(r - q), h - abs(r - q)) > 0.6 * k for q in picks):
            picks.append(int(r))
        if len(picks) == n_boards:
            break
    picks = sorted(picks)
    if len(picks) != n_boards:
        raise RuntimeError(f"scan_board_rows: found {len(picks)} seams")
    out = []
    for a_, b_ in zip(picks, picks[1:] + [picks[0] + h]):
        m = (b_ - a_) * 0.09                 # inset: never sample the scan's own seam
        out.append(((a_ + m) / h, (b_ - m) / h))
    return out


def set_board_uvs(obj, table):
    """Every plank face (front and back) gets one scan board and its own phase along the grain; the ceiling bays and
    other big tiled faces get a random phase, so the tile's knots do not line up. Authoring frame, UVTile units."""
    rows = scan_board_rows()
    me = obj.data
    lay = me.uv_layers[cs2_bake.TILE_LAYER]
    uv = cs2_uv.loop_uvs(lay)
    pv = cs2_compose.part_indices(me)
    names = {i + 1: n for i, n in enumerate(table)}
    rng = random.Random(SEED + 7)
    # never two neighbors from one scan board
    assign, last = {}, -1
    for c in range(BOARD_N):
        for side in "fb":
            choice = rng.choice([i for i in range(len(rows)) if i != last])
            last = choice
            assign[(c, side)] = (choice, rng.random() * 5.0)
    ceil_phase = {}
    for n in table:
        if n.startswith("ceil_"):
            ceil_phase.setdefault(share_key(n), (rng.random(), rng.randrange(10) / 10.0))
    for p in me.polygons:
        n = names.get(int(pv[p.index]), "")
        if n.startswith("plank_") and abs(p.normal.x) > 0.9:
            side = "f" if p.normal.x > 0 else "b"
            m = re.match(r"^plank_b(\d)_(\d+)$", n)
            bay = int(m[1])
            ri, phase = assign[(PLANK_CANON[(bay, int(m[2]))], side)]
            v0, v1 = rows[ri]
            zs = [me.vertices[me.loops[li].vertex_index].co.z for li in range(p.loop_start, p.loop_start + p.loop_total)]
            za, zb = min(zs), max(zs)
            for li in range(p.loop_start, p.loop_start + p.loop_total):
                co = me.vertices[me.loops[li].vertex_index].co
                sgn = 1.0 if side == "f" else -1.0
                uv[li] = (phase + sgn * (co.y - YS[bay]) / BOARD_TILE, v0 + (co.z - za) / max(zb - za, 1e-6) * (v1 - v0))
        elif n.startswith("ceil_"):
            du, dv = ceil_phase[share_key(n)]
            ys = [me.vertices[me.loops[li].vertex_index].co.y for li in range(p.loop_start, p.loop_start + p.loop_total)]
            y0 = min(ys)
            for li in range(p.loop_start, p.loop_start + p.loop_total):
                co = me.vertices[me.loops[li].vertex_index].co
                # boards run along the shelter (+Y); the slope stays in v
                uv[li] = (du + (co.y - y0) / BOARD_TILE, dv + co.x / BOARD_TILE)
    cs2_uv.set_loop_uvs(lay, uv)


def uv_prisms(obj, table, axes):
    """UVTile for the bars: one strip per bar (u along the axis in meters / tile, v around the section), a cap hinged
    onto the strip's end, so a bar is ONE island in the pack (a loose face per bar face would cost a mip margin each).
    Flat boxes (pad, plates, cap, planter walls) get their top with the sides folded out, one island too. Runs on the
    joined mesh, authoring frame."""
    me = obj.data
    lay = me.uv_layers[cs2_bake.TILE_LAYER]
    uv = cs2_uv.loop_uvs(lay)
    pv = cs2_compose.part_indices(me)
    by_part = {}
    for p in me.polygons:
        by_part.setdefault(table[int(pv[p.index]) - 1], []).append(p)
    tile = {}
    for k_, m_ in K.BLDG_MATERIALS.items():
        if m_.set:
            tile[k_] = K.square_tile_m(m_)
    T = tile["steel_ox"]
    for n, axis in axes.items():
        polys = by_part[n]
        ax = Vector(axis)
        vids = sorted({v for p in polys for v in p.vertices})
        co = [me.vertices[i].co.copy() for i in vids]
        c = sum(co, Vector()) / len(co)
        side = ax.orthogonal().normalized()
        upv = ax.cross(side).normalized()
        ang = {}
        for i, q in zip(vids, co):
            d = q - c
            ang[i] = round(math.atan2(d.dot(upv), d.dot(side)), 3)
        order = sorted(set(ang.values()))
        # perimeter position per section corner, from one ring's edge lengths
        pts = {}
        for i, q in zip(vids, co):
            d = q - c
            pts.setdefault(ang[i], (d.dot(side), d.dot(upv)))
        s = {order[0]: 0.0}
        for a, b in zip(order, order[1:]):
            s[b] = s[a] + math.dist(pts[a], pts[b])
        wrap = s[order[-1]] + math.dist(pts[order[-1]], pts[order[0]])
        ulo = min((q - c).dot(ax) for q in co)
        uhi = max((q - c).dot(ax) for q in co)
        for p in polys:
            loops = range(p.loop_start, p.loop_start + p.loop_total)
            if abs(Vector(p.normal).dot(ax)) > 0.9:             # a cap: hinged on its longest section edge
                continue
            vs = [s[ang[me.loops[li].vertex_index]] for li in loops]
            top = max(vs)
            for li in loops:
                vi = me.loops[li].vertex_index
                v_ = s[ang[vi]]
                if top - v_ > 0.5 * wrap:              # the face that closes the ring
                    v_ += wrap
                uv[li] = ((me.vertices[vi].co - c).dot(ax) / T, v_ / T)
        for p in polys:
            if abs(Vector(p.normal).dot(ax)) <= 0.9:
                continue
            loops = list(range(p.loop_start, p.loop_start + p.loop_total))
            sign = 1.0 if Vector(p.normal).dot(ax) > 0 else -1.0
            end = uhi if sign > 0 else ulo
            # hinge edge: consecutive corners a, b, the longest one that does not wrap
            best = None
            for i_, li in enumerate(loops):
                lj = loops[(i_ + 1) % len(loops)]
                va, vb = me.loops[li].vertex_index, me.loops[lj].vertex_index
                sa, sb = s[ang[va]], s[ang[vb]]
                if abs(sa - sb) > 0.5 * wrap:
                    continue
                ln = abs(sa - sb)
                if best is None or ln > best[0]:
                    best = (ln, va, vb, sa, sb)
            _, va, vb, sa, sb = best
            e0, e1 = me.vertices[va].co, me.vertices[vb].co
            ed = (e1 - e0).normalized()
            # toward the section center, in the cap's plane
            inward = c + ax * (e0 - c).dot(ax) - e0
            inward = (inward - ed * inward.dot(ed)).normalized()
            for li in loops:
                q = me.vertices[me.loops[li].vertex_index].co - e0
                uv[li] = ((end + sign * q.dot(inward)) / T, (sa + q.dot(ed) * (1.0 if sb > sa else -1.0)) / T)
    # flat boxes: top in x, y, each side folded out by its drop
    for n, polys in by_part.items():
        if not n.startswith(("pad_", "plate_", "towerplate_", "towercap_", "planterwall_")):
            continue
        key = "pad_concrete" if n.startswith("pad_") else ("corten" if n.startswith("planter") else "steel_ox")
        Tb = tile[key]
        ztop = max(me.vertices[v].co.z for p in polys for v in p.vertices)
        for p in polys:
            nv = Vector(p.normal)
            for li in range(p.loop_start, p.loop_start + p.loop_total):
                q = me.vertices[me.loops[li].vertex_index].co
                dz = ztop - q.z
                uv[li] = ((q.x + (dz if nv.x > 0.5 else -dz if nv.x < -0.5 else 0.0)) / Tb,
                          (q.y + (dz if nv.y > 0.5 else -dz if nv.y < -0.5 else 0.0)) / Tb)
    cs2_uv.set_loop_uvs(lay, uv)


def depot_pack(obj, mesh, A, reserved, margin_px=None, equalise=True, density_layer=cs2_uv.DENSITY_ATTR, share=None):
    """Replaces cs2_uv._pack_layout for this build's bake: exact mip-safe margins (A // 64, PIPELINE section 1: Blender's
    pack_islands could not hold them, the snow props' finding) with one density scale found by binary search, density_w
    honored per island, no rotation."""
    margin = cs2_uv.default_margin_px(A) if margin_px is None else int(margin_px)
    layer = mesh.uv_layers[cs2_uv.UNIQUE_LAYER]
    mesh.uv_layers.active = layer
    pinned = cs2_uv.pinned_faces(mesh, layer, reserved)
    dup, src, dst = cs2_uv._share_loops(mesh, share or [], pinned)
    hold = pinned | dup
    free = [i for i in range(len(mesh.polygons)) if i not in hold]
    uv = cs2_uv.loop_uvs(layer)
    start, total = cs2_uv.face_loop_table(mesh)
    wa = cs2_uv.face_world_areas(mesh)
    ua = cs2_uv.face_uv_areas(mesh, uv)
    weights = cs2_uv.face_density_weights(mesh, density_layer)
    isl = []
    for faces in cs2_uv.uv_islands(mesh, layer, faces=free):
        idx = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in faces])
        a3, a2 = float(sum(wa[f] for f in faces)), float(sum(ua[f] for f in faces))
        m_per_uv = math.sqrt(a3 / max(a2, 1e-12))
        w = float(sum(weights[f] * wa[f] for f in faces)) / max(a3, 1e-12)
        lo, hi = uv[idx].min(0), uv[idx].max(0)
        isl.append(dict(idx=idx, lo=lo, ext=(hi - lo) * m_per_uv, scale=m_per_uv, w=w))
    v_lo, v_hi = cs2_uv.free_v_band(reserved) if reserved else (0.0, 1.0)
    y_min, y_max = int(math.ceil(v_lo * A)) + margin // 2, int(v_hi * A) - margin // 2
    x_max = A - margin // 2

    def fit(k):
        order = sorted(isl, key=lambda d: -(d["ext"][1] * d["w"]))
        x, y, row_h, out = margin // 2, y_min, 0, []
        for d in order:
            w_, h_ = d["ext"] * k * d["w"]
            wp, hp = int(math.ceil(w_)), int(math.ceil(h_))
            if x + wp > x_max:
                x, y, row_h = margin // 2, y + row_h + margin, 0
            if wp > x_max or y + hp > y_max:
                return None
            out.append((d, x, y))
            x += wp + margin
            row_h = max(row_h, hp)
        return out
    lo_k, hi_k = 1.0, float(A)
    best = None
    for _ in range(40):
        k = 0.5 * (lo_k + hi_k)
        r = fit(k)
        if r is None:
            hi_k = k
        else:
            lo_k, best = k, (k, r)
    if best is None:
        raise RuntimeError("depot_pack: nothing fits")
    k, placed = best
    for d, x, y in placed:
        s = d["scale"] * k * d["w"] / float(A)
        uv[d["idx"]] = (uv[d["idx"]] - d["lo"]) * s + np.array((x, y), np.float64) / float(A)
    cs2_uv.set_loop_uvs(layer, uv)
    if len(dst):
        uv[dst] = uv[src]
        cs2_uv.set_loop_uvs(layer, uv)
    layer = mesh.uv_layers[cs2_uv.UNIQUE_LAYER]
    cs2_uv.check_layout(mesh, layer, reserved, pinned)
    fill = sum(float(cs2_uv.face_uv_areas(mesh, uv)[f]) for f in free)
    print(f"[depot] pack @ {A}: {len(isl)} islands, {len(pinned)} pinned, {len(dup)} stacked, margin {margin} px, "
          f"w=1 {k:.1f} px/m, fill {fill:.3f}")
    return {"A": int(A), "margin_px": margin, "faces": len(mesh.polygons), "pinned_faces": len(pinned), "islands": len(isl),
            "band": (v_lo, v_hi), "w1_px_per_m": round(k, 1), "fill": round(fill, 3), "shared_faces": len(dup)}


LUMA = np.array((0.2126, 0.7152, 0.0722))


def _blur_periodic(img, sigma_px):
    n0, n1 = img.shape[:2]
    f0, f1 = np.fft.fftfreq(n0), np.fft.fftfreq(n1)
    g = np.exp(-2.0 * (np.pi * float(sigma_px)) ** 2 * (f0[:, None] ** 2 + f1[None, :] ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(img) * g))


def apply_controls(tps, lin, nrm, rough, ppm):
    """cs2_tilesheet.texpass_apply's controls on a prefiltered tile (linear RGB, 0..1 normal, roughness); flatten and
    contrast keep each channel's mean."""
    def keep_mean(new, old):
        return new * (old.reshape(-1, 3).mean(0) / np.maximum(new.reshape(-1, 3).mean(0), 1e-9))
    if "chroma" in tps:
        lum = (lin @ LUMA)[..., None]
        lin = lum + (lin - lum) * tps["chroma"]
    if "flatten_m" in tps:
        sig = tps["flatten_m"] * ppm
        lum = lin @ LUMA
        lin = keep_mean(lin * (lum.mean() / np.maximum(_blur_periodic(lum, sig), 1e-6))[..., None], lin)
        rough = np.clip(rough - _blur_periodic(rough, sig) + rough.mean(), 0.0, 1.0)
    if "contrast" in tps:
        lum = lin @ LUMA
        m = lum.mean()
        lin = keep_mean(lin * ((m + (lum - m) * tps["contrast"]) / np.maximum(lum, 1e-6))[..., None], np.clip(lin, 0, None))
    if "normal_gain" in tps:
        n = nrm * 2.0 - 1.0
        n[..., :2] *= tps["normal_gain"]
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-9)
        nrm = n * 0.5 + 0.5
    return lin, nrm, rough


def controlled_specs(specs, info, work_dir):
    """The prefiltered source of each CONTROLS key through apply_controls into work_dir, the tint re-derived so the
    palette mean holds (the hut pilot's target_srgb rule)."""
    out = dict(specs)
    for key, tps in CONTROLS.items():
        if key not in specs:
            continue
        s = specs[key]
        src = os.path.normpath(s.texture_set)
        tag = os.path.basename(src)
        dtag = f"{tag}x{key}"
        ddir = os.path.join(work_dir, dtag)
        os.makedirs(ddir, exist_ok=True)
        col = K._load_raw(os.path.join(src, f"{tag}_Color.png"))[..., :3]
        nrm = K._load_raw(os.path.join(src, f"{tag}_NormalGL.png"))[..., :3]
        rough = K._load_raw(os.path.join(src, f"{tag}_Roughness.png"))[..., 0]
        lin, nrm, rough = apply_controls(tps, K.srgb_to_linear(col), nrm, rough, col.shape[0] / s.metres_per_tile)
        q = np.rint(np.clip(K.linear_to_srgb(lin), 0.0, 1.0) * 255.0) / 255.0
        mean_lin = K.srgb_to_linear(q).reshape(-1, 3).mean(0)
        for m, rgb in (("Color", q), ("NormalGL", nrm), ("Roughness", np.repeat(rough[..., None], 3, -1))):
            px = np.ones(rgb.shape[:2] + (4,), np.float64)
            px[..., :3] = np.clip(rgb, 0.0, 1.0)
            K._write_raw_png(os.path.join(ddir, f"{dtag}_{m}.png"), px)
        tl = K.srgb_to_linear(tuple(K.BLDG_MATERIALS[key].target_srgb))
        tint = tuple(float(a) / max(float(b), 1e-6) for a, b in zip(tl, mean_lin))
        out[key] = replace(s, texture_set=ddir, tint=tint)
        info[key] = dict(info[key], controls=tps, tint_lin=[round(t, 4) for t in tint])
    return out


# ======================================================================================
# Weathering: the post-compose paint in the authoring frame (low frequency, >= 5 cm, PIPELINE section 1)
# ======================================================================================

class Noise:
    def __init__(self, seed, n=96):
        self.n = n
        self.t = np.random.default_rng(seed).random((n, n, n), np.float32)

    def __call__(self, x, y, z):
        n = self.n
        x, y, z = np.asarray(x, np.float32), np.asarray(y, np.float32), np.asarray(z, np.float32)
        ix, iy, iz = np.floor(x), np.floor(y), np.floor(z)
        fx, fy, fz = x - ix, y - iy, z - iz
        fx, fy, fz = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy), fz * fz * (3 - 2 * fz)
        ix, iy, iz = ix.astype(np.int64) % n, iy.astype(np.int64) % n, iz.astype(np.int64) % n
        jx, jy, jz = (ix + 1) % n, (iy + 1) % n, (iz + 1) % n
        t = self.t
        c00 = t[ix, iy, iz] * (1 - fx) + t[jx, iy, iz] * fx
        c10 = t[ix, jy, iz] * (1 - fx) + t[jx, jy, iz] * fx
        c01 = t[ix, iy, jz] * (1 - fx) + t[jx, iy, jz] * fx
        c11 = t[ix, jy, jz] * (1 - fx) + t[jx, jy, jz] * fx
        return (c00 * (1 - fy) + c10 * fy) * (1 - fz) + (c01 * (1 - fy) + c11 * fy) * fz

    def fbm(self, x, y, z, octaves=3):
        s, a, tot = 0.0, 1.0, 0.0
        for o in range(octaves):
            s = s + a * self(x * 2 ** o + 13.1 * o, y * 2 ** o + 7.7 * o, z * 2 ** o + 3.3 * o)
            tot += a
            a *= 0.5
        return s / tot


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def world_fields(me, A):
    """Per texel: authoring position (x, y, z), authoring normal, part id (0 = gutter), from the unique layout of the
    oriented mesh (orient_for_net applied: world = (y, -x, z))."""
    P = np.zeros((A, A, 3), np.float32)
    N = np.zeros((A, A, 3), np.float32)
    part = np.zeros((A, A), np.int32)
    ids = cs2_compose.part_indices(me)
    for poly, sl, mask, X, Y, q, Pv, _ppm in cs2_compose.iter_face_rasters(me, A, "UVUnique", 1.5):
        qm, Pm, Mt = cs2_compose._affine_uv_to_world(q, Pv)
        d = np.stack([X - qm[0], Y - qm[1]], -1)
        w = Pm + d @ Mt
        a = np.stack([-w[..., 1], w[..., 0], w[..., 2]], -1)
        nw = poly.normal
        P[sl][mask] = a[mask]
        N[sl][mask] = (-nw.y, nw.x, nw.z)
        part[sl][mask] = ids[poly.index]
    return P, N, part


def tint_mul(base, idx, f):
    base[idx + (slice(0, 3),)] = base[idx + (slice(0, 3),)] * f


def paint_weather(me, A, table, base, msk):
    """Return the height field (meters) of the painted relief; base (linear) and msk are changed in place."""
    P, N, part = world_fields(me, A)
    h = np.zeros((A, A), np.float32)
    nz = Noise(SEED)
    names = {i + 1: n for i, n in enumerate(table)}

    def mask_of(*prefixes):
        ids = [i for i, n in names.items() if n.startswith(prefixes)]
        return np.nonzero(np.isin(part, ids))

    def rgb(idx):
        return base[idx[0], idx[1], :3]

    # ---- oxblood steel: mottled paint, duller and rust-blushed at the feet, a few chips at knee height
    idx = mask_of("post_", "beam_", "rafter_", "crossb_", "plate_", "towerpost_", "towerplate_", "towerrail_", "towercap_",
                  "pfs_", "pfr_", "wallcap_", "roofedge_")
    x, y, z = P[idx][..., 0], P[idx][..., 1], P[idx][..., 2]
    m = nz.fbm(x / 0.55 + 3, y / 0.55, z / 0.9)
    # hand-painted, not powder-coated: broad mottling, vertical runs, a shallow wave in the paint over the rolled steel
    streak = nz(x / 0.07 + 11, y / 0.07, z / 1.4)
    rgbv = rgb(idx) * ((0.82 + 0.36 * m) * (0.90 + 0.20 * streak))[..., None]
    h[idx] += 0.006 * nz.fbm(x / 0.10 + 7, y / 0.10, z / 0.14)
    foot = 1.0 - smoothstep(0.14, 0.62, z + 0.18 * (nz(x / 0.25, y / 0.25, z / 0.25) - 0.5))
    rust = np.array((1.25, 0.86, 0.62), np.float32)
    rgbv = rgbv * (1.0 + (rust - 1.0) * foot[..., None])
    chip = smoothstep(0.71, 0.78, nz.fbm(x / 0.16, y / 0.16, z / 0.32)) * smoothstep(0.25, 0.5, z) * (1 - smoothstep(1.2, 1.7, z))
    rgbv = rgbv * (1.0 - chip[..., None] * 0.28)
    base[idx[0], idx[1], :3] = rgbv
    msk[idx[0], idx[1], 3] = np.clip(msk[idx[0], idx[1], 3] - 0.26 * foot - 0.16 * chip + 0.10 * (m - 0.5), 0.0, 1.0)
    # ---- the weld bead where a post meets its foot plate: a raised, darker ring (4 texels at the posts' density)
    idx = mask_of("post_", "towerpost_")
    weld = np.exp(-((P[idx][..., 2] - (PZ + PLATE_T + 0.012)) / 0.018) ** 2)
    h[idx] += 0.006 * weld
    base[idx[0], idx[1], :3] *= (1.0 - 0.38 * weld)[..., None]
    msk[idx[0], idx[1], 3] = np.clip(msk[idx[0], idx[1], 3] - 0.12 * weld, 0.0, 1.0)
    # ---- bolts on the foot plates; screw heads at the panel corners -----------------------------
    idx = mask_of("plate_", "towerplate_")
    up = N[idx][..., 2] > 0.9
    idx = (idx[0][up], idx[1][up])
    x, y = P[idx][..., 0], P[idx][..., 1]
    # plates stack on one island (share): the bolt pattern is relative to each plate's center
    cx = np.round((x - XR) / (XR - XB)) * (XR - XB) + XR
    near_y = np.array(YS)[np.argmin(np.abs(y[..., None] - np.array(YS)), -1)]
    d = np.minimum.reduce([np.hypot(x - (cx + sx * 0.125), y - (near_y + sy * 0.125)) for sx in (-1, 1) for sy in (-1, 1)])
    dome = np.clip(1.0 - (d / 0.021) ** 2, 0.0, 1.0)
    h[idx] += 0.008 * dome
    base[idx[0], idx[1], :3] *= (1.0 + 0.55 * dome - 0.45 * smoothstep(0.036, 0.022, d) * (d > 0.021))[..., None]
    msk[idx[0], idx[1], 3] = np.clip(msk[idx[0], idx[1], 3] + 0.1 * dome, 0, 1)
    idx = mask_of("pfs_", "pfr_")
    n_out = (N[idx][..., 0] > 0.9) | (N[idx][..., 1] < -0.9)
    idx = (idx[0][n_out], idx[1][n_out])
    x, y, z = P[idx][..., 0], P[idx][..., 1], P[idx][..., 2]
    # along = y on the road frames, x on the end frame; a screw every rail / stile crossing, plus a mid-rail one
    road = N[idx][..., 0] > 0.9
    along = np.where(road, y, x)
    fw = FRAME[0]
    h_row = (PANEL_Z[1] - PANEL_Z[0] - fw) / PANEL_ROWS
    e_row = (END_PANEL_Z[1] - END_PANEL_Z[0] - fw) / PANEL_ROWS
    rz = np.where(road, (z - PANEL_Z[0] - fw / 2) / h_row, (z - END_PANEL_Z[0] - fw / 2) / e_row)
    dz = np.abs(rz - np.round(rz)) * np.where(road, h_row, e_row)
    edges = [e for a, b in zip(YS, YS[1:]) for e in (a + 0.012 + fw / 2, 0.5 * (a + b), b - 0.012 - fw / 2)] + \
            [XB + 0.02 + 0.012 + fw / 2, 0.5 * (XB + XR), XR - 0.02 - 0.012 - fw / 2]
    du = np.min(np.abs(along[..., None] - np.array(edges)), -1)
    d = np.hypot(du, dz)
    dome = np.clip(1.0 - (d / 0.010) ** 2, 0.0, 1.0)
    h[idx] += 0.005 * dome
    base[idx[0], idx[1], :3] = base[idx[0], idx[1], :3] * (1 - dome[..., None]) + np.array((0.24, 0.24, 0.23), np.float32) * dome[..., None]
    msk[idx[0], idx[1], 3] = np.clip(msk[idx[0], idx[1], 3] + 0.1 * dome, 0, 1)
    # ---- cedar boards: board-to-board color, a splash band low down, gray-brown wear at the bottom course
    rng = np.random.default_rng(SEED + 3)
    plank_ids = sorted(i for i, n in names.items() if n.startswith("plank_"))
    per = np.ones((max(names) + 2, 3), np.float32)
    for i in plank_ids:
        l = rng.uniform(0.82, 1.18)
        hue = rng.uniform(-0.09, 0.09)
        per[i] = (l * (1 + hue), l, l * (1 - hue * 1.5))
    idx = mask_of("plank_")
    x, y, z = P[idx][..., 0], P[idx][..., 1], P[idx][..., 2]
    rgbv = rgb(idx) * per[part[idx]]
    m = nz.fbm(y / 0.75, z / 0.4, x * 2 + 9)
    rgbv = rgbv * (0.90 + 0.20 * m)[..., None]
    splash = 1.0 - smoothstep(0.14, 0.66, z + 0.10 * (nz(y / 0.4, z / 0.4, 1.0) - 0.5))
    rgbv = rgbv * (1.0 - splash[..., None] * np.array((0.26, 0.30, 0.30), np.float32))
    outside = N[idx][..., 0] < -0.9
    rgbv[outside] = rgbv[outside] * np.array((0.88, 0.86, 0.86), np.float32)   # the weather side
    # a few painted knots per board: long dark eyes with a soft halo (about 8 x 4.5 cm, well above the 5 cm color floor)
    for i in plank_ids:
        sel = part[idx] == i
        if not sel.any():
            continue
        ys_, zs_ = y[sel], z[sel]
        for _k in range(int(rng.choice((0, 1, 1, 2)))):
            ky, kz = rng.uniform(ys_.min() + 0.10, ys_.max() - 0.10), rng.uniform(zs_.min() + 0.045, zs_.max() - 0.045)
            d = np.hypot((ys_ - ky) / 0.040, (zs_ - kz) / 0.022)
            eye = np.clip(1.3 - d, 0.0, 1.0)
            rgbv[sel] = rgbv[sel] * (1.0 - 0.60 * eye - 0.13 * np.exp(-(d / 2.4) ** 2))[..., None]
    base[idx[0], idx[1], :3] = rgbv
    msk[idx[0], idx[1], 3] = np.clip(msk[idx[0], idx[1], 3] - 0.10 * splash, 0, 1)
    # ---- ceiling: a little board and bay variation, darker over the back
    idx = mask_of("ceil_")
    x, y = P[idx][..., 0], P[idx][..., 1]
    m = nz.fbm(y / 1.1 + 40, x / 0.5, 2.0)
    base[idx[0], idx[1], :3] *= (0.86 + 0.28 * m)[..., None] * (0.90 + 0.10 * smoothstep(EAVE_B, EAVE_R, x))[..., None]
    # ---- corten: patches, vertical streak, darker runs below rails and the LED case, the faint mountain
    idx = mask_of("tower_", "planterwall_", "planterin_")
    x, y, z = P[idx][..., 0], P[idx][..., 1], P[idx][..., 2]
    tower = x > 1.0
    c = rgb(idx)
    patch = nz.fbm(y / 0.45 + x, z / 0.8, 5.0 + x, 4)
    dark = np.array((0.56, 0.50, 0.50), np.float32)
    orange = np.array((1.42, 1.16, 0.84), np.float32)
    t = smoothstep(0.34, 0.68, patch)[..., None]
    c = c * (dark * (1 - t) + orange * t)
    streak = nz.fbm(y / 0.10 + 3 * x, z / 2.2, 11.0, 3)
    streak2 = nz.fbm(y / 0.28 + 3 * x, z / 3.0, 17.0, 2)
    c = c * (0.72 + 0.58 * streak * 0.6 + 0.58 * streak2 * 0.4)[..., None]
    # runs: below every rail and the LED case, fading over 0.5-0.9 m, broken up by the streak field
    runs = np.zeros_like(z)
    for zr, fade in [(r, 0.7) for r in TOWER_RAILS] + [(LED_Z[0], 0.9), (TOWER_TOP, 0.9)]:
        dz = zr - z
        runs = np.maximum(runs, (dz > 0) * (1 - smoothstep(0.0, fade, dz)))
    runs = runs * smoothstep(0.28, 0.62, streak)
    c = c * (1.0 - runs[..., None] * tower[..., None] * np.array((0.42, 0.48, 0.48), np.float32))
    # lighter orange washes beside the runs near the foot, rust on the ground side
    low = 1.0 - smoothstep(PZ, 0.85, z)
    c = c * (1.0 + low[..., None] * np.array((0.10, -0.12, -0.25), np.float32))
    # the faint mountain: a ridge line across the slab under the sign, darker rock with lighter snow fields. Procedural,
    # the shape is noise, never traced from a photo.
    ridge = 2.72 + 0.46 * np.sin((y + 5.45) * 1.9 + 0.6) + 0.60 * (nz.fbm(y / 0.7, 3.5 * np.ones_like(y), 21.0, 4) - 0.5)
    body = smoothstep(0.02, -0.04, z - ridge) * smoothstep(1.76, 1.92, z)
    snow = smoothstep(0.50, 0.62, nz.fbm(y / 0.16 + z / 0.3, z / 0.22, 31.0, 3)) * smoothstep(ridge - 0.72, ridge - 0.2, z)
    mtn = body[..., None] * tower[..., None]
    c = c * (1.0 - mtn * 0.26) * (1.0 + mtn * snow[..., None] * 0.62)
    base[idx[0], idx[1], :3] = c
    msk[idx[0], idx[1], 3] = np.clip(0.10 + 0.14 * (1 - patch), 0.0, 1.0)
    # ---- roof: long streaks down the slope, sheet laps, dirt along the low eave, a rust blush at the edges
    idx = mask_of("roof_")
    x, y = P[idx][..., 0], P[idx][..., 1]
    s = nz.fbm(y / 0.22, x / 2.6, 41.0, 3)
    low_eave = 1.0 - smoothstep(EAVE_B, EAVE_B + 1.2, x + 0.5 * (s - 0.5))
    c = rgb(idx) * (0.82 + 0.36 * s)[..., None]
    c = c * (1.0 - low_eave[..., None] * 0.32)
    lap = 0.91
    dl = np.abs(((y + ROOF_Y) / lap + 0.5) % 1.0 - 0.5) * lap
    lap_line = 1.0 - smoothstep(0.02, 0.05, dl)
    c = c * (1.0 - lap_line[..., None] * 0.20)
    edge = 1.0 - smoothstep(0.0, 0.34, np.minimum.reduce([x - EAVE_B, EAVE_R - x, y + ROOF_Y, ROOF_Y - y]))
    c = c * (1.0 + (edge * smoothstep(0.46, 0.62, nz.fbm(y / 0.3, x / 0.3, 7.0)))[..., None] * np.array((0.25, -0.12, -0.30), np.float32))
    base[idx[0], idx[1], :3] = c
    msk[idx[0], idx[1], 3] = np.clip(msk[idx[0], idx[1], 3] - 0.14 * edge - 0.1 * low_eave, 0, 1)
    h[idx] += -0.004 * lap_line
    # ---- concrete: saw joints, broom lines, drip line under the road eave, stains at the post feet, darker edges
    idx = mask_of("pad_")
    x, y, z = P[idx][..., 0], P[idx][..., 1], P[idx][..., 2]
    up = N[idx][..., 2] > 0.9
    c = rgb(idx) * (0.90 + 0.20 * nz.fbm(x / 1.3, y / 1.3, 51.0, 3))[..., None]
    c = c * (0.96 + 0.08 * nz.fbm(x / 0.25, y / 0.25, 53.0, 2))[..., None]
    pitch_y, pitch_x = 2.35, 2.65
    jy = np.abs(((y - 0.35) / pitch_y + 0.5) % 1.0 - 0.5) * pitch_y
    jx = np.abs(((x - 0.25) / pitch_x + 0.5) % 1.0 - 0.5) * pitch_x
    joint = (1.0 - smoothstep(0.012, 0.038, np.minimum(jx, jy))) * up
    c = c * (1.0 - joint[..., None] * 0.48)
    h[idx] += -0.007 * joint
    broom = up * 0.0009 * np.sin(2 * np.pi * x / 0.075 + 2.0 * nz(x / 0.5, y / 0.5, 61.0)) * smoothstep(0.1, 0.4, nz(x / 0.9, y / 0.9, 63.0) + 0.2)
    h[idx] += broom
    drip = np.exp(-(((x - EAVE_R) / 0.13) ** 2)) * (np.abs(y) < ROOF_Y) * up
    c = c * (1.0 - drip[..., None] * (0.20 + 0.14 * nz(y / 0.2, 1.0, 71.0))[..., None])
    wear = np.exp(-((y - YS[2] - 1.33) / 1.1) ** 2) * np.exp(-((x - 0.8) / 1.2) ** 2) * up
    c = c * (1.0 + wear[..., None] * 0.07)
    feet = np.zeros_like(x)
    for fx in (XR, XB):
        for fy in YS:
            feet = np.maximum(feet, np.exp(-(((x - fx) ** 2 + (y - fy) ** 2) / (0.42 ** 2))))
    feet = np.maximum(feet, np.exp(-(((x - 1.75) ** 2 + (y + 5.45) ** 2 / 2.2) / (0.46 ** 2))))
    c = c * (1.0 - (feet * up)[..., None] * np.array((0.18, 0.24, 0.30), np.float32))
    edge = 1.0 - smoothstep(0.0, 0.25, np.minimum.reduce([x - PAD[0], PAD[2] - x, y - PAD[1], PAD[3] - y]))
    c = c * (1.0 - (edge * 0.10)[..., None])
    base[idx[0], idx[1], :3] = c
    return h


def make_post(obj, band_paths, A, table, rects):
    """cs2_bake post_compose: band back over AO, weathering, relief, compose (welcome_sign.make_post's order)."""
    dspecs = {n: EDGE_ROUND for n in table if n.startswith(EDGE_ROUND_PREFIXES)}
    dspecs.update({n: CORR for n in table if n.startswith("roof_")})
    keys = list(rects.values())

    def hook(base, nrm, msk, ctl, ao, mesh, A_):
        px = {m: cs2_bake._load_rgba(p, A, f"band {m}") for m, p in band_paths.items()}
        for r in keys:
            sl = cs2_compose._rect_slices(r, A)
            base[sl][..., 0:3] = K.srgb_to_linear(px["BaseColor"][sl][..., 0:3])
            nrm[sl][..., 0:3] = px["Normal"][sl][..., 0:3]
            msk[sl][..., 0] = px["MaskMap"][sl][..., 0]
            msk[sl][..., 3] = px["MaskMap"][sl][..., 3]
            ctl[sl][..., 0:3] = px["ControlMask"][sl][..., 0:3]
        h_paint = paint_weather(mesh, A, table, base, msk)
        part_map, ppm = cs2_compose.rasterise_face_fields(mesh, A)
        h = cs2_compose.detail_height(mesh, A, dspecs, part_table=table)
        h = h + h_paint
        uncovered = nrm[..., 0:3].max(axis=-1) < 1e-3
        opts = {"reserved": keys, "part_map": part_map, "noise_amp": 0.03, "gloss_var": 0.04,
                "contact": K.contact_map(mesh, A)}
        base, nrm, msk, ctl = cs2_compose.compose_maps(base, nrm, msk, ctl, None, h, ppm, opts)
        keep = np.zeros((A, A), bool)
        for r in keys:
            keep[cs2_compose._rect_slices(r, A)] = True
        n = nrm[..., 0:3] * 2.0 - 1.0
        n[..., 2] = np.maximum(n[..., 2], 0.05)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        fix = ~keep
        nrm[fix, 0:3] = (n * 0.5 + 0.5)[fix]
        nrm[uncovered & fix, 0:3] = (0.5, 0.5, 1.0)
        nrm[..., 3] = 1.0
        hook.density = K.shipped_density(obj, A)
        hook.done = True
        return base, nrm, msk, ctl
    hook.density = None
    hook.done = False
    return hook


# ======================================================================================
# Share list (identical members on one island)
# ======================================================================================

def share_groups(obj, table):
    me = obj.data
    pv = cs2_compose.part_indices(me)
    by_part = {}
    for p in me.polygons:
        by_part.setdefault(table[int(pv[p.index]) - 1], []).append(p.index)
    co = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    keyed = {}
    for n in sorted(by_part):
        k = share_key(n)
        if k:
            keyed.setdefault(k, []).append(n)
    groups, rep = [], {}

    def same(c, f):
        """Faces of f match c loop by loop up to one translation (the join may start a loop elsewhere)."""
        if len(c) != len(f):
            return False
        t0 = None
        for fc, fd in zip(c, f):
            pc, pd = me.polygons[fc], me.polygons[fd]
            if pc.loop_total != pd.loop_total:
                return False
            vc = co[[me.loops[i].vertex_index for i in range(pc.loop_start, pc.loop_start + pc.loop_total)]]
            vd = co[[me.loops[i].vertex_index for i in range(pd.loop_start, pd.loop_start + pd.loop_total)]]
            d = vd - vc
            t0 = d[0] if t0 is None else t0
            if np.abs(d - t0).max() > 2e-3:
                return False
        return True
    for k, members in sorted(keyed.items()):
        if len(members) < 2:
            continue
        c = by_part[members[0]]
        dups = [n for n in members[1:] if same(c, by_part[n])]
        if len(dups) != len(members) - 1:
            print(f"[depot] share {k}: {len(members) - 1 - len(dups)} of {len(members) - 1} members keep their own island")
        if dups:
            groups.append((c, [by_part[n] for n in dups]))
            rep[k] = 1 + len(dups)
    return groups, rep

# ======================================================================================
# Preview scene (nothing here reaches an FBX, a PNG map or the manifest)
# ======================================================================================

def W(x, y, z):
    """Authoring -> the oriented mesh's world (orient_for_net)."""
    return (y, -x, z)


def _ctx_mat(name, rgb, rough=0.8):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = tuple(rgb) + (1.0,)
    b.inputs["Roughness"].default_value = rough
    return m


def _box(name, lo, hi, mat):
    """An authoring-frame box in the render scene (corners converted to world)."""
    a, b = W(*lo), W(*hi)
    c = tuple(0.5 * (p + q) for p, q in zip(a, b))
    d = tuple(abs(p - q) for p, q in zip(a, b))
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=c)
    o = bpy.context.active_object
    o.name = name
    o.scale = d
    o.data.materials.append(mat)
    return o


def _cyl(name, at, r, h, mat, verts=16, r2=None):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r if r2 is None else r2, depth=h,
                                    location=W(at[0], at[1], at[2] + h / 2))
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return o


def _blob(name, at, r, mat, sub=2, squash=1.0):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub, radius=r, location=W(*at))
    o = bpy.context.active_object
    o.name = name
    o.scale = (1.0, 1.0, squash)
    o.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return o


def add_placeholders():
    """ph_: the vanilla sub-objects as plain matte forms. ctx_: road, ground, a 1.8 m person for scale."""
    out = []
    wood = _ctx_mat("ph_yellow", (0.62, 0.42, 0.10))
    iron = _ctx_mat("ph_iron", (0.03, 0.03, 0.03))
    gray = _ctx_mat("ph_gray", (0.22, 0.23, 0.24))
    rock = _ctx_mat("ph_rock", (0.30, 0.28, 0.25), 0.95)
    leaf = _ctx_mat("ph_leaf", (0.16, 0.28, 0.08), 0.9)
    bark = _ctx_mat("ph_bark", (0.20, 0.15, 0.10), 0.9)
    # bench against the back wall
    bx, by = XB + 0.30, -1.2
    out.append(_box("ph_bench_seat", (bx - 0.22, by - 0.95, PZ + 0.44), (bx + 0.22, by + 0.95, PZ + 0.49), wood))
    out.append(_box("ph_bench_back", (bx - 0.25, by - 0.95, PZ + 0.55), (bx - 0.21, by + 0.95, PZ + 0.86), wood))
    for s in (-0.9, 0.9):
        out.append(_box(f"ph_bench_end{s}", (bx - 0.26, by + s - 0.03, PZ), (bx + 0.24, by + s + 0.03, PZ + 0.88), iron))
    # trash bin
    out.append(_cyl("ph_trash", (-0.55, 4.62, PZ), 0.26, 1.0, gray))
    # lamp
    out.append(_cyl("ph_lamp_pole", (3.25, 6.3, 0.0), 0.05, 4.2, iron, verts=10))
    out.append(_box("ph_lamp_head", (2.85, 6.22, 4.15), (3.3, 6.38, 4.26), iron))
    # planter rocks and two young trees
    rng = random.Random(SEED + 11)
    for i in range(6):
        out.append(_blob(f"ph_rock{i}", (rng.uniform(-0.4, 2.4), rng.uniform(5.6, 7.05), PLANTER_BED_Z + 0.08), rng.uniform(0.12, 0.26), rock, squash=0.7))
    for i, (x, y, hgt) in enumerate(((0.35, 5.95, 3.4), (1.85, 6.65, 2.9))):
        out.append(_cyl(f"ph_trunk{i}", (x, y, PLANTER_BED_Z), 0.045, hgt * 0.72, bark, verts=8, r2=0.025))
        out.append(_blob(f"ph_crown{i}", (x, y, PLANTER_BED_Z + hgt * 0.72), hgt * 0.27, leaf, squash=1.25))
    return out


def add_context():
    out = []
    asphalt = _ctx_mat("ctx_asphalt", (0.055, 0.055, 0.06), 0.9)
    curb = _ctx_mat("ctx_curb", (0.42, 0.42, 0.40), 0.9)
    paint = _ctx_mat("ctx_paint", (0.75, 0.62, 0.14), 0.7)
    out.append(_box("ctx_road", (4.15, -220, 0.0), (19.0, 220, 0.02), asphalt))
    out.append(_box("ctx_curb", (3.95, -220, 0.0), (4.15, 220, 0.13), curb))
    for i in range(-40, 41):
        out.append(_box(f"ctx_dash{i}", (11.5, i * 6.0, 0.0), (11.65, i * 6.0 + 3.0, 0.027), paint))
    person = _ctx_mat("ctx_person", (0.30, 0.34, 0.42), 0.85)
    out.append(_cyl("ctx_person", (3.0, -2.5, PZ), 0.19, 1.45, person, verts=10))
    out.append(_blob("ctx_head", (3.0, -2.5, PZ + 1.6), 0.13, person))
    return out


def _glass_preview(rig, sub):
    mat = rig.track_data(bpy.data.materials, bpy.data.materials.new("PreviewGlass"))
    mat.use_nodes = True
    nt = mat.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    out_ = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    nt.nodes.remove(b)
    fr, gl, tr, mix = (nt.nodes.new("ShaderNodeFresnel"), nt.nodes.new("ShaderNodeBsdfGlossy"),
                       nt.nodes.new("ShaderNodeBsdfTransparent"), nt.nodes.new("ShaderNodeMixShader"))
    # 11 % head on: acrylic plus the dust that makes a panel read. A plane has no inside: Cycles inverts the IOR on the
    # back face, which would go totally internally reflective (a mirror, and no sun through it), so feed it 1 / 2 there.
    geo, ior = nt.nodes.new("ShaderNodeNewGeometry"), nt.nodes.new("ShaderNodeMath")
    ior.operation = "MULTIPLY_ADD"
    ior.inputs[1].default_value = -1.5
    ior.inputs[2].default_value = 2.0
    nt.links.new(geo.outputs["Backfacing"], ior.inputs[0])
    nt.links.new(ior.outputs[0], fr.inputs["IOR"])
    gl.inputs["Roughness"].default_value = 0.04
    tr.inputs[0].default_value = (0.80, 0.83, 0.80, 1.0)
    nt.links.new(fr.outputs[0], mix.inputs[0])
    nt.links.new(tr.outputs[0], mix.inputs[1])
    nt.links.new(gl.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out_.inputs[0])
    sub.data.materials.clear()
    sub.data.materials.append(mat)


# (name, camera, target, vertical fov deg, resolution): authoring frame. Eye-height and game-camera distances as the
# hut pilot's rig (1080p, 45 deg: screen px/m = 1,304 / d).
SHOTS = {
    "road_threequarter": ((10.5, 9.2, 2.6), (0.7, -0.3, 1.9), 42.0, (1600, 1000)),
    "road_tower": ((9.0, -13.2, 2.4), (0.9, -2.8, 2.5), 42.0, (1600, 1000)),
    "back_threequarter": ((-10.2, 8.4, 3.4), (0.4, -0.8, 2.0), 42.0, (1600, 1000)),
    "inside_back_wall": ((1.75, 3.35, 1.62), (-1.0, -0.65, 1.28), 62.0, (1600, 1000)),
    "roof_high": ((8.5, 6.0, 11.5), (0.6, -0.8, 2.0), 42.0, (1600, 1000)),
    "fit_post_foot": ((3.3, -0.2, 0.85), (2.0, -1.33, 0.25), 40.0, (1200, 800)),
    "fit_panel_corner": ((3.55, -0.1, 2.2), (2.1, -1.33, 2.3), 40.0, (1200, 800)),
    "fit_hooks": ((1.2, 2.7, 1.45), (-1.0, 2.7, 1.2), 42.0, (1200, 800)),
    "fit_screen": ((0.5, 3.9, 1.7), (0.5, 1.33, 2.4), 42.0, (1200, 800)),
}
# Far shots at 1920 x 1080, 45 deg: (distance m, elevation deg, azimuth from the road normal toward +Y deg)
FAR = {"far_d70": (70.0, 28.0, 38.0), "far_d110": (110.0, 30.0, 38.0), "far_d150": (150.0, 32.0, 38.0),
       "far_d110_otherside": (110.0, 30.0, -32.0)}
FAR_TARGET = (0.8, -1.0, 2.2)


def _srgb_crop_upscale(path, out, center_uv, box_px, scale):
    im = bpy.data.images.load(path)
    w, h = im.size
    a = np.empty(w * h * 4, np.float32)
    im.pixels.foreach_get(a)
    a = a.reshape(h, w, 4)
    cx, cy = int(center_uv[0] * w), int(center_uv[1] * h)
    bw, bh = box_px
    x0, y0 = max(0, cx - bw // 2), max(0, cy - bh // 2)
    crop = a[y0:y0 + bh, x0:x0 + bw]
    up = np.repeat(np.repeat(crop, scale, 0), scale, 1)
    o = bpy.data.images.new("crop", up.shape[1], up.shape[0], alpha=True)
    o.colorspace_settings.name = im.colorspace_settings.name
    o.pixels.foreach_set(np.ascontiguousarray(up).ravel())
    o.filepath_raw = out
    o.file_format = "PNG"
    o.save()
    bpy.data.images.remove(o)
    bpy.data.images.remove(im)


def render_previews(obj, glass, asset_dir, name, out_dir, samples, only=None):
    import cs2_preview
    from bpy_extras.object_utils import world_to_camera_view
    os.makedirs(out_dir, exist_ok=True)
    rig = cs2_preview._Rig(obj, samples)
    world, sun = cs2_preview._make_world(rig, 42.0, 4.0)
    mat = cs2_preview._pbr_material(rig, asset_dir, name, None)
    for n_ in mat.node_tree.nodes:
        if n_.type == "BSDF_PRINCIPLED":
            n_.inputs["Emission Strength"].default_value = 1.2
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    _glass_preview(rig, glass)
    glass.hide_render = False
    bpy.ops.mesh.primitive_plane_add(size=900.0, location=(0.0, 0.0, -0.01))
    ground = bpy.context.active_object
    ground.name = "ctx_ground"
    ground.data.materials.append(_ctx_mat("ctx_ground", (0.20, 0.205, 0.17), 0.95))
    extra = [ground] + add_placeholders() + add_context()
    for o in extra:
        o.hide_render = False
    sc = rig.scene
    cam = cs2_preview._make_camera(rig)
    cs2_preview._set_engine(sc, "CYCLES")
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.threads_mode = "FIXED"
    sc.render.threads = 12                  # a game may be running; BelowNormal is set by the caller
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = "None"
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGB"
    files = []

    def shoot(nm, cam_a, tgt_a, fov, res):
        cam.location = W(*cam_a)
        d = Vector(W(*tgt_a)) - Vector(W(*cam_a))
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cam.data.sensor_fit = "VERTICAL"
        cam.data.sensor_height = 24.0
        cam.data.lens = 12.0 / math.tan(math.radians(fov) / 2.0)
        sc.render.resolution_x, sc.render.resolution_y = res
        sc.render.resolution_percentage = 100
        p = os.path.join(out_dir, f"{name}_{nm}.png")
        sc.render.filepath = p
        t = time.perf_counter()
        bpy.ops.render.render(write_still=True)
        print(f"[depot] render {nm}: {time.perf_counter() - t:.0f} s -> {p}")
        files.append(p)
        return p

    sun_road = sun.matrix_world.copy()
    sky = next(n for n in world.node_tree.nodes if n.type == "TEX_SKY")
    rot_road = sky.sun_rotation
    people = [o for o in extra if o.name.startswith("ctx_person") or o.name.startswith("ctx_head")]
    for nm, (c, t, fov, res) in SHOTS.items():
        if only and nm not in only:
            continue
        back = nm.startswith("back")          # the sun swings to the camera's side for this one
        sun.matrix_world = (Matrix.Rotation(math.pi, 4, "Z") @ sun_road) if back else sun_road
        sky.sun_rotation = rot_road + (math.pi if back else 0.0)
        for o in people:                      # the scale figure stays out of the close-ups
            o.hide_render = nm.startswith(("fit_", "inside"))
        shoot(nm, c, t, fov, res)
    sun.matrix_world = sun_road
    sky.sun_rotation = rot_road
    for o in people:
        o.hide_render = False
    legib = {}
    for nm, (dist, elev, az) in FAR.items():
        if only and nm not in only:
            continue
        e, a = math.radians(elev), math.radians(az)
        c = (FAR_TARGET[0] + dist * math.cos(e) * math.cos(a), FAR_TARGET[1] + dist * math.cos(e) * math.sin(a), FAR_TARGET[2] + dist * math.sin(e))
        p = shoot(nm, c, FAR_TARGET, 45.0, (1920, 1080))
        # the honest legibility number + a 1:1 pixel crop x 4 of the tower sign (nearest neighbor, no smoothing)
        bpy.context.view_layer.update()
        mid = Vector(W(TOWER_X[1] + LED_T, 0.5 * (LED_Y[0] + LED_Y[1]), LED_Z[0] + 0.405))
        top = Vector(W(TOWER_X[1] + LED_T, 0.5 * (LED_Y[0] + LED_Y[1]), LED_Z[0] + 0.405 + TEXT_CAP_M))
        uv0 = world_to_camera_view(sc, cam, mid)
        uv1 = world_to_camera_view(sc, cam, top)
        px = math.hypot((uv1.x - uv0.x) * 1920, (uv1.y - uv0.y) * 1080)
        legib[nm] = round(px, 1)
        print(f"[depot] {nm}: cap height on screen {px:.1f} px")
        crop = os.path.join(out_dir, f"{name}_{nm}_sign4x.png")
        _srgb_crop_upscale(p, crop, (uv0.x, uv0.y), (160, 120), 4)
        files.append(crop)
    for o in extra:
        try:
            bpy.data.objects.remove(o, do_unlink=True)
        except ReferenceError:
            pass
    return files, legib


# ======================================================================================
# Run
# ======================================================================================

def parse_args(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="ski_bus_depot.py")
    ap.add_argument("--out", required=True, help="build folder (the scratchpad; never assets/import)")
    ap.add_argument("--name", default=NAME)
    ap.add_argument("--preview", default=None, metavar="DIR")
    ap.add_argument("--shots", default=None, help="comma list of shot names (default all)")
    ap.add_argument("--samples", type=int, default=48)
    ap.add_argument("--tex", type=int, default=TEX, choices=(1024, 2048))
    ap.add_argument("--ao-samples", type=int, default=AO_SAMPLES)
    ap.add_argument("--blockout", action="store_true", help="geometry and caps only: no bake, no FBX")
    ap.add_argument("--no-validate", action="store_true")
    return ap.parse_args(argv)


def run(args):
    if not bpy.app.background:
        raise RuntimeError("ski_bus_depot.py runs headless only (PIPELINE section 2)")
    out = os.path.abspath(args.out)
    if os.path.normcase(os.path.join("assets", "import")) in os.path.normcase(out):
        raise RuntimeError("previews only: --out must not be under assets/import")
    t0 = time.perf_counter()
    name = cs2_asset.validate_asset_name(args.name)
    asset_dir = os.path.join(out, name)
    os.makedirs(asset_dir, exist_ok=True)
    A = int(args.tex)
    welcome_sign.clear_scene()
    parts = build_parts()
    names = [o.name for o, _ in parts]
    if len(set(names)) != len(names) or any("." in n for n in names):
        raise RuntimeError(f"part names not unique: {names}")
    C.assert_lot_margin(parts, HALF_X, HALF_Y)
    keys = sorted({k for _o, k in parts})
    part_keys = {o.name: k for o, k in parts}
    axes = {o.name: tuple(o["prism_axis"]) for o, _k in parts if "prism_axis" in o}
    cs2_uv.set_density(parts, DENSITY)
    table = cs2_asset.tag_parts(parts)
    K.strip_uv_layers(parts)
    kit = [k for k in keys if k != BAND_KEY]
    proj = dict(K.projection_specs(kit))
    proj[BAND_KEY] = cs2_bake.MaterialSpec(albedo=(0.8, 0.8, 0.8), metres_per_tile=1.0)
    cs2_bake.material_sources(proj, parts)
    K.retile([(o, k) for o, k in parts if k != BAND_KEY], proj)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _m in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=WELD_M)
    bpy.ops.object.mode_set(mode="OBJECT")
    cs2_asset.store_part_table(obj, table)
    cs2_asset.shade_by_angle(obj, SHADE_ANGLE, weighted=True)
    set_board_uvs(obj, table)
    uv_prisms(obj, table, axes)
    rects = set_band_uvs(obj, table, A, part_keys)
    cs2_asset.orient_for_net(obj)
    glass = cs2_submesh.split_submesh(obj, table, ("glass_",), "Gls")
    base0 = K.check_base_mode(obj, table, "engine", GROUND)
    n0 = K.assert_budget(obj, BUDGET, 0)
    area = K.area_report(obj, table=table)
    result = dict(name=name, tris={0: n0}, glass_tris=cs2_asset.tri_count(glass), base={0: base0}, parts=len(table),
                  keys=keys, sub_objects_planned=[dict(s) for s in SUB_OBJECTS], tex=A, lot=list(LOT))
    print(f"[depot] LOD0 {n0} tris (cap {BUDGET.lod0_cap}), glass {result['glass_tris']}, kept {area['kept_m2']} m2, "
          f"predicted w=1 px/m {area['predicted_ppm_w1']}")
    if args.blockout:
        print(f"PASS ski_bus_depot: {name} BLOCKOUT LOD0 {n0}, engine base {base0['verdict']}")
        return result, obj
    # ---- bake --------------------------------------------------------------------------------
    work = os.path.join(out, "_work")
    band_paths, emis, band_rep = paint_band(A, work, name)
    result["band"] = band_rep
    groups, share_rep = share_groups(obj, table)
    result["share"] = share_rep
    base_ppm = area["predicted_ppm_w1"].get(A) or 0.48 * A / math.sqrt(max(area["sum_w2a"], 1.0))
    used = sorted(keys)
    specs, info = K.material_specs([k for k in used if k != BAND_KEY], base_ppm)
    specs = controlled_specs(specs, info, work)
    sources = {}
    for i, k in enumerate(used):
        if k == BAND_KEY:
            sources[i] = cs2_bake.SourceSet(base=band_paths["BaseColor"], normal=band_paths["Normal"], normal_convention="GL",
                                            mask=band_paths["MaskMap"], control=band_paths["ControlMask"], uv=None, slot=0,
                                            recolor_albedo=False)
        else:
            ss = cs2_bake._resolve_spec(cs2_bake._as_material_spec(specs[k], k), k)
            sources[i] = replace(ss, uv=cs2_bake.TILE_LAYER)
    hook = make_post(obj, band_paths, A, table, rects)
    world = bpy.context.scene.world or bpy.data.worlds.new("World")
    bpy.context.scene.world = world
    world.light_settings.distance = 1.0
    saved_pack = cs2_uv._pack_layout
    cs2_uv._pack_layout = depot_pack
    try:
        brep = cs2_bake.bake_unique(obj, asset_dir, name, A, sources, reserved=list(rects.values()),
                                    margin_px=welcome_sign.pack_margin(A), ao_samples=int(args.ao_samples),
                                    post_compose=hook, emissive=False, src_uv=cs2_bake.TILE_LAYER, share=groups)
    finally:
        cs2_uv._pack_layout = saved_pack
    if not hook.done:
        raise RuntimeError("post hook did not run")
    cs2_asset._write_png_raw(os.path.join(asset_dir, f"{name}_Emissive.png"), f"{name}Emissive", emis, A, False)
    result["bake"] = {k: brep[k] for k in ("A", "margin_px", "uncovered_texels", "seconds", "files", "ao")}
    result["density"] = hook.density
    result["materials"] = info
    cs2_asset.export_fbx(obj, os.path.join(asset_dir, f"{name}.fbx"))
    cs2_submesh.export_submeshes(glass, name, "Gls", asset_dir, lods=(1,))
    # ---- LODs ----------------------------------------------------------------------------------
    recipe_like = types.SimpleNamespace(budget=BUDGET, ground_prefixes=GROUND)
    (t1, t2), plan = K.plan_lod_targets(obj, table, recipe_like, delete_lists=(DELETE1, DELETE2), protect_prefixes=GROUND)
    lods, infos = K.lod_chain(obj, name, (t1, t2), (DELETE1, DELETE2), GROUND)
    result["lod_plan"] = plan
    for lvl, lod in enumerate(lods, start=1):
        result["base"][lvl] = K.check_base_mode(lod, table, "engine", GROUND)
        result["tris"][lvl] = cs2_asset.tri_count(lod)
        if lvl == 2:
            K.assert_budget(lod, BUDGET, 2)
        cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{lvl}.fbx"))
    if result["tris"][1] > 0.5 * n0:
        raise RuntimeError(f"LOD1 {result['tris'][1]} > 0.50 x LOD0 {n0}")
    cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    for lod in lods:
        me = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(me)
    # ---- previews ------------------------------------------------------------------------------
    if args.preview:
        only = set(args.shots.split(",")) if args.shots else None
        files, legib = render_previews(obj, glass, asset_dir, name, os.path.abspath(args.preview), int(args.samples), only)
        result["previews"] = files
        result["sign_cap_px"] = legib
        obj.data.materials.clear()
        glass.data.materials.clear()
    if not args.no_validate:
        obj.name, obj.data.name = name + "Src", name + "Src"
        findings, summary = cs2_validate.validate_asset_folder_ex(asset_dir, name, {
            "kind": "building", "lod0_cap": BUDGET.lod0_cap, "lod2_cap": BUDGET.lod2_cap, "hero": True,
            "expected_lods": (1, 2), "expected_submeshes": ("Gls",), "max_texture": A, "base_mode": "engine",
            "lot_m": (8 * LOT[0], 8 * LOT[1])})
        errs = [f for f in findings if f[0] == "ERROR"]
        warns = [f for f in findings if f[0] == "WARN"]
        result["validate"] = {"errors": [f[1:] for f in errs], "warnings": [f[1:] for f in warns][:20]}
        print(f"[depot] validate: {len(errs)} errors, {len(warns)} warnings")
        for f in errs + warns:
            print("   ", f)
    result["seconds"] = round(time.perf_counter() - t0, 1)
    with open(os.path.join(out, f"{name}.manifest.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(dict(generator="ski_bus_depot.py", prefab=PREFAB, args=vars(args), **result), fh, indent=1, default=str)
    dens = (result.get("density") or {}).get("median_w1")
    print(f"PASS ski_bus_depot: {name} LOD0 {n0}, chain {[result['tris'][i] for i in sorted(result['tris']) if i]}, "
          f"glass {result['glass_tris']}, tex {A}, w=1 median px/m {dens}, sign cap px {result.get('sign_cap_px')}")
    return result, obj


def main():
    try:
        run(parse_args())
    except Exception:
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    main()
