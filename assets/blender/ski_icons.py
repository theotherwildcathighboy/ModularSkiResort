"""
Deterministic UI icon generator for the Modular Ski Resort mod.

Produces the eight asset-menu icons (256x256 RGBA PNG + 32-hex .png.cid sidecar each) in the
exact visual style of the existing SkiResortCategory tab icon (SkiResortTab.png), measured
from its pixels:

    plate: rounded square, RGB(30,35,41), spanning 10..245 px, corner radius ~45
    glyph: white RGB(246,246,246) strokes/fills, stroke weight ~9..14 px, antialiased

No Blender/bpy dependency — pure numpy SDF rasterisation at 4x supersample plus a minimal
PNG encoder, so it runs directly on Blender's bundled python.exe:

    "<blender>\\<ver>\\python\\bin\\python.exe" ski_icons.py --out <dir>

Everything is deterministic, including the .cid sidecars: each cid is the md5 hex digest of
a fixed salt + icon name, so re-running the script never churns installed prefab references.
(A cid only needs to be a unique 32-hex id for the asset database; the game does not derive
it from file content — see the hand-minted SkiResortTab.png.cid precedent.)
"""

import argparse
import hashlib
import os
import struct
import sys
import zlib

import numpy as np

# ---------------------------------------------------------------------------------------
# Style constants measured from SkiResortTab.png (see module docstring).
# ---------------------------------------------------------------------------------------
CANVAS = 256          # output size
SS = 4                # supersample factor (AA)
PLATE_MIN = 10.0      # plate rounded-rect extents in canvas px
PLATE_MAX = 245.0
PLATE_RADIUS = 45.0
PLATE_RGB = (30, 35, 41)
GLYPH_RGB = (246, 246, 246)
STROKE = 12.0         # default stroke weight (canvas px)

CID_SALT = "SkiResortMod.Icons.v1/"  # md5(salt + png name) -> deterministic 32-hex cid


# ---------------------------------------------------------------------------------------
# SDF primitives. All operate on (H,W) coordinate grids in *canvas* units and return
# signed distances (negative inside). Shapes are combined by min().
# ---------------------------------------------------------------------------------------
def grid():
    """Supersampled pixel-centre coordinate grids in canvas units."""
    n = CANVAS * SS
    c = (np.arange(n, dtype=np.float64) + 0.5) / SS
    x, y = np.meshgrid(c, c)
    return x, y


def sd_segment(x, y, ax, ay, bx, by, w):
    """Capsule stroke of width w along segment A->B."""
    px, py = x - ax, y - ay
    dx, dy = bx - ax, by - ay
    t = np.clip((px * dx + py * dy) / (dx * dx + dy * dy), 0.0, 1.0)
    return np.hypot(px - t * dx, py - t * dy) - w / 2.0


def sd_disk(x, y, cx, cy, r):
    return np.hypot(x - cx, y - cy) - r


def sd_ring(x, y, cx, cy, r, w):
    """Circle outline (annulus) of radius r, stroke width w."""
    return np.abs(np.hypot(x - cx, y - cy) - r) - w / 2.0


def sd_round_rect(x, y, x0, y0, x1, y1, r):
    """Filled rounded rectangle covering [x0,x1]x[y0,y1] with corner radius r."""
    cx, cy = (x0 + x1) / 2.0, (y0 + y1) / 2.0
    hx, hy = (x1 - x0) / 2.0 - r, (y1 - y0) / 2.0 - r
    qx = np.abs(x - cx) - hx
    qy = np.abs(y - cy) - hy
    outside = np.hypot(np.maximum(qx, 0.0), np.maximum(qy, 0.0))
    inside = np.minimum(np.maximum(qx, qy), 0.0)
    return outside + inside - r


def sd_polygon(x, y, pts):
    """Filled simple polygon, pts = [(x,y), ...] in order (either winding)."""
    d = np.full(x.shape, np.inf)
    inside = np.zeros(x.shape, dtype=bool)
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % n]
        d = np.minimum(d, sd_segment(x, y, ax, ay, bx, by, 0.0))
        # even-odd crossing test
        cond = (ay > y) != (by > y)
        with np.errstate(divide="ignore", invalid="ignore"):
            xin = ax + (y - ay) * (bx - ax) / (by - ay)
        inside ^= cond & (x < np.where(cond, xin, np.inf))
    return np.where(inside, -d, d)


# ---------------------------------------------------------------------------------------
# Glyph definitions. Each returns a list of SDF arrays; union = min over the list.
# A definition may instead return (shapes, cutouts) — cutouts are subtracted from the
# union (used to carve plate-coloured detail out of filled shapes).
# Canvas is 256x256, y grows downward. Keep glyphs inside ~(36..220) both axes.
# ---------------------------------------------------------------------------------------
def glyph_gate(x, y):
    """Welcome-sign archway: two posts carrying a wide sign board with carved text lines."""
    shapes = [
        sd_segment(x, y, 78, 196, 78, 84, 14),     # left post
        sd_segment(x, y, 178, 196, 178, 84, 14),   # right post
        sd_round_rect(x, y, 48, 58, 208, 108, 10), # sign board
    ]
    cutouts = [
        sd_round_rect(x, y, 66, 72, 190, 80, 4),   # "text" line 1
        sd_round_rect(x, y, 82, 88, 174, 96, 4),   # "text" line 2 (shorter, centred)
    ]
    return shapes, cutouts


def glyph_lodge(x, y):
    """A-frame silhouette: steep roof to the ground, chimney through the right slope,
    centred door."""
    apex = (128, 56)
    bl, br = (56, 196), (200, 196)
    # chimney: vertical bar crossing the right roof line (roof y at x=160 is ~120)
    return [
        sd_segment(x, y, apex[0], apex[1], bl[0], bl[1], 13),   # left slope
        sd_segment(x, y, apex[0], apex[1], br[0], br[1], 13),   # right slope
        sd_segment(x, y, bl[0], bl[1], br[0], br[1], 13),       # ground beam
        sd_round_rect(x, y, 156, 80, 176, 140, 4),              # chimney
        sd_round_rect(x, y, 117, 150, 139, 196, 5),             # door
    ]


def glyph_base_station(x, y):
    """Drive terminal at the bottom of the line: building block on the ground with the
    bullwheel in front, cable heading up-mountain."""
    return [
        sd_segment(x, y, 36, 196, 220, 196, 12),        # ground
        sd_round_rect(x, y, 104, 120, 212, 190, 8),     # terminal building
        sd_ring(x, y, 68, 152, 26, 11),                 # bullwheel
        sd_segment(x, y, 60, 127, 30, 96, 10),          # haul rope leaving uphill
    ]


def glyph_top_station(x, y):
    """Return terminal on the summit: peak silhouette with the terminal block on top."""
    return [
        sd_segment(x, y, 36, 200, 128, 104, 13),        # left ridge
        sd_segment(x, y, 128, 104, 220, 200, 13),       # right ridge
        sd_round_rect(x, y, 96, 58, 160, 96, 8),        # terminal block on the summit
    ]


def _slope_and_corduroy(x, y):
    """Shared groomed-slope geometry: fall-line edge + three corduroy stripes."""
    a, b = (44, 84), (212, 188)                     # slope edge, upper-left -> lower-right
    dx, dy = b[0] - a[0], b[1] - a[1]
    ln = float(np.hypot(dx, dy))
    ux, uy = dx / ln, dy / ln                       # along-slope unit
    nx, ny = -uy, ux                                # perpendicular, into the hill (down-left)
    shapes = [sd_segment(x, y, a[0], a[1], b[0], b[1], 12)]
    for k in (1, 2, 3):
        off = 30.0 * k
        t0 = 0.10 + 0.13 * k
        t1 = 0.97 - 0.05 * k
        sx, sy = a[0] + nx * off, a[1] + ny * off
        shapes.append(sd_segment(x, y,
                                 sx + ux * ln * t0, sy + uy * ln * t0,
                                 sx + ux * ln * t1, sy + uy * ln * t1, 10))
    return shapes


def glyph_run_groomed(x, y):
    return _slope_and_corduroy(x, y)


def glyph_run_snowmaking(x, y):
    """Groomed slope plus a six-armed snowflake in the clear upper-right corner."""
    shapes = _slope_and_corduroy(x, y)
    cx, cy, r = 178, 74, 30
    for ang in (90.0, 30.0, -30.0):                 # three bars -> six arms
        ca, sa = np.cos(np.radians(ang)), np.sin(np.radians(ang))
        shapes.append(sd_segment(x, y, cx - r * ca, cy - r * sa,
                                 cx + r * ca, cy + r * sa, 8))
    return shapes


# Round E1 (2026-10-04, econ-round spec ch2 section 7.1, D20): natural and night run icons.
# Art: needs Richard's verdict (ch2 section 7.3 has the two-line revert per icon).
def glyph_run_natural(x, y):
    """Natural (ungroomed) slope: the fall-line edge, a wavy ski track in place of the corduroy,
    and a small pine in the clear upper-right corner."""
    a, b = (44, 84), (212, 188)
    dx, dy = b[0] - a[0], b[1] - a[1]
    ln = float(np.hypot(dx, dy))
    ux, uy = dx / ln, dy / ln
    nx, ny = -uy, ux
    shapes = [sd_segment(x, y, a[0], a[1], b[0], b[1], 12)]
    # ski track in ungroomed snow: a sine wave (2 periods, amplitude 9 px) 44 px into the hill
    pts = []
    for k in range(17):
        t = 0.24 + 0.6 * k / 16.0
        off = 44.0 + 9.0 * float(np.sin(2.0 * np.pi * 2.0 * k / 16.0))
        pts.append((a[0] + ux * ln * t + nx * off, a[1] + uy * ln * t + ny * off))
    for k in range(16):
        shapes.append(sd_segment(x, y, pts[k][0], pts[k][1], pts[k + 1][0], pts[k + 1][1], 10))
    # pine: two stacked triangles + trunk
    shapes.append(sd_polygon(x, y, [(176, 36), (152, 74), (200, 74)]))
    shapes.append(sd_polygon(x, y, [(176, 56), (146, 102), (206, 102)]))
    shapes.append(sd_round_rect(x, y, 170, 100, 182, 120, 2))
    return shapes


def glyph_run_night(x, y):
    """Groomed slope plus a crescent moon in the clear upper-right corner (night skiing)."""
    shapes = _slope_and_corduroy(x, y)
    shapes.append(sd_disk(x, y, 176, 74, 32))
    cutouts = [sd_disk(x, y, 192, 62, 27)]
    return shapes, cutouts


ICONS = {
    "SkiResortGate": glyph_gate,
    "SkiAFrameDayLodge": glyph_lodge,
    "SkiBaseStationBuilding": glyph_base_station,
    "SkiTopStationBuilding": glyph_top_station,
    "SkiRunGroomedA": glyph_run_groomed,
    "SkiRunSnowmakingA": glyph_run_snowmaking,
    "SkiRunNaturalA": glyph_run_natural,
    "SkiRunNightA": glyph_run_night,
}


# ---------------------------------------------------------------------------------------
# Rasterise + encode
# ---------------------------------------------------------------------------------------
def render(glyph_fn):
    x, y = grid()
    plate = sd_round_rect(x, y, PLATE_MIN, PLATE_MIN, PLATE_MAX, PLATE_MAX, PLATE_RADIUS) < 0
    result = glyph_fn(x, y)
    shapes, cutouts = result if isinstance(result, tuple) else (result, [])
    gd = np.full(x.shape, np.inf)
    for s in shapes:
        gd = np.minimum(gd, s)
    glyph = gd < 0
    for c in cutouts:
        glyph &= ~(c < 0)
    glyph &= plate                                  # glyphs never bleed off the plate

    # premultiplied supersampled compositing, then box-filter downsample
    n = CANVAS * SS
    rgba = np.zeros((n, n, 4), dtype=np.float64)
    for ch in range(3):
        rgba[..., ch] = np.where(glyph, GLYPH_RGB[ch], np.where(plate, PLATE_RGB[ch], 0.0))
    rgba[..., 3] = np.where(plate, 255.0, 0.0)
    rgba[..., :3] *= (rgba[..., 3:4] / 255.0)       # premultiply
    small = rgba.reshape(CANVAS, SS, CANVAS, SS, 4).mean(axis=(1, 3))
    alpha = small[..., 3:4]
    with np.errstate(divide="ignore", invalid="ignore"):
        unpre = np.where(alpha > 0, small[..., :3] * 255.0 / alpha, 0.0)
    out = np.concatenate([unpre, alpha], axis=2)
    return np.clip(np.rint(out), 0, 255).astype(np.uint8)


def encode_png(img):
    """Minimal PNG encoder: 8-bit RGBA, no interlace, filter 0 rows."""
    h, w, _ = img.shape
    raw = b"".join(b"\x00" + img[r].tobytes() for r in range(h))

    def chunk(typ, data):
        c = typ + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def cid_for(png_name):
    return hashlib.md5((CID_SALT + png_name).encode("ascii")).hexdigest()


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                  "icons_out"),
                    help="output directory for the PNG + .png.cid pairs")
    ap.add_argument("--only", default="", help="comma-separated icon names to write (default: all)")
    args = ap.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv
                         else sys.argv[1:])
    only = [s.strip() for s in args.only.split(",") if s.strip()]

    os.makedirs(args.out, exist_ok=True)
    for name, fn in ICONS.items():
        if only and name not in only:
            continue
        png_name = name + ".png"
        img = render(fn)
        with open(os.path.join(args.out, png_name), "wb") as f:
            f.write(encode_png(img))
        cid = cid_for(png_name)
        # byte-for-byte the SkiResortTab.png.cid format: 32 lowercase hex, no newline
        with open(os.path.join(args.out, png_name + ".cid"), "wb") as f:
            f.write(cid.encode("ascii"))
        print(f"{png_name}  cid={cid}")


if __name__ == "__main__":
    main()
