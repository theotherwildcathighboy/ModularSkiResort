"""
Determinism gate for the terminal generator (review F1 / DECISIONS 107, generation M, 2026-10-11).

The test builds each station twice in one Blender process. It hashes vertex coordinates, face vertex
lists and the UV layers. Two builds must hash identically. Otherwise the unique repack, the AO bake
and the LOD decimation (all order-dependent) ship different textures from the same source. The test
exits 1 on any difference.

- M (the shipping generation, design/fg-stations-m.md M16): build_station_m + assemble_m (bevels
  on). It covers geometry, the part table, the pack seed (UVSeed) and the tiling layer (UVTile).
- M --no-fine (fine-detail step 3c off, FINE_DETAIL_M False): the same. M also hashes the row block
  (kit rows).
- L (kept reproducible behind --gen L): build_terminal_pali(finish=True). It covers geometry + the
  quadrant UVs, as before.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_terminal_determinism.py
"""
import hashlib
import json
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_asset  # noqa: E402
import lift_terminal  # noqa: E402


def mesh_hash(me, layers):
    h = hashlib.sha1()
    for v in me.vertices:
        h.update(("%.5f %.5f %.5f;" % tuple(v.co)).encode())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    for name in layers:
        for lp in me.uv_layers[name].data:
            h.update(("%.6f %.6f;" % tuple(lp.uv)).encode())
    h.update(me.get(cs2_asset.PART_TABLE_PROP, "").encode())
    return h.hexdigest()[:12], len(me.vertices), len(me.polygons)


def build_hash_l(kind):
    lift_terminal.clear_scene()
    t = lift_terminal.build_terminal_pali(lift_terminal.TerminalParams(), kind, finish=True)
    return mesh_hash(t.data, [t.data.uv_layers.active.name])


def build_hash_m(kind):
    lift_terminal.clear_scene()
    parts = lift_terminal.build_station_m(kind)
    t, _table, _sources = lift_terminal.assemble_m(parts, True, kind)
    # Fine-detail step 3c adds the row block: vanilla rows plus the dressing kit's "mod" rows, in
    # M's own frame.
    rows = hashlib.sha1(json.dumps(lift_terminal.subobject_rows_m(kind), sort_keys=True).encode()).hexdigest()[:12]
    return mesh_hash(t.data, sorted(l.name for l in t.data.uv_layers)) + (rows,)


def build_hash_m_nofine(kind):
    """fine-detail step 3c: the --no-fine (approved) build is deterministic too."""
    saved = lift_terminal.FINE_DETAIL_M
    lift_terminal.FINE_DETAIL_M = False
    try:
        return build_hash_m(kind)
    finally:
        lift_terminal.FINE_DETAIL_M = saved


ok = True
for gen, fn in (("M", build_hash_m), ("M --no-fine", build_hash_m_nofine), ("L", build_hash_l)):
    for kind in ("return", "drive"):
        a = fn(kind)
        b = fn(kind)
        same = a == b
        ok = ok and same
        print(f"[det] {gen} {kind}: run1 {a} run2 {b} -> {'SAME' if same else 'DIFFERENT'}")
if not ok:
    print("FAIL test_terminal_determinism: builds differ")
    sys.exit(1)
print("PASS test_terminal_determinism: M (fine-detail on and --no-fine) and L stations hash identically across two "
      "builds (geometry, part table, UVSeed + UVTile and the row block for M)")
