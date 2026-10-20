"""
recipes/lodge_aframe_fine.py - the FINE-DETAIL L1 A-frame day lodge (fine-detail pass step 3a, 2026-10-12;
design/fine-detail-pass.md A.2 / B / D as cut by -skeptic.md FS4 / FS6 / FS7, Richard's seventeenth look
H1-H6 defaults; build/fine-detail-step3a-buildings.md; the school is the reference host,
build/fine-detail-step2-engine.md section 12).

WHY THIS MODULE. L1 is a plain bldg_recipes.Recipe defined IN bldg_recipes.py (its sha1 guards L1's K4
row), and fine_detail.FineRecipe cannot be imported there (fine_detail imports bldg_recipes: a circular
import). So the base L1 stays where it is, data untouched, and this module WRAPS it
(fine_detail.wrap: every Recipe field copied, the fine fields set) and hands it to the registry through
OVERRIDES: bldg_recipes.load_recipes() swaps it in for the base L1 (the one bldg_recipes.py edit, a generic
override hook). `lodge.py --lodge L1 --no-fine` rebuilds the approved pre-pass L1 from `legacy`
(byte-identical, proven in the build report); bldg_recipes.L1 itself is that pre-pass recipe.

WHAT CHANGES (fine=True)
    D   kit rows      the 6 modeled A-frame racks (ski_rack slots 8 along Y) -> SkiDressSkiRack / V2 / V3
                      rows alternating the three loadouts (FS6) at the old rack centers on the lot
                      (m_ParentMesh -1), IDENTITY: the kit frame placed by the row = the removed frame
                      vertex for vertex (the A-frame is symmetric under the 180-degree yaw, so the kit's
                      +Z front faces the old side -1: both sides carry gear)
                      the 8 LEANING SINGLES (skirack_leang*) STAY in the host (FS6: no kit prop for them)
    A.2 vanilla       the 2 porch lanterns lamp_porchS / N (on the porch columns' entry faces) ->
                      WallLightShortRangeRandom01 rows (H4) at the old lens (y, z) on the column face
                      (lodge.aframe_parts skips them through PORCH_LAMPS_VANILLA in the params)
    B.1 kit geometry  the deck flag mast 12-seg + ball finial (flag_mast fine=True)
    kept modeled     the 4 rail-F post lanterns lamp_railF0-3 (no vanilla post lantern: GardenLight is a
                      0.62 m bollard, a wall light needs a wall face), the 4 festoon strings (no vanilla
                      string light), the menu board (no sheet slot: a 0.75 x 0.95 m kind needs 225 x 285 px;
                      the only free run that tall leaves the sheet < 200 free rows, the SS1 append reserve;
                      kept in the host atlas at 110 px/m, as built), the two resort-glyph boards (C.1)
    no sign set       (above); the porch bench + bin and the 10 parasols / table sets are vanilla already
Row frame: L1's existing convention, game (gx, gy, gz) = (-y, z, -x) of the PRE-yaw authoring point
(yaw 180), i.e. (y, z, x) of the post-yaw point: gx NOT negated (fine_detail.X_CONVENTION; probe X2
decides, S1b mirrors on branch N).
"""

import os
import sys

_BLENDER = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BLENDER not in sys.path:
    sys.path.insert(0, _BLENDER)
import bldg_recipes as R                                   # noqa: E402
from fine_detail import AFRAME_RACKS, kit_row, wrap       # noqa: E402

BASE = R.L1                                                # the approved pre-pass L1 (plain Recipe)
_RACK_EXTRAS = tuple(e for e in BASE.extras if e.fn == "ski_rack")
assert len(_RACK_EXTRAS) == len(R.L1_SKI_RACKS) == 6, len(_RACK_EXTRAS)


def _fine_extra(e):
    """The base extras with the B.1 fine geometry: the deck flag mast takes fine=True."""
    if e.fn == "flag_mast":
        return R.Extra("flag_mast", dict(e.kwargs, fine=True))
    return e


#: the base extras without the 6 modeled racks (kit rows now), the flag mast fine
FINE_EXTRAS = tuple(_fine_extra(e) for e in BASE.extras if e.fn != "ski_rack")
#: lodge.aframe_parts: PORCH_LAMPS_VANILLA = no modeled porch lanterns (WallLightShortRange rows)
FINE_PARAMS = dict(BASE.params, PORCH_LAMPS_VANILLA=True)
# the porch lanterns as lodge.aframe_parts builds them: lantern(f"porch{tag}", (-16.61, py, 0.52 + 2.2),
# "-X", kind="wall") on the porch columns (col_porch<tag> at x -16.5, 0.22 square: entry face x -16.61)
PORCH_LAMPS = ((-1.9, "S"), (1.9, "N"))
PORCH_LAMP_X, PORCH_LAMP_Z = -16.61, 0.52 + 2.2
_WALL_SHORT = "cfc12fde01d9da24b86ddedd657eaae1"           # WallLightShortRangeRandom01 (SUBOBJ, verified)
#: the porch wall lights: pre-yaw (x, y) -> game (-y, z, -x); facing pre-yaw -X = game +Z = identity
FINE_SUB_OBJECTS = tuple(
    ("WallLightShortRangeRandom01", _WALL_SHORT, (round(-py, 4), round(PORCH_LAMP_Z, 4), round(-PORCH_LAMP_X, 4)),
     (0.0, 0.0, 0.0, 1.0), 0)
    for py, _tag in PORCH_LAMPS)
#: kit color variants (Richard 2026-10-12 ~18:50: "a colour that makes sense for that building";
#: build/kit-colour-variants.md): the A-frame's own slot-2 orange accent (PAD_RGB: the umbrellas / flag of L1)
KIT_COLOUR_SET = "orange"
#: the 6 racks as kit rows at the old centers (pre-yaw (x, y) -> game (-y, 0, -x)), on the lot, identity,
#: alternating SkiDressSkiRack / V2 / V3
KIT_ROWS = tuple(
    kit_row(AFRAME_RACKS[i % len(AFRAME_RACKS)], (round(-y, 4), 0.0, round(-x, 4)), parent=-1,
            note=f"replaces skirack_{n} (ski_rack slots 8 along Y at pre-yaw ({x}, {y}))",
            colour_set=KIT_COLOUR_SET)
    for i, (n, x, y, _gear) in enumerate(R.L1_SKI_RACKS))

L1_FINE = wrap(
    BASE, extras=FINE_EXTRAS, params=FINE_PARAMS,
    sub_objects=tuple(BASE.sub_objects) + FINE_SUB_OBJECTS + KIT_ROWS,
    # fine-detail pass step 3a (opt-in; lodge.py --no-fine restores `legacy` = the approved pre-pass L1)
    fine_detail=True, sign_set=None, kit_colour_set=KIT_COLOUR_SET,
    legacy=(("extras", BASE.extras), ("params", BASE.params), ("sub_objects", BASE.sub_objects)))

#: bldg_recipes.load_recipes(): replace the base RECIPES["L1"] with this one (no RECIPE here: the key
#: would collide)
OVERRIDES = (L1_FINE,)
