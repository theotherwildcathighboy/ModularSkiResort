"""
test_detail_school - detail pass 1 contracts of the ski school (design/detail-pass-1.md
section 6.2, school row, D1-D10, with design/detail-pass-1-skeptic.md M1-M10 applied; build
report docs/research/art/assets-2026-10/build/detail-pass-1-school.md), UPDATED for the
fine-detail pass step 2 (build/fine-detail-step2-engine.md): the school is the reference host
(recipes/school.py RECIPE = fine_detail.FineRecipe, fine_detail=True, sign_set=SIGN_SET).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_detail_school.py [-- --quick]

One finish at the shipping size (2048, 48 AO samples, LODs) is re-used by every check; D5
adds a second identical finish in the same process, D4 a weather=None finish of the same
build (--quick skips those two).

D1  detail counts: every design 3.1 item's parts by exact name that the HOST still models (the
    12 routed label faces live in the sign set; racks / cones / entry lanterns are rows), S13
    belt key `rubber`, stripes `safety_yellow`, sign-lamp lenses `lamp`, arms galv; no `cone_`,
    `lamp_main`, `skirack_` part; the B.1 fine geometry (maze posts 22 tris each, flag masts 58,
    lesson poles 34); sub_objects = the recipe's 30 + the sign-set identity row: vanilla GUIDs of
    the known families, m_ParentMesh 0 on wall props, the kit rows by their staged CIDs.
D2  emissive: covered lamp texels all lit, within +-25 % of the area prediction; no stray lit
    texel; <= 0.5 % of the map; every face of an emissive part >= 2.5 texels across.
D3  labels: the host's remaining labels (tower field, flag cloths) 1 part / 1 face / 1 island,
    rects disjoint, flag skier >= 0.70 m; the SIGN SET's sheet metrics: SKI SCHOOL T1 (cap >= 0.40
    m, stroke >= 4 px), bib numbers / door plates stroke >= 4 px (vector), lesson shapes >= 0.40 m,
    meeting-board trail shapes >= 0.33 m.
D4  weathering: manifest weather.vanilla.ok, intensity = school.WEATHER_INTENSITY, free slots,
    snow 0, G5 / G6 true; full mode: ControlMask byte-identical to a weather=None finish and every
    host label rect of BaseColor identical.
D5  determinism (full mode): two finishes in one process give identical host PNGs, LOD0 / LOD1 /
    LOD2 / _Win hashes and identical sign-set PNGs.
D6  budgets: LOD0 <= 4,400 (skeptic M10), LOD1 <= 2,800, LOD2 <= 500; w = 1 median >= 40 px/m;
    tex 2048; batches 9.
D7  base mode: engine base OK on LOD0 / 1 / 2; no new part below GROUND_LIFT.
D8  lot / geometry: lot margins >= 0.25 m; _civic assert_lot_margin / check_coplanar /
    check_inside on the parts.
D9  LOD: new dressing absent from LOD1 except the `sign_` frames / post signs, every new part
    absent from LOD2; the exported preview LOD2 XOR <= 8 % when the frames exist.
D10 sub-object placement: a wall prop's pivot within 0.05 m of one of our faces facing its +Z, its
    box >= 0.05 m clear of every other face of ours; a grade prop (vanilla or kit) on grade and
    >= 0.30 m clear of every mesh part; the new rows' boxes overlap no other row's box.
D11 fine-detail H1 / H9: every new row at its removed part (cones' plan center +-0.01 m, entry
    lamps on the wall plane at the old lens (y, z) +-0.01 m, each kit rack's frame = the removed
    rack's frame vertex for vertex +-1 mm, 2 cm lower: the kit foot is on y 0); host lot bounds =
    the pre-pass school's +-0.005 m (H9, design FS1).
UPDATED for REVISION 2 (2026-10-12, Richard's twentieth look; build/school-rev2.md): no door
numbering, the red markings, the 13.5 deg monopitch, lot 4 x 5, both carpets in the back at 12 %
(the right one a bubble gallery). D1 / D3 / D8 / D10 / D11 follow the new parts and rows (the
sub_objects are now 32 + the sign set; H9 pins the revision-2 lot bounds), and:
R2  revision-2 contracts, each with a negative: (a) NO door numbering (no `emblem_door` part, the
    four frozen door rects of the sheet carry no text, no digit-only label line) and the red
    markings where the photos put them (placards on the two white door leaves, 4 mm proud, inside
    the leaf; NO SLEDDING over the svc door head and under its wall light; the alarm bell
    above-left of the tower door); (b) the roof: every `roof_uf` face rises toward -X at
    ROOF_PITCH_DEG +-0.01 deg (dz/dy 0), the UF walls meet the slab underside, back wall height;
    (c) the carpets: belt grade = GRADE +-1e-6 from 0.20 m at the loading end, closed skirts from
    grade (z 0) under the whole belt on both sides, the deck flush with the belt end and its far
    edge on the lot margin, both loading ends behind the back eave, the bubble on the right (+Y)
    and longer, the gallery spanning the belt.
Exit 1 on the first failure; one PASS line otherwise.
"""

import glob
import hashlib
import json
import math
import os
import re
import shutil
import sys
import tempfile
import time
import traceback
from dataclasses import replace

import bpy
import numpy as np
from mathutils import Vector, Quaternion
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, HERE)
import bldg_recipes as R   # noqa: E402
import cs2_asset           # noqa: E402
import cs2_bake            # noqa: E402
import cs2_bldg as K       # noqa: E402
import cs2_compose         # noqa: E402
import cs2_signset as SS   # noqa: E402
import cs2_subobj          # noqa: E402
import cs2_uv              # noqa: E402
import fine_detail as FD   # noqa: E402
import lodge               # noqa: E402

QUICK = "--quick" in sys.argv
CHECKS = []
REC = R.get_recipe("school")
SCH = REC.builder.__globals__          # the recipe module (loaded by bldg_recipes)
C = SCH["load_civic"]()
SPEC = REC.sign_set
ROUTED = set(SPEC.face_names())
LEGACY = FD.effective(REC, fine=False)
HOST_LABELS = SS.host_recipe(REC, SPEC).labels

LOD0_CAP, LOD1_CAP, LOD2_CAP = 4400, 2800, 500       # D6 (skeptic M10: 4,000 -> ~4,400)
W1_MIN = 40.0
# H9 pin. Revision 2 (lot 4 x 5, building at OX +12, the yard behind it): x from the carpet decks'
# far edge (-19.75, the lot margin) to the flag (18.861), y from the maze (-12.528) to the +Y rake
# (15.40). Was the pre-pass school's {"x": (-7.6, 6.984), "y": (-15.3, 15.15)} (lot 4 x 2).
PRE_PASS_LOT = {"x": (-19.75, 18.861), "y": (-12.528, 15.4)}
# vanilla render bounds, game local (x, y up, z out / front): envelope of every variant mesh
# of the placeholder family (00_BaseGame dump, *Mesh.Prefab m_Bounds, 2026-10-11 / -12)
VANILLA_BOUNDS = {
    "FenceResidentialPieceLowRandom01": ((-0.026, 0.0, -2.0), (0.03, 1.028, 2.0)),
    "FenceResidentialPieceLowShortRandom01": ((-0.026, 0.0, -1.009), (0.03, 1.028, 0.991)),
    "SnowmanRandom01": ((-0.930, -0.005, -0.438), (0.938, 1.372, 0.692)),
    "GardenBenchRandom01": ((-1.059, 0.0, -0.496), (1.059, 1.054, 0.496)),
    "TrashbinRandom01": ((-0.355, 0.0, -0.364), (0.355, 1.036, 0.364)),
    "WallLightShortRangeRandom01": ((-0.142, -0.058, 0.0), (0.142, 0.058, 0.115)),
    "WallLightRandom01": ((-0.197, -0.105, 0.0), (0.197, 0.508, 0.550)),
    "ACVentWallRandom01": ((-0.187, -0.196, 0.0), (0.187, 0.284, 0.247)),
    "SafetyConeRandom01": ((-0.202, 0.0, -0.202), (0.202, 1.082, 0.202)),
}
KNOWN_GUIDS = {SCH[k] for k in ("FENCE_4M", "FENCE_2M", "SNOWMAN", "BENCH", "BIN", "WALL_SHORT",
                                "WALL_LIGHT", "AC_VENT", "SAFETY_CONE")}
WALL_PROPS = ("WallLightShortRangeRandom01", "WallLightRandom01", "ACVentWallRandom01")


def ok(msg):
    CHECKS.append(msg)
    print(f"ok {msg}")


def sha1_file(p, n=12):
    with open(p, "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()[:n]


def png(path):
    img = bpy.data.images.load(path, check_existing=False)
    a = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], -1)
    bpy.data.images.remove(img)
    return a


def kit_bounds(prefab):
    (lo, hi), src = cs2_subobj.lookup_bounds(prefab, None)
    assert src == "cache", f"{prefab}: kit bounds from {src}"
    return tuple(lo), tuple(hi)


def row_bounds(o):
    if o.get("kind") == "mod":
        return kit_bounds(o["prefab"])
    return VANILLA_BOUNDS[o["prefab"]]


# ======================================================================================
# The new parts (design 3.1 minus what the fine-detail pass moved out), by exact name
# ======================================================================================

def new_part_names():
    names = []
    for nm, *_r in SCH["BOARDS"]:
        names += [f"sign_{nm}_frame"]
    for k in range(len(SCH["LESSON_Y"])):
        names += [f"sign_lesson{k}_post", f"sign_lesson{k}_frame"]
    names += ["maze_carpet", "maze_carpettape"]
    names += ["flag_school", "emblem_schoolflagA_face", "emblem_schoolflagB_face"]
    names += [f"stripe_step{nm}" for nm, *_r in SCH["DOORS"]]
    # revision 2: the carpets' nosing stripes (loading edge + landing edge) and the alarm bell; the
    # door plates are gone (R2a)
    names += [f"stripe_{e}{c[0]}" for c in SCH["CARPETS"] for e in ("ramp", "top")]
    names += ["bell_alarm"]
    names += ["lamp_belfryfloor", "lamp_belfryrail"]
    for k in range(len(SCH["SIGN_LAMPS"])):
        names += [f"lamp_sign{k}", f"lamp_sign{k}arm"]
    return names


def rev2_part_names():
    """Revision 2's structural parts (roof, braces, carpets, gallery) - present at LOD1 except the
    gallery frames (LOD1_EXTRA galleryrib_)."""
    names = ["roof_uf", "fascia_uf", "soffit_uf", "glulam_back", "glulam_front",
             "gallery_side", "gallery_roof", "gallery_in", "galleryrib_b"]
    for c in SCH["CARPETS"]:
        t = c[0]
        names += [f"carpet_skirt{t}", f"carpet_skirt{t}deck", f"carpet_belt{t}", f"carpetrail_{t}",
                  f"carpetrail_ramp{t}", f"carpetdeck_{t}"]
    return names


MARK_FACES = tuple(f"sign_{tag}_face" for tag, _d in SCH["MARK_DOORS"]) + ("sign_nosled_face",)


def is_new(name):
    return name in NEW


NEW = set(new_part_names())


# ======================================================================================
# Parts (authoring frame, before the join): D1 materials, D7 feet, D8, D10 geometry
# ======================================================================================

def build_parts(recipe=REC):
    lodge.clear_scene()
    parts = lodge.build_parts(recipe, {"tower": True, "roof_windows": "win"})
    bpy.context.view_layer.update()
    keys = {o.name: k for o, k in parts}
    tris, zmin = [], {}
    verts = {}
    for o, _k in parts:
        M = o.matrix_world
        me = o.data
        V = [M @ v.co for v in me.vertices]
        verts[o.name] = np.array([tuple(v) for v in V], np.float64)
        zmin[o.name] = min(v.z for v in V) if V else 0.0
        for p in me.polygons:
            vs = list(p.vertices)
            for i in range(1, len(vs) - 1):
                tris.append(((tuple(V[vs[0]]), tuple(V[vs[i]]), tuple(V[vs[i + 1]])), o.name))
    T = np.array([t for t, _n in tris], np.float64)
    names = np.array([n for _t, n in tris])
    ntri = {}
    for n in names:
        ntri[str(n)] = ntri.get(str(n), 0) + 1
    return parts, keys, zmin, T, names, verts, ntri


def host_tris(T, tnames):
    """The triangles that stay in the host (the routed label faces leave for the sign set)."""
    keep = np.array([n not in ROUTED for n in tnames])
    return T[keep], tnames[keep]


def d1_parts(keys, man, ntri):
    table = set(keys)
    missing = sorted(n for n in list(NEW) + rev2_part_names() + list(MARK_FACES) if n not in table)
    assert not missing, f"D1: missing parts {missing}"
    gone = sorted(n for n in table if n.startswith(("cone_", "lamp_main", "skirack_")))
    assert not gone, f"D1: modelled parts the pass replaced are still built: {gone}"
    for c in SCH["CARPETS"]:
        assert keys[f"carpet_belt{c[0]}"] == "rubber", f"D1: S13 belt key {keys[f'carpet_belt{c[0]}']}"
    for n in table:
        if n.startswith("stripe_"):
            assert keys[n] == "safety_yellow", f"D1: {n} key {keys[n]}"
    for k in range(len(SCH["SIGN_LAMPS"])):
        assert keys[f"lamp_sign{k}"] == "lamp" and keys[f"lamp_sign{k}arm"] == "galv", \
            f"D1: sign lamp {k} keys {keys[f'lamp_sign{k}']} / {keys[f'lamp_sign{k}arm']}"
    for n in ("lamp_belfryfloor", "lamp_belfryrail"):
        assert keys[n] == "lamp", f"D1: {n} key {keys[n]}"
    # B.1 fine geometry (fine=True on the kit builders)
    n_posts = ntri["maze_carpet"] // 22
    assert ntri["maze_carpet"] == 22 * n_posts and n_posts == 9, f"D1: maze posts {ntri['maze_carpet']} tris"
    for f in ["flag_school"] + [f"flag_corral{k}" for k in range(len(SCH["PENNANTS"]))]:
        assert ntri[f] == 58, f"D1: {f} {ntri[f]} tris (12-seg + cap + finial = 58)"
    for k in range(len(SCH["LESSON_Y"])):
        assert ntri[f"sign_lesson{k}_post"] == 34, f"D1: lesson pole {k} {ntri[f'sign_lesson{k}_post']} tris"
    sub = man.get("sub_objects") or {}
    objs = sub.get("objects") or []
    assert len(objs) == len(REC.sub_objects) + 1 == 27, f"D1: sub_objects {len(objs)} (want 26 + sign set; T87 dropped the 6 picket-fence rows)"
    assert sub.get("target_prefab") == "SkiSchool", sub.get("target_prefab")
    van = [o for o in objs if o.get("kind") != "mod"]
    mod = [o for o in objs if o.get("kind") == "mod"]
    bad = [o for o in van if o["m_Object"] not in KNOWN_GUIDS]
    assert not bad, f"D1: unknown sub-object GUIDs {bad[:3]}"
    for o in van:
        want = 0 if o["prefab"] in WALL_PROPS else -1
        assert o["m_ParentMesh"] == want, f"D1: {o['prefab']} m_ParentMesh {o['m_ParentMesh']}"
        assert o["prefab"] in VANILLA_BOUNDS, o["prefab"]
    kits = [o for o in mod if o.get("role") != "signset"]
    want_kits = list(SCH["KIT_RACKS"]) + ["SkiDressEStop"] * len(SCH["CARPETS"])   # rev 2: e-stops
    assert [o["prefab"] for o in kits] == want_kits, f"D1: kit rows {[o['prefab'] for o in kits]}"
    for o in kits:
        assert o["m_Object"] == FD.kit_cid(o["prefab"], o.get("colour_set")) \
            == FD.staged_kit_cid(o["prefab"], o.get("colour_set")), f"D1: kit CID {o}"
        assert o["m_ParentMesh"] == -1 and o["ref"] == "CID", f"D1: kit row {o}"
    sets = [o for o in mod if o.get("role") == "signset"]
    assert len(sets) == 1 and sets[0]["prefab"] == SPEC.prop, f"D1: sign-set rows {sets}"
    ok(f"D1 detail counts: {len(NEW)} host parts by name, no cone_ / lamp_main / skirack_, fine maze "
       f"9 x 22 / flags 58 / lesson poles 34 tris; sub_objects {len(objs)} = {len(van)} vanilla + "
       f"{len(kits)} kit + 1 sign set (GUIDs / CIDs known, m_ParentMesh 0 on "
       f"{sum(o['prefab'] in WALL_PROPS for o in van)} wall props)")


def d7_feet(keys, zmin, res):
    for lvl in (0, 1, 2):
        assert res["base"][lvl]["verdict"] == "OK", f"D7: LOD{lvl} base {res['base'][lvl]}"
    low = sorted((n, round(zmin[n], 4)) for n in keys if is_new(n) and
                 zmin[n] < K.GROUND_LIFT - 1e-6)
    assert not low, f"D7: new parts below GROUND_LIFT {low[:6]}"
    assert not any(is_new(n) and n.startswith(REC.ground_prefixes) for n in keys), \
        "D7: a new part is a ground part"
    ok(f"D7 base mode: engine base OK on LOD0 / 1 / 2, every new part's foot >= "
       f"{K.GROUND_LIFT} (lowest {min(zmin[n] for n in keys if is_new(n)):.3f} m)")


def d8_geometry(parts):
    C.assert_lot_margin(parts, SCH["HALF_X"], SCH["HALF_Y"], 0.25, "school")
    C.check_coplanar(parts, exempt=SCH["KIT_INTERNAL"])
    # revision 2: the massing in the LOT frame (the building at OX), the brace feet exempt (buried
    # 0.12 m in the back wall)
    vols = SCH["massing_volumes"]()
    assert abs(vols[0][0][0] - (SCH["GF"][0] + SCH["OX"])) < 1e-9, vols
    C.check_inside(parts, vols, exempt=SCH["INSIDE_EXEMPT"])
    return "lot margin 0.25 (parts), no coplanar z-fight, no face inside the massing"


def d8_lot(res, geo):
    lot = res["lot"]
    assert lot["margin_x"] >= 0.25 - 1e-6 and lot["margin_y"] >= 0.25 - 1e-6, f"D8: lot {lot}"
    ok(f"D8 lot / geometry: shipped margins x {lot['margin_x']} / y {lot['margin_y']} m; {geo} "
       f"(with the new parts)")


# ---- D10: sub-object placement -----------------------------------------------------------

def _game_to_auth(v):
    return Vector((v[2], v[0], v[1]))


def _prop_box(o):
    """(center, axes 3x3 rows = local x / y / z in authoring, lo, hi local) of a prop."""
    lo, hi = row_bounds(o)
    x, y, z, w = o["rotation"]
    q = Quaternion((w, x, y, z))
    axes = np.array([list(_game_to_auth(q @ Vector(e))) for e in
                     ((1, 0, 0), (0, 1, 0), (0, 0, 1))], np.float64)
    P = np.array(list(_game_to_auth(o["position"])), np.float64)
    return P, axes, np.array(lo, np.float64), np.array(hi, np.float64)


def _tri_box_overlap(T, c, h):
    """Bool per triangle (N, 3, 3) (box-local coords) overlapping the AABB centered c, half h
    (Akenine-Moeller SAT: 3 box axes, the triangle normal, 9 edge cross products)."""
    v = T - c
    e = [v[:, 1] - v[:, 0], v[:, 2] - v[:, 1], v[:, 0] - v[:, 2]]
    sep = np.zeros(len(T), bool)
    for a in range(3):
        sep |= (v[:, :, a].min(1) > h[a]) | (v[:, :, a].max(1) < -h[a])
    n = np.cross(e[0], e[1])
    r = np.abs(n) @ h
    d = np.einsum("ij,ij->i", n, v[:, 0])
    sep |= np.abs(d) > r + 1e-12
    I = np.eye(3)
    for ei in e:
        for a in range(3):
            ax = np.cross(ei, I[a][None, :].repeat(len(T), 0))
            p = np.einsum("nkj,nj->nk", v, ax)
            rr = np.abs(ax) @ h
            sep |= (p.min(1) > rr + 1e-12) | (p.max(1) < -rr - 1e-12)
    return ~sep


def _aabb_auth(o):
    P, ax, lo, hi = _prop_box(o)
    C8 = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
    W = P + C8 @ ax
    return W.min(0), W.max(0)


def d10_props(T, tnames, man):
    objs = [o for o in man["sub_objects"]["objects"] if o.get("role") != "signset"]
    flat = T.reshape(-1, 3)
    verts = [tuple(p) for p in flat]
    bvh = BVHTree.FromPolygons(verts, [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(len(T))])
    nrm = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0])
    nl = np.linalg.norm(nrm, axis=1)
    nrm = nrm / np.maximum(nl, 1e-12)[:, None]
    cen = T.mean(axis=1)
    worst = {}
    for o in objs:
        P, ax, lo, hi = _prop_box(o)
        wall = o["prefab"] in WALL_PROPS
        n = ax[2]
        keep = np.ones(len(T), bool)
        if wall:
            assert abs(n[2]) < 1e-6 and max(abs(n[0]), abs(n[1])) > 0.999, \
                f"D10: {o['prefab']} at {o['position']}: +Z {n} is not a horizontal axis"
            hit = bvh.ray_cast(Vector(P + 0.05 * n), Vector(-n), 0.10)
            assert hit[0] is not None and abs(hit[3] - 0.05) <= 0.05 + 1e-6 and \
                Vector(hit[1]).dot(Vector(n)) > 0.95, \
                f"D10: {o['prefab']} at {o['position']}: no wall face facing {n} within 0.05 m ({hit})"
            keep = ~((nrm @ n > 0.99) & (np.abs((cen - P) @ n) < 0.01))
            clear = 0.05
            blo = np.array((lo[0] - clear, lo[1] - clear, 0.001))
            bhi = np.array((hi[0] + clear, hi[1] + clear, hi[2] + clear))
        else:
            assert abs(o["position"][1]) < 1e-6, f"D10: grade prop {o['prefab']} y {o['position'][1]}"
            clear = 0.30
            blo = np.array((lo[0] - clear, 0.001, lo[2] - clear))
            bhi = np.array((hi[0] + clear, hi[1] + clear, hi[2] + clear))
        Tl = np.einsum("nkj,aj->nka", T[keep] - P, ax)
        hit = _tri_box_overlap(Tl, 0.5 * (blo + bhi), 0.5 * (bhi - blo))
        names = sorted(set(tnames[keep][hit]))
        assert not names, (f"D10: {o['prefab']} at game {o['position']} is closer than {clear} m "
                           f"to {names[:6]}")
        g = clear
        while g < 2.0:
            b0 = np.array((lo[0] - g, lo[1] - g if wall else 0.001, 0.001 if wall else lo[2] - g))
            b1 = np.array((hi[0] + g, hi[1] + g, hi[2] + g))
            if _tri_box_overlap(Tl, 0.5 * (b0 + b1), 0.5 * (b1 - b0)).any():
                break
            g += 0.01
        worst.setdefault(o["prefab"], []).append(round(g, 2))
    # the new rows (cones, entry lamps, kit racks, revision 2's two e-stops) overlap no other row
    new = [o for o in objs if o["prefab"] in ("SafetyConeRandom01", "SkiDressEStop") + tuple(SCH["KIT_RACKS"])
           or (o["prefab"] == "WallLightShortRangeRandom01" and abs(o["position"][1] - SCH["ENTRY_LAMP_Z"]) < 1e-6)]
    assert len(new) == 6 + 2 + 2 + 2, f"D10: new rows {len(new)}"
    boxes = [(_aabb_auth(o), o) for o in objs]
    for (alo, ahi), a in [(b, o) for b, o in boxes if o in new]:
        for (blo2, bhi2), b in boxes:
            if b is a:
                continue
            inter = np.minimum(ahi, bhi2) - np.maximum(alo, blo2)
            assert not (inter > 1e-6).all(), f"D10: {a['prefab']} {a['position']} overlaps {b['prefab']} {b['position']}"
    ok(f"D10 sub-objects: {sum(o['prefab'] in WALL_PROPS for o in objs)} wall props on a face facing "
       f"their +Z, >= 0.05 m clear; {sum(o['prefab'] not in WALL_PROPS for o in objs)} grade props "
       f"(vanilla + kit) on grade, >= 0.30 m clear; the {len(new)} new rows overlap no row (first "
       f"contact at growth m: { {k: min(v) for k, v in sorted(worst.items())} })")


def d11_fine(man, verts_fine, legacy):
    """H1 (rows at the removed parts) and H9 (lot bounds), design G.2 / skeptic FS1."""
    objs = man["sub_objects"]["objects"]
    _lp, _lk, _lz, _lT, _ln, lverts, _lnt = legacy
    # cones: plan center of the removed cone_k = the row's (gx, gz) -> authoring (gz, gx)
    cones = [o for o in objs if o["prefab"] == "SafetyConeRandom01"]
    for k, o in enumerate(cones):
        V = lverts[f"cone_{k}"]
        cx, cy = 0.5 * (V[:, 0].min() + V[:, 0].max()), 0.5 * (V[:, 1].min() + V[:, 1].max())
        d = math.hypot(o["position"][2] - cx, o["position"][0] - cy)
        assert d <= 0.01 and o["m_ParentMesh"] == -1, f"D11 H1: cone row {k} {o['position']} vs ({cx:.3f}, {cy:.3f})"
    # entry lamps: the old lens center (y, z) on the wall plane x = TW[1]
    lamps = [o for o in objs if o["prefab"] == "WallLightShortRangeRandom01"
             and abs(o["position"][1] - SCH["ENTRY_LAMP_Z"]) < 1e-6]
    for (tag, _y), o in zip(SCH["ENTRY_LAMPS"], lamps):
        V = lverts[f"lamp_{tag}"]
        cy, cz = 0.5 * (V[:, 1].min() + V[:, 1].max()), 0.5 * (V[:, 2].min() + V[:, 2].max())
        wx = SCH["TW"][1] + SCH["OX"]                 # the tower front in the lot frame (rev 2)
        assert abs(o["position"][0] - cy) <= 0.01 and abs(o["position"][1] - cz) <= 0.01 and \
            abs(o["position"][2] - wx) < 1e-6 and o["rotation"] == [0.0, 0.0, 0.0, 1.0], \
            f"D11 H1: lamp row {tag} {o['position']} vs lens ({cy:.3f}, {cz:.3f}) on x {wx}"
    # kit racks: the kit's own frame (cs2_bldg.ski_rack_frame slots 8 along X at the origin,
    # dropped GROUND_LIFT, prefab frame (x, z, -y)) placed by the row = the removed rack's frame
    # (authoring -> game (y, z, x)), 2 cm lower (the kit foot is on y 0, the old on GROUND_LIFT)
    kit = K.ski_rack_frame("d11kit", 0.0, 0.0, slots=8, along="X")[0][0]
    KV = np.array([tuple(kit.matrix_world @ v.co) for v in kit.data.vertices], np.float64)
    m = kit.data
    bpy.data.objects.remove(kit, do_unlink=True)
    bpy.data.meshes.remove(m)
    KP = np.stack([KV[:, 0], KV[:, 2] - K.GROUND_LIFT, -KV[:, 1]], axis=1)      # prefab frame
    racks = [o for o in objs if o.get("kind") == "mod" and o["prefab"] in SCH["KIT_RACKS"]]
    assert len(racks) == len(SCH["RACKS"]), f"D11: kit rack rows {len(racks)}"
    for (nm, x, y, _g), o in zip(SCH["RACKS"], racks):
        x_, y_, z_, w_ = o["rotation"]
        q = Quaternion((w_, x_, y_, z_))
        R3 = np.array(q.to_matrix(), np.float64)
        G = KP @ R3.T + np.array(o["position"], np.float64)
        old = lverts[f"skirack_{nm}"]
        OG = np.stack([old[:, 1], old[:, 2] - K.GROUND_LIFT, old[:, 0]], axis=1)
        dmax = max(float(np.min(np.linalg.norm(G - p, axis=1))) for p in OG)
        assert dmax <= 0.001, f"D11 H1: kit row {o['prefab']} frame {dmax * 1000:.2f} mm from skirack_{nm}"
        # negative: a 90-degree yaw would not reproduce the frame
        Gw = KP @ np.array(Quaternion((0.7071068, 0.0, 0.7071068, 0.0)).to_matrix(), np.float64).T + \
            np.array(o["position"], np.float64)
        assert max(float(np.min(np.linalg.norm(Gw - p, axis=1))) for p in OG) > 0.05, "D11 negative: yaw 90 matched"
    lot = man["lot"]
    for ax in ("x", "y"):
        d = max(abs(a - b) for a, b in zip(lot[ax], PRE_PASS_LOT[ax]))
        assert d <= 0.005, f"D11 H9: lot {ax} {lot[ax]} vs pre-pass {PRE_PASS_LOT[ax]} ({d:.4f} m)"
    ok(f"D11 H1 / H9: {len(cones)} cone rows at the removed cones' plan centres, {len(lamps)} entry-lamp "
       f"rows on the wall at the old lens (y, z), {len(racks)} kit racks = the removed frames +-1 mm "
       f"(identity yaw; a 90-degree yaw misses), lot x {lot['x']} y {lot['y']} within 5 mm of the "
       f"revision-2 pin")


# ---- R2: revision-2 contracts (each with a negative) -----------------------------------------

def _placard_problems(V, leaf, cz_want):
    """[] when a placard quad V (4, 3) lies MARK_PROUD in front of the +X door leaf `leaf` (its
    vertex array), inside it (0.05 m margin) with its center MARK_CZ above the leaf bottom."""
    bad = []
    fx = leaf[:, 0].max()
    if np.abs(V[:, 0] - (fx + SCH["MARK_PROUD"])).max() > 1e-4:
        bad.append(f"x {V[:, 0].round(4).tolist()} vs leaf face {fx:.4f} + {SCH['MARK_PROUD']}")
    if V[:, 1].min() < leaf[:, 1].min() + 0.05 or V[:, 1].max() > leaf[:, 1].max() - 0.05:
        bad.append(f"y {V[:, 1].min():.3f}..{V[:, 1].max():.3f} outside leaf {leaf[:, 1].min():.3f}.."
                   f"{leaf[:, 1].max():.3f}")
    cz = 0.5 * (V[:, 2].min() + V[:, 2].max()) - leaf[:, 2].min()
    if abs(cz - cz_want) > 0.01 or V[:, 2].max() > leaf[:, 2].max() - 0.05:
        bad.append(f"centre {cz:.3f} m above the leaf bottom (want {cz_want})")
    return bad


def _grade_of(V):
    """(grade, z at the loading end) of a belt quad (loading end = max x)."""
    xl, xt = V[:, 0].max(), V[:, 0].min()
    zl = V[np.isclose(V[:, 0], xl), 2].mean()
    zt = V[np.isclose(V[:, 0], xt), 2].mean()
    return (zt - zl) / (xl - xt), zl


def r2_revision(parts, keys, verts):
    """Runs on the live parts (before the finish clears the scene); rows from REC.sub_objects."""
    ox = SCH["OX"]
    # (a) no door numbering, the red markings where the photos put them
    assert not [n for n in keys if n.startswith("emblem_door")], "R2a: a door plate part is still built"
    for face, kind, kw in SCH["DISPLAY_LABELS"]:
        for d, dkw in kw.get("draws", ()):
            if d == "text":
                assert not any(str(s).strip().isdigit() for s in dkw.get("lines", ())), \
                    f"R2a: {face} still prints a number {dkw['lines']}"
    for n in (1, 2, 3, 4):
        assert not any(d == "text" for d, _kw in SS.kind(f"school.door{n}").draws), \
            f"R2a: sheet kind school.door{n} still draws text"
    doors = {d[0]: d for d in SCH["DOORS"]}
    for tag, dn in SCH["MARK_DOORS"]:
        bad = _placard_problems(verts[f"sign_{tag}_face"], verts[f"door_{dn}_leaf"], SCH["MARK_CZ"])
        assert not bad, f"R2a: placard {tag} on door {dn}: {bad}"
    neg = verts["sign_markL_face"].copy()
    neg[:, 0] += 0.01
    assert _placard_problems(neg, verts["door_wingl_leaf"], SCH["MARK_CZ"]), "R2a negative: 1 cm off the leaf passed"
    ns = verts["sign_nosled_face"]
    head = verts["door_svc_head"]
    _n, _s, xw, yd, wd, _h = doors["svc"]
    lamp = [o for o in REC.sub_objects if o.get("prefab") == "WallLightShortRangeRandom01"
            and abs(o["position"][0] - yd) < 1e-6 and abs(o["position"][2] - (xw + ox)) < 1e-6]
    assert len(lamp) == 1, f"R2a: the svc wall light row {lamp}"
    lamp_lo = lamp[0]["position"][1] + VANILLA_BOUNDS["WallLightShortRangeRandom01"][0][1]
    assert np.abs(ns[:, 0] - (xw + ox - 0.006)).max() < 1e-4, f"R2a: NO SLEDDING x {ns[:, 0]}"
    assert ns[:, 2].min() >= head[:, 2].max() + 0.05 - 1e-6 and ns[:, 2].max() <= lamp_lo - 0.05 + 1e-6, \
        f"R2a: NO SLEDDING z {ns[:, 2].min():.3f}..{ns[:, 2].max():.3f} vs head {head[:, 2].max():.3f} / lamp {lamp_lo:.3f}"
    assert abs(0.5 * (ns[:, 1].min() + ns[:, 1].max()) - yd) < 1e-4, "R2a: NO SLEDDING not centred on the svc door"
    bell, leaf_m, head_m = verts["bell_alarm"], verts["door_main_leaf"], verts["door_main_head"]
    assert bell[:, 1].max() < leaf_m[:, 1].min() - 0.05 and bell[:, 2].min() > head_m[:, 2].max(), \
        f"R2a: the alarm bell is not above-left of the tower door ({bell.min(0)}..{bell.max(0)})"
    # (b) the roof: every roof_uf face rises toward -X at ROOF_PITCH_DEG
    assert keys["roof_uf"] == "slate" and keys["roof_gf"] == "slate", (keys["roof_uf"], keys["roof_gf"])   # T69 shingle roofs
    assert keys["roof_tower"] == "metal_roof" and keys["lanterncap_roof"] == "metal_roof"
    obj ={o.name: o for o, _k in parts}["roof_uf"]
    M = obj.matrix_world.to_3x3()
    angs = []
    for p in obj.data.polygons:
        n = (M @ p.normal).normalized()
        assert abs(n.y) < 1e-6 and n.x > 0 and n.z > 0, f"R2b: roof_uf normal {tuple(n)} (must face +X / up)"
        angs.append(math.degrees(math.atan2(n.x, n.z)))
    want = SCH["ROOF_PITCH_DEG"]
    assert max(abs(a - want) for a in angs) < 0.01, f"R2b: roof pitch {angs} vs {want}"
    assert abs(math.degrees(math.atan(0.25)) - want) > 0.01, "R2b negative: a 14.04 deg roof would pass"
    wu = verts["wall_uf"]
    back = wu[np.isclose(wu[:, 0], SCH["UF"][0] + ox)]
    zb = SCH["roof_bot"](SCH["UF"][0])
    assert abs(back[:, 2].max() - zb) < 1e-4, f"R2b: back wall top {back[:, 2].max():.3f} vs roof underside {zb:.3f}"
    front_eave = SCH["roof_top"](SCH["UF"][1] + SCH["OV_FRONT"])
    back_eave = SCH["roof_top"](SCH["UF"][0] - SCH["OV_BACK"])
    # (c) the carpets
    eave_x = SCH["UF"][0] - SCH["OV_BACK"] + ox
    half_x = SCH["HALF_X"]
    rep = []
    for c in SCH["CARPETS"]:
        tag, yc, xl, L, cov, wd, bubble = c
        B = verts[f"carpet_belt{tag}"]
        g, z0 = _grade_of(B)
        assert abs(g - SCH["GRADE"]) < 1e-6 and abs(z0 - SCH["BELT_Z0"]) < 1e-6, f"R2c: carpet {tag} grade {g} z0 {z0}"
        tilted = B.copy()
        tilted[:, 2] += 0.03 * (tilted[:, 0].max() - tilted[:, 0])
        assert abs(_grade_of(tilted)[0] - SCH["GRADE"]) > 1e-3, "R2c negative: a 15 % belt passed"
        xt = B[:, 0].min()
        S = verts[f"carpet_skirt{tag}"]
        wb = SCH["BELT_W"] / 2 + cov
        for ys in (yc - wb, yc + wb):
            foot = S[np.isclose(S[:, 1], ys) & (np.abs(S[:, 2]) < 1e-9)]
            assert len(foot) and foot[:, 0].min() <= xt + 1e-6 and foot[:, 0].max() >= xl - 1e-6, \
                f"R2c: carpet {tag} skirt at y {ys} does not reach grade under the whole belt"
        D = verts[f"carpetdeck_{tag}"]
        zt = B[np.isclose(B[:, 0], xt), 2].mean()
        assert np.abs(D[:, 2] - zt).max() < 1e-6, f"R2c: carpet {tag} deck {D[:, 2]} not flush with the belt end {zt}"
        assert abs(D[:, 0].min() + (half_x - 0.25)) < 1e-6, f"R2c: carpet {tag} deck far edge {D[:, 0].min()}"
        assert xl < eave_x - 2.0, f"R2c: carpet {tag} loading end {xl} not behind the back eave {eave_x}"
        if bubble:
            G = verts["gallery_roof"]
            assert abs(G[:, 0].max() - xl) < 1e-6 and abs(G[:, 0].min() - xt) < 1e-6, "R2c: gallery span"
        rep.append(f"{tag}: {L} m at {g * 100:.1f} % rise {zt - z0:.2f} m, deck {zt:.2f} m")
    ca, cb = SCH["CARPETS"]
    assert cb[6] and cb[1] > 0 > ca[1] and cb[3] > ca[3], "R2c: the bubble must be the right (+Y), longer carpet"
    assert tuple(REC.lot) == (4, 5), REC.lot
    ok(f"R2 revision 2: no door numbering (no emblem_door, no digit lines, door rects text-free); "
       f"placards on wingl / main leaves {SCH['MARK_PROUD'] * 1000:.0f} mm proud, NO SLEDDING between the "
       f"svc head and its wall light, bell above-left of the tower door; roof {len(angs)} faces at "
       f"{want} deg rising to the back (front eave tip {front_eave:.2f} m, back wall {zb:.2f} m, back eave tip "
       f"{back_eave:.2f} m); carpets {rep}; bubble right + longer; lot {REC.lot} (negatives caught)")


# ======================================================================================
# The finish (shipping size) and the checks on it
# ======================================================================================

def finish(recipe, out):
    args = lodge.parse_args(["--recipe", "school", "--out", os.path.join(out, "A")])
    res = lodge.run(recipe, args)
    d = res["asset_dir"]
    pngs = {os.path.basename(p)[len(res["name"]) + 1:-4]: p
            for p in glob.glob(os.path.join(d, "*.png"))}
    man = json.load(open(os.path.join(d, f"{res['name']}.manifest.json"), encoding="utf-8"))
    return res, pngs, man


def d2_emissive(res, pngs, keys, obj):
    A = res["tex"]
    e = png(pngs["Emissive"])
    lit_map = e[..., :3].max(axis=-1) > 0.0
    lit = int(lit_map.sum())
    assert lit <= 0.005 * A * A, f"D2: {lit} lit texels > 0.5 % of {A}^2"
    me = obj.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    lamps = sorted(n for n, k in keys.items() if k == "lamp")
    ids = np.array([i + 1 for i, n in enumerate(table) if n in lamps], np.int32)
    part_map, _ppm = cs2_compose.rasterise_face_fields(me, A)
    cov_lamp = np.isin(part_map, ids)
    cov_other = (part_map > 0) & ~cov_lamp
    n_cov = int(cov_lamp.sum())
    unlit = int((cov_lamp & ~lit_map).sum())
    stray = int((cov_other & lit_map).sum())
    assert unlit <= 0.01 * n_cov, f"D2: {unlit} of {n_cov} covered lamp texels unlit"
    assert stray == 0, f"D2: {stray} lit texels on covered non-lamp faces"
    w = cs2_uv.face_density_weights(me)
    ar = np.empty(len(me.polygons), np.float32)
    me.polygons.foreach_get("area", ar)
    pv = np.zeros(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", pv)
    sel = np.isin(pv, ids)
    ppm1 = res["density"]["median_w1"]
    pred = float((ar[sel] * (w[sel] * ppm1) ** 2).sum())
    assert 0.75 * pred <= n_cov <= 1.25 * pred, f"D2: covered lamp texels {n_cov} vs area prediction {pred:.0f}"
    uv = cs2_compose.read_uvs(me, cs2_uv.UNIQUE_LAYER)
    small = []
    for f in np.nonzero(sel)[0]:
        p = me.polygons[int(f)]
        q = uv[p.loop_start:p.loop_start + p.loop_total] * A
        area = 0.5 * abs(sum(q[i, 0] * q[(i + 1) % len(q), 1] - q[(i + 1) % len(q), 0] * q[i, 1]
                             for i in range(len(q))))
        edge = max(float(np.linalg.norm(q[(i + 1) % len(q)] - q[i])) for i in range(len(q)))
        if area / max(edge, 1e-9) < 2.5:
            small.append((table[pv[f] - 1], int(f), round(area / max(edge, 1e-9), 2)))
    assert not small, f"D2: lamp faces under 2.5 texels across {small[:6]}"
    ok(f"D2 emissive: {lit} lit texels ({100.0 * lit / (A * A):.3f} % of {A}^2, gutter dilation "
       f"included), {n_cov} covered lamp texels all lit (area prediction {pred:.0f}), 0 stray, "
       f"{len(lamps)} lamp parts, every face >= 2.5 texels across")
    return {"lit": lit, "covered": n_cov, "pred": round(pred)}


def d3_labels(res, man, obj):
    A = res["tex"]
    me = obj.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    pv = np.zeros(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", pv)
    field = SCH["FIELD_FACE"]
    rects = {}
    for face, _p, _kw in HOST_LABELS:
        rects[face] = cs2_compose.uv_bbox_px(me, table, face, A)
        assert rects[face] is not None, f"D3: {face} has no faces"
        if face == field:
            continue
        parts = [n for n in table if n.startswith(face)]
        fs = [i for i in range(len(pv)) if table[pv[i] - 1] == face]
        isl = cs2_uv.uv_islands(me, me.uv_layers[cs2_uv.UNIQUE_LAYER], faces=fs)
        assert len(parts) == 1 and len(fs) == 1 and len(isl) == 1, \
            f"D3: {face}: {len(parts)} parts / {len(fs)} faces / {len(isl)} islands"
    keys = sorted(rects)
    for i, a in enumerate(keys):
        for b in keys[i + 1:]:
            ra, rb = rects[a], rects[b]
            if ra[0] < rb[1] and rb[0] < ra[1] and ra[2] < rb[3] and rb[2] < ra[3]:
                raise AssertionError(f"D3: label rects overlap {a} {ra} / {b} {rb}")
    assert not ROUTED & set(table), "D3: a routed face is still in the host"
    lab = man["labels"]
    for s in "AB":
        g = lab[f"emblem_schoolflag{s}_face"]["draws"][0]
        assert g["side_m"] >= 0.70, f"D3: flag {s} {g}"
    # the sign set's sheet metrics (vector font, 300 px/m)
    sdir = res["sign_set"]["asset_dir"]
    sdir = sdir if os.path.isabs(sdir) else os.path.join(REPO, sdir)
    sm = json.load(open(os.path.join(sdir, f"{SPEC.mesh}.manifest.json"), encoding="utf-8"))
    met = sm["sheet"]["metrics"]
    t = met["school.name"]["draws"][0]
    assert t["tier"] == "T1" and t["cap_m"] >= 0.40 and t["stroke_px"] >= 4.0, f"D3: SKI SCHOOL {t}"
    bib = met["school.bibs"]["draws"][0]
    assert bib["stroke_px"] >= 4.0, f"D3: bib numbers {bib}"
    # revision 2: the red markings' legends (vector, SS3); the retired door rects draw no text (R2a)
    marks = {}
    for key in ("school.markstaff", "school.marknosled"):
        d = [x for x in met[key]["draws"] if x["kind"] == "text"][0]
        assert d["stroke_px"] >= 4.0, f"D3: red marking {key} {d}"
        marks[key] = d["stroke_px"]
    for n in (1, 2, 3, 4):
        assert not any(x["kind"] == "text" for x in met[f"school.door{n}"].get("draws", [])), \
            f"D3: door rect {n} still draws text {met[f'school.door{n}']}"
    for k in range(len(SCH["LESSON_Y"])):
        it = met[f"school.lesson{k}"]["draws"][0]["items"]
        assert min(s for _k, s in it) >= 0.40, f"D3: lesson sign {k} shapes {it}"
    it = met["school.meet"]["draws"][0]["items"]
    assert min(s for _k, s in it[2:]) >= 0.33 and it[0][1] >= 0.90, f"D3: meeting board {it}"
    ok(f"D3 labels: host {len(rects) - 1} display faces 1 part / 1 face / 1 island, {len(rects)} rects "
       f"disjoint, no routed face; sign set: SKI SCHOOL {t['tier']} cap {t['cap_m']:.3f} m stroke "
       f"{t['stroke_px']:.1f} px @ 300 px/m (vector); bibs {bib['stroke_px']:.1f} px; red markings "
       f"{marks} px (no digits on the door rects); lesson shapes >= 0.40 m")
    return rects


def d4_weather(man):
    w = man["weather"]
    v = w["vanilla"]
    assert v["ok"] is True and not v["failed"], f"D4: vanilla budget {v}"
    assert abs(w["intensity"] - SCH["WEATHER_INTENSITY"]) < 1e-9, w["intensity"]
    assert w["slot_wear"] == "free" and w["snow"] == 0.0 and w["coverage"]["snow"] == 0.0, \
        {k: w[k] for k in ("slot_wear", "snow")}
    g = w["guards"]
    assert g["G5_ctl_identity"] and g["G6_protect_identical"] and g["G1_alpha_kept"], g
    ok(f"D4 weathering: intensity {w['intensity']} free slots, snow 0, vanilla ok (VL1 "
       f"{v['VL1_mean_rel_dL']} VL2 {v['VL2_share_rel_dL_gt_0.15']} VL4 {v['VL4_max']}), "
       f"G1 / G5 / G6 true, {w['protect_texels']} protected label texels")


def d6_budgets(res, man):
    t = res["tris"]
    med = res["density"]["median_w1"]
    assert t[0] <= LOD0_CAP and t[1] <= LOD1_CAP and t[2] <= LOD2_CAP, f"D6: tris {t}"
    assert med >= W1_MIN, f"D6: w=1 median {med} < {W1_MIN} px/m"
    assert res["tex"] == 2048 and man["batches_per_mesh_group"] == 9, \
        (res["tex"], man["batches_per_mesh_group"])
    ok(f"D6 budgets: LOD0 / 1 / 2 {t[0]} / {t[1]} / {t[2]} (<= {LOD0_CAP} / {LOD1_CAP} / "
       f"{LOD2_CAP}), w=1 median {med} px/m (>= {W1_MIN}), tex 2048, batches 9")


def d9_lods(res, obj):
    me = obj.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    (t1, t2), _plan = lodge.plan_lod_targets(obj, table, REC)
    d1, d2 = lodge.delete_lists(REC, table)
    lods, _infos = K.lod_chain(obj, "D9chk", (t1, t2), (d1, d2), REC.ground_prefixes)
    try:
        kept = []
        for lod in lods:
            pv = np.zeros(len(lod.data.polygons), np.int32)
            lod.data.attributes["part"].data.foreach_get("value", pv)
            kept.append({table[i - 1] for i in set(pv.tolist())})
        assert [cs2_asset.tri_count(lod) for lod in lods] == [res["tris"][1], res["tris"][2]], \
            "D9: the re-run LOD chain differs from the export"
        l1 = sorted(n for n in kept[0] if is_new(n) and not n.startswith("sign_"))
        l2 = sorted(n for n in kept[1] if is_new(n))
        assert not l1, f"D9: new dressing at LOD1 {l1}"
        assert not l2, f"D9: new parts at LOD2 {l2}"
        s1 = sorted(n for n in kept[0] if is_new(n))
    finally:
        for lod in lods:
            m = lod.data
            bpy.data.objects.remove(lod, do_unlink=True)
            bpy.data.meshes.remove(m)
    xor = []
    for p in glob.glob(os.path.join(REPO, "assets", "preview", "buildings",
                                    f"{res['name']}_lod2_*m_xor*pct.png")):
        m = re.search(r"_xor([0-9.]+)pct\.png$", p)
        xor.append(float(m.group(1)))
    assert all(x <= 8.0 for x in xor), f"D9: preview LOD2 XOR {xor} > 8 %"
    ok(f"D9 LOD: new parts at LOD1 only the sign frames / post signs ({len(s1)} parts), none at "
       f"LOD2; preview LOD2 XOR {xor or 'not exported yet'}")


def _sign_pngs(res):
    d = res["sign_set"]["asset_dir"]
    d = d if os.path.isabs(d) else os.path.join(REPO, d)
    return {m: sha1_file(os.path.join(d, f"{SPEC.mesh}_{m}.png")) for m in SS.MAPS}


def d4_d5_extra(res, pngs, man, rects, tmp):
    """Full mode: D5 second identical finish, D4 weather=None ControlMask + label rects."""
    sp1 = _sign_pngs(res)
    res2, pngs2, man2 = finish(REC, os.path.join(tmp, "det"))
    s1 = {k: sha1_file(v) for k, v in sorted(pngs.items())}
    s2 = {k: sha1_file(v) for k, v in sorted(pngs2.items())}
    assert s1 == s2, f"D5: PNGs differ {s1} vs {s2}"
    hk = ("LOD0", "LOD1", "LOD2", "Win", "LOD0_prebake")
    h1 = {k: res["hashes"].get(k) for k in hk}
    h2 = {k: res2["hashes"].get(k) for k in hk}
    assert h1 == h2, f"D5: hashes differ {h1} vs {h2}"
    sp2 = _sign_pngs(res2)
    assert sp1 == sp2, f"D5: sign-set PNGs differ {sp1} vs {sp2}"
    ok(f"D5 determinism: two finishes in one process, PNGs {s1}, hashes {h1}, sign-set PNGs {sp1} identical")
    res0, pngs0, man0 = finish(replace(REC, weather=None), os.path.join(tmp, "none"))
    assert "weather" not in man0
    assert sha1_file(pngs0["ControlMask"]) == s1["ControlMask"], "D4: weathering changed ControlMask"
    assert sha1_file(pngs0["BaseColor"]) != s1["BaseColor"], "D4: weathering left BaseColor unchanged"
    b0, b1 = png(pngs0["BaseColor"]), png(pngs["BaseColor"])
    for face, r in sorted(rects.items()):
        assert np.array_equal(b0[r[0]:r[1], r[2]:r[3]], b1[r[0]:r[1], r[2]:r[3]]), \
            f"D4: label rect {face} {r} changed under the weathering"
    ok(f"D4 weather vs None: ControlMask byte-identical ({s1['ControlMask']}), BaseColor "
       f"changed, {len(rects)} host label rects identical")


def main():
    t0 = time.perf_counter()
    legacy = build_parts(LEGACY)
    parts, keys, zmin, T, tnames, verts, ntri = build_parts(REC)
    geo = d8_geometry(parts)
    r2_revision(parts, keys, verts)
    Th, thn = host_tris(T, tnames)
    tmp = tempfile.mkdtemp(prefix="detail_school_")
    try:
        res, pngs, man = finish(REC, os.path.join(tmp, "main"))
        name = res["name"]
        obj = bpy.data.objects.get(name + "Src") or bpy.data.objects[name]
        print(f"  blockout hashes {res['hashes']['LOD0_prebake']} / Win {res['hashes']['Win']}; "
              f"LOD0 {res['hashes']['LOD0']} LOD1 {res['hashes'].get('LOD1')} "
              f"LOD2 {res['hashes'].get('LOD2')}")
        d1_parts(keys, man, ntri)
        lit = d2_emissive(res, pngs, keys, obj)
        rects = d3_labels(res, man, obj)
        d4_weather(man)
        d6_budgets(res, man)
        d7_feet(keys, zmin, res)
        d8_lot(res, geo)
        d9_lods(res, obj)
        d10_props(Th, thn, man)
        d11_fine(man, verts, legacy)
        if not QUICK:
            d4_d5_extra(res, pngs, man, rects, tmp)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print(f"PASS test_detail_school: {len(CHECKS)} check groups (D1-D11, R2"
          f"{'' if not QUICK else ', quick: no D5 / D4-None'}), LOD0 {res['tris'][0]}, "
          f"w=1 {res['density']['median_w1']} px/m, {lit['lit']} lit texels ({lit['covered']} covered) "
          f"({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_detail_school")
        sys.exit(1)
