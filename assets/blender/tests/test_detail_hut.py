"""
test_detail_hut - the L2 hut detail-pass-1 contracts D1-D10 (design/detail-pass-1.md 6.2,
hut row, with design/detail-pass-1-skeptic.md M1-M10; kit build/detail-pass-1-kit.md; build
report build/detail-pass-1-hut.md), UPDATED for the fine-detail pass step 3a
(build/fine-detail-step3a-buildings.md): recipes/lodge_hut.py RECIPE is a fine_detail.FineRecipe
(fine_detail=True, sign_set=SIGN_SET); the benches, the wing racks, the upright drum and the two
entry lanterns are ROWS (GardenBench / kit racks / BarrelRandom01 / WallLightShortRange), the three
label faces live in the sign set SkiSignSetHutB (a sharer of the building sheet). NITPICK C (2026-10-13,
build/nitpick-c-lodges.md; Richard: "The GRILL (?) on the left side of the entrance may end up in mid-air depending
on terrain"): the fuel station (cradle drum + legs + pump, the timber crib, the barrel on it) left the host for two
GROUND-FOLLOWING BarrelRandom01 rows (m_ParentMesh -1) at the old drum centres; D13 audits every part whose foot is
on grade outside the plinth (structure / access only: no dressing).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_detail_hut.py
        [-- --quick]     blockout checks only (D1 parts / props, D7 geometry, D8, D10, D11, D5b)
        [-- --no-det]    skip D5 (the second weathered finish in this process)
        [-- --no-xproc]  skip D5b (two child-process blockouts)

One finish at the shipping size (recipe tex 2048, 48 AO samples, LODs, weather on) feeds
D1-D4, D6, D7, D9, D10, D12; a weather=None finish of the same build gives D4 its reference
(ControlMask byte-identical, every remaining host label rect identical in BaseColor: none since
step 3a); a second weathered finish in the same process is D5 (five PNGs + LOD0 / LOD1 / LOD2 /
_Win hashes identical); D5b runs --blockout in two child Blenders and compares the manifest hashes.

D1  detail parts by exact name the HOST still models (H1-H10 minus the parts rows replaced), 12
    bulbs, no bench_ / skirack_ / drum_ (nitpick C: the whole fuel station) / lamp_hut0 / lamp_hut1
    part, no routed label face; the B.1 fine geometry (flag mast 58 tris); no interior gable face
    (H11); 24 recipe rows: the 16 detail-pass rows + 2 GardenBench + 2 ground Barrels (nitpick C) +
    2 entry WallLightShortRange (known, dump-verified GUIDs) + 2 kit racks by their staged CIDs
D2  _Emissive lit texels <= 0.5 % of the map, split into lamp-face content and gutter
    dilation; every lamp lit inside its own rect; every lamp-material face >= 2.5 texels across
D3  host labels: none left (the three faces left for the sign set; D12 checks them there)
D4  manifest weather.vanilla.ok, snow share 0, G5 / G6; ControlMask = the weather=None
    finish byte for byte
D5  determinism (above); D5b cross-process blockout hashes
D6  LOD0 <= 5,300 (M10), LOD1 <= 2,400, LOD2 <= 450; w=1 median >= 35 px/m; non-soffit
    faces < 16 px/m <= 0.5 m2; tex 2048; batches 9
D7  plinth mode: no vertex in |z| <= 0.01 on LOD0 / 1 / 2 / _Win; every grade-standing new part
    reaches <= -0.05 and <= the 35 % design grade at its uphill edge; deck-standing dressing
    stands on the deck top
D8  lot: the bbox equals the shipped one (every new part inside it); coplanar
    same-facing overlaps involving a new part: none; no new face inside a massing volume
D9  LOD: new dressing absent from LOD1 (board frames, wing slabs / fascia / posts kept) and from
    LOD2 (wing slabs kept); ortho LOD2 XOR (5 cm) <= 8 % front / side / top
D10 sub-objects: deck props at the deck top (ray down hits a deck at gy +- 0.01); GROUND rows
    (m_ParentMesh -1, nitpick C: the two fuel barrels) at gy 0 with NO host geometry anywhere in
    their footprint's column (+0.05 m, z -8..+3: the terrain may put them at any height); wall props on one of our wall faces (<= 0.05 m, facing its normal); the render
    box of every new / moved prop (union of its variants, + 0.05 m) clear of our mesh, and every
    prop box clear of the new dressing; new / moved props clear of each other
D11 fine-detail H1 / H9 (step 3a): every new row at the part it replaced, measured on the
    PRE-PASS hut (`--no-fine` builder parts): benches' back plane = the removed bench's back plane
    +-15 mm (built 0.01 m out) and x within 0.015 m of its centre; the two ground barrels (nitpick C) on the
    removed cradle drum's / crib drum's plan centres +-0.01, parent -1, gy 0; the entry wall lights on the wall plane at the old lens (y, z) +-0.01; each kit
    rack at the removed rack frame's plan centre +-0.01 on the wing deck, its box inside the wing
    slab (negative: the 90-degree yaw the old along-X rack would need overhangs the wing); host lot
    bbox = the pre-pass one +-0.005 (H9)
D12 sign set (SkiSignSetHutB, a sharer): SG1 UV rects, SG2 each board within 1 mm of the pre-pass
    host's label quad (negative: 2 mm), SG3 no routed face in LOD0 / LOD1 / LOD2 or the labels
    block, SG4 one identity signset row with the staged CID, SG5 inside the lot, SG6 tris, the
    sharer settings = the five owner keys and no PNG, RESTAURANT T1 on the owner's sheet metrics
D13 nitpick C grade audit (builder parts): the parts whose foot stands on / below the design grade
    >= 0.2 m outside the plinth + cap footprint are exactly GRADE_AUDIT (piers, deck posts, bridge /
    wing posts, the entry steps, the basement stair + landing posts: structure and access); no
    dressing; the `--no-fine` hut still has the fuel station there (negative)
Exit 1 on the first failure; one PASS line otherwise.
"""

import glob
import hashlib
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile
import time
import traceback
from dataclasses import replace

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, HERE)
import bldg_recipes as R   # noqa: E402
import cs2_asset           # noqa: E402
import cs2_bldg as K       # noqa: E402
import cs2_compose         # noqa: E402
import cs2_signset as SS   # noqa: E402
import cs2_subobj          # noqa: E402
import fine_detail as FD   # noqa: E402
import lodge               # noqa: E402

KEY = "L2"
CHECKS = []
WARNS = []

# ---- contract numbers (design 3.2 / 6.2, skeptic M10) ----------------------------------
LOD0_CAP = 5300            # M10: the internal D6 cap raised for real ski gear
PPM_W1_MIN = 35.0
BULBS = 12
SHIPPED_LOT = {"x": [-8.075, 11.8], "y": [-8.575, 6.96]}    # the pre-pass manifest (H9)
# the detail-pass-1 parts the HOST still models after the fine-detail pass (step 3a): the benches,
# the wing racks (+ gear), the upright drum and the two routed label faces left
NEW_PARTS = ("lamp_hutS", "lamp_hutSwire", "deck_skiN", "deck_skiS", "deckfascia_skiN",
             "deckfascia_skiS", "col_skiN", "col_skiS",
             "downspout_hutSW", "downspout_hutSE", "downspout_hutSEext",
             "sign_hutname_frame", "sign_hutmenu_frame", "flag_hut", "flagpanel_hut",
             "stripe_hut1", "stripe_hut2")
NEW_PREFIXES = NEW_PARTS
# replaced by rows / routed to the sign set (must NOT be host parts any more)
GONE = ("bench_hutS0", "bench_hutS0legs", "bench_hutS1", "bench_hutS1legs", "skirack_wingN",
        "skirack_wingS", "drum_hutfuel2", "lamp_hut0", "lamp_hut0arm", "lamp_hut1", "lamp_hut1arm",
        "sign_hutname_face", "sign_hutmenu_face", "sign_hut_face",
        # nitpick C (2026-10-13): the fuel station left for two ground-following barrel rows
        "drum_hutfuel", "drum_hutfuellegs", "drum_hutfuelpump", "drum_hutfuel2base")
GONE_PREFIXES = ("bench_", "skirack_", "drum_")
# design B.1 fine geometry, tris per part (cs2_bldg fine=True, pinned in test_bldg_kit)
FINE_TRIS = {"flag_hut": 58}         # nitpick C: the cradle drum (124) and pump (36) left with the station
GRADE_STANDING = ("col_skiN", "col_skiS", "downspout_hutSEext")
# D13 (nitpick C): every builder part of the SHIPPING hut whose foot is on / below the design grade >= 0.2 m outside
# the plinth + cap footprint (scratchpad audit ncl/l2_grade_audit.json): structure (piers, deck posts, bridge / wing
# posts) and access (entry steps, the basement stair + its landing posts; its rail stands on the stair) only
GRADE_AUDIT = ("col_baslanding", "col_bridge", "col_hutposts", "col_skiN", "col_skiS", "pier_hut0", "pier_hut1",
               "pier_hut2", "stair_bas_str0", "stair_bas_str1", "stair_bas_treads", "step_hut1", "step_hut2")
GRADE_AUDIT_LEGACY = ("drum_hutfuel2base", "drum_hutfuellegs")     # the pre-pass hut (negative)
DECK_STANDING = ("lamp_hutS", "flag_hut", "flagpanel_hut")
LOD1_KEEP_NEW = ("sign_hutname_", "sign_hutmenu_", "deck_ski", "deckfascia_ski", "col_ski")
LOD2_KEEP_NEW = ("deck_ski",)
# design 1.6 placeholder GUIDs + the shipped L2 table (lodges-basic 4.7) + step 3a (GardenBench,
# Barrel); every one must also be dump-verified (lodge.verified_guids, asserted in D1)
KNOWN_GUIDS = {
    "fc058a3d03b337e4794c432a84170316", "b7e2343fd7ff9f642b77084a04738e43",
    "63c46490ab59afd4dae01ee442f1e462", "0d01c98e6f7cd9b44ac31084b6ba7ccd",
    "41e3c9ba6e1d6b948a6b1bc3499a5397", "0bbcb0620e01694448cb78385580351b",
    "cfc12fde01d9da24b86ddedd657eaae1", "b99325ff4ff4dfd459734d8f765c2597",
    "8df0703be09426948b5bc3262d86bc04", "fb4247f7a6f75b1478d9dd01ac88ce86"}
WALL_PROPS = ("WallLightShortRangeRandom01", "ACVentWallRandom01")
# render bounds, union over the family's variants, local (x, y up, z), from
# %TEMP%\pltar\dump.zip RenderPrefab m_Bounds (2026-10-11, build report section 3); step 3a: the
# bench / barrel = cs2_liftdress.SUBOBJ (dump-verified); kit props from subobj_bounds.json
PROP_BOUNDS = {
    "WallLightShortRangeRandom01": ((-0.14, -0.06, 0.0), (0.14, 0.06, 0.11)),
    "ACVentWallRandom01": ((-0.19, -0.20, 0.0), (0.19, 0.28, 0.25)),
    "GasHeaterRandom01": ((-0.37, 0.0, -0.37), (0.36, 2.24, 0.37)),
    "FirewoodPileRandom01": ((-1.62, 0.0, -0.65), (1.62, 1.59, 0.65)),
    "DeckChairRandom01": ((-0.40, 0.0, -1.19), (0.40, 1.23, 1.19)),
    "TableSetCommercialRandom01": ((-1.10, -0.01, -1.26), (1.10, 1.09, 1.26)),
    "ParasolRandom01": ((-1.75, 0.0, -0.73), (1.75, 2.64, 3.06)),
    "TrashbinRandom01": ((-0.36, 0.0, -0.36), (0.36, 1.04, 0.36)),
    "GardenBenchRandom01": ((-1.059, 0.0, -0.496), (1.059, 1.054, 0.496)),
    "BarrelRandom01": ((-0.345, 0.0, -0.345), (0.345, 0.864, 0.345)),
}
# props this pass added or moved (index into the recipe's sub_objects): the detail-pass second deck
# chair, firewood, heaters, wall lights, vents; step 3a: rows 16-22 (2 benches, barrel, 2 entry
# wall lights, 2 kit racks); nitpick C: rows 16-23 (2 benches, 2 ground barrels, 2 entry lights, 2 kit racks)
# T26 2026-10-16 dropped the parasol row (index 3); restored 2026-10-17, so the indices are the original (24 rows)
NEW_OR_MOVED = (5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23)
BOX_SKIP = ("ParasolRandom01",)      # an offset canopy: its AABB is mostly air over the rail
# a row standing on a modelled part other than a deck: prefab -> (part, top z) (nitpick C: none left; the barrels
# are ground rows)
STANDS = {}
GROUND_ROWS = ("BarrelRandom01",)     # m_ParentMesh -1 (nitpick C)


def ok(msg):
    CHECKS.append(msg)
    print(f"ok {msg}")


def warn(msg):
    WARNS.append(msg)
    print(f"WARN {msg}")


def sha(path):
    with open(path, "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()[:12]


# ======================================================================================
# mesh helpers (post-orient Blender frame -> authoring: (x, y, z) = (-by, bx, bz))
# ======================================================================================

def auth_coords(me, M=None):
    co = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    if M is not None:
        M = np.array(M)
        co = co @ M[:3, :3].T + M[:3, 3]
    return np.stack([-co[:, 1], co[:, 0], co[:, 2]], axis=1)


def poly_parts(me):
    out = np.zeros(len(me.polygons), np.int32)
    a = me.attributes.get("part")
    if a is not None:
        a.data.foreach_get("value", out)
    return out


def table_of(me):
    return json.loads(me[cs2_asset.PART_TABLE_PROP])


def part_names_of(me):
    table = table_of(me)
    return sorted({table[i - 1] for i in poly_parts(me) if 0 < i <= len(table)})


def faces_of(me, table, prefixes, exact=False):
    pp = poly_parts(me)
    pre = tuple(prefixes)
    ids = {i + 1 for i, n in enumerate(table) if (n in pre if exact else n.startswith(pre))}
    return [p for p in me.polygons if int(pp[p.index]) in ids]


def part_verts(me, V, table, prefixes, exact=False):
    idx = sorted({v for p in faces_of(me, table, prefixes, exact) for v in p.vertices})
    return V[idx]


def bvh_of(V, me):
    return BVHTree.FromPolygons([tuple(v) for v in V], [tuple(p.vertices) for p in me.polygons],
                                epsilon=0.0)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def load_png(path):
    img = bpy.data.images.load(path, check_existing=False)
    a = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], -1)
    bpy.data.images.remove(img)
    return a


def rows_of(recipe):
    """The recipe's sub_objects as (prefab, guid | cid, (gx, gy, gz), q, parent, kind) tuples."""
    out = []
    for e in recipe.sub_objects:
        if isinstance(e, dict):
            out.append((e["prefab"], e["m_Object"], tuple(e["position"]), tuple(e["rotation"]),
                        int(e.get("m_ParentMesh", -1)), e.get("kind", "vanilla")))
        else:
            out.append(tuple(e) + ("vanilla",))
    return out


def bounds_of(prefab, kind):
    if kind == "mod":
        (lo, hi), src = cs2_subobj.lookup_bounds(prefab, None)
        assert src == "cache", f"{prefab}: kit bounds from {src}"
        return tuple(lo), tuple(hi)
    return PROP_BOUNDS[prefab]


# ======================================================================================
# runs
# ======================================================================================

def recipe_of(**kw):
    r = R.get_recipe(KEY, R.load_recipes())
    return replace(r, **kw) if kw else r


def blockout(recipe, out, extra=()):
    res = lodge.run(recipe, lodge.parse_args(["--recipe", KEY, "--blockout", "--out", out] + list(extra)))
    obj = bpy.data.objects[res["name"]]
    me = obj.data.copy()
    me.name = f"{res['name']}Blk"
    me.use_fake_user = True
    return res, me, np.array(obj.matrix_world)


def finish(recipe, out):
    """A shipping-size finish (no previews, no validate) with the LOD meshes captured."""
    orig = K.lod_chain
    captured = []

    def spy(obj, name, targets, delete_lists, protect_prefixes=(), tol=0.25):
        lods, infos = orig(obj, name, targets, delete_lists, protect_prefixes, tol)
        for lod in lods:
            me = lod.data.copy()
            me.name = lod.name + "Cap"
            me.use_fake_user = True
            captured.append(me)
        return lods, infos
    K.lod_chain = spy
    try:
        res = lodge.run(recipe, lodge.parse_args(["--recipe", KEY, "--no-validate", "--out", out]))
    finally:
        K.lod_chain = orig
    d = res["asset_dir"]
    obj = bpy.data.objects[res["name"]]
    me = obj.data.copy()
    me.name = res["name"] + "Fin"
    me.use_fake_user = True
    import cs2_submesh
    win = bpy.data.objects.get(cs2_submesh.submesh_name(res["name"], "Win"))
    wme = None
    if win is not None:
        wme = win.data.copy()
        wme.name = res["name"] + "WinFin"
        wme.use_fake_user = True
    pngs = {os.path.basename(p)[len(res["name"]) + 1:-4]: p
            for p in sorted(glob.glob(os.path.join(d, "*.png")))}
    man = json.load(open(glob.glob(os.path.join(d, "*.manifest.json"))[0], encoding="utf-8"))
    return dict(res=res, me=me, M=np.array(obj.matrix_world), win=wme, lods=captured,
                pngs=pngs, man=man, A=res["tex"], out=out)


def legacy_parts(recipe):
    """{part name: (N, 3) authoring vertices} of the PRE-PASS hut's builder parts (`--no-fine`)."""
    lodge.clear_scene()
    parts = lodge.build_parts(FD.effective(recipe, fine=False), {"tower": True, "roof_windows": "win"})
    bpy.context.view_layer.update()
    out, tris = {}, {}
    for o, _k in parts:
        out[o.name] = np.array([tuple(o.matrix_world @ v.co) for v in o.data.vertices], np.float64)
        tris[o.name] = sum(len(p.vertices) - 2 for p in o.data.polygons)
    lodge.clear_scene()
    return out, tris


# ======================================================================================
# D1 / D7 / D8 / D10 on a mesh (blockout or finish)
# ======================================================================================

def d1_parts(recipe, mod, me, M, label):
    table = table_of(me)
    names = set(part_names_of(me))
    missing = [n for n in NEW_PARTS if n not in names]
    assert not missing, f"D1 {label}: missing parts {missing}"
    left = sorted(n for n in names if n in GONE or n.startswith(GONE_PREFIXES))
    assert not left, f"D1 {label}: parts the fine-detail pass replaced are still in the host: {left}"
    # 12 bulbs = 12 face islands (connected components) of lamp_hutS
    fs = faces_of(me, table, ("lamp_hutS",), exact=True)
    parent = {}

    def find(a):
        while parent.setdefault(a, a) != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    for p in fs:
        vs = list(p.vertices)
        for v in vs[1:]:
            parent[find(v)] = find(vs[0])
    comps = {find(v) for p in fs for v in p.vertices}
    assert len(comps) == BULBS, f"D1: {len(comps)} bulbs (want {BULBS})"
    # H11: no gable face looks into the building (normals toward the storey centre)
    V = auth_coords(me, M)
    cx, cy = 0.5 * (mod["FL_X0"] + mod["FL_X1"]), 0.0
    inward = 0.0
    for p in faces_of(me, table, ("gable_",)):
        P = V[list(p.vertices)]
        c = P.mean(0)
        n = np.cross(P[1] - P[0], P[2] - P[0])
        if np.linalg.norm(n) < 1e-12 or abs(n[2]) > 0.5 * np.linalg.norm(n):
            continue
        if np.dot(n[:2], np.array([cx, cy]) - c[:2]) > 0 and \
                mod["FL_X0"] - 0.01 <= c[0] <= mod["FL_X1"] + 0.01:
            inward += p.area
    assert inward < 0.01, f"D1 H11: {inward:.2f} m2 of gable faces look inward"
    rows = rows_of(recipe)
    # T26 2026-10-16 dropped the parasol (23 rows); restored 2026-10-17: 24 rows, the detail-pass rows 16
    assert len(rows) == 24, len(rows)
    assert [r[:5] for r in rows[:16]] == [tuple(s) for s in mod["SUB_OBJECTS"]], "D1: the 16 detail-pass rows moved"
    van = [r for r in rows if r[5] == "vanilla"]
    kit = [r for r in rows if r[5] == "mod"]
    bad = [r[0] for r in van if r[1] not in KNOWN_GUIDS]
    assert not bad, f"D1: unknown GUIDs {bad}"
    allowed, rejected = lodge.verified_guids()
    assert all(r[1] in allowed and r[1] not in rejected for r in van), "D1: a GUID is not dump-verified"
    # kit colour variants (2026-10-12): the racks name the hut's colour-set variant (recipe.kit_colour_set)
    assert len(kit) == 2 and all(FD.kit_cid(r[0], recipe.kit_colour_set) == r[1]
                                 == FD.staged_kit_cid(r[0], recipe.kit_colour_set) for r in kit), kit
    assert [r[0] for r in kit] == list(FD.AFRAME_RACKS[:2]), f"D1: kit loadouts {[r[0] for r in kit]} do not alternate"
    fam = {}
    for r in rows:
        fam[r[0]] = fam.get(r[0], 0) + 1
    ok(f"D1 {label}: {len(NEW_PARTS)} host detail parts by name, none of the {len(GONE)} replaced / "
       f"routed parts, {len(comps)} bulbs, 0 inward gable faces (H11), "
       f"{len(rows)} rows {fam} (GUIDs known + dump-verified, kit CIDs staged, loadouts alternate)")


def d1_fine_geometry(parts, legacy):
    """B.1 fine geometry on the BUILDER parts (before the join / weld / cull): the flag mast, the
    cradle drum and its pump take the fine=True builders (tris pinned); the pre-pass builder gave
    fewer (negative: the legacy parts do not match)."""
    tri = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o, _k in parts}
    got = {k: tri.get(k) for k in FINE_TRIS}
    assert got == FINE_TRIS, f"D1 fine geometry {got} != {FINE_TRIS}"
    old = {k: legacy.get(k) for k in FINE_TRIS}
    assert all(old[k] is not None and old[k] < FINE_TRIS[k] for k in FINE_TRIS), f"D1 legacy {old}"
    ok(f"D1 fine geometry (builder parts): {got}; pre-pass {old}")


def _grade_feet(named_verts, mod):
    """{part: (foot z, design grade at the foot's uphill-most x)} of the builder parts whose foot (lowest z, the
    vertices within 5 cm of it) stands on / below the 35 % design grade (or <= 0.2 m above it) and lies >= 0.2 m
    outside the plinth + cap-ring footprint: the parts that float / sink when the terrain differs (nitpick C)."""
    X0, X1 = mod["PL_X0"] - mod["CAP_PROUD"], mod["PL_X1"] + mod["CAP_PROUD"]
    Y = mod["PL_Y"] + mod["CAP_PROUD"]
    out = {}
    for name, V in named_verts.items():
        if not len(V):
            continue
        z0 = float(V[:, 2].min())
        foot = V[V[:, 2] <= z0 + 0.05]
        far = ((foot[:, 0] < X0 - 0.2) | (foot[:, 0] > X1 + 0.2) | (np.abs(foot[:, 1]) > Y + 0.2)).all()
        if z0 >= mod["DECK_TOP"] - 0.3 or not far:
            continue
        grade = float(mod["terrain_z"](float(foot[:, 0].max())))
        if grade - z0 < -0.2:
            continue
        out[name] = (round(z0, 3), round(grade, 3))
    return out


def d13_grade_audit(parts, lp, mod):
    """D13 (nitpick C): the shipping hut's grade-standing parts outside the plinth = GRADE_AUDIT (structure +
    access, no dressing); the pre-pass hut adds the fuel station (negative: the audit sees it)."""
    bpy.context.view_layer.update()
    cur = {o.name: np.array([tuple(o.matrix_world @ v.co) for v in o.data.vertices], np.float64)
           for o, _k in parts}
    got = _grade_feet(cur, mod)
    assert sorted(got) == sorted(GRADE_AUDIT), f"D13: grade-standing parts {sorted(got)} != {sorted(GRADE_AUDIT)}"
    old = _grade_feet(lp, mod)
    assert sorted(old) == sorted(GRADE_AUDIT + GRADE_AUDIT_LEGACY), f"D13 negative: pre-pass {sorted(old)}"
    ok(f"D13 grade audit: {len(got)} parts stand on grade outside the plinth, all structure / access "
       f"{ {k: got[k][0] for k in sorted(got)} }; no dressing (the pre-pass hut's fuel station "
       f"{ {k: old[k] for k in GRADE_AUDIT_LEGACY} } is caught)")


def tri_count_part(me, table, part):
    return sum(len(p.vertices) - 2 for p in faces_of(me, table, (part,), exact=True))


def d7_base(recipe, mod, me, M, label, others=()):
    table = table_of(me)
    V = auth_coords(me, M)
    for nm, A in (("LOD0", V),) + tuple(others):
        band = int((np.abs(A[:, 2]) <= 0.01).sum())
        assert band == 0, f"D7 {label} {nm}: {band} vertices in |z| <= 0.01"
    rows = []
    for part in GRADE_STANDING:
        P = part_verts(me, V, table, (part,), exact=True)
        assert len(P), f"D7: {part} has no vertices"
        z0, xu = float(P[:, 2].min()), float(P[:, 0].max())
        tz = mod["terrain_z"](xu)
        assert z0 <= -0.05 and z0 <= tz - 1e-6, f"D7: {part} lowest {z0:.3f} vs grade {tz:.3f} at x {xu:.2f}"
        rows.append(f"{part} {z0:.2f} (grade {tz:.2f})")
    for pre in DECK_STANDING:
        P = part_verts(me, V, table, (pre,))
        assert len(P), pre
        assert float(P[:, 2].min()) >= mod["DECK_TOP"] - 1e-4, f"D7: {pre} below the deck top"
    ok(f"D7 {label}: 0 band vertices on {['LOD0'] + [o[0] for o in others]}; grade-standing "
       f"{rows}; deck dressing on the deck top")


def d8_lot(recipe, mod, res, me, M, parts=None):
    lot = res["lot"]
    assert [round(v, 3) for v in lot["x"]] == SHIPPED_LOT["x"] and \
        [round(v, 3) for v in lot["y"]] == SHIPPED_LOT["y"], f"D8: lot bbox {lot} moved"
    table = table_of(me)
    V = auth_coords(me, M)
    P = part_verts(me, V, table, NEW_PREFIXES)
    lo, hi = P.min(0), P.max(0)
    sx, sy = SHIPPED_LOT["x"], SHIPPED_LOT["y"]
    assert sx[0] - 1e-6 <= lo[0] and hi[0] <= sx[1] + 1e-6 and sy[0] - 1e-6 <= lo[1] \
        and hi[1] <= sy[1] + 1e-6, f"D8: new parts x {lo[0]:.3f}..{hi[0]:.3f} y {lo[1]:.3f}..{hi[1]:.3f}"
    msg = (f"D8 lot: bbox x {lot['x']} y {lot['y']} unchanged (margin x {lot['margin_x']}, set by "
           f"the shipped step foot), new parts inside it: x {lo[0]:.2f}..{hi[0]:.2f}, y "
           f"{lo[1]:.2f}..{hi[1]:.2f}")
    if parts is not None:
        msg += "; " + d8_coplanar_inside(mod, parts)
    ok(msg)


def d8_coplanar_inside(mod, parts):
    """recipes/_civic check_coplanar / check_inside on the builder parts: coplanar same-facing
    overlaps where at least one part is new; new faces inside the storey / plinth boxes."""
    import importlib.util
    spec = importlib.util.spec_from_file_location("_civic_t", os.path.join(HERE, "recipes", "_civic.py"))
    C = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(C)
    new = [(o, k) for o, k in parts if o.name.startswith(NEW_PREFIXES)]
    bad = []
    # the S downspout's plinth extension overlaps its pipe by 1 cm, the mirror of the
    # shipped +Y pair (downspout_hutNW / NE + ext)
    ex = (("downspout_hutSE", "downspout_hutSEext"),)
    try:
        C.check_coplanar(new, exempt=ex)
    except RuntimeError as e:
        bad.append(str(e))
    rest = [(o, k) for o, k in parts if not o.name.startswith(NEW_PREFIXES)]
    for o, k in rest:
        try:
            C.check_coplanar(new + [(o, k)], exempt=ex)
        except RuntimeError as e:
            msg = str(e)
            if o.name in msg:
                bad.append(msg)
    assert not bad, f"D8 coplanar: {bad[:3]}"
    vols = [((mod["FL_X0"], -mod["FL_Y"], mod["FL1_Z"]), (mod["FL_X1"], mod["FL_Y"], mod["PLATE_Z"])),
            ((mod["PL_X0"], -mod["PL_Y"], -mod["PL_DEPTH"]), (mod["PL_X1"], mod["PL_Y"], mod["PL_TOP"]))]
    C.check_inside(new, vols)
    return f"coplanar: none with {len(new)} new parts; check_inside: no new face in the storey / plinth"


def d10_props(recipe, mod, me, M, label):
    table = table_of(me)
    pp = poly_parts(me)
    V = auth_coords(me, M)
    bvh = bvh_of(V, me)
    names = [table[int(pp[i]) - 1] if 0 < pp[i] <= len(table) else "?" for i in range(len(me.polygons))]
    deck_top = mod["DECK_TOP"]
    rows, boxes, ground = [], [], []
    for k, (prefab, _g, (gx, gy, gz), q, parent, kind) in enumerate(rows_of(recipe)):
        if prefab in GROUND_ROWS:
            # nitpick C: a ground-following row (OnGround: the engine drops it onto the terrain under it, at any
            # height): gy 0, parent -1, and NO host geometry in the column over its footprint (+0.05 m, z -8..+3)
            assert parent == -1 and abs(gy) < 1e-9, (prefab, parent, gy)
            lo, hi = PROP_BOUNDS[prefab]
            cx, cy = gz, gx                              # authoring plan
            a0, a1 = cx + lo[2] - 0.05, cx + hi[2] + 0.05     # identity row: local +Z = authoring +X
            b0, b1 = cy + lo[0] - 0.05, cy + hi[0] + 0.05     # local +X = authoring +Y
            inside = np.where((V[:, 0] > a0) & (V[:, 0] < a1) & (V[:, 1] > b0) & (V[:, 1] < b1) &
                              (V[:, 2] > -8.0) & (V[:, 2] < 3.0))[0]
            hits = [bvh.ray_cast(Vector((x, y, 3.0)), Vector((0.0, 0.0, -1.0)), 11.0)[0]
                    for x in (a0, cx, a1) for y in (b0, cy, b1)]
            assert not len(inside) and all(h is None for h in hits), \
                f"D10: ground row {prefab} #{k} has host geometry over its footprint ({len(inside)} vertices)"
            lot = 4.0 * recipe.lot[1], 4.0 * recipe.lot[0]
            assert abs(a0) <= lot[0] and abs(a1) <= lot[0] and abs(b0) <= lot[1] and abs(b1) <= lot[1], \
                f"D10: ground row {prefab} #{k} outside the lot"
            ground.append(f"{prefab[:6]}@({cx:.2f}, {cy:.2f})")
            boxes.append((k, prefab, Vector((gz, gx, 0.0)), Vector((0.0, 1.0, 0.0)), Vector((0.0, 0.0, 1.0)),
                          Vector((1.0, 0.0, 0.0)), [lo[0], 0.01, lo[2]], list(hi)))
            continue
        assert parent == 0, (prefab, parent)
        pos = Vector((gz, gx, gy))                       # authoring
        th = 2.0 * math.atan2(q[1], q[3])
        F = Vector((math.cos(th), math.sin(th), 0.0))    # local +Z
        X = Vector((-math.sin(th), math.cos(th), 0.0))   # local +X
        U = Vector((0.0, 0.0, 1.0))
        if prefab in WALL_PROPS:
            hit = bvh.ray_cast(pos + F * 0.10, -F, 0.30)
            assert hit[0] is not None, f"D10: {prefab} #{k}: no wall behind it"
            d = (hit[0] - pos).length
            nrm = Vector(hit[1])
            assert d <= 0.05 and nrm.dot(F) >= 0.99, f"D10: {prefab} #{k} wall {d:.3f} m, n.F {nrm.dot(F):.3f}"
            assert names[hit[2]].startswith(("wall_", "plinth_")), (prefab, names[hit[2]])
            rows.append(f"{prefab[:14]}@{names[hit[2]]}")
        else:
            stand = STANDS.get(prefab)
            if stand is None:
                assert abs(gy - deck_top) < 1e-6, (prefab, gy)
            hit = bvh.ray_cast(pos + U * 0.5, -U, 1.0)
            assert hit[0] is not None and abs(hit[0].z - gy) <= 0.01 and Vector(hit[1]).z > 0.99, \
                f"D10: {prefab} #{k}: no deck under it ({hit[0]})"
            assert names[hit[2]].startswith(stand or "deck_"), (prefab, names[hit[2]])
        lo, hi = bounds_of(prefab, kind)
        lo, hi = list(lo), list(hi)
        if prefab in WALL_PROPS:
            lo[2] = 0.01                                  # off the wall it hangs on
        else:
            lo[1] = 0.01                                  # off the deck it stands on
        boxes.append((k, prefab, pos, X, U, F, lo, hi))
    # render boxes (+ 0.05 m) against our mesh
    clash, info = [], []
    for k, prefab, pos, X, U, F, lo, hi in boxes:
        if prefab in BOX_SKIP or prefab in GROUND_ROWS:     # ground rows: the column check above
            continue
        a = [lo[0] - 0.05, lo[1] - (0.05 if prefab in WALL_PROPS else 0.0), lo[2] - (0.0 if prefab in WALL_PROPS else 0.05)]
        b = [hi[0] + 0.05, hi[1] + 0.05, hi[2] + 0.05]
        cs = [pos + X * x + U * y + F * z for x in (a[0], b[0]) for y in (a[1], b[1]) for z in (a[2], b[2])]
        faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        bb = BVHTree.FromPolygons([tuple(c) for c in cs], faces)
        hit_ids = {j for _i, j in bb.overlap(bvh)}
        # ... and every mesh vertex strictly inside the box (a small part wholly inside it
        # crosses no box face)
        D = V - np.array(list(pos))
        lx, ly, lz = D @ np.array(list(X)), D @ np.array(list(U)), D @ np.array(list(F))
        inside = np.where((lx > a[0]) & (lx < b[0]) & (ly > a[1]) & (ly < b[1]) &
                          (lz > a[2]) & (lz < b[2]))[0]
        if len(inside):
            vin = set(int(i) for i in inside)
            hit_ids |= {p.index for p in me.polygons if vin.intersection(p.vertices)}
        hits = sorted({names[j] for j in hit_ids})
        if not hits:
            continue
        if k in NEW_OR_MOVED or any(h.startswith(NEW_PREFIXES) for h in hits):
            clash.append((k, prefab, hits[:4]))
        else:
            info.append((k, prefab, hits[:3]))
    assert not clash, f"D10 clearance: {clash}"
    # new / moved props against every other prop (real bounds, no margin)
    def aabb(k):
        _k, prefab, pos, X, U, F, lo, hi = boxes[k]
        cs = np.array([list(pos + X * x + U * y + F * z) for x in (lo[0], hi[0])
                       for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
        return cs.min(0), cs.max(0)
    pairs = []
    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            if i not in NEW_OR_MOVED and j not in NEW_OR_MOVED:
                continue
            if boxes[i][1] in BOX_SKIP or boxes[j][1] in BOX_SKIP:
                continue
            (a0, a1), (b0, b1) = aabb(i), aabb(j)
            if np.all(a0 < b1) and np.all(b0 < a1):
                pairs.append((boxes[i][1], i, boxes[j][1], j))
    assert not pairs, f"D10 prop vs prop: {pairs}"
    if info:
        warn(f"D10 {label}: pre-existing props whose AABB (+0.05) touches existing parts: {info}")
    ok(f"D10 {label}: {len(boxes) - len(ground)} props parent 0 + ground rows (parent -1) {ground} with no host "
       f"geometry over their footprints (z -8..+3); deck props on deck_ tops, "
       f"wall props on {rows}; render boxes of {len(NEW_OR_MOVED)} new / moved props + 0.05 m clear of "
       f"the mesh and of each other; every prop box clear of the new dressing")


def d11_fine(recipe, mod, lp, lot):
    """H1 (every step-3a row at the part it replaced, on the pre-pass builder parts) and H9."""
    rows = rows_of(recipe)[16:]
    benches = [r for r in rows if r[0] == "GardenBenchRandom01"]
    for (tag, _x), r in zip(mod["GBENCH_X"], benches):
        P = np.vstack([lp[f"bench_{tag}"], lp[f"bench_{tag}legs"]])
        back = float(P[:, 1].max())                          # the -Y facing bench's back plane
        half = PROP_BOUNDS["GardenBenchRandom01"][1][2]
        gx, gy, gz = r[2]
        th = 2.0 * math.atan2(r[3][1], r[3][3])
        fy = math.sin(th)                                    # local +Z: authoring -Y (sitter)
        assert fy < -0.999, f"D11: bench {tag} faces {th}"
        d_back = abs((gx + half) - back)
        d_x = abs(gz - 0.5 * (P[:, 0].min() + P[:, 0].max()))
        assert d_back <= 0.015 and d_x <= 0.015 and abs(gy - mod["DECK_TOP"]) < 1e-6, \
            f"D11 H1: bench row {tag} back {gx + half:.3f} vs {back:.3f}, x off {d_x:.3f}"
    barrel = [r for r in rows if r[0] == "BarrelRandom01"]
    assert len(barrel) == 2, barrel
    # nitpick C: the two ground barrels at the removed cradle drum's and crib drum's plan centres, parent -1, gy 0
    for tag, r in zip(("drum_hutfuel", "drum_hutfuel2"), barrel):
        P = lp[tag]
        cx, cy = 0.5 * (P[:, 0].min() + P[:, 0].max()), 0.5 * (P[:, 1].min() + P[:, 1].max())
        gx, gy, gz = r[2]
        assert math.hypot(gz - cx, gx - cy) <= 0.01 and gy == 0.0 and r[4] == -1, \
            f"D11 H1: barrel {r[2]} (parent {r[4]}) vs {tag} plan centre ({cx:.3f}, {cy:.3f})"
    lamps = [r for r in rows if r[0] == "WallLightShortRangeRandom01"]
    assert len(lamps) == 2
    for tag, r in zip(("lamp_hut0", "lamp_hut1"), lamps):
        P = lp[tag]
        cy, cz = 0.5 * (P[:, 1].min() + P[:, 1].max()), 0.5 * (P[:, 2].min() + P[:, 2].max())
        gx, gy, gz = r[2]
        assert abs(gx - cy) <= 0.01 and abs(gy - cz) <= 0.01 and abs(gz - mod["FL_X1"]) < 1e-6 \
            and tuple(r[3]) == (0.0, 0.0, 0.0, 1.0), f"D11 H1: {tag} row {r[2]} vs lens ({cy:.3f}, {cz:.3f})"
    racks = [r for r in rows if r[5] == "mod"]
    (klo, khi), _src = cs2_subobj.lookup_bounds(racks[0][0], None)
    wings = {"wingN": (mod["WING_X0"], mod["BRIDGE_X1"], mod["BRIDGE_Y"], mod["WING_Y"]),
             "wingS": (mod["WING_X0"], mod["BRIDGE_X1"], -mod["WING_Y"], -mod["BRIDGE_Y"])}

    def box_plan(r, yaw_deg):
        gx, _gy, gz = r[2]
        a = math.radians(yaw_deg)
        c, s_ = math.cos(a), math.sin(a)
        pts = [(x * c + z * s_, -x * s_ + z * c) for x in (klo[0], khi[0]) for z in (klo[2], khi[2])]
        gxs, gzs = [gx + p[0] for p in pts], [gz + p[1] for p in pts]
        return min(gzs), max(gzs), min(gxs), max(gxs)        # authoring x0, x1, y0, y1

    for tag, r in zip(("wingN", "wingS"), racks):
        P = lp[f"skirack_{tag}"]
        cx, cy = 0.5 * (P[:, 0].min() + P[:, 0].max()), 0.5 * (P[:, 1].min() + P[:, 1].max())
        gx, gy, gz = r[2]
        assert math.hypot(gz - cx, gx - cy) <= 0.01 and abs(gy - mod["DECK_TOP"]) < 1e-6 \
            and tuple(r[3]) == (0.0, 0.0, 0.0, 1.0), f"D11 H1: kit rack {tag} {r[2]} vs frame centre ({cx:.3f}, {cy:.3f})"
        wx0, wx1, wy0, wy1 = wings[tag]
        x0, x1, y0, y1 = box_plan(r, 0.0)
        assert wx0 <= x0 and x1 <= wx1 and wy0 <= y0 and y1 <= wy1, \
            f"D11: kit rack {tag} plan x {x0:.3f}..{x1:.3f} y {y0:.3f}..{y1:.3f} outside the wing"
        x0, x1, y0, y1 = box_plan(r, 90.0)
        assert not (wx0 <= x0 and x1 <= wx1 and wy0 <= y0 and y1 <= wy1), \
            "D11 negative: the along-X (yaw 90) kit rack fits the wing"
    for ax in ("x", "y"):
        d = max(abs(a - b) for a, b in zip(lot[ax], SHIPPED_LOT[ax]))
        assert d <= 0.005, f"D11 H9: lot {ax} {lot[ax]} vs pre-pass {SHIPPED_LOT[ax]} ({d:.4f} m)"
    ok(f"D11 H1 / H9: {len(benches)} bench rows on the removed benches' back plane (x within 0.015), the "
       f"2 ground barrels (parent -1) on the removed fuel drums' plan centres, {len(lamps)} entry wall lights at the old lens "
       f"(y, z) on the wall, {len(racks)} kit racks at the removed frames' centres inside the wing "
       f"(yaw 90 would overhang: negative), lot x {lot['x']} y {lot['y']} = the pre-pass +-5 mm")


# ======================================================================================
# finish checks
# ======================================================================================

def d2_emissive(fin):
    """Lit texels split into lamp-face CONTENT (inside each lamp face's UV rect) and gutter
    DILATION (the bake pads every island into its 8 px gutter): the contract is the 0.5 % map
    share, every lamp lit inside its own rect, and >= 2.5 texels across every lamp face."""
    me, A = fin["me"], fin["A"]
    em = load_png(fin["pngs"]["Emissive"])
    litm = em[..., :3].max(axis=-1) > 0.0
    lit = int(litm.sum())
    frac = lit / float(em.shape[0] * em.shape[1])
    assert frac <= 0.005, f"D2: {lit} lit texels = {100 * frac:.3f} % of the map"
    table = table_of(me)
    pp = poly_parts(me)
    uv = me.uv_layers["UVUnique"].data
    worst = (1e9, None)
    lamp_parts = [n for n in table if n.startswith("lamp_") and not n.endswith(("arm", "wire"))]
    cov = np.zeros(litm.shape, bool)
    per = {}
    for p in faces_of(me, table, lamp_parts, exact=True):
        nm = table[int(pp[p.index]) - 1]
        Q = np.array([uv[li].uv[:] for li in p.loop_indices]) * A
        e = min(np.linalg.norm(Q[(i + 1) % len(Q)] - Q[i]) for i in range(len(Q)))
        if e < worst[0]:
            worst = (float(e), nm)
        c0, c1 = int(math.floor(Q[:, 0].min())), int(math.ceil(Q[:, 0].max()))
        r0, r1 = int(math.floor(Q[:, 1].min())), int(math.ceil(Q[:, 1].max()))
        cov[r0:r1, c0:c1] = True
        per[nm] = per.get(nm, 0) + int(litm[r0:r1, c0:c1].sum())
    dark = [n for n in lamp_parts if per.get(n, 0) == 0]
    assert not dark, f"D2: lamps with no lit texel in their own rects: {dark}"
    assert worst[0] >= 2.5, f"D2: lamp face {worst[1]} only {worst[0]:.2f} texels across"
    content = int((litm & cov).sum())
    ok(f"D2 emissive: {lit} lit texels ({100 * frac:.3f} % of {A}^2 <= 0.5 %): content {content} in "
       f"the lamp faces' rects, {lit - content} gutter dilation; every lamp lit {per}; every lamp "
       f"face >= {worst[0]:.2f} texels across (narrowest {worst[1]})")
    return {"lit": lit, "content": content, "dilation": lit - content}


def d3_labels(fin):
    """Step 3a: no label left in the host (the three faces live in the sign set: D12)."""
    info = fin["man"].get("labels") or {}
    host = SS.host_recipe(fin["recipe"], fin["recipe"].sign_set).labels
    assert not host and not info, f"D3: host labels left {[e[0] for e in host]} / {sorted(info)}"
    ok("D3 labels: none left in the host (RESTAURANT, the pictogram and the menu are sign-set faces, D12)")
    return {}


def d4_weather(recipe, fin, ref, rects):
    w = fin["man"].get("weather")
    assert w, "D4: no manifest weather block"
    v, g = w["vanilla"], w["guards"]
    assert v["ok"], f"D4: vanilla budget {v['failed']} (VL1 {v['VL1_mean_rel_dL']}, VL2 {v['VL2_share_rel_dL_gt_0.15']})"
    assert v["VL5_snow_share"] == 0.0 and w["snow"] == 0.0, (v["VL5_snow_share"], w["snow"])
    assert g["G5_ctl_identity"] and g["G6_protect_identical"] and g["G1_alpha_kept"] \
        and g["G2_msk_GB_kept"] and g["G7_range_ok"], g
    assert sha(fin["pngs"]["ControlMask"]) == sha(ref["pngs"]["ControlMask"]), "D4: ControlMask differs"
    assert sha(fin["pngs"]["BaseColor"]) != sha(ref["pngs"]["BaseColor"]), "D4: no weathering"
    b1, b0 = load_png(fin["pngs"]["BaseColor"]), load_png(ref["pngs"]["BaseColor"])
    for prefix, (r0, r1, c0, c1) in rects.items():
        assert np.array_equal(b1[r0:r1, c0:c1], b0[r0:r1, c0:c1]), f"D4: label {prefix} changed"
    ok(f"D4 weather: intensity {w['intensity']} ({w['slot_wear']} slots, ground {w['ground']}), "
       f"vanilla ok (VL1 {v['VL1_mean_rel_dL']}, VL2 {v['VL2_share_rel_dL_gt_0.15']}, VL3 "
       f"{v['VL3_mean_dsmooth']}, VL4 {v['VL4_max']}), snow 0, G1 / G2 / G5 / G6 / G7 true; "
       f"ControlMask = the weather=None finish; {len(rects)} host label rects")
    return w


def d6_budgets(recipe, fin):
    man, me, A = fin["man"], fin["me"], fin["A"]
    t = {int(k): v for k, v in man["tris"].items()}
    assert t[0] <= LOD0_CAP, f"D6: LOD0 {t[0]} > {LOD0_CAP}"
    assert t[1] <= recipe.budget.lod1 and t[2] <= recipe.budget.lod2_cap, t
    med = float(man["shipped_density"]["median_w1"])
    assert med >= PPM_W1_MIN, f"D6: w=1 median {med}"
    assert man["tex"] == 2048 and man["batches_per_mesh_group"] == 9, (man["tex"], man["batches_per_mesh_group"])
    table = table_of(me)
    pp = poly_parts(me)
    uv = me.uv_layers["UVUnique"].data
    V = auth_coords(me, fin["M"])
    low = 0.0
    for p in me.polygons:
        nm = table[int(pp[p.index]) - 1]
        if nm.startswith("soffit_") or p.area < 1e-9:
            continue
        Q = np.array([uv[li].uv[:] for li in p.loop_indices])
        ua = 0.0
        for k in range(1, len(Q) - 1):
            d1, d2 = Q[k] - Q[0], Q[k + 1] - Q[0]
            ua += 0.5 * abs(d1[0] * d2[1] - d1[1] * d2[0])
        P = V[list(p.vertices)]
        area = 0.0
        for k in range(1, len(P) - 1):
            area += 0.5 * np.linalg.norm(np.cross(P[k] - P[0], P[k + 1] - P[0]))
        if area > 1e-9 and math.sqrt(ua * A * A / area) < 16.0:
            low += area
    assert low <= 0.5, f"D6: {low:.2f} m2 of non-soffit faces below 16 px/m"
    ok(f"D6 budgets: tris {t} (LOD0 <= {LOD0_CAP}, LOD1 <= {recipe.budget.lod1}, LOD2 <= "
       f"{recipe.budget.lod2_cap}); w=1 median {med} px/m (>= {PPM_W1_MIN}); non-soffit < 16 px/m "
       f"{low:.2f} m2; tex {man['tex']}; batches {man['batches_per_mesh_group']}")
    return t, med, low


def _raster(co, tris, ax, lo, shp, cell):
    S = np.zeros(shp, bool)
    P = co[:, ax]
    for t in tris:
        Q = P[list(t)]
        mn = np.maximum(np.floor((Q.min(0) - lo) / cell).astype(int), 0)
        mx = np.minimum(np.ceil((Q.max(0) - lo) / cell).astype(int), shp)
        if (mx <= mn).any():
            continue
        Xg, Yg = np.meshgrid(lo[0] + (np.arange(mn[0], mx[0]) + 0.5) * cell,
                             lo[1] + (np.arange(mn[1], mx[1]) + 0.5) * cell, indexing="ij")
        (x0, y0), (x1, y1), (x2, y2) = Q
        den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(den) < 1e-12:
            continue
        a = ((y1 - y2) * (Xg - x2) + (x2 - x1) * (Yg - y2)) / den
        b = ((y2 - y0) * (Xg - x2) + (x0 - x2) * (Yg - y2)) / den
        S[mn[0]:mx[0], mn[1]:mx[1]] |= (a >= -1e-6) & (b >= -1e-6) & (1 - a - b >= -1e-6)
    return S


def _tris(me):
    out = []
    for p in me.polygons:
        vs = list(p.vertices)
        out += [(vs[0], vs[k], vs[k + 1]) for k in range(1, len(vs) - 1)]
    return out


def d9_lods(recipe, fin, mod):
    me0, lods = fin["me"], fin["lods"]
    assert len(lods) == 2, len(lods)
    new1 = [n for n in part_names_of(lods[0]) if n.startswith(NEW_PREFIXES)]
    bad1 = [n for n in new1 if not n.startswith(LOD1_KEEP_NEW)]
    assert not bad1, f"D9: new dressing on LOD1 {bad1}"
    new2 = [n for n in part_names_of(lods[1]) if n.startswith(NEW_PREFIXES)]
    bad2 = [n for n in new2 if not n.startswith(LOD2_KEEP_NEW)]
    assert not bad2, f"D9: new dressing on LOD2 {bad2}"
    co0 = auth_coords(me0, fin["M"])
    lo3, hi3 = co0.min(0) - 0.5, co0.max(0) + 0.5
    cell = 0.05
    xor = {}
    for v, ax in (("front", [1, 2]), ("side", [0, 2]), ("top", [0, 1])):
        lo = lo3[ax]
        shp = tuple(np.ceil((hi3[ax] - lo) / cell).astype(int))
        s0 = _raster(co0, _tris(me0), ax, lo, shp, cell)
        for lvl, me in (("LOD1", lods[0]), ("LOD2", lods[1])):
            c = auth_coords(me)
            s = _raster(c, _tris(me), ax, lo, shp, cell)
            xor[f"{lvl}_{v}"] = round(100.0 * float((s0 ^ s).sum()) / float(s0.sum()), 2)
    over = {k: x for k, x in xor.items() if k.startswith("LOD2") and x > 8.0}
    assert not over, f"D9: ortho LOD2 XOR {over} > 8 %"
    for lvl, me in (("LOD1", lods[0]), ("LOD2", lods[1])):
        d7_vs = auth_coords(me)
        band = int((np.abs(d7_vs[:, 2]) <= 0.01).sum())
        assert band == 0, f"D7 {lvl}: {band} band vertices"
    if fin["win"] is not None:
        Vw = auth_coords(fin["win"])
        assert int((np.abs(Vw[:, 2]) <= 0.01).sum()) == 0, "D7 _Win band vertices"
    ok(f"D9 LODs: LOD1 keeps only {sorted(set(n.rstrip('0123456789') for n in new1))} of the new parts, "
       f"LOD2 only {new2}; ortho XOR (5 cm) {xor}; D7 also: 0 band vertices on LOD1 / LOD2 / _Win")
    return xor


def faces_game(me, M, names):
    """{part name: (4, 3) game coords} of single-quad parts of a joined, net-oriented mesh."""
    table = table_of(me)
    pv = poly_parts(me)
    co = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    Mx = np.array(M)
    co = co @ Mx[:3, :3].T + Mx[:3, 3]
    G = np.stack([co[:, 0], co[:, 2], -co[:, 1]], axis=1)
    out = {}
    for p in me.polygons:
        n = table[pv[p.index] - 1]
        if n in names:
            out[n] = G[list(p.vertices)]
    return out


def d12_signset(fin, legacy_blk):
    """The sign set SkiSignSetHutB on the shipping finish: SG1-SG6 + the sharer settings."""
    recipe = fin["recipe"]
    spec = recipe.sign_set
    ss = fin["res"]["sign_set"]
    sobj = ss["obj"]
    names = spec.face_names()
    assert not spec.owner and spec.mesh == "SkiSignSetHutB", spec
    bad = SS.sg1_uv_in_rect(sobj, spec)
    assert not bad, f"D12 SG1: {bad[:2]}"
    # SG2 against the pre-pass host's own label quads
    set_faces = {}
    st = json.loads(sobj.data[cs2_asset.PART_TABLE_PROP])
    pv = np.zeros(len(sobj.data.polygons), np.int32)
    sobj.data.attributes["part"].data.foreach_get("value", pv)
    G = SS.game_coords(sobj)
    for p in sobj.data.polygons:
        set_faces[st[pv[p.index] - 1]] = G[list(p.vertices)]
    _res0, me0, M0 = legacy_blk
    old = faces_game(me0, M0, set(names))
    bad = SS.sg2_positions(set_faces, old)
    assert len(old) == len(names) and not bad, f"D12 SG2: {bad[:2]}"
    moved = {k: v.copy() for k, v in set_faces.items()}
    moved[names[0]][0, 0] += 0.002
    assert SS.sg2_positions(moved, old), "D12 SG2 negative: a 2 mm move is not reported"
    # SG3 host LOD0 / LOD1 / LOD2 tables + the labels block
    tables = {"LOD0": part_names_of(fin["me"])}
    for i, me in enumerate(fin["lods"], start=1):
        tables[f"LOD{i}"] = part_names_of(me)
    assert not SS.sg3_no_routed_left(tables, fin["man"].get("labels") or {}, spec), "D12 SG3"
    # SG4 one identity row with the staged CID; SG5 lot; SG6 tris
    cid = FD.staged_cid(spec.prop, spec.letter, kind="signs")
    assert cid and ss["cid"] == cid, (cid, ss["cid"])
    assert not SS.sg4_identity_row(fin["man"]["sub_objects"], spec, cid), "D12 SG4"
    bounds = SS.bounds_game(sobj)
    W_, D_ = recipe.lot
    assert not SS.sg5_in_lot(bounds, (4.0 * W_, 4.0 * D_)), f"D12 SG5 {bounds}"
    n = cs2_asset.tri_count(sobj)
    assert not SS.sg6_tris(n) and n == 2 * len(names), f"D12 SG6 {n}"
    # the sharer folder: five owner keys, no PNG
    sd = os.path.join(os.path.dirname(fin["out"]), "signs", spec.letter, spec.mesh)
    sett = json.load(open(os.path.join(sd, "settings.json"), encoding="utf-8"))
    want = {f"{spec.mesh}_{m}.png": f"../{SS.SHEET_OWNER}/{SS.SHEET_OWNER}_{m}.png" for m in SS.MAPS}
    assert sett == {"sharedAssets": want} and not glob.glob(os.path.join(sd, "*.png")), sett
    # RESTAURANT on the owner's shipped sheet (vector, T1)
    om = json.load(open(os.path.join(REPO, "assets", "import", "signs", SS.SHEET_LETTER, SS.SHEET_OWNER,
                                     f"{SS.SHEET_OWNER}.manifest.json"), encoding="utf-8"))
    met = om["sheet"]["metrics"]["hut.name"]
    txt = [d for d in met["draws"] if d.get("kind") == "text"][0]
    assert txt["tier"] == "T1" and txt["cap_m"] >= 0.40 and txt["stroke_px"] >= 4.0, txt
    ok(f"D12 sign set {spec.mesh}: {len(names)} faces SG1 UV = rect, SG2 <= 1 mm from the pre-pass quads "
       f"(2 mm negative caught), SG3 none left in LOD0-2 / labels, SG4 one identity row CID {cid[:8]}, SG5 "
       f"{bounds} in the lot, SG6 {n} tris, sharer settings = the five owner keys, RESTAURANT {txt['tier']} "
       f"cap {txt['cap_m']} m stroke {txt['stroke_px']} px")
    return {"tris": n, "bounds": bounds, "cid": cid}


def d5_same(a, b):
    pa = {k: sha(v) for k, v in a["pngs"].items()}
    pb = {k: sha(v) for k, v in b["pngs"].items()}
    assert pa == pb, f"D5: PNGs differ {pa} vs {pb}"
    ha, hb = a["man"]["mesh_hash"], b["man"]["mesh_hash"]
    assert ha == hb, f"D5: mesh hashes differ {ha} vs {hb}"
    ok(f"D5 determinism: two finishes in one process, 5 PNGs {pa} and mesh hashes {ha} identical")


def d5b_xproc(tmp):
    hs = []
    for i in range(2):
        d = os.path.join(tmp, f"x{i}", "B")
        log = os.path.join(tmp, f"x{i}.log")
        cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
               "--python", os.path.join(HERE, "lodge.py"), "--", "--recipe", KEY, "--blockout",
               "--out", d]
        with open(log, "w", encoding="utf-8") as fh:
            rc = subprocess.run(cmd, stdout=fh, stderr=subprocess.STDOUT, timeout=900).returncode
        assert rc == 0, f"D5b: child {i} exit {rc} ({log})"
        man = json.load(open(glob.glob(os.path.join(d, "*", "*.manifest.json"))[0], encoding="utf-8"))
        hs.append((man["mesh_hash"], man["rooms"]))
    assert hs[0] == hs[1], f"D5b: {hs}"
    ok(f"D5b cross-process blockout: {hs[0][0]} (rooms {len(hs[0][1])}) identical in 2 processes")
    return hs[0][0]


# ======================================================================================

def main():
    t0 = time.perf_counter()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    quick = "--quick" in argv
    recipe = recipe_of()
    assert isinstance(recipe, FD.FineRecipe) and recipe.fine_detail and recipe.sign_set is not None, \
        "L2 is not the fine-detail hut"
    mod = recipe.builder.__globals__
    tmp = tempfile.mkdtemp(prefix="detail_hut_")
    summary = {}
    try:
        lp, ltris = legacy_parts(recipe)
        legacy_blk = blockout(recipe, os.path.join(tmp, "legacy", "B"), extra=("--no-fine",))
        lodge.clear_scene()
        res_b, me_b, M_b = blockout(recipe, os.path.join(tmp, "blk", "B"))
        d1_parts(recipe, mod, me_b, M_b, "blockout")
        d7_base(recipe, mod, me_b, M_b, "blockout")
        lodge.clear_scene()
        parts = list(recipe.builder(recipe, {}))         # the builder parts (pre-join)
        d1_fine_geometry(parts, ltris)
        d13_grade_audit(parts, lp, mod)
        d8_lot(recipe, mod, res_b, me_b, M_b, parts)
        lodge.clear_scene()
        d10_props(recipe, mod, me_b, M_b, "blockout")
        d11_fine(recipe, mod, lp, res_b["lot"])
        if "--no-xproc" not in argv:
            summary["xproc"] = d5b_xproc(tmp)
        if quick:
            print(f"PASS test_detail_hut (quick): {len(CHECKS)} check groups, {len(WARNS)} WARN "
                  f"({time.perf_counter() - t0:.0f} s)")
            return
        fin = finish(recipe, os.path.join(tmp, "w", "B"))
        fin["recipe"] = recipe
        d1_parts(recipe, mod, fin["me"], fin["M"], "finish")
        d10_props(recipe, mod, fin["me"], fin["M"], "finish")
        lit = d2_emissive(fin)
        rects = d3_labels(fin)
        sg = d12_signset(fin, legacy_blk)
        t, med, low = d6_budgets(recipe, fin)
        d7_base(recipe, mod, fin["me"], fin["M"], "finish")
        xor = d9_lods(recipe, fin, mod)
        ref = finish(replace(recipe, weather=None), os.path.join(tmp, "none", "B"))
        w = d4_weather(recipe, fin, ref, rects)
        if "--no-det" not in argv:
            fin2 = finish(recipe, os.path.join(tmp, "w2", "B"))
            d5_same(fin, fin2)
        summary.update(tris=t, w1=med, low16=round(low, 2), lit=lit, xor=xor,
                       weather=w["vanilla"], signset=sg)
        print("[summary] " + json.dumps(summary, default=str))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print(f"PASS test_detail_hut: {len(CHECKS)} check groups D1-D13, {len(WARNS)} WARN "
          f"({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_detail_hut")
        sys.exit(1)
