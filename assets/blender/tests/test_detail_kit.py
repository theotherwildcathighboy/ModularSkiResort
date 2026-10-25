"""
test_detail_kit - the detail-pass-1 kit contracts (design/detail-pass-1.md section 6.2, kit
row, with design/detail-pass-1-skeptic.md M1-M10 applied; build report
docs/research/art/assets-2026-10/build/detail-pass-1-kit.md).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_detail_kit.py
        [-- --no-k4]     skip K4 (every recipe's blockout + blockout LODs, ~4 min, + the sign)
        [-- --no-k5]     skip K5 (three 512 px finishes of a demo recipe)
        [-- --no-k9]     skip K9b (the L1 blockout: tables vs the baked umbrellas, ~25 s)

K1  new builders: documented prefixes, matkeys, tri counts (REF, +-10 %), the foot (min z
    >= z0, or <= -0.05 with leg_to), a second call with the same name raises (no .001);
    the new LOD prefixes are on the kit lists; keyword DEFAULTS reproduce the pre-pass
    geometry vertex for vertex (DEFAULT_HASH, measured on the pre-pass kit 2026-10-11).
K2  DisplayPainter: "AB" text reads left to right from outside on the 4 wall sides x island
    turn {0, 90} x mirror {no, yes} (every inked / blank texel checked against the 5x7
    bitmap in the viewer frame); draw_pictogram(square=True) is 1:1 on a 4:1 face.
K3  label_metrics against hand values (the skeptic's V12 numbers and the M4 fixes).
K4  byte-identity: blockout LOD0_prebake / Win and blockout-level LOD1 / LOD2 hashes of every
    registered recipe against K4_TABLE (re-based 2026-10-11 for the gear without bindings, see
    the comment above the table; the pre-pass values are in the build report), each guarded
    by its recipe source sha1 (a recipe its own group has edited since is skipped, printed);
    the Welcome Sign blockout in a child process (mesh hash + band PNG sha1s).
K5  weathering wiring on the demo volumes recipe at --tex 512 --ao-samples 2: weather None
    -> the five PNGs equal the pre-change engine's (K5_REF, measured with the pre-pass kit);
    weather {} -> ControlMask identical, BaseColor changed, the label rect identical,
    manifest weather (vanilla.ok present, intensity 0.40, slot_wear free, snow 0, G5 / G6
    true); --weather-sweep -> rows + highest_clean, shipped maps unchanged by the sweep.
K6  Recipe.sub_objects -> the manifest sub_objects block (keys, values, validation).
K7  Recipe.weights -> the density_w face attribute; obj["w_override"] wins; no weights =
    the kit's dict.
K8  prism_gable(inner=), gable_roof(wall_inner=False), deck(soffit=True): face / area deltas.
K9  detail pass 1b (ninth look, 2026-10-11): (a) every gear item on the L1 / L2 / school
    racks spans the rack's lean, K.rack_reach() 1.33 m (integration F1; L5's short pole pairs are
    reported, not asserted: not this pass's file); (b) REWRITTEN for the vanilla retrofit
    (fifteenth look, 2026-10-11: no house-made umbrellas): the ten L1 table spots each carry a
    vanilla TableSetCommercialRandom01 AND a vanilla ParasolRandom01 beside it (pivot
    PARASOL_OFFSET from the table, local +Z at it: the base game's convention), the porch bench +
    bin are vanilla rows (m_ParentMesh -1, on the lot), no umbstand_ / umbpole_ / umbcanopy_ /
    bench_ / bin_ part is left in the L1 blockout, no lodge triangle inside any prop box (parasol
    = pole +-0.30 to 1.80 + canopy tier = union footprint from 1.80, as recipes/l5), no box
    overlap but a pole in its own table set; bounds / GUIDs re-derived from the dump when present.
    Fine-detail step 3a: run on the SHIPPING L1 (the FineRecipe of recipes/lodge_aframe_fine.py): its
    first 22 rows, + the 6 kit racks boxed, + no modeled rack / porch-lantern part left.
K10 fine-detail step 3a, L1 (`-- --no-k10` skips): the kit rack rows = the removed rack frames vertex
    for vertex (identity; yaw 90 negative), loadouts alternate, the leaning singles kept (FS6), the
    rail-F lanterns kept, the porch wall lights on the column faces at the old lens, the flag mast fine,
    lot bbox = the pre-pass +-5 mm. K4 source keys may join files with '+' (L1 = bldg_recipes.py +
    recipes/lodge_aframe_fine.py).
Exit 1 on the first failure; one PASS line otherwise.
"""

import glob
import hashlib
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile
import time
import traceback
from dataclasses import replace

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import bldg_recipes as R   # noqa: E402
import cs2_asset           # noqa: E402
import cs2_bldg as K       # noqa: E402
import cs2_compose         # noqa: E402
import cs2_uv              # noqa: E402
import lodge               # noqa: E402
from bldg_recipes import Recipe, Budget, Volume, FacadeRule, Extra   # noqa: E402
from cs2_bldg import WallRef, RoofSpec, WinSpec                       # noqa: E402

CHECKS = []


def ok(msg):
    CHECKS.append(msg)
    print(f"ok {msg}")


def clear():
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for it in list(block):
            if it.users == 0:
                block.remove(it)


def tris(parts):
    return sum(len(p.vertices) - 2 for o, _m in parts for p in o.data.polygons)


def zmin(parts):
    return min(min((o.matrix_world @ v.co).z for v in o.data.vertices) for o, _ in parts)


def phash(parts):
    h = hashlib.sha1()
    for o, m in parts:
        h.update(f"{o.name}|{m}|".encode())
        for v in o.data.vertices:
            h.update(("%.5f %.5f %.5f;" % tuple(v.co)).encode())
        for p in o.data.polygons:
            h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    return h.hexdigest()[:12]


def sha1_file(p, n=12):
    return hashlib.sha1(open(p, "rb").read()).hexdigest()[:n]


def sha1_src(src, n=12):
    """K4 source key: one file under assets/blender, or several joined by '+' (sha1 of their bytes in
    that order; fine-detail step 3a: L1 = bldg_recipes.py + recipes/lodge_aframe_fine.py). One file =
    sha1_file of it."""
    h = hashlib.sha1()
    for p in src.split("+"):
        h.update(open(os.path.join(HERE, p), "rb").read())
    return h.hexdigest()[:n]


# ======================================================================================
# K1 builders
# ======================================================================================

P1_SPEC = RoofSpec("gable", -2, 2, -2.725, 2.725, 2.35, 14.0, axis="Y", overhang=0.35,
                   rake_over=0.34, thick=0.10, mat="metal_roof_s1")
P2_SPEC = RoofSpec("gable", -3.7, 2.3, -3.9, 4.2, 3.0, 28.0, axis="X", overhang=0.85,
                   ridge_at=1.3, thick=0.15, mat="slate")
MAZE = (((-5.6, -12.9), (-2.6, -12.9)), ((-2.6, -12.9), (-2.6, -11.3)),
        ((-5.0, -11.3), (-2.6, -11.3)), ((-5.6, -12.1), (-3.2, -12.1)))
# (label, builder, reference tris (measured 2026-10-11, +-10 %), foot z)
NEW = [
    ("post_sign", lambda: K.post_sign("lesson0", 6.9, -15.0, "+X", 0.6, 0.6, 2.6), 30, K.GROUND_LIFT),
    ("maze_fence", lambda: K.maze_fence("carpet", MAZE), 112, K.GROUND_LIFT),
    ("radio_mast", lambda: K.radio_mast("p1", -1.6, 3.25, 0.60, 5.30,
                                        brackets=((1.2, "-Y", 0.525), (2.0, "-Y", 0.525))), 60, 0.60),
    ("toboggan_rack", lambda: K.toboggan_rack("p1", ("-X", -2.0), -0.6, 2.2, (0.55, 1.75)), 56, 0.53),
    ("backboard", lambda: K.backboard("p1", (-2.30, -1.6, 0.02), "-X", lean_deg=85), 12, K.GROUND_LIFT),
    ("hose_reel", lambda: K.hose_reel("hq", WallRef("+Y", 4.2), -1.8, 1.9), 70, 1.5),
    ("drum", lambda: K.drum("hutfuel", (11.0, -4.9, 0.80), "X"), 28, 0.50),
    ("drum_upright", lambda: K.drum("hutfuel2", (11.0, -5.9), z0=0.12), 28, 0.12),
    ("fuel_cradle", lambda: K.fuel_cradle("hutfuel", 11.0, -4.9, "X", 0.50, -0.90), 66, None),
    ("log_stack", lambda: K.log_stack("hq", 2.33, 2.73, -0.6, 2.4, 1.30, 1.85, "+X"), 10, 1.30),
    ("safety_stripe", lambda: K.safety_stripe("s0", (2.0, -1.8), (2.0, -0.8), 0.15), 10, 0.15),
    ("roof_emblem_s0", lambda: K.roof_emblem("p1roof", P1_SPEC, "+X", 1.5, s0=0.227), 2, None),
    ("roof_emblem_centre", lambda: K.roof_emblem("hqroof", P2_SPEC, "+Y", 2.2, centre=(-0.7, 2.97)), 2, None),
    ("flag_face", lambda: K.flag_mast("school", 6.8, -9.0, h=7.5, flag=(1.4, 0.9), along="Y",
                                      face=True), 24, K.GROUND_LIFT),
    ("flag_z0", lambda: K.flag_mast("hut", 5.75, -8.25, z0=0.43, h=7.5, flag=(1.4, 0.9)), 32, 0.43),
    ("bench_deck", lambda: K.bench("hutS0", -3.0, -6.02, "-Y", length=3.4, z0=0.43), 48, 0.43),
    ("bench_noback", lambda: K.bench("p1", 0.0, -3.05, "-Y", length=1.6, back=False), 36, K.GROUND_LIFT),
    ("rack_z0_gear", lambda: K.ski_rack_frame("hutN", 10.13, 2.60, slots=7, along="X", z0=0.43,
                                              skis=((0, 1, "ski"), (2, -1, "board")), gear=True), 240, 0.43),
    ("rack_leg_to", lambda: K.ski_rack_frame("hutL", 20.0, 2.60, slots=4, along="X", leg_to=-0.6), 176, None),
]
PREFIXES = ("sign_", "maze_", "mast_", "lamp_", "tobrack_", "gear_", "hosereel_", "drum_",
            "wood_", "stripe_", "emblem_", "flag_", "flagpanel_", "bench_", "skirack_")
# pre-pass kit, the same calls as tests/test_bldg_kit + recipe calls, OLD arguments only
DEFAULT_HASH = {
    "bench": "3d8cad9281f7", "flag_mast": "392db5a1fc60", "flag_mast_def": "1be59ee86cf4",
    # the detail-pass-1 gear WITH bindings (keyword bindings=True, kept for this test only); the
    # default is now bindingless (ninth look): its hash is "ski_rack_frame_gear_nb"
    "ski_rack_frame_gear": "af8aab4247c1", "ski_rack_frame_gear_nb": "00efa56b1692",
    "ski_rack_frame_slab": "73a6ae01602c",
    "ski_rack_frame_bare": "ca023b5ec3db", "ski_rack_panel": "fa9b6c10bf0a",
    "ski_rack_slots": "5ef710c0aed4", "prism_gable": "0d327ec45a49",
    "prism_gable_base": "2b8886583913", "gable_roof": "9e92b82595d7",
    "gable_roof_walls": "a30e2a79098f", "deck_col": "027334ba2789", "deck_none": "85491b54cd73",
    "deck_solid": "8dcc7a8a2912",
}
_SPEC_T = RoofSpec("gable", -5, 5, -8, 8, 6.0, 30.0, axis="Y", overhang=1.0)
_GEAR_DEFAULT = (
    (0, 1, "ski", ("trim_white", "ski_blue"), 1.72), (2, 1, "board", ("ski_black", "accent"), 1.54),
    (5, 1, "poles", "ski_black", 1.25), (1, -1, "ski", "accent", 1.70))
DEFAULT_CALLS = [
    ("bench", lambda: K.bench("porch", -13.0, 4.35, "-X", length=1.5)),
    ("flag_mast", lambda: K.flag_mast("deck", 15.5, -21.75, h=9.0, flag=(1.5, 0.9))),
    ("flag_mast_def", lambda: K.flag_mast("f", 110, 0)),
    ("ski_rack_frame_gear", lambda: K.ski_rack_frame("g", 16.4, -15.0, slots=8, along="Y", gear=True,
                                                    bindings=True, skis=_GEAR_DEFAULT)),
    ("ski_rack_frame_gear_nb", lambda: K.ski_rack_frame("gn", 16.4, -15.0, slots=8, along="Y", gear=True,
                                                       skis=_GEAR_DEFAULT)),
    ("ski_rack_frame_slab", lambda: K.ski_rack_frame("s", 0, 30, slots=8, skis=((0, 1, "ski"), (3, -1, "board")))),
    ("ski_rack_frame_bare", lambda: K.ski_rack_frame("b", 0, 40, slots=6, along="X")),
    ("ski_rack_panel", lambda: K.ski_rack("sr", 114, 0, facing="+X")),
    ("ski_rack_slots", lambda: K.ski_rack("sl", 50, 0, slots=8, skis=((0, 1, "ski"),), gear=False)),
    ("prism_gable", lambda: K.prism_gable("g", "Y", -8, -7.7, -5, 5, 6, 9)),
    ("prism_gable_base", lambda: K.prism_gable("hqfront", "X", 2.0, 2.3, -3.9, 4.2, 3.0, 6.0,
                                               ridge_at=1.3, eave_z1=2.5, base_z=1.3)),
    ("gable_roof", lambda: K.gable_roof("gr", _SPEC_T, tails=1.0, snow_guard=1, gutters=True)),
    ("gable_roof_walls", lambda: K.gable_roof("gw", _SPEC_T, walls=True, wall_mat="timber_vboard", ext=0.12)),
    ("deck_col", lambda: K.deck("dk", 20, 26, 10, 16, 3.0, support="col")),
    ("deck_none", lambda: K.deck("hq", 2.3, 4.1, -3.9, 5.0, 1.3, support="none", fascia=0.0,
                                 board_dir="Y", thick=0.05)),
    ("deck_solid", lambda: K.deck("main", 2.8, 14.2, -21.2, 21.2, 4.66, support="solid", fascia=0.0)),
]


def k1_builders():
    clear()
    for nm, fn, ref, foot in NEW:
        parts = fn()
        names = [o.name for o, _ in parts]
        assert len(names) == len(set(names)), f"{nm}: duplicate names {names}"
        for o, m in parts:
            assert o.name.startswith(PREFIXES), f"{nm}: {o.name} has no documented prefix"
            assert m in K.BLDG_MATERIALS, f"{nm}: {o.name} matkey {m}"
            assert all(p.area > 1e-9 for p in o.data.polygons), f"{nm}: zero-area face in {o.name}"
        n = tris(parts)
        assert abs(n - ref) <= max(1, 0.10 * ref), f"{nm}: {n} tris vs reference {ref}"
        z = zmin(parts)
        if foot is not None:
            assert z >= foot - 1e-4, f"{nm}: min z {z:.4f} below its foot {foot}"
        try:
            fn()
            raise AssertionError(f"{nm}: a second build with the same names was accepted")
        except ValueError:
            pass
    rack = [p for p in bpy.data.objects if p.name == "skirack_hutL"][0]
    assert min(v.co.z for v in rack.data.vertices) <= -0.05 + 1e-6, "leg_to rack does not reach -0.05"
    assert all(not (-0.01 <= v.co.z <= 0.01) for v in rack.data.vertices), "leg_to rack vertex in the band"
    cr = bpy.data.objects["drum_hutfuellegs"]
    assert all(not (-0.01 <= v.co.z <= 0.01) for v in cr.data.vertices), "cradle vertex in the band"
    # skeptic M9: facing = the OUTWARD normal of the wall leaned on; the top leans INTO it
    # (backboard "p1" on the -X back wall at x -2.0: its top is at larger x than its foot)
    bb = bpy.data.objects["gear_p1board"]
    xs = [(v.co.z, v.co.x) for v in bb.data.vertices]
    x_top = max(xs)[1]
    x_foot = min(xs)[1]
    assert x_top > x_foot + 0.05 and max(x for _z, x in xs) <= -2.0 + 1e-3, \
        f"backboard leans the wrong way / through the wall (foot x {x_foot:.3f}, top x {x_top:.3f})"
    ok(f"K1 {len(NEW)} new builders / keywords: prefixes, matkeys, tris within 10 %, feet, "
       f"duplicate names refused, plinth-mode feet clear of the band")
    # the foot rule refuses the band
    for bad in (lambda: K.bench("bad", 0, 0, "+X", z0=0.0),
                lambda: K.flag_mast("bad", 0, 0, z0=0.005),
                lambda: K.ski_rack_frame("bad", 0, 0, leg_to=-0.02),
                lambda: K.prism_gable("bad", "Y", 0, 0.3, -1, 1, 2, 3, inner=False)):
        try:
            bad()
            raise AssertionError("a foot in the ground band / inner=False was accepted")
        except ValueError:
            pass
    # LOD lists: new dressing LOD1-deleted, masts to LOD1, sign / emblem faces to LOD1
    for p in ("maze_c", "tobrack_c", "gear_cboard", "hosereel_c", "drum_c", "wood_c", "stripe_c",
              "lamp_cbeacon"):
        assert p.startswith(K.LOD1_DELETE), f"{p} not LOD1-deleted"
    for p in ("mast_c", "mast_cbr", "sign_c_face", "emblem_c_face", "bench_c"):
        assert not p.startswith(K.LOD1_DELETE), f"{p} must survive LOD1 (bench_: recipe lod1_extra, M1)"
    assert "mast_c".startswith(K.LOD2_DELETE) and "maze_c".startswith(K.LOD2_DELETE)
    assert not any(p.startswith("festoon_") for p in K.LOD1_DELETE), "festoon_ (M1: string_lights)"
    for k in ("rubber", "safety_yellow", "fire_red", "lamp_red"):
        assert k in K.BLDG_MATERIALS and k in K.WEATHER_ROLES_EXTRA, k
    assert K.BLDG_MATERIALS["lamp_red"].emissive_srgb is not None
    ok("K1 LOD lists (dressing LOD1-deleted, masts / boards / faces kept to LOD1, no bench_ / "
       "festoon_ globally), 4 new materials with weathering roles")
    clear()
    bad = []
    for nm, fn in DEFAULT_CALLS:
        h = phash(fn())
        if h != DEFAULT_HASH[nm]:
            bad.append((nm, h, DEFAULT_HASH[nm]))
    assert not bad, f"keyword defaults moved geometry: {bad}"
    ok(f"K1 keyword defaults vertex-identical to the pre-pass kit ({len(DEFAULT_CALLS)} calls: "
       f"bench, flag_mast, ski_rack_frame, ski_rack, prism_gable, gable_roof, deck)")


# ======================================================================================
# K2 DisplayPainter orientation
# ======================================================================================

FACE = "sign_t_face"


def _label_mesh(side, turn, mirror, w=2.0, h=0.5, A=1024):
    """One quad `sign_t_face` on a wall `side`, its UVUnique island at turn / mirror; returns
    (obj, table, viewer frame (right, up), A)."""
    clear()
    plane = {"+X": 3.0, "-X": -3.0, "+Y": 2.0, "-Y": -2.0}[side]
    parts = K.pane_quad(FACE, WallRef(side, plane), -w / 2 + 0.3, w / 2 + 0.3, 1.0, 1.0 + h,
                        d=0.0, mat="label")
    o = parts[0][0]
    me = o.data
    table = [FACE]
    me[cs2_asset.PART_TABLE_PROP] = json.dumps(table)
    at = me.attributes.new("part", "INT", "FACE")
    at.data.foreach_set("value", [1] * len(me.polygons))
    n = Vector(me.polygons[0].normal)
    up = Vector((0.0, 0.0, 1.0))
    right = (-n).cross(up)
    lay = me.uv_layers.new(name=cs2_uv.UNIQUE_LAYER)
    k = 0.40 / max(w, h)                           # UV units per meter
    cen = sum((me.vertices[i].co for i in me.polygons[0].vertices), Vector()) / 4.0
    for li in me.polygons[0].loop_indices:
        P = me.vertices[me.loops[li].vertex_index].co - cen
        s, t = P.dot(right), P.dot(up)
        if mirror:
            s = -s
        if turn == 90:
            s, t = -t, s
        lay.data[li].uv = (0.5 + k * s, 0.5 + k * t)
    return o, table, right, up, A


def k2_display():
    font = K._font5x7()
    n_cases = 0
    for side in ("+X", "-X", "+Y", "-Y"):
        for turn in (0, 90):
            for mirror in (False, True):
                o, table, right, up, A = _label_mesh(side, turn, mirror)
                me = o.data
                rect = cs2_compose.uv_bbox_px(me, table, FACE, A)
                r0, r1, c0, c1 = rect
                base = np.zeros((r1 - r0, c1 - c0, 4), np.float32)
                ctl = np.zeros_like(base)
                p = K.DisplayPainter(FACE, K.draw_text(("AB",), fg=1.0))
                p(rect, base, ctl)
                rec = p.last["draws"][0]
                # independent texel -> world -> viewer map (the test's own frame)
                poly = me.polygons[0]
                Q = np.array([[me.uv_layers[cs2_uv.UNIQUE_LAYER].data[li].uv[0],
                               me.uv_layers[cs2_uv.UNIQUE_LAYER].data[li].uv[1], 1.0]
                              for li in poly.loop_indices])
                Pw = np.array([list(me.vertices[me.loops[li].vertex_index].co)
                               for li in poly.loop_indices])
                M, *_ = np.linalg.lstsq(Q, Pw, rcond=None)
                R_, U_ = np.array(list(right)), np.array(list(up))
                C_ = Pw.mean(axis=0)
                s_all, t_all = (Pw - C_) @ R_, (Pw - C_) @ U_
                W, H = s_all.max() - s_all.min(), t_all.max() - t_all.min()
                rr, cc = np.mgrid[r0:r1, c0:c1]
                D = np.stack([(cc + 0.5) / A, (rr + 0.5) / A, np.ones(rr.shape)], -1) @ M - C_
                x = D @ R_ - s_all.min()
                y = t_all.max() - D @ U_
                cell = rec["cell_m"]
                lw = 11 * cell
                gx = (x - 0.5 * (W - lw)) / cell
                gy = (y - 0.5 * (H - 7 * cell)) / cell
                fx, fy = gx - np.floor(gx), gy - np.floor(gy)
                safe = (fx > 0.2) & (fx < 0.8) & (fy > 0.2) & (fy < 0.8)
                ix, iy = np.floor(gx).astype(int), np.floor(gy).astype(int)
                inside = (ix >= 0) & (ix < 11) & (iy >= 0) & (iy < 7) & (ix % 6 < 5)
                exp = np.zeros(ix.shape, bool)
                bmA = np.array([[b == "1" for b in row] for row in font["A"]])
                bmB = np.array([[b == "1" for b in row] for row in font["B"]])
                bm = np.stack([bmA, bmB])
                sel = inside & safe
                exp[sel] = bm[ix[sel] // 6, iy[sel], ix[sel] % 6]
                got = base[..., 0]
                chk = safe & ((ix >= -1) & (ix <= 11) & (iy >= -1) & (iy <= 7))
                wrong = chk & (((got > 0.9) & ~exp) | ((got < 0.1) & exp))
                n_ink = int((chk & exp).sum())
                assert n_ink > 200, f"{side}/{turn}/{mirror}: only {n_ink} inked texels checked"
                assert int(wrong.sum()) == 0, (f"{side} turn {turn} mirror {mirror}: "
                                               f"{int(wrong.sum())} texels disagree with 'AB' read "
                                               f"left to right from outside")
                n_cases += 1
    ok(f"K2 'AB' reads left to right from outside: {n_cases} cases (4 sides x turn 0/90 x "
       f"mirror), every safe texel equals the 5x7 bitmap")
    # pictogram square on a 4:1 face
    o, table, right, up, A = _label_mesh("+X", 0, False, w=2.4, h=0.6)
    rect = cs2_compose.uv_bbox_px(o.data, table, FACE, A)
    r0, r1, c0, c1 = rect
    base = np.zeros((r1 - r0, c1 - c0, 4), np.float32)
    p = K.DisplayPainter(FACE, K.draw_pictogram("restaurant", square=True, fg=1.0))
    p(rect, base, np.zeros_like(base))
    rec = p.last["draws"][0]
    assert abs(rec["box_m"][0] - rec["box_m"][1]) < 1e-6 and abs(rec["box_m"][0] - 0.6) < 1e-3, rec
    ink = base[..., 0] > 0.5
    cols = np.flatnonzero(ink.any(axis=0))
    ppm = p.last["ppm"]
    span_m = (cols.max() - cols.min() + 1) / ppm
    assert span_m <= 0.6 + 2.0 / ppm, f"pictogram ink {span_m:.3f} m wide on a 0.6 m square"
    ok(f"K2 draw_pictogram(square=True) on a 2.4 x 0.6 face: box {rec['box_m']} (1:1), ink "
       f"{span_m:.3f} m wide <= 0.6 m (no 4:1 stretch)")
    # every draw runs; symmetric cross; .last records
    o, table, right, up, A = _label_mesh("-Y", 90, True, w=1.5, h=1.5, A=512)
    rect = cs2_compose.uv_bbox_px(o.data, table, FACE, A)
    r0, r1, c0, c1 = rect
    for nm, kw in (("cross_square", {}), ("bibs", {"seed": 7}), ("notices", {"seed": 3}),
                   ("logs", {"seed": 5}), ("glyph", {"prims": "cand_d"}),
                   ("shapes", {"items": (("circle", 0.25, 0.5, 0.3, (0.1, 0.6, 0.2)),
                                         ("square", 0.5, 0.5, 0.3, (0.1, 0.3, 0.8)),
                                         ("diamond", 0.75, 0.5, 0.3, (0.05, 0.05, 0.05)),
                                         ("glyph:cand_d", 0.5, 0.2, 0.3, 0.92))})):
        base = np.zeros((r1 - r0, c1 - c0, 4), np.float32)
        p = K.make_painter("display", {"draws": ((nm, kw),), "symmetric": nm == "cross_square"},
                           face=FACE)
        p(rect, base, np.zeros_like(base))
        assert p.last["draws"][0]["kind"] == nm and base[..., :3].max() > 0.05, nm
    ok("K2 make_painter('display'): cross_square, bibs, notices, logs, glyph, shapes paint and record")


# ======================================================================================
# K3 label_metrics
# ======================================================================================

def k3_metrics():
    def m(*a, **k):
        return K.label_metrics(*a, **k)
    cases = [  # (w, h, lines, ppm) -> (cell, cap, stroke_px, tier): skeptic V12 + M4 fixes
        ((4.2, 0.9, "SKI SCHOOL", 64.0), (0.85 * 4.2 / 59, 7 * 0.85 * 4.2 / 59, 0.85 * 4.2 / 59 * 64, "T2")),
        ((3.4, 0.6, "SKI PATROL", 88.8), (0.85 * 3.4 / 59, 7 * 0.85 * 3.4 / 59, 0.85 * 3.4 / 59 * 88.8, "T2")),
        ((4.2, 1.4, ("SKI PATROL", "FIRST AID"), 59.0), (0.85 * 4.2 / 59, 7 * 0.85 * 4.2 / 59, 0.85 * 4.2 / 59 * 59, "T2")),
        ((5.0, 0.8, "RESTAURANT", 57.0), (0.85 * 5.0 / 59, 7 * 0.85 * 5.0 / 59, 0.85 * 5.0 / 59 * 57, "T1")),
        ((4.2, 0.9, "SKI SCHOOL", 74.7), (0.85 * 4.2 / 59, 7 * 0.85 * 4.2 / 59, 0.85 * 4.2 / 59 * 74.7, "T1")),
        ((2.6, 1.3, ("SKI", "PATROL"), 88.8), (0.85 * 1.3 / 18, 7 * 0.85 * 1.3 / 18, 0.85 * 1.3 / 18 * 88.8, "T1")),
        ((4.2, 1.4, ("SKI PATROL", "FIRST AID"), 73.8), (0.85 * 4.2 / 59, 7 * 0.85 * 4.2 / 59, 0.85 * 4.2 / 59 * 73.8, "T1")),
    ]
    for args, (cell, cap, sp, tier) in cases:
        r = m(*args)
        assert abs(r["cell_m"] - cell) < 1e-9 and abs(r["cap_m"] - cap) < 1e-9 and \
            abs(r["stroke_px"] - sp) < 1e-9 and r["tier"] == tier, (args, r)
    r = m(3.4, 0.6, "SKI PATROL", 88.8, fill=1.0)
    assert abs(r["cap_m"] - 7 * 3.4 / 59) < 1e-9 and r["rows"] == 9 and r["cols"] == 59
    assert m(4.2, 1.4, ("A", "B"), 50)["rows"] == 18
    ok(f"K3 label_metrics: {len(cases) + 2} hand cases (SKI SCHOOL 4.2 x 0.9 @ 64 px/m stroke "
       f"3.87 tx = T2 at fill 0.85; at w 1.75 (74.7) T1; RESTAURANT T1; P2 2-line T1 at 73.8)")


# ======================================================================================
# K4 byte identity (blockout + blockout-level LODs) against this pass's table
# ======================================================================================

# key: (source file under assets/blender, its sha1[:12], LOD0_prebake, Win, LOD1b, LOD2b).
# RE-BASED 2026-10-11 (detail pass 1b, ninth look: no bindings on skis / boards, pole
# pairs that reach the rail, the P2 name board keeps its LOD1 frame): every row was measured
# on the CURRENT recipe sources (closes integration F2: L2 / school / P1 / P2 / L5 had been
# skipped on their source sha1 since their detail passes), so a recipe edited after this
# table is skipped (printed), not failed. Before the gear change (the same sources, measured
# 2026-10-11 19:22 with the kit still carrying the bindings):
#   L1 c7d90868f331 / 49730cc8149f / 405d21f68fc5 / fa020fae48f8   (the kit-pass table, equal)
#   L2 24cc76a1d5f4 / c240f5239068 / 9bd5e036ecd0 / 70c5833b8a91
#   L5 40b5ac4046cb / 927ee60aeaae / 362d8fdbeb81 / 93c2f6c5039f
#   P1 fa5aaa8aef26 / 463b9012de23 / 3b438484f770 / 7dbfc53cf490   (UNCHANGED below)
#   P2 0e4f28ce5ebd / 38666256c858 / 32fb41179a85 / daa31fe5413a   (only LOD1b moved: the frame)
#   school 1795ca212003 / 4d18334a86c2 / 1a50b6ae7e31 / 2fb129b833b9
#   L3 / L4 / sign: equal to the rows below before and after (no gear, no recipe edit).
# What moved and why: L1, L2, school, L5 = the gear without bindings (L1 / L2 / school also the
# longer pole pairs; L1's blockout LOD0 10,591 tris from 12,111), their LOD1b / LOD2b shift with
# the part ids; P2 = LOD1b only (beam_strutL / R off LOD1, sign_hqname_frame kept). `Win` (the
# room cells) is equal for every recipe.
# RE-BASED AGAIN 2026-10-11 for L1 and L3 only (vanilla retrofit, fifteenth look: "are we
# gonna use our house-made umbrellas?" -> no), measured on the current sources; before (the rows
# of the 1b re-base, the 19:45 L1 / 10:20 L3 exports):
#   L1 520cea17ca2e: db9e6e963b39 / 49730cc8149f / bf6c6dddc948 / fa020fae48f8
#   L3 5150d42da15b: e9768df6c4e9 / adca88d938fd / 9f7ec8bc8400 / 024b3c4ae79a
# What moved and why: L1 = the 10 modeled umbrellas (30 parts), the porch bench (2) and bin (1)
# left for vanilla sub-objects (blockout LOD0 10,591 -> 9,913); L3 = the stair-foot lamp post
# (col_lampTS + lamp_terrstair + arm) left for a vanilla GardenLight (6,976 -> 6,914). `Win` moved
# on both through the `part` attribute only (the pane parts' ids shift down when earlier parts
# leave; mesh_hash hashes it): pane geometry, cells and ids unchanged (build/lodges-basic.md,
# "Vanilla retrofit (fifteenth look)").
# RE-BASED AGAIN 2026-10-12 for P1 and P2 only (patrol lean fix, fifteenth look: "toboggans lean
# against nothing"; recipes/patrol.py 20101f381606 -> 7d10428fc5f2 + recipes/_lean.py), measured
# by the checker on the current sources in two separate processes (equal); before:
#   P1 20101f381606: fa5aaa8aef26 / 463b9012de23 / 3b438484f770 / 7dbfc53cf490
#   P2 20101f381606: 0e4f28ce5ebd / 38666256c858 / 1d36fc868682 / daa31fe5413a
# What moved and why: LOD0_prebake only on P1 (rack upper bar 1.75 -> 1.86, akja 2.0 -> 1.9 m,
# poles on the wall); on P2 also LOD1b / LOD2b (the moved toboggans, rack bar 2.40 -> 3.18, 2 more
# LOD0 tris; the blockout-level LOD plan targets follow the LOD0 count). `Win` equal. Every
# moved part is on a LOD1 delete list, so P1's LOD1b / LOD2b are unchanged.
# RE-BASED AGAIN 2026-10-12 for L5 only (revision 3, twelfth look: plaza on the pool side, valet
# forecourt, deck stair to the right, every tree / bench / bin / table / chair / parasol / lounger /
# lamp post / planter / bollard a vanilla sub-object; build/lodges-swanky.md "Revision 3"),
# measured on recipes/l5_swanky_bottom.py 4c5a7eb4d3ed in two separate processes (equal); before
# (the 1b re-base of revision 2):
#   L5 a8ac79fa142f: 6f15489ad0a7 / 927ee60aeaae / 5226eac33be0 / a45b9a3f2b5c
# What moved and why: everything - the road plaza and its modeled dressing left (vanilla rows now),
# the slope plaza, the valet drive / walks / island and the stair on the deck's +Y edge came, the
# build moved X_SHIFT -4 -> +4 (LOD0 24,383; the revision-2 export shipped 28,285); `Win` moved through the shift and
# the `part` ids (cells and ids unchanged: 21 rooms).
K4_TABLE = {
    # fine-detail step 3a (2026-10-12): L1 opts in through recipes/lodge_aframe_fine.py (fine_detail.wrap of
    # bldg_recipes.L1, registered by the new load_recipes OVERRIDES hook: 6 racks -> kit rows, porch
    # lanterns -> WallLightShortRange, fine flag mast; leaning singles kept); the key now joins both
    # sources; ONLY this row re-based for L1 (was bldg_recipes.py 679655614353 / b09192f46d51 / b907916fb3c6 /
    # 35a6e7745137 / 66cf350ccc79 = what `lodge.py --lodge L1 --no-fine --blockout` still reproduces);
    # `Win` moved through the `part` ids only (pane geometry and cells unchanged)
    "L1": ("bldg_recipes.py+recipes/lodge_aframe_fine.py", "3916af5cd5a4", "9333f47420c4", "747d3542d1a0",
           "4c4ce02d3d12", "281eb36d2b8d"),
    # fine-detail step 3a (2026-10-12): L2 opts in (FineRecipe: kit racks / benches / barrel / entry lamps
    # as rows, fine flag + fuel drum, sign set SkiSignSetHutB; build/fine-detail-step3a-buildings.md);
    # ONLY this row re-based for L2 (was aec16fdc1359 / 93c9b746b2fc / c240f5239068 / 2f413682db4b /
    # 70c5833b8a91 = what `lodge.py --lodge L2 --no-fine --blockout` still reproduces); `Win` moved
    # through the `part` ids only (pane geometry and cells unchanged)
    # NITPICK C (2026-10-13, build/nitpick-c-lodges.md; twenty-fifth look: "the grill (?) on the left side of
    # the entrance may end up in mid-air"): the fine hut's fuel station (cradle drum + legs + pump, crib, crib drum) left
    # for two ground-following BarrelRandom01 rows (blockout LOD0 4,435 -> 4,227); RE-BASED for the five re-exported
    # hosts only (L2, L3, L4, L5, school), measured on the edited sources (scratchpad ncl/scripts/k4_probe.py). Before:
    #   L2 6e6577c37747: 9feb866f1ed0 / a18af35b7148 / e934cdedfc97 / 293415625a4c (the step-3a row; its source had
    #   already moved to 82a480203b82 with the kit color variants, so the row was being SKIPPED, geometry unchanged)
    # `lodge.py --lodge L2 --no-fine --blockout` still reproduces aec16fdc1359's 93c9b746b2fc / c240f5239068 /
    # 2f413682db4b / 70c5833b8a91. `Win` unchanged (the fuel station has no pane).
    "L2": ("recipes/lodge_hut.py", "fdf6eb5f5272", "00d64047d71a", "a18af35b7148", "957caf54482c", "048ca311c2aa"),
    # fine-detail step 3a (2026-10-12): L3 opts in (FineRecipe: 8 wall lanterns -> WallLightShortRange, the
    # gazebo lantern -> RoofLight, 3 kit racks, fine flag masts; no sign set); ONLY this row re-based for L3
    # (was 600882ce1bfb / bf7f67511d3d / eb05d8fa5cfb / ab6fee160814 / eb2855857a0f = what
    # `lodge.py --lodge L3 --no-fine --blockout` still reproduces); `Win` moved through the `part` ids only
    # nitpick C: the three pictogram boards are blank boards (labels only: the blockout and its LODs are unchanged);
    # only the source key moved (cf5c8ce290e3 -> the kit color variants' b2a76689ae38, skipped since -> this edit)
    "L3": ("recipes/lodge_chalet.py", "e254f8eef22c", "6ef1c2370beb", "1bd6d50e4213", "291364c2d737", "ed36571c1c3f"),
    # L4 revision 1 / L5 revision 4 (2026-10-12, eighteenth look; build/lodges-swanky.md "Revision 1 (vanilla
    # pass, cantilevered deck, terrace door, gutter)" / "Revision 4 (gable infill, pool-side doors)"): ONLY
    # these two rows re-based, measured in two separate processes (equal). Before:
    #   L4 8c72e376dfec: 2566625f42aa / 3c18b6ed0f19 / f287996cc220 / e7cc614fa9b6
    #   L5 4c5a7eb4d3ed: 7800f44149ad / 1c1da10f6160 / aec0eb5356de / 593e4a487f90
    # What moved: L4 = vanilla rows for its modeled dressing, the 5.3 m deck cantilever, the dining
    # door, the porch under the eave (blockout LOD0 8,397 -> 8,056); L5 = the porte-cochere gable
    # infill, three terrace doors in recessed openings, the re-centered / new podium flights, six LOD2
    # deletes (LOD0 24,383 -> 24,937); `Win` moved with the door lites / sidelights (cells unchanged:
    # L4 23 rooms, L5 21)
    # fine-detail step 3e (2026-10-12, twenty-third look; build/fine-detail-step3e-swanky.md): L4 and L5 opt in
    # (FineRecipe). L5: 4 plaza racks -> kit rows, 4 road-face lanterns -> WallLightShortRange rows, the porte-cochere
    # name board (face in the sign set SkiSignSetGrandB), 12-seg finial / flue pots, round lantern rods (blockout
    # LOD0 24,937 -> 23,019); L4: 12-seg flue pots only (8,056 -> 8,104), its key now joins the L5 module it
    # imports (shared helpers). ONLY these two rows re-based, measured in separate processes; the old rows are what
    # `lodge.py --lodge L4 | L5 --no-fine --blockout` still reproduces (measured again 2026-10-12 on the edited
    # sources, equal): L4 e73de356d756 d9331fc0488e / 694abbbc3453 / 1233dd0af147 / b2e505abb458;
    # L5 50d890de1deb cd5339492d8f / 2af75ce712a9 / 56181312403c / 55f5c1c398c9. `Win` moved on L5 through the
    # `part` ids only (cells unchanged: 21 rooms); L4's `Win` is equal.
    # nitpick C: L4's forecourt board is blank (labels only: hashes unchanged); L5 not edited (its GRAND LODGE
    # lettering lives on the sheet). Only the source keys moved (e4f246edcee6 / f623d6e6e2e5 were stale since the
    # kit color variants edited l5_swanky_bottom.py: skipped; the hashes measured equal on the current sources)
    "L4": ("recipes/l4_swanky_top.py+recipes/l5_swanky_bottom.py", "7d462e7c1110", "e11882c5b498", "694abbbc3453",
           "a6122de761b8", "f90147e54090"),
    "L5": ("recipes/l5_swanky_bottom.py", "2ae6a82b8990", "f9526ae3a44a", "6249bf89e4dd", "963789b32ff2", "cbfb9dbcdcbd"),
    # fine-detail step 3d (2026-10-12, build/fine-detail-step3d-patrol-sign.md): P1 / P2 opt in (FineRecipe: sign sets
    # SkiSignSetPatrolHutB / HQB on the shared sheet, kit lean rows for every akja / spine board on the SAME support +
    # P2's hose reel, door lanterns -> WallLightShortRange, 12-seg masts, 8-seg logs, 6-sided poles, planked bench, round
    # rack bars); ONLY these two rows re-based, measured in two separate processes (equal). Before (the lean fix):
    #   P1 7d10428fc5f2: b0d4f1b4dfa7 / 463b9012de23 / 3b438484f770 / 7dbfc53cf490
    #   P2 7d10428fc5f2: 11cb23e4083a / 38666256c858 / d77c208b37aa / 3158eb35d38f
    # = what `lodge.py --patrol p1|p2 --no-fine --blockout` still reproduces (test_detail_patrol D13). `Win` unchanged.
    "P1": ("recipes/patrol.py", "198d8515c93b", "4c7f12bb1115", "463b9012de23", "bbceaa4d60ba", "dc189c9a09d0"),
    "P2": ("recipes/patrol.py", "198d8515c93b", "d63a459c221a", "38666256c858", "f64fd6e641e2", "0eb461889d37"),
    # fine-detail step 2 (2026-10-12): the school is the fine-detail reference host (FineRecipe,
    # fine_detail + sign set; build/fine-detail-step2-engine.md): ONLY this row was re-based
    # (was a409931ac715 / be2ca903f8ba / 4d18334a86c2 / 4d7654e3fef0 / f9adccf0ab51)
    # school REVISION 2 (2026-10-12, twentieth look; build/school-rev2.md): no door numbers, the red
    # markings, the 13.5 deg monopitch with the braced back overhang, lot 4 x 5 (building at +12 m),
    # both carpets in the back at 12 % (bubble gallery on the right), stacked back windows: ONLY this
    # row re-based, measured in two separate processes (equal); before (step 2):
    #   ae5499d72b09: 49b57a4338c1 / 4d18334a86c2 / f55dea19243b / 50740c6713f5
    # What moved: everything (the shift, the roof, the yard; blockout LOD0 3,497 -> 3,841); `Win` moved
    # with the taller back panes (2 x 2 lights) and the shift (cells unchanged: 4 rooms)
    # nitpick C: the school's SKI SCHOOL / STAFF ONLY / NO SLEDDING lettering lives on the sheet (cs2_signset): the
    # recipe is not edited, the hashes measured equal; only the stale source key (72187a210775, skipped since the kit
    # color variants) re-based
    "school": ("recipes/school.py", "15fc26957408", "2ea888e2824f", "48b4eeca676d", "662c3f45b7e4", "aafa64874c54"),
}
# fine-detail step 3d (2026-10-12): the sign's default build is the fine one (the SKI AREA header in the sign set
# SkiSignSetGateB, the band keeps the emblem only; lanterns -> WallLightShortRange rows; 12-seg masts): ONLY the sign
# row re-based, measured in two separate processes (equal). Before (= `welcome_sign.py -- --no-fine --blockout`, checked
# below as SIGN_LEGACY): 3d778a71a9ab, band 88bf4fef20d9 / e88b1fa9f78a / 828ae8976935 / cf68b6b6d391
SIGN = ("welcome_sign.py", None, "5a22327dc22a")
SIGN_BAND = {"BaseColor": "e6748ab2381e", "Normal": "c8069272138f", "MaskMap": "d8b521eb9f3b",
             "ControlMask": "b0f9758d6475"}
SIGN_LEGACY = ("3d778a71a9ab", {"BaseColor": "88bf4fef20d9", "Normal": "e88b1fa9f78a", "MaskMap": "828ae8976935",
                                "ControlMask": "cf68b6b6d391"})


def _blockout_lods(recipe, tmp):
    args = lodge.parse_args(["--recipe", recipe.key, "--blockout",
                             "--out", os.path.join(tmp, recipe.key, recipe.letter)])
    res = lodge.run(recipe, args)
    obj = bpy.data.objects[res["name"]]
    table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    (t1, t2), _plan = lodge.plan_lod_targets(obj, table, recipe)
    d1, d2 = lodge.delete_lists(recipe, table)
    lods, _infos = K.lod_chain(obj, res["name"], (t1, t2), (d1, d2), recipe.ground_prefixes)
    h = dict(res["hashes"])
    for lvl, lod in enumerate(lods, start=1):
        h[f"LOD{lvl}b"] = lodge.mesh_hash(lod)
    return h


def k4_identity():
    recipes = R.load_recipes()
    tmp = tempfile.mkdtemp(prefix="detail_kit_k4_")
    checked, skipped = [], []
    try:
        for key in sorted(recipes):
            if key not in K4_TABLE:
                skipped.append(f"{key} (not in the table)")
                continue
            src, sha, h0, hw, h1, h2 = K4_TABLE[key]
            cur = sha1_src(src)
            if cur != sha:
                skipped.append(f"{key} ({src} edited since: {cur})")
                continue
            h = _blockout_lods(recipes[key], tmp)
            want = {"LOD0_prebake": h0, "Win": hw, "LOD1b": h1, "LOD2b": h2}
            got = {k: h.get(k) for k in want}
            assert got == want, f"{key}: hashes {got} != {want}"
            checked.append(key)
            print(f"  {key}: {got}")
        sd = os.path.join(tmp, "sign", "A")
        cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
               "--python", os.path.join(HERE, "welcome_sign.py"), "--", "--blockout", "--out", sd]
        rc = subprocess.run(cmd, capture_output=True, text=True, timeout=900).returncode
        assert rc == 0, f"welcome_sign --blockout exit {rc}"
        man = json.load(open(glob.glob(os.path.join(sd, "*", "*.manifest.json"))[0], encoding="utf-8"))
        assert man["mesh_hash"]["LOD0_prebake"] == SIGN[2], man["mesh_hash"]
        assert man["band"]["png_sha1"] == SIGN_BAND, man["band"]["png_sha1"]
        checked.append("sign")
        # fine-detail step 3d: --no-fine still reproduces the pre-pass sign (mesh + band)
        sl = os.path.join(tmp, "signlegacy", "A")
        rc = subprocess.run(cmd[:-2] + ["--no-fine", "--out", sl], capture_output=True, text=True,
                            timeout=900).returncode
        assert rc == 0, f"welcome_sign --blockout --no-fine exit {rc}"
        man = json.load(open(glob.glob(os.path.join(sl, "*", "*.manifest.json"))[0], encoding="utf-8"))
        assert (man["mesh_hash"]["LOD0_prebake"], man["band"]["png_sha1"]) == SIGN_LEGACY, \
            (man["mesh_hash"], man["band"]["png_sha1"])
        checked.append("sign --no-fine")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    ok(f"K4 byte identity: {checked} equal to the table (blockout LOD0 / Win + blockout LOD1 / "
       f"LOD2; sign mesh + band){'; skipped ' + str(skipped) if skipped else ''}")


# ======================================================================================
# K5-K7 the engine on a demo volumes recipe
# ======================================================================================

def demo_recipe(**kw):
    r = Recipe(
        key="demo", asset="SkiDemoHutCommercialLow", prefab="SkiDemoHut", category="buildings",
        lot=(2, 2), base_mode="engine", plinth=None, yaw_deg=0.0, tex=512, allow_4096=False,
        budget=Budget(1500, 6000, 650, 200, 500), massing="volumes", letter="A",
        volumes=(Volume("body", -4, 4, -5, 5, 0.0, ((3.0, "stone_rubble"), (2.6, "timber_lap")),
                        ground=True, roofed=True),),
        roofs=(Extra("gable_roof", dict(name="body", spec=RoofSpec(
            "gable", -4, 4, -5, 5, 5.6, 30.0, axis="Y", overhang=0.6), tails=1.2, snow_guard=1)),),
        facades=(FacadeRule("body", "+X", 0, "WDW", 2.4, WinSpec(1.2, 1.4, 0.9, lights=2), margin=0.6),
                 FacadeRule("body", "-X", 1, "WW", 2.6, WinSpec(1.4, 1.2, 0.9, lights=2), room="row"),
                 FacadeRule("body", "+Y", 0, "G", 4.0, WinSpec(1.0, 1.0, 0.0)),
                 FacadeRule("body", "-Y", 1, "SB", 3.0, WinSpec(1.0, 1.0, 0.0))),
        extras=(Extra("flag_mast", dict(name="f", x=3.6, y=-3.0, h=6.0)),),
        lamps=(dict(name="side", at=(-4.0, 3.0, 2.2), normal="-X"),),
        labels=(("sign_bodyYn1b0_face", "paint_pictogram", {"kind": "info"}),),
        ground_prefixes=("wall_body",), arrival_prefixes=("entry_",))
    return replace(r, **kw)


# PNG sha1s of demo_recipe() (weather None) through the PRE-PASS engine (measured 2026-10-11)
K5_REF = {"BaseColor": "46907951f8ce", "ControlMask": "f15308e5d3d5", "Emissive": "a00969e9c689",
          "MaskMap": "0fe1f2dc010a", "Normal": "a9e4fdf47529"}
LABEL = "sign_bodyYn1b0_face"


def _finish(recipe, out, extra=()):
    args = lodge.parse_args(["--recipe", "demo", "--ao-samples", "2", "--out",
                             os.path.join(out, "A")] + list(extra))     # recipe tex 512
    res = lodge.run(recipe, args)
    pngs = {os.path.basename(p).split("_", 1)[1][:-4]: p
            for p in glob.glob(os.path.join(res["asset_dir"], "*.png"))}
    man = json.load(open(glob.glob(os.path.join(res["asset_dir"], "*.manifest.json"))[0],
                         encoding="utf-8"))
    return res, pngs, man


def _png(path):
    img = bpy.data.images.load(path, check_existing=False)
    a = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], -1)
    bpy.data.images.remove(img)
    return a


def k5_weather():
    tmp = tempfile.mkdtemp(prefix="detail_kit_k5_")
    try:
        r0 = demo_recipe()
        res0, p0, m0 = _finish(r0, os.path.join(tmp, "none"))
        sha0 = {k: sha1_file(v) for k, v in sorted(p0.items())}
        assert sha0 == K5_REF, f"weather None: PNGs {sha0} != pre-pass engine {K5_REF}"
        assert "weather" not in m0 and "sub_objects" not in m0 and "labels" not in m0, \
            "weather None wrote new manifest keys"
        ok(f"K5 weather None: {len(sha0)} PNGs byte-identical to the pre-pass engine, no new "
           f"manifest keys")
        rw = demo_recipe(weather={"overrides": (("flag_", {"role": "galv"}),)})
        res1, p1, m1 = _finish(rw, os.path.join(tmp, "w"))
        sha1 = {k: sha1_file(v) for k, v in sorted(p1.items())}
        assert sha1["ControlMask"] == sha0["ControlMask"], "weathering wrote ControlMask"
        assert sha1["BaseColor"] != sha0["BaseColor"], "weathering left BaseColor unchanged"
        w = m1["weather"]
        assert "ok" in w["vanilla"] and w["intensity"] == 0.40 and w["slot_wear"] == "free" \
            and w["snow"] == 0.0 and w["guards"]["G5_ctl_identity"] and \
            w["guards"]["G6_protect_identical"], {k: w[k] for k in ("intensity", "slot_wear", "snow")}
        rects = w.get("protect_texels")
        assert rects and rects > 0, "no protected label texels"
        obj = bpy.data.objects.get(res1["name"] + "Src") or bpy.data.objects[res1["name"]]
        table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
        rr = cs2_compose.uv_bbox_px(obj.data, table, LABEL, 512)
        b0, b1 = _png(p0["BaseColor"]), _png(p1["BaseColor"])
        assert np.array_equal(b0[rr[0]:rr[1], rr[2]:rr[3]], b1[rr[0]:rr[1], rr[2]:rr[3]]), \
            "the label rect changed under the weathering"
        ok(f"K5 weather {{}}: ControlMask identical, BaseColor changed, label rect {rr} identical, "
           f"manifest weather: vanilla ok {w['vanilla']['ok']} (VL1 {w['vanilla']['VL1_mean_rel_dL']}), "
           f"intensity 0.40, free slots, snow 0, G5 / G6 true")
        res2, p2, m2 = _finish(rw, os.path.join(tmp, "sweep"), ["--weather-sweep", "0.35,0.50"])
        sw = m2["weather_sweep"]
        assert [r["intensity"] for r in sw["rows"]] == [0.35, 0.50] and "highest_clean" in sw, sw
        assert sha1_file(p2["BaseColor"]) == sha1["BaseColor"], "the sweep changed the shipped maps"
        ok(f"K5 --weather-sweep 0.35,0.50: rows {[(r['intensity'], r['ok'], r['VL1']) for r in sw['rows']]}, "
           f"highest clean {sw['highest_clean']}; shipped BaseColor unchanged by the sweep")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def k6_k7_blockout():
    tmp = tempfile.mkdtemp(prefix="detail_kit_k67_")
    try:
        subs = (("GardenBenchRandom01", "8df0703be09426948b5bc3262d86bc04", (6.5, 0.0, 4.8),
                 (0.0, 0.0, 0.0, 1.0), -1),
                {"m_Object": "cfc12fde01d9da24b86ddedd657eaae1", "prefab": "WallLightShortRangeRandom01",
                 "position": (-3.5, 3.02, 4.0), "rotation": (0.0, 1.0, 0.0, 0.0), "m_ParentMesh": 0})
        extras = (Extra("flag_mast", dict(name="f", x=3.6, y=-3.0, h=6.0)),
                  Extra("painted_board", dict(name="pb", wall=("+X", 4.0), u=0.0, z=3.6, w=0.6,
                                              h=0.6, density_w=5.0)))
        r = demo_recipe(sub_objects=subs, extras=extras,
                        weights=(("sign_", 1.75), ("sign_bodyYn1b0_frame", 0.6)))
        args = lodge.parse_args(["--recipe", "demo", "--blockout", "--out", os.path.join(tmp, "A")])
        res = lodge.run(r, args)
        man = json.load(open(glob.glob(os.path.join(res["asset_dir"], "*.manifest.json"))[0],
                             encoding="utf-8"))
        so = man["sub_objects"]
        assert so["target_prefab"] == "SkiDemoHut" and len(so["objects"]) == 2
        a, b = so["objects"]
        assert set(a) == {"m_Object", "prefab", "position", "rotation", "m_ParentMesh"}
        # x-sign gate N (2026-10-16): the recipe rows are DESIGN frame, the manifest block is rule T (x -> -x,
        # q -> (qx, -qy, -qz, qw)): the design (-3.5, 3.02, 4.0) / (0, 1, 0, 0) is game (3.5, 3.02, 4.0) / (0, -1, 0, 0)
        assert a["m_ParentMesh"] == -1 and b["m_ParentMesh"] == 0 and b["position"] == [3.5, 3.02, 4.0]
        assert a["position"] == [-6.5, 0.0, 4.8] and b["rotation"] == [0.0, -1.0, 0.0, 0.0]             and a["rotation"] == [0.0, 0.0, 0.0, 1.0], (a, b)
        for bad in ((("X", "zz", (0, 0, 0), (0, 0, 0, 1), -1),),
                    (("X", "8df0703be09426948b5bc3262d86bc04", (0, 0, 0), (0, 0, 0, 2), -1),)):
            try:
                lodge.sub_objects_block(replace(r, sub_objects=bad))
                raise AssertionError("a bad sub-object entry was accepted")
            except ValueError:
                pass
        ok("K6 sub_objects: manifest block (m_Object, prefab, position, rotation, m_ParentMesh) "
           "from tuples and dicts; bad GUID / quaternion refused")
        obj = bpy.data.objects[res["name"]]
        table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
        me = obj.data
        w = cs2_uv.face_density_weights(me)
        pp = np.empty(len(me.polygons), np.int32)
        me.attributes["part"].data.foreach_get("value", pp)
        by = {}
        for i, p in enumerate(pp):
            by.setdefault(table[p - 1], set()).add(round(float(w[i]), 4))
        assert by["sign_bodyYn1b0_face"] == {1.75}, by["sign_bodyYn1b0_face"]
        assert by["sign_bodyYn1b0_frame"] == {0.6}, by["sign_bodyYn1b0_frame"]
        assert by["sign_pb_face"] == {5.0}, by["sign_pb_face"]          # w_override wins
        assert by["sign_pb_frame"] == {1.75}, by["sign_pb_frame"]
        ok("K7 Recipe.weights -> density_w: sign_ face 1.75, longer prefix frame 0.6, a "
           "w_override face keeps 5.0 (precedence w_override > recipe.weights > kit)")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    clear()
    parts = [(K.box_part("x_a", (0, 0, 0.1), (1, 1, 1), "concrete")[0][0], "concrete")]
    assert lodge.part_weights(parts) == K.part_weights(parts) == \
        lodge.part_weights(parts, demo_recipe())
    ok("K7 no recipe weights: lodge.part_weights == K.part_weights")


# ======================================================================================
# K8 interior faces / soffit split
# ======================================================================================

def _area(parts):
    return sum(p.area for o, _m in parts for p in o.data.polygons)


def _faces(parts):
    return sum(len(o.data.polygons) for o, _m in parts)


def k8_inner_soffit():
    clear()
    base = K.prism_gable("k8a", "X", 2.0, 2.3, -3.9, 4.2, 3.0, 6.0, ridge_at=1.3, base_z=1.3)
    a0 = K.prism_gable("k8b", "X", 2.0, 2.3, -3.9, 4.2, 3.0, 6.0, ridge_at=1.3, base_z=1.3, inner="a0")
    a1 = K.prism_gable("k8c", "X", 2.0, 2.3, -3.9, 4.2, 3.0, 6.0, ridge_at=1.3, base_z=1.3, inner="a1")
    pent = 8.1 * 1.7 + 0.5 * 8.1 * 3.0
    assert _faces(a0) == _faces(base) - 1 and _faces(a1) == _faces(base) - 1
    assert abs(_area(base) - _area(a0) - pent) < 1e-3 and abs(_area(base) - _area(a1) - pent) < 1e-3
    n0 = [p.normal.x for p in a0[0][0].data.polygons if abs(abs(p.normal.x) - 1) < 1e-6]
    assert n0 == [1.0], f"inner='a0' must keep only the +X end face, got {n0}"
    spec = RoofSpec("gable", -4, 4, -5, 5, 4.0, 30.0, axis="Y", overhang=0.6)
    g0 = K.gable_roof("k8g", spec, walls=True)
    g1 = K.gable_roof("k8h", spec, walls=True, wall_inner=False)
    tri = 0.5 * 8.0 * (4.0 * math.tan(math.radians(30.0)) - 0.02 + 0.0)
    assert _faces(g1) == _faces(g0) - 2
    da = _area(g0) - _area(g1)
    assert abs(da - 2 * tri) < 0.05, f"wall_inner=False removed {da:.3f} m2, want {2 * tri:.3f}"
    d0 = K.deck("k8d", 2.3, 4.1, -3.9, 5.0, 1.3, support="none", fascia=0.0, thick=0.05)
    d1 = K.deck("k8e", 2.3, 4.1, -3.9, 5.0, 1.3, support="none", fascia=0.0, thick=0.05, soffit=True)
    assert [o.name for o, _ in d1] == ["deck_k8e", "soffit_deckk8e"]
    assert abs(_area(d0) - _area(d1)) < 1e-6 and _faces(d1) == _faces(d0)
    assert abs(_area(d1[1:]) - 1.8 * 8.9) < 1e-6
    assert all(p.normal.z < -0.99 for p in d1[1][0].data.polygons)
    ds = K.deck("k8s", 0, 2, 0, 2, 1.0, support="solid", soffit=True)
    assert not any(o.name.startswith("soffit_") for o, _ in ds), "solid deck grew a soffit"
    ok(f"K8 inner 'a0' / 'a1' drop one end face ({pent:.2f} m2); wall_inner=False drops 2 "
       f"({da:.2f} m2); deck soffit=True moves the {1.8 * 8.9:.2f} m2 underside into "
       f"soffit_deck<name> (total area unchanged); solid decks unchanged")


# ======================================================================================
# K9 detail pass 1b: rack gear reaches the rail, L1 umbrella tables
# ======================================================================================

def _recipe_module(rel):
    return R._load_module(os.path.join(HERE, rel))


def k9_rack_reach():
    reach = K.rack_reach()
    racks = {"L1": [(n, g) for n, _x, _y, g in R.L1_SKI_RACKS],
             "L2": list(_recipe_module("recipes/lodge_hut.py").RACK_GEAR.items()),
             "school": [(n, g) for n, _x, _y, g in _recipe_module("recipes/school.py").RACKS]}
    n = 0
    for key, rs in racks.items():
        for name, gear in rs:
            for e in gear:
                _j, _sg, kind, _col, length = K._gear_entry(e, "accent")
                assert length >= reach - 1e-9, \
                    f"{key} rack {name}: a {kind} of {length} m stops short of the {reach:.3f} m lean"
                n += 1
    # L5 (not this pass's file): every (slot, side, kind, color, length) tuple of a rack table
    l5 = _recipe_module("recipes/l5_swanky_bottom.py")
    short = 0
    for v in vars(l5).values():
        if isinstance(v, tuple):
            for r in v:
                if isinstance(r, tuple) and len(r) >= 3 and isinstance(r[-1], tuple):
                    for e in r[-1]:
                        if isinstance(e, tuple) and len(e) == 5 and e[2] in K.GEAR_KINDS \
                                and e[4] < reach - 1e-9:
                            short += 1
    ok(f"K9a every gear item on the L1 / L2 / school racks ({n}) spans the {reach:.3f} m lean "
       f"(pole pairs were 1.18-1.28 m); L5's racks (not this pass's file): {short} short item(s)")


def _variant_bounds(variants):
    """{variant: (lo, hi)} of the base-game render meshes, or None when the dump is absent."""
    import zipfile
    import cs2_liftdress as LD
    if not os.path.isfile(LD.DUMP_ZIP):
        return None
    z = zipfile.ZipFile(LD.DUMP_ZIP)
    out = {}
    for v in variants:
        t = z.read(f"00_BaseGame/Game.Prefabs.RenderPrefab/{v} Mesh/{v} Mesh.Prefab").decode("utf-8", "replace")
        b = [float(x) for x in LD._BOUNDS_RE.search(t).groups()]
        out[v] = (b[:3], b[3:])
    return out


# K9b box contract (game local, prop frame): a parasol is its pole and its canopy tier (the review
# convention of recipes/l5_swanky_bottom.PARASOL_POLE / PARASOL_TIER); every other prop is its union
# box from 5 mm above its pivot (a prop's feet sit in the surface by design). The trash bin uses
# the union of the TrashbinEmpty / Full variant meshes (+-0.355 x +-0.364, the dump; the cached
# placeholder mesh alone is +-0.246).
K9_POLE, K9_TIER = 0.30, 1.80
K9_BIN = ((-0.355, 0.0, -0.364), (0.355, 1.036, 0.364))


def _k9_boxes(pf, pos, q, bounds):
    """[(tag, lo, hi)] game-local AABBs of one row (yaw-only rotation)."""
    a = 2.0 * math.atan2(q[1], q[3])
    c, s_ = math.cos(a), math.sin(a)

    def box(lo, hi):
        P = np.array([[x * c + z * s_, y, -x * s_ + z * c] for x in (lo[0], hi[0])
                      for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]) + np.asarray(pos, float)
        return P.min(0), P.max(0)
    lo, hi = bounds
    if pf == "ParasolRandom01":
        return [("pole",) + box((-K9_POLE, 0.005, -K9_POLE), (K9_POLE, K9_TIER, K9_POLE)),
                ("canopy",) + box((lo[0], K9_TIER, lo[2]), hi)]
    return [("body",) + box((lo[0], 0.005, lo[2]), hi)]


def k9_l1_tables():
    import cs2_liftdress as LD
    import cs2_subobj as SO
    from mathutils.bvhtree import BVHTree
    # fine-detail step 3a: the shipping L1 is the FineRecipe of recipes/lodge_aframe_fine.py (bldg_recipes
    # OVERRIDES); its first 22 rows are the deck / porch rows checked here (the plain R.L1 holds exactly
    # those), then the 2 porch wall lights (K10) and the 6 kit racks (boxed here too, on the lot)
    rec = R.get_recipe("L1")
    ums = R.AFRAME_L1["UMBRELLAS"]
    # T26 2026-10-16 dropped the parasols (12 rows); restored 2026-10-17 (vanilla props, random colors
    # accepted): the deck / porch rows are 10 tables + 10 parasols + 2 porch = 22 again
    rows = tuple(rec.sub_objects[:22])
    kit = tuple((e["prefab"], e["m_Object"], tuple(e["position"]), tuple(e["rotation"]), e["m_ParentMesh"])
                for e in rec.sub_objects if isinstance(e, dict) and e.get("kind") == "mod")
    assert R.AFRAME_L1.get("PARASOLS_VANILLA") is True
    assert rows == R.L1_TABLES + R.L1_PARASOLS + R.L1_PORCH and tuple(R.L1.sub_objects) == rows, len(rows)
    assert len(kit) == 6, kit
    assert len(R.L1_TABLES) == len(R.L1_PARASOLS) == len(ums) == 10
    tbl = LD.SUBOBJ["TableSetCommercialRandom01"]
    for r in R.L1_TABLES:
        name, guid, pos, q, parent = r
        assert name == "TableSetCommercialRandom01" and guid == tbl["guid"], r
        assert parent == 0 and q == (0.0, 0.0, 0.0, 1.0) and abs(pos[1] - 4.66) < 1e-9, r
    for (ux, uy), t, p in zip(ums, R.L1_TABLES, R.L1_PARASOLS):
        assert t[2] == (round(-uy, 4), 4.66, round(-ux, 4)), (t, ux, uy)
        name, guid, pos, q, parent = p
        assert name == "ParasolRandom01" and guid == "b7e2343fd7ff9f642b77084a04738e43", p
        assert parent == 0 and abs(pos[1] - 4.66) < 1e-9 and q in ((0.0, 1.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0)), p
        a = 2.0 * math.atan2(q[1], q[3])
        f = (math.sin(a), 0.0, math.cos(a))                 # local +Z in game local
        d = [t[2][i] - pos[i] for i in range(3)]
        assert all(abs(d[i] - R.PARASOL_OFFSET * f[i]) < 1e-6 for i in range(3)), \
            f"parasol {pos} does not face its table {t[2]} at {R.PARASOL_OFFSET} m"
    porch = {r[0]: r for r in R.L1_PORCH}
    assert set(porch) == {"GardenBenchRandom01", "TrashbinRandom01"}
    assert porch["GardenBenchRandom01"][1] == LD.SUBOBJ["GardenBenchRandom01"]["guid"]
    assert porch["TrashbinRandom01"][1] == LD.SUBOBJ["TrashbinRandom01"]["guid"]
    assert all(r[4] == -1 and r[2][1] == 0.0 and r[3] == (0.0, 0.0, 0.0, 1.0) for r in R.L1_PORCH)
    blk = lodge.sub_objects_block(rec)
    assert blk["target_prefab"] == "SkiAFrameDayLodge" and len(blk["objects"]) == len(rec.sub_objects)
    assert [o["m_ParentMesh"] for o in blk["objects"][:22]] == [0] * 20 + [-1, -1]
    # bounds: the cache (subobj_bounds.json), re-derived from the dump when it is present
    bounds, src = {}, "cache"
    idx = SO._dump()
    for pf, guid in {r[0]: r[1] for r in rows}.items():
        (lo, hi), _s = SO.lookup_bounds(pf, guid)
        if idx is not None:
            got = idx.derive(pf, guid)
            assert idx.guid_of(pf) == guid, (pf, idx.guid_of(pf), guid)
            assert all(abs(a_ - b_) < 0.0015 for a_, b_ in zip(lo + hi, got["bounds"][0] + got["bounds"][1])), \
                (pf, (lo, hi), got["bounds"])
            src = "dump"
        bounds[pf] = (tuple(lo), tuple(hi))
    bounds["TrashbinRandom01"] = K9_BIN
    for pf, _g, _p, _q, _par in kit:                       # dressing-kit racks: the cache (their manifests)
        (lo, hi), s_ = SO.lookup_bounds(pf, None)
        assert s_ == "cache", (pf, s_)
        bounds[pf] = (tuple(lo), tuple(hi))
    if idx is not None:
        bb = list(_variant_bounds([f"Trashbin{k}0{n}" for k in ("Empty", "Full")
                                   for n in range(1, 5)]).values())
        e = 1e-3                                            # K9_BIN is rounded to the mm
        assert len(bb) == 8 and all(
            lo_[0] >= K9_BIN[0][0] - e and hi_[0] <= K9_BIN[1][0] + e and lo_[2] >= K9_BIN[0][2] - e
            and hi_[2] <= K9_BIN[1][2] + e and hi_[1] <= K9_BIN[1][1] + e for lo_, hi_ in bb), bb
    tmp = tempfile.mkdtemp(prefix="detail_kit_k9_")
    try:
        args = lodge.parse_args(["--recipe", "L1", "--blockout", "--out", os.path.join(tmp, "L1", "B")])
        res = lodge.run(rec, args)
        obj = bpy.data.objects[res["name"]]
        table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
        gone = [n for n in table if n.startswith(("umbstand_", "umbpole_", "umbcanopy_", "bench_", "bin_",
                                                  "skirack_entry", "skirack_slope", "lamp_porch"))]
        assert not gone, f"modeled umbrella / bench / bin / rack / porch-lantern parts left: {gone[:6]}"
        rows = rows + kit                                   # box the kit racks too (ground props)
        me = obj.data
        pid = np.empty(len(me.polygons), np.int32)
        me.attributes["part"].data.foreach_get("value", pid)
        verts = [tuple(v.co) for v in me.vertices]
        tris, owner = [], []
        for i, p in enumerate(me.polygons):
            vv = list(p.vertices)
            for j in range(1, len(vv) - 1):
                tris.append((vv[0], vv[j], vv[j + 1]))
                owner.append(table[pid[i] - 1])
        tree = BVHTree.FromPolygons(verts, tris, all_triangles=True)
        quads = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        btris = [(q_[0], q_[1], q_[2]) for q_ in quads] + [(q_[0], q_[2], q_[3]) for q_ in quads]
        boxes = []
        for k, (pf, _g, pos, q, _par) in enumerate(rows):
            for tag, lo, hi in _k9_boxes(pf, pos, q, bounds[pf]):
                boxes.append((k, pf, tag, lo, hi))
                # game local (gx, gy, gz) -> object frame (gx, -gz, gy) (orient_for_net + FBX bake)
                bverts = [Vector((gx, -gz, gy)) for gx in (lo[0], hi[0]) for gy in (lo[1], hi[1])
                          for gz in (lo[2], hi[2])]
                hit = tree.overlap(BVHTree.FromPolygons(bverts, btris, all_triangles=True))
                assert not hit, (f"{pf} {k} {tag} at {pos}: {len(hit)} lodge triangle pair(s) inside its "
                                 f"box: {sorted({owner[a_] for a_, _b in hit})}")
        near = 9.0
        for i, (ka, pa, ta, la, ha) in enumerate(boxes):
            for kb, pb, tb, lb, hb in boxes[i + 1:]:
                if ka == kb:
                    continue
                gap = float(np.max(np.maximum(la - hb, lb - ha)))
                own = {pa, pb} == {"ParasolRandom01", "TableSetCommercialRandom01"} and "pole" in (ta, tb) \
                    and math.dist(rows[ka][2], rows[kb][2]) < 1.6
                if not own:
                    assert gap > 0.0, f"boxes overlap: {pa} {ka} {ta} x {pb} {kb} {tb} ({gap:.3f})"
                    near = min(near, gap)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    ok(f"K9b L1 vanilla deck: 10 TableSetCommercialRandom01 + 10 ParasolRandom01 (each "
       f"{R.PARASOL_OFFSET} m beside its table, facing it) + GardenBench + Trashbin (lot), verified GUIDs "
       f"({src} bounds), the manifest block (22 + the step-3a rows), no modeled umbrella / bench / bin / "
       f"rack / porch lantern left, no lodge triangle inside any prop box (+ the 6 kit racks), no box "
       f"overlap but a pole in its own table (closest {near:.3f} m)")


# ======================================================================================
# K10 fine-detail step 3a: the L1 rows at the parts they replaced (H1) + H9
# ======================================================================================

L1_PRE_PASS_LOT = {"x": (-17.079, 18.4), "y": (-22.75, 22.12)}   # the pre-pass manifest (2026-10-12 00:56)


def k10_l1_fine():
    """recipes/lodge_aframe_fine.py (the shipping L1) against the PRE-PASS builder parts (`--no-fine`):
    each kit rack row (identity, on the lot) = the removed ski_rack frame vertex for vertex +-1 mm, 2 cm
    lower (the kit foot is on y 0, the old on GROUND_LIFT; negative: a 90-degree yaw misses); the 3
    loadouts alternate; the porch wall lights on the porch columns' entry face at the old lens (y, z)
    +-0.01 m, identity; the 8 leaning singles kept vertex for vertex (FS6); the rail-F lanterns kept;
    the deck flag mast fine (58 tris, 20 before); lot bbox = the pre-pass +-5 mm (H9)."""
    import fine_detail as FD
    from mathutils import Quaternion
    rec = R.get_recipe("L1")
    assert isinstance(rec, FD.FineRecipe) and rec.fine_detail and rec.sign_set is None, type(rec)
    leg = FD.effective(rec, fine=False)
    assert leg.extras == R.L1.extras and leg.params == R.L1.params and leg.sub_objects == R.L1.sub_objects

    def parts_of(r):
        lodge.clear_scene()
        ps = lodge.build_parts(r, {"tower": True, "roof_windows": "win"})
        bpy.context.view_layer.update()
        V = {o.name: np.array([tuple(o.matrix_world @ v.co) for v in o.data.vertices]) for o, _k in ps}
        T = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o, _k in ps}
        lodge.clear_scene()
        return V, T
    lv, lt = parts_of(leg)
    fv, ft = parts_of(rec)
    gone = sorted(n for n in fv if n.startswith(("skirack_entry", "skirack_slope", "lamp_porch")))
    assert not gone, f"K10: replaced parts still built: {gone}"
    lean = sorted(n for n in lv if n.startswith("skirack_leang"))
    assert lean and all(n in fv and np.array_equal(fv[n], lv[n]) for n in lean), f"K10: leaning singles {lean}"
    rails = [f"lamp_railF{k}" for k in range(4)]
    assert all(n in fv for n in rails), "K10: the rail-F post lanterns are kept"
    assert ft["flag_deck"] == 58 and lt["flag_deck"] == 20, (ft["flag_deck"], lt["flag_deck"])
    # kit racks: the kit frame (ski_rack_frame slots 8 along X at the origin, prefab frame (x, z, -y))
    kitf = K.ski_rack_frame("k10kit", 0.0, 0.0, slots=8, along="X")[0][0]
    KV = np.array([tuple(kitf.matrix_world @ v.co) for v in kitf.data.vertices], np.float64)
    m_ = kitf.data
    bpy.data.objects.remove(kitf, do_unlink=True)
    bpy.data.meshes.remove(m_)
    KP = np.stack([KV[:, 0], KV[:, 2] - K.GROUND_LIFT, -KV[:, 1]], axis=1)
    rows = [e for e in rec.sub_objects if isinstance(e, dict) and e.get("kind") == "mod"]
    assert [e["prefab"] for e in rows] == list(FD.AFRAME_RACKS) * 2, [e["prefab"] for e in rows]
    worst = 0.0
    for (nm, _x, _y, _g), e in zip(R.L1_SKI_RACKS, rows):
        assert e["m_ParentMesh"] == -1 and FD.row_colour_error(e) is None, (e, FD.row_colour_error(e))
        x_, y_, z_, w_ = e["rotation"]
        G = KP @ np.array(Quaternion((w_, x_, y_, z_)).to_matrix(), np.float64).T + np.array(e["position"])
        old = lv[f"skirack_{nm}"]                            # pre-yaw authoring -> game (-y, z, -x)
        OG = np.stack([-old[:, 1], old[:, 2] - K.GROUND_LIFT, -old[:, 0]], axis=1)
        d = max(float(np.min(np.linalg.norm(G - p, axis=1))) for p in OG)
        assert d <= 0.001, f"K10 H1: kit row {e['prefab']} {d * 1000:.2f} mm from skirack_{nm}"
        worst = max(worst, d)
        Gw = KP @ np.array(Quaternion((0.7071068, 0.0, 0.7071068, 0.0)).to_matrix(), np.float64).T + \
            np.array(e["position"])
        assert max(float(np.min(np.linalg.norm(Gw - p, axis=1))) for p in OG) > 0.05, "K10 negative: yaw 90 matched"
    # porch wall lights: cs2_bldg.lantern(kind "wall") puts the lens 0.22 m out from the face point
    walls = [e for e in rec.sub_objects if not isinstance(e, dict) and e[0] == "WallLightShortRangeRandom01"]
    assert len(walls) == 2, walls
    for tag, r in zip(("S", "N"), walls):
        P = lv[f"lamp_porch{tag}"]
        c = 0.5 * (P.min(0) + P.max(0))
        at = c + np.array((0.22, 0.0, 0.0))                  # pre-yaw: the lens sits 0.22 m toward -X
        want = (-at[1], at[2], -at[0])
        assert max(abs(a - b) for a, b in zip(r[2], want)) <= 0.01 and tuple(r[3]) == (0.0, 0.0, 0.0, 1.0) \
            and r[4] == 0, f"K10 H1: porch light {tag} {r[2]} vs {want}"
        col = lv[f"col_porch{tag}"]
        assert abs(-r[2][2] - col[:, 0].min()) <= 0.005, f"K10: porch light {tag} not on the column face"
    # H9: the shipping L1's blockout lot bbox
    tmp = tempfile.mkdtemp(prefix="detail_kit_k10_")
    try:
        lodge.clear_scene()
        res = lodge.run(rec, lodge.parse_args(["--recipe", "L1", "--blockout", "--out", os.path.join(tmp, "L1", "B")]))
        lot = res["lot"]
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    for ax in ("x", "y"):
        d_ = max(abs(a - b) for a, b in zip(lot[ax], L1_PRE_PASS_LOT[ax]))
        assert d_ <= 0.005, f"K10 H9: lot {ax} {lot[ax]} vs pre-pass {L1_PRE_PASS_LOT[ax]}"
    ok(f"K10 fine-detail L1 (step 3a): 6 kit rack rows = the removed frames +-1 mm (worst {worst * 1000:.2f} "
       f"mm, identity; yaw 90 misses), loadouts V1 / V2 / V3 alternate, {len(lean)} leaning-single parts "
       f"kept vertex for vertex (FS6), rail-F lanterns kept, 2 porch wall lights on the column faces at the "
       f"old lens (y, z), flag mast 58 tris (20 before), lot x {lot['x']} y {lot['y']} = the pre-pass +-5 mm")


def main():
    t0 = time.perf_counter()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    k1_builders()
    k2_display()
    k3_metrics()
    k8_inner_soffit()
    k9_rack_reach()
    k6_k7_blockout()
    if "--no-k5" in argv:
        print("skip K5 (--no-k5)")
    else:
        k5_weather()
    if "--no-k9" in argv:
        print("skip K9b (--no-k9)")
    else:
        k9_l1_tables()
    if "--no-k10" in argv:
        print("skip K10 (--no-k10)")
    else:
        k10_l1_fine()
    if "--no-k4" in argv:
        print("skip K4 (--no-k4)")
    else:
        k4_identity()
    print(f"PASS test_detail_kit: {len(CHECKS)} check groups ({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_detail_kit")
        sys.exit(1)
