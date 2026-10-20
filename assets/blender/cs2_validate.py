"""
cs2_validate.py - acceptance checks for a CS2 asset folder and for a Blender mesh about
to become one (art pipeline v2, section 3.9).

Contract
--------
Every check produces a finding `(level, code, message)` with level in {"ERROR", "WARN",
"INFO"}. Functions return the full list and print each finding as `[validate] LEVEL code:
message`. Only `validate_asset_folder` raises (`AssetValidationError`, carrying the list)
and only after every check has run, so one run reports everything that is wrong.

    validate_asset_folder(asset_dir, name, spec=None) -> findings   (raises on any ERROR)
        name lint, folder contents vs settings.json, texture sets (header + pixels),
        FBX round-trip of every FBX in the folder, LOD ratios/caps, sub-mesh transform
        identity, `_Win` UV overlap, engine-base ring on buildings.
    validate_asset_folder_ex(asset_dir, name, spec=None) -> (findings, summary)
        the same checks without raising; summary = tris per LOD, texture sizes, verdict.
    validate_mesh(obj, kind="prop"|"building"|other, contracts=None) -> findings
        in-scene checks on a Blender object (meters, Z up): naming, loose geometry, UV
        layer count/range/zero-area/mirrored islands, color attributes, prop ground
        contact, building `ground_ring_report`, caller contracts. Never raises.
    ground_ring_report(obj, band=0.01, table=None) -> dict
        the engine-base acceptance check (BatchMeshHelpers.AddBaseLines): edges in the
        z = 0 +- band whose every touching triangle has a vertex above the band are
        base lines ("OK"); an edge with any touching triangle entirely at/below the band
        is dropped ("BLOCKED"). Verdict "OK" | "BLOCKED" | "NONE".
    write_manifest(asset_dir, name, data) -> path
        `<name>.manifest.json` beside settings.json (JSONImporter.CreateAsset returns
        null for unknown names, so the importer ignores it).
    uv_coverage(me, res, uv_layer=None) -> (res, res) int32 texel hit counts, bottom-up
        rows (same orientation as Image.pixels), used for overlap and covered-texel rules.

`spec` (all keys optional): kind ("prop" | "building" | "hanging"), cutout (bool),
min_texture (512: the wiki floor, ERROR below for every kind), max_texture (4096), hero (legacy,
now covered by the floor), lod0_cap,
lod1_ratio (0.5), lod2_cap (500), extent_m ((x, y, z) Blender meters, 2 % tolerance),
expected_lods (tuple), expected_submeshes (tuple of suffixes).

Command line (used by tools\\assets-check.ps1):
    blender --background --factory-startup --python cs2_validate.py -- --root <letter dir>
            [--asset-dir <dir>]... [--kind auto|prop|building|hanging] [--json <out>]
    prints one `[assets-check] ...` line per folder and exits 1 when any folder has an ERROR.

Engine facts relied on (decompile 1.6.2): FBX vertices read as centimeters, Y up, normals
verbatim; textures paired by `<Model node name>_<map>.png`; BaseColor alpha != 255 flips
the surface to DefaultCutout; MaskMap B and ControlMask A are zero in every shipped asset.
"""

import argparse
import datetime
import hashlib
import json
import math
import os
import re
import struct
import sys

import bpy
import bmesh
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402


# --------------------------------------------------------------------------------------
# Constants
# --------------------------------------------------------------------------------------

LEVELS = ("ERROR", "WARN", "INFO")
MAPS = ("BaseColor", "Normal", "MaskMap", "ControlMask")
SUBMESH_SUFFIXES = ("Win", "Wim", "Wif", "Wio", "Gls", "Glf", "Glm", "Gra", "Wat")
WIN_SUFFIXES = ("Win", "Wim", "Wif", "Wio")
NAME_FORBIDDEN = ("Tree", "Bush", "Neon", "#")
PATH_FORBIDDEN = ("Tree", "Bushes")
GROUND_BAND_M = 0.01          # BatchMeshHelpers.AddBaseLines: y = 0 +- 0.01
RAW_UNITS_PER_M = cs2_asset.CS2_UNIT_SCALE
DEFAULT_SPEC = {
    "kind": None, "cutout": False, "min_texture": 512, "max_texture": 4096, "hero": False,
    "lod0_cap": None, "lod1_ratio": 0.5, "lod2_cap": 500, "extent_m": None,
    "expected_lods": None, "expected_submeshes": None,
}
_EMISSIVE_RE = re.compile(r"^Emissive(?:ID)?\d*$")

# Per-PART texel density (2026-10-17; docs/research/art/snow-props-weather-2026-10-17.md section 4,
# PIPELINE "Gates"): a whole-asset px/m average hid snow props B whose painted shells shipped at
# 12-22 px/m while nuts got 200-1,450 px/m (the buildings' T16 lesson again: density, not
# compression). A "part" here is a connected mesh component of the re-imported LOD0 (the FBX
# carries no part names); parts smaller than DENSITY_PART_MIN_M2 are reported, not gated.
DENSITY_PART_MIN_M2 = 0.05
# REDO (wiki style, 2026-10-17, snow-props-weather-2026-10-17.md "Redo"): WARN raised 60 -> 120 px/m
# so the gate keeps asserting vanilla-class intent at the new sizes (vanilla props 158-2,520 px/m;
# the repacked snow set reads min part 80+); ERROR stays 30 for now.
DENSITY_FLOOR_PX_M = {"prop": (30.0, 120.0)}   # kind -> (ERROR below, WARN below)
# Texture floor (REDO, 2026-10-17): the wiki's "minimum 512x512 ... anything smaller is unsupported"
# (Asset Creation Guide; docs/research/wiki/asset-creation-guide.md). DEFAULT_SPEC min_texture = 512,
# an ERROR for every kind (it was 128, with only a hero WARN below 512).


class AssetValidationError(RuntimeError):
    """Raised by validate_asset_folder when at least one ERROR finding exists."""

    def __init__(self, name, findings):
        self.name = name
        self.findings = list(findings)
        errors = [f for f in self.findings if f[0] == "ERROR"]
        lines = "; ".join(f"{code}: {msg}" for _lvl, code, msg in errors[:6])
        more = f" (+{len(errors) - 6} more)" if len(errors) > 6 else ""
        super().__init__(f"{name}: {len(errors)} validation error(s): {lines}{more}")


class _Report(list):
    """Findings list that prints as it collects."""

    def __init__(self, tag):
        super().__init__()
        self.tag = tag

    def add(self, level, code, message):
        assert level in LEVELS, level
        self.append((level, code, message))
        print(f"[validate] {level} {code}: {self.tag}: {message}")

    def error(self, code, message):
        self.add("ERROR", code, message)

    def warn(self, code, message):
        self.add("WARN", code, message)

    def info(self, code, message):
        self.add("INFO", code, message)

    def counts(self):
        return (sum(1 for f in self if f[0] == "ERROR"),
                sum(1 for f in self if f[0] == "WARN"))


def _merge_spec(spec):
    out = dict(DEFAULT_SPEC)
    if spec:
        out.update(spec)
    return out


# --------------------------------------------------------------------------------------
# Naming
# --------------------------------------------------------------------------------------

def lint_asset_name(name, rep, folder=None):
    """validate_asset_name + the importer's magic substrings (Constants.cs:50-73, 90-91)."""
    try:
        cs2_asset.validate_asset_name(name)
    except cs2_asset.AssetNameError as e:
        rep.error("name", str(e))
    for tok in NAME_FORBIDDEN:
        if tok in name:
            rep.error("name", f"name {name!r} contains {tok!r}: the importer treats it as "
                              f"vegetation / neon / a split marker")
    if folder:
        parts = os.path.normpath(os.path.abspath(folder)).split(os.sep)
        for comp in parts:
            for tok in PATH_FORBIDDEN:
                if tok in comp:
                    rep.error("name", f"folder path component {comp!r} contains {tok!r} "
                                      f"(IsTree path rule)")
        if os.path.basename(os.path.normpath(folder)) != name:
            rep.warn("folder", f"folder is named {os.path.basename(os.path.normpath(folder))!r}, "
                               f"asset is {name!r}")


def classify_file(fname, name):
    """
    Map a folder entry to (kind, lod, suffix):
      ("lod0", 0, None) | ("lod", n, None) | ("submesh", n, sfx) | ("map", n, map)
      | ("emissive", 0, sfx) | ("settings"|"manifest"|"unknown"|"png-stray"|"fbx-unknown", ...)
    """
    if fname == "settings.json":
        return ("settings", 0, None)
    if fname == f"{name}.manifest.json":
        return ("manifest", 0, None)
    low = fname.lower()
    if low.endswith(".fbx"):
        stem = fname[:-4]
        if stem == name:
            return ("lod0", 0, None)
        if not stem.startswith(name + "_"):
            return ("fbx-unknown", 0, None)
        rest = stem[len(name) + 1:]
        m = re.fullmatch(r"LOD(\d+)", rest)
        if m:
            return ("lod", int(m.group(1)), None)
        m = re.fullmatch(r"(?:LOD(\d+)_)?(\w+)", rest)
        if m and m.group(2) in SUBMESH_SUFFIXES:
            return ("submesh", int(m.group(1) or 0), m.group(2))
        return ("fbx-unknown", 0, None)
    if low.endswith(".png"):
        stem = fname[:-4]
        if not stem.startswith(name + "_"):
            return ("png-stray", 0, None)
        rest = stem[len(name) + 1:]
        m = re.fullmatch(r"(?:LOD(\d+)_)?(BaseColor|Normal|MaskMap|ControlMask)", rest)
        if m:
            return ("map", int(m.group(1) or 0), m.group(2))
        if _EMISSIVE_RE.match(rest):
            return ("emissive", 0, rest)
        return ("png-stray", 0, None)
    return ("unknown", 0, None)


# --------------------------------------------------------------------------------------
# Textures
# --------------------------------------------------------------------------------------

def read_png_header(path):
    """(width, height, bit_depth, colour_type) from the IHDR chunk; raises on a non-PNG."""
    with open(path, "rb") as fh:
        head = fh.read(33)
    if head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        raise ValueError(f"{os.path.basename(path)}: not a PNG")
    w, h = struct.unpack(">II", head[16:24])
    return w, h, head[24], head[25]


def load_png_pixels(path):
    """(h, w, 4) float32 raw byte/255 values, bottom-up rows (Image.pixels order)."""
    img = bpy.data.images.load(path)
    try:
        w, h = img.size
        buf = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(buf)
        return buf.reshape(h, w, 4)
    finally:
        bpy.data.images.remove(img)


def _is_pow2(n):
    return n > 0 and (n & (n - 1)) == 0


def validate_texture_set(asset_dir, prefix, rep, spec, coverage=None):
    """
    Header + pixel rules for the four maps `<prefix>_<map>.png`. `coverage` is an
    optional (res, res) hit-count array on the same texel grid (bottom-up); when given,
    the Normal rules apply to covered texels only. Returns the set size or None.
    """
    spec = _merge_spec(spec)
    sizes = {}
    for m in MAPS:
        path = os.path.join(asset_dir, f"{prefix}_{m}.png")
        if not os.path.isfile(path):
            rep.error("texture-missing", f"{prefix}_{m}.png missing")
            continue
        try:
            w, h, depth, ctype = read_png_header(path)
        except ValueError as e:
            rep.error("texture-header", str(e))
            continue
        if w != h:
            rep.error("texture-square", f"{prefix}_{m}.png is {w}x{h}, must be square")
        if not _is_pow2(w) or not _is_pow2(h):
            rep.error("texture-pow2", f"{prefix}_{m}.png is {w}x{h}, must be a power of two")
        if w < spec["min_texture"] or w > spec["max_texture"]:
            rep.error("texture-size", f"{prefix}_{m}.png is {w} px, allowed "
                                      f"{spec['min_texture']}..{spec['max_texture']}")
        elif spec["hero"] and w < 512:
            rep.warn("texture-size", f"{prefix}_{m}.png is {w} px on a hero asset (< 512)")
        if ctype != 6:
            rep.error("texture-rgba", f"{prefix}_{m}.png colour type {ctype}, must be 6 (RGBA)")
        if depth != 8:
            rep.warn("texture-depth", f"{prefix}_{m}.png bit depth {depth}, expected 8")
        sizes[m] = w
    if not sizes:
        return None
    if len(set(sizes.values())) > 1:
        rep.error("texture-sizes", f"{prefix} maps differ in size: {sizes}")
        return None
    A = next(iter(sizes.values()))
    if any(m not in sizes for m in MAPS):
        return A

    cov = None
    if coverage is not None:
        if coverage.shape[0] == A:
            cov = coverage > 0
        else:
            # nearest resample of the coverage grid onto the texture grid
            idx = (np.arange(A) * coverage.shape[0] // A)
            cov = coverage[idx][:, idx] > 0
        # erode one texel so the bake margin / island edge texels never count as covered
        cov = _erode(cov)
    tol = 0.5 / 255.0     # one byte of rounding

    # BaseColor: alpha 255 everywhere (AlphaDetectPostProcessor flips the surface)
    px = load_png_pixels(os.path.join(asset_dir, f"{prefix}_BaseColor.png"))
    a = px[..., 3]
    bad = int(np.count_nonzero(a < 1.0 - tol))
    if bad and not spec["cutout"]:
        rep.error("basecolor-alpha", f"{prefix}_BaseColor.png has {bad} texel(s) with "
                                     f"alpha < 255 (min {a.min() * 255:.0f}); the whole surface "
                                     f"would flip to DefaultCutout")
    elif bad:
        rep.info("basecolor-alpha", f"{prefix}_BaseColor.png: cutout asset, {bad} texels < 255")
    del px, a

    # MaskMap: B == 0 (every official map), A = gloss, G = coat (allowed, reported)
    px = load_png_pixels(os.path.join(asset_dir, f"{prefix}_MaskMap.png"))
    bad = int(np.count_nonzero(px[..., 2] > tol))
    if bad:
        rep.error("maskmap-b", f"{prefix}_MaskMap.png has {bad} texel(s) with B > 0 "
                               f"(max {px[..., 2].max() * 255:.0f}); B is 0 in every shipped map")
    g_nz = int(np.count_nonzero(px[..., 1] > tol))
    if g_nz:
        rep.info("maskmap-g", f"{prefix}_MaskMap.png coat (G) non-zero on {g_nz} texels "
                              f"(max {px[..., 1].max():.3f})")
    rep.info("maskmap", f"{prefix}_MaskMap.png metal mean {px[..., 0].mean():.3f}, "
                        f"gloss mean {px[..., 3].mean():.3f}")
    del px

    # ControlMask: RGB exclusive, A == 0
    px = load_png_pixels(os.path.join(asset_dir, f"{prefix}_ControlMask.png"))
    s = px[..., 0] + px[..., 1] + px[..., 2]
    bad = int(np.count_nonzero(s > 1.0 + 1e-3 + 2 * tol))
    if bad:
        rep.error("controlmask-rgb", f"{prefix}_ControlMask.png has {bad} texel(s) with "
                                     f"R+G+B > 1 (max {s.max():.3f}); channels must be exclusive")
    bad = int(np.count_nonzero(px[..., 3] > tol))
    if bad:
        rep.error("controlmask-a", f"{prefix}_ControlMask.png has {bad} texel(s) with A > 0 "
                                   f"(official assets: alpha 0)")
    rep.info("controlmask", f"{prefix}_ControlMask.png slot coverage R {px[..., 0].mean():.2f} "
                            f"G {px[..., 1].mean():.2f} B {px[..., 2].mean():.2f}")
    del px, s

    # Normal: OpenGL tangent space, B > 0.5, unit length (covered texels only)
    px = load_png_pixels(os.path.join(asset_dir, f"{prefix}_Normal.png"))
    n = px[..., 0:3] * 2.0 - 1.0
    length = np.sqrt(np.sum(n * n, axis=-1))
    sel = cov if cov is not None else np.ones(length.shape, dtype=bool)
    where = "covered" if cov is not None else "all"
    n_sel = int(np.count_nonzero(sel))
    if n_sel == 0:
        rep.warn("normal", f"{prefix}_Normal.png: no covered texels to check")
    else:
        bad_b = int(np.count_nonzero((px[..., 2] <= 0.5 + tol) & sel))
        if bad_b:
            frac = bad_b / n_sel
            lvl = "ERROR" if frac > 0.005 else "WARN"
            rep.add(lvl, "normal-b", f"{prefix}_Normal.png has {bad_b} {where} texel(s) with "
                                     f"B <= 0.5 ({100.0 * frac:.2f} %): not a tangent-space "
                                     f"normal (DirectX-flipped, black-filled, or bake bleed)")
        bad_len = int(np.count_nonzero((np.abs(length - 1.0) > 0.05) & sel))
        if bad_len:
            frac = bad_len / n_sel
            lvl = "ERROR" if frac > 0.01 else "WARN"
            rep.add(lvl, "normal-length", f"{prefix}_Normal.png has {bad_len} {where} texel(s) "
                                          f"({100.0 * frac:.2f} %) with |n| outside 1 +- 0.05")
        if cov is not None:
            unc = int(np.count_nonzero(~sel))
            rep.info("normal", f"{prefix}_Normal.png checked on {n_sel} covered texels "
                               f"({unc} uncovered skipped)")
    del px, n, length
    return A


# --------------------------------------------------------------------------------------
# UV rasterizer (overlap + coverage)
# --------------------------------------------------------------------------------------

def _mesh_uv_triangles(me, uv_layer=None):
    """(T, 3, 2) float32 UV triangle array from a mesh (any polygon size, fan-triangulated)."""
    if not me.uv_layers:
        return np.zeros((0, 3, 2), dtype=np.float32)
    layer = me.uv_layers[uv_layer] if uv_layer else me.uv_layers.active
    n_loops = len(me.loops)
    uv = np.empty(n_loops * 2, dtype=np.float32)
    layer.data.foreach_get("uv", uv)
    uv = uv.reshape(n_loops, 2)
    me.calc_loop_triangles()
    n_tri = len(me.loop_triangles)
    idx = np.empty(n_tri * 3, dtype=np.int32)
    me.loop_triangles.foreach_get("loops", idx)
    return uv[idx.reshape(n_tri, 3)]


def uv_coverage(me, res=256, uv_layer=None):
    """
    Rasterize every triangle of `me` on a res x res grid of texel centers; returns int32
    hit counts with row 0 = v 0 (bottom-up, matching Image.pixels). Overlap = count > 1.
    """
    tris = _mesh_uv_triangles(me, uv_layer)
    count = np.zeros((res, res), dtype=np.int32)
    for t in tris:
        p = t * res
        x0 = max(int(math.floor(p[:, 0].min())), 0)
        x1 = min(int(math.ceil(p[:, 0].max())), res)
        y0 = max(int(math.floor(p[:, 1].min())), 0)
        y1 = min(int(math.ceil(p[:, 1].max())), res)
        if x1 <= x0 or y1 <= y0:
            continue
        (ax, ay), (bx, by), (cx, cy) = p
        area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax)
        if abs(area) < 1e-9:
            continue
        if area < 0:                      # normalize to CCW so the edge functions are positive inside
            (bx, by), (cx, cy) = (cx, cy), (bx, by)
        xs = np.arange(x0, x1, dtype=np.float64) + 0.5
        ys = np.arange(y0, y1, dtype=np.float64) + 0.5
        gx, gy = np.meshgrid(xs, ys)
        inside = np.ones(gx.shape, dtype=bool)
        # top-left fill rule: a texel center exactly on a shared edge belongs to ONE triangle
        for (px_, py_), (qx, qy) in (((ax, ay), (bx, by)), ((bx, by), (cx, cy)), ((cx, cy), (ax, ay))):
            dx, dy = qx - px_, qy - py_
            w = dx * (gy - py_) - dy * (gx - px_)
            top_left = (dy < 0) or (dy == 0 and dx > 0)
            on_edge = np.abs(w) <= 1e-7
            inside &= (w > 1e-7) | (on_edge & top_left)
        count[y0:y1, x0:x1][inside] += 1
    return count


def _erode(mask):
    """Boolean erosion by one texel (8-neighborhood); edges of the grid count as uncovered."""
    out = mask.copy()
    out[0, :] = out[-1, :] = out[:, 0] = out[:, -1] = False
    c = mask
    out[1:-1, 1:-1] &= (c[:-2, 1:-1] & c[2:, 1:-1] & c[1:-1, :-2] & c[1:-1, 2:]
                        & c[:-2, :-2] & c[:-2, 2:] & c[2:, :-2] & c[2:, 2:])
    return out


def uv_overlap_fraction(me, res=256, uv_layer=None):
    """(covered_texels, overlapping_texels, fraction) on the raster grid."""
    cov = uv_coverage(me, res, uv_layer)
    covered = int(np.count_nonzero(cov > 0))
    over = int(np.count_nonzero(cov > 1))
    return covered, over, (over / covered if covered else 0.0)


# --------------------------------------------------------------------------------------
# Mesh checks
# --------------------------------------------------------------------------------------

def _sync_depsgraph():
    """Flush pending object transforms into matrix_world (a bare location edit is lazy)."""
    try:
        bpy.context.view_layer.update()
    except (AttributeError, RuntimeError):
        pass


def _part_table(obj):
    raw = obj.data.get(cs2_asset.PART_TABLE_PROP)
    if not raw:
        return None
    try:
        return json.loads(raw)
    except (TypeError, ValueError):
        return None


def ground_ring_report(obj, band=GROUND_BAND_M, table=None, matrix=None):
    """
    Engine-base acceptance (BatchMeshHelpers.AddBaseLines :241-420). Works on a
    triangulated copy of `obj` in world space (Blender Z up = CS2 Y up).

    Returns dict(band_edges, ok_edges, blocked_edges, below_verts, verdict,
    offending_parts, ok_parts). An "OK" edge has both vertices in |z| <= band and every
    touching triangle has a vertex above the band; "BLOCKED" edges have at least one
    touching triangle entirely at/below the band (bottom faces, skirts continuing down),
    which the engine drops - the skirt would then be missing along that edge.
    """
    _sync_depsgraph()
    me = obj.data
    table = table if table is not None else _part_table(obj)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.transform(matrix if matrix is not None else obj.matrix_world)
    layer = bm.faces.layers.int.get("part")
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.verts.ensure_lookup_table()
    below = sum(1 for v in bm.verts if v.co.z < -band)

    def in_band(v):
        return abs(v.co.z) <= band

    def pname(f):
        if layer is None or table is None:
            return "?"
        i = f[layer]
        return table[i - 1] if 0 < i <= len(table) else f"part{i}"

    band_edges = ok = blocked = 0
    offending, ok_parts = {}, {}
    for e in bm.edges:
        if not (in_band(e.verts[0]) and in_band(e.verts[1])):
            continue
        faces = e.link_faces
        if not faces:
            continue
        band_edges += 1
        bad = [f for f in faces if all(v.co.z <= band for v in f.verts)]
        if bad:
            blocked += 1
            for f in bad:
                offending[pname(f)] = offending.get(pname(f), 0) + 1
        else:
            ok += 1
            for f in faces:
                ok_parts[pname(f)] = ok_parts.get(pname(f), 0) + 1
    bm.free()
    verdict = "BLOCKED" if blocked else ("OK" if ok else "NONE")
    return {
        "band_edges": band_edges, "ok_edges": ok, "blocked_edges": blocked,
        "below_verts": below, "verdict": verdict,
        "offending_parts": dict(sorted(offending.items(), key=lambda kv: -kv[1])),
        "ok_parts": dict(sorted(ok_parts.items(), key=lambda kv: -kv[1])),
    }


def part_density(me, A, units_per_m=RAW_UNITS_PER_M):
    """
    Texel density per connected component of `me` (raw FBX units, cm): list of dicts
    {area_m2, px_per_m, tris, centre_m (raw axes, meters)} sorted by px/m, density =
    sqrt(sum UV area * A^2 / sum 3D area). Stacked (shared) islands count for every copy.
    """
    nv = len(me.vertices)
    parent = list(range(nv))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for poly in me.polygons:
        vs = poly.vertices
        r0 = find(vs[0])
        for v in vs[1:]:
            r = find(v)
            if r != r0:
                parent[r] = r0
    co = np.empty(nv * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3) / float(units_per_m)
    uv_layer = me.uv_layers.active
    if uv_layer is None:
        return []
    uv = np.empty(len(me.loops) * 2, dtype=np.float64)
    uv_layer.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    me.calc_loop_triangles()
    acc = {}
    for lt in me.loop_triangles:
        v = lt.vertices
        a3 = 0.5 * float(np.linalg.norm(np.cross(co[v[1]] - co[v[0]], co[v[2]] - co[v[0]])))
        l0, l1, l2 = lt.loops
        au = 0.5 * abs(float((uv[l1, 0] - uv[l0, 0]) * (uv[l2, 1] - uv[l0, 1])
                             - (uv[l2, 0] - uv[l0, 0]) * (uv[l1, 1] - uv[l0, 1])))
        c = find(v[0])
        r = acc.setdefault(c, [0.0, 0.0, 0, np.zeros(3)])
        r[0] += a3
        r[1] += au
        r[2] += 1
        r[3] += a3 * (co[v[0]] + co[v[1]] + co[v[2]]) / 3.0
    rows = []
    for c, (a3, au, n, cs) in acc.items():
        if a3 <= 0.0:
            continue
        rows.append({"area_m2": a3, "px_per_m": math.sqrt(au * A * A / a3), "tris": n,
                     "centre_m": tuple(float(x) for x in cs / a3)})
    rows.sort(key=lambda r: r["px_per_m"])
    return rows


def _report_part_density(obj, A, rep, kind, label):
    """The per-part density gate (props only); returns the rows (for the summary)."""
    rows = part_density(obj.data, A)
    if not rows:
        return rows
    big = [r for r in rows if r["area_m2"] >= DENSITY_PART_MIN_M2]
    small = [r for r in rows if r["area_m2"] < DENSITY_PART_MIN_M2]
    if big:
        d = sorted(r["px_per_m"] for r in big)
        rep.info("texel-density", f"{label}: {len(big)} part(s) >= {DENSITY_PART_MIN_M2} m2 at "
                                  f"px/m min {d[0]:.0f} / median {d[len(d) // 2]:.0f} / max {d[-1]:.0f}; "
                                  f"{len(small)} smaller part(s) max "
                                  f"{max((r['px_per_m'] for r in small), default=0):.0f} px/m (@ {A})")
    floors = DENSITY_FLOOR_PX_M.get(kind)
    if not floors or not big:
        return rows

    def fmt(rs):
        return ", ".join(f"{r['px_per_m']:.0f} px/m ({r['area_m2']:.2f} m2 at "
                         f"({r['centre_m'][0]:.2f}, {r['centre_m'][1]:.2f}, {r['centre_m'][2]:.2f}) m)"
                         for r in rs[:5]) + (f" (+{len(rs) - 5} more)" if len(rs) > 5 else "")

    err_below, warn_below = floors
    bad = [r for r in big if r["px_per_m"] < err_below]
    low = [r for r in big if err_below <= r["px_per_m"] < warn_below]
    if bad:
        rep.error("texel-density", f"{label}: {len(bad)} part(s) >= {DENSITY_PART_MIN_M2} m2 below "
                                   f"{err_below:.0f} px/m (per-part floor, not the asset average): "
                                   f"{fmt(bad)}")
    if low:
        rep.warn("texel-density", f"{label}: {len(low)} part(s) >= {DENSITY_PART_MIN_M2} m2 below "
                                  f"{warn_below:.0f} px/m: {fmt(low)}")
    return rows


def _report_ground_ring(obj, rep, table=None, matrix=None):
    r = ground_ring_report(obj, table=table, matrix=matrix)
    parts = ", ".join(f"{k} x{v}" for k, v in list(r["offending_parts"].items())[:8])
    okp = ", ".join(f"{k}" for k in list(r["ok_parts"].keys())[:8])
    if r["verdict"] == "BLOCKED":
        rep.error("engine-base", f"engine base BLOCKED: {r['blocked_edges']} of "
                                 f"{r['band_edges']} ground-band edges have a face at/below "
                                 f"z=0 (parts: {parts or 'untagged'}); {r['ok_edges']} OK")
    elif r["verdict"] == "OK":
        rep.info("engine-base", f"engine base OK: {r['ok_edges']} ground-band edges, none "
                                f"blocked (parts: {okp or 'untagged'})")
    else:
        rep.warn("engine-base", "engine base NONE: no edges in the z = 0 +- 1 cm band; the "
                                "engine will draw no skirt")
    if r["below_verts"]:
        rep.warn("below-ground", f"{r['below_verts']} vertices below z = -1 cm (a modelled "
                                 f"skirt or a floating error; the engine base ignores them)")
    return r


def _run_contracts(obj, contracts, rep):
    if not contracts:
        return
    items = contracts.items() if isinstance(contracts, dict) else \
        [(getattr(c, "__name__", f"contract{i}"), c) for i, c in enumerate(contracts)]
    for label, fn in items:
        try:
            res = fn(obj)
        except Exception as e:  # noqa: BLE001 - a raising contract is a failed contract
            rep.error("contract", f"{label}: raised {e!r}")
            continue
        detail = ""
        if isinstance(res, tuple):
            res, detail = res[0], f": {res[1]}"
        if res:
            rep.info("contract", f"{label} holds{detail}")
        else:
            rep.error("contract", f"{label} FAILED{detail}")


def validate_mesh(obj, kind="prop", contracts=None):
    """
    In-scene checks on a Blender mesh object (meters, Z up). `kind`: "prop" requires
    world min z == 0 +- 1e-4; "building" runs the engine-base ring report (ERROR when
    BLOCKED); any other value skips the ground rule. `contracts`: dict label -> callable(obj)
    returning bool or (bool, detail), or a list of callables. Returns findings, never raises.
    """
    rep = _Report(obj.name)
    _sync_depsgraph()
    me = obj.data
    if "." in obj.name:
        rep.error("name", f"object name {obj.name!r} contains '.': export_fbx writes it verbatim "
                          f"and the importer would look for '{obj.name}_BaseColor.png'")
    base = obj.name.split("_", 1)[0]
    lint_asset_name(base, rep)
    if "_" in obj.name:
        rest = obj.name.split("_", 1)[1]
        if not re.fullmatch(r"(?:LOD\d+)(?:_(?:%s))?|(?:%s)" % ("|".join(SUBMESH_SUFFIXES),
                                                                 "|".join(SUBMESH_SUFFIXES)), rest):
            rep.error("name", f"suffix {rest!r} is not LODn / a known sub-mesh suffix")

    # topology
    n_faces = len(me.polygons)
    if n_faces == 0:
        rep.error("mesh", "no faces")
        return list(rep)
    bm = bmesh.new()
    bm.from_mesh(me)
    loose_v = sum(1 for v in bm.verts if not v.link_edges)
    loose_e = sum(1 for e in bm.edges if not e.link_faces)
    ngons = sum(1 for f in bm.faces if len(f.verts) > 4)
    bm.free()
    if loose_v:
        rep.error("mesh", f"{loose_v} loose vertices")
    if loose_e:
        rep.error("mesh", f"{loose_e} loose edges (no face)")
    if ngons:
        rep.warn("mesh", f"{ngons} n-gons: the exporter fan-triangulates them (assert_tri_only "
                         f"before export to control the split)")
    tris = cs2_asset.tri_count(obj)
    rep.info("mesh", f"{n_faces} faces, {tris} tris, {len(me.vertices)} verts")
    if me.color_attributes:
        rep.error("mesh", f"colour attributes present {[a.name for a in me.color_attributes]}: "
                          f"the importer reads Color as window/emissive IDs")
    flat = sum(1 for p in me.polygons if not p.use_smooth)
    if flat:
        rep.warn("shading", f"{flat} flat-shaded faces: shade_by_angle not applied (exporter "
                            f"writes every flat edge sharp)")

    # UVs
    if len(me.uv_layers) != 1:
        rep.error("uv", f"{len(me.uv_layers)} UV layers {[u.name for u in me.uv_layers]}; ship "
                        f"exactly one")
    if me.uv_layers:
        layer = me.uv_layers.active
        uv = np.empty(len(me.loops) * 2, dtype=np.float32)
        layer.data.foreach_get("uv", uv)
        out = int(np.count_nonzero((uv < -1e-4) | (uv > 1.0 + 1e-4)))
        if out:
            rep.error("uv-range", f"{out} UV coordinates outside [0, 1] "
                                  f"(min {uv.min():.3f}, max {uv.max():.3f})")
        uv = uv.reshape(-1, 2)
        zero = mirrored = 0
        mw = obj.matrix_world
        for p in me.polygons:
            if p.area <= 1e-10:
                continue
            ls = list(p.loop_indices)
            pts = uv[ls]
            x, y = pts[:, 0], pts[:, 1]
            sa = 0.5 * float(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))
            if abs(sa) < 1e-9:
                zero += 1
            elif sa < 0:
                mirrored += 1
        if zero:
            rep.error("uv-zero", f"{zero} faces with 3D area but zero UV area (unmapped)")
        if mirrored:
            rep.warn("uv-mirror", f"{mirrored} faces whose UV winding is flipped vs 3D "
                                  f"(mirrored islands need a hard edge/seam for tangents)")

    # ground contact
    mw = obj.matrix_world
    zs = [(mw @ v.co).z for v in me.vertices]
    zmin = min(zs)
    if kind == "prop":
        if abs(zmin) > 1e-4:
            rep.error("ground", f"prop min z = {zmin:.4f} m, must be 0 +- 1e-4 (base_pivot)")
        else:
            rep.info("ground", "prop min z == 0")
    elif kind == "building":
        _report_ground_ring(obj, rep)
    else:
        rep.info("ground", f"kind {kind!r}: min z = {zmin:.3f} m (no ground rule)")

    _run_contracts(obj, contracts, rep)
    return list(rep)


# --------------------------------------------------------------------------------------
# FBX round-trip
# --------------------------------------------------------------------------------------

_IMPORT_MATRIX = None


def _import_matrix():
    """Raw FBX (cm, Y up) -> Blender meters, Z up: the inverse of export_fbx's bake."""
    global _IMPORT_MATRIX
    if _IMPORT_MATRIX is None:
        from mathutils import Matrix
        _IMPORT_MATRIX = (Matrix.Rotation(math.radians(90.0), 4, "X")
                          @ Matrix.Scale(1.0 / RAW_UNITS_PER_M, 4))
    return _IMPORT_MATRIX


def import_fbx_temp(path):
    """
    Import one FBX into the current scene and return its new mesh objects. The caller
    removes them with `remove_objects`. Uses wm.fbx_import (5.x) with the legacy fallback.
    """
    before = set(bpy.data.objects.keys())
    if hasattr(bpy.ops.wm, "fbx_import"):
        bpy.ops.wm.fbx_import(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path)
    new = [bpy.data.objects[n] for n in bpy.data.objects.keys() if n not in before]
    return new


def remove_objects(objs):
    for o in objs:
        data = o.data if o.type == "MESH" else None
        try:
            bpy.data.objects.remove(o, do_unlink=True)
        except ReferenceError:
            continue
        if data is not None and data.users == 0:
            bpy.data.meshes.remove(data)


def _fbx_model_name_ok(path, stem):
    """Byte-scan: the Model node must be named exactly `stem` (that is the texture key)."""
    with open(path, "rb") as fh:
        data = fh.read()
    good = (stem.encode() + b"\x00\x01Model") in data
    dotted = re.search(re.escape(stem.encode()) + rb"\.\d{3}\x00\x01Model", data)
    return good, bool(dotted)


def inspect_fbx(path, expected_stem, rep, spec, kind_hint=None, part_table=None):
    """
    Round-trip one FBX. Returns dict(objs, obj, tris, extent_raw, raw_min, has_normals) or
    None when nothing imported. The imported objects stay in the scene until the caller
    removes them (they are needed for coverage / overlap checks).
    """
    fname = os.path.basename(path)
    good, dotted = _fbx_model_name_ok(path, expected_stem)
    if dotted:
        rep.error("fbx-name", f"{fname}: Model node is named '{expected_stem}.NNN' (export_fbx "
                              f"name collision); textures would not bind")
    elif not good:
        rep.error("fbx-name", f"{fname}: no Model node named {expected_stem!r} in the file")
    try:
        objs = import_fbx_temp(path)
    except Exception as e:  # noqa: BLE001
        rep.error("fbx-import", f"{fname}: import failed {e!r}")
        return None
    meshes = [o for o in objs if o.type == "MESH"]
    if len(meshes) != 1:
        rep.error("fbx-import", f"{fname}: {len(meshes)} mesh objects imported, expected 1 "
                                f"({[o.name for o in objs]})")
        if not meshes:
            remove_objects(objs)
            return None
    obj = meshes[0]
    me = obj.data
    short = re.sub(r"\.\d{3}$", "", obj.name)
    if short != expected_stem:
        rep.error("fbx-name", f"{fname}: imported object is {obj.name!r}, expected "
                              f"{expected_stem!r}")
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    if len(co) == 0:
        rep.error("fbx-import", f"{fname}: no vertices")
        return {"objs": objs, "obj": obj, "tris": 0, "extent_raw": (0, 0, 0),
                "raw_min": (0, 0, 0), "has_normals": False}
    # the importer leaves node transform at identity and raw vertices in cm (probed 5.2.2)
    if any(abs(s - 1.0) > 1e-6 for s in obj.scale):
        rep.warn("fbx-transform", f"{fname}: importer applied node scale {tuple(obj.scale)}; "
                                  f"raw extents below are unscaled")
    raw_min = co.min(axis=0)
    raw_max = co.max(axis=0)
    ext = raw_max - raw_min
    tris = sum(1 for p in me.polygons if len(p.vertices) == 3)
    non_tri = len(me.polygons) - tris
    if non_tri:
        rep.error("fbx-tri", f"{fname}: {non_tri} non-triangle polygons in the file")
    tris = cs2_asset.tri_count(obj)
    rep.info("fbx", f"{fname}: raw extent {ext[0]:.1f} x {ext[1]:.1f} x {ext[2]:.1f} cm "
                    f"(y {raw_min[1]:.1f}..{raw_max[1]:.1f}), {tris} tris, "
                    f"{len(me.vertices)} verts")
    if ext.max() < 5.0 or ext.max() > 100000.0:
        rep.error("fbx-extent", f"{fname}: raw extent {ext.max():.1f} units; expected "
                                f"centimetres (x100 bake missing or doubled?)")
    if spec.get("extent_m") is not None and expected_stem == spec.get("_name"):
        # Blender (x, y, z) meters -> raw (x, z, y) cm
        ex = np.array(spec["extent_m"], dtype=np.float32) * RAW_UNITS_PER_M
        want = np.array([ex[0], ex[2], ex[1]])
        if np.any(np.abs(ext - want) > 0.02 * np.maximum(want, 1.0)):
            rep.error("fbx-extent", f"{fname}: raw extent {tuple(np.round(ext, 1))} vs expected "
                                    f"{tuple(np.round(want, 1))} (x100 of spec extent_m)")
    if kind_hint in ("prop", "building") and raw_min[1] < -1.0:
        rep.warn("fbx-extent", f"{fname}: geometry {-raw_min[1]:.1f} cm below y = 0")
    if not me.has_custom_normals:
        rep.error("fbx-normals", f"{fname}: no custom normals in the file; the importer "
                                 f"consumes FBX normals verbatim")
    if me.color_attributes:
        rep.error("fbx-colors", f"{fname}: colour attributes {[a.name for a in me.color_attributes]} "
                                f"exported")
    if len(me.uv_layers) != 1:
        rep.error("fbx-uv", f"{fname}: {len(me.uv_layers)} UV layers in the file, expected 1")
    if me.materials:
        rep.info("fbx", f"{fname}: {len(me.materials)} material slot(s) exported")
    return {"objs": objs, "obj": obj, "tris": tris, "extent_raw": tuple(ext),
            "raw_min": tuple(raw_min), "has_normals": me.has_custom_normals}


# --------------------------------------------------------------------------------------
# Folder validation
# --------------------------------------------------------------------------------------

def guess_kind(name):
    """Heuristic asset kind from the name, for the CLI where no spec is available."""
    n = name.lower()
    if "chair" in n:
        return "hanging"
    if any(t in n for t in ("station", "lodge", "fnd", "hut", "building")):
        return "building"
    return "prop"


def validate_asset_folder(asset_dir, name, spec=None):
    """
    Run every folder-level check (see module docstring). Returns the findings list and
    raises AssetValidationError when any finding is an ERROR. Temporary FBX imports are
    removed from the scene before returning.
    """
    findings, _summary = validate_asset_folder_ex(asset_dir, name, spec)
    if any(f[0] == "ERROR" for f in findings):
        raise AssetValidationError(name, findings)
    return findings


def validate_asset_folder_ex(asset_dir, name, spec=None):
    """
    Same checks as validate_asset_folder but never raises: returns (findings, summary)
    where summary = dict(tris={level: n}, tex=set of sizes, engine_base=verdict|None).
    """
    spec = _merge_spec(spec)
    spec["_name"] = name
    rep = _Report(name)
    summary = {"tris": {}, "tex": set(), "engine_base": None}
    asset_dir = os.path.abspath(asset_dir)
    kind = spec.get("kind")
    if not os.path.isdir(asset_dir):
        rep.error("folder", f"{asset_dir} is not a directory")
        return list(rep), summary

    lint_asset_name(name, rep, asset_dir)

    # -- settings.json
    settings = {}
    spath = os.path.join(asset_dir, "settings.json")
    if not os.path.isfile(spath):
        rep.error("settings", "settings.json missing (the importer never reaches a bare FBX)")
    else:
        try:
            with open(spath, "r", encoding="utf-8-sig") as fh:
                settings = json.load(fh)
        except ValueError as e:
            rep.error("settings", f"settings.json is not valid JSON: {e}")
            settings = {}
    declared_lods = set()
    for key, val in settings.items():
        m = re.fullmatch(r"LOD(\d+)", key)
        if m and isinstance(val, dict) and val.get("mode") == "Skip":
            declared_lods.add(int(m.group(1)))
    shared = settings.get("sharedAssets", {}) or {}
    surface = settings.get("Surface", {}) or {}
    if surface.get("alphaCutoff", 0) > 0 and not spec["cutout"]:
        rep.info("settings", "Surface.alphaCutoff > 0: treating the asset as cutout")
        spec["cutout"] = True

    # -- folder contents
    files = sorted(os.listdir(asset_dir))
    present = set(files)
    lod_fbx, sub_fbx, map_sets, emissive = {}, {}, {}, []
    for f in files:
        if os.path.isdir(os.path.join(asset_dir, f)):
            rep.warn("folder", f"sub-directory {f!r} inside the asset folder")
            continue
        k, lvl, sfx = classify_file(f, name)
        if k == "lod0":
            lod_fbx[0] = f
        elif k == "lod":
            lod_fbx[lvl] = f
        elif k == "submesh":
            sub_fbx[(lvl, sfx)] = f
        elif k == "map":
            map_sets.setdefault(lvl, set()).add(sfx)
        elif k == "emissive":
            emissive.append(f)
        elif k in ("settings", "manifest"):
            pass
        elif k == "png-stray":
            rep.error("folder", f"stray PNG {f!r}: not a `{name}_<map>.png` the importer pairs")
        elif k == "fbx-unknown":
            rep.error("folder", f"FBX {f!r} has an unknown suffix (LODn / {'/'.join(SUBMESH_SUFFIXES)})")
        else:
            rep.warn("folder", f"unexpected file {f!r}")
    if 0 not in lod_fbx:
        rep.error("folder", f"{name}.fbx missing")
    # tiled rollout (2026-10-16): LOD0 maps may come from ANOTHER asset folder through
    # cross-asset sharedAssets targets ("../SkiTileSheetR/SkiTileSheetR_BaseColor.png", resolved
    # against this folder, AssetImportPipeline.cs:502-509 1.6.2f1). The set is then validated at
    # its real location (max_texture 4096: the family sheet), not in this folder.
    ext0 = None
    xt = {m: shared.get(f"{name}_{m}.png") for m in MAPS}
    if all(isinstance(t, str) and "/" in t for t in xt.values()):
        tgt = os.path.normpath(os.path.join(asset_dir, xt["BaseColor"]))
        pre0 = os.path.basename(tgt)[:-len("_BaseColor.png")]
        if all(os.path.normpath(os.path.join(asset_dir, t)) == os.path.join(os.path.dirname(tgt), f"{pre0}_{m}.png")
               for m, t in xt.items()):
            ext0 = (os.path.dirname(tgt), pre0)
            rep.info("settings", f"LOD0 maps shared from {os.path.relpath(tgt, asset_dir)} (cross-asset set)")
        else:
            rep.error("settings", f"LOD0 sharedAssets targets are not one set: {xt}")
    if 0 not in map_sets and ext0 is None:
        rep.error("folder", f"no {name}_<map>.png textures")
    for lvl in sorted(declared_lods):
        if lvl not in lod_fbx:
            rep.error("lod-missing", f"settings.json declares LOD{lvl} Skip but {name}_LOD{lvl}.fbx "
                                     f"is missing")
    for lvl in sorted(lod_fbx):
        if lvl and lvl not in declared_lods:
            rep.warn("lod-undeclared", f"{lod_fbx[lvl]} present but settings.json has no "
                                       f"LOD{lvl} Skip entry")
    if spec["expected_lods"] is not None:
        for lvl in spec["expected_lods"]:
            if lvl not in lod_fbx:
                rep.error("lod-missing", f"spec expects LOD{lvl} but {name}_LOD{lvl}.fbx is missing")
    if spec["expected_submeshes"] is not None:
        for sfx in spec["expected_submeshes"]:
            if (0, sfx) not in sub_fbx:
                rep.error("submesh-missing", f"spec expects {name}_{sfx}.fbx")
    for (lvl, sfx) in sorted(sub_fbx):
        if lvl and lvl not in lod_fbx:
            rep.error("submesh", f"{sub_fbx[(lvl, sfx)]} has no matching {name}_LOD{lvl}.fbx")
    for key, target in shared.items():
        if not isinstance(target, str):
            rep.error("settings", f"sharedAssets[{key!r}] is not a filename")
            continue
        if "/" in target:
            if not os.path.isfile(os.path.normpath(os.path.join(asset_dir, target))):
                rep.error("settings", f"sharedAssets[{key!r}] -> {target!r} does not exist")
            if re.fullmatch(rf"{re.escape(name)}_(BaseColor|Normal|MaskMap|ControlMask|Emissive)\.png", key):
                continue
        elif target not in present:
            rep.error("settings", f"sharedAssets[{key!r}] -> {target!r} does not exist")
        if key in present:
            if target == key:
                # Identity entry = the dedicated-LOD-texture-set pattern every CO example
                # with its own LOD2 set uses (ExampleBakedAlphaFence, ExampleColorMaskFirehouse);
                # cs2_asset.write_settings(dedicated_texture_lods=...) emits it on purpose.
                rep.info("settings", f"sharedAssets[{key!r}] is an identity entry (dedicated LOD texture set)")
            else:
                rep.warn("settings", f"sharedAssets key {key!r} also exists as a real file (shadowed)")
        km = re.fullmatch(rf"{re.escape(name)}_LOD(\d+)_(BaseColor|Normal|MaskMap|ControlMask)\.png", key)
        if not km:
            rep.warn("settings", f"sharedAssets key {key!r} is not a `{name}_LODn_<map>.png` name")
        elif int(km.group(1)) not in declared_lods:
            rep.warn("settings", f"sharedAssets key {key!r} refers to an undeclared LOD")
    for lvl, maps in sorted(map_sets.items()):
        if lvl == 0:
            continue
        if lvl not in lod_fbx:
            rep.error("texture-lod", f"{name}_LOD{lvl}_* textures without {name}_LOD{lvl}.fbx")
        missing = [m for m in MAPS if m not in maps]
        if missing:
            rep.error("texture-lod", f"dedicated LOD{lvl} texture set incomplete: missing {missing}")
        for m in MAPS:
            k = f"{name}_LOD{lvl}_{m}.png"
            if k in shared and shared[k] != k:
                rep.warn("texture-lod", f"{k} exists but sharedAssets maps it elsewhere "
                                        f"(the file is shadowed)")
    for lvl in sorted(lod_fbx):
        if lvl == 0:
            continue
        for m in MAPS:
            key = f"{name}_LOD{lvl}_{m}.png"
            if key not in shared and key not in present:
                rep.warn("texture-lod", f"LOD{lvl} has neither a sharedAssets entry nor a file "
                                        f"for {key}")
    for f in emissive:
        rep.info("emissive", f"{f} present (LOD0 only; importer intensity 5)")

    # -- FBX round-trips
    temp_objs = []
    imported = {}
    tris = {}
    try:
        for lvl in sorted(lod_fbx):
            stem = name if lvl == 0 else f"{name}_LOD{lvl}"
            r = inspect_fbx(os.path.join(asset_dir, lod_fbx[lvl]), stem, rep, spec, kind)
            if r is None:
                continue
            temp_objs.extend(r["objs"])
            imported[("lod", lvl)] = r
            tris[lvl] = r["tris"]
        for (lvl, sfx) in sorted(sub_fbx):
            stem = f"{name}_{sfx}" if lvl == 0 else f"{name}_LOD{lvl}_{sfx}"
            r = inspect_fbx(os.path.join(asset_dir, sub_fbx[(lvl, sfx)]), stem, rep, spec, None)
            if r is None:
                continue
            temp_objs.extend(r["objs"])
            imported[("sub", lvl, sfx)] = r
            if r["obj"].data.materials:
                rep.error("submesh", f"{sub_fbx[(lvl, sfx)]}: sub-meshes must carry no material")
            main = imported.get(("lod", lvl))
            if main is not None:
                a = np.array(main["obj"].matrix_world, dtype=np.float64)
                b = np.array(r["obj"].matrix_world, dtype=np.float64)
                if np.abs(a - b).max() > 1e-5:
                    rep.error("submesh-transform", f"{sub_fbx[(lvl, sfx)]}: node transform differs "
                                                   f"from {lod_fbx[lvl]} (must be identical)")
                else:
                    rep.info("submesh", f"{sub_fbx[(lvl, sfx)]}: transform matches {lod_fbx[lvl]}")
            covered, over, frac = uv_overlap_fraction(r["obj"].data, 256)
            if sfx in WIN_SUFFIXES or sfx.startswith("Gl"):
                if covered == 0:
                    rep.error("submesh-uv", f"{sub_fbx[(lvl, sfx)]}: no UV coverage")
                elif frac > 0.005:
                    rep.error("submesh-uv", f"{sub_fbx[(lvl, sfx)]}: {over} of {covered} raster "
                                            f"texels overlap ({100 * frac:.1f} %); sub-mesh islands "
                                            f"must not overlap")
                else:
                    rep.info("submesh-uv", f"{sub_fbx[(lvl, sfx)]}: {covered} texels, "
                                           f"{over} overlapping")
            if sfx in WIN_SUFFIXES:
                cells = _win_cells(r["obj"].data)
                rep.info("win-cells", f"{sub_fbx[(lvl, sfx)]}: room cells "
                                      f"{sorted(cells.items())} (int(v*5)*5+int(u*5))")

        # LOD budgets
        if 0 in tris:
            t0 = tris[0]
            if spec["lod0_cap"] and t0 > spec["lod0_cap"]:
                rep.error("budget", f"LOD0 {t0} tris > cap {spec['lod0_cap']}")
            else:
                rep.info("budget", f"LOD0 {t0} tris" + (f" (cap {spec['lod0_cap']})" if spec['lod0_cap'] else ""))
            if 1 in tris:
                if tris[1] > spec["lod1_ratio"] * t0:
                    rep.error("budget", f"LOD1 {tris[1]} tris > {spec['lod1_ratio']:.0%} of LOD0 {t0}")
                else:
                    rep.info("budget", f"LOD1 {tris[1]} tris ({100.0 * tris[1] / max(t0, 1):.0f} % of LOD0)")
            for lvl in sorted(tris):
                if lvl >= 2:
                    if tris[lvl] > spec["lod2_cap"]:
                        rep.error("budget", f"LOD{lvl} {tris[lvl]} tris > cap {spec['lod2_cap']}")
                    else:
                        rep.info("budget", f"LOD{lvl} {tris[lvl]} tris (cap {spec['lod2_cap']})")
                    if lvl - 1 in tris and tris[lvl] > tris[lvl - 1]:
                        rep.error("budget", f"LOD{lvl} {tris[lvl]} tris > LOD{lvl - 1} {tris[lvl - 1]}")
            main0 = imported.get(("lod", 0))
            for lvl in sorted(tris):
                if lvl == 0 or main0 is None:
                    continue
                r = imported[("lod", lvl)]
                drift = max(abs(a - b) for a, b in zip(r["extent_raw"], main0["extent_raw"]))
                drift = max(drift, max(abs(a - b) for a, b in zip(r["raw_min"], main0["raw_min"])))
                if drift > 5.0:
                    rep.warn("lod-bounds", f"LOD{lvl} bounds drift {drift / RAW_UNITS_PER_M:.3f} m "
                                           f"vs LOD0 (LOD prefabs carry their own m_Bounds)")

        # engine base on buildings (re-imported LOD0, mapped back to meters / Z up)
        main0 = imported.get(("lod", 0))
        if main0 is not None and kind == "building":
            r = _report_ground_ring(main0["obj"], rep, table=None, matrix=_import_matrix())
            summary["engine_base"] = r["verdict"]
        summary["tris"] = dict(tris)

        # textures (coverage from the re-imported LOD0 for the covered-texel rules)
        for lvl, maps in sorted(map_sets.items()):
            prefix = name if lvl == 0 else f"{name}_LOD{lvl}"
            src = imported.get(("lod", lvl))
            cov = None
            A_hint = 256
            try:
                A_hint = read_png_header(os.path.join(asset_dir, f"{prefix}_BaseColor.png"))[0]
            except (OSError, ValueError):
                pass
            if src is not None and len(src["obj"].data.uv_layers) == 1:
                if lvl == 0 and kind == "prop":
                    drows = _report_part_density(src["obj"], A_hint, rep, kind,
                                                 lod_fbx.get(lvl, prefix))
                    big = [r["px_per_m"] for r in drows if r["area_m2"] >= DENSITY_PART_MIN_M2]
                    summary["density_min"] = round(min(big), 1) if big else None
                cov = uv_coverage(src["obj"].data, min(A_hint, 1024))
                covered = int(np.count_nonzero(cov))
                over = int(np.count_nonzero(cov > 1))
                rep.info("uv-coverage", f"{lod_fbx.get(lvl, prefix)}: {100.0 * covered / cov.size:.1f} % "
                                        f"of the atlas covered, {100.0 * over / max(covered, 1):.1f} % "
                                        f"of it overlapping")
            A = validate_texture_set(asset_dir, prefix, rep, spec, coverage=cov)
            if A:
                summary["tex"].add(A)
        if ext0 is not None and 0 not in map_sets:
            src = imported.get(("lod", 0))
            cov = None
            A_hint = 1024
            try:
                A_hint = read_png_header(os.path.join(ext0[0], f"{ext0[1]}_BaseColor.png"))[0]
            except (OSError, ValueError):
                pass
            if src is not None and len(src["obj"].data.uv_layers) == 1:
                cov = uv_coverage(src["obj"].data, min(A_hint, 1024))
                covered = int(np.count_nonzero(cov))
                rep.info("uv-coverage", f"{lod_fbx.get(0)} on the shared set {ext0[1]}: "
                                        f"{100.0 * covered / cov.size:.1f} % of the sheet covered")
            A = validate_texture_set(ext0[0], ext0[1], rep, dict(spec, max_texture=4096), coverage=cov)
            if A:
                summary["tex"].add(A)
    finally:
        remove_objects(temp_objs)

    errors, warns = rep.counts()
    print(f"[validate] {name}: {errors} error(s), {warns} warning(s)")
    return list(rep), summary


def _win_cells(me):
    """Room cell id per connected pane island: int(v*5)*5 + int(u*5) of the first loop."""
    cells = {}
    if not me.uv_layers:
        return cells
    layer = me.uv_layers.active
    for p in me.polygons:
        u, v = layer.data[p.loop_start].uv
        cell = int(min(max(v, 0.0), 0.999) * 5) * 5 + int(min(max(u, 0.0), 0.999) * 5)
        cells[cell] = cells.get(cell, 0) + 1
    return cells


# --------------------------------------------------------------------------------------
# Manifest
# --------------------------------------------------------------------------------------

def _sha1(path):
    h = hashlib.sha1()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def write_manifest(asset_dir, name, data):
    """
    Write `<name>.manifest.json` beside settings.json: automatic fields (name, timestamp,
    Blender version, per-file bytes + sha1 of every fbx/png/json in the folder) updated
    with the caller's `data` dict. Returns the path.
    """
    files = {}
    for f in sorted(os.listdir(asset_dir)):
        p = os.path.join(asset_dir, f)
        if not os.path.isfile(p) or f == f"{name}.manifest.json":
            continue
        if f.lower().endswith((".fbx", ".png", ".json")):
            files[f] = {"bytes": os.path.getsize(p), "sha1": _sha1(p)}
    manifest = {
        "name": name,
        "written_utc": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "blender": bpy.app.version_string,
        "files": files,
    }
    manifest.update(data or {})
    path = os.path.join(asset_dir, f"{name}.manifest.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=2, sort_keys=False, default=str)
    print(f"[manifest] wrote {path} ({len(files)} files hashed)")
    return path


# --------------------------------------------------------------------------------------
# Command line (tools\assets-check.ps1)
# --------------------------------------------------------------------------------------

def iter_asset_folders(root):
    """Asset folders = direct children of `root` holding settings.json or `<folder>.fbx`."""
    out = []
    for f in sorted(os.listdir(root)):
        d = os.path.join(root, f)
        if not os.path.isdir(d):
            continue
        if os.path.isfile(os.path.join(d, "settings.json")) or \
           os.path.isfile(os.path.join(d, f + ".fbx")):
            out.append(d)
    return out


def _parse_args(argv):
    ap = argparse.ArgumentParser(description="CS2 asset folder validator")
    ap.add_argument("--root", help="letter folder holding one asset folder per child")
    ap.add_argument("--asset-dir", action="append", default=[], help="one asset folder (repeatable)")
    ap.add_argument("--kind", default="auto", choices=("auto", "prop", "building", "hanging"))
    ap.add_argument("--json", default=None, help="write per-asset results here")
    return ap.parse_args(argv)


def main(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = _parse_args(argv)
    folders = list(args.asset_dir)
    if args.root:
        folders.extend(iter_asset_folders(args.root))
    if not folders:
        print("[assets-check] no asset folders given")
        sys.exit(2)
    results = []
    any_error = False
    for d in folders:
        d = os.path.abspath(d)
        name = os.path.basename(os.path.normpath(d))
        kind = guess_kind(name) if args.kind == "auto" else args.kind
        row = {"name": name, "dir": d, "kind": kind, "errors": 0, "warnings": 0,
               "tris": "-", "tex": "-", "engine_base": "-", "density_min": "-", "first_error": ""}
        try:
            findings, summary = validate_asset_folder_ex(d, name, {"kind": kind})
        except Exception as e:  # noqa: BLE001 - a crash in the validator is an ERROR row
            findings = [("ERROR", "crash", repr(e))]
            summary = {"tris": {}, "tex": set(), "engine_base": None}
        row["errors"] = sum(1 for f in findings if f[0] == "ERROR")
        row["warnings"] = sum(1 for f in findings if f[0] == "WARN")
        if summary["tris"]:
            row["tris"] = "/".join(str(summary["tris"][k]) for k in sorted(summary["tris"]))
        if summary["tex"]:
            row["tex"] = "/".join(str(s) for s in sorted(summary["tex"]))
        if summary["engine_base"]:
            row["engine_base"] = summary["engine_base"]
        if summary.get("density_min") is not None:
            row["density_min"] = summary["density_min"]
        errs = [f"{c}: {m}" for l, c, m in findings if l == "ERROR"]
        row["first_error"] = errs[0][:120] if errs else ""
        results.append(row)
        any_error = any_error or row["errors"] > 0
        print(f"[assets-check] {name} kind={kind} errors={row['errors']} warnings={row['warnings']} "
              f"tris={row['tris'] or '-'} tex={row['tex']} base={row['engine_base']} "
              f"minpxm={row['density_min']}"
              + (f" first={row['first_error']}" if row['first_error'] else ""))
    if args.json:
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump(results, fh, indent=2)
    print(f"[assets-check] {len(results)} folder(s), "
          f"{sum(r['errors'] for r in results)} error(s) total")
    sys.exit(1 if any_error else 0)


if __name__ == "__main__":
    main()
