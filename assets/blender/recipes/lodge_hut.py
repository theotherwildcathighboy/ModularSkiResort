"""
recipes/lodge_hut.py - L2 basic mid/top lodge, the mountain hut (SkiLodgeHut, mesh SkiLodgeHutCommercialLowB, lodges/B).
Plinth mode, no flatten.

Contract: docs/research/art/assets-2026-10/design/lodges-basic.md section 4 with design/lodges-basic-skeptic.md applied
(M1-M10) and ROUND-SPEC R21-R31. The kit API of design/bldg-kit.md wins where the two disagree (R25): kit material keys,
prefixes, LOD lists, the plinth base-mode check. Built on cs2_bldg. This module only arranges kit parts (plus four
recipe-local pieces the kit lacks, see KIT_NEEDS). Import-safe: no bpy work at import (cs2_bldg is imported inside the
builder).

FRAME (lodges-basic D4, decompile-confirmed): authoring +X = arrival = UPHILL = game +Z (the road side). z 0 = the terrain at
the lot FRONT edge (x = +4 * lot depth = +12), which is where the engine samples the placement height of a RoadSide building
(ObjectToolSystem.cs:1620-1625, BuildingUtils.cs:88-92). The 35 % design slope falls toward -X: the plinth reaches -0.35 *
(12 + 5.15) = -6.00, the deck supports -7.20.

PLINTH MODE: no vertex anywhere in |z| <= 0.01 (cs2_bldg.check_base_mode, on LOD0/1/2), so the engine finds no base edges and
draws no skirt. recipe.plinth = (7.20, 0.45) because the engine check reads (D, U) as the OVERALL min z (the deck piers). The
plinth itself stops at -6.00 (asserted here and in tests/test_lodges_basic.py). ground_prefixes in plinth mode = parts the
cull / LOD collapse must never touch: the plinth, the piers (they hold the min z on every LOD) and the decks (cull_hidden
assumes grade at z 0, which would delete deck undersides that hang 7 m above the downhill terrain).

DETAIL PASS 1 (2026-10-11, design/detail-pass-1.md 3.2 H1-H11 with the ten MUST-CHANGE items of
design/detail-pass-1-skeptic.md, the kit of build/detail-pass-1-kit.md, report build/detail-pass-1-hut.md). The pass adds:
- "Hausbank" benches (the kit's one bench, z0 = deck top, M1)
- string lights over the S deck (kit string_lights, M1: no festoon_)
- ski-deck wings beside the entry bridge with real ski gear on two racks (gear=True, M10)
- a snow-cat fuel station (horizontal drum on a cradle to below grade, upright drum on a timber crib)
- the missing S-gutter downspouts
- a RESTAURANT word board (-X, the downhill hero view, w 1.75, M4)
- a chalkboard menu (paint_glyph kind "menu", M1)
- a plain accent flag (its cloth on lod1_extra, M8)
- nosing stripes on the two step blocks
- the restaurant pictogram drawn square (no 4:1 stretch)
- the gable prisms without their interior faces (H11)

Vanilla props are Recipe.sub_objects (game frame, quaternions, 16). Weathering: recipe.weather at the highest budget-clean
step (build report). Every grade-standing new part reaches below the 35 % design grade (no band vertex). Everything else
stands on a deck.

FINE-DETAIL PASS, step 3a (2026-10-12, design/fine-detail-pass.md A.2 / B / C as cut by -skeptic.md, seventeenth look H1-H6
defaults, build/fine-detail-step3a-buildings.md). The school is the reference host (build/fine-detail-step2-engine.md section
12). RECIPE is a fine_detail.FineRecipe (fine_detail=True, sign_set=SIGN_SET). `lodge.py --no-fine` rebuilds the approved
pre-pass hut from `legacy` (byte-identical, proven in the build report):
    B.1 kit geometry  flag mast 12-seg + ball finial, the cradle drum 16-seg with chimes, and the
                      6-seg pump (drum / fuel_cradle fine=True)
    A.2 vanilla       the 2 modeled benches -> GardenBenchRandom01 (back 0.06 m off the cap
                      ring: the L1 rule + 0.01). The upright drum -> BarrelRandom01 on its timber crib
                      (the crib stays). The 2 entry wall lanterns lamp_hut0/1 ->
                      WallLightShortRangeRandom01 (H4) on the +X wall at the old lens (y, z)
    D   kit rows      the 2 modeled wing racks -> SkiDressSkiRack / V2 rows (alternating
                      loadouts, FS6) at the old rack centers, IDENTITY: the 8-slot kit rack (1.70 m)
                      does not fit along X on the 1.57 m clear wing run, so it stands along Y
                      (long axis prefab x = game x = authoring y), its +Z (front) uphill
    C   sign set      RESTAURANT (hut.name, lit), the pictogram (hut.picto) and the chalk menu
                      (hut.menu) leave the atlas for SkiSignSetHutB, a SHARER of the building sheet
                      owned by SkiSignSetSchoolB (cs2_signset, same import round S2 ->
                      `../SkiSignSetSchoolB/...`). The frames stay in the host (FS1)
    kept modeled     the 2 deck-corner post-top lanterns lamp_deckSW / NW (a wall light needs a
                      wall face >= 0.28 m wide. The rail post is 0.07 m, so there is no vanilla post lantern),
                      the string lights (no vanilla festoon), the fuel cradle + drum, the crib
Row frame: every row, old and new, is game (gx, gy, gz) = authoring (y, z, x), gx NOT negated (fine_detail.X_CONVENTION, the
hut's existing convention). Probe X2 decides. S1b mirrors on N.

NITPICK C (2026-10-13, twenty-fifth look: "The GRILL (?) on the left side of the entrance may end up in mid-air depending on
terrain. Otherwise good. I like how this one can be molded into the terrain.", build/nitpick-c-lodges.md): the fuel station
(cradle drum + pump, the crib and its barrel) was the only DRESSING standing on grade outside the plinth. On the fine build
it leaves the mesh for two ground-following BarrelRandom01 rows (FUEL_ROWS, m_ParentMesh -1: the engine snaps them to the
terrain). `--no-fine` still builds it (the approved pre-pass hut, byte-identical). The sheet's hut.picto (the fork-and-knife)
is painted blank by cs2_signset (nitpick C), RESTAURANT in Franklin Gothic Demi Cond. No recipe change for either.
"""

import math
import os
import sys

_BLENDER = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BLENDER not in sys.path:
    sys.path.insert(0, _BLENDER)
from bldg_recipes import Budget, Recipe   # noqa: E402
from fine_detail import AFRAME_RACKS, FineRecipe, SignSetSpec, kit_row   # noqa: E402

# ======================================================================================
# Constants (authoring frame, meters) - lodges-basic 4.2 unless noted
# ======================================================================================

LOT = (3, 3)                       # (W, D) cells: |y| <= 12, |x| <= 12
ANCHOR_X = 4.0 * LOT[1]            # 12.0: lot front edge = the placement-height sample
SLOPE = 0.35                       # design slope falling toward -X

PL_X0, PL_X1, PL_Y = -5.15, 8.15, 5.65          # stone plinth (incl. basement story)
PL_TOP = 0.45
PL_DEPTH = 6.00                                  # = 0.35 x (12 + 5.15) = 6.0025 (asserted 1 cm)
CAP_Z0, CAP_PROUD = 0.30, 0.08                   # plinth cap ring 0.30-0.45, 0.08 proud

FL_X0, FL_X1, FL_Y = -5.0, 8.0, 5.5              # timber story FL1 + knee wall
FL1_Z = PL_TOP                                   # 0.45
PLATE_Z = 4.45                                   # 3.0 story + 1.0 knee wall
RIDGE_RISE = 2.92
PITCH_DEG = math.degrees(math.atan(RIDGE_RISE / FL_Y))   # 27.97 (C5)
RIDGE_Z = PLATE_Z + RIDGE_RISE                   # 7.37 (slab center plane at the ridge)
EAVE_OVER, RAKE_OVER, ROOF_T = 1.20, 1.00, 0.22

DECK_TOP, DECK_T = 0.43, 0.25                    # wrap deck slab 0.18-0.43
DECK_X0, DECK_Y0, DECK_X1 = -8.0, -8.5, 6.0
SUPPORT_Z0 = -7.20                               # -0.35 x (12 + 8.2) - embed (C: overall min z)
BRIDGE_X0, BRIDGE_X1, BRIDGE_Y = 8.23, 10.96, 1.40
BRIDGE_POST_Z0 = -2.50
STEP_RISE = round(DECK_TOP / 3.0, 4)             # 3 risers 0.1433 down to the anchor terrain
STEP_X = (BRIDGE_X1, 11.38, 11.80)               # two step blocks, foot at x 11.80
STEP_Z0 = -0.60

WIN_SILL, WIN_W, WIN_H = 1.35, 1.0, 1.3          # FL1 windows (shutters each side)
GDOOR_W, GDOOR_H, GDOOR_SILL = 0.9, 2.2, 0.50    # glazed doors onto the deck
ATT_W, ATT_H, ATT_SILL = 0.8, 1.1, 4.80          # attic pair in each gable
BAS_SILL, BAS_H = -2.00, 1.40                    # basement openings (inside the plinth)
BAS_DOOR_Z0, BAS_DOOR_H = -2.60, 2.0
# Basement-door stair (revision 1, lodges-basic O4: the door floated above grade on slopes > ~15 %). It has a landing at the
# door sill, then a straight plinth-mode stair (ground=False, foot below the 35 % design grade) running -Y along the -X
# plinth face, under the wrap deck (headroom 2.78 at the landing). Rails are recipe-local (`trail_bas*`). The kit rail()
# lifts every point to GROUND_LIFT. Landing posts use box_part (kit need K2).
BAS_LAND_X0 = PL_X0 - 1.50                       # landing -6.65 .. -5.15 (the plinth face)
BAS_LAND_Y = 1.00                                # landing y -1.00 .. +1.00
BAS_LAND_T = 0.20
BAS_STAIR_W = 1.20
BAS_STAIR_X = PL_X0 - 0.75                       # stair center line x -5.90
BAS_STAIR_RISE, BAS_STAIR_RUN = 0.18, 0.28
BAS_STAIR_FOOT_Z = round(-SLOPE * (ANCHOR_X - BAS_STAIR_X) - 0.05, 3)   # -6.315
BAS_STAIR_N = math.ceil((BAS_DOOR_Z0 - BAS_STAIR_FOOT_Z) / BAS_STAIR_RISE - 1e-9)   # 21
BAS_STAIR_FOOT_Y = -BAS_LAND_Y - BAS_STAIR_N * BAS_STAIR_RUN                       # -6.88
BAS_RAIL_H = 1.00
# ---- T58 / T61 (2026-10-18, structure v1: cs2_bldg.STRUCTURE["version"] >= 1, lodge.py --structure v1. Off = the close-look
# hut byte for byte. docs/research/art/structure-2026-10-18.md) ------------------------------------------ T61 (Blender
# review 2026-10-17 item 6: "I imagine a build this into a steep hillside ... the stairs might leave some gaps ... extend the
# front steps down more"): the two stone step blocks stopped 0.60 m under the anchor terrain, so on any hillside steeper than
# ~60 % their undersides hung in the air beside the bridge. With structure v1 they run down to STEP_Z0_V1 (the old bridge /
# wing posts' depth): buried on the 35 % design slope, a stone stair base on a steeper one (covered down to ~240 % at the
# bridge end). The bridge end and both ski-deck wings stand on one stone ABUTMENT `pier_hutentry` (x WING_X0 .. BRIDGE_X1,
# |y| <= WING_Y, down to STEP_Z0_V1) instead of six thin timber posts over the void (col_bridge, col_ski: the "gap" under the
# entry in his sketch). So the bridge spans only plinth -> abutment (1.1 m). The wrap deck's six posts stand in concrete
# FOOTINGS (`footing_hut`): FOOT_W square round each post foot, from FOOT_DOWN under the design grade to FOOT_UP over it at
# the post's highest corner (never under SUPPORT_Z0: the plinth-mode contract's overall min z). The posts start 5 cm inside
# them.
STEP_Z0_V1 = -2.50
FOOT_W, FOOT_DOWN, FOOT_UP = 0.36, 0.30, 0.20
# T58: the basement-stair rail posts stood 5 mm OFF the outer stringer with their feet hanging beside the flight (0.35 .. 3.0
# m over grade). With structure v1 the rail line moves onto the stringer's outer face, so every post is bolted to its side
# (cs2_fittings SIDE_MOUNT)
BAS_RAIL_X_V1 = BAS_LAND_X0 + 0.09 - 0.035      # outer stringer face x -6.56, posts 0.07 square
BAS_POST_DROP_V1 = 0.30                          # the posts run 0.30 m down the stringer's side
SHUTTER_W, SHUTTER_T = 0.50, 0.04
STOVE_XY, STOVE_TOP = (1.50, -3.20), 8.60

# ---- detail pass 1 (design 3.2, positions in the authoring frame) ------------------------
INSET = 0.05                                     # rail line inside a deck edge (as the wrap deck)
BENCH_Y = -(PL_Y + CAP_PROUD)                    # -5.73: H1 bench back line = the cap-ring face
BENCHES = (("hutS0", -3.0, 3.4), ("hutS1", 1.5, 1.8))   # (name, x center, length) on the S deck
#   hutS1 is 1.8 m (design 2.0) and centered at 1.5: clear of the deck chair at x 0 (0.40) and
#   the firewood pile (x >= 2.58, FirewoodPile02) by >= 0.18 m
FESTOON_X = (-4.0, 0.6, 5.2)                     # H2 string-light poles on the S deck
FESTOON_Y, FESTOON_Z = -8.30, 2.93               # 0.15 inside the rail, wire anchors 2.93
FESTOON_SAG, FESTOON_EVERY = 0.25, 0.77          # 6 bulbs per 4.6 m span (12)
WING_X0, WING_Y = 9.30, 3.80                     # H3 ski-deck wings x 9.30..10.96, |y| 1.40..3.80
WING_POSTS_X = (9.40, 10.86)                     # col_ski at |y| 3.70, 0.20 sq, to BRIDGE_POST_Z0
RACK_X, RACK_Y, RACK_SLOTS = 10.13, 2.60, 6      # H4 racks (6 slots, 1.30 m) on the wings
FUEL_X, FUEL_Y, FUEL_Y2 = 11.0, -4.9, -5.9       # H5 fuel station, uphill side S of the steps
FUEL_ZC, FUEL_LEG_TO = 0.80, -0.90               # drum center, cradle legs below the design grade
CRIB_Z0, CRIB_TOP, CRIB_HALF = -0.60, 0.12, 0.40   # timber crib under the upright drum
DS_S_Y = -(PL_Y + CAP_PROUD + 0.07)              # H6 -5.80: S downspouts (mirror of the +Y pair)
NAME_SIGN = dict(u=0.0, z=3.30, w=5.0, h=0.80)   # H7 RESTAURANT, -X story knee band
MENU_SIGN = dict(u=1.30, z=1.10, w=0.55, h=0.80)  # H8 menu, +X between entry and E1 shutter
FLAG_XY, FLAG_H, FLAG_CLOTH = (5.75, -8.25), 7.5, (1.4, 0.9)   # H9 S deck SE corner
STRIPE_IN = 0.045                                # H10 stripe center inside the nosing (0.02 bevel)
# label colors (encoded sRGB, = cs2_bldg BOARD_BROWN_SRGB / HUT_RED_SRGB / CHALK_SRGB)
BOARD_BROWN, HUT_RED, CHALK = (0.20, 0.14, 0.10), (0.62, 0.10, 0.08), (0.10, 0.22, 0.16)
# Real ski gear on the wing racks (cs2_bldg.ski_rack_frame gear=True, M10 / L1 verdict 12'): (slot, side, kind, color,
# length). Side +1 = -Y (toward the bridge), -1 = +Y on an X rack.
_A, _W, _K, _B, _R = "accent", "trim_white", "ski_black", "ski_blue", "ski_red"
# 9 items on 2 x 6 slots: the M10 cap (LOD0 about 5,300) - the first build with 7 slots and 11
# items came to 5,390 (racks 956 tris of the 1,711 new). Detail pass 1b (2026-10-11): no
# bindings on the skis / boards (ninth look), and the two pole pairs are 1.36 / 1.38 m
# (were 1.20 / 1.25): a rack item must span the lean, cs2_bldg.rack_reach() = 1.33 m
# (detail pass 1 integration F1)
RACK_GEAR = {
    "wingN": ((0, 1, "ski", (_W, _B), 1.70), (1, 1, "board", (_A, _K), 1.52),
              (3, 1, "ski", (_K, _A), 1.64), (2, -1, "ski", (_B, _W), 1.66),
              (5, -1, "poles", _K, 1.36)),
    "wingS": ((1, 1, "ski", (_A, _K), 1.68), (2, 1, "poles", _A, 1.38),
              (4, 1, "board", (_W, _R), 1.50), (0, -1, "ski", (_K, _W), 1.62)),
}

# _Win room cells (lodges-basic 4.5): id = row * 5 + col, all distinct (C9)
ROOMS = {"fl1E": (1, 0), "fl1W": (2, 0), "fl1S": (3, 0), "fl1N": (4, 0), "attE": (0, 1),
         "attW": (1, 1), "basW": (2, 1), "basS": (3, 1), "basN": (4, 1)}

# Every part name must start with one of these (C11). Kit vocabulary (bldg-kit section 5).
PREFIXES = ("plinth_", "plinthcap_", "wall_", "gable_", "roof_", "soffit_", "rake_",
            "fascia_", "ridge_", "tail_", "bracket_", "snowguard_", "gutter_", "downspout_",
            "stovepipe_", "win_", "sill_", "head_", "jamb_", "mull_", "trim_", "entry_",
            "door_", "lamp_", "sign_", "deck_", "deckfascia_", "trail_", "pier_", "col_",
            "step_", "stair_", "footing_",
            # detail pass 1 (design 3.2 C11 row as corrected by skeptic M1: no festoon_, the string lights are lamp_, no
            # emblem_ on L2)
            "bench_", "skirack_", "drum_", "flag_", "flagpanel_", "stripe_")

AREA_CAP_M2 = 1650.0               # C10 main-mesh face area after the cull (detail pass 1:
#                                    1600 -> 1650 for +60 m2 of dressing, kept 1,620 m2. The
#                                    texel gate is D6 w = 1 >= 35 px/m, test_detail_hut)
SUM_W2A_CAP = 1100.0               # C10 (printed, WARN)

def _yaw_q(deg):
    """Unity rotation about the game +Y axis by `deg` as (x, y, z, w): local +Z (a wall prop's outward axis) -> game (sin, 0,
    cos). 0 = authoring +X, 180 = -X, 90 = +Y, -90 = -Y."""
    h = math.radians(deg) / 2.0
    return (0.0, round(math.sin(h), 7), 0.0, round(math.cos(h), 7))


# Vanilla props for prefab surgery (ObjectSubObjects, GAME local (gx, gy, gz) = authoring (y, z, x), rotation (x, y, z, w),
# m_ParentMesh 0 = on our mesh: every L2 prop stands on a deck or hangs on a wall, nothing on the unflattened terrain).
# lodges-basic 4.7 (parasol yaw 180: Parasol01 reaches 2.83 m along local +Z) + detail pass 1 (design 3.2, 8 -> 16, the two
# ski-rack spots are gone: the racks are modeled, H4). Placement deltas against the design (render bounds of every variant
# from %TEMP%\pltar\dump.zip, build report section 3):
#   - FirewoodPile at authoring y -6.45 (design -6.10): FirewoodPile03 is +-0.65 deep and
#     would reach y -5.45, 5 cm into the S wall and its window sill board. At -6.45 it ends
#     0.07 m off the cap ring (y -5.73).
#   - the 2nd DeckChair moves to the W deck (x -6.6, y -4.25, yaw 180, facing the view): at
#     its old spot (x 3.0, y -7.2) DeckChair02 / 03 (+-1.07 / 1.19 long) overlap the firewood.
#   - GasHeaters at y -7.7 x -2.8 / 3.2 as designed (now clear of every chair).
#   - wall lights over the glazed doors at z 3.05 (design 2.85: inside the door head board
#     2.70-2.90). The basement-door light at y 0.95 (design 0.85: 1 cm off the surround).
_TABLE, _PARASOL = "fc058a3d03b337e4794c432a84170316", "b7e2343fd7ff9f642b77084a04738e43"
_CHAIR, _FIREWOOD = "63c46490ab59afd4dae01ee442f1e462", "0d01c98e6f7cd9b44ac31084b6ba7ccd"
_BIN, _HEATER = "41e3c9ba6e1d6b948a6b1bc3499a5397", "0bbcb0620e01694448cb78385580351b"
_WALLLIGHT, _VENT = "cfc12fde01d9da24b86ddedd657eaae1", "b99325ff4ff4dfd459734d8f765c2597"
SUB_OBJECTS = (
    ("TableSetCommercialRandom01", _TABLE, (-6.5, 0.43, -6.6), _yaw_q(0), 0),
    ("TableSetCommercialRandom01", _TABLE, (-2.0, 0.43, -6.6), _yaw_q(0), 0),
    ("TableSetCommercialRandom01", _TABLE, (2.5, 0.43, -6.6), _yaw_q(0), 0),
    # T26 dropped it. Restored 2026-10-17. Fittings round (2026-10-17): 1.37 m (PARASOL_OFFSET, the vanilla table-set
    # convention) behind its table at y -2.0 along +Y, local +Z at the table: the 3 m W deck leaves no room across x (the
    # canopy would enter the wall or the pole the rail), and on the -Y side the pole stands clear of the deck chair (y -4.25)
    # and of the W deck door (y -0.7 .. 0.7)
    ("ParasolRandom01", _PARASOL, (-3.37, 0.43, -6.6), _yaw_q(90), 0),
    ("DeckChairRandom01", _CHAIR, (-7.2, 0.43, 0.0), _yaw_q(-90), 0),
    ("DeckChairRandom01", _CHAIR, (-4.25, 0.43, -6.6), _yaw_q(180), 0),
    ("FirewoodPileRandom01", _FIREWOOD, (-6.45, 0.43, 4.2), _yaw_q(90), 0),
    ("TrashbinRandom01", _BIN, (1.0, 0.43, 10.4), _yaw_q(0), 0),
    # winter-only patio heaters (WeatherObject m_RequireSnow) on the S deck
    ("GasHeaterRandom01", _HEATER, (-7.7, 0.43, -2.8), _yaw_q(0), 0),
    ("GasHeaterRandom01", _HEATER, (-7.7, 0.43, 3.2), _yaw_q(0), 0),
    # light pools at the deck doors and the basement door (EffectSource + StreetLightObject)
    ("WallLightShortRangeRandom01", _WALLLIGHT, (1.2, 3.05, -5.0), _yaw_q(180), 0),
    ("WallLightShortRangeRandom01", _WALLLIGHT, (-1.2, 3.05, -5.0), _yaw_q(180), 0),
    ("WallLightShortRangeRandom01", _WALLLIGHT, (-5.5, 3.05, -0.5), _yaw_q(-90), 0),
    ("WallLightShortRangeRandom01", _WALLLIGHT, (0.95, -0.75, -5.15), _yaw_q(180), 0),
    ("ACVentWallRandom01", _VENT, (5.5, 3.5, -4.3), _yaw_q(90), 0),
    ("ACVentWallRandom01", _VENT, (5.5, 3.5, 3.25), _yaw_q(90), 0),
)

# ---- fine-detail pass step 3a (module docstring): the rows that replace modeled parts ---------- (same frame and tuple form
# as SUB_OBJECTS. Every GUID in cs2_liftdress.SUBOBJ, dump-verified)
_GBENCH, _BARREL = "8df0703be09426948b5bc3262d86bc04", "fb4247f7a6f75b1478d9dd01ac88ce86"
GBENCH_HALF_DEPTH = 0.496          # GardenBenchRandom01 union z +-0.496 (x +-1.059, h 1.054)
# bench x: hutS0 on the old center -3.0. hutS1 at 1.49 (old 1.5) = the middle of the free run between DeckChair #5 (x <=
# 0.40) and the FirewoodPile (x >= 2.58): the 2.12 m vanilla bench clears both by 0.031 m (union bounds of every variant)
GBENCH_X = (("hutS0", -3.0), ("hutS1", 1.49))
# back 0.06 m off the cap ring (the L1 rule is 0.05, +0.01 so test_detail_hut D10's +0.05 m render box clears the cap ring's
# face): 0.01 m in front of the removed benches' back plane (-5.78)
GBENCH_Y = round(BENCH_Y - 0.06 - GBENCH_HALF_DEPTH, 4)      # -6.286
ENTRY_LAMP_U = 0.6 + 0.2 + 0.35    # cs2_bldg.entry: lamps at u +-(w / 2 + 0.2 + 0.35), w 1.2
ENTRY_LAMP_Z = FL1_Z + 2.2         # 2.65: the old lens center height (entry lamps at z + 2.2)
# nitpick C (2026-10-13, twenty-fifth look, L2: "The GRILL (?) on the left side of the entrance may end up in mid-air
# depending on terrain. Otherwise good.", build/nitpick-c-lodges.md): the snow-cat fuel station (H5: the horizontal drum on
# its cradle + pump, read as a grill, and the upright barrel on its timber crib) stood on GRADE left of the entry steps (seen
# from the arrival), held below the 35 % DESIGN grade by fixed legs / an open-bottom crib. On any other slope it floats or
# sinks (the hut itself is plinth mode, no flatten). It leaves the mesh for two GROUND-FOLLOWING vanilla barrels at the old
# drum centers. m_ParentMesh -1 sets SubObjectFlags.OnGround, and the engine drops each onto the terrain under it
# (SubObjectSystem.cs 2523-2545 + ObjectUtils.AdjustPosition). Static props get GeometryFlags.Overridable
# (ObjectInitializeSystem.cs 610, decompile 1.6.2f1) [U: first in-game look]. gy 0 (OnGround discards the local y). The
# crib's barrel row (parent 0 on the crib top 0.12) is the second one.
FUEL_ROWS = tuple(("BarrelRandom01", _BARREL, (y, 0.0, FUEL_X), _yaw_q(0), -1) for y in (FUEL_Y, FUEL_Y2))
FINE_SUB_OBJECTS = tuple(
    ("GardenBenchRandom01", _GBENCH, (GBENCH_Y, DECK_TOP, x), _yaw_q(-90), 0) for _t, x in GBENCH_X
) + FUEL_ROWS + tuple(
    ("WallLightShortRangeRandom01", _WALLLIGHT, (sg * ENTRY_LAMP_U, ENTRY_LAMP_Z, FL_X1), _yaw_q(0), 0)
    for sg in (-1.0, 1.0)                                                         # lamp_hut0 / 1
)
# kit color variants (2026-10-12 ~18:50, build/kit-colour-variants.md): the hut red (cs2_bldg.HUT_RED_SRGB) against the green
# trim and dark timber
KIT_COLOUR_SET = "red"
# the wing racks (wingN = +Y, wingS = -Y) as kit rows on the wing decks, alternating loadouts
KIT_ROWS = tuple(
    kit_row(AFRAME_RACKS[i % len(AFRAME_RACKS)], (sg * RACK_Y, DECK_TOP, RACK_X), parent=0,
            note=f"replaces skirack_{tag} (ski_rack_frame slots {RACK_SLOTS} along X at authoring "
                 f"({RACK_X}, {sg * RACK_Y})); the kit's 1.70 m rack stands along Y (identity)",
            colour_set=KIT_COLOUR_SET)
    for i, (tag, sg) in enumerate((("wingN", 1.0), ("wingS", -1.0))))
SIGN_SET = SignSetSpec(
    prop="SkiSignSetHut", letter="B", host="hut", owner=False,
    faces=(("sign_hutname_face", "hut.name"), ("sign_hut_face", "hut.picto"),
           ("sign_hutmenu_face", "hut.menu")))

KIT_NEEDS = (
    "col() lifts any z0 < GROUND_LIFT to 0.02, so below-grade timber posts (plinth mode) are "
    "box_part('col_...') here",
    "rail() lifts every path point to GROUND_LIFT, so the below-grade basement-stair and "
    "landing rails are recipe-local (_rail_low, `trail_bas*`)",
    "entry() lifts its floor to GROUND_LIFT, so the basement door (-2.60) is a recipe-local "
    "surround + cs2_kit.door",
    "downspout() clamps z_bottom >= 0.05; the plinth-face run to -6.00 is a second "
    "`downspout_<n>ext` box",
    "no shutter builder: shutters are `trim_sh*` wall_boxes",
    "cull_hidden assumes grade at z 0 in plinth mode (deck undersides 7 m above the downhill "
    "terrain would go): decks are listed in ground_prefixes to protect them",
    "the buried plinth band below -2 m keeps w 1.0 (lodges-basic asked 0.7): recipe.weights "
    "(detail pass 1) is per PART and the plinth is one part",
    "ski_rack_frame / bench / string_lights / flag_mast stand on a deck through z0 (detail "
    "pass 1); grade-standing dressing (wing posts, fuel cradle, crib) reaches below the "
    "design grade by explicit boxes / leg_to",
    "slate is slot 0 (not recolorable); slot 3 only reaches the metal_roof entry canopy",
)


def terrain_z(x):
    """Design-slope terrain under authoring x (z 0 at the anchor x = +12)."""
    return -SLOPE * (ANCHOR_X - x)


def _post_z0(K, x, z0):
    """T61 (structure v1): a deck / bridge / wing post starts 5 cm inside its footing's top (the footing carries it, and the
    buried length it no longer has is the footings' tri budget). Off: the old foot z0."""
    if K.STRUCTURE.get("version", 0) >= 1:
        return terrain_z(x + FOOT_W / 2) + FOOT_UP - 0.05
    return z0


def _roof_spec(K):
    return K.RoofSpec("gable", FL_X0, FL_X1, -FL_Y, FL_Y, PLATE_Z, PITCH_DEG, axis="X",
                      overhang=EAVE_OVER, rake_over=RAKE_OVER, thick=ROOF_T, mat="slate")


def _shutters(K, name, wall, a0, a1, z0, h):
    """`trim_sh<name>`: one shutter each side of a framed window (outside the jambs)."""
    j = K.jamb_width()                         # jamb width (cs2_bldg.window_bay, v2 casing 0.07)
    boxes = [(a0 - j - SHUTTER_W, a0 - j, z0, z0 + h, 0.0, SHUTTER_T),
             (a1 + j, a1 + j + SHUTTER_W, z0, z0 + h, 0.0, SHUTTER_T)]
    return K.wall_boxes(f"trim_sh{name}", wall, boxes, "trim")


def _window(K, name, wall, u, win, room, shutters=False, sill_board=True):
    parts = K.window_bay(name, wall, u - win.w / 2, u + win.w / 2, win, room=room,
                         sill_board=sill_board)
    if shutters:
        parts += _shutters(K, name, wall, u - win.w / 2, u + win.w / 2, win.sill, win.h)
    return parts


def _rail_low(K, name, path, h=BAS_RAIL_H, post_every=1.8, post_drop=0.0):
    """Timber guard rail along a 3D polyline that may run BELOW grade (plinth mode: the kit rail() lifts every point to
    GROUND_LIFT). It has posts 0.07 square (`trail_<name>p`) at every vertex and at most `post_every` apart. Two rails 0.09 x
    0.06 at h - 0.08 and h - 0.54 run along each segment (`trail_<name>r`) - the kit's timber-rail dimensions. post_drop
    (T58, structure v1): the posts of the FIRST segment (the flight) start this far under the path, bolted along the side of
    the stringer they stand beside."""
    from mathutils import Vector
    pts = [Vector(p) for p in path]
    posts, rails = K._Geo(), K._Geo()
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        n = max(1, int(math.ceil((b - a).length / post_every - 1e-9)))
        for j in range(n + (1 if i == len(pts) - 2 else 0)):
            q = a + (b - a) * (j / n)
            posts.aabb((q.x - 0.035, q.y - 0.035, q.z - (post_drop if i == 0 else 0.0)), (q.x + 0.035, q.y + 0.035, q.z + h))
        for zz in (h - 0.08, h - 0.54):
            up = Vector((0.0, 0.0, zz))
            K._beam_into(rails, a + up, b + up, 0.09, 0.06)
    return [(posts.finish(f"trail_{name}p"), "trim"), (rails.finish(f"trail_{name}r"), "trim")]


def _basement_stair(K):
    """O4 fix (revision 1): landing `deck_bas` at the basement-door sill, plinth-mode stair
    `stair_bas` (ground=False, foot BAS_STAIR_FOOT_Z below the 35 % design grade) down the -X
    plinth face toward -Y, `col_baslanding` posts to below grade, one `trail_bas` rail on the
    open side (landing +Y edge, landing -X edge, stair outer stringer)."""
    z_top = BAS_DOOR_Z0
    P = K.deck("bas", BAS_LAND_X0, PL_X0, -BAS_LAND_Y, BAS_LAND_Y, z_top, support="none",
               fascia=0.0, board_dir="Y", thick=BAS_LAND_T)
    foot = (BAS_STAIR_X, BAS_STAIR_FOOT_Y, BAS_STAIR_FOOT_Z)
    top = (BAS_STAIR_X, -BAS_LAND_Y, z_top)
    P += K.stair("bas", foot, top, w=BAS_STAIR_W, rise_max=BAS_STAIR_RISE, rail="none",
                 ground=False, tread_mat="deck", stringer_mat="timber_heavy")
    zu = z_top - BAS_LAND_T
    px = BAS_LAND_X0 + 0.10
    P += K.boxes_part("col_baslanding",
                      [((px - 0.08, y - 0.08, terrain_z(px) - 0.10), (px + 0.08, y + 0.08, zu))
                       for y in (-0.45, BAS_LAND_Y - 0.10)], "timber_heavy")
    rise = (z_top - BAS_STAIR_FOOT_Z) / BAS_STAIR_N
    rx = BAS_LAND_X0 + 0.05                    # outside the outer stringer (x -6.56)
    drop = 0.0
    if K.STRUCTURE.get("version", 0) >= 1:     # T58: on the stringer's outer face (side-mounted posts)
        rx, drop = BAS_RAIL_X_V1, BAS_POST_DROP_V1
    P += _rail_low(K, "bas", [(rx, BAS_STAIR_FOOT_Y + BAS_STAIR_RUN, BAS_STAIR_FOOT_Z + rise),
                              (rx, -BAS_LAND_Y, z_top), (rx, BAS_LAND_Y - 0.05, z_top),
                              (PL_X0 - 0.05, BAS_LAND_Y - 0.05, z_top)], post_drop=drop)
    return P


def _wings(K):
    """H3 ski-deck wings either side of the entry bridge (x WING_X0..BRIDGE_X1, |y| 1.40..
    3.80) at the bridge top: `deck_ski<N|S>` slab (its inner end face dropped: it abuts the
    bridge slab), `deckfascia_ski<N|S>` on the three free edges (the inner one is the bridge's
    own fascia), `col_ski<N|S>` two posts 0.20 sq at the outer corners down to
    BRIDGE_POST_Z0 (below the 35 % design grade). Slabs are ground parts (deck_)."""
    P = []
    zu = DECK_TOP - DECK_T
    zb = zu - 0.30 + 0.16                     # the kit deck() fascia band (0.04 .. 0.41)
    zt = DECK_TOP - 0.02
    for tag, sg in (("N", 1.0), ("S", -1.0)):
        y0, y1 = sorted((sg * BRIDGE_Y, sg * WING_Y))
        slab = K.box_part(f"deck_ski{tag}", (WING_X0, y0, zu), (BRIDGE_X1, y1, DECK_TOP), "deck",
                          drop=("-Y" if sg > 0 else "+Y",))
        slab[0][0]["tile_rot"] = 90           # boards along Y, as the bridge (board_dir "Y")
        P += slab
        yo0, yo1 = sorted((sg * WING_Y, sg * (WING_Y + 0.04)))
        ya0, ya1 = sorted((sg * (BRIDGE_Y + 0.04), sg * WING_Y))   # clear of the bridge fascia
        P += K.boxes_part(f"deckfascia_ski{tag}",
                          [((WING_X0 - 0.04, yo0, zb), (BRIDGE_X1 + 0.04, yo1, zt)),
                           ((WING_X0 - 0.04, ya0, zb), (WING_X0, ya1, zt)),
                           ((BRIDGE_X1, ya0, zb), (BRIDGE_X1 + 0.04, ya1, zt))], "trim")
        if K.STRUCTURE.get("version", 0) >= 1:     # T61: the wings stand on the stone entry abutment
            continue
        yc = sg * (WING_Y - 0.10)
        P += K.boxes_part(f"col_ski{tag}", [((x - 0.10, yc - 0.10, _post_z0(K, x, BRIDGE_POST_Z0)),
                                             (x + 0.10, yc + 0.10, zu)) for x in WING_POSTS_X],
                          "timber_heavy")
    return P


def _detail1(K, fine=False):
    """Detail pass 1 dressing (design 3.2 H1, H2, H4-H10. H3 is _wings + the bridge rails in build_hut, H11 the gable_roof
    keyword). Returns [(obj, matkey)]. fine (fine-detail step 3a): the benches, the wing racks and the upright drum are rows
    (FINE_SUB_OBJECTS / KIT_ROWS), the cradle drum / pump and the flag mast take the kit's fine=True geometry."""
    WallRef = K.WallRef
    fk = {"fine": True} if fine else {}
    P = []
    # H1 "Hausbank" benches against the cap ring on the S deck (kit bench, M1: z0, back)
    if not fine:                                   # fine: GardenBenchRandom01 rows
        for tag, x, length in BENCHES:
            P += K.bench(tag, x, BENCH_Y, "-Y", length=length, z0=DECK_TOP)
    # H2 string lights over the S deck: poles on the deck, 12 emissive bulbs (lamp_hutS)
    P += K.string_lights("hutS", [(x, FESTOON_Y, FESTOON_Z, DECK_TOP) for x in FESTOON_X],
                         sag=FESTOON_SAG, bulb_every=FESTOON_EVERY)
    # H4 real ski gear on two 7-slot racks on the wings (M10)
    if not fine:                                   # fine: the kit rack rows (KIT_ROWS)
        for tag, sg in (("wingN", 1.0), ("wingS", -1.0)):
            P += K.ski_rack_frame(tag, RACK_X, sg * RACK_Y, slots=RACK_SLOTS, along="X",
                                  gear=True, skis=RACK_GEAR[tag], z0=DECK_TOP)
    # H5 snow-cat fuel station: horizontal drum on a cradle (legs below the design grade),
    # upright drum on a timber crib (open bottom below grade, top 0.12). fine (nitpick C, 2026-10-13): the whole
    # station leaves the mesh for two ground-following BarrelRandom01 rows (FUEL_ROWS, m_ParentMesh -1): on grade
    # beside the steps it floated / sank wherever the slope is not the 35 % design ("the grill (?) ...
    # may end up in mid-air")
    if not fine:
        P += K.drum("hutfuel", (FUEL_X, FUEL_Y, FUEL_ZC), "X", **fk)
        P += K.fuel_cradle("hutfuel", FUEL_X, FUEL_Y, "X", FUEL_ZC - 0.30, FUEL_LEG_TO, **fk)
        P += K.box_part("drum_hutfuel2base", (FUEL_X - CRIB_HALF, FUEL_Y2 - CRIB_HALF, CRIB_Z0),
                        (FUEL_X + CRIB_HALF, FUEL_Y2 + CRIB_HALF, CRIB_TOP), "timber_heavy",
                        drop=("-Z",))
        P += K.drum("hutfuel2", (FUEL_X, FUEL_Y2), z0=CRIB_TOP)
    # H6 the S gutter's outlets (mirror of the +Y pair): SW onto the S deck, SE down the
    # plinth face to the plinth foot
    g_y = FL_Y + EAVE_OVER + 0.20
    for tag, x, zb in (("SW", FL_X0 + 0.10, DECK_TOP + 0.05), ("SE", FL_X1 - 0.10, 0.05)):
        P += K.downspout(f"hut{tag}", x, DS_S_Y, 3.80, "-Y", wall_gap=g_y + DS_S_Y,
                         z_bottom=zb, shoe=(tag == "SW"))      # fittings v1: SE runs on down the plinth
    xe = FL_X1 - 0.10
    P += K.box_part("downspout_hutSEext", (xe - 0.04, DS_S_Y - 0.04, -PL_DEPTH),
                    (xe + 0.04, DS_S_Y + 0.04, 0.06), "galv")
    # H7 RESTAURANT word board (the downhill hero view) and H8 the chalkboard menu
    P += K.sign_board("hutname", WallRef("-X", FL_X0), NAME_SIGN["u"], NAME_SIGN["z"],
                      NAME_SIGN["w"], NAME_SIGN["h"], mat="timber_heavy")
    P += K.sign_board("hutmenu", WallRef("+X", FL_X1), MENU_SIGN["u"], MENU_SIGN["z"],
                      MENU_SIGN["w"], MENU_SIGN["h"], mat="timber_heavy")
    # H9 plain accent flag (slot 2) on the S deck SE corner. The cloth leaves with the mast
    P += K.flag_mast("hut", FLAG_XY[0], FLAG_XY[1], h=FLAG_H, flag=FLAG_CLOTH, z0=DECK_TOP, **fk)
    # H10 nosing stripes on the two step blocks (yellow geometry, 10 tris each)
    for k in range(2):
        top = round(DECK_TOP - (k + 1) * STEP_RISE, 4)
        xs = STEP_X[k + 1] - STRIPE_IN
        P += K.safety_stripe(f"hut{k + 1}", (xs, -BRIDGE_Y + INSET), (xs, BRIDGE_Y - INSET), top)
    return P


def build_hut(recipe, opts):
    """The L2 parts list [(obj, matkey)] (recipe.builder for massing "custom")."""
    import cs2_bldg as K
    import cs2_kit
    WallRef, WinSpec = K.WallRef, K.WinSpec
    assert abs(SLOPE * (ANCHOR_X - PL_X0) - PL_DEPTH) < 0.01, PL_DEPTH
    assert abs(RIDGE_Z - 7.37) < 1e-9 and abs(PITCH_DEG - 27.97) < 0.01, (RIDGE_Z, PITCH_DEG)
    fine = bool(getattr(recipe, "fine_detail", False))     # fine-detail step 3a (docstring)
    P = []

    # ---- stone plinth (open bottom, top under the timber story) + cap ring ------------
    P += K.plinth("hut", PL_X0, PL_X1, -PL_Y, PL_Y, PL_DEPTH, PL_TOP, "stone_rubble",
                  built_on=True)
    cx0, cx1, cy = PL_X0 - CAP_PROUD, PL_X1 + CAP_PROUD, PL_Y + CAP_PROUD
    cap = K.box_part("plinthcap_hut", (cx0, -cy, CAP_Z0), (cx1, cy, PL_TOP), "concrete")
    K.bisect_part(cap[0][0], [((FL_X0, 0, 0), (1, 0, 0)), ((FL_X1, 0, 0), (1, 0, 0)),
                              ((0, -FL_Y, 0), (0, 1, 0)), ((0, FL_Y, 0), (0, 1, 0))])
    P += cap
    # ---- timber story (3.0) + knee wall (1.0) as one story. Gables come with the roof ----
    P += K.storey("hut", FL_X0, FL_X1, -FL_Y, FL_Y, FL1_Z, PLATE_Z, "timber_lap",
                  drop=("-Z", "+Z"))
    corners = [(x, y) for x in (FL_X0, FL_X1) for y in (-FL_Y, FL_Y)]
    P += K.boxes_part("trim_corners", [((x - 0.09, y - 0.09, FL1_Z), (x + 0.09, y + 0.09, PLATE_Z))
                                       for x, y in corners], "trim")
    # ---- roof: 28 deg gable along X, deep eaves, shake gable triangles ------------------
    spec = _roof_spec(K)
    P += K.gable_roof("hut", spec, tails=None, snow_guard=0, gutters=False, walls=True,
                      wall_mat="timber_shake", wall_inner=False)     # H11: no interior faces
    for tag, side in (("S", "-Y"), ("N", "+Y")):
        P += K.snow_guard(f"hut{tag}", spec, side, offset=1.0)
    g_y = FL_Y + EAVE_OVER + 0.20             # outside the fascia (y 6.71-6.83)
    for tag, sg in (("S", -1), ("N", 1)):
        P += K.gutter(f"hut{tag}", (FL_X0 - RAKE_OVER, sg * g_y), (FL_X1 + RAKE_OVER, sg * g_y), 3.80)
    # downspouts on +Y only: down the timber wall, past the cap, down the plinth face to -6.0
    ds_y = PL_Y + CAP_PROUD + 0.07            # 5.80: clear of the cap ring
    for tag, x in (("NW", FL_X0 + 0.10), ("NE", FL_X1 - 0.10)):
        P += K.downspout(f"hut{tag}", x, ds_y, 3.80, "+Y", wall_gap=g_y - ds_y, z_bottom=0.05,
                         shoe=False)          # fittings v1: no shoe, the pipe runs on down the plinth
        P += K.box_part(f"downspout_hut{tag}ext", (x - 0.04, ds_y - 0.04, -PL_DEPTH),
                        (x + 0.04, ds_y + 0.04, 0.06), "galv")
    # purlin ends (3 per gable) + knee brackets (4 per gable) under the rakes
    t = math.tan(math.radians(PITCH_DEG))
    half_t = 0.5 * ROOF_T * math.sqrt(1.0 + t * t)

    def underside(y):
        return PLATE_Z + t * (FL_Y - abs(y)) - half_t

    for tag, x, sgn in (("E", FL_X1, 1.0), ("W", FL_X0, -1.0)):
        boxes = []
        for y in (-2.75, 0.0, 2.75):
            zt = underside(y)
            xa, xb = sorted((x, x + sgn * (RAKE_OVER - 0.05)))
            boxes.append(((xa, y - 0.10, zt - 0.24), (xb, y + 0.10, zt)))
        P += K.boxes_part(f"tail_purlin{tag}", boxes, "timber_heavy")
        for k, y in enumerate((-4.4, -2.75, 2.75, 4.4)):
            ztop = underside(y) - (0.24 if abs(y) == 2.75 else 0.02)
            P += K.bracket(f"rake{tag}{k}", (x, y, ztop), "+X" if sgn > 0 else "-X",
                           span=RAKE_OVER - 0.10, drop=0.9)
    # T91 built "mid" (L2 had 57 tris under its cap). The 2026-10-19 integration build (T85 plates +48, ridge v2 -30)
    # measured 6,009 / 6,000 at weather level 0, so the pipe goes "lean" (-40) and L2 keeps weather level 0. T101 r3
    # (2026-10-19, DECISIONS 320: vanilla props first): fittings v1 = a vanilla ACVentRoof03 row (cs2_fittings
    # ROWS["L2"]["roof"])
    P += [] if K.FITTINGS.get("version", 0) >= 1 else K.stovepipe("hut", spec, STOVE_XY[0], STOVE_XY[1], r=0.15, detail="lean",
                     rise=STOVE_TOP - K.roof_z(spec, *STOVE_XY))
    # ---- facades --------------------------------------------------------------------------
    wE, wW = WallRef("+X", FL_X1), WallRef("-X", FL_X0)
    wS, wN = WallRef("-Y", -FL_Y), WallRef("+Y", FL_Y)
    win = WinSpec(WIN_W, WIN_H, WIN_SILL, lights=2)
    gdoor = WinSpec(GDOOR_W, GDOOR_H, GDOOR_SILL, lights=1)
    att = WinSpec(ATT_W, ATT_H, ATT_SILL, lights=1)
    bas = WinSpec(1.0, BAS_H, BAS_SILL, lights=2)
    # +X arrival (uphill): entry + canopy + 2 lanterns, windows y +-3.0, attic pair, sign
    # (fine: lamps 0, the two lanterns are WallLightShortRangeRandom01 rows, FINE_SUB_OBJECTS)
    P += K.entry("hut", wE, 0.0, w=1.2, h=2.2, sidelights=0, canopy=(1.4, 28.0),
                 lamps=0 if fine else 2, room="fl1E", z=FL1_Z)
    for k, y in enumerate((-3.0, 3.0)):
        P += _window(K, f"E{k}", wE, y, win, "fl1E", shutters=True)
        P += _window(K, f"aE{k}", wE, -1.2 if k == 0 else 1.2, att, "attE")
    P += K.sign_board("hut", wE, 0.0, 3.85, 2.4, 0.6, mat="timber_heavy")
    # -X view (downhill): 2 glazed doors y +-1.2, windows y +-3.8, attic pair, basement
    for k, y in enumerate((-1.2, 1.2)):
        P += _window(K, f"Wd{k}", wW, -y, gdoor, "fl1W", sill_board=False)
        P += _window(K, f"aW{k}", wW, -y, att, "attW")
    for k, y in enumerate((-3.8, 3.8)):
        P += _window(K, f"W{k}", wW, -y, win, "fl1W", shutters=True)
    bW = WallRef("-X", PL_X0)
    for k, y in enumerate((-3.0, 3.0)):
        P += _window(K, f"bW{k}", bW, -y, bas, "basW")
    # basement door (recipe-local: entry() lifts to GROUND_LIFT): surround + kit door
    if K.DOOR_KIT["version"] == 2:      # door kit v2 (chalet): recessed half-glazed leaf
        P += K.door_unit("bas", bW, 0.0, 1.0, BAS_DOOR_H, BAS_DOOR_Z0, room="basW",
                         casing_name="entry_bas", casing_key="timber_heavy", leaf_key="trim")
    else:
        P += K.wall_box("entry_bas", bW, -0.7, 0.7, BAS_DOOR_Z0, BAS_DOOR_Z0 + BAS_DOOR_H + 0.30,
                        -0.08, 0.18, "timber_heavy")
        loc = K.wall_point(bW, 0.0, BAS_DOOR_Z0, 0.18)
        P += [(o, m if isinstance(m, str) else "trim")
              for o, m in cs2_kit.door("bas", w=1.0, h=BAS_DOOR_H, loc=tuple(loc), facing="-X", quad=0)]
    P += _basement_stair(K)
    # -Y (deck side): windows x -2.5, 1.5, 5.0 + glazed door x -0.5, basement x -1.5, 3.5
    for k, x in enumerate((-2.5, 1.5, 5.0)):
        P += _window(K, f"S{k}", wS, x, win, "fl1S", shutters=True)
    P += _window(K, "Sd", wS, -0.5, gdoor, "fl1S", sill_board=False)
    bS = WallRef("-Y", -PL_Y)
    for k, x in enumerate((-1.5, 3.5)):
        P += _window(K, f"bS{k}", bS, x, bas, "basS")
    # +Y: windows x -2.5, 1.5, 5.0, basement x 1.0
    for k, x in enumerate((-2.5, 1.5, 5.0)):
        P += _window(K, f"N{k}", wN, -x, win, "fl1N", shutters=True)
    P += _window(K, "bN", WallRef("+Y", PL_Y), -1.0, bas, "basN")
    # ---- wrap deck (view side, sunny) on stone piers + timber posts ---------------------
    dx_in, dy_in = PL_X0 - CAP_PROUD, -(PL_Y + CAP_PROUD)       # against the cap ring
    P += K.deck("hutW", DECK_X0, dx_in, DECK_Y0, FL_Y, DECK_TOP, support="none", fascia=0.30,
                board_dir="X", thick=DECK_T)
    P += K.deck("hutS", dx_in, DECK_X1, DECK_Y0, dy_in, DECK_TOP, support="none", fascia=0.30,
                board_dir="Y", thick=DECK_T)
    z_under = DECK_TOP - DECK_T
    for k, (px, py) in enumerate(((DECK_X0 + 0.4, DECK_Y0 + 0.4), (DECK_X0 + 0.4, FL_Y - 0.4),
                                  (DECK_X1 - 0.4, DECK_Y0 + 0.4))):
        P += K.stone_pier(f"hut{k}", px, py, 0.8, 0.8, SUPPORT_Z0, z_under, cap=False, ground=False)
    posts = [(DECK_X0 + 0.1, y) for y in (-4.8, -1.5, 1.8)] + \
            [(x, DECK_Y0 + 0.1) for x in (-4.3, -1.0, 2.3)]
    P += K.boxes_part("col_hutposts", [((x - 0.10, y - 0.10, _post_z0(K, x, SUPPORT_Z0)),
                                        (x + 0.10, y + 0.10, z_under))
                                       for x, y in posts], "timber_heavy")
    inset = 0.05
    P += K.rail("hut", [(dx_in, FL_Y - inset, DECK_TOP), (DECK_X0 + inset, FL_Y - inset, DECK_TOP),
                        (DECK_X0 + inset, DECK_Y0 + inset, DECK_TOP),
                        (DECK_X1 - inset, DECK_Y0 + inset, DECK_TOP),
                        (DECK_X1 - inset, dy_in, DECK_TOP)], kind="timber")
    for tag, (lx, ly) in (("deckSW", (DECK_X0 + inset, DECK_Y0 + inset)),
                          ("deckNW", (DECK_X0 + inset, FL_Y - inset))):
        if K.FITTINGS.get("version", 0) >= 1:   # T101 r3 (2026-10-19, DECISIONS 320: vanilla props first):
            continue                            # GardenLight03 rows on the deck (cs2_fittings ROWS["L2"]["curb"])
        lp, _pt = K.lantern(tag, (lx, ly, DECK_TOP + 1.10), "-X", kind="post")
        P += lp
    # ---- entry bridge over the uphill gap + two step blocks to the anchor terrain ----------
    P += K.deck("bridge", BRIDGE_X0, BRIDGE_X1, -BRIDGE_Y, BRIDGE_Y, DECK_TOP, support="none",
                fascia=0.30, board_dir="Y", thick=DECK_T)
    if K.STRUCTURE.get("version", 0) >= 1:     # T61: a stone abutment under the bridge end and both wings
        P += K.box_part("pier_hutentry", (WING_X0, -WING_Y, STEP_Z0_V1), (BRIDGE_X1, WING_Y, z_under),
                        "stone_rubble", drop=("-Z", "+Z"))
    else:
        P += K.boxes_part("col_bridge", [((10.70, sg * 1.2 - 0.10, _post_z0(K, 10.80, BRIDGE_POST_Z0)),
                                          (10.90, sg * 1.2 + 0.10, z_under)) for sg in (-1, 1)],
                          "timber_heavy")
    # H3: the bridge rails stop at the wings (access) and continue around the wing's downhill (x 9.35) and outer (|y| 3.75)
    # edges as ONE rail per side (no doubled corner posts). The wing's uphill end (x 10.96) stays open to the slope
    for tag, sg in (("S", -1), ("N", 1)):
        y = sg * (BRIDGE_Y - inset)
        yw, xw = sg * (WING_Y - INSET), WING_X0 + INSET
        P += K.rail(f"bridge{tag}", [(BRIDGE_X0, y, DECK_TOP), (xw, y, DECK_TOP),
                                     (xw, yw, DECK_TOP), (BRIDGE_X1 - INSET, yw, DECK_TOP)],
                    kind="timber")
    P += _wings(K)
    st = K.STRUCTURE.get("version", 0) >= 1
    for k in range(2):
        top = round(DECK_TOP - (k + 1) * STEP_RISE, 4)
        if st:                                 # T61: the stone stair base runs down to the post depth
            P += K.box_part(f"step_hut{k + 1}", (STEP_X[k], -BRIDGE_Y, STEP_Z0_V1),
                            (STEP_X[k + 1], BRIDGE_Y, top), "stone_rubble", drop=("-Z",))
        else:
            P += K.box_part(f"step_hut{k + 1}", (STEP_X[k], -BRIDGE_Y, STEP_Z0),
                            (STEP_X[k + 1], BRIDGE_Y, top), "stone_rubble")
    if st:                                     # T61: concrete footings round the wrap deck's post feet
        boxes = []
        for x, y in posts:
            g0 = terrain_z(x - FOOT_W / 2)
            g1 = terrain_z(x + FOOT_W / 2)
            boxes.append(((x - FOOT_W / 2, y - FOOT_W / 2, max(g0 - FOOT_DOWN, SUPPORT_Z0)),
                          (x + FOOT_W / 2, y + FOOT_W / 2, g1 + FOOT_UP), ("-Z",)))
        P += K.boxes_part("footing_hut", boxes, "concrete")
    # ---- detail pass 1 dressing (design 3.2) ---------------------------------------------
    P += _detail1(K, fine)
    # ---- contract C11: every part carries a kit prefix -------------------------------------
    bad = sorted(o.name for o, _m in P if not o.name.startswith(PREFIXES + K.KIT2_PARTS))  # + window kit v2
    if bad:
        raise RuntimeError(f"L2: parts outside the prefix table: {bad[:8]}")
    return P


RECIPE = FineRecipe(
    key="L2", aliases=("basic-top",), letter="B",
    asset="SkiLodgeHutCommercialLow", prefab="SkiLodgeHut", category="lodges",
    lot=LOT, base_mode="plinth", plinth=(-SUPPORT_Z0, PL_TOP), yaw_deg=0.0, tex=2048,
    allow_4096=False,
    budget=Budget(5500, 6000, 2400, 450, 450),
    # tiled rollout (texture-parity-rollout-2026-10-16.md section 2, --tiled only): columns, snow guards and decking at 2 x 2
    # tiles keep the tiled LOD0 under the 6,000 cap
    tile_scale=(("timber_heavy", 2), ("galv", 2), ("deck", 2)),
    massing="custom", builder=build_hut,
    rooms=ROOMS, interior="CommercialLow",
    # one resort, one trim (lodges-basic Q6): slot 1 = L1's shipped green VariationSet, slot 2 house orange (no slot-2
    # surface on L2), slot 3 slate gray (entry canopy only)
    slots={1: (0.0625, 0.3125, 0.25), 2: (0.85, 0.25, 0.10), 3: (0.30, 0.31, 0.325)},
    ground_prefixes=("plinth_", "pier_", "deck_"),
    arrival_prefixes=("entry_", "deck_bridge", "step_"),
    # detail pass 1: benches are not on the kit's LOD1 list (M1). The flag cloth leaves with its mast (M8: `flagpanel_` is on
    # the kit's LOD2 list only). lod1_extra is not inherited by LOD2, so bench_ is listed there again (L1 DP4 lesson)
    lod1_extra=("bench_", "flagpanel_"),
    lod2_extra=("col_", "step_", "roof_hutcanopy", "stair_bas", "bench_"),
    lod2_keep=("stovepipe_hut",),
    labels=(
        # the existing restaurant pictogram, drawn in a centered square (was stretched 4:1 on
        # the 2.4 x 0.6 board: buildings-review-pipeline finding 1)
        ("sign_hut_face", "display",
         {"draws": (("pictogram", {"kind": "restaurant", "square": True, "bg": BOARD_BROWN}),)}),
        # H7: white on hut red, the 5x7 font at fill 0.85 (T1 at w 1.75, M4)
        ("sign_hutname_face", "display",
         {"draws": (("text", {"lines": ("RESTAURANT",), "bg": HUT_RED}),)}),
        # H8: the kit's chalkboard menu (paint_glyph kind "menu", M1)
        ("sign_hutmenu_face", "paint_glyph",
         {"face": "sign_hutmenu_face", "kind": "menu", "bg": CHALK}),
    ),
    # texel weights (design 2.0 / M4):
    # - the word board at 1.75 (T1 with margin)
    # - the bulbs at 1.25 so each 0.07 m face spans >= 2.5 texels at the shipped ~36 px/m (design said 1.0 at 38, the kit's
    #   dressing cap would give 0.5)
    # - their wire stays 0.5
    # - the benches take the kit's dressing weight (they leave at LOD1 through lod1_extra, which the kit's LOD1_DELETE weight
    #   rule does not see)
    # - the galv ridge cap 0.5 -> 0.6: the dressing's extra 279 UV islands took w = 1 from 38.2 to 36.2 px/m and left 0.91 m2
    #   of ridge faces at 13.9 px/m (< 16, 0 m2 before the pass)
    weights=(("sign_hutname_face", 1.75), ("lamp_hutS", 1.25), ("lamp_hutSwire", 0.5),
             ("bench_", 0.5), ("ridge_", 0.6)),
    # weathering (DECISIONS 171): intensity = the highest budget-clean step measured on the dressed build (lodge.py
    # --weather-sweep, build report). slot_wear / snow / ground take the module defaults (free, 0.0, plinth: the below-grade
    # base keeps its weathered stone)
    weather={"intensity": 0.40, "overrides": (
        ("drum_", {"role": "galv", "scale": 1.3}),          # old fuel drums: rust, chipped edges
        ("drum_hutfuel2base", {"role": "timber"}),          # ... but the crib is timber
        ("stair_bas", {"role": "deck"}),
        ("deck_ski", {"scuff": 1.0}))},
    sub_objects=SUB_OBJECTS + FINE_SUB_OBJECTS + KIT_ROWS,
    # fine-detail pass step 3a (opt-in, lodge.py --no-fine restores `legacy` = the approved hut)
    fine_detail=True,
    sign_set=SIGN_SET,
    kit_colour_set=KIT_COLOUR_SET,
    legacy=(("sub_objects", SUB_OBJECTS),),
    cull_ok=(),
    # post-orient Blender frame: authored (ax, ay) -> (ay, -ax). Downhill 3/4 = authored (-1, -0.5) -> (-0.5, 1.0), low
    # camera so the plinth reads
    views=(("downhill", (-0.5, 1.0, 0.12)),),
    notes="lodges-basic 4 on the kit: front anchor (D4), plinth to -6.00, supports to -7.20, "
          "no band vertex; tables / parasol / chairs as props (SUB_OBJECTS). Revision 1 "
          "(2026-10-11, O4): basement door reached by a landing + plinth-mode stair down "
          "to the 35 % design grade. Detail pass 1 (2026-10-11, design/detail-pass-1.md 3.2 "
          "+ skeptic M1-M10): benches, string lights, ski-deck wings with real gear on two "
          "racks, fuel station, S downspouts, RESTAURANT + menu boards, flag, step stripes, "
          "square pictogram, no interior gable faces; 16 vanilla sub-objects; weathering",
)
