"""
test_liftdress.py: contract test for cs2_liftdress.py and the additive `share=` keyword of cs2_uv.pack_unique /
cs2_bake.bake_unique (detail pass 1, lifts, design design/detail-pass-1-lifts.md 9.2 "shared" row, as amended by
the skeptic's MC1-MC11).

Run:  blender --background --factory-startup --python-exit-code 1 --python tests\\test_liftdress.py
Exit code 1 on any failure. The test prints one PASS line on success. Optional: -- --golden prints the K5
fixture hashes (run it with the PRE-PASS cs2_uv / cs2_bake preloaded to re-derive the constants below, the build
report has the command).

K1 builders: every builder (sample calls in the DQ / tram frames) is checked for these items:
- family prefixes (BUILDER_PREFIXES)
- material keys (DRESS_MATERIALS)
- tris within +-10 % of EXPECTED_TRIS
- min z >= the builder's z0
- no duplicate names, no dotted names
- lod_split classifies every part (mast / arm, bridge grating + beams, ladder rails kept to LOD1)
- the kind pairs that touch INSIDE a builder (BVH overlap) are all in DRESS_CONTACTS
- every label quad's UVMap reads u = viewer right, v = up, 0..1
- fixed_ladder style "dq" reproduces lift_terminal_detach._ladder's parts vertex for vertex
- style "tram" is parsed by tram_station.walk_ladders (C15) with the right rung count, pitch, foot and first
  rung

K2 label sheet: deterministic. Rects are disjoint and inside the region. Gutters are >= 6 px. The design's DQ
base / top sheets fit the reserved strip (u 806-2042, v 1648-2040 px). Overflow and too-wide items raise.

K3 painters:
- every BOARDS kind paints at its design density with .last metrics (cap_m, stroke_px, glyph_m, rect, t1 / t2,
  read_m)
- every shipped kind holds T2 (0.07 m words, >= 4-texel stems and pictogram strokes)
- 2026-10-12 lift-board redraw: the 0.70 x 0.55 "plate" boards carry ONE real-size portrait placard
  (PLACARD_RATIO) on the backer. T1's 0.40 m pictogram is no longer claimed for them (read_m >= 25 m instead).
  SLOW / stn / dep keep T1.
- the reference-vocabulary kinds (vocab) are on NO station's label table and declare their fine print
- ControlMask 0 and alpha 1 over the rect
- deterministic
- "IW" reads I-left-of-W and "L" keeps its foot at the bottom (rows bottom-up = the stamp frame, unmirrored)
- a circle pictogram stays round in METERS at anisotropic px/m
- the LED board's dots are >= 4 texels
- stamp_on_face painter wrapper

K9 the redraw's glyph primitives:
- the bar clears the rider's head in both states
- the lowered cross-bar sits at chest height in front of the torso
- the raised bar sits above the head
- the ski is one thin line with its tip curved up (no blocks)
- every pictogram stroke is >= 12 canvas units

2026-10-12 nit fix (kit color-variant fleet): the LOWER BAR hoop is loose (>= 18 units over the head top and
ahead of the face). Every arrow (LOWER / RAISE / TIPS UP) clears the ring's inner edge (in the shipped 0.225 m
circle at 400 px/m) and the figure by >= 9 canvas units (3 texels). Negatives: the pre-fix arrow (x 212 / 214,
head 30) and the pre-fix hoop fail.

K4 share_identical: translation copies are grouped. A rotated and a mirrored copy are rejected. A different
material is rejected. The size cap / whitelist / never-list are honored. An ACTIVE DetailSpec prefix is excluded
(MC6), and the same prefix is shared when inactive. Pinned faces are excluded. The result is deterministic.
pack_unique(share=...) stacks every dup loop on its canonical loop's UV, passes the layout contract, reports
shared_faces, and packs fewer islands. A malformed share list raises.

K5 share=None byte-identical: pack_unique() == pack_unique(share=None) on the fixture, and both == the PRE-PASS
module's hash (GOLDEN_PACK). bake_unique() == bake_unique(share=None) PNG bytes == GOLDEN_BAKE. The
bake_unique(share=[]) report carries shared_faces 0 and the same bytes. (The shipped DQ + tram stations rebuilt
before / after: the build report.)

K6 weathering helpers: lift_mat_roles maps by material INDEX from CS2SRC_<key> slots, and raises on an unmapped
key and on a non-placeholder slot. station_weather_spec = station preset, intensity 0.40, slot_wear free, snow
0, edge_band_m 0.08 (0.06 and snow raise). merge_materials agrees with the DQ and tram tables.
highest_clean_step / budget_ok are checked. label_protect_px returns the exact label's texel bbox.

K7 vanilla sub-objects: verify_subobj re-derives every GUID / variant set / component / bounds / tris from
%TEMP%\\pltar\\dump.zip (SKIP when the dump is absent). sub_object_rows / subobj_box / facing_quat are checked.
The WarningLight v02 top over the tram-top goal beam reaches 27.75 (skeptic row 10: above the frozen 27.00).
"""

import hashlib
import math
import os
import re
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
from mathutils import Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

import cs2_asset  # noqa: E402
import cs2_compose  # noqa: E402
import cs2_liftdress as LD  # noqa: E402
import cs2_uv  # noqa: E402

FAILS = []
OKS = [0]

# K5 goldens: the fixture packed / baked by the PRE-PASS cs2_uv / cs2_bake (sha1 abd2d830 /
# 0014cca1), derived with `-- --golden` and those modules preloaded (build report, K5).
GOLDEN_PACK = "b57877191610be76"
GOLDEN_BAKE = {"BaseColor": "88dd394eecf603f2", "Normal": "de7d8e24102f2c21", "MaskMap": "a03e38aeed8e012f",
               "ControlMask": "3f08f5b10af8dea5"}

# measured on this build, with the design 2.1 figure in the comment (deviations: build report)
EXPECTED_TRIS = {
    "maze": 216,          # 212
    "swing_gate": 48,     # 50 (with the label panel)
    "board_wall": 12,     # 14
    "board_post": 32,     # 38
    "board_hang": 30,     # 30
    "flat_label": 2,      # 2
    "stripe": 10,         # 10
    "chevrons": 39,       # 10 + 2 / stripe
    "carpet": 30,         # 32
    "extinguisher": 44,   # 40
    "hose_reel": 74,      # 60
    "tools": 48,          # 50
    "ski_rack": 336,      # 210 (rack 56 + the kit gear 280: ONE ski-gear part, no bindings)
    "aframe_rack": 524,   # tram rev 2 (design 1.7): the A-frame kit rack, L1's entry-rack contents
    "estop_post": 46,     # 40
    "flood_mast": 90,     # 74
    "ladder_dq": 128,     # 14 / rail + 8 / rung
    "ladder_tram": 78,
    "hatch_frame": 34,    # 40
    "bridge": 156,        # 250 (one rail side here)
    "beacon": 32,         # 32
    "crane_hook": 44,     # 36
    "gauge": 22,          # 10 (+ the case)
}

# (call, z0 the builder may not go below)
CASES = {
    "maze": (lambda: LD.maze("ld", [[(5.70, 4.20), (9.80, 4.20)], [(5.70, 5.40), (9.80, 5.40)]],
                             0.30), 0.30),
    "swing_gate": (lambda: LD.swing_gate("ld", (5.70, 4.20), (5.70, 5.40), 0.30, panel=True), 0.30),
    "board_wall": (lambda: LD.board("ops", (7.50, 5.577, 2.525), "-Y", 1.20, 0.35), 2.35),
    "board_post": (lambda: LD.board("slow", (13.75, -5.0, 2.175), "+X", 1.20, 0.35, mount="post",
                                    post_z0=0.30), 0.30),
    "board_hang": (lambda: LD.board("lowerbar", (-1.50, 1.65, 2.575), "+X", 0.70, 0.55,
                                    mount="hang", rods_to=3.35), 2.30),
    "flat_label": (lambda: LD.flat_label("seat1", 3.05, 3.30, 1.575, 1.825, 0.33), 0.33),
    "stripe": (lambda: LD.stripe("load", (2.80, 1.25), (2.80, 3.95), 0.10, 0.30), 0.30),
    "chevrons": (lambda: LD.chevrons("noseC", "-X", -8.0, (-2.05, 2.05), (3.40, 3.55)), 3.40),
    "carpet": (lambda: LD.carpet("ld", 2.80, 5.60, 1.25, 3.95, 0.30), 0.30),
    "extinguisher": (lambda: LD.extinguisher("cab", (9.10, 7.30), "+X", 1.20), 1.20),
    "hose_reel": (lambda: LD.hose_reel("eng", (3.00, 7.45), "-X", 1.25), 0.90),
    "tools": (lambda: LD.tools("cab", (9.42, 7.65), "+X", 0.30), 0.30),
    "ski_rack": (lambda: LD.ski_rack("b", 10.2, 13.2, -6.60, 0.30), 0.30),
    # tram rev 2 (skeptic MC5): the A-frame kit rack delegation, at the tram base's spot and contents
    "aframe_rack": (lambda: LD.aframe_rack("ar", 11.20, 10.80, __import__("tram_station").TRAM_RACK_GEAR, z0=0.02,
                                           along="Y"), 0.02),
    "estop_post": (lambda: LD.estop_post("ld", (2.80, 0.75), 0.30), 0.30),
    "flood_mast": (lambda: LD.flood_mast("bN", (13.75, -5.25), 0.30, (10.0, 0.0)), 0.30),
    "ladder_dq": (lambda: LD.fixed_ladder("pf", (-1.15, 0.70), (1.0, 0.0),
                                          [0.30 + 0.30 * k for k in range(1, 10)], (0.30, 3.30),
                                          standoffs=[(1.5, 0.10)]), 0.30),
    "ladder_tram": (lambda: LD.fixed_ladder("pit", LD.tram_point(-10.00, 0.0, -8.62)[:2],
                                            LD.tram_point(0.0, 0.0, -1.0)[:2],
                                            [3.90 + 0.30 * k for k in range(9)], (3.62, 7.40),
                                            style="tram"), 3.62),
    "hatch_frame": (lambda: LD.hatch_frame("sofpf", (-1.15, 0.70, 3.35)), 3.30),
    "bridge": (lambda: LD.bridge("bridgeR", -0.70, 0.50, 5.95, 10.05, 6.30,
                                 rails=[[(0.45, 5.98), (0.45, 10.0)]]), 6.0),
    "beacon": (lambda: LD.beacon("beacon", LD.tram_point(0.0, 26.80, 5.30)), 26.80),
    "crane_hook": (lambda: LD.crane_hook("R", LD.tram_point(9.58, 24.55, 5.30)), 23.30),
    "gauge": (lambda: LD.gauge("tension", (0.10, 5.16), "+Y", 1.35), 1.18),
}
BUILDER_OF = {"board_wall": "board", "board_post": "board", "board_hang": "board",
              "ladder_dq": "fixed_ladder", "ladder_tram": "fixed_ladder"}


def check(cond, msg):
    if cond:
        OKS[0] += 1
    else:
        FAILS.append(msg)
        print(f"  FAIL {msg}")
    return bool(cond)


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)


def world_tris(o):
    bpy.context.view_layer.update()
    mw = o.matrix_world
    V = [mw @ v.co for v in o.data.vertices]
    out = []
    for p in o.data.polygons:
        vs = list(p.vertices)
        for j in range(1, len(vs) - 1):
            out.append((V[vs[0]], V[vs[j]], V[vs[j + 1]]))
    return out


def kind(name):
    m = re.match(r"[a-z]+", name)
    return m.group(0) if m else name


def ntris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


# ======================================================================================
# K1 builders
# ======================================================================================

def k1_builders():
    clear_scene()
    seen = set()
    all_parts = []
    for case, (fn, z0) in CASES.items():
        parts = fn()
        all_parts += parts
        builder = BUILDER_OF.get(case, case)
        names = [o.name for o, _m in parts]
        check(len(names) == len(set(names)), f"K1 {case}: duplicate part names")
        check(not (set(names) & seen), f"K1 {case}: names clash with another builder")
        seen |= set(names)
        check(all("." not in n for n in names), f"K1 {case}: dotted (renamed) part name")
        pfx = LD.BUILDER_PREFIXES[builder]
        bad = [n for n in names if not n.startswith(pfx)]
        check(not bad, f"K1 {case}: names outside {pfx}: {bad}")
        keys = {str(m) for _o, m in parts}
        check(keys <= set(LD.DRESS_MATERIALS), f"K1 {case}: material keys {keys - set(LD.DRESS_MATERIALS)}")
        n = sum(ntris(o) for o, _m in parts)
        exp = EXPECTED_TRIS[case]
        check(abs(n - exp) <= 0.10 * exp + 1, f"K1 {case}: {n} tris, expected {exp} +-10 %")
        bpy.context.view_layer.update()
        zmin = min(min((o.matrix_world @ v.co).z for v in o.data.vertices) for o, _m in parts)
        check(zmin >= z0 - 1e-5, f"K1 {case}: min z {zmin:.4f} below z0 {z0}")
        lod = LD.lod_split(parts)
        check(len(lod["lod1_delete"]) + len(lod["lod1_keep"]) == len(parts),
              f"K1 {case}: lod_split lost parts")
        # touching pairs inside the builder -> declared kinds
        tris = {o.name: world_tris(o) for o, _m in parts}
        trees = {}
        for nm, T in tris.items():
            verts = [v for t in T for v in t]
            trees[nm] = BVHTree.FromPolygons(verts, [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(len(T))],
                                            all_triangles=True)
        pairs = set()
        for i, a in enumerate(names):
            for b in names[i + 1:]:
                if trees[a].overlap(trees[b]):
                    pairs.add(tuple(sorted((kind(a), kind(b)))))
        undeclared = sorted(p for p in pairs if p not in LD.DRESS_CONTACT_KINDS)
        check(not undeclared, f"K1 {case}: touching kind pairs not in DRESS_CONTACTS: {undeclared}")
        # label quads: u = viewer right, v = up (or the floor read-up), 0..1
        for o, _m in parts:
            if not o.name.startswith("label_"):
                continue
            me = o.data
            check(len(me.polygons) == 1, f"K1 {o.name}: {len(me.polygons)} faces (one quad)")
            pl = me.polygons[0]
            uv = me.uv_layers["UVMap"].data
            P = [o.matrix_world @ me.vertices[me.loops[li].vertex_index].co for li in pl.loop_indices]
            Q = [Vector(uv[li].uv) for li in pl.loop_indices]
            E = np.array([list(P[1] - P[0]), list(P[2] - P[0])]).T
            Qm = np.array([list(Q[1] - Q[0]), list(Q[2] - Q[0])]).T
            J = E @ np.linalg.inv(Qm)
            du, dv = J[:, 0] / np.linalg.norm(J[:, 0]), J[:, 1] / np.linalg.norm(J[:, 1])
            nrm = np.array((o.matrix_world.to_3x3() @ pl.normal).normalized())
            if abs(nrm[2]) < 0.5:
                right = np.cross((0.0, 0.0, 1.0), nrm)
                right /= np.linalg.norm(right)
                check(du @ right > 0.99 and dv[2] > 0.99,
                      f"K1 {o.name}: UV frame u.right {du @ right:.3f} v.up {dv[2]:.3f}")
            else:
                check(abs(np.cross(du, dv) @ nrm - 1.0) < 0.01, f"K1 {o.name}: floor label mirrored")
            uvs = np.array([list(q) for q in Q])
            check(uvs.min() > -1e-6 and uvs.max() < 1 + 1e-6, f"K1 {o.name}: UVs outside 0..1")
    # LOD keep set = mast / arm, bridge grating + beams, ladder rails
    lod = LD.lod_split(all_parts)
    kept = set(lod["lod1_keep"])
    want_keep = {n for n in seen if n.startswith(("flood_", "grate_", "bridgebeam_", "lrail_", "ladder_"))}
    check(kept == want_keep, f"K1 LOD1 keep set {sorted(kept ^ want_keep)}")
    check(not any(n.startswith(("lamp_", "label_", "board_", "maze_")) for n in kept),
          "K1 LOD1 keep holds a lamp / label / board / maze part")
    k1_ladder_compat()
    k1_gear_equality()
    print(f"K1 builders: {len(CASES)} cases, {len(seen)} parts")


def _world_verts(parts):
    bpy.context.view_layer.update()
    return [tuple(o.matrix_world @ v.co) for o in parts for v in o.data.vertices]


def k1_gear_equality():
    """eleventh look: "the skis outside the tram building look totally different from
    the ones on the A frame - the A-frame ones way preferred" and ninth look: no bindings.
    ONE ski-gear part project-wide: the items of LD.ski_rack ARE cs2_bldg.ski_gear's (same
    vertices, same materials / accent slot, no binding blocks, nothing of the old slabs)."""
    from mathutils.kdtree import KDTree
    import cs2_bldg as KB
    clear_scene()
    cases = (("b", 10.2, 13.2, -6.60, 0.30, 7, 2, -1.0),        # the DQ base's rack
             ("t", 0.0, 3.0, 0.0, 0.02, 7, 2, -1.0),            # the tram base's rack
             ("w", 1.0, 3.2, 2.0, 0.50, 4, 1, 1.0))             # other side / count / height
    for name, x0, x1, y, z0, pairs, boards, side in cases:
        parts = LD.ski_rack(name, x0, x1, y, z0, pairs=pairs, boards=boards, side=side)
        gear = [(o, m) for o, m in parts if o.name.startswith(f"ski_{name}_")]
        frame = [(o, m) for o, m in parts if o.name.startswith(f"skirack_{name}_")]
        check(len(gear) + len(frame) == len(parts) and len(frame) == 6,
              f"K1 gear {name}: {len(frame)} frame + {len(gear)} gear parts of {len(parts)}")
        check(all(m != "galv" for _o, m in gear), f"K1 gear {name}: a galv (binding) gear part")
        check({m for _o, m in gear} <= set(KB.GEAR_CODE) - {"galv"},
              f"K1 gear {name}: matkeys {sorted({m for _o, m in gear})} are not the kit's")
        check(all(o.name == f"ski_{name}_{KB.GEAR_CODE[m]}" for o, m in gear),
              f"K1 gear {name}: part names {[o.name for o, _m in gear]}")
        # expected: the SAME items through cs2_bldg.ski_gear (foot / lean from the documented rule)
        sg = -1.0 if side < 0 else 1.0
        n_items = pairs + boards
        pitch = (x1 - x0 - 0.30) / n_items
        items = []
        for i in range(n_items):
            kd = "board" if i >= pairs else "ski"
            col, ln = LD.gear_pick(name, f"sb{i - pairs}" if kd == "board" else f"{i}", kd)
            x = x0 + 0.15 + (i + 0.5) * pitch
            items.append((kd, (x, y + sg * 0.40, max(z0, KB.GROUND_LIFT) + 0.015),
                          (x, y + sg * 0.02, z0 + 1.30 + 0.02), col, ln))
        ref = KB.ski_gear(f"eq_{name}", items)
        by_ref = {m: o for o, m in ref}
        check(set(by_ref) == {m for _o, m in gear}, f"K1 gear {name}: material set "
              f"{sorted(by_ref)} vs {sorted(m for _o, m in gear)}")
        worst = 0.0
        for o, m in gear:
            ro = by_ref[m]
            check(len(o.data.polygons) == len(ro.data.polygons)
                  and len(o.data.vertices) == len(ro.data.vertices),
                  f"K1 gear {name}/{m}: {len(o.data.polygons)} faces / {len(o.data.vertices)} verts "
                  f"vs the kit's {len(ro.data.polygons)} / {len(ro.data.vertices)}")
            mine, theirs = _world_verts([o]), _world_verts([ro])
            kd_ = KDTree(len(mine))
            for i, p in enumerate(mine):
                kd_.insert(p, i)
            kd_.balance()
            worst = max(worst, max(kd_.find(p)[2] for p in theirs))
        check(worst < 1e-4, f"K1 gear {name}: vertices differ from cs2_bldg.ski_gear by {worst}")
        # no binding: the same items with bindings=True are strictly heavier
        for o, _m in ref:
            bpy.data.objects.remove(o, do_unlink=True)
        heavy = KB.ski_gear(f"eqb_{name}", items, bindings=True)
        n_off = sum(len(o.data.polygons) for o, _m in gear)
        n_on = sum(len(o.data.polygons) for o, _m in heavy)
        check(n_on - n_off == pairs * 10 + boards * 20,
              f"K1 gear {name}: bindings=True adds {n_on - n_off} faces, expected "
              f"{pairs * 10 + boards * 20} (none are drawn by default)")
        for o, _m in heavy:
            bpy.data.objects.remove(o, do_unlink=True)
        for o, _m in parts:
            bpy.data.objects.remove(o, do_unlink=True)
    # materials: the dress keys ARE the building kit's look (accent = the slot-2 recolor)
    for k in ("accent", "trim_white", "ski_black", "ski_blue", "ski_red"):
        bm_, dm = KB.BLDG_MATERIALS[k], LD.DRESS_MATERIALS[k]
        alb = KB.srgb_to_linear(bm_.target_srgb)
        check(all(abs(a - b) < 1e-4 for a, b in zip(alb, dm["albedo"])),
              f"K1 gear material {k}: albedo {tuple(dm['albedo'])} vs the kit's {tuple(alb)}")
        check(abs(dm["rough"] - (1.0 - bm_.smooth)) < 1e-6 and dm["metal"] == 0.0
              and dm.get("slot", 0) == bm_.slot and bool(dm.get("recolor_albedo")) == bool(bm_.recolor),
              f"K1 gear material {k}: rough / metal / slot {dm} vs {bm_}")
    check(LD.DRESS_MATERIALS["accent"]["slot"] == 2, "K1 gear: accent is the slot-2 recolor")
    check(not {"ski_a", "ski_b", "ski_c"} & set(LD.DRESS_MATERIALS), "K1 gear: the old ski_a/b/c remain")
    # a rack whose pitch cannot hold two neighbors across is refused
    try:
        LD.ski_rack("tight", 0.0, 1.6, 0.0, 0.30, pairs=3, boards=3)
        check(False, "K1 gear: a 0.17 m pitch next to a board did not raise")
    except ValueError:
        check(True, "")
    clear_scene()
    print("K1 gear equality: LD.ski_rack's items == cs2_bldg.ski_gear's on 3 racks (vertices < 1e-4, "
          "materials, accent slot 2), no bindings, 6 frame parts unchanged")


def k1_ladder_compat():
    import lift_terminal_detach as D
    import tram_station as T
    def verts_by_name(parts):
        bpy.context.view_layer.update()
        out = {o.name: np.array(sorted(tuple(round(c, 5) for c in (o.matrix_world @ v.co))
                                       for v in o.data.vertices)) for o, _m in parts}
        clear_scene()
        return out
    clear_scene()
    ref = verts_by_name(D._ladder("pfq", -1.15, 0.0, 0.30, 3.30))   # rungs z0 + 0.30 i
    n_r = int((3.30 - 0.30 - 0.15) / 0.30)
    mine = verts_by_name(LD.fixed_ladder("pfq", (-1.15, 0.0), (1.0, 0.0),
                                         [0.30 + 0.30 * i for i in range(1, n_r + 1)],
                                         (0.30, 3.30), width=0.56))
    check(sorted(ref) == sorted(mine), f"K1 dq ladder names {sorted(ref)[:3]}.. vs {sorted(mine)[:3]}..")
    worst = 0.0
    for nm, A in ref.items():
        B = mine.get(nm)
        if B is None or A.shape != B.shape:
            worst = 9.0
            break
        worst = max(worst, float(np.abs(A - B).max()))
    check(worst < 1e-4, f"K1 dq ladder: vertices differ from lift_terminal_detach._ladder by {worst}")
    # tram style through C15's own parser (prefab frame = (y, z, x) of the authoring point)
    clear_scene()
    parts = LD.fixed_ladder("pit", LD.tram_point(-10.00, 0.0, -8.62)[:2],
                            LD.tram_point(0.0, 0.0, -1.0)[:2],
                            [3.90 + 0.30 * k for k in range(9)], (3.62, 7.40), style="tram")
    pf, pt = [], {}
    for o, _m in parts:
        ids = []
        for t in world_tris(o):
            for v in t:
                pf.append((v.y, v.z, v.x))
                ids.append(len(pf) - 1)
        pt[o.name] = np.array(ids, dtype=np.int64).reshape(-1, 3)
    lad = T.walk_ladders(np.array(pf, dtype=np.float64), pt)
    L = lad.get("pit")
    check(L is not None, "K1 tram ladder: C15 walk_ladders does not see 'pit'")
    if L:
        check(L["rungs"] == 9, f"K1 tram ladder: {L['rungs']} rungs")
        check(abs(L["pitch"] - 0.30) < 1e-3, f"K1 tram ladder: pitch {L['pitch']}")
        check(abs(L["foot_y"] - 3.62) < 1e-3, f"K1 tram ladder: foot {L['foot_y']}")
        # walk_ladders takes the rung's bbox center: a 3-seg rung's bbox sits r / 4 above its
        # axis (the shipped pcat ladder has the same bias), hence the 6 mm tolerance
        check(abs(L["first"] - 0.28) < 0.006, f"K1 tram ladder: first rung {L['first']}")
        check(abs(L["plane"] - (-8.62)) < 1e-3, f"K1 tram ladder: rung plane {L['plane']}")


# ======================================================================================
# K2 label sheet
# ======================================================================================

def k2_sheet():
    region = (806, 1648, 2042, 2040)
    base_items = {k: LD.board_px(k) for k in ("lowerbar", "tipsup", "waithere", "seat1", "seat2",
                                              "seat3", "seat4", "ops", "slow")}
    top_items = {k: LD.board_px(k) for k in ("raisebar", "unload", "stay", "tension", "ops", "slow")}
    for tag, items in (("base", base_items), ("top", top_items)):
        a = LD.pack_label_sheet(items, region)
        b = LD.pack_label_sheet(dict(reversed(list(items.items()))), region)
        check(a == b, f"K2 {tag}: pack depends on the item order")
        rects = list(a.items())
        for nm, (x0, y0, x1, y1) in rects:
            check(x1 - x0 == items[nm][0] and y1 - y0 == items[nm][1], f"K2 {tag} {nm}: size changed")
            check(x0 >= region[0] and y0 >= region[1] and x1 <= region[2] and y1 <= region[3],
                  f"K2 {tag} {nm}: outside the region")
        for i, (na, ra) in enumerate(rects):
            for nb, rb in rects[i + 1:]:
                gx = max(rb[0] - ra[2], ra[0] - rb[2])
                gy = max(rb[1] - ra[3], ra[1] - rb[3])
                check(max(gx, gy) >= 6, f"K2 {tag}: {na} / {nb} gutter {max(gx, gy)} < 6")
        uv = LD.px_rect_to_uv(a["ops"], 2048)
        check(LD.uv_rect_to_px(uv, 2048) == (a["ops"][1], a["ops"][3], a["ops"][0], a["ops"][2]),
              f"K2 {tag}: px <-> uv round trip")
        print(f"K2 sheet {tag}: {len(a)} labels, {a}")
    for bad in (lambda: LD.pack_label_sheet({"a": (400, 300), "b": (400, 300)}, (0, 0, 500, 500)),
                lambda: LD.pack_label_sheet({"a": (600, 10)}, (0, 0, 500, 500))):
        try:
            bad()
            check(False, "K2 overflow / too wide did not raise")
        except ValueError:
            check(True, "")


# ======================================================================================
# K3 painters
# ======================================================================================

DENS = {"dep": 200.0, "tickets": 240.0}


def _paint(kind_, ppm=None, size=None, words=None, rect=None):
    ppm = ppm or DENS.get(kind_, 400.0)
    if rect is None:
        w, h = LD.board_px(kind_, ppm, size)
        rect = (0, h, 0, w)
    h, w = rect[1] - rect[0], rect[3] - rect[2]
    base = np.zeros((h, w, 4), np.float32)
    ctl = np.ones((h, w, 4), np.float32)
    met = LD.paint_board(kind_, rect, None, base, ctl, size_m=size, words=words)
    return base, ctl, met


def k3_painters():
    import lift_terminal as LT
    import lift_terminal_detach as LTD
    import tram_station as TS
    shipped = set()                               # every kind a station's label table paints
    for tbl in (LTD.D1_LABELS["base"], LTD.D1_LABELS["top"], LTD.GD_LABELS, LT.M_LABELS["drive"],
                LT.M_LABELS["return"]):
        shipped |= set(tbl.values())
    shipped |= {v[0] for v in TS.LABEL_KINDS.values() if not v[0].startswith("trail:")}
    for k in LD.BOARDS:
        base, ctl, met = _paint(k)
        spec = LD.BOARDS[k]
        for key in ("cap_m", "stroke_px", "glyph_m", "rect", "t1_ok", "t2_ok", "px_per_m", "read_m"):
            check(key in met, f"K3 {k}: .last lacks {key}")
        check(LD.paint_board.last is met, f"K3 {k}: paint_board.last not set")
        check(float(np.abs(ctl[..., 0:3]).max()) == 0.0, f"K3 {k}: ControlMask not zero")
        check(float(base[..., 3].min()) == 1.0, f"K3 {k}: alpha not 1")
        check(met["l1_ppm_ok"], f"K3 {k}: L1 density")
        if spec.get("vocab"):
            # the reference vocabulary (ref-signs): drawn, carried by NO station, fine print declared
            check(k not in shipped, f"K3 {k}: a vocabulary kind is on a station's label table")
            check(bool(met.get("fineprint")) == bool(spec.get("fineprint")), f"K3 {k}: fineprint flag")
        elif k != "tension":
            check(met["t2_ok"], f"K3 {k}: T2 stroke {met['stroke_px']} / cap {met['cap_m']} / picto "
                                f"{met.get('picto_stroke_px')}")
        if spec["layout"] == "plate":
            # 2026-10-12 redraw: ONE real-size portrait placard on the backer, T2 words, >= 4-texel pictogram
            # strokes. The T1 50 m pictogram is NOT claimed any more (read_m: design L3's basis, >= 25 m)
            pw, ph = met["placard_m"]
            check(abs(ph / pw - LD.PLACARD_RATIO) < 1e-3 and ph <= spec["size"][1] and met["t2_ok"]
                  and met["picto_stroke_px"] >= LD.STROKE_T2_PX and met["read_m"] >= 25.0 and not met["t1_ok"],
                  f"K3 {k}: placard {pw} x {ph}, T2 {met['t2_ok']}, picto {met['picto_stroke_px']} px, read "
                  f"{met['read_m']} m, T1 {met['t1_ok']}")
        if k in ("slow", "stn", "dep"):
            check(met["t1_ok"], f"K3 {k}: T1 kept (cap {met['cap_m']}, glyph {met['glyph_m']})")
        base2, _c, _m = _paint(k)
        check(np.array_equal(base, base2), f"K3 {k}: not deterministic")
        print(f"K3 {k}: cap {met['cap_m']} stroke {met['stroke_px']} px picto {met.get('picto_stroke_px')} px "
              f"glyph {met['glyph_m']} m read {met['read_m']} m T1 {met['t1_ok']} T2 {met['t2_ok']}"
              f"{' fineprint' if met.get('fineprint') else ''}")
    _b, _c, met = _paint("dep")
    check(met["dot_px"] >= 4.0 - 1e-6, f"K3 dep: dots {met['dot_px']} px")
    check(met["dots_lit"] > 100, f"K3 dep: {met['dots_lit']} lit dots")
    # orientation: "IW" -> the left half holds the thin I, "L" -> its foot at the bottom rows (inside the
    # border line and the bolts: the words' box only)
    field = np.array(LD.INK["orange"], np.float32)

    def ink_box(words):
        base, _c, _m = _paint("slow", words=words)
        h, w = base.shape[:2]
        mi, mj = int(0.045 * h / 0.35), int(0.075 * w / 1.20)
        return np.abs(base[mi:h - mi, mj:w - mj, 0:3] - field).sum(axis=-1) > 0.3
    ink = ink_box(("IW",))
    col_ink = ink.any(axis=0)
    runs, start = [], None                       # contiguous inked column runs, left to right
    for j, v in enumerate(list(col_ink) + [False]):
        if v and start is None:
            start = j
        elif not v and start is not None:
            runs.append(j - start)
            start = None
    check(len(runs) >= 2 and runs[0] * 3 < runs[-1],
          f"K3 'IW': column runs {runs} - the thin I must come first (mirrored?)")
    ink = ink_box(("L",))
    rows = np.nonzero(ink.any(axis=1))[0]
    widths = ink.sum(axis=1)
    widest = int(np.argmax(widths))
    check(widest - rows.min() < 0.25 * (rows.max() - rows.min()),
          f"K3 'L': widest row {widest} not at the bottom ({rows.min()}..{rows.max()}) - flipped?")
    # a circle pictogram stays round in METERS at anisotropic px/m (220 x 200 px over 0.55 x 0.55 m)
    pu, pv = 220 / 0.55, 200 / 0.55
    col = np.empty((200, 220, 3), np.float32)
    col[...] = np.asarray(LD.INK["yellow"], np.float32)
    cv = LD._Canvas(col, 0.55, 0.55, pu, pv)
    cv.circle("unload", 0.275, 0.275, 0.40, LD.BOARDS["unload"], "yellow", "black")
    blk = np.abs(col - np.asarray(LD.INK["black"], np.float32)).sum(axis=-1) < 0.05
    r_, c_ = np.nonzero(blk)
    wm, hm = (c_.max() - c_.min() + 1) / pu, (r_.max() - r_.min() + 1) / pv
    check(abs(wm / hm - 1.0) < 0.04 and abs(wm - 0.40) < 0.01, f"K3 picto ring in meters {wm:.3f} x {hm:.3f}")
    # the stamp_on_face painter wrapper
    p = LD.board_painter("seat2")
    b = np.zeros((100, 100, 4), np.float32)
    c = np.ones((100, 100, 4), np.float32)
    p((0, 100, 0, 100), b, c)
    check(p.last is not None and p.last["kind"] == "seat2", "K3 board_painter .last")
    try:
        LD.paint_board("lowerbar", (0, 220, 0, 280), 300.0, np.zeros((220, 280, 4), np.float32),
                       np.zeros((220, 280, 4), np.float32))
        check(False, "K3 a wrong ppm did not raise")
    except ValueError:
        check(True, "")


def _seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = 0.0 if dx == dy == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


def k9_glyphs():
    """K9 (2026-10-12 lift-board redraw, "the guy is getting his HEAD SLAMMED BY THE BAR ... the SKIS LOOK LIKE
    THEY HAVE BLOCKS ON THEM"). The shared glyph primitives satisfy these rules:
    - the rider's head stays clear of the bar in BOTH states
    - the lowered cross-bar sits at CHEST height in front of the torso
    - the raised bar sits above the head
    - the ski is ONE thin line whose front tip curves UP (no rects / fills under the rider)
    - every pictogram stroke is >= 12 canvas units (4 texels in the smallest shipped circle at 400 px/m)"""
    hx, hy = LD.BAR_HIP
    head = LD.g_seated(hx, hy)[0]
    torso = LD.g_seated(hx, hy)[1]
    _k, cx, cy, r = head
    for state in ("down", "up"):
        (_l, pts, w), = LD.g_bar(hx, hy, state)
        gap = min(_seg_dist(cx, cy, *pts[i], *pts[i + 1]) for i in range(len(pts) - 1)) - r - w / 2.0
        check(gap >= 6.0, f"K9 bar '{state}' clears the head by {gap:.1f} canvas units (>= 6)")
        print(f"K9 bar '{state}': head clearance {gap:.1f} canvas units")
        if state == "down":
            ex, ey = pts[-1]
            (tx0, ty0), (tx1, ty1) = torso[1]
            front = max(tx0, tx1) + torso[2] / 2.0
            check(min(ty0, ty1) <= ey <= max(ty0, ty1) and 0.0 < ex - w / 2.0 - front < 14.0,
                  f"K9 lowered cross-bar at CHEST height in front of the torso (end ({ex:.0f}, {ey:.0f}), torso "
                  f"front x {front:.0f}, shoulder..hip y {min(ty0, ty1):.0f}..{max(ty0, ty1):.0f})")
        else:
            low = max(y for _x, y in pts) + w / 2.0
            check(low < cy - r, f"K9 raised bar entirely above the head top (lowest {low:.0f} < {cy - r:.0f})")
    ski = LD.g_ski(0.0, 100.0, 200.0)
    (_l, sp, sw), = ski
    check(len(ski) == 1 and sw >= 12.0 and sp[0][1] == sp[1][1] == 200.0 and sp[-1][1] <= 200.0 - 0.9 * 20.0
          and sp[-1][0] > sp[1][0], f"K9 ski = one {sw}-unit line, flat, then its front tip {200.0 - sp[-1][1]:.0f} "
                                    f"units UP")
    hx2, hy2 = LD.TIPS_HIP
    low = []
    for p in LD.PICTOS["tipsup"][0][1]:
        if p[0] == "fill":
            ys = [q[1] for q in p[1]]
        elif p[0] == "rect":
            ys = [p[2], p[4]]
        else:
            continue
        if min(ys) > hy2:
            low.append(p[0])
    check(not low, f"K9 KEEP TIPS UP: no rect / fill below the hip (blocks on the skis): {low}")
    thin = [(name, p[2]) for name, layers in LD.PICTOS.items() if name != "tram" for _k2, prims in layers
            for p in prims if p[0] == "line" and p[2] < 12.0]
    check(not thin, f"K9 every pictogram stroke >= 12 canvas units: {thin}")
    check(LD.g_question()[0][2] >= 12.0, "K9 question mark stroke")
    k9_arrows_hoop()


def k9_arrows_hoop(min_gap=9.0, min_hoop=18.0):
    """K9 (2026-10-12 nit fix, lift-boards-redraw-check item 1): every arrow clears the ring's inner edge and the
    figure. The LOWER BAR hoop is loose. Distances are measured on a 0.25-unit raster of
    ski_launcher_glyph.sd_prim (gap(A, B) = min over the raster of sdA + sdB). Negatives: the pre-fix arrow and
    hoop fail."""
    import ski_launcher_glyph as slg
    xs = np.linspace(0.0, 256.0, 1025)
    X, Y = np.meshgrid(xs, xs)
    R = np.hypot(X - 128.0, Y - 128.0)
    ring = max(LD.RING_FRAC * 0.225, LD.STROKE_T2_PX * 1.1 / 400.0)       # the shipped plate circle
    r_in = 128.0 - ring / 0.225 * 256.0 - 0.5

    def sd(prims):
        d = np.full(X.shape, np.inf)
        for q in prims:
            d = np.minimum(d, slg.sd_prim(q, X, Y))
        return d

    def gaps(prims):
        i = next(k for k, q in enumerate(prims) if q[0] == "fill" and len(q[1]) == 3)
        arrow, rest = prims[i:i + 2], prims[:i]
        da = sd(arrow)
        return float(np.min(r_in - R[da <= 0.0])), float(np.min(da + sd(rest)))

    for kind in ("lowerbar", "raisebar", "tipsup"):
        rg, fg = gaps(LD.PICTOS[kind][0][1])
        check(rg >= min_gap and fg >= min_gap, f"K9 {kind}: arrow clears the ring by {rg:.1f} and the figure by "
                                               f"{fg:.1f} canvas units (>= {min_gap})")
        print(f"K9 {kind}: arrow-ring {rg:.1f}, arrow-figure {fg:.1f} canvas units")
    hx, hy = LD.BAR_HIP
    (_k, cx, cy, r) = LD.g_seated(hx, hy)[0]
    (_l, pts, w), = LD.g_bar(hx, hy, "down")
    over = cy - r - (min(p[1] for p in pts) + w / 2.0)
    ahead = max(p[0] for p in pts) - w / 2.0 - (cx + r)
    check(over >= min_hoop and ahead >= min_hoop, f"K9 LOWER BAR hoop: {over:.1f} over the head top, {ahead:.1f} "
                                                  f"ahead of the face (>= {min_hoop})")
    # negatives: the pre-fix glyphs
    old_lower = LD.PICTOS["lowerbar"][0][1][:-2] + LD.g_arrow(212.0, 102.0, 212.0, 186.0)
    old_tips = LD.PICTOS["tipsup"][0][1][:-2] + LD.g_arrow(214.0, 178.0, 214.0, 96.0)
    check(gaps(old_lower)[0] < min_gap and gaps(old_tips)[0] < min_gap,
          "K9 negative: the pre-fix arrows (x 212 / 214, head 30) fuse with the ring")
    px, py, rb, xa = hx - 26.0, hy - 96.0, 18.0, hx + 30.0
    old = [(px, py)] + LD.g_arc(xa, py + rb, rb, -90.0, 0.0, 8) + [(xa + rb, hy - 26.0)]
    check(cy - r - (min(p[1] for p in old) + 6.0) < min_hoop, "K9 negative: the pre-fix hoop (12 over the head) fails")


# ======================================================================================
# K4 share_identical, K5 share=None identity (shared fixture)
# ======================================================================================

def build_fixture(with_relief_copies=True):
    """Joined fixture: maze (translation copies), a rotated post copy, a mirrored tool copy,
    two identical boxes of different materials, big whitelisted / unlisted boxes, gratings,
    labels. Returns (obj, table, parts_keys)."""
    import cs2_kit
    clear_scene()
    parts = []
    parts += LD.maze("m", [[(0.0, 0.0), (4.2, 0.0)], [(0.0, 2.0), (4.2, 2.0)]], 0.0)
    rot = cs2_kit.add_cyl("maze_post_rot", 0.03, 1.0, (8.0, 0.0, 0.5), rot=(0.0, 0.0, math.radians(45)),
                          segments=4, caps="top")
    parts.append((rot, "galv"))
    parts += LD.tools("ta", (10.0, 0.0), "+X", 0.0)
    tb = LD.tools("tb", (12.0, 0.0), "+X", 0.0)
    for o, _m in tb:
        o.scale = (-1.0, 1.0, 1.0)
    parts += tb
    parts.append((cs2_kit.add_box("cube_galv", (0.3, 0.3, 0.3), (14.0, 0.0, 0.15)), "galv"))
    parts.append((cs2_kit.add_box("cube_red", (0.3, 0.3, 0.3), (15.0, 0.0, 0.15)), "fire_red"))
    for k in range(2):
        parts.append((cs2_kit.add_box(f"beam_big{k}", (4.0, 0.4, 0.4), (20.0, 3.0 * k, 0.2)), "galv"))
        parts.append((cs2_kit.add_box(f"slab_big{k}", (4.0, 2.0, 0.2), (28.0, 3.0 * k, 0.1)), "galv"))
        parts += [(o, "galv") for o, _q in cs2_kit.grating(f"g{k}", 1.0, 1.0, 0.06,
                                                           (34.0 + 2.0 * k, 0.0, 0.03))]
        parts += LD.board(f"lb{k}", (40.0 + 2.0 * k, 0.0, 1.0), "+X", 0.5, 0.3)
    for o, _m in parts:                    # the generators' route: strip, then a seed layer
        me = o.data
        while me.uv_layers:
            me.uv_layers.remove(me.uv_layers[0])
        lay = me.uv_layers.new(name="UVMap")
        import bmesh as _bm
        bm = _bm.new()
        bm.from_mesh(me)
        cs2_kit._box_project_uv(bm, bm.loops.layers.uv["UVMap"])
        bm.to_mesh(me)
        bm.free()
        del lay
    cs2_uv.set_density(parts, {"maze_": 0.5})
    table = cs2_asset.tag_parts(parts)
    keys = LD.part_keys(parts)
    mats = {k: bpy.data.materials.new("CS2SRC_" + k) for k in keys}
    for o, m in parts:
        me = o.data
        me.materials.clear()
        for k in keys:
            me.materials.append(mats[k])
        me.polygons.foreach_set("material_index", [keys.index(str(m))] * len(me.polygons))
    bpy.ops.object.select_all(action="DESELECT")
    for o, _m in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    t = bpy.context.active_object
    t.name = "LiftDressFixture"
    t.data.name = "LiftDressFixture"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    cs2_asset.store_part_table(t, table)
    t.data.uv_layers.active = t.data.uv_layers["UVMap"]
    return t, table, keys


def uv_hash(t):
    uv = cs2_uv.loop_uvs(t.data.uv_layers[cs2_uv.UNIQUE_LAYER])
    return hashlib.sha1(np.ascontiguousarray(uv.astype(np.float32)).tobytes()).hexdigest()[:16]


def k4_share():
    import cs2_compose as C
    t, table, _keys = build_fixture()
    specs = {"grate_": C.DetailSpec(grate_m=0.10), "beam_": C.DetailSpec(seams_m=1.2)}
    share = LD.share_identical(t, table, ("beam_",), detail_specs=specs)
    groups = dict(share.report["group_names"])
    canon_of = {}
    for c, ds in share.report["group_names"]:
        for d in ds:
            canon_of[d] = c
    check(canon_of.get("maze_post_m_1") == "maze_post_m_0" and "maze_post_m_7" in canon_of,
          f"K4 maze posts not grouped: {sorted(canon_of)[:6]}")
    check(any(n.startswith("maze_rope_m_1_") for n in canon_of), "K4 ropes of run 1 not stacked on run 0")
    check(any(n.startswith("maze_flag_m_") for n in canon_of), "K4 flags not shared")
    check("maze_post_rot" not in canon_of and "maze_post_rot" not in groups, "K4 rotated copy shared")
    check("tool_tb_scraper" not in canon_of and "tool_tb_shovel" not in canon_of, "K4 mirrored copy shared")
    check("cube_red" not in canon_of and "cube_galv" not in canon_of, "K4 different materials shared")
    check("slab_big1" not in canon_of, "K4 a big unlisted part shared (cap_m2)")
    check("beam_big1" not in canon_of, "K4 an active-relief prefix (beam_) shared (MC6)")
    check(not any(n.startswith("grate_") for n in canon_of), "K4 grate_ (active relief) shared")
    check(not any(n.startswith(("label_", "board_")) for n in canon_of), "K4 never-list shared")
    check(share.report["excluded"]["relief"] >= 4, f"K4 relief exclusions {share.report['excluded']}")
    # inactive spec for beam_ -> the whitelisted big beams ARE shared
    share2 = LD.share_identical(t, table, ("beam_",), detail_specs={"beam_": C.DetailSpec()})
    names2 = {d for _c, ds in share2.report["group_names"] for d in ds}
    check("beam_big1" in names2, "K4 whitelisted beam_ (inactive relief) not shared")
    share3 = LD.share_identical(t, table, ("beam_",), detail_specs=specs)
    check(list(share) == list(share3), "K4 share_identical not deterministic")
    # ground_band: the lower ropes (lowest point 0.54) stack only with lower ropes
    shg = LD.share_identical(t, table, ("beam_",), detail_specs=specs, ground_band=(0.0, 0.70))
    mixed = [c for c, ds in shg.report["group_names"] if c.startswith("maze_rope_")
             and len({n.rsplit("_", 1)[1] for n in [c] + ds}) > 1]
    check(not mixed and shg.report["groups"] == share.report["groups"] + 1,
          f"K4 ground_band: groups {shg.report['groups']} (vs {share.report['groups']}), mixed {mixed}")
    # pinned faces excluded (a reserved rect over the whole seed space of the maze posts)
    me = t.data
    # pack with sharing
    n0 = len(cs2_uv.uv_islands(me, me.uv_layers["UVMap"]))
    rep = cs2_uv.pack_unique(t, 512, [], margin_px=4, share=share)
    check(rep.get("shared_faces") == share.report["dup_faces"],
          f"K4 shared_faces {rep.get('shared_faces')} vs {share.report['dup_faces']}")
    uv = cs2_uv.loop_uvs(me.uv_layers[cs2_uv.UNIQUE_LAYER])
    start, total = cs2_uv.face_loop_table(me)
    worst = 0.0
    for canon, dups in share:
        for dp in dups:
            for fc, fd in zip(canon, dp):
                a = uv[start[fc]:start[fc] + total[fc]]
                b = uv[start[fd]:start[fd] + total[fd]]
                worst = max(worst, float(np.abs(a - b).max()))
    check(worst == 0.0, f"K4 dup loops not on their canonical UVs (max {worst})")
    check(uv.min() >= -1e-6 and uv.max() <= 1 + 1e-6, "K4 UVs outside 0..1")
    print(f"K4 share: {share.report['groups']} groups, {share.report['dup_parts']} dup parts, "
          f"{share.report['dup_faces']} dup faces; seed islands {n0}, packed islands {rep['islands']}")
    # the same fixture without sharing packs more islands (each copy its own)
    t2, _tb, _k = build_fixture()
    rep0 = cs2_uv.pack_unique(t2, 512, [], margin_px=4)
    check(rep0["islands"] == rep["islands"] and rep0["px_per_m"]["median"] < rep["px_per_m"]["median"],
          f"K4 sharing gave no density gain: median {rep0['px_per_m']['median']:.1f} -> "
          f"{rep['px_per_m']['median']:.1f}")
    print(f"K4 gain: median px/m {rep0['px_per_m']['median']:.1f} -> {rep['px_per_m']['median']:.1f}")
    # malformed lists raise
    t3, table3, _k = build_fixture()
    good = LD.share_identical(t3, table3, ())
    c0, d0 = good[0]
    for bad in ([(c0, [d0[0][:-1]])], [(c0, [c0])]):
        try:
            t3.data.uv_layers.active = t3.data.uv_layers["UVMap"]
            cs2_uv.pack_unique(t3, 256, [], margin_px=4, share=bad)
            check(False, "K4 a malformed share list did not raise")
        except ValueError:
            check(True, "")
    # pinned faces are never shared: reserve the whole UV square of the seed layer
    t4, table4, _k = build_fixture()
    sh4 = LD.share_identical(t4, table4, (), reserved=[(-100.0, -100.0, 100.0, 100.0)],
                             uv_layer="UVMap")
    check(len(sh4) == 0 and sh4.report["excluded"]["pinned"] > 0, "K4 pinned parts shared")


def fixture_bake_hashes(out_dir):
    import cs2_bake
    t, _table, keys = build_fixture()
    flat = {k: dict(albedo=(0.5, 0.5, 0.5), metal=0.0, rough=0.6) for k in keys}
    flat["fire_red"] = dict(albedo=(0.7, 0.08, 0.06), metal=0.0, rough=0.4, slot=1)
    sources = {i: cs2_bake.as_source_set(dict(base=flat[k]["albedo"], rough=flat[k]["rough"],
                                              slot=flat[k].get("slot", 0)), i)
               for i, k in enumerate(keys)}
    return t, sources


def bake_hashes(t, sources, out_dir, name, **kw):
    import cs2_bake
    rep = cs2_bake.bake_unique(t, out_dir, name, 256, sources, margin_px=2, ao_samples=0,
                               src_uv="UVMap", **kw)
    out = {}
    for suf in ("BaseColor", "Normal", "MaskMap", "ControlMask"):
        with open(os.path.join(out_dir, f"{name}_{suf}.png"), "rb") as fh:
            out[suf] = hashlib.sha1(fh.read()).hexdigest()[:16]
    return out, rep


def k5_identity(tmp):
    t, _tb, _k = build_fixture()
    cs2_uv.pack_unique(t, 512, [], margin_px=4)
    h0 = uv_hash(t)
    t.data.uv_layers.active = t.data.uv_layers["UVMap"]
    rep0 = cs2_uv.pack_unique(t, 512, [], margin_px=4)          # rebuilds UVUnique
    h0b = uv_hash(t)
    t2, _tb, _k = build_fixture()
    rep1 = cs2_uv.pack_unique(t2, 512, [], margin_px=4, share=None)
    h1 = uv_hash(t2)
    check(h0 == h0b == h1, f"K5 pack: no kwarg {h0} / again {h0b} / share=None {h1}")
    check("shared_faces" not in rep0 and "shared_faces" not in rep1, "K5 share=None report gained keys")
    check(h0 == GOLDEN_PACK, f"K5 pack {h0} != pre-pass golden {GOLDEN_PACK}")
    hs = {}
    for tag, kw in (("plain", {}), ("none", {"share": None}), ("empty", {"share": []})):
        tt, src = fixture_bake_hashes(tmp)
        hs[tag], rep = bake_hashes(tt, src, os.path.join(tmp, tag), "LiftDressFixtureA", **kw)
        if tag == "empty":
            check(rep.get("shared_faces") == 0, f"K5 share=[] report shared_faces {rep.get('shared_faces')}")
        else:
            check("shared_faces" not in rep, f"K5 {tag}: report gained shared_faces")
    check(hs["plain"] == hs["none"] == hs["empty"], f"K5 bake bytes differ: {hs}")
    if all(GOLDEN_BAKE.values()):
        check(hs["plain"] == GOLDEN_BAKE, f"K5 bake {hs['plain']} != pre-pass golden {GOLDEN_BAKE}")
    else:
        check(False, "K5 GOLDEN_BAKE not recorded")
    print(f"K5 identity: pack {h0}, bake {hs['plain']}")


# ======================================================================================
# K6 weathering helpers
# ======================================================================================

def k6_weather():
    import cs2_weather as CW
    clear_scene()
    me = bpy.data.meshes.new("K6")
    for k in ("galv", "label", "rubber"):
        me.materials.append(bpy.data.materials.new("CS2SRC_" + k))
    roles = LD.lift_mat_roles(me, LD.DQ_MAT_ROLES)
    check(roles == {0: "galv", 1: "none", 2: "pad"}, f"K6 lift_mat_roles {roles}")
    check(LD.lift_mat_roles(["concrete", "steel"], LD.TRAM_MAT_ROLES) == {0: "masonry", 1: "paint_steel"},
          "K6 lift_mat_roles by key list")
    me2 = bpy.data.meshes.new("K6b")
    me2.materials.append(bpy.data.materials.new("CS2SRC_unobtainium"))
    try:
        LD.lift_mat_roles(me2, LD.DQ_MAT_ROLES)
        check(False, "K6 unmapped key did not raise")
    except KeyError:
        check(True, "")
    me3 = bpy.data.meshes.new("K6c")
    me3.materials.append(bpy.data.materials.new("CS2BAKE_x"))
    try:
        LD.lift_mat_roles(me3, LD.DQ_MAT_ROLES)
        check(False, "K6 a non-placeholder slot did not raise")
    except ValueError:
        check(True, "")
    for k in LD.DRESS_MATERIALS:
        check(k in LD.DQ_MAT_ROLES and k in LD.TRAM_MAT_ROLES, f"K6 dress key {k} has no role")
        check(LD.DQ_MAT_ROLES.get(k) in CW.ROLES, f"K6 role of {k}")
    spec = LD.station_weather_spec("SkiDQBaseStationA", ground="grade", ground_z=0.30,
                                   traffic_extra=("deck", "grate_"),
                                   overrides=LD.DRESS_WEATHER_OVERRIDES)
    check(spec.kind == "station" and spec.intensity == 0.40 and spec.slot_wear == "free"
          and spec.snow == 0.0 and spec.edge_band_m == 0.08,
          f"K6 spec {spec.kind} {spec.intensity} {spec.slot_wear} {spec.snow} {spec.edge_band_m}")
    check("deck" in spec.traffic_prefixes and "landing_" in spec.traffic_prefixes, "K6 traffic prefixes")
    check(dict(spec.overrides)["label_"].role == "none", "K6 overrides -> PartWeather")
    for bad in ({"snow": 0.6}, {"edge_band_m": 0.06}):
        try:
            LD.station_weather_spec("X", ground="grade", **bad)
            check(False, f"K6 {bad} did not raise")
        except ValueError:
            check(True, "")
    up = LD.station_weather_spec("X", ground="plinth", intensity=0.5)
    check(up.intensity == 0.5, "K6 intensity raise")
    seq = {0.40: True, 0.45: True, 0.50: False}
    best, seen = LD.highest_clean_step(lambda s: {"vanilla": {"ok": seq.get(round(s, 2), False)}})
    check(best == 0.45 and seen == {0.40: True, 0.45: True, 0.50: False}, f"K6 ladder {best} {seen}")
    try:
        LD.highest_clean_step(lambda s: {"vanilla": {"ok": False}})
        check(False, "K6 a WARN at the first step did not raise")
    except RuntimeError:
        check(True, "")
    # materials agree with the generators' own tables
    import lift_terminal_detach as D
    import tram_station as T
    try:
        m = LD.merge_materials(D.MATERIALS, list(LD.DRESS_MATERIALS))
        check(set(LD.DRESS_MATERIALS) <= set(m), "K6 merge DQ")
        m2 = LD.merge_materials(T.material_table("top"), list(LD.DRESS_MATERIALS))
        check("rubber" in m2 and "darksteel" in m2, "K6 merge tram")
    except (ValueError, KeyError) as e:
        check(False, f"K6 merge_materials: {e}")
    # label_protect_px: the exact part's texel bbox
    t, table, _k = build_fixture()
    cs2_uv.pack_unique(t, 256, [], margin_px=4)
    pp = LD.label_protect_px(["label_lb0"])(t.data, 256)
    ref = cs2_compose.uv_bbox_px(t.data, table, "label_lb0", 256)
    check(len(pp) == 1 and tuple(pp[0]) == tuple(ref), f"K6 label_protect_px {pp} vs {ref}")


# ======================================================================================
# K7 vanilla sub-objects
# ======================================================================================

def k7_subobj():
    if os.path.isfile(LD.DUMP_ZIP):
        t0 = time.perf_counter()
        try:
            got = LD.verify_subobj()
            check(True, "")
            for pf, d in got.items():
                print(f"K7 {pf}: {LD.SUBOBJ[pf]['guid']} variants {len(d['variants'])}, consumers "
                      f"{d['consumers']}, tris <= {d['tris']}, bounds {d['bounds']}")
        except AssertionError as e:
            check(False, f"K7 {e}")
        print(f"K7 verify_subobj {time.perf_counter() - t0:.1f} s")
    else:
        print(f"K7 SKIP verify_subobj: no dump at {LD.DUMP_ZIP}")
    blk = LD.sub_object_rows("SkiDQBaseStationBuilding",
                             [("TrashbinRandom01", (4.05, 0.30, 13.75), (0, 0, 0, 1), 0),
                              {"prefab": "GardenBenchRandom01", "pos": (-6.6, 0.3, 8.0),
                               "rot": LD.facing_quat("+X")}])
    check(blk["frame"] == "game local" and len(blk["objects"]) == 2
          and blk["objects"][0]["guid"] == "UnityGUID:41e3c9ba6e1d6b948a6b1bc3499a5397",
          f"K7 sub_object_rows {blk}")
    try:
        LD.sub_object_rows("X", [("FireHydrant01", (0, 0, 0), (0, 0, 0, 1), 0)])
        check(False, "K7 an unverified prefab did not raise")
    except KeyError:
        check(True, "")
    try:
        LD.sub_object_rows("X", [("TrashbinRandom01", (0, 0, 0), (0.7071068, 0, 0, 0.7071068), 0)])
        check(False, "K7 a non-yaw rotation did not raise")
    except ValueError:
        check(True, "")
    lo, hi = LD.subobj_box("WarningLightRandom01", (9.58, 25.80, 5.30))
    check(abs(hi[1] - 27.754) < 0.002, f"K7 WarningLight v02 top {hi[1]:.3f} (27.75 expected)")
    lo, hi = LD.subobj_box("GardenBenchRandom01", (0, 0, 0), LD.facing_quat("+X"))
    check(abs((hi[0] - lo[0]) - 0.992) < 0.002 and abs((hi[2] - lo[2]) - 2.118) < 0.002,
          f"K7 bench yawed 90 deg: {hi - lo}")
    blo, bhi = LD.subobj_box("TrashbinRandom01", (4.05, 0.30, 13.75), frame="blender")
    check(abs(blo[0] - (13.75 - 0.246)) < 1e-3 and abs(blo[2] - 0.30) < 1e-3, f"K7 blender frame {blo}")
    # x-sign gate N (2026-10-15 playtest, S1b-N / T14 2026-10-16): game x = -authoring y, a +Y facing is game -X
    check(LD.facing_quat("+Y", frame="blender") == LD.FACING_QUAT["-X"]
          and LD.facing_quat("-Y", frame="blender") == LD.FACING_QUAT["+X"]
          and LD.facing_quat("+X", frame="blender") == LD.FACING_QUAT["+Z"], "K7 facing_quat frames (game x = -y)")
    check(LD.blender_to_game((1.0, 2.0, 3.0)) == (-2.0, 3.0, 1.0)
          and LD.game_to_blender(LD.blender_to_game((1.0, 2.0, 3.0))) == (1.0, 2.0, 3.0)
          and LD.tram_to_game((5.0, 6.0, 7.0)) == (-5.0, 6.0, 7.0)
          and LD.tram_to_game((5.0, 6.0, 7.0)) == LD.blender_to_game(LD.tram_point(5.0, 6.0, 7.0))
          and LD.rule_t_rot(LD.FACING_QUAT["+X"]) == LD.FACING_QUAT["-X"]
          and str(LD.blender_to_game((0.0, 0.0, 0.0))[0]) == "0.0", "K7 frames: gx = -y, tram design (X) -> game -X")
    ba, bb = LD.subobj_box("TrashbinRandom01", (-3.0, 0.30, 13.75), frame="blender")
    ga, gb = LD.subobj_box("TrashbinRandom01", (-3.0, 0.30, 13.75))
    check(abs(ba[1] + gb[0]) < 1e-9 and abs(bb[1] + ga[0]) < 1e-9 and ba[1] < bb[1],
          f"K7 blender box lo / hi swap on y (lo {ba[1]:.3f} hi {bb[1]:.3f} from game x {ga[0]:.3f}..{gb[0]:.3f})")
    # the cafe table set under the L1 umbrellas: a verified SUBOBJ placeholder, and it is the GUID
    # the L1 rows and the L2 hut place (a table with its two chairs: ActivityLocation seats)
    import bldg_recipes as BR
    tbl = LD.SUBOBJ["TableSetCommercialRandom01"]
    check(tbl["guid"] == "fc058a3d03b337e4794c432a84170316" and "ActivityLocation" in tbl["components"],
          f"K7 table set {tbl['guid']}")
    check(all(r[1] == tbl["guid"] and r[0] == "TableSetCommercialRandom01" for r in BR.L1_TABLES),
          "K7 the L1 table rows use the verified SUBOBJ GUID")
    lo, hi = LD.subobj_box("TableSetCommercialRandom01", (0, 0, 0))
    check(hi[1] < 2.18 and max(-lo[0], hi[0]) < 1.55,
          f"K7 table set bounds {lo} {hi}: taller than the canopy rim (2.18 m) or wider than r 1.55")


def _fine_hash(parts):
    """(12-hex sha1, tris) of a builder's parts: world vertices (1e-6), names, matkeys, faces."""
    hh = hashlib.sha1()
    n = 0
    for o, m in parts:
        me = o.data
        M = o.matrix_world
        co = np.array([tuple(M @ v.co) for v in me.vertices], np.float64).round(6)
        hh.update(o.name.encode())
        hh.update(str(m).encode())
        hh.update(co.tobytes())
        for p in me.polygons:
            hh.update((",".join(map(str, p.vertices)) + ";").encode())
        n += sum(len(p.vertices) - 2 for p in me.polygons)
    return hh.hexdigest()[:12], n


def _fine_drop(parts):
    for o, _m in parts:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(me)


#: fine-detail pass (design B.1): pre-pass default hashes (the snapshot engine, same calls) and
#: the fine=True tris of the three cs2_liftdress builders that gained `fine=`
LD_FINE_CALLS = {
    "ld_maze": lambda kw: LD.maze("fdk12", [[(0.0, 0.0), (3.0, 0.0), (3.0, 1.2)]], 0.0, **kw),
    "ld_swing_gate": lambda kw: LD.swing_gate("fdk13", (0.0, 2.0), (1.0, 2.0), 0.0, panel=True, **kw),
    "ld_board_post": lambda kw: LD.board("fdk14", (0.0, 5.0, 1.5), "+X", 0.7, 0.55, mount="post",
                                         post_z0=0.0, **kw),
    "ld_board_hang": lambda kw: LD.board("fdk15", (0.0, 8.0, 2.5), "+X", 0.7, 0.55, mount="hang",
                                         rods_to=3.2, **kw),
}
LD_FINE_PRE_PASS = {"ld_maze": ("fe3016a0b296", 134), "ld_swing_gate": ("9c4064138dee", 48),
                    "ld_board_post": ("c40d3a2c625a", 32), "ld_board_hang": ("7bfe5c167d45", 30)}
LD_FINE_TRIS = {"ld_maze": 226, "ld_swing_gate": 80, "ld_board_post": 50, "ld_board_hang": 38}


def k8_fine():
    """K8 fine-detail pass: defaults = the pre-pass engine, fine=True = the B.1 tris, same names / matkeys. The
    three fine-detail SUBOBJ placeholders + the rejected GUIDs."""
    for key, call in LD_FINE_CALLS.items():
        d = call({})
        hd = _fine_hash(d)
        nd = [(o.name, m) for o, m in d]
        _fine_drop(d)
        f = call({"fine": True})
        hf = _fine_hash(f)
        nf = [(o.name, m) for o, m in f]
        _fine_drop(f)
        check(hd == LD_FINE_PRE_PASS[key], f"K8 {key} default {hd} != pre-pass {LD_FINE_PRE_PASS[key]}")
        check(hf[1] == LD_FINE_TRIS[key] and hf[0] != hd[0], f"K8 {key} fine {hf}")
        check(nf == nd, f"K8 {key} part names / matkeys changed")
    for pf, g in (("BarrelRandom01", "fb4247f7a6f75b1478d9dd01ac88ce86"),
                  ("SafetyConeRandom01", "dc8abeea43367b24ba2668784a309e56"),
                  ("PotRandom01", "8d8722db083c3564291e3778cceeb1ae")):
        check(LD.SUBOBJ.get(pf, {}).get("guid") == g, f"K8 SUBOBJ {pf}")
    check(not any(s_["guid"] in LD.REJECTED_GUIDS for s_ in LD.SUBOBJ.values()), "K8 rejected GUID in SUBOBJ")


# ======================================================================================

def golden():
    tmp = tempfile.mkdtemp(prefix="liftdress_golden_")
    t, _tb, _k = build_fixture()
    cs2_uv.pack_unique(t, 512, [], margin_px=4)
    print(f"GOLDEN_PACK = {uv_hash(t)!r}")
    tt, src = fixture_bake_hashes(tmp)
    hs, _rep = bake_hashes(tt, src, os.path.join(tmp, "g"), "LiftDressFixtureA")
    print(f"GOLDEN_BAKE = {hs!r}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--golden" in argv:
        golden()
        sys.exit(0)
    t0 = time.perf_counter()
    tmp = tempfile.mkdtemp(prefix="liftdress_test_")
    for name, fn in (("K1", k1_builders), ("K2", k2_sheet), ("K3", k3_painters), ("K4", k4_share),
                     ("K5", lambda: k5_identity(tmp)), ("K6", k6_weather), ("K7", k7_subobj),
                     ("K8", k8_fine), ("K9", k9_glyphs)):
        try:
            fn()
        except Exception as e:  # noqa: BLE001
            traceback.print_exc()
            FAILS.append(f"{name}: {e!r}")
    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_liftdress: {len(FAILS)} failure(s), {OKS[0]} ok ({dt:.0f} s): {FAILS[:12]}")
        sys.exit(1)
    print(f"PASS test_liftdress: K1-K9 (builders + C14 kinds + ladder parsers, label sheet, painters "
          f"T1/T2, bay sharing, share=None byte-identical to the pre-pass modules, weathering "
          f"helpers, vanilla GUIDs, fine-detail builders, the lift-board redraw's glyphs) {OKS[0]} ok ({dt:.0f} s)")
    sys.exit(0)


if __name__ == "__main__":
    main()
