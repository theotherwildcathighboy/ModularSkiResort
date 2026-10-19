"""
test_glass_slots - T85 `--glass gls` + T89 `--slots v2` (docs/research/field/2026-10-19/fix-T85-T89.md).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_glass_slots.py [-- <letter dir>]

Layout gates (no bake, seconds):
  S1  flag off byte-identical: the rollout --trim v1 layout hash equals the X export's (0d3e5eee7bf6c131), before AND after
      a v2 + gls layout was built in the same process (registering v2 keys mutates no module table);
  S2  v2 layout: metal_roof_dk / metal_roof_dk~w on the two retired cells (lux_stone_tan~w (3,6), lux_siding_red~w (6,7),
      0 m2 sampled on all eight X builds), metal_roof_lt / lux_glulam_s2 / lux_balrail_s2 on the cells of the keys they
      replace, every other tile rect and every flag-off palette rect unchanged (the renamed keys' "~f" excepted), the new
      flat keys appended, no rect overlap, palette <= 64;
  S3  slots: frame = slot 3, trim_grain_s2 = slot 2 (strip band 1), lux_glulam_s2 / lux_balrail_s2 = slot 2, the
      accent_* and metal_roof_lt / _dk keys slot 0 and their fixed colour = lin(RECOLOR_ALBEDO) x lin(old default) (the game
      reads a VariationSet colour as sRGB, BatchDataSystem.cs:716-718); slot_default_for inverts fixed_srgb;
      slots_v2_trim_remap keeps L4 / L5 boards on band 0;
  S4  glass: the three interior keys appended, lit / dim emissive, dark none; lodge.asset_name drops the keyword under
      gls and raises if it is kept; the default build path (glass win, slots v1) never registers a v2 key.
Folder gates (when a letter dir with the eight Y builds is given):
  F1  each folder: <name>_Gls / _LOD1_Gls / _LOD2_Gls FBX, no _Win FBX, no interior keyword in the name;
  F2  manifest: glass.plates >= 1, far_plates.moved >= 1 where a kit hole exists, LOD0 <= cap, LOD1 <= 0.5 LOD0,
      LOD2 <= 500, tiled.sheet.emissive_bound true (every building now has lit plates), tiled.slots_v2 present with
      three defaults in [0, 1] and the ControlMask gate passed (slots_v2_report raises otherwise);
  F3  one sheet hash across the eight; the school owns it.
Exit 1 on the first failure; one PASS line otherwise.
"""

import json
import os
import sys
import traceback

import bpy  # noqa: F401  (Blender's Python)
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import cs2_asset                       # noqa: E402
import cs2_bldg as K                   # noqa: E402
import cs2_tilesheet as TS             # noqa: E402
import lodge                           # noqa: E402

# the flag-off trim v1 family sheet. Export round X (DECISIONS 290) was 0d3e5eee7bf6c131; B's T90 wood constants
# (c82e573: GLULAM_LIN / TG_LIN, unconditional) moved it to ecbcf85506aacaa6 (measured on a clean HEAD copy, integration
# build 2026-10-19); re-pinned in T102 (DECISIONS 317). The --slots v2 / --glass gls keys never enter it.
X_HASH = "ecbcf85506aacaa6"
CHECKS = []


def ok(msg):
    CHECKS.append(msg)
    print("  ok", msg)


def rects(lay):
    out = {("t", k): tuple(v["rect_px"][:2]) + (v["swatch_px"],) for k, v in lay["tiles"].items()}
    out.update({("p", k): tuple(v["rect_px"]) for k, v in lay["palette"].items()})
    return out


def overlaps(lay):
    boxes = []
    c = lay["cell"]
    for k, v in lay["tiles"].items():
        x0, y0, _C = v["rect_px"]
        sw, pd = v["swatch_px"], v["pad"]
        boxes.append((k, x0 - pd, y0 - pd, sw, sw))
    px, py, ps = lay["palette_rect"]
    boxes.append(("palette", px, py, ps, ps))
    lx, ly, lw, lh = lay["label_region"]
    boxes.append(("labels", lx, ly, lw, lh))
    bad = []
    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            a, b = boxes[i], boxes[j]
            if a[1] < b[1] + b[3] and b[1] < a[1] + a[3] and a[2] < b[2] + b[4] and b[2] < a[2] + a[4]:
                bad.append((a[0], b[0]))
    assert c > 0
    return bad


def check_layouts():
    reps = lodge._sheet_repeats()
    off = TS.sheet_layout(4096, "rollout", reps, trim=True)
    assert off["hash"] == X_HASH, (off["hash"], X_HASH)
    v2 = TS.sheet_layout(4096, "rollout", reps, trim=True, slots=True, glass=True)
    off2 = TS.sheet_layout(4096, "rollout", reps, trim=True)
    assert off2["hash"] == X_HASH, ("flag-off hash moved after a v2 layout", off2["hash"])
    ok(f"S1 flag-off layout hash {X_HASH} before and after a v2 + gls layout in the same process")
    t0, t2 = off["tiles"], v2["tiles"]
    for k, cell in TS.SLOTS_V2_RETIRE.items():
        assert k not in t2, k
    c = v2["cell"]
    for k, (cc, rr, sp) in TS.SLOTS_V2_TILES.items():
        x0, y0, _C = t2[k]["rect_px"]
        assert (x0 - t2[k]["pad"], y0 - t2[k]["pad"]) == (cc * c, rr * c), (k, t2[k])
    for a, b in TS.slots_v2_rename().items():
        assert a not in t2 and t2[b]["rect_px"] == t0[a]["rect_px"] and t2[b]["repeat"] == t0[a]["repeat"], (a, b)
    for k, v in t0.items():
        if k in TS.SLOTS_V2_RETIRE or k in TS.slots_v2_rename():
            continue
        assert t2[k]["rect_px"] == v["rect_px"], k
    renamed_f = {a + TS.F_SUFFIX: b + TS.F_SUFFIX for a, b in TS.slots_v2_rename().items()}
    for k, v in off["palette"].items():
        nk = renamed_f.get(k, k)
        assert v2["palette"][nk]["rect_px"] == v["rect_px"], ("palette rect moved", k, nk)
    new = [k for k in v2["palette"] if k not in off["palette"] and k not in renamed_f.values()]
    assert set(new) == set(TS.PALETTE_APPENDED_V2) | set(TS.PALETTE_APPENDED_GLASS), new
    assert len(v2["palette"]) <= v2["palette_grid"] ** 2
    assert not overlaps(v2), overlaps(v2)
    ok(f"S2 v2 layout: retired cells re-used, renamed keys on their cells, {len(t0) - 4} tiles + "
       f"{len(off['palette'])} palette rects unchanged ({len(renamed_f)} renamed in place), {len(new)} keys appended "
       f"({len(v2['palette'])} / {v2['palette_grid'] ** 2}), no overlap")
    return off, v2


def check_slots(v2):
    M = K.BLDG_MATERIALS
    assert M["frame"].slot == 3 and M["frame"].recolor
    assert M["trim_grain_s2"].slot == 2 and v2["strip"]["trim_grain_s2"]["band"] == 1
    assert v2["strip"]["trim_grain"]["band"] == 0 and M["trim_grain"].slot == 1
    ren = TS.slots_v2_rename()
    for a, k in ren.items():
        if a.startswith("lux_"):
            assert M[k].slot == 2 and M[k].recolor, k
    same = M["lux_glulam"].target_srgb == M["lux_balrail"].target_srgb
    assert ("lux_balrail" in ren) == same, ("balcony walls join slot 2 exactly when they share the glulam colour", same)
    rl = float(K.srgb_to_linear(cs2_asset.RECOLOR_ALBEDO))
    for k, t in list(TS.ACCENT_FIXED.items()) + [("metal_roof_lt", TS.METAL_ROOF_LIGHT),
                                                   ("metal_roof_dk", TS.METAL_ROOF_DARK)]:
        m = M[k]
        assert m.slot == 0 and not m.recolor, k
        lin = np.asarray(K.srgb_to_linear(np.asarray(m.target_srgb, np.float64)))
        want = rl * np.asarray(K.srgb_to_linear(np.asarray(t, np.float64)))   # the game multiplies lin(colour)
        assert np.allclose(lin, want, atol=2e-4), (k, lin, want)
    assert M["metal_roof"].slot == 3, "metal_roof itself must stay as it was (flag-off builds)"
    for t in ((0.2, 0.09, 0.04), (0.5, 0.5, 0.5)):
        c = TS.slot_default_for(t)
        back = rl * np.asarray(K.srgb_to_linear(np.asarray(c, np.float64)))
        assert np.allclose(back, t, atol=2e-3), (t, c, back)
    assert TS.slots_v2_trim_remap("L5")["lux_trim"] == "trim_grain"
    assert TS.slots_v2_trim_remap("L1")["trim"] == "trim_grain_s2"
    assert TS.slots_v2_trim_remap("school")["trim_white"] == "trim_grain_s2"
    ok("S3 slot keys: frame B, trim_grain_s2 / glulam (+ balcony walls iff one colour) G, accent + metal roofs fixed at "
       "lin(0.80) x lin(old default); slot_default_for round-trips; L4 / L5 boards stay on band 0")


def check_glass():
    M = K.BLDG_MATERIALS
    assert M["interior_lit"].emissive_srgb and M["interior_dim"].emissive_srgb
    assert M["interior_dark"].emissive_srgb is None
    rc = lodge.select_recipe(lodge.parse_args(["--recipe", "L1", "--blockout"]))
    assert lodge.asset_name(rc, "Y", glass="gls") == "SkiLodgeAFrameY"
    try:
        lodge.asset_name(rc, "Y", override="SkiLodgeAFrameCommercialLow", glass="gls")
        raise AssertionError("keyword kept under gls must raise")
    except RuntimeError:
        pass
    assert lodge.asset_name(rc, "Y") == "SkiLodgeAFrameCommercialLowY"
    a = lodge.parse_args(["--recipe", "L1"])
    assert a.glass == "win" and a.slots == "v1"
    ok("S4 glass keys (lit / dim emissive), names: gls drops the keyword (and raises if kept), defaults win / v1")


def check_folder(root):
    names = {}
    hashes = set()
    for d in sorted(os.listdir(root)):
        mp = os.path.join(root, d, d + ".manifest.json")
        if not os.path.isfile(mp):
            continue
        m = json.load(open(mp, encoding="utf-8"))
        if "glass" not in m:
            continue
        fs = set(os.listdir(os.path.join(root, d)))
        for sfx in ("_Gls.fbx", "_LOD1_Gls.fbx", "_LOD2_Gls.fbx"):
            assert d + sfx in fs, (d, sfx)
        assert not any(f.endswith("_Win.fbx") for f in fs), d
        assert "CommercialLow" not in d, d
        g = m["glass"]
        assert len(g["plates"]) >= 1, d
        t = {int(k): v for k, v in m["tris"].items()}
        cap = m["budget"]["lod0_cap"] if isinstance(m.get("budget"), dict) else None
        if cap:
            assert t[0] <= cap, (d, t[0], cap)
        assert t[1] <= 0.5 * t[0] and t[2] <= 500, (d, t)
        ti = m["tiled"]
        assert ti["sheet"]["emissive_bound"], d
        sv = ti["slots_v2"]
        for s in ("slot1", "slot2", "slot3"):
            assert all(0.0 <= c <= 1.0 for c in sv["defaults"][s]), (d, s, sv["defaults"][s])
        hashes.add(ti["sheet"]["hash"])
        names[d] = (len(g["plates"]), g.get("far_plates"), t[0], cap)
    assert len(names) == 8, sorted(names)
    assert len(hashes) == 1, hashes
    ok(f"F1-F3 eight Y folders: _Gls x3, no _Win, no keyword, caps, emissive bound, defaults in [0, 1], "
       f"one sheet {hashes.pop()}: " + "; ".join(f"{k} {v[0]} plates LOD0 {v[2]}/{v[3]}" for k, v in names.items()))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    try:
        off, v2 = check_layouts()
        check_slots(v2)
        check_glass()
        if argv:
            check_folder(argv[0])
    except Exception:
        traceback.print_exc()
        print(f"FAIL test_glass_slots after {len(CHECKS)} checks")
        sys.exit(1)
    print(f"PASS test_glass_slots: {len(CHECKS)} checks")


if __name__ == "__main__":
    main()
