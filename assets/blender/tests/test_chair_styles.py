"""
Gate for lift_chair.py's styles (asset round 2026-10, dq-chair.md section 10 with
dq-chair-skeptic.md applied: T1 determinism, T2 contracts, T3 trace, T4 full build).

  1. Determinism per style: tr, dq and fg6l are each built twice in ONE process (parts ->
     tags -> quadrant UVs -> join -> weld -> pivot -> orient -> shade; then the LOD1
     decimation and, for tr / dq, the authored LOD2 proxy with its UV snap). sha1 of the
     vertices (%.5f), faces, active UV (%.6f) and part table must match; dq is built
     first and again after tr (order independence).
  2. Contracts on every level, no finish (fast): C1-C9 on LOD0 / LOD1 / LOD2 through
     lift_chair.check_contracts, C10 silhouettes, C11 atlas stage (private atlas in a
     per-run temp dir, skeptic D13), budgets = the exact tallies (Revision 2: LOD0
     494 / 996, LOD2 62 / 98; dq C13 inside check_contracts), names through validate_asset_name and
     cs2_validate.lint_asset_name, the dq chair at --hanger-offset 0.40 (C7 bound = offset
     + r + 0.02, skeptic D7). Negative checks: a shifted vertex, a wrong carrier row and a
     shifted LOD2 must RAISE.
  2b. Revision 1 (verdicts 2026-10-11, items 1-2), measured on LOD0:
     tr has no footrest and no restraint part, a fixed grip (jaw block = the top, saddle on
     the rope reaching past the block fore / aft, spring housing outboard, two clamp plates
     below on either side of the stem) and the plank as its lowest part; dq's grip has the
     springs on the outboard face, the lever on the inboard face, the plate still on the
     clamp (Revision 1's top-bar restraint pins are superseded by 2c). Revision 1b (polish,
     revision1-check-chairs 2e; this replaces the Revision 1
     pin "wheels entirely above the rope"): the two running wheels sit BESIDE the jaw on
     the inboard side (wholly inboard of the jaw and the rope, within the jaw's length),
     centers within 0.04 of rope height and at the Kagelok D05 roller lateral (0.363 u x
     S_VERT inboard), tops well below the carriage top, each on an axle running from inside
     the jaw into the wheel; the carriage's top-face and fore / aft edges are chamfered; the
     friction comb (<= 24 tris) sits on the carriage top inside its footprint, its top
     0.1-5 mm below the carriage top (C2 stays the carriage's); LOD1's grip is the
     carriage + jaw only.
  2c. The dq restraint: Revision 2's parts (second look 2026-10-11, item 2') at
     Revision 3's numbers (verdict 2'' "too high", the rider photos; lift_chair PHOTO_*),
     measured on LOD0: the Revision 1 restraint parts are gone; two coaxial hinge bosses
     (~0.06 across), one per hoop leg (outer end at the side frame, center on the leg line),
     at the AL photo's pivot height (0.43-0.51 over the cushion, 0.05-0.15 under the backrest
     top), well below the top bar; the lap bar (the U's lateral run) in LAP_WINDOW_M and
     within 0.02 of the photo's 0.20 over the cushion, its back face ahead of the RIDER knee
     line, its center 0.10-0.25 ahead of the cushion front; the arms >= 0.55 m from the hinge
     to the lap bar axis, above the armrests; the U inside the side frames (no cantilever);
     two inverted T's (foot bar 0.35-0.55 long at y +-0.60, bottom z 0, its top - the boots -
     0.35-0.45 below the cushion, 0.10-0.35 ahead of the seat front and not behind the lap
     bar; stem lap bar -> foot bar, upright in the front view, <= 20 deg lean in profile);
     C13 passes with UP's bottom >= cushion + SEATED_HEAD_M and over the backrest, rider
     clearance >= C13_RIDER_CLEAR_M (heads / torsos >= 0.10), >= 2 deg short of the first
     contact. Negatives - each must RAISE: Revision 2's lap bar height (0.35), a lap bar over
     the knees, a hinge 0.20 over the cushion (the arc runs through the riders), a 60 deg
     swing (UP below a head), a swing past the clamp-block stop, the Revision 1 pivot
     (bosses moved onto the top bar beside the hanger clamp) and bosses floating 0.08
     inboard of the legs (revision2-check 2'-f).
  2d. Revision 1c (revision1b-check-dq-grip, "the softened carriage does not shade the way the
     build report says"): the dq carriage's big (axis-aligned) faces shade flat - corner normals
     within lift_chair.FLAT_FACE_MAX_DEG (2 deg) of the face normal on the built LOD0 (C14) and
     on an exported + re-imported FBX (verify_export flat_box); the pre-1c build (FLAT_PARTS
     empty: 25 / 65 deg) must raise C14 and fail the FBX check.
  2e. Nitpick A2 (2026-10-13, build/nitpick-a2-hanger-gondola.md): the dq grip side is the OUTBOARD
     side lift_chair.DQ_OUTBOARD (-1: the riders' right); every side-dependent check above reads the
     OUTBOARD-positive frame (y * DQ_OUTBOARD). The shipped side (-1) against the approved one (+1),
     built in one process: the same parts and per-part triangles; every part outside
     DQ_GRIP_SIDE_PARTS identical vertex for vertex; every grip-side part the exact mirror (y -> -y)
     of its +1 self; the hanger's vertical run on -Y; C13 equal; the LOD2 proxy's grip box + hanger
     mirrored, the rest identical.
  2f. Carrier split (2026-11-03): tr / dq --split (SkiLiftChair6N + SkiLiftChair6GripN, SkiLiftChairDQ4N +
     SkiLiftChairDQ4GripN) built into a temp folder; S1 / S2 / S3 (the pin sweeps pitch + fore-aft
     only, roll 0: the sway rolls the whole assembly), the pair contracts on every level, validation,
     the re-import and no home-folder path in the manifest args (see test_split); the tr manifests
     carry the clevis alignment audit, dq none.
  2g. tr split round 3 (owner verdict 2026-11-03): lift_chair.split_alignment passes on the built
     parts and raises when the clevis drifts 6 mm off the jaw block center in plan or the tube end
     tilts 2 deg about the pin; round 4: the split "grip" is the 0.68 m plate on the rope + the clamp box,
     the one-piece saddle untouched.
  3. Kagelok trace (T3): the bbox-derived [K] numbers in lift_chair.KAGELOK_U (skeptic
     section 1 corrections) re-measured from assets/external/kagelok-chair-lift/source/*.blend
     within 0.01 u, plus the scale chain to catalog/carriers.json (0.3505 m/u, grip top
     0.220 above the rope, side frames 1.212). SKIP (not fail) when the gitignored
     original is absent.
  4. --full (slow, ~1 min): per style tr / dq, lift_chair.main() twice with --finish --lods
     --verify (v2 bake, C12, folder validation and the manifest run inside) and the FBX
     clock frozen; every file's sha1 must match except <name>.manifest.json (written_utc,
     cs2_validate.py:1087), which is compared without written_utc and args.out (the two
     runs write two folders). Then --style fg6l once: its four PNGs and settings.json must
     equal the shipped assets/import/chairs/L/SkiLiftChair6L byte for byte (the FBX carry
     per-process UIDs - Python's string hash seed - so they are compared within a process
     only, by the T4 pair above).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_chair_styles.py [-- --full]

Exit 1 on any failure; one PASS line on success.
"""
import datetime
import glob
import hashlib
import json
import math
import os
import shutil
import sys
import tempfile
import time
import types

import bpy
import mathutils
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(HERE)
sys.path.insert(0, LIB)
import cs2_asset      # noqa: E402
import cs2_validate   # noqa: E402
import lift_chair as L  # noqa: E402

ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FULL = "--full" in ARGV
KAGELOK_BLEND = sorted(glob.glob(os.path.join(L.REPO_ROOT, "assets", "external",
                                              "kagelok-chair-lift", "source", "*.blend")))
FROZEN_FBX_TIME = datetime.datetime(2026, 1, 1, 0, 0, 0)
FAILS = []


def check(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"[chair-test] FAIL: {msg}")
    return cond


def raises(fn, what):
    try:
        fn()
    except Exception as e:  # noqa: BLE001
        print(f"[chair-test] ok {what} raised: {str(e)[:110]}")
        return True
    check(False, f"{what} did not raise")
    return False


def mesh_hash(obj):
    me = obj.data
    h = hashlib.sha1()
    for v in me.vertices:
        h.update(("%.5f %.5f %.5f;" % tuple(v.co)).encode())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    for lp in me.uv_layers.active.data:
        h.update(("%.6f %.6f;" % tuple(lp.uv)).encode())
    h.update(me.get(cs2_asset.PART_TABLE_PROP, "").encode())
    return h.hexdigest()[:12]


def build(style_key, name, hanger_offset=L.HANGER_OFFSET):
    """LOD0 (+ the style's info) from a clean scene."""
    L.clear_scene()
    st = L.STYLES[style_key]
    row = L.CARRIERS[st.carrier]
    if style_key == "fg6l":
        return st, row, L.build_chair(L.ChairParams(seats=st.seats), name), None
    chair, info = L.build_style_chair(st, row, name, hanger_offset)
    info["lod0_tris"] = cs2_asset.tri_count(chair)
    return st, row, chair, info


def lod1_of(st, row, chair, name):
    lod = cs2_asset.make_lod_chain(chair, name, (st.lod1_target,), (st.lod1_delete,))[0]
    L.pin_height(lod, 0.0, float(row["hang_m"]))
    return lod


# --------------------------------------------------------------------------------------
# 1. determinism
# --------------------------------------------------------------------------------------

def build_hashes(style_key):
    name = {"tr": "DetChairTr", "dq": "DetChairDq", "fg6l": "DetChairFg"}[style_key]
    st, row, chair, _info = build(style_key, name)
    out = {"LOD0": mesh_hash(chair)}
    out["LOD1"] = mesh_hash(lod1_of(st, row, chair, name))
    if style_key != "fg6l":
        out["LOD2"] = mesh_hash(L.build_lod2_proxy(st, row, chair, name))
    return out


def test_determinism():
    runs = []
    for key in ("dq", "tr", "dq", "tr", "fg6l", "fg6l"):
        runs.append((key, build_hashes(key)))
    by = {}
    for key, h in runs:
        by.setdefault(key, []).append(h)
    for key, (a, b) in sorted(by.items()):
        same = a == b
        print(f"[det] {key}: run1 {a} run2 {b} -> {'SAME' if same else 'DIFFERENT'}")
        check(same, f"determinism {key}: {a} vs {b}")
    return {k: v[0] for k, v in by.items()}


# --------------------------------------------------------------------------------------
# 2. contracts on every level
# --------------------------------------------------------------------------------------

def test_contracts(work):
    summary = {}
    for key in ("tr", "dq"):
        st = L.STYLES[key]
        name = f"{st.base}{st.letter}"
        check(cs2_asset.validate_asset_name(name) == name, f"{name}: validate_asset_name")
        rep = cs2_validate._Report(name)
        cs2_validate.lint_asset_name(name, rep)
        check(rep.counts() == (0, 0), f"{name}: lint_asset_name {list(rep)}")
        st, row, chair, info = build(key, name)
        res0 = L.check_contracts(chair, st, row, 0)
        check(res0["C8"] == f"{st.lod0_tris} tris", f"{key} LOD0 {res0['C8']} vs {st.lod0_tris}")
        adir = os.path.join(work, f"atlas_{key}")
        os.makedirs(adir)
        q, c = cs2_asset.lift_atlas_quads(os.path.join(LIB, "..", "textures"))
        cs2_asset.make_texture_set_atlas(adir, name, q, quad_size=L.ATLAS_QUAD_PX,
                                         control_quads=c, post=L.chair_atlas_post)
        c11 = L.check_atlas_stage(chair, info, adir, name)
        lod1 = lod1_of(st, row, chair, name)
        res1 = L.check_contracts(lod1, st, row, 1, info={"lod0_tris": info["lod0_tris"]})
        lod2 = L.build_lod2_proxy(st, row, chair, name)
        res2 = L.check_contracts(lod2, st, row, 2, info={"lod0_tris": info["lod0_tris"],
                                                          "proxy": True})
        check(res2["C8"] == f"{st.lod2_tris} tris", f"{key} LOD2 {res2['C8']} vs {st.lod2_tris}")
        sil = L.check_silhouettes(chair, {1: lod1, 2: lod2}, st)
        for lod in (lod1, lod2):
            check(len(lod.data.uv_layers) == 1 and lod.data.uv_layers.active.name == "UVMap",
                  f"{lod.name}: UV layers {[u.name for u in lod.data.uv_layers]}")
            check(all(abs(x) < 1e-9 for x in lod.location), f"{lod.name}: location {tuple(lod.location)}")
        print(f"[contracts] {key}: LOD0 {res0['C8']}, LOD1 {res1['C8']}, LOD2 {res2['C8']}; "
              f"{res0['C6']}; {res0.get('C7', '-')}; {c11['C11']}")
        print(f"[contracts] {key}: C10 {sil}")
        summary[key] = (res0["C8"], res1["C8"], res2["C8"])

        # negatives: a wrong build must raise
        def shifted():
            low = int(np.argmin([vx.co.z for vx in chair.data.vertices]))
            v = chair.data.vertices[low]                # the footbar's bottom vertex
            v.co.z -= 0.01
            try:
                L.check_contracts(chair, st, row, 0)
            finally:
                v.co.z += 0.01
        raises(shifted, f"{key}: C1 with the lowest vertex 1 cm lower")
        wrong = dict(row, hang_m=float(row["hang_m"]) + 0.10)
        raises(lambda: L.check_contracts(chair, st, wrong, 0), f"{key}: C1 against a hang 0.10 m off")

        def lod2_shift():
            lod2.location.z += 0.05
            bpy.context.view_layer.update()
            try:
                L.check_contracts(lod2, st, row, 2)
            finally:
                lod2.location.z -= 0.05
                bpy.context.view_layer.update()
        raises(lod2_shift, f"{key}: C1 on a LOD2 lifted 5 cm")
        if key == "dq":
            def c7():
                tbl = L.part_table(chair)
                ids = L._part_ids(tbl, "tube_hanger")
                vs = L._verts_of_parts(chair, ids)
                # authoring +y = world +X after orient_for_net; outboard = DQ_OUTBOARD * y (nitpick A2)
                for i in vs:
                    chair.data.vertices[int(i)].co.x += 0.10 * L.DQ_OUTBOARD
                try:
                    L.check_contracts(chair, st, row, 0)
                finally:
                    for i in vs:
                        chair.data.vertices[int(i)].co.x -= 0.10 * L.DQ_OUTBOARD
            raises(c7, "dq: C7 with the hanger pushed 0.10 m outboard")

    # dq at --hanger-offset 0.40 (skeptic D7 bound)
    st, row, chair, info = build("dq", "SkiLiftChairDQ4M", hanger_offset=0.40)
    r = L.check_contracts(chair, st, row, 0, hanger_offset=0.40)
    print(f"[contracts] dq offset 0.40: {r['C7']}")
    check("C7" in r, "dq offset 0.40: C7 missing")
    raises(lambda: L.check_contracts(chair, st, row, 0, hanger_offset=0.30),
           "dq: offset-0.40 hanger against the 0.30 bound")
    return summary


# --------------------------------------------------------------------------------------
# 2b. Revision 1 (verdicts 2026-10-11)
# --------------------------------------------------------------------------------------

ROPE_R = 0.024        # lift_cable.py default rope radius


def part_box(chair, name, outboard=1.0):
    """Authoring-frame AABB (min, max) of the part named exactly `name`; `outboard` -1 reads it in the
    OUTBOARD-positive frame (y negated, nitpick A2: the dq checks pass L.DQ_OUTBOARD)."""
    tbl = L.part_table(chair)
    vs = L._verts_of_parts(chair, L._part_ids(tbl, name, exact=True))
    if not len(vs):
        raise RuntimeError(f"part {name!r} not in {tbl}")
    a = L.authoring_coords(chair)[vs] * np.array([1.0, float(outboard), 1.0])
    return a.min(axis=0), a.max(axis=0)


def test_revision1():
    out = {}
    # -- tr: old school (no footrest, no restraint), a real fixed grip -------------------
    st, row, chair, _info = build("tr", "RevTr")
    tbl = L.part_table(chair)
    banned = [n for n in tbl if n.startswith(("footbar", "footlink_", "barlink", "barfront",
                                              "barhinge", "strut", "restraint"))]
    check(not banned, f"tr: footrest / restraint parts present {banned}")
    hang = float(row["hang_m"])
    rope = hang - float(row["grip_top_above_rope_m"])
    g0, g1 = part_box(chair, "grip")
    j0, j1 = part_box(chair, "gripjaw")
    s0, s1 = part_box(chair, "gripspring")
    check(abs(g1[2] - hang) < 1e-5, f"tr: jaw block top {g1[2]:.4f} != {hang}")
    check(j0[0] < g0[0] - 0.15 and j1[0] > g1[0] + 0.15,
          f"tr: saddle x {j0[0]:.3f}..{j1[0]:.3f} does not reach past the block {g0[0]:.3f}..{g1[0]:.3f}")
    check(rope <= j0[2] <= rope + ROPE_R and j1[2] < hang,
          f"tr: saddle z {j0[2]:.4f}..{j1[2]:.4f} not riding on the rope ({rope:.3f} + {ROPE_R})")
    check(s1[1] > g1[1] + 0.05 and g0[2] <= s0[2] and s1[2] <= g1[2],
          f"tr: spring housing y ..{s1[1]:.3f} z {s0[2]:.3f}..{s1[2]:.3f} vs block {g0}..{g1}")
    p0 = [part_box(chair, f"gripplate_{k}") for k in (0, 1)]
    check(p0[0][1][1] < -0.045 + 1e-4 and p0[1][0][1] > 0.045 - 1e-4,
          f"tr: clamp plates y {p0[0][1][1]:.3f} / {p0[1][0][1]:.3f} do not straddle the stem (r 0.045)")
    check(all(b[1][2] <= g0[2] + 0.012 and b[0][2] < g0[2] - 0.08 for b in p0),
          f"tr: clamp plates not below the jaw block: {[(round(b[0][2], 3), round(b[1][2], 3)) for b in p0]}")
    sb0, _sb1 = part_box(chair, "seat")
    check(abs(sb0[2]) < 1e-5, f"tr: seat plank bottom {sb0[2]:.5f} != 0 (lowest point)")
    out["tr"] = (f"no footrest/restraint, saddle {j1[0] - j0[0]:.2f} m on the rope, spring "
                 f"{s1[1] - g1[1]:.3f} m proud, plates below, plank lowest")
    print(f"[rev1] tr: {out['tr']}")

    # -- dq: the grip (Revision 1 / 1b); the restraint is test_revision3's ---------------
    st, row, chair, _info = build("dq", "RevDq")
    hang = float(row["hang_m"])
    rope = hang - float(row["grip_top_above_rope_m"])
    O = L.DQ_OUTBOARD                       # nitpick A2: every grip check in the OUTBOARD-positive frame

    def pbo(ch, nm):
        return part_box(ch, nm, O)
    c0, c1 = pbo(chair, "clampblock")
    gb0, gb1 = pbo(chair, "gripbody")
    j0, j1 = pbo(chair, "jaw_rope")
    wy_trace = -L.KAGELOK_U["roller_inboard"] * L.S_VERT
    wc_all = []
    for k in (0, 1):
        # Revision 1b: BESIDE the jaw on the inboard side, center near rope height (Kagelok
        # D05 rollers, skeptic claim 4) - was "entirely above the rope" in Revision 1
        w0, w1 = pbo(chair, f"wheel_{k}")
        wc = (w0 + w1) / 2.0
        wc_all.append(wc)
        check(abs(wc[2] - rope) <= 0.04 and w1[2] < hang - 0.05,
              f"dq: wheel {k} center z {wc[2]:.3f} not near rope height {rope:.3f} (top {w1[2]:.3f})")
        check(w1[1] < j0[1] and w1[1] < -ROPE_R and abs(wc[1] - wy_trace) < 0.005,
              f"dq: wheel {k} y {w0[1]:.3f}..{w1[1]:.3f} not inboard beside the jaw (jaw from "
              f"{j0[1]:.3f}, rope {-ROPE_R}, trace center {wy_trace:.3f})")
        check(j0[0] <= w0[0] and w1[0] <= j1[0],
              f"dq: wheel {k} x {w0[0]:.3f}..{w1[0]:.3f} not alongside the jaw {j0[0]:.3f}..{j1[0]:.3f}")
        a0, a1 = pbo(chair, f"axle_{k}")
        check(j0[1] < a1[1] <= j1[1] and j0[2] <= a0[2] and a1[2] <= j1[2] and a0[1] <= wc[1] + 1e-4,
              f"dq: axle {k} y {a0[1]:.3f}..{a1[1]:.3f} z {a0[2]:.3f}..{a1[2]:.3f} does not run from "
              f"inside the jaw into the wheel")
        q0, _q1 = pbo(chair, f"spring_{k}")
        check(q0[1] >= gb1[1] - 0.02, f"dq: spring {k} y {q0[1]:.3f} not on the outboard face {gb1[1]:.3f}")
    check(wc_all[0][0] < 0.0 < wc_all[1][0], f"dq: wheels not fore and aft {[round(w[0], 3) for w in wc_all]}")
    # Revision 1b: the carriage's edges are chamfered (its top vertices sit inside the
    # top-face bbox by the chamfer; its fore / aft faces stop below the top) and the
    # friction comb sits on its top just below the rim, inside its footprint
    soft = L.DQ_GRIP["soft"]
    a = L.authoring_coords(chair) * np.array([1.0, O, 1.0])
    gv = a[L._verts_of_parts(chair, L._part_ids(L.part_table(chair), "gripbody", exact=True))]
    tv = gv[gv[:, 2] >= hang - 1e-4]
    ends = gv[np.abs(gv[:, 0]) >= gb1[0] - 1e-4]
    check(len(tv) > 0 and float(np.abs(tv[:, 0]).max()) <= gb1[0] - soft + 1e-4
          and float(tv[:, 1].min()) >= gb0[1] + soft - 1e-4
          and float(tv[:, 1].max()) <= gb1[1] - soft + 1e-4
          and float(ends[:, 2].max()) <= hang - soft + 1e-4,
          f"dq: carriage edges not chamfered by {soft} (top x {np.abs(tv[:, 0]).max():.4f}, "
          f"y {tv[:, 1].min():.4f}..{tv[:, 1].max():.4f}, fore/aft faces to z {ends[:, 2].max():.4f})")
    cb0, cb1 = pbo(chair, "comb")
    check(hang - 0.005 < cb1[2] < hang - 1e-4 and cb0[2] > hang - 0.02,
          f"dq: comb z {cb0[2]:.4f}..{cb1[2]:.4f} not just below the carriage top {hang:.3f}")
    check(np.all(cb0[:2] > gb0[:2]) and np.all(cb1[:2] < gb1[:2])
          and (cb1[0] - cb0[0]) >= 0.7 * (gb1[0] - gb0[0]),
          f"dq: comb footprint {np.round(cb0[:2], 3)}..{np.round(cb1[:2], 3)} not on the carriage top "
          f"{np.round(gb0[:2], 3)}..{np.round(gb1[:2], 3)}")
    check(dict(L.STYLES["dq"].part_tris)["comb"] <= 24, "dq: comb over 24 tris")
    # LOD1 keeps the grip as box + jaw (Revision 1b: comb and axles are deleted with the
    # wheels, springs and lever)
    lod1 = lod1_of(st, row, chair, "RevDq")
    t1 = L.part_table(lod1)
    present = {t1[i - 1] for i in set(L.face_parts(lod1).tolist())}
    grip = {"gripbody", "comb", "jaw_rope", "lever_fin", "lever_roller", "spring_0", "spring_1",
            "wheel_0", "wheel_1", "axle_0", "axle_1"}
    check(present & grip == {"gripbody", "jaw_rope"},
          f"dq LOD1 grip parts {sorted(present & grip)} != carriage + jaw")
    l0, l1 = pbo(chair, "lever_fin")
    check(l1[1] <= gb0[1] + 1e-4, f"dq: lever y ..{l1[1]:.3f} not on the inboard face {gb0[1]:.3f}")
    pf0, pf1 = pbo(chair, "plate_face")
    check(np.all(pf0 >= c0 - 0.01) and np.all(pf1 <= c1 + 0.01), "dq: number plate left the clamp block")
    out["dq"] = (f"wheels beside the jaw at rope {wc_all[0][2] - rope:+.3f}, {-wc_all[0][1]:.3f} "
                 f"inboard; carriage chamfered, comb top {hang - cb1[2]:.4f} below it; LOD1 grip = "
                 f"carriage + jaw; plate on the clamp")
    print(f"[rev1] dq: {out['dq']}")
    return out


# --------------------------------------------------------------------------------------
# 2c. The dq restraint: Revision 2 parts (item 2'), Revision 3 numbers (2'', rider photos)
# --------------------------------------------------------------------------------------

def _with_bar(**kw):
    """Context: DQ_BAR temporarily patched (restored even when the build raises)."""
    class _Ctx:
        def __enter__(self):
            self.saved = dict(L.DQ_BAR)
            L.DQ_BAR.update(kw)

        def __exit__(self, *exc):
            L.DQ_BAR.clear()
            L.DQ_BAR.update(self.saved)
            return False
    return _Ctx()


def _move_part(chair, name, delta_auth):
    """Translate the part's vertices by an AUTHORING-frame delta (world = (y, -x, z));
    returns an undo callable."""
    tbl = L.part_table(chair)
    vs = L._verts_of_parts(chair, L._part_ids(tbl, name, exact=True))
    dx, dy, dz = delta_auth
    loc = chair.matrix_world.to_3x3().inverted() @ mathutils.Vector((dy, -dx, dz))
    me = chair.data
    for i in vs:
        me.vertices[int(i)].co += loc

    def undo():
        for i in vs:
            me.vertices[int(i)].co -= loc
    return undo


def test_revision3():
    """2c: the dq restraint, Revision 2 parts placed by the Revision 3 rider photos."""
    st, row, chair, _info = build("dq", "Rev3Dq")
    tbl = L.part_table(chair)
    gone = [n for n in tbl if n.startswith(("barlink_", "barfront", "strut", "restraint"))
            or n == "footbar"]
    check(not gone, f"dq: Revision 1 restraint parts still present {gone}")
    for n in ("barhinge_0", "barhinge_1", "barlink", "footstem_0", "footstem_1", "footbar_0", "footbar_1"):
        check(n in tbl, f"dq: part {n} missing")
    seat_top = max(part_box(chair, f"cushion_{k}")[1][2] for k in range(4))
    seat_front = max(part_box(chair, f"cushion_{k}")[1][0] for k in range(4))
    sp0, sp1 = part_box(chair, "seatpan")
    h0, h1 = part_box(chair, "tube_hoop")
    top_bar_z = h1[2] - L.DQ_R["hoop"]
    back_top = max(part_box(chair, n)[1][2] for n in tbl if n.startswith(("pad_", "backboard")))
    arm0, arm1 = part_box(chair, "armrest_0")
    d = L.dq_datums(row)
    (bkx, bkz), (tpx, tpz) = (d["hoop"][1][0], d["hoop"][1][2]), (d["hoop"][2][0], d["hoop"][2][2])
    leg = np.array([tpx - bkx, tpz - bkz]) / np.hypot(tpx - bkx, tpz - bkz)

    # hinges: one boss per SIDE FRAME, on the leg line, coaxial, at the AL photo's pivot
    # height (0.46-0.51 measured; 0.43-0.44 scaled by the backrest / top bar) just under
    # the backrest top
    hc = []
    for k in (0, 1):
        b0, b1 = part_box(chair, f"barhinge_{k}")
        c = (b0 + b1) / 2.0
        hc.append(c)
        outer = b0[1] if k == 0 else b1[1]
        check(abs(abs(outer) - L.SIDE_Y) < 1e-3, f"dq: hinge {k} outer end |y| {abs(outer):.4f} != side frame {L.SIDE_Y}")
        off = np.array([c[0] - bkx, c[2] - bkz])
        dist = abs(off[0] * leg[1] - off[1] * leg[0])
        check(dist < 0.005, f"dq: hinge {k} {dist:.4f} m off the hoop leg line")
        check(0.43 <= c[2] - seat_top <= 0.51 and 0.05 <= back_top - c[2] <= 0.15,
              f"dq: hinge {k} {c[2] - seat_top:.3f} above the cushion, {back_top - c[2]:.3f} under the "
              f"backrest top (want 0.43-0.51 / 0.05-0.15: the AL photo pivot)")
        check(abs(c[2] - seat_top - L.PHOTO_PIVOT_ABOVE_CUSHION_M) < 1e-3,
              f"dq: hinge {k} not at PHOTO_PIVOT_ABOVE_CUSHION_M ({L.PHOTO_PIVOT_ABOVE_CUSHION_M})")
        check(c[2] < top_bar_z - 0.3, f"dq: hinge {k} z {c[2]:.3f} near the top bar {top_bar_z:.3f}")
        check(18 <= dict(st.part_tris)[f"barhinge_{k}"] <= 24 and abs((b1[0] - b0[0]) - 2 * L.DQ_R["barhinge"]) < 0.015,
              f"dq: hinge {k} boss {b1[0] - b0[0]:.3f} across (want ~0.06)")
    check(abs(hc[0][0] - hc[1][0]) < 1e-4 and abs(hc[0][2] - hc[1][2]) < 1e-4, "dq: hinge bosses not coaxial")

    # lap bar DOWN: on the riders' laps (AL 0.19-0.21 above the cushion; Revision 2's 0.35
    # was "too high"), ahead of the knee line, 0.10-0.25 ahead of the cushion front
    a = L.authoring_coords(chair)
    u = a[L._verts_of_parts(chair, L._part_ids(tbl, "barlink", exact=True))]
    lap = u[np.abs(u[:, 1]) <= L.DQ_BAR["arm_y"] - L.DQ_BAR["corner"] + 1e-3]
    lx, lz = float(lap[:, 0].mean()), float(lap[:, 2].mean())
    knee_x = L._pad_front_x(seat_top, d["pad_edge"]) + L.RIDER["knee"]
    check(L.LAP_WINDOW_M[0] <= lz - seat_top <= L.LAP_WINDOW_M[1]
          and abs(lz - seat_top - L.PHOTO_LAP_ABOVE_CUSHION_M) <= 0.02,
          f"dq: lap bar {lz - seat_top:.3f} above the cushion (want {L.LAP_WINDOW_M}, photo "
          f"{L.PHOTO_LAP_ABOVE_CUSHION_M})")
    check(float(lap[:, 0].min()) > knee_x and 0.10 <= lx - seat_front <= 0.25,
          f"dq: lap bar back face x {lap[:, 0].min():.3f} vs knees {knee_x:.3f}; center "
          f"{lx - seat_front:.3f} ahead of the cushion front (want 0.10-0.25)")
    # long arms: hinge -> lap bar axis >= 0.55, passing over the armrests
    arm_len = math.hypot(lx - hc[0][0], lz - hc[0][2])
    t = (0.250 - hc[0][0]) / (lx - hc[0][0])
    arm_z_at_rest_end = hc[0][2] + t * (lz - hc[0][2])
    check(arm_len >= 0.55 and arm_z_at_rest_end > arm1[2] + L.DQ_R["barlink"],
          f"dq: arm {arm_len:.3f} m long, {arm_z_at_rest_end:.3f} at the armrest end (armrest top {arm1[2]:.3f})")
    # no cantilever past the arms: the U stays inside the side frames' inner faces
    check(float(np.abs(u[:, 1]).max()) < L.SIDE_Y - L.DQ_R["hoop"], f"dq: bar reaches |y| {np.abs(u[:, 1]).max():.3f}")

    # two inverted T's hanging from the lap bar; the boots rest 0.35-0.45 below the cushion
    for k, s in ((0, -1), (1, 1)):
        f0, f1 = part_box(chair, f"footbar_{k}")
        t0, t1 = part_box(chair, f"footstem_{k}")
        fy = (f0[1] + f1[1]) / 2.0
        fz = (f0[2] + f1[2]) / 2.0
        check(abs(fy - s * L.DQ_BAR["foot_y"]) < 1e-3 and 0.35 <= f1[1] - f0[1] <= 0.55,
              f"dq: foot bar {k} y {f0[1]:.3f}..{f1[1]:.3f} (one T per rider pair)")
        check(abs(f0[2]) < 1e-6 and 0.35 <= seat_top - f1[2] <= 0.45,
              f"dq: foot bar {k} top {seat_top - f1[2]:.3f} below the cushion (boots, want 0.35-0.45), "
              f"bottom {f0[2]:.4f}")
        check(0.10 <= (f0[0] + f1[0]) / 2.0 - sp1[0] <= 0.35 and (f0[0] + f1[0]) / 2.0 >= lx,
              f"dq: foot bar {k} {(f0[0] + f1[0]) / 2.0 - sp1[0]:.3f} ahead of the seat front (want "
              f"0.10-0.35, not behind the lap bar)")
        check(abs(t1[2] - lz) < L.DQ_R["footstem"] + 1e-3 and abs(t0[2] - fz) < L.DQ_R["footstem"] + 1e-3,
              f"dq: stem {k} z {t0[2]:.3f}..{t1[2]:.3f} does not run lap bar {lz:.3f} -> foot bar {fz:.3f}")
        check(t1[1] - t0[1] <= 2 * L.DQ_R["footstem"] + 1e-3,
              f"dq: stem {k} not upright in the front view ({t1[1] - t0[1]:.3f} wide)")
        lean = math.degrees(math.atan2((t1[0] - t0[0]) - 2 * L.DQ_R["footstem"], t1[2] - t0[2]))
        check(lean <= 20.0, f"dq: stem {k} leans {lean:.1f} deg in profile (want <= 20)")

    # C13: the bar swings DOWN -> UP past the chair and the seated riders; UP over the heads
    r = L.check_bar_mechanics(chair, row)
    check(r["up_bottom"] - seat_top >= L.SEATED_HEAD_M and r["up_bottom"] > back_top,
          f"dq: UP lap bar bottom {r['up_bottom'] - seat_top:.3f} above the cushion (want >= "
          f"{L.SEATED_HEAD_M}, over the backrest {back_top:.3f})")
    check(r["rider_clearance_m"] >= L.C13_RIDER_CLEAR_M and r["body_clearance_m"] >= 0.10,
          f"dq: rider clearance {r['rider_clearance_m']:.3f}, heads / torsos {r['body_clearance_m']:.3f} (want >= 0.10)")
    check(r["stop_margin_deg"] is not None and r["stop_margin_deg"] >= 2.0,
          f"dq: UP {r['stop_margin_deg']} deg short of the first contact (want >= 2)")

    # negatives: each must RAISE (C13)
    cases = (
        ("Revision 2's lap bar 0.35 above the cushion (too high)", {"down": (0.480, 0.750)}),
        ("lap bar over the knees, not ahead of them", {"down": (0.400, 0.600)}),
        ("hinge 0.20 above the cushion (the arc runs through the riders)", {"hinge_z": 0.600}),
        ("swing 60 deg (UP below a seated head)", {"swing_deg": 60.0}),
        ("swing 99.5 deg, past the stop (the lap bar into the hanger clamp)", {"swing_deg": 99.5}),
    )
    for what, kw in cases:
        with _with_bar(**kw):
            _st, _row, bad, _i = build("dq", "Rev3DqBad")
            raises(lambda: L.check_contracts(bad, _st, _row, 0), f"dq C13: {what}")
    # the Revision 1 pivot: bosses on the top bar beside the hanger clamp; and bosses slid
    # 0.08 inboard, floating beside the legs (revision2-check 2'-f: passed C13 before)
    st, row, chair, _info = build("dq", "Rev3DqMoved")
    for what, delta in (("Revision 1 hinge on the top bar beside the hanger clamp",
                         lambda c, s: (0.030 - c[0], s * 0.270 - c[1], 1.503 - c[2])),
                        ("hinge bosses floating 0.08 inboard of the side frames",
                         lambda c, s: (0.0, -s * 0.080, 0.0))):
        undo = []
        for k, s in ((0, -1), (1, 1)):
            b0, b1 = part_box(chair, f"barhinge_{k}")
            undo.append(_move_part(chair, f"barhinge_{k}", delta((b0 + b1) / 2.0, s)))
        try:
            raises(lambda: L.check_bar_mechanics(chair, row, verbose=False), f"dq C13: {what}")
        finally:
            for f in undo:
                f()
    out = (f"hinges on the side frames {hc[0][2] - seat_top:.2f} above the cushion ({back_top - hc[0][2]:.3f} "
           f"under the backrest top, x {hc[0][0]:.3f}); lap bar {lz - seat_top:.2f} above the cushion, "
           f"{lap[:, 0].min() - knee_x:.3f} ahead of the knees, {lx - seat_front:.3f} ahead of the seat "
           f"front; arms {arm_len:.3f} m; T tops {seat_top - (part_box(chair, 'footbar_0')[1][2]):.3f} "
           f"below; C13 swing {r['swing_deg']:.0f} deg, clearance {r['clearance_m']:.3f}, riders "
           f"{r['rider_clearance_m']:.3f} (heads {r['body_clearance_m']:.3f}), UP bottom "
           f"{r['up_bottom'] - seat_top:.3f} above the cushion at x {r['up'][0]:.3f}, stop "
           f"+{r['stop_margin_deg']} deg ({', '.join(r['stop_parts'] or [])})")
    print(f"[rev3] dq: {out}")
    return out


# --------------------------------------------------------------------------------------
# 3. Kagelok trace
# --------------------------------------------------------------------------------------

def _islands(obj):
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    seen, out = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, comp = [v], []
        seen.add(v.index)
        while stack:
            x = stack.pop()
            comp.append(x.index)
            for e in x.link_edges:
                w = e.other_vert(x)
                if w.index not in seen:
                    seen.add(w.index)
                    stack.append(w)
        out.append(sorted(comp))
    bm.free()
    return out


def _co(obj):
    me = obj.data
    a = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", a)
    return a.reshape(-1, 3).astype(np.float64)


def test_trace():
    if not KAGELOK_BLEND:
        print("[trace] SKIP: assets/external/kagelok-chair-lift/source/*.blend absent (gitignored)")
        return "skipped"
    import cs2_ingest
    objs = cs2_ingest.load_external(KAGELOK_BLEND[0], objects=["05", "D05", "BezierCurve"])
    try:
        by = {o.get(cs2_ingest.SRC_NAME_PROP, o.name): o for o in objs}
        K = L.KAGELOK_U
        got = {}
        rope = _co(by["BezierCurve"])
        sel = rope[(np.abs(rope[:, 1] + 24.31) < 1.0) & (rope[:, 0] < 0.0)]
        got["rope_x"] = (sel[:, 0].min() + sel[:, 0].max()) / 2.0
        got["rope_z"] = (sel[:, 2].min() + sel[:, 2].max()) / 2.0
        g = _co(by["D05"])
        got["grip_top_z"], got["grip_bottom_z"] = g[:, 2].max(), g[:, 2].min()
        # D05's two 36-vertex rollers (skeptic claim 4; the dq wheels' Revision 1b trace):
        # their lateral offset from the rope is KAGELOK_U roller_inboard, centers at rope height
        rol = [(g[i].min(axis=0), g[i].max(axis=0)) for i in _islands(by["D05"]) if len(i) == 36]
        check(len(rol) == 2, f"trace: {len(rol)} D05 rollers")
        if rol:
            got["roller_inboard"] = float(np.mean([(r0[0] + r1[0]) / 2.0 for r0, r1 in rol])) - got["rope_x"]
            dz = max(abs((r0[2] + r1[2]) / 2.0 - got["rope_z"]) for r0, r1 in rol)
            check(dz < 0.03, f"trace: D05 roller centers {dz:.3f} u off the rope height")
        c = _co(by["05"])
        got["chair_lowest_z"] = c[:, 2].min()
        isl = [(len(i), c[i].min(axis=0), c[i].max(axis=0)) for i in _islands(by["05"])]
        hoop = max(isl, key=lambda t: t[0])
        got["chair_x"] = (hoop[1][0] + hoop[2][0]) / 2.0
        got["hoop_outer_x"] = (hoop[2][0] - hoop[1][0]) / 2.0
        got["hoop_top_z"] = hoop[2][2]
        cush = sorted([t for t in isl if t[0] == 24 and abs(t[2][2] - K["cushion_top_z"]) < 0.05],
                      key=lambda t: t[1][0])
        pads = [t for t in isl if t[0] == 24 and abs(t[2][2] - K["pad_top_z"]) < 0.05]
        check(len(cush) == 4 and len(pads) == 4, f"trace: {len(cush)} cushions / {len(pads)} pads")
        if len(cush) == 4:
            got["cushion_top_z"] = max(t[2][2] for t in cush)
            got["cushion_w"] = float(np.median([t[2][0] - t[1][0] for t in cush]))
            got["cushion_d"] = float(np.median([t[2][1] - t[1][1] for t in cush]))
            cx = [(t[1][0] + t[2][0]) / 2.0 for t in cush]
            got["cushion_pitch"] = float(np.mean(np.diff(cx)))
        if pads:
            got["pad_top_z"] = max(t[2][2] for t in pads)
        arms = [t for t in isl if t[0] == 96]
        check(len(arms) == 2, f"trace: {len(arms)} armrest loops")
        if arms:
            got["armrest_x"] = float(np.mean([abs((t[1][0] + t[2][0]) / 2.0 - got["chair_x"])
                                              for t in arms]))
        plates = [t for t in isl if t[0] == 8]
        check(len(plates) == 2, f"trace: {len(plates)} clamp plates")
        bar = [t for t in isl if t[0] == 60]
        check(len(bar) == 1, f"trace: {len(bar)} rear crossbars")
        vec = {}
        if plates:
            vec["plate"] = tuple(plates[0][2] - plates[0][1])
        if bar:
            vec["crossbar"] = tuple(bar[0][2] - bar[0][1])
        bad = {k: (round(float(v), 4), K[k]) for k, v in got.items() if abs(float(v) - K[k]) > 0.01}
        for k, v in vec.items():
            if max(abs(a - b) for a, b in zip(v, K[k])) > 0.01:
                bad[k] = (tuple(round(x, 4) for x in v), K[k])
        check(not bad, f"trace: [K] numbers off by > 0.01 u: {bad}")
        # the scale chain to the carrier contract (dq-chair.md section 3, skeptic claim 1)
        s = 2.900 / (got["grip_top_z"] - got["cushion_top_z"])
        check(abs(s - L.S_VERT) < 5e-4, f"trace: S {s:.5f} vs S_VERT {L.S_VERT}")
        above = (got["grip_top_z"] - got["rope_z"]) * L.S_VERT
        want = float(L.CARRIERS["dq"]["grip_top_above_rope_m"])
        check(abs(above - want) < 1e-3, f"trace: grip top above rope {above:.4f} vs carriers.json {want}")
        side = (got["hoop_outer_x"] - K["hoop_r"]) * L.W_LAT
        check(abs(side - L.SIDE_Y) < 1e-3, f"trace: side frames {side:.4f} vs SIDE_Y {L.SIDE_Y}")
        check(abs(L.W_LAT * got.get("cushion_pitch", 0.0) - 0.600) < 1e-3,
              f"trace: pitch x W = {L.W_LAT * got.get('cushion_pitch', 0.0):.4f} vs 0.600")
        print(f"[trace] {len(got) + len(vec)} [K] numbers within 0.01 u; S {s:.4f}, grip top "
              f"{above:.4f} m above the rope, side frames +-{side:.4f} m, rope x "
              f"{got['rope_x']:.4f}, chair {got['chair_x'] - got['rope_x']:.3f} u inboard")
        return "ok"
    finally:
        cs2_ingest.discard(objs)


# --------------------------------------------------------------------------------------
# 4. full builds (opt-in)
# --------------------------------------------------------------------------------------

def freeze_fbx_clock():
    import io_scene_fbx.export_fbx_bin as fbxbin

    class _Frozen(datetime.datetime):
        @classmethod
        def now(cls, tz=None):
            return FROZEN_FBX_TIME

    fbxbin.datetime = types.SimpleNamespace(datetime=_Frozen)


def run_main(args):
    saved = sys.argv
    sys.argv = ["blender", "--"] + args
    try:
        L.main()
    finally:
        sys.argv = saved


def folder_state(folder, name):
    out = {}
    for fn in sorted(os.listdir(folder)):
        p = os.path.join(folder, fn)
        if fn == f"{name}.manifest.json":
            with open(p, encoding="utf-8") as fh:
                m = json.load(fh)
            m.pop("written_utc", None)
            (m.get("args") or {}).pop("out", None)        # the two runs write two folders
            out[fn] = hashlib.sha1(json.dumps(m, sort_keys=True).encode()).hexdigest()
        else:
            with open(p, "rb") as fh:
                out[fn] = hashlib.sha1(fh.read()).hexdigest()
    return out


def test_full(work):
    freeze_fbx_clock()
    for key in ("tr", "dq"):
        st = L.STYLES[key]
        name = f"{st.base}{st.letter}"
        states = []
        for k in (1, 2):
            out = os.path.join(work, f"full{k}", "chairs", "M")
            run_main(["--style", key, "--out", out, "--finish", "--lods", "--verify"])
            states.append(folder_state(os.path.join(out, name), name))
        a, b = states
        diff = sorted(f for f in set(a) | set(b) if a.get(f) != b.get(f))
        print(f"[full] {key}: {len(a)} files, differing {diff or 'none'}")
        check(not diff and len(a) == 9, f"full {key}: files differ {diff} ({len(a)} files)")
        with open(os.path.join(work, "full1", "chairs", "M", name, f"{name}.manifest.json"),
                  encoding="utf-8") as fh:
            m = json.load(fh)
        check(m.get("validation", {}).get("errors") == 0 and "finish" in m.get("contracts", {}),
              f"full {key}: manifest lacks validation / C12 ({m.get('validation')})")
        print(f"[full] {key}: tris {m.get('tris')}, {m['contracts']['finish']['C12']}, "
              f"density median {m.get('density', {}).get('median')} px/m")
    # fg6l = the shipped round-L recipe: PNGs + settings.json byte-identical to chairs/L
    out = os.path.join(work, "fg6l", "chairs", "L")
    run_main(["--style", "fg6l", "--out", out, "--finish", "--lods"])
    ship = os.path.join(L.REPO_ROOT, "assets", "import", "chairs", "L", "SkiLiftChair6L")
    if not os.path.isdir(ship):
        print("[full] fg6l: SKIP shipped compare (assets/import/chairs/L/SkiLiftChair6L absent)")
        return
    files = [f"SkiLiftChair6L_{m}.png" for m in ("BaseColor", "Normal", "MaskMap", "ControlMask")]
    files.append("settings.json")
    a = folder_state(os.path.join(out, "SkiLiftChair6L"), "SkiLiftChair6L")
    b = folder_state(ship, "SkiLiftChair6L")
    diff = [f for f in files if a.get(f) != b.get(f)]
    print(f"[full] fg6l: {len(files) - len(diff)}/{len(files)} files == shipped chairs/L"
          + (f", differing {diff}" if diff else ""))
    check(not diff, f"full fg6l: differs from shipped chairs/L in {diff}")


def test_split(work):
    """2f. Carrier split (2026-11-03, docs/research/carrier-pivot-swing-plan-2026-11-03.md): tr / dq
    --split --verify into a temp folder. Each of the four manifests (body + grip per style) carries
    S1 rejoin <= 2 mm, S2 pin gap in (0, 5] mm and S3 0 clashes over the sweep, the pair contracts on
    LOD0 / LOD1 / LOD2 + C10 silhouettes, 0 validation errors, a re-import OK on all 3 levels, the
    partner's name, and no absolute home-folder path in its args (tools/check-no-user-paths.js)."""
    import carrier_split as CS
    home = os.path.normcase(os.path.realpath(os.path.expanduser("~"))).replace("\\", "/")
    out = []
    for key in ("tr", "dq"):
        st = L.STYLES[key]
        dst = os.path.join(work, "split", "chairs", L.SPLIT_LETTER)
        run_main(["--style", key, "--split", "--verify", "--out", dst])
        names = dict(zip(("body", "grip"), L.split_names(st, L.SPLIT_LETTER)))
        for role, name in names.items():
            with open(os.path.join(dst, name, f"{name}.manifest.json"), encoding="utf-8") as fh:
                m = json.load(fh)
            sp = m.get("split") or {}
            s1, s23 = sp.get("S1_rejoin") or {}, sp.get("S2_S3_sweep") or {}
            rj = max(s1.get("max_old_to_new_mm", 99.0), s1.get("max_new_to_old_mm", 99.0))
            check(sp.get("role") == role and sp.get("partner") == names["grip" if role == "body" else "body"],
                  f"split {key} {name}: role {sp.get('role')} / partner {sp.get('partner')}")
            check(rj <= CS.REJOIN_TOL_M * 1000.0, f"split {key} {name}: S1 rejoin {rj} mm")
            check(0.0 < s23.get("gap_mm_min", 0.0) and s23.get("gap_mm_max", 99.0) <= CS.GAP_MAX_M * 1000.0
                  and s23.get("clash_poses") == 0,
                  f"split {key} {name}: S2 gap {s23.get('gap_mm_min')}..{s23.get('gap_mm_max')} mm, S3 "
                  f"{s23.get('clash_poses')} clashes")
            check(s23.get("roll_deg") == [0.0, 0.0]
                  and s23.get("fore_aft_deg") == [-CS.FORE_AFT_DEG, CS.FORE_AFT_DEG],
                  f"split {key} {name}: swing convention, pin sweep fore-aft {s23.get('fore_aft_deg')}, "
                  f"roll {s23.get('roll_deg')} (the sway rolls the whole assembly)")
            pc = sp.get("pair_contracts") or {}
            check(all(k in pc for k in ("LOD0", "LOD1", "LOD2", "silhouette")),
                  f"split {key} {name}: pair contracts {sorted(pc)}")
            ver = (m.get("verify") or {}).get("levels_ok") or []
            check(m.get("validation", {}).get("errors") == 0 and len(ver) == 3 and all(ver)
                  and len(m.get("tris", {})) == 3,
                  f"split {key} {name}: validation {m.get('validation')}, re-import {ver}, tris {m.get('tris')}")
            leak = {k: v for k, v in (m.get("args") or {}).items() if isinstance(v, str)
                    and os.path.normcase(v).replace("\\", "/").startswith(home)}
            check(not leak, f"split {key} {name}: home-folder paths in the manifest args {leak}")
            al = sp.get("alignment")
            if key == "tr":
                check(bool(al) and al.get("plan_offset_mm", 99.0) < L.SPLIT_ALIGN_PLAN_MAX_M * 1000.0
                      and al.get("pin_plan_offset_mm", 99.0) < L.SPLIT_ALIGN_PLAN_MAX_M * 1000.0
                      and al.get("axis_angle_deg", 99.0) <= L.SPLIT_ALIGN_AXIS_MAX_DEG
                      and al.get("cut_ring_tilt_deg", 99.0) <= L.SPLIT_ALIGN_AXIS_MAX_DEG
                      and al.get("pin_off_axis_mm", 99.0) <= L.SPLIT_ALIGN_AXIS_OFFSET_MAX_M * 1000.0,
                      f"split tr {name}: clevis alignment {al}")
            else:
                check(al is None, f"split {key} {name}: alignment audit is tr-only, got {al}")
            out.append(f"{name} {'/'.join(str(t) for t in m['tris'].values())}")
    print(f"[split] chairs: {', '.join(out)}")
    return out


def test_split_alignment():
    """2g. tr split rounds 3-4 (owner verdicts 2026-11-03): the clevis alignment audit passes on the built
    parts and RAISES (ALIGN tr BROKEN) when the clevis drifts 6 mm off the jaw center in plan or the
    hanger tube's end tilts 2 deg about the pin. Also (round 4): the split "grip" is the long plate on
    the rope (0.68 m, underside on the rope's top, crown at GRIP_TOP_Z) + the clamp box, no "gripjaw"
    part; the one-piece FG_GRIP saddle is untouched."""
    import mathutils
    L.clear_scene()
    st, row = L.STYLES["tr"], L.CARRIERS["tr"]
    body, grip, old, joint = L.build_split_parts(st, row)
    L._remove_objects([o for o, _q in old])
    rep = L.split_alignment("tr", body, grip, joint)
    check(rep["plan_offset_mm"] < 0.01 and rep["axis_angle_deg"] < 0.01 and rep["pin_off_axis_mm"] < 0.01,
          f"tr split alignment as built: {rep}")
    by = {o.name: o for o, _q in body + grip}
    G = np.array([tuple(by["grip"].matrix_world @ v.co) for v in by["grip"].data.vertices])
    sad = [x for x, _z in L.FG_GRIP["saddle"]]
    plate_bottom = min(z for _x, z in L.TR_SPLIT_GRIP["plate"])
    check("gripjaw" not in by and abs(float(np.ptp(G[:, 0])) - 0.68) < 1e-6
          and abs(float(G[:, 2].max()) - L.GRIP_TOP_Z) < 1e-6 and abs(plate_bottom - 0.024) < 1e-9
          and abs((max(sad) - min(sad)) - 0.68) < 1e-6,
          f"round-4 grip: length {np.ptp(G[:, 0]):.3f} m (0.68), top {G[:, 2].max():.4f} ({L.GRIP_TOP_Z}), "
          f"plate underside {plate_bottom} (rope top 0.024), parts {sorted(by)}; one-piece saddle "
          f"{max(sad) - min(sad):.3f} (0.68)")

    def broken(what, reason):
        try:
            L.split_alignment("tr", body, grip, joint)
        except RuntimeError as e:
            ok = "ALIGN tr BROKEN" in str(e) and reason in str(e)
            check(ok, f"{what}: raised the wrong error {str(e)[:160]}")
            if ok:
                print(f"[chair-test] ok {what} raised: {str(e)[:110]}")
            return
        check(False, f"{what} did not raise")

    clevis = [by[n] for n in by if n == "gripbridge" or n.startswith(("gripcheek_", "grippin_"))]
    for o in clevis:
        o.location.x += 0.006
    broken("ALIGN: clevis 6 mm fore of the jaw block center", "in plan")
    for o in clevis:
        o.location.x -= 0.006
    stem = by["tube_stem"]
    keep = stem.matrix_world.copy()
    pin = mathutils.Vector(tuple(float(c) for c in joint["pin"]))
    stem.matrix_world = (mathutils.Matrix.Translation(pin) @ mathutils.Matrix.Rotation(math.radians(2.0), 4, "Y")
                         @ mathutils.Matrix.Translation(-pin) @ keep)
    broken("ALIGN: hanger tube end tilted 2 deg about the pin", "off the clevis axis")
    stem.matrix_world = keep
    rep2 = L.split_alignment("tr", body, grip, joint)
    check(rep2 == rep, f"tr split alignment restored: {rep2}")
    L.clear_scene()
    return rep


def test_revision1c(work):
    """Revision 1c (revision1b-check-dq-grip): the dq carriage's big faces shade FLAT - every
    corner normal of its axis-aligned faces within L.FLAT_FACE_MAX_DEG of the face normal, on
    the built LOD0 (C14) and on an exported + re-imported FBX (verify_export flat_box); tr has
    no flat part. The pre-1c build (FLAT_PARTS empty: one sharp edge per corner fan) must
    RAISE C14 and fail the FBX check. Returns the worst built / FBX deviations."""
    st, row, chair, _info = build("dq", "Rev1cDq")
    rep = L.flat_face_report(chair, ("gripbody",))
    k, worst = rep["gripbody"]
    check(k >= 6 and worst < L.FLAT_FACE_MAX_DEG,
          f"dq C14 carriage: {k} big faces, corner normals <= {worst:.3f} deg (< {L.FLAT_FACE_MAX_DEG})")
    res = L.check_contracts(chair, st, row, 0)
    check("C14" in res, f"dq LOD0 contracts carry C14 ({sorted(res)})")
    path = os.path.join(work, "Rev1cDq.fbx")
    box = L.fbx_box_of_part(chair, "gripbody")
    cs2_asset.export_fbx(chair, path)
    ok, info = L.verify_export(path, expect_height=float(row["hang_m"]), histogram=False, flat_box=box)
    fk, fworst = info["flat_faces"]["faces"], info["flat_faces"]["worst_deg"]
    check(ok and fk >= 6 and fworst < L.FLAT_FACE_MAX_DEG,
          f"dq FBX round trip: {fk} carriage big faces, corner normals <= {fworst:.3f} deg")
    check(not L.FLAT_PARTS.get("tr") and not L.FLAT_PARTS.get("fg6l"), "tr / fg6l carry no flat part")
    # the pre-1c carriage (the shipped 1b / 2 shading) must fail C14 and the FBX check
    saved = dict(L.FLAT_PARTS)
    L.FLAT_PARTS.clear()
    try:
        st, row, bad, _i = build("dq", "Rev1cDqOld")
    finally:
        L.FLAT_PARTS.update(saved)
    old = L.flat_face_report(bad, ("gripbody",))["gripbody"][1]
    raises(lambda: L.check_contracts(bad, st, row, 0),
           f"dq C14: the pre-1c carriage (worst {old:.1f} deg)")
    path = os.path.join(work, "Rev1cDqOld.fbx")
    box = L.fbx_box_of_part(bad, "gripbody")
    cs2_asset.export_fbx(bad, path)
    ok, info = L.verify_export(path, expect_height=float(row["hang_m"]), histogram=False, flat_box=box)
    check(not ok and info["flat_faces"]["worst_deg"] >= L.FLAT_FACE_MAX_DEG,
          f"pre-1c FBX fails the flat check ({info['flat_faces']})")
    print(f"[rev1c] dq carriage: built {worst:.3f} deg, FBX {fworst:.3f} deg over {fk} big faces "
          f"(pre-1c build {old:.1f} deg, FBX {info['flat_faces']['worst_deg']:.1f} deg: C14 raises)")
    return worst, fworst


# --------------------------------------------------------------------------------------
# 2e. Nitpick A2: the dq grip side mirrored onto the outboard (DQ_OUTBOARD)
# --------------------------------------------------------------------------------------

def _parts_auth(obj):
    """{part name: sorted (n, 3) authoring vertices} of a built chair / proxy (rounded 1e-6 m)."""
    tbl = L.part_table(obj)
    a = L.authoring_coords(obj)
    out = {}
    for i, n in enumerate(tbl):
        vs = L._verts_of_parts(obj, {i + 1})
        v = np.round(a[vs], 6)
        out[n] = v[np.lexsort((v[:, 2], v[:, 1], v[:, 0]))]
    return out


def _sorted(v):
    v = np.round(v, 6)
    return v[np.lexsort((v[:, 2], v[:, 1], v[:, 0]))]


def test_nitpick_a2():
    """The shipped dq (DQ_OUTBOARD -1) vs the approved revision 3 (+1), both built here."""
    saved = L.DQ_OUTBOARD
    builds = {}
    try:
        for ob in (1.0, -1.0):
            L.DQ_OUTBOARD = ob
            st, row, chair, info = build("dq", f"A2Dq{'P' if ob > 0 else 'N'}")
            c13 = L.check_contracts(chair, st, row, 0)["C13"]
            proxy = L.build_lod2_proxy(st, row, chair, chair.name)
            builds[ob] = (dict(info["part_tris"]), _parts_auth(chair), c13, _parts_auth(proxy))
    finally:
        L.DQ_OUTBOARD = saved
    (tp, vp, c13p, xp), (tn, vn, c13n, xn) = builds[1.0], builds[-1.0]
    check(tp == tn, f"nitpick A2: per-part tris differ {[(k, tp[k], tn.get(k)) for k in tp if tp[k] != tn.get(k)]}")
    side = tuple(L.DQ_GRIP_SIDE_PARTS)
    mirrored, same, bad = [], [], []
    M = np.array([1.0, -1.0, 1.0])
    for n in sorted(vp):
        if n.startswith(side):
            ok = vp[n].shape == vn[n].shape and np.allclose(_sorted(vp[n] * M), vn[n], atol=2e-6)
            (mirrored if ok else bad).append(n)
        else:
            ok = vp[n].shape == vn[n].shape and np.allclose(vp[n], vn[n], atol=2e-6)
            (same if ok else bad).append(n)
    check(not bad, f"nitpick A2: parts neither identical (symmetric group) nor exact mirrors (grip side): {bad}")
    check(len(mirrored) == 13, f"nitpick A2: {len(mirrored)} grip-side parts mirrored (want 13: {mirrored})")
    hv = vn["tube_hanger"]
    run = hv[(hv[:, 2] > 1.8) & (hv[:, 2] < 3.0)]
    check(float(run[:, 1].max()) < -0.15 and float(hv[:, 1].max()) <= 0.07 + 1e-6,
          f"nitpick A2: the hanger's vertical run y {run[:, 1].min():.3f}..{run[:, 1].max():.3f} not on -Y")
    check(c13p == c13n, f"nitpick A2: C13 differs between the sides:\n  +1 {c13p}\n  -1 {c13n}")
    pm = [n for n in sorted(xp) if n.startswith(side)]
    pbad = [n for n in sorted(xp) if not (np.allclose(_sorted(xp[n] * M), xn[n], atol=2e-6) if n in pm
                                           else np.allclose(xp[n], xn[n], atol=2e-6))]
    check(pm == ["gripbody", "tube_hanger"] and not pbad,
          f"nitpick A2: LOD2 proxy mirrored {pm}, mismatched {pbad}")
    msg = (f"{len(mirrored)} grip-side parts exact mirrors, {len(same)} symmetric parts identical, hanger run y "
           f"{run[:, 1].min():.3f}..{run[:, 1].max():.3f}, C13 equal, LOD2 grip box + hanger mirrored")
    print(f"[a2] dq: {msg}")
    return msg


def main():
    t0 = time.perf_counter()
    work = tempfile.mkdtemp(prefix="cs2_chair_styles_")
    try:
        det = test_determinism()
        con = test_contracts(work)
        test_revision1()
        test_revision3()
        test_revision1c(work)
        a2 = test_nitpick_a2()
        trace = test_trace()
        split = test_split(work)
        align = test_split_alignment()
        if FULL:
            test_full(work)
    except Exception:  # noqa: BLE001
        import traceback
        traceback.print_exc()
        FAILS.append("exception (see traceback)")
    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_chair_styles: {len(FAILS)} failures ({dt:.1f} s); work dir {work}")
        sys.exit(1)
    shutil.rmtree(work, ignore_errors=True)
    print(f"PASS test_chair_styles: determinism {', '.join(sorted(det))}; contracts C1-C11 on "
          f"every level + dq C13 (tr {'/'.join(con['tr'])}, dq {'/'.join(con['dq'])}); Revision 1 "
          f"verdict geometry ok; Revision 3 bar from the rider photos ok (C13 with riders, 7 wrong builds "
          f"raise); Revision 1c "
          f"flat carriage ok (C14 built + FBX, the pre-1c shading raises); nitpick A2 grip side outboard "
          f"(DQ_OUTBOARD {L.DQ_OUTBOARD:+.0f}): {a2}; carrier split {', '.join(split)} (S1 / S2 / S3, "
          f"pair contracts, no home paths); tr clevis alignment {align['plan_offset_mm']} mm / "
          f"{align['axis_angle_deg']} deg (a 6 mm drift and a 2 deg tilt raise); trace {trace}"
          f"{'; full builds byte-identical, fg6l == shipped chairs/L' if FULL else ''} ({dt:.1f} s)")


main()
