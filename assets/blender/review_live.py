"""
review_live.py - the fixed snippet for the LIVE Blender review (ROUND-SPEC R10).

WHY
---
Generators never run in the live session (their clear_scene() wipes every object, the
finish pass leaks scene settings, Cycles bakes block the UI: pipeline-gap.md section 6).
They build headless and, with `--review`, write `assets/preview/review/<Name>.blend`
(cs2_review.write_pack). This file is the only code that touches the live session: it
LINKS those packs (read-only, so the generator stays the source), places them on a grid,
and drives LOD / tint / lighting / camera state. It is self-contained (bpy + stdlib: it
imports no repo module and runs no operator that edits geometry) and is sent VERBATIM
through the MCP execute tool; executing it only defines constants and functions. Append
the call(s) to the same message, e.g.

    print(ensure_scene()); print(load("SkiLiftChair6L")); print(cams("SkiLiftChair6L"))

or call install() once and later use bpy.app.driver_namespace["cs2r_review"].load(...).

Scene contract (read headless from assets/preview/review/review-2026-10-10.blend,
2026-10-11): scene "Scene"; collection "Review" (ReviewCam = camera, lens 35, TRACK_TO
ReviewTarget; ReviewSun = SUN 3.0 at rot (50, 0, 35) deg; ReviewTarget = empty at z 4) with
children "Review_Scale" (LotGrid8m 96 x 96 m, ScalePerson_body/_head 1.8 m at (3, -3),
ScaleTower8m_mast/_crossarm at (-6, -3)), "Kagelok_Ref" (root at x 0, scale 0.30) and
"Gondola_Ref" (roots at x 30, scale 0.27). All of these are KEPT: this script works in the
ACTIVE scene, creates only what is missing and never deletes anything it did not create
(everything it creates is named CS2R_* or ScaleRuler3m / ScaleTowerFG8N).

Placement: one asset per 60 m grid cell at x = 60 k (k >= FIRST_CELL; cell 0 holds the two
reference models). A pack is instanced through one COLLECTION-instance empty per role
(LOD0 / LOD1 / LOD2 / SUB) under a root empty CS2R_<name>_root, all in a local collection
CS2R_<name>_Live under "Review". load() re-includes / unhides "Review" and the _Live
collection in the view layer when they are excluded or hidden (reported as
"unhidden_layers"); lod() reports a shown role that is still invisible (a collection hidden
later) as ok False with "hidden_roles". Tints and the window-glow toggle are custom properties on
those empties ("cs2r_slot1..3" = 3 floats, "cs2r_win_emit" = 0/1) read by the pack
material's INSTANCER Attribute nodes (cs2_review.SLOT_ATTR / WIN_EMIT_ATTR) - the linked
material itself stays read-only.

Lighting = cs2_preview.py:78-91 so the live view matches the PNG previews: Nishita sky
(5.x MULTIPLE_SCATTERING, no disc) rotation 135 deg, altitude 1800 m, strength 0.07; sun
55 deg / 4.0 at noon, 4 deg / 0.4 at dusk, aimed with cs2_preview.py:330-336's formula;
AgX; 45 deg vertical fov (cs2_preview.py:358-361). ensure_scene(lighting=False) gives back
the world (or "no world": stored as "") and the view transform it replaced. Cameras:
cs2_preview.DEFAULT_VIEWS (cs2_preview.py:72-76) + "back" (lift_terminal.py:1291), field
cameras at the pack's predicted LOD switch distances and at the Round L acceptance
distances 30/140/280/600 m, and a turntable empty rotating 360 deg over 240 frames.
props(name=None, on=None) shows / hides the VANILLA-PROP STAND-INS (DECISIONS 183): the labeled,
semi-transparent boxes cs2_review.write_pack puts in a pack's SUB collection, one per manifest
sub-object row (benches, bins, lamps, tables ...; pack only, never exported). SUB's other contents
(`_Win` glass) stay: the SUB instance swaps between the pack's SUB and SUB_NOPROPS collections.
load() / status() / stand_ins() report the stand-in count per pack ("props").
labels() puts an upright "<cell>  <name>" sign in front of every loaded pack (CS2R_Labels);
people() stands a 1.8 m mannequin beside each (CS2R_People, one shared CS2R_Mannequin180 mesh)
(2026-10-17: a person for scale, a sign to tell the cell).
stand_ins() adds a 3 m story ruler (the `_Win` slice height) and the shipped
SkiLiftTowerFG8N into Review_Scale as a COLLECTION instance of its own review pack
(<review_dir>/SkiLiftTowerFG8N.blend, LOD0), built HEADLESS by
`blender --background --factory-startup --python assets/blender/cs2_review.py -- --stand-in`
(cs2_review.write_standin_pack): the FBX import and its Y-up fix never run live (R10).

Every public function returns a dict ({"ok": False, "error": ...} on an expected failure).
"""

import math
import os

import bpy
import mathutils


# --------------------------------------------------------------------------------------
# Constants
# --------------------------------------------------------------------------------------

# The repo root comes from the environment: set MODULAR_SKI_RESORT_REPO to the checkout folder before
# starting Blender (no user-specific path lives in this file). Without it, the pack folder is
# inferred from the open .blend when that file sits in <repo>/assets/preview/review/, and failing
# that _review_dir() raises with this instruction (pass review_dir= to load()/install() instead).
REPO_ROOT = os.environ.get("MODULAR_SKI_RESORT_REPO", "")
if not REPO_ROOT and bpy.data.filepath:
    _here = os.path.dirname(os.path.abspath(bpy.data.filepath))
    if os.path.normcase(os.path.normpath(_here)).endswith(os.path.normcase(os.path.join("assets", "preview", "review"))):
        REPO_ROOT = os.path.normpath(os.path.join(_here, "..", "..", ".."))
REVIEW_DIR = os.path.join(REPO_ROOT, "assets", "preview", "review") if REPO_ROOT else ""
FG8N_PACK = "SkiLiftTowerFG8N"        # <review_dir>/SkiLiftTowerFG8N.blend (cs2_review.STANDIN_FBX)
STANDIN_CMD = ("blender --background --factory-startup --python assets/blender/cs2_review.py "
               "-- --stand-in")

PACK_PREFIX = "CS2R_"                 # cs2_review.PACK_PREFIX
ROLES = ("LOD0", "LOD1", "LOD2", "SUB")
NOPROPS_ROLE = "SUB_NOPROPS"          # cs2_subobj.NOPROPS_ROLE: SUB without the prop stand-ins
SLOT_ATTR = "cs2r_slot{}"             # cs2_review.SLOT_ATTR
WIN_EMIT_ATTR = "cs2r_win_emit"       # cs2_review.WIN_EMIT_ATTR
SLOT_NAMES = {"1": 1, "2": 2, "3": 3, "R": 1, "G": 2, "B": 3, "STEEL": 1, "PAD": 2}

GRID_M = 60.0
FIRST_CELL = 1                        # cell 0 = Kagelok_Ref (x 0) + Gondola_Ref (x 30)
LINEUP_GAP_M = 10.0

REVIEW_COLL = "Review"
SCALE_COLL = "Review_Scale"
REF_COLLS = ("Kagelok_Ref", "Gondola_Ref")
SUN_NAME = "ReviewSun"
CAM_NAME = "ReviewCam"
TARGET_NAME = "ReviewTarget"
KEEP_OBJECTS = ("LotGrid8m", SUN_NAME, CAM_NAME, TARGET_NAME)
KEEP_PREFIXES = ("ScalePerson_", "ScaleTower8m_")

SKY_WORLD = "CS2R_ReviewSky"
SKY_ROTATION_DEG = 135.0              # cs2_preview.SKY_ROTATION_DEG
SKY_ALTITUDE_M = 1800.0               # cs2_preview.SKY_ALTITUDE_M
SKY_STRENGTH = 0.07                   # cs2_preview.SKY_STRENGTH
NOON = (55.0, 4.0)                    # cs2_preview.NOON (elevation deg, sun energy)
DUSK = (4.0, 0.4)                     # cs2_preview.DUSK
SUN_ANGLE_DEG = 0.53
VIEW_TRANSFORM = "AgX"

VERTICAL_FOV_DEG = 45.0               # cs2_preview.VERTICAL_FOV_DEG
SENSOR_HEIGHT_MM = 24.0
FRAME_MARGIN = 1.12                   # lift_tower.frame_camera margin
CAM_CLIP_START = 0.1
CAM_CLIP_END = 20000.0
DEFAULT_VIEWS = (                     # cs2_preview.py:72-76
    ("three_quarter", (1.0, -1.2, 0.45)),
    ("front",         (0.85, 0.9, 0.28)),
    ("line_mouth",    (0.1, 1.0, 0.18)),
)
BACK_VIEW = ("back", (-1.0, -0.8, 0.35))        # lift_terminal.py:1291
FIELD_DIR = (1.0, -1.2, 0.45)                   # field cameras along the three_quarter view
FIELD_DISTANCES_M = (30.0, 140.0, 280.0, 600.0)  # Round L acceptance distances
TURNTABLE_FRAMES = 240
TURNTABLE_START = 1

RULER_NAME = "ScaleRuler3m"
STOREY_M = 3.0                        # the `_Win` floor-slice height
RULER_SIDE_M = 0.12
RULER_POS = (4.5, -3.0, 0.0)          # beside ScalePerson at (3, -3)
FG8N_NAME = "ScaleTowerFG8N"
FG8N_POS = (-12.0, -3.0, 0.0)         # beside the ScaleTower8m stand-in at (-6, -3)


# --------------------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------------------

def _clean(name):
    name = str(name)
    return name[len(PACK_PREFIX):] if name.startswith(PACK_PREFIX) else name


def _review_dir(review_dir=None):
    if not (review_dir or REVIEW_DIR):
        raise RuntimeError("review_live: repo root unknown; set the MODULAR_SKI_RESORT_REPO environment variable "
                           "to the repository folder and restart Blender, or pass review_dir=")
    return os.path.abspath(review_dir or REVIEW_DIR)


def _in_scene(coll, scene):
    return coll == scene.collection or coll in scene.collection.children_recursive


def _ensure_coll(name, parent, created):
    coll = bpy.data.collections.get(name)
    if coll is None or coll.library is not None:
        coll = bpy.data.collections.new(name)
        created.append(name)
    if not _in_scene(coll, bpy.context.scene):
        parent.children.link(coll)
    return coll


def _review_colls(created=None):
    created = [] if created is None else created
    review = _ensure_coll(REVIEW_COLL, bpy.context.scene.collection, created)
    scale = _ensure_coll(SCALE_COLL, review, created)
    return review, scale


def _layer_chain(lc, coll):
    """Layer collections from the view layer root down to `coll` (root excluded)."""
    if lc.collection == coll:
        return [lc]
    for ch in lc.children:
        sub = _layer_chain(ch, coll)
        if sub:
            return ([lc] if lc.collection != bpy.context.scene.collection else []) + sub
    return []


def _show_layer_chain(coll):
    """Clear exclude / hide_viewport on every layer collection down to `coll`: an
    excluded Review collection would otherwise swallow a load() with ok True and nothing
    on screen. Returns the names cleared (only what load() itself needs is touched)."""
    cleared = []
    for lc in _layer_chain(bpy.context.view_layer.layer_collection, coll):
        if lc.exclude:
            lc.exclude = False
            cleared.append(lc.collection.name)
        if lc.hide_viewport:
            lc.hide_viewport = False
            cleared.append(lc.collection.name)
    return sorted(set(cleared))


def _link_pack(path, coll_name, link=True):
    """(collection, error): link / append collection `coll_name` from the pack `path`."""
    with bpy.data.libraries.load(path, link=link, relative=True) as (src, dst):
        if coll_name not in src.collections:
            return None, f"{path} holds no collection {coll_name}"
        dst.collections = [coll_name]
    pack = dst.collections[0]
    if pack is None:
        return None, f"could not {'link' if link else 'append'} {coll_name}"
    if link:
        # Only the role children (LOD0/1/2/SUB) are referenced by the instance empties; the
        # pack collection itself would be dropped as an unused linked ID at save + reopen,
        # and reload() / props() / status() would then lose the pack. Keep it.
        pack.use_fake_user = True
    return pack, None


def _root(name):
    return bpy.data.objects.get(f"{PACK_PREFIX}{_clean(name)}_root")


def _role_empties(name):
    name = _clean(name)
    out = {}
    for o in bpy.data.objects:
        if o.get("cs2r_name") == name and o.get("cs2r_role") in ROLES:
            out[o["cs2r_role"]] = o
    return out


def _pack_coll(name, root=None):
    """The pack collection CS2R_<name> (linked from root's file, or appended)."""
    name = _clean(name)
    root = root or _root(name)
    want = os.path.normcase(os.path.abspath(root["cs2r_path"])) if root and "cs2r_path" in root \
        else None
    best = None
    for c in bpy.data.collections:
        if c.name != f"{PACK_PREFIX}{name}" or not c.get("cs2r_pack"):
            continue
        if c.library is not None and want is not None:
            lib_path = os.path.normcase(os.path.abspath(bpy.path.abspath(c.library.filepath)))
            if lib_path == want:
                return c
        best = best or c
    if best is None and root is not None and root.get("cs2r_link", True) and want \
            and os.path.exists(want):
        # A file saved before the fake-user fix and reopened: the pack collection was dropped
        # as an unused linked ID while its role children survived. Re-link it from the same
        # library (Blender reuses the existing Library datablock for the same path).
        pack, _err = _link_pack(want, f"{PACK_PREFIX}{name}", True)
        return pack
    return best


def _plain(v):
    if hasattr(v, "to_dict"):
        return v.to_dict()
    if hasattr(v, "to_list"):
        return v.to_list()
    return v


def _pack_props(pack):
    return {k: _plain(pack[k]) for k in pack.keys() if k != "cs2r_meta_json"} if pack else {}


def _bbox(pack):
    p = _pack_props(pack)
    lo = mathutils.Vector(p.get("bbox_min", (-1.0, -1.0, 0.0)))
    hi = mathutils.Vector(p.get("bbox_max", (1.0, 1.0, 2.0)))
    return lo, hi


def _switch_m(pack):
    p = _pack_props(pack)
    out = {}
    for k, v in p.items():
        if k.startswith("lod_switch_m_LOD"):
            out[k[len("lod_switch_m_"):]] = float(v)
    return dict(sorted(out.items()))


def _pack_roles(pack):
    """{role: child collection} of a pack (SUB_NOPROPS included)."""
    return {(c.get("cs2r_role") or c.name.split(".")[0]): c for c in pack.children} if pack else {}


def _prop_count(pack):
    """Vanilla-prop stand-ins in a pack (collection prop n_prop_standins; 0 on older packs)."""
    try:
        return int(pack.get("n_prop_standins", 0)) if pack else 0
    except (TypeError, ValueError):
        return 0


def _props_shown(root, pack):
    return bool(_prop_count(pack)) and bool(root.get("cs2r_props", 1)) if root else False


def _apply_props(name, root, pack):
    """Point the SUB instance at SUB (props shown) or SUB_NOPROPS per root["cs2r_props"].
    Returns the collection name used, or None when the pack has no stand-ins / no SUB empty."""
    e = _role_empties(name).get("SUB")
    if e is None or not _prop_count(pack):
        return None
    by_role = _pack_roles(pack)
    target = by_role.get("SUB") if root.get("cs2r_props", 1) else by_role.get(NOPROPS_ROLE)
    if target is None:
        return None
    if e.instance_collection != target:
        e.instance_collection = target
        e.update_tag()
    return target.name


def _prop_report():
    """{pack name: {"count": n, "shown": bool}} for every loaded pack."""
    out = {}
    for o in bpy.data.objects:
        if o.get("cs2r_root"):
            nm = o["cs2r_name"]
            pack = _pack_coll(nm, o)
            out[nm] = {"count": _prop_count(pack), "shown": _props_shown(o, pack)}
    return dict(sorted(out.items()))


def _next_cell():
    used = {int(o["cs2r_cell"]) for o in bpy.data.objects if o.get("cs2r_root")}
    k = FIRST_CELL
    while k in used:
        k += 1
    return k


def _sun_rotation(elevation_deg):
    """cs2_preview.py:330-336: direction TO the sun for sky rotation r / elevation e."""
    r, e = math.radians(SKY_ROTATION_DEG), math.radians(elevation_deg)
    d = mathutils.Vector((-math.cos(r) * math.cos(e), -math.sin(r) * math.cos(e), math.sin(e)))
    return (-d).to_track_quat("-Z", "Y").to_euler()


def _ensure_sun(review, created):
    sun = bpy.data.objects.get(SUN_NAME)
    if sun is None:
        data = bpy.data.lights.new(SUN_NAME, type="SUN")
        sun = bpy.data.objects.new(SUN_NAME, data)
        review.objects.link(sun)
        created.append(SUN_NAME)
    return sun


def _sky_world(created):
    world = bpy.data.worlds.get(SKY_WORLD)
    if world is not None:
        return world
    world = bpy.data.worlds.new(SKY_WORLD)
    if world.node_tree is None:
        world.use_nodes = True
    nt = world.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    sky = nt.nodes.new("ShaderNodeTexSky")
    sky.name = "CS2R_Sky"
    kinds = {i.identifier for i in sky.bl_rna.properties["sky_type"].enum_items}
    sky.sky_type = "MULTIPLE_SCATTERING" if "MULTIPLE_SCATTERING" in kinds else "NISHITA"
    sky.sun_disc = False
    sky.sun_rotation = math.radians(SKY_ROTATION_DEG)
    sky.altitude = SKY_ALTITUDE_M
    sky.sun_elevation = math.radians(NOON[0])
    bg.inputs["Strength"].default_value = SKY_STRENGTH
    nt.links.new(sky.outputs[0], bg.inputs["Color"])
    nt.links.new(bg.outputs[0], out.inputs["Surface"])
    created.append(SKY_WORLD)
    return world


def _set_viewport(mode):
    """Shading of every open 3D view (no-op in background mode). Returns the count."""
    n = 0
    wm = bpy.context.window_manager
    for win in getattr(wm, "windows", []):
        for area in win.screen.areas:
            if area.type != "VIEW_3D":
                continue
            sh = area.spaces.active.shading
            sh.type = mode
            if mode == "MATERIAL":
                sh.use_scene_world = True
                sh.use_scene_lights = True
            elif mode == "RENDERED":
                sh.use_scene_world_render = True
                sh.use_scene_lights_render = True
            n += 1
    return n


def _find_layer_coll(lc, name):
    if lc.collection.name == name:
        return lc
    for ch in lc.children:
        hit = _find_layer_coll(ch, name)
        if hit is not None:
            return hit
    return None


def _fcurves(obj):
    """F-curves of obj's action: legacy Action.fcurves (4.x) or the 5.x channelbag."""
    ad = obj.animation_data
    if ad is None or ad.action is None:
        return []
    act = ad.action
    if hasattr(act, "fcurves"):
        return list(act.fcurves)
    out = []
    slot = getattr(ad, "action_slot", None)
    for layer in getattr(act, "layers", []):
        for strip in layer.strips:
            cb = strip.channelbag(slot) if slot is not None else None
            if cb is not None:
                out.extend(cb.fcurves)
    return out


def _remove_objects(objs):
    n = 0
    for o in list(objs):
        data, ad = o.data, o.animation_data
        action = ad.action if ad is not None else None
        bpy.data.objects.remove(o, do_unlink=True)
        n += 1
        if data is not None and data.users == 0 and data.library is None:
            if isinstance(data, bpy.types.Camera):
                bpy.data.cameras.remove(data)
            elif isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Light):
                bpy.data.lights.remove(data)
        if action is not None and action.users == 0:
            bpy.data.actions.remove(action)
    return n


def _new_camera(name, coll, parent, loc, target):
    data = bpy.data.cameras.new(name)
    data.sensor_fit = "VERTICAL"
    data.sensor_height = SENSOR_HEIGHT_MM
    data.lens = (SENSOR_HEIGHT_MM / 2.0) / math.tan(math.radians(VERTICAL_FOV_DEG) / 2.0)
    data.clip_start = CAM_CLIP_START
    data.clip_end = CAM_CLIP_END
    cam = bpy.data.objects.new(name, data)
    coll.objects.link(cam)
    cam.parent = parent
    cam.location = loc
    cam.rotation_euler = (mathutils.Vector(target) - mathutils.Vector(loc)) \
        .to_track_quat("-Z", "Y").to_euler()
    return cam


def _make_ruler(coll):
    """3 m story ruler: three 1 m bands, alternating light / red, on a 0.12 m square."""
    import bmesh
    me = bpy.data.meshes.new(RULER_NAME)
    bm = bmesh.new()
    for i in range(int(STOREY_M)):
        res = bmesh.ops.create_cube(bm, size=1.0)
        mtx = mathutils.Matrix.Translation((0.0, 0.0, i + 0.5)) @ \
            mathutils.Matrix.Diagonal((RULER_SIDE_M, RULER_SIDE_M, 1.0, 1.0))
        bmesh.ops.transform(bm, matrix=mtx, verts=res["verts"])
        for f in {f for v in res["verts"] for f in v.link_faces}:
            f.material_index = i % 2
    bm.to_mesh(me)
    bm.free()
    light = bpy.data.materials.get(PACK_PREFIX + "RulerLight") or \
        bpy.data.materials.new(PACK_PREFIX + "RulerLight")
    light.diffuse_color = (0.9, 0.9, 0.88, 1.0)
    red = bpy.data.materials.get(PACK_PREFIX + "RulerRed") or \
        bpy.data.materials.new(PACK_PREFIX + "RulerRed")
    red.diffuse_color = (0.75, 0.08, 0.06, 1.0)
    for m in (light, red):
        if m.node_tree is not None:
            bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
            if bsdf is not None:
                bsdf.inputs["Base Color"].default_value = m.diffuse_color
    me.materials.append(light)
    me.materials.append(red)
    obj = bpy.data.objects.new(RULER_NAME, me)
    coll.objects.link(obj)
    return obj


def _link_fg8n(coll, review_dir=None):
    """ScaleTowerFG8N: a COLLECTION-instance empty of the LOD0 child of the headless
    stand-in pack (cs2_review.write_standin_pack), linked like any review pack."""
    path = os.path.join(_review_dir(review_dir), f"{FG8N_PACK}.blend")
    if not os.path.isfile(path):
        return None, None, f"no stand-in pack at {path} - build it headless: {STANDIN_CMD}"
    pack, err = _link_pack(path, f"{PACK_PREFIX}{FG8N_PACK}")
    if err:
        return None, None, err
    lod0 = next((c for c in pack.children
                 if (c.get("cs2r_role") or c.name.split(".")[0]) == "LOD0"), None)
    if lod0 is None:
        return None, pack, f"{path}: pack has no LOD0 child"
    e = bpy.data.objects.new(FG8N_NAME, None)
    e.instance_type = "COLLECTION"
    e.instance_collection = lod0
    e.empty_display_size = 1.0
    coll.objects.link(e)
    return e, pack, None


# --------------------------------------------------------------------------------------
# Public API
# --------------------------------------------------------------------------------------

def ensure_scene(lighting=True, viewport=None):
    """
    Make the ACTIVE scene review-ready without removing anything: Review / Review_Scale
    collections, ReviewSun / ReviewCam / ReviewTarget (created only when missing).
    lighting=True: the cs2_preview sky world + AgX + the sun at the current day/night
    state; whenever it replaces a world (or "" for none) or a view transform, that value
    is remembered in scene["cs2r_prev_world"] / ["cs2r_prev_view_transform"];
    lighting=False: restore both (and forget them); None: leave lighting alone.
    viewport="MATERIAL" / "RENDERED" switches every open 3D view to that shading with
    scene world + lights.
    """
    sc = bpy.context.scene
    created = []
    review, scale = _review_colls(created)
    _ensure_sun(review, created)
    target = bpy.data.objects.get(TARGET_NAME)
    if target is None:
        target = bpy.data.objects.new(TARGET_NAME, None)
        target.location = (0.0, 0.0, 4.0)
        review.objects.link(target)
        created.append(TARGET_NAME)
    if bpy.data.objects.get(CAM_NAME) is None:
        data = bpy.data.cameras.new(CAM_NAME)
        data.lens = 35.0
        data.clip_end = CAM_CLIP_END
        cam = bpy.data.objects.new(CAM_NAME, data)
        cam.location = (28.0, -28.0, 16.0)
        con = cam.constraints.new("TRACK_TO")
        con.target = target
        review.objects.link(cam)
        created.append(CAM_NAME)
        if sc.camera is None:
            sc.camera = cam
    lit = None
    restored = {}
    if lighting:
        world = _sky_world(created)
        if sc.world != world:
            sc["cs2r_prev_world"] = sc.world.name if sc.world else ""       # "" = no world
            sc.world = world
        if sc.view_settings.view_transform != VIEW_TRANSFORM:
            sc["cs2r_prev_view_transform"] = sc.view_settings.view_transform
            try:
                sc.view_settings.view_transform = VIEW_TRANSFORM
            except TypeError:
                pass
        lit = night(bool(sc.get("cs2r_night", False)), windows=None)
    elif lighting is False:
        if "cs2r_prev_world" in sc:
            prev = str(sc["cs2r_prev_world"])
            if prev == "":
                sc.world = None
                restored["world"] = None
            elif bpy.data.worlds.get(prev) is not None:
                sc.world = bpy.data.worlds[prev]
                restored["world"] = prev
            del sc["cs2r_prev_world"]
        if "cs2r_prev_view_transform" in sc:
            try:
                sc.view_settings.view_transform = str(sc["cs2r_prev_view_transform"])
                restored["view_transform"] = sc.view_settings.view_transform
            except TypeError:
                pass
            del sc["cs2r_prev_view_transform"]
    views = _set_viewport(viewport) if viewport else 0
    kept = sorted(n for n in KEEP_OBJECTS if bpy.data.objects.get(n)) + \
        sorted(o.name for o in bpy.data.objects if o.name.startswith(KEEP_PREFIXES))
    return {"ok": True, "scene": sc.name, "created": created, "kept_objects": kept,
            "reference_collections": {n: bpy.data.collections.get(n) is not None
                                      for n in REF_COLLS},
            "world": sc.world.name if sc.world else None,
            "view_transform": sc.view_settings.view_transform, "restored": restored,
            "night": bool(sc.get("cs2r_night", False)), "lighting": lit,
            "viewports_set": views}


def load(name, link=True, cell=None, review_dir=None):
    """
    Link (link=True) or append CS2R_<name> from <review_dir>/<name>.blend and place it on
    grid cell `cell` (default: the next free one, x = 60 k). Already loaded -> reload().
    """
    name = _clean(name)
    if _root(name) is not None:
        res = reload(name)
        res["note"] = "already loaded: reloaded instead"
        return res
    path = os.path.join(_review_dir(review_dir), f"{name}.blend")
    if not os.path.isfile(path):
        return {"ok": False, "error": f"no review pack at {path} (run the generator with --review)"}
    review, _scale = _review_colls()
    pack, err = _link_pack(path, f"{PACK_PREFIX}{name}", link)
    if err:
        return {"ok": False, "error": err}
    live = _ensure_coll(f"{PACK_PREFIX}{name}_Live", review, [])
    unhidden = _show_layer_chain(live)
    k = int(cell) if cell is not None else _next_cell()
    root = bpy.data.objects.new(f"{PACK_PREFIX}{name}_root", None)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 2.0
    root.location = (GRID_M * k, 0.0, 0.0)
    live.objects.link(root)
    root["cs2r_root"] = 1
    root["cs2r_name"] = name
    root["cs2r_cell"] = k
    root["cs2r_path"] = path
    root["cs2r_link"] = bool(link)
    roles = []
    for child in pack.children:
        role = child.get("cs2r_role") or child.name.split(".")[0]
        if role not in ROLES:
            continue
        e = bpy.data.objects.new(f"{PACK_PREFIX}{name}_{role}", None)
        e.instance_type = "COLLECTION"
        e.instance_collection = child
        e.empty_display_size = 0.5
        live.objects.link(e)
        e.parent = root
        e["cs2r_name"] = name
        e["cs2r_role"] = role
        roles.append(role)
    root["cs2r_props"] = 1
    shown = lod(name, 0)
    pprops = _pack_props(pack)
    return {"ok": bool(shown.get("ok")), "name": name, "cell": k, "x": GRID_M * k,
            "props": _prop_count(pack),
            "path": path, "linked": bool(link), "roles": sorted(roles),
            "visible": shown.get("visible"), "hidden_roles": shown.get("hidden_roles"),
            "unhidden_layers": unhidden, "error": shown.get("error"),
            "tris": {k2[5:]: v for k2, v in pprops.items() if k2.startswith("tris_")},
            "lod_switch_m": _switch_m(pack), "letter": pprops.get("letter"),
            "git_sha": pprops.get("git_sha"), "written_utc": pprops.get("written_utc")}


def reload(name):
    """Library.reload() of a linked pack (images re-read too); appended packs re-append."""
    name = _clean(name)
    root = _root(name)
    if root is None:
        return {"ok": False, "error": f"{name} is not loaded"}
    if not root.get("cs2r_link", True):
        cell, path = int(root["cs2r_cell"]), root["cs2r_path"]
        lod_state = root.get("cs2r_lod", "0")
        props_state = root.get("cs2r_props", 1)
        unload(name)
        res = load(name, link=False, cell=cell, review_dir=os.path.dirname(path))
        if res.get("ok") and lod_state != "0":
            lod(name, lod_state)
        if res.get("ok") and not props_state:
            props(name, False)
        res["reloaded"] = True
        return res
    pack = _pack_coll(name, root)
    if pack is None or pack.library is None:
        return {"ok": False, "error": f"no linked library for {name}"}
    lib = pack.library
    lib.reload()
    images = 0
    for img in bpy.data.images:
        if img.library == lib:
            try:
                img.reload()
                images += 1
            except RuntimeError:
                pass
    pack = _pack_coll(name, root)
    by_role = _pack_roles(pack)
    for role, e in _role_empties(name).items():
        if role in by_role and e.instance_collection != by_role[role]:
            e.instance_collection = by_role[role]
    _apply_props(name, root, pack)              # keeps SUB_NOPROPS when props were hidden
    lod(name, root.get("cs2r_lod", "0"))
    pprops = _pack_props(pack)
    return {"ok": True, "name": name, "reloaded": True, "library": lib.filepath,
            "props": _prop_count(pack), "props_shown": _props_shown(root, pack),
            "images_reloaded": images,
            "tris": {k: v for k, v in pprops.items() if k.startswith("tris_")},
            "lod_switch_m": _switch_m(pack), "git_sha": pprops.get("git_sha"),
            "written_utc": pprops.get("written_utc")}


def unload(name):
    """Remove the asset's live objects (instances, cameras, turntable) and its library."""
    name = _clean(name)
    root = _root(name)
    pack = _pack_coll(name, root)
    live = bpy.data.collections.get(f"{PACK_PREFIX}{name}_Live")
    objs = set(live.objects) if live is not None else set()
    objs |= {o for o in bpy.data.objects if o.get("cs2r_name") == name and o.library is None}
    removed = _remove_objects(objs)
    if live is not None:
        bpy.data.collections.remove(live)
    library_removed = False
    if pack is not None:
        if pack.library is not None:
            bpy.data.libraries.remove(pack.library)
            library_removed = True
        else:
            for c in [pack] + list(pack.children_recursive):
                for o in list(c.objects):
                    _remove_objects([o])
                bpy.data.collections.remove(c)
            for coll in (bpy.data.materials, bpy.data.images, bpy.data.meshes):
                for db in list(coll):
                    if db.users == 0 and db.name.startswith(f"{PACK_PREFIX}{name}_"):
                        coll.remove(db)
    return {"ok": root is not None or pack is not None, "name": name,
            "removed_objects": removed, "library_removed": library_removed}


def lod(name, level=0):
    """Show one level (0 / 1 / 2) with SUB, or "lineup": LOD0 / 1 / 2 side by side along
    +X with LINEUP_GAP_M between their bounding boxes (SUB beside LOD0)."""
    name = _clean(name)
    empties = _role_empties(name)
    root = _root(name)
    if not empties or root is None:
        return {"ok": False, "error": f"{name} is not loaded"}
    pack = _pack_coll(name, root)
    lo, hi = _bbox(pack)
    offsets = {r: 0.0 for r in ROLES}
    if str(level).lower() == "lineup":
        pitch = (hi.x - lo.x) + LINEUP_GAP_M
        show = set(ROLES)
        for i, r in enumerate(("LOD0", "LOD1", "LOD2")):
            offsets[r] = i * pitch
        state = "lineup"
    else:
        k = int(level)
        if not 0 <= k <= 2:
            return {"ok": False, "error": f"lod level {level!r} not in 0..2 or 'lineup'"}
        show = {f"LOD{k}", "SUB"}
        state = str(k)
    empty_roles = []
    for role, e in empties.items():
        vis = role in show
        e.hide_viewport = not vis
        e.hide_render = not vis
        e.location = (offsets[role], 0.0, 0.0)
        if e.instance_collection is not None and not e.instance_collection.all_objects:
            empty_roles.append(role)
    root["cs2r_lod"] = state
    # visible_get() is False for an empty whose collection is excluded or hidden in the
    # view layer: report it instead of claiming the role is on screen (P0a review)
    hidden = sorted(r for r in empties if r in show and not empties[r].visible_get())
    out = {"ok": not hidden, "name": name, "state": state,
           "visible": sorted(r for r in empties if r in show and r not in hidden),
           "hidden_roles": hidden,
           "offsets_m": {r: offsets[r] for r in sorted(empties)},
           "empty_roles": sorted(empty_roles)}
    if hidden:
        out["error"] = (f"{hidden} not visible: a collection above them is excluded or "
                        f"hidden in the view layer (load() re-includes Review and _Live)")
    return out


def props(name=None, on=None):
    """
    Show / hide the vanilla-prop stand-ins (DECISIONS 183): the labeled boxes cs2_review.write_pack
    puts in SUB, one per manifest sub-object row. on=None toggles; True / False sets ("on" / "off"
    and 1 / 0 work too). name=None: every loaded pack. SUB's other contents (`_Win` glass) stay -
    the SUB instance swaps between the pack's SUB and SUB_NOPROPS collections. A pack without
    rows reports count 0 and is left alone. Returns {"ok", "props": {name: {"count", "shown"}}}.
    """
    if isinstance(on, str):
        on = {"on": True, "show": True, "1": True, "off": False, "hide": False, "0": False}.get(
            on.lower())
        if on is None:
            return {"ok": False, "error": "on: True / False / 'on' / 'off' / None (toggle)"}
    if name is not None:
        names = [_clean(name)]
    else:
        names = sorted(o["cs2r_name"] for o in bpy.data.objects if o.get("cs2r_root"))
    out = {}
    for n in names:
        root = _root(n)
        if root is None:
            return {"ok": False, "error": f"{n} is not loaded"}
        pack = _pack_coll(n, root)
        count = _prop_count(pack)
        if not count:
            out[n] = {"count": 0, "shown": False, "note": "no vanilla sub-object rows in this pack"}
            continue
        want = (not bool(root.get("cs2r_props", 1))) if on is None else bool(on)
        root["cs2r_props"] = 1 if want else 0
        used = _apply_props(n, root, pack)
        if used is None:
            return {"ok": False, "error": f"{n}: the pack has no SUB / {NOPROPS_ROLE} collection "
                                          f"or no SUB instance (rebuild the pack with --review)"}
        out[n] = {"count": count, "shown": want, "instance": used}
    bpy.context.view_layer.update()
    return {"ok": True, "props": out, "total": sum(v["count"] for v in out.values())}


def tint(name, slot, rgb=None):
    """Recolor slot 1/2/3 (or "R"/"G"/"B", "steel"/"pad") of this asset's instances to
    `rgb` (3 floats, linear like the prefab VariationSet); rgb=None restores the default."""
    name = _clean(name)
    s = SLOT_NAMES.get(str(slot).upper())
    if s is None:
        return {"ok": False, "error": f"slot {slot!r}: use 1/2/3, R/G/B, steel or pad"}
    empties = _role_empties(name)
    if not empties:
        return {"ok": False, "error": f"{name} is not loaded"}
    key = SLOT_ATTR.format(s)
    val = None if rgb is None else [float(c) for c in tuple(rgb)[:3]]
    if val is not None and len(val) != 3:
        return {"ok": False, "error": "rgb needs 3 components"}
    for e in empties.values():
        if val is None:
            if key in e:
                del e[key]
        else:
            e[key] = val                  # plain 3-float array: read as Color, Alpha 1
        e.update_tag()
    bpy.context.view_layer.update()
    return {"ok": True, "name": name, "slot": s, "property": key, "rgb": val,
            "objects": sorted(e.name for e in empties.values())}


def night(on=True, windows=True):
    """Dusk (sun 4 deg / 0.4) or noon (55 deg / 4.0) on ReviewSun and the CS2R sky;
    windows=True also toggles the `_Win` warm emission on every loaded pack."""
    sc = bpy.context.scene
    review, _scale = _review_colls()
    sun = _ensure_sun(review, [])
    elev, energy = DUSK if on else NOON
    sun.data.energy = energy
    sun.data.angle = math.radians(SUN_ANGLE_DEG)
    sun.rotation_mode = "XYZ"
    sun.rotation_euler = _sun_rotation(elev)
    world = bpy.data.worlds.get(SKY_WORLD)
    if world is not None and world.node_tree is not None:
        sky = world.node_tree.nodes.get("CS2R_Sky")
        if sky is not None:
            sky.sun_elevation = math.radians(elev)
    lit = []
    if windows is not None:
        val = 1.0 if (on and windows) else 0.0
        for o in bpy.data.objects:
            if o.get("cs2r_role") in ROLES and o.library is None:
                o[WIN_EMIT_ATTR] = val
                o.update_tag()
                lit.append(o.name)
    sc["cs2r_night"] = bool(on)
    return {"ok": True, "night": bool(on), "sun_elevation_deg": elev, "sun_energy": energy,
            "sky": world is not None and world == sc.world,
            "window_emission_objects": len(lit)}


def stand_ins(name=None, tower=True, review_dir=None):
    """
    Scale stand-ins in Review_Scale: the 3 m story ruler and (when its pack exists) the
    shipped SkiLiftTowerFG8N, an instance of the headless stand-in pack
    <review_dir>/SkiLiftTowerFG8N.blend (STANDIN_CMD builds it; no import runs live).
    Created once; with `name` both move beside that asset's cell, else beside the existing
    person / 8 m mast. The existing ScalePerson_* / ScaleTower8m_* / LotGrid8m are never
    touched.
    """
    _review, scale = _review_colls()
    created, notes = [], []
    if name is not None:
        root = _root(name)
        if root is None:
            return {"ok": False, "error": f"{_clean(name)} is not loaded"}
        lo, hi = _bbox(_pack_coll(name, root))
        base = root.location
        ruler_pos = (base.x + hi.x + 3.0, base.y + lo.y, 0.0)
        tower_pos = (base.x + lo.x - 6.0, base.y + lo.y, 0.0)
    else:
        ruler_pos, tower_pos = RULER_POS, FG8N_POS
    ruler = bpy.data.objects.get(RULER_NAME)
    if ruler is None:
        ruler = _make_ruler(scale)
        created.append(RULER_NAME)
    ruler.location = ruler_pos
    fg8n = bpy.data.objects.get(FG8N_NAME)
    tower_pack = None
    if fg8n is None and tower:
        fg8n, tower_pack, err = _link_fg8n(scale, review_dir)
        if err:
            notes.append(err)
        if fg8n is not None:
            created.append(FG8N_NAME)
    if fg8n is not None and fg8n.instance_collection is None:
        notes.append(f"{FG8N_NAME} is a pre-pack mesh copy (an older stand_ins imported "
                     f"it live): delete it and call stand_ins() again to link the pack")
    if fg8n is not None:
        fg8n.location = tower_pos
        if tower_pack is None and fg8n.instance_collection is not None:
            tower_pack = next((c for c in bpy.data.collections
                               if c.get("cs2r_pack") and fg8n.instance_collection
                               in c.children[:]), None)
    height = None
    if fg8n is not None:
        if tower_pack is not None:
            lo, hi = _bbox(tower_pack)
            height = round(hi.z - lo.z, 3)
        else:
            height = round(fg8n.dimensions.z, 3)
    kept = sorted(o.name for o in bpy.data.objects
                  if o.name.startswith(KEEP_PREFIXES) or o.name == "LotGrid8m")
    return {"ok": True, "created": created, "ruler": RULER_NAME, "ruler_at": list(ruler_pos),
            "prop_standins": _prop_report(),
            "tower": fg8n.name if fg8n is not None else None,
            "tower_kind": (None if fg8n is None else
                           "pack" if fg8n.instance_collection is not None else "mesh"),
            "tower_height_m": height,
            "tower_at": list(tower_pos) if fg8n is not None else None,
            "kept": kept, "notes": notes}


def cams(name, active=None):
    """
    Cameras for one loaded asset, parented to its root (they follow the cell):
    DEFAULT_VIEWS + "back" framed on the LOD0 bounds (45 deg vertical fov, margin 1.12),
    field cameras along the three_quarter direction at the pack's predicted LOD switch
    distances and at 30 / 140 / 280 / 600 m, and a turntable (empty rotating 0 -> 360 deg
    over frames 1..241, linear, with its own camera). Re-running replaces them.
    `active`: a view name to make the scene camera.
    """
    name = _clean(name)
    root = _root(name)
    if root is None:
        return {"ok": False, "error": f"{name} is not loaded"}
    pack = _pack_coll(name, root)
    live = bpy.data.collections.get(f"{PACK_PREFIX}{name}_Live")
    _remove_objects([o for o in bpy.data.objects if o.get("cs2r_cam") == name])
    sc = bpy.context.scene
    lo, hi = _bbox(pack)
    centre = (lo + hi) / 2.0
    radius = max((hi - lo).length / 2.0, 0.5)
    aspect = (sc.render.resolution_x * sc.render.pixel_aspect_x) / \
        max(1.0, sc.render.resolution_y * sc.render.pixel_aspect_y)
    fov_y = math.radians(VERTICAL_FOV_DEG)
    fov_x = 2.0 * math.atan(math.tan(fov_y / 2.0) * aspect)
    fit = radius / math.tan(min(fov_x, fov_y) / 2.0) * FRAME_MARGIN
    made = {}

    def tag(obj, view, dist):
        obj["cs2r_cam"] = name
        obj["cs2r_view"] = view
        obj["cs2r_distance_m"] = round(dist, 2)
        made[view] = obj.name

    for view, d in DEFAULT_VIEWS + (BACK_VIEW,):
        loc = centre + mathutils.Vector(d).normalized() * fit
        tag(_new_camera(f"{PACK_PREFIX}{name}_cam_{view}", live, root, loc, centre), view, fit)
    field = {}
    fdir = mathutils.Vector(FIELD_DIR).normalized()
    for label, dist in list(_switch_m(pack).items()) + \
            [(f"{int(m)}m", m) for m in FIELD_DISTANCES_M]:
        view = f"field_{label}" if label.endswith("m") else f"field_{label}_switch"
        loc = centre + fdir * dist
        tag(_new_camera(f"{PACK_PREFIX}{name}_cam_{view}", live, root, loc, centre), view, dist)
        field[view] = round(dist, 2)

    tt = bpy.data.objects.new(f"{PACK_PREFIX}{name}_turntable", None)
    tt.empty_display_size = 1.0
    live.objects.link(tt)
    tt.parent = root
    tt.location = centre
    tt["cs2r_cam"] = name
    tt["cs2r_view"] = "turntable_pivot"
    tdir = mathutils.Vector(DEFAULT_VIEWS[0][1]).normalized()
    tcam = _new_camera(f"{PACK_PREFIX}{name}_cam_turntable", live, tt, tdir * fit,
                       (0.0, 0.0, 0.0))
    tag(tcam, "turntable", fit)
    tt.rotation_mode = "XYZ"
    tt.rotation_euler = (0.0, 0.0, 0.0)
    tt.keyframe_insert("rotation_euler", index=2, frame=TURNTABLE_START)
    tt.rotation_euler = (0.0, 0.0, 2.0 * math.pi)
    tt.keyframe_insert("rotation_euler", index=2, frame=TURNTABLE_START + TURNTABLE_FRAMES)
    tt.animation_data.action.name = f"{PACK_PREFIX}{name}_turntable"
    for fc in _fcurves(tt):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
    if active:
        cam = bpy.data.objects.get(made.get(str(active), ""))
        if cam is None:
            return {"ok": False, "error": f"no camera view {active!r}", "cameras": made}
        sc.camera = cam
    return {"ok": True, "name": name, "cameras": made, "fit_distance_m": round(fit, 2),
            "field_m": field, "turntable": {"pivot": tt.name, "camera": tcam.name,
                                            "frames": [TURNTABLE_START,
                                                       TURNTABLE_START + TURNTABLE_FRAMES]},
            "scene_camera": sc.camera.name if sc.camera else None}


LABEL_COLL = "CS2R_Labels"
PEOPLE_COLL = "CS2R_People"
MANNEQUIN_MESH = "CS2R_Mannequin180"
MANNEQUIN_H = 1.8                     # a 1.8 m person beside every asset (2026-10-17)
LABEL_SIZE_M = 2.5
LABEL_POS = (0.0, -16.0, 0.0)         # upright text on the ground in front of the cell
PEOPLE_POS = (5.0, -5.0, 0.0)         # the mannequin stands front-right of the asset


def _mannequin_mesh():
    """One shared 1.8 m mannequin mesh (cylinder body + sphere head), built with bmesh."""
    me = bpy.data.meshes.get(MANNEQUIN_MESH)
    if me is not None:
        return me
    import bmesh
    bm = bmesh.new()
    body_h = MANNEQUIN_H - 0.30
    bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.20, radius2=0.16,
                          depth=body_h, matrix=mathutils.Matrix.Translation((0, 0, body_h / 2)))
    bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=0.14,
                              matrix=mathutils.Matrix.Translation((0, 0, MANNEQUIN_H - 0.14)))
    me = bpy.data.meshes.new(MANNEQUIN_MESH)
    bm.to_mesh(me)
    bm.free()
    mat = bpy.data.materials.get("CS2R_MannequinMat")
    if mat is None:
        mat = bpy.data.materials.new("CS2R_MannequinMat")
        mat.diffuse_color = (0.9, 0.35, 0.1, 1.0)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (0.9, 0.35, 0.1, 1.0)
    me.materials.append(mat)
    return me


def labels(size=LABEL_SIZE_M):
    """One upright TEXT object CS2R_Label_<name> per loaded pack ("<cell>  <name>") in
    the CS2R_Labels collection; re-run after loading more packs (existing labels are
    refreshed in place). Reads as a cell sign from the three_quarter / front views."""
    review, _scale = _review_colls()
    coll = _ensure_coll(LABEL_COLL, review, [])
    _show_layer_chain(coll)
    made = []
    for root in [o for o in bpy.data.objects if o.get("cs2r_root")]:
        name = root["cs2r_name"]
        oname = f"{PACK_PREFIX}Label_{name}"
        obj = bpy.data.objects.get(oname)
        if obj is None:
            cu = bpy.data.curves.new(oname, "FONT")
            obj = bpy.data.objects.new(oname, cu)
            coll.objects.link(obj)
            made.append(oname)
        cu = obj.data
        cu.body = f"{int(root['cs2r_cell'])}  {name}"
        cu.size = size
        cu.align_x = "CENTER"
        cu.extrude = 0.02
        obj.location = (root.location.x + LABEL_POS[0], root.location.y + LABEL_POS[1],
                        root.location.z + LABEL_POS[2])
        obj.rotation_euler = (math.radians(90.0), 0.0, 0.0)
        obj["cs2r_label"] = name
    return {"ok": True, "labels": len(coll.objects),
            "created": made}


def people(name=None, on=True):
    """A 1.8 m mannequin CS2R_People_<name> beside every loaded pack (or  only) in the
    CS2R_People collection, sharing the CS2R_Mannequin180 mesh; on=False hides them."""
    review, _scale = _review_colls()
    coll = _ensure_coll(PEOPLE_COLL, review, [])
    _show_layer_chain(coll)
    me = _mannequin_mesh()
    roots = [o for o in bpy.data.objects if o.get("cs2r_root")
             and (name is None or o["cs2r_name"] == _clean(name))]
    made = []
    for root in roots:
        oname = f"{PACK_PREFIX}People_{root['cs2r_name']}"
        obj = bpy.data.objects.get(oname)
        if obj is None:
            obj = bpy.data.objects.new(oname, me)
            coll.objects.link(obj)
            made.append(oname)
        obj.location = (root.location.x + PEOPLE_POS[0], root.location.y + PEOPLE_POS[1],
                        root.location.z + PEOPLE_POS[2])
        obj.hide_viewport = not on
        obj.hide_render = not on
        obj["cs2r_person"] = root["cs2r_name"]
    return {"ok": True, "people": len(roots), "created": made, "height_m": MANNEQUIN_H}


def status():
    """Loaded packs: name, cell, LOD state, library path."""
    out = {}
    for o in bpy.data.objects:
        if o.get("cs2r_root"):
            pack = _pack_coll(o["cs2r_name"], o)
            out[o["cs2r_name"]] = {"cell": int(o["cs2r_cell"]), "x": o.location.x,
                                   "lod": o.get("cs2r_lod"), "path": o.get("cs2r_path"),
                                   "linked": bool(o.get("cs2r_link", True)),
                                   "props": _prop_count(pack),
                                   "props_shown": _props_shown(o, pack)}
    return {"ok": True, "loaded": dict(sorted(out.items())),
            "night": bool(bpy.context.scene.get("cs2r_night", False))}


def install():
    """Register this API as bpy.app.driver_namespace["cs2r_review"] for later MCP calls."""
    import types
    api = types.SimpleNamespace(ensure_scene=ensure_scene, load=load, reload=reload,
                                unload=unload, lod=lod, tint=tint, night=night,
                                stand_ins=stand_ins, cams=cams, status=status, props=props,
                                labels=labels, people=people)
    bpy.app.driver_namespace["cs2r_review"] = api
    return {"ok": True, "namespace": 'bpy.app.driver_namespace["cs2r_review"]',
            "functions": sorted(vars(api))}


# exec(code, globals, locals) with two dicts puts these definitions in `locals` while the
# functions resolve names through `globals`: copy them across (a no-op on a normal import
# or a single-dict exec). Definitions only - nothing above runs.
globals().update({_k: _v for _k, _v in list(locals().items()) if not _k.startswith("__")})
