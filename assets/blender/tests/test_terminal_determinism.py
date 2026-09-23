"""
Determinism gate for the terminal generator (review F1 / DECISIONS 107).

Builds each station twice in one Blender process (geometry + bevels + part tags + the
quadrant unwrap - everything up to the finish pass) and hashes vertex coordinates, face
vertex lists and the active UV layer. Two builds must hash identically, otherwise the
unique repack, the AO bake and the LOD decimation - all order-dependent - ship different
textures from the same source. Exit 1 on any difference.

    blender --background --factory-startup --python tests\\test_terminal_determinism.py
"""
import hashlib
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import lift_terminal  # noqa: E402


def build_hash(kind):
    lift_terminal.clear_scene()
    t = lift_terminal.build_terminal_pali(lift_terminal.TerminalParams(), kind, finish=True)
    me = t.data
    h = hashlib.sha1()
    for v in me.vertices:
        h.update(("%.5f %.5f %.5f;" % tuple(v.co)).encode())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    uv = me.uv_layers.active.data
    for lp in uv:
        h.update(("%.6f %.6f;" % tuple(lp.uv)).encode())
    return h.hexdigest()[:12], len(me.vertices), len(me.polygons)


ok = True
for kind in ("return", "drive"):
    a = build_hash(kind)
    b = build_hash(kind)
    same = a == b
    ok = ok and same
    print(f"[det] {kind}: run1 {a} run2 {b} -> {'SAME' if same else 'DIFFERENT'}")
if not ok:
    print("FAIL test_terminal_determinism: builds differ")
    sys.exit(1)
print("PASS test_terminal_determinism: both stations hash identically across two builds")
