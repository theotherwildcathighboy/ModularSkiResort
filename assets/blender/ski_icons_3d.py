# 3D Build-strip icons on the style-E card (T181, post-release round 1, 2026-11-14): ski_icons.py's own plate, sky, far range,
# snow ground, foot and rim around a Cycles render of the asset (shadow catcher on transparent film), framed tight on its
# above-ground mass. Input = a cs2_review folder pack (cs2_review.py --from-folder <asset folder> --out <dir>).
#   blender -b --factory-startup --python-exit-code 1 --python ski_icons_3d.py -- <pack.blend> <out_dir> <icon_name> [az_deg|-] [line]
# az_deg = the plan direction from the asset toward the camera (default -50). line (lift stations, optional) =
#   "line=<base|top>,y=<rope anchor, pack y>,z=<rope z>,lanes=<x0;x1>,cabin=<carrier fbx>,hang=<m|auto>,at=<m>,cabscale=<k>,
#   slope=<rise/run>,focus=<m>": both rope strands out of the nose (rising from a base, falling from a top), one carrier on
#   its hanger `at` m out; focus frames only the asset within `focus` m of the anchor. ICON_LOOK / ICON_EXPOSURE (env)
#   override the tone map. Writes <out_dir>/<icon_name>.png (256 x 256 RGBA) and the raw render _raw_<icon_name>.png.
# ICON_MODE=bare (env, T210, 2026-11-16): the vanilla menu look instead of the card: the asset with its lot and contact
#   shadow on a transparent canvas, framed on the whole asset and centered in a BARE_BOX square (no sky, no plate, no rim).
#   The prefab m_Icon (asset menu, Find It) uses this look; the hub Build strip keeps the card (<icon>Card.png).
# The per-asset settings of the round-2 preview sheet: docs/research/post-release-round1/WP-D-assets-findings.md (T181).
import bpy, math, os, sys
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
pack, out_dir, icon = argv[0], argv[1], argv[2]
AZ = float(argv[3]) if len(argv) > 3 and argv[3] not in ("", "-") else None
BARE = os.environ.get("ICON_MODE", "card") == "bare"
BARE_BOX = float(os.environ.get("ICON_BARE_BOX", "200"))     # px of the 256 canvas the asset may fill (28 px margin each side)
BARE_FLOOR = 0.08                                             # shadow-catcher alpha below this is haze, not shadow
BARE_SHADOW = 0.35                                            # contact shadow density (vanilla thumbnails: almost none)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ski_icons as SI  # noqa: E402  (pure numpy, no bpy)

SI.SS = 4
SI.build_grid()
N = SI.CANVAS * SI.SS          # 1024 render
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=pack)
sc = bpy.context.scene
keep = []
for o in bpy.data.objects:
    if o.type != "MESH":
        continue
    nm = o.name
    if "_prop" in nm or "_LOD1" in nm or "_LOD2" in nm or "label" in nm.lower() or nm.startswith("cs2r_"):
        continue
    keep.append(o)
for c in list(sc.collection.children):
    sc.collection.children.unlink(c)
for o in list(sc.collection.objects):
    sc.collection.objects.unlink(o)
work = bpy.data.collections.new("ICON")
sc.collection.children.link(work)
for o in keep:
    work.objects.link(o)
    o.hide_render = o.hide_viewport = False
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
frame_objs = list(keep)

# optional lift dressing: "line=<base|top>,y=<anchor pack y>,z=<rope z>,lanes=<x0;x1>,cabin=<fbx>,hang=<m>,at=<m out>"
LINE = dict(kv.split("=", 1) for kv in argv[4].split(",")) if len(argv) > 4 and argv[4] else {}
if BARE:    # bare: no rope stubs or carrier. At menu size the hairline strands are noise and they halve the hall's scale
    LINE = {}


def _mat_from_png(png):
    m = bpy.data.materials.new("cabin")
    m.use_nodes = True
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(png)
    nt.links.new(tex.outputs[0], nt.nodes["Principled BSDF"].inputs["Base Color"])
    nt.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.5
    return m


if LINE:
    ay, rz = float(LINE["y"]), float(LINE["z"])
    lanes = [float(v) for v in LINE["lanes"].split(";")]
    slope = float(LINE.get("slope", 0.35)) * (1 if LINE["line"] == "base" else -1)       # the line climbs away from a base, falls away from a top
    L_OUT = (float(LINE.get("at", 14.0)) + 5.0) if BARE else 60.0    # bare: the strands end just past the carrier, inside the canvas
    for lx in lanes:
        a = Vector((lx, ay, rz))
        b = Vector((lx, ay + L_OUT, rz + L_OUT * slope))
        bpy.ops.mesh.primitive_cylinder_add(radius=0.09, depth=(b - a).length, vertices=8, location=(a + b) / 2)
        cy = bpy.context.active_object
        cy.rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
        cy.name = "xrope"
        if BARE:
            frame_objs.append(cy)
        cy.data.materials.append(bpy.data.materials.new("rope"))
        cy.data.materials[0].diffuse_color = (0.05, 0.05, 0.05, 1)
    if "cabin" in LINE:
        before = set(bpy.data.objects)
        bpy.ops.wm.fbx_import(filepath=LINE["cabin"])
        new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
        png = LINE["cabin"][:-4] + "_BaseColor.png"
        mat = _mat_from_png(png) if os.path.exists(png) else None
        bpy.context.view_layer.update()
        cp = [o.matrix_world @ Vector(c) for o in new for c in o.bound_box]
        if max(p.z for p in cp) - min(p.z for p in cp) > 30.0:      # FBX in cm
            for o in new:
                if o.parent is None:
                    o.scale *= 0.01
                    o.location *= 0.01
            bpy.context.view_layer.update()
        cs_ = float(LINE.get("cabscale", 1.0))
        if cs_ != 1.0:
            for o in new:
                if o.parent is None:
                    o.scale *= cs_
                    o.location *= cs_
            bpy.context.view_layer.update()
        cp = [o.matrix_world @ Vector(c) for o in new for c in o.bound_box]
        print("CABIN h", round(max(p.z for p in cp) - min(p.z for p in cp), 2))
        clo = Vector((min(p.x for p in cp), min(p.y for p in cp), min(p.z for p in cp)))
        chi = Vector((max(p.x for p in cp), max(p.y for p in cp), max(p.z for p in cp)))
        at = float(LINE.get("at", 14.0))
        lx = lanes[0] if LINE["line"] == "base" else lanes[-1]
        grip = Vector((lx, ay + at, rz + at * slope))
        hang = LINE.get("hang", "4.5")                         # rope to cabin floor (catalog/carriers.json gd8)
        hang = (chi.z - clo.z) if hang == "auto" else float(hang)   # auto: the FBX carries its own hanger up to the grip
        floor_z = grip.z - hang
        off = Vector((grip.x, grip.y, floor_z)) - Vector(((clo.x + chi.x) / 2, (clo.y + chi.y) / 2, clo.z))
        hz_top = floor_z + (chi.z - clo.z)
        bpy.ops.mesh.primitive_cylinder_add(radius=0.07, depth=grip.z - hz_top + 0.05, vertices=8,
                                            location=(grip.x, grip.y, (grip.z + hz_top) / 2))
        hg = bpy.context.active_object
        hg.data.materials.append(bpy.data.materials.new("hanger"))
        hg.data.materials[0].diffuse_color = (0.25, 0.25, 0.27, 1)
        frame_objs.append(hg)
        for o in new:
            o.location += off
            if mat is not None:
                o.data.materials.clear()
                o.data.materials.append(mat)
            for cl in list(o.users_collection):
                cl.objects.unlink(o)
            work.objects.link(o)
        frame_objs += new
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()

verts = []
for o in frame_objs:
    ev = o.evaluated_get(dg)
    me = ev.to_mesh()
    mw = o.matrix_world
    verts += [mw @ v.co for v in me.vertices]
    ev.to_mesh_clear()
V = np.array([tuple(v) for v in verts])
lo, hi = V.min(axis=0), V.max(axis=0)
z0 = float(lo[2])

# camera: 3/4 view, light from the upper left as the flat set
az = math.radians(AZ if AZ is not None else -50.0)       # direction from the target toward the camera, in plan
el = math.radians(30.0)
d = Vector((math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el)))
cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("cam", cam_data)
work.objects.link(cam)
ctr = Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2))
cam.location = ctr + d * 500.0
cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
cam_data.clip_end = 3000.0
sc.camera = cam
bpy.context.view_layer.update()
R = np.array(cam.matrix_world.to_3x3())          # columns = camera x, y, z in world
U = V @ R[:, 0]
W = V @ R[:, 1]
mass = (V[:, 2] > z0 + 0.8) & (V[:, 1] > float(LINE["y"]) - float(LINE["focus"]) if "focus" in LINE else True)                        # frame on the above-ground mass, not the lot pads
u_lo, u_hi = np.percentile(U[mass], [0.4, 99.6])
v_lo, v_hi = np.percentile(W[mass], [0.0, 99.6])
v_lo = float(W.min()) if (W[mass].min() - W.min()) < 3.0 else float(np.percentile(W[mass], 0.4))
if BARE:   # the whole asset, lot pads included (vanilla thumbnails show the lot), plus its ground shadow (below)
    LD = -R[:, 0] * 0.75 + np.array([0, 0, 1.0]) * 2.4 + R[:, 2] * 0.55      # the sun direction set further down
    LD = LD / np.linalg.norm(LD)
    S = V - LD[None, :] * ((V[:, 2] - z0) / LD[2])[:, None]                  # each vertex cast onto the ground plane
    VS = np.concatenate([V, S])
    US, WS = VS @ R[:, 0], VS @ R[:, 1]
    u_lo, u_hi = np.percentile(US, [0.1, 99.9])
    v_lo, v_hi = float(WS.min()), float(np.percentile(WS, 99.9))
w, h = u_hi - u_lo, v_hi - v_lo
BOX_W, BOX_H, FOOT_Y = 190.0, 178.0, 212.0
if BARE:
    BOX_W = BOX_H = BARE_BOX
    FOOT_Y = 128.0 + BARE_BOX / 2.0 - max(0.0, BARE_BOX - h * BARE_BOX / max(w, h)) / 2.0   # centered vertically
ortho = max(w * 256.0 / BOX_W, h * 256.0 / BOX_H)
k = 256.0 / ortho
u_c = (u_lo + u_hi) / 2
v_c = v_lo + (FOOT_Y - 128.0) / k
cam_data.ortho_scale = ortho
base = Vector(tuple(R[:, 0] * u_c + R[:, 1] * v_c))
dpt = float(np.dot(np.array(cam.location), R[:, 2]))
cam.location = base + Vector(tuple(R[:, 2])) * dpt
bpy.context.view_layer.update()
# the lot's back edge on the canvas sets the snow horizon
corners = np.array([[x, y, z0] for x in (lo[0], hi[0]) for y in (lo[1], hi[1])])
y_back = float(np.min(128.0 - (corners @ R[:, 1] - v_c) * k))
y_front = float(np.max(128.0 - (corners @ R[:, 1] - v_c) * k))

# shadow catcher ground
bpy.ops.mesh.primitive_plane_add(size=2000.0, location=(ctr.x, ctr.y, z0 - 0.02))
gp = bpy.context.active_object
gp.is_shadow_catcher = True
if gp.name not in work.objects:
    work.objects.link(gp)

sun_d = bpy.data.lights.new("sun", "SUN")
sun_d.energy = 4.2
sun_d.color = (1.0, 0.94, 0.84)
sun_d.angle = math.radians(2.0)
sun = bpy.data.objects.new("sun", sun_d)
work.objects.link(sun)
# from the upper left of the picture: camera-left, camera-up, toward the camera
ldir = Vector(tuple(-R[:, 0] * 0.75 + np.array([0, 0, 1.0]) * (2.4 if BARE else 1.0) + R[:, 2] * 0.55)).normalized()
# bare: a high sun keeps the contact shadow near the footprint, so it never runs off the transparent canvas
sun.rotation_euler = ldir.to_track_quat("Z", "Y").to_euler()
world = bpy.data.worlds.new("w")
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
bg.inputs[0].default_value = (0.62, 0.74, 0.92, 1.0)
bg.inputs[1].default_value = 0.75
sc.world = world

sc.render.engine = "CYCLES"
try:
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "OPTIX"
    prefs.get_devices()
    for dv in prefs.devices:
        dv.use = True
    sc.cycles.device = "GPU"
except Exception as e:  # CPU fallback
    print("GPU off:", e)
sc.cycles.samples = 96
sc.cycles.use_denoising = True
sc.render.resolution_x = sc.render.resolution_y = N
sc.render.film_transparent = True
sc.render.image_settings.file_format = "PNG"
sc.render.image_settings.color_mode = "RGBA"
sc.view_settings.view_transform = "AgX"
try:
    sc.view_settings.look = os.environ.get("ICON_LOOK", "AgX - Base Contrast")
    sc.view_settings.exposure = float(os.environ.get("ICON_EXPOSURE", "0.65" if BARE else "0.45"))   # bare: the dark tile
except TypeError:
    pass
raw = os.path.join(out_dir, f"_raw_{icon}.png")
sc.render.filepath = raw
bpy.ops.render.render(write_still=True)

if BARE:   # vanilla look: the asset, a light contact shadow, premultiplied box downsample to 256 (no edge fringe)
    def _load(p):
        im = bpy.data.images.load(p)
        im.colorspace_settings.name = "sRGB"
        return np.array(im.pixels[:], dtype=np.float64).reshape(N, N, 4)
    full = _load(raw)                                   # asset + caught shadow
    gp.hide_render = True                               # second pass: the asset alone, its own clean alpha
    raw_obj = os.path.join(out_dir, f"_raw_{icon}_obj.png")
    sc.render.filepath = raw_obj
    bpy.ops.render.render(write_still=True)
    obj = _load(raw_obj)
    oa = obj[..., 3]
    # the shadow = what the catcher added outside the asset. It also catches sky occlusion and noise, a faint haze over
    # the whole canvas (alpha < ~0.08) that draws a gray square on a dark menu tile: floored away. Vanilla thumbnails
    # carry almost no shadow, so what is left is drawn at BARE_SHADOW density (a near-black slab at full density).
    sa = np.clip((full[..., 3] - oa) / np.maximum(1.0 - oa, 1e-6), 0.0, 1.0)
    sa = np.clip((sa - BARE_FLOOR) / (1.0 - BARE_FLOOR), 0.0, 1.0) * BARE_SHADOW
    px = np.zeros_like(obj)
    px[..., 3] = oa + (1.0 - oa) * sa
    px[..., :3] = obj[..., :3] * (oa / np.maximum(px[..., 3], 1e-6))[..., None]   # straight color over a black shadow
    pm = np.concatenate([px[..., :3] * px[..., 3:4], px[..., 3:4]], axis=2)
    small = pm.reshape(256, 4, 256, 4, 4).mean(axis=(1, 3))
    a = small[..., 3:4]
    small[..., :3] = np.where(a > 1e-6, small[..., :3] / np.maximum(a, 1e-6), 0.0)
    res = bpy.data.images.new(icon, 256, 256, alpha=True)
    res.colorspace_settings.name = "sRGB"
    res.pixels[:] = np.clip(small, 0, 1).reshape(-1).tolist()
    res.filepath_raw = os.path.join(out_dir, f"{icon}.png")
    res.file_format = "PNG"
    res.save()
    ys, xs = np.where(small[..., 3] > 0.10)
    al = small[..., 3]
    edge = float(max(al[:2].max(), al[-2:].max(), al[:, :2].max(), al[:, -2:].max()))
    print("ICON", icon, res.filepath_raw, "bare", "objs", len(keep), "ortho", round(ortho, 1),
          "ink x", int(xs.min()), int(xs.max()), "y", int(ys.min()), int(ys.max()), "edge alpha", round(edge, 3))
    haze = float((al > 0).mean())
    print("ICON_HAZE", icon, "covered share", round(haze, 3))
    if edge > 0.02:     # the asset or its shadow is cut by the canvas edge: a hard edge on a transparent icon
        raise SystemExit(f"bare icon {icon}: alpha {edge:.2f} on the canvas edge (cut). Lower ICON_BARE_BOX")
    sys.exit(0)

# ---- compose on the style-E card -------------------------------------------------------------------------
img = bpy.data.images.load(raw)
img.colorspace_settings.name = "sRGB"
px = np.array(img.pixels[:], dtype=np.float64).reshape(N, N, 4)[::-1]   # top row first, sRGB-encoded, straight alpha
hz = min(max(y_back - 4.0, 70.0), 188.0)                                # snow horizon on the canvas
X = SI.X
layers = [SI.sky()]
layers += SI.far_range([(-14, hz - 18), (30, hz - 52), (70, hz - 26), (118, hz - 60), (166, hz - 28),
                        (214, hz - 54), (270, hz - 22)])
_, g = SI.ground(lambda x: hz - 5.0 * np.sin(np.pi * (x + 14.0) / 284.0), hz)
layers.append(g)
pc = SI.cov(SI.plate_sdf())


def comp(rgb, al, ly):
    c = SI.cov(ly.sdf)
    a = c * ly.a * pc
    col = np.asarray(ly.col, np.float64)
    if col.ndim == 1:
        col = col[None, None, :]
    return rgb * (1 - a)[..., None] + col * a[..., None], al * (1 - a) + a


rgb = np.zeros((N, N, 3))
al = np.zeros((N, N))
for ly in layers:
    rgb, al = comp(rgb, al, ly)
a3 = px[..., 3] * pc
rgb = rgb * (1 - a3)[..., None] + (px[..., :3] * 255.0) * a3[..., None]
for ly in SI.finish():
    rgb, al = comp(rgb, al, ly)
out = np.concatenate([rgb, (al * 255.0)[..., None]], axis=2)
small = out.reshape(256, 4, 256, 4, 4).mean(axis=(1, 3))
res = bpy.data.images.new(icon, 256, 256, alpha=True)
res.colorspace_settings.name = "sRGB"
res.pixels[:] = (np.clip(small[::-1], 0, 255) / 255.0).reshape(-1).tolist()
res.filepath_raw = os.path.join(out_dir, f"{icon}.png")
res.file_format = "PNG"
res.save()
print("ICON", icon, res.filepath_raw, "objs", len(keep), "ortho", round(ortho, 1), "horizon", round(hz, 1),
      "front", round(y_front, 1))
