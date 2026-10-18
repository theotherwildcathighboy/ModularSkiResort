"""
T63-tiled gate: the review packs of the eight TILED W buildings carry their textures.

The 2026-10-17 W packs were flat untextured meshes: cs2_review.write_pack only looked for
<Name>_<Map>.png, while a tiled build has LOD0 on the family sheet (_SkiTileSheetW) and the far
set as <Name>_LOD1_<Map>.png (docs/research/field/2026-10-15/build/T63-review-packs-tiled.md).

  A. Each of the eight packs in assets/preview/review/ is LINKED the way review_live.load does
     (fresh process; every image's pixels are read): props tiled == 1; LOD0's material uses the
     sheet maps (<sheet>_<Map>.png, >= 4 of them, texture_px square), LOD1 and LOD2 use the far
     set <Name>_LOD1_<Map>.png (4 maps, no emissive, far_px square) through their own UV layer;
     the `_Win` sub keeps the window mock (no image); every image of the pack resolves under
     <review>/_tex/<Name>/ (//_tex/ relative), has pixels, 0 missing, count == n_images > 0; the
     eight staged sheet BaseColors are byte-identical (one family sheet).
  B. Negative: write_folder_pack of an EXPORTED folder with no maps RAISES and writes no pack, and
     so does a tiled folder whose sheet cannot be found (far set only); a synthetic empty folder
     (not exported: test_review_subobjects' packs) still writes.
  C. Path parity: write_pack called the generator's way (lodge.py: manifest={"sub_objects": ...},
     no tiled block in the argument) gives the same per-LOD texture files as write_folder_pack, also
     for a first build with NO manifest on disk (sheet found as ../_SkiTileSheet<L>).
     Source: the closelook export of SkiPatrolHutCommercialLowW (assets/preview/fittings-closelook-
     2026-10-17/assets/W); temp copies only, nothing under assets/preview/review is written.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_review_tiled.py

Exit 1 on any failure; one PASS line on success.
"""
import hashlib
import json
import os
import shutil
import sys
import tempfile
import time

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(HERE)
sys.path.insert(0, LIB)
import cs2_review  # noqa: E402

REVIEW_DIR = cs2_review.DEFAULT_OUT_DIR
W_PACKS = ("SkiLodgeAFrameCommercialLowW", "SkiLodgeChaletCommercialLowW",
           "SkiLodgeHutCommercialLowW", "SkiLodgeSwankyBottomCommercialLowW",
           "SkiLodgeSwankyTopCommercialLowW", "SkiPatrolHQCommercialLowW",
           "SkiPatrolHutCommercialLowW", "SkiSchoolCommercialLowW")
SHEET_MAPS = ("BaseColor", "ControlMask", "MaskMap", "Normal")    # + Emissive when bound
FAR_MAPS = cs2_review.FAR_MAPS
SRC_ROOT = os.path.join(cs2_review.REPO_ROOT, "assets", "preview", "fittings-closelook-2026-10-17",
                        "assets", "W")
SRC_NAME = "SkiPatrolHutCommercialLowW"

FAILS = []


def check(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"[tiled-test] FAIL: {msg}")
    return cond


def sha1(path):
    h = hashlib.sha1()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def norm(p):
    return os.path.normcase(os.path.normpath(os.path.abspath(p)))


def tex_nodes(mat):
    return [n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE"] if mat and mat.node_tree else []


def uv_node(mat):
    return next((n for n in mat.node_tree.nodes if n.type == "UVMAP"), None)


# --------------------------------------------------------------------------------------
# A: the eight packs
# --------------------------------------------------------------------------------------

def check_pack(name):
    path = os.path.join(REVIEW_DIR, f"{name}.blend")
    if not check(os.path.isfile(path), f"{name}: no pack at {path}"):
        return None
    coll = f"{cs2_review.PACK_PREFIX}{name}"
    with bpy.data.libraries.load(path, link=True, relative=False) as (src, dst):
        if not check(coll in src.collections, f"{name}: pack holds {list(src.collections)}"):
            return None
        dst.collections = [coll]
    pack = dst.collections[0]
    lib = pack.library
    out = {"name": name, "bytes": os.path.getsize(path)}
    try:
        p = {k: pack[k] for k in pack.keys()}
        roles = {(c.get("cs2r_role") or c.name.split(".")[0]): c for c in pack.children}
        want = set(cs2_review.ROLES) | ({"SUB_NOPROPS"} if int(p.get("n_prop_standins", 0)) else set())
        check(set(roles) == want, f"{name}: children {sorted(roles)} != {sorted(want)}")
        check(int(p.get("tiled", 0)) == 1, f"{name}: tiled prop {p.get('tiled')}")
        sheet = str(p.get("sheet") or "")
        check(sheet.startswith("SkiTileSheet"), f"{name}: sheet {sheet!r} is not the family sheet")
        A, Af = int(p.get("texture_px", 0)), int(p.get("far_px", 0))
        check(A > Af > 0, f"{name}: texture_px {A} / far_px {Af}")
        tex_dir = norm(os.path.join(REVIEW_DIR, "_tex", name))

        def lod_obj(role):
            objs = [o for o in roles[role].objects if o.type == "MESH"] if role in roles else []
            check(len(objs) == 1, f"{name}: {role} holds {[o.name for o in objs]}")
            return objs[0] if objs else None

        mats = {}
        for role in ("LOD0", "LOD1", "LOD2"):
            o = lod_obj(role)
            if o is None:
                continue
            mat = o.data.materials[0] if len(o.data.materials) else None
            mats[role] = mat
            nodes = tex_nodes(mat)
            labels = {n.label: n for n in nodes}
            uvn = uv_node(mat)
            check(uvn is not None and uvn.uv_map in o.data.uv_layers,
                  f"{name}: {role} UVMap node {getattr(uvn, 'uv_map', None)!r} not a layer of "
                  f"{[l.name for l in o.data.uv_layers]}")
            if role == "LOD0":
                check(set(SHEET_MAPS) <= set(labels), f"{name}: LOD0 maps {sorted(labels)}")
                for lab, n in labels.items():
                    f = os.path.basename(bpy.path.abspath(n.image.filepath, library=lib))
                    check(f == f"{sheet}_{lab}.png", f"{name}: LOD0 {lab} uses {f}, not the sheet")
                    check(tuple(n.image.size) == (A, A), f"{name}: LOD0 {lab} size {tuple(n.image.size)}")
                out["lod0"] = sorted(labels)
            else:
                check(set(labels) == set(FAR_MAPS), f"{name}: {role} maps {sorted(labels)} != far set")
                for lab, n in labels.items():
                    f = os.path.basename(bpy.path.abspath(n.image.filepath, library=lib))
                    check(f == f"{name}_LOD1_{lab}.png", f"{name}: {role} {lab} uses {f}, not _LOD1_")
                    check(tuple(n.image.size) == (Af, Af), f"{name}: {role} {lab} size {tuple(n.image.size)}")
                out[role.lower()] = sorted(labels)
        check(mats.get("LOD0") is not None and mats.get("LOD0") != mats.get("LOD1"),
              f"{name}: LOD0 and LOD1 share one material")
        win = [o for o in roles["SUB"].objects if o.get("cs2r_suffix", "").startswith(
            cs2_review.WIN_FAMILY)] if "SUB" in roles else []
        for o in win:
            m = o.data.materials[0]
            check(m.name.endswith("_Win") and not tex_nodes(m), f"{name}: {o.name} material {m.name}")
        out["win"] = len(win)

        imgs = [i for i in bpy.data.images if i.library == lib]
        missing = []
        for img in imgs:
            check(img.filepath.startswith(("//_tex/", "//_tex\\")),
                  f"{name}: {img.name} not //_tex relative: {img.filepath}")
            real = norm(bpy.path.abspath(img.filepath, library=lib))
            if not os.path.isfile(real) or tuple(img.size) == (0, 0):
                missing.append(img.name)
            check(os.path.dirname(real) == tex_dir, f"{name}: {img.name} -> {real} outside _tex/{name}")
        check(not missing, f"{name}: missing images {missing}")
        check(len(imgs) > 0 and len(imgs) == int(p.get("n_images", -1)),
              f"{name}: {len(imgs)} images linked, n_images {p.get('n_images')}")
        out.update(images=len(imgs), missing=len(missing), sheet=sheet, A=A, Af=Af,
                   sheet_base=os.path.join(tex_dir, f"{sheet}_BaseColor.png"),
                   source=p.get("asset_dir"), pack_from=p.get("pack_from"), extra=p.get("extra_meshes"))
        return out
    finally:
        bpy.data.libraries.remove(lib)


# --------------------------------------------------------------------------------------
# B / C: negatives and path parity (temp copies)
# --------------------------------------------------------------------------------------

def pack_tex(path, name):
    with bpy.data.libraries.load(path, link=True, relative=False) as (src, dst):
        dst.collections = [f"{cs2_review.PACK_PREFIX}{name}"]
    pack = dst.collections[0]
    try:
        return {k: pack.get(k) for k in ("tex_LOD0", "tex_LOD1", "tex_LOD2", "tiled", "sheet",
                                         "sheet_source", "n_images")}
    finally:
        bpy.data.libraries.remove(pack.library)


def expect_raise(label, fn, out_pack):
    try:
        fn()
    except ValueError as exc:
        check("NO image" in str(exc), f"{label}: raised, but not the zero-image gate: {exc}")
        check(not os.path.isfile(out_pack), f"{label}: a pack was written although it raised")
        print(f"[tiled-test] B {label}: raised as required ({str(exc)[:110]}...)")
        return
    check(False, f"{label}: did NOT raise (a textureless pack was written)")


def generator_way(name, folder, out):
    """write_pack exactly as lodge.py calls it: imported objects, manifest = sub_objects only."""
    man = cs2_review._read_json(os.path.join(SRC_ROOT, name, f"{name}.manifest.json")) or {}
    before = set(bpy.data.objects)
    try:
        lods = {0: cs2_review.import_exported_fbx(os.path.join(folder, f"{name}.fbx"))}
        for k in (1, 2):
            lods[k] = cs2_review.import_exported_fbx(os.path.join(folder, f"{name}_LOD{k}.fbx"))
        win = cs2_review.import_exported_fbx(os.path.join(folder, f"{name}_Win.fbx"))
        return cs2_review.write_pack(name, folder, lods, {"Win": win},
                                     meta={"generator": "lodge.py", "kind": "building"},
                                     out_dir=out, manifest={"sub_objects": man.get("sub_objects")})
    finally:
        new = [o for o in bpy.data.objects if o not in before]
        meshes = [o.data for o in new]
        bpy.data.batch_remove(new + meshes)


def negatives_and_parity(work):
    src = os.path.join(SRC_ROOT, SRC_NAME)
    if not check(os.path.isdir(src), f"source export missing: {src}"):
        return {}
    n = SRC_NAME
    res = {}
    # B1: exported folder, FBXs + manifest + settings, no PNG anywhere
    b1 = os.path.join(work, "b1", "W", n)
    os.makedirs(b1)
    for f in os.listdir(src):
        if not f.lower().endswith(".png"):
            shutil.copyfile(os.path.join(src, f), os.path.join(b1, f))
    out_b = os.path.join(work, "review_b")
    expect_raise("no maps", lambda: cs2_review.write_folder_pack(b1, out_dir=out_b),
                 os.path.join(out_b, f"{n}.blend"))
    # B2: the far set only (the sheet is not beside the folder and the owner is absent)
    b2 = os.path.join(work, "b2", "W", n)
    shutil.copytree(src, b2)
    expect_raise("far set without the sheet", lambda: cs2_review.write_folder_pack(b2, out_dir=out_b),
                 os.path.join(out_b, f"{n}.blend"))
    # B3: a synthetic, NOT exported folder still writes an untextured pack (subobject tests)
    syn = os.path.join(work, "syn", "SkiSynthTiledA")
    os.makedirs(syn)
    me = bpy.data.meshes.new("SkiSynthTiledA")
    me.from_pydata([(0, 0, 0), (1, 0, 0), (0, 1, 0)], [], [(0, 1, 2)])
    ob = bpy.data.objects.new("SkiSynthTiledA", me)
    try:
        p = cs2_review.write_pack("SkiSynthTiledA", syn, {0: ob}, {}, manifest={}, out_dir=out_b,
                                  meta={"kind": "prop"})
        check(os.path.isfile(p), "B3: the synthetic (not exported) pack was not written")
    except ValueError as exc:
        check(False, f"B3: a synthetic, not exported folder raised: {exc}")
    finally:
        bpy.data.objects.remove(ob)
        bpy.data.meshes.remove(me)

    # C: path parity. Folder copy with its sheet beside it (as lodge's --out root lays it out)
    root = os.path.join(work, "c", "W")
    shutil.copytree(src, os.path.join(root, n))
    shutil.copytree(os.path.join(SRC_ROOT, "_SkiTileSheetW"), os.path.join(root, "_SkiTileSheetW"))
    pf = cs2_review.write_folder_pack(os.path.join(root, n), out_dir=os.path.join(work, "rv_folder"))
    pg = generator_way(n, os.path.join(root, n), os.path.join(work, "rv_gen"))
    tf, tg = pack_tex(pf, n), pack_tex(pg, n)
    for k in ("tex_LOD0", "tex_LOD1", "tex_LOD2", "tiled", "sheet"):
        check(tf.get(k) == tg.get(k), f"C parity {k}: folder {tf.get(k)!r} != generator {tg.get(k)!r}")
    check(tf.get("sheet_source") == "manifest", f"C folder sheet_source {tf.get('sheet_source')}")
    # C2: first build, no manifest on disk: the sheet is found as ../_SkiTileSheetW
    os.remove(os.path.join(root, n, f"{n}.manifest.json"))
    pg2 = generator_way(n, os.path.join(root, n), os.path.join(work, "rv_gen2"))
    tg2 = pack_tex(pg2, n)
    for k in ("tex_LOD0", "tex_LOD1", "tex_LOD2", "tiled", "sheet"):
        check(tg2.get(k) == tf.get(k), f"C2 (no manifest) {k}: {tg2.get(k)!r} != {tf.get(k)!r}")
    check(tg2.get("sheet_source") == "letter", f"C2 sheet_source {tg2.get('sheet_source')}")
    res.update(folder=tf, generator=tg, first_build=tg2)
    print(f"[tiled-test] C parity: LOD0 {tf.get('tex_LOD0')} | LOD1 {tf.get('tex_LOD1')} | LOD2 "
          f"{tf.get('tex_LOD2')}; generator way identical; no-manifest build: sheet via "
          f"{tg2.get('sheet_source')}")
    return res


def main():
    t0 = time.perf_counter()
    rows = []
    for name in W_PACKS:
        r = check_pack(name)
        if r:
            rows.append(r)
            print(f"[tiled-test] A {name}: {r['bytes']} bytes, {r['images']} images, {r['missing']} "
                  f"missing; LOD0 {r.get('lod0')} on {r['sheet']} {r['A']} px; LOD1/2 far set "
                  f"{r['Af']} px; Win subs {r['win']}; from {r['pack_from']} {r['source']}")
    bases = {r["name"]: sha1(r["sheet_base"]) for r in rows if os.path.isfile(r["sheet_base"])}
    check(len(bases) == len(W_PACKS) and len(set(bases.values())) == 1,
          f"staged sheet BaseColors differ / missing: {bases}")
    work = tempfile.mkdtemp(prefix="t63tiled_")
    try:
        negatives_and_parity(work)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    if FAILS:
        print(f"[tiled-test] {len(FAILS)} FAIL(s)")
        sys.exit(1)
    print(f"[tiled-test] PASS: 8 tiled W packs textured (sheet LOD0 / far-set LOD1-2, "
          f"{sum(r['images'] for r in rows)} images, 0 missing); no-map folders raise; generator and "
          f"folder paths agree ({time.perf_counter() - t0:.1f} s)")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        import traceback
        traceback.print_exc()
        sys.exit(1)
