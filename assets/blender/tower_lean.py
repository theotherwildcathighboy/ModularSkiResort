"""
tower_lean.py - the tower-lean round, art half: leaned towers on sloped ground (previews) and the foundation check.

The planner now caps the rope pitch at a tower (the mean of the two chords: FG 38, DQ 40, GD 40, tram 55). The mast leans
downhill to carry it, head first (the code's rule): lean = clamp(|pitch| - REL, 0, LEAN_MAX). The constants:
- DQ, GD: REL 20 / LEAN_MAX 20.
- FG: REL 12 / LEAN_MAX 25 (600 a, code 6eb3138: the FG has no head, so the mast is its head).
- trams: REL 30 / LEAN_MAX 25.

So the head (lift_head letter T), the FG halves' mean or the tram saddle's mean (tram_saddle letter D, its range unchanged)
sits at most REL off the mast below the cap (FG: 13 at the cap). The lean turns the whole tower about its foot (the
Transform origin: the node on the ground, the position does not move). So the rope anchor moves by yA sin(lean)
(tilted-towers spike section 1), and the foundation turns with the tower.

Yaw (2026-11-04, the owner on the approved sheets: the crash pad faced downhill). The scenes put uphill at +y. Every tower
is built with its uphill face at net -y: the crash pad's padded face and warning triangle (lift_tower.build_crash_pad /
cs2_asset.CRASHPAD: authoring +X -> net -y). The opening, the ladder and the deck ends are downhill. TrainFront = net -y =
game +z. So every tower is turned 180 deg about z (yaw(): x -> -x, y -> -y) into the scene: the padded face uphill (+y),
TrainFront on the uphill half. pose_fn(down, up) takes the halves BY SIDE (the downhill -y half first) and names the bones.
The ropes are built in the mesh frame (the angles negated) and turned with the tower.

Poses (Blender deg, scene frame: x lateral, y travel, z up. A uniform pose theta raises the +y end). At a lean L:
- the world rope pitch is min(L + REL, PITCH_CAP), PITCH_CAP at L = LEAN_MAX (the steepest pitch that lean meets.
  FG: 38 at 25, the mean 13 off the mast). At L = 0 it is the steepest plumb pitch, REL.
- the mean relative to the mast is pitch - L.
- the terrain is a plane through the foot rising toward +y at that pitch ("matched").
- the mast is turned by +L about x through the origin (the top moves toward -y: downhill).

The "max" column: the steepest lean with one half at the type's widest relative angle (FG_MAX_HALVES: at the 38 cap with a
25 lean, mean 13 +- the FG half-break 16 = 29 / -3. For trams: the TS-1 tower with the widest half relative to the mast).

Close frames (--close, the pre-import check of 600 a): every DQ / GD Hold / Combo key at head +-20 relative to the mast on a
mast leaned 20 the same way, the kind's widest fold (Hold: halves head -+ 20, concave. Combo: head -+ 8). Two frames 3 m,
ladder side and side-on (the grab extensions, the landing rail, the fall-arrest rail's top under the comm wedge).

Foundation check (numbers, every key, no render): the Root foot parts (lift towers: `pad` + `pier`, the block continued to z
-4. Trams: the pedestals `ped_*` + open piers `pier_*` + base plates) posed at each lean on matched ground and on a level
crest, against the plumb tower on the same ground:
  bottom_m       the highest pier bottom over the terrain (> 0: a pier's bottom shows: the foundation lifts off).
  float_cover_m  the largest float of the origin over the terrain the piers still cover (= -bottom_m).
  pier_show_m    the most pier (below the pad underside z -0.30 / the pedestal feet z 0) over the terrain at float 0.
  steel_buried_m the deepest point of the tower's steel (Root, non-foundation, z 0..2 m) under the terrain.

Usage (headless, scratch folders only, nothing exported):
  blender --background --factory-startup --python-exit-code 1 --python tower_lean.py -- --out <scratch>
      [--types FG,DQ,GD,tram] [--foundation <json>] [--preview <dir>] [--fg-reach <json>]
"""
import argparse
import json
import math
import os
import sys
import time

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402
import cs2_sheaves as S  # noqa: E402
import cs2_skin  # noqa: E402
import lift_tower as LT  # noqa: E402
import lift_head as LH  # noqa: E402
import tram_tower as TT  # noqa: E402
import tram_saddle as TS  # noqa: E402

TYPES = ("FG", "DQ", "GD", "tram")
PITCH_CAP = {"FG": 38.0, "DQ": 40.0, "GD": 40.0, "tram": TS.PITCH_CAP}    # LiftAlignMath.PitchCapDeg
LEAN_MAX = {"FG": 25.0, "DQ": 20.0, "GD": 20.0, "tram": TS.LEAN_MAX}
# the mean relative to the mast once the mast leans (head first: LiftAlignMath's lean rule)
REL = {"FG": 12.0, "DQ": LH.HEAD_RANGES["T"]["head"], "GD": LH.HEAD_RANGES["T"]["head"], "tram": TS.MEAN_REL_MAX}
LEANS = {"FG": (0.0, 10.0, 20.0, 25.0), "DQ": (0.0, 10.0, 20.0), "GD": (0.0, 10.0, 20.0), "tram": (0.0, 10.0, 20.0, 25.0)}
# deg rel mast, (downhill, uphill) half: the FG at the 38 cap, lean 25 (mean 13 +- a 16 deg half-break)
FG_MAX_HALVES = (29.0, -3.0)
FG_REACH = (30.0, 33.0, 36.0)   # deg: the FG art's G6 (joint_grid at this theta max) tried per member (report only)
FG_MEMBERS = ("FG8", "FG12", "FG16", "FG20")
FG_TWINS = tuple(k for k, (m, _kind) in LT.TWINS.items() if m in FG_MEMBERS)
KEYS = {"FG": FG_MEMBERS + FG_TWINS, "DQ": tuple(k for k in LH.HEAD_KEYS if k.startswith("DQ")),
        "GD": tuple(k for k in LH.HEAD_KEYS if k.startswith("GD")), "tram": (30, 45, 60)}
LINEUP = {"FG": ("FG8", "FG12", "FG20"), "DQ": ("DQ10", "DQ12Hold", "DQ20"), "GD": ("GD12", "GD16Combo", "GD20"),
          "tram": (30, 45, 60)}
HERO = {"FG": "FG12", "DQ": "DQ12Hold", "GD": "GD16Combo", "tram": 45}     # the lean sheet and the foot close-ups
FOUND = ("pad", "pier", "ped_", "bplate_")          # foundation parts (pier covers pier_)
PIER_TOP = {False: -0.30, True: 0.0}               # m: the pier band's top (the pad underside / the pedestal feet)
STEEL_BAND = 2.0                                    # m: the steel checked for burial (the lowest band over the origin)
TERRAIN_ALBEDO = 0.30                               # = LT.SKIN_LIGHT ground_albedo (the earlier rounds' ground)


def world_pitch(typ, lean):
    """The steepest world pitch a lean meets: REL past it, the cap at the full lean (lean = clamp(p - REL, 0, max))."""
    if lean >= LEAN_MAX[typ] - 1e-9:
        return PITCH_CAP[typ]
    return min(lean + REL[typ], PITCH_CAP[typ])


def max_pose(typ):
    """(lean, world pitch = terrain slope, (front, back) Blender deg rel mast) of the "max" column, or None (DQ / GD: the
    head's corner is the lean sheet's last column already)."""
    if typ == "FG":
        lean = LEANS["FG"][-1]
        return lean, world_pitch("FG", lean), FG_MAX_HALVES
    if typ != "tram":
        return None
    best = None
    for _lab, cf, cb in TS.planner_poses():
        m = 0.5 * (cf + cb)
        if abs(m) > PITCH_CAP["tram"] + 1e-9:
            continue
        lean = min(LEAN_MAX["tram"], max(0.0, abs(m) - REL["tram"]))
        lean_c = math.copysign(lean, m)
        pf, pb = TS.half_pitches(cf - lean_c, cb - lean_c, TS.SADDLE_MAX, TS.HALF_BREAK)
        f, b = -pf, -pb                                  # Blender sign
        if m > 0:                                        # mirror along the line: the rope rises toward +y (Blender = -code)
            f, b = -b, -f
        key = (round(max(abs(f), abs(b))), lean)        # the widest half. Among those the most lean
        if best is None or key > best[0]:
            best = (key, lean, abs(m), (round(f, 1), round(b, 1)))
    return best[1], best[2], best[3]


def rel_pitch(typ, lean):
    return world_pitch(typ, lean) - lean


# -- towers: (obj, spec, bones, vertex bone index, pivots, pose(theta), ropes(pose), stage) ------------------------------

def yaw(co):
    """(N, 3) coordinates turned 180 deg about z (x -> -x, y -> -y): the tower's mesh frame (uphill face at net -y) into
    the scene (uphill +y). A proper rotation: no winding flip. Every bone pivot lies on y = 0, so the pivots stay."""
    out = np.array(co, dtype=np.float64, copy=True)
    out[:, 0] *= -1.0
    out[:, 1] *= -1.0
    return out


class Tower:
    def __init__(self, typ, key, t, table, spec, bones, part_bone, pivots, pose_fn, ropes_fn, asset_dir, name):
        self.typ, self.key, self.t, self.table, self.spec = typ, key, t, table, spec
        self.part_bone = part_bone
        self.bones, self.pivots, self.pose_fn, self.ropes_fn = bones, pivots, pose_fn, ropes_fn
        self.asset_dir, self.name = asset_dir, name
        _names, _fb, self.vb = cs2_skin.face_bones(t, table, part_bone, list(bones))
        self.co = yaw(cs2_skin.coords(t))           # the scene's rest coordinates (yaw: the padded face uphill)
        for b, p in pivots.items():
            if abs(p[0]) > 1e-9:
                raise RuntimeError(f"{name}: pivot {b} off y = 0 ({p}): yaw() would move it")
        self.top = float(self.co[:, 2].max())
        fp = cs2_skin.face_parts(t, table)
        vpart = {}
        for p, n in zip(t.data.polygons, fp):
            for v in p.vertices:
                vpart.setdefault(v, n)
        self.vpart = np.array([vpart.get(i, "") for i in range(len(self.co))], dtype=object)


def fg_tower(key, out):
    member = LT.TWINS[key][0] if key in LT.TWINS else key
    head = LT.TWIN_HEAD["FG"]
    atlas, _arep, ref = LT.twin_atlas(member, head, keep_ref=True)
    if key == member:
        t, spec = ref, LT.skin_spec(LT.VARIANTS[member])
        table = json.loads(t.data[cs2_asset.PART_TABLE_PROP])
        pb = LT.skin_part_bones(t, table, spec)
        name = t.name
    else:
        tw = LT.twin_spec(key)
        name = cs2_asset.validate_asset_name(f"{tw['twin'].name_stem}T")
        t, table, info = LT.build_twin(tw, head, atlas, name)
        spec, pb = tw["twin"], info["skin_part_bone"]
        bpy.data.objects.remove(ref, do_unlink=True)
    asset_dir = os.path.join(out, name)
    LT.stage_textures(member, asset_dir, name)
    piv = LT.skin_pivot(spec)

    def pose(down, up=None):        # by side: the downhill (-y) half = TrainBack, the uphill (+y) = TrainFront (yaw)
        return {"TrainFront": down if up is None else up, "TrainBack": down}

    def ropes(p):
        return LT.skin_rope_runs(spec, p, run=14.0, preview=True)
    return Tower("FG", key, t, table, spec, LT.SKIN_BONES, pb, {b: piv for b in LT.SKIN_BONES[1:]}, pose, ropes,
                 asset_dir, name)


def fg_reach(tw):
    """The FG art's G6 (lift_tower.skin_sweep) with its theta max raised to each FG_REACH value: how far the halves already
    reach relative to the mast without a new letter. Report only. The module constant is restored."""
    keep, out = LT.SKIN_THETA_MAX["FG"], {}
    try:
        for tmax in FG_REACH:
            LT.SKIN_THETA_MAX["FG"] = tmax
            try:
                r = LT.skin_sweep(tw.t, tw.table, tw.spec, tw.part_bone)
                out[tmax] = dict(passed=True, min_clear_mm=round(r["min_clear"]["m"] * 1000, 1))
            except LT.TowerContractError as e:
                out[tmax] = dict(passed=False, failed=str(e)[:240])
            print(f"[lean] {tw.name} FG reach {tmax:g}: {json.dumps(out[tmax])}")
    finally:
        LT.SKIN_THETA_MAX["FG"] = keep
    return out


def headed_tower(key, out):
    member = LT.TWINS[key][0] if key in LT.TWINS else key
    rep, (t, arm, lods, tw, name, asset_dir) = LH.run_key(key, out)
    for o in list(lods) + [arm]:
        data, kind = o.data, o.type
        bpy.data.objects.remove(o, do_unlink=True)
        if data is not None and data.users == 0:
            (bpy.data.meshes if kind == "MESH" else bpy.data.armatures).remove(data)
    LT.stage_textures(member, asset_dir, name)
    spec = tw["twin"]
    table = json.loads(t.data[cs2_asset.PART_TABLE_PROP])
    pb = LH.head_part_bones(t, table, spec)
    piv = LT.skin_pivot(spec)
    za = LH.comm_anchor(tw)

    def pose(down, up=None):        # by side (yaw: TrainFront uphill)
        up = down if up is None else up
        return {"TrainFront": up, "TrainBack": down, LH.HEAD_BONE: 0.5 * (down + up)}

    def ropes(p):
        return LT.skin_rope_runs(spec, p, run=14.0, preview=True) + LH.comm_runs(spec, p, za, run=16.0)
    return Tower(spec.family, key, t, table, spec, LH.HEAD_BONES, pb, {b: piv for b in LH.HEAD_BONES[1:]}, pose,
                 ropes, asset_dir, name)


def tram_tower(H, out):
    args = TS.c_args(H)
    S.set_wheels(args.wheels)
    S.set_wheel_dress(True)
    TT.set_skinned(True)
    TT.clear_scene()
    tc, _tab, _spec, _parts, _rc = TS.build(H, TS.SOURCE, args)
    uv, _arep = TS.shipped_uvs(tc, H)
    me = tc.data
    bpy.data.objects.remove(tc, do_unlink=True)
    bpy.data.meshes.remove(me)
    t, table, spec, _parts, _c = TS.build(H, TS.LETTER, args)
    name = f"SkiTramTower{H}{TS.LETTER}"
    t.name = t.data.name = name
    TS.set_uvs(t, uv)
    TS.use(TS.SOURCE)
    asset_dir = os.path.join(out, name)
    TS.stage_textures(H, asset_dir, name)
    pb = TT.skin_part_bones(t, table)

    def pose(down, up=None):        # by side (yaw: TrainFront uphill)
        return TT.skin_pose(down if up is None else up, down)

    def ropes(p):
        return TT.skin_rope_runs(spec, p, run=40.0, preview=True)
    return Tower("tram", H, t, table, spec, TT.SKIN_BONES, pb, TT.skin_pivots(spec), pose, ropes, asset_dir, name)


def make_tower(typ, key, out):
    if typ == "FG":
        return fg_tower(key, out)
    if typ == "tram":
        return tram_tower(key, out)
    return headed_tower(key, out)


# -- the foundation check ------------------------------------------------------------------------------------------------

def leaned(co, lean):
    return cs2_skin.rotate_x(co, lean, (0.0, 0.0))


def foundation(tw, lean, slope):
    """The four numbers (module docstring) for `tw` leaned `lean` on a plane of `slope` deg through the origin."""
    found = np.array([n.startswith(FOUND) for n in tw.vpart])
    root = tw.vb == 0
    lc = tw.co
    pier = found & (lc[:, 2] <= PIER_TOP[tw.typ == "tram"] + 1e-3)     # the pier band (its top ring welds to the pad)
    zbot = float(lc[found, 2].min())
    bottom = found & (lc[:, 2] <= zbot + 1e-3)
    steel = root & ~found & (lc[:, 2] >= 0.0) & (lc[:, 2] <= STEEL_BAND)   # above the foot (tram ladders run below it)
    w = leaned(lc, lean)
    g = w[:, 1] * math.tan(math.radians(slope))
    d = w[:, 2] - g
    return {"bottom_m": round(float(d[bottom].max()), 3),
            "float_cover_m": round(float(-d[bottom].max()), 3),
            "pier_show_m": round(max(0.0, float(d[pier].max())) if pier.any() else 0.0, 3),
            "steel_buried_m": round(max(0.0, float(-d[steel].min())) if steel.any() else 0.0, 3),
            "pier_depth_m": round(-zbot, 3)}


def foundation_table(tw):
    """Per lean: matched ground (the pose's world pitch) and a level crest. Plus the plumb tower on each slope."""
    rows = []
    for lean in LEANS[tw.typ]:
        wp = world_pitch(tw.typ, lean)
        for ground, slope in (("matched", wp), ("crest", 0.0)):
            rows.append(dict(lean=lean, pitch=wp, ground=ground, slope=slope, **foundation(tw, lean, slope),
                             plumb=foundation(tw, 0.0, slope)))
    return rows


# -- previews --------------------------------------------------------------------------------------------------------------

def _mat(name, rgb, metal=0.0, rough=0.95):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    return mat


def terrain(slope, half=160.0, name="LeanTerrain"):
    """A plane through the origin rising toward +y at `slope` deg (lateral x +-half, travel y +-half)."""
    k = math.tan(math.radians(slope))
    n = 8
    ys = np.linspace(-half, half, n + 1)
    verts = [(x, y, y * k) for y in ys for x in (-half, half)]
    faces = [(2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2) for i in range(n)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.materials.append(_mat("LeanTerrain", (TERRAIN_ALBEDO, TERRAIN_ALBEDO, TERRAIN_ALBEDO)))
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def posed_object(tw, pose, lean):
    """A copy of the tower mesh (UVs, materials) at `pose` relative to the mast, leaned about the foot."""
    pc = cs2_skin.posed(tw.co, tw.vb, list(tw.bones), pose, tw.pivots)
    pc = leaned(pc, lean)
    me = tw.t.data.copy()
    me.vertices.foreach_set("co", pc.astype(np.float32).ravel())
    me.update()
    ob = bpy.data.objects.new(f"{tw.name}Lean", me)
    bpy.context.collection.objects.link(ob)
    return ob


def rope_objects(tw, pose, lean):
    mat = _mat("LeanRope", (0.03, 0.03, 0.03), metal=0.7, rough=0.45)
    out = []
    # the scene pose in the mesh frame: the 180 deg yaw turns a rotation about +x into one about -x
    for v, f in tw.ropes_fn({b: -a for b, a in pose.items()}):
        me = bpy.data.meshes.new(f"{tw.name}Rope")
        me.from_pydata([tuple(p) for p in leaned(yaw(np.array(v)), lean)], [], f)
        me.materials.append(mat)
        ob = bpy.data.objects.new(me.name, me)
        bpy.context.collection.objects.link(ob)
        out.append(ob)
    return out


def _drop(objs):
    for o in objs:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)


def frame_views(tw, lean, tag, kinds, slope=0.0):
    """Side (along the line, from +x), 3/4 (from the downhill side) and the foot close-ups (downhill / uphill 3/4)."""
    h = tw.top
    mid = leaned(np.array([[0.0, 0.0, h * 0.5]]), lean)[0]
    big = tw.typ == "tram"
    foot_d = 48.0 if big else 7.5
    k = math.tan(math.radians(slope))                    # the uphill camera looks down the slope: stay above it
    foot_t = (0.0, 0.0, 2.0 if big else 0.45)
    lib = {
        "side": dict(name=f"side_{tag}", dir=(1.0, 0.0, 0.05), target=tuple(mid), lens=50.0, dist=(h + 6.0) * 2.55,
                     ground=False),
        "q34": dict(name=f"q34_{tag}", dir=(1.0, -0.9, 0.30), target=tuple(mid), lens=50.0, dist=(h + 6.0) * 2.55,
                    ground=False),
        "footdown": dict(name=f"footdown_{tag}", dir=(0.75, -1.0, 0.45), target=foot_t, lens=50.0, dist=foot_d,
                         ground=False),
        "footup": dict(name=f"footup_{tag}", dir=(0.75, 1.0, max(0.45, k + 0.5)), target=foot_t, lens=50.0, dist=foot_d,
                       ground=False),
    }
    return [lib[k] for k in kinds]


def render(tw, lean, slope, raw, tag, kinds, resolution=(900, 1100), halves=None):
    pose = tw.pose_fn(*halves) if halves else tw.pose_fn(rel_pitch(tw.typ, lean))
    ob = posed_object(tw, pose, lean)
    extras = rope_objects(tw, pose, lean) + [terrain(slope)]
    try:
        return LT.render_tower_views(ob, tw.asset_dir, tw.name, raw, frame_views(tw, lean, tag, kinds, slope),
                                     resolution=resolution, extras=extras, light=LT.SKIN_LIGHT)
    finally:
        _drop(extras + [ob])


def tower_frames(tw, raw, hero):
    """Every lineup tower: side at each lean (matched ground). The hero also: 3/4 at each lean, the foot close-ups at
    the steepest pose on matched ground and on a level crest."""
    fr = {}
    for lean in LEANS[tw.typ]:
        kinds = ["side", "q34"] if hero else ["side"]
        fr[lean] = render(tw, lean, world_pitch(tw.typ, lean), raw, f"l{int(lean)}", kinds)
    mp = max_pose(tw.typ) if hero else None
    if mp:
        fr["max"] = render(tw, mp[0], mp[1], raw, "lmax", ["side", "q34"], halves=mp[2])
        print(f"[lean] {tw.name} max pose: lean {mp[0]:g}, pitch {mp[1]:g}, halves {mp[2]} rel mast")
    if hero:
        lm = LEANS[tw.typ][-1]
        fr["foot_matched"] = render(tw, lm, world_pitch(tw.typ, lm), raw, f"footm{int(lm)}", ["footdown", "footup"],
                                    resolution=(1100, 900))
        fr["foot_crest"] = render(tw, lm, 0.0, raw, f"footc{int(lm)}", ["footdown", "footup"], resolution=(1100, 900))
    return fr


CLOSE_HEAD = 20.0                                   # deg rel mast: the head at its T limit, both signs
CLOSE_FOLD = {"Hold": 20.0, "Combo": 8.0}           # deg: each half off the head (concave: the kind's widest fold)
# about 3 m from the ladder top, level with the grabs: the ladder side ("ladder") and side-on from the lateral ("side")
CLOSE_VIEWS = {"ladder": dict(dir=(0.45, 1.0, 0.02), lens=28.0, dist=3.0),
               "side": dict(dir=(1.0, 0.35, 0.0), lens=28.0, dist=3.0)}


def close_poses(tw):
    """[(tag, lean, pose)]: the head at -+CLOSE_HEAD on a mast leaned LEAN_MAX the same way, the kind's widest fold."""
    kind = LT.TWINS[tw.key][1]
    d = CLOSE_FOLD[kind]
    hm = LH.HALF_REL_MAX or 40.0
    out = []
    for s in (-1.0, 1.0):
        h = s * CLOSE_HEAD
        f, b = max(-hm, min(hm, h - d)), max(-hm, min(hm, h + d))
        out.append((f"h{'m' if s < 0 else 'p'}{int(CLOSE_HEAD)}", s * LEAN_MAX[tw.typ], tw.pose_fn(f, b)))
    return out


def close_frames(tw, raw):
    """One ladder-side frame per close pose: the target is the ladder rails' top (the grab extensions), leaned with the mast.
    CLOSE_VIEWS, the travel component on the ladder's side."""
    rails = np.array([n.startswith("ladder_rail_") for n in tw.vpart])
    if not rails.any():
        raise RuntimeError(f"{tw.name}: no ladder_rail_ parts")
    rc = tw.co[rails]
    top = rc[rc[:, 2] >= rc[:, 2].max() - 0.05]
    tgt0 = np.array([[float(top[:, 0].mean()), float(top[:, 1].mean()), float(top[:, 2].max()) - 0.25]])
    side = math.copysign(1.0, float(rc[:, 1].mean()))
    paths = []
    for tag, lean, pose in close_poses(tw):
        tgt = leaned(tgt0, lean)[0]
        ob = posed_object(tw, pose, lean)
        extras = rope_objects(tw, pose, lean)
        views = [dict(name=f"close_{tag}_{vn}", dir=(v["dir"][0], side * v["dir"][1], v["dir"][2]), target=tuple(tgt),
                      lens=v["lens"], dist=v["dist"], ground=False) for vn, v in CLOSE_VIEWS.items()]
        try:
            paths += LT.render_tower_views(ob, tw.asset_dir, tw.name, raw, views, resolution=(1000, 1000),
                                           extras=extras, light=LT.SKIN_LIGHT)
        finally:
            _drop(extras + [ob])
        print(f"[lean] {tw.name} close {tag}: lean {lean:g}, pose {pose}")
    return paths


def sheets(typ, frames, prev):
    out = []
    pick = lambda paths, view: next((p for p in paths if f"_view_{view}_" in os.path.basename(p)), None)  # noqa: E731
    grid = [[pick(frames[k][lean], "side") for lean in LEANS[typ]] for k in LINEUP[typ]]
    out.append(LH.montage(grid, os.path.join(prev, f"{typ}_lineup.png")))
    h = frames[HERO[typ]]
    cols = list(LEANS[typ]) + (["max"] if "max" in h else [])
    out.append(LH.montage([[pick(h[c], "side") for c in cols], [pick(h[c], "q34") for c in cols]],
                          os.path.join(prev, f"{typ}_lean.png")))
    out.append(LH.montage([h["foot_matched"], h["foot_crest"]], os.path.join(prev, f"{typ}_foot.png")))
    return out


# -- main ------------------------------------------------------------------------------------------------------------------

def parse(argv):
    ap = argparse.ArgumentParser(prog="tower_lean.py")
    ap.add_argument("--out", required=True, help="scratch folder (never assets/import)")
    ap.add_argument("--types", default=",".join(TYPES))
    ap.add_argument("--foundation", default=None, help="write the foundation table (every key) as JSON here")
    ap.add_argument("--preview", default=None, help="preview folder (raw frames under <preview>/raw)")
    ap.add_argument("--fg-reach", default=None, help="write the FG reach probe (FG_REACH per lineup member) as JSON here")
    ap.add_argument("--close", default=None, help="close-frame folder: the Hold / Combo keys at head +-20 on a 20 lean, "
                    "<close>/close_DQ.png / close_GD.png (raw frames under <close>/raw)")
    return ap.parse_args(argv)


def close_main(args, out):
    sheets_out = []
    for fam in ("DQ", "GD"):
        rows = []
        for key in [k for k in LH.HEAD_TWINS if k.startswith(fam)]:
            S.set_wheels("v2")
            S.set_wheel_dress(True)
            tw = make_tower(fam, key, out)
            rows.append(close_frames(tw, os.path.join(args.close, "raw")))
        sheets_out.append(LH.montage(rows, os.path.join(args.close, f"close_{fam}.png")))
    return sheets_out


def main_cli():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parse(argv)
    out = os.path.abspath(args.out)
    if os.path.normcase(out).startswith(os.path.normcase(os.path.join(LT._REPO, "assets", "import"))):
        raise SystemExit("--out is in the shipping tree assets/import: this round exports nothing")
    types = args.types.split(",")
    S.set_wheels("v2")
    S.set_wheel_dress(True)
    LT.set_skinned(True)
    LT.TWIN_ATLAS.update(LH.HEAD_ATLAS)
    LH.use_letter("T")
    t0 = time.perf_counter()
    if args.close:
        for s in close_main(args, out):
            print(f"[lean] sheet {s}")
        print(f"PASS tower_lean --close: {time.perf_counter() - t0:.0f} s")
        return
    table, sheets_out, reach = {}, [], {}
    for typ in types:
        keys = KEYS[typ] if args.foundation else (LINEUP[typ] if args.fg_reach and typ == "FG" else ())
        keys = tuple(dict.fromkeys(tuple(keys) + (LINEUP[typ] if args.preview else ())))
        frames = {}
        for key in keys:
            S.set_wheels("v2")
            S.set_wheel_dress(True)
            tw = make_tower(typ, key, out)
            table[f"{typ}:{key}"] = rows = foundation_table(tw)
            worst = max(rows, key=lambda r: r["bottom_m"])
            print(f"[lean] {tw.name}: pier depth {rows[0]['pier_depth_m']} m; worst pier bottom {worst['bottom_m']:+.3f} m "
                  f"(lean {worst['lean']:g}, {worst['ground']} {worst['slope']:g}); matched at the max lean: "
                  + json.dumps({k: v for k, v in rows[-2].items() if k != "plumb"}))
            if args.fg_reach and typ == "FG" and key in LINEUP["FG"]:
                reach[key] = fg_reach(tw)
                with open(args.fg_reach, "w", encoding="utf-8", newline="\n") as f:
                    json.dump(reach, f, indent=1)
            if args.preview and key in LINEUP[typ]:
                frames[key] = tower_frames(tw, os.path.join(args.preview, "raw"), key == HERO[typ])
            if args.foundation:
                with open(args.foundation, "w", encoding="utf-8", newline="\n") as f:
                    json.dump(table, f, indent=1)
        if args.preview:
            sheets_out += sheets(typ, frames, args.preview)
    for s in sheets_out:
        print(f"[lean] sheet {s}")
    print(f"PASS tower_lean: {len(table)} towers, {len(sheets_out)} sheets, {time.perf_counter() - t0:.0f} s")


if __name__ == "__main__":
    try:
        main_cli()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
