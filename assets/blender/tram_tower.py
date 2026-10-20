"""
tram_tower.py - lattice towers: the aerial-tram line towers SkiTramTower30A / 45A / 60A
(assets/import/tramtowers/A) and the gondola lattice variants SkiGondolaLattice25O / 32O / 40O
(assets/import/towers/O, written only into their own asset folders).

Contract: docs/research/art/assets-2026-10/design/lattice-towers.md with the skeptic's MUST-CHANGE list
(lattice-towers-skeptic.md section 4) applied, overridden where they conflict by the overnight rulings
R27-R31 of ROUND-SPEC.md. Build report: docs/research/art/assets-2026-10/build/tram_tower.md.

Self-contained on cs2_kit / cs2_asset / cs2_uv / cs2_compose / cs2_validate (+ cs2_bake, cs2_preview,
cs2_review imported lazily: they pull lift_tower in through cs2_preview, which another fleet is rebuilding,
so a geometry-only build and the tests never depend on it). lift_tower.py and the shared cs2_lattice /
lift_tower hook of the design are NOT used: every lattice helper lives in this module (task rule).

WHAT THE RULINGS CHANGED AGAINST THE CONTRACT
    R27  tram hang 10.50 (cabin floor underside -> carriage top); the track rope sits 0.50 below the
         carriage top, so the carriage rides ABOVE the rope (dz -0.05..+0.50) and the cabin underside is
         rope - 10.00. The carrier envelope (CARRIER_TRAM) is the cabin lens' R27-R30 build
         (lift_cabin.py T / tram_dims, read-only).
    R28  HV "rider" lane = the TRACK rope at y +-5.50 (one per side, saddles); the HAUL rope is a
         non-HV (LV) lane at y +-5.00, 0.40 below (haul rollers). No comm wire on the tram.
    R29  identity hosting on both lanes -> the cabin is left/right symmetric: its hanger is a CLOSED
         ring around the rope (uprights |dy| 0.82..1.08, crossbar below dz -3.80, carriage head
         above). A saddle support must cross that ring's sweep: the inboard upright passes through
         the two saddle arms (accepted contact, 0.07 s per arm at 10 m/s, manifest
         g_clear.accepted_contacts_level); the haul grips ride over the haul rollers.
    R30  carriage/hanger half-width <= 1.10; the tower stays <= lane + 0.36 for dz -0.80..+0.60 and
         <= lane + 0.80 for dz -3.80..-0.70, read two-sided (R29) like the cabin lens' k1 / k2.
    R31  per-type rope lanes with m_Hanging 0.004: the lane end slope at a tower is 4 h = 0.016, not
         the vanilla 0.12. Saddle tops and haul-roller tops follow that kinked rope line exactly (no
         burial on a level span); the gondola sheave train stays straight (0.016 is invisible).
    D9 + task: footings are 4 OPEN-BOTTOM pedestals whose side edges end exactly at z 0 (no bottom
         face), with an open pier continuing below (the welded column hides float on steep nodes).

SKEPTIC MUST-CHANGE (section 4) - where each landed
    1  rope / hang row from the rulings; catalog/carriers.json rows read and cross-checked
       (load_catalog; a stale row is a WARN + manifest "catalog_drift", the ruling wins)
    2  grade-aware C8: grade_sweep() moves the level cabin + carriage along the kinked lane at slope
       g -+ end_slope up to the catalog max slope; first clashing g per carrier box and class
       (structure / rope-support) -> manifest. Option taken: MOVE the head frame inside the cabin's
       inner edge - tram head chords at y +-3.40 (outer face 3.55 = 5.50 - 1.85 - 0.10, was 4.25 in
       D6) with 2.1 m saddle arms; gondola deck inboard (|y| <= 1.775 < 1.95). Floor (raise):
       structure g_clear of the level (non-pitched) carrier boxes >= 0.80 tram / 1.00 gondola.
    3  saddle: rope-following top (no burial at level), SADDLE_L a parameter (--saddle-l, default 4.0)
    4  generator-local post_compose(obj, detail) and generator-local detail renders (render_details)
    5  budgets: LOD2 500 / 400 raise, the scaled 450 / 470 warn; tallies measured, not estimated
    6  gondola envelope: cabin underside = A - 4.50 exactly, lateral 2.10 (+0.04 bumpers)
    7  carving block in the manifest states the piece-width condition and both rows
    8  member caps on free ends; each tram leg ONE prism over [foot, kink, head] with a mitred
       ring at the kink (no V-notch); pier allowance uses the corner diagonal
    9  citations live in the build report (PIPELINE line drift fixed there)
    10 validator kind passed explicitly (validate_asset_folder kind "hanging" = no per-part density floor, like lift_tower; assets-check -Kind hanging (DECISIONS 315: the 30 px/m prop floor fails every shipped lattice / tram tower))

REVISION 1 - Richard's first live review (verdicts-2026-10-11.md items 7 / 8; reference digest
ref-liftblog/digest.md sections 1.3-1.5: P2P-03/11/15/24/26/33, JH-09/15). Richard's verdict wins over
the contract; the deviations are listed in build/tram_tower.md "Revision 1".
    V7a  ONE ladder system from the GROUND to the head, outside the shaft on the -X face (both families;
         the gondola had none by default, D8 superseded): build_ladder_system / ladder_plan.
    V8a  climbable: rest platforms are small SIDE BASKETS cantilevered from a ring strut beside the
         ladder, and each next flight starts on the basket OFFSET sideways (lines y 0 / +1.0 tram,
         0 / +0.75 gondola) - never a centered landing over the flight below. C7 now asserts reach
         (rails from z 0 to arrival + 1.0), the offset, flight length and a clear climbing corridor
         per flight; the inner centered landings are gone (the plan diaphragms stay).
    V7b  tram saddles hang from an UPPER STABILIZING FRAME: inner posts continue the leg tops to the
         top beams (one story above the saddles), hanger posts rise from the crossarm ends 1.25 m
         inboard of each lane to the top beams, main diagonals run from the inner post tops down to
         the crossarm ends, knees brace the top-beam tips that overhang each rope lane. Under R29
         (closed symmetric hanger ring) and R30 nothing may come down onto the saddle from above or
         from the side, so the frame reaches the saddle through the crossarm (the only R29 crossing,
         already accepted) - recorded as a deviation from the photos' saddle-landing diagonals.
    V8b  the "rollers": each saddle is one long curved box girder (default L 4.8 = 1.6 x the head
         width, parabolic lead-out), and the haul rope runs on a ROW of small sheaves along the
         saddle's lower inboard edge (JH-15) on a carrier bar tied to the girder; no straight roller
         beam any more.
    V8c  the top parts have a purpose: a slewing pillar jib crane per side (column on the head deck,
         jib at the top-beam level reaching over its rope lane, trolley + hook + covered winch; the
         evacuation crane of P2P-15) and lifting lugs under every top-beam tip over the lanes
         (JH-15). Gondola: lifting lugs under the gantry-beam tips over both lanes.
    C11  the y-mirror check exempts the ladder system only (offset flights cannot be symmetric).

REVISION 1b - fixups (build/revision1-check-tram-towers.md F1, 2026-10-11): the flight-0 rails run on
down LADDER_FAM sink below grade (1b: tram 2.0, gondola 2.7; plumb, no extra segment, 0 tris), which
covers the downhill foot on the corner-diagonal slope at the piers' grade allowance. C7 reach became
"rails from z <= 0". (1b also claimed the foot stays buried wherever the piers keep the legs buried:
false on a floated base and along the face normal - revision1b-check-tram N1 / N2, fixed in 1c.)

REVISION 1c - ladder N1 (build/revision1b-check-tram.md N1 / N2 / N7, 2026-10-11): the D13 score floats
the 30A / 45A base up to 10.0 m and the 25O / 32O base up to 4.66 / 5.33 m (variant_float), so on those
four flight 0 runs its rails AND rungs down to the pier depth (-12 / -8, ladder_depth): a floated base
shows a climbable ladder down its exposed piers (rungs below grade are LOD0-only, like every rung).
60A and 40O never float and keep rails only, LADDER_FAM sink below grade, raised to tram 2.2 / gondola
3.0 so the face-normal slope drop is covered. C7 reach: the foot reaches variant float + the slope
drop at the piers' grade allowance in the WORST direction (allowance x foot L-inf radius / (B/2): the
face normal for a foot outside the leg square), capped at the pier bound (frozen bounds y floor).

REVISION 1d - rungs 60A / 40O (build/revision1c-check-tram-ladder.md R2 / E2, 2026-10-11): the two
variants that never float get rungs on their below-grade rails too, continuing the 0.30 pitch down from
the first rung at 0.25: 60A 7 rungs (lowest -1.85, 0.35 above the rail foot -2.2; +42 tris), 40O 8 rungs
(lowest -2.15, LADDER_FAM sink_rung_lo; +48 tris, 4,496 against the 4,500 cap - the full sink would be
10 rungs, 4,508). The rail feet (-2.2 / -3.0) and the C7 rule are unchanged; the C7 climbing corridor
spans the new rungs (rungs_below).

DETAIL PASS 1 - towers (design/detail-pass-1-towers.md sections 4-6 with the skeptic's M1-M11; build
report build/detail-pass-1-towers-lattice.md, 2026-10-11). Frozen: HEIGHTS / TRAM / GD, the manifest
prefab + carving blocks, bounds, top anchor (DT-C1, asserted in the test against revision 1d).
    geometry  hidden contact faces not built (flange bottom, trans_ ends, rocksub inboard faces on a
              1 cm deeper rockmain, gdstring tops, the tram deck lip on the chords); coplanar z-fights
              removed (travel-axis rings / K mids 1 cm narrower, plan diamonds mitred, gdstring end
              pieces 1 cm narrower, gdbrace / headdiag cut at mirror planes / chord faces, headx_ 0.20,
              haul ties off the bar axis, kick plates butt-jointed, basket posts turned 15 deg); rubber
              liners split off the sheaves / haul sheaves / saddle tops (the saddles in tower steel, Q8);
              tower plates on both travel faces (M10 planes); a fall-arrest rail per ladder flight,
              registered as a member axis (M5); tram leg splice collars (+0.60, S1); the 60A lens and
              ring dropped (M8 option 1: WarningLightRandom01 sub-object, manifest night_subobjects);
              smooth-shaded lattice members re-smoothed on every LOD (M6)
    UVs       plane-cut / mitred member ends keep their 3D aspect (per-corner v), metric post and flange
              strips, chord-corrected handrail tubes, developed V flanks (DT-C5, whole mesh + per class)
    texture   relief_specs (node plates, splice bands, edge rounds, chord splice plates, saddle slots
              + bolt rows, anchor nuts, hub caps, gratings; every floor >= 2 texels, M2), tone_specs
              (mottle / chalk, M7), the plate stamps per part (M3) + the data-plate decal, weathering
              (cs2_weather tower preset, free slots, WEATHER_I from the --weather-probe ladder)
    atlas     plates, hardware (rungs, sheaves, liners, posts, base plates) stacked on one island per
              translation-identical group (cs2_uv bay sharing): the lattice atlas is margin-bound

CLI (headless only):
    blender --background --factory-startup --python-exit-code 1 --python tram_tower.py -- \
        --family tram|gondola [--height H | --all] [--out DIR] [--letter L]
        [--finish] [--lods] [--export] [--review [DIR]] [--preview DIR | --no-preview]
        [--beacon] [--atlas N] [--ao-samples N] [--saddle-l M] [--no-lattice-ladder] [--no-validate]
        [--weather on|off] [--weather-probe]
    --preview with no DIR = the default preview folder (previews are on unless --no-preview).
    Without --export nothing is written (geometry, contracts, LOD deletion counts, tallies only).
    --export requires --finish (the textures come from the bake). Any failure exits non-zero.

Authoring frame: +X travel, +Y lateral (lanes), +Z up, origin = base center at ground. orient_for_net
maps it to CS2 (lateral = x, up = y, travel = z) before export.
"""

import argparse
import dataclasses
import json
import math
import os
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass

import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import cs2_asset     # noqa: E402
import cs2_kit       # noqa: E402
import cs2_uv        # noqa: E402
import cs2_compose   # noqa: E402
import cs2_validate  # noqa: E402
import cs2_sheaves   # noqa: E402  (T62: the one sheave wheel, cs2_sheaves.wheel)

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
CATALOG = os.path.join(REPO, "catalog", "carriers.json")
TEXTURES = os.path.join(REPO, "assets", "textures")
STANDIN_FG8N = os.path.join(REPO, "assets", "import", "towers", "N", "SkiLiftTowerFG8N")

FAMILIES = ("tram", "gondola")
HEIGHTS = {"tram": (30, 45, 60), "gondola": (25, 32, 40)}
NAME_PREFIX = {"tram": "SkiTramTower", "gondola": "SkiGondolaLattice"}
LETTER = {"tram": "A", "gondola": "O"}
DEFAULT_OUT = {"tram": os.path.join(REPO, "assets", "import", "tramtowers", "A"),
               "gondola": os.path.join(REPO, "assets", "import", "towers", "O")}
DEFAULT_PREVIEW = {"tram": os.path.join(REPO, "assets", "preview", "tramtowers"),
                   "gondola": os.path.join(REPO, "assets", "preview", "towers")}
ATLAS = {"tram": 2048, "gondola": 1024}          # RS section 4, R18
AO_SAMPLES = 48
PACK_MARGIN_PX = 6                                # station / tower precedent (lift_terminal.py:359)
SHADE_ANGLE = 66.0
WELD_M = 0.0005

# vanilla lane GUIDs for the stub block (PIPELINE section 6, "Known")
HV_LANE_GUID = "5d88ac45a40b30341a79535930e3b735"
LV_LANE_GUID = "37c8abdb015384f4e8b7ce9b5b2b6c9b"
# Stub lane prefabs (integration 2026-10-11): FindAnchors pairs an anchor with a lane only when the
# stub's m_LanePrefab IS the lane's prefab (PHASE3-LIFT-NETWORK 10.3), so the piece lanes, the station
# stubs and these tower stubs must all name the same R31 rope lane. Names = round-2-contract 10.1 and
# the tram stations' manifest `lanes`; the vanilla GUID is the joint fallback (all three or none).
STUB_LANES = {
    "tram_track": ("SkiTramTrackRope", "UnityGUID:" + HV_LANE_GUID),
    "tram_haul": ("SkiTramHaulRope", "UnityGUID:" + LV_LANE_GUID),
    "gd_haul": ("SkiGondolaHaulRope", "UnityGUID:" + HV_LANE_GUID),
    "gd_comm": ("UnityGUID:" + LV_LANE_GUID, "UnityGUID:" + LV_LANE_GUID),   # vanilla LV comm wire
}


def _stub(kind, label, pos, i):
    prefab, fallback = STUB_LANES[kind]
    return {"lane": label, "lane_prefab": prefab, "lane_fallback": fallback, "pos": pos,
            "node_index": [i, i]}


# --------------------------------------------------------------------------------------
# Rope / carrier contracts (frozen; cross-checked against catalog/carriers.json at run time)
# --------------------------------------------------------------------------------------

TRAM = {
    "lane_y": 5.50,            # HV track rope, one per side [R28; RS R4]
    "haul_y": 5.00,            # LV haul rope [R28; tram-stations haul_x_m]
    "haul_dz": -0.40,          # [R28; tram-stations haul_dy_m]
    "rope_r_hv": 0.100,        # vanilla HV cable mesh radius [V]
    "rope_r_lv": 0.060,        # vanilla LV cable mesh radius [V]
    "lane_hanging": 0.004,     # R31 per-type rope-lane prefab
    "h_top": 3.75,             # D2: bounds top - track rope
    "hang": 10.50,             # R27: floor underside -> carriage top
    "rope_above_floor": 10.00,  # R27: carriage top 0.50 above the rope
    "carriage_half_w": 1.10,   # R30
    "slope_max": 2.5,          # RS section 5 tram catalog slope (grade sweep range)
}
GD = {
    "lane_y": 3.00,            # RS R4 gondola gauge +-3.0
    "hang": 4.50,              # RS R5 / catalog gd8: grip top on the lane -> underside (ChairPin :1040)
    "h_top": 2.00,             # D2 provisional H_top_GD (axle 10.0 on the 12 m tubular, RS R6)
    "tube_top_dA": 0.50,       # provisional tube top above the axle (DQ precedent A + 0.438)
    "mast_r": 0.50, "mast_segs": 16,
    "comm_dA": 1.20,           # comm LV through the gantry window (DQ precedent axle + 1.20)
    "rope_r_hv": 0.100, "rope_r_lv": 0.060,
    "lane_hanging": 0.004,     # R31
    "slope_max": 1.2,          # RS section 5 gondola catalog slope
    "tubular_tops": (12.0, 16.0, 20.0),   # GD tubular family (towers-v2, not built yet): provisional
}

# carrier boxes in the LANE frame: x along travel about the carrier reference point (the rope point
# the mover pins), dy = |y| - lane (outboard +), dz = z - rope. `allowed` = tower prefixes whose contact
# with that box is an ACCEPTED cosmetic clip (recorded in the manifest), never a silent pass.
#
# Tram = the cabin lens' R27-R30 build (lift_cabin.py T / tram_dims, read 2026-10-11): R29 makes the
# carrier front/back AND left/right symmetric (identity hosting on both lanes), so the hanger is a
# CLOSED ring around the rope - two uprights at |dy| 0.82..1.08 from a crossbar (dz -4.10..-3.82) up to
# the carriage head (dz +0.38..+0.48). Any saddle support must cross that ring's sweep once per arm:
# the inboard upright passes through the two saddle arms (sbrkarm_ + their braces) for 0.70 m of
# travel each (0.07 s at 10 m/s). The haul grips (dy -0.50, R28) ride over the haul rollers like an
# FG grip over its sheaves. Both are the only accepted contacts.
# Integration 2026-10-11 (build/gondola-tram-integration.md section 1): the boxes now CONTAIN every
# vertex of the exported SkiTramCabinA (tests/test_gondola_tram_seams.py S5). The design-table boxes
# missed the roof equipment (y 3.00-3.50 above the floor = dz -7.00..-6.50, travel +-3.10, lateral
# +-1.40: up to 0.50 m outside), the column ladder (travel +-0.42) and the haul-clamp links
# (|dy| 0.425..0.575).
CARRIER_TRAM = (
    ("carriage",   (-2.80, 2.80), (-0.55, 0.55), (-0.05, 0.50), ()),    # carbeams, ties, bogies, wheels
    ("carhead",    (-0.20, 0.20), (-1.08, 1.08), (0.38, 0.48), ()),
    ("haulgrip_f", (2.55, 2.85), (-0.58, -0.42), (-0.48, 0.40), ("haulroll_",)),
    ("haulgrip_b", (-2.85, -2.55), (-0.58, -0.42), (-0.48, 0.40), ("haulroll_",)),
    ("haulgrip_fo", (2.55, 2.85), (0.42, 0.58), (-0.48, 0.40), ()),
    ("haulgrip_bo", (-2.85, -2.55), (0.42, 0.58), (-0.48, 0.40), ()),
    ("upright",    (-0.20, 0.20), (0.82, 1.08), (-3.82, 0.38), ()),
    ("upright_in", (-0.20, 0.20), (-1.08, -0.82), (-3.82, 0.38), ("sbrkarm_", "sbrk_")),
    ("crossbar",   (-0.50, 0.50), (-1.08, 1.08), (-4.10, -3.82), ()),
    ("column",     (-0.43, 0.43), (-0.25, 0.25), (-5.40, -4.10), ()),
    ("fin_yoke",   (-2.40, 2.40), (-0.30, 0.30), (-7.00, -5.40), ()),
    ("roof",       (-3.11, 3.11), (-1.41, 1.41), (-7.00, -6.49), ()),   # roof boxes + gratings
    ("body",       (-4.05, 4.05), (-1.90, 1.90), (-10.00, -7.00), ()),
)
R30_ZONES = (      # R30 literal (outboard) + the cabin lens' two-sided reading k1 / k2 (R29)
    ("R30a_out", (0.36, 6.0), (-0.80, 0.60), ()),
    ("R30b_out", (0.80, 6.0), (-3.80, -0.70), ()),
    ("R30a_in",  (-1.10, -0.36), (-0.80, 0.60), ("haulroll_",)),
    ("R30b_in",  (-1.10, -0.80), (-3.80, -0.70), ("sbrkarm_", "sbrk_")),
)
CARRIER_GD = (     # cabins.md 8.3 gondola; underside = A - 4.50 exactly (skeptic MC6); body extents
                   # from lift_cabin CARRIERS gd8 (lateral -1.05..+1.10, travel half 1.18)
    # Integration 2026-10-11: widened to CONTAIN every vertex of the exported SkiGondolaCabin8A in
    # both pin frames (rope point on the lane, and grip top on the lane = 0.08 lower, ChairPin today):
    # carriage frame travel +-0.55 (was +-0.45), hanger fillet dy 0.49 / dz -1.59, jaw / carriage
    # 0.08 lower (tests/test_gondola_tram_seams.py S6).
    ("jaw",      (-0.15, 0.15), (-0.04, 0.30), (-0.21, 0.08), ("sheave_",)),
    ("carriage", (-0.56, 0.56), (0.25, 0.75), (-0.59, -0.12), ()),
    ("stalk",    (-0.15, 0.15), (0.48, 0.81), (-1.74, -0.26), ()),
    ("arm_yoke", (-0.60, 0.60), (-0.70, 0.80), (-2.10, -1.58), ()),
    ("body",     (-1.18, 1.18), (-1.05, 1.10), (-4.50, -1.92), ()),
)
GD_HEAD_RULE = (   # cabins.md 8.3: below the rope only the sheaves may sit at dy > -0.06
    ("GD_head_rule", (-0.06, 6.0), (-1.62, 0.20), ("sheave_",)),
)
ROPE_SUPPORT = {"tram": ("saddle_", "sbrk", "haulroll_"),
                "gondola": ("sheave_", "rockmain_", "rocksub_")}
ZONE_SHRINK = 0.05          # C8: zones shrunk 0.05 m (touching allowed)
SWEEP_TOL = 0.005           # grade sweep: carrier boxes shrunk 5 mm
G_FLOOR = {"tram": 0.80, "gondola": 1.00}   # structure g_clear below this = regression (raise)
# boxes above the cabin pivot K (cabins.md 4.2 `--split`): they pitch with the rope once the carriage
# is its own mesh (cabins Q4), so their level-carrier threshold is reported, not floored
PITCHED_BOXES = {"tram": ("carriage", "carhead", "haulgrip_f", "haulgrip_b", "haulgrip_fo",
                          "haulgrip_bo")}

# --------------------------------------------------------------------------------------
# Tram tower geometry constants (lattice-towers section 4.1; D-tagged numbers are this module's)
# --------------------------------------------------------------------------------------

PED_H_TR, PW_TR, PIER_TR, BPLATE_TR = 1.5, 1.4, 12.0, 0.8
NECK_W_KINK, NECK_W_TOP = 3.4, 3.0
# head frame outer face 3.55 = the R27 cabin body's inner edge (5.50 - 1.85) - 0.10 (chord centers 3.40): MUST-CHANGE 2's
# "move the head frame inside the cabin's inner edge" (D6's 4.25 clipped the level body from g 0.76);
# as built the body is +-1.88 (CARRIER_TRAM body box +-1.90), so the margin is 0.07 m (0.05 to the box);
# the geometry review swept the real cabin through every tower LOD clear (gondola-tram-review-geometry);
# the saddles ride 2.10 m cantilever arms (sbrkarm_) from the frame ends out to y +-5.65
HEAD = {"x": 1.5, "y": 3.40, "top_dz": -1.35, "bot_dz": -2.45, "w": 0.30,
        "verts_y": (1.5, 2.45), "x_members_y": (1.5, 2.45, 3.40)}
# Revision 1 (V8b): one long curved box girder per lane, 0.70 deep at the crown (inside R30a's
# +-0.36 above dz -0.80), parabolic lead-out from |x| = xc to the tips (tip = extra drop at the ends)
SADDLE = {"w": 0.40, "d": 0.70, "xc": 1.0, "tip": 0.22}     # L is a parameter (default 4.8)
SADDLE_L_DEFAULT = 4.8          # 1.6 x the 3.0 m head width (P2P-33; JH-15 reads ~1.5 x)
# V8b haul-rope sheave row along each saddle's lower inboard edge (JH-15: >= 6 small sheaves under the
# girder, no straight roller beam): sheave tops on the haul-rope bottom line (C4); a carrier bar
# inboard of the row (bar_dy inside the haul lane) and ties to the girder web between sheaves
_HS_N, _HS_PITCH = 8, 0.55
ROLLER = {"x": tuple(round((k - (_HS_N - 1) / 2.0) * _HS_PITCH, 4) for k in range(_HS_N)),
          "r": 0.16, "w": 0.08, "segs": 8, "groove": 0.025,
          "bar_dy": 0.10, "bar_w": 0.08, "tie_x": (-1.1, 0.0, 1.1), "tie_w": 0.06}
DECK = {"w": 3.0, "l": 6.8, "rail_x": 1.40, "rail_y": 3.25, "h": 0.85, "knee": 0.45,
        "gate": 0.45}           # V7a: the -X rail opens over |y| < gate where the ladder arrives
W_TR = {"leg": 0.40, "strut": 0.22, "diag": 0.16, "neck": 0.14, "plan": 0.14, "joist": 0.12}
# V7b upper stabilizing frame (authoring frame, relative to z0 = track rope): inner posts on the leg
# tops (y +-1.5) and hanger posts on the crossarm ends (y +-4.25 = lane - 1.25: the post's outer face
# 4.37 stays inboard of the R30 / upright_in bands at lane - 1.10), top beams at z0 + 3.45..3.75 (= H)
# spanning y +-6.0 so each tip overhangs its rope lane, main diagonals inner-post top -> crossarm end,
# knees hanger post -> beam tip (kept >= dz 2.0 over the carriage band so the pitched-carriage
# threshold stays at the lugs' 0.60), a tie along x between each side's hanger posts
UPPER = {"beam_z": 3.60, "beam_w": 0.30, "beam_y": 6.0, "post_y": 1.5, "post_w": 0.30,
         "hang_y": 4.25, "hang_w": 0.24, "diag_w": 0.18, "diag_top": 3.30, "diag_bot": -1.05,
         "knee": ((4.25, 2.0), (5.2, 3.45)), "knee_w": 0.16, "tie_z": 1.20, "tie_w": 0.16,
         "x_beams_y": (0.0, 1.5), "lug_y": 5.5}
# V8c slewing pillar jib crane per side (P2P-15 evacuation crane; Rob Withey's comment: the top
# cranes swing the evacuation gear out over the track ropes onto the saddle): column on the head deck
# at x 0, jib at the top-beam level (top = H) reaching to y 6.0 over the lane, knee, trolley + hook
# block over the lane, covered winch on the column
CRANE = {"y": 2.95, "col_r": 0.14, "col_segs": 8, "jib_y0": 2.80, "jib_w": 0.30, "knee_z": 2.40,
         "knee_y": 4.40, "knee_w": 0.14, "hook_y": 5.5, "hook_bot": 2.58}
# V7a / V8a ladder system: flights follow the -X face outside the shaft (standoff s, rails at the
# line +- rail_y), alternate between two lateral lines so every flight is OFFSET from the one below,
# and each flight but the last ends at a SIDE BASKET cantilevered from a ring strut beside it (open
# toward the arriving flight, railed on the outer and far sides). Flight breaks are ring levels,
# chosen to keep flights <= flight_target (soft) / flight_max (hard).
LADDER = {"rail_y": 0.21, "rail_w": 0.05, "rung_r": 0.018, "pitch": 0.30, "first": 0.25,
          "grab": 1.10, "flight_target": 9.5, "flight_max": 10.5, "edge_gap": 0.33, "far_ext": 0.40,
          "corridor_half": 0.30, "corridor_out": 0.75, "corridor_in": 0.04, "min_offset": 0.70}
# Tram: the -X face (away from the rope lanes at +-y; the head deck rail opens at y 0 for it).
# Gondola: the -Y face - its travel bound is the sheave train (+-2.44 on the 25), which a -X basket
# would overshoot (LOD bounds drift, C9), while its lateral bound is the +-3.32 gantry beam; the
# whole lattice stands below the cabin underside (A - 4.5 = z_t + 1.0) and the last flight arrives
# beside the transition platform's open -Y edge (grating 2.0 deep in y). Standoff 0.25 (rails clear
# the 0.22 legs; the 0.48 first tried pushed the 32 / 40 baskets past +-3.32). Gondola soft flight
# target = the hard max: fewest baskets, for the 4,500-tri cap. Basket rings are filtered so no
# basket reaches past the variant's LOD-stable bound (ladder_bound).
# `sink` = how far the flight-0 rails continue below grade (plumb under the base ring) on a variant
# that never floats (60A, 40O; the floatable 30A / 45A / 25O / 32O run rails + rungs to the pier
# depth instead: ladder_depth, revision 1c). The ladder foot is nearer the axis than the leg corners
# RADIALLY (foot r 5.56 / 5.93 / 6.43 tram vs corner diagonals 7.07 / 7.78 / 8.49) but lies OUTSIDE
# the leg square along the face normal (by s + rail_w / 2 = 0.425 tram / 0.275 gondola), so at the
# piers' grade allowance the worst slope is the face normal: drop = allowance x foot n / (B/2) =
# 2.14 (60A) / 2.95 (40O). Revision 1b had 2.0 / 2.7 (corner diagonal only; revision1b-check-tram N2);
# 1c raises them to 2.2 / 3.0 (rails only, 0 tris). C7 asserts the drop.
# Revision 1d (revision1c-check-tram-ladder R2 / E2): the sink carries rungs too, on the same 0.30 pitch,
# the lowest >= 0.25 above the rail foot (60A: 7 rungs to -1.85, +42 tris) and, where set,
# >= `sink_rung_lo` (40O: 8 rungs to -2.15, +48 tris = 4,496 under the 4,500 cap; 10 rungs would be 4,508).
LADDER_FAM = {"tram": {"face": "-X", "s": 0.40, "lines": (0.0, 1.0), "bskt_in": 0.13, "bskt_depth": 0.85,
                       "sink": 2.2},
              "gondola": {"face": "-Y", "s": 0.25, "lines": (0.0, 0.75), "bskt_in": 0.08, "bskt_depth": 0.82,
                          "flight_target": 10.5, "sink": 3.0, "sink_rung_lo": -2.15}}
BASKET = {"rail_h": 1.0, "mid_h": 0.5, "rail_w": 0.05, "post_w": 0.05, "string_w": 0.08, "t": 0.03}
LADDER_ASYM = ("rung_", "lrail_", "bskt_", "grate_bskt", "arrest_")   # C11 exemption: offset flights (V8a; dp1 arrest rails)
# T62 round 2: LOD0 caps for the 16 haul rollers at the 16-segment capped wheel (+152 tris each): the smallest 100 above
# LOD0 / 0.95 (>= 5 % headroom): 30A 5,500 -> 7,900 (7,504), 45A 6,800 -> 9,100 (8,564), 60A 8,000 -> 9,800 (9,300);
# targets / LOD1 / LOD2 unchanged (the rollers leave at LOD1 with their bar: TRAM_LOD1 "haulroll_")
# T62 round 3: the 98-tri wheel (the inboard boss behind the roller bar is culled) -> caps reset to max(round 1, the
# smallest 100 with >= 5 % headroom): 30A 7,900 -> 5,800 (5,488), 45A 9,100 -> 6,900 (6,548), 60A 9,800 -> 8,000 (7,284:
# the round-1 cap)
TRAM_BUDGET = {30: (4000, 5800, 1900, 450), 45: (5200, 6900, 2400, 470), 60: (6500, 8000, 2800, 500)}
TRAM_LOD2_CAP = 500        # RS section 4 far cap: raise (skeptic MC5); the per-variant value warns

# --------------------------------------------------------------------------------------
# Gondola lattice constants (section 4.2 + the GD head of this module)
# --------------------------------------------------------------------------------------

PED_H_GD, PW_GD, PIER_GD, BPLATE_GD = 0.8, 0.9, 8.0, 0.5
GD_TOP_W = 1.8
W_GD = {"leg": 0.22, "strut": 0.12, "diag": 0.10, "plan": 0.10}
SHEAVES = {25: 8, 32: 10, 40: 12}           # task: 8-12 per side
# Revision 1: 12-gon flat-rim sheaves (44 tris) replace the 10-gon V-groove ones (56): rounder in
# silhouette and 12 x 2 x 12 = 288 tris cheaper on the 40, which pays for the ground ladder (V7a) under
# the 4,500 cap (the groove was invisible: the drawn rope runs on the axle line, FG/DQ convention)
SHEAVE = {"r": 0.25, "w": 0.10, "pitch": 0.62, "segs": 12, "groove": 0.0}
GD_BEAM_Y = 3.32            # gantry-beam half length = the lattice's LOD-stable lateral bound
GD_TRANS = {"gx": 2.6, "gy": 2.0, "rail_x": 1.3}   # transition platform grating (x by y), +-X rails
GD_BUDGET = (4000, 6100, 1500, 400)         # RS section 4 (target, cap, LOD1, LOD2 cap); T62: cap 4,500 -> 9,300 (r2) -> 6,100 (r3)
                                            # (round 3: Lattice40 5,790 with 24 98-tri wheels / 0.95; the wheels leave at LOD1)
GD_DECK = {"y0": 0.60, "y1": 1.75, "top_dA": -0.30, "rail_h": 0.85, "rail_knee": 0.45}

# --------------------------------------------------------------------------------------
# Detail pass 1 (design/detail-pass-1-towers.md sections 4-6 + the skeptic's MUST-CHANGE M1-M11;
# build report build/detail-pass-1-towers-lattice.md). Geometry inside the measured headroom plus
# the hidden-contact reclaim; texture through the shared cs2_compose additions; weathering wired.
# Nothing here moves a frozen number (TRAM / GD / HEIGHTS, the prefab / carving blocks, bounds).
# --------------------------------------------------------------------------------------

DP1 = {
    "inset": 0.010,          # DT10: same-facing coplanar members get w - 0.010 (5 mm per face)
    "headx_w": 0.20,         # tram headx_ 0.22 -> 0.20 (headvert_ | headx_ 3,872 cm2 on 60A)
    "tie_short": 0.005,      # tram haulroll_tie 5 mm shorter at the bar (bar | tie 2 x 24 cm2)
    "kick_trim": 0.010,      # deck kick plates along travel end 1 cm short of the corners (butt joint)
    "rockmain_drop": 0.010,  # gondola rockmain 1 cm deeper so the rocksub inner faces are covered
}
# Smooth-shaded lattice members (DT3, Q5 round-tube read): longitudinal edges smooth on every LOD
# (M6: cleared after the angle shading and again on each LOD make_lod_chain returns)
SMOOTH_MEMBERS = {"tram": ("braceK_", "braceKm_", "braceXa_", "braceXb_", "braceN_", "ringL", "plan_"),
                  "gondola": ("leg_", "ringL", "braceXa_", "braceXb_", "plan_", "gdbrace_", "gdstring_")}
# Tower plate (DT7, Q2: resort glyph + the height numeral), M10 measured host planes: gondola
# gdbeam_0 faces x +-0.120 (0.24 member), tram top beams' outer faces x +-1.650 (0.30 at +-1.50)
PLATE = {"gondola": {"w": 0.44, "h": 0.20, "dA": 1.88, "face_x": 0.120, "target_ppm": 340.0},
         "tram": {"w": 0.56, "h": 0.26, "dH": -0.15, "face_x": 1.650, "target_ppm": 320.0},
         "proud": 0.006, "rim_in": 0.002, "border_m": 0.006, "pad_m": 0.008, "field": 0.85}
# The plate's density weight = target px/m / the variant's base px/m (weight-1.0 classes, leg_
# median, as packed by this pass: measured, build report): one weight for all three would
# over-spend the atlas on 25O (the lattice is margin-bound; every texel the plate takes is lost to
# the members). DT-C7 at 0.20 m tall needs ~330 px/m for the glyph >= 56 px / numeral cap >= 48 px.
PLATE_BASE_PPM = {("gondola", 25): 25.1, ("gondola", 32): 23.1, ("gondola", 40): 18.9,
                  ("tram", 30): 36.1, ("tram", 45): 32.5, ("tram", 60): 29.0}
# Fall-arrest rail (instead of cage rings; section 3.1): 3-face channel, back 2 mm into the rung
# fronts. The rung's outward extent: tram / mast ladders (rungs along y, phase pi/2) show a flat
# at -r/2, the gondola -Y face (rungs along x) a corner at -r: front -0.027 / -0.036 (<= 0.036)
ARREST = {"w": 0.040, "d": 0.020, "into": 0.002, "end_below_top": 0.05}
# Tram leg splice collars (section 5.1, skeptic S1: +0.60 above the ring axis on every ring)
COLLAR = {"w": 0.48, "len": 0.14, "dz": 0.60}
# Night (DT9 + M8 option 1): one fixture on 60A only, the vanilla WarningLightRandom01 sub-object;
# the unlit lens + ring are dropped on all three (64 tris each); --beacon keeps the lens fallback
NIGHT = {60: {"name": "WarningLightRandom01", "guid": "6b6861547d4e1964aa977434af734126",
              "host_part": "gantryx_1", "rotation": [0.0, 0.0, 0.0, 1.0], "parent_mesh": 0,
              "random_pole_m": [0.70, 1.95]}}
# Weathering (DT8, DECISIONS 171): family intensity = the highest step of the 0.50 -> 0.05 ladder
# (0.05 steps) where EVERY member's visible-texel budget is WARN-free (--weather-probe, one bake per
# variant; build report). VL2 (share of texels changed > 15 %) binds: on 0.10-0.40 m members the
# 3-texel edge band covers most of the member, so edge lift x intensity must stay under ~0.15.
WEATHER_I = {"tram": 0.25, "gondola": 0.15}   # the --weather-probe ladder (build report): VL2 binds
WEATHER_STEPS = (0.50, 0.45, 0.40, 0.35, 0.30, 0.25, 0.20, 0.15, 0.10, 0.05)

# --------------------------------------------------------------------------------------
# Materials, density, relief, LOD fates
# --------------------------------------------------------------------------------------

SLOT1_TINT = {"tram": (0.66, 0.67, 0.69),                # Q6 default: galvanized gray
              "gondola": tuple(cs2_asset.STEEL_DEFAULT_RGB)}   # GD family default (dark steel)

# (prefix, material, density_w). Longest prefix wins (cs2_uv.set_density convention).
PREFIX_TABLE = (
    ("leg_", "paint", 1.0), ("ringL2_", "paint", 1.0), ("ringL1_", "paint", 1.0),
    ("ringL0_", "paint", 1.0), ("braceK_", "paint", 1.0), ("braceKm_", "paint", 1.0),
    ("braceXa_", "paint", 1.0), ("braceXb_", "paint", 1.0), ("braceN_", "paint", 1.0),
    ("plan_", "paint", 0.75),
    ("rung_", "galv", 0.5), ("lrail_", "galv", 0.5),
    ("headchord_", "paint", 1.5), ("headend_", "paint", 1.5), ("headvert_", "paint", 1.25),
    ("headx_", "paint", 1.5), ("headdiag_", "paint", 1.25),        # dp1 M2: headx_ 1.25 -> 1.5 (r 0.026 bolts)
    ("saddle_", "paint", 2.0), ("sbrkarm_", "paint", 1.25), ("sbrk_", "paint", 1.0),   # dp1 Q8: tower steel
    ("sbrkhang_", "paint", 1.25), ("sbrkdiag_", "paint", 1.0), ("sbrkknee_", "paint", 1.0),
    ("haulroll_", "galv", 1.0), ("haulroll_bar", "paint", 1.0), ("haulroll_tie", "paint", 1.0),
    ("grate_deck", "galv", 1.25), ("post_deck", "galv", 0.75), ("rail_deck", "galv", 0.75),
    ("kick_deck", "galv", 0.75),
    ("gantrypost_", "paint", 1.25), ("gantrybeam_", "paint", 1.5), ("gantryx_", "paint", 1.0),  # dp1 1.25 -> 1.5
    ("lug_", "paint", 0.75), ("beacon_lens", "lens", 1.0), ("beacon_ring", "galv", 1.0),
    ("cranecol_", "crane", 1.0), ("cranejib_", "crane", 1.25), ("craneknee_", "crane", 1.0),
    ("cranetrol_", "dark", 1.0), ("cranehook_", "dark", 0.75), ("cranehook_b", "crane", 0.75),
    ("cranewinch_", "galv", 1.0),
    # ladder system (both families)
    ("grate_bskt", "galv", 0.75), ("bskt_", "galv", 0.5),
    ("ped_", "concrete", 1.0), ("pier_", "concrete", 0.25), ("bplate_", "galv", 2.0),   # dp1 bplate 1 -> 2
    ("gusset_", "paint", 1.0),
    # gondola lattice + GD head
    ("trans_", "paint", 1.25), ("flange_trans", "paint", 1.25), ("grate_trans", "galv", 1.0),
    ("post_trans", "galv", 0.75), ("rail_trans", "galv", 0.75), ("kick_trans", "galv", 0.75),
    ("mast_", "paint", 1.25), ("gdgirder_", "paint", 1.25), ("gdcap_", "paint", 1.0),
    ("gdpost_", "paint", 1.25), ("gdbeam_", "paint", 1.25), ("gdbrace_", "paint", 1.0),
    ("gdstring_", "paint", 0.75), ("grate_gdeck", "galv", 1.0), ("post_gd", "galv", 0.75),
    ("rail_gd", "galv", 0.75), ("kick_gd", "galv", 0.75), ("sheave_", "galv", 1.0),
    ("rockmain_", "paint", 1.0), ("rocksub_", "paint", 1.0),
    # detail pass 1 parts (plate_ / platerim_ density is family-specific: PLATE[...]["density"])
    ("plate_", "plate", 1.0), ("platerim_", "plate", 1.0), ("arrest_", "galv", 0.1),
    ("collar_", "paint", 1.5), ("collarcap_", "paint", 0.3), ("sheave_liner", "rubber", 1.0), ("haulroll_liner", "rubber", 1.0),
    ("saddle_liner", "rubber", 2.0),
)

TRAM_LOD1 = ("rung_", "lrail_", "grate_bskt", "bskt_", "plan_", "post_deck", "rail_deck",
             "kick_deck", "haulroll_", "lug_", "gusset_", "bplate_", "beacon", "cranetrol_",
             "cranehook_", "cranewinch_",
             "collar_", "collarcap_", "plate_", "platerim_", "arrest_")   # dp1: LOD0-only (haulroll_liner follows haulroll_)
TRAM_LOD2 = TRAM_LOD1 + ("braceKm_", "braceK_", "braceXa_", "braceXb_", "braceN_", "ringL1_",
                         "headvert_", "headx_", "headend_", "headdiag_", "sbrk_", "sbrkhang_",
                         "sbrkdiag_", "sbrkknee_", "grate_deck", "gantryx_", "cranecol_",
                         "cranejib_", "craneknee_")
GDL_LOD1 = ("plan_", "post_trans", "rail_trans", "kick_trans", "gusset_", "bplate_", "flange_trans",
            "grate_trans", "braceXb_", "ringL0_", "rung_", "lrail_", "grate_bskt", "bskt_",
            "sheave_", "post_gd", "rail_gd", "kick_gd", "lug_",
            "plate_", "platerim_", "arrest_")   # dp1: LOD0-only (sheave_liner follows sheave_)
GDL_LOD2 = GDL_LOD1 + ("braceXa_", "ringL1_", "trans_1", "trans_2", "trans_3", "trans_4",
                       "trans_5", "grate_gdeck", "gdstring_", "gdbrace_", "rocksub_", "gdcap_")


def detail_specs():
    """Relief per prefix (section 6 DetailSpec column; empty DetailSpec excludes a longer name)."""
    D = cs2_compose.DetailSpec
    rings_bolts = dict(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.018, seam_axes="rings")
    return {
        "leg_": D(seams_m=6.0, seam_axes="rings", bolt_pitch_m=0.12, bolt_r_m=0.018),
        "headchord_": D(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.018),
        "gantrybeam_": D(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.018),
        "saddle_": D(seams_m=0.6, seam_axes="ribs", seam_depth_m=0.003),
        "grate_": D(grate_m=0.03), "grate_deck_lip": D(), "grate_bskt": D(grate_m=0.03),
        "grate_trans_lip": D(), "grate_gdeck": D(grate_m=0.03),
        "trans_": D(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.018),
        "ped_": D(seams_m=1.2, seam_axes="rings", seam_depth_m=0.003),
        "pier_": D(seams_m=1.2, seam_axes="rings", seam_depth_m=0.003),
        "bplate_": D(bolt_pitch_m=0.15, bolt_r_m=0.02),
        "mast_": D(**rings_bolts), "gdgirder_": D(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.018),
    }


def material_specs(beacon, texture_root, family="gondola"):
    """cs2_bake MaterialSpec table (section 8.1; concrete / galv sets prefiltered, see prefilter_sets).
    Detail pass 1: `rubber` (sheave / saddle liners, Q8) and `plate` (the tower plate's aluminum:
    its front is painted over by the stamp); the tram's slot-1 `paint` is GALVANISED (Q4: metal
    0.10 -> 0.60, rough 0.55 -> 0.50; still slot-1 tintable), the gondola's stays painted steel
    (skeptic S3: family-conditional, material_specs serves both lattice families)."""
    import cs2_bake
    M = cs2_bake.MaterialSpec
    galvanised = family == "tram"
    return {
        "rubber": M(albedo=(0.035, 0.035, 0.037), metal=0.0, rough=0.82, projection="box"),
        "plate": M(albedo=(0.72, 0.73, 0.74), metal=0.35, rough=0.45, projection="box"),
        "concrete": M(texture_set=os.path.join(texture_root, "Concrete034"), metres_per_tile=3.0,
                      tint=(0.661, 0.625, 0.547), rough=0.76, projection="box"),
        "galv": M(texture_set=os.path.join(texture_root, "Metal032"), metres_per_tile=2.0,
                  projection="box"),
        "paint": M(albedo=(0.604, 0.604, 0.604), slot=1, recolor_albedo=True,
                   metal=0.60 if galvanised else 0.1, rough=0.50 if galvanised else 0.55,
                   projection="box"),
        "dark": M(albedo=(0.045, 0.047, 0.050), metal=0.3, rough=0.6, projection="box"),
        "lens": M(albedo=(0.60, 0.03, 0.02), rough=0.3, metal=0.0, projection="box",
                  emissive=(1.0, 0.08, 0.04) if beacon else None),
        # Revision 1 (V8c): safety-yellow evacuation crane (fixed color, not a recolor slot) so the
        # column / knee / jib read as equipment against the slot-1 gray frame
        "crane": M(albedo=(0.78, 0.56, 0.05), metal=0.15, rough=0.5, projection="box"),
        **cs2_sheaves.wheel_materials(M),         # T62 round 2: the one wheel material under v2 ({} under v1)
    }


class ContractError(RuntimeError):
    """A geometry / budget contract failed: the build must not ship."""


# --------------------------------------------------------------------------------------
# Small helpers
# --------------------------------------------------------------------------------------

def lerp(a, b, t):
    return a + (b - a) * t


def geo_panels(z_bot, z_top, w_bot, w_top, alpha):
    """lattice-towers section 5.2 panel fit (identical to design/lattice-towers-calc.py)."""
    H = z_top - z_bot
    s = (w_bot - w_top) / 2.0 / H
    n = max(1, round(math.log(w_bot / w_top) / math.log(1 + 2 * s * alpha)))
    r = (w_bot / w_top) ** (1.0 / n)
    a = (r - 1) / (2 * s)
    lv, w, z = [z_top], w_top, z_top
    for _ in range(n):
        z -= a * w
        w *= r
        lv.append(z)
    lv[-1] = z_bot
    lv = lv[::-1]
    return list(zip(lv, lv[1:])), s, a


def pick_variant(e, sizes, spec_y):
    """D13 score (SubObjectSystem.cs:2189-2199): max of 1/(1 + (spare >= 0 ? 2 spare : -spare))."""
    need = e + spec_y
    best = None
    for s in sizes:
        sp = s - need
        sc = 1.0 / (1.0 + (2.0 * sp if sp >= 0 else -sp))
        if best is None or sc > best[0] + 1e-12:
            best = (sc, s)
    return best[1]


def scoring_sweep(sizes, spec_y, e_max=30.0, step=0.01):
    """Variant switch points, max float and max burial for node elevation 0..e_max."""
    prev, switches = None, []
    worst_float = worst_bury = 0.0
    n = int(round(e_max / step))
    for i in range(n + 1):
        e = i * step
        s = pick_variant(e, sizes, spec_y)
        fl = (e + spec_y) - s
        worst_float = max(worst_float, fl)
        worst_bury = max(worst_bury, -fl)
        if s != prev:
            switches.append((round(e, 2), s))
            prev = s
    return {"switches": switches, "max_float": round(worst_float, 3), "max_bury": round(worst_bury, 3)}


def variant_float(spec, step=0.01):
    """Revision 1c (revision1b-check-tram N1): the most THIS variant's base floats above the terrain
    when the D13 score picks it, over the family's node elevation range (prefab_block
    elevation_range: tram 0..30, gondola 0..28). 30A / 45A 10.0, 25O 4.66, 32O 5.33 (0.01 sweep); 60A / 40O 0."""
    if spec.family == "tram":
        sizes, spec_y = [float(h) for h in HEIGHTS["tram"]], float(HEIGHTS["tram"][0])
        e_max = spec_y
    else:
        sizes, spec_y = list(GD["tubular_tops"]) + [float(h) for h in HEIGHTS["gondola"]], GD["tubular_tops"][0]
        e_max = float(HEIGHTS["gondola"][-1]) - spec_y
    worst = 0.0
    for i in range(int(round(e_max / step)) + 1):
        e = i * step
        if pick_variant(e, sizes, spec_y) == float(spec.H):
            worst = max(worst, e + spec_y - float(spec.H))
    return round(worst, 3)


def git_sha():
    try:
        out = subprocess.run(["git", "-C", REPO, "rev-parse", "--short=12", "HEAD"],
                             capture_output=True, text=True, timeout=10)
        return out.stdout.strip() or None
    except (OSError, subprocess.SubprocessError):
        return None


def load_catalog(path=CATALOG):
    """Carrier rows (MUST-CHANGE 1). Returns (rows, drift list). Missing file -> ({}, [note])."""
    drift = []
    try:
        with open(path, "r", encoding="utf-8") as fh:
            rows = json.load(fh).get("carriers", {})
    except (OSError, ValueError) as e:
        return {}, [f"catalog unreadable ({e!r}); rulings used"]
    tram, gd = rows.get("tram", {}), rows.get("gd8", {})
    if tram:
        if abs(float(tram.get("lane_x_m", TRAM["lane_y"])) - TRAM["lane_y"]) > 1e-6:
            raise ContractError(f"catalog tram lane_x_m {tram.get('lane_x_m')} != R28 {TRAM['lane_y']}")
        if abs(float(tram.get("hang_m", TRAM["hang"])) - TRAM["hang"]) > 1e-6:
            drift.append(f"catalog tram hang_m {tram.get('hang_m')} is stale: R27 rules "
                         f"{TRAM['hang']} (row is humans-only; update it)")
    if gd:
        if abs(float(gd.get("lane_x_m", GD["lane_y"])) - GD["lane_y"]) > 1e-6:
            raise ContractError(f"catalog gd8 lane_x_m {gd.get('lane_x_m')} != R4 {GD['lane_y']}")
        if abs(float(gd.get("hang_m", GD["hang"])) - GD["hang"]) > 1e-6:
            raise ContractError(f"catalog gd8 hang_m {gd.get('hang_m')} != R5 {GD['hang']}")
    for d in drift:
        print(f"[tram_tower] WARN catalog: {d}")
    return rows, drift


# --------------------------------------------------------------------------------------
# Mesh builders (members, sheaves, saddles) - every part one object with one UV layer "UVMap"
# --------------------------------------------------------------------------------------

XV, YV, ZV = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def _link(name, bm, relief="geo"):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    if obj.name != name:
        raise ContractError(f"object name collision: wanted {name!r}, got {obj.name!r}")
    obj["relief"] = relief
    return obj


def _frame(t, hint):
    a = hint - t * hint.dot(t)
    if a.length < 1e-6:
        alt = XV if abs(t.x) < 0.9 else YV
        a = alt - t * alt.dot(t)
    a.normalize()
    return a, t.cross(a)


def _plane_ring(P, offs, t, n):
    if n is None:
        return [P + o for o in offs]
    n = Vector(n)
    return [P + o + t * (-(o.dot(n)) / t.dot(n)) for o in offs]


def _sweep(name, rings, u_edges, perim, caps, uv_breaks, cap_frames, relief="geo", vcorr=None,
           drop=()):
    """Faces between consecutive rings (all rings share the corner count), strip UVs, caps.
    Detail pass 1: `vcorr` = (start, end) lists [ring][corner] of meters added to the strip v of
    that corner where the ring starts / ends a strip (a plane-cut end ring's or a mitred island
    break's corners sit off the ring centroid along the axis: without it those faces shear,
    gdbrace 4.35x / sbrk p95 4.9x texel anisotropy, design 1.2); `drop` = world directions whose
    side / cap faces are not built (normal . d > 0.9: hidden contact faces, design 1.4)."""
    segs = len(rings[0])
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    V = [[bm.verts.new(c) for c in ring] for ring in rings]
    cent = [sum(r, Vector()) / len(r) for r in rings]
    arc = [0.0]
    for a, b in zip(cent, cent[1:]):
        arc.append(arc[-1] + (b - a).length)
    breaks = sorted(set(uv_breaks))
    for j in range(len(rings) - 1):
        isl = sum(1 for b in breaks if b <= j)
        for k in range(segs):
            k1 = (k + 1) % segs
            ca = vcorr[0][j] if vcorr else (0.0,) * segs      # ring j as a strip START
            cb = vcorr[1][j + 1] if vcorr else (0.0,) * segs  # ring j + 1 as a strip END
            f = bm.faces.new((V[j][k], V[j][k1], V[j + 1][k1], V[j + 1][k]))
            u0, u1 = u_edges[k] + 2.0 * isl, u_edges[k + 1] + 2.0 * isl
            q4 = ((u0, (arc[j] + ca[k]) / perim), (u1, (arc[j] + ca[k1]) / perim),
                  (u1, (arc[j + 1] + cb[k1]) / perim), (u0, (arc[j + 1] + cb[k]) / perim))
            for lp, q in zip(f.loops, q4):
                lp[uv].uv = q
    n_isl = len(breaks) + 1
    for which, (ring_i, start) in (("start", (0, True)), ("end", (-1, False))):
        if caps not in ("both", which):
            continue
        ring = V[ring_i]
        verts = list(reversed(ring)) if start else list(ring)
        f = bm.faces.new(verts)
        a, b = cap_frames[0] if start else cap_frames[1]
        c = cent[ring_i]
        off = 2.0 * (n_isl + (0 if start else 1))
        for lp in f.loops:
            d = lp.vert.co - c
            u, v = d.dot(a) / perim, d.dot(b) / perim
            lp[uv].uv = (u + off, (-v if start else v))
    bm.normal_update()
    if drop:
        dead = [f for f in bm.faces if any(f.normal.dot(Vector(d)) > 0.9 for d in drop)]
        if dead:
            bmesh.ops.delete(bm, geom=dead, context="FACES")
        bm.normal_update()
    return _link(name, bm, relief)


def prism(name, pts, radius, segs, hint, phase=0.0, caps="none", end_planes=(None, None),
          uv_breaks=(), drop=()):
    """
    Polygonal tube along the polyline `pts`: corners at radius `radius`, angles phase + 2 pi k / segs
    in the (a, b) frame where a = hint projected off the tangent. Interior points get MITRED rings
    (bisector plane, parallel-transported frame: the joint closes exactly, no V-notch - MC8).
    `end_planes` (normal or None) cut the first / last ring by a plane through the end point
    (legs: horizontal foot and head cuts). UV: strip u = k / segs, v = arc / perimeter (3D aspect kept);
    `uv_breaks` = interior point indices where a new UV island starts. caps none/start/end/both.
    """
    P = [Vector(p) for p in pts]
    if len(P) < 2:
        raise ValueError(f"{name}: prism needs >= 2 points")
    T = []
    for a, b in zip(P, P[1:]):
        d = b - a
        if d.length < 1e-9:
            raise ValueError(f"{name}: coincident points")
        T.append(d.normalized())
    a, b = _frame(T[0], Vector(hint))
    ang = [phase + 2.0 * math.pi * k / segs for k in range(segs)]

    def offs(a, b):
        return [(a * math.cos(q) + b * math.sin(q)) * radius for q in ang]

    cap0 = (a.copy(), b.copy())
    rings = [_plane_ring(P[0], offs(a, b), T[0], end_planes[0])]
    for i in range(1, len(P) - 1):
        tin, tout = T[i - 1], T[i]
        m = (tin + tout).normalized()
        o = offs(a, b)
        rings.append([P[i] + ok + tin * (-(ok.dot(m)) / tin.dot(m)) for ok in o])
        R = tin.rotation_difference(tout)
        a, b = R @ a, R @ b
    rings.append(_plane_ring(P[-1], offs(a, b), T[-1], end_planes[1]))
    side = 2.0 * radius * math.sin(math.pi / segs)
    perim = side * segs
    u_edges = [k / segs for k in range(segs + 1)]
    # detail pass 1: a plane-cut end ring's corner k sits delta_k along the axis from the end point
    # (_plane_ring), a mitred ring's corner along the incoming / outgoing axis; the strip v takes
    # that offset (ends always, mitres where a UV island starts: uv_breaks) so faces keep their aspect
    vs = [[0.0] * segs for _ in rings]
    ve = [[0.0] * segs for _ in rings]
    if end_planes[0] is not None:
        vs[0] = [(c - P[0]).dot(T[0]) for c in rings[0]]
    if end_planes[1] is not None:
        ve[-1] = [(c - P[-1]).dot(T[-1]) for c in rings[-1]]
    for j in set(uv_breaks):
        if 0 < j < len(P) - 1:
            ve[j] = [(c - P[j]).dot(T[j - 1]) for c in rings[j]]
            vs[j] = [(c - P[j]).dot(T[j]) for c in rings[j]]
    return _sweep(name, rings, u_edges, perim, caps, uv_breaks, (cap0, (a, b)), vcorr=(vs, ve), drop=drop)


def member(name, pts, w, hint, caps="none", end_planes=(None, None), uv_breaks=(), drop=()):
    """Square member of side w, faces parallel / perpendicular to `hint` (roll pi/4 of D11)."""
    return prism(name, pts, w / math.sqrt(2.0), 4, hint, math.pi / 4, caps, end_planes, uv_breaks,
                 drop)


def saddle_part(name, y_c, prof, width, depth, liner=None):
    """Rope saddle: vertical rectangular sections (top z, bottom z - depth) along x, end caps.
    Detail pass 1 (Q8): with `liner` the top face (the rope seat) is not built here; it is its own
    part `liner` (rubber), same vertices (welded at the join), so C3 still reads the saddle's top."""
    rings = []
    for x, z in prof:
        rings.append([Vector((x, y_c - width / 2, z)), Vector((x, y_c - width / 2, z - depth)),
                      Vector((x, y_c + width / 2, z - depth)), Vector((x, y_c + width / 2, z))])
    perim = 2.0 * (width + depth)
    acc = [0.0, depth, depth + width, 2 * depth + width, perim]
    u_edges = [v / perim for v in acc]
    body = _sweep(name, rings, u_edges, perim, "both", (), ((-ZV, YV), (ZV, YV)))
    if liner is None:
        return body
    # drop the top strip (corner 3 -> 0 of every section: the faces between the +y top edge and
    # the -y top edge) from the body and build it as the liner strip with the same strip UVs
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.normal_update()
    top = [f for f in bm.faces if f.normal.z > 0.5 and abs(f.normal.y) < 0.5
           and len(f.verts) == 4 and all(abs(abs(v.co.y - y_c) - width / 2) < 1e-6 for v in f.verts)]
    if len(top) != len(prof) - 1:
        bm.free()
        raise ContractError(f"{name}: liner split found {len(top)} top faces, want {len(prof) - 1}")
    lb = bmesh.new()
    luv = lb.loops.layers.uv.new("UVMap")
    suv = bm.loops.layers.uv["UVMap"]
    vmap = {}
    for f in top:
        vs = []
        for lp in f.loops:
            key = tuple(round(c, 7) for c in lp.vert.co)
            if key not in vmap:
                vmap[key] = lb.verts.new(lp.vert.co)
            vs.append(vmap[key])
        nf = lb.faces.new(vs)
        for lp_new, lp_old in zip(nf.loops, f.loops):
            lp_new[luv].uv = lp_old[suv].uv
    bmesh.ops.delete(bm, geom=top, context="FACES")
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()
    lb.normal_update()
    return body, _link(liner, lb)


def sheave_lite(name, r, w, centre, segs, groove=None, liner=None):
    """
    Light line sheave (TV2 D4 'lite' equivalent): V-groove rim (2 bands) + two n-gon side discs,
    axle along world Y. 4 segs + 2 (segs - 2) tris (segs 12 -> 68, 10 -> 56). groove=0: flat rim
    (one band), 2 segs + 2 (segs - 2) tris (segs 12 -> 44): Revision 1 gondola trains.
    Detail pass 1: with `liner` the rim band(s) (the rope faces) are a second part `liner`
    (rubber); the sheave part keeps the two discs (C3 / C4 read the disc rims). Returns
    (sheave, liner) then.
    """
    gd = max(0.02, 0.18 * r) if groove is None else groove
    c = Vector(centre)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")

    def ring(y, rad):
        out = []
        for k in range(segs):
            q = 2.0 * math.pi * k / segs
            out.append(bm.verts.new(c + Vector((rad * math.cos(q), y, rad * math.sin(q)))))
        return out
    circ = 2.0 * math.pi * r
    if gd <= 0.0:
        L, R = ring(-w / 2, r), ring(w / 2, r)
        bands = ((L, R, 0.0, w),)
    else:
        L, G, R = ring(-w / 2, r), ring(0.0, r - gd), ring(w / 2, r)
        side = math.hypot(w / 2, gd)
        bands = ((L, G, 0.0, side), (G, R, side, side))
    for band, (A, B, v0, side) in enumerate(bands):
        rA, rB = (A[0].co - c).xz.length, (B[0].co - c).xz.length
        cone = liner is not None and abs(rA - rB) > 1e-9
        if cone:
            # detail pass 1 (DT-C5): a V flank is a polygonal cone frustum - developed isometrically
            # (corners at apex distance D, D rB / rA; alpha = the apex angle per face), not as the
            # strip u = k / segs that stretched the groove side by rA / rB (anisotropy 1.23)
            D = side * rA / abs(rA - rB)                       # apex -> ring A corners
            alpha = 2.0 * math.asin(rA * math.sin(math.pi / segs) / D)
            if rA < rB:
                alpha = -alpha                                  # keep the strip's handedness
        for k in range(segs):
            k1 = (k + 1) % segs
            f = bm.faces.new((A[k], B[k], B[k1], A[k1]))
            if cone:
                def pol(dist, j):
                    return (dist * math.cos(j * alpha) / circ, dist * math.sin(j * alpha) / circ + 2.0 * band)
                q4 = (pol(D, k), pol(D * rB / rA, k), pol(D * rB / rA, k + 1), pol(D, k + 1))
                for lp, q in zip(f.loops, q4):
                    lp[uv].uv = q
                continue
            u0, u1 = k / segs, (k + 1) / segs
            for lp, q in zip(f.loops, ((u0, v0), (u0, v0 + side), (u1, v0 + side), (u1, v0))):
                lp[uv].uv = (q[0], q[1] / circ)
    for disc, verts, (ax_u, ax_v), off in ((0, L, (XV, ZV), 2.0), (1, list(reversed(R)), (ZV, XV), 4.0)):
        f = bm.faces.new(verts if disc == 0 else verts)
        for lp in f.loops:
            d = lp.vert.co - c
            lp[uv].uv = (d.dot(ax_u) / circ + off, d.dot(ax_v) / circ)
    bm.normal_update()
    # left disc (y = -w/2) must face -Y, right disc +Y, rim outward: fix any inverted face
    for f in bm.faces:
        cen = f.calc_center_median() - c
        if abs(f.normal.y) > 0.9:
            want = Vector((0, 1 if cen.y > 0 else -1, 0))
        else:
            want = Vector((cen.x, 0, cen.z))
        if f.normal.dot(want) < 0:
            f.normal_flip()
    bm.normal_update()
    if liner is None:
        return _link(name, bm)
    lb = bm.copy()
    discs = [f for f in lb.faces if abs(f.normal.y) > 0.9]
    bmesh.ops.delete(lb, geom=discs, context="FACES")
    bands = [f for f in bm.faces if abs(f.normal.y) <= 0.9]
    bmesh.ops.delete(bm, geom=bands, context="FACES")
    lb.normal_update()
    bm.normal_update()
    return _link(name, bm), _link(liner, lb)


def _haul_wheel(name, liner, centre):
    """T62: a tram haul roller. v2 (cs2_sheaves.WHEELS, default) = cs2_sheaves.wheel at the ROLLER radius / width (round
    3: 16 segments, V groove, the hub boss on its seen face), the developed UV map (DT-C5); v1 = sheave_lite at
    ROLLER segs 8 / groove 0.025 (the pre-T62 roller, byte for byte). Returns (discs, liner)."""
    R = ROLLER
    if cs2_sheaves.wheels_v2():
        W = cs2_sheaves.wheel_spec("TRAM")
        if abs(W["r"] - R["r"]) > 1e-9 or abs(W["w"] - R["w"]) > 1e-9:
            raise ContractError(f"T62: cs2_sheaves TRAM wheel {W} != ROLLER r / w {R['r']} / {R['w']}")
        (d, _a), (l, _b) = cs2_sheaves.wheel(name, liner, R["r"], R["w"], centre, segs=W["segs"], ngon=W["ngon"],
                                             uv="developed")
        return d, l
    return sheave_lite(name, R["r"], R["w"], centre, R["segs"], groove=R["groove"], liner=liner)


def _gd_wheel(name, liner, centre):
    """T62: a lattice GD-head sheave. v2 = cs2_sheaves.wheel at the GD family's r 0.25 / w 0.10 (round 3: 16 segments,
    V groove, seen hub bosses; GD_BUDGET cap 6,100), developed UVs; v1 = sheave_lite flat 12-gon (pre-T62). (discs, liner)."""
    if cs2_sheaves.wheels_v2():
        W = cs2_sheaves.wheel_spec("GD")
        if abs(W["r"] - SHEAVE["r"]) > 1e-9 or abs(W["w"] - SHEAVE["w"]) > 1e-9:
            raise ContractError(f"T62: cs2_sheaves GD wheel {W} != SHEAVE r / w {SHEAVE['r']} / {SHEAVE['w']}")
        (d, _a), (l, _b) = cs2_sheaves.wheel(name, liner, SHEAVE["r"], SHEAVE["w"], centre, segs=W["segs"],
                                             ngon=W["ngon"], uv="developed")
        return d, l
    return sheave_lite(name, SHEAVE["r"], SHEAVE["w"], centre, SHEAVE["segs"], groove=SHEAVE["groove"], liner=liner)


# --------------------------------------------------------------------------------------
# Part list with an axis registry (C7 / C8 member-axis tests)
# --------------------------------------------------------------------------------------

class Parts:
    def __init__(self):
        self.items = []              # [(obj, material key)]
        self.axes = {}               # name -> ([(x, y, z)...], w)
        self.exact_aabb = set()      # axis-aligned boxes / plates: AABB test is exact
        self.ladder = None           # ladder_plan() of the ground-to-head ladder system (C7)

    def add(self, obj, mat, axis=None, w=None, aabb=False):
        if any(o.name == obj.name for o, _ in self.items):
            raise ContractError(f"duplicate part name {obj.name}")
        self.items.append((obj, mat))
        if axis is not None:
            self.axes[obj.name] = ([tuple(p) for p in axis], float(w))
        if aabb:
            self.exact_aabb.add(obj.name)
        return obj

    def kit(self, kit_parts, mat, aabb=False):
        for obj, _q in kit_parts:
            self.add(obj, mat, aabb=aabb)

    def member(self, name, pts, w, mat="paint", hint=None, caps="none", end_planes=(None, None),
               uv_breaks=(), drop=()):
        P = [Vector(p) for p in pts]
        if hint is None:
            t = (P[-1] - P[0]).normalized()
            hint = XV if abs(t.z) > 0.7 else ZV
        return self.add(member(name, P, w, hint, caps, end_planes, uv_breaks, drop), mat, axis=P, w=w)

    def tri(self, name, pts, r, mat="galv", hint=ZV, caps="none", phase=math.pi / 2, uv_breaks=()):
        """Triangular prism (circumradius r, 6 tris per span): ladder rungs and basket rails/posts.
        Registered with the square-equivalent width r * sqrt(2) (half-diagonal = r) for C7 / C8."""
        P = [Vector(p) for p in pts]
        return self.add(prism(name, P, r, 3, hint, phase, caps, uv_breaks=uv_breaks), mat, axis=P,
                        w=r * math.sqrt(2.0))

    def box(self, name, size, loc, mat="paint", drop=(), open_bottom=False):
        return self.add(cs2_kit.add_box(name, size, loc, open_bottom=open_bottom, drop=drop), mat,
                        aabb=True)

    def mat_of(self, name):
        for o, m in self.items:
            if o.name == name:
                return m
        raise KeyError(name)


# --------------------------------------------------------------------------------------
# Variant specs
# --------------------------------------------------------------------------------------

@dataclass(frozen=True)
class TramSpec:
    H: int
    B: float
    n_neck: int
    saddle_l: float = SADDLE_L_DEFAULT
    family: str = "tram"
    beacon: bool = False                # detail pass 1 (M8): the lens is built only for --beacon

    @property
    def z0(self):                       # rope datum (track rope axis)
        return self.H - TRAM["h_top"]

    @property
    def hc(self):                       # head bottom-chord center
        return self.z0 + HEAD["bot_dz"]

    @property
    def z_k(self):                      # kink = neck foot (section 3.1)
        return self.z0 - 2.60 - self.n_neck * 3.15

    @property
    def s0(self):                       # lane end slope at the tower (R31)
        return 4.0 * TRAM["lane_hanging"]

    def w(self, z):
        if z <= self.z_k:
            return lerp(self.B, NECK_W_KINK, (z - PED_H_TR) / (self.z_k - PED_H_TR))
        return lerp(NECK_W_KINK, NECK_W_TOP, (z - self.z_k) / (self.hc - self.z_k))


@dataclass(frozen=True)
class GdSpec:
    H: int
    B: float
    n_sheaves: int
    lattice_ladder: bool = True         # V7a: ground-to-platform ladder on by default (D8 superseded)
    family: str = "gondola"

    @property
    def z0(self):                       # axle = lane = sheave-train datum
        return self.H - GD["h_top"]

    @property
    def z_t(self):                      # lattice top: 1.0 below the cabin underside (A - 4.50)
        return self.z0 - GD["hang"] - 1.0

    @property
    def s0(self):
        return 4.0 * GD["lane_hanging"]

    def w(self, z):
        return lerp(self.B, GD_TOP_W, (z - PED_H_GD) / (self.z_t - PED_H_GD))


def make_spec(family, H, args=None):
    if family == "tram":
        B, n = {30: (10.0, 2), 45: (11.0, 3), 60: (12.0, 3)}[H]
        return TramSpec(H, B, n, saddle_l=getattr(args, "saddle_l", None) or SADDLE_L_DEFAULT,
                        beacon=bool(getattr(args, "beacon", False)))
    B = {25: 3.6, 32: 4.4, 40: 5.2}[H]
    return GdSpec(H, B, SHEAVES[H], lattice_ladder=not bool(getattr(args, "no_lattice_ladder", False)))


CORNERS = ((1, 1), (-1, 1), (-1, -1), (1, -1))
# face f: (corner A, corner B, outward normal)
FACES = (((1, -1), (1, 1), XV), ((1, 1), (-1, 1), YV), ((-1, 1), (-1, -1), -XV),
         ((-1, -1), (1, -1), -YV))


def _pt(spec, z, c):
    h = spec.w(z) / 2.0
    return Vector((c[0] * h, c[1] * h, z))


def _mid(spec, z, f):
    a, b, _n = FACES[f]
    return (_pt(spec, z, a) + _pt(spec, z, b)) / 2.0


# --------------------------------------------------------------------------------------
# Shared lattice pieces
# --------------------------------------------------------------------------------------

def build_footings(P, spec, ped_h, pw, pier, bplate, leg_w):
    """
    4 open-bottom pedestals (side edges exactly at z 0, no bottom face - task / engine-base idiom)
    + open piers below (their top ring welds to the pedestal's bottom ring: one column, D9) + base
    plates + 2 gussets per leg foot.
    """
    B = spec.B
    for i, (sx, sy) in enumerate(CORNERS):
        fx, fy = sx * B / 2, sy * B / 2
        P.box(f"ped_{i}", (pw, pw, ped_h), (fx, fy, ped_h / 2), mat="concrete", drop=("-Z",))
        P.box(f"pier_{i}", (pw, pw, pier), (fx, fy, -pier / 2), mat="concrete", drop=("+Z", "-Z"))
        P.box(f"bplate_{i}", (bplate, bplate, 0.05), (fx, fy, ped_h + 0.025), mat="galv",
              drop=("-Z",))
        zt = ped_h + 0.05
        for k, (axis_out, yaw) in enumerate(((sx, 0.0 if sx > 0 else math.pi),
                                             (sy, math.pi / 2 if sy > 0 else -math.pi / 2))):
            if k == 0:
                loc = (fx + sx * leg_w / 2, fy, zt)
            else:
                loc = (fx, fy + sy * leg_w / 2, zt)
            P.kit(cs2_kit.gusset(f"{i}_{k}", 0.30, 0.30, t=0.02, loc=loc, rot=(0, 0, yaw)), "paint")


def leg_foot(spec, c, ped_h, z_next, w_next):
    """Point on the leg line (pedestal center at ped_h -> next corner) at the plate top ped_h + 0.05."""
    B = spec.B
    p0 = Vector((c[0] * B / 2, c[1] * B / 2, ped_h))
    p1 = Vector((c[0] * w_next / 2, c[1] * w_next / 2, z_next))
    t = (0.05) / (z_next - ped_h)
    return p0.lerp(p1, t)


def _travel_inset(f, w):
    """Detail pass 1 (DT10): of the four horizontal members meeting at the shaft corners (rings,
    K-brace mid members), the two along the travel axis (faces 1 / 3, normal +-Y) are 1 cm
    narrower, so their top / bottom faces no longer z-fight with the crossing member's (5 mm per
    face, centered: y-mirror symmetric)."""
    return w - DP1["inset"] if f in (1, 3) else w


def build_rings_and_bracing(P, spec, rings, ring_tag, lower, kind_of, w_strut, w_diag, alt_x):
    for i, z in enumerate(rings):
        tag = ring_tag(i, z)
        for f, (A, Bc, n) in enumerate(FACES):
            P.member(f"{tag}{i}_{f}", [_pt(spec, z, A), _pt(spec, z, Bc)], _travel_inset(f, w_strut), hint=n)
    for p, (z0, z1) in enumerate(lower):
        kind = kind_of(p, z0)
        for f, (A, Bc, n) in enumerate(FACES):
            if kind == "K":
                zm = (z0 + z1) / 2
                La, Lb = _pt(spec, zm, A), _pt(spec, zm, Bc)
                T, Bm = _mid(spec, z1, f), _mid(spec, z0, f)
                for k, (s, e) in enumerate(((La, T), (La, Bm), (Lb, T), (Lb, Bm))):
                    P.member(f"braceK_{p}_{f}_{k}", [s, e], w_diag, hint=n)
                P.member(f"braceKm_{p}_{f}", [La, Lb], _travel_inset(f, w_diag), hint=n)
            else:
                a0, a1 = (A, Bc) if (not alt_x or p % 2 == 0) else (Bc, A)
                P.member(f"braceXa_{p}_{f}", [_pt(spec, z0, a0), _pt(spec, z1, a1)], w_diag, hint=n)
                P.member(f"braceXb_{p}_{f}", [_pt(spec, z0, a1), _pt(spec, z1, a0)], w_diag, hint=n)


def build_plan(P, spec, z, idx, w_plan):
    """Plan diaphragm (P2P-24): a diamond through the face midpoints. Detail pass 1 (DT10): the four
    members meet in MITRES (each end cut by the bisector plane of the two members), so no two
    members overlap at a midpoint (2,744 cm2 same-facing on 60A); a width inset cannot do it
    y-mirror-symmetrically (plan_0 / plan_3 are mirror twins meeting at M0)."""
    M = [_mid(spec, z, f) for f in range(4)]
    D = [(M[(k + 1) % 4] - M[k]).normalized() for k in range(4)]
    for k in range(4):
        n0 = (D[k - 1] + D[k]).normalized()
        n1 = (D[k] + D[(k + 1) % 4]).normalized()
        P.member(f"plan_{idx}_{k}", [M[k], M[(k + 1) % 4]], w_plan, hint=ZV, end_planes=(n0, n1))


# --------------------------------------------------------------------------------------
# Detail pass 1 geometry helpers (plates, arrest rails, collars, kit fixes)
# --------------------------------------------------------------------------------------

def _drop_kit_faces(kit_parts, pred, name):
    """Delete, before the join, the faces of kit part `name` whose WORLD centroid c / normal n
    satisfy pred(c, n) (hidden contact faces of kit geometry, design 1.4). Returns the count."""
    for obj, _q in kit_parts:
        if obj.name != name:
            continue
        M = cs2_kit._object_matrix(obj)
        R = M.to_3x3()
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bm.normal_update()
        dead = [f for f in bm.faces if pred(M @ f.calc_center_median(), (R @ f.normal).normalized())]
        bmesh.ops.delete(bm, geom=dead, context="FACES")
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()
        return len(dead)
    raise ContractError(f"_drop_kit_faces: no kit part {name}")


def _metric_post_uv(obj):
    """cs2_kit cylinders (handrail posts, the transition flange's rim band) carry create_cone UVs that
    are unit-based (texel anisotropy 12x posts, 68x flange band; design 1.2): re-UV the SIDE faces as
    one metric strip (u = chord arc in meters, v = local z); caps keep their (isotropic) disc UVs."""
    me = obj.data
    lay = me.uv_layers["UVMap"]
    co = [v.co.copy() for v in me.vertices]
    side_faces = [p for p in me.polygons if abs(p.normal.z) < 0.5]
    ring_v = {me.loops[li].vertex_index for p in side_faces for li in range(p.loop_start, p.loop_start + p.loop_total)}
    angs = sorted({round(math.atan2(co[i].y, co[i].x), 6) for i in ring_v})
    segs = len(angs)
    r = max(math.hypot(co[i].x, co[i].y) for i in ring_v)
    side = 2.0 * r * math.sin(math.pi / max(segs, 3))
    idx = {a: i for i, a in enumerate(angs)}
    for p in side_faces:
        ks = [idx[round(math.atan2(co[me.loops[li].vertex_index].y, co[me.loops[li].vertex_index].x), 6)]
              for li in range(p.loop_start, p.loop_start + p.loop_total)]
        wrap = max(ks) - min(ks) > 1
        for li, k in zip(range(p.loop_start, p.loop_start + p.loop_total), ks):
            if wrap and k == 0:
                k = segs
            lay.data[li].uv = (k * side, co[me.loops[li].vertex_index].z)


def _chord_rail_uv(obj, segs):
    """cs2_kit.tube_along strips run u = k / segs around a circumference of 2 pi r while the faces are
    chords 2 r sin(pi / segs): texel anisotropy 1.11 on a 4-gon rail (design DT-C5). Scale the side
    faces' u by segs sin(pi / segs) / pi (caps keep their isotropic disc UVs)."""
    k = segs * math.sin(math.pi / segs) / math.pi
    me = obj.data
    lay = me.uv_layers["UVMap"]
    for p in me.polygons:
        us = [lay.data[li].uv[0] for li in range(p.loop_start, p.loop_start + p.loop_total)]
        if all(abs(u * segs - round(u * segs)) < 1e-5 for u in us) and max(us) - min(us) > 0.5 / segs:
            for li in range(p.loop_start, p.loop_start + p.loop_total):
                u, v = lay.data[li].uv
                lay.data[li].uv = (u * k, v)


def _dp1_handrail_fix(rail_parts, kick, lower_travel_kicks=True, segs=4):
    """Generator-local fixes on a cs2_kit handrail (cs2_kit is not this group's file):
    metric post UVs (anisotropy 12x -> 1.0) and, where spans meet at corners, the kick plates
    running along travel (x) end half a plate thickness short of the corner (a butt joint against
    the crossing plate instead of an overlap whose top and bottom faces z-fought, 2 cm2)."""
    for obj, _q in rail_parts:
        if obj.name.startswith("post_"):
            _metric_post_uv(obj)
        elif obj.name.startswith("rail_"):
            _chord_rail_uv(obj, segs)
        elif obj.name.startswith("kick_") and lower_travel_kicks:
            if abs(math.cos(obj.rotation_euler.z)) > 0.9:          # span along x
                bm = bmesh.new()
                bm.from_mesh(obj.data)
                half = max(abs(v.co.x) for v in bm.verts)
                for v in bm.verts:
                    v.co.x = math.copysign(half - DP1["kick_trim"], v.co.x)
                cs2_kit._box_project_uv(bm, bm.loops.layers.uv["UVMap"])
                bm.to_mesh(obj.data)
                bm.free()
                obj.data.update()


def build_plate(P, side, centre, n, w, h):
    """Tower plate (DT7): `plate_<side>` = the front quad PLATE proud mm off the host face (UV u =
    viewer-right, v = up in meters: the stamp reads unmirrored from outside, M3); `platerim_<side>` =
    4 rim quads running back 2 mm INTO the host (no gap, no back face). 2 + 8 tris."""
    c = Vector(centre)
    n = Vector(n).normalized()
    up = ZV.copy()
    right = up.cross(n).normalized()
    pr, ri = PLATE["proud"], PLATE["rim_in"]
    f = [c + n * pr + right * sx * w / 2.0 + up * sz * h / 2.0 for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    b = [q - n * (pr + ri) for q in f]
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    V = [bm.verts.new(q) for q in f]
    face = bm.faces.new(V)
    for lp in face.loops:
        d = lp.vert.co - c
        lp[uv].uv = (d.dot(right), d.dot(up))
    bm.normal_update()
    if face.normal.dot(n) < 0:
        raise ContractError(f"plate_{side}: front normal inward")
    plate = _link(f"plate_{side}", bm)
    rb = bmesh.new()
    ruv = rb.loops.layers.uv.new("UVMap")
    F = [rb.verts.new(q) for q in f]
    Bk = [rb.verts.new(q) for q in b]
    depth = pr + ri
    acc = 0.0
    for k in range(4):
        k1 = (k + 1) % 4
        q = rb.faces.new((Bk[k], Bk[k1], F[k1], F[k]))
        L = (f[k1] - f[k]).length
        for lp, (uu, vv) in zip(q.loops, ((acc, 0.0), (acc + L, 0.0), (acc + L, depth), (acc, depth))):
            lp[ruv].uv = (uu, vv)
        acc += L + 0.01
    rb.normal_update()
    cen = sum(f, Vector()) / 4.0
    for q in rb.faces:
        if q.normal.dot(q.calc_center_median() - (cen - n * (depth / 2.0))) < 0:
            q.normal_flip()
    rb.normal_update()
    rim = _link(f"platerim_{side}", rb)
    P.add(plate, "plate", aabb=True)
    P.add(rim, "plate", aabb=True)
    return plate, rim


def build_plates(P, spec):
    """Both travel faces of the top member (M10: the measured host planes)."""
    if spec.family == "tram":
        cfg = PLATE["tram"]
        z = float(spec.H) + cfg["dH"]
    else:
        cfg = PLATE["gondola"]
        z = spec.z0 + cfg["dA"]
    for side, sx in (("p", 1.0), ("n", -1.0)):
        build_plate(P, side, (sx * cfg["face_x"], 0.0, z), (sx, 0.0, 0.0), cfg["w"], cfg["h"])


def _channel(name, pts_nv, face, e_back, w, d):
    """3-face channel (front + two sides, no back) along a ladder polyline [(n, v, z)] in
    ladder-local coordinates: back at n + e_back, front e_back - d (outward = -n). Metric UVs."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rings = []
    for n_, v_, z_ in pts_nv:
        corners = []
        for dn, dv in ((e_back, -w / 2.0), (e_back - d, -w / 2.0), (e_back - d, w / 2.0), (e_back, w / 2.0)):
            x, y = face_xy(face, n_ + dn, v_ + dv)
            corners.append(bm.verts.new((x, y, z_)))
        rings.append(corners)
    zs = [p[2] for p in pts_nv]
    arc = [0.0]
    for a, b in zip(zs, zs[1:]):
        arc.append(arc[-1] + abs(b - a))
    acc = (0.0, d, d + w, 2 * d + w)
    out_n = Vector(face_xy(face, -1.0, 0.0) + (0.0,))
    v_dir = Vector(face_xy(face, 0.0, 1.0) + (0.0,))
    want = (-v_dir, out_n, v_dir)
    for j in range(len(rings) - 1):
        for k in range(3):
            q = bm.faces.new((rings[j][k], rings[j][k + 1], rings[j + 1][k + 1], rings[j + 1][k]))
            for lp, (uu, vv) in zip(q.loops, ((acc[k], arc[j]), (acc[k + 1], arc[j]),
                                              (acc[k + 1], arc[j + 1]), (acc[k], arc[j + 1]))):
                lp[uv].uv = (uu, vv)
            q.normal_update()
            if q.normal.dot(want[k]) < 0:
                q.normal_flip()
    bm.normal_update()
    return _link(name, bm)


def build_arrest(P, spec, plan):
    """Fall-arrest rail per ladder flight (section 3.1: lift-tower ladders carry a rail or cable, not
    cages), centered on the flight line, back 2 mm into the rung fronts, from the lowest rung to
    just under the rail tops. M5: registered as a MEMBER AXIS (the rail's center line, inside the
    C7 corridor's 0.04 start), so C7 / C8 sample its axis instead of the AABB that spans the
    batter."""
    face = plan["face"]
    e = LADDER["rung_r"] * (0.5 if face == "-X" else 1.0)       # rung's outward extent
    back = -e + ARREST["into"]
    for f in plan["flights"]:
        ns, zs = [p[0] for p in f["pts"]], [p[1] for p in f["pts"]]
        z_lo = f["rung_lo"] - 0.05
        z_hi = f["top"] - ARREST["end_below_top"]
        zz = [z_lo] + [b for b in face_breaks(spec) if z_lo + 1e-6 < b < z_hi - 1e-6] + [z_hi]
        pts = [(float(np.interp(z, zs, ns)), f["line"], z) for z in zz]
        name = f"arrest_{f['j']}"
        obj = _channel(name, pts, face, back, ARREST["w"], ARREST["d"])
        axis = [face_xy(face, n_ + back - ARREST["d"] / 2.0, v_) + (z_,) for n_, v_, z_ in pts]
        P.add(obj, "galv", axis=axis, w=ARREST["w"])


def build_collars(P, spec, lay, leg_axes):
    """Tram leg splice collars (section 5.1; skeptic S1): a capped square sleeve 0.48 (leg 0.40 +
    2 x 0.04) x 0.14 on the leg's own axis and face frame, centered COLLAR dz above the ring axis at
    ring indices >= 2 with an even index; the kink ring (the leg is mitred there) is replaced by
    the next ring up. Returns the ring indices used."""
    rings = lay["rings"]
    k_idx = min(range(len(rings)), key=lambda i: abs(rings[i] - spec.z_k))
    use = []
    for i in range(2, len(rings)):
        if i % 2 == 0:
            j = i + 1 if i == k_idx else i
            if j < len(rings) and j not in use:
                use.append(j)
    for li, (pts, frames) in enumerate(leg_axes):
        for k, ri in enumerate(use):
            zc = rings[ri] + COLLAR["dz"]
            for (a, b), (fa, fb) in zip(zip(pts, pts[1:]), frames):
                if a.z - 1e-9 <= zc <= b.z + 1e-9:
                    t = (b - a).normalized()
                    c = a + (b - a) * ((zc - a.z) / (b.z - a.z))
                    ends = [c - t * COLLAR["len"] / 2.0, c + t * COLLAR["len"] / 2.0]
                    obj = prism(f"collar_{li}_{k}", ends, COLLAR["w"] / math.sqrt(2.0), 4, fa, math.pi / 4,
                                caps="both")
                    # the two end caps (0.48 squares, 70 % buried in the 0.40 leg) are their own part
                    # `collarcap_` at a low density weight (0.3): full weight spent ~37 m2-equivalent of
                    # the atlas on hidden texels and dragged the bolt lattice under its floor there
                    cap = _split_faces(obj, lambda f, t=t: abs(f.normal.dot(t)) > 0.9, f"collarcap_{li}_{k}")
                    P.add(obj, "paint", axis=ends, w=COLLAR["w"])
                    P.add(cap, "paint")
                    break
            else:
                raise ContractError(f"collar: ring {ri} z {zc:.2f} not on leg {li}")
    return use


def _split_faces(obj, pred, name):
    """Move the faces of `obj` satisfying pred(face) into a new part `name` (same vertices: they weld
    back at the join; UVs kept). Returns the new object."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    sel = [f for f in bm.faces if pred(f)]
    nb = bmesh.new()
    nuv = nb.loops.layers.uv.new("UVMap")
    suv = bm.loops.layers.uv["UVMap"]
    vmap = {}
    for f in sel:
        vs = []
        for lp in f.loops:
            key = tuple(round(c, 7) for c in lp.vert.co)
            if key not in vmap:
                vmap[key] = nb.verts.new(lp.vert.co)
            vs.append(vmap[key])
        g = nb.faces.new(vs)
        for ln, lo in zip(g.loops, f.loops):
            ln[nuv].uv = lo[suv].uv
    bmesh.ops.delete(bm, geom=sel, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    nb.normal_update()
    return _link(name, nb)


def leg_frames(pts, hint):
    """Per segment (a, b) face frame of a member polyline as prism() transports it (the collars
    sit on the leg's own faces)."""
    P_ = [Vector(p) for p in pts]
    T = [(b - a).normalized() for a, b in zip(P_, P_[1:])]
    a, b = _frame(T[0], Vector(hint))
    out = [(a.copy(), b.copy())]
    for i in range(1, len(T)):
        R = T[i - 1].rotation_difference(T[i])
        a, b = R @ a, R @ b
        out.append((a.copy(), b.copy()))
    return P_, out


def build_ladder(P, runs, x_planes, tag=""):
    """Straight vertical ladder runs [(z_start, z_end)] on planes x (gondola mast ladder); rails
    y +-0.21, triangular rungs every 0.30."""
    ry, rw, pitch = LADDER["rail_y"], LADDER["rail_w"], LADDER["pitch"]
    for j, (za, zb) in enumerate(runs):
        x = x_planes[j % len(x_planes)]
        for sy, s in ((ry, "p"), (-ry, "n")):
            P.member(f"lrail_{tag}{j}_{s}", [(x, sy, za), (x, sy, zb)], rw, hint=XV, mat="galv")
        k, z = 0, za + LADDER["first"]
        while z <= zb - 0.10 + 1e-9:
            P.tri(f"rung_{tag}{j}_{k}", [(x, -ry, z), (x, ry, z)], LADDER["rung_r"], "galv")
            k += 1
            z += pitch


# --------------------------------------------------------------------------------------
# Ladder system (Revision 1, V7a / V8a): ground -> head outside the -X face, side baskets,
# offset flights. Pure planning first (ladder_plan, used by the build AND by C7), then geometry.
# --------------------------------------------------------------------------------------

BASKET_FLOOR_DZ = BASKET["string_w"] / 2.0 + BASKET["t"]     # ring axis -> basket floor top


def x_face(spec, z):
    """x of the -X face plane (through the leg axes) at height z; above the shaft top the face is
    the head frame (tram, chord line x -1.5) or the transition frame (gondola, x -0.9). Below the
    base ring (pedestal tops) the ladder stands plumb: extrapolating the flare to z 0 would push the
    ladder foot past the pedestals' travel extent and the LOD1/2 bounds would drift (C9)."""
    if spec.family == "tram":
        z = max(z, PED_H_TR)
        return -NECK_W_TOP / 2.0 if z >= spec.hc else -spec.w(z) / 2.0
    z = max(z, PED_H_GD)
    return -GD_TOP_W / 2.0 if z >= spec.z_t else -spec.w(z) / 2.0


def face_breaks(spec):
    """Heights where the -X face changes slope (the ladder rails bend there, mitred)."""
    return (PED_H_TR, spec.z_k, spec.hc) if spec.family == "tram" else (PED_H_GD, spec.z_t)


def ladder_arrival(spec):
    """Floor the last flight lands on: the tram head deck top / the gondola transition platform top."""
    if spec.family == "tram":
        return spec.z0 - 1.185 + 0.015
    return spec.z_t + 0.25 + 0.015


def choose_breaks(rings, z_top, target=None):
    """Basket levels = ring levels (a ring strut carries each basket). Every subset of the
    candidate rings is scored lexicographically: flights over the soft target, number of flights,
    longest flight, unevenness - flights over the hard maximum are never allowed. Deterministic."""
    from itertools import combinations
    cands = sorted(float(z) for z in rings if 2.0 < z < z_top - 2.0)
    hard = LADDER["flight_max"]
    target = LADDER["flight_target"] if target is None else target
    best = None
    for n in range(len(cands) + 1):
        for combo in combinations(cands, n):
            stops = [0.0, *combo, z_top]
            L = [b - a for a, b in zip(stops, stops[1:])]
            if max(L) > hard + 1e-9:
                continue
            mean = z_top / len(L)
            key = (sum(1 for x in L if x > target + 1e-9), len(L), round(max(L), 4),
                   round(sum((x - mean) ** 2 for x in L), 6), combo)
            if best is None or key < best:
                best = key
    if best is None:
        raise ContractError(f"C7: no ring sequence keeps every flight <= {hard} m (top {z_top:.2f})")
    return list(best[-1])


def face_xy(face, n, v):
    """Ladder-local (n = coordinate along the face's outward axis, v = lateral along the face) ->
    authoring (x, y). Face "-X": n is x, v is y. Face "-Y": n is y, v is x."""
    return (n, v) if face == "-X" else (v, n)


def face_nv(face, P):
    """Authoring points (N, 3) -> (n, v) columns for `face`."""
    return (P[:, 0], P[:, 1]) if face == "-X" else (P[:, 1], P[:, 0])


def ladder_bound(spec):
    """The bound the ladder face's outward axis must stay inside on EVERY LOD (C9 drift <= 0.01):
    tram -X = the pedestals' travel extent; gondola -Y = the gantry beam's lateral half length."""
    if spec.family == "tram":
        return (spec.B + PW_TR) / 2.0
    return GD_BEAM_Y


def ladder_depth(spec):
    """How far flight 0's rails run below grade (revision 1c, revision1b-check-tram N1). A variant
    the D13 score can float (variant_float > 0: 30A / 45A, 25O / 32O) runs them - and its rungs - down
    to the pier depth (12 / 8 = the family's max float + slope allowance), so a floated base shows a
    climbable ladder down its exposed piers. 60A and 40O never float: LADDER_FAM sink, rails only."""
    if variant_float(spec) > 0.0:
        return PIER_TR if spec.family == "tram" else PIER_GD
    return LADDER_FAM[spec.family]["sink"]


def ladder_plan(spec, lay):
    """Flights and baskets of the ground-to-head ladder in ladder-local coordinates (n along the
    face's outward axis - negative, v lateral along the face). Flight j climbs from floor f0 (ground
    or basket j-1) to floor f1 (basket j or the arrival floor) on lateral line `line`; its rails
    continue `grab` above f1. Lines alternate so the LAST flight arrives on line 0 (tram: the
    centered gate in the deck rail; gondola: the open -Y edge of the transition platform) and every
    flight is offset from the one below. The face is the square shaft's -X (tram) or -Y (gondola)
    face; both sit at n = x_face(z) because the shaft is square."""
    fam = LADDER_FAM[spec.family]
    s, lines, face = fam["s"], fam["lines"], fam["face"]
    z_arr = ladder_arrival(spec)
    bound = ladder_bound(spec)
    ok_rings = [z for z in lay["rings"] if abs(x_face(spec, z) - s - fam["bskt_depth"]) <= bound - 0.01]
    breaks = choose_breaks(ok_rings, z_arr, fam.get("flight_target", LADDER["flight_target"]))
    floors = [0.0] + [round(z + BASKET_FLOOR_DZ, 6) for z in breaks] + [round(z_arr, 6)]
    n = len(floors) - 1
    flights = []
    for j in range(n):
        f0, f1 = floors[j], floors[j + 1]
        top = f1 + LADDER["grab"]
        z_rail0 = -ladder_depth(spec) if j == 0 else f0     # revisions 1b / 1c: flight 0 continues below grade
        # revision 1c (N1): on a variant that floats, rungs continue below grade too, on the same
        # 0.30 pitch down from the first rung at f0 + 0.25, the lowest >= 0.25 above the rail foot
        # revision 1d (60A / 40O, never floated): rungs down the sink as well, the lowest >= 0.25
        # above the rail foot and >= LADDER_FAM sink_rung_lo where set (40O's 4,500-tri cap)
        below = 0
        if j == 0 and variant_float(spec) > 0.0:
            below = int(math.floor((f0 - z_rail0) / LADDER["pitch"] + 1e-9))
        elif j == 0:
            lo = max(z_rail0 + LADDER["first"], fam.get("sink_rung_lo", -1e9))
            below = max(0, int(math.floor((f0 + LADDER["first"] - lo) / LADDER["pitch"] + 1e-9)))
        zs = [z_rail0] + [b for b in face_breaks(spec) if f0 + 1e-6 < b < top - 1e-6] + [top]
        flights.append({"j": j, "line": float(lines[(n - 1 - j) % 2]), "f0": f0, "f1": f1, "top": top,
                        "rail_z0": z_rail0, "rungs_below": below,
                        "rung_lo": round(f0 + LADDER["first"] - below * LADDER["pitch"], 6),
                        "pts": [(round(x_face(spec, z) - s, 6), round(z, 6)) for z in zs]})
    baskets = []
    for j, zr in enumerate(breaks):
        a, b = flights[j]["line"], flights[j + 1]["line"]
        up = b > a                                   # the next line lies on the +v side
        v_near = a + LADDER["edge_gap"] if up else a - LADDER["edge_gap"]
        v_far = b + LADDER["far_ext"] if up else b - LADDER["far_ext"]
        nf = x_face(spec, zr)
        baskets.append({"j": j, "z_ring": zr, "floor": floors[j + 1], "v_near": v_near, "v_far": v_far,
                        "v0": min(v_near, v_far), "v1": max(v_near, v_far), "n_face": nf,
                        "n_in": nf - fam["bskt_in"], "n_out": nf - s - fam["bskt_depth"]})
    return {"family": spec.family, "face": face, "s": s, "lines": list(lines), "arrival": z_arr,
            "flights": flights, "baskets": baskets}


def build_ladder_system(P, spec, plan):
    """Geometry of ladder_plan: per flight two rails (square 0.05, mitred at the face breaks, level
    cuts, capped at the grab-rail tops) + triangular rungs every 0.30 from f0 + 0.25 up to f1 (flight 0
    also below grade, rung_0_b*: to the rail foot on a floatable variant, revision 1c; down the sink on
    60A / 40O, revision 1d); per basket a grating on two stringers buried in the ring strut, posts and top / mid rails on the outer
    and far sides (open toward the arriving flight and the tower)."""
    face = plan["face"]
    hint = XV if face == "-X" else YV

    def xyz(n, v, z):
        x, y = face_xy(face, n, v)
        return (x, y, z)
    ry, rw, pitch = LADDER["rail_y"], LADDER["rail_w"], LADDER["pitch"]
    for f in plan["flights"]:
        j, v = f["j"], f["line"]
        ns, zs = [p[0] for p in f["pts"]], [p[1] for p in f["pts"]]
        for sv, tag in ((ry, "p"), (-ry, "n")):
            P.member(f"lrail_{j}_{tag}", [xyz(nn, v + sv, z) for nn, z in f["pts"]], rw, mat="galv",
                     hint=hint, caps="end", end_planes=(ZV, ZV))     # level cuts: foot ON the floor
        k, z = 0, f["f0"] + LADDER["first"]
        while z <= f["f1"] + 1e-6:
            nn = float(np.interp(z, zs, ns))
            P.tri(f"rung_{j}_{k}", [xyz(nn, v - ry, z), xyz(nn, v + ry, z)], LADDER["rung_r"], "galv")
            k += 1
            z += pitch
        for k in range(1, f["rungs_below"] + 1):     # revision 1c: below grade on a floatable variant
            z = f["f0"] + LADDER["first"] - k * pitch
            nn = float(np.interp(z, zs, ns))
            P.tri(f"rung_{j}_b{k}", [xyz(nn, v - ry, z), xyz(nn, v + ry, z)], LADDER["rung_r"], "galv")
    B = BASKET
    ins = 0.03                                       # rails / posts sit on the grating edge
    for b in plan["baskets"]:
        j, zr, fl = b["j"], b["z_ring"], b["floor"]
        nin, nout = b["n_in"], b["n_out"]
        cx, cy = face_xy(face, (nin + nout) / 2.0, (b["v0"] + b["v1"]) / 2.0)
        ex, ey = face_xy(face, nin - nout, b["v1"] - b["v0"])
        P.kit(cs2_kit.grating(f"bskt{j}", ex, ey, t=B["t"], loc=(cx, cy, fl - B["t"] / 2.0)), "galv",
              aabb=True)
        # detail pass 1 (DT-C4): the stringers 1 mm below the grating (their tops were half-buried
        # contact faces under it that no drop can reclaim: the inboard 2 cm are open)
        zs_ = zr - 0.001
        for k, ve in enumerate((b["v0"] + 0.05, b["v1"] - 0.05)):
            P.member(f"bskt_{j}_s{k}", [xyz(b["n_face"], ve, zs_), xyz(nout + 0.02, ve, zs_)], B["string_w"],
                     mat="galv", hint=ZV, caps="end")
        sgn = 1.0 if b["v_far"] > b["v_near"] else -1.0
        vfar, vnear = b["v_far"] - sgn * ins, b["v_near"] + sgn * ins
        path = [(nin - ins, vfar), (nout + ins, vfar), (nout + ins, vnear)]
        for k, (pn, pv) in enumerate(path):
            # detail pass 1 (DT10): posts turned 15 deg so no post face is parallel to a rail face
            # (posts and rails shared +-y / +-x face planes at every corner: 15 + 7 cm2 per pair)
            P.tri(f"bskt_{j}_p{k}", [xyz(pn, pv, fl), xyz(pn, pv, fl + B["rail_h"])], B["post_w"] / 2.0, "galv",
                  phase=math.pi / 2 + math.pi / 12)
        for tag, h in (("top", B["rail_h"]), ("mid", B["mid_h"])):
            P.tri(f"bskt_{j}_{tag}", [xyz(pn, pv, fl + h) for pn, pv in path], B["rail_w"] / 2.0, "galv",
                  caps="both", uv_breaks=(1,))


def ladder_summary(plan):
    """JSON-friendly ladder block for the layout / manifest."""
    if not plan:
        return None
    net = {"-X": "travel -z", "-Y": "lateral -x"}[plan["face"]]
    return {"face": f"{plan['face']} (authoring) = {net} in the net frame", "standoff_m": plan["s"],
            "lines": plan["lines"], "arrival_z": round(plan["arrival"], 3),
            "rails_from_z": round(plan["flights"][0]["rail_z0"], 3),
            "rungs_from_z": round(plan["flights"][0]["rung_lo"], 3),
            "flights": [{"line": f["line"], "from": round(f["f0"], 3), "to": round(f["f1"], 3),
                         "length": round(f["f1"] - f["f0"], 3)} for f in plan["flights"]],
            "baskets": [{"z_ring": round(b["z_ring"], 3), "floor": round(b["floor"], 3),
                         "lateral": [round(b["v0"], 3), round(b["v1"], 3)]} for b in plan["baskets"]]}


# --------------------------------------------------------------------------------------
# Tram tower
# --------------------------------------------------------------------------------------

def tram_layout(spec):
    lower, slope, alpha = geo_panels(PED_H_TR, spec.z_k, spec.B, NECK_W_KINK, 0.85)
    dn = (spec.hc - spec.z_k) / spec.n_neck
    neck = [(spec.z_k + i * dn, spec.z_k + (i + 1) * dn) for i in range(spec.n_neck)]
    rings = [p[0] for p in lower] + [spec.z_k] + [p[0] for p in neck[1:]]
    deck_under = spec.z0 - 1.20
    # plan diaphragms (P2P-24): the section 5.4 landing rule's levels, now diaphragms only (the
    # centered landings were the verdict 8 defect; the ladder system carries its own side baskets)
    plan_lv, last = [], 0.0
    while True:
        reach = [z for z in rings if last < z <= last + 11.0 and z < spec.z_k - 1e-6 and z > 2.0]
        if not reach:
            break
        plan_lv.append(max(reach))
        last = plan_lv[-1]
    plan_lv.append(spec.z_k)
    if deck_under - spec.z_k > 9.0:
        plan_lv.append(max(z for z in rings if z > spec.z_k + 1e-6))
    kinds = ["K" if spec.w(z0) >= 6.0 else "X" for z0, _z1 in lower]
    return {"lower": lower, "neck": neck, "rings": rings, "plan_levels": plan_lv, "kinds": kinds,
            "flare_deg": math.degrees(math.atan(slope)), "alpha": alpha}


def saddle_profile(spec):
    """Top line (x, z) of a saddle girder: the rope underside of the level kinked lane for |x| <= xc
    (MC3: never above the rope), then a parabolic lead-out to `tip` extra drop at the ends, so the
    girder reads curved in elevation (V8b; P2P-33, JH-15). 7 sections: -L2, -xm, -xc, 0, xc, xm, L2."""
    L2, xc, tip = spec.saddle_l / 2.0, SADDLE["xc"], SADDLE["tip"]
    top0 = spec.z0 - TRAM["rope_r_hv"]
    xm = (xc + L2) / 2.0
    pts = []
    for x in (-L2, -xm, -xc, 0.0, xc, xm, L2):
        z = top0 - spec.s0 * abs(x)
        if abs(x) > xc + 1e-9:
            z -= tip * ((abs(x) - xc) / (L2 - xc)) ** 2
        pts.append((x, z))
    return pts


def build_tram_tower(spec):
    P = Parts()
    lay = tram_layout(spec)
    z0, hc, zk, B = spec.z0, spec.hc, spec.z_k, spec.B
    # legs: ONE member foot -> kink -> head chord, mitred at the kink, UV island break there
    leg_axes = []
    for i, c in enumerate(CORNERS):
        foot = leg_foot(spec, c, PED_H_TR, zk, NECK_W_KINK)
        kink = Vector((c[0] * NECK_W_KINK / 2, c[1] * NECK_W_KINK / 2, zk))
        top = Vector((c[0] * NECK_W_TOP / 2, c[1] * NECK_W_TOP / 2, hc))
        P.member(f"leg_{i}", [foot, kink, top], W_TR["leg"], hint=XV, end_planes=(ZV, ZV),
                 uv_breaks=(1,), caps="end")
        leg_axes.append(leg_frames([foot, kink, top], XV))
    # detail pass 1 (section 5.1): leg splice collars
    lay["collar_rings"] = build_collars(P, spec, lay, leg_axes)
    nlow = len(lay["lower"])

    def ring_tag(i, z):
        # Revision 1: the base ring (i 0, on the pedestal tops) drops at LOD2 (it is invisible at
        # LOD2 range) - pays for the curved saddles' extra sections in the 500-tri far cap
        return "ringL2_" if (0 < i < nlow and i % 3 == 0) or abs(z - zk) < 1e-6 else "ringL1_"
    build_rings_and_bracing(P, spec, lay["rings"], ring_tag, lay["lower"],
                            lambda p, z: lay["kinds"][p], W_TR["strut"], W_TR["diag"], alt_x=False)
    for p, (za, zb) in enumerate(lay["neck"]):
        for f, (A, Bc, n) in enumerate(FACES):
            P.member(f"braceN_{p}_{f}_a", [_pt(spec, za, A), _pt(spec, zb, Bc)], W_TR["neck"], hint=n)
            P.member(f"braceN_{p}_{f}_b", [_pt(spec, za, Bc), _pt(spec, zb, A)], W_TR["neck"], hint=n)
    for i, z in enumerate(lay["plan_levels"]):
        build_plan(P, spec, z, i, W_TR["plan"])
    # V7a / V8a: ground -> head deck ladder outside the -X face, side baskets, offset flights
    plan = ladder_plan(spec, lay)
    build_ladder_system(P, spec, plan)
    build_arrest(P, spec, plan)              # detail pass 1: one fall-arrest rail per flight
    P.ladder = plan
    lay["ladder"] = ladder_summary(plan)
    build_tram_head(P, spec)
    build_footings(P, spec, PED_H_TR, PW_TR, PIER_TR, BPLATE_TR, W_TR["leg"])
    return P, lay


def build_tram_head(P, spec):
    z0, hx, hy, hw = spec.z0, HEAD["x"], HEAD["y"], HEAD["w"]
    zt, zb = z0 + HEAD["top_dz"], z0 + HEAD["bot_dz"]
    k = 0
    for x in (hx, -hx):
        for z in (zt, zb):
            # L-corner joints: chord to the end vertical's outer face, capped (no open end)
            P.member(f"headchord_{k}", [(x, -hy - hw / 2, z), (x, hy + hw / 2, z)], hw, hint=XV,
                     caps="both")
            k += 1
    k = 0
    for x in (hx, -hx):
        for y in (hy, -hy):
            # between the chords' inner faces: open ends sit flush on the chord faces (no coplanar
            # cap against the chord cap, no z-fight); the capped chords close the corner
            P.member(f"headend_{k}", [(x, y, zb + hw / 2), (x, y, zt - hw / 2)], hw, hint=XV)
            k += 1
    k = 0
    for x in (hx, -hx):
        for y in HEAD["verts_y"]:
            for sy in (1, -1):
                P.member(f"headvert_{k}", [(x, sy * y, zb), (x, sy * y, zt)], 0.22, hint=XV)
                k += 1
    k = 0
    for y in HEAD["x_members_y"]:
        for sy in (1, -1):
            for z in (zt, zb):
                # detail pass 1 (DT10): 0.22 -> 0.20, so its +-y faces leave the headvert_ faces'
                # plane (3,872 cm2 same-facing on 60A)
                P.member(f"headx_{k}", [(-hx, sy * y, z), (hx, sy * y, z)], DP1["headx_w"], hint=YV)
                k += 1
    # y-mirror symmetric truss (C11): outer bays rise inboard, the central bay is a V
    y1, y2 = HEAD["verts_y"]
    diag = [((-hy, zb), (-y2, zt)), ((-y2, zb), (-y1, zt)), ((-y1, zt), (0.0, zb)),
            ((y1, zt), (0.0, zb)), ((y2, zb), (y1, zt)), ((hy, zb), (y2, zt))]
    k = 0
    for x in (hx, -hx):
        for (ya, za), (yb, zb_) in diag:
            # detail pass 1 (DT10): every diagonal ends ON the chord faces (cut level: top chord's
            # bottom face / bottom chord's top face) instead of at the chord axes, where the apex
            # pairs (the central V at the bottom, 1|2 and 3|4 at the top) crossed with coplanar
            # +-x faces (661 cm2 same-facing on 60A); the open ends sit on the chord faces
            def at(zc):
                s_ = (zc - za) / (zb_ - za)
                return (x, ya + (yb - ya) * s_, zc)
            za_c = za - hw / 2.0 if za > zb_ else za + hw / 2.0
            zb_c = zb_ + hw / 2.0 if za > zb_ else zb_ - hw / 2.0
            P.member(f"headdiag_{k}", [at(za_c), at(zb_c)], 0.16, hint=XV, end_planes=(ZV, ZV))
            k += 1
    # V8b saddles: one curved box girder per lane on the crossarm ends (the R29 crossing: the arms
    # and the knee braces are the accepted inboard-upright contact), short seat posts, the knee
    # braces from the head bottom chord, and the haul-rope sheave row along the girder
    prof = saddle_profile(spec)
    sw, sd = SADDLE["w"], SADDLE["d"]
    arm_top = z0 - 1.20
    for s, tag in ((1, "p"), (-1, "n")):
        ly = s * TRAM["lane_y"]
        # detail pass 1 (Q8): the girder in tower steel, its top face (the rope seat) a rubber liner
        body, liner = saddle_part(f"saddle_{tag}", ly, prof, sw, sd, liner=f"saddle_liner_{tag}")
        P.add(body, "paint")
        P.add(liner, "rubber")
        for kk, x in enumerate((hx, -hx)):
            P.member(f"sbrkarm_{tag}_{kk}", [(x, s * (hy + hw / 2), z0 - 1.35), (x, s * 5.65, z0 - 1.35)], 0.30,
                     hint=ZV, caps="end")
        for kk, x in enumerate((hx, -hx)):
            z_post_top = _interp_profile(prof, x) - sd
            P.member(f"sbrk_{tag}_{kk}", [(x, ly, arm_top), (x, ly, z_post_top)], 0.20, hint=XV)
            P.member(f"sbrk_{tag}_{2 + kk}", [(x, s * hy, zb), (x, s * 5.00, z0 - 1.50)], 0.20, hint=XV,
                     end_planes=(None, ZV))
        build_haul_row(P, spec, s, tag)
    # deck + handrail (y-mirror symmetric path; the -X side opens over |y| < gate where the ladder
    # system's last flight arrives: V7a)
    deck = cs2_kit.grating("deck", DECK["w"], DECK["l"], t=0.03, loc=(0, 0, z0 - 1.185))
    # detail pass 1 (design 1.4): the lip's bottom strips along the head top chords (x +-1.5) lie on
    # the chords' top faces (hidden contact, 4 tris): not built
    _drop_kit_faces(deck, lambda c, n: n.z < -0.9 and abs(c.x) > DECK["w"] / 2.0 - 0.05, "grate_deck_lip")
    P.kit(deck, "galv", aabb=True)
    rx, ry, zd, g = DECK["rail_x"], DECK["rail_y"], z0 - 1.17, DECK["gate"]
    path = [(-rx, -g, zd), (-rx, -ry, zd), (rx, -ry, zd), (rx, ry, zd), (-rx, ry, zd), (-rx, g, zd)]
    rail = cs2_kit.handrail("deck", path, h=DECK["h"], knee=DECK["knee"], kick=0.10, post_every=1.5,
                            segs=4, radius=0.025)
    _dp1_handrail_fix(rail, kick=0.10)
    P.kit(rail, "galv")
    build_upper_frame(P, spec)
    build_cranes(P, spec)
    build_plates(P, spec)
    if spec.beacon:
        # the --beacon fallback only (M8 option 2, not shipped): emissive lens under the beam
        zb_beam = z0 + UPPER["beam_z"] - 0.11
        # (the ring's top lies on the beam, the lens' top on the ring: hidden contact caps not built)
        ring = cs2_kit.add_cyl("beacon_ring", 0.15, 0.05, (0, 0, zb_beam - 0.025), segments=8, caps="bottom")
        lens = cs2_kit.add_cyl("beacon_lens", 0.12, 0.20, (0, 0, zb_beam - 0.05 - 0.10), segments=8,
                               caps="bottom")
        P.add(ring, "galv")
        P.add(lens, "lens")


def build_haul_row(P, spec, s, tag):
    """V8b: the haul rope's sheave row along the saddle's lower inboard edge (JH-15). Sheave tops on
    the level kinked haul-rope bottom line (C4); a carrier bar just inboard of the row on the axle
    line (kinked at x 0 like the rope); ties from the bar to the girder web between sheaves. All
    'haulroll_' (R30a_in / haul-grip accepted class: the grips ride over the sheaves)."""
    R = ROLLER
    yh = s * TRAM["haul_y"]

    def axle(x):
        return spec.z0 + TRAM["haul_dz"] - TRAM["rope_r_lv"] - spec.s0 * abs(x) - R["r"]
    for kk, x in enumerate(R["x"]):
        # detail pass 1 (design 5.1): the V flanks (the rope faces) split off as rubber liners
        sh, ln = _haul_wheel(f"haulroll_{tag}_{kk}", f"haulroll_liner_{tag}_{kk}", (x, yh, axle(x)))
        P.add(sh, cs2_sheaves.wheel_key("galv"))        # T62 round 2: the one wheel material under v2
        P.add(ln, "rubber")
    yb = s * (TRAM["haul_y"] - R["bar_dy"])
    xe = max(R["x"]) + R["r"]
    P.member(f"haulroll_bar_{tag}", [(-xe, yb, axle(xe)), (0.0, yb, axle(0.0)), (xe, yb, axle(xe))],
             R["bar_w"], hint=ZV, caps="both")
    y_web = s * (TRAM["lane_y"] - SADDLE["w"] / 2.0 + 0.05)
    # detail pass 1 (DT10): the tie starts on the bar's outboard face + 5 mm inside it, not on the
    # bar axis (its faces crossed the bar's in-plane: 2 x 24 cm2 same-facing)
    y0 = yb + s * (R["bar_w"] / 2.0 - DP1["tie_short"])
    for k, x in enumerate(R["tie_x"]):
        P.member(f"haulroll_tie_{tag}_{k}", [(x, y0, axle(x)), (x, y_web, axle(x))], R["tie_w"], hint=ZV)


def build_upper_frame(P, spec):
    """V7b: the upper stabilizing frame (P2P-11/26/33, JH-09/15). Inner posts continue the leg tops
    (y +-1.5) from the head top chord to the top beams; top beams (bounds top = H) span y +-6.0 so
    each tip overhangs its rope lane; cross beams along x; per side a hanger post on each crossarm
    end (lane - 1.25), a main diagonal from the inner post top down to the crossarm end, a knee from
    the hanger post to the beam tip, a tie between the two hanger posts; lifting lugs under the tips."""
    z0, hx, U = spec.z0, HEAD["x"], UPPER
    zbeam = z0 + U["beam_z"]
    zbeam_bot = zbeam - U["beam_w"] / 2.0
    zchord_top = z0 + HEAD["top_dz"] + HEAD["w"] / 2.0
    k = 0
    for x in (hx, -hx):
        for sy in (1, -1):
            P.member(f"gantrypost_{k}", [(x, sy * U["post_y"], zchord_top), (x, sy * U["post_y"], zbeam_bot)],
                     U["post_w"], hint=XV, end_planes=(ZV, ZV))
            k += 1
    for kk, x in enumerate((hx, -hx)):
        P.member(f"gantrybeam_{kk}", [(x, -U["beam_y"], zbeam), (x, U["beam_y"], zbeam)], U["beam_w"],
                 hint=ZV, caps="both")
    ys = sorted({sy * yy for yy in U["x_beams_y"] for sy in (1, -1)})
    for kk, y in enumerate(ys):
        P.member(f"gantryx_{kk}", [(-hx, y, zbeam), (hx, y, zbeam)], 0.22, hint=ZV)
    arm_top = z0 - 1.20
    (ka_y, ka_z), (kb_y, kb_z) = U["knee"]
    for s, tag in ((1, "p"), (-1, "n")):
        yh = s * U["hang_y"]
        for kk, x in enumerate((hx, -hx)):
            P.member(f"sbrkhang_{tag}_{kk}", [(x, yh, arm_top), (x, yh, zbeam_bot)], U["hang_w"], hint=XV,
                     end_planes=(ZV, ZV))
            P.member(f"sbrkdiag_{tag}_{kk}", [(x, s * U["post_y"], z0 + U["diag_top"]),
                                              (x, yh, z0 + U["diag_bot"])], U["diag_w"], hint=XV)
            P.member(f"sbrkknee_{tag}_{kk}", [(x, s * ka_y, z0 + ka_z), (x, s * kb_y, z0 + kb_z)],
                     U["knee_w"], hint=XV, end_planes=(None, ZV))
        P.member(f"sbrkhang_t_{tag}", [(-hx, yh, z0 + U["tie_z"]), (hx, yh, z0 + U["tie_z"])], U["tie_w"],
                 hint=ZV)
    for kk, (x, sy) in enumerate(((hx, 1), (hx, -1), (-hx, 1), (-hx, -1))):
        P.box(f"lug_{kk}", (0.20, 0.20, 0.40), (x, sy * U["lug_y"], zbeam_bot - 0.20), drop=("+Z",))


def build_cranes(P, spec):
    """V8c: one slewing pillar jib crane per side (P2P-15): column on the head deck at x 0, jib at
    the top-beam level (its top = H) parked over its rope lane, knee, trolley + hook block over the
    lane (dz >= 2.58: clear of the carriage band), covered winch on the column."""
    z0, C, U = spec.z0, CRANE, UPPER
    z_deck = z0 - 1.17
    zj = z0 + U["beam_z"]
    jb = zj - C["jib_w"] / 2.0
    for s, tag in ((1, "p"), (-1, "n")):
        yc = s * C["y"]
        col = [(0.0, yc, z_deck), (0.0, yc, jb)]
        P.add(prism(f"cranecol_{tag}", col, C["col_r"], C["col_segs"], XV), "crane", axis=col,
              w=C["col_r"] * math.sqrt(2.0))
        P.member(f"cranejib_{tag}", [(0.0, s * C["jib_y0"], zj), (0.0, s * U["beam_y"], zj)], C["jib_w"],
                 mat="crane", hint=ZV, caps="both")
        P.member(f"craneknee_{tag}", [(0.0, yc, z0 + C["knee_z"]), (0.0, s * C["knee_y"], jb)], C["knee_w"],
                 mat="crane", hint=XV, end_planes=(None, ZV))
        yh = s * C["hook_y"]
        P.box(f"cranetrol_{tag}", (0.32, 0.40, 0.30), (0.0, yh, jb - 0.15), mat="dark")
        zh = z0 + C["hook_bot"]
        P.member(f"cranehook_{tag}_w", [(0.0, yh, jb - 0.30), (0.0, yh, zh + 0.22)], 0.03, mat="dark", hint=XV)
        P.box(f"cranehook_b{tag}", (0.18, 0.18, 0.22), (0.0, yh, zh + 0.11), mat="crane")
        P.box(f"cranewinch_{tag}", (0.40, 0.34, 0.50), (0.0, s * (C["y"] - C["col_r"] - 0.17), z0 + 0.65),
              mat="galv")


def _interp_profile(prof, x):
    xs = [p[0] for p in prof]
    zs = [p[1] for p in prof]
    return float(np.interp(x, xs, zs))


# --------------------------------------------------------------------------------------
# Gondola lattice + GD head
# --------------------------------------------------------------------------------------

def gd_layout(spec):
    lower, slope, alpha = geo_panels(PED_H_GD, spec.z_t, spec.B, GD_TOP_W, 1.0)
    rings = [p[0] for p in lower]
    plan = [rings[i] for i in range(len(rings)) if i % 3 == 0 and i > 0]
    N = spec.n_sheaves
    p = SHEAVE["pitch"]
    xs = [(k - (N - 1) / 2.0) * p for k in range(N)]
    return {"lower": lower, "rings": rings, "plan": plan, "kinds": ["X"] * len(lower),
            "flare_deg": math.degrees(math.atan(slope)), "alpha": alpha, "sheave_x": xs,
            "train_half": (N - 1) * p / 2 + SHEAVE["r"], "deck_l": (N - 1) * p}


def build_gondola_lattice(spec):
    P = Parts()
    lay = gd_layout(spec)
    zt = spec.z_t
    for i, c in enumerate(CORNERS):
        foot = leg_foot(spec, c, PED_H_GD, zt, GD_TOP_W)
        top = Vector((c[0] * GD_TOP_W / 2, c[1] * GD_TOP_W / 2, zt))
        P.member(f"leg_{i}", [foot, top], W_GD["leg"], hint=XV, end_planes=(ZV, ZV))

    def ring_tag(i, z):
        return "ringL2_" if i % 4 == 0 else ("ringL1_" if i % 2 == 0 else "ringL0_")
    build_rings_and_bracing(P, spec, lay["rings"], ring_tag, lay["lower"], lambda p, z: "X",
                            W_GD["strut"], W_GD["diag"], alt_x=True)
    idx = {z: i for i, z in enumerate(lay["rings"])}
    for z in lay["plan"]:
        build_plan(P, spec, z, idx[z], W_GD["plan"])
    if spec.lattice_ladder:
        # V7a / V8a (the tram lesson): ground -> transition platform outside the -Y face,
        # side baskets, offset flights; the mast ladder continues from the platform
        plan = ladder_plan(spec, lay)
        build_ladder_system(P, spec, plan)
        build_arrest(P, spec, plan)          # detail pass 1: one fall-arrest rail per flight
        P.ladder = plan
        lay["ladder"] = ladder_summary(plan)
    # transition frame (D7): 4 frame boxes + 2 cross beams, grating, flange, rails on +-X
    h = GD_TOP_W / 2
    zc = zt + 0.10
    P.box("trans_0", (GD_TOP_W + 0.24, 0.24, 0.25), (0, h, zc))
    P.box("trans_1", (GD_TOP_W + 0.24, 0.24, 0.25), (0, -h, zc))
    # detail pass 1 (design 1.4): the y ends of the cross boxes sit flush on trans_0 / trans_1's
    # inner faces (hidden contact): not built
    P.box("trans_2", (0.24, GD_TOP_W - 0.24, 0.25), (h, 0, zc), drop=("+Y", "-Y"))
    P.box("trans_3", (0.24, GD_TOP_W - 0.24, 0.25), (-h, 0, zc), drop=("+Y", "-Y"))
    P.box("trans_4", (0.20, GD_TOP_W - 0.24, 0.20), (0.35, 0, zt + 0.125), drop=("+Y", "-Y"))
    P.box("trans_5", (0.20, GD_TOP_W - 0.24, 0.20), (-0.35, 0, zt + 0.125), drop=("+Y", "-Y"))
    zg = zt + 0.25
    # Revision 1: 2.6 x 2.0 (was 2.6 square) - the -Y edge sits on trans_1 and the ladder system's
    # last flight (y -(0.9 + 0.25)) arrives beside it; y-mirror symmetric
    gx, gy, rxx = GD_TRANS["gx"], GD_TRANS["gy"], GD_TRANS["rail_x"]
    P.kit(cs2_kit.grating("trans", gx, gy, t=0.03, loc=(0, 0, zg)), "galv", aabb=True)
    zf = zg + 0.015 + 0.015
    # detail pass 1 (design 1.4): the flange's bottom disc lies on the grating top: not built
    flg = cs2_kit.flange("trans", GD["mast_r"] + 0.15, t=0.03, bolts=8, segs=16, loc=(0, 0, zf), caps="top")
    _metric_post_uv(flg[0][0])
    P.kit(flg, "paint")
    rail_kw = dict(h=GD_DECK["rail_h"], knee=GD_DECK["rail_knee"], kick=0.10, post_every=1.5, segs=4,
                   radius=0.025)
    for nm, xx in (("transA", rxx), ("transB", -rxx)):
        # (the ladder system arrives at the platform's open -Y edge: the +-X rails stay whole)
        rail = cs2_kit.handrail(nm, [(xx, -gy / 2, zg + 0.015), (xx, gy / 2, zg + 0.015)], **rail_kw)
        _dp1_handrail_fix(rail, kick=0.10, lower_travel_kicks=False)
        P.kit(rail, "galv")
    build_gd_top(P, spec, lay, zf + 0.015)
    build_footings(P, spec, PED_H_GD, PW_GD, PIER_GD, BPLATE_GD, W_GD["leg"])
    return P, lay


def build_gd_top(P, spec, lay, z_tube0):
    """Tube neck + GD head kit (this module's GD head: sheave trains at +-3.0, axle = A)."""
    A = spec.z0
    tube_top = A + GD["tube_top_dA"]
    P.add(prism("mast_0", [(0, 0, z_tube0), (0, 0, tube_top)], GD["mast_r"], GD["mast_segs"], XV,
                caps="end"), "paint", axis=[(0, 0, z_tube0), (0, 0, tube_top)],
          w=GD["mast_r"] * math.sqrt(2.0))   # square-equivalent: half-diagonal = tube radius (C7)
    # mast ladder on the -X face (prefixes rung_ / lrail_ so LOD1 drops it)
    build_ladder(P, [(z_tube0, A + 0.70)], (-(GD["mast_r"] + 0.14),), tag="m")
    # detail pass 1: its fall-arrest rail (rungs along y: flat at -r/2 outward, as the tram's)
    xm = -(GD["mast_r"] + 0.14)
    back = -LADDER["rung_r"] * 0.5 + ARREST["into"]
    pts = [(xm, 0.0, z_tube0 + LADDER["first"] - 0.05), (xm, 0.0, A + 0.70 - ARREST["end_below_top"])]
    P.add(_channel("arrest_m", pts, "-X", back, ARREST["w"], ARREST["d"]), "galv",
          axis=[(n_ + back - ARREST["d"] / 2.0, v_, z_) for n_, v_, z_ in pts], w=ARREST["w"])
    # cross girders through the tube head (x +-0.22), deck below them (inboard of the cabins)
    gz0, gz1 = A - 0.09, A + 0.42
    for k, x in enumerate((0.22, -0.22)):
        # girder ends buried in the train carriers (y 2.76 = rockmain center: no coplanar end faces)
        P.box(f"gdgirder_{k}", (0.18, 2 * 2.76, gz1 - gz0), (x, 0, (gz0 + gz1) / 2))
    Ld = lay["deck_l"]
    y0, y1, ztop = GD_DECK["y0"], GD_DECK["y1"], A + GD_DECK["top_dA"]
    for s, tag in ((1, "p"), (-1, "n")):
        yc = s * (y0 + y1) / 2
        P.kit(cs2_kit.grating(f"gdeck{tag}", Ld, y1 - y0, t=0.03, loc=(0, yc, ztop - 0.015)), "galv",
              aabb=True)
        zs = ztop - 0.03 - 0.06
        for kk, ys in enumerate((0.70, 1.62)):
            # detail pass 1 (design 1.4): the long stringers' tops lie on the grating bottom (hidden
            # contact): not built
            P.member(f"gdstring_{tag}_{kk}", [(-Ld / 2, s * ys, zs), (Ld / 2, s * ys, zs)], 0.12,
                     hint=ZV, caps="both", drop=(ZV,))
        for kk, x in enumerate((Ld / 2, -Ld / 2)):
            # detail pass 1 (DT10): the end stringers 1 cm narrower, raised 5 mm so their top stays on
            # the grating bottom: their bottoms leave the long stringers' plane (2 x 288 cm2)
            ins = DP1["inset"]
            P.member(f"gdstring_{tag}_{2 + kk}", [(x, s * 0.70, zs + ins / 2), (x, s * 1.62, zs + ins / 2)],
                     0.12 - ins, hint=ZV)
        rail = cs2_kit.handrail(f"gd{tag}", [(-Ld / 2, s * 1.75, ztop), (Ld / 2, s * 1.75, ztop)],
                                h=GD_DECK["rail_h"], knee=GD_DECK["rail_knee"], kick=0.10,
                                post_every=1.5, segs=4, radius=0.025)
        _dp1_handrail_fix(rail, kick=0.10, lower_travel_kicks=False)
        P.kit(rail, "galv")
        # gantry: cap on the girders, post, braces to the outer stringer ends
        P.box(f"gdcap_{tag}", (0.62, 0.30, 0.06), (0, s * 2.55, gz1 + 0.03))
        P.member(f"gdpost_{tag}", [(0, s * 2.55, gz1 + 0.06), (0, s * 2.55, A + 1.76)], 0.24, hint=XV)
        for kk, x in enumerate((Ld / 2 - 0.05, -(Ld / 2 - 0.05))):
            # detail pass 1 (DT10): the brace pair meets in a MITRE on the x = 0 plane under the
            # beam (it overlapped at the apex: 2 x 5.3 cm2); the cut's top corners stay buried in
            # gdbeam_0 (A + 1.76 .. 2.00, x +-0.12)
            P.member(f"gdbrace_{tag}_{kk}", [(0, s * 1.62, A + 1.76), (x, s * 1.62, zs + 0.06)], 0.12,
                     hint=YV, end_planes=(XV, ZV))
        # sheave train: N sheaves on the lane (centers at A), inboard carriers
        ly = s * GD["lane_y"]
        half = lay["train_half"] + 0.02
        # detail pass 1 (design 1.4): rockmain 1 cm deeper (A - 0.11 .. A + 0.20) so each sub-rocker's
        # inboard face lies wholly on it; that face is then not built (hidden contact)
        dr = DP1["rockmain_drop"]
        P.box(f"rockmain_{tag}", (2 * half, 0.08, 0.30 + dr), (0, s * (GD["lane_y"] - 0.24), A + 0.05 - dr / 2))
        xs = lay["sheave_x"]
        for j in range(len(xs) // 2):
            xc = (xs[2 * j] + xs[2 * j + 1]) / 2
            P.box(f"rocksub_{tag}_{j}", (SHEAVE["pitch"] + 0.30, 0.08, 0.22),
                  (xc, s * (GD["lane_y"] - 0.16), A), drop=("-Y",) if s > 0 else ("+Y",))
        for kk, x in enumerate(xs):
            # detail pass 1: the flat rim band (the rope face) split off as a rubber liner
            sh, ln = _gd_wheel(f"sheave_{tag}_{kk}", f"sheave_liner_{tag}_{kk}", (x, ly, A))
            P.add(sh, cs2_sheaves.wheel_key("galv"))    # T62 round 2: the one wheel material under v2
            P.add(ln, "rubber")
        # V8c lesson (JH-15): the gantry-beam tip over each lane carries a lifting lug (rope /
        # sheave-train lifts); dz +1.46..+1.76 is far above the GD head rule band (dz <= 0.20)
        P.box(f"lug_{tag}", (0.16, 0.16, 0.30), (0, ly, A + 1.76 - 0.15), drop=("+Z",))
    P.member("gdbeam_0", [(0, -GD_BEAM_Y, A + 1.88), (0, GD_BEAM_Y, A + 1.88)], 0.24, hint=ZV, caps="both")
    build_plates(P, spec)                    # detail pass 1: tower plates on the beam's travel faces


def build_parts(spec):
    r = build_tram_tower(spec) if spec.family == "tram" else build_gondola_lattice(spec)
    # T62 round 3: drop the hub bosses the built tower hides (measured, cs2_sheaves.cull_hidden_bosses; v1 no-op)
    cs2_sheaves.cull_hidden_bosses((r[0] if isinstance(r, tuple) else r).items, label=f"{spec.family} {spec.H}")
    return r


# --------------------------------------------------------------------------------------
# Assembly
# --------------------------------------------------------------------------------------

def clear_scene():
    if not bpy.app.background:
        raise RuntimeError("tram_tower.py runs headless only (ROUND-SPEC R10)")
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.cameras,
                 bpy.data.lights, bpy.data.worlds):
        for item in list(coll):
            if item.users == 0:
                coll.remove(item)


def prefix_of(name):
    best = None
    for pre, mat, dw in PREFIX_TABLE:
        if name.startswith(pre) and (best is None or len(pre) > len(best[0])):
            best = (pre, mat, dw)
    return best


def lod_lists(family):
    """(LOD1 delete prefixes, LOD2 delete prefixes): deletion only, no decimation (D10)."""
    return (TRAM_LOD1, TRAM_LOD2) if family == "tram" else (GDL_LOD1, GDL_LOD2)


def lod_fate(name, family):
    l1, l2 = lod_lists(family)
    if name.startswith(l1):
        return 1
    if name.startswith(l2):
        return 2
    return 0


def check_c10(parts, family):
    """C10: one UV layer 'UVMap', a known prefix with the declared material, one LOD fate."""
    l1, l2 = lod_lists(family)
    extra2 = tuple(p for p in l2 if p not in l1)
    bad = []
    for obj, mat in parts.items:
        layers = [l.name for l in obj.data.uv_layers]
        if layers != ["UVMap"]:
            bad.append(f"{obj.name}: UV layers {layers}")
        pre = prefix_of(obj.name)
        if pre is None:
            bad.append(f"{obj.name}: no PREFIX_TABLE entry")
        elif (cs2_sheaves.wheel_key(pre[1]) if ((obj.get("cs2_wheel") or obj.get("cs2_wheel_hub")) and pre[1] == "galv")
              else pre[1]) != mat:
            # T62 round 2: a stamped wheel's discs take cs2_sheaves.WHEEL_KEY (the table's galv under v1)
            bad.append(f"{obj.name}: material {mat} != table {pre[1]}")
        if obj.name.startswith(l1) and obj.name.startswith(extra2):
            bad.append(f"{obj.name}: two LOD fates")
    if bad:
        raise ContractError("C10: " + "; ".join(bad[:10]))


def plate_weight(family, H):
    return round(PLATE[family]["target_ppm"] / PLATE_BASE_PPM[(family, int(H))], 2)


def density_weights(family, H):
    """PREFIX_TABLE density weights; detail pass 1: the tower plates' per-variant weight (DT-C7)."""
    w = {p: dw for p, _m, dw in PREFIX_TABLE}
    w["plate_"] = plate_weight(family, H)          # the rims stay 1.0: 8 mm strips of plain plate
    return w





def _clear_member_sharp(obj, table, axes, family, net=False):
    """M6: clear `sharp_edge` on the LONGITUDINAL edges (parallel to the member axis within 25 deg)
    between two faces of one SMOOTH_MEMBERS part; caps / mitre / end edges stay sharp. `axes` is
    Parts.axes (authoring frame); net=True maps them to the net frame (x, y, z) -> (y, -x, z) for
    a mesh already oriented by orient_for_net. Returns the number of edges smoothed."""
    pre = SMOOTH_MEMBERS[family]
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    lay = bm.faces.layers.int.get("part")
    seg_of = {}
    for nm, (pts, _w) in axes.items():
        if nm.startswith(pre):
            P_ = [Vector((p[1], -p[0], p[2])) if net else Vector(p) for p in pts]
            seg_of[nm] = [(a, b, (b - a).normalized()) for a, b in zip(P_, P_[1:])]
    n = 0
    for e in bm.edges:
        if e.smooth or len(e.link_faces) != 2:
            continue
        f0, f1 = e.link_faces
        if f0[lay] != f1[lay]:
            continue
        segs = seg_of.get(table[f0[lay] - 1])
        if not segs:
            continue
        a_, b_ = e.verts[0].co, e.verts[1].co
        d = (b_ - a_).normalized()
        mid = (a_ + b_) / 2.0
        best = None
        for a, b, t in segs:
            ab = b - a
            s = max(0.0, min(1.0, (mid - a).dot(ab) / max(ab.length_squared, 1e-12)))
            dist = (a + ab * s - mid).length
            if best is None or dist < best[0]:
                best = (dist, t)
        if abs(d.dot(best[1])) > math.cos(math.radians(25.0)):
            e.smooth = True
            n += 1
    bm.to_mesh(me)
    bm.free()
    me.update()
    return n


def shade_lattice(obj, table, axes, family, weighted=True, net=False):
    """Generator-local replacement of cs2_asset.shade_by_angle (M6): smooth by angle (66), then the
    smooth lattice members' longitudinal edges cleared, THEN the face-area weighted normals (custom
    normals are computed on the final sharp set; clearing after them changes nothing). Run on LOD0
    and again on every LOD make_lod_chain returns (it re-marks every 90 deg edge sharp)."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    if obj.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(SHADE_ANGLE), keep_sharp_edges=True)
    n = _clear_member_sharp(obj, table, axes, family, net=net)
    if weighted:
        mod = obj.modifiers.new("WeightedNormal", type="WEIGHTED_NORMAL")
        mod.mode = "FACE_AREA"
        mod.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    me = obj.data
    attr = me.attributes.get("sharp_edge")
    n_sharp = sum(1 for v in attr.data if v.value) if attr is not None else 0
    print(f"[shade] {obj.name}: {len(me.polygons)} faces smooth, {n_sharp} sharp edges; {n} member "
          f"edges smoothed (round-tube read){', weighted normals' if weighted else ''}")
    return n


def assemble(parts, family, finish=False, beacon=False, texture_root=None, H=None):
    check_c10(parts, family)
    cs2_uv.set_density(parts.items, density_weights(family, H or HEIGHTS[family][-1]))
    table = cs2_asset.tag_parts(parts.items)
    sources = None
    if finish:
        import cs2_bake
        sources = cs2_bake.material_sources(material_specs(beacon, texture_root, family), parts.items)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _m in parts.items:
        o.select_set(True)
    first = parts.items[0][0]
    bpy.context.view_layer.objects.active = first
    bpy.ops.object.join()
    t = bpy.context.view_layer.objects.active
    bm = bmesh.new()
    bm.from_mesh(t.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=WELD_M)
    bm.to_mesh(t.data)
    bm.free()
    t.data.update()
    cs2_asset.store_part_table(t, table)
    shade_lattice(t, table, parts.axes, family, weighted=True)
    return t, table, sources


def make_lods(t, name, table, parts, family, n1, n2):
    """cs2_asset.make_lod_chain (deletion only: targets = the post-deletion counts) + M6: every LOD
    re-shaded generator-locally so the smooth members stay smooth (the chain re-marks them sharp)."""
    l1p, l2p = lod_lists(family)
    lods = cs2_asset.make_lod_chain(t, name, targets=(n1, n2), delete_prefixes=(l1p, l2p), tol=0.02,
                                    shade_angle=SHADE_ANGLE, fixed=cs2_sheaves.lod_fixed())   # T62 round 2: WHEEL_LOD
    for lod in lods:
        lod["dp1_smoothed"] = shade_lattice(lod, table, parts.axes, family, weighted=True, net=True)
    return lods


# --------------------------------------------------------------------------------------
# Mesh view for the contracts
# --------------------------------------------------------------------------------------

class MeshView:
    """Vertices (authoring frame), per-part vertex sets and per-face part ids of the joined mesh."""

    def __init__(self, obj, table, net=False):
        me = obj.data
        n = len(me.vertices)
        co = np.empty(n * 3, dtype=np.float64)
        me.vertices.foreach_get("co", co)
        V = co.reshape(n, 3)
        if net:     # net (X, Y, Z) = (y, -x, z)  ->  authoring (x, y, z) = (-Y, X, Z)
            V = np.stack([-V[:, 1], V[:, 0], V[:, 2]], axis=1)
        self.V = V
        self.table = table
        npoly = len(me.polygons)
        part = np.zeros(npoly, dtype=np.int64)
        me.attributes["part"].data.foreach_get("value", part)
        start = np.empty(npoly, dtype=np.int64)
        total = np.empty(npoly, dtype=np.int64)
        me.polygons.foreach_get("loop_start", start)
        me.polygons.foreach_get("loop_total", total)
        lv = np.empty(len(me.loops), dtype=np.int64)
        me.loops.foreach_get("vertex_index", lv)
        self.face_part = part
        self.face_start, self.face_total, self.loop_vert = start, total, lv
        loop_part = np.repeat(part, total)
        order = np.concatenate([np.arange(s, s + c) for s, c in zip(start, total)])
        pairs = np.unique(np.stack([lv[order], loop_part], axis=1), axis=0)
        self.pairs = pairs
        self.part_verts = {}
        for pid in np.unique(pairs[:, 1]):
            self.part_verts[int(pid)] = pairs[pairs[:, 1] == pid, 0]
        self.name_to_pid = {nm: i + 1 for i, nm in enumerate(table)}
        self.tris_per_face = total - 2

    def verts(self, name):
        return self.V[self.part_verts[self.name_to_pid[name]]]

    def names(self, prefix):
        return [nm for nm in self.table if nm.startswith(prefix)]

    def faces_of(self, name):
        pid = self.name_to_pid[name]
        return np.nonzero(self.face_part == pid)[0]

    def face_coords(self, fi):
        s, c = self.face_start[fi], self.face_total[fi]
        return self.V[self.loop_vert[s:s + c]]

    def tally(self):
        out = {}
        for pid in range(1, len(self.table) + 1):
            nm = self.table[pid - 1]
            pre = prefix_of(nm)[0]
            out[pre] = out.get(pre, 0) + int(self.tris_per_face[self.face_part == pid].sum())
        return dict(sorted(out.items()))


def _fail(code, msg):
    raise ContractError(f"{code}: {msg}")


def _close(a, b, tol, code, what):
    if abs(a - b) > tol:
        _fail(code, f"{what} = {a:.5f}, expected {b:.5f} (+-{tol})")


# --------------------------------------------------------------------------------------
# Contracts (C1-C8, C11, footings) on one frame
# --------------------------------------------------------------------------------------

def check_contracts(t, table, spec, parts, frame="authoring"):
    """Run every geometry contract; returns a report dict, raises ContractError on any failure."""
    mv = MeshView(t, table, net=(frame == "net"))
    V = mv.V
    rep = {"frame": frame}
    H = float(spec.H)
    pier = PIER_TR if spec.family == "tram" else PIER_GD
    ped_h = PED_H_TR if spec.family == "tram" else PED_H_GD
    pw = PW_TR if spec.family == "tram" else PW_GD
    h_top = TRAM["h_top"] if spec.family == "tram" else GD["h_top"]
    # C1 / C2 / C5 bounds
    _close(V[:, 2].max(), H, 1e-3, "C1", "bounds top")
    _close(V[:, 2].min(), -pier, 1e-4, "C2", "bounds bottom")
    _close(H - spec.z0, h_top, 1e-9, "C5", "H_top")
    top_part = "gantrybeam_" if spec.family == "tram" else "gdbeam_"
    tops = max(mv.verts(nm)[:, 2].max() for nm in mv.names(top_part))
    _close(tops, H, 1e-3, "C5", f"{top_part} top")
    rep["bounds"] = {"min": V.min(axis=0).round(4).tolist(), "max": V.max(axis=0).round(4).tolist()}
    # family heads
    if spec.family == "tram":
        rep.update(_check_tram_head(mv, spec))
    else:
        rep.update(_check_gd_head(mv, spec))
    # C6 leg feet on the plates
    for i, c in enumerate(CORNERS):
        lv = mv.verts(f"leg_{i}")
        foot = lv[np.abs(lv[:, 2] - (ped_h + 0.05)) < 1e-4]
        if len(foot) != 4:
            _fail("C6", f"leg_{i}: {len(foot)} foot vertices at z {ped_h + 0.05:.2f} (want 4)")
        fx, fy = c[0] * spec.B / 2, c[1] * spec.B / 2
        if np.abs(foot[:, 0] - fx).max() > pw / 2 or np.abs(foot[:, 1] - fy).max() > pw / 2:
            _fail("C6", f"leg_{i} foot outside its pedestal")
        if lv[:, 2].min() < ped_h + 0.05 - 1e-4:
            _fail("C6", f"leg_{i} dips below the plate top")
    # footings: open bottom at z 0 (task), open pier bottom, pedestal top present
    rep["footings"] = _check_footings(mv, spec, ped_h, pier)
    # C7 ladder / landings
    rep["ladder"] = _check_ladder(mv, spec, parts)
    # C8 static zones + rope corridors
    rep["zones"] = check_zones(mv, spec, parts)
    # C11 symmetry
    rep["symmetry"] = _check_symmetry(V, mv)
    return rep, mv


def _check_tram_head(mv, spec):
    z0, s0 = spec.z0, spec.s0
    out = {"z_rope": round(z0, 4), "haul_z": round(z0 + TRAM["haul_dz"], 4)}
    prof = saddle_profile(spec)
    xc, L2 = SADDLE["xc"], spec.saddle_l / 2
    sad = {}
    for tag, s in (("p", 1), ("n", -1)):
        sv = mv.verts(f"saddle_{tag}")
        yc = (sv[:, 1].min() + sv[:, 1].max()) / 2
        _close(yc, s * TRAM["lane_y"], 1e-3, "C3", f"saddle_{tag} lane y")
        _close(sv[:, 1].max() - sv[:, 1].min(), SADDLE["w"], 1e-3, "C3", f"saddle_{tag} width")
        xs = np.round(sv[:, 0], 4)
        top = {}
        for x in np.unique(xs):
            top[float(x)] = float(sv[xs == x, 2].max())
        crown = top[0.0]
        _close(crown, z0 - TRAM["rope_r_hv"], 1e-4, "C3", f"saddle_{tag} crown top")
        worst_bury, worst_gap_contact = 0.0, 0.0
        for x, z in top.items():
            rope_under = z0 - TRAM["rope_r_hv"] - s0 * abs(x)
            worst_bury = max(worst_bury, z - rope_under)
            if abs(x) <= xc + 1e-6:
                worst_gap_contact = max(worst_gap_contact, rope_under - z)
        if worst_bury > 1e-4:
            _fail("C3", f"saddle_{tag} buries the level rope by {worst_bury:.4f} m")
        if worst_gap_contact > 1e-3:
            _fail("C3", f"saddle_{tag} contact zone gap {worst_gap_contact:.4f} m")
        tip_z = z0 - TRAM["rope_r_hv"] - s0 * L2 - SADDLE["tip"]
        _close(top[round(L2, 4)], tip_z, 1e-3, "C3", f"saddle_{tag} tip")
        _close(top[round(-L2, 4)], tip_z, 1e-3, "C3", f"saddle_{tag} tip")
        sad[tag] = {"crown": round(crown, 4), "tip": round(tip_z, 4), "lane_y": round(yc, 4)}
        for kk, x in enumerate(ROLLER["x"]):
            rv = mv.verts(f"haulroll_{tag}_{kk}")
            want = z0 + TRAM["haul_dz"] - TRAM["rope_r_lv"] - s0 * abs(x)
            _close(rv[:, 2].max(), want, 5e-3, "C4", f"haulroll_{tag}_{kk} top")
            _close((rv[:, 1].min() + rv[:, 1].max()) / 2, s * TRAM["haul_y"], 1e-3, "C4",
                   f"haulroll_{tag}_{kk} y")
    out["saddles"] = sad
    out["lanes"] = {"hv_y": TRAM["lane_y"], "lv_haul_y": TRAM["haul_y"], "lv_haul_dz": TRAM["haul_dz"]}
    return out


def _check_gd_head(mv, spec):
    A = spec.z0
    out = {"axle": round(A, 4), "comm_z": round(A + GD["comm_dA"], 4)}
    for tag, s in (("p", 1), ("n", -1)):
        names = mv.names(f"sheave_{tag}_")
        if len(names) != spec.n_sheaves:
            _fail("C3", f"side {tag}: {len(names)} sheaves, want {spec.n_sheaves}")
        for nm in names:
            v = mv.verts(nm)
            c = (v.min(axis=0) + v.max(axis=0)) / 2
            _close(c[2], A, 1e-3, "C3", f"{nm} axle z")
            _close(c[1], s * GD["lane_y"], 1e-3, "C3", f"{nm} lane y")
    out["sheaves_per_side"] = spec.n_sheaves
    out["lanes"] = {"hv_y": GD["lane_y"], "comm_dA": GD["comm_dA"]}
    return out


def _check_footings(mv, spec, ped_h, pier):
    res = {}
    for i in range(4):
        pv = mv.verts(f"ped_{i}")
        _close(pv[:, 2].min(), 0.0, 1e-6, "FOOT", f"ped_{i} bottom edge z")
        _close(pv[:, 2].max(), ped_h, 1e-6, "FOOT", f"ped_{i} top")
        for fi in mv.faces_of(f"ped_{i}"):
            fc = mv.face_coords(fi)
            if np.abs(fc[:, 2]).max() < 1e-5:
                _fail("FOOT", f"ped_{i} has a bottom face at z 0 (must be open)")
        has_top = any(np.abs(mv.face_coords(fi)[:, 2] - ped_h).max() < 1e-5 for fi in mv.faces_of(f"ped_{i}"))
        if not has_top:
            _fail("FOOT", f"ped_{i} has no top face")
        qv = mv.verts(f"pier_{i}")
        _close(qv[:, 2].min(), -pier, 1e-5, "FOOT", f"pier_{i} bottom")
        for fi in mv.faces_of(f"pier_{i}"):
            fc = mv.face_coords(fi)
            if np.abs(fc[:, 2] + pier).max() < 1e-5 or np.abs(fc[:, 2]).max() < 1e-5:
                _fail("FOOT", f"pier_{i} has a horizontal end face")
        ground = pv[np.abs(pv[:, 2]) < 1e-6]
        if len(ground) != 4:
            _fail("FOOT", f"ped_{i}: {len(ground)} vertices at z 0 (want the 4 side-edge ends)")
        res[f"ped_{i}"] = "open bottom at z 0"
    return res


def _seg_seg_dist(p1, q1, p2, q2):
    d1, d2, r = q1 - p1, q2 - p2, p1 - p2
    a, e, f = d1.dot(d1), d2.dot(d2), d2.dot(r)
    if a < 1e-12 and e < 1e-12:
        return (p1 - p2).length
    if a < 1e-12:
        s, t = 0.0, min(max(f / e, 0.0), 1.0)
    else:
        c = d1.dot(r)
        if e < 1e-12:
            t, s = 0.0, min(max(-c / a, 0.0), 1.0)
        else:
            b = d1.dot(d2)
            den = a * e - b * b
            s = min(max((b * f - c * e) / den, 0.0), 1.0) if den > 1e-12 else 0.0
            t = (b * s + f) / e
            if t < 0.0:
                t, s = 0.0, min(max(-c / a, 0.0), 1.0)
            elif t > 1.0:
                t, s = 1.0, min(max((b - c) / a, 0.0), 1.0)
    return ((p1 + d1 * s) - (p2 + d2 * t)).length


def pier_cover(spec):
    """(slope allowance m, corner diagonal m) of the family's piers: the numbers prefab_block
    reports as pier.slope_allowance_m / corner_diag_m (grade covered = allowance / diagonal)."""
    if spec.family == "tram":
        sizes, spec_y, pier = [float(h) for h in HEIGHTS["tram"]], float(HEIGHTS["tram"][0]), PIER_TR
    else:
        tub = list(GD["tubular_tops"])
        sizes, spec_y, pier = tub + [float(h) for h in HEIGHTS["gondola"]], tub[0], PIER_GD
    return pier - scoring_sweep(sizes, spec_y)["max_float"], (spec.B / 2) * math.sqrt(2.0)


def _check_ladder(mv, spec, parts):
    """
    C7 (Revision 1, verdicts 7 / 8). The ladder system (parts.ladder = ladder_plan) must:
      reach   - flight 0's rails run from z <= 0 down to where the terrain can be (revision 1c,
                revision1b-check-tram N1 / N2): the variant's max float (variant_float) + the drop
                under the foot at the piers' grade allowance in the WORST slope direction
                (allowance x foot L-inf radius / (B/2) - the face normal, the foot being outside the
                leg square), capped at the pier bound (the frozen bounds y floor; the shortfall
                beyond it is reported); on a variant that floats, flight 0's rungs run down to
                within 0.25 + one pitch of the rail foot (no bare rails on the exposed piers); the
                last flight's rails rise >= 1.0 above the arrival floor (tram head deck / gondola
                transition platform);
      chain   - every later flight starts on the floor of the basket that ends the flight below,
                each basket's grating top IS that floor, flights <= LADDER flight_max;
      offset  - consecutive flights sit on lines >= min_offset apart (never one above the other);
      corridor- the climbing space of every flight (|y - line| < corridor_half, from corridor_in to
                corridor_out outside the rung plane, lowest rung .. f1) holds no vertex / member-axis
                sample / AABB of any other part: no landing or basket covers a flight;
      rails   - no ladder rail axis comes closer to another member axis than their half widths.
    The gondola mast ladder (lrail_m / rung_m) is a single straight run on the tube.
    """
    rails = mv.names("lrail_")
    rungs = mv.names("rung_")
    if not rails:
        return {"rails": 0}
    rep = {"rails": len(rails), "rungs": len(rungs)}
    plan = parts.ladder
    if plan:
        fl, bks = plan["flights"], plan["baskets"]
        r0 = np.concatenate([mv.verts(f"lrail_0_{t}") for t in "pn"])
        # revisions 1b / 1c (revision1-check-tram-towers F1; revision1b-check-tram N1 / N2): rails
        # from z <= 0, deep enough that the foot is below the terrain on a floated base (variant
        # float) on a slope at the piers' grade allowance in the worst direction; inside the pier
        # bound. The drop ratio maximized over slope directions is the foot's L-inf radius / (B/2)
        # (dual of the corners' L1 reach), i.e. the face normal for a foot outside the leg square.
        z_foot = float(r0[:, 2].min())
        allowance, _diag = pier_cover(spec)
        foot = r0[r0[:, 2] <= z_foot + 1e-6]
        grade = allowance / (spec.B / 2.0)
        drop = grade * float(np.abs(foot[:, :2]).max())
        fl_v = variant_float(spec)
        pier = PIER_TR if spec.family == "tram" else PIER_GD
        need = fl_v + drop
        if z_foot > 1e-6:
            _fail("C7", f"flight 0 starts at z {z_foot:.3f}, above the ground (verdict 7)")
        if z_foot > -min(need, pier) + 1e-6:
            _fail("C7", f"flight 0 rails end at z {z_foot:.3f}: with the base floated {fl_v:.2f} m and the "
                        f"piers' grade allowance ({grade:.3f}) the foot would hang {need + z_foot:.2f} m "
                        f"above the terrain (need z <= {-min(need, pier):.3f})")
        if z_foot < -pier - 1e-6:
            _fail("C7", f"flight 0 rails end at z {z_foot:.3f}, below the pier bound {-pier}")
        r0_rungs = [float(mv.verts(n_)[:, 2].min()) for n_ in rungs if n_.startswith("rung_0_")]
        rung_lo = min(r0_rungs) if r0_rungs else 9e9
        if fl_v > 0.0 and rung_lo > z_foot + LADDER["first"] + LADDER["pitch"] + 1e-6:
            _fail("C7", f"the base floats up to {fl_v:.2f} m but flight 0's rungs start at z {rung_lo:.3f}: "
                        f"bare rails from {z_foot:.3f} (revision1b-check-tram N1)")
        rep.update({"rail_foot_z": round(z_foot, 3), "rung_foot_z": round(rung_lo, 3),
                    "variant_float_m": fl_v, "foot_drop_at_pier_grade_m": round(drop, 3),
                    "foot_need_m": round(need, 3), "foot_short_at_pier_bound_m": round(max(0.0, need - pier), 3)})
        z_arr = ladder_arrival(spec)        # from the spec, not the plan: a short plan cannot pass
        rl = np.concatenate([mv.verts(f"lrail_{fl[-1]['j']}_{t}") for t in "pn"])
        if float(rl[:, 2].max()) < z_arr + 1.0 - 1e-6:
            _fail("C7", f"last flight tops out at {rl[:, 2].max():.3f} < arrival {z_arr:.3f} + 1.0 (verdict 7)")
        if abs(fl[-1]["f1"] - z_arr) > 1e-6:
            _fail("C7", f"last flight lands at {fl[-1]['f1']:.3f}, not on the arrival floor {z_arr:.3f}")
        if len(bks) != len(fl) - 1:
            _fail("C7", f"{len(fl)} flights need {len(fl) - 1} baskets, got {len(bks)}")
        for a, b in zip(fl, fl[1:]):
            if abs(a["f1"] - b["f0"]) > 1e-6:
                _fail("C7", f"flight {b['j']} starts at {b['f0']:.3f}, not on basket floor {a['f1']:.3f}")
            if abs(a["line"] - b["line"]) < LADDER["min_offset"] - 1e-9:
                _fail("C7", f"flights {a['j']} / {b['j']} not offset ({abs(a['line'] - b['line']):.2f} m "
                            f"< {LADDER['min_offset']}): the upper flight stands over the lower (verdict 8)")
        lengths = [f["f1"] - f["f0"] for f in fl]
        if max(lengths) > LADDER["flight_max"] + 1e-6:
            _fail("C7", f"flight of {max(lengths):.2f} m > {LADDER['flight_max']}")
        for bk in bks:
            gv = mv.verts(f"grate_bskt{bk['j']}")
            _close(float(gv[:, 2].max()), bk["floor"], 1e-4, "C7", f"basket {bk['j']} floor")
        rep.update({"flights": len(fl), "baskets": len(bks), "lengths_m": [round(x, 3) for x in lengths],
                    "lines_y": [f["line"] for f in fl], "arrival_z": round(plan["arrival"], 3)})
        rep["corridor_margin_m"] = _ladder_corridors(mv, parts, plan)
    # rails vs every other member axis, segment by segment (rungs attach to rails and are excluded)
    min_d = 9.0
    others = [(nm, ax) for nm, ax in parts.axes.items() if not nm.startswith(("lrail_", "rung_"))]
    for nm in rails:
        pts, w = parts.axes[nm]
        for a, b in zip(pts, pts[1:]):
            a, b = Vector(a), Vector(b)
            for on, (opts, ow) in others:
                for p, q in zip(opts, opts[1:]):
                    d = _seg_seg_dist(a, b, Vector(p), Vector(q))
                    need = (w + ow) / math.sqrt(2.0)
                    min_d = min(min_d, d - need)
                    if d < need:
                        _fail("C7", f"ladder rail {nm} intersects {on} (axis distance {d:.3f} < {need:.3f})")
    rep["rail_member_clearance_m"] = round(min_d, 3)
    return rep


def _ladder_corridors(mv, parts, plan):
    """C7 corridor test (see _check_ladder), in ladder-local (n, v, z); returns the smallest lateral
    margin found (m) between a foreign vertex in a flight's n / z band and its corridor."""
    L = LADDER
    face = plan["face"]
    V = mv.V
    Vn, Vv = face_nv(face, V)
    pid_name = {i + 1: n for i, n in enumerate(mv.table)}
    vert_parts = {}
    for v, pid in mv.pairs:
        vert_parts.setdefault(int(v), []).append(pid_name[int(pid)])
    # member-axis samples (5 cm) and AABBs of everything that is not a registered member
    samp_p, samp_n = [], []
    for nm, (axis, _w) in parts.axes.items():
        for p, q in zip(axis, axis[1:]):
            p, q = np.array(p, float), np.array(q, float)
            n = max(2, int(math.ceil(np.linalg.norm(q - p) / 0.05)) + 1)
            for t in np.linspace(0.0, 1.0, n):
                samp_p.append(p + (q - p) * t)
                samp_n.append(nm)
    samp_p = np.array(samp_p)
    samp_n = np.array(samp_n, dtype=object)
    Sn, Sv = face_nv(face, samp_p)
    boxes = []
    for nm in mv.table:
        if nm in parts.axes:
            continue
        v = mv.verts(nm)
        bn, bv = face_nv(face, v)
        boxes.append((nm, bn.min(), bn.max(), bv.min(), bv.max(), v[:, 2].min(), v[:, 2].max()))
    margin = 9.0
    for f in plan["flights"]:
        j, line = f["j"], f["line"]
        own = {f"lrail_{j}_p", f"lrail_{j}_n"}
        own_rung = f"rung_{j}_"
        ns, zs = [p[0] for p in f["pts"]], [p[1] for p in f["pts"]]
        z_lo, z_hi = f["f0"] + L["first"] - f["rungs_below"] * L["pitch"], f["f1"]     # 1c: below grade too

        def band(Pn, Pz):
            u = Pn - np.interp(Pz, zs, ns)
            return (Pz > z_lo) & (Pz < z_hi) & (u < -L["corridor_in"]) & (u > -L["corridor_out"])
        hit = set()
        in_v = band(Vn, V[:, 2])
        for vi in np.nonzero(in_v & (np.abs(Vv - line) < L["corridor_half"]))[0]:
            for nm in vert_parts.get(int(vi), ()):
                if nm not in own and not nm.startswith(own_rung):
                    hit.add(nm)
        for si in np.nonzero(band(Sn, samp_p[:, 2]) & (np.abs(Sv - line) < L["corridor_half"]))[0]:
            nm = samp_n[si]
            if nm not in own and not nm.startswith(own_rung):
                hit.add(nm)
        n_lo, n_hi = min(ns) - L["corridor_out"], max(ns) - L["corridor_in"]
        for nm, bn0, bn1, bv0, bv1, bz0, bz1 in boxes:
            if (bz0 < z_hi and bz1 > z_lo and bv0 < line + L["corridor_half"] and bv1 > line - L["corridor_half"]
                    and bn0 < n_hi and bn1 > n_lo):
                hit.add(nm)
        if hit:
            _fail("C7", f"flight {j} climbing corridor blocked by {sorted(hit)[:6]} (verdict 8: a landing "
                        f"or basket must sit beside the flight, never over it)")
        for vi in np.nonzero(in_v)[0]:
            if any(nm not in own and not nm.startswith(own_rung) for nm in vert_parts.get(int(vi), ())):
                margin = min(margin, float(abs(Vv[vi] - line) - L["corridor_half"]))
    return round(margin, 3)


def _zone_table(spec):
    if spec.family == "tram":
        z = [(n, dy, dz, al) for n, _x, dy, dz, al in CARRIER_TRAM] + list(R30_ZONES)
        lane = TRAM["lane_y"]
    else:
        z = [(n, dy, dz, al) for n, _x, dy, dz, al in CARRIER_GD] + list(GD_HEAD_RULE)
        lane = GD["lane_y"]
    return z, lane


def _clip_2d(p, q, lo, hi):
    """Liang-Barsky: does segment p->q (2D) enter the open box (lo, hi)?"""
    t0, t1 = 0.0, 1.0
    for k in range(2):
        d = q[k] - p[k]
        if abs(d) < 1e-12:
            if p[k] <= lo[k] or p[k] >= hi[k]:
                return False
            continue
        ta, tb = (lo[k] - p[k]) / d, (hi[k] - p[k]) / d
        if ta > tb:
            ta, tb = tb, ta
        t0, t1 = max(t0, ta), min(t1, tb)
        if t0 >= t1:
            return False
    return True


def check_zones(mv, spec, parts):
    """
    C8: carrier zones (x-unbounded prisms in the lane frame, both sides, shrunk ZONE_SHRINK) and R30 /
    GD head-rule zones: no vertex, no member-axis segment and no exact-AABB part of a non-allowed part
    inside. Rope corridors: the level kinked HV / LV lines pass clear of every non-support part.
    """
    zones, lane = _zone_table(spec)
    V = mv.V
    e = ZONE_SHRINK
    hits = []
    pid_name = {i + 1: n for i, n in enumerate(mv.table)}
    for s in (1, -1):
        dy = s * V[:, 1] - lane
        dz = V[:, 2] - spec.z0
        for zn, (dy0, dy1), (dz0, dz1), allowed in zones:
            inside = (dy > dy0 + e) & (dy < dy1 - e) & (dz > dz0 + e) & (dz < dz1 - e)
            if inside.any():
                vids = np.nonzero(inside)[0]
                sel = np.isin(mv.pairs[:, 0], vids)
                for pid in np.unique(mv.pairs[sel, 1]):
                    nm = pid_name[int(pid)]
                    if not nm.startswith(allowed) if allowed else True:
                        hits.append((zn, s, nm, "vertex"))
            lo, hi = (dy0 + e, dz0 + e), (dy1 - e, dz1 - e)
            for nm, (pts, w) in parts.axes.items():
                if allowed and nm.startswith(allowed):
                    continue
                for p, q in zip(pts, pts[1:]):
                    pp = (s * p[1] - lane, p[2] - spec.z0)
                    qq = (s * q[1] - lane, q[2] - spec.z0)
                    if _clip_2d(pp, qq, lo, hi):
                        hits.append((zn, s, nm, "axis"))
            for nm in parts.exact_aabb:
                if allowed and nm.startswith(allowed):
                    continue
                v = mv.verts(nm)
                ady = s * v[:, 1] - lane
                adz = v[:, 2] - spec.z0
                if ady.max() > lo[0] and ady.min() < hi[0] and adz.max() > lo[1] and adz.min() < hi[1]:
                    hits.append((zn, s, nm, "aabb"))
    if hits:
        uniq = sorted(set(hits))
        _fail("C8", f"{len(uniq)} zone hits, e.g. {uniq[:6]}")
    corridors = _rope_corridors(mv, spec)
    return {"zones": [z[0] for z in zones], "hits": 0, "corridors": corridors}


def _rope_corridors(mv, spec):
    """Level kinked rope lines (end slope s0): radius - 2 mm must hold no vertex of a non-support part."""
    V = mv.V
    if spec.family == "tram":
        ropes = [("HV_track", TRAM["lane_y"], 0.0, TRAM["rope_r_hv"], ("saddle_",)),
                 ("LV_haul", TRAM["haul_y"], TRAM["haul_dz"], TRAM["rope_r_lv"], ("haulroll_",))]
    else:
        ropes = [("HV_haul", GD["lane_y"], 0.0, GD["rope_r_hv"], ("sheave_",)),
                 ("LV_comm", 0.0, GD["comm_dA"], GD["rope_r_lv"], ())]
    out = {}
    pid_name = {i + 1: n for i, n in enumerate(mv.table)}
    for rn, y, dz, r, allowed in ropes:
        sides = (1, -1) if y > 0 else (1,)
        worst = 9.0
        for s in sides:
            zr = spec.z0 + dz - spec.s0 * np.abs(V[:, 0])
            d = np.hypot(V[:, 1] - s * y, V[:, 2] - zr)
            near = np.nonzero((d < r - 0.002) & (np.abs(V[:, 0]) < 12.0))[0]
            if near.size:
                sel = np.isin(mv.pairs[:, 0], near)
                bad = sorted({pid_name[int(p)] for p in np.unique(mv.pairs[sel, 1])
                              if not (allowed and pid_name[int(p)].startswith(allowed))})
                if bad:
                    _fail("C8", f"rope corridor {rn} side {s}: parts inside the rope {bad[:6]}")
            mask = np.abs(V[:, 0]) < 12.0
            worst = min(worst, float(d[mask].min()) - r)
        out[rn] = {"y": y, "dz": dz, "r": r, "min_clearance_m": round(worst, 4)}
    return out


def _check_symmetry(V, mv=None, exempt=LADDER_ASYM):
    """C11: y-mirror symmetry within 1e-4. Revision 1: the ladder system (LADDER_ASYM prefixes:
    offset flights + side baskets, verdict 8) is exempt - it cannot be mirror symmetric by design;
    every other part (lattice, head, saddles, frame, cranes, deck rail with its centered gate) is
    still checked, and the exempt parts are listed in the report."""
    from mathutils.kdtree import KDTree
    keep = np.ones(len(V), dtype=bool)
    exempt_parts = []
    if mv is not None and exempt:
        ex_pids = [i + 1 for i, nm in enumerate(mv.table) if nm.startswith(tuple(exempt))]
        exempt_parts = sorted({mv.table[p - 1].split("_")[0] for p in ex_pids})
        if ex_pids:
            ex_v = np.unique(mv.pairs[np.isin(mv.pairs[:, 1], ex_pids), 0])
            keep[ex_v] = False
    idx = np.nonzero(keep)[0]
    kd = KDTree(len(idx))
    for i in idx:
        kd.insert(V[i], int(i))
    kd.balance()
    worst, wi = 0.0, -1
    for i in idx:
        v = V[i]
        _co, _j, d = kd.find((v[0], -v[1], v[2]))
        if d > worst:
            worst, wi = d, int(i)
    if worst > 1e-4:
        who = ""
        if mv is not None:
            pids = mv.pairs[mv.pairs[:, 0] == wi, 1]
            who = f" at {tuple(np.round(V[wi], 3))} in {[mv.table[int(p) - 1] for p in pids]}"
        _fail("C11", f"y-mirror symmetry broken: worst vertex distance {worst:.6f} m{who}")
    return {"y_mirror_worst_m": round(worst, 7), "exempt_prefixes": list(exempt),
            "exempt_vertices": int(len(V) - len(idx)), "exempt_part_kinds": exempt_parts}


# --------------------------------------------------------------------------------------
# Detail pass 1 contracts (design 6.1 DT-C3 / C4 / C5; generator-local: lift_cabin, where the
# coplanar test was born, is another fleet's file, so the measurement is copied here with the
# same tolerances: 1 deg, 0.5 mm, overlaps > 0.1 cm2)
# --------------------------------------------------------------------------------------

COPLANAR_MAX_CM2 = 1.0          # DT-C3: same-facing overlap per part pair
HIDDEN_COVER = 0.995            # DT-C4: a triangle this much covered by opposed faces is hidden
ISO_BOUND = (1.05, 1.35)        # DT-C5: area-weighted texel anisotropy median / p95 (whole mesh)


def _mesh_tris(obj):
    me = obj.data
    me.calc_loop_triangles()
    nt = len(me.loop_triangles)
    tv = np.zeros(nt * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("vertices", tv)
    tv = tv.reshape(-1, 3)
    tl = np.zeros(nt * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("loops", tl)
    tp = np.zeros(nt, dtype=np.int64)
    me.loop_triangles.foreach_get("polygon_index", tp)
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    pid = np.zeros(len(me.polygons), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", pid)
    return tv, tl.reshape(-1, 3), tp, co, pid[tp]


def _clip_convex(subj, clipper):
    """Sutherland-Hodgman (lift_cabin._clip_convex): convex `subj` clipped by convex CCW `clipper`."""
    out = list(subj)
    for i in range(len(clipper)):
        (ax, ay), (bx, by) = clipper[i], clipper[(i + 1) % len(clipper)]
        inp, out = out, []
        if not inp:
            break

        def inside(p):
            return (bx - ax) * (p[1] - ay) - (by - ay) * (p[0] - ax) >= -1e-12

        def cut(p, q):
            den = (p[0] - q[0]) * (ay - by) - (p[1] - q[1]) * (ax - bx)
            if abs(den) < 1e-18:
                return q
            t = ((p[0] - ax) * (ay - by) - (p[1] - ay) * (ax - bx)) / den
            return (p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1]))
        s = inp[-1]
        for e in inp:
            if inside(e):
                if not inside(s):
                    out.append(cut(s, e))
                out.append(e)
            elif inside(s):
                out.append(cut(s, e))
            s = e
    return out


def _poly_area2(poly):
    return 0.5 * abs(sum(poly[i][0] * poly[(i + 1) % len(poly)][1]
                         - poly[(i + 1) % len(poly)][0] * poly[i][1] for i in range(len(poly))))


def _coplanar_candidates(obj, tol_d=0.0005, ang_deg=1.0):
    """Yields (kind, ta, tb, P, tpart, n_a) for every pair of triangles with normals within ang_deg
    (same or opposed), overlapping AABBs and no shared edge (vertices welded at 1e-5 m) - the
    candidate set of lift_cabin.coplanar_overlaps. Its plane test |d_a -+ d_b| <= tol_d (d = n . p
    from the WORLD origin) is not used: on a 60 m tower two planes 1 deg apart differ in d by up to
    1 m, so the test both misses and invents pairs (it flagged the 0.9 deg-sloped haul bar against
    the level ties, 1 cm apart). The callers test the plane distance LOCALLY, on the overlap."""
    tv, _tl, _tp, co, tpart = _mesh_tris(obj)
    _u, inv = np.unique(np.round(co / 1e-5).astype(np.int64), axis=0, return_inverse=True)
    vid = inv.reshape(-1)[tv]
    P = co[tv]
    fn = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0])
    ln = np.linalg.norm(fn, axis=1)
    keep = np.where(ln > 2e-10)[0]
    n = fn[keep] / ln[keep, None]
    d = np.einsum("ij,ij->i", n, P[keep, 0])
    lo, hi = P[keep].min(axis=1), P[keep].max(axis=1)
    cosmax = math.cos(math.radians(ang_deg))
    M, B = len(keep), 512
    for s0 in range(0, M, B):
        s1 = min(M, s0 + B)
        dot = n[s0:s1] @ n.T
        same = dot > cosmax
        opp = dot < -cosmax
        box = np.all((lo[s0:s1, None, :] <= hi[None, :, :] + 1e-4)
                     & (lo[None, :, :] <= hi[s0:s1, None, :] + 1e-4), axis=2)
        for kind, mask in (("same", same), ("opposed", opp)):
            ii, jj = np.nonzero(mask & box)
            for a, b in zip((ii + s0).tolist(), jj.tolist()):
                if a == b:
                    continue
                ta, tb = int(keep[a]), int(keep[b])
                if len(set(vid[ta].tolist()) & set(vid[tb].tolist())) >= 2:
                    continue
                yield kind, ta, tb, P, tpart, n[a]


def _plane_gap(P, ta, tb, pts3):
    """Max distance of the 3D points `pts3` from triangle tb's plane (the local coplanarity test)."""
    nb = np.cross(P[tb, 1] - P[tb, 0], P[tb, 2] - P[tb, 0])
    nb = nb / np.linalg.norm(nb)
    return max(abs(float((q - P[tb, 0]) @ nb)) for q in pts3)


def coplanar_overlaps(obj, min_m2=1e-5, tol_d=0.0005):
    """DT-C3 (lift_cabin.coplanar_overlaps tolerances: 1 deg, 0.5 mm, > 0.1 cm2): {(part_a, part_b):
    cm2} of SAME-facing coplanar overlap (z-fight) per part pair (part_a <= part_b; equal = inside
    one part). The 0.5 mm is tested on the overlap polygon (both planes within it at every overlap
    vertex), not by the origin-relative plane offset (see _coplanar_candidates)."""
    table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    out = {}
    for kind, ta, tb, P, tpart, na in _coplanar_candidates(obj):
        if kind != "same" or tb < ta:
            continue
        if _plane_gap(P, ta, tb, P[ta]) > 0.05:          # cheap reject before clipping
            continue
        o, ex = P[ta, 0], P[ta, 1] - P[ta, 0]
        ex = ex / np.linalg.norm(ex)
        ey = np.cross(na, ex)
        t1 = [((p - o) @ ex, (p - o) @ ey) for p in P[ta]]
        t2 = [((p - o) @ ex, (p - o) @ ey) for p in P[tb]]
        if (t2[1][0] - t2[0][0]) * (t2[2][1] - t2[0][1]) - (t2[1][1] - t2[0][1]) * (t2[2][0] - t2[0][0]) < 0.0:
            t2 = t2[::-1]
        ov = _clip_convex(t2, t1)
        area = _poly_area2(ov) if len(ov) >= 3 else 0.0
        if area > min_m2 and _plane_gap(P, ta, tb, [o + ex * q[0] + ey * q[1] for q in ov]) <= tol_d:
            pa, pb = table[int(tpart[ta]) - 1], table[int(tpart[tb]) - 1]
            key = (min(pa, pb), max(pa, pb))
            out[key] = out.get(key, 0.0) + area * 1e4
    return {k: round(v, 2) for k, v in sorted(out.items(), key=lambda kv: -kv[1])}


def hidden_contact(obj, cover=HIDDEN_COVER, samples=14, tol_d=0.0005):
    """DT-C4: triangles whose area is >= `cover` covered by the UNION of opposed coplanar triangles
    of OTHER parts (buried contact faces: tris that change no pixel). Union coverage is sampled on
    a barycentric grid (samples per edge), so overlapping partners never double count (the design's
    probe summed areas: it counted the gondola deck lips, which a 4 cm strip leaves visible).
    Returns {part: n_tris}."""
    table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    partners = {}
    geo = None
    for kind, ta, tb, P, tpart, na in _coplanar_candidates(obj):
        if kind != "opposed" or tpart[ta] == tpart[tb]:
            continue
        if _plane_gap(P, ta, tb, P[ta]) > tol_d:          # the whole triangle within 0.5 mm of tb's plane
            continue
        partners.setdefault(ta, []).append(tb)
        geo = (P, tpart)
    if geo is None:
        return {}
    P, tpart = geo
    k = samples
    # barycentric grid that reaches 0.5 % from every edge (a thin uncovered strip still shows)
    g = [0.005 + 0.99 * i / k for i in range(k + 1)]
    bary = [(u, v) for u in g for v in g if u + v <= 0.995 + 1e-9]
    out = {}
    for ta, lst in partners.items():
        A, Bv, C = P[ta]
        nrm = np.cross(Bv - A, C - A)
        nn = nrm / np.linalg.norm(nrm)
        ex = (Bv - A) / np.linalg.norm(Bv - A)
        ey = np.cross(nn, ex)
        pts = [A + (Bv - A) * u + (C - A) * v for u, v in bary]
        got = 0
        for p in pts:
            q = ((p - A) @ ex, (p - A) @ ey)
            for tb in lst:
                tri = [((r - A) @ ex, (r - A) @ ey) for r in P[tb]]
                (x0, y0), (x1, y1), (x2, y2) = tri
                den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
                if abs(den) < 1e-18:
                    continue
                l1 = ((y1 - y2) * (q[0] - x2) + (x2 - x1) * (q[1] - y2)) / den
                l2 = ((y2 - y0) * (q[0] - x2) + (x0 - x2) * (q[1] - y2)) / den
                if l1 >= -1e-9 and l2 >= -1e-9 and l1 + l2 <= 1 + 1e-9:
                    got += 1
                    break
        if got >= cover * len(pts):
            nm = table[int(tpart[ta]) - 1]
            out[nm] = out.get(nm, 0) + 1
    return dict(sorted(out.items()))


def texel_anisotropy(obj, layer="UVMap", min_area=1e-4, min_width=0.02):
    """DT-C5: per triangle the singular-value ratio of the UV -> 3D map (1 = isotropic texels),
    faces >= 1 cm2 and not narrower than 0.02 m (2 area / longest edge). Returns (median, p95)
    area-weighted over the whole mesh and {prefix: (median, p95, m2)} per PREFIX_TABLE class."""
    me = obj.data
    tv, tl, _tp, co, tpart = _mesh_tris(obj)
    uvl = np.empty(len(me.loops) * 2, dtype=np.float64)
    me.uv_layers[layer].data.foreach_get("uv", uvl)
    uvl = uvl.reshape(-1, 2)
    P, Q = co[tv], uvl[tl]
    e1, e2 = P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]
    f1, f2 = Q[:, 1] - Q[:, 0], Q[:, 2] - Q[:, 0]
    a3 = 0.5 * np.linalg.norm(np.cross(e1, e2), axis=1)
    longest = np.max(np.stack([np.linalg.norm(e1, axis=1), np.linalg.norm(e2, axis=1),
                               np.linalg.norm(P[:, 2] - P[:, 1], axis=1)]), axis=0)
    det = f1[:, 0] * f2[:, 1] - f1[:, 1] * f2[:, 0]
    ok = (a3 >= min_area) & (2.0 * a3 / np.maximum(longest, 1e-12) >= min_width) & (np.abs(det) > 1e-14)
    an = np.full(len(a3), np.nan)
    for i in np.nonzero(ok)[0]:
        F = np.array([[f1[i, 0], f2[i, 0]], [f1[i, 1], f2[i, 1]]])
        J = np.stack([e1[i], e2[i]], axis=1) @ np.linalg.inv(F)
        s = np.linalg.svd(J, compute_uv=False)
        an[i] = s[0] / max(s[1], 1e-12)
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])

    def wq(mask):
        m = mask & np.isfinite(an)
        if not m.any():
            return None
        o = np.argsort(an[m])
        cw = np.cumsum(a3[m][o]) / a3[m].sum()
        v = an[m][o]
        return (round(float(v[np.searchsorted(cw, 0.5)]), 3),
                round(float(v[min(len(v) - 1, np.searchsorted(cw, 0.95))]), 3), round(float(a3[m].sum()), 3))
    whole = wq(np.ones(len(a3), bool))
    pre = np.array([prefix_of(table[p - 1])[0] for p in tpart], dtype=object)
    per = {}
    for p in sorted(set(pre)):
        r = wq(pre == p)
        if r is not None:
            per[p] = r
    return whole, per


def detail_part_counts(spec, lay, parts):
    """DT-C6 expected counts of the detail-pass parts per variant."""
    flights = len(parts.ladder["flights"]) if parts.ladder else 0
    c = {"plate_": 2, "platerim_": 2, "arrest_": flights + (0 if spec.family == "tram" else 1)}
    if spec.family == "tram":
        c["collar_"] = c["collarcap_"] = 4 * len(lay.get("collar_rings") or ())
        c["haulroll_liner"] = 2 * len(ROLLER["x"])
        c["saddle_liner"] = 2
        c["beacon_"] = 2 if spec.beacon else 0
    else:
        c["sheave_liner"] = 2 * spec.n_sheaves
    return c


def check_dp1_geometry(t, table, spec, parts, lay, uv_layer="UVMap", net=False):
    """Detail pass 1 geometry contracts on one mesh (frame-free; raises ContractError):
      DT-C3 same-facing coplanar overlap <= 1 cm2 per part pair;
      DT-C4 no hidden contact triangle (>= 99.5 % covered by opposed faces of another part);
      DT-C5 texel anisotropy, whole mesh: area-weighted median <= 1.05, p95 <= 1.35;
      DT-C6 detail part counts, plate offsets (front 4-8 mm proud, rims 1-3 mm into the host),
            arrest front <= rung plane + 0.036 (lattice), collars 0.04 proud of the leg faces and
            never on the kink ring, and no detail part intersects a triangle of a non-host part
            (BVH overlap; hosts: plate -> its beam, arrest -> the rungs, collar -> its leg).
    Returns the report (also the per-class anisotropy for the build report)."""
    from mathutils.bvhtree import BVHTree
    rep = {}
    cop = coplanar_overlaps(t)
    worst = max(cop.values()) if cop else 0.0
    rep["coplanar_same_cm2"] = {"total": round(sum(cop.values()), 2), "worst_pair": worst,
                                "pairs": {f"{a}|{b}": v for (a, b), v in list(cop.items())[:6]}}
    if worst > COPLANAR_MAX_CM2:
        _fail("DT-C3", f"same-facing coplanar overlap {worst} cm2 > {COPLANAR_MAX_CM2}: {list(cop.items())[:4]}")
    hid = hidden_contact(t)
    rep["hidden_contact_tris"] = hid
    if hid:
        _fail("DT-C4", f"hidden contact triangles {hid}")
    whole, per = texel_anisotropy(t, uv_layer)
    rep["anisotropy"] = {"median": whole[0], "p95": whole[1], "per_class_over_1.35":
                         {k: v[:2] for k, v in per.items() if v[1] > ISO_BOUND[1]}}
    if whole[0] > ISO_BOUND[0] or whole[1] > ISO_BOUND[1]:
        _fail("DT-C5", f"texel anisotropy median {whole[0]} / p95 {whole[1]} > {ISO_BOUND}")
    # skeptic S4 (scope): also PER CLASS for every class of >= 0.5 m2 (the whole-mesh figure hides
    # a 12x post class inside 460-1,860 m2 of isotropic members); smaller classes are reported
    bad = {k: v for k, v in per.items() if v[2] >= 0.5 and (v[0] > ISO_BOUND[0] or v[1] > ISO_BOUND[1])}
    if bad:
        _fail("DT-C5", f"texel anisotropy per class (median / p95 / m2) over {ISO_BOUND}: {bad}")
    # DT-C6
    mv = MeshView(t, table, net=net)                     # authoring coordinates either way
    want = detail_part_counts(spec, lay, parts)
    got = {k: len([n for n in table if n.startswith(k)]) for k in want}
    rep["detail_parts"] = got
    if got != want:
        _fail("DT-C6", f"detail part counts {got} != {want}")
    cfg = PLATE[spec.family]
    for side, sx in (("p", 1.0), ("n", -1.0)):
        fv = mv.verts(f"plate_{side}")
        rv = mv.verts(f"platerim_{side}")
        proud = float(np.abs(fv[:, 0]).min() - cfg["face_x"])
        into = float(cfg["face_x"] - np.abs(rv[:, 0]).min())
        if not (0.004 - 1e-6 <= proud <= 0.008 + 1e-6 and 0.001 - 1e-6 <= into <= 0.003 + 1e-6):
            _fail("DT-C6", f"plate_{side}: front {proud * 1000:.1f} mm proud, rims {into * 1000:.1f} mm in")
        rep[f"plate_{side}_mm"] = [round(proud * 1000, 2), round(into * 1000, 2)]
    if parts.ladder:
        face = parts.ladder["face"]
        worst_front = 0.0
        for f in parts.ladder["flights"]:
            av = mv.verts(f"arrest_{f['j']}")
            an, _av = face_nv(face, av)
            u = an - np.interp(av[:, 2], [p[1] for p in f["pts"]], [p[0] for p in f["pts"]])
            worst_front = max(worst_front, float(-u.min()))
        rep["arrest_front_m"] = round(worst_front, 4)
        if worst_front > 0.036 + 1e-6:
            _fail("DT-C6", f"arrest rail front {worst_front:.4f} > rung plane + 0.036 (C7 corridor start 0.04)")
    if spec.family == "tram":
        k_idx = min(range(len(lay["rings"])), key=lambda i: abs(lay["rings"][i] - spec.z_k))
        if k_idx in lay["collar_rings"]:
            _fail("DT-C6", f"a collar on the kink ring {k_idx}")
        prouds = []
        for li in range(4):
            pts = [Vector(p) for p in parts.axes[f"leg_{li}"][0]]
            for nm in mv.names(f"collar_{li}_"):
                cv = mv.verts(nm)
                cz = float(cv[:, 2].mean())
                p, si = _poly_at_z(pts, cz)
                tdir = (pts[si + 1] - pts[si]).normalized()
                _P, frames = leg_frames(pts, XV)
                a, b = frames[si]
                d = cv - np.array(p)
                half = max(float(np.abs(d @ np.array(a)).max()), float(np.abs(d @ np.array(b)).max()))
                prouds.append(half - W_TR["leg"] / 2.0)
        rep["collar_proud_m"] = [round(min(prouds), 4), round(max(prouds), 4)]
        if not (0.04 - 1e-4 <= min(prouds) and max(prouds) <= 0.06 + 1e-4):
            _fail("DT-C6", f"collars proud {rep['collar_proud_m']} not within 0.04..0.06 of the leg faces")
    # BVH: detail part vs every triangle of a non-host part
    me = t.data
    co = [v.co.copy() for v in me.vertices]
    pid = np.zeros(len(me.polygons), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", pid)
    polys = [list(p.vertices) for p in me.polygons]

    def host_of(nm):
        if nm.startswith(("plate", "platerim")):
            return ("gantrybeam_0", "gdbeam_0") if nm.endswith("p") else ("gantrybeam_1", "gdbeam_0")
        if nm.startswith("arrest_"):
            j = nm[len("arrest_"):]
            return ("rung_m",) if j == "m" else (f"rung_{j}_",)
        if nm.startswith(("collar_", "collarcap_")):
            return (f"leg_{nm.split('_')[1]}", f"collar_{nm.split('_')[1]}_", f"collarcap_{nm.split('_')[1]}_")
        return ()
    clashes = []
    for nm in table:
        if not nm.startswith(("plate_", "platerim_", "arrest_", "collar_", "collarcap_")):
            continue
        me_id = table.index(nm) + 1
        hosts = host_of(nm)
        own = [polys[i] for i in range(len(polys)) if pid[i] == me_id]
        oth_idx = [i for i in range(len(polys)) if pid[i] != me_id and not table[pid[i] - 1].startswith(hosts or ("\0",))
                   and not table[pid[i] - 1].startswith(("plate", "platerim")) ]
        if not own or not oth_idx:
            continue
        b1 = BVHTree.FromPolygons(co, own, all_triangles=False)
        b2 = BVHTree.FromPolygons(co, [polys[i] for i in oth_idx], all_triangles=False)
        hits = b1.overlap(b2)
        if hits:
            clashes.append((nm, sorted({table[pid[oth_idx[j]] - 1] for _i, j in hits})[:4]))
    rep["bvh_clashes"] = clashes
    if clashes:
        _fail("DT-C6", f"detail parts intersect non-host parts: {clashes[:4]}")
    return rep


# --------------------------------------------------------------------------------------
# Grade sweep (MUST-CHANGE 2)
# --------------------------------------------------------------------------------------

def grade_points(mv, parts, step=0.10):
    """All vertices + member-axis samples every `step` m, with their part names."""
    pts, names = [mv.V], [None]
    pid_name = {i + 1: n for i, n in enumerate(mv.table)}
    vert_part = {}
    for v, pid in mv.pairs:
        vert_part.setdefault(int(v), []).append(pid_name[int(pid)])
    rows_p, rows_n = [], []
    for v, plist in vert_part.items():
        for nm in plist:
            rows_p.append(mv.V[v])
            rows_n.append(nm)
    for nm, (axis, _w) in parts.axes.items():
        for p, q in zip(axis, axis[1:]):
            p, q = np.array(p), np.array(q)
            n = max(2, int(math.ceil(np.linalg.norm(q - p) / step)) + 1)
            for t in np.linspace(0.0, 1.0, n):
                rows_p.append(p + (q - p) * t)
                rows_n.append(nm)
    return np.array(rows_p, dtype=np.float64), np.array(rows_n, dtype=object)


def grade_sweep(mv, parts, spec, g_step=0.01):
    """
    MUST-CHANGE 2. Level carrier boxes ride the kinked lane: z_ref(x_c) = z0 + (g - s0) x_c uphill
    (x_c >= 0) and z0 + (g + s0) x_c downhill. A tower point clashes when some carrier position puts it
    inside a box (exact per point: z_ref is piecewise linear, so its range over the x_c interval is
    spanned by the interval ends and the kink). Returns the first clashing g per carrier box and per
    class (structure / rope_support) up to the catalog slope (None = clear). g = 0 must be clean.
    Floor (raise): structure g_clear of every box that stays LEVEL in all carrier designs (tram:
    everything below the carriage pivot K; the one-mesh carriage is reported, cabins Q4 split pitches it).
    """
    P, names = grade_points(mv, parts)
    lane = TRAM["lane_y"] if spec.family == "tram" else GD["lane_y"]
    boxes = CARRIER_TRAM if spec.family == "tram" else CARRIER_GD
    g_max = TRAM["slope_max"] if spec.family == "tram" else GD["slope_max"]
    support = ROPE_SUPPORT[spec.family]
    is_sup = np.array([n.startswith(support) for n in names])
    s0, z0, tol = spec.s0, spec.z0, SWEEP_TOL
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    pitched = PITCHED_BOXES.get(spec.family, ())
    pre, pre_ok = [], []
    for bn, (bx0, bx1), (dy0, dy1), (bz0, bz1), allowed in boxes:
        ok = np.array([n.startswith(allowed) for n in names]) if allowed else np.zeros(len(P), bool)
        for s in (1, -1):
            dy = s * y - lane
            for dst, mask in ((pre, ~ok), (pre_ok, ok)):
                lat = (dy > dy0 + tol) & (dy < dy1 - tol) & mask
                if lat.any():
                    idx = np.nonzero(lat)[0]
                    dst.append((bn, idx, x[idx] - (bx1 - tol), x[idx] - (bx0 + tol),
                                z[idx] - (bz1 - tol), z[idx] - (bz0 + tol)))

    def zref(xc, g):
        return z0 + np.where(xc >= 0, (g - s0) * xc, (g + s0) * xc)

    def clash(g, table=None):
        out = {}
        for bn, idx, a, b, need_lo, need_hi in (pre if table is None else table):
            za, zb = zref(a, g), zref(b, g)
            lo, hi = np.minimum(za, zb), np.maximum(za, zb)
            straddle = (a < 0) & (b > 0)
            lo = np.where(straddle, np.minimum(lo, z0), lo)
            hi = np.where(straddle, np.maximum(hi, z0), hi)
            h = (hi > need_lo) & (lo < need_hi)
            if h.any():
                out.setdefault(bn, []).append(idx[h])
        return {k: np.concatenate(v) for k, v in out.items()}

    accepted = {bn: sorted({names[i] for i in v}) for bn, v in clash(0.0, pre_ok).items()}
    lvl = clash(0.0)
    if lvl:
        _fail("C8g", "level carrier sweep clashes: "
              + str(sorted((k, sorted({names[i] for i in v})[:3]) for k, v in lvl.items())))
    by_box = {bn: {"structure": None, "rope_support": None, "parts": {}} for bn, *_r in boxes}
    n = int(round(g_max / g_step))
    for i in range(1, n + 1):
        g = round(i * g_step, 4)
        for bn, hit in clash(g).items():
            for cls, mask in (("structure", ~is_sup[hit]), ("rope_support", is_sup[hit])):
                if by_box[bn][cls] is None and mask.any():
                    by_box[bn][cls] = g
                    by_box[bn]["parts"][cls] = sorted({names[j] for j in hit[mask]})[:4]
        if all(v["structure"] is not None and v["rope_support"] is not None for v in by_box.values()):
            break

    def first(cls, keep=lambda bn: True):
        vals = [(v[cls], bn) for bn, v in by_box.items() if v[cls] is not None and keep(bn)]
        return min(vals) if vals else (None, None)
    gs, bs = first("structure")
    gr, br = first("rope_support")
    gl, bl = first("structure", lambda bn: bn not in pitched)
    result = {"structure": gs, "structure_box": bs, "rope_support": gr, "rope_support_box": br,
              "structure_level_carrier": gl, "structure_level_carrier_box": bl,
              "by_box": by_box, "g_max": g_max, "g_step": g_step, "end_slope": s0,
              "pitched_boxes": list(pitched), "accepted_contacts_level": accepted}
    if gl is not None and gl < G_FLOOR[spec.family]:
        _fail("C8g", f"structure g_clear {gl} (box {bl}: {by_box[bl]['parts'].get('structure')}) "
                     f"< floor {G_FLOOR[spec.family]}")
    return result


# --------------------------------------------------------------------------------------
# LOD chain helpers
# --------------------------------------------------------------------------------------

def count_after_delete(t, table, prefixes):
    tmp = t.copy()
    tmp.data = t.data.copy()
    bpy.context.collection.objects.link(tmp)
    try:
        cs2_asset._delete_tagged_faces(tmp, table, tuple(prefixes))
        return cs2_asset.tri_count(tmp)
    finally:
        me = tmp.data
        bpy.data.objects.remove(tmp, do_unlink=True)
        bpy.data.meshes.remove(me)


def budget(spec):
    if spec.family == "tram":
        t0, cap, l1, l2w = TRAM_BUDGET[spec.H]
        return {"lod0_target": t0, "lod0_cap": cap, "lod1_target": l1, "lod2_warn": l2w,
                "lod2_cap": TRAM_LOD2_CAP}
    t0, cap, l1, l2 = GD_BUDGET
    return {"lod0_target": t0, "lod0_cap": cap, "lod1_target": l1, "lod2_warn": l2, "lod2_cap": l2}


def _vbounds(obj):
    n = len(obj.data.vertices)
    co = np.empty(n * 3, dtype=np.float64)
    obj.data.vertices.foreach_get("co", co)
    co = co.reshape(n, 3)
    return co.min(axis=0), co.max(axis=0)


def check_c9(t, lods, spec):
    """C9: budgets (raise at caps, warn at targets), LOD1 <= 50 %, bounds drift <= 0.01 m."""
    b = budget(spec)
    tris = [cs2_asset.tri_count(t)] + [cs2_asset.tri_count(l) for l in lods]
    warns = []
    if tris[0] > b["lod0_cap"]:
        _fail("C9", f"LOD0 {tris[0]} > cap {b['lod0_cap']}")
    if tris[0] > b["lod0_target"]:
        warns.append(f"LOD0 {tris[0]} > target {b['lod0_target']}")
    if len(tris) > 1:
        if tris[1] > 0.5 * tris[0]:
            _fail("C9", f"LOD1 {tris[1]} > 50 % of LOD0 {tris[0]}")
        if tris[1] > b["lod1_target"]:
            _fail("C9", f"LOD1 {tris[1]} > target {b['lod1_target']}")
    if len(tris) > 2:
        if tris[2] > b["lod2_cap"]:
            _fail("C9", f"LOD2 {tris[2]} > cap {b['lod2_cap']}")
        if tris[2] > b["lod2_warn"]:
            warns.append(f"LOD2 {tris[2]} > target {b['lod2_warn']}")
    lo0, hi0 = _vbounds(t)
    drift = []
    for k, l in enumerate(lods, start=1):
        lo, hi = _vbounds(l)
        d = float(max(np.abs(lo - lo0).max(), np.abs(hi - hi0).max()))
        drift.append(round(d, 5))
        if d > 0.01:
            _fail("C9", f"LOD{k} bounds drift {d:.4f} m > 0.01 (top-anchor contract)")
    for w in warns:
        print(f"[tram_tower] WARN budget: {w}")
    return {"tris": tris, "drift_m": drift, "warnings": warns, **b}


# --------------------------------------------------------------------------------------
# Finish pass helpers (prefiltered CC0 sets, post_compose)
# --------------------------------------------------------------------------------------

def _srgb_to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _lin_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1.0 / 2.4) - 0.055)


def prefilter_sets(sets=("Concrete034", "Metal032"), factor=4):
    """
    R24-style area average of the CC0 sources (the 1-sample EMIT bake aliases at 10-20x
    minification): box-filter every map by `factor` into <tmp>/cs2_lattice_prefilter/f<factor>/<Set>/
    with strict ambientCG names `<Set>_<Map>.png`. Color averaged in linear light, normals
    renormalised. Deterministic. Returns the texture root to give MaterialSpec.
    """
    import cs2_bake
    root = os.path.join(tempfile.gettempdir(), "cs2_lattice_prefilter", f"f{factor}")
    for st in sets:
        src_dir = os.path.join(TEXTURES, st)
        dst_dir = os.path.join(root, st)
        os.makedirs(dst_dir, exist_ok=True)
        for mp in ("Color", "NormalGL", "Roughness", "Metalness"):
            src = cs2_bake.find_set_map(src_dir, mp)
            if src is None:
                continue
            dst = os.path.join(dst_dir, f"{st}_{mp}.png")
            img = bpy.data.images.load(src, check_existing=False)
            img.colorspace_settings.name = "sRGB" if mp == "Color" else "Non-Color"
            w, h = img.size
            px = np.empty(w * h * 4, dtype=np.float32)
            img.pixels.foreach_get(px)
            bpy.data.images.remove(img)
            px = px.reshape(h, w, 4).astype(np.float64)
            if mp == "Color":
                px[..., :3] = _srgb_to_lin(px[..., :3])
            hh, ww = h // factor, w // factor
            px = px[:hh * factor, :ww * factor].reshape(hh, factor, ww, factor, 4).mean(axis=(1, 3))
            if mp == "Color":
                px[..., :3] = _lin_to_srgb(px[..., :3])
            elif mp == "NormalGL":
                n = px[..., :3] * 2.0 - 1.0
                n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-8)
                px[..., :3] = n * 0.5 + 0.5
            px[..., 3] = 1.0
            out = bpy.data.images.new(f"pf_{st}_{mp}", ww, hh, alpha=True)
            out.colorspace_settings.name = "sRGB" if mp == "Color" else "Non-Color"
            out.pixels.foreach_set(np.ascontiguousarray(px, dtype=np.float32).ravel())
            out.filepath_raw = dst
            out.file_format = "PNG"
            out.save()
            bpy.data.images.remove(out)
    return root


def make_post_compose(obj, detail):
    """Generator-local finish hook (MC4): density report, relief, compose_maps, normal renorm."""
    def hook(base, nrm, msk, ctl, ao, mesh, A):
        part_map, ppm_map = cs2_compose.rasterise_face_fields(mesh, A)
        h = cs2_compose.detail_height(mesh, A, detail, verbose=False)
        hook.height_range = (float(h.min()), float(h.max()))
        uncovered = nrm[..., 0:3].max(axis=-1) < 1e-3
        base, nrm, msk, ctl = cs2_compose.compose_maps(
            base, nrm, msk, ctl, None, h, ppm_map, {"reserved": (), "part_map": part_map, "seed": 0})
        n = nrm[..., 0:3] * 2.0 - 1.0
        n[..., 2] = np.maximum(n[..., 2], 0.05)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        nrm[..., 0:3] = n * 0.5 + 0.5
        nrm[uncovered, 0:3] = (0.5, 0.5, 1.0)
        nrm[..., 3] = 1.0
        return base, nrm, msk, ctl
    hook.height_range = None
    return hook


# --------------------------------------------------------------------------------------
# Detail pass 1 texture (design 4.2 / 5.2 as corrected by the skeptic: M2 floors, M3 per-part
# plates, M7 tone, M10 planes). Marks are authored in the AUTHORING frame and mapped to the net
# frame the bake sees (cs2_compose.transform_marks; the compose hook runs after orient_for_net).
# --------------------------------------------------------------------------------------

NET_ROT = Matrix.Rotation(-math.pi / 2.0, 4, "Z")      # orient_for_net: (x, y, z) -> (y, -x, z)
# basket gratings (14-23 px/m at weight 0.75) stay plain: no pitch reaches 2 texels there without a
# density raise the margin-bound atlas cannot pay (the design's 0.06 drew only on the decks either)
GRATE_M = {"tram": {"grate_deck": 0.06, "grate_bskt": None},
           "gondola": {"grate_gdeck": 0.12, "grate_trans": 0.12, "grate_bskt": None}}
# tone (DT6 / M7): (mottle_m, stretch, amp, smooth, chalk_amp, chalk_smooth) per material class.
# A part's MaskMap A std gains ~ smooth x MOTTLE_STD (0.234): 0.10 -> 0.023 (M7 restated: DT-C9
# reads the tone field's own std against that, not a fixed 0.03). Lattice galv cells >= 0.25 m
# (the 0.15 m bloom is under the 4-texel tone floor at 18-24 px/m, M7b); the ladders / baskets
# (9-23 px/m, 0.036-0.05 m members) carry no mottle (scoped out of DT-C9).
TONE = {"gondola": {"paint": (0.60, 1.0, 0.04, 0.10, 0.06, 0.08),
                    "galv": (0.25, 2.5, 0.05, 0.08, 0.0, 0.0)},
        "tram": {"paint": (0.25, 3.0, 0.06, 0.08, 0.0, 0.0),        # galvanised lattice: bloom, no chalk
                 "galv": (0.15, 2.5, 0.05, 0.06, 0.0, 0.0),
                 "crane": (0.60, 1.0, 0.04, 0.10, 0.06, 0.08)}}
TONE_SKIP = ("rung_", "lrail_", "bskt_", "arrest_", "post_", "rail_", "kick_", "grate_bskt",
             "sheave_liner", "haulroll_liner", "saddle_liner", "plate", "lug_", "collarcap_")


def _poly_at_z(pts, z):
    """(point, segment index) on a polyline (Vectors, z increasing) at height z."""
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        if a.z - 1e-9 <= z <= b.z + 1e-9:
            return a + (b - a) * ((z - a.z) / (b.z - a.z)), i
    raise ContractError(f"z {z:.3f} outside the polyline {pts[0].z:.3f}..{pts[-1].z:.3f}")


def _outer_faces(frame, corner):
    """The two OUTER face normals of a leg at shaft corner (cx, cy): the frame vectors (+-a, +-b)
    closest to +-x and +-y."""
    a, b = frame
    cand = [a, -a, b, -b]
    fx = max(cand, key=lambda v: v.x * corner[0])
    fy = max(cand, key=lambda v: v.y * corner[1])
    return fx, fy


def relief_specs(spec, lay, parts, table):
    """prefix -> cs2_compose.DetailSpec (authoring-frame marks). An empty DetailSpec excludes a
    longer name (sheave_liner under sheave_, ...)."""
    D, Mk = cs2_compose.DetailSpec, cs2_compose.Mark
    tram = spec.family == "tram"
    rings = lay["rings"]
    g = GRATE_M[spec.family]
    out = {"grate_deck_lip": D(), "grate_bskt_lip": D(), "grate_trans_lip": D(), "grate_gdeckp_lip": D(),
           "grate_gdeckn_lip": D(), "ped_": D(seams_m=1.2, seam_axes="rings", seam_depth_m=0.003),
           "pier_": D(seams_m=1.2, seam_axes="rings", seam_depth_m=0.003),
           "sheave_liner": D(), "haulroll_liner": D(), "saddle_liner": D(), "plate_": D(), "platerim_": D()}
    for k, v in g.items():
        out[k] = D(grate_m=v) if v else D()
    for nm in table:                          # every grating lip ring (grate_bskt3_lip, ...) stays plain
        if nm.endswith("_lip"):
            out[nm] = D()
    # legs: node plates at every ring level on both outer faces; gondola splice bands, tram rounds
    legw = W_TR["leg"] if tram else W_GD["leg"]
    np_half = (0.18, 0.18, 0.008) if tram else (0.09, 0.15, 0.006)     # across, along, h
    leg_marks = []
    for i, c in enumerate(CORNERS):
        pts = [Vector(p) for p in parts.axes[f"leg_{i}"][0]]
        _P, frames = leg_frames(pts, XV)
        for ri, z in enumerate(rings):
            if not pts[0].z <= z <= pts[-1].z:
                continue
            p, si = _poly_at_z(pts, z)
            t = (pts[si + 1] - pts[si]).normalized()
            for nrm in _outer_faces(frames[si], c):
                u = t.cross(nrm).normalized()          # u x v = nrm with v = t (along the leg, up)
                leg_marks.append(Mk("plate", tuple(p + nrm * (legw / 2.0)), u=tuple(u), v=tuple(t),
                                    half_u=np_half[0], half_v=np_half[1], h=np_half[2]))
            if not tram and ri >= 2 and ri % 2 == 0:
                q, sj = _poly_at_z(pts, z + 0.30)
                leg_marks.append(Mk("band", tuple(q), n=tuple((pts[sj + 1] - pts[sj]).normalized()),
                                    half_w=0.06, r_max=0.20, h=0.010))
    if tram:
        out["leg_"] = D(seams_m=6.0, seam_axes="rings", edge_round_m=0.12, marks=tuple(leg_marks))
    else:
        out["leg_"] = D(marks=tuple(leg_marks))
    if tram:
        rb = dict(bolt_pitch_m=0.15, bolt_r_m=0.026)
        # head chords: splice plates + bolt groups on the outer (travel) faces at y 0 and +-2.45
        chord_marks = []
        zt, zb = spec.z0 + HEAD["top_dz"], spec.z0 + HEAD["bot_dz"]
        for x in (HEAD["x"], -HEAD["x"]):
            sx = 1.0 if x > 0 else -1.0
            nrm = Vector((sx, 0.0, 0.0))
            u = ZV.cross(nrm)                          # viewer-right; u x v = nrm with v = z
            for z in (zt, zb):
                for y in (0.0, 2.45, -2.45):
                    c = (x + sx * HEAD["w"] / 2.0, y, z)
                    chord_marks.append(Mk("plate", c, u=tuple(u), v=(0, 0, 1), half_u=0.225, half_v=0.13, h=0.006))
                    chord_marks.append(Mk("bolts", c, u=tuple(u), v=(0, 0, 1), half_u=0.20, half_v=0.10,
                                          pitch=0.08, bolt_r=0.026, h=0.010))
        out["headchord_"] = D(seams_m=1.2, edge_round_m=0.06, marks=tuple(chord_marks), **rb)
        for pre in ("gantrybeam_", "headx_", "headend_"):
            out[pre] = D(seams_m=1.2, edge_round_m=0.06, **rb)
        out["collar_"] = D(bolt_pitch_m=0.10, bolt_r_m=0.026)
        # DT3: every other square member / box of the head that is neither bevelled nor smooth-shaded
        # gets the normal-map round (r >= 2.2 texels at its measured px/m)
        for pre, r in (("headvert_", 0.06), ("headdiag_", 0.06), ("gantrypost_", 0.06), ("gantryx_", 0.075),
                       ("sbrkarm_", 0.06), ("sbrk_", 0.075), ("sbrkhang_", 0.06), ("sbrkdiag_", 0.075),
                       ("sbrkknee_", 0.075), ("cranecol_", 0.075), ("cranejib_", 0.06), ("craneknee_", 0.075)):
            out[pre] = D(edge_round_m=r)
        # saddles: ribs as today, lightening slots between the ribs on both webs, a bolt row under
        # the top flange following the curved top (one row per section face)
        prof = saddle_profile(spec)
        sm = []
        L2 = spec.saddle_l / 2.0
        for s in (1, -1):
            ly = s * TRAM["lane_y"]
            for side in (1, -1):
                yw = ly + side * SADDLE["w"] / 2.0
                nrm = Vector((0.0, float(side), 0.0))
                u = ZV.cross(nrm)
                x = 0.3
                while x + 0.15 <= L2 - 0.15 + 1e-9:
                    for xs in (x, -x):
                        zc = _interp_profile(prof, xs) - SADDLE["d"] / 2.0
                        sm.append(Mk("slot", (xs, yw, zc), u=tuple(u), v=(0, 0, 1), half_u=0.15, half_v=0.09,
                                     h=-0.012))
                    x += 0.6
                for (xa, za), (xb, zb_) in zip(prof, prof[1:]):
                    e = Vector((xb - xa, 0.0, zb_ - za))
                    ln = e.length
                    eu = e / ln
                    if eu.dot(u) < 0:
                        eu = -eu
                    v = nrm.cross(eu)
                    c = Vector(((xa + xb) / 2.0, yw, (za + zb_) / 2.0)) - v * 0.06
                    sm.append(Mk("bolts", tuple(c), u=tuple(eu), v=tuple(v), half_u=ln / 2.0 - 0.04, half_v=0.0,
                                 pitch=0.15, bolt_r=0.026, h=0.006))
        out["saddle_"] = D(seams_m=0.6, seam_axes="ribs", seam_depth_m=0.003, edge_round_m=0.05, marks=tuple(sm))
        anchor = 0.25
    else:
        out["gdbeam_"] = D(edge_round_m=0.10)
        out["gdgirder_"] = D(seams_m=1.2, edge_round_m=0.10)
        out["trans_"] = D(edge_round_m=0.10)          # DT3 (the 1.2 m seams drew nothing on 0.24 boxes)
        out["gdpost_"] = D(edge_round_m=0.10)
        z_tube0 = spec.z_t + 0.25 + 0.015 + 0.015 + 0.015      # flange top (build_gondola_lattice zf + 0.015)
        out["mast_"] = D(seams_m=1.2, seam_axes="rings",
                         marks=(Mk("band", (0.0, 0.0, z_tube0 + 0.05), n=(0, 0, 1), half_w=0.05,
                                   r_max=GD["mast_r"] + 0.05, h=0.008),))
        # sheave hub caps (M2: r 0.12 at 18-24 px/m) on both discs
        hubs = []
        A = spec.z0
        for s in (1, -1):
            for x in lay["sheave_x"]:
                for side in (1, -1):
                    hubs.append(Mk("ring", (x, s * GD["lane_y"] + side * SHEAVE["w"] / 2.0, A), u=(1, 0, 0),
                                   n=(0, side, 0), r_in=0.0, r_out=0.12, h=0.006))
        if not cs2_sheaves.wheels_v2():       # T62 round 2: the v2 wheel's hub is the geometric boss (no ring)
            out["sheave_"] = D(marks=tuple(hubs))
        anchor = 0.15
    # base plates: 2 x 2 anchor nuts (tram pitch 0.50: clear of the 0.40 leg; gondola 0.30)
    pw = BPLATE_TR if tram else BPLATE_GD
    ped_h = PED_H_TR if tram else PED_H_GD
    bm_ = []
    for sx, sy in CORNERS:
        c = (sx * spec.B / 2.0, sy * spec.B / 2.0, ped_h + 0.05)
        bm_.append(Mk("bolts", c, u=(1, 0, 0), v=(0, 1, 0), half_u=2 * anchor, half_v=2 * anchor,
                      pitch=2 * anchor, bolt_r=0.035, h=0.012))
    out["bplate_"] = D(marks=tuple(bm_))
    if pw / 2.0 < anchor + 0.035:
        raise ContractError("bplate anchors off the plate")
    return out


def tone_specs(spec, table):
    """part-name prefix -> DetailSpec carrying only tone fields (mottle / chalk), by material."""
    D = cs2_compose.DetailSpec
    cfg = TONE[spec.family]
    out = {}
    for pre, mat, _dw in PREFIX_TABLE:
        if pre.startswith(TONE_SKIP) or mat not in cfg:
            out[pre] = D()
            continue
        m, st, amp, sm, ca, cs = cfg[mat]
        out[pre] = D(mottle_m=m, mottle_stretch=st, mottle_amp=amp, mottle_smooth=sm,
                     chalk_amp=ca, chalk_smooth=cs)
    return out


def to_net(specs):
    """Copy of a spec dict with every Mark mapped authoring -> net frame."""
    out = {}
    for k, s in specs.items():
        out[k] = dataclasses.replace(s, marks=cs2_compose.transform_marks(s.marks, NET_ROT)) if s.marks else s
    return out


def _text_coverage(text, W, H, cap_px, ss=4):
    """Coverage (H, W) BOTTOM-UP of `text` in Blender's built-in font, cap height cap_px, centered
    (welcome_sign.text_triangles / _tri_coverage, re-done here: cs2_bldg-free)."""
    cu = bpy.data.curves.new("DP1PlateText", type="FONT")
    cu.body = text
    cu.size = 1.0
    cu.align_x = "CENTER"
    ob = bpy.data.objects.new("DP1PlateText", cu)
    bpy.context.scene.collection.objects.link(ob)
    try:
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        tris = [[(v.co.x, v.co.y) for v in f.verts] for f in bm.faces]
        bm.free()
        bpy.data.meshes.remove(me)
    finally:
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.curves.remove(cu)
    pts = np.array([p for t in tris for p in t], np.float64)
    lo, hi = pts.min(0), pts.max(0)
    sc = cap_px / (hi[1] - lo[1])
    cen = 0.5 * (lo + hi)
    Ws, Hs = W * ss, H * ss
    cov = np.zeros((Hs, Ws), np.float32)
    xs = (np.arange(Ws) + 0.5) / ss - W / 2.0
    ys = (np.arange(Hs) + 0.5) / ss - H / 2.0
    for t in tris:
        a, b, c = [((p[0] - cen[0]) * sc, (p[1] - cen[1]) * sc) for p in t]
        x0, x1 = min(a[0], b[0], c[0]), max(a[0], b[0], c[0])
        y0, y1 = min(a[1], b[1], c[1]), max(a[1], b[1], c[1])
        j0, j1 = np.searchsorted(xs, x0), np.searchsorted(xs, x1, side="right")
        i0, i1 = np.searchsorted(ys, y0), np.searchsorted(ys, y1, side="right")
        if j1 <= j0 or i1 <= i0:
            continue
        X, Y = np.meshgrid(xs[j0:j1], ys[i0:i1])
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-14:
            continue
        l1 = ((b[1] - c[1]) * (X - c[0]) + (c[0] - b[0]) * (Y - c[1])) / d
        l2 = ((c[1] - a[1]) * (X - c[0]) + (a[0] - c[0]) * (Y - c[1])) / d
        cov[i0:i1, j0:j1][(l1 >= 0) & (l2 >= 0) & (l1 + l2 <= 1)] = 1.0
    return cov.reshape(H, ss, W, ss).mean(axis=(1, 3)), (hi[0] - lo[0]) * sc


def plate_layout(W, H, px_per_m, numeral):
    """Tower plate layout in texels (W x H rect, rows bottom-up): border, glyph square, numeral.
    Returns (coverage arrays: field, ink) and the sizes DT-C7 checks."""
    import ski_launcher_glyph as G
    b = max(1, int(round(PLATE["border_m"] * px_per_m)))
    pad = max(2, int(round(PLATE["pad_m"] * px_per_m)))
    field = np.zeros((H, W), np.float32)
    field[b:H - b, b:W - b] = 1.0
    ink = np.zeros((H, W), np.float32)
    gs = H - 2 * (b + pad)
    glyph = G.render_alpha(G.cand_c(), gs, ss=8)[::-1]            # top-down -> bottom-up
    r0, c0 = b + pad, b + pad
    ink[r0:r0 + gs, c0:c0 + gs] = np.maximum(ink[r0:r0 + gs, c0:c0 + gs], glyph)
    x0 = c0 + gs + pad
    avail = W - b - pad - x0
    cap = int(round(0.85 * (H - 2 * (b + pad))))
    cov, wpx = _text_coverage(numeral, avail, H - 2 * b, cap)
    while wpx > avail - 2 and cap > 8:
        cap -= 1
        cov, wpx = _text_coverage(numeral, avail, H - 2 * b, cap)
    ink[b:H - b, x0:x0 + avail] = np.maximum(ink[b:H - b, x0:x0 + avail], cov)
    return field, ink, {"glyph_px": int(gs), "numeral_cap_px": int(cap), "border_px": int(b),
                        "rect_px": [int(W), int(H)]}


def make_plate_painter(numeral, plate_w, report, side):
    """stamp_on_face painter: field white 0.85 (sRGB), 6 mm black border, the resort glyph (left)
    and the height numeral (right) in black, coverage-blended (anti-aliased). ControlMask R / G / B
    zeroed over the whole plate (fixed colors, DT-C7)."""
    white = float(_srgb_to_lin(np.float64(PLATE["field"])))
    black = float(_srgb_to_lin(np.float64(0.04)))

    def painter(rect, base_sl, ctl_sl):
        r0, r1, c0, c1 = rect
        H, W = r1 - r0, c1 - c0
        ppm = W / plate_w
        field, ink, rep = plate_layout(W, H, ppm, numeral)
        col = black + (white - black) * field
        col = col * (1.0 - ink) + black * ink
        base_sl[..., 0:3] = col[..., None]
        ctl_sl[..., 0:3] = 0.0
        rep["px_per_m"] = round(ppm, 1)
        report[side] = rep
    return painter


def data_plate_decals(spec, parts):
    """Section 4.2 / 5.2: a data plate on leg_0's outer travel face, 1.5 m above the pedestal: a light
    plate in a dark frame (decals: frame first, then the plate). Authoring frame."""
    legw = W_TR["leg"] if spec.family == "tram" else W_GD["leg"]
    ped_h = PED_H_TR if spec.family == "tram" else PED_H_GD
    pts = [Vector(p) for p in parts.axes["leg_0"][0]]
    _P, frames = leg_frames(pts, XV)
    p, si = _poly_at_z(pts, ped_h + 1.5)
    t = (pts[si + 1] - pts[si]).normalized()
    nrm, _ny = _outer_faces(frames[si], CORNERS[0])
    u = t.cross(nrm).normalized()
    c = p + nrm * (legw / 2.0)
    field = np.ones((6, 10), np.float32)
    grey = tuple(float(_srgb_to_lin(np.float64(x))) for x in (0.78, 0.79, 0.80))
    dark = tuple(float(_srgb_to_lin(np.float64(x))) for x in (0.08, 0.08, 0.09))
    # plate size fits the leg face (gondola 0.22: 0.16 x 0.10; tram 0.40: the design's 0.25 x 0.15), a
    # 1.5 cm dark frame under it so it reads on galvanised steel too; the design's 4 text bars are
    # ~1 cm tall = 0.2-0.4 texel at 18-38 px/m, so not drawn
    hu, hv = (0.125, 0.075) if spec.family == "tram" else (0.08, 0.05)
    return [cs2_compose.Decal(tuple(c), tuple(u), tuple(t), hu + 0.015, hv + 0.015, field, dark, reach_m=0.05),
            cs2_compose.Decal(tuple(c), tuple(u), tuple(t), hu, hv, field, grey, reach_m=0.05)]


def dt_texture_stats(base, nrm, msk, ctl, dl, ds, part_map, ppm_map, table, hook):
    """Per PREFIX_TABLE class (covered texels of the composed, pre-weather maps): MaskMap A std,
    the tone fields' own std (DT-C9 as restated by M7), detail-normal tilt share > 2 deg (DT-C8)
    and the median px/m; per plate the stamp sizes and the ControlMask under it (DT-C7)."""
    pre_of = np.array(["-"] + [prefix_of(nm)[0] for nm in table], dtype=object)
    pm = np.asarray(part_map)
    n = nrm[..., 0:3] * 2.0 - 1.0
    tilt = np.sqrt(n[..., 0] ** 2 + n[..., 1] ** 2) > math.sin(math.radians(2.0))
    cls = pre_of[pm]
    out = {}
    for c in sorted(set(pre_of[1:])):
        m = (cls == c) & (pm > 0)
        k = int(m.sum())
        if k < 20:
            continue
        out[c] = {"texels": k, "A_std": round(float(msk[..., 3][m].std()), 4),
                  "ds_std": round(float(ds[m].std()), 4), "dl_std": round(float(dl[m].std()), 4),
                  "tilt_share": round(float(tilt[m].mean()), 3),
                  "ppm_med": round(float(np.median(ppm_map[m])), 1)}
    plates = {}
    for nm, r in (hook.plate_rects or {}).items():
        if r is None:
            continue
        r0, r1, c0, c1 = r
        side = nm[-1]
        rep = dict(hook.plates.get(side, {}))
        rep["rect"] = [int(r0), int(r1), int(c0), int(c1)]
        rep["ctl_RGB_max"] = round(float(ctl[r0:r1, c0:c1, 0:3].max()), 4)
        plates[nm] = rep
    return {"classes": out, "plates": plates, "decal_rects": len(hook.decal_rects)}


def fill_subtexel_faces(base, msk, ctl, mesh, A, part_map, table, uv_layer="UVUnique"):
    """Faces whose UV footprint contains NO texel center (islands under ~1 texel wide: gusset and
    kick-plate edges, rail end caps, the 8 mm plate rims, the arrest channels at their low weight)
    get no EMIT sample and, where no neighboring island's margin reaches, sample BLACK in game
    (measured: 43 such faces on the shipped 1d 60A, 131 on 40O). Paint the uncovered texels of
    their 1-texel footprint with their part's mean covered color (BaseColor, MaskMap,
    ControlMask: the slot tint still applies), or, for a part with no covered texel at all, the
    mean of its material's parts. Covered texels are never touched. Returns the stats."""
    uvs = cs2_compose.read_uvs(mesh, uv_layer)
    pid = cs2_compose.part_indices(mesh)
    pm = np.asarray(part_map)
    cov = pm > 0
    n_parts = len(table) + 1
    cnt = np.bincount(pm[cov], minlength=n_parts).astype(np.float64)
    means = {}
    for name_, arr in (("base", base), ("msk", msk), ("ctl", ctl)):
        m = np.zeros((n_parts, 4), np.float64)
        for ch in range(4):
            m[:, ch] = np.bincount(pm[cov], weights=arr[..., ch][cov].astype(np.float64), minlength=n_parts)
        means[name_] = m
    mat_of = {i + 1: prefix_of(nm)[1] for i, nm in enumerate(table)}
    by_mat = {}
    for p in range(1, n_parts):
        if cnt[p] > 0:
            acc = by_mat.setdefault(mat_of[p], [0.0, np.zeros((3, 4))])
            acc[0] += cnt[p]
            acc[1] += np.stack([means[k][p] for k in ("base", "msk", "ctl")])
    faces = texels = 0
    parts = {}
    for poly in mesh.polygons:
        ls, lt = poly.loop_start, poly.loop_total
        q = uvs[ls:ls + lt].astype(np.float64) * A
        x0 = max(0, int(math.floor(q[:, 0].min() - 1.0)))
        x1 = min(A, int(math.ceil(q[:, 0].max() + 1.0)))
        y0 = max(0, int(math.floor(q[:, 1].min() - 1.0)))
        y1 = min(A, int(math.ceil(q[:, 1].max() + 1.0)))
        if x1 <= x0 or y1 <= y0:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        if cs2_compose._poly_mask(q, X, Y, 0.0).any():
            continue                                    # the bake sampled this face
        foot = cs2_compose._poly_mask(q, X, Y, 1.0)
        if not foot.any():
            cx, cy = q.mean(axis=0)
            foot = (np.abs(X - cx) <= 0.5) & (np.abs(Y - cy) <= 0.5)
        sl = (slice(y0, y1), slice(x0, x1))
        tgt = foot & ~cov[sl]
        if not tgt.any():
            continue
        p = int(pid[poly.index])
        if cnt[p] > 0:
            vals = [means[k][p] / cnt[p] for k in ("base", "msk", "ctl")]
        else:
            acc = by_mat.get(mat_of.get(p))
            if acc is None:
                continue
            vals = list(acc[1] / acc[0])
        for arr, v in zip((base, msk, ctl), vals):
            view = arr[sl]
            view[tgt] = v.astype(np.float32)
        faces += 1
        texels += int(tgt.sum())
        k = prefix_of(table[p - 1])[0]
        parts[k] = parts.get(k, 0) + 1
    print(f"[dp1] sub-texel faces filled: {faces} faces, {texels} texels {dict(sorted(parts.items()))}")
    return {"faces": faces, "texels": texels, "by_prefix": dict(sorted(parts.items()))}


def make_post_compose_v2(spec, relief_net, tone_net, decals_net, table, plate_w):
    """Detail pass 1 finish hook (wrapped by the weathering layer in run_variant): relief (round L +
    edge rounds + marks), tone (mottle / chalk), compose_maps, normal renorm, then the tower plates
    (stamp_on_face per part: plate_n / plate_p, M3) and the data plate decal. Records the stats
    DT-C7 / C8 / C9 read."""
    numeral = str(spec.H)

    def hook(base, nrm, msk, ctl, ao, mesh, A):
        part_map, ppm_map = cs2_compose.rasterise_face_fields(mesh, A)
        h = cs2_compose.detail_height(mesh, A, relief_net, verbose=True)
        hook.relief = {k: dict(v) for k, v in cs2_compose.LAST_DETAIL_STATS.items()}
        dl, ds = cs2_compose.detail_tone(mesh, A, tone_net, verbose=True)
        hook.tone = {k: dict(v) for k, v in cs2_compose.LAST_TONE_STATS.items()}
        hook.tone_fields = (dl, ds, part_map)
        hook.height_range = (float(h.min()), float(h.max()))
        uncovered = nrm[..., 0:3].max(axis=-1) < 1e-3
        base, nrm, msk, ctl = cs2_compose.compose_maps(
            base, nrm, msk, ctl, None, h, ppm_map,
            {"reserved": (), "part_map": part_map, "seed": 0, "tone_l": dl, "tone_s": ds})
        n = nrm[..., 0:3] * 2.0 - 1.0
        n[..., 2] = np.maximum(n[..., 2], 0.05)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        nrm[..., 0:3] = n * 0.5 + 0.5
        nrm[uncovered, 0:3] = (0.5, 0.5, 1.0)
        nrm[..., 3] = 1.0
        hook.subtexel = fill_subtexel_faces(base, msk, ctl, mesh, A, part_map, table)
        hook.plates = {}
        hook.plate_rects = {}
        for side in ("n", "p"):
            nm = f"plate_{side}"
            hook.plate_rects[nm] = cs2_compose.uv_bbox_px(mesh, table, nm, A)
            cs2_compose.stamp_on_face(base, ctl, mesh, table, nm,
                                      make_plate_painter(numeral, plate_w, hook.plates, side))
        rects = []
        for dc in decals_net:
            rects += cs2_compose.decal_on_faces(base, ctl, mesh, A, table, "leg_0", dc, verbose=True)
        hook.decal_rects = sorted(set(rects))
        hook.dt = dt_texture_stats(base, nrm, msk, ctl, dl, ds, part_map, ppm_map, table, hook)
        return base, nrm, msk, ctl
    hook.height_range = None
    hook.decal_rects = []
    return hook


def weather_spec(spec, name, intensity):
    """DT8 / design 4.3 + 5.3: the tower preset (crashpad-only override, harmless here), free slot
    wear (DECISIONS 171), no snow; roles by material (concrete -> masonry; galv -> galv; the slot-1
    `paint` -> paint_steel on the gondola, galv on the galvanised tram (skeptic S3); dark -> paint_metal;
    crane -> paint; lens / rubber / plate -> none); rust sources on the hardware."""
    import cs2_weather as CW
    PW = CW.PartWeather
    tram = spec.family == "tram"
    extra = (("paint", "galv" if tram else "paint_steel"), ("dark", "paint_metal"), ("crane", "paint"),
             ("lens", "none"), ("rubber", "none"), ("plate", "none"))
    ov = [("bplate_", PW(rust_source=True)), ("lug_", PW(rust_source=True))]
    if tram:
        ov += [("collar_", PW(rust_source=True)), ("haulroll_", PW(rust_source=True)),
               ("haulroll_liner", PW(role="none", rust_source=False)), ("cranewinch_", PW(rust_source=True))]
    else:
        ov += [("flange_trans", PW(rust_source=True))]
    if cs2_sheaves.wheels_v2():             # T62 round 2: one clean wheel on every family (no rust source on it)
        extra += tuple(cs2_sheaves.wheel_roles().items())
        if tram:
            ov = [(n, PW(role="none", rust_source=False)) for n in ("haulroll_n_", "haulroll_p_")] + ov
    return CW.WeatherSpec.preset("tower", name, intensity=float(intensity), slot_wear="free", snow=0.0,
                                 mat_roles_extra=extra, overrides=tuple(ov))


class _WeatherProbe:
    """One bake, several intensities: the inner compose runs once, then the weathering layer runs
    per step on copies of its maps; the bake ships the `chosen` step. reports {I: report}."""

    def __init__(self, inner, steps, chosen, make):
        self.inner, self.steps, self.chosen, self.make = inner, tuple(steps), chosen, make
        self.reports = {}
        self.report = None

    def __getattr__(self, name):
        if name.startswith("__"):
            raise AttributeError(name)
        return getattr(self.__dict__["inner"], name)

    def __call__(self, base, nrm, msk, ctl, ao, mesh, A):
        b, n, m, c = self.inner(base, nrm, msk, ctl, ao, mesh, A)
        out = None
        for I in sorted(set(self.steps) | {self.chosen}, reverse=True):
            frozen = (b, n, m, c)
            wh = self.make(lambda *_a, f=frozen: tuple(np.array(x, copy=True) for x in f), I)
            res = wh(base, nrm, msk, ctl, ao, mesh, A)
            self.reports[I] = wh.report
            if abs(I - self.chosen) < 1e-9:
                out, self.report = res, wh.report
        return out


def make_weather(inner, spec, name, keys, relief_net, intensity, probe=None):
    """FP6 wrap (design 3.5 / 4.3 / 5.3): stamped = the plates PER PART (M3: plate_n / plate_p, never
    the 'plate_' union rect, which spans foreign islands), protect_px = the data-plate decal rects."""
    import cs2_weather as CW

    def mk(inner_fn, I):
        return CW.make_weather_hook(inner_fn, weather_spec(spec, name, I), mat_keys=keys,
                                    detail_specs=relief_net, stamped=["plate_n", "plate_p"],
                                    protect_px=lambda mesh, A: list(inner.decal_rects))
    if probe:
        return _WeatherProbe(inner, probe, intensity, mk)
    return mk(inner, intensity)


def weather_report(hook):
    """Manifest block: the shipped report (+ the probe ladder: ok / VL1 / VL2 / VL4 / dL* per step)."""
    import cs2_weather as CW
    rep = json.loads(CW.report_json(hook.report))
    keep = ("name", "A", "intensity", "slot_wear", "snow", "ground", "vanilla", "perceptual", "guards",
            "roles", "sources", "protect_texels", "role_source", "coverage", "seconds")
    out = {k: rep.get(k) for k in keep}
    probe = getattr(hook, "reports", None)
    if isinstance(probe, dict) and probe:
        out["probe"] = {f"{I:.2f}": {"ok": r["vanilla"]["ok"], "VL1": round(r["vanilla"]["VL1_mean_rel_dL"], 4),
                                     "VL2": round(r["vanilla"]["VL2_share_rel_dL_gt_0.15"], 4),
                                     "VL4": round(r["vanilla"]["VL4_max"], 3),
                                     "dLstar_mean": round(r["perceptual"]["mean_abs_dLstar"], 3),
                                     "share_dLstar_gt_2": round(r["perceptual"]["share_dLstar_gt_2"], 4)}
                        for I, r in sorted(probe.items(), reverse=True)}
    return out


def plate_share(t, table):
    """cs2_uv bay sharing (pack_unique share=): plate_p's front quad stacks on plate_n's island, so the
    two tower plates (identical stamps, DT7) cost the atlas ONE 300+ px/m island, not two (measured on
    the margin-bound lattice atlas: two separate plates cost 25O ~3 % of its member px/m). Loop k of
    each quad is the same viewer-relative corner (build_plate), so both read unmirrored (M3 test).
    Both stamps still run per part (M3); they paint the same island."""
    me = t.data
    pid = np.zeros(len(me.polygons), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", pid)
    ia = [int(i) for i in np.nonzero(pid == table.index("plate_n") + 1)[0]]
    ib = [int(i) for i in np.nonzero(pid == table.index("plate_p") + 1)[0]]
    if len(ia) != 1 or len(ib) != 1:
        raise ContractError(f"plate_share: plate quads {len(ia)} / {len(ib)} (want 1 / 1)")
    return [(ia, [ib])]


# Hardware stacked on one UV island per translation-identical group (cs2_uv bay sharing, the lifts
# group's API): the lattice atlas is MARGIN-bound (17-40 % covered; 6 px gutters round 570-980
# islands, a third of them rungs / sheaves / posts), so the pass's own new islands (collars,
# arrest rails, plates) cost the members 3-7 % px/m; stacking the identical hardware pays that back.
# Design 1.3: "identical repeats: rungs, sheaves, bolts (hardware: fine)". A stacked texel carries
# the last copy's AO / tone / weathering (cs2_bake); copies only stack within one height band
# (buried / the 0.6 m ground-grime band / above) so a grade rung never shows a buried rung's grime.
SHARE_PREFIXES = ("rung_", "sheave_", "haulroll_liner", "post_", "bplate_")
SHARE_TOL = 0.001


def hardware_share(t, table, ground_band=0.60):
    """[(canonical polys, [dup polys, ...]), ...] for cs2_bake.bake_unique(share=...): parts of the
    SHARE_PREFIXES (haulroll_ sheaves too, not their bar / ties) grouped when translation-identical
    (same loop counts, material, density weight, every loop's position relative to the part's bbox
    min within 1 mm, every corner normal within 0.01) in the same height band. Plus the tower plates
    (plate_share)."""
    me = t.data
    n = len(me.polygons)
    part = np.zeros(n, dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", part)
    start, total = cs2_uv.face_loop_table(me)
    mi = np.empty(n, dtype=np.int32)
    me.polygons.foreach_get("material_index", mi)
    wts = cs2_uv.face_density_weights(me)
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lv = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    cn = np.empty(len(me.loops) * 3, dtype=np.float32)
    me.corner_normals.foreach_get("vector", cn)
    cn = cn.reshape(-1, 3).astype(np.float64)
    order = np.argsort(part, kind="stable")
    bounds = np.searchsorted(part[order], np.arange(1, len(table) + 2))
    buckets = {}
    for p in range(1, len(table) + 1):
        nm = table[p - 1]
        ok = nm.startswith(SHARE_PREFIXES) or (nm.startswith("haulroll_") and nm[len("haulroll_"):][:1] in "pn"
                                                 and not nm.startswith(("haulroll_bar", "haulroll_tie")))
        if not ok:
            continue
        polys = np.sort(order[bounds[p - 1]:bounds[p]])
        if not len(polys):
            continue
        loops = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in polys])
        P = co[lv[loops]]
        zmin = float(P[:, 2].min())
        band = 0 if zmin < -0.5 else (1 if zmin < ground_band else 2)
        rel = P - P.min(axis=0)
        key = (nm.split("_")[0], tuple(int(total[f]) for f in polys), tuple(int(mi[f]) for f in polys),
               tuple(round(float(wts[f]), 4) for f in polys), band,
               np.round(rel / (SHARE_TOL * 10.0)).astype(np.int64).tobytes())
        buckets.setdefault(key, []).append((p, polys, rel, loops))
    share = []
    for key in sorted(buckets, key=lambda k: buckets[k][0][0]):
        left = list(buckets[key])
        while len(left) > 1:
            cp, cpolys, crel, cloops = left[0]
            dups, rest = [], []
            for m in left[1:]:
                good = float(np.abs(m[2] - crel).max()) <= SHARE_TOL and \
                    float(np.abs(cn[m[3]] - cn[cloops]).max()) <= 0.01
                (dups if good else rest).append(m)
            if dups:
                share.append(([int(f) for f in cpolys], [[int(f) for f in d[1]] for d in dups]))
            left = rest
    return share + plate_share(t, table)


def check_dp1_texture(spec, inner, whook, A, relief):
    """DT-C7 stamps, DT-C8 relief drawn, DT-C9 tone (M7 restated), DT-C10 weathering - on the
    stats the hooks recorded during the bake. Raises ContractError; returns the report."""
    dt = inner.dt
    rep = {"relief": inner.relief, "tone": inner.tone, "plates": dt["plates"],
           "decal_rects": dt["decal_rects"], "height_range_m": inner.height_range,
           "classes": dt["classes"], "subtexel_fill": inner.subtexel}
    print("[dp1] texture classes (ppm_med / tilt / A_std / ds_std / dl_std): " + "; ".join(
        f"{k} {v['ppm_med']}/{v['tilt_share']}/{v['A_std']}/{v['ds_std']}/{v['dl_std']}" for k, v in dt["classes"].items()))
    print(f"[dp1] plates {dt['plates']}")
    # DT-C7
    lat_min = (56, 48)
    for nm in ("plate_n", "plate_p"):
        p = dt["plates"].get(nm)
        if not p:
            _fail("DT-C7", f"{nm} not stamped")
        if A >= ATLAS[spec.family]:          # sizes are contracts at the shipping atlas only
            if p["px_per_m"] < 300 - 1e-6 or p["glyph_px"] < lat_min[0] or p["numeral_cap_px"] < lat_min[1]:
                _fail("DT-C7", f"{nm}: {p['px_per_m']} px/m, glyph {p['glyph_px']} px, numeral cap "
                               f"{p['numeral_cap_px']} px (want >= 300 / 56 / 48)")
        if p["ctl_RGB_max"] > 1e-6:
            _fail("DT-C7", f"{nm}: ControlMask {p['ctl_RGB_max']} under the plate (must be 0)")
    if dt["decal_rects"] < 1:
        _fail("DT-C7", "data plate decal painted no face")
    # DT-C8 (shipping atlas: the floors are density contracts)
    sk = inner.relief.get("skipped") or {}
    drawn = inner.relief.get("drawn") or {}
    want = {"edge_round", "marks:plate", "marks:bolts", "grate"}
    # T62 round 2: the gondola's "marks:ring" was the sheave hub ring only (the v2 wheel's hub is a geometric boss)
    want |= ({"bolts", "marks:slot", "seams"} if spec.family == "tram"
             else ({"marks:band"} if cs2_sheaves.wheels_v2() else {"marks:band", "marks:ring"}))
    missing = sorted(want - set(drawn))
    if A >= ATLAS[spec.family]:
        if sk:
            _fail("DT-C8", f"relief below the texel floor: {sk}")
        if missing:
            _fail("DT-C8", f"relief kinds not drawn: {missing}")
        cls = dt["classes"]
        flat = [k for k in relief if relief[k].active() and k in cls and cls[k]["tilt_share"] <= 0.0]
        if flat:
            _fail("DT-C8", f"relief prefixes with no tilted texel: {flat}")
    rep["relief_kinds_missing"] = missing
    # DT-C9 (M7 restated): every tone-drawn class that spans >= 30 independent noise cells (unique
    # texels / ((px/m x cell)^2 x stretch); stacked hardware counts once) carries what the noise
    # delivers - the smoothness field's std >= 0.6 x MOTTLE_STD x mottle_smooth (the sample std of
    # 30 cells scatters ~15 %), and MaskMap A std at least that; smaller classes (a 0.5 m base plate,
    # a 0.08 m haul bar under a 0.25 x 0.75 m cell) cannot show a population std: reported only
    cfg = TONE[spec.family]
    tsk = inner.tone.get("skipped") or {}
    low, small = {}, {}
    for pre, mat, _dw in PREFIX_TABLE:
        if pre.startswith(TONE_SKIP) or mat not in cfg or pre not in dt["classes"]:
            continue
        want_std = 0.6 * cs2_compose.MOTTLE_STD * cfg[mat][3]
        c = dt["classes"][pre]
        cells = c["texels"] / max((c["ppm_med"] * cfg[mat][0]) ** 2 * cfg[mat][1], 1e-9)
        if cells < 30:
            small[pre] = (c["ds_std"], round(cells, 1))
            continue
        if c["ds_std"] < want_std or c["A_std"] < want_std:
            low[pre] = (c["ds_std"], c["A_std"], round(want_std, 4))
    rep["tone_small_classes"] = small
    rep["tone_low"] = low
    if A >= ATLAS[spec.family]:
        if tsk.get("mottle"):
            _fail("DT-C9", f"tone cells below the 4-texel floor on {tsk['mottle']} faces")
        if low:
            _fail("DT-C9", f"tone std below what the noise delivers: {dict(list(low.items())[:5])}")
    # DT-C10
    if whook is not None:
        r = whook.report
        rep["weather"] = {"intensity": r["intensity"], "ok": r["vanilla"]["ok"],
                          "VL1": r["vanilla"]["VL1_mean_rel_dL"], "guards": r["guards"]}
        g = r["guards"]
        if not (g.get("G5_ctl_identity") and g.get("G6_protect_identical")):
            _fail("DT-C10", f"weather guards {g}")
        if r["slot_wear"] != "free" or float(r["snow"]) != 0.0:
            _fail("DT-C10", f"slot_wear {r['slot_wear']} / snow {r['snow']}")
        if not r["vanilla"]["ok"]:
            _fail("DT-C10", f"vanilla budget WARN at intensity {r['intensity']}: {r['vanilla'].get('failed')}")
    return rep


def night_rows(spec, t, table):
    """DT12 + M8 option 1: 60A only - the vanilla WarningLightRandom01 sub-object at the top center on
    gantryx_1 (net frame: x lateral 0, y = its top, z travel 0), identity rotation, parent mesh 0.
    It picks a 0.70 or 1.95 m pole at random, so the lamp stands 0.70 / 1.95 m above the head
    (outside the bounds and the pillar scoring; visible). Surgery: one ObjectSubObjects row on
    SkiTramTower60. None on the other variants."""
    cfg = NIGHT.get(spec.H) if spec.family == "tram" else None
    if not cfg:
        return None
    top = float(MeshView(t, table).verts(cfg["host_part"])[:, 2].max())      # z is up in both frames
    return [{"prefab": cfg["name"], "guid": cfg["guid"], "position_net_m": [0.0, round(top, 3), 0.0],
             "rotation_xyzw": cfg["rotation"], "parent_mesh": cfg["parent_mesh"],
             "host": cfg["host_part"], "random_pole_m": cfg["random_pole_m"],
             "lamp_top_m": [round(top + h, 2) for h in cfg["random_pole_m"]],
             "note": "outside the frozen prefab block; surgery adds one ObjectSubObjects row (U2: "
                     "unverified that a sub-object renders on a pillar object placed through the net)"}]


def shipped_density(obj, table, A):
    """Median / min shipped px/m per prefix on the UVUnique layer (the bake's own report reads the
    meter-strip source layer as if it were an atlas, so its source column is meaningless here)."""
    me = obj.data
    lay = me.uv_layers.get(cs2_uv.UNIQUE_LAYER)
    if lay is None:
        return {}
    dens = cs2_uv.face_px_per_m(me, lay, A)
    part = np.zeros(len(me.polygons), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", part)
    out = {}
    for pre, _m, _w in PREFIX_TABLE:
        pids = [i + 1 for i, nm in enumerate(table) if prefix_of(nm)[0] == pre]
        if not pids:
            continue
        d = dens[np.isin(part, pids) & np.isfinite(dens)]
        if d.size:
            out[pre] = {"median": round(float(np.median(d)), 1), "min": round(float(d.min()), 1)}
    return out


# --------------------------------------------------------------------------------------
# Previews (generator-local detail frames; MC4) - cs2_preview rig internals, headless
# --------------------------------------------------------------------------------------

def _to_net(p):
    return (p[1], -p[0], p[2])


def _standin_material(name, rgb):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.6
    return mat


def _standins(spec, rig, x_c=0.0):
    """Carrier boxes (level, at carrier position x_c, both sides) + rope lines, NET frame."""
    import cs2_preview  # noqa: F401
    objs = []
    lane = TRAM["lane_y"] if spec.family == "tram" else GD["lane_y"]
    boxes = CARRIER_TRAM if spec.family == "tram" else CARRIER_GD
    mat_c = rig.track_data(bpy.data.materials, _standin_material("StandinCarrier", (0.75, 0.32, 0.12)))
    mat_r = rig.track_data(bpy.data.materials, _standin_material("StandinRope", (0.08, 0.08, 0.09)))
    zref = spec.z0 - spec.s0 * abs(x_c)
    for s in (1, -1):
        for bn, (bx0, bx1), (dy0, dy1), (bz0, bz1), _al in boxes:
            cx = x_c + (bx0 + bx1) / 2
            cy = s * (lane + (dy0 + dy1) / 2)
            cz = zref + (bz0 + bz1) / 2
            o = cs2_kit.add_box(f"Standin_{bn}_{s}", (abs(dy1 - dy0), bx1 - bx0, bz1 - bz0),
                                _to_net((cx, cy, cz)))
            o.data.materials.append(mat_c)
            objs.append(rig.track(o))
    if spec.family == "tram":
        ropes = [(TRAM["lane_y"], 0.0, TRAM["rope_r_hv"]), (TRAM["haul_y"], TRAM["haul_dz"], TRAM["rope_r_lv"])]
    else:
        ropes = [(GD["lane_y"], 0.0, GD["rope_r_hv"]), (0.0, GD["comm_dA"], GD["rope_r_lv"])]
    k = 0
    for y, dz, r in ropes:
        for s in ((1, -1) if y > 0 else (1,)):
            pts = [_to_net((-60, s * y, spec.z0 + dz - spec.s0 * 60)), _to_net((0, s * y, spec.z0 + dz)),
                   _to_net((60, s * y, spec.z0 + dz - spec.s0 * 60))]
            o = prism(f"StandinRope_{k}", pts, r, 8, ZV)
            o.data.materials.append(mat_r)
            objs.append(rig.track(o))
            k += 1
    return objs


def render_details(t, lods, asset_dir, name, out_dir, spec, tint, ladder=None):
    """head_detail / base_detail / neck / passage_front / passage_34 / ladder frames / field frames
    (MC4; Revision 1 adds ladder_face, ladder_base and ladder_basket for verdicts 7 / 8). The mesh is
    already in the NET frame here: net (X, Y, Z) = authoring (y, -x, z), so the -X ladder face looks
    toward net +Y."""
    import cs2_preview as cp
    written = []
    os.makedirs(out_dir, exist_ok=True)
    z0 = spec.z0
    tram = spec.family == "tram"
    neck_z = spec.z_k if tram else spec.z_t
    views = [
        ("head_detail", (0, 0, z0 - 0.5), (1.0, -1.1, 0.35), 24.0 if tram else 13.0, 50.0, False),
        ("base_detail", (0, 0, 1.0), (1.0, -0.6, 0.15), 20.0 if tram else 10.0, 35.0, False),
        ("neck", (0, 0, neck_z), (0.2, -1.0, 0.1), 30.0 if tram else 16.0, 35.0, False),
        ("passage_front", (0, 0, z0 - 4.0), (0.08, -1.0, 0.12), 36.0 if tram else 15.0, 35.0, True),
        ("passage_34", (0, 0, z0 - 3.0), (1.0, -1.2, 0.30), 36.0 if tram else 15.0, 35.0, True),
    ]
    if ladder:
        face, s = ladder["face"], ladder["s"]
        f0 = ladder["flights"][0]
        hm = spec.H / 2.0

        def at(n, v, z):                 # ladder-local point -> net frame
            x, y = face_xy(face, n, v)
            return _to_net((x, y, z))

        def look(out, lat, up):          # camera direction: `out` away from the face, net frame
            x, y = face_xy(face, -out, lat)
            return (y, -x, up)
        views += [
            ("ladder_face", at(x_face(spec, hm) - s, 0.5, hm), look(1.0, -0.30, 0.10),
             spec.H * (1.25 if tram else 1.35), 35.0, False),
            ("ladder_base", at(x_face(spec, 1.5) - s, f0["line"], 2.0), look(1.0, -0.55, 0.30),
             11.0 if tram else 8.0, 35.0, False),
        ]
        if ladder["baskets"]:
            b = ladder["baskets"][0]
            views.append(("ladder_basket", at((b["n_in"] + b["n_out"]) / 2.0, (b["v0"] + b["v1"]) / 2.0,
                                              b["floor"] + 0.4), look(1.0, -0.75, 0.45),
                          8.0 if tram else 6.5, 35.0, False))
    rig = cp._Rig(t, None)
    try:
        cam = cp._make_camera(rig)
        sc = rig.scene
        world, sun = cp._make_world(rig, **cp.NOON)
        gnd = cp._make_ground(rig)
        mat = cp._pbr_material(rig, asset_dir, name, tint)
        cp._assign(t, mat)
        stand = []
        for vname, target, direction, dist, lens, with_carriers in views:
            for o in stand:
                o.hide_render = not with_carriers
            if with_carriers and not stand:
                stand = _standins(spec, rig)
            sc.render.resolution_x, sc.render.resolution_y = 1300, 1000
            cp._aim(cam, Vector(target), Vector(direction), dist)
            cam.data.lens = lens
            path = os.path.join(out_dir, f"{name}_{vname}.png")
            written.append(cp._render(rig, path, engine="EEVEE"))
        for o in stand:
            o.hide_render = True
        fl_v = variant_float(spec) if ladder else 0.0
        if fl_v > 0.0:                   # revision 1c (N1): ground lowered to this variant's max float
            gnd.location.z = -fl_v
            cp._aim(cam, Vector(at(x_face(spec, 1.5) - s, f0["line"], 1.5 - fl_v / 2.0)),
                    Vector(look(1.0, -0.55, 0.15)), (fl_v + 6.0) * 1.3)
            cam.data.lens = 35.0
            written.append(cp._render(rig, os.path.join(out_dir, f"{name}_ladder_float.png"), engine="EEVEE"))
            gnd.location.z = 0.0
        # field frames: LOD1 at 150 m, LOD2 at 300 m noon + dusk (silhouettes at Richard's distances)
        cam.data.lens = 12.0 / math.tan(math.radians(cp.VERTICAL_FOV_DEG) / 2.0)
        gnd.hide_render = True
        t.hide_render = True
        for k, lod in enumerate(lods, start=1):
            cp._assign(lod, mat)
        frames = [("field_150m_noon", 1, 150.0), ("field_300m_noon", 2, 300.0)]
        for fname, k, dist in frames:
            if k > len(lods):
                continue
            for j, lod in enumerate(lods, start=1):
                lod.hide_render = (j != k)
            cp._aim(cam, Vector((0, 0, spec.H / 2)), Vector((1.0, -1.2, 0.25)), dist)
            path = os.path.join(out_dir, f"{name}_{fname}.png")
            written.append(cp._render(rig, path, engine="EEVEE"))
        if len(lods) >= 2:              # same rig at dusk: swap the sun + sky for the DUSK set
            rig.objects.remove(sun)
            bpy.data.objects.remove(sun, do_unlink=True)
            sc.world = None
            cp._make_world(rig, **cp.DUSK)
            for j, lod in enumerate(lods, start=1):
                lod.hide_render = (j != 2)
            path = os.path.join(out_dir, f"{name}_field_300m_dusk.png")
            cp._aim(cam, Vector((0, 0, spec.H / 2)), Vector((1.0, -1.2, 0.25)), 300.0)
            written.append(cp._render(rig, path, engine="EEVEE"))
        for lod in lods:
            lod.hide_render = False
            lod.data.materials.clear()
    finally:
        rig.restore()
    t.data.materials.clear()
    return written


def import_fbx_metres(path, name):
    """Re-import an export_fbx file: raw cm, Y-up -> meters, Z-up (inverse of the export bake)."""
    before = set(bpy.data.objects)
    op = bpy.ops.wm.fbx_import if hasattr(bpy.ops.wm, "fbx_import") else bpy.ops.import_scene.fbx
    op(filepath=path)
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    if len(new) != 1:
        raise RuntimeError(f"import {path}: {len(new)} meshes")
    o = new[0]
    o.matrix_world = Matrix.Identity(4)
    M = Matrix(((0.01, 0, 0, 0), (0, 0, -0.01, 0), (0, 0.01, 0, 0), (0, 0, 0, 1)))
    o.data.transform(M)
    o.name = name
    return o


def render_lineup(family, built, out_dir):
    """--all: the three variants side by side (+ FG8N and a 1.8 m person; gondola: cabin boxes)."""
    import cs2_preview as cp
    objs = []
    for i, (name, asset_dir, spec) in enumerate(built):
        o = import_fbx_metres(os.path.join(asset_dir, f"{name}.fbx"), f"Lineup_{name}")
        o.location.x = (i - 1) * (22.0 if family == "tram" else 12.0)
        objs.append((o, asset_dir, name, spec))
    rig = cp._Rig(objs[0][0], None)
    try:
        for o, _d, _n, _s in objs[1:]:
            rig.track(o)
        cam = cp._make_camera(rig)
        cp._make_world(rig, **cp.NOON)
        tint = {"R": SLOT1_TINT[family]}
        for o, d, n, _s in objs:
            o.hide_render = False
            cp._assign(o, cp._pbr_material(rig, d, n, tint, mat_name=f"{n}_Lineup"))
        x_end = objs[-1][0].location.x + (14.0 if family == "tram" else 8.0)
        fg = os.path.join(STANDIN_FG8N, "SkiLiftTowerFG8N.fbx")
        if os.path.isfile(fg):
            f = rig.track(import_fbx_metres(fg, "Lineup_FG8N"))
            f.location.x = x_end
            cp._assign(f, cp._pbr_material(rig, STANDIN_FG8N, "SkiLiftTowerFG8N", None,
                                           mat_name="FG8N_Lineup"))
        person = rig.track(cs2_kit.add_cyl("Lineup_person", 0.25, 1.8, (x_end + 3.0, 0, 0.9), segments=12))
        person.data.materials.append(rig.track_data(bpy.data.materials,
                                                    _standin_material("Person", (0.8, 0.1, 0.1))))
        if family == "gondola":
            cm = rig.track_data(bpy.data.materials, _standin_material("Cabin", (0.75, 0.32, 0.12)))
            for o, _d, _n, s in objs:
                for sgn in (1, -1):
                    c = rig.track(cs2_kit.add_box(f"Lineup_cabin_{o.name}_{sgn}", (2.10, 2.30, 2.58),
                                                  (o.location.x + sgn * GD["lane_y"], 0,
                                                   s.z0 - GD["hang"] + 1.29)))
                    c.data.materials.append(cm)
        gnd = cp._make_ground(rig)          # hides the below-grade piers, as in game
        gnd.scale = (6.0, 6.0, 1.0)
        bpy.context.view_layer.update()
        lo = Vector((min(o.location.x for o, *_ in objs) - 10, -8, -1))
        hi = Vector((x_end + 6, 8, max(s.H for *_, s in objs)))
        centre = (lo + hi) / 2
        rad = (hi - lo).length / 2
        cam.data.lens = 12.0 / math.tan(math.radians(cp.VERTICAL_FOV_DEG) / 2.0)
        cp._aim(cam, centre, Vector((0.25, -1.0, 0.18)), rad / math.tan(math.radians(22.0)) * 1.05)
        rig.scene.render.resolution_x, rig.scene.render.resolution_y = 1600, 1000
        path = os.path.join(out_dir, f"{NAME_PREFIX[family]}_lineup.png")
        cp._render(rig, path, engine="EEVEE")
    finally:
        rig.restore()
        first = objs[0][0]
        me = first.data
        bpy.data.objects.remove(first, do_unlink=True)
        bpy.data.meshes.remove(me)
    return path


# --------------------------------------------------------------------------------------
# Manifest blocks
# --------------------------------------------------------------------------------------

def prefab_block(spec, lay):
    """PillarData / NetSubObjects top-anchor numbers the surgery reads (section 15, R27/R28/R31)."""
    if spec.family == "tram":
        z0 = spec.z0
        sizes = [float(h) for h in HEIGHTS["tram"]]
        spec_y = float(HEIGHTS["tram"][0])
        sweep = scoring_sweep(sizes, spec_y)
        corner_diag = (spec.B / 2) * math.sqrt(2.0)
        return {
            "tower_prefab": f"SkiTramTower{spec.H}", "mesh": f"SkiTramTower{spec.H}A",
            "placeholder": "SkiTramTowerPlaceholder (mesh SkiTramTower30A)",
            "utility_object": {"type": 8, "position": [0.0, round(z0, 3), 0.0]},
            "stubs": [
                _stub("tram_track", "HV track", [-TRAM["lane_y"], round(z0, 3), 0.0], 0),
                _stub("tram_track", "HV track", [TRAM["lane_y"], round(z0, 3), 0.0], 1),
                _stub("tram_haul", "LV haul", [-TRAM["haul_y"], round(z0 + TRAM["haul_dz"], 3), 0.0], 2),
                _stub("tram_haul", "LV haul", [TRAM["haul_y"], round(z0 + TRAM["haul_dz"], 3), 0.0], 3),
            ],
            "standing_object": {"m_LegSize": [round(spec.B + PW_TR, 3), round(spec.z_k, 3), round(spec.B + PW_TR, 3)],
                                "m_CircularLeg": False, "m_LegGap": [0, 0]},
            "pillar_object": {"m_Type": 2, "m_AnchorOffset": 0, "m_VerticalPillarOffsetRange": [-1, 1]},
            "terraform_override": "copy SkiLiftTowerFG12 (LevelMin (2,2), LevelMax (-2,-2), corners (1,1), DontRaise/DontLower)",
            "bounds_top": float(spec.H), "raw_bounds_min_y": -PIER_TR, "clamp_bounds_min_y": -0.01,
            "net_subobjects": {"m_Position": [0.0, spec_y, 0.0], "m_Placement": 0, "m_AnchorTop": False,
                               "note": "= SkiTramTower30A LOD0 bounds max y; re-read after import (PIPELINE 6.5)"},
            "top_anchor_y": spec_y, "elevation_range": [0, spec_y],
            "variant_scoring": {"sizes": sizes, "spec_y": spec_y, **sweep},
            "pier": {"depth": PIER_TR, "covers_float_m": sweep["max_float"],
                     "slope_allowance_m": round(PIER_TR - sweep["max_float"], 3),
                     "corner_diag_m": round(corner_diag, 3),
                     "grade_covered_on_diagonal": round((PIER_TR - sweep["max_float"]) / corner_diag, 3)},
            "color_slot1_default": list(SLOT1_TINT["tram"]),
            "piece_lanes": {"hv_track": [TRAM["lane_y"], round(HEIGHTS["tram"][0] - TRAM["h_top"], 3)],
                            "lv_haul": [TRAM["haul_y"], round(HEIGHTS["tram"][0] - TRAM["h_top"] + TRAM["haul_dz"], 3)],
                            "find_anchor": True, "comm": None, "lane_hanging": TRAM["lane_hanging"],
                            "note": "R31: own rope-lane prefabs with m_Hanging 0.004 (SkiLiftHaulRopeB sags 0.03)"},
        }
    A = spec.z0
    tub = list(GD["tubular_tops"])
    sizes = tub + [float(h) for h in HEIGHTS["gondola"]]
    spec_y = tub[0]
    sweep = scoring_sweep(sizes, spec_y)
    corner_diag = (spec.B / 2) * math.sqrt(2.0)
    lattice_float = max([0.0] + [round(spec_y + e - s, 3) for e, s in sweep["switches"] if s > 20.0])
    return {
        "tower_prefab": f"SkiGondolaLattice{spec.H}", "mesh": f"SkiGondolaLattice{spec.H}O",
        "placeholder": "the GD family placeholder (towers-v2 / gondola net lens)",
        "utility_object": {"type": 8, "position": [0.0, round(A, 3), 0.0]},
        "stubs": [
            _stub("gd_haul", "HV haul", [-GD["lane_y"], round(A, 3), 0.0], 0),
            _stub("gd_haul", "HV haul", [GD["lane_y"], round(A, 3), 0.0], 1),
            _stub("gd_comm", "LV comm", [0.0, round(A + GD["comm_dA"], 3), 0.0], 2),
        ],
        "standing_object": {"m_LegSize": [round(spec.B + PW_GD, 3), round(spec.z_t, 3), round(spec.B + PW_GD, 3)],
                            "m_CircularLeg": False, "m_LegGap": [0, 0]},
        "pillar_object": {"m_Type": 2, "m_AnchorOffset": 0, "m_VerticalPillarOffsetRange": [-1, 1]},
        "terraform_override": "copy SkiLiftTowerFG12",
        "bounds_top": float(spec.H), "raw_bounds_min_y": -PIER_GD, "clamp_bounds_min_y": -0.01,
        "net_subobjects": {"m_Position": [0.0, spec_y, 0.0], "m_Placement": 0, "m_AnchorTop": False,
                           "note": "spec y = the GD12 tubular bounds top (provisional 12.000, towers-v2 D1); the lattice adds nothing to it"},
        "top_anchor_y": spec_y, "elevation_range": [0, round(float(HEIGHTS['gondola'][-1]) - spec_y, 3)],
        "variant_scoring": {"sizes": sizes, "spec_y": spec_y, "tubular_sizes_provisional": tub, **sweep},
        "pier": {"depth": PIER_GD, "max_float_in_family_m": sweep["max_float"],
                 "slope_allowance_m": round(PIER_GD - sweep["max_float"], 3),
                 "corner_diag_m": round(corner_diag, 3),
                 "grade_covered_on_diagonal": round((PIER_GD - sweep["max_float"]) / corner_diag, 3)},
        "color_slot1_default": list(SLOT1_TINT["gondola"]),
        "h_top_gd": GD["h_top"],
        "note_h_top": "the GD tubular variants MUST use the same H_top (2.0) or the lanes jump between variants",
        "piece_lanes": {"hv": [GD["lane_y"], round(spec_y - GD["h_top"], 3)],
                        "lv_comm": [0.0, round(spec_y - GD["h_top"] + GD["comm_dA"], 3)],
                        "lane_hanging": GD["lane_hanging"]},
        "lattice_float_note": lattice_float,
    }


def _body_reach(boxes):
    """Outermost lateral reach |dy| of a carrier's `body` box (lane frame, outboard +): the
    cabin band the piece / carving width must cover (review minor 5: derived, never the
    contract's 1.85 / 1.09)."""
    body = next(b for b in boxes if b[0] == "body")
    return max(abs(body[2][0]), abs(body[2][1]))


def carving_block(spec):
    """MC7: carving numbers for the piece lens (terrain-carving section 2: ceiling = chord + min - 2)."""
    if spec.family == "tram":
        z_line = HEIGHTS["tram"][0] - TRAM["h_top"]          # rope above a ground node (30 m tower)
        under = z_line - TRAM["rope_above_floor"]
        half = _body_reach(CARRIER_TRAM)                     # 1.90 (built cabin +-1.88)
        lane_band = (round(TRAM["lane_y"] - half, 3), round(TRAM["lane_y"] + half, 3))
        return {
            "rope_above_ground_node_m": z_line, "cabin_underside_above_chord_m": under,
            "height_range_min_max_for_clearance_m": round(under + 2.0, 3),
            "midspan_reach_chord_at_span_m": {"h_0.004 (R31)": round(under / 0.004, 1),
                                              "h_0.03 (vanilla)": round(under / 0.03, 1)},
            "cabin_band_abs_x_m": lane_band,
            "piece_width_needed_m": round(2 * lane_band[1], 2),
            "note": "min only protects the cabin when the piece is at least this wide (cut = piece width + margin)",
        }
    A = HEIGHTS["gondola"][0] - GD["h_top"]
    line_A = GD["tubular_tops"][0] - GD["h_top"]
    under = line_A - GD["hang"]
    return {"line_axle_m": line_A, "cabin_underside_above_chord_m": under,
            "height_range_min_max_for_clearance_m": round(under + 2.0 - 0.5, 3),
            "midspan_reach_chord_at_span_m": {"h_0.004 (R31)": round(under / 0.004, 1),
                                              "h_0.03 (vanilla)": round(under / 0.03, 1)},
            "piece_width_needed_m": round(2 * (GD["lane_y"] + _body_reach(CARRIER_GD)), 2),
            "lattice_axle_25m": A}


# --------------------------------------------------------------------------------------
# One variant, end to end
# --------------------------------------------------------------------------------------

def _jsonable_layout(lay):
    def conv(v):
        if isinstance(v, float):
            return round(v, 4)
        if isinstance(v, (list, tuple)):
            return [conv(x) for x in v]
        return v
    return {k: conv(v) for k, v in lay.items()}


def variant_name(family, H, letter=None):
    return cs2_asset.validate_asset_name(f"{NAME_PREFIX[family]}{H}{letter or LETTER[family]}")


def build_variant(family, H, args=None, finish=False, texture_root=None):
    """Geometry -> joined + shaded LOD0 in the authoring frame. Returns (t, table, spec, parts, lay)."""
    spec = make_spec(family, H, args)
    parts, lay = build_parts(spec)
    beacon = bool(getattr(args, "beacon", False))
    t, table, sources = assemble(parts, family, finish=finish, beacon=beacon, texture_root=texture_root,
                                 H=spec.H)
    return t, table, spec, parts, lay, sources


def run_variant(family, H, args, catalog_drift):
    t_start = time.perf_counter()
    name = variant_name(family, H, args.letter)
    clear_scene()
    texture_root = prefilter_sets() if args.finish else None
    t, table, spec, parts, lay, sources = build_variant(family, H, args, finish=args.finish,
                                                       texture_root=texture_root)
    t.name = name
    t.data.name = name
    rep_a, mv = check_contracts(t, table, spec, parts, "authoring")
    sweep = grade_sweep(mv, parts, spec)
    tally = mv.tally()
    cs2_asset.orient_for_net(t)
    rep_n, _mv = check_contracts(t, table, spec, parts, "net")
    l1p, l2p = lod_lists(family)
    n1, n2 = count_after_delete(t, table, l1p), count_after_delete(t, table, l2p)
    n0 = cs2_asset.tri_count(t)
    print(f"[tram_tower] {name}: LOD0 {n0} tris, LOD1 {n1}, LOD2 {n2}; parts {len(table)}; "
          f"z0 {spec.z0:.2f}; g_clear {sweep['structure']} / support {sweep['rope_support']}")
    print(f"[tram_tower] {name} tally: {tally}")
    print(f"[tram_tower] {name} grade sweep (first clashing g per carrier box, structure / support): "
          + ", ".join(f"{bn} {v['structure']}/{v['rope_support']} {v['parts']}"
                      for bn, v in sweep["by_box"].items()))
    if not args.export:
        lods = make_lods(t, name, table, parts, family, n1, n2) if args.lods else []
        c9 = check_c9(t, lods, spec)
        for l in lods:
            me = l.data
            bpy.data.objects.remove(l, do_unlink=True)
            bpy.data.meshes.remove(me)
        print(f"PASS tram_tower (dry run): {name} tris {c9['tris']} drift {c9['drift_m']}")
        return {"name": name, "tris": c9["tris"], "g_clear": sweep, "dry_run": True}
    if not args.finish:
        raise ContractError("--export needs --finish (the shipped textures come from the bake)")
    out_root = os.path.abspath(args.out or DEFAULT_OUT[family])
    asset_dir = os.path.join(out_root, name)
    os.makedirs(asset_dir, exist_ok=True)
    for f in os.listdir(asset_dir):          # own asset folder only: start clean
        p = os.path.join(asset_dir, f)
        if os.path.isfile(p) and f.startswith(name):
            os.remove(p)
    p_settings = os.path.join(asset_dir, "settings.json")
    if os.path.isfile(p_settings):
        os.remove(p_settings)
    import cs2_bake
    A = int(args.atlas or ATLAS[family])
    # detail pass 1: geometry contracts DT-C3..C6 on LOD0 (before the bake: the UV source layer)
    dp1_geo = check_dp1_geometry(t, table, spec, parts, lay, uv_layer="UVMap", net=True)
    relief = relief_specs(spec, lay, parts, table)
    relief_net = to_net(relief)
    tone_net = tone_specs(spec, table)
    decals_net = cs2_compose.transform_marks(data_plate_decals(spec, parts), NET_ROT)
    keys = [m.name[len(cs2_bake.SRC_MAT_PREFIX):] for m in t.data.materials]   # BEFORE bake_unique
    inner = make_post_compose_v2(spec, relief_net, tone_net, decals_net, table, PLATE[family]["w"])
    hook = inner
    w_int = WEATHER_I[family]
    if args.weather != "off":
        hook = make_weather(inner, spec, name, keys, relief_net, w_int,
                            probe=WEATHER_STEPS if args.weather_probe else None)
    # 6 px at the shipped atlas; scaled for smaller test atlases (500+ islands cannot keep 6 px
    # gutters at 256 px: the packer overflows the unit square)
    margin_px = max(2, int(round(PACK_MARGIN_PX * A / ATLAS[family])))
    bake = cs2_bake.bake_unique(t, asset_dir, name, A, sources, reserved=(), margin_px=margin_px,
                                ao_samples=int(args.ao_samples), post_compose=hook,
                                emissive=bool(args.beacon), src_uv="UVMap", share=hardware_share(t, table))
    dp1_tex = check_dp1_texture(spec, inner, hook if hook is not inner else None, A, relief)
    cs2_asset.export_fbx(t, os.path.join(asset_dir, f"{name}.fbx"))
    lods = []
    if args.lods:
        lods = make_lods(t, name, table, parts, family, n1, n2)
    c9 = check_c9(t, lods, spec)
    dp1_lod = {}
    for k, lod in enumerate(lods, start=1):
        cop = coplanar_overlaps(lod)
        wp = max(cop.values()) if cop else 0.0
        dp1_lod[f"LOD{k}"] = {"coplanar_same_total_cm2": round(sum(cop.values()), 2), "worst_pair_cm2": wp,
                              "smooth_member_edges": lod.get("dp1_smoothed")}
        if wp > COPLANAR_MAX_CM2:
            _fail("DT-C3", f"LOD{k} same-facing coplanar {wp} cm2: {list(cop.items())[:3]}")
    for k, lod in enumerate(lods, start=1):
        _mvk = MeshView(lod, table, net=True)
        _close(_mvk.V[:, 2].max(), float(spec.H), 1e-3, "C1", f"LOD{k} bounds top")
        _close(_mvk.V[:, 2].min(), -(PIER_TR if family == "tram" else PIER_GD), 1e-4, "C2",
               f"LOD{k} bounds bottom")
        cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{k}.fbx"))
    cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    tint = {"R": SLOT1_TINT[family]}
    previews = []
    if not args.no_preview:
        import cs2_preview
        prev = os.path.abspath(args.preview or DEFAULT_PREVIEW[family])
        os.makedirs(prev, exist_ok=True)
        modes = ("pbr", "dusk", "normals", "density") + (("lod",) if lods else ())
        previews += cs2_preview.preview(t, asset_dir, name, prev, modes=modes,
                                        views=cs2_preview.DEFAULT_VIEWS, lods=lods or None, tint=tint)
        t.data.materials.clear()
        previews += render_details(t, lods, asset_dir, name, prev, spec, tint, ladder=parts.ladder)
    import cs2_preview
    lo, hi = _vbounds(t)
    area = float(cs2_uv.face_world_areas(t.data).sum())
    switch = cs2_preview.lod_switch_table(tuple(hi - lo), c9["tris"], area=area)
    density = shipped_density(t, table, A)
    cs2_validate.write_manifest(asset_dir, name, {
        "generator": "tram_tower.py", "git_sha": git_sha(), "family": family, "H": spec.H,
        "letter": args.letter or LETTER[family], "args": {k: v for k, v in vars(args).items()},
        "rulings": ["R27", "R28", "R30", "R31"], "contract": "design/lattice-towers.md + skeptic MC1-MC10",
        "revision": {"n": 1, "date": "2026-10-11",
                     "verdicts": "verdicts-2026-10-11.md items 7 / 8 (ref-liftblog digest 1.3-1.5)",
                     "changes": ["ground-to-head ladder outside the -X face, side baskets, offset flights",
                                 "curved saddle girders + haul sheave row" if family == "tram" else
                                 "lifting lugs under the gantry-beam tips over the lanes",
                                 "upper stabilising frame + slewing jib cranes + lugs" if family == "tram"
                                 else "gondola sheave trains unchanged (monocable)"]},
        "tris": dict(enumerate(c9["tris"])), "budget": {k: c9[k] for k in ("lod0_target", "lod0_cap",
                                                                         "lod1_target", "lod2_warn", "lod2_cap")},
        "budget_warnings": c9["warnings"], "lod_drift_m": c9["drift_m"],
        "parts": len(table), "tally": tally,
        "layout": _jsonable_layout(lay),
        "datum": {"z0": round(spec.z0, 4), "h_top": TRAM["h_top"] if family == "tram" else GD["h_top"],
                  "end_slope": spec.s0},
        "contracts": {"authoring": rep_a, "net": rep_n},
        "g_clear": sweep,
        "atlas": A, "bake": {"margin_px": bake.get("margin_px"), "ao": bake.get("ao"),
                             "uncovered_texels": bake.get("uncovered_texels"),
                             "pack_px_per_m": (bake.get("pack") or {}).get("px_per_m"),
                             "islands": (bake.get("pack") or {}).get("islands"),
                             "height_range_m": hook.height_range, "prefilter": "box x4 (R24-style)"},
        "density_px_per_m": density,
        "lod_switch_m": switch, "surface_area_m2": round(area, 1),
        "prefab": prefab_block(spec, lay), "carving": carving_block(spec),
        "catalog_drift": catalog_drift,
        "previews": [os.path.basename(p) for p in previews],
        "detail_pass": {"n": 1, "date": "2026-10-11",
                        "contract": "design/detail-pass-1-towers.md sections 4-6 + skeptic M1-M11 "
                                    "(build/detail-pass-1-towers-lattice.md)",
                        "geometry": dp1_geo, "lods": dp1_lod, "texture": dp1_tex,
                        "ledger_lod0": c9["tris"][0], "relief_prefixes": sorted(k for k, v in relief.items() if v.active())},
        "weather": weather_report(hook) if hook is not inner else None,
        "night_subobjects": night_rows(spec, t, table),
    })
    if not args.no_validate:
        t.name, t.data.name = name + "Src", name + "Src"
        for k, lod in enumerate(lods, start=1):
            lod.name, lod.data.name = f"{name}LOD{k}Src", f"{name}LOD{k}Src"
        b = budget(spec)
        findings = cs2_validate.validate_asset_folder(asset_dir, name, {
            "kind": "hanging", "lod0_cap": b["lod0_cap"], "lod1_ratio": 0.5, "lod2_cap": b["lod2_cap"],
            "hero": False, "expected_lods": tuple(range(1, len(lods) + 1)), "expected_submeshes": ()})
        n_err = sum(1 for f in findings if f[0] == "ERROR")
        n_warn = sum(1 for f in findings if f[0] == "WARN")
        print(f"[tram_tower] validate_asset_folder {name}: {n_err} errors, {n_warn} warnings")
    if args.review is not None:
        import cs2_review
        cs2_review.write_pack(name, asset_dir, dict(enumerate([t] + lods)), {},
                              meta={"generator": "tram_tower.py", "args": vars(args), "kind": "tower",
                                    "caps": {0: budget(spec)["lod0_cap"], 1: budget(spec)["lod1_target"],
                                             2: budget(spec)["lod2_cap"]},
                                    "contracts": {"rope_z": spec.z0,
                                                  "lane_x": TRAM["lane_y"] if family == "tram" else GD["lane_y"],
                                                  "h_top": TRAM["h_top"] if family == "tram" else GD["h_top"],
                                                  "top_anchor_y": prefab_block(spec, lay)["top_anchor_y"],
                                                  "pier": PIER_TR if family == "tram" else PIER_GD},
                                    "tint": {1: SLOT1_TINT[family]}},
                              out_dir=args.review or None)
    for lod in lods:
        me = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(me)
    dt = time.perf_counter() - t_start
    print(f"PASS tram_tower: {name} tris {c9['tris']} drift {c9['drift_m']} g_clear "
          f"{sweep['structure']}/{sweep['rope_support']} -> {asset_dir} ({dt:.0f} s)")
    return {"name": name, "asset_dir": asset_dir, "spec": spec, "tris": c9["tris"], "g_clear": sweep}


# --------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------

def parse_args(argv=None):
    argv = sys.argv[sys.argv.index("--") + 1:] if argv is None and "--" in sys.argv else (argv or [])
    ap = argparse.ArgumentParser(prog="tram_tower.py")
    ap.add_argument("--family", choices=FAMILIES, default="tram")
    ap.add_argument("--height", type=int, default=None)
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--out", default=None)
    ap.add_argument("--letter", default=None)
    ap.add_argument("--finish", action="store_true")
    ap.add_argument("--no-finish", dest="finish", action="store_false")
    ap.add_argument("--lods", action="store_true", default=True)
    ap.add_argument("--no-lods", dest="lods", action="store_false")
    ap.add_argument("--export", action="store_true")
    ap.add_argument("--review", nargs="?", const="", default=None)
    ap.add_argument("--preview", nargs="?", const="", default=None,
                    help="preview folder (no value = the default folder); previews are on unless --no-preview")
    ap.add_argument("--no-preview", action="store_true")
    ap.add_argument("--beacon", action="store_true",
                    help="M8 fallback only (not shipped): build the 60A lens + ring and bake it emissive")
    ap.add_argument("--weather", choices=("on", "off"), default="on",
                    help="detail pass 1: the cs2_weather layer (on = shipping; off = the G5 / DT-C10 reference)")
    ap.add_argument("--weather-probe", action="store_true",
                    help="also evaluate the WEATHER_STEPS ladder in the same bake and report it (manifest)")
    ap.add_argument("--atlas", type=int, default=None)
    ap.add_argument("--ao-samples", type=int, default=AO_SAMPLES)
    ap.add_argument("--saddle-l", type=float, default=SADDLE_L_DEFAULT)
    ap.add_argument("--no-lattice-ladder", action="store_true",
                    help="gondola: omit the ground-to-platform ladder system (on by default since Revision 1)")
    ap.add_argument("--lattice-ladder", action="store_true", help="deprecated no-op (the ladder is the default)")
    ap.add_argument("--no-validate", action="store_true")
    ap.add_argument("--wheels", choices=("v1", "v2"), default="v2",
                    help="T62 sheave wheel generation: v2 (default) = cs2_sheaves.wheel (round 3: the 16-segment "
                         "grooved wheel) on the haul rollers and the lattice GD head; v1 = the pre-T62 sheave_lite wheels byte for byte")
    ap.add_argument("--lineup", action="store_true",
                    help="only re-render the family lineup from the exported asset folders")
    a = ap.parse_args(argv)
    if a.height is not None and a.height not in HEIGHTS[a.family]:
        ap.error(f"--height must be one of {HEIGHTS[a.family]} for {a.family}")
    if a.saddle_l < 2.2 or a.saddle_l > 7.0:
        ap.error("--saddle-l must be within 2.2 .. 7.0 m")
    return a


def main():
    args = parse_args()
    cs2_sheaves.set_wheels(args.wheels)      # T62: before any part is built
    _rows, drift = load_catalog()
    if args.lineup:
        clear_scene()
        out_root = os.path.abspath(args.out or DEFAULT_OUT[args.family])
        built = [(variant_name(args.family, H, args.letter),
                  os.path.join(out_root, variant_name(args.family, H, args.letter)),
                  make_spec(args.family, H, args)) for H in HEIGHTS[args.family]]
        p = render_lineup(args.family, built, os.path.abspath(args.preview or DEFAULT_PREVIEW[args.family]))
        print(f"PASS tram_tower: lineup -> {p}")
        return
    if args.all:
        heights = HEIGHTS[args.family]
    else:
        heights = (args.height or HEIGHTS[args.family][-1],)
    results = []
    for H in heights:
        results.append(run_variant(args.family, H, args, drift))
    if args.all and args.export and not args.no_preview:
        clear_scene()
        built = [(r["name"], r["asset_dir"], r["spec"]) for r in results]
        p = render_lineup(args.family, built,
                          os.path.abspath(args.preview or DEFAULT_PREVIEW[args.family]))
        print(f"[tram_tower] lineup -> {p}")
    print("PASS tram_tower: " + "; ".join(f"{r['name']} {r['tris']} g_clear "
                                          f"{r['g_clear']['structure']}/{r['g_clear']['rope_support']}"
                                          for r in results))


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        import traceback
        traceback.print_exc()
        sys.exit(1)
