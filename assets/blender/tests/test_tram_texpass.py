"""
test_tram_texpass - texture pass v1 on the tram station sheet (`tram_station.py --tiled --texpass v1`,
tram_tilesheet.TEXPASS; docs/PIPELINE.md 1b). Pixels and tables only: no station is built (the builds are the
round's own gates), so the test is a light job.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_tram_texpass.py

Gates (the lodge pilot's P gates, keyed to the station sheet, plus the aspect gate this pass found):
  X1  flag off untouched: the off layout hash is the shipped sheet's (a42b11b6dd083882); register v1 then unregister
      restores every BldgMat, cs2_tilesheet.SMOOTH_TARGET, cs2_tilesheet.TEXPASS and the hash; the v1 hash differs;
      tram_station's --texpass defaults to off and needs --tiled;
  X2  same cut grid: every pass key's period T is an integer multiple of its square tile, no rect of the layout
      moves, the clean and "~w" contents divide by the repeat (so no LOD0 tri moves: the round's probe measures it);
  X3  aspect: every set swatch of the sheet under the pass draws its Color image at the image's own aspect (tile_m
      u / v == pixels w / h). The shipped sheet drew two 2:1 scans on square tiles (stones 2 x too tall): the off
      table still does (recorded, not asserted: flag off is frozen);
  X4  real-world scale: every key of TEXPASS_PITCH shows a course / board pitch in its range on the swatch
      (row-profile autocorrelation of the composed swatch);
  X5  palette: a key with target_srgb lands on it within 0.01 sRGB; a key without one keeps its flag-off mean within
      0.01; the MaskMap A mean of every pass key stays within 0.01 of flag-off;
  X6  low frequency: every pass swatch (clean) keeps its luminance CoV after a periodic 0.2 m Gaussian at or under
      max(1.25 x the flag-off swatch, 0.03) (cs2_tilesheet's P10 rule);
  X7  far sources: texpass_far_fields gives every TEXPASS_FAR key a prefiltered folder with the three maps on the
      SQUARE tile of the swatch, and far mean x tint lies within 0.02 sRGB of the sheet swatch mean (the far set
      draws what LOD0 draws); off = {} and material_table is the shipped table;
  X8  UV turn (tram_tilesheet.TEXPASS_UV_TURN): the timber struts' UVTile turns 90 deg under the pass so the boards
      run up the member: inert when off, a rotation (det +1), the cut grid kept at grid offsets 0 / 0.5, only
      timber parts of the shipped part tables that do not strip.
Exit 1 on the first failure; one PASS line otherwise.
"""

import os
import sys
import traceback

import bpy  # noqa: F401
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import cs2_bake                        # noqa: E402
import cs2_bldg as K                   # noqa: E402
import cs2_tilesheet as TS             # noqa: E402
import tram_station as ts              # noqa: E402
import tram_tilesheet as TT            # noqa: E402

SHIPPED_HASH = "a42b11b6dd083882"
# the flat palette keys of the shipped sheet (SkiTramBaseStationB.manifest.json tiled.sheet.palette): part of the hash
PALETTE = ("clockface", "darksteel", "glass", "label", "lamp_green", "lamp_red", "led", "roof_dark", "rope", "safety",
           "safety_black", "safety_yellow", "trim", "trim_white", "wheel")
MAPS = ("Color", "NormalGL", "Roughness")
V = "v1"
CHECKS = []


def ok(msg):
    CHECKS.append(msg)
    print("  ok", msg)


def layout():
    return TT.sheet_layout("base", list(PALETTE), TT.SHEET_A, 0)


def srgb(lin):
    return np.asarray(K.linear_to_srgb(np.asarray(lin, np.float64)))


def swatch(key, lay, C=None):
    t = lay["tiles"][key]
    C = C or t["rect_px"][2]
    return TT.swatch_maps(key, C, t["repeat"], t["T_m"]), C / t["T_m"]


def image_size(path):
    im = bpy.data.images.load(path, check_existing=False)
    try:
        return tuple(im.size)
    finally:
        bpy.data.images.remove(im)


def row_pitch_m(b, T):
    """Course / board pitch (m) of a composed swatch: the FIRST clear peak of the row-profile autocorrelation. The
    strongest peak can be a 2-board period (PHWeatheredPlankSiding: 0.49 at one board, 0.57 at two), which reads a
    scan as twice its board width."""
    prof = (b @ TS.LUMA).mean(1)
    h = len(prof)
    p = prof - prof.mean()
    ac = np.fft.irfft(np.abs(np.fft.rfft(p, 2 * h)) ** 2)[:h]
    lo = int(h * 0.01) + 2
    top = float(ac[lo:h // 3].max())
    w = max(2, h // 160)
    for i in range(lo, h // 3):
        if ac[i] >= 0.5 * top and ac[i] == ac[max(lo, i - w):i + w + 1].max() and ac[i] > ac[i - 1]:
            return i / h * T
    return (lo + int(np.argmax(ac[lo:h // 3]))) / h * T


def check_flag_off():
    assert TT.texpass_version() is None
    off = layout()
    assert off["hash"] == SHIPPED_HASH, f"off layout hash {off['hash']} != the shipped sheet's {SHIPPED_HASH}"
    mats0 = {k: repr(K.BLDG_MATERIALS[k]) for k in TT.SWATCHES}
    smooth0, passes0 = dict(TS.SMOOTH_TARGET), sorted(TS.TEXPASS)
    TT.register_texpass(V)
    try:
        on = layout()
        assert on["hash"] != off["hash"], "the pass must change the sheet hash"
        assert TT.texpass_version() == V and TS.texpass_version() == "tram-" + V
        moved = [k for k in mats0 if repr(K.BLDG_MATERIALS[k]) != mats0[k]]
        assert sorted(moved) == sorted(TT.TEXPASS[V]), (moved, sorted(TT.TEXPASS[V]))
    finally:
        TT.unregister_texpass()
    assert {k: repr(K.BLDG_MATERIALS[k]) for k in TT.SWATCHES} == mats0, "unregister must restore every BldgMat"
    assert dict(TS.SMOOTH_TARGET) == smooth0 and sorted(TS.TEXPASS) == passes0 and TS.texpass_version() is None
    assert layout()["hash"] == SHIPPED_HASH
    a = ts.parse_args(["--role", "base", "--finish", "--tiled"])
    assert a.texpass == "off"
    try:
        ts.parse_args(["--role", "base", "--finish", "--texpass", V])
        raise AssertionError("--texpass without --tiled must be refused")
    except SystemExit:
        pass
    ok(f"X1 flag off: layout hash {SHIPPED_HASH} (shipped); v1 hash {on['hash']}; register / unregister restores "
       f"{len(mats0)} BldgMat, SMOOTH_TARGET and the pass registry; --texpass defaults to off, needs --tiled")
    return off, on["hash"]


def check_cut_grid(off):
    TT.register_texpass(V)
    try:
        on = layout()
        assert TT.check_layout(on) == {}, TT.check_layout(on)
        rows = []
        for k in sorted(on["tiles"]):
            a, b = off["tiles"][k], on["tiles"][k]
            assert a["rect_px"] == b["rect_px"] and a["T_m"] == b["T_m"] and a["cell"] == b["cell"], (k, a, b)
            bk = TS.base_key(k)
            if bk in TT.TEXPASS[V]:
                tile = TS.tile_m_of(bk)
                assert abs(b["T_m"] / tile - b["repeat"]) < 1e-9 and b["rect_px"][2] % b["repeat"] == 0, (k, b, tile)
                if k == bk:
                    rows.append(f"{k} {tile:g} m x {b['repeat']} = {b['T_m']:g} m (was x {a['repeat']})")
        assert on["palette"] == off["palette"] and on["label_rects"] == off["label_rects"]
    finally:
        TT.unregister_texpass()
    ok("X2 same cut grid: " + ", ".join(rows) + "; no tile, palette or label rect moves")


def _aspect_rows():
    rows, bad = [], []
    for k, d in sorted(TT.SWATCHES.items()):
        m = K.BLDG_MATERIALS[k]
        if not m.set:
            continue
        w, h = image_size(cs2_bake.find_set_map(os.path.join(cs2_bake.TEXTURE_ROOT, m.set), "Color"))
        tu, tv = K.image_tile_m(m)
        okk = abs((tu / tv) / (w / float(h)) - 1.0) < 0.02
        rows.append(f"{k} {m.set} {w}x{h} on {tu:g} x {tv:g} m")
        if not okk:
            bad.append(k)
    return rows, bad


def check_aspect():
    _rows0, bad0 = _aspect_rows()
    TT.register_texpass(V)
    try:
        rows, bad = _aspect_rows()
    finally:
        TT.unregister_texpass()
    assert not bad, f"pass swatches drawn off their image aspect: {bad} ({rows})"
    ok(f"X3 aspect: every set swatch draws its image at the image's aspect under the pass; flag off (frozen) "
       f"stretches {bad0}")


def check_scale_palette_lf(off):
    lf_off, mean_off, sm_off = {}, {}, {}
    for k in sorted(TT.TEXPASS[V]):
        (b, _n, _mt, sm, _c, _i), ppm = swatch(k, off)
        lf_off[k], mean_off[k], sm_off[k] = TS.lf_cov(b, ppm), srgb(b.reshape(-1, 3).mean(0)), float(sm.mean())
    rows_p, rows_c, rows_l = [], [], []
    TT.register_texpass(V)
    try:
        on = layout()
        for k, s in sorted(TT.TEXPASS[V].items()):
            (b, _n, _mt, sm, _c, _i), ppm = swatch(k, on)
            mean = srgb(b.reshape(-1, 3).mean(0))
            want = np.asarray(s["target_srgb"]) if "target_srgb" in s else mean_off[k]
            d = float(np.abs(mean - want).max())
            assert d < 0.01, f"{k}: swatch mean {mean.round(3)} misses {np.asarray(want).round(3)} by {d:.4f} sRGB"
            ds = abs(float(sm.mean()) - sm_off[k])
            assert ds < 0.01, f"{k}: MaskMap A mean moved {sm_off[k]:.3f} -> {float(sm.mean()):.3f}"
            rows_c.append(f"{k} {d:.4f}")
            lf = TS.lf_cov(b, ppm)
            lim = max(TS.TEXPASS_LF_RATIO * lf_off[k], TS.TEXPASS_LF_FLOOR)
            assert lf <= lim, f"{k}: low-frequency CoV {lf:.3f} > {lim:.3f} (flag off {lf_off[k]:.3f})"
            rows_l.append(f"{k} {lf_off[k]:.3f} -> {lf:.3f}")
            if k in TT.TEXPASS_PITCH:
                lo, hi = TT.TEXPASS_PITCH[k]
                p = row_pitch_m(b, on["tiles"][k]["T_m"])
                assert lo <= p <= hi, f"{k}: course pitch {p * 100:.1f} cm outside {lo * 100:.0f}-{hi * 100:.0f} cm"
                rows_p.append(f"{k} {p * 100:.1f} cm")
        missing = [k for k in TT.TEXPASS_PITCH if k not in TT.TEXPASS[V]]
        assert not missing, missing
    finally:
        TT.unregister_texpass()
    ok("X4 real-world scale: " + ", ".join(rows_p))
    ok("X5 palette (max sRGB miss; MaskMap A mean held): " + ", ".join(rows_c))
    ok(f"X6 low frequency (sigma {TS.TEXPASS_LF_SIGMA_M} m): " + ", ".join(rows_l))


def check_far():
    assert TT.texpass_far_fields() == {}, "pass off: no far override"
    t_off = ts.material_table("base", True)
    assert t_off["stone"] == dict(texture_set="Bricks096", metres_per_tile=2.0), t_off["stone"]
    rows = []
    args = ts.parse_args(["--role", "base", "--finish", "--tiled", "--texpass", V])
    ts.apply_texpass(args)
    try:
        on = layout()
        far = TT.texpass_far_fields()
        assert sorted(far) == sorted(TT.TEXPASS_FAR[k] for k in TT.TEXPASS[V]), sorted(far)
        t_on = ts.material_table("base", True)
        for k in sorted(TT.TEXPASS[V]):
            fk = TT.TEXPASS_FAR[k]
            f = far[fk]
            assert t_on[fk]["texture_set"] == f["texture_set"] and t_on[fk]["metres_per_tile"] == f["metres_per_tile"]
            assert abs(f["metres_per_tile"] - TS.tile_m_of(k)) < 1e-9, (k, f, TS.tile_m_of(k))
            col = None
            for m in MAPS:
                p = cs2_bake.find_set_map(f["texture_set"], m)
                assert p and os.path.isfile(p), (k, m, f["texture_set"])
                if m == "Color":
                    col = TS._load(p)[..., :3]
            assert col.shape[0] == col.shape[1], f"{k}: far source {col.shape} is not the stacked square tile"
            got = srgb(K.srgb_to_linear(col).reshape(-1, 3).mean(0) * np.asarray(f["tint"]))
            (b, _n, _mt, _sm, _c, _i), _ppm = swatch(k, on, C=on["tiles"][k]["rect_px"][2] // 4)
            want = srgb(b.reshape(-1, 3).mean(0))
            d = float(np.abs(got - want).max())
            assert d < 0.02, f"{k}: far mean x tint {got.round(3)} vs sheet swatch {want.round(3)} ({d:.4f} sRGB)"
            rows.append(f"{fk} {os.path.basename(f['texture_set'])} @ {f['metres_per_tile']:g} m ({d:.4f})")
    finally:
        ts.apply_texpass(ts.parse_args(["--role", "base", "--finish", "--tiled"]))
    assert ts.TEXPASS is None and TT.texpass_version() is None
    assert ts.material_table("base", True) == t_off, "off again: the shipped material table"
    ok("X7 far sources (far mean x tint vs the sheet swatch, sRGB): " + ", ".join(rows) + "; off = the shipped table")


def check_uv_turn():
    """X8: the per-part UVTile turn (TEXPASS_UV_TURN) is inert when off, is a rotation (the normal map's tangent frame
    turns with the UVs, it is never mirrored), keeps the cut grid at both grid offsets the stations use, and names
    only timber parts of the shipped part tables that do not strip."""
    import json
    assert not TT.uv_turned("tram_timber", "vstrut_La"), "pass off: no UV turn"
    M = TT.turn_uv(np.eye(2))
    assert np.allclose(M, [[0.0, -1.0], [1.0, 0.0]]) and abs(np.linalg.det(M) - 1.0) < 1e-12, M
    rng = np.random.default_rng(7)
    uv = rng.uniform(-30.0, 30.0, (256, 2))
    assert np.allclose(np.linalg.norm(TT.turn_uv(uv), axis=1), np.linalg.norm(uv, axis=1))
    for g in (0.0, 0.5):                         # tile_mesh cuts where uv / repeat + grid_offset is an integer
        lines = np.arange(-8, 9) - g             # the u cut lines (in periods); the turn makes v' = -u
        x = -lines + g
        assert np.abs(x - np.round(x)).max() < 1e-12, f"grid offset {g}: turned u lines leave the cut grid"
    repo = os.path.dirname(os.path.dirname(HERE))
    turned = {}
    TT.register_texpass(V)
    try:
        spec = TT.TEXPASS_UV_TURN[V]
        assert set(spec) <= set(TT.TEXPASS[V]), f"a turned key must be a pass key: {sorted(spec)}"
        for role, name in (("base", "SkiTramBaseStationB"), ("top", "SkiTramTopStationB")):
            p = os.path.join(repo, "assets", "import", "stations", "tram", "B", name, f"{name}.manifest.json")
            with open(p, encoding="utf-8") as fh:
                td = json.load(fh)["tiled"]
            parts = {q for qs in td["routes"].values() for q in qs}
            on_key = {k: set(td["remap"].get(f"{k.split('_', 1)[1]} -> {k}", ())) for k in spec}
            for k, pre in spec.items():
                hit = sorted(q for q in parts if q.startswith(tuple(pre)))
                assert all(TT.uv_turned(k, q) for q in hit)
                assert set(hit) <= on_key[k], f"{name}: turned parts not on {k}: {sorted(set(hit) - on_key[k])}"
                assert not set(hit) & set(td["routes"].get("strip", ())), f"{name}: a turned part strips"
                turned[name] = hit
    finally:
        TT.unregister_texpass()
    assert not TT.uv_turned("tram_timber", "vstrut_La")
    assert len(turned["SkiTramBaseStationB"]) >= 4, turned
    ok(f"X8 UV turn: (u, v) -> (v, -u), det +1, cut grid kept at offsets 0 / 0.5; base {len(turned['SkiTramBaseStationB'])}"
       f" timber struts ({', '.join(turned['SkiTramBaseStationB'])}), top {len(turned['SkiTramTopStationB'])}; off = none")


def main():
    off, _h = check_flag_off()
    check_cut_grid(off)
    check_aspect()
    check_scale_palette_lf(off)
    check_far()
    check_uv_turn()
    print(f"PASS test_tram_texpass: {len(CHECKS)} checks")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
