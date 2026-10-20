"""
cs2_bake.py - multi-material unique bake, high-to-low bake and the tiling-material route
for the CS2 ski-resort art pipeline (asset round 2026-10, phase P0; ROUND-SPEC.md section 6).

WHY
---
The only bake in the tree is lift_tower.apply_finish_textures (lift_tower.py:848-1091): ONE
bake material that re-projects the asset's own four PNGs from "UVAtlas" onto a unique
layout. The 2026-10 round brings meshes with N source materials (the Kagelok chair and grip,
the gondola cabin shell: one texture set or flat color per material) and procedural
buildings that want tiling CC0 sets per material (the never-built art-pipeline-v2
section 3.3/3.4 route). This module is that finish pass generalized to N SOURCE MATERIALS,
plus a Cycles selected-to-active bake for a high-poly source, with the same output contract:
four PNGs (+ `_Emissive`), one UV layer, no materials, ready for cs2_asset.export_fbx.

Contract
--------
    SourceSet(base, normal=None, normal_convention="unknown", height=None, rough=None,
              metal=0.0, slot=0, recolor_albedo=None, emissive=None,
              uv=None, tint=1.0, mask=None, control=None, height_depth_m=0.004)
        One source material (pipeline-gap.md section 3.3). `base` is a path or LINEAR rgb;
        never None (no silent mid-gray). recolor_albedo None = auto: True when slot > 0
        and no `control` image (PIPELINE section 1: light 0.80 under every mask, because
        the tint multiplies; a slot set after match_texture_sets cannot forget the flag).
        An explicit False under a mask prints a WARN when the masked albedo averages below
        DARK_UNDER_MASK_SRGB. The last five fields are cs2_bake extensions:
        `uv` names the UV layer this material samples (default: the bake's source layer),
        `tint` multiplies the albedo in linear, `mask` / `control` take an existing CS2
        MaskMap (R metal, A smoothness) / ControlMask image so our own atlases are valid
        sources, `height_depth_m` is the relief of a 0..1 height swing.
        Any object (or dict) carrying these attribute names is accepted (duck-typed), so
        cs2_ingest may define or re-export its own class.

    bake_unique(obj, asset_dir, name, A, sources, *, reserved=(), margin_px=None,
                ao_samples=48, post_compose=None, emissive=False, high=None,
                cage_extrusion=0.03, max_ray=0.10, src_uv=None) -> report dict
        1. "UVUnique" via cs2_uv.pack_unique from the source layer (rotate=False, pinned
           reserved rects: the 1:1 strip restore and the finish-pass hooks rely on it). The
           source layer is renamed "UVAtlas" for the duration so finish-pass hooks
           (cs2_uv.density_report defaults) work unchanged.
        2. ONE bake material per source material: its own UVMap node -> image (or constant)
           -> Emission, and the shared target image node active in every material.
        3. Passes (Cycles CPU, seed 0, 1 sample): BaseColor (sRGB source -> linear),
           Normal (tangent NORMAL bake of NormalMap(source layer) into UVUnique; DX sources
           get G -> 1 - G first; "unknown" conventions are DROPPED and, when a height map
           exists, rebuilt by cs2_compose.height_to_normal on the unique layout), scalars
           (R metal, G rough, B height), ControlMask (R/G/B exclusive by slot, A 0),
           material ids (margin 0 and margin m), optional Emissive, then AO
           (`ao_samples`, self-occlusion with every other object hidden from render) or,
           with `high`, the selected-to-active normal + AO of bake_high_to_low.
        4. Linear-space compose: recolor slots (slot > 0 and recolor_albedo) are
           desaturated and luminance-normalized to cs2_asset.RECOLOR_ALBEDO (0.80 as
           written to the PNG) because the vanilla tint MULTIPLIES (PIPELINE.md section 1);
           height / high normals blended with cs2_compose.blend_rnm; reserved rects restored
           1:1 from A x A source images; AO multiplied (occ = ao*0.92 + 0.08); then
           `post_compose(base, nrm, msk, ctl, ao, mesh, A)` (finish-pass signature; call
           cs2_compose.compose_maps there with ao=None) or, without a hook,
           cs2_compose.compose_maps for the channel rules.
        5. sRGB encode, BaseColor / Emissive alpha 255 everywhere (any UV-covered alpha
           <= 128 turns the whole surface DefaultCutout), ControlMask A 0, MaskMap B 0;
           writes <name>_{BaseColor,Normal,MaskMap,ControlMask}.png (+ _Emissive).
        6. Removes every UV layer but "UVUnique" and every material slot; removes every
           datablock it created; restores every render / bake / Cycles / tool / color-
           management setting it touched, the selection, the active object and hide_render.
        Raises on any failure after restoring the mesh (UV layers, layer name, materials).
        `share=None` (detail pass 1, lifts): bay sharing, passed through to
        cs2_uv.pack_unique (see there); the report then carries "shared_faces". A stacked
        texel is baked once per copy, so it shows the LAST copy's source sample, AO and
        weathering (Cycles: the later face in index order wins an overlapping texel); only
        translation-identical hardware without world-anchored relief is ever shared
        (cs2_liftdress.share_identical). None = the pre-pass code path, byte-identical.

    bake_high_to_low(high, low, asset_dir, name, A, *, cage_extrusion=0.03, max_ray=0.10,
                     ao_samples=48, margin_px=None, out_dir=None, uv_layer="UVUnique")
        -> (normal_path, ao_path)
        Cycles selected-to-active into `low`'s existing `uv_layer`: tangent NORMAL in the
        OpenGL convention (+X, +Y, +Z swizzle = cs2_compose.height_to_normal's sign) and AO.
        PNGs go to `out_dir` (default <tempdir>/cs2_bake/<name>), never into asset_dir:
        cs2_validate errors on a stray PNG in an asset folder (cs2_validate.py:863).
        `ao_path` is None when ao_samples == 0. bake_unique(high=...) runs the same bake on
        its own unique layout and feeds both maps into its compose step.

    material_sources(spec_table, parts, *, layer="UVTile", texture_root=None)
        -> {mat_index: SourceSet}
        The art-pipeline-v2 section 3.3/3.4 route for PROCEDURAL buildings. `parts` is the
        generator's [(obj, key), ...] list BEFORE the join; `spec_table` maps key ->
        MaterialSpec (or a dict of its fields). Every part gets project_world_uv into
        `layer` at its spec's metres_per_tile and the full sorted material list (so the
        join keeps the indices: mat_index = position of the key in sorted(keys)). CC0 sets
        are resolved by find_set_map (ambientCG names) and FAIL LOUDLY when missing.

    project_world_uv(obj, metres_per_tile, kind="box", layer="UVTile") -> layer name
        World-anchored tiling UVs ("box" dominant-axis, "planar" face frame for roofs,
        "cyl" about world Z), never mirrored, in tiles (coordinate / metres_per_tile).
        Reads the CURRENT world matrix: a generator's `obj.location = ...` leaves
        matrix_world stale until the depsgraph runs, so the view layer is updated first
        (objects outside it: the parent chain is composed by hand).

    find_set_map(set_dir, map_name) -> path | None
        Strict ambientCG lookup: `<Set>_<n>K-<JPG|PNG>_<Map>.<ext>` or `<Set>_<Map>.<ext>`
        with <Set> == the folder name. cs2_asset._load_map's `*_<suffix>` glob silently
        matches `*_Albedo_Roughness` for `Roughness` (pipeline-gap-skeptic verdict 3).

Derived from (file:line)
------------------------
    finish pass: lift_tower.py:848-1091 (source layer renamed "UVAtlas" :906, bake
        material :966-980, Cycles CPU + margin :982-984, EMIT per map :986-1020, strip
        restore + linearise :1022-1049, AO floor + multiply :1050-1055, post_compose
        :1056-1060, sRGB encode + alpha 1 :1061-1069, write :1071-1074, drop UVAtlas +
        materials :1076-1078)
    pack: cs2_uv.py:381-403 (pack_unique), :68-70 (margin_px default), :237-253 (the
        unrestored use_uv_select_sync / mesh_select_mode this module restores)
    compose: cs2_compose.py:60-85 (height_to_normal, GL: G = 0.5 - k*dh/dv), :88-102
        (blend_rnm), :460-475 (rasterise_face_fields), :503-598 (compose_maps)
    recolor: cs2_asset.py:446 (RECOLOR_ALBEDO, PNG-encoded value), PIPELINE.md section 1
    writers / export: cs2_asset.py:283-293 (_write_png_raw), :1192-1218 (export_fbx and
        the __src rename: this module creates no Object or Mesh datablock and removes
        every Image / Material it makes, prefix CS2BAKE_)
    alpha: ref-models-skeptic.md verdict 9 (UV-covered alpha <= 128 -> DefaultCutout)
    set names: cs2_asset.py:262-280 (_load_map suffixes), pipeline-gap-skeptic.md verdict 3
    tiling route: art-pipeline-v2.md:181-193 (project_world_uv), :208-220 (MaterialSpec;
        NormalMap(UVTile) -> NORMAL bake with UVUnique active_render; bake.margin EXTEND
        per call; CPU + seed 0)
    live session: ROUND-SPEC.md R10, pipeline-gap.md section 6 (headless only)
    review fixes: p0a-integration.md "Fixes applied" (stale matrix_world, reserved-rect uv
        name, pre-existing UVUnique restore, recolor_albedo auto)

Headless only (raises when bpy.app.background is False). Deterministic: sorted iteration,
Cycles CPU, fixed seed, fixed sample counts, no wall-clock in the outputs. No top-level side
effects. Import as a sibling of cs2_asset.py.
"""

import math
import os
import re
import sys
import tempfile
import time
from dataclasses import dataclass, fields, replace

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset    # noqa: E402
import cs2_compose  # noqa: E402
import cs2_preview  # noqa: E402  (_srgb_to_linear / _linear_to_srgb, the shared transfer pair)
import cs2_uv       # noqa: E402


# --------------------------------------------------------------------------------------
# Constants
# --------------------------------------------------------------------------------------

BAKE_PREFIX = "CS2BAKE_"        # every Image / Material this module creates (all removed)
SRC_MAT_PREFIX = "CS2SRC_"      # material_sources placeholders (removed by bake_unique)
UNIQUE_LAYER = cs2_uv.UNIQUE_LAYER
ATLAS_LAYER = "UVAtlas"         # finish-pass name of the source layer (lift_tower.py:906)
TILE_LAYER = "UVTile"           # material_sources' tiling layer (art-pipeline-v2.md:220)

BAKE_SEED = 0                   # art-pipeline-v2.md:220 "CPU for the shipped bake; seed=0"
EMIT_SAMPLES = 1                # texture-space reprojection: one sample is exact
BAKE_MARGIN_TYPE = "EXTEND"     # art-pipeline-v2.md:220 (the 5.2 factory default is ADJACENT_FACES)
AO_FLOOR = 0.08                 # occ = ao * 0.92 + 0.08 (lift_tower.py:1054)
ROUGH_DEFAULT = 0.6             # cs2_asset.make_texture_set_atlas fallback without a map
HEIGHT_DEPTH_M = 0.004          # 0..1 height swing -> meters (DetailSpec bolt height)
ID_DIV = 256.0                  # material id emitted as (i + 1) / ID_DIV in a float target
LUMA = np.array((0.2126, 0.7152, 0.0722), dtype=np.float32)   # Rec.709, linear
SLOT_RGB = {0: (0.0, 0.0, 0.0), 1: (1.0, 0.0, 0.0), 2: (0.0, 1.0, 0.0), 3: (0.0, 0.0, 1.0)}
NORMAL_CONVENTIONS = ("GL", "DX", "unknown")
CTL_EXCLUSIVE_TOL = 1e-3        # art-pipeline-v2.md section 2: R + G + B <= 1 + 1e-3
DARK_UNDER_MASK_SRGB = 0.70     # WARN below this masked-albedo mean (PIPELINE sec 1 wants 0.80)

TEXTURE_ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                             os.pardir, "textures"))
SET_MAPS = ("Color", "NormalGL", "NormalDX", "Roughness", "Metalness", "Displacement",
            "AmbientOcclusion", "Opacity")
SET_EXTS = (".png", ".jpg", ".jpeg")
PROJECTIONS = ("box", "planar", "cyl")
CYL_CAP_NZ = 0.7                # cyl projection: faces steeper than this fall back to planar

# Scene state groups snapshotted and restored around every bake (dotted paths off Scene).
STATE_GROUPS = ("render", "render.bake", "cycles", "tool_settings", "display_settings",
                "view_settings")


# --------------------------------------------------------------------------------------
# Source description
# --------------------------------------------------------------------------------------

@dataclass
class SourceSet:
    """One source material (pipeline-gap.md section 3.3 plus the cs2_bake extensions)."""
    base: object = None                 # albedo path (sRGB) | linear (r, g, b) - never None
    normal: object = None               # tangent normal path
    normal_convention: str = "unknown"  # "GL" | "DX" | "unknown" (unknown -> dropped)
    height: object = None               # displacement path -> height_to_normal when no normal
    rough: object = None                # path | float | None (ROUGH_DEFAULT)
    metal: object = 0.0                 # path | float
    slot: int = 0                       # ControlMask 0 none, 1 R, 2 G, 3 B
    recolor_albedo: object = None       # neutral gray at RECOLOR_ALBEDO; None = slot > 0
    emissive: object = None             # path (sRGB) | linear (r, g, b) | None
    uv: object = None                   # UV layer to sample (None: the bake's source layer)
    tint: object = 1.0                  # linear multiply on the albedo (float or rgb)
    mask: object = None                 # CS2 MaskMap image (R metal, A smoothness)
    control: object = None              # CS2 ControlMask image (overrides slot)
    height_depth_m: float = HEIGHT_DEPTH_M


@dataclass
class MaterialSpec:
    """A procedural material for material_sources (art-pipeline-v2.md:208-220, trimmed)."""
    texture_set: object = None      # ambientCG set folder, or a set name under assets/textures
    metres_per_tile: float = 2.0
    tint: object = 1.0              # linear multiply on the set's Color (or on albedo)
    albedo: object = None           # linear (r, g, b) when there is no set (plain paint)
    metal: object = None            # None: the set's Metalness map, else 0.0
    rough: object = None            # None: the set's Roughness map, else ROUGH_DEFAULT
    slot: int = 0
    recolor_albedo: object = None   # None = auto (slot > 0), as SourceSet
    projection: str = "box"         # "box" | "planar" | "cyl"
    emissive: object = None


def _is_path(v):
    return isinstance(v, str)


def _rgb(v, what):
    try:
        t = tuple(float(x) for x in v)
    except TypeError as e:
        raise ValueError(f"{what}: expected a path or an (r, g, b) tuple, got {v!r}") from e
    if len(t) not in (3, 4):
        raise ValueError(f"{what}: expected 3 or 4 components, got {v!r}")
    return t[:3]


def _tint3(v, what):
    if isinstance(v, (int, float)):
        return (float(v),) * 3
    return _rgb(v, what)


def as_source_set(x, index="?"):
    """Coerce a SourceSet, a dict, or any object with SourceSet attribute names; validate."""
    if isinstance(x, SourceSet):
        s = x
    elif isinstance(x, dict):
        unknown = sorted(set(x) - {f.name for f in fields(SourceSet)})
        if unknown:
            raise ValueError(f"material {index}: unknown SourceSet fields {unknown}")
        s = SourceSet(**x)
    else:
        s = SourceSet(**{f.name: getattr(x, f.name) for f in fields(SourceSet)
                         if hasattr(x, f.name)})
    if s.metal is None:
        s = replace(s, metal=0.0)
    if s.tint is None:
        s = replace(s, tint=1.0)
    if s.recolor_albedo is None:        # auto: every mask ships light (PIPELINE section 1)
        s = replace(s, recolor_albedo=bool(s.slot in SLOT_RGB and s.slot > 0
                                           and s.control is None))
    what = f"material {index}"
    if s.base is None:
        raise ValueError(f"{what}: SourceSet.base is None - every kept material needs a base "
                         f"map or colour (no silent mid-grey, pipeline-gap.md section 3.3)")
    if s.normal_convention not in NORMAL_CONVENTIONS:
        raise ValueError(f"{what}: normal_convention {s.normal_convention!r} not in "
                         f"{NORMAL_CONVENTIONS}")
    if s.slot not in SLOT_RGB:
        raise ValueError(f"{what}: slot {s.slot!r} not in 0..3")
    if s.recolor_albedo and s.slot == 0:
        raise ValueError(f"{what}: recolor_albedo needs a recolour slot (slot > 0)")
    if s.mask is not None and (s.rough is not None or _is_path(s.metal) or float(s.metal) != 0.0):
        raise ValueError(f"{what}: give either `mask` or `rough`/`metal`, not both")
    for fname in ("base", "normal", "height", "rough", "metal", "emissive", "mask", "control"):
        v = getattr(s, fname)
        if _is_path(v):
            if not os.path.isfile(v):
                raise FileNotFoundError(f"{what}: SourceSet.{fname} file not found: {v}")
        elif fname in ("base", "emissive") and v is not None:
            _rgb(v, f"{what}.{fname}")
        elif fname in ("rough", "metal") and v is not None:
            float(v)
        elif fname in ("normal", "height", "mask", "control") and v is not None:
            raise ValueError(f"{what}: SourceSet.{fname} must be a path, got {v!r}")
    _tint3(s.tint, f"{what}.tint")
    return s


def _normal_route(s):
    """'GL' | 'DX' | 'height' | 'dropped' | 'none' for one SourceSet."""
    if s.normal is not None and s.normal_convention in ("GL", "DX"):
        return s.normal_convention
    if s.height is not None:
        return "height"
    return "dropped" if s.normal is not None else "none"


# --------------------------------------------------------------------------------------
# Scene state: snapshot / restore (the finish pass leaks these; lift_tower.py:929, 982-984)
# --------------------------------------------------------------------------------------

def _require_background():
    if not bpy.app.background:
        raise RuntimeError("cs2_bake runs headless only (blender --background): generator code "
                           "never runs in the live review session (ROUND-SPEC.md R10)")


def _resolve(scene, path):
    owner = scene
    for part in path.split("."):
        owner = getattr(owner, part, None)
        if owner is None:
            return None
    return owner


def rna_values(owner):
    """Writable scalar / enum / array RNA properties of `owner` as a plain dict."""
    out = {}
    for p in owner.bl_rna.properties:
        if p.identifier == "rna_type" or p.is_readonly or p.type in ("POINTER", "COLLECTION"):
            continue
        try:
            v = getattr(owner, p.identifier)
        except (AttributeError, RuntimeError, TypeError):
            continue
        if p.type == "ENUM" and p.is_enum_flag:
            v = frozenset(v)
        elif p.type in ("BOOLEAN", "INT", "FLOAT") and getattr(p, "array_length", 0):
            v = tuple(v)
        out[p.identifier] = v
    return out


def scene_settings(scene=None):
    """{group: rna_values} for STATE_GROUPS - what bake_* restore (and tests compare)."""
    scene = scene or bpy.context.scene
    return {g: rna_values(o) for g in STATE_GROUPS
            if (o := _resolve(scene, g)) is not None}


class _SceneState:
    """Everything a bake may change outside the baked mesh, restored on exit."""

    def __init__(self, scene):
        self.scene = scene
        self.settings = scene_settings(scene)
        vl = bpy.context.view_layer
        self.active = vl.objects.active
        self.selected = [o for o in vl.objects if o.select_get()]
        self.hide_render = {o.name: o.hide_render for o in scene.objects}
        obj = bpy.context.object
        self.mode = obj.mode if obj is not None else "OBJECT"

    def restore(self):
        failed = []
        obj = bpy.context.object
        if obj is not None and obj.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        for group, saved in self.settings.items():
            owner = _resolve(self.scene, group)
            if owner is None:
                continue
            now = rna_values(owner)
            for key, val in saved.items():
                if now.get(key) == val:
                    continue
                try:
                    setattr(owner, key, set(val) if isinstance(val, frozenset) else val)
                except (AttributeError, TypeError, ValueError, RuntimeError) as e:
                    failed.append(f"{group}.{key}: {e}")
        for o in self.scene.objects:
            if o.name in self.hide_render and o.hide_render != self.hide_render[o.name]:
                o.hide_render = self.hide_render[o.name]
        vl = bpy.context.view_layer
        for o in vl.objects:
            o.select_set(False)
        for o in self.selected:
            try:
                o.select_set(True)
            except (ReferenceError, RuntimeError):
                pass
        try:
            vl.objects.active = self.active
        except (ReferenceError, RuntimeError):
            vl.objects.active = None
        if self.mode != "OBJECT" and vl.objects.active is not None:
            try:
                bpy.ops.object.mode_set(mode=self.mode)
            except RuntimeError as e:
                failed.append(f"mode {self.mode}: {e}")
        if failed:
            print(f"[bake] WARN could not restore {len(failed)} setting(s): {failed[:6]}")
        return failed


def _setup_cycles(scene):
    """Cycles CPU with a fixed seed and no texture limits; restored by _SceneState."""
    scene.render.engine = "CYCLES"
    c = scene.cycles
    for key, val in (("device", "CPU"), ("seed", BAKE_SEED), ("use_animated_seed", False),
                     ("time_limit", 0.0), ("use_adaptive_sampling", False),
                     ("sample_offset", 0), ("use_sample_subset", False),
                     ("texture_limit_render", "OFF"), ("texture_resolution_render", 1.0)):
        if hasattr(c, key):
            setattr(c, key, val)


def _isolate(scene, keep):
    """Only `keep` renders: AO is self-occlusion, never the neighboring LOD or high-poly."""
    keep = {o.name for o in keep if o is not None}
    for o in scene.objects:
        want = o.name not in keep
        if o.hide_render != want:
            o.hide_render = want


def _object_mode():
    obj = bpy.context.object
    if obj is not None and obj.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")


def _check_size(A):
    A = int(A)
    if A < 8 or A & (A - 1):
        raise ValueError(f"texture size {A} is not a power of two >= 8")
    return A


# --------------------------------------------------------------------------------------
# Bake primitives
# --------------------------------------------------------------------------------------

def _new_target(tag, A, is_data):
    return bpy.data.images.new(f"{BAKE_PREFIX}{tag}", width=A, height=A, alpha=True,
                               float_buffer=True, is_data=is_data)


def _read_pixels(img, A):
    buf = np.empty(A * A * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    return buf.reshape(A, A, 4).copy()


def _bake(low, bake_type, samples, margin, *, high=None, cage_extrusion=0.0, max_ray=0.0,
          uv_layer=UNIQUE_LAYER):
    """One bpy.ops.object.bake call; every bake option passed explicitly (none read from
    the scene's bake settings, so the caller's settings never leak in)."""
    bpy.context.scene.cycles.samples = int(samples)
    bpy.ops.object.select_all(action="DESELECT")
    if high is not None:
        high.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.bake(
        type=bake_type, margin=int(margin), margin_type=BAKE_MARGIN_TYPE, use_clear=True,
        target="IMAGE_TEXTURES", save_mode="INTERNAL", use_split_materials=False,
        use_selected_to_active=high is not None, use_cage=False,
        cage_extrusion=float(cage_extrusion), max_ray_distance=float(max_ray),
        normal_space="TANGENT", normal_r="POS_X", normal_g="POS_Y", normal_b="POS_Z",
        uv_layer=uv_layer)


def _bake_into(low, set_target, tag, A, is_data, bake_type, samples, margin, **kw):
    target = _new_target(tag, A, is_data)
    try:
        set_target(target)
        _bake(low, bake_type, samples, margin, **kw)
        return _read_pixels(target, A)
    finally:
        set_target(None)
        bpy.data.images.remove(target)


def _activate_uv(mesh, layer):
    lay = mesh.uv_layers[layer]
    mesh.uv_layers.active = lay
    lay.active_render = True


def _swap_materials(mesh, mats):
    """Replace the mesh's material slots, keeping every polygon's material_index:
    Mesh.materials.clear() resets them all to 0 (5.2, measured by tests/test_bake.py)."""
    idx = np.empty(len(mesh.polygons), dtype=np.int32)
    mesh.polygons.foreach_get("material_index", idx)
    mesh.materials.clear()
    for m in mats:
        mesh.materials.append(m)
    mesh.polygons.foreach_set("material_index", idx)


class _TargetMaterials:
    """Temporary single-target materials on every slot of `obj` (bake_high_to_low alone)."""

    def __init__(self, obj, name):
        self.mesh = obj.data
        self.saved = list(self.mesh.materials)
        n_idx = np.empty(len(self.mesh.polygons), dtype=np.int32)
        self.mesh.polygons.foreach_get("material_index", n_idx)
        n_slots = max(len(self.saved), int(n_idx.max()) + 1 if len(n_idx) else 1, 1)
        self.mat = bpy.data.materials.new(f"{BAKE_PREFIX}{name}_target")
        if self.mat.node_tree is None:
            self.mat.use_nodes = True
        nt = self.mat.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        diff = nt.nodes.new("ShaderNodeBsdfDiffuse")
        nt.links.new(diff.outputs["BSDF"], out.inputs["Surface"])
        self.tgt = nt.nodes.new("ShaderNodeTexImage")
        nt.nodes.active = self.tgt
        _swap_materials(self.mesh, [self.mat] * n_slots)

    def set_target(self, img):
        self.tgt.image = img
        self.mat.node_tree.nodes.active = self.tgt

    def restore(self):
        _swap_materials(self.mesh, self.saved)
        bpy.data.materials.remove(self.mat)


class _BakeRig:
    """
    One bake material per material slot. Each holds the channel endpoints of its
    SourceSet (an output socket, or a constant linear rgb) and the shared target node;
    a pass relinks Emission.Color (EMIT) or the Diffuse BSDF (NORMAL) into the output.
    """

    def __init__(self, mesh, name, srcs, n_slots, layer_of, ext_of):
        self.mesh = mesh
        self.images = {}            # (abs path, colorspace) -> Image
        self.slots = []             # per slot: dict(mat, out, emit, diff, tgt, channels...)
        for i in range(n_slots):
            s = srcs.get(i)
            mat = bpy.data.materials.new(f"{BAKE_PREFIX}{name}_m{i:02d}")
            if mat.node_tree is None:
                mat.use_nodes = True
            self.slots.append(self._build(mat, i, s, layer_of, ext_of))
        _swap_materials(mesh, [slot["mat"] for slot in self.slots])

    # -- construction ---------------------------------------------------------------
    def _image(self, path, colorspace):
        key = (os.path.normcase(os.path.abspath(path)), colorspace)
        img = self.images.get(key)
        if img is None:
            img = bpy.data.images.load(path, check_existing=False)
            img.name = f"{BAKE_PREFIX}src{len(self.images):03d}"
            img.colorspace_settings.name = colorspace
            img.alpha_mode = "CHANNEL_PACKED"   # RGB never premultiplied by a packed alpha
            self.images[key] = img
        return img

    def _build(self, mat, i, s, layer_of, ext_of):
        nt = mat.node_tree
        nt.nodes.clear()
        N, L = nt.nodes, nt.links
        out = N.new("ShaderNodeOutputMaterial")
        emit = N.new("ShaderNodeEmission")
        emit.inputs["Strength"].default_value = 1.0
        diff = N.new("ShaderNodeBsdfDiffuse")
        tgt = N.new("ShaderNodeTexImage")
        slot = dict(mat=mat, out=out, emit=emit, diff=diff, tgt=tgt)
        N.active = tgt
        id_rgb = ((i + 1) / ID_DIV, 0.0, 0.0)
        if s is None:                       # an unused slot: inert, but carries the target
            slot.update(base=(0.0, 0.0, 0.0), scal=(0.0, ROUGH_DEFAULT, 0.0),
                        ctl=(0.0, 0.0, 0.0), emis=(0.0, 0.0, 0.0), id=id_rgb)
            return slot
        layer = layer_of(s)
        uvn = N.new("ShaderNodeUVMap")
        uvn.uv_map = layer

        def tex(path, colorspace):
            n = N.new("ShaderNodeTexImage")
            n.image = self._image(path, colorspace)
            n.interpolation = "Linear"
            n.extension = ext_of[layer]
            L.new(uvn.outputs["UV"], n.inputs["Vector"])
            return n

        def red(n):
            sep = N.new("ShaderNodeSeparateColor")
            L.new(n.outputs["Color"], sep.inputs["Color"])
            return sep.outputs["Red"]

        tint = _tint3(s.tint, f"material {i}.tint")
        # albedo (linear after the sRGB image node; constants are linear already)
        if _is_path(s.base):
            sock = tex(s.base, "sRGB").outputs["Color"]
            if tint != (1.0, 1.0, 1.0):
                vm = N.new("ShaderNodeVectorMath")
                vm.operation = "MULTIPLY"
                L.new(sock, vm.inputs[0])
                vm.inputs[1].default_value = tint
                sock = vm.outputs["Vector"]
            slot["base"] = sock
        else:
            slot["base"] = tuple(c * t for c, t in zip(_rgb(s.base, "base"), tint))
        # tangent normal -> NormalMap(source layer) -> Diffuse.Normal (NORMAL pass)
        route = _normal_route(s)
        if route in ("GL", "DX"):
            sock = tex(s.normal, "Non-Color").outputs["Color"]
            if route == "DX":                       # DirectX -> OpenGL: G' = 1 - G
                sep = N.new("ShaderNodeSeparateColor")
                L.new(sock, sep.inputs["Color"])
                inv = N.new("ShaderNodeMath")
                inv.operation = "SUBTRACT"
                inv.inputs[0].default_value = 1.0
                L.new(sep.outputs["Green"], inv.inputs[1])
                comb = N.new("ShaderNodeCombineColor")
                L.new(sep.outputs["Red"], comb.inputs["Red"])
                L.new(inv.outputs["Value"], comb.inputs["Green"])
                L.new(sep.outputs["Blue"], comb.inputs["Blue"])
                sock = comb.outputs["Color"]
            nm = N.new("ShaderNodeNormalMap")
            nm.space = "TANGENT"
            nm.uv_map = layer
            L.new(sock, nm.inputs["Color"])
            L.new(nm.outputs["Normal"], diff.inputs["Normal"])
        # scalars: R metal, G rough, B height (height only on the height route)
        comb = N.new("ShaderNodeCombineColor")
        if s.mask is not None:
            mk = tex(s.mask, "Non-Color")
            L.new(red(mk), comb.inputs["Red"])
            one_minus = N.new("ShaderNodeMath")
            one_minus.operation = "SUBTRACT"
            one_minus.inputs[0].default_value = 1.0
            L.new(mk.outputs["Alpha"], one_minus.inputs[1])
            L.new(one_minus.outputs["Value"], comb.inputs["Green"])
        else:
            if _is_path(s.metal):
                L.new(red(tex(s.metal, "Non-Color")), comb.inputs["Red"])
            else:
                comb.inputs["Red"].default_value = float(s.metal)
            if _is_path(s.rough):
                L.new(red(tex(s.rough, "Non-Color")), comb.inputs["Green"])
            else:
                comb.inputs["Green"].default_value = (ROUGH_DEFAULT if s.rough is None
                                                      else float(s.rough))
        if route == "height":
            L.new(red(tex(s.height, "Non-Color")), comb.inputs["Blue"])
        else:
            comb.inputs["Blue"].default_value = 0.0
        slot["scal"] = comb.outputs["Color"]
        # ControlMask
        slot["ctl"] = (tex(s.control, "Non-Color").outputs["Color"] if s.control is not None
                       else SLOT_RGB[s.slot])
        # Emissive
        if _is_path(s.emissive):
            slot["emis"] = tex(s.emissive, "sRGB").outputs["Color"]
        else:
            slot["emis"] = (_rgb(s.emissive, "emissive") if s.emissive is not None
                            else (0.0, 0.0, 0.0))
        slot["id"] = id_rgb
        return slot

    # -- passes ---------------------------------------------------------------------
    def set_target(self, img):
        for slot in self.slots:
            slot["tgt"].image = img
            slot["mat"].node_tree.nodes.active = slot["tgt"]

    def _unlink(self, nt, sockets):
        for link in list(nt.links):
            if link.to_socket in sockets:
                nt.links.remove(link)

    def emit_channel(self, channel):
        for slot in self.slots:
            nt = slot["mat"].node_tree
            emit, out = slot["emit"], slot["out"]
            self._unlink(nt, (emit.inputs["Color"], out.inputs["Surface"]))
            v = slot[channel]
            if isinstance(v, bpy.types.NodeSocket):
                nt.links.new(v, emit.inputs["Color"])
            else:
                emit.inputs["Color"].default_value = (v[0], v[1], v[2], 1.0)
            nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])

    def normal_surface(self):
        for slot in self.slots:
            nt = slot["mat"].node_tree
            self._unlink(nt, (slot["out"].inputs["Surface"],))
            nt.links.new(slot["diff"].outputs["BSDF"], slot["out"].inputs["Surface"])

    def remove(self):
        for slot in self.slots:
            try:
                bpy.data.materials.remove(slot["mat"])
            except (ReferenceError, RuntimeError):
                pass
        for img in self.images.values():
            try:
                bpy.data.images.remove(img)
            except (ReferenceError, RuntimeError):
                pass
        self.slots, self.images = [], {}


def _layer_extension(mesh, layer):
    """REPEAT for a tiling layer (coordinates outside 0..1), EXTEND for an atlas layer."""
    uv = cs2_uv.loop_uvs(mesh.uv_layers[layer])
    if len(uv) and (uv.min() < -1e-4 or uv.max() > 1.0 + 1e-4):
        return "REPEAT"
    return "EXTEND"


def _high_to_low_arrays(high, low, A, margin, cage_extrusion, max_ray, ao_samples,
                        set_target, uv_layer):
    """Selected-to-active NORMAL (+ AO) into `low`'s uv_layer; returns (nrm, ao | None)."""
    if high is low:
        raise ValueError("bake_high_to_low: high and low are the same object")
    if not isinstance(high.data, bpy.types.Mesh):
        raise TypeError(f"bake_high_to_low: {high.name} is not a mesh")
    nrm = _bake_into(low, set_target, "h2l_normal", A, True, "NORMAL", EMIT_SAMPLES, margin,
                     high=high, cage_extrusion=cage_extrusion, max_ray=max_ray,
                     uv_layer=uv_layer)
    ao = None
    if ao_samples > 0:
        ao = _bake_into(low, set_target, "h2l_ao", A, True, "AO", ao_samples, margin,
                        high=high, cage_extrusion=cage_extrusion, max_ray=max_ray,
                        uv_layer=uv_layer)
    return nrm, ao


def default_work_dir(name):
    """Where bake_high_to_low writes when no out_dir is given (outside every asset folder)."""
    return os.path.join(tempfile.gettempdir(), "cs2_bake", name)


# --------------------------------------------------------------------------------------
# bake_high_to_low
# --------------------------------------------------------------------------------------

def bake_high_to_low(high, low, asset_dir, name, A, *, cage_extrusion=0.03, max_ray=0.10,
                     ao_samples=48, margin_px=None, out_dir=None, uv_layer=UNIQUE_LAYER):
    """
    Cycles selected-to-active from `high` onto `low`'s existing `uv_layer` (module
    docstring). Returns (normal_path, ao_path); ao_path is None when ao_samples == 0.
    `asset_dir` only names the asset in the log: the intermediate PNGs never enter it.
    """
    _require_background()
    A = _check_size(A)
    me = low.data
    if uv_layer not in me.uv_layers:
        raise RuntimeError(f"{low.name}: no UV layer {uv_layer!r} to bake into - pack it "
                           f"first (cs2_uv.pack_unique) or use bake_unique(..., high=...)")
    margin = cs2_uv.default_margin_px(A) if margin_px is None else int(margin_px)
    out_dir = out_dir or default_work_dir(name)
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    state = _SceneState(scene)
    uv_saved = (me.uv_layers.active.name if me.uv_layers.active else None,
                next((l.name for l in me.uv_layers if l.active_render), None))
    tmp = None
    try:
        _object_mode()
        tmp = _TargetMaterials(low, name)
        _setup_cycles(scene)
        _isolate(scene, (low, high))
        _activate_uv(me, uv_layer)
        nrm, ao = _high_to_low_arrays(high, low, A, margin, cage_extrusion, max_ray,
                                      ao_samples, tmp.set_target, uv_layer)
        nrm[..., 3] = 1.0
        n_path = os.path.join(out_dir, f"{name}_HighNormal.png")
        cs2_asset._write_png_raw(n_path, f"{BAKE_PREFIX}{name}_HighNormal", nrm, A, True)
        a_path = None
        if ao is not None:
            ao[..., 3] = 1.0
            a_path = os.path.join(out_dir, f"{name}_HighAO.png")
            cs2_asset._write_png_raw(a_path, f"{BAKE_PREFIX}{name}_HighAO", ao, A, True)
        print(f"[bake] {name}: high-to-low {high.name} -> {low.name} @ {A} "
              f"(cage {cage_extrusion}, ray {max_ray}, ao {ao_samples}) -> {out_dir} "
              f"[asset {os.path.basename(os.path.normpath(asset_dir))}]")
        return n_path, a_path
    finally:
        if tmp is not None:
            tmp.restore()
        if uv_saved[0] in me.uv_layers:
            me.uv_layers.active = me.uv_layers[uv_saved[0]]
        if uv_saved[1] in me.uv_layers:
            me.uv_layers[uv_saved[1]].active_render = True
        state.restore()


# --------------------------------------------------------------------------------------
# bake_unique
# --------------------------------------------------------------------------------------

def _remove_spent_placeholders():
    """Drop material_sources placeholders no live mesh uses any more (the parts joined
    into the baked object leave orphan meshes behind that still list them)."""
    mats = [m for m in bpy.data.materials if m.name.startswith(SRC_MAT_PREFIX)]
    if not mats:
        return
    users = bpy.data.user_map(subset=mats)
    for m in mats:
        live = [u for u in users.get(m, ()) if not (isinstance(u, bpy.types.Mesh)
                                                     and u.users == 0)]
        if not live:
            bpy.data.materials.remove(m)


def _polygon_material_indices(mesh):
    idx = np.empty(len(mesh.polygons), dtype=np.int32)
    mesh.polygons.foreach_get("material_index", idx)
    return idx


def index_sources(mesh, sources):
    """
    ({mat_index: source}, [unused names]) from a dict keyed by material_index (int) or by
    material slot name (str, as cs2_ingest.match_texture_sets returns it). A name binds
    every slot holding that material; names no slot holds are returned, not raised (a
    match over a whole file covers more materials than one object uses). Two keys
    binding one slot raise.
    """
    names = [m.name if m is not None else None for m in mesh.materials]
    out, unused = {}, []
    for k in sorted(sources, key=lambda k: (1, k) if isinstance(k, str) else (0, int(k))):
        if isinstance(k, str):
            slots = [i for i, n in enumerate(names) if n == k]
            if not slots:
                unused.append(k)
                continue
        else:
            slots = [int(k)]
        for i in slots:
            if i in out:
                raise ValueError(f"{mesh.name}: material slot {i} bound twice "
                                 f"(key {k!r})")
            out[i] = sources[k]
    return out, sorted(unused)


def _pick_source_layer(mesh, src_uv):
    if src_uv is not None:
        if src_uv not in mesh.uv_layers:
            raise KeyError(f"{mesh.name}: no UV layer {src_uv!r} "
                           f"(have {[l.name for l in mesh.uv_layers]})")
        return src_uv
    act = mesh.uv_layers.active
    if act is not None and act.name != UNIQUE_LAYER:
        return act.name
    for lay in mesh.uv_layers:
        if lay.name != UNIQUE_LAYER:
            return lay.name
    raise RuntimeError(f"{mesh.name}: no source UV layer to bake from")


def _load_rgba(path, A, what):
    """Raw stored texels of an A x A image (bottom-up, display-encoded for sRGB files)."""
    img = bpy.data.images.load(path, check_existing=False)
    try:
        img.colorspace_settings.name = "Non-Color"
        img.alpha_mode = "CHANNEL_PACKED"
        if tuple(img.size) != (A, A):
            raise ValueError(f"{what}: reserved-rect restore needs an {A} x {A} source, "
                             f"{os.path.basename(path)} is {tuple(img.size)}")
        return _read_pixels(img, A)
    finally:
        bpy.data.images.remove(img)


def _restore_reserved(mesh, reserved, srcs, src_layer, A, base, nrm, msk, ctl, layer_of):
    """
    Copy each reserved rect 1:1 from the A x A source images of the material whose
    pinned faces sit in it (lift_tower.py:1022-1049 generalized). Constants bake exactly
    already and are left alone. `layer_of` is bake_unique's SourceSet -> sampled layer map
    (uv None or the source layer's ORIGINAL name both mean the packed source layer).
    Returns [(rect, material index, channels copied)].
    """
    done = []
    idx = _polygon_material_indices(mesh)
    for rect in reserved:
        pinned = cs2_uv.pinned_faces(mesh, mesh.uv_layers[UNIQUE_LAYER], [rect])
        mats = sorted({int(idx[f]) for f in pinned})
        if not mats:
            continue
        if len(mats) > 1:
            raise ValueError(f"reserved rect {rect} holds pinned faces of materials {mats}: "
                             f"one rect, one source material")
        m = mats[0]
        s = srcs[m]
        if layer_of(s) != src_layer:
            raise ValueError(f"reserved rect {rect}: material {m} samples {s.uv!r}, not the "
                             f"packed source layer - a 1:1 restore needs an aligned atlas")
        sl = cs2_compose._rect_slices(rect, A)
        what = f"reserved rect {rect} material {m}"
        copied = []
        if _is_path(s.base):
            px = _load_rgba(s.base, A, what)[sl]
            base[sl][..., 0:3] = (cs2_preview._srgb_to_linear(px[..., 0:3])
                                  * np.array(_tint3(s.tint, what), np.float32))
            copied.append("base")
        if _normal_route(s) in ("GL", "DX"):
            px = _load_rgba(s.normal, A, what)[sl]
            nrm[sl][..., 0:3] = px[..., 0:3]
            if s.normal_convention == "DX":
                nrm[sl][..., 1] = 1.0 - px[..., 1]
            copied.append("normal")
        if s.mask is not None:
            px = _load_rgba(s.mask, A, what)[sl]
            msk[sl][..., 0] = px[..., 0]
            msk[sl][..., 3] = px[..., 3]
            copied.append("mask")
        else:
            if _is_path(s.metal):
                msk[sl][..., 0] = _load_rgba(s.metal, A, what)[sl][..., 0]
                copied.append("metal")
            if _is_path(s.rough):
                msk[sl][..., 3] = 1.0 - _load_rgba(s.rough, A, what)[sl][..., 0]
                copied.append("rough")
        if s.control is not None:
            ctl[sl][..., 0:3] = _load_rgba(s.control, A, what)[sl][..., 0:3]
            copied.append("control")
        done.append((tuple(rect), m, copied))
    return done


def bake_unique(obj, asset_dir, name, A, sources, *, reserved=(), margin_px=None,
                ao_samples=48, post_compose=None, emissive=False, high=None,
                cage_extrusion=0.03, max_ray=0.10, src_uv=None, share=None):
    """
    Bake N source materials onto a unique layout and write the CS2 texture set (module
    docstring). `sources` maps polygon material_index (int) or material slot NAME (str,
    e.g. cs2_ingest.match_texture_sets output) -> SourceSet; every index a face uses must
    resolve. `share` = bay sharing for cs2_uv.pack_unique (None: not passed on at all).
    Returns the report dict; raises on failure after restoring the mesh and the
    scene.
    """
    pack_kw = {} if share is None else {"share": share}
    _require_background()
    cs2_asset.validate_asset_name(name)
    A = _check_size(A)
    if not isinstance(obj.data, bpy.types.Mesh):
        raise TypeError(f"{obj.name} is not a mesh")
    if any(slot.link == "OBJECT" for slot in obj.material_slots):
        raise ValueError(f"{obj.name}: object-linked material slots are not supported "
                         f"(link every slot to the mesh data)")
    mesh = obj.data
    by_index, unused_names = index_sources(mesh, sources)
    srcs = {i: as_source_set(v, i) for i, v in sorted(by_index.items())}
    used = sorted(set(int(i) for i in _polygon_material_indices(mesh)))
    missing = [i for i in used if i not in srcs]
    if missing:
        raise KeyError(f"{obj.name}: material indices {missing} are used by faces but have "
                       f"no SourceSet")
    n_slots = max(len(mesh.materials), (used[-1] + 1) if used else 1, 1)
    src_orig = _pick_source_layer(mesh, src_uv)
    for i, s in srcs.items():
        if s.uv is not None and s.uv not in mesh.uv_layers:
            raise KeyError(f"material {i}: UV layer {s.uv!r} not on {obj.name} "
                           f"(have {[l.name for l in mesh.uv_layers]})")
    os.makedirs(asset_dir, exist_ok=True)
    reserved = [tuple(float(x) for x in r) for r in (reserved or ())]

    scene = bpy.context.scene
    state = _SceneState(scene)
    saved_materials = list(mesh.materials)
    saved_active_uv = mesh.uv_layers.active.name if mesh.uv_layers.active else None
    renamed = src_orig != ATLAS_LAYER and ATLAS_LAYER not in mesh.uv_layers
    src_layer = ATLAS_LAYER if renamed else src_orig
    # pack_unique deletes and rebuilds an existing UVUnique: keep its loops for the
    # failure path (a UVUnique that IS the source layer is renamed, not rebuilt)
    had_unique = UNIQUE_LAYER in mesh.uv_layers and src_orig != UNIQUE_LAYER
    saved_unique = cs2_uv.loop_uvs(mesh.uv_layers[UNIQUE_LAYER]).copy() if had_unique else None
    rig = None
    t0 = time.perf_counter()
    seconds = {}
    try:
        _object_mode()
        if renamed:
            mesh.uv_layers[src_orig].name = ATLAS_LAYER

        def layer_of(s):
            if s.uv is None or s.uv == src_orig:
                return src_layer
            return s.uv

        layers = sorted({layer_of(s) for s in srcs.values()} | {src_layer})
        ext_of = {lay: _layer_extension(mesh, lay) for lay in layers}

        # -- 1. unique layout ---------------------------------------------------------
        mesh.uv_layers.active = mesh.uv_layers[src_layer]
        pack = cs2_uv.pack_unique(obj, A, reserved, margin_px=margin_px, **pack_kw)
        margin = int(pack["margin_px"])
        _activate_uv(mesh, UNIQUE_LAYER)
        seconds["pack"] = time.perf_counter() - t0

        # -- 2. bake materials ----------------------------------------------------------
        rig = _BakeRig(mesh, name, srcs, n_slots, layer_of, ext_of)
        _setup_cycles(scene)
        _isolate(scene, (obj, high))

        # -- 3. passes ------------------------------------------------------------------
        t1 = time.perf_counter()

        def emit(channel, is_data, m=margin):
            rig.emit_channel(channel)
            return _bake_into(obj, rig.set_target, channel, A, is_data, "EMIT",
                              EMIT_SAMPLES, m)

        base = emit("base", False)
        rig.normal_surface()
        nrm = _bake_into(obj, rig.set_target, "normal", A, True, "NORMAL", EMIT_SAMPLES,
                         margin)
        scal = emit("scal", True)
        ctl = emit("ctl", True)
        id0 = emit("id", True, 0)
        idm = emit("id", True)
        emis = emit("emis", False) if emissive else None
        seconds["emit"] = time.perf_counter() - t1
        t1 = time.perf_counter()
        hi_nrm = ao = None
        if high is not None:
            hi_nrm, ao = _high_to_low_arrays(high, obj, A, margin, cage_extrusion, max_ray,
                                             ao_samples, rig.set_target, UNIQUE_LAYER)
        elif ao_samples > 0:
            ao = _bake_into(obj, rig.set_target, "ao", A, True, "AO", ao_samples, margin)
        seconds["ao"] = time.perf_counter() - t1

        # -- 4. compose (linear) ------------------------------------------------------
        ids0 = np.rint(id0[..., 0] * ID_DIV).astype(np.int32) - 1    # -1 = no face
        idsm = np.rint(idm[..., 0] * ID_DIV).astype(np.int32) - 1    # margins included
        msk = np.zeros((A, A, 4), dtype=np.float32)
        msk[..., 0] = scal[..., 0]
        msk[..., 3] = 1.0 - scal[..., 1]
        ctl[..., 3] = 0.0
        per_mat = {}
        faces = np.bincount(_polygon_material_indices(mesh), minlength=n_slots)
        recolour_lin = float(cs2_preview._srgb_to_linear(np.float32(cs2_asset.RECOLOR_ALBEDO)))
        in_rect = np.zeros((A, A), dtype=bool)       # stamped strips: restored 1:1 below,
        for r in reserved:                           # so they never steer the recolor mean
            in_rect[cs2_compose._rect_slices(r, A)] = True
        for i, s in srcs.items():
            cov = ids0 == i
            info = {"faces": int(faces[i]) if i < len(faces) else 0,
                    "texels": int(cov.sum()), "slot": s.slot, "normal": _normal_route(s),
                    "recolor_scale": None}
            if cov.any() and (s.recolor_albedo or (s.slot > 0 and s.control is None)):
                lum = base[..., 0:3] @ LUMA
                stat = cov & ~in_rect if (cov & ~in_rect).any() else cov
                mean = float(lum[stat].mean())
                if s.recolor_albedo:
                    if mean <= 1e-6:
                        raise ValueError(f"material {i}: black albedo cannot be recoloured")
                    k = recolour_lin / mean
                    sel = idsm == i
                    base[sel, 0:3] = np.clip(lum[sel] * k, 0.0, 1.0)[:, None]
                    info["recolor_scale"] = k
                else:                       # explicit recolor_albedo=False under a mask
                    srgb = float(cs2_preview._linear_to_srgb(np.float32(mean)))
                    info["mask_albedo_srgb"] = round(srgb, 3)
                    if srgb < DARK_UNDER_MASK_SRGB:
                        print(f"[bake] WARN material {i}: slot {s.slot} with recolor_albedo="
                              f"False ships its own albedo (mean {srgb:.2f} sRGB < "
                              f"{DARK_UNDER_MASK_SRGB}) under the mask - the tint multiplies "
                              f"(PIPELINE section 1: {cs2_asset.RECOLOR_ALBEDO})")
            if info["normal"] == "dropped":
                print(f"[bake] WARN material {i}: normal map dropped (convention unknown, "
                      f"no height map to rebuild it from)")
            per_mat[i] = info
        h_route = [i for i, s in srcs.items() if _normal_route(s) == "height"]
        if h_route:
            depth = np.zeros((A, A), dtype=np.float32)
            for i in h_route:
                depth[idsm == i] = float(srcs[i].height_depth_m)
            _parts, ppm = cs2_compose.rasterise_face_fields(mesh, A, UNIQUE_LAYER)
            det = cs2_compose.height_to_normal(scal[..., 2] * depth, ppm)
            nrm[..., 0:3] = cs2_compose.blend_rnm(nrm[..., 0:3], det)
        if hi_nrm is not None:
            nrm[..., 0:3] = cs2_compose.blend_rnm(hi_nrm[..., 0:3], nrm[..., 0:3])
        restored = _restore_reserved(mesh, reserved, srcs, src_layer, A, base, nrm, msk, ctl,
                                     layer_of)
        if ao is not None:
            occ = np.clip(ao[..., 0:1] * (1.0 - AO_FLOOR) + AO_FLOOR, 0.0, 1.0)
            base[..., 0:3] *= occ
        if post_compose is not None:
            base, nrm, msk, ctl = post_compose(base, nrm, msk, ctl, ao, mesh, A)
        else:
            base, nrm, msk, ctl = cs2_compose.compose_maps(
                base, nrm, msk, ctl, None, None, 0.0, {"reserved": reserved, "gloss_var": 0.0})

        # -- 5. channel invariants, encode, write --------------------------------------
        base = np.clip(np.asarray(base, dtype=np.float32), 0.0, 1.0)
        nrm = np.clip(np.asarray(nrm, dtype=np.float32), 0.0, 1.0)
        msk = np.clip(np.asarray(msk, dtype=np.float32), 0.0, 1.0)
        ctl = np.clip(np.asarray(ctl, dtype=np.float32), 0.0, 1.0)
        base[..., 3] = 1.0
        nrm[..., 3] = 1.0
        msk[..., 2] = 0.0
        ctl[..., 3] = 0.0
        over = ctl[..., 0:3].sum(axis=-1) > 1.0 + CTL_EXCLUSIVE_TOL
        if over.any():
            raise RuntimeError(f"{name}: ControlMask R/G/B not exclusive on {int(over.sum())} "
                               f"texels (art-pipeline-v2.md section 2)")
        base[..., 0:3] = cs2_preview._linear_to_srgb(base[..., 0:3])
        maps = [(base, "BaseColor", False), (nrm, "Normal", True), (msk, "MaskMap", True),
                (ctl, "ControlMask", True)]
        if emis is not None:
            emis = np.clip(emis, 0.0, 1.0)
            emis[..., 0:3] = cs2_preview._linear_to_srgb(emis[..., 0:3])
            emis[..., 3] = 1.0
            maps.append((emis, "Emissive", False))
        files = []
        for arr, suffix, is_data in maps:
            path = os.path.join(asset_dir, f"{name}_{suffix}.png")
            cs2_asset._write_png_raw(path, f"{name}_{suffix}", arr, A, is_data)
            files.append(os.path.basename(path))

        # -- 6. ship one UV layer, no materials ---------------------------------------
        for lay_name in sorted(l.name for l in mesh.uv_layers if l.name != UNIQUE_LAYER):
            mesh.uv_layers.remove(mesh.uv_layers[lay_name])
        _activate_uv(mesh, UNIQUE_LAYER)
        mesh.materials.clear()
        rig.remove()
        rig = None
        _remove_spent_placeholders()
        seconds["total"] = time.perf_counter() - t0
        report = {
            "name": name, "A": A, "margin_px": margin, "src_uv": src_orig,
            "unused_source_names": unused_names,
            "layers_sampled": sorted({layer_of(s) for s in srcs.values()}),
            "pack": pack, "materials": per_mat, "files": files,
            "ao": "high" if high is not None and ao is not None else ("self" if ao is not None
                                                                       else None),
            "height_route": h_route, "reserved_restored": restored,
            "uncovered_texels": int((ids0 < 0).sum()), "seconds": seconds,
        }
        if share is not None:                   # the key exists only when sharing was asked
            report["shared_faces"] = int(pack.get("shared_faces", 0))
        print(f"[bake] {name}: {len(srcs)} source material(s) -> unique {A} px, margin "
              f"{margin}, AO {report['ao']}, {report['uncovered_texels']} gutter texels, "
              f"{seconds['total']:.1f} s; wrote {', '.join(files)}")
        return report
    except Exception:
        try:
            _object_mode()
        except RuntimeError:
            pass
        if rig is not None:
            _swap_materials(mesh, saved_materials)
            rig.remove()
        if not had_unique and UNIQUE_LAYER in mesh.uv_layers:
            mesh.uv_layers.remove(mesh.uv_layers[UNIQUE_LAYER])
        elif had_unique:                    # the caller's UVUnique, loops as they were
            lay = mesh.uv_layers.get(UNIQUE_LAYER)
            if lay is None:
                lay = mesh.uv_layers.new(name=UNIQUE_LAYER, do_init=False)
            if len(lay.data) == len(saved_unique):
                cs2_uv.set_loop_uvs(lay, saved_unique)
        if renamed and ATLAS_LAYER in mesh.uv_layers:
            mesh.uv_layers[ATLAS_LAYER].name = src_orig
        if list(mesh.materials) != saved_materials:
            _swap_materials(mesh, saved_materials)
        if saved_active_uv in mesh.uv_layers:
            mesh.uv_layers.active = mesh.uv_layers[saved_active_uv]
        raise
    finally:
        state.restore()


# --------------------------------------------------------------------------------------
# Tiling-material route for procedural buildings
# --------------------------------------------------------------------------------------

def _set_dir(texture_set, texture_root=None):
    root = texture_root or TEXTURE_ROOT
    if os.path.isabs(texture_set) or os.sep in texture_set or "/" in texture_set:
        return os.path.normpath(texture_set)
    return os.path.join(root, texture_set)


def find_set_map(set_dir, map_name):
    """
    Path of one map of an ambientCG-style set, or None. Strict: the file stem must be
    `<Set>_<Map>` or `<Set>_<n>K-<JPG|PNG>_<Map>` with <Set> == the folder name (case-
    insensitive) and <Map> exactly `map_name`. Several resolutions -> the largest, PNG
    before JPG, then by name.
    """
    if map_name not in SET_MAPS:
        raise ValueError(f"unknown set map {map_name!r} (one of {SET_MAPS})")
    set_name = os.path.basename(os.path.normpath(set_dir)).casefold()
    pat = re.compile(r"(?:(\d+)K-(JPG|PNG)_)?" + re.escape(map_name))
    hits = []
    for fname in sorted(os.listdir(set_dir)):
        stem, ext = os.path.splitext(fname)
        if ext.lower() not in SET_EXTS or "_" not in stem:
            continue
        if not stem.casefold().startswith(set_name + "_"):
            continue
        m = pat.fullmatch(stem[len(set_name) + 1:])
        if m is None:
            continue
        res = int(m.group(1)) if m.group(1) else 0
        hits.append((-res, ext.lower() != ".png", fname))
    if not hits:
        return None
    return os.path.join(set_dir, sorted(hits)[0][2])


def _as_material_spec(x, key):
    if isinstance(x, MaterialSpec):
        spec = x
    elif isinstance(x, dict):
        unknown = sorted(set(x) - {f.name for f in fields(MaterialSpec)})
        if unknown:
            raise ValueError(f"material {key!r}: unknown MaterialSpec fields {unknown}")
        spec = MaterialSpec(**x)
    else:
        raise TypeError(f"material {key!r}: expected MaterialSpec or dict, got {type(x)}")
    if spec.projection not in PROJECTIONS:
        raise ValueError(f"material {key!r}: projection {spec.projection!r} not in {PROJECTIONS}")
    if not spec.metres_per_tile or spec.metres_per_tile <= 0:
        raise ValueError(f"material {key!r}: metres_per_tile must be > 0")
    if (spec.texture_set is None) == (spec.albedo is None):
        raise ValueError(f"material {key!r}: give exactly one of texture_set / albedo")
    return spec


def _resolve_spec(spec, key, texture_root=None):
    """MaterialSpec -> SourceSet (uv filled in by the caller). Fails loudly on a missing set."""
    if spec.texture_set is None:
        return as_source_set(SourceSet(
            base=_rgb(spec.albedo, f"{key}.albedo"),
            rough=ROUGH_DEFAULT if spec.rough is None else float(spec.rough),
            metal=0.0 if spec.metal is None else float(spec.metal),
            slot=spec.slot, recolor_albedo=spec.recolor_albedo, emissive=spec.emissive,
            tint=spec.tint), key)
    d = _set_dir(spec.texture_set, texture_root)
    if not os.path.isdir(d):
        raise FileNotFoundError(
            f"material {key!r}: texture set {spec.texture_set!r} not found at {d} - CC0 sets "
            f"must be downloaded into assets/textures first (ROUND-SPEC.md Q6; only Concrete, "
            f"Metal and PaintedMetal sets ship today)")
    color = find_set_map(d, "Color")
    if color is None:
        raise FileNotFoundError(f"material {key!r}: set {d} has no *_Color map "
                                f"(no silent mid-grey)")
    n_gl, n_dx = find_set_map(d, "NormalGL"), find_set_map(d, "NormalDX")
    normal, conv = (n_gl, "GL") if n_gl else ((n_dx, "DX") if n_dx else (None, "unknown"))
    rough = spec.rough if spec.rough is not None else (find_set_map(d, "Roughness")
                                                       or ROUGH_DEFAULT)
    metal = spec.metal if spec.metal is not None else (find_set_map(d, "Metalness") or 0.0)
    height = find_set_map(d, "Displacement") if normal is None else None
    return as_source_set(SourceSet(
        base=color, normal=normal, normal_convention=conv, height=height, rough=rough,
        metal=metal, slot=spec.slot, recolor_albedo=spec.recolor_albedo,
        emissive=spec.emissive, tint=spec.tint), key)


def _placeholder(key):
    nm = f"{SRC_MAT_PREFIX}{key}"
    mat = bpy.data.materials.get(nm)
    return mat if mat is not None else bpy.data.materials.new(nm)


def _compose_world(obj):
    """World matrix from matrix_basis and the parent chain (never stale; no constraints)."""
    if obj.parent is None:
        return obj.matrix_basis.copy()
    return _compose_world(obj.parent) @ obj.matrix_parent_inverse @ obj.matrix_basis


def _current_world(obj):
    """obj's world matrix NOW. `obj.location = ...` leaves matrix_world stale until the
    depsgraph runs (5.2.2, measured: 10 tiles off at 1 m/tile, P0a review), so update the
    view layer first; an object outside it is never evaluated - compose by hand."""
    vl = bpy.context.view_layer
    if vl.objects.get(obj.name) == obj:
        vl.update()
        return obj.matrix_world.copy()
    return _compose_world(obj)


def project_world_uv(obj, metres_per_tile, kind="box", layer=TILE_LAYER):
    """
    World-anchored tiling UVs in tiles (art-pipeline-v2.md:181-193), read through the
    CURRENT world matrix (_current_world; the object's transform is not applied or changed):
      box     dominant world axis per face; (u, v) frames chosen so u x v = +axis and u is
              negated on negative faces - no mirrored islands; walls get v = world Z.
      planar  per-face plane frame: u horizontal in the face, v up the slope (roofs: no
              1/cos stretch, rows run along the eaves).
      cyl     angle about world Z through the part's bbox center x mean radius, v = z,
              per-face unwrap about the face's own center angle; caps (|n.z| > 0.7) planar.
    Creates `layer` if needed without changing the active layer (unless it is the first).
    """
    if kind not in PROJECTIONS:
        raise ValueError(f"project_world_uv: kind {kind!r} not in {PROJECTIONS}")
    mpt = float(metres_per_tile)
    if mpt <= 0:
        raise ValueError("project_world_uv: metres_per_tile must be > 0")
    me = obj.data
    nv, npoly, nloop = len(me.vertices), len(me.polygons), len(me.loops)
    lay = me.uv_layers.get(layer)
    if lay is None:
        active = me.uv_layers.active.name if me.uv_layers.active else None
        lay = me.uv_layers.new(name=layer, do_init=False)
        if active is not None:
            me.uv_layers.active = me.uv_layers[active]
    if npoly == 0:
        return layer
    M = np.array(_current_world(obj), dtype=np.float64)
    R, t = M[:3, :3], M[:3, 3]
    Nm = np.linalg.inv(R).T
    co = np.empty(nv * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    W = co.reshape(nv, 3).astype(np.float64) @ R.T + t
    pn = np.empty(npoly * 3, dtype=np.float32)
    me.polygons.foreach_get("normal", pn)
    pn = pn.reshape(npoly, 3).astype(np.float64) @ Nm.T
    pn /= np.maximum(np.linalg.norm(pn, axis=1, keepdims=True), 1e-12)
    start = np.empty(npoly, dtype=np.int32)
    total = np.empty(npoly, dtype=np.int32)
    me.polygons.foreach_get("loop_start", start)
    me.polygons.foreach_get("loop_total", total)
    lp = np.repeat(np.arange(npoly), total)          # loop -> polygon (loops are contiguous)
    order = np.concatenate([np.arange(s, s + c) for s, c in zip(start, total)])
    lv = np.empty(nloop, dtype=np.int32)
    me.loops.foreach_get("vertex_index", lv)
    P = W[lv[order]]
    uv = np.zeros((nloop, 2), dtype=np.float64)

    def planar_frames(n):
        up = np.array((0.0, 0.0, 1.0))
        horiz = np.abs(n[:, 2]) < 0.999
        ud = np.cross(np.broadcast_to(up, n.shape), n)
        ud_len = np.linalg.norm(ud, axis=1, keepdims=True)
        flat_u = np.zeros_like(n)
        flat_u[:, 0] = np.where(n[:, 2] >= 0.0, 1.0, -1.0)
        ud = np.where(horiz[:, None], ud / np.maximum(ud_len, 1e-12), flat_u)
        vd = np.cross(n, ud)
        return ud, vd

    if kind == "box":
        n = pn[lp]
        ax = np.argmax(np.abs(n), axis=1)
        sg = np.where(n[np.arange(len(n)), ax] >= 0.0, 1.0, -1.0)
        u = np.where(ax == 0, P[:, 1], np.where(ax == 1, -P[:, 0], P[:, 0])) * sg
        v = np.where(ax == 2, P[:, 1], P[:, 2])
        uv[order] = np.stack([u, v], axis=1)
    else:
        ud, vd = planar_frames(pn)
        u = np.einsum("ij,ij->i", P, ud[lp])
        v = np.einsum("ij,ij->i", P, vd[lp])
        if kind == "cyl":
            c = (W.min(axis=0) + W.max(axis=0)) * 0.5
            rad = float(np.mean(np.linalg.norm(W[:, :2] - c[:2], axis=1))) or 1.0
            cent = np.zeros((npoly, 3))
            np.add.at(cent, lp, P)
            cent /= total[:, None]
            theta_c = np.arctan2(cent[:, 1] - c[1], cent[:, 0] - c[0])[lp]
            theta = np.arctan2(P[:, 1] - c[1], P[:, 0] - c[0])
            theta = theta_c + np.mod(theta - theta_c + math.pi, 2.0 * math.pi) - math.pi
            side = np.abs(pn[lp][:, 2]) <= CYL_CAP_NZ
            u = np.where(side, theta * rad, u)
            v = np.where(side, P[:, 2], v)
        uv[order] = np.stack([u, v], axis=1)
    cs2_uv.set_loop_uvs(lay, uv / mpt)
    return layer


def material_sources(spec_table, parts, *, layer=TILE_LAYER, texture_root=None):
    """
    Tiling sources for a procedural build (module docstring). Resolves every spec first
    (a missing set raises before any mesh is touched), then projects each part into
    `layer` and gives it the full sorted placeholder material list. Returns
    {mat_index: SourceSet} with uv = layer, mat_index = sorted(keys).index(key).
    """
    bpy.context.view_layer.update()     # generator .location edits: no stale matrix_world
    keys = sorted({str(key) for _obj, key in parts})
    missing = [k for k in keys if k not in spec_table]
    if missing:
        raise KeyError(f"material_sources: parts use keys {missing} that spec_table lacks")
    specs = {k: _as_material_spec(spec_table[k], k) for k in keys}
    resolved = {k: _resolve_spec(specs[k], k, texture_root) for k in keys}
    mats = [_placeholder(k) for k in keys]
    layer_lists = {}
    for obj, key in parts:
        key = str(key)
        spec = specs[key]
        project_world_uv(obj, spec.metres_per_tile, spec.projection, layer)
        me = obj.data
        me.materials.clear()
        for m in mats:
            me.materials.append(m)
        me.polygons.foreach_set("material_index", [keys.index(key)] * len(me.polygons))
        layer_lists.setdefault(tuple(l.name for l in me.uv_layers), []).append(obj.name)
    if len(layer_lists) > 1:
        raise ValueError(f"material_sources: parts carry different UV layer lists, the join "
                         f"would mix them: {sorted(layer_lists)}")
    out = {i: replace(resolved[k], uv=layer) for i, k in enumerate(keys)}
    print(f"[bake] material_sources: {len(parts)} part(s), {len(keys)} material(s) "
          f"{keys} -> layer {layer!r}")
    return out
