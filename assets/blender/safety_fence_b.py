"""
safety_fence_b.py - generation B of the orange ski safety fence (2026-10-29): SkiSafetyNetB, a
see-through orange net lane mesh, plus its previews and review pack. The research is in
docs/research/fence-cutout-2026-10-29.md (how vanilla gap fences get their cutout, why generation A
rendered opaque). A solid panel variant (SkiSafetyPanelB) was previewed and dropped (DECISIONS 457,
commit 79d1f08). Generation A (safety_fence.py) stays: it builds SkiNetPostA, the end post the lane
keeps.

    cd assets/blender
    blender --background --factory-startup --python-exit-code 1 --python safety_fence_b.py -- \
        --out ../import/fences/B --export [--preview <dir>] [--review]

THE ASSET (prefab-local meters: x lateral, y up, z travel along the lane)
- SkiSafetyNetB: a see-through orange plastic net (vertical openings, 8 cm sag between posts, 2 cm
  bulge). Its template is CurvedHashedCutout (34) with alphaCutoff 0.54: the vanilla chain-link
  recipe. LOD0 has 68 tris (cap 76). LOD1 has 20 tris (cap 34): straight top 1.20, 3-sided posts, no
  caps.
- Tile = vanilla's chain-link layout (FenceIndustrialPieceHigh01 Mesh: a 4 m tile, two bays, a post
  at each bay start). z is in [-2, +2], and the bays are [-2, 0] and [0, 2].
- Posts: 30 mm square (flats on the axes), y -0.15 .. 1.55. Each post is centered 0.020 after its
  bay start, so post AND cap stay inside [s, s + 0.040] (contract C4).
- Caps: black, square, 39 mm, y 1.52 .. 1.58, sides + top.
- Net: it hangs at x = 0.020 as two single-sided twins 3 mm either side (0.017 facing -x, 0.023
  facing +x). Vanilla's chain-link panel twins sit at x = +-0.003. The hem is at y -0.10 (buried).

V-ROBUST ATLAS (the generation A lesson) In field test 3, the net faces of generation A sampled the
opaque post strip of the atlas (rows 0 .. ~340, V remapped). The mechanism is unverified. So the
cutout never saw a net texel. The generation B atlas is laid out in COLUMNS along u, each uniform
(or periodic) along v. So no V remap can put a net face on post texels:
        1024 px: post [0, 64) | black [64, 96) | guard [96, 128) | sheet (net) [128, 1024)

Post side k samples sub-column k of the post column. Caps sample the black column. The net samples
the sheet column (u across the bay, side b mirrored, v from the hem 0 to the top 1). The net body is
periodic in v (rows r and r + 512 equal outside the two cords). Its first and last rows are net, not
cord. So a clamped V can never stretch an opaque row over the net.

TEXTURES
- Recolor texels: RGB 0.80 (cs2_asset.RECOLOR_ALBEDO). Black texels: 0.04. There is no noise (noise
  would break the V rule).
- Net alpha: vertical openings (superellipse p 1.4) on a staggered lattice, 30 per row per bay (6.67
  cm pitch), 20 rows (6.75 cm pitch at the posts), openings 4.3 x 8.8 cm, mean alpha ~0.45 (vanilla
  chain link 0.443). The RGB of alpha-0 texels = strand RGB (no dark mip fringe).
- ControlMask: sheet + guard G = 1 (slot 2), post B = 1 (slot 3), black 0, A = 0.
- MaskMap: R = G = B = 0, A smoothness (sheet 0.35, post 0.45, cap 0.30).
- Normal: flat.
- settings.json: sharedAssets LOD1 -> LOD0 maps, LOD1 Skip, Surface last.

DETERMINISM The build uses explicit vertex lists, hand-laid texel UVs, the cs2_asset.tag_parts
canonical order and analytic maps (no random numbers). Two builds hash identically
(tests/test_safety_fence_b.py).

The module has no top-level side effects. The __main__ block runs only as a Blender --python script.
"""

import argparse
import math
import os
import sys

import bpy
import mathutils
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import cs2_asset            # noqa: E402
import cs2_validate         # noqa: E402
import safety_fence as F    # noqa: E402  (generation A: part / run / sky helpers and SkiNetPostA)

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))

# --------------------------------------------------------------------------------------
# Name, budgets
# --------------------------------------------------------------------------------------

NAME = "SkiSafetyNetB"
ATLAS = 1024
TRIS = (68, 20)                                  # (LOD0, LOD1), exact
CAPS = (76, 34)                                  # LOD1 cap = 0.50 x LOD0 tris
SURFACE = {"materialTemplate": "CurvedHashedCutout", "needsAlphaClip": True, "alphaCutoff": 0.54}
COLUMNS = {"post": (0, 64), "black": (64, 96), "guard": (96, 128), "sheet": (128, 1024)}
SUB_COLUMNS = 4                                  # post / cap side k -> sub-column k
VALIDATE_SPEC = {"kind": "hanging", "cutout": False, "lod0_cap": CAPS[0], "expected_lods": (0, 1)}
TILING_COUNT, OVERRIDE_LENGTH = 2, 0.0           # the mesh prefabs' CurveProperties (vanilla's chain link)

# --------------------------------------------------------------------------------------
# Geometry in the authoring frame: x = travel along the lane, y = lateral, z = up. orient_for_net
# turns it into the net frame: Blender x = prefab x lateral, Blender -y = prefab z travel.
# --------------------------------------------------------------------------------------

TILE = 4.0
BAY = 2.0
BAY_STARTS = (-2.0, 0.0)
POST_AT = 0.020                   # post center after the bay start
POST_WINDOW = 0.040               # contract C4
POST_Y = (-0.15, 1.55)
CAP_Y = (1.52, 1.58)
POST_HALF = 0.015                 # 30 mm square post
CAP_HALF = 0.0195                 # 39 mm square cap: x in [s + 0.0005, s + 0.0395]
LOD1_POST_SIDES = 3               # apex toward -x, away from the net
SHEET_LAT = 0.020
TWIN = 0.003
HEM = -0.10
NET_TOP, NET_SAG, NET_BULGE, NET_COLS = 1.25, 0.08, 0.020, 4     # generation A's net shape
FLAT_TOP = 1.20                   # LOD1 straight top
X_RANGE = (-0.025, 0.045)

# --------------------------------------------------------------------------------------
# Maps
# --------------------------------------------------------------------------------------

INSET = 0.5                       # texels: UV edges sit on texel centers
RECOLOR = cs2_asset.RECOLOR_ALBEDO     # 0.80
BLACK_SRGB = 0.04
SMOOTH = {"sheet": 0.35, "post": 0.45, "cap": 0.30}
NET_PER_ROW = 30                  # openings per row per bay
NET_ROW_PX = 51.2                 # 20 rows over 1024 px: period 102.4 px, 5 periods = 512 rows
OPEN_K = 0.65                     # opening half-axes = OPEN_K x the rhombus cell's
OPEN_P = 1.4                      # superellipse exponent (1 diamond, 2 ellipse)
CORD_ROWS = ((6, 16), (1008, 1018))   # opaque bottom / top cords, inset: rows 0 and 1023 stay net
PNG_TOL = 1.5 / 255.0             # equality tolerance that survives 8-bit PNG rounding (contracts run on both)

# --------------------------------------------------------------------------------------
# Previews
# --------------------------------------------------------------------------------------

TINT_SRGB = (1.0, 0.42, 0.15)     # ColorProperties slots 2 / 3 as shipped (read as sRGB in game)
POST_OPTIONS = {"tan": (0.80, 0.68, 0.46), "charcoal": (0.20, 0.20, 0.22)}   # slot 3 knobs, sRGB
PREVIEW_DISTANCES = (2.0, 10.0, 50.0)
PREVIEW_VIEWS = ("front", "oblique", "backlit")
RUN_TILES = 16
RUN_RADIUS = 80.0


def _lin(srgb):
    return tuple(float(c) for c in np.where(np.asarray(srgb) <= 0.04045, np.asarray(srgb) / 12.92,
                                            ((np.asarray(srgb) + 0.055) / 1.055) ** 2.4))


# --------------------------------------------------------------------------------------
# Parts
# --------------------------------------------------------------------------------------

def _sub_column(region, k):
    """Texel u range of sub-column k of a column, 1 px margins."""
    c0, c1 = COLUMNS[region]
    w = (c1 - c0) / SUB_COLUMNS
    return c0 + k * w + 1.0, c0 + (k + 1) * w - 1.0


def _post(name, cx, sides):
    """Post prism sides, with no top (the cap covers it) and no bottom (buried). Side k -> post
    sub-column k, v over the whole atlas (the column is uniform along v)."""
    r = POST_HALF * math.sqrt(2.0)
    phase = 45.0 if sides == 4 else 270.0
    verts = F._ring(cx, r, sides, POST_Y[0], phase) + F._ring(cx, r, sides, POST_Y[1], phase)
    faces, uvs = [], []
    for i in range(sides):
        j = (i + 1) % sides
        u0, u1 = _sub_column("post", i)
        faces.append((i, j, sides + j, sides + i))
        uvs.append([(u0, INSET), (u1, INSET), (u1, ATLAS - INSET), (u0, ATLAS - INSET)])
    return F._part(name, verts, faces, uvs, ATLAS)


def _cap(name, cx):
    """Black square cap: 4 sides (black sub-columns) + top (a square in the black column)."""
    n = 4
    r = CAP_HALF * math.sqrt(2.0)
    verts = F._ring(cx, r, n, CAP_Y[0], 45.0) + F._ring(cx, r, n, CAP_Y[1], 45.0)
    faces, uvs = [], []
    for i in range(n):
        j = (i + 1) % n
        u0, u1 = _sub_column("black", i)
        faces.append((i, j, n + j, n + i))
        uvs.append([(u0, INSET), (u1, INSET), (u1, ATLAS - INSET), (u0, ATLAS - INSET)])
    c0, c1 = COLUMNS["black"]
    uc, vc, h = 0.5 * (c0 + c1), 0.5 * ATLAS, 0.5 * (c1 - c0) - 2.0
    faces.append((n, n + 1, n + 2, n + 3))                        # top, CCW from above
    uvs.append([(uc + h * math.copysign(1.0, math.cos(math.radians(45.0 + 90.0 * q))),
                 vc + h * math.copysign(1.0, math.sin(math.radians(45.0 + 90.0 * q)))) for q in range(n)])
    return F._part(name, verts, faces, uvs, ATLAS)


def _sheet_bay(name, bay, side, lod):
    """One bay of net as explicit triangles. Side 'a' at SHEET_LAT - TWIN faces -lateral, side 'b' at
    SHEET_LAT + TWIN faces +lateral (reversed winding, mirrored u: both keep CCW UVs). LOD0 sags and
    bulges over NET_COLS columns. LOD1 is one straight quad at FLAT_TOP."""
    s0, s1 = COLUMNS["sheet"]
    u_lo, u_hi = s0 + INSET, s1 - INSET
    shaped = lod == 0
    ncol = NET_COLS if shaped else 1
    off = -TWIN if side == "a" else TWIN
    cols = []
    for c in range(ncol + 1):
        u = c / ncol
        x = BAY_STARTS[bay] + u * BAY
        if shaped:
            top = NET_TOP - NET_SAG * (1.0 - (2.0 * u - 1.0) ** 2)
            lat = SHEET_LAT + NET_BULGE * math.sin(math.pi * u)
        else:
            top, lat = FLAT_TOP, SHEET_LAT
        cols.append(((x, lat + off, HEM), (x, lat + off, top), u))
    verts = []
    for b, t, _u in cols:
        verts += [b, t]

    def uv(u, top):
        t = u if side == "a" else 1.0 - u
        return (u_lo + t * (u_hi - u_lo), (ATLAS - INSET) if top else INSET)

    faces, uvs = [], []
    for c in range(ncol):
        b0, t0, b1, t1 = 2 * c, 2 * c + 1, 2 * c + 2, 2 * c + 3
        u0, u1 = cols[c][2], cols[c + 1][2]
        if side == "a":
            faces += [(b0, b1, t1), (b0, t1, t0)]
            uvs += [[uv(u0, False), uv(u1, False), uv(u1, True)],
                    [uv(u0, False), uv(u1, True), uv(u0, True)]]
        else:
            faces += [(b0, t1, b1), (b0, t0, t1)]
            uvs += [[uv(u0, False), uv(u1, True), uv(u1, False)],
                    [uv(u0, False), uv(u0, True), uv(u1, True)]]
    return F._part(name, verts, faces, uvs, ATLAS)


def tile_name(lod):
    return NAME if lod == 0 else f"{NAME}_LOD1"


def build_tile(lod):
    """The lane-mesh tile (LOD0 or LOD1), finished: tagged, joined, net-oriented, shaded."""
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    parts = [_sheet_bay(f"net_{side}{bay}", bay, side, lod) for bay in (0, 1) for side in ("a", "b")]
    for k, s in enumerate(BAY_STARTS):
        cx = s + POST_AT
        parts.append(_post(f"post_{k}", cx, 4 if lod == 0 else LOD1_POST_SIDES))
        if lod == 0:
            parts.append(_cap(f"cap_{k}", cx))
    obj = F._join(parts, tile_name(lod))
    cs2_asset.orient_for_net(obj)
    cs2_asset.shade_by_angle(obj, 66.0, weighted=(lod == 0))
    return obj


# --------------------------------------------------------------------------------------
# Maps
# --------------------------------------------------------------------------------------

def net_alpha(atlas=ATLAS, x0=0):
    """(atlas, atlas - x0) alpha of the net pattern for texel columns x0 .. atlas (rows bottom-up = v).
    Openings sit on a staggered lattice centered on the bay center u = 576 (mirror-symmetric, so the
    mirrored side b lines up). The edges are 1 px anti-aliased, and the two cords are opaque."""
    s0, s1 = COLUMNS["sheet"]
    u_lo, u_hi = s0 + INSET, s1 - INSET
    uc = 0.5 * (u_lo + u_hi)
    px = (u_hi - u_lo) / NET_PER_ROW
    y, x = np.mgrid[0:atlas, x0:atlas].astype(np.float64) + 0.5
    a, b = OPEN_K * px / 2.0, OPEN_K * NET_ROW_PX
    best_r = np.full(x.shape, np.inf)
    best_g = np.ones(x.shape)
    j0 = np.floor(y / NET_ROW_PX)
    for dj in (-1.0, 0.0, 1.0):
        j = j0 + dj
        cy = (j + 0.5) * NET_ROW_PX
        shift = np.where(np.mod(j, 2.0) == 1.0, 0.5, 0.0)
        cx = uc + (np.round((x - uc) / px - shift) + shift) * px
        dx, dy = np.abs(x - cx), np.abs(y - cy)
        r = ((dx / a) ** OPEN_P + (dy / b) ** OPEN_P) ** (1.0 / OPEN_P)
        gx, gy = (dx / a) ** (OPEN_P - 1.0) / a, (dy / b) ** (OPEN_P - 1.0) / b
        g = np.maximum(np.power(np.maximum(r, 1e-9), 1.0 - OPEN_P) * np.hypot(gx, gy), 1e-6)
        closer = r < best_r
        best_r = np.where(closer, r, best_r)
        best_g = np.where(closer, g, best_g)
    alpha = np.clip((best_r - 1.0) / best_g + 0.5, 0.0, 1.0)
    for r0, r1 in CORD_ROWS:
        alpha[r0:r1] = 1.0
    return alpha.astype(np.float32)


def paint_maps():
    """{suffix: (rgba float32 (ATLAS, ATLAS, 4) bottom-up rows, is_data)}."""
    A = ATLAS
    base = np.zeros((A, A, 4), np.float32)
    nrm = np.zeros_like(base)
    msk = np.zeros_like(base)
    ctl = np.zeros_like(base)
    nrm[..., 0:2] = 0.5
    nrm[..., 2:4] = 1.0
    base[..., 0:3] = RECOLOR                       # sheet + guard: slot 2
    base[..., 3] = 1.0
    ctl[..., 1] = 1.0
    msk[..., 3] = SMOOTH["sheet"]
    xs = slice(*COLUMNS["post"])                   # posts: slot 3
    ctl[:, xs, 1] = 0.0
    ctl[:, xs, 2] = 1.0
    msk[:, xs, 3] = SMOOTH["post"]
    xs = slice(*COLUMNS["black"])                  # caps: black, no slot
    base[:, xs, 0:3] = BLACK_SRGB
    ctl[:, xs, :] = 0.0
    msk[:, xs, 3] = SMOOTH["cap"]
    g0 = COLUMNS["guard"][0]
    base[:, g0:, 3] = net_alpha(A, g0)
    return {"BaseColor": (base, False), "Normal": (nrm, True), "MaskMap": (msk, True),
            "ControlMask": (ctl, True)}


# --------------------------------------------------------------------------------------
# Contracts (they raise on failure and return the measured numbers)
# --------------------------------------------------------------------------------------

def _region_of(part):
    if part.startswith("net_"):
        return "sheet"
    return "post" if part.startswith("post_") else "black"


def tile_contracts(obj, lod, maps=None):
    """C2-C7 on a finished tile."""
    fails = []
    me = obj.data
    P = F._prefab_co(obj)
    lo, hi = P.min(axis=0), P.max(axis=0)
    tris = cs2_asset.tri_count(obj)
    want, cap = TRIS[lod], CAPS[lod]
    if tris != want or tris > cap:
        fails.append(f"C5 tris {tris} (expected {want}, cap {cap})")
    if lod == 1 and tris > 0.5 * TRIS[0]:
        fails.append(f"C5 LOD1 {tris} > 50 % of LOD0 {TRIS[0]}")
    top = CAP_Y[1] if lod == 0 else POST_Y[1]
    if abs(lo[2] + TILE / 2) > 1e-4 or abs(hi[2] - TILE / 2) > 1e-4:
        fails.append(f"C2 z extent {lo[2]:.5f} .. {hi[2]:.5f} != -2 .. +2")
    if abs(lo[1] - POST_Y[0]) > 1e-4 or abs(hi[1] - top) > 1e-4:
        fails.append(f"C2 y extent {lo[1]:.5f} .. {hi[1]:.5f} != {POST_Y[0]} .. {top}")
    if lo[0] < X_RANGE[0] - 1e-6 or hi[0] > X_RANGE[1] + 1e-6:
        fails.append(f"C2 x extent {lo[0]:.5f} .. {hi[0]:.5f} outside {X_RANGE}")
    names = F._part_names(obj)
    polys = list(me.polygons)
    vpart = {}
    for p, n in zip(polys, names):
        for v in p.vertices:
            vpart[v] = n
    step = BAY / NET_COLS if lod == 0 else BAY
    sheet_v = [v for v, n in vpart.items() if _region_of(n) == "sheet"]
    cols_z = -TILE / 2 + step * np.round((P[sheet_v, 2] + TILE / 2) / step)
    off_col = int(np.count_nonzero(np.abs(P[sheet_v, 2] - cols_z) > 1e-3))
    if off_col:
        fails.append(f"C3 {off_col} sheet vertices off the {step} m columns")
    worst = (1.0, -1.0)
    bad_post = 0
    for v, n in vpart.items():
        if _region_of(n) != "sheet":
            z = P[v, 2]
            s = min(BAY_STARTS, key=lambda b: abs(z - b))
            r = z - s
            worst = (min(worst[0], r), max(worst[1], r))
            if r < -1e-6 or r > POST_WINDOW + 1e-6:
                bad_post += 1
    if bad_post:
        fails.append(f"C4 {bad_post} post / cap vertices outside [s, s + {POST_WINDOW}] "
                     f"(range {worst[0]:.4f} .. {worst[1]:.4f})")
    # C6 twins: same (y, z) per vertex, opposite normals, the -x facing one 6 mm further -x
    keyed = {}
    for p, n in zip(polys, names):
        if _region_of(n) == "sheet":
            key = tuple(sorted((round(float(P[v, 1]), 5), round(float(P[v, 2]), 5)) for v in p.vertices))
            keyed.setdefault(key, []).append((p.normal.copy(), float(np.mean(P[list(p.vertices), 0]))))
    bad_twin = 0
    for pair in keyed.values():
        if len(pair) != 2 or pair[0][0].dot(pair[1][0]) > -0.999:
            bad_twin += 1
            continue
        (n0, x0), (_n1, x1) = pair
        neg_x, pos_x = (x0, x1) if n0.x < 0.0 else (x1, x0)
        if abs((pos_x - neg_x) - 2.0 * TWIN) > 1e-4:
            bad_twin += 1
    n_sheet = sum(1 for n in names if _region_of(n) == "sheet")
    if bad_twin or n_sheet != 2 * len(keyed):
        fails.append(f"C6 {bad_twin} sheet faces without an outward twin 2 x {TWIN} m apart ({n_sheet} faces)")
    # C7 UVs: CCW, non-zero, inside the face's column. Posts / caps on alpha-255 texels
    alpha = maps["BaseColor"][0][..., 3] if maps else None
    bad_uv = zero = mirrored = 0
    for p, n, pts in zip(polys, names, F._uv_faces(obj, ATLAS)):
        sa = F._signed_area(pts)
        if abs(sa) < 1e-9:
            zero += 1
        elif sa < 0.0:
            mirrored += 1
        c0, c1 = COLUMNS[_region_of(n)]
        if ((pts < -1e-6).any() or (pts > ATLAS + 1e-6).any()
                or pts[:, 0].min() < c0 - 1e-6 or pts[:, 0].max() > c1 + 1e-6):
            bad_uv += 1
            continue
        if alpha is not None and _region_of(n) != "sheet":
            u0, u1 = int(math.floor(pts[:, 0].min())), int(math.ceil(pts[:, 0].max()))
            v0, v1 = int(math.floor(pts[:, 1].min())), int(math.ceil(pts[:, 1].max()))
            if float(alpha[v0:v1, u0:u1].min()) < 1.0:
                bad_uv += 1
    if bad_uv or zero or mirrored:
        fails.append(f"C7 UV faces: {bad_uv} outside their column / on alpha < 255, {zero} zero-area, "
                     f"{mirrored} mirrored")
    inward = 0
    for p, n in zip(polys, names):
        if _region_of(n) == "sheet":
            continue
        c = np.array(p.center[:])
        cx = BAY_STARTS[int(n.split("_")[1])] + POST_AT            # authoring travel -> Blender -y
        nrm = np.array(p.normal[:])
        if abs(nrm[2]) > 0.9:
            ok = nrm[2] > 0.0                                       # cap tops only (no bottoms)
        else:
            ok = float(np.dot(nrm[:2], np.array([c[0], c[1] + cx]))) > 0.0
        inward += 0 if ok else 1
    if inward:
        fails.append(f"{inward} post / cap faces point inward or down")
    if fails:
        raise RuntimeError(f"{obj.name}: contracts FAILED: " + "; ".join(fails))
    out = {"tris": tris, "verts": len(me.vertices), "faces": len(polys),
           "bounds_prefab_m": {"min": [round(float(v), 4) for v in lo],
                               "max": [round(float(v), 4) for v in hi]},
           "post_window_m": [round(float(worst[0]), 4), round(float(worst[1]), 4)],
           "sheet_twins": len(keyed)}
    print(f"[safety_fence_b] {obj.name} contracts hold: {tris} tris, bounds {out['bounds_prefab_m']}, "
          f"post window {out['post_window_m']}, {len(keyed)} twin pairs")
    return out


def texture_contracts(maps):
    """C8 / C9 / V on painted arrays (the test re-runs them on the written PNGs)."""
    fails = []
    A = ATLAS
    cols = COLUMNS
    base, ctl, msk = maps["BaseColor"][0], maps["ControlMask"][0], maps["MaskMap"][0]
    a = base[..., 3]
    # V: the column regions that must not vary along v (all four maps)
    for region in ("post", "black"):
        xs = slice(*cols[region])
        for m, (arr, _d) in maps.items():
            if float(np.ptp(arr[:, xs, :], axis=0).max()) > 0.0:
                fails.append(f"V {m} {region} column varies along v")
    if float(a[:, :cols["guard"][0]].min()) < 1.0:
        fails.append("C8 alpha < 255 in the post / black columns")
    body = a[:, cols["guard"][0]:]
    cords = np.zeros(A, bool)
    for r0, r1 in CORD_ROWS:
        cords[r0:r1] = True
    free = ~(cords[:A // 2] | cords[A // 2:])
    drift = float(np.abs(body[:A // 2][free] - body[A // 2:][free]).max())
    if drift > PNG_TOL:
        fails.append(f"V net body not periodic over {A // 2} rows (max diff {drift:.4f})")
    edge = (float(body[0].mean()), float(body[-1].mean()))
    if max(edge) > 0.6:
        fails.append(f"V net edge rows mean alpha {edge} > 0.6 (a clamped V would read opaque)")
    if float(body[cords].min()) < 1.0:
        fails.append("C8 cord rows not opaque")
    net = a[:, cols["sheet"][0]:]
    mean = float(net.mean())
    if not 0.40 <= mean <= 0.50:
        fails.append(f"C8 net mean alpha {mean:.3f} outside 0.40 .. 0.50")
    strand = net > 0.0
    strand_rgb = float(base[:, cols["sheet"][0]:, 0][strand].mean())
    holes = base[:, cols["sheet"][0]:, 0:3][~strand]
    hole_dev = float(np.abs(holes - strand_rgb).max()) if holes.size else 0.0
    if hole_dev > 0.02:
        fails.append(f"C8 alpha-0 RGB deviates {hole_dev:.3f} > 0.02 from strand RGB {strand_rgb:.3f}")
    mirror = float(np.abs(net - net[:, ::-1]).max())       # texel centers 128.5 .. 1023.5 about 576
    if mirror > PNG_TOL:
        fails.append(f"C8 net pattern not mirror-symmetric about the bay center (max diff {mirror:.4f})")
    out = {"net_alpha_mean": round(mean, 4), "net_alpha_zero_fraction": round(float((net <= 0.0).mean()), 4),
           "net_alpha_full_fraction": round(float((net >= 1.0).mean()), 4),
           "edge_row_alpha_mean": [round(e, 4) for e in edge], "hole_rgb_max_dev": round(hole_dev, 4),
           "mirror_max_diff": round(mirror, 4)}
    rec = (ctl[..., 1] >= 0.5) | (ctl[..., 2] >= 0.5)
    dev = float(np.abs(base[..., 0:3][rec] - RECOLOR).max())
    if dev > 0.05:
        fails.append(f"C8 recolor texels deviate {dev:.3f} > 0.05 from {RECOLOR}")
    g0 = cols["guard"][0]
    if float(ctl[:, g0:, 1].min()) < 1.0 or float(ctl[:, g0:, [0, 2]].max()) > 0.0:
        fails.append("C9 sheet / guard ControlMask is not G = 1 only")
    xs = slice(*cols["post"])
    if float(ctl[:, xs, 2].min()) < 1.0 or float(ctl[:, xs, 0:2].max()) > 0.0:
        fails.append("C9 post column ControlMask is not B = 1 only")
    xs = slice(*cols["black"])
    if float(ctl[:, xs, :].max()) > 0.0 or float(base[:, xs, 0:3].max()) > 0.05:
        fails.append("C9 black column not black / ControlMask not 0")
    if float(ctl[..., 3].max()) > 0.0 or float(ctl[..., 0:3].sum(axis=-1).max()) > 1.0 + 1e-6:
        fails.append("C9 ControlMask A > 0 or R + G + B > 1")
    if float(msk[..., 0:3].max()) > 0.0:
        fails.append("C9 MaskMap R / G / B > 0")
    if fails:
        raise RuntimeError(f"{NAME} texture contracts FAILED: " + "; ".join(fails))
    print(f"[safety_fence_b] {NAME} texture contracts hold: {out}")
    return out


# --------------------------------------------------------------------------------------
# Previews: 2 / 10 / 50 m x front / oblique / backlit, LOD1 at 50 m, a 120 m city-camera frame and
# the post-color options. Sheets: a 3 x 3 sheet + an extras sheet
# --------------------------------------------------------------------------------------

def _tinted_image(name, asset_dir, size, tint_g, tint_b, tag):
    """BaseColor x slot 2 (G) / slot 3 (B) linear tints (cs2_preview convention), alpha kept."""
    import cs2_preview
    base = cs2_preview._load_rgba(os.path.join(asset_dir, f"{name}_BaseColor.png"))
    ctl = cs2_preview._load_rgba(os.path.join(asset_dir, f"{name}_ControlMask.png"), is_data=True)
    lin = cs2_preview._srgb_to_linear(base[..., :3])
    for ch, rgb in ((1, tint_g), (2, tint_b)):
        m = np.clip(ctl[..., ch], 0.0, 1.0)[..., None]
        lin = lin * ((1.0 - m) + np.asarray(rgb, np.float32)[None, None, :] * m)
    out = np.empty_like(base)
    out[..., :3] = cs2_preview._linear_to_srgb(lin)
    out[..., 3] = base[..., 3]
    img = bpy.data.images.new(f"{name}_Preview_{tag}", size, size, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.alpha_mode = "STRAIGHT"
    img.pixels.foreach_set(np.ascontiguousarray(out).ravel())
    return img


def _preview_material(name, asset_dir, size, tint_g, tint_b, tag):
    mat = bpy.data.materials.new(f"{name}_Preview_{tag}")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.name = tex.label = "BaseColor"
    tex.image = _tinted_image(name, asset_dir, size, tint_g, tint_b, tag)
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    msk = bpy.data.images.load(os.path.join(asset_dir, f"{name}_MaskMap.png"), check_existing=True)
    msk.colorspace_settings.name = "Non-Color"
    msk.alpha_mode = "CHANNEL_PACKED"
    mt = nt.nodes.new("ShaderNodeTexImage")
    mt.image = msk
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(mt.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
    return F._wire_alpha(mat)


def _flat_material(name, rgb, rough):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    return mat


def _figure(name, s, side_m, radius, body_mat, head_mat):
    """A 1.73 m stand-in person beside the run (scale cue): cylinder body, sphere helmet."""
    pos, _d, n = F.path_point(s, radius)
    p = pos + n * side_m
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.19, depth=1.45, location=(p.x, p.y, 0.725))
    body = bpy.context.active_object
    body.name = f"{name}Body"
    body.data.materials.append(body_mat)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=0.11, location=(p.x, p.y, 1.62))
    head = bpy.context.active_object
    head.name = f"{name}Head"
    head.data.materials.append(head_mat)
    return [body, head]


def _run(tile, i0, n, name, radius):
    """Tiles i0 .. i0 + n - 1 of the path, bent, joined, re-shaded."""
    copies = [F.bend_copy(tile, f"{name}_t{i}", TILE * i, radius) for i in range(i0, i0 + n)]
    bpy.ops.object.select_all(action="DESELECT")
    for c in copies:
        c.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = obj.data.name = name
    if obj.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    cs2_asset.shade_by_angle(obj, 66.0, weighted=False)
    return obj


def _sheet(rows, out_path, cell=(600, 400), pad=4):
    """Grid of preview frames (rows of paths, None = blank), 2x box-downscaled, row 0 on top."""
    import cs2_preview
    nr, nc = len(rows), max(len(r) for r in rows)
    W, H = nc * cell[0] + (nc - 1) * pad, nr * cell[1] + (nr - 1) * pad
    out = np.full((H, W, 4), 0.5, np.float32)
    out[..., 3] = 1.0
    for r, row in enumerate(rows):
        for c, path in enumerate(row):
            if not path or not os.path.isfile(path):
                continue
            img = cs2_preview._load_rgba(path)
            h, w = img.shape[:2]
            if (h, w) == (2 * cell[1], 2 * cell[0]):
                img = img.reshape(cell[1], 2, cell[0], 2, 4).mean(axis=(1, 3))
            else:
                img = img[(np.arange(cell[1]) * h // cell[1])][:, (np.arange(cell[0]) * w // cell[0])]
            y0 = H - (r + 1) * cell[1] - r * pad
            x0 = c * (cell[0] + pad)
            out[y0:y0 + cell[1], x0:x0 + cell[0]] = img
    cs2_preview._write_rgba(out_path, out)
    return out_path


def render_previews(out_dir, built, post, post_dir, resolution=(1200, 800), samples=64):
    """All preview frames into out_dir. Returns the written paths. `built`: {"lod0", "lod1", "dir"}.
    The rig (runs, end posts, figures, ground, camera, materials) is removed afterwards."""
    import cs2_preview
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    hidden = {o.name: o.hide_render for o in sc.objects}
    for o in sc.objects:
        o.hide_render = True
    made_objs, made_mats = [], []
    tint = _lin(TINT_SRGB)
    runs = {0: _run(built["lod0"], 0, RUN_TILES, "PreviewRunL0", RUN_RADIUS),
            1: _run(built["lod1"], 0, RUN_TILES, "PreviewRunL1", RUN_RADIUS)}
    ends = [F.place_copy(post, "PreviewEndPostA", 0.0, RUN_RADIUS),
            F.place_copy(post, "PreviewEndPostB", RUN_TILES * TILE, RUN_RADIUS)]
    made_objs += list(runs.values()) + ends
    mats = {"orange": _preview_material(NAME, built["dir"], ATLAS, tint, tint, "orange")}
    for opt, srgb in POST_OPTIONS.items():
        mats[opt] = _preview_material(NAME, built["dir"], ATLAS, tint, _lin(srgb), opt)
    post_mat = F._preview_material(F.POST_NAME, post_dir, F.A_POST, tint)
    made_mats += list(mats.values()) + [post_mat]
    for o in runs.values():
        o.data.materials.clear()
        o.data.materials.append(mats["orange"])
    for o in ends:
        o.data.materials.clear()
        o.data.materials.append(post_mat)
    bpy.ops.mesh.primitive_plane_add(size=600.0, location=(0.0, -32.0, 0.0))
    ground = bpy.context.active_object
    ground.name = "PreviewSnow"
    snow = _flat_material("PreviewSnow", (0.85, 0.87, 0.90), 0.85)
    ground.data.materials.append(snow)
    body_mat = _flat_material("PreviewJacket", (0.03, 0.06, 0.18), 0.6)
    head_mat = _flat_material("PreviewHelmet", (0.55, 0.56, 0.60), 0.35)
    s_mid = 8 * TILE + 1.0                       # mid-span of bay 0 of tile 8
    figures = (_figure("PreviewFigureFront", s_mid + 2.5, 1.2, RUN_RADIUS, body_mat, head_mat)
               + _figure("PreviewFigureBehind", s_mid - 4.0, -1.5, RUN_RADIUS, body_mat, head_mat))
    made_objs += [ground] + figures
    made_mats += [snow, body_mat, head_mat]
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.sensor_fit = "VERTICAL"
    cam_data.sensor_height = 24.0
    cam_data.lens = 12.0 / math.tan(math.radians(cs2_preview.VERTICAL_FOV_DEG) / 2.0)
    cam_data.clip_end = 5000.0
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    made_objs.append(cam)
    sc.render.resolution_x, sc.render.resolution_y = resolution
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGB"
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = "None"
    for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            sc.render.engine = eng
            break
        except TypeError:
            continue
    sc.eevee.taa_render_samples = samples
    elev = {2.0: 8.0, 10.0: 12.0, 50.0: 18.0, 120.0: 30.0}
    # (file name, run LOD, material option, view, distance)
    frames = [(f"{NAME}_{int(dist)}m_{view}.png", 0, "orange", view, dist)
              for view in PREVIEW_VIEWS for dist in PREVIEW_DISTANCES]
    frames += [(f"{NAME}_50m_lod1_oblique.png", 1, "orange", "oblique", 50.0),
               (f"{NAME}_120m_oblique.png", 0, "orange", "oblique", 120.0)]
    frames += [(f"{NAME}_10m_oblique_posts-{opt}.png", 0, opt, "oblique", 10.0) for opt in POST_OPTIONS]
    written = {}
    for fname, lod, opt, view, dist in frames:
        pos, d, n = F.path_point(s_mid, RUN_RADIUS)
        target = pos + mathutils.Vector((0.0, 0.0, 0.7))
        obl = n * math.cos(math.radians(35)) + d * math.sin(math.radians(35))
        horiz = {"front": n.copy(), "oblique": obl, "backlit": -n}[view]
        horiz.normalize()
        e = math.radians(elev[dist])
        cam.location = target + mathutils.Vector((horiz.x * math.cos(e), horiz.y * math.cos(e), math.sin(e))) * dist
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        if view == "backlit":
            sun_xy, sun_el = n, 18.0                                  # sun behind the fence, low
        else:
            sun_xy = horiz * math.cos(math.radians(40)) + d * math.sin(math.radians(40))
            sun_el = 40.0
        world, sun, _sd = F._sky(sun_el, F._sky_rotation_for((sun_xy.x, sun_xy.y)))
        for k, o in runs.items():
            o.hide_render = k != lod
            o.data.materials[0] = mats[opt]
        for o in ends + figures + [ground]:
            o.hide_render = False
        path = os.path.join(out_dir, fname)
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written[fname] = path
        print(f"[safety_fence_b] preview {fname}")
        ld = sun.data
        bpy.data.objects.remove(sun, do_unlink=True)
        bpy.data.lights.remove(ld)
        sc.world = None
        bpy.data.worlds.remove(world)
    out = list(written.values())
    rows = [[written[f"{NAME}_{int(dist)}m_{view}.png"] for view in PREVIEW_VIEWS] for dist in PREVIEW_DISTANCES]
    out.append(_sheet(rows, os.path.join(out_dir, f"{NAME}_sheet.png")))
    extras = [[written[f"{NAME}_50m_lod1_oblique.png"], written[f"{NAME}_120m_oblique.png"]],
              [written[f"{NAME}_10m_oblique_posts-{opt}.png"] for opt in POST_OPTIONS]]
    out.append(_sheet(extras, os.path.join(out_dir, f"{NAME}_extras.png")))
    for o in made_objs:
        data = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if data is not None and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Camera):
                bpy.data.cameras.remove(data)
    for m in made_mats:
        if m.users == 0:
            bpy.data.materials.remove(m)
    for o in sc.objects:
        if o.name in hidden:
            o.hide_render = hidden[o.name]
    return out


# --------------------------------------------------------------------------------------
# Review pack (cs2_review.write_pack + BaseColor alpha wired into the pack's materials)
# --------------------------------------------------------------------------------------

def write_review(built, post, post_dir, out_dir=None, args=None):
    """LOD0 / LOD1 + a 3-tile curved run with SkiNetPostA at its dead end."""
    import cs2_review
    orig = cs2_review.pbr_material

    def pbr_alpha(*a, **k):
        return F._wire_alpha(orig(*a, **k))

    cs2_review.pbr_material = pbr_alpha
    try:
        run = F.build_run(built["lod0"], 3, f"{NAME}RunThree", radius=20.0, offset=(1.5, 0.0, 0.0))
        end = F.place_copy(post, f"{NAME}RunEndPost", 3 * TILE, radius=20.0, offset=(1.5, 0.0, 0.0))
        try:
            meta = {"generator": "safety_fence_b.py", "args": list(args or []), "kind": "hanging (lane mesh)",
                    "caps": {0: CAPS[0], 1: CAPS[1]}, "letter": "B",
                    "tint": {"G": _lin(TINT_SRGB), "B": _lin(TINT_SRGB)}}
            return cs2_review.write_pack(
                NAME, built["dir"], {0: built["lod0"], 1: built["lod1"]}, manifest={}, meta=meta, out_dir=out_dir,
                extra_meshes={"RunThree": (run, NAME, built["dir"]), "RunEndPost": (end, F.POST_NAME, post_dir)})
        finally:
            for o in (run, end):
                me = o.data
                bpy.data.objects.remove(o, do_unlink=True)
                if me.users == 0:
                    bpy.data.meshes.remove(me)
    finally:
        cs2_review.pbr_material = orig


# --------------------------------------------------------------------------------------
# Export
# --------------------------------------------------------------------------------------

def _portable(path_or_arg):
    """Repo-relative form of an absolute path inside the repo. Any other absolute path gives
    '<outside-repo>' (manifests are tracked: no home paths)."""
    if not os.path.isabs(path_or_arg):
        return path_or_arg
    rel = os.path.relpath(os.path.abspath(path_or_arg), REPO)
    return "<outside-repo>" if rel.startswith("..") or os.path.isabs(rel) else rel.replace("\\", "/")


def _end_post(work_dir):
    """Generation A's SkiNetPostA (the lane's m_NodeObject mesh) + its maps, for previews / packs."""
    post_dir = os.path.join(work_dir, F.POST_NAME)
    os.makedirs(post_dir, exist_ok=True)
    F.write_maps(post_dir, F.POST_NAME, F.paint_post_maps(), F.A_POST)
    return F.build_post(F.POST_NAME), post_dir


def export(out_root, preview_dir=None, review=False, args=None):
    """Build, check and write the asset folder (+ previews / review pack). Returns (built, data)."""
    import cs2_preview
    out_root = os.path.abspath(out_root)
    pargs = [_portable(a) for a in (args or [])]
    d = os.path.join(out_root, NAME)
    os.makedirs(d, exist_ok=True)
    F._clean_asset_dir(d, NAME)
    maps = paint_maps()
    tex = texture_contracts(maps)
    lod0, lod1 = build_tile(0), build_tile(1)
    c0, c1 = tile_contracts(lod0, 0, maps), tile_contracts(lod1, 1, maps)
    F.write_maps(d, NAME, maps, ATLAS)
    cs2_asset.write_settings(d, NAME, supplied_lods=(1,), surface=SURFACE)
    cs2_asset.export_fbx(lod0, os.path.join(d, f"{NAME}.fbx"))
    cs2_asset.export_fbx(lod1, os.path.join(d, f"{NAME}_LOD1.fbx"))
    findings, summary = cs2_validate.validate_asset_folder_ex(d, NAME, VALIDATE_SPEC)
    for o in (lod0, lod1):
        findings += cs2_validate.validate_mesh(o, kind="hanging")
    errors = [f for f in findings if f[0] == "ERROR"]
    warns = [f for f in findings if f[0] == "WARN"]
    if errors:
        raise RuntimeError(f"{NAME}: validation errors: {errors}")
    lb = F.bounds_record(lod0)
    dims = [lb["max"][i] - lb["min"][i] for i in range(3)]
    sw = cs2_preview.lod_switch_table(dims, list(TRIS), area=lb["area_m2"])
    sheet_w = COLUMNS["sheet"][1] - COLUMNS["sheet"][0] - 2 * INSET
    built = {"lod0": lod0, "lod1": lod1, "dir": d}
    data = {
        "generator": "assets/blender/safety_fence_b.py",
        "args": pargs,
        "research": "docs/research/fence-cutout-2026-10-29.md",
        "kind": "hanging (lane mesh tile; cs2_validate has no lane kind: no ground rule)",
        "lane_mesh": True,
        "mesh_prefabs": [f"{NAME} Mesh", f"{NAME}_LOD1 Mesh"],
        "material_template": SURFACE["materialTemplate"],
        "surface_settings": SURFACE,
        "curve_properties": {"tiling_count": TILING_COUNT, "override_length_m": OVERRIDE_LENGTH},
        "tile_length_m": TILE, "bays": 2,
        "tris": {"LOD0": c0["tris"], "LOD1": c1["tris"]},
        "caps": {"LOD0": CAPS[0], "LOD1": CAPS[1]},
        "texture_px": ATLAS, "maps": ["BaseColor", "Normal", "MaskMap", "ControlMask"],
        "atlas_columns_px": COLUMNS, "v_robust": True,
        "alpha": tex,
        "texel_density_px_per_m": {
            "sheet_across": round(sheet_w / BAY, 1),
            "sheet_up_at_posts": round((ATLAS - 2 * INSET) / (NET_TOP - HEM), 1)},
        "net_pattern": {
            "openings_per_row_per_bay": NET_PER_ROW, "row_pitch_px": NET_ROW_PX, "k": OPEN_K,
            "superellipse_p": OPEN_P, "cord_rows": CORD_ROWS,
            "opening_cm": [round(100 * OPEN_K * sheet_w / NET_PER_ROW / (sheet_w / BAY), 1),
                           round(100 * 2 * OPEN_K * NET_ROW_PX / ((ATLAS - 2 * INSET) / (NET_TOP - HEM)), 1)]},
        "geometry": {"post_half_m": POST_HALF, "cap_half_m": CAP_HALF, "post_y": POST_Y, "cap_y": CAP_Y,
                     "post_center_after_bay_start_m": POST_AT, "sheet_x": SHEET_LAT, "twin_offset_m": TWIN,
                     "hem_y": HEM, "lod1_post_sides": LOD1_POST_SIDES,
                     "top_y": NET_TOP, "sag_m": NET_SAG, "bulge_m": NET_BULGE, "lod1_top_y": FLAT_TOP},
        "prefab_bounds": {"LOD0": lb, "LOD1": F.bounds_record(lod1)},
        "contracts": {"LOD0": c0, "LOD1": c1},
        "lod_switch_formula": {"switch_m": {k: round(x, 1) for k, x in sw["switch"].items()},
                               "cull_m": round(sw["cull"], 1), "bias": round(sw["bias"], 3),
                               "note": "cs2_preview.lod_switch_table (object formula); lanes may "
                                       "select LODs differently: unverified"},
        "settings_json": {"LOD1": "Skip", "sharedAssets": "LOD1 -> LOD0 maps", "Surface": SURFACE},
        "validation": {"errors": 0, "warnings": len(warns), "tris": summary.get("tris"),
                       "tex": sorted(summary.get("tex") or []),
                       "warn_list": [f"{c}: {m}" for _l, c, m in warns]},
        "end_post": f"{F.POST_NAME} (generation A, unchanged)",
    }
    if preview_dir or review:
        work = os.path.abspath(preview_dir) if preview_dir else os.path.join(out_root, "_review")
        post, post_dir = _end_post(os.path.join(work, "_endpost"))
        if preview_dir:
            shots = render_previews(os.path.abspath(preview_dir), built, post, post_dir)
            data["previews"] = sorted(_portable(p) for p in shots)
        if review:
            pack = write_review(built, post, post_dir, os.path.join(work, "review") if preview_dir else None, args)
            data["review_pack"] = _portable(pack)
    cs2_validate.write_manifest(d, NAME, data)
    print(f"[safety_fence_b] exported {d} (LOD0 {data['tris']['LOD0']} / LOD1 {data['tris']['LOD1']} tris, "
          f"{ATLAS} px, {SURFACE['materialTemplate']}); validation 0 errors / "
          f"{data['validation']['warnings']} warnings")
    return built, data


# --------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------

def parse_args(argv):
    ap = argparse.ArgumentParser(prog="safety_fence_b.py", description="ski safety fence, netted (generation B)")
    ap.add_argument("--out", default=None, help="letter folder, e.g. ../import/fences/B")
    ap.add_argument("--export", action="store_true", help="write the asset folder (needs --out)")
    ap.add_argument("--preview", default=None, help="preview folder (frames + sheets)")
    ap.add_argument("--review", action="store_true", help="also write a review pack")
    return ap.parse_args(argv)


def main(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parse_args(argv)
    F.clear_scene()
    if args.export:
        if not args.out:
            raise SystemExit("--export needs --out")
        export(args.out, preview_dir=args.preview, review=args.review, args=argv)
        return
    maps = paint_maps()
    texture_contracts(maps)
    for lod in (0, 1):
        tile_contracts(build_tile(lod), lod, maps)
    print("[safety_fence_b] built (no export; pass --out <letter dir> --export)")


if __name__ == "__main__":
    main()
