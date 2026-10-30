"""
test_safety_fence_b - gates for assets/blender/safety_fence_b.py (the ski safety fence, generation B:
SkiSafetyPanelB solid + SkiSafetyNetB netted; research docs/research/fence-cutout-2026-10-29.md).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_safety_fence_b.py [-- --quick]

Exit 1 on any failure; --quick skips the two child processes (B8 fresh-process determinism).

  B1   names pass validate_asset_name and contain no "Fence" (the Editor wraps each mesh in a StaticObjectPrefab);
       an underscore name raises.
  B2   per variant and LOD: tile_contracts hold (C2 bounds, C3 columns, C4 post window, C5 tris exact + caps +
       LOD1 <= 50 %, C6 outward twins 6 mm apart, C7 UVs inside their column, outward posts / caps); two
       in-process builds hash identically; paint_maps is deterministic.
  B3   raw FBX: z -200 .. +200 cm, y -15 .. 158 (LOD1 155), x within -2.5 .. +4.5; sheet vertices on their columns
       (50 cm netted LOD0, 200 cm otherwise); post / cap z - s in [0, 4] cm; tris as built.
  B4   written PNGs: 8-bit RGBA at the atlas size; texture_contracts hold on them (V rule, alpha, ControlMask /
       MaskMap channels; netted mean alpha 0.40 - 0.50, periodic body, net edge rows, cords, mirror symmetry;
       solid alpha 255).
  B5   settings.json exactly (key order too): solid Surface {"materialTemplate": "Curved"}, netted the vanilla
       cutout block; both LOD1 Skip + sharedAssets LOD1 -> LOD0.
  B6   cs2_validate (kind hanging, no cutout flag): both 0 errors / 0 warnings, the netted folder reads its cutout
       from Surface.alphaCutoff; negative: the netted folder without the Surface block ERRORs.
  B7   manifest: template, CurveProperties 2 / 0, tris per LOD, atlas columns, v_robust, alpha stats; no absolute
       path anywhere in it (manifests are tracked).
  NEG  contracts reject: a post column varying along v, an opaque net edge row, a net body that is not periodic, a
       solid texel with alpha < 255, a sheet face UV in the post column (C7), a post moved 5 cm (C4), a missing
       twin (C6), a sheet vertex off its column (C3).
  B8   two fresh Blender processes write byte-identical PNGs + settings.json and identical FBX geometry.
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
sys.path.insert(0, HERE)
import cs2_asset                     # noqa: E402
import cs2_validate                  # noqa: E402
import safety_fence as F             # noqa: E402
import safety_fence_b as B           # noqa: E402

QUICK = "--quick" in sys.argv
FAILS = []
MAPS = ("BaseColor", "Normal", "MaskMap", "ControlMask")


def ok(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"[test_safety_fence_b] FAIL {msg}")
    else:
        print(f"[test_safety_fence_b] ok   {msg}")


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
            for m in MAPS}


def raises(fn, *a, **k):
    try:
        fn(*a, **k)
    except Exception as e:  # noqa: BLE001
        return str(e)
    return None


def copy_maps(maps):
    return {k: (v[0].copy(), v[1]) for k, v in maps.items()}


def run_child(out_dir):
    cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
           "--python", os.path.join(HERE, "safety_fence_b.py"), "--", "--out", out_dir, "--export"]
    r = subprocess.run(cmd, cwd=HERE, capture_output=True, text=True, timeout=900)
    return r.returncode, r.stdout[-1500:] + r.stderr[-1500:]


def _parts(obj):
    return F._part_names(obj)


# --------------------------------------------------------------------------------------

def test_names():
    for n in B.NAMES.values():
        ok(raises(cs2_asset.validate_asset_name, n) is None, f"B1 {n} passes validate_asset_name")
        ok("fence" not in n.lower(), f"B1 {n} has no 'Fence' (its Editor wrapper is a StaticObjectPrefab)")
    ok(raises(cs2_asset.validate_asset_name, "SkiSafety_NetB") is not None, "B1 negative: underscore name raises")


def test_inprocess():
    F.clear_scene()
    for v in B.VARIANTS:
        m1, m2 = B.paint_maps(v), B.paint_maps(v)
        ok(all(np.array_equal(m1[k][0], m2[k][0]) for k in m1), f"B2 {v} paint_maps deterministic")
        ok(raises(B.texture_contracts, v, m1) is None, f"B2 {v} texture contracts hold on the painted maps")
        for lod in (0, 1):
            a, b = B.build_tile(v, lod), B.build_tile(v, lod)
            ok(mesh_hash(a) == mesh_hash(b), f"B2 {v} LOD{lod} two builds hash identically")
            msg = raises(B.tile_contracts, a, v, lod, m1)
            ok(msg is None, f"B2 {v} LOD{lod} tile contracts hold ({msg})")
            ok(B.TRIS[v][lod] <= B.CAPS[v][lod], f"B2 {v} LOD{lod} {B.TRIS[v][lod]} tris <= cap {B.CAPS[v][lod]}")
            remove(b)
            remove(a)
        ok(B.TRIS[v][1] <= 0.5 * B.TRIS[v][0], f"B2 {v} LOD1 {B.TRIS[v][1]} <= 50 % of LOD0 {B.TRIS[v][0]}")


def test_negatives():
    F.clear_scene()
    net = B.paint_maps(B.NET)
    solid = B.paint_maps(B.SOLID)
    cols = B.COLUMNS[B.NET]
    # V rule: one post-column texel off along v
    bad = copy_maps(net)
    bad["BaseColor"][0][3, cols["post"][0] + 4, 0] = 0.5
    msg = raises(B.texture_contracts, B.NET, bad)
    ok(msg is not None and "varies along v" in msg, "NEG V post column varying along v is rejected")
    # an opaque net edge row (a clamped V would stretch it over the panel)
    bad = copy_maps(net)
    bad["BaseColor"][0][0, cols["guard"][0]:, 3] = 1.0
    msg = raises(B.texture_contracts, B.NET, bad)
    ok(msg is not None and "edge rows" in msg, "NEG V opaque net edge row is rejected")
    # a net body that is not periodic along v
    bad = copy_maps(net)
    bad["BaseColor"][0][300, cols["sheet"][0]:, 3] = 1.0
    msg = raises(B.texture_contracts, B.NET, bad)
    ok(msg is not None and "not periodic" in msg, "NEG V non-periodic net body is rejected")
    # solid alpha < 255
    bad = copy_maps(solid)
    bad["BaseColor"][0][100, 100, 3] = 0.5
    msg = raises(B.texture_contracts, B.SOLID, bad)
    ok(msg is not None and "alpha" in msg, "NEG C8 solid texel with alpha < 255 is rejected")
    # C7: a sheet face sampling the post column
    t = B.build_tile(B.NET, 0)
    names = _parts(t)
    idx = next(i for i, n in enumerate(names) if n.startswith("net_"))
    layer = t.data.uv_layers["UVMap"]
    for li in t.data.polygons[idx].loop_indices:
        layer.data[li].uv = (10.0 / B.ATLAS[B.NET], layer.data[li].uv[1])
    msg = raises(B.tile_contracts, t, B.NET, 0, net)
    ok(msg is not None and "C7" in msg, "NEG C7 sheet face UV in the post column is rejected")
    remove(t)
    # C4: post_0 moved 5 cm along travel (Blender -y)
    t = B.build_tile(B.SOLID, 0)
    post0 = {vi for p, n in zip(t.data.polygons, _parts(t)) if n == "post_0" for vi in p.vertices}
    for vi in post0:
        t.data.vertices[vi].co.y -= 0.05
    t.data.update()
    msg = raises(B.tile_contracts, t, B.SOLID, 0, solid)
    ok(msg is not None and "C4" in msg, "NEG C4 post outside its window is rejected")
    remove(t)
    # C6: delete one sheet face (its twin loses its partner)
    t = B.build_tile(B.SOLID, 0)
    idx = next(i for i, n in enumerate(_parts(t)) if n.startswith("panel_"))
    bm = bmesh.new()
    bm.from_mesh(t.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[idx]], context="FACES_ONLY")
    bm.to_mesh(t.data)
    bm.free()
    msg = raises(B.tile_contracts, t, B.SOLID, 0, solid)
    ok(msg is not None and "C6" in msg, "NEG C6 missing twin is rejected")
    remove(t)
    # C3: one netted sheet vertex 0.1 m along travel
    t = B.build_tile(B.NET, 0)
    names = _parts(t)
    vi = next(v for p, n in zip(t.data.polygons, names) if n.startswith("net_") for v in p.vertices)
    t.data.vertices[vi].co.y -= 0.1
    t.data.update()
    msg = raises(B.tile_contracts, t, B.NET, 0, net)
    ok(msg is not None and "C3" in msg, "NEG C3 sheet vertex off its column is rejected")
    remove(t)


def test_export(tmp):
    F.clear_scene()
    built, data = B.export(tmp, args=["--out", tmp, "--export"])
    for v in B.VARIANTS:
        for k in ("lod0", "lod1"):
            remove(built[v][k])
    shared = {f"{{n}}_LOD1_{m}.png": f"{{n}}_{m}.png" for m in ("BaseColor", "ControlMask", "MaskMap", "Normal")}
    for v in B.VARIANTS:
        nm, d, size = B.NAMES[v], os.path.join(tmp, B.NAMES[v]), B.ATLAS[v]
        files = sorted(os.listdir(d))
        want = sorted([f"{nm}.fbx", f"{nm}_LOD1.fbx", "settings.json", f"{nm}.manifest.json"]
                      + [f"{nm}_{m}.png" for m in MAPS])
        ok(files == want, f"{v} folder contents {files}")
        # B3 raw FBX
        for fbx, lod, ytop in ((f"{nm}.fbx", 0, 158.0), (f"{nm}_LOD1.fbx", 1, 155.0)):
            co, tris, _h = raw_fbx(os.path.join(d, fbx))
            lo, hi = co.min(axis=0), co.max(axis=0)
            ok(abs(lo[2] + 200.0) <= 0.1 and abs(hi[2] - 200.0) <= 0.1, f"B3 {fbx} z {lo[2]:.2f} .. {hi[2]:.2f} cm")
            ok(abs(lo[1] + 15.0) <= 0.1 and abs(hi[1] - ytop) <= 0.1, f"B3 {fbx} y {lo[1]:.2f} .. {hi[1]:.2f} cm")
            ok(lo[0] >= -2.5 - 1e-3 and hi[0] <= 4.5 + 1e-3, f"B3 {fbx} x {lo[0]:.2f} .. {hi[0]:.2f} cm")
            sheet_m = (co[:, 0] >= 1.65) & (co[:, 1] <= 126.0)
            step = 50.0 if (v == B.NET and lod == 0) else 200.0
            off = np.abs(co[sheet_m, 2] - step * np.round(co[sheet_m, 2] / step)) > 0.1
            ok(sheet_m.any() and not off.any(), f"B3 {fbx}: {int(sheet_m.sum())} sheet verts on {step:.0f} cm columns")
            posts = co[~sheet_m]
            rel = np.where(posts[:, 2] < -100.0, posts[:, 2] + 200.0, posts[:, 2])
            ok(rel.min() >= -1e-3 and rel.max() <= 4.0 + 1e-3,
               f"B3 {fbx}: post / cap z - s in [{rel.min():.3f}, {rel.max():.3f}] cm")
            ok(tris == B.TRIS[v][lod], f"B3 {fbx} {tris} tris == {B.TRIS[v][lod]}")
        # B4 written PNGs
        for m in MAPS:
            w, h, depth, ctype = cs2_validate.read_png_header(os.path.join(d, f"{nm}_{m}.png"))
            ok((w, h, depth, ctype) == (size, size, 8, 6), f"B4 {nm}_{m} {w}x{h} depth {depth} type {ctype}")
        msg = raises(B.texture_contracts, v, loaded_maps(d, nm))
        ok(msg is None, f"B4 {v} texture contracts hold on the written PNGs ({msg})")
        # B5 settings.json
        with open(os.path.join(d, "settings.json"), encoding="utf-8") as fh:
            st = json.load(fh)
        want_st = {"sharedAssets": {k.format(n=nm): t.format(n=nm) for k, t in shared.items()},
                   "LOD1": {"mode": "Skip"}, "Surface": B.SURFACE[v]}
        ok(st == want_st and list(st) == list(want_st) and list(st["sharedAssets"]) == list(want_st["sharedAssets"]),
           f"B5 {v} settings.json exact (keys in order): Surface {st.get('Surface')}")
        # B6 cs2_validate
        f1, _s1 = cs2_validate.validate_asset_folder_ex(d, nm, {"kind": "hanging"})
        e1 = [f for f in f1 if f[0] != "INFO"]
        reads_cutout = any(c == "settings" and "cutout" in msg for _l, c, msg in f1)
        ok(not e1 and reads_cutout == (v == B.NET),
           f"B6 {v} (kind hanging): {len(e1)} errors / warnings {e1[:2]}, cutout from settings {reads_cutout}")
        # B7 manifest
        with open(os.path.join(d, f"{nm}.manifest.json"), encoding="utf-8") as fh:
            text = fh.read()
        man = json.loads(text)
        ok(man.get("material_template") == B.SURFACE[v]["materialTemplate"]
           and man.get("curve_properties") == {"tiling_count": 2, "override_length_m": 0.0}
           and man.get("tris") == {"LOD0": B.TRIS[v][0], "LOD1": B.TRIS[v][1]}
           and man.get("v_robust") is True and man.get("atlas_columns_px")
           and (v == B.SOLID or 0.40 <= man["alpha"]["net_alpha_mean"] <= 0.50),
           f"B7 {v} manifest: template, CurveProperties, tris, columns, alpha")
        ok(not re.search(r"[A-Za-z]:[\\/]|Users[\\/]", text), f"B7 {v} manifest holds no absolute path")
    neg = os.path.join(tmp, "neg", B.NAMES[B.NET])
    shutil.copytree(os.path.join(tmp, B.NAMES[B.NET]), neg)
    cs2_asset.write_settings(neg, B.NAMES[B.NET], supplied_lods=(1,))
    f3, _s3 = cs2_validate.validate_asset_folder_ex(neg, B.NAMES[B.NET], {"kind": "hanging"})
    ok(any(lv == "ERROR" and c == "basecolor-alpha" for lv, c, _m in f3),
       "B6 negative: the netted folder without the Surface block is an alpha ERROR")


def test_fresh():
    if QUICK:
        print("[test_safety_fence_b] skip B8 fresh processes (--quick)")
        return
    roots = [tempfile.mkdtemp(prefix="test_fence_b_child_") for _ in range(2)]
    try:
        for r in roots:
            code, tail = run_child(r)
            ok(code == 0, f"B8 child export exit {code}" + ("" if code == 0 else f": {tail}"))
        for name in B.NAMES.values():
            d0, d1 = (os.path.join(r, name) for r in roots)
            for f in sorted(os.listdir(d0)):
                if f.endswith((".png", "settings.json")):
                    with open(os.path.join(d0, f), "rb") as f0, open(os.path.join(d1, f), "rb") as f1:
                        ok(f0.read() == f1.read(), f"B8 fresh processes: {name}/{f} byte-identical")
                elif f.endswith(".fbx"):
                    ok(raw_fbx(os.path.join(d0, f))[2] == raw_fbx(os.path.join(d1, f))[2],
                       f"B8 fresh processes: {name}/{f} geometry identical")
    finally:
        for r in roots:
            shutil.rmtree(r, ignore_errors=True)


def main():
    tmp = tempfile.mkdtemp(prefix="test_safety_fence_b_")
    try:
        test_names()
        test_inprocess()
        test_negatives()
        test_export(tmp)
        test_fresh()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        FAILS.append("exception")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print(f"[test_safety_fence_b] {'FAILED ' + str(len(FAILS)) if FAILS else 'ALL OK'}")
    if FAILS:
        for f in FAILS:
            print(f"[test_safety_fence_b]   - {f}")
        sys.exit(1)


if __name__ == "__main__":
    main()
