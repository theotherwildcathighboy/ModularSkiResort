"""
cs2_compose.py - detail layer and compositing for the CS2 ski-resort art pipeline
(art-pipeline-v2.md section 3.5; round L spec "cs2_compose.py").

Contract
--------
Every map here is a numpy float array in LINEAR space, laid out BOTTOM-UP: row 0 is v = 0,
so d/drow == dh/dv.  cs2_asset._write_png_raw writes such buffers as-is, and Cycles bake
targets read back through Image.pixels are already bottom-up.  Callers do the sRGB encode
of BaseColor afterwards (lift_tower.apply_finish_textures:1038-1048) - nothing in this
module touches colour spaces.

    height_to_normal(height_m, ppm, strength=1.0) -> (H, W, 3) float 0..1
        THE single height -> tangent-normal implementation (CA-03 / P-08).  OpenGL sign:
        G = 0.5 - k * dh/dv with bottom-up rows, R = 0.5 - k * dh/du, B > 0.5.
    blend_rnm(n1, n2) -> (H, W, 3)
        Reoriented normal blend (Barre-Brisebois / Hill) of two encoded 0..1 maps.
        blend_rnm(flat, x) == x, blend_rnm(x, flat) == x.
    DetailSpec
        Per-part feature description in METRES.  detail_height keys it by the INT face
        attribute "part" -> part name (json table in me[cs2_asset.PART_TABLE_PROP]) ->
        longest matching prefix in `specs`.
    detail_height(me, A, specs, uv_layer="UVUnique") -> (A, A) float metres
        Rasterises seams / bolt rows / corrugation / helix / grate per face into a height
        field.  Features are anchored to the face's WORLD coordinates (the two world axes
        most orthogonal to the face normal, projected to UV through the face's affine
        UV -> 3D map), so a seam grid is continuous across the faces of one part.  The
        helix uses the face's longest-edge frame instead (ropes are rarely axis-aligned).
        TEXEL FLOOR: a feature whose characteristic size is < 2 texels at the face's px/m
        is skipped for that face (sub-texel relief only adds aliasing).
    compose_maps(base, nrm, msk, ctl, ao, height_m, ppm_map, opts) -> (base, nrm, msk, ctl)
        Linear-space compositing in the plan's order: per-part value noise, curvature,
        AO (occ = ao*0.92+0.08, reserved rects skipped), ground-contact band, detail
        normal via blend_rnm(nrm, height_to_normal(height_m, ppm_map)), gloss variation,
        MaskMap B = 0, ControlMask A = 0, BaseColor A = 1.  Reserved rects are restored
        1:1 in all four maps.  Returns NEW arrays; inputs are not modified.
    stamp_on_face(base, ctl, me, part_table, prefix, painter, uv_layer="UVUnique") -> None
        Calls painter(rect_px, base_slice, ctl_slice) on the UV bounding box of every face
        whose part name starts with `prefix`; ControlMask R is zeroed under the texels the
        painter changed (labels are painted, never masked, never alpha-clipped).
    rasterise_face_fields(me, A, uv_layer="UVUnique") -> (part_map int32, ppm_map float32)
        Per-texel part index (0 = no face) and px/m, the inputs compose_maps wants.

Numpy only.  No top-level side effects.  Imports cs2_asset for PART_TABLE_PROP only.
"""

import json
import math
from dataclasses import dataclass

import numpy as np

import cs2_asset


# --------------------------------------------------------------------------------------
# Normal maths
# --------------------------------------------------------------------------------------

def height_to_normal(height_m, ppm, strength=1.0):
    """
    Height field (metres, bottom-up rows) -> tangent-space normal map, encoded 0..1,
    OpenGL convention.

    `ppm` is texels per metre: a scalar or an (H, W) array (per-texel density from
    rasterise_face_fields).  np.gradient returns metres per texel along rows (dh/dv) and
    columns (dh/du); multiplying by ppm gives the dimensionless slope.  The unnormalised
    normal is (-dh/du, -dh/dv, 1), so after the 0.5 + 0.5*n encode

        R = 0.5 - k * dh/du,   G = 0.5 - k * dh/dv,   B = 0.5 + 0.5/len  (> 0.5)

    with k = 0.5 * strength * ppm / len.  A bump therefore has G > 0.5 on the flank whose
    normal tilts toward +V (height falling as v grows) - the GL "lit from the top" look.
    """
    h = np.asarray(height_m, dtype=np.float32)
    if h.ndim != 2:
        raise ValueError(f"height_to_normal: expected (H, W), got {h.shape}")
    p = np.asarray(ppm, dtype=np.float32)
    gy, gx = np.gradient(h)                      # gy = dh/drow = dh/dv, gx = dh/du
    nx = -gx * p * float(strength)
    ny = -gy * p * float(strength)
    nz = np.ones_like(h)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / ln, ny / ln, nz / ln], axis=-1)
    return (n * 0.5 + 0.5).astype(np.float32)


def blend_rnm(n1, n2):
    """
    Reoriented normal mapping: rotate detail normal `n2` into the frame of base normal
    `n1`.  Both are encoded 0..1 arrays (H, W, 3) or (H, W, 4) (alpha ignored); the
    result is (H, W, 3) encoded.  Identity for a flat partner on either side.
    """
    a = np.asarray(n1, dtype=np.float32)[..., :3]
    b = np.asarray(n2, dtype=np.float32)[..., :3]
    t = a * np.array([2.0, 2.0, 2.0], np.float32) + np.array([-1.0, -1.0, 0.0], np.float32)
    u = b * np.array([-2.0, -2.0, 2.0], np.float32) + np.array([1.0, 1.0, -1.0], np.float32)
    r = t * np.sum(t * u, axis=-1, keepdims=True) - u * t[..., 2:3]
    ln = np.linalg.norm(r, axis=-1, keepdims=True)
    ln = np.where(ln < 1e-12, 1.0, ln)
    out = r / ln * 0.5 + 0.5
    return np.clip(out, 0.0, 1.0).astype(np.float32)


FLAT_NORMAL = (0.5, 0.5, 1.0)


# --------------------------------------------------------------------------------------
# Detail specification
# --------------------------------------------------------------------------------------

@dataclass
class DetailSpec:
    """
    Relief features for one part prefix, all sizes in metres.  A feature is active when
    its pitch/period is not None.  Fields beyond the spec's list carry defaults the
    plan's pseudo-code hard-wired (seam width 0.012, bolt offset 0.15, bolt height
    0.004, 3 strands) plus the axis choice for seams.

    seams_m      panel-join pitch; lines of depth -seam_depth_m on a world-anchored grid
    seam_axes    "both" grid | "rings" horizontal joins only (constant height) |
                 "ribs" vertical joins only
    bolt_pitch_m bolt spacing along a seam; bolt rows sit bolt_offset_m either side of
                 every seam (or on a plain lattice when seams_m is None)
    corrugation_m sine period across the face's most horizontal in-plane world axis
                 (ridges run vertically on walls, down the slope on roofs)
    helix_m      rope lay length; `helix_strands` grooves per period along the face's
                 longest edge
    grate_m      bearing-bar pitch of an opaque painted grating (holes -grate_depth_m)
    """
    seams_m: float = None
    seam_depth_m: float = 0.002
    bolt_pitch_m: float = None
    bolt_r_m: float = 0.008
    corrugation_m: float = None
    corr_depth_m: float = 0.002
    helix_m: float = None
    grate_m: float = None
    # extensions (defaults match the plan's pseudo-code)
    seam_width_m: float = 0.012
    seam_axes: str = "both"
    bolt_offset_m: float = 0.15
    bolt_h_m: float = 0.004
    helix_depth_m: float = 0.002
    helix_strands: int = 3
    grate_depth_m: float = 0.010

    def active(self):
        return any(v is not None for v in (self.seams_m, self.bolt_pitch_m,
                                           self.corrugation_m, self.helix_m, self.grate_m))


TEXEL_FLOOR = 2.0    # features smaller than this many texels are skipped per face


def _spec_for(name, specs):
    """Longest prefix in `specs` that `name` starts with, else None."""
    best = None
    for prefix, spec in specs.items():
        if name.startswith(prefix) and (best is None or len(prefix) > len(best[0])):
            best = (prefix, spec)
    return None if best is None else best[1]


# --------------------------------------------------------------------------------------
# Mesh readers
# --------------------------------------------------------------------------------------

def read_part_table(me, part_table=None):
    """The part-name table: explicit list, else the json custom prop on the mesh."""
    if part_table is not None:
        return list(part_table)
    raw = me.get(cs2_asset.PART_TABLE_PROP)
    if raw is None:
        raise RuntimeError(f"{me.name}: no {cs2_asset.PART_TABLE_PROP} custom prop "
                           "(call cs2_asset.store_part_table first)")
    return list(json.loads(raw))


def part_indices(me):
    """Per-polygon INT face attribute "part" (1-based; 0 where untagged)."""
    attr = me.attributes.get("part")
    out = np.zeros(len(me.polygons), dtype=np.int32)
    if attr is not None and attr.domain == "FACE":
        attr.data.foreach_get("value", out)
    return out


def read_uvs(me, uv_layer="UVUnique"):
    """(n_loops, 2) float32 UVs of the named layer."""
    layer = me.uv_layers.get(uv_layer)
    if layer is None:
        raise KeyError(f"{me.name}: no UV layer {uv_layer!r} "
                       f"(have {[l.name for l in me.uv_layers]})")
    n = len(me.loops)
    buf = np.empty(n * 2, dtype=np.float32)
    try:
        layer.uv.foreach_get("vector", buf)
    except (AttributeError, TypeError):
        layer.data.foreach_get("uv", buf)
    return buf.reshape(n, 2)


def _read_geometry(me, matrix=None):
    """Vertex coordinates (n, 3) (optionally transformed) and loop vertex indices."""
    nv = len(me.vertices)
    co = np.empty(nv * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(nv, 3)
    if matrix is not None:
        m = np.array([list(r) for r in matrix], dtype=np.float64)
        co = co @ m[:3, :3].T + m[:3, 3]
    lv = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", lv)
    return co, lv


def _poly_area3d(P):
    """Area of a planar 3D polygon (fan cross products)."""
    s = np.zeros(3)
    for i in range(1, len(P) - 1):
        s += np.cross(P[i] - P[0], P[i + 1] - P[0])
    return 0.5 * float(np.linalg.norm(s))


def _poly_area_uv(q):
    x, y = q[:, 0], q[:, 1]
    return 0.5 * float(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))


def _poly_normal(P):
    s = np.zeros(3)
    for i in range(1, len(P) - 1):
        s += np.cross(P[i] - P[0], P[i + 1] - P[0])
    ln = np.linalg.norm(s)
    return s / ln if ln > 0 else np.array([0.0, 0.0, 1.0])


def _tri_mask(q, X, Y, dilate):
    """Half-plane test of the (possibly dilated) convex polygon q (n, 2) in texels."""
    sgn = 1.0 if _poly_area_uv(q) >= 0 else -1.0
    inside = np.ones(X.shape, dtype=bool)
    n = len(q)
    for i in range(n):
        ax, ay = q[i]
        bx, by = q[(i + 1) % n]
        ex, ey = bx - ax, by - ay
        L = math.hypot(ex, ey)
        if L < 1e-9:
            continue
        cross = (ex * (Y - ay) - ey * (X - ax)) * sgn
        inside &= cross >= -dilate * L
    return inside


def _is_convex(q):
    n = len(q)
    if n <= 3:
        return True
    signs = []
    for i in range(n):
        a, b, c = q[i], q[(i + 1) % n], q[(i + 2) % n]
        signs.append((b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]))
    return all(s >= -1e-9 for s in signs) or all(s <= 1e-9 for s in signs)


def _poly_mask(q, X, Y, dilate):
    if _is_convex(q):
        return _tri_mask(q, X, Y, dilate)
    m = np.zeros(X.shape, dtype=bool)
    for i in range(1, len(q) - 1):
        m |= _tri_mask(q[[0, i, i + 1]], X, Y, dilate)
    return m


def iter_face_rasters(me, A, uv_layer="UVUnique", dilate_px=0.0, matrix=None):
    """
    Yield, per polygon with a non-degenerate UV footprint inside the A x A atlas:
        (poly, sl, mask, X, Y, q, P, ppm)
    sl = (rows, cols) slices of the face's texel bbox (bottom-up), mask = bool (h, w) of
    texel centres inside the (dilated) UV polygon, X/Y = texel-centre coordinates
    (texels), q = UV polygon in texels (n, 2), P = 3D vertices (n, 3), ppm = texels per
    metre of this face (A * sqrt(uv_area / area3d)).
    """
    uvs = read_uvs(me, uv_layer)
    co, lv = _read_geometry(me, matrix)
    for poly in me.polygons:
        ls, lt = poly.loop_start, poly.loop_total
        if lt < 3:
            continue
        q = uvs[ls:ls + lt].astype(np.float64) * A
        P = co[lv[ls:ls + lt]]
        auv = abs(_poly_area_uv(q))
        a3 = _poly_area3d(P)
        if auv < 1e-6 or a3 < 1e-12:
            continue
        x0 = max(0, int(math.floor(q[:, 0].min() - dilate_px)))
        x1 = min(A, int(math.ceil(q[:, 0].max() + dilate_px)))
        y0 = max(0, int(math.floor(q[:, 1].min() - dilate_px)))
        y1 = min(A, int(math.ceil(q[:, 1].max() + dilate_px)))
        if x1 <= x0 or y1 <= y0:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        mask = _poly_mask(q, X, Y, dilate_px)
        if not mask.any():
            continue
        ppm = A * math.sqrt(auv / (A * A) / a3)
        yield poly, (slice(y0, y1), slice(x0, x1)), mask, X, Y, q, P, ppm


def _affine_uv_to_world(q, P):
    """
    Least-squares affine map from UV (in texels) to 3D: P ~= Pm + (q - qm) @ Mt, with
    Mt (2, 3).  Exact for triangles and affinely mapped planar quads.
    Returns (qm, Pm, Mt).
    """
    qm = q.mean(axis=0)
    Pm = P.mean(axis=0)
    Mt, *_ = np.linalg.lstsq(q - qm, P - Pm, rcond=None)
    return qm, Pm, Mt


def _world_coord_field(axis, X, Y, qm, Pm, Mt):
    """Metre coordinate along unit 3D `axis` at every texel centre of the raster."""
    g = Mt @ axis                                       # d(axis . P) / d(u_tex, v_tex)
    return float(Pm @ axis) + (X - qm[0]) * g[0] + (Y - qm[1]) * g[1]


def _inplane_world_axes(n):
    """The two world axes most orthogonal to normal n, ordered [most horizontal, other]."""
    order = np.argsort(np.abs(n), kind="stable")[:2]
    axes = [np.eye(3)[i] for i in order]
    axes.sort(key=lambda e: abs(e[2]))                  # most horizontal first
    return axes[0], axes[1]


def _longest_edge_frame(P, n):
    """Unit vectors (along the longest edge, across = n x along)."""
    best, blen = None, -1.0
    for i in range(len(P)):
        e = P[(i + 1) % len(P)] - P[i]
        L = np.linalg.norm(e)
        if L > blen:
            best, blen = e / L, L
    across = np.cross(n, best)
    ln = np.linalg.norm(across)
    across = across / ln if ln > 1e-12 else np.array([0.0, 0.0, 1.0])
    return best, across


def _dist_to_lattice(s, pitch, offset=0.0):
    """|distance| from s to the nearest offset + k*pitch."""
    return np.abs((s - offset + 0.5 * pitch) % pitch - 0.5 * pitch)


def _seam_lines(s, pitch, half_w, depth):
    return np.where(_dist_to_lattice(s, pitch) <= half_w, -depth, 0.0)


# --------------------------------------------------------------------------------------
# Detail height
# --------------------------------------------------------------------------------------

def detail_height(me, A, specs, uv_layer="UVUnique", part_table=None, matrix=None,
                  dilate_px=1.0, verbose=True):
    """
    Rasterise the relief features of `specs` (dict part_prefix -> DetailSpec) into an
    (A, A) float32 height field in METRES on the unique layout of `me`.

    Keyed by the INT face attribute "part" -> `part_table[part-1]` (explicit list or the
    json custom prop written by cs2_asset.store_part_table) -> longest matching prefix.
    Faces whose part has no spec, or whose feature is below the 2-texel floor at that
    face's px/m, contribute nothing.  `dilate_px` grows every face footprint so the
    height (and thus the normal) reaches 1 texel into the gutter, matching the bake
    margin's EXTEND behaviour.  `matrix` (4x4) transforms the vertices first when the
    object carries an unapplied transform.
    """
    table = read_part_table(me, part_table)
    parts = part_indices(me)
    h = np.zeros((A, A), dtype=np.float32)
    skipped = {}
    drawn = {}
    for poly, sl, mask, X, Y, q, P, ppm in iter_face_rasters(me, A, uv_layer, dilate_px,
                                                              matrix):
        pi = int(parts[poly.index])
        if pi < 1 or pi > len(table):
            continue
        name = table[pi - 1]
        spec = _spec_for(name, specs)
        if spec is None or not spec.active():
            continue
        n = _poly_normal(P)
        qm, Pm, Mt = _affine_uv_to_world(q, P)
        e_h, e_v = _inplane_world_axes(n)
        s_h = _world_coord_field(e_h, X, Y, qm, Pm, Mt)
        s_v = _world_coord_field(e_v, X, Y, qm, Pm, Mt)
        f = np.zeros(X.shape, dtype=np.float64)
        texel_m = 1.0 / ppm

        def floor_ok(size_m, kind):
            if size_m * ppm >= TEXEL_FLOOR - 1e-6:      # epsilon: 2.0-texel features count
                return True
            skipped[kind] = skipped.get(kind, 0) + 1
            return False

        if spec.seams_m and floor_ok(spec.seams_m, "seams"):
            half_w = 0.5 * max(spec.seam_width_m, texel_m)
            if spec.seam_axes in ("both", "rings"):
                f += _seam_lines(s_v, spec.seams_m, half_w, spec.seam_depth_m)
            if spec.seam_axes in ("both", "ribs"):
                f = np.minimum(f, _seam_lines(s_h, spec.seams_m, half_w, spec.seam_depth_m))
            drawn["seams"] = drawn.get("seams", 0) + 1

        if spec.bolt_pitch_m and floor_ok(2.0 * spec.bolt_r_m, "bolts"):
            r = max(spec.bolt_r_m, texel_m)
            if spec.seams_m:
                da = np.minimum(_dist_to_lattice(s_v, spec.seams_m, spec.bolt_offset_m),
                                _dist_to_lattice(s_v, spec.seams_m, -spec.bolt_offset_m))
            else:
                da = _dist_to_lattice(s_v, spec.bolt_pitch_m, 0.5 * spec.bolt_pitch_m)
            db = _dist_to_lattice(s_h, spec.bolt_pitch_m, 0.5 * spec.bolt_pitch_m)
            d2 = da * da + db * db
            dome = np.sqrt(np.clip(1.0 - d2 / (r * r), 0.0, 1.0)) * spec.bolt_h_m
            f += dome                                   # heads sit ON the surface
            drawn["bolts"] = drawn.get("bolts", 0) + 1

        if spec.corrugation_m and floor_ok(0.5 * spec.corrugation_m, "corrugation"):
            f += spec.corr_depth_m * np.sin(2.0 * math.pi * s_h / spec.corrugation_m)
            drawn["corrugation"] = drawn.get("corrugation", 0) + 1

        if spec.helix_m and floor_ok(spec.helix_m / max(1, spec.helix_strands), "helix"):
            e_along, e_across = _longest_edge_frame(P, n)
            s_a = _world_coord_field(e_along, X, Y, qm, Pm, Mt)
            s_c = _world_coord_field(e_across, X, Y, qm, Pm, Mt)
            # groove k of a `strands`-lay rope: z = lay * theta/2pi + k*lay/strands.
            # theta is approximated by the across-coordinate over the rope radius
            # (continuous across adjacent faces: it is a world coordinate).
            span = float(np.ptp(s_c))
            r_est = max(1e-3, 0.5 * span) if span > 1e-6 else 0.02
            phase = (2.0 * math.pi * spec.helix_strands * s_a / spec.helix_m
                     - spec.helix_strands * (s_c / r_est))
            f += -spec.helix_depth_m * (0.5 - 0.5 * np.cos(phase))
            drawn["helix"] = drawn.get("helix", 0) + 1

        if spec.grate_m and floor_ok(spec.grate_m, "grate"):
            bar = max(0.35 * spec.grate_m, texel_m)
            hole_h = _dist_to_lattice(s_h, spec.grate_m, 0.5 * spec.grate_m) > 0.5 * bar
            hole_v = _dist_to_lattice(s_v, 4.0 * spec.grate_m, 2.0 * spec.grate_m) > 0.5 * bar
            f = np.where(hole_h & hole_v, -spec.grate_depth_m, f)
            drawn["grate"] = drawn.get("grate", 0) + 1

        view = h[sl]
        view[mask] += f[mask].astype(np.float32)
    if verbose:
        print(f"[compose] detail_height {A}: drawn {drawn or '{}'}; "
              f"skipped below {TEXEL_FLOOR:.0f} texels {skipped or '{}'}")
    return h


def rasterise_face_fields(me, A, uv_layer="UVUnique", matrix=None):
    """
    (part_map int32 (A, A), ppm_map float32 (A, A)): per texel the 1-based part index
    (0 = uncovered gutter) and the covering face's texels-per-metre (0 = uncovered).
    compose_maps uses part_map for the per-part noise and ppm_map for height_to_normal.
    """
    parts = part_indices(me)
    part_map = np.zeros((A, A), dtype=np.int32)
    ppm_map = np.zeros((A, A), dtype=np.float32)
    for poly, sl, mask, _X, _Y, _q, _P, ppm in iter_face_rasters(me, A, uv_layer, 0.0,
                                                                  matrix):
        pv = part_map[sl]
        dv = ppm_map[sl]
        pv[mask] = parts[poly.index]
        dv[mask] = ppm
    return part_map, ppm_map


# --------------------------------------------------------------------------------------
# Compositing
# --------------------------------------------------------------------------------------

def _rect_slices(rect, A):
    u0, v0, u1, v1 = rect
    return (slice(max(0, int(v0 * A)), min(A, int(math.ceil(v1 * A)))),
            slice(max(0, int(u0 * A)), min(A, int(math.ceil(u1 * A)))))


def value_noise(A, cells, rng):
    """Bilinear value noise in -1..1, `cells` lattice cells across the A x A map."""
    cells = max(1, int(cells))
    lat = rng.uniform(-1.0, 1.0, size=(cells + 1, cells + 1)).astype(np.float32)
    t = (np.arange(A) + 0.5) / A * cells
    i0 = np.clip(np.floor(t).astype(int), 0, cells - 1)
    fr = (t - i0).astype(np.float32)
    w0r, w1r = (1 - fr)[:, None], fr[:, None]
    w0c, w1c = (1 - fr)[None, :], fr[None, :]
    r0, r1 = i0[:, None], i0[:, None] + 1
    c0, c1 = i0[None, :], i0[None, :] + 1
    return (lat[r0, c0] * w0r * w0c + lat[r1, c0] * w1r * w0c
            + lat[r0, c1] * w0r * w1c + lat[r1, c1] * w1r * w1c)


def compose_maps(base, nrm, msk, ctl, ao, height_m, ppm_map, opts=None):
    """
    Composite the finished texture set in LINEAR space and return NEW
    (base, nrm, msk, ctl) float32 (A, A, 4) arrays.

    Inputs: base/msk/ctl (A, A, 4); nrm (A, A, 3|4); ao (A, A) or (A, A, >=1) channel 0
    (None skips AO); height_m (A, A) metres or None; ppm_map scalar or (A, A).
    opts keys (all optional):
        reserved     [(u0, v0, u1, v1)] rects restored 1:1 in every map and excluded
                     from AO (occ = 1 there)
        part_map     (A, A) int: per-part value noise +-noise_amp seeded by part
        noise_amp    default 0.03;  seed default 0
        convex, concave  (A, A) 0..1 curvature masks:
                     base *= 1 + 0.04*convex - 0.06*concave;
                     smooth += 0.10*convex - 0.08*concave
        ao_floor     default 0.08  (occ = ao*(1-floor) + floor)
        contact      (A, A) 0..1 ground-contact weight: base *= 1 - 0.06*contact
        strength     height_to_normal strength, default 1.0
        gloss_var    amplitude of low-frequency smoothness variation, default 0.03
        gloss_cells  lattice cells of that variation, default 16
    Always: MaskMap B = 0, ControlMask A = 0, BaseColor A = 1 (outside reserved rects;
    ControlMask A = 0 inside them too, it is a set-wide flag).
    """
    o = dict(opts or {})
    A = base.shape[0]
    base = np.array(base, dtype=np.float32, copy=True)
    msk = np.array(msk, dtype=np.float32, copy=True)
    ctl = np.array(ctl, dtype=np.float32, copy=True)
    n_in = np.asarray(nrm, dtype=np.float32)
    nrm = np.ones((A, A, 4), dtype=np.float32)
    nrm[..., :3] = n_in[..., :3]
    if n_in.shape[-1] > 3:
        nrm[..., 3] = n_in[..., 3]

    reserved = list(o.get("reserved") or [])
    keep = [(sl, base[sl].copy(), nrm[sl].copy(), msk[sl].copy(), ctl[sl].copy())
            for sl in (_rect_slices(r, A) for r in reserved)]
    rng = np.random.default_rng(int(o.get("seed", 0)))

    # 2. per-part value noise
    part_map = o.get("part_map")
    if part_map is not None:
        amp = float(o.get("noise_amp", 0.03))
        pm = np.asarray(part_map)
        fac = rng.uniform(-amp, amp, size=int(pm.max()) + 1).astype(np.float32)
        fac[0] = 0.0
        base[..., :3] *= (1.0 + fac[pm])[..., None]

    # 3. curvature
    convex, concave = o.get("convex"), o.get("concave")
    if convex is not None or concave is not None:
        cx = (np.zeros((A, A), np.float32) if convex is None
              else np.asarray(convex, np.float32))
        cc = (np.zeros((A, A), np.float32) if concave is None
              else np.asarray(concave, np.float32))
        base[..., :3] *= (1.0 + 0.04 * cx - 0.06 * cc)[..., None]
        msk[..., 3] += 0.10 * cx - 0.08 * cc

    # 4. AO (reserved rects skipped)
    if ao is not None:
        a = np.asarray(ao, dtype=np.float32)
        a = a[..., 0] if a.ndim == 3 else a
        floor = float(o.get("ao_floor", 0.08))
        occ = np.clip(a * (1.0 - floor) + floor, 0.0, 1.0)
        for r in reserved:
            occ[_rect_slices(r, A)] = 1.0
        base[..., :3] *= occ[..., None]

    # 5. ground-contact band
    contact = o.get("contact")
    if contact is not None:
        w = np.clip(np.asarray(contact, np.float32), 0.0, 1.0)
        base[..., :3] *= (1.0 - 0.06 * w)[..., None]

    # 6. detail normal
    if height_m is not None:
        det = height_to_normal(height_m, ppm_map, float(o.get("strength", 1.0)))
        nrm[..., :3] = blend_rnm(nrm[..., :3], det)

    # gloss variation (low frequency, whole map)
    gv = float(o.get("gloss_var", 0.03))
    if gv > 0:
        msk[..., 3] += gv * value_noise(A, int(o.get("gloss_cells", 16)), rng)

    # 8. channel rules
    base[..., :3] = np.clip(base[..., :3], 0.0, 1.0)
    base[..., 3] = 1.0
    msk[..., 2] = 0.0
    msk[..., 3] = np.clip(msk[..., 3], 0.0, 1.0)
    ctl[..., 3] = 0.0

    # reserved rects: exactly the caller's texels (ControlMask A stays 0 everywhere)
    for sl, b, n, m, c in keep:
        base[sl], nrm[sl], msk[sl], ctl[sl] = b, n, m, c
        ctl[sl][..., 3] = 0.0
    return base, nrm, msk, ctl


# --------------------------------------------------------------------------------------
# Labels
# --------------------------------------------------------------------------------------

def faces_of_prefix(me, part_table, prefix):
    """Polygon indices whose part name starts with `prefix`."""
    parts = part_indices(me)
    hits = {i + 1 for i, n in enumerate(part_table) if n.startswith(prefix)}
    return [i for i, p in enumerate(parts) if int(p) in hits]


def uv_bbox_px(me, part_table, prefix, A, uv_layer="UVUnique"):
    """(row0, row1, col0, col1) texel bounds of the face group's UV bbox, or None."""
    faces = faces_of_prefix(me, part_table, prefix)
    if not faces:
        return None
    uvs = read_uvs(me, uv_layer)
    idx = np.concatenate([np.arange(me.polygons[i].loop_start,
                                    me.polygons[i].loop_start + me.polygons[i].loop_total)
                          for i in faces])
    q = uvs[idx] * A
    r0 = max(0, int(math.floor(q[:, 1].min())))
    r1 = min(A, int(math.ceil(q[:, 1].max())))
    c0 = max(0, int(math.floor(q[:, 0].min())))
    c1 = min(A, int(math.ceil(q[:, 0].max())))
    if r1 <= r0 or c1 <= c0:
        return None
    return r0, r1, c0, c1


def stamp_on_face(base, ctl, me, part_table, prefix, painter, uv_layer="UVUnique"):
    """
    Paint a label onto the faces of one part group, at the host face's shipped density.
    `painter(rect_px, base_slice, ctl_slice)` receives (row0, row1, col0, col1) and
    IN-PLACE views of BaseColor / ControlMask over that UV bbox and paints what it
    likes.  Texels whose BaseColor changed get ControlMask R zeroed (no steel recolour
    over the label) and BaseColor alpha forced to 1 (labels are painted, never
    alpha-clipped).  Returns None.
    """
    A = base.shape[0]
    rect = uv_bbox_px(me, part_table, prefix, A, uv_layer)
    if rect is None:
        print(f"[compose] stamp_on_face: no faces for prefix {prefix!r}")
        return None
    r0, r1, c0, c1 = rect
    sl = (slice(r0, r1), slice(c0, c1))
    before = base[sl].copy()
    painter(rect, base[sl], ctl[sl])
    changed = np.any(np.abs(base[sl] - before) > 1e-6, axis=-1)
    ctl_view = ctl[sl]
    ctl_view[..., 0][changed] = 0.0
    base[sl][..., 3] = 1.0
    print(f"[compose] stamp_on_face {prefix!r}: rect rows {r0}-{r1} cols {c0}-{c1}, "
          f"{int(changed.sum())} texels painted")
    return None
