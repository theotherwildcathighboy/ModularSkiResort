"""
T62 gate (2026-10-18, build note docs/research/field/2026-10-15/build/T62-sheave-wheels.md, rounds 1-3): ONE
sheave wheel on every tower and station. Round 3 sets these values: 16 segments, the V groove, a hub boss on
every SEEN face, <= 120 tris (cs2_sheaves.WHEEL_PROFILE, WHEEL_TRIS_BUDGET).

The test builds the PARTS (no finish) of these assets with cs2_sheaves.WHEELS "v2":
- every tower (lift_tower VARIANTS: FG8-20, ET62 / ET68, DQ10-20, ETDQ, GD12-20, ETGD)
- the lattice / tram towers (tram_tower: gondola 25 / 32 / 40, tram 30 / 45 / 60)
- every station (FG M drive / return, DQ base / top, gondola base / top, tram base / top)

The test RAISES unless all of these hold:

- S1: Every sheave part (identified by its part-name prefix, WHEEL_PARTS) carries the stamp obj["cs2_wheel"] ==
  cs2_sheaves.WHEEL_ID with the family radius / width and 16 segments. Its measured geometry is THE wheel. The
  rim r (1e-4) sits at a crest ring of 16 vertices per side at |y| = w/2 (segment count, no chamfer). The
  V-groove root has 8 vertices at r - max(0.02, 0.18 r). The raised 8-gon hub boss (top ring 0.10 r proud,
  radius <= 0.45 r) sits on exactly the faces that its stamp cs2_wheel_bosses names. The tris are 76 + 22 per
  boss <= WHEEL_TRIS_BUDGET 120. The sharp ring loops are marked. The material key of the discs part is
  cs2_sheaves.WHEEL_KEY. The liner's key is rubber.
- V1: Every kept boss is SEEN and every culled boss is hidden. The test re-measures cs2_sheaves.boss_visibility
  on the built asset: < / >= BOSS_HIDDEN["share"] of the rays blocked within BOSS_HIDDEN["dist"].
- S2: No stamped part is outside WHEEL_PARTS (an unknown wheel). No FG kit axle cap is left (axlecap_ parts,
  except the T27 axlecap_stub_ housing). The asset has its expected wheel count.
- S3: The wheel itself: wheel() tris == WHEEL_TRIS (120, both bosses) <= the budget, for every family and both
  UV modes. No UV face is mirrored or has zero area. wheel() refuses another segment count.
- S4: v1 (set_wheels("v1")) builds no stamped part. It builds the pre-T62 per-family wheels (the FG axle caps,
  the GD flat rims, the 8-gon tram rollers). So the fallback really is the old code path.
- S5: The stations with no sheave train carry no wheel part. These stations are FG M return and DQ / gondola
  base (their trains are the hosted ET62 / ETDQ / ETGD towers), and the tram stations (bull / deflection / drum
  machinery wheels only).
- S6: Negatives. The S1 / S2 check RAISES on each of these cases:
    * a v1 FG8 (kit axle caps, unstamped wheels)
    * a v1 GD12 (unstamped flat rims)
    * a v2 FG8 whose one liner lost its stamp
    * a v2 FG8 wheel with one hub boss cut off
    * a v2 FG8 with a plate set in front of a kept boss (V1)
- M1: Material. cs2_sheaves.WHEEL_MATERIAL is painted / galvanized steel, not chrome (metal <= 0.35, smoothness
  <= 0.45, albedo sRGB luma 0.45-0.70: the snow-props galvanized-luma lesson). EVERY generator's material table
  resolves WHEEL_KEY to that one spec (lift_tower, lift_terminal M, lift_terminal_detach, tram_tower).
- L1: LOD pattern, one rule for every family. A finished-style mesh carries every family's wheel (r / w), in
  the authoring, net-rotated and chord-tilted frames. The test runs the mesh through
  cs2_asset.make_lod_chain(fixed=cs2_sheaves.lod_fixed()). LOD1 = every wheel exactly WHEEL_LOD[1] (8 crest
  vertices per side, 28 tris, no boss, tri-only, every corner UV inside one of that part's LOD0 UV triangles).
  LOD2 = no wheel face. Every generator's make_lod_chain call passes fixed=...lod_fixed() (static check of the
  four sources).
- N1: Normals survive the export. The test shades a wheel (shade_by_angle 66, weighted, as the generators do),
  exports it with cs2_asset.export_fbx, and re-imports it. Then it checks these points:
    * Cap corners (discs, boss tops) are exactly axial (flat caps, vanilla's hard tread / cap split).
    * The groove-flank corners at the crest are NOT axial (the split exists).
    * The treads are smooth around (the flank corners at one crest vertex within 1 deg).
  [The repo's .Geometry decoder (vlib.py) reads the game's imported files. The FBX re-import is the
  check available before the import.]

The ingest DQ heads need the gitignored Kagelok .blend. When it is absent, the test builds DQ10-DQ20 with the
rebuild head (the train is the same code) and prints a line that says so.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_sheaves.py
"""
import math
import os
import re
import sys
import tempfile
import time

import bmesh
import bpy
import mathutils
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import cs2_asset  # noqa: E402
import cs2_sheaves as S  # noqa: E402
import cs2_validate  # noqa: E402
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
# Family wheel per part (radius from cs2_sheaves.WHEEL_FAMILY). The gate holds every asset to the family
# radius.
FAM = {k: S.wheel_spec(k) for k in S.WHEEL_FAMILY}
TOL = 1e-4


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


def _axle_frame_co(co):
    """Returns (center, axle unit) of a wheel from its discs' vertices. The center is the bbox mid (the discs +
    chamfers + bosses are symmetric about the center). The axle is the direction of least extent (PCA)."""
    c = (co.min(0) + co.max(0)) / 2.0
    _u, _s, vt = np.linalg.svd(co - co.mean(0), full_matrices=False)
    return c, vt[-1] / np.linalg.norm(vt[-1])


def _axle_frame(discs):
    return _axle_frame_co(_geom(discs))


def _radii_co(co, c, ax):
    d = co - c
    along = d @ ax
    rad = np.linalg.norm(d - np.outer(along, ax), axis=1)
    return rad, along


def _radii(o, c, ax):
    return _radii_co(_geom(o), c, ax)


def _n_sharp(o):
    a = o.data.attributes.get("sharp_edge")
    return sum(1 for x in a.data if x.value) if a is not None else 0


def wheel_shape(label, dn, ln, discs, liner, W, kept="-+", hub=None):
    """S1 geometry of one wheel against WHEEL_PROFILE at the family r / w. The arguments are the discs part, the
    liner part, and `hub`. `hub` is the hub_name(discs) part of a culled build, or None when the bosses are
    still in the discs. `kept` = the boss faces it must carry ("-+", "-", "+", "")."""
    P = S.wheel_profile(W["r"], W["w"])
    r, hw, hc, gd, rb, h = P["r"], P["hw"], P["hc"], P["gd"], P["rb"], P["h"]
    cen, ax = _axle_frame(liner)        # the liner (two crest rings + the root ring) is symmetric whatever the discs lost
    if ax[1] < 0.0:
        ax = -ax
    rd, ad = _radii(discs, cen, ax)
    rl, al = _radii(liner, cen, ax)
    if abs(rd.max() - r) > TOL or abs(rl.max() - r) > TOL:
        _fail("S1", f"{label}: {dn} radius {rd.max():.5f} / liner {rl.max():.5f}, family r {r}")
    if abs(rl.min() - (r - gd)) > TOL or int(np.sum(np.abs(rl - (r - gd)) < TOL)) != P["rs"]:
        _fail("S1", f"{label}: {ln} groove root {rl.min():.5f} x {int(np.sum(np.abs(rl - (r - gd)) < TOL))}, want "
                    f"{P['rs']} vertices at r - max(0.02, 0.18 r) = {r - gd:.5f}")
    if abs((al.max() - al.min()) - 2.0 * hc) > TOL or hc != hw:
        _fail("S1", f"{label}: {ln} crest to crest {al.max() - al.min():.5f}, want w = {2 * hw:.5f} (no chamfer)")
    crest = np.abs(rl - r) < TOL
    for sg in (-1.0, 1.0):
        n = int(np.sum(crest & (sg * al > 0)))
        if n != S.WHEEL_SEGS:
            _fail("S1", f"{label}: {ln} {n} crest vertices on the {'+' if sg > 0 else '-'} side, want {S.WHEEL_SEGS} "
                        f"(segment count)")
    if hub is not None and (np.abs(ad) > hw + TOL).any():
        _fail("S1", f"{label}: {dn} still carries boss geometry beside its hub part (the rim bbox would shift)")
    if hub is not None or kept:
        rd, ad = _radii(hub if hub is not None else discs, cen, ax)
    for sg, t in ((-1.0, "-"), (1.0, "+")):
        top = (np.abs(sg * ad - (hw + h)) < TOL)
        proud = sg * ad > hw + TOL
        if t in kept:
            if int(top.sum()) != P["bs"] or rd[top].max() > P["rt"] + TOL or rd[proud].max() > rb + TOL:
                _fail("S1", f"{label}: {dn} hub boss on the {t} face: {int(top.sum())} top-ring vertices {h:.4f} proud "
                            f"(want {P['bs']}, radius <= {P['rt']:.4f})")
        elif proud.any():
            _fail("S1", f"{label}: {dn} geometry proud of the {t} face, whose boss the stamp says was culled")
    td, tl = _tris(discs) + (_tris(hub) if hub is not None else 0), _tris(liner)
    want = S.wheel_tris(len(kept))
    if td + tl != want or tl != 6 * P["rs"] or want > S.WHEEL_TRIS_BUDGET:
        _fail("S1", f"{label}: {dn} / {ln} tris {td} / {tl} = {td + tl}, want {want} ({len(kept)} bosses; liner "
                    f"{6 * P['rs']}) <= the budget {S.WHEEL_TRIS_BUDGET}")
    if _n_sharp(discs) + (_n_sharp(hub) if hub is not None else 0) < 2 * S.WHEEL_SEGS + 2 * P["bs"] * len(kept) \
            or _n_sharp(liner) < 2 * S.WHEEL_SEGS + P["rs"]:
        _fail("S1", f"{label}: {dn} / {ln} sharp ring loops {_n_sharp(discs)} / {_n_sharp(liner)}")
    return td + tl


def _liner_of(dn):
    if dn.endswith("_rim"):
        if dn.startswith(("sheave_hold_", "sheave_guide_")):
            a, b = dn[:-len("_rim")].rsplit("sheave_", 1)
            return a + "liner_" + b
        return dn[:-len("_rim")].replace("sheave_", "liner_", 1)
    if dn.startswith("sheave_cw"):
        return dn + "_liner"
    if dn.startswith("haulroll_"):
        return dn.replace("haulroll_", "haulroll_liner_", 1)
    return dn.replace("sheave_", "sheave_liner_", 1)


def check_asset(label, asset, parts):
    names = {o.name: o for o, _m in parts}
    keys = {o.name: m for o, m in parts}
    wheel_names = sorted(n for n in names if WHEEL_PARTS.match(n))
    stamped = sorted(n for n, o in names.items() if o.get("cs2_wheel"))
    # S2: unknown wheels, kit axle caps
    unknown = sorted(set(stamped) - set(wheel_names))
    if unknown:
        _fail("S2", f"{label}: stamped wheel parts outside WHEEL_PARTS {unknown[:6]}")
    caps = sorted(n for n in names if re.search(r"(^|_)axlecap_(?!stub_)", n))
    if caps:
        _fail("S2", f"{label}: kit axle caps on a v2 build {caps[:6]} (the v2 wheel's hub is its own boss)")
    want = asset["wheels"]
    if len(wheel_names) != 2 * want:
        _fail("S2", f"{label}: {len(wheel_names)} wheel parts, want {2 * want} (discs + liner x {want})")
    # S1: every sheave part from cs2_sheaves.wheel with the family r / w, the profile measured
    discs = [n for n in wheel_names if not re.search(r"liner", n)]
    tris = {}
    for dn in discs:
        ln = _liner_of(dn)
        if ln not in names:
            _fail("S1", f"{label}: wheel {dn} has no liner {ln}")
        fam = _family_of(asset, dn)
        W = FAM[fam]
        for n in (dn, ln):
            o = names[n]
            if o.get("cs2_wheel") != S.WHEEL_ID:
                _fail("S1", f"{label}: {n} did not come from cs2_sheaves.wheel (stamp {o.get('cs2_wheel')!r})")
            r = float(o.get("cs2_wheel_r", -1.0))
            if abs(r - W["r"]) > 1e-9 or int(o.get("cs2_wheel_segs", -1)) != S.WHEEL_SEGS \
                    or abs(float(o.get("cs2_wheel_w", -1.0)) - W["w"]) > 1e-9:
                _fail("S1", f"{label}: {n} stamped r {r} w {o.get('cs2_wheel_w')} segs {o.get('cs2_wheel_segs')}, "
                            f"family {fam} wants {W} at {S.WHEEL_SEGS} segments")
        if keys[dn] != S.WHEEL_KEY or keys[ln] != "rubber":
            _fail("S1", f"{label}: {dn} / {ln} material keys {keys[dn]!r} / {keys[ln]!r}, want {S.WHEEL_KEY!r} / 'rubber'")
        kept = names[dn].get("cs2_wheel_bosses")
        if kept not in ("-+", "-", "+", ""):
            _fail("S1", f"{label}: {dn} has no cs2_wheel_bosses stamp ({kept!r})")
        hub = names.get(S.hub_name(dn))
        if (hub is None) != (kept == "") or (hub is not None and (hub.get("cs2_wheel_hub") != dn
                                                                 or keys[hub.name] != S.WHEEL_KEY)):
            _fail("S1", f"{label}: {dn} kept bosses {kept!r} but hub part {hub.name if hub else None} "
                        f"({hub.get('cs2_wheel_hub') if hub else None}, {keys.get(hub.name) if hub else None})")
        tris[dn] = wheel_shape(label, dn, ln, names[dn], names[ln], W, kept, hub)
    # V1: the bosses kept are the seen ones (re-measured on the built asset)
    vis = S.boss_visibility(parts)
    for dn in discs:
        kept = names[dn]["cs2_wheel_bosses"]
        for sg, t in ((-1, "-"), (1, "+")):
            hid = vis[dn][sg] >= S.BOSS_HIDDEN["share"]
            if (t in kept) == hid:
                _fail("V1", f"{label}: {dn} {t} face: boss {'kept' if t in kept else 'culled'} but {vis[dn][sg]:.0%} of "
                            f"its rays are blocked within {S.BOSS_HIDDEN['dist']} m")
    n_b = {k: sum(1 for d in discs if names[d]["cs2_wheel_bosses"] == k) for k in ("-+", "-", "+", "")}
    _ok("S1", f"{label}: {len(discs)} wheels = THE wheel (16 seg, groove, bosses on seen faces: {n_b['-+']} both / "
              f"{n_b['-'] + n_b['+']} one / {n_b['']} none), {sum(tris.values())} tris "
              f"({max(tris.values()) if tris else 0} max / wheel <= {S.WHEEL_TRIS_BUDGET}), {S.WHEEL_KEY}; V1 seen = kept")
    return len(discs), sum(tris.values()), n_b


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


def _uv_areas(o):
    me = o.data
    uvd = me.uv_layers["UVMap"].data
    out = []
    for p in me.polygons:
        pts = [uvd[i].uv for i in p.loop_indices]
        out.append(0.5 * sum(pts[i - 1].x * pts[i].y - pts[i].x * pts[i - 1].y for i in range(len(pts))))
    return out


def s3_wheel():
    _clear()
    for fam, W in sorted(FAM.items()):
        for uv in ("box", "developed"):
            (d, kd), (l, kl) = S.wheel(f"t_{fam}_{uv}", f"t_{fam}_{uv}_liner", W["r"], W["w"], (0.3, -2.6, 8.0), uv=uv)
            if _tris(d) + _tris(l) != S.WHEEL_TRIS or S.WHEEL_TRIS > S.WHEEL_TRIS_BUDGET:
                _fail("S3", f"{fam} {uv}: {_tris(d) + _tris(l)} tris != WHEEL_TRIS {S.WHEEL_TRIS} or over the budget "
                            f"{S.WHEEL_TRIS_BUDGET}")
            bad = [a for o in (d, l) for a in _uv_areas(o) if a <= 0.0]
            if bad:
                _fail("S3", f"{fam} {uv}: {len(bad)} mirrored / zero-area UV faces (a mirrored tangent frame)")
            wheel_shape(f"S3 {fam} {uv}", d.name, l.name, d, l, W)
    try:
        S.wheel("t_bad", "t_bad_liner", 0.2, 0.1, (0, 0, 0), segs=12)
    except ValueError:
        pass
    else:
        _fail("S3", "wheel(segs=12) did not raise (one wheel, 16 segments)")
    _ok("S3", f"wheel() = {S.WHEEL_TRIS} tris (both bosses) <= {S.WHEEL_TRIS_BUDGET} on every family (box + developed "
              f"UVs, no mirrored UV face); "
              f"segs != {S.WHEEL_SEGS} raises")


def m1_material():
    import cs2_bake
    M = S.WHEEL_MATERIAL
    lum = 0.2126 * M["albedo"][0] + 0.7152 * M["albedo"][1] + 0.0722 * M["albedo"][2]
    srgb = 1.055 * lum ** (1 / 2.4) - 0.055
    smooth = 1.0 - M["rough"]
    if not (M["metal"] <= 0.35 and smooth <= 0.45 and 0.45 <= srgb <= 0.70):
        _fail("M1", f"WHEEL_MATERIAL metal {M['metal']} / smoothness {smooth:.2f} / sRGB luma {srgb:.2f}: want <= 0.35 / "
                    f"<= 0.45 / 0.45-0.70 (painted / galvanised, not chrome)")
    want = cs2_bake.MaterialSpec(**M)
    tables = {}
    tables["lift_tower"] = {**LTw.TOWER_MATERIALS, **S.wheel_materials(dict)}
    _clear()
    tables["lift_terminal M"] = LT.materials_for_m(LT.build_station_m("drive"))
    for g in (False, True):
        _clear()
        p = LTD.params_for("top", g, LTD.load_carrier("gd8" if g else "dq"))
        tables[f"lift_terminal_detach {'gondola' if g else 'dq'} top"] = LTD.materials_for(p, LTD.build_station(p))
    for fam in ("gondola", "tram"):
        tables[f"tram_tower {fam}"] = TT.material_specs(False, os.path.join(HERE, "..", "textures"), fam)
    for k, t in tables.items():
        got = t.get(S.WHEEL_KEY)
        got = cs2_bake.MaterialSpec(**got) if isinstance(got, dict) else got
        if got != want:
            _fail("M1", f"{k}: {S.WHEEL_KEY} = {got}, want {want}")
    _ok("M1", f"WHEEL_MATERIAL metal {M['metal']}, smoothness {smooth:.2f}, sRGB luma {srgb:.2f}; one spec in "
              f"{len(tables)} generator tables")


def _wheel_set(frames):
    """Every family's wheel in each frame (name, liner, parts). Returns parts + [(discs, liner, family)]."""
    parts, wheels = [], []
    k = 0
    for fr, M in frames:
        for fam, W in sorted(FAM.items()):
            loc = (1.2 * k, 0.0, 3.0)
            dn, ln = f"sheave_n_{k}_rim", f"liner_n_{k}"
            P = S.wheel(dn, ln, W["r"], W["w"], loc, uv="box")
            for o, _key in P:
                T = mathutils.Matrix.Translation(loc) @ M @ mathutils.Matrix.Translation(tuple(-x for x in loc))
                o.data.transform(T)
                o.data.update()
            parts += P
            wheels.append((P[0][0].name, P[1][0].name, fam, fr))
            k += 1
    return parts, wheels


def _in_any_tri(q, T, eps=1e-6):
    a, b, c = T[:, 0], T[:, 1], T[:, 2]

    def cr(u, v, w):
        return (v[:, 0] - u[:, 0]) * (w[:, 1] - u[:, 1]) - (v[:, 1] - u[:, 1]) * (w[:, 0] - u[:, 0])
    qq = np.broadcast_to(q, a.shape)
    d1, d2, d3 = cr(a, b, qq), cr(b, c, qq), cr(c, a, qq)
    inside = ((d1 >= -eps) & (d2 >= -eps) & (d3 >= -eps)) | ((d1 <= eps) & (d2 <= eps) & (d3 <= eps))
    return bool(inside.any())


def l1_lod():
    _clear()
    S.WHEEL_LOG.clear()
    frames = (("authoring", mathutils.Matrix.Identity(4)),
              ("net", mathutils.Matrix.Rotation(math.radians(90.0), 4, "Z")),
              ("chord", mathutils.Matrix.Rotation(math.radians(-17.0), 4, "Y")))
    parts, wheels = _wheel_set(frames)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=2.0)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=10, use_grid_fill=True)
    me = bpy.data.meshes.new("filler")
    bm.to_mesh(me)
    bm.free()
    fill = bpy.data.objects.new("filler", me)
    bpy.context.collection.objects.link(fill)
    fill.data.uv_layers.new(name="UVMap")
    parts.append((fill, "paint"))
    table = cs2_asset.tag_parts(parts)
    uv0 = {}
    for o, _k in parts:
        uvd = o.data.uv_layers["UVMap"].data
        tri = []
        for p in o.data.polygons:
            li = list(p.loop_indices)
            for k in range(1, len(li) - 1):
                tri.append([tuple(uvd[li[0]].uv), tuple(uvd[li[k]].uv), tuple(uvd[li[k + 1]].uv)])
        uv0[o.name] = np.array(tri)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _k in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    t = bpy.context.view_layer.objects.active
    bm = bmesh.new()
    bm.from_mesh(t.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)
    bm.to_mesh(t.data)
    bm.free()
    cs2_asset.store_part_table(t, table)
    cs2_asset.shade_by_angle(t, 66.0, weighted=True)
    fx = S.lod_fixed()
    lods = cs2_asset.make_lod_chain(t, "l1probe", targets=(400, 200), delete_prefixes=((), ()), fixed=fx)
    idx = {n: i for i, n in enumerate(table, start=1)}
    for level, lod in enumerate(lods, start=1):
        me = lod.data
        if not all(len(p.vertices) == 3 for p in me.polygons):
            _fail("L1", f"LOD{level} not tri-only")
        part = [a.value for a in me.attributes["part"].data]
        co = np.array([tuple(v.co) for v in me.vertices])
        uvd = me.uv_layers["UVMap"].data
        for dn, ln, fam, fr in wheels:
            ids = (idx[dn], idx[ln])
            polys = [p for p in me.polygons if part[p.index] in ids]
            if level == 2:
                if polys:
                    _fail("L1", f"LOD2 keeps {len(polys)} faces of the {fam} wheel ({fr}): LOD2 = no wheel")
                continue
            if len(polys) != S.WHEEL_LOD[1]["tris"]:
                _fail("L1", f"LOD1 {fam} wheel ({fr}): {len(polys)} tris, want {S.WHEEL_LOD[1]['tris']}")
            vi = sorted({v for p in polys for v in p.vertices})
            W = FAM[fam]
            P = S.wheel_profile(W["r"], W["w"])
            dco = co[sorted({v for p in polys if part[p.index] == ids[0] for v in p.vertices})]
            c, ax = _axle_frame_co(co[vi])
            rad, al = _radii_co(co[vi], c, ax)
            n_crest = int(np.sum(np.abs(rad - P["r"]) < TOL))
            if n_crest != 2 * S.WHEEL_LOD[1]["segs"] or np.abs(al).max() > P["hc"] + TOL:
                _fail("L1", f"LOD1 {fam} wheel ({fr}): {n_crest} crest vertices, |axial| max {np.abs(al).max():.4f}: "
                            f"want {2 * S.WHEEL_LOD[1]['segs']} on the crest rings, no boss")
            del dco
            for p in polys:
                T = uv0[table[part[p.index] - 1]]
                for li in p.loop_indices:
                    if not _in_any_tri(np.array(tuple(uvd[li].uv)), T):
                        _fail("L1", f"LOD1 {fam} wheel ({fr}): a corner UV {tuple(uvd[li].uv)} outside every LOD0 UV "
                                    f"triangle of {table[part[p.index] - 1]}")
    for lod in lods:
        m = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(m)
    # every generator's chain takes the hook
    for mod in ("lift_tower", "lift_terminal", "lift_terminal_detach", "tram_tower"):
        src = open(os.path.join(HERE, mod + ".py"), encoding="utf-8").read()
        calls = [m.start() for m in re.finditer(r"cs2_asset\.make_lod_chain\(", src)]
        tail = [src[i:src.index(")\n", i) + 1] for i in calls]
        v2_calls = [s for s in tail if "lod_fixed()" in s]
        if not v2_calls:
            _fail("L1", f"{mod}: no make_lod_chain call passes fixed=...lod_fixed() (the wheels would be collapse-decimated)")
    _ok("L1", f"LOD1 = {S.WHEEL_LOD[1]['form']} ({S.WHEEL_LOD[1]['tris']} tris) and LOD2 = no wheel on "
              f"{len(wheels)} wheels ({len(FAM)} families x {len(frames)} frames), UVs inside LOD0 UV triangles; "
              f"lift_tower / lift_terminal / lift_terminal_detach / tram_tower chains take lod_fixed()")


def n1_normals():
    _clear()
    S.WHEEL_LOG.clear()
    W = FAM["DQ"]
    P = S.wheel("sheave_n_0_rim", "liner_n_0", W["r"], W["w"], (0.0, 0.0, 1.0))
    table = cs2_asset.tag_parts(P)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _k in P:
        o.select_set(True)
    bpy.context.view_layer.objects.active = P[0][0]
    bpy.ops.object.join()
    t = bpy.context.view_layer.objects.active
    bm = bmesh.new()
    bm.from_mesh(t.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)
    bm.to_mesh(t.data)
    bm.free()
    cs2_asset.store_part_table(t, table)
    cs2_asset.shade_by_angle(t, 66.0, weighted=True)
    t.name = "SkiT62WheelProbe"
    d = tempfile.mkdtemp(prefix="t62n1_")
    path = os.path.join(d, "SkiT62WheelProbe.fbx")
    cs2_asset.export_fbx(t, path)
    objs = cs2_validate.import_fbx_temp(path)
    o = [x for x in objs if x.type == "MESH"][0]
    me = o.data
    co = np.array([tuple(o.matrix_world @ v.co) for v in me.vertices])
    c, ax = _axle_frame_co(co)
    rad, al = _radii_co(co, c, ax)
    Pr = S.wheel_profile(W["r"], W["w"])
    sc = rad.max() / Pr["r"]                      # the export scale (cm), whatever the import does with it
    R3 = np.asarray(o.matrix_world.to_3x3().inverted().transposed())
    cn = me.corner_normals
    caps, tread, flank = [], [], {}
    for p in me.polygons:
        fn = np.asarray(R3 @ np.asarray(p.normal))
        fn = fn / np.linalg.norm(fn)
        for li, vi in zip(p.loop_indices, p.vertices):
            n = np.asarray(R3 @ np.asarray(cn[li].vector))
            n = n / np.linalg.norm(n)
            if abs(fn @ ax) > 0.999:
                caps.append(abs(n @ ax))
            elif abs(rad[vi] - Pr["r"] * sc) < 1e-3 * sc:          # a groove flank corner on a crest vertex
                tread.append(abs(n @ ax))
                flank.setdefault(vi, []).append(n)
    cs2_validate.remove_objects(objs)
    if not caps or min(caps) < 0.9999:
        _fail("N1", f"cap corner normals not axial after the FBX round trip (min |n.axle| {min(caps) if caps else None})")
    if not tread or max(tread) > 0.95:
        _fail("N1", f"flank corners at the crest axial ({max(tread) if tread else None}): no hard tread / cap split")
    worst = 0.0
    for vi, ns in flank.items():
        for a in ns:
            for b in ns:
                worst = max(worst, math.degrees(math.acos(max(-1.0, min(1.0, float(a @ b))))))
    if not flank or worst > 1.0:
        _fail("N1", f"tread corners at a crest vertex differ by {worst:.2f} deg (want smooth around, <= 1 deg)")
    _ok("N1", f"FBX round trip: {len(caps)} cap corners axial (min {min(caps):.5f}), {len(tread)} groove-flank corners "
              f"at the crests split from the cap (max |n.axle| {max(tread):.3f}), treads smooth around (worst {worst:.2f} deg)")


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
            if any(k == S.WHEEL_KEY for _o, k in P):
                _fail("S4", f"v1 {label}: a part on the v2 wheel material")
            if not check(names):
                _fail("S4", f"v1 {label}: the pre-T62 wheel parts are missing (FG kit axle caps)")
            rims = [o for o, _ in P if re.match(r"^(sheave_[np]_\d+_rim|haulroll_[np]_\d+)$", o.name)]
            if label == "tower GD12" and any(_tris(o) != 20 for o in rims):
                _fail("S4", f"v1 {label}: rims are not the flat 12-gon (20-tri discs)")
            if label == "tram tower 30" and any(_tris(o) != 12 for o in rims):
                _fail("S4", f"v1 {label}: rollers are not the 8-gon sheave_lite (12-tri discs)")
        if S.lod_fixed() is not None or S.wheel_materials(dict):
            _fail("S4", "v1: lod_fixed() / wheel_materials() not empty (a v1 chain / table must stay as it was)")
        _ok("S4", "v1 builds the pre-T62 wheels (FG axle caps, GD flat 12-gon, tram 8-gon) with no stamp, no wheel "
                  "material, no LOD hook")
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
    P = tower_parts("FG8")
    for o, _m in P:
        if o.name == S.hub_name("sheave_n_0_m_rim"):
            bm = bmesh.new()
            bm.from_mesh(o.data)
            hw = S.wheel_profile(S.FG_SHEAVE["r"], S.FG_SHEAVE["w"])["hw"]
            cy = sum(v.co.y for v in bm.verts) / len(bm.verts)
            kill = [v for v in bm.verts if v.co.y - cy > hw + 1e-4]
            bmesh.ops.delete(bm, geom=kill, context="VERTS")
            bm.to_mesh(o.data)
            bm.free()
    expect_raise("v2 FG8, one hub boss cut off", {"family": "FG", "wheels": 8}, P)
    P = tower_parts("FG8")
    d = [o for o, _m in P if o.name == "sheave_n_0_m_rim"][0]
    co = _geom(d)
    c = (co.min(0) + co.max(0)) / 2.0
    hw = S.wheel_profile(S.FG_SHEAVE["r"], S.FG_SHEAVE["w"])["hw"]
    plate = S.K.add_box("plate_t62neg", (0.30, 0.01, 0.30), (float(c[0]), float(c[1] + hw + 0.05), float(c[2])))
    P.append((plate, "paint"))
    expect_raise("v2 FG8, a plate 2 cm off a kept boss (V1)", {"family": "FG", "wheels": 8}, P)


def main():
    t0 = time.time()
    if S.WHEELS != "v2":
        _fail("S0", f"cs2_sheaves.WHEELS default {S.WHEELS!r}, want v2")
    if any(v["segs"] != S.WHEEL_SEGS for v in FAM.values()) or S.WHEEL_SEGS % 4 or S.WHEEL_SEGS < 16:
        _fail("S0", f"WHEEL_FAMILY segments {[v['segs'] for v in FAM.values()]}, want {S.WHEEL_SEGS} (>= 16, x4) on all")
    s3_wheel()
    m1_material()
    l1_lod()
    n1_normals()
    total, rows = 0, []
    for label, asset, fn in assets():
        P = fn()
        n, tr, nb = check_asset(label, asset, P)
        if asset["wheels"] == 0:
            _ok("S5", f"{label}: no sheave-train wheel part (machinery / hosted tower only)")
        total += n
        rows.append((label, n, tr, nb))
    print("[sheaves] per asset: wheels, wheel tris, bosses (both / one / none)")
    for label, n, tr, nb in rows:
        if n:
            print(f"[sheaves]   {label:16s} {n:3d} wheels {tr:5d} tris ({tr / n:.0f} / wheel), bosses {nb['-+']} / "
                  f"{nb['-'] + nb['+']} / {nb['']}")
    s4_v1()
    s6_negatives()
    print(f"PASS test_sheaves: {total} wheels on {len(assets())} assets = THE wheel (16 seg, groove, seen bosses, "
          f"<= {S.WHEEL_TRIS_BUDGET} tris, {S.WHEEL_KEY}), LOD1 {S.WHEEL_LOD[1]['tris']} / LOD2 none, normals survive the FBX "
          f"({time.time() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except SheaveGateError as e:
        print(f"FAIL test_sheaves: {e}")
        raise
