"""
test_lodge_determinism - this test builds the L1 recipe twice in ONE Blender process. The two builds
must be identical (bldg-kit.md section 17). The order-dependent steps are the cull, the rooms, the
unique pack, the AO bake and the LOD collapse. p0a-integration O9 found a non-reproducible LOD chain
in lift_terminal. So the test also hashes the LOD meshes.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_lodge_determinism.py

Pass 1 runs --blockout twice. These items must match between the two runs:
- lodge.mesh_hash of the joined pre-bake mesh (vertex coords %.5f, face vertex lists, UVTile,
  material index, part, density_w)
- the hash of `_Win`
- the cull report
- the room cells

Pass 2 runs a reduced finish twice (--tex 1024 --ao-samples 8 --no-validate, LODs on, into two
scratch folders). These items must be identical:
- the five PNGs (incl. _Emissive), byte for byte
- the LOD0, LOD1, LOD2 and `_Win` mesh hashes

The test exits 1 on any difference.
"""

import hashlib
import os
import shutil
import sys
import tempfile
import time
import traceback

import bpy

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bldg_recipes as R   # noqa: E402
import lodge               # noqa: E402


def sha(path):
    with open(path, "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()[:12]


def run(argv):
    args = lodge.parse_args(argv)
    return lodge.run(R.get_recipe("L1"), args)


def main():
    t0 = time.perf_counter()
    tmp = tempfile.mkdtemp(prefix="lodge_det_")
    ok = True
    try:
        # -- pass 1: blockout x 2 ------------------------------------------------------
        res = []
        for i in range(2):
            r = run(["--lodge", "L1", "--blockout", "--out", os.path.join(tmp, f"blk{i}", "B")])
            res.append((r["hashes"], r["cells"], r["cull"]["faces_removed"], r["tris"][0]))
        same = res[0] == res[1]
        ok &= same
        print(f"[det] blockout: run1 {res[0][0]} {res[0][2]} faces culled, run2 {res[1][0]} "
              f"-> {'SAME' if same else 'DIFFERENT'}")
        # -- pass 2: reduced finish x 2 --------------------------------------------------
        fin = []
        for i in range(2):
            out = os.path.join(tmp, f"fin{i}", "B")
            r = run(["--lodge", "L1", "--tex", "1024", "--ao-samples", "8", "--no-validate",
                     "--out", out])
            d = r["asset_dir"]
            pngs = {f: sha(os.path.join(d, f)) for f in sorted(os.listdir(d)) if f.endswith(".png")}
            fin.append((pngs, r["hashes"], r["tris"]))
        for key, label in ((0, "PNGs"), (1, "mesh hashes"), (2, "tris")):
            same = fin[0][key] == fin[1][key]
            ok &= same
            print(f"[det] finish {label}: run1 {fin[0][key]} run2 {fin[1][key]} -> "
                  f"{'SAME' if same else 'DIFFERENT'}")
        assert len(fin[0][0]) == 5, f"expected 5 PNGs (4 maps + _Emissive), got {sorted(fin[0][0])}"
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    if not ok:
        print("FAIL test_lodge_determinism: builds differ")
        sys.exit(1)
    print(f"PASS test_lodge_determinism: L1 blockout and finish (PNGs, LOD0/1/2, _Win) identical "
          f"across two builds in one process ({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_lodge_determinism")
        sys.exit(1)
