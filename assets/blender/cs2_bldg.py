"""
cs2_bldg.py - the building kit for the 2026-10 lodge / school / patrol round.

Contract: docs/research/art/assets-2026-10/design/bldg-kit.md (sections 4-7, 9) with the
eight MUST-CHANGE items of design/bldg-kit-skeptic.md applied (the skeptic wins where they
disagree). The recipe engine is lodge.py, the recipes are data in bldg_recipes.py and
recipes/<key>.py. No side effects at import (unlike base_lodge.py:107).

CONVENTIONS
    Every builder returns a parts list [(obj, matkey)], matkey in BLDG_MATERIALS. Part
    names start with the section-5 prefix (`wall_`, `roof_`, `win_<room>_<k>`, ...); a
    duplicate object name raises (Blender would silently rename it `.001` and break every
    prefix match, skeptic "Determinism"). Geometry is written in WORLD coordinates into
    the mesh (objects at identity) except the few cs2_kit parts (door, handrail), which
    carry loc/rot on the object; the join bakes both.
    Authoring frame (section 2): +X = arrival / road, -X = back (slope), +Y/-Y = lot width,
    z 0 = grade at the lot centre. WallRef(side, plane, u0, u1): side in +X/-X/+Y/-Y, plane
    = the wall's outer coordinate, u = position along the wall growing to the viewer's right
    when facing the wall from outside: +X face u = y, -X face u = -y, +Y face u = -x,
    -Y face u = x (the box-projection sign rule). No randomness except crc32 seeds.
    COLOUR SPACES (skeptic MUST-CHANGE 7): every colour constant below is declared
    `_srgb` (display-encoded, what a photo sample or a PNG byte/255 gives) or `_lin`
    (linear light). BldgMat.target_srgb / emissive_srgb are encoded; they are linearised by
    material_specs() before they reach cs2_bake.MaterialSpec (whose albedo is LINEAR,
    cs2_bake.py:198-201). Painters receive LINEAR BaseColor views (post_compose runs before
    the sRGB encode, cs2_bake step 4) and linearise their own sRGB constants.

ENGINE BASE / GROUND BAND (section 3, skeptic MUST-CHANGE 3 + 4)
    Engine mode: a part whose min z < 0.011 must be an open-bottom ground part (listed in
    recipe.ground_prefixes) or start at z >= 0.02; check_base_mode enforces it. Builders
    that may sit at grade but are not ground parts (door, entry surround, lanterns, flag
    masts, racks, akja, downspouts, umbrellas) lift themselves to GROUND_LIFT = 0.02.
    Plinth mode: no vertex in |z| <= 0.01 anywhere; plinth stairs use ground=False with the
    foot below grade (stair(..., ground=False, foot z <= -0.05)).

CC0 PREFILTER (skeptic MUST-CHANGE 6, N13)
    cs2_bake point-samples its sources once per texel (EMIT_SAMPLES 1, cs2_bake.py:147);
    a 2K CC0 tile minified 50-65x aliases (skeptic alias2.py: 1.7x the box-filtered texel
    noise). prefilter_set() area-averages Color (in linear light), NormalGL (decoded,
    averaged, renormalised) and Roughness to `factor` x the predicted shipped px/m,
    stacking the 2:1 sets (WoodSiding009, Bricks096, Concrete034) into square tiles so a
    scalar MaterialSpec.metres_per_tile keeps UVTile ISOTROPIC (pack_unique scales islands
    uniformly: an anisotropic UVTile ships anisotropic texels, skeptic "UV source layer").
    Output is deterministic (separable exact-overlap box filter, sorted inputs) and cached
    in <tempdir>/cs2_bldg_prefilter/<Set>_<N>/ (meta.json carries the source sha1s).

PUBLIC API (one line each; full signatures below)
    data:     BldgMat, WallRef, RoofSpec, WinSpec, BLDG_MATERIALS, BEVEL_PREFIXES,
              LABEL_PREFIXES, LOD1_DELETE, LOD2_DELETE, WIN_POOL, NEIGHBOUR_M, GROUND_LIFT,
              srgb_to_linear, linear_to_srgb, roof_z, roof_frame, chimney_top, wall_point
    massing:  storey, plinth, prism_gable, roof_slab, gable_roof, hip_roof, shed_roof,
              flat_roof, dormer, roof_window, vent_stack, chimney, stovepipe
    facade:   window_bay, pane_quad, wall_box, wall_boxes, box_part, boxes_part, bisect_part,
              curtain_wall, entry, sign_board, emblem_disc, emblem_square, stone_pier, col,
              beam, bracket, truss_kingpost
    outdoor:  deck, stair, rail, balcony, gazebo, porte_cochere, tower, lantern, flag_mast,
              umbrella, ski_rack (painted panel | slots= modelled A-frame), ski_rack_frame,
              SKI_SLABS, akja, snow_guard, gutter, downspout
    detail 1: ski_gear (+ ski_rack_frame gear=True: real ski pairs / boards / poles,
              GEAR_KINDS, GEAR_LEN, GEAR_W; NO bindings by default, keyword bindings=True
              for tests; rack_reach() = the length a rack item needs, 1.33 m),
              painted_board (stamp target, obj["w_override"]),
              bench, litter_bin, string_lights (L1 detail pass 1, 2026-10-11)
    detail 1b (school / L2 / patrol, design/detail-pass-1.md 6.1 + skeptic M1-M10):
              post_sign, maze_fence, radio_mast, toboggan_rack, backboard, hose_reel, drum,
              fuel_cradle, log_stack, safety_stripe, roof_emblem; keywords bench(z0, back),
              flag_mast(z0, along, face), ski_rack_frame(z0, leg_to), prism_gable(inner),
              gable_roof(wall_inner), deck(soffit); materials rubber, safety_yellow,
              fire_red, lamp_red; WEATHER_ROLES_EXTRA (lodge.wrap_weather)
    material: bevel_parts, strip_uv_layers, projection_specs, material_specs, prefilter_set,
              box_filter, image_tile_m, square_tile_m, retile (cross nets, along, uv_rot),
              align_islands, split_poor_islands, pack_layout_calibrated, calibrated_packer
    engine:   cull_hidden, check_lot_fit, check_facing, check_base_mode, assert_budget,
              room_graph, assign_rooms, split_rooms, win_cell_ids, panes_on_surface,
              area_report, shipped_density, part_weight, part_weights, detail_specs,
              contact_map, lod_level, plan_lod_targets, lod_chain
    painters: paint_cross_disc, paint_pictogram, paint_skis, paint_clock, paint_text5x7,
              paint_glyph (the cabins' resort glyph / a menu board, mesh-oriented),
              make_painter (recipe label entries); DisplayPainter + draw_text,
              draw_shapes, draw_glyph, draw_pictogram, draw_cross_square, draw_bibs,
              draw_notices, draw_logs (DRAWS; factory "display"), label_metrics

PACKING (measured on L1 2026-10-11, build/lodge.md): box projection splits every box face
into its own UV island and cs2_uv packs axis-aligned boxes without rotation through
Blender's FRACTION-margin solver, which collapses with ~1,000+ islands. Small boxes carry a
cross-net UV (one island per box), poorly filled islands are rotated / split before the
bake, and lodge.py routes its bake through pack_layout_calibrated (ADD margin calibrated
to real pixels; library function restored on exit).

Derived from (file:line): base_lodge.py:184-240 (gable prism, roof slab), :589-608 (window
band members), :729-770 (deck, rail_run, umbrellas), :819-873 (porch, entry, chimney),
:876-891 (bevel); cs2_kit.py:30-37 (open-bottom rule), :513-560 (handrail), :647-683
(door); cs2_bake.py:1271-1390 (material_sources, project_world_uv), :919 (bake_unique);
cs2_submesh.py:210-278 (split_submesh); cs2_validate.py:447-503 (ground_ring_report);
cs2_asset.py:1075-1133 (make_lod_chain, re-implemented here with the N9 planar pass and
ground protection); lift_terminal.py:1139-1173 (split_windows), :401-431 (compose hook).
"""

import functools
import hashlib
import json
import math
import os
import sys
import tempfile
import zlib
from dataclasses import dataclass, replace

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset     # noqa: E402
import cs2_bake      # noqa: E402
import cs2_compose   # noqa: E402
import cs2_kit       # noqa: E402
import cs2_submesh   # noqa: E402
import cs2_uv        # noqa: E402
import cs2_validate  # noqa: E402


# ======================================================================================
# Colour transfer (sRGB IEC 61966-2-1)
# ======================================================================================

def srgb_to_linear(c):
    """Encoded sRGB (scalar, tuple or array) -> linear light."""
    a = np.asarray(c, dtype=np.float64)
    out = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
    if np.ndim(out) == 0:
        return float(out)
    return tuple(float(x) for x in np.ravel(out)) if isinstance(c, (tuple, list)) else out


def linear_to_srgb(c):
    """Linear light (scalar, tuple or array) -> encoded sRGB."""
    a = np.clip(np.asarray(c, dtype=np.float64), 0.0, None)
    out = np.where(a <= 0.0031308, a * 12.92, 1.055 * a ** (1.0 / 2.4) - 0.055)
    if np.ndim(out) == 0:
        return float(out)
    return tuple(float(x) for x in np.ravel(out)) if isinstance(c, (tuple, list)) else out


def _rgb3(c):
    if isinstance(c, (int, float)):
        return (float(c),) * 3
    return tuple(float(x) for x in c)[:3]


# ======================================================================================
# Data types and constants (section 4.1)
# ======================================================================================

@dataclass(frozen=True)
class BldgMat:
    """One building material (section 7.1). Colours are ENCODED sRGB (`_srgb`)."""
    set: object = None              # CC0 set folder name under assets/textures, or None (flat)
    tile_m: object = None           # (u, v) metres per tile in the WORLD (after uv_rot)
    target_srgb: object = None      # encoded mean albedo after tint; None = native / RECOLOR
    smooth: object = None           # None = 1 - set Roughness per texel
    metal: float = 0.0
    slot: int = 0                   # ControlMask channel 1 R, 2 G, 3 B (0 none)
    recolor: bool = False           # luminance-normalised to RECOLOR_ALBEDO (0.80 encoded)
    detail: object = None           # cs2_compose.DetailSpec or None
    density_w: float = 1.0
    proj: str = "box"               # "box" | "inplane" | "along"
    uv_rot: int = 0                 # 0 | 90 (vertical boards)
    emissive_srgb: object = None    # encoded emissive colour (lamp)
    tint: object = None             # explicit LINEAR multiply (overrides target_srgb)


@dataclass(frozen=True)
class WallRef:
    side: str                       # "+X" | "-X" | "+Y" | "-Y"
    plane: float                    # outer face coordinate on the side's axis
    u0: float = 0.0
    u1: float = 0.0


@dataclass(frozen=True)
class RoofSpec:
    """A pitched roof over the wall rectangle [x0, x1] x [y0, y1]. `eave_z` is the height
    of the slab CENTRE plane at the wall line (the A-frame's wall plate S2_Z); the eave tip
    sits `overhang` further out and tan(pitch) x overhang lower (base_lodge 25c rule)."""
    kind: str
    x0: float
    x1: float
    y0: float
    y1: float
    eave_z: float
    pitch_deg: float
    axis: str = "Y"                 # ridge direction
    overhang: float = 0.6
    rake_over: object = None        # gable-end overhang (default = overhang)
    ridge_at: object = None         # ridge position across the ridge axis (default centre)
    eave_z1: object = None          # second side's wall-line height (saltbox)
    thick: float = 0.24
    mat: str = "slate"
    trim: str = "trim"


@dataclass(frozen=True)
class WinSpec:
    """A window: total width `w` (all lights), height `h`, sill = ABSOLUTE z of the bottom
    of the glass in window_bay (lodge.build_facades converts its storey-relative sill)."""
    w: float
    h: float
    sill: float
    lights: int = 1
    rows: int = 1
    transom: object = None          # absolute z of a transom bar (splits each light in two)
    reveal: float = 0.10
    frame: bool = True


GROUND_LIFT = 0.02          # closed parts at grade start here (skeptic MUST-CHANGE 4)
GROUND_BAND = 0.011         # parts with min z below this must be open-bottom ground parts
PANE_OFFSET = 0.05          # pane quad proud of the wall face (base_lodge.py:596)
ROOF_PANE_OFFSET = 0.03     # roof-window pane above the slab top plane
RIDGE_EXT = 0.35            # slab run past the ridge (25b lesson, base_lodge.py:665-667)

BEVEL_PREFIXES = ("fascia_", "rake_", "ridge_", "step_", "chimcap_", "col_", "beam_",
                  "pier_", "entry_", "truss_", "plinthcap_")
HEAVY_BEVEL_PREFIXES = ("col_", "beam_", "truss_", "pier_")
LABEL_PREFIXES = ("emblem_", "sign_", "clock_", "skirack_")

LOD1_DELETE = ("tail_", "bracket_", "brace_", "snowguard_", "gutter_", "downspout_", "vent_",
               "sill_", "jamb_", "mull_", "rwin_", "door_", "post_", "rail_", "kick_",
               "trail_", "bar_", "lamp_", "flag_", "umbstand_", "umbpole_", "skirack_",
               "akja_",
               # window kit v2 (2026-10-17): the reveal liners and sash rings sit BEHIND the
               # wall face; only the LOD0 copy gets the wall holes (punch_openings), so the
               # far set (LOD1 / LOD2) drops them. No v1 part carries either prefix.
               "reveal_", "sash_",
               # fittings v1 (2026-10-17): the modelled fittings (chimney hoods, ...) are LOD0 dressing;
               # no pre-fittings part carries the prefix
               "fit_",
               # detail pass 1 (school / L2 / patrol, design/detail-pass-1.md 6.1 + skeptic
               # M1): maze fence, toboggan rack, rescue gear, hose reel, fuel drum, log stack,
               # safety stripes. NOT bench_ (L1 keeps its bench through lod1_extra; a global
               # entry would cap its density weight and move L1's blockout hash) and no
               # festoon_ (string_lights parts are lamp_). No existing recipe part carries
               # one of these prefixes (M2 part lists, build/detail-pass-1-kit.md)
               "maze_", "tobrack_", "gear_", "hosereel_", "drum_", "wood_", "stripe_")
LOD2_DELETE = LOD1_DELETE + ("dormerroof_", "dormer_", "ridge_", "fascia_", "rake_",
                             "parapet_", "chimcap_", "stovepipe_", "head_", "cmull_", "ctran_",
                             "entry_", "deckfascia_", "balc_", "glassrail_", "plinthcap_",
                             "beam_", "truss_", "gazebocol_", "lanterncol_", "bell_",
                             "clockring_", "flagpanel_", "umbcanopy_", "trim_", "sign_",
                             "emblem_",
                             # detail pass 1: radio masts are kept to LOD1 (a 0.08 m pole is
                             # < 1 px at the LOD1 switch) and go at LOD2
                             "mast_")

WIN_POOL = tuple(range(1, 24))      # AV2 section 3.8 "1-23" (why 0 / 24 are skipped: unverified)
NEIGHBOUR_M = 3.0                   # Slice.ConservativeMerge 3 m (Room.Merge 2 m) [PG-sk 9]
SOFT_GAP_M = 1.5                    # vertically adjacent rooms: prefer different ids
MAX_ROOM_IDS = 25

# DetailSpec presets (cs2_compose has no batten / standing-seam kind, section 7.5 / N7;
# "ribs" draws lines of constant s_h = vertical on walls, down the slope on roofs. A
# NEGATIVE depth would be a raised rib, but the "ribs" branch combines with np.minimum
# (cs2_compose.py detail_height), which drops positive heights - grooves only, measured
# by reading the code 2026-10-11).
STANDING_SEAM = cs2_compose.DetailSpec(seams_m=0.50, seam_axes="ribs", seam_depth_m=0.006,
                                       seam_width_m=0.03)
BATTEN = cs2_compose.DetailSpec(seams_m=0.30, seam_axes="ribs", seam_depth_m=0.010,
                                seam_width_m=0.06)

# Section 7.1. tile_m in WORLD metres (after uv_rot); target_srgb encoded.
BLDG_MATERIALS = {
    "timber_lap": BldgMat("WoodSiding009", (2.4, 1.2), (0.295, 0.235, 0.185)),
    "timber_shake": BldgMat("WoodSiding013", (1.8, 1.8), None),
    "timber_vboard": BldgMat("WoodSiding009", (1.2, 2.4), (0.282, 0.251, 0.208),
                             detail=BATTEN, uv_rot=90),
    "siding_paint": BldgMat("WoodSiding009", (1.2, 2.4), None, smooth=0.45, slot=1,
                            recolor=True, detail=BATTEN, uv_rot=90),
    "deck": BldgMat("Planks021", (1.54, 1.54), (0.48, 0.42, 0.33), density_w=0.75),
    "timber_heavy": BldgMat("Planks021", (1.54, 1.54), (0.30, 0.22, 0.16), proj="along"),
    "stone_rubble": BldgMat("Bricks096", (3.2, 1.6), (0.455, 0.435, 0.400), smooth=0.22),
    "rock": BldgMat("Rock030", (2.5, 2.5), None),
    "slate": BldgMat("RoofingTiles003", (3.2, 3.2), None, density_w=0.8, proj="inplane"),
    "plaster": BldgMat("Plaster002", (2.0, 2.0), None),
    "concrete": BldgMat("Concrete034", (3.0, 1.5), None, tint=0.58),
    "galv": BldgMat("Metal032", (2.0, 2.0), None, smooth=0.35, metal=1.0, density_w=0.5),
    "metal_roof": BldgMat(None, None, (0.80, 0.80, 0.80), smooth=0.45, metal=0.1, slot=3,
                          recolor=True, detail=STANDING_SEAM, density_w=0.6, proj="inplane"),
    "metal_roof_s1": BldgMat(None, None, (0.80, 0.80, 0.80), smooth=0.45, metal=0.1, slot=1,
                             recolor=True, detail=STANDING_SEAM, density_w=0.6,
                             proj="inplane"),
    "trim": BldgMat(None, None, (0.80, 0.80, 0.80), smooth=0.45, slot=1, recolor=True),
    "trim_white": BldgMat(None, None, (0.85, 0.85, 0.85), smooth=0.40),
    # window kit v2 (2026-10-17): the painted-trim pair. trim_w = the slot-1 trim's weathered
    # sill (same recolour slot, darker base: dirt reads in whatever colour the variation
    # picks); trim_paint / trim_paint_w = a fixed warm off-white for buildings that want
    # light frames whatever their slot 1 is (the school: was trim_white, a pure 0.85 grey)
    "trim_w": BldgMat(None, None, (0.62, 0.61, 0.59), smooth=0.35, slot=1, recolor=True),
    "trim_paint": BldgMat(None, None, (0.80, 0.78, 0.72), smooth=0.40),
    "trim_paint_w": BldgMat(None, None, (0.66, 0.63, 0.57), smooth=0.35),
    "accent": BldgMat(None, None, (0.80, 0.80, 0.80), smooth=0.25, slot=2, recolor=True,
                      density_w=0.5),
    "glass_dark": BldgMat(None, None, (0.07, 0.09, 0.11), smooth=0.92, density_w=0.5),
    "label": BldgMat(None, None, (0.85, 0.85, 0.85), smooth=0.40, density_w=3.0),
    "lamp": BldgMat(None, None, (0.60, 0.58, 0.52), smooth=0.50,
                    emissive_srgb=(0.95, 0.85, 0.65)),
    # detail pass 1 (L1 ski gear, 2026-10-11): fixed paint colours beside accent (slot 2)
    # and trim_white; ski bases, bindings and pole baskets are ski_black
    "ski_black": BldgMat(None, None, (0.06, 0.06, 0.065), smooth=0.55, density_w=0.5),
    "ski_blue": BldgMat(None, None, (0.09, 0.26, 0.64), smooth=0.55, density_w=0.5),
    "ski_red": BldgMat(None, None, (0.72, 0.09, 0.08), smooth=0.55, density_w=0.5),
    # detail pass 1 (school / L2 / patrol, design/detail-pass-1.md 6.1): magic-carpet belt
    # rubber (was glass_dark, smooth 0.92: read as glass), safety stripes + backboards,
    # fire-red reels / drums, the red beacon (emissive: lit at night, LOD0 only engine-side)
    "rubber": BldgMat(None, None, (0.06, 0.06, 0.06), smooth=0.15, density_w=0.5),
    "safety_yellow": BldgMat(None, None, (0.90, 0.70, 0.08), smooth=0.35, density_w=0.5),
    "fire_red": BldgMat(None, None, (0.70, 0.08, 0.06), smooth=0.40),
    "lamp_red": BldgMat(None, None, (0.55, 0.10, 0.08), smooth=0.50,
                        emissive_srgb=(0.95, 0.12, 0.08)),
}

# Weathering roles (cs2_weather role names) of the kit keys cs2_weather.MAT_ROLES does not
# list (cs2_weather never imports the kit; lodge.wrap_weather merges the two tables and
# raises on a key with no role). Ski gear is under one texel wide at building densities
# (detail1-check-aframe F4): left unweathered.
WEATHER_ROLES_EXTRA = {"rubber": "pad", "safety_yellow": "paint", "fire_red": "paint",
                       "lamp_red": "none", "ski_black": "none", "ski_blue": "none",
                       "ski_red": "none", "trim_w": "paint", "trim_paint": "paint",
                       "trim_paint_w": "paint"}

# Exact-name weight overrides (section 7.3): first matching rule wins.
WEIGHT_RULES = (("suffix", "_face", 3.0), ("prefix", "entry_", 1.5), ("prefix", "door_", 1.5),
                ("prefix", "soffit_", 0.25))

_SIDES = {   # side -> (outward normal, u axis)
    "+X": (Vector((1, 0, 0)), Vector((0, 1, 0))),
    "-X": (Vector((-1, 0, 0)), Vector((0, -1, 0))),
    "+Y": (Vector((0, 1, 0)), Vector((-1, 0, 0))),
    "-Y": (Vector((0, -1, 0)), Vector((1, 0, 0))),
}
_KEY_OF = {(1, 0, 0): "+X", (-1, 0, 0): "-X", (0, 1, 0): "+Y", (0, -1, 0): "-Y",
           (0, 0, 1): "+Z", (0, 0, -1): "-Z"}


def _seed(name):
    return zlib.crc32(str(name).encode("utf-8"))


# ======================================================================================
# Geometry core
# ======================================================================================

class _Geo:
    """World-space bmesh accumulator for ONE part (several disconnected shells allowed)."""

    def __init__(self):
        self.bm = bmesh.new()

    def v(self, p):
        return self.bm.verts.new((float(p[0]), float(p[1]), float(p[2])))

    def face(self, verts, outward=None):
        f = self.bm.faces.new(verts)
        if outward is not None:
            f.normal_update()
            if f.normal.dot(Vector(outward)) < 0.0:
                f.normal_flip()
        return f

    def poly(self, pts, outward=None):
        return self.face([self.v(p) for p in pts], outward)

    def obb(self, c, axes, half, drop=(), net=None):
        """Oriented box: centre c, orthonormal axes (ax, ay, az), half sizes; drop = local
        face keys "+X".."-Z" (in the box's own axes) to omit. net (default: boxes whose
        longest edge <= NET_MAX_M with their +Z face present) records a cross-net UV
        (metres) on the loops, so retile() maps the whole box as ONE UV island instead of
        five or six (box projection splits every face; ~2,000 islands on L1 left the
        32 px pack margins no room, measured 2026-10-11)."""
        C = Vector(c)
        A = [Vector(a).normalized() for a in axes]
        h = [float(x) for x in half]
        if min(h) <= 0.0:
            raise ValueError(f"obb: non-positive half size {h}")
        if net is None:
            net = 2.0 * max(h) <= NET_MAX_M and "+Z" not in drop
        V = {}
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    V[(sx, sy, sz)] = self.v(C + A[0] * (sx * h[0]) + A[1] * (sy * h[1])
                                             + A[2] * (sz * h[2]))
        hand = 1.0 if A[0].cross(A[1]).dot(A[2]) > 0.0 else -1.0   # left-handed frames
        if net:                                                     # mirror u back
            lu = self.bm.loops.layers.float.get("netu") or self.bm.loops.layers.float.new("netu")
            lv = self.bm.loops.layers.float.get("netv") or self.bm.loops.layers.float.new("netv")
            lf = self.bm.faces.layers.int.get("netf") or self.bm.faces.layers.int.new("netf")
        names = ("X", "Y", "Z")
        for i in range(3):
            j, k = [a for a in range(3) if a != i]
            for s in (1, -1):
                key = ("+" if s > 0 else "-") + names[i]
                if key in drop:
                    continue
                cyc, idxs = [], []
                for (a, b) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                    idx = [0, 0, 0]
                    idx[i], idx[j], idx[k] = s, a, b
                    cyc.append(V[tuple(idx)])
                    idxs.append(tuple(idx))
                f = self.face(cyc, A[i] * s)
                if net:
                    f[lf] = 1
                    for loop in f.loops:
                        sid = idxs[cyc.index(loop.vert)]
                        u, v = _net_uv(key, sid, h)
                        loop[lu] = u * hand
                        loop[lv] = v
        return V

    def aabb(self, lo, hi, drop=()):
        lo, hi = Vector(lo), Vector(hi)
        c = (lo + hi) * 0.5
        h = (hi - lo) * 0.5
        return self.obb(c, ((1, 0, 0), (0, 1, 0), (0, 0, 1)), (h.x, h.y, h.z), drop)

    def cyl(self, p0, p1, r, segs=8, cap0=True, cap1=True, r1=None, phase=0.0):
        """Cylinder / cone p0 -> p1, triangle-fan caps (n - 2 tris each)."""
        P0, P1 = Vector(p0), Vector(p1)
        ax = (P1 - P0)
        L = ax.length
        if L <= 1e-9:
            raise ValueError("cyl: zero length")
        ax /= L
        ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
        t = ax.cross(ref).normalized()
        b = ax.cross(t).normalized()
        rr1 = r if r1 is None else r1
        ring0, ring1 = [], []
        for k in range(segs):
            a = phase + 2.0 * math.pi * k / segs
            d = t * math.cos(a) + b * math.sin(a)
            ring0.append(self.v(P0 + d * r))
            ring1.append(self.v(P1 + d * rr1))
        for k in range(segs):
            k1 = (k + 1) % segs
            mid = (ring0[k].co + ring0[k1].co + ring1[k].co + ring1[k1].co) * 0.25
            out = mid - (P0 + ax * ((mid - P0).dot(ax)))
            if rr1 < 1e-6:
                self.face([ring0[k], ring0[k1], ring1[k]], out)
            else:
                self.face([ring0[k], ring0[k1], ring1[k1], ring1[k]], out)
        if cap0:
            for k in range(1, segs - 1):
                self.face([ring0[0], ring0[k], ring0[k + 1]], -ax)
        if cap1 and rr1 > 1e-6:
            for k in range(1, segs - 1):
                self.face([ring1[0], ring1[k], ring1[k + 1]], ax)

    def prism(self, base_pts, extrude, caps=(True, True), outward_hint=None):
        """Extrude a planar polygon (world points) by vector `extrude`; side quads face
        away from the polygon centroid, caps along -/+ extrude."""
        E = Vector(extrude)
        B = [Vector(p) for p in base_pts]
        T = [p + E for p in B]
        vb = [self.v(p) for p in B]
        vt = [self.v(p) for p in T]
        cen = sum(B, Vector()) / len(B)
        n = len(B)
        for k in range(n):
            k1 = (k + 1) % n
            mid = (B[k] + B[k1]) * 0.5
            self.face([vb[k], vb[k1], vt[k1], vt[k]], mid - cen)
        if caps[0]:
            self.face(list(vb), -E)
        if caps[1]:
            self.face(list(vt), E)

    def finish(self, name, relief="geo"):
        return _make_obj(name, self.bm, relief)


NET_MAX_M = 2.5         # boxes up to this long get a one-island cross-net UV


def _net_uv(key, sid, h):
    """Cross net of a box in metres, seen from outside (orientation preserving: every
    face keeps a positive UV winding w.r.t. its outward normal). Top (+Z) in the centre,
    +Y / -Y / +X / -X folded out of its edges, bottom (-Z) beyond +Y."""
    hx, hy, hz = h
    x, y, z = sid[0] * hx, sid[1] * hy, sid[2] * hz
    if key == "+Z":
        return x, y
    if key == "+Y":
        return x, hy + (hz - z)
    if key == "-Y":
        return x, -hy - (hz - z)
    if key == "+X":
        return hx + (hz - z), y
    if key == "-X":
        return -hx - (hz - z), y
    return x, hy + 2.0 * hz + (hy - y)      # "-Z"


def _make_obj(name, bm, relief="geo"):
    if name in bpy.data.objects:
        bm.free()
        raise ValueError(f"duplicate part name {name!r} (Blender would rename it .001 and "
                         f"break prefix matching)")
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    bm.normal_update()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    if obj.name != name:
        raise ValueError(f"part {name!r} was renamed {obj.name!r}")
    obj["relief"] = relief
    return obj


def _single(name, fn, mat, relief="geo"):
    g = _Geo()
    fn(g)
    return [(g.finish(name, relief), mat)]


def wall_point(wall, u, z, d=0.0):
    """World point on/in front of a wall: u along the wall, z height, d outward offset."""
    n, t = _SIDES[wall.side]
    if wall.side in ("+X", "-X"):
        base = Vector((wall.plane, 0.0, 0.0))
    else:
        base = Vector((0.0, wall.plane, 0.0))
    return base + n * d + t * u + Vector((0, 0, z))


def _wall_aabb(wall, u0, u1, z0, z1, d0, d1):
    a = wall_point(wall, u0, z0, d0)
    b = wall_point(wall, u1, z1, d1)
    lo = Vector((min(a.x, b.x), min(a.y, b.y), min(a.z, b.z)))
    hi = Vector((max(a.x, b.x), max(a.y, b.y), max(a.z, b.z)))
    return lo, hi


def _wall_keys(wall, local):
    """Wall-local face keys (front/back/top/bottom/left/right) -> world keys."""
    n, t = _SIDES[wall.side]
    m = {"front": n, "back": -n, "top": Vector((0, 0, 1)), "bottom": Vector((0, 0, -1)),
         "right": t, "left": -t}
    out = []
    for k in local:
        v = m[k]
        out.append(_KEY_OF[(int(round(v.x)), int(round(v.y)), int(round(v.z)))])
    return tuple(out)


def wall_box(name, wall, u0, u1, z0, z1, d0, d1, mat, drop=("back",)):
    """Axis-aligned box in wall-local terms (u0..u1 along, z0..z1, d0..d1 outward)."""
    lo, hi = _wall_aabb(wall, min(u0, u1), max(u0, u1), z0, z1, d0, d1)
    keys = _wall_keys(wall, drop)
    return _single(name, lambda g: g.aabb(lo, hi, keys), mat)


def pane_quad(name, wall, u0, u1, z0, z1, d=PANE_OFFSET, mat="glass_dark"):
    """ONE quad facing the wall's outward normal (a `_Win` pane: no box, no top/bottom)."""
    n, _t = _SIDES[wall.side]
    pts = [wall_point(wall, u0, z0, d), wall_point(wall, u1, z0, d),
           wall_point(wall, u1, z1, d), wall_point(wall, u0, z1, d)]
    return _single(name, lambda g: g.poly(pts, n), mat)


def wall_boxes(name, wall, boxes, mat, drop=("back",)):
    """Several wall-local boxes (u0, u1, z0, z1, d0, d1) as ONE part (mullion sets, frames)."""
    g = _Geo()
    keys = _wall_keys(wall, drop)
    for (u0, u1, z0, z1, d0, d1) in boxes:
        lo, hi = _wall_aabb(wall, min(u0, u1), max(u0, u1), z0, z1, d0, d1)
        g.aabb(lo, hi, keys)
    return [(g.finish(name), mat)]


def boxes_part(name, boxes, mat):
    """Several world AABBs ((lo, hi, drop), ...) as ONE part (tails, corner boards)."""
    g = _Geo()
    for b in boxes:
        lo, hi = b[0], b[1]
        g.aabb(lo, hi, b[2] if len(b) > 2 else ())
    return [(g.finish(name), mat)]


def bisect_part(obj, planes):
    """Cut a part with planes ((co, no), ...) - topology only (cull / soffit splits)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for co, no in planes:
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-5,
                               plane_co=co, plane_no=no)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return obj


def box_part(name, lo, hi, mat, drop=(), ground=False):
    """Generic axis-aligned box part (world keys in `drop`; ground -> open bottom at z 0)."""
    keys = tuple(drop) + (("-Z",) if ground else ())
    if ground and abs(lo[2]) > 1e-9:
        raise ValueError(f"{name}: a ground part must start at z 0 (got {lo[2]})")
    return _single(name, lambda g: g.aabb(lo, hi, keys), mat)


def _lift(z):
    """Closed parts never start inside the ground band."""
    return GROUND_LIFT if z < GROUND_LIFT else z


def _rename(obj, new):
    """Rename a part; raises instead of letting Blender append .001."""
    if new != obj.name and new in bpy.data.objects:
        raise ValueError(f"duplicate part name {new!r}")
    obj.name = new
    obj.data.name = new
    if obj.name != new:
        raise ValueError(f"part {new!r} was renamed {obj.name!r}")
    return obj


# ======================================================================================
# Roof planes
# ======================================================================================

def _roof_geometry(spec):
    """Derived numbers of a pitched RoofSpec (axis, wall range e0..e1, ridge, pitches)."""
    ax = spec.axis.upper()
    if ax not in ("X", "Y"):
        raise ValueError(f"RoofSpec.axis {spec.axis!r}")
    if ax == "Y":
        e0, e1, a0, a1 = spec.x0, spec.x1, spec.y0, spec.y1
    else:
        e0, e1, a0, a1 = spec.y0, spec.y1, spec.x0, spec.x1
    t0 = math.tan(math.radians(spec.pitch_deg))
    r = spec.ridge_at if spec.ridge_at is not None else 0.5 * (e0 + e1)
    if spec.kind == "shed":
        return dict(ax=ax, e0=e0, e1=e1, a0=a0, a1=a1, t0=t0, t1=t0, r=r,
                    ridge_z=None, z0=spec.eave_z, z1=None)
    ridge_z = spec.eave_z + t0 * (r - e0)
    if spec.eave_z1 is None:
        z1 = ridge_z - t0 * (e1 - r)
        t1 = t0
    else:
        z1 = float(spec.eave_z1)
        t1 = (ridge_z - z1) / max(e1 - r, 1e-6)
    return dict(ax=ax, e0=e0, e1=e1, a0=a0, a1=a1, t0=t0, t1=t1, r=r, ridge_z=ridge_z,
                z0=spec.eave_z, z1=z1)


def _centre_z(spec, e, side=None):
    """Centre-plane height at across-coordinate e (side 0 = the e0 slope)."""
    g = _roof_geometry(spec)
    if spec.kind == "shed":
        return g["z0"] + g["t0"] * (e - g["e0"])
    if side is None:
        side = 0 if e <= g["r"] else 1
    if side == 0:
        return g["z0"] + g["t0"] * (e - g["e0"])
    return g["z1"] + g["t1"] * (g["e1"] - e)


def roof_z(spec, x, y):
    """Top-plane height of `spec` at (x, y) (dormers, chimneys, vents, roof windows)."""
    g = _roof_geometry(spec)
    e = x if g["ax"] == "Y" else y
    if spec.kind == "hip":
        a = y if g["ax"] == "Y" else x
        half_w = 0.5 * (g["e1"] - g["e0"])
        ca = 0.5 * (g["a0"] + g["a1"])
        half_l = 0.5 * (g["a1"] - g["a0"])
        d_e = half_w - abs(e - 0.5 * (g["e0"] + g["e1"]))
        d_a = half_l - abs(a - ca)
        zc = spec.eave_z + g["t0"] * min(d_e, d_a)
        return zc + 0.5 * spec.thick / math.cos(math.radians(spec.pitch_deg))
    if spec.kind == "flat":
        return spec.eave_z
    side = 0 if (spec.kind == "shed" or e <= g["r"]) else 1
    t = g["t0"] if side == 0 else g["t1"]
    return _centre_z(spec, e, side) + 0.5 * spec.thick * math.sqrt(1.0 + t * t)


def roof_frame(spec, side):
    """(normal, up-slope, along-eave) unit vectors of one slope. side: "-X"/"+X" for a
    Y-ridge roof, "-Y"/"+Y" for an X-ridge roof (the slope that FACES that direction)."""
    g = _roof_geometry(spec)
    t = g["t0"] if side.startswith("-") else g["t1"]
    p = math.atan(t)
    s = 1.0 if side.startswith("-") else -1.0          # up-slope direction across
    if g["ax"] == "Y":
        n = Vector((-s * math.sin(p), 0.0, math.cos(p)))
        up = Vector((s * math.cos(p), 0.0, math.sin(p)))
        along = Vector((0.0, 1.0, 0.0))
    else:
        n = Vector((0.0, -s * math.sin(p), math.cos(p)))
        up = Vector((0.0, s * math.cos(p), math.sin(p)))
        along = Vector((1.0, 0.0, 0.0))
    return n, up, along


def chimney_top(spec, x, y, w, d):
    """Max top-plane z over the stack footprint + 0.9 m, and >= ridge + 0.6 within 3 m of
    the ridge ([K] NFPA 211 "3-2-10", unverified)."""
    zs = [roof_z(spec, x + sx * w / 2, y + sy * d / 2) for sx in (-1, 1) for sy in (-1, 1)]
    top = max(zs) + 0.9
    g = _roof_geometry(spec)
    if g["ridge_z"] is not None:
        e = x if g["ax"] == "Y" else y
        if abs(e - g["r"]) <= 3.0 + 0.5 * (w if g["ax"] == "Y" else d):
            ridge_top = g["ridge_z"] + 0.5 * spec.thick / math.cos(math.radians(spec.pitch_deg))
            top = max(top, ridge_top + 0.6)
    return top


# ======================================================================================
# Massing (section 4.2)
# ======================================================================================

def storey(name, x0, x1, y0, y1, z0, z1, mat, ground=False, drop=(), cut_z=()):
    """`wall_<name>` box; ground -> open bottom (z0 must be 0); `drop` world face keys
    ("+Z" when roofed or stacked); `cut_z` horizontal bisect loops so cull_hidden can
    remove covered strips."""
    keys = tuple(drop) + (("-Z",) if ground else ())
    if ground and abs(z0) > 1e-9:
        raise ValueError(f"storey {name}: ground storeys start at z 0 (got {z0})")
    g = _Geo()
    g.aabb((x0, y0, z0), (x1, y1, z1), keys)
    for z in sorted(set(float(c) for c in cut_z)):
        if z0 + 1e-3 < z < z1 - 1e-3:
            bmesh.ops.bisect_plane(g.bm, geom=g.bm.verts[:] + g.bm.edges[:] + g.bm.faces[:],
                                   dist=1e-5, plane_co=(0.0, 0.0, z), plane_no=(0.0, 0.0, 1.0))
    return [(g.finish(f"wall_{name}"), mat)]


def plinth(name, x0, x1, y0, y1, depth, top, mat="stone_rubble", built_on=True):
    """`plinth_<name>` z -depth..top, open bottom, top dropped when built on. Plinth mode:
    no vertex in |z| <= 0.01 (asserted)."""
    if depth <= 0.011 or top <= 0.011:
        raise ValueError(f"plinth {name}: depth and top must clear the ground band")
    keys = ("-Z",) + (("+Z",) if built_on else ())
    return _single(f"plinth_{name}", lambda g: g.aabb((x0, y0, -depth), (x1, y1, top), keys),
                   mat)


def prism_gable(name, axis, a0, a1, e0, e1, eave_z, ridge_z, ridge_at=None, eave_z1=None,
                mat="timber_lap", base_z=None, bottom=False, inner=True):
    """`gable_<name>`: thin gable-end wall prism (port of base_lodge.add_gable_prism,
    :184-211) spanning a0..a1 along the ridge axis (its thickness) and e0..e1 across;
    `eave_z1` gives the e1 side its own eave height (saltbox); `base_z` turns the triangle
    into a pentagon down to base_z; the bottom face is omitted unless `bottom`.
    inner (detail pass 1, design 5.2 P2 repack): True keeps both end faces; "a0" / "a1"
    drops the end face at a0 (normal -axis) / a1 (normal +axis) - the one that looks INTO
    the building (the kit cannot tell which side is inside, so the caller names it)."""
    if inner not in (True, "a0", "a1"):
        raise ValueError(f"prism_gable {name}: inner {inner!r} (True | 'a0' | 'a1': name the "
                         f"end face that looks into the building)")
    ax = axis.upper()
    r = ridge_at if ridge_at is not None else 0.5 * (e0 + e1)
    z1 = eave_z if eave_z1 is None else eave_z1
    prof = [(e0, eave_z), (r, ridge_z), (e1, z1)]
    if base_z is not None:
        prof = [(e0, base_z)] + prof + [(e1, base_z)]

    def P(e, z, a):
        return (e, a, z) if ax == "Y" else (a, e, z)

    def build(g):
        n = len(prof)
        f0 = [g.v(P(e, z, a0)) for e, z in prof]
        f1 = [g.v(P(e, z, a1)) for e, z in prof]
        out0 = Vector((0, -1, 0)) if ax == "Y" else Vector((-1, 0, 0))
        if inner != "a0":
            g.face(f0, out0)
        if inner != "a1":
            g.face(f1, -out0)
        cen = Vector((sum(e for e, _ in prof) / n, sum(z for _, z in prof) / n))
        for k in range(n):
            k1 = (k + 1) % n
            (ea, za), (eb, zb) = prof[k], prof[k1]
            closing = (k1 == 0)
            if closing and not bottom:
                continue
            mid = Vector(((ea + eb) / 2 - cen.x, (za + zb) / 2 - cen.y))
            out = P(mid.x, mid.y, 0.0)
            g.face([f0[k], f0[k1], f1[k1], f1[k]], out)
    return _single(f"gable_{name}", build, mat)


def _slab_geo(g, axis, a0, a1, e_edge, e_z, r_edge, r_z, thick, wall_line=None):
    """Tilted slab between the eave point and the ridge point (base_lodge.add_roof_slab
    analytic port). Returns the soffit corner loops when `wall_line` splits the underside:
    (soffit_pts) for a separate part, or None."""
    ax = axis.upper()
    E = Vector((e_edge, e_z))
    R = Vector((r_edge, r_z))
    d = (R - E).normalized()
    n = Vector((-d.y, d.x)) if d.x > 0 else Vector((d.y, -d.x))
    h = 0.5 * thick
    Et, Eb, Rt, Rb = E + n * h, E - n * h, R + n * h, R - n * h

    def P(p, a):
        return (p.x, a, p.y) if ax == "Y" else (a, p.x, p.y)
    up3 = Vector(P(n, 0.0))
    dd3 = Vector(P(d, 0.0))
    aout = Vector((0, 1, 0)) if ax == "Y" else Vector((1, 0, 0))
    split = None
    if wall_line is not None:
        lo, hi = sorted((Eb.x, Rb.x))
        if lo + 1e-4 < wall_line < hi - 1e-4:
            s = (wall_line - Eb.x) / (Rb.x - Eb.x)
            split = Eb + (Rb - Eb) * s
    V = {}
    for tag, p in (("Et", Et), ("Eb", Eb), ("Rt", Rt), ("Rb", Rb)):
        V[tag + "0"] = g.v(P(p, a0))
        V[tag + "1"] = g.v(P(p, a1))
    if split is not None:
        V["W0"] = g.v(P(split, a0))
        V["W1"] = g.v(P(split, a1))
    g.face([V["Et0"], V["Rt0"], V["Rt1"], V["Et1"]], up3)                 # top
    g.face([V["Et0"], V["Eb0"], V["Eb1"], V["Et1"]], -dd3)                # eave end
    g.face([V["Rt0"], V["Rb0"], V["Rb1"], V["Rt1"]], dd3)                 # ridge end
    if split is None:
        g.face([V["Eb0"], V["Rb0"], V["Rb1"], V["Eb1"]], -up3)            # underside
        for s, tag in ((-1, "0"), (1, "1")):
            g.face([V["Et" + tag], V["Rt" + tag], V["Rb" + tag], V["Eb" + tag]], aout * s)
        return None
    g.face([V["W0"], V["Rb0"], V["Rb1"], V["W1"]], -up3)                  # inner underside
    for s, tag in ((-1, "0"), (1, "1")):
        g.face([V["Et" + tag], V["Rt" + tag], V["Rb" + tag], V["W" + tag]], aout * s)
        g.face([V["Et" + tag], V["W" + tag], V["Eb" + tag]], aout * s)
    return [P(Eb, a0), P(split, a0), P(split, a1), P(Eb, a1)], -up3


def roof_slab(name, axis, a0, a1, e_edge, e_z, r_edge, r_z, thick=0.24, wall_line=None,
              mat="slate", prefix="roof_"):
    """`roof_<name>` slab from the eave line (e_edge, e_z) to the ridge line (r_edge, r_z),
    spanning a0..a1 along `axis` (the ridge direction); `wall_line` splits the underside
    into `soffit_<name>` (the visible overhang, its own part, w 0.25) and the inner
    underside (attic, culled). `prefix` lets rakes reuse it (`rake_`)."""
    g = _Geo()
    res = _slab_geo(g, axis, a0, a1, e_edge, e_z, r_edge, r_z, thick, wall_line)
    parts = [(g.finish(f"{prefix}{name}"), mat)]
    if res is not None:
        pts, out = res
        parts += _single(f"soffit_{name}", lambda gg: gg.poly(pts, out), "timber_lap")
    return parts


def _ridge_cap_parts(name, spec, g, w, h, a_lo, a_hi, mat="galv"):
    ext = RIDGE_EXT
    tmax = max(g["t0"], g["t1"])
    p = math.atan(tmax)
    cross = g["ridge_z"] + 0.5 * spec.thick / math.cos(p)
    tip = g["ridge_z"] + ext * tmax + 0.5 * spec.thick * math.cos(p)
    zb = cross - 0.03
    zt = max(zb + h, tip + 0.02)
    hw = max(0.5 * w, ext + 0.05)
    if g["ax"] == "Y":
        lo, hi = (g["r"] - hw, a_lo, zb), (g["r"] + hw, a_hi, zt)
    else:
        lo, hi = (a_lo, g["r"] - hw, zb), (a_hi, g["r"] + hw, zt)
    return box_part(f"ridge_{name}", lo, hi, mat)


def _eave_tip(spec, g, side):
    ov = spec.overhang
    if side == 0:
        return g["e0"] - ov, g["z0"] - g["t0"] * ov
    return g["e1"] + ov, g["z1"] - g["t1"] * ov


def gable_roof(name, spec, fascia=0.42, rake=0.40, ridge_cap=(0.45, 0.14), tails=None,
               snow_guard=0, gutters=False, walls=True, wall_mat=None, ext=RIDGE_EXT,
               wall_inner=True):
    """Two slabs (ridge-end extension along the pitch, 25b lesson), ridge cap, fascia,
    rake boards, optional rafter tails `tail_` (spacing `tails` m, 0.62 x 0.12 x 0.16),
    snow-guard rows, gutters; `walls` adds the two `gable_` end walls (0.3 m prisms);
    wall_inner=False (detail pass 1, design 3.2 H11) drops their faces that look into the
    building (2 triangles / pentagons; no texels spent on interior faces)."""
    g = _roof_geometry(spec)
    ro = spec.overhang if spec.rake_over is None else spec.rake_over
    a_lo, a_hi = g["a0"] - ro, g["a1"] + ro
    parts = []
    for side, tag in ((0, "A"), (1, "B")):
        et, zt = _eave_tip(spec, g, side)
        t = g["t0"] if side == 0 else g["t1"]
        sgn = 1.0 if side == 0 else -1.0
        r_edge = g["r"] + sgn * ext
        r_z = g["ridge_z"] + ext * t
        wl = g["e0"] if side == 0 else g["e1"]
        parts += roof_slab(f"{name}{tag}", g["ax"], a_lo, a_hi, et, zt, r_edge, r_z,
                           spec.thick, wall_line=wl, mat=spec.mat)
        # rake boards at both gable ends (slot-1 trim), tracking the slab top
        for aend, atag in ((a_lo - 0.06, "0"), (a_hi + 0.06, "1")):
            parts += roof_slab(f"{name}{tag}{atag}", g["ax"], aend - 0.06, aend + 0.06,
                               et, zt - 0.05, g["r"], g["ridge_z"] - 0.02, thick=rake,
                               mat=spec.trim, prefix="rake_")
        # fascia at the eave tip
        if fascia:
            fz = zt - 0.06
            off = -sgn * 0.07
            if g["ax"] == "Y":
                lo = (et + off - 0.06, a_lo - 0.06, fz - fascia / 2)
                hi = (et + off + 0.06, a_hi + 0.06, fz + fascia / 2)
            else:
                lo = (a_lo - 0.06, et + off - 0.06, fz - fascia / 2)
                hi = (a_hi + 0.06, et + off + 0.06, fz + fascia / 2)
            parts += box_part(f"fascia_{name}{tag}", lo, hi, spec.trim)
        if tails:
            n_t = max(2, int(round((g["a1"] - g["a0"]) / float(tails))) + 1)
            gg = _Geo()
            tz = _centre_z(spec, wl, side) - 0.5 * spec.thick - 0.10
            for k in range(n_t):
                a = g["a0"] + (g["a1"] - g["a0"]) * k / (n_t - 1)
                ec = wl - sgn * 0.31
                if g["ax"] == "Y":
                    gg.aabb((ec - 0.31, a - 0.06, tz - 0.08), (ec + 0.31, a + 0.06, tz + 0.08))
                else:
                    gg.aabb((a - 0.06, ec - 0.31, tz - 0.08), (a + 0.06, ec + 0.31, tz + 0.08))
            parts.append((gg.finish(f"tail_{name}{tag}"), "timber_heavy"))
        side_key = ("-X" if side == 0 else "+X") if g["ax"] == "Y" else \
                   ("-Y" if side == 0 else "+Y")
        for k in range(int(snow_guard)):
            parts += globals()["snow_guard"](f"{name}{tag}{k}", spec, side_key,
                                             offset=0.6 + 0.9 * k)
        if gutters:
            gz = zt - 0.18
            off = -sgn * 0.20
            if g["ax"] == "Y":
                p0, p1 = (et + off, a_lo), (et + off, a_hi)
            else:
                p0, p1 = (a_lo, et + off), (a_hi, et + off)
            parts += gutter(f"{name}{tag}", p0, p1, gz)
    if ridge_cap:
        parts += _ridge_cap_parts(name, spec, g, ridge_cap[0], ridge_cap[1], a_lo, a_hi)
    if walls:
        wm = wall_mat or "timber_lap"
        for aw, atag in ((g["a0"], "0"), (g["a1"], "1")):
            a_in = aw + (0.3 if atag == "0" else -0.3)
            # the end at a_in looks into the building: the prism's a1 end for wall "0"
            # (a0 .. a0 + 0.3), its a0 end for wall "1" (a1 - 0.3 .. a1)
            inner = True if wall_inner else ("a1" if atag == "0" else "a0")
            parts += prism_gable(f"{name}{atag}", g["ax"], min(aw, a_in), max(aw, a_in),
                                 g["e0"], g["e1"], g["z0"], g["ridge_z"] - 0.02,
                                 ridge_at=g["r"], eave_z1=g["z1"], mat=wm, inner=inner)
    return parts


def hip_roof(name, spec, fascia=0.42, ridge_cap=(0.45, 0.14), snow_guard=0, gutters=False):
    """Four planes over the wall rectangle (+ overhang), eave band, flat soffit ring
    `soffit_`, ridge + hip caps `ridge_`. `spec.axis` = ridge direction (long side).
    `snow_guard` is accepted for the contract signature but not built on hip roofs (no
    recipe uses it; snow_guard() needs a gable slope frame)."""
    ov = spec.overhang
    t = math.tan(math.radians(spec.pitch_deg))
    X0, X1, Y0, Y1 = spec.x0 - ov, spec.x1 + ov, spec.y0 - ov, spec.y1 + ov
    zt = spec.eave_z - t * ov                     # centre plane at the eave tip
    hz = 0.5 * spec.thick * math.sqrt(1 + t * t)
    ztop_e = zt + hz
    hw = 0.5 * min(X1 - X0, Y1 - Y0)
    zr = ztop_e + t * hw
    cx, cy = 0.5 * (X0 + X1), 0.5 * (Y0 + Y1)
    if spec.axis.upper() == "Y":
        L = 0.5 * (Y1 - Y0) - hw
        r0, r1 = (cx, cy - L, zr), (cx, cy + L, zr)
    else:
        L = 0.5 * (X1 - X0) - hw
        r0, r1 = (cx - L, cy, zr), (cx + L, cy, zr)
    c = [(X0, Y0, ztop_e), (X1, Y0, ztop_e), (X1, Y1, ztop_e), (X0, Y1, ztop_e)]
    zb = ztop_e - spec.thick

    def build(g):
        V = [g.v(p) for p in c]
        B = [g.v((p[0], p[1], zb)) for p in c]
        R0 = g.v(r0)
        R1 = g.v(r1) if L > 1e-6 else R0
        top_c = Vector((cx, cy, zr))
        # planes: pick ridge end per corner by proximity
        if spec.axis.upper() == "Y":
            quads = [(0, 1, R0, R0), (1, 2, R1, R0), (2, 3, R1, R1), (3, 0, R0, R1)]
        else:
            quads = [(0, 1, R1, R0), (1, 2, R1, R1), (2, 3, R0, R1), (3, 0, R0, R0)]
        for i, j, ra, rb in quads:
            vs = [V[i], V[j], ra] if ra is rb else [V[i], V[j], ra, rb]
            cen = sum((v.co for v in vs), Vector()) / len(vs)
            g.face(vs, (cen - top_c) + Vector((0, 0, 2.0)))
        for i in range(4):
            j = (i + 1) % 4
            mid = (Vector(c[i]) + Vector(c[j])) * 0.5
            g.face([V[i], V[j], B[j], B[i]], Vector((mid.x - cx, mid.y - cy, 0)))
    parts = [(_geo_finish(f"roof_{name}", build), spec.mat)]
    # soffit ring under the overhang (flat, at the band bottom)
    ring = _Geo()
    inner = [(spec.x0, spec.y0), (spec.x1, spec.y0), (spec.x1, spec.y1), (spec.x0, spec.y1)]
    outer = [(X0, Y0), (X1, Y0), (X1, Y1), (X0, Y1)]
    for i in range(4):
        j = (i + 1) % 4
        ring.poly([(outer[i][0], outer[i][1], zb), (inner[i][0], inner[i][1], zb),
                   (inner[j][0], inner[j][1], zb), (outer[j][0], outer[j][1], zb)],
                  (0, 0, -1))
    parts.append((ring.finish(f"soffit_{name}"), "timber_lap"))
    if fascia:
        fg = _Geo()
        for i in range(4):
            j = (i + 1) % 4
            p, q = Vector(outer[i]), Vector(outer[j])
            dvec = (q - p).normalized()
            nrm = Vector((dvec.y, -dvec.x))
            if nrm.dot(Vector(((p.x + q.x) / 2 - cx, (p.y + q.y) / 2 - cy))) < 0:
                nrm = -nrm
            mid = (p + q) * 0.5 + nrm * 0.03
            fg.obb((mid.x, mid.y, ztop_e - 0.5 * fascia + 0.02),
                   ((dvec.x, dvec.y, 0), (nrm.x, nrm.y, 0), (0, 0, 1)),
                   (0.5 * (q - p).length + 0.03, 0.06, 0.5 * fascia))
        parts.append((fg.finish(f"fascia_{name}"), spec.trim))
    if ridge_cap:
        cg = _Geo()
        w, h = ridge_cap
        if L > 1e-6:
            _beam_into(cg, Vector(r0) + Vector((0, 0, 0.03)), Vector(r1) + Vector((0, 0, 0.03)),
                       w, h)
        corner_to_ridge = {0: r0, 1: r0, 2: r1, 3: r1} if spec.axis.upper() == "Y" else \
                          {0: r0, 1: r1, 2: r1, 3: r0}
        for i in range(4):
            a = Vector(c[i]) + Vector((0, 0, 0.03))
            b = Vector(corner_to_ridge[i]) + Vector((0, 0, 0.03))
            _beam_into(cg, a, b, 0.6 * w, h)
        parts.append((cg.finish(f"ridge_{name}"), "galv"))
    if gutters:
        gz = ztop_e - spec.thick - 0.06
        parts += gutter(f"{name}S", (X0, Y0 - 0.12), (X1, Y0 - 0.12), gz)
        parts += gutter(f"{name}N", (X0, Y1 + 0.12), (X1, Y1 + 0.12), gz)
    return parts


def _geo_finish(name, fn, relief="geo"):
    g = _Geo()
    fn(g)
    return g.finish(name, relief)


def _beam_into(g, p0, p1, w, h, up=Vector((0, 0, 1))):
    """OBB between two points into geometry g (width w horizontal-ish, height h)."""
    P0, P1 = Vector(p0), Vector(p1)
    ax = P1 - P0
    L = ax.length
    if L < 1e-6:
        return
    ax /= L
    side = ax.cross(up)
    if side.length < 1e-6:
        side = ax.cross(Vector((1, 0, 0)))
    side.normalize()
    upv = side.cross(ax).normalized()
    g.obb((P0 + P1) * 0.5, (ax, side, upv), (0.5 * L, 0.5 * w, 0.5 * h))


def shed_roof(name, spec, low_side="-X", fascia=0.42, brackets=0):
    """One slab rising from the `low_side` wall line (centre plane at spec.eave_z there,
    pitch spec.pitch_deg), side cheeks `gable_<name>c0/c1` between the walls' top and the
    slab, optional `bracket_` struts every `brackets` m under the low-side overhang."""
    t = math.tan(math.radians(spec.pitch_deg))
    ov = spec.overhang
    ro = ov if spec.rake_over is None else spec.rake_over
    if low_side in ("-X", "+X"):
        lo_e, hi_e = (spec.x0, spec.x1) if low_side == "-X" else (spec.x1, spec.x0)
        a0, a1, slab_axis = spec.y0, spec.y1, "Y"
    else:
        lo_e, hi_e = (spec.y0, spec.y1) if low_side == "-Y" else (spec.y1, spec.y0)
        a0, a1, slab_axis = spec.x0, spec.x1, "X"
    sgn = 1.0 if hi_e > lo_e else -1.0
    e_tip_lo = lo_e - sgn * ov
    z_tip_lo = spec.eave_z - t * ov
    e_tip_hi = hi_e + sgn * ov
    z_hi_wall = spec.eave_z + t * abs(hi_e - lo_e)
    z_tip_hi = z_hi_wall + t * ov
    parts = roof_slab(name, slab_axis, a0 - ro, a1 + ro, e_tip_lo, z_tip_lo, e_tip_hi,
                      z_tip_hi, spec.thick, wall_line=lo_e, mat=spec.mat)
    # cheeks: the wall-top band between eave_z at lo and the slab at hi (quadrilateral)
    wall_top = spec.eave_z
    for k, aw in enumerate((a0, a1)):
        a_in = aw + (0.25 if k == 0 else -0.25)
        parts += prism_gable(f"{name}c{k}", slab_axis, min(aw, a_in), max(aw, a_in),
                             lo_e, hi_e, wall_top - 0.3, z_hi_wall - 0.02, ridge_at=hi_e,
                             eave_z1=z_hi_wall - 0.02, base_z=wall_top - 0.3,
                             mat="timber_lap")
    if fascia:
        for tag, e, z in (("lo", e_tip_lo, z_tip_lo), ("hi", e_tip_hi, z_tip_hi)):
            off = -sgn * 0.07 if tag == "lo" else sgn * 0.07
            if slab_axis == "Y":
                lo = (e + off - 0.06, a0 - ro - 0.06, z - 0.06 - fascia / 2)
                hi = (e + off + 0.06, a1 + ro + 0.06, z - 0.06 + fascia / 2)
            else:
                lo = (a0 - ro - 0.06, e + off - 0.06, z - 0.06 - fascia / 2)
                hi = (a1 + ro + 0.06, e + off + 0.06, z - 0.06 + fascia / 2)
            parts += box_part(f"fascia_{name}{tag}", lo, hi, spec.trim)
    if brackets:
        n = max(2, int(round((a1 - a0) / float(brackets))) + 1)
        side = ("-X" if sgn > 0 else "+X") if slab_axis == "Y" else ("-Y" if sgn > 0 else "+Y")
        wall = WallRef(side, lo_e)
        nvec, tvec = _SIDES[side]
        for k in range(n):
            a = a0 + (a1 - a0) * k / (n - 1)
            u = a if side in ("+X", "-Y") else -a
            at = wall_point(wall, u, spec.eave_z - 0.5 * spec.thick - 0.1, 0.0)
            parts += bracket(f"{name}{k}", tuple(at), side, span=min(ov, 2.0),
                             drop=min(ov, 2.0), curved=False)
    return parts


def flat_roof(name, x0, x1, y0, y1, z, parapet=0.3, band=None, mat="metal_roof"):
    """`roof_<name>` deck (top at z) + `parapet_<name>` ring (0.2 thick, `parapet` tall);
    `band=(h, proud, mat)` adds a fascia band `trim_<name>band` around the top."""
    parts = box_part(f"roof_{name}", (x0, y0, z - 0.2), (x1, y1, z), mat, drop=("-Z",))
    if parapet:
        g = _Geo()
        t = 0.2
        zt = z + parapet
        g.aabb((x0, y0, z - 0.2), (x1, y0 + t, zt), ("-Z",))
        g.aabb((x0, y1 - t, z - 0.2), (x1, y1, zt), ("-Z",))
        g.aabb((x0, y0 + t, z - 0.2), (x0 + t, y1 - t, zt), ("-Z", "+Y", "-Y"))
        g.aabb((x1 - t, y0 + t, z - 0.2), (x1, y1 - t, zt), ("-Z", "+Y", "-Y"))
        parts.append((g.finish(f"parapet_{name}"), "trim"))
    if band:
        h, proud, bmat = band
        g = _Geo()
        zt = z + (parapet or 0.0)
        zb = zt - h
        g.aabb((x0 - proud, y0 - proud, zb), (x1 + proud, y0, zt))
        g.aabb((x0 - proud, y1, zb), (x1 + proud, y1 + proud, zt))
        g.aabb((x0 - proud, y0, zb), (x0, y1, zt), ("+Y", "-Y"))
        g.aabb((x1, y0, zb), (x1 + proud, y1, zt), ("+Y", "-Y"))
        parts.append((g.finish(f"trim_{name}band"), bmat))
    return parts


def dormer(name, spec, u, w=2.4, h=1.6, room=None, win=WinSpec(1.2, 1.2, 0.3), side=None,
           setback=0.8, pitch_deg=40.0, wall_mat=None):
    """Gable dormer on one slope of a pitched RoofSpec: `dormer_<name>_front` wall at
    `setback` up-slope of the wall line, `dormer_<name>_cheek0/1`, `dormerroof_<name>`
    mini gable (ridge perpendicular to the main ridge) and a `window_bay` (win.sill is
    RELATIVE to the dormer floor here). u = position along the main ridge axis."""
    g = _roof_geometry(spec)
    side = side or ("-X" if g["ax"] == "Y" else "-Y")
    s0 = side.startswith("-")
    t = g["t0"] if s0 else g["t1"]
    wl = g["e0"] if s0 else g["e1"]
    sgn = 1.0 if s0 else -1.0
    e_f = wl + sgn * setback
    cz = _centre_z(spec, e_f, 0 if s0 else 1)
    top_off = 0.5 * spec.thick * math.sqrt(1 + t * t)
    z_f = cz + top_off
    z_plate = z_f + h
    depth = (h + 0.35) / max(t, 0.05)
    wall_mat = wall_mat or "timber_lap"
    wall = WallRef(side, e_f)
    nvec, tvec = _SIDES[side]
    uu = u if side in ("+X", "-Y") else -u     # u along the main ridge -> wall u
    del tvec
    parts = wall_box(f"dormer_{name}_front", wall, uu - w / 2, uu + w / 2, z_f - 0.35, z_plate,
                     -0.15, 0.0, wall_mat, drop=("back",))
    for k, du in enumerate((-w / 2, w / 2)):
        p_fb = wall_point(wall, uu + du, z_f - 0.35, -0.0)
        p_ft = wall_point(wall, uu + du, z_plate, 0.0)
        p_bt = wall_point(wall, uu + du, z_plate, -depth)
        thick = Vector(_SIDES[side][1]) * (0.12 if du > 0 else -0.12)
        if side in ("-X", "+Y"):
            thick = -thick
        gg = _Geo()
        gg.prism([p_fb, p_ft, p_bt], thick)
        parts.append((gg.finish(f"dormer_{name}_cheek{k}"), wall_mat))
    # mini gable roof over the dormer (ridge along the dormer axis = outward normal)
    tp = math.tan(math.radians(pitch_deg))
    ov = 0.25
    half = w / 2 + ov
    ridge_z = z_plate + tp * (w / 2)
    ax_d = "X" if side in ("+X", "-X") else "Y"
    a_out = wall_point(wall, uu, 0, ov)
    a_in = wall_point(wall, uu, 0, -depth - 0.3)
    a0, a1 = (sorted((a_out.x, a_in.x)) if ax_d == "X" else sorted((a_out.y, a_in.y)))
    c_across = wall_point(wall, uu, 0, 0)
    cc = c_across.y if ax_d == "X" else c_across.x
    for k, sg in enumerate((-1.0, 1.0)):
        parts += roof_slab(f"dormer{name}{k}", ax_d, a0, a1, cc + sg * half,
                           z_plate - tp * ov, cc - sg * 0.1, ridge_z + 0.1 * tp, 0.16,
                           mat=spec.mat, prefix="dormerroof_")
    # front gable triangle above the front wall (dormer_ prefix: deleted with the dormer)
    tri = [wall_point(wall, uu - w / 2, z_plate, 0.0), wall_point(wall, uu + w / 2, z_plate, 0.0),
           wall_point(wall, uu, ridge_z - 0.03, 0.0)]
    gt = _Geo()
    gt.prism(tri, -nvec * 0.15)
    parts.append((gt.finish(f"dormer_{name}_gable"), wall_mat))
    gw = WallRef(side, e_f, uu - w / 2, uu + w / 2)
    wspec = replace(win, sill=z_f + win.sill)
    parts += window_bay(f"dormer{name}", gw, uu - w / 2, uu + w / 2, wspec,
                        room=room or f"dormer{name}")
    return parts


def roof_window(name, spec, side, u, z, w=0.9, h=1.2, room=None, panes=True):
    """Roof window on one slope: `rwin_<name>` frame ring (0.08 tall, 0.10 wide) on the
    slab's top plane and the pane `win_<room>_<name>` 0.03 above it. u = position along
    the ridge axis, z = centre-plane height of the window centre (the contract's
    "upper row at z 12.5"). With panes=False the pane is painted (`glass_dark`)."""
    g = _roof_geometry(spec)
    s0 = side.startswith("-")
    t = g["t0"] if s0 else g["t1"]
    e_c = g["e0"] + (z - g["z0"]) / g["t0"] if s0 else g["e1"] - (z - g["z1"]) / g["t1"]
    n, up, along = roof_frame(spec, side)
    top_off = 0.5 * spec.thick * math.sqrt(1 + t * t)
    if g["ax"] == "Y":
        c = Vector((e_c, u, z + top_off))
    else:
        c = Vector((u, e_c, z + top_off))
    fr = _Geo()
    hw, hh, fw, fh = w / 2, h / 2, 0.10, 0.08
    # ring = 4 boxes laid on the plane (bottom faces dropped: they sit on the slab)
    for (du, dv, su, sv) in ((0, hh + fw / 2, hw + fw, fw / 2), (0, -hh - fw / 2, hw + fw, fw / 2),
                             (hw + fw / 2, 0, fw / 2, hh), (-hw - fw / 2, 0, fw / 2, hh)):
        cc = c + along * du + up * dv + n * (fh / 2 - 0.01)
        fr.obb(cc, (along, up, n), (su, sv, fh / 2), drop=("-Z",))
    parts = [(fr.finish(f"rwin_{name}"), "trim")]
    pc = c + n * ROOF_PANE_OFFSET
    pts = [pc - along * hw - up * hh, pc + along * hw - up * hh, pc + along * hw + up * hh,
           pc - along * hw + up * hh]
    if panes:
        parts += _single(f"win_{room or name}_{name}", lambda gg: gg.poly(pts, n), "glass_dark")
    else:
        parts += _single(f"rwin_{name}glass", lambda gg: gg.poly(pts, n), "glass_dark")
    return parts


def vent_stack(name, spec, x, y, r=0.12, rise=0.8, segs=8):
    """`vent_<name>` 8-seg pipe, base 0.1 below roof_z, rise above it, top capped."""
    z0 = roof_z(spec, x, y) - 0.10
    return _single(f"vent_{name}",
                   lambda g: g.cyl((x, y, z0), (x, y, z0 + 0.10 + rise), r, segs, cap0=False),
                   "galv")


def chimney(name, x, y, w, d, top_z, base_z=0.0, shoulder=None, ground=False,
            mat="stone_rubble"):
    """`chim_<name>` stack to top_z - 0.28, optional shoulder (w, d, top) as
    `chim_<name>sh` from base_z (open bottom when ground), `chimcap_<name>` slab +0.2 each
    side, 0.28 thick (base_lodge.py:868-873)."""
    parts = []
    stack_z0 = base_z
    if shoulder is not None:
        sw, sd, stop = shoulder
        parts += box_part(f"chim_{name}sh", (x - sw / 2, y - sd / 2, base_z),
                          (x + sw / 2, y + sd / 2, stop), mat, drop=("+Z",),
                          ground=ground and base_z == 0.0)
        stack_z0 = stop
        ground = False
    cap0 = top_z - 0.28
    parts += box_part(f"chim_{name}", (x - w / 2, y - d / 2, stack_z0),
                      (x + w / 2, y + d / 2, cap0 + 0.04), mat,
                      drop=("+Z",) + (() if ground else ("-Z",)), ground=ground)
    parts += box_part(f"chimcap_{name}", (x - w / 2 - 0.2, y - d / 2 - 0.2, cap0),
                      (x + w / 2 + 0.2, y + d / 2 + 0.2, top_z), "galv")
    return parts


def stovepipe(name, spec, x, y, r=0.15, rise=1.2):
    """`stovepipe_<name>` pipe from the roof plus a cap disc `stovepipe_<name>cap`."""
    z0 = roof_z(spec, x, y) - 0.1
    parts = _single(f"stovepipe_{name}",
                    lambda g: g.cyl((x, y, z0), (x, y, z0 + 0.1 + rise), r, 8, cap0=False,
                                    cap1=False), "galv")
    zt = z0 + 0.1 + rise

    def cap(g):
        g.cyl((x, y, zt + 0.12), (x, y, zt + 0.20), r * 2.0, 8)
        if FITTINGS.get("version", 0) >= 1:
            # close look (2026-10-17): the rain cap floated 12 cm over the open pipe (Richard: "floating"); it now
            # stands on a collar from inside the pipe top into the cap disc (16 tris)
            g.cyl((x, y, zt - 0.03), (x, y, zt + 0.125), r * 0.97, 8, cap0=False, cap1=False)
    parts += _single(f"stovepipe_{name}cap", cap, "galv")
    return parts


# ======================================================================================
# Openings and facade (section 4.3)
# ======================================================================================

def window_bay(name, wall, u0, u1, win, room=None, sill_board=True, head_board=True):
    """Framed window centred in [u0, u1] (width win.w): panes = ONE quad per light per row
    `win_<room>_<k>` at wall + 0.05 (a transom splits each light in two); frame members
    `sill_` 0.30 x 0.16, `head_` 0.26 x 0.20, `jamb_` 0.22 x 0.16, `mull_` 0.20 x 0.12 with
    their outer face at pane + reveal (base_lodge.py:589-608); `ctran_<name>` transom bar.
    With the window kit v2 active (window_kit(), lodge.py --window-kit v2) this is
    window_bay_v2: same signature, same panes (rect, names, rooms), recessed REVEAL_M."""
    room = room or name
    if "_" in room or not room.isalnum():
        raise ValueError(f"room tag {room!r} must be alphanumeric (no underscore)")
    if WINDOW_KIT["version"] == 2 and win.frame:
        return window_bay_v2(name, wall, u0, u1, win, room, sill_board, head_board)
    uc = 0.5 * (u0 + u1)
    if win.w > (u1 - u0) + 1e-6:
        raise ValueError(f"window_bay {name}: window {win.w} m wider than its bay {u1 - u0:.2f}")
    a0, a1 = uc - win.w / 2, uc + win.w / 2
    z0, z1 = win.sill, win.sill + win.h
    face = PANE_OFFSET + win.reveal
    parts = []
    rows = [(z0 + (z1 - z0) * r / win.rows, z0 + (z1 - z0) * (r + 1) / win.rows)
            for r in range(win.rows)]
    if win.transom is not None and z0 < win.transom < z1:
        rows = [(rz0, rz1) for (rz0, rz1) in
                sum(([(a, min(b, win.transom)), (max(a, win.transom), b)]
                     if a < win.transom < b else [(a, b)] for a, b in rows), [])]
    k = 0
    for rz0, rz1 in rows:
        for li in range(win.lights):
            la = a0 + (a1 - a0) * li / win.lights
            lb = a0 + (a1 - a0) * (li + 1) / win.lights
            parts += pane_quad(f"win_{room}_{name}{k}", wall, la, lb, rz0, rz1)
            k += 1
    if not win.frame:
        return parts
    if sill_board:
        parts += wall_box(f"sill_{name}", wall, a0 - 0.18, a1 + 0.18, z0 - 0.16, z0,
                          face - 0.30, face, "trim")
    if head_board:
        parts += wall_box(f"head_{name}", wall, a0 - 0.18, a1 + 0.18, z1, z1 + 0.20,
                          face - 0.26, face, "trim")
    jz0 = z0 - (0.16 if sill_board else 0.0)
    jz1 = z1 + (0.20 if head_board else 0.0)
    for j, uj in enumerate((a0 - 0.08, a1 + 0.08)):
        parts += wall_box(f"jamb_{name}{j}", wall, uj - 0.08, uj + 0.08, jz0, jz1,
                          face - 0.22, face - 0.01, "trim")
    mg = _Geo()
    nm = 0
    for li in range(1, win.lights):
        um = a0 + (a1 - a0) * li / win.lights
        lo, hi = _wall_aabb(wall, um - 0.06, um + 0.06, z0, z1, face - 0.20, face)
        mg.aabb(lo, hi, _wall_keys(wall, ("back",)))
        nm += 1
    for r in range(1, win.rows):
        zr = z0 + (z1 - z0) * r / win.rows
        lo, hi = _wall_aabb(wall, a0, a1, zr - 0.05, zr + 0.05, face - 0.18, face - 0.02)
        mg.aabb(lo, hi, _wall_keys(wall, ("back",)))
        nm += 1
    if nm:
        parts.append((mg.finish(f"mull_{name}"), "trim"))
    else:
        mg.bm.free()
    if win.transom is not None and z0 < win.transom < z1:
        parts += wall_box(f"ctran_{name}", wall, a0, a1, win.transom - 0.06,
                          win.transom + 0.06, face - 0.22, face, "trim")
    return parts


# --------------------------------------------------------------------------------------
# Window kit v2 (2026-10-17, docs/research/art/window-kit-2026-10-17.md). Richard on the
# rollout previews: "the vanilla school's window frames next to ours is, uh, rough. Ours need
# to be a bit more detailed and fine." Dimensions from the vanilla meshes (window-kit doc
# section 1: EU_ResidentialLow01 glass 12 cm behind the wall, casing 10.5 cm wide / 3 cm
# proud, sash bar 4 cm, sill 10.5 cm out / 3 cm thick and sloped; NA_ResidentialLow01 7.7 cm
# reveal, 4.6 cm casing 2 cm proud; ElementarySchool01 15.5 cm splayed reveal; the hotel 7 cm
# frames). One opening = a HOLE in the wall face (punch_openings, LOD0 copy only), a jamb-board
# liner `reveal_` round the tunnel, a thin casing (`head_` = the U-shaped casing, or `jamb_`
# posts + a `head_` lintel board on the arrival facade), a sloped `sill_` with a drip nose
# (weathered trim key), a sash ring `sash_` with a glazing-bead step (mode "full"), thin
# glazing bars `mull_`, panes `win_` REVEAL_M behind the wall face. Tris per opening: section 4
# of the doc (kit_tris()). The far set (LOD1 / LOD2) never gets holes: reveal_ / sash_ / mull_
# are LOD1 deletes and the LOD1 / LOD2 `_Win` panes move back to PANE_OFFSET (unrecess_panes).
# --------------------------------------------------------------------------------------

REVEAL_M = 0.10         # glass behind the wall face (vanilla 7.7-15.5 cm; houses 7.7 / 12)
CASING_W = 0.07         # casing board width (vanilla 4.6-10.5 cm)
CASING_P = 0.025        # casing proud of the wall (vanilla 2-3 cm)
LINTEL_H = 0.13         # arrival-facade head board: height, proud, ears past the casing
LINTEL_P = 0.04
LINTEL_EAR = 0.03
SILL_P = 0.06           # sill nose beyond the wall face
SILL_DROP = 0.025       # slope of the sill top over SILL_P (~23 deg)
SILL_NOSE = 0.045       # nose (front face) height
SILL_KERF = 0.010       # the underside rises this much toward the wall: the drip edge
SILL_HORN = 0.03        # sill run past the casing each side
SASH_W = 0.045          # sash stiles / head rail on the glass
SASH_WB = 0.06          # sash bottom rail
SASH_STEP = 0.035       # glazing-bead step: sash face in front of the glass
BAR_W = 0.035           # glazing bar between lights (vanilla EU house 4 cm)
BAR_ROW_W = 0.04        # meeting rail between rows
BAR_TRANSOM_W = 0.06    # transom
BAR_D = 0.025           # glazing bars stand this far in front of the glass
SURF_GLASS = PANE_OFFSET  # surface openings (no hole): glass at the v1 pane depth (LOD1 / 2 `_Win` = v1) ...
SURF_CASING_P = 0.10    # ... and the casing face 5 cm in front of it
BAR_D_LEAN = 0.012      # lean mode: a flat bar (front quad only, 2 tris) this far off the glass
KIT2_RECESSED = ("reveal_", "sash_", "mull_")       # behind the (unholed) wall face: cull-protect
KIT2_PARTS = ("reveal_", "sash_", "mull_", "jamb_", "head_", "sill_")
PUNCH_ORDER = "v"       # punch_openings: "h" = sill / head lines first (shared along a row), "v"

# FAMILIES (Richard's lodge window references, 2026-10-16: Snowbasin Needles / John Paul, the
# glazed BC lodge, A-Basin's 6th Alley, a residential ski lodge, a European timber casement):
#   glazing  big-lodge window walls: thin dark / timber frames, no sash ring, aluminium-width
#            mullions (5 cm), a slim sill flashing, a header on the arrival facade (L1, L4, L5)
#   chalet   divided-light sashes in WIDE timber casings (12 cm, the building's heavy timber),
#            painted sash + bars + deep sill, a head board on every window (L2, L3)
#   utility  plain frames, a thin frame ring, a header board on every window (school, P1, P2;
#            the school's own photo, Palisades "Squaw Kids", keeps 2-row divided lights in
#            light casings, so the rows stay)
FAMILIES = {
    "glazing": dict(reveal=0.08, casing_w=0.05, casing_p=0.02, casing_key=None, sash=False,
                    sash_w=0.04, sash_wb=0.04, sash_step=0.02, bar_w=0.05, row_w=0.05,
                    transom_w=0.06, bar_d=0.04, sill_p=0.03, sill_drop=0.012, sill_nose=0.025,
                    lintel="arrival", lintel_h=0.13, lintel_p=0.04, rows=True),
    "chalet": dict(reveal=0.10, casing_w=0.12, casing_p=0.025, casing_key="timber_heavy",
                   sash=True, sash_w=0.05, sash_wb=0.065, sash_step=0.035, bar_w=0.035,
                   row_w=0.04, transom_w=0.06, bar_d=0.025, sill_p=0.08, sill_drop=0.03,
                   sill_nose=0.05, lintel="all", lintel_h=0.15, lintel_p=0.045, rows=True),
    "utility": dict(reveal=0.10, casing_w=0.06, casing_p=0.02, casing_key=None, sash=True,
                    sash_w=0.04, sash_wb=0.045, sash_step=0.02, bar_w=0.05, row_w=0.05,
                    transom_w=0.06, bar_d=0.03, sill_p=0.05, sill_drop=0.02, sill_nose=0.04,
                    lintel="all", lintel_h=0.12, lintel_p=0.035, rows=True),
    # the first cut (no family): the module constants above
    "base": dict(reveal=REVEAL_M, casing_w=CASING_W, casing_p=CASING_P, casing_key=None,
                 sash=True, sash_w=SASH_W, sash_wb=SASH_WB, sash_step=SASH_STEP, bar_w=BAR_W,
                 row_w=BAR_ROW_W, transom_w=BAR_TRANSOM_W, bar_d=BAR_D, sill_p=SILL_P,
                 sill_drop=SILL_DROP, sill_nose=SILL_NOSE, lintel="arrival", lintel_h=LINTEL_H,
                 lintel_p=LINTEL_P, rows=True),
}

WINDOW_KIT = {"version": 1, "mode": "full", "arrival": None, "lintel": "family",
              "trim": "trim", "trim_w": "trim_w", "family": "base", "holes": "all"}


def kit_params():
    return FAMILIES[WINDOW_KIT.get("family") or "base"]


class window_kit:
    """Context manager: window_bay builds kit v2 inside it (lodge.build_parts).
    mode "full" (sash ring) | "lean" (no sash ring: the no-headroom buildings);
    arrival = authoring-frame (x, y) of the arrival direction (lintel boards on the wall that
    faces it); lintel "arrival" | "all" | "none"; trim / trim_w = casing and sill keys."""

    def __init__(self, version=2, mode="full", arrival=None, lintel="family", trim="trim",
                 trim_w="trim_w", family="base", holes="all"):
        if mode not in ("full", "lean") or lintel not in ("family", "arrival", "all", "none")                 or family not in FAMILIES or holes not in ("all", "arrival"):
            raise ValueError(f"window_kit: mode {mode!r} / lintel {lintel!r} / family {family!r}")
        self.cfg = {"version": int(version), "mode": mode, "lintel": lintel, "trim": trim,
                    "trim_w": trim_w, "family": family, "holes": holes,
                    "arrival": None if arrival is None else (float(arrival[0]), float(arrival[1]))}
        self._saved = None

    def __enter__(self):
        self._saved = dict(WINDOW_KIT)
        WINDOW_KIT.update(self.cfg)
        return WINDOW_KIT

    def __exit__(self, *exc):
        WINDOW_KIT.clear()
        WINDOW_KIT.update(self._saved)
        return False


def jamb_width():
    """Width of the frame member beside the glass (shutters hang outside it)."""
    return kit_params()["casing_w"] if WINDOW_KIT["version"] == 2 else 0.16


def kit_tris(lights=1, rows=1, transom=False, mode="full", lintel=False, sill=True):
    """Main-mesh tris of one v2 opening BEFORE the wall hole (+ ~7 after the ring merge):
    reveal 8, casing 12 (U) or 18 (posts + lintel), sill 10, sash 16 (full), bars 6 each
    (lean: no sash, flat 2-tri bars)."""
    bars = (lights - 1) + (rows - 1) + (1 if transom else 0)
    return (8 + (18 if lintel else 12) + (10 if sill else 4) + (16 if mode == "full" else 0)
            + (6 if mode == "full" else 2) * bars)


def _wpoly(g, wall, pts, out):
    """Polygon from wall-local (u, z, d) points, outward hint in local (du, dz, dd)."""
    n, t = _SIDES[wall.side]
    o = t * out[0] + Vector((0.0, 0.0, out[1])) + n * out[2]
    return g.poly([wall_point(wall, u, z, d) for (u, z, d) in pts], o)


def window_bay_v2(name, wall, u0, u1, win, room, sill_board=True, head_board=True):
    """Kit v2 opening centred in [u0, u1] (module notes above). The panes keep the v1 rects
    and names (rooms unchanged); only their depth moves to -REVEAL_M."""
    cfg = WINDOW_KIT
    p = kit_params()
    full = cfg["mode"] == "full" and p["sash"]
    trim, trim_w = cfg["trim"], cfg["trim_w"]
    ckey = p["casing_key"] or trim                      # casing + jamb board (chalet: timber)
    R, CW, CP = p["reveal"], p["casing_w"], p["casing_p"]
    SILL_P, SILL_DROP, SILL_NOSE = p["sill_p"], p["sill_drop"], p["sill_nose"]
    SASH_W, SASH_WB, SASH_STEP = p["sash_w"], p["sash_wb"], p["sash_step"]
    BAR_W, BAR_ROW_W, BAR_TRANSOM_W, BAR_D = p["bar_w"], p["row_w"], p["transom_w"], p["bar_d"]
    LINTEL_H, LINTEL_P = p["lintel_h"], p["lintel_p"]
    nrows = win.rows if p["rows"] else 1
    uc = 0.5 * (u0 + u1)
    if win.w > (u1 - u0) + 1e-6:
        raise ValueError(f"window_bay {name}: window {win.w} m wider than its bay {u1 - u0:.2f}")
    a0, a1 = uc - win.w / 2, uc + win.w / 2
    z0, z1 = win.sill, win.sill + win.h
    n, _t = _SIDES[wall.side]
    lin = cfg["lintel"] if cfg["lintel"] != "family" else p["lintel"]
    if not sill_board:
        lintel = False                                    # the ring casing (school slits)
    elif lin == "all":
        lintel = head_board
    elif lin == "arrival" and cfg["arrival"] is not None:
        lintel = head_board and (n.x * cfg["arrival"][0] + n.y * cfg["arrival"][1]) > 0.7
    else:
        lintel = False
    faces_arrival = cfg["arrival"] is not None and         (n.x * cfg["arrival"][0] + n.y * cfg["arrival"][1]) > 0.7
    holes = cfg.get("holes", "all")
    recessed = holes == "all" or (holes == "arrival" and faces_arrival)
    if not recessed:
        # SURFACE opening (no wall hole; the budget-bound buildings off their arrival facade):
        # glass SURF_GLASS proud of the wall, the casing built out to SURF_CASING_P round it,
        # so the glass still sits 5 cm behind the frame face
        R, CP = -SURF_GLASS, SURF_CASING_P
        LINTEL_P = max(LINTEL_P, CP + 0.02)
        SILL_P = max(SILL_P, CP + 0.03)
    parts = []
    # -- panes (v1 rects, recessed) -------------------------------------------------------
    rows = [(z0 + (z1 - z0) * r / nrows, z0 + (z1 - z0) * (r + 1) / nrows)
            for r in range(nrows)]
    has_tr = win.transom is not None and z0 < win.transom < z1
    if has_tr:
        rows = [(rz0, rz1) for (rz0, rz1) in
                sum(([(a, min(b, win.transom)), (max(a, win.transom), b)]
                     if a < win.transom < b else [(a, b)] for a, b in rows), [])]
    k = 0
    for rz0, rz1 in rows:
        for li in range(win.lights):
            la = a0 + (a1 - a0) * li / win.lights
            lb = a0 + (a1 - a0) * (li + 1) / win.lights
            parts += pane_quad(f"win_{room}_{name}{k}", wall, la, lb, rz0, rz1, d=-R)
            k += 1
    # -- reveal: jamb-board liner round the tunnel (sides run out to the casing face) -----
    lz = 0.015 if sill_board else 0.0
    g = _Geo()
    _wpoly(g, wall, [(a0, z0 - lz, -R), (a0, z1, -R), (a0, z1, CP), (a0, z0 - lz, CP)], (1, 0, 0))
    _wpoly(g, wall, [(a1, z0 - lz, -R), (a1, z0 - lz, CP), (a1, z1, CP), (a1, z1, -R)], (-1, 0, 0))
    if recessed:
        dt = 0.0 if lintel else CP       # under a U casing the head soffit runs out to its face
        _wpoly(g, wall, [(a0, z1, -R), (a1, z1, -R), (a1, z1, dt), (a0, z1, dt)], (0, -1, 0))
        _wpoly(g, wall, [(a0, z0, -R), (a0, z0, 0.0), (a1, z0, 0.0), (a1, z0, -R)], (0, 1, 0))
        parts.append((g.finish(f"reveal_{name}"), ckey))
    else:                                # no tunnel: sides (+ the head soffit under a U casing)
        if not lintel:
            _wpoly(g, wall, [(a0, z1, -R), (a1, z1, -R), (a1, z1, CP), (a0, z1, CP)], (0, -1, 0))
        parts.append((g.finish(f"jamb_{name}i"), ckey))
    # -- casing ---------------------------------------------------------------------------
    zc = z0 - 0.03 if sill_board else z0 - CW            # posts end inside the sill / a ring
    hc = CW
    g = _Geo()
    if lintel:
        for (ua, ub, sg) in ((a0 - CW, a0, -1), (a1, a1 + CW, 1)):
            _wpoly(g, wall, [(ua, zc, CP), (ub, zc, CP), (ub, z1, CP), (ua, z1, CP)], (0, 0, 1))
            uo = ua if sg < 0 else ub
            _wpoly(g, wall, [(uo, zc, 0.0), (uo, z1, 0.0), (uo, z1, CP), (uo, zc, CP)], (sg, 0, 0))
        if not sill_board:
            _wpoly(g, wall, [(a0 - CW, zc, 0.0), (a1 + CW, zc, 0.0), (a1 + CW, zc, CP),
                             (a0 - CW, zc, CP)], (0, -1, 0))
        parts.append((g.finish(f"jamb_{name}"), ckey))
        parts += wall_box(f"head_{name}", wall, a0 - CW - LINTEL_EAR, a1 + CW + LINTEL_EAR,
                          z1, z1 + LINTEL_H, 0.0, LINTEL_P, ckey, drop=("back",))
    else:
        zb = z0 if not sill_board else zc
        _wpoly(g, wall, [(a0 - CW, zb, CP), (a0, zb, CP), (a0, z1, CP), (a1, z1, CP), (a1, zb, CP),
                         (a1 + CW, zb, CP), (a1 + CW, z1 + hc, CP), (a0 - CW, z1 + hc, CP)], (0, 0, 1))
        _wpoly(g, wall, [(a0 - CW, zc, 0.0), (a0 - CW, z1 + hc, 0.0), (a0 - CW, z1 + hc, CP),
                         (a0 - CW, zc, CP)], (-1, 0, 0))
        _wpoly(g, wall, [(a1 + CW, zc, 0.0), (a1 + CW, zc, CP), (a1 + CW, z1 + hc, CP),
                         (a1 + CW, z1 + hc, 0.0)], (1, 0, 0))
        _wpoly(g, wall, [(a0 - CW, z1 + hc, 0.0), (a1 + CW, z1 + hc, 0.0), (a1 + CW, z1 + hc, CP),
                         (a0 - CW, z1 + hc, CP)], (0, 1, 0))
        if not sill_board:                               # close the ring under the glass
            _wpoly(g, wall, [(a0 - CW, zc, CP), (a1 + CW, zc, CP), (a1 + CW, z0, CP), (a1, z0, CP),
                             (a0, z0, CP), (a0 - CW, z0, CP)], (0, 0, 1))
            _wpoly(g, wall, [(a0 - CW, zc, 0.0), (a0 - CW, zc, CP), (a1 + CW, zc, CP),
                             (a1 + CW, zc, 0.0)], (0, -1, 0))
            _wpoly(g, wall, [(a0, z0, 0.0), (a1, z0, 0.0), (a1, z0, CP), (a0, z0, CP)], (0, 1, 0))
        parts.append((g.finish(f"head_{name}"), ckey))
    # -- sill: sloped top, nose, drip underside (weathered trim) --------------------------
    if sill_board:
        uL, uR = a0 - CW - SILL_HORN, a1 + CW + SILL_HORN
        prof = [(0.0, z0), (SILL_P, z0 - SILL_DROP), (SILL_P, z0 - SILL_DROP - SILL_NOSE),
                (0.0, z0 - SILL_DROP - SILL_NOSE + SILL_KERF)]
        g = _Geo()
        outs = [(0, SILL_P, SILL_DROP), (0, 0, 1), (0, -1, 0)]
        for i in range(3):
            (d0_, zz0), (d1_, zz1) = prof[i], prof[i + 1]
            _wpoly(g, wall, [(uL, zz0, d0_), (uR, zz0, d0_), (uR, zz1, d1_), (uL, zz1, d1_)], outs[i])
        _wpoly(g, wall, [(uL, zz, dd) for dd, zz in prof], (-1, 0, 0))
        _wpoly(g, wall, [(uR, zz, dd) for dd, zz in prof], (1, 0, 0))
        parts.append((g.finish(f"sill_{name}"), trim_w))
    # -- sash ring with the glazing-bead step (full) ----------------------------------------
    if full:
        ds = -R + SASH_STEP
        ia0, ia1, iz0, iz1 = a0 + SASH_W, a1 - SASH_W, z0 + SASH_WB, z1 - SASH_W
        O = [(a0, z0), (a1, z0), (a1, z1), (a0, z1)]
        In = [(ia0, iz0), (ia1, iz0), (ia1, iz1), (ia0, iz1)]
        g = _Geo()
        for i in range(4):
            j = (i + 1) % 4
            _wpoly(g, wall, [(O[i][0], O[i][1], ds), (O[j][0], O[j][1], ds),
                             (In[j][0], In[j][1], ds), (In[i][0], In[i][1], ds)], (0, 0, 1))
        steps = (((ia0, iz0), (ia1, iz0), (0, 1, 0)), ((ia1, iz0), (ia1, iz1), (-1, 0, 0)),
                 ((ia1, iz1), (ia0, iz1), (0, -1, 0)), ((ia0, iz1), (ia0, iz0), (1, 0, 0)))
        for (pa, pb, out) in steps:
            _wpoly(g, wall, [(pa[0], pa[1], -R), (pb[0], pb[1], -R), (pb[0], pb[1], ds),
                             (pa[0], pa[1], ds)], out)
        parts.append((g.finish(f"sash_{name}"), trim))
        bu0, bu1, bz0, bz1 = ia0, ia1, iz0, iz1
    else:
        bu0, bu1, bz0, bz1 = a0, a1, z0, z1
    # -- glazing bars / meeting rails / transom (front + two sides each) -------------------
    g = _Geo()
    nb = 0
    lean = cfg["mode"] == "lean"
    df = -R + (BAR_D_LEAN if lean else BAR_D)              # lean: a flat bar 12 mm off the glass
    for li in range(1, win.lights):
        um = a0 + (a1 - a0) * li / win.lights
        ua, ub = um - BAR_W / 2, um + BAR_W / 2
        _wpoly(g, wall, [(ua, bz0, df), (ub, bz0, df), (ub, bz1, df), (ua, bz1, df)], (0, 0, 1))
        if not lean:
            _wpoly(g, wall, [(ua, bz0, -R), (ua, bz0, df), (ua, bz1, df), (ua, bz1, -R)], (-1, 0, 0))
            _wpoly(g, wall, [(ub, bz0, -R), (ub, bz1, -R), (ub, bz1, df), (ub, bz0, df)], (1, 0, 0))
        nb += 1
    hz = [(z0 + (z1 - z0) * r / nrows, BAR_ROW_W) for r in range(1, nrows)]
    if has_tr:
        hz.append((win.transom, BAR_TRANSOM_W))
    dh = df - 0.005                                       # under the vertical bars' faces
    for zr, bw in hz:
        za, zb_ = zr - bw / 2, zr + bw / 2
        _wpoly(g, wall, [(bu0, za, dh), (bu1, za, dh), (bu1, zb_, dh), (bu0, zb_, dh)], (0, 0, 1))
        if not lean:
            _wpoly(g, wall, [(bu0, zb_, -R), (bu0, zb_, dh), (bu1, zb_, dh), (bu1, zb_, -R)], (0, 1, 0))
            _wpoly(g, wall, [(bu0, za, -R), (bu1, za, -R), (bu1, za, dh), (bu0, za, dh)], (0, -1, 0))
        nb += 1
    if nb:
        parts.append((g.finish(f"mull_{name}"), trim))
    else:
        g.bm.free()
    return parts


def _kit2_name(nm):
    return nm.startswith(KIT2_PARTS)


def _over_ring(f, t, ua, ub, za, zb, m=0.6):
    us = [v.co.dot(t) for v in f.verts]
    zs = [v.co.z for v in f.verts]
    return not (max(us) < ua - m or min(us) > ub + m or max(zs) < za - m or min(zs) > zb + m)


def punch_openings(obj, table, tol=2e-3, triangulate=True, merge=True):
    """LOD0 copy only (lodge.run, kit v2): cut a hole in the wall face behind every `reveal_`
    liner. The hole rect is read back from the liner itself (its side quads run out to the
    casing face, its top / bottom quads stop at the wall plane: that tells outward and the
    plane), so recipe transforms after the kit never matter. Faces of ANY non-kit part that
    lie in the wall plane, face outward and overlap the rect are cut LOCALLY: the two
    vertical lines through the faces that overlap the opening's height, then the two
    horizontal lines through the middle column only, and the pieces inside the rect are
    deleted. bmesh splits shared edges for both faces: no T-junction. lodge runs it on the
    tiled LOD0 AFTER cs2_tilesheet.tile_mesh (only the tile pieces under an opening split;
    run before it, every hole line crossed every tile row: +31 tris / hole on the school),
    then re-triangulates the cut pieces and re-shades. Returns the report."""
    me = obj.data
    M = obj.matrix_world
    if any(abs(M[i][j] - (1.0 if i == j else 0.0)) > 1e-9 for i in range(4) for j in range(4)):
        raise RuntimeError(f"punch_openings: {obj.name} has a non-identity transform")
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    layer = bm.faces.layers.int.get("part")
    names = list(table)

    def pname(f):
        k = f[layer]
        return names[k - 1] if 0 < k <= len(names) else ""

    liners = {}
    for f in bm.faces:
        nm = pname(f)
        if nm.startswith("reveal_"):
            liners.setdefault(nm, []).append(f)
    Z = Vector((0.0, 0.0, 1.0))
    ops = []
    for nm in sorted(liners):
        fs = liners[nm]
        side = [f for f in fs if abs(f.normal.z) < 0.5]
        hor = [f for f in fs if abs(f.normal.z) >= 0.5]
        if not side or not hor:                     # quads, or their triangles after tile_mesh
            raise RuntimeError(f"punch_openings: {nm} has {len(side)} side / {len(hor)} "
                               f"horizontal faces (culled or edited?)")
        t = side[0].normal.copy()
        t.z = 0.0
        t.normalize()
        nax = Z.cross(t)
        sv = [v.co.copy() for f in side for v in f.verts]
        hv = [v.co.copy() for f in hor for v in f.verts]
        bv = [v.co.copy() for f in hor if f.normal.z > 0.5 for v in f.verts]   # the stool: d <= 0
        s = [p.dot(nax) for p in sv]
        h = [p.dot(nax) for p in (bv or hv)]
        if max(s) > max(h) + 1e-4:
            n, plane = nax, max(h)
        elif min(s) < min(h) - 1e-4:
            n, plane = -nax, -min(h)
        else:
            raise RuntimeError(f"punch_openings: {nm}: cannot tell the outward side")
        uu = [p.dot(t) for p in sv]
        # door kit v2 liners have no stool (the threshold closes the bottom): z from the jambs
        zz = [p.z for p in hv] if bv else [p.z for p in sv] + [p.z for p in hv]
        ops.append((nm, n, t, plane, min(uu), max(uu), min(zz), max(zz)))
    # faces per wall plane (one scan per plane, then kept up to date as cuts split them)
    groups = {}
    for op in ops:
        key = (round(op[1].x, 3), round(op[1].y, 3), int(round(op[3] / tol)))
        groups.setdefault(key, []).append(op)
    report = {"openings": len(ops), "holes": 0, "faces_cut": 0, "missing": [], "partial": []}
    touched = []
    created = []                 # every face a cut or the merge produced: the only ones re-triangulated
    for key in sorted(groups):
        n0, plane0 = groups[key][0][1], groups[key][0][3]
        pool = [f for f in bm.faces if f.normal.dot(n0) > 0.999
                and abs(f.calc_center_median().dot(n0) - plane0) < tol and not _kit2_name(pname(f))]
        for (nm, n, t, plane, ua, ub, za, zb) in groups[key]:
            def over(f, u_lo=ua, u_hi=ub):
                us = [v.co.dot(t) for v in f.verts]
                zs = [v.co.z for v in f.verts]
                return not (max(us) <= u_lo + tol or min(us) >= u_hi - tol
                            or max(zs) <= za + tol or min(zs) >= zb - tol)

            def cut(faces, co, no):
                faces = list(dict.fromkeys(faces))
                if not faces:
                    return []
                geom = list(dict.fromkeys(x for f in faces for x in list(f.edges) + list(f.verts)))
                geom += faces
                ret = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=co, plane_no=no,
                                             use_snap_center=False, clear_outer=False,
                                             clear_inner=False)
                out = [e for e in ret["geom"] if isinstance(e, bmesh.types.BMFace)]
                created.extend(out)
                return out

            pool = [f for f in pool if f.is_valid]
            hit = [f for f in pool if over(f)]
            if not hit:
                report["missing"].append(nm)
                continue
            if PUNCH_ORDER == "h":
                # horizontal lines first (shared by every opening of the row on this face),
                # then the two verticals inside the opening's band only
                first, second = ((Z * za, Z), (Z * zb, Z)), ((t * ua, t), (t * ub, t))

                def inband(f):
                    zs = [v.co.z for v in f.verts]
                    return min(zs) >= za - tol and max(zs) <= zb + tol
            else:
                first, second = ((t * ua, t), (t * ub, t)), ((Z * za, Z), (Z * zb, Z))

                def inband(f):
                    us = [v.co.dot(t) for v in f.verts]
                    return min(us) >= ua - tol and max(us) <= ub + tol
            for co, no in first:
                new = cut([f for f in pool if f.is_valid and over(f)], co, no)
                pool = list(dict.fromkeys([f for f in pool if f.is_valid] + new))
            col = [f for f in pool if f.is_valid and over(f) and inband(f)]
            for co, no in second:
                new = cut([f for f in col if f.is_valid and over(f)], co, no)
                col = list(dict.fromkeys([f for f in col if f.is_valid] + new))
                pool = list(dict.fromkeys([f for f in pool if f.is_valid] + new))
            hole = []
            for f in pool:
                if not f.is_valid:
                    continue
                us = [v.co.dot(t) for v in f.verts]
                zs = [v.co.z for v in f.verts]
                if (min(us) >= ua - tol and max(us) <= ub + tol and min(zs) >= za - tol
                        and max(zs) <= zb + tol):
                    hole.append(f)
            area = sum(f.calc_area() for f in hole)
            want = (ub - ua) * (zb - za)
            if area < 0.99 * want:
                report["partial"].append((nm, round(area / want, 3)))
            ring = [f for f in pool if f.is_valid and f not in set(hole) and _over_ring(f, t, ua, ub, za, zb)]
            bmesh.ops.delete(bm, geom=hole, context="FACES")
            pool = [f for f in pool if f.is_valid]
            touched.extend(f for f in ring if f.is_valid)
            report["holes"] += 1
            report["faces_cut"] += len(hole)
    if merge and touched:
        # re-merge the pieces round each hole where they share one swatch mapping (delimit UV:
        # a tile-cell or weather boundary stays), then triangulate: the hole costs its outline,
        # not every tile triangle its lines crossed
        faces = list(dict.fromkeys(f for f in touched if f.is_valid))
        n0 = sum(len(f.verts) - 2 for f in faces)
        bm.verts.index_update()
        bm.edges.index_update()
        fs = set(faces)
        edges = sorted({e for f in faces for e in f.edges
                        if len(e.link_faces) == 2 and all(lf in fs for lf in e.link_faces)},
                       key=lambda e: e.index)
        verts = sorted({v for e in edges for v in e.verts}, key=lambda v: v.index)
        ret = bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(0.5), use_dissolve_boundaries=False,
                                       verts=verts, edges=edges, delimit={"UV", "NORMAL", "SHARP"})
        merged = [f for f in ret.get("region", []) if isinstance(f, bmesh.types.BMFace)]
        created.extend(merged)
        report["ring_tris_before_merge"] = n0
        report["ring_faces_after_merge"] = len(merged)
    ngons = [f for f in dict.fromkeys(created) if f.is_valid and len(f.verts) > 3]
    if ngons and triangulate:
        bmesh.ops.triangulate(bm, faces=ngons)
    bm.to_mesh(me)
    bm.free()
    me.update()
    if triangulate:
        cs2_asset.shade_by_angle(obj, 66.0, weighted=True)
    print(f"[kit2] punch_openings {obj.name}: {report['holes']} holes / {report['openings']} "
          f"openings, {report['faces_cut']} wall faces removed, missing {report['missing'][:6]}, "
          f"partial {report['partial'][:6]}")
    return report


def check_panes_clear(lod0, win, reach=REVEAL_M + 0.03):
    """Every recessed pane must see out of its hole: a ray from the pane centre along its
    normal may not hit the LOD0 mesh within REVEAL_M + 3 cm (that would be an unholed wall).
    Returns [(pane index, distance)] of blocked panes."""
    bm = bmesh.new()
    bm.from_mesh(lod0.data)
    bm.transform(lod0.matrix_world)
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    M = win.matrix_world
    R3 = M.to_3x3()
    out = []
    for p in win.data.polygons:
        c = M @ p.center
        nn = (R3 @ p.normal).normalized()
        loc, _n, _i, dist = bvh.ray_cast(c + nn * 0.002, nn, reach)
        if loc is not None:
            out.append((p.index, round(dist + 0.002, 4)))
    return out


def unrecess_panes(win, far, depth=REVEAL_M, offset=PANE_OFFSET, tol=0.01):
    """The LOD1 / LOD2 `_Win`: a copy of `win` whose recessed panes (an unholed wall face of
    the far set `depth` in front of the pane centre) move back out to `offset` in front of
    that face: the v1 pane positions, so the far set needs no holes. Same faces, UVs and
    room cells. `depth` may be a sequence (door kit v2 adds its glass depths). Returns
    (copy, panes moved)."""
    depths = tuple(depth) if isinstance(depth, (tuple, list, set)) else (depth,)
    bm = bmesh.new()
    bm.from_mesh(far.data)
    bm.transform(far.matrix_world)
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    cp = win.copy()
    cp.data = win.data.copy()
    cp.name = win.name + "Far"
    cp.data.name = cp.name
    bpy.context.scene.collection.objects.link(cp)
    M = cp.matrix_world
    Mi = M.inverted()
    R3 = M.to_3x3()
    moved = {}
    n_moved = 0
    for p in cp.data.polygons:
        c = M @ p.center
        nn = (R3 @ p.normal).normalized()
        loc, _n, _i, dist = bvh.ray_cast(c + nn * 0.002, nn, max(depths) + 0.05)
        if loc is None:
            continue
        dd = min(depths, key=lambda x: abs(dist + 0.002 - x))     # door kit v2: door glass depths
        if abs(dist + 0.002 - dd) > tol:
            continue
        n_moved += 1
        for vi in p.vertices:
            moved[vi] = nn * (dd + offset)
    for vi, dv in moved.items():
        v = cp.data.vertices[vi]
        v.co = Mi @ ((M @ v.co) + dv)
    cp.data.update()
    return cp, n_moved


# --------------------------------------------------------------------------------------
# Door kit v2 (2026-10-17, docs/research/art/door-kit-2026-10-17.md). Richard on window kit
# v2: "big thumbs up"; the doors get the same treatment and ride the same re-export. Vanilla
# measured (doc section 1): EU_ResidentialLow01's door leaf sits 9.5 cm behind the wall face
# in an 11.7 cm casing 3 cm proud, 2 cm panel mouldings and frame steps, a 12 cm threshold;
# EU_CommercialHotel01's glass double doors: 5.3 cm aluminium stiles, frame face 12.5 cm and
# glass 18 cm behind the wall, a 28 cm transom light, canopy over. One builder (door_unit),
# three family presets matching the window families:
#   glazing  glass double doors in a dark frame (the window trim key), timber posts (the
#            surround key), a transom light when the opening is >= 2.45 m tall, a flat concrete
#            threshold plate, galvanised pull bars (L1, L4, L5)
#   chalet   half-glazed painted timber door (stiles + rails, a raised panel below the lock
#            rail) in a 12 cm timber casing, a sloped timber threshold, a lever (L2, L3)
#   utility  flat steel leaf, optional vision panel, a dark (ski_black) kick plate, a lever at
#            0.95 m (the EU house door's lever), 6 cm casing posts + a header board, a flat
#            concrete threshold plate (school, P1, P2); galv is metallic and the previews draw it
#            navy (sky reflection), so plates are flat non-metal keys, unverified in game
# Recessed (default): the leaf face sits `reveal` behind the wall face in a REAL hole cut on
# the tiled LOD0 only (punch_openings reads the `reveal_d<name>` liner: two jamb sides out
# to the casing face + the head soffit, NO stool: the threshold plate closes the bottom, so
# the hole's z range comes from the jamb sides). Surface (holes "none" / not arrival): no
# hole, the leaf at the old depth `ds` inside a casing built out to the wall. Every kit part
# draws its material's flat palette colour (door2_flat), is protected from the cull
# (door2_protect) and, except the `entry_` casing, is LOD1-deleted (door_ / reveal_).
# The leaf glass, sidelights and transom are `_Win` panes (one quad per light) in an
# EXISTING room (glass_room), so the rooms do not change.
# --------------------------------------------------------------------------------------

DOOR_FAMILIES = {
    "glazing": dict(reveal=0.10, casing="u", casing_w=0.10, casing_head=0.10, casing_p=0.03,
                    leaf="glass", stile=0.065, rail_top=0.075, rail_bot=0.16, bead=0.02,
                    transom_min=2.45, leaf_h=2.15, transom_bar=0.07, sl_mull=0.10, sl_rail=0.22,
                    handle="pull", sill="plate", sill_p=0.05, sill_t=0.012, double_min=1.5),
    "chalet": dict(reveal=0.10, casing="u", casing_w=0.12, casing_head=0.16, casing_p=0.025,
                   leaf="half", stile=0.11, rail_top=0.11, rail_lock=0.14, lock_z=0.95,
                   rail_bot=0.20, panel_raise=0.015, bead=0.0,
                   transom_min=None, leaf_h=None, transom_bar=0.07, sl_mull=0.10, sl_rail=0.22,
                   handle="lever", sill="slope", sill_p=0.06, sill_rise=0.035, double_min=1.5),
    "utility": dict(reveal=0.08, casing="posts", casing_w=0.06, casing_p=0.02, casing_head=0.06,
                    header_h=0.12, header_p=0.035, header_ear=0.03, leaf="steel",
                    vision=(-0.10, 0.10, 1.25, 1.85), bead=0.015, kick_h=0.25,
                    transom_min=None, leaf_h=None, transom_bar=0.07, sl_mull=0.10, sl_rail=0.22,
                    handle="lever", lever_z=0.95, sill="plate", sill_p=0.04, sill_t=0.010,
                    double_min=1.5, kick_key="ski_black"),
}
DOOR_GLASS_LEAN = 0.006      # lean mode / no bead: the glass this far behind the leaf face ...
DOOR_GLASS_OVER = 0.015      # ... and this much larger than its hole each side (hidden behind the leaf)
DOOR_PULL = (0.03, 0.90, 0.75, 0.04, 0.025)    # pull bar: width, length, bottom above floor, stand-off, depth
DOOR_LEVER = (0.13, 0.025, 1.00, 0.045, 0.02)  # lever: length, height, z above floor, stand-off, depth
DOOR_LEVER_NECK = 0.02       # the lever's return leg back to the leaf (along u), at the latch end
DOOR_ROSE = (0.05, 0.11, 0.002)              # rose / escutcheon plate: width, height, off the leaf
DOOR_LEVER_TRIS = 18         # bar 3 faces + free-end cap + latch-end face + leg (top, bottom, inner) + rose
DOOR_KIT = {"version": 1, "family": "base", "holes": "all", "mode": "full", "arrival": None}
DOOR2_BUILT = {}             # part name -> role ("kit"), filled by door_unit inside door_kit()
DOOR2_LEAF = {}              # door name -> leaf face depth d (school placards follow the leaf)
DOOR2_GLASS_D = set()        # glass depths behind the wall face (unrecess_panes, far `_Win`)
DOOR2_HARDWARE = {}          # door name -> its hardware part names, or the "none:<reason>" opt-out
DOOR_HARDWARE_KINDS = ("pull", "lever", "knob")   # the gate: one of these per leaf


class door_kit:
    """Context manager: entry() / door_unit build door kit v2 inside it (lodge.build_parts).
    family = DOOR_FAMILIES key; holes "all" | "arrival" | "none"; mode "full" | "lean" (lean:
    no glazing bead / panel-free glass step, no wedge end caps); arrival = authoring-frame
    (x, y) of the arrival direction (holes "arrival")."""

    def __init__(self, version=2, family="glazing", holes="all", mode="full", arrival=None):
        if family not in DOOR_FAMILIES or holes not in ("all", "arrival", "none") \
                or mode not in ("full", "lean"):
            raise ValueError(f"door_kit: family {family!r} / holes {holes!r} / mode {mode!r}")
        self.cfg = {"version": int(version), "family": family, "holes": holes, "mode": mode,
                    "arrival": None if arrival is None else (float(arrival[0]), float(arrival[1]))}
        self._saved = None

    def __enter__(self):
        self._saved = dict(DOOR_KIT)
        DOOR_KIT.update(self.cfg)
        DOOR2_BUILT.clear()
        DOOR2_LEAF.clear()
        DOOR2_GLASS_D.clear()
        DOOR2_HARDWARE.clear()
        return DOOR_KIT

    def __exit__(self, *exc):
        DOOR_KIT.clear()
        DOOR_KIT.update(self._saved)
        return False


def door2_flat():
    """Exact part names of the door kit parts (flat palette colour on the tiled LOD0)."""
    return tuple(sorted(DOOR2_BUILT))


def door2_protect():
    """Exact part names the cull must keep (recessed behind the unholed wall at cull time)."""
    return tuple(sorted(DOOR2_BUILT))


def door_tris(family, leaves=1, mode="full", sidelights=0, transom=False, glass=True,
              vision=True, casing=None, hardware="auto"):
    """Main-mesh tris of one v2 door BEFORE the wall hole (+ ~8-12 after the ring merge)."""
    p = DOOR_FAMILIES[family]
    full = mode == "full"
    cas = casing or p["casing"]
    t = 6                                                    # liner: 2 jambs + soffit
    t += {"u": 12, "posts": 18, "none": 0}[cas]
    if p["leaf"] == "glass":
        per = 8 + (8 if (full or not glass) else 0) + (0 if glass else 2)
    elif p["leaf"] == "half":
        per = 10 + 10 + (8 if full and p["bead"] > 0 else 0)
    else:
        per = (8 + (8 if full else 0) if vision else 2) + 2   # + kick plate
    t += leaves * per
    if hardware == "auto":                   # one pull bar / lever per leaf (door_unit's gate)
        t += (6 if p["handle"] == "pull" else DOOR_LEVER_TRIS) * leaves
    t += 4 if p["sill"] == "plate" else (8 if full else 4)
    t += sidelights * 8 + ((4 + (2 if sidelights else 0)) if transom else 0)
    return t


def door_unit(name, wall, u, w, h, zf, *, leaves=None, room=None, glass_room=None, sidelights=0,
              sidelight_w=0.9, leaf_key="trim", casing_key=None, liner_key=None, frame_key="trim",
              sill_key=None, casing=None, casing_name=None, glass=True, vision=True,
              vision_rect=None, vision_name=None, hardware="auto", ds=0.02, recessed=None,
              lean=None):
    """One door kit v2 opening (module notes above): opening [u - w/2, u + w/2] (+ sidelights)
    x [zf, zf + h] on `wall`; returns the parts. casing_name defaults to `door_<name>_cas`
    (LOD1-deleted, the cs2_kit doors' jambs were); entry() passes `entry_<name>` (LOD1 kept,
    LOD2 deleted, the old surround's rule). ds = the leaf face depth in surface mode."""
    if DOOR_KIT["version"] != 2:
        raise RuntimeError("door_unit outside door_kit()")
    p = DOOR_FAMILIES[DOOR_KIT["family"]]
    n, _t = _SIDES[wall.side]
    full = (DOOR_KIT["mode"] == "full") if lean is None else not lean
    # HARDWARE GATE (Richard 2026-10-17: "It's good to render levers or doorknobs. Vanilla
    # buildings tend to have them"): every leaf ends with a pull / lever / knob part; the only
    # way out is an explicit, reasoned opt-out hardware="none:<reason>" (no silent off switch)
    if hardware == "auto":
        handle = True
    elif isinstance(hardware, str) and hardware.startswith("none:") and hardware[5:].strip():
        handle = False
    else:
        raise ValueError(f"door {name}: hardware={hardware!r}: use 'auto' or 'none:<reason>' "
                         f"(the opt-out needs a reason)")
    if handle and p["handle"] not in DOOR_HARDWARE_KINDS:
        raise RuntimeError(f"door {name}: family {DOOR_KIT['family']!r} has no hardware kind "
                           f"({p['handle']!r}); every door leaf needs a pull / lever / knob")
    if recessed is None:
        hz = DOOR_KIT["holes"]
        arr = DOOR_KIT["arrival"]
        recessed = hz == "all" or (hz == "arrival" and arr is not None
                                   and n.x * arr[0] + n.y * arr[1] > 0.7)
    groom = glass_room or room
    if (glass or vision) and groom is not None and ("_" in groom or not groom.isalnum()):
        raise ValueError(f"door {name}: glass room {groom!r} must be alphanumeric")
    cas = casing or p["casing"]
    CW, CP = p["casing_w"], p["casing_p"] if cas != "none" else 0.0
    HC = p.get("casing_head", CW)
    R = p["reveal"]
    if recessed:
        Dl, Dw = -R, 0.0
    else:
        Dl, Dw = ds, ds + R
    Dc = Dw + CP if cas != "none" else Dw + 0.01
    ckey = casing_key or "trim"
    lkey = liner_key or ckey
    skey = sill_key or ("timber_heavy" if p["sill"] == "slope" else "concrete")
    L = int(leaves or (2 if w >= p["double_min"] - 1e-9 else 1))
    sl = int(sidelights)
    mull, slw = p["sl_mull"], sidelight_w
    aL = u - w / 2 - ((mull + slw) if sl >= 1 else 0.0)
    aR = u + w / 2 + ((mull + slw) if sl >= 2 else 0.0)
    zt = zf + h
    has_glass = glass and p["leaf"] in ("glass", "half")
    transom = bool(has_glass and p["transom_min"] and h >= p["transom_min"] - 1e-9)
    leaf_h = p["leaf_h"] if transom else h
    bead = p["bead"] if full else 0.0
    dg = Dl - (bead if bead > 0 else DOOR_GLASS_LEAN)            # glass plane
    if p["sill"] == "plate":
        zl0 = zf + p["sill_t"]
    else:
        zl0 = zf + p["sill_rise"]
    parts = []
    names = []

    def add(pp):
        for o, _k in pp:
            names.append(o.name)
        parts.extend(pp)

    ov = 0.0 if bead > 0 else DOOR_GLASS_OVER

    def glass_pane(nm, a, b, z0_, z1_, over=None):
        if groom is None:
            raise ValueError(f"door {name}: glass needs a room")
        o_ = ov if over is None else over
        parts.extend(pane_quad(f"win_{groom}_{nm}", wall, a - o_, b + o_, z0_ - o_, z1_ + o_, d=dg))
        if recessed:
            DOOR2_GLASS_D.add(round(-dg, 4))

    # -- liner: two jamb sides out to the casing face + head soffit (no stool) ------------
    # The liner runs back past the glass plane (Dg): a sidelight / transom light meets the jamb
    # or the head directly, and a liner stopping at the leaf face left a slit to the sky between
    # it and the recessed glass (follow-up 2026-10-17, L1 left sidelight). Same tris.
    Dg = dg - 0.005
    g = _Geo()
    _wpoly(g, wall, [(aL, zf, Dg), (aL, zt, Dg), (aL, zt, Dc), (aL, zf, Dc)], (1, 0, 0))
    _wpoly(g, wall, [(aR, zf, Dg), (aR, zf, Dc), (aR, zt, Dc), (aR, zt, Dg)], (-1, 0, 0))
    _wpoly(g, wall, [(aL, zt, Dg), (aR, zt, Dg), (aR, zt, Dw), (aL, zt, Dw)], (0, -1, 0))
    add([(g.finish(f"reveal_d{name}" if recessed else f"door_{name}_lin"), lkey)])
    # -- casing ---------------------------------------------------------------------------
    cn = casing_name or f"door_{name}_cas"
    d0 = 0.0                                                   # outer sides back to the wall
    if cas == "u":
        g = _Geo()
        _wpoly(g, wall, [(aL - CW, zf, Dc), (aL, zf, Dc), (aL, zt, Dc), (aR, zt, Dc), (aR, zf, Dc),
                         (aR + CW, zf, Dc), (aR + CW, zt + HC, Dc), (aL - CW, zt + HC, Dc)], (0, 0, 1))
        _wpoly(g, wall, [(aL - CW, zf, d0), (aL - CW, zt + HC, d0), (aL - CW, zt + HC, Dc),
                         (aL - CW, zf, Dc)], (-1, 0, 0))
        _wpoly(g, wall, [(aR + CW, zf, d0), (aR + CW, zf, Dc), (aR + CW, zt + HC, Dc),
                         (aR + CW, zt + HC, d0)], (1, 0, 0))
        _wpoly(g, wall, [(aL - CW, zt + HC, d0), (aR + CW, zt + HC, d0), (aR + CW, zt + HC, Dc),
                         (aL - CW, zt + HC, Dc)], (0, 1, 0))
        add([(g.finish(cn), ckey)])
    elif cas == "posts":
        g = _Geo()
        for (ua, ub, sg) in ((aL - CW, aL, -1), (aR, aR + CW, 1)):
            _wpoly(g, wall, [(ua, zf, Dc), (ub, zf, Dc), (ub, zt, Dc), (ua, zt, Dc)], (0, 0, 1))
            uo = ua if sg < 0 else ub
            _wpoly(g, wall, [(uo, zf, d0), (uo, zt, d0), (uo, zt, Dc), (uo, zf, Dc)], (sg, 0, 0))
        add([(g.finish(cn), ckey)])
        HP = max(p["header_p"], Dc - Dw + 0.015) + (Dw if not recessed else 0.0)
        add(wall_box(f"{cn}hd" if cn.startswith("entry_") else f"door_{name}_hd", wall,
                     aL - CW - p["header_ear"], aR + CW + p["header_ear"], zt,
                     zt + p["header_h"], 0.0, HP, ckey, drop=("back",)))
    # -- leaves ---------------------------------------------------------------------------
    a0, a1 = u - w / 2, u + w / 2
    lw = (a1 - a0) / L
    zl1 = zf + leaf_h
    for k in range(L):
        la, lb = a0 + k * lw, a0 + (k + 1) * lw
        tag = f"{name}{k}" if L > 1 else name
        g = _Geo()
        if p["leaf"] == "glass":
            s, rt, rb = p["stile"], p["rail_top"], p["rail_bot"]
            ia, ib, iz0, iz1 = la + s, lb - s, zl0 + rb, zl1 - rt
            quads = [(la, ia, zl0, zl1), (ib, lb, zl0, zl1), (ia, ib, zl0, iz0), (ia, ib, iz1, zl1)]
            for (qa, qb, qz0, qz1) in quads:
                _wpoly(g, wall, [(qa, qz0, Dl), (qb, qz0, Dl), (qb, qz1, Dl), (qa, qz1, Dl)], (0, 0, 1))
            dstep = bead if (bead > 0 or not glass) else 0.0
            if not glass and dstep == 0.0:
                dstep = p["bead"]
            if dstep > 0:
                dp = Dl - dstep
                for (pa, pb, out) in (((ia, iz0), (ib, iz0), (0, 1, 0)), ((ib, iz0), (ib, iz1), (-1, 0, 0)),
                                      ((ib, iz1), (ia, iz1), (0, -1, 0)), ((ia, iz1), (ia, iz0), (1, 0, 0))):
                    _wpoly(g, wall, [(pa[0], pa[1], dp), (pb[0], pb[1], dp), (pb[0], pb[1], Dl),
                                     (pa[0], pa[1], Dl)], out)
            if glass:
                glass_pane(f"{name}g{k}", ia, ib, iz0, iz1)
            else:                                              # solid leaf: a sunk flat panel
                dp = Dl - dstep
                _wpoly(g, wall, [(ia, iz0, dp), (ib, iz0, dp), (ib, iz1, dp), (ia, iz1, dp)], (0, 0, 1))
        elif p["leaf"] == "half":
            s, rt, rl, rb = p["stile"], p["rail_top"], p["rail_lock"], p["rail_bot"]
            ia, ib = la + s, lb - s
            zlk0 = zl0 + p["lock_z"]
            zlk1 = zlk0 + rl
            quads = [(la, ia, zl0, zl1), (ib, lb, zl0, zl1), (ia, ib, zl1 - rt, zl1),
                     (ia, ib, zlk0, zlk1), (ia, ib, zl0, zl0 + rb)]
            if not glass:                                      # solid: the glass field is a panel too
                quads = quads
            for (qa, qb, qz0, qz1) in quads:
                _wpoly(g, wall, [(qa, qz0, Dl), (qb, qz0, Dl), (qb, qz1, Dl), (qa, qz1, Dl)], (0, 0, 1))
            # raised panel below the lock rail: the field between the stiles and rails stands
            # `panel_raise` proud with square sides (a shallow bevel shades smooth under the
            # 66 deg split and vanishes; the square edge keeps a crisp shadow line): 10 tris
            rz = p["panel_raise"]
            pz0, pz1 = zl0 + rb, zlk0
            O = [(ia, pz0), (ib, pz0), (ib, pz1), (ia, pz1)]
            outs = ((0, -1, 0), (1, 0, 0), (0, 1, 0), (-1, 0, 0))
            for i in range(4):
                j = (i + 1) % 4
                _wpoly(g, wall, [(O[i][0], O[i][1], Dl), (O[j][0], O[j][1], Dl),
                                 (O[j][0], O[j][1], Dl + rz), (O[i][0], O[i][1], Dl + rz)], outs[i])
            _wpoly(g, wall, [(O[0][0], O[0][1], Dl + rz), (O[1][0], O[1][1], Dl + rz),
                             (O[2][0], O[2][1], Dl + rz), (O[3][0], O[3][1], Dl + rz)], (0, 0, 1))
            gz0, gz1 = zlk1, zl1 - rt
            if bead > 0:
                dp = Dl - bead
                for (pa, pb, out) in (((ia, gz0), (ib, gz0), (0, 1, 0)), ((ib, gz0), (ib, gz1), (-1, 0, 0)),
                                      ((ib, gz1), (ia, gz1), (0, -1, 0)), ((ia, gz1), (ia, gz0), (1, 0, 0))):
                    _wpoly(g, wall, [(pa[0], pa[1], dp), (pb[0], pb[1], dp), (pb[0], pb[1], Dl),
                                     (pa[0], pa[1], Dl)], out)
            if glass:
                glass_pane(f"{name}g{k}", ia, ib, gz0, gz1)
            else:
                _wpoly(g, wall, [(ia, gz0, Dl), (ib, gz0, Dl), (ib, gz1, Dl), (ia, gz1, Dl)], (0, 0, 1))
        else:                                                  # steel
            uc_ = 0.5 * (la + lb)
            vr = vision_rect if vision_rect is not None else p["vision"]
            if vision and vr is not None:
                va, vb, vz0, vz1 = uc_ + vr[0], uc_ + vr[1], zf + vr[2], zf + vr[3]
                quads = [(la, va, zl0, zl1), (vb, lb, zl0, zl1), (va, vb, zl0, vz0), (va, vb, vz1, zl1)]
                for (qa, qb, qz0, qz1) in quads:
                    _wpoly(g, wall, [(qa, qz0, Dl), (qb, qz0, Dl), (qb, qz1, Dl), (qa, qz1, Dl)], (0, 0, 1))
                if bead > 0:
                    dp = Dl - bead
                    for (pa, pb, out) in (((va, vz0), (vb, vz0), (0, 1, 0)), ((vb, vz0), (vb, vz1), (-1, 0, 0)),
                                          ((vb, vz1), (va, vz1), (0, -1, 0)), ((va, vz1), (va, vz0), (1, 0, 0))):
                        _wpoly(g, wall, [(pa[0], pa[1], dp), (pb[0], pb[1], dp), (pb[0], pb[1], Dl),
                                         (pa[0], pa[1], Dl)], out)
                nm = vision_name if (vision_name and L == 1) else f"{name}v{k}"
                if groom is None:
                    raise ValueError(f"door {name}: vision panel needs a room")
                parts.extend(pane_quad(nm if nm.startswith("win_") else f"win_{groom}_{nm}", wall,
                                       va - ov, vb + ov, vz0 - ov, vz1 + ov, d=dg))
                if recessed:
                    DOOR2_GLASS_D.add(round(-dg, 4))
            else:
                _wpoly(g, wall, [(la, zl0, Dl), (lb, zl0, Dl), (lb, zl1, Dl), (la, zl1, Dl)], (0, 0, 1))
        add([(g.finish(f"door_{tag}_leaf"), leaf_key)])
        if p["leaf"] == "steel":                               # kick plate 3 mm proud
            add(pane_quad(f"door_{tag}_kick", wall, la + 0.03, lb - 0.03, zl0 + 0.02,
                          zl0 + 0.02 + p["kick_h"], d=Dl + 0.003, mat=p.get("kick_key", "concrete")))
        # handles
        if handle and p["handle"] == "pull":
            bw, bl, bz, so, bd = DOOR_PULL
            meet = lb if (L == 1 or k == 0) else la
            uc_ = meet - (p["stile"] / 2) * (1 if (L == 1 or k == 0) else -1)
            g = _Geo()
            ua, ub = uc_ - bw / 2, uc_ + bw / 2
            z0_, z1_ = zf + bz, zf + bz + bl
            df, db = Dl + so + bd, Dl + so
            _wpoly(g, wall, [(ua, z0_, df), (ub, z0_, df), (ub, z1_, df), (ua, z1_, df)], (0, 0, 1))
            _wpoly(g, wall, [(ua, z0_, db), (ua, z0_, df), (ua, z1_, df), (ua, z1_, db)], (-1, 0, 0))
            _wpoly(g, wall, [(ub, z0_, db), (ub, z1_, db), (ub, z1_, df), (ub, z0_, df)], (1, 0, 0))
            add([(g.finish(f"door_{tag}_pull"), "galv")])
        elif handle and p["handle"] == "lever":
            ll, lh, lz, so, ldp = DOOR_LEVER
            lz = p.get("lever_z", lz)
            # latch edge: a single leaf's high-u edge; a pair's meeting edge (each leaf its own
            # lever, pointing back toward its hinge side)
            if L == 1:
                ue, sgn = lb - p.get("stile", 0.16) / 2, -1.0
            elif k == 0:
                ue, sgn = lb - 0.06, -1.0
            else:
                ue, sgn = la + 0.06, 1.0
            # An L-shaped lever on a rose (follow-up 2, Richard: "School door handle looks like it
            # is floating"): the bar (front, top, bottom, free-end cap) runs from the latch end
            # `ue` toward the hinge; at the latch end a return leg (DOOR_LEVER_NECK wide) runs
            # back to the leaf face, so no gap; a rose plate (DOOR_ROSE, the EU house door's
            # 11 cm plate) sits on the leaf under it. 18 tris (DOOR_LEVER_TRIS), one part.
            uf = ue + sgn * ll                              # free end
            un = ue + sgn * DOOR_LEVER_NECK                 # inner face of the return leg
            z0_, z1_ = zf + lz, zf + lz + lh
            df, db = Dl + so + ldp, Dl + so
            dl_ = Dl + 0.001                                # the return leg's foot, on the leaf
            g = _Geo()
            _wpoly(g, wall, [(ue, z0_, df), (uf, z0_, df), (uf, z1_, df), (ue, z1_, df)], (0, 0, 1))
            for zz, out in ((z1_, (0, 1, 0)), (z0_, (0, -1, 0))):     # top / bottom: bar + leg
                _wpoly(g, wall, [(ue, zz, db), (uf, zz, db), (uf, zz, df), (ue, zz, df)], out)
                _wpoly(g, wall, [(ue, zz, dl_), (un, zz, dl_), (un, zz, db), (ue, zz, db)], out)
            _wpoly(g, wall, [(uf, z0_, db), (uf, z0_, df), (uf, z1_, df), (uf, z1_, db)], (sgn, 0, 0))
            _wpoly(g, wall, [(ue, z0_, dl_), (ue, z0_, df), (ue, z1_, df), (ue, z1_, dl_)], (-sgn, 0, 0))
            _wpoly(g, wall, [(un, z0_, dl_), (un, z0_, db), (un, z1_, db), (un, z1_, dl_)], (sgn, 0, 0))
            rw, rh, rd = DOOR_ROSE
            zc = 0.5 * (z0_ + z1_)
            ra, rb_ = ue - sgn * 0.3 * rw, ue + sgn * 0.7 * rw
            _wpoly(g, wall, [(ra, zc - rh / 2, Dl + rd), (rb_, zc - rh / 2, Dl + rd),
                             (rb_, zc + rh / 2, Dl + rd), (ra, zc + rh / 2, Dl + rd)], (0, 0, 1))
            add([(g.finish(f"door_{tag}_lever"), "ski_black")])
    DOOR2_LEAF[name] = Dl
    hw_parts = []
    for k in range(L):
        tag = f"{name}{k}" if L > 1 else name
        got = [f"door_{tag}_{kind}" for kind in DOOR_HARDWARE_KINDS if f"door_{tag}_{kind}" in names]
        if handle and not got:
            raise RuntimeError(f"door {name}: leaf {tag} has no hardware part (door_{tag}_pull / _lever "
                               f"/ _knob); pass hardware='none:<reason>' to opt out")
        hw_parts += got
    DOOR2_HARDWARE[name] = hw_parts if handle else hardware
    # -- sidelights: mullion + bottom panel (+ top rail without a transom), glass -----------
    sl_top = zf + leaf_h
    if sl:
        g = _Geo()
        dp = Dl - bead if bead > 0 else Dl
        for k in range(min(sl, 2)):
            sg = -1 if k == 0 else 1
            if sg < 0:
                m0, m1, g0, g1 = a0 - mull, a0, aL, a0 - mull
            else:
                m0, m1, g0, g1 = a1, a1 + mull, a1 + mull, aR
            zs0 = zf + p["sl_rail"]
            gz1 = sl_top if transom else zt - p.get("rail_top", 0.075)
            _wpoly(g, wall, [(m0, zf, Dl), (m1, zf, Dl), (m1, sl_top if transom else zt, Dl),
                             (m0, sl_top if transom else zt, Dl)], (0, 0, 1))
            _wpoly(g, wall, [(g0, zf, Dl), (g1, zf, Dl), (g1, zs0, Dl), (g0, zs0, Dl)], (0, 0, 1))
            if not transom:
                _wpoly(g, wall, [(g0, gz1, Dl), (g1, gz1, Dl), (g1, zt, Dl), (g0, zt, Dl)], (0, 0, 1))
            if bead > 0:
                mi = m1 if sg > 0 else m0                       # mullion face toward the glass
                _wpoly(g, wall, [(mi, zs0, dp), (mi, gz1, dp), (mi, gz1, Dl), (mi, zs0, Dl)],
                       (float(sg), 0, 0))
                _wpoly(g, wall, [(g0, zs0, Dl), (g1, zs0, Dl), (g1, zs0, dp), (g0, zs0, dp)], (0, 1, 0))
            parts.extend(pane_quad(f"win_{room}_{name}s{k}", wall, g0, g1, zs0, gz1, d=dg))
        add([(g.finish(f"door_{name}_sl"), frame_key)])
    # -- transom bar + light ------------------------------------------------------------------
    if transom:
        tb = p["transom_bar"]
        g = _Geo()
        _wpoly(g, wall, [(aL, sl_top, Dl), (aR, sl_top, Dl), (aR, sl_top + tb, Dl), (aL, sl_top + tb, Dl)],
               (0, 0, 1))
        _wpoly(g, wall, [(aL, sl_top + tb, dg), (aL, sl_top + tb, Dl), (aR, sl_top + tb, Dl),
                         (aR, sl_top + tb, dg)], (0, 1, 0))
        if sl:                                 # under the bar: closes the sidelights' top edge
            _wpoly(g, wall, [(aL, sl_top, dg), (aR, sl_top, dg), (aR, sl_top, Dl), (aL, sl_top, Dl)],
                   (0, -1, 0))
        add([(g.finish(f"door_{name}_tr"), frame_key)])
        glass_pane(f"{name}t", aL, aR, sl_top + tb, zt)
    # -- threshold ---------------------------------------------------------------------------
    sp = p["sill_p"]
    g = _Geo()
    if p["sill"] == "plate":
        st = p["sill_t"]
        _wpoly(g, wall, [(aL, zf + st, Dl), (aR, zf + st, Dl), (aR, zf + st, Dw + sp),
                         (aL, zf + st, Dw + sp)], (0, 0, 1))
        _wpoly(g, wall, [(aL, zf, Dw + sp), (aR, zf, Dw + sp), (aR, zf + st, Dw + sp),
                         (aL, zf + st, Dw + sp)], (0, 0, 1))
    else:                                                       # sloped timber wedge
        rise = p["sill_rise"]
        lip = 0.004
        _wpoly(g, wall, [(aL, zf + rise, Dl), (aR, zf + rise, Dl), (aR, zf + lip, Dw + sp),
                         (aL, zf + lip, Dw + sp)], (0, rise, sp))
        _wpoly(g, wall, [(aL, zf, Dw + sp), (aR, zf, Dw + sp), (aR, zf + lip, Dw + sp),
                         (aL, zf + lip, Dw + sp)], (0, 0, 1))
        if full:                                                # end caps outside the wall
            zw = zf + rise + (lip - rise) * (Dw - Dl) / (Dw + sp - Dl)
            for ue, sg in ((aL, -1), (aR, 1)):
                _wpoly(g, wall, [(ue, zf, Dw), (ue, zf, Dw + sp), (ue, zf + lip, Dw + sp), (ue, zw, Dw)],
                       (sg, 0, 0))
    add([(g.finish(f"door_{name}_sill"), skey)])
    for nm in names:
        DOOR2_BUILT[nm] = "kit"
    return parts


def entry_v2(name, wall, u, w, h, sidelights, canopy, lamps, room, z, sidelight_w, surround_mat,
             glass, glass_room):
    """entry() under door_kit(): the old surround's half width still sizes the canopy and
    places the lamps (byte-identical canopy / brackets / lanterns)."""
    zf = _lift(z)
    sl = int(sidelights)
    half = w / 2 + (sidelight_w + 0.15) * (1 if sl else 0) + 0.2
    if sl > 1:
        half = w / 2 + (sidelight_w + 0.15) + 0.2
    hs = h + 0.30
    fam = DOOR_KIT["family"]
    ckey = "timber_heavy" if fam == "chalet" else surround_mat if fam == "glazing" else "trim"
    parts = door_unit(name, wall, u, w, h, zf, room=room, glass_room=glass_room, sidelights=sl,
                      sidelight_w=sidelight_w, leaf_key="trim", casing_key=ckey,
                      liner_key=ckey, frame_key="trim", casing_name=f"entry_{name}", glass=glass)
    if canopy:
        depth, pitch = canopy
        tp = math.tan(math.radians(pitch))
        cw = half + 0.3
        z_e = zf + hs + 0.15
        ridge = z_e + tp * cw
        ax = "X" if wall.side in ("+X", "-X") else "Y"
        pa = wall_point(wall, u, 0, -0.1)
        pb = wall_point(wall, u, 0, depth)
        a0, a1 = (sorted((pa.x, pb.x)) if ax == "X" else sorted((pa.y, pb.y)))
        cpt = wall_point(wall, u, 0, 0)
        cc = cpt.y if ax == "X" else cpt.x
        for k, sg in enumerate((-1.0, 1.0)):
            parts += roof_slab(f"{name}canopy{k}", ax, a0, a1, cc + sg * cw, z_e,
                               cc - sg * 0.1, ridge + 0.1 * tp, 0.16, mat="metal_roof")
        for k, sg in enumerate((-1.0, 1.0)):
            at = wall_point(wall, u + sg * (cw - 0.3), z_e - 0.15, 0.0)
            parts += bracket(f"{name}canopy{k}", tuple(at), wall.side, span=depth - 0.2,
                             drop=min(1.2, depth))
    for k in range(int(lamps)):
        sg = -1 if k == 0 else 1
        at = wall_point(wall, u + sg * (half + 0.35), zf + 2.2, 0.0)
        lp, _pt = lantern(f"{name}{k}", tuple(at), wall.side, kind="wall")
        parts += lp
    return parts



def curtain_wall(name, wall, u0, u1, z0, z1, grid_u=1.6, transom_at=3.0, mullion=0.08,
                 room=None, per_storey_rooms=True):
    """Glazed wall: panes `win_<room><s>_<k>` on a grid ~grid_u wide, rows cut every
    transom_at m above z0 (no pane exceeds one storey: a pane is never cut by the floor
    slicer, PG-sk 9), mullions `cmull_<name>`, transoms + rails `ctran_<name>`."""
    room = room or name
    z0 = max(z0, GROUND_LIFT + 0.06)        # bottom rail clear of the engine ground band
    ncol = max(1, int(round((u1 - u0) / grid_u)))
    nrow = max(1, int(math.ceil((z1 - z0) / transom_at - 1e-6)))
    zs = [z0 + min(transom_at * r, z1 - z0) for r in range(nrow)] + [z1]
    parts = []
    for r in range(nrow):
        tag = f"{room}{r}" if per_storey_rooms else room
        for c in range(ncol):
            ua = u0 + (u1 - u0) * c / ncol
            ub = u0 + (u1 - u0) * (c + 1) / ncol
            parts += pane_quad(f"win_{tag}_{name}r{r}c{c}", wall, ua, ub, zs[r], zs[r + 1])
    face = PANE_OFFSET + 0.10
    mg = _Geo()
    for c in range(ncol + 1):
        um = u0 + (u1 - u0) * c / ncol
        lo, hi = _wall_aabb(wall, um - mullion / 2, um + mullion / 2, z0, z1, 0.0, face)
        mg.aabb(lo, hi, _wall_keys(wall, ("back",)))
    parts.append((mg.finish(f"cmull_{name}"), "trim"))
    tg = _Geo()
    for zt in zs:
        lo, hi = _wall_aabb(wall, u0 - mullion / 2, u1 + mullion / 2, zt - 0.06, zt + 0.06,
                            0.0, face - 0.01)
        tg.aabb(lo, hi, _wall_keys(wall, ("back",)))
    parts.append((tg.finish(f"ctran_{name}"), "trim"))
    return parts


def entry(name, wall, u, w=1.8, h=2.4, sidelights=0, canopy=None, lamps=2, room=None,
          z=0.0, sidelight_w=0.9, surround_mat="timber_heavy", glass=True, glass_room=None):
    """`entry_<name>` timber surround (0.26 deep: 0.18 proud, 0.08 buried; +0.2 each side;
    head 0.30 above the door), two cs2_kit.door leaves (`door_`, w/2 each), sidelight panes
    `win_<room>_<name>s<k>`, optional `canopy=(depth, pitch_deg)` gable porch
    (`roof_<name>canopy0/1`, `bracket_`), `lamps` lanterns (`lamp_`) at 2.2 m. z = floor
    height of the doorway; closed parts never start below GROUND_LIFT (engine band).
    Inside door_kit() (lodge.py --door-kit v2) this is entry_v2 (door kit v2: glass /
    glass_room apply there only; every v1 build is byte-identical)."""
    room = room or f"entry{name}"
    if DOOR_KIT["version"] == 2:
        return entry_v2(name, wall, u, w, h, sidelights, canopy, lamps, room, z, sidelight_w,
                        surround_mat, glass, glass_room)
    zf = _lift(z)
    n, _t = _SIDES[wall.side]
    sl = int(sidelights)
    half = w / 2 + (sidelight_w + 0.15) * (1 if sl else 0) + 0.2
    if sl > 1:
        half = w / 2 + (sidelight_w + 0.15) + 0.2
    hs = h + 0.30
    parts = wall_box(f"entry_{name}", wall, u - half, u + half, zf, zf + hs, -0.08, 0.18,
                     surround_mat, drop=("back",))
    face = 0.18
    for k, du in enumerate((-w / 4, w / 4)):
        p = wall_point(wall, u + du, zf, face)
        parts += cs2_kit.door(f"{name}{k}", w=w / 2 - 0.02, h=h, loc=tuple(p),
                              facing=wall.side, quad=0)
    parts = [(o, m if isinstance(m, str) else "trim") for o, m in parts]
    for k in range(min(sl, 2)):
        sg = -1 if k == 0 else 1
        cu = u + sg * (w / 2 + 0.075 + sidelight_w / 2)
        parts += pane_quad(f"win_{room}_{name}s{k}", wall, cu - sidelight_w / 2,
                           cu + sidelight_w / 2, zf + 0.10, zf + h - 0.10, d=face + 0.03)
    if canopy:
        depth, pitch = canopy
        tp = math.tan(math.radians(pitch))
        cw = half + 0.3
        z_e = zf + hs + 0.15
        ridge = z_e + tp * cw
        ax = "X" if wall.side in ("+X", "-X") else "Y"
        pa = wall_point(wall, u, 0, -0.1)
        pb = wall_point(wall, u, 0, depth)
        a0, a1 = (sorted((pa.x, pb.x)) if ax == "X" else sorted((pa.y, pb.y)))
        cpt = wall_point(wall, u, 0, 0)
        cc = cpt.y if ax == "X" else cpt.x
        for k, sg in enumerate((-1.0, 1.0)):
            parts += roof_slab(f"{name}canopy{k}", ax, a0, a1, cc + sg * cw, z_e,
                               cc - sg * 0.1, ridge + 0.1 * tp, 0.16, mat="metal_roof")
        for k, sg in enumerate((-1.0, 1.0)):
            at = wall_point(wall, u + sg * (cw - 0.3), z_e - 0.15, 0.0)
            parts += bracket(f"{name}canopy{k}", tuple(at), wall.side, span=depth - 0.2,
                             drop=min(1.2, depth))
    for k in range(int(lamps)):
        sg = -1 if k == 0 else 1
        at = wall_point(wall, u + sg * (half + 0.35), zf + 2.2, 0.0)
        lp, _pt = lantern(f"{name}{k}", tuple(at), wall.side, kind="wall")
        parts += lp
    return parts


def sign_board(name, wall, u, z, w, h, t=0.08, mat="timber_lap", painter=None, frame=0.06):
    """`sign_<name>_face` = ONE quad at wall + t (stamp target, mat `label`, w 3.0) and
    `sign_<name>_frame` = board edges + a frame ring `frame` wide. z = bottom of the face.
    `painter` is recorded for lodge.make_post_compose (the stamp itself runs there)."""
    n, _t = _SIDES[wall.side]
    parts = pane_quad(f"sign_{name}_face", wall, u - w / 2, u + w / 2, z, z + h, d=t,
                      mat="label")
    if painter is not None:
        parts[0][0]["painter"] = getattr(painter, "__name__", str(painter))
    g = _Geo()
    fw = frame
    for (ua, ub, za, zb) in ((u - w / 2 - fw, u + w / 2 + fw, z - fw, z),
                             (u - w / 2 - fw, u + w / 2 + fw, z + h, z + h + fw),
                             (u - w / 2 - fw, u - w / 2, z, z + h),
                             (u + w / 2, u + w / 2 + fw, z, z + h)):
        lo, hi = _wall_aabb(wall, ua, ub, za, zb, 0.0, t + 0.03)
        g.aabb(lo, hi, _wall_keys(wall, ("back",)))
    parts.append((g.finish(f"sign_{name}_frame"), mat))
    return parts


def emblem_disc(name, wall, u, z, d, t=0.04, segs=16, painter=None):
    """`emblem_<name>_face` 16-gon centre fan (stamp target) + `emblem_<name>_rim` band.
    z = disc centre height."""
    n, tv = _SIDES[wall.side]
    c = wall_point(wall, u, z, t)
    r = d / 2

    def ring(off):
        return [wall_point(wall, u + r * math.cos(2 * math.pi * k / segs),
                           z + r * math.sin(2 * math.pi * k / segs), off) for k in range(segs)]

    def face(g):
        cv = g.v(c)
        rv = [g.v(p) for p in ring(t)]
        for k in range(segs):
            g.face([cv, rv[k], rv[(k + 1) % segs]], n)
    parts = _single(f"emblem_{name}_face", face, "label")
    if painter is not None:
        parts[0][0]["painter"] = getattr(painter, "__name__", str(painter))

    def rim(g):
        f = [g.v(p) for p in ring(t)]
        b = [g.v(p) for p in ring(0.0)]
        for k in range(segs):
            k1 = (k + 1) % segs
            mid = (f[k].co + f[k1].co) * 0.5
            g.face([f[k], b[k], b[k1], f[k1]], mid - c)
    parts += _single(f"emblem_{name}_rim", rim, "trim")
    return parts


def emblem_square(name, wall, u, z, w, h, t=0.04, painter=None):
    """`emblem_<name>_face` quad (stamp target) + `emblem_<name>_rim` edges. z = bottom."""
    parts = pane_quad(f"emblem_{name}_face", wall, u - w / 2, u + w / 2, z, z + h, d=t,
                      mat="label")
    if painter is not None:
        parts[0][0]["painter"] = getattr(painter, "__name__", str(painter))
    lo, hi = _wall_aabb(wall, u - w / 2, u + w / 2, z, z + h, 0.0, t)
    keys = _wall_keys(wall, ("front", "back"))
    parts += _single(f"emblem_{name}_rim", lambda g: g.aabb(lo, hi, keys), "trim")
    return parts


def stone_pier(name, x, y, w, d, z0, z1, cap=True, ground=True, mat="stone_rubble"):
    """`pier_<name>` box (open bottom at z0 = 0 when ground) + `plinthcap_<name>` 0.15."""
    gr = ground and z0 == 0.0
    parts = box_part(f"pier_{name}", (x - w / 2, y - d / 2, z0), (x + w / 2, y + d / 2, z1),
                     mat, drop=("+Z",) if cap else (), ground=gr)
    if cap:
        parts += box_part(f"plinthcap_{name}", (x - w / 2 - 0.05, y - d / 2 - 0.05, z1),
                          (x + w / 2 + 0.05, y + d / 2 + 0.05, z1 + 0.15), "concrete")
    return parts


def col(name, x, y, z0, z1, size=0.25, kind="square", ground=False, mat="timber_heavy",
        top_cap=True):
    """`col_<name>` square column or `logcol_<name>` 8-seg log (r = size/2, no bottom cap).
    NOT `post_` (cs2_kit.handrail owns that prefix)."""
    if ground and z0 != 0.0:
        raise ValueError(f"col {name}: ground columns start at z 0")
    if not ground:
        z0 = _lift(z0) if z0 < GROUND_LIFT else z0
    if kind == "square":
        return box_part(f"col_{name}", (x - size / 2, y - size / 2, z0),
                        (x + size / 2, y + size / 2, z1), mat,
                        drop=() if top_cap else ("+Z",), ground=ground)
    return _single(f"logcol_{name}",
                   lambda g: g.cyl((x, y, z0), (x, y, z1), size / 2, 8, cap0=False,
                                   cap1=top_cap), mat)


def beam(name, p0, p1, w, h, mat="timber_heavy"):
    """`beam_<name>` box between two points (w across, h up)."""
    return _single(f"beam_{name}", lambda g: _beam_into(g, p0, p1, w, h), mat)


def bracket(name, at, wall_normal, span=1.0, drop=1.0, t=0.15, curved=False,
            mat="timber_heavy"):
    """`bracket_<name>` knee brace under an overhang: `at` = top point on the wall face,
    a horizontal arm `span` out along `wall_normal` and a strut from `drop` below `at` to
    the arm end; curved -> a 4-segment glulam arc (one tube)."""
    n = _SIDES[wall_normal][0] if isinstance(wall_normal, str) else Vector(wall_normal)
    A = Vector(at)
    tip = A + n * span
    foot = A - Vector((0, 0, drop))
    g = _Geo()
    if curved:
        pts = []
        for k in range(5):
            a = 0.5 * math.pi * k / 4
            pts.append(foot + n * (span * (1 - math.cos(a))) + Vector((0, 0, drop * math.sin(a))))
        for p, q in zip(pts, pts[1:]):
            _beam_into(g, p, q, t, t)
    else:
        _beam_into(g, A + n * 0.02, tip, t, t)
        _beam_into(g, foot + n * 0.02, tip - n * 0.05, t, t)
    return [(g.finish(f"bracket_{name}"), mat)]


def truss_kingpost(name, wall, span, eave_z, pitch_deg, member=0.35, braces="curved",
                   d=0.25, mat="timber_heavy"):
    """L5 king-post truss in front of a gable face: `truss_<name>` = tie beam at eave_z,
    two rafters, king post, two braces (curved arcs or straight), all `d` m proud.
    The WallRef's (u0 + u1) / 2 is the truss centre."""
    uc = 0.5 * (wall.u0 + wall.u1)
    tp = math.tan(math.radians(pitch_deg))
    apex_z = eave_z + tp * span / 2
    g = _Geo()

    def P(u, z):
        return wall_point(wall, u, z, d)
    m = member
    _beam_into(g, P(uc - span / 2, eave_z), P(uc + span / 2, eave_z), m, m, up=_SIDES[wall.side][0])
    _beam_into(g, P(uc - span / 2, eave_z), P(uc, apex_z), m, m, up=_SIDES[wall.side][0])
    _beam_into(g, P(uc + span / 2, eave_z), P(uc, apex_z), m, m, up=_SIDES[wall.side][0])
    _beam_into(g, P(uc, eave_z), P(uc, apex_z), m, m * 0.9, up=_SIDES[wall.side][0])
    for sg in (-1, 1):
        a = P(uc, eave_z + 0.25 * (apex_z - eave_z))
        b = P(uc + sg * span / 4, eave_z + tp * span / 4 * 1.0)
        if braces == "curved":
            mid = (Vector(a) + Vector(b)) * 0.5 + Vector((0, 0, -0.25))
            _beam_into(g, a, mid, m * 0.7, m * 0.7, up=_SIDES[wall.side][0])
            _beam_into(g, mid, b, m * 0.7, m * 0.7, up=_SIDES[wall.side][0])
        else:
            _beam_into(g, a, b, m * 0.7, m * 0.7, up=_SIDES[wall.side][0])
    return [(g.finish(f"truss_{name}"), mat)]


# ======================================================================================
# Outdoor and dressing (section 4.4)
# ======================================================================================

def deck(name, x0, x1, y0, y1, z, support="col", support_every=3.2, support_to=0.0,
         fascia=0.30, board_dir="X", thick=0.16, mat="deck", soffit=False):
    """`deck_<name>` floor (top at z, `thick`), `deckfascia_<name>` band, supports `col_`
    / `pier_` along the perimeter every `support_every` (ground when support_to == 0;
    plinth mode: support_to = -D); support "solid" (on a base: bottom dropped) or "none"
    (caller places supports). board_dir "X"/"Y" -> obj["tile_rot"] (Planks021 boards run
    along u = world X on a deck top, measured: row-profile std 0.059 vs column 0.008).
    soffit=True (detail pass 1, design 5.2 P2 repack): the slab underside becomes its own
    part `soffit_deck<name>` (one quad, mat `mat`, soffit_ weight 0.25) instead of the
    deck_ slab's bottom face; ignored on a solid deck (no underside)."""
    solid = support == "solid"
    split = bool(soffit) and not solid
    parts = box_part(f"deck_{name}", (x0, y0, z - thick), (x1, y1, z), mat,
                     drop=("-Z",) if (solid or split) else ())
    parts[0][0]["tile_rot"] = 0 if board_dir.upper() == "X" else 90
    if split:
        zb = z - thick
        parts += _single(f"soffit_deck{name}",
                         lambda g: g.poly([(x0, y0, zb), (x1, y0, zb), (x1, y1, zb), (x0, y1, zb)],
                                          (0.0, 0.0, -1.0)), mat)
    if fascia:
        g = _Geo()
        zb = z - thick - fascia + 0.16
        for lo, hi in (((x0 - 0.04, y0 - 0.04, zb), (x1 + 0.04, y0, z - 0.02)),
                       ((x0 - 0.04, y1, zb), (x1 + 0.04, y1 + 0.04, z - 0.02)),
                       ((x0 - 0.04, y0, zb), (x0, y1, z - 0.02)),
                       ((x1, y0, zb), (x1 + 0.04, y1, z - 0.02))):
            g.aabb(lo, hi)
        parts.append((g.finish(f"deckfascia_{name}"), "trim"))
    if support not in ("solid", "none"):
        pts = []
        nx = max(1, int(math.ceil((x1 - x0) / support_every - 1e-6)))
        ny = max(1, int(math.ceil((y1 - y0) / support_every - 1e-6)))
        inset = 0.25
        for i in range(nx + 1):
            for j in range(ny + 1):
                if 0 < i < nx and 0 < j < ny:
                    continue
                pts.append((x0 + inset + (x1 - x0 - 2 * inset) * i / nx,
                            y0 + inset + (y1 - y0 - 2 * inset) * j / ny))
        ground = support_to == 0.0
        for k, (px, py) in enumerate(sorted(pts)):
            if support == "pier":
                parts += stone_pier(f"{name}{k}", px, py, 0.6, 0.6, support_to, z - thick,
                                    cap=False, ground=ground)
            else:
                parts += col(f"{name}{k}", px, py, support_to, z - thick, size=0.25,
                             ground=ground)
    return parts


def stair(name, foot, top, w=1.2, rise_max=0.18, run=0.28, stringers=True, rail="tube",
          ground=True, rail_sides=(-1, 1), tread_mat="deck", stringer_mat="trim"):
    """n = ceil(dz / rise_max) risers from `foot` (bottom of the first riser) to `top`
    (top nosing): `stair_<name>_treads` (0.05 thick, open risers), `stair_<name>_str0/1`
    stringers (feet open on z 0 when ground -> engine-base edges), rails per `rail`
    ("tube" cs2_kit.handrail / "timber" / "none") on `rail_sides` (+1 = right of the run).
    ground=False (plinth mode): the foot must sit at z <= -0.05 (below grade) or >= 0.02,
    stringer feet are closed (skeptic MUST-CHANGE 3). `run` is informative: the run is
    derived from the foot / top points."""
    F, T = Vector(foot), Vector(top)
    if ground and abs(F.z) > 1e-9:
        raise ValueError(f"stair {name}: ground stairs start at z 0")
    if not ground and -0.05 < F.z < GROUND_LIFT:
        raise ValueError(f"stair {name}: ground=False foot z {F.z} sits in the ground band "
                         f"(use <= -0.05 below grade or >= 0.02)")
    dz = T.z - F.z
    hd = Vector((T.x - F.x, T.y - F.y, 0.0))
    L = hd.length
    d = hd / L
    side = Vector((d.y, -d.x, 0.0))          # right of the run direction
    n = max(1, int(math.ceil(dz / rise_max - 1e-9)))
    rise, tread = dz / n, L / n
    g = _Geo()
    for k in range(n):
        a = F + d * (k * tread) + Vector((0, 0, (k + 1) * rise))
        b = a + d * tread
        c = (a + b) * 0.5 + Vector((0, 0, -0.025))
        g.obb(c, (d, side, Vector((0, 0, 1))), (0.5 * tread + 0.01, 0.5 * w, 0.025))
    parts = [(g.finish(f"stair_{name}_treads"), tread_mat)]
    if stringers:
        th = math.atan2(dz, L)
        hs = 0.25
        for k, sg in enumerate((-1, 1)):
            off = side * (sg * (w / 2 + 0.03))
            ac = hs * math.sin(th) + hs * math.cos(th) / max(dz / L, 1e-6)
            A = F + off
            B = F + off + d * ac
            C = F + off + d * (L + hs * math.sin(th)) + Vector((0, 0, dz - hs * math.cos(th)))
            D = T + off
            gg = _Geo()
            half = side * 0.03
            fa = [gg.v(p - half) for p in (A, B, C, D)]
            fb = [gg.v(p + half) for p in (A, B, C, D)]
            gg.face(fa, -side)
            gg.face(fb, side)
            cen = (A + B + C + D) * 0.25
            for i in range(4):
                j = (i + 1) % 4
                if i == 0 and ground:
                    continue            # open foot on grade: the engine-base edges
                mid = (fa[i].co + fa[j].co) * 0.5 + half
                gg.face([fa[i], fa[j], fb[j], fb[i]], mid - cen)
            parts.append((gg.finish(f"stair_{name}_str{k}"), stringer_mat))
    if rail and rail != "none":
        for sg in rail_sides:
            off = side * (sg * (w / 2 + 0.03))
            p0 = F + d * tread + Vector((0, 0, rise)) + off
            p1 = T + off
            parts += rail_path(f"stair{name}{'R' if sg > 0 else 'L'}", [tuple(p0), tuple(p1)],
                               kind="tube" if rail == "tube" else rail)
    return parts


def rail_path(name, path, h=1.10, kind="tube", gaps=(), post_every=None):
    """Alias of rail() (internal callers)."""
    return rail(name, path, h=h, kind=kind, gaps=gaps, post_every=post_every)


def _split_path(path, gaps):
    """Polyline pieces with the arc-length intervals `gaps` removed."""
    P = [Vector(p) for p in path]
    if not gaps:
        return [P]
    seg_s = [0.0]
    for a, b in zip(P, P[1:]):
        seg_s.append(seg_s[-1] + (b - a).length)
    total = seg_s[-1]

    def at(s):
        for i in range(len(P) - 1):
            if s <= seg_s[i + 1] + 1e-9:
                t = (s - seg_s[i]) / max(seg_s[i + 1] - seg_s[i], 1e-9)
                return P[i] + (P[i + 1] - P[i]) * t
        return P[-1]
    keep = []
    cur = 0.0
    for g0, g1 in sorted(gaps):
        if g0 > cur + 1e-6:
            keep.append((cur, g0))
        cur = max(cur, g1)
    if cur < total - 1e-6:
        keep.append((cur, total))
    pieces = []
    for s0, s1 in keep:
        pts = [at(s0)] + [P[i] for i in range(1, len(P) - 1) if s0 < seg_s[i] < s1] + [at(s1)]
        pieces.append(pts)
    return pieces


def rail(name, path, h=1.10, kind="tube", gaps=(), post_every=None):
    """Guard rail along a 3D polyline (floor-level points). tube = cs2_kit.handrail (h 1.10,
    knee 0.55, posts 1.5 m: `post_`, `rail_`, `kick_`); timber = base_lodge rail_run port
    (posts 0.07 sq, rails 0.09 x 0.06 at h - 0.08 and h - 0.54: `trail_`); glass = opaque
    `glassrail_` panels 0.02 thick (no alpha) + `trail_` cap; bars = 2 steel `bar_` + posts
    `trail_`; `gaps` = arc-length openings."""
    parts = []
    for pi, piece in enumerate(_split_path(path, gaps)):
        tag = f"{name}{pi}" if len(_split_path(path, gaps)) > 1 else name
        pts = [Vector((p.x, p.y, max(p.z, GROUND_LIFT))) for p in piece]
        if kind == "tube":
            kw = {} if post_every is None else {"post_every": post_every}
            for o, _q in cs2_kit.handrail(tag, [tuple(p) for p in pts], h=h, **kw):
                parts.append((o, "trim"))
            continue
        posts, rails = _Geo(), _Geo()
        pe = 2.05 if post_every is None else post_every
        for a, b in zip(pts, pts[1:]):
            dv = b - a
            L = dv.length
            nseg = max(1, int(round(L / pe)))
            for j in range(nseg + (1 if b is pts[-1] else 0)):
                q = a + dv * (j / nseg)
                ps = 0.07 if kind == "timber" else 0.10
                if kind != "glass":
                    posts.aabb((q.x - ps / 2, q.y - ps / 2, q.z), (q.x + ps / 2, q.y + ps / 2, q.z + h))
            dirv = dv / L
            sidev = Vector((dirv.y, -dirv.x, 0))
            if kind == "timber":
                for zz in (h - 0.08, h - 0.54):
                    c = (a + b) * 0.5 + Vector((0, 0, zz))
                    rails.obb(c, (dirv, sidev, Vector((0, 0, 1))), (L / 2, 0.045, 0.03))
            elif kind == "bars":
                for zz in (h - 0.04, 0.5 * h):
                    c = (a + b) * 0.5 + Vector((0, 0, zz))
                    rails.obb(c, (dirv, sidev, Vector((0, 0, 1))), (L / 2, 0.02, 0.02))
            elif kind == "glass":
                c = (a + b) * 0.5 + Vector((0, 0, 0.5 * (h - 0.05)))
                rails.obb(c, (dirv, sidev, Vector((0, 0, 1))), (L / 2, 0.01, 0.5 * (h - 0.05)))
                c2 = (a + b) * 0.5 + Vector((0, 0, h - 0.025))
                posts.obb(c2, (dirv, sidev, Vector((0, 0, 1))), (L / 2 + 0.03, 0.04, 0.025))
        if kind == "glass":
            parts.append((rails.finish(f"glassrail_{tag}"), "glass_dark"))
            parts.append((posts.finish(f"trail_{tag}cap"), "trim"))
        elif kind == "bars":
            parts.append((posts.finish(f"trail_{tag}p"), "timber_heavy"))
            parts.append((rails.finish(f"bar_{tag}"), "galv"))
        else:
            parts.append((posts.finish(f"trail_{tag}p"), "trim"))
            parts.append((rails.finish(f"trail_{tag}r"), "trim"))
    return parts


def balcony(name, wall, u0, u1, z, depth=1.2, rail="timber", brackets=True):
    """`balc_<name>` slab (0.2 thick, top at z) out of a wall + rail on the three outer
    edges + `bracket_` every 2.4 m underneath."""
    parts = wall_box(f"balc_{name}", wall, u0, u1, z - 0.2, z, 0.0, depth, "deck")
    p = [wall_point(wall, u0, z, 0.0), wall_point(wall, u0, z, depth - 0.05),
         wall_point(wall, u1, z, depth - 0.05), wall_point(wall, u1, z, 0.0)]
    parts += globals()["rail"](f"balc{name}", [tuple(v) for v in p], kind=rail)
    if brackets:
        nb = max(2, int(round((u1 - u0) / 2.4)) + 1)
        for k in range(nb):
            u = u0 + 0.2 + (u1 - u0 - 0.4) * k / (nb - 1)
            at = wall_point(wall, u, z - 0.2, 0.0)
            parts += bracket(f"balc{name}{k}", tuple(at), wall.side, span=depth - 0.15,
                             drop=0.9)
    return parts


def gazebo(name, cx, cy, z, size=5.0, post_h=3.0, pitch_deg=30.0, rail=True):
    """`gazebocol_<name>` 4 posts, `gazeboroof_<name>` hip (pyramid, overhang 0.4),
    timber rail with a 1.2 m opening on -X."""
    hs = size / 2
    parts = []
    gp = _Geo()
    for sx in (-1, 1):
        for sy in (-1, 1):
            px, py = cx + sx * (hs - 0.2), cy + sy * (hs - 0.2)
            gp.aabb((px - 0.12, py - 0.12, _lift(z)), (px + 0.12, py + 0.12, z + post_h))
    parts.append((gp.finish(f"gazebocol_{name}"), "timber_heavy"))
    spec = RoofSpec("hip", cx - hs, cx + hs, cy - hs, cy + hs, z + post_h + 0.12, pitch_deg,
                    overhang=0.4, thick=0.16, mat="slate")
    for o, m in hip_roof(f"gz{name}", spec, ridge_cap=(0.3, 0.1)):
        if o.name.startswith("roof_"):
            _rename(o, "gazeboroof_" + o.name[len("roof_"):])
        parts.append((o, m))
    if rail:
        zr = z
        path = [(cx - hs + 0.2, cy - 0.6, zr), (cx - hs + 0.2, cy - hs + 0.2, zr),
                (cx + hs - 0.2, cy - hs + 0.2, zr), (cx + hs - 0.2, cy + hs - 0.2, zr),
                (cx - hs + 0.2, cy + hs - 0.2, zr), (cx - hs + 0.2, cy + 0.6, zr)]
        parts += globals()["rail"](f"gz{name}", path, kind="timber")
    return parts


def porte_cochere(name, x0, x1, y0, y1, clear_h=4.5, piers=4, pier=(1.4, 1.6),
                  pitch_deg=40.0, truss=True):
    """Drive-through porch: `pier_<name>k` stone bases (ground), `col_<name>k` timber
    columns to clear_h, `beam_<name>` ring, gable roof `roof_porte<name>A/B` (ridge along
    X), king-post truss on the +X face. Clear 4.5 m >= [K] 4.1 m apparatus clearance."""
    pw, ph = pier
    parts = []
    if piers != 4:
        raise ValueError("porte_cochere: only the 4-corner layout is built")
    corners = [(x0 + pw / 2, y0 + pw / 2), (x1 - pw / 2, y0 + pw / 2),
               (x0 + pw / 2, y1 - pw / 2), (x1 - pw / 2, y1 - pw / 2)]
    for k, (px, py) in enumerate(corners):
        parts += stone_pier(f"{name}{k}", px, py, pw, pw, 0.0, ph, cap=True, ground=True)
        parts += col(f"{name}{k}", px, py, ph + 0.15, clear_h, size=0.45)
    bg = _Geo()
    zb = clear_h + 0.2
    for p, q in (((x0 + pw / 2, y0 + pw / 2), (x1 - pw / 2, y0 + pw / 2)),
                 ((x0 + pw / 2, y1 - pw / 2), (x1 - pw / 2, y1 - pw / 2)),
                 ((x0 + pw / 2, y0 + pw / 2), (x0 + pw / 2, y1 - pw / 2)),
                 ((x1 - pw / 2, y0 + pw / 2), (x1 - pw / 2, y1 - pw / 2))):
        _beam_into(bg, (p[0], p[1], zb), (q[0], q[1], zb), 0.4, 0.4)
    parts.append((bg.finish(f"beam_{name}"), "timber_heavy"))
    spec = RoofSpec("gable", x0, x1, y0, y1, clear_h + 0.6, pitch_deg, axis="X", overhang=0.6,
                    rake_over=0.4, mat="slate")
    parts += gable_roof(f"porte{name}", spec, tails=None, walls=False)
    if truss:
        parts += truss_kingpost(f"porte{name}", WallRef("+X", x1, y0, y1), (y1 - y0) - 0.4,
                                clear_h + 0.6, pitch_deg, member=0.3)
    return parts


def tower(name, cx, cy, size, eave_z, roof="hip", pitch_deg=15.0, wall_mat="stone_rubble",
          lantern=None, clock=None, cap_z=None, roof_mat="slate", overhang=0.5):
    """`tower_<name>` shaft (ground part, open bottom; wall_mat = str or ((z_top, mat), ...)
    bands -> `tower_<name>`, `tower_<name>b1`, ...), hip roof `roof_<name>`;
    lantern=(w, post, h, cap_pitch) -> `lanterncol_` posts, `lanterncap_` hip, `bell_`;
    clock=(d, z) -> `clock_<k>_face` discs on the four faces + `clockring_<name>`."""
    hs = size / 2
    x0, x1, y0, y1 = cx - hs, cx + hs, cy - hs, cy + hs
    bands = [(eave_z, wall_mat)] if isinstance(wall_mat, str) else list(wall_mat)
    parts = []
    z = 0.0
    for k, (zt, m) in enumerate(bands):
        nm = name if k == 0 else f"{name}b{k}"
        top = min(zt, eave_z)
        if k == 0:
            parts += box_part(f"tower_{nm}", (x0, y0, 0.0), (x1, y1, top), m, drop=("+Z",),
                              ground=True)
        else:
            parts += box_part(f"tower_{nm}", (x0, y0, z), (x1, y1, top), m, drop=("+Z", "-Z"))
        z = top
    spec = RoofSpec("hip", x0, x1, y0, y1, eave_z, pitch_deg, overhang=overhang, thick=0.2,
                    mat=roof_mat, trim="trim_white")
    parts += hip_roof(f"tower{name}", spec)
    top_z = roof_z(spec, cx, cy)
    if lantern:
        lw, post, lh, cpitch = lantern
        z_l0 = roof_z(spec, cx + lw / 2, cy + lw / 2) - 0.05
        gp = _Geo()
        for sx in (-1, 1):
            for sy in (-1, 1):
                px, py = cx + sx * (lw / 2 - post / 2), cy + sy * (lw / 2 - post / 2)
                gp.aabb((px - post / 2, py - post / 2, z_l0), (px + post / 2, py + post / 2, z_l0 + lh))
        parts.append((gp.finish(f"lanterncol_{name}"), "trim_white"))
        cs = RoofSpec("hip", cx - lw / 2, cx + lw / 2, cy - lw / 2, cy + lw / 2, z_l0 + lh + 0.1,
                      cpitch, overhang=0.25, thick=0.14, mat=roof_mat, trim="trim_white")
        for o, m in hip_roof(f"lt{name}", cs, ridge_cap=None, fascia=0.2):
            pre, rest = o.name.split("_", 1)
            _rename(o, f"lanterncap_{pre}{rest}")
            parts.append((o, m if pre == "roof" else "trim_white"))
        bz = z_l0 + lh * 0.55
        parts += _single(f"bell_{name}", lambda g: g.cyl((cx, cy, bz - 0.45), (cx, cy, bz + 0.15),
                                                          0.45, 10, r1=0.18), "galv")
        top_z = roof_z(cs, cx, cy)
    if cap_z is not None and cap_z > top_z + 0.2:
        parts += _single(f"lanterncap_{name}spire",
                         lambda g: g.cyl((cx, cy, top_z - 0.1), (cx, cy, cap_z), 0.08, 6),
                         "galv")
    if clock:
        d, zc = clock
        for k, side in enumerate(("+X", "+Y", "-X", "-Y")):
            plane = {"+X": x1, "-X": x0, "+Y": y1, "-Y": y0}[side]
            uc = {"+X": cy, "-X": -cy, "+Y": -cx, "-Y": cx}[side]
            ps = emblem_disc(f"clk{name}{k}", WallRef(side, plane), uc, zc, d, t=0.05, segs=16)
            for o, m in ps:
                _rename(o, f"clock_{name}{k}_face" if o.name.endswith("_face")
                        else f"clockring_{name}{k}")
                parts.append((o, m))
    return parts


def lantern(name, at, normal, kind="wall", h=0.45, w=0.25):
    """`lamp_<name>` lens box 0.25 x 0.25 x 0.45 (mat `lamp`, emissive) + `lamp_<name>arm`
    bracket. kind "wall": `at` = point on the wall at lens-centre height, lens 0.22 out
    along `normal`; "post": `at` = post top, lens sits on it. Returns (parts, lens centre)."""
    n = _SIDES[normal][0] if isinstance(normal, str) else Vector(normal).normalized()
    A = Vector(at)
    if kind == "post":
        c = A + Vector((0, 0, h / 2 + 0.04))
        parts = box_part(f"lamp_{name}", (c.x - w / 2, c.y - w / 2, c.z - h / 2),
                         (c.x + w / 2, c.y + w / 2, c.z + h / 2), "lamp")
        parts += box_part(f"lamp_{name}arm", (A.x - 0.06, A.y - 0.06, A.z),
                          (A.x + 0.06, A.y + 0.06, A.z + 0.04), "trim")
        return parts, tuple(c)
    c = A + n * 0.22
    c.z = max(c.z, GROUND_LIFT + h / 2)
    parts = box_part(f"lamp_{name}", (c.x - w / 2, c.y - w / 2, c.z - h / 2),
                     (c.x + w / 2, c.y + w / 2, c.z + h / 2), "lamp")
    g = _Geo()
    _beam_into(g, A + Vector((0, 0, h / 2 + 0.02)), c + Vector((0, 0, h / 2 + 0.02)), 0.05, 0.05)
    parts.append((g.finish(f"lamp_{name}arm"), "trim"))
    return parts, tuple(c)


def flag_mast(name, x, y, h=8.0, flag=(1.6, 1.0), *, z0=GROUND_LIFT, along="X", face=False,
              fine=False):
    """`flag_<name>` 6-seg mast r 0.05 from GROUND_LIFT (closed, not a ground part: it is
    deleted at LOD1) to h, `flagpanel_<name>` 0.01 slab (mat accent, slot 2).
    Detail pass 1 keywords (defaults = the geometry above, vertex for vertex):
      z0     foot of the mast (a deck top, or <= -0.05 below grade in plinth mode); the
             whole flag rises by z0 - GROUND_LIFT, so `h` stays the top height ON GRADE;
      along  "X" (default) | "Y": the axis the cloth runs along, out from the mast;
      face   True: the cloth is two LABEL quads instead of the accent slab -
             `emblem_<n>flagA_face` facing the front (+X when along "Y", -Y when along "X")
             and `emblem_<n>flagB_face` facing back, 5 mm either side of the cloth plane
             (stamp both). The kit keeps them to LOD2 (`emblem_`): a recipe that drops the
             mast at LOD1 lists `emblem_<n>flag` in lod1_extra (skeptic M8).
    Fine-detail pass (design B.1, opt-in, fine=True): the mast is 12-seg with a 10-tri top cap
    (no bottom cap: it stands on its foot) and a 6 x 3 ball finial (FINIAL_R) sitting on the cap;
    the cloth is unchanged. 58 tris instead of 20."""
    if str(along).upper() not in ("X", "Y"):
        raise ValueError(f"flag_mast {name}: along {along!r} (X | Y)")
    _check_foot(f"flag_mast {name}", z0)
    top = h + (z0 - GROUND_LIFT)
    if fine:
        def mast(g):
            g.cyl((x, y, z0), (x, y, top), 0.05, 12, cap0=False, cap1=True)
            _ball_into(g, (x, y, top + FINIAL_R * 0.85), FINIAL_R)
        parts = _single(f"flag_{name}", mast, "galv")
    else:
        parts = _single(f"flag_{name}", lambda g: g.cyl((x, y, z0), (x, y, top), 0.05, 6),
                        "galv")
    fw, fh = flag
    zc0, zc1 = top - 0.15 - fh, top - 0.15
    ay = str(along).upper() == "Y"
    if not face:
        if ay:
            parts += box_part(f"flagpanel_{name}", (x - 0.005, y + 0.06, zc0),
                              (x + 0.005, y + 0.06 + fw, zc1), "accent")
        else:
            parts += box_part(f"flagpanel_{name}", (x + 0.06, y - 0.005, zc0),
                              (x + 0.06 + fw, y + 0.005, zc1), "accent")
        return parts
    front = Vector((1.0, 0.0, 0.0)) if ay else Vector((0.0, -1.0, 0.0))
    run = Vector((0.0, 1.0, 0.0)) if ay else Vector((1.0, 0.0, 0.0))
    O = Vector((float(x), float(y), 0.0))
    for tag, sg in (("A", 1.0), ("B", -1.0)):
        c = O + front * (0.005 * sg)
        pts = [c + run * 0.06 + Vector((0, 0, zc0)), c + run * (0.06 + fw) + Vector((0, 0, zc0)),
               c + run * (0.06 + fw) + Vector((0, 0, zc1)), c + run * 0.06 + Vector((0, 0, zc1))]
        parts += _single(f"emblem_{name}flag{tag}_face",
                         lambda g, pts=pts, sg=sg: g.poly(pts, front * sg), "label")
    return parts


def umbrella(name, x, y, z, r=1.55):
    """base_lodge.py:757-770 port: `umbstand_`, `umbpole_`, `umbcanopy_` (8-gon cone,
    accent slot 2). z = deck top."""
    z = _lift(z) if z < GROUND_LIFT else z
    parts = _single(f"umbstand_{name}", lambda g: g.cyl((x, y, z), (x, y, z + 0.08), 0.24, 8,
                                                         cap0=False), "galv")
    parts += _single(f"umbpole_{name}", lambda g: g.cyl((x, y, z + 0.08), (x, y, z + 2.5), 0.04, 6,
                                                         cap0=False), "galv")
    parts += _single(f"umbcanopy_{name}",
                     lambda g: g.cyl((x, y, z + 2.18), (x, y, z + 2.80), 1.0 * r, 8, cap0=True,
                                     r1=0.06), "accent")
    return parts


def ski_rack(name, x, y, length=4.0, facing="+X", h=1.3, *, slots=None, w=None, mat="galv",
             along="Y", skis=(), ski_mat="accent", gear=False, z0=0.0, leg_to=None,
             bindings=False):
    """Two racks behind one name.
    Painted panel (default, the 2026-10 kit build): `skirack_<name>` frame (2 legs, top +
    bottom rails, from GROUND_LIFT) and `skirack_<name>_face` panel (ONE quad facing
    `facing`, stamped with paint_skis).
    Modelled A-frame (keyword `slots=` given; L1 revision 1, Richard's verdict "ski racks
    around the base - not a prop in the game so add it to the asset"): see ski_rack_frame
    (`length` / `facing` / `h` are ignored on that path; `gear`, `z0`, `leg_to` and
    `bindings` (detail pass 1) are passed on; the painted panel takes none of them)."""
    if slots is not None:
        return ski_rack_frame(name, x, y, slots=slots, w=w, mat=mat, along=along, skis=skis,
                              ski_mat=ski_mat, gear=gear, z0=z0, leg_to=leg_to,
                              bindings=bindings)
    if z0 != 0.0 or leg_to is not None or bindings:
        raise ValueError(f"ski_rack {name}: z0 / leg_to / bindings need the modelled rack "
                         f"(slots=)")
    n, t = _SIDES[facing]
    wall = WallRef(facing, (x if facing in ("+X", "-X") else y))
    u = (y if facing == "+X" else -y) if facing in ("+X", "-X") else (-x if facing == "+Y" else x)
    g = _Geo()
    for du in (-length / 2, length / 2):
        lo, hi = _wall_aabb(wall, u + du - 0.04, u + du + 0.04, GROUND_LIFT, h + 0.1, -0.04, 0.04)
        g.aabb(lo, hi)
    for zz in (0.25, h):
        lo, hi = _wall_aabb(wall, u - length / 2, u + length / 2, zz - 0.03, zz + 0.03, -0.03, 0.03)
        g.aabb(lo, hi)
    parts = [(g.finish(f"skirack_{name}"), "trim")]
    parts += pane_quad(f"skirack_{name}_face", wall, u - length / 2 + 0.06, u + length / 2 - 0.06,
                       0.3, h - 0.05, d=0.05, mat="label")
    return parts


SKI_SLABS = {"ski": (1.65, 0.15), "board": (1.50, 0.26)}    # (length, width): a ski PAIR


def _bar_into(g, p0, p1, t, across):
    """Square bar t x t from p0 to p1, one cross axis = `across`; end caps not built (they
    sit inside a rail / foot bar)."""
    P0, P1 = Vector(p0), Vector(p1)
    d = P1 - P0
    L = d.length
    d /= L
    n = d.cross(across).normalized()
    g.obb((P0 + P1) * 0.5, (d, across, n), (0.5 * L, 0.5 * t, 0.5 * t), drop=("+X", "-X"))


def ski_rack_frame(name, x, y, slots=8, w=None, mat="galv", along="Y", skis=(),
                   ski_mat="accent", h=1.20, spread=0.40, bar=0.035, pitch=0.20, gear=False,
                   *, z0=0.0, leg_to=None, bindings=False):
    """Freestanding A-frame ski rack (L1 revision 1: no vanilla ski-rack prop exists).
    Centre (x, y) on grade, long axis `along` ("X" | "Y").
    `skirack_<name>` (mat): top rail 0.05 square at `h`; one base rail per side at z 0.35,
    `spread` out; slots + 1 inclined divider bars per side (`bar` square) at `pitch` = 0.20 m
    slot spacing from the base rail to the top rail (the two sides meet there: an A at every
    divider); the two end dividers run on down to a foot bar on grade (the legs).
    `skirack_<name>skis` (`ski_mat`, accent = slot 2), only when `skis` is given: one slab
    per (slot, side, kind), slot 0..slots-1, side +1 / -1, kind in SKI_SLABS ("ski" = a pair
    0.15 x 1.65, "board" 0.26 x 1.50), foot on grade 0.62 out, leaning on the top rail,
    outside the divider plane (a board may overhang its slot, so the neighbouring slots on
    its side must be empty: asserted).
    w defaults to slots x pitch + 0.10 (8 slots: 1.70 m); a given w sets the pitch (>= 0.16).
    Closed parts from GROUND_LIFT, not ground parts (asserted); bar end caps and the foot
    bars' undersides are not built. The `skirack_` prefix puts both parts on LOD1_DELETE
    (LOD0 only) and on the cull's protect list (LABEL_PREFIXES).
    gear=True (detail pass 1, Richard: "actual skis/boards on the racks instead of wooden
    planks"): `skis` entries are (slot, side, kind[, colour[, length]]), kind in GEAR_KINDS
    (a ski PAIR base to base, a snowboard, a pole pair), colour a matkey or a (body, tip) /
    (top, stripe) pair (default ski_mat), length default GEAR_LEN; the same foot / top-rail
    lean as the slabs, the back face on that line; occupancy GEAR_W (a board's neighbours on
    its side may hold skis / poles, not a board: asserted). Parts `skirack_<name>g<code>`,
    one per matkey (GEAR_CODE), instead of `skirack_<name>skis`.
    bindings (default False, Richard 2026-10-11: "I don't think we need to render the
    bindings. They just come out as blocks and looks bad"): the ski pairs and boards carry NO
    binding blocks. True restores the detail-pass-1 geometry (galv blocks on the skis, base
    plates + highbacks on the boards) and exists for tests only. An item that cannot reach
    the lean point (shorter than rack_reach(): 1.33 m on the default rack) stops in mid-air:
    it is reported on stdout, not refused (L5's pole pairs still do that).
    Detail pass 1 keywords (defaults = the geometry above, vertex for vertex):
    z0 = the surface the rack stands on (a deck top); the feet go to max(z0, GROUND_LIFT)
    and the whole rack rises by that - GROUND_LIFT. leg_to (plinth mode, <= -0.05): the four
    end-leg feet run on down to leg_to as square posts (the rack stands on unflattened
    ground; no vertex in the ground band)."""
    ax = str(along).upper()
    if ax not in ("X", "Y"):
        raise ValueError(f"ski_rack {name}: along {along!r} (X | Y)")
    if leg_to is not None and float(leg_to) > -0.05:
        raise ValueError(f"ski_rack {name}: leg_to {leg_to} must be <= -0.05 (below grade)")
    shift = max(float(z0), GROUND_LIFT) - GROUND_LIFT
    slots = int(slots)
    if slots < 1:
        raise ValueError(f"ski_rack {name}: slots {slots}")
    if w is None:
        w = slots * pitch + 0.10
    pitch = (w - 0.10) / slots
    if pitch < bar + 0.12:
        raise ValueError(f"ski_rack {name}: slot pitch {pitch:.3f} m < {bar + 0.12:.3f}")
    A = Vector((0.0, 1.0, 0.0)) if ax == "Y" else Vector((1.0, 0.0, 0.0))
    C = Vector((A.y, -A.x, 0.0))                    # across, horizontal
    Z = Vector((0.0, 0.0, 1.0))
    O = Vector((float(x), float(y), shift))

    def P(s, c, z):
        return O + A * s + C * c + Z * z
    zb = 0.35                                       # base rail centre
    k = spread / (h - zb)                           # divider: c lost per metre of z
    z_leg = GROUND_LIFT + bar / 2                   # leg foot inside the foot bar
    c_leg = spread + (zb - z_leg) * k
    s_end = w / 2 - 0.05
    fr = _Geo()
    fr.obb(P(0.0, 0.0, h), (A, C, Z), (w / 2, 0.025, 0.025))
    for sg in (-1, 1):
        fr.obb(P(0.0, sg * spread, zb), (A, C, Z), (s_end, bar / 2, bar / 2))
        fr.obb(P(sg * s_end, 0.0, GROUND_LIFT + bar / 2), (C, A, Z),
               (c_leg + 0.04, 0.025, bar / 2), drop=("-Z",))
    for i in range(slots + 1):
        s = -s_end + 2.0 * s_end * i / slots
        end = i in (0, slots)
        for sg in (-1, 1):
            p0 = P(s, sg * (c_leg if end else spread), z_leg if end else zb)
            _bar_into(fr, p0, P(s, 0.0, h), bar, A)
    floor = GROUND_LIFT + shift
    if leg_to is not None:
        floor = float(leg_to)
        for ss in (-1, 1):
            for sg in (-1, 1):
                c = P(ss * s_end, sg * c_leg, 0.0)
                zt = GROUND_LIFT + shift + bar / 2
                fr.obb((c.x, c.y, 0.5 * (floor + zt)), (A, C, Z),
                       (bar / 2, bar / 2, 0.5 * (zt - floor)), drop=("+Z",))
    zmin = min(v.co.z for v in fr.bm.verts)
    if zmin < floor - 1e-6:
        fr.bm.free()
        raise ValueError(f"ski_rack {name}: frame reaches z {zmin:.4f} < GROUND_LIFT")
    parts = [(fr.finish(f"skirack_{name}"), mat)]
    if not skis:
        return parts
    if gear:
        ents = [_gear_entry(e, ski_mat) for e in skis]
        taken = {}
        for j, sg, kind, _colour, L in ents:
            if not (0 <= j < slots) or sg not in (-1, 1) or kind not in GEAR_KINDS or L <= 0.0:
                raise ValueError(f"ski_rack {name}: bad gear entry {(j, sg, kind, L)!r}")
            sc = -s_end + (j + 0.5) * pitch
            span = (sc - GEAR_W[kind] / 2, sc + GEAR_W[kind] / 2)
            for (a0, a1) in taken.get(sg, ()):
                if span[0] < a1 - 1e-6 and a0 < span[1] - 1e-6:
                    raise ValueError(f"ski_rack {name}: {(j, sg, kind)} overlaps a neighbour")
            taken.setdefault(sg, []).append(span)
        G = _GeoSet()
        short = []
        for j, sg, kind, colour, L in ents:
            sc = -s_end + (j + 0.5) * pitch
            foot = P(sc, sg * (c_leg + 0.075), 0.035)
            lean = P(sc, sg * 0.06, h + 0.04) - foot
            d = lean.normalized()
            n = d.cross(A).normalized()
            if n.dot(C * sg) < 0.0:
                n = -n
            if L < lean.length - 1e-6:
                short.append((j, int(sg), kind, round(L, 3)))
            _gear_into(G, kind, foot + n * GEAR_BACK[kind], d, A, n, L, colour, bindings)
        if short:
            print(f"[kit] ski_rack {name}: {len(short)} gear item(s) shorter than the "
                  f"{rack_reach(h, spread, bar):.3f} m lean (stop in mid-air): {short}")
        return parts + G.finish(f"skirack_{name}g")
    taken = {}
    for j, sg, kind in skis:
        if not (0 <= int(j) < slots) or sg not in (-1, 1) or kind not in SKI_SLABS:
            raise ValueError(f"ski_rack {name}: bad ski entry {(j, sg, kind)!r}")
        L, wid = SKI_SLABS[kind]
        sc = -s_end + (int(j) + 0.5) * pitch
        span = (sc - wid / 2, sc + wid / 2)
        for (a0, a1) in taken.get(sg, ()):
            if span[0] < a1 - 1e-6 and a0 < span[1] - 1e-6:
                raise ValueError(f"ski_rack {name}: slab {(j, sg, kind)} overlaps a neighbour")
        taken.setdefault(sg, []).append(span)
    sk = _Geo()
    for j, sg, kind in skis:
        L, wid = SKI_SLABS[kind]
        sc = -s_end + (int(j) + 0.5) * pitch
        foot = P(sc, sg * (c_leg + 0.075), 0.035)
        d = (P(sc, sg * 0.06, h + 0.04) - foot).normalized()
        n = d.cross(A).normalized()
        sk.obb(foot + d * (L / 2), (d, A, n), (L / 2, wid / 2, 0.015), drop=("-X",))
    zmin = min(v.co.z for v in sk.bm.verts)
    if zmin < GROUND_LIFT - 1e-6:
        sk.bm.free()
        raise ValueError(f"ski_rack {name}: skis reach z {zmin:.4f} < GROUND_LIFT")
    parts.append((sk.finish(f"skirack_{name}skis"), ski_mat))
    return parts


def rack_reach(h=1.20, spread=0.40, bar=0.035):
    """Length (m) a gear item needs to span ski_rack_frame's lean, from its foot (c_leg +
    0.075 out, z 0.035 above the rack's base) to the lean point at the top rail (0.06 out,
    h + 0.04): 1.33 m on the default rack. A shorter item stops in mid-air (detail pass 1
    integration F1: L1's and L2's pole pairs were 1.18-1.28 m)."""
    zb = 0.35                                       # base rail centre, as ski_rack_frame
    c_leg = spread + (zb - (GROUND_LIFT + bar / 2)) * (spread / (h - zb))
    return math.hypot(c_leg + 0.075 - 0.06, h + 0.04 - 0.035)


# --------------------------------------------------------------------------------------
# Detail pass 1 (L1, Richard's second verdict 2026-10-11: "actual skis/boards on the racks
# instead of wooden planks, put our icon on it etc."). Additive: real ski gear (ski pairs,
# snowboards, pole pairs) for ski_rack_frame(gear=True) and free-standing ski_gear; the
# painted_board sign (stamp target with a texel-density override); bench, litter_bin and
# string_lights. Nothing above changes for a caller that does not use them.
# --------------------------------------------------------------------------------------

GEAR_KINDS = ("ski", "board", "poles")
# default lengths (m); a pole pair must span the rack lean (rack_reach() 1.33 m) or it stops
# in mid-air: 1.38 (was 1.20, detail pass 1 integration F1; explicit lengths are untouched)
GEAR_LEN = {"ski": 1.70, "board": 1.52, "poles": 1.38}
GEAR_W = {"ski": 0.10, "board": 0.29, "poles": 0.08}        # rack slot occupancy across (m)
GEAR_BACK = {"ski": 0.021, "board": 0.007, "poles": 0.012}  # centre plane -> back face (m)
GEAR_CODE = {"accent": "A", "trim_white": "W", "ski_black": "K", "ski_blue": "B",
             "ski_red": "R", "galv": "G"}                  # part-name code per matkey
SKI_TH = 0.018              # a ski PAIR stands base to base: each top sheet 18 mm off the plane
# one ski, (s / L, half width, curl): square tail, waist (sidecut), shovel, curled round tip
SKI_STATIONS = ((0.00, 0.041, 0.0), (0.45, 0.033, 0.0), (0.80, 0.049, 0.0),
                (0.90, 0.044, 0.012), (0.965, 0.030, 0.040), (1.00, 0.011, 0.060))
SKI_TIP_FROM = 2            # stations from this index: tip colour + the exposed (curled) base
# (bindings are NOT drawn by default - Richard 2026-10-11 "they just come out as blocks and
# looks bad"; SKI_BINDING / BOARD_BINDINGS below serve bindings=True, kept for a test)
SKI_BINDING = (0.36, 0.60, 0.036, 0.050)    # (s0 / L, s1 / L, half width, height)
# snowboard, (s / L, half width, kick toward the top sheet): rounded tail / nose, waist
BOARD_STATIONS = ((0.00, 0.070, 0.050), (0.035, 0.128, 0.030), (0.11, 0.146, 0.0),
                  (0.50, 0.124, 0.0), (0.89, 0.146, 0.0), (0.965, 0.128, 0.030),
                  (1.00, 0.070, 0.050))
BOARD_TH = 0.006
BOARD_STRIPES = ((0.16, 0.24), (0.74, 0.84))   # top-sheet graphic bands (s / L)
BOARD_BINDINGS = (0.33, 0.67)                  # binding centres (s / L)
POLE_R = 0.011


def _hw_at(stations, s):
    """(half width, curl) of a station table at s / L (linear between stations)."""
    for (s0, h0, c0), (s1, h1, c1) in zip(stations, stations[1:]):
        if s0 - 1e-9 <= s <= s1 + 1e-9:
            f = (s - s0) / max(s1 - s0, 1e-9)
            return h0 + (h1 - h0) * f, c0 + (c1 - c0) * f
    return stations[-1][1], stations[-1][2]


def _strip_into(g, O, d, a, n, L, stations, off, cs, face_n):
    """One surface of a ski / board: per station (s, hw, curl) the cross pair
    O + d s L + n (off + cs curl) -+ a hw; one quad per station gap facing `face_n`."""
    rows = []
    for (s, hw, curl) in stations:
        c = O + d * (s * L) + n * (off + cs * curl)
        rows.append((g.v(c - a * hw), g.v(c + a * hw)))
    for (l0, r0), (l1, r1) in zip(rows, rows[1:]):
        g.face([l0, r0, r1, l1], face_n)


def _ski_pair_into(G, O, d, a, n, L, colour, bindings=False):
    """A ski PAIR base to base (as it stands in a rack slot): per ski the top sheet (body
    colour to the shovel, tip colour beyond) and the curled tip's black base (the V of the
    two tips). colour = matkey | (body, tip). bindings=True adds the detail-pass-1 binding
    block (galv) per ski; the default draws none (Richard, 2026-10-11)."""
    body, tip = (colour, colour) if isinstance(colour, str) else tuple(colour)
    k = SKI_TIP_FROM
    s0, s1, bw, bh = SKI_BINDING
    for sg in (1, -1):                      # front ski (+n) and back ski (-n)
        off = sg * SKI_TH
        _strip_into(G(body), O, d, a, n, L, SKI_STATIONS[:k + 1], off, sg, n * sg)
        _strip_into(G(tip), O, d, a, n, L, SKI_STATIONS[k:], off, sg, n * sg)
        _strip_into(G("ski_black"), O, d, a, n, L, SKI_STATIONS[k:], sg * 0.003, sg, -n * sg)
        if bindings:
            c = O + d * (0.5 * (s0 + s1) * L) + n * (off + sg * bh / 2)
            G("galv").obb(c, (d, a, n * sg), (0.5 * (s1 - s0) * L, bw, bh / 2), drop=("-Z",))


def _board_into(G, O, d, a, n, L, colour, bindings=False):
    """A snowboard, base to the support: top sheet (+n), black base (-n), nose / tail kick
    toward the top, two graphic bands proud of the top sheet. colour = matkey | (top,
    stripe). bindings=True adds the detail-pass-1 pair (base plate + highback; black, galv
    on a black board); the default draws none (Richard, 2026-10-11)."""
    top, stripe = (colour, None) if isinstance(colour, str) else tuple(colour)
    _strip_into(G(top), O, d, a, n, L, BOARD_STATIONS, BOARD_TH, 1, n)
    _strip_into(G("ski_black"), O, d, a, n, L, BOARD_STATIONS, -BOARD_TH, 1, -n)
    if stripe:
        for b0, b1 in BOARD_STRIPES:
            h0, _c0 = _hw_at(BOARD_STATIONS, b0)
            h1, _c1 = _hw_at(BOARD_STATIONS, b1)
            off = n * (BOARD_TH + 0.003)
            P0, P1 = O + d * (b0 * L) + off, O + d * (b1 * L) + off
            G(stripe).poly([P0 - a * (0.97 * h0), P0 + a * (0.97 * h0), P1 + a * (0.97 * h1),
                            P1 - a * (0.97 * h1)], n)
    if not bindings:
        return
    bmat = "galv" if top == "ski_black" else "ski_black"
    for sb in BOARD_BINDINGS:
        c = O + d * (sb * L)
        G(bmat).obb(c + n * (BOARD_TH + 0.03), (d, a, n), (0.06, 0.12, 0.03), drop=("-Z",))
        G(bmat).obb(c + a * 0.105 + n * (BOARD_TH + 0.09), (d, a, n), (0.08, 0.015, 0.09),
                    drop=("-Z",))


def _poles_into(G, O, d, a, n, L, colour):
    """A pole pair: 4-sided galv shafts, grips in `colour`, black baskets 0.10 m up."""
    grip = colour if isinstance(colour, str) else colour[0]
    for sa in (-0.022, 0.022):
        p0 = O + a * sa
        p1 = p0 + d * L
        G("galv").cyl(p0, p1 - d * 0.16, POLE_R, 4, cap0=False, cap1=False, phase=math.pi / 4)
        G(grip).obb(p1 - d * 0.08, (d, a, n), (0.08, 0.018, 0.018))
        bc = p0 + d * 0.10
        G("ski_black").poly([bc + a * 0.04, bc + n * 0.04, bc - a * 0.04, bc - n * 0.04], d)


def _gear_into(G, kind, O, d, a, n, L, colour, bindings=False):
    if kind == "ski":
        _ski_pair_into(G, O, d, a, n, L, colour, bindings)
    elif kind == "board":
        _board_into(G, O, d, a, n, L, colour, bindings)
    elif kind == "poles":
        _poles_into(G, O, d, a, n, L, colour)
    else:
        raise ValueError(f"gear kind {kind!r} (have {GEAR_KINDS})")


class _GeoSet:
    """One _Geo per matkey; finish() -> parts `<base><code>` in code order, each checked to
    start at z >= GROUND_LIFT (closed parts, not ground parts)."""

    def __init__(self):
        self.g = {}

    def __call__(self, mat):
        if mat not in BLDG_MATERIALS:
            raise ValueError(f"gear matkey {mat!r} not in BLDG_MATERIALS")
        return self.g.setdefault(mat, _Geo())

    def finish(self, base):
        parts = []
        keys = sorted(self.g, key=lambda m: GEAR_CODE.get(m, m))
        for mat in keys:
            zmin = min(v.co.z for v in self.g[mat].bm.verts)
            if zmin < GROUND_LIFT - 1e-6:
                for m2 in keys:
                    self.g[m2].bm.free()
                raise ValueError(f"{base}: gear reaches z {zmin:.4f} < GROUND_LIFT")
        for mat in keys:
            parts.append((self.g[mat].finish(f"{base}{GEAR_CODE.get(mat, mat)}"), mat))
        return parts


def _gear_entry(e, ski_mat):
    """(slot, side, kind[, colour[, length]]) -> (slot, side, kind, colour, length)."""
    j, sg, kind = int(e[0]), e[1], e[2]
    colour = e[3] if len(e) > 3 and e[3] is not None else ski_mat
    L = float(e[4]) if len(e) > 4 and e[4] is not None else GEAR_LEN.get(kind, 0.0)
    return j, sg, kind, colour, L


def ski_gear(name, items, *, bindings=False):
    """Free-standing real gear leaning on something (detail pass 1: the "leaning singles"):
    items = ((kind, foot, lean_to, colour, length), ...), kind in GEAR_KINDS. `foot` = the
    tail / basket end on grade (z >= GROUND_LIFT + 0.01), `lean_to` = the point of the
    support (post face, wall) the item's BACK touches; the width runs horizontally across
    the lean, the top sheet faces away from the support. Parts
    `skirack_<name>g<code>` (one per matkey): LOD0 only and cull-protected, as the racks.
    bindings=False (default): no binding blocks on skis or boards, here or on any rack
    (Richard, 2026-10-11); True is the detail-pass-1 geometry, for tests only."""
    G = _GeoSet()
    for kind, foot, lean, colour, L in items:
        F, T = Vector(foot), Vector(lean)
        if (T - F).length > L + 1e-6:
            raise ValueError(f"ski_gear {name}: a {kind} of {L} m cannot reach its support")
        d = (T - F).normalized()
        hz = Vector((F.x - T.x, F.y - T.y, 0.0))
        if hz.length < 1e-6:
            raise ValueError(f"ski_gear {name}: a {kind} stands vertical (no lean)")
        a = Vector((0.0, 0.0, 1.0)).cross(hz).normalized()
        n = d.cross(a).normalized()
        if n.dot(hz) < 0.0:
            n = -n
        _gear_into(G, kind, F + n * GEAR_BACK[kind], d, a, n, float(L), colour, bindings)
    return G.finish(f"skirack_{name}g")


def painted_board(name, wall, u, z, w, h, t=0.06, frame=0.06, mat="trim", density_w=None,
                  painter="paint_glyph"):
    """sign_board(...) whose `_face` quad carries obj["w_override"] = density_w (the texel
    density weight part_weights uses instead of the `_face` rule's 3.0: the resort-glyph sign
    ships at >= 400 px/m) and obj["painter"]. The stamp itself is a recipe label entry
    (face prefix, "paint_glyph", {"face": ..., "kind": ...}) run by lodge.make_post_compose.
    `wall` = WallRef or (side, plane) (recipe data stays bpy-free)."""
    if not isinstance(wall, WallRef):
        wall = WallRef(*wall)
    parts = sign_board(name, wall, u, z, w, h, t=t, mat=mat, frame=frame)
    face = parts[0][0]
    if density_w is not None:
        face["w_override"] = float(density_w)
    face["painter"] = str(painter)
    return parts


BENCH_SEAT_SLATS, BENCH_BACK_SLATS, BENCH_GAP = 4, 3, 0.012     # fine bench planking


def bench(name, x, y, facing, length=1.6, mat="timber_heavy", leg_mat="galv", *,
          z0=GROUND_LIFT, back=True, fine=False):
    """Porch bench, back to a wall: the sitter faces `facing`; (x, y) = the back line's
    centre (the wall face). `bench_<name>` seat + backrest (mat), `bench_<name>legs` two end
    frames (leg_mat); from GROUND_LIFT. Not on LOD1_DELETE: a recipe lists `bench_` in
    lod1_extra.
    Detail pass 1 keywords (skeptic M1: the one bench; defaults = the geometry above):
    z0 = the leg feet (a deck top; the whole bench rises by z0 - GROUND_LIFT), back=False
    leaves the backrest out (a free-standing bench under a sign board).
    fine=True (design B.1 / the task's "planked bench where still modelled"): the seat is
    BENCH_SEAT_SLATS slats and the backrest BENCH_BACK_SLATS slats with BENCH_GAP gaps over the
    same envelope (boxes, no bevel strips); legs unchanged."""
    _check_foot(f"bench {name}", z0)
    n, t = _SIDES[facing]
    Z = Vector((0.0, 0.0, 1.0))
    O = Vector((float(x), float(y), z0 - GROUND_LIFT))

    def box(g, u0, u1, d0, d1, z0, z1):
        c = O + t * (0.5 * (u0 + u1)) + n * (0.5 * (d0 + d1)) + Z * (0.5 * (z0 + z1))
        g.obb(c, (t, n, Z), (0.5 * (u1 - u0), 0.5 * (d1 - d0), 0.5 * (z1 - z0)))
    hl = 0.5 * length
    seat = _Geo()
    if fine:
        k, gap = BENCH_SEAT_SLATS, BENCH_GAP
        sd = (0.42 - (k - 1) * gap) / k
        for i in range(k):
            d0 = 0.10 + i * (sd + gap)
            box(seat, -hl, hl, d0, d0 + sd, 0.42, 0.47)
        if back:
            k = BENCH_BACK_SLATS
            sh = (0.36 - (k - 1) * gap) / k
            for i in range(k):
                zb = 0.52 + i * (sh + gap)
                box(seat, -hl, hl, 0.05, 0.10, zb, zb + sh)
    else:
        box(seat, -hl, hl, 0.10, 0.52, 0.42, 0.47)
        if back:
            box(seat, -hl, hl, 0.05, 0.10, 0.52, 0.88)
    legs = _Geo()
    for su in (-1, 1):
        box(legs, su * (hl - 0.15) - 0.025, su * (hl - 0.15) + 0.025, 0.06, 0.48,
            GROUND_LIFT, 0.42)
    return [(seat.finish(f"bench_{name}"), mat), (legs.finish(f"bench_{name}legs"), leg_mat)]


def litter_bin(name, x, y, r=0.26, h=0.90, mat="trim"):
    """`bin_<name>` 8-sided bin from GROUND_LIFT with a lid cap (mat trim = slot 1). A
    recipe lists `bin_` in lod1_extra."""
    return _single(f"bin_{name}", lambda g: g.cyl((x, y, GROUND_LIFT), (x, y, h), r, 8,
                                                  cap0=False, cap1=True, phase=math.pi / 8), mat)


def _tube_into(g, pts, r):
    """3-sided tube through a polyline (one top edge; joined rings, so box projection keeps
    a few long UV islands instead of one per segment)."""
    P = [Vector(p) for p in pts]
    rings = []
    for i, p in enumerate(P):
        tv = (P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]).normalized()
        b1 = tv.cross(Vector((0.0, 0.0, 1.0)))
        b1 = b1.normalized() if b1.length > 1e-6 else Vector((1.0, 0.0, 0.0))
        b2 = b1.cross(tv).normalized()
        if b2.z < 0.0:
            b2 = -b2
        rings.append([g.v(p + (b1 * math.cos(th) + b2 * math.sin(th)) * r)
                      for th in (math.pi / 2, math.pi / 2 + 2 * math.pi / 3,
                                 math.pi / 2 + 4 * math.pi / 3)])
    for i in range(len(P) - 1):
        mid = (P[i] + P[i + 1]) * 0.5
        for k in range(3):
            k1 = (k + 1) % 3
            vs = [rings[i][k], rings[i][k1], rings[i + 1][k1], rings[i + 1][k]]
            out = sum((v.co for v in vs), Vector()) / 4.0 - mid
            g.face(vs, out)


def string_lights(name, anchors, sag=0.40, bulb_every=1.30, bulb=0.07, wire_r=0.008,
                  wire_seg=1.8, pole=0.07):
    """Festoon lights (detail pass 1: the deck's night read). anchors = ((x, y, z,
    pole_base_z | None), ...): consecutive anchors span a parabola with `sag` at mid-span;
    bulbs (boxes `bulb` wide, mat lamp = emissive) every ~bulb_every m hang from the wire; a
    3-sided wire tube (r wire_r) and, for an anchor with a pole_base_z, a square pole up to
    it. Parts `lamp_<name>` (bulbs) and `lamp_<name>wire` (wire + poles, galv); `lamp_` is
    on LOD1_DELETE (LOD0 only)."""
    A = [Vector((float(p[0]), float(p[1]), float(p[2]))) for p in anchors]
    if len(A) < 2:
        raise ValueError(f"string_lights {name}: needs two anchors")
    bulbs, wire = _Geo(), _Geo()
    Z = Vector((0.0, 0.0, 1.0))
    for p0, p1 in zip(A, A[1:]):
        L = (p1 - p0).length

        def at(f, p0=p0, p1=p1):
            return p0 + (p1 - p0) * f - Z * (4.0 * sag * f * (1.0 - f))
        m = max(2, int(math.ceil(L / wire_seg)))
        _tube_into(wire, [at(i / m) for i in range(m + 1)], wire_r)
        nb = max(1, int(round(L / bulb_every)))
        th = Vector((p1.x - p0.x, p1.y - p0.y, 0.0))
        th = th.normalized() if th.length > 1e-6 else Vector((1.0, 0.0, 0.0))
        tb = Z.cross(th).normalized()
        for i in range(nb):
            c = at((i + 0.5) / nb) - Z * (wire_r + 0.65 * bulb)
            bulbs.obb(c, (th, tb, Z), (0.5 * bulb, 0.5 * bulb, 0.65 * bulb))
    for p, a in zip(A, anchors):
        if len(a) > 3 and a[3] is not None:
            z0 = max(float(a[3]), GROUND_LIFT)
            wire.obb(Vector((p.x, p.y, 0.5 * (z0 + p.z + 0.05))),
                     ((1, 0, 0), (0, 1, 0), (0, 0, 1)),
                     (0.5 * pole, 0.5 * pole, 0.5 * (p.z + 0.05 - z0)), drop=("-Z",))
    return [(bulbs.finish(f"lamp_{name}"), "lamp"), (wire.finish(f"lamp_{name}wire"), "galv")]


def akja(name, at, facing, lean_deg=70.0, length=2.0, width=0.6):
    """`akja_<name>` rescue toboggan: half-tube shell (6 segments of a 12-gon) leaning
    against a wall at `lean_deg` from horizontal; `at` = foot point (z >= GROUND_LIFT),
    `facing` = the wall's outward normal (the akja stands in front of it). Slot 2."""
    n = _SIDES[facing][0] if isinstance(facing, str) else Vector(facing)
    A = Vector(at)
    A.z = max(A.z, GROUND_LIFT)
    lean = math.radians(lean_deg)
    up = (Vector((0, 0, 1)) * math.sin(lean) - n * math.cos(lean)).normalized()
    side = up.cross(n).normalized()
    out = side.cross(up).normalized()
    r = width / 2
    g = _Geo()
    rings = []
    for end in (0.0, length):
        ring = []
        for k in range(7):
            a = math.pi * k / 6
            p = A + up * end + side * (r * math.cos(a)) + out * (r * math.sin(a) * 0.6)
            ring.append(g.v(p))
        rings.append(ring)
    axis_mid = A + up * (length / 2)
    for k in range(6):
        vs = [rings[0][k], rings[0][k + 1], rings[1][k + 1], rings[1][k]]
        mid = sum((v.co for v in vs), Vector()) / 4
        g.face(vs, mid - axis_mid)
    return [(g.finish(f"akja_{name}"), "accent")]


def snow_guard(name, spec, side, offset=0.6, bracket_every=1.2, spans=None):
    """`snowguard_<name>` bar (6-seg, r 0.02) `offset` m up-slope of the eave tip, 0.08
    above the slab top, plus brackets (one part) every `bracket_every` m. `spans` =
    ((a0, a1), ...) along the ridge axis (skip a prow junction); default the whole slab."""
    if spans:
        parts = []
        for k, (s0, s1) in enumerate(spans):
            parts += snow_guard(f"{name}{k}", replace(spec, **({"y0": s0, "y1": s1, "rake_over": 0.0}
                                                              if spec.axis.upper() == "Y" else
                                                              {"x0": s0, "x1": s1, "rake_over": 0.0})),
                                side, offset, bracket_every)
        return parts
    g = _roof_geometry(spec)
    n, up, along = roof_frame(spec, side)
    s0 = side.startswith("-")
    et, zt = _eave_tip(spec, g, 0 if s0 else 1)
    t = g["t0"] if s0 else g["t1"]
    top_off = 0.5 * spec.thick * math.sqrt(1 + t * t)
    if g["ax"] == "Y":
        base = Vector((et, 0.0, zt + top_off))
    else:
        base = Vector((0.0, et, zt + top_off))
    base += up * offset
    ro = spec.overhang if spec.rake_over is None else spec.rake_over
    a0, a1 = g["a0"] - ro + 0.2, g["a1"] + ro - 0.2
    p0 = base + along * a0 + n * 0.08
    p1 = base + along * a1 + n * 0.08
    if g["ax"] == "Y":
        p0.y, p1.y = a0, a1
    else:
        p0.x, p1.x = a0, a1
    parts = _single(f"snowguard_{name}", lambda gg: gg.cyl(p0, p1, 0.02, 6), "galv")
    nb = max(2, int(round((a1 - a0) / bracket_every)) + 1)
    bg = _Geo()
    for k in range(nb):
        a = a0 + (a1 - a0) * k / (nb - 1)
        c = Vector(base)
        if g["ax"] == "Y":
            c.y = a
        else:
            c.x = a
        if FITTINGS.get("version", 0) >= 1:
            # fittings v1: the bracket stops at the rail centre line (0.08 off the slab, was 0.09) and is
            # 0.034 across the slope (was 0.04), inside the 6-seg rail's 0.0346 inscribed circle, so its top
            # face is wholly inside the rail: dropped, 2 tris per bracket, nothing visible changes
            bg.obb(c + n * 0.04, (along, up, n), (0.02, 0.017, 0.04), drop=("-Z", "+Z"))
        else:
            bg.obb(c + n * 0.045, (along, up, n), (0.02, 0.02, 0.045), drop=("-Z",))
    parts.append((bg.finish(f"snowguard_{name}b"), "galv"))
    return parts


PAD = (0.12, 0.10, 0.06)    # snow pad: width along the eave, depth up-slope, height of its downslope face


def snow_pads(name, spec, side, offsets=(1.4, 2.0), every=0.75, spans=None, inset=0.4):
    """fittings v1: pad-style snow guards (the slate-roof kind, Courchevel) as `snowguard_<name>p`: staggered
    rows at `offsets` m up-slope of the eave tip, one pad every `every` m along the eave (the second row shifted
    half a step). A pad is a wedge on the slab top: a vertical-ish face toward the eave, a top sloping back onto
    the slab, two end triangles, no bottom: 6 tris. `spans` ((a0, a1), ...) along the ridge axis; default the
    whole slab inset `inset` from the rakes."""
    g = _roof_geometry(spec)
    n, up, along = roof_frame(spec, side)
    s0 = side.startswith("-")
    et, zt = _eave_tip(spec, g, 0 if s0 else 1)
    t = g["t0"] if s0 else g["t1"]
    top_off = 0.5 * spec.thick * math.sqrt(1 + t * t) + 0.004
    base = Vector((et, 0.0, zt + top_off)) if g["ax"] == "Y" else Vector((0.0, et, zt + top_off))
    if spans is None:
        spans = ((g["a0"] + inset, g["a1"] - inset),)
    w, d, h = PAD
    gg = _Geo()
    count = 0
    for r, off in enumerate(offsets):
        for a0, a1 in spans:
            k = 0
            while True:
                a = a0 + (k + (0.5 if r % 2 else 0.0)) * every
                if a > a1 + 1e-6:
                    break
                k += 1
                c = Vector(base) + up * off
                if g["ax"] == "Y":
                    c.y = a
                else:
                    c.x = a
                f0, f1 = c - along * (w / 2), c + along * (w / 2)          # eave-side foot corners
                b0, b1 = f0 + up * d, f1 + up * d                          # up-slope foot corners
                t0, t1 = f0 + n * h, f1 + n * h                            # top of the eave-side face
                v = [gg.v(p) for p in (f0, f1, b0, b1, t0, t1)]
                gg.face([v[0], v[1], v[5], v[4]], -up)                     # face toward the eave
                gg.face([v[4], v[5], v[3], v[2]], n + up)                  # sloping top
                gg.face([v[0], v[4], v[2]], -along)
                gg.face([v[1], v[3], v[5]], along)
                count += 1
    if not count:
        raise ValueError(f"snow_pads {name}: no pad in {spans}")
    return [(gg.finish(f"snowguard_{name}p"), "galv")]


SHROUD = (0.25, 0.05, 0.05, 0.30)   # chimney shroud: leg height, leg size, plate thickness, pyramid rise (a hood)


def chimney_shroud(name, lo, hi):
    """fittings v1: a raised metal hood over a chimney crown (Courchevel's hooded stack, Peak 7's capped flues) as
    `fit_shroud<name>`: four corner legs from the crown top, a plate 5 cm past the stack each side, a low pyramid
    on it. lo / hi = the crown slab (`chimcap_<name>`) bounds in the authoring frame; the plate covers the stack
    (crown minus its 0.2 m drip each side). Close look: a screen band (8, no ends) + 12 (plate) + 4 (pyramid) =
    24 tris (v1's four legs were 32 of 48)."""
    leg_h, leg, th, rise = SHROUD
    x0, y0, x1, y1 = lo[0] + 0.15, lo[1] + 0.15, hi[0] - 0.15, hi[1] - 0.15
    z0 = hi[2]
    g = _Geo()
    # close look (2026-10-17): a screen band (four walls, 1 cm into the crown and the plate) replaces the four
    # 5 cm legs, which vanished at 6-20 m and left the hood reading as floating over the crown (8 tris, was 32)
    g.aabb((x0 + 0.08 - leg / 2, y0 + 0.08 - leg / 2, z0 - 0.01),
           (x1 - 0.08 + leg / 2, y1 - 0.08 + leg / 2, z0 + leg_h + 0.01), drop=("-Z", "+Z"))
    zp = z0 + leg_h
    g.aabb((x0, y0, zp), (x1, y1, zp + th))
    apex = g.v(((x0 + x1) / 2, (y0 + y1) / 2, zp + th + rise))
    c = [g.v(p) for p in ((x0, y0, zp + th), (x1, y0, zp + th), (x1, y1, zp + th), (x0, y1, zp + th))]
    cen = Vector(((x0 + x1) / 2, (y0 + y1) / 2, zp + th))
    for i in range(4):
        a, b = c[i], c[(i + 1) % 4]
        mid = (a.co + b.co) / 2
        g.face([a, b, apex], (mid - cen) + Vector((0, 0, 0.5)))
    return [(g.finish(f"fit_shroud{name}"), "galv")]


# fittings v1 (2026-10-17, docs/research/art/fittings-2026-10-17.md): the shoe at the foot of every downspout
FITTINGS = {"version": 0}
FIT_SHOES = []                  # (downspout part name, outlet point, direction) built inside fittings()
FIT_PIPES = []                  # close look (2026-10-17): every downspout() call's parameters; cs2_fittings re-routes
SHOE = (0.20, 0.26, 0.04)       # shoe: run out along the normal, top of the bend above the pipe foot, outlet height


class fittings:
    """Context manager (lodge.build_parts, --fittings v1): downspout() adds the shoe (a 45-degree kick-out at
    the pipe foot, the vanilla / real-world outlet) and the recipes add their gated fittings
    (`FITTINGS["version"] >= 1`)."""

    def __init__(self, version=1):
        self.cfg = {"version": int(version)}
        self._saved = None

    def __enter__(self):
        self._saved = dict(FITTINGS)
        FITTINGS.update(self.cfg)
        FIT_SHOES.clear()
        FIT_PIPES.clear()
        return FITTINGS

    def __exit__(self, *exc):
        FITTINGS.clear()
        FITTINGS.update(self._saved)
        return False


def _shoe_into(g, x, y, zb, n, size):
    """The shoe: an obb from inside the pipe foot (zb + SHOE[1]) out SHOE[0] along n to the outlet at
    zb + SHOE[2], its start face dropped (inside the pipe): 10 tris; the pipe itself loses its bottom face."""
    run, top, out_z = SHOE
    P0 = Vector((x, y, zb + top))
    P1 = Vector((x, y, zb + out_z)) + Vector((n.x, n.y, 0.0)).normalized() * run
    ax = (P1 - P0)
    L = ax.length
    ax /= L
    side = ax.cross(Vector((0, 0, 1)))
    side.normalize()
    upv = side.cross(ax).normalized()
    g.obb((P0 + P1) * 0.5, (ax, side, upv), (0.5 * L, 0.5 * size, 0.5 * size), drop=("-X",))
    return P1


def _pipe_lo0(parts):
    """Close look: remember the as-built corner of a registered downspout, so the router can follow a recipe that
    moves its parts afterwards (the school shifts the whole building +12 m in x)."""
    if FITTINGS.get("version", 0) >= 1 and FIT_PIPES and FIT_PIPES[-1]["name"] == parts[0][0].name:
        co = [v.co for v in parts[0][0].data.vertices]
        FIT_PIPES[-1]["lo0"] = tuple(min(c[i] for c in co) for i in range(3))
    return parts


def gutter(name, p0, p1, z, size=(0.12, 0.10)):
    """`gutter_<name>` box run between two (x, y) points, top at z ([K] 5 in K-style)."""
    a = Vector((p0[0], p0[1], z - size[1] / 2))
    b = Vector((p1[0], p1[1], z - size[1] / 2))
    return _single(f"gutter_{name}", lambda g: _beam_into(g, a, b, size[0], size[1]), "galv")


def downspout(name, x, y, z_top, normal, size=0.08, wall_gap=0.10, z_bottom=0.05,
              leader=None, shoe=None, shoe_dir=None):
    """`downspout_<name>` square 0.08 pipe from the gutter (z_top) down to z_bottom (0.05
    above grade: outside the ground band; or onto a deck / cap) with an elbow to the
    gutter line `wall_gap` m out along `normal`.
    leader = (lx, ly) (detail pass 1, L1 prow): the outlet drops 0.30 under the gutter at
    (x, y), an offset leg runs down to the pipe line (lx, ly) (a wall corner) and the pipe
    runs down there; `normal` / `wall_gap` are not used on that path."""
    n = _SIDES[normal][0] if isinstance(normal, str) else Vector(normal)
    g = _Geo()
    s = size / 2
    # fittings v1: the shoe, unless the caller says the pipe runs on into a drain / another run (shoe=False);
    # it kicks out along `shoe_dir` (default `normal`: the recipes that pass the elbow's direction TOWARD the
    # wall, the school's, pass the outward one here)
    shoe = FITTINGS.get("version", 0) >= 1 and shoe is not False
    sd = n if shoe_dir is None else (_SIDES[shoe_dir][0] if isinstance(shoe_dir, str) else Vector(shoe_dir))
    if FITTINGS.get("version", 0) >= 1:
        # close look (2026-10-17): cs2_fittings.route_downspouts rebuilds this part from these numbers with the
        # whole building in view (outlet drop into the gutter, a neck under the soffit, the pipe clear of trims,
        # clips, the shoe on its support)
        FIT_PIPES.append({"name": f"downspout_{name}", "x": float(x), "y": float(y), "z_top": float(z_top),
                          "n": tuple(n), "size": float(size), "wall_gap": float(wall_gap),
                          "z_bottom": float(z_bottom), "shoe": bool(shoe), "shoe_dir": tuple(sd),
                          "leader": None if leader is None else (float(leader[0]), float(leader[1]))})
    if leader is not None:
        lx, ly = float(leader[0]), float(leader[1])
        za = z_top - 0.08
        zb = za - 0.30
        zc = zb - max(0.40, 0.8 * math.hypot(lx - x, ly - y))
        _beam_into(g, Vector((x, y, za)), Vector((x, y, zb)), size, size)
        _beam_into(g, Vector((x, y, zb)), Vector((lx, ly, zc)), size, size)
        zb = max(z_bottom, 0.05)
        if shoe:
            g.aabb((lx - s, ly - s, zb + SHOE[1] - s), (lx + s, ly + s, zc + s), drop=("-Z",))
            FIT_SHOES.append((f"downspout_{name}", tuple(_shoe_into(g, lx, ly, zb, sd, size)), tuple(sd)))
        else:
            g.aabb((lx - s, ly - s, zb), (lx + s, ly + s, zc + s))
        return _pipe_lo0([(g.finish(f"downspout_{name}"), "galv")])
    zb = max(z_bottom, 0.05)
    if shoe:
        g.aabb((x - s, y - s, zb + SHOE[1] - s), (x + s, y + s, z_top - 0.25), drop=("-Z",))
        FIT_SHOES.append((f"downspout_{name}", tuple(_shoe_into(g, x, y, zb, sd, size)), tuple(sd)))
    else:
        g.aabb((x - s, y - s, zb), (x + s, y + s, z_top - 0.25))
    _beam_into(g, Vector((x, y, z_top - 0.25)), Vector((x, y, z_top - 0.25)) + n * wall_gap
               + Vector((0, 0, 0.18)), size, size)
    return _pipe_lo0([(g.finish(f"downspout_{name}"), "galv")])


# --------------------------------------------------------------------------------------
# Detail pass 1, school / L2 / patrol (kit group, 2026-10-11): design/detail-pass-1.md
# section 6.1 with design/detail-pass-1-skeptic.md M1-M10 applied (M1: the one bench, the
# one string-lights builder, labels on _label_frame). Additive: new builders here; the new
# keywords on bench / flag_mast / ski_rack_frame / prism_gable / gable_roof / deck default
# to the old geometry vertex for vertex (blockout hashes of every recipe unchanged,
# build/detail-pass-1-kit.md K4).
# --------------------------------------------------------------------------------------

LABEL_PROUD = 0.004         # a stamp quad sits this far proud of the board it labels


def _as_wall(wall):
    """WallRef or (side, plane[, u0, u1]) (recipe data stays bpy-free)."""
    return wall if isinstance(wall, WallRef) else WallRef(*wall)


def _check_foot(what, z0):
    """A closed part's foot: on grade at >= GROUND_LIFT (or on a deck), or below grade at
    <= -0.05 (plinth mode, skeptic MUST-CHANGE 3 rule); never inside the ground band."""
    if -0.05 < float(z0) < GROUND_LIFT - 1e-9:
        raise ValueError(f"{what}: foot z {z0} sits in the ground band (use >= {GROUND_LIFT} "
                         f"or <= -0.05)")


def _bar_open(g, p0, p1, w, h, up=(0.0, 0.0, 1.0)):
    """Box from p0 to p1 (w across, h up) without its two end caps: 8 tris."""
    P0, P1 = Vector(p0), Vector(p1)
    ax = P1 - P0
    L = ax.length
    if L < 1e-6:
        raise ValueError("bar: zero length")
    ax /= L
    side = ax.cross(Vector(up))
    if side.length < 1e-6:
        side = ax.cross(Vector((1.0, 0.0, 0.0)))
    side.normalize()
    upv = side.cross(ax).normalized()
    g.obb((P0 + P1) * 0.5, (ax, side, upv), (0.5 * L, 0.5 * w, 0.5 * h), drop=("+X", "-X"))


# ---- fine-detail pass (design/fine-detail-pass.md B.1 as cut by -skeptic.md FS6; opt-in) --------
# Every builder below takes `fine=False`; False is the old geometry vertex for vertex (the
# default path is the unchanged code), True the B.1 shape: more segments on what stays round,
# caps where a cut end shows, NO bevel strips (vanilla boxy props carry none: benchmark 2.2).
FINIAL_R = 0.07             # flag-mast ball finial radius (m)


def _ball_into(g, c, r, segs=6, rings=3):
    """Low ball (UV sphere) centred c: `segs` around, `rings` latitude bands (two pole fans +
    rings - 2 quad bands): 6 x 3 = 24 tris."""
    C = Vector(c)
    top, bot = g.v(C + Vector((0.0, 0.0, r))), g.v(C - Vector((0.0, 0.0, r)))
    lat = []
    for i in range(1, rings):
        a = math.pi * i / rings
        lat.append([g.v(C + Vector((r * math.sin(a) * math.cos(2 * math.pi * k / segs),
                                    r * math.sin(a) * math.sin(2 * math.pi * k / segs),
                                    r * math.cos(a)))) for k in range(segs)])
    for k in range(segs):
        k1 = (k + 1) % segs
        mid = (lat[0][k].co + lat[0][k1].co + top.co) / 3.0
        g.face([top, lat[0][k], lat[0][k1]], mid - C)
        mid = (lat[-1][k].co + lat[-1][k1].co + bot.co) / 3.0
        g.face([bot, lat[-1][k1], lat[-1][k]], mid - C)
        for a, b in zip(lat, lat[1:]):
            mid = (a[k].co + a[k1].co + b[k].co + b[k1].co) * 0.25
            g.face([a[k], b[k], b[k1], a[k1]], mid - C)


def post_sign(name, x, y, facing, w, h, top_z, z0=GROUND_LIFT, r=0.04, t=0.04,
              mat="trim_white", pole_mat="galv", *, fine=False):
    """Free-standing sign on one pole (school lesson-meeting signs, design S5). (x, y) = the
    pole axis; `sign_<n>_post` 6-seg pole r from z0 to top_z (no bottom cap),
    `sign_<n>_frame` board box w x h x t on the pole's `facing` side (top edge at top_z) and
    `sign_<n>_face` = ONE quad LABEL_PROUD in front of it (the stamp target, mat label).
    About 30 tris. fine=True (design B.1): the pole is 12-seg with a 10-tri top cap (34 tris
    instead of 18); frame and face unchanged."""
    _check_foot(f"post_sign {name}", z0)
    if top_z - h <= z0 + 0.3:
        raise ValueError(f"post_sign {name}: board bottom {top_z - h:.2f} too close to the foot")
    n, tv = _SIDES[facing]
    wall = WallRef(facing, x if facing in ("+X", "-X") else y)
    uc = Vector((float(x), float(y), 0.0)).dot(tv)
    if fine:
        parts = _single(f"sign_{name}_post",
                        lambda g: g.cyl((x, y, z0), (x, y, top_z), r, 12, cap0=False, cap1=True),
                        pole_mat)
    else:
        parts = _single(f"sign_{name}_post",
                        lambda g: g.cyl((x, y, z0), (x, y, top_z), r, 6, cap0=False), pole_mat)
    lo, hi = _wall_aabb(wall, uc - w / 2, uc + w / 2, top_z - h, top_z, r, r + t)
    parts += _single(f"sign_{name}_frame", lambda g: g.aabb(lo, hi), mat)
    parts += pane_quad(f"sign_{name}_face", wall, uc - w / 2, uc + w / 2, top_z - h, top_z,
                       d=r + t + LABEL_PROUD, mat="label")
    return parts


def maze_fence(name, segs, h=1.0, post_every=1.5, z0=GROUND_LIFT, r=0.03, tape=(0.06, 0.02),
               *, fine=False):
    """Lift-line maze (school magic carpet, design S6): segs = (((x0, y0), (x1, y1)), ...).
    `maze_<n>` 4-seg posts r (galv, uncapped: 8 tris each) at every segment end and at most
    `post_every` apart (shared corners once), `maze_<n>tape` one tape `tape` = (height,
    thickness) per segment centred h - 0.10 above z0 (accent, slot 2; ends open: 8 tris).
    fine=True (design B.1): 8-seg posts with a 6-tri top cap (22 tris each); tape unchanged."""
    _check_foot(f"maze_fence {name}", z0)
    pts = []
    for a, b in segs:
        A, B = Vector((a[0], a[1], 0.0)), Vector((b[0], b[1], 0.0))
        n = max(1, int(math.ceil((B - A).length / float(post_every) - 1e-9)))
        for k in range(n + 1):
            p = A + (B - A) * (k / n)
            if not any((p - q).length < 0.01 for q in pts):
                pts.append(p)
    g = _Geo()
    for p in pts:
        if fine:
            g.cyl((p.x, p.y, z0), (p.x, p.y, z0 + h), r, 8, cap0=False, cap1=True,
                  phase=math.pi / 8)
            continue
        g.cyl((p.x, p.y, z0), (p.x, p.y, z0 + h), r, 4, cap0=False, cap1=False,
              phase=math.pi / 4)
    parts = [(g.finish(f"maze_{name}"), "galv")]
    tg = _Geo()
    zt = z0 + h - 0.10
    for a, b in segs:
        _bar_open(tg, (a[0], a[1], zt), (b[0], b[1], zt), tape[1], tape[0])
    parts.append((tg.finish(f"maze_{name}tape"), "accent"))
    return parts


def radio_mast(name, x, y, z0, z1, brackets=(), whip=1.2, arm=0.8, r=0.04, *, fine=False):
    """Patrol radio mast + red beacon (design P1-3 / P2-3). `mast_<n>` (galv): 6-seg mast r
    from z0 to z1 (no bottom cap), a dipole arm 2 cm square, `arm` long, 0.35 under the top
    (across the first bracket's direction), a 4-seg whip r 0.012 `whip` long on the beacon;
    `mast_<n>br` (galv): one 4 cm bar per bracket (z, side, length) from the mast axis
    `length` m toward `side` (the wall it is fixed to); `lamp_<n>beacon` (lamp_red,
    emissive) 0.12 x 0.12 x 0.16 on the mast top. `mast_` is kept to LOD1 (LOD2_DELETE
    only); the beacon is lamp_ (LOD0). About 60 tris with two brackets.
    fine=True (design B.1): the mast is 12-seg (+12 tris); the rest unchanged."""
    _check_foot(f"radio_mast {name}", z0)
    if z1 <= z0 + 0.5:
        raise ValueError(f"radio_mast {name}: z1 {z1} <= z0 {z0} + 0.5")
    acr = Vector((1.0, 0.0, 0.0))
    if brackets:
        d0 = _SIDES[brackets[0][1]][0]
        acr = Vector((-d0.y, d0.x, 0.0))
    g = _Geo()
    g.cyl((x, y, z0), (x, y, z1), r, 12 if fine else 6, cap0=False)
    c = Vector((float(x), float(y), z1 - 0.35))
    _bar_open(g, c - acr * (arm / 2), c + acr * (arm / 2), 0.02, 0.02)
    zb = z1 + 0.16
    g.cyl((x, y, zb), (x, y, zb + whip), 0.012, 4, cap0=False, cap1=False)
    parts = [(g.finish(f"mast_{name}"), "galv")]
    if brackets:
        bg = _Geo()
        for bz, side, L in brackets:
            d = _SIDES[side][0]
            p = Vector((float(x), float(y), float(bz)))
            _bar_open(bg, p, p + d * float(L), 0.04, 0.04)
        parts.append((bg.finish(f"mast_{name}br"), "galv"))
    parts += box_part(f"lamp_{name}beacon", (x - 0.06, y - 0.06, z1), (x + 0.06, y + 0.06, zb),
                      "lamp_red")
    return parts


def toboggan_rack(name, wall, u0, u1, bars_z, depth=0.12, bar=0.04, *, fine=False):
    """Wall rack for rescue toboggans (design P1-4 / P2-5): `tobrack_<n>` (galv) = per z in
    bars_z one bar from u0 to u1, `depth` m out from the wall (bar centre), on two brackets
    (at u0 + 0.05 / u1 - 0.05, wall to bar, ends open). `wall` = WallRef or (side, plane).
    28 tris per bar. fine=True (design B.1): the bar is an 8-seg round tube (open ends, the
    brackets unchanged): 32 tris per bar."""
    wall = _as_wall(wall)
    g = _Geo()
    for z in bars_z:
        if fine:
            g.cyl(wall_point(wall, min(u0, u1), z, depth), wall_point(wall, max(u0, u1), z, depth),
                  bar / 2, 8, cap0=False, cap1=False, phase=math.pi / 8)
        else:
            lo, hi = _wall_aabb(wall, min(u0, u1), max(u0, u1), z - bar / 2, z + bar / 2,
                                depth - bar / 2, depth + bar / 2)
            g.aabb(lo, hi)
        for ub in (min(u0, u1) + 0.05, max(u0, u1) - 0.05):
            _bar_open(g, wall_point(wall, ub, z, 0.0), wall_point(wall, ub, z, depth - bar / 2),
                      0.03, 0.03)
    return [(g.finish(f"tobrack_{name}"), "galv")]


def backboard(name, at, facing, lean_deg=85.0, length=1.85, width=0.45, t=0.05):
    """Rescue spine board (design P1-4 / P2-5): `gear_<n>board` box (safety_yellow) leaning
    on a wall. Same rule as akja: `facing` = the OUTWARD normal of the wall it leans on, the
    top leans toward that wall (up = z sin(lean) - n cos(lean)); `at` = the foot of the
    board's BACK face (the face toward the wall), so its top edge is at
    at + up x length: put the foot length x cos(lean) out from the wall face for the top to
    rest on it. 12 tris."""
    n = _SIDES[facing][0] if isinstance(facing, str) else Vector(facing).normalized()
    A = Vector(at)
    _check_foot(f"backboard {name}", A.z)
    lean = math.radians(lean_deg)
    up = (Vector((0, 0, 1)) * math.sin(lean) - n * math.cos(lean)).normalized()
    side = up.cross(n).normalized()
    out = side.cross(up).normalized()
    if out.dot(n) < 0.0:
        out = -out
    c = A + up * (length / 2) + out * (t / 2)
    return _single(f"gear_{name}board",
                   lambda g: g.obb(c, (up, side, out), (length / 2, width / 2, t / 2)),
                   "safety_yellow")


def hose_reel(name, wall, u, z, r=0.30, w=0.18):
    """Wall-mounted fire-hose reel (design P2-6): `hosereel_<n>` (fire_red) = back plate
    (2 cm, back face dropped), an 8-seg drum r 0.55 r on the wall-normal axis and two 8-seg
    flanges r (1.5 cm, each with its outward cap only). z = reel centre height. 70 tris."""
    wall = _as_wall(wall)
    n, _tv = _SIDES[wall.side]
    g = _Geo()
    lo, hi = _wall_aabb(wall, u - 0.6 * r, u + 0.6 * r, z - 1.1 * r, z + 0.6 * r, 0.0, 0.02)
    g.aabb(lo, hi, _wall_keys(wall, ("back",)))
    p0 = wall_point(wall, u, z, 0.02)
    p1 = wall_point(wall, u, z, 0.02 + w)
    g.cyl(p0, p1, 0.55 * r, 8, cap0=False, cap1=False)
    for da, db in ((0.02, 0.035), (0.02 + w - 0.015, 0.02 + w)):
        g.cyl(wall_point(wall, u, z, da), wall_point(wall, u, z, db), r, 8, cap0=False,
              cap1=True)
    return [(g.finish(f"hosereel_{name}"), "fire_red")]


DRUM_BEVEL = 0.025          # fine drum: chime ring (end bevel) width, m


def drum(name, centre, axis=None, r=0.30, l=0.88, z0=None, mat="fire_red", *, fine=False):
    """Fuel drum / tank (design H5): `drum_<n>` 8-seg tank with both end caps (28 tris).
    centre = (x, y, z) of the tank centre with axis "X" | "Y" | "Z" (default "X"), or
    (x, y) with z0 = the surface an UPRIGHT drum stands on (axis "Z", bottom cap at z0).
    fine=True (design B.1): a 16-seg body with a DRUM_BEVEL end bevel ring at each end (the
    chimes: a 45-degree frustum down to r - DRUM_BEVEL) and 14-tri end caps: 124 tris."""
    if z0 is not None:
        _check_foot(f"drum {name}", z0)
        ax = "Z" if axis is None else str(axis).upper()
        if ax != "Z":
            raise ValueError(f"drum {name}: z0 makes an upright drum (axis Z), got {axis!r}")
        cx, cy = float(centre[0]), float(centre[1])
        p0, p1 = (cx, cy, float(z0)), (cx, cy, float(z0) + l)
    else:
        ax = "X" if axis is None else str(axis).upper()
        d = {"X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1))}[ax]
        C = Vector(centre)
        p0, p1 = tuple(C - d * (l / 2)), tuple(C + d * (l / 2))
        _check_foot(f"drum {name}", min(p0[2], p1[2]) - (0.0 if ax == "Z" else r))
    if fine:
        P0, P1 = Vector(p0), Vector(p1)
        u = (P1 - P0).normalized()
        b = DRUM_BEVEL

        def tank(g):
            g.cyl(P0 + u * b, P1 - u * b, r, 16, cap0=False, cap1=False, phase=math.pi / 16)
            g.cyl(P0, P0 + u * b, r - b, 16, cap0=True, cap1=False, r1=r, phase=math.pi / 16)
            g.cyl(P1 - u * b, P1, r, 16, cap0=False, cap1=True, r1=r - b, phase=math.pi / 16)
        return _single(f"drum_{name}", tank, mat)
    return _single(f"drum_{name}", lambda g: g.cyl(p0, p1, r, 8, phase=math.pi / 8), mat)


def fuel_cradle(name, x, y, axis, z_top, leg_to, r=0.30, l=0.88, pump=True, *, fine=False):
    """Cradle under a horizontal drum (design H5): `drum_<n>legs` (galv) = two A-frames at
    +-(l / 2 - 0.12) along `axis`, each a saddle bar across at z_top (the drum's underside)
    and two legs splaying from +-0.18 at the saddle to +-0.40 at `leg_to` (>= GROUND_LIFT,
    or <= -0.05 below grade in plinth mode: no vertex in the ground band); `drum_<n>pump`
    (galv) a hand pump (body + spout) on the drum top (centre height z_top + r). The drum
    itself is drum(name, (x, y, z_top + r), axis, r, l). fine=True (design B.1): the pump is a
    6-seg body with a top cap, a 6-seg spout and a crank bar (36 tris); the legs unchanged."""
    _check_foot(f"fuel_cradle {name}", leg_to)
    if z_top <= leg_to + 0.2:
        raise ValueError(f"fuel_cradle {name}: z_top {z_top} too close to leg_to {leg_to}")
    ax = str(axis).upper()
    A = Vector((1.0, 0.0, 0.0)) if ax == "X" else Vector((0.0, 1.0, 0.0))
    Cv = Vector((-A.y, A.x, 0.0))
    O = Vector((float(x), float(y), 0.0))
    g = _Geo()
    for sa in (-1, 1):
        base = O + A * (sa * (l / 2 - 0.12))
        _bar_open(g, base + Cv * (-0.30) + Vector((0, 0, z_top - 0.03)),
                  base + Cv * 0.30 + Vector((0, 0, z_top - 0.03)), 0.05, 0.06)
        for sc in (-1, 1):
            top = base + Cv * (sc * 0.18) + Vector((0, 0, z_top - 0.06))
            foot = base + Cv * (sc * 0.40) + Vector((0, 0, leg_to))
            lift = 0.0                  # the bar's lowest corner sits ON leg_to (fixed point)
            for _it in range(4):
                ax = (foot + Vector((0, 0, lift)) - top).normalized()
                sd = ax.cross(Vector((0.0, 0.0, 1.0))).normalized()
                lift = 0.025 * abs(sd.cross(ax).z) + 1e-5
            _bar_open(g, top, foot + Vector((0, 0, lift)), 0.05, 0.05)
    parts = [(g.finish(f"drum_{name}legs"), "galv")]
    if pump and fine:
        zt = z_top + 2.0 * r
        pg = _Geo()
        pg.cyl((x, y, zt - 0.02), (x, y, zt + 0.28), 0.05, 6, cap0=False, cap1=True)
        tip = O + Cv * 0.22 + Vector((0, 0, zt + 0.17))
        pg.cyl((x, y, zt + 0.20), tuple(tip), 0.014, 6, cap0=False, cap1=False)
        _bar_open(pg, Vector((x, y, zt + 0.29)), Vector((x, y, zt + 0.29)) - Cv * 0.16, 0.02, 0.02)
        parts.append((pg.finish(f"drum_{name}pump"), "galv"))
    elif pump:
        zt = z_top + 2.0 * r
        pg = _Geo()
        pg.aabb((x - 0.05, y - 0.05, zt - 0.02), (x + 0.05, y + 0.05, zt + 0.28), ("-Z",))
        tip = O + Cv * 0.22 + Vector((0, 0, zt + 0.20))
        _bar_open(pg, Vector((x, y, zt + 0.20)), tip, 0.025, 0.025)
        parts.append((pg.finish(f"drum_{name}pump"), "galv"))
    return parts


def log_stack(name, x0, x1, y0, y1, z0, z1, face_side="+X", mat="timber_heavy"):
    """Firewood stack (design P2-7): `wood_<n>` box (its bottom and the face opposite
    `face_side` dropped: it stands on a deck against a wall) and `sign_<n>logs_face` = ONE
    quad LABEL_PROUD in front of the `face_side` face (the log-end stamp target, mat
    label). 8 + 2 tris."""
    _check_foot(f"log_stack {name}", z0)
    opp = {"+X": "-X", "-X": "+X", "+Y": "-Y", "-Y": "+Y"}[face_side]
    parts = box_part(f"wood_{name}", (x0, y0, z0), (x1, y1, z1), mat, drop=("-Z", opp))
    if face_side in ("+X", "-X"):
        wall = WallRef(face_side, x1 if face_side == "+X" else x0)
        ua, ub = (y0, y1) if face_side == "+X" else (-y1, -y0)
    else:
        wall = WallRef(face_side, y1 if face_side == "+Y" else y0)
        ua, ub = (-x1, -x0) if face_side == "+Y" else (x0, x1)
    parts += pane_quad(f"sign_{name}logs_face", wall, ua, ub, z0, z1, d=LABEL_PROUD,
                       mat="label")
    return parts


def safety_stripe(name, p0, p1, z, depth=0.05, t=0.008):
    """Yellow nosing stripe (design S9 / H10 / P1-7 / P2-10): `stripe_<n>` box centred on
    the plan line p0 -> p1 (pass the line depth / 2 inside the nosing), `depth` wide, from
    the tread top z up t; bottom dropped (5 faces, 10 tris). Geometry, not a stamp: a flat
    colour needs no texels (design 2.1)."""
    _check_foot(f"safety_stripe {name}", z)
    a = Vector((p0[0], p0[1], 0.0))
    b = Vector((p1[0], p1[1], 0.0))
    d = b - a
    if d.length < 1e-6:
        raise ValueError(f"safety_stripe {name}: zero length")
    d.normalize()
    s = Vector((-d.y, d.x, 0.0))
    c = (a + b) * 0.5 + Vector((0.0, 0.0, z + t / 2))
    return _single(f"stripe_{name}",
                   lambda g: g.obb(c, (d, s, Vector((0, 0, 1))),
                                   (0.5 * (b - a).length, depth / 2, t / 2), drop=("-Z",)),
                   "safety_yellow")


def roof_emblem(name, spec, side, size, s0=None, centre=None, lift=ROOF_PANE_OFFSET,
                along_c=None):
    """A painted roof panel (patrol roof crosses, design P1-2 / P2-2): `emblem_<n>_face` =
    ONE square quad `size` x `size` parallel to the `side` slope's top plane, `lift` above
    it (skeptic S3: ROOF_PANE_OFFSET 0.03, no z-fight at 250-400 m). Placed by
      s0      the slope distance from the ridge line down to the square's upper edge, the
              square centred at along_c along the ridge (default: the slab's middle), or
      centre  the plan (x, y) of the square's centre.
    `spec` = the gable RoofSpec, side as roof_frame ("-X" / "+X" for a Y ridge, "-Y" / "+Y"
    for an X ridge). Raises if the square leaves the slope. 2 tris."""
    if spec.kind != "gable":
        raise ValueError(f"roof_emblem {name}: gable roofs only (got {spec.kind!r})")
    g = _roof_geometry(spec)
    n, up, along = roof_frame(spec, side)
    si = 0 if side.startswith("-") else 1
    t = g["t0"] if si == 0 else g["t1"]
    top_off = 0.5 * spec.thick * math.sqrt(1.0 + t * t)
    if centre is not None:
        cx, cy = float(centre[0]), float(centre[1])
        e = cx if g["ax"] == "Y" else cy
        C = Vector((cx, cy, _centre_z(spec, e, si) + top_off))
    elif s0 is not None:
        am = 0.5 * (g["a0"] + g["a1"]) if along_c is None else float(along_c)
        zr = _centre_z(spec, g["r"], si) + top_off
        R = Vector((g["r"], am, zr)) if g["ax"] == "Y" else Vector((am, g["r"], zr))
        C = R - up * (float(s0) + size / 2)
    else:
        raise ValueError(f"roof_emblem {name}: give s0 or centre")
    hs = size / 2
    lo_e = (C - up * hs)
    e_lo = lo_e.x if g["ax"] == "Y" else lo_e.y
    e_tip = _eave_tip(spec, g, si)[0]
    e_hi = (C + up * hs).x if g["ax"] == "Y" else (C + up * hs).y
    if (si == 0 and (e_lo < e_tip - 1e-6 or e_hi > g["r"] + 1e-6)) or \
            (si == 1 and (e_lo > e_tip + 1e-6 or e_hi < g["r"] - 1e-6)):
        raise ValueError(f"roof_emblem {name}: the square leaves the {side} slope")
    C = C + n * lift
    pts = [C - along * hs - up * hs, C + along * hs - up * hs, C + along * hs + up * hs,
           C - along * hs + up * hs]
    return _single(f"emblem_{name}_face", lambda gg: gg.poly(pts, n), "label")


# ======================================================================================
# Engine: bevel, UV layers, materials, prefilter (section 7, skeptic 1 / 6 / 7)
# ======================================================================================

def bevel_parts(parts, prefixes=BEVEL_PREFIXES, width=0.02, heavy_width=0.03, segments=1,
                angle_deg=45.0):
    """0.02 m x 1 segment angle-limited bevel (base_lodge.py:876-891); 0.03 on
    col_/beam_/truss_/pier_; parts whose bbox min dimension < 0.06 are skipped. Applied
    before any UV and before tag_parts. Returns the number of bevelled parts."""
    n = 0
    for obj, _m in parts:
        if not obj.name.startswith(tuple(prefixes)):
            continue
        if obj.get("no_bevel"):       # fittings v1: a recipe opts a part out (L5's chimney crowns)
            continue
        co = np.empty(len(obj.data.vertices) * 3, np.float32)
        obj.data.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        if co.size == 0 or float((co.max(0) - co.min(0)).min()) < 0.06:
            continue
        mod = obj.modifiers.new("Bevel", type="BEVEL")
        mod.width = heavy_width if obj.name.startswith(HEAVY_BEVEL_PREFIXES) else width
        mod.segments = segments
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(angle_deg)
        mod.use_clamp_overlap = True
        obj["bevelled"] = 1
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
        n += 1
    return n


def strip_uv_layers(parts):
    """Remove every UV layer from every part (skeptic MUST-CHANGE 1: cs2_kit parts carry
    `UVMap`, from_pydata parts carry none; material_sources then sees ONE list)."""
    for obj, _m in parts:
        me = obj.data
        for lay in list(me.uv_layers):
            me.uv_layers.remove(lay)


PREFILTER_FACTOR = 3.0          # filtered px/m = factor x shipped px/m x w (alias wall: 2x 0.74,
                                # 3x 0.79, 4x 0.84 of the box-filtered noise; raw 2.32x)
PREFILTER_VERSION = 1
PREFILTER_MAPS = ("Color", "NormalGL", "Roughness")
PREFILTER_ROOT = os.path.join(tempfile.gettempdir(), "cs2_bldg_prefilter")


def _load_raw(path):
    img = bpy.data.images.load(path, check_existing=False)
    try:
        img.colorspace_settings.name = "Non-Color"
        w, h = img.size
        buf = np.empty(w * h * 4, np.float32)
        img.pixels.foreach_get(buf)
        return buf.reshape(h, w, 4).astype(np.float64)
    finally:
        bpy.data.images.remove(img)


def _write_raw_png(path, rgba):
    h, w = rgba.shape[:2]
    img = bpy.data.images.new("CS2BLDG_pf", width=w, height=h, alpha=True, is_data=True)
    try:
        img.colorspace_settings.name = "Non-Color"
        img.pixels.foreach_set(np.ascontiguousarray(rgba, dtype=np.float32).ravel())
        img.filepath_raw = path
        img.file_format = "PNG"
        img.alpha_mode = "STRAIGHT"
        img.save()
    finally:
        bpy.data.images.remove(img)


def _box_weights(n_in, n_out):
    """(n_out, n_in) exact-overlap box filter: output pixel i averages [i s, (i+1) s)."""
    s = n_in / float(n_out)
    W = np.zeros((n_out, n_in), np.float64)
    for i in range(n_out):
        a, b = i * s, (i + 1) * s
        j0, j1 = int(math.floor(a)), int(math.ceil(b))
        for j in range(j0, min(j1, n_in)):
            ov = min(b, j + 1) - max(a, j)
            if ov > 0:
                W[i, j] = ov / s
    return W


def box_filter(img, n_out):
    """Separable exact-overlap box filter of an (H, W, C) array to (n_out, n_out, C): two
    BLAS tensordots (a 3-operand einsum runs unoptimised, ~1e11 ops on a 2K map)."""
    H, W, _C = img.shape
    Wy, Wx = _box_weights(H, n_out), _box_weights(W, n_out)
    tmp = np.tensordot(Wy, img, axes=(1, 0))                        # (n_out, W, C)
    return np.tensordot(tmp, Wx, axes=(1, 1)).transpose(0, 2, 1)    # (n_out, n_out, C)


def _sha1_file(path):
    h = hashlib.sha1()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def image_tile_m(mat):
    """(u, v) metres of the SOURCE image tile (tile_m is given after uv_rot)."""
    tu, tv = mat.tile_m
    return (tv, tu) if int(mat.uv_rot) % 180 == 90 else (tu, tv)


def prefilter_set(set_name, tile_m_img, ppm, *, cache_dir=None, texture_root=None,
                  maps=PREFILTER_MAPS):
    """
    Area-average one CC0 set to `ppm` px per metre over a SQUARE tile (2:1 sets stacked
    along their short axis) and write `<cache>/<Set>_<N>/<Set>_<N>_<Map>.png`
    (N = round(tile_sq x ppm)). Color is averaged in linear light, NormalGL decoded /
    averaged / renormalised, Roughness averaged raw. Returns dict(dir, N, tile_sq, stack,
    mean_lin, mean_srgb, cached). Deterministic: the same inputs give byte-identical PNGs.
    """
    root = texture_root or cs2_bake.TEXTURE_ROOT
    sdir = os.path.join(root, set_name)
    tu, tv = float(tile_m_img[0]), float(tile_m_img[1])
    big, small = max(tu, tv), min(tu, tv)
    rep = big / small
    if abs(rep - round(rep)) > 1e-6:
        raise ValueError(f"{set_name}: tile {tile_m_img} is not an integer aspect")
    rep = int(round(rep))
    stack = (1, rep) if tv < tu else (rep, 1)          # (copies along u, copies along v)
    tile_sq = big
    N = max(8, int(round(tile_sq * float(ppm))))
    cache = cache_dir or PREFILTER_ROOT
    tag = f"{set_name}_{N}"
    out_dir = os.path.join(cache, tag)
    srcs = {}
    for m in maps:
        p = cs2_bake.find_set_map(sdir, m)
        if p is None:
            raise FileNotFoundError(f"prefilter: set {set_name} has no {m} map in {sdir}")
        srcs[m] = p
    meta = {"version": PREFILTER_VERSION, "set": set_name, "N": N, "tile_sq": tile_sq,
            "stack": list(stack), "sources": {m: [os.path.basename(p), _sha1_file(p)]
                                              for m, p in sorted(srcs.items())}}
    meta_path = os.path.join(out_dir, "meta.json")
    if os.path.isfile(meta_path):
        try:
            with open(meta_path, "r", encoding="utf-8") as fh:
                old = json.load(fh)
            if {k: old.get(k) for k in meta} == meta and all(
                    os.path.isfile(os.path.join(out_dir, f"{tag}_{m}.png")) for m in maps):
                return dict(dir=out_dir, N=N, tile_sq=tile_sq, stack=stack,
                            mean_lin=tuple(old["mean_lin"]), mean_srgb=tuple(old["mean_srgb"]),
                            cached=True)
        except (OSError, ValueError, KeyError):
            pass
    os.makedirs(out_dir, exist_ok=True)
    mean_lin = None
    for m in sorted(maps):
        px = _load_raw(srcs[m])
        px = np.tile(px, (stack[1], stack[0], 1))          # rows = v copies, cols = u copies
        if m == "Color":
            lin = srgb_to_linear(px[..., :3])
            f = box_filter(lin, N)
            enc = linear_to_srgb(f)
            q = np.rint(np.clip(enc, 0, 1) * 255.0) / 255.0
            mean_lin = tuple(float(x) for x in srgb_to_linear(q).reshape(-1, 3).mean(0))
            rgb = q
        elif m.startswith("Normal"):
            n = px[..., :3] * 2.0 - 1.0
            f = box_filter(n, N)
            f[..., 2] = np.maximum(f[..., 2], 0.05)
            f /= np.maximum(np.linalg.norm(f, axis=-1, keepdims=True), 1e-9)
            rgb = f * 0.5 + 0.5
        else:
            rgb = box_filter(px[..., :3], N)
        out = np.ones((N, N, 4), np.float64)
        out[..., :3] = np.clip(rgb, 0.0, 1.0)
        _write_raw_png(os.path.join(out_dir, f"{tag}_{m}.png"), out)
    meta["mean_lin"] = list(mean_lin) if mean_lin else [0.5, 0.5, 0.5]
    meta["mean_srgb"] = list(linear_to_srgb(tuple(meta["mean_lin"])))
    with open(meta_path, "w", encoding="utf-8") as fh:
        json.dump(meta, fh, indent=1, sort_keys=True)
    return dict(dir=out_dir, N=N, tile_sq=tile_sq, stack=stack, mean_lin=tuple(meta["mean_lin"]),
                mean_srgb=tuple(meta["mean_srgb"]), cached=False)


def material_specs(keys, base_ppm, *, factor=PREFILTER_FACTOR, cache_dir=None,
                   texture_root=None):
    """
    {key: cs2_bake.MaterialSpec} for the keys a build uses, plus an info dict. CC0 sets go
    through prefilter_set at factor x base_ppm x density_w (px/m), tint = per-channel
    lin(target_srgb) / mean_lin(filtered Color) (or the explicit linear BldgMat.tint);
    flat materials get albedo = lin(target_srgb). rough = 1 - smooth (None = the set's
    Roughness map); projection "inplane" -> cs2_bake "planar", "along" -> "box" (retile
    overwrites it).
    """
    specs, info = {}, {}
    for key in sorted(set(keys)):
        m = BLDG_MATERIALS[key]
        rough = None if m.smooth is None else 1.0 - float(m.smooth)
        proj = "planar" if m.proj == "inplane" else "box"
        emis = srgb_to_linear(_rgb3(m.emissive_srgb)) if m.emissive_srgb is not None else None
        recolor = True if m.recolor else None
        if m.set:
            ppm = max(4.0, factor * float(base_ppm) * float(m.density_w))
            pf = prefilter_set(m.set, image_tile_m(m), ppm, cache_dir=cache_dir,
                               texture_root=texture_root)
            if m.tint is not None:
                tint = _rgb3(m.tint)
            elif m.target_srgb is not None:
                tl = srgb_to_linear(_rgb3(m.target_srgb))
                tint = tuple(a / max(b, 1e-6) for a, b in zip(tl, pf["mean_lin"]))
            else:
                tint = (1.0, 1.0, 1.0)
            specs[key] = cs2_bake.MaterialSpec(
                texture_set=pf["dir"], metres_per_tile=pf["tile_sq"], tint=tint,
                metal=float(m.metal), rough=rough, slot=m.slot, recolor_albedo=recolor,
                projection=proj, emissive=emis)
            info[key] = dict(set=m.set, filtered_dir=pf["dir"], N=pf["N"], ppm=round(ppm, 2),
                             tile_sq=pf["tile_sq"], stack=list(pf["stack"]),
                             tint_lin=[round(t, 4) for t in tint],
                             set_mean_srgb=[round(c, 3) for c in pf["mean_srgb"]],
                             target_srgb=list(m.target_srgb) if m.target_srgb else None,
                             slot=m.slot, uv_rot=m.uv_rot, proj=m.proj)
        else:
            alb = srgb_to_linear(_rgb3(m.target_srgb))
            specs[key] = cs2_bake.MaterialSpec(
                albedo=alb, metres_per_tile=1.0, metal=float(m.metal),
                rough=0.6 if rough is None else rough, slot=m.slot, recolor_albedo=recolor,
                projection=proj, emissive=emis)
            info[key] = dict(set=None, albedo_lin=[round(a, 4) for a in alb],
                             target_srgb=list(m.target_srgb), slot=m.slot, proj=m.proj,
                             emissive_lin=[round(e, 4) for e in emis] if emis else None)
    return specs, info


def square_tile_m(mat):
    """Edge of the square (stacked) tile prefilter_set writes for a set material."""
    tu, tv = image_tile_m(mat)
    return max(tu, tv)


def projection_specs(keys):
    """MaterialSpecs for cs2_bake.material_sources' projection step only (UVTile in
    metres / square tile): the ORIGINAL set folders (paths are re-bound to the prefiltered
    sets at bake time, lodge._final_sources), so no prefilter runs before the cull."""
    out = {}
    for key in sorted(set(keys)):
        m = BLDG_MATERIALS[key]
        proj = "planar" if m.proj == "inplane" else "box"
        if m.set:
            out[key] = cs2_bake.MaterialSpec(texture_set=m.set, metres_per_tile=square_tile_m(m),
                                             metal=float(m.metal), slot=m.slot,
                                             recolor_albedo=True if m.recolor else None,
                                             projection=proj)
        else:
            out[key] = cs2_bake.MaterialSpec(albedo=srgb_to_linear(_rgb3(m.target_srgb)),
                                             metres_per_tile=1.0, metal=float(m.metal),
                                             slot=m.slot,
                                             recolor_albedo=True if m.recolor else None,
                                             projection=proj)
    return out


def _rot_uv(uv, deg):
    deg = int(deg) % 360
    if deg == 0:
        return uv
    u, v = uv[:, 0].copy(), uv[:, 1].copy()
    if deg == 90:
        return np.stack([v, -u], axis=1)
    if deg == 180:
        return np.stack([-u, -v], axis=1)
    if deg == 270:
        return np.stack([-v, u], axis=1)
    raise ValueError(f"uv rotation {deg} not a multiple of 90")


def retile(parts, spec_table, layer=cs2_bake.TILE_LAYER):
    """After cs2_bake.material_sources: per-part `along` projection (u along the part's
    principal axis, v across; end caps planar), `uv_rot` + obj["tile_rot"] rotations
    (vertical boards, deck boards perpendicular to the host wall). Keeps UVTile isotropic
    (metres / tile_sq on both axes). Small boxes built with a cross net (_Geo.obb) get
    their net instead (one UV island per box; not on bevelled parts, whose chamfers
    would distort it). The net attributes are removed from every part. Returns
    {part name: (proj, rotation, net faces)} for changed parts."""
    changed = {}
    for obj, key in parts:
        m = BLDG_MATERIALS[key]
        rot = (int(m.uv_rot) + int(obj.get("tile_rot", 0))) % 360
        proj = obj.get("proj", m.proj)
        me = obj.data
        has_net = me.attributes.get("netf") is not None and not obj.get("bevelled")
        if proj != "along" and rot == 0 and not has_net:
            _drop_net(me)
            continue
        lay = me.uv_layers[layer]
        mpt = float(spec_table[key].metres_per_tile)
        if proj == "along":
            M = np.array(obj.matrix_world, dtype=np.float64)
            co = np.empty(len(me.vertices) * 3, np.float32)
            me.vertices.foreach_get("co", co)
            W = co.reshape(-1, 3).astype(np.float64) @ M[:3, :3].T + M[:3, 3]
            C = np.cov((W - W.mean(0)).T)
            evals, evecs = np.linalg.eigh(C)
            L = evecs[:, int(np.argmax(evals))]
            L = L * (1.0 if (L[np.argmax(np.abs(L))] > 0) else -1.0)
            uv = np.zeros((len(me.loops), 2))
            lv = np.empty(len(me.loops), np.int32)
            me.loops.foreach_get("vertex_index", lv)
            for p in me.polygons:
                n = np.array(p.normal, dtype=np.float64)
                n = M[:3, :3] @ n
                n /= max(np.linalg.norm(n), 1e-12)
                if abs(float(n @ L)) > 0.7:
                    up = np.array((0.0, 0.0, 1.0))
                    a = np.cross(up, n)
                    if np.linalg.norm(a) < 1e-6:
                        a = np.array((1.0, 0.0, 0.0))
                    a /= np.linalg.norm(a)
                    b = np.cross(n, a)
                else:
                    a = L
                    b = np.cross(n, L)
                    b /= max(np.linalg.norm(b), 1e-12)
                for li in range(p.loop_start, p.loop_start + p.loop_total):
                    P = W[lv[li]]
                    uv[li] = (P @ a, P @ b)
            uv /= mpt
        else:
            uv = cs2_uv.loop_uvs(lay)
        uv = _rot_uv(uv, rot)
        n_net = 0
        if has_net:
            nf = np.zeros(len(me.polygons), np.int32)
            me.attributes["netf"].data.foreach_get("value", nf)
            nu = np.zeros(len(me.loops), np.float32)
            nv = np.zeros(len(me.loops), np.float32)
            me.attributes["netu"].data.foreach_get("value", nu)
            me.attributes["netv"].data.foreach_get("value", nv)
            for p in me.polygons:
                if nf[p.index]:
                    sl = slice(p.loop_start, p.loop_start + p.loop_total)
                    uv[sl, 0] = nu[sl] / mpt
                    uv[sl, 1] = nv[sl] / mpt
                    n_net += 1
        cs2_uv.set_loop_uvs(lay, uv)
        _drop_net(me)
        changed[obj.name] = (proj, rot, n_net)
    return changed


def align_islands(obj, layer=cs2_bake.TILE_LAYER, min_gain=0.15, max_edges=24):
    """Rotate each UV island of `layer` about its centroid to the orientation of minimum
    bounding-box area (candidates: the island's own edge directions) when that shrinks the
    box by more than `min_gain`. cs2_uv.pack_unique packs AXIS-ALIGNED boxes without
    rotation (rotate=False is mandatory there), so a box-projected rake board or slab end
    - a diagonal strip - wastes up to 18x its area (L1 pack median 8.5 px/m against a
    17 px/m prediction, measured 2026-10-11). A rotation keeps the winding (no mirror)
    and the tangent frame follows the UVs, so the tiling texture and its normal map stay
    consistent (grain then runs along the board). Deterministic. Returns (islands
    rotated, box area before, after)."""
    me = obj.data
    lay = me.uv_layers[layer]
    uv = cs2_uv.loop_uvs(lay)
    start, total = cs2_uv.face_loop_table(me)
    islands = cs2_uv.uv_islands(me, lay)
    n_rot, before, after = 0, 0.0, 0.0
    for isl in sorted(islands, key=lambda f: min(f)):
        idx = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in isl])
        P = uv[idx]
        lo, hi = P.min(0), P.max(0)
        a0 = float(np.prod(hi - lo))
        before += a0
        edges = []
        for f in isl:
            s, t = int(start[f]), int(total[f])
            for k in range(t):
                d = uv[s + (k + 1) % t] - uv[s + k]
                L = float(np.hypot(d[0], d[1]))
                if L > 1e-9:
                    edges.append((-L, math.atan2(d[1], d[0]) % (math.pi / 2)))
        best = (a0, 0.0)
        for _negL, th in sorted(edges)[:max_edges]:
            c, s_ = math.cos(-th), math.sin(-th)
            R = np.array(((c, -s_), (s_, c)))
            Q = P @ R.T
            a = float(np.prod(Q.max(0) - Q.min(0)))
            if a < best[0] - 1e-12:
                best = (a, th)
        if best[1] != 0.0 and best[0] < (1.0 - min_gain) * a0:
            c, s_ = math.cos(-best[1]), math.sin(-best[1])
            R = np.array(((c, -s_), (s_, c)))
            cen = P.mean(0)
            uv[idx] = (P - cen) @ R.T + cen
            n_rot += 1
            after += best[0]
        else:
            after += a0
    cs2_uv.set_loop_uvs(lay, uv)
    print(f"[bldg] align_islands: {n_rot}/{len(islands)} islands rotated, box area "
          f"{before:.1f} -> {after:.1f} tile^2")
    return n_rot, before, after


def split_poor_islands(obj, layer=cs2_bake.TILE_LAYER, min_fill=0.35, min_box=20.0):
    """Break UV islands whose area fills < `min_fill` of their bounding box (rings such
    as a culled cap's frame, L-shapes) into per-face islands by shifting each face by
    a whole number of tiles: the tiling texture samples identically (period 1), the pack
    sees compact boxes. Only islands whose box exceeds `min_box` tile^2. Deterministic.
    Returns the number of islands split."""
    me = obj.data
    lay = me.uv_layers[layer]
    uv = cs2_uv.loop_uvs(lay)
    start, total = cs2_uv.face_loop_table(me)
    areas = cs2_uv.face_uv_areas(me, uv, start, total)
    n_split = 0
    shift = 0
    for isl in sorted(cs2_uv.uv_islands(me, lay), key=lambda f: min(f)):
        if len(isl) < 2:
            continue
        idx = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in isl])
        P = uv[idx]
        box = float(np.prod(P.max(0) - P.min(0)))
        if box < min_box or float(areas[isl].sum()) >= min_fill * box:
            continue
        for k, f in enumerate(sorted(isl)):
            shift += 1
            sl = slice(start[f], start[f] + total[f])
            uv[sl, 1] += 2.0 * (shift % 97) + 2.0       # whole tiles; keeps islands near origin
            uv[sl, 0] += float(k % 2)
        n_split += 1
    cs2_uv.set_loop_uvs(lay, uv)
    return n_split


def pack_layout_calibrated(obj, mesh, A, reserved, margin_px=None, equalise=True,
                           density_layer=cs2_uv.DENSITY_ATTR, iterations=3):
    """Drop-in for cs2_uv._pack_layout (same arguments, same report, same asserts) that
    packs with margin_method "ADD" calibrated to `margin_px` texels: pack, measure the
    pack scale s from the UV area, set the pre-scale margin to margin_px / (A s), repack
    (3 passes). Blender 5.2.2's FRACTION solver collapses on building kits (L1, ~1,300
    islands, 8 px: w = 1 median 14.9 px/m, atlas fill 0.17); the calibrated ADD pack
    reaches 23.7 px/m, fill 0.42 with measured gutters of 15.5 px between islands
    (2026-10-11). Reserved rects fall back to the library packer (stamped strips)."""
    if reserved:
        return _LIB_PACK_LAYOUT(obj, mesh, A, reserved, margin_px=margin_px, equalise=equalise,
                                density_layer=density_layer)
    U = cs2_uv.UNIQUE_LAYER
    if U not in mesh.uv_layers:
        raise RuntimeError(f"{mesh.name}: no '{U}' layer to pack")
    margin_px = cs2_uv.default_margin_px(A) if margin_px is None else int(margin_px)
    mesh.uv_layers.active = mesh.uv_layers[U]
    cs2_uv._select_unpinned(obj, mesh, [])
    if equalise:
        bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode="OBJECT")
    weights = cs2_uv.face_density_weights(mesh, density_layer)
    n_scaled = cs2_uv._scale_islands_by_weight(mesh, mesh.uv_layers[U], [], weights)
    pre = cs2_uv.loop_uvs(mesh.uv_layers[U]).copy()
    a_pre = float(cs2_uv.face_uv_areas(mesh, pre).sum())
    m_add = 0.0
    for _ in range(int(iterations)):
        cs2_uv.set_loop_uvs(mesh.uv_layers[U], pre)
        cs2_uv._select_unpinned(obj, mesh, [])
        bpy.ops.uv.pack_islands(rotate=False, scale=True, margin_method="ADD", margin=m_add,
                                shape_method="AABB")
        bpy.ops.object.mode_set(mode="OBJECT")
        post = cs2_uv.loop_uvs(mesh.uv_layers[U])
        s = math.sqrt(max(float(cs2_uv.face_uv_areas(mesh, post).sum()), 1e-12) / max(a_pre, 1e-12))
        m_add = margin_px / float(A) / s
    layer = mesh.uv_layers[U]
    islands = cs2_uv.check_layout(mesh, layer, [], [])
    dens = cs2_uv.face_px_per_m(mesh, layer, A)
    d = dens[np.isfinite(dens)]
    report = {"A": int(A), "margin_px": margin_px, "faces": len(mesh.polygons),
              "pinned_faces": 0, "islands": len(islands), "islands_weighted": n_scaled,
              "band": (0.0, 1.0), "density_w": (float(weights.min()), float(weights.max())),
              "px_per_m": {"min": float(d.min()) if d.size else 0.0,
                           "median": float(np.median(d)) if d.size else 0.0,
                           "max": float(d.max()) if d.size else 0.0},
              "packer": "calibrated-add", "pack_scale": s}
    print(f"[uv] {obj.name}: calibrated ADD pack {len(islands)} islands @ {A}, margin "
          f"{margin_px} px, px/m min {report['px_per_m']['min']:.0f} / med "
          f"{report['px_per_m']['median']:.0f} / max {report['px_per_m']['max']:.0f}")
    return report


_LIB_PACK_LAYOUT = cs2_uv._pack_layout


class calibrated_packer:
    """Context manager: route cs2_uv.pack_unique (and so cs2_bake.bake_unique) through
    pack_layout_calibrated for the duration; restores the library function on exit."""

    def __enter__(self):
        cs2_uv._pack_layout = pack_layout_calibrated
        return self

    def __exit__(self, *exc):
        cs2_uv._pack_layout = _LIB_PACK_LAYOUT
        return False


def _drop_net(me):
    for an in ("netu", "netv", "netf"):
        a = me.attributes.get(an)
        if a is not None:
            me.attributes.remove(a)


# ======================================================================================
# Density weights, detail specs (section 7.3, 7.5)
# ======================================================================================

DRESSING_W = 0.5       # LOD1-deleted dressing (tails, frames, rails, gutters, lamps ...)


def part_weight(name, matkey):
    """Exact-name texel-density weight: `*_face` labels 3.0, entry_/door_ 1.5, soffit_
    0.25; LOD1-deleted dressing min(material w, 0.5) (flat trim / galv / timber bits a
    few cm wide: their texels carry AO only; measured on L1: +1.5-2 px/m on the w = 1
    walls, Blender's packer chokes on ~1,100 small islands, 2026-10-11); else the
    material's density_w."""
    for kind, pat, w in WEIGHT_RULES:
        if (kind == "suffix" and name.endswith(pat)) or (kind == "prefix" and name.startswith(pat)):
            return w
    w = BLDG_MATERIALS[matkey].density_w
    if name.startswith(LOD1_DELETE):
        return min(w, DRESSING_W)
    return w


def part_weights(parts):
    """part_weight per part; a part's obj["w_override"] wins (painted_board, detail pass 1:
    the stamped resort sign ships at >= 400 px/m)."""
    return {obj.name: (float(obj["w_override"]) if "w_override" in obj
                       else part_weight(obj.name, key)) for obj, key in parts}


def detail_specs(parts):
    """Exact part name -> its material's DetailSpec (full names as keys, so cs2_compose's
    longest-prefix lookup matches exactly)."""
    out = {}
    for obj, key in parts:
        d = BLDG_MATERIALS[key].detail
        if d is not None:
            out[obj.name] = d
    return out


# ======================================================================================
# Contracts (sections 2, 3, 12)
# ======================================================================================

def _world_coords(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    M = np.array(obj.matrix_world, dtype=np.float64)
    return co.reshape(-1, 3).astype(np.float64) @ M[:3, :3].T + M[:3, 3]


def _poly_parts(obj):
    me = obj.data
    out = np.zeros(len(me.polygons), np.int32)
    attr = me.attributes.get("part")
    if attr is not None:
        attr.data.foreach_get("value", out)
    return out


def check_lot_fit(obj, lot_w, lot_d, slack=0.25):
    """AUTHORING frame (after apply_yaw, before orient_for_net - skeptic MUST-CHANGE 2):
    |y| <= 4 W + slack and |x| <= 4 D + slack else raise (the engine warns past lot +
    0.5 m total, BuildingInitializeSystem.cs:1326-1334); WARN beyond the lot itself."""
    W = _world_coords(obj)
    lo, hi = W.min(0), W.max(0)
    hx, hy = 4.0 * lot_d, 4.0 * lot_w
    rep = dict(x=(round(float(lo[0]), 3), round(float(hi[0]), 3)),
               y=(round(float(lo[1]), 3), round(float(hi[1]), 3)),
               z=(round(float(lo[2]), 3), round(float(hi[2]), 3)),
               half_lot=(hx, hy), margin_x=round(hx - max(-lo[0], hi[0]), 3),
               margin_y=round(hy - max(-lo[1], hi[1]), 3), warn=[])
    if max(-lo[0], hi[0]) > hx + slack or max(-lo[1], hi[1]) > hy + slack:
        raise RuntimeError(f"lot fit: mesh x {rep['x']} y {rep['y']} exceeds the "
                           f"{lot_w}x{lot_d} lot +-({hx}+{slack}, {hy}+{slack})")
    if max(-lo[0], hi[0]) > hx or max(-lo[1], hi[1]) > hy:
        rep["warn"].append("mesh exceeds the lot itself (inside the slack)")
        print(f"[bldg] WARN lot fit: {rep}")
    return rep


def check_facing(obj, table, arrival_prefixes, lot_d):
    """Area-weighted centroid x of the arrival parts >= 0.25 x 4 D (authoring frame,
    after yaw, before orient), else raise (R15)."""
    me = obj.data
    pp = _poly_parts(obj)
    hits = {i + 1 for i, n in enumerate(table) if n.startswith(tuple(arrival_prefixes))}
    if not hits:
        raise RuntimeError(f"facing: no part matches {arrival_prefixes}")
    M = np.array(obj.matrix_world)
    sx = sa = 0.0
    for p in me.polygons:
        if int(pp[p.index]) in hits:
            c = M[:3, :3] @ np.array(p.center) + M[:3, 3]
            sx += c[0] * p.area
            sa += p.area
    cx = sx / max(sa, 1e-9)
    need = 0.25 * 4.0 * lot_d
    if cx < need:
        raise RuntimeError(f"facing: arrival centroid x {cx:.2f} < {need:.2f} (arrival must "
                           f"face authored +X = game +Z, the road)")
    return dict(arrival_centroid_x=round(cx, 3), need=need)


def check_base_mode(obj, table, mode, ground_prefixes, plinth=None):
    """Engine base contract (section 3) on any LOD. engine: ground_ring_report verdict OK,
    no vertex below -1 cm, every ground prefix contributes OK edges, and the ground-band
    hygiene rule (a part with min z < 0.011 is a ground prefix; nothing starts in
    0.011..0.02). plinth: ok_edges == 0 and min z == -D +- 0.01 and no vertex in |z| <= 0.01.
    Raises; returns the report."""
    r = cs2_validate.ground_ring_report(obj, table=table)
    W = _world_coords(obj)
    pp = _poly_parts(obj)
    me = obj.data
    loops = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get("vertex_index", loops)
    part_min = {}
    for p in me.polygons:
        z = float(W[list(p.vertices), 2].min())
        k = int(pp[p.index])
        part_min[k] = min(part_min.get(k, 1e9), z)
    names_min = {table[k - 1] if 0 < k <= len(table) else f"part{k}": z for k, z in part_min.items()}
    problems = []
    if mode == "engine":
        if r["verdict"] != "OK":
            problems.append(f"verdict {r['verdict']} (blocked {r['blocked_edges']}, offending "
                            f"{list(r['offending_parts'])[:6]})")
        if r["below_verts"]:
            problems.append(f"{r['below_verts']} vertices below -1 cm")
        for gp in ground_prefixes:
            if not any(n.startswith(gp) for n in r["ok_parts"]):
                problems.append(f"ground prefix {gp!r} has no OK base edges")
        for n, z in sorted(names_min.items()):
            if z < GROUND_BAND and not n.startswith(tuple(ground_prefixes)):
                problems.append(f"part {n} reaches z {z:.3f} but is not a ground part")
            elif GROUND_BAND <= z < GROUND_LIFT - 1e-6:
                problems.append(f"part {n} starts at z {z:.3f} (inside 0.011..0.02)")
    elif mode == "plinth":
        if r["ok_edges"]:
            problems.append(f"{r['ok_edges']} OK base edges (plinth mode wants none)")
        band = np.abs(W[:, 2]) <= 0.01
        if band.any():
            problems.append(f"{int(band.sum())} vertices in the 0 +- 0.01 band")
        if plinth is not None:
            D = plinth[0]
            if abs(float(W[:, 2].min()) + D) > 0.01:
                problems.append(f"min z {W[:, 2].min():.3f} != -{D}")
    else:
        raise ValueError(f"base mode {mode!r}")
    rep = {k: r[k] for k in ("verdict", "ok_edges", "blocked_edges", "below_verts")}
    rep["ok_parts"] = sorted(r["ok_parts"])
    rep["mode"] = mode
    if problems:
        raise RuntimeError(f"{obj.name}: base mode {mode}: " + "; ".join(problems))
    return rep


def assert_budget(obj, budget, level=0):
    """LOD0: ERROR (raise) > lod0_cap, WARN < 0.6 x target; LOD1/2 vs their targets."""
    n = cs2_asset.tri_count(obj)
    if level == 0:
        if n > budget.lod0_cap:
            raise RuntimeError(f"{obj.name}: LOD0 {n} tris > cap {budget.lod0_cap}")
        if n < 0.6 * budget.lod0_target:
            print(f"[bldg] WARN {obj.name}: LOD0 {n} tris < 0.6 x target {budget.lod0_target}")
    elif level == 2 and n > budget.lod2_cap:
        raise RuntimeError(f"{obj.name}: LOD2 {n} tris > cap {budget.lod2_cap}")
    return n


# ======================================================================================
# Visibility culling (section 6.2 + skeptic MUST-CHANGE 5)
# ======================================================================================

def _hemisphere_dirs(rays):
    """Golden-spiral hemisphere around +Z (z uniform in (0, 1]: equal solid angle)."""
    ga = math.pi * (3.0 - math.sqrt(5.0))
    out = []
    for i in range(rays):
        z = (i + 0.5) / rays
        r = math.sqrt(max(0.0, 1.0 - z * z))
        a = i * ga
        out.append(Vector((r * math.cos(a), r * math.sin(a), z)))
    return out


def _basis(n):
    ref = Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((1, 0, 0))
    t = ref.cross(n).normalized()
    b = n.cross(t).normalized()
    return t, b


def cull_hidden(obj, protect=(), rays=24, samples=9, contact_m=0.02, horizon_z=None,
                d_min=25.0, ring=8, ring_z=-0.10, wall_rule="contact", arrival=(0.0, -1.0),
                table=None, cull_ok=(), min_keep=0.20, max_dist=200.0, ground_prefixes=None,
                thin_occluders=()):
    """
    Delete faces nobody can see (section 6.2, skeptic MUST-CHANGE 5). Per face of the
    joined, pane-free mesh: up to 9 samples (centre + 15 %-inset vertices / edge mids).
    A sample is CONTACT-hidden when another face touches it face to face (opposite
    normal within `contact_m` in front), duplicates it (coplanar same-facing, lower
    index kept), or a ray along the normal hits mesh within `contact_m`. Otherwise:
      - walls (|n.z| < 0.5) under wall_rule "contact" (default; orchestrator ruling
        "never culling walls except by contact") are VISIBLE; "arrival" ray-tests only
        walls not facing `arrival` (n_xy . arrival <= 0.5); "enclosed" ray-tests walls
        with every hemisphere direction and no ground credit;
      - other faces are visible when one of `rays` golden-spiral hemisphere directions
        (+ a ring of `ring` near-horizontal azimuths at w.z = ring_z for faces with
        n.z < -0.5) escapes the BVH within max_dist. HEIGHT-AWARE horizon: a downward
        direction is ground-blocked only when it reaches grade within d_min
        (-w.z > z_sample / d_min) or crosses grade inside the xy footprint of a ground part
        (`ground_prefixes`, default `protect`: terrain and engine base close every open-
        bottom part, so a ray leaving under its walls is blocked); `horizon_z` (fixed, the
        pre-skeptic rule) is used
        only when d_min is None.
    thin_occluders (close look, 2026-10-17; default none = the old behaviour byte for byte): parts of these
    prefixes (thin fittings: gutters, downpipes, snow rails, hoods) never hide a face of ANOTHER part, by
    contact or by ray: an 8 cm pipe on a facade cannot take a sill's underside or a 29 m eave face with it (the
    fittings-v1 lesson); their own faces are culled as before.
    A face goes when ALL samples are hidden; faces of `protect` prefixes never go. Asserts
    every part keeps >= min_keep of its faces unless matched by `cull_ok`. Returns the
    report (areas, faces per part, wall area deleted by rays - 0 under "contact").
    """
    me = obj.data
    if table is None:
        table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.transform(obj.matrix_world)
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    layer = bm.faces.layers.int.get("part")
    bvh = BVHTree.FromBMesh(bm)
    thin_ids = {i + 1 for i, n in enumerate(table) if n.startswith(tuple(thin_occluders))} \
        if thin_occluders and layer is not None else set()
    occ_map = None
    if thin_ids:                       # the BVH a non-thin face is tested against: everything but thin parts
        keep = [f.index for f in bm.faces if f[layer] not in thin_ids]
        bvh_occ = BVHTree.FromPolygons([v.co.copy() for v in bm.verts],
                                       [[v.index for v in bm.faces[i].verts] for i in keep],
                                       all_triangles=False)
        occ_map = keep
    hemi = _hemisphere_dirs(rays)
    ring_dirs = []
    for k in range(int(ring)):
        a = 2.0 * math.pi * (k + 0.5) / ring
        ring_dirs.append(Vector((math.cos(a), math.sin(a), ring_z)).normalized())
    arr = Vector((arrival[0], arrival[1], 0.0))
    if arr.length > 0:
        arr.normalize()
    prot_ids = {i + 1 for i, n in enumerate(table) if n.startswith(tuple(protect))} if protect \
        else set()
    nf = len(bm.faces)
    normals = [f.normal.copy() for f in bm.faces]
    gpre = tuple(protect if ground_prefixes is None else ground_prefixes)
    gids = {i + 1 for i, n in enumerate(table) if n.startswith(gpre)} if gpre else set()
    gboxes = {}
    for f in bm.faces:
        if layer is not None and f[layer] in gids:
            lo, hi = gboxes.get(f[layer], ((1e9, 1e9), (-1e9, -1e9)))
            xs = [v.co.x for v in f.verts]
            ys = [v.co.y for v in f.verts]
            gboxes[f[layer]] = ((min(lo[0], min(xs)), min(lo[1], min(ys))),
                                (max(hi[0], max(xs)), max(hi[1], max(ys))))
    gboxes = [((lo[0] - 0.05, lo[1] - 0.05), (hi[0] + 0.05, hi[1] + 0.05))
              for lo, hi in gboxes.values()]

    def under_ground_part(p, w):
        t = p.z / max(-w.z, 1e-9)
        qx, qy = p.x + w.x * t, p.y + w.y * t
        return any(lo[0] <= qx <= hi[0] and lo[1] <= qy <= hi[1] for lo, hi in gboxes)
    kill = []
    ray_wall_area = 0.0
    eps = 2e-3
    for f in bm.faces:
        pid = f[layer] if layer is not None else 0
        if pid in prot_ids:
            continue
        n = normals[f.index]
        if n.length < 0.5:
            continue
        c = f.calc_center_median()
        pts = [c]
        vs = [v.co for v in f.verts]
        for i, v in enumerate(vs):
            pts.append(v + (c - v) * 0.15)
            m = (v + vs[(i + 1) % len(vs)]) * 0.5
            pts.append(m + (c - m) * 0.15)
        pts = pts[:max(1, samples)]
        if occ_map is not None and pid not in thin_ids:
            tbvh, tmap = bvh_occ, occ_map
        else:
            tbvh, tmap = bvh, None
        is_wall = abs(n.z) < 0.5
        ray_test = True
        no_ground = False
        if is_wall:
            if wall_rule == "contact":
                ray_test = False
            elif wall_rule == "arrival":
                ray_test = Vector((n.x, n.y, 0)).dot(arr) <= 0.5
            elif wall_rule == "enclosed":
                no_ground = True
        t, b = _basis(n)
        dirs = [t * d.x + b * d.y + n * d.z for d in hemi]
        if n.z < -0.5:
            dirs += [d for d in ring_dirs if d.dot(n) > 0.02]
        visible = False
        contact_only = True
        for p in pts:
            hidden = False
            for (loc, nrm, idx, dist) in tbvh.find_nearest_range(p, contact_m + eps):
                if idx is not None and tmap is not None:
                    idx = tmap[idx]
                if idx is None or idx == f.index:
                    continue
                ng = normals[idx]
                off = (loc - p).dot(n)
                if ng.dot(n) < -0.9 and off >= -1e-4:
                    hidden = True
                    break
                if ng.dot(n) > 0.99 and abs(off) < 1e-3 and dist < 1e-3 and idx < f.index:
                    hidden = True
                    break
            if not hidden:
                hit = tbvh.ray_cast(p + n * 1e-4, n, contact_m)
                if hit[0] is not None and (hit[2] if tmap is None else tmap[hit[2]]) != f.index:
                    hidden = True
            if hidden:
                continue
            if not ray_test:
                visible = True
                break
            contact_only = False
            z_s = p.z
            o = p + n * eps
            for w in dirs:
                if not no_ground and w.z < 0.0:
                    if d_min is None:
                        if w.z < (horizon_z if horizon_z is not None else -0.05):
                            continue
                    elif z_s <= 0.0 or -w.z * d_min > z_s or under_ground_part(p, w):
                        continue
                if tbvh.ray_cast(o + w * 1e-3, w, max_dist)[0] is None:
                    visible = True
                    break
            if visible:
                break
        if not visible:
            kill.append(f)
            if is_wall and not contact_only:
                ray_wall_area += f.calc_area()
    # per-part bookkeeping
    before_area = sum(f.calc_area() for f in bm.faces)
    per_part_total, per_part_kill = {}, {}
    for f in bm.faces:
        pid = f[layer] if layer is not None else 0
        per_part_total[pid] = per_part_total.get(pid, 0) + 1
    for f in kill:
        pid = f[layer] if layer is not None else 0
        per_part_kill[pid] = per_part_kill.get(pid, 0) + 1
    kill_area = sum(f.calc_area() for f in kill)
    kill_idx = sorted(f.index for f in kill)
    starved = []
    for pid, tot in sorted(per_part_total.items()):
        k = per_part_kill.get(pid, 0)
        nm = table[pid - 1] if 0 < pid <= len(table) else f"part{pid}"
        if tot and (tot - k) / tot < min_keep and not nm.startswith(tuple(cull_ok)):
            starved.append(f"{nm} keeps {tot - k}/{tot}")
    bm.free()
    if starved:
        raise RuntimeError(f"cull_hidden: parts below {min_keep:.0%} of their faces (add them "
                           f"to recipe.cull_ok if attic-only): {starved[:12]}")
    if kill_idx:
        bm2 = bmesh.new()
        bm2.from_mesh(me)
        bm2.faces.ensure_lookup_table()
        bmesh.ops.delete(bm2, geom=[bm2.faces[i] for i in kill_idx], context="FACES")
        loose = [v for v in bm2.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm2, geom=loose, context="VERTS")
        bm2.to_mesh(me)
        bm2.free()
        me.update()
    removed = {}
    for pid, k in sorted(per_part_kill.items()):
        nm = table[pid - 1] if 0 < pid <= len(table) else f"part{pid}"
        removed[nm] = k
    rep = dict(faces_before=nf, faces_removed=len(kill), area_before=round(before_area, 1),
               area_after=round(before_area - kill_area, 1),
               wall_area_deleted_by_rays=round(ray_wall_area, 2), wall_rule=wall_rule,
               d_min=d_min, rays=rays, ring=ring, removed_per_part=removed)
    print(f"[cull] {obj.name}: {len(kill)}/{nf} faces removed, area {before_area:.0f} -> "
          f"{before_area - kill_area:.0f} m2, walls deleted by rays {ray_wall_area:.1f} m2 "
          f"(rule {wall_rule})")
    return rep


# ======================================================================================
# Rooms (section 6.1)
# ======================================================================================

def _room_of(name):
    if not name.startswith("win_"):
        return None
    rest = name[4:]
    if "_" not in rest:
        return None
    return rest.split("_", 1)[0]


def room_graph(obj, table, neighbour_m=NEIGHBOUR_M):
    """Pure: ({room: (min xyz, max xyz)}, hard edges, soft edges) from the `win_<room>_`
    panes. Hard = vertical intervals overlap AND horizontal gap < neighbour_m (they would
    merge into one interior: different ids); soft = vertically adjacent (gap < 1.5 m)
    with horizontal overlap (prefer different ids)."""
    W = _world_coords(obj)
    pp = _poly_parts(obj)
    boxes = {}
    for p in obj.data.polygons:
        k = int(pp[p.index])
        if not 0 < k <= len(table):
            continue
        room = _room_of(table[k - 1])
        if room is None:
            continue
        P = W[list(p.vertices)]
        lo, hi = P.min(0), P.max(0)
        if room in boxes:
            a, b = boxes[room]
            boxes[room] = (np.minimum(a, lo), np.maximum(b, hi))
        else:
            boxes[room] = (lo, hi)
    rooms = sorted(boxes)
    hard, soft = set(), set()
    for i, a in enumerate(rooms):
        for b in rooms[i + 1:]:
            (al, ah), (bl, bh) = boxes[a], boxes[b]
            gx = max(0.0, bl[0] - ah[0], al[0] - bh[0])
            gy = max(0.0, bl[1] - ah[1], al[1] - bh[1])
            gh = math.hypot(gx, gy)
            vo = min(ah[2], bh[2]) - max(al[2], bl[2])
            if vo > 1e-6 and gh < neighbour_m:
                hard.add((a, b))
            elif vo <= 1e-6 and -vo < SOFT_GAP_M and gh <= 1e-6:
                soft.add((a, b))
    return {r: (tuple(float(x) for x in boxes[r][0]), tuple(float(x) for x in boxes[r][1]))
            for r in rooms}, sorted(hard), sorted(soft)


def assign_rooms(rooms, hard, soft, name, pool=WIN_POOL):
    """Greedy colouring in sorted room order; candidates = pool rotated by crc32(name);
    take the first id unused by hard neighbours, preferring one unused by soft neighbours,
    then the least-used id so far (variety tie-break; ids are still reused freely beyond
    neighbour_m, where no merge is possible)."""
    pool = list(pool)
    rot = _seed(name) % len(pool)
    cand = pool[rot:] + pool[:rot]
    adj_h = {r: set() for r in rooms}
    adj_s = {r: set() for r in rooms}
    for a, b in hard:
        adj_h[a].add(b)
        adj_h[b].add(a)
    for a, b in soft:
        adj_s[a].add(b)
        adj_s[b].add(a)
    ids = {}
    for r in sorted(rooms):
        banned = {ids[o] for o in adj_h[r] if o in ids}
        avoid = {ids[o] for o in adj_s[r] if o in ids}
        free = [c for c in cand if c not in banned]
        if not free:
            raise RuntimeError(f"rooms: {r} needs more than {len(pool)} ids")
        pref = [c for c in free if c not in avoid] or free
        used = {}
        for v in ids.values():
            used[v] = used.get(v, 0) + 1
        ids[r] = min(pref, key=lambda c: (used.get(c, 0), pref.index(c)))
    if len(set(ids.values())) > MAX_ROOM_IDS:
        raise RuntimeError(f"rooms: {len(set(ids.values()))} distinct ids > {MAX_ROOM_IDS}")
    return ids


def split_rooms(obj, table, rooms="auto", *, name, suffix="Win", pool=WIN_POOL,
                neighbour_m=NEIGHBOUR_M):
    """Move every `win_<room>_<k>` pane off `obj` into ONE `<name>_<suffix>` sub-mesh,
    each room mapped into its 5x5 room cell (id % 5, id // 5) via cs2_submesh.split_submesh
    (lift_terminal.split_windows pattern: rename, then join). rooms = {room: (col, row)} or
    "auto" (room_graph + assign_rooms). Returns (sub or None, {room: id})."""
    present = sorted({_room_of(n) for n in table if _room_of(n)})
    if not present:
        return None, {}
    if rooms == "auto":
        boxes, hard, soft = room_graph(obj, table, neighbour_m)
        cells = assign_rooms(sorted(boxes), hard, soft, name, pool)
    else:
        cells = {}
        for r in present:
            if r not in rooms:
                raise KeyError(f"split_rooms: room {r!r} has no cell in {sorted(rooms)}")
            col_, row_ = rooms[r]
            cells[r] = int(row_) * 5 + int(col_)
    subs = []
    for i, r in enumerate(sorted(cells)):
        cid = cells[r]
        sub = cs2_submesh.split_submesh(obj, table, (f"win_{r}_",), suffix,
                                        win_cell=(cid % 5, cid // 5))
        sub.name = f"{name}{suffix}{i}"
        sub.data.name = sub.name
        subs.append(sub)
    main = subs[0]
    if len(subs) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for s in subs:
            s.select_set(True)
        bpy.context.view_layer.objects.active = main
        bpy.ops.object.join()
        main = bpy.context.active_object
    main.name = cs2_submesh.submesh_name(name, suffix)
    main.data.name = main.name
    if len(main.data.uv_layers) != 1:
        raise RuntimeError(f"{main.name}: {len(main.data.uv_layers)} UV layers")
    print(f"[rooms] {main.name}: {len(cells)} rooms -> {len(set(cells.values()))} ids "
          f"{dict(sorted(cells.items()))}, {len(main.data.polygons)} panes")
    return main, dict(sorted(cells.items()))


def win_cell_ids(sub):
    """Decode int(v*5)*5 + int(u*5) of each polygon's first loop (the importer's rule)."""
    lay = sub.data.uv_layers.active
    out = []
    for p in sub.data.polygons:
        u, v = lay.data[p.loop_start].uv
        out.append(int(min(max(v, 0), 0.999) * 5) * 5 + int(min(max(u, 0), 0.999) * 5))
    return out


def panes_on_surface(lod_obj, win_obj, tol=0.30):
    """Pane centres farther than `tol` from the LOD surface (decimated walls must not leave
    panes floating): WARN list [(pane index, distance)]."""
    bm = bmesh.new()
    bm.from_mesh(lod_obj.data)
    bm.transform(lod_obj.matrix_world)
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    M = win_obj.matrix_world
    out = []
    for p in win_obj.data.polygons:
        c = M @ p.center
        hit = bvh.find_nearest(c)
        d = (hit[0] - c).length if hit[0] is not None else 1e9
        if d > tol:
            out.append((p.index, round(d, 3)))
    if out:
        print(f"[bldg] WARN {lod_obj.name}: {len(out)} panes > {tol} m off the surface")
    return out


# ======================================================================================
# Reports
# ======================================================================================

def area_report(obj, sizes=(1024, 2048, 4096), k=0.48, table=None):
    """Kept (post-cull, packed) face area with the section-7.3 weights (skeptic MUST-CHANGE
    8): predicted median px/m of w = 1 parts = k A / sqrt(sum w^2 a), k measured on
    BaseStationL (0.478). Per material key (obj["matkey"] is not available after the join:
    the density_w face attribute carries the weight) and per part prefix."""
    me = obj.data
    if table is None:
        table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    a = np.empty(len(me.polygons), np.float32)
    me.polygons.foreach_get("area", a)
    w = cs2_uv.face_density_weights(me)
    pp = _poly_parts(obj)
    total = float(a.sum())
    s = float((w * w * a).sum())
    by_prefix = {}
    for i in range(len(a)):
        k_ = int(pp[i])
        nm = table[k_ - 1] if 0 < k_ <= len(table) else "?"
        pre = nm.split("_", 1)[0] + "_"
        e = by_prefix.setdefault(pre, [0.0, 0.0])
        e[0] += float(a[i])
        e[1] += float(w[i] * w[i] * a[i])
    pred = {int(A): round(k * A / math.sqrt(max(s, 1e-9)), 2) for A in sizes}
    pred_unw = {int(A): round(k * A / math.sqrt(max(total, 1e-9)), 2) for A in sizes}
    return dict(kept_m2=round(total, 1), sum_w2a=round(s, 1), k=k,
                predicted_ppm_w1=pred, predicted_ppm_unweighted=pred_unw,
                by_prefix={p: [round(v[0], 1), round(v[1], 1)] for p, v in sorted(by_prefix.items())})


def shipped_density(obj, A, layer=cs2_uv.UNIQUE_LAYER):
    """Area-weighted shipped px/m: median over w = 1 faces (the R18 number), overall
    median, per-weight medians."""
    me = obj.data
    d = cs2_uv.face_px_per_m(me, me.uv_layers[layer], A)
    w = cs2_uv.face_density_weights(me)
    a = np.empty(len(me.polygons), np.float32)
    me.polygons.foreach_get("area", a)
    ok = np.isfinite(d)

    def wmed(sel):
        sel = sel & ok
        if not sel.any():
            return None
        order = np.argsort(d[sel])
        dd, aa = d[sel][order], a[sel][order]
        c = np.cumsum(aa)
        return round(float(dd[np.searchsorted(c, 0.5 * c[-1])]), 2)
    out = dict(median_w1=wmed(np.abs(w - 1.0) < 1e-6), median_all=wmed(np.ones_like(ok)))
    out["by_weight"] = {f"{x:g}": wmed(np.abs(w - x) < 1e-6) for x in sorted(set(np.round(w, 3)))}
    return out


def contact_map(mesh, A, band=0.4, uv_layer=cs2_uv.UNIQUE_LAYER):
    """(A, A) 0..1 ground-contact weight for compose_maps: walls (|n.z| < 0.5) within
    `band` m of z 0 (engine mode only), 1 at grade fading to 0 at `band`."""
    out = np.zeros((A, A), np.float32)
    for poly, sl, mask, X, Y, q, P, _ppm in cs2_compose.iter_face_rasters(mesh, A, uv_layer):
        if P[:, 2].min() > band:
            continue
        n = np.cross(P[1] - P[0], P[2] - P[0])
        ln = np.linalg.norm(n)
        if ln < 1e-12 or abs(n[2] / ln) >= 0.5:
            continue
        qm, Pm, Mt = cs2_compose._affine_uv_to_world(q, P)
        z = cs2_compose._world_coord_field(np.array((0.0, 0.0, 1.0)), X, Y, qm, Pm, Mt)
        wv = np.clip(1.0 - z / band, 0.0, 1.0).astype(np.float32)
        view = out[sl]
        view[mask] = np.maximum(view[mask], wv[mask])
    return out


# ======================================================================================
# LOD chain with ground protection and the planar pre-pass (section 9, N9, skeptic 8)
# ======================================================================================

def _faces_of(bm, layer, table, prefixes):
    pre = tuple(prefixes)
    return [f for f in bm.faces if 0 < f[layer] <= len(table) and table[f[layer] - 1].startswith(pre)]


def lod_level(obj, table, name, target, delete_prefixes, protect_prefixes=(), planar_deg=1.0,
              min_tris=60, shade_angle=66.0):
    """
    One LOD level from the finished `obj`: copy -> delete faces of `delete_prefixes` ->
    split the ground (protect) faces off -> planar dissolve (planar_deg, delimit UV + SHARP)
    + triangulate -> COLLAPSE decimate the rest to (target - protected tris) -> rejoin ->
    shade_by_angle (no weighted normals). Ground faces are never dissolved or collapsed, so
    their z 0 edges survive (LOD meshes inherit MeshFlags.Base, EC-sk 5). Returns
    (lod_obj, info) with info = deleted faces, tris after deletes / planar / final,
    protected tris.
    """
    lod = obj.copy()
    lod.data = obj.data.copy()
    lod.name = name
    lod.data.name = name
    bpy.context.scene.collection.objects.link(lod)
    bm = bmesh.new()
    bm.from_mesh(lod.data)
    layer = bm.faces.layers.int.get("part")
    dead = _faces_of(bm, layer, table, delete_prefixes) if delete_prefixes else []
    n_del = len(dead)
    if dead:
        bmesh.ops.delete(bm, geom=dead, context="FACES")
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    after_delete = len(bm.faces)
    prot = set(_faces_of(bm, layer, table, protect_prefixes)) if protect_prefixes else set()
    prot_verts = {v for f in prot for v in f.verts}
    free_faces = [f for f in bm.faces if f not in prot]
    if planar_deg and free_faces:
        # sorted by index: a set of BMesh elements iterates in address order, and
        # dissolve_limit's result depends on its input order (LOD hashes differed across
        # two builds in one process before this, test_lodge_determinism 2026-10-11)
        bm.verts.index_update()
        bm.edges.index_update()
        verts = sorted((v for v in {v for f in free_faces for v in f.verts} if v not in prot_verts),
                       key=lambda v: v.index)
        edges = sorted((e for e in {e for f in free_faces for e in f.edges}
                        if not any(v in prot_verts for v in e.verts)), key=lambda e: e.index)
        bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(planar_deg), use_dissolve_boundaries=False,
                                 verts=verts, edges=edges, delimit={"UV", "SHARP"})
        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    after_planar = len(bm.faces)
    prot_tris = len(_faces_of(bm, layer, table, protect_prefixes)) if protect_prefixes else 0
    bm.to_mesh(lod.data)
    bm.free()
    lod.data.update()
    # split protected faces into their own object, decimate the rest
    ground = None
    if prot_tris:
        ground = lod.copy()
        ground.data = lod.data.copy()
        ground.name = name + "Ground"
        ground.data.name = ground.name
        bpy.context.scene.collection.objects.link(ground)
        for o, keep_prot in ((ground, True), (lod, False)):
            b2 = bmesh.new()
            b2.from_mesh(o.data)
            l2 = b2.faces.layers.int.get("part")
            pf = set(_faces_of(b2, l2, table, protect_prefixes))
            kill = [f for f in b2.faces if (f in pf) != keep_prot]
            bmesh.ops.delete(b2, geom=kill, context="FACES")
            b2.to_mesh(o.data)
            b2.free()
    free_tris = cs2_asset.tri_count(lod)
    want = max(min_tris, int(target)) - prot_tris
    ratio = min(1.0, max(want, 4) / max(free_tris, 1))
    bpy.ops.object.select_all(action="DESELECT")
    lod.select_set(True)
    bpy.context.view_layer.objects.active = lod
    if ratio < 0.999:
        mod = lod.modifiers.new("Decimate", type="DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
        b3 = bmesh.new()
        b3.from_mesh(lod.data)
        bmesh.ops.dissolve_degenerate(b3, dist=1e-5, edges=b3.edges[:])
        sl = [f for f in b3.faces if f.calc_area() < 1e-8]
        if sl:
            bmesh.ops.delete(b3, geom=sl, context="FACES")
        loose = [v for v in b3.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(b3, geom=loose, context="VERTS")
        bmesh.ops.triangulate(b3, faces=b3.faces[:])
        b3.to_mesh(lod.data)
        b3.free()
    if ground is not None:
        bpy.ops.object.select_all(action="DESELECT")
        lod.select_set(True)
        ground.select_set(True)
        bpy.context.view_layer.objects.active = lod
        bpy.ops.object.join()
        lod = bpy.context.active_object
        lod.name = name
        lod.data.name = name
    cs2_asset.shade_by_angle(lod, shade_angle, weighted=False)
    tris = cs2_asset.tri_count(lod)
    info = dict(deleted_faces=n_del, after_delete=after_delete, after_planar=after_planar,
                protected_tris=prot_tris, target=int(target), tris=tris, ratio=round(ratio, 4))
    return lod, info


def _remove_obj(o):
    me = o.data
    bpy.data.objects.remove(o, do_unlink=True)
    if me is not None and me.users == 0:
        bpy.data.meshes.remove(me)


def plan_lod_targets(obj, table, recipe, delete_lists=None, protect_prefixes=None, tol=0.25):
    """
    Dry-run the WHOLE level on copies (deletes + planar pass + protected collapse,
    skeptic claim 9): t1 = min(r.lod1, round(0.45 n0), achievable), t2 = min(r.lod2,
    achievable); never asks the decimator to grow a mesh. When the protected collapse
    overshoots the target beyond `tol` (ground faces stall it), retarget to the achieved
    count and report instead of raising blind. Returns ((t1, t2), report).
    """
    n0 = cs2_asset.tri_count(obj)
    b = recipe.budget
    d1, d2 = delete_lists
    prot = tuple(protect_prefixes or recipe.ground_prefixes)
    targets, rep = [], {}
    for level, (cap_t, dl) in enumerate(((min(b.lod1, int(round(0.45 * n0))), d1),
                                         (b.lod2, d2)), start=1):
        probe, info = lod_level(obj, table, f"{obj.name}Plan{level}", 10 ** 9, dl, prot)
        achievable = info["tris"]
        _remove_obj(probe)
        t = min(cap_t, achievable)
        lod, info2 = lod_level(obj, table, f"{obj.name}Plan{level}b", t, dl, prot)
        got = info2["tris"]
        _remove_obj(lod)
        note = "ok"
        if abs(got - t) > tol * t:
            note = f"retargeted {t} -> {got} (protected collapse / planar floor)"
            t = got
        targets.append(t)
        rep[f"LOD{level}"] = dict(cap=cap_t, achievable=achievable, target=t, dry_run=got,
                                  protected_tris=info2["protected_tris"], note=note)
        print(f"[lod-plan] LOD{level}: cap {cap_t}, achievable {achievable}, dry run {got} "
              f"-> target {t} ({note})")
    return tuple(targets), rep


def lod_chain(obj, name, targets, delete_lists, protect_prefixes=(), tol=0.25):
    """Build LOD1..n with lod_level; each must land within tol of its (planned) target."""
    lods, infos = [], []
    for level, (t, dl) in enumerate(zip(targets, delete_lists), start=1):
        lod, info = lod_level(obj, json.loads(obj.data[cs2_asset.PART_TABLE_PROP]),
                              f"{name}_LOD{level}", t, dl, protect_prefixes)
        if abs(info["tris"] - t) > tol * max(t, 1):
            raise RuntimeError(f"{lod.name}: {info['tris']} tris vs target {t} (+-{tol:.0%})")
        print(f"[lod] {lod.name}: {info}")
        lods.append(lod)
        infos.append(info)
    return lods, infos


# ======================================================================================
# Painters (section 4.6): numpy, 4x supersampled SDF, bottom-up rows; called through
# cs2_compose.stamp_on_face(base, ctl, me, table, prefix, painter). `base` is LINEAR.
# ======================================================================================

SS = 4


def _grid(rect):
    r0, r1, c0, c1 = rect
    h, w = r1 - r0, c1 - c0
    ys = (np.arange(h * SS) + 0.5) / (h * SS)
    xs = (np.arange(w * SS) + 0.5) / (w * SS)
    X, Y = np.meshgrid(xs, ys)
    return X, Y, h, w


def _down(a, h, w):
    return a.reshape(h, SS, w, SS).mean(axis=(1, 3))


def _blend(base, cov, rgb_lin):
    for c in range(3):
        base[..., c] = base[..., c] * (1.0 - cov) + rgb_lin[c] * cov


def _sd_disk(X, Y, cx, cy, r):
    return np.hypot(X - cx, Y - cy) - r


def _sd_box(X, Y, cx, cy, hx, hy):
    dx, dy = np.abs(X - cx) - hx, np.abs(Y - cy) - hy
    return np.hypot(np.maximum(dx, 0), np.maximum(dy, 0)) + np.minimum(np.maximum(dx, dy), 0)


def _sd_capsule(X, Y, ax, ay, bx, by, r):
    pax, pay, bax, bay = X - ax, Y - ay, bx - ax, by - ay
    h = np.clip((pax * bax + pay * bay) / max(bax * bax + bay * bay, 1e-12), 0, 1)
    return np.hypot(pax - bax * h, pay - bay * h) - r


def _cover(sd, px):
    """Anti-aliased coverage from a signed distance in normalised units (px = one
    supersample in those units)."""
    return np.clip(0.5 - sd / max(px, 1e-9), 0.0, 1.0)


def paint_cross_disc(rect, base, ctl, red=(0.72, 0.09, 0.08), white=0.92, ring=0.06):
    """White Greek cross (arm 0.62 d, width 0.20 d [K]) on a red disc with a white rim
    ring (R ruling "white cross on red" [DB 1.11]). Colours are sRGB-encoded; linearised
    here (skeptic MUST-CHANGE 7: raw they would encode pink)."""
    X, Y, h, w = _grid(rect)
    px = 1.0 / (max(h, w) * SS)
    disc = _cover(_sd_disk(X, Y, 0.5, 0.5, 0.5), px)
    inner = _cover(_sd_disk(X, Y, 0.5, 0.5, 0.5 - ring), px)
    arm = 0.31
    cross = _cover(np.minimum(_sd_box(X, Y, 0.5, 0.5, arm, 0.10),
                              _sd_box(X, Y, 0.5, 0.5, 0.10, arm)), px)
    red_l = srgb_to_linear(_rgb3(red))
    wl = srgb_to_linear(float(white))
    _blend(base, _down(disc, h, w), (wl, wl, wl))
    _blend(base, _down(inner, h, w), red_l)
    _blend(base, _down(inner * cross, h, w), (wl, wl, wl))


def _pictogram_sdf(kind, X, Y):
    if kind == "skier":
        head = _sd_disk(X, Y, 0.62, 0.80, 0.07)
        body = _sd_capsule(X, Y, 0.58, 0.68, 0.45, 0.45, 0.055)
        arm = _sd_capsule(X, Y, 0.56, 0.62, 0.72, 0.52, 0.035)
        pole = _sd_capsule(X, Y, 0.72, 0.52, 0.80, 0.22, 0.018)
        leg1 = _sd_capsule(X, Y, 0.45, 0.45, 0.56, 0.30, 0.045)
        leg2 = _sd_capsule(X, Y, 0.56, 0.30, 0.46, 0.20, 0.045)
        leg3 = _sd_capsule(X, Y, 0.45, 0.45, 0.36, 0.30, 0.045)
        ski = _sd_capsule(X, Y, 0.18, 0.22, 0.78, 0.10, 0.022)
        return np.minimum.reduce([head, body, arm, pole, leg1, leg2, leg3, ski])
    if kind == "restaurant":
        fork = np.minimum.reduce([_sd_box(X, Y, 0.36, 0.40, 0.03, 0.30),
                                  _sd_box(X, Y, 0.36, 0.70, 0.10, 0.03),
                                  _sd_box(X, Y, 0.28, 0.80, 0.02, 0.10),
                                  _sd_box(X, Y, 0.36, 0.80, 0.02, 0.10),
                                  _sd_box(X, Y, 0.44, 0.80, 0.02, 0.10)])
        knife = np.minimum(_sd_box(X, Y, 0.64, 0.40, 0.03, 0.30),
                           _sd_capsule(X, Y, 0.64, 0.62, 0.68, 0.88, 0.05))
        return np.minimum(fork, knife)
    if kind == "info":
        return np.minimum.reduce([_sd_box(X, Y, 0.5, 0.78, 0.36, 0.05),
                                  _sd_box(X, Y, 0.5, 0.58, 0.36, 0.035),
                                  _sd_box(X, Y, 0.45, 0.42, 0.31, 0.035),
                                  _sd_box(X, Y, 0.5, 0.26, 0.36, 0.035)])
    raise ValueError(f"paint_pictogram: unknown kind {kind!r}")


def paint_pictogram(kind, fg=0.92, bg=None):
    """Painter factory: `skier` (school), `restaurant` (fork + knife), `info` (bar lines).
    fg / bg are sRGB-encoded grey levels or rgb (bg None keeps the host)."""
    def painter(rect, base, ctl):
        X, Y, h, w = _grid(rect)
        px = 1.0 / (max(h, w) * SS)
        if bg is not None:
            _blend(base, np.ones((h, w)), srgb_to_linear(_rgb3(bg)))
        cov = _down(_cover(_pictogram_sdf(kind, X, Y), px), h, w)
        _blend(base, cov, srgb_to_linear(_rgb3(fg)))
    painter.__name__ = f"paint_pictogram_{kind}"
    return painter


SKI_PALETTE_SRGB = ((0.85, 0.12, 0.10), (0.10, 0.35, 0.80), (0.95, 0.75, 0.10),
                    (0.10, 0.60, 0.25), (0.92, 0.92, 0.92), (0.12, 0.12, 0.14),
                    (0.95, 0.40, 0.05))


def paint_skis(rect, base, ctl, seed=0, stripe_frac=None):
    """Vertical stripes (each ~0.09 m on a 4 m rack face: 1/44 of the width), saturated
    palette, crc32 seed (deterministic)."""
    r0, r1, c0, c1 = rect
    h, w = r1 - r0, c1 - c0
    n = max(4, int(round(1.0 / (stripe_frac or (1.0 / 44.0)))))
    rng = np.random.default_rng(int(seed) & 0xFFFFFFFF)
    cols = rng.integers(0, len(SKI_PALETTE_SRGB), size=n)
    gaps = rng.random(n) < 0.25
    xs = ((np.arange(w) + 0.5) / w * n).astype(int).clip(0, n - 1)
    for j in range(w):
        k = xs[j]
        if gaps[k]:
            continue
        rgb = srgb_to_linear(SKI_PALETTE_SRGB[cols[k]])
        top = 0.75 + 0.2 * ((k * 2654435761) % 97) / 97.0
        rows = int(h * top)
        for c in range(3):
            base[:rows, j, c] = rgb[c]


def paint_clock(rect, base, ctl):
    """White face, 12 ticks, hands at 10:10 [K convention], dark rim."""
    X, Y, h, w = _grid(rect)
    px = 1.0 / (max(h, w) * SS)
    wl = srgb_to_linear(0.92)
    dk = srgb_to_linear(0.08)
    face = _cover(_sd_disk(X, Y, 0.5, 0.5, 0.48), px)
    _blend(base, _down(face, h, w), (wl, wl, wl))
    sd = np.full(X.shape, 1e9)
    for k in range(12):
        a = 2 * math.pi * k / 12
        ca, sa = math.cos(a), math.sin(a)
        sd = np.minimum(sd, _sd_capsule(X, Y, 0.5 + 0.36 * sa, 0.5 + 0.36 * ca,
                                        0.5 + 0.44 * sa, 0.5 + 0.44 * ca, 0.015))
    for frac, L, r in ((10 / 12 + 10 / 720, 0.24, 0.025), (10 / 60, 0.36, 0.018)):
        a = 2 * math.pi * frac
        sd = np.minimum(sd, _sd_capsule(X, Y, 0.5, 0.5, 0.5 + L * math.sin(a),
                                        0.5 + L * math.cos(a), r))
    _blend(base, _down(_cover(sd, px), h, w) * _down(face, h, w), (dk, dk, dk))


def paint_text5x7(text, fg=0.92, bg=None):
    """Painter factory over ski_icons_sheet.FONT (5x7 bitmap, centred, 1-cell spacing).
    Available, unused by default (words do not localise, DB 1.11)."""
    import ski_icons_sheet   # noqa: E402  (lazy: it imports ski_icons)
    font = ski_icons_sheet.FONT
    glyphs = [font.get(ch.upper(), font[" "]) for ch in text]

    def painter(rect, base, ctl):
        r0, r1, c0, c1 = rect
        h, w = r1 - r0, c1 - c0
        if bg is not None:
            _blend(base, np.ones((h, w)), srgb_to_linear(_rgb3(bg)))
        cols = len(glyphs) * 6 - 1
        cell = min(w / max(cols, 1), h / 9.0)
        x0 = (w - cols * cell) / 2
        y0 = (h - 7 * cell) / 2
        cov = np.zeros((h, w))
        for gi, gl in enumerate(glyphs):
            for ry, row in enumerate(gl):
                for rx, bit in enumerate(row):
                    if bit != "1":
                        continue
                    xa = x0 + (gi * 6 + rx) * cell
                    ya = y0 + (6 - ry) * cell
                    ja, jb = int(math.floor(xa)), int(math.ceil(xa + cell))
                    ia, ib = int(math.floor(ya)), int(math.ceil(ya + cell))
                    for i in range(max(ia, 0), min(ib, h)):
                        for j in range(max(ja, 0), min(jb, w)):
                            ox = max(0.0, min(j + 1, xa + cell) - max(j, xa))
                            oy = max(0.0, min(i + 1, ya + cell) - max(i, ya))
                            cov[i, j] = min(1.0, cov[i, j] + ox * oy)
        _blend(base, cov, srgb_to_linear(_rgb3(fg)))
    painter.__name__ = "paint_text5x7"
    return painter


PAINTERS = {"paint_cross_disc": paint_cross_disc, "paint_skis": paint_skis,
            "paint_clock": paint_clock}
PAINTER_FACTORIES = {"paint_pictogram": paint_pictogram, "paint_text5x7": paint_text5x7}


def make_painter(painter, kwargs=None, face=None):
    """Resolve a recipe label entry: a callable, a PAINTERS name (kwargs bound; paint_skis
    gets seed = crc32(face)) or a PAINTER_FACTORIES name (called with kwargs)."""
    kwargs = dict(kwargs or {})
    if callable(painter):
        return functools.partial(painter, **kwargs) if kwargs else painter
    if painter == "display" and face is not None:
        kwargs.setdefault("face", face)      # detail pass 1: the label's own prefix
    if painter in PAINTER_FACTORIES:
        return PAINTER_FACTORIES[painter](**kwargs)
    if painter == "paint_skis" and "seed" not in kwargs:
        kwargs["seed"] = _seed(face or "")
    fn = PAINTERS[painter]
    return functools.partial(fn, **kwargs) if kwargs else fn


# --------------------------------------------------------------------------------------
# Detail pass 1 (L1, 2026-10-11): the resort glyph / menu painter. The glyph is the one the
# gondola and tram cabins carry (lift_cabin.glyph_alpha: ski_launcher_glyph.cand_c(), the
# shipped launcher glyph (c), peak + snowflake), rendered at the face's own texel size.
# Orientation comes from the baked mesh (UVUnique -> world affine of the single quad), not
# from the UV rect, so a packer turn or a mirrored box projection cannot rotate the sign.
# --------------------------------------------------------------------------------------

GLYPH_LIN = (0.85, 0.85, 0.85)          # lift_cabin.GLYPH_RGB: the cabins' logo white (linear)
GLYPH_SS = 4                            # lift_cabin.GLYPH_SS
SIGN_GREEN_SRGB = (0.07, 0.31, 0.25)    # sign ground: A-Basin's deck-sign green (encoded)
MENU_SRGB = (0.11, 0.11, 0.10)          # menu board ground (chalkboard, encoded)
MENU_INK_SRGB = (0.86, 0.85, 0.80)
_GLYPH_CACHE = {}


def _glyph_cov(size):
    """size x size coverage of the cabins' glyph, row 0 = top (ski_launcher_glyph, no bpy)."""
    size = int(size)
    if size not in _GLYPH_CACHE:
        import ski_launcher_glyph as slg     # noqa: E402  (lazy; the cabins' source)
        _GLYPH_CACHE[size] = np.asarray(slg.render_alpha(slg.cand_c(), size, ss=GLYPH_SS),
                                        dtype=np.float32)
    return _GLYPH_CACHE[size]


def _label_quad(face):
    """(obj, me, poly index) of the ONE single-quad part `face` on the mesh being baked (the
    only mesh object with a part table, a UVUnique layer and faces of that part)."""
    hits = []
    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        me = ob.data
        if cs2_asset.PART_TABLE_PROP not in me or cs2_uv.UNIQUE_LAYER not in me.uv_layers:
            continue
        table = json.loads(me[cs2_asset.PART_TABLE_PROP])
        if not any(n.startswith(face) for n in table):
            continue
        fs = cs2_compose.faces_of_prefix(me, table, face)
        if fs:
            hits.append((ob, me, fs, table))
    if len(hits) != 1:
        raise RuntimeError(f"paint_glyph {face!r}: {len(hits)} baked meshes carry it (need 1)")
    ob, me, fs, table = hits[0]
    if len(fs) != 1 or me.polygons[fs[0]].loop_total != 4:
        raise RuntimeError(f"paint_glyph {face!r}: expected one quad, found {len(fs)} face(s)")
    return ob, me, fs[0], table


def _label_frame(face, rect):
    """UV -> world affine of the quad, its viewer frame (right, up, centre), its extents
    (s0, s1, t0, t1) in metres and the atlas size A (the one whose UV bbox gives `rect`)."""
    ob, me, f, table = _label_quad(face)
    poly = me.polygons[f]
    uvl = me.uv_layers[cs2_uv.UNIQUE_LAYER].data
    mw = ob.matrix_world
    Q = np.array([[uvl[li].uv[0], uvl[li].uv[1], 1.0] for li in poly.loop_indices], np.float64)
    Pw = np.array([list(mw @ me.vertices[me.loops[li].vertex_index].co)
                   for li in poly.loop_indices], np.float64)
    M, *_ = np.linalg.lstsq(Q, Pw, rcond=None)
    n = (mw.to_3x3() @ poly.normal).normalized()
    up = Vector((0.0, 0.0, 1.0))
    up = (up - n * up.dot(n)).normalized()
    right = (-n).cross(up)
    R, U, C = np.array(list(right)), np.array(list(up)), Pw.mean(axis=0)
    s = (Pw - C) @ R
    t = (Pw - C) @ U
    A = None
    for cand in (256, 512, 1024, 2048, 4096, 8192):
        if cs2_compose.uv_bbox_px(me, table, face, cand) == tuple(rect):
            A = cand
            break
    if A is None:
        raise RuntimeError(f"paint_glyph {face!r}: no atlas size gives the rect {rect}")
    return M, R, U, C, (float(s.min()), float(s.max()), float(t.min()), float(t.max())), A


def _bilinear(img, gx, gy):
    N0, N1 = img.shape
    x0 = np.clip(np.floor(gx).astype(int), 0, N1 - 1)
    y0 = np.clip(np.floor(gy).astype(int), 0, N0 - 1)
    x1, y1 = np.clip(x0 + 1, 0, N1 - 1), np.clip(y0 + 1, 0, N0 - 1)
    fx = np.clip(gx - np.floor(gx), 0.0, 1.0)
    fy = np.clip(gy - np.floor(gy), 0.0, 1.0)
    return (img[y0, x0] * (1 - fx) * (1 - fy) + img[y0, x1] * fx * (1 - fy)
            + img[y1, x0] * (1 - fx) * fy + img[y1, x1] * fx * fy)


def _menu_sdf(X, Y):
    """Menu board layout in face units (X right, Y down, 0..1): a title bar and six rows
    (item bar + price bar), as signed distances (< 0 inside)."""
    sd = _sd_box(X, Y, 0.50, 0.12, 0.30, 0.035)
    for k in range(6):
        y = 0.27 + k * 0.115
        wl = (0.30, 0.22, 0.27, 0.18, 0.25, 0.21)[k]
        sd = np.minimum(sd, _sd_box(X, Y, 0.10 + wl, y, wl, 0.020))
        sd = np.minimum(sd, _sd_box(X, Y, 0.83, y, 0.06, 0.020))
    return sd


def paint_glyph(face, kind="resort", bg=None, fg_lin=GLYPH_LIN, glyph_frac=0.86,
                keyline=(0.045, 0.012)):
    """Painter factory (PAINTER_FACTORIES "paint_glyph"; recipe label kwargs carry `face`,
    the label's part prefix). kind "resort": the cabins' glyph in white on SIGN_GREEN, inside
    a white keyline (inset, stroke as fractions of the face height), glyph square =
    glyph_frac x the face's short side, centred; kind "menu": chalkboard ground, a title bar
    and six item / price rows. Paints every texel of the rect (the island's UV bbox) with the
    ground, so no grey label texel survives at the island edge."""
    def painter(rect, base, ctl):
        r0, r1, c0, c1 = rect
        M, R, U, C, (s0, s1, t0, t1), A = _label_frame(face, rect)
        rr, cc = np.mgrid[r0:r1, c0:c1]
        uv1 = np.stack([(cc + 0.5) / A, (rr + 0.5) / A, np.ones(rr.shape)], axis=-1)
        D = uv1 @ M - C
        s, t = D @ R, D @ U
        wq, hq = s1 - s0, t1 - t0
        X = (s - s0) / wq                    # 0 left .. 1 right (viewer's)
        Y = (t1 - t) / hq                    # 0 top .. 1 bottom
        ppm = max(r1 - r0, c1 - c0) / max(wq, hq)
        pix = 1.0 / max(ppm * max(wq, hq), 1.0)          # one texel in face units
        ground = bg if bg is not None else (MENU_SRGB if kind == "menu" else SIGN_GREEN_SRGB)
        _blend(base, np.ones(X.shape), srgb_to_linear(_rgb3(ground)))
        if kind == "menu":
            cov = np.clip(0.5 - _menu_sdf(X, Y) / pix, 0.0, 1.0)
            _blend(base, cov, srgb_to_linear(_rgb3(MENU_INK_SRGB)))
            return
        if kind != "resort":
            raise ValueError(f"paint_glyph: kind {kind!r}")
        if keyline:
            ins, st = keyline
            ring = np.abs(_sd_box(X, Y, 0.5, 0.5, 0.5 - ins, 0.5 - ins)) - st / 2
            _blend(base, np.clip(0.5 - ring / pix, 0.0, 1.0), fg_lin)
        side = glyph_frac * min(wq, hq)
        N = max(16, int(round(side * ppm)))
        cov = _glyph_cov(N)
        gx = ((s - 0.5 * (s0 + s1)) / side + 0.5) * N - 0.5
        gy = (0.5 - (t - 0.5 * (t0 + t1)) / side) * N - 0.5
        inside = (np.abs(s - 0.5 * (s0 + s1)) <= side / 2) & (np.abs(t - 0.5 * (t0 + t1)) <= side / 2)
        g = np.where(inside, _bilinear(cov, gx, gy), 0.0).astype(np.float32)
        _blend(base, g, fg_lin)
    painter.__name__ = f"paint_glyph_{kind}"
    return painter


PAINTER_FACTORIES["paint_glyph"] = paint_glyph


# --------------------------------------------------------------------------------------
# Detail pass 1, school / L2 / patrol (kit group, 2026-10-11): oriented display painters
# (design/detail-pass-1.md 2.0 / 6.1 with skeptic M1 + M4). Built on _label_quad /
# _label_frame (the UVUnique -> world affine of the ONE quad being stamped; no second port of
# _civic.uv_frame): every texel of the stamp rect is supersampled LABEL_SS x LABEL_SS and
# each sample is mapped to FACE metres as the viewer sees the face from outside (x right
# from the left edge, y down from the top edge), so words read left to right on any wall
# side, any island turn, any mirror. Draw functions paint layers on a DisplayCanvas.
# Text uses the 5x7 font of ski_launcher_glyph (the same table as ski_icons_sheet.FONT,
# without importing ski_icons). label_metrics() is the layout the painter really uses:
# columns 6 n - 1, rows 7 L + gap (L - 1) + 2 (= 9 per line at the default gap), cell =
# fill x min(W / cols, H / rows); cap = 7 cells, stroke = 1 cell (skeptic M4).
# --------------------------------------------------------------------------------------

LABEL_FILL = 0.85           # text block / board, design 2.0 (the word boards: build report)
LABEL_SS = 4                # supersamples per texel axis
WHITE_SRGB = 0.92
BOARD_BROWN_SRGB = (0.20, 0.14, 0.10)    # school board brown (= the slot-3 roof target)
PATROL_RED_SRGB = (0.72, 0.09, 0.08)     # = paint_cross_disc red
HUT_RED_SRGB = (0.62, 0.10, 0.08)
CHALK_SRGB = (0.10, 0.22, 0.16)
BIB_SRGB = ((0.95, 0.45, 0.05), (0.98, 0.80, 0.10))   # orange / yellow training bibs
LABEL_TIERS = {"T1": (0.40, 4.0), "T2": (0.0, 2.0)}  # (cap >= m, stroke >= texels), design 2.0


def _font5x7():
    import ski_launcher_glyph as slg     # noqa: E402  (lazy; numpy only)
    return slg.FONT


def label_metrics(face_w, face_h, lines, ppm, fill=LABEL_FILL, line_gap=2):
    """The 5x7 text layout draw_text uses, for a face of face_w x face_h metres shipped at
    `ppm` texels per metre: cols = 6 n - 1 (n = longest line), rows = 7 L + line_gap (L - 1)
    + 2, cell = fill x min(face_w / cols, face_h / rows); cap = 7 cell, stroke = 1 cell.
    Returns the numbers (unrounded) and the legibility tier: T1 (reads at 50 m: cap >= 0.40
    m and stroke >= 4 texels), T2 (close-up: stroke >= 2 texels) or "none"."""
    lines = (lines,) if isinstance(lines, str) else tuple(lines)
    if not lines or max(len(s) for s in lines) == 0:
        raise ValueError("label_metrics: no text")
    L = len(lines)
    n = max(len(s) for s in lines)
    cols = 6 * n - 1
    rows = 7 * L + line_gap * (L - 1) + 2
    cell = float(fill) * min(float(face_w) / cols, float(face_h) / rows)
    cap = 7.0 * cell
    sp = cell * float(ppm)
    if cap >= LABEL_TIERS["T1"][0] - 1e-9 and sp >= LABEL_TIERS["T1"][1] - 1e-9:
        tier = "T1"
    elif sp >= LABEL_TIERS["T2"][1] - 1e-9:
        tier = "T2"
    else:
        tier = "none"
    return dict(lines=list(lines), n=n, L=L, cols=cols, rows=rows, fill=float(fill),
                line_gap=int(line_gap), ppm=float(ppm), cell_m=cell, cap_m=cap, stroke_m=cell,
                stroke_px=sp, block_w_m=cols * cell, block_h_m=(7 * L + line_gap * (L - 1)) * cell,
                tier=tier)


class DisplayCanvas:
    """What a draw function receives. x, y: (h, w, S) sample positions in face metres (x
    right from the face's left edge, y down from its top edge, as seen from outside); W, H
    the face size (m); ppm the shipped texels per metre; base the (h, w, >=3) LINEAR
    BaseColor view of the stamp rect; last the painter's record dict."""

    def __init__(self, base, x, y, W, H, ppm, last):
        self.base, self.x, self.y = base, x, y
        self.W, self.H, self.ppm = float(W), float(H), float(ppm)
        self.last = last

    def cover(self, samples):
        """(h, w) coverage = the mean of a bool / 0..1 sample array (h, w, S)."""
        return np.asarray(samples, dtype=np.float64).mean(axis=-1)

    def paint(self, cov, rgb_srgb):
        _blend(self.base, cov, srgb_to_linear(_rgb3(rgb_srgb)))

    def fill(self, rgb_srgb):
        self.paint(np.ones(self.base.shape[:2]), rgb_srgb)

    def record(self, kind, info):
        self.last.setdefault("draws", []).append({"kind": kind, **info})


class DisplayPainter:
    """Painter for cs2_compose.stamp_on_face on ONE single-quad part `face` (the label's part
    prefix): maps the stamp rect's texel sub-samples through the quad's UVUnique -> world
    affine (_label_frame) to face metres and runs `draw` (a draw function or a sequence of
    them, see DRAWS) on a DisplayCanvas. symmetric=True allows a face whose normal is
    vertical (no viewer "up": the quad's first edge gives the frame; the drawing must not
    care); sloped faces (roof crosses) use the up-slope direction as up either way.
    `.last` records rect, atlas size, ppm, face size and every draw's numbers (text cap /
    stroke / tier, pictogram square, ...) for tests and the manifest `labels` block."""

    def __init__(self, face, draw, symmetric=False, ss=LABEL_SS):
        self.face = str(face)
        self.draws = list(draw) if isinstance(draw, (list, tuple)) else [draw]
        if not self.draws or not all(callable(d) for d in self.draws):
            raise TypeError(f"DisplayPainter {face!r}: draw must be callable(s)")
        self.symmetric = bool(symmetric)
        self.ss = int(ss)
        self.__name__ = f"display_{self.face}"
        self.last = None

    def _frame(self, rect):
        M, R, U, C, ext, A = _label_frame(self.face, rect)
        R, U = np.asarray(R, np.float64), np.asarray(U, np.float64)
        if np.linalg.norm(R) > 0.5 and np.linalg.norm(U) > 0.5 and np.all(np.isfinite(R)):
            return M, R, U, np.asarray(C, np.float64), ext, A
        if not self.symmetric:
            raise RuntimeError(f"DisplayPainter {self.face!r}: the face is horizontal (no "
                               f"viewer up); only a symmetric drawing may go there")
        ob, me, f, _table = _label_quad(self.face)
        mw = ob.matrix_world
        poly = me.polygons[f]
        Pw = np.array([list(mw @ me.vertices[me.loops[li].vertex_index].co)
                       for li in poly.loop_indices], np.float64)
        n = np.array(list((mw.to_3x3() @ poly.normal).normalized()), np.float64)
        R = Pw[1] - Pw[0]
        R /= np.linalg.norm(R)
        U = np.cross(n, R)
        C = Pw.mean(axis=0)
        s, t = (Pw - C) @ R, (Pw - C) @ U
        return M, R, U, C, (float(s.min()), float(s.max()), float(t.min()), float(t.max())), A

    def __call__(self, rect, base, ctl):
        r0, r1, c0, c1 = (int(v) for v in rect)
        M, R, U, C, (s0, s1, t0, t1), A = self._frame(rect)
        h, w, k = r1 - r0, c1 - c0, self.ss
        off = (np.arange(k, dtype=np.float64) + 0.5) / k
        rr = (np.arange(r0, r1, dtype=np.float64)[:, None, None, None]
              + off[None, None, :, None])
        cc = (np.arange(c0, c1, dtype=np.float64)[None, :, None, None]
              + off[None, None, None, :])
        rr, cc = np.broadcast_arrays(rr, cc)
        D = (np.stack([cc / A, rr / A, np.ones(rr.shape)], axis=-1) @ M) - C
        x = (D @ R - s0).reshape(h, w, k * k)
        y = (t1 - D @ U).reshape(h, w, k * k)
        ppm_u = A / max(float(np.linalg.norm(M[0])), 1e-12)
        ppm_v = A / max(float(np.linalg.norm(M[1])), 1e-12)
        ppm = 0.5 * (ppm_u + ppm_v)
        last = {"face": self.face, "rect": [r0, r1, c0, c1], "A": int(A),
                "ppm": round(ppm, 3), "ppm_uv": [round(ppm_u, 3), round(ppm_v, 3)],
                "size_m": [round(s1 - s0, 4), round(t1 - t0, 4)], "symmetric": self.symmetric,
                "right": [round(float(v), 4) for v in R], "up": [round(float(v), 4) for v in U]}
        cv = DisplayCanvas(base, x, y, s1 - s0, t1 - t0, ppm, last)
        for d in self.draws:
            d(cv)
        self.last = last
        kinds = [d.get("kind") for d in last.get("draws", [])]
        print(f"[bldg] display {self.face}: {w} x {h} texels at {ppm:.1f} px/m, "
              f"face {s1 - s0:.2f} x {t1 - t0:.2f} m, draws {kinds}")


def _text_samples(cv, text, x0, y0, cell):
    """(h, w, S) bool: samples inside the 5x7 cells of `text` laid out from (x0, y0) (face
    metres, top-left of the first glyph's cell box) at `cell` metres per font pixel."""
    font = _font5x7()
    bad = [c for c in text if c.upper() not in font]
    if bad:
        raise ValueError(f"draw_text: {bad} not in the 5x7 font")
    bm = np.array([[[b == "1" for b in row] for row in font[c.upper()]] for c in text], bool)
    n = len(text)
    gx = np.floor((cv.x - x0) / cell).astype(np.int64)
    gy = np.floor((cv.y - y0) / cell).astype(np.int64)
    ok = (gx >= 0) & (gx < 6 * n - 1) & (gy >= 0) & (gy < 7) & (gx % 6 < 5)
    out = np.zeros(cv.x.shape, bool)
    out[ok] = bm[gx[ok] // 6, gy[ok], gx[ok] % 6]
    return out


def draw_text(lines, fg=WHITE_SRGB, bg=None, fill=LABEL_FILL, line_gap=2):
    """Words on a board: `lines` (a string or a sequence), each line centred, the block
    centred, laid out by label_metrics (records its numbers: cap_m, stroke_m, stroke_px,
    tier). fg / bg encoded sRGB (bg None keeps what is under it)."""
    lines = (lines,) if isinstance(lines, str) else tuple(lines)

    def draw(cv):
        if bg is not None:
            cv.fill(bg)
        m = label_metrics(cv.W, cv.H, lines, cv.ppm, fill=fill, line_gap=line_gap)
        cell = m["cell_m"]
        y0 = 0.5 * (cv.H - m["block_h_m"])
        ins = np.zeros(cv.x.shape, bool)
        for i, s in enumerate(lines):
            lw = (6 * len(s) - 1) * cell
            ins |= _text_samples(cv, s, 0.5 * (cv.W - lw), y0 + i * (7 + line_gap) * cell, cell)
        cv.paint(cv.cover(ins), fg)
        cv.record("text", {k: (round(v, 5) if isinstance(v, float) else v) for k, v in m.items()})
    return draw


_GLYPH_PRIM_CACHE = {}


def _glyph_samples(cv, prims, xc, yc, side):
    """(h, w, S) 0..1 coverage of a ski_launcher_glyph primitive list (a function name, e.g.
    "cand_d" the skier on a slope, "cand_c" the resort glyph) drawn in a side x side square
    centred at (xc, yc) face metres, upright as the viewer sees it."""
    import ski_launcher_glyph as slg     # noqa: E402
    N = int(min(512, max(32, math.ceil(side * cv.ppm * 4.0))))
    key = (prims, N)
    if key not in _GLYPH_PRIM_CACHE:
        _GLYPH_PRIM_CACHE[key] = np.asarray(slg.render_alpha(getattr(slg, prims)(), N, ss=4),
                                            dtype=np.float32)
    img = _GLYPH_PRIM_CACHE[key]
    gx = ((cv.x - xc) / side + 0.5) * N - 0.5
    gy = ((cv.y - yc) / side + 0.5) * N - 0.5
    inside = (np.abs(cv.x - xc) <= side / 2) & (np.abs(cv.y - yc) <= side / 2)
    return np.where(inside, _bilinear(img, gx, gy), 0.0)


def draw_glyph(prims="cand_d", fg=WHITE_SRGB, frac=0.85, cx=0.5, cy=0.5, bg=None):
    """A ski_launcher_glyph primitive list in a square of frac x the face's short side,
    centred at (cx, cy) (fractions of the face, x right, y down)."""
    def draw(cv):
        if bg is not None:
            cv.fill(bg)
        side = float(frac) * min(cv.W, cv.H)
        cv.paint(cv.cover(_glyph_samples(cv, prims, cx * cv.W, cy * cv.H, side)), fg)
        cv.record("glyph", {"prims": prims, "side_m": round(side, 4)})
    return draw


SHAPE_KINDS = ("circle", "square", "diamond", "cross")


def draw_shapes(items, bg=WHITE_SRGB):
    """Pictogram shapes on a ground: items = ((kind, cx, cy, size, rgb_srgb), ...), kind in
    SHAPE_KINDS or "glyph:<prims>" (draw_glyph's primitive lists); cx, cy fractions of the
    face (x right, y down), size = the shape's extent as a fraction of the face's short
    side (circle diameter, square side, diamond diagonal, cross arm span)."""
    items = tuple(items)
    for it in items:
        if not (it[0] in SHAPE_KINDS or str(it[0]).startswith("glyph:")):
            raise ValueError(f"draw_shapes: kind {it[0]!r}")

    def draw(cv):
        if bg is not None:
            cv.fill(bg)
        S = min(cv.W, cv.H)
        rec = []
        for kind, cx, cy, size, rgb in items:
            X, Y = cv.x - cx * cv.W, cv.y - cy * cv.H
            r = 0.5 * float(size) * S
            if kind == "circle":
                ins = X * X + Y * Y <= r * r
            elif kind == "square":
                ins = (np.abs(X) <= r) & (np.abs(Y) <= r)
            elif kind == "diamond":
                ins = np.abs(X) + np.abs(Y) <= r
            elif kind == "cross":
                ins = ((np.abs(X) <= r) & (np.abs(Y) <= 0.32 * r)) | \
                      ((np.abs(X) <= 0.32 * r) & (np.abs(Y) <= r))
            else:
                ins = _glyph_samples(cv, kind.split(":", 1)[1], cx * cv.W, cy * cv.H, 2 * r)
            cv.paint(cv.cover(ins), rgb)
            rec.append([kind, round(2 * r, 4)])
        cv.record("shapes", {"items": rec})
    return draw


def draw_pictogram(kind, square=True, fg=WHITE_SRGB, bg=None, frac=1.0):
    """paint_pictogram's shapes (skier / restaurant / info) in a centred square of frac x
    the face's short side (square=True: no 4:1 stretch on a long board, the L2 sign fix) or
    over the whole face (square=False, the old paint_pictogram behaviour)."""
    def draw(cv):
        if bg is not None:
            cv.fill(bg)
        if square:
            sw = sh = float(frac) * min(cv.W, cv.H)
        else:
            sw, sh = float(frac) * cv.W, float(frac) * cv.H
        x0, y0 = 0.5 * (cv.W - sw), 0.5 * (cv.H - sh)
        X = (cv.x - x0) / sw
        Y = 1.0 - (cv.y - y0) / sh                 # _pictogram_sdf: y up
        ins = (_pictogram_sdf(kind, X, Y) < 0.0) & (X >= 0) & (X <= 1) & (Y >= 0) & (Y <= 1)
        cv.paint(cv.cover(ins), fg)
        cv.record("pictogram", {"pictogram": kind, "square": bool(square),
                                "box_m": [round(sw, 4), round(sh, 4)],
                                "origin_m": [round(x0, 4), round(y0, 4)]})
    return draw


def draw_cross_square(red=PATROL_RED_SRGB, white=WHITE_SRGB, rim=0.06, arm=0.31, half_w=0.10):
    """White Greek cross on a red square with a white rim (the patrol roof crosses, design
    P1-2 / P2-2): in the centred square of the face's short side S: white ground, red inset
    by rim x S, cross arms 2 arm x S long and 2 half_w x S wide (paint_cross_disc's
    proportions). Symmetric: any frame reads the same."""
    def draw(cv):
        S = min(cv.W, cv.H)
        X, Y = (cv.x - 0.5 * cv.W) / S, (cv.y - 0.5 * cv.H) / S
        cv.fill(white)
        inner = (np.abs(X) <= 0.5 - rim) & (np.abs(Y) <= 0.5 - rim)
        cv.paint(cv.cover(inner), red)
        cross = ((np.abs(X) <= arm) & (np.abs(Y) <= half_w)) | \
                ((np.abs(X) <= half_w) & (np.abs(Y) <= arm))
        cv.paint(cv.cover(cross & inner), white)
        cv.record("cross_square", {"side_m": round(S, 4), "arm_m": round(2 * arm * S, 4),
                                   "arm_width_m": round(2 * half_w * S, 4),
                                   "arm_width_px": round(2 * half_w * S * cv.ppm, 2),
                                   "rim_m": round(rim * S, 4)})
    return draw


def draw_bibs(n=8, seed=0, colours=BIB_SRGB, num_fg=WHITE_SRGB, bg=BOARD_BROWN_SRGB):
    """Training bibs hung in one row (school bib board, design S3): n vests (a rectangle with
    a neck notch, orange / yellow in a crc32-seeded order), each with its number 1..n in
    the 5x7 font, cap = min(0.55 x bib width, 0.30 x bib height) (records cap / stroke)."""
    def draw(cv):
        if bg is not None:
            cv.fill(bg)
        slot = cv.W / n
        bw = 0.88 * slot
        bh = min(0.70 * cv.H, 2.6 * bw)
        top = 0.5 * (cv.H - bh)
        order = np.random.default_rng(int(seed) & 0xFFFFFFFF).permutation(n)
        cap = min(0.55 * bw, 0.30 * bh)
        cell = cap / 7.0
        num = np.zeros(cv.x.shape, bool)
        for i in range(n):
            xc = (i + 0.5) * slot
            X, Y = cv.x - xc, cv.y - top
            vest = (np.abs(X) <= bw / 2) & (Y >= 0) & (Y <= bh)
            neck = X * X + Y * Y <= (0.22 * bw) ** 2
            cv.paint(cv.cover(vest & ~neck), colours[int(order[i]) % len(colours)])
            s = str(i + 1)
            lw = (6 * len(s) - 1) * cell
            num |= _text_samples(cv, s, xc - lw / 2, top + 0.45 * bh - 3.5 * cell, cell)
        cv.paint(cv.cover(num), num_fg)
        cv.record("bibs", {"n": n, "cap_m": round(cap, 4), "stroke_m": round(cell, 5),
                           "stroke_px": round(cell * cv.ppm, 3)})
    return draw


def draw_notices(seed=0, n=6, bg=(0.46, 0.34, 0.22),
                 papers=((0.93, 0.93, 0.90), (0.95, 0.90, 0.62), (0.80, 0.88, 0.95),
                         (0.95, 0.82, 0.84))):
    """A notice board (school, design S4): n pinned sheets on cork in a 2-column grid with a
    crc32-seeded jitter, each with grey text bars and a coloured pin."""
    def draw(cv):
        if bg is not None:
            cv.fill(bg)
        rng = np.random.default_rng(int(seed) & 0xFFFFFFFF)
        cols = 2
        rows = int(math.ceil(n / cols))
        cw, ch = cv.W / cols, cv.H / rows
        ink = np.zeros(cv.x.shape, bool)
        pins = []
        for i in range(n):
            cx = (i % cols + 0.5) * cw + rng.uniform(-0.06, 0.06) * cw
            cy = (i // cols + 0.5) * ch + rng.uniform(-0.06, 0.06) * ch
            pw, ph = rng.uniform(0.70, 0.82) * cw, rng.uniform(0.72, 0.84) * ch
            X, Y = cv.x - cx, cv.y - cy
            sheet = (np.abs(X) <= pw / 2) & (np.abs(Y) <= ph / 2)
            cv.paint(cv.cover(sheet), papers[int(rng.integers(len(papers)))])
            for j in range(4):
                ly = -ph / 2 + ph * (0.30 + 0.16 * j)
                ll = pw * rng.uniform(0.45, 0.80)
                ink |= (np.abs(Y - ly) <= ph * 0.035) & (X >= -pw * 0.38) & (X <= -pw * 0.38 + ll)
            pins.append((cx, cy - ph * 0.40))
        cv.paint(cv.cover(ink), (0.35, 0.35, 0.35))
        pr = 0.035 * min(cw, ch)
        for k, (px, py) in enumerate(pins):
            dot = (cv.x - px) ** 2 + (cv.y - py) ** 2 <= pr * pr
            cv.paint(cv.cover(dot), PATROL_RED_SRGB if k % 2 == 0 else (0.10, 0.25, 0.70))
        cv.record("notices", {"n": n, "seed": int(seed)})
    return draw


def draw_logs(seed=0, bg=(0.10, 0.07, 0.05), wood=(0.62, 0.47, 0.30), bark=(0.28, 0.18, 0.10),
              r_m=(0.055, 0.085)):
    """Log ends of a firewood stack (patrol HQ, design P2-7): rows of discs (radius r_m
    range, crc32-seeded) on a dark gap colour, each a bark ring, end grain and a growth
    ring, in offset rows."""
    def draw(cv):
        if bg is not None:
            cv.fill(bg)
        rng = np.random.default_rng(int(seed) & 0xFFFFFFFF)
        rm = 0.5 * (r_m[0] + r_m[1])
        bark_s = np.zeros(cv.x.shape, bool)
        wood_s = np.zeros(cv.x.shape, bool)
        ring_s = np.zeros(cv.x.shape, bool)
        y, row, count = rm, 0, 0
        while y < cv.H + rm:
            x = rm * (1.0 + (row % 2))
            while x < cv.W + rm:
                r = rng.uniform(r_m[0], r_m[1])
                cx = x + rng.uniform(-0.15, 0.15) * rm
                cy = y + rng.uniform(-0.12, 0.12) * rm
                d2 = (cv.x - cx) ** 2 + (cv.y - cy) ** 2
                bark_s |= d2 <= r * r
                wood_s |= d2 <= (0.84 * r) ** 2
                ring_s |= np.abs(np.sqrt(d2) - 0.48 * r) <= 0.06 * r
                x += 2.0 * rm
                count += 1
            y += 1.75 * rm
            row += 1
        cv.paint(cv.cover(bark_s), bark)
        cv.paint(cv.cover(wood_s), wood)
        cv.paint(cv.cover(ring_s & wood_s), tuple(0.75 * c for c in wood))
        cv.record("logs", {"discs": count, "r_m": list(r_m)})
    return draw


DRAWS = {"text": draw_text, "shapes": draw_shapes, "glyph": draw_glyph,
         "pictogram": draw_pictogram, "cross_square": draw_cross_square, "bibs": draw_bibs,
         "notices": draw_notices, "logs": draw_logs}


def display(face, draws=(), symmetric=False):
    """PAINTER_FACTORIES "display": a DisplayPainter from plain recipe data, e.g.
    ("sign_school_face", "display", {"draws": (("text", {"lines": ("SKI SCHOOL",),
    "bg": BOARD_BROWN_SRGB}),)}); `face` defaults to the label's prefix in make_painter.
    draws = ((DRAWS name, kwargs), ...) or callables (a menu board is paint_glyph
    kind="menu", skeptic M1)."""
    fns = []
    for d in draws:
        if callable(d):
            fns.append(d)
        else:
            kind, kw = d
            if kind not in DRAWS:
                raise KeyError(f"display: draw {kind!r} (have {sorted(DRAWS)})")
            fns.append(DRAWS[kind](**dict(kw)))
    return DisplayPainter(face, fns, symmetric=symmetric)


PAINTER_FACTORIES["display"] = display
