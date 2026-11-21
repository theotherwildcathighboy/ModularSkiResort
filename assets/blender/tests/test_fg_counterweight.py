"""
test_fg_counterweight.py - T277 (1.0.4): the FG drive station's counterweight pot under --texpass v1 (lift_terminal.py
TP1_CW_REKEY / cw_materials_m / cw_detail_specs_m / check_cw_m). Why the part is there: the Yan drive-tension base is
tensioned by gravity; the pot hangs from two cables over the mast-head sheaves and pulls the bullwheel carriage back.
The 1.0.4 review found it read as a lacquered can (it shared paint1 at smoothness 0.58, a smooth cylinder without relief).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_fg_counterweight.py

- CW0 flag off: the drive station's pot and bail are paint1, no part and no table carries cwpaint, detail_specs_m has no
  cwpot entry (flag-off output byte for byte: the build chain proves the maps, this proves the keys).
- CW1 v1: exactly cwpot and cwpot_bail move to cwpaint; every other part keeps its v1 key; the part names, order and
  polygon counts equal the flag-off build (no geometry). The return station carries no cwpaint (its table and manifest
  stay). check_cw_m passes on the shipped constants.
- CW2 the coat: metal <= 0.05, smoothness <= 0.30, slot 1 recolor like paint1 (the frame color is unchanged).
- CW3 the joints and the tone read at the pot's density on the Q export (manifest density.classes.cwpot.px_per_m when
  the Q import is present, else TP1_CW_PPM): the joints are world-height band marks on the pot wall only (none on the
  foot or the lid: DetailSpec "rings" seams striped the lid, critic r7), each >= TEXEL_FLOOR texels wide, a groove
  deeper than TP1_CAVITY from_m; the mottle cell >= TP1_TONE_FLOOR_TEXELS.
- Negatives (each must raise MContractError with its code): a rekey naming a leg (CW1); a satin coat 0.42 rough, a
  metal 0.10 coat, a slot-2 coat (CW2); 0.03 m joints, 2 mm joints, raised joints, a joint on the foot, no joint at
  all, a 0.08 m mottle cell (CW3).
"""
import json
import os
import sys

import bpy  # noqa: F401  (Blender only)

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_bake  # noqa: E402
import cs2_compose  # noqa: E402
import lift_terminal as LT  # noqa: E402

FAILS = []
N_OK = [0]
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir, os.pardir))
Q_MANIFEST = os.path.join(REPO, "assets", "import", "stations", "fixedgrip", "Q", "SkiLiftBaseStationQ",
                          "SkiLiftBaseStationQ.manifest.json")
CW_PARTS = ("cwpot", "cwpot_bail")


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


def station(kind, texpass):
    LT.set_texpass_m(texpass)
    LT.clear_scene()
    parts = LT.build_station_m(kind)
    keys = [(o.name, m, len(o.data.polygons)) for o, m in parts]
    table = LT.materials_for_m(parts)
    specs = LT.detail_specs_m()
    return keys, table, specs


def main():
    try:
        off, off_table, off_specs = station("drive", "off")
        v1, v1_table, v1_specs = station("drive", "v1")
        top, top_table, _ts = station("return", "v1")
        cw_spec = v1_table.get("cwpaint")
    finally:
        LT.set_texpass_m("off")
    offk = {n: m for n, m, _p in off}
    v1k = {n: m for n, m, _p in v1}
    # CW0
    check(all(offk.get(n) == "paint1" for n in CW_PARTS), "CW0 flag off: pot + bail on paint1",
          f"({[offk.get(n) for n in CW_PARTS]})")
    check("cwpaint" not in set(offk.values()) and "cwpaint" not in off_table,
          "CW0 flag off: no part and no table entry carries cwpaint")
    check("cwpot" not in off_specs and "cwpot_bail" not in off_specs, "CW0 flag off: no pot joints in detail_specs_m")
    # CW1
    moved = sorted(n for n in v1k if v1k[n] != offk.get(n))
    check(moved == sorted(CW_PARTS) and all(v1k[n] == "cwpaint" for n in CW_PARTS),
          "CW1 v1: exactly the pot + bail move to cwpaint", f"(moved {moved})")
    check([(n, p) for n, _m, p in off] == [(n, p) for n, _m, p in v1],
          "CW1 v1: part names, order and polygon counts = flag off (no geometry)", f"({len(v1)} parts)")
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
    if FAILS:
        print(f"FAIL test_fg_counterweight: {len(FAILS)} failed: {FAILS}")
        sys.exit(1)
    print(f"PASS test_fg_counterweight: CW0-CW3 + 10 negatives ({N_OK[0]} ok)")


main()
