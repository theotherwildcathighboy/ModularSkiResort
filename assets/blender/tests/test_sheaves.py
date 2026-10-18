"""
T62 gate (2026-10-18; build note docs/research/field/2026-10-15/build/T27-rope-path.md's sibling
docs/research/field/2026-10-15/build/T62-sheave-wheels.md): ONE sheave wheel on every tower and station.

Builds the PARTS (no finish) of every tower (lift_tower VARIANTS: FG8-20, ET62 / ET68, DQ10-20, ETDQ, GD12-20, ETGD),
the lattice / tram towers (tram_tower: gondola 25 / 32 / 40, tram 30 / 45 / 60) and every station (FG M drive /
return, DQ base / top, gondola base / top, tram base / top) with cs2_sheaves.WHEELS "v2" and RAISES unless:

  S1  every sheave part (identified by its part-name prefix, WHEEL_PARTS) carries the stamp obj["cs2_wheel"] ==
      cs2_sheaves.WHEEL_ID, i.e. came from cs2_sheaves.wheel, with the family radius (FAMILY_R per asset) - and its
      measured geometry agrees: max radius about the axle = r (1e-4), V groove root r - max(0.02, 0.18 r), the
      triangle count of (segs, ngon) (6 segs fan / 4 segs + 2 (segs - 2) n-gon, discs + liner);
  S2  no stamped part outside WHEEL_PARTS (an unknown wheel) and no FG kit axle cap left (axlecap_ but the T27
      axlecap_stub_ housing); the asset has its expected wheel count;
  S3  wheel(r 0.223, w 0.10, 12) is lite_sheave_parts(r 0.223, w 0.10, 12) vertex for vertex (the DQ20O wheel IS the
      builder: the DQ family stays byte-identical);
  S4  v1 (set_wheels("v1")) builds no stamped part and the pre-T62 per-family wheels (the FG axle caps, the GD flat
      rims, the 8-gon tram rollers): the fallback really is the old code path;
  S6  negatives: the S1 / S2 check RAISES on a v1 FG8 (kit axle caps, unstamped wheels), a v1 GD12 (unstamped flat
      rims) and a v2 FG8 whose one liner lost its stamp (a hand-built wheel slipped in);
  S5  the stations with no sheave train (FG M return, DQ / gondola base: their trains are the hosted ET62 / ETDQ / ETGD
      towers; the tram stations: bull / deflection / drum machinery wheels only) carry no wheel part.

The ingest DQ heads need the gitignored Kagelok .blend; when it is absent DQ10-DQ20 are built with the rebuild head (the
train is the same code) and a line says so.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_sheaves.py
"""
import math
import os
import re
import sys
import time

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_sheaves as S  # noqa: E402
import lift_tower as LTw  # noqa: E402
import lift_terminal as LT  # noqa: E402
import lift_terminal_detach as LTD  # noqa: E402
import tram_tower as TT  # noqa: E402
import tram_station as TS  # noqa: E402


class SheaveGateError(RuntimeError):
    pass


def _fail(code, msg):
    raise SheaveGateError(f"{code} FAILED {msg}")


def _ok(code, msg):
    print(f"[sheaves] ok {code} {msg}")


# Every sheave part, by part-name prefix (towers, station trains, lattice / tram heads): discs and liners.
WHEEL_PARTS = re.compile(
    r"^(?:(?:sheave_hold_|sheave_guide_)?(?:sheave_[np]_\d+(?:_[mp])?_rim|liner_[np]_\d+(?:_[mp])?)"
    r"|sheave_cw(?:head|defl)_[np](?:_liner)?"
    r"|haulroll_(?:liner_)?[np]_\d+"
    r"|sheave_(?:liner_)?[np]_\d+)$")
# family wheel per part (radius from cs2_sheaves.WHEEL_FAMILY: the family radius the gate holds every asset to)
FAM = {k: S.wheel_spec(k) for k in S.WHEEL_FAMILY}


def _family_of(asset, name):
    if name.startswith("sheave_cwhead_"):
        return "CWHEAD"
    if name.startswith("sheave_cwdefl_"):
        return "CWDEFL"
    if name.startswith("haulroll_"):
        return "TRAM"
    return asset["family"]


def _tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def _geom(o):
    me = o.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.asarray(o.matrix_world)
    co = co @ M[:3, :3].T + M[:3, 3]
    return co


def _axle_frame(discs):
    """(centre, axle unit) of a wheel from its discs part: the discs are two planar rings (+ centres); the axle is the
    direction of least extent of the vertex cloud (PCA), the centre the bbox mid."""
    co = _geom(discs)
    c = (co.min(0) + co.max(0)) / 2.0
    u, s, vt = np.linalg.svd(co - co.mean(0), full_matrices=False)
    ax = vt[-1] / np.linalg.norm(vt[-1])
    return c, ax


def _radii(o, c, ax):
    d = _geom(o) - c
    along = d @ ax
    rad = np.linalg.norm(d - np.outer(along, ax), axis=1)
    return rad, along


def check_asset(label, asset, parts):
    names = {o.name: o for o, _m in parts}
    wheel_names = sorted(n for n in names if WHEEL_PARTS.match(n))
    stamped = sorted(n for n, o in names.items() if o.get("cs2_wheel"))
    # S2: unknown wheels, kit axle caps
    unknown = sorted(set(stamped) - set(wheel_names))
    if unknown:
        _fail("S2", f"{label}: stamped wheel parts outside WHEEL_PARTS {unknown[:6]}")
    caps = sorted(n for n in names if re.search(r"(^|_)axlecap_(?!stub_)", n))
    if caps:
        _fail("S2", f"{label}: kit axle caps on a v2 build {caps[:6]} (the DQ20O wheel has none)")
    want = asset["wheels"]
    if len(wheel_names) != 2 * want:
        _fail("S2", f"{label}: {len(wheel_names)} wheel parts, want {2 * want} (discs + liner x {want})")
    # S1: every sheave part from cs2_sheaves.wheel with the family radius, geometry checked
    discs = [n for n in wheel_names if not re.search(r"liner", n)]
    for dn in discs:
        if dn.endswith("_rim"):
            ln = dn[:-len("_rim")].replace("sheave_", "liner_", 1) if not dn.startswith(("sheave_hold_", "sheave_guide_")) \
                else dn[:-len("_rim")].rsplit("sheave_", 1)[0] + "liner_" + dn[:-len("_rim")].rsplit("sheave_", 1)[1]
        elif dn.startswith("sheave_cw"):
            ln = dn + "_liner"
        elif dn.startswith("haulroll_"):
            ln = dn.replace("haulroll_", "haulroll_liner_", 1)
        else:
            ln = dn.replace("sheave_", "sheave_liner_", 1)
        if ln not in names:
            _fail("S1", f"{label}: wheel {dn} has no liner {ln}")
        fam = _family_of(asset, dn)
        W = FAM[fam]
        for n in (dn, ln):
            o = names[n]
            if o.get("cs2_wheel") != S.WHEEL_ID:
                _fail("S1", f"{label}: {n} did not come from cs2_sheaves.wheel (stamp {o.get('cs2_wheel')!r})")
            r = float(o.get("cs2_wheel_r", -1.0))
            if abs(r - W["r"]) > 1e-9 or int(o.get("cs2_wheel_segs", -1)) != W["segs"] \
                    or abs(float(o.get("cs2_wheel_w", -1.0)) - W["w"]) > 1e-9:
                _fail("S1", f"{label}: {n} stamped r {r} w {o.get('cs2_wheel_w')} segs {o.get('cs2_wheel_segs')}, "
                            f"family {fam} wants {W}")
        c, ax = _axle_frame(names[dn])
        rd, _a = _radii(names[dn], c, ax)
        rl, al = _radii(names[ln], c, ax)
        g = max(0.02, 0.18 * W["r"])
        if abs(rd.max() - W["r"]) > 1e-4 or abs(rl.max() - W["r"]) > 1e-4:
            _fail("S1", f"{label}: {dn} radius {rd.max():.5f} / liner {rl.max():.5f}, family {fam} r {W['r']}")
        if abs(rl.min() - (W["r"] - g)) > 1e-4:
            _fail("S1", f"{label}: {ln} groove root {rl.min():.5f}, want r - max(0.02, 0.18 r) = {W['r'] - g:.5f}")
        if abs((al.max() - al.min()) - W["w"]) > 1e-4:
            _fail("S1", f"{label}: {ln} width {al.max() - al.min():.5f}, family {fam} w {W['w']}")
        segs = W["segs"]
        td = 2 * (segs - 2) if W["ngon"] else 2 * segs
        if _tris(names[dn]) != td or _tris(names[ln]) != 4 * segs:
            _fail("S1", f"{label}: {dn} / {ln} tris {_tris(names[dn])} / {_tris(names[ln])}, want {td} / {4 * segs} "
                        f"({segs} segments, {'n-gon' if W['ngon'] else 'fan'} discs)")
    _ok("S1", f"{label}: {len(discs)} wheels from cs2_sheaves.wheel, families "
              f"{sorted({_family_of(asset, d) for d in discs})}")
    return len(discs)


def _clear():
    LT.clear_scene()


def tower_parts(key):
    spec = LTw.VARIANTS[key]
    _clear()
    if spec.family == "FG":
        return LTw.fg_parts(spec)[0]
    head = spec.heads[0]
    if head == "ingest" and not os.path.isfile(LTw.KAGELOK_BLEND):
        print(f"[sheaves] SKIP-INGEST {key}: Kagelok .blend absent, rebuild head (the same train code)")
        head = "rebuild"
    return LTw.dq_parts(spec, head)[0]


def tt_parts(fam, H):
    _clear()
    r = TT.build_parts(TT.make_spec(fam, H))
    return list((r[0] if isinstance(r, tuple) else r).items)


def fgm_parts(kind):
    _clear()
    return LT.build_station_m(kind)


def dq_parts(kind, gondola):
    _clear()
    p = LTD.params_for(kind, gondola, LTD.load_carrier("gd8" if gondola else "dq"))
    return LTD.build_station(p)


def ts_parts(role):
    _clear()
    k, _m = TS.build_parts(role, TS.parse_args(["--role", role]))
    return k.parts


def assets():
    out = []
    for key, spec in LTw.VARIANTS.items():
        out.append((f"tower {key}", {"family": spec.family, "wheels": 2 * spec.sheaves},
                    lambda k=key: tower_parts(k)))
    for H in (25, 32, 40):
        out.append((f"lattice {H}", {"family": "GD", "wheels": 2 * TT.SHEAVES[H]},
                    lambda h=H: tt_parts("gondola", h)))
    for H in (30, 45, 60):
        out.append((f"tram tower {H}", {"family": "TRAM", "wheels": 2 * len(TT.ROLLER["x"])},
                    lambda h=H: tt_parts("tram", h)))
    out.append(("FG M drive", {"family": "FG", "wheels": 2 * LT.M_HOLD["n"] + 4}, lambda: fgm_parts("drive")))
    out.append(("FG M return", {"family": "FG", "wheels": 0}, lambda: fgm_parts("return")))
    out.append(("DQ base", {"family": "DQ", "wheels": 0}, lambda: dq_parts("base", False)))
    out.append(("DQ top", {"family": "DQ", "wheels": 2 * LTD.NOSE_TRAIN_N}, lambda: dq_parts("top", False)))
    out.append(("gondola base", {"family": "GD", "wheels": 0}, lambda: dq_parts("base", True)))
    out.append(("gondola top", {"family": "GD", "wheels": 2 * LTD.NOSE_TRAIN_N}, lambda: dq_parts("top", True)))
    out.append(("tram base", {"family": "TRAM", "wheels": 0}, lambda: ts_parts("base")))
    out.append(("tram top", {"family": "TRAM", "wheels": 0}, lambda: ts_parts("top")))
    return out


def s3_identity():
    _clear()
    a = S.wheel("w_a", "w_a_liner", 0.223, 0.10, (0.3, -2.6, 8.0), segs=12)
    b = S.lite_sheave_parts("l_b", "l_b_liner", 0.223, 0.10, (0.3, -2.6, 8.0), segs=12)
    for (oa, ka), (ob, kb) in zip(a, b):
        ca, cb = _geom(oa), _geom(ob)
        fa = [tuple(p.vertices) for p in oa.data.polygons]
        fb = [tuple(p.vertices) for p in ob.data.polygons]
        ua = [tuple(round(x, 7) for x in l.uv) for l in oa.data.uv_layers["UVMap"].data]
        ub = [tuple(round(x, 7) for x in l.uv) for l in ob.data.uv_layers["UVMap"].data]
        if ka != kb or ca.shape != cb.shape or np.abs(ca - cb).max() > 0.0 or fa != fb or ua != ub:
            _fail("S3", f"wheel() != lite_sheave_parts() for the DQ wheel ({oa.name} vs {ob.name})")
    _ok("S3", "wheel(0.223, 0.10, 12) == lite_sheave_parts vertex / face / UV for vertex (the DQ20O wheel)")


def s4_v1():
    old = S.set_wheels("v1")
    try:
        for label, fn, check in (
                ("tower FG8", lambda: tower_parts("FG8"),
                 lambda nm: sum(1 for n in nm if re.match(r"^axlecap_[np]_", n)) == 8),
                ("tower GD12", lambda: tower_parts("GD12"),
                 lambda nm: True),
                ("tram tower 30", lambda: tt_parts("tram", 30), lambda nm: True)):
            P = fn()
            names = [o.name for o, _ in P]
            if any(o.get("cs2_wheel") for o, _ in P):
                _fail("S4", f"v1 {label}: a stamped wheel part (the v1 path must not call cs2_sheaves.wheel)")
            if not check(names):
                _fail("S4", f"v1 {label}: the pre-T62 wheel parts are missing (FG kit axle caps)")
            rims = [o for o, _ in P if re.match(r"^(sheave_[np]_\d+_rim|haulroll_[np]_\d+)$", o.name)]
            if label == "tower GD12" and any(_tris(o) != 20 for o in rims):
                _fail("S4", f"v1 {label}: rims are not the flat 12-gon (20-tri discs)")
            if label == "tram tower 30" and any(_tris(o) != 12 for o in rims):
                _fail("S4", f"v1 {label}: rollers are not the 8-gon sheave_lite (12-tri discs)")
        _ok("S4", "v1 builds the pre-T62 wheels (FG axle caps, GD flat 12-gon, tram 8-gon) with no stamp")
    finally:
        S.set_wheels(old)


def s6_negatives():
    def expect_raise(label, asset, parts):
        try:
            check_asset(label, asset, parts)
        except SheaveGateError as e:
            _ok("S6", f"{label} raises: {str(e)[:110]}")
            return
        _fail("S6", f"{label}: the gate did not raise")
    old = S.set_wheels("v1")
    try:
        expect_raise("v1 FG8", {"family": "FG", "wheels": 8}, tower_parts("FG8"))
        expect_raise("v1 GD12", {"family": "GD", "wheels": 20}, tower_parts("GD12"))
    finally:
        S.set_wheels(old)
    P = tower_parts("FG8")
    for o, _m in P:
        if o.name == "liner_n_0_m":
            del o["cs2_wheel"]
    expect_raise("v2 FG8, one liner unstamped", {"family": "FG", "wheels": 8}, P)


def main():
    t0 = time.time()
    if S.WHEELS != "v2":
        _fail("S0", f"cs2_sheaves.WHEELS default {S.WHEELS!r}, want v2")
    s3_identity()
    total = 0
    for label, asset, fn in assets():
        P = fn()
        n = check_asset(label, asset, P)
        if asset["wheels"] == 0:
            _ok("S5", f"{label}: no sheave-train wheel part (machinery / hosted tower only)")
        total += n
    s4_v1()
    s6_negatives()
    print(f"PASS test_sheaves: {total} wheels on {len(assets())} assets from cs2_sheaves.wheel ({time.time() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except SheaveGateError as e:
        print(f"FAIL test_sheaves: {e}")
        raise
