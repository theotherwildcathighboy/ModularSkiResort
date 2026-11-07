"""
test_lodges_basic - the L2 hut and L3 chalet recipes (recipes/lodge_hut.py, recipes/lodge_chalet.py): determinism,
geometry contracts, budgets.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_lodges_basic.py [-- --quick]

Contract: design/lodges-basic.md sections 4.9, 5.9, 9 (C1-C14) and 10 with design/lodges-basic-skeptic.md applied (M5
room prefixes, M7 bounds drift = WARN, M8 contracts in the authoring frame and on every LOD, M10 review tints). Kit
API per bldg-kit.md (R25).

Pass 1 per recipe: --blockout twice in ONE process gives identical LOD0 / `_Win` mesh hashes, room cells, cull counts
and tris. Then the contracts run on the blockout mesh (authoring frame recovered from the post-orient mesh: authored
(x, y) = (-by, bx)). Pass 2 per recipe (skipped with --quick): a reduced finish (--tex 1024 --ao-samples 8
--no-validate, LODs on) runs twice. The five PNGs are byte-identical, and the LOD0 / LOD1 / LOD2 / `_Win` hashes are
identical. LOD contracts run on the LOD meshes captured from cs2_bldg.lod_chain. Nitpick C (2026-10-13,
build/nitpick-c-lodges.md): L2's fuel-station barrels are ground rows (parent -1, no drum_ part left in the host).
L3's three boards are blank (l3_blank_boards: blank draws, we_boards, the shipped rims >= 4 texels, --no-fine keeps
the pictograms). T60 (2026-10-18, build/T59-T60-chalet-summit.md): l3_t60 checks the kiosk door and the patio double
door (bW1 replaced) on the blockout and on a door kit v2 parts build. The build has leaves, a lever per leaf, and
glass in the existing rooms kiosk / bF1W, with no bW1 pane left. It also checks the two T60 wall-light rows on a
non-glass wall face beside their door's casing, facing out. Exit 1 on any failure. The test prints WARN lines and
they are never fatal.
"""

import hashlib
import json
import math
import os
import shutil
import sys
import tempfile
import time
import traceback

import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import bldg_recipes as R   # noqa: E402
import cs2_asset           # noqa: E402
import cs2_bldg as K       # noqa: E402
import cs2_validate        # noqa: E402
import fine_detail as FD   # noqa: E402
import lodge               # noqa: E402

KEYS = ("L2", "L3")
WARNS = []


def warn(msg):
    WARNS.append(msg)
    print(f"WARN {msg}")


def sha(path):
    with open(path, "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()[:12]


# ======================================================================================
# Geometry helpers (authoring frame)
# ======================================================================================

def coords(obj_or_mesh, matrix=None):
    me = obj_or_mesh.data if hasattr(obj_or_mesh, "data") else obj_or_mesh
    co = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.array(matrix if matrix is not None else
                 (obj_or_mesh.matrix_world if hasattr(obj_or_mesh, "matrix_world") else np.eye(4)))
    W = co @ M[:3, :3].T + M[:3, 3]
    return np.stack([-W[:, 1], W[:, 0], W[:, 2]], axis=1)      # post-orient -> authored


def to_auth(v):
    return np.array((-v[1], v[0], v[2]))


def face_parts(me):
    out = np.zeros(len(me.polygons), np.int32)
    a = me.attributes.get("part")
    if a is not None:
        a.data.foreach_get("value", out)
    return out


def faces_of(me, table, prefixes):
    pp = face_parts(me)
    ids = {i + 1 for i, n in enumerate(table) if n.startswith(tuple(prefixes))}
    return [p for p in me.polygons if int(pp[p.index]) in ids]


def centroid_x(me, table, prefixes):
    fs = faces_of(me, table, prefixes)
    assert fs, f"no faces for {prefixes}"
    A = sum(p.area for p in fs)
    return sum(to_auth(p.center)[0] * p.area for p in fs) / A


def part_coords(me, table, prefixes):
    V = coords(me, np.eye(4))
    idx = sorted({v for p in faces_of(me, table, prefixes) for v in p.vertices})
    return V[idx]


def roof_plane(me, table, part, thick):
    """(normal, point) of the largest up-facing face of `part` in the authoring frame, and
    a function z_centre(x, y) of the slab CENTER plane (top plane - thick / 2 / n.z)."""
    fs = [p for p in faces_of(me, table, (part,)) if p.normal.z > 0.3]
    assert fs, f"{part}: no top face"
    top = max(fs, key=lambda p: p.area)
    n = to_auth(top.normal)
    p0 = to_auth(me.vertices[top.vertices[0]].co)

    def zc(x, y):
        z_top = p0[2] - (n[0] * (x - p0[0]) + n[1] * (y - p0[1])) / n[2]
        return z_top - 0.5 * thick / n[2]
    return n, zc


def pitch_deg(n):
    return math.degrees(math.atan2(math.hypot(n[0], n[1]), n[2]))


# ======================================================================================
# Contracts
# ======================================================================================

def common_contracts(recipe, mod, r, obj, win):
    name = r["name"]
    table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    # C13 names
    assert cs2_asset.validate_asset_name(name) == name and "_" not in name, name
    rep = cs2_validate._Report(name)
    cs2_validate.lint_asset_name(name, rep)
    assert not [f for f in rep if f[0] == "ERROR"], f"{name}: lint {list(rep)}"
    assert recipe.interior in name and name.endswith("B"), name
    # C1 lot fit inside the lot itself (authoring frame), LOD0 + _Win
    W_, D_ = recipe.lot
    for o in (obj, win):
        V = coords(o)
        assert np.abs(V[:, 0]).max() <= 4 * D_ + 1e-6, f"{o.name}: |x| {np.abs(V[:, 0]).max():.3f} > {4 * D_}"
        assert np.abs(V[:, 1]).max() <= 4 * W_ + 1e-6, f"{o.name}: |y| {np.abs(V[:, 1]).max():.3f} > {4 * W_}"
    assert r["lot"]["margin_x"] >= 0 and r["lot"]["margin_y"] >= 0, r["lot"]
    # C4 facing (kit rule)
    assert float(r["facing"]["arrival_centroid_x"]) >= r["facing"]["need"], r["facing"]
    # C9 rooms: every ROOMS key has panes, ids as tabled, distinct, in 1..23. Panes <= 3.2 m
    rooms = mod["ROOMS"]
    cells = r["cells"]
    assert set(cells) == set(rooms), f"rooms without panes / extra: {set(rooms) ^ set(cells)}"
    for k, (c, row) in rooms.items():
        assert cells[k] == row * 5 + c, (k, cells[k])
    ids = list(cells.values())
    assert len(set(ids)) == len(ids) and all(1 <= i <= 23 for i in ids), ids
    dec = K.win_cell_ids(win)
    assert set(dec) == set(ids), f"decoded {sorted(set(dec))} vs cells {sorted(ids)}"
    Vw = coords(win)
    for p in win.data.polygons:
        z = Vw[list(p.vertices), 2]
        assert z.max() - z.min() <= 3.2 + 1e-6, f"pane {p.index} {z.max() - z.min():.2f} m tall"
    # M5: no room prefix is a prefix of another
    pre = [f"win_{k}_" for k in rooms]
    for a in pre:
        for b in pre:
            assert a == b or not b.startswith(a), (a, b)
    # C10 area / sum w2a
    area = r["area"]
    assert area["kept_m2"] <= mod["AREA_CAP_M2"], f"{name}: kept {area['kept_m2']} > {mod['AREA_CAP_M2']}"
    if area["sum_w2a"] > mod["SUM_W2A_CAP"]:
        warn(f"{name}: sum w2a {area['sum_w2a']} > plan cap {mod['SUM_W2A_CAP']} (lodges-basic C10 print)")
    # C11 prefixes + materials
    bad = [n for n in table if not n.startswith(mod["PREFIXES"])]
    assert not bad, f"parts outside the prefix table: {bad[:6]}"
    assert set(r["keys"]) <= set(K.BLDG_MATERIALS), r["keys"]
    # C12 batches: (main + _Win + base) x 3 levels
    assert (1 + 1 + 1) * 3 <= 16
    # C6 budget (ERROR cap, WARN under 0.6 / 0.75 x target)
    b = recipe.budget
    n0 = r["tris"][0]
    assert n0 <= b.lod0_cap, f"{name}: LOD0 {n0} > {b.lod0_cap}"
    if n0 < 0.75 * b.lod0_target:
        warn(f"{name}: LOD0 {n0} < 0.75 x target {b.lod0_target} (lodges-basic C6 WARN)")
    # M4 labels: each stamped prefix = exactly one part with one face (one UV island). A fine-detail host's sign-set
    # faces left the host (cs2_signset.route: step 3a, L2)
    spec = getattr(recipe, "sign_set", None)
    routed = set(spec.face_names()) if spec is not None else set()
    assert not routed & set(table), f"sign-set faces left in the host: {sorted(routed & set(table))}"
    for prefix, _p, _kw in (e for e in recipe.labels if e[0] not in routed):
        parts = [n for n in table if n.startswith(prefix)]
        assert len(parts) == 1, (prefix, parts)
        assert len(faces_of(obj.data, table, (prefix,))) == 1, prefix
    # M10 review tints: three slots, slot 1 = L1's trim green (one resort, one trim)
    assert set(recipe.slots) == {1, 2, 3}, recipe.slots
    assert tuple(recipe.slots[1]) == tuple(R.get_recipe("L1").slots[1])
    return table


def l2_contracts(recipe, mod, r, obj, win, table):
    me = obj.data
    V = coords(obj)
    Vw = coords(win)
    # plinth mode: no vertex in the 0 +- 0.01 band, min z -7.20, plinth -6.00, open bottom
    for nm, A in (("LOD0", V), ("_Win", Vw)):
        band = int((np.abs(A[:, 2]) <= 0.01).sum())
        assert band == 0, f"L2 {nm}: {band} vertices in |z| <= 0.01"
    assert abs(V[:, 2].min() - mod["SUPPORT_Z0"]) <= 0.01, V[:, 2].min()
    P = part_coords(me, table, ("plinth_",))
    assert abs(P[:, 2].min() + mod["PL_DEPTH"]) <= 0.001, P[:, 2].min()
    assert not [p for p in faces_of(me, table, ("plinth_",)) if p.normal.z < -0.9], "plinth has a -Z face"
    assert r["base"][0]["ok_edges"] == 0, r["base"][0]
    # anchor: the step foot tread top <= 0.15 at x 11.80, nothing beyond x 11.80
    assert max(V[:, 0].max(), Vw[:, 0].max()) <= 11.80 + 1e-3, V[:, 0].max()
    S = part_coords(me, table, ("step_hut2",))
    assert abs(S[:, 0].max() - 11.80) < 1e-3 and S[:, 2].max() <= 0.15, (S[:, 0].max(), S[:, 2].max())
    # facing
    cd = centroid_x(me, table, ("door_hut",))
    ck = centroid_x(me, table, ("deck_hut",))
    assert cd > 7.9 and ck < 0.0, (cd, ck)
    # C5 roof pitch 27.97 and ridge 7.37 (center plane at y 0)
    for part in ("roof_hutA", "roof_hutB"):
        n, zc = roof_plane(me, table, part, mod["ROOF_T"])
        assert abs(pitch_deg(n) - 27.97) < 0.05, (part, pitch_deg(n))
        assert abs(n[0]) < 1e-4, (part, n)                 # ridge along X
        assert abs(zc(1.5, 0.0) - 7.37) < 0.01, (part, zc(1.5, 0.0))
    # sub-objects (detail pass 1: SUB_OBJECTS, 16, parent 0, game (gx, gy, gz) = authored (y, z, x)): deck props on
    # the deck / bridge / ski-wing surfaces, wall props on a wall plane (story x -5.0 / y +-5.5, plinth face x -5.15)
    # facing out of it
    rects = ((mod["DECK_X0"], mod["PL_X0"] - mod["CAP_PROUD"], mod["DECK_Y0"], mod["FL_Y"]),
             (mod["PL_X0"] - mod["CAP_PROUD"], mod["DECK_X1"], mod["DECK_Y0"], -(mod["PL_Y"] + mod["CAP_PROUD"])),
             (mod["BRIDGE_X0"], mod["BRIDGE_X1"], -mod["BRIDGE_Y"], mod["BRIDGE_Y"]),
             (mod["WING_X0"], mod["BRIDGE_X1"], mod["BRIDGE_Y"], mod["WING_Y"]),
             (mod["WING_X0"], mod["BRIDGE_X1"], -mod["WING_Y"], -mod["BRIDGE_Y"]))
    subs = mod["SUB_OBJECTS"]
    # T26 2026-10-16 dropped the parasol (15 rows). Restored 2026-10-17 (vanilla props, random colors accepted): 16
    # rows again, one ParasolRandom01
    assert len(subs) == 16 and all(s[4] == 0 for s in subs), [(s[0], s[4]) for s in subs]
    assert sum(1 for s in subs if s[0] == "ParasolRandom01") == 1, "hut parasol row missing"
    n_wall = 0
    for nm, _g, (gx, gy, gz), q, _parent in subs:
        x, y = gz, gx
        assert abs(math.hypot(q[1], q[3]) - 1.0) < 1e-6 and q[0] == 0.0 and q[2] == 0.0, (nm, q)
        if nm.startswith(("WallLight", "ACVentWall")):
            th = 2.0 * math.atan2(q[1], q[3])
            fx, fy = math.cos(th), math.sin(th)              # local +Z in the authoring frame
            on = [(abs(x - mod["FL_X0"]) < 1e-6 or abs(x - mod["PL_X0"]) < 1e-6) and fx < -0.999,
                  abs(y - mod["FL_Y"]) < 1e-6 and fy > 0.999,
                  abs(y + mod["FL_Y"]) < 1e-6 and fy < -0.999]
            assert any(on), (nm, x, y, fx, fy)
            n_wall += 1
            continue
        assert abs(gy - mod["DECK_TOP"]) < 1e-6, (nm, gy)
        assert any(x0 <= x <= x1 and y0 <= y <= y1 for x0, x1, y0, y1 in rects), (nm, x, y)
    # C11 prefix table: the detail-pass prefixes are registered
    assert {"bench_", "skirack_", "drum_", "flag_", "flagpanel_", "stripe_"} <= set(mod["PREFIXES"])
    # fine-detail step 3a: the recipe rows = these 16 + FINE_SUB_OBJECTS (benches, barrel, entry wall lights) +
    # KIT_ROWS (wing racks), every one parent 0. The benches are on the S deck, the barrel on the crib top, the wall
    # lights on the +X wall facing +X, and the kit racks on the wing decks
    fine, kit = mod["FINE_SUB_OBJECTS"], mod["KIT_ROWS"]
    assert tuple(recipe.sub_objects) == tuple(subs) + tuple(fine) + tuple(kit), "L2 rows"
    n_ground = 0
    for nm, _g, (gx, gy, gz), q, parent in fine:
        x, y = gz, gx
        if nm == "BarrelRandom01":
            # nitpick C (2026-10-13): the fuel station's two barrels are GROUND-following rows (m_ParentMesh -1, gy
            # 0) at the old drum centers beside the entry steps. Nothing of the hut stands there any more
            assert parent == -1 and gy == 0.0 and abs(x - mod["FUEL_X"]) < 1e-6 \
                and y in (mod["FUEL_Y"], mod["FUEL_Y2"]) and tuple(q) == (0.0, 0.0, 0.0, 1.0), (nm, x, y, parent)
            assert not [n for n in table if n.startswith("drum_")], "L2: a fuel-station part is still in the host"
            n_ground += 1
            continue
        assert parent == 0, (nm, parent)
        if nm.startswith("WallLight"):
            assert abs(x - mod["FL_X1"]) < 1e-6 and tuple(q) == (0.0, 0.0, 0.0, 1.0), (nm, x, q)
        else:
            assert abs(gy - mod["DECK_TOP"]) < 1e-6 and inside_any(x, y, rects[1:2]), (nm, x, y)
    for e in kit:
        gx, gy, gz = e["position"]
        assert e["kind"] == "mod" and e["m_ParentMesh"] == 0 and abs(gy - mod["DECK_TOP"]) < 1e-6 \
            and inside_any(gz, gx, rects[3:]), (e["prefab"], e["position"])
    print(f"ok L2 contracts: plinth {P[:, 2].min():.2f}, min z {V[:, 2].min():.2f}, 0 band "
          f"vertices, step top {S[:, 2].max():.3f} at x {S[:, 0].max():.2f}, door x {cd:.2f}, "
          f"deck x {ck:.2f}, pitch / ridge OK, {len(subs) - n_wall} props on deck surfaces + "
          f"{n_wall} on walls; fine-detail rows {len(fine)} vanilla ({n_ground} ground barrels, nitpick C) + "
          f"{len(kit)} kit on deck / ground / +X wall")


def inside_any(x, y, rects):
    return any(x0 <= x <= x1 and y0 <= y <= y1 for x0, x1, y0, y1 in rects)


def l3_contracts(recipe, mod, r, obj, win, table):
    me = obj.data
    b = r["base"][0]
    assert b["verdict"] == "OK" and b["below_verts"] == 0, b
    for gp in recipe.ground_prefixes:
        assert any(n.startswith(gp) for n in b["ok_parts"]), (gp, b["ok_parts"])
    # C5 ridges at right angles: B along Y at 9.40, A along X at 12.86, 27 deg
    nB, zB = roof_plane(me, table, "roof_BB", mod["ROOF_T"])
    nA, zA = roof_plane(me, table, "roof_AA", mod["ROOF_T"])
    assert abs(nB[1]) < 1e-4 and abs(nA[0]) < 1e-4, (nB, nA)
    for n in (nB, nA):
        assert abs(pitch_deg(n) - 27.0) < 0.05, pitch_deg(n)
    assert abs(zB(mod["B_RIDGE_X"], 0.0) - 9.40) < 0.01, zB(mod["B_RIDGE_X"], 0.0)
    assert abs(zA(-5.0, mod["A_RIDGE_Y"]) - 12.86) < 0.01, zA(-5.0, mod["A_RIDGE_Y"])
    # A's +X gable and L2 face above B's roof (no texel below 6.60 at x 2.0)
    G = part_coords(me, table, ("gable_A1",))
    assert G[:, 2].min() >= mod["L1_TOP"] - 1e-3, G[:, 2].min()
    L = part_coords(me, table, ("wall_Al2",))
    assert L[:, 2].min() >= mod["L1_TOP"] - 1e-3, L[:, 2].min()
    # B's -X eave stops where Mass A abuts: no B roof vertex at x < 2.0 with y < -6.5
    BR = part_coords(me, table, ("roof_BA", "soffit_BA", "fascia_BA", "rake_BAs"))
    sel = (BR[:, 1] < mod["A2_Y1"] - 1e-3) & (BR[:, 0] < mod["B_X0"] - 0.15)   # rake 0.40 thick
    assert not sel.any(), BR[sel][:3]
    # facing
    cd = centroid_x(me, table, ("door_Be",))
    cg = centroid_x(me, table, ("gazebocol_gz", "roof_gz"))
    cp = centroid_x(me, table, ("pier_",))
    assert cd > 12.9 and cg < -9.0 and cp < -9.0, (cd, cg, cp)
    # props
    terr = (mod["POD_X0"], mod["B_X0"], mod["A_Y1"], mod["POD_Y1"])
    gz = (mod["GZ_X0"], mod["GZ_X1"], mod["GZ_Y0"], mod["GZ_Y1"])
    balc = ((-15.40, mod["A2_X0"], mod["A2_Y0"], mod["A2_Y1"]), (-12.0, 0.0, -19.80, mod["A2_Y0"]))
    foot = ((mod["B_X0"], mod["B_X1"], mod["B_Y0"], mod["B_Y1"]), (mod["POD_X0"], mod["B_X0"], mod["A_Y0"], mod["POD_Y1"]),
            (mod["KI_X0"], mod["KI_X1"], mod["KI_Y0"], mod["KI_Y1"]), (13.0, 15.5, -8.5, -4.5))

    def inside(x, y, rc):
        return rc[0] <= x <= rc[1] and rc[2] <= y <= rc[3]
    W_, D_ = recipe.lot
    n = 0
    for nm, _g, pts, _yaw in mod["PROPS"]:
        for (gx, gy, gz_) in pts:
            x, y = gz_, gx
            assert abs(x) <= 4 * D_ and abs(y) <= 4 * W_, (nm, x, y)
            if abs(gy - mod["TERR_TOP"]) < 1e-6:
                assert inside(x, y, terr) and not inside(x, y, gz), (nm, x, y)
            elif abs(gy - 6.80) < 1e-6:
                assert any(inside(x, y, rc) for rc in balc), (nm, x, y)
            else:
                assert gy == 0.0 and not any(inside(x, y, rc) for rc in foot), (nm, x, y)
            n += 1
    # vanilla retrofit (fifteenth look, 2026-10-11): PROPS are the recipe's sub_objects (29 rows, grade props
    # m_ParentMesh -1, terrace / balcony 0) and reach the manifest block. The modeled stair-foot lamp post is gone.
    # No lodge triangle is inside any prop box and no boxes overlap (a parasol pole in its own table set excepted).
    # Every box is inside the lot. fine-detail step 3a: the recipe rows = these 29 + FINE_SUB_OBJECTS (8 entry /
    # porch / wall lanterns -> WallLightShortRange, the gazebo lantern -> RoofLight) + KIT_ROWS (3 racks at
    # SKI_RACK_SPOTS). No lantern lens is left (no lamp_ part at all: no _Emissive map)
    subs = recipe.sub_objects
    base = tuple(mod["SUB_OBJECTS"])
    fine_rows = tuple(mod["FINE_SUB_OBJECTS"]) + tuple(mod["KIT_ROWS"]) + tuple(mod["T60_SUB_OBJECTS"])
    # T26 2026-10-16 dropped the 4 parasols (25 rows). Restored 2026-10-17 (vanilla props, random colors accepted):
    # 29 rows again, four ParasolRandom01
    assert tuple(subs) == base + fine_rows and len(base) == n == 29, (len(subs), n)
    assert sum(1 for s in base if s[0] == "ParasolRandom01") == 4, "chalet parasol rows missing"
    assert all((s[4] == -1) == (s[2][1] == 0.0) for s in base), [(s[0], s[2], s[4]) for s in base]
    blk = lodge.sub_objects_block(recipe)
    assert blk["target_prefab"] == recipe.prefab and len(blk["objects"]) == len(subs), blk["target_prefab"]
    assert not [t for t in table if t.startswith(("col_lampTS", "lamp_terrstair"))]
    assert not [t for t in table if t.startswith("lamp_")], [t for t in table if t.startswith("lamp_")]
    rows = [(e["prefab"], e["m_Object"], tuple(e["position"]), tuple(e["rotation"]), e["m_ParentMesh"])
            if isinstance(e, dict) else tuple(e) for e in subs]
    near = props_clear(recipe, obj, table, rows)
    print(f"ok L3 contracts: engine base OK ({b['ok_edges']} OK edges, {len(b['ok_parts'])} "
          f"ground parts), ridges B {zB(mod['B_RIDGE_X'], 0.0):.3f} (Y) / A "
          f"{zA(-5.0, mod['A_RIDGE_Y']):.3f} (X), A +X gable from {G[:, 2].min():.2f}, B -X eave "
          f"trimmed, door x {cd:.2f}, gazebo x {cg:.2f}, piers x {cp:.2f}, {n} props placed + "
          f"{len(fine_rows)} fine-detail rows = {len(subs)} sub_objects rows, no lodge triangle in a "
          f"prop box (wall / ceiling props off their mount by 5 mm), closest boxes {near:.3f} m")
    l3_fine_h1(recipe, mod, r["lot"])
    l3_t60(recipe, mod, table)
    l3_blank_boards(recipe, mod, r)


def l3_blank_boards(recipe, mod, r):
    """Nitpick C (2026-10-13, "not loving the SIGN of the FORK AND KNIFE ... leave it blank or keep it simple"): the
    chalet's three pictogram boards are BLANK boards (fine_detail.blank_board: the board ground + a trim-color rim).
    Each is a flat label quad listed in the run's `we_boards` (role blank, game-frame center / unit horizontal normal
    / size = the face). `--no-fine` keeps the pictograms (PICTO_LABELS: negative)."""
    labels = recipe.labels
    assert len(labels) == 3 and all(FD.is_blank_label(e) for e in labels), [e[:2] for e in labels]
    leg = FD.effective(recipe, fine=False).labels
    assert tuple(leg) == tuple(mod["PICTO_LABELS"]) and not any(FD.is_blank_label(e) for e in leg), "L3 legacy"
    wb = r.get("we_boards") or []
    assert [b["face"] for b in wb] == [e[0] for e in labels] and all(b["role"] == "blank" for b in wb), wb
    assert sorted(tuple(b["size_m"]) for b in wb) == [(1.6, 0.5), (4.0, 0.6), (4.0, 0.6)], wb
    for b in wb:
        n = np.array(b["normal"], np.float64)
        assert abs(np.linalg.norm(n) - 1.0) < 1e-3 and abs(n[1]) < 1e-6, (b["face"], n)
    # the SHIPPED export (2048 atlas): every board painted blank with a rim >= 4 texels, its we_boards = this build's
    name = r["name"]
    mp = os.path.join(lodge.IMPORT_ROOT, recipe.category, recipe.letter, name, f"{name}.manifest.json")
    shipped = "not exported"
    if os.path.isfile(mp):
        man = json.load(open(mp, encoding="utf-8"))
        lab = man.get("labels") or {}
        rims = {f: [d.get("rim_px") for d in lab.get(f, {}).get("draws", []) if d.get("kind") == "blank"]
                for f in (e[0] for e in labels)}
        assert all(len(v) == 1 and v[0] >= 4.0 for v in rims.values()), rims
        got_wb = [(b["face"], b["centre"], b["normal"], b["size_m"]) for b in man["we_boards"]["boards"]]
        want_wb = [(b["face"], b["centre"], b["normal"], b["size_m"]) for b in wb]
        # T14 x-sign N (2026-10-16), DECISIONS 245 / T53: the build writes we_boards in the game frame (center x and
        # normal x negated, rule T). A shipped manifest from before the next export still holds the pre-T14 boards.
        # Stale = it equals the build with x negated: SKIP (loud). Any other difference still fails.
        pre_t14 = [(f, [-c[0] + 0.0, *c[1:]], [-n[0] + 0.0, *n[1:]], s) for f, c, n, s in want_wb]
        if got_wb != want_wb and got_wb == pre_t14:
            print("SKIP L3 shipped we_boards == this build's: shipped manifest is stale (pre-T14 un-mirrored boards) "
                  "until the next export, DECISIONS 245 / T53")
            shipped = f"shipped rims {rims} px (we_boards stale until export)"
        else:
            assert got_wb == want_wb, "shipped we_boards differ"
            shipped = f"shipped rims {rims} px"
    print(f"ok L3 blank boards (nitpick C): {[e[0] for e in labels]} blank (rim {mod['L3_TRIM_SRGB']}); "
          f"we_boards {[(b['face'], b['centre'], b['size_m']) for b in wb]}; {shipped}; --no-fine keeps the "
          f"pictograms")


def l3_t60(recipe, mod, table):
    """T60 (2026-10-18, "the small building has no door ... we also need a door on the patio itself. Probably turn one
    of the tall windows into a door."):

    - On the blockout (door kit v1) the two entries exist and bW1 is gone.
    - On a door kit v2 parts build (lodge.build_parts with the --tiled --window-kit v2 --door-kit v2 configs) the
      kiosk door is one chalet leaf and the patio door is two. Each leaf has its lever. The leaf glass is in the
      EXISTING rooms kiosk / bF1W (no new room, no bW1 pane).
    - The two T60_SUB_OBJECTS wall lights sit on a non-glass wall face (a ray from 0.3 m out along the row's facing
      meets a wall_ face at 0.3 +- 0.005 m), clear of their door's casing."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    assert mod["T60_DOORS"], "T60_DOORS is off"
    rows = mod["T60_SUB_OBJECTS"]
    assert len(rows) == 2 and all(r[0] == "WallLightShortRangeRandom01" and r[4] == 0 for r in rows), rows
    ent = sorted(t for t in table if t.startswith(("entry_kio", "entry_bWd")))
    assert ent == ["entry_bWd", "entry_kio"], [t for t in table if t.startswith("entry_")]
    assert not [t for t in table if t.startswith("win_bF1W_bW1")], "bW1 pane left on the blockout"
    args = lodge.parse_args(["--recipe", "L3", "--tiled", "--window-kit", "v2", "--door-kit", "v2"])
    opts = {"tower": True, "roof_windows": "win", "window_kit": lodge.window_kit_cfg(recipe, args),
            "door_kit": lodge.door_kit_cfg(recipe, args)}
    ps = lodge.build_parts(recipe, opts)
    bpy.context.view_layer.update()
    names = [o.name for o, _k in ps]
    try:
        for leaf in ("door_kio_leaf", "door_bWd0_leaf", "door_bWd1_leaf"):
            assert leaf in names and leaf.replace("_leaf", "_lever") in names, (leaf, sorted(
                n for n in names if n.startswith("door_")))
        assert "door_kio0_leaf" not in names and "door_bWd2_leaf" not in names, "leaf count"
        glass = sorted(n for n in names if n.startswith(("win_kiosk_kio", "win_bF1W_bWd")))
        assert glass == ["win_bF1W_bWdg0", "win_bF1W_bWdg1", "win_kiosk_kiog0"], glass
        assert not [n for n in names if n.startswith("win_bF1W_bW1")], "bW1 pane left (kit v2)"
        assert [n for n in names if n.startswith("win_bF1W_bW0")] and [n for n in names if n.startswith("win_bF1W_bW2")]
        # the lamp rows on their wall
        verts, polys, own = [], [], []
        for o, _k in ps:
            W = [o.matrix_world @ v.co for v in o.data.vertices]
            b = len(verts)
            verts += W
            for p in o.data.polygons:
                polys.append([b + i for i in p.vertices])
                own.append(o.name)
        bvh = BVHTree.FromPolygons(verts, polys)
        cas = {}
        for o, _k in ps:
            if o.name in ("entry_kio", "entry_bWd"):
                W = np.array([tuple(o.matrix_world @ v.co) for v in o.data.vertices])
                cas[o.name] = (W.min(0), W.max(0))
        out = []
        for (tag, _p, _yaw), row in zip(mod["T60_LAMPS"], rows):
            gx, gy, gz = row[2]
            x, y, z = gz, gx, gy                                    # game (gx, gy, gz) = authoring (y, z, x)
            th = 2.0 * math.atan2(row[3][1], row[3][3])
            n = Vector((math.cos(th), math.sin(th), 0.0))
            hit, nrm, idx, d = bvh.ray_cast(Vector((x, y, z)) + n * 0.3, -n, 0.6)
            assert hit is not None and abs(d - 0.3) <= 0.005 and own[idx].startswith("wall_") \
                and nrm.dot(n) > 0.95, (tag, None if hit is None else (round(d, 4), own[idx], tuple(nrm)))
            lo, hi = cas[f"entry_{tag}"]
            # the lamp's 0.142 m half width clears the casing along the wall
            u = abs(y - 0.5 * (lo[1] + hi[1])) if abs(n.x) > 0.5 else abs(x - 0.5 * (lo[0] + hi[0]))
            half = 0.5 * ((hi[1] - lo[1]) if abs(n.x) > 0.5 else (hi[0] - lo[0]))
            assert u - 0.142 > half, (tag, u, half)
            out.append((tag, own[idx], round(u - 0.142 - half, 3)))
    finally:
        for o, _k in ps:
            me_ = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if me_ is not None and me_.users == 0:
                bpy.data.meshes.remove(me_)
    print(f"ok L3 T60: kiosk door (1 chalet leaf + lever, glass in kiosk) + patio double door (2 leaves + 2 levers, "
          f"glass in bF1W, bW1 gone) on the kit v2 parts; blockout entries {['entry_kio', 'entry_bWd']}; wall lights "
          f"(tag, wall, clearance to the casing m) {out}")


# the pre-pass chalet's lot bbox (manifest 2026-10-12 00:56). The fine flag masts' ball finials (cs2_bldg.FINIAL_R
# 0.07 vs the mast's 0.05) may push y max (flag_m2 at y 14.0) out by 0.02 m: the one documented H9 allowance
# (build/fine-detail-step3a-buildings.md)
L3_PRE_PASS_LOT = {"x": (-15.755, 15.84), "y": (-19.96, 14.05)}
L3_FINIAL_GROW = 0.07 - 0.05 + 0.001


def l3_fine_h1(recipe, mod, lot):
    """Fine-detail step 3a H1 / H9 on the chalet: every new row at the part it replaced, measured on the PRE-PASS
    chalet's builder parts (`--no-fine`). The checks:

    - each wall light on its wall / column face at the removed lantern's lens (y, z) +-0.01 m (the lantern lens = the
      wall point + 0.22 m along the face normal).
    - the gazebo RoofLight at the removed lantern's top / plan center.
    - the kit racks at SKI_RACK_SPOTS (identity, m_ParentMesh -1, alternating loadouts).
    - the fine flag masts (58 tris on the builder parts, 20 before).
    - host lot bbox = the pre-pass one +-5 mm (H9).

    Negative: a wall light 1 cm off is reported."""
    import fine_detail as FD
    def parts_of(rec):
        ps = lodge.build_parts(rec, {"tower": True, "roof_windows": "win"})
        bpy.context.view_layer.update()
        V = {o.name: np.array([tuple(o.matrix_world @ v.co) for v in o.data.vertices]) for o, _k in ps}
        T = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o, _k in ps}
        for o, _k in ps:                                   # one build at a time: part names are unique
            me_ = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.meshes.remove(me_)
        return V, T
    lp, ltri = parts_of(FD.effective(recipe, fine=False))
    _fv, ftri = parts_of(recipe)

    def lamp_err(row, tag):
        P = lp[f"lamp_{tag}"]
        c = 0.5 * (P.min(0) + P.max(0))                      # lens center (authoring)
        gx, gy, gz = row[2]
        th = 2.0 * math.atan2(row[3][1], row[3][3])
        n = np.array((math.cos(th), math.sin(th), 0.0))      # local +Z (out of the wall)
        want = c - 0.22 * n                                  # cs2_bldg.lantern: lens 0.22 out
        return float(np.abs(np.array((gz, gx, gy)) - want).max())
    walls = [r for r in mod["FINE_SUB_OBJECTS"] if r[0] == "WallLightShortRangeRandom01"]
    tags = [t for t, _p, _y in mod["FINE_LAMPS"]]
    assert len(walls) == len(tags) == 8, (len(walls), tags)
    errs = {t: round(lamp_err(r, t), 4) for t, r in zip(tags, walls)}
    assert max(errs.values()) <= 0.01 and all(r[4] == 0 for r in walls), f"L3 H1 wall lights {errs}"
    bad = list(walls[0])
    bad[2] = (bad[2][0] + 0.01, bad[2][1], bad[2][2])
    assert lamp_err(tuple(bad), tags[0]) > 0.009, "L3 H1 negative: a 1 cm move is not reported"
    roof = [r for r in mod["FINE_SUB_OBJECTS"] if r[0] == "RoofLightRandom01"]
    P = lp["lamp_gz"]
    gx, gy, gz = roof[0][2]
    assert len(roof) == 1 and math.hypot(gz - 0.5 * (P[:, 0].min() + P[:, 0].max()),
                                         gx - 0.5 * (P[:, 1].min() + P[:, 1].max())) <= 0.01 \
        and abs(gy - P[:, 2].max()) <= 0.001, f"L3 H1 roof light {roof[0][2]} vs lamp_gz"
    kit = mod["KIT_ROWS"]
    assert [tuple(e["position"]) for e in kit] == [tuple(float(v) for v in s) for s in mod["SKI_RACK_SPOTS"]] \
        and all(e["m_ParentMesh"] == -1 and tuple(e["rotation"]) == (0.0, 0.0, 0.0, 1.0) for e in kit) \
        and [e["prefab"] for e in kit] == list(FD.AFRAME_RACKS), [(e["prefab"], e["position"]) for e in kit]
    flags = {f"flag_m{k}": ftri.get(f"flag_m{k}") for k in range(3)}
    assert set(flags.values()) == {58} and all(ltri[f] == 20 for f in flags), (flags, ltri)
    for ax in ("x", "y"):
        for i, (a, b) in enumerate(zip(lot[ax], L3_PRE_PASS_LOT[ax])):
            tol = L3_FINIAL_GROW if (ax, i) == ("y", 1) else 0.005
            assert abs(a - b) <= tol, f"L3 H9: lot {ax} {lot[ax]} vs pre-pass {L3_PRE_PASS_LOT[ax]}"
    print(f"ok L3 fine-detail H1 / H9: 8 wall lights at the removed lanterns' lens (y, z) on their face "
          f"(worst {max(errs.values()):.4f} m; 1 cm negative caught), the gazebo RoofLight at the removed "
          f"lantern's top, 3 kit racks at SKI_RACK_SPOTS (identity, lot, V1 / V2 / V3), flag masts "
          f"{flags} (pre-pass 20), lot x {lot['x']} y {lot['y']} = the pre-pass +-5 mm")


# prop boxes (game local): a parasol = pole +-0.30 to 1.80 + canopy tier (union footprint from 1.80,
# recipes/l5_swanky_bottom.PARASOL_*). Any other prop = its union box from 5 mm above the pivot. The trash bin uses
# its Empty / Full variant meshes (+-0.355 x +-0.364, the cached placeholder is +-0.246)
PROP_POLE, PROP_TIER = 0.30, 1.80
PROP_BIN = ((-0.355, 0.0, -0.364), (0.355, 1.036, 0.364))


def prop_boxes(pf, pos, q):
    import cs2_subobj
    (lo, hi), src = cs2_subobj.lookup_bounds(pf, None)
    assert src != "unknown", f"{pf}: no bounds (subobj_bounds.json / dump)"
    if pf == "TrashbinRandom01":
        lo, hi = PROP_BIN
    a = 2.0 * math.atan2(q[1], q[3])
    c, s_ = math.cos(a), math.sin(a)

    def box(l_, h_):
        P = np.array([[x * c + z * s_, y, -x * s_ + z * c] for x in (l_[0], h_[0])
                      for y in (l_[1], h_[1]) for z in (l_[2], h_[2])]) + np.asarray(pos, float)
        return P.min(0), P.max(0)
    if pf == "ParasolRandom01":
        return [("pole",) + box((-PROP_POLE, 0.005, -PROP_POLE), (PROP_POLE, PROP_TIER, PROP_POLE)),
                ("canopy",) + box((lo[0], PROP_TIER, lo[2]), hi)]
    # fine-detail step 3a: a wall prop's box starts 5 mm off its wall (local z), and a ceiling prop's ends 5 mm under
    # its ceiling (local y). The mount face itself is not a clash
    import cs2_liftdress as LD
    mount = (LD.SUBOBJ.get(pf) or {}).get("mount", "ground")
    if mount == "wall":
        return [("body",) + box((lo[0], lo[1], 0.005), hi)]
    if mount == "ceiling":
        return [("body",) + box(lo, (hi[0], -0.005, hi[2]))]
    return [("body",) + box((lo[0], 0.005, lo[2]), hi)]


def props_clear(recipe, obj, table, subs):
    """Assert the prop boxes against the LOD0 triangles, each other and the lot. Return the smallest gap between two
    boxes that may not touch."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    me = obj.data
    pp = face_parts(me)
    verts = [tuple(v.co) for v in me.vertices]
    tris, owner = [], []
    for p in me.polygons:
        vv = list(p.vertices)
        for j in range(1, len(vv) - 1):
            tris.append((vv[0], vv[j], vv[j + 1]))
            owner.append(table[int(pp[p.index]) - 1])
    tree = BVHTree.FromPolygons(verts, tris, all_triangles=True)
    quads = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    btris = [(q_[0], q_[1], q_[2]) for q_ in quads] + [(q_[0], q_[2], q_[3]) for q_ in quads]
    W_, D_ = recipe.lot
    boxes = []
    for k, (pf, _g, pos, q, _par) in enumerate(subs):
        for tag, lo, hi in prop_boxes(pf, pos, q):
            boxes.append((k, pf, tag, lo, hi))
            assert lo[0] >= -4 * W_ and hi[0] <= 4 * W_ and lo[2] >= -4 * D_ and hi[2] <= 4 * D_, \
                f"{pf} {k} {tag}: outside the lot ({lo}, {hi})"
            # game local (gx, gy, gz) -> the post-orient object frame (gx, -gz, gy)
            bv = [Vector((gx, -gz, gy)) for gx in (lo[0], hi[0]) for gy in (lo[1], hi[1]) for gz in (lo[2], hi[2])]
            hit = tree.overlap(BVHTree.FromPolygons(bv, btris, all_triangles=True))
            assert not hit, f"{pf} {k} {tag} at {pos}: lodge triangles inside: {sorted({owner[a] for a, _b in hit})}"
    near = 99.0
    for i, (ka, pa, ta, la, ha) in enumerate(boxes):
        for kb, pb, tb, lb, hb in boxes[i + 1:]:
            if ka == kb:
                continue
            gap = float(np.max(np.maximum(la - hb, lb - ha)))
            own = {pa, pb} == {"ParasolRandom01", "TableSetCommercialRandom01"} and "pole" in (ta, tb) \
                and math.dist(subs[ka][2], subs[kb][2]) < 1.6
            if not own:
                assert gap > 0.0, f"prop boxes overlap: {pa} {ka} {ta} x {pb} {kb} {tb} ({gap:.3f} m)"
                near = min(near, gap)
    return near


def lod_contracts(recipe, mod, r, captured):
    """M8: base mode, lot fit, budgets on LOD1 / LOD2 (meshes captured from lod_chain)."""
    b = recipe.budget
    n0 = r["tris"][0]
    W_, D_ = recipe.lot
    for lvl, (nm, me) in enumerate(captured, start=1):
        V = coords(me, np.eye(4))
        n = sum(len(p.vertices) - 2 for p in me.polygons)
        assert n == r["tris"][lvl], (nm, n, r["tris"][lvl])
        assert np.abs(V[:, 0]).max() <= 4 * D_ + 1e-6 and np.abs(V[:, 1]).max() <= 4 * W_ + 1e-6, nm
        base = r["base"][lvl]
        if recipe.base_mode == "plinth":
            assert int((np.abs(V[:, 2]) <= 0.01).sum()) == 0, f"{nm}: band vertices"
            assert abs(V[:, 2].min() - mod["SUPPORT_Z0"]) <= 0.01, (nm, V[:, 2].min())
            assert base["ok_edges"] == 0, base
        else:
            assert base["verdict"] == "OK" and base["below_verts"] == 0, (nm, base)
        if lvl == 1:
            assert n <= 0.5 * n0, f"{nm}: {n} > 0.5 x {n0}"
            t = r["lod_plan"]["LOD1"]["target"]
            assert abs(n - t) <= 0.25 * t, (nm, n, t)
            if abs(n - b.lod1) > 0.25 * b.lod1:
                warn(f"{nm}: {n} tris vs recipe LOD1 {b.lod1} (plan {r['lod_plan']['LOD1']['note']})")
        if lvl == 2:
            assert n <= b.lod2_cap, f"{nm}: {n} > {b.lod2_cap}"
        lo, hi = V.min(0), V.max(0)
        drift = float(max(np.abs(lo - r["_lo0"]).max(), np.abs(hi - r["_hi0"]).max()))
        if drift > 0.05:
            warn(f"{nm}: bounds drift {drift:.2f} m (M7: WARN for buildings)")
    print(f"ok {recipe.key} LODs: tris {[r['tris'][i] for i in sorted(r['tris'])]}, base "
          f"{[r['base'][i].get('verdict') for i in sorted(r['base'])]}, lot fit on every level")


# ======================================================================================
# Runs
# ======================================================================================

def run(recipe, argv):
    return lodge.run(recipe, lodge.parse_args(argv))


def blockout_pass(recipe, tmp):
    mod = recipe.builder.__globals__
    res = []
    for i in range(2):
        r = run(recipe, ["--recipe", recipe.key, "--blockout",
                         "--out", os.path.join(tmp, f"blk{i}", "B")])
        res.append((r["hashes"], r["cells"], r["cull"]["faces_removed"], r["tris"][0]))
    same = res[0] == res[1]
    print(f"[det] {recipe.key} blockout: run1 {res[0][0]} culled {res[0][2]}, run2 {res[1][0]} "
          f"-> {'SAME' if same else 'DIFFERENT'}")
    assert same, f"{recipe.key}: blockout builds differ"
    obj = bpy.data.objects[r["name"]]
    win = bpy.data.objects[cs2_asset_submesh_name(r["name"])]
    table = common_contracts(recipe, mod, r, obj, win)
    (l2_contracts if recipe.key == "L2" else l3_contracts)(recipe, mod, r, obj, win, table)
    print(f"ok {recipe.key} common: lot margins x {r['lot']['margin_x']} / y {r['lot']['margin_y']}, "
          f"arrival x {r['facing']['arrival_centroid_x']}, rooms {len(r['cells'])}, kept "
          f"{r['area']['kept_m2']} m2 (cap {mod['AREA_CAP_M2']}), sum w2a {r['area']['sum_w2a']}, "
          f"LOD0 {r['tris'][0]} (target {recipe.budget.lod0_target}, cap {recipe.budget.lod0_cap}), "
          f"predicted w=1 px/m @ {recipe.tex}: {r['area']['predicted_ppm_w1'].get(recipe.tex)}")


def cs2_asset_submesh_name(name):
    import cs2_submesh
    return cs2_submesh.submesh_name(name, "Win")


def finish_pass(recipe, tmp):
    mod = recipe.builder.__globals__
    orig = K.lod_chain
    fin = []
    try:
        for i in range(2):
            captured = []

            def spy(obj, name, targets, delete_lists, protect_prefixes=(), tol=0.25):
                lods, infos = orig(obj, name, targets, delete_lists, protect_prefixes, tol)
                for lod in lods:
                    me = lod.data.copy()
                    me.name = lod.name + "Cap"
                    me.use_fake_user = True
                    captured.append((lod.name, me))
                return lods, infos
            K.lod_chain = spy
            r = run(recipe, ["--recipe", recipe.key, "--tex", "1024", "--ao-samples", "8",
                             "--no-validate", "--out", os.path.join(tmp, f"fin{i}", "B")])
            d = r["asset_dir"]
            pngs = {f: sha(os.path.join(d, f)) for f in sorted(os.listdir(d)) if f.endswith(".png")}
            fbx = sorted(f for f in os.listdir(d) if f.endswith(".fbx"))
            name = r["name"]
            expect = sorted([f"{name}.fbx", f"{name}_LOD1.fbx", f"{name}_LOD2.fbx", f"{name}_Win.fbx",
                             f"{name}_LOD1_Win.fbx", f"{name}_LOD2_Win.fbx"])
            assert fbx == expect, fbx
            obj = bpy.data.objects[name]
            # an _Emissive map exactly when a lamp_ part is left (fine-detail step 3a: the fine chalet's
            # lanterns are vanilla rows, so it ships the four maps only)
            lamps = any(n.startswith("lamp_") for n in json.loads(obj.data[cs2_asset.PART_TABLE_PROP]))
            assert len(pngs) == (5 if lamps else 4) and \
                any(f.endswith("_Emissive.png") for f in pngs) == lamps, (sorted(pngs), lamps)
            assert len(captured) == 2, captured
            V = coords(obj)
            r["_lo0"], r["_hi0"] = V.min(0), V.max(0)
            lod_contracts(recipe, mod, r, captured)
            fin.append((pngs, r["hashes"], r["tris"]))
            for _n, me in captured:
                me.use_fake_user = False
    finally:
        K.lod_chain = orig
    for key, label in ((0, "PNGs"), (1, "mesh hashes"), (2, "tris")):
        same = fin[0][key] == fin[1][key]
        print(f"[det] {recipe.key} finish {label}: run1 {fin[0][key]} run2 {fin[1][key]} -> "
              f"{'SAME' if same else 'DIFFERENT'}")
        assert same, f"{recipe.key}: finish {label} differ"
    return fin[0]


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    quick = "--quick" in argv
    t0 = time.perf_counter()
    recipes = R.load_recipes()
    tmp = tempfile.mkdtemp(prefix="lodges_basic_")
    try:
        for key in KEYS:
            recipe = R.get_recipe(key, recipes)
            blockout_pass(recipe, os.path.join(tmp, key))
            if not quick:
                finish_pass(recipe, os.path.join(tmp, key))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print(f"PASS test_lodges_basic: {list(KEYS)} {'blockout' if quick else 'blockout + finish'} "
          f"deterministic, contracts C1-C13 + LOD contracts, {len(WARNS)} WARN "
          f"({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_lodges_basic")
        sys.exit(1)
