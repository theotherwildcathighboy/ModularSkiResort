"""
cs2_ingest - bring a FOREIGN mesh (a Sketchfab .blend or .fbx) into the generator frame:
load + bake transforms, relink texture sets, rescale, reframe, split into named parts,
clean, reduce. Asset round 2026-10, phase P0a (ROUND-SPEC.md sec 6; R3 adapt route; R20
originals live in assets/external/<id>/ with a SOURCE.md).

WHY
    Nothing in assets/blender could take a mesh it did not build. FBX import only re-read
    our own exports (cs2_validate.import_fbx_temp, cs2_preview.import_generation - which
    guesses units and would leave Kagelok's chair 37.7 m tall), no texture relink existed,
    and cs2_asset._load_map reads ambientCG names only (pipeline-gap.md sec 3.2, skeptic
    verdict 1). The two reference models break every shortcut:
      * Kagelok "Chair Lift" (.blend from 2.79): 58 of 59 objects are hide_viewport, so
        their matrix_world stays identity until unhide + frame_set + view_layer.update;
        chairs 01-10 ride FOLLOW_PATH + COPY_ROTATION, grips D01-D10 FOLLOW_PATH; every
        material carries a versioning tree with TWO Material Outputs (EEVEE + CYCLES) and
        no image node; the 117 PNGs on disk are named unlike the materials
        (ref-models.md sec 1.2, sec 1.6; ref-models-skeptic verdicts 1, 7, 8, misses 6, 9).
      * gondola.fbx (ASCII FBX 7.5): only bpy.ops.wm.fbx_import reads it; the two roots
        arrive with scale 0.01 and a +90 deg X turn (Y-up file) above 187 empties
        (ref-models.md sec 2.1; measured 2026-10-11 with Blender 5.2.2).
    A generator must never open_mainfile (it would replace the session - R10 keeps
    generator code out of Richard's live Blender), so .blend sources are APPENDED through
    bpy.data.libraries.load(link=False) into a fresh "CS2_INGEST_<stem>" collection.

CONTRACT (pipeline-gap.md sec 3.3, corrected by its skeptic verdict 3 and miss 4)
    SourceSet                     one source material: base / normal / height / rough /
                                  metal / slot / recolor_albedo / emissive (+ key)
    load_external(path, objects=None, report=None) -> [Object]
        Mesh objects only, sorted by name, parentless, constraint-free, animation-free,
        identity transform: the EVALUATED world matrix at frame BAKE_FRAME (FOLLOW_PATH /
        COPY_ROTATION results included) is baked into the vertices (mirror-safe), the FBX
        0.01 root scale included. Curves, empties and other helpers - and, for a filtered
        .blend load, dependencies nobody asked for (constraint targets) - are removed.
        `objects` filters by source name (.blend) or by any ancestor name (.fbx subtree).
        Each object carries SRC_NAME_PROP (library / imported name), SRC_PATH_PROP (the
        pre-flatten hierarchy "root/child/obj") and SRC_FILE_PROP. An .fbx load REFUSES
        (ValueError) while objects of the same file are still loaded, or when the import
        had to rename a node ".NNN" against a name already in the session: FBX names are
        Blender's post-dedup names, so the filter, SRC_NAME / SRC_PATH and to_parts'
        rename map would drift. discard() first, or pass every subtree in ONE objects=[...]
        call. The frame, the selection and the active object are restored on return.
    match_texture_sets(materials, tex_dir, overrides=None, keep=None)
        -> ({material name: SourceSet}, [unmatched material names])
        Every returned set HAS a base map. `keep` (names, or True for all) RAISES
        ValueError for a kept material without one: no silent mid-grey. Never calls
        cs2_asset._load_map (its *_Roughness glob matches *_Albedo_Roughness first).
    load_map(path, size=None) -> (h, w, 4) float32, raw stored values (palette, grey and
        grey + alpha PNGs expand to RGBA)
    rescale_by_reference(objs, ref, axis, metres) -> factor     (scales about the origin)
    reframe(objs, forward, up, pivot="base", pivot_ref=None) -> Matrix
        +X travel, +Z up; "base" puts the lowest point at z = 0 and the ref's XY centre on
        the Z axis (lift_chair.py:551-569: base pivot, grip at the top).
    to_parts(objs, rename, classify=None) -> [(obj, None), ...]   loose parts "<prefix>_NN";
        classify(part, source_name, (lo, hi)) -> prefix | None re-prefixes single loose
        parts (small hardware inside a kit) so LOD delete-by-prefix can drop them alone
    clean(obj, merge_dist=MERGE_DIST, recalc="manifold") -> report dict
        recalc "manifold" (default) recalculates outward normals only on CLOSED 2-manifold
        components (pose-invariant there) and keeps the source winding on open / non-
        manifold ones; "all" is the old whole-mesh recalc (pose-dependent on non-manifold
        meshes: Kagelok chairs 01/03/05/07, one local mesh, flipped 4/34/0/0 faces);
        "none" keeps every source winding. Foreign custom normals and sharp_edge marks are
        CLEARED (the FBX exporter writes custom normals verbatim and the importer copies
        them; shade_by_angle keeps existing sharp edges).
    reduce(obj, target_tris, delete_prefixes=(), tol=0.25) -> obj   (cuts LOD0 in place,
        re-shaded weighted at SHADE_ANGLE_DEG)
    Helpers: world_bounds, used_materials, colour_sets, material_key, discard.

ORDER
    load_external -> rescale_by_reference (foreign units -> metres) -> reframe -> clean ->
    to_parts -> cs2_asset.tag_parts -> join -> cs2_asset.store_part_table -> reduce (only
    over budget) -> cs2_asset.shade_by_angle(obj, 66) (PIPELINE sec 1: every joined mesh is
    shaded weighted at 66 deg; reduce already ends with exactly that call).
    clean's distances are in the object's CURRENT units: rescale the gondola (imported
    metres, a bolt is ~0.2 mm there) before cleaning it.

Derived from (file:line):
    load / bake      ref-models.md sec 1.2, sec 4 (unhide, frame_set(1), update); skeptic verdict 1
    FBX              ref-models.md sec 2.1; skeptic verdict 11 (import_scene.fbx refuses ASCII)
    name normaliser  ref-models.md sec 1.6; ref-models-skeptic verdict 8 (9 exact + 19 + 2
                     manual + 3 untextured); pipeline-gap-skeptic miss 4 (case-only prefix
                     collisions, Therm_Bas_Haut HAS a full set, St_Verre_Haut albedo only)
    _load_map trap   cs2_asset.py:262-280; pipeline-gap-skeptic verdict 3
    PNG formats      ref-models-skeptic miss 9 (57 RGBA, 51 grey, 6 grey+alpha, 2 palette, 1 RGB)
    Material Output  ref-models-skeptic verdict 7 / miss 6 (two outputs per material)
    colour attrs     cs2_validate.py:587-589 (the importer reads Color as window/emissive IDs)
    parts            cs2_asset.py:984-1047 (tag_parts / canonical_order / store_part_table);
                     lift_chair.py:83-84 ("<family>_" LOD-delete prefixes)
    reduce           cs2_asset.py:1075-1133 (make_lod_chain: tagged-part delete, COLLAPSE,
                     degenerate + sliver cleanup, shade_by_angle, tolerance assert)
    shading          cs2_asset.py:925-966 (the exporter writes per-corner normals verbatim;
                     shade_by_angle: keep_sharp_edges=True, weighted FACE_AREA)
    base pivot       lift_chair.py:551-569
    review fixes     p0a-integration.md "Fixes applied" (manifold-only recalc, custom-normal
                     clear, FBX dedup refusal, scene-state restore, classify, re-shade)

Credits (CC BY 4.0, ROUND-SPEC sec 8; full lines in assets/external/<id>/SOURCE.md):
    "Chair Lift" by Kagelok - https://sketchfab.com/3d-models/chair-lift-afd305cdd4cb450f80b59f24b4b3e236
    "Ski gongola and lift station detailed drafts" by Wilin_Amaurea -
    https://sketchfab.com/3d-models/ski-gongola-and-lift-station-detailed-drafts-a8fabd3e955643a2a9e9f09bd46635db
    Licence: https://creativecommons.org/licenses/by/4.0/

No top-level side effects; nothing here renders, bakes or exports. Deterministic: sorted
iteration everywhere, no randomness, canonical part order via cs2_asset.tag_parts.
"""

import os
import re
import sys
import unicodedata
from dataclasses import dataclass
from typing import Optional, Tuple, Union

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402


# --------------------------------------------------------------------------------------
# Constants
# --------------------------------------------------------------------------------------

BAKE_FRAME = 1                  # Kagelok's chairs sit near level at frame 1 (ref-models sec 4)
MERGE_DIST = 1e-4               # clean(): remove_doubles, in the object's current units
RECALC_MODES = ("manifold", "all", "none")   # clean(): which components get recalculated
RECALC_DEFAULT = "manifold"
FOREIGN_NORMAL_ATTRS = ("custom_normal", "sharp_edge")   # cleared by clean() (5.2 names)
SHADE_ANGLE_DEG = 66.0          # cs2_asset.shade_by_angle default (PIPELINE sec 1)
DEGENERATE_FRACTION = 0.1       # dissolve_degenerate distance = merge_dist * this
SLIVER_AREA = 1e-12             # zero-area collinear faces the FBX round trip drops
COLLECTION_PREFIX = "CS2_INGEST_"

SRC_NAME_PROP = "cs2_src_name"  # library / imported object name (before any .NNN dedup)
SRC_PATH_PROP = "cs2_src_path"  # "root/child/obj" hierarchy before the flatten
SRC_FILE_PROP = "cs2_src_file"  # basename of the source file

TEX_EXTS = (".png", ".jpg", ".jpeg")

# Map-kind suffixes on the collapsed stem (accent-stripped, casefolded, [ _]+ -> "_").
# Longest first. Displacement names are truncated in the Kagelok set (_DIS, _DISPLA,
# _DISPLACEM, _Displacement), roughness is spelled _Albedo_Roughness AND _AlbedoRoughness.
MAP_SUFFIXES = (
    ("height", re.compile(r"_albedo_normal_dis[a-z]*$")),
    ("normal", re.compile(r"_albedo_normal$")),
    ("rough", re.compile(r"_albedo_?roughness$")),
    ("base", re.compile(r"_albedo$")),
)

# Material key -> texture-set key where no normalisation can bridge the spelling
# (ref-models.md sec 1.6: "Fennetre Rectangle" -> fenetre_rect, "socle betton" -> socle_beton).
NAME_ALIASES = {"fennetrerectangle": "fenetrerect", "soclebetton": "soclebeton"}

# "Barriere_Pil_Haut_et_Bas" vs material "Barriere Pil Haut Bas": the conjunction is
# dropped only between these tokens (never inside a word: "fenetre" keeps its "et").
CONJ_TOKENS = ("et",)
CONJ_CONTEXT = ("haut", "bas")

BLENDER_DUP_SUFFIX = re.compile(r"\.\d{3,}$")
PART_PREFIX_RE = re.compile(r"[a-z][a-z0-9]*_?")


# --------------------------------------------------------------------------------------
# SourceSet
# --------------------------------------------------------------------------------------

@dataclass
class SourceSet:
    """One source material (pipeline-gap.md sec 3.3). Paths are absolute."""
    base: Union[str, Tuple[float, float, float], None]   # albedo path or linear RGB
    normal: Optional[str] = None
    normal_convention: str = "unknown"   # "GL" | "DX" | "unknown" (cs2_bake drops unknown)
    height: Optional[str] = None         # displacement -> cs2_compose.height_to_normal
    rough: Union[str, float, None] = None    # MaskMap A = 1 - rough
    metal: Union[str, float] = 0.0       # MaskMap R
    slot: int = 0                        # ControlMask 0 none, 1 R, 2 G, 3 B
    recolor_albedo: Optional[bool] = None    # None = auto: True when slot > 0 (cs2_bake)
    emissive: Union[str, Tuple[float, float, float], None] = None
    key: str = ""                        # normalised set key the files were grouped by

    def paths(self):
        """The file paths this set references (sorted, de-duplicated)."""
        out = {v for v in (self.base, self.normal, self.height, self.rough, self.metal,
                           self.emissive) if isinstance(v, str)}
        return sorted(out)


# --------------------------------------------------------------------------------------
# Small geometry helpers
# --------------------------------------------------------------------------------------

def _pointers(seq):
    return {x.as_pointer() for x in seq}


def _world_matrix(obj):
    """matrix_basis for a parentless object (never stale), matrix_world otherwise."""
    return obj.matrix_basis.copy() if obj.parent is None else obj.matrix_world.copy()


def _apply_matrix(obj, m):
    """Transform the mesh data by `m`; mirror matrices flip the winding back outward."""
    me = obj.data
    me.transform(m, shape_keys=True)
    if m.to_3x3().determinant() < 0.0:
        me.flip_normals()
    me.update()


def _bake_world(obj):
    """Apply the object's world transform into its (single-user) mesh; identity after."""
    m = _world_matrix(obj)
    if obj.data.users > 1:
        obj.data = obj.data.copy()
    if obj.parent is not None:
        obj.parent = None
        obj.matrix_parent_inverse = Matrix.Identity(4)
    if m != Matrix.Identity(4):
        _apply_matrix(obj, m)
    obj.matrix_basis = Matrix.Identity(4)


def _coords(obj):
    """(n, 3) float64 world-space vertex coordinates."""
    me = obj.data
    n = len(me.vertices)
    co = np.empty(n * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(n, 3).astype(np.float64)
    m = np.array(_world_matrix(obj), dtype=np.float64)
    return co @ m[:3, :3].T + m[:3, 3]


def world_bounds(objs):
    """World-space AABB over the VERTICES of one object or a list -> (lo, hi) Vectors."""
    if isinstance(objs, bpy.types.Object):
        objs = [objs]
    pts = [_coords(o) for o in objs if o.type == "MESH" and len(o.data.vertices)]
    if not pts:
        raise ValueError("world_bounds: no vertices")
    allp = np.concatenate(pts)
    return Vector(allp.min(axis=0)), Vector(allp.max(axis=0))


def _axis_index(axis):
    if isinstance(axis, int) and 0 <= axis <= 2:
        return axis
    s = str(axis).upper().lstrip("+-")
    if s not in ("X", "Y", "Z"):
        raise ValueError(f"axis {axis!r}: expected X, Y, Z or 0-2")
    return "XYZ".index(s)


def _axis_vec(axis):
    """'+X' / '-Y' / 'Z' or a 3-vector -> Vector."""
    if isinstance(axis, str):
        s = axis.strip().upper()
        sign = -1.0 if s.startswith("-") else 1.0
        v = Vector((0.0, 0.0, 0.0))
        v[_axis_index(s)] = sign
        return v
    v = Vector(tuple(float(c) for c in axis))
    if v.length < 1e-9:
        raise ValueError(f"zero-length axis {axis!r}")
    return v


def _source_name(obj):
    return obj.get(SRC_NAME_PROP) or BLENDER_DUP_SUFFIX.sub("", obj.name)


def _new_collection(path):
    coll = bpy.data.collections.new(COLLECTION_PREFIX + os.path.splitext(os.path.basename(path))[0])
    bpy.context.scene.collection.children.link(coll)
    return coll


def _layer_collection(layer, coll):
    if layer.collection == coll:
        return layer
    for child in layer.children:
        hit = _layer_collection(child, coll)
        if hit is not None:
            return hit
    return None


def _hierarchy_path(obj, lib_names=None):
    """Path "root/child/obj" by source name (library name when known, else object name)."""
    names = []
    o = obj
    while o is not None:
        names.append((lib_names or {}).get(o.as_pointer(), o.name))
        o = o.parent
    return "/".join(reversed(names))


# --------------------------------------------------------------------------------------
# Loading
# --------------------------------------------------------------------------------------

def _load_blend(path, wanted):
    """Append objects (all, or the `wanted` names) without touching the open file."""
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        names = sorted(src.objects)
        if wanted is not None:
            missing = sorted(set(wanted) - set(names))
            if missing:
                raise ValueError(f"{os.path.basename(path)}: no object(s) {missing}")
            names = [n for n in names if n in set(wanted)]
        # A COPY: on exit Blender replaces the assigned list's items with the Objects.
        dst.objects = list(names)
    loaded = {}
    for lib_name, obj in zip(names, dst.objects):
        if obj is None:
            raise RuntimeError(f"{os.path.basename(path)}: failed to append {lib_name!r}")
        loaded[obj.as_pointer()] = lib_name
    return loaded


def _load_fbx(path, coll):
    """bpy.ops.wm.fbx_import (5.x; reads ASCII) into `coll` via the active layer collection."""
    vl = bpy.context.view_layer
    prev = vl.active_layer_collection
    vl.active_layer_collection = _layer_collection(vl.layer_collection, coll)
    try:
        res = bpy.ops.wm.fbx_import(filepath=path)
    finally:
        vl.active_layer_collection = prev
    if "FINISHED" not in res:
        raise RuntimeError(f"wm.fbx_import failed on {path}: {res}")


def load_external(path, objects=None, report=None):
    """
    Load a foreign .blend or .fbx into a new "CS2_INGEST_<stem>" collection and return
    its mesh objects, sorted by name, with every transform baked into the vertices.

    .blend: bpy.data.libraries.load(link=False) - never open_mainfile. All appended
            objects (constraint targets included) are linked, UNHIDDEN (hide_viewport and
            hide_set) and evaluated at BAKE_FRAME, so FOLLOW_PATH / COPY_ROTATION land
            where Sketchfab showed them.
    .fbx:   bpy.ops.wm.fbx_import; the 0.01 root scale and the Y-up turn are part of the
            baked world matrix (vertices end in "imported metres").
    Then: modifiers applied from the evaluated object, shared meshes made single-user,
    parents cleared, constraints + animation removed, sorted by name; non-mesh objects
    and unrequested dependencies deleted with their orphaned data.

    `objects`: None = everything; else source names (.blend) or names of any ancestor
    (.fbx: "cabin.001" returns that subtree). Unknown names raise ValueError.
    `report`: optional dict, filled with what happened (dropped helpers, counts, tris).
    .fbx: refuses while objects of the same file are loaded, or when the import renamed a
    node ".NNN" against a name already in the session (module docstring).
    The scene frame, the selection and the active object are restored before returning.
    """
    path = os.path.abspath(path)
    if not os.path.isfile(path):
        raise FileNotFoundError(path)
    ext = os.path.splitext(path)[1].lower()
    if ext not in (".blend", ".fbx"):
        raise ValueError(f"load_external: unsupported source {ext!r} ({path})")
    wanted = None if objects is None else sorted(set(objects))
    if ext == ".fbx":
        base = os.path.basename(path)
        stale = sorted(o.name for o in bpy.data.objects if o.get(SRC_FILE_PROP) == base)
        if stale:
            raise ValueError(
                f"load_external: {len(stale)} object(s) from {base} are still loaded "
                f"({stale[:3]}): a second FBX import renames every clashing node "
                f"'<name>.NNN', so objects=, SRC_NAME / SRC_PATH and to_parts' rename map "
                f"would drift - discard() them first, or load every subtree in ONE "
                f"objects=[...] call")

    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    saved = _scene_state()
    names_before = {o.name for o in bpy.data.objects}
    data_before = {k: _pointers(getattr(bpy.data, k))
                   for k in ("objects", "meshes", "curves", "actions", "materials")}
    coll = _new_collection(path)
    try:
        return _load_external(path, ext, wanted, coll, data_before, names_before, report)
    except BaseException:
        # Leave the session as it was: no half-loaded objects, data or collection.
        for o in [o for o in bpy.data.objects if o.as_pointer() not in data_before["objects"]]:
            bpy.data.objects.remove(o, do_unlink=True)
        _purge_new_orphans(data_before)
        if coll.name in bpy.data.collections:
            bpy.data.collections.remove(coll)
        raise
    finally:
        _restore_scene_state(saved)


def _scene_state():
    """(frame, active object, selected objects): what load_external gives back."""
    vl = bpy.context.view_layer
    # vl.objects can hold None for an object removed since the last view-layer sync
    return (bpy.context.scene.frame_current, vl.objects.active,
            [o for o in vl.objects if o is not None and o.select_get()])


def _restore_scene_state(saved):
    """Undo BAKE_FRAME and the importer's selection (wm.fbx_import selects and activates
    everything it creates). Runs after the evaluated matrices are baked in."""
    frame, active, selected = saved
    sc = bpy.context.scene
    if sc.frame_current != frame:
        sc.frame_set(frame)
    vl = bpy.context.view_layer
    for o in list(vl.objects):
        if o is not None and o.select_get():     # None: a base removed on the error path
            o.select_set(False)
    for o in selected:
        try:
            o.select_set(True)
        except (ReferenceError, RuntimeError):
            pass
    try:
        vl.objects.active = active
    except (ReferenceError, RuntimeError):
        vl.objects.active = None


def _load_external(path, ext, wanted, coll, data_before, names_before, report):
    lib_names = {}
    if ext == ".blend":
        lib_names = _load_blend(path, wanted)
    else:
        _load_fbx(path, coll)
    new = sorted((o for o in bpy.data.objects if o.as_pointer() not in data_before["objects"]),
                 key=lambda o: o.name)
    if not new:
        raise RuntimeError(f"load_external: nothing loaded from {path}")
    if ext == ".fbx":
        # wm.fbx_import names objects AFTER Blender's dedup: a node whose name was taken
        # lands as "<name>.NNN" and the names after it shift (P0a review, FBX dedup).
        bases_before = {BLENDER_DUP_SUFFIX.sub("", n) for n in names_before}
        renamed = sorted(o.name for o in new if BLENDER_DUP_SUFFIX.search(o.name)
                         and BLENDER_DUP_SUFFIX.sub("", o.name) in bases_before)
        if renamed:
            raise ValueError(
                f"load_external: {os.path.basename(path)} node(s) {renamed[:4]} clash with "
                f"names already in the session, so the importer renamed them - remove those "
                f"objects (discard()) first or load into a clean scene")

    # Link (appended objects belong to no collection), then unhide: a viewport-disabled
    # object is never evaluated, so its matrix_world stays at identity (ref-models sec 0.1).
    for o in new:
        current = list(o.users_collection)
        if coll not in current:
            coll.objects.link(o)
            for c in current:
                c.objects.unlink(o)
    unhidden = 0
    for o in new:
        if o.hide_viewport or o.hide_get():
            unhidden += 1
        o.hide_viewport = False
        o.hide_set(False)
    bpy.context.scene.frame_set(BAKE_FRAME)
    bpy.context.view_layer.update()

    # Which objects survive.
    if ext == ".blend":
        keep_ptr = {p for p in lib_names} if wanted is not None else _pointers(new)
    else:
        if wanted is None:
            keep_ptr = _pointers(new)
        else:
            paths = {o.as_pointer(): _hierarchy_path(o, lib_names).split("/") for o in new}
            found = {n for comps in paths.values() for n in comps if n in wanted}
            missing = sorted(set(wanted) - found)
            if missing:
                raise ValueError(f"{os.path.basename(path)}: no object(s) {missing}")
            keep_ptr = {p for p, comps in paths.items() if set(comps) & set(wanted)}
    kept = [o for o in new if o.type == "MESH" and o.as_pointer() in keep_ptr]
    dropped = [o for o in new if o not in kept]

    # Evaluate everything BEFORE changing anything (edits invalidate the depsgraph).
    dg = bpy.context.evaluated_depsgraph_get()
    world = {}
    evaluated_mesh = {}
    hier = {}
    n_constraints = 0
    for o in kept:
        ev = o.evaluated_get(dg)
        world[o.as_pointer()] = ev.matrix_world.copy()
        hier[o.as_pointer()] = _hierarchy_path(o, lib_names)
        n_constraints += len(o.constraints)
        if o.modifiers:
            evaluated_mesh[o.as_pointer()] = bpy.data.meshes.new_from_object(
                ev, preserve_all_data_layers=True, depsgraph=dg)

    mirrored = 0
    for o in kept:
        p = o.as_pointer()
        if p in evaluated_mesh:
            o.modifiers.clear()
            o.data = evaluated_mesh[p]
        elif o.data.users > 1:
            o.data = o.data.copy()
        o.constraints.clear()
        o.animation_data_clear()
        o.parent = None
        o.matrix_parent_inverse = Matrix.Identity(4)
        o.matrix_basis = Matrix.Identity(4)
        m = world[p]
        if m.to_3x3().determinant() < 0.0:
            mirrored += 1
        _apply_matrix(o, m)
        o.data.name = o.name
        o[SRC_NAME_PROP] = lib_names.get(p, o.name)
        o[SRC_PATH_PROP] = hier[p]
        o[SRC_FILE_PROP] = os.path.basename(path)

    dropped_rows = sorted((o.name, o.type) for o in dropped)
    for o in dropped:
        bpy.data.objects.remove(o, do_unlink=True)
    _purge_new_orphans(data_before)
    bpy.context.view_layer.update()

    kept.sort(key=lambda o: o.name)
    if report is not None:
        report.update({
            "path": path, "kind": ext[1:], "collection": coll.name,
            "loaded": len(new), "meshes": len(kept), "dropped": dropped_rows,
            "unhidden": unhidden, "constraints_baked": n_constraints,
            "modifiers_applied": len(evaluated_mesh), "mirrored": mirrored,
            "tris": sum(cs2_asset.tri_count(o) for o in kept),
        })
    print(f"[ingest] {os.path.basename(path)}: {len(kept)} meshes kept, {len(dropped)} "
          f"dropped, {unhidden} unhidden, {n_constraints} constraints baked, "
          f"{len(evaluated_mesh)} modifier stacks applied, {mirrored} mirrored")
    return kept


def _purge_new_orphans(before):
    """Remove datablocks created by this load that nothing uses any more."""
    for key in ("meshes", "curves", "actions", "materials"):
        coll = getattr(bpy.data, key)
        for block in sorted((b for b in coll if b.as_pointer() not in before[key]),
                            key=lambda b: b.name):
            if block.users == 0:
                coll.remove(block)


def discard(objs):
    """Delete ingested objects plus the meshes / materials they leave orphaned, and any
    CS2_INGEST_ collection they leave empty (lets one process load a source twice)."""
    objs = list(objs)
    meshes = {o.data for o in objs if o.type == "MESH"}
    mats = {m for me in meshes for m in me.materials if m is not None}
    colls = {c for o in objs for c in o.users_collection}
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)
    for me in sorted(meshes, key=lambda b: b.name):
        if me.users == 0:
            bpy.data.meshes.remove(me)
    for m in sorted(mats, key=lambda b: b.name):
        if m.users == 0:
            bpy.data.materials.remove(m)
    for c in sorted(colls, key=lambda b: b.name):
        if c.name.startswith(COLLECTION_PREFIX) and not c.all_objects:
            bpy.data.collections.remove(c)


# --------------------------------------------------------------------------------------
# Texture sets
# --------------------------------------------------------------------------------------

def _fold(s):
    """NFKD accent strip + casefold ("Barri<e-grave>re" -> "barriere")."""
    s = unicodedata.normalize("NFKD", s)
    return "".join(c for c in s if not unicodedata.combining(c)).casefold()


def _join_tokens(tokens):
    out = []
    for i, t in enumerate(tokens):
        if (t in CONJ_TOKENS and 0 < i < len(tokens) - 1
                and tokens[i - 1] in CONJ_CONTEXT and tokens[i + 1] in CONJ_CONTEXT):
            continue
        out.append(t)
    return "".join(out)


def material_key(name):
    """
    Normalised key of a material name: strip Blender's ".NNN" FIRST (a second load makes
    "si<e-grave>ge.001"), NFKD accent strip, casefold, split on [ _]+, drop "et" between
    haut/bas, join without separators, then NAME_ALIASES.
    "pil st haut bas 01" -> "pilsthautbas01"; "Fennetre Rectangle" -> "fenetrerect".
    """
    s = _fold(BLENDER_DUP_SUFFIX.sub("", name)).strip()
    key = _join_tokens([t for t in re.split(r"[ _]+", s) if t])
    return NAME_ALIASES.get(key, key)


def _texture_key(stem):
    """(set key, map kind) of a texture file stem, or None if no suffix matches."""
    s = re.sub(r"[ _]+", "_", _fold(stem)).strip("_")
    for kind, rx in MAP_SUFFIXES:
        m = rx.search(s)
        if m:
            prefix = s[:m.start()]
            return _join_tokens([t for t in prefix.split("_") if t]), kind
    return None


def scan_texture_sets(tex_dir):
    """
    Group the image files of `tex_dir` into {set key: {kind: path}}. Case-only prefix
    collisions (Guide_Cable / Guide_cable, Neige / neige, pil_Haut_01 / pil_haut_01)
    fold into ONE set; two files claiming the same (set, kind) raise ValueError.
    Returns (sets, ignored file names).
    """
    if not os.path.isdir(tex_dir):
        raise FileNotFoundError(tex_dir)
    sets, ignored = {}, []
    for fn in sorted(os.listdir(tex_dir)):
        stem, ext = os.path.splitext(fn)
        if ext.lower() not in TEX_EXTS:
            continue
        hit = _texture_key(stem)
        if hit is None:
            ignored.append(fn)
            continue
        key, kind = hit
        slot = sets.setdefault(key, {})
        if kind in slot:
            raise ValueError(f"texture set {key!r}: two {kind} maps "
                             f"{os.path.basename(slot[kind])!r} and {fn!r}")
        slot[kind] = os.path.join(tex_dir, fn)
    return sets, ignored


def _set_from_maps(key, maps):
    return SourceSet(base=maps.get("base"), normal=maps.get("normal"),
                     height=maps.get("height"), rough=maps.get("rough"), key=key)


def match_texture_sets(materials, tex_dir, overrides=None, keep=None):
    """
    Relink foreign materials to the texture sets in `tex_dir`.

    materials: Material datablocks or names (duplicates collapse).
    overrides: {material name or key: SourceSet | str}; a str names the texture set
               (file stem prefix, e.g. "fenetre_rect"), a SourceSet is used as given
               (e.g. a constant base colour for an untextured "cable").
    keep:      material names (or True = all) that MUST resolve to a base map; any that
               does not raises ValueError naming its key. None = only report.
    Returns ({material name: SourceSet}, sorted unmatched material names). Every returned
    set has a base map; a set found without one (normal-only) counts as unmatched.
    """
    sets, ignored = scan_texture_sets(tex_dir)
    names = sorted({m if isinstance(m, str) else m.name for m in materials})
    ovr = {}
    for k, v in (overrides or {}).items():
        ovr[material_key(k)] = v
    matched, unmatched, why = {}, [], {}
    for name in names:
        key = material_key(name)
        if key in ovr:
            v = ovr[key]
            if isinstance(v, SourceSet):
                ss = v
            else:
                tk = _join_tokens([t for t in re.sub(r"[ _]+", "_", _fold(str(v))).split("_")
                                   if t])
                if tk not in sets:
                    raise ValueError(f"override {name!r} -> {v!r}: no texture set {tk!r}")
                ss = _set_from_maps(tk, sets[tk])
        elif key in sets:
            ss = _set_from_maps(key, sets[key])
        else:
            unmatched.append(name)
            why[name] = f"no texture set {key!r}"
            continue
        if ss.base is None:
            unmatched.append(name)
            why[name] = f"set {ss.key or key!r} has no base map"
            continue
        matched[name] = ss
    if keep is not None:
        keep_keys = set() if keep is True else {material_key(k) for k in keep}
        kept = names if keep is True else [n for n in names if material_key(n) in keep_keys]
        unknown = sorted(keep_keys - {material_key(n) for n in names})
        if unknown:
            raise ValueError(f"keep names no material in the list: {unknown}")
        bad = [n for n in kept if n not in matched]
        if bad:
            raise ValueError("kept material(s) without a base map (no silent mid-grey): "
                             + "; ".join(f"{n!r}: {why.get(n, 'unknown')}" for n in bad)
                             + " - add an override or drop the faces")
    print(f"[ingest] textures {os.path.basename(os.path.normpath(tex_dir))}: "
          f"{len(matched)}/{len(names)} materials matched, {len(sets)} sets on disk, "
          f"unmatched {unmatched}, {len(ignored)} files ignored")
    return matched, unmatched


def load_map(path, size=None):
    """
    One texture as (h, w, 4) float32, Blender's bottom-up order, RAW stored values
    (colour space Non-Color: an sRGB albedo stays sRGB-encoded). Palette, grey and
    grey + alpha PNGs expand to RGBA (grey alpha = 1). `size` resamples to size x size.
    """
    img = bpy.data.images.load(path, check_existing=False)
    try:
        img.colorspace_settings.name = "Non-Color"
        w, h = img.size
        if w == 0 or h == 0:
            raise ValueError(f"load_map: unreadable image {path}")
        if size and (w, h) != (size, size):
            img.scale(size, size)
            w = h = size
        buf = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(buf)
    finally:
        bpy.data.images.remove(img)
    return buf.reshape(h, w, 4)


def used_materials(objs):
    """Materials actually used by faces of `objs` (not merely slotted), sorted by name."""
    out = {}
    for o in objs:
        me = o.data
        n = len(me.polygons)
        if not n or not me.materials:
            continue
        idx = np.empty(n, dtype=np.int32)
        me.polygons.foreach_get("material_index", idx)
        for i in sorted(set(idx.tolist())):
            if 0 <= i < len(me.materials) and me.materials[i] is not None:
                out[me.materials[i].name] = me.materials[i]
    return [out[k] for k in sorted(out)]


def _principled_of(mat):
    nt = mat.node_tree
    if nt is None:
        return None
    outs = [n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"]
    outs.sort(key=lambda n: (not n.is_active_output, n.name))
    for out in outs:
        links = out.inputs["Surface"].links
        if links and links[0].from_node.type == "BSDF_PRINCIPLED":
            return links[0].from_node
    return None


def colour_sets(materials):
    """
    {material name: SourceSet} for FLAT-COLOUR sources (gondola.fbx: 44 materials, no
    images): base = the Principled Base Color (linear RGB) or diffuse_color, rough /
    metal = the Principled constants.
    """
    out = {}
    for m in sorted(materials, key=lambda m: m.name):
        bsdf = _principled_of(m)
        if bsdf is not None and not bsdf.inputs["Base Color"].links:
            rgb = tuple(float(c) for c in bsdf.inputs["Base Color"].default_value[:3])
            rough = float(bsdf.inputs["Roughness"].default_value)
            metal = float(bsdf.inputs["Metallic"].default_value)
        else:
            rgb = tuple(float(c) for c in m.diffuse_color[:3])
            rough, metal = float(m.roughness), float(m.metallic)
        out[m.name] = SourceSet(base=rgb, rough=rough, metal=metal, key=material_key(m.name))
    return out


# --------------------------------------------------------------------------------------
# Scale and frame
# --------------------------------------------------------------------------------------

def rescale_by_reference(objs, ref, axis, metres):
    """
    Uniformly scale `objs` (about the world origin) so that `ref` spans `metres` along
    `axis`. `ref` is an Object, a list of Objects (their joint AABB) or a number (the
    current extent in source units). Transforms are baked first. Returns the factor.
    e.g. Kagelok rung pitch 1.00 u -> 0.30 m; gondola fence 0.0369 -> 1.00 m (x27).
    """
    ax = _axis_index(axis)
    if isinstance(ref, (int, float)):
        extent = float(ref)
    else:
        lo, hi = world_bounds(ref if not isinstance(ref, bpy.types.Object) else [ref])
        extent = hi[ax] - lo[ax]
    if extent <= 0.0 or metres <= 0.0:
        raise ValueError(f"rescale_by_reference: extent {extent} / metres {metres}")
    factor = float(metres) / extent
    s = Matrix.Scale(factor, 4)
    for o in sorted(objs, key=lambda o: o.name):
        _bake_world(o)
        _apply_matrix(o, s)
    return factor


def reframe(objs, forward, up, pivot="base", pivot_ref=None):
    """
    Rotate `objs` into the authoring frame - `forward` -> +X (travel), `up` -> +Z,
    lateral = up x forward -> +Y - and, with pivot="base", translate so the lowest point
    sits at z = 0 and the XY centre of `pivot_ref` (Object / list; default: all `objs`)
    sits on the Z axis: lift_chair's base pivot, grip on top (lift_chair.py:551-569).
    pivot=None rotates only. `forward` / `up` are "+X".."-Z" or vectors; `forward` is
    made orthogonal to `up`. Returns the 4x4 applied (reuse it on related objects).
    """
    f, u = _axis_vec(forward), _axis_vec(up)
    u.normalize()
    f = f - f.dot(u) * u
    if f.length < 1e-6:
        raise ValueError(f"reframe: forward {forward!r} is parallel to up {up!r}")
    f.normalize()
    lat = u.cross(f)
    rot = Matrix((f, lat, u)).to_4x4()
    objs = sorted(objs, key=lambda o: o.name)
    for o in objs:
        _bake_world(o)
    m = rot
    if pivot == "base":
        r = np.array(rot.to_3x3(), dtype=np.float64)
        pts = np.concatenate([_coords(o) @ r.T for o in objs])
        if pivot_ref is None:
            ref_pts = pts
        else:
            refs = [pivot_ref] if isinstance(pivot_ref, bpy.types.Object) else list(pivot_ref)
            ref_pts = np.concatenate([_coords(o) @ r.T for o in refs])
        cx = 0.5 * (ref_pts[:, 0].min() + ref_pts[:, 0].max())
        cy = 0.5 * (ref_pts[:, 1].min() + ref_pts[:, 1].max())
        m = Matrix.Translation((-cx, -cy, -pts[:, 2].min())) @ rot
    elif pivot is not None:
        raise ValueError(f"reframe: pivot {pivot!r} (expected 'base' or None)")
    for o in objs:
        _apply_matrix(o, m)
    return m


# --------------------------------------------------------------------------------------
# Parts
# --------------------------------------------------------------------------------------

def _components(me):
    """Connected vertex sets (through edges) that own at least one face, as sorted lists."""
    nv = len(me.vertices)
    parent = list(range(nv))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    ev = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", ev)
    for a, b in ev.reshape(-1, 2).tolist():
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[max(ra, rb)] = min(ra, rb)
    has_face = set()
    for p in me.polygons:
        has_face.add(find(p.vertices[0]))
    groups = {}
    for v in range(nv):
        r = find(v)
        if r in has_face:
            groups.setdefault(r, []).append(v)
    return [groups[r] for r in sorted(groups)]


def _check_prefix(prefix, what):
    if not isinstance(prefix, str) or not PART_PREFIX_RE.fullmatch(prefix):
        raise ValueError(f"to_parts: prefix {prefix!r} for {what} must match "
                         f"{PART_PREFIX_RE.pattern} (ASCII, lowercase)")
    return prefix.rstrip("_")


def to_parts(objs, rename, classify=None):
    """
    Split every object into its loose parts and name them for the part vocabulary:
    `rename` maps a SOURCE name (SRC_NAME_PROP, else the name without ".NNN") to an ASCII
    prefix such as "seat_" / "grip_"; parts become "<prefix>_00", "<prefix>_01", ...,
    numbered per prefix across all objects in canonical order (rounded centre, then
    size), so make_lod_chain delete prefixes and cs2_asset.tag_parts (which canonical-
    orders each part) see a stable list. Unmapped objects raise ValueError. The source
    objects are replaced (only once every part exists: an error - a bad classify prefix -
    removes the new parts and leaves the sources). Returns [(obj, None), ...] sorted by
    name - tag_parts' input.

    `classify(part, source_name, (lo, hi))` (optional) is called once per loose part
    (`part` = the new part object, lo / hi = its vertex AABB corners in the current
    units) and returns a prefix for THAT part or None for the object's `rename` prefix:
    small hardware inside one imported kit (Roues Pil Haut's sheaves inside the head)
    gets its own prefix, so a LOD level can delete it without the whole object.
    """
    for src, prefix in rename.items():
        _check_prefix(prefix, repr(src))
    objs = sorted(objs, key=lambda o: o.name)
    plan, unmapped = [], []
    for o in objs:
        base = rename.get(_source_name(o), rename.get(o.name))
        if base is None:
            unmapped.append(o.name)
        else:
            plan.append((o, base.rstrip("_")))
    if unmapped:
        raise ValueError(f"to_parts: no prefix for {unmapped}")

    groups, created = {}, []
    try:
        for o, base in plan:
            src_name = _source_name(o)
            _bake_world(o)
            me = o.data
            colls = list(o.users_collection)
            props = {k: o[k] for k in (SRC_NAME_PROP, SRC_PATH_PROP, SRC_FILE_PROP) if k in o}
            co = np.empty(len(me.vertices) * 3, dtype=np.float32)
            me.vertices.foreach_get("co", co)
            co = co.reshape(-1, 3).astype(np.float64)
            for comp in _components(me):
                keep = set(comp)
                part_me = me.copy()
                bm = bmesh.new()
                bm.from_mesh(part_me)
                bm.verts.ensure_lookup_table()
                bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keep],
                                 context="VERTS")
                bm.to_mesh(part_me)
                bm.free()
                part = bpy.data.objects.new(f"{base}_tmp", part_me)
                created.append(part)
                for c in colls:
                    c.objects.link(part)
                for k, v in props.items():
                    part[k] = v
                part["relief"] = "geo"      # cs2_kit contract: the relief IS the geometry
                pc = co[comp]
                lo, hi = pc.min(axis=0), pc.max(axis=0)
                centre = 0.5 * (lo + hi)
                sort_key = (tuple(round(float(c), 4) for c in centre), len(comp),
                            len(part_me.polygons))
                prefix = base
                if classify is not None:
                    hit = classify(part, src_name, (tuple(float(c) for c in lo),
                                                    tuple(float(c) for c in hi)))
                    if hit is not None:
                        prefix = _check_prefix(hit, f"a {src_name!r} part (classify)")
                groups.setdefault(prefix, []).append((sort_key, part))
    except BaseException:
        for part in created:
            pm = part.data
            bpy.data.objects.remove(part, do_unlink=True)
            if pm.users == 0:
                bpy.data.meshes.remove(pm)
        raise
    for o, _base in plan:
        old = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if old.users == 0:
            bpy.data.meshes.remove(old)

    parts = []
    for base in sorted(groups):
        for i, (_k, part) in enumerate(sorted(groups[base], key=lambda t: t[0])):
            part.name = f"{base}_{i:02d}"
            part.data.name = part.name
            parts.append((part, None))
    parts.sort(key=lambda t: t[0].name)
    print(f"[ingest] to_parts: {len(plan)} objects -> {len(parts)} parts "
          + ", ".join(f"{b} x{len(g)}" for b, g in sorted(groups.items())))
    return parts


# --------------------------------------------------------------------------------------
# Clean and reduce
# --------------------------------------------------------------------------------------

def _single_output(mat):
    """
    Keep ONE Material Output: the one fed by a Principled BSDF (else the active one,
    else the first by name), retargeted to ALL so EEVEE and the Cycles bake both see it.
    The 2.79 versioning tree has an EEVEE output (Principled) and a CYCLES output
    (Diffuse); a relink wired into the wrong one is invisible. Returns outputs removed.
    """
    nt = mat.node_tree
    if nt is None:
        return 0
    outs = sorted((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), key=lambda n: n.name)
    if not outs:
        return 0

    def feeds_principled(n):
        links = n.inputs["Surface"].links
        return bool(links) and links[0].from_node.type == "BSDF_PRINCIPLED"

    keep = (next((n for n in outs if feeds_principled(n)), None)
            or next((n for n in outs if n.is_active_output), None) or outs[0])
    for n in outs:
        if n != keep:
            nt.nodes.remove(n)
    keep.target = "ALL"
    keep.is_active_output = True
    return len(outs) - 1


def _drop_unused_slots(me):
    """Remove material slots no face uses (Kagelok chair "01" slots 31, uses 1)."""
    n = len(me.polygons)
    if not me.materials or not n:
        return 0
    idx = np.empty(n, dtype=np.int32)
    me.polygons.foreach_get("material_index", idx)
    used = sorted(set(idx.tolist()))
    if used == list(range(len(me.materials))):
        return 0
    mats = [me.materials[i] for i in used]
    remap = np.zeros(max(len(me.materials), max(used) + 1), dtype=np.int32)
    for new_i, old_i in enumerate(used):
        remap[old_i] = new_i
    removed = len(me.materials) - len(used)
    me.materials.clear()
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", remap[idx])
    me.update()
    return removed


def _face_components(bm):
    """Edge-connected face sets of a bmesh, each sorted, ordered by lowest face index."""
    bm.faces.index_update()
    parent = list(range(len(bm.faces)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for e in bm.edges:
        lf = [f.index for f in e.link_faces]
        for b in lf[1:]:
            ra, rb = find(lf[0]), find(b)
            if ra != rb:
                parent[max(ra, rb)] = min(ra, rb)
    groups = {}
    for f in bm.faces:
        groups.setdefault(find(f.index), []).append(f)
    return [groups[r] for r in sorted(groups)]


def _recalc_normals(bm, recalc):
    """
    Outward normals per edge-connected component. "manifold": only CLOSED 2-manifold
    components (every edge has exactly two faces), where outward is intrinsic and so
    pose-invariant; open shells and non-manifold components keep the source winding,
    because recalc_face_normals guesses there and the guess depends on the pose the mesh
    was baked in (P0a review). "all": every component (the old behaviour). "none": none.
    Returns (components, closed, recalculated, flipped, [(component, faces, flipped)]).
    """
    comps = _face_components(bm)
    closed = [all(len(e.link_faces) == 2 for f in c for e in f.edges) for c in comps]
    if recalc == "all":
        pick = list(range(len(comps)))
    elif recalc == "manifold":
        pick = [i for i, ok in enumerate(closed) if ok]
    else:
        pick = []
    before = {f.index: f.normal.copy() for i in pick for f in comps[i]}
    faces = [f for i in pick for f in comps[i]]
    if faces:
        bmesh.ops.recalc_face_normals(bm, faces=faces)
    rows = []
    for i in pick:
        k = sum(1 for f in comps[i] if f.normal.dot(before[f.index]) < 0.0)
        if k:
            rows.append((i, len(comps[i]), k))
    return len(comps), sum(closed), len(pick), sum(r[2] for r in rows), rows


def _clear_foreign_normals(me):
    """Drop imported custom split normals and sharp_edge marks (FOREIGN_NORMAL_ATTRS):
    export_fbx writes per-corner normals verbatim (cs2_asset.py:925-934) and
    shade_by_angle keeps existing sharp edges, so both would ship the source's shading
    (gondola.fbx: all 35 cabin meshes carry custom normals). Returns (cleared, sharp)."""
    cleared = me.attributes.get(FOREIGN_NORMAL_ATTRS[0]) is not None
    sharp = 0
    attr = me.attributes.get(FOREIGN_NORMAL_ATTRS[1])
    if attr is not None:
        vals = np.zeros(len(attr.data), dtype=bool)
        attr.data.foreach_get("value", vals)
        sharp = int(vals.sum())
    for name in FOREIGN_NORMAL_ATTRS:
        a = me.attributes.get(name)
        if a is not None:
            me.attributes.remove(a)
    if me.has_custom_normals:
        raise RuntimeError(f"clean({me.name}): custom normals survived removing "
                           f"{FOREIGN_NORMAL_ATTRS[0]!r}")
    return cleared, sharp


def clean(obj, merge_dist=MERGE_DIST, recalc=RECALC_DEFAULT):
    """
    Make a foreign mesh pipeline-legal, in place (distances in the CURRENT units):
    bake transforms; remove_doubles(merge_dist); delete loose edges and vertices;
    dissolve degenerate geometry and zero-area slivers; triangulate n-gons (BEAUTY);
    recalc outward normals per `recalc` (RECALC_MODES, module docstring: "manifold" =
    closed components only, the source winding elsewhere); clear foreign custom normals
    and sharp_edge marks; drop colour attributes (cs2_validate.py:587-589: the importer
    reads Color as window/emissive IDs); drop unused material slots; keep ONE Material
    Output per material. Returns a report dict; raises if n-gons remain.
    """
    if recalc not in RECALC_MODES:
        raise ValueError(f"clean: recalc {recalc!r} not in {RECALC_MODES}")
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    _bake_world(obj)
    me = obj.data
    rep = {"name": obj.name, "verts_in": len(me.vertices), "faces_in": len(me.polygons)}

    bm = bmesh.new()
    bm.from_mesh(me)
    n = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=merge_dist)
    rep["doubles"] = n - len(bm.verts)

    loose_e = [e for e in bm.edges if not e.link_faces]
    rep["loose_edges"] = len(loose_e)
    if loose_e:
        bmesh.ops.delete(bm, geom=loose_e, context="EDGES")
    loose_v = [v for v in bm.verts if not v.link_edges]
    rep["loose_verts"] = len(loose_v)
    if loose_v:
        bmesh.ops.delete(bm, geom=loose_v, context="VERTS")

    nf = len(bm.faces)
    bmesh.ops.dissolve_degenerate(bm, dist=merge_dist * DEGENERATE_FRACTION, edges=bm.edges[:])
    slivers = [f for f in bm.faces if f.calc_area() < SLIVER_AREA]
    if slivers:
        bmesh.ops.delete(bm, geom=slivers, context="FACES")
    stray = [v for v in bm.verts if not v.link_faces]
    if stray:
        bmesh.ops.delete(bm, geom=stray, context="VERTS")
    rep["degenerate_faces"] = nf - len(bm.faces)

    ngons = [f for f in bm.faces if len(f.verts) > 4]
    rep["ngons_triangulated"] = len(ngons)
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="BEAUTY")

    (rep["components"], rep["components_closed"], rep["components_recalculated"],
     rep["normals_flipped"], rep["flips_by_component"]) = _recalc_normals(bm, recalc)
    rep["recalc"] = recalc
    bm.to_mesh(me)
    bm.free()
    me.update()

    rep["custom_normals_cleared"], rep["sharp_edges_cleared"] = _clear_foreign_normals(me)
    rep["colour_attributes"] = sorted(a.name for a in me.color_attributes)
    for name in rep["colour_attributes"]:
        me.color_attributes.remove(me.color_attributes[name])
    rep["unused_slots"] = _drop_unused_slots(me)
    rep["outputs_removed"] = sum(_single_output(m) for m in me.materials if m is not None)

    rep["ngons"] = sum(1 for p in me.polygons if p.loop_total > 4)
    rep["verts"] = len(me.vertices)
    rep["faces"] = len(me.polygons)
    rep["tris"] = cs2_asset.tri_count(obj)
    if rep["ngons"]:
        raise RuntimeError(f"clean({obj.name}): {rep['ngons']} n-gons survived")
    print(f"[ingest] clean {obj.name}: {rep['doubles']} doubles, {rep['loose_edges']}+"
          f"{rep['loose_verts']} loose, {rep['degenerate_faces']} degenerate, "
          f"{rep['ngons_triangulated']} n-gons triangulated, {rep['normals_flipped']} "
          f"normals flipped in {len(rep['flips_by_component'])} of "
          f"{rep['components_recalculated']} recalculated components ({recalc}: "
          f"{rep['components_closed']}/{rep['components']} closed), custom normals "
          f"{'cleared' if rep['custom_normals_cleared'] else 'none'}, "
          f"{rep['sharp_edges_cleared']} sharp edges cleared, "
          f"{len(rep['colour_attributes'])} colour attrs, "
          f"{rep['unused_slots']} unused slots, {rep['outputs_removed']} outputs removed "
          f"-> {rep['tris']} tris")
    return rep


def reduce(obj, target_tris, delete_prefixes=(), tol=0.25):
    """
    Cut LOD0 itself down to `target_tris` with cs2_asset.make_lod_chain's own recipe
    (cs2_asset.py:1075-1133): delete the tagged parts whose names start with
    `delete_prefixes`, COLLAPSE-decimate the rest, dissolve degenerate faces and slivers
    and assert the result within `tol` of the target; then re-shade LOD0's way,
    cs2_asset.shade_by_angle(obj, SHADE_ANGLE_DEG, weighted=True) (make_lod_chain's own
    pass is unweighted, LOD style). `obj` must carry the part table (tag_parts -> join ->
    store_part_table). The mesh is replaced in place; the object, its name (mesh name =
    object name, no ".001") and its materials stay. Returns `obj`.
    """
    if cs2_asset.PART_TABLE_PROP not in obj.data:
        raise ValueError(f"reduce({obj.name}): no part table - tag_parts, join and "
                         f"store_part_table first")
    bpy.context.view_layer.update()
    tmp_name = f"{obj.name}Reduce"
    lod = cs2_asset.make_lod_chain(obj, tmp_name, [int(target_tris)],
                                   [tuple(delete_prefixes)], tol=tol)[0]
    old = obj.data
    new_me = lod.data
    bpy.data.objects.remove(lod, do_unlink=True)
    obj.data = new_me
    if old.users == 0:              # free the name first: mesh name = object name, no .001
        bpy.data.meshes.remove(old)
    new_me.name = obj.name
    cs2_asset.shade_by_angle(obj, SHADE_ANGLE_DEG, weighted=True)
    return obj
