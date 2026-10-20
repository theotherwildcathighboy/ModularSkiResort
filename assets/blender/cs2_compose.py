"""
cs2_compose.py - detail layer and compositing for the CS2 ski-resort art pipeline
(art-pipeline-v2.md section 3.5; round L spec "cs2_compose.py").

Contract
--------
Every map here is a numpy float array in LINEAR space, laid out BOTTOM-UP: row 0 is v = 0,
so d/drow == dh/dv.  cs2_asset._write_png_raw writes such buffers as-is, and Cycles bake
targets read back through Image.pixels are already bottom-up.  Callers do the sRGB encode
of BaseColor afterwards (lift_tower.apply_finish_textures:1038-1048) - nothing in this
module touches color spaces.

    height_to_normal(height_m, ppm, strength=1.0) -> (H, W, 3) float 0..1
        THE single height -> tangent-normal implementation (CA-03 / P-08).  OpenGL sign:
        G = 0.5 - k * dh/dv with bottom-up rows, R = 0.5 - k * dh/du, B > 0.5.
    blend_rnm(n1, n2) -> (H, W, 3)
        Reoriented normal blend (Barre-Brisebois / Hill) of two encoded 0..1 maps.
        blend_rnm(flat, x) == x, blend_rnm(x, flat) == x.
    DetailSpec
        Per-part feature description in METERS.  detail_height keys it by the INT face
        attribute "part" -> part name (json table in me[cs2_asset.PART_TABLE_PROP]) ->
        longest matching prefix in `specs`.
    detail_height(me, A, specs, uv_layer="UVUnique") -> (A, A) float meters
        Rasterizes seams / bolt rows / corrugation / helix / grate per face into a height
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

Detail pass 1 (towers, design/detail-pass-1-towers.md section 2; additive - with the new
fields / opts unset every function above is byte-identical to round L):
    DetailSpec.edge_round_m / edge_round_deg / edge_round_tilt_deg
        normal-map round on the part's own convex creases (detail_height)
    DetailSpec.marks + Mark(kind, center, ...)
        world-space plate / slot / bolts / ring / boltring / band relief (detail_height)
    DetailSpec.mottle_m / mottle_amp / mottle_smooth / mottle_stretch / chalk_amp / chalk_smooth
    detail_tone(me, A, specs, ...) -> (dl, ds)
        world-anchored hashed mottle + up-face chalking; feed compose_maps opts
        tone_l / tone_s
    Decal(...) + decal_on_faces(base, ctl, me, A, part_table, prefix, decal, ...) -> rects
        anti-aliased painted plate projected along u x v onto a part's faces
    transform_marks(items, matrix) -> tuple
        Marks / Decals from the authoring frame into the bake-time (net) frame
    LAST_DETAIL_STATS / LAST_TONE_STATS: the drawn / skipped counts of the last call

Numpy only.  No top-level side effects.  Imports cs2_asset for PART_TABLE_PROP only.
"""

import dataclasses
import functools
import json
import math
import zlib
from dataclasses import dataclass

import numpy as np

import cs2_asset


# --------------------------------------------------------------------------------------
# Normal math
# --------------------------------------------------------------------------------------

def height_to_normal(height_m, ppm, strength=1.0):
    """
    Height field (meters, bottom-up rows) -> tangent-space normal map, encoded 0..1,
    OpenGL convention.

    `ppm` is texels per meter: a scalar or an (H, W) array (per-texel density from
    rasterise_face_fields).  np.gradient returns meters per texel along rows (dh/dv) and
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
    Relief features for one part prefix, all sizes in meters.  A feature is active when
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

    Detail pass 1 (towers, design/detail-pass-1-towers.md section 2) - trailing fields, all
    off by default, so positional construction and every existing map are unchanged:
    edge_round_m        normal-map round on the part's own convex creases (radius, meters;
                        never on a prefix that already has a geometric bevel)
    edge_round_deg      min dihedral (angle between face normals) for a crease, default 30
    edge_round_tilt_deg normal tilt at the crease, default 50
    marks               tuple of world-space Mark (plate / slot / bolts / ring / boltring /
                        band) drawn as height by detail_height
    mottle_m            tone cell (m) for detail_tone; None = no mottle
    mottle_amp / mottle_smooth   +- luminance fraction / +- MaskMap A (times MOTTLE_STD = the
                        noise's measured std, so A std gains ~ mottle_smooth * 0.234)
    mottle_stretch      cell x stretch along the face's longest edge (the member axis)
    chalk_amp / chalk_smooth     luminance lift / smoothness drop on up-facing faces
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
    # detail pass 1 (towers): trailing, default off
    edge_round_m: float = None
    edge_round_deg: float = 30.0
    edge_round_tilt_deg: float = 50.0
    marks: tuple = ()
    mottle_m: float = None
    mottle_amp: float = 0.0
    mottle_smooth: float = 0.0
    mottle_stretch: float = 1.0
    chalk_amp: float = 0.0
    chalk_smooth: float = 0.0

    def _legacy_active(self):
        """The round-L features (seams / bolts / corrugation / helix / grate)."""
        return any(v is not None for v in (self.seams_m, self.bolt_pitch_m,
                                           self.corrugation_m, self.helix_m, self.grate_m))

    def _relief_new(self):
        """Detail pass 1 relief: edge rounding or marks."""
        return self.edge_round_m is not None or bool(self.marks)

    def active(self):
        return self._legacy_active() or self._relief_new()

    def tone_active(self):
        """detail_tone has work: mottle with a non-zero amplitude, or chalk."""
        mottle = self.mottle_m is not None and bool(self.mottle_amp or self.mottle_smooth)
        return bool(mottle or self.chalk_amp or self.chalk_smooth)


TEXEL_FLOOR = 2.0    # features smaller than this many texels are skipped per face


def _wants_relief_new(spec):
    """Detail pass 1 relief on this spec (False for None or a duck-typed round-L spec)."""
    f = getattr(spec, "_relief_new", None)
    return bool(f()) if f is not None else False


def _wants_legacy(spec):
    """Round-L features on this spec (a duck-typed spec without the split: True, as before)."""
    f = getattr(spec, "_legacy_active", None)
    return bool(f()) if f is not None else True


def _wants_tone(spec):
    f = getattr(spec, "tone_active", None)
    return bool(f()) if f is not None else False


def _spec_for(name, specs):
    """Longest prefix in `specs` that `name` starts with, else None."""
    best = None
    for prefix, spec in specs.items():
        if name.startswith(prefix) and (best is None or len(prefix) > len(best[0])):
            best = (prefix, spec)
    return None if best is None else best[1]


def _prefix_spec_for(name, specs):
    """(prefix, spec) of the longest prefix in `specs` that `name` starts with, else
    (None, None).  Same rule as _spec_for (detail_tone seeds its noise by the prefix)."""
    best = None
    for prefix, spec in specs.items():
        if name.startswith(prefix) and (best is None or len(prefix) > len(best[0])):
            best = (prefix, spec)
    return (None, None) if best is None else best


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
    texel centers inside the (dilated) UV polygon, X/Y = texel-center coordinates
    (texels), q = UV polygon in texels (n, 2), P = 3D vertices (n, 3), ppm = texels per
    meter of this face (A * sqrt(uv_area / area3d)).
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
    """Meter coordinate along unit 3D `axis` at every texel center of the raster."""
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
    Rasterize the relief features of `specs` (dict part_prefix -> DetailSpec) into an
    (A, A) float32 height field in METERS on the unique layout of `me`.

    Keyed by the INT face attribute "part" -> `part_table[part-1]` (explicit list or the
    json custom prop written by cs2_asset.store_part_table) -> longest matching prefix.
    Faces whose part has no spec, or whose feature is below the 2-texel floor at that
    face's px/m, contribute nothing.  `dilate_px` grows every face footprint so the
    height (and thus the normal) reaches 1 texel into the gutter, matching the bake
    margin's EXTEND behavior.  `matrix` (4x4) transforms the vertices first when the
    object carries an unapplied transform.

    Detail pass 1 relief (DetailSpec.edge_round_m / .marks, section 2.2 / 2.3) is written
    into its own field with CORE PRIORITY: a texel inside a face's undilated footprint
    takes that face's value, a dilation-only texel takes the first face's value unless
    some face's core claims it - so a feature is continuous (not doubled) across the
    shared edges of a contiguous UV strip.  It is then added to the round-L field.  With
    no new field set nothing of this runs and the result is byte-identical to round L.
    The log line gains `edge_round` / `marks:<kind>` drawn / skipped counts (faces); the
    same counts are kept in LAST_DETAIL_STATS {"drawn": {...}, "skipped": {...}}.
    """
    table = read_part_table(me, part_table)
    parts = part_indices(me)
    h = np.zeros((A, A), dtype=np.float32)
    skipped = {}
    drawn = {}
    new_specs = [s for s in specs.values() if _wants_relief_new(s)]
    hn = own = topo = None
    if new_specs:
        hn = np.zeros((A, A), dtype=np.float32)
        own = np.zeros((A, A), dtype=bool)
        if any(s.edge_round_m is not None for s in new_specs):
            topo = _EdgeTopology(me, parts, matrix)
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

        def floor_ok(size_m, kind):
            if size_m * ppm >= TEXEL_FLOOR - 1e-6:      # epsilon: 2.0-texel features count
                return True
            skipped[kind] = skipped.get(kind, 0) + 1
            return False

        if _wants_relief_new(spec):
            fn = _relief_new_field(spec, poly.index, X, Y, q, P, n, qm, Pm, Mt, ppm, mask,
                                   topo, floor_ok, drawn)
            core = mask if dilate_px <= 0 else (_poly_mask(q, X, Y, 0.0) & mask)
            _write_core_priority((hn,), own, sl, mask, core, (fn,))
        if not _wants_legacy(spec):
            continue
        e_h, e_v = _inplane_world_axes(n)
        s_h = _world_coord_field(e_h, X, Y, qm, Pm, Mt)
        s_v = _world_coord_field(e_v, X, Y, qm, Pm, Mt)
        f = np.zeros(X.shape, dtype=np.float64)
        texel_m = 1.0 / ppm

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
    if hn is not None:
        h += hn
    LAST_DETAIL_STATS.clear()
    LAST_DETAIL_STATS.update({"drawn": dict(drawn), "skipped": dict(skipped)})
    if verbose:
        print(f"[compose] detail_height {A}: drawn {drawn or '{}'}; "
              f"skipped below {TEXEL_FLOOR:.0f} texels {skipped or '{}'}")
    return h


def rasterise_face_fields(me, A, uv_layer="UVUnique", matrix=None):
    """
    (part_map int32 (A, A), ppm_map float32 (A, A)): per texel the 1-based part index
    (0 = uncovered gutter) and the covering face's texels-per-meter (0 = uncovered).
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
    (None skips AO); height_m (A, A) meters or None; ppm_map scalar or (A, A).
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
        tone_l       (A, A) luminance fraction from detail_tone: base *= 1 + tone_l,
                     right after the per-part noise, before curvature / AO
        tone_s       (A, A) from detail_tone: MaskMap A += tone_s, before the clip
    Always: MaskMap B = 0, ControlMask A = 0, BaseColor A = 1 (outside reserved rects;
    ControlMask A = 0 inside them too, it is a set-wide flag).  Without tone_l / tone_s
    the result is byte-identical to round L (no RNG draw moves).
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

    # 2b. detail tone (detail pass 1): mottle / chalk luminance from detail_tone
    tone_l = o.get("tone_l")
    if tone_l is not None:
        base[..., :3] *= (1.0 + np.asarray(tone_l, np.float32))[..., None]

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

    # 7b. detail tone (detail pass 1): mottle / chalk smoothness from detail_tone
    tone_s = o.get("tone_s")
    if tone_s is not None:
        msk[..., 3] += np.asarray(tone_s, np.float32)

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
    likes.  Texels whose BaseColor changed get ControlMask R zeroed (no steel recolor
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


# --------------------------------------------------------------------------------------
# Detail pass 1 (towers): edge rounding, marks, tone, decals
# (design/detail-pass-1-towers.md section 2; additive - nothing above changes when the
# new DetailSpec fields are left at their defaults)
# --------------------------------------------------------------------------------------

LAST_DETAIL_STATS = {}   # detail_height: {"drawn": {kind: faces}, "skipped": {kind: faces}}
LAST_TONE_STATS = {}     # detail_tone:   same layout; kinds mottle / chalk / chalk_noise
TONE_FLOOR = 4.0         # a tone cell smaller than this many texels is skipped per face
MOTTLE_STD = 0.234       # std of the [-1, 1] 2-octave mottle noise (measured, TC4): MaskMap A
                         # std gained by a part ~ mottle_smooth * MOTTLE_STD
MARK_KINDS = ("plate", "slot", "bolts", "ring", "boltring", "band")

_SALT_OCT2 = 0x9E3779B9
_SALT_SMOOTH = 0x5BD1E995
_SALT_CHALK = 0x27D4EB2F
_M32 = np.uint64(0xFFFFFFFF)
_HASH_K = (np.uint64(0x8DA6B343), np.uint64(0xD8163841), np.uint64(0xCB1AB31F))


def _vec3(x, what):
    try:
        a = tuple(float(c) for c in x)
    except TypeError:
        raise ValueError(f"{what}: expected 3 numbers, got {x!r}") from None
    if len(a) != 3:
        raise ValueError(f"{what}: expected 3 numbers, got {x!r}")
    return a


def _unit(v, what):
    a = np.asarray(v, dtype=np.float64)
    ln = float(np.linalg.norm(a))
    if ln < 1e-12:
        raise ValueError(f"{what}: zero-length vector")
    return a / ln


@dataclass(frozen=True)
class Mark:
    """
    One world-space relief mark drawn by detail_height (DetailSpec.marks), meters.

    kind      plate | slot | bolts | ring | boltring | band
    center    world point (the plate center, the ring / band axis point)
    u, v      in-plane axes of plate / slot / bolts (v is orthonormalised against u);
              u also sets the boltring phase
    n         ring / boltring face normal and band axis; None = u x v
    Faces:  plate / slot / bolts / ring / boltring draw on faces whose normal is within
    `ang_deg` of u x v (ring / boltring: of n) - signed, so a back face never matches -
    AND whose plane lies within `reach_m` of the center.  band draws on any face of the
    part (normal-free) whose texels are within |(p - c).n| <= half_w and radial distance
    from the axis <= r_max (r_max <= 0 = unlimited).
    Shapes (h = height, default +0.004; slot is always recessed, -|h|):
      plate     raised rectangle +-half_u x +-half_v, 1-texel smoothstep rim
      slot      recessed rounded rectangle (corner radius 0.5 * min half), 1-texel rim
      bolts     domes of radius bolt_r on a grid centered in the rectangle: per axis
                max(1, floor(2 * half / pitch)) bolts at pitch spacing (half 0 = one row)
      ring      annulus r_in..r_out about n (r_in 0 = disc), 1-texel rim
      boltring  `count` domes of radius bolt_r on a circle of `radius`, the first on +u
      band      cosine bead h * 0.5 * (1 + cos(pi * t / half_w)) across |t| <= half_w
    Texel floor (TEXEL_FLOOR = 2 texels at the face's px/m, per face; skipped faces are
    counted under "marks:<kind>"): plate / slot 2 * min(half_u, half_v); bolts /
    boltring 2 * bolt_r; ring r_out - r_in; band 2 * half_w.
    """
    kind: str
    centre: tuple
    u: tuple = (1.0, 0.0, 0.0)
    v: tuple = (0.0, 0.0, 1.0)
    n: tuple = None
    half_u: float = 0.0
    half_v: float = 0.0
    r_in: float = 0.0
    r_out: float = 0.0
    radius: float = 0.0
    count: int = 0
    pitch: float = 0.0
    bolt_r: float = 0.0
    half_w: float = 0.0
    r_max: float = 0.0
    h: float = 0.004
    reach_m: float = 0.03
    ang_deg: float = 25.0

    def __post_init__(self):
        if self.kind not in MARK_KINDS:
            raise ValueError(f"Mark.kind {self.kind!r} not in {MARK_KINDS}")
        put = object.__setattr__
        put(self, "centre", _vec3(self.centre, "Mark.centre"))
        put(self, "u", _vec3(self.u, "Mark.u"))
        put(self, "v", _vec3(self.v, "Mark.v"))
        if self.n is not None:
            put(self, "n", _vec3(self.n, "Mark.n"))
        k = self.kind
        bad = None
        if k in ("plate", "slot") and not (self.half_u > 0 and self.half_v > 0):
            bad = "half_u and half_v must be > 0"
        elif k == "bolts" and not (self.bolt_r > 0 and self.half_u >= 0 and self.half_v >= 0):
            bad = "bolt_r must be > 0 (half_u / half_v >= 0)"
        elif k == "ring" and not (self.r_out > self.r_in >= 0):
            bad = "need r_out > r_in >= 0"
        elif k == "boltring" and not (int(self.count) >= 1 and self.radius > 0 and self.bolt_r > 0):
            bad = "need count >= 1, radius > 0, bolt_r > 0"
        elif k == "band" and not self.half_w > 0:
            bad = "half_w must be > 0"
        if bad:
            raise ValueError(f"Mark({k}): {bad}")
        _mark_frame(self)                     # raises on a degenerate frame


@functools.lru_cache(maxsize=None)
def _mark_frame(mk):
    """(c, u_hat, v_hat, w_hat) of a Mark: v orthonormalised against u; w = n or u x v."""
    c = np.array(mk.centre, dtype=np.float64)
    uh = _unit(mk.u, "Mark.u")
    v = np.array(mk.v, dtype=np.float64)
    v = v - (v @ uh) * uh
    if mk.n is not None:
        w = _unit(mk.n, "Mark.n")
        vn = float(np.linalg.norm(v))
        vh = v / vn if vn > 1e-12 else _unit(np.cross(w, uh), "Mark.v")
    else:
        vh = _unit(v, "Mark.v (parallel to u)")
        w = _unit(np.cross(uh, vh), "Mark u x v")
    if mk.kind == "boltring":                 # phase axis in the ring plane
        _unit(uh - (uh @ w) * w, "Mark.u (parallel to n: no boltring phase)")
    return c, uh, vh, w


def transform_marks(items, matrix):
    """
    Map Marks / Decals authored in one frame into the bake-time mesh frame (RIGID
    transforms: half sizes, radii and pitches are not rescaled): center by the 4x4 affine
    `matrix` (any 4x4 / 3x4 nested sequence, e.g. a mathutils.Matrix), u / v / n by its
    3x3 part.  Example: the generators bake AFTER cs2_asset.orient_for_net, i.e. in the
    net frame (x, y, z) -> (y, -x, z); marks written in the authoring frame go through
    transform_marks(marks, Matrix.Rotation(-pi/2, 4, "Z")).  Returns a tuple.
    """
    m = np.array([list(r) for r in matrix], dtype=np.float64)
    R, t = m[:3, :3], m[:3, 3]
    out = []
    for it in items:
        ch = {"centre": tuple(float(x) for x in R @ np.asarray(it.centre) + t),
              "u": tuple(float(x) for x in R @ np.asarray(it.u)),
              "v": tuple(float(x) for x in R @ np.asarray(it.v))}
        if getattr(it, "n", None) is not None:
            ch["n"] = tuple(float(x) for x in R @ np.asarray(it.n))
        out.append(dataclasses.replace(it, **ch))
    return tuple(out)


def _smooth01(x):
    t = np.clip(x, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def _smoothstep(e0, e1, x):
    return float(_smooth01((float(x) - e0) / (e1 - e0)))


def _world_points(X, Y, qm, Pm, Mt, dx=0.0, dy=0.0):
    """(h, w, 3) world point of every texel center (+ (dx, dy) texels) via the face's
    affine UV -> 3D map."""
    return (Pm + (X + dx - qm[0])[..., None] * Mt[0]
            + (Y + dy - qm[1])[..., None] * Mt[1])


def _write_core_priority(dsts, own, sl, mask, core, vals):
    """Core-priority write (see detail_height): `core` texels always take this face's
    values; dilation-only texels only where no face's core has claimed them yet.
    Returns the bool (h, w) of texels written."""
    ov = own[sl]
    ext = mask & ~core & ~ov
    for dst, val in zip(dsts, vals):
        dv = dst[sl]
        dv[core] = val[core]
        dv[ext] = val[ext]
    ov[core] = True
    return core | ext


class _EdgeTopology:
    """
    Welded-mesh adjacency for edge rounding (section 2.2): per polygon the crease
    segments it shares with a neighbor of the SAME part whose normal differs by >= deg
    and that lies behind this face's plane (convex).  Boundary edges (no same-part
    neighbor: an open prism end buried in a joint) are never creases.
    """

    def __init__(self, me, parts, matrix=None):
        co, lv = _read_geometry(me, matrix)
        npoly = len(me.polygons)
        ls = np.empty(npoly, dtype=np.int64)
        lt = np.empty(npoly, dtype=np.int64)
        me.polygons.foreach_get("loop_start", ls)
        me.polygons.foreach_get("loop_total", lt)
        le = np.empty(len(me.loops), dtype=np.int64)
        me.loops.foreach_get("edge_index", le)
        ev = np.empty(len(me.edges) * 2, dtype=np.int64)
        me.edges.foreach_get("vertices", ev)
        self.co, self.lv, self.ls, self.lt, self.le = co, lv, ls, lt, le
        self.ev = ev.reshape(-1, 2)
        self.parts = np.asarray(parts)
        self.edge_polys = {}
        for pi in range(npoly):
            for li in range(int(ls[pi]), int(ls[pi] + lt[pi])):
                self.edge_polys.setdefault(int(le[li]), []).append(pi)
        self.normal = np.zeros((npoly, 3))
        self.centroid = np.zeros((npoly, 3))
        for pi in range(npoly):
            P = co[lv[ls[pi]:ls[pi] + lt[pi]]]
            if len(P) >= 3:
                self.normal[pi] = _poly_normal(P)
                self.centroid[pi] = P.mean(axis=0)
        self._cache = {}

    def creases(self, pi, deg):
        key = (int(pi), float(deg))
        hit = self._cache.get(key)
        if hit is not None:
            return hit
        cos_lim = math.cos(math.radians(deg))
        na, ca, part = self.normal[pi], self.centroid[pi], self.parts[pi]
        out = []
        for li in range(int(self.ls[pi]), int(self.ls[pi] + self.lt[pi])):
            e = int(self.le[li])
            for b in self.edge_polys.get(e, ()):
                if b == pi or self.parts[b] != part:
                    continue
                if (float(na @ self.normal[b]) <= cos_lim + 1e-12
                        and float((self.centroid[b] - ca) @ na) < -1e-5):
                    va, vb = self.ev[e]
                    out.append((self.co[va].copy(), self.co[vb].copy()))
                    break
        self._cache[key] = out
        return out


def _edge_round_field(p, segs, cen, n, r, tilt_deg):
    """
    Section 2.2 profile: d = distance to the nearest crease segment (signed: negative
    for dilation texels beyond the crease), h = -r k (1 - d/r)^2 for 0 <= d < r with
    k = tan(tilt) / 2 (slope at the crease = tan(tilt)); beyond the crease (d < 0) the
    tangent line continues (-r k + 2 k d, clamped at d = -r) so the border texel's
    central-difference gradient keeps the full tilt.  0 for d >= r.
    """
    k = 0.5 * math.tan(math.radians(tilt_deg))
    d = np.full(p.shape[:-1], np.inf)
    for a, b in segs:
        ab = b - a
        L2 = float(ab @ ab)
        if L2 < 1e-18:
            continue
        ap = p - a
        t = np.clip((ap @ ab) / L2, 0.0, 1.0)
        dv = ap - t[..., None] * ab
        dist = np.sqrt(np.sum(dv * dv, axis=-1))
        m = np.cross(n, ab)
        ln = float(np.linalg.norm(m))
        if ln < 1e-12:
            continue
        m = m / ln
        if float((cen - a) @ m) < 0.0:
            m = -m
        d = np.minimum(d, np.where(ap @ m >= 0.0, dist, -dist))
    out = np.zeros(d.shape, dtype=np.float64)
    sel = d < r
    if sel.any():
        dd = np.maximum(d[sel], -r)
        out[sel] = np.where(dd >= 0.0, -r * k * (1.0 - dd / r) ** 2, -r * k + 2.0 * k * dd)
    return out


def _grid_offset(a, half, pitch):
    """Offset of `a` from the nearest of max(1, floor(2*half/pitch)) grid points centered
    on 0 at `pitch` spacing (one point at 0 when pitch <= 0)."""
    if pitch <= 0:
        return a
    cnt = max(1, int(math.floor(2.0 * half / pitch + 1e-6)))
    mid = 0.5 * (cnt - 1)
    i = np.clip(np.rint(a / pitch + mid), 0, cnt - 1)
    return a - (i - mid) * pitch


def _mark_field(mk, p, n_face, Pm, ppm, mask, floor_ok):
    """Height (h, w) of one Mark on one face, or None when it does not touch the face
    (not counted) or the face is below the mark's texel floor (counted as skipped)."""
    c, uh, vh, w = _mark_frame(mk)
    kind = mk.kind
    tex = 1.0 / ppm
    if kind != "band":
        if float(n_face @ w) < math.cos(math.radians(mk.ang_deg)) - 1e-12:
            return None
        if abs(float((c - Pm) @ n_face)) > mk.reach_m:
            return None
    d = p - c
    label = "marks:" + kind
    if kind in ("plate", "slot", "bolts"):
        a = d @ uh
        b = d @ vh
        grow = (mk.bolt_r if kind == "bolts" else 0.0) + tex
        if not (mask & (np.abs(a) <= mk.half_u + grow) & (np.abs(b) <= mk.half_v + grow)).any():
            return None
        size = 2.0 * mk.bolt_r if kind == "bolts" else 2.0 * min(mk.half_u, mk.half_v)
        if not floor_ok(size, label):
            return None
        if kind == "plate":
            e = np.minimum(mk.half_u - np.abs(a), mk.half_v - np.abs(b))
            f = mk.h * _smooth01(e * ppm + 0.5)
        elif kind == "slot":
            rc = 0.5 * min(mk.half_u, mk.half_v)
            qx = np.abs(a) - (mk.half_u - rc)
            qy = np.abs(b) - (mk.half_v - rc)
            sd = (np.hypot(np.maximum(qx, 0.0), np.maximum(qy, 0.0))
                  + np.minimum(np.maximum(qx, qy), 0.0) - rc)
            f = -abs(mk.h) * _smooth01(-sd * ppm + 0.5)
        else:
            da = _grid_offset(a, mk.half_u, mk.pitch)
            db = _grid_offset(b, mk.half_v, mk.pitch)
            r = max(mk.bolt_r, tex)
            f = np.sqrt(np.clip(1.0 - (da * da + db * db) / (r * r), 0.0, 1.0)) * mk.h
    elif kind in ("ring", "boltring"):
        t = d @ w
        dp = d - t[..., None] * w
        rho = np.sqrt(np.sum(dp * dp, axis=-1))
        if kind == "ring":
            foot = rho <= mk.r_out + tex
            if mk.r_in > 0:
                foot &= rho >= mk.r_in - tex
            if not (mask & foot).any():
                return None
            if not floor_ok(mk.r_out - mk.r_in, label):
                return None
            e = mk.r_out - rho if mk.r_in <= 0 else np.minimum(rho - mk.r_in, mk.r_out - rho)
            f = mk.h * _smooth01(e * ppm + 0.5)
        else:
            if not (mask & (np.abs(rho - mk.radius) <= mk.bolt_r + tex)).any():
                return None
            if not floor_ok(2.0 * mk.bolt_r, label):
                return None
            up = uh - (uh @ w) * w
            up = up / np.linalg.norm(up)
            vp = np.cross(w, up)
            x, y = dp @ up, dp @ vp
            cnt = int(mk.count)
            kk = np.mod(np.rint(np.arctan2(y, x) * cnt / (2.0 * math.pi)), cnt)
            th = kk * (2.0 * math.pi / cnt)
            dd2 = (x - mk.radius * np.cos(th)) ** 2 + (y - mk.radius * np.sin(th)) ** 2
            r = max(mk.bolt_r, tex)
            f = np.sqrt(np.clip(1.0 - dd2 / (r * r), 0.0, 1.0)) * mk.h
    else:                                                      # band
        t = d @ w
        dp = d - t[..., None] * w
        rho = np.sqrt(np.sum(dp * dp, axis=-1))
        inside = np.abs(t) <= mk.half_w
        if mk.r_max > 0:
            inside &= rho <= mk.r_max
        if not (mask & inside).any():
            return None
        if not floor_ok(2.0 * mk.half_w, label):
            return None
        f = np.where(inside, mk.h * 0.5 * (1.0 + np.cos(math.pi * t / mk.half_w)), 0.0)
    if not (f[mask] != 0.0).any():
        return None
    return f


def _relief_new_field(spec, pidx, X, Y, q, P, n, qm, Pm, Mt, ppm, mask, topo, floor_ok, drawn):
    """Edge rounding + marks of one face (meters, float64 (h, w))."""
    f = np.zeros(X.shape, dtype=np.float64)
    p = _world_points(X, Y, qm, Pm, Mt)
    if spec.edge_round_m is not None and topo is not None:
        segs = topo.creases(pidx, spec.edge_round_deg)
        if segs and floor_ok(spec.edge_round_m, "edge_round"):
            f += _edge_round_field(p, segs, P.mean(axis=0), n, float(spec.edge_round_m),
                                   float(spec.edge_round_tilt_deg))
            drawn["edge_round"] = drawn.get("edge_round", 0) + 1
    for mk in spec.marks:
        fm = _mark_field(mk, p, n, Pm, ppm, mask, floor_ok)
        if fm is not None:
            f += fm
            key = "marks:" + mk.kind
            drawn[key] = drawn.get(key, 0) + 1
    return f


# -- tone (section 2.4) ----------------------------------------------------------------

def _fmix32(h):
    """murmur3 finalizer on uint64 arrays holding 32-bit values."""
    h = h ^ (h >> np.uint64(16))
    h = (h * np.uint64(0x85EBCA6B)) & _M32
    h = h ^ (h >> np.uint64(13))
    h = (h * np.uint64(0xC2B2AE35)) & _M32
    return h ^ (h >> np.uint64(16))


def _lattice_value(ix, iy, iz, seed):
    """Hashed lattice value in [-1, 1] at integer points (int64 arrays)."""
    h = np.full(ix.shape, np.uint64(int(seed) & 0xFFFFFFFF), dtype=np.uint64)
    for k, mul in zip((ix, iy, iz), _HASH_K):
        ku = (k & 0xFFFFFFFF).astype(np.uint64)
        h = _fmix32(h ^ ((ku * mul) & _M32))
    return h.astype(np.float64) * (2.0 / 4294967295.0) - 1.0


def _value_noise3(x, seed):
    """Trilinear hashed value noise, x (..., 3) in lattice units -> (...) in [-1, 1].
    World-anchored and stateless (no RNG): the same point gives the same value in any
    process."""
    fl = np.floor(x)
    i = fl.astype(np.int64)
    t = x - fl
    out = np.zeros(x.shape[:-1], dtype=np.float64)
    for dx in (0, 1):
        wx = t[..., 0] if dx else 1.0 - t[..., 0]
        for dy in (0, 1):
            wy = t[..., 1] if dy else 1.0 - t[..., 1]
            for dz in (0, 1):
                wz = t[..., 2] if dz else 1.0 - t[..., 2]
                out += wx * wy * wz * _lattice_value(i[..., 0] + dx, i[..., 1] + dy,
                                                     i[..., 2] + dz, seed)
    return out


def _fbm2(x, seed):
    """2 octaves (half amplitude at half cell), mapped to [-1, 1]; std ~ MOTTLE_STD."""
    return (_value_noise3(x, seed)
            + 0.5 * _value_noise3(2.0 * x, (int(seed) ^ _SALT_OCT2) & 0xFFFFFFFF)) / 1.5


def detail_tone(me, A, specs, uv_layer="UVUnique", part_table=None, matrix=None, seed=0,
                dilate_px=1.0, verbose=True):
    """
    Section 2.4: per-texel tone fields (dl, ds), two (A, A) float32 arrays for
    compose_maps opts tone_l / tone_s.  `specs` (prefix -> DetailSpec, longest prefix
    wins) may be the detail_height dict or a separate one keyed by material class.

    Mottle (spec.mottle_m set, amp or smooth non-zero): hashed trilinear value noise in
    WORLD meters (murmur3 fmix32 of the int lattice, seed = crc32(prefix) ^ seed), cell
    mottle_m across and mottle_m * mottle_stretch along the face's longest edge (the
    member axis; the stretch is sign-free, so the facets of one member share it), 2
    octaves mapped to [-1, 1] (std MOTTLE_STD): dl += amp * n1, ds += smooth * n2 (n2
    an independent seed).  Chalk: s = smoothstep(0.2, 0.9, n_face.z) *
    (0.7 + 0.3 * noise(0.5 m)); dl += chalk_amp * s; ds -= chalk_smooth * s.
    Floor: a cell below TONE_FLOOR (4) texels at the face's px/m is skipped and counted
    (mottle; for chalk only its 0.5 m noise is dropped - the face keeps 0.7 * up -
    counted as chalk_noise).  Faces are written with the same core priority as
    detail_height, so tone is continuous across a contiguous strip.  Stats in
    LAST_TONE_STATS.
    """
    table = read_part_table(me, part_table)
    parts = part_indices(me)
    dl = np.zeros((A, A), dtype=np.float32)
    ds = np.zeros((A, A), dtype=np.float32)
    own = np.zeros((A, A), dtype=bool)
    drawn, skipped = {}, {}
    if any(_wants_tone(s) for s in specs.values()):
        for poly, sl, mask, X, Y, q, P, ppm in iter_face_rasters(me, A, uv_layer, dilate_px,
                                                                  matrix):
            pi = int(parts[poly.index])
            if pi < 1 or pi > len(table):
                continue
            prefix, spec = _prefix_spec_for(table[pi - 1], specs)
            if not _wants_tone(spec):
                continue
            n = _poly_normal(P)
            qm, Pm, Mt = _affine_uv_to_world(q, P)
            p = _world_points(X, Y, qm, Pm, Mt)
            s0 = (zlib.crc32(prefix.encode("utf-8")) ^ int(seed)) & 0xFFFFFFFF
            fl = np.zeros(X.shape, dtype=np.float64)
            fs = np.zeros(X.shape, dtype=np.float64)
            if spec.mottle_m is not None and (spec.mottle_amp or spec.mottle_smooth):
                if spec.mottle_m * ppm >= TONE_FLOOR - 1e-6:
                    ax, _across = _longest_edge_frame(P, n)
                    st = max(float(spec.mottle_stretch), 1e-6)
                    x = (p + (1.0 / st - 1.0) * (p @ ax)[..., None] * ax) / float(spec.mottle_m)
                    if spec.mottle_amp:
                        fl += float(spec.mottle_amp) * _fbm2(x, s0)
                    if spec.mottle_smooth:
                        fs += float(spec.mottle_smooth) * _fbm2(x, s0 ^ _SALT_SMOOTH)
                    drawn["mottle"] = drawn.get("mottle", 0) + 1
                else:
                    skipped["mottle"] = skipped.get("mottle", 0) + 1
            if spec.chalk_amp or spec.chalk_smooth:
                up = _smoothstep(0.2, 0.9, n[2])
                if up > 0.0:
                    if 0.5 * ppm >= TONE_FLOOR - 1e-6:
                        var = 0.7 + 0.3 * _value_noise3(p / 0.5, s0 ^ _SALT_CHALK)
                    else:
                        var = 0.7
                        skipped["chalk_noise"] = skipped.get("chalk_noise", 0) + 1
                    s = up * var
                    fl = fl + float(spec.chalk_amp) * s
                    fs = fs - float(spec.chalk_smooth) * s
                    drawn["chalk"] = drawn.get("chalk", 0) + 1
            core = mask if dilate_px <= 0 else (_poly_mask(q, X, Y, 0.0) & mask)
            _write_core_priority((dl, ds), own, sl, mask, core, (fl, fs))
    LAST_TONE_STATS.clear()
    LAST_TONE_STATS.update({"drawn": dict(drawn), "skipped": dict(skipped)})
    if verbose:
        print(f"[compose] detail_tone {A}: drawn {drawn or '{}'}; "
              f"skipped below {TONE_FLOOR:.0f} texels {skipped or '{}'}")
    return dl, ds


# -- decals (section 2.5) --------------------------------------------------------------

@dataclass(frozen=True, eq=False)
class Decal:
    """
    A painted rectangle projected along w = u x v onto the faces of one part prefix.
    alpha  (H, W) coverage 0..1, BOTTOM-UP like every map here: row 0 = the -v edge,
           col 0 = the -u edge (so a raster of text drawn bottom-up reads along +u)
    rgb    LINEAR color painted at alpha 1
    reach_m  slab half-thickness along w around the center
    min_cos  faces with n . w below this are skipped (0.5 = within 60 deg: a faceted
             cylinder takes the decal on its front facets)
    ctl_zero ControlMask R and G scaled by (1 - alpha), exactly 0 where alpha >= 0.99
    """
    centre: tuple
    u: tuple
    v: tuple
    half_u: float
    half_v: float
    alpha: object
    rgb: tuple
    reach_m: float = 0.6
    min_cos: float = 0.5
    ctl_zero: bool = True

    def __post_init__(self):
        put = object.__setattr__
        put(self, "centre", _vec3(self.centre, "Decal.centre"))
        put(self, "u", _vec3(self.u, "Decal.u"))
        put(self, "v", _vec3(self.v, "Decal.v"))
        a = np.asarray(self.alpha, dtype=np.float64)
        if a.ndim != 2 or min(a.shape) < 1:
            raise ValueError(f"Decal.alpha: expected (H, W), got {a.shape}")
        put(self, "alpha", a)
        put(self, "rgb", tuple(float(x) for x in self.rgb)[:3])
        if not (self.half_u > 0 and self.half_v > 0):
            raise ValueError("Decal: half_u and half_v must be > 0")


def _bilinear_zero(pad, col, row):
    """Bilinear sample of a 1-texel zero-padded image at (col, row) of the UNPADDED
    image (texel centers at integers); 0 outside."""
    Hp, Wp = pad.shape
    x = col + 1.0
    y = row + 1.0
    valid = (x >= 0.0) & (x <= Wp - 1.0) & (y >= 0.0) & (y <= Hp - 1.0)
    xc = np.clip(x, 0.0, Wp - 1.0)
    yc = np.clip(y, 0.0, Hp - 1.0)
    x0 = np.clip(np.floor(xc).astype(np.int64), 0, Wp - 2)
    y0 = np.clip(np.floor(yc).astype(np.int64), 0, Hp - 2)
    fx = xc - x0
    fy = yc - y0
    v = (pad[y0, x0] * (1 - fx) * (1 - fy) + pad[y0, x0 + 1] * fx * (1 - fy)
         + pad[y0 + 1, x0] * (1 - fx) * fy + pad[y0 + 1, x0 + 1] * fx * fy)
    return np.where(valid, v, 0.0)


def decal_on_faces(base, ctl, me, A, part_table, prefix, decal, uv_layer="UVUnique",
                   matrix=None, dilate_px=1.0, verbose=True):
    """
    Section 2.5: paint `decal` IN PLACE onto BaseColor (LINEAR, (A, A, 4)) and
    ControlMask over every face of the parts starting with `prefix` (part_table None =
    the mesh's json prop) whose normal satisfies n . (u x v) >= min_cos, texels inside
    the slab |(p - c) . w| <= reach_m.  Per texel 2 x 2 sub-samples -> decal (s, t) ->
    bilinear alpha (anti-aliased edges) -> base = mix(base, rgb, alpha); ControlMask R
    and G scaled by (1 - alpha) (0 at alpha >= 0.99: the stamp rule); BaseColor alpha 1.
    Faces are written with core priority (1 px dilation into the gutter), so a decal
    spanning several facets of a strip is applied once per texel.
    Returns the touched texel rects [(row0, row1, col0, col1)] (one per face that
    painted, rows bottom-up, as uv_bbox_px) - the weather layer's protect_px.
    """
    table = read_part_table(me, part_table)
    parts = part_indices(me)
    hits = {i + 1 for i, nm in enumerate(table) if nm.startswith(prefix)}
    c = np.array(decal.centre, dtype=np.float64)
    uh = _unit(decal.u, "Decal.u")
    v = np.array(decal.v, dtype=np.float64)
    vh = _unit(v - (v @ uh) * uh, "Decal.v (parallel to u)")
    w = np.cross(uh, vh)
    H, W = decal.alpha.shape
    pad = np.zeros((H + 2, W + 2), dtype=np.float64)
    pad[1:-1, 1:-1] = np.clip(decal.alpha, 0.0, 1.0)
    am = np.zeros((A, A), dtype=np.float32)
    own = np.zeros((A, A), dtype=bool)
    rects = []
    nfaces = 0
    for poly, sl, mask, X, Y, q, P, ppm in iter_face_rasters(me, A, uv_layer, dilate_px, matrix):
        if int(parts[poly.index]) not in hits:
            continue
        n = _poly_normal(P)
        if float(n @ w) < decal.min_cos:
            continue
        qm, Pm, Mt = _affine_uv_to_world(q, P)
        acc = np.zeros(X.shape, dtype=np.float64)
        for dx, dy in ((-0.25, -0.25), (0.25, -0.25), (-0.25, 0.25), (0.25, 0.25)):
            d = _world_points(X, Y, qm, Pm, Mt, dx, dy) - c
            slab = np.abs(d @ w) <= decal.reach_m
            col = ((d @ uh) / decal.half_u + 1.0) * 0.5 * W - 0.5
            row = ((d @ vh) / decal.half_v + 1.0) * 0.5 * H - 0.5
            acc += np.where(slab, _bilinear_zero(pad, col, row), 0.0)
        a = 0.25 * acc
        if not (a[mask] > 1e-6).any():
            continue
        core = mask if dilate_px <= 0 else (_poly_mask(q, X, Y, 0.0) & mask)
        wrote = _write_core_priority((am,), own, sl, mask, core, (a,))
        hit = wrote & (a > 1e-6)
        if hit.any():
            rr = np.where(hit.any(axis=1))[0]
            cc = np.where(hit.any(axis=0))[0]
            r0, c0 = sl[0].start, sl[1].start
            rects.append((int(r0 + rr[0]), int(r0 + rr[-1] + 1), int(c0 + cc[0]),
                          int(c0 + cc[-1] + 1)))
            nfaces += 1
    touched = am > 1e-6
    if touched.any():
        al = am[touched].astype(np.float32)[:, None]
        rgb = np.array(decal.rgb, dtype=np.float32)
        b3 = base[..., 0:3]
        b3[touched] = b3[touched] * (1.0 - al) + rgb[None, :] * al
        b4 = base[..., 3]
        b4[touched] = 1.0
        if decal.ctl_zero:
            cg = ctl[..., 0:2]
            cg[touched] = cg[touched] * (1.0 - al)
            cg[am >= 0.99] = 0.0
    rects = sorted(set(rects))
    if verbose:
        print(f"[compose] decal_on_faces {prefix!r}: {nfaces} faces, "
              f"{int(touched.sum())} texels, {len(rects)} rects")
    return rects
