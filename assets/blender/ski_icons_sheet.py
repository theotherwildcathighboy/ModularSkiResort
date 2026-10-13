"""
Review sheet for the Build-strip icons drawn by ski_icons.py (the preview gate of docs/PIPELINE.md
section 2 for icons): the nine icons large, the hub's Build strip mocked at the real 1080p sizes
(text scale 100 / 130 / 150 %: icons 30 / 39 / 45 px) with an active (blue) tile, a locked tile
and the step digits, the 30 px strip in greyscale, before / after against the installed icons,
the bilinear-without-mipmaps worst case, 4K (60 px), and x3 zooms. Also prints the mean greyscale
difference between the four run icons at 30 px (the "tell the run types apart" check).

The strip mock follows hub.module.scss (.buildFooter / .buildGroup / .buildCell / .buildTile /
.buildIcon / .buildStripe / .buildStep / .buildLock) and the installed UI colours. The game's own
downscale filter is unverified: the sheet shows an area average and a bilinear worst case.

    "<blender>\\<ver>\\python\\bin\\python.exe" ski_icons_sheet.py --out <dir> [--live <root>]

--live = an ImportedData root holding the installed icons for the before-strip (default: the
repo's gamedata-mirror/ImportedData; '' = no before-strip). Writes <dir>/sheet.png only.
Ported 2026-10-04 from the session generator of the style-E round (ski_icons_e2.py).

    ... ski_icons_sheet.py --launcher --out <dir> [--before <png>]

--launcher (Round F7 E1, 2026-10-04 night) writes the hub launcher's review sheet instead
(docs/research/art/icons-2026-10-04/launcher/sheet.png): SkiResortTab.png large and next to the
nine tiles, the floating button at 1080p (button 40 px, icon 33 px) and 4K (80 / 68 px) as
measured in docs/research/field/2026-10-04/20.23.05.92.png, area average and bilinear, greyscale,
zoomed, before / after. --before = the old launcher PNG (default: the retired copy).

    ... ski_icons_sheet.py --contact --out <dir> [--name build-strip.png]

--contact (round 2, 2026-10-11; docs/research/art/assets-2026-10/build/icons-round-2.md) writes the
contact sheet of all 25 Build-strip tiles instead: every tile at 64 px on an 88 px hub tile in
BUILD_GROUPS order with step digits and run stripes, the round-2 tiles labelled in amber; the
strip at the real 1080p size (30 px) as the pickers draw it for five lodge-tier / lift-type pairs,
actual size and zoomed x3; and the 16 round-2 tiles at 160 px.
"""

import argparse
import os
import struct
import sys
import zlib

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ski_icons as si  # noqa: E402

# UI colours (measured in docs/research/field/2026-10-03b/20.45.49.96.png, 4K; installed index.css)
FOOTER_BG = (21, 32, 44)
TILE_BG = (53, 80, 95)
TILE_ACTIVE = (30, 131, 170)        # --selectedColor #1e83aa
TILE_ACCENT = (75, 195, 241)        # --accentColorNormal #4bc3f1
LOCKED = (211, 143, 7)              # --lockedColor #d38f07
TEXT = (240, 251, 255)              # --normalTextColor #F0FBFF

STRIP_ORDER = si.E1_NINE          # the default sheet reviews the nine of the 2026-10-04 round
STEP = si.STEP
GROUPS = [2, 3, 4]
KIND = {"SkiRunNaturalA": "natural", "SkiRunGroomedA": "groomed",
        "SkiRunSnowmakingA": "snowmaking", "SkiRunNightA": "night"}
LABEL = {"SkiResortGate": "WELCOME SIGN", "SkiAFrameDayLodge": "LODGE",
         "SkiBaseStationBuilding": "BASE STATION", "SkiTopStationBuilding": "TOP STATION",
         "SkiChairliftLine": "LIFT LINE", "SkiRunNaturalA": "NATURAL", "SkiRunGroomedA": "GROOMED",
         "SkiRunSnowmakingA": "SNOWMAKING", "SkiRunNightA": "NIGHT"}

DEFAULT_TITLE = "BUILD-STRIP ICONS, STYLE E - REVIEW SHEET (ASSETS/BLENDER/SKI_ICONS.PY)"
LIVE_DIR = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                                         "gamedata-mirror", "ImportedData"))
TAB_ICON = "SkiResortCategory/SkiResortTab.png"     # the lift tile's icon before 2026-10-04
LAUNCHER_BEFORE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                                                "..", "gamedata-mirror", "retired",
                                                "2026-10-04-launcher-pre-edit", "SkiResortTab.png"))
# The launcher's surroundings, measured in docs/research/field/2026-10-04/20.23.05.92.png (4K):
# floating button 80 px, colour (231, 252, 254), the icon 68 px inside it; snowy terrain behind.
BUTTON_BG = (231, 252, 254)
TERRAIN_BG = (227, 229, 225)
NEIGHBOUR = (0, 247, 253)           # the other mods' buttons on the same row (plain squares here)


def decode_png(path):
    """Minimal PNG reader (8 bit, colour type 2 or 6, not interlaced) for the sheet's before-strip
    of the live icons. Returns RGBA uint8."""
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", path
    pos, idat, w = 8, b"", None
    while pos < len(data):
        ln = struct.unpack(">I", data[pos:pos + 4])[0]
        typ, body = data[pos + 4:pos + 8], data[pos + 8:pos + 8 + ln]
        pos += 12 + ln
        if typ == b"IHDR":
            w, h, bd, ct, _, _, il = struct.unpack(">IIBBBBB", body)
            assert bd == 8 and il == 0 and ct in (2, 6), (path, bd, ct, il)
        elif typ == b"IDAT":
            idat += body
        elif typ == b"IEND":
            break
    c = 4 if ct == 6 else 3
    raw = zlib.decompress(idat)
    stride = w * c
    out = np.zeros((h, stride), dtype=np.int64)
    prev = [0] * stride
    p = 0
    for r in range(h):
        f = raw[p]
        line = raw[p + 1:p + 1 + stride]
        p += 1 + stride
        cur = [0] * stride
        for i in range(stride):
            a = cur[i - c] if i >= c else 0
            b = prev[i]
            if f == 0:
                pr = 0
            elif f == 1:
                pr = a
            elif f == 2:
                pr = b
            elif f == 3:
                pr = (a + b) >> 1
            else:
                cc = prev[i - c] if i >= c else 0
                pa, pb, pc = abs(b - cc), abs(a - cc), abs(a + b - 2 * cc)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else cc)
            cur[i] = (line[i] + pr) & 255
        out[r] = cur
        prev = cur
    img = out.reshape(h, w, c).astype(np.uint8)
    if c == 3:
        img = np.concatenate([img, np.full((h, w, 1), 255, np.uint8)], axis=2)
    return img


def live_files(root):
    """Installed icon per tile; the lift tile falls back to SkiResortTab.png where no
    SkiChairliftLine.png is installed yet. Returns (files, lift_note)."""
    files = {n: f"SkiResortIcons/{n}.png" for n in STRIP_ORDER}
    note = "SKIRESORTICONS/SKICHAIRLIFTLINE.PNG"
    if not os.path.exists(os.path.join(root, files["SkiChairliftLine"])):
        files["SkiChairliftLine"] = TAB_ICON
        note = "SKIRESORTCATEGORY/SKIRESORTTAB.PNG"
    return files, note


def load_live(root):
    files, note = live_files(root)
    return {n: decode_png(os.path.join(root, rel)) for n, rel in files.items()}, note


# ---------------------------------------------------------------------------------------------
# Resampling, canvas, pixel font, hub strip mock
# ---------------------------------------------------------------------------------------------
def area_weights(n_in, n_out):
    w = np.zeros((n_out, n_in))
    sc = n_in / n_out
    for o in range(n_out):
        a, b = o * sc, (o + 1) * sc
        for i in range(int(np.floor(a)), min(int(np.ceil(b)), n_in)):
            w[o, i] = max(0.0, min(b, i + 1) - max(a, i))
        w[o] /= w[o].sum()
    return w


def bilin_weights(n_in, n_out):
    """Bilinear sampling at the output pixel centres, no mipmaps (the worst case the field
    screenshot pointed to; game filter unverified)."""
    w = np.zeros((n_out, n_in))
    sc = n_in / n_out
    for o in range(n_out):
        x = (o + 0.5) * sc - 0.5
        i0 = int(np.floor(x))
        f = x - i0
        for i, ww in ((i0, 1 - f), (i0 + 1, f)):
            w[o, min(max(i, 0), n_in - 1)] += ww
    return w


def resize_premul(img, size, kind="area"):
    f = img.astype(np.float64)
    pm = np.concatenate([f[..., :3] * f[..., 3:4] / 255.0, f[..., 3:4]], axis=2)
    wf = area_weights if kind == "area" else bilin_weights
    wy = wf(img.shape[0], size)
    wx = wf(img.shape[1], size)
    tmp = np.tensordot(wy, pm, axes=(1, 0))
    return np.tensordot(tmp, wx, axes=(1, 1)).transpose(0, 2, 1)


class Canvas:
    def __init__(self, w, h, bg):
        self.rgb = np.zeros((h, w, 3)) + np.asarray(bg, np.float64)
        self.w, self.h = w, h

    def fill_sdf(self, sdf_fn, col, a=1.0, box=None):
        bx0, by0, bx1, by1 = box if box else (0, 0, self.w, self.h)
        bx0, by0 = max(int(np.floor(bx0)) - 2, 0), max(int(np.floor(by0)) - 2, 0)
        bx1, by1 = min(int(np.ceil(bx1)) + 2, self.w), min(int(np.ceil(by1)) + 2, self.h)
        # 4x4 supersampled coverage for small glyphs
        k = 4
        ys, xs = np.mgrid[by0 * k:by1 * k, bx0 * k:bx1 * k]
        d = sdf_fn((xs + 0.5) / k, (ys + 0.5) / k)
        c = np.clip(0.5 - d * k, 0, 1).reshape(by1 - by0, k, bx1 - bx0, k).mean(axis=(1, 3))
        c = (c * a)[..., None]
        reg = self.rgb[by0:by1, bx0:bx1]
        self.rgb[by0:by1, bx0:bx1] = reg * (1 - c) + np.asarray(col, np.float64) * c

    def rect(self, x0, y0, x1, y1, r, col, a=1.0):
        self.fill_sdf(lambda xs, ys: si.sd_round_rect(x0, y0, x1, y1, r, x=xs, y=ys), col, a,
                      box=(x0, y0, x1, y1))

    def ring_rect(self, x0, y0, x1, y1, r, w, col):
        self.fill_sdf(lambda xs, ys: np.abs(si.sd_round_rect(x0 + w / 2, y0 + w / 2, x1 - w / 2,
                                                             y1 - w / 2, max(r - w / 2, 0.1),
                                                             x=xs, y=ys)) - w / 2, col,
                      box=(x0, y0, x1, y1))

    def paste_premul(self, pm, x, y, opacity=1.0):
        h, w, _ = pm.shape
        a = pm[..., 3:4] / 255.0 * opacity
        reg = self.rgb[y:y + h, x:x + w]
        self.rgb[y:y + h, x:x + w] = reg * (1 - a) + pm[..., :3] * opacity

    def paste_rgb(self, img, x, y):
        h, w, _ = img.shape
        self.rgb[y:y + h, x:x + w] = img

    def text(self, s, x, y, scale, col):
        for ch in s.upper():
            rows = FONT.get(ch, FONT[" "])
            for r, bits in enumerate(rows):
                for cidx, b in enumerate(bits):
                    if b == "1":
                        self.rgb[y + r * scale:y + (r + 1) * scale,
                                 x + cidx * scale:x + (cidx + 1) * scale] = col
            x += 6 * scale
        return x

    def png(self):
        return np.clip(np.rint(self.rgb), 0, 255).astype(np.uint8)


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
    ";": ["00000", "01100", "01100", "00000", "01100", "00100", "01000"],
    "+": ["00000", "00100", "00100", "11111", "00100", "00100", "00000"],
    "=": ["00000", "00000", "11111", "00000", "11111", "00000", "00000"],
    "_": ["00000", "00000", "00000", "00000", "00000", "00000", "11111"],
}

# Vector step digits (a plain sans; cap height h, stroke 0.15 h), drawn at the real XS font size.
DIGITS = {
    "1": [[(0.18, 0.20), (0.46, 0.0), (0.46, 1.0)]],
    "2": [[(0.04, 0.22), (0.14, 0.06), (0.34, 0.0), (0.54, 0.05), (0.64, 0.22), (0.60, 0.40),
           (0.44, 0.58), (0.04, 1.0), (0.68, 1.0)]],
    "3": [[(0.04, 0.10), (0.22, 0.0), (0.44, 0.0), (0.60, 0.08), (0.64, 0.24), (0.54, 0.40),
           (0.32, 0.47)],
          [(0.32, 0.47), (0.56, 0.54), (0.68, 0.72), (0.62, 0.90), (0.42, 1.0), (0.20, 1.0),
           (0.02, 0.90)]],
}


def draw_digit(cv, ch, x, y, h, col, a=1.0):
    w = 0.15 * h
    strokes = [[(x + px * h, y + py * h) for px, py in st] for st in DIGITS[ch]]

    def f(xs, ys):
        d = np.full(xs.shape, np.inf)
        for st in strokes:
            for i in range(len(st) - 1):
                d = np.minimum(d, si.sd_segment(st[i][0], st[i][1], st[i + 1][0], st[i + 1][1], w,
                                                x=xs, y=ys))
        return d

    cv.fill_sdf(f, col, a, box=(x - 2, y - 2, x + 0.8 * h + 2, y + h + 2))


def draw_lock(cv, x, y, size, col):
    """The game's Media/Glyphs/Lock.svg shape (32 px box), drawn at size x size from (x, y)."""
    s = size / 32.0

    def f(xs, ys):
        u, v = (xs - x) / s, (ys - y) / s
        body = si.sd_round_rect(3.5, 14.5, 28.7, 29.0, 1.6, x=u, y=v)
        ring = np.abs(np.hypot(u - 16.1, v - 10.9) - 6.3) - 2.25
        ring = np.where(v < 14.5, ring, np.inf)
        return np.minimum(body, ring) * s

    cv.fill_sdf(f, col, 1.0, box=(x, y, x + size, y + size))


def strip_geometry(s, u):
    panel = (440.0 * (1.0 / 3.0 + s / 1.5) + 300.0 * (s - 1.0) + 15.0) * u
    inner = panel - 16.0 * u
    cell = (inner - 16.0 * u) / 9.0
    return panel, cell


def draw_strip(cv, icons, x0, y0, s, u, active=None, locked=None, kind_rs="area"):
    """The nine tiles as hub.module.scss draws them. Returns the height used."""
    panel, cell = strip_geometry(s, u)
    tile_h = 44.0 * s * u
    cv.rect(x0, y0, x0 + panel, y0 + tile_h + 12 * u, 0, FOOTER_BG)
    cx = x0 + 8 * u
    ty = y0 + 6 * u
    k = 0
    for gi, n in enumerate(GROUPS):
        if gi:
            cx += 8 * u
        for _ in range(n):
            name = STRIP_ORDER[k]
            k += 1
            bx0, bx1 = cx + 2 * u, cx + cell - 2 * u
            px0, py0, px1, py1 = bx0 + 2 * u, ty + 2 * u, bx1 - 2 * u, ty + tile_h - 2 * u
            on = name == active
            if on:
                cv.rect(bx0, ty, bx1, ty + tile_h, 4 * u, TILE_ACCENT)
            cv.rect(px0, py0, px1, py1, 2 * u, TILE_ACTIVE if on else TILE_BG)
            isz = int(round(30 * s * u))
            ix = int(round((px0 + px1) / 2 - isz / 2))
            iy = int(round((py0 + py1) / 2 - isz / 2))
            cv.paste_premul(resize_premul(icons[name], isz, kind_rs), ix, iy,
                            0.35 if name == locked else 1.0)
            kind = KIND.get(name)
            if kind:
                sx0, sx1, sy1 = px0 + 6 * u, px1 - 6 * u, py1 - 2 * u
                sy0 = sy1 - 4 * u
                if kind == "groomed":
                    cv.rect(sx0, sy0, sx1, sy1, 2 * u, si.HUB_WHITE)
                elif kind == "night":
                    cv.rect(sx0, sy0, sx1, sy1, 2 * u, si.HUB_AMBER)
                elif kind == "snowmaking":
                    cv.ring_rect(sx0, sy0, sx1, sy1, 2 * u, 1 * u, si.HUB_CYAN)
                else:
                    mid = (sx0 + sx1) / 2
                    cv.rect(sx0, sy0, mid - 3 * u, sy1, 2 * u, si.HUB_WHITE, 0.7)
                    cv.rect(mid + 3 * u, sy0, sx1, sy1, 2 * u, si.HUB_WHITE, 0.7)
            if name in STEP:
                fs = 12.0 * s * u                       # --fontSizeXS at this text scale
                cap = 0.70 * fs
                draw_digit(cv, STEP[name], px0 + 4 * u + 0.06 * fs, py0 + 1 * u + 0.30 * fs, cap,
                           TEXT, 0.85)
            if name == locked:
                ls = 12.0 * s * u
                draw_lock(cv, px1 - 3 * u - ls, py0 + 3 * u, ls, LOCKED)
            cx += cell
    return tile_h + 12 * u


def to_grey(im):
    g = im @ np.array([0.2126, 0.7152, 0.0722])
    return np.repeat(g[..., None], 3, axis=2)


def zoom(im, z):
    return np.repeat(np.repeat(im, z, axis=0), z, axis=1)


def make_sheet(icons, path, live=None, live_note="", title=DEFAULT_TITLE):
    W = 1640
    rows_strip = [(1.0, 1, "SkiRunSnowmakingA", "SkiChairliftLine"),
                  (1.3, 1, "SkiBaseStationBuilding", "SkiRunNightA"),
                  (1.5, 1, "SkiRunGroomedA", "SkiTopStationBuilding")]
    H = 2600
    cv = Canvas(W, H, (34, 38, 46))
    y = 16
    cv.text(title, 20, y, 3, (236, 240, 244))
    y += 34
    cv.text("256 PX PICTURES ON A ROUNDED PLATE (10..245, R45); EVERY MARK KEEPS 22+ PX TO THE "
            "PLATE EDGE. HERE AT 160 PX ON THE TILE COLOUR.", 20, y, 2, (180, 192, 204))
    y += 26
    # 1) the nine icons large
    big = 160
    x = 20
    for name in STRIP_ORDER:
        t = Canvas(big + 16, big + 16, TILE_BG)
        t.paste_premul(resize_premul(icons[name], big), 8, 8)
        cv.paste_rgb(t.rgb, x, y)
        cv.text(LABEL[name], x + 2, y + big + 24, 2, (214, 222, 230))
        x += big + 16 + 4
    y += big + 16 + 40
    # 2) the strip at the real 1080p sizes
    cv.text("BUILD STRIP AT REAL 1080P SIZE ON THE HUB FOOTER (ICON = 30 REM X TEXT SCALE; "
            "AREA-AVERAGE DOWNSCALE, GAME FILTER UNVERIFIED)", 20, y, 2, (236, 240, 244))
    y += 24
    strip_imgs = {}
    for s, u, act, lck in rows_strip:
        isz = int(round(30 * s * u))
        cv.text(f"TEXT {int(round(s * 100))}%  ICON {isz} PX  TILE {int(round(44 * s))} PX  "
                f"ACTIVE: {LABEL[act]}  LOCKED: {LABEL[lck]}", 20, y, 2, (190, 200, 212))
        y += 22
        sy0 = y
        h = draw_strip(cv, icons, 20, y, s, u, active=act, locked=lck)
        panel, _ = strip_geometry(s, u)
        strip_imgs[s] = (sy0, int(np.ceil(h)), int(np.ceil(panel)))
        y += int(np.ceil(h)) + 16
    # 3) greyscale copy of the 30 px strip, actual size
    sy0, sh, sw = strip_imgs[1.0]
    cv.text("THE 30 PX STRIP IN GREYSCALE (LUMINANCE ONLY), ACTUAL SIZE", 20, y, 2, (236, 240, 244))
    y += 22
    grey = to_grey(cv.rgb[sy0:sy0 + sh, 20:20 + sw])
    cv.paste_rgb(grey, 20, y)
    y += sh + 20
    # 3b) before / after at 30 px, plain tiles (no active, no lock): the installed icons against
    # the set drawn now
    ba = None
    if live:
        cv.text("BEFORE: LIVE ICONS, 30 PX", 20, y, 2, (236, 240, 244))
        cv.text("AFTER: THIS SET, 30 PX", 20 + sw + 40, y, 2, (236, 240, 244))
        y += 22
        draw_strip(cv, live, 20, y, 1.0, 1)
        draw_strip(cv, icons, 20 + sw + 40, y, 1.0, 1)
        ba = y
        cv.text("(LIVE = GAMEDATA-MIRROR/IMPORTEDDATA/SKIRESORTICONS; THE LIFT TILE SHOWS "
                f"{live_note})", 20, y + sh + 4, 2, (170, 182, 196))
        y += sh + 42
    cv.text("WORST CASE: BILINEAR WITHOUT MIPMAPS AT 30 PX (FIELD MATCH, UNVERIFIED)", 20, y, 2,
            (236, 240, 244))
    y += 22
    draw_strip(cv, icons, 20, y, 1.0, 1, kind_rs="bilin")
    y += sh + 20
    # 4) 4K, text 100 % (60 px icons)
    cv.text("4K, TEXT 100%  ICON 60 PX  TILE 88 PX  ACTIVE: LODGE  LOCKED: NATURAL", 20, y, 2,
            (190, 200, 212))
    y += 22
    h = draw_strip(cv, icons, 20, y, 1.0, 2, active="SkiAFrameDayLodge", locked="SkiRunNaturalA")
    y += int(np.ceil(h)) + 24
    # 5) the 30 px strip zoomed x3 (nearest), colour and grey
    z = 3
    col = cv.rgb[sy0:sy0 + sh, 20:20 + sw]
    cv.text("THE 30 PX STRIP ZOOMED X3 (NEAREST NEIGHBOUR): COLOUR, THEN GREY", 20, y, 2,
            (236, 240, 244))
    y += 22
    cv.paste_rgb(zoom(col, z), 20, y)
    y += sh * z + 10
    cv.paste_rgb(zoom(to_grey(col), z), 20, y)
    y += sh * z + 16
    if ba is not None:
        cv.text("BEFORE (LIVE) AT 30 PX ZOOMED X3, FOR COMPARISON", 20, y, 2, (236, 240, 244))
        y += 22
        cv.paste_rgb(zoom(cv.rgb[ba:ba + sh, 20:20 + sw].copy(), z), 20, y)
        y += sh * z + 16
    cv.text("SNOWMAKING ACTIVE (BLUE TILE), LIFT LINE LOCKED (35 % ICON + LOCK GLYPH). STEP DIGITS "
            "DRAWN AT THE XS FONT SIZE, 85 % OPACITY.", 20, y, 2, (170, 182, 196))
    y += 28
    si.write_png(path, cv.png()[:y])


def launcher_button(icon, btn, isz, kind_rs="area"):
    """The floating launcher button as the field screenshot shows it: a light rounded square of
    btn px, the icon isz px centred in it. Returns an RGB float image (btn x btn)."""
    b = Canvas(btn, btn, TERRAIN_BG)
    b.rect(0, 0, btn, btn, btn * 0.1, BUTTON_BG)
    o = (btn - isz) // 2
    b.paste_premul(resize_premul(icon, isz, kind_rs), o, o)
    return b.rgb


def launcher_row(cv, icon, x, y, btn, isz, kind_rs="area"):
    """The game's top-left button row around the launcher (neighbours as plain squares)."""
    gap = max(2, btn // 8)
    w = 4 * btn + 5 * gap
    cv.rect(x, y, x + w, y + btn + 2 * gap, 0, TERRAIN_BG)
    bx = x + gap
    for k in range(4):
        if k == 1:
            cv.paste_rgb(launcher_button(icon, btn, isz, kind_rs), bx, y + gap)
        else:
            cv.rect(bx, y + gap, bx + btn, y + gap + btn, btn * 0.1, NEIGHBOUR)
        bx += btn + gap
    return w, btn + 2 * gap


def make_launcher_sheet(icons, launcher, before, path):
    W = 1840
    cv = Canvas(W, 1900, (34, 38, 46))
    hd, sub, note = (236, 240, 244), (190, 200, 212), (170, 182, 196)
    y = 16
    cv.text("HUB LAUNCHER ICON, STYLE E - REVIEW SHEET (ASSETS/BLENDER/SKI_ICONS.PY, ROUND F7 E1)",
            20, y, 3, hd)
    y += 34
    cv.text("SKIRESORTTAB.PNG, CID AED3907873AA4D74BB420190D3044E78 KEPT (HUBLAUNCHER.TSX HUB_ICON "
            "UNCHANGED).", 20, y, 2, sub)
    y += 22
    cv.text("A SNOWY PEAK, ONE RED CHAIR ON A DIAGONAL CABLE, ON THE STYLE-E PLATE; EVERY MARK KEEPS "
            "22 PX TO THE PLATE EDGE.", 20, y, 2, sub)
    y += 30
    # 1) before / after at 256 px, actual size, on the button colour
    cv.text("256 PX, ACTUAL SIZE, ON THE BUTTON COLOUR", 20, y, 2, hd)
    y += 22
    x = 20
    for lab, im in (("BEFORE (RETIRED)", before), ("AFTER", launcher)):
        if im is None:
            continue
        t = Canvas(272, 272, BUTTON_BG)
        t.paste_premul(resize_premul(im, 256), 8, 8)
        cv.paste_rgb(t.rgb, x, y)
        cv.text(lab, x + 2, y + 280, 2, (214, 222, 230))
        x += 272 + 24
    y += 272 + 44
    # 2) next to the nine Build-strip tiles at 160 px on the tile colour
    cv.text("NEXT TO THE NINE BUILD-STRIP TILES, 160 PX ON THE TILE COLOUR", 20, y, 2, hd)
    y += 22
    big = 160
    x = 20
    for name in ["SkiResortTab"] + STRIP_ORDER:
        t = Canvas(big + 16, big + 16, TILE_BG)
        t.paste_premul(resize_premul(launcher if name == "SkiResortTab" else icons[name], big), 8, 8)
        cv.paste_rgb(t.rgb, x, y)
        cv.text("LAUNCHER" if name == "SkiResortTab" else LABEL[name], x + 2, y + big + 24, 2,
                (214, 222, 230))
        x += big + 16 + 4
    y += big + 16 + 40
    # 3) 1080p: the launcher on the top-left row (button 40, icon 33) beside the 30 px strip
    cv.text("1080P: FLOATING BUTTON 40 PX, ICON 33 PX, ON THE TOP-LEFT ROW (NEIGHBOURS = OTHER MODS, "
            "PLAIN), BESIDE THE 30 PX BUILD STRIP", 20, y, 2, hd)
    y += 22
    rows = [("AFTER, AREA AVERAGE", launcher, "area"), ("AFTER, BILINEAR NO MIPMAPS", launcher,
                                                        "bilin")]
    if before is not None:
        rows.insert(0, ("BEFORE", before, "area"))
    row_y = {}
    for lab, im, kind in rows:
        cv.text(lab, 20, y + 18, 2, sub)
        w, h = launcher_row(cv, im, 420, y, 40, 33, kind)
        draw_strip(cv, icons, 420 + w + 30, y + 2, 1.0, 1, kind_rs=kind)
        row_y[lab] = (y, w, h)
        y += h + 14
    y += 10
    # 4) 4K: button 80, icon 68 (as measured)
    cv.text("4K: BUTTON 80 PX, ICON 68 PX (MEASURED IN FIELD SCREENSHOT 20.23.05.92), BESIDE THE "
            "60 PX STRIP", 20, y, 2, hd)
    y += 22
    w4, h4 = launcher_row(cv, launcher, 20, y, 80, 68)
    draw_strip(cv, icons, 20 + w4 + 30, y + 4, 1.0, 2)
    if before is not None:
        launcher_row(cv, before, 20, y + h4 + 12, 80, 68)
        cv.text("BEFORE", 20 + w4 + 30, y + h4 + 12 + 36, 2, sub)
        h4 = 2 * h4 + 12
    y += h4 + 24
    # 5) greyscale, then zoomed x6 (nearest): before, after, after bilinear, after grey
    cv.text("THE 33 PX BUTTON IN GREYSCALE (LUMINANCE ONLY), ACTUAL SIZE: BEFORE, AFTER", 20, y, 2,
            hd)
    y += 22
    x = 20
    for im in ([before] if before is not None else []) + [launcher]:
        cv.paste_rgb(to_grey(launcher_button(im, 40, 33)), x, y)
        x += 56
    y += 40 + 20
    cv.text("THE 33 PX BUTTON ZOOMED X6 (NEAREST): BEFORE, AFTER, AFTER BILINEAR, AFTER GREY", 20, y,
            2, hd)
    y += 22
    tiles = []
    if before is not None:
        tiles.append(launcher_button(before, 40, 33))
    tiles += [launcher_button(launcher, 40, 33), launcher_button(launcher, 40, 33, "bilin"),
              to_grey(launcher_button(launcher, 40, 33))]
    x = 20
    for t in tiles:
        cv.paste_rgb(zoom(t, 6), x, y)
        x += 240 + 24
    y += 240 + 20
    cv.text("BUTTON AND ICON SIZES FROM THE 4K FIELD SCREENSHOT HALVED; THE GAME DOWNSCALE FILTER IS "
            "UNVERIFIED (AREA AVERAGE AND A BILINEAR WORST CASE SHOWN).", 20, y, 2, note)
    y += 28
    si.write_png(path, cv.png()[:y])


# ---------------------------------------------------------------------------------------------
# Contact sheet of all 25 tiles (--contact; round 2, 2026-10-11)
# ---------------------------------------------------------------------------------------------
CONTACT_LABEL = {
    "SkiResortGate": "WELCOME SIGN", "SkiAFrameDayLodge": "A-FRAME LODGE",
    "SkiLodgeHut": "MOUNTAIN HUT", "SkiLodgeMid": "CHALET LODGE", "SkiLodgeSummit": "SUMMIT LODGE",
    "SkiLodgeGrand": "GRAND LODGE", "SkiSchool": "SKI SCHOOL", "SkiPatrolHut": "PATROL HUT",
    "SkiPatrolHQ": "PATROL HQ",
    "SkiBaseStationBuilding": "FG BASE", "SkiTopStationBuilding": "FG TOP",
    "SkiChairliftLine": "FG LINE",
    "SkiDQBaseStationBuilding": "DQ BASE", "SkiDQTopStationBuilding": "DQ TOP",
    "SkiDetachableLine": "DQ LINE",
    "SkiGondolaBaseStationBuilding": "GONDOLA BASE", "SkiGondolaTopStationBuilding": "GONDOLA TOP",
    "SkiGondolaLine": "GONDOLA LINE",
    "SkiTramBaseStationBuilding": "TRAM BASE", "SkiTramTopStationBuilding": "TRAM TOP",
    "SkiTramLine": "TRAM LINE",
    "SkiRunNaturalA": "NATURAL", "SkiRunGroomedA": "GROOMED", "SkiRunSnowmakingA": "SNOWMAKING",
    "SkiRunNightA": "NIGHT",
}
LODGES = ["SkiAFrameDayLodge", "SkiLodgeHut", "SkiLodgeMid", "SkiLodgeSummit", "SkiLodgeGrand"]
CIVIC = ["SkiSchool", "SkiPatrolHut", "SkiPatrolHQ"]
LIFT_TYPES = [["SkiBaseStationBuilding", "SkiTopStationBuilding", "SkiChairliftLine"],
              ["SkiDQBaseStationBuilding", "SkiDQTopStationBuilding", "SkiDetachableLine"],
              ["SkiGondolaBaseStationBuilding", "SkiGondolaTopStationBuilding", "SkiGondolaLine"],
              ["SkiTramBaseStationBuilding", "SkiTramTopStationBuilding", "SkiTramLine"]]
RUNS = ["SkiRunNaturalA", "SkiRunGroomedA", "SkiRunSnowmakingA", "SkiRunNightA"]
NEW_COL = (255, 196, 70)            # round-2 labels (the hub's amber)


def draw_tile(cv, icon, name, x, y, s, u, kind_rs="area"):
    """One Build-strip tile as hub.module.scss draws it (tile 44 rem x s, icon 30 rem x s, the run
    stripe, the step digit) with its top-left at (x, y). Returns the tile width."""
    tw = 44.0 * s * u
    px0, py0, px1, py1 = x, y, x + tw, y + tw
    cv.rect(px0, py0, px1, py1, 2 * u, TILE_BG)
    isz = int(round(30 * s * u))
    ix = int(round((px0 + px1) / 2 - isz / 2))
    iy = int(round((py0 + py1) / 2 - isz / 2))
    cv.paste_premul(resize_premul(icon, isz, kind_rs), ix, iy)
    kind = KIND.get(name)
    if kind:
        sx0, sx1, sy1 = px0 + 6 * u * s, px1 - 6 * u * s, py1 - 2 * u * s
        sy0 = sy1 - 4 * u * s
        if kind == "groomed":
            cv.rect(sx0, sy0, sx1, sy1, 2 * u, si.HUB_WHITE)
        elif kind == "night":
            cv.rect(sx0, sy0, sx1, sy1, 2 * u, si.HUB_AMBER)
        elif kind == "snowmaking":
            cv.ring_rect(sx0, sy0, sx1, sy1, 2 * u, 1 * u, si.HUB_CYAN)
        else:
            mid = (sx0 + sx1) / 2
            cv.rect(sx0, sy0, mid - 3 * u, sy1, 2 * u, si.HUB_WHITE, 0.7)
            cv.rect(mid + 3 * u, sy0, sx1, sy1, 2 * u, si.HUB_WHITE, 0.7)
    if name in STEP:
        fs = 12.0 * s * u
        draw_digit(cv, STEP[name], px0 + 4 * u + 0.06 * fs, py0 + 1 * u + 0.30 * fs, 0.70 * fs, TEXT,
                   0.85)
    return tw


def draw_groups(cv, icons, groups, x0, y0, s, u, gap_cell=4.0, gap_group=12.0, labels=False,
                kind_rs="area"):
    """Tiles in groups on the hub footer colour, optional labels under each tile (round-2 tiles in
    amber). Returns (w, h)."""
    tw = 44.0 * s * u
    lab_h = 22 if labels else 0
    n_tiles = sum(len(g) for g in groups)
    w = (2 * 8 * u + n_tiles * tw + (n_tiles - len(groups)) * gap_cell * u
         + (len(groups) - 1) * gap_group * u)
    h = tw + 12 * u + lab_h
    cv.rect(x0, y0, x0 + w, y0 + h, 0, FOOTER_BG)
    x = x0 + 8 * u
    for gi, g in enumerate(groups):
        if gi:
            x += gap_group * u - gap_cell * u
        for name in g:
            draw_tile(cv, icons[name], name, x, y0 + 6 * u, s, u, kind_rs)
            if labels:
                lab = CONTACT_LABEL[name]
                col = NEW_COL if name in si.ROUND2 else (214, 222, 230)
                lx = int(round(x + tw / 2 - 3 * len(lab)))
                cv.text(lab, lx, int(round(y0 + 6 * u + tw + 8)), 1, col)
            x += tw + gap_cell * u
    return int(np.ceil(w)), int(np.ceil(h))


def make_contact_sheet(icons, path):
    W = 1880
    cv = Canvas(W, 2600, (34, 38, 46))
    hd, sub, note = (236, 240, 244), (190, 200, 212), (170, 182, 196)
    y = 16
    cv.text("BUILD-STRIP ICONS, STYLE E - ALL 25 TILES (ROUND 2, 2026-10-11). PREVIEW ONLY.", 20, y,
            3, hd)
    y += 34
    cv.text("16 NEW TILES (AMBER LABELS) DRAWN BY ASSETS/BLENDER/SKI_ICONS.PY NEXT TO THE NINE OF "
            "2026-10-04. SAME PLATE, 22 PX MARK MARGIN, ONE RED ACCENT.", 20, y, 2, sub)
    y += 30
    # 1) all 25 at 64 px on hub tiles
    y += 8
    cv.text("ALL 25 AT 64 PX (HUB TILE 94 PX), BUILD ORDER: SIGN + LODGE TIERS / SCHOOL, PATROL / "
            "LIFT TYPES (BASE 1, TOP 2, LINE 3) / RUNS", 20, y, 2, hd)
    y += 24
    s64 = 64.0 / 30.0
    for groups in ([["SkiResortGate"] + LODGES, CIVIC], LIFT_TYPES, [RUNS]):
        _, h = draw_groups(cv, icons, groups, 20, y, s64, 1, labels=True)
        y += h + 12
    y += 14
    # 2) the strip at real 1080p size as the pickers draw it
    cv.text("THE STRIP AT REAL 1080P SIZE (ICON 30 PX, TILE 44 PX) AS THE PICKERS DRAW IT: ONE LODGE "
            "TIER, ONE LIFT TYPE", 20, y, 2, hd)
    y += 24
    pairs = list(zip(LODGES[:4], LIFT_TYPES)) + [(LODGES[4], LIFT_TYPES[3])]
    strips = []
    for lodge, lift in pairs:
        groups = [["SkiResortGate", lodge], CIVIC, lift, RUNS]
        cv.text(f"{CONTACT_LABEL[lodge]} + {CONTACT_LABEL[lift[0]].split()[0]}", 20, y + 18, 2, sub)
        w, h = draw_groups(cv, icons, groups, 300, y, 1.0, 1)
        strips.append((y, w, h))
        y += h + 8
    y += 12
    cv.text("THE SAME STRIPS ZOOMED X3 (NEAREST NEIGHBOUR)", 20, y, 2, hd)
    y += 24
    for sy, sw, sh in strips:
        cv.paste_rgb(zoom(cv.rgb[sy:sy + sh, 300:300 + sw].copy(), 3), 20, y)
        y += sh * 3 + 8
    y += 12
    # 3) the 16 round-2 tiles at 160 px
    cv.text("THE 16 ROUND-2 TILES AT 160 PX ON THE TILE COLOUR", 20, y, 2, hd)
    y += 24
    big = 160
    for row in (si.ROUND2[:8], si.ROUND2[8:]):
        x = 20
        for name in row:
            t = Canvas(big + 16, big + 16, TILE_BG)
            t.paste_premul(resize_premul(icons[name], big), 8, 8)
            cv.paste_rgb(t.rgb, x, y)
            cv.text(CONTACT_LABEL[name], x + 2, y + big + 22, 2, (214, 222, 230))
            x += big + 16 + 4
        y += big + 16 + 36
    cv.text("AREA-AVERAGE DOWNSCALE (THE GAME DOWNSCALE FILTER IS UNVERIFIED). STEP DIGITS AT THE XS FONT "
            "SIZE, 85 % OPACITY; RUN STRIPES AS HUB.MODULE.SCSS.", 20, y, 2, note)
    y += 28
    si.write_png(path, cv.png()[:y])


def grey_contrast(icons):
    """Mean absolute luminance difference between the run icons at 30 px on the tile colour."""
    runs = [n for n in STRIP_ORDER if n in KIND]
    lum = {}
    for n in runs:
        t = Canvas(30, 30, TILE_BG)
        t.paste_premul(resize_premul(icons[n], 30), 0, 0)
        lum[n] = to_grey(t.rgb)[..., 0]
    out = []
    for i, a in enumerate(runs):
        for b in runs[i + 1:]:
            out.append((KIND[a], KIND[b], float(np.abs(lum[a] - lum[b]).mean())))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", required=True, help="directory for sheet.png")
    ap.add_argument("--live", default=LIVE_DIR, help="ImportedData root of the installed icons "
                    "for the before-strip ('' = none)")
    ap.add_argument("--title", default=DEFAULT_TITLE, help="sheet title line")
    ap.add_argument("--launcher", action="store_true",
                    help="write the hub launcher's review sheet (SkiResortTab.png) instead")
    ap.add_argument("--before", default=LAUNCHER_BEFORE,
                    help="--launcher: the old launcher PNG for before / after ('' = none)")
    ap.add_argument("--contact", action="store_true",
                    help="write the contact sheet of all 25 tiles instead (round 2)")
    ap.add_argument("--name", default="build-strip.png", help="--contact: the file name in --out")
    args = ap.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:])
    os.makedirs(args.out, exist_ok=True)
    if args.contact:
        icons = {name: si.render(name)[0] for name in si.STRIP_ORDER}
        path = os.path.join(args.out, args.name)
        make_contact_sheet(icons, path)
        print(f"wrote {path}")
        return
    icons = {name: si.render(name)[0] for name in STRIP_ORDER}
    if args.launcher:
        before = decode_png(args.before) if args.before else None
        path = os.path.join(args.out, "sheet.png")
        make_launcher_sheet(icons, si.render("SkiResortTab")[0], before, path)
        print(f"wrote {path}")
        return
    for a, b, d in grey_contrast(icons):
        print(f"grey 30px mean |dL| {a:10s} vs {b:10s} {d:.3f}")
    live, note = load_live(args.live) if args.live else (None, "")
    path = os.path.join(args.out, "sheet.png")
    make_sheet(icons, path, live, note, args.title)
    print(f"wrote {path}")


if __name__ == "__main__":
    main()
