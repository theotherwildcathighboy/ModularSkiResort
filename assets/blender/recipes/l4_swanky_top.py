"""
recipes/l4_swanky_top.py - L4 "swanky top" summit lodge (lux-top): glass-and-stone restaurant
stepping DOWN the slope on a stone plinth, no flatten, no engine base.

Contract: docs/research/art/assets-2026-10/design/lodges-swanky.md section 4 (L4) with
design/lodges-swanky-skeptic.md applied: MUST-CHANGE 2 (supported fall 17-30 %, C5 tests the
dining floor and the terrace deck at their UPHILL end X +2 at every supported slope and the
forecourt skirt at 30 %), 3 (plinth walls on Y 0 and X +2 down to the plinth bottom, a
balustrade on the terrace's Y 0 edge, stair S re-routed forecourt -> terrace with walkways
and a skirt; "plinth outline closed" and "stair ends on walkable surfaces" asserted), 4 (C4
T centroid corrected), 9 (kit API), 11 (no default bounds clamp: prefab side). R21 / R25 as
L5. Build report: docs/research/art/assets-2026-10/build/lodges-swanky.md.

REVISION 1 (2026-10-12, verdicts-2026-10-11.md "Eighteenth look": L4 "needs the in-game assets
pass that the others have gotten"; "make the DECK EXTEND OUT OVER THE EDGE via supports similar
to the indoor part"; "indoor part needs a DOOR to the outdoor part"; "roof GUTTER goes through the
door entrance on the front"; "does it look real" detail): (a) every modelled umbrella / stand /
table disc / post lantern / planter / entry lantern is a vanilla sub-object now (L4_ROWS ->
manifest `sub_objects`; GUIDs = recipes/l5_swanky_bottom.VANILLA, verified against the dump by
tests/test_lodges_swanky.py; parasols / loungers ForbidSnow, accepted), deck chairs on the
cantilever face the view; (b) the terrace deck runs out to T_X0 -19.6 as a 5.3 m CANTILEVER past
the plinth face (W1 moved in to T_WALL -14.0, in line with the dining room's west end) on the
dining room's support family: glulam joists, glulam struts down onto the W1 buttress piers, a
metal fascia, the concrete kerb + glass balustrade continued round the new edge; the cantilever
undersides (deck AND dining room) are protected soffit quads (`soffit_cant*` in ground_prefixes:
plinth-mode cull_hidden deletes every down-facing face below grade, so the dining room's
underside had shipped see-through); (c) a French door from the glass dining room onto the deck
(SW.terrace_door in a REAL recessed opening of the glass wall's bay x -6.9..-4.8 between two
glulam posts, clear 1.80 x 2.30, threshold flush with the deck); (d) the entry porch tucked UNDER
the main eave (PORCH_UNDER_EAVE: 30 deg, eave 3.0, ridge cap 0.17 m under the main gutter, which
now runs continuous over it), porch gutters with downspouts on the wall beside the entry, the F1
window behind the porch roof dropped; contract E1 (no gutter / downspout meets the
entrance portal or any door's clear box; PORCH_UNDER_EAVE False = revision 0, which fails it).
Build report section "Revision 1 (vanilla pass, cantilevered deck, terrace door, gutter)".

FINE-DETAIL PASS, step 3e (2026-10-12, verdicts-2026-10-11.md "Twenty-third look": "L5 and L4 look
good, run the engine pass on them too!"; design/fine-detail-pass.md A.2 / B / C as cut by -skeptic.md;
build report docs/research/art/assets-2026-10/build/fine-detail-step3e-swanky.md): RECIPE is a
fine_detail.FineRecipe (fine_detail=True, NO sign set); `lodge.py --no-fine` rebuilds the approved
revision 1 (byte-identical, proven in the build report). What the pass found on L4: no modelled ski
rack (nothing for the dressing kit); every lantern already a vanilla row since revision 1 (the entry's
two WallLightShortRangeRandom01 on the U front wall, the post lanterns LightpoleParkRandom01); the
forecourt board is a fork-and-knife PICTOGRAM, no lettering (design C.1: pictogram boards stay in the
host atlas, so no sign set and the shared sheet is untouched). Fine geometry (design B.1): the four
flue pots 12-seg (were 8). Kept as built: the kit handrail posts / rails (cs2_kit 6-gons like every
station's: an 8-gon would put post flats in the rails' planes, kit need K21), the glass balustrade's
box posts and the glulam columns (boxes stay boxes, B.1), the 12 deck LED pucks (PLACEHOLDER).

NITPICK C (2026-10-13; Richard, twenty-fifth look: "the roof of the lower part is a different style, but we can say
it's lore ... Same issue with the sign outside as the chalet."; build/nitpick-c-lodges.md): the forecourt board's
fork-and-knife becomes a BLANK board (NC_LABELS: sign cream, a thin graphite trim rim; fine_detail.blank_board),
listed in the manifest's `we_boards` block for an optional Write Everywhere layout; `--no-fine` keeps the
pictogram (PICTO_LABELS, legacy). The roof: lore, unchanged. Geometry unchanged: only the label rect repaints.

Frame: +X = arrival (the lot front, game +Z); z 0 = terrain at the lot-front centre (RoadSide
pivot, contract 0.1); the lodge steps down toward -X. Lot 6 x 5 cells -> |Y| <= 24, |X| <= 20.
Plinth mode (bldg-kit.md section 3): no vertex in |z| <= 0.01 anywhere, min z = Z_PLINTH (-16.00 since T24; was -12.00), no
engine-base edges (check_base_mode "plinth" on LOD0/1/2 in lodge.py).

Design terrain t_S(X) = -S (20 - X), S_D = 0.20; supported S in [0.17, 0.30].
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

from bldg_recipes import Budget, Recipe   # noqa: E402
from fine_detail import FineRecipe, blank_board   # noqa: E402  (bpy-free)


def _sw():
    """The shared swanky helpers (recipes/l5_swanky_bottom.py)."""
    from recipes import l5_swanky_bottom as SW
    return SW


# ======================================================================================
# Constants (contract 4.1 / 4.2, authoring metres)
# ======================================================================================

LOT_W, LOT_D = 6, 5
HX, HY = 4.0 * LOT_D, 4.0 * LOT_W                 # 20, 24
S_DESIGN, S_MIN, S_MAX = 0.20, 0.17, 0.30          # skeptic MUST-CHANGE 2 (was 8-30 %)
CROSS_FALL = 0.06                                   # +-6 % across the forecourt (contract 4.1)

Z_ARRIVAL = 0.15                                    # forecourt + U ground floor
Z_LOUNGE = 3.60                                     # U first floor
Z_DINING = -2.70                                    # D floor
Z_DSLAB = -3.10                                     # D slab bottom
Z_DECK = -2.55                                      # terrace deck top
# T24 (2026-10-17, playtest item 72: "maybe the foundation could go a tad deeper"): plinth mode draws NO engine
# skirt (ok_edges 0) and the prefab does not flatten, so the modelled plinth bottom is the only foundation.
# -12.00 (revision 1 .. fittings close look) is flat on every wall and only 1.7 m under t30 at W1 (-10.3);
# a cliff steeper than ~35 % leaves the wall bottoms hanging in the air. DEEP_PLINTH False = the old -12.00.
Z_PLINTH_OLD, Z_PLINTH_DEEP = -12.00, -16.00
DEEP_PLINTH = True
Z_PLINTH = Z_PLINTH_DEEP if DEEP_PLINTH else Z_PLINTH_OLD   # plinth bottom (C2); was -12.00 before T24

U_X0, U_X1, U_Y0, U_Y1 = 2.0, 14.0, -15.0, 9.0
U_BOTTOM, U_EAVE, U_RIDGE_X = -6.0, 7.0, 8.0
D_X0, D_X1, D_Y0, D_Y1 = -14.0, 2.0, -20.0, 0.0
D_EAVE, D_PITCH = 1.5, 20.0                         # 22 deg in the contract: 20 clears U's soffit
F_X0, F_X1, F_Y0, F_Y1 = 14.0, 19.8, -15.0, 10.6   # forecourt (to 10.6: meets the walkway)
W_T = 0.6                                           # plinth wall thickness
ROUTE_Y0, ROUTE_Y1 = 9.0, 10.6                      # walkway / stair corridor (U's north face)
STAIR_FOOT_X, STAIR_TOP_X = 4.0, 8.5                # contract S: X +4.0..+8.5, 16 risers

# revision 1 (b): the terrace deck T cantilevers past the plinth. W1 (was under the deck edge at
# X -17) moves in to T_WALL, in line with the dining room's west end D_X0, and the deck runs out
# to T_X0 (0.4 m inside the lot's back edge with its kerb and fascia): 5.3 m past the plinth face,
# the dining room's 5 m cantilever over W3 repeated on the same support family
T_X0, T_X1, T_Y0, T_Y1 = -19.6, 2.0, 0.0, 23.0
T_WALL = D_X0                                       # -14.0: the W1 centre line
CANT_FACE = T_WALL - W_T / 2                        # -14.3: the plinth face the deck leaves
CANT_LINES = (0.3, 5.75, 11.5, 17.25, 22.7)         # joists + struts, over the W1 buttress piers
W1_PIERS = (0.0, 5.75, 11.5, 17.25, 23.0)           # the buttress piers on W1 (contract 2)
T_JOIST = (0.30, 0.40)                              # glulam joist w x h under the deck slab
T_STRUT_FOOT_Z = -6.4                               # the struts land on the piers here
CANT_MIN = 4.5                                      # R1b: the deck tip >= this past the plinth face

# revision 1 (d): the entry porch tucked UNDER the main eave so the main gutter runs continuous
# over it (revision 0: 45 deg, eave 4.2, ridge 7.7 - the porch roof pierced the eave and the
# gutter / fascia crossed the open entry gable). False rebuilds revision 0 (tests: E1 negative)
PORCH_UNDER_EAVE = True
EG_Y0, EG_Y1, EG_X1 = -6.0, 1.0, 15.3               # porch wall lines and front (unchanged)


def porch():
    """(eave, pitch deg, roof x0, pier top) of the entry porch for the current PORCH_UNDER_EAVE:
    revision 1 30 deg / eave 3.0 (ridge cap top 5.35 under the main gutter's 5.52) on knee-high
    stone bases; revision 0 45 deg / eave 4.2 (ridge 7.7, through the main eave)."""
    if PORCH_UNDER_EAVE:
        return 3.0, 30.0, 13.8, Z_ARRIVAL + 0.80
    return 4.2, 45.0, 12.6, Z_ARRIVAL + 1.60

# revision 1 (c): the dining room's terrace door, in the north glass wall's bay between the glulam
# posts at x -6.9 / -4.8 (post faces 1.80 m apart = the clear opening; the posts are its jambs)
DD_X = -5.85
DD_BAY = (-6.9, -4.8)


def t_at(x, s=S_DESIGN):
    """Design terrain under the lodge (fall toward -X), z 0 at the lot front X +20."""
    return -s * (HX - x)


# The plinth outline (centre lines of the walls / the U base face), closed (skeptic MC3):
# W1 X T_WALL, W2 Y +23, W5 X +2 (Y 9..23), U west face (Y -15..9), W6 X +2 (Y -20..-15),
# W4 Y -20, W3 X -9, W7 Y 0 (X -9..T_WALL). Revision 1: W1 moved in from X -17 to T_WALL = D_X0,
# so the old W7a (X -17..-14 on Y 0) is gone.
OUTLINE = (("W1", (T_WALL, 0.0), (T_WALL, 23.0)), ("W2", (T_WALL, 23.0), (2.0, 23.0)),
           ("W5", (2.0, 23.0), (2.0, 9.0)), ("Uw", (2.0, 9.0), (2.0, -15.0)),
           ("W6", (2.0, -15.0), (2.0, -20.0)), ("W4", (2.0, -20.0), (-9.0, -20.0)),
           ("W3", (-9.0, -20.0), (-9.0, 0.0)), ("W7", (-9.0, 0.0), (T_WALL, 0.0)))
ARRIVAL_PREFIXES = ("deck_fc", "entry_u", "roof_ueg", "pier_ueg", "col_ueg")


# ======================================================================================
# Revision 1 (a): vanilla props by reference (DECISIONS 183; Richard's eighteenth look "needs
# the in-game assets pass that the others have gotten"). GUIDs / bounds / snow flags come from
# recipes/l5_swanky_bottom.VANILLA (one table for both lodges, re-derived from the dump by the
# test). (prefab, x, y, z, yaw deg, m_ParentMesh, zone), authoring frame (no shift): yaw turns
# the prop's local +Z to 0 = +X (road), 90 = +Y, 180 = -X (the view), -90 = -Y.
# ======================================================================================

_T, _P, _C, _B = "TableSetCommercialRandom01", "ParasolRandom01", "DeckChairRandom01", "GardenBenchRandom01"
_BIN, _L, _POT, _H = "TrashbinRandom01", "LightpoleParkRandom01", "PotRandom01", "GasHeaterRandom01"
_WL = "WallLightShortRangeRandom01"
L4_TABLES = ((-12.0, 6.0), (-6.0, 6.0), (-12.0, 15.0), (-6.0, 15.0))   # the revision-0 umbrella spots


def _parasol(tx, ty, z, yaw, zone):
    """A parasol PARASOL_OFFSET behind its table, local +Z at it (the vanilla convention)."""
    return _sw()._parasol(tx, ty, z, yaw, zone)


L4_ROWS = (
    # --- the terrace deck: four cafe tables at the revision-0 umbrella spots with their parasols
    # (poles on the aisle toward the dining room), winter gas heaters between the rows, two
    # benches on the north kerb, a bin by the stair walkway, pots in the corners, lamp posts on
    # the west and north edges
    *((_T, x, y, Z_DECK, 90.0, 0, "terrace") for x, y in L4_TABLES),
    # T26 2026-10-16 dropped the 4 terrace + 2 cantilever parasols (random yellow); restored
    # 2026-10-17 (Richard: vanilla props, random colours accepted)
    *(_parasol(x, y, Z_DECK, 90.0, "terrace") for x, y in L4_TABLES),
    (_H, -9.0, 10.5, Z_DECK, 0.0, 0, "terrace"), (_H, -9.0, 19.5, Z_DECK, 0.0, 0, "terrace"),
    (_B, -11.7, 22.1, Z_DECK, -90.0, 0, "terrace"), (_B, -4.5, 22.1, Z_DECK, -90.0, 0, "terrace"),
    (_BIN, 0.9, 12.0, Z_DECK, 0.0, 0, "terrace"),
    (_POT, -13.3, 1.1, Z_DECK, 0.0, 0, "terrace"), (_POT, 0.9, 1.1, Z_DECK, 0.0, 0, "terrace"),
    (_POT, 0.9, 21.9, Z_DECK, 0.0, 0, "terrace"), (_POT, -8.1, 21.9, Z_DECK, 0.0, 0, "terrace"),
    (_L, -18.9, 0.75, Z_DECK, 0.0, 0, "terrace"), (_L, -18.9, 13.1, Z_DECK, 0.0, 0, "terrace"),
    (_L, -18.9, 22.25, Z_DECK, 0.0, 0, "terrace"), (_L, -0.9, 22.25, Z_DECK, -90.0, 0, "terrace"),
    # --- the cantilever: deck chairs facing the view (-X) in two groups of three, a parasol
    # behind each group (canopy over the chairs; ForbidSnow, accepted)
    *((_C, -17.4, y, Z_DECK, 180.0, 0, "cantilever") for y in (2.5, 4.3, 6.1, 16.9, 18.7, 20.5)),
    (_P, -15.5, 4.3, Z_DECK, 180.0, 0, "cantilever"), (_P, -15.5, 18.7, Z_DECK, 180.0, 0, "cantilever"),
    # --- the forecourt: lamp posts at the revision-0 lantern spots, a bench and a bin, pots
    # either side of the porch; the entry's two wall lanterns -> vanilla short-range wall lights
    # (Richard H4) on the U front wall beside the door surround
    (_L, 16.5, -7.5, Z_ARRIVAL, 0.0, 0, "forecourt"), (_L, 16.5, 2.5, Z_ARRIVAL, 0.0, 0, "forecourt"),
    (_B, 18.6, -5.4, Z_ARRIVAL, 0.0, 0, "forecourt"), (_BIN, 18.9, -7.4, Z_ARRIVAL, 0.0, 0, "forecourt"),
    (_POT, 16.4, -5.4, Z_ARRIVAL, 0.0, 0, "forecourt"), (_POT, 16.4, 0.4, Z_ARRIVAL, 0.0, 0, "forecourt"),
    (_WL, U_X1, -5.10, Z_ARRIVAL + 2.2, 0.0, 0, "entry"), (_WL, U_X1, 0.10, Z_ARRIVAL + 2.2, 0.0, 0, "entry"),
)
# FlagPole01_Clt (no harvestable GUID) stays a surgery proposal: two on the forecourt, one on
# the deck's north-west corner (game (x, y, z) = authoring (y, z, x))
FLAG_PROPOSALS = ((19.3, -10.0, Z_ARRIVAL), (19.0, 6.0, Z_ARRIVAL), (-19.0, 21.6, Z_DECK))


def _sub_objects():
    SW = _sw()
    return tuple((pf, SW.VANILLA[pf]["guid"], (round(float(y), 4), round(float(z), 4),
                                               round(float(x), 4)), SW._yaw_q(yaw), int(parent))
                 for pf, x, y, z, yaw, parent, _zone in L4_ROWS)


# modelled parts that stand in for a vanilla prop or keep a built-in fixture (verdict "let me
# know if they are placeholders"): (part prefixes, what, why)
PLACEHOLDERS = (
    (("lamp_deck",), "12 deck LED pucks along the balustrade kick",
     "built-in flush deck lights (no vanilla flush light; the nearest, GardenLightRandom01, is a "
     "0.62 m bollard that would stand in the deck chairs' and tables' walkway); kept, emissive"),
)


def _plinth_wall(SW, tag, a, b, top, outer, bottom=Z_PLINTH, t=W_T, stone="stone_rubble"):
    """Plinth wall on the centre line a -> b (axis-aligned), stone above the design terrain
    + 0.3, rock (`rock_`) below (contract R: the rock band, split along z = t(X) + 0.3).
    `outer` = the side ("-X"/"+X"/"-Y"/"+Y") that faces out; the face toward the closed
    plinth hollow is not built (walls are only contact-culled: it would ship as texels)."""
    P = []
    (ax, ay), (bx, by) = a, b
    if abs(ay - by) < 1e-9:                      # along X: sloped split, prism in XZ
        caps = (outer == "-Y", outer == "+Y")
        x0, x1 = sorted((ax, bx))
        x0, x1 = x0 - t / 2, x1 + t / 2
        y0, y1 = ay - t / 2, ay + t / 2
        zs0, zs1 = t_at(x0) + 0.3, t_at(x1) + 0.3
        P += SW.prism_xz(f"plinth_{tag}", [(x0, zs0), (x1, zs1), (x1, top), (x0, top)], y0, y1,
                         stone, caps=caps)
        P += SW.prism_xz(f"rock_{tag}", [(x0, bottom), (x1, bottom), (x1, zs1), (x0, zs0)], y0, y1,
                         "lux_rock", caps=caps)
    else:                                        # along Y: horizontal split
        caps = (outer == "-X", outer == "+X")
        y0, y1 = sorted((ay, by))
        y0, y1 = y0 - t / 2, y1 + t / 2
        x0, x1 = ax - t / 2, ax + t / 2
        zs = t_at(ax) + 0.3
        P += SW.prism_yz(f"plinth_{tag}", [(y0, zs), (y1, zs), (y1, top), (y0, top)], x0, x1, stone,
                         caps=caps)
        P += SW.prism_yz(f"rock_{tag}", [(y0, bottom), (y1, bottom), (y1, zs), (y0, zs)], x0, x1,
                         "lux_rock", caps=caps)
    return P


def l4_storey_lines(xc, yc):
    """Storey lines of the mass a pane belongs to (C8)."""
    if xc < U_X0 - 0.3:
        return (D_EAVE,)                         # dining room: floor -2.7, eave 1.5
    return (Z_ARRIVAL, Z_LOUNGE, U_EAVE)


def entrance_boxes(dd_door=None):
    """E1 clear boxes (world AABBs, label first): the entrance portal (between the porch posts'
    inner faces, from the U front wall to the porch's front rake, from the forecourt to the
    porch roof's underside at its apex), the entry door's clear box (its leaves' 2.4 x 2.7 m
    from the door face 1.0 m past the porch) and the dining door's (SW.clear_box)."""
    SW = _sw()
    eave, pitch, _x0, _pier = porch()
    tp = math.tan(math.radians(pitch))
    apex_under = eave + 0.5 * (EG_Y1 - EG_Y0) * tp - 0.12 / math.cos(math.radians(pitch))
    out = [("entrance portal", ((U_X1 + 0.20, EG_Y0 + 0.8, Z_ARRIVAL + 0.02),
                                (EG_X1 + 0.3, EG_Y1 - 0.8, apex_under))),
           ("entry door", ((U_X1 + 0.20, -2.5 - 1.2, Z_ARRIVAL + 0.02),
                           (EG_X1 + 1.3, -2.5 + 1.2, Z_ARRIVAL + 2.7)))]
    if dd_door is not None:
        out.append(("dining door", SW.clear_box(dd_door)))
    return out


def build_l4(recipe, opts):
    """Parts of L4 (custom massing). Order is fixed (determinism); every name unique."""
    K = _sw()._kit()
    SW = _sw()
    from cs2_bldg import RoofSpec, WallRef, WinSpec
    SW.register_materials()
    SID, STN, GLU = "lux_siding_walnut", "stone_rubble", "lux_glulam"
    fine = bool(getattr(recipe, "fine_detail", False))     # fine-detail step 3e (module docstring)
    P = []

    # ---------------- U arrival block: stone -6.0..3.6, timber 3.6..7.0 --------------------
    P += K.storey("ustone", U_X0, U_X1, U_Y0, U_Y1, U_BOTTOM, Z_LOUNGE, STN, drop=("-Z", "+Z"),
                  cut_z=(Z_DSLAB, Z_DECK, Z_ARRIVAL, D_EAVE))
    K.bisect_part(P[-1][0], [((0, D_Y1, 0), (0, 1, 0))])
    P += K.storey("utimber", U_X0, U_X1, U_Y0, U_Y1, Z_LOUNGE, U_EAVE, SID, drop=("-Z", "+Z"))
    uspec = RoofSpec("gable", U_X0, U_X1, U_Y0, U_Y1, U_EAVE, 45.0, axis="Y", overhang=1.2,
                     rake_over=1.2, ridge_at=U_RIDGE_X, thick=0.30, mat="lux_slate")
    P += K.gable_roof("u", uspec, walls=True, wall_mat=SID, gutters=True, snow_guard=1,
                      tails=1.4)
    P += K.boxes_part("trim_ubelt", [
        ((U_X1, U_Y0 - 0.1, Z_LOUNGE - 0.05), (U_X1 + 0.1, U_Y1 + 0.1, Z_LOUNGE + 0.2), ("-X",)),
        ((U_X0 - 0.1, U_Y0 - 0.1, Z_LOUNGE - 0.05), (U_X0, U_Y1 + 0.1, Z_LOUNGE + 0.2), ("+X",)),
        ((U_X0, U_Y0 - 0.1, Z_LOUNGE - 0.05), (U_X1, U_Y0, Z_LOUNGE + 0.2), ("+Y",)),
        ((U_X0, U_Y1, Z_LOUNGE - 0.05), (U_X1, U_Y1 + 0.1, Z_LOUNGE + 0.2), ("-Y",))], "trim")
    P += SW.corner_boards("ucorners", [(U_X0, U_Y0), (U_X0, U_Y1), (U_X1, U_Y0), (U_X1, U_Y1)],
                          Z_LOUNGE + 0.2, U_EAVE)
    # stone quoins on the U corners (alternating long / short legs, 0.05 proud)
    qb = []
    for (cx, cy, sx, sy, z0) in ((U_X1, U_Y0, 1, -1, Z_ARRIVAL), (U_X1, U_Y1, 1, 1, Z_ARRIVAL),
                                 (U_X0, U_Y1, -1, 1, Z_DECK)):
        z = z0 + 0.05
        k = 0
        while z + 0.42 <= Z_LOUNGE - 0.1:
            la, lb = (0.9, 0.5) if k % 2 == 0 else (0.5, 0.9)
            x_in, y_in = cx - sx * la, cy - sy * lb
            qb.append(((min(cx, x_in), min(cy, cy + sy * 0.05), z), (max(cx, x_in), max(cy, cy + sy * 0.05),
                                                                     z + 0.40)))
            qb.append(((min(cx, cx + sx * 0.05), min(cy, y_in), z), (max(cx, cx + sx * 0.05), max(cy, y_in),
                                                                     z + 0.40)))
            z += 0.45
            k += 1
    P += K.boxes_part("trim_uquoins", qb, STN)
    for k, (y, nrm) in enumerate(((U_Y0 + 0.12, "+X"), (U_Y1 - 0.12, "+X"))):
        P += K.downspout(f"u{k}", U_X1 + 0.06, y, U_EAVE - 1.2 - 0.12, nrm, wall_gap=1.15,
                         z_bottom=Z_ARRIVAL + 0.05)
    ufront, uback = WallRef("+X", U_X1), WallRef("-X", U_X0)
    # GF front windows 1.6 x 2.0 (Y -13, -9.5, +4, +7.5), entry glazed double door + sidelights
    for j, y in enumerate((-13.0, -9.5, 4.0, 7.5)):
        P += K.window_bay(f"ug{j}", ufront, y - 1.0, y + 1.0, WinSpec(1.6, 2.0, Z_ARRIVAL + 0.9,
                                                                       lights=2),
                          room=f"ugf{j % 2}{j // 2}")
    # revision 1 (a): the entry's two wall lanterns are vanilla short-range wall lights (L4_ROWS)
    P += K.entry("u", ufront, -2.5, w=2.4, h=2.7, sidelights=2, lamps=0 if PORCH_UNDER_EAVE else 2,
                 z=Z_ARRIVAL, room="uentry", surround_mat=GLU, sidelight_w=0.7)
    # F1 front (Y -13, -9, -2.5 three lights under the porch gable, +3, +7). Revision 1 (d): the
    # porch is tucked under the eave and its roof stands in front of the old -2.5 window, so that
    # one goes (a clerestory above the ridge read as a window cut by the ridge cap); the room
    # uff0 keeps uf0 / uf4
    for j, (y, w, lt) in enumerate(((-13.0, 1.6, 2), (-9.0, 1.6, 2), (-2.5, 2.2, 3), (3.0, 1.6, 2),
                                    (7.0, 1.6, 2))):
        if j == 2 and PORCH_UNDER_EAVE:
            continue
        P += K.window_bay(f"uf{j}", ufront, y - w / 2 - 0.3, y + w / 2 + 0.3,
                          WinSpec(w, 1.4, Z_LOUNGE + 0.85, lights=lt), room=f"uff{j % 2}")
    # back (terrace side, X +2 face, u = -y): L-1 + GF windows on Y 0..9, F1 windows + the
    # balcony door; the F1 back south of Y 0 sits over the dining-room roof (blank)
    for j, y in enumerate((3.0, 7.0)):
        P += K.window_bay(f"ul{j}", uback, -y - 1.0, -y + 1.0, WinSpec(1.6, 1.5, Z_DECK + 0.65,
                                                                       lights=2), room="ulow")
        P += K.window_bay(f"ub{j}", uback, -y - 1.0, -y + 1.0, WinSpec(1.6, 2.0, Z_ARRIVAL + 0.9,
                                                                       lights=2), room="ubgf")
    for j, (y, w, h, sill) in enumerate(((2.5, 1.4, 1.5, Z_LOUNGE + 0.85),
                                         (4.5, 1.0, 2.3, Z_LOUNGE + 0.12),
                                         (6.5, 1.4, 1.5, Z_LOUNGE + 0.85))):
        P += K.window_bay(f"ubf{j}", uback, -y - w / 2 - 0.25, -y + w / 2 + 0.25,
                          WinSpec(w, h, sill, lights=1 if w < 1.2 else 2), room="ubf1")
    P += K.balcony("ub", uback, -8.5, -0.5, Z_LOUNGE, depth=1.5, rail="timber", brackets=True)
    # ends (Y -15 "-Y" u = x; Y +9 "+Y" u = -x): GF + F1, clear of the exterior chimney
    for side, plane, tag in (("-Y", U_Y0, "s"), ("+Y", U_Y1, "n")):
        wall = WallRef(side, plane)
        sg = 1.0 if side == "-Y" else -1.0
        for j, x in enumerate((4.0, 12.0)):
            for fl, sill in ((0, Z_ARRIVAL + 0.9), (1, Z_LOUNGE + 0.85)):
                P += K.window_bay(f"ue{tag}{fl}{j}", wall, sg * x - 1.0, sg * x + 1.0,
                                  WinSpec(1.4, 1.5 if fl else 2.0, sill, lights=2),
                                  room=f"ue{tag}{fl}")
    # dormers: front + back at Y -11, +5
    for k, (side, y) in enumerate((("+X", -11.0), ("+X", 5.0), ("-X", -11.0), ("-X", 5.0))):
        P += K.dormer(f"u{k}", uspec, y, w=2.4, h=1.6, room=f"udorm{k % 2}",
                      win=WinSpec(1.3, 1.1, 0.25, lights=2), side=side, setback=1.0,
                      pitch_deg=45.0, wall_mat=SID)
    # chimneys: exterior on the -Y end (shoulder to 3.6; base below t30), ridge chimney
    P += K.chimney("u1", 8.0, U_Y0 - 0.8, 2.4, 1.6, 16.5, base_z=-4.6,
                   shoulder=(3.2, 2.0, Z_LOUNGE), mat=STN)
    P += K.chimney("u2", U_RIDGE_X, 4.0, 1.2, 1.8, 15.5,
                   base_z=K.roof_z(uspec, U_RIDGE_X - 0.6, 4.0) - 0.25, mat=STN)
    for k, (x, y, ztop) in enumerate(((8.0 - 0.5, U_Y0 - 0.8, 16.5), (8.0 + 0.5, U_Y0 - 0.8, 16.5),
                                      (U_RIDGE_X, 4.0 - 0.45, 15.5), (U_RIDGE_X, 4.0 + 0.45, 15.5))):
        P += K._single(f"flue_u{k}", lambda g, x=x, y=y, z=ztop: g.cyl(
            (x, y, z - 0.02), (x, y, z + 0.5), 0.15, 12 if fine else 8, cap0=False), "plaster")   # 3e: 12-seg

    # ---------------- U.eg entry cross gable + porch (to X 15.6) ---------------------------
    # revision 1 (d): tucked under the main eave (EG_* above): 30 deg, eave 3.0, the main gutter
    # continuous over it; its own gutters drain to downspouts on the U wall beside the porch
    eg_y0, eg_y1 = EG_Y0, EG_Y1
    eg_eave, eg_pitch, eg_x0, eg_pier = porch()
    egspec = RoofSpec("gable", eg_x0, EG_X1, eg_y0, eg_y1, eg_eave, eg_pitch, axis="X",
                      overhang=0.4, rake_over=0.3, thick=0.24, mat="lux_slate")
    P += K.gable_roof("ueg", egspec, walls=False, ridge_cap=(0.35, 0.10),
                      gutters=PORCH_UNDER_EAVE)
    for k, y in enumerate((eg_y0 + 0.6, eg_y1 - 0.6)):
        P += SW.pier_box(f"ueg{k}", (14.4, y - 0.6, Z_ARRIVAL), (15.6, y + 0.6, eg_pier), STN)
        P += K.col(f"ueg{k}", 15.0, y, eg_pier + 0.15, eg_eave - 0.2, size=0.40, mat=GLU)
    P += K.truss_kingpost("ueg", WallRef("+X", 15.0, eg_y0 + 0.6, eg_y1 - 0.6), 5.8, eg_eave,
                          eg_pitch, member=0.40, braces="curved", d=0.0, mat=GLU)
    # the porch ceiling keeps its top (revision 0 dropped it: a ray into the open porch gable
    # met the ceiling's underside from behind - an invisible back face in game)
    P += K.box_part("soffit_uegceil", (U_X1, eg_y0 + 0.4, eg_eave + 0.22),
                    (15.0, eg_y1 - 0.4, eg_eave + 0.28), "deck",
                    drop=() if PORCH_UNDER_EAVE else ("+Z",))
    if PORCH_UNDER_EAVE:
        tp = math.tan(math.radians(eg_pitch))
        gz = eg_eave - 0.4 * tp - 0.18                # the kit gutter top under the eave tip
        for k, (y, nrm) in enumerate(((eg_y0 - 0.4 - 0.2 - 0.15, "+Y"), (eg_y1 + 0.4 + 0.2 + 0.15, "-Y"))):
            P += K.downspout(f"ueg{k}", U_X1 + 0.06, y, gz, nrm, wall_gap=0.15,
                             z_bottom=Z_ARRIVAL + 0.05)

    # ---------------- F forecourt: closed slab top 0.15, skirt to t30 - cross-fall ---------
    def f_skirt(x):
        return t_at(x, S_MAX) - CROSS_FALL * 12.5 - 0.25
    P += SW.prism_xz("deck_fc", [(F_X0, f_skirt(F_X0)), (F_X1, f_skirt(F_X1)), (F_X1, Z_ARRIVAL),
                                 (F_X0, Z_ARRIVAL)], F_Y0, F_Y1, "concrete")
    # revision 1 (a): the two post lanterns are vanilla LightpoleParkRandom01 rows (L4_ROWS)
    # guard rails on the forecourt's side edges (the slope falls away under them: 1.4 m at
    # X 14 on the 20 % design fall)
    for k, y in enumerate((F_Y0 + 0.12, F_Y1 - 0.12)):
        P += SW.handrail_at(f"fc{k}", [(F_X0 + 0.4, y, Z_ARRIVAL), (F_X1 - 0.3, y, Z_ARRIVAL)])
    # forecourt sign: stone block + a board facing the road (no words; nitpick C: blank, NC_LABELS)
    P += SW.pier_box("fcsign", (18.5, -13.6, Z_ARRIVAL), (19.1, -11.4, 1.5), STN)
    P += K.sign_board("fc", WallRef("+X", 19.1), -12.5, 0.45, 1.6, 0.85, t=0.06, mat=GLU)

    # ---------------- D glass dining room -------------------------------------------------
    # revision 1 (b): the slab keeps no underside (it was culled anyway: plinth-mode cull_hidden
    # deletes every down-facing face below grade); the cantilever's underside is the protected
    # soffit quad `soffit_cantd` (ground_prefixes)
    P += K.box_part("deck_dslab", (D_X0, D_Y0, Z_DSLAB), (D_X1, D_Y1, Z_DINING), "concrete",
                    drop=("+Z", "+X", "-Z"))
    K.bisect_part(P[-1][0], [((-9.0, 0, 0), (1, 0, 0))])   # the part over the hollow culls
    P += K._single("soffit_cantd", lambda g: g.poly(
        [(D_X0, D_Y0, Z_DSLAB), (-9.0 - W_T / 2, D_Y0, Z_DSLAB), (-9.0 - W_T / 2, D_Y1, Z_DSLAB),
         (D_X0, D_Y1, Z_DSLAB)], (0.0, 0.0, -1.0)), "concrete")
    # glulam joists under the cantilever (the "glass box over air" read from below)
    for k, y in enumerate((-18.5, -14.5, -10.0, -5.5, -1.5)):
        P += SW.member(f"beam_dj{k}", (D_X0 + 0.05, y, Z_DSLAB - 0.2), (-9.0 - W_T / 2, y, Z_DSLAB - 0.2),
                       0.30, 0.40)
    P += K.box_part("wall_dgl", (D_X0, D_Y0, Z_DINING), (D_X1, D_Y1, D_EAVE - 0.3), "glass_dark",
                    drop=("-Z", "+Z"))
    head = [((D_X0 - 0.12, D_Y0 - 0.12, D_EAVE - 0.3), (D_X0, D_Y1 + 0.12, D_EAVE), ("+X",)),
            ((D_X0, D_Y0 - 0.12, D_EAVE - 0.3), (D_X1, D_Y0, D_EAVE), ("+Y",)),
            ((D_X0, D_Y1, D_EAVE - 0.3), (U_X0, D_Y1 + 0.12, D_EAVE), ("-Y",))]
    P += K.boxes_part("beam_dhead", head, GLU)
    zrows = (Z_DINING + 0.1, 0.20, D_EAVE - 0.3)    # transom at +0.20 (clear of the 0 band)
    dw, ds, dn = WallRef("-X", D_X0), WallRef("-Y", D_Y0), WallRef("+Y", D_Y1)
    P += SW.curtain_rows("dw", dw, -(D_Y1 - 0.5), -(D_Y0 + 0.5), zrows, ncol=10, room="dw",
                         per_row_rooms=True)
    P += SW.curtain_rows("ds", ds, D_X0 + 0.5, D_X1 - 0.5, zrows, ncol=8, room="ds")
    P += SW.curtain_rows("dnc", dn, -(-9.5), -(D_X0 + 0.5), zrows, ncol=2, room="dnc")
    # revision 1 (c): the deck-side glass wall (5 bays of 2.1 m, x -9.0..1.5) keeps 4 glazed bays;
    # the bay DD_BAY becomes the terrace door with its transom lights above (`dnf`, the door's
    # head under the curtain's lowest bar); same room tag dnd, no new _Win room
    drows = (Z_DECK + 0.05, 0.20, D_EAVE - 0.3)
    P += SW.curtain_rows("dnd", dn, -DD_BAY[0], 9.0, drows, ncol=1, room="dnd")
    P += SW.curtain_rows("dne", dn, -(D_X1 - 0.5), -DD_BAY[1], drows, ncol=3, room="dnd")
    dd_head = Z_DECK + SW.DOOR_H + SW.DOOR_FRAME     # -0.15: the rough opening's top
    for r, (za, zb) in enumerate(((dd_head, 0.20), (0.20, D_EAVE - 0.3))):
        P += K.pane_quad(f"win_dnd_dnfr{r}c0", dn, -DD_BAY[1], -DD_BAY[0], za, zb)
    face = K.PANE_OFFSET + 0.10
    P += K.wall_boxes("ctran_dnf", dn, [(-DD_BAY[1] + 0.04, -DD_BAY[0] - 0.04, zt - 0.06, zt + 0.06,
                                         0.0, face - 0.01) for zt in (dd_head, 0.20, D_EAVE - 0.3)],
                      "trim")
    ps, dd_door = SW.terrace_door("dd", "+Y", D_Y1, DD_X, Z_DECK, "dnd", frame_sides=False,
                                  frame_mat="trim", leaf_mat="trim")
    P += ps
    dd_door["host"] = "wall_dgl"
    host = [o for o, _m in P if o.name == "wall_dgl"]
    SW.cut_recess(host[0], "+Y", D_Y1, *dd_door["rough_a"], *dd_door["rough_z"], SW.DOOR_SET)
    posts = []                                        # glulam posts ~2 m, on the mullion lines
    for k in range(11):
        y = -(0.5 + 1.9 * k)
        posts.append(((D_X0 - 0.30, y - 0.15, Z_DINING), (D_X0, y + 0.15, D_EAVE - 0.3)))
    for k in range(9):
        x = D_X0 + 0.5 + 1.875 * k
        posts.append(((x - 0.15, D_Y0 - 0.30, Z_DINING), (x + 0.15, D_Y0, D_EAVE - 0.3)))
    for x in [-13.5 + 2.0 * k for k in range(3)] + [-9.0 + 2.1 * k for k in range(6)]:
        posts.append(((x - 0.15, D_Y1, Z_DINING), (x + 0.15, D_Y1 + 0.30, D_EAVE - 0.3)))
    P += K.boxes_part("col_dposts", posts, GLU)
    dspec = RoofSpec("gable", D_X0, D_X1, D_Y0, D_Y1, D_EAVE, D_PITCH, axis="X", overhang=0.6,
                     rake_over=1.5, thick=0.20, mat="metal_roof")
    # no gutters: they were the -Y extreme and LOD1 deletes gutter_ (C15 drift 0.137 m)
    P += K.gable_roof("d", dspec, walls=False, ridge_cap=(0.35, 0.10), gutters=False,
                      snow_guard=1, tails=1.2)
    d_ridge = D_EAVE + math.tan(math.radians(D_PITCH)) * 0.5 * (D_Y1 - D_Y0)
    P += K.prism_gable("dfront", "X", D_X0, D_X0 + 0.15, D_Y0, D_Y1, D_EAVE, d_ridge - 0.02,
                       mat="glass_dark")
    # gable glazing above 1.5: 6 stepped panes (contract 4.3 "6 trapezoids")
    gw = WallRef("-X", D_X0)
    for j in range(6):
        ya = D_Y0 + 1.0 + 3.0 * j
        yb = ya + 3.0
        top = d_ridge - math.tan(math.radians(D_PITCH)) * max(abs(ya + 10.0), abs(yb + 10.0)) - 0.45
        if top - (D_EAVE + 0.1) > 0.3:
            P += K.pane_quad(f"win_dgab_dg{j}", gw, -yb, -ya, D_EAVE + 0.1, top)
    P += K.truss_kingpost("d", WallRef("-X", D_X0, -D_Y1, -D_Y0), 19.4, D_EAVE, D_PITCH,
                          member=0.40, braces="curved", d=0.75, mat=GLU)
    # cantilever: fascia (slot 3) round the 5 m overhang + 4 glulam struts to the plinth face
    fas = [((D_X0 - 0.10, D_Y0 - 0.10, Z_DSLAB - 0.2), (D_X0, D_Y1 + 0.10, Z_DINING + 0.02), ()),
           ((D_X0, D_Y0 - 0.10, Z_DSLAB - 0.2), (-9.0, D_Y0, Z_DINING + 0.02), ()),
           ((D_X0, D_Y1, Z_DSLAB - 0.2), (-9.0, D_Y1 + 0.10, Z_DINING + 0.02), ())]
    P += K.boxes_part("fascia_dcant", fas, "metal_roof")
    for k, y in enumerate((-18.0, -13.0, -7.0, -2.0)):
        P += SW.member(f"strut_d{k}", (-13.4, y, Z_DSLAB - 0.1), (-9.0 - W_T / 2, y, -6.5), 0.35, 0.35,
                       up=(0, 1, 0))

    # ---------------- T terrace deck + kerb + balustrade + deck lights ---------------------
    # revision 1 (b): the deck slab runs from the cantilever tip T_X0 over the plinth to T_X1 (its
    # underside: the protected soffit over the cantilever only - the hollow never shows it)
    zsl = Z_DECK - 0.16                               # the slab bottom = the plinth walls' top
    P += K.box_part("deck_t", (T_X0, T_Y0, zsl), (T_X1, T_Y1, Z_DECK), "deck", drop=("-Z",))
    P[-1][0]["tile_rot"] = 0                          # boards along X (kit deck board_dir "X")
    # the concrete kerb round the deck edge (the glass balustrade stands on it, 0.10 above the
    # boards): on the cantilever 0.10 past the edge, on W2 flush with the wall's outer face
    P += K.boxes_part("plinthcap_t", [
        ((T_X0 - 0.10, T_Y0 - 0.10, zsl), (T_X0 + 0.25, T_Y1 + 0.10, Z_DECK + 0.10), ("-Z",)),
        ((T_X0 + 0.25, T_Y1 - 0.25, zsl), (CANT_FACE, T_Y1 + 0.10, Z_DECK + 0.10), ("-Z",)),
        ((CANT_FACE, T_Y1 - 0.25, zsl), (T_X1 + 0.30, T_Y1 + W_T / 2, Z_DECK + 0.10), ("-Z",)),
        ((T_X0 + 0.25, T_Y0 - 0.10, zsl), (D_X0, T_Y0 + 0.25, Z_DECK + 0.10), ("-Z",))],
        "concrete")
    zb = Z_DECK + 0.10
    P += SW.glass_balustrade("t", [(D_X0, T_Y0, zb), (T_X0, T_Y0, zb), (T_X0, T_Y1, zb),
                                   (T_X1, T_Y1, zb)])
    pucks = []
    for k in range(12):                              # deck LEDs along the balustrade kick
        if k < 7:
            x, y = T_X0 + 0.55, T_Y0 + 1.6 + 3.3 * k
        else:
            x, y = T_X0 + 2.5 + 3.6 * (k - 7), T_Y1 - 0.55
        pucks.append(((x - 0.125, y - 0.125, Z_DECK), (x + 0.125, y + 0.125, Z_DECK + 0.08)))
    P += K.boxes_part("lamp_deck", pucks, "lamp")
    # the cantilever's structure, the dining room's family: glulam joists over the W1 buttress
    # piers, a metal fascia under the kerb over the joist ends, glulam struts from near the tip
    # down onto the piers, the protected soffit between the fascias
    jw, jh = T_JOIST
    for k, y in enumerate(CANT_LINES):
        P += SW.member(f"beam_tj{k}", (T_X0 + 0.02, y, zsl - jh / 2), (CANT_FACE, y, zsl - jh / 2), jw, jh)
        P += SW.member(f"strut_t{k}", (T_X0 + 0.6, y, zsl - jh - 0.05),
                       (CANT_FACE - 0.45, y, T_STRUT_FOOT_Z), 0.35, 0.35, up=(0, 1, 0))
    fz0 = zsl - jh - 0.02
    P += K.boxes_part("fascia_tcant", [
        ((T_X0 - 0.10, T_Y0 - 0.10, fz0), (T_X0 + 0.02, T_Y1 + 0.10, zsl), ()),
        ((T_X0 + 0.02, T_Y0 - 0.10, fz0), (CANT_FACE, T_Y0 + 0.02, zsl), ()),
        ((T_X0 + 0.02, T_Y1 - 0.02, fz0), (CANT_FACE, T_Y1 + 0.10, zsl), ())], "metal_roof")
    P += K._single("soffit_cantt", lambda g: g.poly(
        [(T_X0 + 0.02, T_Y0 + 0.02, zsl), (CANT_FACE, T_Y0 + 0.02, zsl),
         (CANT_FACE, T_Y1 - 0.02, zsl), (T_X0 + 0.02, T_Y1 - 0.02, zsl)], (0.0, 0.0, -1.0)), "deck")

    # ---------------- S route: forecourt (0.15) -> walkway -> 16 risers -> terrace (-2.55) --
    P += K.deck("su", STAIR_TOP_X, F_X0, ROUTE_Y0, ROUTE_Y1, Z_ARRIVAL, support="none", fascia=0.0,
                board_dir="X", thick=0.20)
    P += K.deck("sl", T_X1, STAIR_FOOT_X, ROUTE_Y0, ROUTE_Y1, Z_DECK, support="none", fascia=0.0,
                board_dir="X", thick=0.20)
    sy = 0.5 * (ROUTE_Y0 + ROUTE_Y1)
    foot, top = (STAIR_FOOT_X, sy, Z_DECK), (STAIR_TOP_X, sy, Z_ARRIVAL)
    P += K.stair("s", foot, top, w=1.4, rise_max=0.17, ground=False, rail="none",
                 tread_mat="deck", stringer_mat="trim")
    sk_y0, sk_y1 = ROUTE_Y1, ROUTE_Y1 + 0.4

    def r_skirt(x):
        return t_at(x, S_MAX) - 0.5
    P += SW.prism_xz("plinth_sroute", [(T_X1, r_skirt(T_X1)), (F_X0, r_skirt(F_X0)),
                                       (F_X0, Z_ARRIVAL), (STAIR_TOP_X, Z_ARRIVAL),
                                       (STAIR_FOOT_X, Z_DECK), (T_X1, Z_DECK)],
                     sk_y0, sk_y1, STN)
    ry = ROUTE_Y1 - 0.12
    P += SW.handrail_at("sroute", [(T_X1 + 0.1, ry, Z_DECK), (STAIR_FOOT_X, ry, Z_DECK),
                                   (STAIR_TOP_X, ry, Z_ARRIVAL), (F_X0, ry, Z_ARRIVAL)])

    # ---------------- PL plinth walls (stone above t + 0.3, rock below) + R ---------------
    top_deck = Z_DECK - 0.16                          # under the deck boards
    P += _plinth_wall(SW, "w1", (T_WALL, T_Y0), (T_WALL, T_Y1), top_deck, "-X")
    P += _plinth_wall(SW, "w2", (T_WALL, T_Y1), (T_X1, T_Y1), top_deck, "+Y")
    P += _plinth_wall(SW, "w5", (T_X1, T_Y1), (T_X1, ROUTE_Y0), top_deck, "+X")
    P += _plinth_wall(SW, "w6", (D_X1 + W_T / 2, U_Y0), (D_X1 + W_T / 2, D_Y0), D_EAVE, "+X")
    P += _plinth_wall(SW, "w4", (D_X1, D_Y0), (-9.0, D_Y0), Z_DSLAB, "-Y")
    P += _plinth_wall(SW, "w3", (-9.0, D_Y0), (-9.0, D_Y1), Z_DSLAB, "-X")
    P += _plinth_wall(SW, "w7b", (-9.0, T_Y0), (D_X0, T_Y0), Z_DSLAB, "-Y")
    # buttress piers on the exposed plinth faces (contract 2: "Courchevel's piers"), stone
    # down to the plinth bottom, back face dropped (it sits on the wall). Revision 1: the W1
    # piers stand under the cantilever up to the joists (the struts land on them)
    bt = []
    for y in W1_PIERS:
        bt.append(((CANT_FACE - 0.45, y - 0.5, Z_PLINTH), (CANT_FACE, y + 0.5, zsl - T_JOIST[1]),
                   ("+X",)))
    for x in (-11.0, -5.0, 1.0):
        bt.append(((x - 0.5, T_Y1 + W_T / 2, Z_PLINTH), (x + 0.5, T_Y1 + W_T / 2 + 0.45, Z_DECK + 0.10),
                   ("-Y",)))
    for y in (-20.0, -14.0, -7.0):
        bt.append(((-9.0 - W_T / 2 - 0.45, y - 0.45, Z_PLINTH), (-9.0 - W_T / 2, y + 0.45, Z_DSLAB),
                   ("+X",)))
    P += K.boxes_part("pier_plinth", bt, STN)
    # cellar windows on W1 (L-2), centred between the buttress piers (Y 5.75 / 11.5 / 17.25 /
    # 23): at Y 6 / 12 / 18 the piers covered 70 / 50 / 30 % of them (buildings-review-
    # geometry F2; check_pane_clearance below)
    for j, y in enumerate((8.625, 14.375, 20.125)):
        P += K.window_bay(f"cel{j}", WallRef("-X", CANT_FACE), -y - 0.9, -y + 0.9,
                          WinSpec(1.2, 1.2, -5.5, lights=1), room="cellar")

    # ---------------- UV-island hygiene (recipes/l5_swanky_bottom.unbevelled) ---------------
    SW.unbevelled(P, "truss_ueg", "trim_trussueg")
    SW.unbevelled(P, "truss_d", "trim_trussd")
    SW.unbevelled(P, "col_dposts", "trim_dposts")
    SW.strip_tiny_faces(P, ("snowguard_uA0", "snowguard_uB0", "snowguard_dA0", "snowguard_dB0"))
    rooms = SW.retag_rooms(P, lambda t: t)           # C13: <= 23 rooms, one id each

    # ---------------- contracts on the parts (section 9, skeptic-corrected) ----------------
    lo, hi = SW.parts_bounds(P)
    SW.check(max(-lo[0], hi[0]) <= HX and max(-lo[1], hi[1]) <= HY,
             f"C1 lot fit: x {lo[0]:.2f}..{hi[0]:.2f}, y {lo[1]:.2f}..{hi[1]:.2f} vs +-{HX}/{HY}")
    SW.check(abs(lo[2] - Z_PLINTH) <= 0.01, f"C2 min z {lo[2]:.3f} != {Z_PLINTH}")
    flo, fhi = SW.parts_bounds(P, ("deck_fc",))
    SW.check(abs(fhi[2] - Z_ARRIVAL) < 1e-6, f"C2 forecourt top {fhi[2]:.3f} != {Z_ARRIVAL}")
    for o, _m in P:                                   # C3: plinth mode, nothing in the band
        W = SW.world_coords(o)
        SW.check(not (abs(W[:, 2]) <= 0.01).any(), f"C3 {o.name} has vertices in z 0 +- 0.01")
    for pre, cond, txt in ((("deck_fc", "roof_ueg", "pier_ueg"), lambda c: c > 13.0, "> 13"),
                           (("deck_t", "plinthcap_t", "glassrail_t"), lambda c: c < -5.0, "< -5"),
                           (("strut_d", "fascia_dcant"), lambda c: c < -9.0, "< -9"),
                           (("strut_t", "fascia_tcant", "soffit_cantt"), lambda c: c < T_WALL,
                            f"< {T_WALL}")):
        cx = SW.area_centroid_x(P, pre)
        SW.check(cond(cx), f"C4 facing: {pre} centroid x {cx:.2f} not {txt}")
    # C5 slope fit at every supported fall (skeptic MUST-CHANGE 2)
    for s in (S_MIN, S_DESIGN, S_MAX):
        SW.check(Z_DINING >= t_at(D_X1, s) + 0.3 - 1e-9,
                 f"C5 D floor {Z_DINING} < t({D_X1}) + 0.3 at {s:.0%}")
        SW.check(Z_DECK >= t_at(T_X1, s) + 0.3 - 1e-9,
                 f"C5 terrace deck {Z_DECK} < t({T_X1}) + 0.3 at {s:.0%}")
        SW.check(Z_DECK >= t_at(STAIR_FOOT_X, s) + 0.1,
                 f"C5 stair foot walkway below terrain at {s:.0%}")
    for tag in ("w1", "w2", "w3", "w4", "w5", "w6", "w7b"):   # bottom below t30
        wlo, whi = SW.parts_bounds(P, (f"rock_{tag}",))
        SW.check(wlo[2] <= t_at(wlo[0], S_MAX) + 1e-6,
                 f"C5 plinth {tag} bottom {wlo[2]:.2f} above t30 {t_at(wlo[0], S_MAX):.2f}")
    SW.check(U_BOTTOM <= t_at(U_X0, S_MAX), f"C5 U base {U_BOTTOM} above t30 at X {U_X0}")
    clo, _chi = SW.parts_bounds(P, ("chim_u1sh",))
    SW.check(clo[2] <= t_at(8.0 - 1.6, S_MAX), "C5 exterior chimney base above t30")
    for x in (F_X0, 0.5 * (F_X0 + F_X1), F_X1):
        SW.check(f_skirt(x) <= t_at(x, S_MAX) - CROSS_FALL * 12.0,
                 f"C5 forecourt skirt at X {x} above t30 - cross-fall")
    # C6 heights
    u_ridge_c = K.roof_z(uspec, U_RIDGE_X, 0.0) - 0.15 * math.sqrt(2)
    SW.check(abs(u_ridge_c - 13.0) < 1e-6, f"C6 U ridge {u_ridge_c:.3f} != 13.0")
    soffit = U_EAVE - 1.2 - 0.15 * math.sqrt(2)       # U soffit bottom at the eave tip X 0.8
    dlo, dhi = SW.parts_bounds(P, ("roof_dA", "roof_dB", "ridge_d"))
    SW.check(dhi[2] <= soffit - 0.05, f"C6 D roof top {dhi[2]:.2f} > U soffit {soffit:.2f} - 0.05")
    SW.check(abs((-9.0) - D_X0 - 5.0) < 0.05, "C6 cantilever != 5.00 m beyond W3")
    for cap in ("chimcap_u1", "chimcap_u2"):
        _lo, _hi = SW.parts_bounds(P, (cap,))
        SW.check(_hi[2] >= K.roof_z(uspec, U_RIDGE_X, 0.0) + 2.0, f"C6 {cap} below ridge + 2")
    # plinth outline closed + every segment built (skeptic MUST-CHANGE 3)
    for (n0, a0, b0), (n1, a1, _b1) in zip(OUTLINE, OUTLINE[1:] + OUTLINE[:1]):
        SW.check(math.dist(b0, a1) < 1e-6, f"plinth outline open between {n0} and {n1}")
    seg_part = {"W1": ("plinth_w1", "rock_w1"), "W2": ("plinth_w2", "rock_w2"),
                "W5": ("plinth_w5", "rock_w5"), "Uw": ("wall_ustone",),
                "W6": ("plinth_w6", "rock_w6"), "W4": ("plinth_w4", "rock_w4"),
                "W3": ("plinth_w3", "rock_w3"), "W7": ("plinth_w7", "rock_w7")}
    for nm, a, b in OUTLINE:
        plo, phi = SW.parts_bounds(P, seg_part[nm])
        for (x, y) in (a, b):
            SW.check(plo[0] - W_T <= x <= phi[0] + W_T and plo[1] - W_T <= y <= phi[1] + W_T,
                     f"plinth outline {nm}: ({x}, {y}) not covered by {seg_part[nm]}")
        x_low = min(a[0], b[0])
        SW.check(plo[2] <= t_at(x_low, S_MAX) + 1e-6,
                 f"plinth outline {nm} bottom {plo[2]:.2f} above t30 {t_at(x_low, S_MAX):.2f}")
    # stair ends on walkable surfaces
    for pt, deck in ((foot, "deck_sl"), (top, "deck_su")):
        dlo_, dhi_ = SW.parts_bounds(P, (deck,))
        SW.check(dlo_[0] - 1e-6 <= pt[0] <= dhi_[0] + 1e-6 and dlo_[1] <= pt[1] <= dhi_[1]
                 and abs(dhi_[2] - pt[2]) < 1e-6, f"stair end {pt} not on {deck}")
    sulo, suhi = SW.parts_bounds(P, ("deck_su",))
    SW.check(abs(suhi[0] - F_X0) < 1e-6 and flo[1] <= ROUTE_Y0 and fhi[1] >= ROUTE_Y1,
             "upper walkway does not meet the forecourt")
    sllo, slhi = SW.parts_bounds(P, ("deck_sl",))
    SW.check(abs(sllo[0] - T_X1) < 1e-6, "lower walkway does not meet the terrace")
    npanes = SW.check_panes(P, 4.6, l4_storey_lines, "L4")
    SW.check_pane_clearance(P, "L4")                  # no pier / roof in front of a pane (F2)
    # ---------------- revision 1 contracts (eighteenth look) ------------------------------------
    cant = check_cantilever(P)                        # (b) R1b
    door = SW.check_door(P, dd_door, "wall_dgl")      # (c) D1-D3
    SW.check_clear_boxes(P, entrance_boxes(dd_door))  # (d) E1 (+ D5 for the dining door)
    zones, _boxes = SW.check_sub_objects(P, rows=L4_ROWS, lot=(-HX, HX, -HY, HY))   # (a) S1-S4
    left = sorted(o.name for o, _m in P if o.name.startswith(
        ("umbstand_", "umbpole_", "umbcanopy_", "plant_t", "col_t", "lamp_t", "col_fc", "lamp_fc",
         "lamp_u0", "lamp_u1")))
    SW.check(not left or not PORCH_UNDER_EAVE, f"R1a modelled dressing left (vanilla rule): {left[:8]}")
    ph = {what: (sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o, _m in P
                     if o.name.startswith(pre)), sum(1 for o, _m in P if o.name.startswith(pre)))
          for pre, what, _why in PLACEHOLDERS}
    print(f"[L4] contracts OK: bounds x {lo[0]:.2f}..{hi[0]:.2f} y {lo[1]:.2f}..{hi[1]:.2f} "
          f"z {lo[2]:.2f}..{hi[2]:.2f}; {npanes} panes in {len(rooms)} rooms; {len(P)} parts; "
          f"pane clearance {SW.PANE_CLEAR_M} m OK; r1: cantilever {cant}; dining door {door}; "
          f"entrance clear of gutters / downspouts; {len(L4_ROWS)} vanilla sub-objects {zones}; "
          f"placeholders {ph}")
    return P


def check_cantilever(parts):
    """Revision 1 (b) contract R1b "the deck extends out over the edge via supports similar to
    the indoor part": the deck top is one level from the cantilever tip to the stair walkway
    (T_X0..T_X1 at Z_DECK) and runs >= CANT_MIN past the plinth face; every joist line carries a
    glulam joist and a strut whose foot lands on a W1 buttress pier's outer face (within the
    pier's width, between its bottom and top); the protected soffit spans the cantilever between
    the fascias; the kerb and the glass balustrade follow the new edge (the tip and both
    cantilevered sides). Raises; returns a dict for the report."""
    SW = _sw()
    dlo, dhi = SW.parts_bounds(parts, ("deck_t",))
    SW.check(abs(dlo[0] - T_X0) < 1e-6 and abs(dhi[0] - T_X1) < 1e-6 and abs(dhi[2] - Z_DECK) < 1e-6,
             f"R1b deck x {dlo[0]:.2f}..{dhi[0]:.2f} top {dhi[2]:.3f}")
    SW.check(CANT_FACE - T_X0 >= CANT_MIN, f"R1b cantilever {CANT_FACE - T_X0:.2f} m < {CANT_MIN}")
    zsl = Z_DECK - 0.16
    feet = []
    for k, y in enumerate(CANT_LINES):
        jlo, jhi = SW.parts_bounds(parts, (f"beam_tj{k}",))
        SW.check(jlo[0] <= T_X0 + 0.05 and jhi[0] >= CANT_FACE - 1e-4 and jhi[2] <= zsl + 1e-4,
                 f"R1b joist {k} x {jlo[0]:.2f}..{jhi[0]:.2f} z ..{jhi[2]:.2f}")
        strut = [o for o, _m in parts if o.name == f"strut_t{k}"]
        SW.check(len(strut) == 1, f"R1b strut {k} missing")
        W = SW.world_coords(strut[0])
        foot = W[W[:, 0] > CANT_FACE - 0.75]            # the strut's lower end (at the pier)
        pier = min(W1_PIERS, key=lambda p: abs(p - y))
        pz0, pz1 = Z_PLINTH, zsl - T_JOIST[1]           # the W1 pier's z range
        SW.check(len(foot) and float(foot[:, 0].max()) <= CANT_FACE
                 and float(foot[:, 0].max()) >= CANT_FACE - 0.45 - 1e-6
                 and float(foot[:, 1].min()) >= pier - 0.5 - 1e-6
                 and float(foot[:, 1].max()) <= pier + 0.5 + 1e-6
                 and float(foot[:, 2].min()) >= pz0 and float(foot[:, 2].max()) <= pz1,
                 f"R1b strut {k} (y {y}) does not land on the W1 pier at y {pier}: foot x "
                 f"{foot[:, 0].min():.2f}..{foot[:, 0].max():.2f} y {foot[:, 1].min():.2f}.."
                 f"{foot[:, 1].max():.2f} z {foot[:, 2].min():.2f}..{foot[:, 2].max():.2f}")
        top = W[W[:, 0] < T_X0 + 1.2]                   # its upper end under the joist
        SW.check(len(top) and float(top[:, 2].max()) >= zsl - T_JOIST[1] - 1e-6,
                 f"R1b strut {k} does not reach its joist")
        feet.append(round(float(foot[:, 2].min()), 2))
    solo, sohi = SW.parts_bounds(parts, ("soffit_cantt",))
    SW.check(solo[0] <= T_X0 + 0.03 and sohi[0] >= CANT_FACE - 1e-6 and solo[1] <= T_Y0 + 0.03
             and sohi[1] >= T_Y1 - 0.03, f"R1b soffit {solo} {sohi}")
    glo, ghi = SW.parts_bounds(parts, ("glassrail_t",))
    SW.check(glo[0] <= T_X0 + 0.05 and glo[1] <= T_Y0 + 0.05 and ghi[1] >= T_Y1 - 0.05,
             f"R1b balustrade x {glo[0]:.2f}.., y {glo[1]:.2f}..{ghi[1]:.2f}")
    return dict(tip=T_X0, past_plinth=round(CANT_FACE - T_X0, 2), joists=len(CANT_LINES),
                strut_feet_z=feet)


NOTES_R1 = ("Revision 1 (vanilla props; the terrace deck cantilevered 5.3 m past the plinth on "
            "glulam joists and struts like the dining room; a French door from the dining room onto "
            "the deck; the entry porch tucked under a continuous main gutter): summit glass-and-stone "
            "restaurant on a stepped plinth: arrival block with entry truss porch, glass dining room "
            "cantilevered 5 m over the slope, panoramic terrace with glass balustrade, re-routed "
            "stair, rock band below the design terrain.")
FINE_NOTES = ("Fine-detail step 3e (12-seg flue pots; no rack, the lanterns vanilla since revision 1, no "
              "lettered board: no sign set) on " + NOTES_R1 + " Nitpick C (2026-10-13): the forecourt board is "
              "blank (cream, graphite rim; manifest we_boards).")
# the approved forecourt board (revision 1 / step 3e): the fork-and-knife painted over the whole 1.6 x 0.85 face
# (white on the light label ground; paint_pictogram has no square mode). `--no-fine` keeps it (legacy).
PICTO_LABELS = (("sign_fc_face", "paint_pictogram", {"kind": "restaurant"}),)
# nitpick C (2026-10-13; Richard, twenty-fifth look, L4: "Same issue with the sign outside as the chalet", i.e. "not
# loving the SIGN of the FORK AND KNIFE ... leave it blank or keep it simple, and people can paste their own labels
# on it?"; build/nitpick-c-lodges.md): a BLANK board - a cream panel (the resort's sign cream, cs2_signset.GATE_CREAM)
# with a thin rim in the lodge's graphite trim (slot 1 (0.12, 0.12, 0.13) as shown on the 0.80 trim paint:
# lin_to_srgb(srgb_to_lin(0.80) x slot) = 0.298 / 0.310); no pictogram. lodge.py lists the face in the manifest's `we_boards`
# block for an optional Write Everywhere layout. Rim 0.03 m at the label density (~135 px/m: label w 3.0 x ~45).
L4_TRIM_SRGB = (0.298, 0.298, 0.310)
SIGN_CREAM = (0.93, 0.89, 0.78)
NC_LABELS = (("sign_fc_face", "display",
              {"draws": (blank_board(bg=SIGN_CREAM, rim=L4_TRIM_SRGB, rim_m=0.03, inset_m=0.05),)}),)

RECIPE = FineRecipe(
    key="L4", aliases=("lux-top",), letter="B",
    # prefab = the code catalog's name (SkiCatalog.Names.LodgeSummit, LocaleEN, hub; R25 /
    # bldg-kit 11). The mesh keeps its built name (no consumer reads it). buildings-integration 1
    asset="SkiLodgeSwankyTopCommercialLow", prefab="SkiLodgeSummit", category="lodges",
    lot=(LOT_W, LOT_D), base_mode="plinth", plinth=(-Z_PLINTH, Z_LOUNGE), yaw_deg=0.0,
    tex=2048, allow_4096=True,       # R18 / R23: 4096 only if the shipped w=1 median < 25 px/m
    budget=Budget(16000, 18000, 7000, 480, 600),
    massing="custom", builder=build_l4,
    rooms="auto", interior="CommercialLow",
    # VariationSet A (contract 6): graphite / red / zinc
    slots={1: (0.12, 0.12, 0.13), 2: (0.55, 0.08, 0.06), 3: (0.30, 0.31, 0.32)},
    # revision 1 (b): plinth mode has no ground parts; this is the cull's / LOD chain's protect
    # list: the two cantilever soffits face down below grade, which plinth-mode cull_hidden
    # always deletes (it assumes terrain there), and the deck would then be see-through from the
    # slope below (the dining room's shipped revision-0 underside was); 2 tris each at every LOD
    ground_prefixes=("soffit_cant",),
    arrival_prefixes=ARRIVAL_PREFIXES,
    lod1_extra=(),                    # kit LOD1 list already drops umbpole_ / lamp_ (deck LEDs)
    lod2_extra=("strut_",),           # contract: struts carry the LOD1 cantilever read
    lod2_keep=(),
    labels=NC_LABELS,                 # nitpick C (2026-10-13): a blank board (was PICTO_LABELS, kept in legacy)
    cull_ok=(),
    views=(("road34left", (-0.7, -1.0, 0.45)), ("slope34right", (0.8, 1.0, 0.35)),
           ("aerial", (0.4, -0.6, 1.2)),
           # revision 1: the entrance from the forecourt (the porch under the continuous main
           # gutter); the cantilever and the dining door are below grade: NOGROUND_VIEWS
           ("entry_gutter", (0.15, -1.0, 0.20))),
    # revision 1 (a): every umbrella, table, lantern, lamp and planter is a vanilla prop by reference
    sub_objects=_sub_objects(),
    notes=FINE_NOTES,
    # fine-detail pass step 3e (opt-in; lodge.py --no-fine restores `legacy` = the approved revision 1)
    fine_detail=True,
    sign_set=None,                    # the forecourt board is a pictogram (no lettering): stays in the atlas
    legacy=(("notes", NOTES_R1), ("labels", PICTO_LABELS)),
)


# ======================================================================================
# No-ground previews (CLI). lodge.py renders with cs2_preview's flat z-0 ground plane, which
# hides everything L4 has below grade (terrace -2.55, plinth to -12, the cantilever):
#   blender --background --factory-startup --python recipes/l4_swanky_top.py -- --noground [OUT]
# re-imports the exported LOD0 and renders pbr + dusk without the ground (kit need: a
# ground=False / slope-ramp switch for plinth recipes in lodge.py). Revision 1 adds the
# deck_cantilever / deck_door frames here and true close-ups:
#   blender --background --factory-startup --python recipes/l4_swanky_top.py -- --closeups [OUT]
# ======================================================================================

NOGROUND_VIEWS = (("noground_slope34", (0.8, 1.0, 0.30)), ("noground_slope34left", (-0.8, 1.0, 0.30)),
                  ("noground_side", (-1.0, 0.15, 0.15)), ("noground_under", (-0.35, 1.0, -0.18)),
                  # revision 1: the cantilever from the slope below, the deck door from the deck
                  ("deck_cantilever", (0.55, 1.0, -0.05)), ("deck_door", (-0.2, 1.0, 0.55)))

# (shot, target, eye) in the authoring frame (+X = road, z up; no shift on L4)
CLOSEUP_SHOTS = (
    ("deck_cantilever", (-17.0, 11.5, -3.6), (-30.0, -6.0, -8.0)),
    ("deck_cantilever_under", (-16.5, 9.0, -3.2), (-24.0, 22.0, -9.5)),
    ("deck_door", (DD_X, D_Y1, Z_DECK + 1.3), (DD_X - 3.0, 8.5, Z_DECK + 2.6)),
    ("entry_gutter", (U_X1 + 0.8, -2.5, 3.6), (U_X1 + 13.0, -4.5, 3.4)),
    ("entry_gutter_34", (U_X1 + 0.8, -2.5, 3.6), (U_X1 + 9.0, -11.5, 4.5)),
)


def noground_previews(out_dir=None):
    import bpy
    import cs2_preview
    import lodge
    recipe = RECIPE
    name = lodge.asset_name(recipe, "B")
    asset_dir = os.path.join(lodge.IMPORT_ROOT, recipe.category, recipe.letter, name)
    out_dir = out_dir or os.path.join(lodge.PREVIEW_ROOT, recipe.category)
    lodge.clear_scene()
    obj = cs2_preview.import_generation(os.path.join(asset_dir, f"{name}.fbx"))
    obj.name = name
    # import_generation's Z-up test trips on the 12 m below-grade plinth and leaves the mesh
    # in the game frame (y up): rotate +90 deg about X, (x, y, z) -> (x, -z, y)
    d = [max(v.co[i] for v in obj.data.vertices) - min(v.co[i] for v in obj.data.vertices)
         for i in range(3)]
    if d[1] < d[2] and min(v.co[1] for v in obj.data.vertices) < -5.0:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        obj.rotation_euler = (math.radians(90.0), 0.0, 0.0)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    tint = {"RGB"[k - 1]: tuple(v) for k, v in recipe.slots.items()}
    return cs2_preview.preview(obj, asset_dir, name, out_dir, modes=("pbr", "dusk"),
                               views=list(NOGROUND_VIEWS), tint=tint, ground=False)


if __name__ == "__main__":
    _argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--noground" in _argv:
        _i = _argv.index("--noground")
        _out = _argv[_i + 1] if len(_argv) > _i + 1 and not _argv[_i + 1].startswith("--") else None
        for _f in noground_previews(_out):
            print("[noground]", _f)
    if "--closeups" in _argv:
        _i = _argv.index("--closeups")
        _out = _argv[_i + 1] if len(_argv) > _i + 1 and not _argv[_i + 1].startswith("--") else None
        for _f in _sw().closeup_previews(RECIPE, CLOSEUP_SHOTS, _out, ground=False):
            print("[closeups]", _f)
