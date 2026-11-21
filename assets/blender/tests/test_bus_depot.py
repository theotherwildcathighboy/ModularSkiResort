"""
test_bus_depot - determinism, budget and flag-off identity of the Ski Bus Depot generator (ski_bus_depot.py).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_bus_depot.py [-- --quick]

All builds run in ONE Blender process, into scratch folders (never assets/import). Exit 1 on any failure.

B1  blockout x 2: the pre-bake mesh hash (vertices, faces, UVTile, material index, part, density_w), the `_Gls` hash
    and the tri count are equal. LOD0 is inside the budget (<= cap) and equals LOD0_TRIS, the released mesh.
B2  flag-off geometry pin: the blockout's pre-bake hash equals FLAG_OFF_PREBAKE (the released building: lot, posts,
    panels, pivots; a change here is a new mesh generation, not a texture pass).
B3  reduced finish x 2 (--tex 1024 --ao-samples 4 --no-validate): the five PNGs (incl. `_Emissive`) are byte-identical,
    the LOD0 / LOD1 / LOD2 / `_Gls` mesh hashes and tri counts are equal, LOD1 <= 0.50 x LOD0, LOD2 <= its cap, every
    BaseColor alpha is 255.
B4  manifest: <Name>.manifest.json sits beside settings.json and records tris, budget, ppm_by_material, texture memory
    and bytes + sha1 of the five PNGs, the five FBX files and settings.json; each sha1 matches the file.
B5  texture pass v1 (reduced finish with --texpass v1): LOD0 tris = the flag-off build + V1_EDGE_TRIS exactly (the
    closed back-wall boards, T273; nothing else moves geometry), the glass sub-mesh is unchanged, the PNG set differs,
    the manifest records the pass and its variants, alpha 255, an unknown --texvar raises.
B6  flag-off identity after a pass: one more flag-off reduced finish in the same process equals B3's PNGs and hashes
    (a pass registers its materials for its build only).
B7  density (--dry-pack at 2048, no bake): flag-off and v1 report px/m per material; under v1 no material's p50 falls
    below its flag-off p50 and the atlas fill does not fall (the pass may only add texels to a part); LOD0 tris differ by
    V1_EDGE_TRIS only.

--quick runs B1, B2 and B7 only (no bake: a light job).

Optional, BUS_DEPOT_SHIPPED_DIR=<folder with the shipped SkiBusDepotA files>: a full flag-off build (the generator's
defaults: 2048, AO 32) must equal that folder's five PNGs byte for byte (the import folder holds no manifest of the
1.0 build, so this is the identity proof of record; about 5 minutes).
"""

import hashlib
import json
import os
import shutil
import sys
import tempfile
import time
import traceback

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import ski_bus_depot as D   # noqa: E402

QUICK = "--quick" in sys.argv
# pinned by the r4/tex audit (2026-11-20): the shipped LOD0 has 1624 tris, and a flag-off rebuild equals the shipped
# LOD0 / LOD1 / LOD2 FBX by content, so this blockout hash is the released mesh
LOD0_TRIS = 1624
FLAG_OFF_PREBAKE = "9cfabfc7e8da"
# texpass v1 closes every back-wall board (a top and a bottom quad each; T273, the see-through gate)
V1_EDGE_TRIS = 2 * 2 * D.BOARD_N * (len(D.YS) - 1) + D.V1_CAP_TRIS     # board edges + hook root / post top caps
FAILS = []


def ok(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"FAIL {msg}")
    return bool(cond)


def sha(path, n=12):
    with open(path, "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()[:n]


def run(argv):
    return D.run(D.parse_args(argv))[0]


def pngs(asset_dir):
    return {f: sha(os.path.join(asset_dir, f)) for f in sorted(os.listdir(asset_dir)) if f.endswith(".png")}


def min_alpha(path):
    im = bpy.data.images.load(path, check_existing=False)
    try:
        w, h = im.size
        a = np.empty(w * h * 4, np.float32)
        im.pixels.foreach_get(a)
        return float(a[3::4].min())
    finally:
        bpy.data.images.remove(im)


def finish(tmp, tag, extra=()):
    out = os.path.join(tmp, tag)
    r = run(["--out", out, "--tex", "1024", "--ao-samples", "4", "--no-validate", *extra])
    d = r["asset_dir"]
    return dict(dir=d, pngs=pngs(d), hashes=dict(r["hashes"]), tris=dict(r["tris"]), glass=r["glass_tris"], result=r)


def main():
    t0 = time.perf_counter()
    tmp = tempfile.mkdtemp(prefix="bus_depot_")
    try:
        # -- B1 / B2: blockout x 2 ---------------------------------------------------------------------
        blk = [run(["--out", os.path.join(tmp, f"blk{i}"), "--blockout"]) for i in range(2)]
        same = (blk[0]["hashes"], blk[0]["tris"], blk[0]["glass_tris"]) == (blk[1]["hashes"], blk[1]["tris"], blk[1]["glass_tris"])
        ok(same, f"B1 blockout builds differ: {blk[0]['hashes']} vs {blk[1]['hashes']}")
        n0 = blk[0]["tris"][0]
        ok(n0 <= D.BUDGET.lod0_cap, f"B1 LOD0 {n0} tris over the cap {D.BUDGET.lod0_cap}")
        if LOD0_TRIS is not None:
            ok(n0 == LOD0_TRIS, f"B1 LOD0 {n0} tris, the released mesh has {LOD0_TRIS}")
        print(f"[bus_depot] B1 blockout x 2: {blk[0]['hashes']} LOD0 {n0} glass {blk[0]['glass_tris']} -> {'SAME' if same else 'DIFFERENT'}")
        if FLAG_OFF_PREBAKE is not None:
            ok(blk[0]["hashes"]["LOD0_prebake"] == FLAG_OFF_PREBAKE,
               f"B2 flag-off pre-bake hash {blk[0]['hashes']['LOD0_prebake']} != the released {FLAG_OFF_PREBAKE}")
        else:
            print(f"[bus_depot] B2 no pin set; this build: {blk[0]['hashes']['LOD0_prebake']}")
        # -- B7: density, no bake ------------------------------------------------------------------------
        dry = {}
        for tag, extra in (("off", ()), ("v1", ("--texpass", "v1"))):
            r = run(["--out", os.path.join(tmp, f"dry_{tag}"), "--dry-pack", *extra])
            dry[tag] = r
            print(f"[bus_depot] B7 {tag}: w=1 {r['pack']['w1_px_per_m']} px/m, fill {r['pack']['fill']}, "
                  f"p50 by material { {k: v[0] for k, v in r['density_by']['material'].items()} }")
        ok(dry["v1"]["tris"][0] == dry["off"]["tris"][0] + V1_EDGE_TRIS,
           f"B7 v1 LOD0 {dry['v1']['tris'][0]} != flag-off {dry['off']['tris'][0]} + {V1_EDGE_TRIS} board-edge tris")
        ok(dry["v1"]["pack"]["fill"] >= dry["off"]["pack"]["fill"], "B7 the pass lowered the atlas fill")
        for k, v in dry["off"]["density_by"]["material"].items():
            if k == D.BAND_KEY or k in D.FLAT_SWATCH:
                continue                # band faces: their UVs are authored into the band, so px/m means nothing there.
                # The flat swatches (hooks, cases) are band faces too: a ramp inside one color rect (T273: the hook root
                # caps moved hook_yellow's p50 with no change on screen)
            w = dry["v1"]["density_by"]["material"].get(k)
            ok(w is not None and w[0] >= v[0] - 0.05, f"B7 {k}: v1 p50 {w and w[0]} px/m under flag-off {v[0]}")
        try:
            D.texpass_config("v1", "cedar=plaid")
            ok(False, "B5 an unknown --texvar did not raise")
        except ValueError:
            pass
        if not QUICK:
            # -- B3: reduced finish x 2 ----------------------------------------------------------------
            fin = [finish(tmp, f"fin{i}") for i in range(2)]
            for key in ("pngs", "hashes", "tris", "glass"):
                same = fin[0][key] == fin[1][key]
                ok(same, f"B3 finish {key} differ: {fin[0][key]} vs {fin[1][key]}")
                print(f"[bus_depot] B3 finish {key}: {fin[0][key]} -> {'SAME' if same else 'DIFFERENT'}")
            ok(len(fin[0]["pngs"]) == 5, f"B3 expected 5 PNGs, got {sorted(fin[0]['pngs'])}")
            t = fin[0]["tris"]
            ok(t[1] <= 0.5 * t[0], f"B3 LOD1 {t[1]} > 0.50 x LOD0 {t[0]}")
            ok(t[2] <= D.BUDGET.lod2_cap, f"B3 LOD2 {t[2]} over its cap {D.BUDGET.lod2_cap}")
            base_png = os.path.join(fin[0]["dir"], f"{D.NAME}_BaseColor.png")
            ok(min_alpha(base_png) >= 1.0, "B3 BaseColor alpha under 255")
            # -- B4: manifest ------------------------------------------------------------------------------
            mp = os.path.join(fin[0]["dir"], f"{D.NAME}.manifest.json")
            if ok(os.path.exists(mp), "B4 no manifest beside settings.json"):
                with open(mp, "r", encoding="utf-8") as fh:
                    man = json.load(fh)
                for field in ("tris", "budget", "ppm_by_material", "texture", "files", "hashes", "pack"):
                    ok(man.get(field), f"B4 manifest lacks {field}")
                files = man.get("files", {})
                ok(sum(f.endswith(".png") for f in files) == 5 and sum(f.endswith(".fbx") for f in files) == 5
                   and "settings.json" in files, f"B4 manifest files: {sorted(files)}")
                for f, rec in files.items():
                    ok(rec["sha1"] == sha(os.path.join(fin[0]["dir"], f), 40), f"B4 manifest sha1 of {f} is stale")
                ok(man["tris"]["lod0"] == t[0] and man["texture"]["size"] == 1024, "B4 manifest tris / texture size")
            # -- B5: the pass --------------------------------------------------------------------------------
            v1 = finish(tmp, "v1", ("--texpass", "v1"))
            ok(v1["tris"][0] == t[0] + V1_EDGE_TRIS and v1["glass"] == fin[0]["glass"],
               f"B5 tris: {v1['tris']} vs {t} + {V1_EDGE_TRIS} board-edge tris")
            ok(v1["tris"][1] <= 0.5 * v1["tris"][0] and v1["tris"][2] <= D.BUDGET.lod2_cap, f"B5 LOD caps: {v1['tris']}")
            ok(v1["hashes"]["Gls"] == fin[0]["hashes"]["Gls"], "B5 the pass moved the glass sub-mesh")
            ok(v1["pngs"][f"{D.NAME}_BaseColor.png"] != fin[0]["pngs"][f"{D.NAME}_BaseColor.png"], "B5 the pass changed no pixel")
            ok(v1["pngs"][f"{D.NAME}_Emissive.png"] == fin[0]["pngs"][f"{D.NAME}_Emissive.png"], "B5 the pass changed the sign's Emissive")
            ok(min_alpha(os.path.join(v1["dir"], f"{D.NAME}_BaseColor.png")) >= 1.0, "B5 BaseColor alpha under 255")
            with open(os.path.join(v1["dir"], f"{D.NAME}.manifest.json"), "r", encoding="utf-8") as fh:
                man = json.load(fh)
            ok((man.get("texpass") or {}).get("version") == "v1" and man["args"]["texpass"] == "v1"
               and set(man["texpass"]["variants"]) == {"cedar", "corten", "roof"}, f"B5 manifest texpass block: {man.get('texpass')}")
            print(f"[bus_depot] B5 v1: {v1['tris']} hashes {v1['hashes']}")
            # -- B6: flag off again ------------------------------------------------------------------------
            again = finish(tmp, "fin2")
            for key in ("pngs", "hashes", "tris"):
                ok(again[key] == fin[0][key], f"B6 flag-off after a pass: {key} differ: {again[key]} vs {fin[0][key]}")
            print(f"[bus_depot] B6 flag-off after v1 -> {'SAME' if again['pngs'] == fin[0]['pngs'] else 'DIFFERENT'}")
        shipped = os.environ.get("BUS_DEPOT_SHIPPED_DIR")
        if shipped:
            r = run(["--out", os.path.join(tmp, "full"), "--no-validate"])
            mine = pngs(r["asset_dir"])
            theirs = pngs(shipped)
            ok(mine == theirs, f"shipped identity: this build {mine} vs {shipped} {theirs}")
            print(f"[bus_depot] shipped identity ({shipped}): {'SAME' if mine == theirs else 'DIFFERENT'}")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    if FAILS:
        print(f"FAIL test_bus_depot: {len(FAILS)} check(s)")
        for f in FAILS:
            print("   ", f)
        sys.exit(1)
    print(f"PASS test_bus_depot: {'quick (B1, B2, B7)' if QUICK else 'B1-B7'} ({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_bus_depot")
        sys.exit(1)
