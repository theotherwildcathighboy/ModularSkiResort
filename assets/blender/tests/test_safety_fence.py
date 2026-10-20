"""
test_safety_fence - gates for assets/blender/safety_fence.py + safety_fence_stage.py (the orange
safety-net fence; design docs/research/art/assets-2026-10/design/safety-fence.md section 7,
build report docs/research/art/assets-2026-10/build/safety-fence.md).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_safety_fence.py [-- --quick]

Exit 1 on any failure; --quick skips the two child processes (C12 fresh-process determinism).

  C1   names: SkiSafetyNetA / SkiNetPostA pass validate_asset_name; no "Fence" in any
       StaticObjectPrefab name (the two Editor wrappers + SkiNetPost); an underscore name raises.
  C2   raw FBX: z -200..+200 cm (+-0.1), y -15..158, x within -2.5..+4.5 (LOD1 same z, y to 155);
       SkiNetPostA y 0..158.
  C3   every re-imported net vertex (x >= 1.9 cm, y <= 126 cm) sits on a 50 cm column, LOD0 + LOD1.
  C4   post / cap vertices z - s in [0, 4.0] cm, s in {-200, 0} (tile_contracts + raw FBX).
  C5   tris LOD0 96 <= 128, LOD1 44 <= 60 and < 50 % of LOD0, post 32 <= 40 (contracts + FBX).
  C6   every net triangle has a coincident opposite-normal twin (tile_contracts).
  C7   UVs in [0, 1]; net faces in the net region; post / cap faces on alpha-255 texels.
  C8   written BaseColor: RGBA, alpha < 255 only in the net region, mean 0.40-0.50, selvedges
       >= 10 rows, alpha-0 RGB within 0.02 of the strand RGB, recolor texels 0.80 +- 0.05.
  C9   ControlMask net G = 1, post band B = 1, caps 0, A = 0, R+G+B <= 1; MaskMap B = 0.
  C10  settings.json exactly the design's (key order too); SkiNetPostA settings {} (no Surface).
  C11  cs2_validate (kind hanging, NO cutout flag) reads Surface.alphaCutoff and passes 0 / 0;
       the post (kind prop) 0 / 0; negative: the same folder without the Surface block ERRORs.
  C12  two in-process builds hash identically, maps paint identically; two fresh Blender processes
       write byte-identical PNGs + settings.json and FBX geometry (re-imported) identical.
  C13  write_settings(surface=None) is byte-identical on three committed settings.json shapes
       (shared_from sharer, supplied LOD1/LOD2, LOD0-only {}); surface={} raises.
  C14  the manifest records template, TilingCount, tile length, alpha mean, tris per LOD.
  STG  staged prefab JSON: design section 5 type tables in order, check_prefab_text clean except
       the two REPLACE_ placeholders, UTF-8 no BOM, CRLF, .cid 32 hex no newline; the staged files
       equal what stage() emits for their CIDs; prove_loc (SkiLiftHaulRopeB .loc rewritten byte
       for byte); renumber_types rejects a forward reference.
  SRG  surgery rehearsal in a temp ImportedData (simulate_import from the temp export): template
       gate passes at 34 and STOPs at 0 / 1; LOD gate; fragments appended once (a second run
       skips); every written prefab passes check_prefab_text; lane -> mesh CID; end posts wire
       m_NodeObject; the probe installs three prefabs.
  NEG  contracts reject: a net vertex off its column (C3), a post moved 5 cm (C4), a missing twin
       (C6), an opaque net (C8 mean), zeroed hole RGB (C8 fringe).
"""

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import traceback

import bmesh
import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, HERE)
import cs2_asset                     # noqa: E402
import cs2_validate                  # noqa: E402
import safety_fence as F             # noqa: E402
import safety_fence_stage as S       # noqa: E402

QUICK = "--quick" in sys.argv
FAILS = []


def ok(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"[test_safety_fence] FAIL {msg}")
    else:
        print(f"[test_safety_fence] ok   {msg}")


def mesh_hash(obj):
    me = obj.data
    h = hashlib.sha1()
    for v in me.vertices:
        h.update(("%.5f %.5f %.5f;" % tuple(v.co)).encode())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    for lp in me.uv_layers["UVMap"].data:
        h.update(("%.6f %.6f;" % tuple(lp.uv)).encode())
    for cn in me.corner_normals:
        h.update(("%.4f %.4f %.4f;" % tuple(cn.vector)).encode())
    h.update(me[cs2_asset.PART_TABLE_PROP].encode())
    return h.hexdigest()


def remove(obj):
    me = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    if me.users == 0:
        bpy.data.meshes.remove(me)


def raw_fbx(path):
    """(verts raw cm (n, 3), tris, geometry hash) of an exported FBX, read as stored."""
    objs = cs2_validate.import_fbx_temp(path)
    try:
        me = [o for o in objs if o.type == "MESH"][0].data
        co = np.empty(len(me.vertices) * 3, dtype=np.float64)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        tris = sum(1 for p in me.polygons if len(p.vertices) == 3)
        h = hashlib.sha1()
        for row in sorted(tuple(np.round(r, 3)) for r in co):
            h.update(repr(row).encode())
        h.update(str(tris).encode())
        return co, tris, h.hexdigest()
    finally:
        cs2_validate.remove_objects(objs)


def loaded_maps(asset_dir, name):
    return {m: (cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{name}_{m}.png")), m != "BaseColor")
            for m in ("BaseColor", "Normal", "MaskMap", "ControlMask")}


def raises(fn, *a, **k):
    try:
        fn(*a, **k)
    except Exception as e:  # noqa: BLE001
        return str(e)
    return None


def run_child(out_dir):
    cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
           "--python", os.path.join(HERE, "safety_fence.py"), "--", "--out", out_dir, "--export"]
    r = subprocess.run(cmd, cwd=HERE, capture_output=True, text=True, timeout=900)
    return r.returncode, r.stdout[-1500:] + r.stderr[-1500:]


def declared_types(text):
    return [m.group(2) for m in re.finditer(r'"\$type":\s*"(\d+)\|([^"]+)"', text)]


DESIGN_TYPES = {
    S.NET: ["NetFencePrefab", "List`1", "UIObject", "UIObjectOverrideGroup", "PlaceableNet", "Bounds1",
            "ServiceObject", "Unlockable", "PrefabBase[]", "NetSectionInfo[]", "NetSectionInfo",
            "NetPieceRequirements[]", "float3", "NetEdgeStateInfo[]", "NetEdgeStateInfo",
            "NetNodeStateInfo[]", "NetNodeStateInfo"],
    S.SECTION: ["NetSectionPrefab", "List`1", "NetSubSectionInfo[]", "NetPieceInfo[]", "NetPieceInfo",
                "NetPieceRequirements[]", "float3"],
    S.PIECE: ["NetPiecePrefab", "List`1", "NetPieceLanes", "NetLaneInfo[]", "NetLaneInfo", "float3",
              "PlaceableNetPiece", "AssetReference`1", "Bounds3", "Bounds1", "float4"],
    S.LANE: ["NetLaneGeometryPrefab", "List`1", "UtilityLane", "NetLaneMeshInfo[]", "NetLaneMeshInfo"],
    S.POST: ["StaticObjectPrefab", "List`1", "UtilityObject", "float3", "ObjectMeshInfo[]", "ObjectMeshInfo",
             "quaternion", "float4"],
}


def short_type(t):
    head = t.split(",")[0]
    return head.split(".")[-1] if "[[" not in head else head.split("[[")[0].split(".")[-1]


# --------------------------------------------------------------------------------------

def test_names():
    for n in (F.NAME, F.POST_NAME):
        ok(raises(cs2_asset.validate_asset_name, n) is None, f"C1 {n} passes validate_asset_name")
    for n in (F.NAME, F.POST_NAME, S.POST):
        ok("fence" not in n.lower(), f"C1 StaticObjectPrefab name {n!r} has no 'Fence'")
    ok(raises(cs2_asset.validate_asset_name, "SkiSafety_NetA") is not None, "C1 negative: underscore name raises")


def test_inprocess():
    F.clear_scene()
    m1, m2 = F.paint_net_maps(), F.paint_net_maps()
    ok(all(np.array_equal(m1[k][0], m2[k][0]) for k in m1), "C12 paint_net_maps deterministic")
    p1, p2 = F.paint_post_maps(), F.paint_post_maps()
    ok(all(np.array_equal(p1[k][0], p2[k][0]) for k in p1), "C12 paint_post_maps deterministic")
    tex = F.texture_contracts(m1, p1)
    ok(0.40 <= tex["net_alpha_mean"] <= 0.50, f"C8 painted net alpha mean {tex['net_alpha_mean']}")
    for lod in (0, 1):
        a = F.build_tile(f"FenceTestA{lod}", lod)
        b = F.build_tile(f"FenceTestB{lod}", lod)
        ok(mesh_hash(a) == mesh_hash(b), f"C12 LOD{lod} two builds hash identically")
        held = F.tile_contracts(a, lod, m1)
        want = F.LOD0_TRIS if lod == 0 else F.LOD1_TRIS
        ok(held["tris"] == want, f"C5 LOD{lod} tris {held['tris']} == {want}")
        remove(b)
        remove(a)
    pa, pb = F.build_post("PostTestA"), F.build_post("PostTestB")
    ok(mesh_hash(pa) == mesh_hash(pb), "C12 post two builds hash identically")
    ok(F.post_contracts(pa, p1)["tris"] == F.POST_TRIS, f"C5 post tris == {F.POST_TRIS}")
    remove(pa)
    remove(pb)
    ok(F.LOD1_TRIS < 0.5 * F.LOD0_TRIS, "C5 LOD1 < 50 % of LOD0")


def _edit_verts(obj, fn):
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    fn(co, F._part_names(obj), me)
    me.vertices.foreach_set("co", co.ravel())
    me.update()


def _vertex_parts(obj):
    names = F._part_names(obj)
    vp = {}
    for p, n in zip(obj.data.polygons, names):
        for v in p.vertices:
            vp[v] = n
    return vp


def test_negatives():
    maps = F.paint_net_maps()
    # C3: one net vertex 0.1 m along travel (Blender -y)
    t = F.build_tile("FenceNegC3", 0)
    vp = _vertex_parts(t)
    v = next(i for i, n in sorted(vp.items()) if n.startswith("net_"))
    t.data.vertices[v].co.y -= 0.1
    t.data.update()
    msg = raises(F.tile_contracts, t, 0, maps)
    ok(msg is not None and "C3" in msg, "NEG C3 net vertex off its column is rejected")
    remove(t)
    # C4: post_0 moved 5 cm along travel
    t = F.build_tile("FenceNegC4", 0)
    vp = _vertex_parts(t)
    for i, n in vp.items():
        if n == "post_0":
            t.data.vertices[i].co.y -= 0.05
    t.data.update()
    msg = raises(F.tile_contracts, t, 0, maps)
    ok(msg is not None and "C4" in msg, "NEG C4 post outside its sub-tile window is rejected")
    remove(t)
    # C6: delete one net face (its twin loses its partner)
    t = F.build_tile("FenceNegC6", 0)
    names = F._part_names(t)
    idx = next(i for i, n in enumerate(names) if n.startswith("net_"))
    bm = bmesh.new()
    bm.from_mesh(t.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[idx]], context="FACES_ONLY")
    bm.to_mesh(t.data)
    bm.free()
    msg = raises(F.tile_contracts, t, 0, maps)
    ok(msg is not None and "C6" in msg, "NEG C6 missing twin is rejected")
    remove(t)
    # C8: opaque net / zeroed hole RGB
    bad = {k: (v[0].copy(), v[1]) for k, v in maps.items()}
    ys, xs = F._sl(F.R_NET)
    bad["BaseColor"][0][ys, xs, 3] = 1.0
    msg = raises(F.texture_contracts, bad)
    ok(msg is not None and "mean alpha" in msg, "NEG C8 opaque net (mean alpha) is rejected")
    bad = {k: (v[0].copy(), v[1]) for k, v in maps.items()}
    holes = bad["BaseColor"][0][ys, xs, 3] <= 0.0
    region = bad["BaseColor"][0][ys, xs]
    region[holes, 0:3] = 0.0
    bad["BaseColor"][0][ys, xs] = region
    msg = raises(F.texture_contracts, bad)
    ok(msg is not None and "alpha-0" in msg, "NEG C8 black RGB under the holes is rejected")


def test_export(tmp):
    asset_dir, post_dir, objs, data = F.export(tmp)
    for o in objs.values():
        remove(o)
    files = sorted(os.listdir(asset_dir))
    want = sorted([f"{F.NAME}.fbx", f"{F.NAME}_LOD1.fbx", "settings.json", f"{F.NAME}.manifest.json"]
                  + [f"{F.NAME}_{m}.png" for m in ("BaseColor", "Normal", "MaskMap", "ControlMask")])
    ok(files == want, f"net folder contents {files}")
    pfiles = sorted(os.listdir(post_dir))
    pwant = sorted([f"{F.POST_NAME}.fbx", "settings.json", f"{F.POST_NAME}.manifest.json"]
                   + [f"{F.POST_NAME}_{m}.png" for m in ("BaseColor", "Normal", "MaskMap", "ControlMask")])
    ok(pfiles == pwant, f"post folder contents {pfiles}")
    # C2 / C3 / C4 / C5 on the raw FBX
    for fbx, lod, ytop in ((f"{F.NAME}.fbx", 0, 158.0), (f"{F.NAME}_LOD1.fbx", 1, 155.0)):
        co, tris, _h = raw_fbx(os.path.join(asset_dir, fbx))
        lo, hi = co.min(axis=0), co.max(axis=0)
        ok(abs(lo[2] + 200.0) <= 0.1 and abs(hi[2] - 200.0) <= 0.1, f"C2 {fbx} z {lo[2]:.2f} .. {hi[2]:.2f} cm")
        ok(abs(lo[1] + 15.0) <= 0.1 and abs(hi[1] - ytop) <= 0.1, f"C2 {fbx} y {lo[1]:.2f} .. {hi[1]:.2f} cm")
        ok(lo[0] >= -2.5 - 1e-3 and hi[0] <= 4.5 + 1e-3, f"C2 {fbx} x {lo[0]:.2f} .. {hi[0]:.2f} cm")
        net = co[(co[:, 0] >= 1.9) & (co[:, 1] <= 126.0)]
        off = np.abs(net[:, 2] - 50.0 * np.round(net[:, 2] / 50.0)) > 0.1
        ok(len(net) > 0 and not off.any(), f"C3 {fbx}: {len(net)} net verts on 50 cm columns")
        posts = co[~((co[:, 0] >= 1.9) & (co[:, 1] <= 126.0))]
        rel = np.where(posts[:, 2] < -100.0, posts[:, 2] + 200.0, posts[:, 2])
        ok(rel.min() >= -1e-3 and rel.max() <= 4.0 + 1e-3,
           f"C4 {fbx}: post / cap z - s in [{rel.min():.3f}, {rel.max():.3f}] cm")
        want_t = F.LOD0_TRIS if lod == 0 else F.LOD1_TRIS
        ok(tris == want_t, f"C5 {fbx} {tris} tris == {want_t}")
    co, tris, _h = raw_fbx(os.path.join(post_dir, f"{F.POST_NAME}.fbx"))
    ok(abs(co[:, 1].min()) <= 0.01 and abs(co[:, 1].max() - 158.0) <= 0.1,
       f"C2 post y {co[:, 1].min():.2f} .. {co[:, 1].max():.2f} cm")
    ok(tris == F.POST_TRIS <= F.POST_CAP, f"C5 post FBX {tris} tris")
    # C8 / C9 on the written PNGs
    for m in ("BaseColor", "Normal", "MaskMap", "ControlMask"):
        w, h, depth, ctype = cs2_validate.read_png_header(os.path.join(asset_dir, f"{F.NAME}_{m}.png"))
        ok((w, h, depth, ctype) == (F.A, F.A, 8, 6), f"C8 {m} {w}x{h} depth {depth} type {ctype}")
    lm = loaded_maps(asset_dir, F.NAME)
    pm = loaded_maps(post_dir, F.POST_NAME)
    msg = raises(F.texture_contracts, lm, pm)
    ok(msg is None, f"C8 / C9 hold on the written PNGs ({msg})")
    # C10
    with open(os.path.join(asset_dir, "settings.json"), encoding="utf-8") as fh:
        st = json.load(fh)
    want_st = {"sharedAssets": {f"{F.NAME}_LOD1_{m}.png": f"{F.NAME}_{m}.png"
                                for m in ("BaseColor", "ControlMask", "MaskMap", "Normal")},
               "LOD1": {"mode": "Skip"},
               "Surface": {"materialTemplate": "CurvedHashedCutout", "needsAlphaClip": True, "alphaCutoff": 0.54}}
    ok(st == want_st and list(st) == list(want_st) and list(st["sharedAssets"]) == list(want_st["sharedAssets"]),
       "C10 net settings.json == design section 4 (keys in order)")
    with open(os.path.join(post_dir, "settings.json"), encoding="utf-8") as fh:
        ok(json.load(fh) == {}, "C10 post settings.json == {} (no Surface)")
    # C11
    f1, s1 = cs2_validate.validate_asset_folder_ex(asset_dir, F.NAME, {"kind": "hanging"})
    e1 = [f for f in f1 if f[0] != "INFO"]
    ok(not e1 and any(c == "settings" and "cutout" in m for _l, c, m in f1),
       f"C11 net (kind hanging, cutout from settings): {len(e1)} errors/warnings {e1[:2]}")
    f2, _s2 = cs2_validate.validate_asset_folder_ex(post_dir, F.POST_NAME, {"kind": "prop"})
    e2 = [f for f in f2 if f[0] != "INFO"]
    ok(not e2, f"C11 post (kind prop): {len(e2)} errors/warnings {e2[:2]}")
    neg = os.path.join(tmp, "neg", F.NAME)
    shutil.copytree(asset_dir, neg)
    cs2_asset.write_settings(neg, F.NAME, supplied_lods=(1,))
    f3, _s3 = cs2_validate.validate_asset_folder_ex(neg, F.NAME, {"kind": "hanging"})
    ok(any(l == "ERROR" and c == "basecolor-alpha" for l, c, _m in f3),
       "C11 negative: without the Surface block the alpha map is an ERROR")
    # C14
    with open(os.path.join(asset_dir, f"{F.NAME}.manifest.json"), encoding="utf-8") as fh:
        man = json.load(fh)
    ok(man.get("material_template") == "CurvedHashedCutout" and man.get("tiling_count") == S.TILING_COUNT
       and man.get("tile_length_m") == F.TILE and 0.40 <= man["alpha"]["mean_net_region"] <= 0.50
       and man["tris"] == {"LOD0": F.LOD0_TRIS, "LOD1": F.LOD1_TRIS},
       "C14 manifest: template, TilingCount, tile length, alpha mean, tris per LOD")
    return asset_dir, post_dir


def test_fresh(tmp_a_root):
    if QUICK:
        print("[test_safety_fence] skip C12 fresh processes (--quick)")
        return
    roots = [tempfile.mkdtemp(prefix="test_fence_child_") for _ in range(2)]
    try:
        for r in roots:
            code, tail = run_child(r)
            ok(code == 0, f"C12 child export exit {code}" + ("" if code == 0 else f": {tail}"))
        for name in (F.NAME, F.POST_NAME):
            d0, d1 = (os.path.join(r, name) for r in roots)
            for f in sorted(os.listdir(d0)):
                if f.endswith((".png", "settings.json")):
                    b0 = open(os.path.join(d0, f), "rb").read()
                    b1 = open(os.path.join(d1, f), "rb").read()
                    ok(b0 == b1, f"C12 fresh processes: {name}/{f} byte-identical")
                elif f.endswith(".fbx"):
                    ok(raw_fbx(os.path.join(d0, f))[2] == raw_fbx(os.path.join(d1, f))[2],
                       f"C12 fresh processes: {name}/{f} geometry identical")
    finally:
        for r in roots:
            shutil.rmtree(r, ignore_errors=True)


def test_settings_additive(tmp):
    cases = (
        ("props/B/SkiDressAkjaB", "SkiDressAkjaB", dict(shared_from="../SkiDressSkiRackB/SkiDressSkiRackB")),
        ("buildings/A/SkiPatrolHQCommercialLowA", "SkiPatrolHQCommercialLowA", dict(supplied_lods=(1, 2))),
        ("towers/N/SkiLiftTowerFG8N", "SkiLiftTowerFG8N", {}),
    )
    for rel, name, kw in cases:
        src = os.path.join(REPO, "assets", "import", rel, "settings.json")
        if not os.path.isfile(src):
            ok(False, f"C13 {rel}/settings.json missing")
            continue
        d = os.path.join(tmp, "c13", name)
        os.makedirs(d, exist_ok=True)
        cs2_asset.write_settings(d, name, **kw)
        a = open(src, "rb").read()
        b = open(os.path.join(d, "settings.json"), "rb").read()
        ok(a == b, f"C13 write_settings byte-identical to committed {rel}/settings.json ({len(a)} bytes)")
    ok(raises(cs2_asset.write_settings, tmp, "X", surface={}) is not None, "C13 surface={} raises")
    p = cs2_asset.write_settings(os.path.join(tmp, "c13"), "X", supplied_lods=(1,), surface=F.SURFACE)
    with open(p, encoding="utf-8") as fh:
        ok(list(json.load(fh)) == ["sharedAssets", "LOD1", "Surface"], "C13 Surface is the last key")


def test_staging(tmp):
    ok(S.prove_loc()[0], f"STG prove_loc: {S.prove_loc()[1]}")
    # the real staged folder equals what the emitter writes for its CIDs
    real = S.STAGING
    for name in S.MAIN_PREFABS + S.PROBE_PREFABS:
        d = S.staged_dir(name, real)
        pfile = os.path.join(d, f"{name}.Prefab")
        cfile = pfile + ".cid"
        if not os.path.isfile(pfile):
            ok(False, f"STG {name}.Prefab staged")
            continue
        raw = open(cfile, "rb").read()
        ok(re.fullmatch(rb"[0-9a-f]{32}", raw) is not None, f"STG {name}.Prefab.cid is 32 hex, no newline")
        data = open(pfile, "rb").read()
        ok(not data.startswith(b"\xef\xbb\xbf") and b"\r\n" in data and data.count(b"\n") == data.count(b"\r\n"),
           f"STG {name}.Prefab UTF-8 no BOM, CRLF")
        text = data.decode("utf-8")
        errs = S.check_prefab_text(text)
        if name in (S.LANE, S.POST):
            errs = [e for e in errs if "placeholder" not in e]
            ok("REPLACE_" in text, f"STG {name} carries its REPLACE_ mesh placeholder")
        ok(not errs, f"STG {name} passes check_prefab_text {errs[:2]}")
        if name in DESIGN_TYPES:
            got = [short_type(t) for t in declared_types(text)]
            ok(got == DESIGN_TYPES[name], f"STG {name} type table == design section 5 {got}")
    cid = {n: S.read_cid(os.path.join(S.staged_dir(n, real), f"{n}.Prefab.cid"))
           for n in S.MAIN_PREFABS + S.PROBE_PREFABS}
    expect = {
        S.NET: S.net_prefab(S.NET, cid[S.SECTION], S.UI_PRIORITY),
        S.SECTION: S.section_prefab(S.SECTION, cid[S.PIECE]),
        S.PIECE: S.piece_prefab(S.PIECE, S.fref("CID", cid[S.LANE])),
        S.LANE: S.lane_prefab(S.LANE, S.fref("CID", S.PLACEHOLDER.format(S.MESH))),
        S.POST: S.post_prefab(S.POST, S.fref("CID", S.PLACEHOLDER.format(S.POST_MESH))),
        S.PROBE_PIECE: S.piece_prefab(S.PROBE_PIECE, S.fref("UnityGUID", S.GUID["chainlink_lane"][0])),
    }
    for n, t in expect.items():
        ok(open(os.path.join(S.staged_dir(n, real), f"{n}.Prefab"), "rb").read() == t.encode("utf-8"),
           f"STG staged {n}.Prefab == emitter output (stage() is idempotent)")
    ok(len(set(cid.values())) == len(cid), "STG staged CIDs are unique")
    loc = os.path.join(S.staged_dir(S.NET, real), f"{S.NET}_en-US.loc")
    ok(os.path.isfile(loc) and open(loc, "rb").read() == S.loc_bytes(S.LOC_ENTRIES), "STG optional .loc staged")
    msg = raises(S.renumber_types, '"$type": 3,\n"$type": "3|X, Game"')
    ok(msg is not None, "STG renumber_types rejects a forward reference")
    # a fresh staging run in temp: same shapes, new CIDs, idempotent second run
    tstage = os.path.join(tmp, "staging")
    r1 = S.stage(tstage)
    r2 = S.stage(tstage)
    ok(r2["written"] == [] and r2["minted"] == [], "STG stage() second run writes and mints nothing")
    return tstage


def test_surgery(tmp, export_root, tstage):
    def fresh(template=S.WANT_TEMPLATE):
        idd = tempfile.mkdtemp(prefix="test_fence_id_", dir=tmp)
        open(os.path.join(idd, S.SCRATCH_MARKER), "w").close()
        S.simulate_import(idd, template=template, import_root=export_root)
        return idd

    quiet = lambda *_a: None  # noqa: E731
    for bad in (0, 1, 31):
        idd = fresh(bad)
        msg = raises(S.surgery, idd, apply=True, staging=tstage, log=quiet)
        ok(msg is not None and "template" in msg, f"SRG template gate STOPs at {bad}")
    idd = fresh()
    ok(raises(S.surgery, os.path.join(tmp, "nowhere"), apply=True, staging=tstage, log=quiet) is not None,
       "SRG missing import STOPs")
    dry = S.surgery(idd, apply=False, staging=tstage, log=quiet)
    ok(not dry["written"], "SRG dry run writes nothing")
    done = S.surgery(idd, apply=True, end_posts=True, staging=tstage, log=quiet, backup_root=tmp)
    found = S.find_imported(idd)
    for key, rec in found.items():
        text = open(rec["prefab"], "rb").read().decode("utf-8")
        ok(not S.check_prefab_text(text), f"SRG {key} passes check_prefab_text after surgery")
        for comp, _f in S.MESH_FRAGMENTS[key]:
            ok(text.count(f"Game.Prefabs.{comp}, Game") == 1, f"SRG {key} carries one {comp}")
        if key.endswith("NetA Mesh"):
            ok('"m_TilingCount": 4' in text and "LodProperties" in text, f"SRG {key} TilingCount 4 + LodProperties")
    for name in (S.NET, S.SECTION, S.PIECE, S.LANE, S.POST):
        p = os.path.join(idd, name, f"{name}.Prefab")
        ok(os.path.isfile(p) and os.path.isfile(p + ".cid"), f"SRG {name} installed")
        if os.path.isfile(p):
            ok(not S.check_prefab_text(open(p, "rb").read().decode("utf-8")), f"SRG {name} clean")
    lane = open(os.path.join(idd, S.LANE, f"{S.LANE}.Prefab"), "rb").read().decode("utf-8")
    post_cid = S.read_cid(os.path.join(S.staged_dir(S.POST, tstage), f"{S.POST}.Prefab.cid"))
    ok(found[f"{S.MESH} Mesh"]["cid"] in lane and f'"m_NodeObject": $fstrref:"CID:{post_cid}"' in lane,
       "SRG lane -> SkiSafetyNetA Mesh CID, m_NodeObject -> SkiNetPost (end posts)")
    again = S.surgery(idd, apply=True, end_posts=True, staging=tstage, log=quiet, backup_root=tmp)
    ok(sum("already carries" in s for s in again["steps"]) == 3, "SRG second run skips the mesh edits")
    idp = tempfile.mkdtemp(prefix="test_fence_probe_", dir=tmp)
    S.surgery(idp, apply=True, probe=True, staging=tstage, log=quiet)
    ok(all(os.path.isfile(os.path.join(idp, n, f"{n}.Prefab")) for n in S.PROBE_PREFABS),
       "SRG probe installs its three prefabs")
    ok(done["backup"] is not None, "SRG edited imported prefabs were backed up first")


def main():
    tmp = tempfile.mkdtemp(prefix="test_safety_fence_")
    try:
        test_names()
        test_inprocess()
        test_negatives()
        F.clear_scene()
        test_export(tmp)
        test_settings_additive(tmp)
        tstage = test_staging(tmp)
        test_surgery(tmp, tmp, tstage)
        test_fresh(tmp)
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        FAILS.append("exception")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print(f"[test_safety_fence] {'FAILED ' + str(len(FAILS)) if FAILS else 'ALL OK'}")
    if FAILS:
        for f in FAILS:
            print(f"[test_safety_fence]   - {f}")
        sys.exit(1)


if __name__ == "__main__":
    main()
