"""
test_fg_counterweight.py - T277 (1.0.4): the FG drive station's counterweight pot under --texpass v1 (lift_terminal.py
TP1_CW_REKEY / cw_materials_m / cw_detail_specs_m / check_cw_m), and T277b: the see-through closes of the same letter
(sv_web_m / sv_rope_wrap_m / the leg caps / check_sv_m). Why the parts are there: the Yan drive-tension base is tensioned
by gravity; the pot hangs from two ropes over the mast-head sheaves and pulls the bullwheel carriage back. The 1.0.4
review found it read as a lacquered can (it shared paint1 at smoothness 0.58, a smooth cylinder without relief). The
seethru gate found the play camera looking through the open bullwheel, into the open leg tops and through the empty
sheave grooves.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_fg_counterweight.py

Every build here is the shipped Q setup (--rig, wheels v2, fine detail), drive and return.
- CW0 flag off: the drive station's pot and bail are paint1, no part and no table carries cwpaint, detail_specs_m has no
  cwpot entry (flag-off output byte for byte: the build chain proves the maps, this proves the keys).
- CW1 v1: exactly cwpot and cwpot_bail move to cwpaint; every other part keeps its v1 key; the T277 rekey adds no
  geometry (apart from the T277b parts, names, order and polygon counts equal the flag-off build). The return station
  carries no cwpaint. check_cw_m passes on the shipped constants.
- CW2 the coat: metal <= 0.05, smoothness <= 0.30, slot 1 recolor like paint1 (the frame color is unchanged).
- CW3 the joints and the tone read at the pot's density on the Q export (manifest density.classes.cwpot.px_per_m when
  the Q import is present, else TP1_CW_PPM): world-height band marks on the pot wall only (none on the foot or the lid:
  DetailSpec "rings" seams striped the lid, critic r7), each >= TEXEL_FLOOR texels wide, a groove deeper than
  TP1_CAVITY from_m; the mottle cell >= TP1_TONE_FLOOR_TEXELS.
- SV0 flag off: no spoke_web, no rope wrap, the legs open-topped (16 faces).
- SV1 v1 drive: added exactly spoke_web (darksteel, on the Bullwheel bone) and the wraps cwrope_<side>_wh0..2 / _wd
  (rope); the four legs gain their top cap (16 fan faces); the tri delta of the assembled LOD0 = sv_tris_m(); the wraps
  start and end on the straight rope pieces' tangent points (2 mm); check_sv_m passes. The return station v1 has no
  T277b part (the top keeps its letter).
- SV2 the contracts of the v1 drive build pass (M1-M21: M7 declares the web not walkable, M8 the wrap / cable joint),
  also with T279's --comm-arm (the export candidate: M22 too); M15's count (the joined LOD0, the _Win part included)
  stays under TP1_RIG_LOD0_CAP with the arm, and the flag-off build under RIG_LOD0_CAP.
- Negatives (each must raise MContractError with its code): a rekey naming a leg (CW1); a satin coat 0.42 rough, a
  metal 0.10 coat, a slot-2 coat (CW2); 0.03 m joints, 2 mm joints, raised joints, a joint on the foot, no joint at
  all, a 0.08 m mottle cell (CW3); a web whose edge shows inside the rim, a web below the rim (SV1).
"""
import json
import os
import sys

import bpy  # noqa: F401  (Blender only)
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_asset  # noqa: E402
import cs2_bake  # noqa: E402
import cs2_compose  # noqa: E402
import cs2_sheaves as S  # noqa: E402
import lift_terminal as LT  # noqa: E402

FAILS = []
N_OK = [0]
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir, os.pardir))
Q_MANIFEST = os.path.join(REPO, "assets", "import", "stations", "fixedgrip", "Q", "SkiLiftBaseStationQ",
                          "SkiLiftBaseStationQ.manifest.json")
CW_PARTS = ("cwpot", "cwpot_bail")
SV_PARTS = ("spoke_web", "cwrope_n_wh0", "cwrope_n_wh1", "cwrope_n_wh2", "cwrope_n_wd",
            "cwrope_p_wh0", "cwrope_p_wh1", "cwrope_p_wh2", "cwrope_p_wd")
LEGS = ("leg_f_n", "leg_f_p", "leg_b_n", "leg_b_p")


def check(cond, label, detail=""):
    if cond:
        N_OK[0] += 1
        print(f"  ok   {label} {detail}".rstrip())
    else:
        print(f"  FAIL {label} {detail}".rstrip())
        FAILS.append(label)


def expect(code, fn, label):
    try:
        fn()
    except LT.MContractError as e:
        check(str(e).startswith(code), f"{label} -> {code}", f"({str(e)[:120]})")
        return
    check(False, f"{label} -> {code}", "(no error raised)")


def station(kind, texpass, contracts=False, comm=False):
    """The Q setup (tests/test_terminal_fg_rig.py build_rig) with the pass on or off (comm: T279's --comm-arm):
    (keys, table, specs, extra)."""
    LT.M_COMM_ARM = bool(comm)
    S.set_wheels("v2")
    LT.FINE_DETAIL_M = True
    LT.M_NOSE_TRAIN = False
    LT.M_ROPE_R = LT.M_ROPE_R_SHIPPED
    LT.set_m_rig(True, kind)
    LT.set_texpass_m(texpass)
    LT.clear_scene()
    parts = LT.build_station_m(kind)
    keys = [(o.name, m, len(o.data.polygons)) for o, m in parts]
    bpy.context.view_layer.update()     # matrix_world of the freshly placed parts
    verts = {o.name: [tuple(o.matrix_world @ v.co) for v in o.data.vertices] for o, _m in parts
             if o.name.startswith(("cwrope_", "cwcable_", "spoke_web"))}
    table = LT.materials_for_m(parts)
    specs = LT.detail_specs_m()
    extra = {"verts": verts}
    if contracts:
        t, tb, _src = LT.assemble_m(parts, False, kind)
        extra["tris"] = cs2_asset.tri_count(t)
        try:
            extra["contracts"] = LT.contracts_m(t, tb, kind, sweep=False)
        except LT.MContractError as e:
            extra["contracts_error"] = str(e)
        extra["bones"] = LT.rig_part_bones_m(tb, kind)
    return keys, table, specs, extra


def rope_ends(pts):
    """The two end centers of an open rope cylinder (its vertices split by the sign of their projection on the principal
    axis; no vertex order assumed)."""
    a = np.asarray(pts, np.float64)
    c = a.mean(axis=0)
    axis = np.linalg.svd(a - c)[2][0]
    s = (a - c) @ axis
    return [tuple(a[s < 0].mean(axis=0)), tuple(a[s > 0].mean(axis=0))]


def main():
    try:
        off, off_table, off_specs, off_x = station("drive", "off", contracts=True)
        v1, v1_table, v1_specs, v1_x = station("drive", "v1", contracts=True)
        _c, _ct, _cs, cm_x = station("drive", "v1", contracts=True, comm=True)
        top_off, _tt, _ts, _tx = station("return", "off")
        top, top_table, _ts2, _tx2 = station("return", "v1")
        cw_spec = v1_table.get("cwpaint")
    finally:
        LT.set_texpass_m("off")
        LT.set_m_rig(False)
        LT.M_COMM_ARM = False
    offk = {n: m for n, m, _p in off}
    v1k = {n: m for n, m, _p in v1}
    offp = {n: p for n, _m, p in off}
    v1p = {n: p for n, _m, p in v1}
    # CW0
    check(all(offk.get(n) == "paint1" for n in CW_PARTS), "CW0 flag off: pot + bail on paint1",
          f"({[offk.get(n) for n in CW_PARTS]})")
    check("cwpaint" not in set(offk.values()) and "cwpaint" not in off_table,
          "CW0 flag off: no part and no table entry carries cwpaint")
    check("cwpot" not in off_specs and "cwpot_bail" not in off_specs, "CW0 flag off: no pot joints in detail_specs_m")
    # CW1
    moved = sorted(n for n in v1k if n in offk and v1k[n] != offk[n])
    check(moved == sorted(CW_PARTS) and all(v1k[n] == "cwpaint" for n in CW_PARTS),
          "CW1 v1: exactly the pot + bail move to cwpaint", f"(moved {moved})")
    same = [(n, p) for n, _m, p in v1 if n not in SV_PARTS and n not in LEGS]
    check([(n, p) for n, _m, p in off if n not in LEGS] == same,
          "CW1 v1: apart from the T277b parts, names, order and polygon counts = flag off", f"({len(same)} parts)")
    check(v1k.get("cwmast") == "paint1", "CW1 v1: the mast stays paint1 (its bolted seams already break the highlight)")
    check("cwpaint" not in {m for _n, m, _p in top} and "cwpaint" not in top_table,
          "CW1 v1: the return station carries no cwpaint")
    mk = v1_specs["cwpot"].marks if "cwpot" in v1_specs else ()
    check(len(mk) == len(LT.cw_joint_z_m()) == 3 and all(m.kind == "band" for m in mk)
          and v1_specs["cwpot"].seams_m is None and not v1_specs["cwpot_bail"].marks,
          "CW1 v1: three band joints on the pot, no seams, none lent to the bail",
          f"(z {[round(m.centre[2], 3) for m in mk]})")
    try:
        LT.check_cw_m(spec=cw_spec)
        check(True, "CW1 check_cw_m passes on the shipped constants")
    except LT.MContractError as e:
        check(False, "CW1 check_cw_m passes on the shipped constants", f"({e})")
    # CW2
    p1 = v1_table["paint1"]
    check(cw_spec is not None and cw_spec.metal <= 0.05 and 1.0 - cw_spec.rough <= 0.30,
          "CW2 matte coat", f"(metal {cw_spec.metal}, smoothness {1 - cw_spec.rough:.2f}; paint1 {1 - p1.rough:.2f})")
    check(cw_spec.slot == p1.slot == 1 and cw_spec.recolor_albedo and tuple(cw_spec.albedo) == tuple(p1.albedo),
          "CW2 slot 1 recolor with paint1's albedo (frame color unchanged)")
    # CW3
    ppm = LT.TP1_CW_PPM
    if os.path.exists(Q_MANIFEST):
        ppm = float(json.load(open(Q_MANIFEST, encoding="utf-8"))["density"]["classes"]["cwpot"]["px_per_m"])
    J = LT.TP1_CW_JOINT
    w_tex = 2 * J["half_w_m"] * ppm
    check(w_tex >= cs2_compose.TEXEL_FLOOR, "CW3 joints >= TEXEL_FLOOR at the pot's density",
          f"({w_tex:.2f} texels at {ppm} px/m)")
    check(J["depth_m"] > LT.TP1_CAVITY["from_m"] and all(m.h < 0 for m in mk),
          "CW3 joints are grooves past the cavity floor", f"({J['depth_m']} m > {LT.TP1_CAVITY['from_m']} m)")
    pz0, pz1 = LT.m_base()["pot_z"]
    check(all(pz0 + J["half_w_m"] < z < pz1 - J["half_w_m"] for z in LT.cw_joint_z_m()),
          "CW3 no joint on the foot or the lid", f"(pot {pz0}..{pz1} m, joints {list(LT.cw_joint_z_m())})")
    check(LT.TP1_TONE["cwpaint"]["mottle_m"] * ppm >= LT.TP1_TONE_FLOOR_TEXELS, "CW3 mottle cell >= the tone floor",
          f"({LT.TP1_TONE['cwpaint']['mottle_m'] * ppm:.1f} texels)")
    # SV0 / SV1
    check(not any(n in offk for n in SV_PARTS) and all(offp[n] == 16 for n in LEGS),
          "SV0 flag off: no web, no wrap, the legs open-topped (16 faces)", f"(legs {[offp[n] for n in LEGS]})")
    added = sorted(n for n in v1k if n not in offk)
    check(added == sorted(SV_PARTS), "SV1 v1: added exactly the web and the rope wraps", f"({added})")
    check(v1k.get("spoke_web") == "darksteel" and all(v1k[n] == "rope" for n in SV_PARTS[1:]),
          "SV1 v1: the web on darksteel, the wraps on rope")
    check(v1_x["bones"].get("spoke_web") == "Bullwheel", "SV1 v1: the web rides the Bullwheel bone",
          f"({v1_x['bones'].get('spoke_web')})")
    check(all(v1p[n] == 32 for n in LEGS), "SV1 v1: the four legs gain their top cap (16 + 16 fan faces)",
          f"({[v1p[n] for n in LEGS]})")
    check(v1_x["tris"] - off_x["tris"] == LT.sv_tris_m(), "SV1 v1: the LOD0 tri delta = sv_tris_m()",
          f"({off_x['tris']} -> {v1_x['tris']}, sv_tris_m {LT.sv_tris_m()})")
    V = v1_x["verts"]
    for side in ("n", "p"):
        for a, b, what in ((f"cwrope_{side}_1", f"cwrope_{side}_wh0", f"the head wrap starts on cwrope_{side}_1's end"),
                           (f"cwcable_{side}", f"cwrope_{side}_wh2", f"the head wrap ends on cwcable_{side}'s start"),
                           (f"cwrope_{side}_0", f"cwrope_{side}_wd", f"the deflection wrap starts on cwrope_{side}_0's end"),
                           (f"cwrope_{side}_1", f"cwrope_{side}_wd", f"the deflection wrap ends on cwrope_{side}_1's start")):
            d = min(max(abs(p - q) for p, q in zip(ea, eb)) for ea in rope_ends(V[a]) for eb in rope_ends(V[b]))
            check(d <= 2e-3, f"SV1 v1: {what} (2 mm)", f"({d * 1000:.2f} mm)")
    try:
        LT.check_sv_m()
        check(True, "SV1 check_sv_m passes on the shipped constants")
    except LT.MContractError as e:
        check(False, "SV1 check_sv_m passes on the shipped constants", f"({e})")
    check([(n, p) for n, _m, p in top_off] == [(n, p) for n, _m, p in top],
          "SV1 v1: the return station's parts and polygon counts = flag off (the top keeps its letter)")
    # SV2
    check("contracts_error" not in v1_x, "SV2 v1 drive: M1-M21 pass (M7 web not walkable, M8 wrap / cable declared)",
          f"({v1_x.get('contracts_error', 'ok')[:160]})")
    cap = LT.TP1_RIG_LOD0_CAP["drive"]
    check("contracts_error" not in cm_x and "M22" in cm_x.get("contracts", {}),
          "SV2 v1 drive + --comm-arm: M1-M22 pass", f"({cm_x.get('contracts_error', 'ok')[:160]})")
    check(cm_x["tris"] <= cap and off_x["tris"] <= LT.RIG_LOD0_CAP["drive"],
          "SV2 M15: v1 + comm arm <= TP1_RIG_LOD0_CAP, flag off <= RIG_LOD0_CAP",
          f"(v1 {v1_x['tris']}, v1 + arm {cm_x['tris']} vs {cap}; flag off {off_x['tris']} vs {LT.RIG_LOD0_CAP['drive']})")
    # negatives
    MS = cs2_bake.MaterialSpec
    grey = cw_spec.albedo
    J = dict(LT.TP1_CW_JOINT)
    expect("CW1", lambda: LT.check_cw_m(rekey={"cwpot": "cwpaint", "leg_n0": "cwpaint"}), "negative: rekey a leg")
    expect("CW2", lambda: LT.check_cw_m(spec=MS(albedo=grey, metal=0.0, rough=0.42, slot=1, recolor_albedo=True)),
           "negative: satin coat (smoothness 0.58)")
    expect("CW2", lambda: LT.check_cw_m(spec=MS(albedo=grey, metal=0.10, rough=0.75, slot=1, recolor_albedo=True)),
           "negative: metal 0.10")
    expect("CW2", lambda: LT.check_cw_m(spec=MS(albedo=grey, metal=0.0, rough=0.75, slot=2, recolor_albedo=True)),
           "negative: slot 2 (frame color change)")
    expect("CW3", lambda: LT.check_cw_m(joint={**J, "half_w_m": 0.015}), "negative: 0.03 m joints (1.3 texels)")
    expect("CW3", lambda: LT.check_cw_m(joint={**J, "depth_m": 0.002}), "negative: 2 mm joints (no cavity)")
    expect("CW3", lambda: LT.check_cw_m(joint={**J, "depth_m": -0.008}), "negative: raised joints")
    expect("CW3", lambda: LT.check_cw_m(joint={**J, "half_w_m": 0.31}), "negative: a joint on the foot")
    expect("CW3", lambda: LT.check_cw_m(joint={**J, "pitch_m": 1.2}), "negative: no joint")
    expect("CW3", lambda: LT.check_cw_m(tone={**LT.TP1_TONE["cwpaint"], "mottle_m": 0.08}),
           "negative: 0.08 m mottle cell")
    W = dict(LT.TP1_SV_WEB)
    expect("SV1", lambda: LT.check_sv_m(web={**W, "r": 1.50}), "negative: a web whose edge shows inside the rim")
    expect("SV1", lambda: LT.check_sv_m(web={**W, "z_bot": -0.25}), "negative: a web below the rim")
    if FAILS:
        print(f"FAIL test_fg_counterweight: {len(FAILS)} failed: {FAILS}")
        sys.exit(1)
    print(f"PASS test_fg_counterweight: CW0-CW3, SV0-SV2 + 12 negatives ({N_OK[0]} ok)")


main()
