"""
test_roof_uvv - the roof kick's precondition (texture pass, cs2_tilesheet.TEXPASS_ROOF_KICK_MAX_DEG, test_texpass P13).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_roof_uvv.py -- --root <letter dir>

A roof-kick key's normal map leans toward +v. That leans a roof toward its ridge only where the face's UV v runs up the
fall line; where v ran down the slope the same map would lean the roof toward its eave. This test reads the exports
(the LOD0 FBX each <root>/<name>/<name>.fbx with its <name>.manifest.json; default root assets/import/buildings/W, the
pass's letter) and checks every face that draws a kick key's cell:

  R1 cell faces: a face draws the cell when its UV centroid (first UV layer, the family-sheet atlas) lies in the cell
     rect + pad of the sheet layout the manifest names (rect y from the bottom = UV v). Per asset, the count in the key's
     rect equals the manifest's tiled faces of the key and its layout aliases (lux_slate -> slate), and the same for the
     "~w" twin, within 1 face (a neighbor's face on the rect edge);
  R2 up axis: the area-weighted upward normal score over the cell faces is highest for +Y (the export bakes Y-up), so
     "up" below is +Y;
  R3 v up-slope: on every sloped top (n . up > 0.05 and a fall line) grad v . up-slope > 0.7 |grad v| (v runs toward
     the ridge); mutation: the same faces with v flipped must read "down" on every sloped top;
  R4 flat tops: a top with no fall line has nothing to lean toward, so the kick shades it as if tilted toward its v.
     Allowed only as named in FLAT_ALLOW_M2 (by recipe key); any other asset keeps 0.1 m2 or less;
  undersides (n . up < -0.05) and vertical faces (|n . up| <= 0.05) are reported (m2 and v direction), not gated: no
  ridge to lean toward; the kick turns a vertical slab edge's normal a little skyward.
At least one export must draw a kick key (else the root is wrong). Plain checks; no file is written.
"""
import json
import os
import sys
import traceback
from collections import defaultdict

import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import cs2_tilesheet as TS             # noqa: E402
import cs2_validate                    # noqa: E402

REPO = os.path.dirname(os.path.dirname(HERE))
# R4 allowance, m2 of flat top per recipe key (measured on the fam-v1 Z builds, 2026-11-20): the ski school's 17 exactly
# flat slate faces (45.0 m2). Its sloped roofs are 13-14 deg; the flat faces take the kick as a 10 deg tilt toward v.
FLAT_ALLOW_M2 = {"school": 45.0}
FLAT_TOL = 1.02                         # x the allowance (a rebuild may cut the faces a little differently)
FLAT_ZERO_M2 = 0.1
CHECKS = []


def ok(msg):
    CHECKS.append(msg)
    print("  ok", msg)


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    root = os.path.join(REPO, "assets", "import", "buildings", "W")
    if "--root" in argv:
        root = argv[argv.index("--root") + 1]
    return os.path.abspath(root)


def cell_rects(lay, key):
    """{cell: (u0, u1, v0, v1)} for the key and its "~w" twin (rect + pad, y from the bottom = UV v)."""
    A = lay["A"]
    out = {}
    for k in (key, key + "~w"):
        t = lay["tiles"].get(k)
        if t is None:
            continue
        (x0, y0, C), p = t["rect_px"], t.get("pad", 0)
        out[k] = ((x0 - p) / A, (x0 + C + p) / A, (y0 - p) / A, (y0 + C + p) / A)
    return out


def drawers(lay, key):
    """The manifest keys that draw the key's cell: the key and every layout alias of it."""
    return [key] + sorted(a for a, b in (lay.get("alias") or {}).items() if b == key)


def faces_in(path, rects):
    """Import the FBX into an empty scene; per cell: [(area m2, n, grad v)] for every face whose UV centroid is in it."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    s2 = float(cs2_validate.RAW_UNITS_PER_M) ** 2          # the import keeps the file's raw centimeters
    out = {c: [] for c in rects}
    for ob in bpy.context.scene.objects:
        if ob.type != "MESH" or not ob.data.uv_layers:
            continue
        me, mw = ob.data, ob.matrix_world
        uv = me.uv_layers[0].data
        co = [mw @ v.co for v in me.vertices]
        for poly in me.polygons:
            li = list(poly.loop_indices)
            cu = sum(uv[i].uv.x for i in li) / len(li)
            cv = sum(uv[i].uv.y for i in li) / len(li)
            cell = next((c for c, (u0, u1, v0, v1) in rects.items() if u0 <= cu <= u1 and v0 <= cv <= v1), None)
            if cell is None:
                continue
            pts = [co[me.loops[i].vertex_index] for i in li]
            vs = [uv[i].uv.y for i in li]
            area, nrm, g = 0.0, np.zeros(3), np.zeros(3)
            for j in range(1, len(pts) - 1):
                e1 = np.array(pts[j] - pts[0])
                e2 = np.array(pts[j + 1] - pts[0])
                cr = np.cross(e1, e2)
                a = float(np.linalg.norm(cr)) / 2
                g11, g12, g22 = e1 @ e1, e1 @ e2, e2 @ e2
                det = g11 * g22 - g12 * g12
                if a < 1e-9 or abs(det) < 1e-18:
                    continue
                d1, d2 = vs[j] - vs[0], vs[j + 1] - vs[0]
                g += (e1 * ((g22 * d1 - g12 * d2) / det) + e2 * ((g11 * d2 - g12 * d1) / det)) * a
                nrm += cr
                area += a
            if area > 0:
                out[cell].append((area / s2, nrm / np.linalg.norm(nrm), g / area))
    return out


def classify(fl):
    """{(position, v direction): m2} and the worst sloped-top cos. Up = +Y (R2)."""
    up = np.array((0.0, 1.0, 0.0))
    cls = defaultdict(float)
    worst = 1.0
    for a, n, g in fl:
        nu = float(n @ up)
        pos = "top" if nu > 0.05 else ("underside" if nu < -0.05 else "vertical")
        u = up - nu * n
        if np.linalg.norm(u) < 0.05:
            cls[(pos, "flat")] += a
            continue
        gl = float(np.linalg.norm(g))
        cos = float(g @ u) / (gl * float(np.linalg.norm(u))) if gl > 1e-12 else 0.0
        c = "up" if cos > 0.7 else ("down" if cos < -0.7 else "sideways")
        cls[(pos, c)] += a
        if pos == "top":
            worst = min(worst, cos)
    return cls, worst


def check_asset(d, name, key, lay, report):
    m = json.load(open(os.path.join(d, f"{name}.manifest.json"), encoding="utf-8"))
    ppm = (m.get("tiled") or {}).get("ppm_by_material") or {}
    keys = drawers(lay, key)
    want = {key: sum(int((ppm.get(k) or {}).get("faces", 0)) for k in keys),
            key + "~w": sum(int((ppm.get(k + "~w") or {}).get("faces", 0)) for k in keys)}
    if not any(want.values()):
        report.append(f"{name}: draws no {key} cell")
        return None
    rects = cell_rects(lay, key)
    got = faces_in(os.path.join(d, f"{name}.fbx"), rects)
    for c, n in want.items():                                                  # R1
        assert abs(len(got.get(c, [])) - n) <= 1, f"R1 {name}: {len(got.get(c, []))} faces in the {c} rect, manifest {n}"
    fl = [f for c in got for f in got[c]]
    axes = {"+X": (1, 0, 0), "-X": (-1, 0, 0), "+Y": (0, 1, 0), "-Y": (0, -1, 0), "+Z": (0, 0, 1), "-Z": (0, 0, -1)}
    score = {k: sum(a * max(float(n @ np.array(v)), 0.0) for a, n, _g in fl) for k, v in axes.items()}
    assert max(score, key=score.get) == "+Y", f"R2 {name}: up axis {max(score, key=score.get)} ({score})"   # R2
    cls, worst = classify(fl)
    bad = sum(a for (p, c), a in cls.items() if p == "top" and c in ("down", "sideways"))
    assert bad == 0.0, f"R3 {name}: {bad:.1f} m2 of {key} roof top with v not up-slope (worst cos {worst:+.3f})"   # R3
    # mutation: the same faces with v flipped (a V-flipped island) must fail R3 on every sloped top
    flip, _w = classify([(a, n, -g) for a, n, g in fl])
    flipped = sum(a for (p, c), a in flip.items() if p == "top" and c == "down")
    up_m2 = cls.get(("top", "up"), 0.0)
    assert up_m2 > 0.0 and abs(flipped - up_m2) < 1e-6, f"R3 mutation {name}: v flipped reads {flipped:.1f} m2 down"
    flat = cls.get(("top", "flat"), 0.0)
    rk = m.get("recipe")
    allow = FLAT_ALLOW_M2.get(rk)
    if allow is None:
        assert flat <= FLAT_ZERO_M2, f"R4 {name} ({rk}): {flat:.1f} m2 of flat {key} top, no allowance"
    else:
        assert flat <= allow * FLAT_TOL, f"R4 {name} ({rk}): {flat:.1f} m2 of flat {key} top > allowance {allow}"
    report.append(f"{name} ({rk}): " + ", ".join(f"{p} {c} {a:.1f}" for (p, c), a in sorted(cls.items()))
                  + f" m2; worst top cos {worst:+.3f}")
    return name, sum(a for (p, c), a in cls.items() if p == "top" and c == "up"), flat


def main():
    root = args()
    kick = getattr(TS, "TEXPASS_ROOF_KICK_MAX_DEG", None)
    assert kick, "cs2_tilesheet has no roof-kick key: nothing to check"
    assert os.path.isdir(root), f"no export folder {root} (pass --root <letter dir>)"
    report, checked = [], []
    for name in sorted(os.listdir(root)):
        d = os.path.join(root, name)
        mp = os.path.join(d, f"{name}.manifest.json")
        if not (os.path.isfile(mp) and os.path.isfile(os.path.join(d, f"{name}.fbx"))):
            continue
        sheet = ((json.load(open(mp, encoding="utf-8")).get("tiled") or {}).get("sheet") or {})
        if not sheet:
            report.append(f"{name}: not a tiled build")
            continue
        lp = os.path.join(os.path.normpath(os.path.join(d, sheet["workspace"])) + ".aux", f"{sheet['name']}.layout.json")
        lay = json.load(open(lp, encoding="utf-8"))
        for key in sorted(kick):
            r = check_asset(d, name, key, lay, report)
            if r:
                checked.append((key,) + r)
    for line in report:
        print("   ", line)
    assert checked, f"no export under {root} draws a roof-kick key ({sorted(kick)})"
    ok(f"R1-R4 roof kick {sorted(kick)} under {root}: " + "; ".join(
        f"{n} {k} {top:.1f} m2 of sloped top v up-slope" + (f", flat {flat:.1f} m2 (allowed)" if flat > FLAT_ZERO_M2 else "")
        for k, n, top, flat in checked))
    print(f"PASS test_roof_uvv: {len(CHECKS)} checks ({len(checked)} assets)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
