"""
ski_probes.py - four tiny CS2 probe assets that convert unverified engine rows into
decisions before any hero asset depends on them (art-pipeline-v2.md section 7, Round L
spec "ski_probes.py"). Each probe is a normal importable asset folder:

    SkiProbeLodA   2 m sphere-ish object with THREE color-coded LOD levels
                   LOD0 = 400 tris, UVs in the RED band of the main 512 set
                   LOD1 = 120 tris, UVs in the GREEN band (shares LOD0's set via sharedAssets)
                   LOD2 =  24 tris, DEDICATED 512 set that is uniformly BLUE; its UVs sit in
                          the band that is MAGENTA in LOD0's set, so in game: blue at
                          distance = dedicated set bound, magenta = fell back to LOD0's set,
                          gray = no texture bound. settings.json is written by this script
                          with identity entries for LOD2 (ExampleBakedAlphaFence pattern).
                   Proves: lod_factor 652 / 45 deg fov switch distances (pops expected at
                   ~20/40/80/160 m), and dedicated_texture_lods binding.
    SkiProbeGlsA   1 m bevelled box + a `SkiProbeGlsA_Gls` pane 0.02 m thick in front (-Y),
                   LOD1 for both (`_LOD1.fbx` + `_LOD1_Gls.fbx`). The pane ships NO textures.
                   Proves: `_Gls` binds DefaultGlassDoubleSided with zero textures, meshCount 2.
    SkiProbeCoatA  2 x 2 x 0.2 m slab, same albedo everywhere, MaskMap A = 0.5 everywhere,
                   MaskMap G = 0 on the -X half and 0.25 on the +X half (small painted "0" /
                   "25" labels in the far corner of each half, plus a 2-texel seam line).
                   Proves: whether MaskMap G (coat) reaches the shader at all.
    SkiProbeFndA   two 3 x 3 x 1 m boxes on ONE asset: box "A" (at -X) is OPEN-BOTTOM with its
                   side-face bottom edges exactly at z = 0 -> engine base expected; box "B"
                   (at +X) is closed with a 0.5 m modeled skirt welded below z = 0 (the K
                   station pattern) -> no engine base expected. "A"/"B" painted on the tops.
                   Proves: BatchMeshHelpers.AddBaseLines behavior (spec "engine facts").
                   MUST be imported with the Building preset: MeshFlags.Base is only set for
                   a building object without BaseProperties (ObjectInitializeSystem.cs:1426-1430).

Contract: every probe ships 512 x 512 maps, shade_by_angle(66) normals, settings.json, and one
preview PNG. Nothing here touches the repo import tree unless --out points there. Run:

    blender --background --factory-startup --python ski_probes.py -- --out <dir> [--preview <dir>]

The script re-imports every FBX it wrote (names, x100 Y-up extents, custom normals) and prints a
one-line PASS summary, exiting 1 on any failure. No top-level side effects.
"""

import argparse
import json
import math
import os
import sys

import bpy
import bmesh
import mathutils
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset            # noqa: E402  (shared library; never edited here)
import lift_tower           # noqa: E402  (pick_engine / frame_camera only)

TEX = 512
MAPS = ("BaseColor", "Normal", "MaskMap", "ControlMask")

# 5 x 7 bitmap glyphs (row 0 = top). Enough for the painted labels used here.
GLYPHS = {
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "B": ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    "0": ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
    "2": ["01110", "10001", "00001", "00110", "01000", "10000", "11111"],
    "5": ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
}


# --------------------------------------------------------------------------------------
# Scene / geometry helpers
# --------------------------------------------------------------------------------------

def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)
    for img in list(bpy.data.images):
        if img.users == 0:
            bpy.data.images.remove(img)


def _link(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.uv_layers.new(name="UVMap")
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    return obj


def add_box(name, size, loc, open_bottom=False):
    """Axis-aligned box; `open_bottom` deletes the -Z face (engine-base rule)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts[:])
    bmesh.ops.translate(bm, vec=loc, verts=bm.verts[:])
    if open_bottom:
        bm.faces.ensure_lookup_table()
        kill = [f for f in bm.faces if f.normal.z < -0.9]
        bmesh.ops.delete(bm, geom=kill, context="FACES")
    return _link(name, bm)


def add_sphere(name, segments, rings, radius, loc):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings,
                                         radius=radius, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    return obj


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)
    bm.to_mesh(obj.data)
    bm.free()
    return obj


def apply_transform(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


# --------------------------------------------------------------------------------------
# UV helpers
# --------------------------------------------------------------------------------------

def project_faces(obj, rect_for_face, bounds_for_face=None):
    """
    Planar-project every face along its dominant normal axis (extents normalized to 0..1
    over the object bbox, or over `bounds_for_face(poly)` -> (lo, hi) when given) and
    squeeze it into the rect returned by `rect_for_face(poly)`. Faces sharing a rect
    overlap - fine for a flat-color probe main mesh (the legacy atlas practice).
    """
    me = obj.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    co = np.array([v.co[:] for v in me.vertices])
    lo_all, hi_all = co.min(axis=0), co.max(axis=0)
    for poly in me.polygons:
        n = poly.normal
        k = max(range(3), key=lambda i: abs(n[i]))
        i, j = [a for a in range(3) if a != k]
        lo, hi = (lo_all, hi_all) if bounds_for_face is None else bounds_for_face(poly)
        lo, hi = np.asarray(lo, dtype=float), np.asarray(hi, dtype=float)
        span = np.maximum(hi - lo, 1e-6)
        u0, v0, u1, v1 = rect_for_face(poly)
        for li in poly.loop_indices:
            p = me.vertices[me.loops[li].vertex_index].co
            a = (p[i] - lo[i]) / span[i]
            b = (p[j] - lo[j]) / span[j]
            uv[li].uv = (u0 + a * (u1 - u0), v0 + b * (v1 - v0))


def remap_uv(obj, rect):
    """Affine-map the existing 0..1 UVs of `obj` into `rect` (LOD color bands)."""
    u0, v0, u1, v1 = rect
    for l in obj.data.uv_layers.active.data:
        u, v = l.uv
        l.uv = (u0 + min(max(u, 0.0), 1.0) * (u1 - u0), v0 + min(max(v, 0.0), 1.0) * (v1 - v0))


def unwrap_unique(obj, margin_px=8):
    """Sub-mesh rule: smart_project + one pack, non-overlapping islands (plan 3.8)."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.uv.pack_islands(rotate=False, scale=True, margin_method="FRACTION",
                            margin=margin_px / float(TEX), shape_method="AABB")
    bpy.ops.object.mode_set(mode="OBJECT")


# --------------------------------------------------------------------------------------
# Texture helpers (bottom-up buffers, written raw: row 0 == v 0)
# --------------------------------------------------------------------------------------

def paint_text(buf, text, x0, y0, cell, rgb, gap=1):
    """Paint `text` with the 5x7 font; (x0, y0) = bottom-left texel, `cell` px per bit."""
    x = x0
    for ch in text:
        g = GLYPHS[ch]
        for r, row in enumerate(g):
            yy = y0 + (6 - r) * cell
            for c, bit in enumerate(row):
                if bit == "1":
                    xx = x + c * cell
                    buf[yy:yy + cell, xx:xx + cell, :3] = rgb
        x += (5 + gap) * cell


def flat_rgba(rgb, a=1.0, size=TEX):
    out = np.empty((size, size, 4), dtype=np.float32)
    out[:, :, :3] = rgb
    out[:, :, 3] = a
    return out


def write_set(out_dir, name, base, msk=None, smooth=0.35, seed=3, grain=0.02):
    """
    Write the four maps for `name` from a bottom-up BaseColor buffer (+ optional MaskMap).
    Normal flat GL (0.5, 0.5, 1); MaskMap R 0 / G 0 / B 0 / A smooth unless given;
    ControlMask all zero (no recolor slot - the probes must show their painted albedo).
    """
    os.makedirs(out_dir, exist_ok=True)
    A = base.shape[0]
    rng = np.random.default_rng(seed)
    b = base.copy()
    b[:, :, :3] = np.clip(b[:, :, :3] + rng.normal(0.0, grain, (A, A, 1)).astype(np.float32), 0, 1)
    b[:, :, 3] = 1.0                                  # BaseColor alpha 255 everywhere
    cs2_asset._write_png_raw(os.path.join(out_dir, f"{name}_BaseColor.png"),
                             f"{name}_BaseColor", b, A, is_data=False)
    nrm = flat_rgba((0.5, 0.5, 1.0), size=A)
    cs2_asset._write_png_raw(os.path.join(out_dir, f"{name}_Normal.png"),
                             f"{name}_Normal", nrm, A, is_data=True)
    if msk is None:
        msk = flat_rgba((0.0, 0.0, 0.0), a=smooth, size=A)
    cs2_asset._write_png_raw(os.path.join(out_dir, f"{name}_MaskMap.png"),
                             f"{name}_MaskMap", msk, A, is_data=True)
    ctl = flat_rgba((0.0, 0.0, 0.0), a=0.0, size=A)
    cs2_asset._write_png_raw(os.path.join(out_dir, f"{name}_ControlMask.png"),
                             f"{name}_ControlMask", ctl, A, is_data=True)
    return [f"{name}_{m}.png" for m in MAPS]


# --------------------------------------------------------------------------------------
# Preview
# --------------------------------------------------------------------------------------

def preview_material(obj, tex_dir, name, coat_from_mask=False, glass=False):
    mat = bpy.data.materials.new(name + "Preview")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    if glass:
        # EEVEE headless has no raytraced transmission: an alpha-blended tinted pane
        # stands in (this is preview-only; the shipped _Gls carries no material at all).
        bsdf.inputs["Alpha"].default_value = 0.35
        bsdf.inputs["Roughness"].default_value = 0.05
        bsdf.inputs["Base Color"].default_value = (0.55, 0.75, 0.9, 1.0)
        if hasattr(mat, "surface_render_method"):
            mat.surface_render_method = "BLENDED"
    else:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(os.path.join(tex_dir, f"{name}_BaseColor.png"))
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.85     # matte: painted labels stay legible
        if coat_from_mask:
            msk = nt.nodes.new("ShaderNodeTexImage")
            msk.image = bpy.data.images.load(os.path.join(tex_dir, f"{name}_MaskMap.png"))
            msk.image.colorspace_settings.name = "Non-Color"
            sep = nt.nodes.new("ShaderNodeSeparateColor")
            nt.links.new(msk.outputs["Color"], sep.inputs["Color"])
            nt.links.new(sep.outputs["Green"], bsdf.inputs["Coat Weight"])
            bsdf.inputs["Roughness"].default_value = 0.5
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def render_preview(objs, out_path, direction=(1.0, -1.2, 0.55), res=(1200, 900)):
    """One framed view of `objs` (dark world, key + fill suns) -> PNG. Materials stay on."""
    scene = bpy.context.scene
    scene.render.engine = lift_tower.pick_engine()
    scene.render.image_settings.file_format = "PNG"
    scene.render.resolution_x, scene.render.resolution_y = res
    world = bpy.data.worlds.new("ProbeWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.11, 0.13, 0.16, 1.0)
    scene.world = world
    bpy.ops.object.camera_add(location=(0, 0, 0))
    cam = bpy.context.active_object
    cam.data.lens = 50
    scene.camera = cam
    bpy.ops.object.light_add(type="SUN", location=(10, -12, 20))
    key = bpy.context.active_object
    key.data.energy = 2.5      # below the tower/terminal 4.0: these albedos are light, not black steel
    key.rotation_euler = (math.radians(50), 0, math.radians(35))
    bpy.ops.object.light_add(type="SUN", location=(-12, 8, 12))
    fill = bpy.context.active_object
    fill.data.energy = 1.0
    fill.rotation_euler = (math.radians(65), 0, math.radians(215))

    # frame the union of all objects with a temporary joined bbox proxy
    los, his = [], []
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ mathutils.Vector(c)
            los.append(w)
            his.append(w)
    lo = mathutils.Vector((min(v.x for v in los), min(v.y for v in los), min(v.z for v in los)))
    hi = mathutils.Vector((max(v.x for v in his), max(v.y for v in his), max(v.z for v in his)))
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(lo + hi) / 2)
    proxy = bpy.context.active_object
    proxy.scale = (hi - lo)
    proxy.hide_render = True
    bpy.context.view_layer.update()          # frame_camera reads matrix_world: sync the scale
    lift_tower.frame_camera(cam, proxy, mathutils.Vector(direction), res, margin=1.15)
    scene.view_settings.view_transform = "AgX"   # keep the 4.0 key sun from clipping albedo
    scene.render.filepath = out_path
    try:
        bpy.ops.render.render(write_still=True)
    except Exception as exc:                       # EEVEE unavailable headless -> Cycles CPU
        print(f"[probes] {scene.render.engine} failed ({exc}); falling back to Cycles")
        scene.render.engine = "CYCLES"
        scene.cycles.device = "CPU"
        scene.cycles.samples = 16
        bpy.ops.render.render(write_still=True)
    for o in (cam, key, fill, proxy):
        bpy.data.objects.remove(o, do_unlink=True)
    return out_path


# --------------------------------------------------------------------------------------
# Probe builders. Each returns (asset_dir, [exported fbx stems], [(obj, tex_dir, tex_name)])
# --------------------------------------------------------------------------------------

def build_lod_probe(out_root, name="SkiProbeLodA"):
    cs2_asset.validate_asset_name(name)
    asset_dir = os.path.join(out_root, name)
    os.makedirs(asset_dir, exist_ok=True)

    # main 512 set: three v-bands  red (LOD0) / green (LOD1) / magenta (LOD2 fallback marker)
    base = flat_rgba((0.8, 0.15, 0.15))
    base[TEX // 3:2 * TEX // 3, :, :3] = (0.15, 0.7, 0.2)
    base[2 * TEX // 3:, :, :3] = (0.8, 0.1, 0.8)
    write_set(asset_dir, name, base, smooth=0.35)
    # dedicated LOD2 set: uniformly blue, no grain
    cs2_asset.make_texture_set(asset_dir, f"{name}_LOD2", size=TEX, base_rgb=(0.1, 0.25, 0.9),
                               metallic=0.0, smoothness=0.35, noise=0.0, control_rgb=(0, 0, 0))

    bands = {0: (0.02, 0.02, 0.98, 0.31), 1: (0.02, 0.35, 0.98, 0.64), 2: (0.02, 0.69, 0.98, 0.98)}
    specs = {0: (20, 11, 400), 1: (10, 7, 120), 2: (6, 3, 24)}
    objs = []
    for level, (segs, rings, expect) in specs.items():
        lname = name if level == 0 else f"{name}_LOD{level}"
        o = add_sphere(lname, segs, rings, 1.0, (0.0, 0.0, 1.0))
        apply_transform(o)
        remap_uv(o, bands[level])
        cs2_asset.shade_by_angle(o, 66.0, weighted=False)
        n = cs2_asset.tri_count(o)
        if n != expect:
            raise RuntimeError(f"{lname}: {n} tris, expected {expect}")
        print(f"[probes] {lname}: {n} tris, UV band {bands[level]}")
        objs.append(o)

    # settings.json by hand: LOD1 shares LOD0's set, LOD2 has IDENTITY entries (its own
    # files exist beside the FBX - the ExampleBakedAlphaFence pattern). Replace with
    # cs2_asset.write_settings(dedicated_texture_lods=(2,)) once the integrator adds it.
    shared = {}
    for m in ("BaseColor", "ControlMask", "MaskMap", "Normal"):
        shared[f"{name}_LOD1_{m}.png"] = f"{name}_{m}.png"
    for m in ("BaseColor", "ControlMask", "MaskMap", "Normal"):
        shared[f"{name}_LOD2_{m}.png"] = f"{name}_LOD2_{m}.png"
    settings = {"sharedAssets": shared, "LOD1": {"mode": "Skip"}, "LOD2": {"mode": "Skip"}}
    with open(os.path.join(asset_dir, "settings.json"), "w", encoding="utf-8") as fh:
        json.dump(settings, fh, indent=2)

    previews = [(objs[0], asset_dir, name), (objs[1], asset_dir, name),
                (objs[2], asset_dir, f"{name}_LOD2")]
    stems = [name, f"{name}_LOD1", f"{name}_LOD2"]
    return asset_dir, stems, previews, objs


def build_gls_probe(out_root, name="SkiProbeGlsA"):
    cs2_asset.validate_asset_name(name)
    asset_dir = os.path.join(out_root, name)
    os.makedirs(asset_dir, exist_ok=True)
    cs2_asset.make_texture_set(asset_dir, name, size=TEX, base_rgb=(0.55, 0.50, 0.42),
                               smoothness=0.30, noise=0.03, control_rgb=(0, 0, 0))

    # LOD0: bevelled 1 m box (so LOD1 = the plain 12-tri box is < 50 %)
    box = add_box(name, (1.0, 1.0, 1.0), (0.0, 0.0, 0.5))
    bpy.ops.object.select_all(action="DESELECT")
    box.select_set(True)
    bpy.context.view_layer.objects.active = box
    bev = box.modifiers.new("Bevel", type="BEVEL")
    bev.width = 0.05
    bev.segments = 1
    bpy.ops.object.modifier_apply(modifier=bev.name)
    project_faces(box, lambda p: (0.05, 0.05, 0.95, 0.95))
    cs2_asset.shade_by_angle(box, 66.0, weighted=True)

    lod1 = add_box(f"{name}_LOD1", (1.0, 1.0, 1.0), (0.0, 0.0, 0.5))
    project_faces(lod1, lambda p: (0.05, 0.05, 0.95, 0.95))
    cs2_asset.shade_by_angle(lod1, 66.0, weighted=False)

    # the pane: 0.02 m thick, 0.8 x 0.8, standing 0.1 m in front of the -Y face, no material,
    # unique non-overlapping UVs, identity transform (same as the parent).
    pane = add_box(f"{name}_Gls", (0.8, 0.02, 0.8), (0.0, -0.61, 0.5))
    unwrap_unique(pane)
    cs2_asset.shade_by_angle(pane, 66.0, weighted=False)
    # The same pane again as the LOD1 glass (review F5): vanilla ExampleColorMaskFirehouse
    # ships _LOD1_Gls beside _Gls, and a LOD1 without its pane would lose the glass at
    # the first switch.
    pane_lod1 = pane.copy()
    pane_lod1.data = pane.data.copy()
    pane_lod1.name = f"{name}_LOD1_Gls"
    pane_lod1.data.name = pane_lod1.name
    bpy.context.collection.objects.link(pane_lod1)
    n0, n1, npane = cs2_asset.tri_count(box), cs2_asset.tri_count(lod1), cs2_asset.tri_count(pane)
    if not n1 < 0.5 * n0:
        raise RuntimeError(f"{name}: LOD1 {n1} tris is not < 50 % of LOD0 {n0}")
    print(f"[probes] {name}: LOD0 {n0} tris, LOD1 {n1}, _Gls pane {npane} (+ _LOD1_Gls)")

    cs2_asset.write_settings(asset_dir, name, supplied_lods=(1,))
    previews = [(box, asset_dir, name), (pane, None, None)]
    return asset_dir, [name, f"{name}_LOD1"], previews, [box, lod1, pane, pane_lod1]


def build_coat_probe(out_root, name="SkiProbeCoatA"):
    cs2_asset.validate_asset_name(name)
    asset_dir = os.path.join(out_root, name)
    os.makedirs(asset_dir, exist_ok=True)

    # -X half of the slab -> u 0.02..0.48, +X half -> u 0.52..0.98 (each half normalized
    # over its OWN x range, so u 0.48 / 0.52 are the x = 0 seam). Seam line 2 texels wide
    # on each side of the seam; labels in the far (+Y) corners: "0" at -X, "25" at +X.
    L = (0.02, 0.02, 0.48, 0.98)
    R = (0.52, 0.02, 0.98, 0.98)
    base = flat_rgba((0.45, 0.46, 0.48))
    dark = (0.10, 0.10, 0.10)
    base[:, int(0.48 * TEX) - 2:int(0.48 * TEX), :3] = dark
    base[:, int(0.52 * TEX):int(0.52 * TEX) + 2, :3] = dark
    paint_text(base, "0", int(0.02 * TEX) + 16, int(0.98 * TEX) - 60, 6, dark)
    paint_text(base, "25", int(0.98 * TEX) - 16 - 11 * 6, int(0.98 * TEX) - 60, 6, dark)
    msk = flat_rgba((0.0, 0.0, 0.0), a=0.5)
    msk[:, TEX // 2:, 1] = 0.25                                       # +X half: coat 0.25
    write_set(asset_dir, name, base, msk=msk, grain=0.015)

    slab = add_box(name, (2.0, 2.0, 0.2), (0.0, 0.0, 0.1))
    bm = bmesh.new()
    bm.from_mesh(slab.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
                           plane_co=(0.0, 0.0, 0.0), plane_no=(1.0, 0.0, 0.0))
    bm.to_mesh(slab.data)
    bm.free()

    def rect(poly):
        return L if poly.center.x < 0 else R

    def bounds(poly):
        return ((-1.0, -1.0, 0.0), (0.0, 1.0, 0.2)) if poly.center.x < 0 else \
               ((0.0, -1.0, 0.0), (1.0, 1.0, 0.2))
    project_faces(slab, rect, bounds)
    cs2_asset.shade_by_angle(slab, 66.0, weighted=False)
    print(f"[probes] {name}: {cs2_asset.tri_count(slab)} tris; -X half G=0, +X half G=0.25, A=0.5")
    cs2_asset.write_settings(asset_dir, name, supplied_lods=())
    return asset_dir, [name], [(slab, asset_dir, name)], [slab]


def build_fnd_probe(out_root, name="SkiProbeFndA"):
    cs2_asset.validate_asset_name(name)
    asset_dir = os.path.join(out_root, name)
    os.makedirs(asset_dir, exist_ok=True)

    base = flat_rgba((0.58, 0.57, 0.55))
    # top-face rects: A -> u 0.06..0.44, B -> u 0.56..0.94, v 0.54..0.96; glyph 5x7 at 20 px
    paint_text(base, "A", int(0.25 * TEX) - 50, int(0.75 * TEX) - 70, 20, (0.08, 0.08, 0.10))
    paint_text(base, "B", int(0.75 * TEX) - 50, int(0.75 * TEX) - 70, 20, (0.08, 0.08, 0.10))
    write_set(asset_dir, name, base, smooth=0.25, grain=0.02)

    # box A: open bottom, side-face bottom edges at z = 0, nothing below
    a = add_box("boxA", (3.0, 3.0, 1.0), (-2.0, 0.0, 0.5), open_bottom=True)
    # box B: closed 1 m box + 0.5 m skirt welded at z = 0 (K station pattern); the two
    # coincident interior faces at z = 0 are removed after the weld.
    b_top = add_box("boxB", (3.0, 3.0, 1.0), (2.0, 0.0, 0.5))
    b_skirt = add_box("boxBskirt", (3.0, 3.0, 0.5), (2.0, 0.0, -0.25))
    b = join([b_top, b_skirt], "boxB")
    bm = bmesh.new()
    bm.from_mesh(b.data)
    interior = [f for f in bm.faces if abs(f.normal.z) > 0.9 and abs(f.calc_center_median().z) < 1e-4]
    bmesh.ops.delete(bm, geom=interior, context="FACES")
    bm.to_mesh(b.data)
    bm.free()

    obj = join([a, b], name)

    def rect(poly):
        u0 = 0.0 if poly.center.x < 0 else 0.5
        if poly.normal.z > 0.9:
            return (u0 + 0.06, 0.54, u0 + 0.44, 0.96)
        return (u0 + 0.06, 0.04, u0 + 0.44, 0.46)

    def bounds(poly):                       # each box normalized over its own extents
        return ((-3.5, -1.5, 0.0), (-0.5, 1.5, 1.0)) if poly.center.x < 0 else \
               ((0.5, -1.5, -0.5), (3.5, 1.5, 1.0))
    project_faces(obj, rect, bounds)
    cs2_asset.shade_by_angle(obj, 66.0, weighted=False)
    print(f"[probes] {name}: {cs2_asset.tri_count(obj)} tris; {ground_ring_report(obj)}")
    cs2_asset.write_settings(asset_dir, name, supplied_lods=())
    return asset_dir, [name], [(obj, asset_dir, name)], [obj]


def ground_ring_report(obj, band=0.01):
    """
    Count edges in the z = 0 +- band that the engine would keep (every touching face has
    a vertex above the band: engine base OK) vs drop (some touching face lies wholly at or
    below the band: BLOCKED) - the AddBaseLines rule from the spec.
    """
    me = obj.data
    ok = blocked = 0
    face_of_edge = {}
    for poly in me.polygons:
        for ek in poly.edge_keys:
            face_of_edge.setdefault(ek, []).append(poly)
    for ek, polys in face_of_edge.items():
        z0, z1 = me.vertices[ek[0]].co.z, me.vertices[ek[1]].co.z
        if abs(z0) > band or abs(z1) > band:
            continue
        dropped = any(all(me.vertices[vi].co.z <= band for vi in p.vertices) for p in polys)
        if dropped:
            blocked += 1
        else:
            ok += 1
    return f"ground ring: {ok} edges engine-base OK, {blocked} BLOCKED"


# --------------------------------------------------------------------------------------
# Export + verification
# --------------------------------------------------------------------------------------

def export_all(asset_dir, objs):
    """Export every object as <obj.name>.fbx (LOD/sub-mesh names are already the stems)."""
    written = []
    for o in objs:
        o.data.materials.clear()
        path = os.path.join(asset_dir, f"{o.name}.fbx")
        cs2_asset.export_fbx(o, path)
        written.append(path)
    return written


def verify_fbx(path, expect_dims_m, name):
    """
    Re-import one FBX into the current (cleared) scene and assert: object name == stem,
    raw vertex extents == expected meters x 100 with Y up, custom normals present.
    """
    bpy.ops.import_scene.fbx(filepath=path)
    imp = [o for o in bpy.context.selected_objects if o.type == "MESH"]
    if len(imp) != 1:
        raise RuntimeError(f"{path}: imported {len(imp)} meshes, expected 1")
    o = imp[0]
    me = o.data
    if o.name != name:
        raise RuntimeError(f"{path}: object name {o.name!r} != {name!r}")
    co = np.array([v.co[:] for v in me.vertices])
    ext = co.max(axis=0) - co.min(axis=0)
    # Blender (x, y, z) m -> file (x, z, -y) cm : extents (X, Z_up, Y)
    want = np.array([expect_dims_m[0], expect_dims_m[2], expect_dims_m[1]]) * 100.0
    if not np.allclose(ext, want, atol=1.0):
        raise RuntimeError(f"{path}: raw extents {ext} cm, expected {want}")
    if not me.has_custom_normals:
        raise RuntimeError(f"{path}: no custom normals after round trip")
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    print(f"[verify] {os.path.basename(path)}: name ok, extents {ext.round(1)} cm, "
          f"{tris} tris, custom normals ok")
    for x in imp:
        bpy.data.objects.remove(x, do_unlink=True)
    return tris


def check_textures(asset_dir, name, lod_sets=()):
    """Every declared PNG exists and is 512 x 512 RGBA (IHDR color type 6)."""
    stems = [name] + [f"{name}_LOD{k}" for k in lod_sets]
    for stem in stems:
        for m in MAPS:
            p = os.path.join(asset_dir, f"{stem}_{m}.png")
            with open(p, "rb") as fh:
                head = fh.read(32)
            w = int.from_bytes(head[16:20], "big")
            h = int.from_bytes(head[20:24], "big")
            if (w, h) != (TEX, TEX) or head[25] != 6:
                raise RuntimeError(f"{p}: {w}x{h} color type {head[25]}, want {TEX} RGBA")
    # channel contract from the written files: BaseColor A == 1 everywhere, MaskMap B == 0,
    # ControlMask A == 0; plus the per-probe MaskMap G expectation.
    def load(stem, m):
        img = bpy.data.images.load(os.path.join(asset_dir, f"{stem}_{m}.png"))
        buf = np.empty(TEX * TEX * 4, dtype=np.float32)
        img.pixels.foreach_get(buf)
        bpy.data.images.remove(img)
        return buf.reshape(TEX, TEX, 4)
    for stem in stems:
        b, mk, ct = load(stem, "BaseColor"), load(stem, "MaskMap"), load(stem, "ControlMask")
        if b[:, :, 3].min() < 0.999:
            raise RuntimeError(f"{stem}: BaseColor alpha < 255 (cutout template risk)")
        if mk[:, :, 2].max() > 1e-3 or ct[:, :, 3].max() > 1e-3:
            raise RuntimeError(f"{stem}: MaskMap B or ControlMask A not zero")
        g_left, g_right = float(mk[:, :TEX // 2, 1].mean()), float(mk[:, TEX // 2:, 1].mean())
        want = (0.0, 0.25) if name == "SkiProbeCoatA" else (0.0, 0.0)
        if abs(g_left - want[0]) > 0.01 or abs(g_right - want[1]) > 0.01:
            raise RuntimeError(f"{stem}: MaskMap G halves {g_left:.3f}/{g_right:.3f}, want {want}")
        print(f"[verify] {stem} maps: BaseColor A=1, MaskMap G {g_left:.2f}|{g_right:.2f} "
              f"A {mk[:, :, 3].mean():.2f}, ControlMask A=0")
    s = json.load(open(os.path.join(asset_dir, "settings.json"), encoding="utf-8"))
    for src, dst in s.get("sharedAssets", {}).items():
        if not os.path.exists(os.path.join(asset_dir, dst)):
            raise RuntimeError(f"{name}: sharedAssets target {dst} missing")
    for key in s:
        if key.startswith("LOD") and not os.path.exists(os.path.join(asset_dir, f"{name}_{key}.fbx")):
            raise RuntimeError(f"{name}: settings declares {key} but {name}_{key}.fbx missing")


# --------------------------------------------------------------------------------------
# Entry point
# --------------------------------------------------------------------------------------

def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True, help="root folder; one sub-folder per probe")
    ap.add_argument("--preview", default=None,
                    help="preview folder (default: ../preview/probes relative to this script)")
    return ap.parse_args(argv)


def main():
    args = parse_args()
    out_root = os.path.abspath(args.out)
    prev_dir = os.path.abspath(args.preview) if args.preview else os.path.normpath(
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "preview", "probes"))
    os.makedirs(out_root, exist_ok=True)
    os.makedirs(prev_dir, exist_ok=True)

    builders = [
        ("SkiProbeLodA", build_lod_probe, (1.0, -1.1, 0.5)),
        ("SkiProbeGlsA", build_gls_probe, (0.6, -1.3, 0.45)),
        ("SkiProbeCoatA", build_coat_probe, (0.4, -1.0, 0.9)),
        ("SkiProbeFndA", build_fnd_probe, (0.7, -1.2, 0.35)),
    ]
    summary = []
    for name, build, cam_dir in builders:
        clear_scene()
        asset_dir, stems, previews, objs = build(out_root, name)
        # LOD probe: spread the three levels out for the single preview frame
        if name == "SkiProbeLodA":
            for o, x in zip(objs, (-2.6, 0.0, 2.6)):
                o.location.x = x
        for o, tex_dir, tex_name in previews:
            if tex_dir is None:
                preview_material(o, None, o.name, glass=True)
            else:
                preview_material(o, tex_dir, tex_name, coat_from_mask=(name == "SkiProbeCoatA"))
        png = render_preview([o for o, _, _ in previews], os.path.join(prev_dir, f"{name}.png"),
                             direction=cam_dir)
        print(f"[probes] preview -> {png}")
        if name == "SkiProbeLodA":
            for o in objs:
                o.location.x = 0.0
        for o in objs:
            o.data.materials.clear()
        dims = {o.name: tuple(o.dimensions) for o in objs}
        export_all(asset_dir, objs)

        clear_scene()
        tris = {}
        for o_name, d in dims.items():
            tris[o_name] = verify_fbx(os.path.join(asset_dir, f"{o_name}.fbx"), d, o_name)
        lod_sets = (2,) if name == "SkiProbeLodA" else ()
        check_textures(asset_dir, name, lod_sets)
        files = sorted(os.listdir(asset_dir))
        print(f"[probes] {name}: {len(files)} files -> {asset_dir}")
        summary.append((name, tris))

    print("PASS ski_probes: " + "; ".join(
        f"{n} " + ",".join(f"{k}={v}" for k, v in t.items()) for n, t in summary))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(1)
