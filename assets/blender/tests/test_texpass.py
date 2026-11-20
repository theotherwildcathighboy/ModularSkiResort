"""
test_texpass - texture pass v1 (`lodge.py --texpass v1`, docs/PIPELINE.md "Texture pass", the L2 hut pilot).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_texpass.py

Gates (the pilot's lessons, made mechanical):
  P1  provenance: every set a pass names (alias followers included) has an assets/texture-sources.json entry (CC0, a
      page, an https url and an md5 per map); a set folder that is present matches those md5s byte for byte; every
      Poly Haven scan a pass draws is named in release/CREDITS.md with its authors;
  P2  same cut grid: per swapped key, the square tile x the sheet repeat it draws with == the flag-off product, so
      no building's LOD0 cut grid (and so no tri count) moves; no tile rect or palette rect moves;
  P3  flag off untouched: register v1 then unregister restores every BldgMat and the off layout (hash included);
      l5 register_materials stays quiet with the pass on and off (lux_slate follows slate: same set, tile, mean);
      the off layout carries no texpass marker, the v1 hash differs; lodge's --texpass defaults to off;
  P4  grade bands: a plinth + timber wall on a 35 % grade through tile_mesh: grade (0, 0, 0, inf) == the z-0 rule
      byte for byte; under the real grade every weathered face lies within base_h of the grade (side by side), every
      clean plinth face above it, the timber wall takes no band (the z-0 rule weathers the whole plinth and bands
      the timber), and no cut vertex lands above the cap (the plinth's z-0 contract band stays free);
  P4c band_skip: a 0.20 m post beside that plinth is banded and cut under the grade; with band_skip ("col_",) it is
      clean and uncut while every other face keeps its state (the hut's posts: 192 tris and a weather level);
  P4d drip_skip: a wall with two window sills (sill_a, sill_b) through tile_mesh: sill drips hang a strip under each;
      with drip_skip ("sill_b",) sill_a's strip stays as it was, sill_b's goes, and every other face keeps its state;
      no ladder step sets it (a flag-off build hangs every strip it did); lodge.texpass_drip_skip returns the matched
      sills and raises on a prefix that is not a sill_ or matches no part; _write_manifest copies its result key
      (texpass_drip_skip, as P15's texpass_mat); each recipe that opts in names its real
      sills (L1's loft pair, a 0.7 m strip that ends mid-gable; the hut's attic pairs and L3's Mass B gable window,
      a strip that runs off the shingle gable onto the boards below: each read as a hard rectangle under the window; the
      strip gate's find, P16: L4's balcony door ubf1, whose strip ran from the walnut 0.55 m down onto the stone under
      the balcony slab);
  P5  lodge.texpass_weather: the hut recipe's grade in the tiled frame matches the authoring grade through
      cs2_asset.orient_for_net; eave_h overrides every side; band_skip and drip_skip carried; a yawed recipe raises;
      None stays None;
  P6  real-world scale: every pass key has a TEXPASS_PITCH range and its scan's course / board pitch (the fundamental of
      the Color map's row autocorrelation at the set's size_m) times tile_m / size_m lies in it (the 0.6 x shake scan
      failed this; the strongest peak passed the siding at half its intended board width); a key in
      TEXPASS_SOLID (a seam-free member face) has no pitch and no range;
  P7  swatch controls: deck chroma + flatten_m + contrast keep the swatch mean within 0.01 sRGB (the palette) and cut
      the mean chroma and the luminance CoV, normal_gain raises the tilt rms; texpass_swatch is {} with the pass off
      (alias followers too);
  P12 normal lean: every pass key with normal_lean draws a sheet swatch whose mean normal points at the stated
      (u, v) lean within 0.5 deg (a lapped scan's mean normal leans: PHGrayRoofTiles02 at gain 1.3 leaned (-9.0,
      -7.2) deg against the shipped slate's (-0.3, +10.6): the sun shaded every slate roof as if it faced 20 deg
      elsewhere); the lean is the last control, so normal_gain cannot scale it again;
  P13 no baked tilt: every pass key's sheet swatch (after normal_gain and normal_lean, as the sheet draws it) has a mean
      height slope under TEXPASS_MAX_MEAN_SLOPE_DEG in u and v (a relief that tiles averages 0; PHGrayRoofTiles02
      carries a bake tilt of +9.2 / +7.8 deg at gain 1.3, which turned its lean and darkened the shaded roof slope by
      23 %); slate without its normal_lean fails it; the one exception is a roof kick (TEXPASS_ROOF_KICK_MAX_DEG): such
      a key may lean its v slope toward +v (up the slope) down to -limit, never the other way (a mirrored kick fails);
      that +v is up the slope is the export's property: tests/test_roof_uvv.py checks it per asset;
  P8 weather level: every recipe that opts in states texpass weather_level; lodge.assert_texpass_level raises when
      a pass build falls below it (PIPELINE gate 316: never buy the look with weathering) and is quiet otherwise;
  P14 ridge key: recipe.texpass ridge_key is L1's alone (metal_roof_lt); lodge.texpass_ridge moves the galv ridge_
      parts onto it and nothing else, and raises on a key outside TEXPASS_RIDGE_KEYS, a roof slab on another key, or
      no ridge; lodge.texpass_ridge_prefixes is trim v1's pair without a ridge_key (pass off: flag-off byte for
      byte) and draws ridge_ flat (no strip) with one; every ridge key has a v1 tile + "~f" cell and is coated;
  P15 mat rule: lodge.py reads recipe.texpass in one place (texpass_rules), which is {} for every recipe with the flag
      off, so no flag-off build sees a "mat" rule; lodge.texpass_mat moves only the parts a prefix names (the
      longest prefix wins; other parts keep key and object), raises on an unknown key and on a prefix that matches no
      part; no live user needed: L3's real parts under a synthetic copy of its recipe that names the two balustrades
      move exactly those two timber_heavy -> timber_lap under v1 and nothing with the flag off, and a prefix one
      character off a real part name raises;
  P9  far set in flag-off units: texpass_tile_factors is {slate, lux_slate, timber_heavy, lux_glulam_s2, siding_paint: 2} (off: {}); a plinth + lap
      wall + slate roof through lodge's prep: the pass's UVTile is exactly 2 x the flag-off one on the pass keys'
      faces (meters / square tile); after lodge.texpass_far_units + align_islands + split_poor_islands the UVTile and
      the dry pack's UVUnique equal the flag-off ones bit for bit, and the sampling layer is the flag-off UVTile x 2
      on the pass faces (the bake draws the scans at their tile on the flag-off layout; split in the pass's units,
      every far FBX was repacked and L1 lost 11 % of its far px/m);
  P10 low frequency: every pass key's sheet swatch keeps its luminance CoV after a periodic 0.2 m Gaussian (the
      blotches that read as a grid when the tile repeats) at or under max(1.25 x the pre-pass swatch, 0.03); the deck
      without flatten_m fails it (v1's first family round: 0.021 -> 0.089, a blotch grid on L3's 15 m deck; the
      mutation drops row_shuffle too, which alone breaks the 0.2 m stain grid: 0.053 -> 0.028);
  P11 far sources: texpass_far_specs runs the same controls on the prefiltered far sources (the far bake drew the raw
      scan): the deck's low frequency halves at least and its mean x tint stays on the palette, the normal-only keys
      keep their color bytes and tint, lux_slate takes slate's controls, a key outside the pass and the off state are
      untouched;
  P16 strip gate: cs2_tilesheet.audit_strips on P4d's wall after tile_mesh (the plan from LAST_STRIPS, each strip
      named by its sill): both strips lie on the timber wall over their full height and pass in RAISE mode; a planted
      strip moved 1.1 m down (its bottom 0.3 m on the stone plinth: an overrun) and one moved 2.5 m along the wall
      (0.64 m past its end: in the air) are reported with those lengths and RAISE; "report" mode only reports; a
      STRIP_ALLOW line makes a strip "allowed" (flag "off": only with the pass off, the same strip under v1 raises; a
      bad flag fails); lodge.py runs the gate after the weather ladder.
Exit 1 on the first failure; one PASS line otherwise.
"""

import dataclasses
import hashlib
import json
import math
import os
import re
import sys
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

SOURCES = os.path.join(os.path.dirname(HERE), "texture-sources.json")
MAPS = ("Color", "NormalGL", "Roughness")
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


def snapshot():
    TS.ensure_materials()
    return {k: repr(m) for k, m in sorted(K.BLDG_MATERIALS.items())}


def layout():
    lay = TS.sheet_layout(4096, "rollout", lodge._sheet_repeats(), trim=True, slots=True, glass=True)
    lay["label_rect"] = list(lay["label_region"])
    return lay


# ---------------------------------------------------------------------------------------------------------

def check_provenance():
    with open(SOURCES, encoding="utf-8") as fh:
        src = json.load(fh)["sets"]
    hashed = 0
    for v in TS.TEXPASS:
        for k, s in sorted(TS.texpass_specs(v).items()):
            if "set" not in s:          # detail-only key (lux_vboard_red): no download
                continue
            e = src.get(s["set"])
            assert e is not None, f"{v} {k}: {s['set']} has no texture-sources.json entry"
            assert e["license"] == "CC0" and e["page"].startswith("https://"), (s["set"], e["license"], e["page"])
            for m in MAPS:
                mp = e["maps"][m]
                assert mp["url"].startswith("https://") and re.fullmatch(r"[0-9a-f]{32}", mp["md5"]), (s["set"], m)
                sdir = os.path.join(cs2_bake.TEXTURE_ROOT, s["set"])
                p = cs2_bake.find_set_map(sdir, m) if os.path.isdir(sdir) else None
                if p is not None:
                    with open(p, "rb") as fh:
                        assert hashlib.md5(fh.read()).hexdigest() == mp["md5"], f"{p}: md5 differs from the source"
                    hashed += 1
    # Credits: every Poly Haven scan a pass draws is named in release/CREDITS.md with its authors (1.0.4: three
    # pass scans shipped in the table before their credit line; CC0 does not require the line, but the listing and
    # the docs site copy it).
    with open(os.path.join(os.path.dirname(os.path.dirname(HERE)), "release", "CREDITS.md"), encoding="utf-8") as fh:
        credits = fh.read()
    ph = sorted({s["set"] for v in TS.TEXPASS for s in TS.texpass_specs(v).values()
                 if "set" in s and src[s["set"]]["source"] == "Poly Haven"})
    for name in ph:
        e = src[name]
        assert f"`{e['id']}`" in credits, f"{name}: release/CREDITS.md does not name `{e['id']}`"
        for a in e["authors"]:
            assert a in credits, f"{name}: release/CREDITS.md does not name {a} ({e['id']})"
    ok(f"P1 provenance: every pass set listed (CC0, page, url + md5 per map); {hashed} local maps match their md5; "
       f"{len(ph)} Poly Haven scans credited")


def check_cut_grid_and_round_trip():
    assert TS.texpass_version() is None, "a pass is already registered"
    off0 = layout()                     # registers the trim / slot / glass keys first: the snapshot holds them
    snap0 = snapshot()
    for v in TS.TEXPASS:
        TS.register_texpass(v)
        try:
            on = layout()
            assert on["hash"] != off0["hash"], "the pass must change the sheet hash"
            # the one cell a pass may RENAME (same rect): the orphaned metal_roof~w taken by the coated light roof
            ren = {}
            if TS.TEXPASS_FLAT.get(v, {}).get("metal_roof_lt", {}).get("light_w"):
                ren["metal_roof" + TS.W_SUFFIX] = "metal_roof_lt" + TS.W_SUFFIX
            for k, t in off0["tiles"].items():
                kn = ren.get(k, k)
                assert on["tiles"][kn]["rect_px"] == t["rect_px"], (k, t["rect_px"], on["tiles"][kn]["rect_px"])
            assert set(on["tiles"]) == {ren.get(k, k) for k in off0["tiles"]} and on["palette"] == off0["palette"]
            specs = TS.texpass_specs(v)
            for k in sorted(specs):
                sk = TS.swatch_key(on, k)
                assert sk is not None, f"{k}: a pass key must draw a tiled swatch"
                new = TS.tile_m_of(k) * on["tiles"][sk]["repeat"]
                old = K.square_tile_m(TS._TEXPASS_STATE["saved"][k]) * off0["tiles"][sk]["repeat"]
                assert abs(new - old) < 1e-9, f"{k}: cut grid {new} m != flag-off {old} m"
                r = on["tiles"][sk]["repeat"]
                assert r & (r - 1) == 0, f"{k}: repeat {r} is not a power of two"
            assert TS.tile_m_of("lux_slate") == TS.tile_m_of("slate")
            from recipes import l5_swanky_bottom as l5
            l5.register_materials()     # re-registering with the pass on must not raise
            assert K.BLDG_MATERIALS["lux_slate"].set == K.BLDG_MATERIALS["slate"].set
        finally:
            TS.unregister_texpass()
        ok(f"P2 {v}: {len(TS.texpass_specs(v))} keys keep their cut grid (tile x repeat), no rect moves, hash changes")
    from recipes import l5_swanky_bottom as l5
    l5.register_materials()
    snap1 = snapshot()
    assert snap1 == snap0, f"unregister_texpass left BldgMats changed: " \
        f"{sorted(k for k in set(snap0) | set(snap1) if snap0.get(k) != snap1.get(k))}"
    off1 = layout()
    assert json.dumps(off1, sort_keys=True, default=str) == json.dumps(off0, sort_keys=True, default=str)
    assert TS.texpass_repeats() == {} and TS.texpass_overlay({"x": 1}) == {"x": 1}
    assert lodge.parse_args(["--recipe", "L2"]).texpass == "off"
    ok("P3 register + unregister: every BldgMat and the off layout (hash included) restored; --texpass defaults off")


def scene(post=False, sills=False):
    """(obj, uvtile, keys): a stone_rubble plinth (z -4..0) under a timber_lap box (z 0..3), 4 x 8 m, lodge prep;
    post: plus a free-standing 0.20 m timber_heavy post `col_a` beside the plinth (x -0.6, y 4); sills: plus two
    1.2 m window sills `sill_a` (y 1.0..2.2) and `sill_b` (y 5.0..6.2) on the -X face, bottom at z 1.5."""
    clear()
    TS.ensure_materials()
    parts = K.box_part("plinth_a", (0.0, 0.0, -4.0), (4.0, 8.0, 0.0), "stone_rubble")
    parts += K.box_part("wall_a", (0.0, 0.0, 0.0), (4.0, 8.0, 3.0), "timber_lap")
    if post:
        parts += K.box_part("col_a", (-0.7, 3.9, -4.0), (-0.5, 4.1, 0.0), "timber_heavy")
    if sills:
        parts += K.box_part("sill_a", (-0.15, 1.0, 1.5), (0.0, 2.2, 1.6), "timber_heavy")
        parts += K.box_part("sill_b", (-0.15, 5.0, 1.5), (0.0, 6.2, 1.6), "timber_heavy")
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
    return obj, cs2_uv.loop_uvs(obj.data.uv_layers[cs2_bake.TILE_LAYER]), keys


def run(rules, post=False, sills=False):
    """Mesh facts of the scene through tile_mesh with `rules` (TileWeather or None)."""
    obj, uvt, keys = scene(post, sills)
    TS.tile_mesh(obj, uvt, keys, layout(), weather=rules, flat_prefixes=TS.FLAT_DETAIL_PREFIXES)
    me = obj.data
    n = len(me.polygons)
    co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co)
    M = np.array(obj.matrix_world)
    V = co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]
    wf = np.zeros(n, np.int32); me.attributes["cs2ts_w"].data.foreach_get("value", wf)
    pa = np.zeros(n, np.int32); me.attributes["part"].data.foreach_get("value", pa)
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    faces = [list(p.vertices) for p in me.polygons]
    nrm = np.array([tuple(M[:3, :3] @ np.array(p.normal)) for p in me.polygons])
    area = np.array([p.area for p in me.polygons])
    h = hashlib.sha1(V.round(6).tobytes())
    for f in faces:
        h.update(np.array(f, np.int64).tobytes())
    h.update(wf.tobytes())
    uv = np.zeros(len(me.loops) * 2); me.uv_layers[0].data.foreach_get("uv", uv)
    h.update(uv.round(6).tobytes())
    names = [table[p - 1] if 1 <= p <= len(table) else "" for p in pa]
    return dict(V=V, faces=faces, wf=wf, names=names, nrm=nrm, area=area, hash=h.hexdigest(),
                tris=sum(len(f) - 2 for f in faces))


def bare():
    """Base bands only (no eave, roof-eave, valley, drip or threshold rule)."""
    return TS.TileWeather(eave_h={"front": 0, "side": 0, "back": 0},
                          roof_eave_m={"front": 0, "side": 0, "back": 0, "top": 0},
                          valleys=False, sill_drips=False, thresholds=False)


def check_grade_bands():
    base = bare()
    zrule = run(base)
    flat = run(dataclasses.replace(base, grade=(0.0, 0.0, 0.0, 1e9)))
    assert zrule["hash"] == flat["hash"], "a zero grade must reproduce the z-0 rule byte for byte"
    ok(f"P4a grade (0, 0, 0, inf) == the z-0 rule byte for byte ({zrule['tris']} tris)")

    cap = -(max(base.base_h.values()) + 0.05)
    grade = (-0.2, 0.0, -0.35, cap)     # 35 % falling toward +Y (the back), capped as lodge.texpass_weather does
    g = run(dataclasses.replace(base, grade=grade))
    zg = lambda P: np.minimum(grade[0] + grade[1] * P[..., 0] + grade[2] * P[..., 1], grade[3])
    hv = g["V"][:, 2] - zg(g["V"])
    n_w, clean_above = 0, 0.0
    for i, f in enumerate(g["faces"]):
        if abs(g["nrm"][i][2]) >= 0.35:             # tops, bottoms, bevels: never a base band
            assert g["wf"][i] != 1, (i, g["names"][i])
            continue
        bh = base.base_h[TS._side_of(g["nrm"][i])]
        lo, hi = hv[f].min(), hv[f].max()
        if g["wf"][i] == 1:
            n_w += 1
            assert hi <= bh + 0.02, f"weathered face {i} ({g['names'][i]}) reaches {hi:.3f} m above grade > {bh}"
            assert not g["names"][i].startswith("wall_a"), f"timber wall face {i} weathered (>= 1.15 m above grade)"
        elif g["names"][i].startswith("plinth_a"):
            assert lo >= bh - 0.02, f"clean plinth face {i} dips to {lo:.3f} m above grade < {bh}"
            clean_above += g["area"][i]
    assert n_w > 0 and clean_above > 1.0, (n_w, clean_above)

    zw = [i for i, nm in enumerate(zrule["names"]) if abs(zrule["nrm"][i][2]) < 0.35]
    assert all(zrule["wf"][i] == 1 for i in zw if zrule["names"][i].startswith("plinth_a")) and \
        any(zrule["wf"][i] == 1 for i in zw if zrule["names"][i].startswith("wall_a")), \
        "the z-0 rule weathers the whole plinth and bands the timber wall (the defect this pass fixes)"
    clean = run(None)
    zs0 = set(np.round(clean["V"][:, 2], 5))
    new = sorted(z for z in set(np.round(g["V"][:, 2], 5)) - zs0)
    top = cap + max(base.base_h.values())
    assert new and new[-1] <= top + 1e-4, f"band cuts top out at z {new[-1] if new else None} (cap line {top:.2f})"
    ok(f"P4b real grade: {n_w} weathered faces all within base_h of the grade, {clean_above:.1f} m2 of clean plinth "
       f"above the band, no timber band (the z-0 rule weathers all of the plinth and bands the timber); band cuts "
       f"top out at z {new[-1]:.2f} <= {top:.2f}")


def check_band_skip():
    base = bare()
    W = dataclasses.replace(base, grade=(-0.2, 0.0, -0.35, -(max(base.base_h.values()) + 0.05)))
    cut = run(W, post=True)
    skip = run(dataclasses.replace(W, band_skip=("col_",)), post=True)
    clean = run(None, post=True)
    post = lambda r: [i for i, nm in enumerate(r["names"]) if nm.startswith("col_a")]
    zs = lambda r: set(np.round(r["V"][sorted({v for i in post(r) for v in r["faces"][i]})][:, 2], 5))
    rest = lambda r: sorted((r["names"][i], int(r["wf"][i])) for i in range(len(r["names"]))
                            if not r["names"][i].startswith("col_a"))
    assert any(cut["wf"][i] == 1 for i in post(cut)) and zs(cut) - zs(clean), "the grade bands an unskipped post"
    assert not any(skip["wf"][i] == 1 for i in post(skip)), "band_skip: a post face took the weathered swatch"
    assert zs(skip) == zs(clean), f"band_skip: the post was cut at z {sorted(zs(skip) - zs(clean))}"
    assert rest(skip) == rest(cut), "band_skip changed a face that is not a post"
    assert skip["tris"] < cut["tris"], (skip["tris"], cut["tris"])
    ok(f"P4c band_skip: the post is banded under the grade ({cut['tris']} tris) and clean, uncut with band_skip "
       f"({skip['tris']} tris); every other face unchanged")


# recipe.texpass drip_skip: the sills each opted recipe skips (lodge.texpass_drip_skip on its real parts)
DRIP_SKIP_SILLS = {"L1": ["sill_LoftN", "sill_LoftS"],
                   "L2": ["sill_aE0", "sill_aE1", "sill_aW0", "sill_aW1"],
                   "L3": ["sill_bSa"],
                   "L4": ["sill_ubf1"]}


def check_drip_skip():
    assert TS.TileWeather().drip_skip == () and all(TS.weather_level(i).drip_skip == () for i in range(5)), \
        "no ladder step may set drip_skip (the flag-off build hangs every strip)"
    W = dataclasses.replace(bare(), sill_drips=True)
    both = run(W, sills=True)
    one = run(dataclasses.replace(W, drip_skip=("sill_b",)), sills=True)

    def strip(r, y0, y1):
        return sorted(tuple(np.round(r["V"][f], 5).ravel()) for i, f in enumerate(r["faces"])
                      if r["wf"][i] == 2 and y0 <= r["V"][f][:, 1].mean() <= y1)
    rest = lambda r: sorted((r["names"][i], int(r["wf"][i]), tuple(np.round(r["V"][f], 5).ravel()))
                            for i, f in enumerate(r["faces"]) if r["wf"][i] != 2)
    sa, sb = strip(both, 1.0, 2.2), strip(both, 5.0, 6.2)
    assert sa and sb, "sill drips on: a strip under each sill"
    assert strip(one, 1.0, 2.2) == sa, "drip_skip moved the strip it does not name"
    assert not strip(one, 5.0, 6.2), "drip_skip: sill_b still hangs a strip"
    assert rest(one) == rest(both), "drip_skip changed a face that is not a strip"
    assert one["tris"] < both["tris"], (one["tris"], both["tris"])
    # lodge: texpass_weather carries the rule; texpass_drip_skip names the sills and raises on a bad prefix
    rc = lodge.select_recipe(lodge.parse_args(["--recipe", "L2"]))
    assert lodge.texpass_weather(TS.TileWeather(), rc, {"drip_skip": ["sill_b"]}).drip_skip == ("sill_b",)
    clear()
    parts = (K.box_part("sill_a", (-0.15, 1.0, 1.5), (0.0, 2.2, 1.6), "timber_heavy")
             + K.box_part("sill_b", (-0.15, 5.0, 1.5), (0.0, 6.2, 1.6), "timber_heavy")
             + K.box_part("wall_a", (0.0, 0.0, 0.0), (4.0, 8.0, 3.0), "timber_lap"))
    assert lodge.texpass_drip_skip(parts, rc, {"drip_skip": ("sill_b",)}) == ["sill_b"]
    assert lodge.texpass_drip_skip(parts, rc, {}) == []
    for pre, why in ((("wall_",), "a prefix that is not a sill_"), (("sill_zz",), "a dead prefix")):
        try:
            lodge.texpass_drip_skip(parts, rc, {"drip_skip": pre})
        except ValueError:
            continue
        raise AssertionError(f"texpass drip_skip with {why} must raise")
    # the recipes that opt in name real sills (flag off reads no rule at all: texpass_rules, P15)
    want = {lodge.select_recipe(lodge.parse_args(["--recipe", k])).key: v for k, v in DRIP_SKIP_SILLS.items()}
    got = {}
    for key, r in sorted(lodge.R.load_recipes().items()):
        tp = lodge.texpass_rules(r, lodge.parse_args(["--recipe", key, "--texpass", "v1"]))
        if tp.get("drip_skip"):
            clear()
            got[r.key] = lodge.texpass_drip_skip(lodge.build_parts(r, {}), r, tp)
    clear()
    assert got == want, got
    # the manifest writer copies only listed result keys: texpass_drip_skip must be one, or the record never lands
    with open(lodge.__file__, encoding="utf-8") as fh:
        src = fh.read()
    mkeys = re.search(r'for k_ in \(([^)]*)\):', src[src.index("def _write_manifest"):]).group(1)
    assert '"texpass_drip_skip"' in mkeys, "_write_manifest drops result['texpass_drip_skip']"
    ok(f"P4d drip_skip: two sills hang a strip each ({both['tris']} tris); drip_skip (\"sill_b\",) keeps sill_a's "
       f"strip and every other face ({one['tris']} tris); no ladder step sets it; a non-sill or dead prefix raises; "
       f"manifest key texpass_drip_skip; opted "
       f"{', '.join(f'{k} {len(v)} sill' + ('' if len(v) == 1 else 's') for k, v in sorted(got.items()))}")


def check_texpass_weather():
    rc = lodge.select_recipe(lodge.parse_args(["--recipe", "L2"]))
    tp = rc.texpass
    assert tp and "grade" in tp, "the hut recipe opts into the pass"
    W = lodge.texpass_weather(TS.TileWeather(), rc, tp)
    s, a = tp["grade"]["slope"], tp["grade"]["anchor_x"]
    assert W.grade == (-s * a, 0.0, -s, -(1.1 + 0.05)), W.grade
    assert all(v == float(tp["eave_h"]) for v in W.eave_h.values()) and W.base_h == TS.TileWeather().base_h
    assert W.band_skip == tuple(tp.get("band_skip", ())), W.band_skip
    assert W.drip_skip == tuple(tp.get("drip_skip", ())), W.drip_skip
    assert lodge.texpass_weather(None, rc, tp) is None
    # the tiled-frame grade == the authoring grade z = -s (a - x), moved through the real orient_for_net
    clear()
    pts = [(x, y, -s * (a - x)) for x in (0.0, 5.0, 11.0) for y in (-3.0, 0.0, 4.0)]
    me = bpy.data.meshes.new("grade_probe")
    me.from_pydata(pts, [], [])
    ob = bpy.data.objects.new("grade_probe", me)
    bpy.context.scene.collection.objects.link(ob)
    cs2_asset.orient_for_net(ob)
    z0, gx, gy, _cap = W.grade
    for v in ob.data.vertices:
        X, Y, Z = v.co
        assert math.isclose(Z, z0 + gx * X + gy * Y, abs_tol=1e-5), (tuple(v.co), z0 + gx * X + gy * Y)
    yawed = dataclasses.replace(rc, yaw_deg=180.0)
    try:
        lodge.texpass_weather(TS.TileWeather(), yawed, tp)
        raise AssertionError("a yawed recipe must raise")
    except ValueError:
        pass
    ok(f"P5 texpass_weather: hut grade {W.grade} == the authoring grade through orient_for_net; eave_h "
       f"{tp['eave_h']} on every side; band_skip {W.band_skip}; drip_skip {W.drip_skip}; yaw raises")


def row_pitch(path, cols=False):
    """The fundamental row period of an image as a fraction of its height (luminance row means, autocorrelation): the
    SHORTEST lag whose peak reaches half the strongest peak. The strongest peak alone is not the pitch: boards that
    alternate in tone, or a bond, put more energy into the 2-course harmonic (PHWeatheredPlankSiding: 0.52 at one
    board, 0.64 at two; the pilot drew its siding at half the intended board width). cols=True: the column period (a
    scan the pass turns a quarter, control "rot": its file columns become the swatch rows)."""
    im = bpy.data.images.load(path, check_existing=False)
    im.colorspace_settings.name = "Non-Color"
    w, h = im.size
    a = np.empty(w * h * 4, np.float32)
    im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    lum = a.reshape(h, w, 4)[..., :3] @ TS.LUMA
    if cols:
        lum = lum.T
        h = w
    prof = lum.mean(1)
    p = prof - prof.mean()
    ac = np.fft.irfft(np.abs(np.fft.rfft(p, 2 * h)) ** 2)[:h]
    lo = int(h * 0.01) + 2
    seg = ac[lo:h // 3]
    peaks = [i for i in range(1, len(seg) - 1) if seg[i] > seg[i - 1] and seg[i] >= seg[i + 1]]
    top = max(seg[i] for i in peaks)
    return (lo + next(i for i in peaks if seg[i] >= 0.5 * top)) / h


def check_scale():
    with open(SOURCES, encoding="utf-8") as fh:
        src = json.load(fh)["sets"]
    rows = []
    for v in TS.TEXPASS:
        for k, s in sorted(TS.TEXPASS[v].items()):
            if k in TS.TEXPASS_SOLID:
                assert k not in TS.TEXPASS_PITCH, f"{v} {k}: a solid key has no pitch range"
                rows.append(f"{k} solid")
                continue
            assert k in TS.TEXPASS_PITCH, f"{v} {k}: no real-world pitch range in cs2_tilesheet.TEXPASS_PITCH"
            lo, hi = TS.TEXPASS_PITCH[k]
            size = float(src[s["set"]]["size_m"])
            p = cs2_bake.find_set_map(os.path.join(cs2_bake.TEXTURE_ROOT, s["set"]), "Color")
            # courses run along the image v (rows); an odd "rot" turns the file's columns into the swatch rows
            world = row_pitch(p, cols=int(s.get("rot", 0)) % 2 == 1) * s["tile_m"][1]
            assert lo <= world <= hi, f"{v} {k}: {s['set']} courses {world * 100:.1f} cm at {s['tile_m'][1]} m " \
                                      f"(scan {size} m), real-world {lo * 100:.0f}-{hi * 100:.0f} cm"
            rows.append(f"{k} {world * 100:.1f} cm (x {s['tile_m'][1] / size:.2f})")
    ok("P6 real-world scale: " + ", ".join(rows))


def check_swatch_controls():
    assert TS.texpass_swatch("deck") == {}, "pass off: no swatch control"
    TS.register_texpass("v1")
    try:
        spec = TS.TEXPASS["v1"]["deck"]
        DECK_CONTROLS = ("chroma", "flatten_m", "contrast", "normal_gain")
        assert all(f in spec for f in DECK_CONTROLS), spec
        tuned = TS._swatch_maps("deck", 480, repeat=2)
        saved = dict(spec)
        try:
            for f in TS.TEXPASS_CONTROLS:       # raw = the bare scan (row_shuffle and any later control too)
                spec.pop(f, None)
            raw = TS._swatch_maps("deck", 480, repeat=2)
        finally:
            spec.clear()
            spec.update(saved)
    finally:
        TS.unregister_texpass()
    srgb = lambda b: np.asarray(K.linear_to_srgb(b.reshape(-1, 3).mean(0)))

    def chroma(b):
        """Spread of the per-texel chromaticity (color / luminance) round its mean: the stains, not the palette
        tint (the target_srgb tint restores the mean's chroma by design)."""
        c = b.reshape(-1, 3) / np.maximum(b.reshape(-1, 3) @ TS.LUMA, 1e-6)[:, None]
        return float(np.abs(c - c.mean(0)).mean())

    def tilt(nrm):
        n = nrm.reshape(-1, 3) * 2.0 - 1.0
        n /= np.linalg.norm(n, axis=1, keepdims=True)
        return float(np.degrees(np.sqrt(np.mean(np.arcsin(np.clip(np.hypot(n[:, 0], n[:, 1]), 0, 1)) ** 2))))
    def lum_cov(b):
        lum = b[:240, :240].reshape(-1, 3) @ TS.LUMA
        return float(lum.std() / lum.mean())
    d = float(np.abs(srgb(tuned[0]) - srgb(raw[0])).max())
    assert d < 0.01, f"the controls moved the deck mean by {d:.4f} sRGB (palette)"
    assert chroma(tuned[0]) < 0.75 * chroma(raw[0]), (chroma(tuned[0]), chroma(raw[0]))
    assert lum_cov(tuned[0]) < 0.75 * lum_cov(raw[0]), (lum_cov(tuned[0]), lum_cov(raw[0]))
    assert tilt(tuned[1]) > 1.15 * tilt(raw[1]), (tilt(tuned[1]), tilt(raw[1]))
    assert tuned[5].get("texpass") == {f: float(saved[f]) for f in TS.TEXPASS_CONTROLS if f in saved}, tuned[5]
    assert "texpass" not in raw[5], raw[5]
    assert TS.texpass_swatch("lux_slate") == {}, "pass off: no alias control"
    ok(f"P7 swatch controls: deck mean held ({d:.4f} sRGB), chroma {chroma(raw[0]):.4f} -> {chroma(tuned[0]):.4f}, "
       f"luminance CoV {lum_cov(raw[0]):.3f} -> {lum_cov(tuned[0]):.3f}, tilt rms {tilt(raw[1]):.1f} -> "
       f"{tilt(tuned[1]):.1f} deg; off = no control")


def swatch_lf(key, lay):
    """(low-frequency luminance CoV, px per meter) of one tile of `key`'s sheet swatch as `lay` cuts it."""
    t = lay["tiles"][key]
    C, r = t["rect_px"][2], t["repeat"]
    base = TS._swatch_maps(key, C, repeat=r, layout_name="rollout")[0]
    n = C // r
    ppm = n / TS.tile_m_of(key)
    return TS.lf_cov(base[:n, :n], ppm), ppm


def check_low_frequency():
    off = layout()
    lf_off = {k: swatch_lf(k, off)[0] for k in sorted(TS.TEXPASS["v1"])}
    rows = []
    TS.register_texpass("v1")
    try:
        on = layout()
        for k in sorted(TS.TEXPASS["v1"]):
            lf, ppm = swatch_lf(k, on)
            lim = max(TS.TEXPASS_LF_RATIO * lf_off[k], TS.TEXPASS_LF_FLOOR)
            assert lf <= lim, (f"{k}: low-frequency CoV {lf:.3f} > {lim:.3f} (pre-pass {lf_off[k]:.3f}): the scan's "
                               f"blotches repeat as a grid every tile (flatten_m)")
            rows.append(f"{k} {lf_off[k]:.3f} -> {lf:.3f}")
        spec = TS.TEXPASS["v1"]["deck"]
        saved = dict(spec)
        try:
            spec.pop("flatten_m")
            spec.pop("row_shuffle", None)
            raw, _ = swatch_lf("deck", on)
        finally:
            spec.clear()
            spec.update(saved)
        lim = max(TS.TEXPASS_LF_RATIO * lf_off["deck"], TS.TEXPASS_LF_FLOOR)
        assert raw > lim, f"mutation: the unflattened deck ({raw:.3f}) must fail the gate ({lim:.3f})"
    finally:
        TS.unregister_texpass()
    ok(f"P10 low frequency (sigma {TS.TEXPASS_LF_SIGMA_M} m): " + ", ".join(rows) +
       f"; the unflattened deck ({raw:.3f}) fails it")


def check_far_sources():
    """The far bake draws what the sheet swatch draws: texpass_far_specs runs the controls on the prefiltered far
    sources (orange stains and flat relief came through raw before)."""
    TS.ensure_materials()
    keys = ["concrete", "deck", "lux_slate", "slate", "timber_lap"]
    specs, _ = K.material_specs(keys, 6.73)
    assert TS.texpass_far_specs(specs) is specs, "pass off: far specs unchanged"
    TS.register_texpass("v1")
    try:
        specs, info = K.material_specs(keys, 6.73)
        far = TS.texpass_far_specs(specs, info)
        assert far["concrete"] == specs["concrete"], "a key outside the pass is untouched"
        maps = {}
        for k in keys[1:]:
            src, dst = specs[k].texture_set, far[k].texture_set
            assert dst != src and os.path.basename(dst) == os.path.basename(src) + "+v1", (k, src, dst)
            assert info[k]["texpass"] == TS.texpass_swatch(k), info[k]
            maps[k] = {}
            for side, d in (("src", src), ("dst", dst)):
                maps[k][side] = {m: TS._load(cs2_bake.find_set_map(d, m))[..., :3] for m in MAPS}
        for k in ("slate", "timber_lap", "lux_slate"):
            # what the far source must show follows the key's swatch controls (v1 gave slate chroma + flatten and
            # timber_lap contrast + row_shuffle): a color control re-derives the tint onto the palette, a pixel move
            # (rot, row_shuffle) changes the map but keeps the mean, no color-side control keeps the map bit for bit
            tps = TS.texpass_swatch(k)
            colctl = any(f in tps for f in TS.TEXPASS_COLOR_CONTROLS)
            moved = colctl or any(f in tps for f in ("rot", "row_shuffle"))
            same = np.array_equal(maps[k]["src"]["Color"], maps[k]["dst"]["Color"])
            assert same != moved, f"{k}: far Color {'unchanged' if same else 'changed'} with controls {sorted(tps)}"
            mat = K.BLDG_MATERIALS[k]
            if not colctl or mat.tint is not None:
                assert far[k].tint == specs[k].tint, (k, far[k].tint, specs[k].tint)
            elif mat.target_srgb is not None:
                got = K.srgb_to_linear(maps[k]["dst"]["Color"]).reshape(-1, 3).mean(0) * np.asarray(far[k].tint)
                d = float(np.abs(np.asarray(K.linear_to_srgb(got)) -
                                 np.asarray(K.linear_to_srgb(np.asarray(TS._lin(mat.target_srgb))))).max())
                assert d < 0.01, f"{k} far mean x tint misses the palette by {d:.4f} sRGB"
            if any(f in tps for f in ("nflatten_m", "normal_gain", "normal_lean")):
                assert not np.array_equal(maps[k]["src"]["NormalGL"], maps[k]["dst"]["NormalGL"]), f"{k}: normal"
        assert TS.texpass_swatch("lux_slate") == TS.texpass_swatch("slate") != {}, \
            "lux_slate draws slate's cell: its far source takes slate's controls (at its own density)"
        dsrc, ddst = maps["deck"]["src"]["Color"], maps["deck"]["dst"]["Color"]
        ppm = dsrc.shape[0] / specs["deck"].metres_per_tile
        lf_src = TS.lf_cov(K.srgb_to_linear(dsrc), ppm)
        lf_dst = TS.lf_cov(K.srgb_to_linear(ddst), ppm)
        assert lf_dst < 0.5 * lf_src, (lf_src, lf_dst)
        tgt = np.asarray(TS._lin(K.BLDG_MATERIALS["deck"].target_srgb))
        got = K.srgb_to_linear(ddst).reshape(-1, 3).mean(0) * np.asarray(far["deck"].tint)
        d = float(np.abs(np.asarray(K.linear_to_srgb(got)) - np.asarray(K.linear_to_srgb(tgt))).max())
        assert d < 0.01, f"deck far mean x tint misses the palette by {d:.4f} sRGB"
    finally:
        TS.unregister_texpass()
    ok(f"P11 far sources: deck LF {lf_src:.3f} -> {lf_dst:.3f} at {ppm:.1f} px/m, mean x tint on the palette "
       f"({d:.4f} sRGB); slate / lux_slate / timber_lap far maps and tints follow their swatch controls; off = unchanged")


def check_normal_lean():
    assert TS.TEXPASS_CONTROLS[-1] == "normal_lean", TS.TEXPASS_CONTROLS
    rows = []
    TS.register_texpass("v1")
    try:
        for k, spec in sorted(TS.TEXPASS["v1"].items()):
            if "normal_lean" not in spec:
                continue
            r = int(spec.get("repeat", 1))
            maps = TS._swatch_maps(k, 480, repeat=r)
            got = TS.normal_lean_deg(maps[1])
            want = tuple(float(x) for x in spec["normal_lean"])
            assert max(abs(g - w) for g, w in zip(got, want)) < 0.5, f"{k}: lean {got} != {want}"
            rows.append(f"{k} ({got[0]:+.1f}, {got[1]:+.1f})")
    finally:
        TS.unregister_texpass()
    ok("P12 normal lean: " + (", ".join(rows) or "no key states one"))


def check_mean_slope():
    lim = TS.TEXPASS_MAX_MEAN_SLOPE_DEG
    rows = []
    TS.register_texpass("v1")
    try:
        on = layout()

        def slope(k):
            t = on["tiles"][k]
            C, r = t["rect_px"][2], t["repeat"]
            n = C // r
            return TS.mean_slope_deg(TS._swatch_maps(k, C, repeat=r, layout_name="rollout")[1][:n, :n])

        def passes(k, su, sv):
            kick = TS.TEXPASS_ROOF_KICK_MAX_DEG.get(k)
            if kick is None:
                return max(abs(su), abs(sv)) < lim
            return abs(su) < lim and -kick <= sv < lim     # a roof kick leans toward +v (negative slope) only
        assert set(TS.TEXPASS_ROOF_KICK_MAX_DEG) <= set(TS.TEXPASS["v1"]), TS.TEXPASS_ROOF_KICK_MAX_DEG
        assert all(0 < v <= 12.0 for v in TS.TEXPASS_ROOF_KICK_MAX_DEG.values()), "a roof kick stays under 12 deg"
        for k in sorted(TS.TEXPASS["v1"]):
            su, sv = slope(k)
            if "set" not in TS.TEXPASS["v1"][k]:
                # detail-only key: it keeps the flag-off scan, so any tilt is the shipped look, not the pass's (recorded)
                rows.append(f"{k} ({su:+.1f}, {sv:+.1f}) flag-off scan, not gated")
                continue
            assert passes(k, su, sv), (f"{k}: mean slope ({su:+.2f}, {sv:+.2f}) deg outside the gate (|u|, |v| < {lim}; "
                                       f"a roof kick: v in [-{TS.TEXPASS_ROOF_KICK_MAX_DEG.get(k)}, {lim})): the "
                                       f"normal map carries a baked tilt (de-tilt it: normal_lean to the de-tilted lean)")
            rows.append(f"{k} ({su:+.1f}, {sv:+.1f})" + (" roof kick" if k in TS.TEXPASS_ROOF_KICK_MAX_DEG else ""))
        spec = TS.TEXPASS["v1"]["slate"]
        saved = dict(spec)
        try:
            spec.pop("normal_lean")
            raw = slope("slate")
            # a mirrored kick (the lean toward -v, down the slope) must fail too
            spec["normal_lean"] = (float(saved["normal_lean"][0]), -abs(float(saved["normal_lean"][1])) - 4.0)
            mirror = slope("slate")
        finally:
            spec.clear()
            spec.update(saved)
        assert not passes("slate", *raw), f"mutation: slate without normal_lean ({raw}) must fail the gate"
        assert not passes("slate", *mirror), f"mutation: slate leaning down the slope ({mirror}) must fail the gate"
    finally:
        TS.unregister_texpass()
    ok(f"P13 no baked tilt (|mean slope| < {lim} deg, roof kick {TS.TEXPASS_ROOF_KICK_MAX_DEG}): " + ", ".join(rows) +
       f"; slate without normal_lean ({raw[0]:+.1f}, {raw[1]:+.1f}) and leaning down the slope ({mirror[0]:+.1f}, "
       f"{mirror[1]:+.1f}) fail it")


def check_level_gate():
    opted = []
    for key, rc in sorted(lodge.R.load_recipes().items()):
        tp = getattr(rc, "texpass", None)
        if tp:
            assert isinstance(tp.get("weather_level"), int), f"{key}: recipe.texpass needs weather_level"
            opted.append(f"{key} {tp['weather_level']}")
    assert opted, "no recipe opts into the pass"
    lodge.assert_texpass_level("probe", {"weather_level": 0}, 0, 5900, 6000)
    lodge.assert_texpass_level("probe", {}, 3, 5900, 6000)
    for lvl in (2, None):
        try:
            lodge.assert_texpass_level("probe", {"weather_level": 0}, lvl, 6051, 6000)
        except RuntimeError:
            continue
        raise AssertionError(f"a pass build at weather level {lvl} under a level-0 recipe must raise")
    ok(f"P8 weather level: {', '.join(opted)} stated; a pass build below it raises, at or above it is quiet")


def check_ridge_key():
    named = {key: rc.texpass["ridge_key"] for key, rc in sorted(lodge.R.load_recipes().items())
             if (getattr(rc, "texpass", None) or {}).get("ridge_key")}
    assert named == {"L1": "metal_roof_lt"}, f"ridge_key is L1's alone (no other building's ridge changes): {named}"
    rc = lodge.R.load_recipes()["L1"]
    clear()
    parts = K.box_part("roof_tA", (0.0, 0.0, 3.0), (4.0, 8.0, 3.2), "metal_roof_lt")
    parts += K.box_part("ridge_t", (1.8, 0.0, 3.2), (2.2, 8.0, 3.3), "galv")
    parts += K.box_part("gutter_t", (-0.2, 0.0, 2.9), (0.0, 8.0, 3.0), "galv")
    same, none = lodge.texpass_ridge(parts, rc, {})
    assert same is parts and none is None, "the pass off (tp {}) must leave the parts as they are"
    out, moved = lodge.texpass_ridge(parts, rc, {"ridge_key": "metal_roof_lt"})
    assert moved == {"ridge_t": ["galv", "metal_roof_lt"]}, moved
    assert [k for _o, k in out] == ["metal_roof_lt", "metal_roof_lt", "galv"], [(o.name, k) for o, k in out]
    dark = [(o, "metal_roof_dk" if o.name.startswith("roof_") else k) for o, k in parts]
    bare = [(o, k) for o, k in parts if not o.name.startswith("ridge_")]
    for tp, pp in (({"ridge_key": "galv"}, parts), ({"ridge_key": "metal_roof_dk"}, parts),
                   ({"ridge_key": "metal_roof_lt"}, dark), ({"ridge_key": "metal_roof_lt"}, bare)):
        try:
            lodge.texpass_ridge(pp, rc, tp)
        except ValueError:
            continue
        raise AssertionError(f"texpass_ridge must raise on {tp} with {[(o.name, k) for o, k in pp]}")
    clear()
    for trim in (False, True):
        off = (TS.flat_detail_prefixes(trim), TS.STRIP_PREFIXES_OWN if trim else ())
        assert lodge.texpass_ridge_prefixes(trim, {}) == off, f"trim {trim}: the off pair moved"
        assert lodge.texpass_ridge_prefixes(trim, {"eave_h": 0.0}) == off, f"trim {trim}: a pass without ridge_key"
        fl, st = lodge.texpass_ridge_prefixes(trim, {"ridge_key": "metal_roof_lt"})
        assert st == () and "ridge_" in fl, f"trim {trim}: a ridge_key draws ridge_ flat ({fl}, {st})"
    TS.register_texpass("v1")
    try:
        lay = layout()
        for k in lodge.TEXPASS_RIDGE_KEYS:      # a ridge follows a coated (painted, metal < 0.5) roof key only
            assert k in lay["tiles"] and (k + TS.F_SUFFIX) in lay["palette"], f"{k}: no tile / flat cell in v1"
            assert float(TS.texpass_flat(k).get("metal", 1.0)) < 0.5, f"{k}: not a coated key under v1"
    finally:
        TS.unregister_texpass()
    ok(f"P14 ridge key: {named} alone; the galv ridge moves, the gutter stays; a non-roof key, a dark key, a roof on "
       f"another key and no ridge raise; the off pair is trim v1's; {list(lodge.TEXPASS_RIDGE_KEYS)} coated in v1")


def check_mat_rule():
    # flag off never reads a rule: lodge reads recipe.texpass in ONE place (texpass_rules), and that is {} when off
    with open(lodge.__file__, encoding="utf-8") as fh:
        src = fh.read()
    reads = len(re.findall(r'getattr\(recipe, "texpass"', src))
    assert reads == 1, f"lodge.py reads recipe.texpass in {reads} places; texpass_rules must be the only one"
    # the manifest writer copies only listed result keys: texpass_mat must be one, or a build's moves never reach it
    keys = re.search(r'for k_ in \(([^)]*)\):', src[src.index("def _write_manifest"):]).group(1)
    assert '"texpass_mat"' in keys, "_write_manifest drops result['texpass_mat']"
    off = lodge.parse_args(["--recipe", "L3"])
    on = lodge.parse_args(["--recipe", "L3", "--texpass", "v1"])
    opted = []
    for key, rc in sorted(lodge.R.load_recipes().items()):
        assert lodge.texpass_rules(rc, off) == {}, f"{key}: texpass_rules read a rule with the flag off"
        if (getattr(rc, "texpass", None) or {}).get("mat"):
            opted.append(key)
    # the rule on probe parts: the longest prefix wins, other parts keep their key and object
    clear()
    rc = lodge.select_recipe(off)
    parts = (K.box_part("balc_ASbal", (0, 0, 0), (1, 0.1, 1), "timber_heavy")
             + K.box_part("balc_AS", (0, 0, -0.2), (1, 1, 0), "deck")
             + K.box_part("col_probe", (2, 0, 0), (2.2, 0.2, 2), "timber_heavy"))
    out, moved = lodge.texpass_mat(parts, rc, {"mat": {"balc_ASbal": "timber_lap", "balc_": "deck"}})
    assert moved == {"balc_ASbal": ["timber_heavy", "timber_lap"]}, moved
    assert [k for _o, k in out] == ["timber_lap", "deck", "timber_heavy"], out
    assert all(a[0] is b[0] for a, b in zip(parts, out))
    assert lodge.texpass_mat(parts, rc, {}) == (parts, {})
    for rules, why in (({"balc_ASbal": "no_such_key"}, "an unknown key"), ({"balcony_": "timber_lap"}, "a dead prefix")):
        try:
            lodge.texpass_mat(parts, rc, {"mat": rules})
        except ValueError:
            continue
        raise AssertionError(f"texpass mat with {why} must raise")
    # real parts, no live user needed: L3's built parts under a synthetic copy of its recipe whose rule names the two
    # balustrades (the registry is untouched); on = exactly those two move timber_heavy -> timber_lap, off = no rule,
    # no move; a prefix one character off the real part name raises (a renamed part never drops a rule silently)
    clear()
    real = list(rc.builder(rc, {}))
    syn_mat = {"balc_AWbal": "timber_lap", "balc_ASbal": "timber_lap"}
    syn = dataclasses.replace(rc, texpass=dict(rc.texpass or {}, mat=syn_mat))
    _o, moved = lodge.texpass_mat(real, syn, lodge.texpass_rules(syn, on))
    want = {"balc_AWbal": ["timber_heavy", "timber_lap"], "balc_ASbal": ["timber_heavy", "timber_lap"]}
    assert moved == want, moved
    assert lodge.texpass_mat(real, syn, lodge.texpass_rules(syn, off))[1] == {}
    try:
        lodge.texpass_mat(real, syn, {"mat": dict(syn_mat, balc_AWbalX="timber_lap")})
    except ValueError:
        pass
    else:
        raise AssertionError("texpass mat with a dead prefix on real parts must raise")
    clear()
    ok(f"P15 mat rule: lodge reads recipe.texpass once (texpass_rules, {{}} with the flag off for every recipe); "
       f"opted {', '.join(opted) or 'none'}; a synthetic L3 rule moves {sorted(moved)} to timber_lap on its real parts "
       f"(manifest key texpass_mat); an unknown key or a dead prefix raises (probe and real parts)")


def far_layout(tp):
    """A stone_rubble plinth, a timber_lap wall and a slate roof slab through lodge's prep, then the far set's UV steps
    (lodge.run: texpass_far_units, align_islands, split_poor_islands, texpass_sample_layer, the dry pack)."""
    clear()
    TS.ensure_materials()
    parts = K.box_part("plinth_a", (0.0, 0.0, -4.0), (4.0, 8.0, 0.0), "stone_rubble")
    parts += K.box_part("wall_a", (0.0, 0.0, 0.0), (4.0, 8.0, 3.0), "timber_lap")
    parts += K.box_part("roof_a", (-0.5, -0.5, 3.0), (4.5, 8.5, 3.3), "slate")
    keys = sorted({k for _o, k in parts})
    cs2_uv.set_density(parts, K.part_weights(parts))
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
    me = obj.data
    mi = np.zeros(len(me.polygons), np.int32)
    me.polygons.foreach_get("material_index", mi)
    start, total = cs2_uv.face_loop_table(me)
    order = np.concatenate([np.arange(s, s + c) for s, c in zip(start, total)])
    loop_mi = np.empty(len(me.loops), np.int32)
    loop_mi[order] = np.repeat(mi, total)
    raw = cs2_uv.loop_uvs(me.uv_layers[cs2_bake.TILE_LAYER])
    idx = lodge.texpass_far_units(obj, keys) if tp else {}
    K.align_islands(obj)
    K.split_poor_islands(obj)
    if idx:
        lodge.texpass_sample_layer(obj, idx)
    tile = cs2_uv.loop_uvs(me.uv_layers[cs2_bake.TILE_LAYER])
    tex = cs2_uv.loop_uvs(me.uv_layers[lodge.TEXPASS_UV]) if idx else None
    me.uv_layers.active = me.uv_layers[cs2_bake.TILE_LAYER]
    with K.calibrated_packer():
        cs2_uv.pack_unique(obj, 1024, [], margin_px=8)
    uniq = cs2_uv.loop_uvs(me.uv_layers[cs2_uv.UNIQUE_LAYER])
    return dict(keys=keys, loop_mi=loop_mi, raw=raw, idx=idx, tile=tile, tex=tex, uniq=uniq)


def check_far_units():
    assert TS.texpass_version() is None and TS.texpass_tile_factors() == {}, "pass off: no tile factor"
    off = far_layout(False)
    TS.register_texpass("v1")
    try:
        fac = TS.texpass_tile_factors()
        assert fac == {"lux_glulam_s2": 2.0, "lux_slate": 2.0, "siding_paint": 2.0, "slate": 2.0,
                       "timber_heavy": 2.0}, fac   # timber_lap: no factor
        on = far_layout(True)
    finally:
        TS.unregister_texpass()
    assert on["keys"] == off["keys"] and np.array_equal(on["loop_mi"], off["loop_mi"])
    f = np.ones(len(on["loop_mi"]))
    for i, s in on["idx"].items():
        f[on["loop_mi"] == i] = s
    assert sorted(on["keys"][i] for i in on["idx"]) == ["slate"], on["idx"]
    assert np.array_equal(on["raw"], off["raw"] * f[:, None]), "the pass's UVTile is not exactly f x the flag-off one"
    assert np.array_equal(on["tile"], off["tile"]), \
        f"far UVTile after the island ops differs from flag-off (max {np.abs(on['tile'] - off['tile']).max()})"
    assert np.array_equal(on["uniq"], off["uniq"]), \
        f"far UVUnique differs from flag-off (max {np.abs(on['uniq'] - off['uniq']).max()})"
    assert np.array_equal(on["tex"], off["tile"] * f[:, None]), "the sampling layer is not the flag-off UVTile x f"
    ok(f"P9 far set: factors {fac}; UVTile x2 exactly on {int((f != 1).sum())} pass loops; island ops and the dry "
       f"pack equal flag-off bit for bit; the sampling layer carries the pass's units")


def check_strip_gate():
    """r4/tex 2026-11-20: audit_strips on the P4d wall (timber_lap z 0..3 over a stone_rubble plinth, sills on the -X
    face at y 1.0..2.2 and 5.0..6.2, bottom z 1.5: strips z 0.8..1.495)."""
    mode, allow = TS.STRIP_AUDIT_MODE, TS.STRIP_ALLOW
    try:
        obj, uvt, keys = scene(sills=True)
        lay = layout()
        TS.tile_mesh(obj, uvt, keys, lay, weather=dataclasses.replace(bare(), sill_drips=True),
                     flat_prefixes=TS.FLAT_DETAIL_PREFIXES)
        plan = [dict(r) for r in TS.LAST_STRIPS["plan"]]
        assert sorted(r["part"] for r in plan if r["rule"] == "sill_drip") == ["sill_a", "sill_b"], plan
        TS.STRIP_ALLOW = ()
        TS.STRIP_AUDIT_MODE = "raise"
        a = TS.audit_strips(obj, keys, lay, "T", texpass="v1")
        assert a["strips"] == 2 and len(a["judged"]) == 2 and not a["hits"], a
        moved = {"sill_a": np.array((0.0, 0.0, -1.1)), "sill_b": np.array((0.0, 2.5, 0.0))}
        bad = [dict(r, quad=r["quad"] + moved[r["part"]]) if r["part"] in moved else r for r in plan]

        def audit(tp="v1"):
            return TS.audit_strips(obj, keys, lay, "T", texpass=tp, plan=bad)

        def raises(tp="v1", what=RuntimeError):
            try:
                audit(tp)
            except what as e:
                return str(e)
            raise AssertionError(f"audit_strips did not raise {what.__name__} (texpass {tp})")
        TS.STRIP_AUDIT_MODE = "report"
        a = audit()
        h = {r["part"]: r for r in a["hits"]}
        assert sorted(h) == ["sill_a", "sill_b"], a
        assert 0.28 <= h["sill_a"]["over_m"] <= 0.36 and h["sill_a"]["air_m"] == 0.0, h["sill_a"]
        assert any(k.startswith("stone_rubble") for k in h["sill_a"]["behind"]), h["sill_a"]
        assert 0.6 <= h["sill_b"]["air_m"] <= 0.7, h["sill_b"]
        TS.STRIP_AUDIT_MODE = "raise"
        msg = raises()
        assert "2 sill-drip strip(s) off their host wall" in msg and "sill_a" in msg and "sill_b" in msg, msg
        TS.STRIP_ALLOW = (("T", "sill_a", "test: an accepted overrun"),)                    # no flag: any pass
        assert "sill_a" not in raises() and "1 sill-drip strip(s)" in raises()
        TS.STRIP_ALLOW = (("T", "sill_", "test: shipped geometry the pass fixes", "off"),)
        a = audit("off")
        assert not a["hits"] and [r["part"] for r in a["allowed"]] == ["sill_a", "sill_b"], a
        assert a["allowed"][0]["reason"] == "test: shipped geometry the pass fixes", a
        assert "2 sill-drip strip(s)" in raises("v1")                # the same strips in a v1 build still raise
        TS.STRIP_ALLOW = (("T", "sill_a", "test: a bad flag", "v9"),)
        assert "STRIP_ALLOW" in raises(what=ValueError)
    finally:
        TS.STRIP_AUDIT_MODE, TS.STRIP_ALLOW = mode, allow
        clear()
    with open(lodge.__file__, encoding="utf-8") as fh:
        src = fh.read()
    call = 'TS.audit_strips(lod0, sorted(keys), layout, recipe.key, texpass=getattr(args, "texpass", "off"))'
    assert call in src, "lodge.py does not run the strip gate with its pass"
    ok(f"P16 strip gate: P4d's two strips pass in RAISE mode; planted, sill_a's runs {h['sill_a']['over_m']} m onto "
       f"the stone plinth and sill_b's hangs {h['sill_b']['air_m']} m in the air past the wall's end: both reported, "
       f"both RAISE; a STRIP_ALLOW line makes one \"allowed\" (flag \"off\": only with the pass off, v1 raises; a "
       f"bad flag fails); lodge.py runs the gate after the ladder")


def main():
    check_provenance()
    check_cut_grid_and_round_trip()
    check_grade_bands()
    check_band_skip()
    check_drip_skip()
    check_texpass_weather()
    check_scale()
    check_swatch_controls()
    check_normal_lean()
    check_mean_slope()
    check_level_gate()
    check_ridge_key()
    check_mat_rule()
    check_far_units()
    check_low_frequency()
    check_far_sources()
    check_strip_gate()
    print(f"PASS test_texpass: {len(CHECKS)} checks")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
