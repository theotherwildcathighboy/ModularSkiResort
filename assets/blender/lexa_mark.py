"""
lexa_mark.py - the resort mark by the logo designer (variant 01: lift, run and peak with snowflakes) as a coverage raster
for baked textures (2026-11-16). No bpy: numpy only, so any build script (or the bundled Python) can import it.

Source: mod/SkiResortUI/src/hub/SkiLauncherGlyph.svg, the hub launcher's copy of the designer's variant 01 (background
removed, white on transparent, DECISIONS 660). It is the in-repo master for the mark. The full handoff (all variants, .ai)
lives in the private docs folder and is not needed here.

The SVG uses only <polygon points> and <path d> with M m L l H h V v C c Z z, no transforms, default nonzero fill.
A parser for exactly that subset plus a supersampled nonzero scanline fill. Anything else in the file raises, so a new
export from the designer cannot silently drop a shape.

API:
    coverage(px, ss=4)  -> (h, w) float32 0..1, rows TOP-DOWN, the ink cropped tight, the longer side = px
    ink_aspect()        -> w / h of the ink box
"""

import os
import re

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
SVG = os.path.normpath(os.path.join(_HERE, "..", "..", "mod", "SkiResortUI", "src", "hub", "SkiLauncherGlyph.svg"))
CURVE_SEGS = 24                 # line segments per cubic Bezier (the longest curve is about 150 units of 916)

_NUM = re.compile(r"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?")
_TOK = re.compile(r"[MmLlHhVvCcZz]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?")


def _read(path=SVG):
    with open(path, encoding="utf-8") as f:
        txt = f.read()
    shapes = re.findall(r"<(\w+)\b", txt)
    bad = sorted(set(s for s in shapes if s not in ("svg", "g", "polygon", "path")))
    if bad:
        raise RuntimeError(f"lexa_mark: unsupported SVG elements {bad} in {path}")
    if "transform=" in txt:
        raise RuntimeError("lexa_mark: the SVG has a transform; extend the parser before using it")
    return txt


def _cubic(p0, p1, p2, p3, n=CURVE_SEGS):
    t = np.linspace(0.0, 1.0, n + 1)[1:, None]
    return ((1 - t) ** 3 * p0 + 3 * (1 - t) ** 2 * t * p1 + 3 * (1 - t) * t ** 2 * p2 + t ** 3 * p3)


def _path_rings(d):
    toks = _TOK.findall(d)
    rings, ring = [], []
    cur = np.zeros(2)
    start = np.zeros(2)
    i, cmd = 0, None
    def num():
        nonlocal i
        v = float(toks[i]); i += 1
        return v
    while i < len(toks):
        if toks[i].isalpha():
            cmd = toks[i]; i += 1
            if cmd in "Zz":
                if ring:
                    rings.append(np.array(ring))
                ring = []
                cur = start.copy()
                continue
        elif cmd is None:
            raise RuntimeError("lexa_mark: path data starts with a number")
        rel = cmd.islower()
        c = cmd.upper()
        if c == "M":
            if ring:
                rings.append(np.array(ring))
            p = np.array([num(), num()])
            cur = cur + p if rel else p
            start = cur.copy()
            ring = [cur.copy()]
            cmd = "l" if rel else "L"            # implicit lineto after a moveto
        elif c == "L":
            p = np.array([num(), num()])
            cur = cur + p if rel else p
            ring.append(cur.copy())
        elif c == "H":
            x = num()
            cur = np.array([cur[0] + x if rel else x, cur[1]])
            ring.append(cur.copy())
        elif c == "V":
            y = num()
            cur = np.array([cur[0], cur[1] + y if rel else y])
            ring.append(cur.copy())
        elif c == "C":
            q = np.array([num() for _ in range(6)]).reshape(3, 2)
            if rel:
                q = q + cur
            pts = _cubic(cur, q[0], q[1], q[2])
            ring.extend(pts)
            cur = q[2].copy()
        else:
            raise RuntimeError(f"lexa_mark: unsupported path command {cmd}")
    if ring:
        rings.append(np.array(ring))
    return rings


def rings(path=SVG):
    """All closed rings in SVG units (y down)."""
    txt = _read(path)
    out = []
    for pts in re.findall(r"<polygon\b[^>]*\bpoints=\"([^\"]+)\"", txt):
        v = np.array([float(x) for x in _NUM.findall(pts)]).reshape(-1, 2)
        out.append(v)
    for d in re.findall(r"<path\b[^>]*\bd=\"([^\"]+)\"", txt):
        out.extend(_path_rings(d))
    if not out:
        raise RuntimeError("lexa_mark: no shapes found")
    return out


def _bbox(rs):
    allp = np.concatenate(rs)
    return allp.min(0), allp.max(0)


def ink_aspect(path=SVG):
    lo, hi = _bbox(rings(path))
    return float((hi[0] - lo[0]) / (hi[1] - lo[1]))


def coverage(px, ss=4, path=SVG):
    """(h, w) float32 coverage, rows top-down, the longer ink side = px pixels. Nonzero winding, ss x ss supersampling."""
    rs = rings(path)
    lo, hi = _bbox(rs)
    k = px / float(max(hi - lo))
    w, h = int(np.ceil((hi[0] - lo[0]) * k)), int(np.ceil((hi[1] - lo[1]) * k))
    W, H = w * ss, h * ss
    ks = k * ss
    edges = []
    for r in rs:
        p = (r - lo) * ks
        q = np.roll(p, -1, axis=0)
        edges.append(np.concatenate([p, q], axis=1))
    e = np.concatenate(edges)
    e = e[e[:, 1] != e[:, 3]]                           # horizontal edges never cross a scanline
    y0, y1 = e[:, 1], e[:, 3]
    up = np.where(y1 > y0, 1, -1)
    ylo, yhi = np.minimum(y0, y1), np.maximum(y0, y1)
    big = np.zeros((H, W), np.uint8)
    xs_cols = np.arange(W) + 0.5
    for row in range(H):
        y = row + 0.5
        m = (ylo <= y) & (y < yhi)
        if not m.any():
            continue
        ee = e[m]
        t = (y - ee[:, 1]) / (ee[:, 3] - ee[:, 1])
        x = ee[:, 0] + t * (ee[:, 2] - ee[:, 0])
        o = np.argsort(x)
        x, wdir = x[o], up[m][o]
        wind = np.cumsum(wdir)
        inside = wind[:-1] != 0
        for a, b in zip(x[:-1][inside], x[1:][inside]):
            i0 = int(np.ceil(a - 0.5)); i1 = int(np.floor(b - 0.5))
            if i1 >= i0:
                big[row, max(i0, 0):min(i1 + 1, W)] = 1
    cov = big.reshape(h, ss, w, ss).mean(axis=(1, 3)).astype(np.float32)
    return cov
