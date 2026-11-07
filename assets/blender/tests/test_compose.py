"""
test_compose.py - unit tests for cs2_compose (round L spec "cs2_compose.py").

Run the tests with this command:

    blender --background --factory-startup --python tests\\test_compose.py

The test exits with code 1 on any failure. It prints one PASS line on success. The test is small: no
bakes, a 2-quad mesh, 64..256 px arrays.

(1) ridge -> height_to_normal: GL sign. Buffers are bottom-up (row 0 = v 0), so np.gradient's row
derivative IS dh/dv. The unnormalized normal of a height field is (-dh/du, -dh/dv, 1). Encoded G =
0.5 + 0.5*ny = 0.5 - k*dh/dv (k > 0). A ridge has two flanks:
- The flank RISING toward +V (rows below the crest, dh/dv > 0) has G < 0.5. This is the "+V-facing
  slope" of the task text, i.e. the slope you climb walking toward +V.
- The flank FALLING toward +V (rows above the crest, dh/dv < 0) has its NORMAL tilted toward +V, so
  G > 0.5. A light coming from +V brightens it (spec line 102).

The test asserts both flanks. It also asserts the lighting check, R == 0.5 (no du gradient), B > 0.5
and unit length.

(2) blend_rnm(flat, flat) == flat, and flat is the identity on either side.

(3) detail_height on a 2-quad mesh: quad 1 at 50 px/m gets 1.2 m seams at the expected texel
columns. Quad 2 (identical geometry, ~1 px/m) is below the 2-texel floor and stays zero.

(4) compose_maps keeps reserved rects byte-identical in all four maps and ships ControlMask A == 0
(and MaskMap B == 0, BaseColor A == 1).

Detail pass 1 (towers), design/detail-pass-1-towers.md section 2.6 (+ skeptic M3):
- TC1 defaults byte-identical: a legacy-only fixture and the shipped lift_tower hook on the FG8
  review pack LOD0 hash the same with this module and the round-L module (git REF_COMMIT, loaded
  side by side). The fixture also matches recorded golden sha1s.
- TC2 edge rounding checks these points: crease tilt >= 35 deg, 0 beyond r + 1 texel, mirror
  symmetry across an in-strip crease, no rounding of open prism ends and concave creases, and a
  logged floor skip.
- TC3 marks check these points: plate on its face only (not a same-facing face 0.2 m behind, not the
  opposed back face), bolt grid count, slot, ring / disc, boltring count + phase, band continuous
  across 16 facets, and a logged floor skip for every kind.
- TC4 tone checks these points: mean, std = MOTTLE_STD, world anchoring, 1 m decorrelation, chalk
  only above n.z 0.2, stretch correlation ratio, and floor skip. It also checks compose_maps tone_l
  / tone_s.
- TC5 decal checks these points: "AB" reads as authored on a mirrored-UV plane and on a 16-gon
  cylinder, AA edges, ControlMask R/G 0 under solid paint, and returned rects that cover every
  changed texel. M3 compares per-part plate rects (plate_n / plate_p) with the prefix union.
- TC6 determinism: two runs and a child Blender process hash identically.

TC1 reads the round-L module from tests/data/cs2_compose_roundL.py (sha1-pinned). It takes about 5
s.
"""

import json
import math
import os
import sys
import traceback

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import bpy  # noqa: E402

import cs2_asset  # noqa: E402
import cs2_compose as C  # noqa: E402


FAILS = []


def check(cond, msg):
    if cond:
        print(f"  ok   {msg}")
    else:
        print(f"  FAIL {msg}")
        FAILS.append(msg)


# --------------------------------------------------------------------------------------
# (1) height_to_normal sign
# --------------------------------------------------------------------------------------

def test_height_to_normal_sign():
    print("[1] height_to_normal: ridge, GL sign")
    H = W = 32
    crest, half, amp, ppm = 16, 8, 0.05, 100.0
    rows = np.arange(H)
    prof = np.clip(1.0 - np.abs(rows - crest) / half, 0.0, 1.0) * amp   # tent along v
    h = np.repeat(prof[:, None], W, axis=1).astype(np.float32)
    n = C.height_to_normal(h, ppm)
    check(n.shape == (H, W, 3), f"shape {n.shape}")
    rising = n[crest - half + 1:crest - 1]     # dh/dv > 0
    falling = n[crest + 2:crest + half]        # dh/dv < 0
    check(np.all(rising[..., 1] < 0.5 - 1e-3),
          f"flank rising toward +V (dh/dv>0) has G < 0.5 (max {rising[..., 1].max():.3f})")
    check(np.all(falling[..., 1] > 0.5 + 1e-3),
          f"flank falling toward +V (normal tilts +V) has G > 0.5 "
          f"(min {falling[..., 1].min():.3f})")
    check(np.allclose(n[..., 0], 0.5, atol=1e-6), "R == 0.5 with no du gradient")
    check(np.all(n[..., 2] > 0.5), "B > 0.5 everywhere")
    dec = n * 2.0 - 1.0
    check(np.allclose(np.linalg.norm(dec, axis=-1), 1.0, atol=1e-4), "unit length")
    # expected magnitude: slope = amp/half m per texel * ppm = 0.625 -> ny = 0.625/sqrt(1+0.39)
    slope = amp / half * ppm
    ny = slope / math.sqrt(1.0 + slope * slope)
    check(abs(falling[3, 0, 1] - (0.5 + 0.5 * ny)) < 1e-3,
          f"G magnitude on the falling flank {falling[3, 0, 1]:.4f} == {0.5 + 0.5 * ny:.4f}")
    # lighting proof (spec line 102): light from +V brightens the flank whose normal
    # tilts toward +V.  Tangent-space light L from +V, elevated 30 deg.
    L = np.array([0.0, math.cos(math.radians(30)), math.sin(math.radians(30))])
    lit = np.einsum("ijk,k->ij", dec, L)
    check(lit[crest + 2:crest + half].mean() > lit[crest - half + 1:crest - 1].mean() + 0.1,
          f"light from +V brightens the +V-tilted flank "
          f"({lit[crest + 2:crest + half].mean():.3f} vs {lit[crest - half + 1:crest - 1].mean():.3f})")
    # per-texel ppm array accepted
    n2 = C.height_to_normal(h, np.full((H, W), ppm, np.float32))
    check(np.allclose(n, n2), "ppm as (H, W) array matches scalar")
    # strength scales the slope
    n3 = C.height_to_normal(h, ppm, strength=0.5)
    check(n3[crest + 3, 0, 1] < n[crest + 3, 0, 1], "strength 0.5 flattens")


# --------------------------------------------------------------------------------------
# (2) blend_rnm
# --------------------------------------------------------------------------------------

def test_blend_rnm():
    print("[2] blend_rnm")
    flat = np.tile(np.array(C.FLAT_NORMAL, np.float32), (8, 8, 1))
    out = C.blend_rnm(flat, flat)
    check(out.shape == (8, 8, 3), f"shape {out.shape}")
    check(np.allclose(out, flat, atol=1e-6), "flat + flat == flat")
    rng = np.random.default_rng(3)
    v = rng.normal(size=(8, 8, 3)); v[..., 2] = np.abs(v[..., 2]) + 0.5
    v /= np.linalg.norm(v, axis=-1, keepdims=True)
    enc = (v * 0.5 + 0.5).astype(np.float32)
    check(np.allclose(C.blend_rnm(flat, enc), enc, atol=1e-5), "flat base is identity")
    check(np.allclose(C.blend_rnm(enc, flat), enc, atol=1e-5), "flat detail is identity")
    d = C.blend_rnm(enc, enc) * 2 - 1
    check(np.allclose(np.linalg.norm(d, axis=-1), 1.0, atol=1e-4), "unit output")
    four = np.concatenate([flat, np.ones((8, 8, 1), np.float32)], axis=-1)
    check(np.allclose(C.blend_rnm(four, four), flat, atol=1e-6), "RGBA inputs accepted")


# --------------------------------------------------------------------------------------
# (3) detail_height on a 2-quad mesh
# --------------------------------------------------------------------------------------

def build_two_quads(A):
    """Two 2.4 x 1.0 m wall quads (normal -Y) tagged wall_hi / wall_lo. wall_hi maps to a 120 x 50
    texel rect (50 px/m). wall_lo maps to a 3 x 2 texel rect (~1.1 px/m)."""
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    verts = [(0, 0, 0), (2.4, 0, 0), (2.4, 0, 1), (0, 0, 1),
             (0, 3, 0), (2.4, 3, 0), (2.4, 3, 1), (0, 3, 1)]
    faces = [(0, 1, 2, 3), (4, 5, 6, 7)]
    me = bpy.data.meshes.new("TwoQuads")
    me.from_pydata(verts, [], faces)
    me.update()
    obj = bpy.data.objects.new("TwoQuads", me)
    bpy.context.collection.objects.link(obj)
    uv = me.uv_layers.new(name="UVUnique")
    px = 1.0 / A
    hi = [(25 * px, 25 * px), (145 * px, 25 * px), (145 * px, 75 * px), (25 * px, 75 * px)]
    lo = [(200 * px, 200 * px), (203 * px, 200 * px), (203 * px, 202 * px), (200 * px, 202 * px)]
    for i, q in enumerate(hi + lo):
        uv.data[i].uv = q
    attr = me.attributes.new("part", "INT", "FACE")
    attr.data.foreach_set("value", [1, 2])
    table = ["wall_hi", "wall_lo"]
    me[cs2_asset.PART_TABLE_PROP] = json.dumps(table)
    return obj, me, table


def test_detail_height():
    print("[3] detail_height: seams at meter pitch, 2-texel floor")
    A = 256
    obj, me, table = build_two_quads(A)
    specs = {"wall": C.DetailSpec(seams_m=1.2, seam_depth_m=0.002, seam_axes="ribs")}
    h = C.detail_height(me, A, specs, uv_layer="UVUnique", dilate_px=0.0)
    check(h.shape == (A, A) and h.dtype == np.float32, f"shape {h.shape} {h.dtype}")
    # wall_hi: x = 0 .. 2.4 m spans cols 25 .. 145 (50 px/m). Seams at x = 0, 1.2, 2.4 -> cols 25,
    # 85, 145. Width max(0.012 m, 1 texel) = 1 texel, so the interior seam lands on the single
    # column whose center is within 0.5 texel of 85.0 (col 84 or 85).
    band = h[30:70, :]
    cols = np.where(band.min(axis=0) < -1e-6)[0]
    check(len(cols) > 0, f"seam columns present: {cols.tolist()}")
    interior = [c for c in cols if 30 < c < 140]
    check(all(abs(c - 85) <= 1 for c in interior) and len(interior) >= 1,
          f"interior seam at col ~85 (found {interior})")
    check(np.allclose(band[:, 40:80], 0.0) and np.allclose(band[:, 90:140], 0.0),
          "no seams between pitches")
    check(np.isclose(band[:, interior].min(), -0.002), f"seam depth {band[:, interior].min()}")
    check(np.all(h[25:75, 25:145] <= 0.0), "seams are recessed (<= 0)")
    # wall_lo: pitch 1.2 m at ~1.1 px/m = 1.3 texels < 2 -> skipped, nothing drawn
    check(np.allclose(h[195:210, 195:210], 0.0), "2-texel floor: low-density quad untouched")
    # rings variant: seams of constant z at 1.2 m pitch -> only z = 0 (row 25) inside a 1 m
    # tall wall: no interior rows
    specs_r = {"wall": C.DetailSpec(seams_m=0.5, seam_axes="rings")}
    hr = C.detail_height(me, A, specs_r, dilate_px=0.0)
    rows = np.where(hr[:, 60:130].min(axis=1) < -1e-6)[0]
    inner = [r for r in rows if 30 < r < 70]
    check(len(inner) >= 1 and all(abs(r - 50) <= 1 for r in inner),
          f"rings: interior seam row ~50 (z = 0.5 m) found {inner}")
    # Bolts (dia 0.06 m = 3 px) and corrugation (half period 0.05 m = 2.5 px) are above the floor at
    # 50 px/m, so they get drawn. Bolt domes reach +bolt_h. The sine reaches +-depth.
    specs_b = {"wall_hi": C.DetailSpec(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.03)}
    hb = C.detail_height(me, A, specs_b, dilate_px=1.0)
    region = hb[25:75, 25:145]
    check(abs(region.max() - 0.004) < 5e-4, f"bolt domes reach +0.004 ({region.max():.4f})")
    check(abs(region.min() + 0.002) < 1e-6, f"seams stay -0.002 beside bolts ({region.min():.4f})")
    specs_c = {"wall_hi": C.DetailSpec(corrugation_m=0.10)}
    hc = C.detail_height(me, A, specs_c, dilate_px=1.0)
    region = hc[25:75, 25:145]
    check(region.min() <= -0.0018 and region.max() >= 0.0018,
          f"corrugation spans +-0.002 ({region.min():.4f}..{region.max():.4f})")
    # stacking: a bolt on a corrugation crest sits on top of it (bolt_h + corr_depth)
    hs = C.detail_height(me, A, {"wall_hi": C.DetailSpec(bolt_pitch_m=0.15, bolt_r_m=0.03,
                                                          corrugation_m=0.10)}, dilate_px=1.0)
    check(hs.max() <= 0.006 + 1e-6, f"bolt + corrugation stack <= 0.006 ({hs.max():.4f})")
    # corrugation at 0.076 m (half period 1.9 px) is below the floor at 50 px/m
    hc = C.detail_height(me, A, {"wall_hi": C.DetailSpec(corrugation_m=0.076)},
                         dilate_px=0.0)
    check(np.allclose(hc, 0.0), "2-texel floor: 0.076 m corrugation skipped at 50 px/m")
    # rasterise_face_fields agrees on coverage and density
    pm, dm = C.rasterise_face_fields(me, A)
    check(pm[50, 80] == 1 and pm[201, 201] == 2 and pm[10, 10] == 0, "part_map coverage")
    check(abs(dm[50, 80] - 50.0) < 0.5, f"ppm_map wall_hi {dm[50, 80]:.2f} ~ 50 px/m")
    # stamp_on_face: the painter paints half the rect. ctl R is zeroed only there.
    base = np.full((A, A, 4), 0.6, np.float32)
    ctl = np.zeros((A, A, 4), np.float32); ctl[..., 0] = 1.0

    def painter(rect, b, c):
        r0, r1, c0, c1 = rect
        b[:, :(c1 - c0) // 2, :3] = 0.1

    C.stamp_on_face(base, ctl, me, table, "wall_hi", painter)
    check(ctl[30, 30, 0] == 0.0 and ctl[30, 140, 0] == 1.0 and ctl[10, 10, 0] == 1.0,
          "stamp_on_face zeroes ctl R under painted texels only")
    check(np.all(base[..., 3] == 1.0) or np.all(base[25:75, 25:145, 3] == 1.0),
          "stamp_on_face forces alpha 1 in the rect")


# --------------------------------------------------------------------------------------
# (4) compose_maps
# --------------------------------------------------------------------------------------

def test_compose_maps():
    print("[4] compose_maps: reserved rects untouched, ctl A == 0")
    A = 64
    rng = np.random.default_rng(7)
    base = rng.uniform(0.1, 0.9, (A, A, 4)).astype(np.float32)
    nrm = rng.uniform(0.3, 0.7, (A, A, 4)).astype(np.float32); nrm[..., 2] = 0.9
    msk = rng.uniform(0.0, 1.0, (A, A, 4)).astype(np.float32)
    ctl = rng.uniform(0.0, 1.0, (A, A, 4)).astype(np.float32)
    ao = rng.uniform(0.0, 1.0, (A, A)).astype(np.float32)
    height = rng.uniform(-0.002, 0.002, (A, A)).astype(np.float32)
    part_map = (rng.integers(0, 4, (A, A))).astype(np.int32)
    reserved = [(0.0, 0.75, 1.0, 1.0), (0.5, 0.25, 0.75, 0.5)]
    inputs = [x.copy() for x in (base, nrm, msk, ctl)]
    b, n, m, c = C.compose_maps(base, nrm, msk, ctl, ao, height, 50.0,
                                {"reserved": reserved, "part_map": part_map,
                                 "convex": np.zeros((A, A)), "gloss_var": 0.03})
    check(all(x.shape == (A, A, 4) for x in (b, n, m, c)), "four RGBA outputs")
    check(all(np.array_equal(x, y) for x, y in zip(inputs, (base, nrm, msk, ctl))),
          "inputs not modified")
    for r in reserved:
        sl = C._rect_slices(r, A)
        check(np.array_equal(b[sl], base[sl]) and np.array_equal(n[sl], nrm[sl])
              and np.array_equal(m[sl], msk[sl])
              and np.array_equal(c[sl][..., :3], ctl[sl][..., :3]),
              f"reserved rect {r} untouched in all maps")
    check(np.all(c[..., 3] == 0.0), "ControlMask A == 0 everywhere")
    free = (slice(0, 16), slice(0, 32))
    check(np.all(m[free][..., 2] == 0.0), "MaskMap B == 0 outside reserved")
    check(np.all(b[free][..., 3] == 1.0), "BaseColor A == 1 outside reserved")
    check(not np.array_equal(b[free], base[free]), "free area was composited (AO/noise)")
    check(not np.array_equal(n[free], nrm[free]), "free area normal got the detail blend")
    occ = ao[free] * 0.92 + 0.08
    # with part noise +-3 % the ratio base_out/base_in stays within occ*(1+-0.03)
    ratio = b[free][..., 0] / np.clip(base[free][..., 0], 1e-6, None)
    check(np.all(ratio <= occ * 1.03 + 1e-4) and np.all(ratio >= occ * 0.97 - 1e-4),
          "AO occ = ao*0.92+0.08 (with +-3 % part noise)")
    # deterministic
    b2, n2, m2, c2 = C.compose_maps(base, nrm, msk, ctl, ao, height, 50.0,
                                    {"reserved": reserved, "part_map": part_map,
                                     "convex": np.zeros((A, A)), "gloss_var": 0.03})
    check(np.array_equal(b, b2) and np.array_equal(m, m2), "deterministic for a fixed seed")
    # no AO, no height: normal passes through unchanged outside reserved
    b3, n3, m3, c3 = C.compose_maps(base, nrm, msk, ctl, None, None, 50.0,
                                    {"gloss_var": 0.0})
    check(np.allclose(n3[..., :3], nrm[..., :3]), "None ao/height: normal untouched")


# ======================================================================================
# Detail pass 1 (towers): TC1-TC6 (design/detail-pass-1-towers.md section 2.6)
# ======================================================================================

import bmesh  # noqa: E402
import contextlib  # noqa: E402
import hashlib  # noqa: E402
import importlib.util  # noqa: E402
import io  # noqa: E402
import subprocess  # noqa: E402
import tempfile  # noqa: E402

REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir, os.pardir))
#: the last commit of the round-L cs2_compose.py (the "before the change" module) and the
#: sha1 of its bytes. TC1 runs it side by side with the current module
REF_COMMIT = "e0b20681a2ca80be2889d170e1275f7abf52369f"
REF_FILE_SHA1 = "fc0d17b750e207aa5e20ef7b2c39f07ee1dbfd9d"
REF_FILE = os.path.join(HERE, "data", "cs2_compose_roundL.py")
FG8_PACK = os.path.join(REPO, "assets", "preview", "review", "SkiLiftTowerFG8O.blend")
#: TC1(a) golden sha1s, recorded 2026-10-11 with the round-L module (REF_COMMIT)
GOLDEN_TC1 = {
    "h_dilate1": "a54eef78c4825858bd6853a7b2cb542248de5264",
    "h_dilate0": "491cea5e8ec04effc7f1c681f79ac6336e9cef5b",
    "fields": "2f34fd8eb6619de75d699ccb4ac8d7101e7567ad",
    "compose": "9f83b9402c351c165cbd04e1582491ee93107914",
    "stamp": "62568791ccdfefcb7ffc164c71e4d53a2eb537e8",
}


def sha1_arrays(*arrs):
    h = hashlib.sha1()
    for a in arrs:
        a = np.ascontiguousarray(a)
        h.update(str(a.dtype).encode())
        h.update(str(a.shape).encode())
        h.update(a.tobytes())
    return h.hexdigest()


def quiet(fn, *a, **k):
    """Call fn with stdout captured. Return (result, captured text)."""
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        out = fn(*a, **k)
    return out, buf.getvalue()


def mesh_from(name, verts, faces, face_uvs_px, part_ids, table, A):
    """Welded mesh (shared vertex indices) with a UVUnique layer given per face in
    TEXELS, the INT face attribute "part" and the json part table."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(float(c) for c in v) for v in verts], [],
                   [tuple(int(i) for i in f) for f in faces])
    me.update()
    uv = me.uv_layers.new(name="UVUnique")
    for poly, quv in zip(me.polygons, face_uvs_px):
        assert len(quv) == poly.loop_total, (name, poly.index)
        for li, (x, y) in zip(poly.loop_indices, quv):
            uv.data[li].uv = (x / A, y / A)
    attr = me.attributes.new("part", "INT", "FACE")
    attr.data.foreach_set("value", [int(p) for p in part_ids])
    me[cs2_asset.PART_TABLE_PROP] = json.dumps(list(table))
    return me


def load_reference_compose():
    """The round-L cs2_compose (REF_COMMIT) as module cs2_compose_ref, read from tests/data. The
    history was rewritten 2026-10-22, so `git show REF_COMMIT` no longer resolves. The fixture is
    byte-identical to the module."""
    with open(REF_FILE, "rb") as fh:
        src = fh.read()
    got = hashlib.sha1(src).hexdigest()
    if got != REF_FILE_SHA1:
        raise RuntimeError(f"reference cs2_compose sha1 {got} != {REF_FILE_SHA1}")
    path = os.path.join(tempfile.mkdtemp(prefix="cs2ref"), "cs2_compose_ref.py")
    with open(path, "wb") as fh:
        fh.write(src)
    spec = importlib.util.spec_from_file_location("cs2_compose_ref", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    assert not hasattr(mod, "detail_tone")
    return mod


# -- fixture geometry -------------------------------------------------------------------

BOX_V = [(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0), (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)]
BOX_SIDES = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]   # strip order, a->b along
BOX_TOP = (4, 5, 6, 7)
BOX_BOT = (3, 2, 1, 0)


def box_uvs(sx, sy, tx, ty, bx, by, s=64):
    """Side strip (4 contiguous faces from (sx, sy)), top cap at (tx, ty), bottom at (bx, by)."""
    uvs = [[(sx + s * k, sy), (sx + s * (k + 1), sy), (sx + s * (k + 1), sy + s), (sx + s * k, sy + s)]
           for k in range(4)]
    uvs.append([(tx, ty), (tx + s, ty), (tx + s, ty + s), (tx, ty + s)])
    uvs.append([(bx, by), (bx + s, by), (bx + s, by + s), (bx, by + s)])
    return uvs


def cylinder(n, r, z0, z1, ppm, X0, Y0, offset=(0.0, 0.0)):
    """n-gon open cylinder (sides only) with a contiguous metric UV strip."""
    ox, oy = offset
    verts = [(ox + r * math.cos(2 * math.pi * i / n), oy + r * math.sin(2 * math.pi * i / n), z)
             for z in (z0, z1) for i in range(n)]
    faces = [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    fw = 2 * r * math.sin(math.pi / n) * ppm
    hh = (z1 - z0) * ppm
    uvs = [[(X0 + i * fw, Y0), (X0 + (i + 1) * fw, Y0), (X0 + (i + 1) * fw, Y0 + hh), (X0 + i * fw, Y0 + hh)]
           for i in range(n)]
    return verts, faces, uvs, fw


def wall(x0, x1, y, z0, z1, u0, v0, ppm):
    """Quad in the plane y = const facing -y, UV x -> u, z -> v (texels)."""
    v = [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)]
    uv = [(u0, v0), (u0 + (x1 - x0) * ppm, v0), (u0 + (x1 - x0) * ppm, v0 + (z1 - z0) * ppm),
          (u0, v0 + (z1 - z0) * ppm)]
    return v, uv


def count_blobs(m):
    """4-connected components of a bool array."""
    m = np.array(m, dtype=bool, copy=True)
    H, W = m.shape
    n = 0
    for i in range(H):
        for j in range(W):
            if not m[i, j]:
                continue
            n += 1
            m[i, j] = False
            stack = [(i, j)]
            while stack:
                a, b = stack.pop()
                for da, db in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    x, y = a + da, b + db
                    if 0 <= x < H and 0 <= y < W and m[x, y]:
                        m[x, y] = False
                        stack.append((x, y))
    return n


def corr(a, b):
    a = np.asarray(a, np.float64).ravel() - np.mean(a)
    b = np.asarray(b, np.float64).ravel() - np.mean(b)
    d = math.sqrt(float(a @ a) * float(b @ b))
    return float(a @ b) / d if d > 0 else 0.0


# --------------------------------------------------------------------------------------
# TC1 defaults byte-identical
# --------------------------------------------------------------------------------------

def tc1_fixture(A=256):
    """Legacy-feature fixture: seams + bolts wall, corrugated wall, helix rope, grate, an
    inactive lip.  Returns (me, table)."""
    verts, faces, uvs, parts = [], [], [], []

    def add(vs, fs, us, part):
        base = len(verts)
        verts.extend(vs)
        faces.extend([tuple(base + i for i in f) for f in fs])
        uvs.extend(us)
        parts.extend([part] * len(fs))

    v, uv = wall(0.0, 2.4, 0.0, 0.0, 1.0, 8, 8, 50.0)
    add(v, [(0, 1, 2, 3)], [uv], 1)
    v, uv = wall(0.0, 1.0, 2.0, 0.0, 1.0, 140, 8, 50.0)
    add(v, [(0, 1, 2, 3)], [uv], 2)
    cv, cf, cu, _fw = cylinder(8, 0.05, 0.0, 1.0, 100.0, 8, 70, offset=(4.0, 0.0))
    add(cv, cf, cu, 3)
    gv = [(0, 5, 3), (1, 5, 3), (1, 6, 3), (0, 6, 3)]
    add(gv, [(0, 1, 2, 3)], [[(60, 70), (120, 70), (120, 130), (60, 130)]], 4)
    v, uv = wall(6.0, 6.2, 0.0, 0.0, 0.2, 140, 70, 50.0)
    add(v, [(0, 1, 2, 3)], [uv], 5)
    table = ["wall_a", "corr_a", "rope_0", "grate_deck", "lip_0"]
    return mesh_from("TC1Fixture", verts, faces, uvs, parts, table, A), table


def tc1_specs(D, explicit_new=False):
    extra = dict(edge_round_m=None, marks=(), mottle_m=None, chalk_amp=0.0) if explicit_new else {}
    return {"wall": D(seams_m=0.5, bolt_pitch_m=0.15, bolt_r_m=0.03, **extra),
            "corr": D(corrugation_m=0.1, **extra), "rope": D(helix_m=0.1, **extra),
            "grate": D(grate_m=0.05, **extra), "lip": D(**extra)}


def tc1_hashes(M, me, table, specs, A=256, tone_none=False):
    out = {}
    h1 = M.detail_height(me, A, specs, verbose=False)
    h0 = M.detail_height(me, A, specs, dilate_px=0.0, verbose=False)
    pm, pp = M.rasterise_face_fields(me, A)
    out["h_dilate1"] = sha1_arrays(h1)
    out["h_dilate0"] = sha1_arrays(h0)
    out["fields"] = sha1_arrays(pm, pp)
    rng = np.random.default_rng(5)
    base = rng.uniform(0.1, 0.9, (A, A, 4)).astype(np.float32)
    nrm = rng.uniform(0.4, 0.6, (A, A, 4)).astype(np.float32)
    nrm[..., 2] = 0.95
    msk = rng.uniform(0.0, 1.0, (A, A, 4)).astype(np.float32)
    ctl = rng.uniform(0.0, 1.0, (A, A, 4)).astype(np.float32)
    ao = rng.uniform(0.3, 1.0, (A, A)).astype(np.float32)
    cx = rng.uniform(0.0, 1.0, (A, A)).astype(np.float32)
    cc = rng.uniform(0.0, 1.0, (A, A)).astype(np.float32)
    ct = rng.uniform(0.0, 1.0, (A, A)).astype(np.float32)
    opts = {"reserved": [(0.75, 0.75, 1.0, 1.0)], "part_map": pm, "seed": 3, "convex": cx,
            "concave": cc, "contact": ct, "gloss_var": 0.03, "gloss_cells": 8, "strength": 1.2}
    if tone_none:
        opts.update({"tone_l": None, "tone_s": None})
    b, n, m, c = M.compose_maps(base, nrm, msk, ctl, ao, h1, pp, opts)
    out["compose"] = sha1_arrays(b, n, m, c)

    def painter(rect, bs, cs):
        bs[: bs.shape[0] // 2, :, 0:3] = 0.2

    quiet(M.stamp_on_face, b, c, me, table, "wall_a", painter)
    out["stamp"] = sha1_arrays(b, c)
    return out


PRE_PASS_TOWER_BOLTS = dict(bolt_pitch_m=0.15, bolt_r_m=0.018)


def pre_pass_tower_specs(D):
    """lift_tower.tower_detail_specs() as shipped before detail pass 1 (2026-10-11)."""
    out = {"crossarm": D(**PRE_PASS_TOWER_BOLTS), "rocker_main_": D(**PRE_PASS_TOWER_BOLTS),
           "gantry_girder_": D(**PRE_PASS_TOWER_BOLTS), "grate_": D(grate_m=0.05)}
    for nm in ("catwalk", "deckp", "deckn", "landp", "landn"):
        out[f"grate_{nm}_lip"] = D()
    return out


def _pre_pass_warning_triangle(rect, base, ctl):
    """lift_tower.paint_warning_triangle as shipped before detail pass 1 (verbatim math)."""
    def lin(c):
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    r0, r1, c0, c1 = rect
    h, w = r1 - r0, c1 - c0
    T = int(round(0.62 * h))
    W = 1.12 * T
    cx = w / 2.0
    y0 = int(round(0.16 * h))
    white, dark = lin(0.92), lin(0.10)
    for row in range(T):
        half = W / 2.0 * (1.0 - row / T)
        cs, ce = int(round(cx - half)), int(round(cx + half))
        rr = y0 + row
        if 0 <= rr < h and ce > cs:
            base[rr, max(cs, 0):min(ce, w), 0:3] = white
            ctl[rr, max(cs, 0):min(ce, w), 0:2] = 0.0
    bw = max(1, int(round(0.05 * T)))
    for f0, f1 in ((0.30, 0.72), (0.10, 0.22)):
        a, b = y0 + int(round(T * f0)), y0 + int(round(T * f1))
        base[a:b, int(cx) - bw:int(cx) + bw, 0:3] = dark


def fg8_hook_hash(M, me, table, specs, A=1024):
    """lift_tower.make_post_compose_v2's cs2_compose calls on synthetic baked maps."""
    rng = np.random.default_rng(11)
    base = rng.uniform(0.2, 0.8, (A, A, 4)).astype(np.float32)
    nrm = rng.uniform(0.45, 0.55, (A, A, 4)).astype(np.float32)
    nrm[..., 2] = 0.97
    msk = rng.uniform(0.0, 1.0, (A, A, 4)).astype(np.float32)
    ctl = rng.uniform(0.0, 1.0, (A, A, 4)).astype(np.float32)
    pm, pp = M.rasterise_face_fields(me, A)
    h = M.detail_height(me, A, specs, verbose=False)
    b, n, m, c = M.compose_maps(base, nrm, msk, ctl, None, h, pp,
                                {"reserved": (), "part_map": pm, "seed": 0})
    quiet(M.stamp_on_face, b, c, me, table, "crashpad_outer", _pre_pass_warning_triangle)
    return {"h": sha1_arrays(h), "fields": sha1_arrays(pm, pp), "maps": sha1_arrays(b, n, m, c)}


def test_tc1_defaults_identical():
    print("[TC1] defaults byte-identical to the round-L module")
    R = load_reference_compose()
    me, table = tc1_fixture()
    ref = tc1_hashes(R, me, table, tc1_specs(R.DetailSpec))
    new = tc1_hashes(C, me, table, tc1_specs(C.DetailSpec))
    new_explicit = tc1_hashes(C, me, table, tc1_specs(C.DetailSpec, explicit_new=True),
                              tone_none=True)
    for k in ref:
        check(new[k] == ref[k], f"fixture {k}: new {new[k][:12]} == round-L {ref[k][:12]}")
        check(new_explicit[k] == ref[k],
              f"fixture {k} with the new fields / tone opts explicitly at defaults")
        if GOLDEN_TC1.get(k, "PENDING") != "PENDING":
            check(ref[k] == GOLDEN_TC1[k], f"fixture {k} == golden {GOLDEN_TC1[k][:12]}")
        else:
            print(f"  note golden {k} = {ref[k]}")
    # the shipped tower hook on FG8 (review pack LOD0: part table + UVUnique)
    check(os.path.isfile(FG8_PACK), f"FG8 review pack present ({FG8_PACK})")
    with bpy.data.libraries.load(FG8_PACK, link=False) as (src, dst):
        dst.objects = [nm for nm in src.objects if nm.endswith("_LOD0")]
    obs = [o for o in dst.objects if o is not None]
    check(len(obs) == 1, f"FG8 LOD0 object loaded ({[o.name for o in obs]})")
    fme = obs[0].data
    ftable = C.read_part_table(fme)
    specs_new = pre_pass_tower_specs(C.DetailSpec)
    try:
        import lift_tower
        live = lift_tower.tower_detail_specs()
        if all(not s._relief_new() and not s.tone_active() for s in live.values()):
            check(live == specs_new, "lift_tower.tower_detail_specs() == the pre-pass snapshot "
                                     "(the shipped hook is what TC1 runs)")
            specs_new = live
        else:
            print("  note lift_tower.tower_detail_specs() now sets detail-pass-1 fields; "
                  "TC1 runs the pre-pass snapshot")
    except Exception as e:  # noqa: BLE001
        print(f"  note lift_tower import failed ({e!r}); TC1 runs the pre-pass snapshot")
    hr = fg8_hook_hash(R, fme, ftable, pre_pass_tower_specs(R.DetailSpec))
    hn = fg8_hook_hash(C, fme, ftable, specs_new)
    for k in hr:
        check(hn[k] == hr[k], f"FG8 hook {k}: new {hn[k][:12]} == round-L {hr[k][:12]}")


# --------------------------------------------------------------------------------------
# TC2 edge rounding
# --------------------------------------------------------------------------------------

def test_tc2_edge_round():
    print("[TC2] edge rounding on a unit box at 64 px/m, r 0.04")
    A, ppm, r = 512, 64.0, 0.04
    sx, sy, tx, ty, bx, by = 32, 32, 32, 160, 160, 160
    uvs = box_uvs(sx, sy, tx, ty, bx, by)
    me = mesh_from("TC2Box", BOX_V, BOX_SIDES + [BOX_TOP, BOX_BOT], uvs, [1] * 6, ["boxa"], A)
    check(len(me.edges) == 12, f"welded box: 12 edges ({len(me.edges)})")
    h, log = quiet(C.detail_height, me, A, {"boxa": C.DetailSpec(edge_round_m=r)})
    st = dict(C.LAST_DETAIL_STATS)
    check(st["drawn"].get("edge_round") == 6, f"6 faces rounded ({st})")
    check("edge_round" in log, "log line names edge_round")
    _pm, pp = C.rasterise_face_fields(me, A)
    nz = C.height_to_normal(h, pp)[..., 2] * 2.0 - 1.0
    tilt = np.degrees(np.arccos(np.clip(nz, -1.0, 1.0)))
    cap_edge = tilt[ty, tx + 20:tx + 44]
    check(cap_edge.min() >= 35.0, f"cap border texel tilt >= 35 deg (min {cap_edge.min():.1f})")
    strip_edge = tilt[sy + 20:sy + 44, sx]
    check(strip_edge.min() >= 35.0, f"strip-end border texel tilt >= 35 deg "
                                    f"(min {strip_edge.min():.1f})")
    k = 0.5 * math.tan(math.radians(50.0))
    want = -r * k * (1.0 - (0.5 / ppm) / r) ** 2
    check(abs(h[ty, tx + 32] - want) < 1e-6, f"profile at d = 0.5 texel {h[ty, tx + 32]:.6f} "
                                             f"== -r k (1 - d/r)^2 {want:.6f}")
    check(h[ty + 2, tx + 32] < 0.0, "inside r (2.5 texels) still rounded")
    inner = h[ty + 4:ty + 60, tx + 4:tx + 60]
    check(np.all(inner == 0.0), "0 beyond r + 1 texel on the cap")
    check(np.all(h[sy + 4:sy + 60, sx + 4:sx + 60] == 0.0), "0 beyond r + 1 texel on a side")
    c = sx + 64                                            # side 0 | side 1 crease
    rows = slice(sy + 16, sy + 48)
    left = h[rows, c - 4:c][:, ::-1]
    right = h[rows, c:c + 4]
    check(np.allclose(left, right, atol=1e-9, rtol=0) and left[:, 0].max() < 0.0,
          f"mirror-symmetric across an in-strip crease (max |diff| "
          f"{np.abs(left - right).max():.2e})")
    # open prism: top / bottom edges are boundaries -> not rounded. Side creases still are rounded.
    me2 = mesh_from("TC2Prism", BOX_V, BOX_SIDES, uvs[:4], [1] * 4, ["prism"], A)
    h2, _ = quiet(C.detail_height, me2, A, {"prism": C.DetailSpec(edge_round_m=r)})
    check(np.all(h2[sy + 60:sy + 64, sx + 8:sx + 56] == 0.0) and h[sy + 63, sx + 32] < 0.0,
          "open prism end not rounded (the closed box's top edge is)")
    check(h2[sy + 32, c - 1] < 0.0, "open prism side creases still rounded")
    # concave creases (inside-out box) are not rounded
    flip = [tuple(reversed(f)) for f in BOX_SIDES + [BOX_TOP, BOX_BOT]]
    me3 = mesh_from("TC2Inside", BOX_V, flip, [list(reversed(u)) for u in uvs], [1] * 6,
                    ["boxa"], A)
    h3, _ = quiet(C.detail_height, me3, A, {"boxa": C.DetailSpec(edge_round_m=r)})
    check(np.all(h3 == 0.0) and "edge_round" not in C.LAST_DETAIL_STATS["drawn"],
          "concave creases (inside-out box) not rounded")
    # floor: r * ppm = 1.28 < 2 -> skipped + logged
    h4, log4 = quiet(C.detail_height, me, A, {"boxa": C.DetailSpec(edge_round_m=0.02)})
    st4 = dict(C.LAST_DETAIL_STATS)
    check(np.all(h4 == 0.0) and st4["skipped"].get("edge_round") == 6 and "edge_round" in log4,
          f"r ppm < 2: skipped and logged ({st4})")


# --------------------------------------------------------------------------------------
# TC3 marks
# --------------------------------------------------------------------------------------

def test_tc3_marks():
    print("[TC3] marks: plate / bolts / ring / boltring / band / floors")
    A, ppm = 512, 100.0
    M, D = C.Mark, C.DetailSpec
    # beam: front (-y at y -0.1), a same-facing web 0.2 m behind, a coplanar back face (+y)
    fv, fuv = wall(-0.5, 0.5, -0.1, 0.0, 0.6, 16, 16, ppm)
    wv, wuv = wall(-0.5, 0.5, 0.1, 0.0, 0.6, 16, 100, ppm)
    bv = [(-0.5, -0.1, 0.0), (-0.5, -0.1, 0.6), (0.5, -0.1, 0.6), (0.5, -0.1, 0.0)]
    buv = [(16, 184), (16, 244), (116, 244), (116, 184)]
    me = mesh_from("TC3Beam", fv + wv + bv, [(0, 1, 2, 3), (4, 5, 6, 7), (8, 9, 10, 11)],
                   [fuv, wuv, buv], [1, 1, 1], ["beam"], A)
    plate = M("plate", (0.0, -0.1, 0.3), half_u=0.2, half_v=0.1, h=0.006)
    h, log = quiet(C.detail_height, me, A, {"beam": D(marks=(plate,))})
    st = dict(C.LAST_DETAIL_STATS)
    front = h[16:76, 16:116]
    check(abs(front[30, 50] - 0.006) < 1e-9, f"plate drawn on its face ({front[30, 50]:.4f})")
    check(np.all(h[100:160, 16:116] == 0.0), "not on the same-facing web 0.2 m behind")
    check(np.all(h[184:244, 16:116] == 0.0), "not on the coplanar back face (normal opposed)")
    check(st["drawn"].get("marks:plate") == 1 and "marks:plate" in log,
          f"marks:plate drawn on 1 face, logged ({st})")
    rim = front[30, :]
    frac = rim[(rim > 0) & (rim < 0.006)]
    check(len(frac) >= 2 and np.all(front[30, 50 - 21:50 + 21][1:-1] > 0),
          f"1-texel rim (fractional rim texels {len(frac)})")
    # transform_marks: the same beam in the net frame (x, y, z) -> (y, -x, z) with the
    # plate authored in the authoring frame draws the same texels
    net = [(y, -x, z) for (x, y, z) in fv + wv + bv]
    me_net = mesh_from("TC3BeamNet", net, [(0, 1, 2, 3), (4, 5, 6, 7), (8, 9, 10, 11)],
                       [fuv, wuv, buv], [1, 1, 1], ["beam"], A)
    rot = ((0, 1, 0, 0), (-1, 0, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1))
    (plate_net,) = C.transform_marks((plate,), rot)
    h_net, _ = quiet(C.detail_height, me_net, A, {"beam": D(marks=(plate_net,))})
    check(np.allclose(h_net, h, atol=1e-9, rtol=0) and plate_net.u == (0.0, -1.0, 0.0),
          f"transform_marks: net-frame mesh + mapped plate draw the same texels "
          f"(u -> {plate_net.u})")
    bolts = M("bolts", (0.0, -0.1, 0.3), half_u=0.3, half_v=0.1, pitch=0.1, bolt_r=0.02)
    hb, _ = quiet(C.detail_height, me, A, {"beam": D(marks=(bolts,))})
    nb = count_blobs(hb[16:76, 16:116] > 1e-9)
    check(nb == 12, f"bolt grid 6 x 2 = 12 domes ({nb})")
    slot = M("slot", (0.0, -0.1, 0.3), half_u=0.15, half_v=0.09, h=0.012)
    hs, _ = quiet(C.detail_height, me, A, {"beam": D(marks=(slot,))})
    check(abs(hs[16 + 30, 16 + 50] + 0.012) < 1e-9 and hs.max() <= 0.0,
          f"slot recessed ({hs[46, 66]:.4f})")
    corner = hs[16 + 30 - 8, 16 + 50 - 14]                 # inside the bbox, outside the round
    check(corner == 0.0 or corner > hs[46, 66], "slot corners rounded")
    # disc (16-gon n-gon, +z) at 100 px/m: ring, disc, boltring
    n16 = 16
    dv = [(0.3 * math.cos(2 * math.pi * i / n16), 0.3 * math.sin(2 * math.pi * i / n16), 0.2)
          for i in range(n16)]
    duv = [(300 + 100 * x, 100 + 100 * y) for x, y, _z in dv]
    dme = mesh_from("TC3Disc", dv, [tuple(range(n16))], [duv], [1], ["disc"], A)
    cols, rows = np.meshgrid(np.arange(A) + 0.5, np.arange(A) + 0.5)
    rho = np.hypot((cols - 300) / 100.0, (rows - 100) / 100.0)
    ring = M("ring", (0, 0, 0.2), n=(0, 0, 1), r_in=0.10, r_out=0.18)
    hr, _ = quiet(C.detail_height, dme, A, {"disc": D(marks=(ring,))})
    on = (rho > 0.11) & (rho < 0.17)
    off = (rho < 0.09) | ((rho > 0.19) & (rho < 0.28))
    check(np.all(np.abs(hr[on] - 0.004) < 1e-9) and np.all(hr[off] == 0.0), "ring annulus")
    disc = M("ring", (0, 0, 0.2), n=(0, 0, 1), r_in=0.0, r_out=0.08)
    hd, _ = quiet(C.detail_height, dme, A, {"disc": D(marks=(disc,))})
    check(np.all(np.abs(hd[rho < 0.07] - 0.004) < 1e-9) and np.all(hd[(rho > 0.09) & (rho < 0.28)] == 0.0),
          "ring with r_in 0 = disc")
    br = M("boltring", (0, 0, 0.2), u=(1, 0, 0), n=(0, 0, 1), count=8, radius=0.2, bolt_r=0.02)
    hbr, _ = quiet(C.detail_height, dme, A, {"disc": D(marks=(br,))})
    nbr = count_blobs(hbr[60:140, 260:340] > 1e-9)
    check(nbr == 8, f"boltring count 8 ({nbr})")
    check(hbr[100, 319] > 0.003, "boltring phase: first bolt on +u")
    # band around a 16-gon cylinder: continuous across facets (no gap, no doubling)
    cppm = 200.0
    cv, cf, cu, fw = cylinder(16, 0.2, 0.0, 1.0, cppm, 8, 260)
    cme = mesh_from("TC3Mast", cv, cf, cu, [1] * 16, ["mast"], A)
    band = M("band", (0, 0, 0.5), n=(0, 0, 1), half_w=0.03, r_max=0.3, h=0.004)
    hband, _ = quiet(C.detail_height, cme, A, {"mast": D(marks=(band,))})
    c0, c1 = 8 + 1, int(8 + 16 * fw) - 1
    rws = slice(260 + 95, 260 + 105)
    blk = hband[rws, c0:c1]
    spread = float((blk.max(axis=1) - blk.min(axis=1)).max())
    check(spread < 1e-7 and blk.max() > 0.0039, f"band continuous across 16 facets "
                                                 f"(row spread {spread:.1e}, peak {blk.max():.4f})")
    check(C.LAST_DETAIL_STATS["drawn"].get("marks:band") == 16, "band drawn on all 16 facets")
    check(np.all(hband[260 + 120:260 + 190, c0:c1] == 0.0), "band zero outside +-half_w")
    # floors: every kind below 2 texels on a 5 px/m quad is skipped and logged
    qv = [(0, 0, 0), (4, 0, 0), (4, 4, 0), (0, 4, 0)]
    qme = mesh_from("TC3Low", qv, [(0, 1, 2, 3)], [[(400, 400), (420, 400), (420, 420), (400, 420)]],
                    [1], ["lowd"], A)
    kw = dict(u=(1, 0, 0), v=(0, 1, 0))
    low = (M("plate", (2, 2, 0), half_u=0.1, half_v=0.1, **kw),
           M("slot", (2, 2, 0), half_u=0.1, half_v=0.1, **kw),
           M("bolts", (2, 2, 0), half_u=0.2, half_v=0.2, pitch=0.2, bolt_r=0.05, **kw),
           M("ring", (2, 2, 0), n=(0, 0, 1), r_in=0.1, r_out=0.2),
           M("boltring", (2, 2, 0), n=(0, 0, 1), count=4, radius=0.5, bolt_r=0.05),
           M("band", (2, 2, 0), n=(0, 0, 1), half_w=0.1))
    hl, logl = quiet(C.detail_height, qme, A, {"lowd": D(marks=low)})
    sk = C.LAST_DETAIL_STATS["skipped"]
    missing = [k for k in C.MARK_KINDS if sk.get("marks:" + k, 0) < 1]
    check(not missing and np.all(hl == 0.0) and "marks:band" in logl,
          f"each kind's floor skip logged ({sk}; missing {missing})")
    try:
        M("lens", (0, 0, 0))
        check(False, "unknown Mark kind raises")
    except ValueError:
        check(True, "unknown Mark kind raises")


# --------------------------------------------------------------------------------------
# TC4 tone
# --------------------------------------------------------------------------------------

def corr_len(f, axis):
    f = f - f.mean()
    var = float((f * f).mean())
    for k in range(1, f.shape[axis] // 2):
        c = (f[:, :-k] * f[:, k:]).mean() if axis == 1 else (f[:-k] * f[k:]).mean()
        if c / var < 0.5:
            return k
    return f.shape[axis] // 2


def tc4_fixture(A=1024, ppm=64.0):
    verts, faces, uvs, parts = [], [], [], []
    table = []

    def add_quad(v, uv, name):
        base = len(verts)
        verts.extend(v)
        faces.append((base, base + 1, base + 2, base + 3))
        uvs.append(uv)
        table.append(name)
        parts.append(len(table))

    add_quad(*wall(0, 6, 5, 0, 6, 16, 16, ppm), "paint_big")
    add_quad(*wall(0, 2, 0, 0, 2, 420, 16, ppm), "paint_a")
    add_quad(*wall(0.5, 2.5, 0, 0, 2, 560, 16, ppm), "paint_b")
    add_quad(*wall(3, 5, 0, 0, 2, 700, 16, ppm), "paint_c")
    add_quad(*wall(0, 6, -3, 0, 2, 16, 420, ppm), "galv_long")
    add_quad([(10, 0, 1), (11, 0, 1), (11, 1, 1), (10, 1, 1)],
             [(420, 200), (484, 200), (484, 264), (420, 264)], "chalk_up")
    add_quad(*wall(12, 13, 0, 0, 1, 500, 200, ppm), "chalk_wall")
    sz, cz = 0.15, math.sqrt(1 - 0.15 ** 2)
    add_quad([(14, 0, 0), (15, 0, 0), (15, sz, cz), (14, sz, cz)],
             [(580, 200), (644, 200), (644, 264), (580, 264)], "chalk_tilt")
    add_quad(*wall(20, 21, 0, 0, 1, 660, 200, ppm), "fine_0")
    return mesh_from("TC4Tone", verts, faces, uvs, parts, table, A), table


def tc4_specs(D):
    return {"paint": D(mottle_m=0.25, mottle_amp=0.05, mottle_smooth=0.06),
            "galv": D(mottle_m=0.2, mottle_amp=0.05, mottle_stretch=2.5),
            "chalk": D(chalk_amp=0.06, chalk_smooth=0.08),
            "fine": D(mottle_m=0.05, mottle_amp=0.05)}


def test_tc4_tone():
    print("[TC4] detail_tone: mottle / chalk / stretch / floor; compose_maps tone opts")
    A = 1024
    me, _table = tc4_fixture(A)
    (dl, ds), log = quiet(C.detail_tone, me, A, tc4_specs(C.DetailSpec))
    st = dict(C.LAST_TONE_STATS)
    big_l = dl[16:400, 16:400] / 0.05
    big_s = ds[16:400, 16:400] / 0.06
    check(abs(big_l.mean()) < 0.1, f"|mean| < 0.1 amp ({big_l.mean():+.3f} amp)")
    check(0.75 * C.MOTTLE_STD < big_l.std() < 1.25 * C.MOTTLE_STD,
          f"luminance std {big_l.std():.3f} amp ~ MOTTLE_STD {C.MOTTLE_STD}")
    check(0.75 * C.MOTTLE_STD < big_s.std() < 1.25 * C.MOTTLE_STD,
          f"smoothness std {big_s.std():.3f} smooth ~ MOTTLE_STD")
    check(abs(corr(big_l, big_s)) < 0.3, f"n1 / n2 independent (corr {corr(big_l, big_s):+.2f})")
    pts = np.random.default_rng(1).uniform(-200.0, 200.0, (400000, 3))
    sd = float(C._fbm2(pts, 12345).std())
    check(abs(sd - C.MOTTLE_STD) < 0.01, f"MOTTLE_STD measured {sd:.4f} (constant {C.MOTTLE_STD})")
    a = dl[16:144, 420:548]
    b = dl[16:144, 560:688]
    c = dl[16:144, 700:828]
    check(np.allclose(b[:, :96], a[:, 32:128], atol=1e-7, rtol=0)
          and np.abs(a - b).max() > 0.005,
          "translated copy moves the pattern (world-anchored: b(x) == a(x + 0.5 m))")
    check(abs(corr(a, c)) < 0.5, f"identical parts 1 m apart correlate < 0.5 ({corr(a, c):+.2f})")
    g = dl[420:548, 16:400]
    la, lc = corr_len(g, 1), corr_len(g, 0)
    check(la >= 1.8 * lc, f"stretch 2.5: along-axis corr length {la} >= 1.8 x across {lc}")
    up = dl[200:264, 420:484]
    check(np.all(up > 0.0) and np.all(ds[200:264, 420:484] < 0.0), "chalk on the up face")
    check(np.all(dl[200:264, 500:564] == 0.0) and np.all(dl[200:264, 580:644] == 0.0),
          "no chalk on a wall or at n.z = 0.15")
    check(0.06 * 0.4 - 1e-6 <= up.min() and up.max() <= 0.06 + 1e-6,
          f"chalk = amp * (0.7 + 0.3 n) on n.z 1 ({up.min():.4f}..{up.max():.4f})")
    check(np.all(dl[200:264, 660:724] == 0.0) and st["skipped"].get("mottle", 0) >= 1
          and "mottle" in log, f"0.05 m cell at 64 px/m (3.2 texels) skipped + logged ({st})")
    # compose_maps tone opts
    Ac = 64
    rng = np.random.default_rng(2)
    base = np.full((Ac, Ac, 4), 0.5, np.float32)
    nrm = np.tile(np.array([0.5, 0.5, 1.0, 1.0], np.float32), (Ac, Ac, 1))
    msk = np.full((Ac, Ac, 4), 0.5, np.float32)
    ctl = np.zeros((Ac, Ac, 4), np.float32)
    tl = rng.uniform(-0.05, 0.05, (Ac, Ac)).astype(np.float32)
    ts = rng.uniform(-0.06, 0.06, (Ac, Ac)).astype(np.float32)
    res = [(0.0, 0.0, 0.25, 0.25)]
    b2, _n2, m2, _c2 = C.compose_maps(base, nrm, msk, ctl, None, None, 50.0,
                                      {"gloss_var": 0.0, "tone_l": tl, "tone_s": ts,
                                       "reserved": res})
    fr = (slice(32, 64), slice(32, 64))
    check(np.allclose(b2[fr][..., 0], 0.5 * (1.0 + tl[fr]), atol=1e-6),
          "tone_l: base *= 1 + tone_l")
    check(np.allclose(m2[fr][..., 3], np.clip(0.5 + ts[fr], 0, 1), atol=1e-6),
          "tone_s: MaskMap A += tone_s")
    rs = C._rect_slices(res[0], Ac)
    check(np.array_equal(b2[rs], base[rs]) and np.array_equal(m2[rs], msk[rs]),
          "reserved rects restored under tone")


# --------------------------------------------------------------------------------------
# TC5 decals (+ M3: per-part plate rects)
# --------------------------------------------------------------------------------------

def text_alpha(body, H, W, ss=4):
    """Bottom-up (H, W) coverage of `body` in Blender's built-in font, fitted with margins."""
    cu = bpy.data.curves.new("TC5Text", type="FONT")
    cu.body = body
    cu.size = 1.0
    ob = bpy.data.objects.new("TC5Text", cu)
    bpy.context.scene.collection.objects.link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    tme = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bm = bmesh.new()
    bm.from_mesh(tme)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    tris = [[(v.co.x, v.co.y) for v in f.verts] for f in bm.faces]
    bm.free()
    bpy.data.meshes.remove(tme)
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.curves.remove(cu)
    pts = np.array([p for t in tris for p in t])
    lo, hi = pts.min(axis=0), pts.max(axis=0)
    sc = min(0.84 * W / (hi[0] - lo[0]), 0.80 * H / (hi[1] - lo[1]))
    ctr = 0.5 * (lo + hi)
    X, Y = np.meshgrid((np.arange(W * ss) + 0.5) / ss, (np.arange(H * ss) + 0.5) / ss)
    cov = np.zeros(X.shape, bool)
    for t in tris:
        (ax, ay), (bx, by), (cx, cy) = [((x - ctr[0]) * sc + W / 2, (y - ctr[1]) * sc + H / 2)
                                        for x, y in t]
        d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(d) < 1e-12:
            continue
        l1 = ((by - cy) * (X - cx) + (cx - bx) * (Y - cy)) / d
        l2 = ((cy - ay) * (X - cx) + (ax - cx) * (Y - cy)) / d
        cov |= (l1 >= 0) & (l2 >= 0) & (l1 + l2 <= 1)
    return cov.reshape(H, ss, W, ss).mean(axis=(1, 3))


def decal_reading(me, A, prefix, decal, base0):
    """Rendered alpha vs the decal sampled at each texel's world (s, t), as read, mirrored
    in s and flipped in t: (corr_read, corr_mirror, corr_flip, n_texels)."""
    table = C.read_part_table(me)
    parts = C.part_indices(me)
    hits = {i + 1 for i, nm in enumerate(table) if nm.startswith(prefix)}
    c, uh, vh = np.array(decal.centre), np.array(decal.u), np.array(decal.v)
    uh, vh = uh / np.linalg.norm(uh), vh / np.linalg.norm(vh)
    w = np.cross(uh, vh)
    H, W = decal.alpha.shape
    pad = np.zeros((H + 2, W + 2))
    pad[1:-1, 1:-1] = decal.alpha
    rend, exp, mir, flp = [], [], [], []
    for poly, sl, mask, X, Y, q, P, _ppm in C.iter_face_rasters(me, A, "UVUnique", 0.0):
        if int(parts[poly.index]) not in hits or float(C._poly_normal(P) @ w) < decal.min_cos:
            continue
        qm, Pm, Mt = C._affine_uv_to_world(q, P)
        d = C._world_points(X, Y, qm, Pm, Mt) - c
        s, t = (d @ uh) / decal.half_u, (d @ vh) / decal.half_v

        def samp(ss, tt):
            return C._bilinear_zero(pad, (ss + 1) * 0.5 * W - 0.5, (tt + 1) * 0.5 * H - 0.5)
        rend.append((1.0 - base0[sl][..., 0] / 0.5)[mask])
        exp.append(samp(s, t)[mask])
        mir.append(samp(-s, t)[mask])
        flp.append(samp(s, -t)[mask])
    rend, exp, mir, flp = (np.concatenate(x) for x in (rend, exp, mir, flp))
    return corr(rend, exp), corr(rend, mir), corr(rend, flp), rend


def test_tc5_decal():
    print("[TC5] decal_on_faces: reads left-to-right on a plane and a 16-gon cylinder")
    A = 512
    alpha = text_alpha("AB", 48, 96)
    check(alpha.max() > 0.99 and 0.05 < alpha.mean() < 0.6, f"text raster ({alpha.mean():.2f})")
    # plane with a MIRRORED UV (u_tex runs along -x): the decal must still read in world
    pv = [(0, 0, 0), (2, 0, 0), (2, 0, 1), (0, 0, 1)]
    puv = [(216, 16), (16, 16), (16, 116), (216, 116)]
    cv, cf, cu, fw = cylinder(16, 0.2, 0.0, 1.0, 200.0, 8, 200, offset=(5.0, 0.0))
    verts = pv + cv
    faces = [(0, 1, 2, 3)] + [tuple(4 + i for i in f) for f in cf]
    me = mesh_from("TC5Decal", verts, faces, [puv] + cu, [1] + [2] * 16, ["plate_n", "mast"], A)
    cases = (("plate_n", C.Decal((1.0, 0.0, 0.5), (1, 0, 0), (0, 0, 1), 0.6, 0.3, alpha, (0, 0, 0))),
             ("mast", C.Decal((5.2, 0.0, 0.5), (0, 1, 0), (0, 0, 1), 0.15, 0.075, alpha, (0, 0, 0))))
    for prefix, dec in cases:
        base = np.zeros((A, A, 4), np.float32)
        base[..., 0:3] = 0.5
        ctl = np.zeros((A, A, 4), np.float32)
        ctl[..., 0:3] = 1.0
        b0, c0 = base.copy(), ctl.copy()
        rects, log = quiet(C.decal_on_faces, base, ctl, me, A, None, prefix, dec)
        cr, cm, cf_, rend = decal_reading(me, A, prefix, dec, base)
        check(cr > 0.9 and cm < cr - 0.3 and cf_ < cr - 0.3,
              f"{prefix}: reads as authored (corr {cr:.2f}; mirrored {cm:.2f}, flipped {cf_:.2f})")
        a_all = 1.0 - base[..., 0] / 0.5
        touched = a_all > 1e-6
        fr = int(((a_all > 0.02) & (a_all < 0.98)).sum())
        check(fr >= 0.1 * int(touched.sum()), f"{prefix}: anti-aliased edges "
                                              f"({fr} of {int(touched.sum())} fractional)")
        solid = a_all >= 0.99
        check(solid.any() and np.all(ctl[solid][:, 0:2] == 0.0) and np.all(ctl[..., 2] == 1.0),
              f"{prefix}: ControlMask R/G 0 under solid paint, B untouched")
        check(np.all(ctl[~touched] == c0[~touched]) and np.all(base[touched][:, 3] == 1.0),
              f"{prefix}: ControlMask untouched off the decal; BaseColor A 1 on it")
        changed = np.any(base != b0, axis=-1) | np.any(ctl != c0, axis=-1)
        cov = np.zeros((A, A), bool)
        for r0, r1, q0, q1 in rects:
            cov[r0:r1, q0:q1] = True
        check(changed.any() and not (changed & ~cov).any(),
              f"{prefix}: {len(rects)} returned rects cover every changed texel")
        if prefix == "mast":
            check(len(rects) >= 4, f"mast: the decal spans facets ({len(rects)} rects)")
    # M3: plates are stamped / protected per part, never by the shared prefix
    q = [(0, 0, 0), (0.36, 0, 0), (0.36, 0, 0.16), (0, 0, 0.16)]
    verts = [(x + 2 * k, y, z) for k in range(4) for (x, y, z) in q]
    faces = [(4 * k, 4 * k + 1, 4 * k + 2, 4 * k + 3) for k in range(4)]
    uvs = [[(16 + 120 * k + x * 100, 300 + z * 100) for (x, _y, z) in q] for k in range(4)]
    tab = ["plate_n", "mid_x", "plate_p", "platerim_n"]
    pme = mesh_from("TC5Plates", verts, faces, uvs, [1, 2, 3, 4], tab, A)
    union = C.uv_bbox_px(pme, tab, "plate_", A)
    rn = C.uv_bbox_px(pme, tab, "plate_n", A)
    rp = C.uv_bbox_px(pme, tab, "plate_p", A)
    check(union[2] <= 136 + 1 and union[3] >= 136 + 36,
          f"M3: the 'plate_' rect {union} spans the island packed between the plates")
    check(rn[3] <= 136 and rp[2] >= 136 + 36 and rn[3] - rn[2] <= 37 and rp[3] - rp[2] <= 37,
          f"M3: per-part rects plate_n {rn} / plate_p {rp} exclude the middle island")
    check(C.faces_of_prefix(pme, tab, "plate_n") == [0]
          and C.faces_of_prefix(pme, tab, "plate_") == [0, 2],
          "M3: 'plate_n' selects plate_n only (never platerim_n); 'plate_' takes both plates")
    # In the weather layer's stamped= list, per-part entries protect two small rects. The prefix
    # protects the whole span (it would freeze mid_x unweathered).
    span = (union[1] - union[0]) * (union[3] - union[2])
    per_part = sum((r[1] - r[0]) * (r[3] - r[2]) for r in (rn, rp))
    check(per_part < 0.3 * span, f"M3: per-part protect area {per_part} px vs prefix {span} px")


# --------------------------------------------------------------------------------------
# TC6 determinism
# --------------------------------------------------------------------------------------

def tc6_hash():
    A = 256
    D, M = C.DetailSpec, C.Mark
    sx, sy, tx, ty, bx, by = 8, 8, 8, 90, 90, 90
    uvs = box_uvs(sx, sy, tx, ty, bx, by, s=32)
    cv, cf, cu, _fw = cylinder(16, 0.2, 0.0, 1.0, 64.0, 8, 160, offset=(3.0, 0.0))
    verts = list(BOX_V) + cv
    faces = BOX_SIDES + [BOX_TOP, BOX_BOT] + [tuple(8 + i for i in f) for f in cf]
    me = mesh_from("TC6Fix", verts, faces, uvs + cu, [1] * 6 + [2] * 16, ["boxa", "mast"], A)
    specs = {"boxa": D(edge_round_m=0.06, bolt_pitch_m=0.15, bolt_r_m=0.03,
                       marks=(M("plate", (0.5, 0.0, 0.5), half_u=0.2, half_v=0.1),
                              M("bolts", (0.5, 0.0, 0.5), half_u=0.2, half_v=0.0, pitch=0.1,
                                bolt_r=0.02)),
                       mottle_m=0.3, mottle_amp=0.05, mottle_smooth=0.06,
                       chalk_amp=0.06, chalk_smooth=0.08),
             "mast": D(marks=(M("band", (3, 0, 0.5), n=(0, 0, 1), half_w=0.04, r_max=0.3),),
                       mottle_m=0.15, mottle_amp=0.05, mottle_stretch=2.5)}
    h, _ = quiet(C.detail_height, me, A, specs)
    (dl, ds), _ = quiet(C.detail_tone, me, A, specs, seed=7)
    base = np.full((A, A, 4), 0.5, np.float32)
    ctl = np.ones((A, A, 4), np.float32)
    alpha = np.zeros((16, 32))
    alpha[2:14, 3:12] = 1.0
    alpha[4:8, 18:30] = 0.6
    rects, _ = quiet(C.decal_on_faces, base, ctl, me, A, None, "mast",
                     C.Decal((3.2, 0.0, 0.5), (0, 1, 0), (0, 0, 1), 0.12, 0.06, alpha, (0.1, 0.1, 0.1)))
    return sha1_arrays(h, dl, ds, base, ctl, np.array(rects, np.int64))


def test_tc6_determinism():
    print("[TC6] determinism: two runs + a child process")
    a, b = tc6_hash(), tc6_hash()
    check(a == b, f"two in-process runs identical ({a[:12]})")
    cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
           "--python", os.path.abspath(__file__), "--", "--tc6-child"]
    res = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
    got = [ln.split()[1] for ln in res.stdout.splitlines() if ln.startswith("TC6SHA ")]
    check(res.returncode == 0 and got == [a], f"child process identical ({got[:1]} vs {a[:12]})")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--tc6-child" in argv:
        print(f"TC6SHA {tc6_hash()}")
        sys.exit(0)
    for t in (test_height_to_normal_sign, test_blend_rnm, test_detail_height,
              test_compose_maps, test_tc1_defaults_identical, test_tc2_edge_round,
              test_tc3_marks, test_tc4_tone, test_tc5_decal, test_tc6_determinism):
        try:
            t()
        except Exception as e:  # noqa: BLE001
            traceback.print_exc()
            FAILS.append(f"{t.__name__}: {e!r}")
    if FAILS:
        print(f"FAIL test_compose: {len(FAILS)} failure(s): {FAILS}")
        sys.exit(1)
    print("PASS test_compose: height_to_normal GL sign, blend_rnm identity, detail_height "
          "seams + texel floor, compose_maps reserved/ctl A; detail pass 1 TC1 defaults "
          "byte-identical (fixture + FG8 hook vs round L), TC2 edge round, TC3 marks, "
          "TC4 tone, TC5 decal (+ M3 per-part rects), TC6 determinism")
    sys.exit(0)


if __name__ == "__main__":
    main()
