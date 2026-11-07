# ski_surface.py - procedural ski-run surface texture sets + previews (headless Blender).
#
# Generates the three maps the CS2 surface import path expects (research:
# docs/research/trails-surfaces-glades.md section 1.5 / golfpack-dissection.md section 2):
#   <Name>_BaseColor.png   sRGB, 1024^2, seamless
#   <Name>_Normal.png      OpenGL (G = +up), linear, flat = (128,128,255)
#   <Name>_MaskMap.png     SURFACE layout: R=Metallic(0) G=MetallicMask(1) B=NormalMask(1)
#                          A=Glossiness (snow ~0.30 groomed / ~0.42 snowmaking + sparkle)
#
# Two surfaces, generation A:
#   SkiRunGroomedA    - workhorse white with corduroy grooming
#   SkiRunSnowmakingA - brighter/denser machine-snow variant, fainter corduroy, icier gloss
#
# All patterns are strictly periodic (integer-frequency sines + FFT spectral noise), so the tiles
# are seamless by construction. The _tiling preview renders a 2x2 repeat to prove it.
#
# Usage:
#   blender --background --factory-startup --python ski_surface.py -- [--textures] [--previews]
# (no flags = both)

import os
import sys

import bpy  # noqa: F401  (headless blender)
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cs2_asset import _write_png  # the proven display-encoded PNG writer

# --------------------------------------------------------------------------------------

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
IMPORT_DIR = os.path.join(ROOT, "import", "surfaces", "A")
PREVIEW_DIR = os.path.join(ROOT, "preview", "surfaces")

SIZE = 1024
GROOVES = 96          # corduroy cycles per tile. At m_UVScale 0.06 a tile is ~16.7 m -> ~17 cm pitch
UV_SCALE = 0.06       # documented for the prefab. Used to scale the preview UVs realistically


def periodic_noise(size, f_lo, f_hi, falloff, seed, rng=None):
    """Tileable scalar noise in -1..1 via random-phase spectrum between integer
    frequencies f_lo..f_hi with 1/f^falloff amplitude."""
    rng = rng or np.random.default_rng(seed)
    f1 = np.fft.fftfreq(size) * size
    fx, fy = np.meshgrid(f1, f1)
    f = np.sqrt(fx * fx + fy * fy)
    mask = (f >= f_lo) & (f <= f_hi)
    amp = np.where(mask, 1.0 / np.maximum(f, 1.0) ** falloff, 0.0)
    phase = rng.uniform(0.0, 2.0 * np.pi, (size, size))
    n = np.real(np.fft.ifft2(amp * np.exp(1j * phase)))
    peak = np.abs(n).max()
    return n / peak if peak > 1e-12 else n


def build_height(kind, seed):
    """Groomed-snow height field in roughly -1..1 (corduroy dominant)."""
    u = (np.arange(SIZE) + 0.5) / SIZE
    X, _Y = np.meshgrid(u, u)

    wobble = periodic_noise(SIZE, 2, 7, 1.2, seed + 1) * 1.6          # groove waviness (radians)
    depth_mod = 0.72 + 0.28 * periodic_noise(SIZE, 2, 9, 1.0, seed + 2)  # patchy grooming depth
    groove = np.sin(2.0 * np.pi * GROOVES * X + wobble) * depth_mod

    undul = periodic_noise(SIZE, 3, 14, 1.4, seed + 3)                # soft settled-snow undulation
    if kind == "groomed":
        grain = periodic_noise(SIZE, 60, 340, 0.9, seed + 4)
        h = 1.00 * groove + 0.55 * undul + 0.22 * grain
    else:  # snowmaking: fainter corduroy, finer/denser grain, flatter base
        grain = periodic_noise(SIZE, 90, 460, 0.8, seed + 4)
        h = 0.60 * groove + 0.35 * undul + 0.30 * grain
    return h / np.abs(h).max(), groove


def normal_from_height(h, strength):
    """OpenGL tangent-space normal (array row 0 = image top, G = +up in image space)."""
    ddu = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5 * SIZE
    ddown = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5 * SIZE
    dup = -ddown                                   # derivative along image-up
    nx = -ddu * strength
    ny = -dup * strength
    nz = np.ones_like(h)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    rgba = np.empty((SIZE, SIZE, 4), dtype=np.float32)
    rgba[..., 0] = nx * inv * 0.5 + 0.5
    rgba[..., 1] = ny * inv * 0.5 + 0.5
    rgba[..., 2] = nz * inv * 0.5 + 0.5
    rgba[..., 3] = 1.0
    return rgba


def sparkle_mask(density, seed):
    """Sparse glint pixels (fraction `density`), with a few 2-px clusters."""
    rng = np.random.default_rng(seed)
    field = rng.random((SIZE, SIZE))
    m = field < density
    m |= np.roll(m, 1, axis=1) & (rng.random((SIZE, SIZE)) < 0.35)  # occasional 2px twinkle
    return m


def make_surface_textures(name, kind, seed):
    out_dir = os.path.join(IMPORT_DIR, name)
    os.makedirs(out_dir, exist_ok=True)

    h, groove = build_height(kind, seed)

    if kind == "groomed":
        base_v, n_strength, gloss_base = 0.90, 0.0035, 0.30
        sparkle_density, sparkle_gloss = 0.0015, 0.85
        cool_shift = 0.012
    else:
        base_v, n_strength, gloss_base = 0.94, 0.0026, 0.42
        sparkle_density, sparkle_gloss = 0.0040, 0.92
        cool_shift = 0.018

    # ---- BaseColor (sRGB): near-white, blue-shadowed troughs, low-freq patchiness, grain
    patch = periodic_noise(SIZE, 2, 10, 1.1, seed + 10) * 0.020
    grain = periodic_noise(SIZE, 80, 400, 0.8, seed + 11) * 0.013
    trough = np.clip(-groove, 0.0, 1.0)            # 1 in groove bottoms
    v = base_v + patch + grain - 0.038 * trough
    rgba = np.empty((SIZE, SIZE, 4), dtype=np.float32)
    rgba[..., 0] = v - cool_shift * trough - 0.004
    rgba[..., 1] = v - 0.5 * cool_shift * trough
    rgba[..., 2] = v + cool_shift                   # cool/blue snow tint, stronger in shadows
    rgba[..., 3] = 1.0
    np.clip(rgba, 0.0, 1.0, out=rgba)
    _write_png(os.path.join(out_dir, f"{name}_BaseColor.png"), f"{name}_BaseColor",
               rgba, SIZE, is_data=False)

    # ---- Normal (linear)
    _write_png(os.path.join(out_dir, f"{name}_Normal.png"), f"{name}_Normal",
               normal_from_height(h, n_strength), SIZE, is_data=True)

    # ---- MaskMap (linear): R=0 G=1 B=1 A=gloss(+ridge boost, + sparkle)
    ridge = np.clip(groove, 0.0, 1.0)
    gloss = gloss_base + 0.05 * ridge + periodic_noise(SIZE, 6, 40, 1.0, seed + 12) * 0.02
    gloss = np.clip(gloss, 0.0, 1.0)
    gloss[sparkle_mask(sparkle_density, seed + 13)] = sparkle_gloss
    mask = np.empty((SIZE, SIZE, 4), dtype=np.float32)
    mask[..., 0] = 0.0
    mask[..., 1] = 1.0
    mask[..., 2] = 1.0
    mask[..., 3] = gloss
    _write_png(os.path.join(out_dir, f"{name}_MaskMap.png"), f"{name}_MaskMap",
               mask, SIZE, is_data=True)

    print(f"[ski_surface] wrote {name} texture set -> {out_dir}")
    return out_dir


# --------------------------------------------------------------------------------------
# Previews
# --------------------------------------------------------------------------------------

def _reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 48
    scene.cycles.device = "CPU"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.view_settings.view_transform = "Filmic"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.55, 0.65, 0.85, 1.0)   # cold blue sky
    bg.inputs[1].default_value = 1.0
    scene.world = world
    return scene


def _snow_material(name, tex_dir):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]

    def img_node(suffix, colorspace, x, y):
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(os.path.join(tex_dir, f"{name}_{suffix}.png"))
        node.image.colorspace_settings.name = colorspace
        node.location = (x, y)
        return node

    base = img_node("BaseColor", "sRGB", -700, 300)
    nt.links.new(base.outputs["Color"], bsdf.inputs["Base Color"])

    nrm_img = img_node("Normal", "Non-Color", -700, 0)
    nrm = nt.nodes.new("ShaderNodeNormalMap")
    nrm.inputs["Strength"].default_value = 1.0
    nt.links.new(nrm_img.outputs["Color"], nrm.inputs["Color"])
    nt.links.new(nrm.outputs["Normal"], bsdf.inputs["Normal"])

    mask = img_node("MaskMap", "Non-Color", -700, -300)
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(mask.outputs["Alpha"], inv.inputs[1])       # roughness = 1 - glossiness
    nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
    bsdf.inputs["Metallic"].default_value = 0.0
    return mat


def _terrain(mat, tiles):
    """40x40 m gently rolling slope, UV-tiled like in game (UV_SCALE)."""
    n = 129
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=n - 1, y_subdivisions=n - 1, size=40.0)
    ob = bpy.context.object
    for vtx in ob.data.vertices:
        x, y = vtx.co.x, vtx.co.y
        vtx.co.z = (y * 0.35                                   # ~19 degree mean slope
                    + 0.55 * np.sin(x * 0.30) * np.cos(y * 0.22)
                    + 0.25 * np.sin(x * 0.83 + 1.7) * np.sin(y * 0.61 + 0.4))
    uv = ob.data.uv_layers.active or ob.data.uv_layers.new()
    for loop in ob.data.loops:
        co = ob.data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = ((co.x + 20.0) * UV_SCALE * tiles, (co.y + 20.0) * UV_SCALE * tiles)
    ob.data.materials.append(mat)
    for poly in ob.data.polygons:
        poly.use_smooth = True
    return ob


def _sun(elev_deg, azim_deg, strength=4.0):
    bpy.ops.object.light_add(type="SUN")
    sun = bpy.context.object
    sun.data.energy = strength
    sun.data.angle = 0.03
    sun.rotation_euler = (np.radians(90.0 - elev_deg), 0.0, np.radians(azim_deg))
    return sun


def _shoot(cam_loc, cam_rot_deg, path):
    bpy.ops.object.camera_add(location=cam_loc)
    cam = bpy.context.object
    cam.rotation_euler = tuple(np.radians(a) for a in cam_rot_deg)
    bpy.context.scene.camera = cam
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


def make_previews(name):
    tex_dir = os.path.join(IMPORT_DIR, name)
    os.makedirs(PREVIEW_DIR, exist_ok=True)

    # three-quarter + raking-light detail on the rolling slope
    _reset_scene()
    mat = _snow_material(name, tex_dir)
    _terrain(mat, tiles=1.0)
    _sun(16.0, 35.0)                                          # low winter sun rakes the corduroy
    _shoot((24.0, -30.0, 16.0), (68.0, 0.0, 38.0),
           os.path.join(PREVIEW_DIR, f"{name}_three_quarter.png"))
    _shoot((4.5, -9.0, 1.8), (81.0, 0.0, 22.0),
           os.path.join(PREVIEW_DIR, f"{name}_detail.png"))

    # seam proof: flat top-down 2x2 tile repeat
    _reset_scene()
    mat = _snow_material(name, tex_dir)
    bpy.ops.mesh.primitive_plane_add(size=40.0)
    ob = bpy.context.object
    uv = ob.data.uv_layers.active or ob.data.uv_layers.new()
    for loop in ob.data.loops:
        co = ob.data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = ((co.x + 20.0) / 20.0, (co.y + 20.0) / 20.0)  # exactly 2x2 tiles
    ob.data.materials.append(mat)
    _sun(35.0, 20.0, strength=3.0)
    _shoot((0.0, 0.0, 55.0), (0.0, 0.0, 0.0),
           os.path.join(PREVIEW_DIR, f"{name}_tiling.png"))
    print(f"[ski_surface] previews for {name} -> {PREVIEW_DIR}")


# --------------------------------------------------------------------------------------

def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    do_tex = "--textures" in argv or not argv
    do_prev = "--previews" in argv or not argv

    surfaces = [("SkiRunGroomedA", "groomed", 20260808), ("SkiRunSnowmakingA", "snowmaking", 20260809)]
    if do_tex:
        for name, kind, seed in surfaces:
            make_surface_textures(name, kind, seed)
    if do_prev:
        for name, _kind, _seed in surfaces:
            make_previews(name)


main()
