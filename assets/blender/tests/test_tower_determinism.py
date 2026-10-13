"""
Determinism gate for the lift_tower v2 port (towers-v2.md section 14.1 + 14.5).

Builds FG8, ET68, DQ12 (rebuild head), ETDQ, DQ12 (Kagelok ingest head), the Revision 1
taller members FG20 and DQ16 (ingest head) and the Revision 1b gondola tubular family
GD12 / GD16 / GD20 (rebuild head) twice each in
one Blender process - parts, bevels, part tags, the kit "UVMap", join, remove_doubles,
shading, contracts, orient_for_net and the authored LOD chain (no bake) - and hashes the
vertex coordinates (%.5f), face vertex lists, the "UVMap" loops, the part table and both
LOD levels' geometry (the LOD chain is in the hash because p0a-integration O9 found the
station LODs drift across processes). Two builds must hash identically: the unique pack,
the AO bake and the decimation are all order-dependent. The ingest pair SKIPS with a
printed line when the gitignored Kagelok .blend is absent.

Detail pass 1 (design 6.1 DT-C11, 2026-10-11): the hash also covers EVERY UV layer (the metric
"UVMap" and the tile-projection layer when present) and the corner normals (the sheave liners,
chamfers and metric UVs are new order-sensitive steps); the new members DQ18 (ingest) and ETGD
(rebuild) join the cases; and a FRESH-PROCESS check runs main_v2 for FG8 at --atlas 256
--ao-samples 4 in two child Blender processes: the four PNGs and settings.json must be
byte-identical (the FBX carries the export clock and is compared in-process by
test_tower_contracts review_case instead).

Follow-up 2026-10-11 (DQ20 + sub-texel faces): DQ20 (ingest head) joins the in-process cases, and
the fresh-process check also bakes DQ18 at the SHIPPING atlas (1024, --ao-samples 4) in two child
processes: its sub-texel fill (lift_tower.fill_subtexel_faces, 19 triangles on DQ18, a bmesh
triangulation + a dilated raster in a fixed face order) must give byte-identical PNGs, and the
manifest must report the same fill and black_after 0. F3b (2026-10-11): the fill gained a second,
any-area pass (black texels under every triangle, separating-axis overlap, fixed face order); the
same two-process comparison covers it (DQ18 at the shipping atlas, FG8 at the
256 atlas), and the manifest fill stats (now with f3b_*) must still agree between the runs.

Legacy stability (section 14.5): build_tower(TowerParams(8.0), finish=True, pier=True) -
the path lift_terminal and the shipped N towers use - must hash exactly as it did before
the port (recorded 2026-10-11 from the pre-port lift_tower.py: 1332 verts, 1137 faces,
2370 tris = FG8N m_IndexCount 7,110 / 3).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_tower_determinism.py
"""
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import time

import bpy

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_asset  # noqa: E402
import lift_tower as LT  # noqa: E402

LEGACY_FG8_PIER_FINISH = "bdb614deb3a732e1b350258ffb7858613d26fb78"   # recorded pre-port
LEGACY_FG8_COUNTS = (1332, 1137, 2370)


def _hash_mesh(h, me, uv_name, all_uv=False, normals=False):
    for v in me.vertices:
        h.update(("%.5f %.5f %.5f;" % tuple(v.co)).encode())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    names = sorted(l.name for l in me.uv_layers) if all_uv else ([uv_name] if uv_name is not None else [])
    for nm in names:
        if all_uv:                                # (the legacy / pre-pass hashes carry no name)
            h.update(nm.encode())
        for lp in me.uv_layers[nm].data:
            h.update(("%.6f %.6f;" % tuple(lp.uv)).encode())
    if normals:                                   # corner (split) normals, detail pass 1
        for cn in me.corner_normals:
            h.update(("%.4f %.4f %.4f;" % tuple(cn.vector)).encode())


def build_hash(key, head=None):
    LT.clear_scene()
    spec = LT.VARIANTS[key]
    t, table, _src, _info = LT.build_tower_v2(spec, head, finish=False)
    lods = LT.lod_chain_v2(t, t.name, spec)
    h = hashlib.sha1()
    _hash_mesh(h, t.data, "UVMap", all_uv=True, normals=True)
    h.update(json.dumps(table).encode())
    counts = [len(t.data.vertices), cs2_asset.tri_count(t)]
    for lod in lods:
        _hash_mesh(h, lod.data, None, normals=True)
        counts.append(cs2_asset.tri_count(lod))
        me = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(me)
    return h.hexdigest()[:12], tuple(counts)


def legacy_hash():
    LT.clear_scene()
    t = LT.build_tower(LT.TowerParams(mast_height=8.0), finish=True, pier=True)
    h = hashlib.sha1()
    _hash_mesh(h, t.data, t.data.uv_layers.active.name)
    return h.hexdigest(), (len(t.data.vertices), len(t.data.polygons), LT.tri_count(t))


def fresh_process_case(variant="FG8", atlas=256):
    """DT-C11: main_v2 <variant> (--ao-samples 4, no LODs / validation) in two CHILD Blender
    processes; every PNG and settings.json byte-identical (FG8 at --atlas 256; the follow-up adds
    DQ18 at the shipping atlas, where the F3 sub-texel fill runs and the manifest's fill stats and
    black_after must agree too). Returns (ok, sha summary)."""
    here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    work = tempfile.mkdtemp(prefix="tower_fresh_")
    shas = []
    fills = []
    for run in ("a", "b"):
        out = os.path.join(work, run)
        cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
               "--python", os.path.join(here, "lift_tower.py"), "--",
               "--variant", variant, "--out", out, "--atlas", str(atlas), "--ao-samples", "4", "--no-lods",
               "--no-validate"]
        r = subprocess.run(cmd, cwd=here, capture_output=True, text=True, timeout=900)
        if r.returncode != 0:
            print(f"[det] fresh-process {variant} run {run} failed: {r.stdout[-400:]} {r.stderr[-400:]}")
            return False, "child failed"
        name = f"SkiLiftTower{variant}O"
        d = os.path.join(out, name)
        hs = {}
        for fn in sorted(os.listdir(d)):
            if fn.endswith(".png") or fn == "settings.json":
                with open(os.path.join(d, fn), "rb") as fh:
                    hs[fn] = hashlib.sha1(fh.read()).hexdigest()
        shas.append(hs)
        with open(os.path.join(d, f"{name}.manifest.json"), encoding="utf-8") as fh:
            fills.append(json.load(fh)["detail_pass_1"]["subtexel_fill"])
    clean = fills[0]["black_after"] == 0 or atlas != 1024          # the F3 contract is defined at the shipping atlas
    same = shas[0] == shas[1] and len(shas[0]) == 5 and fills[0] == fills[1] and clean
    print(f"[det] fresh-process {variant} @ {atlas} (2 child processes): {len(shas[0])} files "
          f"{'SAME' if same else 'DIFFERENT'} {sorted((k, v[:10]) for k, v in shas[0].items())}; "
          f"F3 fill {fills[0]['faces']} triangles / {fills[0]['texels']} texels, black after "
          f"{fills[0]['black_after']} (run b: {fills[1]['faces']} / {fills[1]['texels']})")
    return same, f"{variant}@{atlas}: " + ", ".join(f"{k.split('_')[-1]} {v[:8]}" for k, v in sorted(shas[0].items()))


def main():
    t0 = time.perf_counter()
    ok = True
    cases = [("FG8", None), ("ET68", None), ("DQ12", "rebuild"), ("ETDQ", "rebuild"), ("DQ12", "ingest"),
             ("FG20", None), ("DQ16", "ingest"),          # Revision 1: the taller members
             ("GD12", "rebuild"), ("GD16", "rebuild"), ("GD20", "rebuild"),   # Revision 1b: gondola
             ("DQ18", "ingest"), ("ETGD", "rebuild"),     # detail pass 1: the new members
             ("DQ20", "ingest")]                          # follow-up 2026-10-11: DQ20 beside DQ18
    skipped = []
    for key, head in cases:
        if head == "ingest" and not os.path.isfile(LT.KAGELOK_BLEND):
            print(f"[det] SKIP {key} ingest: {LT.KAGELOK_BLEND} absent (gitignored original)")
            skipped.append(f"{key}/{head}")
            continue
        a = build_hash(key, head)
        b = build_hash(key, head)
        same = a == b
        ok = ok and same
        print(f"[det] {key}/{head or 'yan'}: run1 {a} run2 {b} -> {'SAME' if same else 'DIFFERENT'}")
    fresh = fresh_process_case()
    ok = ok and fresh[0]
    fresh_f3 = fresh_process_case("DQ18", 1024)            # follow-up: the F3 fill, at the shipping atlas
    ok = ok and fresh_f3[0]
    lh, lc = legacy_hash()
    legacy_ok = lh == LEGACY_FG8_PIER_FINISH and lc == LEGACY_FG8_COUNTS
    ok = ok and legacy_ok
    print(f"[det] legacy build_tower(8.0, finish, pier): {lh[:12]} {lc} vs recorded "
          f"{LEGACY_FG8_PIER_FINISH[:12]} {LEGACY_FG8_COUNTS} -> {'SAME' if legacy_ok else 'DIFFERENT'}")
    if not ok:
        print("FAIL test_tower_determinism: builds differ (see [det] lines)")
        sys.exit(1)
    print(f"PASS test_tower_determinism: {len(cases) - len(skipped)} v2 builds hash identically "
          f"twice (geometry + UVMap + part table + LOD1/LOD2), legacy FG8 path unchanged"
          f"{'; skipped ' + ', '.join(skipped) if skipped else ''}; fresh-process FG8 PNGs + settings.json "
          f"byte-identical ({fresh[1]}); fresh-process DQ18 @ 1024 with the F3 fill byte-identical "
          f"({fresh_f3[1]}) ({time.perf_counter() - t0:.0f} s)")


try:
    main()
except SystemExit:
    raise
except Exception:  # noqa: BLE001
    import traceback
    traceback.print_exc()
    print("FAIL test_tower_determinism: exception")
    sys.exit(1)
