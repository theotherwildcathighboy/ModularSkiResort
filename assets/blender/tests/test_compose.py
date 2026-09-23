"""
test_compose.py - unit tests for cs2_compose (round L spec "cs2_compose.py").

Run:  blender --background --factory-startup --python tests\\test_compose.py
Exit code 1 on any failure; prints one PASS line on success.  Small: no bakes, a
2-quad mesh, 64..256 px arrays.

(1) ridge -> height_to_normal: GL sign.  Buffers are bottom-up (row 0 = v 0), so
    np.gradient's row derivative IS dh/dv.  The unnormalised normal of a height field is
    (-dh/du, -dh/dv, 1); encoded G = 0.5 + 0.5*ny = 0.5 - k*dh/dv (k > 0).  A ridge has
    two flanks:
      * the flank RISING toward +V (rows below the crest, dh/dv > 0): G < 0.5 - the
        "+V-facing slope" of the task text, i.e. the slope you climb walking toward +V;
      * the flank FALLING toward +V (rows above the crest, dh/dv < 0): its NORMAL tilts
        toward +V, G > 0.5, and a light coming from +V brightens it (spec line 102).
    Both are asserted, plus the lighting check, plus R == 0.5 (no du gradient), B > 0.5,
    unit length.
(2) blend_rnm(flat, flat) == flat, and flat is the identity on either side.
(3) detail_height on a 2-quad mesh: quad 1 at 50 px/m gets 1.2 m seams at the expected
    texel columns; quad 2 (identical geometry, ~1 px/m) is below the 2-texel floor and
    stays zero.
(4) compose_maps keeps reserved rects byte-identical in all four maps and ships
    ControlMask A == 0 (and MaskMap B == 0, BaseColor A == 1).
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
    """Two 2.4 x 1.0 m wall quads (normal -Y) tagged wall_hi / wall_lo.  wall_hi maps to
    a 120 x 50 texel rect (50 px/m); wall_lo to a 3 x 2 texel rect (~1.1 px/m)."""
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
    print("[3] detail_height: seams at metre pitch, 2-texel floor")
    A = 256
    obj, me, table = build_two_quads(A)
    specs = {"wall": C.DetailSpec(seams_m=1.2, seam_depth_m=0.002, seam_axes="ribs")}
    h = C.detail_height(me, A, specs, uv_layer="UVUnique", dilate_px=0.0)
    check(h.shape == (A, A) and h.dtype == np.float32, f"shape {h.shape} {h.dtype}")
    # wall_hi: x = 0 .. 2.4 m spans cols 25 .. 145 (50 px/m); seams at x = 0, 1.2, 2.4 ->
    # cols 25, 85, 145.  Width max(0.012 m, 1 texel) = 1 texel -> the interior seam lands
    # on the single column whose centre is within 0.5 texel of 85.0 (col 84 or 85).
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
    # bolts (dia 0.06 m = 3 px) + corrugation (half period 0.05 m = 2.5 px) are above
    # the floor at 50 px/m and get drawn: bolt domes reach +bolt_h, sine reaches +-depth
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
    # stamp_on_face: painter paints half the rect; ctl R zeroed only there
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


def main():
    for t in (test_height_to_normal_sign, test_blend_rnm, test_detail_height,
              test_compose_maps):
        try:
            t()
        except Exception as e:  # noqa: BLE001
            traceback.print_exc()
            FAILS.append(f"{t.__name__}: {e!r}")
    if FAILS:
        print(f"FAIL test_compose: {len(FAILS)} failure(s): {FAILS}")
        sys.exit(1)
    print("PASS test_compose: height_to_normal GL sign, blend_rnm identity, detail_height "
          "seams + texel floor, compose_maps reserved/ctl A")
    sys.exit(0)


if __name__ == "__main__":
    main()
