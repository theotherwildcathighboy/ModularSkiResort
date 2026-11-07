"""
Shared helpers that produce CS2-importable asset folders from Blender.

We reverse-engineered the contract from the official examples that ship with the game at
    Cities2_Data/Content/Game/.ModdingToolchain/ExampleAssets/

Every asset is a FOLDER, not a loose FBX. The importer expects this layout:

    <Name>/
        <Name>.fbx                  LOD0 mesh
        <Name>_BaseColor.png        albedo            (sRGB)
        <Name>_Normal.png           tangent normals   (linear)
        <Name>_MaskMap.png          packed PBR        (linear)
        <Name>_ControlMask.png      color variation  (linear)
        settings.json
        [<Name>_LOD1.fbx, <Name>_LOD2.fbx ...]        optional, supplied LODs
        [<Name>_Gls.fbx / _Win.fbx / _Wat.fbx]        optional material sub-meshes

`settings.json` carries a `sharedAssets` map (texture filename -> the file it should reuse) and a per-LOD `{"mode": "Skip"}` entry.
The entry means "I supplied this level, do not generate it". Omit the LOD entries entirely, and LODPostProcessor generates
them by edge collapse.

A bare .fbx with none of this produces no log output at all, because the importer never reaches the file. That is what
happened on the first attempt.
"""

import bpy
import bmesh
import json
import math
import os

import numpy as np


# --------------------------------------------------------------------------------------
# Orientation
# --------------------------------------------------------------------------------------

def orient_for_net(obj):
    """
    Rotate an asset that was authored with +X = travel, so it matches the CS2 network convention.

    CS2 is Unity-handed: X = lateral, Y = up, Z = forward/along the network. The Blender -> CS2 bake in export_fbx maps (x,
    y, z) -> (x, z, -y). So Blender's X stays lateral and Blender's -Y becomes CS2's +Z. An asset built with travel along
    Blender X therefore lands rotated 90 degrees.

    Symptoms that gave this away: lane meshes never stretched, and they sat visibly ACROSS the drawn line. Their long axis
    was X, but the lane deforms along Z, so nothing scaled.

    A rotation of -90 degrees about Blender Z maps (x, y, z) -> (y, -x, z). It sends travel from +X to -Y and the lateral
    axis from Y to X. After the export bake, that becomes travel = +Z, lateral = X, up = Y. This is correct.

    Call this function at the end of a build function, before export. Generators keep authoring in the more readable "+X is
    forward" frame.
    """
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    obj.rotation_euler = (0.0, 0.0, math.radians(-90.0))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return obj


# --------------------------------------------------------------------------------------
# Naming
# --------------------------------------------------------------------------------------

class AssetNameError(ValueError):
    pass


def validate_asset_name(name):
    """
    Asset base names must contain NO underscore.

    CS2 pairs textures to a mesh by parsing `<name>_<suffix>.png`, so the underscore is a reserved separator (`_BaseColor`,
    `_MaskMap`, `_LOD1`, `_Gls`). An underscore inside the base name makes the split ambiguous. The importer then silently
    binds nothing: the asset imports, places and renders wearing CS2's default material, with a 56-byte .Surface and zero
    .Texture assets.

    We proved this side by side in one scene. "SkiTowerRed" gave a 171-byte Surface and 4 textures, and rendered red.
    "SkiTower_RED", with the same generator and the same geometry, gave a 56-byte Surface and no textures, and rendered
    default gray.

    This is consistent with all shipped content: no official ExampleAssets name and no name in the Modular Golf Course Pack
    contains an underscore.

    Failing here is deliberate. This bug costs an entire import/inspect cycle to notice, and it looks like a material
    problem, not a naming problem.
    """
    if "_" in name:
        raise AssetNameError(
            f"Asset name {name!r} contains an underscore. CS2 reserves '_' as the texture "
            f"suffix separator, so the importer will silently bind no textures. "
            f"Use e.g. {name.replace('_', '')!r}."
        )
    if not name or not name[0].isalpha():
        raise AssetNameError(f"Asset name {name!r} should start with a letter.")
    return name


# --------------------------------------------------------------------------------------
# Textures
# --------------------------------------------------------------------------------------

def _write_png(path, name, rgba, size, is_data):
    """
    Write a float RGBA array as PNG through Blender's image API. This avoids a PIL dependency, because Blender ships its own
    Python.

    `rgba` is (h, w, 4) float in 0..1. Blender's pixel buffer is bottom-up, so flip it.
    """
    img = bpy.data.images.new(name, width=size, height=size, alpha=True, is_data=is_data)
    if is_data:
        img.colorspace_settings.name = "Non-Color"

    flat = np.flipud(rgba).astype(np.float32).ravel()
    img.pixels.foreach_set(flat)

    img.filepath_raw = path
    img.file_format = "PNG"
    img.alpha_mode = "STRAIGHT"
    img.save()
    bpy.data.images.remove(img)


def make_texture_set(out_dir, name, size=1024, base_rgb=(0.52, 0.53, 0.55),
                     metallic=0.0, smoothness=0.30, noise=0.03, seed=7,
                     control_rgb=(0.0, 0.0, 0.0)):
    """
    Generate the four maps CS2 expects.

    MaskMap packing. We established it by measuring the official ExampleAssets, and we did not assume the HDRP layout:

        R = metallic     fence (chain link) mean 0.772 / median 1.0
                         firehouse (brick)  mean 0.040 / median 0.0
        G = 0            EXACTLY zero in every official MaskMap
        B = 0            EXACTLY zero in every official MaskMap
        A = smoothness   fence 0.706 (shiny metal), firehouse 0.276 (matte brick)

    G and B are uniformly zero across every shipped example. That is the useful signal. An earlier version set G = 1.0 on the
    assumption that it was ambient occlusion, and the towers rendered with a strong blue sky reflection. Whatever G drives,
    official content leaves it at zero, so we do too.

    ControlMask alpha is zero in every official asset (an earlier version wrongly set 1.0).

    RECOLORING (`control_rgb`): the ControlMask RGB channels are REGION MASKS for the game's custom-color system. We measured
    this from ExampleColorMaskFirehouse. Its mask has three hard binary regions (R ~32% of texels, G ~10%, B ~20%) over a
    desaturated BaseColor. Each channel marks the texels that one of the three color slots tints. `control_rgb=(1,0,0)` makes
    the whole surface recolorable through slot 1. This is the right default for our uniform procedural textures. Keep
    `base_rgb` grayish on recolorable assets, so the tint reads true.
    """
    os.makedirs(out_dir, exist_ok=True)
    rng = np.random.default_rng(seed)

    # --- BaseColor: flat steel with a little grain, so it does not look like plastic
    base = np.zeros((size, size, 4), dtype=np.float32)
    grain = rng.normal(0.0, noise, (size, size, 1)).astype(np.float32)
    for c in range(3):
        base[:, :, c] = np.clip(base_rgb[c] + grain[:, :, 0], 0.0, 1.0)
    base[:, :, 3] = 1.0
    _write_png(os.path.join(out_dir, f"{name}_BaseColor.png"), f"{name}_BaseColor",
               base, size, is_data=False)

    # --- Normal: flat tangent-space normal (0.5, 0.5, 1.0)
    nrm = np.zeros((size, size, 4), dtype=np.float32)
    nrm[:, :, 0] = 0.5
    nrm[:, :, 1] = 0.5
    nrm[:, :, 2] = 1.0
    nrm[:, :, 3] = 1.0
    _write_png(os.path.join(out_dir, f"{name}_Normal.png"), f"{name}_Normal",
               nrm, size, is_data=True)

    # --- MaskMap: R = metallic, A = smoothness, G and B pinned to zero to match official content
    msk = np.zeros((size, size, 4), dtype=np.float32)
    msk[:, :, 0] = metallic
    msk[:, :, 1] = 0.0
    msk[:, :, 2] = 0.0
    msk[:, :, 3] = smoothness
    _write_png(os.path.join(out_dir, f"{name}_MaskMap.png"), f"{name}_MaskMap",
               msk, size, is_data=True)

    # --- ControlMask: RGB = recolor region masks (see docstring), alpha always zero
    ctl = np.zeros((size, size, 4), dtype=np.float32)
    ctl[:, :, 0] = control_rgb[0]
    ctl[:, :, 1] = control_rgb[1]
    ctl[:, :, 2] = control_rgb[2]
    _write_png(os.path.join(out_dir, f"{name}_ControlMask.png"), f"{name}_ControlMask",
               ctl, size, is_data=True)

    return [f"{name}_{m}.png" for m in ("BaseColor", "Normal", "MaskMap", "ControlMask")]


# --------------------------------------------------------------------------------------
# Material
# --------------------------------------------------------------------------------------

def assign_material(obj, name, tex_dir):
    """
    Give the mesh a real material with image-texture nodes.

    Without this, the exported FBX carries NO material at all, and CS2 then has nothing to bind the sibling PNGs to. The
    symptom is subtle and easy to misread: the asset imports, renders, and looks *plausible*, but it wears CS2's default
    surface. Two builds with completely different MaskMaps produced byte-identical 56-byte .Surface assets and no .Texture
    assets whatsoever. That is how we caught this.

    Compare a working asset: the official ExampleBakedAlphaFence.fbx contains a "Texture/" reference. Ours contained no
    material or texture strings at all.

    Call this function AFTER make_texture_set, because it loads the PNGs that function writes.
    """
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree

    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)

    def image_node(filename, colorspace, x, y):
        path = os.path.join(tex_dir, filename)
        if not os.path.exists(path):
            return None
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(path)
        node.image.colorspace_settings.name = colorspace
        node.location = (x, y)
        return node

    base = image_node(f"{name}_BaseColor.png", "sRGB", -600, 300)
    if base and bsdf:
        nt.links.new(base.outputs["Color"], bsdf.inputs["Base Color"])

    nrm = image_node(f"{name}_Normal.png", "Non-Color", -600, 0)
    if nrm and bsdf:
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nmap.location = (-300, 0)
        nt.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])

    # MaskMap and ControlMask are consumed by CS2's own shader, not Blender's. We load them so the exporter records them in
    # the FBX, but we leave them unconnected. Wiring them into a Principled BSDF would misrepresent them.
    image_node(f"{name}_MaskMap.png", "Non-Color", -600, -300)
    image_node(f"{name}_ControlMask.png", "Non-Color", -600, -600)

    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return mat


# --------------------------------------------------------------------------------------
# Real PBR textures (CC0 sets from ambientCG) and the 2x2 atlas
# --------------------------------------------------------------------------------------

def _load_map(set_dir, suffix, size):
    """
    Load one map of an ambientCG set (e.g. suffix='Color') as (size,size,4) float RGBA. Return None if the set has no such
    map. Orientation is irrelevant here, because every consumer treats texels independently, and read and write both use
    Blender's bottom-up buffer.
    """
    import glob
    hits = glob.glob(os.path.join(set_dir, f"*_{suffix}.jpg")) + \
           glob.glob(os.path.join(set_dir, f"*_{suffix}.png"))
    if not hits:
        return None
    img = bpy.data.images.load(hits[0])
    if img.size[0] != size or img.size[1] != size:
        img.scale(size, size)
    buf = np.empty(size * size * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    px = buf.reshape(size, size, 4).copy()
    bpy.data.images.remove(img)
    return px


def _write_png_raw(path, name, rgba, size, is_data):
    """Write an (h,w,4) float array exactly as-is (no flip) via Blender's image API."""
    img = bpy.data.images.new(name, width=size, height=size, alpha=True, is_data=is_data)
    if is_data:
        img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(rgba.astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.alpha_mode = "STRAIGHT"
    img.save()
    bpy.data.images.remove(img)


# Quadrant layout of the atlas, in UV space: (u_offset, v_offset), each quad 0.5 x 0.5.
QUADS = {0: (0.0, 0.0), 1: (0.5, 0.0), 2: (0.0, 0.5), 3: (0.5, 0.5)}


def make_texture_set_atlas(out_dir, name, quad_sets, quad_size=1024,
                           control_quads=(0.0, 0.0, 0.0, 0.0), smoothness_scale=1.0,
                           post=None):
    """
    Build the CS2 four-map pack as a 2x2 atlas of real PBR sets.

    - quad_sets: {quad_index: path_to_ambientCG_set_dir} - quads 0..3 (BL, BR, TL, TR). The function fills a missing quad
      with mid-gray.
    - control_quads: per-quad ControlMask R value (recolor slot 1 coverage). Painted parts get 1.0 (recolorable). Raw
      concrete and steel get 0.0.
    - post: optional callback (base, nrm, msk, ctl, atlas_size). The function calls it after it paints the quads and before
      it writes the PNGs. It lets one asset stamp bespoke detail (e.g. the terminal's painted tension-scale numbers) into ITS
      copy of the atlas, without touching the shared recipe that every other lift asset uses.

    CS2 packing (established earlier from official assets): MaskMap R = metallic, A = smoothness = 1 - roughness. G and B are
    pinned to zero. Normal uses the GL convention.
    """
    os.makedirs(out_dir, exist_ok=True)
    S = quad_size
    A = S * 2
    base = np.full((A, A, 4), (0.5, 0.5, 0.5, 1.0), dtype=np.float32)
    nrm  = np.full((A, A, 4), (0.5, 0.5, 1.0, 1.0), dtype=np.float32)
    msk  = np.zeros((A, A, 4), dtype=np.float32)
    ctl  = np.zeros((A, A, 4), dtype=np.float32)

    rng = np.random.default_rng(11)
    for q, (uo, vo) in QUADS.items():
        x0, y0 = int(uo * A), int(vo * A)
        sl = (slice(y0, y0 + S), slice(x0, x0 + S))
        set_dir = quad_sets.get(q)
        if isinstance(set_dir, dict) and "set" in set_dir:
            # Photo set with adjustments: {"set": path, "tint": 0.6} multiplies the albedo. ambientCG's concrete renders
            # near-white under CS2's sun, so the tint grounds it.
            spec = set_dir
            set_dir = spec["set"]
            tint = spec.get("tint", 1.0)
            c = _load_map(set_dir, "Color", S)
            n = _load_map(set_dir, "NormalGL", S)
            r = _load_map(set_dir, "Roughness", S)
            m = _load_map(set_dir, "Metalness", S)
            if c is not None:
                base[sl] = c
                base[sl][..., :3] *= tint
                base[sl][..., 3] = 1.0
            if n is not None:
                nrm[sl] = n
                nrm[sl][..., 3] = 1.0
            rough = r[..., 0] if r is not None else np.full((S, S), 0.6, dtype=np.float32)
            metal = m[..., 0] if m is not None else np.zeros((S, S), dtype=np.float32)
            msk[sl][..., 0] = metal
            msk[sl][..., 3] = np.clip((1.0 - rough) * spec.get("smooth_scale", smoothness_scale),
                                      0.0, 1.0)
            ctl[sl][..., 0] = control_quads[q] if q < len(control_quads) else 0.0
            continue
        if isinstance(set_dir, dict):
            # Procedural quad: clean uniform surface (powder-coated panels, plain paint).
            spec = set_dir
            grain = rng.normal(0.0, spec.get("noise", 0.015), (S, S, 1)).astype(np.float32)
            for c in range(3):
                base[sl][..., c] = np.clip(spec["rgb"][c] + grain[..., 0], 0.0, 1.0)
            base[sl][..., 3] = 1.0
            msk[sl][..., 0] = spec.get("metal", 0.0)
            msk[sl][..., 3] = spec.get("smooth", 0.4)
            ctl[sl][..., 0] = control_quads[q] if q < len(control_quads) else 0.0
            if spec.get("panels"):
                paint_panel_detail(base[sl], nrm[sl], msk[sl], S, rng,
                                   seam_px=spec.get("seam_px", 96))
            continue
        if set_dir:
            c = _load_map(set_dir, "Color", S)
            n = _load_map(set_dir, "NormalGL", S)
            r = _load_map(set_dir, "Roughness", S)
            m = _load_map(set_dir, "Metalness", S)
            if c is not None:
                base[sl] = c
                base[sl][..., 3] = 1.0
            if n is not None:
                nrm[sl] = n
                nrm[sl][..., 3] = 1.0
            rough = r[..., 0] if r is not None else np.full((S, S), 0.6, dtype=np.float32)
            metal = m[..., 0] if m is not None else np.zeros((S, S), dtype=np.float32)
            msk[sl][..., 0] = metal
            msk[sl][..., 3] = np.clip((1.0 - rough) * smoothness_scale, 0.0, 1.0)
        ctl[sl][..., 0] = control_quads[q] if q < len(control_quads) else 0.0

    if post is not None:
        post(base, nrm, msk, ctl, A)

    _write_png_raw(os.path.join(out_dir, f"{name}_BaseColor.png"), f"{name}_BaseColor", base, A, is_data=False)
    _write_png_raw(os.path.join(out_dir, f"{name}_Normal.png"), f"{name}_Normal", nrm, A, is_data=True)
    _write_png_raw(os.path.join(out_dir, f"{name}_MaskMap.png"), f"{name}_MaskMap", msk, A, is_data=True)
    _write_png_raw(os.path.join(out_dir, f"{name}_ControlMask.png"), f"{name}_ControlMask", ctl, A, is_data=True)
    return [f"{name}_{m}.png" for m in ("BaseColor", "Normal", "MaskMap", "ControlMask")]


def paint_panel_detail(base_sl, nrm_sl, msk_sl, S, rng,
                       seam_px=96, seam_dark=0.10, bolt_every=2, groove_depth=0.35):
    """
    Draw panel seams and bolts into the slices of a quad. The seams are darkened lines in BaseColor and height-derived
    grooves in the Normal map. `base_sl`/`nrm_sl`/`msk_sl` are (S,S,4) views.

    Normal-map math: build a height field (0 = flat, negative = groove), then convert it with finite differences. Normal =
    normalize(-dh/dx, -dh/dy, 1), mapped into 0..1 RGB. This is what makes seams read as real grooves under game lighting
    without any triangles.
    """
    height = np.zeros((S, S), dtype=np.float32)
    for p in range(0, S + 1, seam_px):
        c = min(p, S - 2)
        # seam grooves, 2px wide
        height[c:c + 2, :] -= groove_depth
        height[:, c:c + 2] -= groove_depth
        base_sl[c:c + 2, :, :3] *= (1.0 - seam_dark)
        base_sl[:, c:c + 2, :3] *= (1.0 - seam_dark)
    # bolts beside seam intersections: small height bumps
    for py in range(0, S, seam_px * bolt_every):
        for px in range(0, S, seam_px * bolt_every):
            bx, by = min(px + 8, S - 3), min(py + 8, S - 3)
            height[by - 1:by + 2, bx - 1:bx + 2] += 0.5
    # subtle vertical grime gradient (heavier low)
    grad = np.linspace(0.06, 0.0, S, dtype=np.float32).reshape(S, 1)
    base_sl[..., :3] *= (1.0 - grad)[..., None]

    dhdx = np.zeros_like(height); dhdy = np.zeros_like(height)
    dhdx[:, 1:-1] = (height[:, 2:] - height[:, :-2]) * 0.5
    dhdy[1:-1, :] = (height[2:, :] - height[:-2, :]) * 0.5
    nx, ny, nz = -dhdx * 4.0, -dhdy * 4.0, np.ones_like(height)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    nrm_sl[..., 0] = nx / ln * 0.5 + 0.5
    nrm_sl[..., 1] = ny / ln * 0.5 + 0.5
    nrm_sl[..., 2] = nz / ln * 0.5 + 0.5
    nrm_sl[..., 3] = 1.0


# --------------------------------------------------------------------------------------
# Recolor contract (K generation)
#
# The vanilla recolor UI MULTIPLIES the picked color against the albedo. Recolorable regions must therefore keep a LIGHT,
# DESATURATED albedo, or every tint collapses toward black (the J-round bug: picker colors barely read against the near-black
# steel). Vanilla's own convention: masked regions ship light-neutral albedo, and the prefab's default color VariationSet
# supplies the DEFAULT look. The authored default paint colors therefore move prefab-side. This file records them as the
# single source of truth:
# --------------------------------------------------------------------------------------

STEEL_DEFAULT_RGB = (0.20, 0.21, 0.22)   # the dark Pali steel paint, slot 1 prefab default
RECOLOR_ALBEDO = 0.80                    # light neutral gray under every recolor mask

# T27 (2026-10-17, docs/research/field/2026-10-15/build/T27-rope-path.md section 1): the station rope = the LINE rope. Every
# lift line draws the vanilla HV / LV cable lane mesh (463c051d... / bbb9724d..., DECISIONS 243). Both use ONE material,
# "Powerline Cables" (Game/Blob.cok entry Powerline Cables_207901033b5d7c049b968f36a8a91ad9.Material, CID
# 106cc56e9f15a135bb6ea7b99d675c2d, template id 30 = CurvedTransparent). It has no textures and one override: _BaseColor =
# (0.018868, 0.018868, 0.018868, 1).
#
# A Unity Color is stored gamma-encoded (SurfaceAsset.SyncToUnityMaterial -> Material.SetColor, 1.6.2f1-colossal
# SurfaceAsset.cs:594). So the linear albedo is 0.00146 (sRGB 4.8/255). If the game fed it linear, it would be 0.0189 = sRGB
# 37/255: UNVERIFIED, field line in T27.
#
# Smoothness and metal come from the template (resources.assets Material "CurvedTransparent": _Smoothness 0.5, _Metallic
# 0.0). Our MaterialSpec albedo is LINEAR, and rough = 1 - smoothness. Before T27, every station's "rope" had albedo (0.30,
# 0.30, 0.32), metal 0.9, rough 0.65.
VANILLA_CABLE_BASECOLOR = 0.018868       # the stored _BaseColor (gamma-encoded Unity Color)
VANILLA_CABLE_ALBEDO_LIN = 0.00146       # its linear value
VANILLA_CABLE_SMOOTHNESS = 0.5
VANILLA_CABLE_METAL = 0.0
ROPE_MATERIAL = dict(albedo=(VANILLA_CABLE_ALBEDO_LIN,) * 3, metal=VANILLA_CABLE_METAL,
                     rough=round(1.0 - VANILLA_CABLE_SMOOTHNESS, 6))
ROPE_MATERIAL_PRE_T27 = dict(albedo=(0.30, 0.30, 0.32), metal=0.9, rough=0.65)


def lift_atlas_quads(tex_root):
    """
    The ONE standard atlas for every lift asset (stations, towers, chairs). The whole system shares a palette, and every
    painted part answers to the same recolor slot:

        0 concrete   (pads, plinths, ramps)     photo, tinted down    not recolorable
        1 galvanized (sheaves, grips, ladders)  photo Metal032        not recolorable
        2 dark glass (liftie hut window band)   procedural            not recolorable
          - top of quad 2 is RESERVED for the crash-pad strip, see PAD_* below
        3 painted    (masts, frames, chairs)    procedural light-neutral steel,
                                                RECOLORABLE (slot 1)

    K: quad 3 albedo is RECOLOR_ALBEDO gray, not the dark Pali paint. Recolor tints multiply, so the region must stay light
    for picker colors to read true. The grain, panel seams and grime detail stay (they multiply, so they survive any tint).
    The shipped dark look comes from STEEL_DEFAULT_RGB in each prefab's default VariationSet.

    Return (quad_sets, control_quads), ready for make_texture_set_atlas.
    """
    quad_sets = {
        0: {"set": os.path.join(tex_root, "Concrete034"), "tint": 0.58},
        1: os.path.join(tex_root, "Metal032"),
        2: {"rgb": (0.07, 0.09, 0.11), "noise": 0.004, "smooth": 0.92, "metal": 0.0},
        3: {"rgb": (RECOLOR_ALBEDO, RECOLOR_ALBEDO, RECOLOR_ALBEDO),
            "noise": 0.010, "smooth": 0.45, "metal": 0.1,
            "panels": True, "seam_px": 160},
    }
    return quad_sets, (0.0, 0.0, 0.0, 1.0)


# --------------------------------------------------------------------------------------
# Crash-pad strip (J follow-up)
#
# The tower mast pads are RECOLORABLE ON THEIR OWN SLOT (ControlMask G = slot 2), default orange-red. A player can therefore
# restyle the pads independently of the black steel (slot 1). The pad material lives in a horizontal strip reserved out of
# the top of the GLASS quadrant (quad 2: procedural, mostly headroom). Both halves of the contract are authored HERE.
# paint_pad_strip() writes the texels. lift_tower reads CRASHPAD + PAD_* to build the shell and hand-set its UVs into the
# strip (set_plate_uvs pattern).
#
# unwrap_into_quadrant knows the reservation (QUAD_RESERVED_TOP). It squeezes quad-2 smart projections below the strip, so
# hut window islands can never land on orange.
# --------------------------------------------------------------------------------------

# Strip extent in ATLAS UV space (quad 2 spans u 0..0.5, v 0.5..1.0)
PAD_U0, PAD_U1 = 0.010, 0.490
PAD_V0, PAD_V1 = 0.945, 0.9995

# Shell geometry contract, shared with lift_tower.build_crash_pad. The paint step needs it too. The strip's texel aspect is
# wildly anisotropic (wide arc, short height), so the warning triangle must be pre-stretched horizontally to come out
# equilateral on the pad.
CRASHPAD = {
    "radius": 0.56,        # outer shell radius (m)
    "thickness": 0.06,     # shell wall thickness
    "z0": 0.15,            # bottom of pad above tower base
    "z1": 1.90,            # top of pad - skier impact height
    "arc_deg": 270.0,      # open-backed: the gap faces downhill (-X)
    "segments": 14,
}

# Fraction of the top of a quadrant that unwrap_into_quadrant must keep clear.
#
# - 0.12 of quad 2 = atlas v 0.94..1.0, which contains the pad strip (0.945+).
# - 0.12 of quad 0 = atlas v 0.44..0.50: the tower FOUNDATION-PIER band (M generation, lift_tower.PIER_*).
#
# The pier band must NOT hug the quad-0/quad-2 boundary. Quad 2's glass is near-black. Any strip that touches v 0.5 gets that
# black averaged into it at the first mip level (found the hard way: the pier top rendered pitch black). The band therefore
# ends at 0.4995 as PAINTED padding, while geometry maps only up to 0.4900. This keeps ~20 texels of same-tone insulation
# against the glass.
QUAD_RESERVED_TOP = {2: 0.12, 0: 0.12}

PAD_RGB = (0.85, 0.25, 0.10)          # default orange-red - slot 2 PREFAB default (K:
                                      # applied via the default VariationSet, not albedo)
PAD_SMOOTH = 0.25


def paint_pad_strip(base, nrm, msk, ctl, A):
    """
    Paint the crash-pad material strip and the white warning triangle into an atlas. The signature matches the
    make_texture_set_atlas `post` hooks, so callers can compose it.

    ControlMask: G=1 (recolor slot 2) across the strip, EXCEPT under the triangle markings. They are painted symbols and must
    not tint (same rule as the terminal's tension-scale digits).

    K: the strip albedo is light-neutral RECOLOR_ALBEDO gray, not PAD_RGB. Slot-2 tints multiply, so the orange moves to the
    prefab default VariationSet (PAD_RGB stays above as its source of truth). The warning triangle/exclamation stamps stay
    exactly as authored. They are masked out of G, so they never tint.
    """
    rng = np.random.default_rng(23)
    c0, c1 = int(PAD_U0 * A), int(PAD_U1 * A)
    r0, r1 = int(PAD_V0 * A), int(PAD_V1 * A)
    h, w = r1 - r0, c1 - c0

    # Matte vinyl: flat color with faint grain, dielectric, low smoothness
    grain = rng.normal(0.0, 0.012, (h, w, 1)).astype(np.float32)
    for ch in range(3):
        base[r0:r1, c0:c1, ch] = np.clip(RECOLOR_ALBEDO + grain[..., 0], 0.0, 1.0)
    base[r0:r1, c0:c1, 3] = 1.0
    msk[r0:r1, c0:c1, 0] = 0.0
    msk[r0:r1, c0:c1, 3] = PAD_SMOOTH
    ctl[r0:r1, c0:c1, 0] = 0.0
    ctl[r0:r1, c0:c1, 1] = 1.0                      # recolor slot 2

    # Warning triangle on the uphill face. The outer wall maps arc -> u and height -> v, so horizontal texels/meter exceed
    # vertical by `aniso`. Stretch the width accordingly.
    arc_len = math.radians(CRASHPAD["arc_deg"]) * CRASHPAD["radius"]
    pad_h = CRASHPAD["z1"] - CRASHPAD["z0"]
    aniso = (w / arc_len) / (h / pad_h)
    T = int(h * 0.62)                               # triangle height in texels
    W = int(T * 1.12 * aniso)                       # pre-stretched width
    cx = c0 + w // 2                                # strip center = arc center = +X face
    y_base = r0 + int(h * 0.16)
    white, dark = (0.92, 0.92, 0.92), (0.10, 0.10, 0.10)
    for row in range(T):
        half = int(W / 2.0 * (1.0 - row / T))
        rr = y_base + row
        base[rr, cx - half:cx + half, 0:3] = white
        ctl[rr, cx - half:cx + half, 1] = 0.0
    # Exclamation mark: bar + dot, in dark, pre-stretched horizontally like the triangle
    bw = max(2, int(0.10 * T * aniso / 2.0))
    bar0, bar1 = y_base + int(T * 0.30), y_base + int(T * 0.72)
    dot0, dot1 = y_base + int(T * 0.10), y_base + int(T * 0.22)
    base[bar0:bar1, cx - bw:cx + bw, 0:3] = dark
    base[dot0:dot1, cx - bw:cx + bw, 0:3] = dark


# --------------------------------------------------------------------------------------
# Snowmaking prop family atlas (docs/research/art/snow-prop-materials.md)
#
# This is a FAMILY-PRIVATE recipe. It lives BESIDE lift_atlas_quads and never replaces or extends it. QUADS,
# QUAD_RESERVED_TOP and unwrap_into_quadrant are untouched, so no shipped lift asset re-imports. Quads 0 and 1 are
# deliberately byte-identical to the lift recipe (same Concrete034 at tint 0.58, same Metal032), so the pack still reads as
# one machine shop. Quad 2 is re-cut into rubber / grate / safety-plastic zones. Quad 3 is split into a recolorable livery
# rect and a recolorable accent rect, because every documented manufacturer livery is two-tone.
#
# Why this exists: snow guns need materials that the lift atlas has no slot for. The first draft of snow_props.py put TIRES,
# HOSES AND THE FAN INTAKE on the glass quad at 0.92 smoothness (mirror-finish rubber). It also put the ORANGE HYDRANT LID on
# recolor slot 1, so the safety cap would have recolored with the manufacturer livery.
#
# Recolor slots: R = livery paint, G = safety orange (PAD_RGB, unchanged), B = accent.
# --------------------------------------------------------------------------------------

RUBBER_RGB  = (0.055, 0.055, 0.060)
GRATE_RGB   = (0.090, 0.095, 0.100)
AMBER_RGB   = (0.860, 0.460, 0.040)
FLOODLENS_RGB = (0.780, 0.800, 0.820)   # 4000-5000 K cool white, unlit
REFLECT_RGB = (0.780, 0.790, 0.800)
PLATE_RGB   = (0.160, 0.170, 0.180)


def _rect_px(rect, A):
    """UV rect (u0, v0, u1, v1) -> (row0, row1, col0, col1) texel slice bounds."""
    u0, v0, u1, v1 = rect
    return int(v0 * A), int(v1 * A), int(u0 * A), int(u1 * A)


def height_to_normal(height, nrm_view, strength=4.0):
    """Convert a height field into a tangent-space normal (GL convention) with finite differences, and write it into nrm_view in
    place. We factored this out of paint_panel_detail's idiom, so every rect painter shares one implementation."""
    gy, gx = np.gradient(height.astype(np.float32))
    nx = np.clip(0.5 - gx * strength, 0.0, 1.0)
    ny = np.clip(0.5 + gy * strength, 0.0, 1.0)
    nrm_view[..., 0] = nx
    nrm_view[..., 1] = ny
    nrm_view[..., 2] = 1.0
    nrm_view[..., 3] = 1.0


def snow_prop_zones(A):
    """
    Named atlas rects for the snowmaking prop family, in atlas UV.

    The stamped detail strip is ONE full-width band at the very top of the atlas, not a reservation inside a quad.
    apply_finish_textures packs AO islands into the tallest reserved-free v-band, so a strip buried at a quad boundary would
    halve the usable texel density. STRIP_PX is defined in TEXELS, so a stamped chevron is equally crisp at any atlas size.
    """
    strip_px = 48 if A >= 512 else 32
    vs = (A - strip_px) / float(A)          # strip bottom
    h = vs - 0.5                            # usable height of the upper quads
    e = 0.004                               # edge inset, keeps bilinear off the seams
    return {
        # quad 2, re-cut
        "rubber":  (e,     0.5,                e + 0.492, 0.5 + 0.40 * h),
        "grate":   (e,     0.5 + 0.41 * h,     e + 0.492, 0.5 + 0.62 * h),
        "plastic": (e,     0.5 + 0.63 * h,     e + 0.492, vs - e),
        # quad 3, split
        "livery":  (0.504, 0.5,                0.770,     vs - e),
        "accent":  (0.775, 0.5,                0.996,     vs - e),
        # the stamped strip. `floodlens` is cool-white and DELIBERATELY separate from `amber`. A piste floodlight is
        # 4000-5000 K. Reusing the beacon's amber would tie the floodlight visually to the snow gun. That is backwards,
        # because the whole design goal is to tell the two masts apart at a glance (piste-floodlight-spec.md).
        "chevron":   (0.000, vs,               0.360,     1.0),
        "reflect":   (0.360, vs,               0.540,     1.0),
        "amber":     (0.540, vs,               0.630,     1.0),
        "floodlens": (0.630, vs,               0.720,     1.0),
        "plate":     (0.720, vs,               0.860,     1.0),
        "label":     (0.860, vs,               1.000,     1.0),
        "strip":   (0.000, vs,                 1.000,     1.0),
        # untouched photo quads, addressable by name
        "concrete": (0.0, 0.0, 0.5, 0.5),
        "galv":     (0.5, 0.0, 1.0, 0.5),
    }


def snow_prop_atlas_quads(tex_root):
    """
    Family-private atlas recipe. It returns (quad_sets, control_quads) for make_texture_set_atlas. Pair it with
    post=snow_prop_post.

    control_quads writes ControlMask R across quad 3. snow_prop_post then ZEROES R over the accent rect and sets B there
    instead. So the two paint zones answer to different recolor slots from one quad.
    """
    quad_sets = {
        0: {"set": os.path.join(tex_root, "Concrete034"), "tint": 0.58},
        1: os.path.join(tex_root, "Metal032"),
        # snow_prop_post fully overpaints quad 2. This is just a sane ground.
        2: {"rgb": GRATE_RGB, "noise": 0.004, "smooth": 0.30, "metal": 0.0},
        3: {"rgb": (RECOLOR_ALBEDO, RECOLOR_ALBEDO, RECOLOR_ALBEDO),
            "noise": 0.010, "smooth": 0.60, "metal": 0.05,
            "panels": True, "seam_px": 96},
    }
    return quad_sets, (0.0, 0.0, 0.0, 1.0)


def snow_prop_post(base, nrm, msk, ctl, A):
    """make_texture_set_atlas `post` hook for the snowmaking family."""
    Z = snow_prop_zones(A)
    rng = np.random.default_rng(37)

    def sl(name):
        r0, r1, c0, c1 = _rect_px(Z[name], A)
        return (slice(r0, r1), slice(c0, c1)), (r1 - r0, c1 - c0)

    # ---- rubber: tires, hoses, intake shadow. Matte, dielectric, faint molding ribs.
    s, (h, w) = sl("rubber")
    for ch in range(3):
        base[s][..., ch] = np.clip(RUBBER_RGB[ch] + rng.normal(0, 0.006, (h, w)), 0, 1)
    base[s][..., 3] = 1.0
    msk[s][..., 0] = 0.0
    msk[s][..., 3] = 0.18
    ctl[s][..., 0] = 0.0
    rib = np.zeros((h, w), dtype=np.float32)
    rib[::3, :] = 0.035
    height_to_normal(rib, nrm[s], strength=3.0)

    # ---- grate: intake mesh, platform grating, cabinet louvres.
    s, (h, w) = sl("grate")
    for ch in range(3):
        base[s][..., ch] = GRATE_RGB[ch]
    holes = np.zeros((h, w), dtype=np.float32)
    holes[::9, :] = 1.0
    holes[:, ::9] = 1.0
    base[s][..., 0:3] *= (1.0 - 0.55 * holes)[..., None]
    base[s][..., 3] = 1.0
    msk[s][..., 0] = 0.35
    msk[s][..., 3] = 0.30
    ctl[s][..., 0] = 0.0
    height_to_normal(-holes * 0.05, nrm[s], strength=3.5)

    # ---- plastic: hydrant lids, valve handwheels, marker caps. RECOLOR SLOT 2 (orange).
    s, (h, w) = sl("plastic")
    for ch in range(3):
        base[s][..., ch] = np.clip(RECOLOR_ALBEDO + rng.normal(0, 0.010, (h, w)), 0, 1)
    base[s][..., 3] = 1.0
    msk[s][..., 0] = 0.0
    msk[s][..., 3] = 0.55
    ctl[s][..., 0] = 0.0
    ctl[s][..., 1] = 1.0
    pebble = rng.normal(0.0, 0.010, (h, w)).astype(np.float32)
    pebble[int(h * 0.32):int(h * 0.36), :] += 0.05      # two molded ribs
    pebble[int(h * 0.64):int(h * 0.68), :] += 0.05
    height_to_normal(pebble, nrm[s], strength=3.0)

    # ---- accent: second livery tone. Quad 3 already carries R=1 from control_quads.
    #      Hand this rect to slot 3 instead, so chassis and housing recolor separately.
    s, (h, w) = sl("accent")
    ctl[s][..., 0] = 0.0
    ctl[s][..., 2] = 1.0
    msk[s][..., 3] = 0.55

    # ---- chevron: hazard band. It uses paint_pad_strip's exact idiom: light field on slot 2,
    #      dark chevrons masked OUT of the slot, so painted symbols never tint.
    s, (h, w) = sl("chevron")
    for ch in range(3):
        base[s][..., ch] = RECOLOR_ALBEDO
    base[s][..., 3] = 1.0
    msk[s][..., 0] = 0.0
    msk[s][..., 3] = 0.45
    ctl[s][..., 0] = 0.0
    ctl[s][..., 1] = 1.0
    r0, r1, c0, c1 = _rect_px(Z["chevron"], A)
    period = max(6, h * 2)
    yy, xx = np.mgrid[0:h, 0:w]
    stripe = (((xx + yy) % period) < (period // 2))
    band = base[r0:r1, c0:c1]
    band[..., 0:3] = np.where(stripe[..., None], 0.09, band[..., 0:3])
    ctl[r0:r1, c0:c1, 1] = np.where(stripe, 0.0, 1.0)
    grooves = np.where(stripe, 0.02, 0.0).astype(np.float32)
    height_to_normal(grooves, nrm[r0:r1, c0:c1], strength=2.0)

    # ---- reflective band (marker poles) and amber beacon lens.
    s, (h, w) = sl("reflect")
    for ch in range(3):
        base[s][..., ch] = REFLECT_RGB[ch]
    base[s][..., 3] = 1.0
    msk[s][..., 0] = 0.0
    msk[s][..., 3] = 0.88
    ctl[s][..., 0] = 0.0

    s, (h, w) = sl("amber")
    for ch in range(3):
        base[s][..., ch] = AMBER_RGB[ch]
    base[s][..., 3] = 1.0
    msk[s][..., 0] = 0.0
    msk[s][..., 3] = 0.92
    ctl[s][..., 0] = 0.0
    fres = np.zeros((h, w), dtype=np.float32)
    fres[:, ::2] = 0.03
    height_to_normal(fres, nrm[s], strength=2.5)

    # ---- floodlight lens: cool white, very smooth, faint prismatic ribbing. This is the
    #      surface the emissive map will drive at night.
    s, (h, w) = sl("floodlens")
    for ch, v in enumerate(FLOODLENS_RGB):
        base[s][..., ch] = v
    base[s][..., 3] = 1.0
    msk[s][..., 0] = 0.0
    msk[s][..., 3] = 0.94
    ctl[s][..., 0] = 0.0
    prism = np.zeros((h, w), dtype=np.float32)
    prism[:, ::3] = 0.02
    height_to_normal(prism, nrm[s], strength=2.0)

    # ---- maker's plate and warning label. Generic marks only, no trademarks.
    s, (h, w) = sl("plate")
    for ch in range(3):
        base[s][..., ch] = PLATE_RGB[ch]
    base[s][..., 3] = 1.0
    msk[s][..., 0] = 0.55
    msk[s][..., 3] = 0.70
    ctl[s][..., 0] = 0.0

    s, (h, w) = sl("label")
    for ch in range(3):
        base[s][..., ch] = 0.88
    base[s][..., 3] = 1.0
    msk[s][..., 0] = 0.0
    msk[s][..., 3] = 0.50
    ctl[s][..., 0] = 0.0
    r0, r1, c0, c1 = _rect_px(Z["label"], A)
    T = max(3, int(h * 0.70))
    cx = c0 + (c1 - c0) // 2
    y0 = r0 + int(h * 0.15)
    for row in range(T):
        half = max(1, int((c1 - c0) * 0.16 * (1.0 - row / T)))
        base[y0 + row, cx - half:cx + half, 0:3] = (0.95, 0.78, 0.05)


def make_snow_prop_emissive(out_dir, name, quad_size, lit=None):
    """
    Write the fifth map, `<name>_Emissive.png`, for the snowmaking prop family.

    THREE THINGS THAT ARE EASY TO GET WRONG (docs/research/art/prop-emissive-lights.md):
    - It is **sRGB, not linear**. The asset pipeline maps `*_Emissive` to the default texture profile (linearTexture:false).
      _MaskMap and _ControlMask go to the linear profile instead. So the map is written with is_data=False.
    - It must be **RGBA**. A 3-channel PNG skips BC compression entirely (CompressBC returns early when channels != 4).
    - It is **emitted color, not a mask**. The shader multiplies it by the EmissiveProperties intensity (0.6, which the bake
      scales x100 to a shader multiplier of 60). Black means "not emissive".

    `lit` maps zone name -> (r, g, b) emitted color. By default it lights the two lens zones: the floodlight lens at full
    cool white, and the amber beacon dimmer, because a beacon is a marker and not a source. Everything else stays black.
    """
    S = quad_size
    A = S * 2
    emi = np.zeros((A, A, 4), dtype=np.float32)
    emi[..., 3] = 1.0

    zones = snow_prop_zones(A)
    if lit is None:
        lit = {
            "floodlens": (0.95, 0.97, 1.00),   # 4000-5000 K, the piste floodlight
            "amber":     (0.55, 0.28, 0.03),   # beacon: present, not a light source
        }
    for zone, rgb in lit.items():
        if zone not in zones:
            continue
        r0, r1, c0, c1 = _rect_px(zones[zone], A)
        for ch in range(3):
            emi[r0:r1, c0:c1, ch] = rgb[ch]

    path = os.path.join(out_dir, f"{name}_Emissive.png")
    _write_png_raw(path, f"{name}_Emissive", emi, A, is_data=False)
    return os.path.basename(path)


def unwrap_into_rect(obj, rect, A, inset_px=3):
    """
    Smart-project one object's UVs and squeeze them into an arbitrary atlas rect.

    This generalizes unwrap_into_quadrant, which stays untouched for lift assets. The inset is in TEXELS, not UV, so a small
    rect on a small atlas keeps a real bilinear guard band, not a sub-pixel one.
    """
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")

    u0, v0, u1, v1 = rect
    ins = inset_px / float(A)
    du = max((u1 - u0) - 2 * ins, 1e-4)
    dv = max((v1 - v0) - 2 * ins, 1e-4)
    for loop_uv in obj.data.uv_layers.active.data:
        u, v = loop_uv.uv
        loop_uv.uv = (u0 + ins + min(max(u, 0.0), 1.0) * du,
                      v0 + ins + min(max(v, 0.0), 1.0) * dv)


def preview_material(obj, asset_dir, name):
    """Attach the BaseColor for preview renders only (remove it again before export_fbx)."""
    mat = bpy.data.materials.new(name + "Preview")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    teximg = nt.nodes.new("ShaderNodeTexImage")
    teximg.image = bpy.data.images.load(os.path.join(asset_dir, f"{name}_BaseColor.png"))
    nt.links.new(teximg.outputs["Color"], bsdf.inputs["Base Color"])
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def unwrap_into_quadrant(obj, quad, texel_density=None):
    """
    Smart-project this single object's UVs, then squeeze them into an atlas quadrant.

    Call it once per part BEFORE joining. The function insets by 2% of the quadrant. This keeps bilinear sampling from
    bleeding across quadrant borders at CS2 view distances.

    Quadrants listed in QUAD_RESERVED_TOP additionally keep that fraction of their top clear. Quad 2's headroom holds the
    crash-pad strip, and a smart-projected window island that landed on orange would be exactly the kind of bug that only
    shows in game.
    """
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")

    uo, vo = QUADS[quad]
    inset = 0.01
    scale = 0.5 - 2 * inset
    v_scale = scale * (1.0 - QUAD_RESERVED_TOP.get(quad, 0.0))
    uv = obj.data.uv_layers.active.data
    for loop_uv in uv:
        u, v = loop_uv.uv
        loop_uv.uv = (uo + inset + u * scale, vo + inset + v * v_scale)


# --------------------------------------------------------------------------------------
# Shading (art pipeline v2, docs/research/art/art-pipeline-v2.md section 3.1)
#
# Every part shipped so far was FLAT-shaded (Blender primitive default). The FBX exporter writes per-corner normals straight
# from the mesh (io_scene_fbx/export_fbx_bin.py:1198), and the game's importer copies them verbatim (FBXImporter.cs ->
# NativeModelImport, whose ImportSettings has CalculateTangents only). So a 16-gon mast reached the game as sixteen lit
# facets. We probed this in probe_smooth.py (Blender 5.2.2): after an FBX round trip, shade_smooth_by_angle leaves 2 normals
# per side vertex (smooth side + cap), and the flat default leaves 3.
# --------------------------------------------------------------------------------------

def shade_by_angle(obj, angle_deg=66.0, weighted=True):
    """
    Smooth-shade every face and mark edges sharper than `angle_deg` as sharp (the wiki's ">= 60 deg auto-smooth" rule). A
    value of 66 keeps 6-segment tubes (60 deg between faces) smooth, while every box edge (90) and ramp break (~68) stays
    crisp. The function uses the ATTRIBUTE route (`sharp_edge` + face smooth flags), not the Smooth-by-Angle modifier.
    Attributes survive `join`, `remove_doubles`, the finish repack and decimation. Modifiers do not.

    `weighted` applies a Weighted Normal modifier (face-area, keep_sharp), so the 0.02 m finish bevels do not smear a shading
    gradient across the big flat faces beside them. Call on the JOINED mesh after remove_doubles, before the finish bake and
    export.
    """
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle_deg), keep_sharp_edges=True)
    if weighted:
        mod = obj.modifiers.new("WeightedNormal", type="WEIGHTED_NORMAL")
        mod.mode = "FACE_AREA"
        mod.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    me = obj.data
    flat = sum(1 for p in me.polygons if not p.use_smooth)
    if flat:
        raise RuntimeError(f"{obj.name}: shade_by_angle left {flat} flat faces")
    attr = me.attributes.get("sharp_edge")
    n_sharp = sum(1 for v in attr.data if v.value) if attr is not None else 0
    print(f"[shade] {obj.name}: {len(me.polygons)} faces smooth, {n_sharp} sharp edges "
          f"(> {angle_deg:.0f} deg){', weighted normals' if weighted else ''}")


# --------------------------------------------------------------------------------------
# Part tags + authored LOD chain (art pipeline v2 section 3.7)
#
# The importer generates NO LODs by default (DECISIONS 77/104). A chain must be decimated from the FINISHED mesh, because the
# finish pass repacks UVs. Only a decimated copy samples the shipped atlas correctly. Collapse decimation alone ruins thin
# tubes and small hardware. So each level first deletes whole parts by name prefix (the snow-prop recipe,
# prop-lod-and-lighting.md section 5). Part names die at `join`. The INT face attribute "part" that tag_parts writes survives
# it, remove_doubles, and the finish pass.
# --------------------------------------------------------------------------------------

PART_TABLE_PROP = "cs2_part_table"


def canonical_order(obj):
    """
    Sort a part's vertices and faces by their coordinates, so element order is a pure function of the geometry. Every
    downstream step is order-dependent (smart_project island ids, the unique pack, the AO bake, collapse decimation). One
    order-unstable builder (bmesh.ops.spin in cs2_kit.sheave, review F1 / DECISIONS 107) was enough to ship different
    textures from two identical builds. Attributes, UV layers and custom props survive the bmesh round trip. Call BEFORE
    shading (sharp_edge would not survive).
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    canonical_order_bmesh(bm)
    bm.to_mesh(obj.data)
    bm.free()


def _rank_sort(seq, keyfn):
    """bmesh's BMElemSeq.sort only takes a NUMERIC key. Compute the lexicographic order in Python, and give bmesh the rank of
    each element instead."""
    seq.index_update()
    seq.ensure_lookup_table()
    order = sorted(range(len(seq)), key=lambda i: keyfn(seq[i]))
    rank = [0] * len(order)
    for r, i in enumerate(order):
        rank[i] = r
    seq.sort(key=lambda e: rank[e.index])
    seq.index_update()
    seq.ensure_lookup_table()


def canonical_order_bmesh(bm):
    """Put the vertices and faces of a bmesh in canonical order, in place (see canonical_order)."""
    _rank_sort(bm.verts, lambda v: (round(v.co.x, 5), round(v.co.y, 5), round(v.co.z, 5)))
    _rank_sort(bm.faces, lambda f: tuple(round(c, 5) for c in f.calc_center_median()))


def tag_parts(parts):
    """
    Stamp every face of every part with the INT face attribute "part" (1-based index into the returned name table). Call
    AFTER bevels (chamfer faces belong to the part) and BEFORE joining. `parts` is the generator's [(obj, quad), ...] list.
    The function first puts every part into canonical element order (see canonical_order), so builds are reproducible.
    """
    table = []
    for i, (obj, _quad) in enumerate(parts, start=1):
        canonical_order(obj)
        me = obj.data
        attr = me.attributes.get("part") or me.attributes.new("part", "INT", "FACE")
        attr.data.foreach_set("value", [i] * len(me.polygons))
        table.append(obj.name)
    return table


def store_part_table(obj, table):
    """Attach the name table to the joined mesh and verify no part lost its tag."""
    attr = obj.data.attributes.get("part")
    if attr is None:
        raise RuntimeError(f"{obj.name}: 'part' face attribute did not survive the join")
    seen = {v.value for v in attr.data}
    missing = [n for i, n in enumerate(table, start=1) if i not in seen]
    if missing:
        raise RuntimeError(f"{obj.name}: parts lost their tag through join: {missing[:8]}")
    obj.data[PART_TABLE_PROP] = json.dumps(table)


def tri_count(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    n = len(bm.faces)
    bm.free()
    return n


def _delete_tagged_faces(obj, table, prefixes):
    """Delete every face whose part name starts with one of `prefixes`, and return the count."""
    if not prefixes:
        return 0
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    layer = bm.faces.layers.int.get("part")
    if layer is None:
        bm.free()
        raise RuntimeError(f"{obj.name}: no 'part' layer for LOD deletion")
    kill = [f for f in bm.faces if table[f[layer] - 1].startswith(prefixes)]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()
    return len(kill)


def _keep_tagged_ids(obj, ids, keep):
    """Keep only (keep=True) or delete (keep=False) the faces whose 'part' id is in `ids` (make_lod_chain `fixed`)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    layer = bm.faces.layers.int.get("part")
    kill = [f for f in bm.faces if (f[layer] in ids) != keep]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def make_lod_chain(obj, name, targets, delete_prefixes, tol=0.25, min_tris=60,
                   shade_angle=66.0, fixed=None):
    """
    Build LOD1..n as decimated copies of the finished `obj`. `targets` are ABSOLUTE triangle counts per level (wiki: LOD1 <
    50 % of LOD0, LOD2 <= 500 for buildings). `delete_prefixes` is one tuple of part-name prefixes per level, deleted
    outright before decimation. The function re-shades each level (decimate drops custom normals), and each level must land
    within `tol` of its target. It returns the LOD objects. The caller exports and removes them. The function reports bounds
    drift, because LOD prefabs carry their own m_Bounds (the top-anchor / chair-height contracts apply to every level).

    `fixed` (T62 round 2, cs2_sheaves.lod_fixed(). None = every line below unchanged) names parts that are NEVER
    collapse-decimated:
    - fixed.parts(table) names them (part ids).
    - Per level, after the prefix deletes, fixed.apply(lod, table, level) rebuilds the surviving ones in that level's form
      (or deletes them) and returns (count, tris).
    - The function splits the rest of the mesh off and decimates it alone to `target`. The targets are then CEILINGS of the
      REST: a rest already at or under its target is not decimated and not tolerance-checked, because a decimator cannot add
      tris. The fixed tris come on top.
    - The function joins the fixed faces back.
    - Custom normals are cleared before the level is re-shaded (sharp marks + angle, as for a decimated level).
    """
    table = json.loads(obj.data[PART_TABLE_PROP])
    fixed_ids = fixed.parts(table) if fixed is not None else set()
    bb0 = [obj.bound_box[0][:], obj.bound_box[6][:]]
    lods = []
    for level, (target, prefixes) in enumerate(zip(targets, delete_prefixes), start=1):
        lod = obj.copy()
        lod.data = obj.data.copy()
        lod.name = f"{name}_LOD{level}"
        lod.data.name = lod.name
        bpy.context.collection.objects.link(lod)
        removed = _delete_tagged_faces(lod, table, tuple(prefixes))
        fixed_obj, fixed_tris = None, 0
        if fixed is not None:
            n_fixed, fixed_tris = fixed.apply(lod, table, level)
            if fixed_tris:
                fixed_obj = lod.copy()
                fixed_obj.data = lod.data.copy()
                fixed_obj.name = f"{name}_LOD{level}_fixed"
                bpy.context.collection.objects.link(fixed_obj)
                _keep_tagged_ids(fixed_obj, fixed_ids, keep=True)
                _keep_tagged_ids(lod, fixed_ids, keep=False)
        before = tri_count(lod)
        ratio = min(1.0, max(min_tris, target) / max(before, 1))
        bpy.ops.object.select_all(action="DESELECT")
        lod.select_set(True)
        bpy.context.view_layer.objects.active = lod
        if ratio < 0.999:
            mod = lod.modifiers.new("Decimate", type="DECIMATE")
            mod.decimate_type = "COLLAPSE"
            mod.ratio = ratio
            mod.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=mod.name)
            # Collapse leaves a few zero-area faces that the FBX round trip drops (review F3: 2702 in the mesh, 2694 in the
            # file). Dissolve them, so the counts we assert and write to the manifest are the counts the game sees.
            bm = bmesh.new()
            bm.from_mesh(lod.data)
            bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges[:])
            # Collinear slivers have long edges and zero area. dissolve_degenerate keeps them, and the FBX round trip drops
            # them. Delete them here, so the count in the manifest is the count in the file.
            slivers = [f for f in bm.faces if f.calc_area() < 1e-8]
            if slivers:
                bmesh.ops.delete(bm, geom=slivers, context="FACES")
            bm.to_mesh(lod.data)
            bm.free()
        rest_tris = None
        if fixed is not None:
            if fixed_obj is not None and ratio >= 0.999:
                # The rest needed no collapse (already under its ceiling). Triangulate it as the collapse would have, so a
                # level that carries rebuilt fixed parts stays tri-only like every decimated level.
                bm = bmesh.new()
                bm.from_mesh(lod.data)
                bmesh.ops.triangulate(bm, faces=bm.faces[:])
                bm.to_mesh(lod.data)
                bm.free()
            rest_tris = tri_count(lod)
            if fixed_obj is not None:
                bpy.ops.object.select_all(action="DESELECT")
                lod.select_set(True)
                fixed_obj.select_set(True)
                bpy.context.view_layer.objects.active = lod
                bpy.ops.object.join()
            if lod.data.has_custom_normals:
                bpy.ops.mesh.customdata_custom_splitnormals_clear()
        shade_by_angle(lod, shade_angle, weighted=False)
        tris = tri_count(lod)
        bb = [lod.bound_box[0][:], lod.bound_box[6][:]]
        drift = max(abs(a - b) for p, q in zip(bb0, bb) for a, b in zip(p, q))
        print(f"[lod] {lod.name}: deleted {removed} faces, {before} -> {tris} tris "
              f"(target {target}, ratio {ratio:.3f}), bounds drift {drift:.3f} m"
              + (f"; fixed parts {fixed_tris} tris on top of the rest's {rest_tris}" if fixed is not None else ""))
        if fixed is not None:
            if before > target and abs(rest_tris - target) > tol * target:
                raise RuntimeError(f"{lod.name}: rest {rest_tris} tris vs target {target} (+-{tol:.0%})")
        elif abs(tris - target) > tol * target:
            raise RuntimeError(f"{lod.name}: {tris} tris vs target {target} (+-{tol:.0%})")
        if drift > 0.05:
            print(f"[lod] WARNING {lod.name}: bounds moved {drift:.3f} m - re-check the "
                  f"anchor/height contracts on this level")
        lods.append(lod)
    return lods


SHARED_MAPS = ("BaseColor", "ControlMask", "MaskMap", "Normal")


def write_settings(out_dir, name, supplied_lods=(), dedicated_texture_lods=(), *,
                   shared_from=None, shared_maps=SHARED_MAPS, surface=None,
                   procedural_animation=False):
    """
    Write settings.json.

    `supplied_lods` is e.g. (1, 2) when LOD FBXs ship alongside. Each gets {"mode": "Skip"}, so the pipeline uses ours. Its
    textures point back at LOD0's through sharedAssets, instead of duplicating multi-megabyte PNGs.

    `dedicated_texture_lods` (L, art-pipeline-v2 3.7 / SkiProbeLodA): levels that ship their OWN `<name>_LOD<k>_<map>.png`
    set beside the FBX get IDENTITY sharedAssets entries (`<name>_LOD2_BaseColor.png` -> itself) instead of the LOD0 repoint.
    This is the ExampleBakedAlphaFence pattern. A level listed here must also be in `supplied_lods`. Default (empty) output
    is byte-identical to before.

    CORRECTED 2026-08-13 (docs/research/art/prop-lod-and-lighting.md §11). This docstring used to claim that an empty tuple
    lets LODPostProcessor generate the chain by edge collapse. It does not. LODPostProcessor CAN edge-collapse. But
    PostProcessSettings' default is {"LOD1": Skip, "LOD2": Skip}, and ShouldExecute returns false the moment a second LOD FBX
    exists. So generation is OPT-IN PER LEVEL and mutually exclusive with authored LODs. An empty tuple therefore means "this
    asset ships LOD0 only and has NO chain". That is the right answer for small props (91-98% of comparable vanilla props
    ship none), but it is not the same statement. SPEC §11.1 and lift_tower's --lods help text carry the same inverted claim
    and need the same correction.

    `shared_from` (fine-detail pass, design C.3 / FS12 SG7) makes the asset a SHARER of another asset's texture set. Default
    None = the output above, byte-identical. The value is a path prefix relative to this asset folder, e.g.
    "../SkiSignSetSchoolB/SkiSignSetSchoolB". The function writes one sharedAssets key per map in `shared_maps` (add
    "Emissive" for a five-map set): `<name>_<Map>.png` -> `<shared_from>_<Map>.png`. AssetImportPipeline.cs:502-509 1.6.2f1
    resolves the target with Path.GetFullPath(Path.Combine(rootPath, target)). Share vs copy is probe F1. A sharer ships LOD0
    only (sign sets, kit props). Passing supplied_lods with shared_from raises. Whether the importer chains a LOD key through
    a cross-asset key is unverified.

    `surface` (orange safety fence, 2026-10-13, design safety-fence.md §4 / C13. Default None = every output above,
    byte-identical) is a dict that the function writes as the LAST key, "Surface", e.g. {"materialTemplate":
    "CurvedHashedCutout", "needsAlphaClip": True, "alphaCutoff": 0.54}. The importer expands it to <name>, <name>_LOD1,
    <name>_LOD2 (Colossal.AssetPipeline 1.6.2f1 SettingsImporter.cs:222-241). An explicit materialTemplate replaces the
    template that the importer would pick (SurfacePostProcessor.cs:156). This is the only way to give an imported LANE mesh a
    Curved* template. An empty or non-dict value raises.

    `procedural_animation` (rig probe AlignRigProbeA, 2026-11-02. Default False = every output above, byte-identical) writes
    "useProceduralAnimation": true as the LAST key, for a skinned FBX (export_fbx_skinned). The key is the public field
    Settings.useProceduralAnimation that JSONImporter fills by name (Colossal.AssetPipeline 1.6.2f1 Settings.cs:387,
    JSONImporter.cs:58). Settings.GetDefault already sets it true (Settings.cs:585), and AssetImportPipeline.ImportSettings
    starts from that default (1.6.0f1 AssetImportPipeline.cs:355, :403-406). So the key is explicit, not required. A
    settings.json with false turns it off.
    """
    if surface is not None and (not isinstance(surface, dict) or not surface):
        raise ValueError(f"write_settings {name}: surface must be a non-empty dict, got {surface!r}")
    settings = {}
    shared = {}
    if shared_from is not None:
        if supplied_lods:
            raise ValueError(f"write_settings {name}: a shared_from sharer ships LOD0 only")
        prefix = str(shared_from).replace("\\", "/")
        if not prefix or prefix.endswith((".png", "/")):
            raise ValueError(f"write_settings {name}: shared_from {shared_from!r} must be a path "
                             f"prefix like '../Owner/Owner'")
        for m in shared_maps:
            shared[f"{name}_{m}.png"] = f"{prefix}_{m}.png"
    dedicated = set(dedicated_texture_lods or ())
    for lod in supplied_lods:
        for m in ("BaseColor", "ControlMask", "MaskMap", "Normal"):
            key = f"{name}_LOD{lod}_{m}.png"
            shared[key] = key if lod in dedicated else f"{name}_{m}.png"
        settings[f"LOD{lod}"] = {"mode": "Skip"}

    if shared:
        settings = {"sharedAssets": shared, **settings}
    if surface is not None:
        settings["Surface"] = dict(surface)
    if procedural_animation:
        settings["useProceduralAnimation"] = True

    path = os.path.join(out_dir, "settings.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(settings, fh, indent=2)
    return path


# --------------------------------------------------------------------------------------
# FBX export
# --------------------------------------------------------------------------------------

# CS2's FBXImporter reads vertex data as CENTIMETERS and ignores the FBX header's unit scale factor. Blender works in meters,
# so everything must be multiplied by 100 on export.
#
# Measured, not guessed: exporting at global_scale=1.0 produced a prefab whose m_Bounds were 0.0194 x 0.0382 x 0.1091 for a
# tower that is 1.94 x 3.82 x 10.91 m, exactly 1/100. The asset imported and placed fine (collision warnings fired), but it
# was a 10.9 cm speck, hence invisible.
CS2_UNIT_SCALE = 100.0


def export_fbx(obj, path, unit_scale=CS2_UNIT_SCALE):
    """
    Export one object using the Unity/CS2 axis convention (-Z forward, Y up), with the unit scale BAKED INTO THE VERTEX DATA.

    The exporter's `global_scale` is not enough. Blender applies it to the node transform, and CS2's importer reads raw mesh
    vertices while it ignores node scale. So the asset still came in at 1/100 with global_scale=100. Baking the scale with
    transform_apply is the only thing the importer cannot ignore.

    The function works on a temporary duplicate, so the caller's object stays in meters for previews, measurement and LOD
    generation.
    """
    orig_name = obj.name

    # CRITICAL: free the name BEFORE duplicating.
    #
    # CS2 locates textures by the mesh name in the FBX: it looks for <meshname>_BaseColor.png and friends. If Blender's
    # duplicate keeps its automatic ".001" suffix, the importer hunts for "<name>.001_BaseColor.png", finds nothing, and
    # silently produces a texture-less 56-byte .Surface. The asset still imports and renders, but it wears the default
    # material.
    #
    # Renaming the source first means the duplicate can take the clean name outright, so no ".001" ever reaches the file. We
    # verified this by string-scanning the FBX: a working official asset contains exactly ONE name (ExampleBakedAlphaFence)
    # and nothing else.
    obj.name = orig_name + "__src"
    obj.data.name = orig_name + "__src"

    tmp = obj.copy()
    tmp.data = obj.data.copy()
    bpy.context.collection.objects.link(tmp)

    tmp.name = orig_name
    tmp.data.name = orig_name

    try:
        bpy.ops.object.select_all(action="DESELECT")
        tmp.select_set(True)
        bpy.context.view_layer.objects.active = tmp

        # Bake whatever transform the caller already had FIRST.
        #
        # The CS2 conversion below ASSIGNS rotation_euler and scale, so it would silently discard any existing object-level
        # rotation. That bit the cable generator: its cylinder carried a 90-degree Y rotation to lie along X, which vanished
        # on export and left an 8 m rope standing vertically. Objects built by joining parts are unaffected (join bakes to
        # identity), which is why the tower never showed the bug.
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

        # Bake BOTH the unit scale and the up-axis conversion into the vertices.
        #
        # CS2 expects Y-up raw vertex data. We confirmed this from the official ExampleAssets, whose ground-standing meshes
        # all run y: 0 .. height while x and z straddle zero. Blender is Z-up, and the exporter's axis_up/axis_forward
        # options only rewrite the NODE transform, which CS2 ignores exactly as it ignores node scale. A rotation of -90 deg
        # about X maps (x, y, z) -> (x, z, -y), which puts Blender's up axis onto Y. It is a proper rotation, so handedness
        # is preserved and normals stay correct.
        tmp.rotation_euler = (math.radians(-90.0), 0.0, 0.0)
        tmp.scale = (unit_scale, unit_scale, unit_scale)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

        bpy.ops.export_scene.fbx(
            filepath=path,
            use_selection=True,
            object_types={"MESH"},
            apply_unit_scale=False,
            global_scale=1.0,
            # Identity for Blender. The conversion is already baked in above.
            axis_forward="Y",
            axis_up="Z",
            use_triangles=True,
            mesh_smooth_type="EDGE",
            use_tspace=True,
            bake_anim=False,
            # STRIP writes bare filenames and does not copy textures into a subfolder. The PNGs already sit beside the FBX,
            # exactly as the CS2 folder contract expects.
            path_mode="STRIP",
        )
    finally:
        mesh = tmp.data
        bpy.data.objects.remove(tmp, do_unlink=True)
        bpy.data.meshes.remove(mesh)
        obj.name = orig_name
        obj.data.name = orig_name


# --------------------------------------------------------------------------------------
# Skinned FBX export (procedural bones, rig probe AlignRigProbeA, 2026-11-02)
#
# What the game's importer needs (1.6.0f1 Game.AssetPipeline/AssetImportPipeline.cs, 1.6.2f1 Colossal.AssetPipeline decompile
# under tools/decompile/1.6.2f1-colossal/):
# - The importer reads only mesh 0 of the FBX (FBXImporter.cs:137, default Options.SplitSubmeshes). So the FBX holds one
#   skinned mesh object beside its armature.
# - BlendIndices + BlendWeight + a root bone + bind poses are required. Without them the importer creates no
#   ProceduralAnimationProperties (AssetImportPipeline.cs:2331-2358, :2377-2392). The managed side carries 4 influences per
#   vertex (FBXImporter.cs:249-250, :358-359).
# - Bones = the skin's cluster bones: name, local position / rotation / scale, parent, bind pose (FBXImporter.cs:393-403 ->
#   AssetImportPipeline.cs:2393-2406). Blender writes a cluster for every bone, weighted or not (io_scene_fbx
#   export_fbx_bin.py, "rest pose data").
# - LOD1+ get a COPY of LOD0's component (SetupLODs :2210-2217). SkinPostProcessor remaps each LOD's indices onto the
#   previous LOD's bones BY NAME (SkinPostProcessor.cs:168-187). So every LOD is skinned to the same armature.
# - There is no animation take. An FBX clip turns the bones into PlaybackLayer0 and writes an AnimationAsset
#   (AssetImportPipeline.cs:1977-2010, FBXImporter.cs:494-563). The export therefore uses bake_anim=False.
# --------------------------------------------------------------------------------------

def check_skin_weights(obj, armature, max_influences=4, tol=1e-4):
    """
    Raise unless every vertex of `obj` is weighted to 1..max_influences bones of `armature` with weights summing to 1
    (+-tol), and every vertex group names a bone. Return {bone: vertex count with weight > 0}.
    """
    bones = {b.name for b in armature.data.bones}
    names = {vg.index: vg.name for vg in obj.vertex_groups}
    stray = sorted(n for n in names.values() if n not in bones)
    if stray:
        raise RuntimeError(f"{obj.name}: vertex groups {stray} name no bone of {armature.name}")
    per_bone = {b: 0 for b in sorted(bones)}
    bad = []
    for v in obj.data.vertices:
        ws = [(names[g.group], g.weight) for g in v.groups if g.weight > 0.0]
        total = sum(w for _, w in ws)
        if not ws or len(ws) > max_influences or abs(total - 1.0) > tol:
            bad.append((v.index, len(ws), round(total, 5)))
        for b, _ in ws:
            per_bone[b] += 1
    if bad:
        raise RuntimeError(f"{obj.name}: {len(bad)} vertices not weighted 1..{max_influences} bones "
                           f"summing to 1, first {bad[:5]}")
    return per_bone


def export_fbx_skinned(obj, armature, path, unit_scale=CS2_UNIT_SCALE):
    """
    export_fbx for a mesh skinned to an armature. It uses the same frame for the mesh AND the armature, so the bind poses
    match the baked vertices. The frame is cm, Y up, every transform baked (the game reads raw vertices and ignores node
    transforms). export_fbx itself is unchanged.

    Authoring rule for an identity bone rest rotation in the game: the bone points UP (+Z) with roll 0. The -90 deg X bake
    turns it onto +Y with its X axis on +X. That is the identity bone frame (Blender bones run along their local Y, and
    primary/secondary bone axes Y/X write them uncorrected). The game negates FBX x on import (PIPELINE.md frame rule T:
    position x -> -x, quaternion (qx, qy, qz, qw) -> (qx, -qy, -qz, qw)). That leaves identity alone.

    The caller's mesh needs vertex groups named after the bones (check_skin_weights runs here). A parent or an Armature
    modifier on it is ignored and rebuilt on the temporary copies.
    """
    check_skin_weights(obj, armature)
    names = (obj.name, obj.data.name, armature.name, armature.data.name)
    obj.name = obj.data.name = names[0] + "__src"
    armature.name = armature.data.name = names[2] + "__src"
    tmp = obj.copy()
    tmp.data = obj.data.copy()
    arm = armature.copy()
    arm.data = armature.data.copy()
    for o in (tmp, arm):
        bpy.context.collection.objects.link(o)
    tmp.name = tmp.data.name = names[0]
    arm.name = arm.data.name = names[2]
    try:
        if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        for m in list(tmp.modifiers):
            tmp.modifiers.remove(m)
        for o in (tmp, arm):
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
            o.animation_data_clear()
        for pb in arm.pose.bones:
            pb.matrix_basis.identity()
        # bake each object's own transform, then the Y-up / cm conversion (export_fbx's two steps)
        for o in (tmp, arm):
            bpy.ops.object.select_all(action="DESELECT")
            o.select_set(True)
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
            o.rotation_euler = (math.radians(-90.0), 0.0, 0.0)
            o.scale = (unit_scale, unit_scale, unit_scale)
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        tmp.parent = arm
        tmp.matrix_parent_inverse.identity()
        mod = tmp.modifiers.new("Armature", type="ARMATURE")
        mod.object = arm
        bpy.ops.object.select_all(action="DESELECT")
        tmp.select_set(True)
        arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.export_scene.fbx(
            filepath=path,
            use_selection=True,
            object_types={"MESH", "ARMATURE"},
            apply_unit_scale=False,
            global_scale=1.0,
            axis_forward="Y",
            axis_up="Z",
            use_triangles=True,
            mesh_smooth_type="EDGE",
            use_tspace=True,
            add_leaf_bones=False,
            primary_bone_axis="Y",
            secondary_bone_axis="X",
            use_armature_deform_only=False,
            armature_nodetype="NULL",
            bake_anim=False,
            path_mode="STRIP",
        )
    finally:
        for o in (tmp, arm):
            data = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            else:
                bpy.data.armatures.remove(data)
        obj.name, obj.data.name = names[0], names[1]
        armature.name, armature.data.name = names[2], names[3]


# -- sibling exports (an asset on another asset's texture set, DECISIONS 592 c) ----------------------------------------------

SIBLING_MAPS = ("BaseColor", "ControlMask", "MaskMap", "Normal")


def deployed_asset_names(repo):
    """Return every asset / prefab name the game has seen: the folder names under gamedata-mirror/ImportedData ('<Name> Mesh'
    folded to <Name>) and the stems of its files (up to the first '.' or '_')."""
    import re
    root = os.path.join(repo, "gamedata-mirror", "ImportedData")
    names = set()
    for _dp, dns, fns in os.walk(root):
        for d in dns:
            names.add(d[:-len(" Mesh")] if d.endswith(" Mesh") else d)
        for f in fns:
            names.add(re.split(r"[._]", f, maxsplit=1)[0])
    return names


def assert_new_asset_name(name, repo):
    """Use a new generation letter per import round. Never export under a name that the game already imported (the Editor mints
    a new mesh CID per import, and prefabs repoint by CID)."""
    if name in deployed_asset_names(repo):
        raise ValueError(f"{name} is already deployed (gamedata-mirror/ImportedData): pick a new generation letter")


def sibling_settings(asset_dir, name, owner, levels, maps=SIBLING_MAPS):
    """Write the settings.json of an asset that has no PNG of its own and shares its OWNER's LOD0 set. The owner is a sibling
    folder (the family-folder rule, PIPELINE section 4). The file has these entries:
    - Every map key of LOD0 and of each supplied LOD points STRAIGHT at `../<owner>/<owner>_<Map>.png` (no key chains through
      another key).
    - LOD1..levels are Skip.
    - useProceduralAnimation is set (the skinned towers).

    One set of one size serves every level: the owner's LOD0 maps. The function refuses when the owner's maps are missing or
    differ in size (never mix map sizes in one texture set). It returns the path."""
    odir = os.path.join(os.path.dirname(asset_dir), owner)
    miss = [m for m in maps if not os.path.isfile(os.path.join(odir, f"{owner}_{m}.png"))]
    if miss:
        raise FileNotFoundError(f"{name}: the owner's maps {miss} are missing in {odir} (the import resolves them there)")
    sizes = {}
    for m in maps:
        with open(os.path.join(odir, f"{owner}_{m}.png"), "rb") as fh:
            head = fh.read(24)
        sizes[m] = (int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big"))
    if len(set(sizes.values())) != 1:
        raise ValueError(f"{name}: the owner {owner}'s maps differ in size {sizes}: never mix map sizes in one set")
    shared = {}
    for lod in range(levels + 1):
        for m in maps:
            shared[f"{name}_{m}.png" if lod == 0 else f"{name}_LOD{lod}_{m}.png"] = f"../{owner}/{owner}_{m}.png"
    settings = {"sharedAssets": shared}
    for lod in range(1, levels + 1):
        settings[f"LOD{lod}"] = {"mode": "Skip"}
    settings["useProceduralAnimation"] = True
    path = os.path.join(asset_dir, "settings.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(settings, fh, indent=2)
    return path


def clean_asset_folder(asset_dir, name):
    """Call before a re-export. Remove this asset's own files (`<name>*`, settings.json). Never remove anything else."""
    for f in os.listdir(asset_dir):
        p = os.path.join(asset_dir, f)
        if os.path.isfile(p) and (f.startswith(name) or f == "settings.json"):
            os.remove(p)
