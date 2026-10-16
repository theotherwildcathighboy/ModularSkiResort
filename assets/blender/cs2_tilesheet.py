"""
cs2_tilesheet.py - the TILED texture route for procedural buildings.

T16 texture-parity probe (2026-10-16, docs/research/art/texture-parity-2026-10-16.md) and its ROLLOUT to
the eight lodge.py buildings (2026-10-16, docs/research/art/texture-parity-rollout-2026-10-16.md).

WHY
---
Every building ships ONE unique-UV atlas (cs2_bake.bake_unique): 1,741 m2 of ski school on a
2048^2 map is 20 px/m, and a 6.5 cm brick course is smaller than one texel. Vanilla gets
300-500 px/m on buildings of the same size from ONE main surface whose UVs OVERLAP onto a
4096^2 sheet of tileable material swatches + trims (measured: EU_CommercialHotel01_L1_3x2,
2,017 m2, 4096^2, p50 518 px/m, summed UV area 37.2; ElementarySchool01 99 px/m, UV sum 15.3;
the EU house is the small-building exception, 346 px/m from a unique 4096^2 on 153 m2).
Mod textures import non-VT (full residency), so the vanilla house's "unique 4096^2 per
building" route would cost 12 x 4096^2 for the school; the overlapped-sheet route costs one
shared sheet for the whole family.

LAYOUTS (LAYOUTS[name]; `grid` cells per side, a swatch spans 1 or 2 cells)
    "probe"    the T16 probe: 4 x 4 swatches of A/4 (14 kit materials, palette, labels).
    "rollout"  the family sheet for all eight buildings: an 8 x 8 grid of A/8 cells; 8 HERO
               materials (the big walls and roofs) on 2 x 2 cells (A/4 swatches, ~300-400 px/m
               at 4096), 14 more materials and 15 WEATHERED variants ("<key>~w") on single cells
               (A/8, ~150-310 px/m), one palette cell (8 x 8 flat colours) and a 2 x 1 label
               region split into fixed per-building rects (LABEL_ALLOC). `alias` maps a key onto
               another key's swatch (lux_slate = slate: same CC0 set, tile and tint);
               `palette_extra` draws a set material as a flat cell (plaster: 5 m2 in total).
               `repeat` (per key, default 1): the swatch holds r x r copies of the material tile,
               so a face is cut every r tiles (r^2 fewer pieces) at 1/r the px/m.

WHAT (behind Recipe.tiled / lodge.py --tiled; nothing else calls this module)
    sheet_layout(A, layout="rollout", repeats=None) -> layout dict (rects only, no pixels)
    build_sheet(out_dir, name, A, layout, repeats, cache_dir) -> layout + maps (linear arrays)
        Swatches from the CC0 sets (prefilter_set box-filtered to the swatch content, tint =
        lin(target) / set mean, recolour slots luminance-normalised, DetailSpec "ribs" baked into
        the normal), a weathered variant per "<key>~w" entry (weather_overlay: rule-free tile
        content + a periodic overlay, so clean and weathered pieces of one wall continue the same
        boards / courses), smoothness from the measured vanilla table (SMOOTH_TARGET).
    write_sheet(layout, emissive=True, labels=()) -> files
        Pastes the per-building label rects (<sheet>.aux/labels/*.npz, lodge._tiled_lod0) and
        writes <name>_{BaseColor,Normal,MaskMap,ControlMask,Emissive}.png (BaseColor sRGB alpha 1,
        Normal OpenGL, MaskMap R metal / A smoothness, ControlMask R/G/B slots, A 0).
    tile_mesh(obj, uvtile, keys, layout, ..., weather=None) -> report
        Replaces obj.data by a re-meshed copy: every face of a tiled key is CLIPPED at the
        integer lines of its UVTile coordinates (world metres / tile) so each piece maps inside
        ONE swatch's content square (main textures import with Clamp wrap: no repeat);
        T-junctions are closed by inserting every split point into every polygon that shares
        the original edge. With `weather` (TileWeather) faces are also cut at the band lines of
        the weathering rules and each piece picks the clean or the "~w" swatch by RULE
        (weather_rules); cheap opaque "decal strips" (sill drips, door-threshold scuffs) are
        added as separate quads 12 mm proud, mapped tile-continuously onto the host's "~w"
        swatch. Flat keys map into their palette cell, label parts into the building's label
        rect. One UV layer "UVUnique", material slots cleared, face attributes copied, plus
        `cs2ts_mat` (material index) and `cs2ts_w` (0 clean, 1 weathered piece, 2 strip).
    face_ppm / mask_stats / check_uv_cells / weather_stats: measurement.

Why no ALPHA decal strips: any BaseColor texel alpha <= 128 under a main-mesh UV triangle flips the
WHOLE main surface to DefaultCutout (AlphaDetectPostProcessor.cs:112-160, SurfacePostProcessor.cs:144-160;
art-pipeline-v2.md row "BaseColor A"), and there is no cutout sub-mesh suffix. The strips are opaque
quads that carry the wall / floor material itself with the stain on it.

Smoothness (MaskMap A) is authored from MEASURED vanilla values (bl_smooth.py, texture-parity
section 3): pitched roofs 0.25-0.35, walls 0.26-0.41, trims 0.22-0.44; metal only on metal
roofs (Barn01: metal 1.0 at 0.35) and bare galvanised steel.
"""

import hashlib
import json
import math
import os
import sys
from dataclasses import dataclass, field

import bpy
import bmesh
import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset     # noqa: E402
import cs2_bake      # noqa: E402
import cs2_bldg as K  # noqa: E402
import cs2_compose   # noqa: E402
import cs2_uv        # noqa: E402

LUMA = np.array((0.2126, 0.7152, 0.0722))
SHEET_A = 4096
SWATCH = 1024
PAD = 32
W_SUFFIX = "~w"
F_SUFFIX = "~f"
# thin details drawn flat (their material's mean colour) on every tiled building
FLAT_DETAIL_PREFIXES = ("gutter_", "downspout_", "snowguard_", "ridge_")
SHEET_VERSION = "rollout-1"

# ---- the probe layout (T16): swatch slot (col, row) in units of A/4, rows bottom-up --------
SWATCHES = {
    "timber_lap": (0, 0), "timber_vboard": (1, 0), "siding_paint": (2, 0), "timber_shake": (3, 0),
    "deck": (0, 1), "timber_heavy": (1, 1), "stone_rubble": (2, 1), "rock": (3, 1),
    "slate": (0, 2), "plaster": (1, 2), "concrete": (2, 2), "galv": (3, 2),
    "metal_roof": (0, 3), "metal_roof_s1": (1, 3),
}
PALETTE_SLOT = (2, 3)
LABEL_SLOT = (3, 3)
PALETTE_GRID = 4

LAYOUTS = {
    "probe": {
        "grid": 4,
        "tiles": {k: (c, r, 1) for k, (c, r) in SWATCHES.items()},
        "palette": (PALETTE_SLOT[0], PALETTE_SLOT[1], 1), "palette_grid": 4,
        "labels": (LABEL_SLOT[0], LABEL_SLOT[1], 1, 1),
        "alias": {}, "palette_extra": (),
    },
    # rollout (2026-10-16): areas from the blockout census of the eight buildings (rollout doc
    # section 2); heroes = the walls and roofs that dominate the close views
    "rollout": {
        "grid": 8,
        "tiles": {
            # heroes, 2 x 2 cells (A/4 swatch: 960 px content at 4096)
            "timber_lap": (0, 0, 2), "siding_paint": (2, 0, 2), "stone_rubble": (4, 0, 2),
            "lux_stone_tan": (6, 0, 2),
            "lux_siding_walnut": (0, 2, 2), "slate": (2, 2, 2), "metal_roof": (4, 2, 2),
            "concrete": (6, 2, 2),
            # single cells (A/8 swatch: 480 px content at 4096)
            "deck": (0, 4, 1), "galv": (1, 4, 1), "timber_shake": (2, 4, 1), "timber_heavy": (3, 4, 1),
            "timber_vboard": (4, 4, 1), "metal_roof_s1": (5, 4, 1), "lux_paver": (6, 4, 1),
            "lux_glulam": (7, 4, 1),
            "lux_siding_red": (0, 5, 1), "lux_rock": (1, 5, 1), "lux_balrail": (2, 5, 1),
            "lux_drive": (3, 5, 1), "lux_vboard_red": (4, 5, 1), "lux_tg": (5, 5, 1),
            # weathered variants (single cells)
            "timber_lap~w": (0, 6, 1), "siding_paint~w": (1, 6, 1), "stone_rubble~w": (2, 6, 1),
            "lux_stone_tan~w": (3, 6, 1), "lux_siding_walnut~w": (4, 6, 1), "slate~w": (5, 6, 1),
            "metal_roof~w": (6, 6, 1), "concrete~w": (7, 6, 1),
            "deck~w": (0, 7, 1), "timber_shake~w": (1, 7, 1), "timber_heavy~w": (2, 7, 1),
            "timber_vboard~w": (3, 7, 1), "metal_roof_s1~w": (4, 7, 1), "lux_paver~w": (5, 7, 1),
            "lux_siding_red~w": (6, 7, 1),
        },
        "palette": (7, 7, 1), "palette_grid": 8,
        "labels": (6, 5, 2, 1),
        "alias": {"lux_slate": "slate"},
        "palette_extra": ("plaster",),
    },
}

# Per-building label rects inside the rollout label region (px at A = 4096, scaled with A):
# (x, y, w, h) from the region origin, sized by each host's label area (census: L5 26.1 m2,
# P2 8.7, L3 5.6, P1 2.6, school 2.5, L1 2.4, L4 1.4, L2 none after its sign set) and shape (the
# school's 3.3 m tower emblem needs height: a 128-high rect held it to 34 px/m, 256 gives ~70)
LABEL_ALLOC = {
    "L5": (0, 0, 512, 512), "P2": (512, 0, 256, 256), "L3": (768, 0, 256, 128),
    "school": (768, 128, 256, 256), "P1": (512, 256, 256, 128), "L1": (512, 384, 256, 128),
    "L4": (768, 384, 128, 128), "L2": (896, 384, 128, 128),
}

# Flat-colour tiles (BldgMat.set None) that still tile with a procedural detail normal
FLAT_TILE_M = {"metal_roof": 3.0, "metal_roof_s1": 3.0}

# MaskMap A targets, measured on vanilla (texture-parity-2026-10-16.md section 3; area-weighted
# means of EU / NA ResidentialLow01, EU_CommercialLow01, EU_CommercialHotel01, ElementarySchool01,
# Barn01). A CC0 roughness map keeps its variation at SMOOTH_GAIN around the target mean.
SMOOTH_TARGET = {
    "timber_lap": 0.35, "timber_vboard": 0.35, "siding_paint": 0.38, "timber_shake": 0.30,
    "deck": 0.30, "timber_heavy": 0.30, "stone_rubble": 0.25, "rock": 0.25,
    "slate": 0.30, "plaster": 0.30, "concrete": 0.38, "galv": 0.40,
    "metal_roof": 0.33, "metal_roof_s1": 0.33,
    # palette (flat): vanilla "other" (trims, frames) 0.22-0.44; glass_dark: hotel dark wall
    # band 0.69 (glass reads by the _Win sub-mesh, these are opaque dark panels)
    "trim": 0.35, "trim_white": 0.35, "accent": 0.30, "glass_dark": 0.70, "label": 0.30,
    "lamp": 0.45, "ski_black": 0.45, "ski_blue": 0.45, "ski_red": 0.45, "rubber": 0.15,
    "safety_yellow": 0.35, "fire_red": 0.40, "lamp_red": 0.45,
    # rollout: the swanky (L4 / L5) keys on the same vanilla classes (walls 0.26-0.41, pitched
    # roofs 0.25-0.35, trims 0.22-0.44); water / pool tile keep their glaze, lower than before
    "lux_siding_red": 0.35, "lux_siding_walnut": 0.35, "lux_vboard_red": 0.35, "lux_tg": 0.35,
    "lux_balrail": 0.35, "lux_stone_tan": 0.25, "lux_glulam": 0.33, "lux_rock": 0.25,
    "lux_slate": 0.30, "lux_paver": 0.25, "lux_drive": 0.30, "lux_trim": 0.35,
    "lux_water": 0.85, "lux_pooltile": 0.50, "lux_ember": 0.30, "lux_hedge": 0.10,
}
SMOOTH_GAIN = 0.5
# Metal (MaskMap R) only where vanilla has it: Barn01's metal roof is metal 1.0 / smooth 0.35
# (texture-parity section 3); bare galvanised steel stays 1.0 (BldgMat). Every other key 0.
METAL_OVERRIDE = {}
METAL_ROLLOUT = {"metal_roof": 1.0, "metal_roof_s1": 1.0}
LABEL_PPM = 160.0           # label rects in the label swatch (building signs ~300 px/m moved to sign sets)

# Weathering class per key (weather_overlay); keys absent here never get a "~w" swatch
W_CLASS = {
    "stone_rubble": "stone", "lux_stone_tan": "stone",
    "timber_lap": "timber", "timber_shake": "timber", "timber_heavy": "timber",
    "timber_vboard": "timber", "lux_siding_walnut": "timber", "lux_siding_red": "timber",
    "deck": "deck", "siding_paint": "paint", "metal_roof": "metal_roof",
    "metal_roof_s1": "metal_roof", "slate": "slate", "concrete": "concrete", "lux_paver": "paver",
}


def _lin(c):
    return np.asarray(K.srgb_to_linear(K._rgb3(c)), np.float64)


def _load(path):
    return K._load_raw(path)                    # (h, w, 4) float64 bottom-up, raw values


def ensure_materials():
    """The swanky keys (lux_*) live in recipes/l5_swanky_bottom.py and are registered by its
    builders; the family sheet needs them whatever building is being built."""
    from recipes import l5_swanky_bottom as _l5
    _l5.register_materials()


def base_key(skey):
    return skey[:-len(W_SUFFIX)] if skey.endswith(W_SUFFIX) else skey


def swatch_px(key, A=SHEET_A, swatch=SWATCH, pad=PAD):
    """(x0, y0, C) of a PROBE-layout swatch: content origin (texels, bottom-up) and size."""
    col, row = SWATCHES[key]
    return col * swatch + pad, row * swatch + pad, swatch - 2 * pad


def tile_m_of(key):
    m = K.BLDG_MATERIALS[base_key(key)]
    if m.set:
        return K.square_tile_m(m)
    return FLAT_TILE_M[base_key(key)]


def tiled_keys():
    return tuple(sorted(SWATCHES))


def smooth_of(key):
    k = base_key(key)
    m = K.BLDG_MATERIALS[k]
    return float(SMOOTH_TARGET.get(k, m.smooth if m.smooth is not None else 0.4))


def metal_of(key, layout_name="rollout"):
    k = base_key(key)
    if layout_name == "rollout" and k in METAL_ROLLOUT:
        return METAL_ROLLOUT[k]
    return float(METAL_OVERRIDE.get(k, K.BLDG_MATERIALS[k].metal))


# ======================================================================================
# Layout
# ======================================================================================

def sheet_layout(A=SHEET_A, layout="rollout", repeats=None):
    """Rects of every swatch / the palette / the label region for sheet size A (no pixels)."""
    spec = LAYOUTS[layout]
    if layout == "rollout":
        ensure_materials()              # the palette order must not depend on which builder ran
    g = int(spec["grid"])
    cell = A // g
    if cell * g != A:
        raise ValueError(f"sheet {A} not divisible by grid {g}")
    repeats = dict(repeats or {})
    out = {"A": A, "layout": layout, "grid": g, "cell": cell, "tiles": {}, "palette": {},
           "alias": dict(spec["alias"]), "palette_extra": list(spec["palette_extra"]),
           "repeats": {k: int(v) for k, v in sorted(repeats.items())}}
    for k, (c, r, span) in spec["tiles"].items():
        sw = span * cell
        pad = max(8, sw // 32)
        C = sw - 2 * pad
        rep = int(repeats.get(base_key(k), 1))
        if C % rep:
            raise ValueError(f"{k}: content {C} not divisible by repeat {rep}")
        out["tiles"][k] = {"rect_px": [c * cell + pad, r * cell + pad, C], "swatch_px": sw,
                           "pad": pad, "repeat": rep}
    pc, pr, ps = spec["palette"]
    out["palette_rect"] = [pc * cell, pr * cell, ps * cell]
    out["palette_grid"] = int(spec["palette_grid"])
    lc, lr, lw, lh = spec["labels"]
    out["label_region"] = [lc * cell, lr * cell, lw * cell, lh * cell]
    out["label_rect"] = list(out["label_region"])
    # flat keys -> palette cells (sorted, deterministic)
    flat = sorted(k for k, m in K.BLDG_MATERIALS.items()
                  if not m.set and k not in spec["tiles"] and k not in out["alias"])
    flat += [k for k in spec["palette_extra"] if k not in flat]
    if layout == "rollout":
        flat += sorted(k + F_SUFFIX for k in spec["tiles"] if not k.endswith(W_SUFFIX))
    pg = out["palette_grid"]
    pcell = (ps * cell) // pg
    if len(flat) > pg * pg:
        raise RuntimeError(f"palette overflow: {len(flat)} flat keys > {pg * pg}")
    PX, PY = out["palette_rect"][:2]
    for i, key in enumerate(flat):
        out["palette"][key] = {"rect_px": [PX + (i % pg) * pcell, PY + (i // pg) * pcell, pcell]}
    out["hash"] = layout_hash(out)
    return out


def label_rect_for(layout, recipe_key):
    """The building's own rect in the label region (rollout), else the whole region."""
    LX, LY, LW, LH = layout["label_region"]
    if layout["layout"] != "rollout" or recipe_key not in LABEL_ALLOC:
        return [LX, LY, LW, LH]
    s = layout["A"] / 4096.0
    x, y, w, h = LABEL_ALLOC[recipe_key]
    return [LX + int(x * s), LY + int(y * s), int(w * s), int(h * s)]


def swatch_key(layout, key, weathered=False):
    """The sheet entry a material key draws from: (skey | None for palette)."""
    k = layout["alias"].get(key, key)
    if weathered and (k + W_SUFFIX) in layout["tiles"]:
        return k + W_SUFFIX
    return k if k in layout["tiles"] else None


def layout_hash(layout):
    keys = sorted({base_key(k) for k in layout["tiles"]} | {k for k in layout["palette"] if not k.endswith(F_SUFFIX)})
    mats = {k: repr(K.BLDG_MATERIALS[k]) for k in keys}
    blob = json.dumps({"v": SHEET_VERSION, "tiles": layout["tiles"], "palette": layout["palette"],
                       "repeats": layout["repeats"], "mats": mats, "smooth": SMOOTH_TARGET,
                       "metal": METAL_ROLLOUT}, sort_keys=True, default=str)
    return hashlib.sha1(blob.encode("utf-8")).hexdigest()[:16]


# ======================================================================================
# Swatch pixels
# ======================================================================================

def _ribs_height(C, tile_m, spec, along_u):
    """DetailSpec 'ribs' grooves (lines of constant horizontal coordinate) as an (C, C) height
    field in metres. along_u True: the horizontal in-plane axis is u (columns vary along u)."""
    s = (np.arange(C) + 0.5) / C * tile_m
    pitch = float(spec.seams_m)
    w = float(getattr(spec, "seam_width_m", 0.012) or 0.012)
    d = float(getattr(spec, "seam_depth_m", 0.004) or 0.004)
    ph = np.mod(s, pitch)
    dist = np.minimum(ph, pitch - ph)
    prof = np.where(dist < w / 2.0, -d * 0.5 * (1.0 + np.cos(np.pi * dist / (w / 2.0))), 0.0)
    if along_u:
        return np.broadcast_to(prof[None, :], (C, C)).copy()
    return np.broadcast_to(prof[:, None], (C, C)).copy()


def _swatch_maps(key, C, cache_dir=None, repeat=1, layout_name="probe"):
    """Linear base (C,C,3), normal 0..1 (C,C,3), metal, smooth (C,C), slot, info. With repeat
    r the content holds r x r copies of the material tile (prefiltered at C / r)."""
    m = K.BLDG_MATERIALS[key]
    tile = tile_m_of(key)
    r = int(repeat)
    N = C // r
    ppm = N / tile
    info = {"key": key, "tile_m": round(tile, 4), "ppm": round(ppm, 1), "set": m.set, "repeat": r}
    if m.set:
        pf = K.prefilter_set(m.set, K.image_tile_m(m), ppm, cache_dir=cache_dir)
        if pf["N"] != N:
            raise RuntimeError(f"{key}: prefilter N {pf['N']} != content {N}")
        tag = os.path.basename(pf["dir"])
        col = _load(os.path.join(pf["dir"], f"{tag}_Color.png"))[..., :3]
        nrm = _load(os.path.join(pf["dir"], f"{tag}_NormalGL.png"))[..., :3]
        rough = _load(os.path.join(pf["dir"], f"{tag}_Roughness.png"))[..., 0]
        lin = K.srgb_to_linear(col)
        if m.tint is not None:
            tint = np.asarray(K._rgb3(m.tint))
        elif m.target_srgb is not None:
            tint = _lin(m.target_srgb) / np.maximum(np.asarray(pf["mean_lin"]), 1e-6)
        else:
            tint = np.ones(3)
        base = lin * tint
        info["tint_lin"] = [round(float(t), 4) for t in tint]
        sm = 1.0 - rough if m.smooth is None else np.full((N, N), float(m.smooth))
        if r > 1:
            base = np.tile(base, (r, r, 1)); nrm = np.tile(nrm, (r, r, 1)); sm = np.tile(sm, (r, r))
    else:
        base = np.broadcast_to(_lin(m.target_srgb), (C, C, 3)).copy()
        nrm = np.broadcast_to(np.array((0.5, 0.5, 1.0)), (C, C, 3)).copy()
        sm = np.full((C, C), float(m.smooth if m.smooth is not None else 0.4))
    if m.recolor:
        lum = base @ LUMA
        k = float(K.srgb_to_linear(cs2_asset.RECOLOR_ALBEDO)) / max(float(lum.mean()), 1e-6)
        base = np.repeat(np.clip(lum * k, 0, 1)[..., None], 3, -1)
        info["recolor_scale"] = round(k, 4)
    if m.detail is not None and getattr(m.detail, "seam_axes", None) == "ribs" and m.detail.seams_m:
        rot = int(m.uv_rot) % 180
        along_u = (rot == 0)            # planar / box: u is the horizontal in-plane axis
        h = _ribs_height(C, tile * r, m.detail, along_u)
        det = cs2_compose.height_to_normal(h.astype(np.float32), C / (tile * r))
        nrm = cs2_compose.blend_rnm(nrm.astype(np.float32), det)
        info["detail"] = f"ribs {m.detail.seams_m} m, {'columns' if along_u else 'rows'}"
    src_mean = float(sm.mean())
    tgt = SMOOTH_TARGET.get(key, src_mean)
    sm = np.clip(tgt + (sm - src_mean) * SMOOTH_GAIN, 0.0, 1.0)
    info["smooth_src_mean"] = round(src_mean, 3)
    info["smooth_mean"] = round(float(sm.mean()), 3)
    metal = metal_of(key, layout_name)
    info["metal"] = metal
    info["slot"] = int(m.slot)
    return base, np.asarray(nrm, np.float64), metal, sm, int(m.slot), info


# ---- weathering overlays (periodic: a weathered piece continues its clean neighbours' tile) --

def _pnoise(C, scale_px, seed, beta=2.0, aniso=(1.0, 1.0)):
    """Periodic (C x C) fractal noise, zero mean / unit std: white noise shaped by 1/f^beta
    above the cut-off 1/scale_px (FFT, so it wraps exactly at the swatch edges)."""
    rng = np.random.default_rng(seed)
    F = np.fft.fft2(rng.standard_normal((C, C)))
    fy = np.fft.fftfreq(C)[:, None] * aniso[1]
    fx = np.fft.fftfreq(C)[None, :] * aniso[0]
    f = np.sqrt(fx * fx + fy * fy)
    amp = 1.0 / np.maximum(f, 1.0 / max(scale_px, 1.0)) ** (beta / 2.0)
    amp[0, 0] = 0.0
    n = np.real(np.fft.ifft2(F * amp))
    return (n - n.mean()) / max(n.std(), 1e-9)


def _smooth(x, lo, hi):
    t = np.clip((x - lo) / (hi - lo), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _streaks(C, seed, width_px, vertical_cols):
    """Down-running streaks: thin bands of constant u (vertical_cols) with length modulation."""
    rng = np.random.default_rng(seed + 7)
    n = C
    cols = _pnoise(n, width_px * 1.5, seed + 11, beta=1.2, aniso=(1.0, 0.0001))[0]   # 1-D along u
    band = _smooth(cols, 0.9, 1.9)                                                     # ~15 % of columns
    lenmod = _smooth(_pnoise(n, n / 2.5, seed + 13, beta=2.5), -0.4, 0.9)
    s = band[None, :] * lenmod
    s *= 0.6 + 0.4 * rng.random()
    return s if vertical_cols else s.T


def weather_overlay(key, base, nrm, metal, sm, seed):
    """(base, metal, sm) of the "~w" variant: the clean tile with a PARTIAL overlay (never a
    uniform tone shift, so a clean / weathered boundary reads as the edge of a stain, and the
    overlay stays sparse). Classes (W_CLASS): stone damp + lichen, timber silver-greying + water
    marks, paint dirt, metal-roof streaks, slate moss, concrete drip stains, deck / paver scuff.
    Linear albedo; deterministic per key."""
    cls = W_CLASS[base_key(key)]
    m = K.BLDG_MATERIALS[base_key(key)]
    C = base.shape[0]
    vertical_cols = (int(m.uv_rot) % 180 == 0)          # box / planar: world-vertical = v
    lum = (base @ LUMA)[..., None]
    b, mt, s = base.copy(), np.full((C, C), float(metal)), sm.copy()
    if cls == "stone":
        damp = _smooth(_pnoise(C, C / 2.2, seed, 2.2), -0.3, 1.2)
        lich = _smooth(_pnoise(C, C / 18.0, seed + 1, 1.4), 1.1, 1.9) * _smooth(_pnoise(C, C / 3, seed + 2), 0.0, 1.0)
        b = b * (1.0 - 0.22 * damp[..., None])
        b = b * (1 - 0.55 * lich[..., None]) + np.array((0.33, 0.34, 0.25)) * 0.55 * lich[..., None]
        s = s + 0.04 * damp - 0.10 * lich
    elif cls in ("timber", "deck"):
        g = _smooth(_pnoise(C, C / 2.0, seed, 2.0), -0.6, 0.9)
        grey = lum * np.array((1.0, 1.0, 1.04)) * 1.25
        b = b * (1 - 0.55 * g[..., None]) + grey * 0.55 * g[..., None]
        if cls == "timber":
            st = _streaks(C, seed + 3, C / 60.0, vertical_cols)
            b = b * (1.0 - 0.20 * st[..., None])
        else:
            sc = _smooth(_pnoise(C, C / 10.0, seed + 4, 1.6), 0.8, 1.8)
            b = b * (1.0 + 0.18 * sc[..., None])          # foot-worn, lighter
        s = s - 0.05 * g
    elif cls == "paint":
        d = _smooth(_pnoise(C, C / 2.5, seed, 2.2), -0.2, 1.1)
        st = _streaks(C, seed + 3, C / 70.0, vertical_cols)
        dirt = np.clip(0.6 * d + 0.7 * st, 0, 1)
        b = b * (1.0 - 0.20 * dirt[..., None])
        s = s - 0.06 * dirt
    elif cls == "metal_roof":
        st = _streaks(C, seed + 3, C / 50.0, True)            # planar roofs: v runs up the slope
        rust = st * _smooth(_pnoise(C, C / 6.0, seed + 5, 1.8), 0.3, 1.2)
        b = b * (1.0 - 0.28 * st[..., None])
        b = b * (1 - 0.5 * rust[..., None]) + np.array((0.30, 0.16, 0.09)) * 0.5 * rust[..., None] * lum.mean()
        mt = mt * (1.0 - 0.5 * rust)
        s = s - 0.08 * st
    elif cls == "slate":
        moss = _smooth(_pnoise(C, C / 2.4, seed, 2.3), 0.1, 1.3)
        b = b * (1 - 0.5 * moss[..., None]) + np.array((0.075, 0.095, 0.045)) * 0.5 * moss[..., None]
        s = s - 0.08 * moss
    elif cls == "concrete":
        st = _streaks(C, seed + 3, C / 55.0, vertical_cols)
        g = _smooth(_pnoise(C, C / 3.0, seed, 2.0), 0.0, 1.3)
        b = b * (1.0 - (0.25 * st + 0.10 * g)[..., None])
        s = s - 0.05 * np.clip(st + g, 0, 1)
    elif cls == "paver":
        d = _smooth(_pnoise(C, C / 6.0, seed, 1.8), 0.0, 1.2)
        b = b * (1.0 - 0.15 * d[..., None])
        s = s - 0.04 * d
    else:
        raise KeyError(cls)
    return np.clip(b, 0, 1), np.clip(mt, 0, 1), np.clip(s, 0, 1)


def _put(dst, x0, y0, block):
    h, w = block.shape[:2]
    dst[y0:y0 + h, x0:x0 + w] = block


def build_sheet(out_dir, name, A=SHEET_A, swatch=SWATCH, pad=PAD, keys=None, cache_dir=None,
                layout="probe", repeats=None):
    """The family sheet (module doc). Returns the layout (sheet_layout) with "maps" (linear
    arrays), "tiles" carrying per-swatch info and "means" (per-swatch mean base / mask / ctl,
    used to fill label backgrounds without the pixels). `keys` limits the probe swatches."""
    if layout == "probe" and A != 4 * swatch:
        raise ValueError("probe sheet is a 4 x 4 swatch grid")
    if layout == "rollout":
        ensure_materials()
    lay = sheet_layout(A, layout, repeats)
    base = np.zeros((A, A, 4)); base[..., 3] = 1.0     # LINEAR rgb + alpha (stamp_on_face wants 4)
    nrm = np.zeros((A, A, 3)); nrm[..., :] = (0.5, 0.5, 1.0)
    msk = np.zeros((A, A, 4))
    ctl = np.zeros((A, A, 4))
    emi = np.zeros((A, A, 3))
    means = {}
    tiles = lay["tiles"]
    want = sorted(tiles) if (keys is None or layout != "probe") else sorted(k for k in tiles if k in keys)
    for skey in want:
        t = tiles[skey]
        x0, y0, C = t["rect_px"]
        pd = t["pad"]
        k = base_key(skey)
        b, n, metal, sm, slot, info = _swatch_maps(k, C, cache_dir, repeat=t["repeat"], layout_name=layout)
        mt = np.full((C, C), float(metal))
        if skey.endswith(W_SUFFIX):
            seed = int(hashlib.sha1(skey.encode()).hexdigest()[:8], 16)
            b, mt, sm = weather_overlay(skey, b, n, metal, sm, seed)
            info["weathered"] = W_CLASS[k]
            info["smooth_mean"] = round(float(sm.mean()), 3)
        wrap = lambda a: np.pad(a, ((pd, pd), (pd, pd)) + ((0, 0),) * (a.ndim - 2), mode="wrap")
        X, Y = x0 - pd, y0 - pd
        _put(base[..., :3], X, Y, wrap(b))
        _put(nrm, X, Y, wrap(n))
        mm = np.zeros((C, C, 4)); mm[..., 0] = mt; mm[..., 3] = sm
        _put(msk, X, Y, wrap(mm))
        if slot:
            cc = np.zeros((C, C, 4)); cc[..., slot - 1] = 1.0
            _put(ctl, X, Y, wrap(cc))
        info["rect_px"] = [x0, y0, C]
        t.update(info)
        means[skey] = {"base": b.reshape(-1, 3).mean(0).tolist(), "msk": [float(mt.mean()), 0, 0, float(sm.mean())],
                       "ctl": ([1.0 if slot == c + 1 else 0.0 for c in range(3)] + [0.0])}
    for key, cellinfo in lay["palette"].items():
        cx, cy, cell = cellinfo["rect_px"]
        if key.endswith(F_SUFFIX):
            bk = key[:-len(F_SUFFIX)]
            mn = means[bk]
            m = K.BLDG_MATERIALS[bk]
            base[cy:cy + cell, cx:cx + cell, :3] = mn["base"]
            msk[cy:cy + cell, cx:cx + cell, 0] = mn["msk"][0]
            msk[cy:cy + cell, cx:cx + cell, 3] = mn["msk"][3]
            if m.slot:
                ctl[cy:cy + cell, cx:cx + cell, m.slot - 1] = 1.0
            cellinfo.update({"smooth": float(mn["msk"][3]), "metal": float(mn["msk"][0]), "slot": int(m.slot),
                             "emissive": False, "flat_of": bk})
            means[key] = mn
            continue
        m = K.BLDG_MATERIALS[key]
        if m.set:                                   # palette_extra: the set's tinted mean, flat
            pf = K.prefilter_set(m.set, K.image_tile_m(m), 8.0 / K.square_tile_m(m), cache_dir=cache_dir)
            tint = np.asarray(K._rgb3(m.tint)) if m.tint is not None else (
                _lin(m.target_srgb) / np.maximum(np.asarray(pf["mean_lin"]), 1e-6)
                if m.target_srgb is not None else np.ones(3))
            alb = np.asarray(pf["mean_lin"]) * tint
        else:
            alb = _lin(m.target_srgb)
        if m.recolor:
            alb = np.full(3, float(K.srgb_to_linear(cs2_asset.RECOLOR_ALBEDO)))
        smv = smooth_of(key) if (layout == "rollout" or key in SMOOTH_TARGET) else \
            (m.smooth if m.smooth is not None else 0.4)
        mtv = metal_of(key, layout)
        base[cy:cy + cell, cx:cx + cell, :3] = alb
        msk[cy:cy + cell, cx:cx + cell, 0] = mtv
        msk[cy:cy + cell, cx:cx + cell, 3] = smv
        if m.slot:
            ctl[cy:cy + cell, cx:cx + cell, m.slot - 1] = 1.0
        if m.emissive_srgb is not None:
            emi[cy:cy + cell, cx:cx + cell] = _lin(m.emissive_srgb)
        cellinfo.update({"smooth": float(smv), "metal": float(mtv), "slot": int(m.slot),
                         "emissive": m.emissive_srgb is not None, "flat_from_set": bool(m.set)})
        means[key] = {"base": [float(x) for x in alb], "msk": [float(mtv), 0, 0, float(smv)],
                      "ctl": [1.0 if m.slot == c + 1 else 0.0 for c in range(3)] + [0.0]}
    LX, LY, LW, LH = lay["label_region"]
    lab = K.BLDG_MATERIALS["label"]
    base[LY:LY + LH, LX:LX + LW, :3] = _lin(lab.target_srgb)
    msk[LY:LY + LH, LX:LX + LW, 3] = SMOOTH_TARGET["label"]
    lay["maps"] = {"base": base, "nrm": nrm, "msk": msk, "ctl": ctl, "emi": emi}
    lay["means"] = means
    lay["name"] = name
    lay["out_dir"] = out_dir
    # probe compatibility: the old key names
    if layout == "probe":
        lay["label_rect"] = [LX, LY, LW]
    return lay


def write_sheet(layout, emissive=True, labels=()):
    """Paste the label npz files (paste_labels) and encode + write the PNGs (BaseColor sRGB,
    Emissive sRGB, the rest raw). Returns the file names."""
    A = layout["A"]
    mp = layout["maps"]
    for p in labels:
        paste_labels(layout, p)
    name, out_dir = layout["name"], layout["out_dir"]
    os.makedirs(out_dir, exist_ok=True)

    def rgba(rgb, a=1.0):
        o = np.ones((A, A, 4), np.float32)
        o[..., :3] = np.clip(rgb, 0, 1)
        o[..., 3] = a
        return o
    n = mp["nrm"] * 2 - 1
    n[..., 2] = np.maximum(n[..., 2], 0.05)
    n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
    msk = np.clip(mp["msk"], 0, 1).astype(np.float32); msk[..., 1] = 0.0; msk[..., 2] = 0.0
    ctl = np.clip(mp["ctl"], 0, 1).astype(np.float32); ctl[..., 3] = 0.0
    over = ctl[..., :3].sum(-1) > 1.0 + 1e-3
    if over.any():
        raise RuntimeError(f"sheet ControlMask not exclusive on {int(over.sum())} texels")
    files = []
    maps = [(rgba(K.linear_to_srgb(mp["base"][..., :3])), "BaseColor", False),
            (rgba(n * 0.5 + 0.5), "Normal", True), (msk, "MaskMap", True), (ctl, "ControlMask", True)]
    if emissive:
        maps.append((rgba(K.linear_to_srgb(mp["emi"])), "Emissive", False))
    for arr, suf, is_data in maps:
        p = os.path.join(out_dir, f"{name}_{suf}.png")
        cs2_asset._write_png_raw(p, f"{name}_{suf}", arr, A, is_data)
        files.append(os.path.basename(p))
    return files


def save_layout(layout, path):
    d = {k: v for k, v in layout.items() if k not in ("maps",)}
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(d, fh, indent=1, sort_keys=True, default=lambda o: o.tolist() if hasattr(o, "tolist") else str(o))
        fh.write("\n")


def load_layout(path):
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


# ---- labels on a shared sheet: each building paints its own rect, the sheet composes them --

def paint_labels(layout, label_rects, recipe_labels, me, table, out_npz, margin=6):
    """Paint one building's labels into full-size scratch arrays (only its label rect is
    used), fill each rect with its host material's mean look first (fill_label_backgrounds),
    then save the building's label rect (base linear, msk, ctl, nrm) to `out_npz`."""
    A = layout["A"]
    base = np.zeros((A, A, 4), np.float32); base[..., 3] = 1.0
    ctl = np.zeros((A, A, 4), np.float32)
    msk = np.zeros((A, A, 4), np.float32)
    nrm = np.zeros((A, A, 3), np.float32); nrm[...] = (0.5, 0.5, 1.0)
    LX, LY, LW, LH = layout["label_rect"]
    lab = K.BLDG_MATERIALS["label"]
    base[LY:LY + LH, LX:LX + LW, :3] = _lin(lab.target_srgb)
    msk[LY:LY + LH, LX:LX + LW, 3] = SMOOTH_TARGET["label"]
    for pre, L in label_rects.items():
        mean = layout["means"].get(swatch_key(layout, L["key"]) or L["key"]) or layout["means"].get(L["key"])
        (ox, oy), (w, h) = L["origin_px"], L["size_px"]
        r = (slice(max(LY, oy - margin), min(LY + LH, oy + h + margin)),
             slice(max(LX, ox - margin), min(LX + LW, ox + w + margin)))
        if mean is not None:
            base[r[0], r[1], :3] = mean["base"]
            msk[r[0], r[1]] = mean["msk"]
            ctl[r[0], r[1]] = np.round(mean["ctl"])
    for prefix, painter, kw in recipe_labels:
        p = K.make_painter(painter, kw, face=prefix)
        cs2_compose.stamp_on_face(base, ctl, me, table, prefix, p)
    sl = (slice(LY, LY + LH), slice(LX, LX + LW))
    np.savez_compressed(out_npz, rect=np.array([LX, LY, LW, LH]), base=base[sl][..., :3],
                        msk=msk[sl], ctl=ctl[sl], nrm=nrm[sl])
    return out_npz


def paste_labels(layout, npz_path):
    d = np.load(npz_path)
    LX, LY, LW, LH = [int(v) for v in d["rect"]]
    mp = layout["maps"]
    sl = (slice(LY, LY + LH), slice(LX, LX + LW))
    mp["base"][sl][..., :3] = d["base"]
    mp["msk"][sl] = d["msk"]
    mp["ctl"][sl] = d["ctl"]
    mp["nrm"][sl] = d["nrm"]


# ======================================================================================
# Weathering rules (which faces take the "~w" swatch)
# ======================================================================================

@dataclass(frozen=True)
class TileWeather:
    """Rule set for LOD0 weathering on the tile sheet (rollout doc section 3). Heights in
    metres; per-side dicts keyed front (the arrival / road side, -Y after orient_for_net) /
    side / back. "North" cannot be known at authoring time (the player rotates the lot), so
    the BACK facade plays the weather side: it gets the most."""
    base_h: dict = field(default_factory=lambda: {"front": 0.5, "side": 0.8, "back": 1.1})
    eave_h: dict = field(default_factory=lambda: {"front": 0.0, "side": 0.4, "back": 0.6})
    roof_eave_m: dict = field(default_factory=lambda: {"front": 0.0, "side": 0.0, "back": 1.2, "top": 0.0})
    valleys: bool = True              # roof pieces beside a rising roof / wall (valley, cheek, chimney)
    valley_m: float = 0.8
    sill_drips: bool = True           # strip under every window sill on a weatherable wall
    drip_h: float = 0.7
    thresholds: bool = True           # strip on the floor in front of each door
    threshold_d: float = 0.9
    strip_offset: float = 0.012       # strips sit this far proud of their host face
    level: int = 0                    # fallback ladder position (weather_level), for the record


def weather_level(level):
    """The 'where tris allow' ladder: 0 everything, then drop the most expensive cut lines
    first (eave bands, roof eave bands + drips, front base bands), last the strips."""
    W = TileWeather()
    if level <= 0:
        return W
    if level == 1:
        return TileWeather(eave_h={"front": 0, "side": 0, "back": 0}, level=1)
    if level == 2:
        return TileWeather(eave_h={"front": 0, "side": 0, "back": 0},
                           roof_eave_m={"front": 0, "side": 0, "back": 0, "top": 0}, sill_drips=False, level=2)
    if level == 3:
        return TileWeather(base_h={"front": 0.0, "side": 0.8, "back": 1.1}, eave_h={"front": 0, "side": 0, "back": 0},
                           roof_eave_m={"front": 0, "side": 0, "back": 0, "top": 0}, sill_drips=False, level=3)
    if level == 4:
        return TileWeather(base_h={"front": 0, "side": 0, "back": 0}, eave_h={"front": 0, "side": 0, "back": 0},
                           roof_eave_m={"front": 0, "side": 0, "back": 0, "top": 0}, sill_drips=False, level=4)
    return None


def _side_of(n):
    h = np.array((n[0], n[1]))
    L = float(np.linalg.norm(h))
    if L < 0.3:
        return "top"
    h /= L
    if -h[1] > 0.7:
        return "front"
    if h[1] > 0.7:
        return "back"
    return "side"


class _WeatherCtx:
    """Per-triangle weathering context: world normals, sides, band lines, BVH."""

    def __init__(self, Vw, tris, keys_t, parts_t, part_name, layout, rules, ground_parts=()):
        from mathutils.bvhtree import BVHTree
        from mathutils import Vector
        self.V = Vw
        self.rules = rules
        self.Vector = Vector
        self.tris = tris
        self.part_name = part_name
        polys = [list(t) for t in tris]
        self.bvh = BVHTree.FromPolygons([tuple(v) for v in Vw], polys, all_triangles=False)
        nt = len(tris)
        self.n = np.zeros((nt, 3)); self.zmin = np.zeros(nt); self.zmax = np.zeros(nt)
        self.side = []
        self.wkey = []              # the weathered swatch (or None) per tri
        for i, t in enumerate(tris):
            P = Vw[list(t)]
            s = np.zeros(3)
            for j in range(1, len(P) - 1):
                s += np.cross(P[j] - P[0], P[j + 1] - P[0])
            L = np.linalg.norm(s)
            self.n[i] = s / L if L > 1e-12 else (0, 0, 1)
            self.zmin[i], self.zmax[i] = P[:, 2].min(), P[:, 2].max()
            self.side.append(_side_of(self.n[i]))
            sk = swatch_key(layout, keys_t[i], weathered=True)
            self.wkey.append(sk if (sk is not None and sk.endswith(W_SUFFIX)) else None)
        self.wall = np.abs(self.n[:, 2]) < 0.35
        self.roof = np.array([(self.n[i, 2] >= 0.35 and self.wkey[i] is not None and
                               W_CLASS[base_key(self.wkey[i])] in ("slate", "metal_roof")) for i in range(nt)])
        # roof eave height per part (lowest z of its roof-class faces)
        self.part_eave = {}
        for i in np.flatnonzero(self.roof):
            p = parts_t[i]
            self.part_eave[p] = min(self.part_eave.get(p, 1e9), self.zmin[i])
        self.parts_t = parts_t
        self.sheltered = np.zeros(nt, bool)
        self.top_flat = np.zeros(nt, bool)
        for i in np.flatnonzero(self.wall):
            if self.wkey[i] is None:
                continue
            P = Vw[list(tris[i])]
            top = np.abs(P[:, 2] - self.zmax[i]) < 0.01
            if top.sum() < 2:
                continue
            self.top_flat[i] = True
            q = P[top].mean(0) + self.n[i] * 0.15
            q[2] = self.zmax[i] - 0.05
            hit = self.bvh.ray_cast(Vector(q), Vector((0, 0, 1)), 1.5)
            self.sheltered[i] = hit[0] is not None

    def lines(self, i):
        """World-z cut lines for tri i (only those strictly inside its z range)."""
        R = self.rules
        if self.wkey[i] is None:
            return []
        out = []
        sd = self.side[i]
        if self.wall[i]:
            h = R.base_h.get(sd, 0.0)
            if h > 0:
                out.append(h)
            e = R.eave_h.get(sd, 0.0)
            if e > 0 and self.sheltered[i]:
                out.append(self.zmax[i] - e)
        elif self.roof[i]:
            d = R.roof_eave_m.get(sd, 0.0)
            if d > 0:
                rise = d * math.sqrt(max(0.0, 1.0 - self.n[i, 2] ** 2))
                out.append(self.part_eave.get(self.parts_t[i], self.zmin[i]) + rise)
        return [c for c in out if self.zmin[i] + 0.02 < c < self.zmax[i] - 0.02]

    def classify(self, i, cz, cpt):
        """'base' | 'eave' | 'roof_eave' | 'valley' | None for a piece of tri i (centroid cpt)."""
        R = self.rules
        if self.wkey[i] is None:
            return None
        sd = self.side[i]
        if self.wall[i]:
            h = R.base_h.get(sd, 0.0)
            if h > 0 and cz < h:
                return "base"
            e = R.eave_h.get(sd, 0.0)
            if e > 0 and self.sheltered[i] and cz > self.zmax[i] - e:
                return "eave"
            return None
        if self.roof[i]:
            d = R.roof_eave_m.get(sd, 0.0)
            if d > 0:
                rise = d * math.sqrt(max(0.0, 1.0 - self.n[i, 2] ** 2))
                if cz < self.part_eave.get(self.parts_t[i], self.zmin[i]) + rise:
                    return "roof_eave"
            if R.valleys and self._valley(i, cpt):
                return "valley"
        return None

    def _valley(self, i, cpt):
        V = self.Vector
        n = self.n[i]
        q = V(tuple(np.asarray(cpt) + n * 0.06))
        for a in range(8):
            ang = a * math.pi / 4
            d = V((math.cos(ang), math.sin(ang), 0.0))
            loc, nrm, idx, dist = self.bvh.ray_cast(q, d, self.rules.valley_m)
            if loc is None or idx is None or idx == i:
                continue
            if float(np.dot(self.n[idx], n)) > 0.98:            # same plane (own slope)
                continue
            if self.parts_t[idx] == self.parts_t[i] and float(np.dot(self.n[idx], n)) > 0.9:
                continue
            if self.n[idx, 2] < -0.3:                              # a soffit above: not rising
                continue
            return True
        return False


# ======================================================================================
# Geometry: clip tiled faces at tile boundaries, map every face into the sheet
# ======================================================================================

def _face_attrs(me):
    """FACE-domain attributes to carry (name -> (data_type, array))."""
    out = {}
    for a in me.attributes:
        if a.domain != "FACE" or a.name.startswith(".") or a.name in ("sharp_face", "material_index"):
            continue
        if a.data_type == "INT":
            arr = np.zeros(len(me.polygons), np.int32)
        elif a.data_type == "FLOAT":
            arr = np.zeros(len(me.polygons), np.float32)
        elif a.data_type == "BOOLEAN":
            arr = np.zeros(len(me.polygons), bool)
        else:
            continue
        a.data.foreach_get("value", arr)
        out[a.name] = (a.data_type, arr)
    return out


def _clip(poly, axis, c, keep_ge):
    """Sutherland-Hodgman against uv[axis] >= c (keep_ge) or <= c. poly: list of dicts
    {uv: (2,), p: (3,), on: frozenset of original edges it lies on, key: hashable}."""
    out = []
    n = len(poly)
    if n == 0:
        return out
    ins = lambda q: (q["uv"][axis] >= c - 1e-9) if keep_ge else (q["uv"][axis] <= c + 1e-9)
    for i in range(n):
        P, Q = poly[i], poly[(i + 1) % n]
        pi, qi = ins(P), ins(Q)
        if pi:
            out.append(P)
        if pi != qi:
            dp, dq = P["uv"][axis] - c, Q["uv"][axis] - c
            t = dp / (dp - dq)
            uv = P["uv"] + (Q["uv"] - P["uv"]) * t
            uv[axis] = c
            common = P["on"] & Q["on"]
            out.append({"uv": uv, "p": P["p"] + (Q["p"] - P["p"]) * t, "on": common,
                        "edge_t": None, "key": None, "_pq": (P, Q, t)})
    return out


def _clip_z(poly, zfun, c, keep_ge):
    """Sutherland-Hodgman against world z >= c / <= c. The crossing of a segment is computed
    from its endpoints in a CANONICAL order, so the two pieces sharing that segment get
    bit-identical points (one vertex key)."""
    out = []
    n = len(poly)
    if n == 0:
        return out
    z = [zfun(q["p"]) for q in poly]
    ins = lambda zz: (zz >= c - 1e-9) if keep_ge else (zz <= c + 1e-9)
    for i in range(n):
        j = (i + 1) % n
        P, Q = poly[i], poly[j]
        pi, qi = ins(z[i]), ins(z[j])
        if pi:
            out.append(P)
        if pi != qi:
            A_, B_, za, zb = (P, Q, z[i], z[j])
            if (tuple(A_["uv"]), tuple(A_["p"])) > (tuple(B_["uv"]), tuple(B_["p"])):
                A_, B_, za, zb = B_, A_, zb, za
            t = (za - c) / (za - zb)
            uv = A_["uv"] + (B_["uv"] - A_["uv"]) * t
            common = A_["on"] & B_["on"]
            out.append({"uv": uv, "p": A_["p"] + (B_["p"] - A_["p"]) * t, "on": common,
                        "edge_t": None, "key": None, "_pq": (A_, B_, t)})
    return out


def _cells_of(recs, uv3, max_cells, ti, key):
    lo = np.floor(uv3.min(0) + 1e-7); hi = np.ceil(uv3.max(0) - 1e-7)
    nx, ny = int(max(hi[0] - lo[0], 1)), int(max(hi[1] - lo[1], 1))
    if nx * ny > max_cells:
        raise RuntimeError(f"tri {ti} ({key}) spans {nx}x{ny} tiles")
    return lo, nx, ny


def _affine_tile(Pw, U):
    """(origin p0, u0, A 2x3) with uv = u0 + A (p - p0) for a planar polygon (world Pw, tile U)."""
    E = np.stack([Pw[1] - Pw[0], Pw[2] - Pw[0]], 1)          # 3 x 2
    F = np.stack([U[1] - U[0], U[2] - U[0]], 1)                # 2 x 2
    G = E.T @ E
    if abs(np.linalg.det(G)) < 1e-14:
        return None
    Aff = F @ np.linalg.inv(G) @ E.T                           # 2 x 3
    return Pw[0], U[0], Aff


# keys whose tiles have horizontal courses that must stay aligned around wall corners (box
# projection keeps v = world z continuous there): their patches only shift along u, unless small
COURSED = ("stone_rubble", "lux_stone_tan", "timber_lap", "lux_siding_red", "lux_siding_walnut",
           "timber_shake", "slate", "lux_slate")
SHIFT_FREE_M2 = 0.6


def shift_patches(tuv, tris, V, Mw, fmat, fpart, keys, skey, tiles, is_label):
    """Move every PLANAR PATCH (connected coplanar faces of one part and key with continuous
    UVTile) whose extent along a tile axis is under one tile, but which straddles a tile line,
    into a single cell along that axis (in place on `tuv`). The texture inside the patch stays
    continuous; box / planar / along projections already restart the pattern at every change
    of plane, so nothing visible is lost - except the course alignment of COURSED walls around
    corners (v = world z), which only shift along v when the patch is under SHIFT_FREE_M2.
    Saves the cut + T-junction triangles of most small parts (posts, rails, brackets, gutters,
    trims, stair treads, member sides). Returns {"patches_shifted": n, "axes": [u, v]}."""
    n = len(tris)
    Rw = Mw[:3, :3]
    nrm = np.zeros((n, 3)); area = np.zeros(n)
    for i, t in enumerate(tris):
        P = V[list(t)] @ Rw.T
        s = np.zeros(3)
        for j in range(1, len(P) - 1):
            s += np.cross(P[j] - P[0], P[j + 1] - P[0])
        L = np.linalg.norm(s)
        area[i] = 0.5 * L
        nrm[i] = s / L if L > 1e-12 else (0, 0, 1)
    parent = list(range(n))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    edge_owner = {}
    for i, t in enumerate(tris):
        if is_label[i] or skey[i] not in tiles:
            continue
        m = len(t)
        for j in range(m):
            a, b = int(t[j]), int(t[(j + 1) % m])
            e = (a, b) if a < b else (b, a)
            uva, uvb = tuv[i][j], tuv[i][(j + 1) % m]
            edge_owner.setdefault(e, []).append((i, {a: uva, b: uvb}))
    for e, own in edge_owner.items():
        for x in range(len(own)):
            for y in range(x + 1, len(own)):
                i, ui = own[x]; j, uj = own[y]
                if fpart[i] != fpart[j] or fmat[i] != fmat[j] or float(nrm[i] @ nrm[j]) < 0.9998:
                    continue
                if all(np.abs(ui[v] - uj[v]).max() < 1e-6 for v in e):
                    ra, rb = find(i), find(j)
                    if ra != rb:
                        parent[ra] = rb
    groups = {}
    for i in range(n):
        if is_label[i] or skey[i] not in tiles:
            continue
        groups.setdefault(find(i), []).append(i)
    shifted = 0
    axes = [0, 0]
    for g in groups.values():
        U = np.concatenate([tuv[i] for i in g])
        lo, hi = U.min(0), U.max(0)
        key = keys[int(fmat[g[0]])]
        sh = np.zeros(2)
        for ax in (0, 1):
            ext = hi[ax] - lo[ax]
            if ext >= 1.0 - 1e-6 or math.floor(lo[ax] + 1e-7) == math.floor(hi[ax] - 1e-7):
                continue
            if ax == 1 and key in COURSED and float(area[g].sum()) >= SHIFT_FREE_M2:
                continue
            up = math.ceil(lo[ax]) - lo[ax]          # move up onto the next line
            dn = math.floor(hi[ax]) - hi[ax]         # or down below the previous one
            sh[ax] = up if abs(up) <= abs(dn) else dn
            axes[ax] += 1
        if sh.any():
            for i in g:
                tuv[i] = tuv[i] + sh
            shifted += 1
    return {"patches": len(groups), "patches_shifted": shifted, "axes_uv": axes}


def tile_mesh(obj, uvtile, keys, layout, label_prefixes=(), label_ppm=LABEL_PPM, max_cells=4096,
              ground_prefixes=(), ground_band=0.011, weather=None, patch_shift=True, flat_prefixes=(),
              grid_offset=0.0):
    """
    Re-mesh `obj` for the sheet (module doc). `uvtile`: (n_loops, 2) UVTile coordinates of
    obj's CURRENT loops (tiles); `keys`: sorted material keys (material_index -> key);
    `weather`: TileWeather or None (no weathering). Returns a report dict (tris before /
    after, sliced faces, pieces, T-junction inserts, weathering counts per rule).
    """
    me = obj.data
    A = layout["A"]
    tiles = layout["tiles"]; pal = layout["palette"]
    alias = layout.get("alias", {})
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    # -- triangulate a working bmesh, carrying UVTile as a UV layer --------------------
    tmp_uv = me.uv_layers.new(name="CS2TS_tile", do_init=False)
    cs2_uv.set_loop_uvs(tmp_uv, uvtile)
    bm = bmesh.new()
    bm.from_mesh(me)
    me.uv_layers.remove(me.uv_layers["CS2TS_tile"])
    part_l = bm.faces.layers.int.get("part")
    _pn = lambda pi: table[pi - 1] if 1 <= pi <= len(table) else ""
    _lab = lambda f: part_l is not None and any(_pn(f[part_l]).startswith(pre) for pre in label_prefixes)
    uvl = bm.loops.layers.uv["CS2TS_tile"]

    def _convex(f):
        if len(f.loops) == 3:
            return True
        if len(f.loops) > 4:
            return False
        q = [l[uvl].uv for l in f.loops]
        cr = [(q[(i + 1) % 4] - q[i]).cross(q[(i + 2) % 4] - q[(i + 1) % 4]) for i in range(4)]
        return all(c > 1e-12 for c in cr) or all(c < -1e-12 for c in cr)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if not _lab(f) and not _convex(f)])
    bm.verts.index_update(); bm.faces.index_update()
    V = np.array([v.co[:] for v in bm.verts], np.float64)
    Mw = np.array(obj.matrix_world, np.float64)
    Vw = V @ Mw[:3, :3].T + Mw[:3, 3]
    zfun = lambda p: float(p @ Mw[2, :3] + Mw[2, 3])
    tris, tuv, fmat, fpart = [], [], [], []
    for f in bm.faces:
        tris.append([l.vert.index for l in f.loops])
        tuv.append([l[uvl].uv[:] for l in f.loops])
        fmat.append(f.material_index)
        fpart.append(f[part_l] if part_l is not None else 0)
    fattr = {}
    for name_, (dt, _arr) in _face_attrs(me).items():
        lay = (bm.faces.layers.int.get(name_) if dt == "INT" else
               bm.faces.layers.float.get(name_) if dt == "FLOAT" else
               bm.faces.layers.bool.get(name_) if dt == "BOOLEAN" else None)
        if lay is None:
            continue
        fattr[name_] = (dt, np.array([f[lay] for f in bm.faces]))
    bm.free()
    tuv = [np.array(t, np.float64) for t in tuv]
    skey = []
    for i, k_ in enumerate(fmat):
        kk = keys[k_]
        if kk in FLAT_TILE_M:                      # projection_specs: flat keys use 1 m per unit
            tuv[i] = tuv[i] / FLAT_TILE_M[kk]
        sk = alias.get(kk, kk)
        if sk in tiles:
            r = int(tiles[sk].get("repeat", 1))
            if r > 1:
                tuv[i] = tuv[i] / r
            if grid_offset:
                # plinth recipes: no vertex may sit in |z| <= 0.01 (check_base_mode), and box / along
                # projections put a tile line exactly at z = 0; the whole grid moves together, so
                # every pattern stays continuous
                tuv[i] = tuv[i] + grid_offset
            if kk != sk and abs(tile_m_of(kk) - tile_m_of(sk)) > 1e-6:
                raise ValueError(f"alias {kk} -> {sk}: tile {tile_m_of(kk)} != {tile_m_of(sk)}")
        skey.append(sk)
    fmat = np.array(fmat, np.int64); fpart = np.array(fpart, np.int64)
    n_tris0 = len(tris)
    part_name = lambda pi: table[pi - 1] if 1 <= pi <= len(table) else ""
    is_label = np.array([any(part_name(int(p)).startswith(pre) for pre in label_prefixes)
                         for p in fpart], bool)
    # flat details (FLAT_DETAIL_PREFIXES + recipe.tiled_flat): a tiled key drawn as its swatch's
    # mean colour from a palette cell "<key>~f" (no cuts), the vanilla way of texturing gutters,
    # ridges, snow guards and other thin trims
    is_flatp = np.array([(not is_label[i]) and skey[i] in tiles and (skey[i] + F_SUFFIX) in pal and
                         any(part_name(int(fpart[i])).startswith(f) for f in flat_prefixes)
                         for i in range(len(fpart))], bool)
    n_shift = shift_patches(tuv, tris, V, Mw, fmat, fpart, keys, skey, tiles, is_label | is_flatp) \
        if patch_shift else {}
    pkey = []
    for i in range(len(fpart)):
        if is_label[i] or (skey[i] in tiles and not is_flatp[i]):
            pkey.append(None)
        elif is_flatp[i]:
            pkey.append(skey[i] + F_SUFFIX)
        else:
            pkey.append(keys[int(fmat[i])])
    # -- label rects (shelf-packed in the building's label rect, one ppm that fits all) ---
    LX, LY, LW, LH = (layout["label_rect"] + [layout["label_rect"][2]])[:4] \
        if len(layout["label_rect"]) == 3 else layout["label_rect"]
    ext = {}
    for pre in label_prefixes:
        sel = np.array([part_name(int(p)).startswith(pre) for p in fpart], bool)
        if not sel.any():
            continue
        mpt = np.array([(tile_m_of(skey[i]) * tiles[skey[i]].get("repeat", 1)) if skey[i] in tiles else 1.0
                        for i in np.flatnonzero(sel)])
        uvm = np.concatenate([tuv[i] * m for i, m in zip(np.flatnonzero(sel), mpt)])   # metres
        kk = np.bincount(fmat[sel]).argmax()
        ext[pre] = (uvm.min(0), uvm.max(0), keys[int(kk)])
    gut = 8

    def _pack(ppm):
        res, cx, cy, shelf = {}, LX + gut, LY + gut, 0
        for pre, (lo, hi, kk) in ext.items():
            w_px = int(math.ceil((hi[0] - lo[0]) * ppm)) + 1
            h_px = int(math.ceil((hi[1] - lo[1]) * ppm)) + 1
            if cx + w_px > LX + LW - gut:
                cx, cy, shelf = LX + gut, cy + shelf + gut, 0
            if cy + h_px > LY + LH - gut or cx + w_px > LX + LW - gut:
                return None
            res[pre] = dict(origin=(cx, cy), lo=lo, ppm=ppm, size=(w_px, h_px), key=kk)
            cx += w_px + gut
            shelf = max(shelf, h_px)
        return res
    label_map = {}
    if ext:
        ppm = label_ppm
        while ppm > 2.0:
            label_map = _pack(ppm)
            if label_map is not None:
                break
            ppm *= 0.94
        if not label_map:
            raise RuntimeError("label rect overflow")
    # -- per-key palette affine (all faces of a flat key share one map) ----------------
    flat_aff = {}
    for pk in sorted({x for x in pkey if x is not None}):
        if pk not in pal:
            raise KeyError(f"tile_mesh: key {pk!r} is neither tiled nor in the palette")
        sel = [i for i in range(len(pkey)) if pkey[i] == pk]
        pts = np.concatenate([tuv[i] for i in sel])
        lo, hi = pts.min(0), pts.max(0)
        e = float(max(hi[0] - lo[0], hi[1] - lo[1], 1e-6))
        cx, cy, cell = pal[pk]["rect_px"]
        s = (cell * 0.5) / e                                 # texels per UVTile unit
        flat_aff[pk] = (np.array((cx + cell * 0.25, cy + cell * 0.25)) - lo * s, s)
    # -- weathering context ------------------------------------------------------------
    wctx = None
    if weather is not None:
        wctx = _WeatherCtx(Vw, tris, [keys[k] for k in fmat], fpart, part_name, layout, weather)
        for i in np.flatnonzero(is_flatp):
            wctx.wkey[i] = None
    wcount = {}
    # -- clip ----------------------------------------------------------------------------
    vreg = {}
    vpos = []

    def vid(key, p):
        i = vreg.get(key)
        if i is None:
            i = len(vpos); vreg[key] = i; vpos.append(np.asarray(p, np.float64))
        return i
    edge_pts = {}
    out_polys = []       # (poly [(vid, uvpx, rec)], ti, wflag)
    n_sliced = n_pieces = 0
    n_ground_skip = [0]
    n_band = [0]

    def tile_pieces(recs, uv3, ti, key, ground):
        lo, nx, ny = _cells_of(recs, uv3, max_cells, ti, key)
        if nx * ny == 1:
            return [(recs, (lo[0], lo[1]))], False

        def low_piece(pc):
            return len(pc) >= 3 and max(zfun(r["p"]) for r in pc) <= ground_band + 1e-4
        cuts = []
        for ax, l0, n_ in ((0, lo[0], nx), (1, lo[1], ny)):
            cs = []
            for j in range(1, n_):
                c_ = l0 + j
                if ground and (low_piece(_clip(recs, ax, c_, True)) or low_piece(_clip(recs, ax, c_, False))):
                    n_ground_skip[0] += 1
                    continue
                cs.append(c_)
            cuts.append([None] + cs + [None])
        out = []
        for ia in range(len(cuts[0]) - 1):
            strip = recs
            if cuts[0][ia] is not None:
                strip = _clip(strip, 0, cuts[0][ia], True)
            if cuts[0][ia + 1] is not None:
                strip = _clip(strip, 0, cuts[0][ia + 1], False)
            for ib in range(len(cuts[1]) - 1):
                pc = strip
                if cuts[1][ib] is not None:
                    pc = _clip(pc, 1, cuts[1][ib], True)
                if cuts[1][ib + 1] is not None:
                    pc = _clip(pc, 1, cuts[1][ib + 1], False)
                if len(pc) >= 3:
                    cen = np.mean([r["uv"] for r in pc], 0)
                    out.append((pc, (math.floor(cen[0]), math.floor(cen[1]))))
        return out, True

    for ti in range(len(tris)):
        vs = [int(x) for x in tris[ti]]
        k = int(fmat[ti]); key = keys[k]; sk = skey[ti]
        uv3 = tuv[ti]
        nvs = len(vs)
        recs = []
        for vi, v in enumerate(vs):
            ons = {tuple(sorted((vs[vi - 1], v))), tuple(sorted((v, vs[(vi + 1) % nvs])))}
            recs.append({"uv": uv3[vi].copy(), "p": V[v].copy(), "on": frozenset(ons),
                         "key": ("o", v), "vert": v})
        pieces = []
        if sk in tiles and not is_label[ti] and not is_flatp[ti]:
            ground = any(part_name(int(fpart[ti])).startswith(g) for g in ground_prefixes)
            pieces, sliced = tile_pieces(recs, uv3, ti, key, ground)
            n_sliced += int(sliced)
            if wctx is not None:
                for c in wctx.lines(ti):
                    nxt = []
                    for pc, cell in pieces:
                        for keep in (False, True):
                            q = _clip_z(pc, zfun, c, keep)
                            if len(q) >= 3:
                                nxt.append((q, cell))
                    if len(nxt) > len(pieces):
                        n_band[0] += 1
                    pieces = nxt
        else:
            pieces.append((recs, None))
        for pc, cell in pieces:
            wflag = 0
            rect = tiles.get(sk, {}).get("rect_px")
            if cell is not None and wctx is not None and wctx.wkey[ti] is not None:
                cpt = np.mean([r["p"] for r in pc], 0)
                rule = wctx.classify(ti, zfun(cpt), cpt @ Mw[:3, :3].T + Mw[:3, 3])
                if rule is not None:
                    wflag = 1
                    rect = tiles[wctx.wkey[ti]]["rect_px"]
                    wcount[rule] = wcount.get(rule, 0) + 1
            poly = []
            for r in pc:
                if r.get("key") is None:
                    common = r["on"]
                    if common:
                        ea, eb = sorted(common)[0]
                        P, Q, t = r["_pq"]
                        tp = _t_on(P, ea, eb, V); tq = _t_on(Q, ea, eb, V)
                        tt = tp + (tq - tp) * t
                        r["key"] = ("e", ea, eb, round(tt, 7))
                        r["edge_t"] = ((ea, eb), tt)
                        r["p"] = V[ea] + (V[eb] - V[ea]) * tt
                    else:
                        r["key"] = ("i", ti, round(float(r["uv"][0]), 7), round(float(r["uv"][1]), 7))
                vi_ = vid(r["key"], r["p"])
                if is_label[ti]:
                    pre = next(p for p in label_map if part_name(int(fpart[ti])).startswith(p))
                    L = label_map[pre]
                    mpt = (tile_m_of(sk) * tiles[sk].get("repeat", 1)) if sk in tiles else 1.0
                    uvpx = np.asarray(L["origin"]) + (r["uv"] * mpt - L["lo"]) * L["ppm"]
                elif cell is not None:
                    fr = np.clip(r["uv"] - np.asarray(cell), 0.0, 1.0)
                    uvpx = np.array((rect[0], rect[1])) + fr * rect[2]
                else:
                    off, s = flat_aff[pkey[ti]]
                    uvpx = off + r["uv"] * s
                poly.append((vi_, uvpx, r))
            P3 = np.array([vpos[q[0]] for q in poly])
            if len(poly) < 3 or _poly_area3(P3) < 1e-9:
                continue
            out_polys.append((poly, ti, wflag))
            n_pieces += 1
            for vi_, _uv, r in poly:
                if r["key"][0] == "e":
                    (ea, eb), tt = r["edge_t"]
                    edge_pts.setdefault((ea, eb), {})[round(tt, 7)] = vi_
    # -- decal strips (sill drips, door thresholds): separate quads, never welded ---------
    strips = []
    if wctx is not None:
        strips = _plan_strips(wctx, Vw, tris, fpart, part_name, weather, ground_band)
        Minv = np.linalg.inv(Mw)
        for sid, (Pw4, ti_h, rule) in enumerate(strips):
            Ph = Vw[list(tris[ti_h])]
            aff = _affine_tile(Ph, tuv[ti_h])
            if aff is None:
                continue
            p0, u0, Aff = aff
            Ul = np.array([u0 + Aff @ (q - p0) for q in Pw4])
            Pl = (np.c_[Pw4, np.ones(len(Pw4))] @ Minv.T)[:, :3]
            recs = [{"uv": Ul[j].copy(), "p": Pl[j].copy(), "on": frozenset(), "key": ("s", sid, j)}
                    for j in range(len(Pw4))]
            pcs, _sl = tile_pieces(recs, Ul, ti_h, keys[int(fmat[ti_h])], False)
            rect = tiles[wctx.wkey[ti_h]]["rect_px"]
            for pc, cell in pcs:
                poly = []
                for r in pc:
                    if r.get("key") is None:
                        r["key"] = ("s", sid, round(float(r["uv"][0]), 7), round(float(r["uv"][1]), 7))
                    fr = np.clip(r["uv"] - np.asarray(cell), 0.0, 1.0)
                    poly.append((vid(r["key"], r["p"]), np.array((rect[0], rect[1])) + fr * rect[2], r))
                P3 = np.array([vpos[q[0]] for q in poly])
                if len(poly) < 3 or _poly_area3(P3) < 1e-9:
                    continue
                out_polys.append((poly, ti_h, 2))
            wcount[rule] = wcount.get(rule, 0) + 1
    # -- T-junctions: insert every split point of an original edge into every polygon
    #    edge lying on that original edge ----------------------------------------------
    for (ea, eb), d in edge_pts.items():
        d[0.0] = vreg[("o", ea)] if ("o", ea) in vreg else None
        d[1.0] = vreg[("o", eb)] if ("o", eb) in vreg else None
    n_ins = 0
    n_tj_skip = 0
    final = []
    for poly, ti, wflag in out_polys:
        if is_label[ti] or wflag == 2:
            final.append(([(q[0], q[1]) for q in poly], ti, wflag))
            continue
        newp = []
        gpoly = any(part_name(int(fpart[ti])).startswith(g) for g in ground_prefixes)
        n = len(poly)
        for i in range(n):
            vi_, uvp, r = poly[i]
            vj, uvq, s = poly[(i + 1) % n]
            newp.append((vi_, uvp))
            ce = _edge_common(r, s)
            if ce is None:
                continue
            tp = _t_of(r, ce, V); tq = _t_of(s, ce, V)
            pts = edge_pts.get(ce)
            if not pts:
                continue
            lo_, hi_ = min(tp, tq), max(tp, tq)
            mids = sorted((t, v) for t, v in pts.items() if v is not None and lo_ + 1e-6 < t < hi_ - 1e-6
                          and v != vi_ and v != vj)
            if tq < tp:
                mids = mids[::-1]
            zw = lambda q: zfun(vpos[q])
            on_ground_edge = max(abs(zw(vi_)), abs(zw(vj))) <= 1e-5
            for t, v in mids:
                if gpoly and not on_ground_edge and zw(v) <= ground_band + 1e-4:
                    n_tj_skip += 1
                    continue
                f = (t - tp) / (tq - tp)
                newp.append((v, uvp + (uvq - uvp) * f))
                n_ins += 1
        final.append((newp, ti, wflag))
    # -- build the new mesh -------------------------------------------------------------
    new = bpy.data.meshes.new(me.name + "_tiled")
    nv = len(vpos)
    new.vertices.add(nv)
    new.vertices.foreach_set("co", np.array(vpos, np.float32).ravel())
    loop_tot = np.array([len(p) for p, _t, _w in final], np.int32)
    loop_start = np.concatenate([[0], np.cumsum(loop_tot)[:-1]]).astype(np.int32)
    lv = np.array([q[0] for p, _t, _w in final for q in p], np.int32)
    luv = np.array([q[1] for p, _t, _w in final for q in p], np.float64) / A
    new.loops.add(len(lv))
    new.loops.foreach_set("vertex_index", lv)
    new.polygons.add(len(final))
    new.polygons.foreach_set("loop_start", loop_start)
    new.polygons.foreach_set("loop_total", loop_tot)
    src = np.array([ti for _p, ti, _w in final], np.int64)
    wfl = np.array([w for _p, _t, w in final], np.int32)
    new.update(calc_edges=True)
    lay = new.uv_layers.new(name=cs2_uv.UNIQUE_LAYER)
    cs2_uv.set_loop_uvs(lay, luv)
    for name_, (dt, arr) in fattr.items():
        at = new.attributes.get(name_) or new.attributes.new(name_, dt, "FACE")
        at.data.foreach_set("value", arr[src])
    km = new.attributes.new("cs2ts_mat", "INT", "FACE")
    km.data.foreach_set("value", fmat[src].astype(np.int32))
    kw = new.attributes.new("cs2ts_w", "INT", "FACE")
    kw.data.foreach_set("value", wfl)
    for k_, v_ in me.items():
        new[k_] = v_
    old = obj.data
    obj.data = new
    bpy.data.meshes.remove(old)
    new.name = obj.name
    # triangulate (collinear T-junction points make n-gons; the export triangulates anyway)
    bm = bmesh.new(); bm.from_mesh(new)
    lab_l = bm.faces.layers.int.get("part")
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if not (lab_l is not None and any(
        part_name(f[lab_l]).startswith(pre) for pre in label_prefixes))],
        quad_method="BEAUTY", ngon_method="BEAUTY")
    deg = [f for f in bm.faces if f.calc_area() < 1e-10]
    if deg:
        bmesh.ops.delete(bm, geom=deg, context="FACES")
    bm.to_mesh(new); bm.free()
    new.update()
    cs2_asset.shade_by_angle(obj, 66.0, weighted=True)
    wn = np.zeros(len(new.polygons), np.int32)
    new.attributes["cs2ts_w"].data.foreach_get("value", wn)
    return {"tris_before": int(n_tris0), "tris_after": int(len(new.polygons)), "sliced_tris": int(n_sliced),
            "pieces": int(n_pieces), "tjunction_inserts": int(n_ins), "verts": int(len(new.vertices)),
            "label_rects": {p: {"origin_px": [int(v) for v in L["origin"]], "size_px": list(L["size"]),
                                "ppm": round(float(L["ppm"]), 1), "key": L["key"]} for p, L in label_map.items()},
            "label_rect": [int(LX), int(LY), int(LW), int(LH)],
            "degenerate_removed": len(deg), "ground_cuts_skipped": n_ground_skip[0],
            "ground_tjunctions_left": n_tj_skip, "patch_shift": n_shift,
            "flat_detail_tris": int(is_flatp.sum()), "flat_prefixes": list(flat_prefixes),
            "weather": None if weather is None else {
                "level": int(weather.level), "band_cut_tris": int(n_band[0]), "pieces_by_rule": wcount,
                "strips": len(strips), "tris_weathered": int((wn == 1).sum()), "tris_strips": int((wn == 2).sum())}}


def _plan_strips(wctx, Vw, tris, fpart, part_name, R, ground_band):
    """[(world quad (4,3) CCW seen from outside, host tri index, rule)] for sill drips and
    door thresholds. A strip is kept only when every corner has its host plane right behind
    (below) it, so it never overhangs an edge."""
    from mathutils import Vector
    bvh = wctx.bvh
    out = []
    by_part = {}
    for i, p in enumerate(fpart):
        by_part.setdefault(int(p), []).append(i)

    def pts_of(idx):
        return np.concatenate([Vw[list(tris[i])] for i in idx])

    def frame(P):
        H = P[:, :2] - P[:, :2].mean(0)
        C = np.cov(H.T) if len(H) > 2 else np.eye(2)
        w, v = np.linalg.eigh(C)
        L = np.array((v[0, 1], v[1, 1], 0.0)); L /= np.linalg.norm(L)
        T = np.array((-L[1], L[0], 0.0))
        return L, T

    def host_at(q, d, maxd, want):
        loc, nrm, idx, dist = bvh.ray_cast(Vector(tuple(q)), Vector(tuple(d)), maxd)
        if loc is None or idx is None or not want(idx):
            return None
        return np.array(loc), idx

    if R.sill_drips:
        for p, idx in sorted(by_part.items()):
            nm = part_name(p)
            if not nm.startswith("sill_"):
                continue
            P = pts_of(idx)
            L, T = frame(P)
            c = P.mean(0)
            hl = 0.5 * float(np.ptp(P @ L))
            zb = float(P[:, 2].min())
            if zb - R.drip_h < 0.3 or hl < 0.25:
                continue
            want = lambda j: wctx.wall[j] and wctx.wkey[j] is not None
            hit = None
            for sgn in (1, -1):
                q = c.copy(); q[2] = zb - 0.10
                h = host_at(q, T * sgn, 0.6, want)
                if h is not None and (hit is None or np.linalg.norm(h[0] - q) < np.linalg.norm(hit[0] - q)):
                    hit = (h[0], h[1], sgn)
            if hit is None:
                continue
            loc, ti_h, sgn = hit
            nrm_h = wctx.n[ti_h]
            out_dir = -T * sgn
            if float(np.dot(nrm_h, out_dir)) < 0.9:
                continue
            a = hl - 0.06
            w0 = loc + out_dir * R.strip_offset
            z0, z1 = zb - R.drip_h, zb - 0.005
            corners = [w0 - L * a, w0 + L * a]
            quad = [np.array((c_[0], c_[1], z)) for c_, z in ((corners[0], z0), (corners[1], z0),
                                                              (corners[1], z1), (corners[0], z1))]
            ok = True
            for q in quad:
                h = host_at(q, -out_dir, 0.06, lambda j: abs(float(np.dot(wctx.n[j], nrm_h))) > 0.99)
                if h is None:
                    ok = False
                    break
            if not ok:
                continue
            quad = np.array(quad)
            if float(np.dot(np.cross(quad[1] - quad[0], quad[2] - quad[0]), out_dir)) < 0:
                quad = quad[::-1]
            out.append((quad, ti_h, "sill_drip"))
    if R.thresholds:
        doors = [(p, idx) for p, idx in sorted(by_part.items()) if part_name(p).startswith("door_")]
        groups = []
        for p, idx in doors:
            P = pts_of(idx)
            lo, hi = P.min(0), P.max(0)
            for g in groups:
                if np.all(lo - 0.3 <= g[1]) and np.all(hi + 0.3 >= g[0]):
                    g[0] = np.minimum(g[0], lo); g[1] = np.maximum(g[1], hi); g[2].append(P)
                    break
            else:
                groups.append([lo, hi, [P]])
        floor_ok = lambda j: (wctx.n[j, 2] > 0.8 and wctx.wkey[j] is not None
                              and W_CLASS[base_key(wctx.wkey[j])] in ("deck", "concrete", "paver", "stone"))
        for lo, hi, Ps in groups:
            P = np.concatenate(Ps)
            L, T = frame(P)
            c = P.mean(0)
            hl = 0.5 * float(np.ptp(P @ L)) + 0.15
            zb = float(P[:, 2].min())
            found = []
            for sgn in (1, -1):
                q = c + T * sgn * 0.6; q[2] = zb + 0.3
                h = host_at(q, np.array((0, 0, -1.0)), 0.7, floor_ok)
                if h is not None and abs(h[0][2] - zb) < 0.35:
                    found.append((sgn, h))
            if len(found) != 1:
                continue
            sgn, (loc, ti_h) = found[0]
            zf = max(float(loc[2]) + R.strip_offset, ground_band + 0.014)
            cT = float(np.dot(c, T))
            base_pt = c - T * (np.dot(c, T) - cT)
            depth = R.threshold_d
            for _try in range(2):
                q0 = base_pt + T * sgn * 0.05
                q1 = base_pt + T * sgn * (0.05 + depth)
                quad = [q0 - L * hl, q0 + L * hl, q1 + L * hl, q1 - L * hl]
                quad = [np.array((q[0], q[1], zf)) for q in quad]
                ok = all(host_at(np.array((q[0], q[1], zf + 0.02)), np.array((0, 0, -1.0)), 0.2,
                                 lambda j: wctx.n[j, 2] > 0.8) is not None for q in quad)
                if ok:
                    break
                depth = 0.55
            if not ok:
                continue
            quad = np.array(quad)
            if np.cross(quad[1] - quad[0], quad[2] - quad[0])[2] < 0:
                quad = quad[::-1]
            out.append((quad, ti_h, "threshold"))
    return out


def _poly_area3(P):
    s = np.zeros(3)
    for i in range(1, len(P) - 1):
        s += np.cross(P[i] - P[0], P[i + 1] - P[0])
    return 0.5 * float(np.linalg.norm(s))


def _t_on(rec, ea, eb, V):
    """parameter of a record along original edge (ea, eb): 0 at ea, 1 at eb."""
    k = rec["key"]
    if k is not None and k[0] == "o":
        return 0.0 if k[1] == ea else 1.0
    if rec.get("edge_t") is not None:
        return rec["edge_t"][1]
    d = V[eb] - V[ea]
    return float(np.dot(rec["p"] - V[ea], d) / max(np.dot(d, d), 1e-18))


def _edge_common(r, s):
    c = r["on"] & s["on"]
    if not c:
        return None
    return sorted(c)[0]


def _t_of(rec, edge, V):
    return _t_on(rec, edge[0], edge[1], V)


# ======================================================================================
# Measurement
# ======================================================================================

def _wq(x, w):
    o = np.argsort(x); xs, ws = x[o], w[o]; c = np.cumsum(ws) / ws.sum()
    return lambda f: float(xs[min(np.searchsorted(c, f), len(xs) - 1)])


def face_ppm(obj, A, keys, mat_attr="cs2ts_mat", uv_layer=None, mat_index=None, split_weather=False):
    """{key: {p10, p50, p90, area_m2, n}} shipped px/m per material on `uv_layer`. With
    split_weather the weathered pieces / strips report under "<key>~w"."""
    me = obj.data
    lay = me.uv_layers[uv_layer] if uv_layer else (me.uv_layers.get(cs2_uv.UNIQUE_LAYER) or me.uv_layers.active)
    uv = cs2_uv.loop_uvs(lay)
    co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3).astype(np.float64)
    M = np.array(obj.matrix_world)
    co = co @ M[:3, :3].T
    if mat_index is None:
        mat_index = np.zeros(len(me.polygons), np.int32)
        me.attributes[mat_attr].data.foreach_get("value", mat_index)
    wfl = np.zeros(len(me.polygons), np.int32)
    if split_weather and me.attributes.get("cs2ts_w") is not None:
        me.attributes["cs2ts_w"].data.foreach_get("value", wfl)
    out = {}
    ppm = np.zeros(len(me.polygons)); area = np.zeros(len(me.polygons))
    for p in me.polygons:
        vs = [me.loops[i].vertex_index for i in range(p.loop_start, p.loop_start + p.loop_total)]
        ls = list(range(p.loop_start, p.loop_start + p.loop_total))
        a3 = 0.0; au = 0.0
        for i in range(1, len(vs) - 1):
            a3 += 0.5 * np.linalg.norm(np.cross(co[vs[i]] - co[vs[0]], co[vs[i + 1]] - co[vs[0]]))
            d1 = uv[ls[i]] - uv[ls[0]]; d2 = uv[ls[i + 1]] - uv[ls[0]]
            au += 0.5 * abs(d1[0] * d2[1] - d1[1] * d2[0])
        area[p.index] = a3
        ppm[p.index] = A * math.sqrt(au / a3) if a3 > 1e-9 else 0.0
    for k in sorted(set(int(x) for x in mat_index)):
        for wsel, suf in ((wfl == 0, ""), (wfl > 0, W_SUFFIX)):
            sel = (mat_index == k) & (area > 1e-9) & wsel
            if not sel.any():
                continue
            q = _wq(ppm[sel], area[sel])
            out[keys[k] + suf] = {"p10": round(q(0.1), 1), "p50": round(q(0.5), 1), "p90": round(q(0.9), 1),
                                  "area_m2": round(float(area[sel].sum()), 1), "faces": int(sel.sum())}
    return out


def bc7_mb(A, maps=4):
    """Raw BC7 with a full mip chain: 1 byte / texel x 4/3."""
    return round(A * A * 4.0 / 3.0 * maps / 1e6, 2)


def mask_stats(obj, maskmap_path, keys, mat_attr="cs2ts_mat", mat_index=None, uv_layer=None):
    """{key: {A_mean, A_p10, A_p50, A_p90, metal_mean, area_m2}}: the shipped MaskMap sampled at
    each triangle's centroid UV (3D-area weighted, so overlapped UVs count per use)."""
    me = obj.data
    img = K._load_raw(maskmap_path)
    H, W = img.shape[:2]
    lay = me.uv_layers[uv_layer] if uv_layer else (me.uv_layers.get(cs2_uv.UNIQUE_LAYER) or me.uv_layers.active)
    uv = cs2_uv.loop_uvs(lay)
    co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3).astype(np.float64)
    if mat_index is None:
        mat_index = np.zeros(len(me.polygons), np.int32)
        me.attributes[mat_attr].data.foreach_get("value", mat_index)
    acc = {}
    for p in me.polygons:
        ls = list(range(p.loop_start, p.loop_start + p.loop_total))
        vs = [me.loops[i].vertex_index for i in ls]
        for i in range(1, len(vs) - 1):
            a3 = 0.5 * np.linalg.norm(np.cross(co[vs[i]] - co[vs[0]], co[vs[i + 1]] - co[vs[0]]))
            if a3 <= 1e-9:
                continue
            c = (uv[ls[0]] + uv[ls[i]] + uv[ls[i + 1]]) / 3.0
            x = int(np.clip(c[0] * W, 0, W - 1)); y = int(np.clip(c[1] * H, 0, H - 1))
            acc.setdefault(int(mat_index[p.index]), []).append((img[y, x, 3], img[y, x, 0], a3))
    out = {}
    for k, rows in sorted(acc.items()):
        r = np.array(rows)
        a, m, w = r[:, 0], r[:, 1], r[:, 2]
        q = _wq(a, w)
        out[keys[k]] = {"A_mean": round(float((a * w).sum() / w.sum()), 3), "A_p10": round(q(0.1), 3),
                        "A_p50": round(q(0.5), 3), "A_p90": round(q(0.9), 3),
                        "metal_mean": round(float((m * w).sum() / w.sum()), 3),
                        "area_m2": round(float(w.sum()), 1)}
    return out


def weather_stats(obj, keys, mat_attr="cs2ts_mat"):
    """{key: {area_m2, weathered_m2, strip_m2, share}} of the shipped LOD0."""
    me = obj.data
    mi = np.zeros(len(me.polygons), np.int32); me.attributes[mat_attr].data.foreach_get("value", mi)
    wf = np.zeros(len(me.polygons), np.int32); me.attributes["cs2ts_w"].data.foreach_get("value", wf)
    ar = np.zeros(len(me.polygons)); me.polygons.foreach_get("area", ar)
    out = {}
    for k in sorted(set(mi.tolist())):
        s = mi == k
        tot = float(ar[s & (wf != 2)].sum())
        w1 = float(ar[s & (wf == 1)].sum()); w2 = float(ar[s & (wf == 2)].sum())
        if w1 + w2 <= 0:
            continue
        out[keys[k]] = {"area_m2": round(tot, 1), "weathered_m2": round(w1, 1), "strip_m2": round(w2, 1),
                        "share": round(w1 / max(tot, 1e-9), 3)}
    return out


def fill_label_backgrounds(layout, label_rects, margin=6):
    """PROBE layout: give every label rect its host material's mean look before the painter
    runs (a recolour slot keeps its ControlMask, so a painted field still takes the tint)."""
    mp = layout["maps"]
    for pre, L in label_rects.items():
        key = L["key"]
        if key in layout["tiles"]:
            x0, y0, C = layout["tiles"][key]["rect_px"]
            sl = (slice(y0, y0 + C), slice(x0, x0 + C))
        else:
            cx, cy, cell = layout["palette"][key]["rect_px"]
            sl = (slice(cy + 4, cy + cell - 4), slice(cx + 4, cx + cell - 4))
        (ox, oy), (w, h) = L["origin_px"], L["size_px"]
        r = (slice(oy - margin, oy + h + margin), slice(ox - margin, ox + w + margin))
        mp["base"][r[0], r[1], :3] = mp["base"][sl[0], sl[1], :3].reshape(-1, 3).mean(0)
        mp["msk"][r[0], r[1]] = mp["msk"][sl[0], sl[1]].reshape(-1, 4).mean(0)
        mp["ctl"][r[0], r[1]] = np.round(mp["ctl"][sl[0], sl[1]].reshape(-1, 4).mean(0))
        mp["nrm"][r[0], r[1]] = (0.5, 0.5, 1.0)


def check_uv_cells(obj, layout, keys, mat_attr="cs2ts_mat", tol_px=0.51):
    """Every loop of a face must sample inside its key's region (clean or weathered tile
    content square, palette cell or the label rect). Returns {key: n faces outside}."""
    me = obj.data
    A = layout["A"]
    uv = cs2_uv.loop_uvs(me.uv_layers[cs2_uv.UNIQUE_LAYER]) * A
    mi = np.zeros(len(me.polygons), np.int32)
    me.attributes[mat_attr].data.foreach_get("value", mi)
    lr = layout["label_rect"]
    LX, LY = lr[0], lr[1]
    LW = lr[2]; LH = lr[3] if len(lr) > 3 else lr[2]
    alias = layout.get("alias", {})
    bad = {}
    for p in me.polygons:
        key = keys[int(mi[p.index])]
        sk = alias.get(key, key)
        q = uv[p.loop_start:p.loop_start + p.loop_total]
        rects = []
        for kk in (sk, sk + W_SUFFIX):
            if kk in layout["tiles"]:
                x0, y0, C = layout["tiles"][kk]["rect_px"]; rects.append((x0, y0, C, C))
        for kk in (key, sk + F_SUFFIX):
            if kk in layout["palette"]:
                x, y, s = layout["palette"][kk]["rect_px"]; rects.append((x, y, s, s))
        rects.append((LX, LY, LW, LH))
        ok = any(((q[:, 0] >= x - tol_px) & (q[:, 0] <= x + w + tol_px) & (q[:, 1] >= y - tol_px)
                  & (q[:, 1] <= y + h + tol_px)).all() for x, y, w, h in rects)
        if not ok:
            bad[key] = bad.get(key, 0) + 1
    return bad


# ======================================================================================
# CLI: compose the family sheet (base swatches + every building's label rect)
#   blender -b --factory-startup --python cs2_tilesheet.py -- compose <sheet_dir> <name>
#       [--A 4096] [--layout rollout] [--repeats key=r,...] [--no-emissive]
# ======================================================================================

def _cli(argv):
    import argparse
    ap = argparse.ArgumentParser(prog="cs2_tilesheet.py")
    ap.add_argument("cmd", choices=("compose",))
    ap.add_argument("sheet_dir")
    ap.add_argument("name")
    ap.add_argument("--A", type=int, default=SHEET_A)
    ap.add_argument("--layout", default="rollout")
    ap.add_argument("--repeats", default="")
    ap.add_argument("--no-emissive", action="store_true")
    a = ap.parse_args(argv)
    reps = {kv.split("=")[0]: int(kv.split("=")[1]) for kv in a.repeats.split(",") if kv}
    lay = build_sheet(a.sheet_dir, a.name, A=a.A, layout=a.layout, repeats=reps)
    ldir = os.path.join(a.sheet_dir + ".aux", "labels")
    labels = sorted(os.path.join(ldir, f) for f in os.listdir(ldir) if f.endswith(".npz")) \
        if os.path.isdir(ldir) else []
    files = write_sheet(lay, emissive=not a.no_emissive, labels=labels)
    os.makedirs(a.sheet_dir + ".aux", exist_ok=True)
    save_layout(lay, os.path.join(a.sheet_dir + ".aux", f"{a.name}.layout.json"))
    print(f"[tilesheet] composed {a.name} ({a.layout}, {a.A}, hash {lay['hash']}) with {len(labels)} "
          f"label rect(s): {files}")


if __name__ == "__main__":
    try:
        _cli(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    except Exception:  # noqa: BLE001
        import traceback
        traceback.print_exc()
        sys.exit(1)
