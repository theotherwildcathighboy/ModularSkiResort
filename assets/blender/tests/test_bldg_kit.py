"""
test_bldg_kit - the building kit contracts (bldg-kit.md section 17 rows test_bldg_kit,
test_split_rooms, test_bldg_materials; skeptic MUST-CHANGE 3-7).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_bldg_kit.py

Asserts:
  1. every builder: part names start with a documented prefix, matkeys in BLDG_MATERIALS,
     face counts within +-10 % of the recorded reference build (REF), no zero-area face;
  2. ground parts (open bottom) give ground_ring_report OK with no face at/below z 0;
     closed parts at grade lift to GROUND_LIFT (door, lantern, flag, rack, umbrella);
     plinth stairs refuse a foot in the ground band; plinth has no vertex in |z| <= 0.01;
  3. window_bay: panes = lights x rows (x 2 with a transom), each ONE quad facing the
     wall normal at +0.05; roof_slab(wall_line) splits the underside into soffit_;
     prism_gable(eave_z1) heights; chimney_top rule; no kit column named post_;
  4. split_rooms on a synthetic 3-storey x 6-bay facade + one tall pane: <= 25 ids, hard
     neighbours differ, same room same id, equal ids only beyond 3 m, first-vertex decode
     == id on every polygon, the tall pane uncut, one UV layer; _Win + _LOD1_Win +
     _LOD2_Win written from one mesh;
  5. cull_hidden: a wall visible only through a 3 deg slot under an eave is KEPT (walls
     are never ray-culled), a sealed attic floor and a coincident duplicate are removed,
     a soffit seen only through the near-horizontal ring is kept, the floor of an open-
     bottom ground box is blocked by the ground-footprint rule;
  6. prefilter: byte-identical twice, square stacked tile, mean preserved; bake alias
     check: Planks021 on a 24 x 12 m wall at ~20 px/m keeps the baked texel std within
     1.3x of the box-filtered reference (skeptic MUST-CHANGE 6);
  7. materials: every set has Color / NormalGL / Roughness, image aspect == tile aspect
     (after uv_rot), targets in [0, 1], recolour materials have slot > 0; colour
     transfer round trip; paint_cross_disc encodes its declared sRGB red; retile keeps
     UVTile isotropic and rotates vertical boards;
  8. lod_level keeps every ground edge (protected collapse) and the planar pass;
     plan_lod_targets never asks for more than the dry run achieves.
Exit 1 on the first failure; one PASS line otherwise.
"""

import hashlib
import math
import os
import shutil
import sys
import tempfile
import time
import traceback

import bpy
import bmesh
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_asset    # noqa: E402
import cs2_bake     # noqa: E402
import cs2_bldg as K    # noqa: E402
import cs2_submesh  # noqa: E402
import cs2_uv       # noqa: E402
import cs2_validate     # noqa: E402
from cs2_bldg import WallRef, RoofSpec, WinSpec   # noqa: E402

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


def faces(parts):
    return sum(len(o.data.polygons) for o, _ in parts)


def join(parts, name="T"):
    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    return o


PREFIXES = ("wall_", "plinth_", "gable_", "roof_", "soffit_", "rake_", "ridge_", "fascia_",
            "tail_", "snowguard_", "gutter_", "downspout_", "vent_", "chim_", "chimcap_",
            "stovepipe_", "win_", "sill_", "head_", "jamb_", "mull_", "cmull_", "ctran_",
            "rwin_", "door_", "entry_", "sign_", "emblem_", "pier_", "plinthcap_", "col_",
            "logcol_", "beam_", "bracket_", "truss_", "deck_", "deckfascia_", "stair_",
            "post_", "rail_", "kick_", "trail_", "bar_", "glassrail_", "balc_", "gazebocol_",
            "gazeboroof_", "tower_", "lanterncol_", "lanterncap_", "bell_", "clock_",
            "clockring_", "lamp_", "flag_", "flagpanel_", "umbstand_", "umbpole_",
            "umbcanopy_", "skirack_", "akja_", "parapet_", "trim_", "dormer_", "dormerroof_",
            "step_",
            # detail pass 1 (L1 builders, detail1-check-aframe F5) and 1b (school / L2 /
            # patrol kit group; contracts in tests/test_detail_kit.py)
            "bench_", "bin_", "maze_", "mast_", "tobrack_", "gear_", "hosereel_", "drum_",
            "wood_", "stripe_")

SPEC = RoofSpec("gable", -5, 5, -8, 8, 6.0, 30.0, axis="Y", overhang=1.0)
HIP = RoofSpec("hip", -5, 5, -8, 8, 6.0, 20.0, axis="Y", overhang=0.6)
BUILDERS = [
    ("storey", lambda: K.storey("a", -5, 5, -8, 8, 0, 3, "stone_rubble", ground=True, cut_z=(1.0,))),
    ("plinth", lambda: K.plinth("p", -5, 5, 10, 12, 2.0, 1.0)),
    ("prism_gable", lambda: K.prism_gable("g", "Y", -8, -7.7, -5, 5, 6, 9)),
    ("roof_slab", lambda: K.roof_slab("s", "Y", -9, 9, -6, 5.4, 0.3, 9.1, wall_line=-5)),
    ("gable_roof", lambda: K.gable_roof("gr", SPEC, tails=1.0, snow_guard=1, gutters=True)),
    ("hip_roof", lambda: K.hip_roof("hr", HIP, gutters=True)),
    ("shed_roof", lambda: K.shed_roof("sr", RoofSpec("shed", 20, 30, -5, 5, 4.0, 12, overhang=1.5), brackets=3.0)),
    ("flat_roof", lambda: K.flat_roof("fr", 40, 50, -5, 5, 6.0, band=(0.35, 0.15, "trim_white"))),
    ("dormer", lambda: K.dormer("d", SPEC, 2.0, room="dorm")),
    ("roof_window", lambda: K.roof_window("rw", SPEC, "+X", -3.0, 7.0, room="rwrow")),
    ("vent_stack", lambda: K.vent_stack("v", SPEC, 0.0, 4.0)),
    ("chimney", lambda: K.chimney("c", -2, 6, 1.2, 1.0, K.chimney_top(SPEC, -2, 6, 1.2, 1.0), shoulder=(1.6, 1.4, 3.0), ground=True)),
    ("stovepipe", lambda: K.stovepipe("sp", SPEC, 2.0, -4.0)),
    ("window_bay", lambda: K.window_bay("wb", WallRef("+X", 5), -6, -2, WinSpec(3.0, 1.4, 1.0, lights=3, rows=2), room="r1")),
    ("window_bay_tr", lambda: K.window_bay("wt", WallRef("-Y", -8), -4, 4, WinSpec(6.0, 2.2, 0.8, lights=4, transom=1.9), room="r2")),
    ("curtain_wall", lambda: K.curtain_wall("cw", WallRef("-X", -5), -6, 6, 0.5, 6.5, room="cw")),
    ("entry", lambda: K.entry("e", WallRef("+X", 5), 4.0, sidelights=2, canopy=(1.5, 35), lamps=2)),
    ("sign_board", lambda: K.sign_board("main", WallRef("+X", 5), 0, 3.5, 3.0, 0.8)),
    ("emblem_disc", lambda: K.emblem_disc("pat", WallRef("+Y", 8), 0, 4.0, 1.2)),
    ("emblem_square", lambda: K.emblem_square("sch", WallRef("-Y", -8), 1, 3.0, 1.2, 1.2)),
    ("stone_pier", lambda: K.stone_pier("sp", 30, 10, 1.2, 1.2, 0, 3.4)),
    ("col", lambda: K.col("c1", 32, 10, 0, 3, ground=True)),
    ("logcol", lambda: K.col("c2", 33, 10, 0, 3, kind="log", ground=True)),
    ("beam", lambda: K.beam("b", (30, 12, 3), (34, 12, 3.5), 0.3, 0.4)),
    ("bracket", lambda: K.bracket("br", (5, 0, 5), "+X")),
    ("bracket_curved", lambda: K.bracket("brc", (5, 2, 5), "+X", curved=True)),
    ("truss_kingpost", lambda: K.truss_kingpost("t", WallRef("+X", 5, -5, 5), 9, 6, 36)),
    ("deck", lambda: K.deck("dk", 20, 26, 10, 16, 3.0, support="col")),
    ("deck_pier", lambda: K.deck("dk2", 20, 26, 20, 26, 3.0, support="pier", support_to=-2.0, board_dir="Y")),
    ("stair", lambda: K.stair("st", (27, 12, 0), (27, 16, 3.0), rail="tube")),
    ("stair_plinth", lambda: K.stair("st2", (27, 20, -0.3), (27, 24, 3.0), ground=False, rail="timber")),
    ("rail_timber", lambda: K.rail("rt", [(20, 30, 3), (26, 30, 3), (26, 34, 3)], kind="timber", gaps=((2, 3),))),
    ("rail_glass", lambda: K.rail("rg", [(20, 36, 3), (26, 36, 3)], kind="glass")),
    ("rail_bars", lambda: K.rail("rb", [(20, 38, 1.4), (26, 38, 1.4)], kind="bars", h=1.0)),
    ("rail_tube", lambda: K.rail("rtb", [(20, 40, 3), (26, 40, 3)], kind="tube")),
    ("balcony", lambda: K.balcony("bal", WallRef("+X", 5), -3, 3, 4.0)),
    ("gazebo", lambda: K.gazebo("gz", 60, 0, 0.6, 5.0)),
    ("porte_cochere", lambda: K.porte_cochere("pc", 70, 80, -6, 6)),
    ("tower_clock", lambda: K.tower("clock", 90, 0, 5.0, 21.0, wall_mat=((6.0, "stone_rubble"), (21.0, "timber_heavy")), clock=(2.4, 19.0), cap_z=25.0)),
    ("tower_lantern", lambda: K.tower("sch", 100, 0, 4.5, 11.0, wall_mat="siding_paint", lantern=(2.6, 0.2, 2.4, 25.0), cap_z=14.4)),
    ("lantern_wall", lambda: K.lantern("l1", (5, 0, 2.2), "+X")[0]),
    ("lantern_post", lambda: K.lantern("l2", (5, 3, 1.1), "+X", kind="post")[0]),
    ("flag_mast", lambda: K.flag_mast("f", 110, 0)),
    ("umbrella", lambda: K.umbrella("u", 112, 0, 0.0)),
    ("ski_rack", lambda: K.ski_rack("sr", 114, 0, facing="+X")),
    ("akja", lambda: K.akja("ak", (5.1, 3, 0), "+X")),
    ("snow_guard", lambda: K.snow_guard("sg", SPEC, "-X")),
    ("gutter", lambda: K.gutter("g", (0, 0), (0, 5), 4.0)),
    ("downspout", lambda: K.downspout("ds", 0, 0, 4.0, "+X")),
    # detail pass 1 (L1, 2026-10-11): no case before (detail1-check-aframe F5)
    ("ski_gear", lambda: K.ski_gear("lg", (
        ("ski", (130.0, 0.6, 0.04), (130.6, 0.6, 1.48), ("trim_white", "ski_blue"), 1.72),
        ("board", (130.0, 1.6, 0.04), (130.6, 1.6, 1.30), "accent", 1.52),
        ("poles", (130.0, 2.4, 0.04), (130.6, 2.4, 1.05), "ski_black", 1.20)))),
    ("ski_rack_gear", lambda: K.ski_rack("srg", 134, 0, slots=8, gear=True, skis=(
        (0, 1, "ski", ("trim_white", "ski_red"), 1.7), (2, 1, "board", "accent", 1.5),
        (5, -1, "poles", "ski_black", 1.2)))),
    ("painted_board", lambda: K.painted_board("pb", ("+X", 5.0), 2.0, 3.0, 0.84, 0.84, density_w=24.0)),
    ("bench", lambda: K.bench("b", 140, 0, "+X")),
    ("litter_bin", lambda: K.litter_bin("lb", 142, 0)),
    ("string_lights", lambda: K.string_lights("sl", ((144, 0, 4.0, None), (148.6, 0, 3.8, 0.02)))),
    ("downspout_leader", lambda: K.downspout("dl", 150, 0, 6.0, "+X", z_bottom=4.7, leader=(150.6, 0.4))),
    # detail pass 1b (school / L2 / patrol, kit group 2026-10-11)
    ("post_sign", lambda: K.post_sign("ps", 152, 0, "+X", 0.6, 0.6, 2.6)),
    ("maze_fence", lambda: K.maze_fence("mz", (((154, 0), (157, 0)), ((157, 0), (157, 1.6))))),
    ("radio_mast", lambda: K.radio_mast("rm", 160, 0, 0.6, 5.3, brackets=((1.2, "-Y", 0.5),))),
    ("toboggan_rack", lambda: K.toboggan_rack("tr", ("-X", 5.0), -1.0, 1.0, (0.55, 1.75))),
    ("backboard", lambda: K.backboard("bb", (162, 0, 0.02), "-X")),
    ("hose_reel", lambda: K.hose_reel("hr", WallRef("+Y", 8.0), 0.0, 1.9)),
    ("drum", lambda: K.drum("dr", (164, 0, 0.8), "X")),
    ("fuel_cradle", lambda: K.fuel_cradle("fc", 164, 0, "X", 0.5, 0.02)),
    ("log_stack", lambda: K.log_stack("ls", 166, 166.4, 0, 3, 0.02, 0.6, "+X")),
    ("safety_stripe", lambda: K.safety_stripe("st", (168, 0), (168, 1.2), 0.18)),
    ("roof_emblem", lambda: K.roof_emblem("re", SPEC, "+X", 1.5, s0=0.3)),
]
# Face counts of the reference build (2026-10-11, Blender 5.2.2); +-10 % allowed.
REF = {
    "storey": 9, "plinth": 4, "prism_gable": 4, "roof_slab": 9, "gable_roof": 472,
    "hip_roof": 78, "shed_roof": 77, "flat_roof": 41, "dormer": 53, "roof_window": 21,
    "vent_stack": 14, "chimney": 15, "stovepipe": 28, "window_bay": 41,
    "window_bay_tr": 48, "curtain_wall": 76, "entry": 153, "sign_board": 21,
    "emblem_disc": 32, "emblem_square": 5, "stone_pier": 10, "col": 5, "logcol": 14,
    "beam": 6, "bracket": 12, "bracket_curved": 24, "truss_kingpost": 48, "deck": 70,
    "deck_pier": 78, "stair": 256, "stair_plinth": 186, "rail_timber": 72, "rail_glass": 12,
    "rail_bars": 36, "rail_tube": 72, "balcony": 113, "gazebo": 198, "porte_cochere": 196,
    "tower_clock": 210, "tower_lantern": 150, "lantern_wall": 12, "lantern_post": 12,
    "flag_mast": 20, "umbrella": 44, "ski_rack": 25, "akja": 6, "snow_guard": 94,
    "gutter": 6, "downspout": 12,
    "ski_gear": 50, "ski_rack_gear": 150, "painted_board": 21, "bench": 24, "litter_bin": 14,
    "string_lights": 38, "downspout_leader": 18, "post_sign": 17, "maze_fence": 28,
    "radio_mast": 28, "toboggan_rack": 28, "backboard": 6, "hose_reel": 41, "drum": 20,
    "fuel_cradle": 33, "log_stack": 5, "safety_stripe": 5, "roof_emblem": 1,
}
GROUND_BUILDERS = {"storey": "wall_a", "stone_pier": "pier_sp", "col": "col_c1",
                   "logcol": "logcol_c2", "chimney": "chim_csh", "stair": "stair_st_str",
                   "tower_clock": "tower_clock"}


def check_builders():
    clear()
    built = {}
    for nm, fn in BUILDERS:
        parts = fn()
        for o, m in parts:
            assert o.name.startswith(PREFIXES), f"{nm}: {o.name} has no documented prefix"
            assert m in K.BLDG_MATERIALS, f"{nm}: {o.name} matkey {m}"
            assert not o.name.startswith("post_") or nm in ("stair", "rail_tube"), \
                f"{nm}: kit column named post_ ({o.name})"
            for p in o.data.polygons:
                assert p.area > 1e-9, f"{nm}: zero-area face in {o.name}"
        n = faces(parts)
        ref = REF[nm]
        assert abs(n - ref) <= max(1, 0.10 * ref), f"{nm}: {n} faces vs reference {ref}"
        built[nm] = parts
    # T64: the chimney shoulder keeps its +Z face over its whole footprint (the stack is narrower than the shoulder)
    sh = [o for o, _m in built["chimney"] if o.name == "chim_csh"][0]
    tops = [p for p in sh.data.polygons if p.normal.z > 0.99
            and abs(sum(sh.data.vertices[i].co.z for i in p.vertices) / 4 - 3.0) < 1e-6]
    assert len(tops) == 1 and abs(tops[0].area - 1.6 * 1.4) < 1e-6, f"chimney shoulder +Z face missing / partial: {len(tops)}"
    ok(f"{len(BUILDERS)} builders: prefixes, matkeys, face counts within 10 % of the reference")
    # ground parts: OK ring, nothing at / below z 0 except the open band edges
    for nm, pre in GROUND_BUILDERS.items():
        gp = [(o, m) for o, m in built[nm] if o.name.startswith(pre)]
        assert gp, f"{nm}: no ground part {pre}"
        o = join([(K._make_obj(f"g{nm}", _copy_bm(gp)), "x")], f"G{nm}")
        r = cs2_validate.ground_ring_report(o)
        assert r["verdict"] == "OK" and r["below_verts"] == 0, f"{nm}: ground ring {r}"
        for p in o.data.polygons:
            zs = [o.data.vertices[i].co.z for i in p.vertices]
            assert max(zs) > 0.011, f"{nm}: a face lies at/below z 0 (closed bottom)"
    ok(f"{len(GROUND_BUILDERS)} ground builders: open bottom, ground ring OK, nothing below 0")
    # closed parts at grade lift to GROUND_LIFT
    for nm, pre in (("flag_mast", "flag_f"), ("ski_rack", "skirack_sr"), ("umbrella", "umbstand_u"),
                    ("akja", "akja_ak"), ("bench", "bench_b"), ("litter_bin", "bin_lb"),
                    ("ski_gear", "skirack_lg"), ("post_sign", "sign_ps"), ("maze_fence", "maze_mz"),
                    ("backboard", "gear_bb"), ("fuel_cradle", "drum_fc"), ("log_stack", "wood_ls")):
        zmin = min(min(v.co.z for v in o.data.vertices) for o, _ in built[nm] if o.name.startswith(pre))
        assert zmin >= K.GROUND_LIFT - 1e-6, f"{nm}: starts at {zmin} (< GROUND_LIFT)"
    zdoor = min(min((o.matrix_world @ v.co).z for v in o.data.vertices)
                for o, _ in built["entry"] if o.name.startswith(("door_", "entry_")))
    assert zdoor >= K.GROUND_LIFT - 1e-6, f"entry at z 0 starts at {zdoor}"
    try:
        K.stair("bad", (0, 50, 0.0), (0, 54, 3.0), ground=False)
        raise AssertionError("plinth stair with its foot in the ground band was accepted")
    except ValueError:
        pass
    pl = built["plinth"][0][0]
    assert all(abs(v.co.z) > 0.01 for v in pl.data.vertices), "plinth vertex in the band"
    ok("closed parts at grade start >= 0.02; plinth stair foot in the band refused; plinth clear of |z|<=0.01")
    return built


def check_gear_bindings():
    """Richard, 2026-10-11 (ninth look): NO binding blocks on skis or boards, anywhere.
    bindings=False is the default of ski_gear / ski_rack_frame / ski_rack; the keyword
    bindings=True is the detail-pass-1 geometry and exists for this test."""
    # a ski pair and a board, free-standing: default has no galv part, bindings=True adds
    # 2 binding blocks per pair (2 x 5 faces) and 2 x (base plate + highback) per board (4 x 5)
    items = {"ski": ("ski", (200.0, 0.6, 0.04), (200.6, 0.6, 1.48), ("trim_white", "ski_blue"), 1.72),
             "board": ("board", (200.0, 1.6, 0.04), (200.6, 1.6, 1.30), ("ski_black", "accent"), 1.52)}
    n_extra = {"ski": 10, "board": 20}
    for kind, it in items.items():
        off = K.ski_gear(f"nb_{kind}", (it,))
        on = K.ski_gear(f"wb_{kind}", (it,), bindings=True)
        assert all(m != "galv" for _o, m in off), f"{kind}: a galv (binding) part without bindings"
        d = faces(on) - faces(off)
        assert d == n_extra[kind], f"{kind}: bindings add {d} faces, expected {n_extra[kind]}"
    # the rack path: the same, and the explicit keyword reaches ski_rack_frame via ski_rack
    skis = ((0, 1, "ski", ("trim_white", "ski_red"), 1.7), (2, 1, "board", "accent", 1.5),
            (4, -1, "ski", ("accent", "ski_black"), 1.62))
    off = K.ski_rack("nbr", 210, 0, slots=8, gear=True, skis=skis)
    on = K.ski_rack("wbr", 220, 0, slots=8, gear=True, skis=skis, bindings=True)
    gear_off = [(o, m) for o, m in off if o.name.startswith("skirack_nbrg")]
    assert gear_off and all(m != "galv" for _o, m in gear_off), "rack gear drew a galv binding"
    assert faces(on) - faces(off) == 2 * 10 + 20, (faces(on), faces(off))
    try:
        K.ski_rack("bad", 0, 0, facing="+X", bindings=True)
        raise AssertionError("bindings= on the painted-panel rack was accepted")
    except ValueError:
        pass
    # the lean: rack_reach() is the foot -> lean distance, and the default pole pair reaches it
    r = K.rack_reach()
    assert abs(r - 1.3296) < 1e-3, r
    assert K.GEAR_LEN["poles"] >= r, "the default pole pair cannot reach the rack's lean"
    ok(f"gear bindings: none by default (ski_gear, rack gear), bindings=True adds 10 faces per "
       f"pair and 20 per board, refused on the painted rack; rack_reach() {r:.3f} m, default "
       f"pole pair {K.GEAR_LEN['poles']} m")


def _copy_bm(parts):
    bm = bmesh.new()
    for o, _ in parts:
        tmp = bmesh.new()
        tmp.from_mesh(o.data)
        tmp.transform(o.matrix_basis)
        me = bpy.data.meshes.new("tmpcopy")
        tmp.to_mesh(me)
        tmp.free()
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    return bm


def check_windows_roofs(built):
    # window_bay: 3 lights x 2 rows = 6 panes; 4 lights + transom = 8
    for nm, n_exp, n_dir in (("window_bay", 6, Vector((1, 0, 0))), ("window_bay_tr", 8, Vector((0, -1, 0)))):
        panes = [o for o, _ in built[nm] if o.name.startswith("win_")]
        assert len(panes) == n_exp, f"{nm}: {len(panes)} panes, expected {n_exp}"
        for o in panes:
            assert len(o.data.polygons) == 1 and len(o.data.polygons[0].vertices) == 4, \
                f"{o.name}: a pane is ONE quad"
            n = o.data.polygons[0].normal
            assert n.dot(n_dir) > 0.999, f"{o.name}: pane faces {tuple(n)}"
            c = o.data.polygons[0].center
            plane = 5.0 if nm == "window_bay" else -8.0
            off = (c.x - plane) if nm == "window_bay" else (plane - c.y)
            assert abs(off - K.PANE_OFFSET) < 1e-5, f"{o.name}: pane at {off} from the wall"
    assert any(o.name.startswith("ctran_wt") for o, _ in built["window_bay_tr"]), "no transom bar"
    ok("window_bay: lights x rows (x2 with transom) single-quad panes at wall + 0.05, transom bar")
    # roof_slab(wall_line) -> soffit_ part under the overhang only
    roof, soffit = built["roof_slab"][0][0], built["roof_slab"][1][0]
    assert soffit.name == "soffit_s" and len(soffit.data.polygons) == 1
    sx = [soffit.data.vertices[i].co.x for i in soffit.data.polygons[0].vertices]
    assert min(sx) < -5.0 - 0.5 and abs(max(sx) - (-5.0)) < 1e-4, f"soffit spans {sx}"
    assert soffit.data.polygons[0].normal.z < -0.5
    under = [p for p in roof.data.polygons if p.normal.z < -0.8]     # not the eave end face
    assert all(min(roof.data.vertices[i].co.x for i in p.vertices) >= -5.0 - 1e-4 for p in under), \
        "roof_ kept an underside face over the overhang"
    ok("roof_slab(wall_line): overhang underside is its own soffit_ quad, inner underside stays on roof_")
    # prism_gable eave_z1 (saltbox)
    clear()
    g = K.prism_gable("salt", "X", 0, 0.3, -3.8, 3.8, 3.0, 6.17, ridge_at=1.1, eave_z1=4.26)[0][0]
    zs = sorted({round(v.co.z, 4) for v in g.data.vertices})
    assert zs == [3.0, 4.26, 6.17], f"saltbox gable heights {zs}"
    ok("prism_gable(eave_z1): eaves 3.0 / 4.26, ridge 6.17")
    # chimney_top: >= plane + 0.9 and ridge + 0.6 within 3 m of the ridge
    top_near = K.chimney_top(SPEC, -0.5, 0, 1.0, 1.0)
    top_far = K.chimney_top(SPEC, -4.0, 0, 1.0, 1.0)
    rz = K.roof_z(SPEC, 0.0, 0.0)
    assert top_near >= rz + 0.6 - 1e-6 and top_near >= K.roof_z(SPEC, 0.0, 0) + 0.6
    assert abs(top_far - (K.roof_z(SPEC, -3.5, 0.5) + 0.9)) < 1e-6, (top_far, K.roof_z(SPEC, -3.5, 0.5))
    ok(f"chimney_top: near ridge {top_near:.2f} >= ridge {rz:.2f} + 0.6; far {top_far:.2f} = plane + 0.9")


def check_rooms():
    clear()
    parts = []
    wall = WallRef("+X", 0.0)
    # 3 storeys x 6 bays, bays 2.4 m apart (gap 1.0 m < 3 m -> hard neighbours)
    for s in range(3):
        for b in range(6):
            u0 = -7.2 + 2.4 * b
            parts += K.window_bay(f"s{s}b{b}", wall, u0, u0 + 2.4, WinSpec(1.4, 1.8, 1.0 + 3.0 * s,
                                  lights=2), room=f"s{s}b{b}")
    # far bay on the same floor 10 m away (no merge possible -> may reuse)
    parts += K.window_bay("far", wall, 9.0, 11.0, WinSpec(1.4, 1.4, 1.0, lights=1), room="far")
    # one tall pane (two storeys)
    parts += K.pane_quad("win_tall_tall0", WallRef("-X", -6.0), -1.0, 1.0, 0.5, 6.5)
    parts += K.storey("body", -6.0, 0.0, -9.0, 12.0, 0.0, 9.0, "timber_lap", ground=True)
    table = cs2_asset.tag_parts(parts)
    obj = join(parts, "RoomTest")
    cs2_asset.store_part_table(obj, table)
    boxes, hard, soft = K.room_graph(obj, table)
    assert ("s0b0", "s0b1") in hard and ("s0b0", "s1b0") not in hard, "hard = same floor < 3 m"
    assert ("s0b0", "s1b0") in soft, "vertically adjacent bays are soft neighbours"
    sub, cells = K.split_rooms(obj, table, "auto", name="RoomTest")
    assert len(set(cells.values())) <= 25 and set(cells.values()) <= set(K.WIN_POOL)
    for a, b in hard:
        assert cells[a] != cells[b], f"hard neighbours {a} {b} share id {cells[a]}"
    # equal ids only beyond 3 m on overlapping floors
    hs = set(hard)
    for a in cells:
        for b in cells:
            if a < b and cells[a] == cells[b]:
                assert (a, b) not in hs
    # decode every polygon's first loop
    ids = K.win_cell_ids(sub)
    tbl = {}
    pp = K._poly_parts(sub)
    for p, cid in zip(sub.data.polygons, ids):
        room = K._room_of(table[int(pp[p.index]) - 1])
        assert cid == cells[room], f"pane of {room}: decoded {cid} != {cells[room]}"
        tbl.setdefault(room, set()).add(cid)
    assert all(len(v) == 1 for v in tbl.values()), "same room -> one id"
    tall = [p for p in sub.data.polygons if table[int(pp[p.index]) - 1] == "win_tall_tall0"]
    assert len(tall) == 1 and abs(tall[0].area - 12.0) < 1e-4, "the tall pane must stay uncut"
    assert len(sub.data.uv_layers) == 1, "sub-mesh ships one UV layer"
    assert not any(n.startswith("win_") for n in [table[int(i) - 1] for i in K._poly_parts(obj)]), \
        "panes left on the main mesh"
    d = tempfile.mkdtemp(prefix="bldgkit_win_")
    try:
        paths = cs2_submesh.export_submeshes(sub, "RoomTest", "Win", d, lods=(1, 2))
        assert [os.path.basename(p) for p in paths] == ["RoomTest_Win.fbx", "RoomTest_LOD1_Win.fbx",
                                                        "RoomTest_LOD2_Win.fbx"]
    finally:
        shutil.rmtree(d, ignore_errors=True)
    ok(f"split_rooms: {len(cells)} rooms -> {len(set(cells.values()))} ids, hard neighbours differ, "
       f"decode == id on {len(ids)} panes, tall pane uncut, _Win/_LOD1_Win/_LOD2_Win")


def check_cull():
    """The skeptic's vis3 cases on a small synthetic building (walls never ray-culled)."""
    clear()
    parts = []
    parts += K.storey("body", -5, 5, -6, 6, 0.0, 3.0, "timber_lap", ground=True, drop=("+Z",))
    # deck at 0.6 in front of +X and an eave at 1.2 m above the wall top: the +X wall's
    # lower band sees out only through a ~3 deg slot (eave tip vs deck far edge)
    parts += K.box_part("deck_front", (5.0, -6, 0.44), (15.0, 6, 0.60), "deck", drop=("-Z",))
    parts += K.box_part("roof_lid", (-6.0, -7, 3.0), (6.2, 7, 3.2), "slate")
    parts += K.box_part("fascia_lid", (6.2, -7, 0.75), (6.32, 7, 3.2), "trim")
    # attic floor: a horizontal plate fully inside the body (non-wall, hidden)
    parts += K.box_part("plinthcap_in", (-3, -3, 1.0), (3, 3, 1.1), "concrete")
    # soffit seen only near-horizontally: underside strip of the lid outside the walls
    table = cs2_asset.tag_parts(parts)
    obj = join(parts, "CullTest")
    cs2_asset.store_part_table(obj, table)
    area_walls_before = sum(p.area for p in obj.data.polygons if abs(p.normal.z) < 0.5)
    rep = K.cull_hidden(obj, protect=(), table=table, ground_prefixes=("wall_body",),
                        cull_ok=("plinthcap_in", "roof_lid", "deck_front", "fascia_lid"))
    area_walls_after = sum(p.area for p in obj.data.polygons if abs(p.normal.z) < 0.5)
    assert rep["wall_area_deleted_by_rays"] == 0.0, rep
    pp = K._poly_parts(obj)
    names = [table[int(i) - 1] for i in pp]
    assert "plinthcap_in" not in names or all(
        abs(p.normal.z) < 0.5 for p, n in zip(obj.data.polygons, names) if n == "plinthcap_in"), \
        "the sealed interior plate's top / bottom must be culled"
    lid_under = [p for p, n in zip(obj.data.polygons, names) if n == "roof_lid" and p.normal.z < -0.5]
    assert lid_under, "the lid underside (soffit band outside the walls) must survive (ring rays)"
    walls = [p for p, n in zip(obj.data.polygons, names) if n == "wall_body"]
    px = sum(p.area for p in walls if p.normal.x > 0.9)
    assert abs(px - 36.0) < 1e-3, f"the +X wall under the eave lost area ({px} of 36 m2)"
    assert area_walls_after > 0.5 * area_walls_before
    ok(f"cull_hidden: under-eave wall kept, sealed interior plate culled, lid underside kept, "
       f"walls deleted by rays {rep['wall_area_deleted_by_rays']} m2 ({rep['faces_removed']} faces)")
    # duplicate coplanar same-facing faces: the later one goes
    clear()
    parts = K.box_part("wall_x", (0, 0, 0.5), (4, 4, 3), "trim") + \
        K.box_part("wall_y", (0, 0, 0.5), (2, 4, 3), "trim", drop=("+X",))
    table = cs2_asset.tag_parts(parts)
    obj = join(parts, "DupTest")
    cs2_asset.store_part_table(obj, table)
    n0 = len(obj.data.polygons)
    rep = K.cull_hidden(obj, table=table, cull_ok=("wall_y",))
    assert rep["faces_removed"] >= 3, f"coincident duplicates not removed: {rep}"
    ok(f"cull_hidden: coincident duplicate faces removed ({rep['faces_removed']} of {n0})")


def _std_lum(px):
    return float(px[..., :3].mean(-1).std())


def check_prefilter_and_alias():
    d1 = tempfile.mkdtemp(prefix="bldgkit_pf1_")
    d2 = tempfile.mkdtemp(prefix="bldgkit_pf2_")
    try:
        m = K.BLDG_MATERIALS["timber_lap"]
        a = K.prefilter_set(m.set, K.image_tile_m(m), 32.0, cache_dir=d1)
        b = K.prefilter_set(m.set, K.image_tile_m(m), 32.0, cache_dir=d2)
        c = K.prefilter_set(m.set, K.image_tile_m(m), 32.0, cache_dir=d1)
        assert c["cached"] and not a["cached"]
        assert a["N"] == round(2.4 * 32.0) and a["tile_sq"] == 2.4 and a["stack"] == (1, 2)
        for mp in K.PREFILTER_MAPS:
            fa = os.path.join(a["dir"], f"{os.path.basename(a['dir'])}_{mp}.png")
            fb = os.path.join(b["dir"], f"{os.path.basename(b['dir'])}_{mp}.png")
            ha = hashlib.sha1(open(fa, "rb").read()).hexdigest()
            hb = hashlib.sha1(open(fb, "rb").read()).hexdigest()
            assert ha == hb, f"prefilter {mp} not deterministic"
            w, h, _d, _c = cs2_validate.read_png_header(fa)
            assert w == h == a["N"], f"{mp}: {w}x{h}"
        src = K._load_raw(cs2_bake.find_set_map(os.path.join(cs2_bake.TEXTURE_ROOT, m.set), "Color"))
        full_mean = np.array(K.srgb_to_linear(src[..., :3])).reshape(-1, 3).mean(0)
        assert np.allclose(full_mean, a["mean_lin"], rtol=0.01, atol=0.002), (full_mean, a["mean_lin"])
        ok(f"prefilter: WoodSiding009 -> {a['N']}^2 stacked square tile, byte-identical twice, "
           f"linear mean preserved ({np.round(full_mean, 4)})")
    finally:
        shutil.rmtree(d1, ignore_errors=True)
        shutil.rmtree(d2, ignore_errors=True)
    # alias check (skeptic MUST-CHANGE 6): Planks021 on a 24 x 12 m wall at ~20 px/m
    clear()
    ppm = 20.0
    deck = K.BLDG_MATERIALS["deck"]
    tile = K.square_tile_m(deck)
    # box-filtered reference: the tile area-averaged straight to ppm
    srcp = K._load_raw(cs2_bake.find_set_map(os.path.join(cs2_bake.TEXTURE_ROOT, deck.set), "Color"))
    n_ref = int(round(tile * ppm))
    lin = np.array(K.srgb_to_linear(srcp[..., :3]))

    ref = np.array(K.linear_to_srgb(K.box_filter(lin, n_ref)))
    ref_std = _std_lum(ref)
    res = {}
    for label, factor in (("prefiltered", K.PREFILTER_FACTOR), ("raw", None)):
        clear()
        part = K.box_part("wall_alias", (0, 0, 0), (24.0, 0.2, 12.0), "deck", drop=("-Z",))
        o = part[0][0]
        bm = bmesh.new()
        bm.from_mesh(o.data)
        keep = [f for f in bm.faces if f.normal.y < -0.9]
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep], context="FACES")
        bm.to_mesh(o.data)
        bm.free()
        cs2_asset.tag_parts(part)
        cs2_asset.store_part_table(o, ["wall_alias"])
        K.strip_uv_layers(part)
        if factor is None:
            spec = cs2_bake.MaterialSpec(texture_set=deck.set, metres_per_tile=tile, metal=0.0)
            srcs = cs2_bake.material_sources({"deck": spec}, part)
        else:
            specs, _ = K.material_specs(["deck"], ppm, factor=factor)
            spec = specs["deck"]
            cs2_bake.material_sources({"deck": K.projection_specs(["deck"])["deck"]}, part)
            ss = cs2_bake._resolve_spec(spec, "deck")
            from dataclasses import replace
            srcs = {0: replace(ss, uv="UVTile")}
        o.name = "AliasTest"
        d = tempfile.mkdtemp(prefix="bldgkit_alias_")
        try:
            rep = cs2_bake.bake_unique(o, d, "AliasTest", 512, srcs, ao_samples=0, margin_px=8,
                                       src_uv="UVTile")
            px = cs2_validate.load_png_pixels(os.path.join(d, "AliasTest_BaseColor.png"))
            dens = rep["pack"]["px_per_m"]["median"]
            # covered texels only (eroded by 3 px)
            cov = cs2_validate.uv_coverage(o.data, 512) > 0
            for _ in range(3):
                cov = cs2_validate._erode(cov)
            res[label] = (_std_lum(px[cov][None, ...]), dens)
        finally:
            shutil.rmtree(d, ignore_errors=True)
    std_f, dens_f = res["prefiltered"]
    std_r, _ = res["raw"]
    assert 15.0 < dens_f < 25.0, f"alias wall shipped at {dens_f:.1f} px/m, expected ~20"
    assert std_f <= 1.3 * ref_std, f"prefiltered bake std {std_f:.4f} > 1.3 x box reference {ref_std:.4f}"
    ok(f"alias: baked std {std_f:.4f} (prefiltered) vs box reference {ref_std:.4f} "
       f"({std_f / ref_std:.2f}x <= 1.3x; unfiltered {std_r:.4f} = {std_r / ref_std:.2f}x) at {dens_f:.1f} px/m")


def check_materials_colour():
    for key, m in sorted(K.BLDG_MATERIALS.items()):
        if m.target_srgb is not None:
            assert all(0.0 <= c <= 1.0 for c in m.target_srgb), key
        if m.recolor:
            assert m.slot > 0, f"{key}: recolour needs a slot"
        if m.set:
            d = os.path.join(cs2_bake.TEXTURE_ROOT, m.set)
            paths = {mp: cs2_bake.find_set_map(d, mp) for mp in ("Color", "NormalGL", "Roughness")}
            assert all(paths.values()), f"{key}: set {m.set} incomplete {paths}"
            w, h, _d, _c = cs2_validate.read_png_header(paths["Color"]) if paths["Color"].endswith(".png") \
                else (None, None, None, None)
            if w is None:
                img = bpy.data.images.load(paths["Color"])
                w, h = img.size
                bpy.data.images.remove(img)
            tu, tv = K.image_tile_m(m)
            assert abs(w / h - tu / tv) < 1e-6, f"{key}: image {w}x{h} vs tile {tu}x{tv}"
    ok(f"{len(K.BLDG_MATERIALS)} materials: sets complete, image aspect == tile aspect (after uv_rot), "
       f"targets in [0, 1], recolour has a slot")
    for c in (0.0, 0.02, 0.2, 0.5, 0.8, 1.0):
        assert abs(K.linear_to_srgb(K.srgb_to_linear(c)) - c) < 1e-9
    assert abs(K.srgb_to_linear(0.80) - 0.6038) < 1e-3
    base = np.zeros((64, 64, 4), np.float32)
    K.paint_cross_disc((0, 64, 0, 64), base, np.zeros_like(base))
    red = np.array(K.linear_to_srgb(base[16, 16, :3]))     # inside the disc, outside the cross
    assert np.allclose(red, (0.72, 0.09, 0.08), atol=0.01), f"cross-disc red encodes {red}"
    white = np.array(K.linear_to_srgb(base[32, 32, :3]))
    assert np.allclose(white, 0.92, atol=0.01), f"cross encodes {white}"
    for p in (K.paint_pictogram("skier"), K.paint_pictogram("restaurant"), K.paint_pictogram("info"),
              K.paint_text5x7("SKI"), K.paint_clock,
              K.make_painter("paint_skis", face="skirack_a_face")):
        b = np.full((40, 80, 4), 0.5, np.float32)
        p((0, 40, 0, 80), b, np.zeros_like(b))
        assert np.abs(b - 0.5).max() > 0.05, f"painter {getattr(p, '__name__', p)} painted nothing"
    ok("colour: sRGB round trip, cross-disc red / white encode to their declared sRGB, 6 painters paint")
    # retile: vertical boards rotate UVTile, along keeps it isotropic
    clear()
    parts = K.storey("vb", 0, 4, 0, 4, 0.5, 3.5, "timber_vboard") + \
        K.beam("bm", (10, 0, 2), (16, 0, 2.5), 0.3, 0.4)
    K.strip_uv_layers(parts)
    specs = K.projection_specs([k for _o, k in parts])
    cs2_bake.material_sources(specs, parts)
    before = cs2_uv.loop_uvs(parts[0][0].data.uv_layers["UVTile"]).copy()
    K.retile(parts, specs)
    after = cs2_uv.loop_uvs(parts[0][0].data.uv_layers["UVTile"])
    assert np.allclose(after[:, 0], before[:, 1]) and np.allclose(after[:, 1], -before[:, 0]), \
        "uv_rot 90 not applied"
    me = parts[1][0].data
    uv = cs2_uv.loop_uvs(me.uv_layers["UVTile"])
    for p in me.polygons:
        P = [me.vertices[i].co for i in p.vertices]
        q = uv[p.loop_start:p.loop_start + p.loop_total]
        e3 = (P[1] - P[0]).length
        e2 = float(np.linalg.norm(q[1] - q[0])) * specs["timber_heavy"].metres_per_tile
        assert abs(e3 - e2) < 1e-3, f"along projection not isotropic ({e3} vs {e2})"
    ok("retile: uv_rot 90 rotates UVTile, `along` projection isotropic (metres / tile)")


def check_lods():
    clear()
    parts = []
    parts += K.storey("base", -6, 6, -8, 8, 0.0, 3.0, "stone_rubble", ground=True, drop=("+Z",))
    parts += K.storey("up", -6, 6, -8, 8, 3.0, 6.0, "timber_lap", drop=("-Z", "+Z"))
    parts += K.gable_roof("r", RoofSpec("gable", -6, 6, -8, 8, 6.0, 30, overhang=0.8), tails=0.6,
                          snow_guard=1)
    for i in range(4):
        parts += K.window_bay(f"w{i}", WallRef("+X", 6), -7 + 3.5 * i, -4 + 3.5 * i,
                              WinSpec(1.4, 1.4, 4.0, lights=2))
    parts += K.box_part("step_a", (6.0, -1, 0.0), (7.0, 1, 0.3), "concrete", ground=True)
    K.bevel_parts(parts)
    table = cs2_asset.tag_parts(parts)
    obj = join(parts, "LodTest")
    cs2_asset.store_part_table(obj, table)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")
    cs2_asset.shade_by_angle(obj, 66.0)
    n0 = cs2_asset.tri_count(obj)

    class _R:
        budget = type("B", (), {"lod1": 10 ** 6, "lod2": 120})()
        ground_prefixes = ("wall_base", "step_")
    (t1, t2), rep = K.plan_lod_targets(obj, table, _R, delete_lists=(K.LOD1_DELETE, K.LOD2_DELETE))
    assert t1 <= round(0.45 * n0) and t1 <= rep["LOD1"]["achievable"] and t2 <= rep["LOD2"]["achievable"]
    lods, infos = K.lod_chain(obj, "LodTest", (t1, t2), (K.LOD1_DELETE, K.LOD2_DELETE),
                              ("wall_base", "step_"))
    for lod, info in zip(lods, infos):
        r = K.check_base_mode(lod, table, "engine", ("wall_base", "step_"))
        assert r["verdict"] == "OK"
        assert info["after_planar"] <= info["after_delete"]
    ok(f"lod chain: LOD0 {n0} -> {[i['tris'] for i in infos]} (targets {t1}, {t2}), ground edges "
       f"intact on every level, planar pass {infos[1]['after_delete']} -> {infos[1]['after_planar']}")


def check_packer():
    """pack_layout_calibrated: at least the library FRACTION fill on a many-island kit,
    gutters >= 1.8 x margin between island boxes, the library packer restored on exit."""
    clear()
    parts = K.storey("big", 0, 12, 0, 8, 0.0, 4.0, "timber_lap", ground=True)
    for i in range(160):
        x, y = 20 + (i % 16) * 0.8, (i // 16) * 0.8
        parts += K.box_part(f"tail_t{i}", (x, y, 1.0), (x + 0.5, y + 0.12, 1.16), "timber_heavy")
    K.strip_uv_layers(parts)
    specs = K.projection_specs([k for _o, k in parts])
    cs2_bake.material_sources(specs, parts)
    K.retile(parts, specs)
    obj = join(parts, "PackTest")
    res = {}
    for label in ("fraction", "calibrated"):
        o = obj.copy()
        o.data = obj.data.copy()
        bpy.context.scene.collection.objects.link(o)
        o.data.uv_layers.active = o.data.uv_layers["UVTile"]
        if label == "calibrated":
            with K.calibrated_packer():
                rep = cs2_uv.pack_unique(o, 512, [], margin_px=8)
            assert cs2_uv._pack_layout is K._LIB_PACK_LAYOUT, "library packer not restored"
        else:
            rep = cs2_uv.pack_unique(o, 512, [], margin_px=8)
        me = o.data
        lay = me.uv_layers["UVUnique"]
        uv = cs2_uv.loop_uvs(lay)
        st, tt = cs2_uv.face_loop_table(me)
        boxes = []
        for isl in cs2_uv.uv_islands(me, lay):
            idx = np.concatenate([np.arange(st[f], st[f] + tt[f]) for f in isl])
            boxes.append((uv[idx].min(0), uv[idx].max(0)))
        lo = np.array([b[0] for b in boxes])
        hi = np.array([b[1] for b in boxes])
        gap = 1e9
        for i in range(len(boxes)):
            g = np.maximum(np.maximum(lo[:, 0] - hi[i, 0], lo[i, 0] - hi[:, 0]),
                           np.maximum(lo[:, 1] - hi[i, 1], lo[i, 1] - hi[:, 1]))
            g[i] = 1e9
            gap = min(gap, float(g.min()) * 512)
        res[label] = (float(cs2_uv.face_uv_areas(me, uv).sum()), gap, rep.get("packer", "library"))
    fc, gc, _ = res["calibrated"]
    ff, _gf, _ = res["fraction"]
    assert fc >= ff - 1e-3, f"calibrated fill {fc:.3f} < library {ff:.3f}"
    assert gc >= 1.8 * 8, f"calibrated gutter {gc:.1f} px < 1.8 x 8"
    ok(f"calibrated ADD pack: fill {fc:.3f} vs library FRACTION {ff:.3f}, min gutter {gc:.1f} px "
       f"(margin 8), library packer restored")


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


#: fine-detail pass (design B.1, build/fine-detail-step2-engine.md): the default keyword of every
#: builder that gained `fine=` reproduces the PRE-PASS engine (hashes measured on the 2026-10-12
#: snapshot of assets/blender taken before the edit, same calls, same names) and fine=True has
#: the B.1 triangle counts (and the same part names / matkeys).
FINE_CALLS = {
    "flag_mast": lambda kw: K.flag_mast("fdk1", 1.0, 2.0, h=7.5, flag=(1.4, 0.9), along="Y", face=True, **kw),
    "flag_mast_plain": lambda kw: K.flag_mast("fdk2", 5.0, 2.0, h=2.0, flag=(0.35, 0.25), **kw),
    "post_sign": lambda kw: K.post_sign("fdk3", 6.9, -15.0, "+X", 0.6, 0.6, 2.6, **kw),
    "maze_fence": lambda kw: K.maze_fence("fdk4", (((-5.6, -12.9), (-2.6, -12.9)), ((-2.6, -12.9), (-2.6, -11.3))),
                                          post_every=2.0, **kw),
    "radio_mast": lambda kw: K.radio_mast("fdk5", -1.6, 3.25, 0.60, 5.30, brackets=((1.2, "-Y", 0.525),), **kw),
    "toboggan_rack": lambda kw: K.toboggan_rack("fdk6", ("-X", -2.0), -0.6, 2.2, (0.55, 1.75), **kw),
    "drum": lambda kw: K.drum("fdk7", (11.0, -4.9, 0.80), "X", **kw),
    "drum_upright": lambda kw: K.drum("fdk8", (11.0, -5.9), z0=0.12, **kw),
    "fuel_cradle": lambda kw: K.fuel_cradle("fdk9", 11.0, -4.9, "X", 0.50, -0.90, **kw),
    "bench": lambda kw: K.bench("fdk10", 0.0, -3.05, "-Y", length=1.6, **kw),
    "bench_noback": lambda kw: K.bench("fdk11", 3.0, -3.05, "-Y", back=False, **kw),
}
FINE_PRE_PASS = {"flag_mast": ("1aa9682ff4db", 24), "flag_mast_plain": ("e71803687278", 32),
                 "post_sign": ("aa3aca715119", 30), "maze_fence": ("496e807d594c", 48),
                 "radio_mast": ("f9fde22fb5d2", 52), "toboggan_rack": ("18bf61e5fd92", 56),
                 "drum": ("08ab050f3a0d", 28), "drum_upright": ("03228db6d537", 28),
                 "fuel_cradle": ("f33f9cad1b94", 66), "bench": ("575401837b87", 48),
                 "bench_noback": ("3d238f957156", 36)}
FINE_TRIS = {"flag_mast": 62, "flag_mast_plain": 70, "post_sign": 48, "maze_fence": 104,
             "radio_mast": 70, "toboggan_rack": 64, "drum": 124, "drum_upright": 124,
             "fuel_cradle": 84, "bench": 108, "bench_noback": 72}


def check_fine_builders():
    for key, call in FINE_CALLS.items():
        d = call({})
        hd = _fine_hash(d)
        names_d = [(o.name, m) for o, m in d]
        _fine_drop(d)
        assert hd == FINE_PRE_PASS[key], f"fine: {key} default {hd} != pre-pass {FINE_PRE_PASS[key]}"
        f = call({"fine": True})
        hf = _fine_hash(f)
        zmin = min(min((o.matrix_world @ v.co).z for v in o.data.vertices) for o, _m in f)
        names_f = [(o.name, m) for o, m in f]
        _fine_drop(f)
        assert hf[1] == FINE_TRIS[key] and hf[0] != hd[0], f"fine: {key} fine {hf} (want {FINE_TRIS[key]} tris)"
        assert names_f == names_d, f"fine: {key} part names / matkeys {names_f} != {names_d}"
        assert key == "fuel_cradle" or zmin >= K.GROUND_LIFT - 1e-6, f"fine: {key} foot {zmin}"
    ok(f"fine builders (B.1): {len(FINE_CALLS)} default calls equal the pre-pass engine; fine=True "
       f"tris {FINE_TRIS}; same part names / matkeys; feet >= GROUND_LIFT")


def _closed_one_shell(o):
    """(shells, edges not shared by exactly two faces) of a mesh object."""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bad = sum(1 for e in bm.edges if len(e.link_faces) != 2)
    seen, shells = set(), 0
    for v in bm.verts:
        if v.index in seen:
            continue
        shells += 1
        stack = [v]
        while stack:
            w = stack.pop()
            if w.index in seen:
                continue
            seen.add(w.index)
            stack += [e.other_vert(w) for e in w.link_edges]
    bm.free()
    return shells, bad


def check_curved_braces():
    """Structure v1 round 2 (Richard 2026-10-18: "that break in brace frame ... it would need to be curved. You can
    see there is a slight gap"): a framing / curved truss brace and a curved bracket are ONE mitred beam each - one
    closed shell (every edge on exactly two faces: no gap or notch at any joint), CURVE_SEGS equal segments turning
    by equal angles (a circular arc), the section 0.7 x member square to the member in every segment, every corner
    edge parallel to its segment (exact mitre), the frame brace bulging away from the king post with both ends on the
    tie / rafter centre lines; each brace its own part (grain along it). Flag off: the old one-part truss."""
    clear()
    n = K.CURVE_SEGS
    m, span, eave, uc = 0.30, 5.6, 15.0, 0.0
    wall = WallRef("-X", -9.6, -3.0, 3.0)
    with K.structure(version=1):
        fr = K.truss_kingpost("cbf", wall, span, eave, 45.0, member=m, braces="frame", d=0.22, frame=(1.12, 1.75))
        cu = K.truss_kingpost("cbc", WallRef("+X", 5, -5, 5), 9, 6, 36, braces="curved")
        kb = K.bracket("cbk", (0.0, 0.0, 3.0), "-X", span=1.4, drop=1.2, t=0.22, curved=True)
    assert [o.name for o, _m in fr] == ["truss_cbf", "truss_cbfbr0", "truss_cbfbr1"], [o.name for o, _m in fr]
    assert [o.name for o, _m in cu] == ["truss_cbc", "truss_cbcbr0", "truss_cbcbr1"], [o.name for o, _m in cu]
    for o, _m in fr[1:] + cu[1:] + kb:
        sh, bad = _closed_one_shell(o)
        if "cbf" in o.name:       # the frame brace has no wall-facing side: open ONLY along that back plane
            assert sh == 1 and bad == 2 * n + 2, f"{o.name}: {sh} shell(s), {bad} open edge(s) (want {2 * n + 2})"
            assert len(o.data.polygons) == 3 * n + 2, (o.name, len(o.data.polygons))
            assert all(p.normal.dot(Vector((1, 0, 0))) < 0.9 for p in o.data.polygons), o.name   # none faces +X
            bm = bmesh.new()
            bm.from_mesh(o.data)
            back_x = -9.6 - 0.22 + 0.5 * 0.7 * m
            assert all(abs((o.matrix_world @ v.co).x - back_x) < 1e-4
                       for e in bm.edges if len(e.link_faces) == 1 for v in e.verts), o.name
            bm.free()
        else:
            assert sh == 1 and bad == 0, f"{o.name}: {sh} shell(s), {bad} open / non-manifold edge(s)"
            assert len(o.data.polygons) == 4 * n + 2, (o.name, len(o.data.polygons))
        V = [o.matrix_world @ v.co for v in o.data.vertices]          # rings of 4, in build order
        rings = [V[4 * i:4 * i + 4] for i in range(n + 1)]
        C = [sum(r, Vector()) / 4.0 for r in rings]
        D = [(q - p) for p, q in zip(C, C[1:])]
        lens = [d.length for d in D]
        turns = [math.degrees(a.angle(b)) for a, b in zip(D, D[1:])]
        if o.name.startswith("bracket_"):         # the kit bracket is an elliptic quarter (span x drop): smooth only
            assert all(0.5 < t_ < 20.0 for t_ in turns), (o.name, turns)
        else:                                     # a brace is a circular arc: equal chords, equal turns
            assert max(lens) - min(lens) < 1e-5, (o.name, lens)
            assert max(turns) - min(turns) < 1e-3 and 0.5 < turns[0] < 20.0, (o.name, turns)
        w = (0.22 if o.name.startswith("bracket_") else 0.7 * (m if "cbf" in o.name else 0.35))
        for i, d in enumerate(D):
            dn = d.normalized()
            for k in range(4):
                e = rings[i + 1][k] - rings[i][k]
                assert e.cross(dn).length < 1e-5 * max(1.0, e.length), (o.name, i, k)   # exact mitre
            for k in range(3):
                s = rings[i][k + 1] - rings[i][k]
                s = s - dn * s.dot(dn)
                assert abs(s.length - w) < 1e-4, (o.name, i, k, s.length, w)        # w x w square to the member
    # the frame brace: ends on the tie / rafter centre lines, the arc bulges away from the king post
    tp = 1.0
    for k, (o, _m) in enumerate(fr[1:]):
        V = [o.matrix_world @ v.co for v in o.data.vertices]
        a = sum(V[0:4], Vector()) / 4.0
        b = sum(V[4 * n:4 * n + 4], Vector()) / 4.0
        mid = sum(V[4 * (n // 2):4 * (n // 2) + 4], Vector()) / 4.0
        sg = -1.0 if k == 0 else 1.0
        ua, ub = sg * 1.12, sg * 1.75
        ub_w = -b.y                                   # -X wall: u = -y
        assert abs(a.z - eave) < 1e-6 and abs(-a.y - ua) < 1e-6 and abs(a.x - (-9.6 - 0.22)) < 1e-6, a
        assert abs(ub_w - ub) < 1e-6 and abs(b.z - (eave + tp * (span / 2 - abs(ub)))) < 1e-6, b
        chord_mid = (a + b) * 0.5
        assert abs(-mid.y - uc) > abs(-chord_mid.y - uc) + 0.05 and mid.z < chord_mid.z, (mid, chord_mid)
    # flag off: one part, the old kinked geometry (the builders' REF face count)
    old = K.truss_kingpost("cbo", wall, span, eave, 45.0, member=m, braces="frame", d=0.22, frame=(1.12, 1.75))
    assert len(old) == 1 and len(old[0][0].data.polygons) == 6 * 8, len(old[0][0].data.polygons)
    ok(f"curved braces (structure v1): frame + curved truss braces and the curved bracket are one closed mitred "
       f"shell each ({n} equal segments, equal turns, 0.7 x member square section, exact mitres), own parts; the "
       f"frame brace bulges away from the king post, ends on the tie / rafter centre lines; flag off unchanged")


def main():
    t0 = time.perf_counter()
    check_curved_braces()
    built = check_builders()
    check_gear_bindings()
    check_fine_builders()
    check_windows_roofs(built)
    check_rooms()
    check_cull()
    check_materials_colour()
    check_lods()
    check_packer()
    check_prefilter_and_alias()
    print(f"PASS test_bldg_kit: {len(CHECKS)} check groups ({time.perf_counter() - t0:.1f} s)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_bldg_kit")
        sys.exit(1)
