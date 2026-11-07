"""
test_trim_strip - T70 `--trim v1` (docs/research/art/trim-swatch-plan-2026-10-18.md section 5, gates
G1-G9).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_trim_strip.py

The test builds a scene by hand (the lodge.py prep: bevel, density, tags, UVTile projection, retile,
join + weld). The scene holds a gable_roof, a hip_roof (four fascia boxes in ONE part, mitered ridge
beams), a deck with its four-box deckfascia_, a box ridge_ on galv, a trim_white and a lux_trim
fascia, a 44 m trim fascia (L1's stress case) and a timber_lap wall beside them. The test runs the
scene through cs2_tilesheet.tile_mesh with the weathering rules on. Asserts:
- G1: Every face of a rake_ / fascia_ / deckfascia_ part is on a strip key after trim_remap (trim /
  lux_trim -> trim_grain, trim_white -> trim_grain_w) and is strip-mapped. Every ridge_ face is
  strip-mapped on galv. No strip face is weathered.
- G2: Grain runs along the member. Every long feature edge (>= 1 m, between faces of different
  planes) of a strip part maps to sheet x (|dy| <= 0.5 px + 0.5 % |dx|).
- G3: check_uv_cells == {}. Every strip loop is inside the usable rows of its band (guards excluded)
  or inside the galv content.
- G4: Across px/m is within 10 % of the band (400) / galv (120). Along px/m is >= 8. The 44 m fascia
  is at 480 / 44. A 74 m member is cut at the 60 m world lines (only it pays tris). A 0.6 m wide
  board raises. strip_seg_m 4 cuts every member over 1.2 m (tris up, along 120 px/m).
- G5: The ControlMask of the strip cell (the sheet writer's own _strip_maps) at face centroids has
  these values: trim_grain R = 1, trim_grain_w R = 0, galv slot 0 / metal 1, strip metal 0.
- G6: Layout. v1 has trim_grain at (5, 7), no lux_paver~w, and no rect overlap (tiles, palette,
  label region). Every other tile and every palette rect equals the off layout. The off layout
  equals the layout of the pre-T70 module (git 53defed) byte for byte (hash included), before and
  after the test builds a v1 layout in the same process.
- G7: Tri parity. The LOD0 tris of the v1 scene == the LOD0 tris of the flag-off scene (flat keys,
  flat-drawn ridge).
- G8: Flag off is byte-identical. The off scene through the current tile_mesh (default kwargs)
  hashes identical to the pre-T70 tile_mesh (53defed). It carries no cs2ts_strip. lodge.trim_cfg is
  None by default. A default lodge.py run never calls trim_remap / register_trim_materials
  (monkeypatched to raise). --trim v1 without a tiled build raises.
- G9: Determinism. Two v1 scenes hash identical.

The test exits 1 on the first failure. Otherwise it prints one PASS line.
"""

import hashlib
import importlib.util
import math
import os
import sys
import tempfile
import traceback

import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import cs2_asset                       # noqa: E402
import cs2_bake                        # noqa: E402
import cs2_bldg as K                   # noqa: E402
import cs2_tilesheet as TS             # noqa: E402
import cs2_uv                          # noqa: E402
import lodge                           # noqa: E402

PRE_T70 = "53defed"                    # The last commit before T70. It is the flag-off reference implementation.
PRE_T70_SHA1 = "8dd04295205481d3197e913a0b351044969d2e58"    # sha1 of its cs2_tilesheet.py (tests/data fixture)
CHECKS = []


def ok(msg):
    CHECKS.append(msg)
    print("  ok", msg)


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)


def pre_t70_module():
    """cs2_tilesheet as of PRE_T70, imported under another name (tests/data fixture). The history was
    rewritten 2026-10-22, so `git show PRE_T70` no longer resolves. The fixture is byte-identical to
    that commit's file."""
    with open(os.path.join(HERE, "tests", "data", "cs2_tilesheet_pre_t70.py"), "rb") as fh:
        src = fh.read()
    assert hashlib.sha1(src).hexdigest() == PRE_T70_SHA1, "tests/data/cs2_tilesheet_pre_t70.py changed"
    d = tempfile.mkdtemp(prefix="t70_ref_")
    p = os.path.join(d, "cs2_tilesheet_pre_t70.py")
    with open(p, "wb") as fh:
        fh.write(src)
    spec = importlib.util.spec_from_file_location("cs2_tilesheet_pre_t70", p)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def scene(trim, extra=()):
    """Returns (obj, uvtile, keys, moved) of the hand-built scene after the lodge.py prep."""
    clear()
    TS.ensure_materials()
    spec = K.RoofSpec("gable", 0.0, 8.0, 0.0, 12.0, 3.0, 35.0, axis="Y")
    parts = K.gable_roof("g", spec)
    parts += K.hip_roof("h", K.RoofSpec("hip", 14.0, 22.0, 0.0, 6.0, 3.0, 30.0, axis="X"))
    parts += K.deck("d", 0.0, 6.0, -7.0, -3.0, 1.2)
    parts += K.box_part("ridge_x", (30.0, 0.0, 4.0), (30.45, 9.0, 4.14), "galv")
    parts += K.box_part("fascia_w", (30.0, 12.0, 3.0), (30.12, 20.0, 3.42), "trim_white")
    parts += K.box_part("fascia_l", (34.0, 12.0, 3.0), (34.12, 20.0, 3.42), "lux_trim")
    parts += K.box_part("fascia_long", (40.0, 0.0, 3.0), (40.12, 44.0, 3.42), "trim")
    parts += K.box_part("wall_a", (0.0, -0.3, 0.0), (8.0, 0.0, 3.0), "timber_lap")
    for nm, lo, hi, key in extra:
        parts += K.box_part(nm, lo, hi, key)
    moved = {}
    if trim:
        TS.register_trim_materials()
        parts, moved = TS.trim_remap(parts)
    keys = sorted({k for _o, k in parts})
    K.bevel_parts(parts)
    cs2_uv.set_density(parts, K.part_weights(parts))
    table = cs2_asset.tag_parts(parts)
    K.strip_uv_layers(parts)
    proj = K.projection_specs(keys)
    cs2_bake.material_sources(proj, parts)
    K.retile(parts, proj)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _m in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=lodge.WELD_M)
    bpy.ops.object.mode_set(mode="OBJECT")
    cs2_asset.store_part_table(obj, table)
    uvt = cs2_uv.loop_uvs(obj.data.uv_layers[cs2_bake.TILE_LAYER])
    return obj, uvt, keys, moved


def run_tile(mod, obj, uvt, keys, trim, seg=None):
    lay = mod.sheet_layout(4096, "rollout", lodge._sheet_repeats(), **({"trim": True} if trim else {}))
    lay["label_rect"] = list(lay["label_region"])
    kw = dict(weather=mod.weather_level(0), flat_prefixes=mod.FLAT_DETAIL_PREFIXES)
    if trim:
        kw.update(flat_prefixes=mod.flat_detail_prefixes(True), strip_prefixes=mod.STRIP_PREFIXES_OWN, strip_seg_m=seg)
    rep = mod.tile_mesh(obj, uvt, keys, lay, **kw)
    return lay, rep


def face_data(obj, keys):
    me = obj.data
    n = len(me.polygons)
    mi = np.zeros(n, np.int32); me.attributes["cs2ts_mat"].data.foreach_get("value", mi)
    wf = np.zeros(n, np.int32); me.attributes["cs2ts_w"].data.foreach_get("value", wf)
    sf = np.zeros(n, np.int32)
    if me.attributes.get("cs2ts_strip") is not None:
        me.attributes["cs2ts_strip"].data.foreach_get("value", sf)
    pa = np.zeros(n, np.int32); me.attributes["part"].data.foreach_get("value", pa)
    table = __import__("json").loads(me[cs2_asset.PART_TABLE_PROP])
    names = [table[p - 1] if 1 <= p <= len(table) else "" for p in pa]
    return mi, wf, sf, names


# ---------------------------------------------------------------------------------------------------------

def check_layout(ref):
    off_ref = ref.sheet_layout(4096, "rollout", lodge._sheet_repeats())
    off1 = TS.sheet_layout(4096, "rollout", lodge._sheet_repeats())
    v1 = TS.sheet_layout(4096, "rollout", lodge._sheet_repeats(), trim=True)
    off2 = TS.sheet_layout(4096, "rollout", lodge._sheet_repeats())
    j = lambda d: __import__("json").dumps(d, sort_keys=True, default=str)
    assert j(off1) == j(off_ref) and j(off2) == j(off_ref), "off layout differs from the pre-T70 module's"
    assert "strip" not in off1 and STRIP_FREE(off1)
    assert TS.STRIP_CELL not in off1["tiles"] and "lux_paver~w" in off1["tiles"]
    ok(f"G6 off layout == pre-T70 ({PRE_T70}) byte for byte, hash {off1['hash']} (also after a v1 layout)")
    assert "lux_paver~w" not in v1["tiles"], "lux_paver~w still on the v1 sheet"
    cell = v1["cell"]
    x0, y0, C = v1["tiles"][TS.STRIP_CELL]["rect_px"]
    assert (x0 // cell, y0 // cell) == (5, 7) and C == 480, (x0, y0, C)
    for k, t in off1["tiles"].items():
        if k == "lux_paver~w":
            continue
        assert v1["tiles"][k] == t, f"tile {k} moved"
    assert v1["palette"] == off1["palette"], "palette moved"
    assert v1["hash"] != off1["hash"]
    rects = [(k, t["rect_px"][0] - t["pad"], t["rect_px"][1] - t["pad"], t["swatch_px"], t["swatch_px"])
             for k, t in v1["tiles"].items()]
    px, py, ps = v1["palette_rect"]
    rects.append(("palette", px, py, ps, ps))
    LX, LY, LW, LH = v1["label_region"]
    rects.append(("labels", LX, LY, LW, LH))
    for i in range(len(rects)):
        for jx in range(i + 1, len(rects)):
            a, b = rects[i], rects[jx]
            ov = min(a[1] + a[3], b[1] + b[3]) > max(a[1], b[1]) and min(a[2] + a[4], b[2] + b[4]) > max(a[2], b[2])
            assert not ov, f"rects overlap: {a[0]} / {b[0]}"
    st = v1["strip"]
    assert st["trim_grain"]["band"] == 0 and st["trim_grain_w"]["band"] == 1
    assert st["trim_grain"]["guard_px"] == 16 and abs(st["trim_grain"]["ppm"] - 400.0) < 1e-9
    ok("G6 v1 layout: trim_grain at (5,7) 480 px, lux_paver~w retired, 65 rects disjoint, every other tile and "
       "every palette cell unchanged, two bands (guard 16 px, 400 px/m)")
    return v1


def STRIP_FREE(lay):
    return not any(k in lay["tiles"] for k in TS.STRIP_KEYS)


def check_flag_off(ref):
    obj, uvt, keys, _ = scene(False)
    run_tile(ref, obj, uvt, keys, False)
    h_ref = lodge.mesh_hash(obj)
    attrs_ref = sorted(a.name for a in obj.data.attributes if not a.name.startswith("."))
    obj, uvt, keys, _ = scene(False)
    lay, rep = run_tile(TS, obj, uvt, keys, False)
    h_now = lodge.mesh_hash(obj)
    attrs_now = sorted(a.name for a in obj.data.attributes if not a.name.startswith("."))
    assert h_now == h_ref, f"flag-off tile_mesh differs from {PRE_T70}: {h_now} != {h_ref}"
    assert attrs_now == attrs_ref and "cs2ts_strip" not in attrs_now, (attrs_now, attrs_ref)
    assert "strip" not in rep
    ok(f"G8a flag-off tile_mesh (default kwargs) == pre-T70 {PRE_T70}: mesh hash {h_now}, attributes {attrs_now}")
    return rep["tris_after"]


def check_scene(tris_off):
    obj, uvt, keys, moved = scene(True)
    assert moved == {"fascia_gA": ["trim", "trim_grain"], "fascia_gB": ["trim", "trim_grain"],
                     "fascia_h": ["trim", "trim_grain"], "fascia_l": ["lux_trim", "trim_grain"],
                     "fascia_long": ["trim", "trim_grain"], "fascia_w": ["trim_white", "trim_grain_w"],
                     "deckfascia_d": ["trim", "trim_grain"],
                     "rake_gA0": ["trim", "trim_grain"], "rake_gA1": ["trim", "trim_grain"],
                     "rake_gB0": ["trim", "trim_grain"], "rake_gB1": ["trim", "trim_grain"]}, moved
    lay, rep = run_tile(TS, obj, uvt, keys, True)
    A = lay["A"]
    mi, wf, sf, names = face_data(obj, keys)
    me = obj.data
    # G1
    for i, nm in enumerate(names):
        if nm.startswith(TS.TRIM_V1_PREFIXES):
            assert keys[mi[i]] in TS.STRIP_KEYS and sf[i] == 1, (nm, keys[mi[i]], sf[i])
        if nm.startswith("ridge_"):
            assert keys[mi[i]] == "galv" and sf[i] == 1, (nm, keys[mi[i]])
        if sf[i]:
            assert wf[i] == 0, f"strip face weathered: {nm}"
    assert not any(sf[i] for i, nm in enumerate(names) if not nm.startswith(TS.TRIM_V1_PREFIXES + ("ridge_",)))
    ok(f"G1 {int(sf.sum())} strip faces: every rake_/fascia_/deckfascia_ face on trim_grain(_w), every ridge_ face "
       f"strip-mapped on galv, none weathered; {rep['strip']['patches']} patches in {rep['strip']['parts']} parts")
    # G7
    assert rep["tris_after"] == tris_off, f"tri parity: v1 {rep['tris_after']} != off {tris_off}"
    ok(f"G7 LOD0 tris v1 {rep['tris_after']} == flag-off {tris_off}")
    # G3
    bad = TS.check_uv_cells(obj, lay, keys)
    assert bad == {}, bad
    uv = cs2_uv.loop_uvs(me.uv_layers[cs2_uv.UNIQUE_LAYER]) * A
    for p in me.polygons:
        if not sf[p.index]:
            continue
        k = keys[mi[p.index]]
        x0, Cw, v_lo, v_hi, _ppm, _b = TS.strip_region(lay, k)
        q = uv[p.loop_start:p.loop_start + p.loop_total]
        assert (q[:, 0] >= x0 - 0.01).all() and (q[:, 0] <= x0 + Cw + 0.01).all(), (names[p.index], q)
        assert (q[:, 1] >= v_lo - 0.01).all() and (q[:, 1] <= v_hi + 0.01).all(), (names[p.index], q, v_lo, v_hi)
    ok("G3 check_uv_cells == {}; every strip loop inside its band's usable rows (guards free) / the galv content")
    # G2: long feature edges of strip parts lie along sheet x
    co = np.array([v.co[:] for v in me.vertices])
    edges = {}
    nrm = np.array([p.normal[:] for p in me.polygons])
    for p in me.polygons:
        ls = list(range(p.loop_start, p.loop_start + p.loop_total))
        for a, b in zip(ls, ls[1:] + ls[:1]):
            va, vb = me.loops[a].vertex_index, me.loops[b].vertex_index
            edges.setdefault(tuple(sorted((va, vb))), []).append((p.index, {va: uv[a], vb: uv[b]}))
    n_long = 0
    for (va, vb), own in edges.items():
        if not all(sf[f] for f, _ in own) or np.linalg.norm(co[va] - co[vb]) < 1.0:
            continue
        if len(own) == 2 and float(nrm[own[0][0]] @ nrm[own[1][0]]) > 0.999:
            continue                                       # a triangulation diagonal inside one board face
        for f, d in own:
            dx, dy = d[vb] - d[va]
            assert abs(dy) <= 0.5 + 0.005 * abs(dx), f"{names[f]}: long edge maps to ({dx:.1f}, {dy:.1f}) px"
        n_long += 1
    assert n_long > 40, n_long
    ok(f"G2 {n_long} long feature edges of strip parts all map to sheet x (grain along the member)")
    # G4
    ss = TS.strip_stats(obj, A, keys)
    for k, tgt in (("trim_grain", 400.0), ("trim_grain_w", 400.0), ("galv", 120.0)):
        assert abs(ss[k]["across_p50"] - tgt) <= 0.1 * tgt, (k, ss[k])
        assert ss[k]["along_p10"] >= 8.0, (k, ss[k])
    sel = [p.index for p in me.polygons if names[p.index] == "fascia_long" and sf[p.index]]
    long_rows = []
    for f in sel:
        p = me.polygons[f]
        ls = list(range(p.loop_start, p.loop_start + p.loop_total))
        vs = [me.loops[i].vertex_index for i in ls]
        L3 = [np.linalg.norm(co[vs[i]] - co[vs[(i + 1) % 3]]) for i in range(3)]
        Lu = [abs(uv[ls[i]][0] - uv[ls[(i + 1) % 3]][0]) for i in range(3)]
        j = int(np.argmax(L3))
        if L3[j] > 40:
            long_rows.append(Lu[j] / L3[j])
    assert long_rows and all(abs(r - 480.0 / 44.0) < 0.3 for r in long_rows), long_rows[:4]
    ok(f"G4 across p50 trim_grain {ss['trim_grain']['across_p50']} / trim_grain_w {ss['trim_grain_w']['across_p50']} "
       f"/ galv {ss['galv']['across_p50']} px/m (400 / 400 / 120 +-10 %); along p10 >= 8; the 44 m fascia at "
       f"{np.mean(long_rows):.1f} px/m along (480 / 44)")
    # G5
    sheet_C = 480
    _b, _n, mt, _sm, ctl, _info = TS._strip_maps(sheet_C)
    xs, ys, _C = lay["tiles"][TS.STRIP_CELL]["rect_px"]
    n_s = 0
    for p in me.polygons:
        if not sf[p.index]:
            continue
        k = keys[mi[p.index]]
        q = uv[p.loop_start:p.loop_start + p.loop_total].mean(0)
        if k in TS.STRIP_KEYS:
            r, c = int(q[1] - ys), int(q[0] - xs)
            want = 1.0 if TS.STRIP_KEYS[k] == 0 else 0.0
            assert ctl[r, c, 0] == want and mt[r, c] == 0.0, (names[p.index], k, r, c, ctl[r, c, 0])
            n_s += 1
    gm = K.BLDG_MATERIALS["galv"]
    assert gm.slot == 0 and TS.metal_of("galv") == 1.0
    ok(f"G5 ControlMask R at {n_s} strip-face centroids: trim_grain 1, trim_grain_w 0; metal 0; galv slot 0 metal 1")
    return lay


def check_long_and_wide(tris_v1):
    obj, uvt, keys, _ = scene(False, extra=(("fascia_huge", (60.0, 0.0, 3.0), (60.12, 74.0, 3.42), "trim"),))
    _lay, rep0 = run_tile(TS, obj, uvt, keys, False)
    obj, uvt, keys, _ = scene(True, extra=(("fascia_huge", (60.0, 0.0, 3.0), (60.12, 74.0, 3.42), "trim"),))
    lay, rep = run_tile(TS, obj, uvt, keys, True)
    assert rep["strip"]["auto_cut_parts"] == ["fascia_huge"], rep["strip"]
    assert TS.check_uv_cells(obj, lay, keys) == {}
    ss = TS.strip_stats(obj, lay["A"], keys)
    assert ss["trim_grain"]["along_p10"] >= 8.0 - 1e-6, ss["trim_grain"]
    extra = rep["tris_after"] - rep0["tris_after"]
    assert 0 < extra <= 48, extra
    obj, uvt, keys, _ = scene(True, extra=(("fascia_wide", (60.0, 0.0, 3.0), (60.12, 6.0, 3.6), "trim"),))
    try:
        run_tile(TS, obj, uvt, keys, True)
        raise AssertionError("a 0.6 m wide board did not raise")
    except RuntimeError as e:
        assert "across" in str(e), e
    ok(f"G4 a 74 m member (L5's main fascia: 6.5 px/m whole) is cut at the 60 m world lines (+{extra} tris, along p10 "
       f"{ss['trim_grain']['along_p10']} px/m, only that part); a 0.6 m wide board (> 0.52 m band) raises")


def check_segments(tris_v1):
    obj, uvt, keys, _ = scene(True)
    lay, rep = run_tile(TS, obj, uvt, keys, True, seg=4.0)
    assert rep["tris_after"] > tris_v1, (rep["tris_after"], tris_v1)
    assert TS.check_uv_cells(obj, lay, keys) == {}
    ss = TS.strip_stats(obj, lay["A"], keys)
    assert ss["trim_grain"]["along_p10"] >= 100.0, ss["trim_grain"]
    ok(f"G4 strip_seg_m 4: {rep['strip']['segments']} segments, LOD0 {tris_v1} -> {rep['tris_after']} tris "
       f"(+{rep['tris_after'] - tris_v1}), along p10 {ss['trim_grain']['along_p10']} px/m, cells clean")


def check_determinism():
    hs = []
    for _ in range(2):
        obj, uvt, keys, _m = scene(True)
        run_tile(TS, obj, uvt, keys, True)
        hs.append(lodge.mesh_hash(obj))
    assert hs[0] == hs[1], hs
    ok(f"G9 two v1 scenes hash identical ({hs[0]})")
    return hs[0]


def check_lodge_gating():
    args = lodge.parse_args(["--patrol", "p1"])
    assert args.trim == "off" and lodge.trim_cfg(lodge.select_recipe(args), args) is None
    real = (TS.trim_remap, TS.register_trim_materials)

    def boom(*_a, **_k):
        raise AssertionError("trim code called on a flag-off build")
    TS.trim_remap = TS.register_trim_materials = boom
    try:
        out = tempfile.mkdtemp(prefix="t70_off_")
        a = lodge.parse_args(["--patrol", "p1", "--blockout", "--out", out, "--no-validate", "--letter", "W"])
        lodge.clear_scene()
        r = lodge.run(lodge.select_recipe(a), a)
        assert "trim" not in r
    finally:
        TS.trim_remap, TS.register_trim_materials = real
    a = lodge.parse_args(["--patrol", "p1", "--blockout", "--out", tempfile.mkdtemp(prefix="t70_bl_"),
                          "--no-validate", "--letter", "W", "--trim", "v1"])
    try:
        lodge.clear_scene()
        lodge.run(lodge.select_recipe(a), a)
        raise AssertionError("--trim v1 on a blockout did not raise")
    except RuntimeError as e:
        assert "tiled build" in str(e), e
    ok("G8b trim_cfg None by default; a default lodge run never calls trim_remap / register_trim_materials; "
       "--trim v1 without a tiled build raises")


def main():
    ref = pre_t70_module()
    check_layout(ref)
    tris_off = check_flag_off(ref)
    check_scene(tris_off)
    check_long_and_wide(tris_off)
    check_segments(tris_off)
    check_determinism()
    check_lodge_gating()
    print(f"PASS test_trim_strip: {len(CHECKS)} checks")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
