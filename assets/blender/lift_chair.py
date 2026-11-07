"""
Chairlift chair generator. One module, three styles, one carrier contract per style.

Asset round 2026-10 (ROUND-SPEC R1 amended, R5, P1/P1b). Build contract:
docs/research/art/assets-2026-10/design/dq-chair.md with dq-chair-skeptic.md applied (the skeptic wins where they
disagree). Build report: .../assets-2026-10/build/lift_chair.md.

Styles. Each item gives the style key, the default name, what the style is, the triangle counts (LOD0 / LOD1 / LOD2) and
the carrier.

- tr, default name SkiLiftChair6M. A fixed-grip TRIPLE chair, old school: the FG J-stem chair with Kagelok-shaped
  cushions and back pads. It has NO footrest and NO restraint (2026-10-11). It has a real FIXED GRIP: a jaw block, a long
  jaw saddle on the rope, a lateral spring housing, and two clamp plates on the hanger. The bench hangs 0.09 m lower, so
  the plank bottom closes the 2.400 contract. Tris 494 / ~220 / 62. Carrier tr (2.400).
- dq, default name SkiLiftChairDQ4M. A DETACHABLE QUAD traced from Kagelok chair 05. It has a DT-style grip: a soft-edged
  carriage / spring box with the friction comb in a tray on top, two running wheels beside the jaw at rope height on the
  inboard side, two coil springs, a jaw on the rope, and a lever + roller. It also has an outboard C-hanger, a hoop
  frame, and a 4 x 0.6 m bench. The restraint bar is DOWN, hinged on the SIDE FRAMES just under the backrest top (one
  bent U tube: arm - lap bar - arm). The lap bar is 0.20 over the cushion, ahead of the knees (rider photos). Two
  inverted-T footrests hang from the lap bar. The plate is on the clamp. C13 sweeps the bar DOWN -> UP past a
  seated-rider envelope and asserts that it moves. Tris 996 / ~350 / 98. Carrier dq (3.300).
- fg6l, default name SkiLiftChair6L. The SHIPPED round-L build, kept reproducible so the old numbers can be regenerated
  (never imported, R1 amended). It uses the same code path and makes byte-identical PNGs. Tris 454 / 220 / 70. Carrier
  tr.

Revision 1 (first live review, 2026-10-11, verdicts-2026-10-11.md items 1-2, build report .../build/lift_chair.md
"Revision 1"): tr lost the footrest (+ links) and the clamp studs and gained the fixed grip. C4's bottom owner became the
seat plank. dq got the DT grip. The restraint pivoted on the hoop top bar beside the hanger clamp. The footrest moved
under the seat front (bar modeled DOWN).

Revision 1b (polish, 2026-10-11, revision1-check-chairs.md item 2e): the dq running wheels moved from the rope plane
above the rope to beside the jaw on the inboard side. Their centers are near rope height (Kagelok D05 rollers,
dq-chair-skeptic claim 4), on stub axles buried in the jaw. The carriage got chamfered edges and a shallow tray on top
that holds the silver friction comb (ribs = DetailSpec seams). LOD1 still keeps box + jaw only.

Revision 2 (second look, verdicts-2026-10-11 item 2', build report "Revision 2"): the restraint was rebuilt around a
working hinge. Two bosses on the inboard faces of the hoop's side legs at shoulder height carry ONE bent tube (arm -> lap
bar -> arm, ends buried in the bosses). The lap bar sat 0.35 above the cushion (was 0.40, "a bit high") and 0.40 in front
of the pads. Two inverted-T footrests (stem + short foot bar) hang from it between riders 1-2 / 3-4. The top-bar lugs,
the separate front rail and the footbar were removed. Revision 2 added contract C13 (check_bar_mechanics). C13 sweeps the
bar assembly about the measured hinge axis DOWN -> UP in 12 steps (x4 sub-poses) with a BVH overlap test against every
other part. UP must park over the backrest top and a seated head (not in front of the riders' faces). --preview adds
<name>_pbr_barup_* (the UP pose).

Revision 3 (2026-10-11 ~14:10, verdict 2'': "still ... too high", three photos with riders, build report "Revision 3"):
the bar was placed from the RIDERS. Measured on the frontal AL photo (PHOTO_* constants):
- the lap bar sits 0.20 above the cushion (was 0.35)
- the pivot sits 0.47 above the cushion, just under the backrest top (was 0.60)
- the lap bar sits ahead of the knees (x 0.48)
- the T stems lean ~10 deg forward

C13 now also does these things:
- It asserts the DOWN window (LAP_WINDOW_M, ahead of the knee line).
- It sweeps a seated-rider envelope (RIDER: head, torso, thighs per seat) as an obstacle.
- It tests that each boss end is INSIDE the hoop tube (not just near it).
- It parks UP over the head top and over the seat (the clamp block is the stop).

--preview adds <name>_pbr_riders_{profile,three_quarter} (bar DOWN) and _pbr_riders_barup_profile with a boxy rider
stand-in that is never exported.

Nitpick A2 (2026-10-13, orchestrator ruling "Nitpick A follow-ups" (1) on build/nitpick-a-stations.md section 2.4, build
report build/nitpick-a2-hanger-gondola.md): the dq grip side was MIRRORED across the travel plane (DQ_OUTBOARD = -1). The
hanger, carriage + comb, springs, lever + roller, running wheels + axles, clamp block and plate moved from authoring +Y
to -Y as an exact mirror (same triangles). The hoop, bench, bar and footrests are symmetric and stay untouched. Every
chair station is built round the up lane on the RIGHT looking uphill (the stations' spin). On that lane, a chair that
faces its travel has the outboard side on its RIGHT = authoring -Y. So the hanger now runs OUTBOARD on both lanes, and
the grip clears the towers' rocker mains (tests/test_dq_family_seams.py S9). DQ_OUTBOARD = +1 rebuilds the approved
revision 3.

Run headless. In an interactive session main() raises (ROUND-SPEC R10):
    blender --background --factory-startup --python lift_chair.py -- \\
        --style tr|dq|fg6l  --out assets/import/chairs/M
        [--letter M] [--name <Base><Letter>]   default name = STYLES[style].base + letter
        [--finish] [--bake v2|legacy]          v2 = cs2_bake.bake_unique (default; fg6l: legacy)
        [--lods] [--lod2 proxy|decimate]       proxy = authored LOD2 (default; fg6l: decimate)
        [--preview assets/preview/chairs] [--ab <prev asset dir>]
        [--review [<dir>]]                     cs2_review.write_pack -> assets/preview/review/<Name>.blend
        [--verify] [--plate-number 27] [--hanger-offset 0.30 (dq)] [--carriers catalog/carriers.json]
        [--seats N]   accepted only when N == the style's seats
        [--basecolor/--metallic/--smoothness]  kept for old command lines, ignored

CARRIER CONTRACT (catalog/carriers.json, the table the C# catalog mirrors, R5):

- hang_m = grip top -> lowest point. The code asserts it on the ACTUAL vertices of EVERY level (LOD0, LOD1, LOD2 and the
  FBX re-import), with the lowest point at z = 0 (base pivot: only geometry at y >= 0 renders in game). tr 2.400, dq
  3.300.
- grip_top_above_rope_m = how far the mesh top sits above the haul-rope center. tr 0.080 (grip box +-0.08 about the
  rope), dq 0.220 (Kagelok D05 top 12.187 - rope 11.560 u at 0.3505 m/u). ChairPin pins bounds.max.y on the lane today,
  so a dq chair rides 0.22 m low until the Phase B catalog field lands (dq-chair.md section 9 Q1).
- The code asserts every geometry contract C1-C12 of dq-chair.md section 8 (check_contracts / check_atlas_stage /
  check_finished_maps / check_silhouettes). A wrong build raises and never ships. Budgets and the per-part triangle tally
  (C8/C9) are exact.

Axis convention: authoring +X = travel/facing (riders face +X), +Y = lateral (the riders' LEFT), +Z = up. On export,
orient_for_net() turns it into CS2's X-lateral / Y-up / Z-forward (FBX raw = authoring (y, z, x)). dq is authored
post-pivot (footbar bottom z = 0). tr / fg6l are authored with the origin at the grip (all geometry at negative Z), and
the assembly re-pivots them.

OUTBOARD side (nitpick A2, 2026-10-13): authoring -Y, the riders' RIGHT (DQ_OUTBOARD). The old text here said "+Y =
OUTBOARD on both lanes (the -x NetSubObjects row is rotated 180 deg)". That held only if both of these were true: the
game kept FBX x (probe X2 branch P), AND the chairs ran up the lane the stations are NOT built round.

The mover yaws every chair to its travel direction (the static NetSubObjects rotations do not survive it). It keeps chain
A = the lane on the RIGHT looking uphill (StationAlign.UpLaneIsSecond, a WORLD rule). In the Blender picture, every chair
station is built round that lane (bases load +Y, tops unload -Y: nitpick A, contracts O1 / M20).
- Branch N (game x = -authoring y, the picture-preserving map): the rule holds as written, and a chair's right is
  outboard.
- Branch P (game x = +authoring y): the game shows every mesh mirrored, and S1b-P flips the rule's sign so the chairs
  still go up the stations' lane. The mirrored chair's outboard is again the side authored as -Y.

So -Y is outboard in BOTH branches once the stations are right. The tr chair is centered. Only its grip spring housing
reaches +Y, clear of the FG trees either way (tests/test_fg_family_seams.py S9). It keeps its frame.

Line facts this module prints but does not own: tr NetSubObjects (+-1.80, 7.15, AnchorTop, spacing 15 per R2). dq row
(+-2.6, 8.25 = axle 8.0 + 0.22 + 0.03, spacing 30 as the spawn floor). The carrier COUNT comes from code (RS finding 9).
"""

import argparse
import collections
import json
import math
import os
import shutil
import sys
import tempfile
from dataclasses import dataclass, field

import bpy
import bmesh
import mathutils
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import carrier_split as CS  # noqa: E402
import cs2_asset  # noqa: E402
import cs2_kit    # noqa: E402


HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
CARRIERS_PATH = os.path.join(REPO_ROOT, "catalog", "carriers.json")
CARRIER_FIELDS = ("seats", "hang_m", "grip_top_above_rope_m", "lane_x_m", "mast_r_m")

DEFAULT_NAME = "SkiLiftChair"

# Atlas quadrants (cs2_asset.lift_atlas_quads layout, shared with towers and stations)
QUAD_GALV, QUAD_PAINT = 1, 3
# Bench vinyl: the chair does not use the glass quad. So chair_atlas_post repaints quad 2 as light-neutral vinyl on
# recolor slot 3 (ControlMask B). NOTE for lift_terminal: its atlas has no such repaint. Bench parts built there through
# build_chair_parts would wear the glass quad. This round deletes the static terminal chairs anyway.
QUAD_BENCH = 2


def load_carriers(path=CARRIERS_PATH):
    """
    The shared per-carrier contract table (catalog/carriers.json, dq-chair.md section 2).
    Returns {carrier key: row dict}. Raises on a missing file, a schema other than 1, or a
    chair row without the fields this generator asserts.
    """
    with open(path, "r", encoding="utf-8") as fh:
        data = json.load(fh)
    if data.get("schema") != 1:
        raise RuntimeError(f"{path}: schema {data.get('schema')!r}, expected 1")
    rows = data.get("carriers") or {}
    for key in ("tr", "dq"):
        row = rows.get(key)
        if row is None:
            raise RuntimeError(f"{path}: carrier row {key!r} missing")
        missing = [f for f in CARRIER_FIELDS if not isinstance(row.get(f), (int, float))]
        if missing:
            raise RuntimeError(f"{path}: carrier {key!r} lacks numeric {missing}")
    return rows


CARRIERS = load_carriers()

# -- the shipped 6L constants (fg6l, tr builds on the same frame) -------------------------
HEIGHT_CONTRACT = float(CARRIERS["tr"]["hang_m"])   # grip top -> lowest point (2.400)
GRIP_TOP_Z = 0.08              # authored: grip box top above the grip origin (= the rope)
LOWEST_Z = -2.32               # authored: fg6l footrest bar bottom = tr seat plank bottom
TRI_BUDGET = 500               # LOD0 cap (art-pipeline-v2 section 4)
if abs(GRIP_TOP_Z - float(CARRIERS["tr"]["grip_top_above_rope_m"])) > 1e-9 or \
        abs((GRIP_TOP_Z - LOWEST_Z) - HEIGHT_CONTRACT) > 1e-9:
    raise RuntimeError("catalog/carriers.json row 'tr' disagrees with the authored FG frame "
                       f"(grip top {GRIP_TOP_Z}, lowest {LOWEST_Z})")
# tr (Revision 1): there is no footrest, so the seat plank's bottom face is the lowest point. The bench hangs 0.09 m
# lower than 6L's (rope -> plank center 2.29 instead of 2.20). So the plank bottom (center - 0.03) lands on LOWEST_Z and
# the 2.400 contract still closes.
TR_HANGER_LEN = 2.29
TR_SEAT_THICK = 0.06
if abs(-(TR_HANGER_LEN + TR_SEAT_THICK / 2.0) - LOWEST_Z) > 1e-9:
    raise RuntimeError("TR_HANGER_LEN does not put the tr plank bottom on LOWEST_Z")
STEM_TOP_DZ = -0.055           # J-stem top point below the rope (fg6l: inside the grip box)
TR_STEM_TOP_DZ = -0.020        # tr: higher, so the open stem end sits inside the jaw block

LOD_TARGETS = (220, 70)
LOD1_DELETE = ("bolt_", "divider_")
LOD2_DELETE = LOD1_DELETE + ("armrest_", "armpost_", "footlink_", "backbar_", "plate_")

ATLAS_QUAD_PX = 256            # private source atlas = 512
FINISH_ATLAS = 512
AO_SAMPLES = 24
# v2 unique pack margin at 512 (texels). The bake extends islands by the same amount. The default of cs2_uv, max(16, A //
# 64), is sized for >= 1024 sets. It left the chairs at 14 / 33 px/m (96 % gutter). 3 px = the towers' 6 px @ 1024
# (lift_tower PACK_MARGIN_V2) at the same mip fraction. Measured medians: tr 117, dq 75 px/m (build report).
PACK_MARGIN_PX = 3
SHADE_ANGLE = 66.0
WELD_M = 0.0005

BENCH_SMOOTH = 0.25
BENCH_PREVIEW_RGB = (0.14, 0.18, 0.28)   # slot-3 preview tint only (prefab VariationSet owns it)
PLATE_LIGHT = (0.90, 0.90, 0.86)
PLATE_DARK = (0.08, 0.08, 0.09)
PLATE_CELL_M = 0.0105                    # glyph cell (5 x 7 font) in meters on the plate

# After orient_for_net the travel axis is Blender -Y. The profile view looks along X and shows the J-stem edge-on. The
# `front_far` view of dq-chair.md is dropped (skeptic D12). cs2_preview frames every view automatically, and the `lod`
# mode already renders at d_k.
CHAIR_VIEWS = (
    ("three_quarter", (1.0, -1.1, 0.35)),
    ("profile",       (1.0, 0.05, 0.10)),
    ("front",         (0.05, -1.0, 0.12)),
    ("back",          (0.7, 1.0, 0.30)),      # number plate + stem clamp
)

# Texel-density weights per part prefix (1 = the equalized base density). The legacy quadrant layout gives every part the
# same UV area, whatever its size. The finish packer scales all islands by ONE factor. So the spread would ship (measured
# before this: stem 60 px/m, clamp studs 2765). equalise_quadrant_density shrinks islands inside their quad to
# area3d-proportional sizes times these weights before the join.
DENSITY_WEIGHTS = {
    "plate_face": 2.5,     # the painted digits
    "plate_rim": 0.8,
    "grip": 1.4,           # bolt-head relief
    "clampblock": 1.2,
    "cushion_": 1.3,       # seam relief + scoop shading
    "backrest": 1.2,
    "bolt_": 0.6,
    "footlink_": 0.8,
    "armpost_": 0.8,
}

_DIGITS = {
    "0": ("01110", "10001", "10011", "10101", "11001", "10001", "01110"),
    "1": ("00100", "01100", "00100", "00100", "00100", "00100", "01110"),
    "2": ("01110", "10001", "00001", "00010", "00100", "01000", "11111"),
    "3": ("11111", "00010", "00100", "00010", "00001", "10001", "01110"),
    "4": ("00010", "00110", "01010", "10010", "11111", "00010", "00010"),
    "5": ("11111", "10000", "11110", "00001", "00001", "10001", "01110"),
    "6": ("00110", "01000", "10000", "11110", "10001", "10001", "01110"),
    "7": ("11111", "00001", "00010", "00100", "01000", "01000", "01000"),
    "8": ("01110", "10001", "10001", "01110", "10001", "10001", "01110"),
    "9": ("01110", "10001", "10001", "01111", "00001", "00010", "01100"),
}

# --------------------------------------------------------------------------------------
# Kagelok trace (dq). World units (u) of "Chair Lift" by Kagelok (CC BY 4.0, ROUND-SPEC section 8), chair 05 / grip D05 /
# rope mesh BezierCurve after unhide + frame 1. The numbers are as CORRECTED by dq-chair-skeptic.md section 1 (rope x
# -7.633 not -7.556, pads 7.83 deg / 0.453 thick, side frames +-3.232). tests/test_chair_styles.py re-derives the
# bbox-derived ones from the .blend (SKIP when the gitignored original is absent).
# --------------------------------------------------------------------------------------
KAGELOK_U = {
    "rope_x": -7.633, "rope_z": 11.560,        # rope center at the grip, y -24.31
    "chair_x": -7.000,                         # hoop bbox center (0.633 u inboard of the rope)
    "grip_top_z": 12.187, "grip_bottom_z": 11.430,
    "chair_lowest_z": 3.096,                   # hoop bottom
    "cushion_top_z": 3.912, "cushion_w": 1.436, "cushion_d": 2.021, "cushion_pitch": 1.600,
    "pad_top_z": 5.472, "pad_tilt_deg": 7.83, "pad_t": 0.453,
    "hoop_outer_x": 3.357, "hoop_r": 0.125, "hoop_top_z": 7.190,
    "armrest_x": 3.248,
    "plate": (0.907, 0.090, 0.908),            # clamp plates (x, y, z)
    "crossbar": (6.722, 0.461, 0.248),         # rear crossbar (x, y, z), not built (D9, [D])
    "roller_inboard": 0.363,                   # rollers INBOARD of the rope (skeptic claim 4)
}
S_VERT = 0.3505            # m/u vertical + grip: seat top 2.900 below the grip top
W_LAT = 0.375              # m/u lateral: cushion pitch 1.600 u -> 0.600 m
D_TRAV = 0.2573            # m/u travel: cushion depth 2.021 u -> 0.520 m
SIDE_Y = round((KAGELOK_U["hoop_outer_x"] - KAGELOK_U["hoop_r"]) * W_LAT, 3)   # 1.212
PAD_TILT_DEG = round(KAGELOK_U["pad_tilt_deg"], 1)                            # 7.8

# dq hanger (outboard C): --hanger-offset o puts H2.y = o and H1.y = o - 0.065 ([D] 0.30). Relative to the rope, the
# offset is a design pick (skeptic D6).
HANGER_OFFSET = 0.30
HANGER_R = 0.045
# Nitpick A2 (2026-10-13): the authoring y sign of the dq chair's OUTBOARD side (module docstring, "OUTBOARD side"). -1 =
# the riders' right. The code mirrors the grip-side group below across y = 0 after it builds the group
# (mirror_grip_side). The mirror is exact for each part, with the same triangles. jaw_rope is centered on the rope and
# needs no mirror. +1 = the approved revision-3 build, byte for byte. This module writes every side-dependent number
# OUTBOARD-positive and maps it through this sign (DQ_GRIP "y = outboard", dq_datums' H, C7, the C10 hanger window, the
# LOD2 proxy grip).
DQ_OUTBOARD = -1.0
DQ_GRIP_SIDE_PARTS = ("gripbody", "comb", "lever_fin", "lever_roller", "spring_", "wheel_",
                      "axle_", "tube_hanger", "clampblock", "plate_face")
# Tube radii (skeptic D3, "4-seg only if r < 0.02", plan 3.1):
DQ_R = {"hanger": HANGER_R, "hoop": 0.044, "armrest": 0.020, "armpost": 0.018,
        "barhinge": 0.032, "barlink": 0.024, "footstem": 0.016, "footbar": 0.022}

# dq restraint. Revision 2 (second look, verdicts-2026-10-11 item 2': "bar down is good - but the safety bar is a bit
# high", "2 upside down T-s coming from the safety bar", "think about the mechanics ... right now it would be stuck in
# place", "where the bar is coming from is all wrong") rebuilt it around a working hinge. It has these parts:
# - bosses on the side frames
# - ONE bent tube (arm -> lap bar -> arm) whose ends are buried in the bosses
# - two inverted-T footrests hung from the lap bar
#
# Contract C13 (check_bar_mechanics) sweeps it DOWN -> UP. Photos: detachable quad 1 (bar UP: the lap bar just under the
# top bar, each arm running down INBOARD of the hoop leg to a pivot at the backrest's outer end), lpoa-04/-08.
#
# Revision 3 (2026-10-11 ~14:10, verdict 2'': "quad chair safety bar still isn't quite right, it's too high", with three
# photos of chairs WITH RIDERS in detachable quad/) placed the bar from the riders, not from the frame:
# - AL_New_Generation_2018_059-scaled-1.jpg (2560 x 1707, near-level frontal shot of a Poma six-pack, bar DOWN, two
#   riders). Every number below is measured on it. The scale comes from the back pads (6 x 300 px, a rider's seat is
#   0.45-0.50 m) and the helmets (136 px = 0.21 m): 1.50-1.67 mm/px, central 1.53. Heights are from the cushion top's
#   front edge (y 1046 px, the sky gap under the pads). The camera sits near cushion height, so the frontal distances are
#   close to true heights (they differ by a few cm for parts 0.2 m nearer the camera).
# - 2.17.23-lenawee-highrez-33-ian-zinner-1024x683.jpg (six-pack from below, bar DOWN) and
#   Chairlift_in_Whistler_BC-small_1.webp (Doppelmayr-style quad, three-quarter from below): the lap bar crosses the
#   riders' laps just above and in front of the knees, with their hands resting on it. The photos were shot from below.
#   So they bound the height from above and give no number of their own.
PHOTO_MM_PER_PX = (1.50, 1.53, 1.67)   # AL scale: low / central / high
PHOTO_AL_PX = {                        # AL pixel rows (original frame), measured by column profiles
    "cushion": 1046, "lap_bar": 918, "pivot": 740, "pad_top": 658, "foot_top": 1378,
    "top_bar": 256, "helmet_tops": (512, 550), "t_bar_len": 318, "seat_pitch": 300,
}
PHOTO_LAP_ABOVE_CUSHION_M = 0.20       # AL 128 px: 0.19-0.21 (tube 28 px = 0.043 across)
PHOTO_PIVOT_ABOVE_CUSHION_M = 0.47     # AL 306 px: 0.46-0.51, 0.12 under the pad top (388 px)
PHOTO_FOOT_BELOW_CUSHION_M = 0.51      # AL 332 px: 0.50-0.55 to the footrest top (boots on skis ~0.46)
PHOTO_HELMET_TOP_M = (0.76, 0.82)      # AL riders' helmet tops above the cushion (slouched)
PHOTO_TOP_BAR_ABOVE_CUSHION_M = 1.21   # AL 790 px: 1.19-1.32 (this traced hoop: 1.10)
PHOTO_T_BAR_M = 0.49                   # AL 318 px: the T's foot bar (this build: 0.45)
LAP_WINDOW_M = (0.15, 0.25)            # C13: lap bar DOWN center above the cushion (brief + AL)

# Seated rider (C13's obstacle envelope + the preview stand-in, NEVER exported). [D] adult medians (ANSUR-like,
# unverified), checked against the AL riders:
# - head top 0.90 above the cushion (AL helmets 0.76-0.82, so the envelope is generous)
# - face / chest line 0.35 in front of the pads' front plane
# - knee front 0.62 in front of it at cushion height (buttock-knee ~0.60 + clothing)
# - thigh top 0.15 above the cushion
#
# Riders sit centered on the four cushions. Laterally the envelope stops `side_gap` inside the arm planes (by
# construction, the arms run in the side planes beside the outer riders).
DQ_SEAT_TOP = 0.400
SEATED_HEAD_M = 0.90
SEATED_FACE_M = 0.35
RIDER = {"head": SEATED_HEAD_M, "face": SEATED_FACE_M, "knee": 0.62, "thigh": 0.15,
         "half_w": 0.24, "thigh_half_w": 0.20, "side_gap": 0.020,
         "seats_y": (-0.90, -0.30, 0.30, 0.90)}

# - hinge: a boss on the INBOARD face of each hoop leg, PHOTO_PIVOT_ABOVE_CUSHION_M over the cushion (0.088 under the
#   backrest top 0.958, AL: 0.12 under its pad top). Its axis runs laterally, and its x follows the leg line (x -0.096).
#   Revision 2 had it at 0.60.
# - arms + lap bar: ONE bent tube (U), both ends buried in the bosses. The arms run straight forward-down from the hinge
#   over the armrests (0.64 m to the lap bar axis), in the planes |y| = arm_y (inboard of the legs, which a full-width
#   bar could never swing past). The corners have fillets, and the lap bar runs across. Lap bar DOWN 0.20 above the
#   cushion (PHOTO_LAP_ABOVE_CUSHION_M, Revision 2: 0.35, "too high") and AHEAD of the knees: its back face is 0.013 in
#   front of the RIDER knee line (center 0.157 ahead of the cushion front, not measurable on the frontal photo, [D] from
#   the knee).
# - footrests: two inverted T's (stem + short foot bar) under riders 1-2 / 3-4 (AL: one T per rider pair, stems upright
#   in the front view). The stems lean ~10 deg forward in profile [D], so the boots sit ahead of the knees. The foot bars
#   close the 3.300 contract (C4). Their top is 0.356 below the cushion (AL: 0.51 to the footrest top). A lower position
#   would raise the seat over the lowest point and move the station's seat_top 0.400 datum (lift_terminal_detach
#   CARRIER_FALLBACK), so it stays (build report R2.7 item 2).
# - swing: DOWN -> UP is a rotation of swing_deg about the hinge axis. Under this traced hoop (top bar 1.10 above the
#   cushion, AL 1.21), a lap bar ahead of the knees cannot pass under the hanger clamp. So UP parks it just in front of
#   the clamp at the top bar's height, 1.04 above the cushion (over every seated head of the envelope, 0.10 ahead of the
#   face line). The clamp block is the mechanical stop 4 deg further. C13 asserts this and sweeps the seated-rider
#   envelope too.
DQ_BAR = {
    "hinge_z": round(DQ_SEAT_TOP + PHOTO_PIVOT_ABOVE_CUSHION_M, 3),   # 0.870, x on the leg line
    "arm_y": 1.120,                    # arm plane |y| (leg inner face 1.168, armrest 1.192)
    "boss_in": 0.035,                  # boss runs from SIDE_Y (buried) to arm_y - boss_in
    "down": (0.480, round(DQ_SEAT_TOP + PHOTO_LAP_ABOVE_CUSHION_M, 3)),  # lap bar DOWN (x, z)
    "corner": 0.100,                   # arm -> lap bar corner fillet
    "foot": (0.580, 0.022),            # foot bar center (x, z), z = r -> bottom 0 (C4)
    "foot_y": 0.600,                   # T center |y| (between riders 1-2 / 3-4)
    "foot_half": 0.225,                # foot bar half-length (AL T 0.49, lpoa-04 ~0.4)
    "swing_deg": 95.0,                 # DOWN -> UP about the hinge (C13, the clamp stops it at 99)
}

# dq detachable grip, Revision 1 ("the grip is not quite right"). The reference is the photo detachable quad/close up of
# grip.jpg, a Doppelmayr DT grip: a carriage with the friction plate, two coil-spring stacks, the jaw lever with its
# roller, and running wheels. The photo detachable quad 1.jpg shows the grip on the line. Units are meters. Heights are
# relative to the rope center unless noted. y = outboard. The carriage keeps the mesh top (C2) and holds the rope point
# (C3). The jaw rides on the rope (C3: bottom rope + 0.006). Every other part stays below the top.
#
# Revision 1b (polish, revision1-check-chairs 2e): the carriage's edges along travel and lateral are chamfered ("soft").
# The vertical edges stay square, so the lever's buried face stays buried. The top face sinks into a shallow tray whose
# outer edge is still the mesh top. The friction comb (the photo's silver ribbed plate) sits in the tray just below that
# edge. The running wheels sit BESIDE the jaw on the INBOARD side, with their centers at the jaw's mid-height (Kagelok
# D05 rollers: rope height, 0.363 u inboard of the rope, dq-chair-skeptic claim 4). Each wheel sits on a stub axle buried
# 1 cm in the jaw. All numbers are [D] except the traced wheel lateral.
DQ_GRIP = {
    "body": (0.300, 0.140, 0.250),     # carriage / spring box (x, y, z), top = hang_m
    "body_y0": -0.030,                 # its inboard face (covers the rope's inboard side)
    "soft": 0.015,                     # chamfer on the carriage's x- and y-axis edges
    "tray": (0.008, 0.008),            # top tray: inset, depth (45 deg walls, rim = hang_m)
    "comb": (0.002, 0.0015),           # friction comb: margin to the tray floor edge, top below the rim
    "jaw": (0.580, 0.080, 0.045),      # jaw bar on the rope (x, y, z), 1 cm proud inboard
    "jaw_dz": 0.006,                   # jaw bottom above the rope center
    "lever": (0.240, 0.020, 0.045),    # lever arm on the inboard face (x, y, z)
    "lever_c": (-0.030, 0.085, 20.0),  # lever center x, z (above the rope), tilt deg (aft up)
    "roller": (0.032, 0.030, -0.140, 0.125),   # roller r, width, center x, z: the lever's aft end
    "spring": (0.032, 0.170, 0.085),   # coil cylinders r, height, +-x, on the outboard face
    "wheel": (0.080, 0.050, 0.200),    # running wheels r, width, +-x (center z = jaw mid-height)
    "wheel_y": -round(KAGELOK_U["roller_inboard"] * S_VERT, 3),   # -0.127: inboard, traced
    "axle_r": 0.016,                   # stub axle (6-seg, uncapped, both ends buried: jaw / wheel)
}
# Revision 1c (revision1b-check-dq-grip "the softened carriage does not shade the way the build report says"): on the
# chamfered carriage, only the 4 vertical edges came out sharp (90 deg). One sharp edge cannot split a corner fan (face
# -> 45 deg chamfer -> 60 deg chamfer corner -> 45 deg chamfer -> face). So the weighted normals bent the sides 25 deg
# and the ends 65 deg, and the box shaded like a rounded can.
#
# Now the code marks every edge of a FLAT part's big faces sharp before shading. A big face has an axis-aligned normal:
# the 4 sides, the bottom, and the tray floor. So those faces shade flat (corner normals = face normal, C14 <
# FLAT_FACE_MAX_DEG, measured again on the re-imported FBX by --verify). The chamfers and tray walls still smooth into
# each other at the corners.
FLAT_PARTS = {"dq": ("gripbody",)}     # style key -> part names (tr / fg6l: none, unchanged)
FLAT_FACE_MAX_DEG = 2.0
FLAT_FACE_AXIS = 0.999                 # |normal . axis| above this = a big (axis-aligned) face
# Authored LOD2 proxy radii (skeptic D11: >= the LOD0 radius). A 3-gon of circumradius R reads about as wide as a circle
# of 0.8 R, so R = 1.25 r.
PROXY_R = {"hanger": 0.056, "hoop": 0.055, "restraint": 0.030, "footstem": 0.025,
           "footbar": 0.031, "stem": 0.056}


# --------------------------------------------------------------------------------------
# Styles
# --------------------------------------------------------------------------------------

@dataclass(frozen=True)
class Style:
    key: str
    base: str
    carrier: str
    letter: str
    seats: int
    lod0_cap: int
    lod1_target: int
    lod2_cap: int
    lod1_delete: tuple
    lod2_delete: tuple            # only --lod2 decimate
    lod2_decimate_target: int
    bake: str                     # default --bake
    lod2: str                     # default --lod2
    grip_part: str                # the part that owns the mesh top (C2/C3), exact name
    tint: dict                    # preview / review slot tints. The prefab VariationSet owns colors
    bottom_part: str = "footbar"  # part-name PREFIX that owns the lowest vertices (C4)
    density: dict = field(default_factory=dict)
    part_tris: tuple = ()         # ((name, tris), ...) - the contract tally, asserted exactly (C9)
    proxy_tris: tuple = ()        # ((name, tris), ...) - the authored LOD2 tally
    ab: tuple = ()                # previous generation for the ab preview (import-tree path parts)

    @property
    def lod0_tris(self):
        return sum(t for _n, t in self.part_tris)

    @property
    def lod2_tris(self):
        return sum(t for _n, t in self.proxy_tris)

    @property
    def parts(self):
        return tuple(n for n, _t in self.part_tris)


# Preview / review slot tints. Slot 1 left out = the default of cs2_preview / cs2_review (cs2_asset.STEEL_DEFAULT_RGB,
# p0a fix 19). The prefab VariationSet owns the shipped look.
GALV_PREVIEW_RGB = (0.62, 0.64, 0.66)     # dq default frame [U] (photos lpoa-04, img_6201)
GREY_BENCH_RGB = (0.25, 0.26, 0.29)       # dq default bench [U] (photo lpoa-08)

_TR_TALLY = (
    # Revision 1: the 6L grip box became a fixed grip (jaw block = the top part, jaw saddle, spring housing, two hanger
    # clamp plates). Revision 1 removed the footbar + footlinks (old-school chair, no footrest, no restraint) and the two
    # 1 cm clamp studs (for the 500 cap). The clampblock's map:bolts relief still paints its bolt heads.
    ("grip", 12), ("gripjaw", 20), ("gripspring", 18), ("gripplate_0", 12),
    ("gripplate_1", 12),
    ("tube_stem", 128), ("clampblock", 12),
    ("backrail", 32), ("backbar_o0", 12), ("backbar_o1", 12),
    ("backrest", 12), ("plate_rim", 8), ("plate_face", 2), ("seat", 12),
    ("cushion_0", 26), ("cushion_1", 26), ("cushion_2", 26),
    ("pad_0", 20), ("pad_1", 20), ("pad_2", 20),
    ("armrest_0", 18), ("armpost_0", 8), ("armrest_1", 18), ("armpost_1", 8),
)
_DQ_TALLY = (
    # Revision 1 grip (photo "close up of grip"): carriage / spring box, jaw on the rope, lever arm (+Y face buried) +
    # roller, two coil-spring cylinders, two running wheels. Revision 1b added a carriage chamfered on 8 edges + a top
    # tray (all quads, 36), the friction comb in the tray (box, bottom buried, 10), and the wheels' stub axles (6-seg,
    # uncapped, 12 each).
    ("gripbody", 36), ("comb", 10), ("jaw_rope", 12), ("lever_fin", 10), ("lever_roller", 24),
    ("spring_0", 24), ("spring_1", 24), ("wheel_0", 32), ("wheel_1", 32),
    ("axle_0", 12), ("axle_1", 12),
    ("tube_hanger", 144), ("clampblock", 12),
    ("plate_face", 2), ("tube_hoop", 168), ("armrest_0", 18), ("armrest_1", 18),
    ("armpost_0", 8), ("armpost_1", 8),
    # Revision 2 restraint (DQ_BAR): hinge bosses on the side-frame legs (6-seg, outer cap buried in the leg, 18), the
    # bar as ONE U sweep arm -> lap bar -> arm (6-seg, 8 rings, ends buried in the bosses, 84), two inverted-T footrests
    # (4-seg stem buried at both ends, 8, and 6-seg capped foot bar, 24)
    ("barhinge_0", 18), ("barhinge_1", 18), ("barlink", 84),
    ("footstem_0", 8), ("footstem_1", 8), ("footbar_0", 24), ("footbar_1", 24),
    ("seatpan", 12), ("backboard", 10),
    ("cushion_0", 26), ("cushion_1", 26), ("cushion_2", 26), ("cushion_3", 26),
    ("pad_0", 20), ("pad_1", 20), ("pad_2", 20), ("pad_3", 20),
)

STYLES = {
    "tr": Style(
        key="tr", base="SkiLiftChair6", carrier="tr", letter="M", seats=3,
        lod0_cap=500, lod1_target=220, lod2_cap=70,
        lod1_delete=("bolt_",),
        lod2_delete=("bolt_", "armrest_", "armpost_", "footlink_", "backbar_", "plate_",
                     "cushion_", "pad_", "gripjaw", "gripspring", "gripplate_"),
        lod2_decimate_target=62,
        bake="v2", lod2="proxy", grip_part="grip",
        tint={"B": BENCH_PREVIEW_RGB},
        bottom_part="seat",                  # Revision 1: no footrest, the plank is lowest
        density=dict(DENSITY_WEIGHTS, **{"pad_": 1.3, "gripjaw": 1.2, "gripspring": 1.0,
                                         "gripplate_": 1.2}),
        part_tris=_TR_TALLY,
        proxy_tris=(("grip", 10), ("tube_stem", 30), ("seatblock", 12), ("backblock", 10)),
        ab=("chairs", "L", "SkiLiftChair6L")),
    "dq": Style(
        key="dq", base="SkiLiftChairDQ4", carrier="dq", letter="M", seats=4,
        lod0_cap=1000, lod1_target=350, lod2_cap=100,
        # LOD1 grip = carriage box + jaw only (Revision 1b keeps that: comb + axles go too)
        lod1_delete=("bolt_", "wheel_", "spring_", "lever_", "barhinge_", "comb", "axle_"),
        lod2_delete=("bolt_", "wheel_", "spring_", "lever_", "jaw_", "armrest_", "armpost_",
                     "barhinge_", "plate_", "cushion_", "pad_", "clampblock",
                     "comb", "axle_"),
        lod2_decimate_target=96,
        bake="v2", lod2="proxy", grip_part="gripbody",
        tint={"R": GALV_PREVIEW_RGB, "B": GREY_BENCH_RGB},
        # comb 2.5: its rib pitch (2 cm) must clear the 2-texel relief floor at 512
        density={"plate_face": 2.5, "gripbody": 1.4, "comb": 2.5, "clampblock": 1.2,
                 "cushion_": 1.3, "pad_": 1.3, "jaw_": 1.0, "lever_": 0.8, "spring_": 0.8,
                 "wheel_": 0.8, "axle_": 0.6, "armpost_": 0.8, "barhinge_": 0.8,
                 "seatpan": 0.8, "backboard": 0.7},
        part_tris=_DQ_TALLY,
        # Revision 1: the proxy grip box also covers the hanger's top arm, so the proxy hanger is one bend (18). Revision
        # 2 added the bar line (the lap bar, one tri-link) and the two T footrests (stem + foot bar, 4 tri-links). It
        # left out the short arms (sub-pixel beside the hoop legs at the LOD2 switch) for the 100 cap.
        proxy_tris=(("gripbody", 10), ("tube_hanger", 18), ("hooptop", 6), ("hoopside_0", 6),
                    ("hoopside_1", 6), ("seatblock", 12), ("backblock", 10),
                    ("restraint", 6), ("footstem_0", 6), ("footstem_1", 6),
                    ("footbar_0", 6), ("footbar_1", 6)),
        ab=()),
    "fg6l": Style(
        key="fg6l", base="SkiLiftChair6", carrier="tr", letter="L", seats=3,
        lod0_cap=TRI_BUDGET, lod1_target=LOD_TARGETS[0], lod2_cap=LOD_TARGETS[1],
        lod1_delete=LOD1_DELETE, lod2_delete=LOD2_DELETE, lod2_decimate_target=LOD_TARGETS[1],
        bake="legacy", lod2="decimate", grip_part="grip",
        tint={"B": BENCH_PREVIEW_RGB}, density=DENSITY_WEIGHTS,
        ab=("chairs", "K", "SkiLiftChair6K")),
}

# Part -> atlas quad family, for the C11/C12 checks (G galvanized, P paint, B bench)
FAMILY_OF_QUAD = {QUAD_GALV: "G", QUAD_PAINT: "P", QUAD_BENCH: "B"}


@dataclass
class ChairParams:
    seats: int = 3                  # double / triple / quad
    seat_width_each: float = 0.6    # meters of bench per rider

    hanger_len: float = 2.2         # rope to seat center height (the J stem reaches it)
    hanger_radius: float = 0.045    # the swan-neck stem tube
    grip_size: float = 0.16         # the clamp that grips the haul rope

    seat_depth: float = 0.52
    seat_thick: float = 0.06
    back_height: float = 0.50       # seat top to backrest top rail
    rail_radius: float = 0.028      # frame tubing

    plate_w: float = 0.20           # number plate on the backrest back
    plate_h: float = 0.13

    @property
    def width(self):
        return self.seats * self.seat_width_each


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights,
                  bpy.data.images):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


# --------------------------------------------------------------------------------------
# Primitives (thin wrappers over cs2_kit so every part carries a relief tag + UV layer)
# --------------------------------------------------------------------------------------

def add_box(name, size, location, drop=()):
    """Axis-aligned box centered on `location`. `drop` = buried face keys ("-Z", "+X"...)."""
    return cs2_kit.add_box(name, size, location, drop=drop)


def add_tube(name, radius, depth, location, rotation=(0, 0, 0), segments=8, caps="both"):
    """Cylinder along local Z (rotated by `rotation`) with fan caps unless buried."""
    return cs2_kit.add_cyl(name, radius, depth, location, rotation, segments, caps)


def add_link(name, p0, p1, radius, segments=6, extend=0.03, caps="both"):
    """
    Tube spanning two 3D points, lengthened `extend` past each end. Local +Z runs p0 -> p1, with local +Y tracked to
    world up. So a 6-gon has one vertex straight up and one straight DOWN, and a bar's lowest point is exactly center -
    radius. This matters because the height contract is measured from real vertices. caps: "both", "top" (= the p1 end),
    "bottom" (= the p0 end) or "none".
    """
    a, b = mathutils.Vector(p0), mathutils.Vector(p1)
    d = b - a
    rot = d.to_track_quat("Z", "Y").to_euler()
    return cs2_kit.add_cyl(name, radius, d.length + 2.0 * extend, tuple((a + b) / 2.0),
                           tuple(rot), segments, caps)


def _bar(name, p0, p1, radius, caps="none"):
    """Frame bar with both ends buried: 4-seg below r 0.02 (plan 3.1), else 6-seg."""
    return add_link(name, p0, p1, radius, segments=4 if radius < 0.02 else 6,
                    extend=0.0, caps=caps)


def _mesh_object(name, bm, loc, relief="geo"):
    if bm.loops.layers.uv.get("UVMap") is None:
        bm.loops.layers.uv.new("UVMap")
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    obj["relief"] = relief
    return obj


def add_cushion(name, size, loc, scoop=0.02):
    """
    Vinyl cushion: an open-bottomed block whose top is a 4-triangle fan to a center vertex lowered by `scoop` (the seat
    dip, which reads as a soft hollow when smooth-shaded at 66 deg). 12 tris. relief "map:seams" (cs2_compose draws the
    stitching).
    """
    sx, sy, sz = size
    hx, hy, hz = sx / 2.0, sy / 2.0, sz / 2.0
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    corners = ((-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy))
    bot = [bm.verts.new((x, y, -hz)) for x, y in corners]
    top = [bm.verts.new((x, y, hz)) for x, y in corners]
    c = bm.verts.new((0.0, 0.0, hz - scoop))
    for k in range(4):
        k1 = (k + 1) % 4
        bm.faces.new((bot[k], bot[k1], top[k1], top[k]))     # outward side (CCW from outside)
        bm.faces.new((top[k], top[k1], c))                   # top fan, normal +Z
    bm.normal_update()
    return _mesh_object(name, bm, loc, "map:seams")


def add_plate_face(name, x, yc, zc, w, h, facing=1):
    """The number plate's visible face: ONE quad in the plane x, normal -X * facing."""
    hy, hz = w / 2.0, h / 2.0
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    order = [(-hy, -hz), (-hy, hz), (hy, hz), (hy, -hz)]     # normal -X
    if facing < 0:
        order.reverse()
    vs = [bm.verts.new((0.0, y, z)) for y, z in order]
    bm.faces.new(vs)
    bm.normal_update()
    return _mesh_object(name, bm, (x, yc, zc), "geo")


def add_profile_slab(name, pts_xz, width, loc, rot_y=0.0, open_bottom=False,
                     relief="map:seams"):
    """
    A convex (x, z) profile, counter-clockwise seen from -Y, extruded +-width/2 along Y (dq-chair.md section 4.3). The
    side quads and the two caps are triangle fans WITHOUT a center vertex (n - 2 tris each). open_bottom drops the side
    quad on the first edge (P1-P2, the face that sits on the seat pan). The object sits at `loc`, rotated `rot_y` rad
    about its local Y (the profile's origin is the hinge). UVs are box-projected "UVMap". Relief is "map:seams". Raises
    on a non-convex or clockwise profile.
    """
    n = len(pts_xz)
    for i in range(n):
        (ax, az), (bx, bz), (cx, cz) = pts_xz[i], pts_xz[(i + 1) % n], pts_xz[(i + 2) % n]
        if (bx - ax) * (cz - bz) - (bz - az) * (cx - bx) <= 0.0:
            raise ValueError(f"{name}: profile not convex / counter-clockwise at point {i + 1}")
    hw = width / 2.0
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    lo = [bm.verts.new((x, -hw, z)) for x, z in pts_xz]
    hi = [bm.verts.new((x, hw, z)) for x, z in pts_xz]
    for i in range(n):
        j = (i + 1) % n
        if open_bottom and i == 0:
            continue
        bm.faces.new((lo[i], hi[i], hi[j], lo[j]))           # normal (dz, 0, -dx): outward
    for k in range(1, n - 1):
        bm.faces.new((lo[0], lo[k], lo[k + 1]))              # cap -Y
        bm.faces.new((hi[0], hi[k + 1], hi[k]))              # cap +Y
    cs2_kit._box_project_uv(bm, uv)
    bm.normal_update()
    obj = _mesh_object(name, bm, loc, relief)
    obj.rotation_euler = (0.0, rot_y, 0.0)
    return obj


def cushion_profile(d, tf, tb, xs=1.0):
    """profile8 (dq-chair.md 4.3): bullnose front, raked top. The x offsets scale by `xs`."""
    return [(-d / 2.0, 0.0), (d / 2.0 - 0.040 * xs, 0.0), (d / 2.0 - 0.005 * xs, 0.020),
            (d / 2.0, 0.060 * tf / 0.11), (d / 2.0 - 0.015 * xs, tf - 0.015),
            (d / 2.0 - 0.050 * xs, tf), (-d / 2.0 + 0.020 * xs, tb), (-d / 2.0, tb - 0.017)]


def pad_profile(h, t):
    """profile6 (dq-chair.md 4.3): front face at x 0, back at -t, rolled top."""
    return [(0.0, 0.0), (0.0, h - 0.04), (-0.025, h), (-t + 0.015, h), (-t, h - 0.03),
            (-t, 0.0)]


def add_tri_link(name, p0, p1, radius):
    """
    3-gon uncapped prism (6 tris) from p0 to p1, circumradius `radius`, one ring vertex
    pointing straight DOWN (as near as the axis allows), so a horizontal footbar proxy's
    bottom is exactly center.z - radius. The LOD2 proxy builder.
    """
    a, b = mathutils.Vector(p0), mathutils.Vector(p1)
    t = (b - a).normalized()
    down = mathutils.Vector((0.0, 0.0, -1.0))
    u = down - t * down.dot(t)
    if u.length < 1e-6:
        ref = mathutils.Vector((1.0, 0.0, 0.0))
        u = ref - t * ref.dot(t)
    u.normalize()
    v = t.cross(u)
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    rings = []
    for c in (a, b):
        rings.append([bm.verts.new(c + (u * math.cos(k * 2.0 * math.pi / 3.0)
                                        + v * math.sin(k * 2.0 * math.pi / 3.0)) * radius)
                      for k in range(3)])
    for k in range(3):
        k1 = (k + 1) % 3
        bm.faces.new((rings[0][k], rings[0][k1], rings[1][k1], rings[1][k]))
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    f = bm.faces[0]
    if f.normal.dot(f.calc_center_median() - (a + b) / 2.0) < 0.0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
        bm.normal_update()
    cs2_kit._box_project_uv(bm)
    return _mesh_object(name, bm, (0.0, 0.0, 0.0), "geo")


def add_closed_block(name, corners_lo, corners_hi, drop_bottom=False):
    """
    Hexahedron from 4 bottom + 4 top corners (each listed counter-clockwise seen from
    above, top[k] above bottom[k]). drop_bottom removes the bottom face (buried).
    """
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    lo = [bm.verts.new(c) for c in corners_lo]
    hi = [bm.verts.new(c) for c in corners_hi]
    for k in range(4):
        k1 = (k + 1) % 4
        bm.faces.new((lo[k], lo[k1], hi[k1], hi[k]))
    bm.faces.new(hi)
    if not drop_bottom:
        bm.faces.new(list(reversed(lo)))
    bm.normal_update()
    cs2_kit._box_project_uv(bm)
    return _mesh_object(name, bm, (0.0, 0.0, 0.0), "geo")


def add_soft_box(name, size, loc, chamfer, axes="xy", tray=None):
    """
    Box of `size` centered on `loc` (dq carriage, Revision 1b). Its edges along the listed axes ("x", "y", "z") are
    chamfered by `chamfer` (one bevel segment, 45 deg).

    Shading: shade_by_angle(66) + weighted normals alone do NOT keep the big faces flat (the corner fans span face ->
    chamfers -> face, revision1b-check-dq-grip). List the part in FLAT_PARTS, so _assemble marks the edges of its big
    faces sharp (Revision 1c, C14).

    tray = (inset, depth) sinks the top face into a shallow tray. The outer edge loop of the face stays at the box top
    (the mesh top, C2). The tray floor is `inset` in and `depth` lower, joined by sloped walls. All faces are quads (axes
    "xy": 28 tris, + tray 36). UVs are box-projected "UVMap". The build is deterministic (bmesh bevel / inset on the
    fixed create_cube topology).
    """
    bm = cs2_kit._box_bm(size)
    sel = []
    for e in bm.edges:
        d = e.verts[1].co - e.verts[0].co
        if "xyz"[max(range(3), key=lambda i: abs(d[i]))] in axes:
            sel.append(e)
    if sel and chamfer > 0.0:
        bmesh.ops.bevel(bm, geom=sel, offset=float(chamfer), offset_type="OFFSET", segments=1,
                        profile=0.5, affect="EDGES", clamp_overlap=True)
    if tray is not None:
        bm.normal_update()
        top = max(bm.faces, key=lambda f: (round(f.normal.z, 6), f.calc_center_median().z))
        inset, depth = tray
        bmesh.ops.inset_region(bm, faces=[top], thickness=float(inset), depth=-float(depth),
                               use_even_offset=True)
    bm.normal_update()
    cs2_kit._box_project_uv(bm)
    return cs2_kit._new_obj(name, bm, loc)


# --------------------------------------------------------------------------------------
# The FG chair (fg6l, and tr = the same frame with the Kagelok bench)
# --------------------------------------------------------------------------------------

def build_chair_parts(p: ChairParams, offset=(0, 0, 0), prefix="", facing=1,
                      bench_quad=QUAD_BENCH, variant="6l"):
    """
    Build the chair's parts, each tagged with its atlas quadrant: [(obj, quad), ...].

    M-style rebuild of the J/K swan-neck chair (liftblog Pali photos, the Yan drawing). A single continuous J-curved stem
    drops from the grip, sweeps back and down behind the backrest, and clamps to the backrest top rail (clamp block + two
    studs). The bench is a tube-steel frame: top rail, four back bars, armrests with open front ends on front posts,
    front lip tube, and a footrest bar on two links. The frame carries a padded backrest panel and three scooped seat
    cushions split by two dividers. A number plate sits on the back of the backrest.

    variant "tr" (the 2026-10 triple, dq-chair.md section 4.2) differs from 6L in these ways:
    - It drops the scooped cushions, the dividers, the lip tube and the two inner back bars (hidden behind the panel).
    - It adds three Kagelok profile8 cushions and three profile6 back pads (vertical: the J-stem frame is vertical,
      section 9 Q7).

    Revision 1 (2026-10-11): the grip box became a real fixed grip (build_fixed_grip). The footrest bar + links and the
    two clamp studs were dropped. The bench hangs at p.hanger_len = TR_HANGER_LEN, so the plank bottom is the lowest
    point. Everything else is the 6L chair. Variant "6l" is the shipped build, unchanged (same calls in the same order).

    HEIGHT CONTRACT (hard invariant - the game positions chairs by this): grip top to lowest point is EXACTLY 2.40 m. The
    grip top sits at offset.z + 0.08. The lowest point sits at offset.z - 2.32. In 6l it is the footrest bar's bottom
    VERTEX (add_link puts a 6-gon vertex straight down). In tr it is the seat plank's bottom face. Nothing may hang below
    it. The contract checks assert it from the mesh.

    Geometry hangs BELOW the grip, and the grip sits at `offset`. Standalone export re-pivots afterwards. A terminal
    generator may pass `offset` directly, so the grip lands on its modeled rope. `facing` is +1 (riders face +X, the
    travel direction) or -1 (mirrored along X). `prefix` keeps part names unique when several chairs share one scene.
    Part names are the LOD delete keys (LOD1_DELETE / LOD2_DELETE).
    """
    if variant not in ("6l", "tr"):
        raise ValueError(f"variant {variant!r}")
    if variant == "tr" and facing != 1:
        raise ValueError("variant 'tr' is authored for facing +1 only")
    K = cs2_kit
    ox, oy, oz = offset
    f = 1 if facing >= 0 else -1
    parts = []
    w = p.width
    seat_z = oz - p.hanger_len                       # bench plank center (-2.20)

    def X(relx):
        """Author in the facing=+1 frame. Mirror along X for facing=-1."""
        return ox + f * relx

    def P(relx, rely, z):
        return (X(relx), oy + rely, z)

    back_x = -0.45                                   # backrest panel center plane
    seat_cx = back_x + p.seat_depth / 2.0            # plank center: -0.19
    seat_top = seat_z + p.seat_thick / 2.0           # -2.17
    back_top_z = seat_top + p.back_height            # backrest top rail: -1.67
    rail_x = -0.50
    bar_x = -0.486                                   # back bars, just behind the panel
    arm_z = seat_z + 0.28
    y_out = w / 2.0 - 0.06                           # outer bars / armrests
    y_in = 0.30                                      # inner bars (behind the panel)

    if variant == "6l":
        # Grip on the rope - top face at oz + 0.08, the height datum. Clamp bolts painted.
        grip = K.add_box(f"{prefix}grip", (p.grip_size * 1.6, p.grip_size, p.grip_size),
                         P(0.0, 0.0, oz))
        grip["relief"] = "map:bolts"
        parts.append((grip, QUAD_GALV))
    else:
        parts.extend(build_fixed_grip(P, oz, prefix))

    # THE SWAN-NECK: one continuous tube_along sweep. Its top end is buried in the grip, its bottom end in the clamp
    # block, so no caps. Rings only across the three bends. The three fillets MEET end to end (stem_points: middle spans
    # = 2 x fillet), so the sweep is one even chain of ~0.15 m chords with no sliver faces. A short face beside a long
    # chord skews the area-weighted vertex normals and dents the tube.
    top_dz = STEM_TOP_DZ if variant == "6l" else TR_STEM_TOP_DZ
    stem = K.tube_along(f"{prefix}tube_stem", stem_points(oz, back_top_z, X, oy, top_dz),
                        p.hanger_radius, segments=8, caps="none", bend_rings=STEM_BEND_RINGS)
    if stem["rings"] != STEM_RINGS:
        raise RuntimeError(f"stem: expected {STEM_RINGS} rings (fillets meeting), "
                           f"got {stem['rings']}")
    parts.append((stem, QUAD_PAINT))

    # Stem/rail clamp block around the joint, two studs on top (real geometry, LOD1 drops them). 6l only: tr paints them
    # through the block's map:bolts relief (500-tri cap).
    clamp = K.add_box(f"{prefix}clampblock", (0.13, 0.15, 0.13), P(-0.51, 0.0, back_top_z))
    clamp["relief"] = "map:bolts"
    parts.append((clamp, QUAD_GALV))
    if variant == "6l":
        for k, sy in enumerate((-1, 1)):
            parts.append((K.add_cyl(f"{prefix}bolt_clamp_{k}", 0.010, 0.02,
                                    P(-0.51, sy * 0.045, back_top_z + 0.065 + 0.01),
                                    (0.0, 0.0, 0.0), 4, "top"), QUAD_GALV))

    # Backrest top rail (ends exposed: capped) and four back bars (ends buried).
    rl = w * 0.98
    parts.append((add_link(f"{prefix}backrail", P(rail_x, -rl / 2.0, back_top_z),
                           P(rail_x, rl / 2.0, back_top_z), p.rail_radius,
                           segments=8, extend=0.0, caps="both"), QUAD_PAINT))
    for k, side in enumerate((-1, 1)):
        parts.append((_bar(f"{prefix}backbar_o{k}", P(bar_x, side * y_out, back_top_z - 0.01),
                           P(bar_x, side * y_out, seat_top - 0.01), 0.022), QUAD_PAINT))
        if variant == "6l":
            parts.append((_bar(f"{prefix}backbar_i{k}", P(bar_x, side * y_in, back_top_z - 0.01),
                               P(bar_x, side * y_in, seat_top - 0.01), 0.016), QUAD_PAINT))

    # Padded backrest panel between the plank and the rail (bench vinyl, slot 3).
    pan_top = back_top_z - p.rail_radius
    pan_bot = seat_top + 0.02
    panel = K.add_box(f"{prefix}backrest", (0.04, w * 0.90, pan_top - pan_bot),
                      P(back_x, 0.0, (pan_top + pan_bot) / 2.0))
    panel["relief"] = "map:seams"
    parts.append((panel, bench_quad))

    # Number plate on the panel's back face, off-center so the stem never hides it. The
    # rim is a box with both X faces dropped (back buried, front replaced by plate_face,
    # the single quad cs2_compose.stamp_on_face paints the number onto).
    plate_y = 0.55
    plate_zc = (pan_top + pan_bot) / 2.0 + 0.02
    panel_back = back_x - 0.02
    parts.append((K.add_box(f"{prefix}plate_rim", (0.012, p.plate_w, p.plate_h),
                            P(panel_back - 0.006, plate_y, plate_zc), drop=("+X", "-X")),
                  QUAD_PAINT))
    parts.append((add_plate_face(f"{prefix}plate_face", X(panel_back - 0.012), oy + plate_y,
                                 plate_zc, p.plate_w, p.plate_h, f), QUAD_PAINT))

    # Seat plank (frame paint) with the front lip tube half proud of its front edge.
    parts.append((K.add_box(f"{prefix}seat", (p.seat_depth, w, p.seat_thick),
                            P(seat_cx, 0.0, seat_z)), QUAD_PAINT))
    if variant == "6l":
        lip_x = seat_cx + p.seat_depth / 2.0
        stl = w * 0.96
        parts.append((add_link(f"{prefix}seattube", P(lip_x, -stl / 2.0, seat_z),
                               P(lip_x, stl / 2.0, seat_z), 0.026, segments=6, extend=0.0,
                               caps="both"), QUAD_PAINT))

    if variant == "6l":
        # Three scooped cushions and two dividers on the plank (0.10 m plank margin each
        # side keeps the armrest posts clear of the vinyl).
        inner = w - 0.20
        div_w = 0.03
        cw = (inner - 2.0 * div_w) / 3.0
        cush_d = p.seat_depth - 0.06
        for k, m in enumerate((-1, 0, 1)):
            parts.append((add_cushion(f"{prefix}cushion_{k}", (cush_d, cw, 0.05),
                                      P(seat_cx, m * (cw + div_w), seat_top + 0.025),
                                      scoop=0.02),
                          bench_quad))
        for k, m in enumerate((-1, 1)):
            parts.append((K.add_box(f"{prefix}divider_{k}", (cush_d, div_w, 0.06),
                                    P(seat_cx, m * (cw + div_w) / 2.0, seat_top + 0.03),
                                    drop=("-Z",)), bench_quad))
    else:
        # Kagelok cushions (profile8, open bottom on the plank) and vertical back pads (profile6, closed) in front of the
        # panel. Pitch (w - 0.20) / 3 = 0.533, width = Kagelok w / pitch 0.8975 x pitch = 0.479 (a 5.5 cm gap: neighbors
        # must not come within the 0.5 mm weld). Depth 0.46 (x offsets x 0.92), front 0.09, back 0.05 (dq-chair.md 4.2).
        pitch = (w - 0.20) / 3.0
        cw = KAGELOK_U["cushion_w"] / KAGELOK_U["cushion_pitch"] * pitch
        cush_d = 0.46
        prof = cushion_profile(cush_d, 0.09, 0.05, xs=cush_d / 0.50)
        for k, m in enumerate((-1, 0, 1)):
            parts.append((add_profile_slab(f"{prefix}cushion_{k}", prof, cw,
                                           P(seat_cx + 0.03, m * pitch, seat_top),
                                           open_bottom=True), bench_quad))
        pad_front = back_x + 0.09                    # -0.36 (panel front -0.43 + 0.07)
        pad_t = 0.07
        prof = pad_profile(pan_top - pan_bot, pad_t)
        for k, m in enumerate((-1, 0, 1)):
            parts.append((add_profile_slab(f"{prefix}pad_{k}", prof, cw,
                                           P(pad_front, m * pitch, pan_bot)), bench_quad))

    # Armrests: back end buried in the outer back bar, open front end capped, on a
    # front post down into the plank.
    for k, side in enumerate((-1, 1)):
        y = side * y_out
        parts.append((add_link(f"{prefix}armrest_{k}", P(bar_x, y, arm_z), P(-0.02, y, arm_z),
                               0.020, segments=6, extend=0.0, caps="top"), QUAD_PAINT))
        parts.append((_bar(f"{prefix}armpost_{k}", P(-0.05, y, arm_z - 0.01),
                           P(-0.05, y, seat_top - 0.01), 0.018), QUAD_PAINT))

    if variant == "6l":
        # Footrest bar on two links below the seat front. Bar bottom VERTEX at oz - 2.32 CLOSES the 2.40 m height
        # contract. The links stop inside the bar.
        bar_r = 0.025
        bar_z = oz + LOWEST_Z + bar_r
        fl = w * 0.72
        parts.append((add_link(f"{prefix}footbar", P(0.14, -fl / 2.0, bar_z),
                               P(0.14, fl / 2.0, bar_z),
                               bar_r, segments=6, extend=0.0, caps="both"), QUAD_GALV))
        for k, side in enumerate((-1, 1)):
            y = side * w * 0.29
            parts.append((_bar(f"{prefix}footlink_{k}",
                               P(0.02, y, seat_z - p.seat_thick / 2.0 + 0.01),
                               P(0.13, y, bar_z + 0.012), 0.016), QUAD_GALV))
    elif abs((seat_z - p.seat_thick / 2.0) - (oz + LOWEST_Z)) > 1e-9:
        # tr (Revision 1): there is no footrest. The plank's bottom face closes the contract.
        raise RuntimeError(f"tr: plank bottom {seat_z - p.seat_thick / 2.0:.4f} != "
                           f"{oz + LOWEST_Z:.4f}; build with tr_params()")
    return parts


def tr_params():
    """ChairParams of the tr triple (Revision 1): the 6L frame with the bench lowered."""
    return ChairParams(seats=3, hanger_len=TR_HANGER_LEN, seat_thick=TR_SEAT_THICK)


# Fixed grip (tr, Revision 1), meters relative to the rope (the grip origin), authoring
# frame (x travel, y outboard, z up). Photos: fixed grips/close-jaw-grip-ski-lift-...webp
# (jaw saddle on the rope, housing below, spring on the hanger side) and csm_Fixe-
# Klemme...jpg (Doppelmayr fixed grip: long tapered jaws along the rope, lateral spring
# housing with an end cap, hanger clamped below). [D] sizes, proportions from the photos.
FG_GRIP = {
    "block": (0.160, 0.100, 0.140),     # jaw block (x, y, z), top = rope + GRIP_TOP_Z
    "saddle": [(-0.340, 0.010), (0.340, 0.010), (0.340, 0.028), (0.075, 0.062),
               (-0.075, 0.062), (-0.340, 0.028)],   # jaw saddle (x, z) profile on the rope
    "saddle_w": 0.070,                  # saddle width (rope 0.048)
    "spring": (0.038, 0.130, 0.100, -0.005),   # housing r, length, center y, center z
    "plate": (0.140, 0.016, 0.110),     # clamp plate (x, y, z)
    "plate_c": (-0.010, 0.055, -0.105), # plate center (x, +-y, z), stem r 0.045 between
}


def build_fixed_grip(P, oz, prefix=""):
    """
    The tr fixed grip [(obj, quad), ...] (Revision 1, verdicts-2026-10-11 item 1). It has these parts:
    - a jaw block around the rope, whose top face is the mesh top (rope + 0.080, C2/C3)
    - a long tapered jaw saddle riding on the rope fore and aft
    - a lateral spring housing with its end cap on the outboard side (inner end buried in the block)
    - two clamp plates below that sandwich the J-stem's top (proud of the block's sides)

    74 tris.
    """
    K = cs2_kit
    g = FG_GRIP
    bx, by, bz = g["block"]
    block = K.add_box(f"{prefix}grip", (bx, by, bz), P(0.0, 0.0, oz + GRIP_TOP_Z - bz / 2.0))
    block["relief"] = "map:bolts"
    parts = [(block, QUAD_GALV)]
    parts.append((add_profile_slab(f"{prefix}gripjaw", g["saddle"], g["saddle_w"],
                                   P(0.0, 0.0, oz), relief="geo"), QUAD_GALV))
    r, ln, cy, cz = g["spring"]
    parts.append((K.add_cyl(f"{prefix}gripspring", r, ln, P(0.0, cy, oz + cz),
                            (-math.pi / 2.0, 0.0, 0.0), 6, "top"), QUAD_GALV))
    px, py, pz = g["plate_c"]
    for k, s in enumerate((-1, 1)):
        # closed: the plates stand 1.3 cm proud of the block's sides, so their tops show
        pl = K.add_box(f"{prefix}gripplate_{k}", g["plate"], P(px, s * py, oz + pz))
        pl["relief"] = "map:bolts"
        parts.append((pl, QUAD_GALV))
    return parts


STEM_BEND_RINGS = 3
STEM_SPAN = 0.30                      # the two middle spans, fillet = STEM_SPAN / 2 each
STEM_ANGLES_DEG = (56.0, 74.0)        # middle-span slopes below horizontal (seg0 ~38, seg3 90)
STEM_VERT_TOP = -0.802                # where the vertical run starts (authoring z)
STEM_RINGS = 2 + 3 * STEM_BEND_RINGS - 2   # touching fillets: two coincident rings dedupe


def stem_points(oz, back_top_z, X=lambda x: x, oy=0.0, top_dz=STEM_TOP_DZ):
    """
    The five J-stem points, authored so cs2_kit.tube_along's fillets meet. The two middle spans are exactly STEM_SPAN
    long, so every bend gets fillet = STEM_SPAN / 2 and the end of one fillet is the start of the next (no straight
    slivers between bends). The points are built backwards from the vertical run (x = -0.52, behind the backrest) at the
    middle-span slopes. The first span (grip -> P1) comes out ~0.34 m at ~38 deg (tr: top_dz -0.020, ~0.37 m at ~42 deg).
    The vertical run reaches the clamp block on the backrest rail.
    """
    a1, a2 = (math.radians(a) for a in STEM_ANGLES_DEG)
    p3 = (-0.52, oz + STEM_VERT_TOP)
    p2 = (p3[0] + STEM_SPAN * math.cos(a2), p3[1] + STEM_SPAN * math.sin(a2))
    p1 = (p2[0] + STEM_SPAN * math.cos(a1), p2[1] + STEM_SPAN * math.sin(a1))
    return [(X(0.0), oy, oz + top_dz),
            (X(p1[0]), oy, p1[1]),
            (X(p2[0]), oy, p2[1]),
            (X(p3[0]), oy, p3[1]),
            (X(-0.52), oy, back_top_z - 0.03)]


# --------------------------------------------------------------------------------------
# The detachable quad (dq-chair.md section 4.1, authored post-pivot: footbar bottom z = 0)
# --------------------------------------------------------------------------------------

def dq_datums(row, hanger_offset=HANGER_OFFSET):
    """Authoring datums of the dq chair from its carrier row (meters, post-pivot)."""
    top = float(row["hang_m"])
    rope = top - float(row["grip_top_above_rope_m"])
    o = float(hanger_offset)
    y = SIDE_Y
    hoop_back, hoop_top = (-0.200, 0.190), (0.0, 1.503)       # the leg: B_L -> T_L (x, z)
    hz = DQ_BAR["hinge_z"]
    hx = hoop_back[0] + (hoop_top[0] - hoop_back[0]) * (hz - hoop_back[1]) / (hoop_top[1] - hoop_back[1])
    return {
        "top": top, "rope": rope, "y": y,
        # C-hanger: buried in the grip body -> top arm out to the outboard side -> the
        # vertical run -> the bottom leg back inboard into the clamp on the top bar
        "H": [(0.0, 0.070, rope + 0.020), (0.0, o - 0.065, rope - 0.035), (0.0, o, 1.717),
              (0.0, -0.020, 1.675)],
        # hoop: front rail ends -> back corner -> top corner -> top bar -> mirror
        "hoop": [(0.160, -y, 0.190), (hoop_back[0], -y, hoop_back[1]), (hoop_top[0], -y, hoop_top[1]),
                 (hoop_top[0], y, hoop_top[1]), (hoop_back[0], y, hoop_back[1]), (0.160, y, 0.190)],
        # restraint (Revision 2 / 3, DQ_BAR): hinge axis on the leg line, lap bar DOWN, T's
        "hinge": (hx, hz), "arm_y": DQ_BAR["arm_y"],
        "bar": DQ_BAR["down"],                   # lap bar center DOWN (x, z)
        "foot": DQ_BAR["foot"],                  # foot bar center (x, z), r -> bottom 0
        "seat_top": DQ_SEAT_TOP, "pan": (0.073, 0.262),
        "pad_edge": (-0.177, 0.400),             # pads' front-bottom hinge
        "pad_h": 0.55, "pad_t": 0.09,
    }


def build_dq_parts(row, hanger_offset=HANGER_OFFSET):
    """
    The detachable quad's parts [(obj, quad), ...] in contract order (dq-chair.md 4.1, skeptic D3/D4 radii and burial
    applied). Grip-side heights follow the carrier row: the grip body's top IS hang_m and the rope point is hang_m -
    grip_top_above_rope_m.
    """
    K = cs2_kit
    d = dq_datums(row, hanger_offset)
    top, rope, Y = d["top"], d["rope"], d["y"]
    R = DQ_R
    parts = []

    # -- detachable grip (Revision 1, photo "close up of grip" + detachable quad 1) -----
    # carriage / spring box: the top tray's rim = hang_m (C2), bbox holds the rope point
    # (C3). Revision 1b: chamfered x- / y-axis edges + the tray (add_soft_box)
    g = DQ_GRIP
    sx_, sy_, sz_ = g["body"]
    by_ = g["body_y0"] + sy_ / 2.0
    soft, (t_in, t_dz) = g["soft"], g["tray"]
    gb = add_soft_box("gripbody", (sx_, sy_, sz_), (0.0, by_, top - sz_ / 2.0), soft, "xy",
                      (t_in, t_dz))
    gb["relief"] = "map:bolts"
    parts.append((gb, QUAD_GALV))
    # the friction comb (photo: the silver ribbed plate on the carriage top) in the tray: floor -> `recess` below the
    # rim, so the carriage keeps the mesh top (C2). Its ribs are relief (style_detail_specs "comb"). The box bottom is
    # buried on the tray floor
    margin, recess = g["comb"]
    floor = top - t_dz
    ct = (top - recess) - floor
    parts.append((K.add_box("comb", (sx_ - 2.0 * (soft + t_in + margin),
                                     sy_ - 2.0 * (soft + t_in + margin), ct),
                            (0.0, by_, floor + ct / 2.0), drop=("-Z",)), QUAD_GALV))
    parts[-1][0]["relief"] = "map:seams"
    # the jaw: a long bar riding ON the rope (bottom just above the rope center, C3),
    # proud of the carriage fore / aft and 1 cm proud of its inboard face
    jx, jy, jz = g["jaw"]
    parts.append((K.add_box("jaw_rope", (jx, jy, jz), (0.0, 0.0, rope + g["jaw_dz"] + jz / 2.0)),
                  QUAD_GALV))
    # the jaw lever on the inboard face, rising aft to its roller (its +Y face is buried)
    lx, ly, lz = g["lever"]
    lcx, lcz, ltilt = g["lever_c"]
    parts.append((K.add_box("lever_fin", (lx, ly, lz),
                            (lcx, g["body_y0"] - ly / 2.0, rope + lcz),
                            rot=(0.0, math.radians(ltilt), 0.0), drop=("+Y",)), QUAD_GALV))
    rr, rd, rcx, rcz = g["roller"]
    parts.append((K.add_cyl("lever_roller", rr, rd,
                            (rcx, g["body_y0"] - ly - rd / 2.0 + 0.010, rope + rcz),
                            (math.pi / 2.0, 0.0, 0.0), 6, "both"), QUAD_GALV))
    # two coil-spring cylinders on the outboard face (inner 1 cm buried in the box)
    sr, sd, sxc = g["spring"]
    for k, s in enumerate((-1, 1)):
        parts.append((K.add_cyl(f"spring_{k}", sr, sd,
                                (s * sxc, g["body_y0"] + sy_ + sr - 0.012, top - 0.030 - sd / 2.0),
                                (0.0, 0.0, 0.0), 6, "both"), QUAD_GALV))
    # two running wheels fore / aft BESIDE the jaw on the inboard side (Revision 1b, was: in the rope's plane above the
    # rope): centers at the jaw's mid-height, near rope height, 0.127 inboard (Kagelok D05 rollers, skeptic claim 4).
    # Each wheel sits on a stub axle from 1 cm inside the jaw's inboard face to the wheel center (both ends buried)
    wr, wd, wx = g["wheel"]
    wy, wz = g["wheel_y"], rope + g["jaw_dz"] + jz / 2.0
    for k, s in enumerate((-1, 1)):
        parts.append((K.add_cyl(f"wheel_{k}", wr, wd, (s * wx, wy, wz),
                                (math.pi / 2.0, 0.0, 0.0), 8, "both"), QUAD_GALV))
    for k, s in enumerate((-1, 1)):
        parts.append((add_link(f"axle_{k}", (s * wx, -jy / 2.0 + 0.010, wz), (s * wx, wy, wz),
                               g["axle_r"], segments=6, extend=0.0, caps="none"), QUAD_GALV))

    # -- the C-hanger: one sweep, both ends buried (body / clamp block) ------------------
    hanger = K.tube_along("tube_hanger", d["H"], R["hanger"], segments=8, caps="none",
                          bend_rings=4)
    if hanger["rings"] != 10:
        raise RuntimeError(f"tube_hanger: expected 10 rings, got {hanger['rings']} (C5)")
    parts.append((hanger, QUAD_PAINT))

    clamp = K.add_box("clampblock", (0.120, 0.340, 0.320), (0.0, 0.059, 1.598))
    clamp["relief"] = "map:bolts"
    parts.append((clamp, QUAD_GALV))
    parts.append((add_plate_face("plate_face", 0.062, 0.059, 1.600, 0.20, 0.13, facing=-1),
                  QUAD_PAINT))

    # -- hoop frame: one sweep F_L -> B_L -> T_L -> T_R -> B_R -> F_R ---------------------
    hoop = K.tube_along("tube_hoop", d["hoop"], R["hoop"], segments=6, caps="both",
                        bend_rings=3, fillet=0.25)
    if hoop["rings"] != 14:
        raise RuntimeError(f"tube_hoop: expected 14 rings, got {hoop['rings']} (C5)")
    parts.append((hoop, QUAD_PAINT))

    # -- armrests on posts (armrest back end buried in the side frame) -----------------
    for k, s in enumerate((-1, 1)):
        parts.append((add_link(f"armrest_{k}", (-0.150, s * Y, 0.520), (0.250, s * Y, 0.520),
                               R["armrest"], segments=6, extend=0.0, caps="top"), QUAD_PAINT))
    for k, s in enumerate((-1, 1)):
        parts.append((add_link(f"armpost_{k}", (0.240, s * Y, 0.510), (0.240, s * Y, 0.290),
                               R["armpost"], segments=4, extend=0.0, caps="none"), QUAD_PAINT))

    # -- restraint bar, DOWN (9 Q3, Revision 2 parts, Revision 3 numbers: DQ_BAR) --------
    # hinge bosses on the inboard faces of the hoop legs: axis lateral through (hx, hz), outer end at the leg's center
    # line (buried, no cap), inner end capped just past the arm (the arm's open end ring sits inside it)
    hx, hz = d["hinge"]
    ay = d["arm_y"]
    y_in = ay - DQ_BAR["boss_in"]
    for k, s in enumerate((-1, 1)):
        # rot x +90 deg: local +Z -> world -Y, so the inboard (kept) cap is local -Z on the
        # -Y side and local +Z on the +Y side
        boss = K.add_cyl(f"barhinge_{k}", R["barhinge"], Y - y_in, (hx, s * (Y + y_in) / 2.0, hz),
                         (math.pi / 2.0, 0.0, 0.0), 6, "bottom" if s < 0 else "top")
        boss["relief"] = "map:bolts"
        parts.append((boss, QUAD_PAINT))
    # arms + lap bar: one U sweep hinge -> corner -> corner -> hinge (ends buried in the bosses, 2 bends x 3 rings + 2
    # ends = 8 rings)
    bx, bz = d["bar"]
    u = K.tube_along("barlink", [(hx, -ay, hz), (bx, -ay, bz), (bx, ay, bz), (hx, ay, hz)],
                     R["barlink"], segments=6, caps="none", bend_rings=3,
                     fillet=DQ_BAR["corner"])
    if u["rings"] != 8:
        raise RuntimeError(f"barlink: expected 8 rings, got {u['rings']}")
    parts.append((u, QUAD_PAINT))
    # two inverted-T footrests hung from the lap bar: the stem's ends sit on the lap bar's and the foot bar's axes
    # (buried: r 0.016 < the 6-gons' inscribed 0.021 / 0.019). The foot bar's bottom vertex is z 0 (C4)
    fx, fz = d["foot"]
    if abs(fz - R["footbar"]) > 1e-12:
        raise RuntimeError(f"foot bar center z {fz} != its radius {R['footbar']} (C4: bottom 0)")
    fy, fh = DQ_BAR["foot_y"], DQ_BAR["foot_half"]
    for k, s in enumerate((-1, 1)):
        parts.append((add_link(f"footstem_{k}", (bx, s * fy, bz), (fx, s * fy, fz), R["footstem"],
                               segments=4, extend=0.0, caps="none"), QUAD_GALV))
    for k, s in enumerate((-1, 1)):
        parts.append((add_link(f"footbar_{k}", (fx, s * fy - fh, fz), (fx, s * fy + fh, fz),
                               R["footbar"], segments=6, extend=0.0, caps="both"), QUAD_GALV))

    # -- bench block ---------------------------------------------------------------------
    pcx, pcz = d["pan"]
    parts.append((K.add_box("seatpan", (0.520, 2.460, 0.056), (pcx, 0.0, pcz)), QUAD_BENCH))
    tilt = math.radians(PAD_TILT_DEG)
    board = K.add_box("backboard", (0.020, 2.400, 0.670), (-0.305, 0.0, 0.625),
                      rot=(0.0, -tilt, 0.0), drop=("-Z",))
    parts.append((board, QUAD_BENCH))
    prof = cushion_profile(0.50, 0.110, 0.067)
    for k, yc in enumerate((-0.90, -0.30, 0.30, 0.90)):
        parts.append((add_profile_slab(f"cushion_{k}", prof, 0.5385, (pcx, yc, 0.290),
                                       open_bottom=True), QUAD_BENCH))
    prof = pad_profile(d["pad_h"], d["pad_t"])
    ex, ez = d["pad_edge"]
    for k, yc in enumerate((-0.90, -0.30, 0.30, 0.90)):
        parts.append((add_profile_slab(f"pad_{k}", prof, 0.5385, (ex, yc, ez), rot_y=-tilt),
                      QUAD_BENCH))
    mirror_grip_side(parts)          # nitpick A2: the grip side onto the outboard (DQ_OUTBOARD)
    return parts


def _mirror_part_y(obj):
    """Mirror one part across the travel plane y = 0 in place: world' = D @ world with
    D = diag(1, -1, 1). The object keeps a proper placement (D M D: location y negated, rotation
    conjugated) and its mesh is mirrored locally (D) with the winding reversed, so the normals
    stay outward and the triangle count is unchanged."""
    D = mathutils.Matrix.Diagonal((1.0, -1.0, 1.0, 1.0))
    obj.matrix_basis = D @ obj.matrix_basis @ D
    obj.data.transform(D)
    obj.data.flip_normals()
    obj.data.update()


def mirror_grip_side(parts, names=DQ_GRIP_SIDE_PARTS, outboard=None):
    """Nitpick A2: when the outboard side is authoring -Y (DQ_OUTBOARD < 0), mirror every part whose name starts with one of
    `names` (the grip-side group, [(obj, quad|chooser)] lists of build_dq_parts and _proxy_parts). Returns the number
    mirrored (0 for +1)."""
    o = DQ_OUTBOARD if outboard is None else float(outboard)
    if o > 0.0:
        return 0
    n = 0
    for obj, _q in parts:
        if obj.name.startswith(tuple(names)):
            _mirror_part_y(obj)
            n += 1
    return n


# --------------------------------------------------------------------------------------
# Atlas repaint + density
# --------------------------------------------------------------------------------------

def chair_atlas_post(base, nrm, msk, ctl, A):
    """
    make_texture_set_atlas `post` hook: repaint quad 2 (the lift atlas glass quad, which the chair never uses) as bench
    vinyl - light-neutral RECOLOR_ALBEDO albedo with a faint grain, dielectric, matte - on recolor slot 3 (ControlMask B
    = 1, R = G = 0). The whole quad is painted, reserved top strip included (nothing maps there, and the finish restores
    it 1:1, harmlessly).
    """
    rng = np.random.default_rng(41)
    h = A // 2
    sl = (slice(A - h, A), slice(0, h))
    grain = rng.normal(0.0, 0.012, (h, h)).astype(np.float32)
    for ch in range(3):
        base[sl][..., ch] = np.clip(cs2_asset.RECOLOR_ALBEDO + grain, 0.0, 1.0)
    base[sl][..., 3] = 1.0
    nrm[sl][..., 0:3] = (0.5, 0.5, 1.0)
    nrm[sl][..., 3] = 1.0
    msk[sl][..., 0] = 0.0
    msk[sl][..., 1] = 0.0
    msk[sl][..., 2] = 0.0
    msk[sl][..., 3] = BENCH_SMOOTH
    ctl[sl][..., 0] = 0.0
    ctl[sl][..., 1] = 0.0
    ctl[sl][..., 2] = 1.0
    ctl[sl][..., 3] = 0.0


def _weight_for(name, weights):
    best = None
    for prefix, w in weights.items():
        if name.startswith(prefix) and (best is None or len(prefix) > len(best[0])):
            best = (prefix, w)
    return 1.0 if best is None else float(best[1])


def equalise_quadrant_density(parts, weights=None, verbose=True):
    """
    Shrink-only per-part island scaling INSIDE each atlas quadrant so the UV area of a
    part is proportional to its 3D area x weight^2 (uniform px/m at weight 1). Call after
    unwrap_into_quadrant and before the join. Scaling is about the quadrant center, so
    islands stay inside their quad (and below quad 2's reserved top). Returns
    {part name: (px/m ratio before, after)} in atlas-UV units (uv_area / area3d).
    """
    weights = DENSITY_WEIGHTS if weights is None else weights
    stats = []
    for obj, quad in parts:
        me = obj.data
        uv = me.uv_layers.active.data
        a3 = sum(pg.area for pg in me.polygons)
        auv = 0.0
        for pg in me.polygons:
            pts = [uv[li].uv for li in pg.loop_indices]
            s = 0.0
            for i in range(len(pts)):
                x0, y0 = pts[i]
                x1, y1 = pts[(i + 1) % len(pts)]
                s += x0 * y1 - x1 * y0
            auv += abs(s) * 0.5
        if a3 < 1e-9 or auv < 1e-12:
            continue
        w = _weight_for(obj.name, weights)
        stats.append((obj, quad, auv / a3, w))
    ref = min(r / (w * w) for _o, _q, r, w in stats)
    report = {}
    for obj, quad, r, w in stats:
        target = ref * w * w
        s = min(1.0, math.sqrt(target / r))
        uo, vo = cs2_asset.QUADS[quad]
        cu, cv = uo + 0.25, vo + 0.25
        if s < 0.9999:
            for l in obj.data.uv_layers.active.data:
                u, v = l.uv
                l.uv = (cu + (u - cu) * s, cv + (v - cv) * s)
        report[obj.name] = (r, r * s * s)
    if verbose:
        lo = min(v[1] for v in report.values())
        hi = max(v[1] for v in report.values())
        print(f"[chair] density equalized in the atlas quads: uv/3d ratio now "
              f"{lo:.3f} .. {hi:.3f} (was {min(v[0] for v in report.values()):.3f} .. "
              f"{max(v[0] for v in report.values()):.3f}); weights {weights}")
    return report


# --------------------------------------------------------------------------------------
# Assembly
# --------------------------------------------------------------------------------------

def mark_flat_faces(obj, table, names):
    """Revision 1c: mark sharp every edge of the big (axis-aligned, FLAT_FACE_AXIS) faces of the parts `names`, so
    shade_by_angle(keep_sharp_edges) + weighted normals leave those faces flat. Writes the mesh's `sharp_edge` attribute
    and returns the number of edges marked."""
    ids = {table.index(n) + 1 for n in names if n in table}
    if not ids:
        return 0
    me = obj.data
    fp = face_parts(obj)
    key_to_edge = {tuple(sorted(e.vertices)): e.index for e in me.edges}
    sharp = me.attributes.get("sharp_edge") or me.attributes.new("sharp_edge", "BOOLEAN", "EDGE")
    vals = np.zeros(len(me.edges), dtype=bool)
    sharp.data.foreach_get("value", vals)
    marked = set()
    for poly, p in zip(me.polygons, fp):
        if int(p) in ids and float(np.abs(np.array(poly.normal)).max()) > FLAT_FACE_AXIS:
            marked.update(key_to_edge[tuple(sorted(k))] for k in poly.edge_keys)
    for i in marked:
        vals[i] = True
    sharp.data.foreach_set("value", vals)
    me.update()
    return len(marked)


def flat_face_report(obj, names):
    """C14 (Revision 1c): {part: (big faces, worst corner-normal deviation in degrees)} - the
    angle between each corner normal and its face normal over the big (axis-aligned) faces
    of the parts `names` on a shaded mesh."""
    me = obj.data
    table = part_table(obj)
    fp = face_parts(obj)
    cn = np.array([tuple(c.vector) for c in me.corner_normals], dtype=np.float64)
    out = {}
    for n in names:
        if n not in table:
            out[n] = (0, 180.0)
            continue
        pid = table.index(n) + 1
        k, worst = 0, 0.0
        for poly, p in zip(me.polygons, fp):
            fn = np.array(poly.normal, dtype=np.float64)
            if int(p) != pid or float(np.abs(fn).max()) <= FLAT_FACE_AXIS:
                continue
            k += 1
            d = cn[list(poly.loop_indices)] @ fn
            worst = max(worst, float(np.degrees(np.arccos(np.clip(d, -1.0, 1.0))).max()))
        out[n] = (k, worst)
    return out


def _assemble(parts, name, weights, shade=True, density_w=False, flat_parts=()):
    """
    Parts -> tag_parts -> per-part atlas UVs -> equalize density [-> density_w attribute
    for cs2_bake's unique pack] -> join -> remove_doubles -> base pivot -> orient_for_net
    -> store_part_table [-> mark_flat_faces(flat_parts)] -> shade_by_angle(66). Returns
    (joined object, part table).
    """
    table = cs2_asset.tag_parts(parts)

    # Per-part UVs into the shared atlas quadrants BEFORE joining, then equalized so the
    # finish repack ships a uniform (weighted) texel density.
    for obj, quad in parts:
        cs2_asset.unwrap_into_quadrant(obj, quad)
    equalise_quadrant_density(parts, weights)
    if density_w:
        import cs2_uv
        cs2_uv.set_density(parts, weights)

    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()

    chair = bpy.context.active_object
    chair.name = name
    chair.data.name = name

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=WELD_M)
    bpy.ops.object.mode_set(mode="OBJECT")

    # BASE-PIVOT: shift the whole chair up so its lowest point sits at z = 0.
    #
    # The first version put the origin at the grip, so the chair hung entirely BELOW its own origin. In game only the 8
    # cm grip rendered - everything below the origin was treated as underground and clipped. Every CS2 object we have
    # inspected (the vanilla 47 m pylon, the 8 m pole, our tower) is base-pivoted with geometry at y >= 0.
    #
    # The grip therefore sits at the TOP of the object, at z = height, and the NetSubObjects m_Position.y must be lowered
    # by that height so the grip still lands on the rope.
    bpy.ops.object.select_all(action="DESELECT")
    chair.select_set(True)
    bpy.context.view_layer.objects.active = chair
    lo = min((chair.matrix_world @ v.co).z for v in chair.data.vertices)
    chair.location.z -= lo
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

    cs2_asset.orient_for_net(chair)
    cs2_asset.store_part_table(chair, table)
    if flat_parts:
        n = mark_flat_faces(chair, table, flat_parts)
        print(f"[chair] {name}: {n} edges of the big faces of {list(flat_parts)} marked sharp "
              f"(Revision 1c, C14)")
    if shade:
        cs2_asset.shade_by_angle(chair, SHADE_ANGLE)
    return chair, table


def build_chair(p: ChairParams, name=DEFAULT_NAME, shade=True):
    """
    The shipped 6L chair (fg6l): build_chair_parts -> _assemble. Returns the joined object. Its mesh carries
    cs2_asset.PART_TABLE_PROP (json name table) and "part".
    """
    chair, _table = _assemble(build_chair_parts(p), name, DENSITY_WEIGHTS, shade=shade)
    return chair


def build_style_chair(style, row, name, hanger_offset=HANGER_OFFSET, shade=True):
    """
    tr / dq LOD0: the style's parts, the C9 part checks on the loose parts (names, order,
    per-part triangles = the contract tally), then _assemble with density_w.
    Returns (chair, info) - info: part table, {part: quad}, {part: tris}.
    """
    if style.key == "tr":
        parts = build_chair_parts(tr_params(), variant="tr")
    elif style.key == "dq":
        parts = build_dq_parts(row, hanger_offset)
    else:
        raise ValueError(f"build_style_chair: style {style.key!r} has its own path")
    names = tuple(o.name for o, _q in parts)
    tris = {o.name: cs2_asset.tri_count(o) for o, _q in parts}
    if names != style.parts:
        raise RuntimeError(f"C9 part list ({style.key}): built {names}, contract {style.parts}")
    want = dict(style.part_tris)
    bad = {n: (tris[n], want[n]) for n in names if tris[n] != want[n]}
    if bad:
        raise RuntimeError(f"C9 part tally ({style.key}): built vs contract {bad}")
    quads = {o.name: q for o, q in parts}
    chair, table = _assemble(parts, name, style.density, shade=shade, density_w=True,
                             flat_parts=FLAT_PARTS.get(style.key, ()))
    return chair, {"table": table, "quads": quads, "part_tris": tris}


# --------------------------------------------------------------------------------------
# Geometry contracts (dq-chair.md section 8) - every one raises on failure
# --------------------------------------------------------------------------------------

class ContractError(RuntimeError):
    pass


def _fail(cid, level, msg):
    raise ContractError(f"CONTRACT {cid} BROKEN ({level}): {msg}")


def authoring_coords(obj):
    """(n, 3) float64 vertex positions in the AUTHORING frame (x travel, y outboard, z up)
    of a net-oriented object: orient_for_net maps (x, y, z) -> (y, -x, z)."""
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3).astype(np.float64)
    m = np.array(obj.matrix_world, dtype=np.float64)
    w = co @ m[:3, :3].T + m[:3, 3]
    return np.stack([-w[:, 1], w[:, 0], w[:, 2]], axis=1)


def face_parts(obj):
    """Per-polygon part index (1-based into the part table)."""
    me = obj.data
    attr = me.attributes.get("part")
    if attr is None:
        raise RuntimeError(f"{obj.name}: no 'part' face attribute")
    out = np.empty(len(me.polygons), dtype=np.int32)
    attr.data.foreach_get("value", out)
    return out


def part_table(obj):
    return json.loads(obj.data[cs2_asset.PART_TABLE_PROP])


def _part_ids(table, prefix, exact=False):
    return {i + 1 for i, n in enumerate(table) if (n == prefix if exact else n.startswith(prefix))}


def _verts_of_parts(obj, ids):
    me = obj.data
    fp = face_parts(obj)
    out = set()
    for poly, p in zip(me.polygons, fp):
        if int(p) in ids:
            out.update(poly.vertices)
    return np.array(sorted(out), dtype=np.int64)


def _parts_touching(obj, vmask):
    """Part ids of every face using a vertex flagged in `vmask`."""
    me = obj.data
    fp = face_parts(obj)
    hit = set()
    for poly, p in zip(me.polygons, fp):
        if any(vmask[v] for v in poly.vertices):
            hit.add(int(p))
    return hit


def check_contracts(obj, style, row, level, hanger_offset=HANGER_OFFSET, info=None):
    """
    C1-C9 (C5/C7/C9 on LOD0) on one level of a net-oriented chair, `level` 0 / 1 / 2. The dq LOD0 adds C13 (bar
    mechanics) and C14 (Revision 1c: the carriage's big faces shade flat). Returns {cid: detail}. Raises ContractError on
    the first failure.
    """
    res = {}
    lv = f"LOD{level}"
    hang = float(row["hang_m"])
    rope = hang - float(row["grip_top_above_rope_m"])
    table = part_table(obj)
    a = authoring_coords(obj)
    z = a[:, 2]

    # C1 height contract on the actual vertices
    lo, hi = float(z.min()), float(z.max())
    if abs(lo) > 1e-5 or abs(hi - hang) > 1e-4:
        _fail("C1", lv, f"z {lo:.5f} .. {hi:.5f}; must be 0 .. {hang:.3f}")
    res["C1"] = f"z 0 .. {hi:.4f} (hang {hang:.3f})"

    # C2 the mesh top belongs to the grip part only (exact name: tr's "grip" is the jaw block. Its gripjaw / gripspring /
    # gripplate_ siblings must stay below the top)
    gid = _part_ids(table, style.grip_part, exact=True)
    top_parts = _parts_touching(obj, z >= hang - 1e-4)
    if not top_parts or not top_parts <= gid:
        _fail("C2", lv, f"vertices at the top belong to {sorted(table[i - 1] for i in top_parts)}")
    res["C2"] = f"top owned by {style.grip_part}"

    # C3 rope point inside the grip part's bbox. The dq jaw sits on the rope (LOD0/1). ENVELOPE: the grip part is
    # outboard of the wheels, and the jaw beside it (ENVELOPE_JAW) holds the rope: their union
    cid = set(gid)
    if ENVELOPE and style.key in ENVELOPE_JAW:
        cid |= _part_ids(table, ENVELOPE_JAW[style.key], exact=True)
    gv = _verts_of_parts(obj, cid)
    g = a[gv]
    gl, gh = g.min(axis=0), g.max(axis=0)
    if not (gl[0] <= 0.0 <= gh[0] and gl[1] <= 0.0 <= gh[1] and gl[2] <= rope <= gh[2]):
        _fail("C3", lv, f"rope point (0, 0, {rope:.3f}) outside {style.grip_part} bbox "
                        f"{np.round(gl, 3)} .. {np.round(gh, 3)}")
    res["C3"] = f"rope z {rope:.3f} inside {style.grip_part}"
    if style.key == "dq" and level in (0, 1):
        jv = _verts_of_parts(obj, _part_ids(table, "jaw_rope"))
        jb = float(a[jv, 2].min()) if len(jv) else float("nan")
        if not (rope - 1e-4 <= jb <= rope + 0.03):
            _fail("C3", lv, f"jaw_rope bottom {jb:.4f} not in [{rope:.3f}, {rope + 0.03:.3f}]")
        res["C3"] += f", jaw bottom {jb:.4f}"

    # C4 the lowest vertices belong to the bottom part only (dq / fg6l: the footbar. tr, Revision 1 without a footrest:
    # the seat plank - "seat" also names the LOD2 seatblock)
    low_parts = _parts_touching(obj, z <= 1e-5)
    fid = _part_ids(table, style.bottom_part)
    if not low_parts or not low_parts <= fid:
        _fail("C4", lv, f"vertices at z 0 belong to {sorted(table[i - 1] for i in low_parts)}")
    res["C4"] = f"bottom owned by {style.bottom_part}"

    # C5 ring counts (LOD0, the tube objects asserted them before the join)
    if level == 0:
        # tr: rings from the contracted tally (8 segments: 16 tris a span), 9 one-piece, 11 split (round 3)
        res["C5"] = (f"tube_stem {dict(style.part_tris)['tube_stem'] // 16 + 1} rings" if style.key == "tr"
                     else "tube_hanger 10, tube_hoop 14 rings")

    # C6 passing clearance and mast clearance from the chair width
    width = float(a[:, 1].max() - a[:, 1].min())
    lane = float(row["lane_x_m"])
    mast = float(row["mast_r_m"])
    passing, mast_gap = 2.0 * lane - width, lane - width / 2.0
    if passing < 1.0 or mast_gap < mast + 0.3:
        _fail("C6", lv, f"width {width:.3f}: passing {passing:.3f} (>= 1.0), mast gap "
                        f"{mast_gap:.3f} (>= {mast + 0.3:.2f})")
    res["C6"] = f"width {width:.3f}, passing {passing:.3f}, mast gap {mast_gap:.3f}"

    # C7 dq hanger opens to the outboard side (LOD0, bound = offset + r + 0.02, skeptic D7), measured OUTBOARD-positive
    # (nitpick A2: outboard = authoring DQ_OUTBOARD * y)
    if style.key == "dq" and level == 0:
        hv = a[_verts_of_parts(obj, _part_ids(table, "tube_hanger"))]
        yo = DQ_OUTBOARD * hv[:, 1]
        ymax = float(hanger_offset) + HANGER_R + 0.02
        if float(yo.min()) < -0.07 or float(yo.max()) > ymax:
            _fail("C7", lv, f"tube_hanger outboard y {yo.min():.3f} .. {yo.max():.3f} "
                            f"outside [-0.07, {ymax:.3f}] (authoring y {hv[:, 1].min():.3f} .. "
                            f"{hv[:, 1].max():.3f}, outboard sign {DQ_OUTBOARD:+.0f})")
        res["C7"] = (f"hanger y {hv[:, 1].min():.3f} .. {hv[:, 1].max():.3f} (outboard "
                     f"{yo.min():.3f} .. {yo.max():.3f}, outboard = {'+' if DQ_OUTBOARD > 0 else '-'}Y)")

    # C8 budgets
    tris = cs2_asset.tri_count(obj)
    if level == 0:
        if tris > style.lod0_cap or (style.part_tris and tris != style.lod0_tris):
            _fail("C8", lv, f"{tris} tris (tally {style.lod0_tris}, cap {style.lod0_cap})")
    elif level == 1:
        lod0 = info["lod0_tris"] if info else style.lod0_tris
        if abs(tris - style.lod1_target) > 0.25 * style.lod1_target or tris > 0.5 * lod0:
            _fail("C8", lv, f"{tris} tris vs target {style.lod1_target} +-25 %, <= 0.5 x {lod0}")
    else:
        if tris > style.lod2_cap:
            _fail("C8", lv, f"{tris} tris > cap {style.lod2_cap}")
        if info and info.get("proxy") and tris != style.lod2_tris:
            _fail("C8", lv, f"{tris} tris != proxy tally {style.lod2_tris}")
    res["C8"] = f"{tris} tris"

    # C9 part list (LOD0)
    if level == 0 and style.part_tris:
        if tuple(table) != style.parts:
            _fail("C9", lv, f"part table {table} != contract {style.parts}")
        res["C9"] = f"{len(table)} parts as contracted"

    # C13 (Revision 2) the restraint bar can actually be raised and lowered (LOD0, dq)
    if level == 0 and style.key == "dq":
        res["C13"] = check_bar_mechanics(obj, row)["summary"]

    # C14 (Revision 1c) the flat parts' big faces shade flat: every corner normal within FLAT_FACE_MAX_DEG of its face
    # normal (LOD0, the dq carriage)
    if level == 0 and FLAT_PARTS.get(style.key):
        rep = flat_face_report(obj, FLAT_PARTS[style.key])
        for n, (k, worst) in rep.items():
            if k < 6 or worst >= FLAT_FACE_MAX_DEG:
                _fail("C14", lv, f"{n}: {k} big faces, corner normals bend up to {worst:.1f} deg "
                                 f"off the face normal (want >= 6 faces, < {FLAT_FACE_MAX_DEG} deg): "
                                 f"the carriage shades like a rounded can")
        res["C14"] = "; ".join(f"{n} {k} big faces flat (corner normals <= {w:.2f} deg off)"
                               for n, (k, w) in rep.items())
    return res


# --------------------------------------------------------------------------------------
# C13 bar mechanics (Revision 2, with the riders added in Revision 3): sweep the restraint about its hinge, DOWN -> UP,
# past the chair AND a seated-rider envelope
# --------------------------------------------------------------------------------------

BAR_MOVING = ("barlink", "footstem_", "footbar_")   # the rigid bar assembly (arms + lap bar + T's)
BAR_HINGE = "barhinge_"                             # the bosses = the hinge itself (not obstacles)
C13_STEPS = 12                                      # the contract's 12 steps DOWN -> UP ...
C13_SUB = 4                                         # ... each checked at 4 sub-poses (a 10 deg step
                                                    # moves a stem 9 cm at 0.5 m from the hinge, as much as the top bar
                                                    # is thick. 4 sub-poses: 2 cm)
C13_CLEAR_M = 0.010                                 # [D] min vertex-to-surface clearance, both ways
C13_RIDER_CLEAR_M = 0.010                           # [D] the same against the seated-rider envelope
# The rider (RIDER, SEATED_HEAD_M, SEATED_FACE_M) is defined next to the bar's photo numbers above. "UP" means two
# things. The lap bar's bottom is above the backrest top AND a seated head top (cushion + SEATED_HEAD_M). Its center is
# over the seat (no further forward than the cushion front, where the riders reach up for it). Revision 2's "no further
# forward than the face" cannot hold with the lap bar ahead of the knees under this hoop (build report Revision 3). The
# rider envelope sweep now proves the bar never touches a head, face, chest or thigh on the way.


def _part_vert_sets(obj, fp):
    """{part id: sorted vertex indices} in one pass over the polygons."""
    out = collections.defaultdict(set)
    for poly, p in zip(obj.data.polygons, fp):
        out[int(p)].update(poly.vertices)
    return {k: np.array(sorted(v), dtype=np.int64) for k, v in out.items()}


def bar_rotate(pts, hinge_xz, deg):
    """Rotate authoring points (n, 3) about the lateral axis through hinge (x, z) by `deg`
    (positive = UP: the lap bar rises in front of the riders and swings back over them)."""
    hx, hz = hinge_xz
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    out = np.array(pts, dtype=np.float64, copy=True)
    dx, dz = out[:, 0] - hx, out[:, 2] - hz
    out[:, 0] = hx + c * dx - s * dz
    out[:, 2] = hz + s * dx + c * dz
    return out


def _pad_front_x(z, pad_edge):
    """x of the pads' front plane (hinged at pad_edge, tilted back PAD_TILT_DEG) at height z."""
    ex, ez = pad_edge
    return ex - (z - ez) * math.tan(math.radians(PAD_TILT_DEG))


def _box_faces(base):
    """Six outward quads of an 8-corner box listed bottom ring then top ring (both rings
    counter-clockwise seen from +Z)."""
    return [(base, base + 3, base + 2, base + 1), (base + 4, base + 5, base + 6, base + 7),
            (base, base + 1, base + 5, base + 4), (base + 1, base + 2, base + 6, base + 5),
            (base + 2, base + 3, base + 7, base + 6), (base + 3, base, base + 4, base + 7)]


def rider_envelope(seat_top, pad_edge, y_limit=None):
    """
    C13's seated-rider envelope (Revision 3), authoring frame. Each seat (RIDER["seats_y"]) has two boxes:
    - an upper-body box from the cushion top to the head top (SEATED_HEAD_M), with its back on the pads' front plane and
      its front SEATED_FACE_M ahead of it (sheared with the pad tilt)
    - a thigh box from the pads to the knee line (RIDER knee) up to the thigh top

    `y_limit` clips the outer riders laterally (the arm planes minus RIDER side_gap). Returns (verts, quads, labels): six
    quads per box, labels[i // 6] names the box of quad i. Shins and boots are left out: the riders lift their feet as
    the bar rises, and the foot bars move forward-up, away from the boots.
    """
    verts, quads, labels = [], [], []
    s0, s1 = seat_top, seat_top + RIDER["head"]
    for k, yc in enumerate(RIDER["seats_y"]):
        for what, hw in (("body", RIDER["half_w"]), ("thighs", RIDER["thigh_half_w"])):
            ya, yb = yc - hw, yc + hw
            if y_limit is not None:
                ya, yb = max(ya, -y_limit), min(yb, y_limit)
            if what == "body":
                rings = [(z, _pad_front_x(z, pad_edge), _pad_front_x(z, pad_edge) + RIDER["face"])
                         for z in (s0, s1)]
            else:
                xa = _pad_front_x(s0, pad_edge)
                rings = [(z, xa, xa + RIDER["knee"]) for z in (s0, s0 + RIDER["thigh"])]
            base = len(verts)
            for z, xa, xb in rings:
                verts += [(xa, ya, z), (xb, ya, z), (xb, yb, z), (xa, yb, z)]
            quads += _box_faces(base)
            labels.append(f"rider {k + 1} {what}")
    return verts, quads, labels


def check_bar_mechanics(obj, row, swing_deg=None, steps=C13_STEPS, sub=C13_SUB, verbose=True):
    """
    C13 on a net-oriented dq LOD0 (dq-chair Revision 2, the riders in Revision 3). The check measures from the mesh, not
    the datums:
    - The hinge axis = the two barhinge_ bosses (coaxial, lateral, one on each side frame). Each boss's outer end must be
      INSIDE the hoop tube: within r of its surface and behind it (Revision 3), not merely near it.
    - The bar assembly (BAR_MOVING) must be attached at the axis (the U's end rings centered on it).
    - DOWN (Revision 3, verdicts 2' / 2'' "too high"): the lap bar's center sits LAP_WINDOW_M above the cushion top, and
      its back face is ahead of the riders' knee line.

    The assembly is then rotated about the axis from DOWN (0) to UP (swing_deg, default DQ_BAR) in `steps` steps, each
    checked at `sub` sub-poses. At every pose these conditions must hold:
    - The BVH overlap against every other part except the bosses (backrest, side frames, armrests, cushions, seat pan,
      hanger, clamp, plate, grip) AND against the seated-rider envelope (rider_envelope: body and thighs of four riders)
      must be empty.
    - The vertex-to-surface clearance (both ways, an upper bound on the true gap, edge-edge distances are not sampled)
      must be >= C13_CLEAR_M to the chair and >= C13_RIDER_CLEAR_M to the riders.

    UP must park the lap bar's bottom above the backrest top and a seated head (cushion top + SEATED_HEAD_M), with its
    center over the seat (x <= the cushion front). Reported, not asserted: how many degrees past UP the first contact
    happens (the mechanical stop) and how far UP sits ahead of the face line. Returns {summary, ...}. Raises
    ContractError on any failure.
    """
    from mathutils.bvhtree import BVHTree
    lv = "LOD0"
    swing = float(DQ_BAR["swing_deg"] if swing_deg is None else swing_deg)
    table = part_table(obj)
    a = authoring_coords(obj)
    fp = face_parts(obj)
    polys = [tuple(p.vertices) for p in obj.data.polygons]
    vsets = _part_vert_sets(obj, fp)

    def ids_of(prefix):
        return _part_ids(table, prefix)

    # -- the hinge axis, from the bosses ------------------------------------------------
    hid = sorted(ids_of(BAR_HINGE))
    if len(hid) != 2:
        _fail("C13", lv, f"{len(hid)} hinge bosses ({BAR_HINGE}*), expected 2")
    centres, yext = [], []
    for i in hid:
        b = a[vsets[i]]
        lo, hi = b.min(axis=0), b.max(axis=0)
        centres.append(((lo[0] + hi[0]) / 2.0, (lo[2] + hi[2]) / 2.0))
        yext.append((float(lo[1]), float(hi[1])))
    (x0, z0), (x1, z1) = centres
    if abs(x0 - x1) > 1e-4 or abs(z0 - z1) > 1e-4:
        _fail("C13", lv, f"hinge bosses not coaxial: (x, z) {centres}")
    hinge = ((x0 + x1) / 2.0, (z0 + z1) / 2.0)
    sides = sorted(yext)
    if not (sides[0][1] < -0.5 and sides[1][0] > 0.5):
        _fail("C13", lv, f"hinge bosses not one per side frame: y {sides}")
    # each boss's outer end must sit INSIDE the hoop (the side frame): not on the hanger and not floating beside the leg.
    # Within r of the hoop surface AND behind it - the nearest hoop face's outward normal points away from the end
    # (Revision 3, revision2-check 2'-f: a boss slid 8 cm inboard used to pass on distance alone)
    hoop_ids = ids_of("tube_hoop")
    hoop_polys = [polys[k] for k in range(len(polys)) if int(fp[k]) in hoop_ids]
    hoop_tree = BVHTree.FromPolygons([tuple(v) for v in a], hoop_polys)
    for y_end in (sides[0][0], sides[1][1]):
        p = mathutils.Vector((hinge[0], y_end, hinge[1]))
        hit = hoop_tree.find_nearest(p)
        inside = hit[0] is not None and (p - hit[0]).dot(hit[1]) < 0.0
        if hit[0] is None or hit[3] > DQ_R["hoop"] + 1e-3 or not inside:
            _fail("C13", lv, f"hinge boss end (x {hinge[0]:.3f}, y {y_end:.3f}, z {hinge[1]:.3f}) "
                             f"is {hit[3] if hit[0] is not None else 'inf'} m from the side frame, "
                             f"{'inside' if inside else 'OUTSIDE'} it (must be inside the hoop "
                             f"tube, r {DQ_R['hoop']})")

    # -- the moving assembly and the obstacles ------------------------------------------
    mid = set()
    for prefix in BAR_MOVING:
        mid |= ids_of(prefix)
    if not mid:
        _fail("C13", lv, f"no bar assembly parts {BAR_MOVING}")
    mfaces = [k for k in range(len(polys)) if int(fp[k]) in mid]
    sfaces = [k for k in range(len(polys)) if int(fp[k]) not in mid and int(fp[k]) not in hid]
    mverts = np.array(sorted({v for k in mfaces for v in polys[k]}), dtype=np.int64)
    sverts = np.array(sorted({v for k in sfaces for v in polys[k]}), dtype=np.int64)
    remap = {int(v): n for n, v in enumerate(mverts)}
    mpolys = [tuple(remap[v] for v in polys[k]) for k in mfaces]
    static_tree = BVHTree.FromPolygons([tuple(v) for v in a], [polys[k] for k in sfaces])
    s_pts = [tuple(v) for v in a[sverts]]
    part_of_face = {n: table[int(fp[k]) - 1] for n, k in enumerate(sfaces)}

    # attached: the U's end rings are centered on the hinge axis, inside the bosses' y range
    u = a[vsets[next(iter(ids_of("barlink")))]]
    dxz = np.hypot(u[:, 0] - hinge[0], u[:, 2] - hinge[1])
    ring = u[dxz < DQ_R["barlink"] + 0.002]
    for lo, hi in sides:
        sel = ring[(ring[:, 1] >= lo - 1e-6) & (ring[:, 1] <= hi + 1e-6)]
        if len(sel) < 3 or float(np.hypot(sel[:, 0].mean() - hinge[0],
                                         sel[:, 2].mean() - hinge[1])) > 2e-3:
            _fail("C13", lv, f"the bar is not attached at the hinge (x {hinge[0]:.3f}, z "
                             f"{hinge[1]:.3f}) inside boss y {lo:.3f}..{hi:.3f}")

    # -- DOWN sits on the riders' laps, ahead of the knees (Revision 3). The lap bar = the
    # U's straight lateral run (its two end rings sit where the corner fillets end)
    lap = u[np.abs(u[:, 1]) <= DQ_BAR["arm_y"] - DQ_BAR["corner"] + 1e-3]
    if len(lap) < 6:
        _fail("C13", lv, f"lap bar run not found ({len(lap)} vertices at |y| <= "
                         f"{DQ_BAR['arm_y'] - DQ_BAR['corner']:.3f})")
    down_c = (float(lap[:, 0].mean()), float(lap[:, 2].mean()))
    seat_top = float(max(a[vsets[i]][:, 2].max() for i in ids_of("cushion_")))
    pad_edge = dq_datums(row)["pad_edge"]
    above = down_c[1] - seat_top
    if not (LAP_WINDOW_M[0] <= above <= LAP_WINDOW_M[1]):
        _fail("C13", lv, f"the lap bar DOWN sits {above:.3f} above the cushion, outside "
                         f"{LAP_WINDOW_M[0]:.2f}-{LAP_WINDOW_M[1]:.2f} (2026-10-11: 0.35 "
                         f"was too high; the AL rider photo measures {PHOTO_LAP_ABOVE_CUSHION_M:.2f})")
    knee_x = _pad_front_x(seat_top, pad_edge) + RIDER["knee"]
    back_face = float(lap[:, 0].min())
    if back_face < knee_x:
        _fail("C13", lv, f"the lap bar DOWN (back face x {back_face:.3f}) is not ahead of the "
                         f"riders' knees (x {knee_x:.3f}): it must cross in front of them")

    # -- the seated riders (an obstacle envelope, never exported) -------------------------
    y_lim = DQ_BAR["arm_y"] - DQ_R["barlink"] - RIDER["side_gap"]
    rv, rq, rlabels = rider_envelope(seat_top, pad_edge, y_lim)
    rider_tree = BVHTree.FromPolygons(rv, rq)
    r_pts = [tuple(v) for v in rv]

    # -- sweep ---------------------------------------------------------------------------
    m0 = a[mverts]
    n = steps * sub
    worst = (float("inf"), None, None)
    worst_r = (float("inf"), None, None)
    worst_b = (float("inf"), None, None)       # report: the closest pass to a head / torso
    # the rider clearance is measured on the parts that pass IN FRONT OF / OVER the riders: the lap bar run and the T's
    # (|y| <= arm_y - corner). The arms run beside the outer riders at the constructed RIDER side_gap. The overlap test
    # still covers them
    inner = np.abs(m0[:, 1]) <= DQ_BAR["arm_y"] - DQ_BAR["corner"] + 1e-3
    ipolys = [q for q in mpolys if all(inner[v] for v in q)]
    for j in range(n + 1):
        deg = swing * j / n
        mp = bar_rotate(m0, hinge, deg)
        mtree = BVHTree.FromPolygons([tuple(v) for v in mp], mpolys)
        pairs = static_tree.overlap(mtree)
        if pairs:
            hit = sorted({part_of_face[i] for i, _j in pairs})
            _fail("C13", lv, f"the bar assembly hits {hit} at {deg:.1f} deg of {swing:.1f} "
                             f"(step {j / sub:.2f} of {steps}; hinge x {hinge[0]:.3f} z {hinge[1]:.3f})")
        rpairs = rider_tree.overlap(mtree)
        if rpairs:
            hit = sorted({rlabels[i // 6] for i, _j in rpairs})
            _fail("C13", lv, f"the bar assembly runs through a seated rider ({', '.join(hit)}) at "
                             f"{deg:.1f} deg of {swing:.1f} (hinge x {hinge[0]:.3f} z {hinge[1]:.3f})")
        for p in mp:
            h = static_tree.find_nearest(tuple(p))
            if h[0] is not None and h[3] < worst[0]:
                worst = (float(h[3]), deg, part_of_face[h[2]])
        for p in mp[inner]:
            h = rider_tree.find_nearest(tuple(p))
            if h[0] is not None and h[3] < worst_r[0]:
                worst_r = (float(h[3]), deg, rlabels[h[2] // 6])
            if h[0] is not None and rlabels[h[2] // 6].endswith("body") and h[3] < worst_b[0]:
                worst_b = (float(h[3]), deg, rlabels[h[2] // 6])
        for p in s_pts:
            h = mtree.find_nearest(p)
            if h[0] is not None and h[3] < worst[0]:
                worst = (float(h[3]), deg, "static vertex")
        itree = BVHTree.FromPolygons([tuple(v) for v in mp], ipolys)
        for k, p in enumerate(r_pts):
            h = itree.find_nearest(p)
            if h[0] is not None and h[3] < worst_r[0]:
                worst_r = (float(h[3]), deg, f"{rlabels[k // 8]} corner")
            if h[0] is not None and rlabels[k // 8].endswith("body") and h[3] < worst_b[0]:
                worst_b = (float(h[3]), deg, f"{rlabels[k // 8]} corner")
    if worst[0] < C13_CLEAR_M:
        _fail("C13", lv, f"clearance {worst[0]:.4f} m < {C13_CLEAR_M} at {worst[1]:.1f} deg "
                         f"(nearest {worst[2]})")
    if worst_r[0] < C13_RIDER_CLEAR_M:
        _fail("C13", lv, f"rider clearance {worst_r[0]:.4f} m < {C13_RIDER_CLEAR_M} at "
                         f"{worst_r[1]:.1f} deg (nearest {worst_r[2]})")
    # report only: how far past UP the assembly could swing before it first touches (the
    # stop: on this hoop the lap bar comes up against the hanger clamp)
    stop = None
    for j in range(1, 61):
        deg = swing + 0.5 * j
        mtree = BVHTree.FromPolygons([tuple(v) for v in bar_rotate(m0, hinge, deg)], mpolys)
        pairs = static_tree.overlap(mtree)
        if pairs:
            stop = (0.5 * j, sorted({part_of_face[i] for i, _j in pairs}))
            break

    # -- UP parks over the backrest, over a seated head, over the seat -------------------
    up = bar_rotate(lap, hinge, swing)
    up_c = (float(up[:, 0].mean()), float(up[:, 2].mean()))
    back_ids = ids_of("pad_") | ids_of("backboard")
    back_top = float(max(a[vsets[i]][:, 2].max() for i in back_ids))
    up_low = float(up[:, 2].min())
    head = seat_top + SEATED_HEAD_M
    if not (up_low > back_top and up_low > head):
        _fail("C13", lv, f"UP ({swing:.1f} deg) parks the lap bar at z {up_low:.3f} (bottom): "
                         f"must be above the backrest top {back_top:.3f} and a seated head {head:.3f}")
    seat_front = float(max(a[vsets[i]][:, 0].max() for i in ids_of("cushion_")))
    if up_c[0] > seat_front:
        _fail("C13", lv, f"UP ({swing:.1f} deg) parks the lap bar at x {up_c[0]:.3f}, ahead of the "
                         f"seat front (x {seat_front:.3f}): out of the riders' reach")
    face_x = _pad_front_x(up_c[1], pad_edge) + SEATED_FACE_M
    summary = (f"bar swings {swing:.0f} deg about the side-frame hinge (x {hinge[0]:.3f}, z "
               f"{hinge[1]:.3f}, {hinge[1] - seat_top:.3f} above the cushion) in {steps} steps x "
               f"{sub}: no contact, min clearance {worst[0]:.3f} m at {worst[1]:.0f} deg ({worst[2]}), "
               f"riders {worst_r[0]:.3f} m at {worst_r[1]:.0f} deg ({worst_r[2]}; heads / torsos "
               f"{worst_b[0]:.3f} at {worst_b[1]:.0f} deg); DOWN lap bar "
               f"{above:.3f} above the cushion, {back_face - knee_x:.3f} ahead of the knees; UP lap "
               f"bar at x {up_c[0]:.3f} z {up_c[1]:.3f}, bottom {up_low:.3f} ({up_low - seat_top:.3f} "
               f"above the cushion; head top {head:.3f}, backrest top {back_top:.3f}, "
               f"{up_c[0] - face_x:+.3f} vs the face line); "
               + (f"first contact {stop[0]:.1f} deg past UP ({', '.join(stop[1])})" if stop
                  else "no contact within 30 deg past UP"))
    if verbose:
        print(f"[chair] C13 {summary}")
    return {"summary": summary, "hinge": hinge, "swing_deg": swing, "clearance_m": worst[0],
            "clearance_at_deg": worst[1], "clearance_part": worst[2],
            "rider_clearance_m": worst_r[0], "rider_clearance_at_deg": worst_r[1],
            "rider_clearance_part": worst_r[2], "body_clearance_m": worst_b[0],
            "body_clearance_at_deg": worst_b[1], "down": down_c, "down_above_cushion": above,
            "ahead_of_knees": back_face - knee_x, "knee_x": knee_x, "seat_top": seat_top,
            "up": up_c, "up_bottom": up_low, "backrest_top": back_top, "head": head,
            "face_x": face_x, "seat_front": seat_front,
            "stop_margin_deg": stop[0] if stop else None,
            "stop_parts": stop[1] if stop else None}


def bar_pose_copy(obj, hinge_xz, deg, name):
    """
    A linked-in duplicate of a net-oriented dq LOD0 with the bar assembly (BAR_MOVING)
    rotated `deg` about the lateral hinge axis through hinge_xz (authoring x, z), re-shaded
    (shade_by_angle recomputes the weighted normals of the moved parts). Preview only: the
    caller removes it. The source object is not touched.
    """
    dup = obj.copy()
    dup.data = obj.data.copy()
    dup.name = name
    dup.data.name = name
    bpy.context.collection.objects.link(dup)
    table = part_table(dup)
    ids = set()
    for prefix in BAR_MOVING:
        ids |= _part_ids(table, prefix)
    vs = _verts_of_parts(dup, ids)
    ar = bar_rotate(authoring_coords(dup)[vs], hinge_xz, deg)
    w = np.stack([ar[:, 1], -ar[:, 0], ar[:, 2]], axis=1)       # authoring -> world (orient_for_net)
    inv = np.array(dup.matrix_world.inverted(), dtype=np.float64)
    loc = w @ inv[:3, :3].T + inv[:3, 3]
    me = dup.data
    for i, p in zip(vs, loc):
        me.vertices[int(i)].co = tuple(p)
    me.update()
    cs2_asset.shade_by_angle(dup, SHADE_ANGLE)
    return dup


# --------------------------------------------------------------------------------------
# Nitpick A2 (2026-10-13): the carrier sweep - a chair along its lane through a sheave train
# (tests/test_dq_family_seams.py / test_fg_family_seams.py S9, build/nitpick-a2-hanger-gondola.md)
# --------------------------------------------------------------------------------------
# Frames: every input is AUTHORING (x travel, y lateral, z up). The chair rides lane s (y = s * lane) with its GRIP TOP
# on the lane height z_lane(x) (ChairPin, round-2 contract D8: the grip top stays ON the lane for every type). It faces f
# = -s along x. A chair facing its travel has the outboard side on its RIGHT on both lanes (chain A = the right lane
# looking uphill, see the module docstring "OUTBOARD side"). So chair (xc, yc, zc) -> (x0 + f xc, s lane + f yc,
# z_lane(x0) - hang + zc). `facing=+1` sweeps the opposite spin (the old relation: the dq hanger inboard).
#
# The family rule (gated): no chair part crosses a tower / station part other than the F10 convention set below. The
# drawn rope runs through the sheave centers (dq-family-review-geometry F10). The set has these parts: rims, liners, the
# gantry heads' sub rockers + sub pins, the Yan heads' sheave arms + axle caps. Every gated pair, the rocker mains first,
# keeps CARRIER_CLEAR_M (= C13_CLEAR_M, the chair's own moving-part clearance). Distances are vertex-to-surface both ways
# at every step (C13's method).
CARRIER_SWEEP_STEP_M = 0.01
CARRIER_CLEAR_M = 0.010
CARRIER_PASS = {
    "gantry": ("sheave_", "liner_", "rocker_sub_", "pivot_n_s", "pivot_p_s"),     # DQ / ETDQ / GD / ETGD
    "yan": ("sheave_", "liner_", "sheave_arm_", "axlecap_"),                      # FG / ET / the Pali hold-down
}
CARRIER_STATION_PREFIXES = ("sheave_guide_", "sheave_hold_")


def carrier_part_core(name):
    """A station train part's tower name (its station prefix removed)."""
    for pre in CARRIER_STATION_PREFIXES:
        if name.startswith(pre):
            return name[len(pre):]
    return name


def carrier_sweep(chair, target, lane, z_lane, facing=-1.0, step=CARRIER_SWEEP_STEP_M, near=0.25,
                  depth=1.9, window=0.9):
    """
    Sweep `chair` = (coords (n, 3), polys, part name per poly, hang) along both lanes +-`lane` of `target` = (coords,
    polys, part name per poly), every `step` m over the target's travel extent plus the chair's. Chair faces reaching
    within `depth` of the grip top take part. So do target faces with a vertex within `window` of the lane. Returns {s:
    {(chair part, target part): (d, x0)}} with d = the min vertex-to-surface distance (m, < near) or -1.0 when the two
    parts' triangles intersect at x0 (the first such step).
    """
    from mathutils.bvhtree import BVHTree
    cco, cpolys, cnames, hang = chair
    tco, tpolys, tnames = target
    cco, tco = np.asarray(cco, np.float64), np.asarray(tco, np.float64)
    cpolys = [tuple(int(v) for v in p) for p in cpolys]     # numpy ints silently build an empty BVH
    tpolys = [tuple(int(v) for v in p) for p in tpolys]
    keep = [k for k, p in enumerate(cpolys) if cco[list(p), 2].max() >= hang - depth]
    cp, cn = [cpolys[k] for k in keep], [cnames[k] for k in keep]
    cv = sorted({v for p in cp for v in p})
    vface_c = {}
    for fi, p in enumerate(cp):
        for v in p:
            vface_c.setdefault(v, fi)
    out = {}
    for s in (-1.0, 1.0):
        f = float(facing) * s
        tk = [k for k, p in enumerate(tpolys) if (np.abs(tco[list(p), 1] - s * lane) <= window).any()]
        res = {}
        out[s] = res
        if not tk:
            continue
        tp, tn = [tpolys[k] for k in tk], [tnames[k] for k in tk]
        tv = np.array(sorted({v for p in tp for v in p}), dtype=np.int64)
        vface_t = {}
        for fi, p in enumerate(tp):
            for v in p:
                vface_t.setdefault(v, fi)
        tb = BVHTree.FromPolygons([tuple(v) for v in tco], tp, epsilon=0.0)
        L = np.stack([f * cco[:, 0], s * lane + f * cco[:, 1], cco[:, 2] - hang], axis=1)
        tx0, tx1 = float(tco[tv, 0].min()), float(tco[tv, 0].max())
        cx0, cx1 = float(L[cv, 0].min()), float(L[cv, 0].max())
        n = int(math.floor(((tx1 - cx0) - (tx0 - cx1) + 0.10) / step)) + 1
        for k in range(n):
            x0 = round(tx0 - cx1 - 0.05 + k * step, 6)
            P = L + np.array([x0, 0.0, float(z_lane(x0))])
            cb = BVHTree.FromPolygons([tuple(v) for v in P], cp, epsilon=0.0)
            for i, j in tb.overlap(cb):
                key = (cn[j], tn[i])
                if res.get(key, (0.0,))[0] != -1.0:
                    res[key] = (-1.0, x0)
            lo, hi = P[cv].min(axis=0) - near, P[cv].max(axis=0) + near
            for v in cv:
                hit = tb.find_nearest(mathutils.Vector(P[v]), near)
                if hit[0] is None:
                    continue
                key = (cn[vface_c[v]], tn[hit[2]])
                r = res.get(key)
                if r is None or (r[0] != -1.0 and hit[3] < r[0]):
                    res[key] = (float(hit[3]), x0)
            sel = tv[((tco[tv] >= lo) & (tco[tv] <= hi)).all(axis=1)]
            for v in sel:
                hit = cb.find_nearest(mathutils.Vector(tco[v]), near)
                if hit[0] is None:
                    continue
                key = (cn[hit[2]], tn[vface_t[int(v)]])
                r = res.get(key)
                if r is None or (r[0] != -1.0 and hit[3] < r[0]):
                    res[key] = (float(hit[3]), x0)
    return out


def carrier_verdict(sweep, head, clear=CARRIER_CLEAR_M):
    """The family rule on a carrier_sweep result. Returns per lane {"mains": (d, chair part, part, x)
    the nearest rocker_main_ pair (None = none within reach), "gated": the nearest pair outside the
    F10 pass set, "fail": [pairs crossing or nearer than `clear`], "pass": sorted F10 crossings}."""
    allowed = CARRIER_PASS[head]
    out = {}
    for s, res in sweep.items():
        mains, gated, fail, ok = None, None, [], set()
        for (cp, tp), (d, x0) in res.items():
            core = carrier_part_core(tp)
            rec = (round(d, 4), cp, tp, x0)
            if core.startswith(allowed):
                if d < 0.0:
                    ok.add((cp, core))
                continue
            if core.startswith("rocker_main_") and (mains is None or d < mains[0]):
                mains = rec
            if gated is None or d < gated[0]:
                gated = rec
            if d < clear:
                fail.append(rec)
        out[s] = {"mains": mains, "gated": gated, "fail": sorted(fail), "pass": sorted(ok)}
    return out


def check_atlas_stage(chair, info, atlas_dir, name):
    """
    C11: atlas quads 2 and 3 carry the recolor albedo (0.80 +- 0.02 sRGB-encoded). Quad 3 is measured in its top quarter:
    below that, the shared recipe's grime gradient darkens it by up to 6 %. Every face of every part sits in its family's
    quad on "UVMap".
    """
    import cs2_validate
    px = cs2_validate.load_png_pixels(os.path.join(atlas_dir, f"{name}_BaseColor.png"))
    A = px.shape[0]
    h = A // 2
    q2 = px[h:, :h, 0:3]                         # bottom-up rows: quad 2 = u 0..0.5, v 0.5..1
    q3 = px[A - h // 4:, h:, 0:3]                # quad 3, top quarter
    m2, m3 = float(np.median(q2)), float(np.median(q3))
    ref = cs2_asset.RECOLOR_ALBEDO
    if abs(m2 - ref) > 0.02 or abs(m3 - ref) > 0.02:
        _fail("C11", "atlas", f"quad 2 median {m2:.3f}, quad 3 (top) {m3:.3f}; want {ref} +- 0.02")
    me = chair.data
    uv = me.uv_layers["UVMap"].data
    fp = face_parts(chair)
    table = info["table"]
    wrong = collections.Counter()
    for poly, p in zip(me.polygons, fp):
        u = sum(uv[li].uv[0] for li in poly.loop_indices) / poly.loop_total
        v = sum(uv[li].uv[1] for li in poly.loop_indices) / poly.loop_total
        q = (1 if u >= 0.5 else 0) + (2 if v >= 0.5 else 0)
        if q != info["quads"][table[p - 1]]:
            wrong[table[p - 1]] += 1
    if wrong:
        _fail("C11", "atlas", f"faces outside their quad: {dict(wrong)}")
    return {"C11": f"quad 2 {m2:.3f}, quad 3 {m3:.3f}; every face in its family quad"}


def check_finished_maps(chair, info, asset_dir, name, A, plate_prefix="plate_face",
                        plate_required=True):
    """
    C12 on the shipped maps, per texel of every face (cs2_compose.rasterise_face_fields on the shipped layer):
    - bench -> ControlMask B >= 0.99 (R 0)
    - paint -> R >= 0.99 except the plate
    - galvanized -> R = G = B = 0
    - G = 0 everywhere
    - BaseColor A 255
    - >= 20 dark (< 0.05 linear) texels inside the plate's UV rect (plate_required=False: the split GRIP, which carries
      no plate)
    """
    import cs2_compose
    import cs2_preview
    import cs2_validate
    me = chair.data
    layer = me.uv_layers.active.name
    part_map, _ppm = cs2_compose.rasterise_face_fields(me, A, layer)
    ctl = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{name}_ControlMask.png"))
    base = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{name}_BaseColor.png"))
    table = info["table"]
    fam = np.zeros(len(table) + 1, dtype=np.int8)       # 0 none, 1 G, 2 P, 3 B, 4 plate
    for i, n in enumerate(table, start=1):
        fam[i] = 4 if n.startswith(plate_prefix) else {"G": 1, "P": 2, "B": 3}[
            FAMILY_OF_QUAD[info["quads"][n]]]
    f = fam[part_map]
    tol = 0.99
    errs = []
    n_b, n_p, n_g = int((f == 3).sum()), int((f == 2).sum()), int((f == 1).sum())
    if (ctl[f == 3, 2] < tol).any() or (ctl[f == 3, 0] > 1e-3).any():
        errs.append(f"bench: {int((ctl[f == 3, 2] < tol).sum())} of {n_b} texels B < 0.99")
    if (ctl[f == 2, 0] < tol).any():
        errs.append(f"paint: {int((ctl[f == 2, 0] < tol).sum())} of {n_p} texels R < 0.99")
    if (ctl[f == 1, 0:3] > 1e-3).any():
        errs.append(f"galvanised: {int((ctl[f == 1, 0:3] > 1e-3).any(axis=-1).sum())} of "
                    f"{n_g} texels recolorable")
    if (ctl[..., 1] > 1e-3).any():
        errs.append(f"ControlMask G non-zero on {int((ctl[..., 1] > 1e-3).sum())} texels")
    if (base[..., 3] < 1.0 - 0.5 / 255.0).any():
        errs.append("BaseColor alpha < 255")
    rect = cs2_compose.uv_bbox_px(me, table, plate_prefix, A, layer)
    dark = 0
    if rect is not None:
        r0, r1, c0, c1 = rect
        lin = cs2_preview._srgb_to_linear(base[r0:r1, c0:c1, 0:3])
        dark = int((lin.max(axis=-1) < 0.05).sum())
    if plate_required and dark < 20:
        errs.append(f"plate: {dark} dark texels (< 0.05 linear), need >= 20")
    if errs:
        _fail("C12", "finish", "; ".join(errs))
    return {"C12": f"bench {n_b} / paint {n_p} / galv {n_g} texels as recolored; "
                   + (f"plate {dark} dark texels" if plate_required else "no plate (grip)")}


def _raster(tris2d, x0, y0, nx, ny, px):
    img = np.zeros((ny, nx), dtype=bool)
    for t in tris2d:
        mn, mx = t.min(axis=0), t.max(axis=0)
        i0 = max(int(math.floor((mn[0] - x0) / px)), 0)
        i1 = min(int(math.ceil((mx[0] - x0) / px)), nx)
        j0 = max(int(math.floor((mn[1] - y0) / px)), 0)
        j1 = min(int(math.ceil((mx[1] - y0) / px)), ny)
        if i1 <= i0 or j1 <= j0:
            continue
        X, Y = np.meshgrid(x0 + (np.arange(i0, i1) + 0.5) * px, y0 + (np.arange(j0, j1) + 0.5) * px)
        (ax, ay), (bx, by), (cx, cy) = t
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-14:
            continue
        l1 = ((by - cy) * (X - cx) + (cx - bx) * (Y - cy)) / den
        l2 = ((cy - ay) * (X - cx) + (ax - cx) * (Y - cy)) / den
        img[j0:j1, i0:i1] |= (l1 >= -1e-9) & (l2 >= -1e-9) & (1.0 - l1 - l2 >= -1e-9)
    return img


def _dilate(m):
    out = m.copy()
    out[1:, :] |= m[:-1, :]
    out[:-1, :] |= m[1:, :]
    out[:, 1:] |= m[:, :-1]
    out[:, :-1] |= m[:, 1:]
    return out


def _tris_auth(obj):
    a = authoring_coords(obj)
    me = obj.data
    me.calc_loop_triangles()
    idx = np.array([lt.vertices[:] for lt in me.loop_triangles], dtype=np.int64)
    return a[idx]                                       # (n, 3, 3)


SIL_PX = 0.02
# (cover >=, extra <=) per level. LOD1 = the contract's [D] 0.60 / 0.10. The AUTHORED LOD2 was tuned on the first build
# (dq-chair.md C10 "[D, tune on first previews]"). By design it draws the restraint as one straight link per side and has
# no clamp block. Measured: dq side/bench extra 0.21 and side/topbar cover 0.58 (tr 0.87 / 0.01). At its ~100 m switch
# distance one screen pixel is ~8 cm, four raster pixels here.
SIL_LIMITS = {1: (0.60, 0.10), 2: (0.55, 0.25)}


def silhouette_windows(style, hanger_offset=HANGER_OFFSET):
    """C10 windows (authoring): name -> (y range, z range)."""
    if style.key == "dq":
        o = float(hanger_offset)
        hy = (o - 0.15, o + 0.10)                 # outboard-positive (nitpick A2: DQ_OUTBOARD)
        if DQ_OUTBOARD < 0.0:
            hy = (-hy[1], -hy[0])
        return {"bench": ((-1.2, 1.2), (0.23, 0.95)),
                "hanger": (hy, (1.70, 3.05)),
                "topbar": ((-1.0, 1.0), (1.45, 1.55))}
    # tr: the FG bench box, post-pivot (Revision 1: the plank bottom is z 0, 0.09 lower
    # than the 6L footbar-closed frame the original window 0.09-0.68 was drawn for)
    return {"bench": ((-0.9, 0.9), (0.00, 0.59))}


def check_silhouettes(lod0, lods, style, hanger_offset=HANGER_OFFSET):
    """
    C10: numpy raster at 0.02 m of the front (lateral-up) and side (travel-up) projections. Per window and LODk: cover =
    |dilate(LODk) & LOD0| / |LOD0| and extra = |LODk minus dilate(LOD0)| / |LOD0| against SIL_LIMITS[k] (one-pixel
    tolerance both ways, the side projection uses the window's z range only). Returns the table. Raises with every
    failing window listed.
    """
    t0 = _tris_auth(lod0)
    res = {}
    for k, lod in sorted(lods.items()):
        tk = _tris_auth(lod)
        for proj, ax in (("front", 1), ("side", 0)):
            p0 = t0[:, :, [ax, 2]]
            pk = tk[:, :, [ax, 2]]
            allp = np.concatenate([p0.reshape(-1, 2), pk.reshape(-1, 2)])
            x0, y0 = allp.min(axis=0) - 2 * SIL_PX
            x1, y1 = allp.max(axis=0) + 2 * SIL_PX
            nx, ny = int(math.ceil((x1 - x0) / SIL_PX)), int(math.ceil((y1 - y0) / SIL_PX))
            r0 = _raster(p0, x0, y0, nx, ny, SIL_PX)
            rk = _raster(pk, x0, y0, nx, ny, SIL_PX)
            xs = x0 + (np.arange(nx) + 0.5) * SIL_PX
            ys = y0 + (np.arange(ny) + 0.5) * SIL_PX
            for wname, (yr, zr) in silhouette_windows(style, hanger_offset).items():
                win = np.zeros((ny, nx), dtype=bool)
                zsel = (ys >= zr[0]) & (ys <= zr[1])
                xsel = np.ones(nx, dtype=bool) if proj == "side" else \
                    (xs >= yr[0]) & (xs <= yr[1])
                win[np.ix_(zsel, xsel)] = True
                base_n = int((r0 & win).sum())
                if base_n == 0:
                    continue
                cover = float((_dilate(rk) & r0 & win).sum()) / base_n
                extra = float((rk & ~_dilate(r0) & win).sum()) / base_n
                res[f"LOD{k}/{proj}/{wname}"] = (round(cover, 3), round(extra, 3))
    def limits(key):
        return SIL_LIMITS[min(int(key.split("/")[0][3:]), max(SIL_LIMITS))]
    bad = {key: v for key, v in res.items()
           if v[0] < limits(key)[0] or v[1] > limits(key)[1]}
    if bad:
        _fail("C10", "/".join(sorted({key.split("/")[0] for key in bad})),
              f"(cover, extra) limits {SIL_LIMITS} failed {bad}; all {res}")
    return res


# --------------------------------------------------------------------------------------
# Height pin + LOD2 proxy
# --------------------------------------------------------------------------------------

def assert_height_contract(obj, label="chair", tol=1e-4, height=HEIGHT_CONTRACT):
    """
    Grip-top -> lowest point = `height` (2.400 for the FG chair), measured from the ACTUAL vertices (world space), with
    the lowest point at z = 0. Raises on any drift. Returns (lo, hi).
    """
    zs = [(obj.matrix_world @ v.co).z for v in obj.data.vertices]
    lo, hi = min(zs), max(zs)
    if abs(lo) > 1e-5 or abs((hi - lo) - height) > tol:
        raise RuntimeError(
            f"HEIGHT CONTRACT BROKEN ({label}): z {lo:.5f} .. {hi:.5f} = {hi - lo:.5f} m "
            f"grip-top to lowest point, must be exactly {height:.3f} with the "
            f"lowest point at 0 (the game positions chairs by this height).")
    return lo, hi


def pin_height(obj, lo=0.0, hi=HEIGHT_CONTRACT, snap=0.005):
    """
    Restore the height contract on a decimated LOD: collapse decimation nudges the
    extreme vertices by a millimeter or so (measured 1.1 mm on the footbar bottom), and
    LOD prefabs carry their OWN m_Bounds. Every vertex within `snap` of the lowest /
    highest point is set to exactly lo / hi. Returns (n_low, n_high) vertices moved.
    """
    me = obj.data
    mw = obj.matrix_world
    zs = [(mw @ v.co).z for v in me.vertices]
    zmin, zmax = min(zs), max(zs)
    inv = mw.inverted()
    n_lo = n_hi = 0
    for v, z in zip(me.vertices, zs):
        if z <= zmin + snap and abs(z - lo) > 1e-7:
            w = mw @ v.co
            w.z = lo
            v.co = inv @ w
            n_lo += 1
        elif z >= zmax - snap and abs(z - hi) > 1e-7:
            w = mw @ v.co
            w.z = hi
            v.co = inv @ w
            n_hi += 1
    me.update()
    print(f"[chair] {obj.name}: pinned {n_lo} lowest / {n_hi} highest vertices to "
          f"z = {lo:.3f} / {hi:.3f} (was {zmin:.4f} .. {zmax:.4f})")
    return n_lo, n_hi


def _proxy_parts(style, row, hanger_offset=HANGER_OFFSET):
    """
    The authored LOD2 (dq-chair.md section 7, skeptic D11: radii >= LOD0, seat block
    closed): [(obj, chooser)] in the authoring post-pivot frame, in tally order.
    chooser(authoring face normal) -> tuple of LOD0 part prefixes for the UV snap.
    """
    K = cs2_kit
    hang = float(row["hang_m"])
    out = []
    if style.key == "dq":
        tilt = math.radians(PAD_TILT_DEG)
        d = dq_datums(row, hanger_offset)
        Y = d["y"]
        # Revision 1: the grip box spans the carriage AND the hanger's top arm, so the
        # proxy hanger starts at the top of its vertical run (one bend, 4 rings, 18 tris)
        H = d["H"]
        gx, _gy, gz = DQ_GRIP["body"]
        gy0 = DQ_GRIP["body_y0"]
        gy1 = H[1][1] + PROXY_R["hanger"] + 0.010
        out.append((K.add_box("gripbody", (gx, gy1 - gy0, gz + 0.010),
                              (0.0, (gy0 + gy1) / 2.0, hang - (gz + 0.010) / 2.0),
                              drop=("-Z",)), lambda n: ("gripbody",)))
        # ... and its bottom leg runs straight into the top bar (the proxy has no clamp
        # block, so ending at H3 would leave the hanger floating 0.17 m above the bar)
        hz = K.tube_along("tube_hanger", [(0.0, H[1][1], H[0][2]), H[2],
                                          (0.0, H[3][1], d["hoop"][2][2])],
                          PROXY_R["hanger"], segments=3, caps="none", bend_rings=2)
        if hz["rings"] != 4:
            raise RuntimeError(f"LOD2 tube_hanger: expected 4 rings, got {hz['rings']}")
        out.append((hz, lambda n: ("tube_hanger",)))
        out.append((add_tri_link("hooptop", (0.0, -Y, 1.503), (0.0, Y, 1.503), PROXY_R["hoop"]),
                    lambda n: ("tube_hoop",)))
        for k, s in enumerate((-1, 1)):
            out.append((add_tri_link(f"hoopside_{k}", (0.0, s * Y, 1.503),
                                     (-0.200, s * Y, 0.190), PROXY_R["hoop"]),
                        lambda n: ("tube_hoop",)))
        # back block: pads + board as one slab hinged like the pads (front face on the pads' front plane), reaching 0.10
        # below the hinge so its open bottom sits inside the seat block. The seat block runs back under it for the same
        # reason (D11)
        ex, ez = d["pad_edge"]
        up = mathutils.Vector((-math.sin(tilt), 0.0, math.cos(tilt)))
        fw = mathutils.Vector((math.cos(tilt), 0.0, math.sin(tilt)))
        hinge = mathutils.Vector((ex, 0.0, ez))
        depth = d["pad_t"] + 0.020
        hy = 1.21

        def corner(a_up, a_fw, y):
            p = hinge + up * a_up + fw * a_fw
            return (p.x, y, p.z)

        def ring(a_up):      # counter-clockwise seen from +Z: back-left, front-left, front-right, back-right
            return [corner(a_up, -depth, -hy), corner(a_up, 0.0, -hy), corner(a_up, 0.0, hy),
                    corner(a_up, -depth, hy)]
        bl, bh = ring(-0.10), ring(d["pad_h"])
        back_x0 = min(c[0] for c in bl) - 0.005
        seat_x1 = d["pan"][0] + 0.260
        sl = [(back_x0, -1.23, 0.234), (seat_x1, -1.23, 0.234), (seat_x1, 1.23, 0.234),
              (back_x0, 1.23, 0.234)]
        sh = [(x, y, d["seat_top"]) for x, y, _z in sl]
        out.append((add_closed_block("seatblock", sl, sh),
                    lambda n: ("cushion_",) if n[2] > 0.5 else ("seatpan",)))
        out.append((add_closed_block("backblock", bl, bh, drop_bottom=True),
                    lambda n: ("pad_",) if n[0] > 0.5 else ("backboard",)))
        # restraint (Revision 2): the lap bar line between the arm planes, then the two T
        # footrests (stem from the bar to the foot bar, foot bar bottom at z 0 = C4)
        bx, bz = d["bar"]
        fx, _fz = d["foot"]
        ay = d["arm_y"]
        rf = PROXY_R["footbar"]
        fy, fh = DQ_BAR["foot_y"], DQ_BAR["foot_half"]
        out.append((add_tri_link("restraint", (bx, -ay, bz), (bx, ay, bz), PROXY_R["restraint"]),
                    lambda n: ("barlink",)))
        for k, s in enumerate((-1, 1)):
            out.append((add_tri_link(f"footstem_{k}", (bx, s * fy, bz), (fx, s * fy, rf),
                                     PROXY_R["footstem"]), lambda n: ("footstem_",)))
        for k, s in enumerate((-1, 1)):
            out.append((add_tri_link(f"footbar_{k}", (fx, s * fy - fh, rf), (fx, s * fy + fh, rf), rf),
                        lambda n: ("footbar_",)))
        mirror_grip_side(out)        # nitpick A2: the proxy grip box + hanger with the LOD0 grip side
        return out

    # tr: the FG frame (grip origin at z = 2.32 post-pivot). Revision 1: bench lowered, no footrest. The closed seat
    # block's bottom face is the lowest point (C4 "seat")
    p = tr_params()
    oz = hang - GRIP_TOP_Z                              # 2.32
    w = p.width
    seat_z = oz - p.hanger_len
    seat_top = seat_z + p.seat_thick / 2.0
    back_top = seat_top + p.back_height
    # the fixed grip as one box as long as the jaw saddle (LOD bounds stay with LOD0's,
    # cs2_validate lod-bounds), as wide as the jaw block, deep enough to bury the stem top
    sad = [x for x, _z in FG_GRIP["saddle"]]
    gl, gw, gh = max(sad) - min(sad), FG_GRIP["block"][1], GRIP_TOP_Z + 0.070
    out.append((K.add_box("grip", (gl, gw, gh), (0.0, 0.0, oz + GRIP_TOP_Z - gh / 2.0),
                          drop=("-Z",)), lambda n: ("grip", "gripjaw")))
    st = K.tube_along("tube_stem", stem_points(oz, back_top, top_dz=TR_STEM_TOP_DZ),
                      PROXY_R["stem"], segments=3, caps="none", bend_rings=2)
    if st["rings"] != 6:
        raise RuntimeError(f"LOD2 tube_stem: expected 6 rings, got {st['rings']}")
    out.append((st, lambda n: ("tube_stem",)))
    bx0, bx1 = -0.47, -0.36
    sx1 = -0.19 + p.seat_depth / 2.0                    # plank / cushion front 0.07
    yh = w / 2.0
    zb = seat_z - p.seat_thick / 2.0
    sl = [(bx0, -yh, zb), (sx1, -yh, zb), (sx1, yh, zb), (bx0, yh, zb)]
    sh = [(x, y, seat_top + 0.09) for x, y, _z in sl]
    out.append((add_closed_block("seatblock", sl, sh),
                lambda n: ("cushion_",) if n[2] > 0.5 else ("seat",)))
    by = w * 0.98 / 2.0
    bl = [(bx0, -by, seat_top), (bx1, -by, seat_top), (bx1, by, seat_top), (bx0, by, seat_top)]
    bh = [(x, y, back_top) for x, y, _z in bl]
    out.append((add_closed_block("backblock", bl, bh, drop_bottom=True),
                lambda n: ("pad_",) if n[0] > 0.5 else ("backrest",)))
    if abs(zb) > 1e-9:
        raise RuntimeError(f"tr LOD2 seat block bottom {zb:.5f}, must be 0 (C4)")
    return out


def snap_proxy_uvs(proxy, lod0, choosers, A, min_texels=9.0):
    """
    UVs for the authored LOD2 from the finished LOD0 (dq-chair.md section 7). For each proxy face, the nearest LOD0 face
    (BVH over the mapped source prefixes, preferring faces of >= `min_texels` UV area) gives its UV centroid. The loops
    of the proxy face get a non-degenerate 2-texel micro-island about that point, laid out in the face's own plane frame
    (winding kept, never mirrored). So each face samples one color and one ControlMask value. The proxy ends with ONE UV
    layer named like LOD0's active layer.
    """
    from mathutils.bvhtree import BVHTree
    me0 = lod0.data
    lay0 = me0.uv_layers.active
    uv0 = lay0.data
    t0 = part_table(lod0)
    fp0 = face_parts(lod0)
    mw0 = lod0.matrix_world
    verts0 = [mw0 @ v.co for v in me0.vertices]
    cent, area = [], []
    for poly in me0.polygons:
        pts = [tuple(uv0[li].uv) for li in poly.loop_indices]
        cent.append((sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts)))
        s = 0.0
        for i in range(len(pts)):
            s += pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1]
        area.append(abs(s) * 0.5 * A * A)
    cache = {}

    def tree(prefixes):
        key = tuple(sorted(prefixes))
        if key not in cache:
            ids = [i for i, p in enumerate(fp0) if t0[p - 1].startswith(key)]
            if not ids:
                raise RuntimeError(f"snap_proxy_uvs: no LOD0 faces for {key}")
            big = [i for i in ids if area[i] >= min_texels]
            ids = big or ids
            cache[key] = (BVHTree.FromPolygons(verts0, [list(me0.polygons[i].vertices)
                                                        for i in ids]), ids)
        return cache[key]

    me = proxy.data
    table = part_table(proxy)
    fp = face_parts(proxy)
    name0 = lay0.name
    for lay in sorted((l.name for l in me.uv_layers), reverse=True):
        if lay != "UVMap":
            me.uv_layers.remove(me.uv_layers[lay])
    me.uv_layers["UVMap"].name = name0
    me.uv_layers.active = me.uv_layers[name0]
    data = me.uv_layers[name0].data
    mw = proxy.matrix_world
    rot = mw.to_3x3()
    for poly, p in zip(me.polygons, fp):
        n_w = (rot @ poly.normal).normalized()
        n_a = (-n_w.y, n_w.x, n_w.z)                    # authoring frame (orient_for_net)
        bvh, ids = tree(choosers[table[p - 1]](n_a))
        c = mw @ poly.center
        hit = bvh.find_nearest(c)
        cu, cv = cent[ids[hit[2]]]
        P = [mw @ me.vertices[v].co for v in poly.vertices]
        t1 = (P[1] - P[0]).normalized()
        t2 = n_w.cross(t1)
        q = [((x - c).dot(t1), (x - c).dot(t2)) for x in P]
        ext = max(max(a for a, _b in q) - min(a for a, _b in q),
                  max(b for _a, b in q) - min(b for _a, b in q))
        s = (2.0 / A) / max(ext, 1e-9)
        for li, (a, b) in zip(poly.loop_indices, q):
            data[li].uv = (min(max(cu + a * s, 0.0), 1.0), min(max(cv + b * s, 0.0), 1.0))
    return len(me.polygons)


def _shipped_size(obj):
    """Texture size the active UV layer is laid out for (finished unique / private atlas)."""
    return FINISH_ATLAS if obj.data.uv_layers.active.name == "UVUnique" else 2 * ATLAS_QUAD_PX


def build_lod2_proxy(style, row, lod0, name, hanger_offset=HANGER_OFFSET):
    """
    The authored LOD2 `<name>_LOD2`: proxy parts -> tag_parts -> join -> weld -> orient
    -> part table -> UV snap from `lod0` (its active layer) -> shade_by_angle(66, flat
    weights) -> pin_height. Asserts the part list and the per-part tally.
    """
    pairs = _proxy_parts(style, row, hanger_offset)
    names = tuple(o.name for o, _c in pairs)
    want = tuple(n for n, _t in style.proxy_tris)
    if names != want:
        raise RuntimeError(f"LOD2 proxy parts {names} != {want}")
    tally = {o.name: cs2_asset.tri_count(o) for o, _c in pairs}
    if tally != dict(style.proxy_tris):
        raise RuntimeError(f"LOD2 proxy tally {tally} != {dict(style.proxy_tris)}")
    choosers = {o.name: c for o, c in pairs}
    parts = [(o, None) for o, _c in pairs]
    table = cs2_asset.tag_parts(parts)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    lod = bpy.context.active_object
    lod.name = f"{name}_LOD2"
    lod.data.name = lod.name
    # the join keeps the FIRST part's object transform (the grip box location). Bake it into the vertices, origin at the
    # world origin like LOD0. export_fbx bakes only rotation and scale, so a leftover location would ship the proxy
    # shifted
    bpy.ops.object.select_all(action="DESELECT")
    lod.select_set(True)
    bpy.context.view_layer.objects.active = lod
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=WELD_M)
    bpy.ops.object.mode_set(mode="OBJECT")
    cs2_asset.orient_for_net(lod)
    if any(abs(c) > 1e-9 for c in lod.location) or any(abs(c) > 1e-9 for c in lod.rotation_euler):
        raise RuntimeError(f"{lod.name}: object transform not identity after the bake")
    cs2_asset.store_part_table(lod, table)
    snap_proxy_uvs(lod, lod0, choosers, _shipped_size(lod0))
    cs2_asset.shade_by_angle(lod, SHADE_ANGLE, weighted=False)
    pin_height(lod, 0.0, float(row["hang_m"]))
    return lod


def unwrap(obj):
    """Legacy whole-object smart projection (unused: parts unwrap per quadrant)."""
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")


def tri_count(obj):
    return cs2_asset.tri_count(obj)


# --------------------------------------------------------------------------------------
# Post-finish texture work: number plate + geometry-keyed relief
# --------------------------------------------------------------------------------------

def _digit_bitmap(text):
    """Rows (top -> bottom) of a 5x7 glyph strip with one blank column between glyphs."""
    glyphs = [_DIGITS.get(ch, _DIGITS["0"]) for ch in str(text)]
    rows = []
    for r in range(7):
        rows.append("0".join(g[r] for g in glyphs))
    return rows


def make_plate_painter(chair, table, number, A, plate_w, plate_h, colours=None):
    """
    Painter for cs2_compose.stamp_on_face over the plate_face quad. The painter reads the orientation from the face
    itself. The UV -> world affine of its loops maps every texel center to a point on the plate. (right, up) = ((-n) x
    up_world, up_world), as a viewer in front of the face sees it. So the digits come out upright and unmirrored,
    whatever the packer did with the island (rotate=False, but flips/aspect are free). `colours` = (light, dark) in the
    buffer's space. The default is the sRGB PLATE_LIGHT / PLATE_DARK (the legacy finish edits PNG-encoded values), and
    linear for cs2_bake hooks.
    """
    import cs2_compose
    me = chair.data
    faces = cs2_compose.faces_of_prefix(me, table, "plate_face")
    if len(faces) != 1:
        raise RuntimeError(f"plate_face: expected one quad, found {len(faces)} faces")
    poly = me.polygons[faces[0]]
    mw = chair.matrix_world
    uvl = me.uv_layers["UVUnique"].data
    Q = np.array([[uvl[li].uv[0], uvl[li].uv[1], 1.0] for li in poly.loop_indices], np.float64)
    Pw = np.array([list(mw @ me.vertices[me.loops[li].vertex_index].co)
                   for li in poly.loop_indices], np.float64)
    M, *_ = np.linalg.lstsq(Q, Pw, rcond=None)           # world = [u v 1] @ M
    n = (mw.to_3x3() @ poly.normal).normalized()
    up = mathutils.Vector((0.0, 0.0, 1.0))
    up = (up - n * up.dot(n)).normalized()
    right = (-n).cross(up)
    centre = Pw.mean(axis=0)
    right_a, up_a = np.array(list(right)), np.array(list(up))
    bitmap = _digit_bitmap(number)
    rows, cols = len(bitmap), len(bitmap[0])
    bits = np.array([[ch == "1" for ch in row] for row in bitmap])
    cell = PLATE_CELL_M
    tw, th = cols * cell, rows * cell
    hw, hh = plate_w / 2.0, plate_h / 2.0
    lt, dk = colours if colours is not None else (PLATE_LIGHT, PLATE_DARK)
    light = np.array(lt, np.float32)
    dark = np.array(dk, np.float32)

    def painter(rect, base_sl, ctl_sl):
        r0, r1, c0, c1 = rect
        rr, cc = np.mgrid[r0:r1, c0:c1]
        uv1 = np.stack([(cc + 0.5) / A, (rr + 0.5) / A, np.ones(rr.shape)], axis=-1)
        Wp = uv1 @ M
        d = Wp - centre
        s = d @ right_a
        t = d @ up_a
        img = np.empty(s.shape + (3,), np.float32)
        img[...] = light
        border = (np.abs(s) > hw - 0.008) | (np.abs(t) > hh - 0.008)
        ci = np.floor((s + tw / 2.0) / cell).astype(int)
        ri = np.floor((th / 2.0 - t) / cell).astype(int)
        inside = (ci >= 0) & (ci < cols) & (ri >= 0) & (ri < rows)
        glyph = np.zeros(s.shape, bool)
        glyph[inside] = bits[ri[inside], ci[inside]]
        img[border | glyph] = dark
        base_sl[..., 0:3] = img
        base_sl[..., 3] = 1.0

    return painter


def detail_specs():
    """Relief specs keyed by part-name prefix (cs2_compose.detail_height) - the 6L set."""
    from cs2_compose import DetailSpec
    return {
        "cushion_":   DetailSpec(seams_m=0.12, seam_depth_m=0.003, seam_width_m=0.010),
        "backrest":   DetailSpec(seams_m=0.15, seam_depth_m=0.003, seam_width_m=0.010),
        "grip":       DetailSpec(bolt_pitch_m=0.09, bolt_r_m=0.010, bolt_h_m=0.004),
        "clampblock": DetailSpec(bolt_pitch_m=0.07, bolt_r_m=0.008, bolt_h_m=0.004),
    }


def style_detail_specs(style):
    """dq-chair.md section 5 DetailSpec per style (fg6l = the 6L set)."""
    from cs2_compose import DetailSpec
    specs = detail_specs()
    if style.key == "tr":
        specs["pad_"] = DetailSpec(seams_m=0.15, seam_depth_m=0.003, seam_width_m=0.010)
        # Revision 1 fixed grip: bolt heads on the jaw block ("grip", 6L spec) and the clamp plates. None on the saddle
        # or the spring housing (inactive specs stop the "grip" prefix from reaching them)
        specs["gripjaw"] = DetailSpec()
        specs["gripspring"] = DetailSpec()
        specs["gripplate_"] = DetailSpec(bolt_pitch_m=0.05, bolt_r_m=0.008, bolt_h_m=0.004)
    elif style.key == "dq":
        specs = {
            "cushion_":   DetailSpec(seams_m=0.12, seam_depth_m=0.003, seam_width_m=0.010),
            "pad_":       DetailSpec(seams_m=0.15, seam_depth_m=0.003, seam_width_m=0.010),
            "gripbody":   DetailSpec(bolt_pitch_m=0.06, bolt_r_m=0.008, bolt_h_m=0.004),
            "clampblock": DetailSpec(bolt_pitch_m=0.07, bolt_r_m=0.008, bolt_h_m=0.004),
            # Revision 1b friction comb: transverse ribs. On its top face the in-plane
            # axes are world X (lateral) / world Y (travel), so "rings" = grooves at
            # constant travel, i.e. teeth across the rope as in the photo
            "comb":       DetailSpec(seams_m=0.020, seam_depth_m=0.003, seam_width_m=0.010,
                                     seam_axes="rings"),
        }
    return specs


def post_finish_textures(chair, asset_dir, name, A, plate_number, specs=None):
    """
    After lift_tower.apply_finish_textures (unique layout shipped, maps written):
      1. stamp the number plate (opaque, ControlMask R zeroed under it),
      2. rasterize the relief (cushion/backrest seams, grip + clamp bolt heads) with
         cs2_compose.detail_height and blend it into the Normal (RNM, GL sign),
      3. rewrite BaseColor / ControlMask / Normal with BaseColor alpha 1, ControlMask A 0.
    """
    import cs2_compose
    me = chair.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])

    def load(suffix):
        img = bpy.data.images.load(os.path.join(asset_dir, f"{name}_{suffix}.png"))
        buf = np.empty(A * A * 4, dtype=np.float32)
        img.pixels.foreach_get(buf)
        bpy.data.images.remove(img)
        return buf.reshape(A, A, 4).copy()

    base, ctl, nrm = load("BaseColor"), load("ControlMask"), load("Normal")

    p = ChairParams()
    cs2_compose.stamp_on_face(base, ctl, me, table, "plate_face",
                              make_plate_painter(chair, table, plate_number, A,
                                                 p.plate_w, p.plate_h))

    h = cs2_compose.detail_height(me, A, specs if specs is not None else detail_specs())
    _part_map, ppm_map = cs2_compose.rasterise_face_fields(me, A)
    det = cs2_compose.height_to_normal(h, ppm_map)
    # The EMIT rebake leaves uncovered gutter texels black. Give them a flat normal (blend_rnm would turn black into
    # mid-gray) and renormalize every covered texel (photo normal sets are not unit length, nz clamped >= 0.05 so B > 0.5
    # everywhere).
    uncovered = nrm[..., 0:3].max(axis=-1) < 1e-3
    nrm[..., 0:3] = cs2_compose.blend_rnm(nrm, det)
    n = nrm[..., 0:3] * 2.0 - 1.0
    n[..., 2] = np.maximum(n[..., 2], 0.05)
    n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
    nrm[..., 0:3] = n * 0.5 + 0.5
    nrm[uncovered, 0:3] = (0.5, 0.5, 1.0)
    nrm[..., 3] = 1.0
    base[..., 3] = 1.0
    ctl[..., 3] = 0.0

    for arr, suffix, is_data in ((base, "BaseColor", False), (ctl, "ControlMask", True),
                                 (nrm, "Normal", True)):
        cs2_asset._write_png_raw(os.path.join(asset_dir, f"{name}_{suffix}.png"),
                                 f"{name}_{suffix}", arr, A, is_data)
    print(f"[chair] post-finish: plate '{plate_number}' stamped, relief blended into Normal "
          f"(height range {h.min():+.4f} .. {h.max():+.4f} m)")


def make_post_compose(chair, plate_number, specs, plate_w=0.20, plate_h=0.13, plate=True):
    """
    cs2_bake.bake_unique post_compose hook (dq-chair.md 6.3, skeptic D2 fixed: part_map is bound), LINEAR space, AO
    already multiplied by the bake: relief height -> compose_maps (per-part noise, detail normal, channel rules, ao=None)
    -> normals renormalized (nz >= 0.05, uncovered gutters flat) -> the number plate stamped in linear plate colors
    (zeroes ControlMask R under the label, plate=False: the split GRIP, which carries no plate).
    """
    import cs2_compose
    import cs2_preview
    lin = (tuple(float(c) for c in cs2_preview._srgb_to_linear(np.array(PLATE_LIGHT, np.float32))),
           tuple(float(c) for c in cs2_preview._srgb_to_linear(np.array(PLATE_DARK, np.float32))))

    def post(base, nrm, msk, ctl, ao, mesh, A):
        table = json.loads(mesh[cs2_asset.PART_TABLE_PROP])
        uncovered = np.asarray(nrm)[..., 0:3].max(axis=-1) < 1e-3
        h = cs2_compose.detail_height(mesh, A, specs)
        part_map, ppm = cs2_compose.rasterise_face_fields(mesh, A)
        base, nrm, msk, ctl = cs2_compose.compose_maps(base, nrm, msk, ctl, None, h, ppm,
                                                       {"part_map": part_map})
        n = nrm[..., 0:3] * 2.0 - 1.0
        n[..., 2] = np.maximum(n[..., 2], 0.05)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        nrm[..., 0:3] = n * 0.5 + 0.5
        nrm[uncovered, 0:3] = (0.5, 0.5, 1.0)
        if plate:
            cs2_compose.stamp_on_face(base, ctl, mesh, table, "plate_face",
                                      make_plate_painter(chair, table, plate_number, A,
                                                         plate_w, plate_h, colours=lin))
        print(f"[chair] post-compose: relief {h.min():+.4f} .. {h.max():+.4f} m"
              + (f", plate '{plate_number}' stamped (linear)" if plate else ", no plate"))
        return base, nrm, msk, ctl

    return post


# --------------------------------------------------------------------------------------
# Previews
# --------------------------------------------------------------------------------------

def render_previews(chair, out_dir, stem):
    """
    Legacy fallback (used only when cs2_preview cannot be imported): framed views via
    lift_tower's camera helpers. The profile view shows the swan-neck curve edge-on.
    """
    import lift_tower
    scene = bpy.context.scene
    scene.render.engine = lift_tower.pick_engine()
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.60, 0.66, 0.76, 1.0)
    scene.world = world

    bpy.ops.object.camera_add(location=(0, 0, 0))
    cam = bpy.context.active_object
    cam.data.lens = 50
    scene.camera = cam

    bpy.ops.object.light_add(type="SUN", location=(10, -12, 20))
    key = bpy.context.active_object
    key.data.energy = 5.5
    key.rotation_euler = (math.radians(50), 0, math.radians(35))
    bpy.ops.object.light_add(type="SUN", location=(-12, 8, 12))
    fill = bpy.context.active_object
    fill.data.energy = 2.2
    fill.rotation_euler = (math.radians(65), 0, math.radians(215))

    views = [(vname, mathutils.Vector(vdir), (1000, 1200)) for vname, vdir in CHAIR_VIEWS]
    written = []
    for vname, direction, res in views:
        scene.render.resolution_x, scene.render.resolution_y = res
        lift_tower.frame_camera(cam, chair, direction, res)
        path = os.path.join(out_dir, f"{stem}_{vname}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
    return written


def preview_rig(chair, asset_dir, name, prev_dir, lods=(), prev_generation=None, tint=None):
    """cs2_preview when importable (pbr / normals / density / lod / ab), else the legacy rig."""
    os.makedirs(prev_dir, exist_ok=True)
    try:
        import cs2_preview
    except Exception as e:  # noqa: BLE001
        print(f"[chair] cs2_preview unavailable ({e!r}) - legacy previews")
        cs2_asset.preview_material(chair, asset_dir, name)
        try:
            return render_previews(chair, prev_dir, name)
        finally:
            chair.data.materials.clear()
    modes = ["pbr", "normals", "density"]
    if lods:
        modes.append("lod")
    if prev_generation:
        modes.append("ab")
    return cs2_preview.preview(chair, asset_dir, name, prev_dir, modes=tuple(modes),
                               views=CHAIR_VIEWS, lods=list(lods) or None,
                               prev_generation=prev_generation,
                               tint=tint if tint is not None else {"B": BENCH_PREVIEW_RGB},
                               ground=False, resolution=(1000, 1200))


BAR_UP_VIEWS = (("barup_three_quarter", CHAIR_VIEWS[0][1]), ("barup_profile", CHAIR_VIEWS[1][1]))
# Revision 3: the bar against four boxy seated riders, framed on the riders (a close-up of the bench). Bar DOWN in both
# views, then UP in profile
RIDER_VIEWS = (("riders_profile", CHAIR_VIEWS[1][1]), ("riders_three_quarter", CHAIR_VIEWS[0][1]))
RIDER_UP_VIEWS = (("riders_barup_profile", CHAIR_VIEWS[1][1]),)
RIDER_PREVIEW_RGB = (0.80, 0.30, 0.08)      # a ski-jacket orange, so the riders read against the bar


def preview_bar_up(chair, row, asset_dir, name, prev_dir, tint):
    """
    dq only: <name>_pbr_barup_{three_quarter,profile}.png - the finished LOD0 with the bar
    assembly swung to C13's UP pose (a re-shaded duplicate, removed afterwards).
    """
    import cs2_preview
    r = check_bar_mechanics(chair, row, verbose=False)
    dup = bar_pose_copy(chair, r["hinge"], r["swing_deg"], f"{name}_BarUp")
    try:
        return cs2_preview.preview(dup, asset_dir, name, prev_dir, modes=("pbr",),
                                   views=BAR_UP_VIEWS, tint=tint, ground=False,
                                   resolution=(1000, 1200))
    finally:
        me = dup.data
        bpy.data.objects.remove(dup, do_unlink=True)
        bpy.data.meshes.remove(me)


def rider_standins(row, name):
    """
    Revision 3 preview only (NEVER exported, never joined): four boxy seated riders in the net-oriented world frame
    (authoring (x, y, z) -> world (y, -x, z), as orient_for_net), in one mesh object. Each seat has a torso against the
    pads (sheared with their tilt, shoulders 0.60 over the cushion), a helmeted head (top 0.88, inside C13's 0.90
    envelope), two thighs to a 0.60 knee line, two shins, and two boots resting on the foot bars' top.
    """
    d = dq_datums(row)
    s, pe = d["seat_top"], d["pad_edge"]
    fx, fz = d["foot"]
    foot_top = fz + DQ_R["footbar"]
    verts, quads = [], []

    def box(rings):
        base = len(verts)
        for ring in rings:
            verts.extend(ring)
        quads.extend(_box_faces(base))

    def slab(x0, x1, y0, y1, z0, z1):
        box([[(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)] for z in (z0, z1)])

    for yc in RIDER["seats_y"]:
        # torso, back on the pads' front plane (+1 cm), 0.26 deep, 0.38 wide
        box([[(_pad_front_x(z, pe) + 0.01, yc - 0.19, z), (_pad_front_x(z, pe) + 0.27, yc - 0.19, z),
              (_pad_front_x(z, pe) + 0.27, yc + 0.19, z), (_pad_front_x(z, pe) + 0.01, yc + 0.19, z)]
             for z in (s, s + 0.60)])
        hx = _pad_front_x(s + 0.75, pe) + 0.03
        slab(hx, hx + 0.25, yc - 0.11, yc + 0.11, s + 0.63, s + 0.88)
        kx = _pad_front_x(s, pe) + 0.60
        for side in (-1.0, 1.0):
            ly = yc + side * 0.095
            slab(_pad_front_x(s, pe) + 0.02, kx, ly - 0.075, ly + 0.075, s, s + 0.14)
            # shin: knee (kx - 0.07, s + 0.07) -> ankle (fx - 0.03, foot_top + 0.10), 0.10 thick
            a0 = mathutils.Vector((kx - 0.07, s + 0.07))
            a1 = mathutils.Vector((fx - 0.03, foot_top + 0.10))
            t = (a1 - a0).normalized()
            nrm = mathutils.Vector((-t.y, t.x)) * 0.05
            box([[(p.x, ly - 0.065, p.y), (q.x, ly - 0.065, q.y), (q.x, ly + 0.065, q.y),
                  (p.x, ly + 0.065, p.y)] for p, q in ((a1 + nrm, a1 - nrm), (a0 + nrm, a0 - nrm))])
            slab(fx - 0.12, fx + 0.18, ly - 0.055, ly + 0.055, foot_top, foot_top + 0.13)
    me = bpy.data.meshes.new(name)
    me.from_pydata([(y, -x, z) for x, y, z in verts], [], quads)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)       # every box outward (the shins too)
    bm.to_mesh(me)
    bm.free()
    me.update()
    for poly in me.polygons:
        poly.use_smooth = False
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def preview_riders(chair, row, asset_dir, name, prev_dir, tint, r=None):
    """
    dq only (Revision 3): <name>_pbr_riders_{profile,three_quarter}.png with the bar DOWN and
    <name>_pbr_riders_barup_profile.png with it at C13's UP pose, each with the four rider stand-ins (rider_standins) and
    framed on them. Same lighting / material recipe as cs2_preview's pbr mode (its rig, sky and material builders). The
    function removes everything it added: the posed duplicate, the riders, camera, sun, world and materials.
    """
    import cs2_preview as P
    if r is None:
        r = check_bar_mechanics(chair, row, verbose=False)
    written = []
    res = (1000, 1200)
    for deg, views in ((0.0, RIDER_VIEWS), (r["swing_deg"], RIDER_UP_VIEWS)):
        dup = bar_pose_copy(chair, r["hinge"], deg, f"{name}_RiderPose")
        rig = P._Rig(dup, None)
        try:
            riders = rig.track(rider_standins(row, f"{name}_Riders"))
            mat = rig.track_data(bpy.data.materials, bpy.data.materials.new(f"{name}_RiderStandin"))
            mat.use_nodes = True
            bsdf = next(nd for nd in mat.node_tree.nodes if nd.type == "BSDF_PRINCIPLED")
            bsdf.inputs["Base Color"].default_value = (*RIDER_PREVIEW_RGB, 1.0)
            bsdf.inputs["Roughness"].default_value = 0.85
            riders.data.materials.append(mat)
            riders.hide_render = False
            cam = P._make_camera(rig)
            P._make_world(rig, **P.NOON)
            P._assign(dup, P._pbr_material(rig, asset_dir, name, tint))
            sc = rig.scene
            for vname, vdir in views:
                sc.render.resolution_x, sc.render.resolution_y = res
                P._frame(cam, riders, vdir, res)
                path = os.path.join(prev_dir, f"{name}_pbr_{vname}.png")
                written.append(P._render(rig, path, engine="EEVEE"))
                print(f"[preview] pbr (riders) -> {path}")
        finally:
            rig.restore()
            me = dup.data
            bpy.data.objects.remove(dup, do_unlink=True)
            bpy.data.meshes.remove(me)
    return written


# --------------------------------------------------------------------------------------
# Round trip
# --------------------------------------------------------------------------------------

def fbx_box_of_part(obj, name):
    """Revision 1c: a part's bbox in the exported FBX's raw frame (cm, export_fbx bakes the net frame (x, y, z) m -> (x, z,
    -y) x 100, Y up) for verify_export's flat-face check."""
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    m = np.array(obj.matrix_world, dtype=np.float64)
    w = co.reshape(-1, 3).astype(np.float64) @ m[:3, :3].T + m[:3, 3]     # net frame, m
    fp = face_parts(obj)
    pid = part_table(obj).index(name) + 1
    vi = sorted({i for poly, p in zip(me.polygons, fp) if int(p) == pid for i in poly.vertices})
    net = w[vi]
    raw = np.stack([net[:, 0], net[:, 2], -net[:, 1]], axis=1) * 100.0
    return raw.min(axis=0), raw.max(axis=0)


def fbx_flat_faces(m, box, tol_cm=0.1, min_area_cm2=100.0):
    """C14 on a re-imported FBX mesh `m`: (big faces found, worst corner-normal deviation in
    degrees) over the axis-aligned faces of >= min_area_cm2 lying wholly inside `box`."""
    lo, hi = np.asarray(box[0]) - tol_cm, np.asarray(box[1]) + tol_cm
    co = np.empty(len(m.vertices) * 3, dtype=np.float64)
    m.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    cn = np.array([tuple(c.vector) for c in m.corner_normals], dtype=np.float64)
    k, worst = 0, 0.0
    for poly in m.polygons:
        fn = np.array(poly.normal, dtype=np.float64)
        pv = co[list(poly.vertices)]
        if (float(np.abs(fn).max()) <= FLAT_FACE_AXIS or poly.area < min_area_cm2
                or (pv < lo).any() or (pv > hi).any()):
            continue
        k += 1
        d = cn[list(poly.loop_indices)] @ fn
        worst = max(worst, float(np.degrees(np.arccos(np.clip(d, -1.0, 1.0))).max()))
    return k, worst


def verify_export(fbx_path, expect_height=HEIGHT_CONTRACT, histogram=True, flat_box=None):
    """
    Re-import a written FBX and report: raw vertex extent (cm, Y up), custom normals and
    the split-normals-per-vertex histogram (probe_smooth.py idiom), and the height
    contract on the imported vertices. `flat_box` (Revision 1c): the carriage's raw-frame
    bbox (fbx_box_of_part) - its big faces' corner normals are measured (fbx_flat_faces)
    and must stay within FLAT_FACE_MAX_DEG. Returns (ok, info dict). Imported data is removed.
    """
    before = set(bpy.data.objects)
    op = bpy.ops.wm.fbx_import if hasattr(bpy.ops.wm, "fbx_import") else bpy.ops.import_scene.fbx
    op(filepath=fbx_path)
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    if not new:
        raise RuntimeError(f"verify: nothing imported from {fbx_path}")
    o = new[0]
    m = o.data
    xs = [v.co.x for v in m.vertices]
    ys = [v.co.y for v in m.vertices]
    zs = [v.co.z for v in m.vertices]
    ext = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    unit = 1.0 if max(ext) > 10.0 else 100.0           # cm baked in, unless the importer rescaled
    height = ext[1] * unit / 100.0
    lo_y = min(ys) * unit / 100.0
    info = {"name": o.name, "verts": len(m.vertices), "polys": len(m.polygons),
            "extent_cm": tuple(round(e * unit, 3) for e in ext), "height_m": height,
            "min_y_m": lo_y, "custom_normals": bool(m.has_custom_normals)}
    if histogram:
        per_vert = collections.defaultdict(set)
        for lp in m.loops:
            per_vert[lp.vertex_index].add(tuple(round(c, 3) for c in lp.normal))
        info["normals_per_vertex"] = dict(sorted(collections.Counter(
            len(v) for v in per_vert.values()).items()))
    ok = abs(height - expect_height) < 1e-3 and abs(lo_y) < 1e-3
    stem = os.path.basename(fbx_path)
    print(f"[verify] {stem}: mesh '{o.name}' verts {info['verts']} polys {info['polys']} "
          f"raw extent cm {info['extent_cm']} (Y up) custom_normals={info['custom_normals']}")
    if histogram:
        print(f"[verify] {stem}: normals-per-vertex histogram {info['normals_per_vertex']}")
    print(f"[verify] {stem}: height {height:.4f} m (y from {lo_y:.4f}) vs {expect_height:.3f} "
          f"-> {'OK' if ok else 'FAIL'}")
    if flat_box is not None:
        box = (np.asarray(flat_box[0]) / unit, np.asarray(flat_box[1]) / unit)
        k, worst = fbx_flat_faces(m, box, tol_cm=0.1 / unit, min_area_cm2=100.0 / unit ** 2)
        info["flat_faces"] = {"faces": k, "worst_deg": round(worst, 3)}
        flat_ok = k >= 6 and worst < FLAT_FACE_MAX_DEG
        ok = ok and flat_ok
        print(f"[verify] {stem}: C14 carriage big faces in the FBX: {k}, corner normals <= "
              f"{worst:.2f} deg off the face normal (< {FLAT_FACE_MAX_DEG}) -> "
              f"{'OK' if flat_ok else 'FAIL'}")
    for obj in new:
        me = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(me)
    return ok, info


# --------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------

def parse_args(argv=None):
    if argv is None:
        argv = sys.argv
        argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser(prog="lift_chair.py")
    ap.add_argument("--style", default="tr", choices=sorted(STYLES),
                    help="tr = fixed-grip triple (default), dq = detachable quad, "
                         "fg6l = the shipped round-L chair (old numbers)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--letter", default=None, help="generation letter (default per style)")
    ap.add_argument("--name", default=None, help="asset name (default <style base><letter>)")
    ap.add_argument("--seats", type=int, default=None,
                    help="accepted only when it equals the style's seats")
    ap.add_argument("--preview", default=None)
    ap.add_argument("--finish", action="store_true",
                    help="unique repack + AO bake at 512, relief normal, number plate")
    ap.add_argument("--bake", default=None, choices=("v2", "legacy"),
                    help="v2 = cs2_bake.bake_unique (tr/dq default), legacy = "
                         "lift_tower.apply_finish_textures (fg6l, always)")
    ap.add_argument("--lods", action="store_true", help="ship an authored LOD1/LOD2 chain")
    ap.add_argument("--lod2", default=None, choices=("proxy", "decimate"),
                    help="proxy = authored LOD2 (tr/dq default), decimate = make_lod_chain "
                         "(fg6l, always)")
    ap.add_argument("--verify", action="store_true",
                    help="re-import every written FBX and check the height contract")
    ap.add_argument("--plate-number", default="27")
    ap.add_argument("--hanger-offset", type=float, default=None,
                    help="dq only: outboard offset of the C-hanger's vertical run (m, 0.30)")
    ap.add_argument("--carriers", default=CARRIERS_PATH, help="catalog/carriers.json")
    ap.add_argument("--ab", default=None,
                    help="previous generation asset folder for the ab preview "
                         "(default: tr chairs/L/SkiLiftChair6L, fg6l chairs/K/SkiLiftChair6K)")
    # kept for CLI compatibility with earlier rounds. The atlas recipe owns these now
    ap.add_argument("--basecolor", type=float, nargs=3, default=[0.13, 0.16, 0.22])
    ap.add_argument("--metallic", type=float, default=0.1)
    ap.add_argument("--smoothness", type=float, default=0.35)
    # live review (ROUND-SPEC R10): optional DIR, default assets/preview/review
    ap.add_argument("--review", nargs="?", const="", default=None, metavar="DIR",
                    help="also write the live-review pack <DIR>/<name>.blend "
                         "(cs2_review.write_pack, duplicates only; exports unchanged)")
    ap.add_argument("--split", action="store_true",
                    help="tr / dq: build the GRIP and BODY meshes (<base>Grip<letter> + <base><letter>, "
                         "default letter " + SPLIT_LETTER + "; carrier split 2026-11-03), finish + LODs always")
    ap.add_argument("--envelope", action="store_true",
                    help="with --split: the jaw envelope (ENVELOPE_*; default letter " + ENVELOPE_LETTER + "): no "
                         "bake, the shipped " + SPLIT_LETTER + " atlas remapped; only the changed meshes are written")
    ap.add_argument("--thin-jaw", dest="thin_jaw", nargs="?", const=THIN_JAW_LETTER, default=None,
                    choices=sorted(THIN_JAW_SPECS),
                    help="with --envelope, style tr: the thin jaw (THIN_JAW_SPECS, art-601) as that grip letter "
                         "(default " + THIN_JAW_LETTER + "); the O body is unchanged and not written")
    ap.add_argument("--remap-from", default=None,
                    help="--envelope: the shipped split letter folder (default assets/import/chairs/"
                         + SPLIT_LETTER + ")")
    return ap.parse_args(argv)


def _resolve(args):
    """Style, carrier row, letter and name, with the option checks."""
    st = STYLES[args.style]
    if args.seats is not None and args.seats != st.seats:
        raise ValueError(f"--seats {args.seats}: style {st.key!r} has {st.seats} seats")
    if args.hanger_offset is not None and st.key != "dq":
        raise ValueError("--hanger-offset applies to --style dq only")
    if st.key == "fg6l" and (args.bake not in (None, "legacy") or args.lod2 not in (None, "decimate")):
        raise ValueError("--style fg6l is the shipped recipe: legacy bake, decimated LOD2")
    rows = load_carriers(args.carriers)
    row = rows[st.carrier]
    if int(row["seats"]) != st.seats:
        raise ValueError(f"carrier {st.carrier!r} seats {row['seats']} != style {st.seats}")
    if args.split and st.key not in SPLIT_PLAN:
        raise ValueError(f"--split: style {st.key!r} has no split (tr / dq)")
    if getattr(args, "envelope", False):
        if not args.split:
            raise ValueError("--envelope needs --split")
        global ENVELOPE
        ENVELOPE = True
    if getattr(args, "thin_jaw", None):
        if not ENVELOPE or st.key != "tr":
            raise ValueError("--thin-jaw needs --split --envelope --style tr")
        global THIN_JAW, THIN_JAW_LETTER, THIN_JAW_SPEC
        THIN_JAW = True
        THIN_JAW_LETTER = args.thin_jaw
        THIN_JAW_SPEC = THIN_JAW_SPECS[THIN_JAW_LETTER]
    letter = args.letter or (THIN_JAW_LETTER if THIN_JAW else ENVELOPE_LETTER if ENVELOPE
                             else SPLIT_LETTER if args.split else st.letter)
    name = cs2_asset.validate_asset_name(args.name or f"{st.base}{letter}")
    import cs2_validate
    rep = cs2_validate._Report(name)
    cs2_validate.lint_asset_name(name, rep)
    if rep.counts()[0]:
        raise ValueError(f"asset name {name!r} fails cs2_validate.lint_asset_name")
    return st, row, letter, name


def _main_fg6l(args, st, name):
    """The shipped round-L recipe, unchanged (byte-identical PNGs / settings.json)."""
    clear_scene()
    here = HERE

    p = ChairParams(seats=st.seats)
    chair = build_chair(p, name)
    table = json.loads(chair.data[cs2_asset.PART_TABLE_PROP])
    lo, hi = assert_height_contract(chair)
    tris = cs2_asset.tri_count(chair)
    if tris > TRI_BUDGET:
        raise RuntimeError(f"LOD0 {tris} tris exceeds the {TRI_BUDGET} budget")

    asset_dir = os.path.join(os.path.abspath(args.out), name)
    os.makedirs(asset_dir, exist_ok=True)

    d = chair.dimensions
    grip_h = hi - lo                 # base-pivoted, so the grip is at the very top
    rope_y = 7.04                    # lane height, from TowerParams.sheave_axle_height
    print(f"[chair] seats: {p.seats}   bench width: {p.width:.2f} m   parts: {len(table)}")
    print(f"[chair] tris: {tris} (budget {TRI_BUDGET}), verts: {len(chair.data.vertices)}")
    print(f"[chair] size (m): {d.x:.3f} x {d.y:.3f} x {d.z:.3f}")
    print(f"[chair] base-pivoted: geometry runs z {lo:.4f} .. {hi:.4f}, grip at the top; "
          f"height contract {grip_h:.4f} m OK")
    print(f"[chair] ---- CS2 NetSubObjects settings ----")
    print(f"[chair]   m_Placement 2 (EdgeMiddle), m_Spacing 15")
    print(f"[chair]   m_Position  (+/-1.80, {rope_y - grip_h:.2f}, 0)")
    print(f"[chair]   = rope {rope_y} minus chair height {grip_h:.2f}, so the grip lands on the rope")

    # Private copy of the shared lift atlas at 512 (quad 256): the bench quad repainted.
    tex_root = os.path.join(here, "..", "textures")
    quad_sets, control_quads = cs2_asset.lift_atlas_quads(tex_root)
    cs2_asset.make_texture_set_atlas(asset_dir, name, quad_sets, quad_size=ATLAS_QUAD_PX,
                                     control_quads=control_quads, post=chair_atlas_post)

    finished = False
    if args.finish:
        import lift_tower
        finished = lift_tower.apply_finish_textures(chair, asset_dir, name, FINISH_ATLAS,
                                                    ao_samples=AO_SAMPLES)
        if finished:
            post_finish_textures(chair, asset_dir, name, FINISH_ATLAS, args.plate_number)
        else:
            print("[chair] finish failed - shipping the atlas layout, plate unpainted")
    else:
        print("[chair] no --finish: atlas layout shipped, number plate unpainted")

    cs2_asset.export_fbx(chair, os.path.join(asset_dir, f"{name}.fbx"))

    lods = []
    if args.lods:
        lods = cs2_asset.make_lod_chain(chair, name, LOD_TARGETS, (LOD1_DELETE, LOD2_DELETE))
        for lod in lods:
            pin_height(lod)
            llo, lhi = assert_height_contract(lod, lod.name)
            print(f"[chair] {lod.name}: {cs2_asset.tri_count(lod)} tris, "
                  f"z {llo:.4f} .. {lhi:.4f} (height {lhi - llo:.4f} m) OK")
            cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{lod.name}.fbx"))
    cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    print(f"[chair] asset folder: {asset_dir}")

    if args.preview:
        prev = os.path.abspath(args.preview)
        ab = args.ab
        if ab is None:
            cand = os.path.join(here, "..", "import", *st.ab)
            ab = os.path.abspath(cand) if os.path.isdir(cand) else None
        for png in preview_rig(chair, asset_dir, name, prev, lods, ab):
            print(f"[chair] preview -> {png}")

    if args.verify:
        ok0, info0 = verify_export(os.path.join(asset_dir, f"{name}.fbx"))
        results = [ok0]
        for k in range(1, len(lods) + 1):
            okk, _ = verify_export(os.path.join(asset_dir, f"{name}_LOD{k}.fbx"), histogram=False)
            results.append(okk)
        if not all(results):
            raise RuntimeError(f"verify: height contract failed on re-import: {results}")
        if not info0["custom_normals"]:
            raise RuntimeError("verify: LOD0 re-import carries no custom normals")
        print(f"PASS lift_chair: {name} {tris} tris LOD0"
              + (f" / {'/'.join(str(l) for l in LOD_TARGETS)} chain" if lods else "")
              + f", height {info0['height_m']:.3f} m on every level, finish "
              f"{'on' if finished else 'off'}, normals histogram {info0['normals_per_vertex']}")

    # -- live-review pack (ROUND-SPEC R10, --review): DUPLICATES of the built LOD0..2 go to <DIR>/<name>.blend AFTER
    # export, previews and --verify (pipeline-gap.md section 6). The build objects and every file written above stay
    # untouched (gate: tests/test_review_side_effects.py). The code frees the LODs after it.
    if args.review is not None:
        import cs2_review
        cs2_review.write_pack(name, asset_dir, dict(enumerate([chair] + lods)), {},
                              meta={"generator": "lift_chair.py", "args": CS.manifest_args(args, REPO_ROOT),
                                    "kind": "hanging", "caps": {0: TRI_BUDGET},
                                    "tint": {"B": BENCH_PREVIEW_RGB}},
                              out_dir=args.review or None)
    for lod in lods:
        me = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(me)


def build_lods(chair, style, row, name, lod2_mode, hanger_offset, info):
    """LOD1 (make_lod_chain + pin) and LOD2 (proxy or decimated), contracts on each."""
    hang = float(row["hang_m"])
    results = {}
    if lod2_mode == "decimate":
        lods = cs2_asset.make_lod_chain(chair, name, (style.lod1_target, style.lod2_decimate_target),
                                        (style.lod1_delete, style.lod2_delete))
        for lod in lods:
            pin_height(lod, 0.0, hang)
    else:
        lods = cs2_asset.make_lod_chain(chair, name, (style.lod1_target,), (style.lod1_delete,))
        pin_height(lods[0], 0.0, hang)
        lods.append(build_lod2_proxy(style, row, chair, name, hanger_offset))
    for k, lod in enumerate(lods, start=1):
        r = check_contracts(lod, style, row, k, hanger_offset,
                            info={"lod0_tris": info["lod0_tris"], "proxy": lod2_mode == "proxy"})
        results[f"LOD{k}"] = r
        print(f"[chair] {lod.name}: {cs2_asset.tri_count(lod)} tris, contracts "
              f"{', '.join(sorted(r))} OK")
    return lods, results


# --------------------------------------------------------------------------------------
# Carrier split (2026-11-03, --split): the chair as TWO meshes, the GRIP and the BODY
# (docs/research/carrier-pivot-swing-plan-2026-11-03.md sections 2, 5, 6, 8, brief
# docs/research/art/carrier-split-2026-11/BRIEF.md). The code pitches the grip with the rope and keeps the body plumb
# (later: swings it). They meet at a LATERAL pivot pin:
#   grip  the jaws / carriage + a yoke (two cheeks) that carries the pin ends
#   body  the hanger, ending in a knuckle (the eye, a sphere on the pin) between the cheeks,
#         + the seat
# Both meshes keep the base pivot (lowest vertex z = 0). carrier_split.py holds the joint geometry and the split
# contracts:
# - S1 (rejoin within 2 mm)
# - S2 (pin gap in (0, 5 mm] over the pin sweep: +-45 deg pitch, each +-3 deg fore-aft, all about the pin)
# - S3 (no body/grip clash over that sweep, BVH as C13)
#
# The cross-wind sway rolls grip + body TOGETHER about the rope point: no lateral turn at the pin (swing convention,
# carrier_split.py header).
#
# Split-only geometry changes (critique 2026-11-03):
# - The dq hanger head is a casting (relief geo, no painted bolts, the hex pin ends stay).
# - The dq running wheels go 8 -> SPLIT_WHEEL_SEGS segments (split_wheel: n-gon caps triangulated after their normals,
#   segs - 2 tris a cap).
#
# The one-piece M chairs are unchanged. Every one-piece contract C1-C14 still runs, on the assembled pair (split_pair:
# body + grip at zero, part table body + grip) of every level, with the pair tally and caps below.
# --------------------------------------------------------------------------------------

SPLIT_LETTER = "N"
SPLIT_GRIP_INFIX = "Grip"
#: dq running wheels in the split (critique 2026-11-03: the 8-gon read as a polygon in the grip
#: preview): 16 segments, both caps as 16-gons triangulated after their normals = 32 side + 2 x 14
SPLIT_WHEEL_SEGS = 16
SPLIT_WHEEL_TRIS = 2 * SPLIT_WHEEL_SEGS + 2 * (SPLIT_WHEEL_SEGS - 2)
# tr split touch-up (owner verdict 2026-11-03, round 3), split-only like the dq wheels (the one-piece M keeps FG_GRIP and
# stem_points):
# - The clevis hangs CENTERED under the jaw block on the rope axis (round 2 had it on the J-stem's 42 deg first span,
#   0.10 m behind the block). The J-stem's top runs STRAIGHT DOWN into it: top point -> V (vertical, x 0) -> U (one span
#   at slope_deg, onto the vertical run x -0.52) -> the old vertical run's end.
# - The two 52 deg bends' fillets MEET (|TV|, |UP4| >= |VU|, so each fillet is |VU| / 2: one even S of ~0.155 m chords,
#   the stem_points lesson). They have 5 rings each = 11 rings, 13 deg a joint. With 4 rings the joint is 17 deg, a
#   visible polyline in the side preview. Non-meeting fillets at a steeper slope put long straights beside short chords,
#   and those crease the face-area weighted normals.
# - The top segment (the cut ring at the pin -> the V fillet's first ring, 0.23 m) is a straight frustum on the clevis
#   axis.
# - Round 4 (owner: the round-3 F4a short jaw was wrong, photo fixed grips/close-jaw-grip-ski-lift-260nw-544514905.webp):
#   the jaw is ONE long plate lying ON the rope (underside on the rope's top), 0.68 m like the one-piece saddle. The
#   plate is chamfered to a 7 mm edge at both ends so the rope runs in smoothly. The compact clamp box sits UNDER the
#   plate's middle and wraps the rope. The clevis hangs under the box. No block encloses the plate any more (round 2's
#   "two blades out of the block's sides"). The plate's crown is the mesh top (rope + GRIP_TOP_Z, C1 / C2), so plate +
#   box are ONE part "grip" (a casting, relief geo). C2 wants the top on "grip", and C3 wants the rope point in its bbox
#   (the box). 32 tris = the one-piece block 12 + saddle 20.
# - The audit split_alignment() raises when either regresses (SPLIT_ALIGN_* below).
TR_SPLIT_GRIP = {
    # plate (x, z) about the rope, CCW from -Y: flat underside on the rope's top (r 0.024), sloping
    # 9.6 deg up to a 0.10 m crown at GRIP_TOP_Z (edge to the end face 80 deg: crisp at SHADE_ANGLE 66)
    "plate": [(-0.340, 0.024), (0.340, 0.024), (0.340, 0.031), (0.050, GRIP_TOP_Z),
              (-0.050, GRIP_TOP_Z), (-0.340, 0.031)],
    "plate_w": 0.080,                       # rope 0.048
    "box": (0.140, 0.090, -0.060, 0.030),   # x length, y width, z bottom (the old block's), z top (in the plate)
}
TR_SPLIT_STEM = {"v_z": -0.700, "slope_deg": 38.0, "bend_rings": 5}   # V z (rope frame), V -> U slope
TR_SPLIT_STEM_RINGS = 2 + 2 * TR_SPLIT_STEM["bend_rings"] - 1          # the two fillets meet: one dedupe
TR_SPLIT_STEM_TRIS = 2 * 8 * (TR_SPLIT_STEM_RINGS - 1)                 # 8 segments: 160
SPLIT_ALIGN_PLAN_MAX_M = 0.005       # clevis center -> jaw block center, in plan (owner: < 5 mm)
SPLIT_ALIGN_AXIS_MAX_DEG = 1.0       # tube end axis vs clevis axis, and each end ring's tilt off it
SPLIT_ALIGN_AXIS_OFFSET_MAX_M = 0.001   # the pin center off the tube end axis (coaxial, not just parallel)
# Joint numbers in each style's BUILD frame (tr: rope at z 0, all geometry below. dq: post-pivot). Knuckle R + the design
# gap c + cheek thickness t = the cheek's outer face. "over" = how far the bridge / head overhangs the cheeks sideways
# (no coplanar faces).
SPLIT_JOINT = {
    # tr (round 3): the pin sits under the jaw block's center on the stem's vertical top run, R + c under the bridge
    # plate. The bridge spans the cheeks + bridge_x_over fore and aft, its top buried in the block (underside -0.060),
    # the cheeks' tops buried in the bridge
    "tr": {"knuckle_r": 0.052, "knuckle_segs": (8, 4), "cheek_t": 0.016, "cheek_r": 0.055,
           "bridge_bottom": -0.085, "bridge_top": -0.050, "cheek_top": -0.065, "bridge_x_over": 0.004,
           "over": 0.003, "pin_r": 0.020, "pin_len": 0.012, "r_x": 0.17},
    # dq: the pin sits in the C-hanger's vertical run just under the top bend (fillet end z 2.958):
    # the head block spans z head_top - head[1] .. head_top, the top arm is cut at arm_cut_z
    # inside it, the body's hanger starts in the knuckle
    "dq": {"knuckle_r": 0.054, "knuckle_segs": (8, 4), "head": (0.120, 0.070), "head_top": 2.950,
           "arm_cut_z": 2.915, "cheek_t": 0.016, "cheek_r": 0.058, "cheek_bury": 0.010,
           "over": 0.003, "pin_r": 0.020, "pin_len": 0.012, "r_x": 0.17},
}
# Part-name prefixes that belong to the GRIP mesh (everything else is the body).
SPLIT_GRIP_PARTS = {
    "tr": ("grip",),       # grip (round 4: plate + clamp box), gripspring + gripbridge, gripcheek_*, grippin_*
    "dq": ("gripbody", "comb", "jaw_rope", "lever_", "spring_", "wheel_", "axle_",
           "tube_hangerarm", "hangerhead", "hangercheek_", "hangerpin_"),
}
SPLIT_DENSITY = {
    "tr": {"gripbridge": 1.2, "gripcheek_": 1.2, "grippin_": 1.0, "knuckle": 1.0},
    "dq": {"hangerhead": 1.2, "hangercheek_": 1.2, "hangerpin_": 1.0, "knuckle": 1.0},
}
# The measured split (first build 2026-11-03), asserted exactly like C9 / the proxy tally: body / grip LOD0 tallies, LOD1
# targets + delete prefixes, LOD2 proxy tallies. pin_detail = pair LOD0 - the one-piece tally (the joint parts minus what
# they replace): the pair cap is the old cap + the pin detail (BRIEF "Budgets"). The pair LOD2 cap works likewise (+ the
# yoke box).
SPLIT_PLAN = {
    "tr": {"body_tally": (("knuckle", 48),) + tuple(
               (n, TR_SPLIT_STEM_TRIS if n == "tube_stem" else t)
               for n, t in _TR_TALLY if not n.startswith("grip")),
           "grip_tally": (("grip", 32), ("gripspring", 18), ("gripbridge", 12),
                          ("gripcheek_0", 22), ("gripcheek_1", 22), ("grippin_0", 18), ("grippin_1", 18)),
           "pin_detail": 116,             # knuckle 48 + bridge 12 + cheeks 44 + pin ends 36 - plates 24
           # round 3: the straight-down stem (2 meeting bends x 5 rings = 11 rings) = 160 tris, +32 on the one-piece J
           # (128). The round-4 plate + box "grip" (32) = block + saddle. The pair (610 + 32 =
           # 642) is 26 over the BRIEF cap 500 + 116 = 616 (+4.2 %). Cap raised by 26 (rule: <= 5 %)
           "segs_detail": TR_SPLIT_STEM_TRIS - 128, "cap_raise": 26,
           "lod1": (200, 70), "lod1_delete": (("bolt_",), ("bolt_", "grippin_")),
           "body_proxy": (("knuckle", 8), ("tube_stem", 30), ("seatblock", 12), ("backblock", 10)),
           "grip_proxy": (("grip", 10), ("gripyoke", 10)),
           "pin_detail_lod2": 18},         # yoke box 10 + knuckle 8 (the proxy stem pin -> V -> U -> end: 6 rings)
    "dq": {"body_tally": (("knuckle", 48), ("tube_hanger", 80)) + tuple(
               (n, t) for n, t in _DQ_TALLY[12:]),
           "grip_tally": tuple((n, SPLIT_WHEEL_TRIS if n.startswith("wheel_") else t)
                               for n, t in _DQ_TALLY[:11])
                         + (("tube_hangerarm", 80), ("hangerhead", 12),
                            ("hangercheek_0", 22), ("hangercheek_1", 22),
                            ("hangerpin_0", 18), ("hangerpin_1", 18)),
           "pin_detail": 156,             # knuckle 48 + head 12 + cheeks 44 + pin ends 36 + hanger cut 16
           # the 16-segment wheels: 2 x (60 - 32) = +56 tris. The pair (1152 + 56 = 1208) is 52 over the BRIEF cap 1000 +
           # 156 = 1156 (+4.5 %). Cap raised by 52 (rule: <= 5 %, DECISIONS 585)
           "segs_detail": 2 * (SPLIT_WHEEL_TRIS - 32), "cap_raise": 52,
           "lod1": (280, 180),
           "lod1_delete": (("bolt_", "barhinge_"),
                           ("bolt_", "wheel_", "spring_", "lever_", "comb", "axle_", "hangerpin_")),
           "body_proxy": (("knuckle", 8), ("tube_hanger", 18)) + STYLES["dq"].proxy_tris[2:],
           "grip_proxy": (("gripbody", 10), ("hangeryoke", 10)),
           "pin_detail_lod2": 18},         # yoke box 10 + knuckle 8
}

# --------------------------------------------------------------------------------------
# Jaw envelope (breakover-twins round, v2, 2026-11-03): split only, --envelope, generation O. Every carrier passes every
# tower train under tests/test_grip_clearance.py:
# - A tower wheel's rim / liner may touch ONLY the grip's smooth jaw envelope (|z| <= ENVELOPE_Z about the rope center).
# - Hubs / bosses / axle caps never touch it.
# - Every other tower part keeps CARRIER_CLEAR_M.
#
# Real trains pass grips that way: the closed jaws sit nearly flush with the rope and stand a limited, smooth amount
# above it, and the sheave battery rocks as it rides over them (Poma US4441430, US4691643,
# docs/research/art/breakover-twins-2026-11/refs/SOURCES.md "Grip passage under compression"). So everything taller than
# the jaw sits OUTBOARD (authoring -Y) of the widest wheel and its boss:
# - tr: the clamp block (the mesh top, rope + GRIP_TOP_Z), the spring housing, the clevis, the pin and the J-stem's
#   straight top run move out to y ENVELOPE_TR["block_y"]. The plate stays ON the rope as the jaw (crown lowered into the
#   envelope) and reaches out into the block. The stem's V -> U span also leans back to the seat's center line, below the
#   wheels.
# - dq: the carriage (body, comb, lever, springs, running wheels, axles) moves out by ENVELOPE_DQ["dy"] over the
#   C-hanger's vertical run. The jaw bar stays on the rope and reaches out to the running wheels, and the stub axles
#   reach back into it. The hanger's top bend is drawn into the carriage. No dq pin moves: the dq body is unchanged
#   (keeps its letter).
#
# Topology is unchanged part for part, so the shipped atlas maps on loop for loop (split_shipped_uvs: no bake, 0 MB
# added). Only vertex positions move. LOD2 adds a jaw proxy (C3 holds the rope point in grip part + jaw: ENVELOPE_JAW).
# --------------------------------------------------------------------------------------
ENVELOPE = False                    # --envelope (split only)
ENVELOPE_LETTER = "O"
ENVELOPE_Z = 0.06                   # m: the jaw band about the rope center the wheels may roll over
ENVELOPE_TR = {"block_y": -0.200,   # clamp block + clevis + pin + stem top run center (FG wheel 0.065 + boss)
               "block_w": 0.090, "block_z": (-0.060, GRIP_TOP_Z), "crown": 0.060,
               "plate_y": (-0.165, 0.040),        # the jaw plate: on the rope, 1 cm into the block
               "spring_axis_y": -0.100,           # spring housing turned 180 deg about z: outboard of the block
               "slope_deg": 34.0}                 # V -> U in the travel plane (38 centered): the leaned span keeps
                                                  # |VU| <= |TV|, |UP4| (the even S, fillets meet)
ENVELOPE_DQ = {"dy": -0.250,        # carriage shift (running wheels' inboard face 0.152 -> -0.098)
               "jaw_y_out": -0.105,               # jaw bar: from 1 cm proud inboard (0.04) out into the wheels
               "axle_y_in": -0.095,               # stub axles' inboard ends: back into the jaw bar
               "arm_y0": -0.235, "arm_k": 0.04,   # hanger top bend (z > rope + arm_z_min): drawn into the body
               "arm_z_min": -0.030}
ENVELOPE_ROLES = {"tr": ("body", "grip"), "dq": ("grip",)}    # the meshes the envelope changes (new letter)
# --------------------------------------------------------------------------------------
# Thin jaw (art-601, owner hand-test note 9, 2026-11-05): --envelope --thin-jaw, tr only, grip letter P. From above, the
# O plate read as a 0.68 x 0.205 m slab lying on a 0.048 m rope (4x the rope's width).
#
# Vertex moves only (the O atlas maps on loop for loop, 0 MB):
# - The plate's inboard edge comes in from rope + 0.040 to THIN_JAW["inboard_y"] (it still covers the rope's center line:
#   C3).
# - Its outboard ring (inside the clamp block) shrinks along travel to THIN_JAW["outboard_half"].
#
# So in plan the jaw tapers from its full length on the rope to the block's length where it enters the block. The result
# is a jaw on the rope and a short neck instead of a wide slab. The body is the O body, unchanged (keeps O).
#
# Q (the critique pass: P's edge on the rope center leaves half the rope bare beside a fin, and a fixed grip is a compact
# clamp straddling the rope): the inboard edge stays at O's +0.040, and the whole plate shortens along the rope, 0.44 m
# on the rope tapering to 0.24 m at the block. Every Q plate vertex stays inside the O plate's volume, so Q can add no
# overlap O does not have.
# --------------------------------------------------------------------------------------
THIN_JAW = False                    # --thin-jaw [P|Q] (with --envelope, tr)
THIN_JAW_SPECS = {
    "P": {"inboard_y": 0.010,       # m: the plate's inboard edge (O: 0.040, the rope's r is 0.024)
          "inboard_half": None,     # m: the inboard ring's half length (None: O's 0.340)
          "outboard_half": 0.054,   # m: the outboard ring's half length (O: 0.340): the block's 0.140 at its face
          "inboard_dz": 0.35},      # the inboard ring's height over the plate's flat underside x this: a wedge
    "Q": {"inboard_y": 0.040, "inboard_half": 0.22, "outboard_half": 0.12, "inboard_dz": 1.0},
}
# P is the shipping letter (owner verdict, round 2: "a thin tapered triangle of the shipped version", over the critique's
# Q). "inboard_dz" (round 2): P's long edge on the rope read as a fin. The plate now also thins toward that edge (crown
# 0.060 -> 0.037 over the rope top 0.024 at the edge, full at the block), so it reads as a tapered wedge. The squash is
# toward the profile's flat underside, so every vertex stays inside the round-1 P plate: no new overlap.
THIN_JAW_LETTER = "P"
THIN_JAW_SPEC = THIN_JAW_SPECS[THIN_JAW_LETTER]
ENVELOPE_JAW = {"tr": "gripjaw", "dq": "jaw_rope"}             # C3: the jaw beside the grip part
#: C15 (every split mesh, every LOD): lift_tower's wheel half width + 4 cm (FG 0.065, DQ 0.05) and the wheel bottoms
#: under the rope (FG 0.433, DQ 0.482) + margin. jaw_x = the outboard end of the jaw (LOD1 snap window)
ENVELOPE_GATE = {"tr": {"xlim": 0.105, "z_bot": 0.47, "jaw_x": ENVELOPE_TR["plate_y"][0]},
                 "dq": {"xlim": 0.09, "z_bot": 0.49, "jaw_x": ENVELOPE_DQ["jaw_y_out"]}}


def _envelope_lod_snap(lod, key, rope_z):
    """ENVELOPE LOD1: collapse decimation flattens the jaw's lowered crown arc a few mm ABOVE the band (the tr plate came
    out at rope + 0.062). Jaw vertices (net x >= jaw_x) within 1 cm over the band snap back onto it. Returns the count
    and the largest move."""
    me = lod.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    top = rope_z + ENVELOPE_Z
    hit = (co[:, 0] >= ENVELOPE_GATE[key]["jaw_x"] - 1e-4) & (co[:, 2] > top + 1e-6) & (co[:, 2] <= top + 0.01)
    mv = float((co[hit, 2] - top).max()) if hit.any() else 0.0
    co[hit, 2] = top
    me.vertices.foreach_set("co", co.reshape(-1))
    me.update()
    return f"{int(hit.sum())} jaw vertices snapped onto rope + {ENVELOPE_Z} (max {mv * 1000:.1f} mm)"


def check_envelope(obj, key, rope_z, label):
    """C15 (ENVELOPE, net frame: x lateral, -x outboard, z up, rope_z = the rope center in this mesh): every face's part
    beyond the jaw band (|z - rope| > ENVELOPE_Z) and within the wheels' reach (z - rope > -z_bot) lies outboard of x
    -xlim. Exact: each triangle is clipped to those slabs, so a face that only straddles the reach line is judged by its
    part above it."""
    g = ENVELOPE_GATE[key]
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    me.calc_loop_triangles()
    tri = np.empty(len(me.loop_triangles) * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("vertices", tri)
    T = co[tri.reshape(-1, 3)]
    T[:, :, 2] -= rope_z

    def clip(poly, k, c, keep_above):
        out = []
        for i in range(len(poly)):
            a, b = poly[i], poly[(i + 1) % len(poly)]
            ia, ib = (a[k] >= c) == keep_above, (b[k] >= c) == keep_above
            if ia:
                out.append(a)
            if ia != ib:
                out.append(a + (b - a) * ((c - a[k]) / (b[k] - a[k])))
        return out

    bad, worst = 0, -9.0
    eps = 1e-4
    for t in T:
        if t[:, 0].max() <= -g["xlim"] + 1e-6 or t[:, 2].max() < -g["z_bot"]:
            continue
        pts = clip(list(t), 2, ENVELOPE_Z + eps, True)
        lo = clip(clip(list(t), 2, -ENVELOPE_Z - eps, False), 2, -g["z_bot"], True)
        xs = [p[0] for p in pts + lo]
        if xs and max(xs) > -g["xlim"] + 1e-6:
            bad += 1
            worst = max(worst, max(xs))
    if bad:
        raise ContractError(f"CONTRACT C15 BROKEN ({label}): {bad} triangles pass rope +-{ENVELOPE_Z} "
                            f"inboard of x -{g['xlim']} within the wheels' reach (rope -{g['z_bot']}); "
                            f"worst x {worst:+.4f}")
    return f"0 faces past rope +-{ENVELOPE_Z} inboard of x -{g['xlim']} (reach rope -{g['z_bot']})"


def split_plan(key):
    """SPLIT_PLAN[key], with the envelope's LOD2 jaw proxy (+10 tris, pin_detail_lod2) when ENVELOPE."""
    plan = SPLIT_PLAN[key]
    if not ENVELOPE:
        return plan
    gp = list(plan["grip_proxy"])
    gp.insert(1, (ENVELOPE_JAW[key], 10))
    return dict(plan, grip_proxy=tuple(gp), pin_detail_lod2=plan["pin_detail_lod2"] + 10)


def _dq_envelope(parts, row):
    """ENVELOPE dq edits on the build-frame parts (before the S1 copies): vertex moves only."""
    E = ENVELOPE_DQ
    rope = float(row["hang_m"]) - float(row["grip_top_above_rope_m"])
    carriage = ("gripbody", "comb", "lever_", "spring_", "wheel_", "axle_")
    bpy.context.view_layer.update()
    for o, _q in parts:
        n = o.name
        if not (n.startswith(carriage) or n in ("jaw_rope", "tube_hanger")):
            continue
        CS.bake_transform(o)
        for v in o.data.vertices:
            if n.startswith(carriage):
                v.co.y += E["dy"]
                if n.startswith("axle_") and v.co.y > E["axle_y_in"] - 0.035:
                    v.co.y = E["axle_y_in"]
            elif n == "jaw_rope":
                if v.co.y < 0.0:
                    v.co.y = E["jaw_y_out"]
            elif v.co.z - rope > E["arm_z_min"] and v.co.y > E["arm_y0"]:
                v.co.y = E["arm_y0"] + (v.co.y - E["arm_y0"]) * E["arm_k"]
        o.data.update()
    return parts


def tr_split_stem_points(oz, back_top_z, lat=0.0):
    """The split tr J-stem (round 3). It is built from:
    - the one-piece stem's top and bottom points (stem_points)
    - the top run straight DOWN to V (x of the top point, TR_SPLIT_STEM v_z) under the jaw block
    - one span at slope_deg to U on the vertical run (x of the bottom point)
    - then down into the clamp block

    Raises unless both fillets can be |VU| / 2 (they meet: |TV| and |UP4| >= |VU|). Rope frame offset by oz. lat
    (ENVELOPE): T and V at lateral y `lat` (outboard of the wheels), U back on the center line."""
    old = stem_points(oz, back_top_z, top_dz=TR_STEM_TOP_DZ)
    (tx, _ty, tz), (bx, _by, bz) = old[0], old[-1]
    vz = oz + TR_SPLIT_STEM["v_z"]
    a = math.radians(ENVELOPE_TR["slope_deg"] if lat else TR_SPLIT_STEM["slope_deg"])
    uz = vz - abs(tx - bx) * math.tan(a)
    L = abs(tx - bx) / math.cos(a) if not lat else math.sqrt((tx - bx) ** 2 + lat ** 2 + (vz - uz) ** 2)
    if not (tz - vz >= L and uz - bz >= L):
        raise RuntimeError(f"tr split stem: |TV| {tz - vz:.4f}, |UP4| {uz - bz:.4f} < |VU| {L:.4f}: "
                           f"the fillets would not meet")
    if lat:
        return [(tx, lat, tz), (tx, lat, vz), (bx, 0.0, uz), old[-1]]
    return [old[0], (tx, 0.0, vz), (bx, 0.0, uz), old[-1]]


def _tr_back_top(oz=0.0):
    p = tr_params()
    return (oz - p.hanger_len + p.seat_thick / 2.0) + p.back_height


def _tr_split_rebuild(parts):
    """The tr split's split-only parts, rebuilt in place (list order, quads): the jaw block "grip" ->
    TR_SPLIT_GRIP's plate + clamp box joined as one "grip" (round 4), the saddle "gripjaw" dropped
    (the plate replaces it), the J-stem -> tr_split_stem_points (round 3). Build frame (rope z 0)."""
    K = cs2_kit
    p = tr_params()
    g = TR_SPLIT_GRIP
    out = []
    for o, q in parts:
        if o.name == "gripjaw":
            _remove_objects([o])
            continue
        if o.name == "grip":
            _remove_objects([o])
            bl, bw, bz0, bz1 = g["box"]
            if ENVELOPE:
                # the clamp block outboard (the mesh top), the plate ON the rope inside the envelope and
                # reaching into the block: the jaw the wheels roll over (Poma US4441430)
                E = ENVELOPE_TR
                bz0, bz1 = E["block_z"]
                box = K.add_box("grip_box", (bl, E["block_w"], bz1 - bz0),
                                (0.0, E["block_y"], 0.5 * (bz0 + bz1)))
                prof = [(x, E["crown"] if abs(z - GRIP_TOP_Z) < 1e-9 else z) for x, z in g["plate"]]
                py0, py1 = E["plate_y"]
                plate = add_profile_slab("grip_plate", prof, py1 - py0, (0.0, 0.5 * (py0 + py1), 0.0),
                                         relief="geo")
            else:
                box = K.add_box("grip_box", (bl, bw, bz1 - bz0), (0.0, 0.0, 0.5 * (bz0 + bz1)))
                plate = add_profile_slab("grip_plate", g["plate"], g["plate_w"], (0.0, 0.0, 0.0), relief="geo")
            for x in (box, plate):
                CS.bake_transform(x)
            bpy.ops.object.select_all(action="DESELECT")
            box.select_set(True)
            plate.select_set(True)
            bpy.context.view_layer.objects.active = plate
            bpy.ops.object.join()
            o = bpy.context.active_object
            o.name = o.data.name = "grip"
            o["relief"] = "geo"                            # one casting: no painted bolt heads
        elif ENVELOPE and o.name == "gripspring":
            # the spring housing turned 180 deg about z: from the jaw's inboard side to the block's outboard
            CS.bake_transform(o)
            yc = ENVELOPE_TR["spring_axis_y"]
            for v in o.data.vertices:
                v.co.x, v.co.y = -v.co.x, 2.0 * yc - v.co.y
            o.data.update()
        elif o.name == "tube_stem":
            relief = o.get("relief", "geo")
            _remove_objects([o])
            lat = ENVELOPE_TR["block_y"] if ENVELOPE else 0.0
            o = K.tube_along("tube_stem", tr_split_stem_points(0.0, _tr_back_top(), lat=lat), p.hanger_radius,
                             segments=8, caps="none", bend_rings=TR_SPLIT_STEM["bend_rings"])
            if o["rings"] != TR_SPLIT_STEM_RINGS:
                raise RuntimeError(f"tr split stem: {o['rings']} rings, want {TR_SPLIT_STEM_RINGS}")
            o["relief"] = relief
        out.append((o, q))
    return out


def _thin_jaw(grip, table):
    """THIN_JAW on the ASSEMBLED grip (net frame: x lateral, -x outboard, y travel). It runs after _apply_shipped_uvs,
    because the assembly orders polygons by position and the remap is polygon for polygon. The plate is the part "grip"'s
    vertices on its two lateral rings (O: x -0.165 inside the clamp block, x +0.040 inboard, the block's own faces sit at
    -0.155 / -0.245). The inboard ring moves to THIN_JAW_SPEC["inboard_y"]. The travel coordinate of every plate vertex
    scales linearly in its lateral position, from outboard_half / the plate's half length at the outboard ring to 1 at
    the inboard edge (a uniform scale of a convex profile per ring: still convex). Raises unless both rings hold the
    plate profile's 6 + 6 vertices. Returns a report."""
    me = grip.data
    fp = face_parts(grip)
    gid = table.index("grip") + 1
    vids = sorted({v for p in me.polygons if fp[p.index] == gid for v in p.vertices})
    py0, py1 = ENVELOPE_TR["plate_y"]
    out_r = [i for i in vids if abs(me.vertices[i].co.x - py0) < 1e-4]
    in_r = [i for i in vids if abs(me.vertices[i].co.x - py1) < 1e-4]
    if len(out_r) != 6 or len(in_r) != 6:
        raise ContractError(f"thin jaw {grip.name}: plate rings {len(out_r)} / {len(in_r)} vertices, want 6 / 6")
    yc = 0.5 * (min(me.vertices[i].co.y for i in in_r) + max(me.vertices[i].co.y for i in in_r))
    half = max(abs(me.vertices[i].co.y - yc) for i in in_r)
    s0 = THIN_JAW_SPEC["outboard_half"] / half
    h1 = THIN_JAW_SPEC.get("inboard_half") or half
    s1 = h1 / half
    x1 = THIN_JAW_SPEC["inboard_y"]
    k = THIN_JAW_SPEC.get("inboard_dz", 1.0)
    zb = min(me.vertices[i].co.z for i in in_r)            # the plate's flat underside (on the rope's top)
    zt = max(me.vertices[i].co.z for i in in_r)            # the crown (ENVELOPE_TR crown)
    for i in out_r:
        v = me.vertices[i].co
        v.y = yc + (v.y - yc) * s0
    for i in in_r:
        v = me.vertices[i].co
        v.x = x1
        v.y = yc + (v.y - yc) * s1
        v.z = zb + (v.z - zb) * k
    me.update()
    return {"inboard_x": [py1, x1], "half_length_m": {"outboard": [round(half, 4), THIN_JAW_SPEC["outboard_half"]],
                                                      "inboard": [round(half, 4), round(h1, 4)]},
            "inboard_edge_top_m": [round(zt, 4), round(zb + (zt - zb) * k, 4)],
            "plan_area_m2": [round(2 * half * (py1 - py0), 4),
                             round((h1 + THIN_JAW_SPEC["outboard_half"]) * (x1 - py0), 4)]}


def _ring_after(obj, ring):
    """The vertex ring joined by edges to `ring` (vertex indices) that is not `ring` itself."""
    rs = set(ring)
    nxt = set()
    for e in obj.data.edges:
        a, b = e.vertices
        if a in rs and b not in rs:
            nxt.add(b)
        elif b in rs and a not in rs:
            nxt.add(a)
    return sorted(nxt)


def _ring_normal(P):
    """Unit normal of a near-planar vertex ring (least squares) and its centroid."""
    c = P.mean(axis=0)
    n = np.linalg.svd(P - c)[2][-1]
    return n / np.linalg.norm(n), c


def split_alignment(style_key, body_parts, grip_parts, joint):
    """
    The tr clevis alignment audit (round 3, the owner's "bar slightly misaligned with the carrier"), measured on the
    built parts at zero pose. It raises when it regresses:
    - plan: the clevis (gripbridge + gripcheek_* + grippin_*, bbox center) and the pin under the jaw block's ("grip"
      bbox) center in plan: < SPLIT_ALIGN_PLAN_MAX_M
    - axis: the hanger tube's end axis (cut ring -> the next ring, centroids) is parallel to the clevis axis (pin -> the
      bridge center, square to the pin axis) within SPLIT_ALIGN_AXIS_MAX_DEG (a bend reaching into the clevis tilts that
      centroid line). The cut ring is square to it within the same angle. The pin is on that axis within
      SPLIT_ALIGN_AXIS_OFFSET_MAX_M.

    Returns the report dict (None for styles without the audit).
    """
    if style_key != "tr":
        return None
    by = {o.name: o for o, _q in body_parts + grip_parts}
    bpy.context.view_layer.update()

    def verts(o):
        return np.array([tuple(o.matrix_world @ v.co) for v in o.data.vertices], dtype=np.float64)

    def bbox_c(V):
        return 0.5 * (V.min(axis=0) + V.max(axis=0))

    pin = np.asarray(joint["pin"], dtype=np.float64)
    block_c = bbox_c(_tr_block_verts(verts(by["grip"])))
    clevis = [n for n in by if n == "gripbridge" or n.startswith(("gripcheek_", "grippin_"))]
    if len(clevis) != 5:
        raise RuntimeError(f"ALIGN tr: clevis parts {sorted(clevis)}, want the bridge + 2 cheeks + 2 pin ends")
    clevis_c = bbox_c(np.vstack([verts(by[n]) for n in clevis]))
    plan = float(np.linalg.norm((clevis_c - block_c)[:2]))
    pin_plan = float(np.linalg.norm((pin - block_c)[:2]))
    bridge_c = bbox_c(verts(by["gripbridge"]))
    ax_c = bridge_c - pin
    ax_c[1] = 0.0                                       # square to the pin axis (authoring y)
    ax_c /= np.linalg.norm(ax_c)
    stem = by["tube_stem"]
    S = verts(stem)
    # the cut ring = the stem's open end nearest the pin (boundary edges: one face each)
    nface = {}
    for poly in stem.data.polygons:
        for ek in poly.edge_keys:
            nface[ek] = nface.get(ek, 0) + 1
    bnd = sorted({i for ek, k in nface.items() if k == 1 for i in ek},
                 key=lambda i: float(np.linalg.norm(S[i] - pin)))
    cut = bnd[:8]
    if len(bnd) != 16 or np.linalg.norm(S[cut].mean(axis=0) - pin) > 0.01:
        raise RuntimeError(f"ALIGN tr: the stem's open ends ({len(bnd)} boundary vertices) put no ring of 8 "
                           f"at the pin {pin.round(4)}")
    nxt = _ring_after(stem, cut)
    if len(nxt) != 8:
        raise RuntimeError(f"ALIGN tr: the ring after the cut has {len(nxt)} vertices, want 8")
    n0, c0 = _ring_normal(S[cut])
    n1, c1 = _ring_normal(S[nxt])
    ax_t = (c0 - c1) / np.linalg.norm(c0 - c1)          # the tube end axis, pointing into the clevis

    def ang(a, b):
        return math.degrees(math.acos(min(1.0, abs(float(np.dot(a, b))))))

    axis_deg = math.degrees(math.acos(max(-1.0, min(1.0, float(np.dot(ax_t, ax_c))))))
    tilt = ang(n0, ax_t)
    off = float(np.linalg.norm(np.cross(pin - c0, ax_t)))
    rep = {"plan_offset_mm": round(plan * 1000.0, 3), "pin_plan_offset_mm": round(pin_plan * 1000.0, 3),
           "plan_max_mm": SPLIT_ALIGN_PLAN_MAX_M * 1000.0,
           "axis_angle_deg": round(axis_deg, 4), "cut_ring_tilt_deg": round(tilt, 4),
           "axis_max_deg": SPLIT_ALIGN_AXIS_MAX_DEG,
           # info: the bend's first ring leans by tube_along's smoothed tangent (not asserted, a bend inside the clevis
           # shows up as axis_angle_deg instead)
           "bend_ring_tilt_deg": round(ang(n1, ax_t), 4),
           "pin_off_axis_mm": round(off * 1000.0, 3), "pin_off_axis_max_mm": SPLIT_ALIGN_AXIS_OFFSET_MAX_M * 1000.0,
           "straight_end_m": round(float(np.linalg.norm(c0 - c1)), 4),
           "clevis_axis": [round(float(v), 5) for v in ax_c], "tube_end_axis": [round(float(v), 5) for v in ax_t],
           "jaw_block_center_m": [round(float(v), 4) for v in block_c],
           "clevis_center_m": [round(float(v), 4) for v in clevis_c]}
    bad = []
    if not (plan < SPLIT_ALIGN_PLAN_MAX_M and pin_plan < SPLIT_ALIGN_PLAN_MAX_M):
        bad.append(f"clevis {plan * 1000:.2f} mm / pin {pin_plan * 1000:.2f} mm off the jaw block center in plan "
                   f"(< {SPLIT_ALIGN_PLAN_MAX_M * 1000:.0f} mm)")
    if not (axis_deg <= SPLIT_ALIGN_AXIS_MAX_DEG and tilt <= SPLIT_ALIGN_AXIS_MAX_DEG):
        bad.append(f"tube end axis {axis_deg:.3f} deg off the clevis axis, cut ring tilted {tilt:.3f} deg "
                   f"(<= {SPLIT_ALIGN_AXIS_MAX_DEG} deg)")
    if off > SPLIT_ALIGN_AXIS_OFFSET_MAX_M:
        bad.append(f"pin {off * 1000:.2f} mm off the tube end axis (<= {SPLIT_ALIGN_AXIS_OFFSET_MAX_M * 1000:.0f} mm)")
    if bad:
        raise RuntimeError(f"ALIGN tr BROKEN: {'; '.join(bad)} | {rep}")
    return rep


def _tr_block_verts(V):
    """The tr jaw BLOCK's vertices of the "grip" part (build frame): all of it, or with ENVELOPE the clamp
    block outboard (the plate's edge buried in it included): the SPLIT_ALIGN reference stays the block."""
    if not ENVELOPE:
        return V
    return V[V[:, 1] <= ENVELOPE_TR["block_y"] + ENVELOPE_TR["block_w"] / 2.0 + 1e-6]


def split_names(style, letter):
    """(body name, grip name): SkiLiftChair6N / SkiLiftChair6GripN, SkiLiftChairDQ4N / ...GripN."""
    return (cs2_asset.validate_asset_name(f"{style.base}{letter}"),
            cs2_asset.validate_asset_name(f"{style.base}{SPLIT_GRIP_INFIX}{letter}"))


def _is_split_grip(style_key, name):
    return name.startswith(SPLIT_GRIP_PARTS[style_key])


def _dup_part(obj, name):
    c = obj.copy()
    c.data = obj.data.copy()
    c.name = name
    c.data.name = name
    bpy.context.collection.objects.link(c)
    return c


def _remove_objects(objs):
    for o in objs:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)


def _z_range(parts):
    bpy.context.view_layer.update()
    zs = [(o.matrix_world @ v.co).z for o, _q in parts for v in o.data.vertices]
    return min(zs), max(zs)


def split_wheel(name, r, depth, center, segs=SPLIT_WHEEL_SEGS):
    """A running wheel (axis = authoring y) at `segs` segments, world-baked at `center`. Both caps are
    segs-gons triangulated AFTER their normals exist (the lift_cabin C1 lesson): segs - 2 tris a cap
    instead of the kit's segs-tri fans, the same flat disc."""
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=int(segs), radius1=float(r),
                          radius2=float(r), depth=float(depth), calc_uvs=True)
    bm.normal_update()
    caps = [f for f in bm.faces if len(f.verts) > 4]
    if len(caps) != 2:
        raise RuntimeError(f"{name}: expected 2 cap n-gons, got {len(caps)}")
    bmesh.ops.triangulate(bm, faces=caps, quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.normal_update()
    obj = cs2_kit._new_obj(name, bm, tuple(center), (math.pi / 2.0, 0.0, 0.0), "geo")
    CS.bake_transform(obj)
    return obj


def _resegment_wheels(parts):
    """The dq split's wheels: each wheel_k rebuilt at SPLIT_WHEEL_SEGS on the same center, radius and width
    (DQ_GRIP["wheel"]). The function removes the 8-segment one-piece wheel. Same list order."""
    wr, wd, _wx = DQ_GRIP["wheel"]
    bpy.context.view_layer.update()                        # the kit parts carry loc / rot on the object
    out = []
    for o, q in parts:
        if not o.name.startswith("wheel_"):
            out.append((o, q))
            continue
        V = np.array([tuple(o.matrix_world @ v.co) for v in o.data.vertices])
        center = 0.5 * (V.min(axis=0) + V.max(axis=0))
        ext = V.max(axis=0) - V.min(axis=0)
        if abs(ext[1] - wd) > 1e-4 or abs(ext[0] - 2.0 * wr) > 1e-4 or abs(ext[2] - 2.0 * wr) > 1e-4:
            raise RuntimeError(f"{o.name}: extents {ext.round(4)} are not a y-axis wheel r {wr} x {wd}")
        name, relief = o.name, o.get("relief", "geo")
        _remove_objects([o])
        w = split_wheel(name, wr, wd, center)
        w["relief"] = relief
        if cs2_asset.tri_count(w) != SPLIT_WHEEL_TRIS:
            raise RuntimeError(f"{name}: {cs2_asset.tri_count(w)} tris, want {SPLIT_WHEEL_TRIS}")
        out.append((w, q))
    return out


def build_split_parts(style, row, hanger_offset=HANGER_OFFSET):
    """
    The split carrier's loose parts in the style's BUILD frame: the contracted one-piece parts first (C9 names + tally
    asserted), a copy of them for S1, then the joint:
    - tr: the jaw and the J-stem are first rebuilt split-only (_tr_split_rebuild: round 4's plate + clamp box "grip",
      round 3's straight-down stem top). The stem is cut square to its vertical top run at the pin, which sits under the
      jaw block's center. The two clamp plates (gripplate_*) give way to a bridge plate centered under the jaw block
      (gripbridge) carrying two yoke cheeks (gripcheek_*) with the pin ends (grippin_*). The body's knuckle sits on the
      stem's cut end between the cheeks.
    - dq: the C-hanger is cut twice in its vertical run. The top arm (tube_hangerarm) stays with the grip and ends in a
      head block (hangerhead) with two cheeks (hangercheek_*) and the pin ends (hangerpin_*). The body's hanger starts in
      the knuckle at the pin.

    Returns (body [(obj, quad)], grip [(obj, quad)], old [(obj, quad)], joint dict).
    """
    K = cs2_kit
    if style.key == "tr":
        parts = build_chair_parts(tr_params(), variant="tr")
    elif style.key == "dq":
        parts = build_dq_parts(row, hanger_offset)
    else:
        raise ValueError(f"--split: style {style.key!r} has no split")
    names = tuple(o.name for o, _q in parts)
    if names != style.parts:
        raise RuntimeError(f"C9 part list ({style.key}): built {names}, contract {style.parts}")
    tris = {o.name: cs2_asset.tri_count(o) for o, _q in parts}
    bad = {n: (tris[n], t) for n, t in style.part_tris if tris[n] != t}
    if bad:
        raise RuntimeError(f"C9 part tally ({style.key}): built vs contract {bad}")
    if style.key == "dq":
        parts = _resegment_wheels(parts)
        if ENVELOPE:
            parts = _dq_envelope(parts, row)
    else:
        parts = _tr_split_rebuild(parts)
    bpy.context.view_layer.update()
    old = [(_dup_part(o, f"old_{o.name}"), q) for o, q in parts]
    z_lo, _z_hi = _z_range(parts)
    J = SPLIT_JOINT[style.key]
    R, c, t, rc = J["knuckle_r"], CS.DESIGN_GAP_M, J["cheek_t"], J["cheek_r"]
    y_in, y_out = R + c, R + c + t
    by = {o.name: (o, q) for o, q in parts}
    hang = float(row["hang_m"])
    rope = hang - float(row["grip_top_above_rope_m"])
    if style.key == "tr":
        lat = ENVELOPE_TR["block_y"] if ENVELOPE else 0.0
        s0, s1 = (np.array(p, dtype=np.float64)
                  for p in tr_split_stem_points(0.0, _tr_back_top(), lat=lat)[:2])
        stem = by["tube_stem"][0]
        V = np.array([tuple(stem.matrix_world @ v.co) for v in stem.data.vertices])
        top_ring = V[np.linalg.norm(V - s0, axis=1) < 0.06]
        if len(top_ring) != 8 or np.linalg.norm(top_ring.mean(axis=0) - s0) > 1e-4:
            raise RuntimeError(f"split tr: the stem's top ring is not at {s0} (build frame changed?)")
        d = (s1 - s0) / np.linalg.norm(s1 - s0)           # straight down (the vertical top run)
        # the pin under the jaw block's center (round 3), R + c under the bridge plate
        bV = np.array([tuple(by["grip"][0].matrix_world @ v.co) for v in by["grip"][0].data.vertices])
        bV = _tr_block_verts(bV)
        bc = 0.5 * (bV.min(axis=0) + bV.max(axis=0))
        z_b = J["bridge_bottom"]
        pin = np.array((bc[0], bc[1], z_b - R - c), dtype=np.float64)
        if np.linalg.norm(np.cross(pin - s0, d)) > 1e-6:
            raise RuntimeError(f"split tr: the pin {pin.round(4)} is off the stem's top run {s0} + t {d.round(4)}")
        s2 = np.array(tr_split_stem_points(0.0, _tr_back_top(), lat=lat)[2], dtype=np.float64)
        bend_top = s1[2] + 0.5 * min(np.linalg.norm(s1 - s0), np.linalg.norm(s2 - s1))  # V fillet start
        if not bend_top < pin[2] - rc:
            raise RuntimeError(f"split tr: the stem bends at z {bend_top:.4f}, inside the clevis (cheek "
                               f"bottom {pin[2] - rc:.4f}): the top run must leave it straight")
        CS.cut_part(stem, pin, -d)
        knuck = CS.knuckle("knuckle", pin, R, J["knuckle_segs"], meridian_xz=(-d[2], d[0]))
        px, py, pz = float(pin[0]), float(pin[1]), float(pin[2])      # py 0 (ENVELOPE: the block's y)
        hx = rc + J["bridge_x_over"]
        bx0, bx1 = px - hx, px + hx
        bridge = K.add_box("gripbridge", (bx1 - bx0, 2.0 * (y_out + J["over"]), J["bridge_top"] - z_b),
                           ((bx0 + bx1) / 2.0, py, (z_b + J["bridge_top"]) / 2.0))
        bridge["relief"] = "map:bolts"
        prof = CS.plate_profile(px - rc, px + rc, J["cheek_top"], (px, pz), rc)
        new_grip = [(bridge, QUAD_GALV),
                    (CS.slab_y("gripcheek_0", prof, py - y_out, py - y_in), QUAD_GALV),
                    (CS.slab_y("gripcheek_1", prof, py + y_in, py + y_out), QUAD_GALV),
                    (CS.pin_end("grippin_0", py - y_out, (px, pz), J["pin_r"], J["pin_len"], -1.0), QUAD_GALV),
                    (CS.pin_end("grippin_1", py + y_out, (px, pz), J["pin_r"], J["pin_len"], 1.0), QUAD_GALV)]
        removed = [by[n][0] for n in ("gripplate_0", "gripplate_1")]
        joint = {"cheeks": ["gripcheek_0", "gripcheek_1"], "floor": ["gripbridge"],
                 "old_skip": ("old_gripplate_",),
                 "new_skip": ("gripbridge", "gripcheek_", "grippin_", "knuckle"),
                 "rope": np.array((0.0, 0.0, 0.0)), "replaced": ["gripplate_0", "gripplate_1"]}
    else:
        o = DQ_OUTBOARD
        H = dq_datums(row, hanger_offset)["H"]
        a0 = np.array((H[1][0], o * H[1][1], H[1][2]), dtype=np.float64)
        a1 = np.array((H[2][0], o * H[2][1], H[2][2]), dtype=np.float64)
        d = (a1 - a0) / np.linalg.norm(a1 - a0)

        def on_axis(z):
            return a0 + d * ((z - a0[2]) / d[2])

        hx, hh = J["head"]
        z_hb = J["head_top"] - hh
        pin = on_axis(z_hb - R - c)                        # head underside: c over the knuckle
        px, py, pz = (float(v) for v in pin)
        hanger = by["tube_hanger"][0]
        arm = _dup_part(hanger, "tube_hangerarm")
        CS.cut_part(arm, (0.0, 0.0, J["arm_cut_z"]), (0.0, 0.0, -1.0))   # keep z >= the cut
        CS.cut_part(hanger, (0.0, 0.0, pz), (0.0, 0.0, 1.0))              # keep z <= the pin
        arm["relief"] = hanger.get("relief", "geo")
        knuck = CS.knuckle("knuckle", pin, R, J["knuckle_segs"], meridian_xz=(1.0, 0.0))
        head = K.add_box("hangerhead", (hx, 2.0 * (y_out + J["over"]), hh),
                         (px, py, J["head_top"] - hh / 2.0))
        head["relief"] = "geo"                             # a clevis is a casting: no painted bolts
        prof = CS.plate_profile(px - rc, px + rc, z_hb + J["cheek_bury"], (px, pz), rc)
        new_grip = [(arm, QUAD_PAINT), (head, QUAD_GALV),
                    (CS.slab_y("hangercheek_0", prof, py - y_out, py - y_in), QUAD_GALV),
                    (CS.slab_y("hangercheek_1", prof, py + y_in, py + y_out), QUAD_GALV),
                    (CS.pin_end("hangerpin_0", py - y_out, (px, pz), J["pin_r"], J["pin_len"], -1.0),
                     QUAD_GALV),
                    (CS.pin_end("hangerpin_1", py + y_out, (px, pz), J["pin_r"], J["pin_len"], 1.0),
                     QUAD_GALV)]
        removed = []
        joint = {"cheeks": ["hangercheek_0", "hangercheek_1"], "floor": ["hangerhead"],
                 "old_skip": (), "new_skip": ("hangerhead", "hangercheek_", "hangerpin_", "knuckle"),
                 "rope": np.array((0.0, 0.0, rope)), "replaced": []}
    rem = {x.name for x in removed}
    body = [(knuck, QUAD_PAINT)] + [(ob, q) for ob, q in parts
                                    if ob.name not in rem and not _is_split_grip(style.key, ob.name)]
    grip = [(ob, q) for ob, q in parts
            if ob.name not in rem and _is_split_grip(style.key, ob.name)] + new_grip
    _remove_objects(removed)
    bpy.context.view_layer.update()
    joint.update({"pin": np.asarray(pin, dtype=np.float64), "knuckles": ["knuckle"],
                  "r_x": J["r_x"], "z_shift": -z_lo, "axis_dir": [round(float(v), 5) for v in d],
                  "knuckle_r": R, "gap_c": c, "cheek_inner": y_in, "cheek_outer": y_out,
                  "cheek_r": rc})
    return body, grip, old, joint


def split_pair(body, grip, grip_z0, name):
    """A joined COPY of body + grip at zero pose in the assembled frame (the grip copy raised by
    grip_z0), part table = body table + grip table (the grip's part ids offset): the one-piece
    carrier every C1-C14 contract runs on. The caller removes it."""
    tb, tg = part_table(body), part_table(grip)
    cb, cg = _dup_part(body, name), _dup_part(grip, f"{name}_g")
    attr = cg.data.attributes["part"]
    vals = np.empty(len(cg.data.polygons), dtype=np.int32)
    attr.data.foreach_get("value", vals)
    attr.data.foreach_set("value", (vals + len(tb)).tolist())
    cg.data.transform(mathutils.Matrix.Translation((0.0, 0.0, float(grip_z0))))
    cg.data.update()
    bpy.ops.object.select_all(action="DESELECT")
    cb.select_set(True)
    cg.select_set(True)
    bpy.context.view_layer.objects.active = cb
    bpy.ops.object.join()
    pair = bpy.context.active_object
    pair.name = name
    pair.data.name = name
    # the table as is: LOD1 / LOD2 have parts deleted or decimated away (no store_part_table check)
    pair.data[cs2_asset.PART_TABLE_PROP] = json.dumps(tb + tg)
    return pair


def split_pair_style(style, plan):
    """The pair's Style: the C8 / C9 numbers of body + grip (tallies, caps, LOD1 target)."""
    import dataclasses
    brief_cap = style.lod0_cap + plan["pin_detail"]
    raise_ = plan.get("cap_raise", 0)
    if raise_ > 0.05 * brief_cap:
        raise RuntimeError(f"split {style.key}: cap raise {raise_} > 5 % of the brief cap {brief_cap}: "
                           f"the owner's call, not a constant")
    return dataclasses.replace(
        style, lod0_cap=brief_cap + raise_,
        lod1_target=sum(plan["lod1"]),
        lod2_cap=style.lod2_cap + plan["pin_detail_lod2"],
        part_tris=tuple(plan["body_tally"]) + tuple(plan["grip_tally"]),
        proxy_tris=tuple(plan["body_proxy"]) + tuple(plan["grip_proxy"]))


def _add_octa(name, center, half_xy, z_top):
    """Closed octahedron (8 tris): the LOD2 knuckle, its top vertex exactly at z_top."""
    cx, cy, cz = center
    hz = z_top - cz
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    top = bm.verts.new((cx, cy, z_top))
    bot = bm.verts.new((cx, cy, cz - hz))
    ring = [bm.verts.new((cx + half_xy * math.cos(k * math.pi / 2.0), cy + half_xy * math.sin(k * math.pi / 2.0), cz))
            for k in range(4)]
    for k in range(4):
        k1 = (k + 1) % 4
        bm.faces.new((ring[k], ring[k1], top))
        bm.faces.new((ring[k1], ring[k], bot))
    bm.normal_update()
    for f in bm.faces:
        if f.normal.dot(f.calc_center_median() - mathutils.Vector(center)) < 0.0:
            f.normal_flip()
    bm.normal_update()
    cs2_kit._box_project_uv(bm)
    return _mesh_object(name, bm, (0.0, 0.0, 0.0), "geo")


def split_proxy_parts(style, row, joint, body_top, hanger_offset=HANGER_OFFSET):
    """
    The authored LOD2 of the split, in the ASSEMBLED frame (the one-piece proxy's frame): (body [(obj, chooser)], grip
    [(obj, chooser)]). From _proxy_parts: the grip keeps the grip box and gains a yoke box over the joint (bottom = the
    cheeks' bottom, top buried in the grip box). The body's stem / hanger restarts at the pin, plus an octahedron knuckle
    whose top vertex is the body LOD0's top (the height every level of the body keeps).
    """
    K = cs2_kit
    J = SPLIT_JOINT[style.key]
    hang = float(row["hang_m"])
    zs = joint["z_shift"]
    pin = joint["pin"] + np.array((0.0, 0.0, zs))
    px, py, pz = (float(v) for v in pin)
    rc, y_out = J["cheek_r"], joint["cheek_outer"] + J["over"]
    full = _proxy_parts(style, row, hanger_offset)
    by = {o.name: (o, ch) for o, ch in full}
    if style.key == "tr":
        p = tr_params()
        oz = hang - GRIP_TOP_Z
        back_top = _tr_back_top(oz)
        pts = tr_split_stem_points(oz, back_top, lat=py)  # round 3: the straight-down top run
        pts[0] = (px, py, pz)
        old_stem, old_grip = by["tube_stem"][0], by["grip"][0]
        _remove_objects([old_stem, old_grip])
        # ENVELOPE: the V fillet's top ring stays below the wheels' reach. The default fillet (half the pin -> V run,
        # 0.295 m) put that ring at rope - 0.41, so the two faces down to the V -> U chord had their upper vertices
        # inside the reach and their lower ones inboard of x -xlim (C15's exact clip passed, the conservative vertex form
        # did not). The fillet is capped at V's depth below the reach line minus the proxy radius (0.70 - 0.47 - 0.056 =
        # 0.174 m). Then the ring sits at rope - 0.53 and only the outboard straight run is within the reach. It has the
        # same 6 rings, 30 tris.
        fil = (-TR_SPLIT_STEM["v_z"] - ENVELOPE_GATE["tr"]["z_bot"] - PROXY_R["stem"]) if ENVELOPE else None
        stem = K.tube_along("tube_stem", pts, PROXY_R["stem"], segments=3, caps="none", bend_rings=2,
                            fillet=fil)
        # the proxy grip box as long as the round-4 plate, as wide as the clamp box (LOD bounds stay
        # with LOD0's), as deep as _proxy_parts'
        jx = [x for x, _z in TR_SPLIT_GRIP["plate"]]
        gl, gw, gh = max(jx) - min(jx), TR_SPLIT_GRIP["box"][1], GRIP_TOP_Z + 0.070
        if THIN_JAW:                                        # as long as the thin jaw (LOD0 bounds: Q 0.44 m)
            gl = min(gl, 2.0 * max(THIN_JAW_SPEC.get("inboard_half") or 0.5 * gl, THIN_JAW_SPEC["outboard_half"]))
        gy = ENVELOPE_TR["block_y"] if ENVELOPE else 0.0
        if ENVELOPE:
            gw = ENVELOPE_TR["block_w"]
        gbox = K.add_box("grip", (gl, gw, gh), (0.0, gy, oz + GRIP_TOP_Z - gh / 2.0), drop=("-Z",))
        g_lo = oz + GRIP_TOP_Z - gh                         # the proxy grip box's underside
        hx = rc + J["bridge_x_over"]
        x0, x1 = px - hx, px + hx
        yoke = K.add_box("gripyoke", (x1 - x0, 2.0 * y_out, (g_lo + 0.010) - (pz - rc)),
                         ((x0 + x1) / 2.0, py, ((g_lo + 0.010) + (pz - rc)) / 2.0), drop=("+Z",))
        grip = [(gbox, lambda n: ("grip", "gripjaw")), (yoke, lambda n: ("gripbridge", "gripcheek_"))]
        if ENVELOPE:                                        # the jaw plate on the rope, inside the envelope
            E = ENVELOPE_TR
            p0 = TR_SPLIT_GRIP["plate"][0][1]
            jy0, jy1 = E["plate_y"][0], THIN_JAW_SPEC["inboard_y"] if THIN_JAW else E["plate_y"][1]
            jaw = K.add_box("gripjaw", (gl, jy1 - jy0, E["crown"] - p0),
                            (0.0, 0.5 * (jy0 + jy1), oz + 0.5 * (p0 + E["crown"])), drop=("-Z",))
            grip.insert(1, (jaw, lambda n: ("grip",)))
        body = [(_add_octa("knuckle", (px, py, pz), J["knuckle_r"], body_top), lambda n: ("knuckle",)),
                (stem, lambda n: ("tube_stem",)), by["seatblock"], by["backblock"]]
    else:
        o = DQ_OUTBOARD
        d = dq_datums(row, hanger_offset)
        H = d["H"]
        old_h = by["tube_hanger"][0]
        _remove_objects([old_h])
        hz = K.tube_along("tube_hanger", [(px, py, pz), (H[2][0], o * H[2][1], H[2][2]),
                                          (0.0, o * H[3][1], d["hoop"][2][2])],
                          PROXY_R["hanger"], segments=3, caps="none", bend_rings=2)
        gz = DQ_GRIP["body"][2]
        g_lo = hang - (gz + 0.010)                            # the proxy carriage box's underside
        hx = J["head"][0] / 2.0
        yoke = K.add_box("hangeryoke", (2.0 * hx, 2.0 * y_out, (g_lo + 0.010) - (pz - rc)),
                         (px, py, ((g_lo + 0.010) + (pz - rc)) / 2.0), drop=("+Z",))
        grip = [by["gripbody"], (yoke, lambda n: ("hangerhead", "hangercheek_"))]
        if ENVELOPE:                                        # the carriage outboard, the jaw bar on the rope
            gb = by["gripbody"][0]
            CS.bake_transform(gb)
            for v in gb.data.vertices:
                v.co.y += ENVELOPE_DQ["dy"]
            gb.data.update()
            jl, _jw, jh = DQ_GRIP["jaw"]
            y0, y1 = ENVELOPE_DQ["jaw_y_out"], DQ_GRIP["jaw"][1] / 2.0
            rope = hang - float(row["grip_top_above_rope_m"])
            jaw = K.add_box("jaw_rope", (jl, y1 - y0, jh), (0.0, 0.5 * (y0 + y1),
                                                            rope + DQ_GRIP["jaw_dz"] + jh / 2.0), drop=("-Z",))
            grip.insert(1, (jaw, lambda n: ("jaw_rope",)))
        body = [(_add_octa("knuckle", (px, py, pz), J["knuckle_r"], body_top), lambda n: ("knuckle",)),
                (hz, lambda n: ("tube_hanger",))] + [by[n] for n in (
                    "hooptop", "hoopside_0", "hoopside_1", "seatblock", "backblock", "restraint",
                    "footstem_0", "footstem_1", "footbar_0", "footbar_1")]
    return body, grip


def build_split_proxy(pairs, lod0, name, z0, height):
    """One split mesh's authored LOD2 `<name>_LOD2` from `pairs` (assembled frame): tag ->
    join -> bake the transform -> shift by -z0 (the mesh's own base pivot) -> weld -> orient ->
    part table -> UV snap from `lod0` -> shade (flat weights) -> pin the height to 0 .. height."""
    choosers = {o.name: c for o, c in pairs}
    parts = [(o, None) for o, _c in pairs]
    table = cs2_asset.tag_parts(parts)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    lod = bpy.context.active_object
    lod.name = f"{name}_LOD2"
    lod.data.name = lod.name
    bpy.ops.object.select_all(action="DESELECT")
    lod.select_set(True)
    bpy.context.view_layer.objects.active = lod
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    lod.data.transform(mathutils.Matrix.Translation((0.0, 0.0, -float(z0))))
    lod.data.update()
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=WELD_M)
    bpy.ops.object.mode_set(mode="OBJECT")
    cs2_asset.orient_for_net(lod)
    cs2_asset.store_part_table(lod, table)
    snap_proxy_uvs(lod, lod0, choosers, _shipped_size(lod0))
    cs2_asset.shade_by_angle(lod, SHADE_ANGLE, weighted=False)
    pin_height(lod, 0.0, height)
    return lod


def _check_base_pivot(obj, height, label):
    z = authoring_coords(obj)[:, 2]
    if abs(float(z.min())) > 1e-5 or abs(float(z.max()) - height) > 1e-4:
        raise ContractError(f"CONTRACT SPLIT-PIVOT BROKEN ({label}): z {z.min():.5f} .. "
                            f"{z.max():.5f}, must be 0 .. {height:.4f} (base pivot)")
    return f"z 0 .. {height:.4f}"


def _split_tally(parts):
    return tuple((o.name, cs2_asset.tri_count(o)) for o, _q in parts)


SPLIT_MAPS = ("BaseColor", "Normal", "MaskMap", "ControlMask")


def split_shipped_uvs(st, row, ho, src_dir):
    """
    The shipped split meshes' atlas, read onto a rebuild of them (ENVELOPE off: the shipping code path
    through _assemble): every FBX triangle's three vertices land on one rebuilt polygon (positions match
    within 0.2 mm), each corner's UV onto that polygon's loop. Returns {role: {"name", "dir", "parts"
    (part per polygon), "counts" (loops per polygon), "uv" (n_loops, 2), "worst_mm", "ambiguous"}}.
    The envelope build moves vertices only, so its polygons line up one for one (_apply_shipped_uvs).
    """
    global ENVELOPE
    from mathutils.kdtree import KDTree
    keep = ENVELOPE
    ENVELOPE = False
    out = {}
    try:
        clear_scene()
        body_parts, grip_parts, old, _joint = build_split_parts(st, row, ho)
        _remove_objects([o for o, _q in old])
        names = split_names(st, SPLIT_LETTER)
        density = dict(st.density, **SPLIT_DENSITY[st.key])
        gt = _split_tally(grip_parts)
        for role, parts, name in (("body", body_parts, names[0]), ("grip", grip_parts, names[1])):
            flat = tuple(n for n in FLAT_PARTS.get(st.key, ()) if n in gt_names(gt)) if role == "grip" else ()
            obj, tab = _assemble(parts, name, density, density_w=True, flat_parts=flat)
            fbx = os.path.join(src_dir, name, name + ".fbx")
            before = set(bpy.data.objects)
            bpy.ops.import_scene.fbx(filepath=fbx)
            new = [o for o in bpy.data.objects if o not in before]
            imp = next(o for o in new if o.type == "MESH")
            mw = imp.matrix_world.copy()
            ico = np.array([tuple(mw @ v.co) for v in imp.data.vertices], dtype=np.float64)
            me = obj.data
            rco = np.array([tuple(obj.matrix_world @ v.co) for v in me.vertices], dtype=np.float64)
            kd = KDTree(len(rco))
            for i, c in enumerate(rco):
                kd.insert(c, i)
            kd.balance()
            best = None
            for cand in (ico, np.c_[ico[:, 0] / 100.0, ico[:, 2] / 100.0, ico[:, 1] / 100.0],
                         np.c_[ico[:, 0] / 100.0, -ico[:, 2] / 100.0, ico[:, 1] / 100.0]):
                err = max(kd.find(tuple(p))[2] for p in cand[:: max(1, len(cand) // 200)])
                if best is None or err < best[0]:
                    best = (err, cand)
            mp = best[1]
            vmap = np.empty(len(mp), dtype=np.int64)
            dmax = 0.0
            for i, p in enumerate(mp):
                _c, j, d = kd.find(tuple(p))
                vmap[i], dmax = j, max(dmax, d)
            if dmax > 2e-4:
                raise ContractError(f"remap {name}: the rebuild does not match its shipped LOD0 "
                                    f"({len(rco)} / {len(ico)} vertices, worst {dmax * 1000:.3f} mm)")
            vpolys = {}
            for p in me.polygons:
                for v in p.vertices:
                    vpolys.setdefault(v, set()).add(p.index)
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
                    if dst is None:
                        continue
                    u = tuple(uv_in.data[li].uv)
                    if not np.isnan(uv[dst, 0]) and np.abs(uv[dst] - u).max() > 1e-5:
                        conflict += 1
                    uv[dst] = u
            missing = int(np.isnan(uv[:, 0]).sum())
            if missing or conflict:
                raise ContractError(f"remap {name}: {missing} loops without a shipped UV, {conflict} conflicts "
                                    f"({amb} ambiguous triangles)")
            fp = face_parts(obj)
            out[role] = {"name": name, "dir": os.path.join(src_dir, name), "parts": list(fp),
                         "counts": [p.loop_total for p in me.polygons], "uv": uv,
                         "worst_mm": round(dmax * 1000.0, 4), "ambiguous": amb}
            _remove_objects(new)
        clear_scene()
    finally:
        ENVELOPE = keep
    return out


def _apply_shipped_uvs(obj, ref):
    """The shipped atlas (split_shipped_uvs) onto an envelope mesh, polygon for polygon: same part per
    polygon, same loops per polygon (vertex moves only), else raises. One UV layer UVUnique, no material
    slots (what bake_unique leaves)."""
    me = obj.data
    fp = face_parts(obj)
    counts = [p.loop_total for p in me.polygons]
    if list(fp) != ref["parts"] or counts != ref["counts"]:
        bad = sum(1 for a, b in zip(fp, ref["parts"]) if a != b)
        raise ContractError(f"remap {obj.name}: topology differs from {ref['name']} ({len(counts)} / "
                            f"{len(ref['counts'])} polygons, {bad} part mismatches): the envelope may move "
                            f"vertices only")
    lay = me.uv_layers.new(name="UVUnique", do_init=False)
    flat = ref["uv"].reshape(-1)
    lay.data.foreach_set("uv", flat.astype(np.float32))
    for l in [l for l in me.uv_layers if l.name != "UVUnique"]:
        me.uv_layers.remove(l)
    me.uv_layers["UVUnique"].active = True
    me.uv_layers["UVUnique"].active_render = True
    me.materials.clear()
    me.update()


def _main_split(args, st, row, letter):
    """--split: the GRIP and BODY meshes (see the section comment above), each with the full finish / LOD / verify /
    validate / manifest path of the one-piece build. ENVELOPE (--envelope): no bake. The build maps the shipped atlas
    onto the moved vertices (split_shipped_uvs, 0 MB added), and only the meshes the envelope changes (ENVELOPE_ROLES)
    get the new letter and files."""
    if st.key not in SPLIT_PLAN:
        raise ValueError(f"--split: style {st.key!r} has no split")
    plan = split_plan(st.key)
    ho = HANGER_OFFSET if args.hanger_offset is None else float(args.hanger_offset)
    hang = float(row["hang_m"])
    gtar = float(row["grip_top_above_rope_m"])
    body_name, grip_name = split_names(st, letter)
    remap = None
    write_role = {"body": True, "grip": True}
    if ENVELOPE:
        src = os.path.abspath(args.remap_from or os.path.join(HERE, "..", "import", "chairs", SPLIT_LETTER))
        remap = split_shipped_uvs(st, row, ho, src)
        old_body, old_grip = split_names(st, SPLIT_LETTER)
        roles = ENVELOPE_ROLES[st.key]
        if THIN_JAW:                       # only the grip changes. The body stays the O body
            roles = ("grip",)
            old_body = split_names(st, ENVELOPE_LETTER)[0]
        write_role = {r: r in roles for r in write_role}
        body_name = body_name if write_role["body"] else old_body
        grip_name = grip_name if write_role["grip"] else old_grip
        print(f"[split] ENVELOPE {st.key}: remap from {src}: " + "; ".join(
            f"{r} {v['name']} worst {v['worst_mm']} mm, {v['ambiguous']} ambiguous" for r, v in remap.items())
              + f"; writes {[r for r, w in write_role.items() if w]}")
    clear_scene()

    body_parts, grip_parts, old, joint = build_split_parts(st, row, ho)
    align = split_alignment(st.key, body_parts, grip_parts, joint)
    if align is not None:
        print(f"[split] ALIGN {st.key}: {align}")
    s1 = CS.rejoin(old, body_parts + grip_parts, [joint["pin"]], joint["r_x"], joint["old_skip"],
                   joint["new_skip"], label=st.key)
    print(f"[split] S1 rejoin {st.key}: {s1}")
    s23 = CS.sweep_joint(body_parts, grip_parts,
                         [(joint["pin"], joint["knuckles"], joint["cheeks"], joint["floor"])],
                         label=st.key)
    print(f"[split] S2/S3 sweep {st.key}: {s23}")
    _remove_objects([o for o, _q in old])

    bt, gt = _split_tally(body_parts), _split_tally(grip_parts)
    detail = sum(n for _p, n in bt + gt) - st.lod0_tris - plan.get("segs_detail", 0)
    if plan["body_tally"] is None or tuple(plan["body_tally"]) != bt or \
            tuple(plan["grip_tally"]) != gt or plan["pin_detail"] != detail:
        raise RuntimeError(f"C9 split tally ({st.key}): built body {bt}, grip {gt}, pin detail "
                           f"{detail}; contract body {plan['body_tally']}, grip {plan['grip_tally']}, "
                           f"pin detail {plan['pin_detail']}")
    zs = joint["z_shift"]
    gz0 = _z_range(grip_parts)[0] + zs
    bz0, bz1 = (z + zs for z in _z_range(body_parts))
    if abs(bz0) > 1e-6:
        raise RuntimeError(f"split {st.key}: the body's lowest point is z {bz0:.5f}, not the "
                           f"carrier's (0): the bottom moved to the grip?")
    body_h, grip_h = bz1, hang - gz0
    pin_asm = joint["pin"] + np.array((0.0, 0.0, zs))
    rope_asm = joint["rope"] + np.array((0.0, 0.0, zs))
    if abs(rope_asm[2] - (hang - gtar)) > 1e-6:
        raise RuntimeError(f"split {st.key}: rope point z {rope_asm[2]:.4f} != hang - grip_top "
                           f"{hang - gtar:.4f}")
    pivots = CS.pivot_numbers(rope_asm, pin_asm, gz0, 0.0, hang, gtar,
                              "outboard = authoring -Y" if st.key == "dq" else "")
    pivots["grip_height_m"] = round(float(grip_h), 4)
    pivots["body_height_m"] = round(float(body_h), 4)
    print(f"[split] pivots {st.key}: {json.dumps(pivots)}")

    density = dict(st.density, **SPLIT_DENSITY[st.key])
    quads = {o.name: q for o, q in body_parts + grip_parts}
    body, btab = _assemble(body_parts, body_name, density, density_w=True)
    grip, gtab = _assemble(grip_parts, grip_name, density, density_w=True,
                           flat_parts=tuple(n for n in FLAT_PARTS.get(st.key, ()) if n in gt_names(gt)))
    if remap is not None:
        _apply_shipped_uvs(body, remap["body"])
        _apply_shipped_uvs(grip, remap["grip"])
        if THIN_JAW:
            print(f"[split] THIN JAW {grip.name}: {_thin_jaw(grip, gtab)}")
    pst = split_pair_style(st, plan)
    meshes = {
        "body": {"obj": body, "name": body_name, "table": btab, "h": body_h, "z0": 0.0,
                 "plate": True, "tally": bt, "lod1": plan["lod1"][0], "lod1_del": plan["lod1_delete"][0],
                 "proxy": plan["body_proxy"]},
        "grip": {"obj": grip, "name": grip_name, "table": gtab, "h": grip_h, "z0": gz0,
                 "plate": False, "tally": gt, "lod1": plan["lod1"][1], "lod1_del": plan["lod1_delete"][1],
                 "proxy": plan["grip_proxy"]},
    }
    for role, m in meshes.items():
        m["info"] = {"table": m["table"], "quads": {n: quads[n] for n in m["table"]},
                     "part_tris": dict(m["tally"]), "lod0_tris": cs2_asset.tri_count(m["obj"])}
        m["contracts"] = {"pivot": _check_base_pivot(m["obj"], m["h"], f"{m['name']} LOD0")}
    pairs = {0: split_pair(body, grip, gz0, f"{body_name}Pair0")}
    pair_c = {"LOD0": check_contracts(pairs[0], pst, row, 0, ho)}
    pair0_tris = cs2_asset.tri_count(pairs[0])
    print("[split] pair contracts LOD0: " + "; ".join(f"{k} {v}" for k, v in pair_c["LOD0"].items()))

    work_root = tempfile.mkdtemp(prefix=f"cs2split_{body_name}_")
    lods = {"body": [], "grip": []}
    specs = style_detail_specs(st)
    if st.key == "tr":
        from cs2_compose import DetailSpec
        specs["grip"] = DetailSpec()                       # round 4: plate + clamp box, one casting
        specs["gripbridge"] = DetailSpec(bolt_pitch_m=0.05, bolt_r_m=0.008, bolt_h_m=0.004)
        specs["gripcheek_"] = DetailSpec()
        specs["grippin_"] = DetailSpec()
    else:
        from cs2_compose import DetailSpec
        specs["hangerhead"] = DetailSpec()                 # cast clevis: no bolt heads (relief geo)
    results = {}
    try:
        import cs2_bake
        import cs2_preview
        import cs2_review
        import cs2_validate
        tex_root = os.path.join(HERE, "..", "textures")
        quad_sets, control_quads = cs2_asset.lift_atlas_quads(tex_root)
        for role, m in meshes.items():
            obj, name = m["obj"], m["name"]
            asset_dir = os.path.join(os.path.abspath(args.out), name)
            if remap is not None:
                m["dir"] = asset_dir
                if not write_role[role]:
                    continue                              # unchanged by the envelope: keeps its shipped files
                os.makedirs(asset_dir, exist_ok=True)
                ref = remap[role]
                for x in SPLIT_MAPS:                      # the shipped maps under the new name (0 MB in game:
                    shutil.copyfile(os.path.join(ref["dir"], f"{ref['name']}_{x}.png"),   # same files)
                                    os.path.join(asset_dir, f"{name}_{x}.png"))
                m["bake"] = {"remap_from": ref["name"], "worst_vertex_mm": ref["worst_mm"],
                             "ambiguous_tris": ref["ambiguous"], "textures_added_mb": 0.0}
                m["contracts"]["finish"] = check_finished_maps(obj, m["info"], asset_dir, name, FINISH_ATLAS,
                                                               plate_required=m["plate"])
                print(f"[split] {name}: remapped onto {ref['name']}'s atlas: {m['contracts']['finish']['C12']}")
                cs2_asset.export_fbx(obj, os.path.join(asset_dir, f"{name}.fbx"))
                continue
            os.makedirs(asset_dir, exist_ok=True)
            m["dir"] = asset_dir
            work_dir = os.path.join(work_root, name)
            os.makedirs(work_dir, exist_ok=True)
            cs2_asset.make_texture_set_atlas(work_dir, name, quad_sets, quad_size=ATLAS_QUAD_PX,
                                             control_quads=control_quads, post=chair_atlas_post)
            m["contracts"]["atlas"] = check_atlas_stage(obj, m["info"], work_dir, name)
            maps = [f"{name}_{x}.png" for x in ("BaseColor", "Normal", "MaskMap", "ControlMask")]
            src = cs2_bake.SourceSet(base=os.path.join(work_dir, maps[0]),
                                     normal=os.path.join(work_dir, maps[1]), normal_convention="GL",
                                     mask=os.path.join(work_dir, maps[2]),
                                     control=os.path.join(work_dir, maps[3]))
            rep = cs2_bake.bake_unique(obj, asset_dir, name, FINISH_ATLAS, {0: src},
                                       ao_samples=AO_SAMPLES, src_uv="UVMap", margin_px=PACK_MARGIN_PX,
                                       post_compose=make_post_compose(obj, args.plate_number, specs,
                                                                      plate=m["plate"]))
            m["bake"] = {k: rep[k] for k in ("A", "margin_px", "ao", "uncovered_texels", "files")}
            m["bake"]["px_per_m"] = rep["pack"]["px_per_m"]
            m["contracts"]["finish"] = check_finished_maps(obj, m["info"], asset_dir, name, FINISH_ATLAS,
                                                           plate_required=m["plate"])
            print(f"[split] {name}: {m['contracts']['finish']['C12']}")
            cs2_asset.export_fbx(obj, os.path.join(asset_dir, f"{name}.fbx"))

        # LOD1 (decimated) + LOD2 (authored proxy) per mesh
        bproxy, gproxy = split_proxy_parts(st, row, joint, body_h, ho)
        for role, m, prox in (("body", meshes["body"], bproxy), ("grip", meshes["grip"], gproxy)):
            obj, name = m["obj"], m["name"]
            l1 = cs2_asset.make_lod_chain(obj, name, (m["lod1"],), (m["lod1_del"],),
                                          min_tris=min(60, m["lod1"]))[0]
            pin_height(l1, 0.0, m["h"])
            rope_z = m["h"] - gtar if role == "grip" else hang - gtar     # the rope center in this mesh
            if ENVELOPE and role == "grip":
                m["contracts"]["envelope_LOD1"] = _envelope_lod_snap(l1, st.key, rope_z)
            ptally = tuple((o.name, cs2_asset.tri_count(o)) for o, _c in prox)
            if m["proxy"] is None or tuple(m["proxy"]) != ptally:
                raise RuntimeError(f"LOD2 split proxy tally ({name}): built {ptally}, contract {m['proxy']}")
            l2 = build_split_proxy(prox, obj, name, m["z0"], m["h"])
            lods[role] = [l1, l2]
            for k, lod in enumerate(lods[role], start=1):
                m["contracts"][f"pivot_LOD{k}"] = _check_base_pivot(lod, m["h"], lod.name)
            if ENVELOPE:
                for k, lod in enumerate([obj, l1, l2]):
                    m["contracts"][f"C15_LOD{k}"] = check_envelope(lod, st.key, rope_z, f"{name} LOD{k}")
                    print(f"[split] C15 {name} LOD{k}: {m['contracts'][f'C15_LOD{k}']}")
            n0 = m["info"]["lod0_tris"]
            n1 = cs2_asset.tri_count(l1)
            if n1 > 0.5 * n0:
                raise ContractError(f"CONTRACT C8 BROKEN ({name}_LOD1): {n1} tris > 0.5 x {n0}")
        for k in (1, 2):
            pairs[k] = split_pair(lods["body"][k - 1], lods["grip"][k - 1], gz0, f"{body_name}Pair{k}")
            pair_c[f"LOD{k}"] = check_contracts(pairs[k], pst, row, k, ho,
                                                info={"lod0_tris": pair0_tris, "proxy": k == 2})
            print(f"[split] pair LOD{k}: {cs2_asset.tri_count(pairs[k])} tris, contracts "
                  f"{', '.join(sorted(pair_c[f'LOD{k}']))} OK")
        pair_c["silhouette"] = check_silhouettes(pairs[0], {1: pairs[1], 2: pairs[2]}, st, ho)
        print("[split] C10 silhouettes (pair): " + "; ".join(
            f"{k} {v}" for k, v in pair_c["silhouette"].items()))

        for role, m in meshes.items():
            obj, name, asset_dir = m["obj"], m["name"], m["dir"]
            if not write_role[role]:
                continue
            for lod in lods[role]:
                cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{lod.name}.fbx"))
            cs2_asset.write_settings(asset_dir, name, supplied_lods=(1, 2))
            tri_levels = [m["info"]["lod0_tris"]] + [cs2_asset.tri_count(l) for l in lods[role]]
            a = authoring_coords(obj)
            ext = a.max(axis=0) - a.min(axis=0)
            area = float(sum(p.area for p in obj.data.polygons))
            switch = cs2_preview.lod_switch_table((float(ext[1]), float(ext[0]), float(ext[2])),
                                                  tri_levels, area=area)
            density_rep = cs2_review.density_stats(obj, FINISH_ATLAS)
            if args.preview:
                prev = os.path.abspath(args.preview)
                for png in preview_rig(obj, asset_dir, name, prev, lods[role], None, tint=st.tint):
                    print(f"[split] preview -> {png}")
            verify = None
            if args.verify:
                flat = [n for n in FLAT_PARTS.get(st.key, ()) if n in m["table"]]
                ok0, info0 = verify_export(os.path.join(asset_dir, f"{name}.fbx"), expect_height=m["h"],
                                           flat_box=fbx_box_of_part(obj, flat[0]) if flat else None)
                oks = [ok0] + [verify_export(os.path.join(asset_dir, f"{name}_LOD{k}.fbx"),
                                             expect_height=m["h"], histogram=False)[0] for k in (1, 2)]
                if not all(oks):
                    raise RuntimeError(f"verify {name}: height / base pivot (or C14) failed on re-import: {oks}")
                if not info0["custom_normals"]:
                    raise RuntimeError(f"verify {name}: LOD0 re-import carries no custom normals")
                verify = {"height_m": info0["height_m"], "levels_ok": oks,
                          "normals_per_vertex": info0["normals_per_vertex"]}
                if flat:
                    verify["flat_faces_LOD0"] = info0["flat_faces"]
            spec = {"kind": "hanging", "lod0_cap": sum(n for _p, n in m["tally"]),
                    "lod2_cap": sum(n for _p, n in m["proxy"]), "expected_lods": (1, 2)}
            findings = cs2_validate.validate_asset_folder(asset_dir, name, spec)
            validation = {"errors": 0, "warnings": sum(1 for f in findings if f[0] == "WARN"),
                          "warn_list": [f[1] + ": " + f[2] for f in findings if f[0] == "WARN"]}
            split = {"role": role, "partner": meshes["grip" if role == "body" else "body"]["name"],
                     "pivot": pivots, "alignment": align, "S1_rejoin": s1, "S2_S3_sweep": s23,
                     "pair_contracts": pair_c, "pair_style": {"lod0_cap": pst.lod0_cap,
                                                              "lod1_target": pst.lod1_target,
                                                              "lod2_cap": pst.lod2_cap,
                                                              "pin_detail": plan["pin_detail"],
                                                              "pin_detail_lod2": plan["pin_detail_lod2"],
                                                              "segs_detail": plan.get("segs_detail", 0),
                                                              "cap_raise": plan.get("cap_raise", 0)},
                     "joint": {k: (v.tolist() if isinstance(v, np.ndarray) else v)
                               for k, v in joint.items() if k not in ("old_skip", "new_skip")},
                     "replaces": f"{st.base}{st.letter} (one piece)"}
            cs2_validate.write_manifest(asset_dir, name, {
                "generator": "lift_chair.py --split", "style": st.key, "letter": letter,
                "kind": "hanging", "carrier": {st.carrier: row},
                "tris": {f"LOD{k}": n for k, n in enumerate(tri_levels)},
                "caps": spec, "part_table": m["table"], "part_tris": m["info"]["part_tris"],
                "contracts": m["contracts"], "lod_switch": switch, "density": density_rep,
                "bake": m["bake"], "lod2": "proxy", "verify": verify, "validation": validation,
                "split": split, "hanger_offset_m": ho if st.key == "dq" else None,
                "envelope": ({"z_m": ENVELOPE_Z, **({"tr": ENVELOPE_TR, "dq": ENVELOPE_DQ}[st.key])}
                             if ENVELOPE else None),
                "args": CS.manifest_args(args, REPO_ROOT)})
            results[name] = {"tris": tri_levels, "validation": validation, "verify": verify,
                             "dir": asset_dir}
            if args.review is not None:
                cs2_review.write_pack(name, asset_dir, dict(enumerate([obj] + lods[role])), {},
                                      meta={"generator": "lift_chair.py --split",
                                            "args": CS.manifest_args(args, REPO_ROOT),
                                            "kind": "hanging", "letter": letter,
                                            "caps": {0: spec["lod0_cap"], 2: spec["lod2_cap"]},
                                            "tint": dict(st.tint), "style": st.key},
                                      out_dir=args.review or None)
    finally:
        _remove_objects([p for p in pairs.values()])
        for role in lods:
            _remove_objects(lods[role])
        shutil.rmtree(work_root, ignore_errors=True)

    for name, r in results.items():
        print(f"PASS lift_chair --split: {name} {'/'.join(str(t) for t in r['tris'])} tris, "
              f"validation 0 errors / {r['validation']['warnings']} warnings"
              + (f", verify OK {r['verify']['normals_per_vertex']}" if r["verify"] else ""))
    print(f"PASS lift_chair --split {st.key}: S1 {s1['max_old_to_new_mm']}/{s1['max_new_to_old_mm']} mm, "
          f"S2 gap {s23['gap_mm_min']}..{s23['gap_mm_max']} mm over {s23['poses']} poses, S3 0 clashes, "
          f"pair LOD0 {pair0_tris} (cap {pst.lod0_cap})"
          + (f", ALIGN clevis {align['plan_offset_mm']} mm off the block center in plan, tube end axis "
             f"{align['axis_angle_deg']} deg off the clevis axis" if align else ""))


def gt_names(tally):
    return {n for n, _t in tally}


def main():
    if not bpy.app.background:
        raise RuntimeError("lift_chair.py runs headless only (blender --background): generator "
                           "code never runs in the live review session (ROUND-SPEC R10)")
    args = parse_args()
    st, row, letter, name = _resolve(args)
    if args.split:
        return _main_split(args, st, row, letter)
    if st.key == "fg6l":
        return _main_fg6l(args, st, name)

    bake = args.bake or st.bake
    lod2_mode = args.lod2 or st.lod2
    hanger_offset = HANGER_OFFSET if args.hanger_offset is None else float(args.hanger_offset)
    hang = float(row["hang_m"])
    clear_scene()

    chair, info = build_style_chair(st, row, name, hanger_offset)
    contracts = {"LOD0": check_contracts(chair, st, row, 0, hanger_offset)}
    tris = cs2_asset.tri_count(chair)
    info["lod0_tris"] = tris
    a = authoring_coords(chair)
    ext = a.max(axis=0) - a.min(axis=0)
    rope = hang - float(row["grip_top_above_rope_m"])
    axle = row.get("axle_m")
    print(f"[chair] style {st.key} ({name}): {len(info['table'])} parts, {tris} tris "
          f"(cap {st.lod0_cap}), extents travel {ext[0]:.3f} x lateral {ext[1]:.3f} x "
          f"height {ext[2]:.3f} m")
    print(f"[chair] carrier {st.carrier}: hang {hang:.3f}, rope at z {rope:.3f} "
          f"(grip top {hang - rope:.3f} above it), lane +-{row['lane_x_m']}"
          + (f", axle {axle}: chair bottom {axle - rope:.2f} m over the edge at the towers "
             f"once the rope runs through the grip (ChairPin today pins the mesh top on the "
             f"lane: {axle - hang:.2f} m, dq-chair.md 9 Q1)" if axle else ""))
    print("[chair] contracts LOD0: " + "; ".join(f"{k} {v}" for k, v in contracts["LOD0"].items()))

    asset_dir = os.path.join(os.path.abspath(args.out), name)
    os.makedirs(asset_dir, exist_ok=True)
    work_dir = tempfile.mkdtemp(prefix=f"cs2chair_{name}_")   # per run (skeptic D13)
    lods = []
    try:
        tex_root = os.path.join(HERE, "..", "textures")
        quad_sets, control_quads = cs2_asset.lift_atlas_quads(tex_root)
        cs2_asset.make_texture_set_atlas(work_dir, name, quad_sets, quad_size=ATLAS_QUAD_PX,
                                         control_quads=control_quads, post=chair_atlas_post)
        contracts["atlas"] = check_atlas_stage(chair, info, work_dir, name)
        maps = [f"{name}_{m}.png" for m in ("BaseColor", "Normal", "MaskMap", "ControlMask")]
        specs = style_detail_specs(st)
        bake_report = None
        finished = False
        if args.finish and bake == "v2":
            import cs2_bake
            src = cs2_bake.SourceSet(base=os.path.join(work_dir, maps[0]),
                                     normal=os.path.join(work_dir, maps[1]),
                                     normal_convention="GL",
                                     mask=os.path.join(work_dir, maps[2]),
                                     control=os.path.join(work_dir, maps[3]))
            rep = cs2_bake.bake_unique(chair, asset_dir, name, FINISH_ATLAS, {0: src},
                                       ao_samples=AO_SAMPLES, src_uv="UVMap",
                                       margin_px=PACK_MARGIN_PX,
                                       post_compose=make_post_compose(chair, args.plate_number,
                                                                      specs))
            bake_report = {k: rep[k] for k in ("A", "margin_px", "ao", "uncovered_texels", "files")}
            bake_report["px_per_m"] = rep["pack"]["px_per_m"]
            finished = True
        elif args.finish:
            for m in maps:
                shutil.copyfile(os.path.join(work_dir, m), os.path.join(asset_dir, m))
            import lift_tower
            finished = bool(lift_tower.apply_finish_textures(chair, asset_dir, name, FINISH_ATLAS,
                                                             ao_samples=AO_SAMPLES))
            if not finished:
                raise RuntimeError("legacy finish failed")
            post_finish_textures(chair, asset_dir, name, FINISH_ATLAS, args.plate_number, specs)
        else:
            for m in maps:
                shutil.copyfile(os.path.join(work_dir, m), os.path.join(asset_dir, m))
            print("[chair] no --finish: atlas layout shipped, number plate unpainted")
        A = FINISH_ATLAS if finished else 2 * ATLAS_QUAD_PX
        if finished:
            contracts["finish"] = check_finished_maps(chair, info, asset_dir, name, A)
            print(f"[chair] {contracts['finish']['C12']}")

        cs2_asset.export_fbx(chair, os.path.join(asset_dir, f"{name}.fbx"))

        if args.lods:
            lods, lod_res = build_lods(chair, st, row, name, lod2_mode, hanger_offset, info)
            contracts.update(lod_res)
            contracts["silhouette"] = check_silhouettes(chair, dict(enumerate(lods, start=1)),
                                                        st, hanger_offset)
            print("[chair] C10 silhouettes (cover, extra): " + "; ".join(
                f"{k} {v}" for k, v in contracts["silhouette"].items()))
            for lod in lods:
                cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{lod.name}.fbx"))
        cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
        print(f"[chair] asset folder: {asset_dir}")

        import cs2_preview
        import cs2_review
        import cs2_validate
        tri_levels = [tris] + [cs2_asset.tri_count(l) for l in lods]
        area = float(sum(p.area for p in chair.data.polygons))
        size = (float(ext[1]), float(ext[0]), float(ext[2]))
        switch = cs2_preview.lod_switch_table(size, tri_levels, area=area)
        print("[chair] predicted LOD switch (lodFactor 652, unverified): "
              + ", ".join(f"LOD{k} {v:.0f} m" for k, v in switch["switch"].items())
              + f", cull {switch['cull']:.0f} m")
        density = cs2_review.density_stats(chair, A)

        if args.preview:
            prev = os.path.abspath(args.preview)
            ab = args.ab
            if ab is None and st.ab:
                cand = os.path.join(HERE, "..", "import", *st.ab)
                ab = os.path.abspath(cand) if os.path.isdir(cand) else None
            for png in preview_rig(chair, asset_dir, name, prev, lods, ab, tint=st.tint):
                print(f"[chair] preview -> {png}")
            if st.key == "dq":
                # Revision 2: the bar UP (C13's end pose) for the verdict on the mechanics. It is a re-shaded duplicate,
                # removed again (nothing exported comes from it)
                for png in preview_bar_up(chair, row, asset_dir, name, prev, st.tint):
                    print(f"[chair] preview -> {png}")
                # Revision 3: the bar against boxy seated riders (stand-ins, never exported)
                for png in preview_riders(chair, row, asset_dir, name, prev, st.tint):
                    print(f"[chair] preview -> {png}")

        verify = None
        if args.verify:
            flat = FLAT_PARTS.get(st.key)
            ok0, info0 = verify_export(os.path.join(asset_dir, f"{name}.fbx"), expect_height=hang,
                                       flat_box=fbx_box_of_part(chair, flat[0]) if flat else None)
            results = [ok0]
            for k in range(1, len(lods) + 1):
                okk, _ = verify_export(os.path.join(asset_dir, f"{name}_LOD{k}.fbx"),
                                       expect_height=hang, histogram=False)
                results.append(okk)
            if not all(results):
                raise RuntimeError(f"verify: height (or LOD0 C14 flat carriage) contract failed on "
                                   f"re-import: {results}")
            if not info0["custom_normals"]:
                raise RuntimeError("verify: LOD0 re-import carries no custom normals")
            verify = {"height_m": info0["height_m"], "levels_ok": results,
                      "normals_per_vertex": info0["normals_per_vertex"]}
            if flat:
                verify["flat_faces_LOD0"] = info0["flat_faces"]

        validation = None
        if finished:
            spec = {"kind": "hanging", "lod0_cap": st.lod0_cap, "lod2_cap": st.lod2_cap,
                    "expected_lods": tuple(range(1, len(lods) + 1)) or None}
            findings = cs2_validate.validate_asset_folder(asset_dir, name, spec)
            validation = {"errors": 0, "warnings": sum(1 for f in findings if f[0] == "WARN")}

        cs2_validate.write_manifest(asset_dir, name, {
            "generator": "lift_chair.py", "style": st.key, "letter": letter,
            "carrier": {st.carrier: row},
            "tris": {f"LOD{k}": n for k, n in enumerate(tri_levels)},
            "part_table": info["table"], "part_tris": info["part_tris"],
            "contracts": contracts, "lod_switch": switch, "density": density,
            "bake": (bake_report or bake) if finished else None,
            "lod2": lod2_mode if lods else None,
            "verify": verify, "validation": validation,
            "hanger_offset_m": hanger_offset if st.key == "dq" else None,
            "args": CS.manifest_args(args, REPO_ROOT),
        })

        # -- live-review pack (ROUND-SPEC R10): after export, previews, --verify, the validator and the manifest
        # (pipeline-gap.md section 6, p0a fix 15). DUPLICATES only, nothing above changes (gate:
        # tests/test_review_side_effects.py)
        if args.review is not None:
            cs2_review.write_pack(name, asset_dir, dict(enumerate([chair] + lods)), {},
                                  meta={"generator": "lift_chair.py", "args": CS.manifest_args(args, REPO_ROOT),
                                        "kind": "hanging", "letter": letter,
                                        "caps": {0: st.lod0_cap, 2: st.lod2_cap},
                                        "tint": dict(st.tint), "style": st.key,
                                        "carrier": st.carrier},
                                  out_dir=args.review or None)
    finally:
        for lod in lods:
            me = lod.data
            bpy.data.objects.remove(lod, do_unlink=True)
            bpy.data.meshes.remove(me)
        shutil.rmtree(work_dir, ignore_errors=True)

    print(f"PASS lift_chair: {name} style {st.key}, {'/'.join(str(t) for t in tri_levels)} tris, "
          f"height {hang:.3f} m on every level, finish {('on (' + bake + ')') if finished else 'off'}"
          + (f", LOD2 {lod2_mode}" if lods else "")
          + (f", verify OK, normals histogram {verify['normals_per_vertex']}" if verify else "")
          + (f", validation 0 errors / {validation['warnings']} warnings" if validation else ""))


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - make the exit status unambiguous for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
