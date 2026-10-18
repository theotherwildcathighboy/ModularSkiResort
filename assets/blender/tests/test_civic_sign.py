"""
test_civic_sign - contracts of the civic-sign build (design/civic-sign.md section 7 with
civic-sign-skeptic.md applied): the Welcome Sign generator, the school / patrol recipes and
the draft Write Everywhere layout.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_civic_sign.py [-- --quick]

Checks (exit 1 on any failure; --quick skips the two reduced bakes):
  WE layout   root WELayout, one textMesh, text@formulae GetEntityName, default "SKI AREA",
              MaxWidthMeters = welcome_sign.WE_MAX_WIDTH, offsetPosition = WE_OFFSET +- 0.001,
              fontName "", rescaleHeightOnTextOverflow "true", no hideMesh / imageMesh.
  band math   rects integral at A 128..4096, square emblem, header texel aspect +- 1 px,
              band top v <= 0.125.
  label frame _civic.display_map / texel_to_display on the 8 axis-aligned frames: the
              display's top-right lands at the viewer's top-right in the world.
  sign        two --blockout builds in one process: mesh hash + band PNG sha1 equal (G8);
              S1-S7 / G3 / G4 / S6 / G1 run inside every build (a failure raises); --masts 0
              and 3 build; a reduced finish (A 512, AO 4, LODs): engine base OK on LOD0-2,
              caps, the shipped BaseColor / ControlMask equal the band PNGs inside the
              reserved rects (N3: the band is not AO-darkened), _Emissive present.
  P1 / P2     --blockout: room cells (hut 12 / hq 18), `_Win` one UV layer and one cell,
              M1 (|x| <= 3.75 on P1), P2 lot 2 x 2 (R22), budgets 650 / 200 (M7), the
              front-only label faces (M3) are ONE UV island each, interior keyword in the name.
  G8 recipes  school / P1 / P2 --blockout twice: hashes, cells, cull counts equal.
  school      --blockout: 4 cells {store 6, upper 8, tower 12, back 19}, G10 (in the
              builder); reduced finish (A 1024, AO 2, no LODs): pictogram texels white with
              ControlMask R 0, the skier's head at the viewer's upper RIGHT (not mirrored).
Fine-detail step 3d (2026-10-12, build/fine-detail-step3d-patrol-sign.md): the sign's DEFAULT build is
the fine one (the SKI AREA header in the sign set SkiSignSetGateB, the lanterns as WallLightShortRange
rows, 12-seg masts); `--no-fine` is the pre-pass sign. Added:
  sign fine   the three header faces left the host (SG3) and form the sign set: SG1 UV = the painted
              rect, SG4 one identity row in the manifest's sub_objects (+ 2 short-range wall lights ON the
              post fronts, facing the road), SG5 inside the 2 x 1 lot, SG6 6 tris; the band keeps the emblem
              only; the finish ships NO _Emissive (no lamp_ part) and the emblem rect is restored 1:1 (N3)
  sign legacy `--no-fine` blockout = the shipped pre-pass sign (LOD0_prebake 3d778a71a9ab), header band
              painted; its finish keeps the _Emissive
  P1 / P2     the info / name board faces are in the sign sets, not in the host
"""

import hashlib
import json
import math
import os
import shutil
import sys
import tempfile
import time
import traceback
import xml.etree.ElementTree as ET

import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, HERE)
import bldg_recipes as R      # noqa: E402
import cs2_bake               # noqa: E402
import cs2_compose            # noqa: E402
import cs2_uv                 # noqa: E402
import lodge                  # noqa: E402
import welcome_sign as WS     # noqa: E402
import cs2_asset              # noqa: E402
import cs2_signset as SS      # noqa: E402  (fine-detail step 3d)
import fine_detail as FD      # noqa: E402

LEGACY_SIGN_HASH = "3d778a71a9ab"        # the shipped pre-pass sign (signs/A, 2026-10-11 10:03; K4 before 3d)

CIV = WS.C
QUICK = "--quick" in sys.argv
FAILS = []


def ok(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"FAIL {msg}")
    return cond


def sha(path):
    with open(path, "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()[:12]


# ======================================================================================
# WE layout
# ======================================================================================

def check_we_layout():
    path = os.path.join(REPO, "assets", "we", "K45_WE", "layouts", "SkiResortGate.wedefault.xml")
    root = ET.parse(path).getroot()
    ok(root.tag == "WELayout", f"WE root {root.tag}")
    tms = root.findall(".//textMesh")
    ok(len(tms) == 1, f"WE textMesh count {len(tms)}")
    tm = tms[0]
    txt = tm.find("text")
    ok(txt.get("formulae") == "&BelzontWE.Builtin.WEUtitlitiesFn;GetEntityName",
       f"WE formulae {txt.get('formulae')!r}")
    ok((txt.text or "") == "SKI AREA", f"WE default text {txt.text!r}")
    ok(abs(float(tm.find("MaxWidthMeters").text) - WS.WE_MAX_WIDTH) < 1e-6, "WE MaxWidthMeters")
    ok(tm.get("fontName") == "", "WE fontName")
    ok(tm.get("rescaleHeightOnTextOverflow") == "true", "WE rescale flag")
    op = root.find(".//transform/offsetPosition")
    got = tuple(float(op.get(a)) for a in "xyz")
    ok(max(abs(a - b) for a, b in zip(got, WS.WE_OFFSET)) <= 1e-3, f"WE offset {got} vs {WS.WE_OFFSET}")
    ok(root.find(".//hideMesh") is None and root.find(".//imageMesh") is None, "WE hideMesh/imageMesh")
    print(f"ok WE layout: offset {got}, max width {WS.WE_MAX_WIDTH}, GetEntityName, no hideMesh")


# ======================================================================================
# Band math and the label frame helper
# ======================================================================================

def check_band_math():
    for A in (128, 256, 512, 1024, 2048, 4096):
        rects = WS.band_rects(A)
        for k, r in rects.items():
            px = [x * A for x in r]
            ok(all(abs(p - round(p)) < 1e-9 for p in px), f"band {k} @ {A} not integral {px}")
        e = [x * A for x in rects["emblem"]]
        ok(abs((e[2] - e[0]) - (e[3] - e[1])) < 1e-9, f"emblem rect not square @ {A}")
        h = [x * A for x in rects["header"]]
        w_px, h_px = h[2] - h[0], h[3] - h[1]
        exp = w_px / (2 * WS.HEADER_Y) * (WS.HEADER_BEAM[1] - WS.HEADER_BEAM[0])
        ok(abs(exp - h_px) <= max(1.0, A / 1024.0), f"header aspect @ {A}: {exp:.2f} vs {h_px}")
        ok(max(r[3] for r in rects.values()) <= 0.125 + 1e-12, f"band top @ {A}")
    print("ok band rects: integral at 128..4096, square emblem, header aspect within 1 px, v <= 0.125")


def check_tex_flag():
    """T72: the sign bakes at --tex 2048 (about 2x the stone / roof px/m); the DEFAULT stays 1024 (the shipped export)."""
    ok(WS.TEX == 1024 and WS.parse_args([]).tex == 1024, f"sign default --tex {WS.parse_args([]).tex} != 1024")
    ok(WS.parse_args(["--finish", "--tex", "2048"]).tex == 2048 and WS.parse_args(["--closeups"]).closeups == "",
       "sign --tex 2048 / --closeups parse")
    print("ok sign --tex: default 1024, 2048 accepted, --closeups parses")


def check_display_frames():
    right = np.array((0.0, 1.0, 0.0))     # a +X-facing wall (authoring): viewer right = +Y
    up = np.array((0.0, 0.0, 1.0))
    n = np.array((1.0, 0.0, 0.0))
    frames = []
    for wu in (right, -right, up, -up):
        for wv in (right, -right, up, -up):
            if abs(float(wu @ wv)) > 0.5:
                continue
            frames.append((wu, wv))
    for wu, wv in frames:
        coeffs = CIV.display_map(wu * 0.01, wv * 0.01, n)
        h = w = 9
        drow, dcol, Hv, Wv = CIV.texel_to_display(h, w, coeffs)
        # the texel whose world position is most right + up must show display (row 0, col max)
        ii, jj = np.meshgrid(np.arange(h), np.arange(w), indexing="ij")
        world = (jj[..., None] * wu + ii[..., None] * wv)
        score = world @ right + world @ up
        i, j = np.unravel_index(np.argmax(score), score.shape)
        ok(drow[i, j] == 0 and dcol[i, j] == Wv - 1, f"frame {wu}/{wv}: top-right -> "
           f"({drow[i, j]}, {dcol[i, j]})")
    print(f"ok label frames: {len(frames)} axis-aligned UV frames map display top-right to the "
          f"viewer's top-right")


# ======================================================================================
# Sign
# ======================================================================================

def sign_run(argv):
    args = WS.parse_args(argv)
    return WS.run(args)


def check_sign(tmp):
    res = []
    for i in range(2):
        r, _obj = sign_run(["--blockout", "--out", os.path.join(tmp, f"sblk{i}", "A")])
        res.append((r["hashes"]["LOD0_prebake"], r["band"]["png_sha1"], r["tris"][0]))
    ok(res[0] == res[1], f"sign blockout determinism {res[0]} vs {res[1]}")
    r0 = r
    c = r0["contracts"]
    ok(set(("S1", "S2", "S4", "S7", "G3")) <= set(c), f"sign contracts ran: {sorted(c)}")
    ok(r0["tris"][0] <= WS.BUDGET.lod0_cap, "sign LOD0 cap")
    g3 = r0["contracts"]["G3"]
    ok(g3["margin_x"] >= WS.LOT_MARGIN + 0.02 and g3["margin_y"] >= WS.LOT_MARGIN + 0.02,
       f"G3 slack >= 0.02 past the contract margin (skeptic should-fix): {g3}")
    print(f"ok sign: blockout x2 identical {res[0][0]} / band {res[0][1]}, LOD0 {res[0][2]}, "
          f"contracts {sorted(c)}, G3 {c['G3']}")
    check_sign_fine(r0, _o_name(r0))         # fine-detail step 3d (its objects are still alive here)
    # S7 must catch the contract's original rake (front x +0.30: the 45-deg view loses the
    # top of the emblem, skeptic finding 1 / M5) - proves the check is not vacuous
    saved = WS.RAKE_FRONT_X
    WS.RAKE_FRONT_X = 0.30
    try:
        sign_run(["--blockout", "--out", os.path.join(tmp, "s7neg", "A")])
        ok(False, "S7 did not fire with the front rake at x +0.30")
    except RuntimeError as e:
        ok("S7" in str(e), f"S7 negative: unexpected error {e}")
        print(f"ok S7 fires on the contract's original rake: {str(e)[:90]}")
    finally:
        WS.RAKE_FRONT_X = saved
    for m in (0, 3):
        r, _o = sign_run(["--blockout", "--masts", str(m), "--out", os.path.join(tmp, f"sm{m}", "A")])
        ok(r["base"][0]["verdict"] == "OK", f"sign masts {m} base")
        print(f"ok sign --masts {m}: LOD0 {r['tris'][0]}, G3 {r['contracts']['G3']}")
    # fine-detail step 3d: --no-fine = the shipped pre-pass sign; the fine blockout routes the header out
    rl, _o = sign_run(["--blockout", "--no-fine", "--out", os.path.join(tmp, "slegacy", "A")])
    ok(rl["hashes"]["LOD0_prebake"] == LEGACY_SIGN_HASH and sorted(rl["band"]["rects"]) == ["emblem", "header"],
       f"sign --no-fine blockout {rl['hashes']['LOD0_prebake']} != the shipped {LEGACY_SIGN_HASH}")
    print(f"ok sign --no-fine blockout = the shipped pre-pass sign {rl['hashes']['LOD0_prebake']}, band "
          f"{sorted(rl['band']['rects'])}")
    if QUICK:
        return
    out = os.path.join(tmp, "sfin", "A")
    r, _o = sign_run(["--finish", "--tex", "512", "--ao-samples", "4", "--lods", "--no-validate",
                      "--out", out])
    A = r["tex"]
    ok(A == 512, f"sign finish A {A}")
    for lvl in (0, 1, 2):
        ok(r["base"][lvl]["verdict"] == "OK", f"sign LOD{lvl} engine base {r['base'][lvl]}")
    ok(r["tris"][1] <= WS.BUDGET.lod1 and r["tris"][2] <= WS.BUDGET.lod2_cap,
       f"sign LOD budgets {r['tris']}")
    d = r["asset_dir"]
    name = r["name"]
    band_dir = r["band"]["dir"]
    for m in ("BaseColor", "ControlMask"):
        ship = cs2_bake._load_rgba(os.path.join(d, f"{name}_{m}.png"), A, m)
        band = cs2_bake._load_rgba(os.path.join(band_dir, f"{name}_band_{m}.png"), A, m)
        for k, rect in WS.band_rects(A, tuple(r["band"]["rects"])).items():
            sl = cs2_compose._rect_slices(rect, A)
            diff = float(np.abs(ship[sl][..., 0:3] - band[sl][..., 0:3]).max())
            ok(diff <= 1.5 / 255.0, f"N3 band {k} {m} differs by {diff * 255:.1f}/255")
    ok(not os.path.isfile(os.path.join(d, f"{name}_Emissive.png")) and r["bake"]["files"] ==
       [f"{name}_{m}.png" for m in ("BaseColor", "Normal", "MaskMap", "ControlMask")],
       f"sign fine finish: no _Emissive (lanterns are rows) {r['bake']['files']}")
    ss = r["sign_set"]
    sd = os.path.join(os.path.dirname(out), "signs", WS.SIGN_SET.letter, WS.SIGN_SET.mesh)
    sett = json.load(open(os.path.join(sd, "settings.json"), encoding="utf-8"))
    want = {f"{WS.SIGN_SET.mesh}_{m}.png": f"../{SS.SHEET_OWNER}/{SS.SHEET_OWNER}_{m}.png" for m in SS.MAPS}
    ok(sett == {"sharedAssets": want} and ss["validate"]["errors"] == [] and ss["tris"] == 6,
       f"sign set folder / validate {ss.get('validate')} tris {ss.get('tris')}")
    print(f"ok sign finish @ {A}: LOD {r['tris']}, engine base OK on 0-2, band rects {sorted(r['band']['rects'])} "
          f"restored 1:1 after AO (N3), no _Emissive; sign set {WS.SIGN_SET.mesh} {ss['tris']} tris, m_Bias "
          f"{ss['cull']['m_Bias']} (target {ss['cull']['target_m']} m), sharer settings, 0 validator errors")
    # the --no-fine finish keeps the pre-pass map set (the band's header, the lanterns' _Emissive)
    ol = os.path.join(tmp, "sfinl", "A")
    rl, _o = sign_run(["--finish", "--no-fine", "--tex", "512", "--ao-samples", "4", "--no-validate", "--out", ol])
    ok(os.path.isfile(os.path.join(rl["asset_dir"], f"{name}_Emissive.png")), "sign --no-fine _Emissive")
    print("ok sign --no-fine finish: _Emissive present (modelled lanterns)")


def _o_name(r):
    return r["name"]


def check_sign_fine(r, name):
    """Fine-detail step 3d on the fine blockout `r` (its object is alive): the header faces left the host
    (SG3), the sign set (SG1, SG5, SG6) and the manifest rows (SG4 + the two post-front wall lights)."""
    obj = bpy.data.objects[name]
    table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    spec = WS.SIGN_SET
    ok(not SS.sg3_no_routed_left({"LOD0": table}, [], spec) and not any(n.startswith("header_") for n in table)
       and not any(n.startswith("lamp_") for n in table), f"sign SG3: routed / lamp parts left {table}")
    ok(sorted(r["band"]["rects"]) == ["emblem"], f"sign fine band keys {sorted(r['band']['rects'])}")
    sobj = r["sign_set"]["obj"]
    ok(not SS.sg1_uv_in_rect(sobj, spec), f"sign SG1 {SS.sg1_uv_in_rect(sobj, spec)}")
    b = SS.bounds_game(sobj)
    ok(not SS.sg5_in_lot(b, (4.0 * WS.LOT[0], 4.0 * WS.LOT[1])), f"sign SG5 {b}")
    n = cs2_asset.tri_count(sobj)
    ok(not SS.sg6_tris(n) and n == 6, f"sign SG6 {n}")
    man = json.load(open(os.path.join(r["asset_dir"], f"{name}.manifest.json"), encoding="utf-8"))
    sub = man.get("sub_objects") or {}
    cid = FD.staged_cid(spec.prop, spec.letter, kind="signs") or SS.PLACEHOLDER_CID
    ok(not SS.sg4_identity_row(sub, spec, cid), f"sign SG4 {SS.sg4_identity_row(sub, spec, cid)}")
    ok(sub.get("target_prefab") == "SkiResortGate" and sub.get("kinds") == {"vanilla": 2, "mod": 1},
       f"sign rows {sub.get('target_prefab')} {sub.get('kinds')}")
    lights = [o for o in sub.get("objects", []) if o.get("prefab") == "WallLightShortRangeRandom01"]
    lx, lz = WS.LANTERN_AT
    ok(len(lights) == 2 and all(abs(o["position"][2] - lx) < 1e-6 and abs(o["position"][1] - lz) < 1e-6
                                and abs(abs(o["position"][0]) - WS.PIER_Y) < 1e-6 and o["rotation"] == [0, 0, 0, 1]
                                and o["m_ParentMesh"] == 0 for o in lights),
       f"sign wall-light rows {lights}")
    # a light on its post front: the post (POST size 0.35, z 2.62..4.45) holds the 0.284 x 0.116 light
    s_, z0, z1 = WS.POST
    ok(0.284 <= s_ and z0 <= lz - 0.058 and lz + 0.058 <= WS.LOWER_BEAM[0], "sign wall light fits the post front")
    print(f"ok sign fine: header faces routed (SG3), band {sorted(r['band']['rects'])}, sign set SG1 / SG5 {b} / "
          f"SG6 {n} tris, SG4 one identity row (CID {cid[:8]}), 2 post-front WallLightShortRange rows, no lamp_")


# ======================================================================================
# Recipes
# ======================================================================================

def recipe_run(key, extra, out):
    args = lodge.parse_args(["--recipe", key] + extra + ["--out", out])
    return lodge.run(R.get_recipe(key), args)


def _part_faces(obj, prefix):
    table = json.loads(obj.data[lodge.cs2_asset.PART_TABLE_PROP])
    pv = np.zeros(len(obj.data.polygons), np.int32)
    obj.data.attributes["part"].data.foreach_get("value", pv)
    ids = {i + 1 for i, n in enumerate(table) if n.startswith(prefix)}
    return [i for i in range(len(pv)) if int(pv[i]) in ids]


def check_patrol(tmp):
    for key, cells, label, lot in (("P1", {"hut": 12}, "emblem_p1_face", (1, 1)),
                                   ("P2", {"hq": 18}, "emblem_hq_face", (2, 2))):
        rec = R.get_recipe(key)
        r = recipe_run(key, ["--blockout"], os.path.join(tmp, key, "A"))
        name = r["name"]
        ok("CommercialLow" in name, f"{key} keyword in {name}")
        ok(r["cells"] == cells, f"{key} cells {r['cells']}")
        win = bpy.data.objects.get(f"{name}_Win")
        ok(win is not None and len(win.data.uv_layers) == 1, f"{key} _Win UV layers")
        import cs2_bldg as K
        ids = set(K.win_cell_ids(win))
        ok(ids == set(cells.values()), f"{key} _Win cell ids {ids}")
        ok(tuple(rec.lot) == lot, f"{key} lot {rec.lot}")
        ok((rec.budget.lod1, rec.budget.lod2, rec.budget.lod2_cap) == (650, 200, 200),
           f"{key} budget {rec.budget}")
        x0, x1 = r["lot"]["x"]
        y0, y1 = r["lot"]["y"]
        ok(max(-x0, x1) <= 4 * lot[1] - 0.25 + 1e-6 and max(-y0, y1) <= 4 * lot[0] - 0.25 + 1e-6,
           f"{key} lot margin x {r['lot']['x']} y {r['lot']['y']}")
        obj = bpy.data.objects[name]
        faces = _part_faces(obj, label)
        isl = cs2_uv.uv_islands(obj.data, obj.data.uv_layers[cs2_bake.TILE_LAYER], faces=faces)
        ok(len(faces) == 16 and len(isl) == 1, f"{key} M3 label {label}: {len(faces)} faces, "
                                                f"{len(isl)} islands")
        print(f"ok {key} ({name}): cells {r['cells']}, lot x {r['lot']['x']} y {r['lot']['y']}, "
              f"label {label} 1 island / {len(faces)} faces, LOD0 {r['tris'][0]}")
        if key == "P1":
            ok(x1 <= 3.75 + 1e-6, f"M1: P1 max x {x1}")
        # fine-detail step 3d: the info / name boards are sign-set faces, not host faces
        routed = FD.sign_set_of(rec).face_names()
        ok(all(len(_part_faces(obj, f)) == 0 for f in routed) and sorted(r["sign_set"]["faces"]) == sorted(routed),
           f"{key} sign-set faces {routed} left the host ({r['sign_set']['faces']})")


def check_recipe_determinism(tmp):
    """G8 for the three recipes: two --blockout builds in one process give the same
    joined-mesh / `_Win` hashes, room cells and cull counts (the lodge determinism
    pattern; the finish-pass determinism of the shared engine is test_lodge_determinism)."""
    for key in ("school", "P1", "P2"):
        runs = []
        for i in range(2):
            r = recipe_run(key, ["--blockout"], os.path.join(tmp, f"det{key}{i}", "A"))
            runs.append((r["hashes"], r["cells"], r["cull"]["faces_removed"], r["tris"][0]))
        ok(runs[0] == runs[1], f"{key} blockout determinism {runs[0]} vs {runs[1]}")
        print(f"ok {key} determinism: {runs[0][0]}, cull {runs[0][2]}, LOD0 {runs[0][3]}")


def check_school(tmp):
    import cs2_asset
    import cs2_bldg as K
    key = "school"
    r = recipe_run(key, ["--blockout"], os.path.join(tmp, "schoolblk", "A"))
    ok(r["cells"] == {"back": 19, "store": 6, "tower": 12, "upper": 8}, f"school cells {r['cells']}")
    ok(r["base"][0]["verdict"] == "OK", "school base")
    # F4 (buildings-review-geometry): the lantern columns survive to LOD2 (kit LOD2_DELETE
    # drops "lanterncol_"; the recipe's lod2_keep names the exact part), so the cap never floats
    tbl = json.loads(bpy.data.objects[r["name"]].data[cs2_asset.PART_TABLE_PROP])
    _d1, d2 = lodge.delete_lists(R.get_recipe(key), tbl)
    ok("lanterncol_school" in tbl and not "lanterncol_school".startswith(d2),
       "school lanterncol_school is deleted at LOD2 (the lantern cap would float)")
    print(f"ok school blockout: cells {r['cells']}, LOD0 {r['tris'][0]}, lot {r['lot']['x']} / "
          f"{r['lot']['y']}, G10 asserted in the builder")
    if QUICK:
        return
    r = recipe_run(key, ["--tex", "1024", "--ao-samples", "2", "--no-lods", "--no-validate"],
                   os.path.join(tmp, "schoolfin", "A"))
    A = r["tex"]
    name = r["name"]
    obj = bpy.data.objects[name]
    me = obj.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    face = "emblem_school_face"
    rect = cs2_compose.uv_bbox_px(me, table, face, A)
    r0, r1, c0, c1 = rect
    base = cs2_bake._load_rgba(os.path.join(r["asset_dir"], f"{name}_BaseColor.png"), A, "base")
    ctl = cs2_bake._load_rgba(os.path.join(r["asset_dir"], f"{name}_ControlMask.png"), A, "ctl")
    sub_b, sub_c = base[r0:r1, c0:c1], ctl[r0:r1, c0:c1]
    glyph = sub_c[..., 0] < 0.5
    frac = float(glyph.mean())
    ok(0.05 < frac < 0.6, f"pictogram coverage {frac:.3f}")
    # glyph texels (ControlMask R zeroed by stamp_on_face) are white; the antialiased rim
    # blends with the siding, so judge the median and the core
    lum = sub_b[glyph][:, 0:3].mean(-1)
    ok(float(np.median(lum)) >= 0.85 and float((lum >= 0.88).mean()) >= 0.5,
       f"pictogram texels not white: median {float(np.median(lum)):.3f}, "
       f"core share {float((lum >= 0.88).mean()):.2f}")
    # orientation: the skier's head (cand_d disk at canvas (172, 44) of 256) must sit at the
    # viewer's upper RIGHT of the field; its mirror image must be field (slot 1)
    Wu, Wv, n = CIV.uv_frame(me, face, A=A)
    fpoly = me.polygons[_part_faces(obj, face)[0]]
    P = np.array([tuple(obj.matrix_world @ me.vertices[v].co) for v in fpoly.vertices])
    lay = me.uv_layers[cs2_uv.UNIQUE_LAYER]
    Q = np.array([tuple(lay.data[li].uv) for li in range(fpoly.loop_start,
                                                         fpoly.loop_start + fpoly.loop_total)]) * A
    centre_w, centre_q = P.mean(0), Q.mean(0)
    upv = np.array((0.0, 0.0, 1.0))
    right = np.cross(-n, upv)
    right /= np.linalg.norm(right)
    side = float((P.max(0) - P.min(0)).max())              # 3.2 m square field (edge, not diagonal)
    L = side * 0.85
    M = np.stack([Wu, Wv], axis=1)                          # world = M @ (du, dv)
    hits = {}
    for tag, sx in (("head", 1.0), ("mirror", -1.0)):
        x = (172.0 / 256.0 - 0.5) * L * sx
        y = (0.5 - 44.0 / 256.0) * L
        wpt = centre_w + right * x + upv * y
        dq, *_ = np.linalg.lstsq(M, wpt - centre_w, rcond=None)
        q = centre_q + dq
        hits[tag] = float(ctl[int(q[1]), int(q[0]), 0])
    ok(hits["head"] < 0.5 and hits["mirror"] > 0.5, f"pictogram orientation {hits}")
    print(f"ok school finish @ {A}: pictogram {frac:.1%} of the field white with ControlMask R 0, "
          f"head at upper right (R {hits['head']:.2f}), mirror field (R {hits['mirror']:.2f})")


def check_school_manifest():
    """The exported school manifest carries the sub_objects block. Detail pass 1: lodge.py
    writes it from Recipe.sub_objects on every export (lodge.sub_objects_block: per-object
    m_Object / prefab / position / rotation / m_ParentMesh, skeptic S4), so a re-export no
    longer drops it (recipes/school.py -- --merge-manifest is kept for older exports)."""
    d = os.path.join(REPO, "assets", "import", "buildings", "A", "SkiSchoolCommercialLowA")
    path = os.path.join(d, "SkiSchoolCommercialLowA.manifest.json")
    if not os.path.isfile(path):
        print(f"SKIP school manifest: not exported ({path})")
        return
    rec = R.get_recipe("school")
    with open(path, encoding="utf-8") as fh:
        man = json.load(fh)
    sub = man.get("sub_objects") or {}
    objs = sub.get("objects") or []
    # fine-detail step 2: the school's manifest_block() = lodge.sub_objects_block(recipe) + the
    # sign-set identity row lodge appends at export (its staged CID)
    want = rec.builder.__globals__["manifest_block"]()["sub_objects"]
    n_extra = 1 if getattr(rec, "sign_set", None) is not None else 0
    ok(len(objs) == len(rec.sub_objects) + n_extra == len(want["objects"]),
       f"school manifest sub_objects: {len(objs)} objects (want {len(rec.sub_objects)} from "
       f"Recipe.sub_objects + {n_extra} sign-set row; re-export with lodge.py --school)")
    ok(sub.get("target_prefab") == man.get("prefab") == "SkiSchool",
       f"school sub_objects target {sub.get('target_prefab')} / prefab {man.get('prefab')}")
    # DECISIONS 245 (T14 x-sign N, 2026-10-16; T53): the shipped manifest holds the PRE-T14 rows until the next
    # export regenerates it. Stale = every row's position is the build's with x negated (rule T) and nothing
    # else differs: SKIP (loud). Anything else, or a match, is asserted as before - never silently passes.
    def _pre_t14(o):
        return {**o, "position": [-float(o["position"][0]) + 0.0, *map(float, o["position"][1:])]}
    stale = (objs != want["objects"] and len(objs) == len(want["objects"])
             and all({**a, "position": [round(float(v), 3) for v in a["position"]], "rotation": None}
                     == {**_pre_t14(b), "position": [round(float(v), 3) for v in _pre_t14(b)["position"]],
                         "rotation": None}
                     for a, b in zip(objs, want["objects"])))
    if stale:
        print("SKIP school manifest sub_objects == recipes/school.SUB_OBJECTS: shipped manifest is stale (pre-T14 "
              "un-mirrored rows) until the next export, DECISIONS 245 / T53")
    else:
        ok(objs == want["objects"],
           "school manifest sub_objects differ from recipes/school.SUB_OBJECTS (Recipe.sub_objects)")
    print(f"ok school manifest: sub_objects {len(objs)} objects -> {sub.get('target_prefab')} "
          f"(m_ParentMesh 0 on {sum(o['m_ParentMesh'] == 0 for o in objs)})")


def main():
    t0 = time.perf_counter()
    check_school_manifest()
    check_we_layout()
    check_band_math()
    check_tex_flag()
    check_display_frames()
    tmp = tempfile.mkdtemp(prefix="civic_sign_")
    try:
        check_sign(tmp)
        check_patrol(tmp)
        check_recipe_determinism(tmp)
        check_school(tmp)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    if FAILS:
        print(f"FAIL test_civic_sign: {len(FAILS)} failure(s): {FAILS[:8]}")
        sys.exit(1)
    print(f"PASS test_civic_sign: WE layout, band math, label frames, sign (determinism, "
          f"contracts{', finish' if not QUICK else ''}), P1 / P2, school"
          f"{' + pictogram' if not QUICK else ''} ({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_civic_sign")
        sys.exit(1)
