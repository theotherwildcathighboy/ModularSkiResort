"""
Chairlift chair generator - M-style rebuild (letter L: SkiLiftChair6L).

Hangs from the haul rope as a NetSubObject with m_Placement = 2 (EdgeMiddle) and a spacing,
the same mechanism MarinaPierNetwork uses to distribute boats along a pier.

Run headless:
    blender --background --factory-startup --python lift_chair.py -- --out <dir> --name SkiLiftChair6L
        [--finish] [--lods] [--preview <dir>] [--verify] [--plate-number 27] [--ab <prev asset dir>]

CONTRACT (round L spec, "lift_chair.py (M-style, letter L)")
  * Grip-top -> lowest point is EXACTLY 2.400 m, asserted from the ACTUAL joined mesh
    (max z - min z after the base pivot), never from the authored constants (CH-14).
  * Base pivot (lowest point at z = 0, grip at the top), cs2_asset.orient_for_net, the
    NetSubObjects print (m_Placement 2, spacing 15, m_Position y = rope - height),
    cs2_asset.export_fbx, atlas via cs2_asset.lift_atlas_quads.
  * Every part comes from cs2_kit (add_cyl / add_box / tube_along) or the two local bmesh
    builders (scooped cushion, single-quad plate face). Buried caps are never built
    (caps="none" where a tube end sits inside another part); bars with r < 0.02 m are 4-seg.
  * J-stem = ONE cs2_kit.tube_along sweep (8 segs, rings only across the three bends).
  * Bench (backrest panel, three scooped seat cushions, two dividers) maps to the BENCH
    quad = atlas quad 2 (unused glass) repainted as vinyl by chair_atlas_post with
    ControlMask B = 1 (recolour slot 3). Frame paint stays on quad 3 (slot 1, R), hardware
    (grip, clamp block + studs, footrest) on quad 1 (galvanized, no recolour).
  * Number plate on the back of the backrest: an OPAQUE painted label stamped with
    cs2_compose.stamp_on_face onto the finished unique layout (BaseColor alpha stays 255
    everywhere - a label is painted, never alpha-clipped). ControlMask R is zeroed under it.
  * Finish: lift_tower.apply_finish_textures(atlas_size=512, ao_samples=24) on the chair's
    private 512 atlas (make_texture_set_atlas quad_size=256), then the detail relief
    (cushion/backrest seams, grip and clamp bolt heads: relief "map:seams"/"map:bolts")
    from cs2_compose.detail_height blended into the Normal map (single GL implementation).
  * LOD chain: cs2_asset.make_lod_chain targets (220, 70); LOD1 deletes bolt_ / divider_,
    LOD2 additionally armrest_ / armpost_ / footlink_ / backbar_ / plate_. LOD0 <= 500 tris.
  * cs2_asset.shade_by_angle(66) on the joined mesh; tag_parts / store_part_table so the
    "part" face attribute keys the LOD deletes, the relief specs and the plate stamp.
  * Previews through cs2_preview.preview (pbr / normals / density / lod / ab vs the previous
    letter) when importable, else the legacy render_previews.

Axis convention matches lift_tower.py: +X = travel/facing, +Y = chair width, +Z = up while
authoring; orient_for_net() converts to CS2's X-lateral / Y-up / Z-forward on export.
ORIGIN IS AT THE GRIP while authoring (all geometry at negative Z); build_chair re-pivots
so the lowest point sits at z = 0 and the grip at z = 2.400 (in game only geometry at
y >= 0 renders - the first version hung below its origin and only the grip showed).

Dimensions are real fixed-grip figures: ~0.6 m of bench per rider, 2.2 m rope to seat,
backrest ~0.5 m. Chair spacing ~15 m (double chair at 1200 p/h, 2.5 m/s: one every 6 s).
"""

import argparse
import collections
import json
import math
import os
import sys
from dataclasses import dataclass

import bpy
import bmesh
import mathutils
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402
import cs2_kit    # noqa: E402


DEFAULT_NAME = "SkiLiftChair"

# Atlas quadrants (cs2_asset.lift_atlas_quads layout, shared with towers and stations)
QUAD_GALV, QUAD_PAINT = 1, 3
# Bench vinyl: the chair does not use the glass quad, so chair_atlas_post repaints quad 2
# as light-neutral vinyl on recolour slot 3 (ControlMask B). NOTE for lift_terminal: its
# atlas has no such repaint, so bench parts built through build_chair_parts there would
# wear the glass quad - the static terminal chairs are being deleted this round anyway.
QUAD_BENCH = 2

HEIGHT_CONTRACT = 2.400        # grip top -> lowest point (the game positions chairs by it)
GRIP_TOP_Z = 0.08              # authored: grip box top above the grip origin
LOWEST_Z = -2.32               # authored: footrest bar bottom
TRI_BUDGET = 500               # LOD0 cap (art-pipeline-v2 section 4)

LOD_TARGETS = (220, 70)
LOD1_DELETE = ("bolt_", "divider_")
LOD2_DELETE = LOD1_DELETE + ("armrest_", "armpost_", "footlink_", "backbar_", "plate_")

ATLAS_QUAD_PX = 256            # private source atlas = 512
FINISH_ATLAS = 512
AO_SAMPLES = 24

BENCH_SMOOTH = 0.25
BENCH_PREVIEW_RGB = (0.14, 0.18, 0.28)   # slot-3 preview tint only (prefab VariationSet owns it)
PLATE_LIGHT = (0.90, 0.90, 0.86)
PLATE_DARK = (0.08, 0.08, 0.09)
PLATE_CELL_M = 0.0105                    # glyph cell (5 x 7 font) in metres on the plate

# Post-orient_for_net the travel axis is Blender -Y; the profile view looks along X and
# shows the J-stem edge-on.
CHAIR_VIEWS = (
    ("three_quarter", (1.0, -1.1, 0.35)),
    ("profile",       (1.0, 0.05, 0.10)),
    ("front",         (0.05, -1.0, 0.12)),
    ("back",          (0.7, 1.0, 0.30)),      # number plate + stem clamp
)

# Texel-density weights per part prefix (1 = the equalised base density). The legacy
# quadrant layout gives every part the same UV area whatever its size, and the finish
# packer scales all islands by ONE factor, so the spread would ship (measured before this:
# stem 60 px/m, clamp studs 2765). equalise_quadrant_density shrinks islands inside their
# quad to area3d-proportional sizes times these weights before the join.
DENSITY_WEIGHTS = {
    "plate_face": 2.5,     # the painted digits
    "plate_rim": 0.8,
    "grip": 1.4,           # bolt-head relief
    "clampblock": 1.2,
    "cushion_": 1.3,       # seam relief + scoop shading
    "backrest": 1.2,
    "bolt_": 0.6,
    "footlink_": 0.8,
    "armpost_": 0.8,
}

_DIGITS = {
    "0": ("01110", "10001", "10011", "10101", "11001", "10001", "01110"),
    "1": ("00100", "01100", "00100", "00100", "00100", "00100", "01110"),
    "2": ("01110", "10001", "00001", "00010", "00100", "01000", "11111"),
    "3": ("11111", "00010", "00100", "00010", "00001", "10001", "01110"),
    "4": ("00010", "00110", "01010", "10010", "11111", "00010", "00010"),
    "5": ("11111", "10000", "11110", "00001", "00001", "10001", "01110"),
    "6": ("00110", "01000", "10000", "11110", "10001", "10001", "01110"),
    "7": ("11111", "00001", "00010", "00100", "01000", "01000", "01000"),
    "8": ("01110", "10001", "10001", "01110", "10001", "10001", "01110"),
    "9": ("01110", "10001", "10001", "01111", "00001", "00010", "01100"),
}


@dataclass
class ChairParams:
    seats: int = 3                  # double / triple / quad
    seat_width_each: float = 0.6    # metres of bench per rider

    hanger_len: float = 2.2         # rope to seat centre height (the J stem reaches it)
    hanger_radius: float = 0.045    # the swan-neck stem tube
    grip_size: float = 0.16         # the clamp that grips the haul rope

    seat_depth: float = 0.52
    seat_thick: float = 0.06
    back_height: float = 0.50       # seat top to backrest top rail
    rail_radius: float = 0.028      # frame tubing

    plate_w: float = 0.20           # number plate on the backrest back
    plate_h: float = 0.13

    @property
    def width(self):
        return self.seats * self.seat_width_each


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights,
                  bpy.data.images):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


# --------------------------------------------------------------------------------------
# Primitives (thin wrappers over cs2_kit so every part carries a relief tag + UV layer)
# --------------------------------------------------------------------------------------

def add_box(name, size, location, drop=()):
    """Axis-aligned box centred on `location`; `drop` = buried face keys ("-Z", "+X"...)."""
    return cs2_kit.add_box(name, size, location, drop=drop)


def add_tube(name, radius, depth, location, rotation=(0, 0, 0), segments=8, caps="both"):
    """Cylinder along local Z (rotated by `rotation`) with fan caps unless buried."""
    return cs2_kit.add_cyl(name, radius, depth, location, rotation, segments, caps)


def add_link(name, p0, p1, radius, segments=6, extend=0.03, caps="both"):
    """
    Tube spanning two 3D points, lengthened `extend` past each end. Local +Z runs
    p0 -> p1 with local +Y tracked to world up, so a 6-gon has one vertex straight up and
    one straight DOWN: a bar's lowest point is exactly centre - radius (the height
    contract is measured from real vertices, so this matters). caps: "both", "top"
    (= the p1 end), "bottom" (= the p0 end) or "none".
    """
    a, b = mathutils.Vector(p0), mathutils.Vector(p1)
    d = b - a
    rot = d.to_track_quat("Z", "Y").to_euler()
    return cs2_kit.add_cyl(name, radius, d.length + 2.0 * extend, tuple((a + b) / 2.0),
                           tuple(rot), segments, caps)


def _bar(name, p0, p1, radius, caps="none"):
    """Frame bar with both ends buried: 4-seg below r 0.02 (plan 3.1), else 6-seg."""
    return add_link(name, p0, p1, radius, segments=4 if radius < 0.02 else 6,
                    extend=0.0, caps=caps)


def _mesh_object(name, bm, loc, relief="geo"):
    if bm.loops.layers.uv.get("UVMap") is None:
        bm.loops.layers.uv.new("UVMap")
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    obj["relief"] = relief
    return obj


def add_cushion(name, size, loc, scoop=0.02):
    """
    Vinyl cushion: an open-bottomed block whose top is a 4-triangle fan to a centre
    vertex lowered by `scoop` (the seat dip; smooth-shaded at 66 deg it reads as a soft
    hollow). 12 tris. relief "map:seams" (cs2_compose draws the stitching).
    """
    sx, sy, sz = size
    hx, hy, hz = sx / 2.0, sy / 2.0, sz / 2.0
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    corners = ((-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy))
    bot = [bm.verts.new((x, y, -hz)) for x, y in corners]
    top = [bm.verts.new((x, y, hz)) for x, y in corners]
    c = bm.verts.new((0.0, 0.0, hz - scoop))
    for k in range(4):
        k1 = (k + 1) % 4
        bm.faces.new((bot[k], bot[k1], top[k1], top[k]))     # outward side (CCW from outside)
        bm.faces.new((top[k], top[k1], c))                   # top fan, normal +Z
    bm.normal_update()
    return _mesh_object(name, bm, loc, "map:seams")


def add_plate_face(name, x, yc, zc, w, h, facing=1):
    """The number plate's visible face: ONE quad in the plane x, normal -X * facing."""
    hy, hz = w / 2.0, h / 2.0
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    order = [(-hy, -hz), (-hy, hz), (hy, hz), (hy, -hz)]     # normal -X
    if facing < 0:
        order.reverse()
    vs = [bm.verts.new((0.0, y, z)) for y, z in order]
    bm.faces.new(vs)
    bm.normal_update()
    return _mesh_object(name, bm, (x, yc, zc), "geo")


# --------------------------------------------------------------------------------------
# The chair
# --------------------------------------------------------------------------------------

def build_chair_parts(p: ChairParams, offset=(0, 0, 0), prefix="", facing=1,
                      bench_quad=QUAD_BENCH):
    """
    Build the chair's parts, each tagged with its atlas quadrant: [(obj, quad), ...].

    M-style rebuild of the J/K swan-neck chair (liftblog Pali photos, the Yan drawing):
    a single continuous J-curved stem drops from the grip, sweeps back and down behind
    the backrest and clamps to the backrest top rail (clamp block + two studs). The
    bench is a tube-steel frame (top rail, four back bars, armrests with open front
    ends on front posts, front lip tube, footrest bar on two links) carrying a padded
    backrest panel and three scooped seat cushions split by two dividers, plus a number
    plate on the back of the backrest.

    HEIGHT CONTRACT (hard invariant - the game positions chairs by this): grip top to
    lowest point is EXACTLY 2.40 m. Grip box top sits at offset.z + 0.08; the footrest
    bar's bottom VERTEX sits at offset.z - 2.32 (add_link puts a 6-gon vertex straight
    down). Nothing may hang below the footrest bar; build_chair asserts it from the mesh.

    Geometry hangs BELOW the grip: the grip sits at `offset`. Standalone export
    re-pivots afterwards; a terminal generator may pass `offset` directly so the grip
    lands on its modelled rope. `facing` is +1 (riders face +X, the travel direction)
    or -1 (mirrored along X). `prefix` keeps part names unique when several chairs
    share one scene. Part names are the LOD delete keys (LOD1_DELETE / LOD2_DELETE).
    """
    K = cs2_kit
    ox, oy, oz = offset
    f = 1 if facing >= 0 else -1
    parts = []
    w = p.width
    seat_z = oz - p.hanger_len                       # bench plank centre (-2.20)

    def X(relx):
        """Author in the facing=+1 frame; mirror along X for facing=-1."""
        return ox + f * relx

    def P(relx, rely, z):
        return (X(relx), oy + rely, z)

    back_x = -0.45                                   # backrest panel centre plane
    seat_cx = back_x + p.seat_depth / 2.0            # plank centre: -0.19
    seat_top = seat_z + p.seat_thick / 2.0           # -2.17
    back_top_z = seat_top + p.back_height            # backrest top rail: -1.67
    rail_x = -0.50
    bar_x = -0.486                                   # back bars, just behind the panel
    arm_z = seat_z + 0.28
    y_out = w / 2.0 - 0.06                           # outer bars / armrests
    y_in = 0.30                                      # inner bars (behind the panel)

    # Grip on the rope - top face at oz + 0.08, the height datum. Clamp bolts painted.
    grip = K.add_box(f"{prefix}grip", (p.grip_size * 1.6, p.grip_size, p.grip_size),
                     P(0.0, 0.0, oz))
    grip["relief"] = "map:bolts"
    parts.append((grip, QUAD_GALV))

    # THE SWAN-NECK: one continuous tube_along sweep. Its top end is buried in the grip,
    # its bottom end in the clamp block, so no caps. Rings only across the three bends,
    # and the three fillets MEET end to end (stem_points: middle spans = 2 x fillet), so
    # the sweep is one even chain of ~0.15 m chords with no sliver faces - a short face
    # beside a long chord skews the area-weighted vertex normals and dents the tube.
    stem = K.tube_along(f"{prefix}tube_stem", stem_points(oz, back_top_z, X, oy),
                        p.hanger_radius, segments=8, caps="none", bend_rings=STEM_BEND_RINGS)
    if stem["rings"] != STEM_RINGS:
        raise RuntimeError(f"stem: expected {STEM_RINGS} rings (fillets meeting), "
                           f"got {stem['rings']}")
    parts.append((stem, QUAD_PAINT))

    # Stem/rail clamp block around the joint, two studs on top (real geometry, LOD1 drops).
    clamp = K.add_box(f"{prefix}clampblock", (0.13, 0.15, 0.13), P(-0.51, 0.0, back_top_z))
    clamp["relief"] = "map:bolts"
    parts.append((clamp, QUAD_GALV))
    for k, sy in enumerate((-1, 1)):
        parts.append((K.add_cyl(f"{prefix}bolt_clamp_{k}", 0.010, 0.02,
                                P(-0.51, sy * 0.045, back_top_z + 0.065 + 0.01),
                                (0.0, 0.0, 0.0), 4, "top"), QUAD_GALV))

    # Backrest top rail (ends exposed: capped) and four back bars (ends buried).
    rl = w * 0.98
    parts.append((add_link(f"{prefix}backrail", P(rail_x, -rl / 2.0, back_top_z),
                           P(rail_x, rl / 2.0, back_top_z), p.rail_radius,
                           segments=8, extend=0.0, caps="both"), QUAD_PAINT))
    for k, side in enumerate((-1, 1)):
        parts.append((_bar(f"{prefix}backbar_o{k}", P(bar_x, side * y_out, back_top_z - 0.01),
                           P(bar_x, side * y_out, seat_top - 0.01), 0.022), QUAD_PAINT))
        parts.append((_bar(f"{prefix}backbar_i{k}", P(bar_x, side * y_in, back_top_z - 0.01),
                           P(bar_x, side * y_in, seat_top - 0.01), 0.016), QUAD_PAINT))

    # Padded backrest panel between the plank and the rail (bench vinyl, slot 3).
    pan_top = back_top_z - p.rail_radius
    pan_bot = seat_top + 0.02
    panel = K.add_box(f"{prefix}backrest", (0.04, w * 0.90, pan_top - pan_bot),
                      P(back_x, 0.0, (pan_top + pan_bot) / 2.0))
    panel["relief"] = "map:seams"
    parts.append((panel, bench_quad))

    # Number plate on the panel's back face, off-centre so the stem never hides it. The
    # rim is a box with both X faces dropped (back buried, front replaced by plate_face,
    # the single quad cs2_compose.stamp_on_face paints the number onto).
    plate_y = 0.55
    plate_zc = (pan_top + pan_bot) / 2.0 + 0.02
    panel_back = back_x - 0.02
    parts.append((K.add_box(f"{prefix}plate_rim", (0.012, p.plate_w, p.plate_h),
                            P(panel_back - 0.006, plate_y, plate_zc), drop=("+X", "-X")),
                  QUAD_PAINT))
    parts.append((add_plate_face(f"{prefix}plate_face", X(panel_back - 0.012), oy + plate_y,
                                 plate_zc, p.plate_w, p.plate_h, f), QUAD_PAINT))

    # Seat plank (frame paint) with the front lip tube half proud of its front edge.
    parts.append((K.add_box(f"{prefix}seat", (p.seat_depth, w, p.seat_thick),
                            P(seat_cx, 0.0, seat_z)), QUAD_PAINT))
    lip_x = seat_cx + p.seat_depth / 2.0
    stl = w * 0.96
    parts.append((add_link(f"{prefix}seattube", P(lip_x, -stl / 2.0, seat_z),
                           P(lip_x, stl / 2.0, seat_z), 0.026, segments=6, extend=0.0,
                           caps="both"), QUAD_PAINT))

    # Three scooped cushions and two dividers on the plank (0.10 m plank margin each side
    # keeps the armrest posts clear of the vinyl).
    inner = w - 0.20
    div_w = 0.03
    cw = (inner - 2.0 * div_w) / 3.0
    cush_d = p.seat_depth - 0.06
    for k, m in enumerate((-1, 0, 1)):
        parts.append((add_cushion(f"{prefix}cushion_{k}", (cush_d, cw, 0.05),
                                  P(seat_cx, m * (cw + div_w), seat_top + 0.025), scoop=0.02),
                      bench_quad))
    for k, m in enumerate((-1, 1)):
        parts.append((K.add_box(f"{prefix}divider_{k}", (cush_d, div_w, 0.06),
                                P(seat_cx, m * (cw + div_w) / 2.0, seat_top + 0.03),
                                drop=("-Z",)), bench_quad))

    # Armrests: back end buried in the outer back bar, open front end capped, on a
    # front post down into the plank.
    for k, side in enumerate((-1, 1)):
        y = side * y_out
        parts.append((add_link(f"{prefix}armrest_{k}", P(bar_x, y, arm_z), P(-0.02, y, arm_z),
                               0.020, segments=6, extend=0.0, caps="top"), QUAD_PAINT))
        parts.append((_bar(f"{prefix}armpost_{k}", P(-0.05, y, arm_z - 0.01),
                           P(-0.05, y, seat_top - 0.01), 0.018), QUAD_PAINT))

    # Footrest bar on two links below the seat front. Bar bottom VERTEX at oz - 2.32
    # CLOSES the 2.40 m height contract; the links stop inside the bar.
    bar_r = 0.025
    bar_z = oz + LOWEST_Z + bar_r
    fl = w * 0.72
    parts.append((add_link(f"{prefix}footbar", P(0.14, -fl / 2.0, bar_z), P(0.14, fl / 2.0, bar_z),
                           bar_r, segments=6, extend=0.0, caps="both"), QUAD_GALV))
    for k, side in enumerate((-1, 1)):
        y = side * w * 0.29
        parts.append((_bar(f"{prefix}footlink_{k}", P(0.02, y, seat_z - p.seat_thick / 2.0 + 0.01),
                           P(0.13, y, bar_z + 0.012), 0.016), QUAD_GALV))
    return parts


STEM_BEND_RINGS = 3
STEM_SPAN = 0.30                      # the two middle spans; fillet = STEM_SPAN / 2 each
STEM_ANGLES_DEG = (56.0, 74.0)        # middle-span slopes below horizontal (seg0 ~38, seg3 90)
STEM_VERT_TOP = -0.802                # where the vertical run starts (authoring z)
STEM_RINGS = 2 + 3 * STEM_BEND_RINGS - 2   # touching fillets: two coincident rings dedupe


def stem_points(oz, back_top_z, X=lambda x: x, oy=0.0):
    """
    The five J-stem points, authored so cs2_kit.tube_along's fillets meet: the two middle
    spans are exactly STEM_SPAN long, so every bend gets fillet = STEM_SPAN / 2 and the
    end of one fillet is the start of the next (no straight slivers between bends). Built
    backwards from the vertical run (x = -0.52, behind the backrest) at the middle-span
    slopes; the first span (grip -> P1) comes out ~0.34 m at ~38 deg, the vertical run
    reaches the clamp block on the backrest rail.
    """
    a1, a2 = (math.radians(a) for a in STEM_ANGLES_DEG)
    p3 = (-0.52, oz + STEM_VERT_TOP)
    p2 = (p3[0] + STEM_SPAN * math.cos(a2), p3[1] + STEM_SPAN * math.sin(a2))
    p1 = (p2[0] + STEM_SPAN * math.cos(a1), p2[1] + STEM_SPAN * math.sin(a1))
    return [(X(0.0), oy, oz - 0.055),
            (X(p1[0]), oy, p1[1]),
            (X(p2[0]), oy, p2[1]),
            (X(p3[0]), oy, p3[1]),
            (X(-0.52), oy, back_top_z - 0.03)]


def chair_atlas_post(base, nrm, msk, ctl, A):
    """
    make_texture_set_atlas `post` hook: repaint quad 2 (the lift atlas glass quad, which
    the chair never uses) as bench vinyl - light-neutral RECOLOR_ALBEDO albedo with a
    faint grain, dielectric, matte - on recolour slot 3 (ControlMask B = 1, R = G = 0).
    The whole quad is painted, reserved top strip included (nothing maps there; the
    finish restores it 1:1, harmlessly).
    """
    rng = np.random.default_rng(41)
    h = A // 2
    sl = (slice(A - h, A), slice(0, h))
    grain = rng.normal(0.0, 0.012, (h, h)).astype(np.float32)
    for ch in range(3):
        base[sl][..., ch] = np.clip(cs2_asset.RECOLOR_ALBEDO + grain, 0.0, 1.0)
    base[sl][..., 3] = 1.0
    nrm[sl][..., 0:3] = (0.5, 0.5, 1.0)
    nrm[sl][..., 3] = 1.0
    msk[sl][..., 0] = 0.0
    msk[sl][..., 1] = 0.0
    msk[sl][..., 2] = 0.0
    msk[sl][..., 3] = BENCH_SMOOTH
    ctl[sl][..., 0] = 0.0
    ctl[sl][..., 1] = 0.0
    ctl[sl][..., 2] = 1.0
    ctl[sl][..., 3] = 0.0


def _weight_for(name, weights):
    best = None
    for prefix, w in weights.items():
        if name.startswith(prefix) and (best is None or len(prefix) > len(best[0])):
            best = (prefix, w)
    return 1.0 if best is None else float(best[1])


def equalise_quadrant_density(parts, weights=None, verbose=True):
    """
    Shrink-only per-part island scaling INSIDE each atlas quadrant so the UV area of a
    part is proportional to its 3D area x weight^2 (uniform px/m at weight 1). Call after
    unwrap_into_quadrant and before the join. Scaling is about the quadrant centre, so
    islands stay inside their quad (and below quad 2's reserved top). Returns
    {part name: (px/m ratio before, after)} in atlas-UV units (uv_area / area3d).
    """
    weights = DENSITY_WEIGHTS if weights is None else weights
    stats = []
    for obj, quad in parts:
        me = obj.data
        uv = me.uv_layers.active.data
        a3 = sum(pg.area for pg in me.polygons)
        auv = 0.0
        for pg in me.polygons:
            pts = [uv[li].uv for li in pg.loop_indices]
            s = 0.0
            for i in range(len(pts)):
                x0, y0 = pts[i]
                x1, y1 = pts[(i + 1) % len(pts)]
                s += x0 * y1 - x1 * y0
            auv += abs(s) * 0.5
        if a3 < 1e-9 or auv < 1e-12:
            continue
        w = _weight_for(obj.name, weights)
        stats.append((obj, quad, auv / a3, w))
    ref = min(r / (w * w) for _o, _q, r, w in stats)
    report = {}
    for obj, quad, r, w in stats:
        target = ref * w * w
        s = min(1.0, math.sqrt(target / r))
        uo, vo = cs2_asset.QUADS[quad]
        cu, cv = uo + 0.25, vo + 0.25
        if s < 0.9999:
            for l in obj.data.uv_layers.active.data:
                u, v = l.uv
                l.uv = (cu + (u - cu) * s, cv + (v - cv) * s)
        report[obj.name] = (r, r * s * s)
    if verbose:
        lo = min(v[1] for v in report.values())
        hi = max(v[1] for v in report.values())
        print(f"[chair] density equalised in the atlas quads: uv/3d ratio now "
              f"{lo:.3f} .. {hi:.3f} (was {min(v[0] for v in report.values()):.3f} .. "
              f"{max(v[0] for v in report.values()):.3f}); weights {weights}")
    return report


def build_chair(p: ChairParams, name=DEFAULT_NAME, shade=True):
    """
    Parts -> tag_parts -> per-part atlas UVs -> equalise density -> join -> remove_doubles
    -> base pivot ->
    orient_for_net -> store_part_table -> shade_by_angle(66). Returns the joined object;
    its mesh carries cs2_asset.PART_TABLE_PROP (json name table) and the "part" attribute.
    """
    parts = build_chair_parts(p)
    table = cs2_asset.tag_parts(parts)

    # Per-part UVs into the shared atlas quadrants BEFORE joining, then equalised so the
    # finish repack ships a uniform (weighted) texel density.
    for obj, quad in parts:
        cs2_asset.unwrap_into_quadrant(obj, quad)
    equalise_quadrant_density(parts)

    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()

    chair = bpy.context.active_object
    chair.name = name
    chair.data.name = name

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")

    # BASE-PIVOT: shift the whole chair up so its lowest point sits at z = 0.
    #
    # The first version put the origin at the grip, so the chair hung entirely BELOW its
    # own origin. In game only the 8 cm grip rendered - everything below the origin was
    # treated as underground and clipped. Every CS2 object we have inspected (the vanilla
    # 47 m pylon, the 8 m pole, our tower) is base-pivoted with geometry at y >= 0.
    #
    # The grip therefore ends up at the TOP of the object, at z = height, and the
    # NetSubObjects m_Position.y must be lowered by that height so the grip still lands
    # on the rope.
    bpy.ops.object.select_all(action="DESELECT")
    chair.select_set(True)
    bpy.context.view_layer.objects.active = chair
    lo = min((chair.matrix_world @ v.co).z for v in chair.data.vertices)
    chair.location.z -= lo
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

    cs2_asset.orient_for_net(chair)
    cs2_asset.store_part_table(chair, table)
    if shade:
        cs2_asset.shade_by_angle(chair, 66.0)
    return chair


def assert_height_contract(obj, label="chair", tol=1e-4):
    """
    2.400 m grip-top -> lowest point, measured from the ACTUAL vertices (world space),
    with the lowest point at z = 0. Raises on any drift; returns (lo, hi).
    """
    zs = [(obj.matrix_world @ v.co).z for v in obj.data.vertices]
    lo, hi = min(zs), max(zs)
    if abs(lo) > 1e-5 or abs((hi - lo) - HEIGHT_CONTRACT) > tol:
        raise RuntimeError(
            f"HEIGHT CONTRACT BROKEN ({label}): z {lo:.5f} .. {hi:.5f} = {hi - lo:.5f} m "
            f"grip-top to lowest point, must be exactly {HEIGHT_CONTRACT:.3f} with the "
            f"lowest point at 0 (the game positions chairs by this height).")
    return lo, hi


def pin_height(obj, lo=0.0, hi=HEIGHT_CONTRACT, snap=0.005):
    """
    Restore the height contract on a decimated LOD: collapse decimation nudges the
    extreme vertices by a millimetre or so (measured 1.1 mm on the footbar bottom), and
    LOD prefabs carry their OWN m_Bounds. Every vertex within `snap` of the lowest /
    highest point is set to exactly lo / hi. Returns (n_low, n_high) vertices moved.
    """
    me = obj.data
    mw = obj.matrix_world
    zs = [(mw @ v.co).z for v in me.vertices]
    zmin, zmax = min(zs), max(zs)
    inv = mw.inverted()
    n_lo = n_hi = 0
    for v, z in zip(me.vertices, zs):
        if z <= zmin + snap and abs(z - lo) > 1e-7:
            w = mw @ v.co
            w.z = lo
            v.co = inv @ w
            n_lo += 1
        elif z >= zmax - snap and abs(z - hi) > 1e-7:
            w = mw @ v.co
            w.z = hi
            v.co = inv @ w
            n_hi += 1
    me.update()
    print(f"[chair] {obj.name}: pinned {n_lo} lowest / {n_hi} highest vertices to "
          f"z = {lo:.3f} / {hi:.3f} (was {zmin:.4f} .. {zmax:.4f})")
    return n_lo, n_hi


def unwrap(obj):
    """Legacy whole-object smart projection (unused: parts unwrap per quadrant)."""
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")


def tri_count(obj):
    return cs2_asset.tri_count(obj)


# --------------------------------------------------------------------------------------
# Post-finish texture work: number plate + geometry-keyed relief
# --------------------------------------------------------------------------------------

def _digit_bitmap(text):
    """Rows (top -> bottom) of a 5x7 glyph strip with one blank column between glyphs."""
    glyphs = [_DIGITS.get(ch, _DIGITS["0"]) for ch in str(text)]
    rows = []
    for r in range(7):
        rows.append("0".join(g[r] for g in glyphs))
    return rows


def make_plate_painter(chair, table, number, A, plate_w, plate_h):
    """
    Painter for cs2_compose.stamp_on_face over the plate_face quad. Orientation is read
    from the face itself: the UV -> world affine of its loops maps every texel centre to
    a point on the plate, and (right, up) = ((-n) x up_world, up_world) as a viewer in
    front of the face sees it - so the digits come out upright and unmirrored whatever
    the packer did with the island (rotate=False, but flips/aspect are free).
    """
    import cs2_compose
    me = chair.data
    faces = cs2_compose.faces_of_prefix(me, table, "plate_face")
    if len(faces) != 1:
        raise RuntimeError(f"plate_face: expected one quad, found {len(faces)} faces")
    poly = me.polygons[faces[0]]
    mw = chair.matrix_world
    uvl = me.uv_layers["UVUnique"].data
    Q = np.array([[uvl[li].uv[0], uvl[li].uv[1], 1.0] for li in poly.loop_indices], np.float64)
    Pw = np.array([list(mw @ me.vertices[me.loops[li].vertex_index].co)
                   for li in poly.loop_indices], np.float64)
    M, *_ = np.linalg.lstsq(Q, Pw, rcond=None)           # world = [u v 1] @ M
    n = (mw.to_3x3() @ poly.normal).normalized()
    up = mathutils.Vector((0.0, 0.0, 1.0))
    up = (up - n * up.dot(n)).normalized()
    right = (-n).cross(up)
    centre = Pw.mean(axis=0)
    right_a, up_a = np.array(list(right)), np.array(list(up))
    bitmap = _digit_bitmap(number)
    rows, cols = len(bitmap), len(bitmap[0])
    bits = np.array([[ch == "1" for ch in row] for row in bitmap])
    cell = PLATE_CELL_M
    tw, th = cols * cell, rows * cell
    hw, hh = plate_w / 2.0, plate_h / 2.0
    light = np.array(PLATE_LIGHT, np.float32)
    dark = np.array(PLATE_DARK, np.float32)

    def painter(rect, base_sl, ctl_sl):
        r0, r1, c0, c1 = rect
        rr, cc = np.mgrid[r0:r1, c0:c1]
        uv1 = np.stack([(cc + 0.5) / A, (rr + 0.5) / A, np.ones(rr.shape)], axis=-1)
        Wp = uv1 @ M
        d = Wp - centre
        s = d @ right_a
        t = d @ up_a
        img = np.empty(s.shape + (3,), np.float32)
        img[...] = light
        border = (np.abs(s) > hw - 0.008) | (np.abs(t) > hh - 0.008)
        ci = np.floor((s + tw / 2.0) / cell).astype(int)
        ri = np.floor((th / 2.0 - t) / cell).astype(int)
        inside = (ci >= 0) & (ci < cols) & (ri >= 0) & (ri < rows)
        glyph = np.zeros(s.shape, bool)
        glyph[inside] = bits[ri[inside], ci[inside]]
        img[border | glyph] = dark
        base_sl[..., 0:3] = img
        base_sl[..., 3] = 1.0

    return painter


def detail_specs():
    """Relief specs keyed by part-name prefix (cs2_compose.detail_height)."""
    from cs2_compose import DetailSpec
    return {
        "cushion_":   DetailSpec(seams_m=0.12, seam_depth_m=0.003, seam_width_m=0.010),
        "backrest":   DetailSpec(seams_m=0.15, seam_depth_m=0.003, seam_width_m=0.010),
        "grip":       DetailSpec(bolt_pitch_m=0.09, bolt_r_m=0.010, bolt_h_m=0.004),
        "clampblock": DetailSpec(bolt_pitch_m=0.07, bolt_r_m=0.008, bolt_h_m=0.004),
    }


def post_finish_textures(chair, asset_dir, name, A, plate_number):
    """
    After lift_tower.apply_finish_textures (unique layout shipped, maps written):
      1. stamp the number plate (opaque, ControlMask R zeroed under it),
      2. rasterise the relief (cushion/backrest seams, grip + clamp bolt heads) with
         cs2_compose.detail_height and blend it into the Normal (RNM, GL sign),
      3. rewrite BaseColor / ControlMask / Normal with BaseColor alpha 1, ControlMask A 0.
    """
    import cs2_compose
    me = chair.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])

    def load(suffix):
        img = bpy.data.images.load(os.path.join(asset_dir, f"{name}_{suffix}.png"))
        buf = np.empty(A * A * 4, dtype=np.float32)
        img.pixels.foreach_get(buf)
        bpy.data.images.remove(img)
        return buf.reshape(A, A, 4).copy()

    base, ctl, nrm = load("BaseColor"), load("ControlMask"), load("Normal")

    p = ChairParams()
    cs2_compose.stamp_on_face(base, ctl, me, table, "plate_face",
                              make_plate_painter(chair, table, plate_number, A,
                                                 p.plate_w, p.plate_h))

    h = cs2_compose.detail_height(me, A, detail_specs())
    _part_map, ppm_map = cs2_compose.rasterise_face_fields(me, A)
    det = cs2_compose.height_to_normal(h, ppm_map)
    # The EMIT rebake leaves uncovered gutter texels black; give them a flat normal
    # (blend_rnm would turn black into mid-grey) and renormalise every covered texel
    # (photo normal sets are not unit length; nz clamped >= 0.05 so B > 0.5 everywhere).
    uncovered = nrm[..., 0:3].max(axis=-1) < 1e-3
    nrm[..., 0:3] = cs2_compose.blend_rnm(nrm, det)
    n = nrm[..., 0:3] * 2.0 - 1.0
    n[..., 2] = np.maximum(n[..., 2], 0.05)
    n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
    nrm[..., 0:3] = n * 0.5 + 0.5
    nrm[uncovered, 0:3] = (0.5, 0.5, 1.0)
    nrm[..., 3] = 1.0
    base[..., 3] = 1.0
    ctl[..., 3] = 0.0

    for arr, suffix, is_data in ((base, "BaseColor", False), (ctl, "ControlMask", True),
                                 (nrm, "Normal", True)):
        cs2_asset._write_png_raw(os.path.join(asset_dir, f"{name}_{suffix}.png"),
                                 f"{name}_{suffix}", arr, A, is_data)
    print(f"[chair] post-finish: plate '{plate_number}' stamped, relief blended into Normal "
          f"(height range {h.min():+.4f} .. {h.max():+.4f} m)")


# --------------------------------------------------------------------------------------
# Previews
# --------------------------------------------------------------------------------------

def render_previews(chair, out_dir, stem):
    """
    Legacy fallback (used only when cs2_preview cannot be imported): framed views via
    lift_tower's camera helpers. The profile view shows the swan-neck curve edge-on.
    """
    import lift_tower
    scene = bpy.context.scene
    scene.render.engine = lift_tower.pick_engine()
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.60, 0.66, 0.76, 1.0)
    scene.world = world

    bpy.ops.object.camera_add(location=(0, 0, 0))
    cam = bpy.context.active_object
    cam.data.lens = 50
    scene.camera = cam

    bpy.ops.object.light_add(type="SUN", location=(10, -12, 20))
    key = bpy.context.active_object
    key.data.energy = 5.5
    key.rotation_euler = (math.radians(50), 0, math.radians(35))
    bpy.ops.object.light_add(type="SUN", location=(-12, 8, 12))
    fill = bpy.context.active_object
    fill.data.energy = 2.2
    fill.rotation_euler = (math.radians(65), 0, math.radians(215))

    views = [(vname, mathutils.Vector(vdir), (1000, 1200)) for vname, vdir in CHAIR_VIEWS]
    written = []
    for vname, direction, res in views:
        scene.render.resolution_x, scene.render.resolution_y = res
        lift_tower.frame_camera(cam, chair, direction, res)
        path = os.path.join(out_dir, f"{stem}_{vname}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
    return written


def preview_rig(chair, asset_dir, name, prev_dir, lods=(), prev_generation=None):
    """cs2_preview when importable (pbr / normals / density / lod / ab), else the legacy rig."""
    os.makedirs(prev_dir, exist_ok=True)
    try:
        import cs2_preview
    except Exception as e:  # noqa: BLE001
        print(f"[chair] cs2_preview unavailable ({e!r}) - legacy previews")
        cs2_asset.preview_material(chair, asset_dir, name)
        try:
            return render_previews(chair, prev_dir, name)
        finally:
            chair.data.materials.clear()
    modes = ["pbr", "normals", "density"]
    if lods:
        modes.append("lod")
    if prev_generation:
        modes.append("ab")
    return cs2_preview.preview(chair, asset_dir, name, prev_dir, modes=tuple(modes),
                               views=CHAIR_VIEWS, lods=list(lods) or None,
                               prev_generation=prev_generation,
                               tint={"B": BENCH_PREVIEW_RGB}, ground=False,
                               resolution=(1000, 1200))


# --------------------------------------------------------------------------------------
# Round trip
# --------------------------------------------------------------------------------------

def verify_export(fbx_path, expect_height=HEIGHT_CONTRACT, histogram=True):
    """
    Re-import a written FBX and report: raw vertex extent (cm, Y up), custom normals and
    the split-normals-per-vertex histogram (probe_smooth.py idiom), and the height
    contract on the imported vertices. Returns (ok, info dict). Imported data is removed.
    """
    before = set(bpy.data.objects)
    op = bpy.ops.wm.fbx_import if hasattr(bpy.ops.wm, "fbx_import") else bpy.ops.import_scene.fbx
    op(filepath=fbx_path)
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    if not new:
        raise RuntimeError(f"verify: nothing imported from {fbx_path}")
    o = new[0]
    m = o.data
    xs = [v.co.x for v in m.vertices]
    ys = [v.co.y for v in m.vertices]
    zs = [v.co.z for v in m.vertices]
    ext = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    unit = 1.0 if max(ext) > 10.0 else 100.0           # cm baked in, unless the importer rescaled
    height = ext[1] * unit / 100.0
    lo_y = min(ys) * unit / 100.0
    info = {"name": o.name, "verts": len(m.vertices), "polys": len(m.polygons),
            "extent_cm": tuple(round(e * unit, 3) for e in ext), "height_m": height,
            "min_y_m": lo_y, "custom_normals": bool(m.has_custom_normals)}
    if histogram:
        per_vert = collections.defaultdict(set)
        for lp in m.loops:
            per_vert[lp.vertex_index].add(tuple(round(c, 3) for c in lp.normal))
        info["normals_per_vertex"] = dict(sorted(collections.Counter(
            len(v) for v in per_vert.values()).items()))
    ok = abs(height - expect_height) < 1e-3 and abs(lo_y) < 1e-3
    stem = os.path.basename(fbx_path)
    print(f"[verify] {stem}: mesh '{o.name}' verts {info['verts']} polys {info['polys']} "
          f"raw extent cm {info['extent_cm']} (Y up) custom_normals={info['custom_normals']}")
    if histogram:
        print(f"[verify] {stem}: normals-per-vertex histogram {info['normals_per_vertex']}")
    print(f"[verify] {stem}: height {height:.4f} m (y from {lo_y:.4f}) vs {expect_height:.3f} "
          f"-> {'OK' if ok else 'FAIL'}")
    for obj in new:
        me = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(me)
    return ok, info


# --------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------

def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--name", default=DEFAULT_NAME)
    ap.add_argument("--seats", type=int, default=3)
    ap.add_argument("--preview", default=None)
    ap.add_argument("--finish", action="store_true",
                    help="unique repack + AO rebake at 512 (lift_tower.apply_finish_textures), "
                         "then the number plate stamp and the relief normal")
    ap.add_argument("--lods", action="store_true",
                    help="ship an authored LOD1/LOD2 chain (targets %s)" % (LOD_TARGETS,))
    ap.add_argument("--verify", action="store_true",
                    help="re-import every written FBX and check the height contract")
    ap.add_argument("--plate-number", default="27")
    ap.add_argument("--ab", default=None,
                    help="previous generation asset folder for the ab preview "
                         "(default: ../import/chairs/K/SkiLiftChair6K if present)")
    # kept for CLI compatibility with earlier rounds; the atlas recipe owns these now
    ap.add_argument("--basecolor", type=float, nargs=3, default=[0.13, 0.16, 0.22])
    ap.add_argument("--metallic", type=float, default=0.1)
    ap.add_argument("--smoothness", type=float, default=0.35)
    return ap.parse_args(argv)


def main():
    args = parse_args()
    clear_scene()
    here = os.path.dirname(os.path.abspath(__file__))

    p = ChairParams(seats=args.seats)
    name = cs2_asset.validate_asset_name(args.name)
    chair = build_chair(p, name)
    table = json.loads(chair.data[cs2_asset.PART_TABLE_PROP])
    lo, hi = assert_height_contract(chair)
    tris = cs2_asset.tri_count(chair)
    if tris > TRI_BUDGET:
        raise RuntimeError(f"LOD0 {tris} tris exceeds the {TRI_BUDGET} budget")

    asset_dir = os.path.join(os.path.abspath(args.out), name)
    os.makedirs(asset_dir, exist_ok=True)

    d = chair.dimensions
    grip_h = hi - lo                 # base-pivoted, so the grip is at the very top
    rope_y = 7.04                    # lane height, from TowerParams.sheave_axle_height
    print(f"[chair] seats: {p.seats}   bench width: {p.width:.2f} m   parts: {len(table)}")
    print(f"[chair] tris: {tris} (budget {TRI_BUDGET}), verts: {len(chair.data.vertices)}")
    print(f"[chair] size (m): {d.x:.3f} x {d.y:.3f} x {d.z:.3f}")
    print(f"[chair] base-pivoted: geometry runs z {lo:.4f} .. {hi:.4f}, grip at the top; "
          f"height contract {grip_h:.4f} m OK")
    print(f"[chair] ---- CS2 NetSubObjects settings ----")
    print(f"[chair]   m_Placement 2 (EdgeMiddle), m_Spacing 15")
    print(f"[chair]   m_Position  (+/-1.80, {rope_y - grip_h:.2f}, 0)")
    print(f"[chair]   = rope {rope_y} minus chair height {grip_h:.2f}, so the grip lands on the rope")

    # Private copy of the shared lift atlas at 512 (quad 256): the bench quad repainted.
    tex_root = os.path.join(here, "..", "textures")
    quad_sets, control_quads = cs2_asset.lift_atlas_quads(tex_root)
    cs2_asset.make_texture_set_atlas(asset_dir, name, quad_sets, quad_size=ATLAS_QUAD_PX,
                                     control_quads=control_quads, post=chair_atlas_post)

    finished = False
    if args.finish:
        import lift_tower
        finished = lift_tower.apply_finish_textures(chair, asset_dir, name, FINISH_ATLAS,
                                                    ao_samples=AO_SAMPLES)
        if finished:
            post_finish_textures(chair, asset_dir, name, FINISH_ATLAS, args.plate_number)
        else:
            print("[chair] finish failed - shipping the atlas layout, plate unpainted")
    else:
        print("[chair] no --finish: atlas layout shipped, number plate unpainted")

    cs2_asset.export_fbx(chair, os.path.join(asset_dir, f"{name}.fbx"))

    lods = []
    if args.lods:
        lods = cs2_asset.make_lod_chain(chair, name, LOD_TARGETS, (LOD1_DELETE, LOD2_DELETE))
        for lod in lods:
            pin_height(lod)
            llo, lhi = assert_height_contract(lod, lod.name)
            print(f"[chair] {lod.name}: {cs2_asset.tri_count(lod)} tris, "
                  f"z {llo:.4f} .. {lhi:.4f} (height {lhi - llo:.4f} m) OK")
            cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{lod.name}.fbx"))
    cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    print(f"[chair] asset folder: {asset_dir}")

    if args.preview:
        prev = os.path.abspath(args.preview)
        ab = args.ab
        if ab is None:
            cand = os.path.join(here, "..", "import", "chairs", "K", "SkiLiftChair6K")
            ab = os.path.abspath(cand) if os.path.isdir(cand) else None
        for png in preview_rig(chair, asset_dir, name, prev, lods, ab):
            print(f"[chair] preview -> {png}")

    for lod in lods:
        me = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(me)

    if args.verify:
        ok0, info0 = verify_export(os.path.join(asset_dir, f"{name}.fbx"))
        results = [ok0]
        for k in range(1, len(lods) + 1):
            okk, _ = verify_export(os.path.join(asset_dir, f"{name}_LOD{k}.fbx"), histogram=False)
            results.append(okk)
        if not all(results):
            raise RuntimeError(f"verify: height contract failed on re-import: {results}")
        if not info0["custom_normals"]:
            raise RuntimeError("verify: LOD0 re-import carries no custom normals")
        print(f"PASS lift_chair: {name} {tris} tris LOD0"
              + (f" / {'/'.join(str(l) for l in LOD_TARGETS)} chain" if lods else "")
              + f", height {info0['height_m']:.3f} m on every level, finish "
              f"{'on' if finished else 'off'}, normals histogram {info0['normals_per_vertex']}")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - make the exit status unambiguous for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)
