"""
Chairlift terminal generator v2 - drive station (bottom) and return station (top).

v2: real CC0 PBR textures via the 2x2 atlas (cs2_asset.make_texture_set_atlas), per-part UV
quadrants, lowered machinery housing (v1 read too tall in game), chunkier columns, and the
bullwheel visibly exposed under the housing with a shaft gap.

J: the built-in entry towers (and their droop-anchor ropes) left both builders - each
station's tower is now a HOSTED SUB-OBJECT prefab-side (SkiLiftTowerET68 at the drive,
SkiLiftTowerET62 at the return, standing at x -6.9 / -4.9 with the anchor at axle height
5.84 / 5.24). The interior static ropes keep their exact start points, which is the whole
contract between the two halves. Stations also moved to a 4096 atlas (quad_size=2048).

Atlas quadrants:
    0 concrete   (pad)                          not recolourable
    1 galvanized (columns, shaft, cabinet)      not recolourable
    2 plates     (bullwheel)                    not recolourable
    3 painted    (housing, hut)                 RECOLOURABLE (ControlMask R = 1)

    blender --background --factory-startup --python lift_terminal.py -- --out ../import --kind drive --preview ../preview

Import contract as ever (SPEC 11.0): base-pivoted, +X travel pre-conversion, no underscores.
"""

import bpy
import bmesh
import math
import os
import sys
import argparse
from dataclasses import dataclass

import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402
import lift_tower  # noqa: E402  (reuses its preview camera helpers)
import lift_chair  # noqa: E402  (a chair is baked into each terminal at load/unload)

TEX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "textures")

QUAD_CONCRETE, QUAD_GALV, QUAD_GLASS, QUAD_PAINT = 0, 1, 2, 3
# The atlas definition moved to cs2_asset.lift_atlas_quads so towers and chairs share the
# exact same palette and recolour slot. Concrete is tinted down there (rendered near-white
# in game), painted steel stays the dark Pali look.
QUAD_SETS, CONTROL_QUADS = cs2_asset.lift_atlas_quads(TEX)


@dataclass
class TerminalParams:
    rope_height: float = 7.04
    wheel_radius: float = 1.8
    wheel_depth: float = 0.4
    wheel_segments: int = 28

    pad: tuple = (9.0, 6.0, 0.3)
    col_x: float = 2.6
    col_size: float = 0.7
    housing: tuple = (5.6, 3.4, 1.3)
    housing_gap: float = 0.26          # daylight between wheel top and housing underside
    hut: tuple = (2.0, 1.8, 2.4)

    r_pad: tuple = (6.5, 5.0, 0.3)
    r_col_x: float = 2.2
    r_col_size: float = 0.6
    r_housing: tuple = (4.2, 2.6, 1.0)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def add_box(name, size, center):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=center)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return o


def add_cyl(name, radius, depth, center, segments=16, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments, radius=radius, depth=depth,
                                        location=center, rotation=rotation)
    o = bpy.context.active_object
    o.name = name
    return o


def add_wedge(name, x0, x1, width, z0, z1):
    """Vertical-sided ramp block: flat bottom at z=0, top sloping z0 (at x0) -> z1 (at x1)."""
    hw = width / 2.0
    m = bpy.data.meshes.new(name)
    m.from_pydata(
        [(x0, -hw, 0), (x0, hw, 0), (x1, hw, 0), (x1, -hw, 0),
         (x0, -hw, z0), (x0, hw, z0), (x1, hw, z1), (x1, -hw, z1)],
        [],
        [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
         (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)])
    m.validate()
    o = bpy.data.objects.new(name, m)
    bpy.context.collection.objects.link(o)
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    return o


def add_strut(name, p0, p1, radius, segments=8):
    """Cylinder spanning two arbitrary 3D points - for cables that angle in both axes."""
    a, b = mathutils.Vector(p0), mathutils.Vector(p1)
    d = b - a
    o = add_cyl(name, radius, d.length, tuple((a + b) / 2.0), segments=segments)
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    return o


def add_droop_rope(name, p0, p1, radius, sag, segs=5):
    """
    Rope between two points with parabolic droop (user direction, I-generation): our
    static haul-rope pieces sag like the game's own cables, so the free-air anchor
    junction reads as one continuous catenary instead of a kink. Returns a list of
    strut objects approximating the curve.
    """
    a, b = mathutils.Vector(p0), mathutils.Vector(p1)
    pts = []
    for i in range(segs + 1):
        t = i / segs
        p = a.lerp(b, t)
        p.z -= sag * 4.0 * t * (1.0 - t)
        pts.append(p)
    return [add_strut(f"{name}{i}", tuple(pts[i]), tuple(pts[i + 1]), radius, segments=6)
            for i in range(segs)]


def build_spoked_wheel(cx, cy, cz, prefix=""):
    """
    Bullwheel the way the Pali photos actually show one (liftblog img_6900: the summit
    wheel seen from below): a deep dark RIM ring with a light galvanized SPOKE web and
    hub - you can see straight through it. The old flat cylinder read as a dinner plate.
    Rim outer edge sits at r=1.80 so the rope torus (r 1.82) lies proud in the groove and
    chair grips at radial 1.84 stay clear, exactly as before.
    """
    parts = []
    bpy.ops.mesh.primitive_torus_add(major_radius=1.66, minor_radius=0.14,
                                     major_segments=24, minor_segments=6,
                                     location=(cx, cy, cz))
    rim = bpy.context.active_object
    rim.name = f"{prefix}wheelrim"
    parts.append((rim, QUAD_PAINT))
    for k in range(8):
        a = k * math.pi / 4.0
        sp = add_box(f"{prefix}spoke{k}", (1.34, 0.09, 0.055),
                     (cx + 0.97 * math.cos(a), cy + 0.97 * math.sin(a), cz))
        sp.rotation_euler = (0.0, 0.0, a)
        parts.append((sp, QUAD_GALV))
    parts.append((add_cyl(f"{prefix}wheelhub", 0.34, 0.62, (cx, cy, cz),
                          segments=12), QUAD_GALV))
    return parts


# --------------------------------------------------------------------------------------
# The painted tension scale (liftblog img_6828/6870: "1 2 3 ... 16" in white along the
# rail tube - the carriage position indicator every Yan drive-tension terminal carries).
# The digits are stamped into a thin strip at the top edge of the PAINT quadrant of this
# terminal's atlas copy; two thin plates on the rails UV-map exactly onto the strip.
# --------------------------------------------------------------------------------------

SCALE_U0, SCALE_U1 = 0.505, 0.995
SCALE_V0, SCALE_V1 = 2028.0 / 2048.0, 2047.0 / 2048.0

_FONT = {   # 3x5 digit bitmaps, row 0 = top
    "0": ("111", "101", "101", "101", "111"),
    "1": ("010", "110", "010", "010", "111"),
    "2": ("111", "001", "111", "100", "111"),
    "3": ("111", "001", "111", "001", "111"),
    "4": ("101", "101", "111", "001", "001"),
    "5": ("111", "100", "111", "001", "111"),
    "6": ("111", "100", "111", "101", "111"),
    "7": ("111", "001", "010", "010", "010"),
    "8": ("111", "101", "111", "101", "111"),
    "9": ("111", "101", "111", "001", "111"),
}


def stamp_tension_scale(base, nrm, msk, ctl, A):
    """
    make_texture_set_atlas `post` hook: paint 1..16 in white into the scale strip.
    The pixel buffer is bottom-up, so higher rows = higher V. ControlMask R is zeroed
    under every painted texel so the digits stay white when the player recolours the
    black steel (they are painted markings, not steel).

    Every pixel measure scales with `sc` (J: the stations moved to a 4096 atlas, which
    doubles the strip's pixel height - without this the digits shrank to half the strip
    and sat at its bottom edge).
    """
    white = (0.85, 0.86, 0.88)
    tick = (0.62, 0.63, 0.66)
    sc = max(1, A // 2048)                   # glyph scale: 1 at the old 2048 atlas
    c0, c1 = int(SCALE_U0 * A), int(SCALE_U1 * A)
    r0 = int(SCALE_V0 * A)
    n = 16
    cell = (c1 - c0) / float(n)
    for i in range(1, n + 1):
        s = str(i)
        h = (8 if len(s) == 1 else 6) * sc   # horizontal texels per font pixel
        wpx = (len(s) * 3 + (len(s) - 1)) * h
        x0 = int(c0 + (i - 1) * cell + (cell - wpx) / 2.0)
        y0 = r0 + 2 * sc
        for gi, ch in enumerate(s):
            glyph = _FONT[ch]
            gx = x0 + gi * 4 * h
            for row in range(5):
                for col in range(3):
                    if glyph[row][col] == "1":
                        rr = y0 + (4 - row) * 3 * sc
                        base[rr:rr + 3 * sc, gx + col * h:gx + (col + 1) * h, 0:3] = white
                        ctl[rr:rr + 3 * sc, gx + col * h:gx + (col + 1) * h, 0] = 0.0
        tx = int(c0 + (i - 1) * cell)
        base[r0:r0 + 4 * sc, tx:tx + 2 * sc, 0:3] = tick
        ctl[r0:r0 + 4 * sc, tx:tx + 2 * sc, 0] = 0.0


def set_plate_uvs(obj, outboard, ascending):
    """
    Map the plate's outboard face onto the tension-scale strip; every other face lands
    on a quiet texel of the paint quadrant. `outboard` is the sign of the face normal's
    Y; `ascending` makes the numbers run with +X or -X so each side of the terminal
    reads left-to-right from its own viewpoint (the real machine is painted per side).
    """
    mesh = obj.data
    uv = mesh.uv_layers.active or mesh.uv_layers.new()
    xs = [v.co.x for v in mesh.vertices]
    zs = [v.co.z for v in mesh.vertices]
    x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
    for poly in mesh.polygons:
        if poly.normal.y * outboard > 0.5:
            for li in poly.loop_indices:
                co = mesh.vertices[mesh.loops[li].vertex_index].co
                t = (co.x - x0) / (x1 - x0)
                if not ascending:
                    t = 1.0 - t
                s = (co.z - z0) / (z1 - z0)
                uv.data[li].uv = (SCALE_U0 + t * (SCALE_U1 - SCALE_U0),
                                  SCALE_V0 + s * (SCALE_V1 - SCALE_V0))
        else:
            for li in poly.loop_indices:
                co = mesh.vertices[mesh.loops[li].vertex_index].co
                # Tiny NON-DEGENERATE patch on quiet paint texels (was a single point:
                # zero-area islands upset tangent export and, since the finish pass,
                # pack_islands during the unique repack).
                uv.data[li].uv = (0.555 + 0.008 * (co.x - x0) / max(x1 - x0, 1e-6),
                                  0.555 + 0.008 * (co.z - z0) / max(z1 - z0, 1e-6))


# Boxy parts that take the finishing-pass bevel. Glass bands, the rope torus and the
# baked chairs are excluded (chairs are lift_chair's domain and stay untouched).
BEVEL_PREFIXES = ("pad", "skirt", "beam_", "saddle_", "endmember_", "carr_",
                  "machfloor", "machinebox", "boxroof", "cabfloor", "cabin", "cabroof",
                  "hutbase", "hut", "hutroof", "cabinet", "legfoot_", "bankmount_",
                  "plinth", "crest", "rampup", "rampdown")
BEVEL_EXCLUDE = ("hutwin", "cabwin")


def apply_finish_bevels(parts):
    """0.02 m single-segment bevel on the boxy steel/concrete, applied BEFORE unwrap."""
    for obj, _ in parts:
        if obj.name.startswith(BEVEL_EXCLUDE) or not obj.name.startswith(BEVEL_PREFIXES):
            continue
        mod = obj.modifiers.new("Bevel", type="BEVEL")
        mod.width = 0.02
        mod.segments = 1
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(45.0)
        mod.use_clamp_overlap = True
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)


def build_return_simple(p: TerminalParams, finish=False):
    """
    Top station at ski-off height. The rope lane crosses the whole line at 7.04 m, so the
    mesh models the BREAKOVER (liftblog: 'a fixed bullwheel at the breakover point'): the
    rope dives from line height at the mesh edge down to a low fixed bullwheel, and the
    ground rises to meet the chair - a built-up unload knoll like every real top station.
    Chair seat arrives ~0.6 m above the knoll crest: stand up, ski off down the run-out.
    The old angled strut is gone: at this height its only footing would be in the middle
    of the unload run-out, so the wheel sits on a stout centre mast on the crest instead
    (inside the chairs' swing circle, radius < 0.6 m).
    """
    parts = []
    rope_y = 1.8
    wheel_z = 3.7                     # rope over the wheel: seat ~2.05 m, crest 1.42 m
    crest_z = 1.42
    mound_w = 5.4                     # spans both rope lines plus chair overhang

    # Unload knoll: up-ramp facing the line, crest deck under the wheel, run-out down back.
    # E: shortened both ramps (footprint was sprawling on slopes) and added a FOUNDATION
    # SKIRT below y=0 - with the no-flatten terraform the ground is never levelled, so on
    # steep terrain the downhill edge used to hang in the air. The skirt buries into the
    # hill instead (the tower's sunken pad proved below-origin geometry renders fine).
    parts.append((add_wedge("rampup", -4.6, -1.1, mound_w, 0.02, crest_z), QUAD_CONCRETE))
    parts.append((add_box("crest", (2.4, mound_w, crest_z),
                          (0.1, 0, crest_z / 2.0)), QUAD_CONCRETE))
    parts.append((add_wedge("rampdown", 1.3, 2.6, mound_w, crest_z, 0.02), QUAD_CONCRETE))
    parts.append((add_box("skirt", (7.2, mound_w, 3.0), (-1.0, 0, -1.5)), QUAD_CONCRETE))

    # Stout centre mast on a plinth. Liftblog img_6900 (the real Pali summit): one FAT
    # clean tube, no gussets, wheel right on top - so the old thin mast+fin cluster is
    # replaced by a thicker tube with a collar flange under the wheel bearing.
    # Everything stays inside r=0.6 of centre - the chairs' swing circle.
    parts.append((add_box("plinth", (0.9, 0.9, 0.35),
                          (0, 0, crest_z + 0.175)), QUAD_CONCRETE))
    parts.append((add_cyl("mast", 0.42, wheel_z - 0.17 - crest_z,
                          (0, 0, crest_z + (wheel_z - 0.17 - crest_z) / 2.0),
                          segments=14), QUAD_PAINT))
    parts.append((add_cyl("mastcollar", 0.52, 0.18,
                          (0, 0, wheel_z - 0.38), segments=14), QUAD_PAINT))

    # The fixed bullwheel at the breakover - spoked, because from the unload you look
    # straight up through it (img_6900 shows the light spoke web against the dark rim)
    parts.extend(build_spoked_wheel(0, 0, wheel_z, prefix="t"))

    # J-generation: the built-in entry tower and its droop-anchor ropes are GONE - a
    # hosted sub-object tower (SkiLiftTowerET62, same train geometry) stands at
    # px = -4.9 prefab-side and carries the cable anchor. Only the interior static
    # rope remains ours, starting exactly where that tower's trailing sheaves meet the
    # line (px - cant + 0.75 at axle_z + 0.12) - see the drive-side comment.
    px = -4.9                               # hosted tower base (prefab-side contract)
    tp = lift_tower.TowerParams(mast_height=6.2)
    axle_z = tp.sheave_axle_height          # 5.24 - HARD INVARIANT, the anchor height
    cant = 1.0                              # its trains hang 1.0 m line-ward (G, slope fix)
    for ry in (-rope_y, rope_y):
        d_x0, d_z0 = px - cant + 0.75, axle_z + 0.12
        tan_x, tan_z = -0.35, wheel_z + 0.05
        ddx, ddz = tan_x - d_x0, tan_z - d_z0
        dlen = math.sqrt(ddx * ddx + ddz * ddz)
        dang = math.atan2(ddz, ddx)
        parts.append((add_cyl(f"divrope_{ry}", 0.055, dlen,
                              ((d_x0 + tan_x) / 2.0, ry, (d_z0 + tan_z) / 2.0), segments=8,
                              rotation=(0, math.radians(90) - dang, 0)), QUAD_PAINT))
    bpy.ops.mesh.primitive_torus_add(major_radius=1.82, minor_radius=0.055,
                                     major_segments=28, minor_segments=6,
                                     location=(0, 0, wheel_z))
    _torus = bpy.context.active_object
    _torus.name = "wheelrope"
    parts.append((_torus, QUAD_PAINT))

    # An arriving chair right at the breakover - riders about to stand up and ski off.
    # Grip ON the modelled dive rope at its x (rope z 3.86 here, not wheel height - at
    # 3.7 the grip floated 15 cm below the rope); the footrest skims the crest deck by
    # centimetres, exactly like a real unload.
    parts.extend(lift_chair.build_chair_parts(lift_chair.ChairParams(),
                                              offset=(-0.6, -rope_y, 3.86),
                                              prefix="uchair", facing=1))
    # ...and an empty chair mid-dive on the departure side, heading back down the hill
    # (the game's chairs stop at the portal anchor - the static segment rides alone).
    parts.extend(lift_chair.build_chair_parts(lift_chair.ChairParams(),
                                              offset=(-2.5, rope_y, 4.66),
                                              prefix="dchair", facing=-1))

    # Liftie box on flat ground beside the crest, sightline down the unload
    hut = (1.8, 1.6, 2.3)
    hx, hy = -1.3, mound_w / 2.0 + hut[1] / 2.0 + 0.7
    parts.append((add_box("hutbase", (hut[0] + 0.3, hut[1] + 0.3, 0.15),
                          (hx, hy, 0.075)), QUAD_CONCRETE))
    # K: the hut footprint gets its OWN below-grade skirt, same 3 m depth as the main
    # structure's - it stands off the knoll on raw terrain, and with the no-flatten
    # terraform the box floated on its 15 cm base wherever the ground dropped.
    parts.append((add_box("hutskirt", (hut[0] + 0.3, hut[1] + 0.3, 3.0),
                          (hx, hy, -1.5)), QUAD_CONCRETE))
    parts.append((add_box("hut", hut, (hx, hy, 0.15 + hut[2] / 2.0)), QUAD_PAINT))
    parts.append((add_box("hutwin", (hut[0] + 0.05, hut[1] + 0.05, 0.7),
                          (hx, hy, 0.15 + hut[2] * 0.62)), QUAD_GLASS))
    parts.append((add_box("hutroof", (hut[0] + 0.2, hut[1] + 0.2, 0.08),
                          (hx, hy, 0.15 + hut[2] + 0.04)), QUAD_GALV))
    # Weather mast on the hut roof (img_6900: anemometer pole over the summit hut)
    wm_z = 0.15 + hut[2] + 0.08
    parts.append((add_cyl("wxmast", 0.025, 1.3,
                          (hx - 0.55, hy - 0.45, wm_z + 0.65), segments=6), QUAD_GALV))
    parts.append((add_cyl("wxarm", 0.018, 0.36,
                          (hx - 0.55, hy - 0.45, wm_z + 1.24),
                          rotation=(math.radians(90), 0, 0), segments=6), QUAD_GALV))
    parts.append((add_box("wxbox", (0.09, 0.09, 0.14),
                          (hx - 0.55, hy - 0.45, wm_z + 1.36)), QUAD_GALV))

    if finish:
        apply_finish_bevels(parts)
    for obj, quad in parts:
        cs2_asset.unwrap_into_quadrant(obj, quad)
    bpy.ops.object.select_all(action="DESELECT")
    for obj, _ in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    t = bpy.context.active_object
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    cs2_asset.orient_for_net(t)
    return t


def build_terminal_pali(p: TerminalParams, kind: str, finish=False):
    """
    Drive terminal after the Pallavicini reference photos (liftblog img_6828/6866/6870):
    a LOW, LONG, HEAVY machine. Twin fat tube rails (round end caps, box end members)
    ride four fat column legs; the bullwheel hangs from a tension CARRIAGE riding those
    rails; the machinery box sits directly over the wheel and the windowed operator cabin
    on top toward the back (img_6828: corrugated box, wrap-around glass, catwalk with
    railing, stovepipe, ladder up the back). The haul rope dives from the hosted entry
    tower (a prefab-side sub-object since J - see the comment at px below),
    passes UNDER depression-sheave banks hung from the front cross beam - rope contact at
    the sheave BOTTOM, exactly as img_6866 shows - then runs to the wheel tangent and
    wraps. The counterweight (img_6867: fat black cylinder drum) hangs under the rails
    behind the carriage, cabled over the back end member: the "tension" in Yan
    drive-tension. White 1..16 tension scale painted along both rails (img_6828), via
    stamp_tension_scale + set_plate_uvs. Line side is -X (maps to CS2 -Z, where the
    building's connection stub sits).
    """
    drive = (kind == "drive")
    if not drive:
        return build_return_simple(p, finish=finish)
    parts = []

    rope_y = 1.8                     # lane offset, as everywhere
    wheel_x, wheel_z = 0.2, 3.5      # bullwheel centre - loading height (INVARIANT)
    rail_len = 10.0                  # frame front at -5.0, back at +5.0
    rail_y, rail_r, rail_z = 1.25, 0.30, 4.95
    leg_y = 3.35                     # legs fully outside the chair corridor (y 0.6..3.0)
    front_x, back_x = -4.3, 4.0      # portal (leg pair) planes
    deck_z = rail_z + 0.35           # top-of-rail deck the boxes stand on
    pad = (11.0, 7.6, 0.3)

    parts.append((add_box("pad", pad, (0, 0, pad[2] / 2.0)), QUAD_CONCRETE))
    # Foundation skirt below y=0: buries into un-flattened slopes (see build_return_simple)
    parts.append((add_box("skirt", (pad[0], pad[1], 3.0), (0, 0, -1.5)), QUAD_CONCRETE))

    # FOUR fat tube legs (img_6870: ~0.6 m round columns, not lattice), one pair per
    # portal plane, standing outside the corridor, each pair tied by a transverse box
    # beam whose top carries the rails. Saddle blocks make the rail/beam joints read
    # heavy instead of tangent.
    beam_z = 4.55                    # beam centre; beam spans z 4.35..4.75
    for lx in (front_x, back_x):
        for ly in (-leg_y, leg_y):
            parts.append((add_cyl(f"leg_{lx:.0f}_{ly:.0f}", 0.30, beam_z - 0.2 - pad[2],
                                  (lx, ly, pad[2] + (beam_z - 0.2 - pad[2]) / 2.0),
                                  segments=14), QUAD_PAINT))
            parts.append((add_box(f"legfoot_{lx:.0f}_{ly:.0f}", (0.85, 0.85, 0.14),
                                  (lx, ly, pad[2] + 0.07)), QUAD_PAINT))
        parts.append((add_box(f"beam_{lx:.0f}", (0.5, leg_y * 2 + 0.7, 0.4),
                              (lx, 0, beam_z)), QUAD_PAINT))
        for ry in (-rail_y, rail_y):
            parts.append((add_box(f"saddle_{lx:.0f}_{ry:.0f}", (0.6, 0.44, 0.3),
                                  (lx, ry, beam_z + 0.25)), QUAD_PAINT))

    # Twin FAT tube rails - the low long chassis that IS the Pali silhouette - with
    # proud round end caps (img_6870: open tube ends with cap flanges) and box end
    # members tying the pair together at both ends. Small lifting lugs on top at the
    # ends, straight off img_6828.
    for ry in (-rail_y, rail_y):
        parts.append((add_cyl(f"rail_{ry:.0f}", rail_r, rail_len, (0, ry, rail_z),
                              segments=16, rotation=(0, math.radians(90), 0)), QUAD_PAINT))
        for ex in (-rail_len / 2, rail_len / 2):
            parts.append((add_cyl(f"railcap_{ry:.0f}_{ex:.0f}", rail_r + 0.05, 0.10,
                                  (ex, ry, rail_z), segments=16,
                                  rotation=(0, math.radians(90), 0)), QUAD_PAINT))
            parts.append((add_box(f"raillug_{ry:.0f}_{ex:.0f}", (0.10, 0.05, 0.16),
                                  (ex - math.copysign(0.3, ex), ry, rail_z + rail_r + 0.07)),
                          QUAD_PAINT))
    for ex in (-4.6, 4.6):
        parts.append((add_box(f"endmember_{ex:.0f}", (0.35, rail_y * 2 + 0.3, 0.35),
                              (ex, 0, rail_z)), QUAD_PAINT))

    # Tension carriage: two saddles riding the rails, a yoke slung between them, and the
    # fat wheel shaft dropping to the bullwheel. On the real machine this whole trolley
    # slides along the rails as rope tension changes - which is what the painted scale
    # (below) measures.
    parts.append((add_box("carr_yoke", (0.6, rail_y * 2, 0.35),
                          (wheel_x, 0, 4.45)), QUAD_PAINT))
    for ry in (-rail_y, rail_y):
        parts.append((add_box(f"carr_saddle_{ry:.0f}", (1.7, 0.42, 0.55),
                              (wheel_x, ry, rail_z - 0.05)), QUAD_PAINT))
    parts.append((add_cyl("wheelshaft", 0.17, 4.45 - wheel_z + 0.2,
                          (wheel_x, 0, (4.45 + wheel_z) / 2.0 + 0.1), segments=10), QUAD_PAINT))

    # Spoked bullwheel (see build_spoked_wheel - liftblog img_6900) + haul rope torus
    # seated in its groove. Rim outer edge r=1.80, rope torus 1.82: INVARIANT.
    parts.extend(build_spoked_wheel(wheel_x, 0, wheel_z))
    bpy.ops.mesh.primitive_torus_add(major_radius=1.82, minor_radius=0.055,
                                     major_segments=28, minor_segments=6,
                                     location=(wheel_x, 0, wheel_z))
    _torus = bpy.context.active_object
    _torus.name = "wheelrope"
    parts.append((_torus, QUAD_PAINT))

    # Counterweight drum under the rails behind the carriage (img_6867: a fat black
    # cylinder on cables). Cables run from the carriage yoke back along the frame, over
    # the back end member and down to the drum. Drum nearest point sits 3.3 m from the
    # wheel centre - outside the chairs' swing annulus around the wrap (max 2.9 m).
    cw_x, cw_top = 4.2, 2.85
    parts.append((add_cyl("cweight", 0.72, 1.5, (cw_x, 0, cw_top - 0.75),
                          segments=16), QUAD_PAINT))
    parts.append((add_box("cwplate", (0.9, 0.9, 0.10), (cw_x, 0, cw_top + 0.05)), QUAD_PAINT))
    for s in (-1, 1):
        parts.append((add_strut(f"tcable_a_{s}", (wheel_x + 0.35, s * 0.5, 4.55),
                                (4.42, s * 0.5, 4.80), 0.030), QUAD_PAINT))
        parts.append((add_strut(f"tcable_b_{s}", (4.60, s * 0.45, 4.80),
                                (cw_x + 0.05, s * 0.45, cw_top), 0.030), QUAD_PAINT))

    # J-generation: the built-in ENTRY TOWER is GONE. A hosted sub-object tower (the
    # standalone SkiLiftTowerET68 asset, same sheave-train geometry) now stands at
    # px = -6.9 prefab-side, and it also carries the game's cable anchor - so the F/G/I
    # etpad/etmast/etarm/cantilever/droop-anchor mesh all left this builder. What
    # REMAINS ours is the interior static rope, and its start point is untouched: it
    # still begins exactly where that tower's trailing sheaves meet the line
    # (d_x0 = px - cant + 0.75 at axle_z + 0.12), so the rope lands in the hosted
    # tower's wheels without either side knowing about the other.
    px = -6.9                               # hosted tower base (prefab-side contract)
    tp = lift_tower.TowerParams(mast_height=6.8)
    axle_z = tp.sheave_axle_height          # 5.84 - HARD INVARIANT, the anchor height
    cant = 1.0                              # its trains hang 1.0 m line-ward (G, slope fix)

    # Two-angle rope, the way img_6866 actually shows it: from the entry tower the rope
    # DIVES steeply to a depression-sheave bank hung from the UNDERSIDE of the front
    # cross beam (rope contact at sheave BOTTOM - the sheaves press the rope down), then
    # runs nearly level to the bullwheel tangent. Angled guard plates flank each bank.
    bank_z = 4.28                    # sheave axle height; rope centre below at 4.06
    rope_at_bank = bank_z - 0.17 - 0.05
    tan_x, tan_z = wheel_x - 0.1, wheel_z + 0.05
    d_x0, d_z0 = px - cant + 0.75, axle_z + 0.12
    for ry in (-rope_y, rope_y):
        parts.append((add_box(f"bankmount_{ry:.0f}", (0.55, 0.14, 0.35),
                              (front_x, ry, bank_z + 0.28)), QUAD_PAINT))
        for sxo in (-0.2, 0.2):
            parts.append((add_cyl(f"banksheave_{ry:.0f}_{sxo}", 0.17, 0.13,
                                  (front_x + sxo, ry, bank_z), segments=10,
                                  rotation=(math.radians(90), 0, 0)), QUAD_GALV))
        guard = add_box(f"ropeguard_{ry:.0f}", (0.75, 0.04, 0.26),
                        (front_x, ry + math.copysign(0.13, ry), bank_z + 0.02))
        guard.rotation_euler = (0, math.radians(25), 0)
        parts.append((guard, QUAD_PAINT))
        # segment A: entry-tower axle down to the front of the bank (steep - the Pali
        # dive really is ~50 degrees, img_6866)
        parts.append((add_strut(f"divropea_{ry:.0f}", (d_x0, ry, d_z0),
                                (front_x - 0.22, ry, rope_at_bank + 0.01), 0.055), QUAD_PAINT))
        # segment B: under the bank, nearly level, out to the wheel tangent
        parts.append((add_strut(f"divropeb_{ry:.0f}", (front_x - 0.24, ry, rope_at_bank),
                                (tan_x, ry, tan_z), 0.055), QUAD_PAINT))

    # Rope height on segment B at a given x - chairs bake with their grips exactly here
    def rope_b_z(x):
        t = (x - (front_x - 0.24)) / (tan_x - (front_x - 0.24))
        return rope_at_bank + t * (tan_z - rope_at_bank)

    # A chair at the load point: just off the wheel wrap, riders facing the line (-X),
    # about to head uphill. Grip ON the modelled rope, clear of the rim (radial 1.84 m
    # vs rim 1.80) and of the carriage above.
    parts.extend(lift_chair.build_chair_parts(lift_chair.ChairParams(),
                                              offset=(-0.2, rope_y, rope_b_z(-0.2)),
                                              prefix="lchair", facing=-1))
    # ...and one on the arrival side under the frame: the game's own chairs stop at the
    # tower anchor, so the static rope between tower and wheel needs its own riders.
    parts.extend(lift_chair.build_chair_parts(lift_chair.ChairParams(),
                                              offset=(-2.0, -rope_y, rope_b_z(-2.0)),
                                              prefix="achair", facing=1))

    # -------- topside: machinery box over the wheel, operator cabin at the back -------
    # Machinery box directly above the carriage/wheel (drive machinery drives the wheel
    # shaft - vents + hatch, no glazing; locked design decision). Floor plates close the
    # gap down to the rails.
    box = (3.2, 2.4, 1.9)
    bx = 0.3
    parts.append((add_box("machfloor", (box[0] + 0.1, box[1] + 0.1, 0.12),
                          (bx, 0, deck_z - 0.06)), QUAD_PAINT))
    parts.append((add_box("machinebox", box, (bx, 0, deck_z + box[2] / 2.0)), QUAD_PAINT))
    parts.append((add_box("hatch", (0.06, 0.8, 1.3),
                          (bx - box[0] / 2 - 0.01, 0.3, deck_z + 0.75)), QUAD_GALV))
    for i in range(3):
        parts.append((add_box(f"boxvent_{i}", (0.05, box[1] * 0.5, 0.09),
                              (bx + box[0] / 2 + 0.01, 0, deck_z + 0.5 + i * 0.24)), QUAD_GALV))
    parts.append((add_box("boxroof", (box[0] + 0.25, box[1] + 0.25, 0.09),
                          (bx, 0, deck_z + box[2] + 0.045)), QUAD_GALV))

    # Operator cabin ON the frame toward the back (img_6828/6870: corrugated black box,
    # wrap-around glass band, roof overhang, stovepipe) - in ADDITION to the ground hut.
    cab = (2.2, 2.5, 2.05)
    cx = 3.4
    parts.append((add_box("cabfloor", (cab[0] + 0.1, cab[1] + 0.1, 0.12),
                          (cx, 0, deck_z - 0.06)), QUAD_PAINT))
    parts.append((add_box("cabin", cab, (cx, 0, deck_z + cab[2] / 2.0)), QUAD_PAINT))
    cab_band_z = deck_z + cab[2] * 0.62
    parts.append((add_box("cabwin", (cab[0] + 0.05, cab[1] + 0.05, 0.72),
                          (cx, 0, cab_band_z)), QUAD_GLASS))
    for mx, my in ((-1, -1), (-1, 1), (1, -1), (1, 1)):
        parts.append((add_box(f"cabmull_{mx}{my}", (0.07, 0.07, 0.72),
                              (cx + mx * cab[0] / 2.0, my * cab[1] / 2.0, cab_band_z)),
                      QUAD_GALV))
    parts.append((add_box("cabroof", (cab[0] + 0.24, cab[1] + 0.24, 0.09),
                          (cx, 0, deck_z + cab[2] + 0.045)), QUAD_GALV))
    parts.append((add_cyl("stovepipe", 0.055, 0.85,
                          (cx - 0.85, -0.85, deck_z + cab[2] + 0.5), segments=8), QUAD_GALV))

    # Catwalk plank along the +Y side of the deck with a thin railing (img_6828: the
    # operator walks the frame between ladder and cabin).
    parts.append((add_box("catwalk", (6.8, 0.55, 0.06), (1.6, 1.73, deck_z - 0.03)),
                  QUAD_GALV))
    for i, rx in enumerate((-1.6, 0.0, 1.6, 3.2, 4.8)):
        parts.append((add_cyl(f"railpost_{i}", 0.025, 0.95,
                              (rx, 1.97, deck_z + 0.475), segments=6), QUAD_GALV))
    for hz in (deck_z + 0.55, deck_z + 0.93):
        parts.append((add_strut(f"handrail_{hz:.1f}", (-1.7, 1.97, hz),
                                (4.9, 1.97, hz), 0.028, segments=6), QUAD_GALV))

    # Ladder up the back end to the deck (img_6828: straight ladder to the cabin)
    lad_x, lad_top = rail_len / 2 + 0.25, deck_z + 0.25
    for ly in (-0.25, 0.25):
        parts.append((add_cyl(f"lrail_{ly}", 0.035, lad_top,
                              (lad_x, ly, lad_top / 2.0), segments=6), QUAD_GALV))
    for i in range(1, 10):
        parts.append((add_cyl(f"lrung_{i}", 0.024, 0.5,
                              (lad_x, 0, i * lad_top / 10.0),
                              rotation=(math.radians(90), 0, 0), segments=6), QUAD_GALV))

    # Ground-level glazed liftie hut off to the side (stays - locked decision), plus the
    # electrical cabinet on the pad.
    hut = (1.8, 1.6, 2.3)
    hx, hy = -1.2, pad[1] / 2.0 + hut[1] / 2.0 + 0.55
    parts.append((add_box("hutbase", (hut[0] + 0.3, hut[1] + 0.3, 0.15),
                          (hx, hy, 0.075)), QUAD_CONCRETE))
    # K: below-grade skirt under the hut footprint (it stands OFF the pad, on raw
    # terrain, and floated on dropping ground) - same 3 m depth as the main skirt.
    parts.append((add_box("hutskirt", (hut[0] + 0.3, hut[1] + 0.3, 3.0),
                          (hx, hy, -1.5)), QUAD_CONCRETE))
    parts.append((add_box("hut", hut, (hx, hy, 0.15 + hut[2] / 2.0)), QUAD_PAINT))
    parts.append((add_box("hutwin", (hut[0] + 0.05, hut[1] + 0.05, 0.7),
                          (hx, hy, 0.15 + hut[2] * 0.62)), QUAD_GLASS))
    parts.append((add_box("hutroof", (hut[0] + 0.2, hut[1] + 0.2, 0.08),
                          (hx, hy, 0.15 + hut[2] + 0.04)), QUAD_GALV))
    parts.append((add_box("cabinet", (0.9, 0.5, 1.4),
                          (-3.2, -pad[1] / 2.0 + 0.55, pad[2] + 0.7)), QUAD_GALV))

    # The painted 1..16 tension scale on the outboard face of each rail (img_6828).
    # UVs are hand-set onto the stamped atlas strip, so these two plates skip the
    # normal quadrant unwrap (quad None).
    for s, asc in ((-1, True), (1, False)):
        plate = add_box(f"scale_{s}", (4.0, 0.024, 0.34),
                        (1.7, s * (rail_y + rail_r + 0.012), rail_z))
        set_plate_uvs(plate, outboard=s, ascending=asc)
        parts.append((plate, None))

    if finish:
        apply_finish_bevels(parts)
    for obj, quad in parts:
        if quad is not None:
            cs2_asset.unwrap_into_quadrant(obj, quad)

    bpy.ops.object.select_all(action="DESELECT")
    for obj, _ in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    t = bpy.context.active_object

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")

    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    cs2_asset.orient_for_net(t)
    return t


def build_terminal(p: TerminalParams, kind: str):
    drive = (kind == "drive")
    pad = p.pad if drive else p.r_pad
    col_x = p.col_x if drive else p.r_col_x
    col_size = p.col_size if drive else p.r_col_size
    housing = p.housing if drive else p.r_housing

    wheel_top = p.rope_height + p.wheel_depth / 2.0
    housing_z0 = wheel_top + p.housing_gap
    col_top = housing_z0 + housing[2] * 0.6

    parts = []   # (object, atlas quadrant)

    parts.append((add_box("pad", pad, (0, 0, pad[2] / 2.0)), QUAD_CONCRETE))

    for sx in (-1, 1):
        parts.append((add_box(f"col_{sx}",
                              (col_size, col_size, col_top - pad[2]),
                              (sx * col_x, 0, pad[2] + (col_top - pad[2]) / 2.0)), QUAD_GALV))
        # haunches - short diagonal braces where columns meet the housing
        parts.append((add_box(f"brace_{sx}",
                              (0.9, col_size * 0.7, 0.22),
                              (sx * (col_x - 0.5), 0, housing_z0 + 0.1)), QUAD_GALV))

    parts.append((add_cyl("bullwheel", p.wheel_radius, p.wheel_depth,
                          (0, 0, p.rope_height), segments=p.wheel_segments), QUAD_GALV))
    parts.append((add_cyl("hub", p.wheel_radius * 0.22, p.wheel_depth + 0.14,
                          (0, 0, p.rope_height), segments=12), QUAD_GALV))
    parts.append((add_cyl("shaft", 0.15, housing_z0 - p.rope_height + 0.5,
                          (0, 0, p.rope_height + (housing_z0 - p.rope_height) / 2.0 + 0.2),
                          segments=10), QUAD_GALV))

    parts.append((add_box("housing", housing,
                          (0, 0, housing_z0 + housing[2] / 2.0)), QUAD_PAINT))
    # thin roof cap with a slight overhang sheds the boxy look
    parts.append((add_box("roofcap", (housing[0] + 0.3, housing[1] + 0.3, 0.1),
                          (0, 0, housing_z0 + housing[2] + 0.05)), QUAD_GALV))

    # Liftie box - every lift has one at each end. Walls, wrap-around window band, roof.
    hut = p.hut if drive else (1.8, 1.6, 2.3)
    hx = pad[0] / 2.0 - hut[0] / 2.0 - 0.4
    hy = pad[1] / 2.0 - hut[1] / 2.0 - 0.4
    parts.append((add_box("hut", hut, (hx, hy, pad[2] + hut[2] / 2.0)), QUAD_PAINT))
    band_h = 0.75
    band_z = pad[2] + hut[2] * 0.62
    parts.append((add_box("hutwindows",
                          (hut[0] + 0.05, hut[1] + 0.05, band_h),
                          (hx, hy, band_z)), QUAD_GLASS))
    # Mullions: corner posts + one mid-bar per face, slightly prouder than the glass.
    for cx, cy in ((-1, -1), (-1, 1), (1, -1), (1, 1)):
        parts.append((add_box(f"mull_c{cx}{cy}", (0.07, 0.07, band_h),
                              (hx + cx * hut[0] / 2.0, hy + cy * hut[1] / 2.0, band_z)),
                      QUAD_GALV))
    for sx in (-1, 1):
        parts.append((add_box(f"mull_x{sx}", (0.06, 0.06, band_h),
                              (hx + sx * (hut[0] / 2.0 + 0.035), hy, band_z)), QUAD_GALV))
        parts.append((add_box(f"mull_y{sx}", (0.06, 0.06, band_h),
                              (hx, hy + sx * (hut[1] / 2.0 + 0.035), band_z)), QUAD_GALV))
    # Sill band under the glass
    parts.append((add_box("sill", (hut[0] + 0.09, hut[1] + 0.09, 0.06),
                          (hx, hy, band_z - band_h / 2.0 - 0.03)), QUAD_GALV))
    # Steel door on the pad-facing side of the hut
    parts.append((add_box("door", (0.06, 0.85, 1.95),
                          (hx - hut[0] / 2.0 - 0.02, hy - 0.15, pad[2] + 1.95 / 2.0)),
                  QUAD_GALV))
    parts.append((add_box("hutroof", (hut[0] + 0.2, hut[1] + 0.2, 0.08),
                          (hx, hy, pad[2] + hut[2] + 0.04)), QUAD_GALV))
    # Vent slats on one housing end
    for i in range(3):
        parts.append((add_box(f"vent_{i}", (0.05, housing[1] * 0.5, 0.09),
                              (housing[0] / 2.0 + 0.02, 0,
                               housing_z0 + 0.3 + i * 0.24)), QUAD_GALV))

    if drive:
        parts.append((add_box("cabinet", (0.9, 0.5, 1.4),
                              (-col_x, pad[1] / 2.0 - 0.6, pad[2] + 0.7)), QUAD_GALV))

    # Per-part UVs into atlas quadrants BEFORE joining
    for obj, quad in parts:
        cs2_asset.unwrap_into_quadrant(obj, quad)

    bpy.ops.object.select_all(action="DESELECT")
    for obj, _ in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    t = bpy.context.active_object

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")

    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    cs2_asset.orient_for_net(t)
    return t


def tri_count(obj):
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    n = len(bm.faces); bm.free(); return n


def render_previews(obj, out_dir, stem, hut_target=None):
    """
    Three framed views using lift_tower's camera helpers, with the atlas as material.

    `hut_target` (post-orient x, y, z, optional): adds a hut_closeup view aimed at the
    liftie control box - the K acceptance view for its new below-grade skirt.
    """
    scene = bpy.context.scene
    scene.render.engine = lift_tower.pick_engine()
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    # Dark world like the tower previews: a bright sky here acts as shadowless ambient
    # flood and washes the charcoal steel to pale gray, misrepresenting the atlas.
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.11, 0.13, 0.16, 1.0)
    scene.world = world

    bpy.ops.object.camera_add(location=(0, 0, 0))
    cam = bpy.context.active_object
    cam.data.lens = 50
    scene.camera = cam

    # Key/fill a notch above the tower previews (4.0/1.4): this is ALL-BLACK steel, and
    # at the tower energies the whole machine collapsed into one silhouette.
    bpy.ops.object.light_add(type="SUN", location=(10, -12, 20))
    key = bpy.context.active_object
    key.data.energy = 4.5
    key.rotation_euler = (math.radians(50), 0, math.radians(35))
    bpy.ops.object.light_add(type="SUN", location=(-12, 8, 12))
    fill = bpy.context.active_object
    fill.data.energy = 1.8
    fill.rotation_euler = (math.radians(65), 0, math.radians(215))

    # The object is already net-oriented when previews render (orient_for_net ran at the
    # end of the build), so pre-rotation -X (the line side) is +Y here and the catwalk
    # side (+Y pre-rotation) is +X.
    import mathutils
    views = [("three_quarter", mathutils.Vector((1.0, -1.2, 0.45)), (1300, 1000)),
             ("front",         mathutils.Vector((0.85, 0.9, 0.28)), (1300, 1000)),
             ("line_mouth",    mathutils.Vector((0.1, 1.0, 0.18)), (1300, 1000))]
    if hut_target is not None:
        views.append(("hut_closeup", mathutils.Vector((1.0, 0.45, 0.30)), (1200, 1000)))
    written = []
    for vname, direction, res in views:
        scene.render.resolution_x, scene.render.resolution_y = res
        if vname == "hut_closeup":
            # The liftie control box with its whole foundation column in frame: hut
            # (z to ~2.6) down through the 3 m below-grade skirt (z -3), so the K
            # skirt fix is verifiable (no ground plane in previews).
            tgt = mathutils.Vector(hut_target)
            cam.data.lens = 50
            cam.location = tgt + direction.normalized() * 13.0
            cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
        else:
            cam.data.lens = 50
            lift_tower.frame_camera(cam, obj, direction, res)
        path = os.path.join(out_dir, f"{stem}_{vname}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
    return written


def preview_material(obj, asset_dir, name):
    """Attach the atlas BaseColor for preview renders only (stripped again on export)."""
    mat = bpy.data.materials.new(name + "Preview")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    teximg = nt.nodes.new("ShaderNodeTexImage")
    teximg.image = bpy.data.images.load(os.path.join(asset_dir, f"{name}_BaseColor.png"))
    nt.links.new(teximg.outputs["Color"], bsdf.inputs["Base Color"])
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--kind", choices=["drive", "return"], default="drive")
    ap.add_argument("--name", default=None)
    ap.add_argument("--preview", default=None)
    ap.add_argument(
        "--finish", action="store_true",
        help="finishing pass (J follow-up, now the shipping build): bevel the boxy "
             "steel/concrete and rebake the 4096 atlas onto unique UVs with baked AO, "
             "density-protecting the tension-scale and pad strips (see lift_tower."
             "apply_finish_textures).",
    )
    return ap.parse_args(argv)


def main():
    args = parse_args()
    clear_scene()
    p = TerminalParams()

    default = "SkiLiftBaseStation" if args.kind == "drive" else "SkiLiftTopStation"
    t = build_terminal_pali(p, args.kind, finish=args.finish)
    t.name = args.name or default
    name = cs2_asset.validate_asset_name(t.name)

    asset_dir = os.path.join(os.path.abspath(args.out), name)
    os.makedirs(asset_dir, exist_ok=True)

    d = t.dimensions
    print(f"[terminal] kind: {args.kind}   tris: {tri_count(t)}")
    print(f"[terminal] size (m): {d.x:.2f} x {d.y:.2f} x {d.z:.2f}   (v1 was 9.14 tall)")

    # post hook stamps the white 1..16 tension scale into THIS asset's atlas copy - the
    # shared lift atlas recipe itself stays untouched for towers and chairs.
    # quad_size=2048 (J): the stations get a 4096 atlas - they are the hero assets the
    # camera parks next to. Towers and chairs stay at their smaller sizes.
    # paint_pad_strip is composed in (J follow-up) so every lift atlas carries the same
    # crash-pad strip - stations have no pads, but the palette stays identical across
    # the system and quad 2's reserved headroom is painted, not undefined.
    def _post(base, nrm, msk, ctl, A):
        cs2_asset.paint_pad_strip(base, nrm, msk, ctl, A)
        stamp_tension_scale(base, nrm, msk, ctl, A)

    cs2_asset.make_texture_set_atlas(asset_dir, name, QUAD_SETS, quad_size=2048,
                                     control_quads=CONTROL_QUADS, post=_post)

    if args.finish:
        # Texture half of the finishing pass (shared pipeline in lift_tower). BOTH
        # stamped strips are density-protected through the repack; 32 AO samples keep
        # the 4096 bake tractable headless.
        lift_tower.apply_finish_textures(
            t, asset_dir, name, atlas_size=4096,
            reserved_strips=[
                (cs2_asset.PAD_U0, cs2_asset.PAD_V0, cs2_asset.PAD_U1, cs2_asset.PAD_V1),
                (SCALE_U0, SCALE_V0, SCALE_U1, SCALE_V1),
            ],
            ao_samples=32,
        )

    if args.preview:
        prev = os.path.abspath(args.preview)
        os.makedirs(prev, exist_ok=True)
        preview_material(t, asset_dir, name)
        # Drive station: aim a closeup at the liftie control box (K skirt acceptance).
        # Pre-orient the hut centre is (hx, hy) = (-1.2, pad_y/2 + hut_y/2 + 0.55)
        # = (-1.2, 5.15); orient_for_net maps (x, y) -> (y, -x), so it lands at
        # (5.15, 1.2). Target z -0.2 centres the hut-plus-skirt column (z -3 .. 2.6).
        hut_target = (5.15, 1.2, -0.2) if args.kind == "drive" else None
        for f in render_previews(t, prev, name, hut_target=hut_target):
            print(f"[terminal] preview -> {f}")
        t.data.materials.clear()      # export stays material-free per the import contract

    cs2_asset.export_fbx(t, os.path.join(asset_dir, f"{name}.fbx"))
    cs2_asset.write_settings(asset_dir, name)
    print(f"[terminal] asset folder: {asset_dir}")


if __name__ == "__main__":
    main()
