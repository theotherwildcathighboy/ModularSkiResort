"""
test_beacon - gates for assets/blender/beacon.py (SkiBeaconRedA and build/beacon.md).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_beacon.py [-- --quick]

The test exits 1 on any failure. --quick skips the two child processes. Checks:
- determinism: two in-process builds hash identically. The hash covers vertex coordinates (%.5f),
  face vertex lists, the "UVMap" loops, corner normals and the part table. paint_maps() returns
  equal arrays twice.
- fresh: two child Blender processes export (--export --no-measure) into temp folders. The five PNGs
  and settings.json are byte-identical. The FBX carries the export clock, so the test compares the
  FBX by its re-imported geometry instead: tris, extents, normals present.
- caps: LOD0 tris == beacon.TRIS (86) <= beacon.LOD0_CAP (100). There are exactly five maps, all 128
  x 128 RGBA. There is no LOD FBX, and settings.json == {} (single LOD by design). The VRAM of the
  five maps with mips is <= 0.5 MB uncompressed.
- emissive: in _Emissive.png, every texel covered by a lens face (eroded 1 texel) is red (R >= 0.75,
  G and B <= 0.2). Every texel covered by a non-lens face is black. The UVs of the non-lens faces
  sit on texel centers 0.5 texel inside their rects, so bilinear filtering never reaches the lens.
  BaseColor of the lens texels is also red-dominant (the unlit look between flashes).
- contracts: beacon.contracts() holds (ground, 0.26 m rim, cage clearance, outward normals, UV
  rules, lens faces inside the lens block).
- validator: cs2_validate.validate_asset_folder_ex(kind prop, lod0_cap 100) gives 0 errors on the
  temp export. It also gives 0 errors on the shipped folder assets/import/props/A/SkiBeaconRedA
  (when present).
- measure: beacon.measure_anchors() on the shipped tower FBX gives 7 rows, center hit normal +y,
  footprint flat <= 5 mm, and nothing inside the beacon envelope. A row SKIPs when its FBX is
  missing.
"""

import hashlib
import json
import os
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
import beacon as B          # noqa: E402
import cs2_asset            # noqa: E402
import cs2_validate         # noqa: E402

QUICK = "--quick" in sys.argv
FAILS = []
SKIPS = []


def ok(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"[test_beacon] FAIL {msg}")
    else:
        print(f"[test_beacon] ok   {msg}")


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


def sub_mesh(obj, keep_lens):
    """Copy of obj.data holding only the lens faces (keep_lens) or only the others."""
    lens = set(B.part_faces(obj, "lens"))
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    kill = [f for f in bm.faces if (f.index in lens) != keep_lens]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    me = bpy.data.meshes.new("test_beacon_sub")
    bm.to_mesh(me)
    bm.free()
    return me


def dilate(mask):
    """Boolean dilation by one texel (8-neighborhood), no wrap-around at the atlas edges."""
    p = np.pad(mask, 1, constant_values=False)
    out = np.zeros_like(mask)
    h, w = mask.shape
    for dy in (0, 1, 2):
        for dx in (0, 1, 2):
            out |= p[dy:dy + h, dx:dx + w]
    return out


def run_child(out_dir):
    exe = bpy.app.binary_path
    cmd = [exe, "--background", "--factory-startup", "--python-exit-code", "1", "--python",
           os.path.join(HERE, "beacon.py"), "--", "--out", out_dir, "--export", "--no-measure"]
    r = subprocess.run(cmd, cwd=HERE, capture_output=True, text=True, timeout=600)
    return r.returncode, r.stdout[-2000:] + r.stderr[-2000:]


def check_folder(asset_dir, label):
    findings, summary = cs2_validate.validate_asset_folder_ex(asset_dir, B.NAME, B.VALIDATE_SPEC)
    errs = [f for f in findings if f[0] == "ERROR"]
    warns = [f for f in findings if f[0] == "WARN"]
    ok(not errs, f"{label}: validator 0 errors (got {len(errs)}: {errs[:3]}), {len(warns)} warnings")
    ok(summary["tris"].get(0) == B.TRIS, f"{label}: FBX LOD0 tris {summary['tris']} == {B.TRIS}")
    ok(summary["tex"] == {B.A}, f"{label}: texture sizes {summary['tex']} == {{{B.A}}}")


def main():
    B.clear_scene()

    # -- determinism (in-process) + contracts -------------------------------------------
    a = B.build_beacon("BeaconTestA")
    held = B.contracts(a)
    ha = mesh_hash(a)
    b = B.build_beacon("BeaconTestB")
    hb = mesh_hash(b)
    ok(ha == hb, f"two builds hash identically ({ha[:12]} / {hb[:12]})")
    remove(b)
    m1, m2 = B.paint_maps(), B.paint_maps()
    ok(all(np.array_equal(m1[k][0], m2[k][0]) for k in m1), "paint_maps deterministic (5 maps)")
    ok(held["tris"] == B.TRIS <= B.LOD0_CAP, f"LOD0 tris {held['tris']} == {B.TRIS} <= {B.LOD0_CAP}")
    ok(abs(held["lens_rim_diameter_m"] - 0.26) < 1e-3, f"lens rim {held['lens_rim_diameter_m']} m")
    reach = B.reach_table(a)
    ok(reach["cull_m_at_surgery_bias"] >= 200.0,
       f"cull at m_Bias {B.LOD_BIAS} = {reach['cull_m_at_surgery_bias']} m >= 200 m (formula)")

    # -- export into a temp folder ----------------------------------------------------------
    tmp = tempfile.mkdtemp(prefix="test_beacon_")
    try:
        asset_dir, obj, data = B.export(tmp, measure=False)
        files = sorted(os.listdir(asset_dir))
        pngs = [f for f in files if f.endswith(".png")]
        ok(len(pngs) == 5 and f"{B.NAME}_Emissive.png" in pngs, f"five maps incl. _Emissive: {pngs}")
        ok(not [f for f in files if "_LOD" in f], "no LOD FBX (single LOD by design)")
        with open(os.path.join(asset_dir, "settings.json"), encoding="utf-8") as fh:
            ok(json.load(fh) == {}, "settings.json == {} (LOD0 only)")
        sizes = {cs2_validate.read_png_header(os.path.join(asset_dir, f))[:2] for f in pngs}
        ok(sizes == {(B.A, B.A)}, f"all maps {B.A} x {B.A}: {sizes}")
        ctypes = {cs2_validate.read_png_header(os.path.join(asset_dir, f))[3] for f in pngs}
        ok(ctypes == {6}, f"all maps RGBA (color type 6): {ctypes}")
        vram = 5 * B.A * B.A * 4 * 4 / 3 / 2 ** 20
        ok(vram <= 0.5, f"VRAM five maps uncompressed with mips {vram:.3f} MB <= 0.5")
        check_folder(asset_dir, "temp export")

        # -- emissive region ---------------------------------------------------------------
        emi = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{B.NAME}_Emissive.png"))
        base = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{B.NAME}_BaseColor.png"))
        me_l, me_o = sub_mesh(obj, True), sub_mesh(obj, False)
        try:
            cov_l = cs2_validate.uv_coverage(me_l, B.A) > 0
            cov_o = cs2_validate.uv_coverage(me_o, B.A) > 0
        finally:
            bpy.data.meshes.remove(me_l)
            bpy.data.meshes.remove(me_o)
        core = cs2_validate._erode(cov_l)
        rgb = emi[..., :3]
        red = (rgb[..., 0] >= 0.75) & (rgb[..., 1] <= 0.2) & (rgb[..., 2] <= 0.2)
        ok(int(core.sum()) > 500 and bool(red[core].all()),
           f"lens texels emissive red: {int(red[core].sum())} / {int(core.sum())}")
        lit = rgb.max(axis=-1) > 1.0 / 255.0
        ok(int(cov_o.sum()) > 500 and not bool(lit[cov_o].any()),
           f"non-lens texels black in _Emissive: {int(lit[cov_o].sum())} lit of {int(cov_o.sum())}")
        ok(not bool(lit[dilate(cov_o) & ~dilate(cov_l)].any()),
           "no emissive texel next to a non-lens face away from the lens block edge")
        ok(not bool((cov_l & cov_o).any()), "lens and non-lens UVs do not share a texel")
        lb = base[..., :3][core]
        ok(bool((lb[:, 0] > 2.5 * lb[:, 1]).all()), "lens BaseColor red-dominant (unlit glass)")
        ok(bool((emi[..., 3] > 0.999).all()), "_Emissive alpha 255 everywhere")

        # -- fresh processes ------------------------------------------------------------------
        if QUICK:
            SKIPS.append("fresh-process check (--quick)")
        else:
            d1, d2 = os.path.join(tmp, "p1"), os.path.join(tmp, "p2")
            r1, log1 = run_child(d1)
            r2, log2 = run_child(d2)
            ok(r1 == 0 and r2 == 0, f"child exports exit 0 ({r1}, {r2})" + ("" if r1 == r2 == 0
                                                                            else f": {log1}{log2}"))
            if r1 == 0 and r2 == 0:
                names = [f"{B.NAME}_{m}.png" for m in ("BaseColor", "Normal", "MaskMap",
                                                       "ControlMask", "Emissive")] + ["settings.json"]
                same = []
                for n in names:
                    with open(os.path.join(d1, B.NAME, n), "rb") as f1, \
                            open(os.path.join(d2, B.NAME, n), "rb") as f2:
                        same.append(f1.read() == f2.read())
                ok(all(same), f"fresh processes: {sum(same)} / {len(names)} files byte-identical")
                g = []
                for d in (d1, d2):
                    objs = cs2_validate.import_fbx_temp(os.path.join(d, B.NAME, B.NAME + ".fbx"))
                    me = [o for o in objs if o.type == "MESH"][0].data
                    co = np.array([v.co[:] for v in me.vertices])
                    g.append((len(me.polygons), tuple(np.round(co.min(0), 2)),
                              tuple(np.round(co.max(0), 2)), me.has_custom_normals))
                    cs2_validate.remove_objects(objs)
                ok(g[0] == g[1], f"fresh FBX geometry equal: {g[0]}")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    # -- the shipped folder -------------------------------------------------------------------
    shipped = os.path.join(REPO, "assets", "import", "props", "A", B.NAME)
    if os.path.isdir(shipped):
        check_folder(shipped, "shipped folder")
        man = os.path.join(shipped, f"{B.NAME}.manifest.json")
        if os.path.isfile(man):
            with open(man, encoding="utf-8") as fh:
                m = json.load(fh)
            ok(m.get("tris", {}).get("LOD0") == B.TRIS, f"shipped manifest tris {m.get('tris')}")
    else:
        SKIPS.append(f"shipped folder {shipped} absent")

    # -- anchors on the shipped towers -----------------------------------------------------
    rows = B.measure_anchors()
    ok(len(rows) == 7, f"{len(rows)} host rows")
    for r in rows:
        if "error" in r and "missing" in r["error"]:
            SKIPS.append(f"{r['prefab']}: {r['error']}")
            continue
        ok(r.get("ok"), f"{r['prefab']} anchor {r.get('anchor')} (flat {r.get('footprint_flat_m')}, "
                        f"env {r.get('envelope_verts')}, up {r.get('up_hits')}, side {r.get('side_hits')})")
    remove(a)


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        FAILS.append("exception")
    for s in SKIPS:
        print(f"[test_beacon] SKIP {s}")
    print(f"[test_beacon] {'PASS' if not FAILS else 'FAIL'}: {len(FAILS)} failure(s), "
          f"{len(SKIPS)} skip(s)")
    sys.exit(1 if FAILS else 0)
