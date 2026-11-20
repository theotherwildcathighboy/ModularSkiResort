"""r4 texture pass v1, tram carriers: the split carriage's atlas-scaled bake margin behind --tram-texpass v1.

    blender --background --factory-startup --python tests/test_tram_carriage_margin.py

M1 flag off: every tram asset bakes with the shipped T["margin"] (the carriers byte for byte).
M2 v1: the split carriage's margin is T["margin"] x split_tex / tex (the cabin's fraction of its atlas); the cabin and the
   one-piece tram keep T["margin"].
M3 the flag is a tram --split option: off by default, refused for the gondola and for the one-piece tram.
No build, no bake: seconds.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import lift_cabin as LC  # noqa: E402


def check(cond, msg):
    if not cond:
        raise AssertionError(msg)


def m1_off():
    T = LC.T
    for role, tex in (("whole", T["tex"]), ("cabin", T["tex"]), ("carriage", T["split_tex"])):
        m = LC.tram_margin(role, tex)
        check(m == T["margin"], f"M1 off: {role} margin {m} != shipped {T['margin']}")
        m = LC.tram_margin(role, tex, "off")
        check(m == T["margin"], f"M1 off (explicit): {role} margin {m} != shipped {T['margin']}")
    return f"M1 flag off: whole / cabin / carriage bake at {T['margin']} px (shipped)"


def m2_v1():
    T = LC.T
    car = LC.tram_margin("carriage", T["split_tex"], "v1")
    cab = LC.tram_margin("cabin", T["tex"], "v1")
    whole = LC.tram_margin("whole", T["tex"], "v1")
    check(cab == T["margin"] and whole == T["margin"], f"M2 v1: cabin {cab} / whole {whole} moved from {T['margin']}")
    check(car == T["margin"] * T["split_tex"] // T["tex"], f"M2 v1: carriage margin {car}")
    check(abs(car / T["split_tex"] - cab / T["tex"]) < 1e-12,
          f"M2 v1: carriage {car}/{T['split_tex']} is not the cabin's fraction {cab}/{T['tex']}")
    check(2 <= car < T["margin"], f"M2 v1: carriage margin {car} out of range")
    return (f"M2 v1: carriage {car} px on {T['split_tex']} (= cabin {cab} px on {T['tex']}, 1/{T['tex'] // cab} of the "
            f"atlas); cabin and one-piece unchanged")


def m3_args():
    a = LC.parse_args(["--kind", "tram", "--split"])
    check(a.tram_texpass == "off", f"M3 default {a.tram_texpass}")
    a = LC.parse_args(["--kind", "tram", "--split", "--tram-texpass", "v1"])
    check(a.tram_texpass == "v1", "M3 v1 not parsed")
    for bad in (["--kind", "gondola", "--split", "--tram-texpass", "v1"], ["--kind", "tram", "--tram-texpass", "v1"]):
        try:
            LC.parse_args(bad)
        except SystemExit:
            continue
        raise AssertionError(f"M3 accepted {bad}")
    return "M3 --tram-texpass: off by default, tram --split only (gondola and one-piece tram refused)"


def main():
    lines = [m1_off(), m2_v1(), m3_args()]
    for ln in lines:
        print("  ok " + ln)
    print(f"PASS test_tram_carriage_margin: {len(lines)} checks")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
