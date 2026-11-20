"""
test_texpass - texture pass v1 (`lodge.py --texpass v1`, docs/PIPELINE.md "Texture pass", the L2 hut pilot).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_texpass.py

Gates (the pilot's lessons, made mechanical):
  P1  provenance: every set a pass names (alias followers included) has an assets/texture-sources.json entry (CC0, a
      page, an https url and an md5 per map); a set folder that is present matches those md5s byte for byte;
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
  P5  lodge.texpass_weather: the hut recipe's grade in the tiled frame matches the authoring grade through
      cs2_asset.orient_for_net; eave_h overrides every side; band_skip carried; a yawed recipe raises; None stays None;
  P6  real-world scale: every pass key has a TEXPASS_PITCH range and its scan's course / board pitch (Color map row
      autocorrelation at the set's size_m) times tile_m / size_m lies in it (the 0.6 x shake scan failed this);
  P7  swatch controls: deck chroma + flatten_m + contrast keep the swatch mean within 0.01 sRGB (the palette) and cut
      the mean chroma and the luminance CoV, normal_gain raises the tilt rms; texpass_swatch is {} with the pass off
      (alias followers too);
  P8  weather level: every recipe that opts in states texpass weather_level; lodge.assert_texpass_level raises when
      a pass build falls below it (PIPELINE gate 316: never buy the look with weathering) and is quiet otherwise;
  P9  far set in flag-off units: texpass_tile_factors is {slate, lux_slate, timber_lap: 2} (off: {}); a plinth + lap
      wall + slate roof through lodge's prep: the pass's UVTile is exactly 2 x the flag-off one on the pass keys'
      faces (meters / square tile); after lodge.texpass_far_units + align_islands + split_poor_islands the UVTile and
      the dry pack's UVUnique equal the flag-off ones bit for bit, and the sampling layer is the flag-off UVTile x 2
      on the pass faces (the bake draws the scans at their tile on the flag-off layout; split in the pass's units,
      every far FBX was repacked and L1 lost 11 % of its far px/m);
  P10 low frequency: every pass key's sheet swatch keeps its luminance CoV after a periodic 0.2 m Gaussian (the
      blotches that read as a grid when the tile repeats) at or under max(1.25 x the pre-pass swatch, 0.03); the deck
      without flatten_m fails it (v1's first family round: 0.021 -> 0.089, a blotch grid on L3's 15 m deck);
  P11 far sources: texpass_far_specs runs the same controls on the prefiltered far sources (the far bake drew the raw
      scan): the deck's low frequency halves at least and its mean x tint stays on the palette, the normal-only keys
      keep their color bytes and tint, lux_slate takes slate's controls, a key outside the pass and the off state are
      untouched.
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
    ok(f"P1 provenance: every pass set listed (CC0, page, url + md5 per map); {hashed} local maps match their md5")


def check_cut_grid_and_round_trip():
    assert TS.texpass_version() is None, "a pass is already registered"
    off0 = layout()                     # registers the trim / slot / glass keys first: the snapshot holds them
    snap0 = snapshot()
    for v in TS.TEXPASS:
        TS.register_texpass(v)
        try:
            on = layout()
            assert on["hash"] != off0["hash"], "the pass must change the sheet hash"
            for k, t in off0["tiles"].items():
                assert on["tiles"][k]["rect_px"] == t["rect_px"], (k, t["rect_px"], on["tiles"][k]["rect_px"])
            assert set(on["tiles"]) == set(off0["tiles"]) and on["palette"] == off0["palette"]
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


def scene(post=False):
    """(obj, uvtile, keys): a stone_rubble plinth (z -4..0) under a timber_lap box (z 0..3), 4 x 8 m, lodge prep;
    post: plus a free-standing 0.20 m timber_heavy post `col_a` beside the plinth (x -0.6, y 4)."""
    clear()
    TS.ensure_materials()
    parts = K.box_part("plinth_a", (0.0, 0.0, -4.0), (4.0, 8.0, 0.0), "stone_rubble")
    parts += K.box_part("wall_a", (0.0, 0.0, 0.0), (4.0, 8.0, 3.0), "timber_lap")
    if post:
        parts += K.box_part("col_a", (-0.7, 3.9, -4.0), (-0.5, 4.1, 0.0), "timber_heavy")
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


def run(rules, post=False):
    """Mesh facts of the scene through tile_mesh with `rules` (TileWeather or None)."""
    obj, uvt, keys = scene(post)
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


def check_texpass_weather():
    rc = lodge.select_recipe(lodge.parse_args(["--recipe", "L2"]))
    tp = rc.texpass
    assert tp and "grade" in tp, "the hut recipe opts into the pass"
    W = lodge.texpass_weather(TS.TileWeather(), rc, tp)
    s, a = tp["grade"]["slope"], tp["grade"]["anchor_x"]
    assert W.grade == (-s * a, 0.0, -s, -(1.1 + 0.05)), W.grade
    assert all(v == float(tp["eave_h"]) for v in W.eave_h.values()) and W.base_h == TS.TileWeather().base_h
    assert W.band_skip == tuple(tp.get("band_skip", ())), W.band_skip
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
       f"{tp['eave_h']} on every side; band_skip {W.band_skip}; yaw raises")


def row_pitch(path):
    """The dominant row period of an image as a fraction of its height (luminance row means, autocorrelation)."""
    im = bpy.data.images.load(path, check_existing=False)
    im.colorspace_settings.name = "Non-Color"
    w, h = im.size
    a = np.empty(w * h * 4, np.float32)
    im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    prof = (a.reshape(h, w, 4)[..., :3] @ TS.LUMA).mean(1)
    p = prof - prof.mean()
    ac = np.fft.irfft(np.abs(np.fft.rfft(p, 2 * h)) ** 2)[:h]
    lo = int(h * 0.01) + 2
    return (lo + int(np.argmax(ac[lo:h // 3]))) / h


def check_scale():
    with open(SOURCES, encoding="utf-8") as fh:
        src = json.load(fh)["sets"]
    rows = []
    for v in TS.TEXPASS:
        for k, s in sorted(TS.TEXPASS[v].items()):
            assert k in TS.TEXPASS_PITCH, f"{v} {k}: no real-world pitch range in cs2_tilesheet.TEXPASS_PITCH"
            lo, hi = TS.TEXPASS_PITCH[k]
            size = float(src[s["set"]]["size_m"])
            p = cs2_bake.find_set_map(os.path.join(cs2_bake.TEXTURE_ROOT, s["set"]), "Color")
            world = row_pitch(p) * s["tile_m"][1]          # courses run along the image v (rows)
            assert lo <= world <= hi, f"{v} {k}: {s['set']} courses {world * 100:.1f} cm at {s['tile_m'][1]} m " \
                                      f"(scan {size} m), real-world {lo * 100:.0f}-{hi * 100:.0f} cm"
            rows.append(f"{k} {world * 100:.1f} cm (x {s['tile_m'][1] / size:.2f})")
    ok("P6 real-world scale: " + ", ".join(rows))


def check_swatch_controls():
    assert TS.texpass_swatch("deck") == {}, "pass off: no swatch control"
    TS.register_texpass("v1")
    try:
        spec = TS.TEXPASS["v1"]["deck"]
        assert all(f in spec for f in TS.TEXPASS_CONTROLS), spec
        tuned = TS._swatch_maps("deck", 480, repeat=2)
        saved = dict(spec)
        try:
            for f in TS.TEXPASS_CONTROLS:
                spec.pop(f)
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
    assert tuned[5].get("texpass") == {f: float(saved[f]) for f in TS.TEXPASS_CONTROLS}, tuned[5]
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
            assert np.array_equal(maps[k]["src"]["Color"], maps[k]["dst"]["Color"]), f"{k}: no color control"
            assert far[k].tint == specs[k].tint, (k, far[k].tint, specs[k].tint)
            assert not np.array_equal(maps[k]["src"]["NormalGL"], maps[k]["dst"]["NormalGL"]), f"{k}: normal_gain"
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
       f"({d:.4f} sRGB); slate / lux_slate / timber_lap normals gained, colors untouched; off = unchanged")


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
        assert fac == {"lux_slate": 2.0, "slate": 2.0, "timber_lap": 2.0}, fac
        on = far_layout(True)
    finally:
        TS.unregister_texpass()
    assert on["keys"] == off["keys"] and np.array_equal(on["loop_mi"], off["loop_mi"])
    f = np.ones(len(on["loop_mi"]))
    for i, s in on["idx"].items():
        f[on["loop_mi"] == i] = s
    assert sorted(on["keys"][i] for i in on["idx"]) == ["slate", "timber_lap"], on["idx"]
    assert np.array_equal(on["raw"], off["raw"] * f[:, None]), "the pass's UVTile is not exactly f x the flag-off one"
    assert np.array_equal(on["tile"], off["tile"]), \
        f"far UVTile after the island ops differs from flag-off (max {np.abs(on['tile'] - off['tile']).max()})"
    assert np.array_equal(on["uniq"], off["uniq"]), \
        f"far UVUnique differs from flag-off (max {np.abs(on['uniq'] - off['uniq']).max()})"
    assert np.array_equal(on["tex"], off["tile"] * f[:, None]), "the sampling layer is not the flag-off UVTile x f"
    ok(f"P9 far set: factors {fac}; UVTile x2 exactly on {int((f != 1).sum())} pass loops; island ops and the dry "
       f"pack equal flag-off bit for bit; the sampling layer carries the pass's units")


def main():
    check_provenance()
    check_cut_grid_and_round_trip()
    check_grade_bands()
    check_band_skip()
    check_texpass_weather()
    check_scale()
    check_swatch_controls()
    check_level_gate()
    check_far_units()
    check_low_frequency()
    check_far_sources()
    print(f"PASS test_texpass: {len(CHECKS)} checks")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
