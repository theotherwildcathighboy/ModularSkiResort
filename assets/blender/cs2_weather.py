"""
cs2_weather.py - the weathering layer of the art pass (detail pass 1). It composites these effects
in LINEAR light over a finished texture set:
- edge wear
- grime (ground contact, under eaves, cavities, door thresholds)
- rust streaks below bolts and below galvanized fittings
- scuffs on walking surfaces and crash pads
- optional snow caps

Contract: docs/research/art/assets-2026-10/design/weathering-layer.md (sections 2-8). The build
report is docs/research/art/assets-2026-10/build/weathering-layer.md.

Where it runs
-------------
Inside cs2_bake.bake_unique's post_compose slot, as a WRAPPER around the generator's own hook
(lift_terminal_detach.py:2541-2555 idiom):

    hook = make_weather_hook(inner, spec, mat_keys=keys, detail_specs=dspecs,
                             stamped=[p for p, _, _ in recipe.labels], compose_contact=...)
    cs2_bake.bake_unique(..., post_compose=hook)

The maps are LINEAR there, with rows bottom-up (row 0 = v 0), AO already multiplied into base, and
reserved rects already restored. bake_unique clips the maps, sets the channel invariants and does
the sRGB encode afterwards (cs2_bake step 5). Nothing here touches color spaces.

Public API
----------
    PartWeather(role, scale, edge, grime, rust, scuff, snow, rust_source, slot_bound)
    WeatherSpec(name, kind="building", intensity=0.40, ..., tone=1.0,
                slot_wear="free")  .key() .validate() .preset()
                (defaults per DECISIONS 171, 2026-10-11: slots may weather beyond
                +-10 %, intensity = the highest budget-clean step on L2 with free slots,
                snow stays 0.0. A building may raise its own intensity to its own highest
                budget-clean step: lodge.py --weather-sweep)
    WeatherMaps(base, nrm, msk, ctl, ao=None)
    build_fields(me, A, spec, maps=None, *, uv_layer, matrix, part_table, mat_keys,
                 mat_roles, detail_specs, stamped, reserved, protect_px) -> WeatherFields
    compute_masks(maps, fields, spec, *, compose_contact=False) -> dict of (N,) masks
    apply_weathering(maps, fields, spec, *, compose_contact=False, gutter_px=3,
                     effects=EFFECTS, masks=None) -> WeatherMaps (new base/nrm/msk.
                     ctl and ao are the SAME objects, never written)
    weather_report(before, after, fields, spec, masks=None) -> dict (vanilla VL1-VL5,
                     perceptual dL*, guards, coverage, sources, roles, seed, key)
    perceptual_delta(before, after, fields, spec, *, tint=None) -> dict (mean |dL*| and
                     share |dL*| > 2 over the visible texels, optionally slot-tinted)
    make_weather_hook(inner, spec, *, ...) -> WeatherHook (post_compose signature)

Fix pass 1 (2026-10-11, build report "Fix pass 1"):
- visibility retune (edge band >= 3 texels, 0.6 m deeper ground band, large-scale tone, AO-aware
  eave grime)
- slot_wear "bounded" | "free" (Q2 is one spec field)
- scuffs fixed in meters and oriented by the part axis (no radial sunburst)
- gutter deltas slot-bounded
- the tower preset keeps the concrete foundation `pad` out of the crash-pad role
- `protect_px` for painter sub-rects

    hash3 / vnoise3 / fbm / uniform   world-meter hashed value noise (no tiling, no seams)
    ROLES, ROLE_COEFF, MAT_ROLES, PREFIX_ROLES   the role tables (design section 3.3)

Masks are COMPACT: one value per covered texel, in the order of fields.idx (flat row-major indices,
rows bottom-up). fields.full(name) / scatter(fields, arr) give (A, A) arrays.

Determinism:
- seed = zlib.crc32(asset name) (or spec.seed), with per-effect sub-seeds crc32(f"{name}:{effect}")
- uint32 hash arithmetic (murmur3 fmix32)
- polygons and parts in index order
- no RNG state, no wall clock in the outputs

Imports numpy, zlib, json, math, hashlib, dataclasses, cs2_compose (public API plus the private
lattice helpers cs2_bldg already uses) and cs2_asset.PART_TABLE_PROP. Never imports cs2_bldg,
bldg_recipes, lodge or recipes.*. bpy is used only through the mesh it is handed. No side effects at
import (beyond the sibling-module path, as cs2_bake).
"""

import hashlib
import json
import math
import os
import sys
import time
import zlib
from dataclasses import dataclass, fields as dc_fields

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_compose  # noqa: E402
from cs2_compose import (  # noqa: E402  (private lattice helpers, as cs2_bldg uses them)
    _affine_uv_to_world, _dist_to_lattice, _inplane_world_axes, _poly_mask, _poly_normal,
    _rect_slices, _spec_for, _world_coord_field)
from cs2_asset import PART_TABLE_PROP  # noqa: E402,F401  (the part table key; read via cs2_compose)


# --------------------------------------------------------------------------------------
# Constants
# --------------------------------------------------------------------------------------

KINDS = ("building", "station", "tower", "carrier", "prop")
GROUND_MODES = ("grade", "plinth", "none")
SLOT_WEAR = ("bounded", "free")
#: section 6.6 order, with the large-scale tone first (material variation under the dirt)
EFFECTS = ("tone", "grime", "rust", "edge", "scuff", "snow")

LUMA = np.array((0.2126, 0.7152, 0.0722), dtype=np.float32)   # Rec.709 = cs2_bake.LUMA
GRIME_MUL = np.array((0.60, 0.56, 0.50), dtype=np.float32)    # darker, slightly warm
GRIME_LUMA_DARK = float(1.0 - GRIME_MUL @ LUMA)                # 0.436: luminance loss at d = 1
RUST_LIN = np.array((0.205, 0.072, 0.024), dtype=np.float32)  # ~ sRGB 0.49 / 0.30 / 0.17
SNOW_LIN = np.array((0.80, 0.82, 0.86), dtype=np.float32)
RUST_SMOOTH = 0.15
SNOW_SMOOTH = 0.10
GRIME_SMOOTH = 0.12
SCUFF_SMOOTH = 0.10
RUST_MAX = 0.85
EDGE_GREY = 0.35            # base <- mix(base, L, 0.35 e) * (1 + lift e)
SCUFF_GREY = 0.25
SCUFF_LIFT = 0.60

TEXEL_FLOOR = cs2_compose.TEXEL_FLOOR    # 2 texels: band widths never go below this
KAPPA0 = 20.0               # 1/m: a groove of ~5 cm radius normalizes normal-map curvature
CELL_M = 0.10               # cover grid (section 4.4)
COVER_K = 6                 # lowest down-facing layers kept per cell
COVER_EPS_M = 0.02          # a layer must be this far above the texel to count as cover
COVER_STEP_M = 0.06         # walls look up just outside their own plane
EDGE_CAP_M = 2.0
EDGE_THETA_DEG = (20.0, 60.0)
CONVEX_EPS = 1e-4
GROUND_EDGE_M = 0.02        # grade mode: edges lying on the grade touch the ground (k = 0)
AO_FLOOR = 0.08             # occ = ao * 0.92 + 0.08 (cs2_bake.AO_FLOOR)
COMPOSE_CONTACT_AMP = 0.06  # compose_maps' ground band: base *= 1 - 0.06 * contact
COMPOSE_CONTACT_BAND_M = 0.4

# noise scales (meters) - see the build report for the ones retuned from the design
EDGE_WIDTH_CELL_M = 0.30
#: edge band (fix pass 1): b = max(edge_band_m * (0.7 + 0.6 u), EDGE_MIN_TEXELS / ppm),
#: full weight out to EDGE_CORE * b, smoothstep to 0 at b. No area renormalization where
#: the floor binds (it was D3: at 39-46 px/m it left a 1-2 texel line nobody could see)
EDGE_MIN_TEXELS = 3.0
EDGE_VAR = (0.7, 0.6)
EDGE_CORE = 0.35
CHIP_CELL_M = 0.12
CHIP_LO, CHIP_HI = 0.35, 0.62
GROUND_CELL_M = 0.10
#: ground contact (fix pass 1): profile (1 - smoothstep(0, 1, t))^GROUND_EXP over the
#: 0.6 m band. The darkening at the contact is deepened by GROUND_GAIN relative to the
#: other grime terms: d = c * w_grime * (1 + (GROUND_GAIN - 1) * w_ground)
GROUND_EXP = 1.2
GROUND_GAIN = 2.0
#: AO terms (fix pass 1): the wired bake hands the layer the raw AO. The design's ranges
#: (cavity 1 - smoothstep(0.35, 0.85, ao), eave assist 0.6 (1 - smoothstep(0.4, 0.9, ao)))
#: grimed 46-68 % of the visible texels of every building. AO is already in the albedo, so
#: that was the "darken every crevice twice" the design warned about. It also spent the whole
#: vanilla budget on a uniform murk. Only deep occlusion counts now
CAVITY_AO = (0.08, 0.40)
CAVITY_AMP = 0.4
EAVE_AO = (0.25, 0.65)
EAVE_AO_AMP = 0.4
#: eave profile (1 - cover_h / eave_band_m)^EAVE_EXP (design 1.5): fuller right under the
#: soffit, so the designed ~5 % reaches the wall when the broad AO assist is narrowed
EAVE_EXP = 1.0
#: large-scale tone (fix pass 1): 1-3 m blotches on walls and roofs so flat planes stop
#: reading as CAD. Luminance x (1 + TONE_AMP * intensity * tone), tone in [-1, 1]
#: (+-4 % at intensity 0.5). Cells taller than wide: rain-washed, not camouflage
TONE_CELLS_M = (1.6, 1.6, 2.4)
TONE_OCTAVES = 3
TONE_AMP = 0.08
TONE_ROLES = ("timber", "paint", "paint_metal", "masonry", "roof_tile", "generic")
PLINTH_CELL_M = 0.40
EAVE_CELL_M = 0.30
STREAK_CELLS_M = (0.08, 0.60, 1.00)     # across, along (downhill), normal
STREAK_LO, STREAK_HI = 0.35, 0.75
RUST_COL_CELL_M = 0.15
SPOT_CELL_M = 0.10
SPOT_LO, SPOT_HI = 0.80, 0.95
SPOT_AMP = 0.6
CURTAIN_AMP = 0.5
CURTAIN_GAP_CELL_M = 0.25
#: geometric rust "sits on": the UNDILATED source bbox comes within this distance of the
#: target plane. Design 4.6 used 0.25 m on the 0.05-dilated corners. On L2 that let the
#: eave snow guards, 0.30 m outside the wall and up on the roof, streak 14 m of eave wall
#: (build-report deviation D7)
GEO_CONTACT_M = 0.10
GEO_LONG_M = 0.50           # sources longer than this across the streak drip at points
DRIP_CELL_M = 0.40
DRIP_LO, DRIP_HI = 0.55, 0.75
SNOW_CELL_M = 0.50
#: scuffs (fix pass 1, F3): cells FIXED in meters - along the part's principal axis,
#: across, z. Octave 0 is never faded. Octaves k >= 1 fade with the texel density
#: (fbm), so a 2048 -> 4096 rebake keeps the same lattice. It used to floor the across
#: cell at 2 texels, which moved every scuff
SCUFF_CELLS_M = (0.30, 0.06, 1.00)
SCUFF_LO, SCUFF_HI = 0.55, 0.80
#: door_d over which scuffs go from an isotropic worn patch (cells along x along) to
#: streaks along the part axis (F5: no radial direction anywhere, so no sunburst)
SCUFF_ISO_M = (0.6, 1.5)

VN_STD = 0.20022083556241982   # std of vnoise3 - 0.5: sqrt(v1^3 / 12), v1 = 181/231 (quintic)
NOISE_CHUNK = 1 << 20

LABEL_PREFIXES = ("emblem_", "sign_", "clock_", "skirack_")   # = cs2_bldg.LABEL_PREFIXES

ROLES = ("paint_steel", "paint", "paint_metal", "pad", "galv", "timber", "deck", "masonry",
         "roof_tile", "generic", "none")
ROLE_ID = {r: i for i, r in enumerate(ROLES)}
NONE_ID = ROLE_ID["none"]

# Section 3.3 at intensity 1.0, as retuned by the builder inside the design's +-50 % allowance after
# the L2 demo (build report "coefficients"). Wear was below the preview's resolving power. The
# changes:
# - edge lift x ~1.45
# - scuff x ~1.3, except deck (kept 0.60: at 0.70 the flat test deck leaves the VL4 budget)
# - roof_tile rust 0.30 -> 0.20 (the streak band below the L2 snow-guard rails read as dirt).
# Design values are in ROLE_COEFF_DESIGN.
#
# Fix pass 1 (visibility, budget re-checked on the visible texels at 0.35 / 0.5 / 0.7):
# - edge lift x ~1.4-1.6 again (the 3-texel band made it resolvable, the budget had room)
# - grime moved toward the LIGHT materials where it reads (masonry 0.55 -> 0.65, deck 0.50 -> 0.60)
# - grime moved off dark timber (0.55 -> 0.45: a 5 % darkening of a 0.03-0.05 albedo is |dL*| < 1
#   but costs as much VL1 as on stone) Beyond the design's +-50 % on edge lift: the fixer's brief
#   overrides it, recorded in the build report.
#
# The table columns are:
# - lift / dsmooth: edge-wear albedo lift and smoothness change
# - grime: darkening weight
# - rust: rust target
# - source: "lattice" | "geometric" | None
# - scuff
# - snow: eligible (subject to slot < 0.05 everywhere, so the table's "if not slot" == True here)
ROLE_COEFF = {
    "paint_steel": dict(lift=0.85, dsmooth=-0.25, grime=0.40, rust=0.55, source="lattice", scuff=0.40, snow=False),
    "paint":       dict(lift=0.70, dsmooth=-0.20, grime=0.40, rust=0.30, source=None, scuff=0.50, snow=True),
    "paint_metal": dict(lift=0.60, dsmooth=-0.15, grime=0.35, rust=0.35, source=None, scuff=0.25, snow=True),
    "pad":         dict(lift=0.80, dsmooth=-0.30, grime=0.45, rust=0.00, source=None, scuff=0.80, snow=False),
    "galv":        dict(lift=0.60, dsmooth=+0.10, grime=0.35, rust=0.30, source="geometric", scuff=0.25, snow=True),
    "timber":      dict(lift=0.45, dsmooth=-0.08, grime=0.45, rust=0.30, source=None, scuff=0.50, snow=True),
    "deck":        dict(lift=0.50, dsmooth=-0.10, grime=0.60, rust=0.20, source=None, scuff=0.60, snow=True),
    "masonry":     dict(lift=0.35, dsmooth=-0.05, grime=0.65, rust=0.35, source=None, scuff=0.40, snow=True),
    "roof_tile":   dict(lift=0.35, dsmooth=-0.05, grime=0.30, rust=0.20, source=None, scuff=0.00, snow=True),
    "generic":     dict(lift=0.35, dsmooth=-0.05, grime=0.40, rust=0.25, source=None, scuff=0.40, snow=True),
    "none":        dict(lift=0.00, dsmooth=0.00, grime=0.00, rust=0.00, source=None, scuff=0.00, snow=False),
}
ROLE_COEFF_DESIGN = {    # design section 3.3 starting values (lift, scuff, rust changed above)
    "paint_steel": dict(lift=0.45, scuff=0.30, rust=0.55), "paint": dict(lift=0.35, scuff=0.40, rust=0.30),
    "paint_metal": dict(lift=0.30, scuff=0.20, rust=0.35), "pad": dict(lift=0.40, scuff=0.60, rust=0.00),
    "galv": dict(lift=0.35, scuff=0.20, rust=0.30), "timber": dict(lift=0.20, scuff=0.40, rust=0.30),
    "deck": dict(lift=0.25, scuff=0.60, rust=0.20), "masonry": dict(lift=0.15, scuff=0.30, rust=0.35),
    "roof_tile": dict(lift=0.15, scuff=0.00, rust=0.30), "generic": dict(lift=0.15, scuff=0.30, rust=0.25),
}


def _coef(name):
    return np.array([float(ROLE_COEFF[r][name]) for r in ROLES], dtype=np.float64)


# Kit material keys as plain strings (cs2_bldg.BLDG_MATERIALS, cs2_bldg.py:244-271).
MAT_ROLES = {
    "timber_lap": "timber", "timber_shake": "timber", "timber_vboard": "timber",
    "timber_heavy": "timber",
    "siding_paint": "paint", "trim": "paint", "accent": "paint", "trim_white": "paint",
    "deck": "deck",
    "stone_rubble": "masonry", "rock": "masonry", "plaster": "masonry", "concrete": "masonry",
    "slate": "roof_tile",
    "galv": "galv",
    "metal_roof": "paint_metal", "metal_roof_s1": "paint_metal",
    "glass_dark": "none", "label": "none", "lamp": "none",
}

# Kit and lift vocabularies. The longest matching prefix wins.
PREFIX_ROLES = tuple(
    [(p, "none") for p in ("win_", "pane_", "lamp_") + LABEL_PREFIXES]
    + [(p, "galv") for p in ("gutter_", "downspout_", "snowguard_", "stovepipe_", "vent_",
                             "ridge_", "flag_")]
    + [("crashpad", "pad"), ("deckfascia_", "paint")]
    + [(p, "deck") for p in ("deck_", "step_", "stair_", "porch_", "floor_")]
    + [(p, "masonry") for p in ("plinth", "pier", "chimney_", "stone", "pad", "hutbase")]
    + [("roof_", "roof_tile")]
    + [(p, "paint") for p in ("trim_", "fascia_", "rake_", "sill_", "head_", "jamb_", "mull_",
                              "door_", "trail_")]
    + [(p, "timber") for p in ("col_", "beam_", "truss_", "bracket_", "tail_", "brace_",
                               "post_", "rail_", "entry_", "wall_", "gable_", "dormer_",
                               "soffit_")])

TRAFFIC_PREFIXES = ("deck_", "step_", "stair_", "porch_", "floor_", "landing_", "bridge_",
                    "ramp_", "tread_")
STAIR_PREFIXES = ("step_", "stair_")
EDGE_KAPPA_ROLES = ("timber", "deck", "masonry")


# --------------------------------------------------------------------------------------
# Small math
# --------------------------------------------------------------------------------------

def smoothstep(e0, e1, x):
    """Hermite smoothstep. e0 / e1 may be arrays (a zero-width ramp is a step at e0)."""
    x = np.asarray(x, dtype=np.float64)
    e0 = np.asarray(e0, dtype=np.float64)
    d = np.asarray(e1, dtype=np.float64) - e0
    safe = np.where(d != 0.0, d, 1.0)
    t = np.where(d != 0.0, (x - e0) / safe, (x >= e0).astype(np.float64))
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def _erf(x):
    """Vectorized erf, Abramowitz & Stegun 7.1.26 (|error| < 1.5e-7)."""
    x = np.asarray(x, dtype=np.float64)
    s = np.sign(x)
    a = np.abs(x)
    t = 1.0 / (1.0 + 0.3275911 * a)
    y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t
               + 0.254829592) * t * np.exp(-a * a)
    return s * y


def uniform(f):
    """Zero-mean unit-variance noise -> near-uniform [0, 1]: 0.5 + 0.5 erf(f / sqrt 2)."""
    return 0.5 + 0.5 * _erf(np.asarray(f, dtype=np.float64) / math.sqrt(2.0))


def luma(rgb):
    return np.asarray(rgb, dtype=np.float32)[..., :3] @ LUMA


# --------------------------------------------------------------------------------------
# Noise (section 5): hashed lattice value noise in world meters
# --------------------------------------------------------------------------------------

_H1 = np.uint32(0x8DA6B343)
_H2 = np.uint32(0xD8163841)
_H3 = np.uint32(0xCB1AB31F)
_F1 = np.uint32(0x85EBCA6B)
_F2 = np.uint32(0xC2B2AE35)
_S16 = np.uint32(16)
_S13 = np.uint32(13)


def _u32(a):
    return (np.asarray(a, dtype=np.int64) & 0xFFFFFFFF).astype(np.uint32)


def hash3(ix, iy, iz, seed):
    """uint32 hash of int64 lattice coordinates (masked to uint32), murmur3 fmix32."""
    with np.errstate(over="ignore"):
        h = ((_u32(ix) * _H1) ^ (_u32(iy) * _H2) ^ (_u32(iz) * _H3)
             ^ np.uint32(int(seed) & 0xFFFFFFFF))
        h = h ^ (h >> _S16)
        h = h * _F1
        h = h ^ (h >> _S13)
        h = h * _F2
        h = h ^ (h >> _S16)
    return np.asarray(h, dtype=np.uint32)


def hash01(ix, iy, iz, seed):
    return hash3(ix, iy, iz, seed).astype(np.float64) * (1.0 / 4294967296.0)


def vnoise3(p, seed):
    """Trilinear value noise with the quintic fade at lattice coords p (N, 3), in [0, 1)."""
    p = np.asarray(p, dtype=np.float64).reshape(-1, 3)
    out = np.empty(len(p), dtype=np.float64)
    for a in range(0, len(p), NOISE_CHUNK):
        q = p[a:a + NOISE_CHUNK]
        fl = np.floor(q)
        f = q - fl
        i = fl.astype(np.int64)
        u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0)
        acc = np.zeros(len(q), dtype=np.float64)
        for dx in (0, 1):
            wx = u[:, 0] if dx else 1.0 - u[:, 0]
            for dy in (0, 1):
                wy = u[:, 1] if dy else 1.0 - u[:, 1]
                for dz in (0, 1):
                    wz = u[:, 2] if dz else 1.0 - u[:, 2]
                    acc += wx * wy * wz * hash01(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
        out[a:a + NOISE_CHUNK] = acc
    return out


def fbm(coords, cells_m, ppm, seed, octaves=4):
    """
    Zero-mean, unit-variance fractal value noise at world coordinates `coords` (N, 3) in meters.
    `cells_m`: scalar, (3,) or (N, 3) cell size per axis (anisotropic cells make streaks). Axes 0
    and 1 are the in-plane axes for the density fade. Octave k uses cells / 2^k and amplitude 0.5^k.
    Octaves k >= 1 fade with smoothstep(2, 4, cell * ppm) (cells under 2 texels add nothing). The
    sum is renormalized by sqrt(sum (w a)^2), so a threshold gives the same coverage whichever
    octaves survive. Octave 0 is the effect's designed scale and is never faded (build-report
    deviation D2).
    """
    c = np.asarray(coords, dtype=np.float64).reshape(-1, 3)
    n = len(c)
    cells = np.asarray(cells_m, dtype=np.float64)
    if cells.ndim == 0:
        cells = np.full(3, float(cells))
    ppm = np.broadcast_to(np.asarray(ppm, dtype=np.float64), (n,))
    total = np.zeros(n, dtype=np.float64)
    w2 = np.zeros(n, dtype=np.float64)
    base = int(seed) & 0xFFFFFFFF
    for k in range(int(octaves)):
        ck = cells / float(2 ** k)
        amp = 0.5 ** k
        sk = (base + 0x9E3779B9 * (k + 1)) & 0xFFFFFFFF
        if k == 0:
            nv = vnoise3(c / ck, sk) - 0.5
            total += amp * nv
            w2 += amp * amp
            continue
        cmin = np.minimum(ck[..., 0], ck[..., 1]) * ppm
        w = smoothstep(2.0, 4.0, cmin)
        sel = w > 0.0
        if not sel.any():
            continue
        cks = ck[sel] if ck.ndim == 2 else ck
        nv = vnoise3(c[sel] / cks, sk) - 0.5
        total[sel] += w[sel] * amp * nv
        w2[sel] += (w[sel] * amp) ** 2
    return (total / (VN_STD * np.sqrt(np.maximum(w2, 1e-12)))).astype(np.float32)


# --------------------------------------------------------------------------------------
# Data types (section 3)
# --------------------------------------------------------------------------------------

PW_ATTRS = ("scale", "edge", "grime", "rust", "scuff", "snow", "rust_source", "slot_bound")


@dataclass(frozen=True)
class PartWeather:
    """Per-part override. None = inherit. `scale` 1.0 counts as unset when merging."""
    role: str = None
    scale: float = 1.0
    edge: float = None
    grime: float = None
    rust: float = None
    scuff: float = None
    snow: bool = None
    rust_source: bool = None
    slot_bound: float = None

    def validate(self):
        if self.role is not None and self.role not in ROLE_ID:
            raise ValueError(f"PartWeather.role {self.role!r} not in {ROLES}")
        for a in ("scale", "edge", "grime", "rust", "scuff", "slot_bound"):
            v = getattr(self, a)
            if v is not None and not (0.0 <= float(v) <= 4.0):
                raise ValueError(f"PartWeather.{a} = {v} outside 0..4")


@dataclass(frozen=True)
class WeatherSpec:
    """Section 3.1. Frozen. Overrides / tables are coerced to tuples."""
    name: str
    kind: str = "building"
    #: DECISIONS 171 (2026-10-11, applied by the detail-pass kit group): 0.40 = the
    #: highest step of the 0.35 / 0.40 / 0.45 ... ladder that keeps the vanilla budget on
    #: every one of L2 / school / P1 / P2 with free slots. L2 first WARNs at 0.45
    #: (weathering-layer-check-2 section 7). Was 0.35
    intensity: float = 0.40
    edge: float = 1.0
    grime: float = 1.0
    rust: float = 1.0
    scuff: float = 1.0
    snow: float = 0.0
    ground: str = "grade"
    ground_z: float = 0.0
    ground_band_m: float = 0.60
    eave_band_m: float = 1.20
    rust_len_m: float = 0.60
    edge_band_m: float = 0.08
    snow_inset_m: float = 0.30
    slot_bound: float = 0.10
    #: Q2 switch: "free" (DEFAULT since DECISIONS 171: slot texels take the full luminance
    #: change, kept NEUTRAL so the picker tint still reads) or "bounded" (opt-in, W8:
    #: +-slot_bound per channel on slot texels). ControlMask stays untouched either way (G5)
    slot_wear: str = "free"
    tone: float = 1.0
    albedo_floor: float = 0.03
    overrides: tuple = ()
    mat_roles_extra: tuple = ()
    door_prefixes: tuple = ("door_",)
    traffic_prefixes: tuple = TRAFFIC_PREFIXES
    seed: int = None

    def __post_init__(self):
        ov = tuple((str(p), w) for p, w in (self.overrides or ()))
        object.__setattr__(self, "overrides", ov)
        mr = self.mat_roles_extra
        if isinstance(mr, dict):
            mr = sorted(mr.items())
        object.__setattr__(self, "mat_roles_extra", tuple((str(k), str(r)) for k, r in (mr or ())))
        object.__setattr__(self, "door_prefixes", tuple(self.door_prefixes or ()))
        object.__setattr__(self, "traffic_prefixes", tuple(self.traffic_prefixes or ()))

    # -- seeds ---------------------------------------------------------------------------
    def seed_value(self):
        if self.seed is not None:
            return int(self.seed) & 0xFFFFFFFF
        return zlib.crc32(str(self.name).encode("utf-8")) & 0xFFFFFFFF

    def sub_seed(self, effect):
        stem = str(self.name) if self.seed is None else str(int(self.seed))
        return zlib.crc32(f"{stem}:{effect}".encode("utf-8")) & 0xFFFFFFFF

    # -- identity ------------------------------------------------------------------------
    def key(self):
        items = tuple((f.name, getattr(self, f.name)) for f in dc_fields(self))
        return hashlib.sha1(repr(items).encode("utf-8")).hexdigest()

    def validate(self):
        if not self.name:
            raise ValueError("WeatherSpec.name is required (seed source)")
        if self.kind not in KINDS:
            raise ValueError(f"WeatherSpec.kind {self.kind!r} not in {KINDS}")
        if self.ground not in GROUND_MODES:
            raise ValueError(f"WeatherSpec.ground {self.ground!r} not in {GROUND_MODES}")
        for a in ("intensity", "edge", "grime", "rust", "scuff", "snow", "slot_bound", "tone"):
            v = float(getattr(self, a))
            if not (0.0 <= v <= 1.0):
                raise ValueError(f"WeatherSpec.{a} = {v} outside 0..1")
        if self.slot_wear not in SLOT_WEAR:
            raise ValueError(f"WeatherSpec.slot_wear {self.slot_wear!r} not in {SLOT_WEAR}")
        for a in ("ground_band_m", "eave_band_m", "rust_len_m", "edge_band_m"):
            if not float(getattr(self, a)) > 0.0:
                raise ValueError(f"WeatherSpec.{a} must be > 0")
        if float(self.snow_inset_m) < 0.0:
            raise ValueError("WeatherSpec.snow_inset_m must be >= 0")
        if not (0.0 <= float(self.albedo_floor) <= 0.5):
            raise ValueError("WeatherSpec.albedo_floor outside 0..0.5")
        for p, w in self.overrides:
            if not isinstance(w, PartWeather):
                raise TypeError(f"override {p!r}: expected PartWeather, got {type(w).__name__}")
            w.validate()
        for k, r in self.mat_roles_extra:
            if r not in ROLE_ID:
                raise ValueError(f"mat_roles_extra {k!r}: role {r!r} not in {ROLES}")
        if self.kind == "carrier" and (float(self.snow) > 0.0 or self.ground != "none"):
            raise ValueError("carrier specs refuse snow and ground grime "
                             f"(snow {self.snow}, ground {self.ground!r})")
        return self

    @classmethod
    def preset(cls, kind, name, **kw):
        """Section 3.4 presets. Keyword arguments override any field."""
        if kind not in KINDS:
            raise ValueError(f"preset kind {kind!r} not in {KINDS}")
        args = dict(name=name, kind=kind)
        ov = []
        if kind == "tower":
            # "crashpad" covers crashpad and crashpad_outer. NOT "pad": that is lift_tower's
            # concrete foundation (lift_tower.py:699-703, checker F1). It resolves by its material
            # (concrete -> masonry) or by PREFIX_ROLES ("pad" -> masonry)
            ov = [("crashpad", PartWeather(role="pad"))]
        elif kind == "carrier":
            args.update(ground="none", snow=0.0)
        ov += list(kw.pop("overrides", ()) or ())
        args["overrides"] = tuple(ov)
        args.update(kw)
        return cls(**args).validate()


@dataclass
class WeatherMaps:
    """(A, A, 4) float32 LINEAR maps, rows bottom-up, as post_compose receives them. ao is the raw AO
    ((A, A) or (A, A, >=1), channel 0) before the 0.92 / 0.08 floor, or None."""
    base: object
    nrm: object
    msk: object
    ctl: object
    ao: object = None


class WeatherFields:
    """Compact per-covered-texel fields (section 3.6). See build_fields."""

    COMPACT = ("face", "tpart", "role", "pos", "ngeo", "down_uv", "g", "l", "edge_d",
               "edge_k", "cover_h", "rust_src", "door_d", "door_dz", "door_own", "traffic",
               "kappa", "slot", "protect_c")
    FULL = ("part", "ppm", "protect")

    def __init__(self, A):
        self.A = int(A)
        self.idx = None
        self.stats = {}
        self.doors = []

    @property
    def N(self):
        return 0 if self.idx is None else int(len(self.idx))

    def full(self, name, fill=0.0):
        """Scatter a compact field (or an (N, ...) array) back to (A, A, ...)."""
        if isinstance(name, str):
            if name in self.FULL:
                return getattr(self, name)
            arr = getattr(self, name)
        else:
            arr = np.asarray(name)
        return scatter(self, arr, fill)

    def nbytes(self):
        tot = 0
        for n in self.COMPACT + ("idx",):
            a = getattr(self, n, None)
            if isinstance(a, np.ndarray):
                tot += a.nbytes
        return int(tot)


def scatter(fields, arr, fill=0.0):
    A = fields.A
    arr = np.asarray(arr)
    out = np.full((A * A,) + arr.shape[1:], fill, dtype=arr.dtype)
    out[fields.idx] = arr
    return out.reshape((A, A) + arr.shape[1:])


# --------------------------------------------------------------------------------------
# Role resolution (section 3.3)
# --------------------------------------------------------------------------------------

def _longest(name, pairs):
    best = None
    for prefix, val in pairs:
        if name.startswith(prefix) and (best is None or len(prefix) > len(best[0])):
            best = (prefix, val)
    return None if best is None else best[1]


def _override_attrs(name, overrides):
    """(role of the longest prefix that sets one, {attr: value of the longest prefix that
    sets it})."""
    best = {}
    role = None
    for prefix, pw in overrides:
        if not name.startswith(prefix):
            continue
        if pw.role is not None and (role is None or len(prefix) > role[0]):
            role = (len(prefix), pw.role)
        for a in PW_ATTRS:
            v = getattr(pw, a)
            if v is None or (a == "scale" and float(v) == 1.0):
                continue
            if a not in best or len(prefix) > best[a][0]:
                best[a] = (len(prefix), v)
    return (None if role is None else role[1]), {a: v for a, (_l, v) in best.items()}


# --------------------------------------------------------------------------------------
# Mesh readers
# --------------------------------------------------------------------------------------

def _mesh_arrays(me, matrix=None):
    nv = len(me.vertices)
    co = np.empty(nv * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(nv, 3)
    if matrix is not None:
        m = np.array([list(r) for r in matrix], dtype=np.float64)
        co = co @ m[:3, :3].T + m[:3, 3]
    F = len(me.polygons)
    tmp = np.empty(F, dtype=np.int32)
    me.polygons.foreach_get("loop_start", tmp)
    ls = tmp.astype(np.int64)
    me.polygons.foreach_get("loop_total", tmp)
    lt = tmp.astype(np.int64)
    me.polygons.foreach_get("material_index", tmp)
    mi = tmp.astype(np.int64)
    L = len(me.loops)
    lv = np.empty(L, dtype=np.int32)
    me.loops.foreach_get("vertex_index", lv)
    le = np.empty(L, dtype=np.int32)
    me.loops.foreach_get("edge_index", le)
    E = len(me.edges)
    ev = np.empty(E * 2, dtype=np.int32)
    me.edges.foreach_get("vertices", ev)
    return co, ls, lt, mi, lv.astype(np.int64), le.astype(np.int64), ev.reshape(E, 2).astype(np.int64)


def _face_geometry(co, ls, lt, lv):
    """Per polygon: unit normal (fan cross sum, as cs2_compose._poly_normal), area, centroid,
    and the loop -> face map."""
    F = len(ls)
    nt = np.maximum(lt - 2, 0)
    tf = np.repeat(np.arange(F), nt)
    k = np.arange(int(nt.sum())) - np.repeat(np.cumsum(nt) - nt, nt)
    i0 = lv[ls[tf]]
    i1 = lv[ls[tf] + k + 1]
    i2 = lv[ls[tf] + k + 2]
    cr = np.cross(co[i1] - co[i0], co[i2] - co[i0])
    S = np.stack([np.bincount(tf, cr[:, j], minlength=F) for j in range(3)], axis=1)
    ln = np.linalg.norm(S, axis=1)
    nrm = np.where(ln[:, None] > 0, S / np.where(ln > 0, ln, 1.0)[:, None],
                   np.array((0.0, 0.0, 1.0))[None, :])
    area = 0.5 * ln
    L = len(lv)
    loop_face = np.zeros(L, dtype=np.int64)
    within = np.arange(int(lt.sum())) - np.repeat(np.cumsum(lt) - lt, lt)
    loop_face[np.repeat(ls, lt) + within] = np.repeat(np.arange(F), lt)
    cen = np.stack([np.bincount(loop_face, co[lv, j], minlength=F) for j in range(3)], axis=1)
    cen /= np.maximum(lt, 1)[:, None]
    return nrm, area, cen, loop_face


def _convex_edges(co, ev, le, loop_face, fn, fc, spec):
    """Edge weight k (E,): smoothstep(20, 60 deg) of the dihedral on convex two-face edges. It is 0 on
    boundary, concave, non-manifold and (grade mode) on-grade edges."""
    E = len(ev)
    k = np.zeros(E, dtype=np.float64)
    if E == 0:
        return k
    cnt = np.bincount(le, minlength=E)
    two = np.flatnonzero(cnt == 2)
    if len(two):
        order = np.argsort(le, kind="stable")
        sle = le[order]
        first = np.searchsorted(sle, two)
        f1 = loop_face[order[first]]
        f2 = loop_face[order[first + 1]]
        n1, n2 = fn[f1], fn[f2]
        cosv = np.clip(np.sum(n1 * n2, axis=1), -1.0, 1.0)
        theta = np.degrees(np.arccos(cosv))
        convex = np.sum((fc[f2] - fc[f1]) * n1, axis=1) < -CONVEX_EPS
        kk = smoothstep(EDGE_THETA_DEG[0], EDGE_THETA_DEG[1], theta) * convex * (f1 != f2)
        k[two] = kk
    if spec.ground == "grade":
        z = co[:, 2]
        on = (np.abs(z[ev[:, 0]] - spec.ground_z) <= GROUND_EDGE_M) & \
             (np.abs(z[ev[:, 1]] - spec.ground_z) <= GROUND_EDGE_M)
        k[on] = 0.0
    return k


def _seg_dist(p, a, b):
    """(m, e) distance from points p (m, 3) to segments a -> b (e, 3)."""
    ab = b - a
    L2 = np.maximum(np.sum(ab * ab, axis=1), 1e-18)
    ap = p[:, None, :] - a[None, :, :]
    t = np.clip(np.sum(ap * ab[None], axis=2) / L2[None], 0.0, 1.0)
    d = ap - t[..., None] * ab[None]
    return np.sqrt(np.sum(d * d, axis=2))


# --------------------------------------------------------------------------------------
# build_fields (section 4)
# --------------------------------------------------------------------------------------

def _protect_rects_px(protect_px, me, A):
    """protect_px: iterable of (row0, row1, col0, col1) texel rects (rows bottom-up, as
    cs2_compose.uv_bbox_px), or a callable (me, A) -> such an iterable. A generator that paints a
    sub-rect of a part's island (lift_tower's warning triangle) passes the rect of its painter
    without knowing A in advance."""
    if protect_px is None:
        return []
    rects = protect_px(me, A) if callable(protect_px) else protect_px
    out = []
    for r in rects or ():
        r0, r1, c0, c1 = (int(v) for v in r)
        r0, r1 = max(0, r0), min(A, r1)
        c0, c1 = max(0, c0), min(A, c1)
        if r1 > r0 and c1 > c0:
            out.append((r0, r1, c0, c1))
    return out


def build_fields(me, A, spec, maps=None, *, uv_layer="UVUnique", matrix=None, part_table=None,
                 mat_keys=None, mat_roles=None, detail_specs=None, stamped=(), reserved=(),
                 protect_px=()):
    """
    Per-texel fields of mesh `me` on its `uv_layer` at A x A (section 4). One pass over
    cs2_compose.iter_face_rasters (the rasterizer of rasterise_face_fields, so part / ppm agree
    texel for texel). Everything else is derived from the mesh: world position, the face normal and
    gravity frame, distance to convex edges (dihedral-weighted), cover above (top-down peel of
    down-facing faces), rust sources (DetailSpec lattice + geometric), doors, traffic directions,
    normal-map curvature, protect rects. `maps` (WeatherMaps) is optional: it feeds the role
    refinement (MaskMap R, ControlMask), kappa and slot. `protect_px` adds texel rects (or a
    callable (me, A) -> rects) restored 1:1, for a painter that stamps only part of an island (see
    _protect_rects_px).
    """
    spec.validate()
    t0 = time.perf_counter()
    A = int(A)
    table = cs2_compose.read_part_table(me, part_table)
    P = len(table)
    parts_raw = cs2_compose.part_indices(me)
    poly_part = parts_raw.astype(np.int64)
    poly_part = np.where((poly_part >= 1) & (poly_part <= P), poly_part, 0)
    co, ls, lt, mi, lv, le, ev = _mesh_arrays(me, matrix)
    fn, farea, fc, loop_face = _face_geometry(co, ls, lt, lv)
    kE = _convex_edges(co, ev, le, loop_face, fn, fc, spec)
    stats = {"faces": int(len(ls)), "parts": int(P), "convex_edges": int((kE > 0).sum())}

    # -- pass 1: the rasterizer of rasterise_face_fields ---------------------------------
    face_map = np.full((A, A), -1, dtype=np.int32)
    part_map = np.zeros((A, A), dtype=np.int32)
    ppm_map = np.zeros((A, A), dtype=np.float32)
    finfo = {}
    for poly, sl, mask, _X, _Y, q, Pv, ppm in cs2_compose.iter_face_rasters(me, A, uv_layer, 0.0,
                                                                          matrix):
        fv = face_map[sl]
        fv[mask] = poly.index
        pv = part_map[sl]
        pv[mask] = parts_raw[poly.index]
        dv = ppm_map[sl]
        dv[mask] = ppm
        finfo[poly.index] = (q, Pv, ppm)
    idx = np.flatnonzero(face_map.ravel() >= 0).astype(np.int64)
    N = len(idx)
    face = face_map.ravel()[idx].astype(np.int32)
    tpart = part_map.ravel()[idx].astype(np.int64)
    tpart = np.where((tpart >= 1) & (tpart <= P), tpart, 0)

    fl = WeatherFields(A)
    fl.idx = idx
    fl.face = face
    fl.tpart = tpart.astype(np.int32)
    fl.part = part_map
    fl.ppm = ppm_map
    fl.spec_key = spec.key()
    fl.seed = spec.seed_value()
    fl.part_names = list(table)

    # -- roles ---------------------------------------------------------------------------
    msk_c = ctl_c = None
    if maps is not None:
        m = np.asarray(maps.msk, dtype=np.float32)
        c = np.asarray(maps.ctl, dtype=np.float32)
        msk_c = m.reshape(-1, m.shape[-1])[idx]
        ctl_c = c.reshape(-1, c.shape[-1])[idx]
    _resolve_parts(fl, table, spec, poly_part, mi, farea, mat_keys, mat_roles, tpart, msk_c,
                   ctl_c, co, lv, loop_face, stats)
    fl.role = fl.part_role[tpart].astype(np.uint8)

    # -- per-texel geometry, edges, lattice and geometric rust (one group-by over faces) --
    pos = np.zeros((N, 3), dtype=np.float64)
    ngeo = np.zeros((N, 3), dtype=np.float64)
    gdir = np.zeros((N, 3), dtype=np.float64)
    ldir = np.zeros((N, 3), dtype=np.float64)
    down_uv = np.zeros((N, 2), dtype=np.float64)
    edge_d = np.full(N, EDGE_CAP_M, dtype=np.float32)
    edge_k = np.zeros(N, dtype=np.float32)
    rust_src = np.zeros(N, dtype=np.float32)
    Xc = (idx % A).astype(np.float64) + 0.5
    Yc = (idx // A).astype(np.float64) + 0.5

    conv_loops = np.flatnonzero(kE[le] > 0.0)
    seg_by_face = {}
    for li in conv_loops.tolist():
        seg_by_face.setdefault(int(loop_face[li]), set()).add(int(le[li]))

    sources = _rust_sources(fl, co, lv, loop_face, poly_part, P)
    rust_coef = _coef("rust")[fl.part_role] * fl.part_mult[:, 2] * fl.part_scale
    cand = _rust_candidates(sources, finfo, fn, poly_part, rust_coef)
    stats["rust_sources"] = int(len(sources))
    stats["geo_candidates"] = int(sum(len(v) for v in cand.values()))
    geo_pairs = lat_faces = lat_skipped = lat_curtain = 0
    dspecs = dict(detail_specs or {})

    order = np.argsort(face, kind="stable")
    fs = face[order]
    uniq, starts = np.unique(fs, return_index=True)
    ends = np.append(starts[1:], len(fs))
    Zw = np.array((0.0, 0.0, 1.0))
    for fi, s0, s1 in zip(uniq.tolist(), starts.tolist(), ends.tolist()):
        sel = order[s0:s1]
        q, Pv, ppm = finfo[fi]
        qm, Pm, Mt = _affine_uv_to_world(q, Pv)
        Xs, Ys = Xc[sel], Yc[sel]
        p = Pm[None, :] + (Xs - qm[0])[:, None] * Mt[0][None, :] + (Ys - qm[1])[:, None] * Mt[1][None, :]
        pos[sel] = p
        n = _poly_normal(Pv)
        ngeo[sel] = n
        gv = lvv = None
        if abs(n[2]) <= 0.97:
            gv = -Zw + n[2] * n
            gv = gv / np.linalg.norm(gv)
            lvv = np.cross(n, gv)
            gdir[sel] = gv
            ldir[sel] = lvv
            duv, *_ = np.linalg.lstsq(Mt.T, gv, rcond=None)
            dn = np.linalg.norm(duv)
            if dn > 1e-12:
                down_uv[sel] = duv / dn
        segs = seg_by_face.get(fi)
        if segs:
            e = np.array(sorted(segs), dtype=np.int64)
            d = _seg_dist(p, co[ev[e, 0]], co[ev[e, 1]])
            j = np.argmin(d, axis=1)
            edge_d[sel] = np.minimum(d[np.arange(len(sel)), j], EDGE_CAP_M)
            edge_k[sel] = kE[e][j]
        pi = int(poly_part[fi])
        if pi == 0 or fl.part_role[pi] == NONE_ID:
            continue
        # lattice rust (section 4.6)
        if dspecs and abs(n[2]) < 0.3:
            ds = _spec_for(table[pi - 1], dspecs)
            if ds is not None and (ds.bolt_pitch_m or (ds.seams_m and ds.seam_axes in ("both", "rings"))):
                e_h, e_v = _inplane_world_axes(n)
                if abs(e_v[2]) < 0.999:
                    lat_skipped += 1
                else:
                    w, cur = _lattice_rust(ds, e_h, e_v, Xs, Ys, qm, Pm, Mt, Pv, ppm, spec)
                    if w is not None:
                        lat_faces += 1
                        lat_curtain += int(cur)
                        rust_src[sel] = np.maximum(rust_src[sel], w)
        # geometric rust (W10)
        if gv is not None and fi in cand:
            for si in cand[fi]:
                w = _geo_rust(sources[si], p, n, gv, lvv, Pv, ppm, spec)
                if w is not None:
                    geo_pairs += 1
                    rust_src[sel] = np.maximum(rust_src[sel], w)
    stats.update(lattice_faces=lat_faces, lattice_curtain_faces=lat_curtain,
                 lattice_skipped=lat_skipped, geo_pairs=geo_pairs)

    fl.pos = pos.astype(np.float32)
    fl.ngeo = ngeo.astype(np.float16)
    fl.g = gdir.astype(np.float16)
    fl.l = ldir.astype(np.float16)
    fl.down_uv = down_uv.astype(np.float16)
    fl.edge_d = edge_d
    fl.edge_k = edge_k
    fl.rust_src = rust_src

    # -- cover above (section 4.4) -------------------------------------------------------
    fl.cover_h, cov_stats = _cover_heights(co, ls, lt, lv, fn, pos, ngeo)
    stats.update(cov_stats)

    # -- doors and traffic (sections 4.7, 4.8) -------------------------------------------
    _doors_and_traffic(fl, table, spec, co, lv, loop_face, poly_part, fn, farea, pos, ngeo,
                       tpart, stats)

    # -- curvature from the input normal map (section 4.5), slot -------------------------
    if maps is not None and maps.nrm is not None:
        fl.kappa = _kappa(np.asarray(maps.nrm, dtype=np.float32), part_map, ppm_map,
                          face_map >= 0).reshape(-1)[idx].astype(np.float32)
        fl.slot = np.max(ctl_c[:, :3], axis=1).astype(np.float32)
    else:
        fl.kappa = np.zeros(N, dtype=np.float32)
        fl.slot = np.zeros(N, dtype=np.float32)

    # -- protect (section 4.9) -----------------------------------------------------------
    prot = np.zeros((A, A), dtype=bool)
    stamped_rects = []
    for prefix in stamped or ():
        r = cs2_compose.uv_bbox_px(me, table, prefix, A, uv_layer)
        if r is not None:
            prot[r[0]:r[1], r[2]:r[3]] = True
            stamped_rects.append([prefix, [int(v) for v in r]])
    for rect in reserved or ():
        prot[_rect_slices(tuple(float(x) for x in rect), A)] = True
    px_rects = _protect_rects_px(protect_px, me, A)
    for r0, r1, c0, c1 in px_rects:
        prot[r0:r1, c0:c1] = True
    stats["protect_px_rects"] = [list(r) for r in px_rects]
    fl.protect = prot
    fl.protect_c = prot.reshape(-1)[idx] | (fl.role == NONE_ID)
    stats["protect_texels"] = int(prot.sum())
    stats["stamped_rects"] = stamped_rects
    stats["N"] = int(N)
    stats["covered_fraction"] = round(N / float(A * A), 4)
    stats["seconds"] = round(time.perf_counter() - t0, 3)
    fl.stats = stats
    return fl


def _resolve_parts(fl, table, spec, poly_part, mi, farea, mat_keys, mat_roles, tpart, msk_c,
                   ctl_c, co, lv, loop_face, stats):
    P = len(table)
    nR = len(ROLES)
    role = np.full(P + 1, ROLE_ID["generic"], dtype=np.int64)
    src = ["default"] * (P + 1)
    scale = np.ones(P + 1, dtype=np.float64)
    mult = np.ones((P + 1, 4), dtype=np.float64)          # edge, grime, rust, scuff
    snow = np.zeros(P + 1, dtype=bool)
    rsrc = np.zeros(P + 1, dtype=bool)
    sbound = np.full(P + 1, float(spec.slot_bound), dtype=np.float64)
    explicit = {}

    # rule 2: material votes, area-weighted
    votes = np.zeros((P + 1, nR), dtype=np.float64)
    rm = None
    if mat_roles is not None:
        rm = {int(i): ROLE_ID[r] for i, r in dict(mat_roles).items()}
    elif mat_keys is not None:
        merged = dict(MAT_ROLES)
        merged.update(dict(spec.mat_roles_extra))
        rm = {i: ROLE_ID[merged[k]] for i, k in enumerate(mat_keys) if k in merged}
    if rm:
        top = int(max(int(mi.max()) if len(mi) else 0, max(rm))) + 1
        lut = np.full(top, -1, dtype=np.int64)
        for i, r in rm.items():
            if 0 <= i < top:
                lut[i] = r
        fr = lut[np.clip(mi, 0, top - 1)]
        ok = fr >= 0
        np.add.at(votes, (poly_part[ok], fr[ok]), farea[ok])

    for p in range(1, P + 1):
        name = table[p - 1]
        orole, attrs = _override_attrs(name, spec.overrides)
        if orole is not None:                                    # rule 1
            role[p] = ROLE_ID[orole]
            src[p] = "override"
        elif votes[p].sum() > 0:                                 # rule 2
            role[p] = int(np.argmax(votes[p]))
            src[p] = "material"
        else:                                                    # rule 3
            pr = _longest(name, PREFIX_ROLES)
            if pr is not None:
                role[p] = ROLE_ID[pr]
                src[p] = "prefix"
        explicit[p] = attrs

    # rule 4: map refinement, never on override / none parts
    refined = 0
    if msk_c is not None and len(tpart):
        cnt = np.bincount(tpart, minlength=P + 1).astype(np.float64)
        metal = np.bincount(tpart, (msk_c[:, 0] >= 0.5).astype(np.float64), minlength=P + 1)
        slot = np.bincount(tpart, (np.max(ctl_c[:, :3], axis=1) >= 0.5).astype(np.float64),
                           minlength=P + 1)
        for p in range(1, P + 1):
            if src[p] == "override" or role[p] == NONE_ID or cnt[p] == 0:
                continue
            r = ROLES[role[p]]
            if metal[p] >= 0.5 * cnt[p] and r not in ("paint_steel", "galv"):
                role[p] = ROLE_ID["galv"]
                src[p] += "+metal"
                refined += 1
            elif metal[p] < 0.5 * cnt[p] and slot[p] >= 0.5 * cnt[p]:
                if r in ("timber", "generic"):
                    role[p] = ROLE_ID["paint"]
                    src[p] += "+slot"
                    refined += 1
                elif r == "roof_tile":
                    role[p] = ROLE_ID["paint_metal"]
                    src[p] += "+slot"
                    refined += 1

    for p in range(1, P + 1):
        r = ROLES[role[p]]
        a = explicit[p]
        scale[p] = float(a.get("scale", 1.0))
        for j, nm in enumerate(("edge", "grime", "rust", "scuff")):
            if nm in a:
                mult[p, j] = float(a[nm])
        snow[p] = bool(a["snow"]) if "snow" in a else bool(ROLE_COEFF[r]["snow"])
        rsrc[p] = bool(a["rust_source"]) if "rust_source" in a else r in ("galv", "paint_steel")
        if "slot_bound" in a:
            sbound[p] = float(a["slot_bound"])
    snow[0] = True
    zl = co[lv, 2]
    pl = poly_part[loop_face]
    zmin = np.full(P + 1, np.inf)
    zmax = np.full(P + 1, -np.inf)
    np.minimum.at(zmin, pl, zl)
    np.maximum.at(zmax, pl, zl)
    fl.part_role = role.astype(np.uint8)
    fl.part_role_source = src
    fl.part_scale = scale
    fl.part_mult = mult
    fl.part_snow = snow
    fl.part_rust_src = rsrc
    fl.part_slot_bound = sbound
    fl.part_zmin = np.where(np.isfinite(zmin), zmin, 0.0)
    fl.part_zmax = np.where(np.isfinite(zmax), zmax, 0.0)
    fl.part_stair = np.array([False] + [n.startswith(STAIR_PREFIXES) for n in table], dtype=bool)
    stats["roles_refined"] = refined
    stats["role_source"] = {k: src[1:].count(k) for k in sorted(set(src[1:]))}


def _rust_sources(fl, co, lv, loop_face, poly_part, P):
    """[(part, bbox_min (3,), bbox_max (3,))] of rust-source parts (role != none)."""
    pl = poly_part[loop_face]
    out = []
    for p in range(1, P + 1):
        if not fl.part_rust_src[p] or fl.part_role[p] == NONE_ID:
            continue
        v = co[lv[pl == p]]
        if len(v) == 0:
            continue
        out.append((p, v.min(axis=0), v.max(axis=0)))
    return out


def _rust_candidates(sources, finfo, fn, poly_part, rust_coef):
    """{face: [source index]} after a numpy bbox cull (0.3 m slack)."""
    if not sources:
        return {}
    faces = [f for f in sorted(finfo) if abs(fn[f, 2]) <= 0.97 and poly_part[f] > 0
             and rust_coef[poly_part[f]] > 0]
    if not faces:
        return {}
    fmin = np.array([finfo[f][1].min(axis=0) for f in faces]) - 0.3
    fmax = np.array([finfo[f][1].max(axis=0) for f in faces]) + 0.3
    smin = np.array([s[1] for s in sources]) - 0.05
    smax = np.array([s[2] for s in sources]) + 0.05
    hit = (np.all(fmin[:, None, :] <= smax[None], axis=2)
           & np.all(fmax[:, None, :] >= smin[None], axis=2))
    spart = np.array([s[0] for s in sources])
    out = {}
    for i, f in enumerate(faces):
        js = np.flatnonzero(hit[i] & (spart != poly_part[f]))
        if len(js):
            out[f] = js.tolist()
    return out


def _bbox_corners(lo, hi):
    return np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1])
                     for z in (lo[2], hi[2])], dtype=np.float64)


def _geo_rust(src, p, n, g, l, Pv, ppm, spec):
    """Streak weight on one target face below one source part, or None (section 4.6)."""
    _part, lo, hi = src
    c = _bbox_corners(lo, hi)
    h = (c - Pv[0]) @ n
    if h.min() > GEO_CONTACT_M or h.max() < -0.05:
        return None
    cd = _bbox_corners(lo - 0.05, hi + 0.05)
    fa, fb = Pv @ l, Pv @ g
    ca, cb = cd @ l, cd @ g
    if ca.max() < fa.min() or ca.min() > fa.max() or cb.max() < fb.min() or cb.min() > fb.max():
        return None
    a = c @ l
    b = c @ g
    a0, a1, bmax = a.min() - 0.02, a.max() + 0.02, b.max()
    ta = p @ l
    tb = p @ g
    fall = max(0.02, 1.0 / ppm)
    out = np.maximum(np.maximum(a0 - ta, ta - a1), 0.0)
    lat = 1.0 - smoothstep(0.0, fall, out)
    if a1 - a0 > GEO_LONG_M:
        # a long source (snow-guard rail, ridge cap, gutter) drips at points, not along its whole
        # length. 1D hashed value noise along the across axis keeps ~1/4 of it. The noise is in
        # world meters, so it is continuous over faces sharing l (build-report D8)
        gp = np.stack([ta / DRIP_CELL_M, np.full_like(ta, float(_part) * 7.31),
                       np.zeros_like(ta)], axis=1)
        lat = lat * smoothstep(DRIP_LO, DRIP_HI, vnoise3(gp, spec.sub_seed("drip")))
    dv = tb - bmax
    w = np.where(dv > 0.0, np.exp(-np.maximum(dv, 0.0) / spec.rust_len_m), 0.0) * lat
    if not (w > 1e-4).any():
        return None
    return w.astype(np.float32)


def _lattice_rust(ds, e_h, e_v, Xs, Ys, qm, Pm, Mt, Pv, ppm, spec):
    """Bolt streaks and ring-seam curtain on one lattice face: (w, curtain?) or (None, 0). Rows /
    columns are detail_height's (cs2_compose.py:414-425). A streak is drawn only where detail_height
    draws its bolt or seam (same texel-floor test)."""
    s_h = _world_coord_field(e_h, Xs, Ys, qm, Pm, Mt)
    s_v = _world_coord_field(e_v, Xs, Ys, qm, Pm, Mt)
    vz = Pv @ e_v
    hz = Pv @ e_h
    vtop = vz.max()
    hmin, hmax = hz.min(), hz.max()
    L = float(spec.rust_len_m)
    w = np.zeros(len(Xs), dtype=np.float64)
    any_drawn = False
    curtain = False
    seed = spec.sub_seed("bolt")
    if ds.bolt_pitch_m and 2.0 * ds.bolt_r_m * ppm >= TEXEL_FLOOR - 1e-6:
        pitch = float(ds.bolt_pitch_m)
        r = max(float(ds.bolt_r_m), 1.0 / ppm)
        if ds.seams_m:
            fams = ((float(ds.bolt_offset_m), float(ds.seams_m)),
                    (-float(ds.bolt_offset_m), float(ds.seams_m)))
        else:
            fams = ((0.5 * pitch, pitch),)
        dv = np.full(len(Xs), np.inf)
        krow = np.zeros(len(Xs), dtype=np.int64)
        for fi, (off, per) in enumerate(fams):
            d = np.mod(off - s_v, per)                  # up to the next row above
            row = s_v + d
            d = np.where(row <= vtop + r, d, np.inf)    # that row must lie on this face
            better = d < dv
            dv = np.where(better, d, dv)
            kr = np.rint((row - off) / per).astype(np.int64) * 2 + fi
            krow = np.where(better, kr, krow)
        j = np.floor(s_h / pitch).astype(np.int64)
        colc = (j + 0.5) * pitch
        dl = np.abs(s_h - colc)
        colok = (colc >= hmin - r) & (colc <= hmax + r)
        Lj = L * (0.4 + 0.6 * hash01(j, krow, 0, seed))
        nom = 1.2 * float(ds.bolt_r_m)
        ws0 = max(nom, TEXEL_FLOOR / ppm)
        fin = np.isfinite(dv)
        dvf = np.where(fin, dv, 0.0)
        ws = ws0 * (1.0 + dvf / L)
        wb = np.exp(-dvf / Lj) * (1.0 - smoothstep(0.0, ws, dl)) * (nom / ws0)
        wb = np.where(fin & (dv > r) & colok, wb, 0.0)
        w = np.maximum(w, wb)
        any_drawn = True
    if (ds.seams_m and ds.seam_axes in ("both", "rings")
            and ds.seams_m * ppm >= TEXEL_FLOOR - 1e-6):
        per = float(ds.seams_m)
        half_w = 0.5 * max(float(ds.seam_width_m), 1.0 / ppm)
        d = np.mod(-s_v, per)
        seam = s_v + d
        ok = (seam <= vtop + half_w) & (d > half_w)
        ks = np.rint(seam / per).astype(np.int64)
        gp = np.stack([s_h / CURTAIN_GAP_CELL_M, ks.astype(np.float64) * 7.0,
                       np.zeros_like(s_h)], axis=1)
        gap = smoothstep(0.35, 0.65, vnoise3(gp, spec.sub_seed("curtain")))
        wc = CURTAIN_AMP * np.exp(-d / (0.5 * L)) * gap
        w = np.maximum(w, np.where(ok, wc, 0.0))
        any_drawn = True
        curtain = True
    if not any_drawn:
        return None, 0
    return w.astype(np.float32), curtain


def _cover_heights(co, ls, lt, lv, fn, pos, ngeo):
    """Section 4.4: distance up to the nearest down-facing surface above (inf = sky).
    Each cell keeps the K lowest down-facing planes (height at the cell center + slope),
    evaluated exactly at the texel's look-up point."""
    N = len(pos)
    stats = {"down_faces": 0, "cover_overflow_cells": 0}
    down = np.flatnonzero(fn[:, 2] < -0.2)
    if N == 0 or len(down) == 0:
        return np.full(N, np.inf, dtype=np.float32), stats
    x0, y0 = co[:, 0].min() - 2 * CELL_M, co[:, 1].min() - 2 * CELL_M
    nx = int(math.ceil((co[:, 0].max() - x0) / CELL_M)) + 3
    ny = int(math.ceil((co[:, 1].max() - y0) / CELL_M)) + 3
    K = COVER_K
    Lz = np.full((ny, nx, K), np.inf, dtype=np.float64)
    Lgx = np.zeros((ny, nx, K), dtype=np.float64)
    Lgy = np.zeros((ny, nx, K), dtype=np.float64)
    over = np.zeros((ny, nx), dtype=bool)
    used = 0
    for f in down.tolist():
        Pv = co[lv[ls[f]:ls[f] + lt[f]]]
        n = fn[f]
        qc = np.stack([(Pv[:, 0] - x0) / CELL_M, (Pv[:, 1] - y0) / CELL_M], axis=1)
        c0 = max(0, int(math.floor(qc[:, 0].min() - 1)))
        c1 = min(nx, int(math.ceil(qc[:, 0].max() + 1)))
        r0 = max(0, int(math.floor(qc[:, 1].min() - 1)))
        r1 = min(ny, int(math.ceil(qc[:, 1].max() + 1)))
        if c1 <= c0 or r1 <= r0:
            continue
        X, Y = np.meshgrid(np.arange(c0, c1) + 0.5, np.arange(r0, r1) + 0.5)
        m = _poly_mask(qc, X, Y, 0.5)
        if not m.any():
            continue
        used += 1
        wx = x0 + X[m] * CELL_M
        wy = y0 + Y[m] * CELL_M
        d0 = float(n @ Pv[0])
        z = (d0 - n[0] * wx - n[1] * wy) / n[2]
        gx, gy = -n[0] / n[2], -n[1] / n[2]
        vz, vx, vy = Lz[r0:r1, c0:c1], Lgx[r0:r1, c0:c1], Lgy[r0:r1, c0:c1]
        cz = np.concatenate([vz[m], z[:, None]], axis=1)
        cx = np.concatenate([vx[m], np.full((len(z), 1), gx)], axis=1)
        cy = np.concatenate([vy[m], np.full((len(z), 1), gy)], axis=1)
        o = np.argsort(cz, axis=1, kind="stable")
        cz = np.take_along_axis(cz, o, 1)
        cx = np.take_along_axis(cx, o, 1)
        cy = np.take_along_axis(cy, o, 1)
        ov = over[r0:r1, c0:c1]
        ov[m] |= np.isfinite(cz[:, K])
        vz[m] = cz[:, :K]
        vx[m] = cx[:, :K]
        vy[m] = cy[:, :K]
    stats["down_faces"] = int(used)
    stats["cover_overflow_cells"] = int(over.sum())
    out = np.full(N, np.inf, dtype=np.float32)
    for a in range(0, N, NOISE_CHUNK):
        p = pos[a:a + NOISE_CHUNK]
        n = ngeo[a:a + NOISE_CHUNK]
        lx = p[:, 0] + n[:, 0] * COVER_STEP_M
        ly = p[:, 1] + n[:, 1] * COVER_STEP_M
        ci = np.floor((lx - x0) / CELL_M).astype(np.int64)
        ri = np.floor((ly - y0) / CELL_M).astype(np.int64)
        ok = (ci >= 0) & (ci < nx) & (ri >= 0) & (ri < ny)
        ci = np.clip(ci, 0, nx - 1)
        ri = np.clip(ri, 0, ny - 1)
        cxc = x0 + (ci + 0.5) * CELL_M
        cyc = y0 + (ri + 0.5) * CELL_M
        zl = Lz[ri, ci] + Lgx[ri, ci] * (lx - cxc)[:, None] + Lgy[ri, ci] * (ly - cyc)[:, None]
        dz = zl - p[:, 2][:, None]
        dz = np.where(np.isfinite(zl) & (dz > COVER_EPS_M), dz, np.inf)
        out[a:a + NOISE_CHUNK] = np.where(ok, dz.min(axis=1), np.inf)
    return out, stats


def _door_group(name, prefixes):
    for p in prefixes:
        if name.startswith(p):
            rest = name[len(p):]
            if "_" in rest:
                return p + rest.split("_", 1)[0] + "_"
            return name
    return None


def _doors_and_traffic(fl, table, spec, co, lv, loop_face, poly_part, fn, farea, pos, ngeo,
                       tpart, stats):
    N = len(pos)
    P = len(table)
    groups = {}
    for p in range(1, P + 1):
        gid = _door_group(table[p - 1], spec.door_prefixes)
        if gid is not None:
            groups.setdefault(gid, []).append(p)
    mesh_c = co[:, :2].mean(axis=0) if len(co) else np.zeros(2)
    pl = poly_part[loop_face]
    doors = []
    part_door = np.full(P + 1, -1, dtype=np.int64)
    for gid in sorted(groups):
        ps = groups[gid]
        v = co[lv[np.isin(pl, ps)]]
        if len(v) == 0:
            continue
        zb = float(v[:, 2].min())
        c = v[:, :2].mean(axis=0)
        fmask = np.isin(poly_part, ps)
        leaf = [p for p in ps if "leaf" in table[p - 1][len(gid):]]
        cand = np.flatnonzero(fmask & np.isin(poly_part, leaf) & (np.abs(fn[:, 2]) < 0.5))
        if len(cand) == 0:
            cand = np.flatnonzero(fmask & (np.abs(fn[:, 2]) < 0.5))
        if len(cand) == 0:
            continue
        f = cand[np.argmax(farea[cand])]
        nxy = fn[f, :2].astype(np.float64)
        ln = np.linalg.norm(nxy)
        if ln < 1e-6:
            continue
        nxy /= ln
        if np.dot(nxy, c - mesh_c) < 0:
            nxy = -nxy
        di = len(doors)
        doors.append(dict(id=gid, bottom_z=round(zb, 4), centre=[round(float(x), 3) for x in c],
                          out=[round(float(x), 4) for x in nxy],
                          threshold=(c + 0.3 * nxy).tolist(), zb=zb))
        for p in ps:
            part_door[p] = di
    stats["doors"] = [dict(id=d["id"], bottom_z=d["bottom_z"], out=d["out"]) for d in doors]
    near = np.full(N, -1, dtype=np.int64)
    if doors:
        th = np.array([d["threshold"] for d in doors])
        zbs = np.array([d["zb"] for d in doors])
        best = np.full(N, np.inf)
        for i in range(len(doors)):
            dd = np.hypot(pos[:, 0] - th[i, 0], pos[:, 1] - th[i, 1])
            b = dd < best
            best = np.where(b, dd, best)
            near = np.where(b, i, near)
        own = part_door[tpart]
        hasown = own >= 0
        near = np.where(hasown, own, near)
        door_d = np.hypot(pos[:, 0] - th[near, 0], pos[:, 1] - th[near, 1]).astype(np.float32)
        door_dz = (pos[:, 2] - zbs[near]).astype(np.float32)
        fl.door_own = hasown
    else:
        door_d = np.full(N, np.inf, dtype=np.float32)
        door_dz = np.full(N, np.inf, dtype=np.float32)
        fl.door_own = np.zeros(N, dtype=bool)
    fl.door_d = door_d
    fl.door_dz = door_dz
    fl.doors = doors

    # traffic: the part's principal XY axis on up-facing texels of traffic parts (the MINOR axis on
    # steps and stairs = the run direction). Fix pass 1 (F5): the direction toward the nearest
    # threshold is no longer used, because every radial direction converges on the door and drew a
    # sunburst. Near a door the scuffs turn isotropic instead
    traffic = np.zeros((N, 2), dtype=np.float64)
    tparts = [p for p in range(1, P + 1) if table[p - 1].startswith(spec.traffic_prefixes)
              and fl.part_role[p] != NONE_ID]
    used = 0
    nz = ngeo[:, 2]
    for p in tparts:
        sel = np.flatnonzero((tpart == p) & (nz >= 0.85))
        if len(sel) == 0:
            continue
        used += 1
        v = co[lv[pl == p]][:, :2]
        vc = v - v.mean(axis=0)
        _w, V = np.linalg.eigh(vc.T @ vc)
        axis = V[:, 0] if table[p - 1].startswith(STAIR_PREFIXES) else V[:, 1]
        if axis[np.argmax(np.abs(axis))] < 0:
            axis = -axis
        traffic[sel] = axis
    fl.traffic = traffic.astype(np.float16)
    stats["traffic_parts"] = int(used)


def _kappa(nrm, part_map, ppm_map, covered):
    """Section 4.5: kappa = (dn_x/du + dn_y/dv) * ppm on the input normal map (1/m, + convex). It is 0
    where any 4-neighbor is uncovered or belongs to another part."""
    n = nrm[..., :3] * 2.0 - 1.0
    _gr, gx_col = np.gradient(n[..., 0])
    gy_row, _gc = np.gradient(n[..., 1])
    k = gx_col + gy_row
    ok = covered.copy()
    for sh, ax in ((1, 0), (-1, 0), (1, 1), (-1, 1)):
        ok &= np.roll(covered, sh, axis=ax) & (np.roll(part_map, sh, axis=ax) == part_map)
    ok[0, :] = ok[-1, :] = False
    ok[:, 0] = ok[:, -1] = False
    return np.where(ok, k * ppm_map, 0.0).astype(np.float32)


# --------------------------------------------------------------------------------------
# Masks (section 6)
# --------------------------------------------------------------------------------------

def _check_pair(fields, spec):
    if fields.spec_key != spec.key():
        raise ValueError("WeatherFields were built for another WeatherSpec "
                         f"(fields {fields.spec_key[:10]} vs spec {spec.key()[:10]})")


def _compact(arr, fields):
    a = np.asarray(arr)
    if a.ndim == 3:
        a = a[..., 0]
    return a.reshape(-1)[fields.idx]


def _ground_eff(w):
    """Effective ground darkening weight w (1 + (GROUND_GAIN - 1) w) (apply_weathering)."""
    return w * (1.0 + (GROUND_GAIN - 1.0) * w)


def _ground_inv(e):
    """Inverse of _ground_eff on [0, GROUND_GAIN] -> [0, 1]."""
    g = GROUND_GAIN - 1.0
    e = np.clip(e, 0.0, GROUND_GAIN)
    if g <= 1e-9:
        return np.clip(e, 0.0, 1.0)
    return np.clip((-1.0 + np.sqrt(1.0 + 4.0 * g * e)) / (2.0 * g), 0.0, 1.0)


def compute_masks(maps, fields, spec, *, compose_contact=False):
    """
    Section 6 masks, each (N,) float32 0..1 on the covered texels of `fields`, BEFORE the
    intensity x effect x role x part-scale factors (snow is its own coverage target and is
    final). Keys: tone (the one SIGNED mask, -1..1, 0 = no change), edge, grime_ground,
    grime_eave, grime_cavity, grime_door, grime, rust, snow, scuff. Protected texels
    (stamped / reserved / protect_px rects, role none) are 0 in every mask.
    """
    _check_pair(fields, spec)
    F = fields
    N = F.N
    pos = F.pos.astype(np.float64)
    z = pos[:, 2]
    nz = F.ngeo[:, 2].astype(np.float64)
    ppm = np.maximum(F.ppm.reshape(-1)[F.idx].astype(np.float64), 1e-3)
    role = F.role.astype(np.int64)
    act = ~F.protect_c
    ao = None
    if maps is not None and maps.ao is not None:
        ao = _compact(maps.ao, F).astype(np.float64)
    tp = F.tpart.astype(np.int64)
    out = {}

    # tone (fix pass 1): signed large-scale luminance variation on walls and roofs (no
    # undersides), continuous in world meters across parts and islands
    tn = np.zeros(N)
    tsel = act & np.isin(role, [ROLE_ID[r] for r in TONE_ROLES]) & (nz > -0.2)
    if tsel.any():
        u = uniform(fbm(pos[tsel], TONE_CELLS_M, ppm[tsel], spec.sub_seed("tone"),
                        octaves=TONE_OCTAVES))
        tn[tsel] = 2.0 * u - 1.0
    out["tone"] = tn

    # 6.1 edge wear (fix pass 1): band b = max(edge_band_m (0.7 + 0.6 u), 3 texels), full
    # weight out to EDGE_CORE b, then a smoothstep to 0 at b
    w = np.zeros(N)
    bmax = np.maximum(spec.edge_band_m * (EDGE_VAR[0] + EDGE_VAR[1]), EDGE_MIN_TEXELS / ppm)
    sel = act & (F.edge_k > 0) & (F.edge_d < bmax)
    if sel.any():
        p, pp = pos[sel], ppm[sel]
        ub = uniform(fbm(p, EDGE_WIDTH_CELL_M, pp, spec.sub_seed("edge_band")))
        b = np.maximum(spec.edge_band_m * (EDGE_VAR[0] + EDGE_VAR[1] * ub), EDGE_MIN_TEXELS / pp)
        chip = smoothstep(CHIP_LO, CHIP_HI, uniform(fbm(p, CHIP_CELL_M, pp, spec.sub_seed("edge_chip"))))
        w[sel] = F.edge_k[sel] * (1.0 - smoothstep(EDGE_CORE * b, b, F.edge_d[sel])) * chip
    if ao is not None:
        w *= smoothstep(0.35, 0.70, ao)
    kr = np.isin(role, [ROLE_ID[r] for r in EDGE_KAPPA_ROLES]) & act
    w[kr] += 0.5 * np.clip(F.kappa[kr] / KAPPA0, 0.0, 1.0)
    out["edge"] = np.clip(w, 0.0, 1.0)

    # 6.2 grime - ground
    wg = np.zeros(N)
    if spec.ground in ("grade", "plinth"):
        gz = float(spec.ground_z)
        band = float(spec.ground_band_m)
        sel = act & (z < gz + 1.5 * band)
        if sel.any():
            ug = uniform(fbm(pos[sel], GROUND_CELL_M, ppm[sel], spec.sub_seed("ground")))
            t = (z[sel] - gz) / band + 0.25 * (ug - 0.5)
            wb = (1.0 - smoothstep(0.0, 1.0, t)) ** GROUND_EXP
            wb = np.where(nz[sel] < 0.7, wb, 0.0)
            below = z[sel] < gz
            if spec.ground == "grade":
                wb = np.where(below, 1.0, wb)
            else:
                up = uniform(fbm(pos[sel], PLINTH_CELL_M, ppm[sel], spec.sub_seed("plinth")))
                wb = np.where(below, 0.5 + 0.2 * (up - 0.5), wb)
            wg[sel] = wb
        if compose_contact:
            # the TOTAL at the contact matches the target: subtract compose's 6 % band in
            # the effective (GROUND_GAIN-deepened) weight, then map back to a mask weight
            k = (float(spec.intensity) * float(spec.grime) * GRIME_LUMA_DARK
                 * _coef("grime")[role] * F.part_mult[tp, 1] * F.part_scale[tp])
            c = np.where(np.abs(nz) < 0.5,
                         np.clip(1.0 - z / COMPOSE_CONTACT_BAND_M, 0.0, 1.0), 0.0)
            have = k > 1e-6
            e = _ground_eff(wg) - COMPOSE_CONTACT_AMP * c / np.where(have, k, 1.0)
            wg = np.where(have, _ground_inv(e), wg)
    out["grime_ground"] = np.where(act, wg, 0.0)

    # eave (walls under a down-facing surface, AO assists only in sheltered zones)
    we = np.zeros(N)
    wall = act & (np.abs(nz) < 0.5)
    ch = F.cover_h.astype(np.float64)
    sel = wall & (ch <= spec.eave_band_m)
    if sel.any():
        ue = uniform(fbm(pos[sel], EAVE_CELL_M, ppm[sel], spec.sub_seed("eave")))
        we[sel] = (1.0 - ch[sel] / spec.eave_band_m) ** EAVE_EXP * (0.7 + 0.3 * ue)
    if ao is not None:
        e2 = np.where(wall & (ch < 3.0),
                      EAVE_AO_AMP * (1.0 - smoothstep(EAVE_AO[0], EAVE_AO[1], ao)), 0.0)
        we = 1.0 - (1.0 - we) * (1.0 - e2)
    out["grime_eave"] = np.clip(we, 0.0, 1.0)

    # cavity
    cav = np.clip(-F.kappa.astype(np.float64) / KAPPA0, 0.0, 1.0)
    if ao is not None:
        cav = np.maximum(cav, 1.0 - smoothstep(CAVITY_AO[0], CAVITY_AO[1], ao))
    out["grime_cavity"] = np.where(act, CAVITY_AMP * cav, 0.0)

    # door threshold and kick band
    wd = np.zeros(N)
    if F.doors:
        dd = F.door_d.astype(np.float64)
        dz = F.door_dz.astype(np.float64)
        up = act & (nz >= 0.7) & (np.abs(dz) <= 0.35) & np.isfinite(dd)
        wd[up] = 1.0 - smoothstep(0.3, 1.2, dd[up])
        kick = act & F.door_own & (dz >= 0.0) & (dz < 0.30)
        wd[kick] = np.maximum(wd[kick], 1.0 - dz[kick] / 0.30)
    out["grime_door"] = wd

    out["grime"] = 1.0 - ((1.0 - out["grime_ground"]) * (1.0 - out["grime_eave"])
                          * (1.0 - out["grime_cavity"]) * (1.0 - out["grime_door"]))

    # 6.3 rust: streaks below sources (+ galv self-spots low on each galv part)
    wr = np.zeros(N)
    rs = F.rust_src.astype(np.float64)
    sel = act & (rs > 1e-4)
    if sel.any():
        p = pos[sel]
        g = F.g[sel].astype(np.float64)
        l = F.l[sel].astype(np.float64)
        n = F.ngeo[sel].astype(np.float64)
        abc = np.stack([np.sum(p * l, 1), np.sum(p * g, 1), np.sum(p * n, 1)], axis=1)
        streak = smoothstep(STREAK_LO, STREAK_HI,
                            uniform(fbm(abc, STREAK_CELLS_M, ppm[sel], spec.sub_seed("streak"))))
        wr[sel] = rs[sel] * streak
    galv = act & (role == ROLE_ID["galv"])
    if galv.any():
        zr = (z[galv] - F.part_zmin[tp[galv]]) / np.maximum(
            F.part_zmax[tp[galv]] - F.part_zmin[tp[galv]], 1e-3)
        low = 1.0 - smoothstep(0.0, 0.25, zr)
        us = uniform(fbm(pos[galv], SPOT_CELL_M, ppm[galv], spec.sub_seed("spot")))
        spots = SPOT_AMP * smoothstep(SPOT_LO, SPOT_HI, us) * (0.25 + 0.75 * low)
        wr[galv] = np.maximum(wr[galv], spots)
    out["rust"] = np.clip(wr, 0.0, 1.0)

    # 6.4 snow (coverage target, NOT x intensity, off at snow = 0)
    sn = np.zeros(N)
    if float(spec.snow) > 0.0:
        elig = (act & F.part_snow[tp] & (F.slot < 0.05) & (nz >= 0.70)
                & np.isinf(F.cover_h) & (F.edge_d >= spec.snow_inset_m))
        if elig.any():
            us = uniform(fbm(pos[elig], SNOW_CELL_M, ppm[elig], spec.sub_seed("snow")))
            s = float(spec.snow)
            sn[elig] = (smoothstep(1.0 - s - 0.08, 1.0 - s + 0.08, us)
                        * smoothstep(0.70, 0.85, nz[elig])
                        * smoothstep(spec.snow_inset_m, spec.snow_inset_m + 0.3, F.edge_d[elig]))
    out["snow"] = sn

    # 6.5 scuffs: streaks along the part's principal axis on traffic texels (no radial direction:
    # F5), an isotropic worn patch within SCUFF_ISO_M of a threshold, density falling off
    # isotropically with the distance to the door, horizontal on pads. Every cell is FIXED in meters
    # (F3): octave 0 never fades, octaves k >= 1 fade by density
    sc = np.zeros(N)
    tr = F.traffic.astype(np.float64)
    tsel = act & (np.abs(tr).sum(axis=1) > 0.5)
    if tsel.any():
        t = tr[tsel]
        t /= np.linalg.norm(t, axis=1, keepdims=True)
        p = pos[tsel]
        along = p[:, 0] * t[:, 0] + p[:, 1] * t[:, 1]
        across = -p[:, 0] * t[:, 1] + p[:, 1] * t[:, 0]
        pp = ppm[tsel]
        dd = F.door_d[tsel].astype(np.float64)
        ddf = np.where(np.isfinite(dd), dd, 99.0)
        abz = np.stack([along, across, p[:, 2]], 1)
        u = uniform(fbm(abz, SCUFF_CELLS_M, pp, spec.sub_seed("scuff")))
        iso = 1.0 - smoothstep(SCUFF_ISO_M[0], SCUFF_ISO_M[1], ddf)
        near = iso > 0.0
        if near.any():
            ui = uniform(fbm(abz[near], (SCUFF_CELLS_M[0], SCUFF_CELLS_M[0], SCUFF_CELLS_M[2]),
                             pp[near], spec.sub_seed("scuff_iso")))
            u[near] = u[near] * (1.0 - iso[near]) + ui * iso[near]
        dens = 1.0 - 0.6 * np.clip((ddf - 2.0) / 6.0, 0.0, 1.0)
        dens = np.where(F.part_stair[tp[tsel]], 1.0, dens)
        sc[tsel] = smoothstep(SCUFF_LO, SCUFF_HI, u) * dens
    pad = act & (role == ROLE_ID["pad"])
    if pad.any():
        p = pos[pad]
        l = F.l[pad].astype(np.float64)
        g = F.g[pad].astype(np.float64)
        hz = np.linalg.norm(l, axis=1) < 0.5
        l[hz] = (1.0, 0.0, 0.0)
        g[hz] = (0.0, 1.0, 0.0)
        pp = ppm[pad]
        abc = np.stack([np.sum(p * l, 1), np.sum(p * g, 1),
                        np.sum(p * F.ngeo[pad].astype(np.float64), 1)], 1)
        u = uniform(fbm(abc, SCUFF_CELLS_M, pp, spec.sub_seed("pad")))
        sc[pad] = np.maximum(sc[pad], smoothstep(SCUFF_LO, SCUFF_HI, u))
    out["scuff"] = sc
    return {k: np.asarray(v, dtype=np.float32) for k, v in out.items()}


# --------------------------------------------------------------------------------------
# apply_weathering (sections 6-7)
# --------------------------------------------------------------------------------------

def _occ(ao, fields):
    if ao is None:
        return np.ones(fields.N, dtype=np.float64)
    a = _compact(ao, fields).astype(np.float64)
    return np.clip(a * (1.0 - AO_FLOOR) + AO_FLOOR, 0.0, 1.0)


def apply_weathering(maps, fields, spec, *, compose_contact=False, gutter_px=3, effects=EFFECTS,
                     masks=None):
    """
    Composite the section-6 effects over `maps` (WeatherMaps, linear) in the order tone -> grime ->
    rust -> edge -> scuff -> snow. Then apply these steps: the section-7 guards (ranges, slot bound
    per spec.slot_wear, albedo floor), the 1:1 protect restore and the gutter dilation (slot-bounded
    too). Returns a NEW WeatherMaps. ctl and ao are the same objects as the input (never written).
    `effects` drops effects (tests).
    """
    _check_pair(fields, spec)
    F = fields
    A = F.A
    base_in = np.asarray(maps.base, dtype=np.float32)
    nrm_in = np.asarray(maps.nrm, dtype=np.float32)
    msk_in = np.asarray(maps.msk, dtype=np.float32)
    if base_in.shape[:2] != (A, A):
        raise ValueError(f"maps are {base_in.shape[:2]}, fields are {A} x {A}")
    if masks is None:
        masks = compute_masks(maps, F, spec, compose_contact=compose_contact)
    effects = tuple(effects)
    idx = F.idx
    cb, cn, cm = base_in.shape[-1], nrm_in.shape[-1], msk_in.shape[-1]
    B0 = base_in.reshape(-1, cb)[idx, :3].astype(np.float64)
    S0 = msk_in.reshape(-1, cm)[idx, 3].astype(np.float64)
    M0 = msk_in.reshape(-1, cm)[idx, 0].astype(np.float64)
    B, S, M = B0.copy(), S0.copy(), M0.copy()
    role = F.role.astype(np.int64)
    tp = F.tpart.astype(np.int64)
    act = (~F.protect_c).astype(np.float64)
    I = float(spec.intensity)
    sc = F.part_scale[tp] * act
    mult = F.part_mult[tp]
    LU = LUMA.astype(np.float64)
    gm = 1.0 - GRIME_MUL.astype(np.float64)

    if "tone" in effects and "tone" in masks:
        tf = 1.0 + TONE_AMP * I * float(spec.tone) * sc * masks["tone"].astype(np.float64)
        B = B * tf[:, None]
    if "grime" in effects:
        wgr = masks["grime"] * (1.0 + (GROUND_GAIN - 1.0) * masks["grime_ground"])
        d = np.minimum(1.0, I * spec.grime * _coef("grime")[role] * mult[:, 1] * sc * wgr)
        B = B * (1.0 - d[:, None] * gm[None, :])
        S = S - GRIME_SMOOTH * d
    if "rust" in effects:
        r = np.minimum(RUST_MAX, I * spec.rust * _coef("rust")[role] * mult[:, 2] * sc * masks["rust"])
        hit = r > 0
        if hit.any():
            ur = uniform(fbm(F.pos[hit], RUST_COL_CELL_M, F.ppm.reshape(-1)[idx][hit],
                             spec.sub_seed("rust_col")))
            col = RUST_LIN.astype(np.float64)[None, :] * (0.75 + 0.5 * ur)[:, None]
            rh = r[hit]
            B[hit] = B[hit] * (1.0 - rh)[:, None] + col * rh[:, None]
            M[hit] = M[hit] * (1.0 - rh)
            S[hit] = S[hit] * (1.0 - rh) + RUST_SMOOTH * rh
    if "edge" in effects:
        e = I * spec.edge * mult[:, 0] * sc * masks["edge"]
        L = B @ LU
        B = ((B * (1.0 - EDGE_GREY * e)[:, None] + (L * EDGE_GREY * e)[:, None])
             * (1.0 + _coef("lift")[role] * e)[:, None])
        S = S + _coef("dsmooth")[role] * e
    if "scuff" in effects:
        s = I * spec.scuff * _coef("scuff")[role] * mult[:, 3] * sc * masks["scuff"]
        L = B @ LU
        B = ((B * (1.0 - SCUFF_GREY * s)[:, None] + (L * SCUFF_GREY * s)[:, None])
             * (1.0 + SCUFF_LIFT * s)[:, None])
        S = S - SCUFF_SMOOTH * s
    sig = np.zeros(F.N)
    if "snow" in effects and float(spec.snow) > 0.0:
        sig = masks["snow"].astype(np.float64) * act
        B = B * (1.0 - sig)[:, None] + SNOW_LIN.astype(np.float64)[None, :] * sig[:, None]
        S = S * (1.0 - sig) + SNOW_SMOOTH * sig
        M = M * (1.0 - sig)

    # -- guards (section 7) --------------------------------------------------------------
    B = np.clip(B, 0.0, 1.0)                                         # G7 ranges
    S = np.clip(S, 0.0, 1.0)
    M = np.clip(M, 0.0, 1.0)
    c = np.asarray(maps.ctl, dtype=np.float32)
    slot = np.max(c.reshape(-1, c.shape[-1])[idx, :3], axis=1).astype(np.float64)
    on = slot > 0.0                                                  # G3 slot guard
    if on.any():
        if spec.slot_wear == "bounded":
            beta = (F.part_slot_bound[tp[on]] / slot[on])[:, None]
            B[on] = np.clip(B[on], B0[on] * (1.0 - beta), B0[on] * (1.0 + beta))
        else:
            B[on] = _neutral_on_slot(B0[on], B[on], slot[on])
    occ = _occ(maps.ao, F)                                           # G4 albedo floor
    Lin = B0 @ LU
    Lout = B @ LU
    thr = np.minimum(Lin, occ * float(spec.albedo_floor))
    need = (Lout < thr) & (Lin > Lout)
    if need.any():                     # mix toward the input: stays inside the G3 box
        t = np.clip((thr[need] - Lout[need]) / (Lin[need] - Lout[need]), 0.0, 1.0)[:, None]
        B[need] = B[need] + t * (B0[need] - B[need])
    pc = F.protect_c                                                 # G6 (compact)
    B[pc], S[pc], M[pc] = B0[pc], S0[pc], M0[pc]

    base = base_in.copy()
    msk = msk_in.copy()
    nrm = nrm_in.copy()
    bf = base.reshape(-1, cb)
    mf = msk.reshape(-1, cm)
    bf[idx, :3] = B.astype(np.float32)
    mf[idx, 3] = S.astype(np.float32)
    mf[idx, 0] = M.astype(np.float32)
    snowed = sig > 0.0
    if snowed.any():                                                 # snow buries the relief
        nf = nrm.reshape(-1, cn)
        ii = idx[snowed]
        n = nf[ii, :3].astype(np.float64) * 2.0 - 1.0
        k = (0.85 * sig[snowed])[:, None]
        n = n * (1.0 - k) + np.array((0.0, 0.0, 1.0))[None, :] * k
        n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
        n[:, 2] = np.maximum(n[:, 2], 1e-3)
        nf[ii, :3] = (n * 0.5 + 0.5).astype(np.float32)

    prot = F.protect                                                 # G6: rects 1:1
    if prot.any():
        base[prot] = base_in[prot]
        msk[prot] = msk_in[prot]
        nrm[prot] = nrm_in[prot]
    if gutter_px and gutter_px > 0:                                  # G8 gutter
        _gutter(base_in, base, msk_in, msk, F, int(gutter_px), maps.ctl, spec)
    return WeatherMaps(base, nrm, msk, maps.ctl, maps.ao)


def _neutral_on_slot(b0, b1, slot):
    """slot_wear "free": on recolor-slot texels the change becomes a pure luminance ratio of the input
    (b0 * L(b1) / L(b0)), mixed in by the slot weight. So the vanilla picker tint (a multiply under
    ControlMask) still reads on worn paint. Rust turns into a neutral darkening instead of a tinted
    orange. ControlMask itself is never written."""
    LU = LUMA.astype(np.float64)
    L0 = b0 @ LU
    L1 = b1 @ LU
    ratio = np.where(L0 > 1e-9, L1 / np.maximum(L0, 1e-9), 1.0)
    s = np.clip(slot, 0.0, 1.0)[:, None]
    return np.clip(b1 * (1.0 - s) + b0 * ratio[:, None] * s, 0.0, 1.0)


def _gutter(base_in, base, msk_in, msk, F, steps, ctl=None, spec=None):
    """Spread the base RGB / metal / smoothness deltas `steps` texels into uncovered texels
    by masked 4-neighbor mean passes (never into protect rects, no wrap at the border).
    Gutter texels under ControlMask (real bakes dilate it into the gutter) obey the same
    slot guard as covered texels (fix pass 1, F6): +-slot_bound / slot per channel when
    spec.slot_wear is "bounded", the neutral luminance ratio when it is "free"."""
    A = F.A
    cov = np.zeros(A * A, dtype=bool)
    cov[F.idx] = True
    cov = cov.reshape(A, A)
    delta = np.zeros((A, A, 5), dtype=np.float32)
    delta[..., 0:3] = base[..., 0:3] - base_in[..., 0:3]
    delta[..., 3] = msk[..., 0] - msk_in[..., 0]
    delta[..., 4] = msk[..., 3] - msk_in[..., 3]
    delta[~cov] = 0.0
    known = cov.copy()
    filled = np.zeros((A, A), dtype=bool)
    blocked = F.protect
    for _ in range(steps):
        acc = np.zeros_like(delta)
        cnt = np.zeros((A, A), dtype=np.float32)
        for sh, ax in ((1, 0), (-1, 0), (1, 1), (-1, 1)):
            kn = np.roll(known, sh, axis=ax)
            dv = np.roll(delta, sh, axis=ax)
            if ax == 0:
                kn[0 if sh == 1 else -1, :] = False
            else:
                kn[:, 0 if sh == 1 else -1] = False
            acc += np.where(kn[..., None], dv, 0.0)
            cnt += kn
        new = (~known) & (cnt > 0) & (~blocked)
        if not new.any():
            break
        delta[new] = acc[new] / cnt[new][:, None]
        known |= new
        filled |= new
    nz = filled & (np.abs(delta).max(axis=-1) > 0.0)
    if nz.any():
        b_in = base_in[nz, 0:3].astype(np.float64)
        b_new = np.clip(b_in + delta[nz, 0:3].astype(np.float64), 0.0, 1.0)
        if ctl is not None and spec is not None:
            c = np.asarray(ctl, dtype=np.float32)[nz, 0:3].max(axis=-1).astype(np.float64)
            on = c > 0.0
            if on.any():
                if spec.slot_wear == "bounded":
                    beta = (float(spec.slot_bound) / c[on])[:, None]
                    b_new[on] = np.clip(b_new[on], b_in[on] * (1.0 - beta),
                                        b_in[on] * (1.0 + beta))
                else:
                    b_new[on] = _neutral_on_slot(b_in[on], b_new[on], c[on])
        base[nz, 0:3] = b_new.astype(base.dtype)
        msk[nz, 0] = np.clip(msk_in[nz, 0] + delta[nz, 3], 0.0, 1.0)
        msk[nz, 3] = np.clip(msk_in[nz, 3] + delta[nz, 4], 0.0, 1.0)


# --------------------------------------------------------------------------------------
# Report (section 8)
# --------------------------------------------------------------------------------------

VL_BUDGET = dict(VL1=0.05, VL2=0.08, VL3_lo=-0.06, VL3_hi=0.01, VL3_abs=0.30, VL4=0.5)
VL4_STD_FLOOR = 0.10        # std(L) floored at 10 % of the part's mean L (flat-color parts)
#: perceptual delta (fix pass 1): texels more than this far below ground_z are not
#: "visible" (L2's plinth runs to z -7.2 m, and the terrain covers it in game)
VIS_BELOW_M = 0.5
#: texels with raw AO below this are enclosed - the inner sides of 0.3 m gables and walls,
#: whose AO rays all hit the opposite wall (measured on P2: gable_hqback / _hqfront inner
#: faces, AO 0.000, 42 k texels). Never seen from outside. Not "visible" when AO is known
AO_ENCLOSED = 0.02
DLSTAR_JND = 2.0            # |dL*| above ~2 is a just-noticeable lightness step


def lstar(Y):
    """CIE L* (0..100) of linear luminance Y (white = 1)."""
    Y = np.asarray(Y, dtype=np.float64)
    return np.where(Y > 216.0 / 24389.0, 116.0 * np.cbrt(np.maximum(Y, 0.0)) - 16.0,
                    Y * (24389.0 / 27.0))


def visible_texels(fields, spec, ao=None):
    """(N,) bool: covered texels a 3/4 camera can see and the layer may touch. A texel is visible when
    it is:
    - not protected
    - not role none
    - not an underside (n.z > -0.2)
    - not buried (z >= ground_z - VIS_BELOW_M in grade / plinth mode)
    - not enclosed, when the raw AO is known (AO >= AO_ENCLOSED: interior faces of thick walls)

    The vanilla budget and the perceptual delta are both measured on this set (fix pass 1)."""
    F = fields
    nz = F.ngeo[:, 2].astype(np.float64)
    vis = ~F.protect_c & (F.role != NONE_ID) & (nz > -0.2)
    if spec.ground in ("grade", "plinth"):
        vis &= F.pos[:, 2].astype(np.float64) >= float(spec.ground_z) - VIS_BELOW_M
    if ao is not None:
        vis &= _compact(ao, F).astype(np.float64) >= AO_ENCLOSED
    return vis


def _tint_rgbs(tint):
    """Slot 1/2/3 multiply colors from {"R"|1: rgb, "G"|2: rgb, "B"|3: rgb} or a list."""
    out = [np.ones(3), np.ones(3), np.ones(3)]
    if not tint:
        return out
    if isinstance(tint, dict):
        for i, keys in enumerate((("R", 1, "1"), ("G", 2, "2"), ("B", 3, "3"))):
            for k in keys:
                if k in tint and tint[k] is not None:
                    out[i] = np.asarray(tint[k], dtype=np.float64)[:3]
                    break
    else:
        for i, v in enumerate(list(tint)[:3]):
            if v is not None:
                out[i] = np.asarray(v, dtype=np.float64)[:3]
    return out


def perceptual_delta(before, after, fields, spec, *, tint=None, visible=None):
    """
    Perceptual size of the change on the visible texels (visible_texels). It compares the CIE L* of
    the linear luminance of BaseColor before and after. When `tint` is given, BaseColor is
    multiplied by the slot tint under ControlMask first, as the vanilla picker does. Returns the
    mean
    |dL*|, the share of texels with |dL*| > 2 (a just-noticeable step), p95 |dL*| and the
    mean L* before. Texture space: what a texel can show, not what the camera resolves.
    """
    F = fields
    idx = F.idx
    b0 = np.asarray(before.base, dtype=np.float32)
    b1 = np.asarray(after.base, dtype=np.float32)
    B0 = b0.reshape(-1, b0.shape[-1])[idx, :3].astype(np.float64)
    B1 = b1.reshape(-1, b1.shape[-1])[idx, :3].astype(np.float64)
    if tint:
        c = np.asarray(before.ctl, dtype=np.float32)
        cc = np.clip(c.reshape(-1, c.shape[-1])[idx, :3].astype(np.float64), 0.0, 1.0)
        for ch, rgb in enumerate(_tint_rgbs(tint)):
            m = cc[:, ch:ch + 1]
            mul = (1.0 - m) + rgb[None, :] * m
            B0 = B0 * mul
            B1 = B1 * mul
    LU = LUMA.astype(np.float64)
    L0 = lstar(B0 @ LU)
    L1 = lstar(B1 @ LU)
    vis = (visible_texels(F, spec, before.ao) if visible is None
           else np.asarray(visible, dtype=bool))
    if not vis.any():
        return {"texels": 0, "mean_abs_dLstar": 0.0, "share_dLstar_gt_2": 0.0,
                "p95_abs_dLstar": 0.0, "mean_Lstar_before": 0.0, "tinted": bool(tint)}
    d = np.abs(L1 - L0)[vis]
    return {"texels": int(vis.sum()), "mean_abs_dLstar": _r(d.mean(), 4),
            "share_dLstar_gt_2": _r((d > DLSTAR_JND).mean(), 4),
            "p95_abs_dLstar": _r(np.percentile(d, 95), 3),
            "mean_Lstar_before": _r(L0[vis].mean(), 3), "tinted": bool(tint)}


def _r(v, nd=5):
    v = float(v)
    return round(v, nd) if math.isfinite(v) else v


def weather_report(before, after, fields, spec, masks=None):
    """Vanilla-look budget VL1-VL5 (section 8) on covered, unprotected texels, the guard results,
    per-effect coverage, roles and source counts. Prints one WARN line outside the budget. Never
    raises."""
    F = fields
    idx = F.idx
    b0 = np.asarray(before.base, dtype=np.float32)
    b1 = np.asarray(after.base, dtype=np.float32)
    m0 = np.asarray(before.msk, dtype=np.float32)
    m1 = np.asarray(after.msk, dtype=np.float32)
    n0 = np.asarray(before.nrm, dtype=np.float32)
    n1 = np.asarray(after.nrm, dtype=np.float32)
    act = ~F.protect_c
    B0 = b0.reshape(-1, b0.shape[-1])[idx, :3].astype(np.float64)
    B1 = b1.reshape(-1, b1.shape[-1])[idx, :3].astype(np.float64)
    L0 = B0 @ LUMA.astype(np.float64)
    L1 = B1 @ LUMA.astype(np.float64)
    S0 = m0.reshape(-1, m0.shape[-1])[idx, 3].astype(np.float64)
    S1 = m1.reshape(-1, m1.shape[-1])[idx, 3].astype(np.float64)
    # VL1-VL4 measure the visible WEAR (fix pass 1: on visible_texels, because buried plinth and
    # enclosed interior faces are never seen). Snowed texels (a coverage target, VL5) are left out.
    # VL1 over every covered texel is kept as VL1_all_covered
    vis = visible_texels(F, spec, before.ao)
    wear = act & vis
    if masks is not None and "snow" in masks:
        wear &= ~(np.asarray(masks["snow"]) > 0)
    a = wear & (L0 > 1e-4)
    rel = np.abs(L1[a] - L0[a]) / L0[a] if a.any() else np.zeros(1)
    dS = (S1 - S0)[wear] if wear.any() else np.zeros(1)
    aa = act & (L0 > 1e-4)
    rel_all = np.abs(L1[aa] - L0[aa]) / L0[aa] if aa.any() else np.zeros(1)
    vl = {"VL1_mean_rel_dL": float(rel.mean()),
          "VL2_share_rel_dL_gt_0.15": float((rel > 0.15).mean()),
          "VL3_mean_dsmooth": float(dS.mean()), "VL3_max_abs_dsmooth": float(np.abs(dS).max()),
          "VL1_all_covered": _r(rel_all.mean()), "visible_texels": int(vis.sum())}
    tp = F.tpart.astype(np.int64)
    cnt = np.bincount(tp[wear], minlength=len(F.part_names) + 1)
    cnt[0] = 0
    top = [int(p) for p in np.argsort(-cnt, kind="stable")[:5] if cnt[p] > 0]
    vl4 = []
    for p in top:
        sel = wear & (tp == p)
        if not sel.any():
            continue
        l0 = L0[sel]
        sd = float(l0.std())
        floor = VL4_STD_FLOOR * float(l0.mean())
        den = max(sd, floor, 1e-6)
        vl4.append({"part": F.part_names[p - 1], "texels": int(sel.sum()),
                    "role": ROLES[int(F.part_role[p])], "std_L": _r(sd),
                    "floored": bool(floor > sd),
                    "ratio": _r(np.abs(L1[sel] - l0).mean() / den, 4)})
    vl["VL4_parts"] = vl4
    vl["VL4_max"] = max([v["ratio"] for v in vl4], default=0.0)
    if masks is not None and "snow" in masks:
        snow_share = float((masks["snow"] > 0).mean()) if F.N else 0.0
    else:
        snow_share = 0.0 if float(spec.snow) == 0.0 else float("nan")
    vl["VL5_snow_share"] = snow_share
    checks = {
        "VL1": vl["VL1_mean_rel_dL"] <= VL_BUDGET["VL1"],
        "VL2": vl["VL2_share_rel_dL_gt_0.15"] <= VL_BUDGET["VL2"],
        "VL3": (VL_BUDGET["VL3_lo"] <= vl["VL3_mean_dsmooth"] <= VL_BUDGET["VL3_hi"]
                and vl["VL3_max_abs_dsmooth"] <= VL_BUDGET["VL3_abs"]),
        "VL4": vl["VL4_max"] <= VL_BUDGET["VL4"],
        "VL5": snow_share == 0.0 or float(spec.snow) > 0.0,
    }
    vl["ok"] = bool(all(checks.values()))
    vl["failed"] = [k for k, v in checks.items() if not v]
    vl["budget"] = dict(VL_BUDGET)
    for k in ("VL1_mean_rel_dL", "VL2_share_rel_dL_gt_0.15", "VL3_mean_dsmooth",
              "VL3_max_abs_dsmooth", "VL5_snow_share"):
        vl[k] = _r(vl[k])

    g = {}
    g["G1_alpha_kept"] = bool(np.array_equal(b0[..., 3], b1[..., 3])
                              and (n0.shape[-1] < 4 or np.array_equal(n0[..., 3], n1[..., 3])))
    g["G2_msk_GB_kept"] = bool(np.array_equal(m0[..., 1:3], m1[..., 1:3]))
    g["G5_ctl_identity"] = bool(after.ctl is before.ctl)
    c = np.asarray(before.ctl, dtype=np.float32)
    slot = c.reshape(-1, c.shape[-1])[idx, :3].max(axis=1)
    full = slot >= 0.999
    if full.any():
        rr = np.abs(B1[full] - B0[full]) / np.maximum(B0[full], 1e-9)
        g["G3_worst_full_slot_rel"] = _r(rr.max(), 6)
    else:
        g["G3_worst_full_slot_rel"] = 0.0
    occ = _occ(before.ao, F)
    thr = np.minimum(L0 / occ, float(spec.albedo_floor))
    dark = L1 < L0 - 1e-9
    g["G4_worst_floor_gap"] = _r(np.min(L1[dark] / occ[dark] - thr[dark]), 7) if dark.any() else 0.0
    prot = F.protect
    g["G6_protect_identical"] = bool(np.array_equal(b0[prot], b1[prot])
                                     and np.array_equal(m0[prot], m1[prot])
                                     and np.array_equal(n0[prot], n1[prot]))
    g["G7_range_ok"] = bool(b1[..., :3].min() >= 0 and b1[..., :3].max() <= 1
                            and m1[..., (0, 3)].min() >= 0 and m1[..., (0, 3)].max() <= 1)
    roles = {ROLES[r]: int(n) for r, n in enumerate(np.bincount(F.role, minlength=len(ROLES))) if n}
    cov = {}
    if masks is not None:
        for k in ("edge", "grime_ground", "grime_eave", "grime_cavity", "grime_door", "grime",
                  "rust", "snow", "scuff"):
            if k in masks:
                cov[k] = _r((masks[k][act] > 0.1).mean() if act.any() else 0.0)
    st = dict(F.stats)
    sources = {k: st.get(k) for k in ("lattice_faces", "lattice_curtain_faces", "lattice_skipped",
                                      "rust_sources", "geo_candidates", "geo_pairs",
                                      "traffic_parts", "cover_overflow_cells", "down_faces")}
    sources["doors"] = len(st.get("doors") or [])
    rep = {"name": spec.name, "A": F.A, "N": F.N, "seed": int(F.seed), "spec_key": spec.key(),
           "intensity": spec.intensity, "snow": spec.snow, "ground": spec.ground,
           "slot_wear": spec.slot_wear,
           "perceptual": perceptual_delta(before, after, F, spec),
           "vanilla": vl, "guards": g, "coverage": cov, "roles": roles, "sources": sources,
           "doors": st.get("doors"), "protect_texels": st.get("protect_texels"),
           "role_source": st.get("role_source"), "roles_refined": st.get("roles_refined"),
           "fields_MB": round(F.nbytes() / 1e6, 1), "fields_seconds": st.get("seconds")}
    if not vl["ok"]:
        print(f"[weather] WARN vanilla budget {spec.name} at intensity {spec.intensity}: "
              f"{', '.join(vl['failed'])} (VL1 {vl['VL1_mean_rel_dL']:.3f}, VL2 "
              f"{vl['VL2_share_rel_dL_gt_0.15']:.3f}, VL3 {vl['VL3_mean_dsmooth']:+.3f}/"
              f"{vl['VL3_max_abs_dsmooth']:.2f}, VL4 {vl['VL4_max']:.2f})")
    return rep


# --------------------------------------------------------------------------------------
# post_compose wrapper (section 2.1)
# --------------------------------------------------------------------------------------

class WeatherHook:
    """
    cs2_bake post_compose hook: runs `inner` (or bake_unique's own no-hook compose_maps
    branch) and then the weathering. Unknown attributes are read from `inner`, so
    lodge.run's hook.density / height_range / seconds keep working on a wrapped hook.
    """

    def __init__(self, inner, spec, *, mat_keys=None, mat_roles=None, detail_specs=None,
                 stamped=(), reserved=(), protect_px=(), matrix=None, compose_contact=False,
                 gutter_px=3, keep_fields=False):
        spec.validate()
        self.inner = inner
        self.spec = spec
        self.mat_keys = None if mat_keys is None else list(mat_keys)
        self.mat_roles = None if mat_roles is None else dict(mat_roles)
        self.detail_specs = detail_specs
        self.stamped = tuple(stamped or ())
        self.reserved = tuple(tuple(float(x) for x in r) for r in (reserved or ()))
        self.protect_px = protect_px if callable(protect_px) else tuple(protect_px or ())
        self.matrix = matrix
        self.compose_contact = bool(compose_contact)
        self.gutter_px = int(gutter_px)
        self.keep_fields = bool(keep_fields)
        self.report = None
        self.fields = None
        self.masks = None

    def __getattr__(self, name):
        if name.startswith("__"):
            raise AttributeError(name)
        inner = self.__dict__.get("inner")
        if inner is None:
            raise AttributeError(name)
        return getattr(inner, name)

    def __call__(self, base, nrm, msk, ctl, ao, mesh, A):
        t0 = time.perf_counter()
        if self.inner is not None:
            base, nrm, msk, ctl = self.inner(base, nrm, msk, ctl, ao, mesh, A)
        else:                                   # bake_unique's own no-hook branch
            base, nrm, msk, ctl = cs2_compose.compose_maps(
                base, nrm, msk, ctl, None, None, 0.0,
                {"reserved": list(self.reserved), "gloss_var": 0.0})
        t1 = time.perf_counter()
        maps = WeatherMaps(np.asarray(base, dtype=np.float32), np.asarray(nrm, dtype=np.float32),
                           np.asarray(msk, dtype=np.float32), ctl, ao)
        fl = build_fields(mesh, A, self.spec, maps, matrix=self.matrix, mat_keys=self.mat_keys,
                          mat_roles=self.mat_roles, detail_specs=self.detail_specs,
                          stamped=self.stamped, reserved=self.reserved,
                          protect_px=self.protect_px)
        t2 = time.perf_counter()
        masks = compute_masks(maps, fl, self.spec, compose_contact=self.compose_contact)
        t3 = time.perf_counter()
        out = apply_weathering(maps, fl, self.spec, compose_contact=self.compose_contact,
                               gutter_px=self.gutter_px, masks=masks)
        t4 = time.perf_counter()
        rep = weather_report(maps, out, fl, self.spec, masks)
        rep["seconds"] = {"inner": round(t1 - t0, 3), "fields": round(t2 - t1, 3),
                          "masks": round(t3 - t2, 3), "apply": round(t4 - t3, 3),
                          "total": round(time.perf_counter() - t0, 3)}
        rep["compose_contact"] = self.compose_contact
        self.report = rep
        if self.keep_fields:
            self.fields = fl
            self.masks = masks
        v = rep["vanilla"]
        pd = rep["perceptual"]
        print(f"[weather] {self.spec.name} @ {A}: intensity {self.spec.intensity} "
              f"({self.spec.slot_wear}), N {fl.N}, "
              f"VL1 {v['VL1_mean_rel_dL']:.3f} VL2 {v['VL2_share_rel_dL_gt_0.15']:.3f} "
              f"VL3 {v['VL3_mean_dsmooth']:+.3f} VL4 {v['VL4_max']:.2f} ok {v['ok']}; "
              f"dL* mean {pd['mean_abs_dLstar']:.2f} >2 {pd['share_dLstar_gt_2']:.3f}; "
              f"{rep['seconds']['total']:.1f} s")
        return out.base, out.nrm, out.msk, ctl


def make_weather_hook(inner, spec, *, mat_keys=None, mat_roles=None, detail_specs=None,
                      stamped=(), reserved=(), protect_px=(), matrix=None,
                      compose_contact=False, gutter_px=3, keep_fields=False):
    """Wrap a generator's post_compose hook (or None) with the weathering layer.
    `protect_px`: texel rects, or a callable (mesh, A) -> rects, restored 1:1 (a painter
    that stamps only part of an island, e.g. lift_tower's warning triangle)."""
    return WeatherHook(inner, spec, mat_keys=mat_keys, mat_roles=mat_roles,
                       detail_specs=detail_specs, stamped=stamped, reserved=reserved,
                       protect_px=protect_px, matrix=matrix, compose_contact=compose_contact,
                       gutter_px=gutter_px, keep_fields=keep_fields)


def report_json(rep, indent=1):
    """json.dumps of a weather report (numpy scalars converted), e.g. for a manifest."""
    def conv(o):
        if isinstance(o, np.integer):
            return int(o)
        if isinstance(o, np.floating):
            return float(o)
        if isinstance(o, np.bool_):
            return bool(o)
        if isinstance(o, np.ndarray):
            return o.tolist()
        raise TypeError(type(o).__name__)
    return json.dumps(rep, default=conv, sort_keys=True, indent=indent)
