"""
cs2_uv.py: unique UV repack with texel-density control (art pipeline v2, section 3.3).

Contract (docs/research/art/art-pipeline-v2.md 3.3, round L spec "cs2_uv.py"):

    pack_unique(obj, A, reserved_rects, margin_px=None, equalise=True,
                density_layer="density_w") -> dict

It creates the UV layer "UVUnique" from the active layer, then lays it out uniquely:
1. Faces whose loops all lie inside any reserved rect are PINNED, by deselection, the one pin
   mechanism (lift_tower.apply_finish_textures:906-919): bpy.ops.uv.* only touches selected faces
   under UV sync-select.
2. bpy.ops.uv.average_islands_scale() runs on the rest (UV area ~ 3D area).
3. Every non-pinned island is scaled about its UV centroid by the mean of its faces' float FACE
   attribute `density_layer` (missing / <= 0 counts as 1.0).
4. ONE bpy.ops.uv.pack_islands(rotate=False, scale=True, margin_method='FRACTION', margin=margin_px
   / A, shape_method='AABB') packs the islands.
5. The packed loops are squeezed into the tallest v-band free of every reserved rect
   (lift_tower.py:934-946, rects treated full-width, 0.005 guard).
6. Asserts: every loop in [0, 1], no non-pinned loop inside a reserved rect, no zero-area island.

margin_px defaults to max(16, A // 64) (16 @ 1024, 32 @ 2048, 64 @ 4096). rotate=False is mandatory:
the EMIT rebake that follows does not re-rotate the tangent-space normal texels.

pack_unique(..., share=None) / _pack_layout(..., share=None)   (detail pass 1, lifts)
Bay sharing: `share` = [(canon_polys, [dup_polys, ...]), ...] (polygon index lists, pairwise the
same length and loop counts. cs2_liftdress.share_identical builds it). Dup faces are held back with
the pinned set for steps 2-5 (not equalized, weighted, packed or squeezed). After step 5, every dup
loop takes its canonical loop's UV (loop k of dup poly j = loop k of canon poly j), so the copies
stack on ONE island. The report gains "shared_faces" / "shared_groups". share=None (the default)
runs the exact pre-pass code path and never passes the keyword on (cs2_bldg's drop-in packer keeps
its signature). The layouts are byte-identical, proven on the shipped lift stations.

    finish_packer(obj, mesh, A, reserved) -> None

Drop-in packer hook for lift_tower.apply_finish_textures(packer=...): the layer "UVUnique" already
exists and is active. It performs steps 1-6 above and nothing else.

    density_report(obj, A, source_layer="UVAtlas", shipped_layer="UVUnique") -> list

Per part (INT face attribute "part", names from cs2_asset.PART_TABLE_PROP): (part_index,
source_px_per_m, shipped_px_per_m). It prints a table and the worst ratios. It returns the rows
sorted worst-first (largest |log(shipped/source)|).

    set_density(parts, weights) -> dict

Writes the float FACE attribute "density_w" on every part of the generator's [(obj, quad), ...] list
BEFORE the join. The longest matching name prefix in `weights` wins, and everything else gets 1.0
(so the attribute survives the join with a sane value on every face).

Nothing here bakes or writes files. There are no top-level side effects. Import as a sibling of
cs2_asset.py (Blender's Python, numpy available).
"""

import math
import os
import sys

import bpy
import bmesh
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402

UNIQUE_LAYER = "UVUnique"
DENSITY_ATTR = "density_w"
PART_ATTR = "part"
PIN_EPS = 1e-4          # lift_tower's rect containment tolerance
BAND_GUARD = 0.005      # lift_tower's guard between the packed band and a reserved rect
BOUNDS_TOL = 1e-6
ZERO_ISLAND_AREA = 1e-9


# --------------------------------------------------------------------------------------
# Array helpers
# --------------------------------------------------------------------------------------

def default_margin_px(A):
    """Island margin in texels for an atlas of size A: mip-4 safe at every shipped size."""
    return max(16, int(A) // 64)


def loop_uvs(layer):
    """(N, 2) float64 array of a UV layer's loop coordinates."""
    n = len(layer.data)
    buf = np.empty(n * 2, dtype=np.float32)
    layer.data.foreach_get("uv", buf)
    return buf.reshape(n, 2).astype(np.float64)


def set_loop_uvs(layer, arr):
    """Write an (N, 2) array back into a UV layer."""
    layer.data.foreach_set("uv", np.ascontiguousarray(arr, dtype=np.float32).ravel())


def face_loop_table(mesh):
    """(loop_start, loop_total) int arrays per polygon."""
    n = len(mesh.polygons)
    start = np.empty(n, dtype=np.int32)
    total = np.empty(n, dtype=np.int32)
    mesh.polygons.foreach_get("loop_start", start)
    mesh.polygons.foreach_get("loop_total", total)
    return start, total


def face_uv_areas(mesh, uv, start=None, total=None):
    """Per-polygon UV area (shoelace, absolute) for an (N, 2) loop-UV array."""
    if start is None:
        start, total = face_loop_table(mesh)
    out = np.zeros(len(start), dtype=np.float64)
    for i in range(len(start)):
        s, t = int(start[i]), int(total[i])
        x = uv[s:s + t, 0]
        y = uv[s:s + t, 1]
        out[i] = 0.5 * abs(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))
    return out


def face_world_areas(mesh):
    """Per-polygon 3D area in mesh-local units (our meshes ship with unit scale)."""
    n = len(mesh.polygons)
    a = np.empty(n, dtype=np.float32)
    mesh.polygons.foreach_get("area", a)
    return a.astype(np.float64)


def face_px_per_m(mesh, layer, A):
    """Per-polygon texel density sqrt(uv_area * A^2 / world_area), nan for zero-area faces."""
    uv_a = face_uv_areas(mesh, loop_uvs(layer))
    w_a = face_world_areas(mesh)
    with np.errstate(divide="ignore", invalid="ignore"):
        d = np.sqrt(uv_a * float(A) * float(A) / w_a)
    d[w_a <= 0.0] = np.nan
    return d


# --------------------------------------------------------------------------------------
# Islands, pins, weights
# --------------------------------------------------------------------------------------

def uv_islands(mesh, layer, faces=None, quant=1e-5):
    """
    Group polygons into UV islands: faces sharing a mesh vertex at the SAME UV coordinate
    (within `quant`) are connected. Returns a list of lists of polygon indices.
    `faces` restricts the grouping to that iterable of polygon indices.
    """
    uv = loop_uvs(layer)
    nloops = len(mesh.loops)
    vidx = np.empty(nloops, dtype=np.int32)
    mesh.loops.foreach_get("vertex_index", vidx)
    q = np.round(uv / quant).astype(np.int64)
    start, total = face_loop_table(mesh)
    wanted = range(len(start)) if faces is None else sorted(set(int(f) for f in faces))

    parent = {}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    owner = {}
    for f in wanted:
        parent[f] = f
        s, t = int(start[f]), int(total[f])
        for li in range(s, s + t):
            key = (int(vidx[li]), int(q[li, 0]), int(q[li, 1]))
            o = owner.get(key)
            if o is None:
                owner[key] = f
            else:
                union(o, f)
    groups = {}
    for f in wanted:
        groups.setdefault(find(f), []).append(f)
    return [sorted(g) for g in groups.values()]


def pinned_faces(mesh, layer, reserved, eps=PIN_EPS):
    """Polygon indices whose loops ALL lie inside some reserved (u0, v0, u1, v1) rect."""
    if not reserved:
        return set()
    uv = loop_uvs(layer)
    inside = np.zeros(len(uv), dtype=bool)
    for (u0, v0, u1, v1) in reserved:
        inside |= ((uv[:, 0] >= u0 - eps) & (uv[:, 0] <= u1 + eps) &
                   (uv[:, 1] >= v0 - eps) & (uv[:, 1] <= v1 + eps))
    start, total = face_loop_table(mesh)
    pinned = set()
    for i in range(len(start)):
        s, t = int(start[i]), int(total[i])
        if t and inside[s:s + t].all():
            pinned.add(i)
    return pinned


def face_density_weights(mesh, density_layer=DENSITY_ATTR):
    """Per-polygon density weight. Missing attribute or values <= 0 read as 1.0."""
    n = len(mesh.polygons)
    w = np.ones(n, dtype=np.float64)
    attr = mesh.attributes.get(density_layer)
    if attr is None or attr.domain != "FACE" or attr.data_type != "FLOAT":
        return w
    buf = np.empty(n, dtype=np.float32)
    attr.data.foreach_get("value", buf)
    w = buf.astype(np.float64)
    w[~(w > 0.0)] = 1.0
    return w


def set_density(parts, weights):
    """
    Set the float FACE attribute "density_w" on every part before the join.

    `parts` is the generator's [(obj, quad), ...] list. `weights` maps a name PREFIX to a float
    (hero faces > 1, corrugated >= 2). The longest matching prefix wins. Parts without a match get
    1.0, so the attribute exists (and survives the join) everywhere. Returns {part_name: weight}.
    """
    applied = {}
    prefixes = sorted(weights.items(), key=lambda kv: -len(kv[0]))
    for obj, _quad in parts:
        w = 1.0
        for prefix, value in prefixes:
            if obj.name.startswith(prefix):
                w = float(value)
                break
        me = obj.data
        attr = me.attributes.get(DENSITY_ATTR)
        if attr is None:
            attr = me.attributes.new(DENSITY_ATTR, "FLOAT", "FACE")
        attr.data.foreach_set("value", [w] * len(me.polygons))
        applied[obj.name] = w
    return applied


# --------------------------------------------------------------------------------------
# The packer
# --------------------------------------------------------------------------------------

def _select_unpinned(obj, mesh, pinned):
    """Enter edit mode with every face selected EXCEPT the pinned ones (UV sync-select)."""
    scene = bpy.context.scene
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    scene.tool_settings.use_uv_select_sync = True
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bpy.ops.mesh.select_all(action="SELECT")
    bm = bmesh.from_edit_mesh(mesh)
    bm.faces.ensure_lookup_table()
    for i in pinned:
        bm.faces[i].select_set(False)       # uv ops only move selected islands
    bmesh.update_edit_mesh(mesh)


def _scale_islands_by_weight(mesh, layer, pinned, weights):
    """Scale each non-pinned island about its UV centroid by the mean face weight."""
    if np.allclose(weights, 1.0):
        return 0
    free = [i for i in range(len(mesh.polygons)) if i not in pinned]
    islands = uv_islands(mesh, layer, faces=free)
    uv = loop_uvs(layer)
    start, total = face_loop_table(mesh)
    scaled = 0
    for isl in islands:
        w = float(np.mean(weights[isl]))
        if abs(w - 1.0) < 1e-6:
            continue
        idx = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in isl])
        c = uv[idx].mean(axis=0)
        uv[idx] = c + (uv[idx] - c) * w
        scaled += 1
    set_loop_uvs(layer, uv)
    return scaled


def free_v_band(reserved, eps=PIN_EPS, guard=BAND_GUARD):
    """Tallest v-band (lo, hi) free of every reserved rect (rects treated full-width)."""
    v_edges = sorted({0.0, 1.0} | {r[1] for r in reserved} | {r[3] for r in reserved})
    free = [(a, b) for a, b in zip(v_edges, v_edges[1:])
            if not any(v0 - eps <= (a + b) / 2.0 <= v1 + eps
                       for (_u0, v0, _u1, v1) in reserved)]
    g0, g1 = max(free, key=lambda s: s[1] - s[0])
    lo = g0 + (guard if g0 > 0.0 else 0.0)
    hi = g1 - guard
    return lo, hi


def _squeeze_into_band(mesh, layer, pinned, lo, hi):
    """Linear v remap of every non-pinned loop into [lo, hi] (island-safe: affine)."""
    uv = loop_uvs(layer)
    start, total = face_loop_table(mesh)
    mask = np.ones(len(uv), dtype=bool)
    for i in pinned:
        mask[start[i]:start[i] + total[i]] = False
    uv[mask, 1] = lo + uv[mask, 1] * (hi - lo)
    set_loop_uvs(layer, uv)


def check_layout(mesh, layer, reserved, pinned, tol=BOUNDS_TOL):
    """Raise RuntimeError unless the layout satisfies the pack_unique contract."""
    uv = loop_uvs(layer)
    if len(uv) == 0:
        raise RuntimeError(f"{mesh.name}: no UV loops to pack")
    lo, hi = uv.min(axis=0), uv.max(axis=0)
    if (lo < -tol).any() or (hi > 1.0 + tol).any():
        raise RuntimeError(f"{mesh.name}: UVUnique loops outside [0,1]: min {lo}, max {hi}")
    start, total = face_loop_table(mesh)
    mask = np.ones(len(uv), dtype=bool)
    for i in pinned:
        mask[start[i]:start[i] + total[i]] = False
    for (u0, v0, u1, v1) in reserved:
        hit = (mask & (uv[:, 0] > u0 + tol) & (uv[:, 0] < u1 - tol) &
               (uv[:, 1] > v0 + tol) & (uv[:, 1] < v1 - tol))
        if hit.any():
            raise RuntimeError(f"{mesh.name}: {int(hit.sum())} non-pinned loops inside "
                               f"reserved rect {(u0, v0, u1, v1)}")
    areas = face_uv_areas(mesh, uv, start, total)
    islands = uv_islands(mesh, layer)
    smallest = min(float(areas[isl].sum()) for isl in islands)
    if smallest <= ZERO_ISLAND_AREA:
        raise RuntimeError(f"{mesh.name}: zero-area UV island (area {smallest:.3e})")
    return islands


def _share_loops(mesh, share, pinned):
    """Validate a bay-sharing list (pack_unique `share`) against the mesh and the pinned set.
    Returns (dup face set, canonical loop indices, dup loop indices) as int arrays in
    correspondence: loop k of dup poly j <- loop k of canon poly j. Raises ValueError on a
    malformed list (unequal lengths / loop counts, a face used twice, a pinned face, an
    index out of range)."""
    start, total = face_loop_table(mesh)
    n = len(start)
    seen, dup = set(), set()
    src, dst = [], []
    for g, (canon, dups) in enumerate(share):
        canon = [int(f) for f in canon]
        if not canon:
            raise ValueError(f"{mesh.name}: share group {g} has no canonical faces")
        for f in canon:
            if not 0 <= f < n:
                raise ValueError(f"{mesh.name}: share group {g}: face {f} out of range")
            if f in seen:
                raise ValueError(f"{mesh.name}: share group {g}: face {f} used twice")
            seen.add(f)
        for d, dp in enumerate(dups):
            dp = [int(f) for f in dp]
            if len(dp) != len(canon):
                raise ValueError(f"{mesh.name}: share group {g} dup {d}: {len(dp)} faces, "
                                 f"canonical {len(canon)}")
            for fc, fd in zip(canon, dp):
                if not 0 <= fd < n:
                    raise ValueError(f"{mesh.name}: share group {g}: face {fd} out of range")
                if fd in seen:
                    raise ValueError(f"{mesh.name}: share group {g}: face {fd} used twice")
                if int(total[fc]) != int(total[fd]):
                    raise ValueError(f"{mesh.name}: share group {g}: face {fd} has "
                                     f"{int(total[fd])} loops, canonical {fc} {int(total[fc])}")
                seen.add(fd)
                dup.add(fd)
                src.append(np.arange(start[fc], start[fc] + total[fc]))
                dst.append(np.arange(start[fd], start[fd] + total[fd]))
    bad = sorted(seen & set(pinned))
    if bad:
        raise ValueError(f"{mesh.name}: shared faces {bad[:8]} lie in a reserved rect (pinned)")
    if not src:
        return dup, np.zeros(0, dtype=np.int64), np.zeros(0, dtype=np.int64)
    return dup, np.concatenate(src).astype(np.int64), np.concatenate(dst).astype(np.int64)


def _pack_layout(obj, mesh, A, reserved, margin_px=None, equalise=True,
                 density_layer=DENSITY_ATTR, share=None):
    """Steps 1-6 of the contract on the existing 'UVUnique' layer. Returns the report. `share` (default
    None = the pre-pass code path, unchanged): bay sharing, module docstring."""
    if UNIQUE_LAYER not in mesh.uv_layers:
        raise RuntimeError(f"{mesh.name}: no '{UNIQUE_LAYER}' layer to pack")
    reserved = list(reserved or [])
    margin_px = default_margin_px(A) if margin_px is None else int(margin_px)
    layer = mesh.uv_layers[UNIQUE_LAYER]
    mesh.uv_layers.active = layer

    # 1. pin by deselection
    pinned = pinned_faces(mesh, layer, reserved)
    hold = pinned                                  # faces the uv ops never see
    if share is not None:
        dup, share_src, share_dst = _share_loops(mesh, share, pinned)
        hold = pinned | dup
    _select_unpinned(obj, mesh, hold)
    # 2. equalize: UV area proportional to 3D area
    if equalise:
        bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode="OBJECT")
    # 3. per-island density weights (about the island centroid, global scale follows)
    layer = mesh.uv_layers[UNIQUE_LAYER]           # mode switches invalidate the handle
    weights = face_density_weights(mesh, density_layer)
    n_scaled = _scale_islands_by_weight(mesh, layer, hold, weights)
    # 4. ONE pack pass, no rotation (the EMIT rebake keeps texel vectors as they are)
    _select_unpinned(obj, mesh, hold)
    bpy.ops.uv.pack_islands(rotate=False, scale=True, margin_method="FRACTION",
                            margin=margin_px / float(A), shape_method="AABB")
    bpy.ops.object.mode_set(mode="OBJECT")
    # 5. squeeze into the tallest reserved-free v-band
    layer = mesh.uv_layers[UNIQUE_LAYER]
    lo, hi = free_v_band(reserved) if reserved else (0.0, 1.0)
    if reserved:
        _squeeze_into_band(mesh, layer, hold, lo, hi)
    # 5b. bay sharing: every dup loop takes its canonical loop's packed UV
    if share is not None and len(share_dst):
        uv = loop_uvs(layer)
        uv[share_dst] = uv[share_src]
        set_loop_uvs(layer, uv)
    # 6. contract asserts
    islands = check_layout(mesh, layer, reserved, pinned)

    dens = face_px_per_m(mesh, layer, A)
    free_mask = np.ones(len(dens), dtype=bool)
    for i in pinned:
        free_mask[i] = False
    d = dens[free_mask & np.isfinite(dens)]
    report = {
        "A": int(A), "margin_px": margin_px, "faces": len(mesh.polygons),
        "pinned_faces": len(pinned), "islands": len(islands),
        "islands_weighted": n_scaled, "band": (lo, hi),
        "density_w": (float(weights.min()), float(weights.max())),
        "px_per_m": {"min": float(d.min()) if d.size else 0.0,
                     "median": float(np.median(d)) if d.size else 0.0,
                     "max": float(d.max()) if d.size else 0.0},
    }
    if share is not None:                          # keys only exist when sharing was asked
        report["shared_faces"] = len(dup)
        report["shared_groups"] = len(share)
        print(f"[uv] {obj.name}: bay sharing {len(dup)} dup faces in {len(share)} group(s) "
              f"stacked on their canonical islands")
    print(f"[uv] {obj.name}: packed {report['islands']} islands ({report['pinned_faces']} "
          f"pinned faces) @ {A}, margin {margin_px} px, band v {lo:.3f}..{hi:.3f}, "
          f"px/m min {report['px_per_m']['min']:.0f} / med {report['px_per_m']['median']:.0f}"
          f" / max {report['px_per_m']['max']:.0f}")
    return report


def pack_unique(obj, A, reserved_rects, margin_px=None, equalise=True,
                density_layer=DENSITY_ATTR, share=None):
    """
    Create "UVUnique" from the active UV layer and pack it uniquely (module docstring).

    The active layer keeps its name (lift_tower renames it "UVAtlas" itself). A stale "UVUnique"
    that is not the active layer is rebuilt from the active one. `share` = bay sharing (module
    docstring). None keeps the pre-pass call exactly (the keyword is not even passed on, so a
    patched _pack_layout without it still works). Returns the report dict.
    """
    kw = {} if share is None else {"share": share}
    mesh = obj.data
    src = mesh.uv_layers.active
    if src is None:
        raise RuntimeError(f"{obj.name}: no active UV layer to pack from")
    if src.name == UNIQUE_LAYER:
        raise RuntimeError(f"{obj.name}: the active layer is already '{UNIQUE_LAYER}'; "
                           f"activate the source layer first")
    if UNIQUE_LAYER in mesh.uv_layers:
        mesh.uv_layers.remove(mesh.uv_layers[UNIQUE_LAYER])
        mesh.uv_layers.active = mesh.uv_layers[src.name]
    uniq = mesh.uv_layers.new(name=UNIQUE_LAYER, do_init=True)   # copies the active layer
    mesh.uv_layers.active = uniq
    return _pack_layout(obj, mesh, A, reserved_rects, margin_px=margin_px,
                        equalise=equalise, density_layer=density_layer, **kw)


def finish_packer(obj, mesh, A, reserved):
    """
    Packer hook for lift_tower.apply_finish_textures(packer=finish_packer).

    It replaces the step 1 pack+squeeze of apply_finish_textures. "UVUnique" already exists (copied
    from "UVAtlas") and is active. The hook pins stamped-strip islands by deselection, equalizes,
    applies density_w, packs once and squeezes into the reserved-free v-band. It returns None and
    leaves the object in OBJECT mode with "UVUnique" active, exactly as the inline code did.
    """
    _pack_layout(obj, mesh, A, reserved, margin_px=None, equalise=True,
                 density_layer=DENSITY_ATTR)
    return None


# --------------------------------------------------------------------------------------
# Density report
# --------------------------------------------------------------------------------------

def _part_names(mesh):
    import json
    raw = mesh.get(cs2_asset.PART_TABLE_PROP)
    if not raw:
        return []
    try:
        return list(json.loads(raw))
    except (TypeError, ValueError):
        return []


def density_report(obj, A, source_layer="UVAtlas", shipped_layer=UNIQUE_LAYER,
                   reserved_rects=None, raise_on_error=False):
    """
    Per-part texel density before and after the unique repack.

    Returns [(part_index, source_px_per_m, shipped_px_per_m), ...] sorted worst-first by
    |log(shipped / source)|. part_index is the INT face attribute "part" (0 when the mesh is
    untagged). Prints a table plus the spread checks of the plan:
    - ERROR when the shipped max/median or median/min exceeds 2 over non-pinned parts
    - WARN when a part ships above 1.5x its source density (upsampled, no new detail)

    `reserved_rects` lets the spread check ignore pinned (stamped) parts. `raise_on_error` turns the
    ERROR line into a RuntimeError.
    """
    mesh = obj.data
    src = mesh.uv_layers.get(source_layer)
    shp = mesh.uv_layers.get(shipped_layer)
    if src is None or shp is None:
        raise RuntimeError(f"{obj.name}: need UV layers '{source_layer}' and "
                           f"'{shipped_layer}' (have {[l.name for l in mesh.uv_layers]})")
    n = len(mesh.polygons)
    part = np.zeros(n, dtype=np.int32)
    attr = mesh.attributes.get(PART_ATTR)
    if attr is not None and attr.domain == "FACE":
        attr.data.foreach_get("value", part)
    names = _part_names(mesh)
    w_area = face_world_areas(mesh)
    s_area = face_uv_areas(mesh, loop_uvs(src))
    d_area = face_uv_areas(mesh, loop_uvs(shp))
    pinned = pinned_faces(mesh, shp, reserved_rects or [])
    pinned_mask = np.zeros(n, dtype=bool)
    for i in pinned:
        pinned_mask[i] = True

    rows = []
    unpinned_shipped = []
    for p in sorted(set(int(x) for x in part)):
        sel = part == p
        wa = float(w_area[sel].sum())
        if wa <= 0.0:
            continue
        s = math.sqrt(float(s_area[sel].sum()) * A * A / wa)
        d = math.sqrt(float(d_area[sel].sum()) * A * A / wa)
        rows.append((p, s, d))
        if not pinned_mask[sel].all():
            unpinned_shipped.append(d)

    def label(p):
        return names[p - 1] if 1 <= p <= len(names) else f"part{p}"

    def ratio(r):
        return r[2] / r[1] if r[1] > 0 else float("inf")

    rows.sort(key=lambda r: -abs(math.log(max(ratio(r), 1e-9))))
    print(f"[uv] density report {obj.name} @ {A}: {len(rows)} parts "
          f"({source_layer} -> {shipped_layer})")
    print(f"  {'part':>4}  {'name':<24} {'src px/m':>9} {'ship px/m':>9} {'ratio':>6}")
    for p, s, d in rows:
        print(f"  {p:>4}  {label(p):<24.24} {s:>9.1f} {d:>9.1f} {ratio((p, s, d)):>6.2f}")
    problems = []
    if unpinned_shipped:
        arr = np.array(unpinned_shipped)
        med = float(np.median(arr))
        if med > 0 and (arr.max() / med > 2.0 or med / arr.min() > 2.0):
            problems.append(f"shipped density spread: min {arr.min():.0f} / median "
                            f"{med:.0f} / max {arr.max():.0f} px/m exceeds 2x")
    up = [(p, s, d) for p, s, d in rows if s > 0 and d > 1.5 * s]
    for p, s, d in up:
        print(f"  WARN part {p} {label(p)} ships at {d / s:.2f}x its source density "
              f"(upsampled, no new detail)")
    for msg in problems:
        print(f"  ERROR {msg}")
    if problems and raise_on_error:
        raise RuntimeError(f"{obj.name}: " + "; ".join(problems))
    if rows:
        worst = rows[0]
        print(f"  worst ratio: part {worst[0]} {label(worst[0])} {ratio(worst):.2f}x")
    return rows
