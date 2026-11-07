"""
Launcher glyph candidates (Round S1 F9): a one-color white glyph for the hub's floating button. The
game draws every other button in the top-left row the same way (cs2/ui FloatingButton with the
default tint: a white mask on the accent plate).

Same technique as assets/blender/ski_icons.py: numpy signed-distance shapes, analytic coverage at
supersampling, a minimal PNG encoder, no bpy. Run it on Blender's bundled python.exe:

    "<blender>\\<ver>\\python\\bin\\python.exe" ski_launcher_glyph.py --out <dir> [--row <toolbar-row.png>]
        [--only a,b,c,d] [--detail]

Writes per candidate:
- launcher-<x>.png (256 x 256, RGB 255 everywhere, the shape in alpha). The mask is the same whether
  Gameface reads it as an alpha or a luminance mask.
- launcher-<x>.svg (the same primitives, white, viewBox 0 0 256 256). Every run parses it back and
  re-renders it, so the SVG says exactly what the PNG shows.

It also writes sheet.png when --row is given (the real row on top, each candidate in our slot, the
button at 80 / 40 px normal and selected, the rejected outline drafts of (a)). With --detail it
writes detail.png (a review aid).

Written 2026-10-05 for the S1 spec (docs/research/ski-days/spec/ch5-launcher.md). The review copy is
in docs/research/art/icons-2026-10-05/launcher-glyph/ (as launcher_glyph.py). Once a glyph is
chosen, the script's repo home is assets/blender/ski_launcher_glyph.py (ch5 E.1 step 2).

Shipped: (c), as mod/SkiResortUI/src/hub/SkiLauncherGlyph.svg (a byte copy of launcher-c.svg, md5
605f036f50ed1b03ffff6cc5dcdd0f78). It was the owner's pick (DECISIONS 148). Swapping the pick is a
one-file copy.

Every shape is one of five primitives that map 1:1 to SVG:
- a round-capped, round-joined stroke along a polyline (<path stroke-linecap=round
  stroke-linejoin=round>)
- a filled polygon (<path>)
- a disk (<circle>)
- a ring (<circle fill=none>)
- a rounded rectangle (<rect rx>)

There are no knockouts (a mask cannot subtract) and no text.

The measurements come from the real row (toolbar-row.png, 4K). The plate is 80 px. The icon box is
72 px, since .button_ke4 pads var(--gap2) = 4 px. The vanilla Info glyph is 58 px = 81 % of its box.

Targets:
- The glyph outer bbox is about 80 % of the canvas (205 of 256 px) -> 72 % of the plate.
- The main stroke is 24 canvas px = 3/32 of the box, exactly Media/Glyphs/Info.svg's ring (6.75 px
  at 4K, 3.4 px at 1080p). No stroke is under 14 (2 px at 1080p).
- Gaps between separate parts are about 21 canvas px (3 px at 1080p), so they stay apart at 40 px.
"""

import argparse
import os
import struct
import sys
import zlib

import numpy as np

CANVAS = 256

# --------------------------------------------------------------------------------------------
# Candidates. Each primitive: ("line", [(x,y),...], w) | ("fill", [(x,y),...]) |
# ("disk", cx, cy, r) | ("ring", cx, cy, r, w) | ("rect", x0, y0, x1, y1, r)
# --------------------------------------------------------------------------------------------


def on_line(p0, p1, x):
    """y of the straight cable p0 -> p1 at x."""
    return p0[1] + (p1[1] - p0[1]) * (x - p0[0]) / (p1[0] - p0[0])


def hook_chair(gx, gy, back_x, arc_y, seat_y, seat_x1, hw, sw, n=10):
    """Side-view chairlift chair. The hanger drops from the grip (gx, gy) on the cable. It bends into
    the back at back_x along a quarter circle (radius gx - back_x, starting at arc_y). The back
    drops to the seat at seat_y (one stroke, hw: a thicker back made an elbow where the two met).
    The seat runs forward to seat_x1 (sw, the heaviest stroke). No footrest: with one it read as a
    zigzag at 40 px."""
    r = gx - back_x
    arc = [(back_x + r * np.cos(t), arc_y + r * np.sin(t)) for t in np.linspace(0.0, np.pi / 2, n)]
    return [("line", [(gx, gy)] + arc + [(back_x, seat_y)], hw),
            ("line", [(back_x + (sw - hw) / 2.0, seat_y), (seat_x1, seat_y)], sw)]   # cap flush with the back


def cand_a():
    """(a) Peak, cable and chair. A two-peak range forms a solid silhouette on the right. The cable
    rises from the left toward the high peak. One chair hangs in the air on the left."""
    p0, p1 = (30.0, 104.0), (150.0, 48.0)
    gx = 74.0
    rng = [(98.0, 228.0), (148.0, 126.0), (168.0, 150.0), (198.0, 80.0), (236.0, 228.0)]
    gy = on_line(p0, p1, gx)
    return ([("fill", rng), ("line", [p0, p1], 20.0)]
            + hook_chair(gx, gy, 52.0, gy + 20.0, gy + 80.0, 95.0, 18.0, 24.0))


def cand_b():
    """(b) A chair on a cable, alone and bold."""
    p0, p1 = (34.0, 110.0), (222.0, 46.0)
    gx = 124.0
    gy = on_line(p0, p1, gx)
    return [("line", [p0, p1], 24.0)] + hook_chair(gx, gy, 86.0, gy + 34.0, 202.0, 172.0, 22.0, 30.0)


def snowflake(cx, cy, r, w, branch=0.32, bw=16.0):
    """Six arms (stroke w). Each arm has one V of short branches (stroke bw) at 62 % out."""
    out = []
    for k in range(6):
        a = np.radians(90 + 60 * k)
        ux, uy = np.cos(a), -np.sin(a)
        out.append(("line", [(cx, cy), (cx + ux * r, cy + uy * r)], w))
        bx, by = cx + ux * r * 0.62, cy + uy * r * 0.62
        for sgn in (-1, 1):
            b = a + sgn * np.radians(50)
            out.append(("line", [(bx, by), (bx + np.cos(b) * r * branch, by - np.sin(b) * r * branch)], bw))
    return out


def cand_c():
    """(c) A peak with a snowflake. A two-peak range forms a solid silhouette, with a flake in the sky."""
    mt = [(26.0, 228.0), (100.0, 126.0), (128.0, 156.0), (170.0, 98.0), (230.0, 228.0)]
    return [("fill", mt)] + snowflake(72.0, 72.0, 44.0, 20.0)


def cand_d():
    """(d) A skier pictogram on a slope (downhill to the right). The slope is a solid wedge whose top
    is the snow. The skis lie on it with the tips turned up. One pole is planted ahead."""
    slope = ("fill", [(26.0, 176.0), (232.0, 226.0), (26.0, 226.0)])
    ski = ("line", [(58.0, 179.0), (184.0, 209.0), (200.0, 197.0)], 16.0)
    head = ("disk", 172.0, 44.0, 21.0)
    body = ("line", [(146.0, 92.0), (110.0, 134.0)], 28.0)                     # shoulder -> hip
    leg = ("line", [(110.0, 134.0), (148.0, 152.0), (126.0, 182.0)], 24.0)     # hip, knee, ankle
    arm = ("line", [(146.0, 92.0), (184.0, 116.0)], 18.0)                      # shoulder -> hand
    pole = ("line", [(184.0, 116.0), (214.0, 168.0)], 14.0)
    return [slope, ski, head, body, leg, arm, pole]


# (a) drawn as the brief's OUTLINE first. Kept for the sheet's last row. Every outline version
# failed at 40 px. Each version read as a line chart, the letter "A" / an upturned V, or "M".
def rejected_foothill():
    p0, p1 = (30.0, 102.0), (152.0, 44.0)
    gy = on_line(p0, p1, 74.0)
    return ([("line", [(24.0, 212.0), (124.0, 192.0), (192.0, 62.0), (234.0, 140.0)], 20.0),
             ("line", [p0, p1], 18.0)] + hook_chair(74.0, gy, 54.0, gy + 18.0, gy + 70.0, 94.0, 14.0, 20.0))


def rejected_peak():
    p0, p1 = (34.0, 104.0), (150.0, 46.0)
    gy = on_line(p0, p1, 78.0)
    return ([("line", [(112.0, 222.0), (190.0, 70.0), (228.0, 146.0)], 22.0), ("line", [p0, p1], 18.0)]
            + hook_chair(78.0, gy, 54.0, gy + 20.0, gy + 78.0, 100.0, 16.0, 22.0))


def rejected_two_peak():
    p0, p1 = (30.0, 104.0), (150.0, 48.0)
    gy = on_line(p0, p1, 72.0)
    return ([("line", [(112.0, 228.0), (156.0, 140.0), (174.0, 160.0), (202.0, 92.0), (232.0, 168.0)], 20.0),
             ("line", [p0, p1], 20.0)] + hook_chair(72.0, gy, 50.0, gy + 20.0, gy + 78.0, 92.0, 16.0, 22.0))


CANDIDATES = {
    "a": ("PEAK, CABLE AND CHAIR", cand_a),
    "b": ("CHAIR ON A CABLE", cand_b),
    "c": ("PEAK AND SNOWFLAKE", cand_c),
    "d": ("SKIER ON A SLOPE", cand_d),
}
REJECTED = {
    "a, outline 1": rejected_foothill,
    "a, outline 2": rejected_peak,
    "a, outline 3": rejected_two_peak,
}

# --------------------------------------------------------------------------------------------
# SDF rendering (canvas units, y down, negative inside)
# --------------------------------------------------------------------------------------------


def sd_segment(x, y, ax, ay, bx, by, w):
    px, py = x - ax, y - ay
    dx, dy = bx - ax, by - ay
    dd = dx * dx + dy * dy
    t = np.clip((px * dx + py * dy) / dd, 0.0, 1.0) if dd > 0 else 0.0
    return np.hypot(px - t * dx, py - t * dy) - w / 2.0


def sd_polygon(x, y, pts):
    d = np.full(x.shape, np.inf)
    inside = np.zeros(x.shape, dtype=bool)
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % n]
        d = np.minimum(d, sd_segment(x, y, ax, ay, bx, by, 0.0))
        cond = (ay > y) != (by > y)
        with np.errstate(divide="ignore", invalid="ignore"):
            xin = ax + (y - ay) * (bx - ax) / (by - ay)
        inside ^= cond & (x < np.where(cond, xin, np.inf))
    return np.where(inside, -d, d)


def sd_round_rect(x, y, x0, y0, x1, y1, r):
    cx, cy = (x0 + x1) / 2.0, (y0 + y1) / 2.0
    hx, hy = (x1 - x0) / 2.0 - r, (y1 - y0) / 2.0 - r
    qx, qy = np.abs(x - cx) - hx, np.abs(y - cy) - hy
    return np.hypot(np.maximum(qx, 0.0), np.maximum(qy, 0.0)) + np.minimum(np.maximum(qx, qy), 0.0) - r


def sd_prim(p, x, y):
    k = p[0]
    if k == "line":
        pts, w = p[1], p[2]
        if len(pts) == 1:
            return np.hypot(x - pts[0][0], y - pts[0][1]) - w / 2.0
        d = np.full(x.shape, np.inf)
        for i in range(len(pts) - 1):
            d = np.minimum(d, sd_segment(x, y, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1], w))
        return d
    if k == "fill":
        return sd_polygon(x, y, p[1])
    if k == "disk":
        return np.hypot(x - p[1], y - p[2]) - p[3]
    if k == "ring":
        return np.abs(np.hypot(x - p[1], y - p[2]) - p[3]) - p[4] / 2.0
    if k == "rect":
        return sd_round_rect(x, y, *p[1:])
    raise ValueError(k)


def render_alpha(prims, size, ss=8):
    """Coverage (0..1) of the glyph drawn into a size x size box (the 256 canvas scaled to fit),
    supersampled ss x ss. This gives the vector look of an SVG mask at that size."""
    n = size * ss
    c = (np.arange(n, dtype=np.float64) + 0.5) / ss * (CANVAS / size)
    x, y = np.meshgrid(c, c)
    d = np.full(x.shape, np.inf)
    for p in prims:
        d = np.minimum(d, sd_prim(p, x, y))
    px = CANVAS / size / ss              # canvas units per supersample
    cov = np.clip(0.5 - d / px, 0.0, 1.0)
    return cov.reshape(size, ss, size, ss).mean(axis=(1, 3))


# --------------------------------------------------------------------------------------------
# PNG / SVG output
# --------------------------------------------------------------------------------------------


def encode_png(img):
    img = np.ascontiguousarray(img.astype(np.uint8))
    h, w, c = img.shape
    ct = 6 if c == 4 else 2
    raw = b"".join(b"\x00" + img[r].tobytes() for r in range(h))

    def chunk(typ, data):
        cc = typ + data
        return struct.pack(">I", len(data)) + cc + struct.pack(">I", zlib.crc32(cc) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, ct, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def write_png(path, img):
    with open(path, "wb") as f:
        f.write(encode_png(img))


def glyph_png(prims):
    a = render_alpha(prims, CANVAS)
    img = np.full((CANVAS, CANVAS, 4), 255, np.uint8)
    img[..., 3] = np.clip(np.rint(a * 255), 0, 255).astype(np.uint8)
    return img, a


def _f(v):
    """A coordinate with two decimals, trailing zeros dropped (the SVG round trip in main()
    stays within 0.01 alpha of the PNG)."""
    s = f"{float(v):.2f}".rstrip("0").rstrip(".")
    return "0" if s in ("", "-0") else s


def glyph_svg(prims, title):
    out = ['<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">',
           f"<!-- Modular Ski Resort hub launcher glyph: {title.lower()}. Generated; white on transparent. -->"]
    for p in prims:
        k = p[0]
        if k == "line":
            pts = p[1]
            d = "M" + " L".join(f"{_f(x)} {_f(y)}" for x, y in pts)
            out.append(f'<path d="{d}" fill="none" stroke="#fff" stroke-width="{_f(p[2])}" '
                       'stroke-linecap="round" stroke-linejoin="round"/>')
        elif k == "fill":
            d = "M" + " L".join(f"{_f(x)} {_f(y)}" for x, y in p[1]) + " Z"
            out.append(f'<path d="{d}" fill="#fff"/>')
        elif k == "disk":
            out.append(f'<circle cx="{_f(p[1])}" cy="{_f(p[2])}" r="{_f(p[3])}" fill="#fff"/>')
        elif k == "ring":
            out.append(f'<circle cx="{_f(p[1])}" cy="{_f(p[2])}" r="{_f(p[3])}" fill="none" stroke="#fff" '
                       f'stroke-width="{_f(p[4])}"/>')
        elif k == "rect":
            x0, y0, x1, y1, r = p[1:]
            out.append(f'<rect x="{_f(x0)}" y="{_f(y0)}" width="{_f(x1 - x0)}" height="{_f(y1 - y0)}" '
                       f'rx="{_f(r)}" fill="#fff"/>')
    out.append("</svg>")
    return "\n".join(out) + "\n"


# --------------------------------------------------------------------------------------------
# Measures (same yardstick as the row: share of the 80 px plate, the glyph in a 72 px box)
# --------------------------------------------------------------------------------------------
BOX_TO_PLATE = 72.0 / 80.0


def measure(alpha):
    m = alpha >= 0.5
    ys, xs = np.nonzero(m)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    span = max(x1 - x0 + 1, y1 - y0 + 1) / CANVAS * BOX_TO_PLATE
    area = alpha.sum() / CANVAS ** 2 * BOX_TO_PLATE ** 2
    # gap: the smallest distance between two separate parts (connected components at alpha 0.5).
    # Parts that touch are one component, so gaps inside one part are judged on the sheet.
    labels, n = label(m)
    gap = None
    for i in range(1, n + 1):
        di = chamfer(labels == i)
        for j in range(i + 1, n + 1):
            g = float(di[labels == j].min()) - 1.0
            gap = g if gap is None else min(gap, g)
    return dict(bbox=(int(x0), int(y0), int(x1), int(y1)), span=span, area=area,
                centre=((x0 + x1) / 2.0, (y0 + y1) / 2.0), gap=gap, parts=n)


def label(m):
    """4-connected components of a boolean image (BFS)."""
    h, w = m.shape
    lab = np.zeros((h, w), np.int32)
    n = 0
    for sy, sx in zip(*np.nonzero(m)):
        if lab[sy, sx]:
            continue
        n += 1
        stack = [(sy, sx)]
        lab[sy, sx] = n
        while stack:
            y, x = stack.pop()
            for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                if 0 <= ny < h and 0 <= nx < w and m[ny, nx] and not lab[ny, nx]:
                    lab[ny, nx] = n
                    stack.append((ny, nx))
    return lab, n


def chamfer(src):
    """Approximate Euclidean distance (3-4 chamfer / 3) from every pixel to the True pixels."""
    h, w = src.shape
    big = 1e9
    d = np.where(src, 0.0, big)
    for y in range(h):                       # forward pass, row-vectorized where possible
        if y:
            up = d[y - 1]
            d[y] = np.minimum(d[y], up + 3)
            d[y, 1:] = np.minimum(d[y, 1:], up[:-1] + 4)
            d[y, :-1] = np.minimum(d[y, :-1], up[1:] + 4)
        row = d[y]
        for x in range(1, w):
            if row[x - 1] + 3 < row[x]:
                row[x] = row[x - 1] + 3
    for y in range(h - 1, -1, -1):           # backward pass
        if y < h - 1:
            dn = d[y + 1]
            d[y] = np.minimum(d[y], dn + 3)
            d[y, 1:] = np.minimum(d[y, 1:], dn[:-1] + 4)
            d[y, :-1] = np.minimum(d[y, :-1], dn[1:] + 4)
        row = d[y]
        for x in range(w - 2, -1, -1):
            if row[x + 1] + 3 < row[x]:
                row[x] = row[x + 1] + 3
    return d / 3.0


# --------------------------------------------------------------------------------------------
# Sheet
# --------------------------------------------------------------------------------------------
PLATE = (0, 219, 238)            # measured in toolbar-row.png (tone-mapped #4bc3f1, normal)
PLATE_SEL = (174, 237, 243)      # measured: our selected plate (tone-mapped #9ee2fc)
GLYPH = (250, 250, 250)          # .icon_be5 --iconColor rgb(250,250,250)
SNOW_BG = (232, 236, 238)
INK = (30, 40, 52)
FONT = {
    " ": ["00000"] * 7,
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "B": ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    "C": ["01110", "10001", "10000", "10000", "10000", "10001", "01110"],
    "D": ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    "F": ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
    "G": ["01110", "10001", "10000", "10111", "10001", "10001", "01111"],
    "H": ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
    "I": ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
    "J": ["00111", "00010", "00010", "00010", "00010", "10010", "01100"],
    "K": ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
    "L": ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    "M": ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    "N": ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "P": ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
    "Q": ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
    "R": ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    "S": ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
    "T": ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    "U": ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
    "V": ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
    "W": ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
    "X": ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
    "Y": ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
    "Z": ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
    "0": ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    "2": ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
    "3": ["11111", "00010", "00100", "00010", "00001", "10001", "01110"],
    "4": ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
    "5": ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
    "6": ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
    "7": ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
    "8": ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
    "9": ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
    "%": ["11000", "11001", "00010", "00100", "01000", "10011", "00011"],
    "-": ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
    ".": ["00000", "00000", "00000", "00000", "00000", "01100", "01100"],
    ",": ["00000", "00000", "00000", "00000", "01100", "00100", "01000"],
    "/": ["00001", "00001", "00010", "00100", "01000", "10000", "10000"],
    "(": ["00010", "00100", "01000", "01000", "01000", "00100", "00010"],
    ")": ["01000", "00100", "00010", "00010", "00010", "00100", "01000"],
    ":": ["00000", "01100", "01100", "00000", "01100", "01100", "00000"],
    "+": ["00000", "00100", "00100", "11111", "00100", "00100", "00000"],
    "=": ["00000", "00000", "11111", "00000", "11111", "00000", "00000"],
}


class Canvas:
    def __init__(self, w, h, bg):
        self.rgb = np.zeros((h, w, 3)) + np.asarray(bg, np.float64)
        self.w, self.h = w, h

    def text(self, s, x, y, scale, col):
        for ch in s.upper():
            rows = FONT.get(ch) or FONT[" "]
            for r, bits in enumerate(rows):
                for c, b in enumerate(bits):
                    if b == "1":
                        self.rgb[y + r * scale:y + (r + 1) * scale, x + c * scale:x + (c + 1) * scale] = col
            x += 6 * scale
        return x

    def paste(self, img, x, y):
        h, w = img.shape[:2]
        self.rgb[y:y + h, x:x + w] = img

    def png(self):
        return np.clip(np.rint(self.rgb), 0, 255).astype(np.uint8)


def gauss_blur(a, sigma):
    if sigma <= 0:
        return a
    r = int(np.ceil(3 * sigma))
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    k /= k.sum()
    h, w = a.shape
    p = np.pad(a, r)
    t = sum(k[i] * p[:, i:i + w] for i in range(2 * r + 1))           # (h + 2r, w)
    return sum(k[i] * t[i:i + h, :] for i in range(2 * r + 1))         # (h, w)


def plate_cov(size, radius, ss=8):
    n = size * ss
    c = (np.arange(n) + 0.5) / ss
    x, y = np.meshgrid(c, c)
    d = sd_round_rect(x, y, 0.0, 0.0, float(size), float(size), radius)
    return np.clip(0.5 - d * ss, 0.0, 1.0).reshape(size, ss, size, ss).mean(axis=(1, 3))


def button(prims, size, selected, bg=SNOW_BG, rem=None):
    """The floating button as the game draws it: a rounded plate (40 rem, radius 6 rem), padding
    var(--gap2) (4 px at 4K, 2 px at 1080p), the glyph mask (mask-size contain) filled with
    rgb(250,250,250). Selected state: the pale plate and drop-shadow(0 0 1rem rgba(0,0,0,0.1))."""
    rem = size / 40.0 if rem is None else rem
    pad = 4 if size >= 80 else 2
    radius = 6 * rem
    box = size - 2 * pad
    a = np.zeros((size, size))
    a[pad:pad + box, pad:pad + box] = render_alpha(prims, box)
    pc = plate_cov(size, radius)[..., None]
    plate = np.asarray(PLATE_SEL if selected else PLATE, np.float64)
    out = np.asarray(bg, np.float64) * (1 - pc) + plate * pc
    if selected:
        sh = gauss_blur(a, rem / 2.0) * 0.1
        out = out * (1 - sh[..., None])
    g = a[..., None]
    out = out * (1 - g) + np.asarray(GLYPH, np.float64) * g
    return out, pc


def zoom(img, z):
    return np.repeat(np.repeat(img, z, axis=0), z, axis=1)


def gap_text(m):
    if m["gap"] is None:
        return "ONE PART"
    return f"{m['parts']} PARTS, GAP {m['gap']:.0f}/256 = {m['gap'] * 36 / 256:.1f} PX AT 1080P"


def make_sheet(row_png, cands, path, rejected=None):
    from_row = read_png(row_png)[..., :3].astype(np.float64)
    rh, rw = from_row.shape[:2]
    margin = 20
    block_h = 26 + rh + 8 + 120 + 30 + 4
    rej_h = 26 + 120 + 30 if rejected else 0
    cv = Canvas(rw + 2 * margin, 40 + rh + 30 + len(cands) * block_h + rej_h + 20, (246, 247, 248))
    cv.text("LAUNCHER GLYPH (ROUND S1 F9). TOP: THE REAL ROW, FIELD TEST 8, 4K - OURS (2ND) SELECTED",
            margin, 14, 2, INK)
    cv.paste(from_row, margin, 40)
    y = 40 + rh + 30
    for key, (title, prims, m) in cands.items():
        cv.text(f"{key.upper()}  {title}  (IN THE ROW: NORMAL STATE)   SPAN {m['span'] * 100:.0f}%  "
                f"AREA {m['area'] * 100:.0f}%", margin, y, 2, INK)
        y += 26
        # the row with this candidate in our slot (normal state)
        row = from_row.copy()
        b, pc = button(prims, 80, False, bg=(0, 0, 0))
        reg = row[20:100, 112:192]
        row[20:100, 112:192] = reg * (1 - pc) + b
        cv.paste(row, margin, y)
        y += rh + 8
        # tiles: 80 normal, 80 selected, 40 normal, 40 selected, 40 normal x3, 40 selected x3
        x = margin
        for size, sel, z, lab in ((80, False, 1, "80 PX 4K"), (80, True, 1, "80 SELECTED"),
                                  (40, False, 1, "40 PX 1080P"), (40, True, 1, "40 SELECTED"),
                                  (40, False, 3, "40 PX ZOOM X3"), (40, True, 3, "40 SELECTED X3")):
            img, _ = button(prims, size, sel, bg=(246, 247, 248))
            img = zoom(img, z)
            s = size * z
            cv.paste(img, x, y + (120 - s) // 2)
            cv.text(lab, x, y + 126, 1, INK)
            x += max(s, 84) + 22
        # raw glyph on dark gray (what the PNG holds)
        g = render_alpha(prims, 120)[..., None]
        cv.paste(np.asarray((60, 66, 74), np.float64) * (1 - g) + 255 * g, x, y)
        cv.text("THE GLYPH ITSELF", x, y + 126, 1, INK)
        y += 120 + 30
    if rejected:
        cv.text("REJECTED: (A) AS AN OUTLINE - AT 40 PX IT READ AS A LINE CHART, A LETTER OR AN M", margin, y, 2, INK)
        y += 26
        x = margin
        for name, prims in rejected.items():
            for size, z in ((80, 1), (40, 2)):
                img, _ = button(prims, size, False, bg=(246, 247, 248))
                s_ = size * z
                cv.paste(zoom(img, z), x, y + (120 - s_) // 2)
                x += s_ + 14
            cv.text(name.upper(), x - 80 - 14 - 94, y + 126, 1, INK)
            x += 40
        y += 120 + 30
    write_png(path, cv.png())


def make_detail(cands, path):
    """Review aid (not a deliverable). Per candidate it draws the 256 px glyph on gray, the 80 px
    button x2 and the 40 px button x4, normal and selected."""
    keys = list(cands)
    cell_h = 300
    cv = Canvas(256 + 3 * 180 + 4 * 16, 10 + cell_h * len(keys), (246, 247, 248))
    y = 10
    for key in keys:
        title, prims, m = cands[key]
        cv.text(f"{key} {title}", 10, y, 2, INK)
        g = render_alpha(prims, 256)[..., None]
        cv.paste(np.asarray((60, 66, 74), np.float64) * (1 - g) + 255 * g, 10, y + 22)
        x = 10 + 256 + 16
        for size, z, sel in ((80, 2, False), (40, 4, False), (40, 4, True)):
            img, _ = button(prims, size, sel, bg=(246, 247, 248))
            cv.paste(zoom(img, z), x, y + 22)
            x += 160 + 20
        y += cell_h
    write_png(path, cv.png())


def parse_svg(text):
    """Reads back the five primitive forms that glyph_svg writes (a writer check, not an SVG engine)."""
    import re
    prims = []
    for tag in re.findall(r"<(path|circle|rect)\b([^>]*)/>", text):
        kind, attrs = tag
        at = dict(re.findall(r'([a-z-]+)="([^"]*)"', attrs))
        if kind == "path":
            nums = [float(v) for v in re.findall(r"-?\d+(?:\.\d+)?", at["d"])]
            pts = list(zip(nums[0::2], nums[1::2]))
            if at.get("fill") == "none":
                prims.append(("line", pts, float(at["stroke-width"])))
            else:
                prims.append(("fill", pts))
        elif kind == "circle":
            cx, cy, r = float(at["cx"]), float(at["cy"]), float(at["r"])
            if at.get("fill") == "none":
                prims.append(("ring", cx, cy, r, float(at["stroke-width"])))
            else:
                prims.append(("disk", cx, cy, r))
        else:
            x, y, w, h, rx = (float(at[k]) for k in ("x", "y", "width", "height", "rx"))
            prims.append(("rect", x, y, x + w, y + h, rx))
    return prims


def read_png(path):
    data = open(path, "rb").read()
    i, idat = 8, b""
    while i < len(data):
        ln = struct.unpack(">I", data[i:i + 4])[0]
        typ, body = data[i + 4:i + 8], data[i + 8:i + 8 + ln]
        i += 12 + ln
        if typ == b"IHDR":
            w, h, bd, ct = struct.unpack(">IIBB", body[:10])
        elif typ == b"IDAT":
            idat += body
    ch = {2: 3, 6: 4}[ct]
    raw = zlib.decompress(idat)
    stride = w * ch
    out = np.zeros((h, stride), np.int64)
    prev = np.zeros(stride, np.int64)
    p = 0
    for r in range(h):
        f = raw[p]
        line = np.frombuffer(raw[p + 1:p + 1 + stride], np.uint8).astype(np.int64)
        p += 1 + stride
        if f == 0:
            cur = line.copy()
        elif f == 2:
            cur = (line + prev) & 255
        else:
            cur = np.zeros(stride, np.int64)
            for x in range(stride):
                a = cur[x - ch] if x >= ch else 0
                b = prev[x]
                c = prev[x - ch] if x >= ch else 0
                if f == 1:
                    v = line[x] + a
                elif f == 3:
                    v = line[x] + ((a + b) >> 1)
                else:
                    pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                    v = line[x] + (a if (pa <= pb and pa <= pc) else (b if pb <= pc else c))
                cur[x] = v & 255
        out[r] = cur
        prev = cur
    return out.reshape(h, w, ch).astype(np.uint8)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--row", default="")
    ap.add_argument("--only", default="")
    ap.add_argument("--detail", action="store_true")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    only = [s.strip() for s in args.only.split(",") if s.strip()]
    cands = {}
    for key, (title, fn) in CANDIDATES.items():
        if only and key not in only:
            continue
        prims = fn()
        img, a = glyph_png(prims)
        write_png(os.path.join(args.out, f"launcher-{key}.png"), img)
        with open(os.path.join(args.out, f"launcher-{key}.svg"), "w", encoding="ascii", newline="\n") as f:
            f.write(glyph_svg(prims, title))
        m = measure(a)
        cands[key] = (title, prims, m)
        # the SVG says exactly what the PNG shows: parse it back, re-render, compare
        with open(os.path.join(args.out, f"launcher-{key}.svg"), encoding="ascii") as f:
            back = parse_svg(f.read())
        diff = float(np.abs(render_alpha(back, CANVAS) - a).max())
        strokes = [q[2] for q in prims if q[0] == "line"]
        print(f"   svg round trip: {len(back)} shapes, max alpha difference {diff:.3f} "
              f"({'OK' if diff < 0.05 else 'MISMATCH'}); strokes {min(strokes):.0f}-{max(strokes):.0f} px of 256 "
              f"= {min(strokes) * 72 / 256:.1f}-{max(strokes) * 72 / 256:.1f} px at 4K, "
              f"{min(strokes) * 36 / 256:.1f}-{max(strokes) * 36 / 256:.1f} px at 1080p", flush=True)
        print(f"{key} {title:24s} bbox {m['bbox']}  span {m['span'] * 100:.1f}% of plate  area "
              f"{m['area'] * 100:.1f}% of plate  center ({m['centre'][0]:.0f},{m['centre'][1]:.0f})  "
              + gap_text(m), flush=True)
    if args.row:
        rej = {} if only else {k: fn() for k, fn in REJECTED.items()}
        make_sheet(args.row, cands, os.path.join(args.out, "sheet.png"), rej)
        print("sheet written")
    if args.detail:
        make_detail(cands, os.path.join(args.out, "detail.png"))


if __name__ == "__main__":
    main()
