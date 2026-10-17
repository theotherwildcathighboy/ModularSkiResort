"""
recipes/school.py - the ski school (Palisades board-and-batten + lantern tower) as a
lodge.py recipe: `lodge.py --school` -> SkiSchoolCommercialLowA (buildings/A).

Contract: docs/research/art/assets-2026-10/design/civic-sign.md section 2 with
civic-sign-skeptic.md applied (skeptic wins) and ROUND-SPEC R21-R25 (kit API wins, R25):
    M4  the tower is CUT cleanly through the wings: the ground-floor (GF) and upper-floor
        (UF) walls are boxes / quads with the tower footprint left out, the GF / UF roofs
        stop at the tower, the tower walls start where they become exposed (front from
        grade, sides above the GF roof strip / UF roof, back above the UF roof), the white
        bands stop at the tower corner boards; G10 (no same-facing coplanar overlap between
        parts, no face centroid inside a massing volume) is asserted in the builder, so a
        wrong build FAILS.
    _Win cells: store (1,1)=6, upper (3,1)=8, tower (2,2)=12, back (4,3)=19 (4 cells).
Kit adaptations (R25): materials from cs2_bldg.BLDG_MATERIALS (siding_paint slot 1 for the
barn-red board-and-batten, trim_white bands / frames / doors, metal_roof slot 3 for the
tower and lantern roofs, concrete roofs); prefixes follow the kit LOD tables (LOD1/LOD2
deletes + lod1_extra / lod2_extra / lod2_keep below); ground parts never deleted (kit
assert), so steps, cones and pennant poles are closed parts from z 0.02.
Pictogram: the field `emblem_school_face` (siding_paint, coplanar in the tower front grid)
gets ski_launcher_glyph.cand_d (skier on a slope) white at 0.85 of the field through
_civic.GlyphPainter, which reads the face's UV -> world frame at bake time (N5).

Authoring frame: +X = road / arrival (game +Z), origin = lot centre at grade. No bpy at
import (recipe modules must be import-safe): geometry is built in build(recipe, opts), which
loads recipes/_civic.py lazily.

Art pass, detail pass 1 (2026-10-11; design/detail-pass-1.md 3.1 with the ten MUST-CHANGE items
of design/detail-pass-1-skeptic.md): word / pictogram boards on their own label quads (SKI SCHOOL
name board, meeting / bib / notice boards), 4 lesson-meeting post signs, a lift-line maze, ski
racks, a flag, yellow nosing stripes, belfry glow, sign lamps, the rubber magic-carpet belt;
vanilla sub-objects (Recipe.sub_objects), weathering (Recipe.weather).

Fine-detail pass, step 2 (2026-10-12; build/fine-detail-step2-engine.md): the school is the
REFERENCE HOST: RECIPE is a fine_detail.FineRecipe (fine_detail=True, sign_set=SIGN_SET;
`lodge.py --no-fine` rebuilds this revision without the pass from `legacy`): kit fine geometry
(maze posts, flag masts, lesson poles), vanilla rows for the cones and entry lanterns, kit
racks, and the sign set SkiSignSetSchoolB (the OWNER of the shared building sheet, cs2_signset).

REVISION 2 (2026-10-12, Richard's twentieth look; build/school-rev2.md; 9 reference photos:
the original "palisades ski school.jpg" + 8 Google Street View screenshots, 12:07-12:10):
    DOORS   the door numbering is gone (no plates "1"-"4"); the RED MARKINGS of the photos are
            reproduced: a red placard with a white legend on the upper half of the two white
            doors (wingl = the left-wing door, main = the tower door), a red NO SLEDDING sign
            above the back door (svc; 120856 shows it beside a back door), and a red fire-alarm
            bell high on the wall above-left of the tower door (original photo). The placards
            are sign-set faces (cs2_signset kinds school.markstaff / school.marknosled, vector
            font); the bell is modelled (fire_red, LOD0 only).
    ROOF    the UF roof is a MONOPITCH (shed) roof rising from the road side to the hill side
            at ROOF_PITCH_DEG 13.5 (measured on 121022, report section 1.2): front eave at the
            old band (top 6.75 at the UF front wall), back wall 8.49 m to the soffit, a 1.80 m
            back overhang carried by 7 diagonal glulam braces (120838 / 120856 / 120905), a
            0.60 m front overhang with outrigger ends, 0.40 m rakes, white fascia, timber
            soffits. The tower, the UF walls and the corner boards follow the slope; the back
            UF windows are the photos' stacked tall windows (0.95 x 2.40, 2 x 2 lights); the two
            back downspouts went (the back is the HIGH side).
    LOT     4 x 5 (was 4 x 2): the building keeps its own frame (BF, the constants below) and
            moves OX = +12 m toward the road (its front stays 4 m from the lot edge); the yard
            behind it (LOT frame) holds the learning area.
    CARPETS both in the BACK, 20.5 m apart, at a FIXED 12 % grade (6.8 deg; Sunkid rates its
            beginner surface lift "beginners max. 15%"), each on a closed-sided ramp (timber
            skirt from grade, open bottom = an engine-base ground part) rising from the loading
            end (belt 0.20 m up, loading ramp + yellow nosing) to a 2.0 m top platform at the
            lot's back edge (x -19.75), where the player's sculpted learning hill continues:
            A = open carpet (left, y -10.0, 12 m belt, rise 1.44 m), B = the covered BUBBLE
            carpet (right, y +10.5, 16 m belt, rise 1.92 m) under a gallery of plumb arched
            frames every 2.0 m with frosted panels (an opaque baked panel: our pipeline has no
            verified see-through glass) and dark tinted side panels. The maze sits at A's
            loading end (the approved maze, mirrored onto the new carpet), the cones are a
            slalom in the learning area, pennants mark its corners, the lesson signs face the
            back door, the corral fences line the area, the corral floodlights moved to the
            back wall, two kit e-stops stand at the loading ends.
Row frame: every row is game (gx, gy, gz) = authoring (y, z, x) of the LOT frame, gx NOT negated
(fine_detail.X_CONVENTION; probe X2 decides, S1b mirrors on branch N). Building rows are written
in BF and shifted by OX (_bf).
"""

import importlib.util
import json
import math
import os
import sys
import zlib

_HERE = os.path.dirname(os.path.abspath(__file__))
_BLENDER = os.path.dirname(_HERE)
if _BLENDER not in sys.path:
    sys.path.insert(0, _BLENDER)
from bldg_recipes import Budget, Recipe, PAD_RGB   # noqa: E402,F401
from fine_detail import FineRecipe, SignSetSpec, kit_row   # noqa: E402


def load_civic():
    name = "bldg_recipe_civic_helpers"
    if name in sys.modules:
        return sys.modules[name]
    spec = importlib.util.spec_from_file_location(name, os.path.join(_HERE, "_civic.py"))
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


# ======================================================================================
# Constants. BUILDING FRAME (BF): authoring metres of the building as approved (contract
# 2.2); the lot frame = BF + (OX, 0, 0). Yard constants (marked LOT) are in the lot frame.
# ======================================================================================

LOT = (4, 5)                         # (W, D) cells: Y +-16, X +-20 (revision 2; was (4, 2))
HALF_X, HALF_Y = 4.0 * LOT[1], 4.0 * LOT[0]
OX = 12.0                            # BF -> lot frame (+X, toward the road)
GF = (-7.0, 4.0, -8.0, 15.0)         # x0, x1, y0, y1 (BF)
UF = (-7.0, 1.5, -8.0, 15.0)
TW = (-1.0, 4.0, -1.5, 3.5)          # tower, front flush with the GF front
GF_TOP, UF_TOP = 3.30, 6.60          # UF_TOP: the old flat-roof wall top (check volume only)
ROOF_GF_Z, ROOF_UF_Z = 3.45, 6.75    # GF flat roof top; UF roof top AT THE UF FRONT WALL
BAND_GF, BAND_UF = (3.15, 3.45), (6.45, 6.75)
BAND_PROUD = 0.06
GRADE_H, GRADE_PROUD = 0.30, 0.02
CORNER = 0.15                        # white corner boards
# revision 2 roof: monopitch, low at the road (+X), high at the hill (-X)
ROOF_PITCH_DEG = 13.5                # measured, build/school-rev2.md 1.2 (121022, 120943, 120817)
ROOF_T = math.tan(math.radians(ROOF_PITCH_DEG))
ROOF_TH = 0.30                       # slab / fascia depth (vertical)
OV_FRONT, OV_BACK, OV_RAKE = 0.60, 1.80, 0.40
BRACE_Y = (-7.6, -4.1, -0.3, 3.5, 7.3, 11.1, 14.6)   # back glulam braces (mid-bays + ends)
BRACE_FOOT_Z, BRACE_OUT = 3.75, 1.45                 # strut foot on the back wall, strut reach
STUB_Y = (-7.6, -5.75, -3.0, 3.9, 6.25, 8.75, 11.25, 13.75)   # front outrigger ends


def roof_top(x):
    """UF roof top z at BF x (rises toward -X)."""
    return ROOF_UF_Z + (UF[1] - x) * ROOF_T


def roof_bot(x):
    return roof_top(x) - ROOF_TH


# tower roof: hip 18 deg, overhang 0.70, truncated at a 3.0 x 3.0 deck at z 11.05,
# white fascia 0.25; the tower walls stop at the soffit
TOWER_PITCH, TOWER_OVER, TOWER_FASCIA = 18.0, 0.70, 0.25
DECK_Z0, DECK_Z1, DECK_HALF = 11.05, 11.20, 1.50
_TAN = 0.3249196962329063            # tan(18 deg)
TOWER_EAVE_TOP = DECK_Z0 - (2.5 + TOWER_OVER - DECK_HALF) * _TAN
TOWER_WALL_TOP = TOWER_EAVE_TOP - TOWER_FASCIA
# lantern (contract 2.2)
POST_TOP, POST_W = 13.40, 0.22
RAIL_TOP = DECK_Z1 + 0.35
HEAD_Z = (13.15, 13.40)
CORNICE = (13.40, 13.58, 1.70)       # z0, z1, half size
LANTERN_PITCH = 25.0
SPIRE_TOP = 15.00
BELL = (12.30, 13.00, 0.45, 0.18)    # z0, z1, r bottom, r top
GLOW_Z = 13.10
# pictogram field (tower front, 3.2 m square)
FIELD = (-0.6, 2.6, 4.6, 7.8)        # y0, y1, z0, z1
FIELD_FRAC = 0.85
TC = (0.5 * (TW[0] + TW[1]), 0.5 * (TW[2] + TW[3]))    # tower centre (1.5, 1.0)

# windows (contract 2.2): WinSpec(w, h, sill, lights, rows); u along the wall
WIN6 = (0.95, 1.15)                  # 6-light 3 x 2
STOREFRONTS = ((4.5, 8.5, 4), (10.0, 14.0, 4), (-7.4, -4.4, 3))   # +X face, sill 0.45 head 2.85
CORRAL_WIN = (-3.0, -0.5)            # -Y face (u = x), sill 0.60 head 2.40
UF_FRONT_Y = (-7.0, -4.5, 5.0, 7.5, 10.0, 12.5)     # +X face at x 1.5, sill 4.30
BACK_Y = (-6.0, -2.2, 1.6, 5.4, 9.2, 13.0)          # -X face, GF sill 0.90 / UF sill 4.30
BACK_UF_WIN = (0.95, 2.40, 4.30, 2, 2)               # rev 2: the photos' stacked tall windows
SIDE_N = ((-2.0, 0.90), (-4.5, 4.30), (-1.0, 4.30))  # +Y face (y 15): (x, sill)
SIDE_S = ((-4.5, 4.30), (-1.0, 4.30))                # -Y face (y -8): UF only
DOORS = (("main", "+X", 4.0, 1.0, 1.0, 2.2), ("wingr", "+X", 4.0, 9.25, 1.0, 2.2),
         ("wingl", "+X", 4.0, -3.5, 1.0, 2.2), ("svc", "-X", -7.0, -4.1, 0.9, 2.1))
STEP = (0.02, 0.17, 0.60)            # z0, z1, depth (closed, not ground: deleted at LOD1)
STEP_SIDE = 0.25                     # step runs past the door opening each side (= band gap)
HVAC = ((-4.0, 6.0), (-4.0, 11.0))

# ---- revision 2: the red markings (photos; build/school-rev2.md section 1) ----------------
LEAF_PROUD = 0.02                    # cs2_kit.door leaf face (0.02 proud of the wall)
MARK_PROUD = 0.004                   # placard 4 mm proud of the leaf; the wall sign 6 mm
MARK_STAFF = (0.44, 0.36)            # cs2_signset school.markstaff (white "STAFF / ONLY" on red)
MARK_NOSLED = (0.76, 0.36)           # cs2_signset school.marknosled ("NO / SLEDDING", white rim)
MARK_DOORS = (("markL", "wingl"), ("markM", "main"))     # placards on these door leaves
MARK_CZ = 1.45                       # placard centre above the leaf bottom (photo: ~1.4 m)
NOSLED_Z0 = 2.40                     # bottom of the NO SLEDDING sign over the svc door
ALARM_BELL = (-0.30, 2.75, 0.11, 0.07)   # (y, z, r, depth) on the tower front, above-left of main

# ---- revision 2: the yard (LOT frame) -------------------------------------------------
GRADE = 0.12                         # 12 % = 6.84 deg, both carpets
BELT_W, BELT_Z0 = 0.80, 0.20         # belt width; belt surface height at the loading end
RAMP_L, DECK_L, COVER_H = 0.60, 2.00, 0.08
# carpets: (tag, centre y, loading-end x, belt length, side cover width, top deck width, bubble)
CARPETS = (("a", -10.0, -5.75, 12.0, 0.20, 2.40, False),
           ("b", 10.5, -1.75, 16.0, 0.35, 2.60, True))
GALLERY = (0.72, 1.20, 0.80, 2.0)    # half width, side height, arc rise, frame spacing (bubble)
RIB_W, RIB_OUT, STILE_D, PANEL_T = 0.10, 0.02, 0.04, 0.012
# the approved maze (detail pass 1), mirrored onto carpet A's loading end: lanes along X beside
# the carpet, the exit gap (x -5.15..-5.75) onto the loading ramp
MAZE_SEGS = (((-5.15, -12.5), (-8.15, -12.5)), ((-8.15, -12.5), (-8.15, -10.9)),
             ((-5.75, -10.9), (-8.15, -10.9)), ((-5.15, -11.7), (-7.55, -11.7)))
MAZE_POST = 2.0
LESSON_Y = (-2.6, -1.4, -0.2, 1.0)   # S5 post signs at x LESSON_X, facing +X (the back door)
LESSON_X, LESSON_TOP, LESSON_SIZE = 1.0, 2.6, 0.6
CONES = ((-7.0, -1.0), (-9.0, 1.0), (-11.0, -1.0), (-13.0, 1.0), (-15.0, -1.0), (-17.0, 1.0))
CONE_R, CONE_H = 0.15, 0.45
PENNANTS = ((-4.4, -7.6), (-0.8, 8.2), (-18.8, -7.6), (-18.8, 8.2))
ESTOPS = ((-4.60, -11.25), (-0.80, 11.85))   # kit e-stops beside the loading ramps (x, y)


def _carpet_z(c, x):
    """Belt surface z at lot x of carpet c (GRADE from BELT_Z0 at the loading end)."""
    _t, _y, xl, _L, _cv, _wd, _b = c
    return BELT_Z0 + GRADE * (xl - x)


# Vanilla sub-objects for the hand-made SkiSchool prefab (contract 2.7, game local
# coordinates (gx, gy, gz) = authoring (y, z, x) of the LOT frame; GUIDs inferred from the
# shared SpawnableObject placeholder of each variant pair, civicsign/guids.py). Facing (local
# +Z out of the wall / the bench front, game frame): authoring +X -> identity, -X -> Y180,
# +Y -> Y90, -Y -> Y-90. m_ParentMesh: -1 on the lot, 0 = on the main mesh (wall lights,
# vents). Benches / bins >= 0.3 m clear of every mesh part (D10, vanilla render bounds read
# from the dump); wall lights over the three secondary doors are WallLightShortRange (S7).
# Revision 2: the corral moved to the back yard (the fences line the learning area, the snowman
# stands in it), the two corral floodlights light it from the back wall (between a window and
# a brace, z 3.70 over the GF band), the AC vents moved 0.9 m off the brace lines.
Y90 = (0.0, 0.7071068, 0.0, 0.7071068)
YM90 = (0.0, -0.7071068, 0.0, 0.7071068)
Y180 = (0.0, 1.0, 0.0, 0.0)
QID = (0.0, 0.0, 0.0, 1.0)
FENCE_4M = "68c87b3894d5ed74d8180dd43c12da30"      # FenceResidentialPieceLowRandom01
FENCE_2M = "ed041afbdfcc3db4188ce264b34304a2"      # FenceResidentialPieceLowShortRandom01
SNOWMAN = "a4fdfd0ffe3c5fa4bb7cc6522ef81d01"        # SnowmanRandom01 (m_RequireSnow)
BENCH = "8df0703be09426948b5bc3262d86bc04"          # GardenBenchRandom01 (ActivityLocation)
BIN = "41e3c9ba6e1d6b948a6b1bc3499a5397"            # TrashbinRandom01
WALL_SHORT = "cfc12fde01d9da24b86ddedd657eaae1"     # WallLightShortRangeRandom01 (StreetLight)
WALL_LIGHT = "96d63256a8b9f4942b1949c7067e6613"     # WallLightRandom01 (StreetLight, mid range)
AC_VENT = "b99325ff4ff4dfd459734d8f765c2597"        # ACVentWallRandom01


def _so(guid, prefab, pos, rot, parent):
    return {"m_Object": guid, "prefab": prefab, "position": pos, "rotation": rot,
            "m_ParentMesh": parent}


def _bf(pos):
    """A game-local row position written in the BUILDING frame -> the lot frame (gz += OX)."""
    gx, gy, gz = pos
    return (gx, gy, gz + OX)


SUB_OBJECTS = (
    # the corral fences, now lining the learning area (LOT): left line y -13.6, right y 12.6
    _so(FENCE_4M, "FenceResidentialPieceLowRandom01", (-13.6, 0.0, -6.5), QID, -1),
    _so(FENCE_4M, "FenceResidentialPieceLowRandom01", (-13.6, 0.0, -10.5), QID, -1),
    _so(FENCE_4M, "FenceResidentialPieceLowRandom01", (-13.6, 0.0, -14.5), QID, -1),
    _so(FENCE_4M, "FenceResidentialPieceLowRandom01", (12.6, 0.0, -6.5), QID, -1),
    _so(FENCE_4M, "FenceResidentialPieceLowRandom01", (12.6, 0.0, -10.5), QID, -1),
    _so(FENCE_2M, "FenceResidentialPieceLowShortRandom01", (12.6, 0.0, -13.5), QID, -1),
    _so(SNOWMAN, "SnowmanRandom01", (3.5, 0.0, -4.0), QID, -1),                    # learning area
    _so(BENCH, "GardenBenchRandom01", _bf((6.5, 0.0, 5.0)), QID, -1),          # +X, storefront 1
    _so(BENCH, "GardenBenchRandom01", _bf((-9.0, 0.0, 1.8)), YM90, -1),        # under the meeting board
    _so(BIN, "TrashbinRandom01", _bf((-0.9, 0.0, 4.8)), QID, -1),              # beside the main door
    _so(BIN, "TrashbinRandom01", _bf((-8.9, 0.0, 3.35)), QID, -1),             # -Y corner
    _so(WALL_SHORT, "WallLightShortRangeRandom01", _bf((-3.5, 3.02, 4.0)), QID, 0),    # wingL door
    _so(WALL_SHORT, "WallLightShortRangeRandom01", _bf((9.25, 3.02, 4.0)), QID, 0),    # wingR door
    _so(WALL_SHORT, "WallLightShortRangeRandom01", _bf((-4.1, 2.90, -7.0)), Y180, 0),  # svc door
    _so(WALL_LIGHT, "WallLightRandom01", _bf((-4.75, 3.70, -7.0)), Y180, 0),           # yard flood
    _so(WALL_LIGHT, "WallLightRandom01", _bf((7.85, 3.70, -7.0)), Y180, 0),            # yard flood
    _so(AC_VENT, "ACVentWallRandom01", _bf((12.0, 5.8, -7.0)), Y180, 0),               # back UF bays
    _so(AC_VENT, "ACVentWallRandom01", _bf((8.2, 5.8, -7.0)), Y180, 0),
    _so(AC_VENT, "ACVentWallRandom01", _bf((4.4, 5.8, -7.0)), Y180, 0),
    _so(AC_VENT, "ACVentWallRandom01", _bf((0.6, 5.8, -7.0)), Y180, 0),
)
SUB_OBJECT_NOTES = ("ObjectSubObjects on SkiSchool: m_GroupIndex 0, m_Probability 100; "
                    "m_ParentMesh per object (-1 lot, 0 wall-attached); GUIDs inferred (the "
                    "one placeholder GUID every variant shares), proof = no UNLISTED WARN in "
                    "preflight after whitelisting + the props appear in game")

# ---- fine-detail pass step 2 (module docstring) ------------------------------------------
SAFETY_CONE = "dc8abeea43367b24ba2668784a309e56"    # SafetyConeRandom01 (cs2_liftdress.SUBOBJ, verified)
ENTRY_LAMPS = (("mainL", 0.25), ("mainR", 1.75))    # the old lanterns' (tag, y); lens height LAMP_Z
ENTRY_LAMP_Z = 2.55
# kit racks replacing RACKS (same order): alternating loadouts, prefab = the kit object prefab
KIT_RACKS = ("SkiDressSkiRack", "SkiDressSkiRackV2")
FINE_SUB_OBJECTS = tuple(
    _so(SAFETY_CONE, "SafetyConeRandom01", (y, 0.0, x), QID, -1) for x, y in CONES
) + tuple(
    _so(WALL_SHORT, "WallLightShortRangeRandom01", _bf((y, ENTRY_LAMP_Z, TW[1])), QID, 0)
    for _tag, y in ENTRY_LAMPS
)
# RACKS is defined below; the kit rows are appended there (KIT_ROWS)
SIGN_SET = SignSetSpec(
    prop="SkiSignSetSchool", letter="B", host="school", owner=True,
    faces=(("sign_school_face", "school.name"), ("sign_meet_face", "school.meet"),
           ("sign_bibs_face", "school.bibs"), ("sign_notice_face", "school.notice"))
    + tuple((f"sign_lesson{k}_face", f"school.lesson{k}") for k in range(4))
    + tuple((f"sign_{tag}_face", "school.markstaff") for tag, _door in MARK_DOORS)
    + (("sign_nosled_face", "school.marknosled"),))

# ---- detail pass 1 (design 3.1, skeptic M1-M10; build/detail-pass-1-kit.md API) ------
BOARD_BROWN = (0.20, 0.14, 0.10)     # cs2_bldg.BOARD_BROWN_SRGB (the slot-3 roof target)
WHITE = 0.92                         # cs2_bldg.WHITE_SRGB
SCHOOL_RED = (0.62, 0.10, 0.08)      # cs2_bldg.HUT_RED_SRGB: flag cloth, red markings
TRAIL_GREEN = (0.08, 0.50, 0.20)
TRAIL_BLUE = (0.08, 0.28, 0.72)
TRAIL_BLACK = (0.05, 0.05, 0.05)
# S1-S4 boards (BF): (name, wall side, plane, u, z bottom, w, h, frame mat)
BOARDS = (("school", "+X", 4.0, 1.0, 3.30, 4.20, 0.90, "trim_white"),    # name board
          ("meet", "-Y", -8.0, 1.8, 1.00, 2.40, 1.20, "trim_white"),      # meeting board
          ("bibs", "-Y", -8.0, -5.0, 1.20, 2.00, 0.90, "trim_white"),     # bib board
          ("notice", "+X", 4.0, 2.65, 1.10, 0.70, 1.00, "timber_heavy"))  # notice board
# S7 racks (M10 gear=True, BF): in front of storefronts 3 and 2 (the legacy path builds them;
# the fine path hosts the kit racks instead)
_A, _W, _K, _B, _R = "accent", "trim_white", "ski_black", "ski_blue", "ski_red"
RACKS = (
    ("schoolA", 5.6, -5.9, (
        (0, 1, "ski", (_A, _K), 1.48), (2, 1, "ski", (_W, _B), 1.40),
        (3, 1, "board", (_B, _W), 1.42), (6, 1, "ski", (_R, _K), 1.55),
        (1, -1, "ski", (_K, _A), 1.52), (4, -1, "poles", _A, 1.40),
        (6, -1, "ski", (_B, _W), 1.45))),
    ("schoolB", 5.6, 12.0, (
        (1, 1, "ski", (_W, _R), 1.50), (3, 1, "ski", (_A, _K), 1.42),
        (5, 1, "board", (_K, _A), 1.46), (0, -1, "ski", (_R, _W), 1.58),
        (3, -1, "ski", (_K, _W), 1.62), (5, -1, "poles", _K, 1.38),
        (7, -1, "ski", (_B, _K), 1.44))),
)
# kit colour variants (Richard 2026-10-12 ~18:50; build/kit-colour-variants.md): blue, the school's lesson /
# trail-board blue (TRAIL_BLUE); the brown siding has no slot-2 surface to match
KIT_COLOUR_SET = "blue"
# the kit rows at the racks' own centres (on grade: m_ParentMesh -1), facing game +Z (identity)
KIT_ROWS = tuple(kit_row(KIT_RACKS[i % len(KIT_RACKS)], _bf((y, 0.0, x)), parent=-1,
                         note=f"replaces skirack_{nm} (ski_rack_frame slots 8 along Y at BF "
                              f"({x}, {y}) = lot ({x + OX}, {y}))", colour_set=KIT_COLOUR_SET)
                 for i, (nm, x, y, _gear) in enumerate(RACKS))
ESTOP_ROWS = tuple(kit_row("SkiDressEStop", (y, 0.0, x), parent=-1,
                           note=f"carpet {c[0]} loading-end stop button")
                   for (x, y), c in zip(ESTOPS, CARPETS))
FLAG = (6.8, -9.0, 7.5, (1.4, 0.9))              # S8 x, y, top h, cloth (BF, along Y, faces +X)
SIGN_LAMPS = ((-0.4, 4.40), (2.4, 4.40))         # S12 (y, z) on the tower front, above S1
BELFRY_FLOOR = 1.20                              # S11 lit floor half size (posts at 1.28)
BELFRY_LIFT = 0.02                               # above the deck top / off the rail face
WEATHER_INTENSITY = 0.55     # re-swept for revision 2 (build/school-rev2.md section 7)

# VariationSet defaults (target sRGB / 0.80, contract 1.6): barn-red siding, pad orange,
# dark-brown tower / lantern roofs (slot 3: the kit's standing-seam roof is slot 3).
SLOTS = {1: (0.575, 0.35, 0.25), 2: PAD_RGB, 3: (0.25, 0.19, 0.15)}
# kit window_bay internals: jambs start at the sill bottom / end at the head top inside
# those boxes (hidden coplanar caps, cs2_bldg.window_bay) - not a recipe overlap
KIT_INTERNAL = (("jamb_", "sill_"), ("jamb_", "head_"))
# revision 2: the carpet skirts (open-bottom closed-sided ramps from grade) are the carpets'
# ground parts; the belt now starts 0.20 m up the ramp (no longer a ground part)
GROUND = ("wall_gf", "wall_tower", "band_grade", "carpet_skirt")
FIELD_FACE = "emblem_school_face"
# the flag cloth labels go with their mast at LOD1 (skeptic M8), the alarm bell and the gallery
# frames are LOD0 only; the carpet covers stay to LOD1 (they close the ramp tops)
LOD1_EXTRA = ("bell_", "step_", "hvac_", "cone_", "flagpanel_", "lanterncap_spire",
              "emblem_schoolflag", "galleryrib_")
LOD2_EXTRA = LOD1_EXTRA + ("lanternrail_", "lamp_glow", "carpetrail_", "glulam_", "gallery_in")
# check_inside exemption: the brace struts bury their feet 0.12 m in the back wall
INSIDE_EXEMPT = ("glulam_",)


# ======================================================================================
# Builder
# ======================================================================================

def _boxes(C, name, boxes, mat):
    return C.new_part(name, lambda bm: [C.box_into(bm, lo, hi, drop) for lo, hi, drop in boxes],
                      mat)


def _shift(parts, dx):
    """Move BF parts into the lot frame (+dx along X). A part at identity gets its mesh moved
    (it may become the join's active object); a placed kit part (cs2_kit doors) its location."""
    import bpy
    from mathutils import Matrix
    T = Matrix.Translation((dx, 0.0, 0.0))
    for o, _k in parts:
        if o.parent is not None:
            raise RuntimeError(f"_shift: {o.name} has a parent")
        ident = (o.location.length < 1e-12 and max(abs(a) for a in o.rotation_euler) < 1e-12
                 and max(abs(s - 1.0) for s in o.scale) < 1e-12)
        if ident and o.data.users == 1:
            o.data.transform(T)
            o.data.update()
        else:
            o.location.x += dx
    bpy.context.view_layer.update()


def roof_parts(C):
    """Revision 2 UF roof (BF): slab top `roof_uf` (concrete), edge `fascia_uf` (white), soffits
    `soffit_uf` (timber); the slab is cut around the tower (x >= tx0, ty0..ty1)."""
    ux0, ux1 = UF[0], UF[1]
    gy0, gy1 = GF[2], GF[3]
    tx0, _tx1, ty0, ty1 = TW
    xF, xB = ux1 + OV_FRONT, ux0 - OV_BACK
    yL, yR = gy0 - OV_RAKE, gy1 + OV_RAKE

    def top_q(x0, x1, y0, y1):
        return ([(x0, y0, roof_top(x0)), (x1, y0, roof_top(x1)), (x1, y1, roof_top(x1)),
                 (x0, y1, roof_top(x0))], (ROOF_T, 0.0, 1.0))

    def bot_q(x0, x1, y0, y1):
        return ([(x0, y0, roof_bot(x0)), (x1, y0, roof_bot(x1)), (x1, y1, roof_bot(x1)),
                 (x0, y1, roof_bot(x0))], (0.0, 0.0, -1.0))
    P = C.quads_part("roof_uf", [top_q(xB, tx0, yL, ty0), top_q(xB, tx0, ty0, ty1),
                                 top_q(xB, tx0, ty1, yR), top_q(tx0, xF, yL, ty0),
                                 top_q(tx0, xF, ty1, yR)], "concrete")
    fas = []
    for y0, y1 in ((yL, ty0), (ty1, yR)):
        fas.append(([(xF, y0, roof_bot(xF)), (xF, y1, roof_bot(xF)), (xF, y1, roof_top(xF)),
                     (xF, y0, roof_top(xF))], (1, 0, 0)))
    fas.append(([(xB, yL, roof_bot(xB)), (xB, yR, roof_bot(xB)), (xB, yR, roof_top(xB)),
                 (xB, yL, roof_top(xB))], (-1, 0, 0)))
    for yw, out in ((yL, (0, -1, 0)), (yR, (0, 1, 0))):
        fas.append(([(xB, yw, roof_bot(xB)), (xF, yw, roof_bot(xF)), (xF, yw, roof_top(xF)),
                     (xB, yw, roof_top(xB))], out))
    P += C.quads_part("fascia_uf", fas, "trim_white")
    P += C.quads_part("soffit_uf", [bot_q(ux1, xF, yL, ty0), bot_q(ux1, xF, ty1, yR),
                                    bot_q(xB, ux0, yL, yR), bot_q(ux0, ux1, yL, gy0),
                                    bot_q(ux0, ux1, gy1, yR)], "timber_lap")
    return P


def glulam_parts(C):
    """The back overhang's 7 glulam braces (an outrigger under the soffit + a diagonal strut
    from the back wall above the GF band; 120838 / 120856) and the front outrigger ends
    (original photo: the dark beam ends under the main eave)."""
    ux0, ux1 = UF[0], UF[1]
    xB = ux0 - OV_BACK
    cosr = math.cos(math.radians(ROOF_PITCH_DEG))
    oh, ow, sw = 0.30, 0.22, 0.20          # outrigger depth / width, strut section
    dz = 0.5 * oh / cosr                   # axis below the soffit so the top face lies on it

    def back(bm):
        for y in BRACE_Y:
            C.beam_into(bm, (ux0, y, roof_bot(ux0) - dz), (xB + 0.08, y, roof_bot(xB + 0.08) - dz),
                        ow, oh)
            xs = ux0 - BRACE_OUT
            top = (xs, y, roof_bot(xs) - 2.0 * dz)       # the outrigger's underside
            foot = (ux0, y, BRACE_FOOT_Z)
            d = [b - a for a, b in zip(foot, top)]
            n = math.sqrt(sum(v * v for v in d))
            e = [v / n * 0.12 for v in d]
            C.beam_into(bm, tuple(a - b for a, b in zip(foot, e)),
                        tuple(a + b for a, b in zip(top, e)), sw, sw)

    def front(bm):
        sh, swd = 0.24, 0.15
        dzf = 0.5 * sh / cosr
        x1 = ux1 + OV_FRONT - 0.07
        for y in STUB_Y:
            C.beam_into(bm, (ux1, y, roof_bot(ux1) - dzf), (x1, y, roof_bot(x1) - dzf), swd, sh)
    return C.new_part("glulam_back", back, "timber_heavy") + \
        C.new_part("glulam_front", front, "timber_heavy")


def marking_parts(K, C):
    """Revision 2 red markings (BF): placard faces on the two white door leaves, the NO
    SLEDDING sign over the back door (sign-set faces, mat label) and the modelled alarm bell."""
    from cs2_bldg import WallRef
    P = []
    doors = {d[0]: d for d in DOORS}
    _s0, s1, _sd = STEP
    w, h = MARK_STAFF
    for tag, dn in MARK_DOORS:
        _n, side, xw, y, _w, _hh = doors[dn]
        zc = s1 + 0.02 + MARK_CZ                    # kit door: leaf bottom = loc z + 0.02
        u = y if side == "+X" else -y
        leaf_d = K.DOOR2_LEAF.get(dn, LEAF_PROUD) if K.DOOR_KIT["version"] == 2 else LEAF_PROUD
        P += K.pane_quad(f"sign_{tag}_face", WallRef(side, xw), u - w / 2, u + w / 2,
                         zc - h / 2, zc + h / 2, d=leaf_d + MARK_PROUD, mat="label")
    _n, side, xw, y, _w, _hh = doors["svc"]
    w, h = MARK_NOSLED
    u = y if side == "+X" else -y
    P += K.pane_quad("sign_nosled_face", WallRef(side, xw), u - w / 2, u + w / 2, NOSLED_Z0,
                     NOSLED_Z0 + h, d=0.006, mat="label")
    by, bz, br, bd = ALARM_BELL
    tx1 = TW[1]
    P += C.cyl_part("bell_alarm", (tx1, by, bz), (tx1 + bd, by, bz), br, "fire_red", segs=8,
                    cap0=False)
    return P


def detail_parts(K, C, fine=False):
    """Detail pass 1 dressing ON THE BUILDING (BF; design/detail-pass-1.md 3.1 as corrected by
    the skeptic) + the revision-2 markings. LOD rules: `sign_` boards to LOD1 (kit
    LOD2_DELETE), `emblem_schoolflag` LOD0 only (LOD1_EXTRA), stripes / flag mast / lamps
    LOD0 only (kit LOD1_DELETE). fine: the kit's fine=True geometry and NO modelled racks
    (KIT_ROWS replace them)."""
    from cs2_bldg import WallRef
    P = []
    fk = {"fine": True} if fine else {}
    # S1-S4 boards (label quads stamped by recipe.labels "display" entries)
    for nm, side, plane, u, z, w, h, mat in BOARDS:
        P += K.sign_board(nm, WallRef(side, plane), u, z, w, h, mat=mat)
    # S7 ski racks with real gear (M10); the fine-detail pass hosts the kit racks instead
    if not fine:
        for nm, x, y, gear in RACKS:
            P += K.ski_rack_frame(nm, x, y, slots=8, mat="galv", along="Y", skis=gear,
                                  ski_mat="accent", gear=True)
    # S8 flag: the cloth is two label quads (white skier on red), facing the road
    fx, fy, fh, cloth = FLAG
    P += K.flag_mast("school", fx, fy, h=fh, flag=cloth, along="Y", face=True, **fk)
    # S9 nosing stripes: the 4 door steps (outer 0.05 m)
    _s0, s1, sd = STEP
    for nm, side, xw, y, w, _hh in DOORS:
        sg = 1.0 if side == "+X" else -1.0
        xc = xw + sg * (sd - 0.025)
        y0, y1 = y - w / 2 - STEP_SIDE, y + w / 2 + STEP_SIDE
        P += K.safety_stripe(f"step{nm}", (xc, y0), (xc, y1), s1)
    # revision 2: the red markings (the door numbering is gone)
    P += marking_parts(K, C)
    # S11 belfry glow: a lit floor on the lantern deck + the balustrade's inner faces
    cx, cy = TC
    f, zf = BELFRY_FLOOR, DECK_Z1 + BELFRY_LIFT
    P += C.quads_part("lamp_belfryfloor", [([(cx - f, cy - f, zf), (cx + f, cy - f, zf),
                                              (cx + f, cy + f, zf), (cx - f, cy + f, zf)],
                                             (0, 0, 1))], "lamp")
    h, t, a = DECK_HALF, 0.05, DECK_HALF - POST_W
    zr0, zr1 = zf, RAIL_TOP - 0.01
    xi0, xi1 = cx - h + t + BELFRY_LIFT, cx + h - t - BELFRY_LIFT
    yi0, yi1 = cy - h + t + BELFRY_LIFT, cy + h - t - BELFRY_LIFT
    P += C.quads_part("lamp_belfryrail", [
        ([(xi0, cy - a, zr0), (xi0, cy + a, zr0), (xi0, cy + a, zr1), (xi0, cy - a, zr1)], (1, 0, 0)),
        ([(xi1, cy + a, zr0), (xi1, cy - a, zr0), (xi1, cy - a, zr1), (xi1, cy + a, zr1)], (-1, 0, 0)),
        ([(cx + a, yi0, zr0), (cx - a, yi0, zr0), (cx - a, yi0, zr1), (cx + a, yi0, zr1)], (0, 1, 0)),
        ([(cx - a, yi1, zr0), (cx + a, yi1, zr0), (cx + a, yi1, zr1), (cx - a, yi1, zr1)], (0, -1, 0))],
        "lamp")
    # S12 sign lamps over the name board (lenses `lamp_sign<k>`, arms remapped to galv)
    for k, (y, z) in enumerate(SIGN_LAMPS):
        lp, _pt = K.lantern(f"sign{k}", (TW[1], y, z), "+X", kind="wall", h=0.18, w=0.25)
        P += lp
    return P


def carpet_parts(K, C, c):
    """One magic carpet (LOT frame) at the fixed GRADE: `carpet_skirt<t>` the closed-sided ramp
    from grade (open bottom: a ground part), `carpet_belt<t>` (rubber), `carpetrail_<t>` raised
    side covers (accent), `carpetrail_ramp<t>` the loading ramp, `carpet_skirt<t>deck` +
    `carpetdeck_<t>` the top platform (flush with the belt end), `stripe_ramp<t>` / `stripe_top<t>`."""
    tag, yc, xl, L, cov, wd, _bubble = c
    xt, xe = xl - L, xl - L - DECK_L
    hb, wb = BELT_W / 2.0, BELT_W / 2.0 + cov
    z0, zt = _carpet_z(c, xl), _carpet_z(c, xt)
    yl, yr = yc - wb, yc + wb
    P = []
    # closed-sided ramp: two trapezoid skirts + the loading-end face (the deck block closes the top)
    P += C.quads_part(f"carpet_skirt{tag}", [
        ([(xl, yl, 0.0), (xt, yl, 0.0), (xt, yl, zt), (xl, yl, z0)], (0, -1, 0)),
        ([(xl, yr, 0.0), (xt, yr, 0.0), (xt, yr, zt), (xl, yr, z0)], (0, 1, 0)),
        ([(xl, yl, 0.0), (xl, yr, 0.0), (xl, yr, z0), (xl, yl, z0)], (1, 0, 0))], "timber_heavy")
    P += C.quads_part(f"carpet_belt{tag}", [
        ([(xl, yc - hb, z0), (xt, yc - hb, zt), (xt, yc + hb, zt), (xl, yc + hb, z0)],
         (GRADE, 0.0, 1.0))], "rubber")
    rail = []
    ch = COVER_H
    for ya, yb, yin, yout, sg in ((yl, yc - hb, yc - hb, yl, 1.0), (yc + hb, yr, yc + hb, yr, -1.0)):
        rail.append(([(xl, ya, z0 + ch), (xt, ya, zt + ch), (xt, yb, zt + ch), (xl, yb, z0 + ch)],
                     (GRADE, 0.0, 1.0)))
        rail.append(([(xl, yin, z0), (xt, yin, zt), (xt, yin, zt + ch), (xl, yin, z0 + ch)],
                     (0.0, sg, 0.0)))
        rail.append(([(xl, yout, z0), (xt, yout, zt), (xt, yout, zt + ch), (xl, yout, z0 + ch)],
                     (0.0, -sg, 0.0)))
        rail.append(([(xl, ya, z0), (xl, yb, z0), (xl, yb, z0 + ch), (xl, ya, z0 + ch)], (1, 0, 0)))
        rail.append(([(xt, ya, zt), (xt, yb, zt), (xt, yb, zt + ch), (xt, ya, zt + ch)], (-1, 0, 0)))
    P += C.quads_part(f"carpetrail_{tag}", rail, "accent")

    def ramp(bm):
        lo_z = 0.02
        x_lo = xl + RAMP_L
        C.face(bm, [(xl, yc - hb, lo_z), (x_lo, yc - hb, lo_z), (x_lo, yc + hb, lo_z),
                    (xl, yc + hb, lo_z)], (0, 0, -1))
        C.face(bm, [(xl, yc - hb, lo_z), (x_lo, yc - hb, lo_z), (xl, yc - hb, z0)], (0, -1, 0))
        C.face(bm, [(xl, yc + hb, lo_z), (x_lo, yc + hb, lo_z), (xl, yc + hb, z0)], (0, 1, 0))
        C.face(bm, [(x_lo, yc - hb, lo_z), (x_lo, yc + hb, lo_z), (xl, yc + hb, z0),
                    (xl, yc - hb, z0)], (0.3, 0, 1))
    P += C.new_part(f"carpetrail_ramp{tag}", ramp, "accent")
    # the top platform (a ground block, open bottom) and its deck top
    yd0, yd1 = yc - wd / 2.0, yc + wd / 2.0
    P += C.quads_part(f"carpet_skirt{tag}deck", [
        ([(xe, yd0, 0.0), (xt, yd0, 0.0), (xt, yd0, zt), (xe, yd0, zt)], (0, -1, 0)),
        ([(xe, yd1, 0.0), (xt, yd1, 0.0), (xt, yd1, zt), (xe, yd1, zt)], (0, 1, 0)),
        ([(xe, yd0, 0.0), (xe, yd1, 0.0), (xe, yd1, zt), (xe, yd0, zt)], (-1, 0, 0)),
        ([(xt, yd0, 0.0), (xt, yl, 0.0), (xt, yl, zt), (xt, yd0, zt)], (1, 0, 0)),
        ([(xt, yr, 0.0), (xt, yd1, 0.0), (xt, yd1, zt), (xt, yr, zt)], (1, 0, 0))], "timber_heavy")
    P += C.quads_part(f"carpetdeck_{tag}", [([(xe, yd0, zt), (xt, yd0, zt), (xt, yd1, zt),
                                              (xe, yd1, zt)], (0, 0, 1))], "deck")
    # nosing: the loading edge (sunk 15 mm into the ramp / belt) and the landing edge on the deck
    hs = hb - 0.01                                  # 10 mm in from the ramp / cover sides
    P += K.safety_stripe(f"ramp{tag}", (xl + 0.025, yc - hs), (xl + 0.025, yc + hs), z0 - 0.015,
                         t=0.023)
    P += K.safety_stripe(f"top{tag}", (xt - 0.025, yc - hs), (xt - 0.025, yc + hs), zt)
    return P


def gallery_parts(C, c):
    """The bubble carpet's covered gallery (LOT frame): plumb arched frames every GALLERY[3] m,
    standing on the side covers; `gallery_side` dark tinted side panels (glass_dark),
    `gallery_roof` the frosted arc (trim_white: an OPAQUE baked panel, see-through glass is
    unverified in our pipeline), `gallery_in` the inside (seen through the open ends),
    `galleryrib_<t>` white stiles on the sides + frame strips over the arc."""
    tag, yc, xl, L, _cov, _wd, _b = c
    xt = xl - L
    a, hs, rise, step = GALLERY

    def zc(x):
        return _carpet_z(c, x) + COVER_H

    # profile (lateral v, height h above the cover top): side, 3 arc segments, apex, mirrored
    arc = [(-a * math.cos(math.radians(t)), hs + rise * math.sin(math.radians(t)))
           for t in (0.0, 30.0, 60.0, 90.0)]
    prof = [(-a, 0.0)] + arc + [(-v, h) for v, h in reversed(arc[:-1])] + [(a, 0.0)]
    segs = list(zip(prof, prof[1:]))                # 8 segments: side, 3 + 3 arc, side

    def pt(x, v, h):
        return (x, yc + v, zc(x) + h)

    def seg_quad(x0, x1, p, q, off=0.0):
        (v0, h0), (v1, h1) = p, q
        nv, nh = (h1 - h0), -(v1 - v0)              # a normal of the segment in (v, h)
        n = math.hypot(nv, nh)
        nv, nh = nv / n, nh / n
        if nv * ((v0 + v1) / 2) + nh * ((h0 + h1) / 2 - hs * 0.5) < 0:
            nv, nh = -nv, -nh                       # outward (away from the tunnel axis)
        ov, oh = nv * off, nh * off
        pts = [pt(x0, v0 + ov, h0 + oh), pt(x1, v0 + ov, h0 + oh),
               pt(x1, v1 + ov, h1 + oh), pt(x0, v1 + ov, h1 + oh)]
        return pts, (0.0, nv, nh)

    side_q, roof_q, in_q = [], [], []
    for i, (p, q) in enumerate(segs):
        pts, out = seg_quad(xl, xt, p, q)
        (side_q if i in (0, len(segs) - 1) else roof_q).append((pts, out))
        ipts, _o = seg_quad(xl, xt, p, q, off=-PANEL_T)    # the panel's inner skin (no weld)
        in_q.append((ipts[::-1], (-out[0], -out[1], -out[2])))
    P = C.quads_part("gallery_side", side_q, "glass_dark")
    P += C.quads_part("gallery_roof", roof_q, "trim_white")
    P += C.quads_part("gallery_in", in_q, "trim_white")
    n = int(round(L / step))
    rib = []
    for k in range(n + 1):
        x = xl - L * k / n
        xa, xb = x + RIB_W / 2.0, x - RIB_W / 2.0
        if xa > xl:
            xa, xb = xl, xl - RIB_W
        if xb < xt:
            xa, xb = xt + RIB_W, xt
        for i, (p, q) in enumerate(segs):
            if i in (0, len(segs) - 1):             # side stile: its outer face + two cheeks
                (v0, h0), (_v1, h1) = p, q
                sg = -1.0 if v0 < 0 else 1.0
                vo = v0 + sg * STILE_D
                rib.append(([pt(xa, vo, h0), pt(xb, vo, h0), pt(xb, vo, h1), pt(xa, vo, h1)],
                            (0.0, sg, 0.0)))
                for xx, sx in ((xa, 1.0), (xb, -1.0)):
                    rib.append(([pt(xx, v0, h0), pt(xx, vo, h0), pt(xx, vo, h1), pt(xx, v0, h1)],
                                (sx, 0.0, 0.0)))
            else:                                   # arc: an offset frame strip
                rib.append(seg_quad(xa, xb, p, q, off=RIB_OUT))
    P += C.quads_part(f"galleryrib_{tag}", rib, "trim_white")
    return P


def yard_parts(K, C, fine=False):
    """The revision-2 back yard (LOT frame): both carpets, the bubble gallery, the maze at
    carpet A's loading end, the lesson signs, the slalom cones (legacy path) and the pennants."""
    fk = {"fine": True} if fine else {}
    P = []
    for c in CARPETS:
        P += carpet_parts(K, C, c)
        if c[6]:
            P += gallery_parts(C, c)
    # S6 maze at carpet A's loading end (galv posts, slot-2 tape)
    P += K.maze_fence("carpet", MAZE_SEGS, h=1.0, post_every=MAZE_POST, **fk)
    # S5 lesson-meeting post signs, facing the back door (+X)
    for k, y in enumerate(LESSON_Y):
        P += K.post_sign(f"lesson{k}", LESSON_X, y, "+X", LESSON_SIZE, LESSON_SIZE, LESSON_TOP,
                         **fk)
    if not fine:                                # fine: SafetyConeRandom01 rows (design A.2)
        for k, (x, y) in enumerate(CONES):
            P += C.cyl_part(f"cone_{k}", (x, y, 0.02), (x, y, 0.02 + CONE_H), CONE_R,
                            "accent", segs=8, r1=0.0)
    for k, (x, y) in enumerate(PENNANTS):
        P += K.flag_mast(f"corral{k}", x, y, h=2.0, flag=(0.35, 0.25), **fk)
    return P


def massing_volumes():
    """The G10b check boxes (LOT frame): GF, UF up to its old flat-roof wall top (conservative
    under the monopitch), the tower."""
    gx0, gx1, gy0, gy1 = GF
    ux0, ux1 = UF[0], UF[1]
    tx0, tx1, ty0, ty1 = TW
    return [((gx0 + OX, gy0, 0.0), (gx1 + OX, gy1, GF_TOP)),
            ((ux0 + OX, gy0, GF_TOP), (ux1 + OX, gy1, UF_TOP)),
            ((tx0 + OX, ty0, 0.0), (tx1 + OX, ty1, TOWER_WALL_TOP))]


def build(recipe, opts):
    import cs2_bldg as K
    import cs2_kit
    C = load_civic()
    from cs2_bldg import WallRef, WinSpec
    fine = bool(getattr(recipe, "fine_detail", False))
    P = []
    gx0, gx1, gy0, gy1 = GF
    ux0, ux1 = UF[0], UF[1]
    tx0, tx1, ty0, ty1 = TW

    # ---- GF walls: three boxes, the tower footprint left out (M4) ------------------------
    P += _boxes(C, "wall_gf", [
        ((gx0, gy0, 0.0), (gx1, ty0, GF_TOP), ("-Z", "+Z", "+Y")),
        ((gx0, ty1, 0.0), (gx1, gy1, GF_TOP), ("-Z", "+Z", "-Y")),
        ((gx0, ty0, 0.0), (tx0, ty1, GF_TOP), ("-Z", "+Z", "+Y", "-Y", "+X"))], "siding_paint")
    # ---- UF walls (revision 2): tops follow the roof underside; the tower left out --------
    zf, zb = roof_bot(ux1), roof_bot(ux0)
    P += C.quads_part("wall_uf", [
        ([(ux1, gy0, GF_TOP), (ux1, ty0, GF_TOP), (ux1, ty0, zf), (ux1, gy0, zf)], (1, 0, 0)),
        ([(ux1, ty1, GF_TOP), (ux1, gy1, GF_TOP), (ux1, gy1, zf), (ux1, ty1, zf)], (1, 0, 0)),
        ([(ux0, gy0, GF_TOP), (ux0, gy1, GF_TOP), (ux0, gy1, zb), (ux0, gy0, zb)], (-1, 0, 0)),
        ([(ux0, gy0, GF_TOP), (ux1, gy0, GF_TOP), (ux1, gy0, zf), (ux0, gy0, zb)], (0, -1, 0)),
        ([(ux0, gy1, GF_TOP), (ux1, gy1, GF_TOP), (ux1, gy1, zf), (ux0, gy1, zb)], (0, 1, 0))],
        "siding_paint")

    # ---- tower: front from grade (field cell left out), sides / back where exposed -----
    fy0, fy1, fz0, fz1 = FIELD
    zs_front = [0.0, GF_TOP, fz0, fz1, TOWER_WALL_TOP]
    polys = C.grid_quads(lambda u, v: (tx1, u, v), [ty0, fy0, fy1, ty1], zs_front, (1, 0, 0),
                         skip={(1, 2)})
    for yw, out in ((ty0, (0, -1, 0)), (ty1, (0, 1, 0))):
        polys += C.grid_quads(lambda u, v, yw=yw: (u, yw, v), [ux1, tx1],
                              [ROOF_GF_Z, ROOF_UF_Z, TOWER_WALL_TOP], out)
        polys.append(([(tx0, yw, roof_top(tx0)), (ux1, yw, roof_top(ux1)),
                       (ux1, yw, TOWER_WALL_TOP), (tx0, yw, TOWER_WALL_TOP)], out))
    polys += C.grid_quads(lambda u, v: (tx0, u, v), [ty0, ty1], [roof_top(tx0), TOWER_WALL_TOP],
                          (-1, 0, 0))
    P += C.quads_part("wall_tower", polys, "siding_paint")
    P += C.quads_part(FIELD_FACE, [([(tx1, fy0, fz0), (tx1, fy1, fz0), (tx1, fy1, fz1),
                                     (tx1, fy0, fz1)], (1, 0, 0))], "siding_paint")

    # ---- GF flat roof strip (the canopy with the white fascia), the UF monopitch (rev 2) --
    P += C.quads_part("roof_gf", [
        ([(ux1, gy0, ROOF_GF_Z), (gx1, gy0, ROOF_GF_Z), (gx1, ty0, ROOF_GF_Z), (ux1, ty0, ROOF_GF_Z)], (0, 0, 1)),
        ([(ux1, ty1, ROOF_GF_Z), (gx1, ty1, ROOF_GF_Z), (gx1, gy1, ROOF_GF_Z), (ux1, gy1, ROOF_GF_Z)], (0, 0, 1))],
        "concrete")
    P += roof_parts(C)
    P += glulam_parts(C)

    # ---- white GF band, stopping at the tower corner boards (M4) -------------------------
    b = BAND_PROUD
    cb = CORNER / 2.0
    z0, z1 = BAND_GF
    P += _boxes(C, "fascia_gf", [
        ((gx1, gy0, z0), (gx1 + b, ty0 - cb, z1), ("-X", "-Y")),
        ((gx1, ty1 + cb, z0), (gx1 + b, gy1, z1), ("-X", "+Y")),
        ((gx0 - b, gy0 - b, z0), (gx1 + b, gy0, z1), ("+Y",)),
        ((gx0 - b, gy1, z0), (gx1 + b, gy1 + b, z1), ("-Y",)),
        ((gx0 - b, gy0, z0), (gx0, gy1, z1), ("+X", "-Y", "+Y"))], "trim_white")

    # ---- grade band (ground), gaps at the doors ---------------------------------------
    gp = GRADE_PROUD
    gaps_front = sorted((dy - w / 2 - STEP_SIDE, dy + w / 2 + STEP_SIDE)
                        for _n, s, _x, dy, w, _h in DOORS if s == "+X")
    gaps_back = sorted((dy - w / 2 - STEP_SIDE, dy + w / 2 + STEP_SIDE)
                       for _n, s, _x, dy, w, _h in DOORS if s == "-X")

    def runs(a0, a1, gaps):
        out, cur = [], a0
        for g0, g1 in gaps:
            out.append((cur, g0))
            cur = g1
        out.append((cur, a1))
        return [(s0, s1) for s0, s1 in out if s1 - s0 > 1e-6]
    gb = []
    for y0, y1 in runs(gy0, gy1, gaps_front):
        gb.append(((gx1, y0, 0.0), (gx1 + gp, y1, GRADE_H), ("-X", "-Z")))
    for y0, y1 in runs(gy0, gy1, gaps_back):
        gb.append(((gx0 - gp, y0, 0.0), (gx0, y1, GRADE_H), ("+X", "-Z")))
    gb.append(((gx0 - gp, gy0 - gp, 0.0), (gx1 + gp, gy0, GRADE_H), ("+Y", "-Z")))
    gb.append(((gx0 - gp, gy1, 0.0), (gx1 + gp, gy1 + gp, GRADE_H), ("-Y", "-Z")))
    P += _boxes(C, "band_grade", gb, "concrete")

    # ---- white corner boards (trim_: LOD2 delete); UF corners up to the roof underside ---
    def cbox(x, y, z0, z1):
        return ((x - cb, y - cb, z0), (x + cb, y + cb, z1), ())
    corners = [cbox(x, y, GRADE_H, BAND_GF[0]) for x in (gx0, gx1) for y in (gy0, gy1)]
    corners += [cbox(x, y, BAND_GF[1], roof_bot(x)) for x in (gx0, ux1) for y in (gy0, gy1)]
    corners += [cbox(tx1, y, GRADE_H, TOWER_WALL_TOP) for y in (ty0, ty1)]
    corners += [cbox(tx0, y, roof_top(tx0 + cb), TOWER_WALL_TOP) for y in (ty0, ty1)]
    P += _boxes(C, "trim_corner", corners, "trim_white")

    # ---- truncated hip tower roof, fascia, soffit (recipe-local: kit hip_roof is full) -
    o0x, o1x, o0y, o1y = tx0 - TOWER_OVER, tx1 + TOWER_OVER, ty0 - TOWER_OVER, ty1 + TOWER_OVER
    cx, cy = TC
    h = DECK_HALF
    te = TOWER_EAVE_TOP
    outer = [(o0x, o0y, te), (o1x, o0y, te), (o1x, o1y, te), (o0x, o1y, te)]
    inner = [(cx - h, cy - h, DECK_Z0), (cx + h, cy - h, DECK_Z0), (cx + h, cy + h, DECK_Z0),
             (cx - h, cy + h, DECK_Z0)]
    roof = []
    for i in range(4):
        j = (i + 1) % 4
        mid = ((outer[i][0] + outer[j][0]) / 2 - cx, (outer[i][1] + outer[j][1]) / 2 - cy, 2.0)
        roof.append(([outer[i], outer[j], inner[j], inner[i]], mid))
    P += C.quads_part("roof_tower", roof, "metal_roof")
    fz = te - TOWER_FASCIA
    fas, sof = [], []
    wall_r = [(tx0, ty0), (tx1, ty0), (tx1, ty1), (tx0, ty1)]
    for i in range(4):
        j = (i + 1) % 4
        (ax, ay, _), (bx, by, _) = outer[i], outer[j]
        out = ((ax + bx) / 2 - cx, (ay + by) / 2 - cy, 0.0)
        fas.append(([(ax, ay, fz), (bx, by, fz), (bx, by, te), (ax, ay, te)], out))
        sof.append(([(ax, ay, fz), (bx, by, fz), (wall_r[j][0], wall_r[j][1], fz),
                     (wall_r[i][0], wall_r[i][1], fz)], (0, 0, -1)))
    P += C.quads_part("fascia_tower", fas, "trim_white")
    P += C.quads_part("soffit_tower", sof, "trim_white")

    # ---- lantern (belfry) -------------------------------------------------------------
    P += _boxes(C, "lanterncap_deck", [((cx - h, cy - h, DECK_Z0), (cx + h, cy + h, DECK_Z1), ())],
                "trim_white")
    pw = POST_W
    posts = [((cx + sx * (h - pw / 2) - pw / 2, cy + sy * (h - pw / 2) - pw / 2, DECK_Z1),
              (cx + sx * (h - pw / 2) + pw / 2, cy + sy * (h - pw / 2) + pw / 2, POST_TOP),
              ("-Z", "+Z")) for sx in (-1, 1) for sy in (-1, 1)]
    P += _boxes(C, "lanterncol_school", posts, "trim_white")
    rails, heads = [], []
    a0, a1 = -h + pw, h - pw
    t = 0.05
    for side in ("-X", "+X", "-Y", "+Y"):
        if side in ("-X", "+X"):
            xx = cx + (-h if side == "-X" else h - t)
            rails.append(((xx, cy + a0, DECK_Z1), (xx + t, cy + a1, RAIL_TOP), ("-Z",)))
            heads.append(((xx, cy + a0, HEAD_Z[0]), (xx + t, cy + a1, HEAD_Z[1]), ("+Z",)))
        else:
            yy = cy + (-h if side == "-Y" else h - t)
            rails.append(((cx + a0, yy, DECK_Z1), (cx + a1, yy + t, RAIL_TOP), ("-Z",)))
            heads.append(((cx + a0, yy, HEAD_Z[0]), (cx + a1, yy + t, HEAD_Z[1]), ("+Z",)))
    P += _boxes(C, "lanternrail_school", rails, "trim_white")
    P += _boxes(C, "lanterncap_head", heads, "trim_white")
    c0, c1, ch = CORNICE
    P += _boxes(C, "lanterncap_cornice", [((cx - ch, cy - ch, c0), (cx + ch, cy + ch, c1), ())],
                "trim_white")
    apex = c1 + ch * 0.4663076581549986         # tan(25 deg)
    base4 = [(cx - ch, cy - ch, c1), (cx + ch, cy - ch, c1), (cx + ch, cy + ch, c1),
             (cx - ch, cy + ch, c1)]
    pyr = []
    for i in range(4):
        j = (i + 1) % 4
        out = ((base4[i][0] + base4[j][0]) / 2 - cx, (base4[i][1] + base4[j][1]) / 2 - cy, 1.0)
        pyr.append(([base4[i], base4[j], (cx, cy, apex)], out))
    P += C.quads_part("lanterncap_roof", pyr, "metal_roof")
    P += C.cyl_part("lanterncap_spire", (cx, cy, apex - 0.08), (cx, cy, SPIRE_TOP), 0.08, "galv",
                    segs=6, cap0=False, r1=0.0)
    bz0, bz1, br0, br1 = BELL
    P += C.cyl_part("bell_school", (cx, cy, bz0), (cx, cy, bz1), br0, "galv", segs=10, r1=br1)
    P += C.quads_part("lamp_glow", [([(cx - 0.25, cy - 0.25, GLOW_Z), (cx + 0.25, cy - 0.25, GLOW_Z),
                                      (cx + 0.25, cy + 0.25, GLOW_Z), (cx - 0.25, cy + 0.25, GLOW_Z)],
                                     (0, 0, -1))], "lamp")

    # ---- windows (kit window_bay; frames remapped to white) ---------------------------
    front = WallRef("+X", gx1)
    for k, (y0, y1, n) in enumerate(STOREFRONTS):
        P += K.window_bay(f"store{k}", front, y0, y1, WinSpec(y1 - y0, 2.40, 0.45, lights=n),
                          room="store")
    south = WallRef("-Y", gy0)
    P += K.window_bay("corral", south, CORRAL_WIN[0], CORRAL_WIN[1],
                      WinSpec(CORRAL_WIN[1] - CORRAL_WIN[0], 1.80, 0.60, lights=3), room="store")
    w6, h6 = WIN6
    uf_front = WallRef("+X", ux1)
    for k, y in enumerate(UF_FRONT_Y):
        P += K.window_bay(f"uff{k}", uf_front, y - w6 / 2, y + w6 / 2,
                          WinSpec(w6, h6, 4.30, lights=3, rows=2), room="upper")
    back = WallRef("-X", gx0)
    bw, bh, bs, bl, br = BACK_UF_WIN
    for k, y in enumerate(BACK_Y):
        u = -y
        P += K.window_bay(f"backg{k}", back, u - w6 / 2, u + w6 / 2,
                          WinSpec(w6, h6, 0.90, lights=3, rows=2), room="back")
        P += K.window_bay(f"backu{k}", back, u - bw / 2, u + bw / 2,
                          WinSpec(bw, bh, bs, lights=bl, rows=br), room="back")
    north = WallRef("+Y", gy1)
    for k, (x, sill) in enumerate(SIDE_N):
        u = -x
        P += K.window_bay(f"sn{k}", north, u - w6 / 2, u + w6 / 2,
                          WinSpec(w6, h6, sill, lights=3, rows=2), room="back")
    for k, (x, sill) in enumerate(SIDE_S):
        P += K.window_bay(f"ss{k}", south, x - w6 / 2, x + w6 / 2,
                          WinSpec(w6, h6, sill, lights=3, rows=2), room="back")
    tfront = WallRef("+X", tx1)
    P += K.window_bay("twf", tfront, TC[1] - 0.6, TC[1] + 0.6, WinSpec(1.2, 1.4, 8.4, lights=2),
                      room="tower")
    tsouth = WallRef("-Y", ty0)
    P += K.window_bay("tws", tsouth, 1.0, 2.0, WinSpec(1.0, 1.2, 8.6, lights=2), room="tower")
    for tag, wall, u in (("slits", tsouth, 2.75), ("slitn", WallRef("+Y", ty1), -2.75)):
        P += K.window_bay(tag, wall, u - 0.15, u + 0.15, WinSpec(0.30, 0.90, 4.4, lights=1),
                          room="tower", sill_board=False, head_board=False)

    # ---- doors (cs2_kit.door, white) on closed concrete steps --------------------------
    s0, s1, sd = STEP
    for nm, side, xw, y, w, hh in DOORS:
        sg = 1.0 if side == "+X" else -1.0
        xs = sorted((xw, xw + sg * sd))
        P += K.box_part(f"step_{nm}", (xs[0], y - w / 2 - STEP_SIDE, s0),
                        (xs[1], y + w / 2 + STEP_SIDE, s1), "concrete",
                        drop=("-X",) if side == "+X" else ("+X",))
        if K.DOOR_KIT["version"] == 2:  # door kit v2 (utility): recessed steel leaf, posts + header;
            # the photo's two STAFF ONLY doors (MARK_DOORS) are flush, the others get a vision panel
            u = y if side == "+X" else -y
            P += K.door_unit(nm, WallRef(side, xw), u, w, hh, s1,
                             room="store" if side == "+X" else "back",
                             vision=nm not in {d for _t, d in MARK_DOORS},
                             leaf_key="trim_white", casing_key="trim_paint", liner_key="trim_paint")
            continue
        for o, _q in cs2_kit.door(nm, w=w, h=hh, loc=(xw, y, s1), facing=side):
            P.append((o, "trim_white"))

    # ---- entry lanterns (beside the main door); fine: vanilla WallLightShortRange rows (H4)
    if not fine:
        for tag, y in ENTRY_LAMPS:
            lp, _pt = K.lantern(tag, (tx1, y, ENTRY_LAMP_Z), "+X", kind="wall", h=0.35, w=0.20)
            P += lp

    # ---- downspouts (front only: the back is the HIGH side of the monopitch), HVAC on the slope
    for nm, x, y, zt, nrm in (("fs", gx1 + 0.10, gy0 + 0.30, ROOF_GF_Z, "-X"),
                              ("fn", gx1 + 0.10, gy1 - 0.30, ROOF_GF_Z, "-X")):
        P += K.downspout(nm, x, y, zt, nrm, size=0.08, wall_gap=0.06, z_bottom=0.20)
    for k, (x, y) in enumerate(HVAC):
        P += K.box_part(f"hvac_{'ab'[k]}", (x - 0.75, y - 0.5, roof_top(x + 0.75) - 0.05),
                        (x + 0.75, y + 0.5, roof_top(x - 0.75) + 0.75), "galv", drop=("-Z",))

    # ---- detail pass 1 + revision-2 markings on the building -----------------------------
    P += detail_parts(K, C, fine)

    # ---- into the lot frame, then the yard (revision 2) ---------------------------------
    _shift(P, OX)
    P += yard_parts(K, C, fine)

    # ---- material remap (kit hard-codes "trim" = slot 1 = barn red here) --------------
    if K.WINDOW_KIT["version"] == 2:     # window kit v2: warm off-white paint, weathered sills
        P = C.remat(P, (("sill_", "trim_paint_w"), ("head_", "trim_paint"), ("jamb_", "trim_paint"),
                        ("mull_", "trim_paint"), ("ctran_", "trim_paint"), ("reveal_", "trim_paint"),
                        ("sash_", "trim_paint"), ("lamp_main", "galv"), ("lamp_sign", "galv")))
    else:
        P = C.remat(P, (("sill_", "trim_white"), ("head_", "trim_white"), ("jamb_", "trim_white"),
                        ("mull_", "trim_white"), ("ctran_", "trim_white"), ("lamp_main", "galv"),
                        ("lamp_sign", "galv")))
    lenses = ("lamp_mainL", "lamp_mainR") + tuple(f"lamp_sign{k}" for k in range(len(SIGN_LAMPS)))
    P = [(o, "lamp" if o.name in lenses else k) for o, k in P]

    # ---- contracts on the parts (lot frame) -------------------------------------------
    C.assert_lot_margin(P, HALF_X, HALF_Y, 0.25, "school")
    C.check_coplanar(P, exempt=KIT_INTERNAL)
    C.check_inside(P, massing_volumes(), exempt=INSIDE_EXEMPT + ((tuple(f"sign_{t}_face" for t, _d in MARK_DOORS)
                                                               if K.DOOR_KIT["version"] == 2 else ())))   # door kit v2: placards on the recessed leaves
    return P


# ======================================================================================
# Recipe
# ======================================================================================

RECIPE = FineRecipe(
    key="school", aliases=("ski-school",), letter="A",
    asset="SkiSchoolCommercialLow", prefab="SkiSchool", category="buildings",
    lot=LOT, base_mode="engine", plinth=None, yaw_deg=0.0, tex=2048, allow_4096=False,
    budget=Budget(6000, 7000, 2800, 500, 500),
    massing="custom", builder=build,
    rooms={"store": (1, 1), "upper": (3, 1), "tower": (2, 2), "back": (4, 3)},
    interior="CommercialLow", slots=SLOTS,
    ground_prefixes=GROUND,
    arrival_prefixes=("door_main", "door_wing", FIELD_FACE, "win_store"),
    # lodge.delete_lists: LOD2 = kit LOD2_DELETE + lod2_extra (lod1_extra is NOT inherited)
    lod1_extra=LOD1_EXTRA,
    lod2_extra=LOD2_EXTRA,
    # lanterncol_school: kit LOD2_DELETE drops "lanterncol_", which left the lantern cap
    # floating 1.95 m over the deck at LOD2 (buildings-review-geometry F4); lod2_keep takes
    # exact part names. fascia_uf: the monopitch's edge (revision 2) closes the roof at LOD2
    lod2_keep=(FIELD_FACE, "fascia_gf", "fascia_uf", "lanterncol_school"),
    labels=((FIELD_FACE, None, {}),),      # painter bound in _labels() (needs the class)
    cull_ok=(),
    views=(("tower", (0.85, -0.55, 0.55)), ("carpets", (0.55, 1.0, 0.45)),
           ("bubble", (1.0, 0.15, 0.30))),
    notes="civic-sign contract section 2 + skeptic M4; detail pass 1; fine-detail step 2; "
          "REVISION 2 (twentieth look): no door numbers, the photos' red markings, a 13.5 deg "
          "monopitch UF roof with a braced back overhang, lot 4 x 5, both magic carpets in the "
          "back at 12 % on closed-sided ramps (the right one a covered bubble gallery)",
    weather=None,                          # set below (WEATHER)
    weights=(),                            # set below (WEIGHTS)
    sub_objects=SUB_OBJECTS + FINE_SUB_OBJECTS + KIT_ROWS + ESTOP_ROWS,
    # fine-detail pass (opt-in; lodge.py --no-fine restores `legacy`: this revision without it)
    fine_detail=True,
    sign_set=SIGN_SET,
    kit_colour_set=KIT_COLOUR_SET,
    legacy=(("sub_objects", SUB_OBJECTS),),
)


def _disp(face, *draws, symmetric=False):
    """A "display" label entry (cs2_bldg.display via make_painter; plain data)."""
    return (face, "display", {"draws": tuple(draws), "symmetric": symmetric})


def _seed(face):
    return zlib.crc32(face.encode("utf-8")) & 0xFFFF


# Stamp table (the LEGACY / --no-signset path: the host atlas paints these faces with the kit's
# 5x7 display font; the shipping path routes them to the sign set, cs2_signset.SHEET)
LESSON_SHAPES = (("circle", 0.80, TRAIL_GREEN), ("square", 0.70, TRAIL_BLUE),
                 ("diamond", 0.86, TRAIL_BLACK), ("glyph:cand_d", 0.86, SCHOOL_RED))
DISPLAY_LABELS = (
    _disp("sign_school_face", ("text", {"lines": ("SKI SCHOOL",), "fg": WHITE,
                                        "bg": BOARD_BROWN})),
    # meeting board: the school's skier (white on red, as the tower field) + the three
    # trail-difficulty symbols, on white
    _disp("sign_meet_face", ("shapes", {"items": (
        ("square", 0.21, 0.5, 0.80, SCHOOL_RED), ("glyph:cand_d", 0.21, 0.5, 0.68, WHITE),
        ("circle", 0.525, 0.5, 0.333, TRAIL_GREEN), ("square", 0.7083, 0.5, 0.283, TRAIL_BLUE),
        ("diamond", 0.8917, 0.5, 0.383, TRAIL_BLACK)), "bg": WHITE})),
    _disp("sign_bibs_face", ("bibs", {"n": 8, "seed": _seed("sign_bibs_face"),
                                      "bg": BOARD_BROWN})),
    _disp("sign_notice_face", ("notices", {"seed": _seed("sign_notice_face")})),
) + tuple(
    _disp(f"sign_lesson{k}_face", ("shapes", {"items": ((kind, 0.5, 0.5, size, rgb),),
                                              "bg": WHITE}))
    for k, (kind, size, rgb) in enumerate(LESSON_SHAPES)
) + tuple(
    _disp(f"sign_{tag}_face", ("text", {"lines": ("STAFF", "ONLY"), "fg": WHITE, "bg": SCHOOL_RED}))
    for tag, _door in MARK_DOORS
) + (
    _disp("sign_nosled_face", ("text", {"lines": ("NO", "SLEDDING"), "fg": WHITE,
                                        "bg": SCHOOL_RED})),
) + tuple(
    _disp(f"emblem_schoolflag{s}_face", ("glyph", {"prims": "cand_d", "fg": WHITE, "frac": 0.85,
                                                   "bg": SCHOOL_RED}))
    for s in "AB"
)
# Texel-density weights (design 2.0 rule w = clamp(4 / (stroke_m x ppm_w1), 1, 3), M4):
# exact face names (longest prefix wins in lodge.part_weights); revision 2: the yard's large
# low-detail surfaces (skirts, deck, gallery) and the bigger slab top are down-weighted so the
# building's w = 1 density holds (D6 >= 40 px/m)
WEIGHTS = (("sign_school_face", 1.75), ("sign_meet_face", 1.5), ("emblem_schoolflag", 1.5)) + \
    tuple((f"sign_lesson{k}_face", 1.5) for k in range(len(LESSON_Y))) + \
    (("carpet_skirt", 0.5), ("carpetdeck_", 0.5), ("carpet_belt", 0.5), ("carpetrail_", 0.4),
     ("gallery_in", 0.25), ("gallery_", 0.4), ("galleryrib_", 0.3), ("roof_uf", 0.5),
     ("glulam_", 0.45))
# Weathering (design 3.1 overrides; DECISIONS 171: free slots + snow 0.0 are the module
# defaults, ground "grade" automatic in engine mode)
WEATHER = {"intensity": WEATHER_INTENSITY, "overrides": (
    ("carpet_belt", {"role": "pad"}),              # rubber belt: scuffs
    ("carpetrail_", {"role": "paint"}), ("cone_", {"role": "pad", "scale": 0.6}),
    ("maze_", {"role": "pad", "scale": 0.5}), ("hvac_", {"role": "galv"}),
    ("bell_", {"role": "none"}), ("stripe_", {"role": "paint", "scuff": 1.0}))}


def _labels():
    """Bind the oriented glyph painter (a callable is a legal make_painter entry)."""
    import dataclasses

    class _Lazy:
        """Defers loading _civic (bpy) until the bake calls the painter."""
        __name__ = "glyph_cand_d"

        def __call__(self, rect, base, ctl):
            return load_civic().GlyphPainter(FIELD_FACE, "cand_d", FIELD_FRAC)(rect, base, ctl)
    return dataclasses.replace(RECIPE, labels=((FIELD_FACE, _Lazy(), {}),) + DISPLAY_LABELS,
                               weights=WEIGHTS, weather=WEATHER)


RECIPE = _labels()


# ======================================================================================
# Manifest sub-object block. Detail pass 1: lodge.py writes it from Recipe.sub_objects on
# every export (lodge.sub_objects_block, skeptic S4 keys), so a re-export no longer drops
# it; merge_manifest stays as a no-op-safe script for older exports.
# ======================================================================================

def manifest_block():
    """{"sub_objects": <the block lodge.py writes>} (lodge imported lazily: needs bpy); with the
    sign set, lodge appends its identity row at export (the staged CID), so does this."""
    import dataclasses
    import lodge
    import fine_detail as FD
    rec = RECIPE
    if rec.sign_set is not None:
        ss = rec.sign_set
        cid = FD.staged_cid(ss.prop, ss.letter, kind="signs") or "0" * 32
        sm = os.path.join(_BLENDER, os.pardir, "import", ss.category, ss.letter, ss.mesh,
                          f"{ss.mesh}.manifest.json")
        bounds = None
        if os.path.isfile(sm):                      # the exported set's game-local bounds
            with open(sm, encoding="utf-8") as fh:
                bg = json.load(fh).get("bounds_game") or {}
            bounds = (bg["min"], bg["max"]) if bg else None
        rec = dataclasses.replace(rec, sub_objects=tuple(rec.sub_objects)
                                  + (FD.sign_set_row(ss, cid, bounds=bounds),))
    return {"sub_objects": lodge.sub_objects_block(rec)}


def merge_manifest(asset_dir, name="SkiSchoolCommercialLowA"):
    """Make <asset_dir>/<name>.manifest.json carry the recipe's sub-object block: no write
    when it already does (lodge.py wrote it), else add / replace it (idempotent)."""
    path = os.path.join(asset_dir, f"{name}.manifest.json")
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    block = manifest_block()
    if data.get("sub_objects") == block["sub_objects"]:
        print(f"[school] manifest sub_objects already current: {path} "
              f"({len(block['sub_objects']['objects'])} objects, no write)")
        return path
    data.update(block)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(data, fh, indent=2, sort_keys=False, default=str)
    print(f"[school] manifest sub_objects merged: {path} ({len(block['sub_objects']['objects'])} objects)")
    return path


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    if len(argv) != 2 or argv[0] != "--merge-manifest":
        print("usage: school.py -- --merge-manifest <asset_dir>")
        sys.exit(2)
    merge_manifest(os.path.abspath(argv[1]))
