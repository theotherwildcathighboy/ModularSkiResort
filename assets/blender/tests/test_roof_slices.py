"""
test_roof_slices - T272 (r7 2026-11-20): the DQ / gondola texture pass draws the roof field as a repeating seam tile
(lift_terminal_detach.roof_shell_slices, share lists, draw_roof_seams). Geometry and pixels in-process: no station is
built and nothing is baked, so the test is a light job (a few seconds).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_roof_slices.py

Gates:
  R1  flag off: build_roof / build_gd_roof return ONE "roof_shell" (30 / 32 quads over the full roof), then roof_rim,
      rooflining (and the gondola's eave soffits) in the shipped order; roof_share_lists returns the shipped DQ lists;
  R2  pass on (DQ base / top, gondola base / top): N = round(len / 2.8 m) slices roof_shell_<i> over the same extent,
      the middle one LAST (a stacked texel carries the last copy's bake), each slice L = len / N long with
      k = round(L / 0.50) seams (pitch 0.46-0.53 m), all slices translation-identical (vertex multiset relative to the
      bbox min, 1e-5 m) with sharp end loops (each slice's normals its own: the gable rim never smooths into an end
      slice), quads = N x the shell's cross-section + 2 fascias; the roof tri delta fits the manifest headroom
      (lod0_cap - the shipped 1.0.4 LOD0); L sits off share_identical's 1 cm bucket edge; the DQ share lists add
      roof_shell and keep roof_rim a hero part;
  R3  draw_roof_seams (the dot-grid fix): three DQ slices oriented as the bake mesh (orient_for_net), stacked on ONE uv
      island at 80 px/m: across the barrel the normal shows k seams per slice; ALONG a seam (down the arc, across 27
      facet edges) the tilt is constant (sd <= 0.3 deg per column: no facet edge draws a seam twice, the 1.0.4 dot
      grid); the seam (two flanks + the flat top) is >= 3 texels wide at 80 px/m and sits at (j + 0.5) L / k
      (1.5 texels); the albedo mean over the island holds (|dev| <= 0.5 %); the smoothness only rises; a second call on
      the same maps from a fresh copy is identical (assignment, not accumulation).
  R4  T273 closures (the see-through gate's rule: a camera ray's FIRST surface must be a front face). Rays from 20 m at
      every 30 deg of bearing and elevation 8 / 22 / 40 deg (the gate's play cameras), aimed at a grid over each opening:
      (a) the eave gutter: off = the shipped open box (5 faces, no top); on = the trough (14 quads) and no ray into it
      meets a back face, where the shipped box shows them; (b) the crash pad: on = a 4-quad top ring from the pad's rim
      to the column, no ray into the 0.16 m gap under the crosshead meets a back face (the shipped pad shows them);
      (c) the SLOW board: on = a 4-quad 3 mm edge band, no grazing ray at the board's ends meets the label's back (the
      shipped board shows it); (d) the gable rims: every edge sharp under the pass, none when off.
Exit 1 on the first failure; one PASS line otherwise.
"""

import math
import os
import sys
import traceback

import bmesh
import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import cs2_asset                       # noqa: E402
import cs2_liftdress as LD             # noqa: E402
import lift_terminal_detach as D       # noqa: E402

CHECKS = []
# the shipped 1.0.4 LOD0 tris (stations/detachable/E, stations/gondola/D manifests) and their caps
SHIPPED_LOD0 = {("base", False): (8477, 12000), ("top", False): (7453, 10000),
                ("base", True): (8869, 13000), ("top", True): (8661, 10000)}


def ok(msg):
    CHECKS.append(msg)
    print("  ok", msg)


def params(kind, gondola):
    return D.params_for(kind, gondola, D.load_carrier("gd8" if gondola else "dq"))


def build(p):
    return D.build_gd_roof(p) if p.gondola else D.build_roof(p)


def clear(parts):
    for o, _m in parts:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(me)


def coords(o):
    a = np.array([v.co[:] for v in o.data.vertices], np.float64)
    return a


def check_off():
    D.TEXPASS = None
    for kind in ("base", "top"):
        for g in (False, True):
            p = params(kind, g)
            assert not D.roof_slices_on(p)
            parts = build(p)
            names = [o.name for o, _m in parts]
            want = ["roof_shell", "roof_rim", "rooflining"] + (["eavesof_n", "eavesof_p"] if g else [])
            assert names == want, names
            q = len(parts[0][0].data.polygons)
            assert q == (32 if g else 30), q
            xa, xb = D.roof_extent(p)
            x = coords(parts[0][0])[:, 0]
            assert abs(x.min() - xa) < 1e-5 and abs(x.max() - xb) < 1e-5, (x.min(), x.max(), xa, xb)
            clear(parts)
    assert D.roof_share_lists(params("base", False), LD.DQ_SHARE_WHITELIST, LD.DQ_SHARE_HERO) == \
        (LD.DQ_SHARE_WHITELIST, LD.DQ_SHARE_HERO)
    ok("R1 flag off: one roof_shell (30 / 32 quads, the full extent), the shipped part order and share lists")


def check_on():
    D.TEXPASS = "v1"
    rows = []
    try:
        for kind in ("base", "top"):
            for g in (False, True):
                p = params(kind, g)
                assert D.roof_slices_on(p), (kind, g)
                xa, xb = D.roof_extent(p)
                sl, k = D.roof_slices(xa, xb)
                n = len(sl)
                L = (xb - xa) / n
                assert n == int(round((xb - xa) / D.ROOF_SLICE_M)) and 0.46 <= L / k <= 0.53, (n, L, k)
                assert abs((L / 0.01) % 1.0 - 0.5) > 0.01, L
                parts = build(p)
                shells = [(o, m) for o, m in parts if o.name.startswith("roof_shell_")]
                names = [o.name for o, _m in shells]
                assert len(shells) == n and names[-1] == f"roof_shell_{n // 2}", names
                assert sorted(names) == sorted(f"roof_shell_{i}" for i in range(n)), names
                assert [o.name for o, _m in parts[n:]][:2] == ["roof_rim", "rooflining"]
                q0 = 32 if g else 30
                ref = None
                xs = []
                for o, m in shells:
                    assert m == "paint1" and len(o.data.polygons) == q0, (o.name, len(o.data.polygons))
                    sh = o.data.attributes.get("sharp_edge")
                    n_sharp = sum(1 for v in sh.data if v.value) if sh is not None else 0
                    assert n_sharp == 2 * q0, (o.name, n_sharp)     # the two end loops (facets + 2 fascias): q0 edges each
                    c = coords(o)
                    xs.append((c[:, 0].min(), c[:, 0].max()))
                    rel = c - c.min(axis=0)
                    rel = rel[np.lexsort(rel.T[::-1])]
                    if ref is None:
                        ref = rel
                    assert rel.shape == ref.shape and float(np.abs(rel - ref).max()) < 1e-5, o.name
                xs.sort()
                assert abs(xs[0][0] - xa) < 1e-5 and abs(xs[-1][1] - xb) < 1e-5
                assert all(abs((b - a) - L) < 1e-5 for a, b in xs)
                dtri = 2 * q0 * (n - 3)               # vs the shipped shell cut in 3 (CUT_PIECES)
                lod0, cap = SHIPPED_LOD0[(kind, g)]
                assert lod0 + dtri <= cap, (kind, g, lod0, dtri, cap)
                rows.append(f"{'gd' if g else 'dq'} {kind} {n} x {L:.4f} m, {k} seams ({L / k:.3f} m), "
                            f"+{dtri} tris ({lod0 + dtri} / {cap})")
                clear(parts)
        wl, hero = D.roof_share_lists(params("base", False), LD.DQ_SHARE_WHITELIST, LD.DQ_SHARE_HERO)
        assert "roof_shell" in wl and "roof_" not in hero and "roof_rim" in hero, (wl, hero)
    finally:
        D.TEXPASS = None
    ok("R2 pass on: " + "; ".join(rows) + "; translation-identical slices with sharp end loops, the middle one last; share lists + roof_shell")


def synthetic_roof(p, n_use=3, ppm=80.0, A=1024):
    """n_use DQ slices joined, oriented as the bake mesh, every slice on the SAME uv island (u along the barrel, v along
    the arc; the fascias in a strip beside it). Returns (obj, table, island px rect, L, k)."""
    D.TEXPASS = "v1"
    try:
        parts = build(p)
    finally:
        D.TEXPASS = None
    shells = [o for o, _m in parts if o.name.startswith("roof_shell_")][:n_use]
    for o, _m in parts:
        if o not in shells:
            me = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.meshes.remove(me)
    xa, xb = D.roof_extent(p)
    sl, k = D.roof_slices(xa, xb)
    L = (xb - xa) / len(sl)
    table = [o.name for o in shells]
    bm = bmesh.new()
    part_vals = []
    for pi, o in enumerate(shells, start=1):
        c = coords(o)
        x0 = float(c[:, 0].min())
        m = bmesh.new()
        m.from_mesh(o.data)
        uvl = m.loops.layers.uv.new("UVUnique")
        top = D.arch(p)[0]                      # the cross-section (y, z), eave -> crown -> eave
        zmin = min(z for _y, z in top) - D.ROOF_T
        for f in m.faces:
            fascia = abs(f.normal.z) < 0.05 and abs(abs(f.normal.y) - 1.0) < 1e-3
            for lp in f.loops:
                u = (lp.vert.co.x - x0) * ppm / A
                if fascia:
                    v = 0.92 + (lp.vert.co.z - zmin) * ppm / A * 0.5
                    u = u + (0.0 if f.normal.y < 0 else 0.5)
                else:
                    v = _arc_len(top, lp.vert.co.y) * ppm / A + 0.02
                lp[uvl].uv = (u + 0.02, v)
        me = bpy.data.meshes.new(f"tmp{pi}")
        m.to_mesh(me)
        m.free()
        part_vals += [pi] * len(me.polygons)
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    me = bpy.data.meshes.new("roofsyn")
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new("roofsyn", me)
    bpy.context.collection.objects.link(obj)
    for o in shells:
        m_ = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(m_)
    attr = me.attributes.new("part", "INT", "FACE")
    attr.data.foreach_set("value", np.array(part_vals, np.int32))
    cs2_asset.orient_for_net(obj)
    return obj, table, L, k


def _arc_len(top, y):
    """Arc length (m) from the -y eave to y over the cross-section points `top` [(y, z)] sorted by y."""
    s = 0.0
    for (y0, z0), (y1, z1) in zip(top, top[1:]):
        seg = math.hypot(y1 - y0, z1 - z0)
        if y >= y1 - 1e-9:
            s += seg
        else:
            s += seg * max(0.0, (y - y0) / (y1 - y0))
            break
    return s


def check_seams():
    p = params("base", False)
    A = 1024
    obj, table, L, k = synthetic_roof(p, A=A)
    me = obj.data

    def run():
        base = np.full((A, A, 4), 0.5, np.float32)
        base[..., 3] = 1.0
        nrm = np.zeros((A, A, 4), np.float32)
        nrm[...] = (0.5, 0.5, 1.0, 1.0)
        msk = np.zeros((A, A, 4), np.float32)
        msk[..., 3] = 0.4
        D.TEXPASS = "v1"
        try:
            rep = D.draw_roof_seams(base, nrm, msk, me, table, p)
        finally:
            D.TEXPASS = None
        return base, nrm, msk, rep

    base, nrm, msk, rep = run()
    assert rep["slices"] >= 3 and rep["seams_per_slice"] == k, rep
    # the main island: u from 0.02 over L, v from 0.02 over the arc
    ppm = 80.0
    u0, u1 = int(0.02 * A) + 2, int(0.02 * A + L * ppm) - 2
    v0, v1 = int(0.02 * A) + 3, int(0.02 * A + 10.0 * ppm) - 3
    n = nrm[v0:v1, u0:u1, :3] * 2.0 - 1.0
    tilt = np.degrees(np.arccos(np.clip(n[..., 2] / np.linalg.norm(n, axis=-1), -1, 1)))
    sd = float(tilt.std(axis=0).max())
    assert sd <= 0.3, f"tilt varies along a seam (facet edges drew it twice?): sd {sd:.3f} deg"
    col = tilt.mean(0) > 4.0
    d = np.diff(np.r_[0, col.astype(np.int8), 0])
    runs = list(zip(np.flatnonzero(d == 1).tolist(), np.flatnonzero(d == -1).tolist()))
    # one seam = its two flanks and the flat top between them (2 x 7 mm: a gap of at most 2 texels at 80 px/m)
    gap = int(math.ceil(2.0 * D.ROOF_SEAM["rib_m"][0] * ppm)) + 1
    seams = [list(runs[0])]
    for a, b in runs[1:]:
        if a - seams[-1][1] <= gap:
            seams[-1][1] = b
        else:
            seams.append([a, b])
    widths = [b - a for a, b in seams]
    assert len(widths) == k and min(widths) >= 3, (k, widths, runs)
    centers = np.array([(a + b) / 2.0 for a, b in seams]) / ppm          # m from the island's u0
    want = (np.arange(k) + 0.5) * L / k - (u0 - 0.02 * A) / ppm
    assert float(np.abs(centers - want).max()) <= 1.5 / ppm, (centers, want)
    lum = base[v0:v1, int(0.02 * A):int(0.02 * A + L * ppm), 0]
    dev = float(lum.mean() / 0.5 - 1.0)
    assert abs(dev) <= 0.005, dev
    assert float(msk[..., 3].min()) >= 0.4 - 1e-6 and float(msk[..., 3].max()) > 0.41
    base2, nrm2, msk2, _r = run()
    assert np.array_equal(base, base2) and np.array_equal(nrm, nrm2) and np.array_equal(msk, msk2)
    bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.meshes.remove(me)
    ok(f"R3 seams: {k} a slice, widths {min(widths)}-{max(widths)} texels at {ppm:g} px/m, tilt sd along the seams "
       f"{sd:.3f} deg (no facet-edge doubling), albedo mean {dev:+.4f}, smoothness only rises, repeatable")


def _world_bvh(objs):
    """BVH + per-face world normals (from the winding) of objects in world space."""
    from mathutils.bvhtree import BVHTree
    bpy.context.view_layer.update()              # kit parts are placed by their object transform
    verts, polys, nrm = [], [], []
    for o in objs:
        mw = o.matrix_world
        b = len(verts)
        verts += [mw @ v.co for v in o.data.vertices]
        for pl in o.data.polygons:
            idx = [b + i for i in pl.vertices]
            polys.append(idx)
            a, c, d = verts[idx[0]], verts[idx[1]], verts[idx[2]]
            nrm.append((c - a).cross(d - a).normalized())
    return BVHTree.FromPolygons(verts, polys), nrm


def _back_hits(objs, targets, dist=20.0):
    """Rays from the gate's play cameras (bearing every 30 deg, elevation 8 / 22 / 40, `dist` m) at each target point:
    (rays that hit anything, rays whose FIRST hit is a back face)."""
    from mathutils import Vector
    bvh, nrm = _world_bvh(objs)
    n_hit = n_back = 0
    for t in targets:
        t = Vector(t)
        for b in range(0, 360, 30):
            for e in (8.0, 22.0, 40.0):
                a, el = math.radians(b), math.radians(e)
                eye = t + Vector((math.sin(a) * math.cos(el), -math.cos(a) * math.cos(el), math.sin(el))) * dist
                d = (t - eye).normalized()
                hit, _n, fi, _d = bvh.ray_cast(eye, d)
                if hit is None:
                    continue
                n_hit += 1
                if nrm[fi].dot(d) > 0.0:
                    n_back += 1
    return n_hit, n_back


def check_closures():
    rows = []
    try:
        # (a) the gutter trough, at the DQ base's size (0.12 x 0.12)
        for tp in (None, "v1"):
            D.TEXPASS = tp
            p = params("base", False)
            o = D.gutter_trough("gutter_t", 0.0, 6.0, 0.0, 0.12, 0.0, 0.12, p)
            nf = len(o.data.polygons)
            ups = sum(1 for pl in o.data.polygons if (o.matrix_world.to_3x3() @ pl.normal).z > 0.9)
            tg = [(x, y, z) for x in np.linspace(0.5, 5.5, 6) for y in (0.03, 0.06, 0.09) for z in (0.03, 0.08, 0.115)]
            nh, nb = _back_hits([o], tg)
            if tp is None:
                assert nf == 5 and ups == 0 and nb > 0, (nf, ups, nb)
                off_b, off_nf = nb, nf
            else:
                assert nf == 14 and nb == 0, (nf, nb)
                rows.append(f"gutter off {off_nf} faces / {off_b} back-face rays, on {nf} quads / 0 of {nh}")
            clear([(o, None)])
        # (b) the crash pad under the crosshead (DQ top, the front pedestal)
        for tp in (None, "v1"):
            D.TEXPASS = tp
            p = params("top", False)
            parts = D.build_pedestals(p)
            keep = [(o, m) for o, m in parts if o.name.startswith(("ped_f", "pad_f", "crosshead_f", "pedf", "plinth_f"))]
            names = sorted(o.name for o, _m in keep)
            zt = D.z_ped_top(p)
            xp = p.ped_f
            tg = [(xp + x, y, zt - 0.16 + dz) for x in (-0.75, -0.6, 0.0, 0.6, 0.75) for y in (-0.65, 0.0, 0.65)
                  for dz in (0.0, 0.08, 0.15) if abs(x) > 0.55 or abs(y) > 0.5]
            nh, nb = _back_hits([o for o, _m in keep], tg)
            if tp is None:
                assert "pad_f_top" not in names and nb > 0, (names, nb)
                off_b = nb
            else:
                assert "pad_f_top" in names and nb == 0, (names, nb)
                top = [o for o, _m in keep if o.name == "pad_f_top"][0]
                assert len(top.data.polygons) == 4
                rows.append(f"pad off {off_b} back-face rays, on 4-quad top ring / 0 of {nh}")
            clear(parts)
        # (c) the SLOW board on posts: grazing rays at its two ends
        for tp in (None, "v1"):
            D.TEXPASS = tp
            p = params("base", False)
            c, w, h = (0.0, 0.0, 1.375), 1.20, 0.35
            parts = LD.board("t", c, "-Y", w, h, mount="post", post_z0=0.0)
            n, r = LD._plan("-Y")
            if D.roof_slices_on(p):
                parts.append((D.board_edge("board_t_edge", c, n, r, w, h, LD.LABEL_PROUD), "galv"))
            ys = c[1] - n.y * LD.LABEL_PROUD / 2.0        # the slot between the label and the backing's face
            tg = [(sx * (w / 2.0 - dx), ys, c[2] + dz) for sx in (-1.0, 1.0)
                  for dx in (0.0, 0.002, 0.005) for dz in np.linspace(-0.16, 0.16, 9)]
            tg += [(x, ys, c[2] + sz * (h / 2.0 - dz)) for sz in (-1.0, 1.0)
                   for x in np.linspace(-0.5, 0.5, 5) for dz in (0.0, 0.002)]
            nh, nb = _back_hits([o for o, _m in parts], tg)
            if tp is None:
                assert nb > 0, nb
                off_b = nb
            else:
                assert nb == 0, nb
                rows.append(f"board off {off_b} back-face rays, on 3 mm edge band / 0 of {nh}")
            clear(parts)
        # (d) the gable rims sharp under the pass only
        for tp in (None, "v1"):
            D.TEXPASS = tp
            for g in (False, True):
                p = params("top", g)
                parts = build(p)
                rim = [o for o, _m in parts if o.name == "roof_rim"][0]
                sh = rim.data.attributes.get("sharp_edge")
                n_sh = sum(1 for v in sh.data if v.value) if sh is not None else 0
                assert n_sh == (len(rim.data.edges) if tp else 0), (tp, g, n_sh, len(rim.data.edges))
                clear(parts)
        rows.append("rims all sharp on / none off")
    finally:
        D.TEXPASS = None
    ok("R4 closures: " + "; ".join(rows))


def main():
    check_off()
    check_on()
    check_seams()
    check_closures()
    print(f"PASS test_roof_slices: {len(CHECKS)} checks")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
