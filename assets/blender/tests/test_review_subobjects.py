"""
Vanilla-prop stand-ins in the live-review packs (DECISIONS 183: props are REFERENCED as
sub-objects, never modeled, so the review shows them as labeled boxes).

  A. READER: for every shipped manifest under assets/import, cs2_subobj.rows_from_manifest returns
     exactly as many rows as the manifest's own block holds (counted here by hard-coded paths,
     independent of the reader's generic search): sub_objects.objects (lodges, school, patrol),
     detail1.sub_objects.objects (DQ stations), detail1.sub_objects.rows (tram stations),
     night_subobjects (lattice towers). Every GUID has known bounds (cs2_liftdress.SUBOBJ or the
     assets/blender/subobj_bounds.json cache), and the cache agrees with SUBOBJ.
  B. FRAMES: every shipped row's stand-in box equals cs2_liftdress.subobj_box(...) (the generators'
     own box test) mapped to the net-oriented pack frame (-gx, -gz, gy) (x-sign gate N, 2026-10-16: was (gx, -gz, gy)); the tram rows' recorded
     `box` too.
  C. PACK: a synthetic asset with N rows in three block shapes (+ one unknown prop) is written with
     cs2_review.write_pack: N boxes + N text labels in SUB with the right bounds, positions,
     rotations, family colors (distinct), semi-transparent material, a "(ground)" tag for
     m_ParentMesh -1, a 0.5 m cube and "?" for unknown bounds, SUB_NOPROPS holding the sub-meshes
     only, collection props (n_prop_standins ...), a night_subobjects WarningLight replaced by the
     beacon addendum row on a beacon host, and the build session left exactly as it was (every ID
     collection, fonts included). A pack without rows is unchanged (four roles, no SUB_NOPROPS).
  D. LIVE: review_live.py in a fresh scene: load() / status() / stand_ins() report the count per
     pack, props() swaps the SUB instance to SUB_NOPROPS and back (the sub-meshes stay), reload()
     keeps the state, a pack without rows reports 0.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_review_subobjects.py

Exit 1 on any failure; one PASS line on success. Writes only to a temp folder.
"""
import json
import math
import os
import shutil
import sys
import tempfile
import time

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(HERE)
sys.path.insert(0, LIB)
import cs2_liftdress as LD   # noqa: E402
import cs2_review            # noqa: E402
import cs2_subobj as S       # noqa: E402
import fine_detail as FD     # noqa: E402

REVIEW_LIVE = os.path.join(LIB, "review_live.py")
FAILS = []
TOL = 1e-6
Q_YAW90 = (0.0, 0.7071068, 0.0, 0.7071068)       # facing +X (cs2_liftdress.FACING_QUAT)
GUID_ACVENT = "b99325ff4ff4dfd459734d8f765c2597"   # not in SUBOBJ: the JSON cache only
ACVENT_BOUNDS = ((-0.187, -0.196, 0.0), (0.187, 0.284, 0.247))
UNKNOWN_GUID = "f" * 32


def check(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"[subobj-test] FAIL: {msg}")
    return cond


def near(a, b, tol=TOL):
    return len(a) == len(b) and all(abs(float(x) - float(y)) <= tol for x, y in zip(a, b))


def pack_aabb_from_game(lo, hi):
    """A game-frame AABB -> the pack frame (-gx, -gz, gy): the game negates FBX x (x-sign gate N)."""
    return ([-hi[0], -hi[2], lo[1]], [-lo[0], -lo[2], hi[1]])


# --------------------------------------------------------------------------------------
# A + B: every shipped manifest
# --------------------------------------------------------------------------------------

def expected_count(m):
    """The row count a manifest's own block holds, by explicit path (not the reader's search)."""
    n = 0
    so = m.get("sub_objects")
    if isinstance(so, dict) and so.get("objects"):
        n += len(so["objects"])
    d1 = (m.get("detail1") or {}).get("sub_objects") if isinstance(m.get("detail1"), dict) else None
    if isinstance(d1, dict):
        if d1.get("objects"):
            n += len(d1["objects"])
        elif isinstance(d1.get("block"), dict) and d1["block"].get("objects"):
            n += len(d1["block"]["objects"])
        elif d1.get("rows"):
            n += len(d1["rows"])
    r1 = (m.get("rev1") or {}).get("sub_objects") if isinstance(m.get("rev1"), dict) else None
    if not n and isinstance(r1, dict) and r1.get("objects"):
        n += len(r1["objects"])
    if isinstance(m.get("night_subobjects"), list):
        n += len(m["night_subobjects"])
    return n


def test_shipped_manifests():
    cache = S._json_cache()
    table = LD.SUBOBJ
    for g, e in cache.items():
        for pf, s in table.items():
            if s["guid"] == g:
                dlo = max(abs(a - b) for a, b in zip(e["bounds"][0], s["bounds"][0]))
                dhi = max(abs(a - b) for a, b in zip(e["bounds"][1], s["bounds"][1]))
                check(max(dlo, dhi) < 0.0015, f"cache {pf} bounds differ from SUBOBJ by {max(dlo, dhi)}")
    seen = 0
    total = 0
    for dp, _dn, fns in os.walk(S.IMPORT_ROOT):
        for fn in sorted(fns):
            if not fn.endswith(".manifest.json"):
                continue
            with open(os.path.join(dp, fn), "r", encoding="utf-8") as fh:
                m = json.load(fh)
            name = fn[:-len(".manifest.json")]
            want = expected_count(m)
            rows = S.rows_from_manifest(m)
            check(len(rows) == want, f"{name}: reader found {len(rows)} rows, manifest holds {want}")
            if not rows:
                continue
            seen += 1
            total += len(rows)
            tram_boxes = None
            d1 = (m.get("detail1") or {}).get("sub_objects") if isinstance(m.get("detail1"), dict) else None
            if isinstance(d1, dict) and d1.get("rows") and "box" in d1["rows"][0]:
                tram_boxes = [r.get("box") for r in d1["rows"]]
            for i, r in enumerate(rows):
                tag = f"{name} row {i} {r['prefab']}"
                bounds, src = S.lookup_bounds(r["prefab"], r["guid"])
                bounds, src = S.lookup_bounds(r["prefab"], r["guid"], r.get("own_bounds"))
                want_src = ("own", "cache") if r.get("kind") == "mod" else ("SUBOBJ", "cache")
                check(src in want_src, f"{tag}: bounds from {src}, expected {want_src}")
                check(len(r["pos"]) == 3 and len(r["rot"]) == 4 and r["parent"] in (-1, 0),
                      f"{tag}: malformed row {r}")
                corners = S.box_corners_pack(bounds, r["pos"], r["rot"])
                lo, hi = S.aabb(corners)
                if r["prefab"] in table:
                    glo, ghi = LD.subobj_box(r["prefab"], r["pos"], r["rot"], frame="game")
                    elo, ehi = pack_aabb_from_game(glo, ghi)
                    check(near(lo, elo) and near(hi, ehi),
                          f"{tag}: stand-in AABB {lo} {hi} != subobj_box mapped {elo} {ehi}")
                # fine-detail step 3c: a tram block lists its kit "mod" rows after the vanilla C16 rows (which carry
                # the recorded boxes), so only the first len(tram_boxes) rows have one
                if tram_boxes and i < len(tram_boxes) and tram_boxes[i]:
                    blo, bhi = tram_boxes[i]
                    if "NEGATED" in str(((d1.get("block") or {}).get("x_convention")) or ""):
                        # the recorded boxes stay in the DESIGN frame; the rows (and the pack) are game frame, gx negated
                        blo, bhi = (-bhi[0], blo[1], blo[2]), (-blo[0], bhi[1], bhi[2])
                    elo, ehi = pack_aabb_from_game(blo, bhi)
                    check(near(lo, elo, 0.002) and near(hi, ehi, 0.002),
                          f"{tag}: AABB {lo} {hi} != the manifest's recorded box mapped {elo} {ehi}")
    check(seen >= 8 and total >= 80, f"only {seen} manifests / {total} rows had sub-objects")
    # the beacon addendum: one row per host tower, own bounds, 60A's WarningLight replaced
    bm = S._beacon_manifest()
    check(bm is not None, "beacon manifest unreadable")
    if bm:
        hosts = [r["mesh"] for r in bm["hosting"]["rows"]]
        check(len(hosts) == 7, f"beacon hosts {hosts}")
        for h in hosts:
            b = S.beacon_rows(h)
            check(len(b) == 1 and b[0]["own_bounds"][1][1] > 0.4, f"{h}: beacon rows {b}")
    print(f"[subobj-test] A/B: {seen} shipped manifests, {total} rows read; every stand-in AABB equals "
          f"the generators' own box test in the pack frame")


# --------------------------------------------------------------------------------------
# C: a synthetic pack
# --------------------------------------------------------------------------------------

def snapshot():
    return {a: sorted(i.name for i in getattr(bpy.data, a)) for a in cs2_review._ID_COLLECTIONS}


def make_lod0(name):
    me = bpy.data.meshes.new(name)
    v = [(x, y, z) for x in (-1.0, 1.0) for y in (-1.0, 1.0) for z in (0.0, 2.0)]
    me.from_pydata(v, [], [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)])
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def make_win(name):
    me = bpy.data.meshes.new(name + "Win")
    me.from_pydata([(0, 0, 0), (1, 0, 0), (1, 0, 1)], [], [(0, 1, 2)])
    me.update()
    ob = bpy.data.objects.new(name + "Win", me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


GUIDS = {pf: s["guid"] for pf, s in LD.SUBOBJ.items()}


def synthetic_manifest():
    return {
        "sub_objects": {"target_prefab": "Synth", "frame": "game local", "objects": [
            {"m_Object": GUIDS["TrashbinRandom01"], "prefab": "TrashbinRandom01",
             "position": [1.0, 2.0, 3.0], "rotation": [0, 0, 0, 1], "m_ParentMesh": 0},
            {"m_Object": GUIDS["GardenBenchRandom01"], "prefab": "GardenBenchRandom01",
             "position": [0.0, 0.5, 0.0], "rotation": list(Q_YAW90), "m_ParentMesh": -1},
            {"m_Object": GUID_ACVENT, "prefab": "ACVentWallRandom01",
             "position": [5.0, 3.5, -4.3], "rotation": [0, 0, 0, 1], "m_ParentMesh": 0},
            {"m_Object": UNKNOWN_GUID, "prefab": "NoSuchPropRandom01",
             "position": [-4.0, 0.0, 6.0], "rotation": [0, 0, 0, 1], "m_ParentMesh": 0},
            {"m_Object": GUIDS["TableSetCommercialRandom01"], "prefab": "TableSetCommercialRandom01",
             "position": [7.0, 0.43, -2.0], "rotation": [0, 0, 0, 1], "m_ParentMesh": 0}]},
        "night_subobjects": [
            {"prefab": "WarningLightRandom01", "guid": GUIDS["WarningLightRandom01"],
             "position_net_m": [0, 59.96, 0], "rotation_xyzw": [0, 0, 0, 1], "parent_mesh": 0,
             "host": "gantryx_1"}],
    }


def inspect_pack(path, name, n_expected, label):
    coll_name = f"{cs2_review.PACK_PREFIX}{name}"
    with bpy.data.libraries.load(path, link=True, relative=False) as (src, dst):
        check(coll_name in src.collections, f"{label}: pack holds {list(src.collections)}")
        dst.collections = [coll_name]
    pack = dst.collections[0]
    lib = pack.library
    out = {}
    try:
        roles = {(c.get("cs2r_role") or c.name): c for c in pack.children}
        out["roles"] = sorted(roles)
        sub = roles["SUB"]
        boxes = sorted([o for o in sub.objects if o.get("cs2r_prop") == "box"], key=lambda o: o["cs2r_prop_index"])
        labels = sorted([o for o in sub.objects if o.get("cs2r_prop") == "label"],
                        key=lambda o: (o["cs2r_prop_index"], o["cs2r_label_face"]))
        check(len(boxes) == n_expected and len(labels) == n_expected * S.LABEL_FACES,
              f"{label}: {len(boxes)} boxes / {len(labels)} labels, expected {n_expected} boxes x {S.LABEL_FACES} label faces")
        check(int(pack.get("n_prop_standins", -1)) == n_expected,
              f"{label}: n_prop_standins {pack.get('n_prop_standins')} != {n_expected}")
        out.update(pack=pack, sub=sub, boxes=boxes, labels=labels, roles_map=roles,
                   props={k: pack[k] for k in pack.keys() if k != "cs2r_meta_json"})
        return out, lib
    except Exception:
        bpy.data.libraries.remove(lib)
        raise


def test_synthetic_pack(work):
    name = "SkiSynthPropsA"
    asset_dir = os.path.join(work, "asset", name)
    os.makedirs(asset_dir)
    review_dir = os.path.join(work, "review")
    lod0 = make_lod0(name)
    win = make_win(name)
    before = snapshot()
    path = cs2_review.write_pack(name, asset_dir, {0: lod0}, {"Win": win},
                                 meta={"generator": "test", "kind": "building"}, out_dir=review_dir,
                                 manifest=synthetic_manifest())
    after = snapshot()
    check(before == after, "write_pack changed the build session: "
          + str({a: (len(before[a]), len(after[a])) for a in before if before[a] != after[a]}))
    check(not os.listdir(asset_dir), f"write_pack wrote into the asset folder: {os.listdir(asset_dir)}")
    n = 6
    got, lib = inspect_pack(path, name, n, "synthetic")
    try:
        check(got["roles"] == sorted(list(cs2_review.ROLES) + ["SUB_NOPROPS"]),
              f"children {got['roles']}")
        noprops = got["roles_map"]["SUB_NOPROPS"]
        check([o.name for o in noprops.objects] == [f"CS2R_{name}_Win"],
              f"SUB_NOPROPS holds {[o.name for o in noprops.objects]}")
        check(not [o for o in noprops.objects if o.get("cs2r_prop")], "SUB_NOPROPS holds stand-ins")
        check(sorted(o.name for o in got["sub"].objects if not o.get("cs2r_prop")) == [f"CS2R_{name}_Win"],
              "SUB lost its `_Win` sub-mesh")
        rows = [  # (prefab, bounds, pos, rot, label, family, parent, bounds source)
            ("TrashbinRandom01", LD.SUBOBJ["TrashbinRandom01"]["bounds"], (1.0, 2.0, 3.0), (0, 0, 0, 1),
             "Trashbin", "bins", 0, "SUBOBJ"),
            ("GardenBenchRandom01", LD.SUBOBJ["GardenBenchRandom01"]["bounds"], (0.0, 0.5, 0.0), Q_YAW90,
             "GardenBench (ground)", "seating", -1, "SUBOBJ"),
            ("ACVentWallRandom01", ACVENT_BOUNDS, (5.0, 3.5, -4.3), (0, 0, 0, 1), "ACVentWall", "other", 0,
             "cache"),
            ("NoSuchPropRandom01", ((-0.25, 0.0, -0.25), (0.25, 0.5, 0.25)), (-4.0, 0.0, 6.0), (0, 0, 0, 1),
             "NoSuchProp ?", "other", 0, "unknown"),
            ("TableSetCommercialRandom01", LD.SUBOBJ["TableSetCommercialRandom01"]["bounds"],
             (7.0, 0.43, -2.0), (0, 0, 0, 1), "TableSetCommercial", "seating", 0, "SUBOBJ"),
            ("WarningLightRandom01", LD.SUBOBJ["WarningLightRandom01"]["bounds"], (0.0, 59.96, 0.0),
             (0, 0, 0, 1), "WarningLight", "lights", 0, "SUBOBJ"),
        ]
        mats = {}
        for i, (pf, bounds, pos, rot, text, fam, parent, bsrc) in enumerate(rows):
            box = got["boxes"][i]
            labs = got["labels"][i * S.LABEL_FACES:(i + 1) * S.LABEL_FACES]
            lab = labs[0]
            tag = f"row {i} {pf}"
            check(box.name == f"CS2R_{name}_prop{i:02d}" and lab.name == box.name + "_label"
                  and [lb.name for lb in labs[1:]] == [box.name + f"_label{k}" for k in range(1, S.LABEL_FACES)],
                  f"{tag}: names {box.name} / {[lb.name for lb in labs]}")
            check(sorted(c.name for c in box.children) == sorted(lb.name for lb in labs), f"{tag}: labels not parented to the box")
            yaws = [round(math.degrees(lb.rotation_euler[2]) % 360.0, 3) for lb in labs]
            check(all(abs((yaws[k] - yaws[0]) % 360.0 - 360.0 / S.LABEL_FACES * k) < 1e-3 for k in range(S.LABEL_FACES)),
                  f"{tag}: label faces not 90 degrees apart {yaws}")
            xs = [tuple(box.matrix_world @ v.co) for v in box.data.vertices]
            lo, hi = S.aabb(xs)
            if bsrc == "unknown":
                # a 0.5 m cube standing on its position
                check(near(lo, (-pos[0] - 0.25, -pos[2] - 0.25, pos[1])) and near(hi, (-pos[0] + 0.25, -pos[2] + 0.25, pos[1] + 0.5)),
                      f"{tag}: unknown-bounds cube {lo} {hi}")
            else:
                glo, ghi = LD.subobj_box(pf, pos, rot, frame="game") if pf in LD.SUBOBJ else \
                    (None, None)
                if glo is None:                       # the cache-only prop: shift the table bounds by hand
                    glo, ghi = ([pos[i] + bounds[0][i] for i in range(3)], [pos[i] + bounds[1][i] for i in range(3)])
                elo, ehi = pack_aabb_from_game(glo, ghi)
                check(near(lo, elo, 1e-5) and near(hi, ehi, 1e-5), f"{tag}: AABB {lo} {hi} != {elo} {ehi}")
            check(len(box.data.vertices) == 8 and len(box.data.polygons) == 6, f"{tag}: not a box")
            check(all(abs(lb.data.size - 0.30) < 1e-6 for lb in labs) and abs(box["cs2r_label_size_m"] - 0.30) < 1e-9,
                  f"{tag}: label size {lab.data.size} (a 3.5 m pack keeps the 0.30 m minimum)")
            lm = labs[0].data.materials[0]
            check(lm.name == f"CS2R_{name}_PropText_{fam}" and any(n.type == "NEW_GEOMETRY" for n in lm.node_tree.nodes)
                  and lm.use_backface_culling, f"{tag}: label material {lm.name} does not hide its back")
            vol_ok = True
            c = [sum(v[k] for v in xs) / 8.0 for k in range(3)]
            for pl in box.data.polygons:                  # outward normals
                n_ = pl.normal
                mid = [sum(xs[j][k] for j in pl.vertices) / len(pl.vertices) - c[k] for k in range(3)]
                vol_ok &= (n_[0] * mid[0] + n_[1] * mid[1] + n_[2] * mid[2]) > 0
            check(vol_ok, f"{tag}: inward-facing faces")
            check(all(lb.type == "FONT" and lb.data.body == text for lb in labs), f"{tag}: labels {[(lb.type, getattr(lb.data, 'body', None)) for lb in labs]} != {text!r}")
            check(all(lb.location.z >= hi[2] and abs(lb.location.x - (lo[0] + hi[0]) / 2) < 1e-5
                      and abs(lb.location.y - (lo[1] + hi[1]) / 2) < 1e-5 for lb in labs),
                  f"{tag}: labels not above the box ({[tuple(lb.location) for lb in labs]})")
            check(box["cs2r_family"] == fam and box["cs2r_prefab"] == pf and box["cs2r_parent_mesh"] == parent
                  and box["cs2r_bounds_src"] == bsrc and box["cs2r_role"] == "SUB",
                  f"{tag}: props family {box['cs2r_family']} parent {box['cs2r_parent_mesh']} src {box['cs2r_bounds_src']}")
            check(near(list(box["cs2r_pos_game"]), pos) and near(list(box["cs2r_rot_game"]), rot, 1e-6),
                  f"{tag}: pos/rot props")
            mat = box.data.materials[0]
            mats.setdefault(fam, mat)
            check(mats[fam] == mat and mat.name == f"CS2R_{name}_Prop_{fam}", f"{tag}: material {mat.name}")
            alpha = mat.node_tree.nodes["Principled BSDF"].inputs["Alpha"].default_value
            check(abs(alpha - S.STANDIN_ALPHA) < 1e-6 and abs(mat.diffuse_color[3] - S.STANDIN_ALPHA) < 1e-6,
                  f"{tag}: not semi-transparent ({alpha})")
        cols = {f: tuple(round(c, 3) for c in m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value[:3])
                for f, m in mats.items()}
        check(len(set(cols.values())) == len(cols) == 4, f"family colors not distinct: {cols}")
        p = got["props"]
        fams = json.loads(p["prop_families"])
        check(fams == {"bins": 1, "seating": 2, "other": 2, "lights": 1}, f"prop_families {fams}")
        check(int(p["prop_bounds_unknown"]) == 1, f"prop_bounds_unknown {p['prop_bounds_unknown']}")
        check(p.get("subobj_source") == "arg", f"subobj_source {p.get('subobj_source')!r}")
        meta = json.loads(got["pack"]["cs2r_meta_json"])
        check(len(meta.get("sub_objects", [])) == n, "cs2r_meta_json lacks the sub_objects rows")
        check(S.label_size(None) == 0.30 and abs(S.label_size(30.0) - 0.48) < 1e-9 and S.label_size(1000.0) == 0.60
              and S.label_size(5.0) == 0.30, "label_size scaling")
        check(int(p["tris_LOD0"]) == 12, f"tris_LOD0 {p['tris_LOD0']} (the stand-ins must not count)")
        check(list(p["bbox_max"])[2] == 2.0, f"bbox includes the stand-ins: {list(p['bbox_max'])}")
    finally:
        bpy.data.libraries.remove(lib)

    # the beacon host: a night_subobjects WarningLight at the beacon spot is replaced by the beacon row
    bname = "SkiTramTower60A"
    bdir = os.path.join(work, "asset", bname)
    os.makedirs(bdir)
    lod_b = make_lod0(bname)
    man = {"night_subobjects": [{"prefab": "WarningLightRandom01", "guid": GUIDS["WarningLightRandom01"],
                                 "position_net_m": [0, 59.96, 0], "rotation_xyzw": [0, 0, 0, 1],
                                 "parent_mesh": 0, "host": "gantryx_1"}]}
    path_b = cs2_review.write_pack(bname, bdir, {0: lod_b}, {}, meta={"kind": "tower"}, out_dir=review_dir,
                                   manifest=man)
    got_b, lib_b = inspect_pack(path_b, bname, 1, "beacon host")
    try:
        bx = got_b["boxes"][0]
        check(bx["cs2r_prefab"] == "SkiBeaconRed" and bx["cs2r_own_prop"] == 1 and bx["cs2r_family"] == "lights"
              and bx["cs2r_bounds_src"] == "own", f"beacon row props {dict(bx.items())}")
        xs = [tuple(bx.matrix_world @ v.co) for v in bx.data.vertices]
        lo, hi = S.aabb(xs)
        check(near(lo, (-0.13, -0.13, 59.96), 1e-5) and near(hi, (0.13, 0.13, 59.96 + 0.4508), 1e-5),
              f"beacon AABB {lo} {hi}")
        check("WarningLight" in str(got_b["props"].get("subobj_replaced", "")),
              f"subobj_replaced {got_b['props'].get('subobj_replaced')!r}")
        check(all(lb.data.body == "SkiBeaconRed" for lb in got_b["labels"]) and len(got_b["labels"]) == S.LABEL_FACES,
              f"beacon labels {[lb.data.body for lb in got_b['labels']]}")
    finally:
        bpy.data.libraries.remove(lib_b)

    # a pack WITHOUT rows is unchanged: four roles, empty SUB, no SUB_NOPROPS, count 0
    plain = "SkiSynthPlainA"
    pdir = os.path.join(work, "asset", plain)
    os.makedirs(pdir)
    lod_p = make_lod0(plain)
    before = snapshot()
    path_p = cs2_review.write_pack(plain, pdir, {0: lod_p}, {}, meta={"kind": "prop"}, out_dir=review_dir)
    check(before == snapshot(), "write_pack (no rows) changed the build session")
    with bpy.data.libraries.load(path_p, link=True, relative=False) as (src, dst):
        dst.collections = [f"CS2R_{plain}"]
    pk = dst.collections[0]
    try:
        check(sorted((c.get("cs2r_role") or c.name) for c in pk.children) == sorted(cs2_review.ROLES),
              f"plain pack children {[c.name for c in pk.children]}")
        sub = next(c for c in pk.children if c.get("cs2r_role") == "SUB")
        check(len(sub.objects) == 0 and int(pk.get("n_prop_standins", -1)) == 0, "plain pack has stand-ins")
    finally:
        bpy.data.libraries.remove(pk.library)

    # a broken block never loses the pack
    bad = "SkiSynthBadA"
    bdir2 = os.path.join(work, "asset", bad)
    os.makedirs(bdir2)
    lod_x = make_lod0(bad)
    path_x = cs2_review.write_pack(bad, bdir2, {0: lod_x}, {}, out_dir=review_dir,
                                   manifest={"sub_objects": {"objects": [{"prefab": "X"}, "junk", None]}})
    check(os.path.isfile(path_x), "a malformed sub_objects block lost the pack")
    print(f"[subobj-test] C: 6 rows in 3 block shapes -> 6 boxes + 6 labels, bounds / positions / "
          f"rotations / families / unknown cube / beacon replacement / SUB_NOPROPS / no session change OK")
    return review_dir, name, plain



# --------------------------------------------------------------------------------------
# E: our own props by CID ("kind": "mod", fine-detail pass) + the sign set as an extra mesh
# --------------------------------------------------------------------------------------

KIT_CID = "e8c76b0b9f9340a08f728e6d992d4692"      # SkiDressHoseReel (fine_detail.KIT; uncolored: the cyan box)
SET_CID = "1" * 32
#: kit color variants (2026-10-12): a colored rack row names its variant and is TINTED with the set
RED_VARIANT = "SkiDressSkiRackV2_Red"


def test_mod_pack(work, review_dir):
    name = "SkiSynthModA"
    asset_dir = os.path.join(work, "asset", name)
    os.makedirs(asset_dir)
    lod0 = make_lod0(name)
    sme = bpy.data.meshes.new(name + "Set")
    sme.from_pydata([(-1.0, -1.01, 0.5), (1.0, -1.01, 0.5), (1.0, -1.01, 1.5), (-1.0, -1.01, 1.5)], [],
                    [(0, 1, 2, 3)])
    sme.update()
    sme.uv_layers.new(name="UVMap")
    sobj = bpy.data.objects.new(name + "Set", sme)               # never linked, like lodge's sign set
    man = {"sub_objects": {"target_prefab": "Synth", "objects": [
        {"m_Object": GUIDS["TrashbinRandom01"], "prefab": "TrashbinRandom01",
         "position": [1.0, 0.0, 3.0], "rotation": [0, 0, 0, 1], "m_ParentMesh": -1},
        {"kind": "mod", "ref": "CID", "m_Object": KIT_CID, "prefab": "SkiDressHoseReel",
         "mesh": "SkiDressHoseReelB", "position": [-2.0, 0.0, 0.5], "rotation": [0, 0, 0, 1], "m_ParentMesh": -1},
        {"kind": "mod", "ref": "CID", "m_Object": FD.variant_cids(RED_VARIANT)[0], "prefab": "SkiDressSkiRackV2",
         "mesh": "SkiDressSkiRackV2B", "position": [2.0, 0.0, 0.5], "rotation": [0, 0, 0, 1], "m_ParentMesh": -1,
         "colour_set": "red", "variant": RED_VARIANT},
        {"kind": "mod", "ref": "CID", "m_Object": SET_CID, "prefab": "SkiSignSetSynth", "mesh": "SkiSignSetSynthB",
         "role": "signset", "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "m_ParentMesh": 0,
         "bounds": [[-1.0, 0.5, 1.01], [1.0, 1.5, 1.01]]}]}}
    rows = S.rows_from_manifest(man)
    check(len(rows) == 4 and rows[1]["kind"] == "mod" and rows[1]["guid"] is None and rows[1]["cid"] == KIT_CID
          and rows[2]["colour_set"] == "red" and rows[2]["variant"] == RED_VARIANT
          and rows[3]["role"] == "signset" and rows[3]["own_bounds"][1][1] == 1.5,
          f"E reader: mod rows {rows}")
    (lo, hi), src = S.lookup_bounds("SkiDressSkiRackV2", None)
    check(src == "cache" and abs(hi[0] - 0.85) < 1e-6, f"E kit bounds from {src}: {lo} {hi}")
    before = snapshot()
    path = cs2_review.write_pack(name, asset_dir, {0: lod0}, {}, meta={"kind": "building"},
                                 out_dir=review_dir, manifest=man,
                                 extra_meshes={"SignSet": (sobj, "SkiSignSetSynthB", asset_dir)})
    check(before == snapshot(), "E write_pack (mod rows + extra mesh) changed the build session")
    got, lib = inspect_pack(path, name, 3, "mod rows")
    try:
        mods = [b for b in got["boxes"] if b.get("cs2r_kind") == "mod"]
        kit = [b for b in mods if not b.get("cs2r_colour_set")]
        check(len(kit) == 1 and kit[0]["cs2r_family"] == "mod" and kit[0]["cs2r_cid"] == KIT_CID
              and kit[0]["cs2r_bounds_src"] == "cache" and kit[0]["cs2r_mesh"] == "SkiDressHoseReelB",
              f"E kit box {[dict(b.items()) for b in kit]}")
        labs = [lb for lb in got["labels"] if lb["cs2r_prop_index"] == kit[0]["cs2r_prop_index"]]
        check(all(lb.data.body == "SkiDressHoseReel (ground)" for lb in labs), "E kit label = the full prop name")
        # kit color variants: the colored rack is tinted with its set and labeled with its variant
        red = [b for b in mods if b.get("cs2r_colour_set")]
        rcol = tuple(round(c, 3) for c in red[0].data.materials[0].node_tree.nodes["Principled BSDF"]
                     .inputs["Base Color"].default_value[:3]) if red else None
        rlabs = [lb for lb in got["labels"] if red and lb["cs2r_prop_index"] == red[0]["cs2r_prop_index"]]
        check(len(red) == 1 and red[0]["cs2r_family"] == "mod" and red[0]["cs2r_colour_set"] == "red"
              and red[0]["cs2r_variant"] == RED_VARIANT and red[0].data.materials[0].name == f"CS2R_{name}_Prop_mod_red"
              and rcol == tuple(round(c, 3) for c in S.colour_set_rgb("red"))
              and all(lb.data.body == f"{RED_VARIANT} (ground)" for lb in rlabs)
              and rlabs and rlabs[0].data.materials[0].name == f"CS2R_{name}_PropText_mod_red",
              f"E tinted kit box: color {rcol} (want {S.colour_set_rgb('red')}), label / material by set")
        check(rcol != tuple(round(c, 3) for c in S.FAMILY_RGB["mod"]), "E the tinted box is not the cyan mod color")
        col = tuple(round(c, 3) for c in kit[0].data.materials[0].node_tree.nodes["Principled BSDF"]
                    .inputs["Base Color"].default_value[:3])
        others = {tuple(round(c, 3) for c in v) for f, v in S.FAMILY_RGB.items() if f != "mod"}
        check(col == tuple(round(c, 3) for c in S.FAMILY_RGB["mod"]) and col not in others,
              f"E mod color {col} distinct from the vanilla families")
        check(not [b for b in got["boxes"] if b.get("cs2r_prefab") == "SkiSignSetSynth"], "E a signset row got a box")
        ss = [o for o in got["sub"].objects if o.get("cs2r_extra") == "SkiSignSetSynthB"]
        check(len(ss) == 1 and ss[0].name == f"CS2R_{name}_SignSet", f"E sign-set mesh in SUB {[o.name for o in ss]}")
        nop = got["roles_map"]["SUB_NOPROPS"]
        check([o.name for o in nop.objects] == [f"CS2R_{name}_SignSet"], f"E SUB_NOPROPS keeps the sign set "
                                                                         f"{[o.name for o in nop.objects]}")
        p_ = got["props"]
        check(p_.get("extra_meshes") == "SignSet:SkiSignSetSynthB" and "SkiSignSetSynth" in p_.get("signset_rows", ""),
              f"E props extra_meshes {p_.get('extra_meshes')} signset_rows {p_.get('signset_rows')}")
    finally:
        bpy.data.libraries.remove(lib)
        bpy.data.objects.remove(sobj, do_unlink=True)
        bpy.data.meshes.remove(sme)
    print("[subobj-test] E: a kit row by CID -> one cyan box labeled with the prop name (cache bounds), a "
          "signset row -> no box + the real sign-set mesh in SUB and SUB_NOPROPS, session untouched")


# --------------------------------------------------------------------------------------
# D: review_live
# --------------------------------------------------------------------------------------

def test_live(review_dir, name, plain):
    ns = {"__name__": "review_live_props_test"}
    with open(REVIEW_LIVE, encoding="utf-8") as fh:
        src = fh.read()
    check(src.isascii(), "review_live.py is not ASCII")
    exec(compile(src, REVIEW_LIVE, "exec"), ns)

    def call(fn, *a, **kw):
        res = ns[fn](*a, **kw)
        check(isinstance(res, dict) and res.get("ok"), f"{fn}{a} -> {res}")
        return res

    call("ensure_scene", lighting=None)
    r = call("load", name, review_dir=review_dir)
    check(r.get("props") == 6, f"load props {r.get('props')}")
    r2 = call("load", plain, review_dir=review_dir)
    check(r2.get("props") == 0, f"plain load props {r2.get('props')}")
    st = call("status")
    check(st["loaded"][name]["props"] == 6 and st["loaded"][name]["props_shown"] is True
          and st["loaded"][plain]["props"] == 0 and st["loaded"][plain]["props_shown"] is False,
          f"status {st['loaded']}")
    si = call("stand_ins", review_dir=review_dir)
    check(si["prop_standins"][name] == {"count": 6, "shown": True} and si["prop_standins"][plain]["count"] == 0,
          f"stand_ins prop_standins {si['prop_standins']}")
    sub_e = bpy.data.objects[f"CS2R_{name}_SUB"]

    def visible_props():
        return sorted(o.name for o in sub_e.instance_collection.all_objects if o.get("cs2r_prop") == "box")

    check(sub_e.instance_collection.name == "SUB" and len(visible_props()) == 6, "props not shown after load")
    r = call("props", name)                                 # toggle off
    check(r["props"][name] == {"count": 6, "shown": False, "instance": "SUB_NOPROPS"}
          and sub_e.instance_collection.name == "SUB_NOPROPS" and not visible_props(), f"props off: {r}")
    check([o.name for o in sub_e.instance_collection.all_objects] == [f"CS2R_{name}_Win"],
          "the `_Win` sub-mesh must stay when the props are hidden")
    check(call("status")["loaded"][name]["props_shown"] is False, "status after hide")
    call("reload", name)
    check(sub_e.instance_collection.name == "SUB_NOPROPS", "reload() brought the props back")
    call("lod", name, "lineup")
    check(sub_e.instance_collection.name == "SUB_NOPROPS", "lod() brought the props back")
    r = call("props", name, True)
    check(r["props"][name]["shown"] is True and sub_e.instance_collection.name == "SUB"
          and len(visible_props()) == 6, f"props on: {r}")
    r = call("props", None, "off")                           # every loaded pack
    check(r["total"] == 6 and r["props"][plain] == {"count": 0, "shown": False,
                                                     "note": "no vanilla sub-object rows in this pack"},
          f"props(None, 'off'): {r}")
    check(ns["props"](name, "maybe")["ok"] is False, "bad `on` accepted")
    check(ns["props"]("NoSuchPack")["ok"] is False, "unloaded pack accepted")
    call("props", name, "on")
    # exec hosts that pass separate globals / locals, and install(): props() must resolve there too
    g2, l2 = {"__name__": "review_live_props_test2"}, {}
    exec(compile(src, REVIEW_LIVE, "exec"), g2, l2)
    check(g2.get("props") is not None and l2["props"](name, "off")["ok"] and l2["status"]()["loaded"][name]["props_shown"] is False,
          "two-dict exec: props() does not resolve")
    ns["props"](name, True)
    inst = call("install")
    check("props" in inst["functions"], f"install() lacks props: {inst}")
    check(bpy.app.driver_namespace["cs2r_review"].props(name, False)["ok"], "driver-namespace props()")
    ns["props"](name, True)
    call("unload", name)
    call("unload", plain)
    print("[subobj-test] D: review_live load / status / stand_ins report 6 and 0; props() swaps SUB <-> "
          "SUB_NOPROPS, keeps `_Win`, survives reload / lod")


def main():
    t0 = time.perf_counter()
    work = tempfile.mkdtemp(prefix="cs2_subobj_test_")
    test_shipped_manifests()
    review_dir, name, plain = test_synthetic_pack(work)
    test_mod_pack(work, review_dir)
    test_live(review_dir, name, plain)
    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_review_subobjects: {len(FAILS)} failures ({dt:.1f} s); work dir {work}")
        sys.exit(1)
    shutil.rmtree(work, ignore_errors=True)
    print(f"PASS test_review_subobjects: shipped rows read and boxed in the generators' own frame, "
          f"synthetic pack N rows -> N boxes + labels, session untouched, review_live props OK ({dt:.1f} s)")


main()
