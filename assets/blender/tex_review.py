"""
tex_review.py - fixed-camera texture review renders, per-pixel diffs and side-by-side sheets
(docs/PIPELINE.md "Texture pass"; first used by the L2 hut texture pilot, 2026-10-29).

Headless only:
  blender --background --factory-startup --python-exit-code 1 --python assets/blender/tex_review.py -- <mode> ...

render  --out DIR --tag TAG --ours "<asset_dir>|<Name>[|<maps_dir>|<maps_name>]" --recipe KEY
        [--cams FILE] [--ground flat|recipe] [--shots name,...] [--samples N]
    Renders one of our exported assets with the T16 rig (cs2_preview: Nishita sky, sun 55 deg at
    rotation 135, AgX, 45 deg vertical fov, EEVEE) at 1920 x 1080 (1080p game frame: screen
    px/m = 1,304 / d). <maps_dir>|<maps_name> is where the LOD0 maps live when they are not in the
    asset folder (a tiled building draws the family sheet: <out>/_SkiTileSheet<L>|SkiTileSheet<L>).
    Cameras: SHOTS below, resolved against the asset's bounds and facade on the FIRST run and
    written to --cams (default DIR/cams.json); every later run that passes the same file renders
    from the SAME matrices, so a before / after pair differs only by what changed in the asset.
    --ground recipe: a plinth recipe gets its design slope as the ground (Recipe.plinth with the
    lot front at z 0: the L2 hut falls 35 % toward the downhill side), else a flat plane.
render  --out DIR --tag vanilla --vanilla <RenderPrefab base> --bench DIR [--vanilla-scripts DIR]
    One vanilla building under the same light and the same shot definitions (its own bounds).
    Reads the game's .cok content through the research readers in
    docs/research/art/vanilla-detail-benchmark.data/scripts (vlib / pxlib; local only, git-ignored,
    so a worktree passes the main checkout's folder as --vanilla-scripts) with the content index
    (cidindex.json, vtmap.json) in --bench.
diff    --a DIR_A --b DIR_B --out DIR
    Per shot: |A - B| amplified x4 (diff_<shot>.png), a before / after / diff strip
    (pair_<shot>.png) and diff.json (mean abs difference in 8-bit steps, share of pixels that moved
    more than 2 and 8 steps). Renders of an unchanged asset diff to 0 (EEVEE is deterministic at a
    fixed sample count): measure that floor once per machine before reading a real diff.
sheet   --out FILE --cols "LABEL=DIR,LABEL=DIR,..." [--shots name,...] [--scale 3]
    One row per shot, one column per directory (missing frames stay gray).

Read-only on the game, the mod and every asset folder; writes only under --out.
"""

import json
import math
import os
import re
import sys

import bpy
import mathutils
import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_preview as P   # noqa: E402

REPO = os.path.normpath(os.path.join(_HERE, os.pardir, os.pardir))
VANILLA_SCRIPTS = os.path.join(REPO, "docs", "research", "art", "vanilla-detail-benchmark.data", "scripts")
RES = (1920, 1080)
SAMPLES = 64

# (name, camera direction from the target (x, y, z), distance m, aim): aim "facade" = the point a
# horizontal ray from the camera side hits at 3 m (so "6 m" is 6 m from a wall, whatever the size),
# "center" = the bbox center, "roof" = the bbox center raised to 85 % of the height, "low" = the
# facade point at 1 m over the ground under it (plinth / base band close-ups). Directions are in the
# EXPORTED frame (the FBX after cs2_asset.export_fbx, Z up): -Y is the arrival / road side of every
# lodge.py building, +Y the back (the weathering rules' "north").
SHOTS = (
    ("arrival_d6", (0.8, -1.0, 0.45), 6.0, "facade"),
    ("arrival_d20", (0.8, -1.0, 0.45), 20.0, "facade"),
    ("arrival_d68", (0.8, -1.0, 0.45), 68.0, "center"),
    ("back_d20", (-0.8, 1.0, 0.30), 20.0, "facade"),
    ("back_d45", (-0.5, 1.0, 0.12), 45.0, "center"),
    ("deck_d8", (-0.6, 1.0, 0.15), 8.0, "low"),
    ("base_d8", (1.0, 0.15, 0.12), 8.0, "low"),
    ("roof_d14", (0.5, -0.7, 1.1), 14.0, "roof"),
    ("side_d12", (-1.0, -0.25, 0.25), 12.0, "facade"),
    ("far_d150", (0.8, -1.0, 0.45), 150.0, "center"),
)


def _args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    import argparse
    ap = argparse.ArgumentParser(prog="tex_review.py")
    ap.add_argument("mode", choices=("render", "diff", "sheet"))
    ap.add_argument("--out", required=True)
    ap.add_argument("--tag", default="subject")
    ap.add_argument("--ours", default=None)
    ap.add_argument("--recipe", default=None)
    ap.add_argument("--vanilla", default=None)
    ap.add_argument("--bench", default=None)
    ap.add_argument("--vanilla-scripts", default=VANILLA_SCRIPTS)
    ap.add_argument("--cams", default=None)
    ap.add_argument("--ground", default="recipe", choices=("flat", "recipe"))
    ap.add_argument("--shots", default=None)
    ap.add_argument("--samples", type=int, default=SAMPLES)
    ap.add_argument("--sun-rot", type=float, default=None, help="one sun rotation (deg) for every shot")
    ap.add_argument("--raw-tint", action="store_true",
                    help="multiply the slot colors undecoded (the pilot's renders; the game decodes sRGB first)")
    ap.add_argument("--fixed-sun", action="store_true", help="the rig's 135 deg for every shot (the pilot's renders)")
    ap.add_argument("--a", default=None)
    ap.add_argument("--b", default=None)
    ap.add_argument("--cols", default=None)
    ap.add_argument("--scale", type=int, default=3)
    return ap.parse_args(a)


# ---------------------------------------------------------------- scene
def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)


def _import(path):
    before = set(bpy.data.objects)
    op = bpy.ops.wm.fbx_import if hasattr(bpy.ops.wm, "fbx_import") else bpy.ops.import_scene.fbx
    op(filepath=path)
    return [o for o in bpy.data.objects if o not in before and o.type == "MESH"][0]


def _apply(o, M):
    o.matrix_world = M @ o.matrix_world
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def _frame_fix(o):
    """cs2_asset.export_fbx bakes cm and Y-up into the vertices and Blender 5.2's importer keeps them:
    scale back to meters and rotate Z-up (the bl_rollout_render.py rule: always rotate)."""
    bpy.context.view_layer.update()
    W = np.array([(o.matrix_world @ v.co)[:] for v in o.data.vertices])
    sc = 0.01 if float((W.max(0) - W.min(0)).max()) > 200.0 else 1.0
    M = mathutils.Matrix.Scale(sc, 4)
    if (W.max(0) - W.min(0))[1] > 0.0:
        M = mathutils.Matrix.Rotation(math.radians(90.0), 4, "X") @ M
    return M


def _glass_material(name, dark):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (0.02, 0.025, 0.03, 1.0) if dark else (0.05, 0.06, 0.07, 1.0)
    b.inputs["Roughness"].default_value = 0.06
    if not dark:
        b.inputs["Alpha"].default_value = 0.35
        try:
            m.surface_render_method = "BLENDED"
        except (AttributeError, TypeError):
            m.blend_method = "BLEND"
    return m


def load_ours(asset_dir, name, tag):
    """The LOD0 FBX + its `_Win` (dark glass, the game draws interiors) and `_Gls` (clear glass)."""
    o = _import(os.path.join(asset_dir, f"{name}.fbx"))
    M = _frame_fix(o)
    _apply(o, M)
    o.name = tag
    for lay in list(o.data.uv_layers)[1:]:
        o.data.uv_layers.remove(lay)
    subs = []
    for sfx, dark in (("Win", True), ("Gls", False)):
        p = os.path.join(asset_dir, f"{name}_{sfx}.fbx")
        if os.path.exists(p):
            s = _import(p)
            _apply(s, M)
            s.data.materials.clear()
            s.data.materials.append(_glass_material(f"{tag}_{sfx}", dark))
            s.name = f"{tag}_{sfx}"
            subs.append(s)
    return o, subs


def load_vanilla(rp, bench, scripts=VANILLA_SCRIPTS):
    """A vanilla RenderPrefab's LOD0 mesh + its decoded 4096^2 maps (written as PNGs into bench/maps)."""
    sys.path.insert(0, scripts)
    import vlib
    vlib.BENCH = bench
    import pxlib
    pxlib.TMP = os.path.join(bench, "tmp")
    os.makedirs(pxlib.TMP, exist_ok=True)
    import cs2_asset
    import re
    d = vlib.render_prefab(rp + " Mesh")
    if d is None:
        raise RuntimeError(f"vanilla RenderPrefab '{rp} Mesh' not in the dump")
    gb, _r, _e = vlib.read_cid(d["geom"])
    g = vlib.parse_geometry(gb)
    m0 = g["meshes"][0]
    pos = vlib.attr_float(*m0["attrs"][0])[:, :3].astype(np.float64)
    uv = vlib.attr_float(*m0["attrs"][4])[:, :2].astype(np.float64)
    s = g["subs"][0]
    tris = m0["indices"][s["istart"]: s["istart"] + s["icount"]].reshape(-1, 3)
    # game (x right, y up, z fwd; left-handed) -> Blender (-x, -z, y): a mirror, so the winding flips
    V = np.stack([-pos[:, 0], -pos[:, 2], pos[:, 1]], 1)
    me = bpy.data.meshes.new(rp)
    me.from_pydata(V.tolist(), [], tris[:, ::-1].tolist())
    me.update()
    lay = me.uv_layers.new(name="UVUnique")
    loops = np.array([lp.vertex_index for lp in me.loops])
    lay.data.foreach_set("uv", uv[loops].astype(np.float32).ravel())
    o = bpy.data.objects.new("vanilla", me)
    bpy.context.scene.collection.objects.link(o)
    cs2_asset.shade_by_angle(o, 66.0, weighted=False)
    _d2, hs = pxlib.tex_handles(rp)
    ent = hs[0]
    sb, _r, _e = vlib.read_cid(d["surf"][0])
    names = vlib.parse_surface(sb)
    maps = {}
    if "vts" in ent:
        b, H = ent["vts"]
        pre = [k for k in range(H["stacks"][0]["nlayers"])
               if H["layers"][0][k]["pre"] and H["layers"][0][k]["ppf"] == 0]
        inv = {v: k for k, v in names.items()}
        for slot, k in enumerate(pre):
            prop = inv.get(H["layers"][0][k]["tex"])
            arr, *_ = pxlib.decode_vts(ent, slot, 0, None)
            maps[prop] = arr
    else:
        for prop, (rel, en) in ent["maps"].items():
            arr, *_ = pxlib.decode_vtt(rel, en, 0, None)
            maps[prop] = arr
    A = maps["_BaseColorMap"].shape[0]
    nm = maps["_NormalMap"]
    x = nm[..., 3] * 2 - 1
    y = nm[..., 1] * 2 - 1
    z = np.sqrt(np.clip(1 - x * x - y * y, 0, 1))
    nrgb = np.stack([x, y, z], -1) * 0.5 + 0.5
    mdir = os.path.join(bench, "maps")
    os.makedirs(mdir, exist_ok=True)
    mname = rp.replace("_", "") + "V"
    for suf, arr, is_data in (("BaseColor", maps["_BaseColorMap"], False), ("Normal", nrgb, True),
                              ("MaskMap", maps["_MaskMap"], True), ("ControlMask", maps["_ControlMask"], True)):
        a = np.ones((A, A, 4), np.float32)
        n = min(arr.shape[-1], 4)
        a[..., :n] = arr[..., :n]
        if suf in ("BaseColor", "Normal"):
            a[..., 3] = 1.0
        cs2_asset._write_png_raw(os.path.join(mdir, f"{mname}_{suf}.png"), f"{mname}_{suf}", a, A, is_data)
    tint = None
    t = vlib.dump_find("RenderPrefab", rp + " Mesh")
    j = t.find("m_ColorVariations") if t else -1
    if j >= 0:
        k = t.find("m_Colors", j)
        nums = [float(v) for v in re.findall(r"^\s*([-\d.eE+]+),?\s*$", t[k:k + 1500], re.M)[:12]]
        if len(nums) == 12:
            tint = {"R": tuple(nums[0:3]), "G": tuple(nums[4:7]), "B": tuple(nums[8:11])}
    return o, mdir, mname, tint


def shipped_slots(asset_dir, name):
    """The slot colors the game multiplies in: the manifest's `tiled.slots_v2.defaults` (= the mesh prefab's VariationSet
    after --slots v2), or None. recipe.slots is the pre-slots-v2 table: with it the slot-2 trim boards and the slot-3
    casings render in colors the game never shows (P1: orange fascias, pale casings)."""
    base = re.sub(r"_LOD[0-9]$", "", name)
    path = os.path.join(asset_dir, f"{base}.manifest.json")
    if not os.path.exists(path):
        return None
    try:
        d = json.load(open(path)).get("tiled", {}).get("slots_v2", {}).get("defaults")
    except (OSError, ValueError):
        return None
    if not d:
        return None
    return {"RGB"[i]: tuple(d[f"slot{i + 1}"]) for i in range(3) if f"slot{i + 1}" in d}


def game_tint(tint):
    """Slot colors as the game multiplies them. A VariationSet color is stored sRGB-encoded and the game multiplies its
    LINEAR value (Game.Rendering/BatchDataSystem.cs:716-718, 1.6.0f1: meshColor.m_ColorSet.m_Channel0.linear;
    cs2_tilesheet.slot_default_for is the inverse). cs2_preview._tinted_base_image multiplies the tuple as given, so the
    decode happens here: without it every recolor part renders too light (L5 glulam: a light tan where the game shows mid
    walnut; fascias 6 x too bright)."""
    return {ch: tuple(float(x) for x in P._srgb_to_linear(np.asarray(rgb, np.float64))) for ch, rgb in tint.items()}


def recipe_tint(key, asset_dir=None, name=None, raw=False):
    import bldg_recipes as R
    rec = R.get_recipe(key)
    tint = shipped_slots(asset_dir, name) if asset_dir and name else None
    tint = tint or {"RGB"[k - 1]: tuple(v) for k, v in rec.slots.items()}
    return (tint if raw else game_tint(tint)), rec


# Sun rotation per shot (sky node degrees; the rig's default 135 puts the sun toward +X / -Y, behind the arrival
# cameras). The back and west-side shots get the sun behind THEIR camera, or the facade they look at stands in full
# shade and its weathering bands cannot be judged. --sun-rot overrides every shot; --fixed-sun keeps 135 everywhere
# (the L2 pilot's renders).
SHOT_SUN = {"back_d20": 315.0, "back_d45": 315.0, "deck_d8": 315.0, "side_d12": 45.0}


def set_sun(world, sun, rot_deg, elevation_deg):
    sky = next(n for n in world.node_tree.nodes if n.bl_idname == "ShaderNodeTexSky")
    sky.sun_rotation = math.radians(rot_deg)
    r, e = math.radians(rot_deg), math.radians(elevation_deg)
    d = mathutils.Vector((-math.cos(r) * math.cos(e), -math.sin(r) * math.cos(e), math.sin(e)))
    sun.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()


def ground(rig, rec, mode):
    """Snow-white ground: the design slope of a plinth recipe (z 0 at the lot front, falling toward the
    back at the grade that Recipe.plinth[0] reaches at the deepest support), else a plane at z 0."""
    size = 3000.0
    bpy.ops.mesh.primitive_plane_add(size=size, location=(0, 0, 0))
    gnd = rig.track(bpy.context.active_object)
    gnd.name = "PGround"
    if mode == "recipe" and rec is not None and getattr(rec, "base_mode", None) == "plinth":
        mod = sys.modules.get(getattr(rec.builder, "__module__", ""), None)
        grade = float(getattr(mod, "SLOPE", 0.35))   # the recipe's design slope (L2: 0.35)
        front = 4.0 * rec.lot[1]                     # the lot front edge (exported -Y side)
        # exported frame: arrival side at y = -front, z 0 there; the ground falls toward +y
        gnd.rotation_euler = (-math.atan(grade), 0.0, 0.0)
        gnd.location = (0.0, -front, 0.0)
    mat = rig.track_data(bpy.data.materials, bpy.data.materials.new("PGround"))
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (P.GROUND_ALBEDO,) * 3 + (1.0,)
    bsdf.inputs["Roughness"].default_value = 0.95
    gnd.data.materials.append(mat)
    return gnd


def _bvh(o):
    from mathutils.bvhtree import BVHTree
    return BVHTree.FromObject(o, bpy.context.evaluated_depsgraph_get())


def aim_point(o, kind, direction, gnd):
    import lift_tower
    c, size = lift_tower.world_bounds(o)
    if kind == "center":
        return mathutils.Vector(c)
    if kind == "roof":
        return mathutils.Vector((c.x, c.y, c.z - size.z / 2 + 0.85 * size.z))
    bvh = _bvh(o)
    d = mathutils.Vector((direction[0], direction[1], 0.0)).normalized()
    if kind == "low":
        gb = _bvh(gnd)
        z0 = None
        for off in (0.0, -0.15, 0.15):
            side = mathutils.Vector((-d.y, d.x, 0.0)) * off * max(size.x, size.y)
            for z in np.linspace(c.z - size.z / 2 + 0.2, c.z + size.z / 2, 40):
                org = mathutils.Vector((c.x, c.y, z)) + d * (size.length + 50.0) + side
                hit = bvh.ray_cast(org, -d, 10000.0)
                if hit[0] is None:
                    continue
                g = gb.ray_cast(hit[0] + d * 0.05 + mathutils.Vector((0, 0, 50)), mathutils.Vector((0, 0, -1)), 200.0)
                gz = g[0].z if g[0] is not None else 0.0
                if hit[0].z >= gz + 0.6:
                    z0 = hit[0]
                    break
            if z0 is not None:
                return z0 + mathutils.Vector((0, 0, 0.4))
        kind = "facade"
    z = min(3.0, (c.z + size.z / 2) * 0.5)
    best = None
    for off in (0.0, -0.15, 0.15, -0.3, 0.3):
        side = mathutils.Vector((-d.y, d.x, 0.0)) * off * max(size.x, size.y)
        org = mathutils.Vector((c.x, c.y, z)) + d * (size.length + 50.0) + side
        hit = bvh.ray_cast(org, -d, 10000.0)
        if hit[0] is not None and (best is None or hit[3] < best[1]):
            best = (hit[0], hit[3])
    if best is not None:
        return best[0]
    return mathutils.Vector((c.x, c.y, z))


def resolve_cams(o, gnd, shots):
    cams = {}
    for name, direction, dist, kind in shots:
        t = aim_point(o, kind, direction, gnd)
        d = mathutils.Vector(direction).normalized()
        loc = t + d * dist
        rot = (t - loc).to_track_quat("-Z", "Y").to_euler()
        cams[name] = {"loc": list(loc), "rot": list(rot), "dist": dist, "aim": kind,
                      "target": list(t)}
    return cams


def render(a):
    out = os.path.join(a.out, a.tag)
    os.makedirs(out, exist_ok=True)
    clear()
    rec, tint, subs = None, None, []
    if a.ours:
        parts = a.ours.split("|")
        adir, name = parts[0], parts[1]
        mdir, mname = (parts[2], parts[3]) if len(parts) >= 4 else (adir, name)
        o, subs = load_ours(adir, name, a.tag)
        if a.recipe:
            tint, rec = recipe_tint(a.recipe, adir, name, raw=a.raw_tint)
    elif a.vanilla:
        o, mdir, mname, tint = load_vanilla(a.vanilla, a.bench, a.vanilla_scripts)
    else:
        raise SystemExit("render needs --ours or --vanilla")
    rig = P._Rig(o, a.samples)
    cam = P._make_camera(rig)
    world, sun = P._make_world(rig, **P.NOON)
    gnd = ground(rig, rec, a.ground if a.ours else "flat")
    sc = rig.scene
    sc.render.resolution_x, sc.render.resolution_y = RES
    P._assign(o, P._pbr_material(rig, mdir, mname, tint, mat_name=f"{a.tag}_PBR"))
    for s in subs:
        s.hide_render = False
    o.hide_render = False
    gnd.hide_render = False
    shots = SHOTS if not a.shots else tuple(s for s in SHOTS if s[0] in a.shots.split(","))
    cams_path = a.cams or os.path.join(a.out, "cams.json")
    key = "vanilla:" + a.vanilla if a.vanilla else "ours"
    store = json.load(open(cams_path)) if os.path.exists(cams_path) else {}
    cams = store.get(key) or {}
    missing = [s for s in shots if s[0] not in cams]
    if missing:
        cams.update(resolve_cams(o, gnd, missing))
        store[key] = cams
        json.dump(store, open(cams_path, "w"), indent=1)
        print(f"[tex_review] cameras resolved for {[s[0] for s in missing]} -> {cams_path}")
    written, suns = {}, {}
    for name, *_rest in shots:
        cdef = cams[name]
        rot = P.SKY_ROTATION_DEG if a.fixed_sun else SHOT_SUN.get(name, P.SKY_ROTATION_DEG)
        rot = float(a.sun_rot) if a.sun_rot is not None else rot
        set_sun(world, sun, rot, P.NOON["elevation_deg"])
        suns[name] = rot
        cam.location = mathutils.Vector(cdef["loc"])
        cam.rotation_euler = mathutils.Euler(cdef["rot"])
        path = os.path.join(out, f"{name}.png")
        P._render(rig, path, engine="EEVEE")
        written[name] = os.path.basename(path)
        print(f"[tex_review] {a.tag} {name} -> {path}")
    json.dump({"tag": a.tag, "ours": a.ours, "vanilla": a.vanilla, "recipe": a.recipe, "cams": cams_path,
               "samples": a.samples, "renders": written, "sun_rot": suns, "tint": tint}, open(os.path.join(out, "renders.json"), "w"), indent=1)


# ---------------------------------------------------------------- images
def load_png(p):
    return P._load_rgba(p)[::-1]                    # top-down rows, sRGB-encoded floats


def save_png(p, img):
    P._write_rgba(p, np.ascontiguousarray(img[::-1]).astype(np.float32))


def down(img, f):
    if f <= 1:
        return img
    h, w = img.shape[0] // f * f, img.shape[1] // f * f
    return img[:h, :w].reshape(h // f, f, w // f, f, -1).mean((1, 3))


def _font():
    import ski_icons_sheet
    return ski_icons_sheet.FONT


def stamp(img, text, x, y, cell=2, col=(1, 1, 1)):
    F = _font()
    for gi, ch in enumerate(text.upper()):
        gl = F.get(ch, F[" "])
        for ry, row in enumerate(gl):
            for rx, bit in enumerate(row):
                if bit == "1":
                    yy, xx = y + ry * cell, x + (gi * 6 + rx) * cell
                    img[yy:yy + cell, xx:xx + cell, :3] = col


def grid(rows, path, tw, th, bar=26, gap=6, cell=2):
    ncol = max(len(r) for r in rows)
    W = ncol * tw + (ncol + 1) * gap
    H = len(rows) * (th + bar) + (len(rows) + 1) * gap
    img = np.full((H, W, 4), 0.18, np.float32)
    img[..., 3] = 1.0
    for r, row in enumerate(rows):
        for c, (tile, lab) in enumerate(row):
            y0, x0 = gap + r * (th + bar + gap), gap + c * (tw + gap)
            if tile is not None:
                img[y0 + bar:y0 + bar + th, x0:x0 + tw] = tile[:th, :tw]
            stamp(img, lab, x0 + 6, y0 + 7, cell=cell)
    save_png(path, img)
    print(f"[tex_review] sheet -> {path}")


def diff(a):
    os.makedirs(a.out, exist_ok=True)
    names = sorted(f[:-4] for f in os.listdir(a.a) if f.endswith(".png") and os.path.exists(os.path.join(a.b, f)))
    stats = {}
    for n in names:
        A = load_png(os.path.join(a.a, n + ".png"))
        B = load_png(os.path.join(a.b, n + ".png"))
        d = np.abs(A[..., :3] - B[..., :3]).max(-1)
        steps = d * 255.0
        stats[n] = {"mean_steps": round(float(steps.mean()), 3), "p99_steps": round(float(np.percentile(steps, 99)), 2),
                    "share_gt2": round(float((steps > 2).mean()), 4), "share_gt8": round(float((steps > 8).mean()), 4)}
        heat = np.zeros(A.shape, np.float32)
        v = np.clip(d * 4.0, 0, 1)
        heat[..., 0] = v
        heat[..., 1] = np.clip(v * 2 - 1, 0, 1)
        heat[..., 2] = np.where(steps > 2, 0.25, 0.0)
        heat[..., 3] = 1.0
        save_png(os.path.join(a.out, f"diff_{n}.png"), heat)
        f = 2
        tw, th = RES[0] // f, RES[1] // f
        grid([[(down(A, f), f"BEFORE {n}"), (down(B, f), f"AFTER {n}"), (down(heat, f), "DIFF X4")]],
             os.path.join(a.out, f"pair_{n}.png"), tw, th)
    json.dump(stats, open(os.path.join(a.out, "diff.json"), "w"), indent=1)
    for n, s in stats.items():
        print(f"[tex_review] diff {n}: {s}")


def sheet(a):
    cols = [c.split("=", 1) for c in a.cols.split(",")]
    first = cols[0][1]
    names = [s[0] for s in SHOTS if os.path.exists(os.path.join(first, s[0] + ".png"))]
    if a.shots:
        names = [n for n in names if n in a.shots.split(",")]
    f = a.scale
    tw, th = RES[0] // f, RES[1] // f
    rows = []
    for n in names:
        row = []
        for lab, d in cols:
            p = os.path.join(d, n + ".png")
            row.append((down(load_png(p), f) if os.path.exists(p) else None, f"{lab} {n}"))
        rows.append(row)
    grid(rows, a.out, tw, th)


if __name__ == "__main__":
    A = _args()
    {"render": render, "diff": diff, "sheet": sheet}[A.mode](A)
