"""
Shared helpers for producing CS2-importable asset folders from Blender.

The contract was reverse-engineered from the official examples shipped with the game at
    Cities2_Data/Content/Game/.ModdingToolchain/ExampleAssets/

Every asset is a FOLDER, not a loose FBX. The importer expects:

    <Name>/
        <Name>.fbx                  LOD0 mesh
        <Name>_BaseColor.png        albedo            (sRGB)
        <Name>_Normal.png           tangent normals   (linear)
        <Name>_MaskMap.png          packed PBR        (linear)
        <Name>_ControlMask.png      colour variation  (linear)
        settings.json
        [<Name>_LOD1.fbx, <Name>_LOD2.fbx ...]        optional, supplied LODs
        [<Name>_Gls.fbx / _Win.fbx / _Wat.fbx]        optional material sub-meshes

`settings.json` carries a `sharedAssets` map (texture filename -> the file it should actually
reuse) plus a per-LOD `{"mode": "Skip"}` entry meaning "I supplied this level, don't generate it".
Omit the LOD entries entirely and LODPostProcessor generates them by edge collapse.

Importing a bare .fbx with none of this produces no log output at all - the importer never
reaches the file. That is what happened on the first attempt.
"""

import bpy
import json
import math
import os

import numpy as np


# --------------------------------------------------------------------------------------
# Orientation
# --------------------------------------------------------------------------------------

def orient_for_net(obj):
    """
    Rotate an asset authored with +X = travel so it matches CS2's network convention.

    CS2 is Unity-handed: X = lateral, Y = up, Z = forward/along the network. The Blender ->
    CS2 bake in export_fbx maps (x, y, z) -> (x, z, -y), so Blender's X stays lateral and
    Blender's -Y becomes CS2's +Z. An asset built with travel along Blender X therefore lands
    rotated 90 degrees.

    Symptoms that gave this away: lane meshes never stretched (their long axis was X, but the
    lane deforms along Z, so nothing scaled) and they sat visibly ACROSS the drawn line.

    Rotating -90 degrees about Blender Z maps (x, y, z) -> (y, -x, z), sending travel from +X
    to -Y and the lateral axis from Y to X. After the export bake that becomes travel = +Z,
    lateral = X, up = Y. Correct.

    Call at the end of a build function, before export. Generators keep authoring in the more
    readable "+X is forward" frame.
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

    CS2 pairs textures to a mesh by parsing `<name>_<suffix>.png`, so the underscore is a
    reserved separator (`_BaseColor`, `_MaskMap`, `_LOD1`, `_Gls`). An underscore inside the
    base name makes the split ambiguous and the importer silently binds nothing: the asset
    imports, places and renders wearing CS2's default material, producing a 56-byte .Surface
    and zero .Texture assets.

    Proven side by side in one scene: "SkiTowerRed" -> 171-byte Surface + 4 textures, renders
    red. "SkiTower_RED", same generator and same geometry -> 56-byte Surface, no textures,
    renders default grey.

    Consistent with all shipped content - no official ExampleAssets name and no name in the
    Modular Golf Course Pack contains an underscore.

    Failing here is deliberate: this bug costs an entire import/inspect cycle to notice, and it
    looks like a material problem rather than a naming one.
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
    Write a float RGBA array as PNG via Blender's image API - avoids a PIL dependency,
    since Blender ships its own Python.

    `rgba` is (h, w, 4) float in 0..1. Blender's pixel buffer is bottom-up, so flip.
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

    MaskMap packing, established by measuring the official ExampleAssets rather than assuming
    HDRP's layout:

        R = metallic     fence (chain link) mean 0.772 / median 1.0
                         firehouse (brick)  mean 0.040 / median 0.0
        G = 0            EXACTLY zero in every official MaskMap
        B = 0            EXACTLY zero in every official MaskMap
        A = smoothness   fence 0.706 (shiny metal), firehouse 0.276 (matte brick)

    G and B being uniformly zero across every shipped example is the useful signal. An earlier
    version set G = 1.0 on the assumption it was ambient occlusion, and the towers rendered
    with a strong blue sky reflection. Whatever G drives, official content leaves it at zero,
    so we do too.

    ControlMask alpha is zero in every official asset (an earlier version wrongly set 1.0).

    RECOLOURING (`control_rgb`): the ControlMask RGB channels are REGION MASKS for the game's
    custom-colour system - measured from ExampleColorMaskFirehouse, whose mask has three hard
    binary regions (R ~32% of texels, G ~10%, B ~20%) over a desaturated BaseColor. Each channel
    marks the texels tinted by one of the three colour slots. `control_rgb=(1,0,0)` makes the
    whole surface recolourable via slot 1 - the right default for our uniform procedural
    textures. Keep `base_rgb` greyish on recolourable assets so the tint reads true.
    """
    os.makedirs(out_dir, exist_ok=True)
    rng = np.random.default_rng(seed)

    # --- BaseColor: flat steel with a little grain so it isn't plastic-looking
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

    # --- ControlMask: RGB = recolour region masks (see docstring), alpha always zero
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

    Without this the exported FBX carries NO material at all, and CS2 then has nothing to bind
    the sibling PNGs to. The symptom is subtle and easy to misread: the asset imports, renders,
    and looks *plausible* - but it is wearing CS2's default surface. Two builds with completely
    different MaskMaps produced byte-identical 56-byte .Surface assets and no .Texture assets
    whatsoever, which is how this was caught.

    Compare a working asset: the official ExampleBakedAlphaFence.fbx contains a "Texture/"
    reference; ours contained no material or texture strings at all.

    Must be called AFTER make_texture_set, since it loads the PNGs it writes.
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

    # MaskMap and ControlMask are consumed by CS2's own shader, not Blender's. They are loaded
    # so the exporter records them in the FBX, but left unconnected - wiring them into a
    # Principled BSDF would misrepresent them.
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
    Load one map of an ambientCG set (e.g. suffix='Color') as (size,size,4) float RGBA.
    Returns None if the set has no such map. Orientation is irrelevant here: every consumer
    treats texels independently, and read/write both use Blender's bottom-up buffer.
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

    quad_sets: {quad_index: path_to_ambientCG_set_dir} - quads 0..3 (BL, BR, TL, TR).
               A missing quad is filled with mid-grey.
    control_quads: per-quad ControlMask R value (recolour slot 1 coverage). Painted parts
               get 1.0 (recolourable), raw concrete/steel get 0.0.
    post:      optional callback (base, nrm, msk, ctl, atlas_size) invoked after the quads
               are painted but before the PNGs are written - lets one asset stamp bespoke
               detail (e.g. the terminal's painted tension-scale numbers) into ITS copy of
               the atlas without touching the shared recipe every other lift asset uses.

    CS2 packing (established earlier from official assets): MaskMap R = metallic,
    A = smoothness = 1 - roughness; G/B pinned to zero. Normal uses the GL convention.
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
            # Photo set with adjustments: {"set": path, "tint": 0.6} multiplies the albedo -
            # ambientCG's concrete renders near-white under CS2's sun, tint grounds it.
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
    Draw panel seams + bolts into a quad's slices: darkened seam lines in BaseColor and
    height-derived grooves in the Normal map. `base_sl`/`nrm_sl`/`msk_sl` are (S,S,4) views.

    Normal-map math: build a height field (0 = flat, negative = groove), then convert with
    finite differences. Normal = normalize(-dh/dx, -dh/dy, 1) mapped into 0..1 RGB. This is
    what makes seams read as real grooves under game lighting without any triangles.
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
# Recolour contract (K generation)
#
# The vanilla recolour UI MULTIPLIES the picked colour against the albedo, so recolourable
# regions must keep LIGHT, DESATURATED albedo or every tint collapses toward black (the
# J-round bug: picker colours barely read against the near-black steel). Vanilla's own
# convention: masked regions ship light-neutral albedo and the DEFAULT look is supplied by
# the prefab's default colour VariationSet. The authored default paint colours therefore
# move prefab-side; they are recorded here as the single source of truth:
# --------------------------------------------------------------------------------------

STEEL_DEFAULT_RGB = (0.20, 0.21, 0.22)   # the dark Pali steel paint - slot 1 prefab default
RECOLOR_ALBEDO = 0.80                    # light neutral gray under every recolour mask


def lift_atlas_quads(tex_root):
    """
    The ONE standard atlas for every lift asset (stations, towers, chairs), so the whole
    system shares a palette and every painted part answers to the same recolour slot:

        0 concrete   (pads, plinths, ramps)     photo, tinted down    not recolourable
        1 galvanized (sheaves, grips, ladders)  photo Metal032        not recolourable
        2 dark glass (liftie hut window band)   procedural            not recolourable
          - top of quad 2 is RESERVED for the crash-pad strip, see PAD_* below
        3 painted    (masts, frames, chairs)    procedural light-neutral steel,
                                                RECOLOURABLE (slot 1)

    K: quad 3 albedo is RECOLOR_ALBEDO gray, not the dark Pali paint - recolour tints
    multiply, so the region must stay light for picker colours to read true. The grain,
    panel seams and grime detail are kept (they multiply, so they survive any tint). The
    shipped dark look comes from STEEL_DEFAULT_RGB in each prefab's default VariationSet.

    Returns (quad_sets, control_quads) ready for make_texture_set_atlas.
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
# The tower mast pads are RECOLOURABLE ON THEIR OWN SLOT (ControlMask G = slot 2),
# default orange-red, so a player can restyle pads independently of the black steel
# (slot 1). The pad material lives in a horizontal strip reserved out of the top of the
# GLASS quadrant (quad 2 - procedural, mostly headroom). Both halves of the contract are
# authored HERE: paint_pad_strip() writes the texels, and lift_tower reads CRASHPAD +
# PAD_* to build the shell and hand-set its UVs into the strip (set_plate_uvs pattern).
#
# unwrap_into_quadrant knows the reservation (QUAD_RESERVED_TOP) and squeezes quad-2
# smart projections below the strip, so hut window islands can never land on orange.
# --------------------------------------------------------------------------------------

# Strip extent in ATLAS UV space (quad 2 spans u 0..0.5, v 0.5..1.0)
PAD_U0, PAD_U1 = 0.010, 0.490
PAD_V0, PAD_V1 = 0.945, 0.9995

# Shell geometry contract, shared with lift_tower.build_crash_pad. The paint step needs
# it too: the strip's texel aspect is wildly anisotropic (wide arc, short height), so the
# warning triangle must be pre-stretched horizontally to come out equilateral on the pad.
CRASHPAD = {
    "radius": 0.56,        # outer shell radius (m)
    "thickness": 0.06,     # shell wall thickness
    "z0": 0.15,            # bottom of pad above tower base
    "z1": 1.90,            # top of pad - skier impact height
    "arc_deg": 270.0,      # open-backed: the gap faces downhill (-X)
    "segments": 14,
}

# Fraction of the top of a quadrant that unwrap_into_quadrant must keep clear.
# 0.12 of quad 2 = atlas v 0.94..1.0, which contains the pad strip (0.945+).
# 0.12 of quad 0 = atlas v 0.44..0.50: the tower FOUNDATION-PIER band (M generation,
# lift_tower.PIER_*). The pier band must NOT hug the quad-0/quad-2 boundary: quad 2's
# glass is near-black, and any strip touching v 0.5 gets that black averaged into it
# at the first mip level (found the hard way - the pier top rendered pitch black).
# The band therefore ends at 0.4995 as PAINTED padding while geometry only maps up
# to 0.4900, keeping ~20 texels of same-tone insulation against the glass.
QUAD_RESERVED_TOP = {2: 0.12, 0: 0.12}

PAD_RGB = (0.85, 0.25, 0.10)          # default orange-red - slot 2 PREFAB default (K:
                                      # applied via the default VariationSet, not albedo)
PAD_SMOOTH = 0.25


def paint_pad_strip(base, nrm, msk, ctl, A):
    """
    Paint the crash-pad material strip + white warning triangle into an atlas.
    Signature matches make_texture_set_atlas `post` hooks so callers can compose it.

    ControlMask: G=1 (recolour slot 2) across the strip, EXCEPT under the triangle
    markings, which are painted symbols and must not tint (same rule as the terminal's
    tension-scale digits).

    K: the strip albedo is light-neutral RECOLOR_ALBEDO gray, not PAD_RGB - slot-2
    tints multiply, so the orange moves to the prefab default VariationSet (PAD_RGB is
    kept above as its source of truth). The warning triangle/exclamation stamps stay
    exactly as authored: they are masked out of G, so they never tint.
    """
    rng = np.random.default_rng(23)
    c0, c1 = int(PAD_U0 * A), int(PAD_U1 * A)
    r0, r1 = int(PAD_V0 * A), int(PAD_V1 * A)
    h, w = r1 - r0, c1 - c0

    # Matte vinyl: flat colour with faint grain, dielectric, low smoothness
    grain = rng.normal(0.0, 0.012, (h, w, 1)).astype(np.float32)
    for ch in range(3):
        base[r0:r1, c0:c1, ch] = np.clip(RECOLOR_ALBEDO + grain[..., 0], 0.0, 1.0)
    base[r0:r1, c0:c1, 3] = 1.0
    msk[r0:r1, c0:c1, 0] = 0.0
    msk[r0:r1, c0:c1, 3] = PAD_SMOOTH
    ctl[r0:r1, c0:c1, 0] = 0.0
    ctl[r0:r1, c0:c1, 1] = 1.0                      # recolour slot 2

    # Warning triangle on the uphill face. The outer wall maps arc -> u and height -> v,
    # so horizontal texels/metre exceed vertical by `aniso`; stretch width accordingly.
    arc_len = math.radians(CRASHPAD["arc_deg"]) * CRASHPAD["radius"]
    pad_h = CRASHPAD["z1"] - CRASHPAD["z0"]
    aniso = (w / arc_len) / (h / pad_h)
    T = int(h * 0.62)                               # triangle height in texels
    W = int(T * 1.12 * aniso)                       # pre-stretched width
    cx = c0 + w // 2                                # strip centre = arc centre = +X face
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


def preview_material(obj, asset_dir, name):
    """Attach the BaseColor for preview renders only (strip again before export_fbx)."""
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

    Call per part BEFORE joining. Inset by 2% of the quadrant keeps bilinear sampling from
    bleeding across quadrant borders at CS2 view distances.

    Quadrants listed in QUAD_RESERVED_TOP additionally keep that fraction of their top
    clear - quad 2's headroom holds the crash-pad strip, and a smart-projected window
    island landing on orange would be exactly the kind of bug that only shows in game.
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

def write_settings(out_dir, name, supplied_lods=()):
    """
    Write settings.json.

    `supplied_lods` is e.g. (1, 2) when LOD FBXs are shipped alongside; each gets
    {"mode": "Skip"} so the pipeline uses ours, and its textures are pointed back at LOD0's
    via sharedAssets rather than duplicating multi-megabyte PNGs.

    Empty tuple (the default) means: ship LOD0 only and let LODPostProcessor generate the
    rest by edge collapse.
    """
    settings = {}
    shared = {}
    for lod in supplied_lods:
        for m in ("BaseColor", "ControlMask", "MaskMap", "Normal"):
            shared[f"{name}_LOD{lod}_{m}.png"] = f"{name}_{m}.png"
        settings[f"LOD{lod}"] = {"mode": "Skip"}

    if shared:
        settings = {"sharedAssets": shared, **settings}

    path = os.path.join(out_dir, "settings.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(settings, fh, indent=2)
    return path


# --------------------------------------------------------------------------------------
# FBX export
# --------------------------------------------------------------------------------------

# CS2's FBXImporter reads vertex data as CENTIMETRES and ignores the FBX header's unit
# scale factor. Blender works in metres, so everything must be multiplied by 100 on export.
#
# Measured, not guessed: exporting at global_scale=1.0 produced a prefab whose m_Bounds were
# 0.0194 x 0.0382 x 0.1091 for a tower that is 1.94 x 3.82 x 10.91 m - exactly 1/100. The
# asset imported and placed fine (collision warnings fired) but was a 10.9 cm speck, hence
# invisible.
CS2_UNIT_SCALE = 100.0


def export_fbx(obj, path, unit_scale=CS2_UNIT_SCALE):
    """
    Export one object using the Unity/CS2 axis convention (-Z forward, Y up), with the unit
    scale BAKED INTO THE VERTEX DATA.

    The exporter's `global_scale` is not enough. Blender applies it to the node transform, and
    CS2's importer reads raw mesh vertices while ignoring node scale - so the asset still came
    in at 1/100 with global_scale=100. Baking the scale with transform_apply is the only thing
    the importer cannot ignore.

    Work happens on a temporary duplicate so the caller's object stays in metres for previews,
    measurement and LOD generation.
    """
    orig_name = obj.name

    # CRITICAL: free the name BEFORE duplicating.
    #
    # CS2 locates textures by the mesh name in the FBX - it looks for <meshname>_BaseColor.png
    # and friends. If Blender's duplicate keeps its automatic ".001" suffix, the importer hunts
    # for "<name>.001_BaseColor.png", finds nothing, and silently produces a texture-less
    # 56-byte .Surface. The asset still imports and renders, just wearing the default material.
    #
    # Renaming the source first means the duplicate can take the clean name outright, so no
    # ".001" ever reaches the file. Verified by string-scanning the FBX: a working official
    # asset contains exactly ONE name (ExampleBakedAlphaFence) and nothing else.
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
        # The CS2 conversion below ASSIGNS rotation_euler and scale, so any existing object-level
        # rotation would be silently discarded. That bit the cable generator: its cylinder carried
        # a 90-degree Y rotation to lie along X, which vanished on export and left an 8 m rope
        # standing vertically. Objects built by joining parts are unaffected (join bakes to
        # identity), which is why the tower never showed the bug.
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

        # Bake BOTH the unit scale and the up-axis conversion into the vertices.
        #
        # CS2 expects Y-up raw vertex data - confirmed from the official ExampleAssets, whose
        # ground-standing meshes all run y: 0 .. height while x and z straddle zero. Blender is
        # Z-up, and the exporter's axis_up/axis_forward options only rewrite the NODE transform,
        # which CS2 ignores exactly as it ignores node scale. Rotating -90 deg about X maps
        # (x, y, z) -> (x, z, -y), putting Blender's up axis onto Y. It is a proper rotation, so
        # handedness is preserved and normals stay correct.
        tmp.rotation_euler = (math.radians(-90.0), 0.0, 0.0)
        tmp.scale = (unit_scale, unit_scale, unit_scale)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

        bpy.ops.export_scene.fbx(
            filepath=path,
            use_selection=True,
            object_types={"MESH"},
            apply_unit_scale=False,
            global_scale=1.0,
            # Identity for Blender - the conversion is already baked in above.
            axis_forward="Y",
            axis_up="Z",
            use_triangles=True,
            mesh_smooth_type="EDGE",
            use_tspace=True,
            bake_anim=False,
            # STRIP writes bare filenames rather than copying textures into a subfolder -
            # the PNGs already sit beside the FBX exactly as CS2's folder contract expects.
            path_mode="STRIP",
        )
    finally:
        mesh = tmp.data
        bpy.data.objects.remove(tmp, do_unlink=True)
        bpy.data.meshes.remove(mesh)
        obj.name = orig_name
        obj.data.name = orig_name
