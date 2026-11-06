"""
lift_cabin.py - carrier generator for the GONDOLA 8-pax and the TRAM 100-pax (cabins/A),
asset round 2026-10 (ROUND-SPEC P2 / P5).

Contract: docs/research/art/assets-2026-10/design/cabins.md, with every MUST-CHANGE of
design/cabins-skeptic.md applied and the overnight rulings R27-R31 (ROUND-SPEC.md) overriding
the contract where they conflict. Build report: docs/research/art/assets-2026-10/build/lift_cabin.md.

Run (headless only - generator code never runs in the live Blender, R10):

    blender --background --factory-startup --python-exit-code 1 --python lift_cabin.py -- \\
        --kind gondola|tram [--out assets/import/cabins/A] [--export]
        [--name NAME] [--source gondola.fbx] [--carriers catalog/carriers.json]
        [--finish | --draft] [--lods] [--verify] [--preview [DIR]] [--review [DIR]]
        [--plate-number 27] [--outboard=+Y|--outboard=-Y] [--lod2 proxy|decimate] (gondola)
        [--hang 10.50|7.00] [--split]                                         (tram)
        [--split --envelope [--remap-from assets/import/cabins/B]]            (gondola, B2 v2)
        [--ab DIR] [--no-validate]

    Shipping: --kind gondola --finish --lods --verify --export --preview --review
              --kind tram    --finish --lods --verify --export --preview --review
    Without --export NOTHING is written under assets/import: the asset folder goes to
    <tempdir>/lift_cabin/<Name> (skeptic should-fix: drafts never land where they can be
    imported by mistake). --finish = full bake (gondola 1024 / AO 32, tram 2048 / AO 48,
    relief); otherwise the draft bake (256 px, AO 8, no relief) - stamps are always painted.

GONDOLA `SkiGondolaCabin8A` (cabins.md section 3)
    Wilin_Amaurea's cabin shell (gondola.fbx, CC BY 4.0, credit below) ingested with
    cs2_ingest: subtree "cabin.001", keep the shell + both door leaves (door details, footstep,
    fasteners, box_top dropped = doors simplified), x27, reframe forward -X (180 deg yaw, no
    mirror: doors + stalk to authoring +Y, C4; since nitpick A3 the finished cabin is then
    mirrored to -Y = OUTBOARD, see GD_OUTBOARD), plan scale (0.8779, 0.9091),
    piecewise z remap to 2.40, clean (739 -> 571), the 3 shell holes (door opening + two
    door-side corner holes) filled = part doorplug_ (17 tris), split by material into
    body_/roof_/trim_/hw_/glass_ (+ door*), every ingested part RE-UV'd into exactly one layer
    "UVMap" with the Maya "map1" layer removed (skeptic MC3, asserted). Rebuilt (Revision 1
    shapes, see below): roof rails, the traced hanger (one 12-segment tube A -> B -> D),
    hanger head, springs, carriage frame, 8 rollers (10 segments, 30 tris each), grip
    (top = bounds max 4.50 = hang), bumpers, roof hat, number plate, two end-skirt logo
    quads fitted to the shell. Painted glass (R8). LOD1 = protected-prefix
    decimation <= 0.48 x LOD0; LOD2 = authored proxy (~204 tris, UVs snapped to LOD0) - the
    --lod2 decimate A/B fails G3/G6 by measurement (the protected contract parts alone are
    ~244 tris > the ~200 budget), which is C6's reason for the proxy.
    Hang 4.50 floor underside -> grip top; rope 4.42 (grip_top_above_rope 0.08, C5).

TRAM `SkiTramCabinA` (cabins.md section 4 as reconciled by R27-R30)
    R27 hang 10.50 (floor underside -> carriage top, bounds max), track rope 0.50 below the
        carriage top (rope 10.00); --hang 7.00 keeps the compact fallback (rope 6.50).
    R28 ONE track rope per side = the HV rider lane at x +-5.5: the carriage rides it with two
        bogies x four wheels ON THAT ROPE (wheel groove bottom on the drawn HV rope's top,
        +0.0866); the haul rope is a non-HV lane at +-5.0, 0.40 below -> haul grips at dy +-0.50,
        dz -0.40.
    R29 front/back AND left/right symmetric (doors on both long sides, mirror-asserted), parked
        identity in both base bays, pivot at the floor underside, never yawed 180: so the
        haul grips sit on both sides and the hanger is laterally symmetric - ONE centered
        box-section column on the roof fin, forked below the saddle band into two uprights
        that pass the rope outside the tower keep-out and meet the carriage above the rope.
    R30 at saddle height the carriage/hanger half-width is <= 1.10 (uprights 0.82..1.08); the
        carrier leaves the tower its envelope: nothing at |dy| < 0.36 for dz -0.80..-0.05 and
        nothing at |dy| < 0.80 for dz -3.80..-0.70 (crossbar below dz -3.80).
    Body ~8.0 x 3.7 x 3.0 rounded-rectangle loft (JH bands), mullions, one centered double door
    per long side (Revision 2), 6 logos, roof equipment + gratings + 2 hatches, ladder, end
    bumpers; 2048 set. LOD1 AUTHORED (details deleted, the eight
    16-segment wheels swapped for 8-segment wheels at the same seats, UVs snapped; COLLAPSE on
    the identical rims was not reproducible build to build), LOD2 protected-prefix decimation.
    --split: SkiTramCabinA (everything below the carriage pin K) + SkiTramCarriageA (carriage,
    pivot K = (0, 0, hang - 0.12)), each its own 4-map set (cabins.md section 4.8).

REVISION 1 (first live review 2026-10-11, verdicts items 6 / 9: "a bit blocky ... straight
outta CAD - a cleanup pass"; "the icon is great, reuse it, but a tiny bit low-res"; tram "same
critique"). Every contract number above is unchanged (hang / rope / UtilityObject / bounds / hosted
poses; seams S5 / S6 and the station T8 sweep pin them). Gondola: shell crown (3 seg) / skirt foot
(2 seg) / window-frame (1 seg) bevels on the welded shell + plug; door leaves inset 14 mm (dark
reveal) with grab handles; ONE 12-segment hanger tube on a foot block and a saddle clamp, a rounded
hanger head, coil springs, a rounded carriage frame, 10-segment rollers, an L-section grip prism;
rounded bumpers and roof hat; plate and logos as plaques. Tram: rounded boat bottom and roof crown,
glazing set 45 mm behind its frames, rounded box sections, dished / flanged / grooved wheels,
lighter bogie plates, rounded roof equipment with seams. Both: no coplanar overlapping faces, sub-5
cm detail in the normal map, metric UVs (no stretched texels), textures 2048 (gondola bumped from
1024 by the density report, R18), and the STAMP STRIPE (stamp_layout): every logo quad shares one
pinned square >= 4x the shipped logo, hero faces pinned and skyline-packed >= 120 px/m.

REVISION 1b (2026-10-11, build/revision1-check-cabins.md defects C1-C4; the look is unchanged):
C1 / C2 builder caps are polyfilled on their own normals (triangulate_caps; every cap held to
cap_check, a signed-area test); C3 the end-window frame-lip bevel skips the > 90 deg folds against
the door-plug corner fill and welds the one collapsed corner, so the shell + plug island stays
closed (asserted in soften_shell and as G11); C4 grip stem, fork crossbar ends and cliplinks sit
2 mm inside the faces they were flush with. G12 / T12: no same-facing coplanar overlap > 1 cm2.

REVISION 2 (2026-10-11, tram only; the gondola build is untouched): after approving both
looks - "gondola has some details that the tram doesn't now (door handles, bolts etc), also a tram
would have a pair of sliding doors on each side in the middle, opening outwards". tram_door_kit: one
outward-opening sliding (plug) double door per long side, centered, CLOSED: leaves 0.838 wide from
the threshold (0.32 = the station boarding deck + 2 cm) to the glazing head (2.38), faces 15 mm
PROUD of the wall (the exported lateral bound 1.880 is the mullions'), rubber jamb seals and meeting
nosings (the gap line), leaf windows in rubber gaskets 25 mm behind the face, grab bars in recessed
pockets, release pulls, a header runner box with two rubber-tired hanger rollers per leaf, a safety-
yellow threshold nosing, a drip rail, intercom boxes; the main-row center mullion became the seam,
+-1.25 moved to +-1.70, the side logo moved to the panes either side (2 per side). Gondola parity:
EVERY Revision 1 tram bolt row was below the 2-texel floor (509 faces skipped, measured): tram-scale
heads + heavier density weights draw them now; hanger pin boss + nut on the carriage head; roof
hatches with hinges; end bumpers. The rubber parts and the trim join the pinned constant stripes so
the margin-bound galv pack keeps its island count. T13 asserts the doors (LOD0 / LOD1); LOD1 keeps
the leaves (doors read closed), LOD2 drops every door part (the body behind is unchanged).

NITPICK A3 (2026-10-13, gondola only; the tram is untouched; orchestrator ruling F1 on
build/nitpick-a2-hanger-gondola.md; build/nitpick-a3-cabin-nose.md): every station is built round
the up lane on the RIGHT looking uphill and the mover yaws every carrier to face its travel
(prefab +z = authoring +X), so a carrier's OUTBOARD side is its RIGHT = authoring -Y in both x-sign
branches (the DQ chair's lift_chair.DQ_OUTBOARD -1, nitpick A2). The approved cabin carried its
stalk, carriage, grip and doors on +Y (C4 read the lens's "forward -X"): on the stations' spin that
is its LEFT - inboard, through every GD head's rocker mains / links / girders and the gondola top's
nose train, the doors facing away from the platforms. GD_OUTBOARD = "-Y" (the --outboard default for
the gondola) builds the EXACT mirror of the whole cabin across y = 0 (gondola_parts / gondola_proxy:
scale y -1, winding flipped; the stamps and the plate are laid out after the mirror, so they read
right): look, tris, hang 4.50, bounds (mirrored) and door openings unchanged. `--outboard=+Y`
rebuilds the approved revision 1b cabin byte for byte (the A-B proof). catalog/carriers.json gd8
"outboard" carries the same sign; the gondola stations' cabin pose (lift_terminal_detach G1 / G8 /
previews) reads it there. The preview cameras mirror with the side (same frames, mirror image).

Every geometry contract (G1-G12 gondola, T1-T13 tram; G11 / G12 / T12 on LOD0 only) is asserted
on LOD0, LOD1 and LOD2 from the actual vertices: a wrong build raises RuntimeError before anything
is exported. Names via cs2_asset.validate_asset_name. Deterministic (canonical part order, CPU
bake seed 0, no wall-clock in the textures): tests/test_cabin_contracts.py builds each kind twice.

Library (no edits to any shared module; local helpers are marked "L<n>" after cabins.md
section 13): cs2_ingest (load_external, rescale_by_reference, reframe, clean, discard),
cs2_kit (add_box, add_cyl, gusset, grating, cable_clamp, sheave, tube_along), cs2_uv,
cs2_asset (tag_parts, store_part_table, shade_by_angle, orient_for_net, export_fbx,
write_settings), cs2_bake (material_sources, bake_unique, MaterialSpec), cs2_compose,
cs2_validate, cs2_preview, cs2_review, ski_launcher_glyph (the shipped glyph (c)).

Credit (CC BY 4.0, ROUND-SPEC section 8; assets/external/wilin-gondola/SOURCE.md):
    "Ski gongola and lift station detailed drafts" by Wilin_Amaurea -
    https://sketchfab.com/3d-models/ski-gongola-and-lift-station-detailed-drafts-a8fabd3e955643a2a9e9f09bd46635db
    licensed under CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/). Modified: re-scaled,
    re-topologised, re-UV'd and re-textured for Cities: Skylines II. The tram uses no
    third-party geometry.
"""

import argparse
import collections
import hashlib
import json
import math
import os
import shutil
import sys
import tempfile

import bpy
import bmesh
import mathutils
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import carrier_split as CS  # noqa: E402
import cs2_asset     # noqa: E402
import cs2_compose   # noqa: E402
import cs2_ingest    # noqa: E402
import cs2_kit       # noqa: E402
import cs2_uv        # noqa: E402
import cs2_validate  # noqa: E402


# ======================================================================================
# Paths, names, carrier rows
# ======================================================================================

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
DEFAULT_OUT = os.path.join(REPO, "assets", "import", "cabins", "A")
DEFAULT_SPLIT_OUT = os.path.join(REPO, "assets", "import", "cabins", "B")   # --split (2026-11-03)
DEFAULT_PREVIEW = os.path.join(REPO, "assets", "preview", "cabins")
DEFAULT_SOURCE = os.path.join(REPO, "assets", "external", "wilin-gondola", "source", "gondola.fbx")
DEFAULT_CARRIERS = os.path.join(REPO, "catalog", "carriers.json")
DEFAULT_AB = os.path.join(REPO, "assets", "import", "chairs", "L", "SkiLiftChair6L")
DRAFT_ROOT = os.path.join(tempfile.gettempdir(), "lift_cabin")

NAMES = {"gondola": "SkiGondolaCabin8A", "tram": "SkiTramCabinA"}
#: --split (carrier split 2026-11-03, docs/research/carrier-pivot-swing-plan-2026-11-03.md): (body, grip) per
#: kind, a new generation letter B (deployed A names are never reused). The tram split was written as
#: SkiTramCabinA + SkiTramCarriageA and never shipped.
SPLIT_NAMES = {"gondola": ("SkiGondolaCabin8B", "SkiGondolaCabin8GripB"),
               "tram": ("SkiTramCabinB", "SkiTramCarriageB")}
CATALOG_KEY = {"gondola": "gd8", "tram": "tram"}

FRAME_PROP = "cabin_frame"       # "net" once orient_for_net has run (contract math undo it)
PART_TABLE_PROP = cs2_asset.PART_TABLE_PROP

#: The two rows this generator owns (cabins.md section 9; R27-R31). catalog/carriers.json is
#: human-written and not this generator's file: it is READ and any drift is reported loudly
#: (console + manifest + build report) so the catalog owner can paste these rows.
#: Fixer 2026-10-11 (gondola-tram-review-pipeline finding 1 / 7, geometry F7): line_prefab and
#: lift_type are round-2-contract section 1.3 / 10.2 (SkiGondolaHV / SkiTramHV); no `owner` (the
#: catalog owner writes its own). Every field is PREFAB-local (x lateral, y up, z travel):
#: `outboard` is where the gondola stalk / door side sits in the raw FBX frame (prefab x = authoring
#: y): -X since nitpick A3 (GD_OUTBOARD -Y, the carrier's right on the stations' spin; it was +X =
#: authoring +Y, the lens's "forward -X" reading); catalog_drift writes the built side into the
#: manifest's generator row. `bounds_lat_m` is read OUTBOARD-positive (outboard +1.10 at the door
#: handles, inboard -1.05), as the towers' C18 / C20 and the stations' GD_ENV read it. The tram
#: carriage pitch pivot K is a height above the cabin underside.
CARRIERS = {
    "gd8": {
        "object_prefab": "SkiGondolaCabin8", "mesh_base": "SkiGondolaCabin8",
        "line_prefab": "SkiGondolaHV", "lift_type": "gondola", "seats": 8, "hang_m": 4.500,
        "grip_top_above_rope_m": 0.080, "lane_x_m": 3.0, "axle_m": 10.0, "spacing_m": 60.0,
        "speed_mps": 5.5, "bounds_lat_m": [-1.05, 1.10], "bounds_travel_half_m": 1.18,
        "outboard": "-X", "facing": "travel", "lane_hanging_max": 0.004},
    "tram": {
        "object_prefab": "SkiTramCabin", "mesh_base": "SkiTramCabin",
        "line_prefab": "SkiTramHV", "lift_type": "tram",
        "host": "base station ObjectSubObjects x2, identity rotation (R29)", "seats": 100,
        "hang_m": 10.500, "grip_top_above_rope_m": 0.500, "lane_x_m": 5.5,
        "haul_x_m": 5.0, "haul_dy_m": -0.40, "track_ropes_per_side": 1,
        "speed_mps": 10.0, "dwell_s": 60, "bounds_lat_half_m": 1.90,
        "bounds_travel_half_m": 4.05, "carriage_half_w_m": 1.10,
        "pivot": "floor underside", "facing": "identity, never yawed 180 (R29)",
        "pivot_k_up_m": 10.38, "lane_hanging_max": 0.004},
}

#: Nitpick A3 (2026-10-13; orchestrator ruling F1 on build/nitpick-a2-hanger-gondola.md; module
#: docstring): the gondola cabin's OUTBOARD side in the AUTHORING frame = its stalk / carriage / grip
#: / door side. The family rule (every carrier): outboard = the carrier's RIGHT facing its travel =
#: authoring -Y on the stations' spin (chain A = the right lane looking uphill, the stations built
#: round it; the mover yaws every carrier to face its travel). The --outboard default for the gondola;
#: "+Y" = the approved revision 1b cabin (the A-B rebuild). catalog/carriers.json gd8 "outboard"
#: says the same (tests/test_gondola_tram_seams.py S11 / S15 gate the seam).
GD_OUTBOARD = "-Y"
OUTBOARD_PREFAB_X = {"+Y": "+X", "-Y": "-X"}     # raw FBX / prefab x = authoring y

#: The carrier-contract fields this generator builds its meshes to. catalog_drift and
#: tests/test_gondola_tram_seams.py S11 compare ONLY these (numbers within 1e-6), so keys the
#: catalog owner adds or rewrites (line_prefab, lift_type, owner, seats, speed, spacing, doc,
#: schema) never count as drift (gondola-tram-review-pipeline finding 1).
CONTRACT_FIELDS = ("hang_m", "grip_top_above_rope_m", "lane_x_m", "axle_m", "haul_x_m",
                   "haul_dy_m", "bounds_lat_m", "bounds_lat_half_m", "bounds_travel_half_m",
                   "carriage_half_w_m", "lane_hanging_max")


def same_value(a, b, eps=1e-6):
    """Contract-field equality: numbers within eps, lists element-wise, anything else ==."""
    if isinstance(a, bool) or isinstance(b, bool):
        return a == b
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return abs(float(a) - float(b)) <= eps
    if isinstance(a, (list, tuple)) and isinstance(b, (list, tuple)):
        return len(a) == len(b) and all(same_value(x, y, eps) for x, y in zip(a, b))
    return a == b

#: The tram stations are the anchor owner of the parked-cabin heights (tram_station.py DECK_Y,
#: DOCK_Z, TRACK_Y; build/tram_station.md section 2): the manifest's hosted-pose rows are
#: derived from these, and tests/test_gondola_tram_seams.py asserts them equal to the station's.
TRAM_STATION = {"deck_y": 6.30, "dock_z": -5.50, "track_y": 16.00, "deck_edge_dx": 2.30}


# ======================================================================================
# Shared constants
# ======================================================================================

#: Revision 1 (verdicts 2026-10-11 items 6 / 9) measured 60 vs 66: the Wilin shell's
#: facets sit at 50-70 deg, so 60 marks 93 more shell edges sharp (the faceted "CAD" read) while
#: 66 keeps the shell, the 12-segment tubes and rollers (30 deg) and the 2-3 segment bevels
#: (30-45 deg) smooth and every box / frame edge (>= 80 deg) crisp. lift_cabin.md Revision 1.
SHADE_ANGLE = 66.0
REMOVE_DOUBLES_M = 0.0005
HV_ROPE_TOP = 0.0866       # vanilla HV cable lane mesh: hexagon r 0.100, flat top 0.0866 [V]
HV_ROPE_R = 0.100
LV_ROPE_R = 0.060          # vanilla LV cable lane mesh bounds x +-0.060 [V]
GLYPH_SS = 4               # ski_launcher_glyph.render_alpha supersampling of the logo stamp
GLYPH_RGB = (0.85, 0.85, 0.85)          # linear white of the stamped logos
#: Revision 1: the logo shipped at 95 px (gondola, 170 px/m x 0.56 m) and 135 px (tram, 193 px/m
#: x 0.70 m); "a tiny bit low-res". Every logo quad of one asset now shares ONE pinned
#: square of the stamp stripe (PIPELINE section 3: stamped strips keep their UVs, copied 1:1),
#: >= 4x the shipped edge length; the glyph alpha is rendered at the square's own pixel size.
LOGO_SHIPPED_PX = {"gondola": 95.0, "tram": 135.0}
PLATE_LIGHT_SRGB = (0.90, 0.90, 0.86)   # lift_chair plate colors (sRGB-encoded there)
PLATE_DARK_SRGB = (0.08, 0.08, 0.09)
PLATE_CELL_M = 0.0105                   # 5x7 glyph cell on the plate (lift_chair.py:93)

PAINT_LIN = 0.604          # sRGB 0.80 (cs2_asset.RECOLOR_ALBEDO) in linear
DIGITS = {
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

#: Preview views AFTER orient_for_net (travel = Blender -Y, authoring +Y = Blender +X). Written for
#: the door / stalk side on +Y; view_dirs() mirrors their x with the built side (nitpick A3: the -Y
#: cabin is shot from -X, the same frames as the mirror image).
CABIN_VIEWS = (
    ("three_quarter", (1.0, -1.1, 0.45)),
    ("side",          (1.0, 0.0, 0.10)),
    ("end",           (0.0, -1.0, 0.10)),
    ("top",           (0.15, -0.2, 1.0)),
)
PREVIEW_RES = (1000, 1200)


def view_dirs(views, sign):
    """`views` ((name, (x, y, z)), ...) with x times `sign` (+1 as written; -1 for the -Y cabin)."""
    return tuple((n, (sign * d[0], d[1], d[2])) for n, d in views)


def _paint(slot, rough):
    return {"albedo": (PAINT_LIN,) * 3, "slot": slot, "recolor_albedo": True, "metal": 0.0,
            "rough": rough}


#: cs2_bake.MaterialSpec fields per material key (cabins.md section 3.6 / 4.3).
MATERIALS = {
    "paint_body":  _paint(1, 0.45),
    "paint_roof":  _paint(2, 0.50),
    "paint_trim":  _paint(3, 0.55),
    "paint_band":  _paint(1, 0.45),
    "paint_frame": _paint(2, 0.50),
    "paint_dark":  {"albedo": (0.06, 0.06, 0.065), "slot": 0, "metal": 0.0, "rough": 0.55},
    "glass":       {"albedo": (0.030, 0.034, 0.040), "slot": 0, "metal": 0.0, "rough": 0.12},
    "galv":        {"texture_set": "Metal032", "metres_per_tile": 2.0},
    "rubber":      {"albedo": (0.035, 0.035, 0.035), "slot": 0, "metal": 0.0, "rough": 0.80},
    "plate":       {"albedo": (0.75, 0.75, 0.75), "slot": 0, "metal": 0.0, "rough": 0.50},
    # Revision 2 (tram only): the "stand clear" stripe on the door threshold nosing (fixed
    # safety yellow, never recolored); unused keys never reach a bake (build_lod0 takes
    # only the keys its parts carry), so the gondola build is unchanged
    "safety":      {"albedo": (0.78, 0.47, 0.02), "slot": 0, "metal": 0.0, "rough": 0.60},
}


# ======================================================================================
# GONDOLA constants (cabins.md section 3.1, skeptic corrections)
# ======================================================================================

G = dict(
    ingest_scale=27.0,                         # fence = 1.00 m [W]
    subtree="cabin.001",
    keep={"shell": "/cabin", "door_l": "/doors/door_left/door.001",
          "door_r": "/doors/door_right/door"},
    plan_scale=(0.8779, 0.9091, 1.0),          # 2.620 -> 2.30, 2.309 -> 2.10 (C1)
    z_knots=((0.0, 0.0), (0.624, 0.820), (1.519, 2.075), (1.823, 2.400)),
    shell_raw_tris=739, shell_clean_tris=571, door_tris=114,
    plug_loops=3, plug_tris=17,                # skeptic verdict 2 / MC6
    body=(2.30, 2.10, 2.40),
    hang=4.500, rope=4.420,                    # C5: grip_top_above_rope 0.08
    wilin_mount_m=(1.217, 1.267, 0.560),       # skeptic verdict 3 / MC6 (source column)
    # Revision 1 hanger: ONE smooth 12-segment tube (tube_along, bend rings) instead of the
    # box-section sweep + flat gussets. The arm rests on both roof rails (center 0.085 above the
    # 2.52 rail top, 1 cm seated); the stalk center moves 0.68 -> 0.655 so the tube sits inside
    # its hanger head; every point stays inside tram_tower.CARRIER_GD (seams S6, both pin frames).
    hanger_a=(0.0, -0.58, 2.595), hanger_b=(0.0, 0.655, 2.595), hanger_d=(0.0, 0.655, 3.98),
    hanger_r=0.085, hanger_segs=12, hanger_fillet=0.40, hanger_bend_rings=6, roller_segs=10,
    yoke_y=(-0.59, 0.15),                      # rails under the arm's straight run (was 0.44)
    door_centroid_min=0.90,                    # G5 on the centroid (skeptic MC2; measured 0.98)
    door_reveal=0.014,                         # each leaf shrinks 14 mm per side: dark plug reveal
    extents=dict(x=1.20, y_lo=-1.06, y_hi=1.10),
    logo_size=0.56, logo_zc=0.42, plate_wh=(0.20, 0.13), plate_zc=3.45,
    # Revision 1: 2048 (R18 "bump only where the density report demands it"): at 1024 the
    # measured pack (Blender AABB, 156 islands, 16 px margin) covers 15 % of the atlas and the
    # shell ships at 82 px/m; hero faces >= 120 px/m plus a >= 4x logo square do not fit 1024.
    tex=2048, ao=32, margin=32,
    logo_frac=400.0 / 2048.0,                  # logo square edge / A (400 px at 2048 = 4.2x)
    plate_ppm=1100.0,                          # pinned plate texels (2048; scaled with A)
    fill_prefixes=("body_", "doorbody_"),      # pinned beside the logo (same material)
    fill_uv_angle=40.0,
    lod1_delete=("hw_", "doorhw_", "bumper_", "roller_", "plate_", "plaque_",
                 "doorhandle_", "hangerclamp_"),
    lod1_protect=("grip_", "hanger_", "logo_"),
    lod2_cap=250, lod2_target=200, lod0_cap=2500,
    lod0_projection=2420,                      # G7: Revision 1 measured build (+-10 %), cap 2,500
    lod0_band=(2100, 2500),
)
G["lod2_delete"] = G["lod1_delete"] + ("doorbody_", "doortrim_", "doorglass_", "logo_",
                                       "vent_", "spring_")   # yoke_ kept: G4 (roof -> arm gap)

#: Wilin material -> (part prefix, material key); doors get the "door" prefix in front.
G_MATERIAL_MAP = {
    "yellow_basic": ("body_", "paint_body"), "yellow_dark": ("body_", "paint_body"),
    "yellow_med": ("body_", "paint_body"), "yellow_light": ("roof_", "paint_roof"),
    "gray_dark": ("trim_", "paint_trim"), "gray_darker": ("trim_", "paint_trim"),
    "gray_basic": ("hw_", "galv"), "gray_light": ("hw_", "galv"),
    "glass": ("glass_", "glass"),
}
#: tris per part after clean (per door leaf). Measured in the contract's order (rescale ->
#: reframe -> plan scale + z remap -> clean): yellow_basic 143 + yellow_dark 84 = body_ 227 and
#: gray_dark 136 + gray_darker 94 = trim_ 230; cabins.md section 3.2 printed 231 / 226 (the
#: total 571 agrees - clean's degenerate dissolve moves 4 tris between the two materials).
G_SPLIT_EXPECT = {"body_": 227, "roof_": 8, "trim_": 230, "hw_": 34, "glass_": 72,
                  "doorbody_": 12, "doortrim_": 84, "doorhw_": 2, "doorglass_": 16}

#: Revision 1: hero faces (trim, roof - the CS2 camera looks down on it - doors) weigh more;
#: body_ / doorbody_ / logo_ / plate_face are PINNED in the stamp stripe (stamp_layout), so their
#: weights only matter to an unpinned build (the --split carriage, tests).
G_DENSITY = {
    "body_": 1.2, "roof_": 1.1, "trim_": 1.3, "hw_": 0.6, "glass_": 0.5, "doorplug_": 0.2,
    "doorbody_": 1.2, "doortrim_": 1.3, "doorglass_": 0.5, "doorhw_": 0.6, "yoke_": 0.8,
    "hanger_": 1.0, "hangerclamp_": 0.8, "carframe_": 1.0, "grip_": 1.4, "roller_": 0.6,
    "spring_": 0.8, "bumper_": 0.6, "vent_": 0.9, "plate_face": 6.0, "logo_": 2.5,
    "plaque_": 0.4, "doorhandle_": 0.6,
}

#: VariationSets (surgery; ColorProperties channels 0/1/2 = slot 1 body / 2 roof / 3 trim).
#: ONE set since 2026-10-16 (T29, field item 51: four random sets made the line read as color groups):
#: station accent body, station galvanized roof, dark trim; jitter 0 (m_VariationRanges below).
G_SETS = (
    ("Station livery", (0.85, 0.25, 0.10), (0.25, 0.26, 0.275), (0.07, 0.07, 0.08)),
)


# ======================================================================================
# TRAM constants (cabins.md section 4, reconciled by R27-R30)
# ======================================================================================

T = dict(
    hang_choices=(10.50, 7.00), grip_above_rope=0.50,      # R27
    fin_top={10.50: 4.60, 7.00: 4.10},
    corner_r=0.35, corner_segs=4,
    # Revision 1 body profile (section 4.1 bands, rounded): (z, half L travel, half W) knots of
    # the wall polyline from the underside edge to the roof edge; `round` lists the knots that
    # get a quadratic fillet (z-extent below / above, inward run of a horizontal leg).
    #   0 underside edge (6.50 x 3.40)  1 boat chamfer top / band foot (7.70 x 3.70)
    #   2 band top 1.06  3 trim top 1.10  4 main glazing top 2.41 (ends lean ~5 deg)
    #   5 sill top 2.49  6 transom top 2.81  7 roof-crown virtual corner 3.00 (8.00 x 3.70)
    profile=((0.00, 3.25, 1.70), (0.30, 3.85, 1.85), (1.06, 3.85, 1.85), (1.10, 3.855, 1.85),
             (2.41, 3.97, 1.85), (2.49, 3.975, 1.85), (2.81, 4.00, 1.85), (3.00, 4.00, 1.85)),
    boat_fillet=dict(run=(0.20, 0.06), up=0.06, steps=3),  # underside -> chamfer (run x / y)
    chamfer_fillet=dict(down=0.07, up=0.07, steps=4),      # chamfer -> vertical band
    crown=dict(down=0.15, run=0.15, steps=4),              # wall -> roof plane (rounded crown)
    reveal=0.045,                                         # glazing set back behind the frames
    split_at=(2, 7, 12, 17),                              # ring index cuts: 4 UV pieces per run
    haul_dy=0.50, haul_dz=-0.40,                          # R28
    half_w_max=1.10,                                      # R30
    k1=(-0.80, -0.05, 0.36), k2=(-3.80, -0.70, 0.80),     # R30 tower keep-outs (dz lo, hi, |dy| min)
    upright_dy=0.95, upright_wd=(0.40, 0.26), fork_half=1.08, fork_h=0.28, fork_below=3.82,
    column_wd=(0.75, 0.50), fin_len=(4.80, 0.75), fin_t=0.40,
    # Revision 1 radii of the box sections (rounded, 2 segments per corner)
    r_column=0.08, r_fork=0.06, r_upright=0.05, r_brace=0.04, r_head=0.02, r_beam=0.025,
    wheel_r=0.20, wheel_w=0.20, wheel_segs=12, wheel_hub_r=0.06,
    # wheel profile (radius, axial) from the bore round the tread back to the bore: dished web,
    # rim flanges and the V groove whose bottom (r - 0.036) sits on the drawn HV rope top (R28)
    wheel_profile=((0.065, -0.030), (0.150, -0.030), (0.150, -0.100), (0.200, -0.100),
                   (0.200, -0.065), (0.164, 0.000), (0.200, 0.065), (0.200, 0.100),
                   (0.150, 0.100), (0.150, 0.030), (0.065, 0.030)),
    bogie_x=(-1.50, 1.50), wheel_dx=(-0.75, -0.25, 0.25, 0.75),
    # lighter bogie side plate: outline (x, dz above the rope), chamfered ends and a raised
    # center arch; its foot stays 0.02 above the rope (T3 / T11a clear by construction)
    bogie_outline=((-1.05, 0.31), (1.05, 0.31), (1.05, 0.17), (0.92, 0.02), (0.14, 0.02),
                   (0.06, 0.11), (-0.06, 0.11), (-0.14, 0.02), (-0.92, 0.02), (-1.05, 0.17)),
    plate_dy=0.14, carbeam_dy=0.50, carbeam_len=5.60, tie_x=(-2.70, 2.70),
    clamp_x=(-2.70, 2.70),
    extents=dict(x=4.05, y=1.90),
    tex=2048, ao=48, margin=32, lod0_cap=8000, lod2_cap=500, lod2_target=460,
    logo_frac=576.0 / 2048.0,                  # logo square edge / A (576 px at 2048 = 4.3x)
    logo_zc=0.71,                              # band center above the rounded band foot
    # T7 projections (+-10 %): Revision 2 measured builds (RS section 4 target 6,000, cap 8,000;
    # Revision 1 was 4,758 / split 2,000 + 2,760)
    # carrier split 2026-11-03: the split adds the pin joint (T_SPLIT pin_detail 256; was 2,830 + 2,848)
    # and its carriage the 16-segment rims (T_SPLIT segs_detail 640)
    lod0_projection=5678, split_projection={"cabin": 2926, "carriage": 3648},
    # Revision 2: the door leaves stay at LOD1 (doors read closed, protected from the collapse),
    # their small hardware goes; LOD2 drops every door* / hatch* part (the body behind the
    # leaves is the unchanged Revision 1 body, so LOD2 is the Revision 1 LOD2)
    lod1_delete=("mullion_", "ladder_", "grate_", "clamp_", "bolt_", "cliplink_", "axle_",
                 "plaque_", "doorgrab_", "doorpull_", "doorroller_", "doorhanger_", "doordrip_",
                 "doorpocket_", "intercom_", "bumper_", "hatchhinge_", "pivot_"),
    # bogie_ is protected at LOD1: collapse once dragged plate corners into the T3 saddle clear
    # zone (split carriage LOD1: 8 vertices below rope - 0.05 at |y| < 0.36 - the contract caught
    # it). LOD2 deletes the plates instead (Revision 1: lighter plates cost tris; T8 coverage is
    # carried by the uprights, carriage head and beams).
    lod1_protect=("carbeam_", "carhead_", "hanger_", "fork_", "logo_", "bogie_", "doorband_",
                  "doortrim_", "doorframe_", "doorglass_", "doorgasket_", "doorseal_", "doorjamb_",
                  "doorsill_", "doorheader_"),
    lod2_protect=("carbeam_", "carhead_", "hanger_", "fork_"),
    carriage_prefixes=("carhead_", "carbeam_", "tie_", "bolster_", "bogie_", "sheave_", "axle_",
                       "clamp_", "bolt_", "cliplink_", "pivot_"),
    split_tex=1024,
    # ---- Revision 2 (2026-10-11: "a tram would have a pair of sliding doors on each
    # side in the middle, opening outwards"; "gondola has some details that the tram doesn't")
    # One outward-opening sliding (plug) double door per long side, centered on the cabin length
    # (R29 symmetric), modeled CLOSED: two leaves meeting at x 0, each 0.838 wide, from the
    # threshold (0.32, the station boarding deck = pivot + 0.30, +2 cm) to the main glazing
    # head (2.38). Leaves stand 15 mm PROUD of the 1.85 wall (front 1.865; the exported
    # lateral bound 1.880 is the mullions' and leaves 15 mm for hardware), their edge faces run
    # back to the glazing plane (1.803), dark rubber jamb seals outline the opening and rubber
    # nosings meet at the center (the gap line), the leaf windows sit 25 mm behind the leaf
    # face in rubber gaskets. Grab bars sit in recessed dark pockets (the 15 mm to the bound
    # leaves no room for a stand-off bar); a release pull on the meeting stile; a header
    # runner box over the opening + slide travel with two hanger rollers per leaf on brackets;
    # a galvanised threshold with a safety-yellow nosing; a drip rail over the door.
    door=dict(x_meet=0.012, x_out=0.850, y_front=1.865, y_back=1.803, y_win=1.840,
              z_bot=0.320, z_top=2.380, stile=0.070, rail_top=0.080, rail_bot=0.035,
              pocket=(0.075, 0.135, 0.60, 1.00, 1.853), grab_r=0.010, grab_y=1.862,
              pull=(0.028, 0.064, 1.55, 1.67, 1.877),
              seal=(0.001, 0.013, 1.852, 1.869), jamb=(0.852, 0.866, 1.862),
              sill=(0.875, 1.830, 1.876, 0.295), header=(1.655, 1.848, 1.878, 2.412, 2.530),
              roller_r=0.020, roller_w=0.016, roller_y=1.861, roller_dx=0.25,
              hanger_y=(1.853, 1.863), drip=(1.00, 1.848, 1.874, 2.822, 2.840),
              intercom=(0.95, 1.05, 0.66, 0.82, 1.872)),
    # long-side mullions: the center one (main row) is now the door seam; +-1.25 move to +-1.70
    # so the panes either side of the door are 0.79 / 0.72 m (not 0.35); the transom keeps a
    # center mullion above the door, standing on the runner box
    mullion_main_x=(-2.50, -1.70, 1.70, 2.50), mullion_transom_x=(-2.50, -1.70, 0.0, 1.70, 2.50),
    logo_side_x=(-2.10, 2.10),            # the side logos flank the doors (one per side pane)
    bumper=dict(size=(0.11, 0.40, 0.32), y=1.10, zc=0.62, r=0.025),
    hatch=dict(x=2.77, size=(0.55, 0.85, 0.07), hinge_y=0.25, hinge_r=0.016, hinge_l=0.10),
    pivot=dict(boss_r=0.045, boss_l=0.06, nut_r=0.030, nut_l=0.02),
)
T["lod2_delete"] = T["lod1_delete"] + ("sheave_", "logo_", "roofbox_", "tie_", "bolster_",
                                       "bogie_", "door", "hatch")

#: Revision 1: band_ / doorband_ / logo_ are PINNED in the stamp stripe; the long body runs are
#: split into 4 UV pieces each (T split_at), so their weights no longer fight a 22 m strip.
#: Revision 2: the unpinned galv pack is margin-bound (~25 px/m per unit weight at 2048 with
#: ~400 islands), so the bolt-bearing parts weigh 1.8-2.4 (their tram-scale heads then clear the
#: 2-texel floor, tram_detail), the wheels 1.6, ladder and axles 1.0 (they would otherwise drop
#: to ~13-20 px/m).
T_DENSITY = {
    "under_": 0.5, "chamfer_": 0.9, "band_": 1.0, "trim_": 3.0, "glass_": 0.5, "sill_": 1.2,
    "reveal_": 1.0, "frame_": 1.2, "roof_": 0.8, "mullion_": 1.0, "doorband_": 1.0,
    "doorglass_": 0.5, "logo_": 2.5, "roofbox_": 2.2, "grate_": 0.6, "yoke_": 2.2,
    "hanger_": 2.0, "fork_": 2.0, "ladder_": 1.0, "carhead_": 2.4, "carbeam_": 2.4, "tie_": 0.9,
    "bolster_": 0.9, "bogie_": 2.4, "sheave_": 1.6, "axle_": 1.0, "clamp_": 0.8, "bolt_": 0.8,
    "cliplink_": 0.8, "plaque_": 0.4,
    # Revision 2: door kit (doorframe_ / doorheader_ pinned with the frames at 130 px/m,
    # doorband_ in the fill, doorglass_ with the glass; small hardware weighted toward the
    # gondola's handle density), roof hatches, end bumpers, hanger pivot
    "doortrim_": 3.0, "doorframe_": 1.2, "doorgasket_": 1.0, "doorseal_": 1.0, "doorjamb_": 1.0,
    "doorsill_": 1.5, "doorheader_": 1.2, "doorgrab_": 3.0, "doorpull_": 3.0,
    "doorroller_": 1.5, "doorhanger_": 2.0, "doordrip_": 1.5, "doorpocket_": 1.0,
    "intercom_": 2.0, "bumper_": 0.8, "hatch_": 1.2, "hatchhinge_": 2.0, "pivot_": 2.5,
}

T_SETS = (
    ("Jackson red (default)", (0.75, 0.06, 0.05), (0.05, 0.05, 0.06), (0.05, 0.05, 0.06)),
    ("Snowbird (documented)", (0.75, 0.06, 0.05), (0.75, 0.06, 0.05), (0.90, 0.90, 0.90)),
    ("Aiguille (documented)", (0.06, 0.06, 0.07), (0.06, 0.06, 0.07), (0.75, 0.06, 0.05)),
)


def tram_dims(hang):
    """Every derived tram height for one hang (10.50 default, 7.00 compact)."""
    hang = float(hang)
    key = min(T["hang_choices"], key=lambda h: abs(h - hang))
    if abs(key - hang) > 1e-6:
        raise ValueError(f"--hang {hang}: supported {T['hang_choices']} (cabins.md C2, R27)")
    rope = hang - T["grip_above_rope"]
    fin_top = T["fin_top"][key]
    fork_top = max(rope - T["fork_below"], fin_top + T["fork_h"])
    groove = max(0.02, 0.18 * T["wheel_r"])               # cs2_kit.sheave default groove
    d = dict(hang=hang, rope=rope, fin_top=fin_top, fork_top=fork_top,
             fork_bot=fork_top - T["fork_h"], k_z=hang - 0.12,
             carhead=(hang - 0.12, hang - 0.02), carbeam=(hang - 0.10, hang),
             plate=(rope - 0.04, rope + HV_ROPE_TOP + 0.164 + 0.06),
             wheel_z=rope + HV_ROPE_TOP + (T["wheel_r"] - groove),
             haul_z=rope + T["haul_dz"])
    d["clear_lo"] = d["fork_top"] + 0.02                  # T3 clear zone floor
    d["k2_lo"] = max(rope + T["k2"][0], d["clear_lo"])    # R30 band (compact: shortened)
    return d


# ======================================================================================
# Scene helpers
# ======================================================================================

def clear_scene():
    """Delete every object and the orphaned data / ingest collections (headless builds)."""
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.cameras,
                 bpy.data.lights, bpy.data.curves, bpy.data.actions):
        for item in list(coll):
            if item.users == 0:
                coll.remove(item)
    for c in list(bpy.data.collections):
        if c.name.startswith(cs2_ingest.COLLECTION_PREFIX):
            bpy.data.collections.remove(c)


def _link(obj):
    bpy.context.collection.objects.link(obj)
    return obj


def _select_only(obj):
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def apply_transform(obj):
    """Bake loc/rot/scale into the mesh (matrix_basis -> identity)."""
    m = mathutils.Matrix.LocRotScale(obj.location, obj.rotation_euler, obj.scale)
    obj.data.transform(m)
    if m.is_negative:
        obj.data.flip_normals()
    obj.location = (0.0, 0.0, 0.0)
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    obj.data.update()


def _mesh_from_bm(name, bm, relief="geo"):
    if bm.loops.layers.uv.get("UVMap") is None:
        bm.loops.layers.uv.new("UVMap")
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj = _link(bpy.data.objects.new(name, me))
    obj["relief"] = relief
    return obj


def _orient_faces_outward(bm, centre):
    """Flip every face whose normal points toward `centre` (convex parts only)."""
    bm.normal_update()
    c = Vector(centre)
    flip = [f for f in bm.faces if f.normal.dot(f.calc_center_median() - c) < 0.0]
    if flip:
        bmesh.ops.reverse_faces(bm, faces=flip)
    bm.normal_update()


def _planar_uv(bm, layer, scale=1.0):
    """Per-face dominant-axis projection in meters (no mirrored islands)."""
    bm.normal_update()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        sgn = 1.0 if n[ax] >= 0 else -1.0
        o = [i for i in range(3) if i != ax]
        for lp in f.loops:
            co = lp.vert.co
            lp[layer].uv = (sgn * co[o[0]] * scale, co[o[1]] * scale)


# ======================================================================================
# Local geometry builders (L3 box sweep, L4 loft, quads, prisms)
# ======================================================================================

def quad_face(name, corners, outward, uv=((0, 0), (1, 0), (1, 1), (0, 1))):
    """ONE quad through 4 world corners whose normal points along `outward`."""
    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    vs = [bm.verts.new(c) for c in corners]
    f = bm.faces.new(vs)
    bm.normal_update()
    if f.normal.dot(Vector(outward)) < 0.0:
        bmesh.ops.reverse_faces(bm, faces=[f])
        bm.normal_update()
        uv = list(reversed(uv))
    for lp, (u, v) in zip(f.loops, uv):
        lp[layer].uv = (u, v)
    return _mesh_from_bm(name, bm)


def prism(name, bottom, top, drop=()):
    """Convex 8-corner prism: `bottom` / `top` are 4 corners each (same winding); faces
    named bottom/top/s0..s3 (s_k = edge k of the bottom ring); `drop` removes some."""
    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    vb = [bm.verts.new(p) for p in bottom]
    vt = [bm.verts.new(p) for p in top]
    faces = {}
    if "bottom" not in drop:
        faces["bottom"] = bm.faces.new(vb)
    if "top" not in drop:
        faces["top"] = bm.faces.new(vt)
    for k in range(4):
        k1 = (k + 1) % 4
        tag = f"s{k}"
        if tag not in drop:
            faces[tag] = bm.faces.new((vb[k], vb[k1], vt[k1], vt[k]))
    centre = sum((Vector(p) for p in list(bottom) + list(top)), Vector()) / 8.0
    _orient_faces_outward(bm, centre)
    _planar_uv(bm, layer)
    return _mesh_from_bm(name, bm)


def _sweep_samples(pts, bend_rings, fillet):
    """Centerline samples of a polyline with Hermite fillets at the bends (tube_along's rule)."""
    P = [Vector(p) for p in pts]
    dirs = []
    for a, b in zip(P, P[1:]):
        d = b - a
        if d.length < 1e-9:
            raise ValueError("box_along: coincident consecutive points")
        dirs.append(d.normalized())
    samples = [P[0].copy()]
    for i in range(1, len(P) - 1):
        tin, tout = dirs[i - 1], dirs[i]
        if math.degrees(tin.angle(tout)) < 1.0:
            continue
        lin, lout = (P[i] - P[i - 1]).length, (P[i + 1] - P[i]).length
        f = 0.5 * min(lin, lout)
        if fillet is not None:
            f = min(f, float(fillet))
        A, B = P[i] - tin * f, P[i] + tout * f
        samples.extend(cs2_kit._catmull_fillet(A, B, tin, tout, bend_rings))
    samples.append(P[-1].copy())
    clean = [samples[0]]
    for q in samples[1:]:
        if (q - clean[-1]).length > 1e-6:
            clean.append(q)
    tangents = []
    for i, q in enumerate(clean):
        if i == 0:
            t = dirs[0]
        elif i == len(clean) - 1:
            t = dirs[-1]
        else:
            t = clean[i + 1] - clean[i - 1]
        tangents.append(t.normalized())
    return clean, tangents


def _section(w, d, chamfer, segs=1):
    """Box cross-section, CCW: square corners (chamfer 0), a 1-segment chamfer, or (segs >= 2)
    corners rounded with `segs` segments of radius `chamfer` (Revision 1: soft box sections)."""
    hw, hd = w / 2.0, d / 2.0
    c = float(chamfer)
    if c <= 0.0:
        return [(hw, -hd), (hw, hd), (-hw, hd), (-hw, -hd)]
    if segs <= 1:
        return [(hw, -hd + c), (hw, hd - c), (hw - c, hd), (-hw + c, hd),
                (-hw, hd - c), (-hw, -hd + c), (-hw + c, -hd), (hw - c, -hd)]
    c = min(c, hw - 1e-4, hd - 1e-4)
    pts = []
    for cx, cy, a0 in ((hw - c, -hd + c, -90.0), (hw - c, hd - c, 0.0),
                       (-hw + c, hd - c, 90.0), (-hw + c, -hd + c, 180.0)):
        for k in range(segs + 1):
            a = math.radians(a0 + 90.0 * k / segs)
            pts.append((cx + c * math.cos(a), cy + c * math.sin(a)))
    return pts


def cap_check(label, outline, tris, rel_tol=1e-7, abs_tol=1e-10):
    """Revision 1b cap contract (C1 / C2): the triangles of one PLANAR cap neither overlap nor
    flip. Signed-area test against the cap normal taken from the outline winding (Newell):
    every triangle's area along that normal is >= 0 (no flipped ear) and the triangles' areas
    sum to the polygon's area (an overlapping ear adds area). `outline` = the cap's corners in
    order, `tris` = each triangle's three corners. float64 about the outline centroid (mathutils
    is float32). Raises RuntimeError("CAP ...")."""
    pts = np.array([tuple(p) for p in outline], dtype=np.float64)
    c0 = pts.mean(axis=0)
    pts = pts - c0
    nv = np.cross(pts, np.roll(pts, -1, axis=0)).sum(axis=0)
    area = 0.5 * float(np.linalg.norm(nv))
    if area < 1e-12:
        raise RuntimeError(f"CAP {label}: degenerate outline ({len(pts)} corners, area {area:.3e})")
    nh = nv / np.linalg.norm(nv)
    T3 = np.array([[tuple(p) for p in t] for t in tris], dtype=np.float64).reshape(-1, 3, 3) - c0
    signed = 0.5 * np.cross(T3[:, 1] - T3[:, 0], T3[:, 2] - T3[:, 0]) @ nh
    flipped = int(np.sum(signed < -abs_tol))
    total = float(np.abs(signed).sum())
    if flipped or len(tris) != len(pts) - 2 or abs(total - area) > abs_tol + rel_tol * area:
        raise RuntimeError(f"CAP {label}: {len(tris)} triangles for {len(pts)} corners, {flipped} "
                           f"flipped, triangle area {total * 1e4:.4f} cm2 vs polygon "
                           f"{area * 1e4:.4f} cm2 (overlapping / flipped ears)")
    return {"tris": len(tris), "area_m2": area}


def triangulate_caps(bm, caps, label, normals_first=True):
    """Revision 1b (C1 / C2): polyfill a builder's planar n-gon caps with their normals FIRST.
    bmesh.ops.triangulate projects each n-gon on its face normal, and a face fresh from
    bm.faces.new has none yet (0, 0, 0): the concave grip / bogie-plate caps came out with
    overlapping and flipped ears, the carbeam end caps with a 0.4 mm2 overlap. Every cap is
    then held to cap_check. normals_first=False keeps the as-shipped triangulation of caps that
    already pass (the horizontal rrect_loft / ring_cap caps: re-triangulating them would move
    valid triangles and the face-area weighted normals at their smooth rims), checked only."""
    rec = [(set(f.verts), [v.co.copy() for v in f.verts]) for f in caps]
    for f in caps if normals_first else ():
        f.normal_update()
    res = bmesh.ops.triangulate(bm, faces=caps, quad_method="BEAUTY", ngon_method="BEAUTY")
    out = list(res["faces"])
    for k, (vs, outline) in enumerate(rec):
        tris = [[v.co for v in t.verts] for t in out if set(t.verts) <= vs]
        cap_check(f"{label} cap {k}", outline, tris)
    return res


def box_along(name, pts, w, d, side=(1.0, 0.0, 0.0), chamfer=0.0, bend_rings=8, fillet=None,
              caps="none", relief="geo", corner_segs=1):
    """
    L3 (cabins.md section 13): a box-section sweep. `w` runs along the frame axis N (the
    `side` vector made normal to the path, parallel-transported), `d` along B = t x N. The
    corners may carry a 1-segment `chamfer` (8-vertex ring, the 0.02 m finish bevel) or, with
    corner_segs >= 2, a rounded corner of radius `chamfer` (Revision 1). Fillets as
    cs2_kit.tube_along. caps in none/both/start/end (n-gon caps, triangulated). UVMap = one
    strip (u = perimeter meters, v = arc meters) + planar caps. Returns the object with
    obj["rings"] = ring count.
    """
    if caps not in ("none", "both", "start", "end"):
        raise ValueError(f"caps {caps!r}")
    ring_pts, tangents = _sweep_samples(pts, bend_rings, fillet)
    t0 = tangents[0]
    N = Vector(side) - t0 * Vector(side).dot(t0)
    if N.length < 1e-6:
        raise ValueError(f"{name}: side vector parallel to the path")
    N.normalize()
    frames = []
    for t in tangents:
        N = N - t * N.dot(t)
        N.normalize()
        frames.append((N.copy(), t.cross(N)))
    sec = _section(w, d, chamfer, corner_segs)
    n = len(sec)
    per = [0.0]
    for k in range(n):
        a, b = sec[k], sec[(k + 1) % n]
        per.append(per[-1] + math.hypot(b[0] - a[0], b[1] - a[1]))
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rings = []
    for q, (Nk, Bk) in zip(ring_pts, frames):
        rings.append([bm.verts.new(q + Nk * a + Bk * b) for a, b in sec])
    arc = [0.0]
    for a, b in zip(ring_pts, ring_pts[1:]):
        arc.append(arc[-1] + (b - a).length)
    for i, (ra, rb) in enumerate(zip(rings, rings[1:])):
        for k in range(n):
            k1 = (k + 1) % n
            f = bm.faces.new((ra[k], ra[k1], rb[k1], rb[k]))
            u0, u1 = per[k], per[k + 1]
            for lp, (u, v) in zip(f.loops, ((u0, arc[i]), (u1, arc[i]), (u1, arc[i + 1]),
                                            (u0, arc[i + 1]))):
                lp[uv].uv = (u, v)
    cap_faces = []
    if caps in ("both", "start"):
        f = bm.faces.new(list(reversed(rings[0])))
        for lp, (a, b) in zip(f.loops, list(reversed(sec))):
            lp[uv].uv = (a + 5.0, b)
        cap_faces.append(f)
    if caps in ("both", "end"):
        f = bm.faces.new(rings[-1])
        for lp, (a, b) in zip(f.loops, sec):
            lp[uv].uv = (a + 8.0, b)
        cap_faces.append(f)
    if cap_faces and n > 4:
        triangulate_caps(bm, cap_faces, name)
    bm.normal_update()
    obj = _mesh_from_bm(name, bm, relief)
    obj["rings"] = len(rings)
    return obj


def rrect_ring(L, W, r, segs, z):
    """Rounded-rectangle ring (CCW seen from +Z), 4 x (segs + 1) points, mirror-symmetric."""
    cx, cy = L / 2.0 - r, W / 2.0 - r
    pts = []
    for sx, sy, a0 in ((1, 1, 0.0), (-1, 1, 90.0), (-1, -1, 180.0), (1, -1, 270.0)):
        for k in range(segs + 1):
            a = math.radians(a0 + 90.0 * k / segs)
            pts.append((sx * cx + r * math.cos(a), sy * cy + r * math.sin(a), z))
    return pts


def loft_band(name, ring_a, ring_b, relief="geo"):
    """L4: one band of quads between two rings of equal length (a below b): outward normals,
    UVMap strip u = perimeter meters along ring a, v = slant meters."""
    n = len(ring_a)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    va = [bm.verts.new(p) for p in ring_a]
    vb = [bm.verts.new(p) for p in ring_b]
    per = [0.0]
    for k in range(n):
        per.append(per[-1] + (Vector(ring_a[(k + 1) % n]) - Vector(ring_a[k])).length)
    slant = sum((Vector(b) - Vector(a)).length for a, b in zip(ring_a, ring_b)) / n
    for k in range(n):
        k1 = (k + 1) % n
        f = bm.faces.new((va[k], va[k1], vb[k1], vb[k]))
        for lp, (u, v) in zip(f.loops, ((per[k], 0.0), (per[k + 1], 0.0), (per[k + 1], slant),
                                        (per[k], slant))):
            lp[uv].uv = (u, v)
    bm.normal_update()
    return _mesh_from_bm(name, bm, relief)


def ring_cap(name, ring, up):
    """Flat n-gon cap over a ring, triangulated (BEAUTY): 18 tris for 20 points."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    vs = [bm.verts.new(p) for p in ring]
    f = bm.faces.new(vs if up else list(reversed(vs)))
    for lp in f.loops:
        lp[uv].uv = (lp.vert.co.x, lp.vert.co.y)
    triangulate_caps(bm, [f], name, normals_first=False)
    bm.normal_update()
    return _mesh_from_bm(name, bm)


def bevel_part(obj, width=0.02, segments=1, angle_deg=30.0):
    """The finish bevel on a boxy steel part (Bevel modifier, applied; UVs interpolated)."""
    apply_transform(obj)
    _select_only(obj)
    mod = obj.modifiers.new("Bevel", type="BEVEL")
    mod.width = width
    mod.segments = segments
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(angle_deg)
    mod.harden_normals = False
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


# --------------------------------------------------------------------------------------
# Revision 1 builders (verdicts 2026-10-11: "blocky ... straight outta CAD")
# --------------------------------------------------------------------------------------

def rounded_box(name, size, loc, radius, segs=2, open_bottom=False, drop=()):
    """cs2_kit.add_box with every edge rounded (Bevel, `segs` segments, applied) and a fresh
    smart_project UVMap (the bevel's interpolated planar UVs would fragment the islands).
    Open / dropped faces stay open: their boundary edges are not bevelled."""
    obj = cs2_kit.add_box(name, size, loc, open_bottom=open_bottom, drop=drop)
    bevel_part(obj, width=radius, segments=segs, angle_deg=30.0)
    smart_uv(obj)
    return obj


def extrude_profile(name, profile, a0, a1, axis="X"):
    """A closed prism: the CCW polygon `profile` [(p, q), ...] in the plane normal to `axis`
    (X: (p, q) = (y, z); Y: (p, q) = (x, z)) swept from a0 to a1 along the axis; concave
    outlines allowed (caps triangulated by polyfill on their own normals, Revision 1b C1 / C2:
    triangulate_caps + cap_check). UVMap: one strip (u = perimeter meters, v = axis meters) +
    planar caps. Normals outward."""
    def P(p, q, a):
        return (a, p, q) if axis == "X" else (p, a, q)
    n = len(profile)
    area = 0.5 * sum(profile[k][0] * profile[(k + 1) % n][1] - profile[(k + 1) % n][0] * profile[k][1]
                     for k in range(n))
    if area < 0.0:
        profile = list(reversed(profile))
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    lo = [bm.verts.new(P(p, q, a0)) for p, q in profile]
    hi = [bm.verts.new(P(p, q, a1)) for p, q in profile]
    per = [0.0]
    for k in range(n):
        a, b = profile[k], profile[(k + 1) % n]
        per.append(per[-1] + math.hypot(b[0] - a[0], b[1] - a[1]))
    L = abs(a1 - a0)
    for k in range(n):
        k1 = (k + 1) % n
        f = bm.faces.new((lo[k], lo[k1], hi[k1], hi[k]))
        for lp, (u, v) in zip(f.loops, ((per[k], 0.0), (per[k + 1], 0.0), (per[k + 1], L),
                                        (per[k], L))):
            lp[uv].uv = (u, v)
    caps = [bm.faces.new(list(reversed(lo))), bm.faces.new(hi)]
    for i, f in enumerate(caps):
        for lp in f.loops:
            p, q = (lp.vert.co.y, lp.vert.co.z) if axis == "X" else (lp.vert.co.x, lp.vert.co.z)
            lp[uv].uv = (per[-1] + 0.2 + p + i * 1.0, q)
    triangulate_caps(bm, caps, name)
    c = Vector(P(0.0, 0.0, 0.5 * (a0 + a1)))
    bm.normal_update()
    # outward: the strip faces point away from the axis line through the profile centroid
    cen = Vector((sum(p for p, _q in profile) / n, sum(q for _p, q in profile) / n))
    side = [f for f in bm.faces if len(f.verts) == 4]
    f0 = max(side, key=lambda f: (f.calc_center_median() - c).length)
    m = f0.calc_center_median()
    pm = Vector((m.y, m.z)) if axis == "X" else Vector((m.x, m.z))
    nn = Vector((f0.normal.y, f0.normal.z)) if axis == "X" else Vector((f0.normal.x, f0.normal.z))
    if nn.dot(pm - cen) < 0.0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
        bm.normal_update()
    return _mesh_from_bm(name, bm)


def rrect_loft(name, specs, cap_top=True, cap_bottom=False, segs=3):
    """One closed rounded-rectangle loft [(z, L, W, r), ...] (bottom -> top, `segs` corner
    segments) as ONE object: strip UVs (u = ring meters, v = cumulative slant) + planar caps."""
    rings = [rrect_ring(L, W, r, segs, z) for z, L, W, r in specs]
    n = len(rings[0])
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    V = [[bm.verts.new(p) for p in ring] for ring in rings]
    v_acc = [0.0]
    for a, b in zip(rings, rings[1:]):
        v_acc.append(v_acc[-1] + sum((Vector(q) - Vector(p)).length for p, q in zip(a, b)) / n)
    for j in range(len(rings) - 1):
        per = [0.0]
        for k in range(n):
            per.append(per[-1] + (Vector(rings[j][(k + 1) % n]) - Vector(rings[j][k])).length)
        for k in range(n):
            k1 = (k + 1) % n
            f = bm.faces.new((V[j][k], V[j][k1], V[j + 1][k1], V[j + 1][k]))
            for lp, (u, v) in zip(f.loops, ((per[k], v_acc[j]), (per[k + 1], v_acc[j]),
                                            (per[k + 1], v_acc[j + 1]), (per[k], v_acc[j + 1]))):
                lp[uv].uv = (u, v)
    caps = []
    if cap_top:
        caps.append(bm.faces.new(V[-1]))
    if cap_bottom:
        caps.append(bm.faces.new(list(reversed(V[0]))))
    for f in caps:
        for lp in f.loops:
            lp[uv].uv = (lp.vert.co.x + 50.0, lp.vert.co.y)
    if caps:
        triangulate_caps(bm, caps, name, normals_first=False)
    bm.normal_update()
    return _mesh_from_bm(name, bm)


def plaque(name, rim_name, centre, right, up, w, h, depth):
    """A stamp panel that is not a floating zero-thickness quad: the front face `name` is ONE
    quad (G9 / the stamp painters), `rim_name` the four side faces running `depth` back into
    the host surface. UVs of the front quad: (0..1) along right / up (stamp_layout re-maps
    them into the pinned square)."""
    c, R, U = Vector(centre), Vector(right).normalized(), Vector(up).normalized()
    n = R.cross(U).normalized()
    corners = [c - R * (w / 2) - U * (h / 2), c + R * (w / 2) - U * (h / 2),
               c + R * (w / 2) + U * (h / 2), c - R * (w / 2) + U * (h / 2)]
    front = quad_face(name, [tuple(p) for p in corners], tuple(n))
    return front, quad_rim(front, rim_name, depth)


def quad_rim(quad_obj, rim_name, depth):
    """The four side faces of a single-quad panel, `depth` back along -normal (into the host
    surface): one strip UV island (u perimeter, v depth). Outward normals."""
    me = quad_obj.data
    if len(me.polygons) != 1 or me.polygons[0].loop_total != 4:
        raise RuntimeError(f"quad_rim: {quad_obj.name} is not one quad")
    poly = me.polygons[0]
    corners = [Vector(me.vertices[i].co) for i in poly.vertices]
    n = Vector(poly.normal).normalized()
    c = sum(corners, Vector()) / 4.0
    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    fv = [bm.verts.new(p) for p in corners]
    bv = [bm.verts.new(p - n * depth) for p in corners]
    per = [0.0]
    for k in range(4):
        per.append(per[-1] + (corners[(k + 1) % 4] - corners[k]).length)
    faces = []
    for k in range(4):
        k1 = (k + 1) % 4
        f = bm.faces.new((bv[k], bv[k1], fv[k1], fv[k]))
        faces.append((f, k))
    bm.normal_update()
    for f, k in faces:
        if f.normal.dot(f.calc_center_median() - c) < 0.0:
            bmesh.ops.reverse_faces(bm, faces=[f])
    bm.normal_update()
    for f, k in faces:                              # one strip island (u perimeter, v depth)
        k1 = (k + 1) % 4
        for lp in f.loops:
            u = per[k] if lp.vert in (fv[k], bv[k]) else per[k + 1]
            v = depth if lp.vert in (fv[k], fv[k1]) else 0.0
            lp[layer].uv = (u, v)
    return _mesh_from_bm(rim_name, bm)


def wheel_rim(name, profile, segs, loc):
    """Revision 1 'real wheel profile': a revolved (radius, axial) profile about the axis Y
    at `loc` (dished web, flanges, V groove), built ring by ring in a fixed order (no spin, so
    no element-order variance), one strip UV island (u = mean circumference meters, v =
    profile meters on the tread / groove; planar annuli on the web and flange faces). Open at
    the bore: the axle fills it."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    x0, y0, z0 = loc
    V = []
    for r, a in profile:
        row = []
        for k in range(segs):
            t = 2.0 * math.pi * k / segs
            row.append(bm.verts.new((x0 + r * math.cos(t), y0 + a, z0 + r * math.sin(t))))
        V.append(row)
    vv = [0.0]
    for (r0, a0), (r1, a1) in zip(profile, profile[1:]):
        vv.append(vv[-1] + math.hypot(r1 - r0, a1 - a0))
    faces = []
    nseg = len(profile) - 1
    is_disc = [abs(profile[j + 1][0] - profile[j][0]) > abs(profile[j + 1][1] - profile[j][1])
               for j in range(nseg)]
    # consecutive strip segments (tread + groove) share one unroll radius -> one island
    R = [0.0] * nseg
    j = 0
    while j < nseg:
        if is_disc[j]:
            j += 1
            continue
        j1 = j
        while j1 < nseg and not is_disc[j1]:
            j1 += 1
        rr = max(max(profile[i][0], profile[i + 1][0]) for i in range(j, j1))
        for i in range(j, j1):
            R[i] = rr
        j = j1
    for j in range(nseg):
        (ra, aa), (rb, ab) = profile[j], profile[j + 1]
        disc = is_disc[j]
        rm = R[j]
        for k in range(segs):
            k1 = (k + 1) % segs
            f = bm.faces.new((V[j][k], V[j][k1], V[j + 1][k1], V[j + 1][k]))
            if disc:
                # web / flange faces are flat annuli: planar (x, z) projection, the two sides
                # offset apart - exact, no unrolled-annulus shear
                off = 0.0 if aa + ab < 0.0 else 0.5
                for lp in f.loops:
                    c = lp.vert.co
                    lp[uv].uv = (c.x - x0 + off, c.z - z0)
            else:
                # tread / groove / bore faces: unrolled strip at the mean radius
                for lp, (kk, v) in zip(f.loops, ((k, vv[j]), (k + 1, vv[j]), (k + 1, vv[j + 1]),
                                                 (k, vv[j + 1]))):
                    lp[uv].uv = (1.0 + 2.0 * math.pi * rm * kk / segs, v)
            faces.append(f)
    bm.normal_update()
    # outward = away from the profile's interior: the tread (largest radius) faces point out
    tread = max(faces, key=lambda f: Vector((f.calc_center_median().x - x0, 0.0,
                                             f.calc_center_median().z - z0)).length)
    m = tread.calc_center_median()
    radial = Vector((m.x - x0, 0.0, m.z - z0))
    if tread.normal.dot(radial) < 0.0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
        bm.normal_update()
    return _mesh_from_bm(name, bm)


def cyl_uv(obj):
    """Metric UVMap for a cs2_kit.add_cyl part (axis = local Z, before apply_transform):
    side faces u = arc meters (angle x radius), v = z meters - one strip island, seam at the
    back; caps planar x / y meters. cs2_kit's create_cone UVs map a 5.6 cm x 5 cm roller side
    face to a 0.1 x 0.5 UV cell (5x anisotropic texels; Revision 1 "stretched texels")."""
    me = obj.data
    lay = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    co = _co_array(me)
    for p in me.polygons:
        n = p.normal
        idx = list(p.loop_indices)
        if abs(n.z) > 0.7:
            off = 0.0 if n.z > 0 else 1.0
            for li in idx:
                x, y, _z = co[me.loops[li].vertex_index]
                lay.data[li].uv = (x + off + 2.0, y)
            continue
        c = co[list(p.vertices)].mean(axis=0)
        a0 = math.atan2(c[1], c[0])
        for li in idx:
            x, y, z = co[me.loops[li].vertex_index]
            a = math.atan2(y, x)
            a += 2.0 * math.pi * round((a0 - a) / (2.0 * math.pi))
            lay.data[li].uv = (a * math.hypot(x, y), z)
    return obj


def kit_cyl(name, r, depth, loc, rot, segs, caps):
    """cs2_kit.add_cyl with the metric cylinder UVMap (cyl_uv)."""
    return cyl_uv(cs2_kit.add_cyl(name, r, depth, loc, rot, segs, caps))


def skyline_pack(sizes, W, H, rotate=False):
    """Deterministic bottom-left skyline packing of axis-aligned boxes into a W x H region:
    boxes sorted by their longer side (desc, stable); with `rotate` each box may turn 90 deg
    (whichever orientation leaves the lower top wins). Returns [(x, y, turned)] in input
    order, or None when they do not fit."""
    order = sorted(range(len(sizes)), key=lambda i: (-max(sizes[i]), -min(sizes[i]), i))
    sky = [(0.0, float(W), 0.0)]
    pos = [None] * len(sizes)
    for i in order:
        best = None
        for turned in ((False, True) if rotate else (False,)):
            w, h = (sizes[i][1], sizes[i][0]) if turned else sizes[i]
            if w > W + 1e-9 or h > H + 1e-9:
                continue
            for x0, _x1, _y in sky:
                if x0 + w > W + 1e-9:
                    continue
                y = max(s[2] for s in sky if s[0] < x0 + w - 1e-9 and s[1] > x0 + 1e-9)
                key = (y + h, y, x0, turned)
                if y + h <= H + 1e-9 and (best is None or key < best[0]):
                    best = (key, x0, y, turned, w, h)
        if best is None:
            return None
        _key, x, y, turned, w, h = best
        new = []
        for s in sky:
            if s[1] <= x + 1e-9 or s[0] >= x + w - 1e-9:
                new.append(s)
            else:
                if s[0] < x - 1e-9:
                    new.append((s[0], x, s[2]))
                if s[1] > x + w + 1e-9:
                    new.append((x + w, s[1], s[2]))
        new.append((x, x + w, y + h))
        new.sort()
        merged = []
        for s in new:
            if merged and abs(merged[-1][2] - s[2]) < 1e-9 and abs(merged[-1][1] - s[0]) < 1e-9:
                merged[-1] = (merged[-1][0], s[1], s[2])
            else:
                merged.append(s)
        sky = merged
        pos[i] = (x, y, turned)
    return pos


# ======================================================================================
# Ingest helpers (L1 split_by_material, L2 scale_axes / remap_axis)
# ======================================================================================

def _co_array(me):
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3).astype(np.float64)


def _set_co(me, arr):
    me.vertices.foreach_set("co", np.ascontiguousarray(arr, dtype=np.float32).ravel())
    me.update()


def scale_axes(obj, factors):
    """L2: per-axis scale about the origin (vertices; the object transform is identity)."""
    co = _co_array(obj.data)
    co *= np.array(factors, dtype=np.float64)
    _set_co(obj.data, co)


def remap_axis_z(obj, knots):
    """L2: piecewise-linear remap of z through `knots` ((src, dst), ...), linear beyond."""
    zs = np.array([k[0] for k in knots], np.float64)
    ws = np.array([k[1] for k in knots], np.float64)
    co = _co_array(obj.data)
    z = co[:, 2]
    out = np.interp(z, zs, ws)
    hi = z > zs[-1]
    out[hi] = ws[-1] + (z[hi] - zs[-1]) * (ws[-1] - ws[-2]) / (zs[-1] - zs[-2])
    lo = z < zs[0]
    out[lo] = ws[0] + (z[lo] - zs[0]) * (ws[1] - ws[0]) / (zs[1] - zs[0])
    co[:, 2] = out
    _set_co(obj.data, co)


def split_by_material(obj, mapping, prefix_extra, suffix):
    """
    L1: one part object per material group of `obj`: {material: (prefix, key)} ->
    [(part_obj, key)] named f"{prefix_extra}{prefix}{suffix}". Faces with an unmapped
    material RAISE (nothing is dropped silently). Parts sharing a prefix are merged.
    """
    me = obj.data
    idx = np.zeros(len(me.polygons), dtype=np.int64)
    me.polygons.foreach_get("material_index", idx)
    names = [m.name if m is not None else None for m in me.materials]
    groups = collections.OrderedDict()
    for i, nm in enumerate(names):
        if not np.any(idx == i):
            continue
        base = nm.split(".")[0] if nm else nm
        if base not in mapping:
            raise RuntimeError(f"{obj.name}: material {nm!r} has no part mapping "
                               f"({int(np.sum(idx == i))} faces)")
        prefix, key = mapping[base]
        groups.setdefault((prefix, key), []).append(i)
    parts = []
    for (prefix, key), mat_ids in groups.items():
        pname = f"{prefix_extra}{prefix}{suffix}"
        new_me = me.copy()
        new_me.name = pname
        bm = bmesh.new()
        bm.from_mesh(new_me)
        dead = [f for f in bm.faces if f.material_index not in mat_ids]
        bmesh.ops.delete(bm, geom=dead, context="FACES")
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bm.to_mesh(new_me)
        bm.free()
        new_me.materials.clear()
        part = _link(bpy.data.objects.new(pname, new_me))
        part["relief"] = "geo"
        parts.append((part, key))
    return parts


def smart_uv(obj, angle=66.0):
    """Fresh "UVMap" by smart_project (66 deg, margin 0); every other UV layer removed
    (the Maya "map1" layer included: skeptic MC3)."""
    me = obj.data
    for lay in [l.name for l in me.uv_layers]:
        me.uv_layers.remove(me.uv_layers[lay])
    me.uv_layers.new(name="UVMap")
    me.uv_layers.active = me.uv_layers["UVMap"]
    _select_only(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=0.0)
    bpy.ops.object.mode_set(mode="OBJECT")


def box_uv(obj):
    """
    One UV island per axis-aligned box part (kit add_box, open-bottom / dropped faces too):
    the side faces unrolled into one strip around the box's LONGEST axis a (u = perimeter
    meters of the b x c cross-section, v = meters along a) and both a-caps attached to the
    +b side's ends. Every face keeps a non-mirrored frame (u x v = the face normal). Returns
    False (layout untouched) unless every face is an axis-aligned quad and there are <= 6.
    Six islands per box under cs2_uv's 1/64 margin starved the unique pack (14 % coverage).
    """
    me = obj.data
    if not 0 < len(me.polygons) <= 6 or any(p.loop_total != 4 for p in me.polygons):
        return False
    norms = [p.normal for p in me.polygons]
    if any(max(abs(n.x), abs(n.y), abs(n.z)) < 0.999 for n in norms):
        return False
    co = _co_array(me)
    lo, hi = co.min(axis=0), co.max(axis=0)
    dims = hi - lo
    a = int(np.argmax(dims))
    b, c = (a + 1) % 3, (a + 2) % 3
    hb, hc = dims[b] / 2.0, dims[c] / 2.0
    cb, cc = (lo[b] + hi[b]) / 2.0, (lo[c] + hi[c]) / 2.0
    la = dims[a]
    lay = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    for p, n in zip(me.polygons, norms):
        k = max(range(3), key=lambda i: abs(n[i]))
        sgn = 1.0 if n[k] > 0 else -1.0
        for li in p.loop_indices:
            q = co[me.loops[li].vertex_index]
            pa, pb, pc = q[a] - lo[a], q[b] - cb, q[c] - cc
            if k == a:
                u, v = pc + hc, (la + (hb - pb)) if sgn > 0 else (pb - hb)
            elif k == b and sgn > 0:
                u, v = pc + hc, pa
            elif k == c and sgn > 0:
                u, v = 2 * hc + (hb - pb), pa
            elif k == b:
                u, v = 2 * hc + 2 * hb + (hc - pc), pa
            else:
                u, v = 4 * hc + 2 * hb + (pb + hb), pa
            lay.data[li].uv = (u, v)
    return True


def assert_uv_layers(parts, expect=("UVMap",), stage=""):
    """MC3: every part carries exactly the expected UV layer list (no "map1")."""
    report = {}
    bad = []
    for obj, _key in parts:
        lays = tuple(l.name for l in obj.data.uv_layers)
        report[obj.name] = list(lays)
        if lays != tuple(expect):
            bad.append((obj.name, lays))
    if bad:
        raise RuntimeError(f"UV layers {stage}: expected exactly {list(expect)} on every part, "
                           f"got {bad[:6]}")
    return report


def strip_ingest_props(obj):
    for k in (cs2_ingest.SRC_NAME_PROP, cs2_ingest.SRC_PATH_PROP, cs2_ingest.SRC_FILE_PROP):
        if k in obj.keys():
            del obj[k]


# ======================================================================================
# GONDOLA parts
# ======================================================================================

def _boundary_loops(bm):
    bnd = [e for e in bm.edges if e.is_boundary]
    seen, loops = set(), []
    for e in bnd:
        if e.index in seen:
            continue
        stack, comp = [e], []
        while stack:
            x = stack.pop()
            if x.index in seen:
                continue
            seen.add(x.index)
            comp.append(x)
            for v in x.verts:
                for e2 in v.link_edges:
                    if e2.is_boundary and e2.index not in seen:
                        stack.append(e2)
        loops.append(comp)
    return bnd, loops


def door_plug(shell, report):
    """Fill the shell's holes (door opening + two door-side corner holes) into a separate
    part doorplug_shell (glass): 3 loops, 17 tris (skeptic MC6). The shell keeps its
    boundary; the join's remove_doubles welds plug and shell (0 boundary edges, asserted)."""
    bm = bmesh.new()
    bm.from_mesh(shell.data)
    bm.edges.index_update()
    bnd, loops = _boundary_loops(bm)
    report["plug_loops"] = len(loops)
    report["plug_loop_edges"] = sorted(len(c) for c in loops)
    if len(loops) != G["plug_loops"]:
        bm.free()
        raise RuntimeError(f"door plug: {len(loops)} boundary loops, expected {G['plug_loops']}")
    tag = bm.faces.layers.int.new("cabin_plug")      # before any face reference is taken
    bnd = [e for e in bm.edges if e.is_boundary]
    res = bmesh.ops.holes_fill(bm, edges=bnd, sides=0)
    new = list(res["faces"])
    tri = bmesh.ops.triangulate(bm, faces=new, quad_method="BEAUTY", ngon_method="BEAUTY")
    new = list(tri["faces"])
    centre = Vector((0.0, 0.0, 1.2))
    flip = [f for f in new if f.normal.dot(f.calc_center_median() - centre) < 0.0]
    if flip:
        bmesh.ops.reverse_faces(bm, faces=flip)
    after_bnd = sum(1 for e in bm.edges if e.is_boundary)
    nonman = sum(1 for e in bm.edges if not e.is_manifold)
    report["plug_tris"] = len(new)
    report["plug_boundary_after"] = after_bnd
    report["plug_nonmanifold_after"] = nonman
    if len(new) != G["plug_tris"] or after_bnd or nonman:
        bm.free()
        raise RuntimeError(f"door plug: {len(new)} tris (expected {G['plug_tris']}), "
                           f"{after_bnd} boundary / {nonman} non-manifold edges left")
    for f in new:
        f[tag] = 1
    bm_plug = bm.copy()
    bm.free()
    tagp = bm_plug.faces.layers.int["cabin_plug"]
    bmesh.ops.delete(bm_plug, geom=[f for f in bm_plug.faces if f[tagp] == 0], context="FACES")
    loose = [v for v in bm_plug.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm_plug, geom=loose, context="VERTS")
    bm_plug.faces.layers.int.remove(tagp)
    me = bpy.data.meshes.new("doorplug_shell")
    bm_plug.to_mesh(me)
    bm_plug.free()
    me.materials.clear()
    plug = _link(bpy.data.objects.new("doorplug_shell", me))
    plug["relief"] = "geo"
    if cs2_asset.tri_count(plug) != G["plug_tris"]:
        raise RuntimeError(f"door plug part: {cs2_asset.tri_count(plug)} tris")
    return plug


#: Revision 1 shell bevels (bmesh.ops.bevel on the welded shell + plug, AFTER the ingest asserts):
#: (selector, width m, segments). Order matters: the frame chamfer first (it never reaches the
#: roof or skirt rings), then the roof crown and the skirt foot.
SHELL_BEVELS = (
    ("frames", 0.012, 1),        # window-band frame edges: a small chamfer catches a highlight
    ("crown", 0.07, 3),          # roof perimeter ring: a real radius instead of a hard rim
    ("skirt", 0.05, 2),          # skirt foot ring: softened underside edge
)
#: Revision 1b (C3): a frame lip is trim meeting glazing at 35.9-68.0 deg (80 edges measured).
#: Three more trim edges met the door-side corner-hole fill (doorplug_, counted as glazing) at
#: 124.9 deg (both end-window heads, 3.1 cm each) and 179.5 deg (one sill corner, 2.2 cm): folds,
#: not lips. Bevelling them twisted the +y end-window head strip (8 open edges per end, 12 open +
#: 4 non-manifold after the join's weld). Lips are capped at 90 deg; soften_shell asserts closure.
FRAME_LIP_MAX_DEG = 90.0


def _shell_edge_sets(bm, tag, trim_ids, glass_ids, kind):
    out = []
    for e in bm.edges:
        if len(e.link_faces) != 2:
            continue
        ang = math.degrees(e.calc_face_angle(0.0))
        z0, z1 = e.verts[0].co.z, e.verts[1].co.z
        if kind == "crown":
            ok = ang > 20.0 and min(z0, z1) >= 2.36
        elif kind == "skirt":
            ok = ang > 20.0 and max(z0, z1) <= 0.015
        else:
            # the frame lips round the glazing: trim faces meeting glass faces
            ts = [f[tag] for f in e.link_faces]
            ok = (30.0 < ang < FRAME_LIP_MAX_DEG and any(t in trim_ids for t in ts)
                  and any(t in glass_ids for t in ts) and 0.05 < min(z0, z1) and max(z0, z1) < 2.34)
        if ok:
            out.append(e)
    return out


def soften_shell(parts, report):
    """Revision 1: weld the shell parts (body / roof / trim / hw / glass / door plug) into one
    closed bmesh, bevel the frame edges (1 seg), the roof crown ring (3 segs) and the skirt foot
    ring (2 segs), and split the faces back into their parts by a face tag (new bevel faces
    inherit an adjacent face's tag). The ingest facts (739 -> 571, plug 3 loops / 17 tris, the
    material split) are asserted BEFORE this, on the untouched Wilin topology."""
    shell = [(o, k) for o, k in parts if o.name.endswith("shell")]
    names = [o.name for o, _k in shell]
    trim_ids = {i for i, n in enumerate(names) if n.startswith("trim_")}
    glass_ids = {i for i, n in enumerate(names) if n.startswith(("glass_", "doorplug_"))}
    bm = bmesh.new()
    tag = bm.faces.layers.int.new("src")
    for i, (o, _k) in enumerate(shell):
        me = o.data
        vs = [bm.verts.new(v.co) for v in me.vertices]
        for p in me.polygons:
            f = bm.faces.new([vs[j] for j in p.vertices])
            f[tag] = i
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    before = len(bm.faces)
    nonman = sum(1 for e in bm.edges if not e.is_manifold)
    rows = {}
    for kind, width, segs in SHELL_BEVELS:
        edges = _shell_edge_sets(bm, tag, trim_ids, glass_ids, kind)
        rows[kind] = {"edges": len(edges), "width_m": width, "segments": segs}
        if not edges:
            continue
        bmesh.ops.bevel(bm, geom=edges, offset=width, offset_type="OFFSET", segments=segs,
                        profile=0.5, affect="EDGES", clamp_overlap=True, loop_slide=True)
    # Revision 1b (C3): even without the folds, the frame bevel cannot offset the +x end's
    # door-side head corner (the plug corner fill meets the soffit there): it leaves two
    # coincident vertices, a zero-area triangle and a twisted head strip (4 open edges). Welding
    # exact coincidences (1 um) rebuilds that strip as one valid facet that tapers into the
    # corner; nothing else in the shell is within 1 um of another vertex.
    nv = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    welded = nv - len(bm.verts)
    # the skirt bevel lifts the foot ring off z 0 by < 1 mm: re-seat it (G1 / G8 base pivot)
    zmin = min(v.co.z for v in bm.verts)
    seated = 0
    for v in bm.verts:
        if v.co.z <= zmin + 0.0015:
            v.co.z = 0.0
            seated += 1
    rows["foot_reseated"] = {"verts": seated, "lift_m": round(zmin, 5)}
    # Revision 1b (C3): the softened shell must still be one closed 2-manifold
    open_e = [e for e in bm.edges if len(e.link_faces) == 1]
    nonman_e = [e for e in bm.edges if len(e.link_faces) > 2]
    if open_e or nonman_e:
        where = [tuple(round(c, 3) for c in e.verts[0].co) for e in (open_e + nonman_e)[:4]]
        bm.free()
        raise RuntimeError(f"G11 soften_shell: {len(open_e)} open / {len(nonman_e)} non-manifold "
                           f"edges after the bevels (e.g. at {where}); the shell must stay closed")
    bm.normal_update()
    tris_after = sum(len(f.verts) - 2 for f in bm.faces)
    for i, (o, _k) in enumerate(shell):
        sub = bm.copy()
        st = sub.faces.layers.int["src"]
        bmesh.ops.delete(sub, geom=[f for f in sub.faces if f[st] != i], context="FACES")
        loose = [v for v in sub.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(sub, geom=loose, context="VERTS")
        sub.faces.layers.int.remove(st)
        sub.to_mesh(o.data)
        sub.free()
        o.data.update()
    bm.free()
    report["soften"] = {"bevels": rows, "faces_before": before, "nonmanifold_before": nonman,
                        "welded_coincident": welded, "open_after": 0, "nonmanifold_after": 0,
                        "shell_tris_after": tris_after,
                        "part_tris": {o.name: cs2_asset.tri_count(o) for o, _k in shell}}
    print(f"[cabin] soften_shell: {rows} -> shell {tris_after} tris")
    return report["soften"]


def door_reveal_and_handles(parts, report):
    """Revision 1 doors: each leaf (body / trim / glass / hw of one side) shrinks G door_reveal
    per side about its own x center, so the dark door plug (the opening behind the leaves)
    shows as a recessed reveal round both leaves and in the meeting seam; a grab handle (bar on
    two stand-offs, galv) sits on each leaf beside the seam, on the leaf's outer surface found
    by ray cast."""
    from mathutils.bvhtree import BVHTree
    d = G["door_reveal"]
    out = []
    for side in ("l", "r"):
        leaf = [o for o, _k in parts if o.name.startswith("door") and o.name.endswith(f"_{side}")
                and not o.name.startswith("doorplug")]
        co_all = np.concatenate([_co_array(o.data) for o in leaf])
        x0, x1 = float(co_all[:, 0].min()), float(co_all[:, 0].max())
        xc, half = 0.5 * (x0 + x1), 0.5 * (x1 - x0)
        k = (half - d) / half
        for o in leaf:
            co = _co_array(o.data)
            co[:, 0] = xc + (co[:, 0] - xc) * k
            _set_co(o.data, co)
        # grab handle on the leaf's lower (skirt) panel beside the seam, z 0.46 .. 0.74: the
        # skirt leans in there, so bar + stand-off stay inside G6 / CARRIER_GD (y <= 1.10)
        bm = bmesh.new()
        for o in leaf:
            bm.from_mesh(o.data)
        tree = BVHTree.FromBMesh(bm)
        bm.free()
        xs = -1.0 if side == "l" else 1.0
        xh = xs * (d + 0.07)
        zb, zt = 0.46, 0.74
        pts = []
        for z in (zb + 0.02, 0.5 * (zb + zt), zt - 0.02):
            loc, nrm, _i, _dd = tree.ray_cast(Vector((xh, 3.0, z)), Vector((0.0, -1.0, 0.0)))
            if loc is None:
                raise RuntimeError(f"door handle: ray at x {xh:+.3f} z {z} missed leaf {side}")
            pts.append((loc, nrm))
        y_face = max(p[0].y for p in pts)
        r_bar = 0.012
        off = min(0.032, G["extents"]["y_hi"] - 0.006 - r_bar - y_face)
        if off < 0.015:
            raise RuntimeError(f"door handle {side}: leaf face y {y_face:.3f} leaves {off:.3f} m "
                               f"to G6 y {G['extents']['y_hi']}")
        bar = kit_cyl(f"doorhandle_{side}bar", r_bar, zt - zb, (xh, y_face + off,
                                                                         0.5 * (zb + zt)),
                              (0.0, 0.0, 0.0), 6, "both")
        out.append((bar, "galv"))
        for j, (loc, _n) in enumerate((pts[0], pts[2])):
            yf = loc.y
            post = kit_cyl(f"doorhandle_{side}post{j}", 0.008, off + (y_face - yf) + 0.01,
                                   (xh, 0.5 * (yf - 0.01 + y_face + off), loc.z),
                                   (math.pi / 2.0, 0.0, 0.0), 4, "none")
            out.append((post, "galv"))
        report.setdefault("door_reveal", {})[side] = {"x_before": [round(x0, 4), round(x1, 4)],
                                                      "scale_x": round(k, 5),
                                                      "handle_y_face": round(y_face, 4),
                                                      "handle_offset": round(off, 4)}
    for o, _k in out:
        apply_transform(o)
    return out


def ingest_gondola(src, report):
    """cabins.md section 3.2 (order load-bearing). Returns ([(part, key)], shell_bvh_obj)."""
    if not os.path.isfile(src):
        raise FileNotFoundError(
            f"{src} missing: the Wilin original is gitignored - re-download it from the URL in "
            f"assets/external/wilin-gondola/SOURCE.md and place it at that path")
    load_rep = {}
    objs = cs2_ingest.load_external(src, objects=[G["subtree"]], report=load_rep)
    paths = {o.name: o[cs2_ingest.SRC_PATH_PROP] for o in objs}
    kept = {}
    for role, tail in G["keep"].items():
        hit = [o for o in objs if paths[o.name].endswith(tail)
               and paths[o.name].split("/")[-1] == tail.split("/")[-1]]
        if len(hit) != 1:
            raise RuntimeError(f"ingest: {len(hit)} objects match {tail!r}")
        kept[role] = hit[0]
    dropped = [o for o in objs if o not in kept.values()]
    report["source"] = os.path.relpath(src, REPO).replace("\\", "/")
    report["subtree_meshes"] = len(objs)
    report["dropped"] = collections.Counter(paths[o.name].split("/")[2] for o in dropped)
    report["dropped_tris"] = int(sum(cs2_asset.tri_count(o) for o in dropped))
    report["shell_raw_tris"] = cs2_asset.tri_count(kept["shell"])
    if report["shell_raw_tris"] != G["shell_raw_tris"]:
        raise RuntimeError(f"ingest: shell {report['shell_raw_tris']} tris, expected "
                           f"{G['shell_raw_tris']} (wrong source file?)")
    cs2_ingest.discard(dropped)
    keep = [kept["shell"], kept["door_l"], kept["door_r"]]
    report["scale"] = cs2_ingest.rescale_by_reference(keep, 1.0, "Z", G["ingest_scale"])
    cs2_ingest.reframe(keep, forward="-X", up="+Z", pivot="base", pivot_ref=kept["shell"])
    for o in keep:
        scale_axes(o, G["plan_scale"])
        remap_axis_z(o, G["z_knots"])
    cleans = {}
    for role in ("shell", "door_l", "door_r"):
        cleans[role] = cs2_ingest.clean(kept[role])["tris"]
    report["clean_tris"] = cleans
    if cleans["shell"] != G["shell_clean_tris"] or cleans["door_l"] != G["door_tris"] \
            or cleans["door_r"] != G["door_tris"]:
        raise RuntimeError(f"ingest: clean gave {cleans}, expected shell "
                           f"{G['shell_clean_tris']} / doors {G['door_tris']}")
    lo, hi = cs2_ingest.world_bounds([kept["shell"]])
    report["shell_bbox"] = [[round(v, 4) for v in lo], [round(v, 4) for v in hi]]
    for role in ("door_l", "door_r"):
        lo, hi = cs2_ingest.world_bounds([kept[role]])
        report[f"{role}_bbox"] = [[round(v, 4) for v in lo], [round(v, 4) for v in hi]]
    plug = door_plug(kept["shell"], report)

    # BVH of the closed shell (shell + plug) for the logo fit, in the authoring frame
    bvh_obj = kept["shell"].copy()
    bvh_obj.data = kept["shell"].data.copy()
    _link(bvh_obj)

    parts = split_by_material(kept["shell"], G_MATERIAL_MAP, "", "shell")
    parts += [(plug, "glass")]
    parts += split_by_material(kept["door_l"], G_MATERIAL_MAP, "door", "l")
    parts += split_by_material(kept["door_r"], G_MATERIAL_MAP, "door", "r")
    split = collections.Counter()
    for obj, _k in parts:
        pre = obj.name[:obj.name.index("_") + 1]
        split[pre] += cs2_asset.tri_count(obj)
    report["split_tris"] = dict(sorted(split.items()))
    for pre, n in G_SPLIT_EXPECT.items():
        want = n * (2 if pre.startswith("door") else 1)
        if split.get(pre) != want:
            raise RuntimeError(f"ingest: material split {pre} {split.get(pre)} tris, "
                               f"expected {want} (cabins.md section 3.2)")
    cs2_ingest.discard(keep)
    report["uv_before"] = sorted({tuple(l.name for l in o.data.uv_layers) for o, _ in parts})
    # Revision 1 cleanup (ingest facts above stay asserted on the untouched Wilin topology)
    soften_shell(parts, report)
    handles = door_reveal_and_handles(parts, report)
    for obj, _k in parts:
        # the stamp-stripe fill parts unwrap at a lower angle: the 66 deg charts of the corner
        # pillars are C-shaped (11 % of their AABB), which starves a box packer
        fill = obj.name.startswith(G["fill_prefixes"])
        smart_uv(obj, G["fill_uv_angle"] if fill else 66.0)
        strip_ingest_props(obj)
    report["uv_after"] = assert_uv_layers(parts, stage="after the ingest re-UV")
    report["plug_tris_shipped"] = sum(cs2_asset.tri_count(o) for o, _k in parts
                                      if o.name.startswith("doorplug_"))
    return parts + handles, bvh_obj


def fit_logo(bvh_obj, end, name):
    """Logo quad on an end skirt: 5 rays along -end*X, least-squares plane x = a + b y + c z,
    corners offset along the outward normal by (max residual + 0.004) (cabins.md 3.1)."""
    from mathutils.bvhtree import BVHTree
    bm = bmesh.new()
    bm.from_mesh(bvh_obj.data)
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    s, zc = G["logo_size"] / 2.0, G["logo_zc"]
    samples = [(0.0, zc), (-s, zc - s), (s, zc - s), (s, zc + s), (-s, zc + s)]
    hits = []
    for y, z in samples:
        loc, _n, _i, _d = tree.ray_cast(Vector((end * 3.0, y, z)), Vector((-end, 0.0, 0.0)))
        if loc is None:
            raise RuntimeError(f"logo fit: ray at y {y}, z {z} missed the {end:+d} end skirt")
        hits.append(loc)
    A = np.array([[1.0, h.y, h.z] for h in hits])
    bx = np.array([h.x for h in hits])
    coef, *_ = np.linalg.lstsq(A, bx, rcond=None)
    resid = np.abs(A @ coef - bx)
    nrm = Vector((1.0, -coef[1], -coef[2])).normalized() * end
    off = float(resid.max()) + 0.004
    corners = []
    for y, z in samples[1:]:
        p = Vector((coef[0] + coef[1] * y + coef[2] * z, y, z))
        corners.append(p + nrm * off)
    obj = quad_face(name, corners, nrm)
    fit = {"coef": [round(float(c), 5) for c in coef],
           "max_residual": round(float(resid.max()), 5), "offset": round(off, 5)}
    return obj, fit


def gondola_parts(args, report):
    """All gondola parts in the authoring frame: [(obj, material key)].

    Revision 1 (verdict item 6): every rebuilt part is soft-edged and connected -
    rounded roof rails carrying the hanger arm on a foot block and a saddle clamp; ONE smooth
    12-segment hanger tube (no flat gussets); a rounded hanger head the tube ends in; two coil
    springs up to a rounded carriage frame; 12-segment rollers that overlap the frame sides
    (no coplanar caps); an L-section grip (one closed prism, top = 4.50) on a stem; rounded
    bumpers; a rounded roof hat; the plate and logos as plaques, not floating quads. All of it
    inside tram_tower.CARRIER_GD in both pin frames (seams S6)."""
    parts, bvh_obj = ingest_gondola(args.source, report)
    K = cs2_kit
    proc = []
    za, zb = 2.40, 2.52                                  # roof rail underside / top
    for k, y in enumerate(G["yoke_y"]):
        yk = rounded_box(f"yoke_{k}", (1.06, 0.18, zb - za + 0.02), (0.0, y, 0.5 * (za - 0.02 + zb)),
                         0.03, segs=1, open_bottom=True)
        yk["relief"] = "map:bolts"
        proc.append((yk, "galv"))
    r = G["hanger_r"]
    arm = K.tube_along("hanger_arm", [G["hanger_a"], G["hanger_b"], G["hanger_d"]], r,
                       segments=G["hanger_segs"], caps="start", bend_rings=G["hanger_bend_rings"],
                       fillet=G["hanger_fillet"])
    arm["relief"] = "map:seams"
    proc.append((arm, "galv"))
    ya, yb = G["hanger_a"][1], G["hanger_b"][1]
    zarm = G["hanger_a"][2]
    # foot block over the arm end (on rail 0) and a saddle clamp round the arm (on rail 1)
    foot = rounded_box("hangerclamp_foot", (0.24, 0.20, 0.17), (0.0, ya, zb - 0.02 + 0.085),
                       0.03, segs=1)
    foot["relief"] = "map:bolts"
    proc.append((foot, "galv"))
    sad = rounded_box("hangerclamp_saddle", (0.22, 0.10, 0.17),
                      (0.0, G["yoke_y"][1], zb - 0.02 + 0.085), 0.025, segs=1)
    sad["relief"] = "map:bolts"
    proc.append((sad, "galv"))
    # hanger head (the tube ends inside it), springs, carriage frame, rollers, grip
    zd = G["hanger_d"][2]
    head = rounded_box("hanger_head", (0.40, 0.19, 0.13), (0.0, yb, 3.995), 0.03, segs=2)
    head["relief"] = "map:bolts"
    proc.append((head, "galv"))
    for k, x in enumerate((-0.12, 0.12)):
        sp = kit_cyl(f"spring_{k}", 0.045, 0.14, (x, yb, 4.11), (0.0, 0.0, 0.0), 12, "none")
        sp["relief"] = "map:seams"
        proc.append((sp, "paint_dark"))
    frame = rounded_box("carframe_main", (1.10, 0.42, 0.12), (0.0, 0.50, 4.22), 0.025, segs=2)
    frame["relief"] = "map:bolts"
    proc.append((frame, "galv"))
    n = 0
    for x in (-0.45, -0.15, 0.15, 0.45):
        for y, cap in ((0.275, "bottom"), (0.725, "top")):
            # axis Y: the cylinder's local +Z maps to +Y, so the OUTER cap is "bottom" on the
            # inboard row and "top" on the outboard row; the inner cap is buried in the frame
            # (rollers overlap the frame sides by 1 cm: no coplanar cap / frame faces)
            # --split: 16 segments (critique 2026-11-03: the 10-gon read as a polygon); the
            # one-piece A keeps its 10
            segs = G_SPLIT["roller_segs"] if getattr(args, "split", False) else G["roller_segs"]
            proc.append((kit_cyl(f"roller_{n}", 0.09, 0.05, (x, y, 4.21),
                                 cs2_kit._axis_rot("Y"), segs, cap), "galv"))
            n += 1
    # grip: the jaw bar (top = 4.50 = bounds max, G2 footprint x +-0.15 / y -0.04..0.30) and
    # the jaw cheek as ONE L-section prism (no coplanar bar / jaw faces), chamfered lower edges
    grip = extrude_profile("grip_body", [(-0.04, 4.455), (-0.04, 4.50), (0.30, 4.50),
                                         (0.30, 4.30), (0.115, 4.30), (0.10, 4.315),
                                         (0.10, 4.44), (-0.025, 4.44)],
                           -0.15, 0.15, axis="X")
    grip["relief"] = "map:bolts"
    proc.append((grip, "galv"))
    # stem: bridges the 2 cm between the grip (4.30) and the frame top (4.28), overlapping both;
    # its outboard face sits 2 mm inside the grip's y 0.30 face (Revision 1b C4: no flush strip)
    proc.append((K.add_box("grip_stem", (0.10, 0.048, 0.09), (0.0, 0.274, 4.265)), "galv"))
    n = 0
    for x in (-1.13, 1.13):
        for y in (-0.80, 0.80):
            # the face against the end skirt is buried: dropped (fewer bevel tris, no hidden face)
            proc.append((rounded_box(f"bumper_{n}", (0.10, 0.40, 0.35), (x, y, 0.775), 0.03,
                                     segs=1, drop=("-X" if x > 0 else "+X",)), "rubber"))
            n += 1
    hat = rrect_loft("vent_roof", [(za - 0.02, 0.46, 0.46, 0.10), (2.47, 0.42, 0.42, 0.08),
                                   (2.49, 0.36, 0.36, 0.05)], cap_top=True, segs=2)
    hat.data.transform(mathutils.Matrix.Translation((0.55, 0.0, 0.0)))
    smart_uv(hat)
    proc.append((hat, "paint_roof"))
    # number plate: a 12 mm plaque on the stalk's outboard side (front face = plate_face)
    pw, ph = G["plate_wh"]
    py = yb + r + 0.009
    zc = G["plate_zc"]
    pf, prim = plaque("plate_face", "plaque_plate", (0.0, py, zc), (-1.0, 0.0, 0.0),
                      (0.0, 0.0, 1.0), pw, ph, 0.012 + 0.006)
    proc += [(pf, "plate"), (prim, "galv")]
    logo_f, fit_f = fit_logo(bvh_obj, +1, "logo_front")
    logo_b, fit_b = fit_logo(bvh_obj, -1, "logo_back")
    report["logo_fit"] = {"logo_front": fit_f, "logo_back": fit_b}
    proc += [(logo_f, "paint_body"), (logo_b, "paint_body")]
    for lg, fit in ((logo_f, fit_f), (logo_b, fit_b)):
        proc.append((quad_rim(lg, "plaque_" + lg.name, fit["offset"] + 0.006), "paint_body"))
    me = bvh_obj.data
    bpy.data.objects.remove(bvh_obj, do_unlink=True)
    bpy.data.meshes.remove(me)
    for obj, _k in proc:
        apply_transform(obj)
    allp = parts + proc
    if args.outboard == "-Y":
        for obj, _k in allp:
            scale_axes(obj, (1.0, -1.0, 1.0))
            obj.data.flip_normals()
            obj.data.update()
    return allp


# ======================================================================================
# TRAM parts
# ======================================================================================

def _tram_side_box(name, x, s, y_face, z0, z1, wx, proud):
    """A box proud of a long-side wall (y = s * y_face), back face dropped."""
    return cs2_kit.add_box(name, (wx, proud, z1 - z0), (x, s * (y_face + proud / 2.0),
                                                         (z0 + z1) / 2.0),
                           drop=("-Y" if s > 0 else "+Y",))


def _bez(p0, p1, p2, t):
    """Quadratic Bezier between equal-length tuples (the profile fillets)."""
    return tuple((1 - t) ** 2 * a + 2 * t * (1 - t) * b + t ** 2 * c for a, b, c in zip(p0, p1, p2))


def _lerp(p, q, t):
    return tuple(a + (b - a) * t for a, b in zip(p, q))


def tram_profile():
    """
    Revision 1 tram body section (T profile): [(run, [(z, half L, half W, corner r), ...])],
    bottom -> top. Runs share their boundary rings. The boat bottom gets two rounded edges
    (underside -> chamfer, chamfer -> band), the glazing sits T reveal behind its frames (flat
    annular sills and heads, offset rings so the reveal is even round the corners), and the
    roof edge is a rounded crown. Every fillet is a quadratic Bezier whose start / end share
    their z in both plan axes, so each ring stays planar.
    """
    K = [tuple(k) for k in T["profile"]]
    r0, rv = T["corner_r"], T["reveal"]
    bf, cf, cr = T["boat_fillet"], T["chamfer_fillet"], T["crown"]

    def ring(p, r=r0):
        return (p[0], p[1], p[2], r)

    def inset(p, d):
        return (p[0], p[1] - d, p[2] - d, r0 - d)

    S = (0.0, K[0][1] - bf["run"][0], K[0][2] - bf["run"][1])
    E = _lerp(K[0], K[1], bf["up"] / (K[1][0] - K[0][0]))
    boat = [ring(_bez(S, K[0], E, t / bf["steps"])) for t in range(bf["steps"] + 1)]
    F = _lerp(K[0], K[1], (K[1][0] - cf["down"] - K[0][0]) / (K[1][0] - K[0][0]))
    Gp = _lerp(K[1], K[2], cf["up"] / (K[2][0] - K[1][0]))
    fil = [ring(_bez(F, K[1], Gp, t / cf["steps"])) for t in range(cf["steps"] + 1)]
    mid = cf["steps"] // 2
    CS = (K[7][0] - cr["down"], K[7][1], K[7][2])
    CE = (K[7][0], K[7][1] - cr["run"], K[7][2] - cr["run"])
    crown = []
    for t in range(cr["steps"] + 1):
        p = _bez(CS, K[7], CE, t / cr["steps"])
        crown.append((p[0], p[1], p[2], r0 - (K[7][2] - p[2])))
    runs = [
        ("chamfer", boat + fil[:mid + 1]),
        ("band", fil[mid:] + [ring(K[2])]),
        ("trim", [ring(K[2]), ring(K[3])]),
        ("reveal_lo", [ring(K[3]), inset(K[3], rv)]),
        ("glass_main", [inset(K[3], rv), inset(K[4], rv)]),
        ("reveal_hi", [inset(K[4], rv), ring(K[4])]),
        ("sill", [ring(K[4]), ring(K[5])]),
        ("reveal_lo2", [ring(K[5]), inset(K[5], rv)]),
        ("glass_tran", [inset(K[5], rv), inset(K[6], rv)]),
        ("reveal_hi2", [inset(K[6], rv), ring(K[6])]),
        ("frame", [ring(K[6])] + crown),
    ]
    return runs


#: run -> (part prefix + name stem, material key); runs in one UV group share their ring
#: seams in UV too (one island per piece and group).
TRAM_RUNS = {
    "chamfer": ("chamfer_body", "paint_frame"), "band": ("band_body", "paint_band"),
    "trim": ("trim_body", "paint_trim"), "reveal_lo": ("reveal_lo", "paint_frame"),
    "glass_main": ("glass_main", "glass"), "reveal_hi": ("reveal_hi", "paint_frame"),
    "sill": ("sill_body", "paint_frame"), "reveal_lo2": ("reveal_lotr", "paint_frame"),
    "glass_tran": ("glass_transom", "glass"), "reveal_hi2": ("reveal_hitr", "paint_frame"),
    "frame": ("frame_upper", "paint_frame"),
}
TRAM_UV_GROUPS = (("chamfer",), ("band",), ("trim",), ("reveal_lo",), ("glass_main",),
                  ("reveal_hi", "sill", "reveal_lo2"), ("glass_tran",), ("reveal_hi2", "frame"))
#: islands of a PINNED group (stamp_layout) must carry one material: the band and the frame
#: runs never share an island with the trim or the glass


def _run_piece(name, rings, i0, i1, v_at, u_ref):
    """One piece of a run: quads between consecutive rings over ring points i0..i1 (wrapping
    the 20-point ring). UV u = meters along the group's reference ring `u_ref` (the same u for
    a column on every ring: per-ring arcs shear the strip - the boat-bottom rings shrink 1.6 m
    end to end over a 0.34 m slant), v = v_at[j][i] (cumulative slant meters down each COLUMN:
    the boat ends slope 2x steeper than the sides)."""
    n = len(rings[0])
    idx = [(i0 + k) % n for k in range(((i1 - i0) % n) + 1)]
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    V = [[bm.verts.new(ring[i]) for i in idx] for ring in rings]
    acc = [0.0]
    for a, b in zip(idx, idx[1:]):
        acc.append(acc[-1] + (Vector(u_ref[b]) - Vector(u_ref[a])).length)
    U = [acc] * len(rings)
    for j in range(len(rings) - 1):
        for k in range(len(idx) - 1):
            f = bm.faces.new((V[j][k], V[j][k + 1], V[j + 1][k + 1], V[j + 1][k]))
            ka, kb = idx[k], idx[k + 1]
            for lp, (u, v) in zip(f.loops, ((U[j][k], v_at[j][ka]), (U[j][k + 1], v_at[j][kb]),
                                            (U[j + 1][k + 1], v_at[j + 1][kb]),
                                            (U[j + 1][k], v_at[j + 1][ka]))):
                lp[uv].uv = (u, v)
    bm.normal_update()
    return _mesh_from_bm(name, bm)


def tram_body_parts():
    """The tram body as 4 UV pieces per run (T split_at: cuts at the corner-arc midpoints,
    so no island is a 22 m strip that caps the unique pack's scale), underside and roof caps.
    Returns ([(obj, key)], {"wall_y": .., "glass_y": .., "end_x": .., "roof_z": ..})."""
    runs = dict(tram_profile())
    segs = T["corner_segs"]
    rings = {nm: [rrect_ring(2 * hl, 2 * hw, r, segs, z) for z, hl, hw, r in specs]
             for nm, specs in runs.items()}
    cuts = T["split_at"]
    out = []
    for group in TRAM_UV_GROUPS:
        # cumulative slant per group (mean over the ring points), runs share boundary rings
        v_at = {}
        acc = np.zeros(len(rings[group[0]][0]))
        for nm in group:
            rr = rings[nm]
            vals = [acc.copy()]
            for a, b in zip(rr, rr[1:]):
                acc = acc + np.array([(Vector(q) - Vector(p)).length for p, q in zip(a, b)])
                vals.append(acc.copy())
            v_at[nm] = vals
        ref = max((r for nm in group for r in rings[nm]),
                  key=lambda r: sum((Vector(r[(i + 1) % len(r)]) - Vector(r[i])).length
                                    for i in range(len(r))))
        for nm in group:
            stem, key = TRAM_RUNS[nm]
            for k, i0 in enumerate(cuts):
                i1 = cuts[(k + 1) % len(cuts)]
                obj = _run_piece(f"{stem}{k}", rings[nm], i0, i1, v_at[nm], ref)
                if stem.startswith(("band_", "frame_")):
                    obj["relief"] = "map:seams"
                out.append((obj, key))
    under = rings["chamfer"][0]
    roof = rings["frame"][-1]
    out.append((ring_cap("under_body", under, up=False), "paint_frame"))
    out.append((ring_cap("roof_body", roof, up=True), "paint_frame"))
    K = T["profile"]
    geo = {"wall_y": K[1][2], "glass_y": K[1][2] - T["reveal"], "end_x": K[1][1],
           "roof_z": K[7][0], "roof_half": (K[7][1] - T["crown"]["run"],
                                            K[7][2] - T["crown"]["run"])}
    return out, geo


#: Revision 2: right-handed planar UV frames (u x v = the face normal) per dominant normal axis
#: and sign, meters. _planar_uv mirrors the +-Y faces; these never mirror a texel.
_RH_FRAMES = {(0, 1): ((0, 1, 0), (0, 0, 1)), (0, -1): ((0, -1, 0), (0, 0, 1)),
              (1, 1): ((-1, 0, 0), (0, 0, 1)), (1, -1): ((1, 0, 0), (0, 0, 1)),
              (2, 1): ((1, 0, 0), (0, 1, 0)), (2, -1): ((1, 0, 0), (0, -1, 0))}


def poly_part(name, polys, split=False):
    """Revision 2: one part from explicit planar polygons [(corners, outward normal), ...].
    Corners at the same position (1e-7 m) are ONE vertex, so neighboring polygons (and parts,
    after the join's remove_doubles) share edges; every face is wound to its outward normal;
    UVMap = right-handed planar meters per dominant axis (_RH_FRAMES), so the faces of one plane
    form one non-mirrored island. split=True gives every face its own island (a frame annulus
    packs as four tight strips instead of one box around the window hole)."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    verts = {}

    def V(p):
        k = tuple(round(float(c), 7) for c in p)
        if k not in verts:
            verts[k] = bm.verts.new(tuple(float(c) for c in p))
        return verts[k]

    faces = []
    for corners, outward in polys:
        faces.append((bm.faces.new([V(c) for c in corners]), Vector(outward)))
    bm.normal_update()
    for f, n in faces:
        if f.normal.dot(n) < 0.0:
            bmesh.ops.reverse_faces(bm, faces=[f])
    bm.normal_update()
    for i, (f, n) in enumerate(faces):
        ax = max(range(3), key=lambda i: abs(n[i]))
        fu, fv = _RH_FRAMES[(ax, 1 if n[ax] > 0 else -1)]
        U, W = Vector(fu), Vector(fv)
        du = 10.0 * i if split else 0.0
        for lp in f.loops:
            lp[uv].uv = (lp.vert.co.dot(U) + du, lp.vert.co.dot(W))
    return _mesh_from_bm(name, bm)


def _ring_quads(x0, x1, z0, z1, hx0, hx1, hz0, hz1, y, s, t):
    """The 4 planar trapezoids of a rectangle (x0..x1, z0..z1) with a rectangular hole
    (hx0..hx1, hz0..hz1), in the plane y (side s, leaf t mirrors x), facing +s y."""
    def P(x, z):
        return (t * x, s * y, z)
    n = (0.0, float(s), 0.0)
    return [([P(x0, z0), P(x1, z0), P(hx1, hz0), P(hx0, hz0)], n),
            ([P(x1, z0), P(x1, z1), P(hx1, hz1), P(hx1, hz0)], n),
            ([P(x1, z1), P(x0, z1), P(hx0, hz1), P(hx1, hz1)], n),
            ([P(x0, z1), P(x0, z0), P(hx0, hz0), P(hx0, hz1)], n)]


def _side_box(name, x0, x1, y0, y1, z0, z1, s, t=1, drop=()):
    """cs2_kit.add_box over |x| x0..x1 (leaf t), |y| y0..y1 on side s, z0..z1; `drop` names the
    canonical (+x leaf, +y side) faces "in" / "out" (y) / "meet" / "edge" (x) / "top" / "bottom",
    mapped to the mirrored keys (box_uv: one island)."""
    xa, xb = sorted((t * x0, t * x1))
    ya, yb = sorted((s * y0, s * y1))
    key = {"out": "+Y" if s > 0 else "-Y", "in": "-Y" if s > 0 else "+Y",
           "edge": "+X" if t > 0 else "-X", "meet": "-X" if t > 0 else "+X",
           "top": "+Z", "bottom": "-Z"}
    return cs2_kit.add_box(name, (xb - xa, yb - ya, z1 - z0),
                           ((xa + xb) / 2.0, (ya + yb) / 2.0, (z0 + z1) / 2.0),
                           drop=tuple(key[d] for d in drop))


def tram_door_kit():
    """Revision 2: the outward-opening sliding (plug) double door on BOTH long sides (R29),
    centered on the cabin length, CLOSED, plus its hardware. Built per side s (y) and leaf t (x)
    from one canonical leaf (+x, +y); every part is mirror-exact (T5) and its UVs right-handed.
    The body behind the leaves is the unchanged Revision 1 body (no hole), so LOD2, which drops
    every door part, is the Revision 1 LOD2.

    Per leaf: doorband_ (red lower panel, PINNED in the logo stripe fill; a recessed grab-bar
    pocket cut into it), doortrim_ (the 1.06..1.10 trim line, slot 3), doorframe_ (black glazed
    frame, pinned with the frames), doorgasket_ (rubber window reveal), doorglass_ (25 mm behind
    the leaf face), doorpocket_ (rubber-lined), doorgrab_ (galv bar in the pocket), doorpull_
    (release pull on the meeting stile), doorseal_ (rubber nosing on the meeting edge), 2 x
    doorroller_ (rubber-tired) on 2 x doorhanger_ brackets riding under the header, doorjamb_
    (rubber outline on the outer edge), intercom_ (call / intercom box beside the door). Per
    door: doorsill_ (the threshold nosing in safety yellow: the "stand clear" stripe),
    doorheader_ (runner box over the opening + the slide travel), doordrip_ (drip rail above
    the transom). Every rubber part, the trim and the black frames are pinned constants
    (stamp groups), which keeps the margin-bound galv pack near its Revision 1 island count. The leaves' edge faces run back to the
    glazing plane; their back and bottom faces are dropped (buried in the body / sitting on the
    threshold)."""
    D = T["door"]
    K = T["profile"]
    yf, yb, yw = D["y_front"], D["y_back"], D["y_win"]
    x0, x1 = D["x_meet"], D["x_out"]
    zb, zt = D["z_bot"], D["z_top"]
    zband, ztrim = K[2][0], K[3][0]                       # 1.06 band top, 1.10 trim top
    xi0, xi1 = x0 + D["stile"], x1 - D["stile"]
    zi0, zi1 = ztrim + D["rail_bot"], zt - D["rail_top"]
    px0, px1, pz0, pz1, py = D["pocket"]
    out = []
    for s in (-1, 1):
        sn = "n" if s < 0 else "p"
        for t in (-1, 1):
            tag = f"{sn}{'a' if t < 0 else 'b'}"

            def P(x, y, z, _s=s, _t=t):
                return (_t * x, _s * y, z)
            ny, nx, nm = (0.0, float(s), 0.0), (float(t), 0.0, 0.0), (-float(t), 0.0, 0.0)

            def edges(za, zz, _P=P, _nx=nx, _nm=nm):
                """The leaf's meeting / outer edge faces z za..zz, glazing plane to the front."""
                return [([_P(x0, yb, za), _P(x0, yf, za), _P(x0, yf, zz), _P(x0, yb, zz)], _nm),
                        ([_P(x1, yb, za), _P(x1, yf, za), _P(x1, yf, zz), _P(x1, yb, zz)], _nx)]
            band = _ring_quads(x0, x1, zb, zband, px0, px1, pz0, pz1, yf, s, t) + edges(zb, zband)
            out.append((poly_part(f"doorband_{tag}", band), "paint_band"))
            out.append((poly_part(f"doortrim_{tag}",
                                  [([P(x0, yf, zband), P(x1, yf, zband), P(x1, yf, ztrim),
                                     P(x0, yf, ztrim)], ny)] + edges(zband, ztrim)),
                        "paint_trim"))
            frame = _ring_quads(x0, x1, ztrim, zt, xi0, xi1, zi0, zi1, yf, s, t) + edges(ztrim, zt)
            frame.append(([P(x0, yb, zt), P(x1, yb, zt), P(x1, yf, zt), P(x0, yf, zt)],
                          (0.0, 0.0, 1.0)))
            out.append((poly_part(f"doorframe_{tag}", frame, split=True), "paint_frame"))
            # window reveal (gasket) faces point INTO the opening; the glass faces out
            out.append((poly_part(f"doorgasket_{tag}", [
                ([P(xi0, yw, zi0), P(xi1, yw, zi0), P(xi1, yf, zi0), P(xi0, yf, zi0)], (0, 0, 1)),
                ([P(xi0, yw, zi1), P(xi1, yw, zi1), P(xi1, yf, zi1), P(xi0, yf, zi1)], (0, 0, -1)),
                ([P(xi0, yw, zi0), P(xi0, yw, zi1), P(xi0, yf, zi1), P(xi0, yf, zi0)], nx),
                ([P(xi1, yw, zi0), P(xi1, yw, zi1), P(xi1, yf, zi1), P(xi1, yf, zi0)], nm)]),
                "rubber"))
            out.append((poly_part(f"doorglass_{tag}", [
                ([P(xi0, yw, zi0), P(xi1, yw, zi0), P(xi1, yw, zi1), P(xi0, yw, zi1)], ny)]),
                "glass"))
            # recessed grab pocket (dark): walls face into the pocket; the bar's ends sit in
            # the pocket's top / bottom walls
            out.append((poly_part(f"doorpocket_{tag}", [
                ([P(px0, py, pz0), P(px1, py, pz0), P(px1, py, pz1), P(px0, py, pz1)], ny),
                ([P(px0, py, pz0), P(px1, py, pz0), P(px1, yf, pz0), P(px0, yf, pz0)], (0, 0, 1)),
                ([P(px0, py, pz1), P(px1, py, pz1), P(px1, yf, pz1), P(px0, yf, pz1)], (0, 0, -1)),
                ([P(px0, py, pz0), P(px0, py, pz1), P(px0, yf, pz1), P(px0, yf, pz0)], nx),
                ([P(px1, py, pz0), P(px1, py, pz1), P(px1, yf, pz1), P(px1, yf, pz0)], nm)]),
                "rubber"))
            out.append((kit_cyl(f"doorgrab_{tag}", D["grab_r"], pz1 - pz0 + 0.01,
                                (t * 0.5 * (px0 + px1), s * D["grab_y"], 0.5 * (pz0 + pz1)),
                                (0.0, 0.0, 0.0), 6, "none"), "galv"))
            ux0, ux1, uz0, uz1, uy = D["pull"]
            out.append((_side_box(f"doorpull_{tag}", ux0, ux1, yf - 0.002, uy, uz0, uz1, s, t,
                                  drop=("in",)), "galv"))
            sx0, sx1, sy0, sy1 = D["seal"]
            out.append((_side_box(f"doorseal_{tag}", sx0, sx1, sy0, sy1, zb + 0.005, zt - 0.005,
                                  s, t, drop=("edge",)), "rubber"))
            hy0, hy1 = D["hanger_y"]
            zr = D["header"][3]
            for k, dx in enumerate((-D["roller_dx"], D["roller_dx"])):
                xr = 0.5 * (x0 + x1) + dx
                out.append((_side_box(f"doorhanger_{tag}{k}", xr - 0.012, xr + 0.012, hy0, hy1,
                                      zt - 0.008, zr - 0.007, s, t, drop=("top", "bottom")),
                            "galv"))
                cap = "top" if s > 0 else "bottom"           # local +Z -> +Y: outer cap only
                out.append((kit_cyl(f"doorroller_{tag}{k}", D["roller_r"], D["roller_w"],
                                    (t * xr, s * D["roller_y"], zr), cs2_kit._axis_rot("Y"), 8,
                                    cap), "rubber"))
            jx0, jx1, jy1 = D["jamb"]
            out.append((_side_box(f"doorjamb_{tag}", jx0, jx1, yb, jy1, zb, zt, s, t,
                                  drop=("in", "bottom")), "rubber"))
            ix0, ix1, iz0, iz1, iy = D["intercom"]
            out.append((_side_box(f"intercom_{tag}", ix0, ix1, K[2][2] - 0.002, iy, iz0, iz1,
                                  s, t, drop=("in",)), "galv"))
        # per door: the threshold nosing in safety yellow (the "stand clear" stripe: the inside
        # floor edge is never visible with the doors closed, so the stripe is the step nosing
        # outside the leaves), runner box, drip rail
        hx, sy0, sy1, sz0 = D["sill"]
        out.append((_side_box(f"doorsill_{sn}", -hx, hx, sy0, sy1, sz0, zb, s, drop=("in",)),
                    "safety"))
        hx, hy0, hy1, hz0, hz1 = D["header"]
        hd = _side_box(f"doorheader_{sn}", -hx, hx, hy0, hy1, hz0, hz1, s)
        hd["relief"] = "map:bolts"
        out.append((hd, "paint_frame"))
        dx, dy0, dy1, dz0, dz1 = D["drip"]
        out.append((_side_box(f"doordrip_{sn}", -dx, dx, dy0, dy1, dz0, dz1, s, drop=("in",)),
                    "galv"))
    return out


def tram_parts(args, report, dims):  # noqa: ARG001 (report kept for the gondola signature)
    """All tram parts in the authoring frame (cabins.md 4.1-4.2 reconciled by R27-R30).

    Revision 1 (verdict item 9, "same critique as the gondola"): rounded boat
    bottom and roof crown, glazing set back behind frames (reveals), rounded box sections on
    the yoke / fin / column / fork / uprights / carriage, real wheel profiles (dished web,
    flanges, V groove), lighter bogie side plates, rounded roof equipment with seams, logo
    and plate panels as plaques, no coplanar end faces (ties, posts, links and the column
    ends overlap their hosts instead)."""
    K = cs2_kit
    out, geo = tram_body_parts()
    yw, yg, xe = geo["wall_y"], geo["glass_y"], geo["end_x"]

    # mullions: long sides x 0, +-1.25, +-2.50 from the set-back glass out to 0.03 proud of the
    # wall; ends y 0 (slanted with the end lean); 1 cm into the sill / head (no coplanar faces)
    prof = T["profile"]
    spans = ((prof[3][0] - 0.01, prof[4][0] + 0.01), (prof[5][0] - 0.01, prof[6][0] + 0.01))
    # Revision 2: the main-row center mullion is the door seam now and +-1.25 move to +-1.70
    # (T mullion_*_x); the transom's center mullion stands 1 cm into the door's runner box
    n = 0
    z_hdr_top = T["door"]["header"][4]
    for s in (-1, 1):
        for (z0, z1), xs in zip(spans, (T["mullion_main_x"], T["mullion_transom_x"])):
            for x in xs:
                za = max(z0, z_hdr_top - 0.01) if abs(x) < 1e-9 else z0
                out.append((_tram_side_box(f"mullion_{n}", x, s, yg, za, z1, 0.08,
                                           yw + 0.03 - yg), "paint_frame"))
                n += 1
    for e in (-1, 1):
        for (ka, kb) in ((3, 4), (5, 6)):
            za, ha = prof[ka][0] - 0.01, prof[ka][1]
            zb, hb = prof[kb][0] + 0.01, prof[kb][1]
            gi, go = T["reveal"], 0.03
            bot = [(e * (ha - gi), -0.04, za), (e * (ha + go), -0.04, za), (e * (ha + go), 0.04, za),
                   (e * (ha - gi), 0.04, za)]
            top = [(e * (hb - gi), -0.04, zb), (e * (hb + go), -0.04, zb), (e * (hb + go), 0.04, zb),
                   (e * (hb - gi), 0.04, zb)]
            out.append((prism(f"mullion_{n}", bot, top, drop=("s3",)), "paint_frame"))
            n += 1
    # Revision 2 doors: ONE outward-opening sliding double door per long side, centered (R29:
    # both long sides), closed, with its hardware (tram_door_kit; replaces the two 1.00 m
    # proud boxes per side at x +-1.875 of Revision 1)
    out += tram_door_kit()
    # logos: plaques (4 mm proud + 6 mm rim into the wall); long sides 0.70, ends 0.60.
    # Revision 2: the doors take the band's center, so each long side carries two logos, one
    # under each pane beside the door (x +-2.10); all six quads share the one pinned square
    zl = T["logo_zc"]
    up = Vector((0.0, 0.0, 1.0))
    for s, tag in ((-1, "n"), (1, "p")):
        nrm = Vector((0.0, s, 0.0))
        for k, xl in enumerate(T["logo_side_x"]):
            f, rim = plaque(f"logo_side{tag}{k}", f"plaque_side{tag}{k}",
                            (xl, s * (yw + 0.004), zl), tuple((-nrm).cross(up)), tuple(up),
                            0.70, 0.70, 0.010)
            out += [(f, "paint_band"), (rim, "paint_band")]
        nrm = Vector((s, 0.0, 0.0))
        f, rim = plaque(f"logo_end{tag}", f"plaque_end{tag}", (s * (xe + 0.004), 0.0, zl),
                        tuple((-nrm).cross(up)), tuple(up), 0.60, 0.60, 0.010)
        out += [(f, "paint_band"), (rim, "paint_band")]
    # roof: rounded yoke beam (2 cm into the roof), rounded fin, rounded equipment boxes,
    # gratings
    zr = geo["roof_z"]
    yb = rounded_box("yoke_beam", (T["fin_len"][0], 0.60, 0.32), (0.0, 0.0, zr + 0.14), 0.05,
                     segs=2, open_bottom=True)
    yb["relief"] = "map:bolts"
    out.append((yb, "galv"))
    L0, L1 = T["fin_len"]
    fin = rrect_loft("yoke_fin", [(zr + 0.28, L0, T["fin_t"], 0.10),
                                  (dims["fin_top"], L1, T["fin_t"], 0.10)], cap_top=True)
    smart_uv(fin)                        # a 4.80 -> 0.75 frustum: a ring strip would shear 3x
    fin["relief"] = "map:bolts"
    out.append((fin, "galv"))
    n = 0
    for x in (-2.6, 2.6):
        for y in (-1.0, 1.0):
            rb = rounded_box(f"roofbox_{n}", (1.0, 0.8, 0.52), (x, y, zr + 0.25), 0.05, segs=2,
                             open_bottom=True)
            rb["relief"] = "map:seams"
            out.append((rb, "galv"))
            n += 1
    for k, y in enumerate((-0.9, 0.9)):
        for obj, _q in K.grating(f"roof{k}", 2.0, 0.8, 0.03, (0.0, y, zr + 0.02)):
            out.append((obj, "galv"))
    # Revision 2 (gondola parity: its roof hat / hatch language): one roof escape hatch per
    # cabin end between the equipment boxes, open-bottom and 3 mm into the roof, with two hinge
    # knuckles on its inboard edge; inside tram_tower.CARRIER_TRAM's roof box (|x| <= 3.11)
    H = T["hatch"]
    hx, hy, hz = H["size"]
    for k, sx in enumerate((-1, 1)):
        out.append((K.add_box(f"hatch_{k}", (hx, hy, hz), (sx * H["x"], 0.0, zr - 0.003 + hz / 2.0),
                              open_bottom=True), "galv"))
        xh = sx * (H["x"] - hx / 2.0 - 0.003)
        hr = H["hinge_r"]
        for j, sy in enumerate((-1, 1)):
            # hinge knuckle + strap: a box half over the hatch edge, half on the roof
            out.append((K.add_box(f"hatchhinge_{k}{j}", (2.6 * hr, H["hinge_l"], 2.0 * hr),
                                  (xh, sy * H["hinge_y"], zr - 0.003 + hz - 0.6 * hr)), "galv"))
    # Revision 2 (gondola parity: its end bumpers): rubber buffers on both end walls, their
    # wall face dropped and 1 cm into the band (x 3.84 .. 3.95 < the 4.03 bound)
    B = T["bumper"]
    bx, by, bz = B["size"]
    n = 0
    for sx in (-1, 1):
        for sy in (-1, 1):
            out.append((rounded_box(f"bumper_{n}", (bx, by, bz),
                                    (sx * (xe - 0.01 + bx / 2.0), sy * B["y"], B["zc"]), B["r"],
                                    segs=1, drop=("-X" if sx > 0 else "+X",)), "rubber"))
            n += 1
    # hanger: ONE centered rounded column on the fin (R29 symmetric), ends sunk into the fin
    # and the crossbar (no coplanar caps) ...
    cw, cd = T["column_wd"]
    col_len = dims["fork_bot"] - dims["fin_top"]
    if col_len > 0.05:
        col = box_along("hanger_column", [(0, 0, dims["fin_top"] - 0.02),
                                          (0, 0, dims["fork_bot"] + 0.02)],
                        cw, cd, side=(1, 0, 0), chamfer=T["r_column"], corner_segs=2,
                        caps="start")
        col["relief"] = "map:seams"
        out.append((col, "galv"))
    # ... forked below the saddle band (R30: crossbar below dz -3.80, uprights 0.82..1.08)
    zc = (dims["fork_bot"] + dims["fork_top"]) / 2.0
    fh = T["fork_half"]
    # Revision 1b C4: the crossbar ends stop 2 mm inside the uprights' outer faces (y +-1.08)
    cb = box_along("fork_crossbar", [(0, -(fh - 0.002), zc), (0, fh - 0.002, zc)], 1.00, T["fork_h"],
                   side=(1, 0, 0), chamfer=T["r_fork"], corner_segs=2, caps="both")
    cb["relief"] = "map:bolts"
    out.append((cb, "galv"))
    if col_len > 0.6:
        for tag, sy in (("n", -1), ("p", 1)):
            zb0 = dims["fork_bot"] - min(0.70, col_len - 0.05)
            # both ends buried: in the column (|y| <= 0.25) and in the crossbar (0.28 thick)
            br = box_along(f"fork_brace{tag}", [(0, sy * 0.10, zb0),
                                                (0, sy * T["upright_dy"], dims["fork_bot"] + 0.10)],
                           0.40, 0.18, side=(1, 0, 0), chamfer=T["r_brace"], corner_segs=2,
                           caps="none")
            out.append((br, "galv"))
    uw, ud = T["upright_wd"]
    c0, c1 = dims["carhead"]
    for tag, s in (("n", -1), ("p", 1)):
        up_ = box_along(f"fork_upright{tag}", [(0, s * T["upright_dy"], dims["fork_top"] - 0.02),
                                               (0, s * T["upright_dy"], dims["k_z"])],
                        uw, ud, side=(1, 0, 0), chamfer=T["r_upright"], corner_segs=2,
                        caps="none")
        up_["relief"] = "map:seams"
        out.append((up_, "galv"))
    # ladders on both column faces (front/back symmetric)
    if col_len >= 0.6:
        n = 0
        for sx in (-1, 1):
            x = sx * (cw / 2.0 + 0.03)
            for sy in (-1, 1):
                out.append((K.tube_along(f"ladder_rail{n}", [(x, sy * 0.18, dims["fin_top"] - 0.15),
                                                             (x, sy * 0.18, dims["fork_bot"] + 0.05)],
                                         0.02, 6, "none", 2), "galv"))
                n += 1
            z = dims["fin_top"] + 0.15
            while z < dims["fork_bot"] - 0.10:
                out.append((kit_cyl(f"ladder_rung{n}", 0.015, 0.36, (x, 0.0, z),
                                    cs2_kit._axis_rot("Y"), 4, "none"), "galv"))
                n += 1
                z += 0.30
    # carriage (R27 top = hang; R28 one track rope at dy 0): rounded head and beams, slimmer
    # ties / bolsters ending INSIDE the beams (no coplanar end faces)
    head = box_along("carhead_main", [(0, -fh, (c0 + c1) / 2), (0, fh, (c0 + c1) / 2)], 0.40,
                     c1 - c0, side=(1, 0, 0), chamfer=T["r_head"], corner_segs=2, caps="both")
    head["relief"] = "map:bolts"
    out.append((head, "galv"))
    # Revision 2 (gondola parity: its hanger head pivot): the hanger pin through the carriage
    # head's center, along the travel axis (the cabin's lateral swing), a boss + hex nut on each
    # travel face. Inside CARRIER_TRAM's carriage box (|y| <= 0.55, dz -0.05..0.50); the
    # uprights' box allows no protrusion, so no pin bosses sit on the uprights themselves
    Pv = T["pivot"]
    zc_head = (c0 + c1) / 2.0
    for tag, sx in (("n", -1), ("p", 1)):
        cap = "top" if sx > 0 else "bottom"                   # local +Z -> +X: outer cap only
        out.append((kit_cyl(f"pivot_boss{tag}", Pv["boss_r"], Pv["boss_l"],
                            (sx * (0.20 - 0.005 + Pv["boss_l"] / 2.0), 0.0, zc_head),
                            cs2_kit._axis_rot("X"), 8, cap), "galv"))
        out.append((kit_cyl(f"pivot_nut{tag}", Pv["nut_r"], Pv["nut_l"],
                            (sx * (0.20 - 0.005 + Pv["boss_l"] - 0.001 + Pv["nut_l"] / 2.0), 0.0,
                             zc_head), cs2_kit._axis_rot("X"), 6, cap), "galv"))
    b0, b1 = dims["carbeam"]
    hl = T["carbeam_len"] / 2.0
    for tag, s in (("n", -1), ("p", 1)):
        beam = box_along(f"carbeam_{tag}", [(-hl, s * T["carbeam_dy"], (b0 + b1) / 2),
                                            (hl, s * T["carbeam_dy"], (b0 + b1) / 2)],
                         0.10, b1 - b0, side=(0, 1, 0), chamfer=T["r_beam"], corner_segs=2,
                         caps="both")
        beam["relief"] = "map:bolts"
        out.append((beam, "galv"))
    tie_y = 2.0 * (T["carbeam_dy"] + 0.03)                # ends 2 cm inside the 0.10 beams
    for k, x in enumerate(T["tie_x"]):
        out.append((K.add_box(f"tie_{k}", (0.08, tie_y, c1 - c0), (x, 0.0, (c0 + c1) / 2)), "galv"))
    for k, x in enumerate(T["bogie_x"]):
        out.append((K.add_box(f"bolster_{k}", (0.10, tie_y, c1 - c0), (x, 0.0, (c0 + c1) / 2)),
                    "galv"))
    rope = dims["rope"]
    p1 = rope + max(q for _x, q in T["bogie_outline"])
    n = 0
    for k, x in enumerate(T["bogie_x"]):
        for s in (-1, 1):
            outline = [(x + px, rope + dz) for px, dz in T["bogie_outline"]]
            yc = s * T["plate_dy"]
            pl = extrude_profile(f"bogie_{n}", outline, yc - 0.0125, yc + 0.0125, axis="Y")
            pl["relief"] = "map:bolts"
            out.append((pl, "galv"))
            n += 1
        # bogie post: plates -> bolster, between the two middle wheels; its ends sit at the
        # plates' mid-planes and 2-3 cm inside plate top / bolster (no coplanar faces)
        out.append((K.add_box(f"bogie_post{k}", (0.08, 2 * T["plate_dy"], c0 + 0.02 - (p1 - 0.03)),
                              (x, 0.0, 0.5 * (c0 + 0.02 + p1 - 0.03))), "galv"))
    n = 0
    # --split: 16-segment rims (critique 2026-11-03: the 12-gon read as a polygon); the one-piece A
    # keeps its 12
    wsegs = T_SPLIT["wheel_segs"] if getattr(args, "split", False) else T["wheel_segs"]
    for x in T["bogie_x"]:
        for dx in T["wheel_dx"]:
            out.append((wheel_rim(f"sheave_w{n}_rim", T["wheel_profile"], wsegs,
                                  (x + dx, 0.0, dims["wheel_z"])), "galv"))
            out.append((kit_cyl(f"axle_{n}", T["wheel_hub_r"] + 0.005, 2 * (T["plate_dy"] + 0.015),
                                (x + dx, 0.0, dims["wheel_z"]), cs2_kit._axis_rot("Y"), 8,
                                "both"), "galv"))
            n += 1
    # haul grips at dy +-0.50, dz -0.40 (R28), both sides (R29 identity hosting); the links
    # run 3 cm up into the beams
    n = 0
    for x in T["clamp_x"]:
        for s in (-1, 1):
            loc = (x, s * T["haul_dy"], dims["haul_z"])
            for obj, _q in K.cable_clamp(f"haul{n}", LV_ROPE_R, loc):
                if obj.name.startswith("bolt_"):
                    cyl_uv(obj)                    # kit studs: metric UVs (no stretch)
                out.append((obj, "galv"))
            ztop = dims["haul_z"] + 0.003 + LV_ROPE_R + 0.015
            # Revision 1b C4: the outer face 2 mm inside the tie ends (|y| 0.53), not flush
            out.append((K.add_box(f"cliplink_{n}", (0.06, 0.058, b0 + 0.03 - ztop),
                                  (x, s * (T["haul_dy"] - 0.001), (ztop + b0 + 0.03) / 2)), "galv"))
            n += 1
    for obj, _k in out:
        apply_transform(obj)
    return out


# ======================================================================================
# Join / finish (shared)
# ======================================================================================

def set_kit_uv_names(parts):
    """Box parts get the one-island unwrap; then every part must carry exactly "UVMap"
    (MC3 covers the whole parts list)."""
    n = sum(1 for o, _k in parts if box_uv(o))
    print(f"[cabin] box_uv: {n} of {len(parts)} parts unwrapped as one island")
    return assert_uv_layers(parts, stage="before set_density / material_sources")


def join_parts(parts, name):
    """join -> remove_doubles -> store_part_table -> shade_by_angle(66). Returns the object."""
    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=REMOVE_DOUBLES_M)
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def material_specs():
    import cs2_bake
    return {k: cs2_bake.MaterialSpec(**v) for k, v in MATERIALS.items()}


def build_lod0(parts, name, density, table_out):
    """Tag, tiling sources, join, shade, orient. Returns (obj, sources, table)."""
    import cs2_bake
    keys = sorted({k for _o, k in parts})
    set_kit_uv_names(parts)
    cs2_uv.set_density(parts, density)
    table = cs2_asset.tag_parts(parts)
    specs = material_specs()
    sources = cs2_bake.material_sources({k: specs[k] for k in keys}, parts)
    obj = join_parts(parts, name)
    cs2_asset.store_part_table(obj, table)
    lo = min(v.co.z for v in obj.data.vertices)
    if abs(lo) > 1e-4:
        raise RuntimeError(f"{name}: lowest point at z {lo:.5f}, expected 0 (base pivot)")
    cs2_asset.shade_by_angle(obj, SHADE_ANGLE)
    cs2_asset.orient_for_net(obj)
    obj[FRAME_PROP] = "net"
    table_out.extend(table)
    return obj, sources, table


# ======================================================================================
# Contract math (all in the AUTHORING frame: x travel, y lateral outboard, z up)
# ======================================================================================

class Mesh:
    """Read-only arrays of one object in the authoring frame."""

    def __init__(self, obj):
        me = obj.data
        co = _co_array(me)
        M = np.array(obj.matrix_world, dtype=np.float64)
        co = co @ M[:3, :3].T + M[:3, 3]
        if obj.get(FRAME_PROP) == "net":
            co = np.stack([-co[:, 1], co[:, 0], co[:, 2]], axis=1)
        self.co = co
        self.obj = obj
        self.faces = [tuple(p.vertices) for p in me.polygons]
        self.loop_total = np.array([p.loop_total for p in me.polygons])
        attr = me.attributes.get("part")
        if attr is not None and len(me.polygons):
            part = np.zeros(len(me.polygons), dtype=np.int64)
            attr.data.foreach_get("value", part)
        else:
            part = np.zeros(len(me.polygons), dtype=np.int64)
        self.part = part
        try:
            self.table = json.loads(me[PART_TABLE_PROP])
        except (KeyError, TypeError, ValueError):
            self.table = []

    def faces_of(self, prefixes):
        if isinstance(prefixes, str):
            prefixes = (prefixes,)
        hits = {i + 1 for i, n in enumerate(self.table) if n.startswith(tuple(prefixes))}
        return [i for i, p in enumerate(self.part) if int(p) in hits]

    def verts_of(self, prefixes):
        idx = sorted({v for f in self.faces_of(prefixes) for v in self.faces[f]})
        return self.co[idx] if idx else np.zeros((0, 3))

    def present(self, prefixes):
        return bool(self.faces_of(prefixes))


class Checker:
    """Collects contract results; raise_if_failed() raises with every failure listed."""

    def __init__(self, label):
        self.label = label
        self.rows = []

    def check(self, code, ok, detail):
        self.rows.append({"code": code, "ok": bool(ok), "detail": detail})
        print(f"[contract] {self.label} {code}: {'OK' if ok else 'FAIL'} - {detail}")
        return ok

    def raise_if_failed(self):
        bad = [r for r in self.rows if not r["ok"]]
        if bad:
            raise RuntimeError(f"CONTRACT BROKEN ({self.label}): " +
                               "; ".join(f"{r['code']} {r['detail']}" for r in bad))
        return self.rows


def z_coverage_gaps(m, lo, hi):
    """Largest gap in the union of face z-intervals over [lo, hi]."""
    iv = sorted((float(m.co[list(f), 2].min()), float(m.co[list(f), 2].max())) for f in m.faces)
    cur = lo
    worst = 0.0
    for a, b in iv:
        if a > cur:
            worst = max(worst, a - cur)
        cur = max(cur, b)
        if cur >= hi:
            break
    if cur < hi:
        worst = max(worst, hi - cur)
    return worst


def mirror_symmetric(co, axis, tol=1e-4):
    """True when the vertex set maps onto itself under the mirror of `axis` (0 x, 1 y)."""
    kd = mathutils.kdtree.KDTree(len(co))
    for i, p in enumerate(co):
        kd.insert(p, i)
    kd.balance()
    worst = 0.0
    for p in co:
        q = p.copy()
        q[axis] = -q[axis]
        _c, _i, dist = kd.find(q)
        worst = max(worst, dist)
    return worst <= tol, worst


def _welded_ids(co, q=1e-5):
    """Vertex ids welded by position (q meters): FBX-style split vertices count as one."""
    _u, inv = np.unique(np.round(co / q).astype(np.int64), axis=0, return_inverse=True)
    return inv.reshape(-1)


def _clip_convex(subj, clipper):
    """Sutherland-Hodgman: 2-D convex polygon `subj` clipped by convex CCW `clipper`."""
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


def _poly_area(poly):
    return 0.5 * abs(sum(poly[i][0] * poly[(i + 1) % len(poly)][1]
                         - poly[(i + 1) % len(poly)][0] * poly[i][1] for i in range(len(poly))))


def coplanar_overlaps(obj, tol_d=0.0005, ang_deg=1.0, min_m2=1e-5):
    """Revision 1b (C1 / C2 / C4): every pair of coplanar triangles of `obj` (Blender loop
    triangles) that overlap by > min_m2 and share no edge (vertices welded at 1e-5 m): normals
    within ang_deg, planes within tol_d (the checker's 1 deg / 0.5 mm, revision1-check-cabins.md
    section A). 'same' pairs face the same way (z-fight); 'opposed' pairs are back-to-back
    buried contact. Returns {"same" | "opposed": [(part_a, part_b, area_m2), ...]} with part
    names (part_a <= part_b; equal = inside one part)."""
    me = obj.data
    me.calc_loop_triangles()
    nt = len(me.loop_triangles)
    tv = np.zeros(nt * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("vertices", tv)
    tv = tv.reshape(-1, 3)
    tp = np.zeros(nt, dtype=np.int64)
    me.loop_triangles.foreach_get("polygon_index", tp)
    co = _co_array(me)
    pid = np.zeros(len(me.polygons), dtype=np.int64)
    attr = me.attributes.get("part")
    if attr is not None and len(me.polygons):
        attr.data.foreach_get("value", pid)
    try:
        table = json.loads(me[PART_TABLE_PROP])
    except (KeyError, TypeError, ValueError):
        table = []
    tpart = pid[tp]
    vid = _welded_ids(co)[tv]
    P = co[tv]
    fn = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0])
    ln = np.linalg.norm(fn, axis=1)
    keep = np.where(ln > 2e-10)[0]
    n = fn[keep] / ln[keep, None]
    d = np.einsum("ij,ij->i", n, P[keep, 0])
    lo, hi = P[keep].min(axis=1), P[keep].max(axis=1)
    cosmax = math.cos(math.radians(ang_deg))
    out = {"same": [], "opposed": []}
    M, B = len(keep), 512
    for s0 in range(0, M, B):
        s1 = min(M, s0 + B)
        dot = n[s0:s1] @ n.T
        same = (dot > cosmax) & (np.abs(d[s0:s1, None] - d[None, :]) <= tol_d)
        opp = (dot < -cosmax) & (np.abs(d[s0:s1, None] + d[None, :]) <= tol_d)
        box = np.all((lo[s0:s1, None, :] <= hi[None, :, :] + 1e-4)
                     & (lo[None, :, :] <= hi[s0:s1, None, :] + 1e-4), axis=2)
        upper = np.arange(M)[None, :] > np.arange(s0, s1)[:, None]
        for kind, mask in (("same", same), ("opposed", opp)):
            ii, jj = np.nonzero(mask & box & upper)
            for a, b in zip((ii + s0).tolist(), jj.tolist()):
                ta, tb = int(keep[a]), int(keep[b])
                if len(set(vid[ta].tolist()) & set(vid[tb].tolist())) >= 2:
                    continue                          # shared edge: neighbors, no area overlap
                o, ex = P[ta, 0], P[ta, 1] - P[ta, 0]
                ex = ex / np.linalg.norm(ex)
                ey = np.cross(n[a], ex)
                t1 = [((p - o) @ ex, (p - o) @ ey) for p in P[ta]]
                t2 = [((p - o) @ ex, (p - o) @ ey) for p in P[tb]]
                if (t2[1][0] - t2[0][0]) * (t2[2][1] - t2[0][1]) \
                        - (t2[1][1] - t2[0][1]) * (t2[2][0] - t2[0][0]) < 0.0:
                    t2 = t2[::-1]
                ov = _clip_convex(t2, t1)
                area = _poly_area(ov) if len(ov) >= 3 else 0.0
                if area > min_m2:
                    pa, pb = (table[int(tpart[ta]) - 1] if 0 < tpart[ta] <= len(table) else "?",
                              table[int(tpart[tb]) - 1] if 0 < tpart[tb] <= len(table) else "?")
                    out[kind].append((min(pa, pb), max(pa, pb), area))
    return out


def coplanar_summary(found):
    """Counts / cm2 per kind, and the same-facing area per part pair (the contract's unit)."""
    by_pair = collections.defaultdict(float)
    for a, b, area in found["same"]:
        by_pair[(a, b)] += area
    return {k: {"pairs": len(v), "area_cm2": round(sum(x[2] for x in v) * 1e4, 2)}
            for k, v in found.items()}, {f"{a}|{b}": round(v * 1e4, 2) for (a, b), v in
                                         sorted(by_pair.items(), key=lambda kv: -kv[1])}


#: Revision 1b: the coplanar contract's bound - same-facing overlap per part pair (cm2)
COPLANAR_MAX_CM2 = 1.0


def check_coplanar(c, code, obj):
    """G12 / T12: no same-facing coplanar overlap > COPLANAR_MAX_CM2 between two parts (or
    inside one part: a cap's overlapping ears) - the z-fight tells of revision1-check-cabins.md
    C1 / C2 / C4. Back-to-back (opposed) buried contact is reported, not failed."""
    found = coplanar_overlaps(obj)
    tot, by_pair = coplanar_summary(found)
    worst = {k: v for k, v in by_pair.items() if v > COPLANAR_MAX_CM2}
    c.check(code, not worst, f"same-facing coplanar overlap {tot['same']['pairs']} tri pairs / "
                             f"{tot['same']['area_cm2']} cm2, part pairs > {COPLANAR_MAX_CM2} cm2: "
                             f"{dict(list(worst.items())[:4])}; opposed (buried) "
                             f"{tot['opposed']['pairs']} / {tot['opposed']['area_cm2']} cm2")
    return tot, by_pair


def shell_open_edges(obj, suffix="shell"):
    """Revision 1b (C3): (open, non-manifold) edge counts of the faces whose part name ends
    with `suffix` (the gondola shell + door plug island), vertices welded at 1e-5 m."""
    m = Mesh(obj)
    ids = {i + 1 for i, nm in enumerate(m.table) if nm.endswith(suffix)}
    vid = _welded_ids(m.co)
    count = collections.Counter()
    for f, poly in enumerate(m.faces):
        if int(m.part[f]) not in ids:
            continue
        w = [int(vid[v]) for v in poly]
        for a, b in zip(w, w[1:] + w[:1]):
            if a != b:
                count[(min(a, b), max(a, b))] += 1
    return (sum(1 for k in count.values() if k == 1), sum(1 for k in count.values() if k > 2),
            len(ids))


def gondola_contracts(obj, level, outboard=None, single_quads=True, role="whole", z_span=None):
    """G1-G10 (cabins.md 3.4 with the skeptic fixes) on one LOD; raises on any failure. `outboard`
    = the side the build put the stalk + doors on (None = GD_OUTBOARD, the shipped -Y).
    role (--split, 2026-11-03): "whole" = the one-piece cabin or the joined body + grip pair (every
    contract); "body" / "grip" = one split mesh, read in the ASSEMBLED frame (its base pivot shift
    undone): z_span = (assembled z of its origin, assembled top); G1 / G4 / G8 run over that span,
    G2 / G10 only on the grip, G3 / G9 / G11 only on the body."""
    outboard = outboard or GD_OUTBOARD
    m = Mesh(obj)
    c = Checker(f"{obj.name} (gondola LOD{level}" + ("" if role == "whole" else f", {role}") + ")")
    hang, rope = G["hang"], G["rope"]
    z_lo, z_hi = (0.0, hang) if z_span is None else z_span
    m.co = m.co + np.array([0.0, 0.0, z_lo])          # base pivot -> assembled frame
    z = m.co[:, 2]
    c.check("G1", abs(z.min() - z_lo) <= 1e-4 and abs(z.max() - z_hi) <= 1e-4,
            f"z {z.min():.5f} .. {z.max():.5f} (want {z_lo:.4f} .. {z_hi:.4f}, assembled)")
    sgn = 1.0 if outboard == "+Y" else -1.0
    g = m.verts_of("grip_")
    if role == "body":
        pass
    elif len(g):
        top = g[np.abs(g[:, 2] - g[:, 2].max()) < 1e-4]
        ty = sgn * top[:, 1]                       # outboard-positive lateral
        # --envelope: the 0.08 top starts outboard of the chamfer; the jaw still reaches y -0.04
        t_in = GD_ENVELOPE["u_top"] if ENVELOPE else -0.04
        ok = (top[:, 0].min() <= -0.149 and top[:, 0].max() >= 0.149 and ty.min() <= t_in + 0.001
              and ty.max() >= 0.299 and abs(g[:, 2].max() - hang) <= 1e-4
              and g[:, 2].min() < rope and (sgn * g[:, 1]).min() <= -0.039)
        c.check("G2", ok, f"grip top face x {top[:, 0].min():+.3f}..{top[:, 0].max():+.3f} "
                          f"y(outboard+) {ty.min():+.3f}..{ty.max():+.3f} at z {g[:, 2].max():.4f}, jaw "
                          f"from y {(sgn * g[:, 1]).min():+.3f}; rope point (0, 0, {rope:.3f}) under it")
    else:
        c.check("G2", False, "no grip_ faces")
    shell = m.verts_of(("body_", "glass_", "trim_"))
    if role == "grip":
        pass
    elif len(shell):
        cx = 0.5 * (shell[:, 0].min() + shell[:, 0].max())
        cy = 0.5 * (shell[:, 1].min() + shell[:, 1].max())
        c.check("G3", abs(cx) <= 0.01 and abs(cy) <= 0.01, f"shell bbox center ({cx:+.4f}, {cy:+.4f})")
    else:
        c.check("G3", False, "no shell faces")
    gap = z_coverage_gaps(m, z_lo, z_hi)
    c.check("G4", gap <= 0.05, f"largest z-coverage gap {gap:.3f} m over [{z_lo:.4f}, {z_hi:.4f}]")
    h = m.verts_of("hanger_")
    hi = h[h[:, 2] > 3.0] if len(h) else h
    ok5 = len(hi) > 0 and bool(np.all(sgn * hi[:, 1] > 0.0))
    detail = f"hanger z>3.0 y {hi[:, 1].min():+.3f}..{hi[:, 1].max():+.3f}" if len(hi) else "no hanger"
    if m.present(("doorbody_", "doortrim_", "doorglass_")):
        dv = m.verts_of(("doorbody_", "doortrim_", "doorglass_"))
        cy = float(dv[:, 1].mean())
        ok5 = ok5 and sgn * cy >= G["door_centroid_min"]
        detail += f"; door centroid y {cy:+.4f} (outboard >= {G['door_centroid_min']})"
    c.check("G5", ok5, detail)
    ex = G["extents"]
    if outboard == "+Y":
        ylo, yhi = ex["y_lo"], ex["y_hi"]
    else:
        ylo, yhi = -ex["y_hi"], -ex["y_lo"]
    c.check("G6", np.abs(m.co[:, 0]).max() <= ex["x"] and m.co[:, 1].min() >= ylo
            and m.co[:, 1].max() <= yhi,
            f"|x| {np.abs(m.co[:, 0]).max():.3f} <= {ex['x']}, y {m.co[:, 1].min():+.3f}.."
            f"{m.co[:, 1].max():+.3f} in [{ylo}, {yhi}]")
    c.check("G8", z.min() >= z_lo - 1e-4, f"min z {z.min():.5f}")
    if ENVELOPE:
        # G13 (B2 v2, GD_ENVELOPE): no face within the wheels' reach (down to rope - z_bot) passes
        # rope +- z inboard of u_in; the jaw inside the band is the only part the liners touch
        E = GD_ENVELOPE
        u = sgn * m.co[:, 1]
        bad = 0
        for f in m.faces:
            vs = list(f)
            dz = m.co[vs, 2] - rope
            if dz.max() >= -E["z_bot"] and np.abs(dz).max() > E["z"] + 1e-4 \
                    and u[vs].min() < E["u_in"] - 1e-4:
                bad += 1
        c.check("G13", bad == 0, f"envelope: {bad} faces reach past rope +-{E['z']} inboard of y(outboard+) "
                                 f"{E['u_in']} (wheel reach rope -{E['z_bot']})")
    if role == "grip":
        pass
    elif single_quads:
        for pre in ("logo_front", "logo_back", "plate_face"):
            fs = m.faces_of(pre)
            ok = len(fs) == 1 and int(m.loop_total[fs[0]]) == 4 if fs else False
            c.check("G9", ok, f"{pre}: {len(fs)} face(s)"
                              + (f", {int(m.loop_total[fs[0]])} corners" if fs else ""))
    else:
        for pre in ("logo_front", "logo_back"):
            if m.present(pre):
                c.check("G9", True, f"{pre} present (decimated level)")
    # G10: carriage within the traced Wilin mount footprint (skeptic MC6 numbers)
    car = m.verts_of(("carframe_", "roller_", "grip_"))
    if len(car):
        ext = car.max(axis=0) - car.min(axis=0)
        mx, my, _mz = G["wilin_mount_m"]
        c.check("G10", ext[0] <= mx and ext[1] <= my,
                f"carriage {ext[0]:.3f} x {ext[1]:.3f} within the Wilin mount {mx} x {my}")
    if level == 0:
        # Revision 1b: G11 the shell + plug island is closed (C3), G12 no z-fight overlap (C1 / C4)
        n_open, n_nm, n_parts = shell_open_edges(obj)
        if role != "grip":
            c.check("G11", n_parts >= 6 and n_open == 0 and n_nm == 0,
                    f"shell island ({n_parts} *shell parts): {n_open} open / {n_nm} non-manifold edges")
        check_coplanar(c, "G12", obj)
    return c.raise_if_failed()


def tram_contracts(obj, level, dims, part_role="whole"):
    """T1-T11 on one LOD (cabins.md 4.4 reconciled by R27-R30); raises on any failure.
    part_role: whole | cabin | carriage (--split). The split dims (tram_split_dims) carry cab_top (the
    cabin's top = the lug eyes over the pin) and car_z0 (the carriage's base pivot: its lowest
    vertex, z 0 in its own mesh, at car_z0 in the assembled frame)."""
    m = Mesh(obj)
    c = Checker(f"{obj.name} (tram LOD{level}, {part_role})")
    hang, rope = dims["hang"], dims["rope"]
    co = m.co
    z = co[:, 2]
    if part_role == "whole":
        c.check("T1", abs(z.min()) <= 1e-4 and abs(z.max() - hang) <= 1e-4,
                f"z {z.min():.5f} .. {z.max():.5f} (want 0 .. {hang:.3f})")
    elif part_role == "cabin":
        top = dims.get("cab_top", dims["k_z"])
        c.check("T9", abs(z.min()) <= 1e-4 and abs(z.max() - top) <= 1e-4,
                f"cabin z {z.min():.5f} .. {z.max():.5f} (want 0 .. {top:.4f}, the lug eyes over K "
                f"{dims['k_z']:.4f})")
    else:
        z0c = dims.get("car_z0", dims["k_z"])
        ztop = hang - z0c
        # base pivot (split 2026-11-03): LOD0's lowest vertex is z 0; the LODs lose the haul clamps
        # (their lowest point rises), nothing may go below 0
        okb = z.min() >= -1e-4 and (level > 0 or abs(z.min()) <= 1e-4)
        c.check("T9", abs(z.max() - ztop) <= 1e-4 and okb,
                f"carriage local z {z.min():.4f} .. {z.max():.5f} (base pivot at assembled {z0c:.4f}, "
                f"top {ztop:.4f}; pin K at {dims['k_z'] - z0c:+.4f}, rope seat at {rope - z0c:+.4f})")
    # back to the whole-carrier frame for the envelope checks
    zoff = dims.get("car_z0", dims["k_z"]) if part_role == "carriage" else 0.0
    W = co.copy()
    W[:, 2] += zoff
    if m.present("sheave_"):
        bad = []
        for i, nm in enumerate(m.table, start=1):
            if not (nm.startswith("sheave_") and nm.endswith("_rim")):
                continue
            idx = sorted({v for f in m.faces_of(nm) for v in m.faces[f] if m.part[f] == i})
            if not idx:
                bad.append((nm, "missing"))
                continue
            vv = W[idx]
            cz = 0.5 * (vv[:, 2].min() + vv[:, 2].max())
            cy = 0.5 * (vv[:, 1].min() + vv[:, 1].max())
            if abs(cz - dims["wheel_z"]) > 1e-3 or abs(cy) > 1e-3:
                bad.append((nm, round(cy, 4), round(cz, 4)))
        n_rims = sum(1 for nm in m.table if nm.startswith("sheave_") and nm.endswith("_rim"))
        c.check("T2", not bad and n_rims == len(T["bogie_x"]) * len(T["wheel_dx"]),
                f"{n_rims} wheel rims centered at y 0 (the single HV track rope, R28), z "
                f"{dims['wheel_z']:.4f} = rope {rope:.2f} + HV top {HV_ROPE_TOP} + groove radius; "
                f"bad {bad[:3]}")
    # T3: carriage clear zone (C10 / R30)
    zl, zh = dims["clear_lo"], rope - 0.05
    sel = (np.abs(W[:, 1]) < T["k1"][2]) & (W[:, 2] > zl) & (W[:, 2] < zh)
    c.check("T3", not sel.any(), f"{int(sel.sum())} vertices with |y| < 0.36 in z {zl:.2f}..{zh:.2f}")
    if part_role != "carriage":
        low = ("under_", "chamfer_", "band_", "trim_", "glass_", "sill_", "frame_", "roof_",
               "mullion_", "door", "logo_", "roofbox_", "grate_", "yoke_", "reveal_", "plaque_",
               "hatch", "bumper_", "intercom_")
        lowv = m.verts_of(low)
        top = float(lowv[:, 2].max()) if len(lowv) else 99.0
        c.check("T3b", top <= dims["fin_top"] + 1e-4,
                f"body/roof/yoke/fin top {top:.3f} <= fin top {dims['fin_top']:.2f} "
                f"(rope - {rope - dims['fin_top']:.2f})")
    # T4 plumb
    if part_role == "whole":
        topv = W[W[:, 2] > hang - 0.01]
        cxy = topv[:, :2].mean(axis=0) if len(topv) else np.array([9.0, 9.0])
        c.check("T4", np.abs(cxy).max() <= 0.01, f"top-face centroid ({cxy[0]:+.4f}, {cxy[1]:+.4f})")
    # T5 symmetry (R29)
    if level == 0:
        okx, wx = mirror_symmetric(W, 0)
        oky, wy = mirror_symmetric(W, 1)
        c.check("T5", okx and oky, f"mirror x worst {wx:.2e}, mirror y worst {wy:.2e} (R29)")
    else:
        lo, hi = W.min(axis=0), W.max(axis=0)
        c.check("T5", abs(lo[0] + hi[0]) <= 0.02 and abs(lo[1] + hi[1]) <= 0.02,
                f"bounds symmetric x {lo[0]:+.3f}/{hi[0]:+.3f}, y {lo[1]:+.3f}/{hi[1]:+.3f}")
    # T6 extents + R30 half-width above the fin
    ex = T["extents"]
    up = W[W[:, 2] > dims["fin_top"] + 1e-4]
    hw = float(np.abs(up[:, 1]).max()) if len(up) else 0.0
    c.check("T6", np.abs(W[:, 0]).max() <= ex["x"] and np.abs(W[:, 1]).max() <= ex["y"]
            and hw <= T["half_w_max"] + 1e-6,
            f"|x| {np.abs(W[:, 0]).max():.3f} <= {ex['x']}, |y| {np.abs(W[:, 1]).max():.3f} <= {ex['y']}, "
            f"carriage/hanger half-width {hw:.3f} <= {T['half_w_max']} (R30)")
    # T8 coverage
    lo_z, hi_z = (0.0, hang) if part_role == "whole" else (
        (0.0, dims.get("cab_top", dims["k_z"])) if part_role == "cabin" else (W[:, 2].min(), hang))
    gap = z_coverage_gaps(m, lo_z - zoff, hi_z - zoff)
    c.check("T8", gap <= 0.05, f"largest z-coverage gap {gap:.3f} m over [{lo_z:.2f}, {hi_z:.2f}]")
    # T10 haul grips (R28 / R29)
    if level == 0 and m.present("clamp_"):
        cl = []
        for i, nm in enumerate(m.table, start=1):
            if nm.startswith("clamp_haul") and nm.endswith("_u"):
                vi = sorted({v for f in m.faces_of(nm) for v in m.faces[f]}
                            | {v for f in m.faces_of(nm[:-1] + "l") for v in m.faces[f]})
                if not vi:
                    cl.append(np.array([9.0, 9.0, 9.0]))       # deleted grip: counts as bad
                    continue
                both = W[vi]
                cl.append(((both.min(0) + both.max(0)) / 2.0))
        good = [p for p in cl if abs(abs(p[1]) - T["haul_dy"]) <= 1e-3
                and abs(p[2] - dims["haul_z"]) <= 1e-3]
        c.check("T10", len(cl) == 4 and len(good) == 4,
                f"{len(good)}/{len(cl)} haul grips centered at |y| {T['haul_dy']} z {dims['haul_z']:.2f} "
                f"(haul rope +-5.0, 0.40 below the track rope, both sides)")
    # T11 R30 tower keep-outs
    for code, (dz0, dz1, dmin) in (("T11a", T["k1"]), ("T11b", T["k2"])):
        za = rope + dz0 if code == "T11a" else dims["k2_lo"]
        zb = rope + dz1
        sel = (W[:, 2] >= za) & (W[:, 2] <= zb) & (np.abs(W[:, 1]) < dmin)
        c.check(code, not sel.any(), f"{int(sel.sum())} vertices with |dy| < {dmin} in dz "
                                     f"{za - rope:+.2f}..{zb - rope:+.2f} (R30 tower envelope)")
    if level == 0:
        check_coplanar(c, "T12", obj)       # Revision 1b: no z-fight overlap (C2 / C4)
    if part_role != "carriage" and level in (0, 1):
        door_contract(c, m, W, level)        # Revision 2: centered plug double doors, closed
    return c.raise_if_failed()


#: Revision 2 exported bounds (prefab-local x lateral 1.880, z travel 4.030): the doors and
#: their hardware may not grow them (catalog bounds_lat_half_m 1.90 / travel 4.05 keep slack)
T_BOUNDS = {"lat": 1.880, "travel": 4.030}


def door_contract(c, m, W, level):
    """T13 (Revision 2): on each long side two door leaves meet at x 0 (centered on the cabin
    length, R29), each 0.75..0.90 wide and >= 2.0 m tall, their faces PROUD of the 1.85 wall
    by >= 10 mm (a real reveal, never flush: T12 catches a flush leaf too) and inside the
    1.880 lateral bound, the sill within 5 cm above the cabin interior floor = the station
    boarding deck (TRAM_STATION deck - pivot); LOD0 also needs the gap-line parts (meeting
    nosings, jamb seals) and the hardware (grab bar, release pull, 2 rollers) on every leaf;
    every vertex inside the Revision 1 exported bounds."""
    D = T["door"]
    wall = T["profile"][2][2]
    # interior floor above the pivot = station deck - parked pivot (6.30 - 6.00; the station
    # parks the default hang, so the compact --hang 7.00 cabin keeps the same floor)
    rope_default = CARRIERS["tram"]["hang_m"] - T["grip_above_rope"]
    floor = TRAM_STATION["deck_y"] - (TRAM_STATION["track_y"] - rope_default)
    bad = []
    widths, fronts = [], []
    for s in (-1, 1):
        for t in (-1, 1):
            tag = f"{'n' if s < 0 else 'p'}{'a' if t < 0 else 'b'}"
            v = m.verts_of((f"doorband_{tag}", f"doortrim_{tag}", f"doorframe_{tag}"))
            if not len(v):
                bad.append(f"leaf {tag} missing")
                continue
            xs = v[:, 0] * t
            if xs.min() < -1e-6:
                bad.append(f"leaf {tag} crosses the door center")
            w = float(xs.max() - xs.min())
            front = float((v[:, 1] * s).max())
            zb, zt = float(v[:, 2].min()), float(v[:, 2].max())
            widths.append(round(w, 3))
            fronts.append(round(front, 4))
            if not 0.75 <= w <= 0.90:
                bad.append(f"leaf {tag} width {w:.3f}")
            if xs.min() > 0.015:
                bad.append(f"leaf {tag} meeting edge {xs.min():.3f} off the center")
            if not (wall + 0.010 <= front <= T_BOUNDS["lat"]):
                bad.append(f"leaf {tag} face {front:.4f} not proud of the wall {wall} (>= 10 mm, "
                           f"<= {T_BOUNDS['lat']})")
            if not 0.0 <= zb - floor <= 0.05 or zt - zb < 2.0:
                bad.append(f"leaf {tag} z {zb:.3f}..{zt:.3f} (floor {floor:.2f})")
            if level == 0:
                for pre in ("doorseal_", "doorjamb_", "doorgrab_", "doorpull_", "doorgasket_",
                            "doorglass_"):
                    if not m.present(f"{pre}{tag}"):
                        bad.append(f"{pre}{tag} missing")
                if sum(1 for n in m.table if n.startswith(f"doorroller_{tag}")) != 2:
                    bad.append(f"leaf {tag}: not 2 rollers")
    lat = float(np.abs(W[:, 1]).max())
    trav = float(np.abs(W[:, 0]).max())
    if lat > T_BOUNDS["lat"] + 1e-5 or trav > T_BOUNDS["travel"] + 1e-5:
        bad.append(f"bounds lateral {lat:.4f} / travel {trav:.4f} > {T_BOUNDS}")
    c.check("T13", not bad, f"4 leaves, widths {sorted(set(widths))}, faces {sorted(set(fronts))} "
                            f"(wall {wall}, proud >= 10 mm), sill vs floor {floor:.2f}, bounds "
                            f"lat {lat:.3f} / travel {trav:.3f}; bad {bad[:4]}")


# ======================================================================================
# LODs (L5 pin_height, L7 protected-prefix decimation, C6 proxy + L6 UV snap)
# ======================================================================================

def pin_height(obj, lo, hi, snap=0.005):
    """Snap every vertex within `snap` of the lowest / highest point to exactly lo / hi
    (lo None: the top only - the split carriage has no bottom contract and its LODs lose
    the haul clamps, so a low pin would drag the plates down to LOD0's clamp level)."""
    me = obj.data
    co = _co_array(me)
    zmin, zmax = co[:, 2].min(), co[:, 2].max()
    a = (co[:, 2] <= zmin + snap) if lo is not None else np.zeros(len(co), dtype=bool)
    b = (co[:, 2] >= zmax - snap) & ~a
    if lo is not None:
        co[a, 2] = lo
    co[b, 2] = hi
    _set_co(me, co)
    return int(a.sum()), int(b.sum())


def _clear_custom_normals(me):
    for nm in ("custom_normal",):
        a = me.attributes.get(nm)
        if a is not None:
            me.attributes.remove(a)


def _delete_parts(obj, table, prefixes, keep=False):
    """Delete faces whose part starts with `prefixes` (keep=True: delete all OTHER faces)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    layer = bm.faces.layers.int.get("part")
    hit = lambda f: table[f[layer] - 1].startswith(tuple(prefixes)) if prefixes else False
    kill = [f for f in bm.faces if (not hit(f)) == keep]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return len(kill)


def _decimate(obj, ratio):
    if ratio >= 0.999:
        return
    _select_only(obj)
    mod = obj.modifiers.new("Decimate", type="DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges[:])
    slivers = [f for f in bm.faces if f.calc_area() < 1e-8]
    if slivers:
        bmesh.ops.delete(bm, geom=slivers, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()


def _dup(obj, name):
    d = obj.copy()
    d.data = obj.data.copy()
    d.name = name
    d.data.name = name
    _link(d)
    return d


def make_lod(obj, name, level, target, delete_prefixes, protect_prefixes=()):
    """
    L7: cs2_asset.make_lod_chain's recipe (delete tagged parts, COLLAPSE-decimate, dissolve
    degenerate + slivers, re-shade unweighted) with PROTECTED prefixes: those faces are
    split off before the decimation and joined back untouched, so the contract-bearing parts
    (grip top, carriage beams, hanger) keep their exact geometry. Returns the LOD object.
    """
    table = json.loads(obj.data[PART_TABLE_PROP])
    lod = _dup(obj, f"{name}_LOD{level}")
    removed = _delete_parts(lod, table, tuple(delete_prefixes))
    before = cs2_asset.tri_count(lod)
    prot = None
    if protect_prefixes:
        prot = _dup(lod, f"{name}_LOD{level}_prot")
        _delete_parts(prot, table, tuple(protect_prefixes), keep=True)
        _delete_parts(lod, table, tuple(protect_prefixes))
    n_prot = cs2_asset.tri_count(prot) if prot is not None else 0
    rest = cs2_asset.tri_count(lod)
    ratio = min(1.0, max(1, target - n_prot) / max(rest, 1))
    _decimate(lod, ratio)
    if prot is not None:
        if len(prot.data.polygons):
            bpy.ops.object.select_all(action="DESELECT")
            prot.select_set(True)
            lod.select_set(True)
            bpy.context.view_layer.objects.active = lod
            bpy.ops.object.join()
        else:
            pm = prot.data
            bpy.data.objects.remove(prot, do_unlink=True)
            bpy.data.meshes.remove(pm)
    lod.name = f"{name}_LOD{level}"
    lod.data.name = lod.name
    _clear_custom_normals(lod.data)
    cs2_asset.shade_by_angle(lod, SHADE_ANGLE, weighted=False)
    tris = cs2_asset.tri_count(lod)
    print(f"[lod] {lod.name}: deleted {removed} faces, {before} -> {tris} tris (target {target}, "
          f"protected {n_prot}, ratio {ratio:.3f})")
    return lod


def _mirror_net_x(obj):
    """Mirror a built LOD across the authoring y = 0 plane (net frame: Blender x = authoring y), winding
    flipped; custom normals cleared (they do not mirror) - the caller re-shades."""
    me = obj.data
    for v in me.vertices:
        v.co.x = -v.co.x
    me.flip_normals()
    _clear_custom_normals(me)
    me.update()


def mirrored_lod(fn, obj, name, level, *rest):
    """Nitpick A3: the -Y gondola's DECIMATED LOD as the exact mirror of the +Y one. COLLAPSE on the mirrored
    input picks other collapses (measured on LOD1: 1105 -> 1104 tris, vertices up to 0.17 m apart), so the LOD
    is decimated on a copy mirrored back to +Y (the +Y winding restored) and the result mirrored again, then
    re-shaded as make_lod shades. UVs are the -Y LOD0's (decimation carries them)."""
    back = _dup(obj, obj.name + "_mirrorback")
    _mirror_net_x(back)
    try:
        lod = fn(back, name, level, *rest)
    finally:
        _drop(back)
    _mirror_net_x(lod)
    cs2_asset.shade_by_angle(lod, SHADE_ANGLE, weighted=False)
    print(f"[lod] {lod.name}: decimated on the +Y mirror and mirrored back (nitpick A3: an exact mirror)")
    return lod


def tram_lod1(obj, name, dims, delete_prefixes, protect_prefixes, target):
    """
    Tram LOD1, AUTHORED (no collapse on the wheels): LOD0 minus the LOD1 details, every
    16-segment wheel rim swapped for an 8-segment rim at the same seat (cs2_kit.sheave, same
    radius / width / groove), its faces tagged with that rim's part index and its UVs snapped
    to that rim's LOD0 island. Measured reason: COLLAPSE on the eight identical 16-seg rims
    gave a different LOD1 on every in-process build (identical input hash; jitter did not
    help; LOD2, which deletes the rims, reproduces). Decimates the remaining non-wheel,
    non-protected faces only if the swap alone misses `target`.
    """
    table = json.loads(obj.data[PART_TABLE_PROP])
    lod = _dup(obj, f"{name}_LOD1")
    removed = _delete_parts(lod, table, tuple(delete_prefixes) + ("sheave_",))
    rims = [(i, nm) for i, nm in enumerate(table, start=1)
            if nm.startswith("sheave_") and nm.endswith("_rim")]
    rot = mathutils.Matrix.Rotation(math.radians(-90.0), 4, "Z")      # orient_for_net
    zoff = dims.get("car_z0", dims["k_z"]) if obj.get("cabin_role") == "carriage" else 0.0
    xs = [x + dx for x in T["bogie_x"] for dx in T["wheel_dx"]]
    new = []
    for (pi, nm), x in zip(rims, xs):
        parts = cs2_kit.sheave(nm[len("sheave_"):-len("_rim")] + "L1", T["wheel_r"], T["wheel_w"],
                               (x, 0.0, dims["wheel_z"] - zoff), "Y", segs=8, hub=False,
                               r_hub=T["wheel_hub_r"])
        w = parts[0][0]
        apply_transform(w)
        w.data.transform(rot)
        tab = [nm]
        attr = w.data.attributes.new("part", "INT", "FACE")
        attr.data.foreach_set("value", [1] * len(w.data.polygons))
        w.data[PART_TABLE_PROP] = json.dumps(tab)
        snap_proxy_uvs(w, obj, {nm: nm}, shrink=0.1)
        attr = w.data.attributes["part"]
        attr.data.foreach_set("value", [pi] * len(w.data.polygons))
        del w.data[PART_TABLE_PROP]
        new.append(w)
    bpy.ops.object.select_all(action="DESELECT")
    for w in new:
        w.select_set(True)
    lod.select_set(True)
    bpy.context.view_layer.objects.active = lod
    bpy.ops.object.join()
    lod.name = f"{name}_LOD1"
    lod.data.name = lod.name
    tris = cs2_asset.tri_count(lod)
    if tris > target * 1.04:
        print(f"[lod] {lod.name}: {tris} tris after the wheel swap > target {target}: decimating "
              f"the non-wheel faces")
        keep = tuple(protect_prefixes) + ("sheave_",)
        prot = _dup(lod, f"{name}_LOD1_prot")
        _delete_parts(prot, table, keep, keep=True)
        _delete_parts(lod, table, keep)
        rest = cs2_asset.tri_count(lod)
        _decimate(lod, min(1.0, max(1, target - cs2_asset.tri_count(prot)) / max(rest, 1)))
        bpy.ops.object.select_all(action="DESELECT")
        prot.select_set(True)
        lod.select_set(True)
        bpy.context.view_layer.objects.active = lod
        bpy.ops.object.join()
        lod.name = f"{name}_LOD1"
        lod.data.name = lod.name
    _clear_custom_normals(lod.data)
    cs2_asset.shade_by_angle(lod, SHADE_ANGLE, weighted=False)
    print(f"[lod] {lod.name}: deleted {removed} faces, {len(new)} wheels swapped to 8 segments, "
          f"{cs2_asset.tri_count(lod)} tris (target {target})")
    return lod


def snap_proxy_uvs(proxy, lod0, mapping, shrink=0.2):
    """
    L6: give an authored proxy the LOD0 "UVUnique" texels. For every proxy face, the
    nearest LOD0 face of the mapped part prefix is found (BVH, net frame) and every loop of
    the proxy face gets the UV of its projection INTO THAT ONE TRIANGLE (barycentrics
    clipped, shrunk `shrink` toward its centroid), so a proxy face never straddles islands.
    `mapping` = {proxy part prefix: LOD0 part prefix}.
    """
    from mathutils.bvhtree import BVHTree
    src = lod0.data
    table0 = json.loads(src[PART_TABLE_PROP])
    bm0 = bmesh.new()
    bm0.from_mesh(src)
    bmesh.ops.triangulate(bm0, faces=bm0.faces[:])
    bm0.faces.ensure_lookup_table()
    # triangle -> (vertex coords, uvs) per mapped prefix
    play = bm0.faces.layers.int.get("part")
    uvl = bm0.loops.layers.uv[cs2_uv.UNIQUE_LAYER]
    trees = {}
    for pre in sorted(set(mapping.values())):
        tris = [f for f in bm0.faces if table0[f[play] - 1].startswith(pre)]
        if not tris:
            raise RuntimeError(f"snap_proxy_uvs: LOD0 has no {pre} faces")
        verts = []
        polys = []
        uvs = []
        for f in tris:
            base = len(verts)
            for lp in f.loops:
                verts.append(lp.vert.co.copy())
                uvs.append(Vector(lp[uvl].uv))
            polys.append((base, base + 1, base + 2))
        trees[pre] = (BVHTree.FromPolygons(verts, polys), verts, polys, uvs)
    bm0.free()
    me = proxy.data
    table = json.loads(me[PART_TABLE_PROP])
    pattr = np.zeros(len(me.polygons), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", pattr)
    if cs2_uv.UNIQUE_LAYER not in me.uv_layers:
        me.uv_layers.new(name=cs2_uv.UNIQUE_LAYER)
    lay = me.uv_layers[cs2_uv.UNIQUE_LAYER].data
    for p, pi in zip(me.polygons, pattr):
        pname = table[pi - 1]
        pre0 = next(v for k, v in mapping.items() if pname.startswith(k))
        tree, verts, polys, uvs = trees[pre0]
        cen = Vector(p.center)
        _loc, _n, ti, _d = tree.find_nearest(cen)
        a, b, cc = (verts[i] for i in polys[ti])
        ua, ub, uc = (uvs[i] for i in polys[ti])
        for li in p.loop_indices:
            q = me.vertices[me.loops[li].vertex_index].co
            w = _barycentric(q, a, b, cc)
            w = np.clip(w, 0.0, None)
            w = w / max(w.sum(), 1e-12)
            w = (1.0 - shrink) * w + shrink / 3.0
            lay[li].uv = tuple(ua * w[0] + ub * w[1] + uc * w[2])
    for nm in [l.name for l in me.uv_layers if l.name != cs2_uv.UNIQUE_LAYER]:
        me.uv_layers.remove(me.uv_layers[nm])
    me.uv_layers.active = me.uv_layers[cs2_uv.UNIQUE_LAYER]


def _barycentric(p, a, b, c):
    v0, v1, v2 = b - a, c - a, p - a
    n = v0.cross(v1)
    if n.length < 1e-12:
        return np.array([1.0, 0.0, 0.0])
    # project p onto the triangle plane
    v2 = v2 - n.normalized() * v2.dot(n.normalized())
    d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
    d20, d21 = v2.dot(v0), v2.dot(v1)
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-18:
        return np.array([1.0, 0.0, 0.0])
    v = (d11 * d20 - d01 * d21) / den
    w = (d00 * d21 - d01 * d20) / den
    return np.array([1.0 - v - w, v, w])


def gondola_proxy(lod0, name, outboard):
    """C6: the authored LOD2 proxy (body loft 156 + hanger 24 + carriage 12 + jaw 12)."""
    rings = [rrect_ring(2.00, 1.80, 0.35, 4, 0.0), rrect_ring(2.30, 2.10, 0.35, 4, 0.82),
             rrect_ring(2.26, 2.06, 0.35, 4, 2.30), rrect_ring(2.10, 1.90, 0.35, 4, 2.40)]
    parts = [(loft_band("body_proxyskirt", rings[0], rings[1]), None),
             (ring_cap("body_proxyunder", rings[0], up=False), None),
             (loft_band("glass_proxyband", rings[1], rings[2]), None),
             (loft_band("roof_proxyedge", rings[2], rings[3]), None),
             (ring_cap("roof_proxycap", rings[3], up=True), None)]
    a = (0.0, G["hanger_a"][1], 2.52)                  # sits on the roof (no rails at LOD2)
    b = (0.0, G["hanger_b"][1], 2.52)
    w = d = 2.0 * G["hanger_r"]                         # the LOD0 tube's diameter
    top = (0.0, G["hanger_b"][1], 4.16)                 # up to the frame (the head is LOD0 only)
    parts.append((box_along("hanger_proxy", [a, b, top], w, d, side=(1, 0, 0),
                            chamfer=0.0, bend_rings=2, fillet=G["hanger_fillet"]), None))
    parts.append((cs2_kit.add_box("carframe_proxy", (1.10, 0.42, 0.12), (0.0, 0.50, 4.22)), None))
    parts.append((cs2_kit.add_box("grip_proxy", (0.30, 0.34, 0.20), (0.0, 0.13, 4.40)), None))
    for o, _ in parts:
        apply_transform(o)
        if outboard == "-Y":
            scale_axes(o, (1.0, -1.0, 1.0))
            o.data.flip_normals()
    table = cs2_asset.tag_parts(parts)
    obj = join_parts(parts, f"{name}_LOD2")
    cs2_asset.store_part_table(obj, table)
    cs2_asset.orient_for_net(obj)
    obj[FRAME_PROP] = "net"
    snap_proxy_uvs(obj, lod0, {"body_proxy": "body_", "glass_proxy": "glass_",
                               "roof_proxy": "roof_", "hanger_proxy": "hanger_",
                               "carframe_proxy": "carframe_", "grip_proxy": "grip_"})
    cs2_asset.shade_by_angle(obj, SHADE_ANGLE, weighted=False)
    return obj


# ======================================================================================
# Bake post-compose (relief, logos, number plate)
# ======================================================================================

_GLYPH = {}


def glyph_alpha(size):
    """The shipped launcher glyph (c) (peak + snowflake) as size x size coverage, row 0 = top,
    rendered at the stamp square's own pixel size (Revision 1: no 256 -> N resampling)."""
    size = int(size)
    if size not in _GLYPH:
        import ski_launcher_glyph as slg
        _GLYPH[size] = np.asarray(slg.render_alpha(slg.cand_c(), size, ss=GLYPH_SS),
                                  dtype=np.float32)
    return _GLYPH[size]


def _island_boxes(me, lay, uv, faces, start, total):
    """[(faces, loop idx, uv lo, meters per uv unit, size in meters)] of the UV islands of
    `faces` (each island scaled to its own true meters: uniform density after placement)."""
    isl = cs2_uv.uv_islands(me, lay, faces=faces)
    wa = cs2_uv.face_world_areas(me)
    ua = cs2_uv.face_uv_areas(me, uv, start, total)
    out = []
    for I in isl:
        idx = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in I])
        k = math.sqrt(float(wa[I].sum()) / max(float(ua[I].sum()), 1e-12))
        lo, hi = uv[idx].min(axis=0), uv[idx].max(axis=0)
        out.append((I, idx, lo, k, (hi - lo) * k))
    return out


def _pack_boxes(boxes, s_cap, W, H, m):
    """Highest density s <= s_cap at which the island boxes (+ gap m) skyline-pack into
    W x H px (90-degree turns allowed). Returns (s, positions) or (None, None)."""
    def fits(sc):
        return skyline_pack([(b[4][0] * sc + m, b[4][1] * sc + m) for b in boxes], W, H,
                            rotate=True)
    pos = fits(s_cap)
    if pos is not None:
        return s_cap, pos
    s_lo, s_hi, best = 0.0, s_cap, None
    for _ in range(40):
        mid = 0.5 * (s_lo + s_hi)
        p = fits(mid)
        if p is not None:
            s_lo, best = mid, p
        else:
            s_hi = mid
    return (s_lo, best) if best is not None else (None, None)


def _place_boxes(uv, boxes, pos, s, x0, y0, m, A):
    """Write the packed island UVs: island i at (x0 + x, y0 + y) + m/2, `s` px per meter,
    turned islands rotated +90 deg (never mirrored). Returns the number turned."""
    turned_n = 0
    for (_I, idx, lo, k, bb), (x, y, turned) in zip(boxes, pos):
        du = (uv[idx, 0] - lo[0]) * k * s
        dv = (uv[idx, 1] - lo[1]) * k * s
        if turned:                        # +90 deg: (du, dv) -> (h - dv, du), det +1
            du, dv = bb[1] * s - dv, du
            turned_n += 1
        uv[idx] = np.stack([(x0 + x + m / 2.0 + du) / A, (y0 + y + m / 2.0 + dv) / A], axis=1)
    return turned_n


def stamp_layout(obj, cfg, A, margin):
    """
    Revision 1 stamp stripe (PIPELINE section 3: stamped strips keep their UVs, copied 1:1).
    Writes the source layer "UVMap" of the joined LOD0 so cs2_bake.bake_unique(reserved=...)
    PINS the stamp and hero faces and packs only the rest:

      * stripe 1 (top): every logo quad of the asset -> ONE shared square of P = logo_frac x A
        px at the left (u along the viewer's right, v along up: identical, non-mirrored texels
        on every face; the glyph is painted once, at P px, >= 4x the shipped logo); the number
        plate (gondola) -> its own rect at the right, plate_ppm texels; the "fill" parts of the
        logo's material (gondola body / door skins, tram band / door panels) -> skyline-packed
        between them at the highest density that fits (hero faces);
      * stripes 2.. ("groups", tram): further single-material hero groups (the black frames,
        sills, reveals, mullions), each in its own full-width stripe just tall enough for the
        group at its target density;
      * every other face moves out of [0, 1] (never pinned by accident) and is pre-stretched in
        v by 1 / (free band height): pack_unique squeezes the unpinned islands into the band
        below the stripes with an affine v-remap, which would otherwise ship anisotropic texels.

    Only constant-albedo materials can be pinned (bake_unique restores a rect from its source
    1:1; pin_sources). Blender's AABB pack fills ~15-20 % of the atlas on these cabins (156-270
    islands at a 1/64 margin); the skyline here fills the pinned stripes far better.

    Returns None (no stamps) or {"reserved": [rect], "materials": [mat per rect], "info": {}}.
    """
    st = cfg.get("stamp")
    if not st:
        return None
    me = obj.data
    table = json.loads(me[PART_TABLE_PROP])
    lay = me.uv_layers["UVMap"]
    part = np.zeros(len(me.polygons), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", part)
    names = [table[p - 1] for p in part]
    mats = np.zeros(len(me.polygons), dtype=np.int64)
    me.polygons.foreach_get("material_index", mats)
    uv = cs2_uv.loop_uvs(lay)
    start, total = cs2_uv.face_loop_table(me)
    scale = A / 2048.0

    def faces_with(prefixes):
        return [i for i, nm in enumerate(names) if prefixes and nm.startswith(tuple(prefixes))]

    logo_f = faces_with(st["logo"])
    plate_f = faces_with(st.get("plate", ()))
    fill_f = [f for f in faces_with(st.get("fill", ())) if f not in set(logo_f)]
    if not logo_f:
        raise RuntimeError(f"{obj.name}: stamp layout found no logo faces {st['logo']}")
    m = float(margin)
    # inside one pinned single-material rect the islands are the same paint: a small gap is
    # enough (A // 256, 8 px at 2048); rects and the unpinned band keep the full margin
    mi = float(max(2, A // 256))
    P = int(round(st["logo_frac"] * A))
    H1 = P + 2.0 * m

    def frame_of(f):
        poly = me.polygons[f]
        if poly.loop_total != 4:
            raise RuntimeError(f"{obj.name}: stamp face {names[f]} is not a quad")
        pts = [Vector(me.vertices[me.loops[li].vertex_index].co) for li in poly.loop_indices]
        n = Vector(poly.normal).normalized()
        up = Vector((0.0, 0.0, 1.0))
        up = (up - n * up.dot(n)).normalized()
        right = (-n).cross(up)
        c = sum(pts, Vector()) / 4.0
        w = 2.0 * max(abs((p - c).dot(right)) for p in pts)
        h = 2.0 * max(abs((p - c).dot(up)) for p in pts)
        return poly, pts, c, right, up, w, h

    def place_quad(f, x0, y0, wpx, hpx):
        poly, pts, c, right, up, w, h = frame_of(f)
        for li, p in zip(poly.loop_indices, pts):
            s = (p - c).dot(right) / w + 0.5
            t = (p - c).dot(up) / h + 0.5
            uv[li] = ((x0 + s * wpx) / A, (y0 + t * hpx) / A)
        return w, h

    # -- stripe 1: logo square | fill | plate ----------------------------------------------
    lx0, ly0 = m, A - m - P
    logo_m = [place_quad(f, lx0, ly0, P, P)[0] for f in logo_f]
    info = {"A": int(A), "margin_px": m, "logo_px": P,
            "logo_ppm": {names[f]: round(P / w, 1) for f, w in zip(logo_f, logo_m)},
            "logo_vs_shipped": round(P / LOGO_SHIPPED_PX[cfg["kind"]], 2)}
    rect_faces = []
    x_right = float(A)
    if plate_f:
        if len(plate_f) != 1:
            raise RuntimeError(f"{obj.name}: plate stamp must be ONE quad, got {len(plate_f)}")
        _poly, _pts, _c, _r, _u, pw, ph = frame_of(plate_f[0])
        ppm = st["plate_ppm"] * scale
        wpx, hpx = pw * ppm, ph * ppm
        px0, py0 = A - m - wpx, A - m - hpx
        place_quad(plate_f[0], px0, py0, wpx, hpx)
        x_right = px0 - m / 2.0
        rect_faces.append(((x_right / A, (py0 - m / 2.0) / A, 1.0, 1.0), set(plate_f)))
        info["plate_ppm"] = round(ppm, 1)
        info["plate_px"] = [round(wpx, 1), round(hpx, 1)]
    fx0, fy0 = lx0 + P + m / 2.0, A - H1
    if fill_f:
        boxes = _island_boxes(me, lay, uv, fill_f, start, total)
        # region inset so the outer gap is the full margin (logo square, plate rect, band)
        gx0, gy0 = fx0 + (m - mi) / 2.0, fy0 + m / 2.0 - mi / 2.0
        RW, RH = x_right - m / 2.0 + mi / 2.0 - gx0, A - m / 2.0 + mi / 2.0 - gy0
        s, pos = _pack_boxes(boxes, st["fill_cap_ppm"] * scale, RW, RH, mi)
        if s is None:
            raise RuntimeError(f"{obj.name}: {len(boxes)} fill islands do not fit the stamp "
                               f"stripe ({RW:.0f} x {RH:.0f} px)")
        info["fill_turned"] = _place_boxes(uv, boxes, pos, s, gx0, gy0, mi, A)
        info["fill_ppm"] = round(s, 1)
        info["fill_islands"] = len(boxes)
        info["fill_region_px"] = [round(RW, 1), round(RH, 1)]
    rect_faces.insert(0, ((0.0, fy0 / A, x_right / A, 1.0), set(logo_f) | set(fill_f)))
    # -- stripes 2..: further single-material hero groups, full width ------------------------
    y_top = fy0
    info["groups"] = []
    for g in st.get("groups", ()):
        gf = faces_with(g["prefixes"])
        if not gf:
            continue
        boxes = _island_boxes(me, lay, uv, gf, start, total)
        s_g = g["ppm"] * scale
        W_in = A - m + mi                        # full margin at the stripe border

        def fits_h(hh):
            return skyline_pack([(b[4][0] * s_g + mi, b[4][1] * s_g + mi) for b in boxes],
                                W_in, hh, rotate=True)
        lo_h, hi_h = 0.0, y_top
        if fits_h(hi_h) is None:
            raise RuntimeError(f"{obj.name}: hero group {g['prefixes']} does not fit at "
                               f"{s_g:.0f} px/m")
        for _ in range(30):                       # the shortest stripe that holds the group
            mid = 0.5 * (lo_h + hi_h)
            if fits_h(mid) is None:
                lo_h = mid
            else:
                hi_h = mid
        h_in = math.ceil(hi_h)
        pos = fits_h(h_in)
        h_g = h_in + m - mi
        y0 = y_top - h_g
        turned = _place_boxes(uv, boxes, pos, s_g, (m - mi) / 2.0, y0 + (m - mi) / 2.0, mi, A)
        rect_faces.append(((0.0, y0 / A, 1.0, y_top / A), set(gf)))
        info["groups"].append({"prefixes": list(g["prefixes"]), "ppm": round(s_g, 1),
                               "stripe_px": h_g, "islands": len(boxes), "turned": turned})
        y_top = y0
    # one material per rect (bake_unique restores one source per rect)
    rects, per_rect, pinned = [], [], set()
    for rect, faces in rect_faces:
        ms = sorted({int(mats[f]) for f in faces})
        if len(ms) != 1:
            raise RuntimeError(f"{obj.name}: stamp rect {rect} would hold materials {ms}")
        rects.append(rect)
        per_rect.append(ms[0])
        pinned |= faces
    # everything else: out of [0, 1] and pre-stretched against the band squeeze
    lo_b, hi_b = cs2_uv.free_v_band(rects)
    stretch = 1.0 / (hi_b - lo_b)
    free = np.ones(len(uv), dtype=bool)
    for f in pinned:
        free[start[f]:start[f] + total[f]] = False
    uv[free, 0] += 2.0
    uv[free, 1] *= stretch
    cs2_uv.set_loop_uvs(lay, uv)
    info.update({"rects": [[round(v, 5) for v in r] for r in rects],
                 "band_v": [round(lo_b, 5), round(hi_b, 5)], "prestretch_v": round(stretch, 5),
                 "pinned_faces": len(pinned), "stripe1_px": round(H1, 1)})
    print(f"[cabin] stamp_layout {obj.name} @ {A}: logo {P} px ({info['logo_vs_shipped']}x the "
          f"shipped {LOGO_SHIPPED_PX[cfg['kind']]:.0f} px), fill {info.get('fill_ppm')} px/m, "
          f"plate {info.get('plate_ppm')} px/m, groups {info['groups']}, band v "
          f"{lo_b:.3f}..{hi_b:.3f}")
    return {"reserved": rects, "materials": sorted(set(per_rect)), "info": info}


def pin_sources(sources, materials):
    """bake_unique restores a reserved rect 1:1 only from a source that samples the packed
    source layer; the pinned stamp materials are constants, so they sample it (uv None) - the
    texels they bake are identical. A textured source cannot be pinned (raises)."""
    import dataclasses
    out = dict(sources)
    for i in materials:
        s = out[i]
        if isinstance(s.base, str):
            raise RuntimeError(f"material {i}: a textured source ({s.base}) cannot be pinned")
        out[i] = dataclasses.replace(s, uv=None)
    return out


def _srgb_to_lin(c):
    c = np.asarray(c, np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4).astype(np.float32)


def _quad_frame(obj, table, prefix):
    """UV -> world affine of a single-quad part and its viewer frame (right, up, center)."""
    me = obj.data
    faces = cs2_compose.faces_of_prefix(me, table, prefix)
    if len(faces) != 1 or me.polygons[faces[0]].loop_total != 4:
        raise RuntimeError(f"{prefix}: expected one quad, found {len(faces)} face(s)")
    poly = me.polygons[faces[0]]
    mw = obj.matrix_world
    uvl = me.uv_layers[cs2_uv.UNIQUE_LAYER].data
    Q = np.array([[uvl[li].uv[0], uvl[li].uv[1], 1.0] for li in poly.loop_indices], np.float64)
    Pw = np.array([list(mw @ me.vertices[me.loops[li].vertex_index].co)
                   for li in poly.loop_indices], np.float64)
    M, *_ = np.linalg.lstsq(Q, Pw, rcond=None)
    n = (mw.to_3x3() @ poly.normal).normalized()
    up = Vector((0.0, 0.0, 1.0))
    up = (up - n * up.dot(n)).normalized()
    right = (-n).cross(up)
    return M, np.array(list(right)), np.array(list(up)), Pw.mean(axis=0)


def make_glyph_painter(obj, table, prefix, size_w, size_h, rgb, A):
    """L9: stamp the glyph onto a single-quad part, bilinear, only where coverage > 0. The
    glyph alpha is rendered at the quad's own texel width (Revision 1), so sampling is ~1:1."""
    M, right, up, centre = _quad_frame(obj, table, prefix)
    me = obj.data
    f = cs2_compose.faces_of_prefix(me, table, prefix)[0]
    uvl = me.uv_layers[cs2_uv.UNIQUE_LAYER].data
    us = [uvl[li].uv[0] for li in me.polygons[f].loop_indices]
    alpha = glyph_alpha(max(16, int(round((max(us) - min(us)) * A))))
    G_ = alpha.shape[0]
    col = np.array(rgb, np.float32)

    def painter(rect, base_sl, ctl_sl):
        r0, r1, c0, c1 = rect
        rr, cc = np.mgrid[r0:r1, c0:c1]
        uv1 = np.stack([(cc + 0.5) / A, (rr + 0.5) / A, np.ones(rr.shape)], axis=-1)
        d = uv1 @ M - centre
        s = d @ right
        t = d @ up
        gx = (s / size_w + 0.5) * G_ - 0.5
        gy = (0.5 - t / size_h) * G_ - 0.5
        inside = (np.abs(s) <= size_w / 2.0) & (np.abs(t) <= size_h / 2.0)
        x0 = np.clip(np.floor(gx).astype(int), 0, G_ - 1)
        y0 = np.clip(np.floor(gy).astype(int), 0, G_ - 1)
        x1 = np.clip(x0 + 1, 0, G_ - 1)
        y1 = np.clip(y0 + 1, 0, G_ - 1)
        fx = np.clip(gx - np.floor(gx), 0.0, 1.0)
        fy = np.clip(gy - np.floor(gy), 0.0, 1.0)
        cov = (alpha[y0, x0] * (1 - fx) * (1 - fy) + alpha[y0, x1] * fx * (1 - fy)
               + alpha[y1, x0] * (1 - fx) * fy + alpha[y1, x1] * fx * fy)
        cov = np.where(inside, cov, 0.0).astype(np.float32)
        m = cov > 1e-3
        if m.any():
            base_sl[..., 0:3][m] = (base_sl[..., 0:3][m] * (1.0 - cov[m, None])
                                    + col[None, :] * cov[m, None])

    return painter


def make_plate_painter(obj, table, number, A, plate_w, plate_h):
    """The lift_chair number plate (5x7 digits) in LINEAR values (post_compose is linear)."""
    M, right, up, centre = _quad_frame(obj, table, "plate_face")
    rows = []
    glyphs = [DIGITS.get(ch, DIGITS["0"]) for ch in str(number)]
    for r in range(7):
        rows.append("0".join(g[r] for g in glyphs))
    bits = np.array([[ch == "1" for ch in row] for row in rows])
    nr, nc = bits.shape
    cell = PLATE_CELL_M
    tw, th = nc * cell, nr * cell
    light = _srgb_to_lin(PLATE_LIGHT_SRGB)
    dark = _srgb_to_lin(PLATE_DARK_SRGB)

    def painter(rect, base_sl, ctl_sl):
        r0, r1, c0, c1 = rect
        rr, cc = np.mgrid[r0:r1, c0:c1]
        uv1 = np.stack([(cc + 0.5) / A, (rr + 0.5) / A, np.ones(rr.shape)], axis=-1)
        d = uv1 @ M - centre
        s = d @ right
        t = d @ up
        img = np.empty(s.shape + (3,), np.float32)
        img[...] = light
        border = (np.abs(s) > plate_w / 2.0 - 0.008) | (np.abs(t) > plate_h / 2.0 - 0.008)
        ci = np.floor((s + tw / 2.0) / cell).astype(int)
        ri = np.floor((th / 2.0 - t) / cell).astype(int)
        inside = (ci >= 0) & (ci < nc) & (ri >= 0) & (ri < nr)
        glyph = np.zeros(s.shape, bool)
        glyph[inside] = bits[ri[inside], ci[inside]]
        img[border | glyph] = dark
        base_sl[..., 0:3] = img

    return painter


def make_post_compose(obj, cfg, quality, stamps, plate_number):
    """cabins.md 3.6: density report -> relief -> compose_maps -> renormalise -> stamps."""
    report = {}

    def hook(base, nrm, msk, ctl, ao, mesh, A):
        table = json.loads(mesh[PART_TABLE_PROP])
        try:
            rows = cs2_uv.density_report(obj, A)
            vals = sorted(r[2] for r in rows)
            report["density_px_per_m"] = {"median": round(float(np.median(vals)), 1),
                                          "min": round(float(vals[0]), 1),
                                          "max": round(float(vals[-1]), 1)} if vals else None
            # Revision 1: the hero faces' shipped density (lowest part per prefix), at A
            hero = {}
            for p, _src, shp in rows:
                nm = table[p - 1] if 1 <= p <= len(table) else ""
                for pre in cfg.get("hero", ()):
                    if nm.startswith(pre):
                        hero[pre] = min(hero.get(pre, float("inf")), shp)
            report["density_hero"] = {k: round(float(v), 1) for k, v in sorted(hero.items())}
        except Exception as e:  # noqa: BLE001 - a report must never block the bake
            report["density_px_per_m"] = f"unavailable: {e!r}"
        h = None
        ppm = 1.0
        if quality == "finish":
            h = cs2_compose.detail_height(mesh, A, cfg["detail"]())
            _pm, ppm = cs2_compose.rasterise_face_fields(mesh, A)
        base, nrm, msk, ctl = cs2_compose.compose_maps(base, nrm, msk, ctl, None, h, ppm,
                                                       {"seed": 0})
        n = nrm[..., 0:3] * 2.0 - 1.0
        flat = np.linalg.norm(n, axis=-1) < 0.1
        n[..., 2] = np.maximum(n[..., 2], 0.05)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        nrm[..., 0:3] = n * 0.5 + 0.5
        nrm[flat, 0:3] = (0.5, 0.5, 1.0)
        nrm[..., 3] = 1.0
        for prefix, (w, hgt) in stamps.items():
            cs2_compose.stamp_on_face(base, ctl, mesh, table, prefix,
                                      make_glyph_painter(obj, table, prefix, w, hgt, GLYPH_RGB, A))
        if plate_number is not None and cs2_compose.faces_of_prefix(mesh, table, "plate_face"):
            pw, ph = G["plate_wh"]
            cs2_compose.stamp_on_face(base, ctl, mesh, table, "plate_face",
                                      make_plate_painter(obj, table, plate_number, A, pw, ph))
        report["stamps"] = sorted(stamps) + (["plate_face"] if plate_number is not None else [])
        # Revision 1: a gutter texel the bake margin averaged from two recolor slots (e.g.
        # roof G 0.17 + trim B 0.83) is meaningless and encodes to R+G+B 1.004 in 8 bits; keep
        # its dominant slot only (sum <= 1 by construction, no mixed tints in the gutters)
        c = ctl[..., 0:3]
        multi = (c > 1e-6).sum(axis=-1) > 1
        if multi.any():
            keep = c[multi].argmax(axis=-1)
            vals = c[multi].max(axis=-1)
            one = np.zeros((int(multi.sum()), 3), np.float32)
            one[np.arange(len(keep)), keep] = vals
            c[multi] = one
        report["ctl_gutter_blends_hardened"] = int(multi.sum())
        return base, nrm, msk, ctl

    return hook, report


def gondola_detail():
    """Sub-5 cm detail lives here, not in geometry (Revision 1): weld seams, bolt heads, the
    clamp U-bolt nuts and the spring coils are normal-map relief."""
    D = cs2_compose.DetailSpec
    return {"yoke_": D(bolt_pitch_m=0.15, bolt_r_m=0.010, bolt_h_m=0.004),
            "hanger_": D(seams_m=0.60, seam_axes="rings"),
            # at the Revision 1 densities (140-200 px/m) a 7 cm bolt lattice reads as rivet
            # spam: sparser heads on the housings, none on the cast grip
            "hanger_head": D(bolt_pitch_m=0.12, bolt_r_m=0.009, bolt_h_m=0.004),
            # --split joint (2026-11-03): no relief on the knuckle, cheek plates and pin ends (a
            # seam ring or a bolt lattice on 6-14 cm parts reads as noise); the neck keeps the
            # hanger seams
            "hanger_knuckle": D(), "hanger_cheek_": D(), "hanger_pin_": D(),
            "hangerclamp_": D(bolt_pitch_m=0.10, bolt_r_m=0.010, bolt_h_m=0.005),
            "spring_": D(seams_m=0.025, seam_axes="rings", seam_width_m=0.008,
                         seam_depth_m=0.004),
            "carframe_": D(bolt_pitch_m=0.14, bolt_r_m=0.009, bolt_h_m=0.004)}


def tram_detail():
    """Revision 2 ("gondola has some details that the tram doesn't (door handles,
    bolts etc)"): measured on the Revision 1 bake, EVERY tram bolt row was below the 2-texel
    floor and skipped (509 faces) - the galv band ships at ~44-54 px/m and a 0.010 m head is
    1.1 texels across. The tram's structural bolts are now tram-scale heads (r 0.018-0.022,
    M30-M36 class on a 100-pax carriage) and the bolt-bearing parts carry heavier density
    weights (T_DENSITY), so the rows draw at 2-3 texels per head like the gondola's (r x px/m
    >= 1). New: header cover screws, intercom grille."""
    D = cs2_compose.DetailSpec
    return {"band_": D(seams_m=1.20, seam_axes="ribs"),
            "frame_": D(seams_m=1.20, seam_axes="rings"),
            "roof_": D(seams_m=1.20, seam_axes="rings"),
            "roofbox_": D(seams_m=0.50, seam_axes="both", bolt_pitch_m=0.10, bolt_r_m=0.020,
                          bolt_h_m=0.004),
            "grate_": D(grate_m=0.05),
            # a full bolt lattice over the 4.8 m fin read as rivet spam (Revision 2 close-ups):
            # bolt rows on the yoke beam only, the fin a smooth fairing with one weld ring
            # (JH-13's hanger foot), the bogie plates two rows along a hidden joint line
            "yoke_": D(bolt_pitch_m=0.15, bolt_r_m=0.020, bolt_h_m=0.006),
            "yoke_fin": D(seams_m=1.20, seam_axes="rings"),
            "hanger_": D(seams_m=1.50, seam_axes="rings", bolt_pitch_m=0.15, bolt_r_m=0.022,
                         bolt_h_m=0.006),
            "fork_": D(seams_m=1.50, seam_axes="rings", bolt_pitch_m=0.15, bolt_r_m=0.022,
                       bolt_h_m=0.006),
            "carbeam_": D(bolt_pitch_m=0.15, bolt_r_m=0.020, bolt_h_m=0.006),
            "carhead_": D(bolt_pitch_m=0.15, bolt_r_m=0.020, bolt_h_m=0.006),
            # --split joint (2026-11-03): plain lug and cheek plates, the pin heads are the detail
            "fork_lug": D(), "carhead_cheek": D(),
            "bogie_": D(seams_m=0.30, seam_axes="rings", seam_depth_m=0.0, bolt_pitch_m=0.10,
                        bolt_offset_m=0.07, bolt_r_m=0.020, bolt_h_m=0.006),
            "doorheader_": D(bolt_pitch_m=0.10, bolt_r_m=0.008, bolt_h_m=0.002),
            "intercom_": D(seams_m=0.03, seam_axes="rings", seam_width_m=0.008,
                           seam_depth_m=0.002)}


# ======================================================================================
# Reports: geometry hash, clearance rows, prefab numbers, code consequences, catalog drift
# ======================================================================================

def geometry_hash(obj):
    me = obj.data
    h = hashlib.sha1()
    for v in me.vertices:
        h.update(("%.5f %.5f %.5f;" % tuple(v.co)).encode())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    if cs2_uv.UNIQUE_LAYER in me.uv_layers:
        for lp in me.uv_layers[cs2_uv.UNIQUE_LAYER].data:
            h.update(("%.6f %.6f;" % tuple(lp.uv)).encode())
    return h.hexdigest()[:16]


def bounds_authoring(obj):
    m = Mesh(obj)
    lo, hi = m.co.min(axis=0), m.co.max(axis=0)
    return [round(float(v), 4) for v in lo], [round(float(v), 4) for v in hi]


def clearance_rows(kind, dims=None):
    """MC4: mid-span cabin underside and the carving bound WITH lane sag (h_lane x span)."""
    rows = []
    if kind == "gondola":
        axle, hang = CARRIERS["gd8"]["axle_m"], G["hang"]
        for span in (60.0, 120.0, 250.0):
            for h in (0.03, 0.004):
                sag = span * h
                rows.append({"span_m": span, "h_lane": h, "lane_sag_m": round(sag, 3),
                             "underside_above_chord_mid_m": round(axle - hang - sag, 3),
                             "heightrange_min_max": round(7.0 - sag, 3),
                             "static_guard_trips": sag > 1.5})
        note = ("underside(mid) = chord + axle - hang - span x h_lane (ChairPin puts the grip top "
                "ON the lane); carving: cut ceiling edgeY + min - 2 <= underside - 0.5 -> "
                "min <= 7.0 - span x h_lane; 250 m spans need h_lane <= ~0.004 (shipped "
                "SkiLiftHaulRopeB m_Hanging 0.03, SkiLiftHaulRopeB.Prefab:24; R31)")
    else:
        rope_below = dims["hang"] - T["grip_above_rope"]
        for ly in (30.0, 45.0, 60.0):
            for span in (250.0, 500.0, 1000.0):
                for h in (0.03, 0.004):
                    sag = span * h
                    rows.append({"Ly_m": ly, "span_m": span, "h_lane": h, "lane_sag_m": round(sag, 3),
                                 "underside_above_chord_mid_m": round(ly - rope_below - sag, 3),
                                 "heightrange_min_max": round(ly - rope_below + 1.5 - sag, 3)})
        note = (f"underside(mid) = chord + Ly - {rope_below:.2f} - span x h_lane (shuttle places by "
                f"the rope, R27); carving min <= Ly - {rope_below - 1.5:.2f} - span x h_lane; "
                f"R31 lane prefab m_Hanging ~0.004")
    return {"rows": rows, "note": note}


def prefab_numbers(kind, name, bounds, dims=None, outboard="+Y"):
    """cabins.md section 8 (surgery numbers) reconciled by R27-R31."""
    lo, hi = bounds
    pre_bounds = {"x": [lo[1], hi[1]], "y": [lo[2], hi[2]], "z": [lo[0], hi[0]]}
    if kind == "gondola":
        return {
            "object_prefab": "SkiGondolaCabin8", "mesh": name,
            "UtilityObject": {"m_UtilityType": 8, "m_UtilityPosition": [0.0, G["hang"], 0.0],
                              "why": "grip top = bounds max (chair precedent (0, 2.4, 0))"},
            "PillarObject": "none (PillarData preempts the UtilityObject exemption)",
            "expected_mesh_bounds_prefab_local": pre_bounds,
            "grip_top_m": G["hang"], "rope_m": G["rope"], "grip_top_above_rope_m": 0.08,
            "lane_offset_m": 3.0, "axle_m": CARRIERS["gd8"]["axle_m"],
            "NetSubObjects": {"m_Position": "(+-3.0, 10.11, 0)", "m_Placement": 2,
                              "m_AnchorTop": True, "minus_x_row_rotation": "(0, 1, 0, 0)",
                              "m_Spacing": "code catalog spawn floor (<= 40; code thins to 60)",
                              "why": "axle 10.0 + 0.11, the shipped 7.15 = 7.04 + 0.11 with "
                                     "AnchorTop (SkiChairliftHV.Prefab:102 y 7.15, :117 spacing, "
                                     ":118 AnchorTop)"},
            "station_rope_above_deck_m": 4.55,
            "ColorProperties": {"on": "LOD0 RenderPrefab only", "m_VariationRanges": [0, 0, 0],
                                "sets": [{"name": s[0], "slot1": s[1], "slot2": s[2], "slot3": s[3]}
                                         for s in G_SETS]},
            # prefab frame (review F7): authoring +Y = prefab +X on the +x row
            "outboard": {"+Y": "+X", "-Y": "-X"}[outboard], "outboard_authoring": outboard,
            "seats": 8, "lot": "n/a (net sub-object)",
            "stubs": "n/a (station lens)",
        }
    return {
        "object_prefab": "SkiTramCabin", "mesh": name,
        "UtilityObject": {"m_UtilityType": 8, "m_UtilityPosition": [0.0, dims["hang"], 0.0],
                          "why": "R27 grip-top idiom: carriage top = bounds max"},
        "PillarObject": "none; hosted cabins carry UtilityObject only",
        "expected_mesh_bounds_prefab_local": pre_bounds,
        "carriage_top_m": dims["hang"], "track_rope_m": dims["rope"],
        "haul_rope_m": dims["haul_z"], "lane_offset_m": 5.5, "haul_offset_m": 5.0,
        "ropes": "R28: ONE HV track rope per side at +-5.5 (the rider lane, the carriage rides "
                 "it at dy 0); LV haul at +-5.0, 0.40 below; no LV track lanes",
        "hosted_poses": {
            "host": "base station ObjectSubObjects",
            "cabin_A": "(-5.50, Yp, z_bay) identity (0,0,0,1)",
            "cabin_B": "(+5.50, Yp, z_bay) identity (0,0,0,1)",
            "Yp": "pivot = floor underside = platform deck top - 0.30 (interior floor 0.30 above "
                  "the underside)",
            "derived": f"TRACK_Y = Yp + {dims['rope']:.2f}; HAUL_Y = Yp + {dims['haul_z']:.2f}; "
                       f"carriage top = Yp + {dims['hang']:.2f}",
            # integration 2026-10-11: the tram stations own the anchor heights (they keep TRACK_Y
            # 16.00 and drop the deck to 6.30); these rows used the contract's abandoned deck 9.00
            "station": {
                "source": "tram_station.py DECK_Y / DOCK_Z / TRACK_Y (anchor owner)",
                "deck_y": TRAM_STATION["deck_y"],
                "Yp": round(TRAM_STATION["deck_y"] - 0.30, 3),
                "z_bay": TRAM_STATION["dock_z"],
                "track_y": round(TRAM_STATION["deck_y"] - 0.30 + dims["rope"], 3),
                "haul_y": round(TRAM_STATION["deck_y"] - 0.30 + dims["haul_z"], 3),
                "carriage_top_y": round(TRAM_STATION["deck_y"] - 0.30 + dims["hang"], 3),
                "matches_station_track_y": abs(TRAM_STATION["deck_y"] - 0.30 + dims["rope"]
                                               - TRAM_STATION["track_y"]) < 1e-6,
                "clearance": "base face girder opening 18.60 and top portal heads 17.10 clear "
                             "the carriage top 16.50 (station contract C4 + the real-cabin sweep)",
            },
        },
        "station_deck_edge_m": (f"lane +- {TRAM_STATION['deck_edge_dx']:.2f} as built (bay deck "
                                "edges x +-3.20 / +-7.80; 0.42 m to the 1.88 body); the cabin "
                                "lens' tighter ask was lane +- 1.95"),
        "tower_envelope": "R30: tower <= lane + 0.36 at dz -0.80..+0.60 and <= lane + 0.80 at dz "
                          "-3.80..-0.70; carrier half-width <= 1.10 above the fin; carrier "
                          "symmetric (the inboard fork upright clips the saddle cantilever arm "
                          "for ~0.7 s per tower pass - the accepted centered-hanger class, "
                          "lattice-towers D4)",
        "ColorProperties": {"on": "LOD0 RenderPrefab only", "m_VariationRanges": [0, 0, 0],
                            "sets": [{"name": s[0], "slot1": s[1], "slot2": s[2], "slot3": s[3]}
                                     for s in T_SETS[:1]],
                            "documented": [s[0] for s in T_SETS[1:]]},
        "seats": "100 (+1 operator)", "lot": "n/a (hosted)", "stubs": "n/a (station lens)",
    }


CODE_CONSEQUENCES = {
    "gondola": [
        "ChairPrefabNames += \"SkiGondolaCabin8\" (ChairPinSystem.cs:174 family)",
        "ChairPin moving mode resolves only \"SkiChairliftHV\" (ChairPinSystem.cs:303, :1640): add "
        "the gondola net or it gets no moving cabins (skeptic M3)",
        "seats / speed per type (ChairPinSystem.cs:157, :165, :174)",
        "F7 approach ceiling per type = gripTop + 1.5 (ChairPinSystem.cs:256, :279); the absolute "
        "4.0 < 4.5 trips LaneTooFar on approach edges; mid-span lane sag > 1.5 m trips the static "
        "guard too",
        "rope = hang_m - grip_top_above_rope_m (4.42); ChairPin puts the grip top ON the lane "
        "(:1040) so cabins hang 0.08 low until it reads the field (invisible)",
        "carrier count per type from code (finding 9: EvenSpacing spawns nothing at 72 m on 60 m "
        "edges); prefab m_Spacing is only a spawn floor",
        "R31: own rope-lane prefab with m_Hanging ~0.004 for 250 m spans; carving envelope "
        "includes span x h_lane (clearance_rows)",
    ],
    "tram": [
        "R27: the shuttle places by the ROPE (bounds 10.50 - 0.50 = 10.00 above the pivot), never "
        "by bounds.max.y",
        "R29: shuttle never yaws 180 at a reversal (cabins symmetric); hosted identity in both bays",
        "R28: rider lane = the HV track rope at +-5.5 (harvest by HighVoltageLine, "
        "ChairPinSystem.cs:746, :1203); haul is LV at +-5.0 / -0.40",
        "carriage pitch: one mesh stays level on pitched ropes (ends +-2.80 tan(theta)); --split "
        "gives SkiTramCarriageA pitched about K = (0, 0, hang - 0.12)",
        "R31: tram rope-lane prefab m_Hanging ~0.004 (1 km x 0.03 = 30 m sag); carving envelope "
        "includes span x h_lane",
        "Phase C shuttle: terminals-only serve, dwell 60 s, speed 10 m/s (RS section 5)",
    ],
}


def load_catalog(path):
    """Read catalog/carriers.json (human-written); returns (rows dict or None, error)."""
    if not path or not os.path.isfile(path):
        return None, f"{path} not found"
    try:
        with open(path, "r", encoding="utf-8-sig") as fh:
            return json.load(fh).get("carriers", {}), None
    except (OSError, ValueError) as e:
        return None, repr(e)


def catalog_drift(kind, path, hang=None, outboard=None):
    rows, err = load_catalog(path)
    key = CATALOG_KEY[kind]
    mine = dict(CARRIERS[key])
    if hang is not None:
        mine["hang_m"] = float(hang)
    if outboard is not None and "outboard" in mine:
        mine["outboard"] = OUTBOARD_PREFAB_X[outboard]      # nitpick A3: the side this build put it
    out = {"catalog": os.path.relpath(path, REPO).replace("\\", "/") if path else None,
           "row": key, "generator_row": mine, "drift": [],
           "compared": [f for f in CONTRACT_FIELDS if f in mine]}
    if rows is None:
        out["error"] = err
        print(f"[catalog] WARN {err}: building from the generator's ruled row {key}")
        return out
    cat = rows.get(key, {})
    if outboard is not None and "outboard" in cat:
        # nitpick A3: the catalog's authoring side (the stations' cabin pose reads it) vs this build's;
        # reported, never fatal here (the catalog is human-owned; test_gondola_tram_seams S11 gates it)
        out["outboard"] = {"catalog": cat["outboard"], "build": outboard, "agree": cat["outboard"] == outboard}
        if cat["outboard"] != outboard:
            print(f"[catalog] DRIFT {key}.outboard: catalog {cat['outboard']!r} vs this build {outboard!r} "
                  f"(the gondola stations pose the cabin by the catalog's sign)")
    for f in out["compared"]:
        a, b = cat.get(f), mine.get(f)
        if f not in cat:
            out["drift"].append({"field": f, "catalog": None, "generator": b})
        elif not same_value(a, b):
            out["drift"].append({"field": f, "catalog": a, "generator": b})
    for d in out["drift"]:
        lvl = "DRIFT" if d["catalog"] is not None else "missing"
        print(f"[catalog] {lvl} {key}.{d['field']}: catalog {d['catalog']!r} vs generator "
              f"{d['generator']!r}" + ("  <- R27 supersedes R5" if d["field"] == "hang_m" else ""))
    return out


# ======================================================================================
# Export / verify / preview
# ======================================================================================

def verify_export(fbx_path, expect_top, expect_lo=0.0):
    """Re-import an FBX: raw extent (cm, Y up), custom normals, the top (hang) contract and,
    unless expect_lo is None, the lowest point."""
    before = set(bpy.data.objects)
    op = bpy.ops.wm.fbx_import if hasattr(bpy.ops.wm, "fbx_import") else bpy.ops.import_scene.fbx
    op(filepath=fbx_path)
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    if not new:
        raise RuntimeError(f"verify: nothing imported from {fbx_path}")
    o = new[0]
    ys = np.array([v.co.y for v in o.data.vertices])
    ext = (ys.max() - ys.min())
    unit = 1.0 if ext > 10.0 else 100.0
    top = ys.max() * unit / 100.0
    lo = ys.min() * unit / 100.0
    info = {"file": os.path.basename(fbx_path), "top_m": round(float(top), 5),
            "min_y_m": round(float(lo), 5), "custom_normals": bool(o.data.has_custom_normals),
            "tris": cs2_asset.tri_count(o)}
    ok = (abs(top - expect_top) < 1e-3 and info["custom_normals"]
          and (expect_lo is None or abs(lo - expect_lo) < 1e-3))
    info["ok"] = ok
    print(f"[verify] {info['file']}: top {top:.4f} m (min y {lo:.4f}) vs {expect_top:.3f}"
          f"{'' if expect_lo is None else f' / {expect_lo:.3f}'}, custom normals "
          f"{info['custom_normals']} -> {'OK' if ok else 'FAIL'}")
    for obj in new:
        me = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
    return info


def _detail_copy(obj, z_min):
    """Temporary copy of the parts above z_min (authoring) for the close-up view."""
    d = _dup(obj, obj.name + "Detail")
    m = Mesh(d)
    bm = bmesh.new()
    bm.from_mesh(d.data)
    bm.faces.ensure_lookup_table()
    kill = [bm.faces[i] for i, f in enumerate(m.faces) if m.co[list(f), 2].max() < z_min]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(d.data)
    bm.free()
    return d


def _region_copy(obj, lo, hi):
    """Temporary copy of the faces whose centroid lies in the authoring box lo..hi (Revision 1
    logo close-up)."""
    d = _dup(obj, obj.name + "Region")
    m = Mesh(d)
    bm = bmesh.new()
    bm.from_mesh(d.data)
    bm.faces.ensure_lookup_table()
    lo, hi = np.array(lo), np.array(hi)
    kill = [bm.faces[i] for i, f in enumerate(m.faces)
            if not (np.all(m.co[list(f)].mean(axis=0) >= lo)
                    and np.all(m.co[list(f)].mean(axis=0) <= hi))]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(d.data)
    bm.free()
    return d


def _rope_fit_copy(obj, ropes):
    """Copy + rope stand-in cylinders (drawn lane radii) for the normals rope-fit frame."""
    d = _dup(obj, obj.name + "Ropes")
    parts = [(d, None)]
    for k, (y, z, r, half) in enumerate(ropes):
        c = cs2_kit.add_cyl(f"ropefit{k}", r, 2 * half, (y, 0.0, z), cs2_kit._axis_rot("Y"), 6, "both")
        # authoring x (travel) = Blender -Y after orient_for_net; lateral y = Blender X
        apply_transform(c)
        parts.append((c, None))
    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = d
    bpy.ops.object.join()
    return d


def _drop(obj):
    me = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    if me is not None and me.users == 0:
        bpy.data.meshes.remove(me)


def render_previews(obj, asset_dir, name, prev_dir, lods, kind, ab, sets, detail_z, ropes,
                    logo_view=None, view_sign=1.0):
    """`view_sign` -1 mirrors every camera's x (nitpick A3: the -Y gondola is shot from the side its
    doors / stalk moved to, so each frame is the mirror image of the approved one)."""
    import cs2_preview
    os.makedirs(prev_dir, exist_ok=True)
    set1 = {"R": sets[0][1], "G": sets[0][2], "B": sets[0][3]}
    views = view_dirs(CABIN_VIEWS, view_sign)
    modes = ["pbr", "dusk", "normals", "density"]
    if lods:
        modes.append("lod")
    if ab and os.path.isdir(ab):
        modes.append("ab")
    out = cs2_preview.preview(obj, asset_dir, name, prev_dir, modes=tuple(modes),
                              views=views, lods=list(lods) or None,
                              prev_generation=ab if "ab" in modes else None, tint=set1,
                              ground=False, resolution=PREVIEW_RES)
    det = _detail_copy(obj, detail_z)
    try:
        out += cs2_preview.preview(det, asset_dir, name, prev_dir, modes=("pbr", "normals"),
                                   views=view_dirs((("carriage", (0.9, -0.6, 0.35)),), view_sign),
                                   tint=set1, ground=False, resolution=(1200, 1000))
    finally:
        _drop(det)
    if logo_view is not None:                   # Revision 1: the stamped logo, close
        lo, hi, vdir = logo_view
        reg = _region_copy(obj, lo, hi)
        try:
            out += cs2_preview.preview(reg, asset_dir, name, prev_dir, modes=("pbr",),
                                       views=view_dirs((("logo", vdir),), view_sign), tint=set1,
                                       ground=False, resolution=(1200, 1000))
        finally:
            _drop(reg)
    rf = _rope_fit_copy(obj, ropes)
    try:
        for vname, vdir in view_dirs((("ropefit_end", (0.0, -1.0, 0.05)),
                                      ("ropefit_side", (1.0, 0.0, 0.05))), view_sign):
            out += cs2_preview.preview(rf, asset_dir, name, prev_dir, modes=("normals",),
                                       views=((vname, vdir),), ground=False,
                                       resolution=(1000, 1200))
    finally:
        _drop(rf)
    tmp = tempfile.mkdtemp(prefix="cabin_tint_")
    try:
        for k, s in enumerate(sets, start=1):
            tint = {"R": s[1], "G": s[2], "B": s[3]}
            paths = cs2_preview.preview(obj, asset_dir, name, tmp, modes=("pbr",),
                                        views=(views[0],), tint=tint, ground=False,
                                        resolution=PREVIEW_RES)
            for p in paths:
                dst = os.path.join(prev_dir, f"{name}_tint{k}_three_quarter.png")
                shutil.move(p, dst)
                out.append(dst)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    return out


# ======================================================================================
# One asset: bake, export, LODs, contracts, manifest, validate, verify, preview, review
# ======================================================================================

def asset_dir_for(args, name, root=None):
    root = root or (os.path.abspath(args.out) if args.export else DRAFT_ROOT)
    d = os.path.join(root, name)
    os.makedirs(d, exist_ok=True)
    for f in os.listdir(d):                     # a rebuild never inherits stale files
        p = os.path.join(d, f)
        if os.path.isfile(p) and f.startswith(name) and f.lower().endswith((".fbx", ".png", ".json")):
            os.remove(p)
    return d


def finish_asset(args, cfg, obj, sources, result):
    """Everything after the LOD0 join for one asset (cfg = per-asset settings dict)."""
    import cs2_bake
    name = cfg["name"]
    quality = "finish" if args.finish else "draft"
    A = cfg["tex"] if quality == "finish" else 256
    ao = cfg["ao"] if quality == "finish" else 8
    # draft: A // 64 (4 px at 256) - cs2_uv's default floor of 16 px is a 1/16 FRACTION
    # margin at 256 and ~400 islands cannot fit (measured: pack left UVs up to 8.3)
    margin = cfg["margin"] if quality == "finish" else max(2, A // 64)
    asset_dir = asset_dir_for(args, name, cfg.get("out_root"))
    result["asset_dir"] = asset_dir
    result["quality"] = quality
    result["texture_px"] = A

    contracts = {0: cfg["contracts"](obj, 0)}
    result["contracts"] = contracts
    result["shipped_uv_before_bake"] = sorted(l.name for l in obj.data.uv_layers)

    if cfg.get("remap"):
        # --envelope (GD_ENVELOPE): no bake - gondola_shipped_uvs put the shipped atlas on this mesh
        post_rep = {}
        result["bake"] = dict({k: v for k, v in cfg["remap"].items() if k != "dir"}, A=A,
                              files=_copy_shipped_maps(cfg["remap"], asset_dir, name), textures_added_mb=0.0)
    else:
        hook, post_rep = make_post_compose(obj, cfg, quality, cfg["stamps"], cfg.get("plate"))
        layout = stamp_layout(obj, cfg, A, margin)
        reserved = ()
        if layout is not None:
            reserved = layout["reserved"]
            sources = pin_sources(sources, layout["materials"])
            result["stamp_layout"] = layout["info"]
        bake = cs2_bake.bake_unique(obj, asset_dir, name, A, sources, reserved=reserved,
                                    margin_px=margin, ao_samples=ao, post_compose=hook,
                                    src_uv="UVMap")
        if layout is not None:
            result["stamp_layout"]["reserved_restored"] = len(bake.get("reserved_restored") or [])
        result["bake"] = {"A": A, "ao_samples": ao, "margin_px": bake["margin_px"],
                          "uncovered_texels": bake["uncovered_texels"], "files": bake["files"],
                          "materials": {str(k): {kk: v.get(kk) for kk in ("faces", "slot", "texels")}
                                        for k, v in bake["materials"].items()},
                          "seconds": {k: round(v, 1) for k, v in bake["seconds"].items()}}
    result["post"] = post_rep
    me = obj.data
    result["shipped_uv"] = [l.name for l in me.uv_layers]
    result["shipped_materials"] = len(me.materials)
    result["shipped_colour_attrs"] = [a.name for a in me.color_attributes]
    if result["shipped_uv"] != [cs2_uv.UNIQUE_LAYER] or result["shipped_materials"] \
            or result["shipped_colour_attrs"]:
        raise RuntimeError(f"{name}: after the bake expected one UV layer UVUnique, no materials, "
                           f"no color attributes; got {result['shipped_uv']}, "
                           f"{result['shipped_materials']} materials, {result['shipped_colour_attrs']}")
    cs2_asset.export_fbx(obj, os.path.join(asset_dir, f"{name}.fbx"))

    tris = {0: cs2_asset.tri_count(obj)}
    lods = []
    if args.lods:
        t0 = tris[0]
        lod1_fn = cfg.get("lod1") or make_lod
        lod1_args = (obj, name, 1, int(round(cfg["lod1_ratio_target"] * t0)), cfg["lod1_delete"],
                     cfg["lod1_protect"])
        lod1 = mirrored_lod(lod1_fn, *lod1_args) if cfg.get("mirror_lod1") else lod1_fn(*lod1_args)
        lods.append(lod1)
        lods.append(cfg["lod2"](obj, name))
        for k, lod in enumerate(lods, start=1):
            pin_height(lod, *cfg["z_range"])
            contracts[k] = cfg["contracts"](lod, k)
            tris[k] = cs2_asset.tri_count(lod)
            cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{lod.name}.fbx"))
    result["tris"] = tris
    budget = cfg["budget"](tris)
    result["budget"] = budget
    cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))

    bounds = bounds_authoring(obj)
    result["bounds_authoring"] = bounds
    result["hashes"] = {0: geometry_hash(obj)}
    for k, lod in enumerate(lods, start=1):
        result["hashes"][k] = geometry_hash(lod)

    import cs2_preview
    area = float(cs2_uv.face_world_areas(obj.data).sum())
    size = [hi - lo for lo, hi in zip(*bounds)]
    sw = cs2_preview.lod_switch_table(size, [tris[k] for k in sorted(tris)], area=area)
    result["lod_switch"] = {"switch_m": {str(k): round(v, 1) for k, v in sw["switch"].items()},
                            "cull_m": round(sw["cull"], 0), "bias": round(sw["bias"], 3),
                            "area_m2": round(area, 2), "lod_factor": 652.0,
                            "status": "formula prediction until the SkiProbeLodA verdict [U]"}

    manifest = {
        "generator": "lift_cabin.py", "args": CS.manifest_args(args, REPO),
        "kind": "hanging", "carrier": cfg["kind"], "role": cfg.get("role", "whole"),
        "quality": quality, "texture_px": A,
        "tris": {f"LOD{k}": v for k, v in tris.items()}, "budget": budget,
        "contract": cfg["contract_summary"], "contracts_held": {
            f"LOD{k}": [r["code"] for r in rows] for k, rows in contracts.items()},
        "bounds_authoring_m": {"min": bounds[0], "max": bounds[1]},
        "density_px_per_m": post_rep.get("density_px_per_m"),
        "density_hero_px_per_m": post_rep.get("density_hero"),
        "stamp_layout": result.get("stamp_layout"),
        "revision": "Revision 1 (verdicts 2026-10-11 items 6 / 9): soft-edged rebuild, "
                    "CAD-tell cleanup, pinned >= 4x logo square; Revision 1b (2026-10-11): "
                    "geometry defects C1-C4 fixed (cap triangulation, closed shell, 2 mm insets), "
                    "G11 / G12 / T12 + cap_check contracts" + (
                        "; Revision 2 (2026-10-11, tram): centered outward-opening sliding double "
                        "doors on both long sides (closed, 15 mm proud, gap-line seals, pocket "
                        "grab bars, release pulls, header rollers, threshold nosing, drip rail), "
                        "gondola detail parity (bolt rows above the texel floor, hanger pin, roof "
                        "hatches, end bumpers, intercoms), T13 door contract"
                        if cfg["kind"] == "tram" else "") + (
                        "; nitpick A3 (2026-10-13): the whole cabin mirrored across y = 0 - stalk, "
                        "carriage, grip and doors on authoring -Y = the carrier's RIGHT = OUTBOARD on "
                        "the stations' spin (catalog/carriers.json gd8 outboard -Y); exact mirror, "
                        "look / tris / hang 4.50 / door openings unchanged"
                        if cfg["kind"] == "gondola" and getattr(args, "outboard", "+Y") == "-Y" else ""),
        "lod_switch": result["lod_switch"],
        "catalog": result.get("catalog"),
        "clearance": result.get("clearance"),
        "prefab_numbers": cfg.get("prefab_numbers") or prefab_numbers(
            cfg["kind"], name, bounds, cfg.get("dims"), getattr(args, "outboard", "+Y")),
        "code_consequences": CODE_CONSEQUENCES[cfg["kind"]],
        "rulings": cfg["rulings"], "ingest": result.get("ingest"),
        "credit": cfg.get("credit"),
        "geometry_hashes": {f"LOD{k}": v for k, v in result["hashes"].items()},
    }
    manifest.update(cfg.get("manifest_extra") or {})
    result["manifest"] = cs2_validate.write_manifest(asset_dir, name, manifest)

    if not args.no_validate:
        spec = {"kind": "hanging", "lod0_cap": cfg["lod0_cap"], "lod1_ratio": 0.48,
                "lod2_cap": cfg["lod2_cap"], "expected_lods": tuple(range(1, len(lods) + 1)),
                "expected_submeshes": ()}
        if not args.finish:
            spec["min_texture"] = 128       # the draft bake is 256 px by design; only --finish output ships
        findings = cs2_validate.validate_asset_folder(asset_dir, name, spec)
        result["validation"] = {"errors": sum(1 for f in findings if f[0] == "ERROR"),
                                "warnings": sum(1 for f in findings if f[0] == "WARN")}

    if args.verify:
        zl, zh = cfg["z_range"]
        res = [verify_export(os.path.join(asset_dir, f"{name}.fbx"), zh, zl)]
        for k in range(1, len(lods) + 1):
            res.append(verify_export(os.path.join(asset_dir, f"{name}_LOD{k}.fbx"), zh, zl))
        result["verify"] = res
        if not all(r["ok"] for r in res):
            raise RuntimeError(f"{name}: verify failed on re-import: {res}")

    if args.preview:
        result["previews"] = render_previews(obj, asset_dir, name, os.path.abspath(args.preview),
                                             lods, cfg["kind"], args.ab, cfg["sets"],
                                             cfg["detail_z"], cfg["ropes"], cfg.get("logo_view"),
                                             view_sign=cfg.get("view_sign", 1.0))
    if args.review is not None:
        import cs2_review
        s1 = cfg["sets"][0]
        result["review_pack"] = cs2_review.write_pack(
            name, asset_dir, dict(enumerate([obj] + lods)), {},
            meta={"generator": "lift_cabin.py", "args": CS.manifest_args(args, REPO), "kind": "hanging",
                  "caps": {0: cfg["lod0_cap"], 1: int(0.48 * tris[0]), 2: cfg["lod2_cap"]},
                  "tint": {"R": s1[1], "G": s1[2], "B": s1[3]}},
            out_dir=args.review or None)
    png = {}
    for f in sorted(os.listdir(asset_dir)):
        if f.lower().endswith(".png"):
            with open(os.path.join(asset_dir, f), "rb") as fh:
                png[f] = hashlib.sha1(fh.read()).hexdigest()
    result["png_sha1"] = png
    if cfg.get("keep_lods"):            # --split: the caller joins body + grip LODs for the pair contracts
        result["lod_objs"] = lods
    else:
        for lod in lods:
            _drop(lod)
    return result


# ======================================================================================
# Per-kind drivers
# ======================================================================================

def _lint(name):
    rep = cs2_validate._Report(name)
    cs2_validate.lint_asset_name(name, rep)
    if rep.counts()[0]:
        raise RuntimeError(f"asset name {name!r} fails the importer lint: {list(rep)}")


def _budget_gondola(tris):
    t0 = tris[0]
    proj = G["lod0_projection"]
    b0, b1 = G["lod0_band"]
    rows = {"LOD0": t0, "LOD0_band": [b0, b1], "LOD0_cap": G["lod0_cap"], "projection": proj}
    ok = t0 <= G["lod0_cap"] and b0 <= t0 <= b1 and abs(t0 - proj) <= 0.10 * proj
    if 1 in tris:
        rows["LOD1_ratio"] = round(tris[1] / t0, 3)
        ok = ok and tris[1] <= 0.48 * t0
    if 2 in tris:
        rows["LOD2"] = tris[2]
        ok = ok and tris[2] <= G["lod2_cap"] and tris[2] >= 150
    rows["ok"] = ok
    if not ok:
        raise RuntimeError(f"gondola budget broken: {rows} (LOD0 in {G['lod0_band']} cap 2,500, LOD1 <= "
                           f"0.48 x LOD0, LOD2 ~200 <= {G['lod2_cap']})")
    return rows


def _budget_tram(tris, proj, cap0=None, lod2_cap=None):
    t0 = tris[0]
    cap0 = cap0 or T["lod0_cap"]
    lod2_cap = lod2_cap or T["lod2_cap"]
    rows = {"LOD0": t0, "LOD0_cap": cap0, "projection": proj,
            "RS_target": 6000 if proj == T["lod0_projection"] else None}
    ok = t0 <= cap0 and abs(t0 - proj) <= 0.10 * proj
    if 1 in tris:
        rows["LOD1_ratio"] = round(tris[1] / t0, 3)
        ok = ok and tris[1] <= 0.48 * t0
    if 2 in tris:
        rows["LOD2"] = tris[2]
        ok = ok and tris[2] <= lod2_cap
    rows["ok"] = ok
    if not ok:
        raise RuntimeError(f"tram budget broken: {rows}")
    return rows


def _gondola_cfg(args, name, contracts, lod2):
    """The one-piece gondola's finish_asset settings (the --split body starts from them)."""
    return dict(kind="gondola", name=name, tex=G["tex"], ao=G["ao"], margin=G["margin"],
                contracts=contracts, lod2=lod2, lod1_ratio_target=0.46,
                lod1_delete=G["lod1_delete"], lod1_protect=G["lod1_protect"],
                z_range=(0.0, G["hang"]), budget=_budget_gondola,
                lod0_cap=G["lod0_cap"], lod2_cap=G["lod2_cap"],
                # both logo quads share ONE pinned square (stamp_layout): paint it once
                stamps={"logo_front": (G["logo_size"], G["logo_size"])},
                stamp={"logo": ("logo_",), "plate": ("plate_face",),
                       "fill": G["fill_prefixes"], "logo_frac": G["logo_frac"],
                       "plate_ppm": G["plate_ppm"], "fill_cap_ppm": 260.0},
                hero=("body_", "doorbody_", "trim_", "doortrim_", "roof_", "logo_", "hanger_"),
                plate=args.plate_number, detail=gondola_detail, sets=G_SETS, detail_z=2.3,
                logo_view=((0.70, -1.2, -0.01), (1.30, 1.2, 0.95), (0.25, -1.0, 0.25)),
                view_sign=(1.0 if args.outboard == "+Y" else -1.0),
                mirror_lod1=(args.outboard == "-Y"),
                ropes=[(0.0, G["rope"], HV_ROPE_R, 2.0)],
                contract_summary={"hang_m": G["hang"], "rope_m": G["rope"],
                                  "grip_top_above_rope_m": 0.08, "outboard": args.outboard,
                                  "body_m": G["body"], "wilin_mount_m": G["wilin_mount_m"],
                                  "door_plug": {"loops": 3, "tris": 17}},
                rulings=["R5 hang 4.50", "R8 painted glass", "R18 1024",
                         ("C4 outboard +Y" if args.outboard == "+Y" else
                          "C4 outboard -Y (nitpick A3: the carrier's right on the stations' spin)"),
                         "C5 rope 4.42", "C6 LOD2 proxy", "C7 LOD1 <= 0.48", "MC2 G5 centroid",
                         "MC3 one UVMap", "MC4 lane sag rows", "MC6 numeric fixes", "R31 lanes"],
                credit=("\"Ski gongola and lift station detailed drafts\" by Wilin_Amaurea "
                        "(https://sketchfab.com/3d-models/ski-gongola-and-lift-station-detailed-"
                        "drafts-a8fabd3e955643a2a9e9f09bd46635db), CC BY 4.0; modified (re-scaled, "
                        "re-topologised, re-UV'd, re-textured)"))


def run_gondola(args):
    if args.split:
        return run_gondola_split(args)
    name = cs2_asset.validate_asset_name(args.name or NAMES["gondola"])
    _lint(name)
    result = {"name": name, "kind": "gondola"}
    result["catalog"] = catalog_drift("gondola", args.carriers, outboard=args.outboard)
    result["clearance"] = clearance_rows("gondola")
    ingest = {}
    parts = gondola_parts(args, ingest)
    result["ingest"] = ingest
    result["uv_layers_before_sources"] = assert_uv_layers(parts, stage="gondola parts")
    table = []
    obj, sources, table = build_lod0(parts, name, G_DENSITY, table)
    result["parts"] = len(table)

    def contracts(o, level):
        return gondola_contracts(o, level, args.outboard, single_quads=(level == 0))

    def lod2(o, nm):
        if args.lod2 == "proxy":
            return gondola_proxy(o, nm, args.outboard)
        return make_lod(o, nm, 2, G["lod2_target"], G["lod2_delete"],
                        ("grip_", "hanger_", "carframe_", "yoke_"))

    cfg = _gondola_cfg(args, name, contracts, lod2)
    return [finish_asset(args, cfg, obj, sources, result)]


# ======================================================================================
# --split (carrier split 2026-11-03): GRIP + BODY, pivot pin between them
# ======================================================================================

#: The gondola joint (BRIEF "The split", carrier_split.py header). The one-piece hanger head (0.40 x
#: 0.19 x 0.13, z 3.93-4.06) becomes the GRIP's head: same plan and rounding, 8 cm tall (z 3.98-4.06),
#: two cheek plates hanging under it carry the LATERAL pin (authoring y) with a pin end on each outer
#: face (the clevis of a detachable gondola hanger). The stalk tube is cut 15 cm under the pin and ends
#: in a tapered neck and the BODY's knuckle (eye) between the cheeks. pin z = head underside - R - c.
#: Tri counts (measured, asserted): pin_detail = body + grip LOD0 - the one-piece LOD0 (the brief's
#: budget is the old cap + this); the per-mesh caps are the measured tallies (cs2_validate caps).
G_SPLIT = dict(
    head_bot=3.98, head_top=4.06, head_r=0.03,
    knuckle_r=0.060, knuckle_segs=(8, 4),
    neck_len=0.15, neck_taper=0.06, neck_r=0.045,   # cut ring (r 0.085) -> 0.045 over 6 cm, then straight
    cheek_t=0.018, cheek_r=0.070, cheek_bury=0.015,
    pin_r=0.026, pin_len=0.014,
    r_x=0.22,                                       # S1 joint zone round the pin center
    grip_prefixes=("hanger_head", "hanger_cheek_", "hanger_pin_", "spring_", "carframe_", "roller_",
                   "grip_"),
    # grip texture by texel density: ~2.3 m2 of carriage at 512 packs to ~200-260 px/m, above the
    # cabin's 120-200 px/m hero faces at 2048; 256 would ship ~110 px/m (and cs2_validate floors 512)
    grip_tex=512, grip_margin=8,
    # measured 2026-11-03: one-piece parts 2412 -> body 1964 + grip 624 (knuckle 48, neck 24 more on the
    # cut stalk, cheeks 2 x 22, pin ends 2 x 18; the 8 cm head has the 13 cm head's tris); LOD2 proxies
    # body 188 (loft 156 + stalk box 24 + octahedron 8) + grip 36 (three boxes) = the one-piece 204 + 20
    pin_detail=176, pin_detail_lod2=20,
    # critique 2026-11-03: the 8 carriage rollers 10 -> 16 segments (3 x segs tris: 30 -> 48, +144 on
    # the grip; LOD1 deletes them, the LOD2 proxy has none). The pair 2580 + 144 = 2724 is 48 over the
    # BRIEF cap 2500 + 176 = 2676 (+1.8 %): raised by 48 (rule: <= 5 %, DECISIONS 585)
    roller_segs=16, segs_detail=144, cap_raise=48,
    body_caps=(1964, 188), grip_caps=(768, 36),     # (LOD0, LOD2) tallies = the cs2_validate caps
)

#: B2 v2 grip passage (--envelope with --split, 2026-11-03; refs/SOURCES.md "Grip passage under
#: compression": Poma US4441430 / US4691643 - the sheave liners roll over the grip's smooth jaw, never
#: its body). The tower gate's rule: inside the GD wheel span plus margin (outboard-positive lateral
#: u < u_in) nothing of the carrier reaches more than z above or below the rope center, down to the
#: wheel bottoms (z_bot under it). The jaw bar's top drops 0.08 -> 0.06 there and a 45 deg chamfer
#: (u_in .. u_top) climbs back to the 0.08 top outboard (C5 grip_top_above_rope stays). The cut and
#: the move run on the rebuilt LOD0 after the SHIPPED B atlas is read onto it (gondola_shipped_uvs):
#: no bake, 0 MB added. tris / lod2_tris = what the edit adds (asserted): two cuts on the top, one on
#: the underside, both end caps re-filled as an inboard and an outboard piece; LOD2 = the grip box
#: split into the outboard box + a jaw box. New letter for the grip only; the body stays B.
GD_ENVELOPE = dict(z=0.06, z_bot=0.54, u_in=0.09, u_top=0.11, letter="C", tris=12, lod2_tris=10)
ENVELOPE = False                    # run() sets it from --envelope
SPLIT_MAPS = ("BaseColor", "Normal", "MaskMap", "ControlMask")


def gondola_shipped_uvs(obj, fbx):
    """--envelope: the shipped LOD0's atlas read onto `obj`, its rebuild (same code path, before any
    envelope edit): every FBX triangle lands on the rebuilt polygon holding its three corners (positions
    within 0.2 mm; coincident vertices of touching parts resolved by the triangle's normal), each
    corner's UV onto that polygon's loop. Leaves one UV layer UVUnique, no materials, no color
    attributes (what bake_unique leaves). Returns the match numbers."""
    from mathutils.kdtree import KDTree
    me = obj.data
    mw = obj.matrix_world
    rco = np.array([tuple(mw @ v.co) for v in me.vertices], dtype=np.float64)
    kd = KDTree(len(rco))
    for i, c in enumerate(rco):
        kd.insert(c, i)
    kd.balance()
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=fbx)
    new = [o for o in bpy.data.objects if o not in before]
    try:
        imp = next(o for o in new if o.type == "MESH")
        imw = imp.matrix_world
        ico = np.array([tuple(imw @ v.co) for v in imp.data.vertices], dtype=np.float64)
        best = None
        for cand in (ico, np.c_[ico[:, 0] / 100.0, ico[:, 2] / 100.0, ico[:, 1] / 100.0],
                     np.c_[ico[:, 0] / 100.0, -ico[:, 2] / 100.0, ico[:, 1] / 100.0]):
            err = max(kd.find(tuple(p))[2] for p in cand[:: max(1, len(cand) // 200)])
            if best is None or err < best[0]:
                best = (err, cand)
        mp = best[1]
        tol = 2e-4
        near = [[j for _c, j, _d in kd.find_range(tuple(p), tol)] for p in mp]
        worst = max(kd.find(tuple(p))[2] for p in mp)
        if any(not c for c in near):
            raise RuntimeError(f"remap {obj.name}: the rebuild does not match {os.path.basename(fbx)} "
                               f"({len(rco)} / {len(ico)} vertices, worst {worst * 1000:.3f} mm)")
        vpolys = [[] for _ in range(len(rco))]
        for p in me.polygons:
            for v in p.vertices:
                vpolys[v].append(p.index)
        R3 = mw.to_3x3()
        pn = np.array([tuple((R3 @ p.normal).normalized()) for p in me.polygons], dtype=np.float64)
        uv_in = imp.data.uv_layers.get(cs2_uv.UNIQUE_LAYER) or imp.data.uv_layers[0]
        uv = np.full((len(me.loops), 2), np.nan)
        amb = conflict = lost = 0
        for tri in imp.data.polygons:
            lis = list(tri.loop_indices)
            vis = [imp.data.loops[li].vertex_index for li in lis]
            corners = [near[v] for v in vis]
            polys = set.intersection(*({pp for v in c for pp in vpolys[v]} for c in corners))
            if len(polys) > 1:
                P = mp[vis]
                n = np.cross(P[1] - P[0], P[2] - P[0])
                n = n / max(np.linalg.norm(n), 1e-12)
                polys = {pi for pi in polys if abs(float(pn[pi] @ n)) > 0.99} or polys
                amb += len(polys) > 1
            if not polys:
                lost += 1
                continue
            pi = min(polys)
            loops = {me.loops[li].vertex_index: li for li in me.polygons[pi].loop_indices}
            for li, c in zip(lis, corners):
                dst = next((loops[v] for v in c if v in loops), None)
                if dst is None:
                    continue
                u = tuple(uv_in.data[li].uv)
                if not np.isnan(uv[dst, 0]) and np.abs(uv[dst] - u).max() > 1e-5:
                    conflict += 1
                uv[dst] = u
        missing = int(np.isnan(uv[:, 0]).sum())
        if missing or conflict or lost:
            raise RuntimeError(f"remap {obj.name}: {missing} loops without a shipped UV, {conflict} "
                               f"conflicts, {lost} unplaced / {amb} ambiguous triangles")
    finally:
        for o in new:
            m = o.data if o.type == "MESH" else None
            bpy.data.objects.remove(o, do_unlink=True)
            if m is not None and m.users == 0:
                bpy.data.meshes.remove(m)
    lay = me.uv_layers.get(cs2_uv.UNIQUE_LAYER) or me.uv_layers.new(name=cs2_uv.UNIQUE_LAYER)
    lay.data.foreach_set("uv", uv.astype(np.float32).reshape(-1))
    for nm in [l.name for l in me.uv_layers if l.name != cs2_uv.UNIQUE_LAYER]:
        me.uv_layers.remove(me.uv_layers[nm])
    me.uv_layers[cs2_uv.UNIQUE_LAYER].active = True
    me.uv_layers[cs2_uv.UNIQUE_LAYER].active_render = True
    me.materials.clear()
    for nm in [a.name for a in me.color_attributes]:
        me.color_attributes.remove(me.color_attributes[nm])
    me.update()
    return {"remap_from": os.path.splitext(os.path.basename(fbx))[0],
            "worst_vertex_mm": round(worst * 1000.0, 4), "ambiguous_tris": amb}


def gondola_envelope_jaw(obj, outboard, rope_z):
    """--envelope on the grip LOD0 (net frame, base pivot; rope_z = the rope center there): cut the
    grip_body prism's top at u_in and u_top and its underside at u_in, re-fill both end caps as an
    inboard and an outboard piece, then lower the top inboard of u_in to rope_z + z (the u_in .. u_top
    strip becomes the chamfer). Every loop UV comes from its face's plane -> UV map fitted BEFORE the
    cut and the move (affine per face: atlas islands are similarity images of the planar faces), so
    the shipped texels stay put. Returns the tris added."""
    E = GD_ENVELOPE
    sgn = 1.0 if outboard == "+Y" else -1.0         # net x = authoring y; u = outboard-positive lateral
    me = obj.data
    pid = json.loads(me[PART_TABLE_PROP]).index("grip_body") + 1
    t0 = cs2_asset.tri_count(obj)
    bm = bmesh.new()
    bm.from_mesh(me)
    src = bm.faces.layers.int.new("env_src")          # first: a new layer drops element references
    play = bm.faces.layers.int["part"]
    uvl = bm.loops.layers.uv[cs2_uv.UNIQUE_LAYER]
    jaw = [f for f in bm.faces if f[play] == pid]
    bm.normal_update()

    def u_of(v):
        return sgn * v.co.x

    def plane_map(faces, n):
        ax = [a for a in range(3) if a != int(np.argmax(np.abs(n)))]   # drop the normal's main axis
        P = np.array([(l.vert.co[ax[0]], l.vert.co[ax[1]], 1.0) for f in faces for l in f.loops])
        U = np.array([tuple(l[uvl].uv) for f in faces for l in f.loops])
        M = np.linalg.lstsq(P, U, rcond=None)[0]
        res = float(np.abs(P @ M - U).max())
        if res > 1e-4:
            raise RuntimeError(f"envelope: grip_body UVs are not one affine map per face (residual "
                               f"{res:.2e})")
        return ax, M

    def put_uv(f, fmap):
        ax, M = fmap
        for l in f.loops:
            l[uvl].uv = tuple(np.array((l.vert.co[ax[0]], l.vert.co[ax[1]], 1.0)) @ M)

    caps = [f for f in jaw if abs(f.normal.y) > 0.99]    # net y = travel: the prism's end caps
    cap_y = sorted({round(f.verts[0].co.y, 5) for f in caps})
    cap = {}
    for y in cap_y:
        fs = [f for f in caps if abs(f.verts[0].co.y - y) < 1e-5]
        cap[y] = (plane_map(fs, (0.0, 1.0, 0.0)), 1.0 if fs[0].normal.y > 0.0 else -1.0, fs[0])
    smap = {}
    for i, f in enumerate((f for f in jaw if f not in caps), start=1):
        f[src] = i
        smap[i] = plane_map([f], f.normal)
    protos = {y: (c[2].smooth, c[2].material_index) for y, c in cap.items()}
    bmesh.ops.delete(bm, geom=caps, context="FACES_ONLY")
    ztop = max(v.co.z for f in bm.faces if f[play] == pid for v in f.verts)

    def cut(u, top_only):
        fs = [f for f in bm.faces if f[play] == pid
              and min(map(u_of, f.verts)) < u - 1e-6 and max(map(u_of, f.verts)) > u + 1e-6
              and (not top_only or all(abs(v.co.z - ztop) < 1e-6 for v in f.verts))]
        geom = list(set(fs) | {e for f in fs for e in f.edges} | {v for f in fs for v in f.verts})
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-7, plane_co=(sgn * u, 0.0, 0.0),
                               plane_no=(1.0, 0.0, 0.0))

    n_strip = len(smap)
    cut(E["u_in"], False)
    cut(E["u_top"], True)
    strip = [f for f in bm.faces if f[play] == pid]
    if len(strip) != n_strip + 3 or not all(f[src] for f in strip):
        raise RuntimeError(f"envelope: the cuts left {len(strip)} grip_body strip faces (want "
                           f"{n_strip} + 3, each tagged with its source face)")
    for f in bm.faces:
        if f[src]:
            put_uv(f, smap[f[src]])
    new_caps = []
    for y, (cmap, sign, _f) in cap.items():
        ring_v = {v for f in bm.faces if f[play] == pid for v in f.verts if abs(v.co.y - y) < 1e-5}
        adj = collections.defaultdict(list)
        for e in bm.edges:
            if e.is_boundary and all(v in ring_v for v in e.verts):
                a, b = e.verts
                adj[a].append(b)
                adj[b].append(a)
        if any(len(n) != 2 for n in adj.values()) or len(adj) != len(ring_v):
            raise RuntimeError(f"envelope: cap y {y:+.3f} outline is not one loop")
        ring = [next(iter(adj))]
        prev = None
        while True:
            nxt = next(w for w in adj[ring[-1]] if w is not prev)
            if nxt is ring[0]:
                break
            prev = ring[-1]
            ring.append(nxt)
        cuts = [k for k, v in enumerate(ring) if abs(u_of(v) - E["u_in"]) < 1e-6]
        if len(ring) != len(ring_v) or len(cuts) != 2:
            raise RuntimeError(f"envelope: cap y {y:+.3f} ring {len(ring)} / {len(ring_v)} vertices, "
                               f"{len(cuts)} on u {E['u_in']}")
        i, j = cuts
        for arc in (ring[i:j + 1], ring[j:] + ring[:i + 1]):
            f = bm.faces.new(arc)
            f[play], f[src] = pid, 0
            f.smooth, f.material_index = protos[y]
            f.normal_update()
            if f.normal.y * sign < 0.0:
                f.normal_flip()
            put_uv(f, cmap)
            new_caps.append(f)
    low = {v for f in bm.faces if f[play] == pid for v in f.verts
           if abs(v.co.z - ztop) < 1e-6 and u_of(v) <= E["u_in"] + 1e-6}
    if any(g[play] != pid for v in low for g in v.link_faces):
        raise RuntimeError("envelope: a lowered jaw vertex is welded to another part")
    for v in low:
        v.co.z = rope_z + E["z"]
    triangulate_caps(bm, new_caps, "envelope jaw")
    bm.faces.layers.int.remove(src)
    bm.to_mesh(me)
    bm.free()
    me.update()
    cs2_asset.shade_by_angle(obj, SHADE_ANGLE)
    return cs2_asset.tri_count(obj) - t0


def _copy_shipped_maps(remap, asset_dir, name):
    """--envelope: the shipped map set copied under the new mesh name (the importer finds maps by it)."""
    out = []
    for s in SPLIT_MAPS:
        dst = os.path.join(asset_dir, f"{name}_{s}.png")
        shutil.copyfile(os.path.join(remap["dir"], f"{remap['remap_from']}_{s}.png"), dst)
        out.append(os.path.basename(dst))
    return out


def gondola_split_parts(parts, outboard):
    """The joint on the finished (mirrored) gondola parts, ASSEMBLED frame. Returns (body, grip, old,
    joint): body / grip = [(obj, material)] (the grip still at its assembled height), old = the
    one-piece part list for S1 (copies of the two parts this replaces), joint = the numbers."""
    J = G_SPLIT
    sgn = 1.0 if outboard == "+Y" else -1.0
    ys = sgn * G["hanger_b"][1]
    R, c = J["knuckle_r"], CS.DESIGN_GAP_M
    pz = J["head_bot"] - R - c
    z_n = pz - J["neck_len"]
    by = {o.name: (o, k) for o, k in parts}
    arm, arm_k = by["hanger_arm"]
    head, head_k = by["hanger_head"]
    old = [(o, k) for o, k in parts if o is not arm and o is not head]
    old += [(_dup(arm, "old_hanger_arm"), arm_k), (_dup(head, "old_hanger_head"), head_k)]
    keep = [(o, k) for o, k in parts if o is not head]
    hm = head.data
    bpy.data.objects.remove(head, do_unlink=True)
    bpy.data.meshes.remove(hm)
    # the stalk: cut 15 cm under the pin; the open cut ring is where the neck starts
    CS.cut_part(arm, (0.0, 0.0, z_n), (0.0, 0.0, 1.0))
    ring = [v.co.copy() for v in arm.data.vertices if abs(v.co.z - z_n) < 1e-6]
    radii = [math.hypot(v.x, v.y - ys) for v in ring]
    if len(ring) != G["hanger_segs"] or max(radii) - min(radii) > 1e-5:
        raise RuntimeError(f"gondola split: the stalk cut ring has {len(ring)} vertices, radii "
                           f"{min(radii) if radii else 0:.5f}..{max(radii) if radii else 0:.5f} "
                           f"(want {G['hanger_segs']} on one circle)")
    r_arm = sum(radii) / len(radii)
    a0 = math.atan2(ring[0].y - ys, ring[0].x)
    neck = CS.ring_loft("hanger_neck", [(0.0, r_arm), (J["neck_taper"], J["neck_r"]),
                                        (J["neck_len"], J["neck_r"])], G["hanger_segs"],
                        ((0.0, ys, z_n), (0.0, ys, pz)), angle0=a0, relief="map:seams")
    n0 = [v.co for v in neck.data.vertices if abs(v.co.z - z_n) < 1e-6]
    miss = max(min((p - q).length for q in ring) for p in n0)
    if miss > 1e-5:
        raise RuntimeError(f"gondola split: neck ring 0 misses the cut ring by {miss * 1000:.3f} mm")
    kn = CS.knuckle("hanger_knuckle", (0.0, ys, pz), R, J["knuckle_segs"], relief="geo",
                    meridian_xz=(1.0, 0.0))
    hz = J["head_top"] - J["head_bot"]
    new_head = rounded_box("hanger_head", (0.40, 0.19, hz), (0.0, ys, J["head_bot"] + hz / 2.0),
                           J["head_r"], segs=2)
    new_head["relief"] = "map:bolts"
    apply_transform(new_head)
    t, rc = J["cheek_t"], J["cheek_r"]
    prof = CS.plate_profile(-rc, rc, J["head_bot"] + J["cheek_bury"], (0.0, pz), rc, arc_segs=4)
    gin = R + c
    cheeks = [CS.slab_y("hanger_cheek_0", prof, ys - gin - t, ys - gin),
              CS.slab_y("hanger_cheek_1", prof, ys + gin, ys + gin + t)]
    pins = [CS.pin_end("hanger_pin_0", ys - gin - t, (0.0, pz), J["pin_r"], J["pin_len"], -1.0),
            CS.pin_end("hanger_pin_1", ys + gin + t, (0.0, pz), J["pin_r"], J["pin_len"], 1.0)]
    allp = keep + [(neck, "galv"), (kn, "galv"), (new_head, "galv")] + \
        [(o, "galv") for o in cheeks + pins]
    grip = [(o, k) for o, k in allp if o.name.startswith(J["grip_prefixes"])]
    body = [(o, k) for o, k in allp if not o.name.startswith(J["grip_prefixes"])]
    joint = {"pin": (0.0, ys, pz), "ys": ys, "pz": pz, "z_cut": z_n, "r_arm": r_arm,
             "knuckles": ["hanger_knuckle"], "cheeks": ["hanger_cheek_0", "hanger_cheek_1"],
             "floor": ["hanger_head"], "r_x": J["r_x"], "old_skip": ("old_hanger_head",),
             "new_skip": ("hanger_head", "hanger_cheek_", "hanger_pin_", "hanger_knuckle",
                          "hanger_neck"),
             "rope": (0.0, 0.0, G["rope"])}
    return body, grip, old, joint


def _world_z(parts):
    zs = [(o.matrix_world @ v.co).z for o, _k in parts for v in o.data.vertices]
    return min(zs), max(zs)


def split_pair_copy(body, grip, grip_z0, name):
    """A joined COPY of body + grip at zero pose (the grip copy raised by grip_z0), part table = body +
    grip (the grip's ids offset): the one-piece carrier every G / T contract runs on. Caller drops it."""
    tb = json.loads(body.data[PART_TABLE_PROP])
    tg = json.loads(grip.data[PART_TABLE_PROP])
    cb, cg = _dup(body, name), _dup(grip, f"{name}_g")
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
    pair.data[PART_TABLE_PROP] = json.dumps(tb + tg)
    pair[FRAME_PROP] = body.get(FRAME_PROP)
    return pair


def gondola_split_proxy(lod0, name, outboard, joint, role):
    """C6 LOD2 of one split mesh, cut at the pin. body = gondola_proxy's cabin loft + the stalk box up to
    the pin + an octahedron knuckle (its top = the body's top contract); grip = gondola_proxy's frame
    and jaw boxes + a yoke box from the cheek bottom 2 cm into the frame (it hides the stalk end).
    Built at +Y and mirrored like gondola_proxy; the grip is lowered by its base pivot shift."""
    yb = G["hanger_b"][1]
    pz = joint["pz"]
    if role == "body":
        rings = [rrect_ring(2.00, 1.80, 0.35, 4, 0.0), rrect_ring(2.30, 2.10, 0.35, 4, 0.82),
                 rrect_ring(2.26, 2.06, 0.35, 4, 2.30), rrect_ring(2.10, 1.90, 0.35, 4, 2.40)]
        parts = [(loft_band("body_proxyskirt", rings[0], rings[1]), None),
                 (ring_cap("body_proxyunder", rings[0], up=False), None),
                 (loft_band("glass_proxyband", rings[1], rings[2]), None),
                 (loft_band("roof_proxyedge", rings[2], rings[3]), None),
                 (ring_cap("roof_proxycap", rings[3], up=True), None)]
        a = (0.0, G["hanger_a"][1], 2.52)
        b = (0.0, yb, 2.52)
        w = 2.0 * G["hanger_r"]
        parts.append((box_along("hanger_proxy", [a, b, (0.0, yb, pz)], w, w, side=(1, 0, 0),
                                chamfer=0.0, bend_rings=2, fillet=G["hanger_fillet"]), None))
        R = G_SPLIT["knuckle_r"]
        parts.append((CS.octa("hanger_knuckle", (0.0, yb, pz), R, pz + R), None))
        mapping = {"body_proxy": "body_", "glass_proxy": "glass_", "roof_proxy": "roof_",
                   "hanger_proxy": "hanger_arm", "hanger_knuckle": "hanger_knuckle"}
        dz = 0.0
    else:
        z0, z1 = joint["grip_z0"], 4.16 + 0.02
        if ENVELOPE:
            # GD_ENVELOPE: the outboard box keeps the 0.08 top; the jaw box (the LOD0 bar's underside
            # 4.44 up to the envelope, 1 cm into the outboard box, that face dropped) passes the wheels
            ui, zj = GD_ENVELOPE["u_in"], G["rope"] + GD_ENVELOPE["z"]
            grip = [(cs2_kit.add_box("grip_proxy", (0.30, 0.30 - ui, 0.20), (0.0, 0.5 * (0.30 + ui), 4.40)),
                     None),
                    (cs2_kit.add_box("grip_proxyjaw", (0.30, ui + 0.05, zj - 4.44),
                                     (0.0, 0.5 * (ui + 0.01 - 0.04), 0.5 * (zj + 4.44)), drop=("+Y",)), None)]
        else:
            grip = [(cs2_kit.add_box("grip_proxy", (0.30, 0.34, 0.20), (0.0, 0.13, 4.40)), None)]
        parts = [(cs2_kit.add_box("carframe_proxy", (1.10, 0.42, 0.12), (0.0, 0.50, 4.22)), None)] + grip + \
                [(cs2_kit.add_box("hanger_yokeproxy", (0.18, 0.19, z1 - z0),
                                  (0.0, yb, 0.5 * (z0 + z1))), None)]
        mapping = {"carframe_proxy": "carframe_", "grip_proxy": "grip_",
                   "hanger_yokeproxy": "hanger_head"}
        dz = -z0
    for o, _ in parts:
        apply_transform(o)
        if outboard == "-Y":
            scale_axes(o, (1.0, -1.0, 1.0))
            o.data.flip_normals()
        if dz:
            o.data.transform(mathutils.Matrix.Translation((0.0, 0.0, dz)))
        o.data.update()
    table = cs2_asset.tag_parts(parts)
    obj = join_parts(parts, f"{name}_LOD2")
    cs2_asset.store_part_table(obj, table)
    cs2_asset.orient_for_net(obj)
    obj[FRAME_PROP] = "net"
    snap_proxy_uvs(obj, lod0, mapping)
    cs2_asset.shade_by_angle(obj, SHADE_ANGLE, weighted=False)
    return obj


def _budget_split_mesh(kind, role, tris, caps):
    """One split mesh: LOD0 / LOD2 = the measured tallies (caps), LOD1 <= 0.48 x LOD0."""
    rows = {"role": role, "LOD0": tris[0], "LOD0_cap": caps[0]}
    ok = tris[0] <= caps[0]
    if 1 in tris:
        rows["LOD1_ratio"] = round(tris[1] / tris[0], 3)
        ok = ok and tris[1] <= 0.48 * tris[0]
    if 2 in tris:
        rows["LOD2"], rows["LOD2_cap"] = tris[2], caps[1]
        ok = ok and tris[2] <= caps[1]
    rows["ok"] = ok
    if not ok:
        raise RuntimeError(f"{kind} split {role} budget broken: {rows}")
    return rows


def _budget_gondola_pair(tris):
    """The pair (body + grip) against the one-piece budget + the pin detail (BRIEF 'Budgets')."""
    pd, pd2 = G_SPLIT["pin_detail"], G_SPLIT["pin_detail_lod2"]
    sd, cr = G_SPLIT["segs_detail"], G_SPLIT["cap_raise"]
    if ENVELOPE:                       # the jaw cut: +12 on 2676 (+2.2 % with the 48), rule <= 5 %
        cr, pd2 = cr + GD_ENVELOPE["tris"], pd2 + GD_ENVELOPE["lod2_tris"]
    t0 = tris[0]
    brief_cap = G["lod0_cap"] + pd
    if cr > 0.05 * brief_cap:
        raise RuntimeError(f"gondola split: cap raise {cr} > 5 % of the brief cap {brief_cap}")
    cap0 = brief_cap + cr
    b0, b1 = G["lod0_band"]
    proj = G["lod0_projection"] + pd + sd
    rows = {"LOD0": t0, "LOD0_cap": cap0, "brief_cap": brief_cap, "cap_raise": cr,
            "LOD0_band": [b0, b1 + pd + cr], "projection": proj,
            "pin_detail": pd, "segs_detail": sd, "pin_detail_lod2": pd2}
    ok = t0 <= cap0 and b0 <= t0 <= b1 + pd + cr and abs(t0 - proj) <= 0.10 * proj
    if 1 in tris:
        rows["LOD1_ratio"] = round(tris[1] / t0, 3)
        ok = ok and tris[1] <= 0.48 * t0
    if 2 in tris:
        rows["LOD2"], rows["LOD2_cap"] = tris[2], G["lod2_cap"] + pd2
        ok = ok and 150 <= tris[2] <= G["lod2_cap"] + pd2
    rows["ok"] = ok
    if not ok:
        raise RuntimeError(f"gondola split pair budget broken: {rows}")
    return rows


def _rewrite_manifest(res, extra):
    """Add `extra` to a finished asset's manifest (the pair results exist only after both meshes)."""
    path = res["manifest"]
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    for k in ("name", "written_utc", "blender", "files"):
        data.pop(k, None)
    data.update(extra)
    res["manifest"] = cs2_validate.write_manifest(res["asset_dir"], res["name"], data)


def run_gondola_split(args):
    """--split: SkiGondolaCabin8B (body: cabin + stalk + knuckle) + SkiGondolaCabin8GripB (grip: head +
    cheeks + pin + springs + carriage + rollers + jaw), each with its base pivot (lowest vertex z 0),
    3 LODs and its own map set. S1 / S2 / S3 on the parts; every G contract on the joined pair at every
    LOD, the role contracts on each mesh."""
    J = G_SPLIT
    shipped = SPLIT_NAMES["gondola"]
    names = (shipped[0], shipped[1][:-1] + GD_ENVELOPE["letter"]) if ENVELOPE else shipped
    nb, ng = (cs2_asset.validate_asset_name(n) for n in names)
    for n in (nb, ng):
        _lint(n)
    catalog = catalog_drift("gondola", args.carriers, outboard=args.outboard)
    clearance = clearance_rows("gondola")
    ingest = {}
    parts = gondola_parts(args, ingest)
    assert_uv_layers(parts, stage="gondola parts")
    tri_old = sum(cs2_asset.tri_count(o) for o, _k in parts)
    body, grip, old, joint = gondola_split_parts(parts, args.outboard)
    s1 = CS.rejoin(old, body + grip, [joint["pin"]], joint["r_x"], joint["old_skip"],
                   joint["new_skip"], label="gondola")
    s23 = CS.sweep_joint(body, grip, [(joint["pin"], joint["knuckles"], joint["cheeks"],
                                       joint["floor"])], label="gondola")
    for o, _k in old[-2:]:
        _drop(o)
    tb = sum(cs2_asset.tri_count(o) for o, _k in body)
    tg = sum(cs2_asset.tri_count(o) for o, _k in grip)
    measured = {"one_piece_lod0": tri_old, "body_lod0": tb, "grip_lod0": tg,
                "pin_detail": tb + tg - tri_old}
    print(f"[split] gondola tallies {measured}")
    if J["pin_detail"] is None or J["body_caps"] is None or J["grip_caps"] is None:
        raise RuntimeError(f"gondola split: set G_SPLIT pin_detail / caps from the measured tallies "
                           f"{measured}")
    if measured["pin_detail"] != J["pin_detail"] or tb != J["body_caps"][0] or tg != J["grip_caps"][0]:
        raise RuntimeError(f"gondola split: tallies {measured} differ from G_SPLIT pin_detail "
                           f"{J['pin_detail']}, body {J['body_caps']}, grip {J['grip_caps']}")
    gz0, gz1 = _world_z(grip)
    bz0, bz1 = _world_z(body)
    pz, R = joint["pz"], J["knuckle_r"]
    if abs(bz0) > 1e-5 or abs(bz1 - (pz + R)) > 1e-5 or abs(gz1 - G["hang"]) > 1e-5 \
            or abs(gz0 - (pz - J["cheek_r"])) > 1e-5:
        raise RuntimeError(f"gondola split: body z {bz0:.5f}..{bz1:.5f} (want 0..{pz + R:.5f}), grip z "
                           f"{gz0:.5f}..{gz1:.5f} (want {pz - J['cheek_r']:.5f}..{G['hang']})")
    joint["grip_z0"] = gz0
    for o, _k in grip:                                   # the grip's base pivot (lowest vertex z 0)
        o.data.transform(mathutils.Matrix.Translation((0.0, 0.0, -gz0)))
        o.data.update()
    pivots = CS.pivot_numbers(joint["rope"], joint["pin"], gz0, 0.0, G["hang"], G["hang"] - G["rope"],
                              outboard_note=f"outboard authoring {args.outboard} = prefab "
                                            f"{OUTBOARD_PREFAB_X[args.outboard]}")
    ob, src_b, table_b = build_lod0(body, nb, G_DENSITY, [])
    og, src_g, table_g = build_lod0(grip, ng, G_DENSITY, [])
    for o in (ob, og):
        lo = min(v.co.z for v in o.data.vertices)
        if abs(lo) > 1e-5:
            raise RuntimeError(f"{o.name}: base pivot broken, lowest z {lo:.6f}")
    remap = {}
    if ENVELOPE:                       # GD_ENVELOPE: the shipped atlas first, then the jaw edit
        src_dir = os.path.abspath(args.remap_from)
        for o, sn in ((ob, shipped[0]), (og, shipped[1])):
            remap[o.name] = dict(gondola_shipped_uvs(o, os.path.join(src_dir, sn, sn + ".fbx")),
                                 dir=os.path.join(src_dir, sn))
        added = gondola_envelope_jaw(og, args.outboard, G["rope"] - gz0)
        print(f"[envelope] {ng}: jaw cut + lowered, +{added} tris; remap "
              f"{ {k: v['worst_vertex_mm'] for k, v in remap.items()} } mm worst")
        if added != GD_ENVELOPE["tris"]:
            raise RuntimeError(f"envelope: the jaw edit added {added} tris (GD_ENVELOPE tris "
                               f"{GD_ENVELOPE['tris']})")
    pair0 = split_pair_copy(ob, og, gz0, nb + "Pair0")
    try:
        pair_rows = {0: gondola_contracts(pair0, 0, args.outboard, single_quads=True)}
        pair_tris = {0: cs2_asset.tri_count(pair0)}
    finally:
        _drop(pair0)
    split_info = {"pair": f"{nb} + {ng}", "body": nb, "grip": ng,
                  "pivots": pivots, "S1_rejoin": s1, "S2_S3_sweep": s23,
                  "tallies": measured, "joint": {k: joint[k] for k in ("pin", "z_cut", "r_arm", "grip_z0")},
                  "joint_design": {k: v for k, v in J.items() if not k.endswith(("caps", "prefixes"))}}
    gcaps = J["grip_caps"]
    if ENVELOPE:
        gcaps = (gcaps[0] + GD_ENVELOPE["tris"], gcaps[1] + GD_ENVELOPE["lod2_tris"])
        split_info["envelope"] = dict(GD_ENVELOPE, replaces=shipped[1])

    results = []
    zb = (0.0, pz + R)
    zg = (0.0, G["hang"] - gz0)

    def c_body(o, level):
        return gondola_contracts(o, level, args.outboard, single_quads=(level == 0), role="body",
                                 z_span=zb)

    def c_grip(o, level):
        return gondola_contracts(o, level, args.outboard, single_quads=False, role="grip",
                                 z_span=(gz0, G["hang"]))

    cfg_b = _gondola_cfg(args, nb, c_body, lambda o, nm: gondola_split_proxy(o, nm, args.outboard,
                                                                              joint, "body"))
    cfg_b.update(role="body", z_range=zb, keep_lods=True, lod0_cap=J["body_caps"][0],
                 lod2_cap=J["body_caps"][1],
                 budget=lambda tr: _budget_split_mesh("gondola", "body", tr, J["body_caps"]),
                 prefab_numbers={"mesh": nb, "role": "body", "pivots": pivots,
                                 "note": "split body: the one-piece prefab numbers (UtilityObject, "
                                         "NetSubObjects) stay with the SkiGondolaCabin8A manifest; the "
                                         "code places the body from the pivots"})
    cfg_g = _gondola_cfg(args, ng, c_grip, lambda o, nm: gondola_split_proxy(o, nm, args.outboard,
                                                                              joint, "grip"))
    cfg_g.update(role="grip", z_range=zg, keep_lods=True, tex=J["grip_tex"], margin=J["grip_margin"],
                 lod0_cap=gcaps[0], lod2_cap=gcaps[1],
                 lod1_delete=G["lod1_delete"] + ("hanger_pin_",), lod1_protect=("grip_", "hanger_"),
                 budget=lambda tr: _budget_split_mesh("gondola", "grip", tr, gcaps),
                 stamps={}, stamp=None, plate=None, logo_view=None, detail_z=0.0,
                 hero=("hanger_", "carframe_", "grip_", "spring_"),
                 ropes=[(0.0, G["rope"] - gz0, HV_ROPE_R, 2.0)], credit=None,
                 prefab_numbers={"mesh": ng, "role": "grip", "pivots": pivots,
                                 "note": "split grip: base pivot = the cheek bottom; the rope point "
                                         "and the pin are relative to it"})
    if ENVELOPE:                       # no bake; the unchanged body goes to _unchanged/ (pair LODs only)
        cfg_b.update(remap=remap[nb], out_root=os.path.join(os.path.abspath(args.out), "_unchanged"))
        cfg_g.update(remap=remap[ng], manifest_extra={"envelope": dict(
            GD_ENVELOPE, remap={k: v for k, v in remap[ng].items() if k != "dir"})})
    for nm, obj, src, cfg, table in ((nb, ob, src_b, cfg_b, table_b), (ng, og, src_g, cfg_g, table_g)):
        res = {"name": nm, "kind": "gondola", "role": cfg["role"], "catalog": catalog,
               "clearance": clearance, "parts": len(table)}
        if obj is ob:
            res["ingest"] = ingest
        results.append(finish_asset(args, cfg, obj, src, res))
    rb, rg = results
    lb, lg = rb.get("lod_objs") or [], rg.get("lod_objs") or []
    try:
        for k, (a, b) in enumerate(zip(lb, lg), start=1):
            pr = split_pair_copy(a, b, gz0, f"{nb}Pair{k}")
            try:
                pair_rows[k] = gondola_contracts(pr, k, args.outboard, single_quads=False)
                pair_tris[k] = cs2_asset.tri_count(pr)
            finally:
                _drop(pr)
    finally:
        for o in lb + lg:
            _drop(o)
    split_info["pair_tris"] = {f"LOD{k}": v for k, v in pair_tris.items()}
    split_info["pair_budget"] = _budget_gondola_pair(pair_tris)
    split_info["pair_contracts_held"] = {f"LOD{k}": [r["code"] for r in rows]
                                         for k, rows in pair_rows.items()}
    for r in results:
        r.pop("lod_objs", None)
        r["split"] = split_info
        r["pair_contracts"] = pair_rows
        _rewrite_manifest(r, {"split": split_info})
    print(f"PASS lift_cabin --split gondola: S1 {s1['max_old_to_new_mm']}/{s1['max_new_to_old_mm']} mm, "
          f"S2 gap {s23['gap_mm_min']}..{s23['gap_mm_max']} mm over {s23['poses']} poses, S3 "
          f"{s23['clash_poses']} clashes, pair tris {pair_tris} (LOD0 cap "
          f"{split_info['pair_budget']['LOD0_cap']}), pivots grip rope "
          f"{pivots['grip_rope_point_m']['prefab']} pin {pivots['grip_pin_center_m']['prefab']}, body pin "
          f"{pivots['body_pin_center_m']['prefab']}")
    return results


#: The tram joint (carrier split 2026-11-03; carrier_split.py header). The one-piece hanger's two fork
#: uprights (y +-0.95) butt against the carriage head's underside (K = hang - 0.12 = the carhead bottom).
#: Split: each upright ends 0.16 under the pin and carries a LUG (the body's eye: a 12 cm plate with a
#: half-round top of radius 0.12 round the pin), the carriage head carries two CHEEK plates per lug (a
#: clevis) with a pin head on each outer face, inner and outer (symmetric, R29 / T5). Both pins are ONE
#: lateral axis (authoring y). The eye cannot sit inside the 10 cm carhead, so the pin moves down
#: by lug_r + c: K = carhead underside - c - lug_r (10.2565 at hang 10.50; was 10.38): a proposed
#: carriers.json pivot change (build report). Roll: two coaxial lugs 1.90 m apart cannot roll about the
#: travel axis (each would move +-0.13 m vertically at 8 deg). Swing convention (carrier_split.py
#: header): the pin sweeps pitch + fore-aft only; the cross-wind sway rolls cabin + carriage together
#: about the track rope (<= CS.TRAM_ROLL_DEG).
T_SPLIT = dict(
    lug_r=0.12, lug_t=0.12, lug_bury=0.03, lug_drop=0.16, lug_segs=8,
    cheek_t=0.03, cheek_r=0.13, cheek_bury=0.02,
    pin_r=0.035, pin_len=0.014,
    r_x=0.30,                                       # S1 joint zone round each lug's pin center
    # critique 2026-11-03: the eight bogie wheel rims 12 -> 16 segments (10 profile strips x 2 tris x
    # segs: 240 -> 320 a rim, +640 on the carriage; LOD1 swaps the rims for 8-segment sheaves, LOD2
    # deletes them). The pair stays under the cap 8000 + 256: no raise.
    wheel_segs=16, segs_detail=640,
    # measured 2026-11-03 (asserted): pin_detail = cabin + carriage LOD0 - the one-piece LOD0 (both with
    # the split's rims): cabin 2830 -> 2926 (two lugs, the uprights' top caps: +96), carriage 2848 + 640
    # (rims) = 3488 -> 3648 (four cheeks, four pin heads: +160)
    pin_detail=256, cabin_lod0=2926, carriage_lod0=3648,
)


def tram_split_dims(dims, car_z0):
    """The split's dims: K at the lug pin, the cabin top over it, the carriage's base pivot."""
    S = T_SPLIT
    k = dims["carhead"][0] - CS.DESIGN_GAP_M - S["lug_r"]
    return dict(dims, k_z=k, k_one_piece=dims["k_z"], cab_top=k + S["lug_r"], car_z0=car_z0,
                z_upright=k - S["lug_drop"])


def tram_split_parts(parts, dims):
    """The joint on the finished tram parts, ASSEMBLED frame. Returns (cabin, carriage, old, joint):
    cabin = everything below the pin (+ the lugs), carriage = T carriage_prefixes (+ the cheeks and pin
    heads), old = the one-piece part list for S1 (copies of the two uprights this replaces)."""
    S = T_SPLIT
    sd = tram_split_dims(dims, 0.0)
    K, z_ut = sd["k_z"], sd["z_upright"]
    c = CS.DESIGN_GAP_M
    dy = T["upright_dy"]
    uw, ud = T["upright_wd"]
    ups = {o.name: (o, k) for o, k in parts if o.name in ("fork_uprightn", "fork_uprightp")}
    if len(ups) != 2:
        raise RuntimeError(f"tram split: uprights {sorted(ups)}")
    old = [(o, k) for o, k in parts if o.name not in ups]
    old += [(_dup(o, "old_" + o.name), k) for o, k in ups.values()]
    keep = [(o, k) for o, k in parts if o.name not in ups]
    for o, _k in ups.values():
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(me)
    r, t_l = S["lug_r"], S["lug_t"]
    zb = z_ut - S["lug_bury"]
    n = S["lug_segs"]
    lug_prof = [(-r, zb), (r, zb)] + [(r * math.cos(math.pi * k / n), K + r * math.sin(math.pi * k / n))
                                      for k in range(n + 1)]
    rc, t = S["cheek_r"], S["cheek_t"]
    ch_prof = CS.plate_profile(-rc, rc, dims["carhead"][0] + S["cheek_bury"], (0.0, K), rc, arc_segs=4)
    yi = dy - t_l / 2.0 - c                    # |y| of the inner cheek's lug-side face
    yo = dy + t_l / 2.0 + c                    # |y| of the outer cheek's lug-side face
    new = []
    joints = []
    for tag, s in (("n", -1.0), ("p", 1.0)):
        up_ = box_along(f"fork_upright{tag}", [(0, s * dy, dims["fork_top"] - 0.02), (0, s * dy, z_ut)],
                        uw, ud, side=(1, 0, 0), chamfer=T["r_upright"], corner_segs=2, caps="end")
        up_["relief"] = "map:seams"
        lug = CS.slab_y(f"fork_lug{tag}", lug_prof, s * dy - t_l / 2.0, s * dy + t_l / 2.0,
                        open_first=True, relief="geo")
        if s > 0:
            ci = CS.slab_y(f"carhead_cheek{tag}i", ch_prof, yi - t, yi, relief="geo")
            co = CS.slab_y(f"carhead_cheek{tag}o", ch_prof, yo, yo + t, relief="geo")
        else:
            ci = CS.slab_y(f"carhead_cheek{tag}i", ch_prof, -yi, -yi + t, relief="geo")
            co = CS.slab_y(f"carhead_cheek{tag}o", ch_prof, -yo - t, -yo, relief="geo")
        po = CS.pin_end(f"pivot_pin{tag}o", s * (yo + t), (0.0, K), S["pin_r"], S["pin_len"], s)
        pi = CS.pin_end(f"pivot_pin{tag}i", s * (yi - t), (0.0, K), S["pin_r"], S["pin_len"], -s)
        new += [(up_, "galv"), (lug, "galv"), (ci, "galv"), (co, "galv"), (po, "galv"), (pi, "galv")]
        joints.append(((0.0, s * dy, K), [f"fork_lug{tag}"], [f"carhead_cheek{tag}i",
                                                               f"carhead_cheek{tag}o"], ["carhead_main"]))
    for o, _k in new:
        apply_transform(o)
    allp = keep + new
    car = [(o, k) for o, k in allp if o.name.startswith(T["carriage_prefixes"])]
    cab = [(o, k) for o, k in allp if not o.name.startswith(T["carriage_prefixes"])]
    joint = {"K": K, "z_upright": z_ut, "joints": joints, "pins": [j[0] for j in joints],
             "new_skip": ("fork_lug", "carhead_cheek", "pivot_pin"), "r_x": S["r_x"],
             "rope": (0.0, 0.0, dims["rope"])}
    return cab, car, old, joint


def run_tram(args):
    dims = tram_dims(args.hang)
    if args.split:
        names = [cs2_asset.validate_asset_name(n) for n in SPLIT_NAMES["tram"]]
    else:
        names = [cs2_asset.validate_asset_name(args.name or NAMES["tram"])]
    for n in names:
        _lint(n)
    catalog = catalog_drift("tram", args.carriers, dims["hang"])
    clearance = clearance_rows("tram", dims)
    parts = tram_parts(args, {}, dims)
    base_rulings = ["R27 hang 10.50 / rope 10.00", "R28 one HV track rope per side",
                    "R29 symmetric, identity, pivot floor underside", "R30 half-width 1.10 + tower "
                    "keep-outs", "R31 lanes", "R8 painted glass", "R18 2048"]
    summary = {"hang_m": dims["hang"], "track_rope_m": dims["rope"], "haul_rope_m": dims["haul_z"],
               "haul_dy_m": T["haul_dy"], "wheel_centre_m": round(dims["wheel_z"], 4),
               "fin_top_m": dims["fin_top"], "fork_m": [dims["fork_bot"], dims["fork_top"]],
               "k_pin_m": dims["k_z"], "half_width_max_m": T["half_w_max"]}
    jobs = []
    split_info = None
    if not args.split:
        jobs.append(("whole", names[0], parts, T["tex"]))
    else:
        S = T_SPLIT
        tri_old = sum(cs2_asset.tri_count(o) for o, _k in parts)
        cab, car, old, joint = tram_split_parts(parts, dims)
        s1 = CS.rejoin(old, cab + car, joint["pins"], joint["r_x"], (), joint["new_skip"], label="tram")
        # pitch + fore-aft about the pin (the swing convention, T_SPLIT header)
        s23 = CS.sweep_joint(cab, car, joint["joints"], label="tram")
        for o, _k in old[-2:]:
            _drop(o)
        tc = sum(cs2_asset.tri_count(o) for o, _k in cab)
        tk = sum(cs2_asset.tri_count(o) for o, _k in car)
        measured = {"one_piece_lod0": tri_old, "cabin_lod0": tc, "carriage_lod0": tk,
                    "pin_detail": tc + tk - tri_old}
        print(f"[split] tram tallies {measured}")
        if abs(args.hang - CARRIERS["tram"]["hang_m"]) < 1e-9:     # the shipping hang: asserted
            if S["pin_detail"] is None or S["cabin_lod0"] is None or S["carriage_lod0"] is None:
                raise RuntimeError(f"tram split: set T_SPLIT pin_detail / tallies from {measured}")
            if (measured["pin_detail"], tc, tk) != (S["pin_detail"], S["cabin_lod0"], S["carriage_lod0"]):
                raise RuntimeError(f"tram split: tallies {measured} differ from T_SPLIT")
        car_z0, car_z1 = _world_z(car)
        cab_z0, cab_z1 = _world_z(cab)
        sd = tram_split_dims(dims, car_z0)
        if abs(cab_z0) > 1e-5 or abs(cab_z1 - sd["cab_top"]) > 1e-5 or abs(car_z1 - dims["hang"]) > 1e-5:
            raise RuntimeError(f"tram split: cabin z {cab_z0:.5f}..{cab_z1:.5f} (want 0..{sd['cab_top']:.5f}),"
                               f" carriage top {car_z1:.5f} (want {dims['hang']})")
        for o, _k in car:                                  # the carriage's base pivot (lowest vertex z 0)
            o.data.transform(mathutils.Matrix.Translation((0.0, 0.0, -car_z0)))
            o.data.update()
        pivots = CS.pivot_numbers(joint["rope"], (0.0, 0.0, sd["k_z"]), car_z0, 0.0, dims["hang"],
                                  dims["hang"] - dims["rope"],
                                  outboard_note="symmetric (R29): the pin axis runs through both lugs")
        pivots["pin_lugs_lateral_m"] = [-T["upright_dy"], T["upright_dy"]]
        pivots["grip_haul_points_m"] = {"authoring": [[0.0, sy * T["haul_dy"],
                                                       round(dims["haul_z"] - car_z0, 4)]
                                                      for sy in (-1, 1)]}
        pivots["k_one_piece_m"] = dims["k_z"]
        split_info = {"pair": f"{names[0]} + {names[1]}", "body": names[0], "grip": names[1],
                      "pivots": pivots, "S1_rejoin": s1, "S2_S3_sweep": s23,
                      "roll": f"never at the pin (two coaxial lugs 1.90 m apart: each would move +-0.13 m "
                              f"vertically at 8 deg); the sway rolls cabin + carriage together about the "
                              f"track rope, <= {CS.TRAM_ROLL_DEG:g} deg",
                      "tallies": measured, "K_m": round(sd["k_z"], 4),
                      "K_one_piece_m": dims["k_z"], "car_z0_m": round(car_z0, 4),
                      "joint_design": dict(S)}
        dims = sd
        summary["k_pin_m"] = round(sd["k_z"], 4)
        summary["k_one_piece_m"] = sd["k_one_piece"]
        jobs.append(("cabin", names[0], cab, T["tex"]))
        jobs.append(("carriage", names[1], car, T["split_tex"]))
    results = []
    for role, name, plist, tex in jobs:
        result = {"name": name, "kind": "tram", "role": role, "catalog": catalog,
                  "clearance": clearance}
        result["uv_layers_before_sources"] = assert_uv_layers(plist, stage=f"tram {role} parts")
        table = []
        obj, sources, table = build_lod0(plist, name, T_DENSITY, table)
        result["parts"] = len(table)
        # the split carriage: top contract only on the LODs (they lose the haul clamps, the lowest
        # parts, so pinning a bottom would drag the plates down); LOD0's base pivot is T9 / build_lod0
        z_range = {"whole": (0.0, dims["hang"]), "cabin": (0.0, dims.get("cab_top", dims["k_z"])),
                   "carriage": (None, dims["hang"] - dims.get("car_z0", dims["k_z"]))}[role]

        def contracts(o, level, _role=role):
            return tram_contracts(o, level, dims, _role)

        def lod2(o, nm, _role=role):
            dele = T["lod2_delete"]
            # split 2026-11-03: the cabin's protected lugs (76 tris) come on top of the old 300, else the
            # collapse leaves the body ~30 tris (T3b / T5 / T8 broke); the carriage ships its 220
            # protected tris (carhead + cheeks + beams) either way
            target = {"whole": T["lod2_target"], "cabin": 380, "carriage": 250}[_role]
            return make_lod(o, nm, 2, target, dele, T["lod2_protect"])

        # all four logo quads share ONE pinned square (stamp_layout): paint it once
        # Revision 2: six logo quads share the square; the painter takes exactly one quad
        stamps = {} if role == "carriage" else {"logo_sidep0": (0.70, 0.70)}
        stamp = None if role == "carriage" else {
            "logo": ("logo_",), "fill": ("band_", "doorband_"), "logo_frac": T["logo_frac"],
            "fill_cap_ppm": 260.0,
            # the black window frames, sills, reveals and mullions: hero faces, pinned
            # (Revision 2: + the door leaves' glazed frames and the runner boxes, same paint)
            "groups": ({"prefixes": ("frame_", "sill_", "reveal_", "doorframe_", "doorheader_"),
                        "ppm": 130.0},
                       # Revision 2: the trim line (body + door leaves) and every rubber part
                       # (gaskets, nosings, jambs, pockets, rollers, bumpers) are constants:
                       # pinned stripes instead of ~80 islands in the margin-bound galv pack
                       {"prefixes": ("trim_", "doortrim_"), "ppm": 140.0},
                       {"prefixes": ("doorgasket_", "doorseal_", "doorjamb_", "doorpocket_",
                                     "doorroller_", "bumper_"), "ppm": 50.0},
                       {"prefixes": ("roof_", "under_", "chamfer_", "mullion_"), "ppm": 62.0},
                       {"prefixes": ("glass_", "doorglass_"), "ppm": 40.0})}
        proj = T["lod0_projection"] if role == "whole" else T["split_projection"][role]
        obj["cabin_role"] = role

        def lod1(o, nm, level, target, dele, prot, _role=role):
            if _role == "cabin":
                return make_lod(o, nm, level, target, dele, prot)
            return tram_lod1(o, nm, dims, dele, prot, target)

        cfg = dict(lod1=lod1, kind="tram", role=role, name=name, tex=tex, ao=T["ao"], margin=T["margin"],
                   contracts=contracts, lod2=lod2, lod1_ratio_target=0.46,
                   lod1_delete=T["lod1_delete"], lod1_protect=T["lod1_protect"],
                   z_range=z_range,
                   budget=lambda tr, _p=proj: _budget_tram(tr, _p),
                   lod0_cap=T["lod0_cap"], lod2_cap=T["lod2_cap"], stamps=stamps, plate=None,
                   stamp=stamp,
                   logo_view=None if role == "carriage" else
                   ((-2.50, 1.0, -0.01), (2.50, 2.0, 1.45), (1.0, -0.2, 0.25)),
                   hero=("band_", "doorband_", "trim_", "doortrim_", "frame_", "doorframe_",
                         "sill_", "reveal_", "chamfer_", "roof_", "logo_", "hanger_", "fork_",
                         "carhead_", "carbeam_", "bogie_", "doorgrab_"),
                   detail=tram_detail, sets=T_SETS, detail_z=dims["rope"] - 1.5 - (
                       dims.get("car_z0", dims["k_z"]) if role == "carriage" else 0.0),
                   ropes=_tram_ropes(dims, role), dims=dims,
                   contract_summary=dict(summary, role=role), rulings=base_rulings)
        if split_info is not None:
            cfg.update(keep_lods=True, prefab_numbers={
                "mesh": name, "role": role, "pivots": split_info["pivots"],
                "note": "split mesh: the one-piece prefab numbers (UtilityObject, hosted poses) stay "
                        "with the SkiTramCabinA manifest; the code places the mesh from the pivots"})
        results.append(finish_asset(args, cfg, obj, sources, result))
    if split_info is not None:
        _tram_split_pair(results, split_info, tram_dims(args.hang), dims["car_z0"])
    return results


def _tram_split_pair(results, split_info, dims_one, car_z0):
    """Every T contract on the joined cabin + carriage at every LOD, the pair budget, the manifests."""
    rc, rk = results
    pair_rows, pair_tris = {}, {}
    objs = {0: (bpy.data.objects[rc["name"]], bpy.data.objects[rk["name"]])}
    lc, lk = rc.get("lod_objs") or [], rk.get("lod_objs") or []
    for k, (a, b) in enumerate(zip(lc, lk), start=1):
        objs[k] = (a, b)
    try:
        for k, (a, b) in sorted(objs.items()):
            pr = split_pair_copy(a, b, car_z0, f"{rc['name']}Pair{k}")
            try:
                pair_rows[k] = tram_contracts(pr, k, dims_one, "whole")
                pair_tris[k] = cs2_asset.tri_count(pr)
            finally:
                _drop(pr)
    finally:
        for o in lc + lk:
            _drop(o)
    pd, sd = T_SPLIT["pin_detail"] or 0, T_SPLIT["segs_detail"]
    cap0, proj = T["lod0_cap"] + pd, T["lod0_projection"] + pd + sd
    budget = {"LOD0": pair_tris[0], "LOD0_cap": cap0, "projection": proj, "pin_detail": pd,
              "segs_detail": sd}
    ok = pair_tris[0] <= cap0 and abs(pair_tris[0] - proj) <= 0.10 * proj
    if 1 in pair_tris:
        budget["LOD1_ratio"] = round(pair_tris[1] / pair_tris[0], 3)
        ok = ok and pair_tris[1] <= 0.48 * pair_tris[0]
    if 2 in pair_tris:
        budget["LOD2"] = pair_tris[2]
    budget["ok"] = ok
    if not ok:
        raise RuntimeError(f"tram split pair budget broken: {budget}")
    split_info["pair_tris"] = {f"LOD{k}": v for k, v in pair_tris.items()}
    split_info["pair_budget"] = budget
    split_info["pair_contracts_held"] = {f"LOD{k}": [r["code"] for r in rows]
                                         for k, rows in pair_rows.items()}
    for r in results:
        r.pop("lod_objs", None)
        r["split"] = split_info
        r["pair_contracts"] = pair_rows
        _rewrite_manifest(r, {"split": split_info})
    s1, s23, pv = split_info["S1_rejoin"], split_info["S2_S3_sweep"], split_info["pivots"]
    print(f"PASS lift_cabin --split tram: S1 {s1['max_old_to_new_mm']}/{s1['max_new_to_old_mm']} mm, "
          f"S2 gap {s23['gap_mm_min']}..{s23['gap_mm_max']} mm over {s23['poses']} poses (pitch + fore-aft "
          f"at the pin), "
          f"S3 {s23['clash_poses']} clashes, pair tris {pair_tris} (LOD0 cap {cap0}), K "
          f"{split_info['K_m']} (one-piece {split_info['K_one_piece_m']}), pivots grip rope "
          f"{pv['grip_rope_point_m']['prefab']} pin {pv['grip_pin_center_m']['prefab']}, body pin "
          f"{pv['body_pin_center_m']['prefab']}")


def _tram_ropes(dims, role):
    off = dims.get("car_z0", dims["k_z"]) if role == "carriage" else 0.0
    return [(0.0, dims["rope"] - off, HV_ROPE_R, 4.5),
            (T["haul_dy"], dims["haul_z"] - off, LV_ROPE_R, 4.5),
            (-T["haul_dy"], dims["haul_z"] - off, LV_ROPE_R, 4.5)]


# ======================================================================================
# CLI
# ======================================================================================

def parse_args(argv):
    ap = argparse.ArgumentParser(prog="lift_cabin.py")
    ap.add_argument("--kind", required=True, choices=("gondola", "tram"))
    ap.add_argument("--out", default=None,
                    help="letter folder (with --export; default cabins/A, cabins/B with --split)")
    ap.add_argument("--export", action="store_true",
                    help="write the asset folder under --out (else a temp draft folder)")
    ap.add_argument("--name", default=None)
    ap.add_argument("--source", default=DEFAULT_SOURCE, help="gondola.fbx (gondola only)")
    ap.add_argument("--carriers", default=DEFAULT_CARRIERS)
    q = ap.add_mutually_exclusive_group()
    q.add_argument("--finish", action="store_true", help="full bake (1024/2048, AO 32/48, relief)")
    q.add_argument("--draft", action="store_true", help="draft bake (256, AO 8, no relief) - default")
    ap.add_argument("--lods", action="store_true")
    ap.add_argument("--verify", action="store_true")
    ap.add_argument("--preview", nargs="?", const=DEFAULT_PREVIEW, default=None, metavar="DIR")
    ap.add_argument("--review", nargs="?", const="", default=None, metavar="DIR")
    ap.add_argument("--plate-number", default="27")
    ap.add_argument("--outboard", default=None, choices=("+Y", "-Y"),
                    help="gondola stalk / door side (default GD_OUTBOARD -Y, nitpick A3; +Y = the "
                         "approved revision 1b cabin)")
    ap.add_argument("--lod2", default="proxy", choices=("proxy", "decimate"),
                    help="gondola LOD2: the authored proxy (C6, default) or the decimate A/B, "
                         "which FAILS G3/G6 by measurement (protected contract parts ~244 tris "
                         "> the ~200 budget, the shell collapses) - kept to re-measure C6")
    ap.add_argument("--hang", type=float, default=CARRIERS["tram"]["hang_m"])
    ap.add_argument("--split", action="store_true",
                    help="carrier split (2026-11-03): body + grip meshes, letter B (both kinds)")
    ap.add_argument("--envelope", action="store_true",
                    help="with --split (gondola): the B2 v2 grip passage (GD_ENVELOPE): grip letter C, "
                         "the shipped B atlas remapped, no bake")
    ap.add_argument("--remap-from", default=DEFAULT_SPLIT_OUT,
                    help="--envelope: the letter folder holding the shipped split meshes and maps")
    ap.add_argument("--ab", default=DEFAULT_AB)
    ap.add_argument("--no-validate", action="store_true")
    args = ap.parse_args(argv)
    if args.outboard is None:          # nitpick A3: the gondola ships -Y; the tram is symmetric (R29)
        args.outboard = GD_OUTBOARD if args.kind == "gondola" else "+Y"
    if args.out is None:
        args.out = DEFAULT_SPLIT_OUT if args.split else DEFAULT_OUT
    if args.kind == "gondola" and abs(args.hang - CARRIERS["tram"]["hang_m"]) > 1e-9:
        ap.error("--hang is a tram option")
    if args.kind == "gondola" and args.split and (args.lod2 != "proxy" or args.name):
        ap.error("--split builds the SPLIT_NAMES pair with the proxy LOD2 (no --name / --lod2 decimate)")
    if args.kind == "tram" and (args.outboard != "+Y" or args.lod2 != "proxy"):
        ap.error("--outboard / --lod2 are gondola options (the tram is symmetric, R29)")
    if args.envelope and (args.kind != "gondola" or not args.split):
        ap.error("--envelope is a gondola --split option")
    return args


def run(argv):
    """Build one kind; returns a list of per-asset result dicts. Clears the scene first."""
    global ENVELOPE
    args = parse_args(argv)
    ENVELOPE = bool(args.envelope)
    if not bpy.app.background:
        raise RuntimeError("lift_cabin runs headless only (ROUND-SPEC R10)")
    clear_scene()
    if args.kind == "gondola":
        res = run_gondola(args)
    else:
        res = run_tram(args)
    for r in res:
        print(f"PASS lift_cabin: {r['name']} ({r['kind']}/{r.get('role', 'whole')}) tris "
              f"{r['tris']}, {r['quality']} {r['texture_px']} px, contracts held on "
              f"{sorted(r['contracts'])} -> {r['asset_dir']}")
    return res


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    run(argv)


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
