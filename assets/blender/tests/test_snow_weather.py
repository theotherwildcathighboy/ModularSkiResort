"""
test_snow_weather.py - the REDO gates of snow_weather (wiki style, 2026-10-17).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_snow_weather.py

1. overlay_gate RAISES OverlayScaleError for a feature under 5 cm and for a soft edge under 5 cm
   (the rejected rev 4 pass painted 1-6 cm features); a 5 cm feature at 100 px/m (5 texels) gets weight
   0 and the overlay is SKIPPED (None), at 250 px/m (12.5 texels) weight 1; the 8-texel midpoint
   gives 0.5.
2. face_rotations: a quad whose unique UVs are its atlas UVs turned 90 deg (and scaled) yields a
   90 deg rotation; identical layers yield the identity (the N0 re-orientation the REDO packer needs).
Prints one PASS line and exits 0; any failure prints FAIL and exits 1.
"""

import math
import os
import sys
import traceback

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import snow_weather as SW   # noqa: E402

CHECKS = 0


def check(cond, msg):
    global CHECKS
    CHECKS += 1
    if not cond:
        raise AssertionError(msg)


class FakeFields:
    def __init__(self, ppm):
        self.ppm = np.asarray(ppm, np.float64)
        self.w = np.ones_like(self.ppm)


def run():
    # -- 1. overlay gate -------------------------------------------------------------------
    F = FakeFields(np.full(100, 100.0))
    sel = np.ones(100, bool)
    for feat, soft in ((0.03, 0.05), (0.05, 0.03), (0.012, 0.012)):
        try:
            SW.overlay_gate(F, sel, "t", feat, soft)
            check(False, f"feature {feat} / soft {soft} did not raise")
        except SW.OverlayScaleError:
            pass
    check(SW.overlay_gate(F, sel, "t", 0.05, 0.05) is None, "5 cm at 100 px/m (5 texels) was not skipped")
    g = SW.overlay_gate(FakeFields(np.full(100, 250.0)), sel, "t", 0.05, 0.05)
    check(g is not None and np.allclose(g, 1.0), "5 cm at 250 px/m (12.5 texels) is not full weight")
    g = SW.overlay_gate(FakeFields(np.full(100, 160.0)), sel, "t", 0.05, 0.05)
    check(g is not None and abs(float(g[0]) - 0.5) < 1e-6, "8 texels is not the 0.5 midpoint")
    mixed = FakeFields(np.r_[np.full(80, 300.0), np.full(20, 100.0)])
    g = SW.overlay_gate(mixed, sel, "t", 0.05, 0.05)
    check(g is not None and np.all(g[80:] == 0.0) and np.all(g[:80] == 1.0),
          "texels under 6 texels must carry weight 0")
    print("[test] overlay gate: raises < 5 cm, skips < 8 texels, 0 under 6 texels")

    # -- 2. face rotations ---------------------------------------------------------------------
    bpy.ops.wm.read_factory_settings(use_empty=True)
    me = bpy.data.meshes.new("q")
    me.from_pydata([(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)], [], [(0, 1, 2, 3)])
    a = me.uv_layers.new(name="UVAtlas")
    u = me.uv_layers.new(name="UVUnique")
    base = [(0.1, 0.1), (0.3, 0.1), (0.3, 0.3), (0.1, 0.3)]
    for i, l in enumerate(a.data):
        l.uv = base[i]
    for i, l in enumerate(u.data):
        x, y = base[i]
        l.uv = (0.5 - 2.0 * (y - 0.1), 0.2 + 2.0 * (x - 0.1))      # +90 deg, scale 2
    R = SW.face_rotations(me)[0]
    check(np.allclose(R, [[0.0, -1.0], [1.0, 0.0]], atol=1e-6), f"90 deg rotation expected, got {R}")
    for i, l in enumerate(u.data):
        l.uv = base[i]
    R = SW.face_rotations(me)[0]
    check(np.allclose(R, np.eye(2), atol=1e-9), f"identity expected, got {R}")
    print("[test] face_rotations: 90 deg and identity")


if __name__ == "__main__":
    try:
        run()
    except Exception:
        traceback.print_exc()
        print("FAIL: test_snow_weather")
        sys.exit(1)
    print(f"PASS: test_snow_weather ({CHECKS} checks)")
    sys.exit(0)
