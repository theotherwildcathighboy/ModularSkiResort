"""
recipes/lodge_chalet.py - L3 mid-tier lodge, the chalet complex (prefab SkiLodgeMid, mesh
SkiLodgeChaletCommercialLowB, lodges/B). Engine mode on a flattened 5 x 4 lot.

Contract: docs/research/art/assets-2026-10/design/lodges-basic.md section 5 with
design/lodges-basic-skeptic.md applied (M1-M10 + the L3 should-changes: Mass B's -X eave
trimmed where Mass A abuts, coincident A/B wall faces dropped) and ROUND-SPEC R21-R31; the kit
API of design/bldg-kit.md wins where the two disagree (R25). Built on cs2_bldg; this module
arranges kit parts (recipe-local pieces listed in KIT_NEEDS). Import-safe: no bpy work at
import.

MASSING (Courchevel complex, lodges-basic 5.1-5.2): Mass B (road hall, ridge along Y at
x 7.5, ridge 9.40) on the arrival side; Mass A (slope wing, stone G + L1 with a 4-pier arcade
on its -X gable, jettied timber L2, ridge along X at y -12.5, ridge 12.86) at right angles;
a stone podium under a terrace (top 3.45) with an opaque balustrade, a terrace stair, a
gable-roofed lookout gazebo on a raised platform, a kiosk, an entry porch, 3 flag masts.
Authoring frame: +X = arrival = game +Z (the road), z 0 = grade (vanilla flatten).

ENGINE BASE: every part reaching z < 0.011 is an open-bottom ground part (ground_prefixes);
every other part starts at z >= 0.02 (cs2_bldg.check_base_mode, on LOD0/1/2). Faces that
sit against another mass are not built (drop=) so the protected ground walls carry no
hidden texels: A's +X face (B covers it), B's G -X face (A + podium), the podium's +X / -Y
faces, the piers' +X faces, the chimney's -Y face below the gable.

FINE-DETAIL PASS, step 3a (2026-10-12; design/fine-detail-pass.md A.2 / B as cut by -skeptic.md,
Richard's seventeenth look H1-H6 defaults; build/fine-detail-step3a-buildings.md; the school is the
reference host, build/fine-detail-step2-engine.md section 12). RECIPE is a fine_detail.FineRecipe
(fine_detail=True, NO sign set); `lodge.py --no-fine` rebuilds the approved chalet from `legacy`:
    B.1 kit geometry  the 3 flag masts 12-seg + ball finial (flag_mast fine=True)
    A.2 vanilla       the 8 wall lanterns (lamp_Be0 / Be1, porchB0 / porchB1, Bn, aW, podN0, kiosk)
                      -> WallLightShortRangeRandom01 rows (H4) on their wall / column face at the old
                      lens (y, z); the gazebo's hanging lantern -> RoofLightRandom01 at the foot of
                      its rod (the rod stays as the pendant stem, renamed trim_gzrod: no lamp_ part is
                      left, so the chalet ships no _Emissive map)
    D   kit rows      L3 had NO racks (SKI_RACK_SPOTS was planned, never placed): 3 kit A-frame racks
                      at those spots in front of Mass A's arcade (slope side, on the flattened lot,
                      m_ParentMesh -1), alternating the three loadouts, identity (long axis along
                      authoring Y, parallel to the arcade)
    no sign set       L3's boards are pictograms only (restaurant x 2, kiosk info: no lettering), so
                      they stay in the host atlas (design C.1); the kiosk and the gazebo stay modeled
                      (approved; vanilla Kiosk / GazeboRandom01 exist: PLACEHOLDERS in the report)
Row frame: every row, old and new, is game (gx, gy, gz) = authoring (y, z, x), gx NOT negated
(fine_detail.X_CONVENTION, the chalet's existing convention; probe X2 decides, S1b mirrors on N).

NITPICK C (2026-10-13; Richard, twenty-fifth look: "not loving the SIGN of the FORK AND KNIFE ... leave it blank or
keep it simple, and people can paste their own labels on it?"; build/nitpick-c-lodges.md): the three pictogram
boards are BLANK boards (NC_LABELS: their own ground + a thin trim-color rim, fine_detail.blank_board), listed in
the manifest's `we_boards` block for an optional Write Everywhere layout; `--no-fine` keeps the pictograms
(PICTO_LABELS, legacy). Geometry unchanged: only the three label rects of the atlas repaint.

T60 (2026-10-18; Richard, Blender review 2026-10-17 item 5: "the small building has no door, so it just needs one. I
think we also need a door on the patio itself. Probably turn one of the tall windows into a door."; build note
docs/research/field/2026-10-15/build/T59-T60-chalet-summit.md): T60_DOORS (fine builds only; False = the chalet before
T60, byte-identical). (a) the kiosk (the small building) gets a chalet-family door on its -Y gable, the end facing the
terrace stair foot and the podium (the staff route; the -X face is the serving hatch, the +X face carries the board):
1.0 x 2.1 m, one half-glazed leaf, its glass in the existing room `kiosk`; (b) the middle of the three tall glazed
F1 openings on B's -X face onto the terrace (`bW1`, a 1.0 m window_bay at y -1.0) becomes a chalet DOUBLE door 1.6 x
2.2 m (two leaves, a lever each) on the terrace deck, its glass in the existing room `bF1W` (that room's panes 3 -> 4:
bW0 + bW2 + the two leaf lights). Each new door gets a WallLightShortRangeRandom01 row beside it (T60_LAMPS, the
FINE_LAMPS form); the kiosk's trash bin row, which stood on the new door's axis 0.9 m out, moves 1.4 m east. With --door-kit v1 the two are v1 entries, as every other L3 door (same lamp rows).
"""

import math
import os
import sys

_BLENDER = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BLENDER not in sys.path:
    sys.path.insert(0, _BLENDER)
from bldg_recipes import PARASOL_OFFSET, Budget, Recipe   # noqa: E402
from fine_detail import AFRAME_RACKS, FineRecipe, blank_board, kit_row   # noqa: E402

# ======================================================================================
# Constants (authoring frame, meters) - lodges-basic 5.2
# ======================================================================================

LOT = (5, 4)                       # (W, D): |y| <= 20, |x| <= 16
G_TOP, L1_TOP, L2_TOP = 3.40, 6.60, 9.80
PITCH_DEG = 27.0                   # tan 0.5095 (C5)
T27 = math.tan(math.radians(PITCH_DEG))

B_X0, B_X1, B_Y0, B_Y1 = 2.0, 13.0, -18.0, 5.0       # Mass B (road hall)
B_RIDGE_X = 0.5 * (B_X0 + B_X1)                      # 7.50
B_RIDGE_Z = L1_TOP + T27 * (B_RIDGE_X - B_X0)        # 9.40 (C5)
A_X0, A_X1, A_Y0, A_Y1 = -12.70, 2.0, -18.0, -7.0    # Mass A stone (infill plane -12.70)
PIER_X0 = -13.50                                     # arcade pier face
A2_X0, A2_Y0, A2_Y1 = -14.0, -18.5, -6.5             # Mass A L2, jettied 0.50
A_RIDGE_Y = 0.5 * (A2_Y0 + A2_Y1)                    # -12.50
A_RIDGE_Z = L2_TOP + T27 * (A_RIDGE_Y - A2_Y0)       # 12.86 (C5)
EAVE_OVER, RAKE_OVER, ROOF_T = 1.20, 1.00, 0.24
POD_X0, POD_Y1, POD_TOP = -13.50, 5.0, 3.30          # podium x -13.5..2.0, y -7..5
TERR_TOP = 3.45
GZ_X0, GZ_X1, GZ_Y0, GZ_Y1, GZ_TOP = -13.30, -9.10, 0.60, 4.80, 4.85   # gazebo platform
GZ_POSTS = ((-12.90, 1.00), (-9.50, 1.00), (-12.90, 4.40), (-9.50, 4.40))
GZ_PITCH, GZ_TIP_Z = 35.0, 8.05
KI_X0, KI_X1, KI_Y0, KI_Y1, KI_H = -15.20, -12.20, 8.50, 11.00, 2.50  # kiosk
PORCH = (13.0, 15.42, -8.42, -4.58)                  # porch roof rectangle (x0, x1, y0, y1)
MAST_X, MAST_YS = 14.80, (8.0, 11.0, 14.0)

WIN = (1.2, 1.4)                   # standard window w x h, sill floor + 0.9
GDOOR = (1.0, 2.2)                 # glazed door w x h, sill floor + 0.10
# T60 (2026-10-18, module docstring): the kiosk door + the patio double door. False = the pre-T60 chalet (byte-identical)
T60_DOORS = True
KIOSK_DOOR = (-13.70, 1.0, 2.1)    # (x center on the -Y gable, w, h): the gable's center, L3's single-door size
PATIO_DOOR = (-1.0, 1.6, 2.2)      # (y center on B -X, w, h): bW1's spot, 2 leaves (chalet double_min 1.5)

ROOMS = {"bGE": (1, 0), "bGS": (2, 0), "bF1S": (3, 0), "bF1E": (4, 0), "bF1W": (0, 1),
         "bGN": (1, 1), "bF1N": (2, 1), "aW0": (3, 1), "aW1": (4, 1), "aGS": (0, 2),
         "aL1S": (1, 2), "aL2W": (2, 2), "aL2S": (3, 2), "aL2N": (4, 2), "aL1N": (0, 3),
         "podW": (1, 3), "podN": (2, 3), "kiosk": (3, 3)}

PREFIXES = ("wall_", "gable_", "roof_", "soffit_", "rake_", "fascia_", "ridge_", "snowguard_",
            "gutter_", "downspout_", "chim_", "chimcap_", "win_", "sill_", "head_", "jamb_",
            "mull_", "cmull_", "ctran_", "trim_", "entry_", "door_", "lamp_", "sign_",
            "deck_", "deckfascia_", "trail_", "glassrail_", "post_", "rail_", "kick_",
            "stair_", "pier_", "col_", "beam_", "bracket_", "balc_", "gazebocol_", "flag_",
            "flagpanel_", "step_")

AREA_CAP_M2 = 4000.0
SUM_W2A_CAP = 2400.0

RED_BOARD_SRGB = (0.55, 0.08, 0.06)    # Courchevel red boards (lodges-basic 2.5 redboard)
DARK_BOARD_SRGB = (0.20, 0.14, 0.10)
# the approved boards (fine-detail step 3a and before): pictograms painted over the whole face (paint_pictogram
# has no square mode, so the fork-and-knife stretched 6.7:1 on the 4.0 x 0.6 boards). `--no-fine` keeps them.
PICTO_LABELS = (("sign_boardB_face", "paint_pictogram",
                 {"kind": "restaurant", "fg": 0.92, "bg": RED_BOARD_SRGB}),
                ("sign_boardP_face", "paint_pictogram",
                 {"kind": "restaurant", "fg": 0.92, "bg": RED_BOARD_SRGB}),
                ("sign_kiosk_face", "paint_pictogram",
                 {"kind": "info", "fg": 0.92, "bg": DARK_BOARD_SRGB}))
# nitpick C (2026-10-13; Richard, twenty-fifth look, L3: "not loving the SIGN of the FORK AND KNIFE, that looks
# quite bad. I wonder if we just leave it blank or keep it simple, and people can paste their own labels on it?";
# build/nitpick-c-lodges.md): the three pictogram boards become BLANK boards - the board's own ground (red / dark)
# with a thin rim in the chalet's trim color (slot 1 (0.0625, 0.3125, 0.25) as shown on the 0.80 trim paint:
# lin_to_srgb(srgb_to_lin(0.80) x slot)), no pictogram. The kiosk's stretched info bars go too (same board family,
# same stretch). The faces stay flat and unobstructed; lodge.py lists them in the manifest's `we_boards` block
# (center / normal / size) for an optional Write Everywhere layout. Rim 0.056 m at the label density (71.4 px/m
# measured: label w 3.0 x the chalet's 23.8 px/m w = 1) = 4.0 texels (0.04 m gave 2.86, under the 4 px floor).
L3_TRIM_SRGB = (0.214, 0.472, 0.425)
NC_LABELS = tuple(
    (face, "display", {"draws": (blank_board(bg=bg, rim=L3_TRIM_SRGB, rim_m=0.056, inset_m=0.06),)})
    for face, bg in (("sign_boardB_face", RED_BOARD_SRGB), ("sign_boardP_face", RED_BOARD_SRGB),
                     ("sign_kiosk_face", DARK_BOARD_SRGB)))

# Props (GAME coords (gx, gy, gz) = authoring (y, z, x); lodges-basic 5.7). Not in the mesh.
# (prefab, GUID, positions, yaw deg): yaw turns the prop's local +Z to authoring 0 = +X (road),
# 90 = +Y, 180 = -X (slope), -90 = -Y (_yaw_q). Vanilla retrofit (fifteenth look, 2026-10-11):
# every GUID re-derived from %TEMP%\pltar\dump.zip (placeholder + variants + union bounds, as
# cs2_liftdress.verify_subobj), the rows wired into the manifest (SUB_OBJECTS; the 10:20 export had
# none: the recipe never set Recipe.sub_objects, which the engine only gained in detail pass 1),
# and four placement fixes found by the box check against the built LOD0 (no lodge triangle in a
# prop box, no box overlap, inside the lot):
#   - parasols: the vanilla convention (bldg_recipes.L1_PARASOLS: 442 base-game parasol / table
#     pairs) puts the parasol pivot PARASOL_OFFSET 1.37 m from its table, local +Z at it, the offset
#     canopy (z -0.73..3.06) over the table. On the table pivots (the design) the canopy hung 1.2 m
#     off the tables and the one over the (-1.0, -4.5) table went through Mass B's -X wall, eave and
#     door heads. Now beside four tables (_PARASOL_TABLES): three of the design's, and the one at
#     (-1.0, -4.5) moved to its neighbor (-4.5, -4.5): any 3.5 m canopy over that table reaches
#     Mass B's -X fascia / gutter (x 0.60-0.79, z 5.7-6.1);
#   - the three DeckChairRandom01 (2.39 m loungers, yaw 180) on the 1.32 m deep A -X balcony ran
#     through the balustrade, the L2 wall and two window sills: ChairCommercialRandom01 (single
#     cafe chair 0.76 x 0.81 m, ActivityLocation seat facing local +Z, no snow rule; 146 buildings)
#     at the same spots, facing the slope;
#   - the two LightpoleParkRandom01 (lamp arm to local z +0.83) reached 3 cm past the road-side lot
#     edge (x 16.03): x 15.2 -> 15.1;
#   - the modeled lamp post at the terrace stair foot (col_lampTS + lamp_terrstair) is a seventh
#     GardenLightRandom01 (lit bollard) on its spot.
_TABLE_AT = ((-4.5, 3.45, -11.5), (-4.5, 3.45, -8.0), (-4.5, 3.45, -4.5), (-4.5, 3.45, -1.0),
             (-1.0, 3.45, -11.5), (-1.0, 3.45, -4.5), (-1.0, 3.45, -1.0), (3.0, 3.45, -4.5))
# (table, parasol yaw): the A-wall row's pair faces +X / -X (poles outside the pair, canopies
# across y, clear of the A wall at y -7.0), the north table +X, the middle one -Y (its canopy fits
# between the rows' canopies; along x it would touch them)
_PARASOL_TABLES = (((-4.5, 3.45, -8.0), 0), ((-4.5, 3.45, -4.5), 180), ((3.0, 3.45, -4.5), 0),
                   ((-1.0, 3.45, -4.5), -90))   # T26 2026-10-16 emptied this; restored 2026-10-17 (random colors accepted)


def _parasol_at(table, yaw):
    """Parasol pivot (game) PARASOL_OFFSET behind `table` along the yaw's authoring direction."""
    fx, fy = round(math.cos(math.radians(yaw)), 9), round(math.sin(math.radians(yaw)), 9)
    gx, gy, gz = table
    return (round(gx - PARASOL_OFFSET * fy, 4), gy, round(gz - PARASOL_OFFSET * fx, 4))


PROPS = (
    ("TableSetCommercialRandom01", "fc058a3d03b337e4794c432a84170316", _TABLE_AT, 0),
    *(("ParasolRandom01", "b7e2343fd7ff9f642b77084a04738e43",
       tuple(_parasol_at(t, y) for t, y in _PARASOL_TABLES if y == yaw), yaw)
      for yaw in sorted({y for _t, y in _PARASOL_TABLES})),        # one entry per yaw
    ("ChairCommercialRandom01", "024ff26a34d89b241b55fd5ea12c5dcc",
     ((-16.0, 6.80, -14.7), (-12.5, 6.80, -14.7), (-9.0, 6.80, -14.7)), 180),
    ("GardenBenchRandom01", "8df0703be09426948b5bc3262d86bc04",
     ((12.4, 0.0, -14.0), (12.4, 0.0, -11.5)), 0),
    ("GardenLightRandom01", "ba47f549eb430ac46a7c1b4cca509594",
     ((-16.0, 0.0, 14.6), (-12.0, 0.0, 14.6), (-1.0, 0.0, 14.6), (3.0, 0.0, 14.6),
      (6.5, 0.0, 14.6), (15.5, 0.0, 14.6), (6.5, 0.0, -14.9)), 0),
    ("LightpoleParkRandom01", "c987b53e24a01e744b4a00ee5b1f8a08",
     ((-18.6, 0.0, 15.1), (17.0, 0.0, 15.1)), 0),
    # T60: the kiosk bin stood 0.9 m in front of the new kiosk door on its axis (auth x -14.0, y 7.2): 1.4 m east,
    # under the door's lamp, off the door line (T60_DOORS False keeps -14.0)
    ("TrashbinRandom01", "41e3c9ba6e1d6b948a6b1bc3499a5397",
     ((-9.2, 0.0, 15.4), (7.2, 0.0, -12.6 if T60_DOORS else -14.0)), 0),
    ("FirewoodShelterRandom01", "c1d98e3e6f5e1204cb071deaf0f74020", ((6.3, 0.0, 10.8),), 90),
)
SKI_RACK_SPOTS = ((-13.0, 0.0, -15.0), (-10.0, 0.0, -15.0), (-7.0, 0.0, -15.0))  # props


def _yaw_q(deg):
    """Unity yaw about game +Y as (x, y, z, w): local +Z -> game (sin, 0, cos) = authoring
    (cos, sin) (recipes/lodge_hut._yaw_q): 0 = authoring +X, 90 = +Y, 180 = -X, -90 = -Y."""
    h = math.radians(deg) / 2.0
    return (0.0, round(math.sin(h), 7), 0.0, round(math.cos(h), 7))


# the manifest rows (lodge.sub_objects_block): grade props (gy 0) on the flattened lot
# m_ParentMesh -1, terrace / balcony props on our mesh 0 (lodges-basic report D13 + the skeptic's
# should-change); 29 rows
SUB_OBJECTS = tuple((nm, guid, tuple(float(v) for v in p), _yaw_q(yaw), -1 if p[1] == 0.0 else 0)
                    for nm, guid, pts, yaw in PROPS for p in pts)

# ---- fine-detail pass step 3a (module docstring): the rows that replace modeled parts --------------
# (same frame and tuple form as SUB_OBJECTS; GUIDs = cs2_liftdress.SUBOBJ, dump-verified)
_WALL_SHORT = "cfc12fde01d9da24b86ddedd657eaae1"      # WallLightShortRangeRandom01 (mount wall, 138 tris)
_ROOF_LIGHT = "b0ea4a4915b28f14a826bb809e02db7a"      # RoofLightRandom01 (mount ceiling, 380 tris)
_LIFT = 0.02                                          # cs2_bldg.GROUND_LIFT (entry floors on grade)
#: the 8 wall lanterns as WallLightShortRange rows: (lantern tag, authoring wall point (x, y, z) =
#: the lantern's `at` on its wall / column face, yaw of the face normal). cs2_bldg.entry puts its lamps
#: at u -+(w / 2 + 0.2 + 0.35), floor + 2.2; cs2_bldg.lantern(kind "wall") puts the lens 0.22 m out
#: from `at` at the same height (lodges' rows: on the face at the old lens (y, z))
FINE_LAMPS = (
    ("Be0", (B_X1, -6.5 - (1.8 / 2 + 0.2 + 0.35), 0.15 + 2.2), 0),    # entry Be (u -6.5, w 1.8, z 0.15)
    ("Be1", (B_X1, -6.5 + (1.8 / 2 + 0.2 + 0.35), 0.15 + 2.2), 0),
    ("porchB0", (15.41, -8.30, 2.60), 0),                              # on the porch column's +X face
    ("porchB1", (15.41, -4.70, 2.60), 0),
    ("Bn", (6.0, B_Y1, 2.30), 90),                                      # B +Y gable (u -6.0)
    ("aW", (A2_X0, -12.5, 8.80), 180),                                  # A L2 -X, between the balcony doors
    ("podN0", (-(3.0 - (1.0 / 2 + 0.2 + 0.35)), POD_Y1, _LIFT + 2.2), 90),   # entry podN (u 3.0, w 1.0)
    ("kiosk", (KI_X0, 10.75, 1.80), 180),                              # kiosk -X wall (u -10.75)
)
#: the gazebo's hanging lantern -> RoofLightRandom01 hung at the foot of its rod (the rod stays,
#: trim_gzrod): the plan center of the four posts, z = the rod foot 7.60 + 0.225
GZ_LAMP = (0.5 * (GZ_POSTS[0][0] + GZ_POSTS[1][0]), 0.5 * (GZ_POSTS[0][1] + GZ_POSTS[2][1]), 7.60 + 0.225)
FINE_SUB_OBJECTS = tuple(
    ("WallLightShortRangeRandom01", _WALL_SHORT, (round(y, 4), round(z, 4), round(x, 4)), _yaw_q(yaw), 0)
    for _tag, (x, y, z), yaw in FINE_LAMPS
) + (
    ("RoofLightRandom01", _ROOF_LIGHT, (round(GZ_LAMP[1], 4), round(GZ_LAMP[2], 4), round(GZ_LAMP[0], 4)),
     _yaw_q(0), 0),
)
#: T60 (module docstring): an entry light beside each new door, the FINE_LAMPS form and the entry() lamp spot
#: (u + (w / 2 + 0.2 + 0.35), floor + 2.2) on the wall face: the kiosk door's on the podium side of its -Y gable, the
#: patio door's on its south side (between it and bW0)
T60_LAMPS = (
    ("kio", (KIOSK_DOOR[0] + (KIOSK_DOOR[1] / 2 + 0.2 + 0.35), KI_Y0, _LIFT + 2.2), -90),
    ("bWd", (B_X0, PATIO_DOOR[0] - (PATIO_DOOR[1] / 2 + 0.2 + 0.35), TERR_TOP + 2.2), 180),
)
T60_SUB_OBJECTS = tuple(
    ("WallLightShortRangeRandom01", _WALL_SHORT, (round(y, 4), round(z, 4), round(x, 4)), _yaw_q(yaw), 0)
    for _tag, (x, y, z), yaw in T60_LAMPS) if T60_DOORS else ()
#: the planned SKI_RACK_SPOTS (never placed: L3 had no racks) as kit rows on the flattened lot in front
#: of Mass A's arcade, alternating the three loadouts (FS6), identity (long axis along authoring Y)
#: kit color variants (Richard 2026-10-12 ~18:50; build/kit-colour-variants.md): the chalet's trim green (slot 1,
#: "one resort, one trim"); L1 next door is orange
KIT_COLOUR_SET = "green"
KIT_ROWS = tuple(
    kit_row(AFRAME_RACKS[i % len(AFRAME_RACKS)], tuple(float(v) for v in spot), parent=-1,
            note="SKI_RACK_SPOTS (lodges-basic 5.7): in front of Mass A's -X arcade, on the lot",
            colour_set=KIT_COLOUR_SET)
    for i, spot in enumerate(SKI_RACK_SPOTS))

KIT_NEEDS = (
    "gable_roof has one symmetric overhang: Mass B's roof is composed from roof_slab / "
    "prism_gable / box_part so the -X eave stops where Mass A abuts (skeptic should-change)",
    "gazebo() builds a hip roof; the photo shows a gable with knee braces (lodges-basic 5.1, "
    "skeptic claim 8): gazebo composed from col / deck / gable_roof / bracket here",
    "balcony() rails are see-through kinds only; Mass A's opaque timber balustrades are "
    "`balc_*bal` boxes",
    "stone_pier() / chimney() have no drop=: arcade piers and the chimney are box_part with "
    "their buried faces dropped (protected ground parts are never culled)",
    "no per-part density override: A's +X gable over B's roof keeps w 1.0 (lodges-basic 0.5)",
    "slate is slot 0: L3 has no slot-3 surface (VariationSet slot 3 is inert)",
)


def _rename(obj, new):
    obj.name = new
    obj.data.name = new
    if obj.name != new:
        raise RuntimeError(f"part {new!r} was renamed {obj.name!r}")


def _win(K, name, wall, u, w, h, sill, room, lights=None, sill_board=True):
    lights = lights if lights is not None else (2 if w >= 1.1 else 1)
    return K.window_bay(name, wall, u - w / 2, u + w / 2, K.WinSpec(w, h, sill, lights=lights),
                        room=room, sill_board=sill_board)


def _roof_b(K):
    """Mass B's gable (ridge along Y at x 7.5, 27 deg, plate 6.60): the kit gable_roof
    pieces with the -X eave overhang only north of Mass A's L2 (y >= -6.5)."""
    t, ov, ro, ext = T27, EAVE_OVER, RAKE_OVER, 0.35        # ext = cs2_bldg.RIDGE_EXT
    a_lo, a_hi, a_cut = B_Y0 - ro, B_Y1 + ro, A2_Y1
    r_z = B_RIDGE_Z + ext * t
    tip_w = (B_X0 - ov, L1_TOP - t * ov)                     # (0.80, 5.99)
    tip_e = (B_X1 + ov, L1_TOP - t * ov)                     # (14.20, 5.99)
    P = []
    P += K.roof_slab("BAn", "Y", a_cut, a_hi, tip_w[0], tip_w[1], B_RIDGE_X + ext, r_z,
                     ROOF_T, wall_line=B_X0, mat="slate")
    P += K.roof_slab("BAs", "Y", a_lo, a_cut, B_X0, L1_TOP, B_RIDGE_X + ext, r_z, ROOF_T,
                     wall_line=None, mat="slate")
    P += K.roof_slab("BB", "Y", a_lo, a_hi, tip_e[0], tip_e[1], B_RIDGE_X - ext, r_z, ROOF_T,
                     wall_line=B_X1, mat="slate")
    for nm, a, (ex, ez) in (("BAs0", a_lo - 0.06, (B_X0, L1_TOP)), ("BAn1", a_hi + 0.06, tip_w),
                            ("BB0", a_lo - 0.06, tip_e), ("BB1", a_hi + 0.06, tip_e)):
        P += K.roof_slab(nm, "Y", a - 0.06, a + 0.06, ex, ez - 0.05, B_RIDGE_X, B_RIDGE_Z - 0.02,
                         thick=0.40, mat="trim", prefix="rake_")
    fz = tip_w[1] - 0.06
    P += K.box_part("fascia_BAn", (tip_w[0] - 0.13, a_cut, fz - 0.21),
                    (tip_w[0] - 0.01, a_hi + 0.06, fz + 0.21), "trim")
    P += K.box_part("fascia_BB", (tip_e[0] + 0.01, a_lo - 0.06, fz - 0.21),
                    (tip_e[0] + 0.13, a_hi + 0.06, fz + 0.21), "trim")
    # ridge cap (cs2_bldg._ridge_cap_parts arithmetic, galv)
    p = math.atan(t)
    cross = B_RIDGE_Z + 0.5 * ROOF_T / math.cos(p)
    tip = B_RIDGE_Z + ext * t + 0.5 * ROOF_T * math.cos(p)
    zb = cross - 0.03
    zt = max(zb + 0.14, tip + 0.02)
    hw = max(0.225, ext + 0.05)
    P += K.box_part("ridge_B", (B_RIDGE_X - hw, a_lo, zb), (B_RIDGE_X + hw, a_hi, zt), "galv")
    # shake gable triangles facing -Y / +Y
    P += K.prism_gable("B0", "Y", B_Y0, B_Y0 + 0.3, B_X0, B_X1, L1_TOP, B_RIDGE_Z - 0.02,
                       ridge_at=B_RIDGE_X, mat="timber_shake")
    P += K.prism_gable("B1", "Y", B_Y1 - 0.3, B_Y1, B_X0, B_X1, L1_TOP, B_RIDGE_Z - 0.02,
                       ridge_at=B_RIDGE_X, mat="timber_shake")
    spec = K.RoofSpec("gable", B_X0, B_X1, B_Y0, B_Y1, L1_TOP, PITCH_DEG, axis="Y",
                      overhang=ov, rake_over=ro, thick=ROOF_T, mat="slate")
    P += K.snow_guard("BB", spec, "+X", offset=0.6)
    if K.FITTINGS.get("version", 0) >= 1:
        # fittings v1 (2026-10-17): pad snow guards on the slate field above the arrival entry, two staggered
        # rows behind the eave rail (Courchevel, L3's source: rows of pads on the slate)
        P += K.snow_pads("BB", spec, "+X", offsets=(1.4, 2.0), every=0.75)
    P += K.snow_guard("BAn", spec, "-X", offset=0.6, spans=((a_cut, B_Y1),))
    gz = 5.95
    P += K.gutter("BE", (tip_e[0] + 0.20, a_lo), (tip_e[0] + 0.20, a_hi), gz)
    P += K.gutter("BW", (tip_w[0] - 0.20, a_cut), (tip_w[0] - 0.20, a_hi), gz)
    for nm, x, y, n, gap in (("BES", B_X1 + 0.10, B_Y0 + 0.20, "+X", tip_e[0] + 0.20 - B_X1 - 0.10),
                             ("BEN", B_X1 + 0.10, B_Y1 - 0.20, "+X", tip_e[0] + 0.20 - B_X1 - 0.10),
                             ("BWN", B_X0 + 0.10, B_Y1 + 0.10, "-X", B_X0 + 0.10 - (tip_w[0] - 0.20))):
        P += K.downspout(nm, x, y, gz, n, wall_gap=gap, z_bottom=0.05)
    return P


def _gazebo(K, fine=False):
    """Lookout gazebo on the terrace: platform (top 4.85) on 4 posts that run up to the
    eave beams, gable roof 35 deg ridge along X (peak 9.66), 8 knee braces, timber rail with
    the stair opening on +X, 8-riser stair from the terrace, a hanging lantern. fine (step 3a):
    no lantern (a RoofLightRandom01 row hangs at the rod's foot), the rod is `trim_gzrod`."""
    cx = 0.5 * (GZ_POSTS[0][0] + GZ_POSTS[1][0])
    cy = 0.5 * (GZ_POSTS[0][1] + GZ_POSTS[2][1])
    t = math.tan(math.radians(GZ_PITCH))
    ov = 0.60
    plate = GZ_TIP_Z + t * ov                                 # 8.47 center plane at the posts
    under = plate - 0.5 * 0.16 * math.sqrt(1 + t * t)         # slab underside at the post line
    beam_top = round(under - 0.02, 3)
    beam_bot = beam_top - 0.22
    P = []
    P += K.deck("gz", GZ_X0, GZ_X1, GZ_Y0, GZ_Y1, GZ_TOP, support="none", fascia=0.20,
                board_dir="X", thick=0.20)
    P += K.boxes_part("gazebocol_gz", [((x - 0.11, y - 0.11, TERR_TOP), (x + 0.11, y + 0.11, beam_bot))
                                       for x, y in GZ_POSTS], "timber_heavy")
    xs = (GZ_POSTS[0][0], GZ_POSTS[1][0])
    ys = (GZ_POSTS[0][1], GZ_POSTS[2][1])
    beams = [((xs[0] - 0.11, y - 0.11, beam_bot), (xs[1] + 0.11, y + 0.11, beam_top)) for y in ys] + \
            [((x - 0.10, ys[0] + 0.11, beam_bot), (x + 0.10, ys[1] - 0.11, beam_top - 0.02)) for x in xs]
    P += K.boxes_part("beam_gz", beams, "timber_heavy")
    spec = K.RoofSpec("gable", xs[0], xs[1], ys[0], ys[1], plate, GZ_PITCH, axis="X",
                      overhang=ov, rake_over=ov, thick=0.16, mat="slate")
    P += K.gable_roof("gz", spec, fascia=0.25, rake=0.25, ridge_cap=(0.30, 0.10), tails=None,
                      walls=False)
    k = 0
    for (x, y) in GZ_POSTS:
        for n in (((1.0 if x < cx else -1.0), 0.0, 0.0), (0.0, (1.0 if y < cy else -1.0), 0.0)):
            at = (x + 0.11 * n[0], y + 0.11 * n[1], beam_bot - 0.01)
            P += K.bracket(f"gz{k}", at, n, span=0.60, drop=0.60)
            k += 1
    zr = GZ_TOP
    P += K.rail("gz", [(GZ_X1 - 0.05, 1.50, zr), (GZ_X1 - 0.05, GZ_Y0 + 0.05, zr),
                       (GZ_X0 + 0.05, GZ_Y0 + 0.05, zr), (GZ_X0 + 0.05, GZ_Y1 - 0.05, zr),
                       (GZ_X1 - 0.05, GZ_Y1 - 0.05, zr), (GZ_X1 - 0.05, 2.70, zr)], kind="timber")
    P += K.stair("gz", (-6.86, 2.10, TERR_TOP), (GZ_X1, 2.10, GZ_TOP), w=1.2, rise_max=0.18,
                 rail="tube", ground=False, rail_sides=(-1, 1))
    if not fine:                                   # fine: the RoofLightRandom01 row (GZ_LAMP)
        lp, _pt = K.lantern("gz", (cx, cy, 7.60 - 0.265), "+X", kind="post")
        P += lp
    ridge_under = plate + t * (ys[1] - ys[0]) / 2 - 0.10
    P += K.box_part("trim_gzrod" if fine else "lamp_gzrod", (cx - 0.02, cy - 0.02, 7.60 + 0.225),
                    (cx + 0.02, cy + 0.02, ridge_under),
                    "trim")
    return P


def build_chalet(recipe, opts):
    """The L3 parts list [(obj, matkey)] (recipe.builder for massing "custom")."""
    import cs2_bldg as K
    WallRef = K.WallRef
    assert abs(B_RIDGE_Z - 9.40) < 0.01 and abs(A_RIDGE_Z - 12.86) < 0.01, (B_RIDGE_Z, A_RIDGE_Z)
    fine = bool(getattr(recipe, "fine_detail", False))     # fine-detail step 3a (docstring)
    fk = {"fine": True} if fine else {}
    P = []

    # ---- Mass B: stone G (ground) + timber F1 ---------------------------------------------
    P += K.storey("Bg", B_X0, B_X1, B_Y0, B_Y1, 0.0, G_TOP, "stone_rubble", ground=True,
                  drop=("+Z", "-X"))
    bf1 = K.storey("Bf1", B_X0, B_X1, B_Y0, B_Y1, G_TOP, L1_TOP, "timber_lap", drop=("-Z", "+Z"))
    K.bisect_part(bf1[0][0], [((0, A_Y1, 0), (0, 1, 0))])        # covered -X strip culls
    P += bf1
    P += _roof_b(K)
    # chimney on B's +Y gable: -Y face dropped where the gable covers it
    cx0, cx1, cy0, cy1 = B_RIDGE_X - 0.70, B_RIDGE_X + 0.70, B_Y1, B_Y1 + 1.10
    P += K.box_part("chim_B", (cx0, cy0, 0.0), (cx1, cy1, 9.0), "stone_rubble", drop=("+Z", "-Y"),
                    ground=True)
    P += K.box_part("chim_Btop", (cx0, cy0, 9.0), (cx1, cy1, 11.0 - 0.24), "stone_rubble",
                    drop=("-Z", "+Z"))
    # T95 (cad-review-2026-10-19 section 6, fittings v1): a concrete mortar crown with the kit's 6 cm drip (the
    # shroud plate reads these bounds), not a galv tray
    dr, ck = (K.CHIM_DRIP, "concrete") if K.FITTINGS.get("version", 0) >= 1 else (0.15, "galv")
    P += K.box_part("chimcap_B", (cx0 - dr, cy0 - dr, 11.0 - 0.28), (cx1 + dr, cy1 + dr, 11.0), ck)
    # ---- Mass A: stone G + L1, arcade piers, jettied timber L2, roof along X ----------------
    P += K.storey("Ag", A_X0, A_X1, A_Y0, A_Y1, 0.0, G_TOP, "stone_rubble", ground=True,
                  drop=("+Z", "+X", "+Y"))
    P += K.storey("Al1", A_X0, A_X1, A_Y0, A_Y1, G_TOP, L1_TOP, "stone_rubble",
                  drop=("-Z", "+Z"))         # +X kept: contact-culls against B's F1
    pier_y = ((A_Y0, A_Y0 + 1.10), (-14.95, -13.85), (-11.15, -10.05), (A_Y1 - 1.10, A_Y1))
    for k, (y0, y1) in enumerate(pier_y):
        P += K.box_part(f"pier_A{k}", (PIER_X0, y0, 0.0), (PIER_X0 + 1.10, y1, L1_TOP),
                        "stone_rubble", drop=("+Z", "+X"), ground=True)
    al2 = K.storey("Al2", A2_X0, A_X1, A2_Y0, A2_Y1, L1_TOP, L2_TOP, "timber_lap", drop=("+Z",))
    K.bisect_part(al2[0][0], [((A_X0, 0, 0), (1, 0, 0)), ((0, A_Y0, 0), (0, 1, 0)),
                              ((0, A_Y1, 0), (0, 1, 0))])         # jetty strips = own faces
    P += al2
    specA = K.RoofSpec("gable", A2_X0, A_X1, A2_Y0, A2_Y1, L2_TOP, PITCH_DEG, axis="X",
                       overhang=EAVE_OVER, rake_over=RAKE_OVER, thick=ROOF_T, mat="slate")
    P += K.gable_roof("A", specA, tails=None, snow_guard=1, gutters=True, walls=True,
                      wall_mat="timber_shake")
    gzA = L2_TOP - T27 * EAVE_OVER - 0.18
    # close look (2026-10-17): v1's pipes stood at the jettied L2 corner and hung 6.5 m in front of the arcade
    # with nothing behind them; with fittings they come down the L2 side wall just inside the stone corner and
    # jog in under the jetty onto the stone (cs2_fittings.route_downspouts)
    ax = A_X0 + 0.35 if K.FITTINGS.get("version", 0) >= 1 else A2_X0 - 0.10
    for nm, y, n in (("AS", A2_Y0 - 0.10, "-Y"), ("AN", A2_Y1 + 0.10, "+Y")):
        P += K.downspout(nm, ax, y, gzA, n, wall_gap=EAVE_OVER + 0.20 - 0.10,
                         z_bottom=0.05)
    # ---- podium + terrace + opaque balustrade + terrace stair ------------------------------
    P += K.storey("pod", POD_X0, B_X0, A_Y1, POD_Y1, 0.0, POD_TOP, "stone_rubble", ground=True,
                  drop=("+Z", "+X", "-Y"))
    P += K.deck("terr", POD_X0, B_X0, A_Y1, POD_Y1, TERR_TOP, support="solid", fascia=0.30,
                board_dir="X", thick=TERR_TOP - POD_TOP)
    bx, by = POD_X0 + 0.05, POD_Y1 - 0.05
    s0 = (by - A_Y1) + (-9.40 - bx)                       # arc length to the stair opening
    P += K.rail("terr", [(bx, A_Y1, TERR_TOP), (bx, by, TERR_TOP), (B_X0, by, TERR_TOP)], h=1.20,
                kind="glass", gaps=((s0, s0 + 1.20),))
    P += K.deck("tland", -9.40, -8.20, POD_Y1, 6.30, TERR_TOP, support="none", fascia=0.0,
                board_dir="Y")
    for k, x in enumerate((-9.30, -8.30)):
        P += K.col(f"tland{k}", x, 6.20, 0.0, TERR_TOP - 0.16, size=0.16, ground=True, mat="galv")
    P += K.rail("tland", [(-9.40, 6.25, TERR_TOP), (-8.25, 6.25, TERR_TOP), (-8.25, POD_Y1, TERR_TOP)],
                kind="tube")
    P += K.stair("terr", (-14.72, 5.65, 0.0), (-9.40, 5.65, TERR_TOP), w=1.2, rise_max=0.18,
                 rail="tube", ground=True, rail_sides=(-1,), tread_mat="deck", stringer_mat="trim")
    # (the stair-foot lamp post is a vanilla GardenLightRandom01 since the fifteenth look: PROPS)
    # ---- Mass A balconies (L2 floor 6.80): slab + opaque timber balustrade + knee braces ----
    P += K.box_part("balc_AW", (-15.40, A2_Y0, L1_TOP), (A2_X0, A2_Y1, 6.80), "deck")
    P += K.boxes_part("balc_AWbal", [((-15.40, A2_Y0, 6.80), (-15.32, A2_Y1, 7.80)),
                                     ((-15.32, A2_Y0, 6.80), (A2_X0, A2_Y0 + 0.08, 7.80)),
                                     ((-15.32, A2_Y1 - 0.08, 6.80), (A2_X0, A2_Y1, 7.80))],
                      "timber_heavy")
    for k, y in enumerate((-17.45, -14.40, -10.60, -7.55)):
        P += K.bracket(f"AW{k}", (PIER_X0, y, L1_TOP - 0.02), "-X", span=1.85, drop=1.0)
    P += K.box_part("balc_AS", (-12.0, -19.80, L1_TOP), (0.0, A2_Y0, 6.80), "deck")
    P += K.boxes_part("balc_ASbal", [((-12.0, -19.80, 6.80), (0.0, -19.72, 7.80)),
                                     ((-12.0, -19.72, 6.80), (-11.92, A2_Y0, 7.80)),
                                     ((-0.08, -19.72, 6.80), (0.0, A2_Y0, 7.80))], "timber_heavy")
    for k, x in enumerate((-11.6, -8.25, -4.75, -1.25)):        # between the window heads
        P += K.bracket(f"AS{k}", (x, A_Y0, L1_TOP - 0.02), "-Y", span=1.75, drop=1.0)
    # ---- gazebo on the terrace ----------------------------------------------------------------
    P += _gazebo(K, fine)
    # ---- kiosk (ground part), gable along Y, hatch + awning on -X ---------------------------
    P += K.storey("kiosk", KI_X0, KI_X1, KI_Y0, KI_Y1, 0.0, KI_H, "timber_shake", ground=True,
                  drop=("+Z",))
    specK = K.RoofSpec("gable", KI_X0, KI_X1, KI_Y0, KI_Y1, KI_H, 35.0, axis="Y", overhang=0.40,
                       rake_over=0.30, thick=0.16, mat="slate")
    P += K.gable_roof("kiosk", specK, fascia=0.25, rake=0.25, ridge_cap=(0.30, 0.10), tails=None,
                      walls=True, wall_mat="timber_shake")
    wK = WallRef("-X", KI_X0)
    P += _win(K, "kh", wK, -9.75, 1.2, 0.8, 0.95, "kiosk", lights=1)
    P += K.roof_slab("flapkiosk", "Y", 9.0, 10.5, KI_X0 - 0.55, 1.92, KI_X0, 2.07, thick=0.04,
                     mat="trim", prefix="trim_")
    if not fine:                                   # fine: a WallLightShortRange row (FINE_LAMPS)
        lp, _pt = K.lantern("kiosk", tuple(K.wall_point(wK, -10.75, 1.80, 0.0)), "-X", kind="wall")
        P += lp
    P += K.sign_board("kiosk", WallRef("+X", KI_X1), 9.75, 1.40, 1.6, 0.5, mat="timber_heavy")
    t60 = T60_DOORS and fine
    if t60:                                        # T60 (a): the kiosk's door, on its -Y gable (lamp: T60_LAMPS)
        P += K.entry("kio", WallRef("-Y", KI_Y0), KIOSK_DOOR[0], w=KIOSK_DOOR[1], h=KIOSK_DOOR[2], sidelights=0,
                     lamps=0, room="kiosk", z=0.0)
    # ---- entry porch on B's +X face ---------------------------------------------------------
    px0, px1, py0, py1 = PORCH
    P += K.box_part("step_porchB", (B_X1, -8.50, 0.0), (15.50, -4.50, 0.15), "concrete", ground=True)
    for k, y in enumerate((-8.30, -4.70)):
        P += K.col(f"porchB{k}", 15.30, y, 0.15, 3.0, size=0.22)
        if not fine:                               # fine: WallLightShortRange rows (FINE_LAMPS)
            lp, _pt = K.lantern(f"porchB{k}", (15.41, y, 2.60), "+X", kind="wall")
            P += lp
    P += K.beam("porchB", (15.30, -8.42, 3.10), (15.30, -4.58, 3.10), 0.24, 0.24)
    specP = K.RoofSpec("gable", px0, px1, py0, py1, 3.20, 30.0, axis="X", overhang=0.30,
                       rake_over=0.30, thick=0.16, mat="slate")
    P += K.gable_roof("porchB", specP, fascia=0.25, rake=0.25, ridge_cap=(0.30, 0.10), tails=None,
                      walls=False)
    rz = 3.20 + math.tan(math.radians(30.0)) * 0.5 * (py1 - py0)
    P += K.prism_gable("porchBf", "X", px1 - 0.30, px1, py0, py1, 3.20, rz - 0.02,
                       ridge_at=0.5 * (py0 + py1), mat="timber_shake")
    # ---- flags (slot 2 banners) -----------------------------------------------------------------
    for k, y in enumerate(MAST_YS):
        P += K.flag_mast(f"m{k}", MAST_X, y, h=8.0, flag=(0.8, 2.4), **fk)
    # ---- facades ----------------------------------------------------------------------------------
    gs, f1s, l2s = 0.90, G_TOP + 0.90, L1_TOP + 0.90
    wBE, wBS, wBN, wBW = (WallRef("+X", B_X1), WallRef("-Y", B_Y0), WallRef("+Y", B_Y1),
                          WallRef("-X", B_X0))
    # B +X (arrival): two storefronts, double door + porch, 5 F1 windows, red board
    P += K.window_bay("bE0", wBE, -16.0, -12.0, K.WinSpec(3.6, 2.2, 0.60, lights=3), room="bGE")
    P += K.window_bay("bE1", wBE, -2.0, 3.0, K.WinSpec(4.5, 2.2, 0.60, lights=3), room="bGE")
    P += K.entry("Be", wBE, -6.5, w=1.8, h=2.4, sidelights=0, lamps=0 if fine else 2, room="bGE",
                 z=0.15)
    for k, y in enumerate((-16.0, -12.5, -2.5, 0.5, 3.5)):
        P += _win(K, f"bE{k}f", wBE, y, *WIN, 4.40, "bF1E")
    P += K.sign_board("boardB", wBE, -12.0, 3.50, 4.0, 0.6, mat="timber_heavy")
    # B -Y gable: G 2, F1 2, attic 1
    for k, x in enumerate((5.0, 10.0)):
        P += _win(K, f"bS{k}", wBS, x, *WIN, gs, "bGS")
        P += _win(K, f"bS{k}f", wBS, x, *WIN, f1s, "bF1S")
    P += _win(K, "bSa", wBS, B_RIDGE_X, 0.9, 1.0, 7.20, "bF1S", lights=1)
    # B +Y gable: G door + window, F1 2; lantern by the door (chimney at x 6.8-8.2)
    P += K.entry("Bn", wBN, -4.6, w=1.0, h=2.1, sidelights=0, lamps=0, room="bGN")
    P += _win(K, "bN", wBN, -10.5, *WIN, gs, "bGN")
    for k, x in enumerate((4.6, 10.5)):
        P += _win(K, f"bN{k}f", wBN, -x, *WIN, f1s, "bF1N")
    if not fine:
        lp, _pt = K.lantern("Bn", tuple(K.wall_point(wBN, -6.0, 2.30, 0.0)), "+Y", kind="wall")
        P += lp
    # B -X (onto the terrace): 3 glazed F1 doors
    for k, y in enumerate((-5.0, -1.0, 3.0)):
        if t60 and abs(y - PATIO_DOOR[0]) < 1e-9:
            continue                               # T60 (b): bW1 is the patio double door below
        P += _win(K, f"bW{k}", wBW, -y, *GDOOR, TERR_TOP + 0.05, "bF1W", sill_board=False)
    if t60:                                        # T60 (b): chalet double door on the terrace deck, glass in bF1W
        P += K.entry("bWd", wBW, -PATIO_DOOR[0], w=PATIO_DOOR[1], h=PATIO_DOOR[2], sidelights=0, lamps=0,
                     room="bF1W", z=TERR_TOP)
    # A -X arcade: curtain glazing between the piers (G + L1 rows, transom 3.40)
    wAW = WallRef("-X", A_X0)
    bays = ((pier_y[0][1], pier_y[1][0]), (pier_y[1][1], pier_y[2][0]), (pier_y[2][1], pier_y[3][0]))
    for k, (y0, y1) in enumerate(bays):
        P += K.curtain_wall(f"aW{k}", wAW, -y1, -y0, 0.20, 6.40, grid_u=1.0, transom_at=3.20,
                            room="aW")
    # A L2 -X: 2 balcony doors + 2 windows; lantern between the doors
    wA2W = WallRef("-X", A2_X0)
    for k, y in enumerate((-14.0, -11.0)):
        P += _win(K, f"aWd{k}", wA2W, -y, 0.9, 2.2, 6.90, "aL2W", sill_board=False)
    for k, y in enumerate((-16.8, -8.2)):
        P += _win(K, f"aWw{k}", wA2W, -y, *WIN, l2s, "aL2W")
    if not fine:
        lp, _pt = K.lantern("aW", tuple(K.wall_point(wA2W, 12.5, 8.80, 0.0)), "-X", kind="wall")
        P += lp
    # A -Y: G 4, L1 4 (stone), L2 2 windows + 2 balcony doors
    wAS, wA2S = WallRef("-Y", A_Y0), WallRef("-Y", A2_Y0)
    for k, x in enumerate((-10.0, -6.5, -3.0, 0.5)):
        P += _win(K, f"aS{k}", wAS, x, *WIN, gs, "aGS")
        P += _win(K, f"aS{k}f", wAS, x, *WIN, f1s, "aL1S")
    for k, x in enumerate((-10.5, -1.5)):
        P += _win(K, f"aS{k}w", wA2S, x, *WIN, l2s, "aL2S")
    for k, x in enumerate((-7.5, -4.5)):
        P += _win(K, f"aS{k}d", wA2S, x, 0.9, 2.2, 6.90, "aL2S", sill_board=False)
    # A +Y: L1 3 glazed doors onto the terrace, L2 3 windows
    wAN, wA2N = WallRef("+Y", A_Y1), WallRef("+Y", A2_Y1)
    for k, x in enumerate((-10.0, -6.0, -2.0)):
        P += _win(K, f"aN{k}d", wAN, -x, *GDOOR, TERR_TOP + 0.05, "aL1N", sill_board=False)
        P += _win(K, f"aN{k}w", wA2N, -x, *WIN, l2s, "aL2N")
    # podium -X: 2 windows + door; +Y: 2 windows + door with a lantern, red board above
    wPW, wPN = WallRef("-X", POD_X0), WallRef("+Y", POD_Y1)
    for k, y in enumerate((-4.5, 3.0)):
        P += _win(K, f"pW{k}", wPW, -y, *WIN, gs, "podW")
    P += K.entry("podW", wPW, 1.0, w=1.0, h=2.1, sidelights=0, lamps=0, room="podW")
    for k, x in enumerate((-7.0, 0.0)):
        P += _win(K, f"pN{k}", wPN, -x, *WIN, gs, "podN")
    P += K.entry("podN", wPN, 3.0, w=1.0, h=2.1, sidelights=0, lamps=0 if fine else 1, room="podN")
    P += K.sign_board("boardP", wPN, 3.0, 2.60, 4.0, 0.6, mat="timber_heavy")
    # ---- corner boards on the timber stories ------------------------------------------------------
    cb = [(x, y, G_TOP, L1_TOP) for x, y in ((B_X1, B_Y0), (B_X1, B_Y1), (B_X0, B_Y1))] + \
         [(x, y, L1_TOP, L2_TOP) for x, y in ((A2_X0, A2_Y0), (A2_X0, A2_Y1), (A_X1, A2_Y0),
                                               (A_X1, A2_Y1))]
    P += K.boxes_part("trim_corners", [((x - 0.09, y - 0.09, z0), (x + 0.09, y + 0.09, z1))
                                       for x, y, z0, z1 in cb], "trim")
    # ---- C11 -----------------------------------------------------------------------------------------
    bad = sorted(o.name for o, _m in P if not o.name.startswith(PREFIXES + K.KIT2_PARTS))  # + window kit v2
    if bad:
        raise RuntimeError(f"L3: parts outside the prefix table: {bad[:8]}")
    return P


RECIPE = FineRecipe(
    key="L3", aliases=("mid",), letter="B",
    # prefab = the code catalog's name (SkiCatalog.Names.LodgeMid, LocaleEN, hub; R25 /
    # bldg-kit 11). The mesh keeps its built name (no consumer reads it). buildings-integration 1
    asset="SkiLodgeChaletCommercialLow", prefab="SkiLodgeMid", category="lodges",
    lot=LOT, base_mode="engine", plinth=None, yaw_deg=0.0, tex=2048, allow_4096=False,
    budget=Budget(11000, 12000, 4800, 500, 500),
    massing="custom", builder=build_chalet,
    rooms=ROOMS, interior="CommercialLow",
    # one resort, one trim (lodges-basic Q6): L1's shipped green; slot 2 = house orange
    # (banners); slot 3 inert (no slot-3 surface on L3)
    slots={1: (0.0625, 0.3125, 0.25), 2: (0.85, 0.25, 0.10), 3: (0.30, 0.31, 0.325)},
    ground_prefixes=("wall_Bg", "wall_Ag", "wall_pod", "wall_kiosk", "pier_A", "chim_B",
                     "step_porchB", "stair_terr_str", "col_tland"),
    arrival_prefixes=("entry_Be", "step_porchB", "roof_porchB"),
    # fine-detail step 3a: the gazebo rod (trim_gzrod, was lamp_gzrod on the kit's LOD1 list) leaves at
    # LOD1 as before (trim_ is on the kit's LOD2 list)
    lod1_extra=("trim_gzrod",),
    lod2_extra=("col_porchB", "stair_gz", "stair_terr_treads", "deck_tland", "deck_gz"),
    lod2_keep=("gazebocol_gz", "flagpanel_m0", "flagpanel_m1", "flagpanel_m2"),
    labels=NC_LABELS,                 # nitpick C (2026-10-13): blank boards (was PICTO_LABELS, kept in legacy)
    sub_objects=SUB_OBJECTS + FINE_SUB_OBJECTS + KIT_ROWS + T60_SUB_OBJECTS,   # 29 PROPS + 9 vanilla + 3 kit (+ 2 T60)
    # fine-detail pass step 3a (opt-in; lodge.py --no-fine restores `legacy` = the approved chalet)
    fine_detail=True,
    sign_set=None,
    kit_colour_set=KIT_COLOUR_SET,
    legacy=(("sub_objects", SUB_OBJECTS), ("lod1_extra", ()), ("labels", PICTO_LABELS)),
    cull_ok=(),
    # post-orient Blender frame (authored (ax, ay) -> (ay, -ax)): terrace 3/4 from authored
    # (-1, +1) -> (1, 1)
    views=(("terrace", (0.9, 0.9, 0.45)),),
    notes="lodges-basic 5 on the kit: two masses at right angles, pier arcade, terrace + "
          "gazebo (gable, photo), kiosk, porch, 3 masts; tables / parasols / benches / lights "
          "/ ski racks as props (PROPS). Vanilla retrofit (fifteenth look, 2026-10-11): PROPS "
          "wired as Recipe.sub_objects (29 rows), parasols beside their tables (vanilla "
          "convention), balcony cafe chairs, stair-foot lamp post -> GardenLight",
)
