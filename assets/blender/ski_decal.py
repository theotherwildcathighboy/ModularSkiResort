# ski_decal.py - Layer-2 "alpha-coverage" DECAL texture sets for the RenderedArea run
# decals (research: docs/research/run-rendering-synthesis.md "Layer 2" +
# docs/research/area-material-override.md Tier 0 / property inventory / verdict).
#
# Unlike the SkiTerrain* TERRAIN pairs (untouched by this script - the snow-slot code
# reuses SkiTerrainSnowCorduroy_* as-is), these are DECAL textures for the
# AreaDecalShader: BaseColor ALPHA is per-texel decal coverage (official: "The alpha
# channel of the BaseColor texture is used to control surface transparency, on a
# per-pixel basis"). Terrain albedo/relief/shading must ghost through, and the color must
# read sensibly over BOTH summer grass and winter snow - the game's snow sim (plus the
# Layer-1 injected snow) supplies winter white; these decals are the year-round
# grooming/wear layer, NOT white paint.
#
# ALPHA DESIGN NOTE: the texture TILES across the run polygon, so there is no "tile edge"
# to feather - a border vignette would render as a repeating grid of holes. Coverage is
# therefore built from multi-octave periodic noise EVERYWHERE: ~55-70% mean opacity
# (groomed) / ~70-80% (snowmaking) with patchy thin spots fading toward 0. Feathering at
# the actual polygon boundary is the shader's own job (colossal_EdgeFadeRange /
# colossal_EdgeNoise - already good, per the research doc).
#
# With the albedo mostly transparent, the NORMAL carries the grooming signature: corduroy
# at the same 16.7 cm pitch / V-alignment as ski_terrain.py (48 grooves per 8 m tile,
# same sine + wobble + depth-modulation math), noticeably STRONGER than the terrain pair
# (intended to run with m_NormalAlphaSource = 1, i.e. full-strength normals independent
# of albedo coverage), plus subtle along-V ski-track streaks. The snowmaking set adds
# faint mogul-scale (3-5 m) undulation.
#
# Outputs (file contract - the runtime material code loads these EXACT names):
#   mod/SkiResortMod/Textures/SkiRunDecalGroomed_BaseColor.png     sRGB + coverage alpha
#   mod/SkiResortMod/Textures/SkiRunDecalGroomed_Normal.png        OpenGL tangent, linear
#   mod/SkiResortMod/Textures/SkiRunDecalSnowmaking_BaseColor.png  sRGB + coverage alpha
#   mod/SkiResortMod/Textures/SkiRunDecalSnowmaking_Normal.png     OpenGL tangent, linear
# Previews (numpy compositing only - NO Cycles, per machine rule):
#   assets/preview/terrain/<Name>_tiling2x2.png    2x2 repeat over neutral gray
#   assets/preview/terrain/<Name>_closeup.png      center 4 m over neutral gray
#   assets/preview/terrain/<Name>_overGrass.png    alpha-blend over summer grass, 2x2
#   assets/preview/terrain/<Name>_overSnow.png     alpha-blend over winter snow, 2x2
# The over-context comps composite the decal the way the shader will: albedo lerped by
# BaseColor alpha, decal normal at FULL strength regardless of albedo alpha (the
# m_NormalAlphaSource = 1 configuration), blended into the background's own terrain
# relief by slope addition (DECAL_SURFACE_GRADIENT semantics: decal normals blend into
# the receiver's normal, they do not replace it), then numpy-Lambert relit with
# ski_terrain.py's raking light.
#
# Pattern primitives are lifted from ski_terrain.py (which runs main() on import and
# therefore cannot be imported), extended with an anisotropic streak-noise variant.
#
# Usage:
#   blender --background --factory-startup --python ski_decal.py
#
# Deterministic: fixed seeds, no wall-clock or GPU dependence.

import os
import sys

import bpy  # noqa: F401  (headless blender)
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cs2_asset import _write_png  # the proven display-encoded PNG writer

# --------------------------------------------------------------------------------------

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
TEX_DIR = os.path.join(ROOT, "mod", "SkiResortMod", "Textures")
PREVIEW_DIR = os.path.join(ROOT, "assets", "preview", "terrain")

SIZE = 1024
TILE_METERS = 8.0            # world coverage of one tile (same convention as ski_terrain)
GROOVES = 48                 # corduroy ridge lines per tile: 8 m / 0.167 m pitch

SEED_GROOMED = 20260813
SEED_SNOWMAKING = 20260814
SEED_BACKDROP = 20260815     # preview backgrounds only - never shipped

# Coverage targets (mean BaseColor alpha), from the task spec / research doc S3 Tier 0.
GROOMED_COVERAGE = 0.62      # ~55-70% interior opacity
SNOWMAKING_COVERAGE = 0.76   # ~70-80%, snowmaking wets/whitens + covers more


# --------------------------------------------------------------------------------------
# Pattern primitives (from ski_terrain.py - periodic by construction)
# --------------------------------------------------------------------------------------

def periodic_noise(size, f_lo, f_hi, falloff, seed):
    """Tileable scalar noise in -1..1 via random-phase spectrum between integer
    frequencies f_lo..f_hi with 1/f^falloff amplitude."""
    rng = np.random.default_rng(seed)
    f1 = np.fft.fftfreq(size) * size
    fx, fy = np.meshgrid(f1, f1)
    f = np.sqrt(fx * fx + fy * fy)
    mask = (f >= f_lo) & (f <= f_hi)
    amp = np.where(mask, 1.0 / np.maximum(f, 1.0) ** falloff, 0.0)
    phase = rng.uniform(0.0, 2.0 * np.pi, (size, size))
    n = np.real(np.fft.ifft2(amp * np.exp(1j * phase)))
    peak = np.abs(n).max()
    return n / peak if peak > 1e-12 else n


def periodic_noise_aniso(size, fu_lo, fu_hi, fv_max, falloff, seed):
    """Tileable STREAK noise: spectral energy at U frequencies fu_lo..fu_hi but only low
    V frequencies (|fv| <= fv_max) -> long coherent streaks running along V (image
    vertical) - ski/board track texture aligned with the corduroy/fall-line axis."""
    rng = np.random.default_rng(seed)
    f1 = np.fft.fftfreq(size) * size
    fu, fv = np.meshgrid(f1, f1)          # fu varies along axis 1 (U), fv along axis 0
    au = np.abs(fu)
    mask = (au >= fu_lo) & (au <= fu_hi) & (np.abs(fv) <= fv_max)
    amp = np.where(mask, 1.0 / np.maximum(au, 1.0) ** falloff, 0.0)
    phase = rng.uniform(0.0, 2.0 * np.pi, (size, size))
    n = np.real(np.fft.ifft2(amp * np.exp(1j * phase)))
    peak = np.abs(n).max()
    return n / peak if peak > 1e-12 else n


def normal_from_height(h, strength):
    """OpenGL tangent-space normal (array row 0 = image top; G = +up in image space)."""
    size = h.shape[0]
    ddu = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5 * size
    ddown = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5 * size
    dup = -ddown                                   # derivative along image-up
    nx = -ddu * strength
    ny = -dup * strength
    nz = np.ones_like(h)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    rgba = np.empty((size, size, 4), dtype=np.float32)
    rgba[..., 0] = nx * inv * 0.5 + 0.5
    rgba[..., 1] = ny * inv * 0.5 + 0.5
    rgba[..., 2] = nz * inv * 0.5 + 0.5
    rgba[..., 3] = 1.0
    return rgba


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# --------------------------------------------------------------------------------------
# Shared construction: corduroy groove field + coverage alpha field
# --------------------------------------------------------------------------------------

def corduroy_groove(seed):
    """ski_terrain.py's corduroy math: integer-frequency sine along U (ridges run along
    V = fall line), phase-wobbled and depth-modulated so it reads groomed, not printed."""
    u = (np.arange(SIZE) + 0.5) / SIZE
    X, _Y = np.meshgrid(u, u)
    wobble = periodic_noise(SIZE, 2, 7, 1.2, seed + 1) * 1.4        # groove waviness (rad)
    depth_mod = 0.70 + 0.30 * periodic_noise(SIZE, 2, 9, 1.0, seed + 2)
    return np.sin(2.0 * np.pi * GROOVES * X + wobble) * depth_mod


def coverage_alpha(seed, base_cov, amp, ridge, worn_edges, worn_floor):
    """Per-texel coverage: multi-octave periodic noise everywhere (NO vignette - the tile
    repeats, so patchiness must be stationary), with 'worn' thin spots where the broad
    field dips low, fading toward 0. `ridge` (0..1 groove crests) adds a touch more
    coverage along the groomed lines, per the research doc's 'higher along groomed
    lines, lower between'. Returns (alpha, field, worn) so color can correlate."""
    broad = periodic_noise(SIZE, 2, 6, 1.10, seed + 10)             # 1.3-4 m patches
    mid = periodic_noise(SIZE, 7, 26, 1.00, seed + 11)              # 0.3-1.1 m detail
    fine = periodic_noise(SIZE, 40, 200, 0.85, seed + 12)           # 4-20 cm speckle
    field = 0.60 * broad + 0.30 * mid + 0.10 * fine
    field /= np.abs(field).max()
    worn = worn_floor + (1.0 - worn_floor) * smoothstep(worn_edges[0], worn_edges[1], field)
    alpha = (base_cov + amp * field + 0.055 * ridge) * worn
    return np.clip(alpha, 0.0, 0.95).astype(np.float32), field, worn


# --------------------------------------------------------------------------------------
# Set 1: groomed run - year-round trampled-snow/dirt-track wear layer
# --------------------------------------------------------------------------------------

def build_groomed(seed):
    """Returns (base_rgba, normal_rgba). BaseColor alpha = coverage (~55-70% mean)."""
    groove = corduroy_groove(seed)
    ridge = np.clip(groove, 0.0, 1.0)

    alpha, field, worn = coverage_alpha(
        seed, base_cov=GROOMED_COVERAGE, amp=0.30, ridge=ridge,
        worn_edges=(-0.80, -0.34), worn_floor=0.12)

    # Color: desaturated trampled-snow / dirt-track blend (mean RGB ~0.5-0.6). Whiter
    # where coverage is dense (packed corduroy), beiger in normal wear, dirt-toned in
    # the worn thin patches. Reads as faint groomed-path wear over grass AND snow.
    white = np.array([0.640, 0.630, 0.615])          # trampled snow, slightly warm gray
    beige = np.array([0.545, 0.515, 0.465])          # packed dirt-track beige
    dirt = np.array([0.430, 0.390, 0.340])           # worn-through earth

    t = smoothstep(0.30, 0.85, alpha)
    rgb = beige[None, None, :] + (white - beige)[None, None, :] * t[..., None]
    dirt_w = (1.0 - smoothstep(-0.80, -0.30, field)) * 0.60
    rgb = rgb * (1.0 - dirt_w[..., None]) + dirt[None, None, :] * dirt_w[..., None]

    # Groove troughs a touch darker (packed shadowed snow), plus luminance patchiness.
    trough = np.clip(-groove, 0.0, 1.0)
    rgb -= 0.028 * trough[..., None] * np.array([1.00, 0.92, 0.78])[None, None, :]
    rgb += (0.020 * periodic_noise(SIZE, 80, 400, 0.8, seed + 20)
            + 0.016 * periodic_noise(SIZE, 3, 9, 1.1, seed + 21))[..., None]

    base = np.empty((SIZE, SIZE, 4), dtype=np.float32)
    base[..., :3] = np.clip(rgb, 0.0, 1.0)
    base[..., 3] = alpha

    # Normal: STRONG corduroy (the albedo is mostly transparent, so the normal carries
    # the grooming signature) + along-V ski-track streaks + settled undulation + grain.
    streak = periodic_noise_aniso(SIZE, 6, 90, 3, 0.8, seed + 5)
    undul = periodic_noise(SIZE, 3, 12, 1.3, seed + 3)
    grain = periodic_noise(SIZE, 60, 320, 0.9, seed + 4)
    h = 1.00 * groove + 0.30 * streak + 0.22 * undul + 0.15 * grain
    h /= np.abs(h).max()
    normal = normal_from_height(h, 0.0050)           # vs 0.0032 on the terrain pair
    return base, normal


# --------------------------------------------------------------------------------------
# Set 2: snowmaking run - denser, whiter, faint mogul undulation
# --------------------------------------------------------------------------------------

def build_snowmaking(seed):
    """Returns (base_rgba, normal_rgba). Higher coverage (~70-80% mean), whiter tone
    (snowmaking wets/whitens the surface), faint 3-5 m mogul undulation in the normal."""
    groove = corduroy_groove(seed)
    ridge = np.clip(groove, 0.0, 1.0)

    alpha, field, worn = coverage_alpha(
        seed, base_cov=SNOWMAKING_COVERAGE, amp=0.20, ridge=ridge,
        worn_edges=(-0.92, -0.50), worn_floor=0.30)

    # Color: wet machine-made snow - cool white, denser and brighter than the groomed
    # set but still translucent enough for terrain to ghost through.
    white = np.array([0.760, 0.775, 0.800])          # wet snow, cool
    base_tone = np.array([0.640, 0.650, 0.665])      # thinner coverage, grayer

    t = smoothstep(0.45, 0.90, alpha)
    rgb = base_tone[None, None, :] + (white - base_tone)[None, None, :] * t[..., None]

    trough = np.clip(-groove, 0.0, 1.0)
    rgb -= 0.022 * trough[..., None] * np.array([1.00, 0.95, 0.80])[None, None, :]
    rgb[..., 2] += 0.010                              # cool cast in the pack
    rgb += (0.014 * periodic_noise(SIZE, 80, 400, 0.8, seed + 20)
            + 0.012 * periodic_noise(SIZE, 3, 9, 1.1, seed + 21))[..., None]

    base = np.empty((SIZE, SIZE, 4), dtype=np.float32)
    base[..., :3] = np.clip(rgb, 0.0, 1.0)
    base[..., 3] = alpha

    # Normal: corduroy + FAINT mogul-scale undulation (3-5 m -> integer frequencies
    # 2..3 on the 8 m tile) + streaks + grain. Low-frequency relief contributes little
    # slope, so the moguls read as broad soft shading - intended.
    mogul = periodic_noise(SIZE, 2, 3, 1.5, seed + 6)
    streak = periodic_noise_aniso(SIZE, 6, 90, 3, 0.8, seed + 5)
    grain = periodic_noise(SIZE, 60, 320, 0.9, seed + 4)
    h = 0.95 * groove + 1.00 * mogul + 0.25 * streak + 0.18 * grain
    h /= np.abs(h).max()
    normal = normal_from_height(h, 0.0058)
    return base, normal


# --------------------------------------------------------------------------------------
# Seam verification (ski_terrain.py's check): roll 50% on both axes, compare the
# relocated wrap edge against the image's own adjacent-pixel differences. Checks EVERY
# channel it is given - for BaseColor that includes the coverage alpha, which is just as
# load-bearing for tiling as the color.
# --------------------------------------------------------------------------------------

def seam_check(label, arr, tolerance=1.5):
    a = arr if arr.ndim == 3 else arr[..., None]
    s = a.shape[0]
    r = np.roll(a, (s // 2, s // 2), axis=(0, 1))
    # The original wrap edges now sit between rows/cols s//2-1 and s//2.
    seam_v = np.abs(r[s // 2] - r[s // 2 - 1]).mean()          # horizontal seam (V wrap)
    seam_u = np.abs(r[:, s // 2] - r[:, s // 2 - 1]).mean()    # vertical seam (U wrap)
    base_v = np.abs(np.diff(a, axis=0)).mean()                 # interior baselines
    base_u = np.abs(np.diff(a, axis=1)).mean()
    ok = (seam_v <= base_v * tolerance + 1e-4) and (seam_u <= base_u * tolerance + 1e-4)
    print(f"[seam] {label:45s} U-wrap {seam_u:.5f} (interior {base_u:.5f})  "
          f"V-wrap {seam_v:.5f} (interior {base_v:.5f})  -> {'PASS' if ok else 'FAIL'}")
    return ok


# --------------------------------------------------------------------------------------
# Previews: decal composited the way the shader will, then Lambert-relit in numpy.
# --------------------------------------------------------------------------------------

LIGHT = np.array([0.55, 0.35, 0.76])
LIGHT = LIGHT / np.linalg.norm(LIGHT)


def comp_over(bg_rgb, bg_normal, base, normal):
    """Albedo lerped by BaseColor alpha; decal normal at FULL strength independent of
    albedo alpha (m_NormalAlphaSource = 1), added to the background's slopes
    (DECAL_SURFACE_GRADIENT semantics); Lambert-lit with the raking preview light."""
    a = base[..., 3:4]
    rgb = bg_rgb[..., :3] * (1.0 - a) + base[..., :3] * a

    nb = bg_normal[..., :3] * 2.0 - 1.0
    nd = normal[..., :3] * 2.0 - 1.0
    sx = nb[..., 0] / nb[..., 2] + nd[..., 0] / nd[..., 2]
    sy = nb[..., 1] / nb[..., 2] + nd[..., 1] / nd[..., 2]
    inv = 1.0 / np.sqrt(sx * sx + sy * sy + 1.0)
    shade = np.clip(sx * inv * LIGHT[0] + sy * inv * LIGHT[1] + inv * LIGHT[2],
                    0.0, 1.0) / LIGHT[2]             # flat surface -> 1.0
    out = np.empty((SIZE, SIZE, 4), dtype=np.float32)
    out[..., :3] = np.clip(rgb * (0.45 + 0.55 * shade[..., None]), 0.0, 1.0)
    out[..., 3] = 1.0
    return out


def make_backdrops():
    """Tileable preview grounds: shared low-frequency terrain relief (so ghost-through
    of the receiver's shading is visible) under summer grass and winter snow albedo."""
    ht = (0.70 * periodic_noise(SIZE, 2, 6, 1.2, SEED_BACKDROP + 1)
          + 0.30 * periodic_noise(SIZE, 8, 40, 1.0, SEED_BACKDROP + 2))
    ht /= np.abs(ht).max()
    bg_normal = normal_from_height(ht, 0.0040)

    grass = np.empty((SIZE, SIZE, 3), dtype=np.float32)
    grass[:] = np.array([0.295, 0.355, 0.185])
    grass *= (1.0 + 0.16 * periodic_noise(SIZE, 3, 30, 1.0, SEED_BACKDROP + 3))[..., None]

    snow = np.empty((SIZE, SIZE, 3), dtype=np.float32)
    snow[:] = np.array([0.805, 0.825, 0.860])
    snow *= (1.0 + 0.05 * periodic_noise(SIZE, 3, 30, 1.0, SEED_BACKDROP + 4))[..., None]

    flat = np.empty((SIZE, SIZE, 4), dtype=np.float32)
    flat[..., :2] = 0.5
    flat[..., 2:] = 1.0
    gray = np.full((SIZE, SIZE, 3), 0.45, dtype=np.float32)
    return gray, flat, np.clip(grass, 0.0, 1.0), np.clip(snow, 0.0, 1.0), bg_normal


def _tile2x2_down(img):
    tiled = np.tile(img, (2, 2, 1))
    return tiled.reshape(SIZE, 2, SIZE, 2, 4).mean(axis=(1, 3)).astype(np.float32)


def write_previews(name, base, normal, backdrops):
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    gray, flat_n, grass, snow, bg_n = backdrops

    # Over neutral gray: alpha patchiness + groove relief without color context.
    on_gray = comp_over(gray, flat_n, base, normal)
    _write_png(os.path.join(PREVIEW_DIR, f"{name}_tiling2x2.png"), f"{name}_tiling2x2",
               _tile2x2_down(on_gray), SIZE, is_data=False)
    q = SIZE // 4
    crop = on_gray[q:q + SIZE // 2, q:q + SIZE // 2]
    close = np.repeat(np.repeat(crop, 2, axis=0), 2, axis=1).astype(np.float32)
    _write_png(os.path.join(PREVIEW_DIR, f"{name}_closeup.png"), f"{name}_closeup",
               close, SIZE, is_data=False)

    # Over-context comps: judge the ghost-through over summer grass and winter snow.
    for bg_name, bg in (("overGrass", grass), ("overSnow", snow)):
        comp = comp_over(bg, bg_n, base, normal)
        _write_png(os.path.join(PREVIEW_DIR, f"{name}_{bg_name}.png"),
                   f"{name}_{bg_name}", _tile2x2_down(comp), SIZE, is_data=False)
    print(f"[ski_decal] previews for {name} -> {PREVIEW_DIR}")


# --------------------------------------------------------------------------------------
# Readback sanity: confirm the PNG really carries the coverage alpha and the
# display-encoded values we wrote (guards against a colorspace transform or an alpha
# drop sneaking into the write path).
# --------------------------------------------------------------------------------------

def readback_stats(path):
    img = bpy.data.images.load(path)
    px = np.empty(len(img.pixels), dtype=np.float32)
    img.pixels.foreach_get(px)     # byte-backed image: raw byte/255, no transform
    bpy.data.images.remove(img)
    px = px.reshape(-1, 4)
    return px[:, :3].mean(), px[:, 3].mean(), px[:, 3].min(), px[:, 3].max()


# --------------------------------------------------------------------------------------

def main():
    os.makedirs(TEX_DIR, exist_ok=True)
    backdrops = make_backdrops()
    results = {}

    for name, builder in (("SkiRunDecalGroomed", lambda: build_groomed(SEED_GROOMED)),
                          ("SkiRunDecalSnowmaking", lambda: build_snowmaking(SEED_SNOWMAKING))):
        base, normal = builder()
        results[f"{name}_BaseColor"] = seam_check(f"{name}_BaseColor", base)      # RGBA
        results[f"{name}_Normal"] = seam_check(f"{name}_Normal", normal[..., :3])

        print(f"[ski_decal] {name}_BaseColor authored: "
              f"mean RGB {base[..., :3].mean():.3f}  "
              f"alpha mean {base[..., 3].mean():.3f} "
              f"min {base[..., 3].min():.3f} max {base[..., 3].max():.3f}")

        base_path = os.path.join(TEX_DIR, f"{name}_BaseColor.png")
        _write_png(base_path, f"{name}_BaseColor", base, SIZE, is_data=False)
        _write_png(os.path.join(TEX_DIR, f"{name}_Normal.png"), f"{name}_Normal",
                   normal, SIZE, is_data=True)

        rgb_mean, a_mean, a_min, a_max = readback_stats(base_path)
        print(f"[ski_decal] {name}_BaseColor readback: mean RGB {rgb_mean:.3f}  "
              f"alpha mean {a_mean:.3f} min {a_min:.3f} max {a_max:.3f}")
        write_previews(name, base, normal, backdrops)

    n_pass = sum(results.values())
    print(f"[ski_decal] seam checks: {n_pass}/{len(results)} PASS")
    if n_pass != len(results):
        sys.exit(1)


main()
