"""
cs2_preview.py - ONE preview rig for every CS2 asset (art-pipeline-v2 section 3.10).

Contract
--------
    preview(obj, asset_dir, name, out_dir,
            modes=("pbr", "dusk", "normals", "lod", "density", "ab"),
            views=None, lods=None, prev_generation=None, tint=None, ground=True,
            resolution=(1300, 1000), samples=None) -> [written PNG paths]

    lod_switch_table(bbox_size_xyz, tris_by_level, lod_factor=652.0, area=None) -> dict

`obj` is the FINISHED LOD0 object in meters (Blender Z-up, already net-oriented), `asset_dir`
holds the four shipped maps `<name>_{BaseColor,Normal,MaskMap,ControlMask}.png` (optionally
`<name>_Emissive.png`). The rig never touches the export path: everything it adds to the
scene (camera, sun, ground, world, materials, images, color attributes, imported previous-
generation meshes) is removed again and the object's material slots and every object's
`hide_render` flag are restored, so a generator can call it right before `export_fbx`.

Modes (file names are `<name>_<mode>_<view>.png` unless stated):
  pbr      Nishita sky (5.2: sky_type MULTIPLE_SCATTERING, no disc) sun elevation 55 deg,
           rotation 135 deg, altitude 1800 m + a matched SUN lamp (4.0), AgX view transform,
           45 deg vertical fov, 60 m ground plane albedo 0.85, material built from the four
           maps: BaseColor x mix(1, STEEL_DEFAULT_RGB, ctl.R) x mix(1, PAD_RGB, ctl.G)
           x mix(1, tint["B"] or 1, ctl.B) (multiplied in LINEAR space - the picker tint
           multiplies), Normal -> NormalMap, MaskMap R -> Metallic, 1 - A -> Roughness,
           G -> Coat Weight, `_Emissive` -> Emission strength 5.
  dusk     the same rig at sun elevation 4 deg / lamp 0.4 (the frame that shows emissive).
  normals  Workbench MATCAP, single color: faceting, bevels and shading seams only.
  lod      for every LOD k: LOD0 and LODk rendered as silhouettes from the same camera at
           the predicted switch distance d_k (lod_switch_table) and at d_k / 2; written as
           `<name>_lod<k>_<d>m_xor<pct>pct.png` = [LOD0 | LODk | XOR] cropped to the union
           silhouette, the XOR percentage (xor / union) in the file name.
  density  per-face shipped px/m (layer "UVUnique" if present, else the active layer) on a
           log color ramp (blue 25 - cyan 50 - green 100 - yellow 200 - red 400 px/m)
           multiplied by a 4-texel checker, rendered unlit; a per-part table is printed.
  ab       `prev_generation` (an asset FOLDER holding `<prev>.fbx` + its maps, or the FBX
           path) is re-imported (cm Y-up -> meters Z-up, as cs2_asset.export_fbx bakes it),
           given the same pbr material recipe from ITS maps and rendered with the camera
           framed on the NEW asset; written side by side as `<name>_ab_<view>.png` = [new | prev].

Engine: EEVEE (Blender 5.x identifier BLENDER_EEVEE = EEVEE-Next) for pbr/dusk/density/ab;
if a render raises, the rig falls back to Cycles CPU 16 samples (as lift_tower.pick_engine
picks the best engine present). Workbench for normals and the lod silhouettes.

Absolute match to CS2 lighting is unverified (documented in the plan); the value is an
identical rig across rounds. The sun lamp aims along the sky node's `sun_direction`, so
lamp shadows and the sky gradient agree.
"""

import math
import os
import sys

import bpy
import mathutils
import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset   # noqa: E402
import lift_tower  # noqa: E402  (frame_camera / world_bounds / pick_engine)


# --------------------------------------------------------------------------------------
# Constants
# --------------------------------------------------------------------------------------

#: (view name, camera direction) - lift_terminal.render_previews' three views: the object
#: is net-oriented, so the lift line runs along +Y and the catwalk side is +X.
DEFAULT_VIEWS = (
    ("three_quarter", (1.0, -1.2, 0.45)),
    ("front",         (0.85, 0.9, 0.28)),
    ("line_mouth",    (0.1, 1.0, 0.18)),
)

SKY_ROTATION_DEG = 135.0
SKY_ALTITUDE_M = 1800.0
#: Background strength on the Nishita sky. Blender's sky is physical (noon sky-only ground
#: radiance ~2.4 at strength 1 on a 0.5 gray) while the SUN lamp uses the 4.0 W/m^2 preview
#: convention (0.64 on the same ground): 0.07 puts diffuse sky at ~26 % of direct sun on a
#: horizontal surface, the real clear-noon ratio. Measured 2026-09-22, Blender 5.2.2.
SKY_STRENGTH = 0.07
NOON = dict(elevation_deg=55.0, sun_energy=4.0)
DUSK = dict(elevation_deg=4.0, sun_energy=0.4)
GROUND_SIZE_M = 60.0
GROUND_ALBEDO = 0.85
VERTICAL_FOV_DEG = 45.0
EMISSIVE_STRENGTH = 5.0
EEVEE_SAMPLES = 32
CYCLES_FALLBACK_SAMPLES = 16

#: density ramp stops (px/m, rgb) - log-spaced, blue = starved, red = wasteful
DENSITY_RAMP = (
    (25.0,  (0.10, 0.20, 0.90)),
    (50.0,  (0.10, 0.80, 0.90)),
    (100.0, (0.15, 0.80, 0.15)),
    (200.0, (0.95, 0.85, 0.10)),
    (400.0, (0.90, 0.15, 0.10)),
)
DENSITY_CHECKER_PX = 4
#: lod composites are upscaled (nearest) to at least this height; the silhouette pixel
#: counts in the console line are measured BEFORE the upscale.
LOD_MIN_PANEL_PX = 256

_PREVIEW_TAG = "cs2_preview_rig"


# --------------------------------------------------------------------------------------
# LOD switch distances
# --------------------------------------------------------------------------------------

def lod_switch_table(bbox_size_xyz, tris_by_level, lod_factor=652.0, area=None):
    """
    Predicted LOD switch distances (plan 3.7; RequiredBatchesSystem.cs:671-698,
    RenderingUtils.cs:142, thin-mesh bias MeshSystem.cs:505-511).

        csum  = sx + sy + sz
        bias  = -log2(sqrt(clamp(area / (2 * (sx*sy + sy*sz + sz*sx)), 1e-6, 1)))  (0 without area)
        d_k   = lod_factor * 2^-bias * csum * 0.57735 / sqrt(3 * tris_k)
        cull  = lod_factor * csum / 3 * 2^-bias

    `tris_by_level` lists LOD0..LODn triangle counts; `area` is the mesh surface area in m^2
    (optional: the thin-mesh term needs it; a box-like building has bias ~0). lod_factor 652
    assumes the 45 deg fov and LOD Medium zoomed out (unverified - proof = SkiProbeLodA pop
    distances); halve at ground zoom.

    Returns {"csum", "bias", "distances": [d_0..d_n], "switch": {k: d_k for k >= 1}, "cull"}.
    LODk is used from `switch[k]` outward; LOD0 inside `switch[1]`.
    """
    sx, sy, sz = (float(v) for v in bbox_size_xyz)
    csum = sx + sy + sz
    if csum <= 0.0:
        raise ValueError("lod_switch_table: degenerate bbox")
    bias = 0.0
    if area is not None:
        surf = 2.0 * (sx * sy + sy * sz + sz * sx)
        ratio = min(max(float(area) / max(surf, 1e-9), 1e-6), 1.0)
        bias = -math.log2(math.sqrt(ratio))
    scale = lod_factor * (2.0 ** -bias)
    distances = []
    for tris in tris_by_level:
        t = max(int(tris), 1)
        distances.append(scale * csum * 0.57735 / math.sqrt(3.0 * t))
    switch = {k: distances[k] for k in range(1, len(distances))}
    cull = scale * csum / 3.0
    return {"csum": csum, "bias": bias, "distances": distances, "switch": switch, "cull": cull}


# --------------------------------------------------------------------------------------
# Image helpers (Blender's image API only - no PIL)
# --------------------------------------------------------------------------------------

def _load_rgba(path, is_data=False):
    """(h, w, 4) float32, Blender's bottom-up row order, raw (no color transform)."""
    img = bpy.data.images.load(path)
    try:
        if is_data:
            img.colorspace_settings.name = "Non-Color"
        w, h = img.size
        buf = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(buf)
        return buf.reshape(h, w, 4).copy()
    finally:
        bpy.data.images.remove(img)


def _write_rgba(path, rgba):
    """Write an (h, w, 4) float array (bottom-up rows) as PNG; non-square allowed."""
    h, w = rgba.shape[:2]
    img = bpy.data.images.new("cs2_preview_out", width=w, height=h, alpha=True)
    try:
        img.pixels.foreach_set(np.ascontiguousarray(rgba, dtype=np.float32).ravel())
        img.filepath_raw = path
        img.file_format = "PNG"
        img.alpha_mode = "STRAIGHT"
        img.save()
    finally:
        bpy.data.images.remove(img)
    return path


def _srgb_to_linear(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _linear_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1.0 / 2.4) - 0.055)


def _hstack(panels, pad=4):
    """Side-by-side composite of (h, w, 4) arrays of equal height, on a mid-gray gutter."""
    h = max(p.shape[0] for p in panels)
    parts = []
    for i, p in enumerate(panels):
        if p.shape[0] != h:
            pad_rows = np.zeros((h - p.shape[0], p.shape[1], 4), np.float32)
            p = np.concatenate([p, pad_rows], axis=0)
        parts.append(p)
        if i < len(panels) - 1:
            parts.append(np.full((h, pad, 4), (0.5, 0.5, 0.5, 1.0), np.float32))
    return np.concatenate(parts, axis=1)


# --------------------------------------------------------------------------------------
# Rig state: everything is undone at the end of preview()
# --------------------------------------------------------------------------------------

class _Rig:
    """Tracks what preview() adds so it can be removed, and restores the scene."""

    def __init__(self, obj, samples):
        self.obj = obj
        self.scene = bpy.context.scene
        self.objects = []        # temporary objects (camera, sun, ground, imported prev)
        self.datablocks = []     # (collection, datablock) to remove
        self.samples = samples
        sc = self.scene
        self.saved = dict(
            engine=sc.render.engine,
            res=(sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage),
            film=sc.render.film_transparent,
            view=sc.view_settings.view_transform,
            look=sc.view_settings.look,
            world=sc.world,
            camera=sc.camera,
            fmt=sc.render.image_settings.file_format,
            cmode=sc.render.image_settings.color_mode,
            hide={o.name: o.hide_render for o in sc.objects},
            materials=list(obj.data.materials),
            eevee=sc.eevee.taa_render_samples,
            cycles=(sc.cycles.samples, sc.cycles.device),
            shading=(sc.display.shading.light, sc.display.shading.color_type,
                     tuple(sc.display.shading.single_color)),
        )
        # only `obj` renders unless a mode says otherwise
        for o in sc.objects:
            o.hide_render = (o is not obj)
        obj.hide_render = False

    def track(self, o):
        self.objects.append(o)
        return o

    def track_data(self, coll, db):
        self.datablocks.append((coll, db))
        return db

    def restore(self):
        sc = self.scene
        for o in list(self.objects):
            try:
                data = o.data
                bpy.data.objects.remove(o, do_unlink=True)
                if data is not None and data.users == 0:
                    if isinstance(data, bpy.types.Mesh):
                        bpy.data.meshes.remove(data)
                    elif isinstance(data, bpy.types.Camera):
                        bpy.data.cameras.remove(data)
                    elif isinstance(data, bpy.types.Light):
                        bpy.data.lights.remove(data)
            except (ReferenceError, RuntimeError):
                pass
        self.obj.data.materials.clear()
        for m in self.saved["materials"]:
            self.obj.data.materials.append(m)
        for coll, db in reversed(self.datablocks):
            try:
                coll.remove(db)
            except (ReferenceError, RuntimeError):
                pass
        s = self.saved
        sc.render.engine = s["engine"]
        sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = s["res"]
        sc.render.film_transparent = s["film"]
        try:
            sc.view_settings.view_transform = s["view"]
            sc.view_settings.look = s["look"]
        except TypeError:
            pass
        sc.world = s["world"]
        sc.camera = s["camera"]
        sc.render.image_settings.file_format = s["fmt"]
        sc.render.image_settings.color_mode = s["cmode"]
        sc.eevee.taa_render_samples = s["eevee"]
        sc.cycles.samples, sc.cycles.device = s["cycles"]
        sc.display.shading.light, sc.display.shading.color_type, sc.display.shading.single_color = s["shading"]
        for o in sc.objects:
            if o.name in s["hide"]:
                o.hide_render = s["hide"][o.name]


# --------------------------------------------------------------------------------------
# Scene building blocks
# --------------------------------------------------------------------------------------

def _set_engine(scene, engine):
    """Set a render engine by identifier, tolerating the 4.x/5.x EEVEE renames."""
    for cand in ((engine,) if engine != "EEVEE" else ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE")):
        try:
            scene.render.engine = cand
            return cand
        except TypeError:
            continue
    scene.render.engine = lift_tower.pick_engine()
    return scene.render.engine


def _make_world(rig, elevation_deg, sun_energy):
    """Nishita sky (no disc) + a SUN lamp aimed along the sky node's own sun direction."""
    sc = rig.scene
    world = rig.track_data(bpy.data.worlds, bpy.data.worlds.new("PreviewSky"))
    world.use_nodes = True
    nt = world.node_tree
    sky = nt.nodes.new("ShaderNodeTexSky")
    if "MULTIPLE_SCATTERING" in {i.identifier for i in sky.bl_rna.properties["sky_type"].enum_items}:
        sky.sky_type = "MULTIPLE_SCATTERING"          # Blender 5.x name of the Nishita model
    else:
        sky.sky_type = "NISHITA"
    sky.sun_disc = False
    sky.sun_elevation = math.radians(elevation_deg)
    sky.sun_rotation = math.radians(SKY_ROTATION_DEG)
    sky.altitude = SKY_ALTITUDE_M
    bg = nt.nodes["Background"]
    bg.inputs[1].default_value = SKY_STRENGTH
    nt.links.new(sky.outputs[0], bg.inputs[0])
    sc.world = world

    bpy.ops.object.light_add(type="SUN", location=(0.0, 0.0, 30.0))
    sun = rig.track(bpy.context.active_object)
    sun.name = "PreviewSun"
    sun.data.energy = sun_energy
    sun.data.angle = math.radians(0.53)
    # Direction TO the sun for the sky node's rotation r / elevation e. Measured on 5.2.2
    # (sun disc rendered at rotation 135 deg sits toward +X/-Y): (-cos r, -sin r, tan e)
    # x cos e. The node's `sun_direction` attribute is NOT this (it read (0, 0, 1)).
    r, e = math.radians(SKY_ROTATION_DEG), math.radians(elevation_deg)
    d = mathutils.Vector((-math.cos(r) * math.cos(e), -math.sin(r) * math.cos(e), math.sin(e)))
    sun.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    return world, sun


def _make_ground(rig):
    bpy.ops.mesh.primitive_plane_add(size=GROUND_SIZE_M, location=(0.0, 0.0, 0.0))
    ground = rig.track(bpy.context.active_object)
    ground.name = "PreviewGround"
    mat = rig.track_data(bpy.data.materials, bpy.data.materials.new("PreviewGround"))
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (GROUND_ALBEDO, GROUND_ALBEDO, GROUND_ALBEDO, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.95
    ground.data.materials.append(mat)
    return ground


def _make_camera(rig):
    bpy.ops.object.camera_add(location=(0.0, 0.0, 0.0))
    cam = rig.track(bpy.context.active_object)
    cam.name = "PreviewCamera"
    cam.data.sensor_fit = "VERTICAL"
    cam.data.sensor_height = 24.0
    cam.data.lens = 12.0 / math.tan(math.radians(VERTICAL_FOV_DEG) / 2.0)   # 45 deg vertical
    cam.data.clip_end = 20000.0
    rig.scene.camera = cam
    return cam


def _aim(cam, target, direction, distance):
    d = mathutils.Vector(direction).normalized()
    cam.location = mathutils.Vector(target) + d * distance
    cam.rotation_euler = (mathutils.Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()


def _frame(cam, obj, direction, resolution):
    """lift_tower.frame_camera at the rig's 45 deg lens (it keeps cam.data.lens)."""
    cam.data.lens = 12.0 / math.tan(math.radians(VERTICAL_FOV_DEG) / 2.0)
    lift_tower.frame_camera(cam, obj, mathutils.Vector(direction), resolution)


def _render(rig, path, engine="EEVEE", transparent=False, view_transform="AgX", look="None"):
    """Render one frame; EEVEE failures fall back to Cycles CPU (16 samples). `look`: the view look (None = flat AgX;
    lift_tower.SKIN_LIGHT asks for a contrast look; an unknown name falls back to None)."""
    sc = rig.scene
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.render.film_transparent = transparent
    sc.render.resolution_percentage = 100
    try:
        sc.view_settings.view_transform = view_transform
        sc.view_settings.look = "None"
    except TypeError:
        pass
    if look != "None":
        try:
            sc.view_settings.look = look
        except TypeError:
            print(f"[preview] view look {look!r} unknown; None")
    sc.render.filepath = path
    if engine == "WORKBENCH":
        _set_engine(sc, "BLENDER_WORKBENCH")
        bpy.ops.render.render(write_still=True)
        return path
    used = _set_engine(sc, engine)
    if used.startswith("BLENDER_EEVEE"):
        sc.eevee.taa_render_samples = rig.samples or EEVEE_SAMPLES
        try:
            bpy.ops.render.render(write_still=True)
            if os.path.exists(path):
                return path
            raise RuntimeError("EEVEE wrote nothing")
        except Exception as e:   # noqa: BLE001 - any engine failure -> Cycles
            print(f"[preview] EEVEE failed ({e!r}); falling back to Cycles CPU")
    _set_engine(sc, "CYCLES")
    sc.cycles.device = "CPU"
    sc.cycles.samples = rig.samples or CYCLES_FALLBACK_SAMPLES
    bpy.ops.render.render(write_still=True)
    return path


# --------------------------------------------------------------------------------------
# Materials
# --------------------------------------------------------------------------------------

def _tint_colours(tint):
    tint = dict(tint or {})
    return (tuple(tint.get("R", cs2_asset.STEEL_DEFAULT_RGB)),
            tuple(tint.get("G", cs2_asset.PAD_RGB)),
            tuple(tint.get("B", (1.0, 1.0, 1.0))))


def _tinted_base_image(rig, asset_dir, name, tint):
    """
    In-memory BaseColor with the recolor tints multiplied in (linear-space math):
    base_lin x mix(1, R_rgb, ctl.R) x mix(1, G_rgb, ctl.G) x mix(1, B_rgb, ctl.B).
    """
    base_path = os.path.join(asset_dir, f"{name}_BaseColor.png")
    ctl_path = os.path.join(asset_dir, f"{name}_ControlMask.png")
    base = _load_rgba(base_path)                                  # sRGB-encoded values
    h, w = base.shape[:2]
    lin = _srgb_to_linear(base[..., :3])
    if os.path.exists(ctl_path):
        ctl = _load_rgba(ctl_path, is_data=True)
        if ctl.shape[:2] != (h, w):
            ctl = np.repeat(np.repeat(ctl, max(1, h // ctl.shape[0]), 0), max(1, w // ctl.shape[1]), 1)[:h, :w]
        for ch, rgb in enumerate(_tint_colours(tint)):
            m = np.clip(ctl[..., ch], 0.0, 1.0)[..., None]
            col = np.asarray(rgb, np.float32)[None, None, :]
            lin = lin * ((1.0 - m) + col * m)
    out = np.empty((h, w, 4), np.float32)
    out[..., :3] = _linear_to_srgb(lin)
    out[..., 3] = 1.0
    img = rig.track_data(bpy.data.images, bpy.data.images.new(f"{name}_PreviewTinted", w, h, alpha=True))
    # colorspace BEFORE the pixel write: changing it afterwards regenerates the buffer
    # (measured: the tinted image came back all zeros).
    img.colorspace_settings.name = "sRGB"
    img.pixels.foreach_set(np.ascontiguousarray(out).ravel())
    return img


def _pbr_material(rig, asset_dir, name, tint, mat_name=None):
    """Principled material from the four maps (+ optional Emissive), see the module doc."""
    mat = rig.track_data(bpy.data.materials, bpy.data.materials.new(mat_name or f"{name}_PreviewPBR"))
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")

    def tex(image, colorspace, y):
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = image
        if image.colorspace_settings.name != colorspace:
            image.colorspace_settings.name = colorspace
        node.location = (-700, y)
        return node

    def load(suffix, colorspace):
        p = os.path.join(asset_dir, f"{name}_{suffix}.png")
        if not os.path.exists(p):
            return None
        return rig.track_data(bpy.data.images, bpy.data.images.load(p))

    base = tex(_tinted_base_image(rig, asset_dir, name, tint), "sRGB", 400)
    nt.links.new(base.outputs["Color"], bsdf.inputs["Base Color"])

    nrm_img = load("Normal", "Non-Color")
    if nrm_img is not None:
        nrm = tex(nrm_img, "Non-Color", 0)
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nmap.location = (-350, 0)
        nt.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])

    msk_img = load("MaskMap", "Non-Color")
    if msk_img is not None:
        msk = tex(msk_img, "Non-Color", -400)
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        sep.location = (-350, -400)
        nt.links.new(msk.outputs["Color"], sep.inputs["Color"])
        nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
        nt.links.new(sep.outputs["Green"], bsdf.inputs["Coat Weight"])
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        inv.location = (-350, -600)
        nt.links.new(msk.outputs["Alpha"], inv.inputs[1])
        nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
    else:
        bsdf.inputs["Roughness"].default_value = 0.7

    emi_img = load("Emissive", "sRGB")
    if emi_img is not None:
        emi = tex(emi_img, "sRGB", -900)
        nt.links.new(emi.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = EMISSIVE_STRENGTH
    return mat


def _assign(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)


# --------------------------------------------------------------------------------------
# Density
# --------------------------------------------------------------------------------------

def _ramp_colour(ppm):
    stops = DENSITY_RAMP
    if ppm <= stops[0][0]:
        return stops[0][1]
    if ppm >= stops[-1][0]:
        return stops[-1][1]
    x = math.log2(max(ppm, 1e-6))
    for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
        if p0 <= ppm <= p1:
            t = (x - math.log2(p0)) / (math.log2(p1) - math.log2(p0))
            return tuple(c0[i] + (c1[i] - c0[i]) * t for i in range(3))
    return stops[-1][1]


def face_texel_density(obj, A, uv_layer=None):
    """Per-polygon shipped px/m: A * sqrt(uv_area / world_area) (0 for degenerate faces)."""
    me = obj.data
    if uv_layer is None:
        uv_layer = "UVUnique" if "UVUnique" in me.uv_layers else me.uv_layers.active.name
    uv = me.uv_layers[uv_layer].data
    M = obj.matrix_world
    out = np.zeros(len(me.polygons), np.float64)
    for poly in me.polygons:
        li = list(poly.loop_indices)
        pts = [M @ me.vertices[me.loops[i].vertex_index].co for i in li]
        uvs = [uv[i].uv for i in li]
        wa = 0.0
        ua = 0.0
        for k in range(1, len(pts) - 1):
            wa += (pts[k] - pts[0]).cross(pts[k + 1] - pts[0]).length * 0.5
            ua += abs((uvs[k] - uvs[0]).cross(uvs[k + 1] - uvs[0])) * 0.5
        out[poly.index] = A * math.sqrt(ua / wa) if wa > 1e-12 and ua > 0.0 else 0.0
    return out, uv_layer


def _density_material(rig, obj, A, name):
    """Unlit face-color ramp x 4-texel checker on the shipped UV layer."""
    dens, uv_layer = face_texel_density(obj, A)
    me = obj.data
    attr_name = "cs2_preview_density"
    if attr_name in me.color_attributes:
        me.color_attributes.remove(me.color_attributes[attr_name])
    ca = me.color_attributes.new(attr_name, "FLOAT_COLOR", "FACE")
    cols = np.empty((len(me.polygons), 4), np.float32)
    for i, d in enumerate(dens):
        cols[i, :3] = _ramp_colour(d)
        cols[i, 3] = 1.0
    ca.data.foreach_set("color", cols.ravel())
    rig.datablocks.append((_ColourAttrRemover(me), attr_name))

    mat = rig.track_data(bpy.data.materials, bpy.data.materials.new(f"{name}_PreviewDensity"))
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    outn = nt.nodes.new("ShaderNodeOutputMaterial")
    emis = nt.nodes.new("ShaderNodeEmission")
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = attr_name
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = uv_layer
    chk = nt.nodes.new("ShaderNodeTexChecker")
    chk.inputs["Scale"].default_value = A / float(DENSITY_CHECKER_PX)
    chk.inputs["Color1"].default_value = (1.0, 1.0, 1.0, 1.0)
    chk.inputs["Color2"].default_value = (0.72, 0.72, 0.72, 1.0)
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs["Factor"].default_value = 1.0
    nt.links.new(uvn.outputs["UV"], chk.inputs["Vector"])
    nt.links.new(attr.outputs["Color"], mul.inputs[6])
    nt.links.new(chk.outputs["Color"], mul.inputs[7])
    nt.links.new(mul.outputs[2], emis.inputs["Color"])
    emis.inputs["Strength"].default_value = 1.0
    nt.links.new(emis.outputs["Emission"], outn.inputs["Surface"])

    # per-part table (tag_parts int attribute "part" + part table if present)
    _print_density_table(obj, dens)
    return mat, dens


class _ColourAttrRemover:
    """Adapter so the rig's datablock cleanup can drop a mesh color attribute by name."""

    def __init__(self, mesh):
        self.mesh = mesh

    def remove(self, attr_name):
        if attr_name in self.mesh.color_attributes:
            self.mesh.color_attributes.remove(self.mesh.color_attributes[attr_name])


def _print_density_table(obj, dens):
    me = obj.data
    table = None
    try:
        import json
        if cs2_asset.PART_TABLE_PROP in me:
            table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    except Exception:   # noqa: BLE001
        table = None
    nz = dens[dens > 0]
    if nz.size == 0:
        print("[preview] density: no textured faces")
        return
    print(f"[preview] density px/m: median {np.median(nz):.1f}  min {nz.min():.1f}  max {nz.max():.1f}")
    if table and "part" in me.attributes:
        part = np.empty(len(me.polygons), np.int32)
        me.attributes["part"].data.foreach_get("value", part)
        rows = []
        for idx in np.unique(part):
            sel = dens[(part == idx) & (dens > 0)]
            if sel.size == 0:
                continue
            pname = table[idx - 1] if 1 <= idx <= len(table) else f"#{idx}"
            rows.append((pname, float(np.median(sel)), float(sel.min()), float(sel.max())))
        rows.sort(key=lambda r: r[1])
        for pname, med, lo, hi in rows:
            print(f"[preview]   {pname:<24} median {med:7.1f}  min {lo:7.1f}  max {hi:7.1f}")


# --------------------------------------------------------------------------------------
# Silhouettes (lod mode)
# --------------------------------------------------------------------------------------

def _silhouette(rig, cam, obj, path, resolution):
    """Workbench flat render with a transparent film -> boolean coverage mask (bottom-up)."""
    sc = rig.scene
    sc.render.resolution_x, sc.render.resolution_y = resolution
    shading = sc.display.shading
    shading.light = "FLAT"
    shading.color_type = "SINGLE"
    shading.single_color = (0.9, 0.9, 0.9)
    obj.hide_render = False
    _render(rig, path, engine="WORKBENCH", transparent=True, view_transform="Standard")
    obj.hide_render = True
    px = _load_rgba(path)
    return px[..., 3] > 0.5, px


def _find_lods(name, obj):
    lods = []
    k = 1
    while True:
        o = bpy.data.objects.get(f"{name}_LOD{k}")
        if o is None or o is obj:
            break
        lods.append(o)
        k += 1
    return lods


def _mode_lod(rig, cam, obj, name, out_dir, lods, resolution, view_dir):
    written = []
    if not lods:
        print("[preview] lod: no LOD objects supplied or found - mode skipped")
        return written
    center, size = lift_tower.world_bounds(obj)
    tris = [cs2_asset.tri_count(obj)] + [cs2_asset.tri_count(l) for l in lods]
    table = lod_switch_table(size, tris)
    print(f"[preview] lod switch (csum {table['csum']:.1f} m, tris {tris}): "
          + "  ".join(f"LOD{k} {d:.0f} m" for k, d in table["switch"].items())
          + f"  cull {table['cull']:.0f} m")
    sil_res = (min(resolution[0] * 2, 2600), min(resolution[1] * 2, 2000))
    tmp0 = os.path.join(out_dir, f"_{name}_sil_lod0.png")
    tmpk = os.path.join(out_dir, f"_{name}_sil_lodk.png")
    ground_saved = None
    for k, lod in enumerate(lods, start=1):
        d_k = table["switch"][k]
        for dist in (d_k, d_k / 2.0):
            _aim(cam, center, view_dir, dist)
            m0, p0 = _silhouette(rig, cam, obj, tmp0, sil_res)
            mk, pk = _silhouette(rig, cam, lod, tmpk, sil_res)
            union = m0 | mk
            xor = m0 ^ mk
            pct = 100.0 * xor.sum() / max(union.sum(), 1)
            ys, xs = np.nonzero(union)
            if ys.size == 0:
                y0, y1, x0, x1 = 0, union.shape[0], 0, union.shape[1]
            else:
                pad = 12
                y0, y1 = max(ys.min() - pad, 0), min(ys.max() + pad + 1, union.shape[0])
                x0, x1 = max(xs.min() - pad, 0), min(xs.max() + pad + 1, union.shape[1])
            viz = np.zeros_like(p0)
            viz[..., 3] = 1.0
            viz[m0 & mk] = (0.85, 0.85, 0.85, 1.0)
            viz[m0 & ~mk] = (0.95, 0.20, 0.15, 1.0)     # only LOD0 - lost by LODk
            viz[~m0 & mk] = (0.15, 0.35, 0.95, 1.0)     # only LODk - grown
            for p in (p0, pk):
                p[..., :3] = np.where(p[..., 3:4] > 0.5, p[..., :3], 0.18)
                p[..., 3] = 1.0
            comp = _hstack([p0[y0:y1, x0:x1], pk[y0:y1, x0:x1], viz[y0:y1, x0:x1]])
            # nearest-neighbor upscale so the far-distance crop stays legible (the
            # on-screen size at d is what the crop's pixel count reports, not this)
            up = max(1, int(math.ceil(LOD_MIN_PANEL_PX / max(comp.shape[0], 1))))
            if up > 1:
                comp = np.repeat(np.repeat(comp, up, axis=0), up, axis=1)
            path = os.path.join(out_dir, f"{name}_lod{k}_{dist:.0f}m_xor{pct:.1f}pct.png")
            _write_rgba(path, comp)
            print(f"[preview] lod{k} @ {dist:.0f} m: silhouette xor {pct:.2f} % -> {path}")
            written.append(path)
    for t in (tmp0, tmpk):
        try:
            os.remove(t)
        except OSError:
            pass
    obj.hide_render = False
    return written


# --------------------------------------------------------------------------------------
# Previous generation import (ab mode)
# --------------------------------------------------------------------------------------

def import_generation(fbx_path):
    """
    Re-import an FBX written by cs2_asset.export_fbx and undo its bake: vertices are cm,
    Y-up, (x, y, z)_blender -> (x, z, -y); the inverse is (X, Y, Z) -> (X, -Z, Y) / 100.
    Returns the imported mesh object in meters, Z-up, identity transform.
    """
    before = set(bpy.data.objects)
    op = bpy.ops.wm.fbx_import if hasattr(bpy.ops.wm, "fbx_import") else bpy.ops.import_scene.fbx
    op(filepath=fbx_path)
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    if not new:
        raise RuntimeError(f"import_generation: nothing imported from {fbx_path}")
    if len(new) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for o in new:
            o.select_set(True)
        bpy.context.view_layer.objects.active = new[0]
        bpy.ops.object.join()
    prev = new[0]
    ys = [v.co.y for v in prev.data.vertices]
    zs = [v.co.z for v in prev.data.vertices]
    y_up = (max(ys) - min(ys)) >= (max(zs) - min(zs)) * 0.5 and abs(min(ys)) < 0.2 * max(1e-6, max(ys) - min(ys))
    rot = mathutils.Matrix.Rotation(math.radians(90.0), 4, "X") if y_up else mathutils.Matrix.Identity(4)
    extent = max(max(ys) - min(ys), max(zs) - min(zs), max(v.co.x for v in prev.data.vertices) - min(v.co.x for v in prev.data.vertices))
    scale = 0.01 if extent > 60.0 else 1.0
    prev.matrix_world = rot @ mathutils.Matrix.Scale(scale, 4) @ prev.matrix_world
    bpy.ops.object.select_all(action="DESELECT")
    prev.select_set(True)
    bpy.context.view_layer.objects.active = prev
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return prev


def _resolve_prev(prev_generation):
    """(fbx path, asset dir, name) from a folder or an FBX path."""
    p = os.path.abspath(prev_generation)
    if os.path.isdir(p):
        cands = [f for f in os.listdir(p) if f.lower().endswith(".fbx") and "_" not in f[:-4]]
        if not cands:
            cands = [f for f in os.listdir(p) if f.lower().endswith(".fbx")]
        if not cands:
            raise FileNotFoundError(f"ab: no FBX in {p}")
        stem = os.path.splitext(sorted(cands)[0])[0]
        return os.path.join(p, stem + ".fbx"), p, stem
    stem = os.path.splitext(os.path.basename(p))[0]
    return p, os.path.dirname(p), stem


# --------------------------------------------------------------------------------------
# Entry point
# --------------------------------------------------------------------------------------

def preview(obj, asset_dir, name, out_dir,
            modes=("pbr", "dusk", "normals", "lod", "density", "ab"),
            views=None, lods=None, prev_generation=None, tint=None, ground=True,
            resolution=(1300, 1000), samples=None):
    """
    Render the preview set described in the module docstring. Returns the written paths.

    `views`: iterable of (name, (x, y, z) camera direction) - default DEFAULT_VIEWS.
    `lods`: LOD objects (LOD1, LOD2, ...) for the lod mode; None = look up `<name>_LOD<k>`.
    `prev_generation`: previous letter's asset folder (or FBX) for the ab mode; None skips it.
    `tint`: {"R"|"G"|"B": rgb} overrides of the recolor slots (defaults steel / pad / white).
    `samples`: override the EEVEE (32) / Cycles fallback (16) sample count (tests).
    """
    os.makedirs(out_dir, exist_ok=True)
    views = list(views) if views is not None else list(DEFAULT_VIEWS)
    modes = tuple(modes)
    asset_dir = os.path.abspath(asset_dir)
    written = []
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")

    rig = _Rig(obj, samples)
    try:
        cam = _make_camera(rig)
        sc = rig.scene
        res = tuple(int(v) for v in resolution)

        def lit_pass(mode, world_spec):
            world, sun = _make_world(rig, **world_spec)
            gnd = _make_ground(rig) if ground else None
            _assign(obj, _pbr_material(rig, asset_dir, name, tint))
            for vname, vdir in views:
                sc.render.resolution_x, sc.render.resolution_y = res
                _frame(cam, obj, vdir, res)
                path = os.path.join(out_dir, f"{name}_{mode}_{vname}.png")
                written.append(_render(rig, path, engine="EEVEE"))
                print(f"[preview] {mode} -> {path}")
            for o in [x for x in (gnd, sun) if x is not None]:
                rig.objects.remove(o)
                bpy.data.objects.remove(o, do_unlink=True)
            sc.world = None

        if "pbr" in modes:
            lit_pass("pbr", NOON)
        if "dusk" in modes:
            lit_pass("dusk", DUSK)

        if "normals" in modes:
            shading = sc.display.shading
            shading.light = "MATCAP"
            shading.color_type = "SINGLE"
            shading.single_color = (0.80, 0.80, 0.80)
            try:
                shading.show_cavity = False
            except AttributeError:
                pass
            for vname, vdir in views:
                sc.render.resolution_x, sc.render.resolution_y = res
                _frame(cam, obj, vdir, res)
                path = os.path.join(out_dir, f"{name}_normals_{vname}.png")
                written.append(_render(rig, path, engine="WORKBENCH", view_transform="Standard"))
                print(f"[preview] normals -> {path}")

        if "density" in modes:
            A = None
            bp = os.path.join(asset_dir, f"{name}_BaseColor.png")
            if os.path.exists(bp):
                img = bpy.data.images.load(bp)
                A = img.size[0]
                bpy.data.images.remove(img)
            if A is None or not obj.data.uv_layers:
                print("[preview] density: no BaseColor / UVs - mode skipped")
            else:
                mat, _ = _density_material(rig, obj, A, name)
                _assign(obj, mat)
                sc.world = None
                for vname, vdir in views:
                    sc.render.resolution_x, sc.render.resolution_y = res
                    _frame(cam, obj, vdir, res)
                    path = os.path.join(out_dir, f"{name}_density_{vname}.png")
                    written.append(_render(rig, path, engine="EEVEE", view_transform="Standard"))
                    print(f"[preview] density -> {path}")

        if "lod" in modes:
            lod_objs = list(lods) if lods is not None else _find_lods(name, obj)
            for l in lod_objs:
                l.hide_render = True
            written += _mode_lod(rig, cam, obj, name, out_dir, lod_objs, res, views[0][1])

        if "ab" in modes:
            if prev_generation is None:
                print("[preview] ab: no prev_generation given - mode skipped")
            else:
                fbx, prev_dir, prev_name = _resolve_prev(prev_generation)
                prev = rig.track(import_generation(fbx))
                prev.name = f"{prev_name}_PreviewPrev"
                _assign(prev, _pbr_material(rig, prev_dir, prev_name, tint, mat_name=f"{prev_name}_PreviewPBRPrev"))
                _assign(obj, _pbr_material(rig, asset_dir, name, tint, mat_name=f"{name}_PreviewPBRAB"))
                world, sun = _make_world(rig, **NOON)
                gnd = _make_ground(rig) if ground else None
                tmp_new = os.path.join(out_dir, f"_{name}_ab_new.png")
                tmp_old = os.path.join(out_dir, f"_{name}_ab_old.png")
                for vname, vdir in views:
                    sc.render.resolution_x, sc.render.resolution_y = res
                    _frame(cam, obj, vdir, res)
                    prev.hide_render = True
                    obj.hide_render = False
                    _render(rig, tmp_new, engine="EEVEE")
                    prev.hide_render = False
                    obj.hide_render = True
                    _render(rig, tmp_old, engine="EEVEE")
                    obj.hide_render = False
                    prev.hide_render = True
                    comp = _hstack([_load_rgba(tmp_new), _load_rgba(tmp_old)])
                    path = os.path.join(out_dir, f"{name}_ab_{vname}.png")
                    _write_rgba(path, comp)
                    written.append(path)
                    print(f"[preview] ab ({name} | {prev_name}) -> {path}")
                for t in (tmp_new, tmp_old):
                    try:
                        os.remove(t)
                    except OSError:
                        pass
    finally:
        rig.restore()
    return written
