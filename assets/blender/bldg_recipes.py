"""
bldg_recipes.py - recipe schema, the A-frame constants and the L1 recipe (DATA only).

Contract: docs/research/art/assets-2026-10/design/bldg-kit.md section 8 (schema), 13.1
(L1), 12 (budgets), 18.3 (slot defaults) with design/bldg-kit-skeptic.md applied. No side
effects at import; nothing here touches bpy.

RECIPES
    The engine (lodge.py) builds a Recipe in one of three massing modes:
      "volumes" - lodge.build_volumes / build_roofs / build_facades / build_extras from
                  the data tuples below (Volume, Extra, FacadeRule);
      "aframe"  - lodge.aframe_parts(params, opts) (L1, the base_lodge.py port);
      "custom"  - recipe.builder(recipe, opts) -> [(obj, matkey)] (any cs2_bldg calls).
    Extras / roofs are Extra(fn, kwargs): `fn` names any cs2_bldg builder, kwargs may hold
    WallRef / RoofSpec / WinSpec objects. Coordinates: the authoring frame of bldg-kit.md
    section 2 (+X = arrival / road side BEFORE yaw_deg is applied; L1 is authored as
    base_lodge with the entry at -X and yawed 180).

REGISTRY (parallel-safe: the recipe builders never edit this file)
    RECIPES holds L1. Each module assets/blender/recipes/<anything>.py that defines RECIPE
    (a Recipe) or RECIPES (a sequence of Recipe) is merged by load_recipes() in sorted file
    order; get_recipe(key_or_alias) resolves "L1" or "basic-base" etc. Keys and aliases
    must be unique across all modules (load_recipes raises). A module's OVERRIDES (fine-detail
    step 3a) replaces a recipe of THIS file's RECIPES by key, once, aliases unchanged:
    recipes/lodge_aframe_fine.py hands in L1 wrapped as a fine_detail.FineRecipe (which cannot
    be imported here: fine_detail imports this module); L1's data stays below, untouched.

AFRAME (DECISIONS 25c "wall plates DERIVED from roof constants", docs/DECISIONS.md:150-160)
    The base set is copied verbatim from base_lodge.py:556-577; aframe_derived() computes
    the five derived values only (MAIN_PITCH 1.32558 / 52.97 deg, S2_Z 6.758, PROW_PITCH
    1.325, S2P_Z 6.625, DECK_TOP 4.66). tests/test_lodge_contracts.py parses base_lodge.py
    with `ast` (importing it mutates cs2_asset.QUAD_RESERVED_TOP, base_lodge.py:107) and
    asserts equality. L1 builds from AFRAME_L1 = aframe_knee(AFRAME, KNEE_H, ...): revision 1
    (2026-10-11) raises the eave on knee walls with the same derivations (the eave moves,
    the ridges follow the pitches, the wall plates stay derived).
"""

import glob
import importlib.util
import math
import os
from dataclasses import dataclass, field

_HERE = os.path.dirname(os.path.abspath(__file__))
RECIPE_DIR = os.path.join(_HERE, "recipes")


# ======================================================================================
# Schema (section 8)
# ======================================================================================

@dataclass(frozen=True)
class Budget:
    lod0_target: int
    lod0_cap: int
    lod1: int
    lod2: int
    lod2_cap: int = 500


@dataclass(frozen=True)
class Volume:
    """A stack of storeys: storeys = ((height_m, wall_matkey), ...) bottom-up. Part names:
    storey 0 `wall_<name>`, storey i > 0 `wall_<name>s<i>`. ground -> storey 0 open-bottom
    from z0 = 0 (engine mode); roofed -> the top storey's +Z face is dropped."""
    name: str
    x0: float
    x1: float
    y0: float
    y1: float
    z0: float
    storeys: tuple
    ground: bool = False
    roofed: bool = True
    cut_z: tuple = ()


@dataclass(frozen=True)
class FacadeRule:
    """Tokens along one side of a Volume storey: W window (window_bay, WinSpec.sill is
    RELATIVE to the storey floor), D entry (storey 0: cs2_bldg.entry; above: a door),
    G curtain-wall bay, S sign board (sign kwargs in `sign`), B blank. n = len(pattern)
    bays of bay_w centred in the wall span minus `margin` (asserts bay_w >= win.w + 0.6).
    room: "bay" (one room per window), "row" (one per rule) or a fixed alphanumeric tag."""
    volume: str
    side: str
    storey: int
    pattern: str
    bay_w: float
    win: object
    margin: float = 0.8
    room: str = "bay"
    door: dict = field(default_factory=dict)
    sign: dict = field(default_factory=dict)


@dataclass(frozen=True)
class Extra:
    fn: str
    kwargs: dict


@dataclass(frozen=True)
class Recipe:
    key: str
    asset: str                  # mesh base name WITHOUT the letter (letter appended)
    prefab: str                 # hand-made BuildingPrefab (catalog Phase A decides)
    category: str               # assets/import/<category>/<letter>
    lot: tuple                  # (W, D) cells: game X = authored Y = W x 8, game Z = D x 8
    base_mode: str              # "engine" | "plinth"
    plinth: object              # (D, U) or None
    yaw_deg: float
    tex: int
    allow_4096: bool
    budget: Budget
    massing: str                # "volumes" | "aframe" | "custom"
    volumes: tuple = ()
    roofs: tuple = ()
    facades: tuple = ()
    extras: tuple = ()
    rooms: object = "auto"      # "auto" | {room: (col, row)}
    interior: str = "CommercialLow"
    slots: dict = field(default_factory=dict)       # {1|2|3: VariationSet rgb} (section 18.3)
    ground_prefixes: tuple = ()
    arrival_prefixes: tuple = ()
    lod1_extra: tuple = ()
    lod2_extra: tuple = ()
    lod2_keep: tuple = ()
    labels: tuple = ()          # ((face_prefix, painter name | callable, kwargs), ...)
    lamps: tuple = ()           # (lantern kwargs dict, ...) built by build_extras
    cull_ok: tuple = ()         # parts allowed to lose > 80 % of their faces
    views: tuple = ()           # extra preview views ((name, (x, y, z)), ...)
    aliases: tuple = ()
    letter: str = "A"
    params: object = None       # massing parameters (AFRAME for "aframe")
    builder: object = None      # callable(recipe, opts) for "custom"
    wall_rule: str = "contact"  # cs2_bldg.cull_hidden wall rule (orchestrator ruling)
    notes: str = ""
    # ---- art pass, detail pass 1 (design/detail-pass-1.md section 4 + skeptic M3). The
    # defaults keep every recipe that does not set them byte-identical (lodge.py takes the
    # old path): weather None = NO weathering. The "auto" default of the weathering design
    # (W 2.2) and of build/weathering-layer.md section 8 is NOT adopted: it would weather
    # L1 / L3 / L4 / L5 behind their owners' backs.
    weather: object = None      # None | {"intensity", "ground", "snow", "slot_wear", ...any
    #                             cs2_weather.WeatherSpec field, "overrides": ((prefix,
    #                             {PartWeather kwargs}), ...)} -> lodge.wrap_weather
    weights: tuple = ()         # ((part-name prefix, density w), ...): longest prefix wins,
    #                             over the kit rules; a part's obj["w_override"] wins over it
    sub_objects: tuple = ()     # vanilla props on the hand-made prefab: ((prefab name, GUID,
    #                             (gx, gy, gz) game local, (x, y, z, w) rotation, m_ParentMesh),
    #                             ...) -> manifest "sub_objects" block (lodge.sub_objects_block)
    # ---- T16 texture-parity probe (2026-10-16, docs/research/art/texture-parity-2026-10-16.md):
    # True = LOD0 on the shared tile sheet (cs2_tilesheet), LOD1/LOD2 on an own far set.
    # False (every recipe) = the unique-atlas path, byte-identical. lodge.py --tiled sets it.
    tiled: bool = False


# ======================================================================================
# The A-frame (base_lodge.py:556-577, verbatim)
# ======================================================================================

AFRAME = {
    "S1_Z": 4.4,
    "EAVE_Z": 5.30,
    "BASE_X0": -13.0, "BASE_X1": 14.0,
    "BASE_Y0": -21.0, "BASE_Y1": 21.0,
    "S2_X0": -12.0, "S2_X1": 3.0,
    "RIDGE_AT_X": -4.5,
    "OVER": 1.1,
    "RIDGE_Z": 16.7,
    "PROW_X1": 10.0,
    "PROW_Y0": -5.0, "PROW_Y1": 9.0,
    "PROW_RIDGE_Y": 2.0,
    "PROW_RIDGE": 15.9,
    "DECK_X1": 13.6,
    "DECK_Y": 20.5,
    "SILL_Z": 4.95, "HEAD_Z": 6.30,
}
AFRAME_DERIVED_NAMES = ("MAIN_PITCH", "S2_Z", "PROW_PITCH", "S2P_Z", "DECK_TOP")


def aframe_derived(c):
    """The five derived A-frame values (base_lodge.py:566-575); never literals (25c)."""
    out = dict(c)
    out["MAIN_PITCH"] = (c["RIDGE_Z"] - c["EAVE_Z"]) / (c["RIDGE_AT_X"] - (c["S2_X0"] - c["OVER"]))
    out["S2_Z"] = c["EAVE_Z"] + out["MAIN_PITCH"] * c["OVER"]
    out["PROW_PITCH"] = (c["PROW_RIDGE"] - c["EAVE_Z"]) / (c["PROW_RIDGE_Y"] - (c["PROW_Y0"] - 1.0))
    out["S2P_Z"] = c["EAVE_Z"] + out["PROW_PITCH"] * 1.0
    out["DECK_TOP"] = c["S1_Z"] + 0.26
    out["MAIN_PITCH_DEG"] = math.degrees(math.atan(out["MAIN_PITCH"]))
    out["PROW_PITCH_DEG"] = math.degrees(math.atan(out["PROW_PITCH"]))
    return out


def aframe_knee(c, knee_h, **over):
    """AFRAME with knee walls (revision 1, Richard's first live review 2026-10-11, verdicts
    item 12: "needs walls at the base of the A frame, it's not a triangle right from the
    base"). The eave EAVE_Z rises by knee_h; the ridges FOLLOW from the unchanged pitches
    (25c: RIDGE_Z = EAVE_Z + MAIN_PITCH x run, PROW_RIDGE likewise), so aframe_derived keeps
    MAIN_PITCH / PROW_PITCH and derives the wall plates S2_Z / S2P_Z = old + knee_h. S1_Z
    (stone storey) and DECK_TOP do not move. `over` sets the non-derived keys (SILL_Z,
    HEAD_Z, DECK_X1, DECK_Y, UMBRELLAS, RIBBON_TRANSOM). The base set AFRAME itself stays
    the verbatim base_lodge port (test_lodge_contracts parses base_lodge.py against it)."""
    d = aframe_derived(c)
    out = dict(c)
    out["KNEE_H"] = float(knee_h)
    out["EAVE_Z"] = c["EAVE_Z"] + knee_h
    out["RIDGE_Z"] = out["EAVE_Z"] + d["MAIN_PITCH"] * (c["RIDGE_AT_X"] - (c["S2_X0"] - c["OVER"]))
    out["PROW_RIDGE"] = out["EAVE_Z"] + d["PROW_PITCH"] * (c["PROW_RIDGE_Y"] - (c["PROW_Y0"] - 1.0))
    bad = set(over) & set(AFRAME_DERIVED_NAMES + ("EAVE_Z", "RIDGE_Z", "PROW_RIDGE", "KNEE_H"))
    if bad:
        raise ValueError(f"aframe_knee: {sorted(bad)} are derived, not set")
    out.update(over)
    return out


# ======================================================================================
# L1 - the A-frame day lodge, v2 (lodges/B). Section 13.1 + skeptic MUST-CHANGE 3/4.
# ======================================================================================

PAD_RGB = (0.85, 0.25, 0.10)            # cs2_asset.PAD_RGB (slot 2 default, umbrellas)

# ---- Revision 1 (Richard's first live review 2026-10-11, verdicts-2026-10-11.md item 12) --
# Knee walls: 2.40 m of vertical timber wall under the eaves (eave 5.30 -> 7.70, ridge 16.7 ->
# 19.1, prow ridge 15.9 -> 18.3, wall plates S2_Z 9.158 / S2P_Z 9.025; pitches unchanged).
# Visible wall deck -> fascia 2.77 m (A-Basin: one clad storey above the deck under the eave).
# Ribbon windows 5.25-7.05 with a transom at 6.55 (fascia bottom 7.43). Deck +1.5 m on the
# slope side on posts (DECK_X1 13.6 -> 15.1, slab to 15.3 past the cap 14.2) and to the full
# base width (rails at y +-21.0, slab +-21.2). Umbrellas respaced into two staggered rows per
# wing, clear of the prow roof plan (|y - 2| > 10.05 inside x 13.15) and 1.70 m off the rails.
# Fifteenth look (2026-10-11, vanilla retrofit): UMBRELLAS = the TABLE spots (one vanilla table set
# + one vanilla parasol each, L1_TABLES / L1_PARASOLS); PARASOLS_VANILLA = no modelled umbrella.
# The outer rows' first two spots moved toward the wall (6.5 / 10.0 -> 5.6 / 9.5): three vanilla
# canopies (union 3.78 m along their facing axis) do not fit 3.4-3.5 m apart (see L1_PARASOLS).
KNEE_H = 2.40
AFRAME_L1 = aframe_knee(
    AFRAME, KNEE_H,
    SILL_Z=5.25, HEAD_Z=7.05, RIBBON_TRANSOM=6.55,
    DECK_X1=15.1, DECK_Y=21.0, DETAIL1=True, PARASOLS_VANILLA=True,
    UMBRELLAS=((5.6, -17.6), (9.5, -17.6), (13.4, -17.6), (8.25, -12.0), (11.75, -12.0),
               (5.6, 17.6), (9.5, 17.6), (13.4, 17.6), (8.25, 13.0), (11.75, 13.0)),
)
# Ski racks (Richard: "ski racks around the base - not a prop in the game so add it to the
# asset"): modelled A-frame racks (cs2_bldg.ski_rack slots=, 8 slots x 0.20 m per side,
# 1.70 m), pre-yaw frame: two flank the porch (arrival), four stand in front of the deck
# posts on the slope side, clear of the deck stair (x 15.6-17.0, y -5.8..3.0). Skis / boards
# = slot-2 slabs, (slot, side, kind); LOD0 only (`skirack_` is on LOD1_DELETE).
# (revision 1 slab racks: superseded by detail pass 1 below)
# ---- Detail pass 1 (Richard's second verdict 2026-10-11 ~14:10: "A frame lodge looks solid
# nice work. Now for the detail pass on that one, actual skis/boards on the racks instead of
# wooden planks, put our icon on it etc. Brown looks good on it.") ---------------------------
# Rack contents are REAL gear (cs2_bldg.ski_rack_frame gear=True): ski pairs base to base
# (sidecut, curled tips), snowboards (rounded nose / tail, kick, two graphic bands), pole
# pairs; NO bindings (Richard, ninth look: "they just come out as blocks and looks bad");
# colours = accent (slot 2, recolours with the umbrellas) +
# white / black / blue / red paint, (body, tip) or (top, stripe). Entries (slot, side, kind,
# colour, length). Pre-yaw frame as above; LOD0 only (`skirack_` is on LOD1_DELETE).
# Every item must span the rack's lean, cs2_bldg.rack_reach() = 1.33 m (detail pass 1
# integration F1): the six pole pairs were 1.18-1.28 m and stopped 0.05-0.15 m short of the
# rail; now 1.35-1.40 m (the tip passes the lean point by 0.02-0.07 m).
_A, _W, _K, _B, _R = "accent", "trim_white", "ski_black", "ski_blue", "ski_red"
L1_SKI_RACKS = (
    ("entryS", -15.2, -6.8, (
        (0, 1, "ski", (_W, _B), 1.72), (1, 1, "ski", (_K, _A), 1.64),
        (2, 1, "board", (_K, _A), 1.54), (4, 1, "ski", (_R, _K), 1.60),
        (5, 1, "poles", _K, 1.38), (6, 1, "ski", (_B, _W), 1.78),
        (1, -1, "ski", (_A, _K), 1.70), (3, -1, "board", (_W, _R), 1.50),
        (4, -1, "ski", _W, 1.58), (6, -1, "ski", (_K, _W), 1.75))),
    ("entryN", -15.2, 6.8, (
        (0, 1, "board", (_B, _W), 1.56), (1, 1, "ski", (_W, _R), 1.68),
        (3, 1, "ski", _K, 1.62), (4, 1, "ski", (_A, _K), 1.74),
        (6, 1, "board", (_R, _W), 1.48), (7, 1, "ski", (_W, _B), 1.60),
        (0, -1, "ski", (_B, _W), 1.66), (2, -1, "poles", _A, 1.36),
        (3, -1, "ski", (_R, _K), 1.58), (5, -1, "ski", (_W, _A), 1.80))),
    ("slopeS1", 16.4, -15.0, (
        (0, 1, "ski", (_K, _A), 1.76), (1, 1, "ski", _W, 1.62),
        (2, 1, "board", (_A, _K), 1.52), (3, 1, "ski", (_B, _W), 1.70),
        (5, 1, "ski", (_R, _K), 1.58), (6, 1, "poles", _B, 1.40),
        (7, 1, "ski", (_W, _B), 1.66), (1, -1, "ski", (_W, _R), 1.72),
        (4, -1, "board", (_K, _A), 1.55), (6, -1, "ski", (_A, _K), 1.64))),
    ("slopeS2", 16.4, -10.5, (
        (1, 1, "ski", (_B, _W), 1.80), (2, 1, "ski", (_K, _W), 1.60),
        (4, 1, "board", (_W, _R), 1.46), (5, 1, "ski", (_A, _K), 1.68),
        (7, 1, "ski", _R, 1.56), (0, -1, "ski", (_W, _B), 1.62),
        (2, -1, "ski", (_R, _K), 1.74), (3, -1, "poles", _K, 1.37),
        (5, -1, "board", (_B, _W), 1.58), (7, -1, "ski", _K, 1.70))),
    ("slopeN1", 16.4, 7.5, (
        (0, 1, "ski", (_W, _A), 1.66), (2, 1, "board", (_R, _W), 1.54),
        (3, 1, "ski", (_K, _A), 1.78), (4, 1, "ski", (_B, _W), 1.60),
        (6, 1, "ski", _W, 1.70), (7, 1, "poles", _R, 1.35),
        (1, -1, "ski", (_A, _K), 1.58), (3, -1, "ski", (_W, _R), 1.72),
        (5, -1, "board", (_K, _A), 1.50), (6, -1, "ski", (_B, _W), 1.64))),
    ("slopeN2", 16.4, 12.0, (
        (0, 1, "ski", (_R, _K), 1.70), (1, 1, "ski", (_W, _B), 1.64),
        (3, 1, "board", (_A, _K), 1.56), (5, 1, "ski", _K, 1.76),
        (6, 1, "ski", (_B, _W), 1.58), (0, -1, "poles", _K, 1.39),
        (2, -1, "board", (_W, _R), 1.52), (3, -1, "ski", (_K, _W), 1.68),
        (5, -1, "ski", (_A, _K), 1.62), (7, -1, "ski", (_W, _A), 1.74))),
)
# Leaning singles: against the stone base either side of the arrival racks, and against the
# slope-side deck posts (col_deckext at x 15.05, faces 15.15; y +-16.76 / +-20.95), clear of
# the racks, the flag and the deck stair. (kind, foot, lean_to, colour, length).
L1_LEANING = (
    ("ski", (-13.62, -9.40, 0.04), (-13.0, -9.40, 1.48), (_W, _B), 1.72),
    ("board", (-13.52, -10.0, 0.04), (-13.0, -10.0, 1.30), (_B, _W), 1.52),
    ("ski", (-13.60, 9.60, 0.04), (-13.0, 9.60, 1.45), (_K, _A), 1.66),
    ("poles", (-13.42, 9.98, 0.04), (-13.0, 9.98, 1.05), _A, 1.20),
    ("ski", (15.72, 16.76, 0.04), (15.15, 16.76, 1.50), (_R, _K), 1.70),
    ("board", (15.66, 20.95, 0.04), (15.15, 20.95, 1.35), (_K, _A), 1.55),
    ("ski", (15.72, -20.95, 0.04), (15.15, -20.95, 1.45), (_A, _K), 1.64),
    ("board", (15.66, -16.76, 0.04), (15.15, -16.76, 1.32), (_W, _R), 1.50),
)
# "our icon" = the cabins' glyph (lift_cabin.glyph_alpha: ski_launcher_glyph cand_c, peak +
# snowflake), white on deck-sign green, stamped by cs2_bldg.paint_glyph on two painted_board
# signs in a raised white frame: over the entry on the porch gable (road side, 1.00 m board)
# and on the prow glass gable (slope side, 1.16 m, on galv brackets to the clerestory
# mullions). SIGN_W = texel-density weight of the two glyph faces (part_weights w_override):
# >= 400 px/m shipped. The 8 cm frame is geometry, so only the 0.84 / 1.00 m faces pay for
# the density. Measured (scratch bakes, w=1 median px/m; 20.06 before this pass, gear alone
# 19.67): faces 1.0 + 1.4 m 16.86; 0.84 + 1.14 m 16.56; 0.84 + 1.00 m 18.41 (chosen);
# entry only 18.59 - Blender's AABB packer is not monotonic in island size.
SIGN_W = 24.0
SIGN_FRAME = 0.08
_D1 = aframe_derived(AFRAME_L1)
_DTOP, _EAVE = _D1["DECK_TOP"], AFRAME_L1["EAVE_Z"]
_PX1, _PRY, _BX0 = AFRAME_L1["PROW_X1"], AFRAME_L1["PROW_RIDGE_Y"], AFRAME_L1["BASE_X0"]
# ---- Detail pass 1b (Richard, ninth look: "the umbrellas need tables - use the in-game
# tables"): one vanilla cafe table set (a table with its two chairs, ActivityLocation seats)
# under each of the ten deck umbrellas, as ObjectSubObjects of SkiAFrameDayLodge (manifest
# `sub_objects`, m_ParentMesh 0 = on the deck). TableSetCommercialRandom01 is the placeholder
# of TableSetCommercial01-06 (base game, 203 buildings place it; the L2 hut uses the same
# GUID; cs2_liftdress.SUBOBJ holds it verified against the dump). Chosen over the table alone
# (TableCommercial: no seats; its variants 05/06 are 0.9 x 2.2 m benches): the set brings
# its matching chairs. Fit: every variant is <= 1.09 m tall (the canopy rim is 2.18 m
# above the deck) and its footprint corner lies <= 1.57 m from the pivot (variant 04; the
# rim radius is 1.55, a bounding-box corner only). The umbrella's pole stands on the pivot.
# Position = the umbrella's (x, y) on the deck top; GAME local (gx, gy, gz) = (-y, z, -x) of
# the pre-yaw authoring point (yaw_deg 180, then lodge.apply_yaw / orient_for_net / the FBX
# axes: gx = y', gz = x'); identity rotation (the chairs sit on the game z axis).
# Fifteenth look: the modelled umbrellas are gone; a vanilla parasol stands beside each table
# (L1_PARASOLS) and two outer-row spots moved (AFRAME_L1 comment).
_TABLE_GUID = "fc058a3d03b337e4794c432a84170316"
L1_TABLES = tuple(
    ("TableSetCommercialRandom01", _TABLE_GUID, (round(-uy, 4), round(_DTOP, 4), round(-ux, 4)),
     (0.0, 0.0, 0.0, 1.0), 0)
    for ux, uy in AFRAME_L1["UMBRELLAS"])
# ---- Vanilla retrofit (fifteenth look, 2026-10-11; Richard: "are we gonna use our house-made
# umbrellas?" -> no). The ten modelled umbrellas (umbstand_ / umbpole_ / umbcanopy_) are vanilla
# ParasolRandom01 (Parasol01-03, base game, 241 buildings place it; GUID re-derived from the dump:
# PlaceholderObject, 3 variants, union bounds x +-1.75, y 0..2.641, z -0.726..3.056 = an OFFSET
# canopy). Placement = the vanilla convention measured on the dump (442 parasol / table-set pairs
# within 2.5 m in base-game prefabs): the parasol pivot (its stand; StandingObject leg 0.6 x 1.9 x
# 0.6) sits PARASOL_OFFSET from the table, local +Z pointing at it (median 1.37 m, quartiles 1.30 /
# 1.54; |lateral| < 0.4 m in 362 of 442) and never on the table's pivot (0 of 442 within 0.3 m), so
# the canopy hangs over the table. Facing along the deck's x axis (the canopy's 3.5 m local X then
# spans y, between the festoons L1_STRINGS): +1 = faces authoring +X (pole on the wall side); the
# inner rows' slope-side spot faces -X (both poles outside the pair). Contract (scratch check and
# test_detail_kit K9b, the review convention of recipes/l5: pole +-0.30 to 1.80, canopy tier = the
# union footprint from 1.80 up): no L1 triangle in any box, no box overlap but a pole in its own
# table set. Summer only: every Parasol variant carries WeatherObject m_ForbidSnow (hidden while
# the lodge's Surface.m_AccumulatedSnow >= 15, SubObjectSystem.cs:3231-3233, 1.6.0f1).
PARASOL_GUID = "b7e2343fd7ff9f642b77084a04738e43"
PARASOL_OFFSET = 1.37
L1_PARASOL_FACING = (1, 1, 1, 1, -1, 1, 1, 1, 1, -1)
_Q_AUTH_X = {1: (0.0, 1.0, 0.0, 0.0), -1: (0.0, 0.0, 0.0, 1.0)}   # yaw 180: game -Z = authoring +X
L1_PARASOLS = tuple(
    ("ParasolRandom01", PARASOL_GUID,
     (round(-uy, 4), round(_DTOP, 4), round(-(ux - f * PARASOL_OFFSET), 4)), _Q_AUTH_X[f], 0)
    for (ux, uy), f in zip(AFRAME_L1["UMBRELLAS"], L1_PARASOL_FACING))
# The porch bench and bin (detail pass 1, modelled) are vanilla too: GardenBenchRandom01
# (GardenBench01-04, 1,065 buildings; union x +-1.059, z +-0.496, h 1.054) with its back to the
# stone base, the sitter facing away from it (the variants' ActivityLocation seats are at identity
# rotation = local +Z = game +Z = authoring -X), pivot its half depth + 0.05 m off the wall face;
# TrashbinRandom01 (Trashbin01-04, 689 buildings; Empty / Full meshes +-0.355 x +-0.364, h 1.04) on
# the modelled bin's spot. Both on the flattened lot: m_ParentMesh -1, gy 0 (the school's ground
# rows). Same y as before (bench 4.35, bin -3.55).
L1_PORCH = (
    ("GardenBenchRandom01", "8df0703be09426948b5bc3262d86bc04", (-4.35, 0.0, round(-(_BX0 - 0.546), 4)),
     (0.0, 0.0, 0.0, 1.0), -1),
    ("TrashbinRandom01", "41e3c9ba6e1d6b948a6b1bc3499a5397", (3.55, 0.0, round(-(_BX0 - 0.42), 4)),
     (0.0, 0.0, 0.0, 1.0), -1),
)
_FASCIA_X = AFRAME_L1["S2_X1"] + AFRAME_L1["OVER"] + 0.36     # just past the E gutter
_POLE_X = AFRAME_L1["DECK_X1"] + 0.12                          # outside rail F
L1_STRINGS = (-19.6, -15.3, 15.3, 19.6)     # festoon y: between / outside the umbrella rows
L1_DETAIL = (
    Extra("ski_gear", dict(name="lean", items=L1_LEANING)),
    Extra("painted_board", dict(name="resortE", wall=("-X", -17.15), u=0.0,
                                z=3.31 + SIGN_FRAME, w=1.0 - 2 * SIGN_FRAME,
                                h=1.0 - 2 * SIGN_FRAME, t=0.05, frame=SIGN_FRAME,
                                mat="trim_white", density_w=SIGN_W)),
    Extra("painted_board", dict(name="resortP", wall=("+X", _PX1 + 0.15), u=_PRY,
                                z=11.02 + SIGN_FRAME, w=1.16 - 2 * SIGN_FRAME,
                                h=1.16 - 2 * SIGN_FRAME, t=0.06, frame=SIGN_FRAME,
                                mat="trim_white", density_w=SIGN_W)),
    Extra("boxes_part", dict(name="sign_resortPbrkt", mat="galv", boxes=tuple(
        ((_PX1 + 0.15, u0, z0), (_PX1 + 0.20, u1, z0 + 0.06))
        for z0 in (11.16, 11.96) for (u0, u1) in ((0.81, _PRY - 0.58), (_PRY + 0.58, 3.19))))),
    Extra("painted_board", dict(name="menu", wall=("-X", _BX0), u=4.6, z=1.05, w=0.75, h=0.95,
                                t=0.04, frame=0.04, mat="timber_heavy", density_w=6.0)),
    # (the porch bench and bin are vanilla sub-objects since the fifteenth look: L1_PORCH)
    # flag panel ends at x 17.06, inside the stair landing extent (17.075): no LOD1 bounds drift
    Extra("flag_mast", dict(name="deck", x=15.50, y=-21.75, h=9.0, flag=(1.5, 0.9))),
) + tuple(
    Extra("string_lights", dict(name=f"deck{k}", sag=0.45, bulb_every=1.35,
                                anchors=((_FASCIA_X, y, _EAVE - 0.26, None),
                                         (_POLE_X, y, _EAVE - 0.40, _DTOP))))
    for k, y in enumerate(L1_STRINGS))
L1_LABELS = (("sign_resortE_face", "paint_glyph",
              {"face": "sign_resortE_face", "keyline": None}),
             ("sign_resortP_face", "paint_glyph",
              {"face": "sign_resortP_face", "keyline": None}),
             ("sign_menu_face", "paint_glyph", {"face": "sign_menu_face", "kind": "menu"}))


L1 = Recipe(
    key="L1", aliases=("basic-base",), letter="B",
    asset="SkiLodgeAFrameCommercialLow", prefab="SkiAFrameDayLodge", category="lodges",
    lot=(6, 5), base_mode="engine", plinth=None, yaw_deg=180.0, tex=2048, allow_4096=False,
    budget=Budget(12000, 16000, 5500, 500, 500),
    massing="aframe", params=AFRAME_L1,
    extras=tuple(Extra("ski_rack", dict(name=n, x=x, y=y, slots=8, mat="galv", along="Y",
                                        skis=s, ski_mat="accent", gear=True))
                 for n, x, y, s in L1_SKI_RACKS) + L1_DETAIL,
    rooms="auto", interior="CommercialLow",
    # VariationSet values = target / RECOLOR_ALBEDO 0.80 (section 18.3; colour space as the
    # shipped steel set, unverified): trim green, umbrellas pad orange, roof grey (fifteenth
    # look: no modelled umbrella; slot 2 still drives the accent gear and the flag cloth).
    slots={1: (0.0625, 0.3125, 0.25), 2: PAD_RGB, 3: (0.69, 0.69, 0.675)},
    # ground parts (open bottom, z 0 edges): stone base, porch steps, chimney shoulder,
    # deck-stair stringers, landing columns (skeptic MUST-CHANGE 4 + the stair stringers)
    ground_prefixes=("wall_base", "step_", "chim_north", "stair_deck_str", "col_decklanding"),
    arrival_prefixes=("step_", "entry_", "roof_porch"),
    # detail pass 1: porch furniture, menu; the flag cloth goes with its mast at LOD1
    # (detail1-check-aframe F1 / detail-pass-1 skeptic M8: `flagpanel_` is on the kit's
    # LOD2 list only, so the 1.5 x 0.9 m cloth floated at LOD1 with no mast under it)
    lod1_extra=("sign_menu", "flagpanel_"),       # bench_ / bin_: vanilla since the 15th look
    # LOD2 keeps the silhouette: treads, porch timbers, the landing and the deck posts go
    lod2_extra=("stair_deck_treads", "col_porch", "gable_porch", "deck_landing", "col_deckext"),
    lod2_keep=(),
    labels=L1_LABELS,
    # detail pass 1b: a vanilla table set at each umbrella spot; fifteenth look: a vanilla parasol
    # beside each (no modelled umbrella), the porch bench + bin vanilla (22 rows)
    # T26 2026-10-16: parasols dropped (random yellow): L1_PARASOLS stays defined, not used here
    sub_objects=L1_TABLES + L1_PORCH,
    cull_ok=("ridge_main", "ridge_prow", "soffit_mainE"),   # caps inside the slab X; the E
    # soffit faces the wall over the deck (no camera position sees it)
    views=(("prow", (0.55, 1.0, 0.30)), ("entry", (0.35, -1.0, 0.22)),
           ("roofwindows", (-1.0, 0.25, 0.85))),
    notes="base_lodge.py port: skirt deleted, porch slab a ground part, entry surround on "
          "the porch (z 0.52). Revision 1 (verdicts 2026-10-11 item 12): 2.40 m knee walls "
          "(aframe_knee), deck +1.5 m on posts and full width, 6 modelled ski racks (LOD0). "
          "Detail pass 1 (Richard's second verdict 2026-10-11): real ski / board / pole gear "
          "on the racks + 8 leaning singles, the resort-glyph sign over the entry and on the "
          "prow, menu board, bench, bin, flag, 4 deck festoons, prow snow guards + downspouts. "
          "Detail pass 1b (ninth look, 2026-10-11): rack gear without bindings, pole pairs "
          "that reach the rail, a vanilla table set under each deck umbrella (sub_objects). "
          "Vanilla retrofit (fifteenth look, 2026-10-11): the 10 modelled umbrellas, the porch "
          "bench and bin are vanilla ParasolRandom01 / GardenBenchRandom01 / TrashbinRandom01 "
          "sub-objects (parasol 1.37 m beside its table, the vanilla convention)",
)

RECIPES = {L1.key: L1}


# ======================================================================================
# Registry
# ======================================================================================

def _load_module(path):
    name = "bldg_recipe_" + os.path.splitext(os.path.basename(path))[0]
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def load_recipes(recipe_dir=RECIPE_DIR):
    """RECIPES + every recipes/*.py module's RECIPE / RECIPES (sorted file order).
    Raises on a duplicate key or alias."""
    out = dict(RECIPES)
    overridden = set()
    if os.path.isdir(recipe_dir):
        for path in sorted(glob.glob(os.path.join(recipe_dir, "*.py"))):
            if os.path.basename(path).startswith("_"):
                continue
            mod = _load_module(path)
            found = []
            if hasattr(mod, "RECIPE"):
                found.append(mod.RECIPE)
            found.extend(getattr(mod, "RECIPES", ()) or ())
            for r in found:
                if not isinstance(r, Recipe):
                    raise TypeError(f"{path}: {r!r} is not a bldg_recipes.Recipe")
                if r.key in out:
                    raise ValueError(f"{path}: recipe key {r.key!r} already defined")
                out[r.key] = r
            for r in getattr(mod, "OVERRIDES", ()) or ():
                if not isinstance(r, Recipe) or r.key not in RECIPES or r.key in overridden \
                        or tuple(r.aliases) != tuple(RECIPES[r.key].aliases):
                    raise ValueError(f"{path}: OVERRIDES {getattr(r, 'key', r)!r} must replace a "
                                     f"bldg_recipes.RECIPES recipe once, with its aliases")
                out[r.key] = r
                overridden.add(r.key)
    seen = {}
    for r in out.values():
        for a in (r.key,) + tuple(r.aliases):
            if a in seen and seen[a] != r.key:
                raise ValueError(f"recipe alias {a!r} used by {seen[a]} and {r.key}")
            seen[a] = r.key
    return out


def get_recipe(key, recipes=None):
    recipes = recipes or load_recipes()
    for r in recipes.values():
        if key == r.key or key in r.aliases:
            return r
    raise KeyError(f"no recipe {key!r} (have {sorted(k for r in recipes.values() for k in (r.key,) + tuple(r.aliases))})")
