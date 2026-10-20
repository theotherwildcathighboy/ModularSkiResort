"""
cs2_submesh.py - material sub-meshes (`_Win`, `_Gls`, ...) split off a joined asset mesh.

Contract (art-pipeline-v2.md section 3.8, roundL-spec.md "cs2_submesh.py"):

  split_submesh(parent, table, prefixes, suffix, win_cell=None) -> sub_obj
      Runs AFTER join / orient_for_net and BEFORE the finish pass. The faces whose part
      name (INT face attribute "part" -> `table`) starts with one of `prefixes` are
      MOVED: deleted from `parent` (no texels, no AO, no z-fight) and kept on a copy that
      becomes the sub-mesh. The sub carries the parent's transform, no material and
      exactly one UV layer ("UVMap").
        Win family (Win/Wim/Wif/Wio): pane-box faces with |n.z| > 0.9 are deleted (the top
        and bottom of a window box are buried "windows"); every remaining connected
        coplanar pane is mapped into the 5x5 room cell `win_cell = (col, row)`, i.e. the
        UV rect (col/5, row/5)-((col+1)/5, (row+1)/5), with world +Z -> higher v. Panes
        are tiled inside the cell so no two islands overlap, and every loop is inset from
        the cell border so the importer's `int(v*5)*5 + int(u*5)` of each triangle's first
        vertex yields exactly `row*5 + col`.
        Anything else (Gls/Glf/Glm/Gra/Wat): smart_project + one pack_islands pass, unique
        and non-overlapping (`_Gls` needs no textures: DefaultGlassDoubleSided).
  export_submeshes(sub, name, suffix, asset_dir, lods=(1,)) -> [paths]
      Writes <name>_<suffix>.fbx and <name>_LOD<k>_<suffix>.fbx (same mesh, panes are
      never decimated) through cs2_asset.export_fbx, so the sub is baked with the very
      same x100 / -90 deg X transform as the parent. The mesh name inside each file
      equals the file stem (the importer pairs by name).
  submesh_name(base, suffix) -> f"{base}_{suffix}"; suffix whitelist SUBMESH_SUFFIXES.

Engine facts this relies on (decompile 1.6.2, see the spec): a sub-mesh must have the
parent's transform (only TryConcat compares it), no material and no overlapping UVs; the
`_Win` room id is int(v*5)*5 + int(u*5) of the first vertex of each triangle (Window.cs:52-71),
same-id windows merge within a distance; UV0 is rewritten afterwards, so aspect and top
orientation are cosmetic but kept. No top-level side effects; import cs2_asset for the
shared names.
"""

import math
import os

import bpy
import bmesh
from mathutils import Vector

import cs2_asset

SUBMESH_SUFFIXES = frozenset(
    {"LOD1", "LOD2", "Win", "Wim", "Wif", "Wio", "Gls", "Glf", "Glm", "Gra", "Wat"})
WIN_FAMILY = ("Win", "Wim", "Wif", "Wio")
UV_LAYER = "UVMap"
# Cosine threshold for "same plane" when grouping pane faces; ~2.6 deg.
_COPLANAR_COS = 0.999
# Inset of every pane island from its tile, as a fraction of the tile side. Keeps the
# first vertex strictly inside the room cell and gives the importer's mip chain a gutter.
_PANE_INSET = 0.08


class SubmeshError(RuntimeError):
    pass


# --------------------------------------------------------------------------------------
# Naming
# --------------------------------------------------------------------------------------

def submesh_name(base, suffix):
    """`<base>_<suffix>` after validating the base name and whitelisting the suffix."""
    cs2_asset.validate_asset_name(base)
    if suffix not in SUBMESH_SUFFIXES:
        raise SubmeshError(
            f"sub-mesh suffix {suffix!r} is not one the importer binds a material to; "
            f"use one of {sorted(SUBMESH_SUFFIXES)}")
    return f"{base}_{suffix}"


# --------------------------------------------------------------------------------------
# bmesh helpers
# --------------------------------------------------------------------------------------

def _part_layer(bm, obj):
    layer = bm.faces.layers.int.get("part")
    if layer is None:
        raise SubmeshError(f"{obj.name}: no 'part' face attribute (call cs2_asset.tag_parts "
                           f"before join)")
    return layer


def _faces_with_prefix(bm, layer, table, prefixes):
    prefixes = tuple(prefixes)
    out = []
    for f in bm.faces:
        idx = f[layer] - 1
        if 0 <= idx < len(table) and table[idx].startswith(prefixes):
            out.append(f)
    return out


def _select_only(obj):
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def _single_uv_layer(me):
    """Leave `me` with exactly one UV layer named UV_LAYER (the importer reads UV0 only)."""
    for layer in list(me.uv_layers):
        if layer.name != UV_LAYER:
            me.uv_layers.remove(layer)
    if UV_LAYER not in me.uv_layers:
        me.uv_layers.new(name=UV_LAYER)
    me.uv_layers.active = me.uv_layers[UV_LAYER]
    return me.uv_layers[UV_LAYER]


def _connected_coplanar_groups(bm):
    """Faces grouped by edge connectivity AND shared plane orientation: one group per pane."""
    bm.faces.ensure_lookup_table()
    seen = set()
    groups = []
    for start in bm.faces:
        if start.index in seen:
            continue
        group = []
        stack = [start]
        seen.add(start.index)
        while stack:
            f = stack.pop()
            group.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g.index in seen:
                        continue
                    if f.normal.dot(g.normal) >= _COPLANAR_COS:
                        seen.add(g.index)
                        stack.append(g)
        groups.append(group)
    return groups


def _pane_axes(normal):
    """In-plane (right, up) axes of a pane: up = world +Z projected onto the plane, right
    = up x n (a viewer facing the pane sees u grow to their right)."""
    n = normal.normalized()
    up = Vector((0.0, 0.0, 1.0))
    up = up - n * up.dot(n)
    if up.length < 1e-6:
        # Horizontal pane (should have been deleted for the Win family): use +Y as up.
        up = Vector((0.0, 1.0, 0.0))
        up = up - n * up.dot(n)
    up.normalize()
    right = up.cross(n).normalized()
    return right, up


def _map_pane(pane, uv_layer, rect):
    """Fit one pane's faces into UV `rect` = (u0, v0, u1, v1), aspect preserved, +Z up."""
    normal = Vector((0.0, 0.0, 0.0))
    for f in pane:
        normal += f.normal
    if normal.length < 1e-6:
        normal = pane[0].normal.copy()
    right, up = _pane_axes(normal)
    pts = {}
    for f in pane:
        for loop in f.loops:
            co = loop.vert.co
            pts[loop.index] = (co.dot(right), co.dot(up))
    us = [p[0] for p in pts.values()]
    vs = [p[1] for p in pts.values()]
    umin, umax, vmin, vmax = min(us), max(us), min(vs), max(vs)
    w = max(umax - umin, 1e-6)
    h = max(vmax - vmin, 1e-6)
    u0, v0, u1, v1 = rect
    rw, rh = u1 - u0, v1 - v0
    inset = _PANE_INSET * min(rw, rh)
    aw, ah = rw - 2 * inset, rh - 2 * inset
    s = min(aw / w, ah / h)
    ou = u0 + inset + (aw - w * s) * 0.5
    ov = v0 + inset + (ah - h * s) * 0.5
    for f in pane:
        for loop in f.loops:
            pu, pv = pts[loop.index]
            loop[uv_layer].uv = (ou + (pu - umin) * s, ov + (pv - vmin) * s)


def _win_cell_rect(win_cell):
    col, row = win_cell
    if not (0 <= col < 5 and 0 <= row < 5):
        raise SubmeshError(f"win_cell {win_cell!r} outside the 5x5 room grid")
    return (col / 5.0, row / 5.0, (col + 1) / 5.0, (row + 1) / 5.0)


def _tile_rects(cell, n):
    """Split `cell` into n non-overlapping tiles (near-square grid), row-major from the top."""
    u0, v0, u1, v1 = cell
    cols = max(1, int(math.ceil(math.sqrt(n))))
    rows = max(1, int(math.ceil(n / cols)))
    tw, th = (u1 - u0) / cols, (v1 - v0) / rows
    rects = []
    for i in range(n):
        c, r = i % cols, i // cols
        rects.append((u0 + c * tw, v1 - (r + 1) * th, u0 + (c + 1) * tw, v1 - r * th))
    return rects


# --------------------------------------------------------------------------------------
# Split
# --------------------------------------------------------------------------------------

def split_submesh(parent, table, prefixes, suffix, win_cell=None):
    """
    Move the faces of the parts named by `prefixes` off `parent` onto a new sub-mesh
    object named submesh_name(parent.name, suffix). See the module docstring.
    """
    prefixes = (prefixes,) if isinstance(prefixes, str) else tuple(prefixes)
    if not prefixes:
        raise SubmeshError("split_submesh: no part prefixes given")
    if suffix in WIN_FAMILY and win_cell is None:
        raise SubmeshError(f"split_submesh: suffix {suffix} needs win_cell=(col, row)")
    name = submesh_name(parent.name, suffix)
    if name in bpy.data.objects or name in bpy.data.meshes:
        raise SubmeshError(f"{name!r} already exists; Blender would rename the sub-mesh")

    sub = parent.copy()
    sub.data = parent.data.copy()
    sub.name = name
    sub.data.name = name
    bpy.context.collection.objects.link(sub)
    if "." in sub.name:
        raise SubmeshError(f"sub-mesh got renamed to {sub.name!r}")

    # Sub keeps only the tagged faces; parent loses exactly them.
    bm = bmesh.new()
    bm.from_mesh(sub.data)
    layer = _part_layer(bm, sub)
    keep = set(f.index for f in _faces_with_prefix(bm, layer, table, prefixes))
    if not keep:
        bm.free()
        bpy.data.objects.remove(sub, do_unlink=True)
        raise SubmeshError(f"{parent.name}: no faces tagged with {prefixes}")
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context="FACES")
    if suffix in WIN_FAMILY:
        bm.normal_update()
        buried = [f for f in bm.faces if abs(f.normal.z) > 0.9]
        bmesh.ops.delete(bm, geom=buried, context="FACES")
    if not bm.faces:
        bm.free()
        bpy.data.objects.remove(sub, do_unlink=True)
        raise SubmeshError(f"{name}: nothing left after deleting buried pane faces")
    bm.to_mesh(sub.data)
    bm.free()

    bm = bmesh.new()
    bm.from_mesh(parent.data)
    layer = _part_layer(bm, parent)
    kill = _faces_with_prefix(bm, layer, table, prefixes)
    n_moved = len(kill)
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(parent.data)
    bm.free()

    # Sub-mesh rules: parent's transform, no material, one UV layer, no color attributes.
    sub.matrix_world = parent.matrix_world.copy()
    sub.data.materials.clear()
    for attr in list(sub.data.color_attributes):
        sub.data.color_attributes.remove(attr)
    uv = _single_uv_layer(sub.data)

    if suffix in WIN_FAMILY:
        _map_win(sub, win_cell)
    else:
        _unwrap_unique(sub)

    print(f"[submesh] {name}: moved {n_moved} faces off {parent.name}, sub has "
          f"{len(sub.data.polygons)} faces / {cs2_asset.tri_count(sub)} tris"
          + (f", room cell {tuple(win_cell)}" if suffix in WIN_FAMILY else ""))
    return sub


def _map_win(sub, win_cell):
    cell = _win_cell_rect(win_cell)
    bm = bmesh.new()
    bm.from_mesh(sub.data)
    bm.normal_update()
    uv_layer = bm.loops.layers.uv.get(UV_LAYER) or bm.loops.layers.uv.new(UV_LAYER)
    panes = _connected_coplanar_groups(bm)
    for pane, rect in zip(panes, _tile_rects(cell, len(panes))):
        _map_pane(pane, uv_layer, rect)
    bm.to_mesh(sub.data)
    bm.free()
    _check_win_uvs(sub, cell)


def _check_win_uvs(sub, cell):
    u0, v0, u1, v1 = cell
    uv = sub.data.uv_layers[UV_LAYER]
    for loop_uv in uv.data:
        u, v = loop_uv.uv
        if not (u0 < u < u1 and v0 < v < v1):
            raise SubmeshError(f"{sub.name}: UV ({u:.4f}, {v:.4f}) outside room cell {cell}")


def _unwrap_unique(sub):
    """Non-overlapping unique unwrap for the glass/other families (no textures needed)."""
    _select_only(sub)
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
        bpy.ops.uv.select_all(action="SELECT")
        bpy.ops.uv.pack_islands(rotate=False, scale=True, margin_method="FRACTION",
                                margin=8.0 / 512.0, shape_method="AABB")
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    for loop_uv in sub.data.uv_layers[UV_LAYER].data:
        u, v = loop_uv.uv
        if not (-1e-4 <= u <= 1 + 1e-4 and -1e-4 <= v <= 1 + 1e-4):
            raise SubmeshError(f"{sub.name}: UV ({u:.4f}, {v:.4f}) outside 0..1 after pack")


# --------------------------------------------------------------------------------------
# Export
# --------------------------------------------------------------------------------------

def export_submeshes(sub, name, suffix, asset_dir, lods=(1,)):
    """
    Write `<name>_<suffix>.fbx` plus `<name>_LOD<k>_<suffix>.fbx` for k in `lods`, all
    from the same `sub` mesh (panes are never decimated; LOD2 normally omits the sub).
    The object is renamed to each file stem for the duration of its export because
    cs2_asset.export_fbx writes obj.name verbatim and the importer pairs by that name.
    """
    cs2_asset.validate_asset_name(name)
    if suffix not in SUBMESH_SUFFIXES or suffix.startswith("LOD"):
        raise SubmeshError(f"export_submeshes: bad material suffix {suffix!r}")
    os.makedirs(asset_dir, exist_ok=True)
    stems = [f"{name}_{suffix}"] + [f"{name}_LOD{int(k)}_{suffix}" for k in lods]
    orig_name, orig_data = sub.name, sub.data.name
    paths = []
    try:
        for stem in stems:
            clash = bpy.data.objects.get(stem)
            if clash is not None and clash != sub:
                raise SubmeshError(f"another object is already named {stem!r}")
            sub.name = stem
            sub.data.name = stem
            if sub.name != stem:
                raise SubmeshError(f"could not take the name {stem!r} (got {sub.name!r})")
            path = os.path.join(asset_dir, stem + ".fbx")
            cs2_asset.export_fbx(sub, path)
            paths.append(path)
            print(f"[submesh] wrote {path} ({cs2_asset.tri_count(sub)} tris)")
    finally:
        sub.name = orig_name
        sub.data.name = orig_data
    return paths
