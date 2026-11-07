"""
recipes/_civic.py: recipe-local helpers shared by the civic recipes (recipes/school.py, recipes/patrol.py)
and welcome_sign.py. NOT a recipe: bldg_recipes.load_recipes() skips files whose name starts with "_"
(bldg_recipes.py:236-237). It imports bpy at module level, so recipe modules load it lazily (load_civic())
inside their builders: they stay import-safe.

Contract: docs/research/art/assets-2026-10/design/civic-sign.md with civic-sign-skeptic.md applied
(M1-M8). Everything here is a part the kit (cs2_bldg.py, other owner, not edited) lacks. Each is reported
as a kit need in build/civic-sign.md:
    new_part / face / quads_part / grid_quads   one part from explicit polygons (walls cut
                                                 round a tower, roof top quads, flags)
    cyl_into / cyl_part                          free-axis cylinder / cone part (logs, piers,
                                                 masts, cones, bells)
    remat                                        material remap by name prefix (kit window
                                                 frames / doors / lamp arms are hard-coded
                                                 "trim" = slot 1)
    world_bbox / assert_lot_margin               G3 with the contract margin (lot - 0.25 m)
    check_coplanar / check_inside                G10 (skeptic M4): no same-facing coplanar
                                                 overlap between parts, no face centroid
                                                 inside a massing volume
    uv_frame / GlyphPainter / BarsPainter        N5: asymmetric painted labels oriented by the
                                                 stamped face's UV -> world frame at bake time
    srgb_lin                                     sRGB -> linear (kit painters take linear base)
Deterministic: no randomness, sorted iteration, never a set of BMesh elements.
"""

import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

_HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER_DIR = os.path.dirname(_HERE)
if BLENDER_DIR not in sys.path:
    sys.path.insert(0, BLENDER_DIR)
import cs2_asset   # noqa: E402

UNIQUE = "UVUnique"
AX_TOL = 1e-3


def srgb_lin(c):
    a = np.asarray(c, dtype=np.float64)
    out = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
    return tuple(float(x) for x in np.ravel(out)) if np.ndim(out) else float(out)


# ======================================================================================
# Parts from explicit polygons
# ======================================================================================

def face(bm, pts, outward=None):
    vs = [bm.verts.new((float(p[0]), float(p[1]), float(p[2]))) for p in pts]
    f = bm.faces.new(vs)
    f.normal_update()
    if outward is not None and f.normal.dot(Vector(outward)) < 0.0:
        f.normal_flip()
    return f


def new_part(name, build, mat, weld=1e-6):
    """One part (object at identity, world coords in the mesh) from build(bm). Raises on a
    duplicate name instead of letting Blender append .001 (kit rule, cs2_bldg._make_obj)."""
    if name in bpy.data.objects:
        raise ValueError(f"duplicate part name {name!r}")
    bm = bmesh.new()
    build(bm)
    if not bm.faces:
        bm.free()
        raise ValueError(f"part {name!r} has no faces")
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=weld)
    bm.normal_update()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    if obj.name != name:
        raise ValueError(f"part {name!r} was renamed {obj.name!r}")
    obj["relief"] = "geo"
    return [(obj, mat)]


def quads_part(name, polys, mat):
    """polys = [(points, outward), ...] -> one part."""
    def build(bm):
        for pts, out in polys:
            face(bm, pts, out)
    return new_part(name, build, mat)


def grid_quads(point, us, vs, outward, skip=()):
    """Quads of a planar grid: point(u, v) -> xyz. It cuts `us` x `vs`. `skip` = {(i, j)} cells left out (i
    along us, j along vs). Shared cut lines -> no T-junctions."""
    out = []
    for i in range(len(us) - 1):
        for j in range(len(vs) - 1):
            if (i, j) in skip:
                continue
            u0, u1, v0, v1 = us[i], us[i + 1], vs[j], vs[j + 1]
            out.append(([point(u0, v0), point(u1, v0), point(u1, v1), point(u0, v1)], outward))
    return out


def cyl_into(bm, p0, p1, r, segs=8, cap0=True, cap1=True, r1=None, phase=0.0):
    """Cylinder / cone p0 -> p1 into bm (fan caps, outward normals, cs2_bldg._Geo.cyl)."""
    P0, P1 = Vector(p0), Vector(p1)
    ax = P1 - P0
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
        ring0.append(bm.verts.new(P0 + d * r))
        if rr1 > 1e-6:
            ring1.append(bm.verts.new(P1 + d * rr1))
    tip = bm.verts.new(P1) if rr1 <= 1e-6 else None
    for k in range(segs):
        k1 = (k + 1) % segs
        if tip is not None:
            vs = [ring0[k], ring0[k1], tip]
            mid = (ring0[k].co + ring0[k1].co + tip.co) / 3.0
        else:
            vs = [ring0[k], ring0[k1], ring1[k1], ring1[k]]
            mid = (ring0[k].co + ring0[k1].co + ring1[k].co + ring1[k1].co) * 0.25
        out = mid - (P0 + ax * ((mid - P0).dot(ax)))
        f = bm.faces.new(vs)
        f.normal_update()
        if f.normal.dot(out) < 0.0:
            f.normal_flip()
    for ring, sgn, on in ((ring0, -1.0, cap0), (ring1, 1.0, cap1 and tip is None)):
        if not on:
            continue
        for k in range(1, segs - 1):
            f = bm.faces.new([ring[0], ring[k], ring[k + 1]])
            f.normal_update()
            if f.normal.dot(ax * sgn) < 0.0:
                f.normal_flip()


def cyl_part(name, p0, p1, r, mat, segs=8, cap0=True, cap1=True, r1=None, phase=0.0):
    return new_part(name, lambda bm: cyl_into(bm, p0, p1, r, segs, cap0, cap1, r1, phase), mat)


def box_into(bm, lo, hi, drop=()):
    """Axis-aligned box faces into bm. `drop` = world face keys "+X".."-Z"."""
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    fs = {"-X": ([(x0, y0, z0), (x0, y1, z0), (x0, y1, z1), (x0, y0, z1)], (-1, 0, 0)),
          "+X": ([(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)], (1, 0, 0)),
          "-Y": ([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], (0, -1, 0)),
          "+Y": ([(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)], (0, 1, 0)),
          "-Z": ([(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)], (0, 0, -1)),
          "+Z": ([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], (0, 0, 1))}
    for k in ("-X", "+X", "-Y", "+Y", "-Z", "+Z"):
        if k not in drop:
            face(bm, *fs[k])


def beam_into(bm, p0, p1, w, h, up=(0.0, 0.0, 1.0)):
    """Oriented box between two points (w across, h along the `up`-derived axis), the
    cs2_bldg._beam_into convention (side = axis x up)."""
    P0, P1 = Vector(p0), Vector(p1)
    ax = P1 - P0
    L = ax.length
    if L < 1e-6:
        raise ValueError("beam: zero length")
    ax /= L
    side = ax.cross(Vector(up))
    if side.length < 1e-6:
        side = ax.cross(Vector((1, 0, 0)))
    side.normalize()
    upv = side.cross(ax).normalized()
    c = (P0 + P1) * 0.5
    hx, hy, hz = 0.5 * L, 0.5 * w, 0.5 * h
    V = {}
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                V[(sx, sy, sz)] = c + ax * (sx * hx) + side * (sy * hy) + upv * (sz * hz)
    axes = (ax, side, upv)
    for i in range(3):
        j, k = [a for a in range(3) if a != i]
        for s_ in (1, -1):
            cyc = []
            for (a, b) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                idx = [0, 0, 0]
                idx[i], idx[j], idx[k] = s_, a, b
                cyc.append(V[tuple(idx)])
            face(bm, cyc, axes[i] * s_)


def beam_part(name, p0, p1, w, h, mat, extend=0.0):
    """One beam part. `extend` lengthens both ends (buried ends of braces)."""
    P0, P1 = Vector(p0), Vector(p1)
    d = (P1 - P0).normalized() * float(extend)
    return new_part(name, lambda bm: beam_into(bm, P0 - d, P1 + d, w, h), mat)


def remat(parts, rules):
    """[(obj, key)] with key replaced by the first matching (prefix, new_key) rule."""
    out = []
    for o, k in parts:
        for pre, nk in rules:
            if o.name.startswith(pre):
                k = nk
                break
        out.append((o, k))
    return out


# ======================================================================================
# Geometry contracts on the parts list (before the join)
# ======================================================================================

def _world_polys(parts):
    bpy.context.view_layer.update()
    out = []
    for idx, (o, _k) in enumerate(parts):
        M = o.matrix_world
        R = M.to_3x3()
        me = o.data
        for p in me.polygons:
            pts = np.array([tuple(M @ me.vertices[v].co) for v in p.vertices], dtype=np.float64)
            n = R @ p.normal
            if n.length < 1e-9:
                continue
            n.normalize()
            out.append((idx, np.array(tuple(n)), pts))
    return out


def world_bbox(parts):
    lo = np.full(3, 1e9)
    hi = np.full(3, -1e9)
    for _i, _n, pts in _world_polys(parts):
        lo = np.minimum(lo, pts.min(0))
        hi = np.maximum(hi, pts.max(0))
    return lo, hi


def assert_lot_margin(parts, half_x, half_y, margin=0.25, what="lot"):
    """G3 (contract 1.1): |x| <= half_x - margin, |y| <= half_y - margin, nothing below
    -1e-5 (authoring frame, before the join). Returns (lo, hi)."""
    lo, hi = world_bbox(parts)
    mx, my = max(-lo[0], hi[0]), max(-lo[1], hi[1])
    if mx > half_x - margin + 1e-6 or my > half_y - margin + 1e-6:
        raise RuntimeError(f"G3 {what}: mesh |x| {mx:.3f} / |y| {my:.3f} exceeds "
                           f"{half_x - margin:.2f} / {half_y - margin:.2f}")
    if lo[2] < -1e-5:
        raise RuntimeError(f"G1 {what}: vertex below grade (min z {lo[2]:.4f})")
    return lo, hi


def _clip(poly, a, b):
    """Sutherland-Hodgman: keep the part of convex `poly` left of edge a->b (2D)."""
    out = []
    n = len(poly)
    for i in range(n):
        p, q = poly[i], poly[(i + 1) % n]
        sp = (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])
        sq = (b[0] - a[0]) * (q[1] - a[1]) - (b[1] - a[1]) * (q[0] - a[0])
        if sp >= 0:
            out.append(p)
        if (sp >= 0) != (sq >= 0):
            t = sp / (sp - sq)
            out.append((p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1])))
    return out


def _area2(poly):
    s = 0.0
    for i in range(len(poly)):
        x0, y0 = poly[i]
        x1, y1 = poly[(i + 1) % len(poly)]
        s += x0 * y1 - x1 * y0
    return 0.5 * s


def _ccw(poly):
    return poly if _area2(poly) >= 0 else poly[::-1]


def check_coplanar(parts, tol=1e-3, min_area=2e-4, exempt=()):
    """G10a (skeptic M4): two faces of DIFFERENT parts that lie in the same axis-aligned
    plane, face the same way and overlap by more than `min_area` m^2 z-fight -> raise.
    (Opposite-facing coplanar pairs are contact pairs and allowed.) `exempt` = part-name
    prefix pairs allowed to overlap."""
    names = [o.name for o, _k in parts]
    buckets = {}
    for idx, n, pts in _world_polys(parts):
        ax = int(np.argmax(np.abs(n)))
        if abs(abs(n[ax]) - 1.0) > 1e-4:
            continue
        sgn = 1 if n[ax] > 0 else -1
        c = float(pts[:, ax].mean())
        if np.abs(pts[:, ax] - c).max() > tol:
            continue
        key = (ax, sgn, int(round(c / tol)))
        uv = [i for i in range(3) if i != ax]
        buckets.setdefault(key, []).append((idx, _ccw([tuple(p[uv]) for p in pts])))
    bad = []
    for key in sorted(buckets):
        lst = buckets[key]
        for i in range(len(lst)):
            ia, pa = lst[i]
            la = np.array(pa)
            for j in range(i + 1, len(lst)):
                ib, pb = lst[j]
                if ia == ib:
                    continue
                lb = np.array(pb)
                if (la[:, 0].max() <= lb[:, 0].min() + tol or lb[:, 0].max() <= la[:, 0].min() + tol
                        or la[:, 1].max() <= lb[:, 1].min() + tol
                        or lb[:, 1].max() <= la[:, 1].min() + tol):
                    continue
                if any(names[ia].startswith(x) and names[ib].startswith(y) or
                       names[ib].startswith(x) and names[ia].startswith(y) for x, y in exempt):
                    continue
                inter = list(pa)
                for k in range(len(pb)):
                    inter = _clip(inter, pb[k], pb[(k + 1) % len(pb)])
                    if not inter:
                        break
                ar = abs(_area2(inter)) if len(inter) >= 3 else 0.0
                if ar > min_area:
                    bad.append((names[ia], names[ib], round(ar, 4), key))
    if bad:
        raise RuntimeError(f"G10 coplanar overlap (z-fight) between parts: {bad[:8]}")
    return 0


def check_inside(parts, volumes, shrink=0.01, exempt=()):
    """G10b (skeptic M4): no face centroid strictly inside a massing volume ((lo, hi) boxes shrunk by
    `shrink`). Parts whose name starts with `exempt` are skipped."""
    names = [o.name for o, _k in parts]
    bad = []
    import cs2_bldg as _K
    if _K.WINDOW_KIT["version"] == 2:       # window kit v2: liners, sash, bars, panes sit in the reveal
        exempt = tuple(exempt) + _K.KIT2_RECESSED + ("win_",)
    if _K.DOOR_KIT["version"] == 2:         # door kit v2: recessed leaves, liners, thresholds
        exempt = tuple(exempt) + _K.door2_protect() + ("win_",)
    # structure v1 (T58): a bracket's members start STRUCT_WALL_EMBED inside their wall (a declared embed)
    seated = ("bracket_",) if _K.STRUCTURE["version"] >= 1 else ()
    for idx, _n, pts in _world_polys(parts):
        if names[idx].startswith(tuple(exempt)) if exempt else False:
            continue
        c = pts.mean(0)
        sh = max(shrink, 2.0 * _K.STRUCT_WALL_EMBED) if seated and names[idx].startswith(seated) else shrink
        for vi, (lo, hi) in enumerate(volumes):
            if all(lo[a] + sh < c[a] < hi[a] - sh for a in range(3)):
                bad.append((names[idx], vi, tuple(round(float(x), 3) for x in c)))
                break
    if bad:
        raise RuntimeError(f"G10 faces inside a massing volume: {bad[:8]}")
    return 0


# ======================================================================================
# Oriented painted labels (N5)
# ======================================================================================

def find_baking_mesh(prefix):
    """The one mesh with a UVUnique layer whose part table holds `prefix` (cs2_bake's post_compose runs on
    it, LOD copies do not exist yet). Raises unless exactly one."""
    cands = []
    for me in sorted(bpy.data.meshes, key=lambda m: m.name):
        if me.users == 0 or UNIQUE not in me.uv_layers:
            continue
        raw = me.get(cs2_asset.PART_TABLE_PROP)
        if raw is None:
            continue
        if any(n.startswith(prefix) for n in json.loads(raw)):
            cands.append(me)
    if len(cands) != 1:
        raise RuntimeError(f"label {prefix!r}: expected one baking mesh, got "
                           f"{[m.name for m in cands]}")
    return cands[0]


def uv_frame(me, prefix, layer=UNIQUE, A=1.0):
    """World vectors (Wu, Wv) per texel (A texels per UV unit) and the outward normal of the FIRST polygon of
    part `prefix` (least squares over its loops, the obj transform is applied). The face must be planar
    and its UV map affine."""
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    hits = {i + 1 for i, n in enumerate(table) if n.startswith(prefix)}
    attr = me.attributes.get("part")
    pv = np.zeros(len(me.polygons), np.int32)
    attr.data.foreach_get("value", pv)
    polys = [p for p in me.polygons if int(pv[p.index]) in hits]
    if not polys:
        raise RuntimeError(f"uv_frame: no faces for {prefix!r}")
    objs = [o for o in bpy.data.objects if o.data == me]
    M = objs[0].matrix_world if objs else None
    lay = me.uv_layers[layer]
    p = max(polys, key=lambda q: q.area)
    P, Q = [], []
    for li in range(p.loop_start, p.loop_start + p.loop_total):
        co = me.vertices[me.loops[li].vertex_index].co
        P.append(tuple(M @ co) if M is not None else tuple(co))
        Q.append(tuple(lay.data[li].uv))
    P = np.array(P, np.float64)
    Q = np.array(Q, np.float64) * float(A)
    dq = Q[1:] - Q[0]
    dp = P[1:] - P[0]
    W, *_ = np.linalg.lstsq(dq, dp, rcond=None)
    n = np.array(tuple((M.to_3x3() @ p.normal) if M is not None else p.normal), np.float64)
    n /= max(np.linalg.norm(n), 1e-12)
    return W[0], W[1], n


def display_map(Wu, Wv, n):
    """Coefficients (a, b, c, d): texel offsets (dj along u, di along v) map to the
    viewer's (right, up) as right = a dj + c di, up = b dj + d di. The viewer stands
    outside the face (looking along -n), world up +Z. Asserts an axis-aligned,
    isotropic frame (no shear, |det| = 1)."""
    up = np.array((0.0, 0.0, 1.0))
    right = np.cross(-n, up)
    if np.linalg.norm(right) < 0.5:
        raise RuntimeError("display_map: label face is not vertical")
    right /= np.linalg.norm(right)
    lu, lv = np.linalg.norm(Wu), np.linalg.norm(Wv)
    if abs(lu - lv) > 0.02 * max(lu, lv):
        raise RuntimeError(f"display_map: anisotropic texels ({lu:.5f} vs {lv:.5f} m)")
    a, b = float(Wu @ right / lu), float(Wu @ up / lu)
    c, d = float(Wv @ right / lv), float(Wv @ up / lv)
    for x in (a, b, c, d):
        if min(abs(x), abs(abs(x) - 1.0)) > AX_TOL * 10:
            raise RuntimeError(f"display_map: label UV frame not axis-aligned "
                               f"({a:.3f}, {b:.3f}, {c:.3f}, {d:.3f})")
    a, b, c, d = (float(round(x)) for x in (a, b, c, d))
    if abs(a * d - b * c) != 1.0:
        raise RuntimeError(f"display_map: degenerate frame ({a}, {b}, {c}, {d})")
    return a, b, c, d


def texel_to_display(h, w, coeffs):
    """Index maps (drow, dcol) (each (h, w)) from a bottom-up texel block to a top-down DISPLAY image of size
    (Hv, Wv) as the viewer sees the face. Returns (drow, dcol, Hv, Wv). A swapped frame (u vertical) gives
    Hv = w, Wv = h."""
    a, b, c, d = coeffs
    ii, jj = np.meshgrid(np.arange(h), np.arange(w), indexing="ij")
    ci, cj = ii - (h - 1) / 2.0, jj - (w - 1) / 2.0
    right = a * cj + c * ci
    upv = b * cj + d * ci
    Hv, Wv = (h, w) if b == 0.0 else (w, h)
    drow = np.clip(np.rint((Hv - 1) / 2.0 - upv).astype(int), 0, Hv - 1)
    dcol = np.clip(np.rint((Wv - 1) / 2.0 + right).astype(int), 0, Wv - 1)
    return drow, dcol, Hv, Wv


class GlyphPainter:
    """Painter for cs2_compose.stamp_on_face: a ski_launcher_glyph primitive list (`prims` = function name,
    e.g. "cand_d" the skier on a slope) drawn white at `frac` of the face, oriented by the face's UV ->
    world frame (N5: the glyph is asymmetric, the host material may carry uv_rot 90). Paints only the
    glyph: stamp_on_face then zeroes ControlMask R under it, the background keeps the host's slot color."""

    def __init__(self, face_prefix, prims="cand_d", frac=0.85, fg_srgb=0.92):
        self.face = face_prefix
        self.prims = prims
        self.frac = float(frac)
        self.fg = float(fg_srgb)
        self.__name__ = f"glyph_{prims}"
        self.last = None

    def __call__(self, rect, base, ctl):
        import ski_launcher_glyph as G
        me = find_baking_mesh(self.face)
        Wu, Wv, n = uv_frame(me, self.face)
        coeffs = display_map(Wu, Wv, n)
        h, w = base.shape[0], base.shape[1]
        drow, dcol, Hv, Wvw = texel_to_display(h, w, coeffs)
        s = max(8, int(self.frac * min(Hv, Wvw)))
        glyph = G.render_alpha(getattr(G, self.prims)(), s, ss=8)     # top-down
        disp = np.zeros((Hv, Wvw), np.float32)
        r0, c0 = (Hv - s) // 2, (Wvw - s) // 2
        disp[r0:r0 + s, c0:c0 + s] = glyph
        cov = disp[drow, dcol]
        fl = srgb_lin(self.fg)
        for ch in range(3):
            base[..., ch] = base[..., ch] * (1.0 - cov) + fl * cov
        self.last = dict(rect=tuple(int(x) for x in rect), size_px=s, coeffs=coeffs,
                         coverage=float(cov.mean()))
        print(f"[civic] glyph {self.prims} on {self.face}: {s} px of {h} x {w}, frame {coeffs}")


class BarsPainter:
    """P1 info board: white field and three horizontal color bars (blue, green, short
    blue), left-aligned as the viewer sees the board (contract 3.5)."""

    BARS = ((0.78, 1.00, (0.05, 0.20, 0.45)), (0.52, 0.80, (0.05, 0.30, 0.12)),
            (0.26, 0.55, (0.05, 0.20, 0.45)))     # (center height 0..1, length, LINEAR rgb)

    def __init__(self, face_prefix, white_srgb=0.90):
        self.face = face_prefix
        self.white = float(white_srgb)
        self.__name__ = "bars_info"
        self.last = None

    def __call__(self, rect, base, ctl):
        me = find_baking_mesh(self.face)
        Wu, Wv, n = uv_frame(me, self.face)
        coeffs = display_map(Wu, Wv, n)
        h, w = base.shape[0], base.shape[1]
        drow, dcol, Hv, Wvw = texel_to_display(h, w, coeffs)
        wl = srgb_lin(self.white)
        disp = np.full((Hv, Wvw, 3), wl, np.float32)
        yy, xx = np.meshgrid((np.arange(Hv) + 0.5) / Hv, (np.arange(Wvw) + 0.5) / Wvw,
                             indexing="ij")
        for cy, ln, rgb in self.BARS:
            m = (np.abs((1.0 - yy) - cy) < 0.065) & (xx > 0.14) & (xx < 0.14 + 0.72 * ln)
            disp[m] = rgb
        base[..., 0:3] = disp[drow, dcol]
        self.last = dict(rect=tuple(int(x) for x in rect), coeffs=coeffs)
        print(f"[civic] info bars on {self.face}: {h} x {w}, frame {coeffs}")
