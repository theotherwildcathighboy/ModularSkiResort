# ski_terrain.py - seamless TILING terrain-material texture sets for the splatmap route
# (research: docs/research/terrain-material-runs.md "Proposed architecture").
#
# Unlike the surface/decal atlas work, these are TERRAIN textures: projected by the game's
# terrain shader (triplanar on steeps) as an Extra-slot diffuse+normal pair, so they must
# tile seamlessly on BOTH axes. All pattern math is strictly periodic (integer-frequency
# sines + FFT random-phase spectral noise), so the tiles are seamless by construction; a
# roll-by-half seam check verifies it numerically and prints PASS/FAIL per texture.
#
# World-scale assumption: one tile covers ~8 x 8 m (documented in Textures/README.md).
#   Winter: corduroy pitch ~16.7 cm -> 48 ridge lines per tile, ridges run along V.
#   Summer: mower stripes, 3 light/dark band pairs per tile (6 bands, ~1.33 m each,
#           nearest seamless fit to the ~1.5 m deck-width spec), bands run along V.
#
# Outputs (file contract - a parallel agent loads these EXACT names):
#   mod/SkiResortMod/Textures/SkiTerrainSnowCorduroy_BaseColor.png   sRGB (display-encoded)
#   mod/SkiResortMod/Textures/SkiTerrainSnowCorduroy_Normal.png      OpenGL tangent, linear
#   mod/SkiResortMod/Textures/SkiTerrainSummerRun_BaseColor.png      sRGB (display-encoded)
#   mod/SkiResortMod/Textures/SkiTerrainSummerRun_Normal.png         OpenGL tangent, linear
# Previews (numpy compositing only - NO Cycles, per machine rule):
#   assets/preview/terrain/<Name>_tiling2x2.png   2x2 repeat, 2048 box-downscaled to 1024
#   assets/preview/terrain/<Name>_closeup.png     center 4 m crop upscaled to 1024
#
# Pattern code (periodic_noise / normal_from_height / sparkle_mask) is lifted from
# ski_surface.py, which runs main() on import and therefore cannot be imported.
#
# Usage:
#   blender --background --factory-startup --python ski_terrain.py
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
TILE_METERS = 8.0            # world coverage of one tile (documented assumption)
SNOW_GROOVES = 48            # corduroy ridge lines per tile: 8 m / 0.167 m pitch
STRIPE_PAIRS = 3             # mower light/dark band pairs per tile: 6 bands of ~1.33 m

SEED_SNOW = 20260811
SEED_SUMMER = 20260812


# --------------------------------------------------------------------------------------
# Pattern primitives (from ski_surface.py - periodic by construction)
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


def sparkle_mask(size, density, seed):
    """Sparse glint pixels (fraction `density`), with a few 2-px clusters."""
    rng = np.random.default_rng(seed)
    field = rng.random((size, size))
    m = field < density
    m |= np.roll(m, 1, axis=1) & (rng.random((size, size)) < 0.35)
    return m


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# --------------------------------------------------------------------------------------
# Winter: groomed corduroy snow
# --------------------------------------------------------------------------------------

def build_snow(seed):
    """Returns (base_rgba, normal_rgba). Corduroy ridges run along V (image vertical),
    so the sine varies along U."""
    u = (np.arange(SIZE) + 0.5) / SIZE
    X, _Y = np.meshgrid(u, u)

    # Height field: dominant corduroy + settled undulation + fine snow grain.
    wobble = periodic_noise(SIZE, 2, 7, 1.2, seed + 1) * 1.4        # groove waviness (rad)
    depth_mod = 0.74 + 0.26 * periodic_noise(SIZE, 2, 9, 1.0, seed + 2)
    groove = np.sin(2.0 * np.pi * SNOW_GROOVES * X + wobble) * depth_mod
    undul = periodic_noise(SIZE, 3, 14, 1.4, seed + 3)              # soft snow undulation
    grain = periodic_noise(SIZE, 60, 340, 0.9, seed + 4)            # fine snow grain
    h = 1.00 * groove + 0.45 * undul + 0.22 * grain
    h /= np.abs(h).max()
    normal = normal_from_height(h, 0.0032)

    # BaseColor: display values kept in ~0.78-0.88 (ledger 46: >=0.9 reads blown-out).
    trough = np.clip(-groove, 0.0, 1.0)                             # 1 in groove bottoms
    patch = periodic_noise(SIZE, 3, 8, 1.1, seed + 10) * 0.018      # 1-3 m patchiness
    fine = periodic_noise(SIZE, 80, 400, 0.8, seed + 11) * 0.007
    v = 0.842 + patch + fine - 0.042 * trough

    base = np.empty((SIZE, SIZE, 4), dtype=np.float32)
    base[..., 0] = v - 0.020 * trough - 0.004    # cool blue-gray shadow between ridges
    base[..., 1] = v - 0.010 * trough
    base[..., 2] = v + 0.012
    base[..., 3] = 1.0

    glint = sparkle_mask(SIZE, 0.0012, seed + 13)                   # faint sparkle
    base[..., :3][glint] = np.minimum(base[..., :3][glint] + 0.055, 0.93)
    np.clip(base, 0.0, 1.0, out=base)
    return base, normal


# --------------------------------------------------------------------------------------
# Summer: brush-cut mountain grass with mower stripes
# --------------------------------------------------------------------------------------

def build_summer(seed):
    """Returns (base_rgba, normal_rgba). Mower bands aligned with V like the corduroy."""
    u = (np.arange(SIZE) + 0.5) / SIZE
    X, _Y = np.meshgrid(u, u)

    # Stripe wave: soft-edged alternating bands, slight waviness so edges look mowed,
    # not printed. stripe in -1..1, +1 = light (blades bent away) band.
    swobble = periodic_noise(SIZE, 2, 6, 1.2, seed + 1) * 0.55
    s = np.sin(2.0 * np.pi * STRIPE_PAIRS * X + swobble)
    stripe = np.tanh(s * 2.6)                        # flatten band interiors, soft edges

    # Grass color: green base with straw tones, muted (between game grass and dirt).
    green = np.array([0.27, 0.32, 0.16])
    straw = np.array([0.43, 0.38, 0.22])
    dirt = np.array([0.35, 0.28, 0.19])

    straw_w = 0.28 + 0.30 * periodic_noise(SIZE, 4, 24, 1.0, seed + 2)   # straw blotches
    straw_w = np.clip(straw_w, 0.0, 0.75)
    rgb = green[None, None, :] * (1.0 - straw_w[..., None]) + straw[None, None, :] * straw_w[..., None]

    # Mower banding: +/- ~12% luminance, light bands read slightly warmer.
    band_gain = 1.0 + 0.115 * stripe
    rgb *= band_gain[..., None]
    rgb[..., 0] += 0.014 * np.clip(stripe, 0.0, 1.0)

    # Patchy dirt/wear spots (low-freq mask, mid-freq noise roughens the edges).
    wear_n = (0.80 * periodic_noise(SIZE, 2, 6, 1.0, seed + 3)
              + 0.30 * periodic_noise(SIZE, 8, 30, 0.9, seed + 8))
    wear = smoothstep(0.46, 0.84, wear_n)                            # ~8-12% coverage
    rgb = rgb * (1.0 - wear[..., None]) + dirt[None, None, :] * wear[..., None]

    # Blade micro-texture + broad luminance patchiness.
    micro = periodic_noise(SIZE, 60, 300, 0.85, seed + 4) * 0.020
    broad = periodic_noise(SIZE, 3, 9, 1.1, seed + 5) * 0.013
    rgb += (micro + broad)[..., None]

    base = np.empty((SIZE, SIZE, 4), dtype=np.float32)
    base[..., :3] = np.clip(rgb, 0.0, 1.0)
    base[..., 3] = 1.0

    # Normal: gentle stripe relief + grass micro-noise (+ dirt spots slightly sunken).
    h = (0.30 * stripe
         + 0.55 * periodic_noise(SIZE, 50, 280, 0.9, seed + 6)       # grass clumps
         + 0.35 * periodic_noise(SIZE, 4, 18, 1.2, seed + 7)         # ground undulation
         - 0.25 * wear)
    h /= np.abs(h).max()
    normal = normal_from_height(h, 0.0022)
    return base, normal


# --------------------------------------------------------------------------------------
# Seam verification: roll 50% on both axes, compare the relocated wrap edge against the
# image's own adjacent-pixel differences. Periodic construction should make them match.
# --------------------------------------------------------------------------------------

def seam_check(label, arr, tolerance=1.5):
    a = arr[..., :3] if arr.ndim == 3 else arr[..., None]
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
# Previews: normal-lit Lambert composite in numpy (no Cycles / no GPU).
# --------------------------------------------------------------------------------------

def lit_composite(base, normal):
    l = np.array([0.55, 0.35, 0.76])           # raking from upper-right: shows corduroy
    l /= np.linalg.norm(l)
    n = normal[..., :3] * 2.0 - 1.0
    shade = np.clip(n @ l, 0.0, 1.0) / l[2]    # flat surface -> 1.0
    lit = base.copy()
    lit[..., :3] = np.clip(base[..., :3] * (0.45 + 0.55 * shade[..., None]), 0.0, 1.0)
    return lit


def write_previews(name, base, normal):
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    lit = lit_composite(base, normal)

    # 2x2 tiled seam proof: 2048 -> box downscale -> 1024.
    tiled = np.tile(lit, (2, 2, 1))
    down = tiled.reshape(SIZE, 2, SIZE, 2, 4).mean(axis=(1, 3)).astype(np.float32)
    _write_png(os.path.join(PREVIEW_DIR, f"{name}_tiling2x2.png"), f"{name}_tiling2x2",
               down, SIZE, is_data=False)

    # Closeup: center 4 m (512 px) crop, 2x pixel upscale.
    q = SIZE // 4
    crop = lit[q:q + SIZE // 2, q:q + SIZE // 2]
    close = np.repeat(np.repeat(crop, 2, axis=0), 2, axis=1).astype(np.float32)
    _write_png(os.path.join(PREVIEW_DIR, f"{name}_closeup.png"), f"{name}_closeup",
               close, SIZE, is_data=False)
    print(f"[ski_terrain] previews for {name} -> {PREVIEW_DIR}")


# --------------------------------------------------------------------------------------
# Readback sanity: confirm the PNG bytes really are the display-encoded values we wrote
# (guards against a colorspace transform sneaking into the write path).
# --------------------------------------------------------------------------------------

def readback_stats(path):
    img = bpy.data.images.load(path)
    px = np.empty(len(img.pixels), dtype=np.float32)
    img.pixels.foreach_get(px)     # byte-backed image: raw byte/255, no transform
    bpy.data.images.remove(img)
    rgb = px.reshape(-1, 4)[:, :3]
    return rgb.mean(), rgb.min(), rgb.max()


# --------------------------------------------------------------------------------------

def main():
    os.makedirs(TEX_DIR, exist_ok=True)
    results = {}

    for name, builder in (("SkiTerrainSnowCorduroy", lambda: build_snow(SEED_SNOW)),
                          ("SkiTerrainSummerRun", lambda: build_summer(SEED_SUMMER))):
        base, normal = builder()
        results[f"{name}_BaseColor"] = seam_check(f"{name}_BaseColor", base)
        results[f"{name}_Normal"] = seam_check(f"{name}_Normal", normal)

        base_path = os.path.join(TEX_DIR, f"{name}_BaseColor.png")
        _write_png(base_path, f"{name}_BaseColor", base, SIZE, is_data=False)
        _write_png(os.path.join(TEX_DIR, f"{name}_Normal.png"), f"{name}_Normal",
                   normal, SIZE, is_data=True)

        mean, lo, hi = readback_stats(base_path)
        print(f"[ski_terrain] {name}_BaseColor readback: mean {mean:.3f} "
              f"min {lo:.3f} max {hi:.3f}")
        write_previews(name, base, normal)

    n_pass = sum(results.values())
    print(f"[ski_terrain] seam checks: {n_pass}/{len(results)} PASS")
    if n_pass != len(results):
        sys.exit(1)


main()
