"""
test_lodges_swanky - L4 (lux-top) and L5 (lux-base) recipe contracts (design/lodges-swanky.md
section 9 / 12 with design/lodges-swanky-skeptic.md applied).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_lodges_swanky.py
        [-- --fast]          skip the cross-process reduced-finish determinism pass

Pass 1 (in-process --blockout builds; the recipe builders assert C1, C2, C4-C8 and the L4
slope / plinth-outline / stair-end contracts themselves, so a wrong build raises here):
  - C3 base mode on LOD0 (L5 engine OK with every ground part present; L4 plinth: zero OK
    edges, min z -12.00, no vertex in z 0 +- 0.01), C11 LOD0 <= cap, C14 name + keyword;
  - C10 culling gate (skeptic MUST-CHANGE 10): a second cull_hidden pass with the strict
    "enclosed" wall rule on a COPY of the shipped LOD0 finds <= 15 % hidden face area;
  - C13 rooms: <= 25 distinct ids and every pane's loops decode to ONE cell
    (int(v*5)*5 + int(u*5), cs2_bldg.win_cell_ids rule) - both triangles of a quad agree;
  - labels: the four clock faces (L5) and the forecourt sign (L4; blank since nitpick C) are single planar parts;
  - L5 --no-tower builds (max z = the flue pots) and clock_tower(z0=-1.5, ground=False)
    (the hosted / tram-station variant) keeps its section-5 contract; rise=4.0 (L5 r1) lifts
    the finial by exactly 4 m.
L5 revision 1 (2026-10-11, verdict B1 "bigger" / Peak 7 / pools), on the joined LOD0:
  - lot 10 x 8, every vertex inside the lot (margins >= 0), the arrival centroid on +X;
  - pools: every `water_` face below the deck top SW.PD by >= 0.05 m, the `deck_pool` top at
    PD, every pool part inside the podium;
  - no coplanar overlap: no two faces of DIFFERENT parts lie in one plane with the same
    facing and overlap (z-fighting) - cull_hidden deletes exact duplicates, this proves none
    survive anywhere on the shipped LOD0;
  - `_Win` has no loose vertices (they would stretch the sub-mesh bounds).
L5 revision 2 (2026-10-11, verdict B1' "Seventh look": plaza, clock down, tower deck), on the
joined LOD0 (authoring frame after the X_SHIFT, i.e. the shipped one):
  - lot 10 x 9; the plaza (`deck_e*`) is end to end (y within 2 m of both lot sides) and reaches
    within 1 m of the road edge, its top at SW.SLAB_T; every rack / bistro / bench / planter /
    bollard part starts at or above the plaza top;
  - the tower deck (`deck_tw`) top at SW.DK_Z and touching the tower base on all four sides;
    the stair treads run from the plaza to the deck top;
  - four racks carry real gear (`skirack_pz*g*` parts), nine bistro tops (`cafetop_`);
  - clock: every dial centre at SW.CT_CLOCK_Z_L5, the dials on the shaft below the stage, and
    from the PREVIEW cameras (cs2_preview framing: bounding sphere, 45 deg, 1300 x 1000,
    margin 1.12) of every arrival-side view, >= 99 % of the samples of each dial that faces
    the camera see it (no part in between); clock_tower() defaults keep the section-5 dial
    on the stage (z CT_CLOCK_Z, plane +-2.3 + 0.08).
L5 revision 3 (2026-10-11, verdict B1'' "Twelfth look": plaza on the pool side, vanilla props,
stair to the right), replacing the revision-2 plaza checks (check_l5_r3, on the joined LOD0 and
the blockout manifest, shipped frame):
  - the slope plaza (`deck_s*`) end to end, within 1 m of the BACK lot edge, top SW.SLAB_T,
    reaching the podium wall; podium steps `stairblk_w` from the plaza up to under the deck;
  - the road side is the valet forecourt: drive `deck_e0` at SW.DRIVE_T to within 1 m of the
    road edge, walks `deck_e1` at SW.SLAB_T, island `deck_e2` at SW.ISLAND_T;
  - no modelled tree / bench / bin / table / chair / umbrella / lamp post / planter / bollard
    part is left; four racks carry gear, and each rack's gear part face counts equal a
    bindings=False kit build and differ from a bindings=True one (no bindings, proven on the
    mesh);
  - the manifest `sub_objects` block = SW.SUB_OBJECTS (85 rows, every GUID a VANILLA one);
    every m_ParentMesh 0 row stands on a mesh face at its height (ray down at the pivot);
  - the deck stair: treads from the walk to the deck top, beyond the deck's +Y edge (the
    right-hand end seen from the road), every stair part >= 1 m short of the drive;
  - the valet corridor past the deck: rays from the curb toward the road (0.2-3.5 m) meet no
    mesh face and no vanilla prop box (rebuilt from the MANIFEST rows) for >= SW.CORRIDOR_MIN;
  - the clock check as revision 2, from the arrival-side views incl. `valet` and `tower_deck`.
L5 revision 4 (2026-10-12, "Eighteenth look": the porte-cochere gable closed, pool-side doors;
check_l5_r4 on the joined LOD0 + manifest): SW.ARRIVAL_VIEWS are the real preview views; G1 again
on the culled mesh (every ray from each arrival-side preview camera and the close walk-around
through the gable triangle meets a front face within SW.GABLE_DEPTH_MAX of the truss face); no
back face first-hit inside the porte-cochere in a 130 x 100 scan from those cameras (totals
printed); D1 / D2 on the three terrace doors (rays through the clear opening meet the door's
leaves / plates / glass backing >= DOOR_REAL_MIN behind the face, never the wall or a frame);
the two gable lights in the manifest; parent-0 rows on their face (assert_mounted: wall mounts
look back at the wall).
L4 revision 1 (check_l4_r1): manifest `sub_objects` = L4_ROWS, every parent-0 row mounted; both
cantilever soffits survive the cull; the deck from the cantilever tip at Z_DECK with its joists
and struts; the dining door real on the mesh; E1 (no gutter / downspout face in the entrance
portal or a door's clear box).
Fine-detail step 3e (2026-10-12, twenty-third look "run the engine pass on them too!";
build/fine-detail-step3e-swanky.md): both recipes are fine_detail.FineRecipes and the shipping (fine)
builds are what passes 1-3 check. check_l5_fine: `legacy` = revision 4's fields; the fine parts =
the --no-fine parts vertex for vertex except the replaced racks / road-face lanterns (gone), the
12-seg finial and flue pots, the round lantern rods (same heights) and the new name board; H1: each
kit rack row places the kit frame (ski_rack_frame 8 slots along X, prefab frame) on the removed
frame vertex for vertex +-1 mm (identity; a 90-degree yaw misses), loadouts V1 / V2 / V3 / V1; each
road-face wall light at its removed lantern's mount point +-0.01 m (lens - 0.22 m), yaw 0, on its
face; the fire pits and hanging lanterns kept; the --no-fine racks still carry no bindings; the
sign set SkiSignSetGrandB: SG1 UVs = the `l5.name` rect, SG2 its board = the --no-signset host's
own label quad +-1 mm, SG3 the face in no host LOD / labels, SG4 one identity row, SG5 in the lot,
SG6 tris; the manifest's kit rows = KIT_ROWS. check_l4_fine: the fine L4 = the --no-fine L4 vertex
for vertex except the 12-seg flue pots; no sign set, no kit rows (none was modelled).
Revision-4 guards (check_r4_guards, each shown to FAIL on the wrong build): L5 with
PC_GABLE_INFILL False raises G1; L4 with PORCH_UNDER_EAVE False raises E1; a door on a probe wall
without cut_recess fails D1 (with it passes); a wall light 0.10 m off its wall fails S1.
verify_vanilla (when %TEMP%\\pltar\\dump.zip exists): every VANILLA entry re-derived from the
dump - the variants naming the GUID, PlaceholderObject on the named prefab, union bounds
(<= 1.5 mm), max tris, WeatherObject snow flags, base-game consumers > 0 - and the four GUIDs
cs2_liftdress.SUBOBJ also holds agree with it.
Pane guards (buildings-review-geometry F1-F3): L5 with the old 1.0 m cross-gable bays must
raise F1; check_pane_clearance must raise on a roof part 0.3 m in front of a pane.
Pass 2 (skeptic MUST-CHANGE 6: Blender runs with hash randomisation on, so one process cannot
see set-order nondeterminism): each recipe's --blockout in TWO separate Blender processes ->
identical manifest mesh hashes (LOD0 pre-bake, _Win) and room cells; unless --fast, L4 reduced
finish (--tex 1024 --ao-samples 4) in two processes -> identical PNG sha1s and LOD hashes.
Pass 3 (exported folders under assets/import/lodges/B, when present): C12 batches per mesh
group <= 16 (L5 9, L4 6 + 3 if the BaseProperties surgery fails), C15 LOD1 / LOD2 bounds vs
LOD0 (FBX round trip), texture size and shipped w = 1 px/m from the manifest (R18 rule).
C15 at LOD2 (revision 1, 2026-10-11): the LOD2 FBX bounds vs the bounds of the LOD0 parts
LOD2 KEEPS (pass 1's blockout minus lodge.delete_lists' LOD2 list, <= 0.25 m), so parts the
recipe deletes on purpose (balconies, rake boards, the clock finial) do not count but a
collapse that eats a kept part (an eave, the tower cap) still fails. The pass-1 LOD0 must
match the exported LOD0 FBX bounds (<= 0.01 m) for that comparison to stand; the raw LOD2
drift vs LOD0 is printed beside it.
"""

import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import traceback

import bmesh
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import bldg_recipes as R      # noqa: E402
import cs2_asset              # noqa: E402
import cs2_bldg as K          # noqa: E402
import cs2_validate           # noqa: E402
import lodge                  # noqa: E402

REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
EXPORT = os.path.join(REPO, "assets", "import", "lodges", "B")
C10_MAX = 0.15
LOD0_BOUNDS = {}        # recipe key -> (lo, hi) of pass 1's LOD0 (export orientation)
LOD2_KEPT = {}          # recipe key -> (lo, hi) of the LOD0 faces LOD2 keeps


def _fbx_frame(lo, hi):
    """Blender-frame bounds -> the frame _fbx_bounds reports (the raw FBX axes: x, z, -y;
    measured 2026-10-11 on the L5 export: Blender y -31.0..31.4 reads z -31.4..31.0)."""
    lo, hi = np.asarray(lo, np.float64), np.asarray(hi, np.float64)
    return np.array([lo[0], lo[2], -hi[1]]), np.array([hi[0], hi[2], -lo[1]])


def lod2_kept_bounds(recipe, obj, table):
    """(lo, hi) of the LOD0 faces whose part is NOT on the recipe's LOD2 delete list."""
    _d1, d2 = lodge.delete_lists(recipe, table)
    pp = np.zeros(len(obj.data.polygons), np.int32)
    obj.data.attributes["part"].data.foreach_get("value", pp)
    W = K._world_coords(obj)
    keep = np.zeros(len(W), bool)
    for p in obj.data.polygons:
        if not table[pp[p.index] - 1].startswith(d2):
            keep[list(p.vertices)] = True
    return W[keep].min(0), W[keep].max(0)


def _recipes():
    rec = R.load_recipes()
    return R.get_recipe("L4", rec), R.get_recipe("L5", rec)


def _t14_pos(pos):
    """T14 x-sign N (2026-10-16): design position -> manifest (game) position, rule T: x -> -x."""
    x, y, z = (round(float(v), 4) for v in pos)
    return (-x + 0.0, y, z)


def _want_rows(recipe):
    """(prefab, m_Object, position, m_ParentMesh) of the recipe's sub_objects (vanilla tuples and
    fine-detail "mod" dicts), in manifest order (lodge.py appends a sign set's identity row after).
    T14 x-sign N (2026-10-16): the recipes design their rows in the old frame; the manifest holds rule T, the game
    frame (x -> -x), so the expected position here is the design position with x negated."""
    out = []
    for e in recipe.sub_objects:
        if isinstance(e, dict):
            out.append((e["prefab"], e["m_Object"], _t14_pos(e["position"]),
                        int(e.get("m_ParentMesh", -1))))
        else:
            pf, g, pos, _q, par = e
            out.append((pf, g, _t14_pos(pos), int(par)))
    return out


def _got_rows(so):
    return [(o["prefab"], o["m_Object"], tuple(o["position"]), o["m_ParentMesh"]) for o in so]


def blockout(recipe, out, extra=()):
    args = lodge.parse_args(["--recipe", recipe.key, "--blockout", "--out", out] + list(extra))
    return lodge.run(recipe, args)


def check_c10(obj, recipe):
    """Residual hidden area on a copy of the shipped LOD0 under the strict wall rule."""
    tmp = obj.copy()
    tmp.data = obj.data.copy()
    tmp.name = obj.name + "C10"
    bpy.context.scene.collection.objects.link(tmp)
    try:
        table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
        rep = K.cull_hidden(tmp, protect=K.LABEL_PREFIXES, table=table, cull_ok=("",),
                            wall_rule="enclosed", ground_prefixes=recipe.ground_prefixes,
                            min_keep=0.0)
    finally:
        me = tmp.data
        bpy.data.objects.remove(tmp, do_unlink=True)
        bpy.data.meshes.remove(me)
    hidden = 1.0 - rep["area_after"] / max(rep["area_before"], 1e-9)
    return hidden, rep


def _loose_verts(obj):
    used = np.zeros(len(obj.data.vertices), bool)
    for p in obj.data.polygons:
        used[list(p.vertices)] = True
    return int((~used).sum())


def check_cells(win):
    lay = win.data.uv_layers.active
    ids = set()
    for p in win.data.polygons:
        cells = set()
        for li in range(p.loop_start, p.loop_start + p.loop_total):
            u, v = lay.data[li].uv
            cells.add(int(min(max(v, 0), 0.999) * 5) * 5 + int(min(max(u, 0), 0.999) * 5))
        assert len(cells) == 1, f"pane {p.index} spans room cells {sorted(cells)}"
        ids |= cells
    assert len(ids) <= 25, f"{len(ids)} room cells"
    return ids


def coplanar_overlaps(obj, table, tol=1e-3):
    """Pairs of DIFFERENT parts with same-facing coplanar overlapping faces (z-fighting) on
    the joined mesh: every face's centre is tested against the faces within `tol`; a
    parallel (n . n' > 0.999) face whose plane holds the centre and whose surface is within
    `tol` of it overlaps. Adjacent coplanar faces (bisected wall strips) only meet at an
    edge, which is farther than `tol` from a face centre."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    layer = bm.faces.layers.int.get("part")
    bvh = BVHTree.FromBMesh(bm)
    bad = set()
    for f in bm.faces:
        if f.calc_area() < 1e-6:
            continue
        c = f.calc_center_median()
        for _loc, _n, idx, _d in bvh.find_nearest_range(c, tol):
            if idx is None or idx == f.index:
                continue
            g = bm.faces[idx]
            if g[layer] == f[layer] or g.normal.dot(f.normal) <= 0.999:
                continue
            if abs((c - g.verts[0].co).dot(g.normal)) < tol:
                a, b = table[f[layer] - 1], table[g[layer] - 1]
                bad.add((min(a, b), max(a, b)))
    bm.free()
    return sorted(bad)


def check_l5_r1(r, obj, table):
    """Revision-1 contracts on the joined LOD0 (lot, facing, pools; see the docstring)."""
    from recipes import l5_swanky_bottom as SW
    recipe = R.get_recipe("L5")
    assert tuple(recipe.lot) == (10, 9), recipe.lot          # revision 2: 10 x 8 -> 10 x 9
    lot = r["lot"]
    assert lot["margin_x"] >= 0.0 and lot["margin_y"] >= 0.0, lot
    assert r["facing"]["arrival_centroid_x"] >= SW.H_X1 + SW.X_SHIFT, r["facing"]
    assert recipe.budget.lod0_cap == 30000 and r["tris"][0] <= 30000, r["tris"]
    pp = np.zeros(len(obj.data.polygons), np.int32)
    obj.data.attributes["part"].data.foreach_get("value", pp)
    W = K._world_coords(obj)
    # the joined LOD0 is already orient_for_net'ed ((x, y) -> (y, -x)); the podium rectangle
    # SW.POOL_* is in the authoring frame: authoring x = -y, authoring y = x; revision 2 builds
    # in the revision-1 frame and shifts by SW.X_SHIFT, so the constants are compared after
    # undoing the shift
    A = np.stack([-W[:, 1] - SW.X_SHIFT, W[:, 0]], axis=1)
    seen = {"water": 0, "deck": 0, "pool": 0}
    for p in obj.data.polygons:
        nm = table[pp[p.index] - 1]
        z = W[list(p.vertices), 2]
        xy = A[list(p.vertices)]
        if nm.startswith("water_"):
            seen["water"] += 1
            assert float(z.max()) <= SW.PD - 0.05, f"{nm}: water z {z.max():.3f} not below the deck {SW.PD}"
        if nm == "deck_pool":
            seen["deck"] += 1
            assert abs(float(z.max()) - SW.PD) < 1e-4 and abs(float(z.min()) - SW.PD) < 1e-4, nm
        # the pool fire pit only: the plaza fire pit `fp_pz` (road side in r2, slope side in r3)
        if nm.startswith(("water_", "poolcope_", "poolwall_", "deck_pool", "lounger_", "fp_ring")):
            seen["pool"] += 1
            eps = 1e-4
            assert (SW.POOL_X0 - eps <= xy[:, 0].min() and xy[:, 0].max() <= SW.POOL_X1 + eps
                    and SW.POOL_Y0 - eps <= xy[:, 1].min()
                    and xy[:, 1].max() <= SW.POOL_Y1 + eps), \
                f"{nm} leaves the pool podium: x {xy[:, 0].min():.2f}..{xy[:, 0].max():.2f}, " \
                f"y {xy[:, 1].min():.2f}..{xy[:, 1].max():.2f}"
    assert seen["water"] and seen["deck"], seen
    print(f"ok L5 r1: lot 10 x 9 (margins x {lot['margin_x']} / y {lot['margin_y']} m), arrival x "
          f"{r['facing']['arrival_centroid_x']}, water {seen['water']} faces below the deck "
          f"{SW.PD} m, {seen['pool']} pool faces inside the podium, no coplanar overlaps")


def _preview_cam(obj, direction):
    """cs2_preview._frame / lift_tower.frame_camera: bounding-sphere fit at the 45 deg lens,
    1300 x 1000, margin 1.12, camera along `direction` from the bounds centre."""
    import math
    W = K._world_coords(obj)
    lo, hi = W.min(0), W.max(0)
    centre, radius = (lo + hi) / 2.0, float(np.linalg.norm(hi - lo)) / 2.0
    fy = math.radians(45.0)
    fx = 2.0 * math.atan(math.tan(fy / 2.0) * 1300 / 1000)
    dist = radius / math.tan(min(fx, fy) / 2.0) * 1.12
    d = np.asarray(direction, np.float64)
    return centre + d / np.linalg.norm(d) * dist


def _yaw_deg(q):
    """Yaw (deg) of a pure game-+Y quaternion (x, y, z, w)."""
    import math
    assert abs(q[0]) < 1e-6 and abs(q[2]) < 1e-6, q
    return math.degrees(2.0 * math.atan2(q[1], q[3]))


def _rack_face_counts(SW):
    """{rack: (faces with bindings=False, faces with bindings=True)} of each plaza rack's gear
    parts, built fresh by the kit (the reference for the no-bindings proof on the mesh)."""
    out = {}
    for nm, x, y, along, gear in SW.PZ_RACKS:
        counts = []
        for b in (False, True):
            lodge.clear_scene()
            ps = K.ski_rack_frame(nm, x, y, slots=8, mat="galv", along=along, skis=gear,
                                  ski_mat="accent", gear=True, z0=SW.SLAB_T, bindings=b)
            counts.append({o.name: len(o.data.polygons) for o, _m in ps
                           if o.name.startswith(f"skirack_{nm}g")})
        out[nm] = tuple(counts)
    lodge.clear_scene()
    return out


def assert_mounted(bvh, SW, row):
    """S1 on the shipped mesh: a m_ParentMesh 0 row stands on (ground) or, revision 4, hangs on
    (VANILLA mount "wall") a mesh face at its pivot, the face turned the right way
    (SW.mount_ray: down from 0.3 m above / back from 0.3 m out along the row's yaw)."""
    o, d, nwant = SW.mount_ray(row)
    loc, nrm, _i, dist = bvh.ray_cast(o, d, 0.6)
    assert loc is not None and abs(dist - 0.3) < 2e-3 and nrm.dot(nwant) > 0.9, \
        f"{row[0]} at {tuple(round(v, 3) for v in row[1:4])} is not on a face ({loc}, {dist})"


def _shipped_frame(obj, table):
    """(A authoring-frame vertices of the joined LOD0 (orient_for_net (x, y) -> (y, -x) undone),
    polygon part names, BVH over A)."""
    from mathutils import Vector
    pp = np.zeros(len(obj.data.polygons), np.int32)
    obj.data.attributes["part"].data.foreach_get("value", pp)
    W = K._world_coords(obj)
    A = np.stack([-W[:, 1], W[:, 0], W[:, 2]], axis=1)
    names = [table[i - 1] for i in pp]
    bvh = BVHTree.FromPolygons([Vector(v) for v in A], [list(p.vertices) for p in obj.data.polygons])
    return A, names, bvh


def door_on_mesh(bvh, names, SW, door, tag_note=""):
    """D1 / D2 on the SHIPPED mesh (the lites are in `_Win`: rays meet the leaves, plates or the
    glass backing): every ray through the clear opening (9 x 7, inset 0.02) from 0.5 m out meets
    a part of the door no nearer than SW.DOOR_REAL_MIN behind the face (kick plates / pulls may
    stand proud) - never the host wall or a fixed frame. Returns the nearest door depth."""
    from mathutils import Vector
    side, plane, s, z0, w, h = (door[k] for k in ("side", "plane", "s", "z0", "w", "h"))
    tag = door["tag"]
    ax = 0 if side[1] == "X" else 1
    sg = 1.0 if side[0] == "+" else -1.0
    n = Vector((0.0, 0.0, 0.0))
    n[ax] = sg
    movable = (f"door_{tag}_leaf", f"door_{tag}_kick", f"door_{tag}_pull", f"glassback_{tag}")
    dmin = 9.0
    for i in range(9):
        a = s - w / 2 + 0.02 + (w - 0.04) * i / 8
        for j in range(7):
            z = z0 + 0.02 + (h - 0.04) * j / 6
            o = Vector((0.0, 0.0, z))
            o[ax], o[1 - ax] = plane + sg * 0.5, a
            loc, _nn, idx, d = bvh.ray_cast(o, -n, 1.2)
            assert loc is not None, f"door {tag}{tag_note}: see-through at {a:.2f}, {z:.2f}"
            nm, depth = names[idx], d - 0.5
            assert nm.startswith(movable), \
                f"door {tag}{tag_note}: {nm} in the clear opening at {a:.2f}, {z:.2f} ({depth:.3f} m)"
            if not nm.startswith((f"door_{tag}_kick", f"door_{tag}_pull")):
                assert depth >= SW.DOOR_REAL_MIN, f"door {tag}{tag_note}: {nm} only {depth:.3f} m deep"
                dmin = min(dmin, depth)
    return round(dmin, 3)


def check_l5_r4(r, obj, table, man):
    """Revision-4 contracts on the SHIPPED L5 LOD0 + manifest (eighteenth look): the preview views
    the builder's G1 cameras use are the real ones; G1 again on the joined, culled mesh (the
    porte-cochere gable closed from every arrival-side preview camera and the close walk-around);
    no back face first-hit anywhere in the porte-cochere from those preview cameras (a coarse
    whole-frame scan: the totals are printed); D1 / D2 on the three terrace doors; the two gable
    lights in the manifest. Returns a dict for the report."""
    import math
    from mathutils import Vector
    from recipes import l5_swanky_bottom as SW
    recipe = R.get_recipe("L5")
    views = dict(list(lodge.VIEWS) + list(recipe.views))
    for vn, d in SW.ARRIVAL_VIEWS:
        assert tuple(views.get(vn, ())) == tuple(d), f"ARRIVAL_VIEWS {vn} {d} != the preview view {views.get(vn)}"
    A, names, bvh = _shipped_frame(obj, table)
    xs = SW.X_SHIFT
    cams = {}
    for vn, _d in SW.ARRIVAL_VIEWS:
        c = _preview_cam(obj, views[vn])
        cams[vn] = Vector((-c[1], c[0], c[2]))
    for rr, a, zc in SW.GABLE_NEAR_CAMS:
        cams[f"near{rr:g}_{a:+g}_{zc:g}"] = Vector((SW.GABLE_FACE_X + xs + rr * math.cos(math.radians(a)),
                                                    rr * math.sin(math.radians(a)), zc))
    xf = SW.GABLE_FACE_X + xs
    rays, bad = 0, []
    for cn, c in sorted(cams.items()):
        for i in range(27):
            y = -5.2 + 0.4 * i
            top = SW.P_TIE + SW.P_Y - abs(y) - 0.17 - 0.06
            z = SW.PC_GAB_EAVE + 0.06
            while z <= top:
                p = Vector((xf, y, z))
                d = (p - c).normalized()
                loc, nn, idx, _dd = bvh.ray_cast(c, d, (p - c).length + 50.0)
                rays += 1
                if loc is None or nn.dot(d) > 0.0 or loc.x < xf - SW.GABLE_DEPTH_MAX:
                    bad.append((cn, round(y, 2), round(z, 2), None if loc is None else names[idx]))
                z += 0.3
    assert not bad, f"G1 on the shipped mesh: {len(bad)} ray(s) see into the gable, e.g. {bad[:5]}"
    # the whole-frame back-face scan (130 x 100 rays per arrival-side preview view)
    back, scan = {}, {}
    for vn, _d in SW.ARRIVAL_VIEWS:
        c = cams[vn]
        lo, hi = A.min(0), A.max(0)
        centre = Vector(((lo + hi) / 2.0).tolist())
        fwd = (centre - c).normalized()
        right = fwd.cross(Vector((0.0, 0.0, 1.0))).normalized()
        up = right.cross(fwd)
        th = math.tan(math.radians(22.5))
        tw = th * 1.3
        hits, nb, pc = 0, 0, 0
        for j in range(100):
            for i in range(130):
                u = (2 * (i + 0.5) / 130 - 1) * tw
                v = (1 - 2 * (j + 0.5) / 100) * th
                dv = (fwd + right * u + up * v).normalized()
                loc, nn, idx, _dd = bvh.ray_cast(c, dv, 5000.0)
                if loc is None:
                    continue
                hits += 1
                if nn.dot(dv) > 0.0:
                    nb += 1
                    if SW.P_X0 + xs - 0.5 <= loc.x <= SW.P_X1 + xs + 0.5 and abs(loc.y) <= SW.P_Y + 1.0 \
                            and 4.0 <= loc.z <= 12.0:
                        pc += 1
                        back.setdefault(names[idx], 0)
                        back[names[idx]] += 1
        scan[vn] = (hits, nb, pc)
    assert not back, f"back faces seen in the porte-cochere from the preview cameras: {back}"
    doors = {}
    for tag, side, plane, s in SW.POOL_DOORS:
        ax = 0 if side[1] == "X" else 1
        d = dict(tag=tag, side=side, plane=plane + (xs if ax == 0 else 0.0), s=s + (xs if ax == 1 else 0.0),
                 z0=SW.SLAB_T, w=SW.DOOR_W, h=SW.DOOR_H)
        doors[tag] = door_on_mesh(bvh, names, SW, d)
    lights = [o for o in (man.get("sub_objects") or {}).get("objects", [])
              if o["prefab"] == "WallLightShortRangeRandom01"]
    assert len(lights) == 2 + (len(SW.FINE_ROWS) if recipe.fine_detail else 0), lights
    print(f"ok L5 r4: G1 on the shipped mesh {rays} rays from {len(cams)} cameras, the gable closed; "
          f"back-face scan (hits, back faces, in the porte-cochere) {scan}; terrace doors real, clear "
          f"{SW.DOOR_W} x {SW.DOOR_H}, nearest door depth {doors}; wall lights {len(lights)} (2 on the gable "
          f"boards{', 4 on the road face (fine)' if recipe.fine_detail else ''})")
    return dict(rays=rays, scan=scan, doors=doors)


def check_l4_r1(r, obj, table, man):
    """Revision-1 contracts on the SHIPPED L4 LOD0 + manifest (eighteenth look): the manifest
    `sub_objects` = the recipe's L4_ROWS (every GUID a VANILLA one), every parent-0 row on its
    face (wall lights on the wall); both cantilever soffits survived the cull (down-facing area
    under the deck's cantilever and the dining room's); the deck one level from the tip to the
    walkway with struts and joists; the dining door real (D1 / D2) on the mesh; no gutter /
    downspout face inside the entrance portal or a door's clear box (E1). Returns a dict."""
    from recipes import l5_swanky_bottom as SW
    import importlib
    L4 = R.get_recipe("L4")
    g4 = L4.builder.__globals__
    A, names, bvh = _shipped_frame(obj, table)
    so = (man.get("sub_objects") or {}).get("objects") or []
    want = g4["_sub_objects"]()
    assert so and [(o["prefab"], o["m_Object"], tuple(o["position"]), o["m_ParentMesh"]) for o in so] == \
        [(p, gd, _t14_pos(pos), par) for p, gd, pos, _q, par in want], "L4 manifest sub_objects != L4_ROWS"
    for o in so:
        gx, gy, gz = o["position"]
        if o["m_ParentMesh"] == 0:
            # T14 x-sign N (2026-10-16): the manifest row is in the game frame (rule T); assert_mounted takes the
            # design frame, so un-mirror: design x = -gx, yaw = -yaw
            assert_mounted(bvh, SW, (o["prefab"], gz, -gx + 0.0, gy, -_yaw_deg(o["rotation"])))
    # soffits: the down-facing area that survived, per part
    down = {}
    for p in obj.data.polygons:
        nm = names[p.index]
        if nm.startswith("soffit_cant"):
            V = A[list(p.vertices)]
            e1, e2 = V[1] - V[0], V[2] - V[0]
            nz = float(np.cross(e1, e2)[2])
            if nz < 0.0:
                down[nm] = down.get(nm, 0.0) + p.area
    cf, tx0 = g4["CANT_FACE"], g4["T_X0"]
    want_t = (cf - tx0 - 0.02) * (g4["T_Y1"] - g4["T_Y0"] - 0.04)
    want_d = (g4["D_X0"] * -1 + (-9.0 - g4["W_T"] / 2)) * (g4["D_Y1"] - g4["D_Y0"])
    assert down.get("soffit_cantt", 0.0) >= 0.99 * want_t and down.get("soffit_cantd", 0.0) >= 0.99 * want_d, \
        f"cantilever soffits culled: {down} (want {want_t:.1f} / {want_d:.1f} m2)"
    deck = A[[v for p in obj.data.polygons if names[p.index] == "deck_t" for v in p.vertices]]
    assert abs(deck[:, 0].min() - tx0) < 1e-3 and abs(deck[:, 2].max() - g4["Z_DECK"]) < 1e-3, \
        (deck.min(0), deck.max(0))
    for k in range(len(g4["CANT_LINES"])):
        assert f"strut_t{k}" in table and f"beam_tj{k}" in table, k
    door = dict(tag="dd", side="+Y", plane=g4["D_Y1"], s=g4["DD_X"], z0=g4["Z_DECK"], w=SW.DOOR_W,
                h=SW.DOOR_H, set_back=SW.DOOR_SET)
    dd = door_on_mesh(bvh, names, SW, door)
    # E1 on the shipped mesh: no gutter / downspout vertex or face inside the clear boxes
    boxes = g4["entrance_boxes"](door)
    hit = []
    for label, (lo, hi) in boxes:
        lo, hi = np.array(lo), np.array(hi)
        for p in obj.data.polygons:
            nm = names[p.index]
            if not nm.startswith(("gutter_", "downspout_")):
                continue
            V = A[list(p.vertices)]
            if (np.minimum(V.max(0), hi) - np.maximum(V.min(0), lo) > 1e-4).all():
                hit.append((label, nm))
                break
    assert not hit, f"E1 on the shipped mesh: {hit}"
    print(f"ok L4 r1: {len(so)} vanilla rows = L4_ROWS, parent-0 rows on their faces (2 on the wall); "
          f"cantilever soffits kept after the cull {({k: round(v, 1) for k, v in down.items()})} m2; deck "
          f"x {deck[:, 0].min():.2f}..{deck[:, 0].max():.2f} at {g4['Z_DECK']}; dining door real, nearest "
          f"depth {dd}; entrance portal + door clear boxes free of gutters / downspouts ({len(boxes)} boxes)")
    return dict(rows=len(so), soffit=down, door=dd)


def check_r4_guards():
    """Each revision-4 / L4 revision-1 contract shown to FAIL on the wrong build:
    - G1: L5 with PC_GABLE_INFILL False (the revision-3 open gable) raises G1;
    - E1: L4 with PORCH_UNDER_EAVE False (the revision-0 porch through the main eave) raises E1;
    - D1: a terrace door on a probe wall WITHOUT the cut (a plate on a solid wall) fails
      check_door, the same door with cut_recess passes;
    - S1 wall mount: a WallLightShortRangeRandom01 row 0.10 m off its wall fails, on it passes."""
    from cs2_bldg import WallRef  # noqa: F401
    L4, L5 = _recipes()
    out = {}
    for rec, flag, want in ((L5, "PC_GABLE_INFILL", "G1"), (L4, "PORCH_UNDER_EAVE", "E1")):
        g = rec.builder.__globals__
        old = g[flag]
        g[flag] = False
        msg = ""
        try:
            lodge.clear_scene()
            lodge.build_parts(rec, {"tower": True})
        except RuntimeError as e:
            msg = str(e)
        finally:
            g[flag] = old
            lodge.clear_scene()
        assert want in msg, f"{rec.key} with {flag} False did not fail {want}: {msg[:200]!r}"
        out[want] = msg[:80]
    from recipes import l5_swanky_bottom as SW
    SW.register_materials()
    for cut in (False, True):
        lodge.clear_scene()
        parts = K.box_part("wall_probe", (-2.0, -4.0, 0.0), (0.0, 4.0, 3.5), "stone_rubble")
        parts += K.box_part("deck_probe", (0.0, -4.0, 0.05), (3.0, 4.0, 0.14), "concrete", drop=("-Z",))
        ps, door = SW.terrace_door("pr", "+X", 0.0, 0.0, 0.14, "probe")
        parts += ps
        if cut:
            SW.cut_recess(parts[0][0], "+X", 0.0, *door["rough_a"], *door["rough_z"], SW.DOOR_SET)
        failed = ""
        try:
            SW.check_door(parts, door, "wall_probe")
        except RuntimeError as e:
            failed = str(e)
        assert bool(failed) == (not cut) and (cut or "D1" in failed), \
            f"door probe cut={cut}: failed={failed[:160]!r}"
        if not cut:
            out["D1"] = failed[:80]
        else:
            for off, want_fail in ((0.10, True), (0.0, False)):
                row = ("WallLightShortRangeRandom01", 0.0 + off, 2.6, 1.8, 0.0, 0, "probe")
                msg = ""
                try:
                    SW.check_sub_objects(parts, rows=(row,), lot=(-10.0, 10.0, -10.0, 10.0))
                except RuntimeError as e:
                    msg = str(e)
                assert bool(msg) == want_fail and (not want_fail or "S1" in msg), \
                    f"wall light {off} m off the wall: {msg[:160]!r}"
            out["S1 wall"] = "0.10 m off fails, on the wall passes"
    lodge.clear_scene()
    print(f"ok r4 guards: {out}")
    return out


def _parts_of(recipe):
    """{part: world vertices (shipped authoring frame)}, {part: tris} of a recipe's build_parts."""
    from recipes import l5_swanky_bottom as SW
    lodge.clear_scene()
    ps = lodge.build_parts(recipe, {"tower": True, "roof_windows": False})
    V = {o.name: SW.world_coords(o) for o, _k in ps}
    T = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o, _k in ps}
    F = {o.name: len(o.data.polygons) for o, _k in ps}
    lodge.clear_scene()
    return V, T, F


def _faces_game(obj, names):
    """{part name: (4, 3) game coords} of single-quad parts in a joined, net-oriented mesh."""
    import cs2_signset as SS
    me = obj.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    pv = np.zeros(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", pv)
    G = SS.game_coords(obj)
    return {table[pv[p.index] - 1]: G[list(p.vertices)] for p in me.polygons if table[pv[p.index] - 1] in names}


def check_l5_fine(tmp, r, racks):
    """Fine-detail step 3e on L5 (module docstring). `r` = pass 1's fine blockout (its sign set and
    joined LOD0), `racks` = the kit's no-bindings gear face counts. Returns a dict for the report."""
    import fine_detail as FD
    import cs2_signset as SS
    from mathutils import Quaternion
    from recipes import l5_swanky_bottom as SW
    rec = R.get_recipe("L5")
    assert isinstance(rec, FD.FineRecipe) and rec.fine_detail and rec.sign_set == SW.SIGN_SET, type(rec)
    leg = FD.effective(rec, fine=False)
    assert leg.sub_objects == SW.SUB_OBJECTS and leg.labels == SW.clock_tower_labels("ct") \
        and leg.notes == SW.NOTES_R4 and leg.sign_set is None and not leg.fine_detail
    # the sign set (pass 1's fine blockout, whose joined host build_parts below clears) + SG1-SG6
    spec = rec.sign_set
    ss = r["sign_set"]
    sobj = ss["obj"]
    bad = SS.sg1_uv_in_rect(sobj, spec)
    assert not bad, f"SG1 {bad}"
    st = json.loads(sobj.data[cs2_asset.PART_TABLE_PROP])
    pv = np.zeros(len(sobj.data.polygons), np.int32)
    sobj.data.attributes["part"].data.foreach_get("value", pv)
    G = SS.game_coords(sobj)
    set_faces = {st[pv[p.index] - 1]: G[list(p.vertices)] for p in sobj.data.polygons}
    host = bpy.data.objects[r["name"]]
    table = json.loads(host.data[cs2_asset.PART_TABLE_PROP])
    (t1, t2), _plan = lodge.plan_lod_targets(host, table, rec)
    d1, d2 = lodge.delete_lists(rec, table)
    lods, _inf = K.lod_chain(host, "SGl5", (t1, t2), (d1, d2), rec.ground_prefixes)
    tables = {"LOD0": table}
    for i, lod in enumerate(lods, start=1):
        pv2 = np.zeros(len(lod.data.polygons), np.int32)
        lod.data.attributes["part"].data.foreach_get("value", pv2)
        tables[f"LOD{i}b"] = [table[v - 1] for v in sorted(set(pv2.tolist()))]
    for lod in lods:
        m2 = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(m2)
    man = json.load(open(os.path.join(r["asset_dir"], f"{r['name']}.manifest.json"), encoding="utf-8"))
    assert not SS.sg3_no_routed_left(tables, man.get("labels") or {}, spec), "SG3"
    assert not SS.sg4_identity_row(man["sub_objects"], spec, ss["cid"]), "SG4"
    bounds = SS.bounds_game(sobj)
    lw_, ld_ = rec.lot
    assert not SS.sg5_in_lot(bounds, (4.0 * lw_, 4.0 * ld_)), ("SG5", bounds)
    nsg = cs2_asset.tri_count(sobj)
    assert not SS.sg6_tris(nsg) and nsg == 2, ("SG6", nsg)
    # SG2 against the --no-signset host (the face stays a label quad there)
    lodge.clear_scene()
    r0 = blockout(rec, os.path.join(tmp, "p1", "L5ns", "B"), ["--no-signset"])
    assert r0.get("sign_set") is None
    old = _faces_game(bpy.data.objects[r0["name"]], set(spec.face_names()))
    bad = SS.sg2_positions(set_faces, old)
    assert len(old) == 1 and not bad, f"SG2 {bad} {sorted(old)}"
    moved_ = {k: v.copy() for k, v in set_faces.items()}
    moved_[spec.face_names()[0]][0, 0] += 0.002
    assert SS.sg2_positions(moved_, old), "SG2 negative: a corner moved 2 mm is not reported"
    lodge.clear_scene()
    lv, lt, lf = _parts_of(leg)
    fv, ft, _ff = _parts_of(rec)
    removed = sorted(n for n in lv if n.startswith(("skirack_pz", "lamp_pm")))
    added = sorted(set(fv) - set(lv))
    assert len(removed) == 4 * 7 + 8 and not [n for n in removed if n in fv], removed
    assert added == ["sign_l5name_back", "sign_l5name_face", "sign_l5name_frame"], added
    moved = {f"flue_m{k}{j}": (22, 34) for k in range(2) for j in range(2)}
    moved.update({"tower_ctfin": (20, 44), "lamp_pc0rod": (12, 16), "lamp_pc1rod": (12, 16)})
    assert set(lv) - set(removed) == set(fv) - set(added), "part sets differ beyond the pass"
    for n, (t_old, t_new) in moved.items():
        assert (lt[n], ft[n]) == (t_old, t_new), (n, lt[n], ft[n])
        assert abs(lv[n][:, 2].min() - fv[n][:, 2].min()) < 1e-5 and abs(lv[n][:, 2].max() - fv[n][:, 2].max()) < 1e-5, n
        c0 = 0.5 * (lv[n].min(0) + lv[n].max(0))
        c1 = 0.5 * (fv[n].min(0) + fv[n].max(0))
        assert np.abs(c0[:2] - c1[:2]).max() < 1e-4, (n, c0, c1)
    same = [n for n in fv if n in lv and n not in moved]
    diff = [n for n in same if not np.array_equal(fv[n], lv[n])]
    assert not diff, f"fine L5 parts moved beyond the pass: {diff[:6]}"
    flo = np.min([v.min(0) for v in fv.values()], axis=0)
    fhi = np.max([v.max(0) for v in fv.values()], axis=0)
    llo = np.min([v.min(0) for v in lv.values()], axis=0)
    lhi = np.max([v.max(0) for v in lv.values()], axis=0)
    assert np.abs(flo - llo).max() <= 0.005 and np.abs(fhi - lhi).max() <= 0.005, (flo, llo, fhi, lhi)
    # the --no-fine racks: gear without bindings (revision 3's proof, on the legacy parts)
    for nm, (no_b, with_b) in sorted(racks.items()):
        got = {k: lf.get(k, 0) for k in no_b}
        assert got == no_b and no_b != with_b, f"legacy rack {nm}: gear faces {got} != the no-bindings build {no_b}"
    # H1 kit racks: the kit frame (ski_rack_frame 8 slots along X at the origin, prefab (x, z - lift, -y))
    kitf = K.ski_rack_frame("l5kit", 0.0, 0.0, slots=8, along="X")[0][0]
    KV = np.array([tuple(kitf.matrix_world @ v.co) for v in kitf.data.vertices], np.float64)
    m_ = kitf.data
    bpy.data.objects.remove(kitf, do_unlink=True)
    bpy.data.meshes.remove(m_)
    KP = np.stack([KV[:, 0], KV[:, 2] - K.GROUND_LIFT, -KV[:, 1]], axis=1)
    kit = [e for e in rec.sub_objects if isinstance(e, dict) and e.get("kind") == "mod"]
    assert [e["prefab"] for e in kit] == [p_ for _n, p_ in SW.RACK_KIT] == \
        ["SkiDressSkiRack", "SkiDressSkiRackV2", "SkiDressSkiRackV3", "SkiDressSkiRack"], [e["prefab"] for e in kit]
    worst = 0.0
    for (nm, *_r), e in zip(SW.PZ_RACKS, kit):
        assert e["m_ParentMesh"] == 0 and FD.row_colour_error(e) is None \
            and tuple(e["rotation"]) == (0.0, 0.0, 0.0, 1.0), e
        x_, y_, z_, w_ = e["rotation"]
        G = KP @ np.array(Quaternion((w_, x_, y_, z_)).to_matrix(), np.float64).T + np.array(e["position"])
        old = lv[f"skirack_{nm}"]                            # shipped authoring -> game (y, z, x)
        OG = np.stack([old[:, 1], old[:, 2], old[:, 0]], axis=1)
        d = max(float(np.min(np.linalg.norm(G - q, axis=1))) for q in OG)
        assert d <= 0.001, f"H1: kit row {e['prefab']} {d * 1000:.2f} mm from skirack_{nm}"
        worst = max(worst, d)
        Gw = KP @ np.array(Quaternion((0.7071068, 0.0, 0.7071068, 0.0)).to_matrix(), np.float64).T + \
            np.array(e["position"])
        assert max(float(np.min(np.linalg.norm(Gw - q, axis=1))) for q in OG) > 0.05, "H1 negative: yaw 90 matched"
    # H1 road-face wall lights: at the removed lantern's mount point (lens centre - 0.22 m along +X)
    lights = [e for e in rec.sub_objects if not isinstance(e, dict) and e[2] in
              [SW.to_game(*row[1:4]) for row in SW.FINE_ROWS]]
    assert len(lights) == 4 and all(e[0] == "WallLightShortRangeRandom01" for e in lights), lights
    lw = 0.0
    for k, e in enumerate(lights):
        P_ = lv[f"lamp_pm{k}"]
        c = 0.5 * (P_.min(0) + P_.max(0)) - np.array((0.22, 0.0, 0.0))
        got = np.array((e[2][2], e[2][0], e[2][1]))          # game -> shipped authoring (gz, gx, gy)
        lw = max(lw, float(np.abs(got - c).max()))
        assert np.abs(got - c).max() <= 0.01 and tuple(e[3]) == (0.0, 0.0, 0.0, 1.0) and e[4] == 0, \
            f"H1 road-face light {k}: {got} vs the lantern's mount {c}"
    # placeholders kept vertex for vertex
    for n in ("lamp_pc0", "lamp_pc1", "lamp_pc0arm", "lamp_pc1arm", "fp_ring", "fp_pz", "lamp_ember", "lamp_pzember"):
        assert n in fv and np.array_equal(fv[n], lv[n]), n
    print(f"ok L5 fine (step 3e): legacy fields = revision 4; parts = the --no-fine parts vertex for vertex but "
          f"{len(removed)} removed (racks + road-face lanterns), {len(moved)} finer (finial 20 -> 44, 4 flue pots "
          f"22 -> 34, 2 rods 12 -> 16 tris, same heights), {len(added)} added (name board); part bounds +-5 mm; H1 kit "
          f"racks = the removed frames (worst {worst * 1000:.2f} mm, identity; yaw 90 misses), loadouts "
          f"{[e['prefab'] for e in kit]}; 4 road-face wall lights at the lanterns' mounts (worst {lw:.4f} m); fire "
          f"pits + hanging lanterns kept; sign set {spec.mesh}: SG1-SG6 ({nsg} tris, bounds {bounds}, CID "
          f"{ss['cid'][:8]}), SG2 vs the --no-signset label quad")
    return dict(removed=len(removed), worst_mm=round(worst * 1000, 3), lights_m=round(lw, 4), sign_tris=nsg)


def check_l4_fine():
    """Fine-detail step 3e on L4: a FineRecipe with no sign set and no kit rows; `legacy` = revision 1's
    notes; the fine parts = the --no-fine parts vertex for vertex except the 4 flue pots (8 -> 12-seg,
    22 -> 34 tris, same heights / centres)."""
    import fine_detail as FD
    rec = R.get_recipe("L4")
    assert isinstance(rec, FD.FineRecipe) and rec.fine_detail and rec.sign_set is None, type(rec)
    assert not [e for e in rec.sub_objects if isinstance(e, dict)], "L4 kit rows"
    leg = FD.effective(rec, fine=False)
    g4 = rec.builder.__globals__
    assert leg.notes == g4["NOTES_R1"] and leg.sub_objects == rec.sub_objects
    # nitpick C (2026-10-13; Richard: "Same issue with the sign outside as the chalet"): the forecourt board is a
    # BLANK board (fine_detail.blank_board: sign cream + a graphite trim rim); --no-fine keeps the fork-and-knife
    assert tuple(leg.labels) == tuple(g4["PICTO_LABELS"]) and not any(FD.is_blank_label(e) for e in leg.labels)
    assert len(rec.labels) == 1 and rec.labels[0][0] == "sign_fc_face", rec.labels
    assert FD.is_blank_label(rec.labels[0]), rec.labels
    lv, lt, lf = _parts_of(leg)
    fv, ft, _ff = _parts_of(rec)
    assert set(lv) == set(fv), sorted(set(lv) ^ set(fv))
    flues = [f"flue_u{k}" for k in range(4)]
    for n in flues:
        assert (lt[n], ft[n]) == (22, 34), (n, lt[n], ft[n])
        assert abs(lv[n][:, 2].min() - fv[n][:, 2].min()) < 1e-5 and abs(lv[n][:, 2].max() - fv[n][:, 2].max()) < 1e-5
    diff = [n for n in fv if n not in flues and not np.array_equal(fv[n], lv[n])]
    assert not diff, f"fine L4 parts moved beyond the flue pots: {diff[:6]}"
    print(f"ok L4 fine (step 3e + nitpick C): FineRecipe, no sign set, the forecourt board BLANK (--no-fine: the "
          f"pictogram), no kit rows (no modelled rack), "
          f"lanterns vanilla since revision 1; parts = the --no-fine parts vertex for vertex but the 4 flue pots "
          f"12-seg (22 -> 34 tris, same heights)")
    return dict(flues=len(flues))


def check_l5_r3(r, obj, table, man, racks):
    """Revision-3 contracts on the joined LOD0 + the blockout manifest (see the docstring)."""
    from mathutils import Vector
    from recipes import l5_swanky_bottom as SW
    recipe = R.get_recipe("L5")
    pp = np.zeros(len(obj.data.polygons), np.int32)
    obj.data.attributes["part"].data.foreach_get("value", pp)
    W = K._world_coords(obj)
    A = np.stack([-W[:, 1], W[:, 0], W[:, 2]], axis=1)       # authoring frame (shipped, shifted)
    names = [table[i - 1] for i in pp]
    by = {}
    for p in obj.data.polygons:
        by.setdefault(names[p.index], []).extend(p.vertices)

    def pts(pre):
        idx = sorted({v for nm, vs in by.items() if nm.startswith(pre) for v in vs})
        assert idx, f"no faces of {pre}"
        return A[idx]
    xs = SW.X_SHIFT
    # (1) the plaza on the slope side, joined to the pool terrace
    ps = pts("deck_s")
    assert abs(ps[:, 2].max() - SW.SLAB_T) < 1e-4, ps[:, 2].max()
    assert ps[:, 1].min() <= -(SW.HY - 2.0) and ps[:, 1].max() >= SW.HY - 2.0, \
        f"slope plaza not end to end: y {ps[:, 1].min():.2f}..{ps[:, 1].max():.2f}"
    assert ps[:, 0].min() <= -SW.HX + 1.0, \
        f"slope plaza ends {ps[:, 0].min() + SW.HX:.2f} m short of the back lot edge"
    assert abs(ps[:, 0].max() - (SW.POOL_X0 + xs)) < 1e-3, f"plaza reach {ps[:, 0].max():.2f}"
    st = pts("stairblk_w")
    assert abs(st[:, 0].max() - (SW.POOL_X0 + xs)) < 1e-3 and st[:, 2].max() < SW.PD, \
        (st.min(0), st.max(0))
    # (2) the valet forecourt
    dr, wk, il = pts("deck_e0"), pts("deck_e1"), pts("deck_e2")
    assert abs(dr[:, 2].max() - SW.DRIVE_T) < 1e-4 and abs(wk[:, 2].max() - SW.SLAB_T) < 1e-4 \
        and abs(il[:, 2].max() - SW.ISLAND_T) < 1e-4, (dr[:, 2].max(), wk[:, 2].max(), il[:, 2].max())
    assert dr[:, 0].max() >= SW.HX - 1.0, f"drive ends {SW.HX - dr[:, 0].max():.2f} m short of the road"
    # (3) the vanilla rule: no modelled stand-in left; racks with gear and NO bindings (fine-detail
    # step 3e: the racks are dressing-kit rows, none is modelled; the --no-fine racks' gear is proven
    # bindings-free in check_l5_fine)
    gone = ("cafe", "bench_", "bin_", "planter_", "shrub_", "conifer_", "bollard_", "lounger_",
            "umbstand_", "umbpole_", "umbcanopy_", "post_pl", "post_fc", "post_pz", "post_tw")
    left = sorted({nm for nm in by if nm.startswith(gone)})
    assert not left, f"modelled dressing left: {left[:8]}"
    fine = bool(getattr(recipe, "fine_detail", False))
    if fine:
        left = sorted({nm for nm in by if nm.startswith(("skirack_", "lamp_pm"))})
        assert not left, f"fine L5: modelled racks / road-face lanterns left: {left[:6]}"
        rk = [r_["prefab"] for r_ in SW.KIT_ROWS]
    else:
        rz = pts("skirack_pz")
        assert rz[:, 2].min() >= SW.SLAB_T - 1e-3, f"a rack starts at z {rz[:, 2].min():.3f}"
        gear = sorted({nm for nm in by if nm.startswith("skirack_pz") and
                       nm.rstrip("AWKBRG").endswith("g")})
        rk = sorted({nm[len("skirack_"):].split("g")[0] for nm in gear})
        assert len(rk) == 4, f"racks with real gear: {rk}"
        faces = {}
        for nm in names:
            faces[nm] = faces.get(nm, 0) + 1
        for nm, (no_b, with_b) in sorted(racks.items()):
            got = {k: faces.get(k, 0) for k in no_b}
            assert got == no_b and no_b != with_b, \
                f"rack {nm}: gear faces {got} != the kit's no-bindings build {no_b} (bindings {with_b})"
    # (4) the manifest's rows = the recipe's (vanilla SUB_OBJECTS [+ FINE_SUB_OBJECTS] [+ KIT_ROWS]) [+ the
    # sign set's identity row]; every vanilla GUID a VANILLA one; parent-0 vanilla rows stand on a mesh face
    so = man.get("sub_objects") or {}
    allrows = so.get("objects") or []
    want = _want_rows(recipe)
    assert so.get("target_prefab") == recipe.prefab and _got_rows(allrows)[:len(want)] == want, \
        (so.get("target_prefab"), len(allrows), len(want))
    extra = allrows[len(want):]
    assert [(o.get("role"), o["prefab"]) for o in extra] == ([("signset", recipe.sign_set.prop)]
                                                            if recipe.sign_set else []), extra
    rows = [o for o in allrows if o.get("kind") != "mod"]
    guids = {v["guid"]: k for k, v in SW.VANILLA.items()}
    for row in rows:
        assert row["m_Object"] in guids and guids[row["m_Object"]] == row["prefab"], row
    verts = [Vector(v) for v in A]
    bvh = BVHTree.FromPolygons(verts, [list(p.vertices) for p in obj.data.polygons])
    down = Vector((0.0, 0.0, -1.0))
    sboxes, zones = [], {}
    for row in rows:
        gx, gy, gz = row["position"]
        # T14 x-sign N (2026-10-16): manifest rows are rule T (game frame); back to the design frame the box/mount
        # tests use: design x = -gx, yaw = -yaw
        X, Y, Z = gz, -gx + 0.0, gy                        # game local -> authoring (shipped)
        yaw = -_yaw_deg(row["rotation"])
        if row["m_ParentMesh"] == 0:                       # revision 4: wall mounts look back
            assert_mounted(bvh, SW, (row["prefab"], X, Y, Z, yaw))
        for kind, (lo, hi) in SW.vanilla_boxes((row["prefab"], X, Y, Z, yaw)):
            sboxes.append((lo, hi))
        zones[row["prefab"]] = zones.get(row["prefab"], 0) + 1
    # (5) the deck stair at the right-hand end, clear of the drive; the valet corridor
    dk = pts("deck_tw")
    assert abs(dk[:, 2].max() - SW.DK_Z) < 1e-4, dk[:, 2].max()
    hb = SW.CT_BASE_W / 2
    cx, cy = SW.CT_C[0] + xs, SW.CT_C[1]
    for ax, val in ((0, cx - hb), (0, cx + hb), (1, cy - hb), (1, cy + hb)):
        on = np.abs(dk[:, ax] - val) < 0.01
        other = 1 - ax
        mid = cy if other else cx
        inside = (dk[:, other] > mid - hb - 0.01) & (dk[:, other] < mid + hb + 0.01)
        assert (on & inside).any(), f"the deck does not reach the tower base side {ax} = {val}"
    tr = pts("stair_tw_treads")
    assert abs(tr[:, 2].max() - SW.DK_Z) < 1e-4 and tr[:, 2].min() >= SW.SLAB_T - 1e-4, \
        (tr[:, 2].min(), tr[:, 2].max())
    assert tr[:, 1].min() >= SW.DK_Y1 - 1e-3 and tr[:, 1].max() > SW.DK_Y1 + 5.0, \
        f"stair treads y {tr[:, 1].min():.2f}..{tr[:, 1].max():.2f}: not off the +Y (right) edge"
    sall = np.concatenate([pts(p) for p in ("stair_tw", "post_stairtw", "rail_stairtw")])
    assert sall[:, 0].max() <= SW.LANE_X0 + xs - 1.0, f"stair reaches x {sall[:, 0].max():.2f}"
    tris = [list(p.vertices) for p in obj.data.polygons]
    for lo, hi in sboxes:
        (x0, y0, z0), (x1, y1, z1) = lo, hi
        base = len(verts)
        verts += [Vector(v) for v in ((x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
                                      (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1))]
        tris += [[base + a, base + b, base + c] for a, b, c in
                 ((0, 2, 1), (0, 3, 2), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6),
                  (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7))]
    bvh2 = BVHTree.FromPolygons(verts, tris)
    x0c, x1c = SW.LANE_X0 + xs, SW.RD_X1 + xs
    worst = (1e9, None, None)
    for y in np.arange(SW.DK_Y0, SW.DK_STAIR_FOOT_Y + SW.DK_STAIR_LAND + 1e-9, 0.25):
        for zz in (0.2, 1.0, 2.0, 3.0, 3.5):
            loc, _n, _i, d = bvh2.ray_cast(Vector((x0c + 0.01, float(y), zz)),
                                           Vector((1.0, 0.0, 0.0)), x1c - x0c - 0.01)
            w = (d + 0.01) if loc is not None else (x1c - x0c)
            if w < worst[0]:
                worst = (round(float(w), 3), round(float(y), 2), zz)
    assert worst[0] >= SW.CORRIDOR_MIN, f"valet corridor past the deck {worst}"
    # (6) the clock: dial centres, on the shaft below the stage, sightline from the previews
    faces_ct = {}
    for p in obj.data.polygons:
        nm = names[p.index]
        if nm.startswith("clock_ct") and nm.endswith("_face"):
            faces_ct.setdefault(nm, []).append(p)
    assert len(faces_ct) == 4, sorted(faces_ct)
    stage = SW.CT_SHAFT_TOP + SW.CT_RISE
    wverts = [Vector(v) for v in W]
    bvh3 = BVHTree.FromPolygons(wverts, [list(p.vertices) for p in obj.data.polygons])
    samples = {}
    for nm, ps_ in sorted(faces_ct.items()):
        V = np.concatenate([W[list(p.vertices)] for p in ps_])
        c = V.mean(0)
        assert abs(c[2] - SW.CT_CLOCK_Z_L5) < 0.01, f"{nm} centre z {c[2]:.3f}"
        assert V[:, 2].max() <= stage - 0.3 + 1e-3, f"{nm} top {V[:, 2].max():.2f} below the stage?"
        n = sum((Vector(p.normal) * p.area for p in ps_), Vector()).normalized()
        sp = [Vector(c)]
        for p in ps_:
            q = W[list(p.vertices)]
            sp.append(Vector(q.mean(0)))
            far = sorted(q, key=lambda v: float(np.linalg.norm(v - c)))[-2:]
            sp.append(Vector((far[0] + far[1]) * 0.5 * 0.92 + c * 0.08))
        samples[nm] = (sp, n, Vector(c))
    views = dict(list(lodge.VIEWS) + list(recipe.views))
    report = {}
    for vn in ("arrival_front", "arrival_3q", "arrival", "road34left", "valet", "tower_deck"):
        cam = Vector(_preview_cam(obj, views[vn]))
        for nm, (sp, n, c) in samples.items():
            if n.dot((cam - c).normalized()) <= 0.05:
                continue
            ok = 0
            for q in sp:
                o = q + n * 0.005
                d = cam - o
                loc, _nn, _i, _dd = bvh3.ray_cast(o, d.normalized(), d.length)
                ok += loc is None
            frac = ok / len(sp)
            report[f"{vn}/{nm[len('clock_'):-len('_face')]}"] = round(frac, 3)
            assert frac >= 0.99, f"clock {nm} only {frac:.0%} visible from the {vn} preview camera"
    print(f"ok L5 r3: slope plaza x {ps[:, 0].min():.2f}..{ps[:, 0].max():.2f} y "
          f"{ps[:, 1].min():.1f}..{ps[:, 1].max():.1f} at {SW.SLAB_T}; drive to x "
          f"{dr[:, 0].max():.2f} at {SW.DRIVE_T}, walks {SW.SLAB_T}, island {SW.ISLAND_T}; racks "
          f"{rk} {'as kit rows' if fine else 'gear faces = the kits no-bindings build'}; {len(rows)} "
          f"vanilla rows {zones} (+ {len(allrows) - len(rows)} mod rows), "
          f"parent-0 rows on a face; stair y {tr[:, 1].min():.2f}..{tr[:, 1].max():.2f}, x <= "
          f"{sall[:, 0].max():.2f}; valet corridor {worst}; dials at {SW.CT_CLOCK_Z_L5}, "
          f"preview-camera visibility {report}")


def verify_vanilla():
    """Re-derive every recipe VANILLA entry from the vanilla dump (see the docstring). Returns
    the number of entries checked, or None when the dump is absent."""
    import re
    import zipfile
    from recipes import l5_swanky_bottom as SW
    dump = os.path.join(os.environ.get("TEMP", ""), "pltar", "dump.zip")
    if not os.path.isfile(dump):
        print(f"SKIP verify_vanilla: no dump at {dump}")
        return None
    z = zipfile.ZipFile(dump)
    names = z.namelist()
    static = {n.split("/")[2]: n for n in names
              if n.startswith("00_BaseGame/Game.Prefabs.StaticObjectPrefab/") and n.endswith(".Prefab")}
    render = {n.split("/")[2]: n for n in names
              if n.startswith("00_BaseGame/Game.Prefabs.RenderPrefab/") and n.endswith(".Prefab")}
    ph_re = re.compile(r'"m_Placeholders"\s*:\s*\{.*?"\$rcontent"\s*:\s*\[(.*?)\]', re.S)
    b_re = re.compile(r'"m_Bounds".*?"min".*?"x":\s*([-0-9.Ee]+),\s*"y":\s*([-0-9.Ee]+),\s*'
                      r'"z":\s*([-0-9.Ee]+).*?"max".*?"x":\s*([-0-9.Ee]+),\s*"y":\s*'
                      r'([-0-9.Ee]+),\s*"z":\s*([-0-9.Ee]+)', re.S)
    w_re = re.compile(r'"WeatherObject".*?"m_RequireSnow":\s*(\w+),\s*"m_ForbidSnow":\s*(\w+)', re.S)
    gtov, snow = {}, {}
    for k in sorted(static):
        t = z.read(static[k]).decode("utf-8", "replace")
        m = ph_re.search(t)
        if m:
            for g in re.findall(r"UnityGUID:([0-9a-f]{32})", m.group(1)):
                gtov.setdefault(g, []).append(k)
        w = w_re.search(t)
        snow[k] = None if not w else ("require" if w.group(1) == "true" else
                                      "forbid" if w.group(2) == "true" else None)
    want = {v["guid"]: k for k, v in SW.VANILLA.items()}
    cons = {}
    for n in names:
        if n.startswith("00_BaseGame/Game.Prefabs.BuildingPrefab/") and n.endswith(".Prefab"):
            t = z.read(n).decode("utf-8", "replace")
            for g in want:
                if g in t:
                    cons[g] = cons.get(g, 0) + 1
    out = {}
    for pf, s in sorted(SW.VANILLA.items()):
        vs = tuple(sorted(gtov.get(s["guid"], [])))
        assert vs == tuple(sorted(s["variants"])), f"{pf}: variants {vs} != {s['variants']}"
        assert pf in static and "Game.Prefabs.PlaceholderObject, Game" in \
            z.read(static[pf]).decode("utf-8", "replace"), f"{pf}: no PlaceholderObject prefab"
        meshes = [pf + " Mesh"] if pf + " Mesh" in render else []   # the placeholder's own
        for v in vs:
            if v + " Mesh" in render:
                meshes.append(v + " Mesh")
            else:           # trees: one mesh per growth stage; Trashbin0N: Empty / Full meshes
                stem, num = re.match(r"(.*?)(\d+)$", v).groups()
                hit = sorted(k for k in render if re.fullmatch(
                    re.escape(stem) + r"[A-Z][A-Za-z]*" + num + " Mesh", k))
                assert hit, f"{pf}: no mesh for the variant {v}"
                meshes += hit
        lo = hi = None
        tris = 0
        for m in meshes:
            t = z.read(render[m]).decode("utf-8", "replace")
            b = [float(x) for x in b_re.search(t).groups()]
            lo = b[:3] if lo is None else [min(a, c) for a, c in zip(lo, b[:3])]
            hi = b[3:] if hi is None else [max(a, c) for a, c in zip(hi, b[3:])]
            tris = max(tris, int(re.search(r'"m_IndexCount":\s*(\d+)', t).group(1)) // 3)
        for got, exp in ((lo, s["bounds"][0]), (hi, s["bounds"][1])):
            assert all(abs(a - b) <= 0.0015 for a, b in zip(got, exp)), f"{pf}: bounds {lo} {hi}"
        assert tris == s["tris"], f"{pf}: tris {tris} != {s['tris']}"
        sn = {snow[v] for v in vs}
        assert sn == {s["snow"]}, f"{pf}: snow flags {sn} != {s['snow']}"
        assert cons.get(s["guid"], 0) > 0, f"{pf}: no base-game building places {s['guid']}"
        out[pf] = (len(vs), cons[s["guid"]])
    import cs2_liftdress as LD
    shared = sorted(set(LD.SUBOBJ) & set(SW.VANILLA))
    wider = []
    for pf in shared:                   # same GUID; our envelope contains SUBOBJ's
        assert LD.SUBOBJ[pf]["guid"] == SW.VANILLA[pf]["guid"], pf
        (a0, a1), (b0, b1) = LD.SUBOBJ[pf]["bounds"], SW.VANILLA[pf]["bounds"]
        assert all(b0[k] <= a0[k] + 1e-6 and b1[k] >= a1[k] - 1e-6 for k in range(3)), pf
        if tuple(a0) != tuple(b0) or tuple(a1) != tuple(b1):
            wider.append(pf)
    print(f"ok verify_vanilla: {len(out)} placeholders re-derived from the dump "
          f"(variants, consumers) {out}; {len(shared)} share cs2_liftdress.SUBOBJ's GUID "
          f"{shared}, envelope wider than SUBOBJ's (variant meshes it does not read): {wider}")
    return len(out)


def pass1(tmp):
    L4, L5 = _recipes()
    out = {}
    from recipes import l5_swanky_bottom as SW0
    racks = _rack_face_counts(SW0)      # before the builds: it clears the scene
    for recipe in (L4, L5):
        r = blockout(recipe, os.path.join(tmp, "p1", recipe.key, "B"))
        name = r["name"]
        assert "CommercialLow" in name and "_" not in name, name
        rep = cs2_validate._Report(name)
        cs2_validate.lint_asset_name(name, rep)
        assert not [f for f in rep if f[0] == "ERROR"], list(rep)
        assert r["tris"][0] <= recipe.budget.lod0_cap, r["tris"]
        b = r["base"][0]
        obj = bpy.data.objects[name]
        W = K._world_coords(obj)
        if recipe.base_mode == "engine":
            assert b["verdict"] == "OK", b
            from recipes import l5_swanky_bottom as SW
            for gp in SW.GROUND_PARTS + ("wall_gct",):
                assert gp in b["ok_parts"], f"C3 {gp} has no base edges: {b['ok_parts']}"
            assert float(W[:, 2].min()) > -1e-4
        else:
            assert b["ok_edges"] == 0, b
            assert abs(float(W[:, 2].min()) + 12.0) <= 0.01, W[:, 2].min()
            assert not (np.abs(W[:, 2]) <= 0.01).any(), "C3 vertex in the 0 band"
        hidden, crep = check_c10(obj, recipe)
        assert hidden <= C10_MAX, f"C10 {name}: {hidden:.1%} hidden > {C10_MAX:.0%}"
        win = bpy.data.objects[f"{name}_Win"]
        ids = check_cells(win)
        loose = _loose_verts(win)
        if recipe.key == "L5":         # r1: 42 loose verts once put the _Win bounds at grade
            assert loose == 0, f"{name}_Win: {loose} vertices no pane uses"
        cells = r["cells"]
        assert len(set(cells.values())) == len(cells),             f"C13 {name}: room ids reused {cells} (split_rooms would overlap the _Win cells)"
        table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
        labels = [n for n in table if n.endswith("_face")]
        want = [f"clock_ct{k}_face" for k in range(4)] if recipe.key == "L5" else ["sign_fc_face"]
        assert sorted(want) == sorted(n for n in labels if n in want), labels
        table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
        LOD0_BOUNDS[recipe.key] = _fbx_frame(W.min(0), W.max(0))
        LOD2_KEPT[recipe.key] = _fbx_frame(*lod2_kept_bounds(recipe, obj, table))
        over = coplanar_overlaps(obj, table)
        if recipe.key == "L5":         # revision-1 contract; L4 reported only (not revised)
            assert not over, f"{name}: {len(over)} coplanar same-facing overlaps: {over[:8]}"
            check_l5_r1(r, obj, table)
            # revision 3 (the revision-2 plaza checks retired with the road plaza)
            with open(os.path.join(r["asset_dir"], f"{name}.manifest.json"), encoding="utf-8") as fh:
                man = json.load(fh)
            check_l5_r3(r, obj, table, man, racks)
            check_l5_r4(r, obj, table, man)        # revision 4 (eighteenth look)
            out["L5fine"] = check_l5_fine(tmp, r, racks)   # fine-detail step 3e
        else:
            print(f"info {recipe.key}: {len(over)} coplanar same-facing part overlaps {over[:4]}")
            with open(os.path.join(r["asset_dir"], f"{name}.manifest.json"), encoding="utf-8") as fh:
                man = json.load(fh)
            check_l4_r1(r, obj, table, man)        # L4 revision 1 (eighteenth look)
            out["L4fine"] = check_l4_fine()                # fine-detail step 3e
        print(f"ok {recipe.key} {name}: LOD0 {r['tris'][0]} tris, base {b['verdict']} "
              f"({b['ok_edges']} OK edges), C10 residual hidden {hidden:.1%} of "
              f"{crep['area_before']:.0f} m2, rooms {len(r['cells'])} -> {len(ids)} cells, "
              f"lot margins x {r['lot']['margin_x']} y {r['lot']['margin_y']}, arrival x "
              f"{r['facing']['arrival_centroid_x']}, kept {r['area']['kept_m2']} m2")
        out[recipe.key] = r
    # L5 without the in-mesh clock tower (lodge.py --no-tower)
    r = blockout(L5, os.path.join(tmp, "p1", "L5nt", "B"), ["--no-tower"])
    obj = bpy.data.objects[r["name"]]
    zmax = float(K._world_coords(obj)[:, 2].max())
    from recipes import l5_swanky_bottom as SW
    assert abs(zmax - SW.FLUE_TOP) < 0.06, (zmax, SW.FLUE_TOP)
    assert r["base"][0]["verdict"] == "OK"
    print(f"ok L5 --no-tower: LOD0 {r['tris'][0]} tris, max z {zmax:.2f} (flue pots)")
    # the hosted clock tower (tram base station / static object): footing -1.5, no ground
    lodge.clear_scene()
    from recipes import l5_swanky_bottom as SW
    parts = SW.clock_tower("cth", 0.0, 0.0, -1.5, ground=False)
    lo, hi = SW.parts_bounds(parts)
    assert abs(lo[2] + 1.5) < 1e-6 and abs(hi[2] - SW.CT_FINIAL_TOP) < 1e-6, (lo, hi)
    assert max(abs(lo[0]), abs(hi[0]), abs(lo[1]), abs(hi[1])) <= 3.6, (lo, hi)
    # revision 2 kept the defaults: the section-5 dial sits on the stage (+X face at hg + t)
    flo, fhi = SW.parts_bounds(parts, ("clock_cth0_face",))
    assert abs(0.5 * (flo[2] + fhi[2]) - SW.CT_CLOCK_Z) < 1e-6 and \
        abs(flo[0] - (SW.CT_STAGE_W / 2 + 0.08)) < 1e-6, (flo, fhi)
    n = sum(len(o.data.polygons) for o, _m in parts)
    print(f"ok clock_tower hosted: z {lo[2]:.2f}..{hi[2]:.2f}, footprint "
          f"+-{max(abs(lo[0]), hi[0]):.2f} m, {len(parts)} parts, {n} faces")
    lodge.clear_scene()
    parts = SW.clock_tower("ctr", 0.0, 0.0, 0.0, ground=True, rise=SW.CT_RISE)
    lo, hi = SW.parts_bounds(parts)
    assert abs(hi[2] - (SW.CT_FINIAL_TOP + SW.CT_RISE)) < 1e-6 and abs(lo[2]) < 1e-9, (lo, hi)
    print(f"ok clock_tower rise {SW.CT_RISE}: finial {hi[2]:.2f} (L5 revision 1)")
    lodge.clear_scene()
    return out


def check_pane_guards():
    """buildings-review-geometry F1-F3 guards, each shown to FAIL on a wrong build:
    - L5 with the old 1.0 m cross-gable bays (inside the 1.46 m main eave reach) raises F1;
    - check_pane_clearance raises on a roof part 0.3 m in front of a pane and passes at 0.7 m
      (the 0.6 m ray reach; L4 / L5 builders and test_lodge_contracts use it)."""
    from cs2_bldg import WallRef
    L4, L5 = _recipes()
    g = L5.builder.__globals__              # the module the registered builder really reads
    old = g["XG_PROJ"]
    g["XG_PROJ"] = 1.0
    msg = ""
    try:
        lodge.clear_scene()
        lodge.build_parts(L5, {"tower": True})
    except RuntimeError as e:
        msg = str(e)
    finally:
        g["XG_PROJ"] = old
        lodge.clear_scene()
    assert "F1 cross gable" in msg, f"L5 with 1.0 m bays did not fail F1: {msg!r}"
    SW = g["check_pane_clearance"]
    for x0, want_fail in ((0.30, True), (0.70, False)):
        lodge.clear_scene()
        parts = K.pane_quad("win_t_p0", WallRef("+X", 0.0), -0.5, 0.5, 1.0, 2.0)
        parts += K.box_part("roof_t", (x0, -1.0, 1.2), (x0 + 0.2, 1.0, 1.8), "trim")
        try:
            SW(parts, "probe")
            failed = False
        except RuntimeError:
            failed = True
        assert failed == want_fail, f"pane clearance with a roof part at x {x0}: failed={failed}"
    lodge.clear_scene()
    print(f"ok pane guards: L5 with 1.0 m bays fails F1 ({msg[:60]}...); a roof part 0.3 m in "
          f"front of a pane fails the 0.6 m pane-ray check, 0.7 m passes")


def _blender_run(argv, log):
    cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
           "--python", os.path.join(HERE, "lodge.py"), "--"] + argv
    with open(log, "w", encoding="utf-8") as fh:
        res = subprocess.run(cmd, stdout=fh, stderr=subprocess.STDOUT, timeout=1800)
    if res.returncode != 0:
        raise RuntimeError(f"child Blender failed ({res.returncode}): {' '.join(argv)} - see {log}")


def _manifest(d, name):
    with open(os.path.join(d, name, f"{name}.manifest.json"), encoding="utf-8") as fh:
        return json.load(fh)


def _sha(path):
    with open(path, "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()[:12]


def pass2(tmp, fast):
    L4, L5 = _recipes()
    for recipe in (L4, L5):
        name = lodge.asset_name(recipe, "B")
        man = []
        for k in range(2):
            d = os.path.join(tmp, "p2", f"{recipe.key}{k}", "B")
            _blender_run(["--recipe", recipe.key, "--blockout", "--out", d],
                         os.path.join(tmp, f"p2_{recipe.key}{k}.log"))
            man.append(_manifest(d, name))
        h0, h1 = man[0]["mesh_hash"], man[1]["mesh_hash"]
        assert h0 == h1, f"{recipe.key}: blockout hashes differ across processes {h0} {h1}"
        assert man[0]["rooms"] == man[1]["rooms"], f"{recipe.key}: room cells differ"
        print(f"ok {recipe.key}: blockout identical across 2 processes {h0}")
    if fast:
        print("skip reduced-finish determinism (--fast)")
        return
    name = lodge.asset_name(L4, "B")
    shas = []
    for k in range(2):
        d = os.path.join(tmp, "p2f", f"L4{k}", "B")
        _blender_run(["--recipe", "L4", "--tex", "1024", "--ao-samples", "4", "--no-validate",
                      "--out", d], os.path.join(tmp, f"p2f_L4{k}.log"))
        m = _manifest(d, name)
        pngs = {f: _sha(os.path.join(d, name, f)) for f in sorted(os.listdir(os.path.join(d, name)))
                if f.endswith(".png")}
        shas.append((m["mesh_hash"], pngs))
    assert shas[0] == shas[1], f"L4 reduced finish differs across processes: {shas}"
    print(f"ok L4: reduced finish identical across 2 processes ({len(shas[0][1])} PNGs, "
          f"LOD hashes {shas[0][0]})")


def _basis_coords(o):
    """Vertex coords through matrix_basis (matrix_world is stale right after an import)."""
    co = np.empty(len(o.data.vertices) * 3, np.float32)
    o.data.vertices.foreach_get("co", co)
    M = np.array(o.matrix_basis, dtype=np.float64)
    return co.reshape(-1, 3).astype(np.float64) @ M[:3, :3].T + M[:3, 3]


def _fbx_bounds(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    # import_scene.fbx keeps the file's raw centimetres (export_fbx bakes x100)
    pts = np.concatenate([_basis_coords(o) for o in new if o.type == "MESH"])         / cs2_validate.RAW_UNITS_PER_M
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    return pts.min(0), pts.max(0)


def pass3():
    L4, L5 = _recipes()
    found = 0
    for recipe, expect_batches in ((L4, 6), (L5, 9)):
        name = lodge.asset_name(recipe, "B")
        d = os.path.join(EXPORT, name)
        if not os.path.isfile(os.path.join(d, f"{name}.manifest.json")):
            print(f"SKIP {name}: not exported yet ({d})")
            continue
        found += 1
        files = set(os.listdir(d))
        levels = [lvl for lvl in (0, 1, 2) if (f"{name}.fbx" if lvl == 0 else f"{name}_LOD{lvl}.fbx")
                  in files]
        batches = 0
        for lvl in levels:
            win = (f"{name}_Win.fbx" if lvl == 0 else f"{name}_LOD{lvl}_Win.fbx") in files
            batches += 1 + int(win) + int(recipe.base_mode == "engine")
        assert levels == [0, 1, 2], levels
        assert batches == expect_batches <= 16, f"C12 {name}: {batches} batches"
        worst = batches + (3 if recipe.base_mode == "plinth" else 0)
        assert worst <= 16
        m = json.load(open(os.path.join(d, f"{name}.manifest.json"), encoding="utf-8"))
        tex = m["tex"]
        med = (m.get("shipped_density") or {}).get("median_w1")
        assert tex in (2048, 4096), tex
        assert tex == 2048 or med is not None, "4096 without a measured density"
        lo0, hi0 = _fbx_bounds(os.path.join(d, f"{name}.fbx"))
        drift = {}
        lods = {}
        for lvl in (1, 2):
            lo, hi = _fbx_bounds(os.path.join(d, f"{name}_LOD{lvl}.fbx"))
            lods[lvl] = (lo, hi)
            drift[lvl] = round(float(max(np.abs(lo - lo0).max(), np.abs(hi - hi0).max())), 3)
        assert drift[1] <= 0.05, f"C15 {name}: LOD1 bounds drift {drift[1]} m"
        # LOD2 vs the LOD0 parts LOD2 keeps (pass 1); the pass-1 LOD0 must be this export's
        b0 = LOD0_BOUNDS.get(recipe.key)
        assert b0 is not None, f"C15 {name}: pass 1 did not record the LOD0 bounds"
        same = float(max(np.abs(b0[0] - lo0).max(), np.abs(b0[1] - hi0).max()))
        assert same <= 0.01, f"C15 {name}: exported LOD0 bounds differ from the recipe's by " \
                             f"{same:.3f} m (stale export?)"
        klo, khi = LOD2_KEPT[recipe.key]
        lo2, hi2 = lods[2]
        kept = round(float(max(np.abs(lo2 - klo).max(), np.abs(hi2 - khi).max())), 3)
        assert kept <= 0.25, f"C15 {name}: LOD2 bounds drift {kept} m vs the kept LOD0 parts " \
                             f"(lo {np.round(lo2 - klo, 2)}, hi {np.round(hi2 - khi, 2)})"
        subs = ""
        # revision 3 / L4 revision 1: the rows ship in the manifest; fine-detail step 3e: = the recipe's
        # rows (vanilla + kit) + the sign set's identity row (L5)
        so = (m.get("sub_objects") or {}).get("objects") or []
        want = _want_rows(recipe)
        # T53: the shipped export predates the T14 x-sign N rule-T mirror (2026-10-16, DECISIONS 245: _want_rows is the
        # game frame, the shipped rows still have x un-negated), so it is stale until the next export. If the shipped
        # rows equal the recipe rows with x negated, SKIP (loud). Any other difference still fails. (T26's parasol
        # normalisation is gone: the parasols are back in the recipes and in the shipped manifest, 2026-10-17.)
        so_np = so
        got_rows = _got_rows(so_np)[:len(want)]
        pre_t14 = [(p, g, _t14_pos(pos), par) for p, g, pos, par in want]
        if got_rows != want and got_rows == pre_t14:
            print(f"SKIP {name} shipped manifest sub_objects == recipe rows: stale until the next export "
                  f"(pre-T14 un-mirrored rows, DECISIONS 245) / T53")
        else:
            assert got_rows == want, f"{name}: manifest sub_objects stale"
        extra = so_np[len(want):]
        assert [(o.get("role"), o["prefab"]) for o in extra] == ([("signset", recipe.sign_set.prop)]
                                                                if recipe.sign_set else []), extra
        nmod = sum(1 for o in so if o.get("kind") == "mod")
        subs = f", {len(so)} sub-objects in the manifest ({len(so) - nmod} vanilla, {nmod} mod)"
        if recipe.sign_set:
            sd = os.path.join(REPO, "assets", "import", "signs", recipe.sign_set.letter, recipe.sign_set.mesh)
            sm = json.load(open(os.path.join(sd, f"{recipe.sign_set.mesh}.manifest.json"), encoding="utf-8"))
            assert sm["object_cid"] == extra[0]["m_Object"] and sm["tris"] == 2, (sm["object_cid"], sm["tris"])
            subs += f", sign set {recipe.sign_set.mesh} (CID {sm['object_cid'][:8]}, m_Bias {sm['cull']['m_Bias']})"
        print(f"ok {name} (export): batches {batches} (worst case {worst}) <= 16, tex {tex}, "
              f"shipped w=1 median {med} px/m, tris {m['tris']}, LOD bounds drift {drift} m "
              f"(LOD2 vs its kept parts {kept} m){subs}")
    return found


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    fast = "--fast" in argv
    t0 = time.perf_counter()
    tmp = tempfile.mkdtemp(prefix="lodges_swanky_")
    verify_vanilla()
    try:
        pass1(tmp)
        check_pane_guards()
        check_r4_guards()              # revision 4 / L4 revision 1 negatives
        pass2(tmp, fast)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    n = pass3()
    print(f"PASS test_lodges_swanky: L4 + L5 contracts, C10 <= {C10_MAX:.0%}, cross-process "
          f"determinism, {n} exported folder(s) checked ({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_lodges_swanky")
        sys.exit(1)
