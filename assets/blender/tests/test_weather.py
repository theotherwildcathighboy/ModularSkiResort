"""
test_weather.py - tests for cs2_weather (design/weathering-layer.md section 11).

Run the tests with this command:

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_weather.py

The test exits with code 1 on any failure. It prints one PASS line on success.

Fixture: a "kit cube". It is built bmesh-free from from_pydata parts (no cs2_bldg import) and tagged
with cs2_asset.tag_parts / store_part_table. It has a hand-made isotropic UVUnique with these
properties:
- One axis-aligned island per face, and one island rotated 90 deg. The density is uniform rho px/m.
- Hidden faces sit at 0.1 rho, one steel plate at 1.6 rho and one at 0.5 rho.
- The islands are skyline-packed with every rect corner on a texel EDGE. So no texel center sits on
  a face boundary at rho 40 or 80.
- The RESERVED_RECT stays free.

The fixture parts are:
- wall_t: 3.6 x 3.0 x 2.5 m closed timber box on z 0 (top face removed, as cull).
- gable_t0/1: gable triangles.
- roof_tS / roof_tN: 25 deg slabs, 0.24 m thick, 0.6 m eave overhang (n.z 0.906), downward soffits.
- trim_t: 0.15 m slot-1 trim board (floating).
- bar_t: unslotted steel bar (override paint_steel, floating).
- bracket_g: 0.10 x 0.10 x 0.20 m galv bracket on the +X wall at z 2.0.
- deck_t: 1.2 m deck slab (top z 0.30) half under the -Y eave, steps step_t0 / t1.
- door_t0_leaf / door_t0_jamb0: on the deck wall.
- sign_t_face: quad on the -X wall.
- steelplate_hi / steelplate_lo: bolted steel quads (DetailSpec seams 1.2, bolts 0.15, r 0.018) at
  64 / 20 px/m.

The synthetic maps have this content:
- flat linear albedo per material key (slot texels at 0.604 = RECOLOR_ALBEDO 0.80 encoded)
- flat normals
- MaskMap from the key
- ControlMask R on the trim
- AO 1 except 0.4 on walls right under the soffit (cover_h <= 0.3), multiplied into base as the bake
  does

Tests T1-T11 follow the design table. The build report gives the exact thresholds and the two
documented interpretations: T5 selects walls beyond the eave band, and the low face of T9 carries a
bolts-only spec.

Fix pass 1 (build report "Fix pass 1") adds these tests:
- T3 gains the slot-bounded gutter (F6), a dark-timber key that makes the W9 floor bind and a
  0.5-slot seam (F7), and protect_px.
- T4: the 3-texel edge band.
- T7: scuff and tone (F3).
- T10: the budget at 0.5.
- T12: the tower preset on lift_tower's vocabulary (F1).
- T13: slot_wear "free".

The fixture builders are importable (tools_weather_demo.py uses them).
"""

import hashlib
import json
import math
import os
import subprocess
import sys
import tempfile
import time
import traceback

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER_DIR = os.path.dirname(HERE)
if BLENDER_DIR not in sys.path:
    sys.path.insert(0, BLENDER_DIR)

import bpy  # noqa: E402

import cs2_asset  # noqa: E402
import cs2_compose as C  # noqa: E402
import cs2_weather as W  # noqa: E402

FAILS = []

A0 = 512
RHO0 = 40.0                 # px/m at A0
MARGIN_PX = 4
TAN25 = math.tan(math.radians(25.0))
SLAB_T = 0.24
SLAB_DZ = SLAB_T / math.cos(math.radians(25.0))
EAVE_Z = 2.5
Z_EAVE_EDGE = EAVE_Z - 0.6 * TAN25
Z_RIDGE = EAVE_Z + 1.5 * TAN25

# material keys (sorted = material index), linear albedo, smoothness, metal, slot
KEY_INFO = {
    "deck":        ((0.20, 0.15, 0.10), 0.25, 0.0, 0),
    "galv":        ((0.45, 0.45, 0.46), 0.35, 1.0, 0),
    "label":       ((0.70, 0.70, 0.70), 0.40, 0.0, 0),
    "slate":       ((0.06, 0.065, 0.075), 0.30, 0.0, 0),
    "steel_paint": ((0.20, 0.21, 0.22), 0.45, 0.0, 0),
    "timber_lap":  ((0.10, 0.07, 0.045), 0.20, 0.0, 0),
    "trim":        ((0.604, 0.604, 0.604), 0.45, 0.0, 1),
    "trim_white":  ((0.65, 0.65, 0.65), 0.40, 0.0, 0),
}
KEYS = sorted(KEY_INFO)
OVERRIDES = (("bar_", W.PartWeather(role="paint_steel")),
             ("steelplate", W.PartWeather(role="paint_steel")),
             ("bracket_g", W.PartWeather(role="galv")))
MAT_EXTRA = (("steel_paint", "paint_steel"),)
STAMPED = ("sign_t_face",)
RESERVED_RECT = (0.90, 0.90, 0.97, 0.97)    # UV rect restored 1:1 (gutter area)


def detail_specs():
    return {"steelplate_hi": C.DetailSpec(seams_m=1.2, bolt_pitch_m=0.15, bolt_r_m=0.018),
            "steelplate_lo": C.DetailSpec(bolt_pitch_m=0.15, bolt_r_m=0.018)}


def kit_spec(name="WeatherKitCube", **kw):
    # The thresholds of the fixture were tuned on the pre-DECISIONS-171 defaults (W8 bounded slot
    # wear, intensity 0.35). They are pinned here, so the module defaults (now "free" / 0.40) do not
    # move the guard checks. T13 still runs both slot_wear modes explicitly.
    args = dict(overrides=OVERRIDES, mat_roles_extra=MAT_EXTRA, slot_wear="bounded",
                intensity=0.35)
    args.update(kw)
    return W.WeatherSpec.preset("building", name, **args)


def check(cond, msg):
    if cond:
        print(f"  ok   {msg}")
    else:
        print(f"  FAIL {msg}")
        FAILS.append(msg)


# --------------------------------------------------------------------------------------
# Fixture
# --------------------------------------------------------------------------------------

BOX_FACES = {"bottom": (0, 3, 2, 1), "top": (4, 5, 6, 7), "-y": (0, 1, 5, 4),
             "+x": (1, 2, 6, 5), "+y": (2, 3, 7, 6), "-x": (3, 0, 4, 7)}


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)


def _face_frame(n):
    n = np.asarray(n, dtype=np.float64)
    if abs(n[2]) < 0.97:
        t = np.cross((0.0, 0.0, 1.0), n)
    else:
        t = np.array((1.0, 0.0, 0.0))
    t /= np.linalg.norm(t)
    v = np.cross(n, t)
    return t, v


class _Kit:
    def __init__(self):
        self.parts = []        # (obj, key)
        self.records = []      # (obj, face index, local (n, 2) meters x factor)

    def add(self, name, key, verts, faces, flags=None):
        """faces: list of vertex-index tuples. flags: {face index: (rho factor, rotate)}."""
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
        me.update()
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        V = np.asarray(verts, dtype=np.float64)
        for i, f in enumerate(faces):
            P = V[list(f)]
            n = C._poly_normal(P)
            t, v = _face_frame(n)
            fac, rot = (flags or {}).get(i, (1.0, False))
            loc = np.stack([(P - P[0]) @ t, (P - P[0]) @ v], axis=1) * fac
            if rot:
                loc = np.stack([-loc[:, 1], loc[:, 0]], axis=1)
            self.records.append((obj, i, loc))
        self.parts.append((obj, key))
        return obj

    def box(self, name, key, lo, hi, drop=(), hidden=(), rho=1.0, rotate=()):
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        verts = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
                 (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        return self.hexa(name, key, verts, drop, hidden, rho, rotate)

    def hexa(self, name, key, verts, drop=(), hidden=(), rho=1.0, rotate=()):
        keys = [k for k in BOX_FACES if k not in drop]
        faces = [BOX_FACES[k] for k in keys]
        flags = {i: ((0.1 if k in hidden else rho), k in rotate) for i, k in enumerate(keys)}
        return self.add(name, key, verts, faces, flags)


def _pack(records, A, rho):
    """Skyline bottom-left pack, one rect per face, MARGIN_PX apart, never over
    RESERVED_RECT (kept free as a gutter). Returns ({record index: (offset_px (2,), scale)},
    used rows). Deterministic: rects in (height, width, index) descending order."""
    sizes = []
    for i, (_o, _f, loc) in enumerate(records):
        lo = loc.min(axis=0)
        wh = (loc.max(axis=0) - lo) * rho
        sizes.append((i, lo, int(math.ceil(wh[0])) + 1, int(math.ceil(wh[1])) + 1))
    order = sorted(sizes, key=lambda s: (-s[3], -s[2], s[0]))
    rs = C._rect_slices(RESERVED_RECT, A)
    ry0, ry1, rx0, rx1 = rs[0].start, rs[0].stop, rs[1].start, rs[1].stop
    M = MARGIN_PX
    sky = [[M, M, A - 2 * M]]                 # skyline segments [x, y, width]
    out = {}
    top = 0
    for i, lo, w, h in order:
        best = None
        for j in range(len(sky)):
            x = sky[j][0]
            if x + w + M > A:
                break
            y, k, span = 0, j, 0
            while span < w + M and k < len(sky):
                y = max(y, sky[k][1])
                span += sky[k][2]
                k += 1
            if span < min(w + M, A - M - x) or y + h + M > A:
                continue
            if x < rx1 + M and x + w + M > rx0 and y < ry1 + M and y + h + M > ry0:
                continue
            if best is None or (y + h, x) < (best[1] + h, best[0]):
                best = (x, y)
        if best is None:
            raise RuntimeError(f"kit cube UV pack overflow at A {A} (rect {w} x {h})")
        x, y = best
        # Rect corners sit on texel EDGES (integer coords). At rho 40 and rho 80 alike, the first
        # texel center sits half a texel inside every face. So no texel center lies on a face
        # boundary, where float rounding decides its face (T7 meter parity).
        out[i] = (np.array((float(x), float(y))) - lo * rho, rho)
        top = max(top, y + h)
        x1 = x + w + M                        # [x, x1) rises to y + h + M
        new = []
        for sx, sy, sw in sky:
            ex = sx + sw
            if ex <= x or sx >= x1:
                new.append([sx, sy, sw])
                continue
            if sx < x:
                new.append([sx, sy, x - sx])
            if ex > x1:
                new.append([x1, sy, ex - x1])
        new.append([x, y + h + M, min(x1, A - M) - x])
        new.sort()
        sky = []
        for seg in new:
            if sky and sky[-1][1] == seg[1] and sky[-1][0] + sky[-1][2] == seg[0]:
                sky[-1][2] += seg[2]
            else:
                sky.append(seg)
    return out, top


def build_kit_cube(name="WeatherKitCube", with_uvmap=False):
    """Build the fixture and return (obj, part table). UVs are normalized (A-independent): rho = 40
    px/m at A 512, 80 at A 1024, ..."""
    clear_scene()
    K = _Kit()
    K.box("wall_t", "timber_lap", (-1.8, -1.5, 0.0), (1.8, 1.5, EAVE_Z), drop=("top",),
          hidden=("bottom",), rotate=("-x",))
    K.add("gable_t0", "timber_lap", [(1.8, -1.5, EAVE_Z), (1.8, 1.5, EAVE_Z), (1.8, 0.0, Z_RIDGE)],
          [(0, 1, 2)])
    K.add("gable_t1", "timber_lap", [(-1.8, 1.5, EAVE_Z), (-1.8, -1.5, EAVE_Z), (-1.8, 0.0, Z_RIDGE)],
          [(0, 1, 2)])
    zs, zr = Z_EAVE_EDGE, Z_RIDGE
    s = [(-1.8, -2.1, zs), (1.8, -2.1, zs), (1.8, 0.0, zr), (-1.8, 0.0, zr),
         (-1.8, -2.1, zs + SLAB_DZ), (1.8, -2.1, zs + SLAB_DZ), (1.8, 0.0, zr + SLAB_DZ),
         (-1.8, 0.0, zr + SLAB_DZ)]
    K.hexa("roof_tS", "slate", s, drop=("+y",))
    nn = [(-1.8, 0.0, zr), (1.8, 0.0, zr), (1.8, 2.1, zs), (-1.8, 2.1, zs),
          (-1.8, 0.0, zr + SLAB_DZ), (1.8, 0.0, zr + SLAB_DZ), (1.8, 2.1, zs + SLAB_DZ),
          (-1.8, 2.1, zs + SLAB_DZ)]
    K.hexa("roof_tN", "slate", nn, drop=("-y",))
    K.box("deck_t", "deck", (-1.8, -2.7, 0.0), (1.8, -1.5, 0.30), drop=("+y",), hidden=("bottom",))
    K.box("step_t0", "deck", (-0.5, -3.0, 0.0), (0.5, -2.7, 0.20), drop=("+y",), hidden=("bottom",))
    K.box("step_t1", "deck", (-0.5, -3.3, 0.0), (0.5, -3.0, 0.10), drop=("+y",), hidden=("bottom",))
    K.box("door_t0_leaf", "trim_white", (-0.45, -1.55, 0.30), (0.45, -1.50, 2.30), drop=("+y",),
          hidden=("bottom",))
    K.box("door_t0_jamb0", "trim_white", (-0.53, -1.58, 0.30), (-0.45, -1.50, 2.30), drop=("+y",),
          hidden=("bottom",))
    K.add("sign_t_face", "label", [(-1.805, 0.3, 1.4), (-1.805, -0.3, 1.4), (-1.805, -0.3, 1.8),
                                   (-1.805, 0.3, 1.8)], [(0, 1, 2, 3)])
    K.box("bracket_g", "galv", (1.8, 0.45, 2.0), (1.9, 0.55, 2.2), drop=("-x",))
    K.box("trim_t", "trim", (3.0, 1.5, 1.0), (3.8, 1.65, 3.0))
    K.box("bar_t", "steel_paint", (3.0, -0.35, 1.0), (3.7, 0.35, 2.6))
    K.add("steelplate_hi", "steel_paint", [(-4.6, -1.0, 0.5), (-3.4, -1.0, 0.5), (-3.4, -1.0, 2.9),
                                            (-4.6, -1.0, 2.9)], [(0, 1, 2, 3)], {0: (1.6, False)})
    K.add("steelplate_lo", "steel_paint", [(-3.4, 1.0, 0.5), (-4.6, 1.0, 0.5), (-4.6, 1.0, 2.9),
                                            (-3.4, 1.0, 2.9)], [(0, 1, 2, 3)], {0: (0.5, False)})
    # UVs: pack at A0 / RHO0, store normalized
    placed, used_h = _pack(K.records, A0, RHO0)
    by_obj = {}
    for i, (obj, fi, loc) in enumerate(K.records):
        off, rho = placed[i]
        by_obj.setdefault(obj.name, {})[fi] = (loc * rho + off) / A0
    mats = []
    for k in KEYS:
        m = bpy.data.materials.get(f"WK_{k}") or bpy.data.materials.new(f"WK_{k}")
        mats.append(m)
    for obj, key in K.parts:
        me = obj.data
        lay = me.uv_layers.new(name="UVUnique")
        for p in me.polygons:
            uv = by_obj[obj.name][p.index]
            for j, li in enumerate(range(p.loop_start, p.loop_start + p.loop_total)):
                lay.data[li].uv = (float(uv[j, 0]), float(uv[j, 1]))
        for m in mats:
            me.materials.append(m)
        for p in me.polygons:
            p.material_index = KEYS.index(key)
    table = cs2_asset.tag_parts(K.parts)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _k in K.parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = K.parts[0][0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    cs2_asset.store_part_table(obj, table)
    if with_uvmap:
        me = obj.data
        src = me.uv_layers["UVUnique"]
        lay = me.uv_layers.new(name="UVMap")
        buf = np.empty(len(me.loops) * 2, dtype=np.float32)
        src.data.foreach_get("uv", buf)
        lay.data.foreach_set("uv", buf)
        me.uv_layers.active = lay
    obj["kit_used_rows"] = int(used_h)
    return obj, table


def poly_materials(me):
    mi = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", mi)
    return mi


def synth_maps(obj, A, f0=None):
    """Synthetic linear maps on the hand layout (module docstring)."""
    me = obj.data
    spec0 = kit_spec()
    if f0 is None:
        f0 = W.build_fields(me, A, spec0, None, detail_specs=detail_specs())
    mi = poly_materials(me)
    tmi = mi[f0.face]
    info = [KEY_INFO[k] for k in KEYS]
    alb = np.array([i[0] for i in info], dtype=np.float32)
    smo = np.array([i[1] for i in info], dtype=np.float32)
    met = np.array([i[2] for i in info], dtype=np.float32)
    slot = np.array([i[3] for i in info], dtype=np.int32)
    base = np.zeros((A * A, 4), dtype=np.float32)
    base[:, :3] = 0.5
    base[:, 3] = 1.0
    nrm = np.zeros((A * A, 4), dtype=np.float32)
    nrm[:] = (0.5, 0.5, 1.0, 1.0)
    msk = np.zeros((A * A, 4), dtype=np.float32)
    msk[:, 3] = 0.5
    ctl = np.zeros((A * A, 4), dtype=np.float32)
    ao = np.ones(A * A, dtype=np.float32)
    idx = f0.idx
    nz = f0.ngeo[:, 2].astype(np.float32)
    under = (np.abs(nz) < 0.5) & (f0.cover_h <= 0.3)
    ao[idx[under]] = 0.4
    occ = ao[idx] * 0.92 + 0.08
    base[idx, :3] = alb[tmi] * occ[:, None]
    msk[idx, 0] = met[tmi]
    msk[idx, 3] = smo[tmi]
    ctl[idx, 0] = (slot[tmi] == 1).astype(np.float32)
    return W.WeatherMaps(base.reshape(A, A, 4), nrm.reshape(A, A, 4), msk.reshape(A, A, 4),
                         ctl.reshape(A, A, 4), ao.reshape(A, A)), f0


def run_layer(obj, A, spec, maps=None, f0=None, **kw):
    me = obj.data
    if maps is None:
        maps, f0 = synth_maps(obj, A, f0)
    fl = W.build_fields(me, A, spec, maps, detail_specs=detail_specs(), stamped=STAMPED,
                        reserved=[RESERVED_RECT])
    masks = W.compute_masks(maps, fl, spec)
    out = W.apply_weathering(maps, fl, spec, masks=masks, **kw)
    return maps, fl, masks, out


def part_sel(fl, prefix):
    ids = [i + 1 for i, n in enumerate(fl.part_names) if n.startswith(prefix)]
    return np.isin(fl.tpart, ids)


def L_of(arr, fl):
    return arr.reshape(-1, arr.shape[-1])[fl.idx, :3].astype(np.float64) @ W.LUMA.astype(np.float64)


def S_of(arr, fl):
    return arr.reshape(-1, arr.shape[-1])[fl.idx, 3].astype(np.float64)


def outputs_sha1(out, fl):
    h = hashlib.sha1()
    for a in (out.base, out.nrm, out.msk):
        h.update(np.ascontiguousarray(a, dtype=np.float32).tobytes())
    for n in fl.COMPACT + ("idx",):
        h.update(np.ascontiguousarray(getattr(fl, n)).tobytes())
    return h.hexdigest()


# --------------------------------------------------------------------------------------
# Tests
# --------------------------------------------------------------------------------------

def t1_determinism(obj):
    print("[T1] determinism")
    spec = kit_spec()
    maps, f0 = synth_maps(obj, A0)
    _m, fa, _ma, oa = run_layer(obj, A0, spec, maps)
    _m, fb, _mb, ob = run_layer(obj, A0, spec, maps)
    same = all(np.array_equal(getattr(oa, k), getattr(ob, k)) for k in ("base", "nrm", "msk"))
    same_f = all(np.array_equal(getattr(fa, n), getattr(fb, n)) for n in fa.COMPACT + ("idx",))
    check(same and same_f, "two runs: maps and every compact field array_equal")
    spec2 = kit_spec("WeatherKitCubeB")
    _m, _f2, _m2, o2 = run_layer(obj, A0, spec2, maps)
    check(not np.array_equal(oa.base, o2.base), "another name gives different noise")
    sha = outputs_sha1(oa, fa)
    out_json = os.path.join(tempfile.gettempdir(), "cs2_weather_child.json")
    if os.path.exists(out_json):
        os.remove(out_json)
    cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
           "--python", os.path.abspath(__file__), "--", "--child", out_json]
    t0 = time.perf_counter()
    r = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
    child = None
    if os.path.exists(out_json):
        with open(out_json) as fh:
            child = json.load(fh).get("sha1")
    check(r.returncode == 0 and child == sha,
          f"child Blender process reproduces sha1 {sha[:12]} (child {str(child)[:12]}, "
          f"exit {r.returncode}, {time.perf_counter() - t0:.1f} s)")
    return maps, f0


def t2_parity(obj):
    print("[T2] field parity with cs2_compose.rasterise_face_fields")
    for A in (A0, 2 * A0):
        fl = W.build_fields(obj.data, A, kit_spec(), None)
        pm, dm = C.rasterise_face_fields(obj.data, A)
        check(np.array_equal(fl.part, pm) and np.array_equal(fl.ppm, dm),
              f"A {A}: part / ppm array_equal ({fl.N} covered texels)")


def _gutter_far(fl, steps):
    A = fl.A
    cov = np.zeros(A * A, dtype=bool)
    cov[fl.idx] = True
    cov = cov.reshape(A, A)
    near = cov.copy()
    for _ in range(steps):
        g = near.copy()
        g[1:, :] |= near[:-1, :]
        g[:-1, :] |= near[1:, :]
        g[:, 1:] |= near[:, :-1]
        g[:, :-1] |= near[:, 1:]
        near = g
    return ~near


def _dilated_ctl(maps, fl, steps=3, value=1.0):
    """A copy of the maps whose ControlMask R is dilated `steps` texels into the uncovered
    gutter around slot texels (what a real bake's margin does), at `value`."""
    A = fl.A
    ctl = maps.ctl.copy()
    cov = np.zeros(A * A, bool)
    cov[fl.idx] = True
    cov = cov.reshape(A, A)
    slot = ctl[..., 0] > 0
    grow = slot.copy()
    for _ in range(steps):
        g = grow.copy()
        g[1:, :] |= grow[:-1, :]
        g[:-1, :] |= grow[1:, :]
        g[:, 1:] |= grow[:, :-1]
        g[:, :-1] |= grow[:, 1:]
        grow = g
    gut = grow & ~cov
    ctl[gut, 0] = value
    return W.WeatherMaps(maps.base, maps.nrm, maps.msk, ctl, maps.ao), gut


def t3_extra(obj, maps, f0):
    """Fix pass 1: F6 slot-bounded gutter, F7 floor binding + 0.5-slot seam, protect_px."""
    # F6: gutter texels under a dilated ControlMask obey +-slot_bound / slot
    for val in (1.0, 0.5):
        m2, gut = _dilated_ctl(maps, f0, 3, val)
        spec = kit_spec(intensity=1.0)
        _m, fl, _ms, out = run_layer(obj, A0, spec, m2, f0)
        b0 = m2.base[gut, :3].astype(np.float64)
        b1 = out.base[gut, :3].astype(np.float64)
        moved = np.abs(b1 - b0).max(axis=-1) > 0
        rr = (np.abs(b1 - b0) / np.maximum(b0, 1e-9)).max() if moved.any() else 0.0
        check(moved.sum() > 0 and rr <= 0.10 / val + 1e-5,
              f"F6 gutter under ControlMask {val}: {int(moved.sum())} texels moved, "
              f"worst |d|/in {rr:.4f} <= {0.10 / val:.2f}")
    # F7 uses a dark-timber key (Y < 0.03), which makes the W9 floor bind, and a 0.5-slot seam
    # strip.
    dark = W.WeatherMaps(maps.base.copy(), maps.nrm, maps.msk, maps.ctl.copy(), maps.ao)
    wsel = part_sel(f0, "wall_t")
    wi = f0.idx[wsel]
    dark.base.reshape(-1, 4)[wi, :3] *= 0.3
    seam = wi[: min(400, len(wi))]
    dark.ctl.reshape(-1, 4)[seam, 0] = 0.5
    spec = kit_spec(intensity=1.0)
    m, fl, _ms, out = run_layer(obj, A0, spec, dark, f0)
    spec0 = kit_spec(intensity=1.0, albedo_floor=0.0)
    _m0, _f0b, _ms0, out0 = run_layer(obj, A0, spec0, dark, f0)
    b0 = m.base.reshape(-1, 4)[fl.idx, :3].astype(np.float64)
    b1 = out.base.reshape(-1, 4)[fl.idx, :3].astype(np.float64)
    bn = out0.base.reshape(-1, 4)[fl.idx, :3].astype(np.float64)
    occ = m.ao.reshape(-1)[fl.idx].astype(np.float64) * 0.92 + 0.08
    LU = W.LUMA.astype(np.float64)
    L0, L1, Ln = b0 @ LU, b1 @ LU, bn @ LU
    bound = (Ln < L1 - 1e-9) & (L0 / occ < 0.03)
    drk = L1 < L0
    gap = (L1[drk] / occ[drk] - np.minimum(L0[drk] / occ[drk], 0.03)).min() if drk.any() else 0.0
    check(int(bound.sum()) > 100 and gap >= -1e-6,
          f"F7 W9 floor binds on {int(bound.sum())} dark-timber texels (L/occ < 0.03) and holds "
          f"(worst gap {gap:.2e})")
    s_c = np.isin(fl.idx, seam)
    rr = (np.abs(b1[s_c] - b0[s_c]) / np.maximum(b0[s_c], 1e-9)).max()
    check(rr <= 0.20 + 1e-5, f"F7 0.5-slot seam texels: |d|/in max {rr:.4f} <= 0.20 (slot_bound / 0.5)")
    # protect_px: a callable rect inside the wall island stays byte-identical, the rest weathers
    r = C.uv_bbox_px(obj.data, f0.part_names, "wall_t", A0)
    mid = ((r[0] + r[1]) // 2, (r[2] + r[3]) // 2)
    rect = (mid[0] - 10, mid[0] + 10, mid[1] - 10, mid[1] + 10)
    spec = kit_spec()
    fl = W.build_fields(obj.data, A0, spec, maps, detail_specs=detail_specs(), stamped=STAMPED,
                        reserved=[RESERVED_RECT], protect_px=lambda me, A: [rect])
    out = W.apply_weathering(maps, fl, spec)
    sl = (slice(rect[0], rect[1]), slice(rect[2], rect[3]))
    same = all(np.array_equal(getattr(out, k)[sl], getattr(maps, k)[sl]) for k in ("base", "nrm", "msk"))
    ws = part_sel(fl, "wall_t") & ~fl.protect_c
    changed = int((np.abs(out.base.reshape(-1, 4)[fl.idx[ws], :3]
                          - maps.base.reshape(-1, 4)[fl.idx[ws], :3]).max(axis=-1) > 0).sum())
    check(same and changed > 1000 and fl.stats["protect_px_rects"] == [list(rect)],
          f"protect_px rect {rect} byte-identical; {changed} other wall_t texels changed")


def t3_guards(obj, maps, f0):
    print("[T3] guards")
    for inten in (0.35, 1.0):
        spec = kit_spec(intensity=inten)
        ctl_bytes = maps.ctl.tobytes()
        m, fl, masks, out = run_layer(obj, A0, spec, maps, f0)
        check(np.array_equal(out.base[..., 3], m.base[..., 3])
              and np.array_equal(out.nrm[..., 3], m.nrm[..., 3]), f"{inten}: G1 BaseColor A / Normal A kept")
        check(np.array_equal(out.msk[..., 1:3], m.msk[..., 1:3]), f"{inten}: G2 MaskMap G/B kept")
        check(out.ctl is m.ctl and m.ctl.tobytes() == ctl_bytes, f"{inten}: G5 ControlMask same object, bytes kept")
        b0 = m.base.reshape(-1, 4)[fl.idx, :3].astype(np.float64)
        b1 = out.base.reshape(-1, 4)[fl.idx, :3].astype(np.float64)
        slot = m.ctl.reshape(-1, 4)[fl.idx, 0] >= 0.999
        rr = (np.abs(b1[slot] - b0[slot]) / np.maximum(b0[slot], 1e-9)).max()
        check(rr <= 0.10 + 1e-5, f"{inten}: G3 full-slot texels |d|/in max {rr:.5f} <= 0.10")
        occ = m.ao.reshape(-1)[fl.idx].astype(np.float64) * 0.92 + 0.08
        L0, L1 = b0 @ W.LUMA.astype(np.float64), b1 @ W.LUMA.astype(np.float64)
        dark = L1 < L0
        gap = (L1[dark] / occ[dark] - np.minimum(L0[dark] / occ[dark], 0.03)).min() if dark.any() else 0
        check(gap >= -1e-6, f"{inten}: G4 albedo floor (worst gap {gap:.2e}, {int(dark.sum())} darkened)")
        r = C.uv_bbox_px(obj.data, fl.part_names, "sign_t_face", A0)
        sl = (slice(r[0], r[1]), slice(r[2], r[3]))
        rs = C._rect_slices(RESERVED_RECT, A0)
        ok6 = all(np.array_equal(getattr(out, k)[s], getattr(m, k)[s]) for k in ("base", "nrm", "msk")
                  for s in (sl, rs))
        check(ok6, f"{inten}: G6 sign_t_face bbox {r} and the reserved rect byte-identical")
        check(out.base[..., :3].min() >= 0 and out.base[..., :3].max() <= 1
              and out.msk.min() >= 0 and out.msk.max() <= 1, f"{inten}: G7 ranges")
        far = _gutter_far(fl, 3)
        same_far = all(np.array_equal(getattr(out, k)[far], getattr(m, k)[far]) for k in ("base", "nrm", "msk"))
        cov = np.zeros(A0 * A0, bool)
        cov[fl.idx] = True
        gut = (~cov.reshape(A0, A0)) & (~far)
        moved = int((np.abs(out.base[gut] - m.base[gut]).max(axis=-1) > 0).sum())
        check(same_far and moved > 0, f"{inten}: G8 far gutter identical; {moved} near-gutter texels got deltas")
        rep = W.weather_report(m, out, fl, spec, masks)
        g = rep["guards"]
        check(all(g[k] for k in ("G1_alpha_kept", "G2_msk_GB_kept", "G5_ctl_identity",
                                 "G6_protect_identical", "G7_range_ok"))
              and g["G3_worst_full_slot_rel"] <= 0.10 + 1e-5 and g["G4_worst_floor_gap"] >= -1e-6,
              f"{inten}: report guards {g}")
    t3_extra(obj, maps, f0)


def t4_edge(obj, maps, f0):
    print("[T4] edge wear")
    res = {}
    for inten in (0.35, 1.0):
        m, fl, masks, out = run_layer(obj, A0, kit_spec(intensity=inten), maps, f0)
        L0, L1 = L_of(m.base, fl), L_of(out.base, fl)
        ratio = L1 / np.maximum(L0, 1e-9)
        for pre in ("bar_t", "trim_t"):
            s = part_sel(fl, pre)
            e = s & (fl.edge_d <= 0.03)
            c = s & (fl.edge_d >= 0.30)
            res[(pre, inten)] = (ratio[e].mean() / ratio[c].mean(), int(e.sum()), int(c.sum()))
        s = part_sel(fl, "bar_t") & (fl.edge_d <= 0.03)
        res[("dS", inten)] = (S_of(out.msk, fl)[s] - S_of(m.msk, fl)[s]).mean()
    R, ne, nc = res[("bar_t", 0.35)]
    check(R >= 1.05, f"steel bar at 0.35: R {R:.4f} >= 1.05 ({ne} edge / {nc} center texels)")
    R1 = res[("bar_t", 1.0)][0]
    check(R1 >= 1.12, f"steel bar at 1.0: R {R1:.4f} >= 1.12")
    Rt = res[("trim_t", 0.35)][0]
    Rt1 = res[("trim_t", 1.0)][0]
    check(1.02 <= Rt <= 1.10 + 1e-3 and Rt1 <= 1.10 + 1e-3,
          f"slot-1 trim: R {Rt:.4f} at 0.35 in [1.02, 1.10], {Rt1:.4f} at 1.0 <= 1.10")
    check(res[("dS", 0.35)] < -0.02, f"smoothness drops at bar edges (mean {res[('dS', 0.35)]:+.4f} < -0.02)")
    # Fix pass 1: the band is never narrower than EDGE_MIN_TEXELS texels (it read as a 1-2 texel
    # line at 39-46 px/m). The test measures this on the bar at rho 40 and 80.
    for A in (A0, 2 * A0):
        m2, f2 = synth_maps(obj, A)
        _m, fl, masks, _o = run_layer(obj, A, kit_spec(), m2, f2)
        s = part_sel(fl, "bar_t") & (masks["edge"] > 0.05)
        ppm = float(fl.ppm.reshape(-1)[fl.idx][part_sel(fl, "bar_t")].mean())
        reach = float(fl.edge_d[s].max()) * ppm
        check(reach >= 2.5, f"A {A}: bar edge band reaches {reach:.2f} texels from the edge (>= 2.5; "
                            f"band >= {W.EDGE_MIN_TEXELS:.0f} texels at {ppm:.0f} px/m)")


def t5_grime(obj, maps, f0):
    print("[T5] grime gradient")
    # tone off: the 1-3 m blotches are not part of the ground gradient under test
    spec = kit_spec(tone=0.0)
    m, fl, masks, out = run_layer(obj, A0, spec, maps, f0)
    L0, L1 = L_of(m.base, fl), L_of(out.base, fl)
    ratio = L1 / np.maximum(L0, 1e-9)
    z = fl.pos[:, 2]
    nz = fl.ngeo[:, 2].astype(np.float64)
    wall = part_sel(fl, "wall_t") & (np.abs(nz) < 0.5) & (fl.edge_d > 0.2)
    open_ = wall & (fl.cover_h > spec.eave_band_m + 0.1)
    b = spec.ground_band_m                       # 0.6 m since fix pass 1 (design 0.4)
    bands = ((0.0, b / 4), (b / 4, b / 2), (b / 2, 3 * b / 4), (3 * b / 4, b), (1.5 * b, 1.5 * b + 0.4))
    vals = []
    for lo, hi in bands:
        s = open_ & (z >= lo) & (z < hi)
        vals.append((ratio[s].mean(), int(s.sum())))
    r = [v[0] for v in vals]
    check(all(r[i] < r[i + 1] for i in range(3)),
          f"L ratio rises over the four quarter bands of 0-{b:.2f} m: {[round(x, 4) for x in r]} "
          f"(n {[v[1] for v in vals]})")
    check(r[0] <= 0.92, f"first band {r[0]:.4f} <= 0.92 (deepened contact, GROUND_GAIN {W.GROUND_GAIN})")
    check(r[4] >= 0.99, f"band {1.5 * b:.2f}-{1.5 * b + 0.4:.2f} m {r[4]:.4f} >= 0.99")
    near = wall & (fl.cover_h <= 0.3)
    farc = wall & (fl.cover_h >= 1.5) & (z > 0.6)
    check(ratio[near].mean() < ratio[farc].mean(),
          f"under the soffit: cover_h <= 0.3 ratio {ratio[near].mean():.4f} < cover_h >= 1.5 "
          f"{ratio[farc].mean():.4f} (n {int(near.sum())} / {int(farc.sum())})")


def t6_snow(obj, maps, f0):
    print("[T6] snow")
    spec0 = kit_spec()
    m, fl, masks, out = run_layer(obj, A0, spec0, maps, f0)
    out2 = W.apply_weathering(m, fl, spec0, masks=masks,
                              effects=tuple(e for e in W.EFFECTS if e != "snow"))
    check(float(masks["snow"].max()) == 0.0
          and all(np.array_equal(getattr(out, k), getattr(out2, k)) for k in ("base", "nrm", "msk")),
          "snow 0: snow mask all 0 and maps identical to the run without the snow path")
    spec = kit_spec(snow=0.6)
    m, fl, masks, out = run_layer(obj, A0, spec, maps, f0)
    sig = masks["snow"]
    roof = part_sel(fl, "roof_t") & (fl.ngeo[:, 2].astype(np.float64) > 0.7)
    deep = roof & (fl.edge_d >= 0.6)
    cov = float((sig[deep] >= 0.5).mean())
    check(0.45 <= cov <= 0.75, f"roof coverage (edge_d >= 0.6) {cov:.3f} in [0.45, 0.75] (n {int(deep.sum())})")
    nz = fl.ngeo[:, 2].astype(np.float64)
    walls = nz < 0.7
    slot = m.ctl.reshape(-1, 4)[fl.idx, 0] > 0
    deck_cov = part_sel(fl, "deck_t") & np.isfinite(fl.cover_h)
    check(sig[walls].max() == 0 and sig[slot].max() == 0 and sig[deck_cov].max() == 0
          and int(deck_cov.sum()) > 0,
          f"walls, slot texels and the deck under the overhang ({int(deck_cov.sum())} texels) get 0")
    inset = roof & (fl.edge_d < spec.snow_inset_m)
    check(float((sig[inset] >= 0.5).mean()) <= 0.05, f"within snow_inset of the eave: "
          f"{float((sig[inset] >= 0.5).mean()):.3f} <= 0.05")
    full = sig >= 0.999
    sm = S_of(out.msk, fl)
    met = out.msk.reshape(-1, 4)[fl.idx, 0]
    nb = out.nrm.reshape(-1, 4)[fl.idx, 2]
    check(full.any() and sm[full].max() <= 0.12 and met[full].max() == 0 and nb[full].min() > 0.5,
          f"snowed texels ({int(full.sum())}): smoothness <= 0.12, metal 0, normal B > 0.5")
    try:
        W.WeatherSpec.preset("carrier", "WeatherCarrier", snow=0.3)
        check(False, "carrier with snow > 0 raises")
    except ValueError:
        check(True, "carrier with snow > 0 raises ValueError")


def _down2(a):
    A = a.shape[0] // 2
    return a.reshape(A, 2, A, 2).mean(axis=(1, 3))


def t7_density(obj):
    print("[T7] density: rho 40 (A 512) vs rho 80 (A 1024)")
    spec = kit_spec(snow=0.6)
    res = {}
    for A in (A0, 2 * A0):
        maps, f0 = synth_maps(obj, A)
        m, fl, masks, out = run_layer(obj, A, spec, maps, f0)
        res[A] = (fl, masks)
    fc, mc = res[A0]
    ff, mf = res[2 * A0]
    covf = np.zeros(4 * A0 * A0, bool)
    covf[ff.idx] = True
    allf = _down2(covf.reshape(2 * A0, 2 * A0).astype(np.float64)) >= 1.0
    covc = np.zeros(A0 * A0, bool)
    covc[fc.idx] = True
    both = allf & covc.reshape(A0, A0)
    # The 3-texel floor of the edge band (fix pass 1) widens the band on purpose where the floor
    # binds everywhere on a part. It also widens the band on parts thinner than two bands (step
    # risers, door leaf, trim board). The band covers their faces, so the widening shows 1:1. The
    # test asserts meter parity on the remaining parts.
    ppm_c = np.bincount(fc.tpart, fc.ppm.reshape(-1)[fc.idx], minlength=len(fc.part_names) + 1) \
        / np.maximum(np.bincount(fc.tpart, minlength=len(fc.part_names) + 1), 1)
    bmax = spec.edge_band_m * (W.EDGE_VAR[0] + W.EDGE_VAR[1])
    edge_floor_parts = []
    for p, n in enumerate(fc.part_names, start=1):
        sp = fc.tpart == p
        if not sp.any():
            continue
        dims = np.sort(np.ptp(fc.pos[sp].astype(np.float64), axis=0))
        minor = dims[1] if dims[0] < 1e-3 else dims[0]      # a planar part: its in-plane minor
        thin = float(minor) < 2.0 * bmax
        if (ppm_c[p] > 0 and W.EDGE_MIN_TEXELS / ppm_c[p] > bmax) or thin:
            edge_floor_parts.append(n)
    print(f"    edge parity skips floor-bound / thin parts {edge_floor_parts}")
    for eff, rmin in (("edge", 0.70), ("grime", 0.85), ("rust", 0.70), ("snow", 0.85),
                      ("scuff", 0.90), ("tone", 0.90)):
        worst = 0.0
        detail = []
        for p, name in enumerate(fc.part_names, start=1):
            if eff == "tone":
                break                          # signed, zero-mean: Pearson r only
            if eff == "rust" and name.startswith("steelplate"):
                continue                       # lattice rust is density-gated (T9)
            if eff == "edge" and name in edge_floor_parts:
                continue
            sc_ = fc.tpart == p
            sf_ = ff.tpart == p
            a, b = float(mc[eff][sc_].mean()), float(mf[eff][sf_].mean())
            if max(a, b) < 0.005:
                continue
            rel = abs(a - b) / max(a, b)
            detail.append((name, round(a, 4), round(b, 4)))
            worst = max(worst, rel)
        if eff != "tone":
            check(worst <= 0.10, f"{eff}: per-part mean masks agree within 10 % (worst {worst:.3f}; {detail[:6]})")
        full_c = W.scatter(fc, mc[eff])
        full_f = _down2(W.scatter(ff, mf[eff]).astype(np.float64))
        x, y = full_c[both].astype(np.float64), full_f[both]
        if eff == "rust":
            keep = ~np.isin(fc.part, [i + 1 for i, n in enumerate(fc.part_names)
                                      if n.startswith("steelplate")])[both]
            x, y = x[keep], y[keep]
        sel = (x != 0) | (y != 0)
        r = float(np.corrcoef(x[sel], y[sel])[0, 1]) if sel.sum() > 10 else 0.0
        check(r >= rmin, f"{eff}: Pearson r (2x2 box-downsampled fine vs coarse) {r:.3f} >= {rmin} "
                         f"(n {int(sel.sum())})")


def t8_gravity(obj, maps, f0):
    print("[T8] gravity / streaks")
    spec = kit_spec()
    m, fl, masks, out = run_layer(obj, A0, spec, maps, f0)
    nz = fl.ngeo[:, 2].astype(np.float64)
    du = fl.down_uv.astype(np.float64)
    walls = (np.abs(nz) < 0.1) & ~fl.protect_c
    nx = fl.ngeo[:, 0].astype(np.float64)
    rot = part_sel(fl, "wall_t") & (nx < -0.99)
    up = walls & ~rot
    ang = np.degrees(np.arccos(np.clip(du[up] @ np.array((0.0, -1.0)), -1, 1)))
    check(ang.max() <= 1.0, f"down_uv = (0, -1) on {int(up.sum())} wall texels (max {ang.max():.3f} deg)")
    angr = np.degrees(np.arccos(np.clip(du[rot] @ np.array((1.0, 0.0)), -1, 1)))
    check(rot.any() and angr.max() <= 1.0,
          f"rotated island: down_uv = (1, 0) (max {angr.max():.3f} deg, {int(rot.sum())} texels)")
    flat = np.abs(nz) > 0.97
    check(np.abs(du[flat]).max() == 0.0, "down_uv 0 on |n.z| > 0.97")
    wx = part_sel(fl, "wall_t") & (nx > 0.99)
    y, z = fl.pos[:, 1].astype(np.float64), fl.pos[:, 2].astype(np.float64)
    rs = fl.rust_src
    hot = wx & (rs > 0.05)
    check(hot.any() and y[hot].min() >= 0.45 - 0.05 - 1e-6 and y[hot].max() <= 0.55 + 0.05 + 1e-6
          and z[hot].max() <= 2.0 + 1e-6,
          f"bracket rust > 0.05 only within width +-0.05 and below z 2.0 "
          f"(y {y[hot].min():.3f}..{y[hot].max():.3f}, z max {z[hot].max():.3f})")
    core = wx & (np.abs(y - 0.5) <= 0.03)
    bins = [rs[core & (z < 2.0 - 0.2 * k) & (z >= 1.8 - 0.2 * k)].mean() for k in range(3)]
    check(bins[0] > bins[1] > bins[2] > 0, f"mean rust_src falls over 0.2 m bins below: {[round(b, 3) for b in bins]}")
    # structure tensor of the streak mask in texture space
    full = W.scatter(fl, masks["rust"]).astype(np.float64)
    reg = W.scatter(fl, wx & (z < 2.0) & (z > 0.6) & (np.abs(y - 0.5) < 0.25))
    rr = np.flatnonzero(reg.any(axis=1))
    cc = np.flatnonzero(reg.any(axis=0))
    win = full[rr.min():rr.max() + 1, cc.min():cc.max() + 1] * reg[rr.min():rr.max() + 1, cc.min():cc.max() + 1]
    gy, gx = np.gradient(win)
    J = np.array([[np.sum(gx * gx), np.sum(gx * gy)], [np.sum(gx * gy), np.sum(gy * gy)]])
    w, V = np.linalg.eigh(J)
    along = V[:, 0]                                     # least change = along the streaks
    d = du[wx][0]
    ang = math.degrees(math.acos(min(1.0, abs(float(along @ d)) / max(np.linalg.norm(d), 1e-9))))
    check(ang <= 15.0, f"streak structure tensor long axis within 15 deg of down_uv ({ang:.2f} deg)")


def t9_lattice(obj, maps, f0):
    print("[T9] lattice rust")
    spec = kit_spec()
    m, fl, masks, out = run_layer(obj, A0, spec, maps, f0)
    hi = part_sel(fl, "steelplate_hi")
    lo = part_sel(fl, "steelplate_lo")
    ppm = float(fl.ppm.reshape(-1)[fl.idx][hi].mean())
    check(ppm >= 60.0, f"steelplate_hi at {ppm:.1f} px/m (>= 60: bolts drawn)")
    x = fl.pos[:, 0].astype(np.float64)
    z = fl.pos[:, 2].astype(np.float64)
    cols = (fl.idx % fl.A)[hi]
    rs = fl.rust_src[hi].astype(np.float64)
    xs = x[hi]
    uc = np.unique(cols)
    cm = np.array([rs[cols == c].mean() for c in uc])
    cx = np.array([xs[cols == c].mean() for c in uc])
    texel = 1.0 / ppm
    worst = 0.0
    n = 0
    for j in range(-31, -22):
        xc = (j + 0.5) * 0.15
        if xc < cx.min() + 0.02 or xc > cx.max() - 0.02:
            continue
        win = np.abs(cx - xc) <= 0.075
        xp = cx[win][np.argmax(cm[win])]
        worst = max(worst, abs(xp - xc))
        n += 1
    check(n >= 6 and worst <= texel + 1e-6,
          f"column means peak within 1 texel of (j + 1/2) 0.15 m on {n} columns (worst {worst * 1000:.1f} mm, texel {texel * 1000:.1f} mm)")
    top = hi & (z > 2.55 + 0.018 + texel)
    check(top.any() and fl.rust_src[top].max() == 0.0,
          f"nothing above the top bolt row z 2.55 ({int(top.sum())} texels)")
    cur = hi & (z < 2.4 - 0.02) & (z > 2.3) & (np.abs(((x / 0.15) % 1.0) - 0.5) > 0.4)
    check(fl.rust_src[cur].max() > 0.1, f"ring-seam curtain below the 2.4 m seam between columns "
          f"(max {fl.rust_src[cur].max():.3f})")
    plo = float(fl.ppm.reshape(-1)[fl.idx][lo].mean())
    check(plo <= 25.0 and fl.rust_src[lo].max() == 0.0,
          f"steelplate_lo at {plo:.1f} px/m: no lattice rust (bolts below the floor)")
    check(fl.stats["lattice_faces"] >= 1, f"lattice faces {fl.stats['lattice_faces']}")


def textured(maps, f0, cov=0.25, seed=0x5EED):
    """The synthetic maps with a CC0-like albedo variation: base x (1 + cov * f). f is a unit-variance
    hashed fbm of the texel's world position (0.15 m cells), clipped to +-2 sigma. VL4 measures wear
    against the texture variation already there (design 8). The flat synthetic colors have none (D10
    floors std at 10 % of the mean). So the test asserts the budget on this variant. The shipped L2
    parts measure CoV 0.18-1.3."""
    f = W.fbm(f0.pos.astype(np.float64), 0.15, f0.ppm.reshape(-1)[f0.idx], seed)
    k = 1.0 + cov * np.clip(f.astype(np.float64), -2.0, 2.0)
    base = maps.base.copy()
    bf = base.reshape(-1, 4)
    slot = maps.ctl.reshape(-1, 4)[f0.idx, 0] > 0           # recolor texels stay flat
    k = np.where(slot, 1.0, k)
    bf[f0.idx, :3] = np.clip(bf[f0.idx, :3] * k[:, None], 0.0, 1.0)
    return W.WeatherMaps(base, maps.nrm, maps.msk, maps.ctl, maps.ao)


def t10_budget(obj, maps, f0):
    print("[T10] vanilla budget at 0.35 and 0.5 (fix pass 1: WARN-free up to 0.5)")
    rep = None
    for inten in (0.35, 0.5):
        spec = kit_spec(intensity=inten)
        m, fl, masks, out = run_layer(obj, A0, spec, maps, f0)
        v = W.weather_report(m, out, fl, spec, masks)["vanilla"]
        print(f"    flat synthetic colors (VL4 floored, informative): VL1 {v['VL1_mean_rel_dL']} "
              f"VL2 {v['VL2_share_rel_dL_gt_0.15']} VL4 {v['VL4_max']} "
              f"{[(x['part'], x['ratio']) for x in v['VL4_parts']]}")
        m, fl, masks, out = run_layer(obj, A0, spec, textured(maps, f0), f0)
        r = W.weather_report(m, out, fl, spec, masks)
        v = r["vanilla"]
        p = r["perceptual"]
        check(v["ok"], f"{inten}: vanilla ok: VL1 {v['VL1_mean_rel_dL']} VL2 {v['VL2_share_rel_dL_gt_0.15']} "
                       f"VL3 {v['VL3_mean_dsmooth']}/{v['VL3_max_abs_dsmooth']} VL4 {v['VL4_max']} "
                       f"VL5 {v['VL5_snow_share']} failed {v['failed']}; dL* {p['mean_abs_dLstar']} "
                       f">2 {p['share_dLstar_gt_2']}")
        rep = rep or r
    print("    coverage", rep["coverage"])
    print("    roles", rep["roles"], "sources", rep["sources"])
    return rep


def build_tower_stub(name="WeatherTowerStub"):
    """lift_tower's part vocabulary (lift_tower.py:2084-2090, 2003-2047) as boxes: the
    concrete foundation `pad` and `pier`, a galv `mast`, the crash pad `crashpad_outer` /
    `crashpad`. Material keys as material_sources orders them (sorted keys)."""
    clear_scene()
    K = _Kit()
    K.box("pad", "concrete", (-0.6, -0.6, -0.30), (0.6, 0.6, 0.15), drop=("bottom",))
    K.box("pier", "concrete", (-0.6, -0.6, -1.5), (0.6, 0.6, -0.30), drop=("top", "bottom"))
    K.box("mast", "galv", (-0.15, -0.15, 0.15), (0.15, 0.15, 3.0), drop=("bottom",))
    K.box("crashpad_outer", "pad", (0.30, -0.45, 0.20), (0.36, 0.45, 1.40), drop=("-x",))
    K.box("crashpad", "pad", (0.18, -0.45, 0.20), (0.30, 0.45, 1.40), drop=("+x",))
    keys = sorted({k for _o, k in K.parts})
    placed, _h = _pack(K.records, 256, 40.0)
    by_obj = {}
    for i, (o, fi, loc) in enumerate(K.records):
        off, rho = placed[i]
        by_obj.setdefault(o.name, {})[fi] = (loc * rho + off) / 256
    mats = [bpy.data.materials.get(f"WT_{k}") or bpy.data.materials.new(f"WT_{k}") for k in keys]
    for o, key in K.parts:
        me = o.data
        lay = me.uv_layers.new(name="UVUnique")
        for p in me.polygons:
            uv = by_obj[o.name][p.index]
            for j, li in enumerate(range(p.loop_start, p.loop_start + p.loop_total)):
                lay.data[li].uv = (float(uv[j, 0]), float(uv[j, 1]))
        for m in mats:
            me.materials.append(m)
        for p in me.polygons:
            p.material_index = keys.index(key)
    table = cs2_asset.tag_parts(K.parts)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _k in K.parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = K.parts[0][0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    cs2_asset.store_part_table(obj, table)
    return obj, keys


def t12_tower_preset():
    print("[T12] tower preset on lift_tower's vocabulary (checker F1)")
    spec = W.WeatherSpec.preset("tower", "WeatherTowerStub")
    check(all(not p.startswith("pad") for p, _w in spec.overrides)
          and any(p == "crashpad" for p, _w in spec.overrides),
          f"preset('tower') overrides {[p for p, _w in spec.overrides]}: 'crashpad' only, never 'pad'")
    obj, keys = build_tower_stub()
    want_pad = ("masonry", "generic")
    for label, kw in (("prefix roles", {}),
                      ("mat_keys", {"mat_keys": keys}),
                      ("lift-wrap mat_roles", {"mat_roles": {keys.index("concrete"): "generic",
                                                             keys.index("galv"): "galv",
                                                             keys.index("pad"): "pad"}})):
        fl = W.build_fields(obj.data, 256, spec, None, **kw)
        roles = {n: W.ROLES[int(fl.part_role[i])] for i, n in enumerate(fl.part_names, start=1)}
        check(roles["pad"] in want_pad and roles["pier"] in want_pad
              and roles["crashpad"] == "pad" and roles["crashpad_outer"] == "pad",
              f"{label}: foundation pad -> {roles['pad']}, pier -> {roles['pier']}, crashpad / "
              f"crashpad_outer -> {roles['crashpad']} / {roles['crashpad_outer']} ({roles})")


def t13_slot_wear():
    print("[T13] slot_wear 'bounded' | 'free' (Q2 switch)")
    obj, _t = build_kit_cube()
    maps, f0 = synth_maps(obj, A0)
    ctl_bytes = maps.ctl.tobytes()
    res = {}
    for mode in ("bounded", "free"):
        spec = kit_spec(intensity=1.0, slot_wear=mode)
        m, fl, masks, out = run_layer(obj, A0, spec, maps, f0)
        slot = m.ctl.reshape(-1, 4)[fl.idx, 0] >= 0.999
        b0 = m.base.reshape(-1, 4)[fl.idx, :3].astype(np.float64)[slot]
        b1 = out.base.reshape(-1, 4)[fl.idx, :3].astype(np.float64)[slot]
        rel = np.abs(b1 - b0) / np.maximum(b0, 1e-9)
        ratio = b1 / np.maximum(b0, 1e-9)
        res[mode] = (rel.max(), np.abs(ratio - ratio.mean(axis=1, keepdims=True)).max(),
                     out.ctl is m.ctl, W.weather_report(m, out, fl, spec, masks)["guards"])
    rb, rf = res["bounded"], res["free"]
    check(rb[0] <= 0.10 + 1e-5, f"bounded: full-slot |d|/in max {rb[0]:.4f} <= 0.10")
    check(rf[0] > 0.10 and rf[1] <= 1e-5,
          f"free: full-slot |d|/in max {rf[0]:.4f} > 0.10 and the change is NEUTRAL (channel ratio "
          f"spread {rf[1]:.2e}), so the picker tint still reads")
    check(rf[2] and maps.ctl.tobytes() == ctl_bytes and rf[3]["G4_worst_floor_gap"] >= -1e-6,
          f"free: ControlMask same object and bytes (G5), W9 floor holds (gap {rf[3]['G4_worst_floor_gap']})")
    try:
        W.WeatherSpec.preset("building", "X", slot_wear="loose")
        check(False, "slot_wear 'loose' raises")
    except ValueError:
        check(True, "slot_wear outside {'bounded', 'free'} raises ValueError")


def kit_sources():
    import cs2_bake
    out = {}
    for i, k in enumerate(KEYS):
        alb, smo, met, slot = KEY_INFO[k]
        out[i] = cs2_bake.SourceSet(base=alb, rough=1.0 - smo, metal=met, slot=slot)
    return out


class StubInner:
    """A generator hook stand-in: the no-hook compose branch plus an attribute."""
    density = "x"

    def __call__(self, base, nrm, msk, ctl, ao, mesh, A):
        return C.compose_maps(base, nrm, msk, ctl, None, None, 0.0,
                              {"reserved": [], "gloss_var": 0.0})


def _png_bytes(path):
    with open(path, "rb") as fh:
        return fh.read()


def t11_hook(tmp):
    print("[T11] hook integration through cs2_bake.bake_unique (A 256, AO 8)")
    import cs2_bake
    import cs2_preview
    A = 256
    d0 = os.path.join(tmp, "plain")
    d1 = os.path.join(tmp, "weather")
    # margin_px 4. The default max(16, A // 64) = 16 px at A 256 leaves Blender's packer no room for
    # the cube's 62 one-face islands (loops end up outside [0, 1]).
    obj, table = build_kit_cube("WeatherKitCube", with_uvmap=True)
    cs2_bake.bake_unique(obj, d0, "WeatherKitCube", A, kit_sources(), ao_samples=8,
                         post_compose=None, src_uv="UVMap", margin_px=4)
    obj, table = build_kit_cube("WeatherKitCube", with_uvmap=True)
    spec = kit_spec()
    hook = W.make_weather_hook(StubInner(), spec, mat_keys=KEYS, detail_specs=detail_specs(),
                               keep_fields=True)
    cs2_bake.bake_unique(obj, d1, "WeatherKitCube", A, kit_sources(), ao_samples=8,
                         post_compose=hook, src_uv="UVMap", margin_px=4)
    c0 = _png_bytes(os.path.join(d0, "WeatherKitCube_ControlMask.png"))
    c1 = _png_bytes(os.path.join(d1, "WeatherKitCube_ControlMask.png"))
    check(c0 == c1, "ControlMask PNGs byte-identical with and without the layer")
    b = cs2_preview._load_rgba(os.path.join(d1, "WeatherKitCube_BaseColor.png"))
    check(np.all(np.rint(b[..., 3] * 255) == 255), "BaseColor alpha 255 everywhere")
    b0 = cs2_preview._load_rgba(os.path.join(d0, "WeatherKitCube_BaseColor.png"))
    check(not np.array_equal(b, b0), "BaseColor changed by the layer")
    check(hook.density == "x", "hook.density delegates to inner")
    check(hook.report is not None and "vanilla" in hook.report,
          f"hook.report['vanilla'] exists (ok {hook.report['vanilla']['ok'] if hook.report else None})")
    fl = hook.fields
    merged = dict(W.MAT_ROLES)
    merged.update(dict(MAT_EXTRA))
    part_key = {}
    for o_name, key in _PART_KEYS.items():
        part_key[o_name] = key
    bad = []
    for p, name in enumerate(fl.part_names, start=1):
        exp = W._longest(name, [(pre, pw.role) for pre, pw in OVERRIDES]) or merged[part_key[name]]
        got = W.ROLES[int(fl.part_role[p])]
        if got != exp:
            bad.append((name, exp, got, fl.part_role_source[p]))
    check(not bad, f"roles from mat_keys match the parts' keys ({len(fl.part_names)} parts; mismatches {bad})")
    print("    hook report seconds", hook.report["seconds"], "vanilla ok", hook.report["vanilla"]["ok"])


_PART_KEYS = {"wall_t": "timber_lap", "gable_t0": "timber_lap", "gable_t1": "timber_lap",
              "roof_tS": "slate", "roof_tN": "slate", "deck_t": "deck", "step_t0": "deck",
              "step_t1": "deck", "door_t0_leaf": "trim_white", "door_t0_jamb0": "trim_white",
              "sign_t_face": "label", "bracket_g": "galv", "trim_t": "trim",
              "bar_t": "steel_paint", "steelplate_hi": "steel_paint",
              "steelplate_lo": "steel_paint"}


# --------------------------------------------------------------------------------------
# main
# --------------------------------------------------------------------------------------

def child_main(out_json):
    obj, _t = build_kit_cube()
    spec = kit_spec()
    maps, f0 = synth_maps(obj, A0)
    _m, fl, _ms, out = run_layer(obj, A0, spec, maps, f0)
    with open(out_json, "w") as fh:
        json.dump({"sha1": outputs_sha1(out, fl)}, fh)
    sys.exit(0)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if argv[:1] == ["--child"]:
        child_main(argv[1])
        return
    t0 = time.perf_counter()
    obj, table = build_kit_cube()
    print(f"[fixture] {len(table)} parts, {len(obj.data.polygons)} faces, packed rows "
          f"{obj['kit_used_rows']} of {A0}")
    maps = f0 = None
    tmp = tempfile.mkdtemp(prefix="cs2_weather_test_")
    tests = [("T1", lambda: t1_determinism(obj)), ("T2", lambda: t2_parity(obj))]
    for name, fn in tests:
        try:
            r = fn()
            if name == "T1":
                maps, f0 = r
        except Exception as e:  # noqa: BLE001
            traceback.print_exc()
            FAILS.append(f"{name}: {e!r}")
    if maps is None:
        maps, f0 = synth_maps(obj, A0)
    for name, fn in (("T3", t3_guards), ("T4", t4_edge), ("T5", t5_grime), ("T6", t6_snow),
                     ("T8", t8_gravity), ("T9", t9_lattice), ("T10", t10_budget)):
        try:
            fn(obj, maps, f0)
        except Exception as e:  # noqa: BLE001
            traceback.print_exc()
            FAILS.append(f"{name}: {e!r}")
    for name, fn in (("T7", lambda: t7_density(obj)), ("T11", lambda: t11_hook(tmp)),
                     ("T12", t12_tower_preset), ("T13", t13_slot_wear)):
        try:
            fn()
        except Exception as e:  # noqa: BLE001
            traceback.print_exc()
            FAILS.append(f"{name}: {e!r}")
    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_weather: {len(FAILS)} failure(s) ({dt:.0f} s): {FAILS}")
        sys.exit(1)
    print(f"PASS test_weather: T1-T13 (determinism + child sha1, field parity, guards G1-G8 + "
          f"F6/F7/protect_px, edge wear >= 3 texels, grime gradient, snow, density 40/80 px/m "
          f"incl. scuff + tone, gravity/streaks, lattice, vanilla budget 0.35 + 0.5, bake_unique "
          f"hook, tower preset, slot_wear) ({dt:.0f} s)")
    sys.exit(0)


if __name__ == "__main__":
    main()
