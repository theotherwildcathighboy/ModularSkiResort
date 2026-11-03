"""
Procedural chairlift tower generator for the Modular Ski Resort mod.

V2 (towers/O, asset round 2026-10) - the default entry point:
    blender --background --factory-startup --python-exit-code 1 --python lift_tower.py --         --variant {FG8|FG12|FG16|FG20|ET62|ET68|DQ10|DQ12|DQ14|DQ16|ETDQ|GD12|GD16|GD20} --out <letter folder>         [--head ingest|rebuild] [--preview DIR] [--review [DIR]] [--export]         [--no-finish] [--no-lods] [--no-validate] [--name NAME] [--letter O]
    Contract: docs/research/art/assets-2026-10/design/towers-v2.md + towers-v2-skeptic.md
    (see the "v2 PORT" banner below). Public: TowerSpec, VARIANTS, build_tower_v2,
    assert_tower_contracts, ingest_gantry, lite_sheave, crashpad_parts, lod_chain_v2,
    assert_budgets, render_tower_views, main_v2, pillar_scoring (Revision 1: FG20 / DQ16 rows;
    Revision 1b: the gondola tubular family GD12 / GD16 / GD20, the DQ rebuild head stretched to
    the 6.0 m gauge - see the "GD" constants block).
    `--legacy` routes to the unchanged J..N main() below (e.g. `-- --legacy --out ...
    --mast 8 --pier --finish`); its functions are kept byte for byte for their consumers.

LEGACY (J..N) description follows.

Phase 1 deliverable: prove the whole art pipeline (script -> mesh -> UVs -> LODs -> FBX)
on the cheapest possible asset before investing in anything else.

Run headless:
    blender --background --factory-startup --python lift_tower.py -- --out ../fbx --preview ../preview

Everything is parametric because real lift towers ARE parametric - a resort's towers are the
same hardware repeated at different heights. That is exactly why this asset was chosen first.

Orientation convention (matters for the CS2 network prefab):
    +X = direction of lift travel (the cable runs along X)
    +Y = crossarm span (sheave axles point along Y)
    +Z = up
Origin sits at the BASE of the tower, on the ground plane, so the network can place it
directly on terrain with no offset.
"""

import bpy
import bmesh
import math
import os
import sys
import argparse
from dataclasses import dataclass, field

import mathutils

# Sibling module with the CS2 asset-folder contract (textures, settings.json, FBX export).
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402


# --------------------------------------------------------------------------------------
# Parameters
# --------------------------------------------------------------------------------------

DEFAULT_NAME = "SkiLiftTowerFixedGrip"   # no underscore - see cs2_asset.validate_asset_name

# Atlas quadrants (cs2_asset.lift_atlas_quads layout, shared with stations and chairs)
QUAD_CONCRETE, QUAD_GALV, QUAD_PAINT = 0, 1, 3


@dataclass
class TowerParams:
    """All tower dimensions in meters. CS2 uses 1 unit = 1 meter, same as Blender default."""

    # Foundation
    pad_size: float = 1.8
    pad_height: float = 0.45
    pad_sink: float = 0.30          # how far the pad sits below origin, to bed into terrain

    # Mast - real lift towers are constant-diameter steel tube.
    #
    # 8 m rather than the original 10.5 m. Real fixed-grip towers run 6-12 m so both are
    # accurate, but calibrated against the official ExampleAssets (site fence 2.36 m, adult
    # tree 7.17 m, firehouse 17.9 m) 10.5 m read as too tall and too slender in game. 8 m with
    # a slightly fatter mast sits better. Real lift lines vary tower height along the line;
    # height variants are a later addition.
    mast_height: float = 8.0
    mast_radius: float = 0.38
    mast_segments: int = 12         # 12-gon reads as round at game distance, stays cheap

    # Crossarm
    arm_span: float = 3.6           # tip to tip along Y
    arm_thickness: float = 0.22
    arm_depth: float = 0.34

    # Sheave trains (the wheel assemblies the cable rides on)
    sheave_count: int = 4           # per side
    sheave_radius: float = 0.20
    sheave_width: float = 0.13
    sheave_segments: int = 12       # 10 read as visibly polygonal up close; 12 is the sweet spot
    sheave_spacing: float = 0.52    # along X, direction of travel

    # Vertical layout of the rocker tree, measured down from the crossarm. Kept here rather
    # than inline so the sheave axle height has ONE source of truth - the CS2 lane Position Y
    # must match it exactly or the ropes float past the wheels.
    hanger_len: float = 0.40        # crossarm tip -> main balance beam
    main_to_sub: float = 0.30       # main beam -> sub-rocker
    sub_to_axle: float = 0.26       # sub-rocker -> wheel axle

    # Ladder
    ladder_width: float = 0.42
    ladder_rail_radius: float = 0.035
    ladder_rung_radius: float = 0.025
    ladder_rung_spacing: float = 0.35

    # LOD decimation ratios (LOD0 is the full mesh)
    lod_ratios: tuple = (0.45, 0.18)

    @property
    def sheave_axle_height(self):
        """
        Height of the sheave axles above the tower base.

        This is exactly the value CS2's NetPieceLanes `Position.Y` must use, or the ropes hang
        past the wheels instead of sitting in them. Derived, never hand-measured.
        """
        return self.mast_height - self.hanger_len - self.main_to_sub - self.sub_to_axle

    @property
    def lane_offset(self):
        """Lateral offset of each rope: the lane `Position.X`, mirrored +/-."""
        return self.arm_span / 2.0


# --------------------------------------------------------------------------------------
# Scene helpers
# --------------------------------------------------------------------------------------

def clear_scene():
    """Factory startup still ships a cube, camera and light. Remove everything."""
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def add_box(name, size, location):
    """Axis-aligned box. `size` is full extents (x, y, z), not half-extents."""
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size[0], size[1], size[2])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return obj


def add_cylinder(name, radius, depth, location, rotation=(0.0, 0.0, 0.0), segments=12):
    """Cylinder along local Z by default; rotate to reorient."""
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=segments, radius=radius, depth=depth,
        location=location, rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
    return obj


# --------------------------------------------------------------------------------------
# Tower construction
# --------------------------------------------------------------------------------------

def build_sheave_train(p: TowerParams, side: int, arm_z: float):
    """
    One sheave assembly hanging under a crossarm tip.

    Real fixed-grip towers use a *rocker tree*: a main balance beam hangs from the crossarm,
    and pivoting sub-rockers hang from that, each carrying a pair of sheaves. The articulation
    lets the wheel train follow the cable's angle. It is also the silhouette detail that makes
    the asset read as a chairlift rather than a power pylon, so it is worth the ~200 triangles.

    `side` is +1 or -1 along Y. Wheels roll on the cable, so their axles point along Y,
    which means each wheel cylinder's local Z is rotated onto the world Y axis.
    """
    parts = []
    y = side * (p.arm_span / 2.0)

    # Vertical layout, measured down from the crossarm (constants live in TowerParams so the
    # axle height has a single source of truth - see TowerParams.sheave_axle_height)
    hanger_len = p.hanger_len
    main_z = arm_z - hanger_len                      # main balance beam
    sub_z = main_z - p.main_to_sub                   # sub-rockers
    axle_z = sub_z - p.sub_to_axle                   # wheel axles

    n_sub = max(1, p.sheave_count // 2)              # sub-rockers, 2 sheaves each
    sub_span = p.sheave_spacing                      # wheel pair separation

    # Sub-rocker separation must leave the *inner* wheels of adjacent sub-rockers clear of
    # each other. Those wheels sit (main_span - sub_span) apart, so that gap has to exceed
    # one wheel diameter. Deriving it beats eyeballing: at sheave_radius 0.20 the previous
    # hand-picked 0.34 clearance overlapped the inner pair by 6 cm.
    sub_clear = 2.0 * p.sheave_radius + 0.10
    main_span = (n_sub - 1) * (sub_span + sub_clear)

    # Hanger: crossarm tip down to the main rocker
    parts.append((add_box(
        f"hanger_{side}",
        (0.16, 0.15, hanger_len + p.arm_thickness),
        (0.0, y, main_z + hanger_len / 2.0),
    ), QUAD_PAINT))

    # Main balance beam
    parts.append((add_box(
        f"rocker_main_{side}",
        (main_span + 0.40, p.arm_thickness * 0.7, 0.15),
        (0.0, y, main_z),
    ), QUAD_PAINT))

    for s in range(n_sub):
        sx = -main_span / 2.0 + s * (main_span / max(1, n_sub - 1)) if n_sub > 1 else 0.0

        # Pivot pin joining main beam to this sub-rocker
        parts.append((add_cylinder(
            f"pivot_{side}_{s}",
            radius=0.045, depth=0.22,
            location=(sx, y, (main_z + sub_z) / 2.0),
            rotation=(math.radians(90.0), 0.0, 0.0),
            segments=6,
        ), QUAD_GALV))

        # Sub-rocker beam
        parts.append((add_box(
            f"rocker_sub_{side}_{s}",
            (sub_span + 0.30, p.arm_thickness * 0.55, 0.12),
            (sx, y, sub_z),
        ), QUAD_PAINT))

        # Its pair of sheaves
        for w in (-1, 1):
            wx = sx + w * sub_span / 2.0
            parts.append((add_box(          # short arm down to the axle
                f"sheave_arm_{side}_{s}_{w}",
                (0.09, 0.10, 0.26),
                (wx, y, (sub_z + axle_z) / 2.0),
            ), QUAD_PAINT))
            parts.append((add_cylinder(
                f"sheave_{side}_{s}_{w}",
                radius=p.sheave_radius,
                depth=p.sheave_width,
                location=(wx, y, axle_z),
                rotation=(math.radians(90.0), 0.0, 0.0),
                segments=p.sheave_segments,
            ), QUAD_GALV))
    return parts


def build_tip_deflector(p: TowerParams):
    """
    Tip-deflector collar as a WRAPPED PLATE, not a closed cone (K clash fix).

    The J-generation collar was a full 360-degree frustum (radius 0.62 flaring down from
    0.40 over 0.50 m, centered at axle - 0.42); its skirt passed straight through the
    service ladder, whose rails run up the -X face at x = -(mast_radius + 0.10) = -0.48.
    The assembly drawing's real part is a bent plate bolted AROUND the mast - and on a
    real tower it stops at the ladder, exactly like our crash pad's open back.

    So: a conical shell arc of 280 degrees, gap centered on -X. The gap's 80-degree
    sector keeps every plate texel at least ~0.13 m clear of the ladder envelope
    (rails at y +/-0.245, rungs inside them): at the widest radius (0.62, bottom) the
    plate edge sits at (-0.475, +/-0.399) while the ladder corridor spans |y| <= 0.245.
    Same z band and radii as the J cone, so the silhouette from the line is unchanged.

    Built like build_crash_pad (thin shell, from_pydata, smooth-shaded walls) so it
    reads as rolled plate rather than a 12-gon. ~100 tris.
    """
    axle = p.sheave_axle_height
    z0, z1 = axle - 0.67, axle - 0.17           # the J cone's exact 0.50 m band
    r_bot, r_top = 0.62, 0.40                   # flares downward, per the drawing
    t = 0.03                                    # bent-plate thickness
    segs = 12
    half = math.radians(280.0) / 2.0            # open sector faces -X (the ladder)

    angles = [-half + i * (2.0 * half) / segs for i in range(segs + 1)]
    n = segs + 1
    verts = []
    for r_off in (0.0, -t):                     # outer surface, then inner
        for z, r in ((z0, r_bot), (z1, r_top)):
            verts.extend(((r + r_off) * math.cos(a), (r + r_off) * math.sin(a), z)
                         for a in angles)

    def V(ri, zi, k):
        return ri * 2 * n + zi * n + k

    faces = []
    for k in range(segs):
        faces.append((V(0, 0, k), V(0, 0, k + 1), V(0, 1, k + 1), V(0, 1, k)))      # outer
        faces.append((V(1, 0, k + 1), V(1, 0, k), V(1, 1, k), V(1, 1, k + 1)))      # inner
        faces.append((V(0, 1, k), V(0, 1, k + 1), V(1, 1, k + 1), V(1, 1, k)))      # top rim
        faces.append((V(0, 0, k + 1), V(0, 0, k), V(1, 0, k), V(1, 0, k + 1)))      # bottom
    faces.append((V(0, 0, 0), V(1, 0, 0), V(1, 1, 0), V(0, 1, 0)))                  # end cap
    faces.append((V(0, 0, segs), V(0, 1, segs), V(1, 1, segs), V(1, 0, segs)))      # end cap

    mesh = bpy.data.meshes.new("tipdeflector")
    mesh.from_pydata(verts, [], faces)
    mesh.validate()
    obj = bpy.data.objects.new("tipdeflector", mesh)
    bpy.context.collection.objects.link(obj)

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")

    # Smooth-shade the conical walls (their normals lean ~24 degrees off horizontal,
    # z component ~0.4); rims and end caps stay flat for crisp plate edges.
    for poly in mesh.polygons:
        if abs(poly.normal.z) < 0.5:
            poly.use_smooth = True
    return obj


def build_head_details(p: TowerParams):
    """
    J-generation detail pass, from the user's Yan assembly drawing and the Pali photos
    (liftblog img_6880/6888/6914). Everything here is parametric on TowerParams so the
    short ET terminal towers get the identical kit. None of it touches the invariants:
    sheave_axle_height, lane_offset, base pivot.
    """
    parts = []
    arm_top = p.mast_height + p.arm_thickness / 2.0

    # LIFTING FRAME - the signature Yan tower-top (assembly drawing: a slim rectangular
    # steel frame standing proud above the crossarm, used to jack sheave trains during
    # maintenance). Two posts on the arm's centerline plus a top bar spanning them.
    lf_h, lf_y = 0.62, 1.15
    for side in (-1, 1):
        parts.append((add_box(
            f"liftpost_{side}", (0.09, 0.09, lf_h),
            (0.0, side * lf_y, arm_top + lf_h / 2.0)), QUAD_PAINT))
    parts.append((add_box(
        "liftbar", (0.11, lf_y * 2 + 0.30, 0.11),
        (0.0, 0.0, arm_top + lf_h + 0.055)), QUAD_PAINT))

    # CATWALK - thin perforated-plate walkway along the crossarm between the sheave
    # trains (img_6880: the maintenance plank on the arm). Offset to the -X face so the
    # ladder tops out right at its edge; railing kept to three posts + one handrail on
    # the outboard edge, per the drawing's minimal rail.
    cw_x = -(p.arm_depth / 2.0 + 0.21)
    cw_len = 2.70                                   # clear of the hangers at +/-1.8
    parts.append((add_box("catwalk", (0.42, cw_len, 0.035),
                          (cw_x, 0.0, arm_top + 0.018)), QUAD_GALV))
    for i, ry in enumerate((-cw_len / 2 + 0.05, 0.0, cw_len / 2 - 0.05)):
        parts.append((add_cylinder(f"cwpost_{i}", 0.022, 0.85,
                                   (cw_x - 0.19, ry, arm_top + 0.46),
                                   segments=6), QUAD_GALV))
    parts.append((add_cylinder("cwrail", 0.024, cw_len,
                               (cw_x - 0.19, 0.0, arm_top + 0.88),
                               rotation=(math.radians(90.0), 0.0, 0.0),
                               segments=6), QUAD_GALV))
    # Two thin brackets tying the plank back to the arm
    for ry in (-0.9, 0.9):
        parts.append((add_box(f"cwbrace_{ry:.0f}", (0.40, 0.05, 0.05),
                              (cw_x + 0.10, ry, arm_top - 0.02)), QUAD_GALV))

    # TIP DEFLECTOR - the curved plate shield wrapped around the mast just below the
    # head (assembly drawing: guides a derailed rope clear of the tower). K fix: the J
    # version was a full 360-degree cone whose 0.62 m skirt swallowed the ladder rails
    # at x -0.48 - now it is built as the drawing actually shows it, a bent PLATE
    # wrapped around the mast with an open sector on the ladder face (-X), so the
    # ladder passes clean. See build_tip_deflector.
    parts.append((build_tip_deflector(p), QUAD_PAINT))

    # COMM-LINE INSULATORS - two small vertical posts on top of the arm near the
    # centerline (img_6914: the comm/power line rides over every tower top; the wire
    # itself is prefab-side, we only supply the hardware).
    for side in (-1, 1):
        y = side * 0.45
        parts.append((add_cylinder(f"insulator_{side}", 0.035, 0.28,
                                   (0.0, y, arm_top + 0.14), segments=6), QUAD_GALV))
        parts.append((add_box(f"insulcap_{side}", (0.09, 0.09, 0.04),
                              (0.0, y, arm_top + 0.30)), QUAD_GALV))
    return parts


def set_pad_uvs(obj):
    """
    Hand-set UVs mapping the crash pad into the reserved strip of the glass quadrant
    (cs2_asset.PAD_*), same pattern as the terminal's tension-scale plates. The OUTER
    wall unrolls arc -> u, height -> v, so the warning triangle stamped at the strip's
    center lands exactly on the +X (uphill) face. Inner wall, rims and end caps map to
    the strip's left margin, which paint_pad_strip keeps plain orange.
    """
    cp = cs2_asset.CRASHPAD
    U0, U1 = cs2_asset.PAD_U0, cs2_asset.PAD_U1
    V0, V1 = cs2_asset.PAD_V0, cs2_asset.PAD_V1
    half = math.radians(cp["arc_deg"]) / 2.0
    R, t = cp["radius"], cp["thickness"]
    z0, z1 = cp["z0"], cp["z1"]

    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new()
    uv = mesh.uv_layers.active.data
    for poly in mesh.polygons:
        n = poly.normal
        ctr = poly.center
        radial = mathutils.Vector((ctr.x, ctr.y, 0.0)).normalized()
        is_outer_wall = abs(n.z) < 0.5 and n.dot(radial) > 0.5
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            a = math.atan2(co.y, co.x)
            afrac = (a + half) / (2.0 * half)
            zfrac = (co.z - z0) / (z1 - z0)
            if is_outer_wall:
                ufrac = 0.08 + 0.84 * afrac
                vfrac = 0.03 + 0.94 * zfrac
            else:
                # Quiet left margin. Varied by angle/height/radius so no face has a
                # degenerate (zero-area) UV, which would upset tangent-space export.
                rfrac = (math.hypot(co.x, co.y) - (R - t)) / t
                ufrac = 0.008 + 0.030 * afrac + 0.015 * rfrac
                vfrac = 0.30 + 0.34 * zfrac + 0.04 * rfrac
            uv[li].uv = (U0 + ufrac * (U1 - U0), V0 + vfrac * (V1 - V0))


def build_crash_pad():
    """
    The red mast crash pad every real tower carries at skier height: an open-backed
    cylindrical shell (cs2_asset.CRASHPAD contract) around the mast base. The 90-degree
    opening faces DOWNHILL (-X, where the chairs approach and the ladder lives); the
    padding protects the uphill face and flanks that a skier can actually hit. Textured
    via the pad strip - recolorable on its own slot (2), default orange-red, with the
    white warning triangle on the uphill face.
    """
    cp = cs2_asset.CRASHPAD
    R, t = cp["radius"], cp["thickness"]
    r_in = R - t
    z0, z1 = cp["z0"], cp["z1"]
    segs = cp["segments"]
    half = math.radians(cp["arc_deg"]) / 2.0
    n = segs + 1

    angles = [-half + i * (2.0 * half) / segs for i in range(n)]
    verts = []
    for rad in (R, r_in):
        for z in (z0, z1):
            verts.extend((rad * math.cos(a), rad * math.sin(a), z) for a in angles)

    def V(ri, zi, k):
        return ri * 2 * n + zi * n + k

    faces = []
    for k in range(segs):
        faces.append((V(0, 0, k), V(0, 0, k + 1), V(0, 1, k + 1), V(0, 1, k)))      # outer
        faces.append((V(1, 0, k + 1), V(1, 0, k), V(1, 1, k), V(1, 1, k + 1)))      # inner
        faces.append((V(0, 1, k), V(0, 1, k + 1), V(1, 1, k + 1), V(1, 1, k)))      # top rim
        faces.append((V(0, 0, k + 1), V(0, 0, k), V(1, 0, k), V(1, 0, k + 1)))      # bottom
    faces.append((V(0, 0, 0), V(1, 0, 0), V(1, 1, 0), V(0, 1, 0)))                  # end cap
    faces.append((V(0, 0, segs), V(0, 1, segs), V(1, 1, segs), V(1, 0, segs)))      # end cap

    mesh = bpy.data.meshes.new("crashpad")
    mesh.from_pydata(verts, [], faces)
    mesh.validate()
    obj = bpy.data.objects.new("crashpad", mesh)
    bpy.context.collection.objects.link(obj)

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")

    # Smooth-shade the walls so the shell reads as a cylinder, not a 14-gon (the
    # closeup preview banded per facet). Rims stay flat for a crisp top edge.
    for poly in mesh.polygons:
        if abs(poly.normal.z) < 0.5:
            poly.use_smooth = True

    set_pad_uvs(obj)
    return obj


# --------------------------------------------------------------------------------------
# Foundation pier (M generation, reshaped in N - the pillar-ized line towers)
#
# Towers ground themselves on terrain and can float 1-3 m where the course rides above
# ground. Vanilla buries pylon bases; our visual equivalent is a below-grade concrete
# pier under the pad: fully buried on flat ground (below-grade renders fine - proven),
# reading as an engineered foundation where the tower floats.
#
# N revision (user verdict on the M previews): NO taper - the pier is a STRAIGHT
# vertical extension of the pad block, same rectangular footprint, straight formed
# sides, pad underside down to -4.0. And the whole pad+pier block is retoned to the
# VANILLA HV PYLON slab look: light, clean, slightly warm concrete (sampled from the
# user's reference screenshots - slab highlights ~0.83 display, matching bright
# terrain; our old 0.42 cool gray read dark and muddy next to them). When --pier is
# on, the PAD is hand-mapped into the same painted band as the pier, so pad + pier
# are one continuously-textured formed block; quad 0's photo concrete (stations,
# knoll, plinths) is deliberately untouched for coherence with shipped stations.
#
# The block's material lives in a PAINTED BAND reserved out of the top of the CONCRETE
# quadrant (quad 0) via cs2_asset.QUAD_RESERVED_TOP - the same mechanism that keeps
# quad 2's headroom for the crash-pad strip. Smooth gradients only, no crisp stamps,
# so it does NOT need density protection through the finish repack.
#
# MIP-BLEED CONTRACT (the M round's first cut failed this): quad 2's glass above
# v 0.5 is near-black, and bilinear/mip filtering averages neighbors across the quad
# boundary, so the block must never MAP against it. The band is painted up to
# PIER_V1 = 0.4995, but geometry maps only into PIER_MV0..PIER_MV1 (top 0.4900) - the
# painted rows above the mapped window are same-tone padding absorbing early mips.
# --------------------------------------------------------------------------------------

PIER_U0, PIER_U1 = 0.010, 0.490
PIER_V0, PIER_V1 = 0.4425, 0.4995     # painted band (~116 rows at the 2048 atlas)
PIER_MV0, PIER_MV1 = 0.4450, 0.4900   # window the block geometry actually maps into

PIER_TOP_Z = -0.30                    # flush with the pad underside: same footprint,
                                      # coplanar side planes, so no embed (an embedded
                                      # overlap would z-fight the coplanar pad sides)
PIER_BOT_Z = -4.0

# u-fraction split inside the band: side walls wrap the perimeter in the left zone;
# the pad's walking surface and the hidden caps take small patches in the right zone
# (painted clean - no streaks or grime gradient there).
PIER_USIDE = 0.84                     # sides map ufrac 0.02..0.84
PIER_UTOP0, PIER_UTOP1 = 0.86, 0.95   # pad top-face patch (~square texels)
PIER_UQUIET0, PIER_UQUIET1 = 0.96, 0.99

# Vanilla-slab tone (N): warm light concrete, authored display-space. Sampled targets
# render ~0.8 under full CS2 sun; 0.60 albedo lands there without blowing out.
PIER_TONE = (0.600, 0.585, 0.550)


def paint_pier_strip(base, nrm, msk, ctl, A):
    """
    Author the pad+pier block's concrete into its reserved band (post-hook signature,
    composed after cs2_asset.paint_pad_strip when --pier is on).

    Two zones by column (see PIER_USIDE): the SIDE zone wraps the block perimeter and
    is anisotropic (~820 texels around 7.2 m vs ~92 mapped rows over 4.15 m, ~5:1),
    so per-texel noise smears into faint vertical streaks - rain-run staining down
    formed concrete. Grime is GENTLE and weighted toward the BOTTOM (the buried end
    shows soil staining when a float exposes it; the top stays vanilla-clean, per the
    N verdict: display-space multipliers land ~gamma-squared in linear, so 0.86 here
    is ~0.72 linear). The PATCH zone (pad walking surface + hidden caps) is clean
    noise only. Not recolorable (ctl zeroed).
    """
    import numpy as np
    rng = np.random.default_rng(31)
    c0, c1 = int(PIER_U0 * A), int(PIER_U1 * A)
    r0, r1 = int(PIER_V0 * A), int(PIER_V1 * A)
    h, w = r1 - r0, c1 - c0
    cs = c0 + int(PIER_USIDE * w)                # side zone | patch zone split

    tone = np.array(PIER_TONE, dtype=np.float32).reshape(1, 1, 3)
    noise = rng.normal(0.0, 0.015, (h, w, 1)).astype(np.float32)
    px = np.clip(tone + noise, 0.0, 1.0)

    # Side zone only: vertical streaks + bottom-weighted soil grime
    ws = cs - c0
    streak = np.clip(1.0 + rng.normal(0.0, 0.03, (1, ws, 1)), 0.88, 1.12).astype(np.float32)
    frac = (np.arange(h, dtype=np.float32) / max(h - 1, 1)).reshape(h, 1, 1)
    grime = 1.0 - 0.14 * (1.0 - frac) ** 1.5     # 0.86 at the buried bottom -> 1.0 top
    px[:, :ws] = np.clip(px[:, :ws] * streak * grime, 0.0, 1.0)

    base[r0:r1, c0:c1, 0:3] = px
    base[r0:r1, c0:c1, 3] = 1.0
    nrm[r0:r1, c0:c1, 0:3] = (0.5, 0.5, 1.0)     # flat: the quad-0 photo normal texels
    nrm[r0:r1, c0:c1, 3] = 1.0                   # under the margin would be wrong here
    msk[r0:r1, c0:c1, 0] = 0.0
    msk[r0:r1, c0:c1, 3] = 0.24                  # clean matte concrete
    ctl[r0:r1, c0:c1, 0:3] = 0.0


def set_block_uvs(obj, hw, z_lo, z_hi):
    """
    Hand-map an axis-aligned box (the pad or the pier) into the reserved band as part
    of ONE continuous formed-concrete block (N). Vertex coords must be WORLD-space -
    the caller applies transforms first (the M round shipped a pitch-black pier
    because the cone's location was still on the object and co.z was local).

    Side walls wrap the perimeter CCW into the band's side zone, u continuous at
    three corners (the fourth carries the wrap seam exactly on the corner edge, where
    face interiors never cross it). v maps height over the WHOLE block range
    (z_lo..z_hi = -4.0..pad top), so streak columns and the v gradient run unbroken
    across the pad/pier joint. +Z caps (the pad's walking surface; the pier's hidden
    top) take the clean top patch; -Z caps take the quiet patch. All mapping stays
    inside PIER_MV0..PIER_MV1 - the painted rows above are mip insulation against
    quad 2's black glass.
    """
    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new()
    uv = mesh.uv_layers.active.data

    def band_uv(ufrac, vfrac):
        return (PIER_U0 + ufrac * (PIER_U1 - PIER_U0),
                PIER_MV0 + vfrac * (PIER_MV1 - PIER_MV0))

    for poly in mesh.polygons:
        n = poly.normal
        if n.z > 0.7 or n.z < -0.7:
            u0, u1 = (PIER_UTOP0, PIER_UTOP1) if n.z > 0 else (PIER_UQUIET0, PIER_UQUIET1)
            for li in poly.loop_indices:
                co = mesh.vertices[mesh.loops[li].vertex_index].co
                tx = co.x / (2.0 * hw) + 0.5
                ty = co.y / (2.0 * hw) + 0.5
                uv[li].uv = band_uv(u0 + tx * (u1 - u0), 0.05 + 0.90 * ty)
            continue
        # Perimeter quarter by dominant horizontal normal, CCW: +X, +Y, -X, -Y
        if abs(n.x) >= abs(n.y):
            quarter, t_of = (0.0, lambda c: (c.y + hw) / (2 * hw)) if n.x > 0 else \
                            (0.50, lambda c: (hw - c.y) / (2 * hw))
        else:
            quarter, t_of = (0.25, lambda c: (hw - c.x) / (2 * hw)) if n.y > 0 else \
                            (0.75, lambda c: (c.x + hw) / (2 * hw))
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            t = quarter + 0.25 * min(max(t_of(co), 0.0), 1.0)
            zfrac = min(max((co.z - z_lo) / (z_hi - z_lo), 0.0), 1.0)
            uv[li].uv = band_uv(0.02 + (PIER_USIDE - 0.02) * t, 0.02 + 0.96 * zfrac)


def build_pier(p: TowerParams):
    """
    The pier body (N): a STRAIGHT rectangular column - the pad block simply continued
    downward. Same footprint as the pad, straight formed sides, pad underside (-0.30)
    down to -4.0. Top face sits exactly flush against the pad bottom (same footprint
    means an embed would z-fight the coplanar side walls; the flush faces are
    back-to-back and both culled). 12 tris + caps.
    """
    pier = add_box("pier", (p.pad_size, p.pad_size, PIER_TOP_Z - PIER_BOT_Z),
                   (0.0, 0.0, (PIER_TOP_Z + PIER_BOT_Z) / 2.0))
    # World-space verts BEFORE mapping - add_box leaves location on the object, and
    # set_block_uvs reads co.z as world height (the M pitch-black-pier bug class).
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    set_block_uvs(pier, p.pad_size / 2.0, PIER_BOT_Z, p.pad_height - p.pad_sink)
    return pier


def build_ladder(p: TowerParams):
    """
    Service ladder up the -X face of the mast, full height: the rails now run all the
    way to the catwalk deck (J detail pass - previously they stopped 0.6 m short).
    """
    parts = []
    x = -(p.mast_radius + 0.10)
    top = p.mast_height + p.arm_thickness / 2.0 + 0.05

    for side in (-1, 1):
        y = side * p.ladder_width / 2.0
        parts.append((add_cylinder(
            f"ladder_rail_{side}",
            radius=p.ladder_rail_radius,
            depth=top,
            location=(x, y, top / 2.0),
            segments=6,
        ), QUAD_GALV))

    n_rungs = int(top / p.ladder_rung_spacing)
    for i in range(1, n_rungs):
        parts.append((add_cylinder(
            f"ladder_rung_{i}",
            radius=p.ladder_rung_radius,
            depth=p.ladder_width,
            location=(x, 0.0, i * p.ladder_rung_spacing),
            rotation=(math.radians(90.0), 0.0, 0.0),
            segments=6,
        ), QUAD_GALV))

    # Standoff brackets tying the ladder back to the mast, otherwise it reads as floating
    bracket_len = (p.mast_radius + 0.10) - p.mast_radius + 0.12
    for frac in (0.18, 0.52, 0.86):
        parts.append((add_box(
            f"ladder_standoff_{frac}",
            (bracket_len, 0.05, 0.05),
            (-(p.mast_radius + 0.05), 0.0, top * frac),
        ), QUAD_GALV))
    return parts


# Parts that take the finishing-pass bevel: the boxy steel. Cylinders already read round,
# the crash pad has hand-set UVs a bevel would smear, and tiny fittings aren't worth tris.
BEVEL_PREFIXES = ("pad", "crossarm", "hanger_", "rocker_main", "rocker_sub",
                  "sheave_arm", "liftpost", "liftbar", "catwalk", "cwbrace")


def build_tower(p: TowerParams, finish=False, pier=False):
    """
    Assemble every part, then join into a single mesh object.

    `finish=True` runs the finishing-pass bevel (J follow-up experiment): a modest
    0.02 m single-segment bevel on the boxy parts, applied BEFORE unwrap so the bevel
    faces get atlas UVs like everything else. Razor-sharp box edges are the single
    biggest procedural tell against vanilla assets - a bevel catches a specular line.

    `pier=True` (M generation; N extends it to the ET terminal towers too - they float
    where terrain drops at the station mouths) adds the below-grade foundation pier
    and folds the pad into the painted block. Everything above z=0 is geometrically
    untouched: bounds.max must stay mast + arm/2 + 0.90 exactly (the top-anchor spec
    depends on it); only bounds.min drops from -0.30 to -4.0.
    """
    parts = []

    # Foundation pad, bedded into the ground
    pad = add_box(
        "pad",
        (p.pad_size, p.pad_size, p.pad_height),
        (0.0, 0.0, p.pad_height / 2.0 - p.pad_sink),
    )
    if pier:
        # N: the pad joins the painted block - hand-mapped into the reserved band
        # continuously with the pier (quad None skips the quadrant unwrap; the
        # crash-pad pattern), so pad + pier read as one light formed-concrete
        # block in the vanilla pylon-slab tone. World-space verts first.
        bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
        set_block_uvs(pad, p.pad_size / 2.0, PIER_BOT_Z, p.pad_height - p.pad_sink)
        parts.append((pad, None))
        parts.append((build_pier(p), None))
    else:
        parts.append((pad, QUAD_CONCRETE))

    # Mast
    parts.append((add_cylinder(
        "mast",
        radius=p.mast_radius,
        depth=p.mast_height,
        location=(0.0, 0.0, p.mast_height / 2.0),
        segments=p.mast_segments,
    ), QUAD_PAINT))

    # L-generation: TALL masts (the pillar-ized line towers, 12/16 m) are the same
    # head on a longer tube - real tall fixed-grip towers stack extra mast SECTIONS
    # and keep the head hardware unchanged. A slim flange collar marks each section
    # joint so the tube reads as stacked steel rather than one stretched extrusion.
    # Kicks in above 9 m only: the K-class towers (6.2 / 6.8 / 8.0) rebuild
    # identically. Collar radius +0.03 stays inside the ladder envelope (rails at
    # x -0.445..-0.515), and the ~5-6 m joint spacing clears the crash pad (top
    # 1.90), the tip-deflector band (axle -0.67..-0.17) and the ladder standoffs.
    if p.mast_height > 9.0:
        n_sections = max(2, int(round(p.mast_height / 6.0)))
        for j in range(1, n_sections):
            parts.append((add_cylinder(
                f"mastflange_{j}",
                radius=p.mast_radius + 0.03,
                depth=0.08,
                location=(0.0, 0.0, j * p.mast_height / n_sections),
                segments=p.mast_segments,
            ), QUAD_PAINT))

    # Crossarm across the top
    arm_z = p.mast_height
    parts.append((add_box(
        "crossarm",
        (p.arm_depth, p.arm_span, p.arm_thickness),
        (0.0, 0.0, arm_z),
    ), QUAD_PAINT))

    # Sheave trains both sides
    for side in (-1, 1):
        parts.extend(build_sheave_train(p, side, arm_z))

    parts.extend(build_head_details(p))
    parts.extend(build_ladder(p))

    # Crash pad around the mast base (J follow-up). quad=None: its UVs are hand-set
    # into the reserved pad strip, so it must skip the quadrant unwrap below.
    parts.append((build_crash_pad(), None))

    # Finishing pass (optional): bevel the boxy steel before unwrap
    if finish:
        for obj, quad in parts:
            if pier and obj.name == "pad":
                # N: the pad is part of the hand-mapped block - a bevel would smear
                # its band UVs (crash-pad rule), and the vanilla slab reference is
                # crisp formed concrete anyway. The pier is never beveled either.
                continue
            if obj.name.startswith(BEVEL_PREFIXES):
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

    # Per-part UVs into the shared atlas quadrants BEFORE joining
    for obj, quad in parts:
        if quad is not None:
            cs2_asset.unwrap_into_quadrant(obj, quad)

    # Join everything into one object
    bpy.ops.object.select_all(action="DESELECT")
    for obj, _ in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()

    tower = bpy.context.active_object
    tower.name = DEFAULT_NAME

    # Merge coincident verts introduced by the join
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")

    # join() inherits the origin of the first object joined - the foundation pad, whose center
    # sits 7.5 cm below ground. Pin the origin to the world origin instead, so the asset's pivot
    # is exactly the base of the mast at ground level, as documented at the top of this file.
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

    # Built with +X as travel for readability; CS2 networks run along Z. See cs2_asset.
    cs2_asset.orient_for_net(tower)

    return tower


# --------------------------------------------------------------------------------------
# UVs, LODs, export
# --------------------------------------------------------------------------------------

def unwrap(obj):
    """Smart-project UVs. Good enough for hard-surface metal; no hand seams needed."""
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")


def tri_count(obj):
    """Triangle count of the evaluated mesh."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    n = len(bm.faces)
    bm.free()
    return n


def make_lod(source, ratio, name):
    """Duplicate + decimate. CS2 expects explicit LOD meshes rather than generating them."""
    lod = source.copy()
    lod.data = source.data.copy()
    lod.name = name
    bpy.context.collection.objects.link(lod)

    bpy.context.view_layer.objects.active = lod
    mod = lod.modifiers.new(name="Decimate", type="DECIMATE")
    mod.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return lod


# FBX export lives in cs2_asset.export_fbx - shared by every asset generator.


# --------------------------------------------------------------------------------------
# Finishing pass, texture half: baked AO (J follow-up experiment)
# --------------------------------------------------------------------------------------

def apply_finish_textures(tower, asset_dir, name, atlas_size,
                          reserved_strips=None, ao_samples=48, *,
                          packer=None, post_compose=None):
    """
    Bake ambient occlusion into the asset's own atlas BaseColor.

    L hooks (art-pipeline-v2, keyword-only, default None = the byte-identical legacy path):
      packer(obj, mesh, A, reserved)  replaces ONLY step 1's pack+squeeze on the already
                                      created, active "UVUnique" layer (cs2_uv.finish_packer:
                                      pin by deselection, average_islands_scale, density_w,
                                      one pack_islands, v-band squeeze).
      post_compose(base, nrm, msk, ctl, ao, mesh, A) -> (base, nrm, msk, ctl)
                                      called in LINEAR space after the AO multiply and
                                      BEFORE the sRGB encode (the detail layer /
                                      cs2_compose.compose_maps go here; pass ao=None to
                                      compose_maps or the occlusion is applied twice).

    The naive version (bake AO over the existing UVs, multiply into BaseColor) CANNOT
    work with our layout: every part is smart-projected into a shared quadrant, so
    islands OVERLAP - dozens of surfaces claim the same texels, and an AO bake would
    scribble each surface's occlusion over the others'. The fix is a re-layout:

      1. add a second UV layer and pack all islands uniquely (no rotation - rotating
         islands would invalidate the tangent-space normal map), pinning stamped-strip
         islands in place for texel-density protection,
      2. EMIT-bake all four maps from the old atlas UVs into the new layout (a pure
         texture-space reprojection; 1 sample, deterministic),
      3. bake AO on the new unique layout (Cycles CPU) and multiply it into the
         rebaked BaseColor, after restoring the stamped strips 1:1,
      4. drop the old UV layer so the FBX ships the unique layout, and overwrite the
         asset's four PNGs with the rebaked set.

    `tower` is any joined lift asset (the terminals reuse this). `reserved_strips` is
    a list of (u0, v0, u1, v1) atlas rects whose texels and islands are protected;
    default is the crash-pad strip. `ao_samples`: 48 suits a 2048 atlas; the 4096
    stations drop to 32 to keep headless bake time sane.

    Per-asset safe: each asset already owns its atlas copy. Returns True on success;
    on any bake failure restores a sane state and returns False so the caller ships
    bevels alone (the coordinator-approved fallback).
    """
    import numpy as np
    scene = bpy.context.scene
    mesh = tower.data
    A = atlas_size

    try:
        # -- 1. unique UV layout with PINNED stamp strips -----------------------------
        # TEXEL-DENSITY PROTECTION (user verdict on the first A/B: AO/bevels win, but
        # chunky stamps are a no-go). Stamped regions - the crash-pad strip, and the
        # tension-scale strip when a terminal calls this - must keep their crisp
        # native density through the repack. Islands whose UVs lie inside a reserved
        # strip KEEP their coordinates untouched; every other island packs into the
        # tallest strip-free v-band (v 0..0.94 for the classic top strips). After the bakes,
        # the strip rectangles are copied 1:1 from the source atlas, so the stamps
        # ship at exactly the texels their painters authored.
        reserved = reserved_strips or [(cs2_asset.PAD_U0, cs2_asset.PAD_V0,
                                        cs2_asset.PAD_U1, cs2_asset.PAD_V1)]
        mesh.uv_layers.active.name = "UVAtlas"
        uniq = mesh.uv_layers.new(name="UVUnique", do_init=True)   # copies UVAtlas
        mesh.uv_layers.active = uniq

        if packer is not None:
            # L: the caller's packer owns the layout (cs2_uv.finish_packer); it must leave
            # the object in OBJECT mode with "UVUnique" active, exactly as the inline
            # block below does.
            packer(tower, mesh, A, reserved)
        else:
            eps = 1e-4
            uvd = uniq.data

            def in_reserved(u, v):
                return any(u0 - eps <= u <= u1 + eps and v0 - eps <= v <= v1 + eps
                           for (u0, v0, u1, v1) in reserved)

            pinned = {poly.index for poly in mesh.polygons
                      if all(in_reserved(*uvd[li].uv) for li in poly.loop_indices)}

            bpy.ops.object.select_all(action="DESELECT")
            tower.select_set(True)
            bpy.context.view_layer.objects.active = tower
            scene.tool_settings.use_uv_select_sync = True
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_mode(type="FACE")
            bpy.ops.mesh.select_all(action="SELECT")
            bm = bmesh.from_edit_mesh(mesh)
            bm.faces.ensure_lookup_table()
            for i in pinned:
                bm.faces[i].select_set(False)       # pack_islands only moves selected islands
            bmesh.update_edit_mesh(mesh)
            try:
                bpy.ops.uv.pack_islands(rotate=False, margin=0.004)
            except TypeError:
                bpy.ops.uv.pack_islands(margin=0.004)                  # older signature
            bpy.ops.object.mode_set(mode="OBJECT")

            # Squeeze the packed islands into the tallest v-band free of EVERY reserved
            # rect (rects treated as full-width - conservative). Uniform per-loop
            # v-scale+offset is island-safe (linear map). With the historical top strips
            # (crash pad / tension scale, v >= 0.945) the tallest free band is v 0..0.945
            # and, with the 0.005 margin, this reproduces the original v * 0.94 squeeze
            # exactly. MID-ATLAS strips (the lodge's wood/stone bands) are honored too:
            # packed islands can no longer land inside a rect where the 1:1 restore
            # below would stomp them, and the AO bake never sees overlapped islands.
            v_edges = sorted({0.0, 1.0} | {r[1] for r in reserved} | {r[3] for r in reserved})
            free = [(a, b) for a, b in zip(v_edges, v_edges[1:])
                    if not any(v0 - eps <= (a + b) / 2.0 <= v1 + eps
                               for (_u0, v0, _u1, v1) in reserved)]
            g0, g1 = max(free, key=lambda s: s[1] - s[0])
            lo = g0 + (0.005 if g0 > 0.0 else 0.0)
            hi = g1 - 0.005
            uvd = mesh.uv_layers["UVUnique"].data   # re-fetch: mode switches invalidate it
            for poly in mesh.polygons:
                if poly.index not in pinned:
                    for li in poly.loop_indices:
                        u, v = uvd[li].uv
                        uvd[li].uv = (u, lo + v * (hi - lo))

        # -- 2. bake material: emission of the old-UV atlas, target = active image ----
        mat = bpy.data.materials.new(name + "BakeMat")
        mat.use_nodes = True
        nt = mat.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        emit = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(emit.outputs[0], out.inputs[0])
        uvn = nt.nodes.new("ShaderNodeUVMap")
        uvn.uv_map = "UVAtlas"
        src = nt.nodes.new("ShaderNodeTexImage")
        nt.links.new(uvn.outputs[0], src.inputs[0])
        tgt = nt.nodes.new("ShaderNodeTexImage")                   # bake target
        mesh.materials.clear()
        mesh.materials.append(mat)

        scene.render.engine = "CYCLES"
        scene.cycles.device = "CPU"
        scene.render.bake.margin = 8

        def bake_map(src_png, is_data, use_alpha=False, bake_type="EMIT", samples=1):
            """Bake one map into a fresh target image, return (A,A,4) float array."""
            img = None
            if src_png is not None:
                img = bpy.data.images.load(os.path.join(asset_dir, src_png))
                img.colorspace_settings.name = "Non-Color" if is_data else "sRGB"
                src.image = img
                for l in list(nt.links):
                    if l.to_node == emit:
                        nt.links.remove(l)
                nt.links.new(src.outputs["Alpha" if use_alpha else "Color"],
                             emit.inputs[0])
            target = bpy.data.images.new(f"{name}_bake", width=A, height=A,
                                         alpha=True, float_buffer=True, is_data=is_data)
            tgt.image = target
            nt.nodes.active = tgt
            scene.cycles.samples = samples
            bpy.ops.object.select_all(action="DESELECT")
            tower.select_set(True)
            bpy.context.view_layer.objects.active = tower
            bpy.ops.object.bake(type=bake_type)
            buf = np.empty(A * A * 4, dtype=np.float32)
            target.pixels.foreach_get(buf)
            arr = buf.reshape(A, A, 4).copy()
            bpy.data.images.remove(target)
            if img is not None:
                bpy.data.images.remove(img)
            return arr

        base = bake_map(f"{name}_BaseColor.png", is_data=False)
        nrm = bake_map(f"{name}_Normal.png", is_data=True)
        msk = bake_map(f"{name}_MaskMap.png", is_data=True)
        msk_a = bake_map(f"{name}_MaskMap.png", is_data=True, use_alpha=True)
        ctl = bake_map(f"{name}_ControlMask.png", is_data=True)
        ao = bake_map(None, is_data=True, bake_type="AO", samples=ao_samples)

        # -- 3. compose ---------------------------------------------------------------
        nrm[..., 3] = 1.0
        msk[..., 3] = msk_a[..., 0]          # smoothness rides MaskMap alpha
        ctl[..., 3] = 0.0                    # official assets: ControlMask alpha 0

        # Restore the stamped strips 1:1 from the source atlas. Their islands were
        # pinned, so geometry maps to these exact texels - but the EMIT bake resampled
        # them bilinearly (soft) and dilated margins into the rect edges. The copy puts
        # back precisely the texels the painters authored. BaseColor rects are
        # linearized on the way in so the shared encode below returns them bit-close.
        def srgb_to_lin(x):
            return np.where(x <= 0.04045, x / 12.92,
                            np.power((x + 0.055) / 1.055, 2.4))

        for suffix, dst in (("BaseColor", base), ("Normal", nrm),
                            ("MaskMap", msk), ("ControlMask", ctl)):
            img = bpy.data.images.load(os.path.join(asset_dir, f"{name}_{suffix}.png"))
            buf = np.empty(A * A * 4, dtype=np.float32)
            img.pixels.foreach_get(buf)
            src_px = buf.reshape(A, A, 4)
            for (u0, v0, u1, v1) in reserved:
                sl = (slice(int(v0 * A), int(v1 * A)), slice(int(u0 * A), int(u1 * A)))
                if suffix == "BaseColor":
                    dst[sl][..., 0:3] = srgb_to_lin(src_px[sl][..., 0:3])
                    dst[sl][..., 3] = 1.0
                else:
                    dst[sl] = src_px[sl]
            bpy.data.images.remove(img)

        # Gentle AO: full-black corners read sooty on painted steel; floor it slightly.
        # Applied over the restored strips too, so the pad keeps its contact shadow -
        # AO is smooth, it darkens the stamp without blurring it.
        occ = np.clip(ao[..., 0:1] * 0.92 + 0.08, 0.0, 1.0)
        base[..., 0:3] *= occ
        if post_compose is not None:
            # L: detail layer / compositing in LINEAR space (cs2_compose), before the
            # encode. The hook returns the four arrays (compose_maps makes new ones).
            base, nrm, msk, ctl = post_compose(base, nrm, msk, ctl, ao, mesh, A)
        # COLORSPACE: Cycles sampled the sRGB source through an sRGB->linear conversion
        # and the EMIT bake wrote LINEAR values, but Image.pixels round-trips RAW buffer
        # values - our PNGs store display-encoded texels. Without re-encoding, the saved
        # BaseColor is linear-in-an-sRGB-file and the whole asset renders near-black
        # (first --finish run: the concrete pad came out charcoal). AO is multiplied in
        # linear (physically right), THEN the result is encoded back.
        lin = np.clip(base[..., 0:3], 0.0, 1.0)
        base[..., 0:3] = np.where(lin <= 0.0031308, lin * 12.92,
                                  1.055 * np.power(lin, 1.0 / 2.4) - 0.055)
        base[..., 3] = 1.0

        for arr, suffix, is_data in ((base, "BaseColor", False), (nrm, "Normal", True),
                                     (msk, "MaskMap", True), (ctl, "ControlMask", True)):
            cs2_asset._write_png_raw(os.path.join(asset_dir, f"{name}_{suffix}.png"),
                                     f"{name}_{suffix}", arr, A, is_data)

        # -- 4. ship the unique layout only -------------------------------------------
        mesh.uv_layers.remove(mesh.uv_layers["UVAtlas"])
        mesh.materials.clear()
        print(f"[tower] finish: rebaked 4 maps onto unique UVs + AO multiply (atlas {A})")
        return True
    except Exception as e:  # noqa: BLE001 - any bake failure means: ship bevels alone
        print(f"[tower] finish: AO/rebake pass FAILED ({e!r}); shipping bevels only")
        try:
            bpy.ops.object.mode_set(mode="OBJECT")
        except RuntimeError:
            pass
        if "UVUnique" in mesh.uv_layers and "UVAtlas" in mesh.uv_layers:
            mesh.uv_layers.remove(mesh.uv_layers["UVUnique"])
            mesh.uv_layers.active = mesh.uv_layers["UVAtlas"]
        mesh.materials.clear()
        return False


# --------------------------------------------------------------------------------------
# Preview render - so the geometry can actually be eyeballed without opening Blender
# --------------------------------------------------------------------------------------

def pick_engine():
    """Blender 5.x renamed engines over time; choose the best one actually present."""
    available = {
        item.identifier
        for item in bpy.types.Scene.bl_rna.properties["render"]
        .fixed_type.properties["engine"]
        .enum_items
    }
    for candidate in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        if candidate in available:
            return candidate
    return "BLENDER_WORKBENCH"


def world_bounds(obj):
    """World-space bounding box corners -> (center, size) as Vectors."""
    corners = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    lo = mathutils.Vector((min(c[i] for c in corners) for i in range(3)))
    hi = mathutils.Vector((max(c[i] for c in corners) for i in range(3)))
    return (lo + hi) / 2.0, (hi - lo)


def frame_camera(cam, obj, direction, resolution, margin=1.12):
    """
    Place `cam` along `direction` from the object, far enough that the whole thing fits.

    Uses the bounding SPHERE and the narrower of the two field-of-view angles, which
    guarantees a fit regardless of view direction. Sensor fit is pinned to VERTICAL so
    the FOV math is unambiguous rather than depending on Blender's AUTO heuristics.
    """
    center, size = world_bounds(obj)
    radius = size.length / 2.0

    cam.data.sensor_fit = "VERTICAL"
    cam.data.sensor_height = 24.0
    aspect = resolution[0] / resolution[1]

    angle_y = 2.0 * math.atan(cam.data.sensor_height / (2.0 * cam.data.lens))
    angle_x = 2.0 * math.atan((cam.data.sensor_height * aspect) / (2.0 * cam.data.lens))
    fov = min(angle_x, angle_y)

    distance = radius / math.tan(fov / 2.0) * margin
    cam.location = center + direction.normalized() * distance
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()


def render_preview(target_obj, out_dir, stem="lift_tower", head_z=None, pier=False):
    """
    Render a few framed views so the geometry can be checked without opening Blender.
    `head_z` is the height the detail view aims at - pass the actual mast height so the
    ET variants (6.8 / 6.2 m) frame their own heads instead of an 8 m tower's.
    `pier=True` adds a below-grade view of the foundation pier (M acceptance render:
    previews have no ground plane, so the camera simply drops below z 0 - in game this
    is what a tower floating over a gully exposes).
    """
    if head_z is None:
        head_z = TowerParams().mast_height - 0.25
    scene = bpy.context.scene
    scene.render.engine = pick_engine()
    scene.render.image_settings.file_format = "PNG"

    # Neutral studio background
    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.11, 0.13, 0.16, 1.0)
    scene.world = world

    bpy.ops.object.camera_add(location=(0.0, 0.0, 0.0))
    cam = bpy.context.active_object
    cam.data.lens = 50
    scene.camera = cam

    # Key light plus a softer fill, so shape reads rather than flattening out
    bpy.ops.object.light_add(type="SUN", location=(10.0, -12.0, 20.0))
    key = bpy.context.active_object
    key.data.energy = 4.0
    key.rotation_euler = (math.radians(50.0), 0.0, math.radians(35.0))

    bpy.ops.object.light_add(type="SUN", location=(-12.0, 8.0, 12.0))
    fill = bpy.context.active_object
    fill.data.energy = 1.4
    fill.rotation_euler = (math.radians(65.0), 0.0, math.radians(215.0))

    # (name, direction the camera sits in, resolution)
    #  'front' looks down the lift line, showing the crossarm and both sheave trains.
    views = [
        ("three_quarter", mathutils.Vector((1.0, -1.25, 0.42)), (1000, 1300)),
        ("front",         mathutils.Vector((1.0,  0.0,  0.10)), (1000, 1300)),
        ("detail_head",   mathutils.Vector((1.0, -1.10, 0.30)), (1300, 1000)),
        ("mast_head_closeup", mathutils.Vector((0.85, 1.0, 0.30)), (1200, 1000)),
        ("pad_closeup",   mathutils.Vector((-0.45, -1.0, 0.30)), (1200, 1000)),
    ]
    if pier:
        views.append(("pier_view", mathutils.Vector((1.0, -1.0, 0.10)), (1000, 1300)))

    written = []
    for name, direction, res in views:
        scene.render.resolution_x, scene.render.resolution_y = res

        if name == "detail_head":
            # Tight on the crossarm and sheave assemblies - the part worth inspecting
            head = mathutils.Vector((0.0, 0.0, head_z))
            cam.data.lens = 85
            cam.location = head + direction.normalized() * 7.5
            cam.rotation_euler = (head - cam.location).to_track_quat("-Z", "Y").to_euler()
        elif name == "mast_head_closeup":
            # Tight on the tip-deflector collar FROM THE LADDER SIDE (post-orient the
            # ladder rides +Y): the K acceptance view for the deflector/ladder clash
            # fix - the ladder must pass clean through the collar's open sector.
            # defl_z = axle - 0.42 = (mast - 0.96) - 0.42, and head_z = mast - 0.25.
            tgt = mathutils.Vector((0.0, 0.30, head_z - 1.05))
            cam.data.lens = 85
            cam.location = tgt + direction.normalized() * 5.0
            cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
        elif name == "pad_closeup":
            # Tight on the crash pad's warning triangle (post-orient it faces -Y) -
            # the acceptance test for stamp crispness through the finish repack.
            tgt = mathutils.Vector((0.0, 0.0, 1.00))
            cam.data.lens = 85
            cam.location = tgt + direction.normalized() * 5.5
            cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
        elif name == "pier_view":
            # Below grade, level with the pier: pad above, tapered column below,
            # grime band at the joint - what a floating tower exposes in game.
            tgt = mathutils.Vector((0.0, 0.0, -1.4))
            cam.data.lens = 42
            cam.location = tgt + direction.normalized() * 13.0
            cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
            # Both studio suns sit high, so everything under the pad is in a hard
            # umbra (the square pad corners paint V-shaped black wedges down the
            # column) and the frame misrepresents the authored texture - measured:
            # band texels are 0.36-0.42, render showed near 0. In game, GI and sky
            # light fill below-grade content. A temporary camera-side bounce sun
            # stands in for that; removed right after this frame renders.
            bpy.ops.object.light_add(type="SUN", location=(8.0, -8.0, -6.0))
            bounce = bpy.context.active_object
            bounce.data.energy = 2.0
            bounce.rotation_euler = (math.radians(115.0), 0.0, math.radians(45.0))
        else:
            cam.data.lens = 50
            frame_camera(cam, target_obj, direction, res)

        path = os.path.join(out_dir, f"{stem}_{name}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
        if name == "pier_view":
            bpy.data.objects.remove(bounce, do_unlink=True)   # this frame only

    return written


# --------------------------------------------------------------------------------------
# Entry point
# --------------------------------------------------------------------------------------

def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True, help="directory for FBX output")
    ap.add_argument("--preview", default=None, help="directory for preview PNG")
    ap.add_argument(
        "--name", default=None,
        help="override the asset name, e.g. SkiLiftTower_matte. Lets several material or "
             "geometry variants coexist in one scene for side-by-side comparison instead of "
             "overwriting each other.",
    )
    ap.add_argument(
        "--basecolor", type=float, nargs=3, default=[0.52, 0.53, 0.55],
        metavar=("R", "G", "B"),
        help="linear BaseColor. Use a garish value to test whether textures bind at all.",
    )
    ap.add_argument(
        "--metallic", type=float, default=0.0,
        help="MaskMap R channel. 0 = painted/dielectric, 1 = bare metal.",
    )
    ap.add_argument(
        "--smoothness", type=float, default=0.30,
        help="MaskMap A channel. Low = matte, high = glossy.",
    )
    ap.add_argument(
        "--mast", type=float, default=8.0,
        help="mast height in meters, overriding TowerParams.mast_height (default 8.0 - "
             "the line tower). 6.8 / 6.2 build the short ET terminal towers that stand "
             "at the station mouths as hosted sub-objects (J-generation).",
    )
    ap.add_argument(
        "--pier", action="store_true",
        help="M/N generations (pillar-ized towers): add the straight below-grade "
             "concrete pier (the pad block continued down to z -4.0, vanilla-slab "
             "tone) so towers that float over dropping terrain read as engineered "
             "rather than airborne. N ships it on FG line towers AND the ET "
             "terminal towers. Leave OFF only for byte-identical K/L rebuilds.",
    )
    ap.add_argument(
        "--lods", action="store_true",
        help="also export decimated LOD1/LOD2 (TowerParams.lod_ratios). Without them the "
             "tower ships LOD0-only: the importer generates NOTHING by default "
             "(LODPostProcessor Skip/Skip - DECISIONS 77/104, art-pipeline-v2.md). The "
             "tower round (M) makes the chain the default.",
    )
    ap.add_argument(
        "--finish", action="store_true",
        help="finishing-pass experiment (J follow-up): bevel the boxy steel and rebake "
             "the atlas onto unique UVs with baked AO multiplied into BaseColor. Off by "
             "default so the standard pipeline stays deterministic; used for the "
             "realism A/B comparison against vanilla assets.",
    )
    return ap.parse_args(argv)


def main():
    args = parse_args()
    p = TowerParams(mast_height=args.mast)
    clear_scene()

    tower = build_tower(p, finish=args.finish, pier=args.pier)
    # UVs already laid into atlas quadrants per part inside build_tower - no re-unwrap.
    if args.name:
        tower.name = args.name
    name = cs2_asset.validate_asset_name(tower.name)

    # CS2 wants a folder per asset, named after the asset itself.
    asset_dir = os.path.join(os.path.abspath(args.out), name)
    os.makedirs(asset_dir, exist_ok=True)

    d = tower.dimensions
    lo, hi = world_bounds(tower)[0] - world_bounds(tower)[1] / 2, world_bounds(tower)[0] + world_bounds(tower)[1] / 2
    lo_expect = PIER_BOT_Z if args.pier else -p.pad_sink
    print(f"[tower] LOD0 tris: {tri_count(tower)}")
    print(f"[tower] size (m):  {d.x:.3f} x {d.y:.3f} x {d.z:.3f}")
    print(f"[tower] z range:   {lo.z:.3f} .. {hi.z:.3f}   "
          f"(expect {lo_expect:.2f} .. {p.mast_height + p.arm_thickness / 2 + 0.90:.2f}"
          f" - catwalk handrail tops the head details; bounds.max is the top-anchor "
          f"spec and must not move)")
    print(f"[tower] CS2 m_Bounds should match these meters once unit scale is right")
    print(f"[tower] ---- CS2 NetPieceLanes settings for this tower ----")
    print(f"[tower]   lane 0 Position: X {-p.lane_offset:.2f}  Y {p.sheave_axle_height:.2f}  Z 0")
    print(f"[tower]   lane 1 Position: X {+p.lane_offset:.2f}  Y {p.sheave_axle_height:.2f}  Z 0")
    print(f"[tower]   (Y is the sheave axle height - ropes sit in the wheels only at this value)")

    # ORDER MATTERS.
    #   1. textures   - the material loads these PNGs, so they must exist first
    #   2. material   - LOD copies then inherit it automatically
    #   3. export     - an FBX exported before step 2 carries NO material, and CS2 silently
    #                   falls back to its default surface while ignoring every texture
    #
    # Painted steel is treated as a DIELECTRIC. An early pass used metallic 0.90, which has no
    # diffuse term at all and renders as pure environment reflection.
    # noise 0.012 rather than the default 0.03: at 1024 over a 8 m object the coarser grain
    # read as granite rather than painted steel.
    # Shared lift atlas: dark painted steel (recolorable slot 1) for mast/crossarm/rockers,
    # galvanized photo steel for sheaves/ladder, tinted concrete for the pad. Matches the
    # stations so the whole lift line reads as one machine.
    # post=paint_pad_strip: the crash-pad material + warning triangle live in the strip
    # reserved out of the glass quadrant (authored in cs2_asset, slot-2 recolorable).
    tex_root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "textures")
    quad_sets, control_quads = cs2_asset.lift_atlas_quads(tex_root)

    # Compose the atlas post hooks: every tower carries the pad strip; --pier towers
    # additionally paint the pier strip (M). ET / pier-less builds keep a
    # byte-identical atlas to the K/L rounds.
    def _post(base, nrm, msk, ctl, A):
        cs2_asset.paint_pad_strip(base, nrm, msk, ctl, A)
        if args.pier:
            paint_pier_strip(base, nrm, msk, ctl, A)

    textures = cs2_asset.make_texture_set_atlas(
        asset_dir, name, quad_sets, quad_size=1024, control_quads=control_quads,
        post=_post,
    )
    if args.finish:
        # Texture half of the finishing pass: unique-UV rebake + AO multiply. Falls
        # back to bevels alone if any bake misbehaves headless (documented inside).
        apply_finish_textures(tower, asset_dir, name, atlas_size=2048)
    # NO material is assigned on purpose. CS2 does not read materials out of the FBX at all -
    # the official ExampleBakedAlphaFence.fbx contains no Material, no LayerElementMaterial and
    # no FbxFileTexture, yet imports with all four textures bound. Textures are matched by MESH
    # NAME against the sibling PNGs, so a clean name is what matters, not material wiring.

    # LODs are opt-in HERE (the tower round M flips the default). The importer generates
    # none on its own - DECISIONS 77/104.
    lods = []
    if args.lods:
        for i, ratio in enumerate(p.lod_ratios, start=1):
            lod = make_lod(tower, ratio, f"{name}_LOD{i}")
            lods.append(lod)
            print(f"[tower] LOD{i} tris: {tri_count(lod)} (ratio {ratio})")

    cs2_asset.export_fbx(tower, os.path.join(asset_dir, f"{name}.fbx"))
    for i, lod in enumerate(lods, start=1):
        cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{i}.fbx"))
        lod.hide_render = True       # keep LODs out of the preview render

    cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))

    print(f"[tower] asset folder: {asset_dir}")
    print(f"[tower] wrote {1 + len(lods)} FBX + {len(textures)} textures + settings.json")

    if args.preview:
        import shutil
        prev_dir = os.path.abspath(args.preview)
        os.makedirs(prev_dir, exist_ok=True)
        cs2_asset.preview_material(tower, asset_dir, name)
        for png in render_preview(tower, prev_dir, stem=name, head_z=p.mast_height - 0.25,
                                  pier=args.pier):
            print(f"[tower] preview -> {png}")
        # A/B pair for the finishing-pass experiment: the same three_quarter frame,
        # tagged by whether --finish was on, so the two runs sit side by side.
        tag = "finish_on" if args.finish else "finish_off"
        shutil.copyfile(os.path.join(prev_dir, f"{name}_three_quarter.png"),
                        os.path.join(prev_dir, f"{name}_{tag}.png"))
        print(f"[tower] preview -> {os.path.join(prev_dir, f'{name}_{tag}.png')}")
        tower.data.materials.clear()   # export already happened, but keep the scene clean


# ======================================================================================
# v2 PORT - towers/O (asset round 2026-10)
#
# Contract: docs/research/art/assets-2026-10/design/towers-v2.md with
# design/towers-v2-skeptic.md applied in full (the skeptic wins where they disagree).
# Everything ABOVE this banner is the legacy J..N path and is unchanged byte for byte:
# lift_terminal.py (TowerParams at :529/:782, apply_finish_textures, frame_camera,
# pick_engine, world_bounds), snow_props.py:894 (make_lod), cs2_preview.py and the other
# consumers keep working, and `--legacy` routes to main().
#
# The v2 path: cs2_kit parts tagged with tag_parts, joined, remove_doubles, weighted
# shade_by_angle(66), geometry contracts asserted in code (C1-C17 below; a wrong build
# raises), material bake through cs2_bake (Concrete034 / Metal032 tiling + recolorable
# paint and pad, one 1024 unique set, AO 48), authored LOD chain, cs2_preview + targeted
# views, cs2_review pack, manifest, validation.
#
# Skeptic MUST-CHANGE items and where they live (M = towers-v2-skeptic.md section 3):
#   M1  shear only the gantry part; level end-plate assert ........ ingest_gantry, C15
#   M2  untilt by the measured axis rotation_difference ........... entry kit only: ETDQ is
#       rebuild-only (M3), so no shipped variant untilts; _untilt_matrix keeps the rule
#   M3  ETDQ rebuild-only ......................................... VARIANTS["ETDQ"].heads
#   M4  ETDQ per dq-terminal: axle DQ_ENTRY_ROPE_Y (3.70, see below), hosted by the base
#       station only, no head comm stub, 8 sheaves/side, inboard end <= -8.3 . VARIANTS, C13
#   M5  rail_catwalk_top kept through LOD2 (FG drift 0) ........... FG_LOD2, C12
#   M6  targeted preview views via a generator-local camera helper  render_tower_views
#   M7  ingest H_top 1.9501 +- 0.002 ............................. H_TOP_INGEST, C3
#   M8  DQ base flange r 0.60 + no vertex inside the pad shell .... DQ_FLANGE_R, C14
#   M9  sheave_ deleted at LOD2, both families (FG: M5 wins the XOR gate) FG_LOD2
#   M10 size.y = bounds.max.y; LegSize.y = leg zone above origin .. manifest prefab_notes
#   M11 kept plates renamed endplate_0..3 ......................... ingest_gantry
#   F9  rocker_sub 5 mm inboard, strict 1e-3 axial margin ......... DQ_SUB_Y, C9
#   F10 deck rails split at the portal (no rail in girder/hanger) . _dq_rails, C17
#   F11 ladder landing plates beside the ladder ................... _dq_ladder, C16
#   F12 DQ LOD1 target 1,350 ...................................... VARIANTS
#   F13 saddle = the shared block between the girder ends ......... gantry_saddle
#   F14 DQ AimHeightM 9.0 handed to the catalog ................... manifest catalog_handoff
#   review F2 (dq-family-review-geometry): decks <= 2.15, rails 2.05, hanger column clear . C18
# Revision 1 (verdicts 2026-10-11, build/lift_tower.md "Revision 1"):
#   verdict 5: one taller line member per family, FG20 (axle 19.04, top 21.014) and DQ16
#       (axle 14.0, top 15.950); new placeholder rows in their manifests .. VARIANTS, pillar_scoring
#   review F3: crash pad sits on the base-flange plate (z0 = plate top + 5 mm), triangle-
#       overlap + faceted-shell test ................................... crashpad_z0, C14
#   review F5: DQ LOD2 keeps the rebuild A-braces and both deck plates . DQ_LOD2, C12 (F5)
# ======================================================================================

import json  # noqa: E402

import numpy as np  # noqa: E402
import cs2_kit as K  # noqa: E402  (bpy + cs2_asset only: no import cycle)
import cs2_uv  # noqa: E402
import cs2_compose  # noqa: E402
# 2026-10-13 (nitpick-a, S-2): the sheave-train builder lives in cs2_sheaves (this module's code, moved
# verbatim) so the lift stations build the same trains; the towers pass THIS module's globals into it at call time.
import cs2_sheaves as S  # noqa: E402
from cs2_sheaves import (_box_uv_bm, _new_part, lite_sheave, lite_sheave_parts, _unit,  # noqa: E402,F401
                         chamfer_long_edges, _sub_pairs)
import cs2_skin  # noqa: E402  (lift alignment phase 2 round 2: the half-train rig, weights and G6 sweep)
# cs2_bake / cs2_preview / cs2_review / cs2_validate / cs2_ingest are imported inside the
# functions that use them: cs2_preview imports lift_tower at module level.

_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.normpath(os.path.join(_HERE, "..", ".."))
KAGELOK_BLEND = os.path.join(_REPO, "assets", "external", "kagelok-chair-lift", "source",
                             "Sketchfab_2017_09_02_17_41_10.blend")
KAGELOK_UID = "afd305cdd4cb450f80b59f24b4b3e236"
KAGELOK_URL = "https://sketchfab.com/3d-models/chair-lift-afd305cdd4cb450f80b59f24b4b3e236"
KAG_SCALE = 0.30                       # m per Kagelok unit (ROUND-SPEC finding 11)
# Kagelok LINE head kit (support tower). The ENTRY kit is not ingested (M3).
KAG_LINE = {"gantry": "Plateforme Pil Haut", "plates": "Pil Deco",
            "sheaves": "Roues Pil Haut", "tube": "Pil Haut.001"}
KAG_GANTRY_VERTS, KAG_GANTRY_TRIS = 144, 290

V2_LETTER = "O"
SHADE_ANGLE_V2 = 66.0
ATLAS_V2 = 1024                        # RS R18 / RS section 4: towers ship one 1024 set
AO_SAMPLES_V2 = 48                     # the finish pass' count (AO 48, Cycles CPU)
PACK_MARGIN_V2 = 6                     # station precedent: 12 px gap = 1.5 texels at mip 3
BEVEL_W = S.BEVEL_W                    # 0.02 (cs2_sheaves)

# H_top = bounds top - axle (one constant per family; D2, M7). GD = the gondola tubular family
# (Revision 1b): 2.000 = the lattice's H_top_GD (lattice-towers D2, tram_tower.GD["h_top"]).
H_TOP = {"FG": 1.974, "DQ": 1.950, "GD": 2.000}
H_TOP_INGEST = 1.9501                  # Kagelok end-plate top - chord mid, 6.5004 u x 0.30
H_TOP_TOL = {"FG": 1e-3, "DQ": 2e-3, "GD": 1e-3}
GANTRY_FAMILIES = ("DQ", "GD")         # the gantry head kit (section 4.2); FG = the Yan head

# -- FG (legacy head numbers, verbatim: lift_tower.py TowerParams / build_sheave_train) --
FG_ARM_T, FG_ARM_D, FG_ARM_SPAN = S.FG_ARM_T, 0.34, 3.6     # FG_ARM_T 0.22 (cs2_sheaves)
FG_HANGER, FG_MAIN_TO_SUB, FG_SUB_TO_AXLE = S.FG_HANGER, S.FG_MAIN_TO_SUB, S.FG_SUB_TO_AXLE   # 0.40 / 0.30 / 0.26: axle = mast - 0.96
FG_TOP_ABOVE_ARM = 0.904               # deck 0.0355 + handrail h 0.8445 + rail r 0.024

# -- DQ head (towers-v2 section 4.2, Kagelok at 0.30, gauge fitted by translation) ------
DQ_TT = 0.438                          # tube top above the axle [K]
DQ_DECK = -0.296                       # deck top below the axle (brace feet after shear) [K]
# F2 (build/dq-family-review-geometry.md), GEOMETRY fallback: the DQ chair's hanger column
# (lane 2.6 +- 0.45, the station's C8 column) is kept clear on BOTH sides of the lane. On the
# stations' spin (chain A = the right lane looking uphill, the stations built round it) the hanger
# runs OUTBOARD in both x-sign branches since nitpick A2 mirrored the chair's grip side to the
# carrier's right (lift_chair.DQ_OUTBOARD -1; the old "inboard until code round 2" reading assumed
# branch P); the inboard side stays clear for a reversed loop. The decks stop at |Y| 2.15 (were 2.492
# [K]) and the rails at 2.05 (were 2.30), so the column |Y| >= 2.15 is clear (C18); the rails
# stay 0.31 outboard of the A-braces (|Y| 1.53..1.72, skeptic F10).
DQ_DECK_Y0, DQ_DECK_Y1 = 0.695, 2.15   # deck strip |Y| (inner edge [K])
DQ_RAIL_Y = 2.05                       # deck rail line |Y| (tube r 0.028 -> outer 2.078)
DQ_HANGER_COL = dict(half=0.45, depth=1.70, rise=0.22)   # C18: lane +- half, grip top down
#   depth (the station's C8 hanger column); grip top at the rope (today) to rope + rise (Q1)
DQ_RAIL_GAP = 0.20                     # F10: rail runs stop |X| >= 0.20 short of the portal
DQ_SUB_Y = S.DQ_SUB_Y                  # 2.512, F9: 2.517 moved 5 mm inboard (outer face 2.545)
DQ_MAIN_Y = S.DQ_MAIN_Y                # 2.44
# Revision 1c (revision1b-check-gd-towers section B; verdict 7 on the tram tower, "the
# rollers are not attached to any upper stabilizing mechanism"): a sub-rocker no main rocker
# carries - the lone MIDDLE sub of an odd train (GD: 10 sheaves = 5 subs, _sub_pairs) - hangs from
# the yoke on a LINK in the main rockers' section (DQ_MAIN_Y, 0.072 thick, paint, 1-segment bevel)
# from the mains' bottom (sub center - 0.10) up into the yoke (buried 0.02); its own pivot pin runs
# through the link's lug as through a main. It was 0.125 m below the yoke with an open pin. DQ /
# ETDQ trains pair every sub (even counts): no link, unchanged. C21 (check_sub_hangers) asserts it.
DQ_LINK = S.DQ_LINK                    # w 0.15, t 0.072, below 0.10, bury 0.02; None = the pre-Revision-1c build (test)
C21_THROUGH = 0.90                     # a pin end passes through >= 90 % of its lug's thickness ...
C21_POKE = 0.02                        # ... and stands <= 2 cm proud of it (DQ pins: 1.3 / 1.6 cm)
C21_BURY = 0.005                       # a link reaches >= 5 mm into the yoke
C21_BRIDGE = 0.001                     # a main's own pin overlaps main top and yoke bottom >= 1 mm
DQ_FLANGE_R = 0.60                    # M8: bolt circle 0.468 + 0.025 = 0.493 < pad 0.50
DQ_FLANGE_T, DQ_FLANGE_Z = 0.05, 0.175     # DQ base-flange plate z 0.15..0.20
FG_FLANGE_R, FG_FLANGE_T, FG_FLANGE_Z = 0.58, 0.04, 0.17   # FG plate z 0.15..0.19 (legacy numbers)
# Review F3 (dq-family-review-geometry, Revision 1): the base-flange PLATE (r 0.58 / 0.60) ran
# through the crash-pad shell (r 0.50-0.56, which started at z 0.15 = the plate bottom) on all
# nine towers - a 2-4 cm steel lip at the pad foot. The pad now SITS on the plate: its bottom is
# the plate top + this gap (FG 0.195, DQ 0.205). C14 asserts it with a triangle-overlap test.
PAD_FLANGE_GAP = 0.005
DQ_LADDER_X = -0.55
DQ_LANDING_X0, DQ_LANDING_X1 = -0.85, -0.40   # F11 landing plates (clear of the r 0.45 mast)
DQ_GRATE_T = 0.04
DQ_GIRDER = dict(y0=0.12, y1=2.30, z0=-0.087, z1=0.423, x=0.186)   # rebuild girder [D]
DQ_HANGER = S.DQ_HANGER                # y0 2.30, y1 2.41, z0 0.25, z1 0.42, x 0.15
DQ_STATION_INBOARD_MAX = -8.3          # dq-terminal.md N8: train inboard end <= z -8.3
# ETDQ rope height = the DQ base station's lane (lift_terminal_detach Z_ROPE = 0.30 deck + 0.10
# load clearance + 3.30 hang). dq-terminal-skeptic MUST-CHANGE 1 moved it from 3.95 after
# towers-v2-skeptic M4 / ROUND-SPEC R26 had copied the pre-skeptic 3.95; integration
# 2026-10-11 (build/dq-family-integration.md). tests/test_dq_family_seams.py asserts equality.
DQ_ENTRY_ROPE_Y = 3.70

# -- GD: the gondola tubular family (Revision 1b, 2026-10-11; revision1-check-towers item 6) --
# Verdict 5 ("towers serve gondola, fixed grip and detachable alike") had no tubular
# gondola member: the gondola line's spec y 12.000 rested on a GD12 that did not exist
# (tram_tower.md O2). Contract = the GD numbers the shipped lattice towers already assume
# (tram_tower.GD; lattice-towers D2 / Q5; towers-v2 section 3 "Gondola tubular (P2)"; RS R4 / R6):
#   numerals = bounds top (towers-v2 D1, as DQ): GD12 / GD16 / GD20 = top 12 / 16 / 20;
#   axle = top - H_top_GD 2.000 = 10 / 14 / 18 (GD12 grounds at node elevation 0 on the piece
#   lane 10.0 = SkiCatalog gondola AxleM; NOT the FG "mast - 0.96": that is the Yan head's rocker
#   tree, and it would put GD12's lane at 11.04); tube top A + 0.50; mast r 0.50; comm A + 1.20.
# Head = the DQ REBUILD kit (towers-v2 section 3: rebuild only, the ingested beams do not stretch)
# with the crossarm stretched from the 5.2 m to the 6.0 m gauge: every outboard member moves out
# by GD_DY; the carriers, hangers and girder ends by GD_CAR_DY (1 cm less: their outer faces then
# sit at lane - 0.065, inside the gondola head rule dy <= -0.06 below the rope, cabins.md 8.3 /
# tram_tower.GD_HEAD_RULE); the A-braces stay at |Y| 1.622 (beam -> deck, 0.35 inboard of the
# rails); the gantry rises GD_RISE so the end-plate tops land on H_top 2.000.
GD_LANE = 3.0                          # RS R4; catalog/carriers.json gd8 lane_x_m; tram_tower.GD
GD_DY = 0.40                           # GD_LANE - 2.6 (the DQ lane)
GD_CAR_DY = S.GD_CAR_DY                # 0.39
GD_RISE = 0.05                         # H_top 2.000 - DQ 1.950
GD_TT = 0.50                           # tube top above the axle (tram_tower.GD tube_top_dA)
GD_MAST_R = 0.50                       # tram_tower.GD mast_r (lattice tube neck); RS section 4 "~1.0 m"
GD_COMM_DA = 1.20                      # comm LV through the gantry window (tram_tower.GD comm_dA)
# = tram_tower.SHEAVE, the lattice GD trains as of their Revision 1: 12-gon FLAT-rim sheaves,
# 44 tris (one band + two n-gon discs; the groove is invisible - the drawn rope runs on the axle
# line). Also what fits the RS section 4 cap: GD20 with 10 per side measured 3,662 tris on the
# DQ 72-tri lite sheave (and 4,110 with 12 per side).
GD_SHEAVE = S.GD_SHEAVE                # r 0.25, w 0.10, pitch 0.62, segs 12, groove 0.0
GD_FLANGE_R = 0.65                     # mast r + 0.15 (the DQ 0.45 + 0.15); bolt circle 0.507 + 0.025
GD_PAD_R = 0.61                        # crash pad shell r (inner 0.55 = mast + 0.05, the DQ margin;
                                       # cs2_asset.CRASHPAD radius 0.56 would sit ON the 0.50 mast)
# C18 for the gondola cabin (gd8): the hanger column, both lanes, either loop direction (the DQ
# rule: on the stations' spin the stalk runs OUTBOARD since nitpick A3 mirrored the cabin to the
# carrier's right - carriers.json gd8 outboard -Y; the inboard side stays clear for a reversed loop).
# Lateral half-width from the exported SkiGondolaCabin8A envelope (tram_tower.CARRIER_GD, the
# integration S6 boxes, both pin frames): jaw / carriage / stalk / arm_yoke reach |dy| 0.81 from
# the grip top (rope + 0.08 = carriers.json gd8 grip_top_above_rope_m) down to the arm_yoke bottom
# (rope - 2.10); the body reaches |dy| 1.10 (carriers.json gd8 bounds_lat_m [-1.05, 1.10]) from
# rope - 1.92 down to the underside rope - 4.50 (hang 4.50, grip top on the lane = ChairPin today).
# Decks / rails / posts / kicks therefore stop at |Y| <= lane - 0.81 = 2.19 (DQ: lane - 0.45).
GD_HANGER_COL = (dict(half=0.81, z0=-2.10, z1=0.08), dict(half=1.10, z0=-4.50, z1=-1.92))
# C20: the gondola carrier boxes in the lane frame (x travel about the carrier, dy = |y| - lane
# outboard +, dz = z - rope) = tram_tower.CARRIER_GD verbatim (tests/test_tower_contracts.py
# compares them), swept along the whole travel extent on both lanes with the hanger OUTBOARD
# (facing travel on the stations' spin: the cabin's right since nitpick A3, carriers.json gd8 outboard
# -Y; the shipped mesh is swept so in test_gondola_tram_seams S15); `allowed` = the accepted
# jaw-over-sheave pass.
GD_CARRIER = (
    ("jaw",      (-0.15, 0.15), (-0.04, 0.30), (-0.21, 0.08), ("sheave_",)),
    ("carriage", (-0.56, 0.56), (0.25, 0.75), (-0.59, -0.12), ()),
    ("stalk",    (-0.15, 0.15), (0.48, 0.81), (-1.74, -0.26), ()),
    ("arm_yoke", (-0.60, 0.60), (-0.70, 0.80), (-2.10, -1.58), ()),
    ("body",     (-1.18, 1.18), (-1.05, 1.10), (-4.50, -1.92), ()),
)
GD_HEAD_RULE = (("GD_head_rule", (-0.06, 6.0), (-1.62, 0.20), ("sheave_",)),)   # = tram_tower
GD_SWEEP_TOL = 0.005                   # boxes shrunk 5 mm (tram_tower SWEEP_TOL)
GD_AIM_HEIGHT_M = 11.0                 # SkiCatalog gondola AimHeightM (axle 10 / comm 11.2)
# The rest of the GD placeholder group: the shipped lattice towers (tram_tower.py --family gondola;
# towers/O/SkiGondolaLattice*O manifests): (key, top-anchor y, axle, comm, pier)
GD_LATTICE = (("Lattice25", 25.0, 23.0, 24.2, 8.0), ("Lattice32", 32.0, 30.0, 31.2, 8.0),
              ("Lattice40", 40.0, 38.0, 39.2, 8.0))
GD_STUB_LANES = {"hv": ("SkiGondolaHaulRope", "UnityGUID:5d88ac45a40b30341a79535930e3b735"),
                 "lv": ("UnityGUID:37c8abdb015384f4e8b7ce9b5b2b6c9b",
                        "UnityGUID:37c8abdb015384f4e8b7ce9b5b2b6c9b")}   # = tram_tower.STUB_LANES gd_*

# -- Detail pass 1 (2026-10-11): design/detail-pass-1-towers.md section 3 with the skeptic's
# MUST-CHANGE items (detail-pass-1-towers-skeptic.md) and DECISIONS 171 ("yes to
# both": a DQ20 member, the gondola entry tower). Build report: build/detail-pass-1-towers-o.md.
#   M1  FG16 / FG20 mast cut at one stiff_mastjoint (+32 tris) so the metric mast island fits
#       the 1024 atlas .................................................... _mast_cut_z
#   M2  relief under its texel floor raised or dropped (r, half_w, stiff_ weight) . DP1_RELIEF
#   M3  plates stamped / protected PER PART (plate_n, plate_p), never "plate_" ...... hook
#   M4  brace a|b z-fight: brace a 0.186 -> 0.176; every tube inset named ......... DP1_INSET
#   M6  (lattice members; the tube towers have no smooth-shaded member list)
#   M7  the MaskMap-variation gate restated to the noise: DT-C9 compares the tone ON vs OFF
#       compose of the same bake (std >= 0.9 * sqrt(s0^2 + (MOTTLE_STD * smooth)^2)) . hook
#   M9  conduit standoff 0.05 (clears the r + 0.04 joint rings), FG foot inside the pad gap
#       at z 1.85, DQ/GD foot on the pad rim top; data plate 45 deg off the conduit . _conduit
#   M11 metric UVs on each part's OWN axis (revolve: u = rho theta, v = meridian; caps and
#       flat flange faces planar, exactly isotropic) .................................. _uv_*
DP1_TOWER_DEPTH = 0.006                # plate front quad proud of its host face (DT-C6: 4-8 mm)
DP1_PLATE_BURY = 0.002                 # rim quads reach this far INTO the host (DT-C6: 1-3 mm)
DP1_PLATE = {"FG": (0.36, 0.16), "DQ": (0.36, 0.16), "GD": (0.36, 0.16)}   # w x h, meters
DP1_CONDUIT = dict(r=0.025, standoff=0.05, jbox=(0.22, 0.12, 0.28), fg_foot_z=1.85,
                   fg_top_below_mast=0.50, dq_top_below_deck=0.12, theta_deg=90.0)
DP1_DATAPLATE = dict(w=0.25, h=0.15, z=2.30, theta_deg=45.0)   # M9c: 45 deg off the conduit
DP1_ARREST = dict(w=0.040, d=0.020, bury=0.002)                  # fall-arrest channel
DP1_CHAMFER = S.DP1_CHAMFER            # 0.012: FG / ET rocker_sub_ + sheave_arm_ long edges
DP1_INSET = dict(fg_hanger_top=S.FG_HANGER_INSET, gd_post_bottom=0.005, dq_hanger_z=S.DQ_HANGER_Z_INSET,
                 brace_a_w=0.176)      # M4 (0 / 0.186 = the pre-pass geometry, negatives)
DP1_GUSSET_APEX = dict(t=0.012, along=0.30, drop=0.40, bury=0.003)
DP1_MAST_PPM_EST = {"FG": 66.0, "DQ": 40.0, "GD": 40.0}   # M1: px/m of the mast class (shipped)
DP1_ISLAND_MAX_PX = 1000.0             # usable 1012 px at A 1024 (6 px margins) less a guard
# DQ20 (DECISIONS 171): the stiffener rings drop to 12 segments on members whose tube tops
# carry two joints, the design's named reclaim (16 -> 12 saves 24 tris per ring)
DP1_RING_SEGS_RECLAIM = 12
GD_ENTRY_ROPE_Y = 4.85                 # gondola base station lane (dq-terminal 13: 0.30 + 0.05 + 4.50)
GD_STATION_INBOARD_MAX = -8.4          # lift_terminal_detach.GD_TOWER_KEEPOUT: x_nose -8.0 - dx 0.40
# Weathering (DECISIONS 171; build/weathering-layer.md FP6): free slot wear, no snow, the
# family intensity = the highest 0.05 step from 0.50 where every member is vanilla.ok
# (the --weather-probe run in the build report).
WEATHER_I = {"FG": 0.30, "DQ": 0.25, "GD": 0.25}   # --weather-probe 2026-10-11: FG/ET ok to 0.30;
#   DQ / ETDQ / GD12 / ETGD first WARN at 0.30 (VL2, the >15 % share; edge wear on thin slot steel)
WEATHER_PROBE = (0.50, 0.45, 0.40, 0.35, 0.30, 0.25, 0.20, 0.15, 0.10)
LINER_KEY, PLATE_KEY = S.LINER_KEY, "plate"

# Material keys -> cs2_bake.MaterialSpec fields (dicts: cs2_bake is imported lazily)
TOWER_MATERIALS = {
    "concrete": dict(texture_set="Concrete034", metres_per_tile=3.0,
                     tint=(0.661, 0.625, 0.547), rough=0.76, metal=0.0, projection="box"),
    "galv": dict(texture_set="Metal032", metres_per_tile=2.0, projection="box"),
    "paint": dict(albedo=(0.604, 0.604, 0.604), slot=1, recolor_albedo=True, metal=0.1,
                  rough=0.55, projection="box"),
    "pad": dict(albedo=(0.604, 0.604, 0.604), slot=2, recolor_albedo=True, metal=0.0,
                rough=0.75, projection="box"),
    # detail pass 1: the sheave rope liners and the tower plates (fixed colors, no slot)
    "rubber": dict(albedo=(0.035, 0.035, 0.038), metal=0.0, rough=0.80, projection="box"),
    "plate": dict(albedo=(0.69, 0.69, 0.69), metal=0.0, rough=0.45, projection="box"),
}

DENSITY_V2 = {
    "pier": 0.25, "pad": 0.75, "ladder_": 0.75,
    "mast": 1.0, "rail_": 1.0, "post_": 1.0, "flange_": 1.0, "stiff_": 1.0, "crashpad": 1.0,
    "crashpad_outer": 2.0,
    "crossarm": 1.5, "rocker_": 1.5, "yoke_": 1.5, "hanger_": 1.5, "gantry_": 1.5,
    "endplate_": 1.5, "sheave_": 1.5,
    "grate_": 1.25, "tipdeflector": 1.25,
}
# detail pass 1 keys (design 3.3; M2: stiff_ 1.5 on DQ / GD so the splice boltring draws)
DENSITY_DP1 = {"liner_": 1.5, "conduit_": 0.75, "jbox_": 1.0, "arrest_": 0.5, "gusset_apex": 1.5}
DENSITY_PLATE = {"FG": 5.8, "DQ": 9.8, "GD": 9.8}       # >= 300 px/m on the plates (DT-C7; FG20 / GD20 bind)
DENSITY_STIFF = {"FG": 1.0, "DQ": 1.5, "GD": 1.5}


def density_table(spec):
    """DENSITY_V2 + the detail-pass keys for one variant's family (plates >= 300 px/m)."""
    t = dict(DENSITY_V2)
    t.update(DENSITY_DP1)
    t["plate_"] = DENSITY_PLATE[spec.family]
    t["platerim_"] = 1.5              # 6 mm strips: the head's weight, not the plate's (margins)
    t["stiff_"] = DENSITY_STIFF[spec.family]
    # T62 round 3: the seen hub bosses (cs2_sheaves.hub_name parts, 0.1 r tall) at the base weight, not the rims' 1.5:
    # their developed islands (true area, round 3) otherwise took atlas from the galv hardware (FG8 DT-C9 0.0275 < 0.0276)
    t["sheave_hub"] = 1.0
    # lift alignment round 4: a skinned GD at 10 a side (--gd-sheaves 10, 213 parts) packs the atlas tighter and the
    # gantry girders' bolts fell under the 2-texel floor (1.98, DT-C8); the girders take 1.6 there (SKINNED only)
    if SKINNED and spec.family == "GD" and spec.sheaves >= 10:
        t["gantry_girder_"] = 1.6
        if spec.top >= 16.0:
            t.update(SKIN_GD_TALL_DENSITY)
    return t


# family round (2026-11-03): the skinned GD16 / GD20 at 10 a side pack a 1024 atlas the GD12 table does not fit: GD20 lost
# 219 of 239 edge rounds, every mast weld band and boltring (DT-C8; the clevis tongue at 1.69 texels, the yokes 1.85,
# everything else 1.98-2.0) and both plates fell to 272 / 297 px/m (DT-C7: glyph 38 / 42 < 44). Measured fix (probe, atlas
# cost = area x w^2): the plain long parts give texels (ladder 0.75 -> 0.5, deck rails / posts 1.0 -> 0.75, the buried pier
# 0.25 -> 0.15, conduit 0.75 -> 0.5), the train hardware, the mast bands and the deck grates take them (yoke 1.75, hanger /
# tongue 1.8, main rockers 1.65, girders 1.75, mast 1.08, grates 1.35, plates 11.2): GD20 / GD16 draw every relief feature,
# plates 325 / 337 px/m (glyph 46 / 47). GD12 (imported, Q) keeps its table.
SKIN_GD_TALL_DENSITY = {"ladder_": 0.5, "rail_": 0.75, "post_": 0.75, "pier": 0.15, "conduit_": 0.5, "yoke_": 1.75,
                        "hanger_": 1.8, "rocker_main_": 1.65, "gantry_girder_": 1.75, "plate_": 11.2, "mast": 1.08,
                        "grate_": 1.35}


FG_LOD1 = ("bolt_", "ladder_rung_", "ladder_standoff_", "pivot_", "insulator_", "insulcap_",
           "post_catwalk", "kick_catwalk", "rail_catwalk_knee", "cwbrace", "stiff_", "axlecap_",
           "gusset_", "plate_", "platerim_", "conduit_", "jbox_", "arrest_", "comm_")
# M5: rail_catwalk_top is NOT deleted (it is the bounds top: LOD2 drift 0).
# M9 asks to CONSIDER sheave_ for FG LOD2, gated on the lod-mode XOR. Measured 2026-10-11
# on FG8: keeping the rims gives 11.9 / 11.7 % silhouette XOR at 209 / 105 m vs 15.2 /
# 14.8 % deleted - BUT keeping them forces a 0.32 collapse ratio that erodes
# rail_catwalk_top (top drift 8 mm FG8, 16 mm FG16), breaking M5's zero drift. M5 is a
# MUST-CHANGE, so the rims are deleted (drift 0). DQ deletes them too (M9 MUST; measured
# neutral there: 10.9 / 10.5 % deleted vs 10.8 / 11.3 % kept).
FG_LOD2 = FG_LOD1 + ("sheave_", "rocker_sub_", "liftpost_", "liftbar", "grate_",
                     "tipdeflector", "ladder_rail_", "flange_", "hanger_", "liner_",
                     "yoke_")       # skinned FG only (round 4 yoke straps): they leave with the hanger
FG_KEEP = ("pad", "pier", "mast", "crossarm", "rocker_main_", "rail_catwalk_top", "crashpad")
DQ_RAIL_RUNS = ("deckn0", "deckn1", "deckp0", "deckp1")
DQ_LOD1 = (("bolt_", "ladder_rung_", "ladder_standoff_", "pivot_", "post_", "kick_", "stiff_",
            "hanger_", "gantry_saddle")
           + tuple(f"rail_{r}_knee" for r in DQ_RAIL_RUNS)
           + ("plate_", "platerim_", "conduit_", "jbox_", "arrest_", "gusset_apex", "comm_"))
# Review F5 (Revision 1): LOD2 keeps the rebuild head's A-braces (`gantry_brace` is no longer
# deleted: ETDQ stopped reading as a flat goalpost; ingest heads carry theirs inside gantry_00)
# and the two deck PLATES (`grate_deckn` / `grate_deckp`, top + underside quad = 4 tris each:
# the top view keeps the decks). Only the deck lips and the landing gratings go.
DQ_LOD2 = DQ_LOD1 + ("rail_", "grate_land", "grate_deckn_lip", "grate_deckp_lip", "rocker_sub_",
                     "ladder_rail_", "flange_", "endplate_", "sheave_", "rocker_link_", "liner_")
# Revision 1c: a middle sub's link (GD only) leaves with the subs at LOD2
DQ_LOD2_KEEP = ("grate_deckn", "grate_deckp")   # F5: asserted present at LOD2 (assert_budgets)
DQ_KEEP = ("pad", "pier", "mast", "gantry_", "rocker_main_", "yoke_", "crashpad")
LOD_TOL = 0.10
# C12 (D9 as corrected by M5): expected LOD2 bounds-top drift per family; LOD1 must be 0.
LOD2_DRIFT_EXPECT = {"FG": 0.0, "DQ": 1.950 - 1.926, "GD": 2.000 - (1.926 + GD_RISE)}
LOD2_DRIFT_RAISE = 0.02                # beyond expected + this the build fails
# Per-family LOD0 triangle cap (RS section 4: 3,500). DQ20 follow-up 2026-10-11: "no need
# to hold the cap on the towers" -> the DQ family (DQ10-DQ20 + ETDQ) is held at 3,600 so the
# transfer audit's DQ20 (3,534 rebuild / 3,554 ingest after every reclaim) ships beside DQ18.
# FG / ET and the GD family (GD12-20, ETGD) stay at 3,500; the lattice towers have their own caps.
# T62 round 2 (2026-10-18, "probably raise the cap"): the 16-segment capped wheel (cs2_sheaves.WHEEL_TRIS 224 vs
# 72 / 44) on 8 (FG) / 16 (DQ) / 16-20 (GD) wheels; each cap = the smallest 100 above the family's worst LOD0 / 0.95
# (>= 5 % headroom): FG 3,500 -> 4,600 (FG20 4,282), DQ 3,600 -> 6,400 (DQ20 5,986), GD 3,500 -> 7,500 (GD20 7,062).
# v1 builds sit under these too (a cap is a ceiling).
# T62 round 3 ("Ouch, 224 tris ... we need to reduce the tris on the wheels"): the <= 120-tri wheel (76 + 22 per
# seen hub boss: FG 120 both faces seen, DQ / GD 98 inboard face behind the sub rocker) -> every cap reset to
# max(the round-1 cap, the smallest 100 with >= 5 % headroom): FG 4,600 -> 3,700 (FG20 3,450), DQ 6,400 -> 4,200 (DQ20
# 3,970), GD 7,500 -> 4,800 (GD20 4,542). Round 1: 3,500 / 3,600 / 3,500.
# FG 3,700 -> 3,800 (lift alignment round 3): FG20 skinned builds 3,762 with the round-2 bogie bars (SKIN_FG
# bogie_low off), 3,394 with bogie_low; unskinned FG20 3,450.
# GD 4,800 -> 5,000 (lift alignment round 4): the skinned GD at the shipped 10 sheaves a side, GD12 4,626, GD20 4,938.
# DQ 4,200 -> 4,250 (family round 2026-11-03, owner: "We can raise the cap on DQ20"): the skinned DQ20 ingest builds
# 4,234 + the 16-tri comm hanger = 4,250.
LOD0_CAP = {"FG": 3800, "DQ": 4250, "GD": 5000}


@dataclass(frozen=True)
class TowerSpec:
    """One exported tower variant (towers-v2 section 3 with the skeptic's M4 / F12)."""
    key: str
    family: str             # "FG" (Yan head) | "DQ" (gantry head) | "GD" (gantry head, 6.0 gauge)
    role: str               # "line" | "terminal" (hosted)
    kind: str               # "support" | "compression" (sheaves above the rope)
    tube_top: float         # FG: mast height; DQ: tube top TT = axle + 0.438; GD: axle + 0.50
    axle: float             # lane y = sheave axle (D6)
    top: float              # bounds top = top-anchor spec y
    gauge: float            # lane spacing (2 x lane offset)
    sheaves: int            # per side
    sheave_r: float
    sheave_w: float
    pitch: float
    sag: float              # m, + = ends low; ETDQ -0.06 (compression, ends high)
    mast_r: float
    pad: float
    pier: float
    ladder_pitch: float
    ladder_w: float
    comm_y: object          # LV comm wire y on the line piece; None = hosted (station owns it)
    lod0: int               # LOD0 target (warn at +-15 %)
    lod1: int
    lod2: int
    heads: tuple = ("yan",)
    hosted_z: object = None  # station-frame z of the hosted tower (rot 180 deg)
    cap: int = 3500         # LOD0 cap; the factories below set LOD0_CAP[family]

    @property
    def h_top(self):
        return self.top - self.axle

    @property
    def name_stem(self):
        return f"SkiLiftTower{self.key}"


def _fg_spec(key, mast, role, comm, lod0):
    return TowerSpec(key, "FG", role, "support", mast, round(mast - 0.96, 6),
                     round(mast + 1.014, 6), 3.6, 4, S.FG_SHEAVE["r"], S.FG_SHEAVE["w"], S.FG_SHEAVE["pitch"], 0.0, 0.38, 1.8, 4.0,
                     0.35, 0.42, comm, lod0, 1100, 380, cap=LOD0_CAP["FG"])


def _dq_spec(key, axle, role, kind, sag, pier, comm, lod0, heads, hosted_z=None):
    return TowerSpec(key, "DQ", role, kind, round(axle + DQ_TT, 6), axle,
                     round(axle + H_TOP["DQ"], 6), 5.2, 8, S.DQ_SHEAVE["r"], S.DQ_SHEAVE["w"], S.DQ_SHEAVE["pitch"], sag, 0.45, 2.2,
                     pier, 0.30, 0.56, comm, lod0, 1350, 400, heads, hosted_z, cap=LOD0_CAP["DQ"])


def _gd_spec(key, top, sheaves, lod0):
    """GD line member: numeral = bounds top (D1), axle = top - 2.000, tube top A + 0.50, the DQ
    ladder (0.30 / 0.56) and pad (2.2), pier 4.0 (the GD group's tubular members float at most
    3.333 m before the lattice 25 takes over: pillar_scoring("GD")), RS section 4 budgets
    3,500 cap / LOD1 1,400 / LOD2 400, rebuild head only."""
    axle = round(top - H_TOP["GD"], 6)
    return TowerSpec(key, "GD", "line", "support", round(axle + GD_TT, 6), axle, top, 2 * GD_LANE,
                     sheaves, GD_SHEAVE["r"], GD_SHEAVE["w"], GD_SHEAVE["pitch"], 0.0, GD_MAST_R, 2.2,
                     4.0, 0.30, 0.56, round(axle + GD_COMM_DA, 6), lod0, 1400, 400, ("rebuild",),
                     cap=LOD0_CAP["GD"])


def _gd_entry_spec(key, lod0, lod1):
    """Detail pass 1 / towers-transfer G2 (DECISIONS 171): the GONDOLA entry compression tower,
    hosted by the gondola base station only at (0, 0, -11.0) rot 180 deg (dq-terminal.md 12-13:
    rope 4.85 = deck 0.30 + step-in 0.05 + hang 4.50; gauge 6.0). The ETDQ pattern on the GD
    head kit: rebuild head, 8 flat-rim sheaves per side (even: no middle link), train sag -0.06
    (compression: ends high), tube top A + 0.50, H_top 2.000 -> top 6.850 (inside the station's
    keep-out z < 7.0), pier 4.0, no head comm stub (the station's LV anchor owns it)."""
    axle = GD_ENTRY_ROPE_Y
    return TowerSpec(key, "GD", "terminal", "compression", round(axle + GD_TT, 6), axle,
                     round(axle + H_TOP["GD"], 6), 2 * GD_LANE, 8, GD_SHEAVE["r"], GD_SHEAVE["w"],
                     GD_SHEAVE["pitch"], -0.06, GD_MAST_R, 2.2, 4.0, 0.30, 0.56, None, lod0, lod1, 400,
                     ("rebuild",), -11.0, cap=LOD0_CAP["GD"])


# LOD0 targets: FG = towers-v2 section 11 estimates; DQ = the section 11 sums re-measured
# after the skeptic's changes (split deck rails +168, landing plates +56, 8 ETDQ sheaves) [D].
VARIANTS = {
    "FG8": _fg_spec("FG8", 8.0, "line", 8.15, 2600),
    "FG12": _fg_spec("FG12", 12.0, "line", 12.15, 2750),
    "FG16": _fg_spec("FG16", 16.0, "line", 16.15, 2950),
    # Revision 1 (verdict 5, 2026-10-11): one taller line member per family for big
    # gaps; same head, pier and spec y, scored per node elevation (pillar_scoring below).
    "FG20": _fg_spec("FG20", 20.0, "line", 20.15, 3050),
    "ET62": _fg_spec("ET62", 6.2, "terminal", None, 2550),
    "ET68": _fg_spec("ET68", 6.8, "terminal", None, 2550),
    "DQ10": _dq_spec("DQ10", 8.0, "line", "support", 0.0, 6.0, 9.20, 3100,
                     ("ingest", "rebuild")),
    "DQ12": _dq_spec("DQ12", 10.0, "line", "support", 0.0, 6.0, 11.20, 3250,
                     ("ingest", "rebuild")),
    "DQ14": _dq_spec("DQ14", 12.0, "line", "support", 0.0, 6.0, 13.20, 3300,
                     ("ingest", "rebuild")),
    # Revision 1: the DQ pattern 8 / 10 / 12 -> axle 14.0, tube top 14.438, bounds top 15.950.
    "DQ16": _dq_spec("DQ16", 14.0, "line", "support", 0.0, 6.0, 15.20, 3350,
                     ("ingest", "rebuild")),
    # M3 + M4: rebuild-only, axle DQ_ENTRY_ROPE_Y 3.70 (dq-terminal Z_ROPE), 8 sheaves/side,
    # hosted by the BASE station only at (0, 0, -11.0) rot 180 deg, no head comm stub (nose bracket).
    "ETDQ": _dq_spec("ETDQ", DQ_ENTRY_ROPE_Y, "terminal", "compression", -0.06, 4.0, None, 2950,
                     ("rebuild",), hosted_z=-11.0),
    # Revision 1b (revision1-check-towers item 6): the gondola tubular family; GD12's top 12.000
    # is the gondola line's spec y (tram_tower.GD tubular_tops). 10 sheaves per side on all three
    # (task: 10-12): 12 on GD20 overruns the RS section 4 cap of 3,500 even with the 44-tri flat
    # sheaves (measured below in the build report) [D].
    "GD12": _gd_spec("GD12", 12.0, 10, 3030),
    "GD16": _gd_spec("GD16", 16.0, 10, 3130),
    "GD20": _gd_spec("GD20", 20.0, 10, 3340),
    # Detail pass 1 (DECISIONS 171, "yes to both"): the towers-transfer audit's DQ20 (top
    # 19.950, axle 18.0, comm 19.2) did NOT fit the 3,500 cap with the design's reclaims
    # (superseded by the DQ20 follow-up below: the DQ cap is now 3,600, both members ship):
    # measured 3,554 tris (ingest head; 3,534 rebuild) after every hidden-face reclaim and the
    # 12-segment joint rings. So the audit's fallback DQ18 ships: top 17.950, axle 16.0, comm
    # 17.2, pier 6, lanes +-2.6 (wins e 5.333.. -> DQ16 5.333-7.333, DQ18 7.333-14, grounds to
    # 14), 3,498 tris on the shipped ingest head.
    "DQ18": _dq_spec("DQ18", 16.0, "line", "support", 0.0, 6.0, 17.20, 3480,
                     ("ingest", "rebuild")),
    # ... and the gondola entry compression tower (transfer audit G2).
    "ETGD": _gd_entry_spec("ETGD", 2600, 1300),
    # Follow-up 2026-10-11 (DQ20 + sub-texel faces): "no need to hold the cap on the
    # towers" -> LOD0_CAP["DQ"] 3,600, so the audit's DQ20 ships ALONGSIDE DQ18 (both are
    # height options): top 19.950, axle / UtilityObject y 18.0, comm 19.2, pier 6, legs
    # (2.2, 6, 2.2), lanes +-2.6; wins e 9.333-16 (DQ18 then wins 7.333-9.333), grounds to 16
    # = the FG line. DQ20_MEASURED below is the build report's measurement it is built on.
    "DQ20": _dq_spec("DQ20", 18.0, "line", "support", 0.0, 6.0, 19.20, 3540,
                     ("ingest", "rebuild")),
}
# DQ20 as measured by the detail pass through the shipping code path (build report), before the
# cap was raised: it did not fit 3,500 (+54 ingest / +34 rebuild) and DQ18 shipped instead; with
# LOD0_CAP["DQ"] 3,600 it fits with 46 / 66 tris of margin. The exported counts are in the manifest.
DQ20_MEASURED = {"ingest": 3554, "rebuild": 3534, "cap_then": 3500, "cap_now": LOD0_CAP["DQ"]}
# Members added AFTER the frozen manifests were written (towers-transfer audit): the frozen
# blocks of the variants above (prefab_notes.switch_points, placeholder_row_new) are computed
# without them, byte-identical (DT-C1); the new members' own manifests carry the updated family.
# POST_FREEZE_STAGE orders the additions: stage 1 = the family as DQ18's manifest froze it
# (DQ18 added, DQ20 NOT - that manifest is a frozen block too), stage 2 = with DQ20.
POST_FREEZE_STAGE = {"DQ18": 1, "DQ20": 2}
POST_FREEZE = tuple(POST_FREEZE_STAGE)
DQ_AIM_HEIGHT_M = 9.0                  # F14: catalog AimHeightM for the DQ line (axle 8 / comm 9.2)

# Revision 1 (verdict 5): the placeholder-group rows this revision ADDS. The line prefab's
# NetSubObjects entry points at the family placeholder (spec y = the shortest member's top,
# unchanged); each member is a SpawnableObject row scored per node elevation (pillar-system Q1).
# Only these variants' manifests carry `placeholder_row_new` (the R0 manifests keep their shape).
R1_NEW_ROWS = ("FG20", "DQ16")
# Revision 1b: the whole tubular GD family is new; its rows join the lattice ones in the gondola
# placeholder (code-round-2 build-core: SkiLiftTowerGD12/16/20 + SkiGondolaLattice25/32/40).
R1B_NEW_ROWS = ("GD12", "GD16", "GD20")
PLACEHOLDER = {"FG": "SkiLiftTowerPlaceholder", "DQ": "SkiLiftTowerDQPlaceholder",
               "GD": "SkiGondolaTowerPlaceholder"}
MEMBER_PREFAB = {"FG8": "SkiLiftTowerFG2"}      # the FG8 member kept its old prefab name


def pillar_scoring(family, post_freeze=False):
    """
    pillar-system Q1 for one family's LINE members (SubObjectSystem CreateSubObjects,
    PillarType.Standalone): spare s = size.y - (e + y), size.y = bounds.max.y = spec.top
    (ObjectUtils.GetSize, skeptic claim 10), y = the line NetSubObjects spec y = the shortest
    member's top, e = node elevation above terrain; score 1 / (1 + (s >= 0 ? 2 s : -s)).
    Consecutive members a < b switch at e = (h_a + 2 h_b) / 3 (h = top - y). A member's base
    sits h below the node, i.e. e - h above terrain; the pier covers a float up to `pier`,
    so the tallest member grounds nodes up to h + pier.
    GD (Revision 1b): the group also holds the shipped lattice towers (GD_LATTICE, made by
    tram_tower.py); their rows carry "generator": "tram_tower.py".
    post_freeze (detail pass 1): False / 0 = the family as frozen in the shipped manifests
    (POST_FREEZE members left out, DT-C1); True / 1 = the family with DQ18 (the DQ18 manifest);
    2 = with DQ18 and DQ20 (the DQ20 manifest; follow-up 2026-10-11). A member joins at the
    stage POST_FREEZE_STAGE gives it.
    """
    stage = int(post_freeze)
    fam = sorted((s for s in VARIANTS.values() if s.family == family and s.role == "line"
                  and POST_FREEZE_STAGE.get(s.key, 0) <= stage), key=lambda s: s.top)
    rows = [(s.key, MEMBER_PREFAB.get(s.key, f"SkiLiftTower{s.key}"), s.name_stem + V2_LETTER, s.top,
             s.axle, [s.gauge / 2.0, s.axle], s.comm_y, s.pier, None) for s in fam]
    if family == "GD":
        rows += [(k, f"SkiGondola{k}", f"SkiGondola{k}{V2_LETTER}", top, axle, [GD_LANE, axle], comm, pier,
                  "tram_tower.py") for k, top, axle, comm, pier in GD_LATTICE]
        rows.sort(key=lambda r: r[3])
    y = rows[0][3]
    hs = [round(r[3] - y, 6) for r in rows]
    switch = [round((a + 2.0 * b) / 3.0, 3) for a, b in zip(hs, hs[1:])]
    members = []
    for i, ((key, prefab, mesh, top, axle, lanes, comm, pier, gen), h) in enumerate(zip(rows, hs)):
        lo = 0.0 if i == 0 else switch[i - 1]
        hi = switch[i] if i < len(switch) else round(h + pier, 3)
        members.append({"variant": key, "member_prefab": prefab,
                        "mesh": mesh, "top_anchor_y": top,
                        "utility_object_y": axle, "hv_lanes": lanes,
                        "lv_comm_y": comm, "base_below_node_m": h,
                        "wins_e_m": [lo, hi], "base_above_terrain_m": [round(lo - h, 3), round(hi - h, 3)],
                        "pier": pier})
        if gen:
            members[-1]["generator"] = gen
    return {"placeholder": PLACEHOLDER[family], "net_subobject_y": y,
            "score": "1/(1+(s>=0 ? 2s : -s)), s = top - (e + y)", "switch_points_e_m": switch,
            "grounded_up_to_e_m": members[-1]["wins_e_m"][1], "members": members}


def switch_points_note(family=None, post_freeze=False):
    """Manifest prefab_notes.switch_points from pillar_scoring (M10's 'FG 8/3, 20/3 m; DQ 4/3,
    10/3 m' before Revision 1 added FG20 / DQ16: + 32/3 and 16/3). family "GD" (Revision 1b):
    the gondola group's own line (the FG / DQ string stays byte-identical in their manifests).
    post_freeze (detail pass 1): 1 = the string with the DQ18 point (the DQ18 manifest only);
    2 = with the DQ20 point as well (the DQ20 manifest only; follow-up 2026-10-11)."""
    if family == "GD":
        sw = pillar_scoring("GD")["switch_points_e_m"]
        return ("GD " + ", ".join(f"{int(round(v * 3))}/3" for v in sw)
                + " m (tubular GD12/16/20 + lattice 25/32/40; Revision 1b)")
    parts = []
    for fam in ("FG", "DQ"):
        sw = pillar_scoring(fam, post_freeze)["switch_points_e_m"]
        parts.append(f"{fam} " + ", ".join(f"{int(round(v * 3))}/3" for v in sw))
    if int(post_freeze) >= 2:
        return ("; ".join(parts) + " m (follow-up 2026-10-11: DQ20 adds 28/3 beside DQ18's 22/3; "
                "the other manifests keep their frozen string)")
    if post_freeze:
        return "; ".join(parts) + " m (detail pass 1: DQ18 adds 22/3; the other manifests keep the frozen string)"
    return "; ".join(parts) + " m (M10; Revision 1: FG20 / DQ16 add the last point)"


class TowerContractError(RuntimeError):
    """A towers-v2 geometry / budget contract failed: the build must not ship."""


def _check(cond, label, detail=""):
    if not cond:
        raise TowerContractError(f"{label} FAILED {detail}")
    print(f"[tower2] ok {label} {detail}")


# --------------------------------------------------------------------------------------
# Part helpers (bmesh-built, deterministic; every part carries exactly one "UVMap")
# --------------------------------------------------------------------------------------
# _box_uv_bm, _new_part, lite_sheave (the D4 lite sheave): moved to cs2_sheaves (imported above).


# --------------------------------------------------------------------------------------
# Detail pass 1 part helpers (design 3.1; every new part is LOD0-only except the liners)
# --------------------------------------------------------------------------------------
# lite_sheave_parts (discs + rubber liner), _unit: moved to cs2_sheaves (imported above).


def _planar_uv_bm(bm, layer, ref=(0.0, 0.0, 1.0)):
    """Per-face planar projection in meters, unmirrored (u x v = +normal), u along the face
    plane's projection of `ref` rotated a quarter turn (v = ref's projection: 'up')."""
    bm.normal_update()
    rv = _unit(ref)
    for f in bm.faces:
        n = f.normal
        up = rv - n * rv.dot(n)
        if up.length < 1e-6:
            alt = mathutils.Vector((1.0, 0.0, 0.0)) if abs(n.x) < 0.9 else mathutils.Vector((0.0, 1.0, 0.0))
            up = alt - n * alt.dot(n)
        up = up.normalized()
        right = up.cross(n)                    # right x up = n  (u x v = +normal)
        for lp in f.loops:
            c = lp.vert.co
            lp[layer].uv = (c.dot(right), c.dot(up))


def _quad_part(name, quads, ref=(0.0, 0.0, 1.0)):
    """Part from explicit world quads (each 4 points, CCW seen from outside), planar UVs."""
    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    cache = {}

    def vert(p):
        k = tuple(round(c, 7) for c in p)
        if k not in cache:
            cache[k] = bm.verts.new(p)
        return cache[k]
    for q in quads:
        bm.faces.new([vert(p) for p in q])
    cs2_asset.canonical_order_bmesh(bm)
    _planar_uv_bm(bm, layer, ref)
    return _new_part(name, bm)


def plate_parts(tag, centre, normal, size, depth=None, bury=None):
    """
    The tower plate (design 3.1 / 3.4): `plate_<tag>` = one front quad `depth` proud of the
    host face (6 mm), `platerim_<tag>` = 4 rim quads from its edges back to `bury` (2 mm)
    INSIDE the host plane; no back face. `centre` lies ON the host face, `normal` its outward
    normal (authoring frame, horizontal), `size` (w, h). The front quad's UVMap reads
    unmirrored from outside: u = the viewer's right (z x n), v = up, so a painter's columns
    run left to right on both faces (M3). 2 + 8 tris.
    """
    depth = DP1_TOWER_DEPTH if depth is None else depth
    bury = DP1_PLATE_BURY if bury is None else bury
    c, n = mathutils.Vector(centre), _unit(normal)
    up = mathutils.Vector((0.0, 0.0, 1.0))
    up = (up - n * up.dot(n)).normalized()
    right = up.cross(n)
    hw, hh = size[0] / 2.0, size[1] / 2.0
    f = c + n * depth
    b = c - n * bury
    corners = [(-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)]
    front = [f + right * x + up * y for x, y in corners]
    back = [b + right * x + up * y for x, y in corners]
    plate = _quad_part(f"plate_{tag}", [front])
    # the front quad's UV: right / up in meters (identical to _planar_uv_bm's frame here)
    rims = []
    for k in range(4):
        k1 = (k + 1) % 4
        rims.append([back[k], back[k1], front[k1], front[k]])
    rim = _quad_part(f"platerim_{tag}", rims)
    rim["dp_axis"] = tuple(n)                  # uv_rule: one perimeter strip about the normal
    return [(plate, PLATE_KEY), (rim, PLATE_KEY)]


def _mast_surface_y(mast_r, segs, half_w):
    """Smallest +Y radius of the mast's segs-gon (vertex at angle 0, as create_cone builds it)
    over |x| <= half_w: where a flat back face must sit to stay inside the mast."""
    pts = [(mast_r * math.cos(2.0 * math.pi * k / segs), mast_r * math.sin(2.0 * math.pi * k / segs))
           for k in range(segs + 1)]
    lo = mast_r
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        if min(y0, y1) <= 0.0:
            continue
        for x in (-half_w, 0.0, half_w):
            if min(x0, x1) - 1e-9 <= x <= max(x0, x1) + 1e-9 and abs(x1 - x0) > 1e-12:
                lo = min(lo, y0 + (y1 - y0) * (x - x0) / (x1 - x0))
    return lo


def conduit_parts(spec, z_foot, z_top):
    """
    The comm / signal conduit (design 3.1 + M9): a 4-segment uncapped square raceway r 0.025
    on the mast's +Y side with a 0.05 standoff (the stiff_mastjoint rings reach mast r + 0.04:
    1 cm clear), from `z_foot` up into the junction box `jbox_0` (0.22 wide x 0.28 tall, deep
    enough to swallow the conduit + 1 cm, back face dropped and buried 5 mm in the mast) whose
    top is `z_top`. 8 + 10 tris. Authoring frame.
    """
    c = DP1_CONDUIT
    r, so = c["r"], c["standoff"]
    rho = spec.mast_r + so + r
    jw, _jd, jh = c["jbox"]
    y_back = _mast_surface_y(spec.mast_r, 16, jw / 2.0) - 0.005
    jd = max(c["jbox"][1], rho + r + 0.01 - y_back)
    z_jb = z_top - jh
    h = (z_jb + 0.01) - z_foot
    cond = K.add_cyl("conduit_0", r, h, (0.0, rho, z_foot + h / 2.0), (0.0, 0.0, math.radians(45.0)),
                     segments=4, caps="none")
    x0, x1, y0, y1 = -jw / 2.0, jw / 2.0, y_back, y_back + jd
    zb, zt = z_jb, z_top
    quads = [[(x0, y1, zb), (x1, y1, zb), (x1, y1, zt), (x0, y1, zt)],          # front (+Y)
             [(x1, y0, zb), (x1, y1, zb), (x1, y1, zt), (x1, y0, zt)],          # +X side
             [(x0, y0, zb), (x0, y1, zb), (x0, y1, zt), (x0, y0, zt)],          # -X side
             [(x0, y0, zt), (x1, y0, zt), (x1, y1, zt), (x0, y1, zt)],          # top
             [(x0, y0, zb), (x1, y0, zb), (x1, y1, zb), (x0, y1, zb)]]          # bottom
    jbox = _quad_part("jbox_0", _outward(quads, ((x0 + x1) / 2.0, (y0 + y1) / 2.0, (zb + zt) / 2.0)))
    return [(cond, "galv"), (jbox, "galv")], {"rho": rho, "y_back": y_back, "depth": jd,
                                               "z_foot": z_foot, "z_box": (z_jb, z_top)}


# Family round (2026-11-03, owner: "does the middle cable attach to nothing? It's just floating"): the LV comm wire's
# anchor (0, comm_y; line piece and tower stub, unchanged) gets a hanger on the skinned heads, as real lines hang the
# signal cable from a bracket with a clamp. FG: an inverted U strap over the wire on the crossarm top (the wire center
# sits comm_y - arm_top = 0.04 above it); DQ / GD: a square bar down from the gantry beam's underside to a square sleeve
# round the wire (the wire runs through the portal window, A + 1.20, beam underside A + 1.692 / + 1.742). The clamp's
# inside half-size clears the vanilla LV hex (r 0.060, flats 0.052). FG 6 tris, DQ / GD 16; LOD1 drops them.
COMM_CLAMP = dict(len=0.12, half=0.07, bar=0.04, bury=0.03)


def comm_hanger_parts(spec, steel_z):
    """The comm wire's hanger and clamp at its anchor (0, 0, spec.comm_y), authoring frame (x travel). FG: `steel_z` is
    the crossarm top the U strap stands on; DQ / GD: the gantry beam underside the bar hangs from (buried
    COMM_CLAMP["bury"] in it). Galvanized, `comm_` parts."""
    c = COMM_CLAMP
    cz, h, ln = spec.comm_y, c["half"], c["len"]
    if spec.family == "FG":
        top = cz + h
        return [(K.add_box("comm_clamp", (ln, 2.0 * h, top - steel_z), (0.0, 0.0, (steel_z + top) / 2.0),
                           drop=("-Z", "+X", "-X")), "galv")]
    z0, z1 = cz + h - 0.005, steel_z + c["bury"]
    return [(K.add_box("comm_clamp", (ln, 2.0 * h, 2.0 * h), (0.0, 0.0, cz), drop=("+X", "-X")), "galv"),
            (K.add_box("comm_hanger", (c["bar"], c["bar"], z1 - z0), (0.0, 0.0, (z0 + z1) / 2.0), drop=("+Z", "-Z")),
             "galv")]


def _outward(quads, centre):
    """Re-wind each quad so its normal points away from `centre` (closed-ish convex boxes)."""
    c = mathutils.Vector(centre)
    out = []
    for q in quads:
        P = [mathutils.Vector(p) for p in q]
        n = (P[1] - P[0]).cross(P[2] - P[0])
        m = sum(P, mathutils.Vector()) / len(P)
        out.append(list(q) if n.dot(m - c) >= 0.0 else list(reversed(q)))
    return out


def arrest_part(lx, rung_half, z0, z1):
    """
    Fall-arrest rail (design 3.1): a 3-face channel 0.040 wide x 0.020 deep centered on the
    ladder (y 0), its open back 2 mm INTO the rung fronts (the rung's outer face at
    lx - rung_half, the climber's side = -X), from z0 to z1. 6 tris. Authoring frame.
    """
    a = DP1_ARREST
    xb = lx - rung_half + a["bury"]
    xf = xb - a["d"]
    hw = a["w"] / 2.0
    quads = [[(xf, hw, z0), (xf, -hw, z0), (xf, -hw, z1), (xf, hw, z1)],       # front (-X)
             [(xb, hw, z0), (xf, hw, z0), (xf, hw, z1), (xb, hw, z1)],         # +Y side
             [(xf, -hw, z0), (xb, -hw, z0), (xb, -hw, z1), (xf, -hw, z1)]]     # -Y side
    return [(_quad_part("arrest_0", _outward(quads, (xb + 0.05, 0.0, (z0 + z1) / 2.0))), "galv")]


def gusset_apex_parts(spec, z_beam_bottom):
    """
    Apex gusset plates of the rebuild head's A-braces (design 3.1; M4: they do NOT fix the
    brace a|b z-fight - DP1_INSET brace_a_w does): a 12 mm triangular plate in each brace plane
    (|y| 1.622) under the beam, top edge 0.60 along the beam bottom buried 3 mm into it, apex
    0.40 down between the braces. 8 tris each.
    """
    g = DP1_GUSSET_APEX
    zt = z_beam_bottom + g["bury"]
    zb = z_beam_bottom - g["drop"]
    out = []
    for tag, s in (("n", -1.0), ("p", 1.0)):
        y0, y1 = s * 1.622 - g["t"] / 2.0, s * 1.622 + g["t"] / 2.0
        A_, B_, C_ = (-g["along"], zt), (g["along"], zt), (0.0, zb)
        quads = [[(A_[0], y0, A_[1]), (C_[0], y0, C_[1]), (B_[0], y0, B_[1])],           # -Y tri
                 [(A_[0], y1, A_[1]), (B_[0], y1, B_[1]), (C_[0], y1, C_[1])],           # +Y tri
                 [(A_[0], y0, A_[1]), (B_[0], y0, B_[1]), (B_[0], y1, B_[1]), (A_[0], y1, A_[1])],
                 [(B_[0], y0, B_[1]), (C_[0], y0, C_[1]), (C_[0], y1, C_[1]), (B_[0], y1, B_[1])],
                 [(C_[0], y0, C_[1]), (A_[0], y0, A_[1]), (A_[0], y1, A_[1]), (C_[0], y1, C_[1])]]
        cen = (0.0, s * 1.622, (zt * 2 + zb) / 3.0)
        out.append((_quad_part(f"gusset_apex_{tag}", _outward(quads, cen)), "paint"))
    return out


def _drop_faces_where(obj, pred):
    """Delete the faces of a part (world = mesh coords) for which pred(normal, center) holds.
    Used BEFORE the join for hidden contact faces the kit has no flag for (design 3.1)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    dead = [f for f in bm.faces if pred(f.normal, f.calc_center_median())]
    if dead:
        bmesh.ops.delete(bm, geom=dead, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return len(dead)


def _seam_dir(axis):
    """UV seam direction for an axis: -X (the ladder side) on vertical parts, -Z (the
    underside) on horizontal ones."""
    a = _unit(axis)
    s = mathutils.Vector((-1.0, 0.0, 0.0)) if abs(a.z) > 0.9 else mathutils.Vector((0.0, 0.0, -1.0))
    s = s - a * s.dot(a)
    return s.normalized()


def uv_revolve(obj, axis, centre, kind="revolve", root=None, cut=None):
    """
    M11 metric UVs on a part's OWN axis (surfaces of revolution): cap faces (|n.a| > 0.9) get
    a planar projection (exactly isotropic: flanges, rings, discs); side faces u = rho theta
    (rho per vertex, theta from the seam: -X on vertical parts, the underside on horizontal),
    v = the meridian coordinate (rho m_rho + z m_z: cylinders v = z, cones the slant); faces
    neither cap nor radial (end caps of an arc) planar on their own. Inward faces flip u, so
    every island reads unmirrored (u x v = +normal). kind "liner": v = the signed slant from
    the groove root (radius `root`), so both V flanks form ONE strip. `cut` (M1): side faces
    above this axial coordinate move to their own island.
    """
    a = _unit(axis)
    c = mathutils.Vector(centre)
    e1 = _seam_dir(a)
    e2 = a.cross(e1)
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    layer = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    bm.normal_update()
    two_pi = 2.0 * math.pi
    for f in bm.faces:
        n = f.normal
        fc = f.calc_center_median()
        na = n.dot(a)
        if abs(na) > 0.9 and kind != "liner":
            b2 = n.cross(e1).normalized()
            for lp in f.loops:
                d = lp.vert.co - c
                lp[layer].uv = (d.dot(e1), d.dot(b2))
            continue
        rf = (fc - c) - a * (fc - c).dot(a)
        if kind != "liner" and (rf.length < 1e-9 or abs(n.dot(rf.normalized())) < 0.3):
            up = a - n * na
            if up.length < 1e-6:
                up = e1 - n * n.dot(e1)
            up = up.normalized()
            right = up.cross(n)
            for lp in f.loops:
                d = lp.vert.co - c
                lp[layer].uv = (d.dot(right), d.dot(up))
            continue
        sgn = 1.0 if n.dot(rf) >= 0.0 else -1.0
        rec = []
        for lp in f.loops:
            d = lp.vert.co - c
            za = d.dot(a)
            dp = d - a * za
            th = math.atan2(dp.dot(e2), dp.dot(e1)) % two_pi
            rec.append([lp, th, dp.length, za])
        ths = [t for _l, t, _r, _z in rec]
        if max(ths) - min(ths) > math.pi:
            for r_ in rec:
                if r_[1] < math.pi:
                    r_[1] += two_pi
        if kind == "liner":
            vv = [math.copysign(math.hypot(rho - root, za), za) for _l, _t, rho, za in rec]
        else:
            pts = [(rho, za) for _l, _t, rho, za in rec]
            best = (0.0, (0.0, 1.0))
            for i in range(len(pts)):
                for j in range(i + 1, len(pts)):
                    dr, dz = pts[j][0] - pts[i][0], pts[j][1] - pts[i][1]
                    L = math.hypot(dr, dz)
                    if L > best[0] + 1e-9:
                        best = (L, (dr / L, dz / L))
            mr, mz = best[1]
            if mz < -1e-9 or (abs(mz) <= 1e-9 and mr < 0.0):
                mr, mz = -mr, -mz
            vv = [rho * mr + za * mz for _l, _t, rho, za in rec]
        off = 1000.0 if (cut is not None and fc.dot(a) - c.dot(a) > cut) else 0.0
        for (lp, th, rho, _za), v in zip(rec, vv):
            lp[layer].uv = (sgn * rho * th + off, v)
    bm.to_mesh(me)
    bm.free()
    me.update()


def uv_prism(obj, axis, centre):
    """M11 for chamfered boxes: the side faces as ONE strip (u = the cross-section perimeter
    from the seam, v = along `axis`), caps planar, unmirrored."""
    a = _unit(axis)
    c = mathutils.Vector(centre)
    e1 = _seam_dir(a)
    e2 = a.cross(e1)
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    layer = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    bm.normal_update()
    sides = [f for f in bm.faces if abs(f.normal.dot(a)) < 0.1]
    pts = {}
    for f in sides:
        for lp in f.loops:
            d = lp.vert.co - c
            s, t = d.dot(e1), d.dot(e2)
            pts[(round(s, 6), round(t, 6))] = (s, t)
    order = sorted(pts, key=lambda k: math.atan2(pts[k][1], pts[k][0]) % (2.0 * math.pi))
    u_of, acc = {}, 0.0
    for i, k in enumerate(order):
        if i:
            p0, p1 = pts[order[i - 1]], pts[k]
            acc += math.hypot(p1[0] - p0[0], p1[1] - p0[1])
        u_of[k] = (i, acc)
    p0, p1 = pts[order[-1]], pts[order[0]]
    perim = acc + math.hypot(p1[0] - p0[0], p1[1] - p0[1])
    m = len(order)
    for f in bm.faces:
        n = f.normal
        if f in sides:
            ks = []
            for lp in f.loops:
                d = lp.vert.co - c
                ks.append((lp, u_of[(round(d.dot(e1), 6), round(d.dot(e2), 6))], d.dot(a)))
            idx = [k[1][0] for k in ks]
            wrap = max(idx) - min(idx) > m / 2
            for lp, (i, u), v in ks:
                lp[layer].uv = (u + (perim if wrap and i < m / 2 else 0.0), v)
        else:
            b2 = n.cross(e1).normalized() if abs(n.dot(e1)) < 0.9 else n.cross(e2).normalized()
            b1 = b2.cross(n)
            for lp in f.loops:
                d = lp.vert.co - c
                lp[layer].uv = (d.dot(b1), d.dot(b2))
    bm.to_mesh(me)
    bm.free()
    me.update()


def _part_bbox_centre(obj):
    co = _coords(obj)
    return tuple(float(v) for v in (co.min(axis=0) + co.max(axis=0)) / 2.0)


def uv_rule(name, spec, obj=None):
    """(kind, axis, center-on-origin) of the M11 metric UV a part takes, or None (kit UVs kept:
    boxes are box-projected in their local frame, isotropic; crashpad_outer is metric already).
    The small detail parts take ONE strip island each (plate rims, arrest channel, junction
    box sides): every island costs its 6 px pack margin all round (measured: per-face rims at
    the plate weight cost GD16 ~4 % of its base px/m)."""
    Z, Y, X = (0.0, 0.0, 1.0), (0.0, 1.0, 0.0), (1.0, 0.0, 0.0)
    if name == "crashpad_outer":
        return None
    if obj is not None and "dp_axis" in obj:
        return ("prism", tuple(obj["dp_axis"]), False)
    if name.startswith(("arrest_", "jbox_")):
        return ("prism", Z, False)
    if name.startswith(("mast", "flange_", "stiff_", "crashpad", "tipdeflector")):
        return ("revolve", Z, True)
    if name.startswith(("ladder_rail_", "insulator_", "post_", "conduit_", "bolt_mastbase_")):
        return ("revolve", Z, False)
    if name.startswith(("ladder_rung_", "pivot_", "axlecap_")):
        return ("revolve", Y, False)
    if obj is not None and (obj.get("cs2_wheel") or obj.get("cs2_wheel_hub")):
        return None         # T62 round 3: the v2 wheel's own isometric (developed) UVs - uv_revolve shears its groove fan
    if name.startswith("liner_"):
        return ("liner", Y, False)
    if spec.family == "FG" and name.startswith("rocker_sub_"):
        return ("prism", X, False)
    if spec.family == "FG" and name.startswith("sheave_arm_"):
        return ("prism", Z, False)
    return None


def _mast_cut_z(spec, side_len, joints):
    """M1: the axial coordinate where the mast island is split (its seam hidden under the
    stiff_mastjoint ring nearest mid-height) when the metric island would overflow the atlas,
    else None."""
    if side_len * DP1_MAST_PPM_EST[spec.family] <= DP1_ISLAND_MAX_PX or not joints:
        return None
    return min(joints, key=lambda z: abs(z - side_len / 2.0))


def detail_post(parts, spec, info):
    """
    Detail-pass steps on the transformed parts, BEFORE the join (build_tower_v2): hidden
    contact faces the kit has no flag for (gusset_arm tops on the crossarm bottom, landing-lip
    faces on the deck lips), the FG / ET chamfers, and the M11 metric UVs.
    """
    rep = {"dropped": {}, "chamfered": [], "uv": {}}
    for o, _k in parts:
        nm = o.name
        if nm.startswith("gusset_arm_"):
            zt = info["arm_top"] - FG_ARM_T
            n = _drop_faces_where(o, lambda nn, cc: nn.z > 0.9 and abs(cc.z - zt) < 1e-4)
            rep["dropped"][nm] = n
        elif nm.startswith(("grate_landn_lip", "grate_landp_lip")):
            n = _drop_faces_where(o, lambda nn, cc: abs(nn.y) > 0.9 and nn.y * cc.y > 0.0
                                  and abs(abs(cc.y) - DQ_DECK_Y0) < 1e-4)
            rep["dropped"][nm] = n
        if spec.family == "FG" and nm.startswith("rocker_sub_"):
            chamfer_long_edges(o, (1.0, 0.0, 0.0))
            rep["chamfered"].append(nm)
        elif spec.family == "FG" and nm.startswith("sheave_arm_"):
            chamfer_long_edges(o, (0.0, 0.0, 1.0))
            rep["chamfered"].append(nm)
    for o, _k in parts:
        rule = uv_rule(o.name, spec, o)
        if rule is None:
            continue
        kind, axis, on_origin = rule
        cen = _part_bbox_centre(o)
        if on_origin:
            cen = (0.0, 0.0, cen[2])
        if kind == "prism":
            uv_prism(o, axis, cen)
        elif kind == "liner":
            uv_revolve(o, axis, cen, "liner", root=float(o["dp_root_r"]))
        else:
            cut = info.get("mast_cut") if o.name == "mast" else None
            uv_revolve(o, axis, cen, cut=None if cut is None else cut - cen[2])
        rep["uv"][kind] = rep["uv"].get(kind, 0) + 1
    return rep


# --------------------------------------------------------------------------------------
# Per-family head-kit numbers (Revision 1b): DQ returns its constants untouched (every DQ /
# ETDQ build stays byte-identical); GD stretches the kit to the 6.0 m gauge
# --------------------------------------------------------------------------------------

def head_kit(spec):
    """Gantry-kit offsets: dy (beam, posts, end plates), cdy (carriers, hangers, girder ends),
    rise (gantry), mdr (ladder / landing shift for the wider mast) - all 0.0 on DQ - and pin_s,
    the sub-rocker pivot pin length (DQ 0.17; GD 0.14 so the pin end stays at lane - 0.064,
    inside the gondola head rule dy <= -0.06)."""
    if spec.family == "GD":
        return {"dy": GD_DY, "cdy": GD_CAR_DY, "rise": GD_RISE, "mdr": 0.05, "pin_s": S.GD_PIN_S}
    return {"dy": 0.0, "cdy": 0.0, "rise": 0.0, "mdr": 0.0, "pin_s": S.DQ_PIN_S}


def deck_y(spec):
    """Deck strip |Y| (inner, outer): DQ 0.695 / 2.15 = lane - 0.45; GD 0.695 / lane - 0.81 = 2.19."""
    if spec.family == "GD":
        return DQ_DECK_Y0, round(GD_LANE - GD_HANGER_COL[0]["half"], 6)
    return DQ_DECK_Y0, DQ_DECK_Y1


def rail_y(spec):
    """Deck rail line |Y|: DQ 2.05; GD 2.09 (deck outer edge - 0.10, the DQ relation)."""
    if spec.family == "GD":
        return round(deck_y(spec)[1] - (DQ_DECK_Y1 - DQ_RAIL_Y), 6)
    return DQ_RAIL_Y


def girder_box(spec):
    if spec.family == "GD":
        return dict(DQ_GIRDER, y1=round(DQ_GIRDER["y1"] + GD_CAR_DY, 6))
    return DQ_GIRDER


def hanger_box(spec):
    if spec.family == "GD":
        if SKINNED:
            # lift alignment phase 2 round 2: as DQ below, the hanger stops SKIN_DQ["gap"] short of the yoke (2.794)
            return dict(DQ_HANGER, y0=round(DQ_HANGER["y0"] + GD_CAR_DY, 6),
                        y1=round(DQ_MAIN_Y + GD_CAR_DY - 0.072 / 2.0 - SKIN_DQ["gap"], 6))
        return dict(DQ_HANGER, y0=round(DQ_HANGER["y0"] + GD_CAR_DY, 6),
                    y1=round(DQ_HANGER["y1"] + GD_CAR_DY, 6))
    if SKINNED:
        # lift alignment Phase 2: the hanger stops SKIN_DQ["gap"] short of the yoke's inboard face (it ran 6 mm into
        # the yoke, which now pitches about the main pin): yoke t 0.072 (cs2_sheaves.dq_train), 2.404 - 0.010 = 2.394
        return dict(DQ_HANGER, y1=round(DQ_MAIN_Y - 0.072 / 2.0 - SKIN_DQ["gap"], 6))
    return DQ_HANGER


def hanger_columns(spec):
    """C18 column boxes relative to the rope: [{half, z0, z1}] (DQ = DQ_HANGER_COL)."""
    if spec.family == "GD":
        return [dict(c) for c in GD_HANGER_COL]
    return [{"half": DQ_HANGER_COL["half"], "z0": -DQ_HANGER_COL["depth"], "z1": DQ_HANGER_COL["rise"]}]


def flange_r(spec):
    return {"FG": FG_FLANGE_R, "DQ": DQ_FLANGE_R, "GD": GD_FLANGE_R}[spec.family]


def pad_radius(spec):
    """Crash-pad shell outer radius: cs2_asset.CRASHPAD (0.56) on FG / DQ, GD_PAD_R on GD."""
    return GD_PAD_R if spec.family == "GD" else cs2_asset.CRASHPAD["radius"]


def sheave_segs(spec):
    """T62: v2 = cs2_sheaves.WHEEL_SEGS (16, every family: round 2); v1 = GD 12 flat / 12."""
    if S.wheels_v2():
        return S.wheel_spec(spec.family)["segs"]
    return GD_SHEAVE["segs"] if spec.family == "GD" else 12


def sheave_groove(spec):
    """None = lite_sheave's default V-groove (FG / DQ; every family under T62 v2); GD 0.0 = the v1 flat rim."""
    if S.wheels_v2():
        return None
    return GD_SHEAVE["groove"] if spec.family == "GD" else None


def sheave_ngon(spec):
    """T62 v2: n-gon side discs (round 2: every family, under the hub bosses); v1: never."""
    return bool(S.wheels_v2() and S.wheel_spec(spec.family)["ngon"])


# -- T27 ROPE ON THE CROWN (2026-10-17, docs/research/field/2026-10-15/build/T27-rope-path.md) -------------------------
# Playtest items 25 / 27: "the cable passes straight through pulleys/rollers". The shipped contract (D6) puts
# the LANE on the sheave AXLE, so the vanilla rope drawn on the lane runs through the wheel centers. ROPE_ON_CROWN True =
# the lane rides the train: a SUPPORT head (rope above the sheaves: every line tower, ET62 / ET68) lane = axle + seat, a
# COMPRESSION head (ETDQ / ETGD, rope under) lane = axle - seat, seat = rope_seat() (the lane's vanilla cable hexagon
# resting in the groove: LANE_ROPE). The tower MESH does not change with the flag: only the lane numbers the manifest
# hands to the surgery (piece m_Lanes y, station HV sub-lane anchors, chair / cabin NetSubObjects y) and the rope
# height the carrier contracts (C18 hanger column, C20 gondola carrier) sweep at. False (default) = every shipped
# tower build, byte for byte.
ROPE_ON_CROWN = False
VANILLA_HV_ROPE = dict(r=0.100, h=0.0866)   # High-voltage Cable Lane Mesh m_Bounds x +-0.100, y +-0.0866 (hexagon, flats
VANILLA_LV_ROPE = dict(r=0.060, h=0.0520)
# the cable the FG / DQ / GD rope lanes draw (lift alignment round 4, decision 2026-11-02: the vanilla LOW-voltage
# cable mesh bbb9724d instead of the HV one; no new rope mesh): crown_seat seats it, G6 sweeps and seats it, the
# game-rope previews draw it (rounds 1-3 and T27 seated the HV hexagon: FG 0.2833 / DQ 0.3096 / GD 0.3366)
LANE_ROPE = VANILLA_LV_ROPE
# the FG / ET (Yan) carrier tree hangs IN the lane plane (cs2_sheaves.fg_train_side): sub rocker axle + 0.20..0.32,
# sheave arms from the axle up to it, so a rope on the crown (axle + 0.19..0.37) would run through them; under T27 the
# tree moves inboard by the rope half-width 0.10 + the main rocker half-width 0.077 + 0.023 clear = 0.20 and each sheave
# hangs on an axle stub (the hanger stays under the 3.6 m crossarm: lane 1.8 - 0.20 = 1.60)
FG_CROWN_INBOARD = 0.20   # top / bottom); Low-voltage Cable Lane Mesh x +-0.060, y +-0.0520 (%TEMP%\pltar dump)


def rope_seat(sheave_r, sheave_w, groove, rope=VANILLA_HV_ROPE, chamfer=0.0):
    """Rope AXIS above the sheave axle when the vanilla cable (a hexagon, circumradius R, flats top / bottom at +-h,
    bottom flat +-R/2 wide) rests on a lite_sheave of radius r, width w and V-groove depth `groove` (0 = flat rim; the
    groove profile radius at lateral y is r - groove (1 - |y| / (w/2))). The larger of: the bottom flat's corners on the
    groove flanks (|y| = min(R/2, w/2)) and the slanted faces on the flange tips (|y| = w/2 when R/2 < w/2 < R).
    T62 round 2: `chamfer` (cs2_sheaves.wheel_profile c, the v2 wheel's rim chamfer) puts the crests (the flange tips)
    at |y| = w/2 - chamfer: the formula runs on that half width (0 = every v1 number unchanged)."""
    R, h, hw = rope["r"], rope["h"], sheave_w / 2.0 - chamfer
    yb = min(R / 2.0, hw)
    prof = sheave_r - groove * (1.0 - yb / hw) if groove > 0.0 else sheave_r
    seat = prof + h
    if R / 2.0 < hw < R:
        seat = max(seat, sheave_r + h * (R - hw) / (R / 2.0))
    elif hw <= R / 2.0:
        seat = max(seat, sheave_r + h)
    return seat


def spec_groove(spec):
    """The groove depth lite_sheave actually builds for this head (None -> max(0.02, 0.18 r))."""
    g = sheave_groove(spec)
    return max(0.02, 0.18 * spec.sheave_r) if g is None else g


def wheel_chamfer(r, w):
    """T62 round 2: the v2 wheel's rim chamfer (crests at w/2 - c) for rope_seat; 0 under v1."""
    return S.wheel_profile(r, w)["c"] if S.wheels_v2() else 0.0


def crown_seat(spec, rope=None):
    """rope_seat of `rope` (default LANE_ROPE, the LV cable) for this tower's head, 0.1 mm-rounded. The HV hexagon
    (rounds 1-3, T27): v1 FG 0.278, DQ 0.310, GD 0.337; T62 v2 chamfered crests FG 0.2833, DQ / GD unchanged."""
    return round(rope_seat(spec.sheave_r, spec.sheave_w, spec_groove(spec), rope=LANE_ROPE if rope is None else rope,
                           chamfer=wheel_chamfer(spec.sheave_r, spec.sheave_w)), 4)


def lane_y(spec, on=None):
    """The lane / rope height this tower carries: the axle (shipped, ROPE_ON_CROWN False) or the crown (axle +- seat,
    by the head's kind). `on` overrides the module flag (tests, renders)."""
    on = ROPE_ON_CROWN if on is None else on
    if not on:
        return spec.axle
    s = crown_seat(spec)
    return round(spec.axle - s if spec.kind == "compression" else spec.axle + s, 4)


import dataclasses as _dataclasses  # noqa: E402
VARIANTS_SHIPPED = None     # filled by set_rope_on_crown (the VARIANTS table as shipped)


def set_rope_on_crown(on):
    """Set ROPE_ON_CROWN and swap the COMPRESSION terminal heads (ETDQ, ETGD): their rope is the station's loading rope
    (DQ_ENTRY_ROPE_Y 3.70 / GD_ENTRY_ROPE_Y 4.85, frozen by the deck and the chair hang), so under T27 the TRAIN moves
    up by crown_seat (axle = rope + seat, tube top / bounds top with it) and lane_y = the rope again. Every support head
    keeps its mesh (the lane rises instead). Idempotent; off restores the shipped table."""
    global ROPE_ON_CROWN, VARIANTS_SHIPPED
    if VARIANTS_SHIPPED is None:
        VARIANTS_SHIPPED = dict(VARIANTS)
    ROPE_ON_CROWN = bool(on)
    for k, s in VARIANTS_SHIPPED.items():
        if on and s.role == "terminal" and s.kind == "compression":
            d = crown_seat(s)
            VARIANTS[k] = _dataclasses.replace(s, axle=round(s.axle + d, 6), tube_top=round(s.tube_top + d, 6),
                                              top=round(s.top + d, 6))
        else:
            VARIANTS[k] = s


def rope_on_crown_block(spec):
    """Manifest block (ROPE_ON_CROWN only): the numbers the surgery needs for this tower."""
    return {"rule": "lane on the sheave crown (T27): support lane = axle + seat, compression lane = axle - seat",
            "kind": spec.kind, "axle": spec.axle, "sheave_r": spec.sheave_r, "sheave_w": spec.sheave_w,
            "groove": round(spec_groove(spec), 4), "seat": crown_seat(spec), "lane_y_old": spec.axle,
            "lane_y_new": lane_y(spec, True), "delta": round(lane_y(spec, True) - spec.axle, 4),
            "rope": f"vanilla LV cable mesh hexagon r {LANE_ROPE['r']} / h {LANE_ROPE['h']} (round 4; was HV r 0.100 / "
                    "h 0.0866)",
            "mesh_changes": False}


# -- LIFT ALIGNMENT PHASE 2: SKINNED TOWERS, HINGED HALF TRAINS (round 2, 2026-11-02; cs2_skin; the articulation
# design note option C, docs/research/field/2026-11-02/liftalign/articulation-design-note.md) ------------------------
# CS2 draws each span's rope straight to the tower's anchor, so over a train the rope CORNERS at the anchor. Each lane's
# train is two HALF trains hinged on the main pin, which lies on the lateral line through both rope points (net frame
# (x, 0, crown lane y)). Bones (cs2_skin module docstring, the contract): `Root` (static), `TrainFront` = the halves on
# the game +z side (authoring +X = net -Y: cs2_skin.FRONT_SIGN_NET), `TrainBack` = the -z side, both heads on that
# line at net (0, 0, crown lane y), up, roll 0, children of Root. Our code pitches TrainFront by the rope leaving toward
# +z and TrainBack by the rope toward -z (+ lowers the game +z end), each clamped to SKIN_THETA_MAX, the half-break
# |front - back| / 2 to cs2_skin.HALF_BREAK_MAX (20 deg). Every vertex weighs 1.0 on its part's bone on EVERY LOD (the
# importer copies LOD0's ProceduralAnimationProperties onto each LOD: 1.6.0f1 AssetImportPipeline.SetupLODs
# :2196-2218). SKINNED implies the rope on the crowns (T27, set_rope_on_crown): the pivot line is lane_y(spec, True).
# Head recipes (SKIN_HEADS):
#   FG Yan     cs2_sheaves.fg_train_side_halves: a half = one 2-wheel bogie on its main-rocker half; the two halves
#              lap on the main pin `pivot_main_<side>`, held by the hanger one lateral slab inboard. SKIN_FG beam
#              (round 4, the default): the shipped three tiers - a half main beam at the shipped height (mast - 0.40,
#              0.28 over the pin) on a yoke strap `yoke_<side>_f|b` rising from the main pin, the bogie pivots floating
#              between beam and bar as shipped, the bars at rope height on sheave arms, all inboard of the wheels.
#              bogie_low (round 3): the bogie bar sits at AXLE height between its wheel pair (wheels on axle stubs from
#              the bar, no sheave arms) and the main-rocker half is a drop_plate from its lug on the main pin down to
#              the bogie pivot; both off = round 2 (lug_plate at the pin, bars at rope height on sheave arms).
#   DQ ingest  cs2_sheaves.dq_train_halves: a half = one 4-wheel bogie (two 2-wheel bogies on a main rocker) on its
#              yoke half `yoke_<side>_f|b`; the Kagelok girder end and the hanger cut SKIN_DQ["gap"] short of the
#              yoke, a main pin through all three, the yoke's band SKIN_YOKE (its top under the
#              gantry's slanted posts).
#   GD rebuild dq_train_halves at SKIN_GD["sheaves"] a side (skin_spec; the shipped 10 a side has a 2-wheel bogie on
#              the pin: with --gd-sheaves 10 each half pairs its 5 wheels from the outer end, a 4-wheel bogie + the
#              inner wheel on a 1-wheel sub hung on a link); the gantry posts land on the girder ends (they came
#              down onto the yoke, and with the rope on the crowns through the rope: the T27 C20 head-rule item), the
#              hanger cut short of the yoke,
#              the main pin from inside the girder end (SKIN_GD["pin_in"]).
#   DQ / GD clevis (round 3, SKIN_CLEVIS via skin_clevis): the yoke halves thin to plate_t at the yoke slab's outer
#              faces and a static tongue `hanger_<side>_t` (Root) fills the middle from the head down to a round lug
#              on the main pin between the inner wheels: both halves' plates straddle it (a clevis on the pin).
#              Round 4: the plates are only the cheeks on the pin; from beam_x out each half is the shipped full yoke
#              section (0.072 thick), a second shell of the yoke part.
# Each half's bogies move SKIN_HALF_SHIFT out along the train, so the two inner wheels clear at a 20 deg half-break
# phi: 2 (x cos phi - d sin phi) - 2 r >= SKIN_CLEAR_MIN (x the inner wheel center, d the crown seat, r the wheel).
# G6 (skin_sweep -> cs2_skin.sweep): the joint grid cs2_skin.joint_grid(theta_max); per pose no BVH overlap half vs
# head and half vs half, clearance >= SKIN_CLEAR_MIN (the main pins vs the lugs they run through exempt), the pin joint
# opening <= SKIN_PIN_GAP_MAX, each half's rope (a straight run from its pivot at its pitch, both lanes) clear of the
# head and the non-wheel train parts and seated on every crown of its half (SKIN_SEAT_GAP).
SKINNED = False
SKIN_BONES = ("Root", "TrainFront", "TrainBack")
SKIN_TYPE_MAX_SLOPE = {"FG": 38.7, "DQ": 42.0, "GD": 50.2}     # deg, research G6 (the planner's max slope per type)
# deg, each half's clamp: min(type max slope, cs2_skin.TILT_CAP 30) = 30 for every family
SKIN_THETA_MAX = {f: cs2_skin.theta_max(a) for f, a in SKIN_TYPE_MAX_SLOPE.items()}
_SKIN_GANTRY = ("rocker_", "pivot_", "yoke_", "sheave_", "liner_")
SKIN_TRAIN = {"FG": ("rocker_main_", "rocker_sub_", "pivot_", "sheave_", "liner_", "axlecap_", "yoke_"),
              "DQ": _SKIN_GANTRY, "GD": _SKIN_GANTRY}
SKIN_ROOT_FIRST = ("pivot_main_",)      # the main pin belongs to its hanger: both halves turn on it
# the train parts the main pin runs through (FG: the round 2 / 3 lap plates, the round 4 yoke straps)
SKIN_LUG = {"FG": ("rocker_main_", "yoke_"), "DQ": "yoke_", "GD": "yoke_"}
SKIN_HEADS = {"FG": ("yan",), "DQ": ("ingest",), "GD": ("rebuild",)}
# m each half's bogies move out along the train: the inner-wheel clearance above at phi 20 deg is FG 11 mm (the design
# note's 0.06 leaves FG 8 mm inside), DQ 9 mm, GD 21 mm (GD 0.07 would leave 2 mm: under SKIN_CLEAR_MIN)
SKIN_HALF_SHIFT = {"FG": 0.07, "DQ": 0.06, "GD": 0.09}
SKIN_LAP_GAP = 0.010                    # m between the two halves' lap plates on the main pin (each its own slab)
# FG lap plates' bevel segments (FG_BEVEL gives the shipped box rocker 2: 108 tris; a 2-segment lug plate is 124, x 4 =
# +280 on the cap-tight FG; 1 segment = 70 a plate)
SKIN_PLATE_BEVEL = {"FG": 1}
SKIN_FG = dict(gap=0.015, poke=0.006, pin_r=0.05,           # cs2_sheaves.fg_train_side_halves
               bogie_low=False,     # round 3: the bogie bars at axle height between their wheels (no sheave arms)
               beam=True)           # round 4: the shipped three tiers (half beams on yoke straps, stubs, bars)
# DQ: pin from inside the girder end (|y| 2.30)
SKIN_DQ = dict(gap=0.010, poke=0.006, pin_r=0.04, pin_in=2.30)
# the yoke halves' band (bottom, top), axle-relative (cs2_sheaves.dq_train_halves yoke_bot / yoke_top). Shipped
# dq_train: 0.25..0.42 (0.17 deep). Rounds 2-3 had the top at pin + 0.05 (DQ A + 0.36) for the 42 deg tilt: the
# Kagelok gantry's slanted posts cross the yoke's slab (|y| 2.404..2.476) from A + 0.47 up. Round 4, at the 30 deg cap:
# GD keeps the shipped band; DQ keeps the shipped 0.17 depth 0.01 lower (G6 measured with the LV lane rope, pin at
# A + 0.2589, and the full-section yoke, SKIN_CLEVIS beam_x: a top of 0.42 meets the gantry_00 posts at a -30 pitch,
# 3.5 mm, 0.41 clears; the mains' pins, axle + 0.195..0.255, bridge 15 mm into the yoke, C21)
SKIN_YOKE = {"DQ": (0.24, 0.41), "GD": (0.25, 0.42)}
# GD: sheaves a side (owner decision: 10, as shipped; 8 = two 4-wheel bogies, a wheel short of the walkway at each end;
# 12 = 4,958 tris on GD12); the main pin
# from 0.09 inside the girder end (2.69); the posts 5 mm in from the girder end, buried 5 mm in its top
SKIN_GD = dict(sheaves=10, pin_in=2.60, post_bury=0.005, post_inset=0.005)
# round 3 clevis (cs2_sheaves.dq_train_halves clevis): the yoke plates 20 mm at the yoke slab's faces, a static 20 mm
# tongue 6 mm off each (G6 >= 5 mm) from the head down to a r 0.075 lug on the main pin (pin r 0.04) over the train
# center. Top: DQ ingest in the Kagelok gantry_00 slanted post (its underside A + 0.56..0.65 across the tongue slab,
# A + 1.15 its top side: probed), GD 0.03 into the gantry beam (bottom A + 1.692 + rise)
SKIN_CLEVIS = dict(plate_t=0.020, gap=0.006, r=0.075, dq_top=0.70, dq_top_hw=0.075, gd_bury=0.03, gd_top_hw=0.12,
                   # round 4: each half the shipped full yoke section (0.072) from beam_x out, a cheek on the pin
                   # inside it. The beam's inner top corner at a 30 deg pitch: 0.866 x - 0.5 (top - pin) >= the
                   # tongue's half width + 5 mm: DQ >= 0.168, GD >= 0.223 (the tongue's top half width)
                   dq_beam_x=0.20, gd_beam_x=0.25)
SKIN_CLEAR_MIN = 0.005                  # m, half vs head and half vs half at every pose
SKIN_PIN_GAP_MAX = 0.005                # m, main-pin joint opening over the sweep (G6)
SKIN_ROPE_RUN = 1.0                     # m of rope past a half's outer wheel in the rope check
SKIN_SEAT_GAP = cs2_skin.SEAT_GAP       # m, the rope's underside over each crown: contact, not inside
# round 3: the preview renders' rope stand-in at a real rope's diameter, 0.10 x the sheave's (FG 40 / DQ 45 / GD 50 mm),
# seated in the groove: the HV cable mesh (0.20 m, 44-50 % of the wheel) hid every bogie part in the side views
SKIN_PREVIEW_ROPE = 0.10


def set_skinned(on):
    """Set SKINNED (before any part is built); on also puts the rope on the crowns (set_rope_on_crown(True)), whose
    line the half bones pivot on. Off leaves the crown flag as it is."""
    global SKINNED
    SKINNED = bool(on)
    if SKINNED:
        set_rope_on_crown(True)


def skin_spec(spec):
    """The spec a skinned build uses: GD at SKIN_GD["sheaves"] a side (10 default as shipped, --gd-sheaves); else `spec`."""
    if SKINNED and spec.family == "GD" and spec.sheaves != SKIN_GD["sheaves"]:
        return _dataclasses.replace(spec, sheaves=SKIN_GD["sheaves"])
    return spec


def skin_pivot(spec):
    """Net-frame (y, z) of both half bones' pivot: the lateral line through both rope points (game (x, crown y, 0))."""
    return (0.0, lane_y(spec, True))


def skin_heads(spec):
    """{bone: net-frame head} in SKIN_BONES order: Root at the origin, both halves on the pivot line at x 0."""
    zp = lane_y(spec, True)
    return {"Root": (0.0, 0.0, 0.0), "TrainFront": (0.0, 0.0, zp), "TrainBack": (0.0, 0.0, zp)}


def skin_half_extent(spec):
    """m from the pivot to a half's outer wheel tip along travel (the bogies moved SKIN_HALF_SHIFT out)."""
    sh = SKIN_HALF_SHIFT[spec.family]
    if spec.family == "FG":
        _n, sub_span, main_span = S.fg_spans(spec.sheaves, spec.pitch, spec.sheave_r)
        return main_span / 2.0 + sh + sub_span / 2.0 + spec.sheave_r
    return (spec.sheaves - 1) * spec.pitch / 2.0 + sh + spec.sheave_r


def skin_part_bones(obj, table, spec):
    """{train part: TrainFront | TrainBack} on the joined NET-frame LOD0 by travel side (cs2_skin.bones_by_travel),
    held to the builders' names: `rocker_main_<side>_f` / `yoke_<side>_f` (built at authoring +X) ride TrainFront, the
    `_b` plates TrainBack. That is the axis record's build-time test (authoring +X -> net -Y -> game +z). Raises."""
    import re
    fam = spec.family
    pb = cs2_skin.bones_by_travel(
        obj, table, lambda n: n.startswith(SKIN_TRAIN[fam]) and not n.startswith(SKIN_ROOT_FIRST),
        "TrainFront", "TrainBack")
    named = {n: ("TrainFront" if n.endswith("_f") else "TrainBack") for n in pb
             if re.match(r"^(rocker_main|yoke)_[np]_[fb]$", n)}
    bad = {n: (pb[n], w) for n, w in named.items() if pb[n] != w}
    cnt = {b: sum(1 for v in pb.values() if v == b) for b in SKIN_BONES[1:]}
    want = 8 if fam == "FG" and SKIN_FG.get("beam") else 4      # FG round 4: the half beams + their yoke straps
    _check(not bad and len(named) == want and cnt["TrainFront"] == cnt["TrainBack"] > 0,
           f"[{spec.key}] skin: the halves by travel side (the _f lap plates on TrainFront = game +z, the _b on "
           f"TrainBack), equal part counts", f"({len(named)} plates, mismatched {bad}, parts {cnt})")
    return pb


def skin_assign(obj, table, part_bone):
    """Vertex groups Root / TrainFront / TrainBack on `obj` (LOD0 or a LOD copy: the 'part' face attribute survives
    the chain), every vertex weight 1.0 on its bone (cs2_asset.check_skin_weights holds). Returns {bone: vertices}."""
    try:
        return cs2_skin.assign(obj, table, part_bone, list(SKIN_BONES))
    except cs2_skin.SkinError as e:
        raise TowerContractError(str(e))


def build_skin_armature(spec, name):
    """The three-bone rig in the net frame (the joined tower's): cs2_skin.build_armature at skin_heads."""
    try:
        return cs2_skin.build_armature(name, skin_heads(spec))
    except cs2_skin.SkinError as e:
        raise TowerContractError(str(e))


def preview_rope(spec):
    """(radius, drop below the pivot line) of the PREVIEW rope stand-in: a real rope (diameter SKIN_PREVIEW_ROPE x the
    sheave's) with its axis at the groove bottom + its radius. Previews only (round 3; round 4 renders the game rope):
    the game draws the lane's vanilla cable mesh (LANE_ROPE: LV, r 0.060) on the lane, and G6 checks that one."""
    rt = SKIN_PREVIEW_ROPE * spec.sheave_r
    d = crown_seat(spec) - (spec.sheave_r - spec_groove(spec) + rt)      # the lane seat minus the real rope's axis
    return rt, round(-d if spec.kind == "compression" else d, 6)


def skin_rope_runs(spec, pose, run=None, start=0.0, preview=False):
    """[(verts, faces)]: the lane's vanilla cable (LANE_ROPE, the LV hexagon r 0.060) as the game draws it at a tower:
    per lane one straight run per half from the pivot outward, at that half's pitch (cs2_skin.rope_run). Net frame.
    preview=True: the round 3 real-diameter stand-in seated in the groove (preview_rope) instead."""
    zp = lane_y(spec, True)
    ext = skin_half_extent(spec) + (SKIN_ROPE_RUN if run is None else run)
    r, drop = preview_rope(spec) if preview else (LANE_ROPE["r"], 0.0)
    out = []
    for side in (-1.0, 1.0):
        for b, sign in (("TrainFront", cs2_skin.FRONT_SIGN_NET), ("TrainBack", -cs2_skin.FRONT_SIGN_NET)):
            out.append(cs2_skin.rope_run(side * spec.gauge / 2.0, (0.0, zp), pose.get(b, 0.0), sign, ext,
                                         r=r, start=start, drop=drop))
    return out


def skin_sweep(obj, table, spec, part_bone):
    """G6 on the joined net-frame LOD0 (cs2_skin.sweep over cs2_skin.joint_grid(theta_max)); raises TowerContractError
    on a failed gate after logging every pose. Returns the report (meters)."""
    fam = spec.family
    zp = lane_y(spec, True)
    tmax = SKIN_THETA_MAX[fam]
    bones = list(SKIN_BONES)
    try:
        names, fb, _vb = cs2_skin.face_bones(obj, table, part_bone, bones)
    except cs2_skin.SkinError as e:
        raise TowerContractError(str(e))
    co = cs2_skin.coords(obj)
    polys = [tuple(p.vertices) for p in obj.data.polygons]
    piv = skin_pivot(spec)
    pivots = {b: piv for b in bones[1:]}
    poses = [{"TrainFront": f, "TrainBack": b} for f, b in cs2_skin.joint_grid(tmax)]
    is_wheel = lambda n: n.startswith(("sheave_", "liner_"))       # noqa: E731
    fx = np.array([co[list(p), 0].mean() for p in polys])
    wf = {}
    for b in bones[1:]:
        for side in (-1.0, 1.0):
            wf[(b, side)] = [i for i, n in enumerate(names) if is_wheel(n) and bones[fb[i]] == b
                             and np.sign(fx[i]) == side]
    ext = skin_half_extent(spec) + 0.30
    R, h = LANE_ROPE["r"], LANE_ROPE["h"]

    def seats(pc, ang):
        out = {}
        for b, sign in (("TrainFront", cs2_skin.FRONT_SIGN_NET), ("TrainBack", -cs2_skin.FRONT_SIGN_NET)):
            for side in (-1.0, 1.0):
                out[f"{b}_{'n' if side < 0 else 'p'}"] = cs2_skin.seat_half(
                    pc, polys, wf[(b, side)], piv, ang.get(b, 0.0), side * spec.gauge / 2.0, sign, ext, R, h)
        return out
    lug = SKIN_LUG[fam]
    rep = cs2_skin.sweep(co, polys, names, fb, bones, pivots, poses,
                         pins=lambda n: n.startswith(SKIN_ROOT_FIRST),
                         lugs={b: (lambda n: n.startswith(lug)) for b in bones[1:]},
                         wheels=is_wheel, ropes=lambda ang: skin_rope_runs(spec, ang), seats=seats,
                         clear_min=SKIN_CLEAR_MIN, pin_gap_max=SKIN_PIN_GAP_MAX, label=spec.key)
    seat0 = seats(co, {})
    gaps = [g for runs in seat0.values() for _d, g in runs]
    print(f"[tower2] G6 [{spec.key}] {len(poses)} poses (theta_max {tmax}, half-break <= {cs2_skin.HALF_BREAK_MAX}); "
          f"min clearance {rep['min_clear']['m'] * 1000:.1f} mm at {rep['min_clear']['pose']} "
          f"({' / '.join(str(n) for n in rep['min_clear']['parts'] or [])}); rest seat {len(gaps)} crowns "
          f"{min(gaps) * 1000:.2f}..{max(gaps) * 1000:.2f} mm")
    try:
        cs2_skin.check_rows(rep["poses"], spec.key, SKIN_CLEAR_MIN, SKIN_PIN_GAP_MAX, seat_tops=2 * spec.sheaves,
                            seat_gap=SKIN_SEAT_GAP)
    except cs2_skin.SkinError as e:
        raise TowerContractError(str(e))
    print(f"[tower2] ok [{spec.key}] G6 joint sweep: {len(poses)} poses, no overlap, clearance >= "
          f"{SKIN_CLEAR_MIN * 1000:.0f} mm, pin gap <= {SKIN_PIN_GAP_MAX * 1000:.0f} mm, both halves' ropes clear and "
          f"seated on all {2 * spec.sheaves} crowns")
    rep.update({"pivot_net": [0.0, piv[0], piv[1]], "pivot_game": [0.0, zp, 0.0], "theta_max_deg": tmax,
                "half_break_max_deg": cs2_skin.HALF_BREAK_MAX, "half_shift_m": SKIN_HALF_SHIFT[fam],
                "rope_points_game": [[-spec.gauge / 2.0, zp, 0.0], [spec.gauge / 2.0, zp, 0.0]],
                "crown_seat": crown_seat(spec), "seat_rest_m": seat0, "sheaves": spec.sheaves,
                "train_parts": {b: sorted(n for n, v in part_bone.items() if v == b) for b in bones[1:]},
                "verts": int(len(co))})
    return rep


def skin_views(spec):
    """Round 2 previews: [(label, pose {bone: deg}, render_tower_views dicts)] in the net frame (X lateral, -Y = game
    forward = TrainFront). Level: the head from the front, the side, 3/4, above and below, and the whole tower 3/4; then
    the +X lane's train close up along the lateral axis (the pivot axis: pitches read true), 3/4, and the joint on the
    main pin (FG from inboard, DQ / GD from outboard), at the uniform slope limit (front = back = theta_max), a crest
    like the photo (front +15 / back -15: a 30 deg break), the 20 deg half-break limit (+20 / -20), the steep limit
    (front theta_max, back theta_max - 40) and a dip (-15 / +15). Distances fit the head / train to a 50 mm lens at
    1300 x 1000 (half-width 15.6 / 50)."""
    zp = lane_y(spec, True)
    k = 50.0 / 15.6
    head_w = spec.gauge / 2.0 + 0.7
    train = skin_half_extent(spec)
    tmax = SKIN_THETA_MAX[spec.family]
    slope = round(tmax, 1)
    hd = (0.0, 0.0, zp - 0.35)
    tc = (spec.gauge / 2.0, 0.0, zp - 0.10)
    jx = spec.gauge / 2.0 - (0.45 if spec.family == "FG" else 0.25)

    def side(tag, d=1.0):
        return dict(name=f"skin_train_side_{tag}", dir=(1.0, 0.0, 0.05), target=tc, lens=50.0, dist=(train + 0.45) * k * d)

    def q34(tag):
        return dict(name=f"skin_train_34_{tag}", dir=(1.0, -0.9, 0.30), target=tc, lens=50.0, dist=(train + 0.45) * k * 0.9)

    def joint(tag):
        if spec.family == "FG":
            return dict(name=f"skin_joint_{tag}", dir=(-1.0, -0.30, 0.22), lens=50.0, dist=1.9,
                        target=(jx, 0.0, zp - 0.10))
        # round 3: DQ / GD from outboard of the rope (inboard sits in the walkway truss), slightly above the crowns,
        # on the inner wheel pair and the clevis on the main pin between them
        return dict(name=f"skin_joint_{tag}", dir=(1.0, -0.35, 0.18), lens=50.0, dist=2.1,
                    target=(spec.gauge / 2.0 - 0.08, 0.0, zp - 0.12))
    return [
        ("level", {}, [dict(name="skin_head_front", dir=(0.0, -1.0, 0.12), target=hd, lens=50.0, dist=head_w * k),
                       dict(name="skin_head_side", dir=(1.0, 0.0, 0.10), target=hd, lens=50.0, dist=(train + 0.8) * k),
                       dict(name="skin_head_34", dir=(1.0, -1.1, 0.35), target=hd, lens=50.0, dist=head_w * k),
                       dict(name="skin_head_top", dir=(0.0, -0.25, 1.0), target=(0.0, 0.0, zp), lens=50.0,
                            dist=max(head_w, train + 0.5) * k),
                       dict(name="skin_head_under", dir=(0.25, -0.35, -1.0), target=(0.0, 0.0, zp - 0.2), lens=35.0,
                            dist=4.0, ground=False),
                       dict(name="skin_tower_34", dir=(1.0, -1.1, 0.30), target=(0.0, 0.0, spec.top / 2.0), lens=35.0,
                            dist=(spec.top / 2.0 + 1.0) * 35.0 / 12.0),
                       side("level"), joint("level")]),
        (f"slope{slope:g}", {"TrainFront": slope, "TrainBack": slope}, [side(f"slope{slope:g}"), q34(f"slope{slope:g}")]),
        # family round: the whole head on the crest, both lanes' halves seated (the owner's crest shot)
        ("crest30", {"TrainFront": 15.0, "TrainBack": -15.0},
         [side("crest30"), q34("crest30"), joint("crest30"),
          dict(name="skin_head_34_crest30", dir=(1.0, -1.1, 0.35), target=hd, lens=50.0,
               dist=max(head_w, train + 0.5) * k * 1.1)]),
        ("limit40", {"TrainFront": 20.0, "TrainBack": -20.0}, [side("limit40"), joint("limit40")]),
        ("steep", {"TrainFront": tmax, "TrainBack": round(tmax - 40.0, 3)}, [side("steep", 1.1)]),
        ("dip30", {"TrainFront": -15.0, "TrainBack": 15.0}, [side("dip30"), q34("dip30")]),
    ]


# The round 2 preview light (render_tower_views `light`): the pilot's noon from 55 deg over a snow-white ground read flat
# and pale; a lower key sun from the camera side over a gray ground keeps the bounce down and draws the grooves
SKIN_LIGHT = dict(elevation_deg=32.0, sun_energy=4.5, azimuth_deg=-40.0, ground_albedo=0.30,
                  look="AgX - Medium High Contrast")


def render_skin_views(t, arm, spec, asset_dir, name, out_dir, views=None):
    """skin_views through render_tower_views (the baked maps) in SKIN_LIGHT: per pose one posed copy (cs2_skin.posed_copy:
    the armature's result, held to the sweep's pose) with each half's rope as the game draws it (skin_rope_runs: the
    lane's LV cable on the crowns, dark steel; round 4 drops the round 3 thin stand-in) and the LV comm wire on its
    anchor (0, comm_y), each half's run at that half's pitch, as extras. Returns the PNG paths."""
    mat = bpy.data.materials.new(name + "_PreviewRope")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (0.03, 0.03, 0.03, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.7
    bsdf.inputs["Roughness"].default_value = 0.45
    piv = skin_pivot(spec)
    written = []
    for _label, pose, vv in (views or skin_views(spec)):
        try:
            objs = [cs2_skin.posed_copy(t, arm, pose, {b: piv for b in SKIN_BONES[1:]}, f"{name}Pose")]
        except cs2_skin.SkinError as e:
            raise TowerContractError(str(e))
        runs = skin_rope_runs(spec, pose, run=2.0)
        if spec.comm_y is not None:
            ext = skin_half_extent(spec) + 2.0
            runs += [cs2_skin.rope_run(0.0, (0.0, spec.comm_y), pose.get(b, 0.0), sign, ext, r=VANILLA_LV_ROPE["r"])
                     for b, sign in (("TrainFront", cs2_skin.FRONT_SIGN_NET), ("TrainBack", -cs2_skin.FRONT_SIGN_NET))]
        for v, f in runs:
            me = bpy.data.meshes.new(f"{name}Rope")
            me.from_pydata(v, [], f)
            me.materials.append(mat)
            objs.append(bpy.data.objects.new(me.name, me))
            bpy.context.collection.objects.link(objs[-1])
        try:
            written += render_tower_views(objs[0], asset_dir, name, out_dir, vv, extras=objs[1:], light=SKIN_LIGHT)
        finally:
            for ob in objs:
                me = ob.data
                bpy.data.objects.remove(ob, do_unlink=True)
                bpy.data.meshes.remove(me)
    bpy.data.materials.remove(mat)
    return written


def skin_roundtrip(asset_dir, name, levels, spec):
    """Re-import every exported level (cs2_skin.roundtrip: the raw file frame the game reads) and hold the rig to the
    design: bones exactly SKIN_BONES at skin_heads, identity rests, one bone per vertex, and the axis test (TrainFront
    at raw z > 0 = game +z). Returns {level: {...}}."""
    return cs2_skin.roundtrip(asset_dir, name, levels, skin_heads(spec), _check, spec.key)


# The axis record (manifest skin.axis): every link of authoring +X -> game +z, cited
SKIN_AXIS = {
    "game_plus_z": "net -Y = authoring +X (TrainFront = the halves built at authoring +X: rocker_main_<side>_f, "
                   "yoke_<side>_f)",
    "chain": ["cs2_asset.orient_for_net (cs2_asset.py:40): (x, y, z) -> (y, -x, z), authoring +X -> net -Y",
              "cs2_asset.export_fbx_skinned (cs2_asset.py:1484-1487): -90 deg about X baked, (x, y, z) -> (x, z, -y) "
              "x 100 cm: net -Y -> file +z",
              "the game negates FBX x on import (docs/PIPELINE.md frame rule T): game = (-x_net, z_net, -y_net)"],
    "tests": ["skin_part_bones: the _f plates ride TrainFront by travel side (build time)",
              "cs2_skin.roundtrip: every exported level's TrainFront group bbox center at raw z > 0, TrainBack < 0",
              "tests/test_tower_skinned.py K8 (the axis test)"],
}


def skin_manifest_block(spec, skin):
    """Manifest `skin` (SKINNED only): the rig, the axis record, G6, the weights, the round trip and the surgery."""
    zp = lane_y(spec, True)
    return {"rule": "lift alignment phase 2 round 2 (articulation design note, option C): skinned to Root / TrainFront "
                    "/ TrainBack; our code pitches TrainFront by the rope leaving toward game +z and TrainBack by the "
                    "rope toward -z, about each bone's local X (+ lowers the game +z end), each clamped to theta_max, "
                    "the half-break |front - back| / 2 to 20 deg",
            "bones": {"Root": {"parent": None, "head_game": [0.0, 0.0, 0.0]},
                      "TrainFront": {"parent": "Root", "head_game": [0.0, zp, 0.0], "rest_rotation": [0, 0, 0, 1],
                                     "side": "game +z"},
                      "TrainBack": {"parent": "Root", "head_game": [0.0, zp, 0.0], "rest_rotation": [0, 0, 0, 1],
                                    "side": "game -z"}},
            "axis": SKIN_AXIS,
            "rope_points_game": skin["rope_points_game"], "lane_y": zp, "crown_seat": skin["crown_seat"],
            "theta_max_deg": skin["theta_max_deg"], "half_break_max_deg": skin["half_break_max_deg"],
            "half_shift_m": skin["half_shift_m"], "sheaves": skin["sheaves"],
            "g6": {"poses": skin["poses"], "min_clear": skin["min_clear"], "seat_rest_m": skin["seat_rest_m"]},
            "train_parts": skin["train_parts"], "weights": skin.get("weights"), "roundtrip": skin.get("roundtrip"),
            "mesh_changes": {
                "FG": "cs2_sheaves.fg_train_side_halves: per lane two 2-wheel bogies, each on its main-rocker half "
                      + ("(a half beam at the shipped height on a yoke strap rising from the main pin, the two halves "
                         "lapping, the front outboard; the bogie pivots between beam and bar as shipped)"
                         if SKIN_FG.get("beam") else
                         "(lug plates lapping on the main pin, the front plate outboard)")
                      + f", the bogies {SKIN_HALF_SHIFT['FG']} m further out; the hanger one slab inboard holds the "
                      "main pin",
                "DQ": "cs2_sheaves.dq_train_halves: per lane two 4-wheel bogies on the yoke halves (lug plates lapping on "
                      f"the main pin), every sheave {SKIN_HALF_SHIFT['DQ']} m further out; the hanger and the Kagelok "
                      "girder end cut back to |y| 2.394 (SKIN_DQ gap short of the yoke); a main pin from |y| 2.30 "
                      "through hanger, girder end and yoke; the yoke halves at axle + "
                      f"{SKIN_YOKE['DQ'][0]}..{SKIN_YOKE['DQ'][1]} (the shipped 0.17 depth and 0.072 section from "
                      f"{SKIN_CLEVIS['dq_beam_x']} m out, cheeks on the pin) under the gantry's slanted posts",
                "GD": f"cs2_sheaves.dq_train_halves at {SKIN_GD['sheaves']} sheaves a side: per lane two halves (a "
                      "4-wheel bogie on a main rocker + a 2-wheel bogie on a link at 12, + a 1-wheel sub on a link at "
                      "10; one 4-wheel bogie at 8) on the "
                      f"yoke halves, every sheave {SKIN_HALF_SHIFT['GD']} m further out; the gantry posts land on the "
                      "girder ends (inboard of the yoke and the rope); the hanger cut short of the yoke; a main pin from "
                      "inside the girder end through hanger and yoke"}[spec.family],
            "surgery": [
                "after EVERY import, every LOD prefab: the root bone (parentId -1) m_Rotation (0, 0, 0, 1) and m_Scale "
                "(1, 1, 1) (the import gives it -90 deg X / scale 100; gate BONEROOT)",
                "TrainFront / TrainBack: m_Type 0 (None); our code writes each Bone m_Rotation = rest x pitch about "
                "local X and sets Skeleton.m_CurrentUpdated (PIPELINE 'Skinned (procedural-bone) assets'); bone names "
                "map through ProceduralBone.m_BindIndex (the engine re-sorts bones by hierarchy depth)",
                "LOD0 / LOD1 / LOD2 FBXs are all skinned (SetupLODs copies LOD0's ProceduralAnimationProperties to "
                "every LOD); settings.json useProceduralAnimation",
                f"rope-lane anchors at the crown lane (+-{spec.gauge / 2.0}, {zp}, 0) (T27, phase 1)"]}


def flange_top(spec):
    """Top of the mast-base flange plate (FG 0.19, DQ / GD 0.20)."""
    if spec.family == "FG":
        return round(FG_FLANGE_Z + FG_FLANGE_T / 2.0, 6)
    return round(DQ_FLANGE_Z + DQ_FLANGE_T / 2.0, 6)


def crashpad_z0(spec):
    """Review F3: the crash pad's bottom = flange plate top + PAD_FLANGE_GAP (0.195 / 0.205)."""
    return round(flange_top(spec) + PAD_FLANGE_GAP, 6)


def crashpad_parts(z0=None, radius=None):
    """
    CRASHPAD shell (cs2_asset.CRASHPAD contract, open sector on -X) split in two parts:
    `crashpad_outer` = the outer wall only, UVMap u = R * theta, v = z in meters (theta
    -135..+135 deg, unmirrored seen from outside: the warning-triangle island keeps its 3D
    aspect through the unique pack); `crashpad` = inner wall, rims and end caps, box UVs.
    Windings are explicit (outward), 28 + 88 tris. `z0` overrides CRASHPAD["z0"] (0.15): the
    v2 towers pass crashpad_z0(spec) so the pad sits on the base flange (review F3). `radius`
    overrides the shell's outer radius (GD: pad_radius(spec) around the r 0.50 mast).
    """
    cp = cs2_asset.CRASHPAD
    R, t = (cp["radius"] if radius is None else radius), cp["thickness"]
    ri = R - t
    z0, z1 = (cp["z0"] if z0 is None else z0), cp["z1"]
    segs = cp["segments"]
    half = math.radians(cp["arc_deg"]) / 2.0
    ang = [-half + i * (2.0 * half) / segs for i in range(segs + 1)]

    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    lo = [bm.verts.new((R * math.cos(a), R * math.sin(a), z0)) for a in ang]
    hi = [bm.verts.new((R * math.cos(a), R * math.sin(a), z1)) for a in ang]
    for k in range(segs):
        f = bm.faces.new((lo[k], lo[k + 1], hi[k + 1], hi[k]))
        for lp, (i, z) in zip(f.loops, ((k, z0), (k + 1, z0), (k + 1, z1), (k, z1))):
            lp[layer].uv = (R * ang[i], z)
    outer = _new_part("crashpad_outer", bm)

    bm = bmesh.new()
    layer = bm.loops.layers.uv.new("UVMap")
    ring = {}
    for tag, rad in (("o", R), ("i", ri)):
        for ztag, z in (("0", z0), ("1", z1)):
            ring[tag + ztag] = [bm.verts.new((rad * math.cos(a), rad * math.sin(a), z))
                                for a in ang]
    o0, o1, i0, i1 = ring["o0"], ring["o1"], ring["i0"], ring["i1"]
    for k in range(segs):
        bm.faces.new((i0[k + 1], i0[k], i1[k], i1[k + 1]))      # inner wall (faces the mast)
        bm.faces.new((o1[k], o1[k + 1], i1[k + 1], i1[k]))      # top rim (+Z)
        bm.faces.new((o0[k + 1], o0[k], i0[k], i0[k + 1]))      # bottom rim (-Z)
    bm.faces.new((o0[0], o1[0], i1[0], i0[0]))                  # end cap at -135 deg
    bm.faces.new((o0[segs], i0[segs], i1[segs], o1[segs]))      # end cap at +135 deg
    _box_uv_bm(bm, layer)
    rest = _new_part("crashpad", bm)
    return [(outer, "pad"), (rest, "pad")]


def _uvmap_box(obj):
    """Give a legacy-built part one unmirrored box-projected "UVMap" layer."""
    me = obj.data
    for lay in [l.name for l in me.uv_layers]:
        me.uv_layers.remove(me.uv_layers[lay])
    bm = bmesh.new()
    bm.from_mesh(me)
    layer = bm.loops.layers.uv.new("UVMap")
    _box_uv_bm(bm, layer)
    bm.to_mesh(me)
    bm.free()
    me.update()


def _rot_euler(ex, ez):
    """Euler that maps local +X -> ex and local +Z -> ez (ey = ez x ex, right-handed)."""
    ex, ez = mathutils.Vector(ex), mathutils.Vector(ez)
    ey = ez.cross(ex)
    m = mathutils.Matrix((ex, ey, ez)).transposed()
    return tuple(m.to_euler())


def _kit(parts_q, key_of):
    """cs2_kit (obj, quad) list -> (obj, material key) by a name -> key rule."""
    return [(o, key_of(o.name)) for o, _q in parts_q]


# --------------------------------------------------------------------------------------
# FG family (FG8/12/16 line, ET62/68 hosted): legacy head geometry, v2 parts (section 5.1)
# --------------------------------------------------------------------------------------

def _mast_part(spec, height, caps, cut):
    """The mast (16-gon, z 0..height); `cut` (M1) adds the edge loop the metric UV splits the
    island at (+16 quads), hidden under that stiff_mastjoint ring."""
    m = K.add_cyl("mast", spec.mast_r, height, (0.0, 0.0, height / 2.0), segments=16, caps=caps)
    if cut is not None:
        bm = bmesh.new()
        bm.from_mesh(m.data)
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=(0.0, 0.0, cut - height / 2.0),
                               plane_no=(0.0, 0.0, 1.0))
        bm.to_mesh(m.data)
        bm.free()
        m.data.update()
    return m


def _joint_zs(spec, height):
    if height <= 9.0:
        return []
    n_sec = max(2, int(round(height / 6.0)))
    return [j * height / n_sec for j in range(1, n_sec)]


def _ring_segs(spec, joints):
    """DQ20 reclaim (DP1_RING_SEGS_RECLAIM): 12-segment joint rings on a DQ member with two
    joints; every frozen variant keeps 16."""
    return DP1_RING_SEGS_RECLAIM if (spec.family == "DQ" and len(joints) >= 2) else 16


def fg_parts(spec):
    mast = spec.tube_top
    A = spec.axle
    arm_top = mast + FG_ARM_T / 2.0
    P = []
    P.append((K.add_box("pad", (spec.pad, spec.pad, 0.45), (0.0, 0.0, -0.075), drop=("-Z",)),
              "concrete"))
    P.append((K.add_box("pier", (spec.pad, spec.pad, spec.pier - 0.30),
                        (0.0, 0.0, (-spec.pier - 0.30) / 2.0), drop=("+Z", "-Z")), "concrete"))
    joints = _joint_zs(spec, mast)
    cut = _mast_cut_z(spec, mast - 0.14, joints)
    P.append((_mast_part(spec, mast - 0.14, "none", cut), "paint"))
    # detail pass 1: the flange plate's bottom sits on the pad top (hidden, design 1.4)
    P += _kit(K.flange("mastbase", FG_FLANGE_R, t=FG_FLANGE_T, bolts=8, segs=16, loc=(0.0, 0.0, FG_FLANGE_Z),
                       bolt_geo=True, caps="top"), lambda n: "galv" if n.startswith("bolt_") else "paint")
    P += _kit(K.flange("armcap", 0.44, t=0.03, segs=16, loc=(0.0, 0.0, mast - 0.125)),
              lambda n: "paint")
    for j, zj in enumerate(joints, start=1):
        P += _kit(K.stiffener_ring(f"mastjoint_{j}", spec.mast_r, w=0.04, t=0.08,
                                   loc=(0.0, 0.0, zj), segs=16), lambda n: "paint")
    for tag, s in (("n", -1.0), ("p", 1.0)):
        P += _kit(K.gusset(f"arm_{tag}", 0.35, 0.30, t=0.02,
                           loc=(0.0, s * spec.mast_r, mast - FG_ARM_T / 2.0),
                           rot=_rot_euler((0.0, s, 0.0), (0.0, 0.0, -1.0))), lambda n: "paint")
    P.append((K.add_box("crossarm", (FG_ARM_D, FG_ARM_SPAN, FG_ARM_T), (0.0, 0.0, mast)),
              "paint"))

    # sheave trains: the legacy rocker tree verbatim (build_sheave_train), kit parts
    main_z = mast - FG_HANGER
    sub_z = main_z - FG_MAIN_TO_SUB
    axle_z = sub_z - FG_SUB_TO_AXLE
    assert abs(axle_z - A) < 1e-9, (axle_z, A)
    n_sub = max(1, spec.sheaves // 2)
    sub_span = spec.pitch
    main_span = (n_sub - 1) * (sub_span + 2.0 * spec.sheave_r + 0.10)
    ins = DP1_INSET["fg_hanger_top"]               # M4: hanger top 5 mm inside the crossarm
    for tag, s in (("n", -1.0), ("p", 1.0)):
        y = s * spec.gauge / 2.0
        if SKINNED:
            # lift alignment phase 2 round 2: two half trains (a 2-wheel bogie each) lapping on the main pin on the
            # rope line, held by a hanger one slab inboard
            if n_sub != 2:
                raise TowerContractError(f"{spec.key}: the FG half trains take 2 bogies a lane (got {n_sub})")
            P += S.fg_train_side_halves(tag, s, y, main_z, sub_z, axle_z, sub_span, main_span / 2.0,
                                        spec.sheave_r, spec.sheave_w, pin_z=lane_y(spec, True),
                                        inboard=FG_CROWN_INBOARD, shift=SKIN_HALF_SHIFT["FG"], arm_t=FG_ARM_T,
                                        hanger=FG_HANGER, ins=ins, lap_gap=SKIN_LAP_GAP, **SKIN_FG)
            continue
        # the Yan rocker tree of this lane: cs2_sheaves.fg_train_side (this loop's body, moved verbatim)
        P += S.fg_train_side(tag, s, y, main_z, sub_z, axle_z, n_sub, sub_span, main_span,
                             spec.sheave_r, spec.sheave_w, arm_t=FG_ARM_T, hanger=FG_HANGER, ins=ins,
                             **({"inboard": FG_CROWN_INBOARD} if ROPE_ON_CROWN else {}))

    # head details (legacy build_head_details numbers)
    lf_h, lf_y = 0.62, 1.15
    for tag, s in (("n", -1.0), ("p", 1.0)):
        P.append((K.add_box(f"liftpost_{tag}", (0.09, 0.09, lf_h),
                            (0.0, s * lf_y, arm_top + lf_h / 2.0), drop=("-Z",)), "paint"))
    P.append((K.add_box("liftbar", (0.11, lf_y * 2 + 0.30, 0.11),
                        (0.0, 0.0, arm_top + lf_h + 0.055)), "paint"))
    cw_x = -(FG_ARM_D / 2.0 + 0.21)
    P += _kit(K.grating("catwalk", 0.42, 2.70, t=0.035, loc=(cw_x, 0.0, arm_top + 0.018)),
              lambda n: "galv")
    P += _kit(K.handrail("catwalk", [(cw_x - 0.19, -1.30, arm_top + 0.0355),
                                     (cw_x - 0.19, 1.30, arm_top + 0.0355)],
                         h=0.8445, knee=0.45, kick=0.10, post_every=1.30, segs=6, radius=0.024,
                         post_radius=0.022), lambda n: "galv")
    for tag, s in (("n", -1.0), ("p", 1.0)):
        P.append((K.add_box(f"cwbrace_{tag}", (0.40, 0.05, 0.05),
                            (cw_x + 0.10, s * 0.9, arm_top - 0.02)), "galv"))
    defl = build_tip_deflector(TowerParams(mast_height=mast))      # legacy plate, same geometry
    _uvmap_box(defl)
    P.append((defl, "paint"))
    for tag, s in (("n", -1.0), ("p", 1.0)):
        # detail pass 1: both insulator caps are hidden (crossarm top / insulcap bottom)
        P.append((K.add_cyl(f"insulator_{tag}", 0.035, 0.28, (0.0, s * 0.45, arm_top + 0.14),
                            segments=6, caps="none"), "galv"))
        P.append((K.add_box(f"insulcap_{tag}", (0.09, 0.09, 0.04),
                            (0.0, s * 0.45, arm_top + 0.30)), "galv"))
    if SKINNED and spec.comm_y is not None:
        P += comm_hanger_parts(spec, arm_top)

    # ladder up the -X face (legacy build_ladder numbers; rungs 4-seg uncapped, detail pass 1:
    # turned 45 deg about their axis - a flat top, the square-bar rung - instead of a corner)
    lx = -(spec.mast_r + 0.10)
    top = mast + FG_ARM_T / 2.0 + 0.05
    for tag, s in (("n", -1.0), ("p", 1.0)):
        P.append((K.add_cyl(f"ladder_rail_{tag}", 0.035, top, (lx, s * spec.ladder_w / 2.0, top / 2.0),
                            segments=6, caps="top"), "galv"))
    n_rungs = int(top / spec.ladder_pitch)
    for i in range(1, n_rungs):
        P.append((K.add_cyl(f"ladder_rung_{i}", 0.025, spec.ladder_w, (lx, 0.0, i * spec.ladder_pitch),
                            (math.radians(90.0), math.radians(45.0), 0.0), segments=4, caps="none"), "galv"))
    for k, frac in enumerate((0.18, 0.52, 0.86)):
        P.append((K.add_box(f"ladder_standoff_{k}", (0.22, 0.05, 0.05),
                            (-(spec.mast_r + 0.05), 0.0, top * frac)), "galv"))
    P += crashpad_parts(crashpad_z0(spec))
    info = {"arm_top": arm_top, "mast_cut": cut, "joints": joints}
    P += detail_parts_fg(spec, info, lx)
    S.cull_hidden_bosses(P, label=spec.key)         # T62 round 3: hidden hub bosses dropped (measured; v1 no-op)
    return P, info


def detail_parts_fg(spec, info, lx):
    """FG / ET detail-pass parts (design 3.1): the two tower plates on the crossarm's travel
    faces, conduit + junction box (below the tip deflector, which the design missed), and
    the fall-arrest rail (from the first rung to just under the catwalk grating)."""
    mast = spec.tube_top
    P = []
    for tag, s in (("n", -1.0), ("p", 1.0)):
        P += plate_parts(tag, (s * FG_ARM_D / 2.0, 0.0, mast), (s, 0.0, 0.0), DP1_PLATE["FG"])
    # the tip deflector (build_tip_deflector) wraps the +Y side at axle - 0.67 .. axle - 0.17,
    # r 0.40-0.62: the junction box stays 0.10 under it (design: "<= mast - 0.50")
    z_top = min(mast - DP1_CONDUIT["fg_top_below_mast"], spec.axle - 0.67 - 0.10)
    cp, cinfo = conduit_parts(spec, DP1_CONDUIT["fg_foot_z"], z_top)
    P += cp
    info["conduit"] = cinfo
    rung_half = 0.025 * math.cos(math.radians(45.0))
    z_grate = info["arm_top"] + 0.018 - 0.035 / 2.0            # catwalk grating underside
    P += arrest_part(lx, rung_half, spec.ladder_pitch, z_grate - 0.002)
    info["arrest"] = {"x_rung": lx, "z": (spec.ladder_pitch, z_grate - 0.002)}
    return P


FG_BEVEL = {"crossarm": 2, "hanger_": 1, "rocker_main_": 2, "liftbar": 2}
DQ_BEVEL = {"rocker_main_": 1, "rocker_link_": 1, "gantry_beam": 2, "gantry_girder_": 1}


# --------------------------------------------------------------------------------------
# DQ family (DQ10/12/14 line, ETDQ hosted compression): gantry head (section 5.2)
# --------------------------------------------------------------------------------------

def dq_deck_length(spec):
    """Deck length L = max(4.30, train + 0.20); train = rim tip to rim tip [K, D] (SKINNED: + the two halves' shift)."""
    train = (spec.sheaves - 1) * spec.pitch + 2.0 * spec.sheave_r
    if SKINNED:
        train += 2.0 * SKIN_HALF_SHIFT[spec.family]
    return max(4.30, train + 0.20)


def dq_sheave_centres(spec, side):
    """[(X_k, Y, Z_k)] for one side: X_k = (k - (n-1)/2) pitch, Z_k = A - sag (2 X_k / L_c)^2."""
    return S.dq_centres(spec.sheaves, spec.pitch, spec.axle, spec.sag, spec.gauge, side)


def _gantry_rebuild(spec):
    """Rebuilt portal (section 4.2 numbers) + the F13 saddle as the girders' shared block.
    GD (Revision 1b): the same members stretched by head_kit (beam, posts, end plates out by
    0.40; girder ends 0.39; the whole portal 0.05 higher -> end-plate tops at A + 2.000); no
    saddle (the r 0.50 tube tops out at A + 0.50, above the saddle's A + 0.453: it would be
    100 % hidden); the A-braces keep |Y| 1.622."""
    A = spec.axle
    kit = head_kit(spec)
    dy, rise = kit["dy"], kit["rise"]
    P = []
    P.append((K.add_box("gantry_beam", (0.30, 2 * (2.922 + dy), 0.234), (0.0, 0.0, A + 1.809 + rise)),
              "paint"))
    pb = DP1_INSET["gd_post_bottom"]          # M4: post bottom 5 mm above the yoke's bottom face
    for tag, s in (("n", -1.0), ("p", 1.0)):
        g = girder_box(spec)
        if SKINNED and spec.family == "GD":
            # lift alignment phase 2 round 2: the post lands on the girder end (outboard face SKIN_GD post_inset in from
            # it, buried post_bury in its top), inboard of the yoke the half trains pitch in: it came down onto the
            # yoke at |y| 2.811..3.009, through the rope on the crowns (lane 3.0, the T27 C20 head-rule item)
            yo, z0, z1 = g["y1"] - SKIN_GD["post_inset"], A + g["z1"] - SKIN_GD["post_bury"], A + 1.692 + rise
            P.append((K.add_box(f"gantry_post_{tag}", (0.18, 0.198, z1 - z0), (0.0, s * (yo - 0.099), (z0 + z1) / 2.0),
                                drop=("+Z", "-Z")), "paint"))
        else:
            # detail pass 1: the post top is hidden under the beam (design 1.4: dropped)
            P.append((K.add_box(f"gantry_post_{tag}", (0.18, 0.198, 1.442 + rise - pb),
                                (0.0, s * (2.51 + dy), A + 0.25 + 0.721 + rise / 2.0 + pb / 2.0),
                                drop=("+Z",)), "paint"))
        # the girder's inner end is hidden on the saddle's face (DQ / ETDQ; GD has no saddle)
        inner = (("+Y",) if s < 0 else ("-Y",)) if spec.family != "GD" else ()
        P.append((K.add_box(f"gantry_girder_{tag}", (g["x"], g["y1"] - g["y0"], g["z1"] - g["z0"]),
                            (0.0, s * (g["y0"] + g["y1"]) / 2.0, A + (g["z0"] + g["z1"]) / 2.0),
                            drop=inner), "paint"))
    # F13 [D]: the saddle is the block the two girders share (girder section, between their
    # inner ends at Y +-0.12), dropped 0.09 into the tube top; Kagelok's is part of the
    # gantry island (0.79 u block, z 22.23-24.23 rel flange).
    if spec.family != "GD":
        P.append((K.add_box("gantry_saddle", (DQ_GIRDER["x"], 2 * DQ_GIRDER["y0"], 0.63),
                            (0.0, 0.0, A + 0.138)), "paint"))
    top = (0.0, A + 1.692 + rise)
    foot_x = 2.17
    dz = (A + DQ_DECK) - top[1]
    L = math.hypot(foot_x, dz)
    theta = math.atan2(-dz, foot_x)
    for tag, s in (("n", -1.0), ("p", 1.0)):
        for btag, bx in (("a", -1.0), ("b", 1.0)):
            mid = (bx * foot_x / 2.0, s * 1.622, (top[1] + A + DQ_DECK) / 2.0)
            # M4: brace a 10 mm narrower laterally - its side faces no longer share b's planes
            wy = DP1_INSET["brace_a_w"] if btag == "a" else 0.186
            P.append((K.add_box(f"gantry_brace_{tag}_{btag}", (L, wy, 0.186), mid,
                                rot=(0.0, bx * theta, 0.0)), "paint"))
    for i, y in enumerate((-2.957, -2.735, 2.735, 2.957)):
        P.append((K.add_box(f"endplate_{i}", (0.492, 0.072, 0.285),
                            (0.0, y + math.copysign(dy, y), A + H_TOP[spec.family] - 0.1425)), "paint"))
    P += gusset_apex_parts(spec, top[1])
    return P


def _dq_rails(spec, L):
    """F10: deck rails split at the portal - four runs, none crossing a girder or hanger."""
    A = spec.axle
    P = []
    z = A + DQ_DECK
    for tag, s in (("n", -1.0), ("p", 1.0)):
        y = s * rail_y(spec)
        for half, (x0, x1) in enumerate(((-L / 2.0 + 0.1, -DQ_RAIL_GAP), (DQ_RAIL_GAP, L / 2.0 - 0.1))):
            P += _kit(K.handrail(f"deck{tag}{half}", [(x0, y, z), (x1, y, z)], h=1.05, knee=0.50,
                                 kick=0.10, post_every=1.6, segs=6, radius=0.028),
                      lambda n: "paint")
    return P


def _dq_ladder(spec):
    """
    F11: the ladder tops out at deck level BESIDE two landing plates that bridge from the
    ladder to each deck strip (|Y| 0.315..0.695 at X -0.85..-0.40, top = deck top); the
    rails run on as grab rails to deck + 1.0.
    """
    A = spec.axle
    P = []
    deck = A + DQ_DECK
    rail_top = deck + 1.0
    mdr = head_kit(spec)["mdr"]                 # GD: ladder + landings 0.05 further out (r 0.50 mast)
    lx = DQ_LADDER_X - mdr
    lx0, lx1 = DQ_LANDING_X0 - mdr, DQ_LANDING_X1 - mdr
    for tag, s in (("n", -1.0), ("p", 1.0)):
        P.append((K.add_cyl(f"ladder_rail_{tag}", 0.035, rail_top,
                            (lx, s * spec.ladder_w / 2.0, rail_top / 2.0),
                            segments=6, caps="top"), "galv"))
    n_rungs = int(math.floor(deck / spec.ladder_pitch + 1e-6))
    for i in range(1, n_rungs + 1):
        P.append((K.add_cyl(f"ladder_rung_{i}", 0.025, spec.ladder_w,
                            (lx, 0.0, i * spec.ladder_pitch),
                            (math.radians(90.0), math.radians(45.0), 0.0), segments=4, caps="none"),
                  "galv"))
    for k, frac in enumerate((0.12, 0.37, 0.62, 0.87)):
        P.append((K.add_box(f"ladder_standoff_{k}", (0.20, 0.05, 0.05),
                            (-(spec.mast_r + 0.05), 0.0, rail_top * frac)), "galv"))
    y_in = spec.ladder_w / 2.0 + 0.035
    w_x = lx1 - lx0
    for tag, s in (("n", -1.0), ("p", 1.0)):
        P += _kit(K.grating(f"land{tag}", w_x, DQ_DECK_Y0 - y_in, t=DQ_GRATE_T,
                            loc=((lx0 + lx1) / 2.0,
                                 s * (y_in + DQ_DECK_Y0) / 2.0, deck - DQ_GRATE_T / 2.0)),
                  lambda n: "galv")
    return P


def skin_clevis(spec):
    """The clevis dict for cs2_sheaves.dq_train_halves (SKIN_CLEVIS): the tongue's top in the head by family."""
    C = SKIN_CLEVIS
    if spec.family == "GD":
        top, hw, bx = spec.axle + 1.692 + head_kit(spec)["rise"] + C["gd_bury"], C["gd_top_hw"], C["gd_beam_x"]
    else:
        top, hw, bx = spec.axle + C["dq_top"], C["dq_top_hw"], C["dq_beam_x"]
    return dict(plate_t=C["plate_t"], gap=C["gap"], r=C["r"], top_z=round(top, 6), top_hw=hw, beam_x=bx)


def _dq_train(spec):
    """Sheave trains with every carrier INBOARD of the sheaves (section 4.2, F9). GD: carriers
    and hangers at head_kit cdy (lane - 0.098 / - 0.17 / - 0.20..- 0.31), the lattice's
    12-segment r 0.25 flat sheaves, odd sub counts symmetric (_sub_pairs). Revision 1c: a sub
    no main carries hangs from the yoke on a link `rocker_link_<side>_<sub>` (DQ_LINK), its
    pivot pin through the link's lug (C21)."""
    n = spec.sheaves
    if n % 2:
        raise TowerContractError(f"{spec.key}: sheaves per side must be even, got {n}")
    kit = head_kit(spec)
    cdy = kit["cdy"]
    sub_y, main_y = DQ_SUB_Y + cdy, DQ_MAIN_Y + cdy
    # cs2_sheaves.dq_train = this function's body, moved verbatim; THIS module's globals (DQ_SUB_Y, DQ_LINK,
    # DP1_INSET, head_kit / hanger_box) are passed at call time, so the tests' constant patches still apply
    if not SKINNED:
        return S.dq_train(lambda s: dq_sheave_centres(spec, s), n, spec.axle, spec.sheave_r, spec.sheave_w,
                          sheave_segs(spec), sheave_groove(spec), sub_y, main_y, kit["pin_s"], hanger_box(spec),
                          DP1_INSET["dq_hanger_z"], link=DQ_LINK, ngon=sheave_ngon(spec))
    # lift alignment phase 2 round 2: two half trains a lane on the yoke halves, lapping on the main pin
    P = S.dq_train_halves(lambda s: dq_sheave_centres(spec, s), n, spec.axle, spec.sheave_r, spec.sheave_w,
                          sheave_segs(spec), sheave_groove(spec), sub_y, main_y, kit["pin_s"], hanger_box(spec),
                          DP1_INSET["dq_hanger_z"], pin_z=lane_y(spec, True),
                          yoke_top=SKIN_YOKE[spec.family][1], yoke_bot=SKIN_YOKE[spec.family][0],
                          shift=SKIN_HALF_SHIFT[spec.family], link=DQ_LINK, ngon=sheave_ngon(spec),
                          lap_gap=SKIN_LAP_GAP, clevis=skin_clevis(spec))
    # the main pin on the rope line (travel 0, crown lane y), from inside the girder end (SKIN_DQ / SKIN_GD pin_in)
    # through the hanger and the yoke, its outboard end `poke` proud of the yoke
    a = SKIN_GD["pin_in"] if spec.family == "GD" else SKIN_DQ["pin_in"]
    b = main_y + 0.072 / 2.0 + SKIN_DQ["poke"]
    for tag, s in (("n", -1.0), ("p", 1.0)):
        P.append((K.add_cyl(f"pivot_main_{tag}", SKIN_DQ["pin_r"], b - a, (0.0, s * (a + b) / 2.0, lane_y(spec, True)),
                            (math.radians(90.0), 0.0, 0.0), segments=8, caps="both"), "galv"))
    return P


def dq_parts(spec, head, kagelok=None):
    A = spec.axle
    TT = spec.tube_top
    L = dq_deck_length(spec)
    P = []
    P.append((K.add_box("pad", (spec.pad, spec.pad, 0.45), (0.0, 0.0, -0.075), drop=("-Z",)),
              "concrete"))
    P.append((K.add_box("pier", (spec.pad, spec.pad, spec.pier - 0.30),
                        (0.0, 0.0, (-spec.pier - 0.30) / 2.0), drop=("+Z", "-Z")), "concrete"))
    joints = _joint_zs(spec, TT)
    cut = _mast_cut_z(spec, TT, joints)
    P.append((_mast_part(spec, TT, "top", cut), "paint"))
    # detail pass 1: the flange plate's bottom sits on the pad top (hidden, design 1.4)
    P += _kit(K.flange("mastbase", flange_r(spec), t=DQ_FLANGE_T, bolts=8, segs=16, loc=(0.0, 0.0, DQ_FLANGE_Z),
                       bolt_geo=True, caps="top"), lambda n: "galv" if n.startswith("bolt_") else "paint")
    rs = _ring_segs(spec, joints)
    for j, zj in enumerate(joints, start=1):
        P += _kit(K.stiffener_ring(f"mastjoint_{j}", spec.mast_r, w=0.04, t=0.08,
                                   loc=(0.0, 0.0, zj), segs=rs), lambda n: "paint")
    info = {"deck_length": L, "mast_cut": cut, "joints": joints, "ring_segs": rs}
    if head == "ingest":
        gp, rep = ingest_gantry(spec, kagelok or KAGELOK_BLEND)
        P += gp
        info["ingest"] = rep
    elif head == "rebuild":
        P += _gantry_rebuild(spec)
    else:
        raise ValueError(f"{spec.key}: head {head!r} not in {spec.heads}")
    y0, y1 = deck_y(spec)
    for tag, s in (("n", -1.0), ("p", 1.0)):
        P += _kit(K.grating(f"deck{tag}", L, y1 - y0, t=DQ_GRATE_T,
                            loc=(0.0, s * (y0 + y1) / 2.0, A + DQ_DECK - DQ_GRATE_T / 2.0)),
                  lambda n: "galv")
    P += _dq_rails(spec, L)
    P += _dq_train(spec)
    P += _dq_ladder(spec)
    P += crashpad_parts(crashpad_z0(spec), radius=pad_radius(spec))
    P += detail_parts_dq(spec, head, info, P)
    S.cull_hidden_bosses(P, label=spec.key)         # T62 round 3: hidden hub bosses dropped (measured; v1 no-op)
    return P, info


def ingest_beam_faces(gantry, spec):
    """The Kagelok gantry_00 beam's two TRAVEL faces at the centerline (design 3.1; skeptic M1
    / M10: plane AND normal measured, the ingest gantry is sheared): faces with |n.x| > 0.9
    whose y extent reaches |y| <= 0.3 (the beam is two half-shells meeting at y 0: measured
    faces x -0.151 / +0.149, z A + 1.691 .. A + 1.925, unbevelled) and center z A + 1.70 ..
    A + 1.93. Returns {+1 / -1: (center on the face plane at y 0 and the faces' mid height,
    unit normal, (z0, z1), (y0, y1))}."""
    A = spec.axle
    me = gantry.data
    out = {}

    def yext(p):
        ys = [me.vertices[v].co.y for v in p.vertices]
        return min(ys), max(ys)
    for sgn in (-1.0, 1.0):
        sel = [p for p in me.polygons
               if p.normal.x * sgn > 0.9 and yext(p)[0] <= 0.3 and yext(p)[1] >= -0.3
               and A + 1.70 <= p.center.z <= A + 1.93]
        if not sel:
            raise TowerContractError(f"{spec.key}: no gantry_00 beam face with n.x {sgn:+.0f} at the centerline")
        area = sum(p.area for p in sel)
        n = sum((p.normal * p.area for p in sel), mathutils.Vector()) / area
        n.normalize()
        c = sum((p.center * p.area for p in sel), mathutils.Vector()) / area
        vz = [me.vertices[v].co.z for p in sel for v in p.vertices]
        vy = [me.vertices[v].co.y for p in sel for v in p.vertices]
        z0, z1 = min(vz), max(vz)
        zc = (z0 + z1) / 2.0
        # the plane point at y 0, z zc: c + t, t in the plane (n . t = 0)
        p = mathutils.Vector((c.x - (n.y * (0.0 - c.y) + n.z * (zc - c.z)) / n.x, 0.0, zc))
        out[int(sgn)] = (tuple(p), tuple(n), (z0, z1), (min(vy), max(vy)))
    return out


def detail_parts_dq(spec, head, info, parts):
    """DQ / GD / ETDQ / ETGD detail-pass parts (design 3.1): the tower plates on the gantry
    beam's travel faces (rebuild: the 0.30 beam's faces at x +-0.150; ingest: the measured
    gantry_00 face), the conduit + junction box (foot on the pad rim top, M9; box top under
    the deck), the fall-arrest rail (first rung -> the ladder rails' top)."""
    A = spec.axle
    P = []
    if head == "ingest":
        g = next(o for o, _k in parts if o.name == "gantry_00")
        faces = ingest_beam_faces(g, spec)
        info["plate_faces"] = {k: {"centre": [round(c, 5) for c in v[0]], "normal": [round(c, 5) for c in v[1]],
                                   "z": [round(z, 4) for z in v[2]], "y": [round(y, 4) for y in v[3]]}
                               for k, v in faces.items()}
        for tag, s in (("n", -1), ("p", 1)):
            cen, nrm, zr, yr = faces[s]
            w, h = DP1_PLATE[spec.family]
            _check(zr[1] - zr[0] >= h + 0.02 and yr[0] <= -w / 2.0 and yr[1] >= w / 2.0,
                   f"[{spec.key}] DT-C6 ingest beam face {tag} holds the {w} x {h} plate",
                   f"(face z {zr[0] - A:+.3f}..{zr[1] - A:+.3f} rel A, y {yr[0]:+.3f}..{yr[1]:+.3f})")
            P += plate_parts(tag, cen, nrm, DP1_PLATE[spec.family])
    else:
        rise = head_kit(spec)["rise"]
        for tag, s in (("n", -1.0), ("p", 1.0)):
            P += plate_parts(tag, (s * 0.15, 0.0, A + 1.809 + rise), (s, 0.0, 0.0), DP1_PLATE[spec.family])
    z_foot = cs2_asset.CRASHPAD["z1"] - 0.002              # on the pad rim top (M9a)
    z_top = A + DQ_DECK - DP1_CONDUIT["dq_top_below_deck"]
    cp, cinfo = conduit_parts(spec, z_foot, z_top)
    P += cp
    info["conduit"] = cinfo
    lx = DQ_LADDER_X - head_kit(spec)["mdr"]
    rung_half = 0.025 * math.cos(math.radians(45.0))
    z1 = A + DQ_DECK + 1.0                                   # the ladder rails' top (_dq_ladder)
    P += arrest_part(lx, rung_half, spec.ladder_pitch, z1)
    info["arrest"] = {"x_rung": lx, "z": (spec.ladder_pitch, z1)}
    if SKINNED and spec.comm_y is not None:
        P += comm_hanger_parts(spec, A + 1.809 + head_kit(spec)["rise"] - 0.117)      # the gantry beam's underside
    return P


# --------------------------------------------------------------------------------------
# Kagelok line-head ingest (towers-v2 section 6 with M1 / M7 / M11; skeptic claim 17)
# --------------------------------------------------------------------------------------

def _coords(obj):
    """(N, 3) float64 mesh-local vertex coordinates."""
    me = obj.data
    buf = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", buf)
    return buf.reshape(-1, 3).astype(np.float64)


def _set_coords(obj, co):
    obj.data.vertices.foreach_set("co", np.asarray(co, dtype=np.float32).ravel())
    obj.data.update()


def _untilt_matrix(axis_vec, pivot):
    """
    M2 (entry kit only, kept for the record): rotate the measured tube axis onto +Z with
    axis.rotation_difference(+Z) about `pivot` - no hand-picked sign. Not used by any
    shipped variant: ETDQ is rebuild-only (M3).
    """
    q = mathutils.Vector(axis_vec).normalized().rotation_difference(mathutils.Vector((0, 0, 1)))
    p = mathutils.Vector(pivot)
    return mathutils.Matrix.Translation(p) @ q.to_matrix().to_4x4() @ mathutils.Matrix.Translation(-p)


def ingest_gantry(spec, path):
    """
    The Kagelok LINE head kit: gantry island + its 4 end plates, onto our axle and gauge.
    Measures P0 from the source (end-sheave chord mid, gantry x center, tube y center),
    then: translate -P0 -> x0.30 -> reframe (+Y travel -> +X) -> shear_level on the GANTRY
    PART ONLY (M1) -> stretch_gauge -> +A -> clean -> smart-project UVMap. Raises
    TowerContractError on any section 6.2 assert (the caller ships rebuild then).
    Returns ([(obj, "paint")], report).
    """
    import cs2_ingest
    if not os.path.isfile(path):
        raise FileNotFoundError(f"Kagelok source not found: {path} (use --head rebuild)")
    names = KAG_LINE
    objs = cs2_ingest.load_external(path, objects=sorted(names.values()))
    parts = cs2_ingest.to_parts(objs, {names["gantry"]: "gantry_", names["plates"]: "endplate_",
                                       names["sheaves"]: "ksheave_", names["tube"]: "ktube_"})
    by = {}
    for o, _ in parts:
        by.setdefault(o.name.split("_")[0], []).append(o)

    def bbox(o):
        c = _coords(o)
        return c.min(axis=0), c.max(axis=0)

    # -- measure P0 in Kagelok units ------------------------------------------------------
    cents = []
    for o in by.get("ksheave", []):
        lo, hi = bbox(o)
        cents.append((lo + hi) / 2.0)
    if len(cents) != 16:
        raise TowerContractError(f"ingest: expected 16 line sheaves, got {len(cents)}")
    cents = np.array(cents)
    ends = []
    for side in (-1.0, 1.0):
        c = cents[np.sign(cents[:, 0]) == side]
        ends.append(c[np.argmin(c[:, 1])])
        ends.append(c[np.argmax(c[:, 1])])
    chord_z = float(np.mean([e[2] for e in ends]))
    tube = np.concatenate([_coords(o) for o in by.get("ktube", [])])
    tube_y = float((tube[:, 1].min() + tube[:, 1].max()) / 2.0)
    gantry = [o for o in by.get("gantry", [])
              if len(o.data.vertices) == KAG_GANTRY_VERTS and cs2_asset.tri_count(o) == KAG_GANTRY_TRIS]
    if len(gantry) != 1:
        raise TowerContractError(f"ingest: {len(gantry)} gantry islands with "
                                 f"{KAG_GANTRY_VERTS} v / {KAG_GANTRY_TRIS} tris (need exactly 1)")
    gantry = gantry[0]
    glo, ghi = bbox(gantry)
    p0 = np.array([(glo[0] + ghi[0]) / 2.0, tube_y, chord_z])
    plates = []
    for o in by.get("endplate", []):
        lo, hi = bbox(o)
        if abs((lo[1] + hi[1]) / 2.0 - tube_y) < 3.0:
            plates.append((float((lo[0] + hi[0]) / 2.0), o))
    if len(plates) != 4:
        raise TowerContractError(f"ingest: {len(plates)} end plates near the line tower (need 4)")
    plates.sort(key=lambda t: t[0])
    keep_names = {gantry.name} | {o.name for _x, o in plates}
    cs2_ingest.discard([o for o, _ in parts if o.name not in keep_names])
    # M11: rename after selection (they arrive as endplate_00/02/04/06)
    gantry.name = gantry.data.name = "gantry_00"
    for i, (_x, o) in enumerate(plates):
        o.name = o.data.name = f"endplate_{i}"
    keep = [gantry] + [o for _x, o in plates]
    for o in keep:                       # into the generator's collection (join + review)
        for c in list(o.users_collection):
            c.objects.unlink(o)
        bpy.context.collection.objects.link(o)
    for c in [c for c in bpy.data.collections if c.name.startswith(cs2_ingest.COLLECTION_PREFIX)]:
        if not c.objects and not c.children:
            bpy.data.collections.remove(c)

    # -- transform chain ----------------------------------------------------------------
    T = mathutils.Matrix.Translation(mathutils.Vector(-p0))
    for o in keep:
        o.data.transform(T)
        o.data.update()
    factor = cs2_ingest.rescale_by_reference(keep, 1.0, "Z", KAG_SCALE)
    cs2_ingest.reframe(keep, forward="+Y", up="+Z", pivot=None)
    # shear_level, gantry part ONLY (M1); end bands |x| within 0.05 m of each line end
    g = _coords(gantry)
    xmin, xmax = g[:, 0].min(), g[:, 0].max()
    lo_band = g[g[:, 0] <= xmin + 0.05]
    hi_band = g[g[:, 0] >= xmax - 0.05]
    a = lo_band[np.argmin(lo_band[:, 2])]
    b = hi_band[np.argmin(hi_band[:, 2])]
    k = float((b[2] - a[2]) / (b[0] - a[0]))
    if not (-0.24 < k < -0.21):
        raise TowerContractError(f"ingest: shear k {k:.4f} outside the measured -0.2233 +- 0.015")
    sel = np.abs(g[:, 0]) > 0.18
    g[sel, 2] -= k * g[sel, 0]
    _set_coords(gantry, g)
    # stretch_gauge (every kept part): |y| > 0.40 -> y += sign(y) * delta
    delta = spec.gauge / 2.0 - 2.25
    for o in keep:
        c = _coords(o)
        m = np.abs(c[:, 1]) > 0.40
        c[m, 1] += np.sign(c[m, 1]) * delta
        c[:, 2] += spec.axle
        _set_coords(o, c)
    girder_cut = None
    if SKINNED:
        # lift alignment Phase 2: the girder's end block (x +-0.09, z A + 0.22..0.43) reaches |y| 2.491, through the
        # yoke (2.404..2.476) the train now pitches in: its 4 end vertices a side move in to the skinned hanger end
        # (hanger_box y1, SKIN_DQ["gap"] short of the yoke), the main pin bridges the gap
        y_cut = hanger_box(spec)["y1"]
        g = _coords(gantry)
        m = ((np.abs(g[:, 1]) > y_cut) & (np.abs(g[:, 0]) < 0.12)
             & (g[:, 2] > spec.axle + 0.15) & (g[:, 2] < spec.axle + 0.50))
        _check(int(m.sum()) == 8, "ingest skinned: the girder end has 4 vertices a side past the yoke face",
               f"({int(m.sum())} at |y| {np.abs(g[m, 1]).min() if m.any() else 0:.3f}.. -> {y_cut})")
        girder_cut = {"from": round(float(np.abs(g[m, 1]).max()), 4), "to": y_cut}
        g[m, 1] = np.sign(g[m, 1]) * y_cut
        _set_coords(gantry, g)
    # detail pass 1 reclaim: the Kagelok beam is two half-shells welded at y 0 with an
    # INTERNAL partition (3 quads, 6 tris, 8 non-manifold edges) inside the beam - never seen
    beam_z = (spec.axle + 1.60, spec.axle + 2.00)
    n_part = _drop_faces_where(gantry, lambda nn, cc: abs(nn.y) > 0.99 and abs(cc.y) < 1e-4
                               and beam_z[0] < cc.z < beam_z[1])
    if n_part != 3:
        raise TowerContractError(f"ingest: expected the 3-quad internal beam partition at y 0, found {n_part}")
    cleans = {}
    for o in keep:
        cleans[o.name] = cs2_ingest.clean(o)
        me = o.data
        for lay in [l.name for l in me.uv_layers]:
            me.uv_layers.remove(me.uv_layers[lay])
        me.uv_layers.new(name="UVMap")
        me.materials.clear()
        for key in list(o.keys()):
            if key.startswith("cs2_src"):
                del o[key]
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.0)
        bpy.ops.object.mode_set(mode="OBJECT")
        o["relief"] = "geo"

    # -- section 6.2 asserts (+ M1 level plates, M7 H_top) ----------------------------------
    A = spec.axle
    g = _coords(gantry)
    pc = np.concatenate([_coords(o) for _x, o in plates])
    ngons = sum(1 for o in keep for p in o.data.polygons if len(p.vertices) > 4)
    _check(ngons == 0, "ingest zero n-gons", f"({ngons})")
    _check(abs(g[:, 1].max() - 2.922) < 0.01 and abs(g[:, 1].min() + 2.922) < 0.01,
           "ingest gantry Y extent +-2.922 +-0.01", f"({g[:, 1].min():.4f}..{g[:, 1].max():.4f})")
    _check(abs(pc[:, 1].max() - 2.993) < 0.01 and abs(pc[:, 1].min() + 2.993) < 0.01,
           "ingest end-plate Y extent +-2.993 +-0.01", f"({pc[:, 1].min():.4f}..{pc[:, 1].max():.4f})")
    top = pc[:, 2].max()
    _check(abs(top - A - H_TOP_INGEST) <= 0.002, "ingest end-plate top A + 1.9501 +-0.002 (M7)",
           f"(A + {top - A:.5f})")
    tops = []
    for _x, o in plates:
        c = _coords(o)
        t = c[c[:, 2] > c[:, 2].max() - 0.01, 2]
        tops.append((t.min(), t.max()))
    spread = max(t[1] for t in tops) - min(t[0] for t in tops)
    _check(spread <= 1e-4, "ingest end-plate tops level (M1, dz <= 1e-4)", f"(dz {spread:.2e})")
    _check(abs(g[:, 2].max() - A - 1.926) <= 0.005, "ingest beam top A + 1.926 +-0.005",
           f"(A + {g[:, 2].max() - A:.4f})")
    feet = [g[g[:, 0] <= g[:, 0].min() + 0.05, 2].min(), g[g[:, 0] >= g[:, 0].max() - 0.05, 2].min()]
    _check(all(abs(f - (A + DQ_DECK)) <= 0.01 for f in feet),
           "ingest brace feet level at A - 0.296 +-0.01", f"(A {feet[0] - A:+.4f} / {feet[1] - A:+.4f})")
    centre = g[(np.abs(g[:, 1]) < 0.13) & (np.abs(g[:, 0]) < 0.10)]
    sad_lo = centre[:, 2].min() if len(centre) else float("nan")
    _check(len(centre) > 0 and sad_lo < spec.tube_top, "ingest saddle bottom inside the tube",
           f"(A {sad_lo - A:+.4f} < TT A + {DQ_TT})")
    rep = {"source": os.path.basename(path), "uid": KAGELOK_UID, "kit": "line (Plateforme Pil Haut + Pil Deco)",
           "p0_u": [round(float(v), 4) for v in p0], "scale_m_per_u": factor,
           "shear_k": round(k, 5), "shear_deg": round(math.degrees(math.atan(-k)), 3),
           "gauge_delta_m": round(delta, 4),
           "kept": {o.name: {"verts": len(o.data.vertices), "tris": cs2_asset.tri_count(o)} for o in keep},
           "h_top_m": round(float(top - A), 5), "beam_top_m": round(float(g[:, 2].max() - A), 5),
           "feet_m": [round(float(f - A), 5) for f in feet], "clean": cleans,
           "internal_partition_quads_dropped": n_part}
    if girder_cut is not None:
        rep["skinned_girder_end_y"] = girder_cut
    print(f"[tower2] ingest: P0 {rep['p0_u']} u, shear k {k:.5f}, delta {delta:.3f} m, "
          f"H_top {rep['h_top_m']:.5f} m, kept {rep['kept']}")
    return [(o, "paint") for o in keep], rep


# --------------------------------------------------------------------------------------
# Assembly
# --------------------------------------------------------------------------------------

def _apply_transforms(parts):
    """Bake every part's object transform into its mesh (identity objects, fresh matrices)."""
    for o, _k in parts:
        mb = o.matrix_basis.copy()
        if mb != mathutils.Matrix.Identity(4):
            o.data.transform(mb)
            o.matrix_basis = mathutils.Matrix.Identity(4)
            o.data.update()
    bpy.context.view_layer.update()


def _bevel(parts, table):
    """cs2_sheaves.bevel_parts (this function's body, moved): BEVEL_W bevel by the longest matching prefix."""
    S.bevel_parts(parts, table, width=BEVEL_W)


def lod_prefixes(spec):
    return (FG_LOD1, FG_LOD2) if spec.family == "FG" else (DQ_LOD1, DQ_LOD2)


def check_c10(parts, spec):
    """C10: one UV layer "UVMap" per part; every part named by a known prefix."""
    l1, l2 = lod_prefixes(spec)
    keep = FG_KEEP if spec.family == "FG" else DQ_KEEP
    known = tuple(density_table(spec)) + l1 + l2 + keep
    bad_uv = [o.name for o, _ in parts if [l.name for l in o.data.uv_layers] != ["UVMap"]]
    _check(not bad_uv, "C10 one UV layer 'UVMap' per part", f"({len(parts)} parts) {bad_uv[:6]}")
    unknown = [o.name for o, _ in parts if not o.name.startswith(known)]
    _check(not unknown, "C10 every part prefix in DENSITY / LOD / keep tables", f"{unknown[:6]}")
    names = [o.name for o, _ in parts]
    _check(len(set(names)) == len(names), "C10 part names unique")


def build_tower_v2(spec, head=None, finish=True, kagelok=None, name=None):
    """
    Build one v2 tower in the scene: parts -> bevel -> C10 -> set_density -> tag_parts ->
    (finish: material_sources) -> join -> remove_doubles(0.0005) -> origin -> store_part_table
    -> shade_by_angle(66, weighted) -> C1-C17 (authoring frame) -> orient_for_net -> C1-C5
    (net frame). Returns (obj, table, sources | None, info).
    """
    head = head or ("yan" if spec.family == "FG" else spec.heads[0])
    if head not in spec.heads:
        raise ValueError(f"{spec.key}: --head {head!r} not allowed (heads {spec.heads})")
    spec = skin_spec(spec)            # SKINNED GD: SKIN_GD sheaves a side (info["spec"] hands it back)
    if SKINNED and (head not in SKIN_HEADS.get(spec.family, ()) or spec.kind != "support" or not S.wheels_v2()):
        raise TowerContractError(f"{spec.key}: no skinned recipe for the {spec.family} {head} {spec.kind} head "
                                 f"(SKIN_HEADS {SKIN_HEADS}, support heads, v2 wheels)")
    if spec.family == "FG":
        parts, info = fg_parts(spec)
        bevel = FG_BEVEL
        if SKINNED:      # the half trains' lap plates (rocker_main_<side>_f|b): SKIN_PLATE_BEVEL segments
            bevel = dict(FG_BEVEL, **{f"rocker_main_{tg}_": SKIN_PLATE_BEVEL["FG"] for tg in ("n", "p")})
    else:
        parts, info = dq_parts(spec, head, kagelok)
        bevel = DQ_BEVEL
    info["head"] = head
    _bevel(parts, bevel)
    _apply_transforms(parts)
    info["detail_post"] = detail_post(parts, spec, info)
    check_c10(parts, spec)
    info["density_applied"] = cs2_uv.set_density(parts, density_table(spec))
    info["part_material"] = {o.name: k for o, k in parts}
    table = cs2_asset.tag_parts(parts)
    sources = None
    if finish:
        import cs2_bake
        bpy.context.view_layer.update()
        # T62 round 2: + the one wheel material (cs2_sheaves.WHEEL_MATERIAL) under v2; v1 = TOWER_MATERIALS as it was
        sources = cs2_bake.material_sources({**TOWER_MATERIALS, **S.wheel_materials(dict)}, parts)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _k in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    t = bpy.context.active_object
    nm = name or spec.name_stem + V2_LETTER
    t.name = nm
    t.data.name = nm
    bm = bmesh.new()
    bm.from_mesh(t.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0005)
    bm.to_mesh(t.data)
    bm.free()
    t.data.update()
    if t.matrix_basis != mathutils.Matrix.Identity(4):
        t.data.transform(t.matrix_basis)
        t.matrix_basis = mathutils.Matrix.Identity(4)
    cs2_asset.store_part_table(t, table)
    cs2_asset.shade_by_angle(t, SHADE_ANGLE_V2, weighted=True)
    info["contracts_authoring"] = assert_tower_contracts(t, spec, "authoring", head)
    cs2_asset.orient_for_net(t)
    info["contracts_net"] = assert_tower_contracts(t, spec, "net", head)
    info["parts"] = len(table)
    if SKINNED:
        info["spec"] = spec
        info["skin_part_bone"] = skin_part_bones(t, table, spec)     # the halves by travel side (+ the axis test)
        info["skin"] = skin_sweep(t, table, spec, info["skin_part_bone"])   # G6 (raises on a failed gate)
    return t, table, sources, info


# --------------------------------------------------------------------------------------
# Geometry contracts (section 7, + the skeptic's C13-C17). Any failure raises.
# --------------------------------------------------------------------------------------

def part_vertices(obj, table):
    """{part name: (N, 3) float64 coords of the vertices its faces use}."""
    me = obj.data
    npoly = len(me.polygons)
    part = np.zeros(npoly, dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    start = np.empty(npoly, dtype=np.int32)
    total = np.empty(npoly, dtype=np.int32)
    me.polygons.foreach_get("loop_start", start)
    me.polygons.foreach_get("loop_total", total)
    lv = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", lv)
    poly_of_loop = np.repeat(np.arange(npoly), total)
    order = np.concatenate([np.arange(s, s + c) for s, c in zip(start, total)]) if npoly else lv
    co = _coords(obj)
    out = {}
    pl = part[poly_of_loop]
    vl = lv[order]
    for p in sorted(set(int(x) for x in part)):
        idx = np.unique(vl[pl == p])
        out[table[p - 1]] = co[idx]
    return out


def _frame_axes(frame):
    """(travel index, travel sign, gauge index): authoring X/Y; net = orient_for_net's -Y/X."""
    return (0, 1.0, 1) if frame == "authoring" else (1, -1.0, 0)


def sheave_centres(pv, frame):
    """[(name, travel, gauge, z)] of every sheave rim (bbox center: the rims are symmetric)."""
    ti, ts, gi = _frame_axes(frame)
    out = []
    for nm in sorted(pv):
        if nm.startswith("sheave_") and nm.endswith("_rim"):
            c = (pv[nm].min(axis=0) + pv[nm].max(axis=0)) / 2.0
            out.append((nm, ts * float(c[ti]), float(c[gi]), float(c[2])))
    return out


def _part_bvh(obj, table, test):
    """(BVHTree, [part name per polygon], (N, 3) vertex coords) of the faces whose part name
    passes `test` - mesh-local coordinates (the joined tower is identity-transformed)."""
    from mathutils.bvhtree import BVHTree
    me = obj.data
    part = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    co = _coords(obj)
    polys, names, used = [], [], set()
    for pl, q in zip(me.polygons, part):
        nm = table[q - 1]
        if test(nm):
            vs = tuple(pl.vertices)
            polys.append(vs)
            names.append(nm)
            used.update(vs)
    if not polys:
        return None, [], np.zeros((0, 3))
    return BVHTree.FromPolygons([tuple(v) for v in co], polys, epsilon=0.0), names, \
        co[np.array(sorted(used), dtype=np.int64)]


def check_hanger_column(obj, table, spec, frame="authoring"):
    """C18 on one gantry-head tower mesh (authoring frame: X travel, Y gauge). Returns
    {"column": [(lane sign, part)], "gantry": [(part, gantry part)], "box_z": (z0, z1)}; empty
    lists = clear. The column boxes come from hanger_columns(spec) (DQ: one box, the station's
    C8 column; GD: the gd8 hanger + body boxes); "box_z" is the first box. Used by
    assert_tower_contracts and tests/test_tower_contracts.py."""
    from mathutils.bvhtree import BVHTree
    ti, _ts, gi = _frame_axes(frame)
    lane, A, sag = spec.gauge / 2.0, lane_y(spec), abs(spec.sag)      # T27: the rope height (= axle unless ROPE_ON_CROWN)
    cols = hanger_columns(spec)
    deckish = ("grate_", "post_", "rail_", "kick_")
    bvh, names, _v = _part_bvh(obj, table, lambda n: n.startswith(deckish))
    pv = {n: v for n, v in part_vertices(obj, table).items() if n.startswith(deckish)}
    co = _coords(obj)
    xt = float(np.abs(co[:, ti]).max()) + 0.5
    eps = 0.002
    zb = (A + cols[0]["z0"] - sag, A + cols[0]["z1"] + sag)
    out = {"column": [], "gantry": [], "box_z": (round(zb[0], 4), round(zb[1], 4))}
    if bvh is None:
        return out
    for col in cols:
        z0, z1 = A + col["z0"] - sag, A + col["z1"] + sag
        for s in (-1.0, 1.0):
            g0, g1 = s * lane - col["half"] + eps, s * lane + col["half"] - eps
            corners = []
            for zz in (z0 + eps, z1 - eps):
                for tt, gg in ((-xt, g0), (xt, g0), (xt, g1), (-xt, g1)):
                    c = [0.0, 0.0, zz]
                    c[ti], c[gi] = tt, gg
                    corners.append(tuple(c))
            quads = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
            box = BVHTree.FromPolygons(corners, quads, epsilon=0.0)
            hit = {names[i] for i, _j in bvh.overlap(box)}          # faces crossing the box
            for n, v in pv.items():                                    # parts wholly inside it
                if ((v[:, gi] > g0) & (v[:, gi] < g1) & (v[:, 2] > z0 + eps)
                        & (v[:, 2] < z1 - eps)).any():
                    hit.add(n)
            out["column"] += [(int(s), h) for h in sorted(hit) if (int(s), h) not in out["column"]]
    rails, rnames, _rv = _part_bvh(obj, table, lambda n: n.startswith(("post_", "rail_", "kick_")))
    gant, gnames, _gv = _part_bvh(obj, table, lambda n: n.startswith("gantry_"))
    if rails is not None and gant is not None:
        out["gantry"] = sorted({(rnames[i], gnames[j]) for i, j in rails.overlap(gant)})
    return out


def _pad_facet_radii(pts, radius=None):
    """(inner, outer) radius of the FACETED crash-pad walls (crashpad_parts: `segments` flat
    facets between the arc ends) along each point's azimuth (authoring frame: the open sector
    is on -X); NaN outside the arc. A facet at azimuth th sits at r_v cos(step/2) / cos(th - mid).
    `radius` = the shell's outer radius (default cs2_asset.CRASHPAD; GD: pad_radius(spec))."""
    cp = cs2_asset.CRASHPAD
    R, t, segs = (cp["radius"] if radius is None else radius), cp["thickness"], cp["segments"]
    half = math.radians(cp["arc_deg"]) / 2.0
    step = 2.0 * half / segs
    th = np.arctan2(pts[:, 1], pts[:, 0])
    k = np.clip(np.floor((th + half) / step), 0, segs - 1)
    f = math.cos(step / 2.0) / np.cos(th - (-half + (k + 0.5) * step))
    arc = np.abs(th) <= half + 1e-9
    return np.where(arc, (R - t) * f, np.nan), np.where(arc, R * f, np.nan)


def check_pad_flange(obj, table, spec):
    """
    C14 data on one tower mesh (AUTHORING frame, or any frame rotated about Z only for the
    triangle test): `tri_hits` = sorted {(flange/bolt part, pad part)} whose triangles cross
    (BVH overlap); `ring_inside` / `facet_inside` = flange/bolt vertices inside the ideal ring
    (r 0.50-0.56) / the faceted shell over the pad's real z range; `clear_mm` = how far the
    nearest in-arc flange/bolt vertex sits inside the inner facets; `pad_z0`, `plate_top`.
    Used by assert_tower_contracts and tests/test_tower_contracts.py.
    """
    cpd = dict(cs2_asset.CRASHPAD, radius=pad_radius(spec))
    pv = part_vertices(obj, table)
    fbn = ("flange_mastbase", "bolt_mastbase")
    cp = np.concatenate([pv["crashpad"], pv["crashpad_outer"]])
    pz0, pz1 = float(cp[:, 2].min()), float(cp[:, 2].max())
    fb = np.concatenate([v for n, v in pv.items() if n.startswith(fbn)])
    r = np.hypot(fb[:, 0], fb[:, 1])
    ang = np.degrees(np.abs(np.arctan2(fb[:, 1], fb[:, 0])))
    zin = (fb[:, 2] >= pz0 - 1e-6) & (fb[:, 2] <= pz1 + 1e-6)
    ring = ((r > cpd["radius"] - cpd["thickness"] - 1e-6) & (r < cpd["radius"] + 1e-6) & zin
            & (ang <= cpd["arc_deg"] / 2.0 + 1e-6))
    r_in, r_out = _pad_facet_radii(fb, cpd["radius"])
    arc = ~np.isnan(r_in)
    facet = np.zeros(len(fb), dtype=bool)
    facet[arc] = (r[arc] > r_in[arc] - 1e-6) & (r[arc] < r_out[arc] + 1e-6) & zin[arc]
    hole = arc & zin & ~facet
    clear = float((r_in[hole] - r[hole]).min()) if hole.any() else float("inf")
    a, an, _a = _part_bvh(obj, table, lambda n: n.startswith(fbn))
    b, bn, _b = _part_bvh(obj, table, lambda n: n.startswith("crashpad"))
    hits = sorted({(an[i], bn[j]) for i, j in a.overlap(b)}) if a is not None and b is not None else []
    return {"tri_hits": hits, "ring_inside": int(ring.sum()), "facet_inside": int(facet.sum()),
            "clear_mm": round(clear * 1000.0, 2), "pad_z0": round(pz0, 6),
            "plate_top": round(float(pv["flange_mastbase"][:, 2].max()), 6),
            "max_r_in": float(r[r < cpd["radius"] - cpd["thickness"]].max())
            if (r < cpd["radius"] - cpd["thickness"]).any() else 0.0}


def _box_tree(lo, hi):
    """BVH of the closed axis-aligned box lo..hi (authoring frame)."""
    from mathutils.bvhtree import BVHTree
    (x0, y0, z0), (x1, y1, z1) = lo, hi
    corners = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
               (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    quads = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return BVHTree.FromPolygons(corners, quads, epsilon=0.0)


def _zone_hits(bvh, names, pv, lo, hi, allowed=()):
    """Parts (not starting with `allowed`) whose faces cross the box lo..hi or that have a
    vertex strictly inside it (authoring frame)."""
    hit = {names[i] for i, _j in bvh.overlap(_box_tree(lo, hi))}
    lo_a, hi_a = np.array(lo), np.array(hi)
    for n, v in pv.items():
        if ((v > lo_a) & (v < hi_a)).all(axis=1).any():
            hit.add(n)
    return sorted(h for h in hit if not h.startswith(allowed))


def check_gd_clearances(obj, table, spec):
    """
    GD contracts on one tower mesh (AUTHORING frame, before orient_for_net):
    C19 comm: the LV comm wire (lateral 0, z = comm_y, the whole travel extent; vanilla LV mesh
        r 0.06 + 0.04) crosses no triangle - it runs through the gantry window between the
        tube top / girders (A + 0.50) and the beam (A + 1.742).
    C20 gondola carrier (the hanger OUTBOARD, facing travel): every GD_CARRIER box swept along
        the travel extent on both lanes, shrunk GD_SWEEP_TOL, crosses no tower triangle except
        its `allowed` parts (the jaw over the sheaves), and the cabins.md 8.3 head rule (below
        the rope only sheaves at dy > -0.06) holds.
    Returns {"comm": [parts], "carrier": [(lane, box, part)], "comm_window": (lo, hi)}.
    """
    from mathutils.bvhtree import BVHTree
    co = _coords(obj)
    me = obj.data
    part = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    polys = [tuple(p.vertices) for p in me.polygons]
    names = [table[q - 1] for q in part]
    bvh = BVHTree.FromPolygons([tuple(v) for v in co], polys, epsilon=0.0)
    pv = part_vertices(obj, table)
    xt = float(np.abs(co[:, 0]).max()) + 0.5
    A, lane, tol = spec.axle, spec.gauge / 2.0, GD_SWEEP_TOL
    out = {"comm": [], "carrier": [], "comm_window": (float("nan"), float("nan"))}
    if spec.comm_y is not None:                  # ETGD (hosted): the station owns the comm anchor
        cz, cr = spec.comm_y, 0.10
        # the family round's hanger and clamp (comm_hanger_parts) hold the wire: theirs are the only hits allowed
        out["comm"] = _zone_hits(bvh, names, pv, (-xt, -cr, cz - cr), (xt, cr, cz + cr), allowed=("comm_",))
        # the gantry window the comm wire passes at the portal (X 0, Y 0): first structure below /
        # above it (ray casts), relative to the axle
        from mathutils import Vector
        dn = bvh.ray_cast(Vector((0.0, 0.0, cz)), Vector((0.0, 0.0, -1.0)))
        up = bvh.ray_cast(Vector((0.0, 0.0, cz)), Vector((0.0, 0.0, 1.0)))
        out["comm_window"] = (round(dn[0].z - A, 4) if dn[0] is not None else float("nan"),
                              round(up[0].z - A, 4) if up[0] is not None else float("nan"))
    zones = [(n, dy, dz, al) for n, _x, dy, dz, al in GD_CARRIER] + list(GD_HEAD_RULE)
    Ar = lane_y(spec)                            # T27: the carrier rides the rope (= the axle unless ROPE_ON_CROWN)
    for s in (-1.0, 1.0):
        for nm, (dy0, dy1), (dz0, dz1), allowed in zones:
            # detail pass 1: a sheave's rope liner is part of the sheave (lite_sheave_parts)
            allowed = tuple(allowed) + (("liner_",) if "sheave_" in allowed else ())
            ys = sorted((s * (lane + dy0), s * (lane + dy1)))
            lo = (-xt, ys[0] + tol, Ar + dz0 + tol)
            hi = (xt, ys[1] - tol, Ar + dz1 - tol)
            out["carrier"] += [(int(s), nm, h) for h in _zone_hits(bvh, names, pv, lo, hi, allowed)]
    return out


def check_sub_hangers(pv, spec, frame="authoring"):
    """C21 (Revision 1c, revision1b-check-gd-towers section B): every sub-rocker HANGS. For
    each sub pivot pin `pivot_<side>_s<j>` (part AABBs; lateral as |gauge|):
      - its OUTBOARD end runs through its own `rocker_sub_<side>_<j>` and its INBOARD end
        through a `rocker_main_<side>_*` or `rocker_link_<side>_*` lug: the pin's cross-section
        (+- r in travel and height) inside the lug, the pin overlapping >= C21_THROUGH of the
        lug's thickness, its end <= C21_POKE proud of the lug;
      - that inboard lug hangs from `yoke_<side>`: a link reaches >= C21_BURY into the yoke
        (inside its travel span, lateral overlap >= C21_THROUGH); a main by its own pivot pin
        `pivot_<side>_m<m>` overlapping both the main's top and the yoke's bottom >= C21_BRIDGE.
    Raises TowerContractError naming the first unhung pin; returns a summary string."""
    ti, _ts, gi = _frame_axes(frame)

    def box(n):
        v = pv[n]
        lo, hi = v.min(axis=0), v.max(axis=0)
        g = sorted((abs(float(lo[gi])), abs(float(hi[gi]))))
        return {"t": (float(lo[ti]), float(hi[ti])), "g": tuple(g), "z": (float(lo[2]), float(hi[2]))}

    def through(pin, lug, end):
        """The pin end `end` ("out" | "in") runs through `lug`: (ok, detail)."""
        tc, zc = sum(pin["t"]) / 2.0, sum(pin["z"]) / 2.0
        r = max(pin["t"][1] - pin["t"][0], pin["z"][1] - pin["z"][0]) / 2.0
        if not (lug["t"][0] <= tc - r + 1e-4 and lug["t"][1] >= tc + r - 1e-4
                and lug["z"][0] <= zc - r + 1e-4 and lug["z"][1] >= zc + r - 1e-4):
            return False, "pin cross-section outside the lug"
        thick = lug["g"][1] - lug["g"][0]
        over = min(pin["g"][1], lug["g"][1]) - max(pin["g"][0], lug["g"][0])
        if over < C21_THROUGH * thick:
            return False, f"pin covers {max(over, 0.0) * 1000:.0f} of {thick * 1000:.0f} mm"
        poke = pin["g"][1] - lug["g"][1] if end == "out" else lug["g"][0] - pin["g"][0]
        if poke > C21_POKE:
            return False, f"pin end {poke * 1000:.0f} mm proud"
        return True, ""

    def fail(label, detail):
        raise TowerContractError(f"[{spec.key}] C21 {label} FAILED {detail}")

    def yoke_of(tag, lug):
        """The yoke a lug hangs from: `yoke_<tag>`, or (the skinned half trains, lift alignment phase 2 round 2) the
        half plate `yoke_<tag>_f|b` on the lug's travel side, its lateral band the two lapping plates together (a
        main / link spans the whole yoke slab)."""
        if f"yoke_{tag}" in pv:
            return box(f"yoke_{tag}")
        plates = [box(f"yoke_{tag}_{h}") for h in ("f", "b")]
        lc = sum(lug["t"]) / 2.0
        mine = next(p for p in plates if (sum(p["t"]) / 2.0) * lc > 0.0)
        return dict(mine, g=(min(p["g"][0] for p in plates), max(p["g"][1] for p in plates)))

    out = {"pins": 0, "main": 0, "link": 0, "bury": [], "poke": []}
    # the skinned half trains pair each half's wheels on its own: an odd count a half adds a 1-wheel sub (GD 10 a side)
    want = 2 * math.ceil(spec.sheaves / 4) if SKINNED else spec.sheaves // 2
    for tag in ("n", "p"):
        pins = sorted(n for n in pv if n.startswith(f"pivot_{tag}_s"))
        subs = sorted(n for n in pv if n.startswith(f"rocker_sub_{tag}_"))
        if len(pins) != len(subs) or len(subs) != want:
            fail(f"one pivot pin per sub-rocker ({tag})",
                 f"({len(pins)} pins, {len(subs)} subs, want {want})")
        lugs = sorted(n for n in pv if n.startswith((f"rocker_main_{tag}_", f"rocker_link_{tag}_")))
        for pn in pins:
            j = pn[len(f"pivot_{tag}_s"):]
            pin = box(pn)
            sub = box(f"rocker_sub_{tag}_{j}")
            ok, why = through(pin, sub, "out")
            if not ok:
                fail(f"{pn} outboard end through rocker_sub_{tag}_{j}",
                     f"({why}; pin |y| {pin['g'][0]:.3f}..{pin['g'][1]:.3f})")
            hit = [n for n in lugs if through(pin, box(n), "in")[0]]
            if len(hit) != 1:
                fail(f"{pn} inboard end through exactly one main / link lug",
                     f"(lugs {hit}; pin |y| {pin['g'][0]:.3f}..{pin['g'][1]:.3f}, travel "
                     f"{pin['t'][0]:+.3f}..{pin['t'][1]:+.3f}, z {pin['z'][0]:.3f}..{pin['z'][1]:.3f}: "
                     f"{'the inboard end is in air' if not hit else 'ambiguous'})")
            ln, lug = hit[0], box(hit[0])
            yoke = yoke_of(tag, lug)
            out["poke"] += [pin["g"][1] - sub["g"][1], lug["g"][0] - pin["g"][0]]
            if ln.startswith("rocker_link_"):
                bury = lug["z"][1] - yoke["z"][0]
                lat = min(lug["g"][1], yoke["g"][1]) - max(lug["g"][0], yoke["g"][0])
                if not (bury >= C21_BURY and lug["z"][0] < yoke["z"][0]
                        and lat >= C21_THROUGH * (lug["g"][1] - lug["g"][0])
                        and yoke["t"][0] <= lug["t"][0] and lug["t"][1] <= yoke["t"][1]):
                    fail(f"{ln} hangs from yoke_{tag} (buried >= {C21_BURY * 1000:.0f} mm)",
                         f"(link top {lug['z'][1]:.3f} vs yoke bottom {yoke['z'][0]:.3f}: "
                         f"{bury * 1000:+.1f} mm; lateral overlap {lat * 1000:.0f} mm)")
                out["link"] += 1
                out["bury"].append(bury)
            else:
                m = ln.split("_")[-1]
                mp = box(f"pivot_{tag}_m{m}")
                ok = (mp["z"][0] <= lug["z"][1] - C21_BRIDGE and mp["z"][1] >= yoke["z"][0] + C21_BRIDGE
                      and all(b["t"][0] <= mp["t"][0] and mp["t"][1] <= b["t"][1] for b in (lug, yoke))
                      and all(min(mp["g"][1], b["g"][1]) - max(mp["g"][0], b["g"][0])
                              >= C21_THROUGH * (b["g"][1] - b["g"][0]) for b in (lug, yoke)))
                if not ok:
                    fail(f"{ln} hangs from yoke_{tag} on pivot_{tag}_m{m}",
                         f"(pin z {mp['z'][0]:.3f}..{mp['z'][1]:.3f}, main top {lug['z'][1]:.3f}, "
                         f"yoke bottom {yoke['z'][0]:.3f})")
                out["main"] += 1
            out["pins"] += 1
    txt = (f"{out['pins']} sub pivots each through a lug at both ends (pin ends <= "
           f"{max(out['poke']) * 1000:.0f} mm proud; {out['main']} on mains, {out['link']} on links"
           + (f" buried {min(out['bury']) * 1000:.0f} mm in the yoke" if out["bury"] else "")
           + "); every lug hangs from its yoke")
    print(f"[tower2] ok [{spec.key}] C21 {txt}")
    return txt


def assert_tower_contracts(obj, spec, frame, head):
    """C1-C18 on the joined LOD0 (frame "authoring" before orient_for_net, "net" after);
    GD adds C19 (comm wire) and C20 (gondola carrier sweep + head rule); every gantry head
    C21 (Revision 1c: each sub-rocker pin through a lug at both ends, hung from the yoke)."""
    table = json.loads(obj.data[cs2_asset.PART_TABLE_PROP])
    pv = part_vertices(obj, table)
    co = _coords(obj)
    ti, ts, gi = _frame_axes(frame)
    A = spec.axle
    res = {}
    tag = f"[{spec.key} {frame}]"
    full = frame == "authoring"

    # C1 axle: the two center sheaves of each side
    sc = sheave_centres(pv, frame)
    _check(len(sc) == 2 * spec.sheaves, f"{tag} sheave count", f"({len(sc)} = 2 x {spec.sheaves})")
    tol1 = 1e-4 if spec.sag == 0.0 else 2e-3
    for side in (-1.0, 1.0):
        mine = sorted([c for c in sc if np.sign(c[2]) == side], key=lambda c: abs(c[1]))[:2]
        mz = sum(c[3] for c in mine) / 2.0
        _check(abs(mz - A) <= tol1, f"{tag} C1 center-pair axle side {side:+.0f} = {A}",
               f"({mz:.5f}, tol {tol1})")
    if spec.family == "FG":
        _check(abs(A - (spec.tube_top - 0.96)) < 1e-9, f"{tag} C1 FG axle = mast - 0.96",
               f"({A} = {spec.tube_top} - 0.96)")
    res["axle"] = A
    # C2 lanes
    worst = max(abs(abs(c[2]) - spec.gauge / 2.0) for c in sc)
    _check(worst <= 1e-4, f"{tag} C2 every sheave |gauge| = {spec.gauge / 2.0}", f"(worst {worst:.2e})")
    res["lane_x"] = spec.gauge / 2.0
    # C3 / C4 bounds
    zmax, zmin = float(co[:, 2].max()), float(co[:, 2].min())
    h_want = H_TOP_INGEST if head == "ingest" else H_TOP[spec.family]
    _check(abs((zmax - A) - h_want) <= H_TOP_TOL[spec.family],
           f"{tag} C3 bounds top - axle = H_top {h_want}", f"({zmax - A:.5f}; top {zmax:.4f})")
    _check(abs(zmax - spec.top) <= H_TOP_TOL[spec.family] + 2e-4,
           f"{tag} C3 bounds top = spec top {spec.top}", f"({zmax:.4f})")
    _check(abs(zmin + spec.pier) <= 1e-4, f"{tag} C4 bounds min = -pier {spec.pier}", f"({zmin:.5f})")
    res["top"] = zmax
    res["min"] = zmin
    # C5 mast center, pad top
    m = pv["mast"]
    mc = (m.min(axis=0) + m.max(axis=0)) / 2.0
    _check(abs(mc[0]) <= 1e-4 and abs(mc[1]) <= 1e-4, f"{tag} C5 mast centered on the origin",
           f"({mc[0]:+.1e}, {mc[1]:+.1e})")
    _check(abs(pv["pad"][:, 2].max() - 0.15) <= 1e-4, f"{tag} C5 pad top 0.15",
           f"({pv['pad'][:, 2].max():.5f})")
    # lateral / travel extents (post-orient lateral = gauge axis)
    res["lateral"] = float(np.abs(co[:, gi]).max())
    res["travel"] = float(np.abs(co[:, ti]).max())
    if not full:
        return res

    if spec.family == "FG":
        arm_top = spec.tube_top + FG_ARM_T / 2.0
        rt = pv["rail_catwalk_top"][:, 2].max()
        _check(abs(rt - (arm_top + FG_TOP_ABOVE_ARM)) <= 1e-4 and abs(rt - zmax) <= 1e-6,
               f"{tag} C6 rail_catwalk_top = arm_top + 0.904 = bounds top", f"({rt:.5f})")
        _check(pv["liftbar"][:, 2].max() < rt, f"{tag} C6 liftbar below the top rail",
               f"({pv['liftbar'][:, 2].max():.4f} < {rt:.4f})")
        lr = np.concatenate([v for n, v in pv.items() if n.startswith("ladder_rail_")])
        _check(np.abs(lr[:, gi]).max() <= 0.245 + 1e-6, f"{tag} C7 ladder rails |y| <= 0.245",
               f"({np.abs(lr[:, gi]).max():.4f})")
        # T27: with the tree inboard (FG_CROWN_INBOARD) the pivot pins' outer ends (lane + 0.11 = 1.91) move in and the
        # outboard axle caps (lane + 0.065 + 0.02 = 1.885) set the lateral extent; T62 round 2 v2: the wheels' outboard
        # hub bosses (lane + w / 2 + 0.10 r = 1.885), the same number
        lat_ship = 1.885 if ROPE_ON_CROWN else 1.91
        # lift alignment phase 2 round 2: the half trains' bogies sit SKIN_HALF_SHIFT further out along travel
        trav = 0.97 + (SKIN_HALF_SHIFT["FG"] if SKINNED else 0.0)
        _check(abs(res["lateral"] - lat_ship) <= 0.01 and abs(res["travel"] - trav) <= 0.01,
               f"{tag} FG lateral / travel extents +-1.91 / +-{trav:.2f} (shipped N)",
               f"({res['lateral']:.4f} / {res['travel']:.4f})")
    # C8 crash pad clearance
    cp = np.concatenate([pv["crashpad"], pv["crashpad_outer"]])
    r_in = float(np.hypot(cp[:, 0], cp[:, 1]).min())
    _check(r_in >= spec.mast_r + 0.03 - 1e-6, f"{tag} C8 crash pad inner r >= mast r + 0.03",
           f"({r_in:.4f} >= {spec.mast_r + 0.03:.2f})")
    # C14 (M8 + review F3): the pad sits ON the base flange. Triangle test first: the plate has
    # no vertex in the shell, so the M8 vertex test alone passed while the plate cut the shell.
    pf = check_pad_flange(obj, table, spec)
    _check(not pf["tri_hits"], f"{tag} C14 no flange/bolt triangle crosses a crash-pad triangle (F3)",
           f"({len(pf['tri_hits'])} part pairs {pf['tri_hits'][:3]})")
    _check(not pf["ring_inside"] and not pf["facet_inside"],
           f"{tag} C14 no flange/bolt vertex inside the pad shell (M8; ideal ring + 14 facets)",
           f"({pf['ring_inside']} / {pf['facet_inside']} inside; nearest bolt/flange vertex "
           f"{pf['clear_mm']:.1f} mm inside the inner facets; bolt/flange r {pf['max_r_in']:.4f})")
    _check(abs(pf["pad_z0"] - crashpad_z0(spec)) <= 1e-5 and 0.0 < pf["pad_z0"] - pf["plate_top"] <= 0.01,
           f"{tag} C14 crash pad bottom = flange plate top + {PAD_FLANGE_GAP} (F3)",
           f"(pad z0 {pf['pad_z0']:.4f}, plate top {pf['plate_top']:.4f})")
    res["pad_flange"] = {k: pf[k] for k in ("pad_z0", "plate_top", "clear_mm")}

    if spec.family in GANTRY_FAMILIES:
        # C9 (F9): no carrier / post / rail vertex inside a sheave rim cylinder
        guard = ("rocker_", "yoke_", "pivot_", "hanger_", "post_", "rail_", "kick_", "gantry_")
        pts = np.concatenate([v for n, v in pv.items() if n.startswith(guard)])
        hits = 0
        min_r = 9.0
        for nm, tr, gg, zz in sc:
            ax = np.abs(pts[:, gi] - gg) < spec.sheave_w / 2.0 + 1e-3
            rad = np.hypot(pts[:, ti] * ts - tr, pts[:, 2] - zz)
            hit = ax & (rad < spec.sheave_r + 0.005)
            hits += int(hit.sum())
            if ax.any():
                min_r = min(min_r, float(rad[ax].min()))
        _check(hits == 0, f"{tag} C9 no carrier/post/rail vertex in a sheave rim cylinder (strict, 1e-3)",
               f"({hits} hits; nearest radial in the axial band {min_r:.4f} vs {spec.sheave_r + 0.005:.3f})")
        sub = np.concatenate([v for n, v in pv.items() if n.startswith("rocker_sub_")])
        gap = spec.gauge / 2.0 - spec.sheave_w / 2.0 - float(np.abs(sub[:, gi]).max())
        _check(gap >= 1e-3, f"{tag} C9 rocker_sub outer face clear of the sheave face (F9)", f"({gap * 1000:.1f} mm)")
        # C21 (Revision 1c): every sub-rocker's pivot pin runs through a lug at both ends and
        # the inboard lug (a main rocker or the middle sub's link) hangs from the yoke
        res["C21"] = check_sub_hangers(pv, spec, frame)
        # C15 (M1): end-plate tops level
        tops = []
        for n, v in pv.items():
            if n.startswith("endplate_"):
                tz = v[v[:, 2] > v[:, 2].max() - 0.01, 2]
                tops += [tz.min(), tz.max()]
        _check(len(tops) == 8 and max(tops) - min(tops) <= 1e-4,
               f"{tag} C15 end-plate tops level (dz <= 1e-4)", f"(dz {max(tops) - min(tops):.2e})")
        # C16 (F11): ladder landing
        deck = A + DQ_DECK
        lands = [v for n, v in pv.items() if n.startswith(("grate_landp", "grate_landn"))]
        _check(len(lands) == 4, f"{tag} C16 landing plates present", f"({len(lands)} parts)")
        land = np.concatenate(lands)
        dy0, dy1 = deck_y(spec)
        _check(abs(land[:, 2].max() - deck) <= 1e-4, f"{tag} C16 landing top = deck top A - 0.296",
               f"({land[:, 2].max() - A:+.4f})")
        _check(np.abs(land[:, gi]).min() <= spec.ladder_w / 2.0 + 0.05
               and np.abs(land[:, gi]).max() >= dy0 - 1e-3,
               f"{tag} C16 landing bridges ladder -> deck", f"(|y| {np.abs(land[:, gi]).min():.3f}..{np.abs(land[:, gi]).max():.3f})")
        lmast = np.hypot(land[:, 0], land[:, 1]).min()
        _check(lmast > spec.mast_r, f"{tag} C16 landing clear of the mast", f"(r {lmast:.3f} > {spec.mast_r})")
        rungs = [v[:, 2].mean() for n, v in pv.items() if n.startswith("ladder_rung_")]
        rr = np.concatenate([v for n, v in pv.items() if n.startswith("ladder_rail_")])
        _check(max(rungs) <= deck + 1e-6 and max(rungs) >= deck - spec.ladder_pitch - 1e-6
               and rr[:, 2].max() >= deck + 0.9, f"{tag} C16 ladder reaches the landing",
               f"(top rung A {max(rungs) - A:+.3f}, rail top A {rr[:, 2].max() - A:+.3f})")
        # C17 (F10): no rail / post / kick PART overlaps a girder or hanger box. Per-part
        # AABB, not vertices: a straight rail tube has rings only at its two ends.
        g, h = girder_box(spec), hanger_box(spec)
        boxes = []
        for sg in (-1.0, 1.0):
            for b in (g, h):
                ys = sorted((sg * b["y0"], sg * b["y1"]))
                boxes.append(((-b["x"] / 2.0, b["x"] / 2.0), tuple(ys), (A + b["z0"], A + b["z1"])))
        bad = []
        m = 0.005
        for n, v in sorted(pv.items()):
            if not n.startswith(("post_", "rail_", "kick_")):
                continue
            lo, hi = v.min(axis=0), v.max(axis=0)
            for bx, by, bz in boxes:
                if (lo[ti] < bx[1] + m and hi[ti] > bx[0] - m and lo[gi] < by[1] + m
                        and hi[gi] > by[0] - m and lo[2] < bz[1] + m and hi[2] > bz[0] - m):
                    bad.append(n)
                    break
        _check(not bad, f"{tag} C17 deck rails / posts / kicks clear of girders and hangers (F10)",
               f"(overlapping parts {bad[:4]})")
        # deck level, deck covers the train
        dk = np.concatenate([v for n, v in pv.items() if n.startswith(("grate_deckp", "grate_deckn"))])
        _check(abs(dk[:, 2].max() - deck) <= 1e-4, f"{tag} deck top A - 0.296 (level)",
               f"({dk[:, 2].max() - A:+.4f})")
        # C18 (review F2): the DQ chair's hanger column - the station's C8 column, lane +- 0.45,
        # from the grip top down 1.70 m; grip top on the rope today, 0.22 higher after dq-chair
        # Q1; train sag added both ways - is clear of every grate_ / post_ / rail_ / kick_
        # triangle and vertex on BOTH lanes over the whole travel extent (outboard on the
        # stations' spin since nitpick A2 / A3 - the carrier's right; inboard kept clear for a
        # reversed loop), and the rails / posts / kicks clear every gantry_ triangle (the
        # A-braces, skeptic F10).
        res["C18"] = check_hanger_column(obj, table, spec)
        cols = hanger_columns(spec)
        col_txt = ", ".join(f"lane +-{c['half']} over rope {c['z0']:+.2f}..{c['z1']:+.2f}" for c in cols)
        _check(not res["C18"]["column"], f"{tag} C18 hanger column ({col_txt}) clear of grate_/post_/rail_/kick_",
               f"(hits {res['C18']['column'][:6]}; deck |Y| <= {dy1}, rails {rail_y(spec)})")
        _check(not res["C18"]["gantry"], f"{tag} C18 rails / posts / kicks clear of gantry_ triangles",
               f"(hits {res['C18']['gantry'][:6]})")
        if spec.family == "GD":
            gd = check_gd_clearances(obj, table, spec)
            res["C19_C20"] = gd
            if spec.comm_y is not None:          # ETGD: the station owns the comm anchor
                _check(abs(spec.comm_y - (A + GD_COMM_DA)) < 1e-9 and not gd["comm"],
                       f"{tag} C19 comm LV at axle + {GD_COMM_DA} = {spec.comm_y} clear (r 0.10 along travel)",
                       f"(hits {gd['comm'][:4]}; window A{gd['comm_window'][0]:+.3f}..A{gd['comm_window'][1]:+.3f})")
            _check(not gd["carrier"], f"{tag} C20 gondola carrier boxes (tram_tower.CARRIER_GD, hanger "
                   f"outboard) + head rule clear on both lanes", f"(hits {gd['carrier'][:6]})")
        if spec.role == "terminal":
            # C13 (M4): ETDQ per dq-terminal.md; ETGD (detail pass 1) per dq-terminal 13 and the
            # gondola base station's keep-out (lift_terminal_detach.GD_TOWER_KEEPOUT)
            rope, lim, nm = ((GD_ENTRY_ROPE_Y, GD_STATION_INBOARD_MAX, "ETGD") if spec.family == "GD"
                             else (DQ_ENTRY_ROPE_Y, DQ_STATION_INBOARD_MAX, "ETDQ"))
            # T27: the ROPE (lane_y: the axle, or the hold-down crown under ROPE_ON_CROWN) is the station's loading rope
            _check(abs(lane_y(spec) - rope) < 1e-9,
                   f"{tag} C13 {nm} axle {rope} (dq-terminal Z_ROPE)", f"({A})")
            _check(spec.sheaves == 8, f"{tag} C13 {nm} 8 sheaves per side")
            inboard = spec.hosted_z + res["travel"]
            _check(inboard <= lim, f"{tag} C13 hosted inboard end <= {lim}",
                   f"(z {spec.hosted_z} + {res['travel']:.3f} = {inboard:.3f})")
            if spec.family == "GD":
                _check(zmax <= 7.0 - 1e-6 and res["lateral"] <= 4.60,
                       f"{tag} C13 ETGD inside the station keep-out (top < 7.0, |lateral| <= 4.60)",
                       f"(top {zmax:.3f}, lateral {res['lateral']:.3f})")
            mx = float(np.abs(pv["mast"][:, gi]).max())
            _check(mx <= 1.0, f"{tag} C13 mast |x| <= 1.0", f"({mx:.3f})")
            res["station_inboard_z"] = inboard
        res["deck_length"] = dq_deck_length(spec)
    return res


def gd_numbers(spec, contracts):
    """Revision 1b report block for one GD member (manifest `gd_numbers`; the build report)."""
    A = spec.axle
    return {"top_anchor_y": spec.top, "axle": A, "h_top": round(contracts["top"] - A, 5),
            "tube_top": spec.tube_top, "mast_r": spec.mast_r, "lane_x": spec.gauge / 2.0,
            "comm_lv_y": spec.comm_y, "sheaves_per_side": spec.sheaves,
            "sheave_r_w_pitch_segs": [spec.sheave_r, spec.sheave_w, spec.pitch, sheave_segs(spec)],
            "deck_y": list(deck_y(spec)), "deck_top_y": round(A + DQ_DECK, 6), "rail_y": rail_y(spec),
            "hanger_columns_rel_rope": hanger_columns(spec), "gantry_rise": GD_RISE,
            "beam_half": round(2.922 + GD_DY, 6), "flange_r": flange_r(spec), "pad_r": pad_radius(spec),
            "pier": spec.pier, "bounds": {"max_z": round(contracts["top"], 5), "min_z": round(contracts["min"], 5),
                                          "lateral": round(contracts["lateral"], 5),
                                          "travel": round(contracts["travel"], 5)}}


# --------------------------------------------------------------------------------------
# Bake hooks
# --------------------------------------------------------------------------------------

D_BOLT = dict(bolt_pitch_m=0.15, bolt_r_m=0.022)   # DT5: r 0.018 -> 0.022 (floor 45.5 px/m)
# M2: the DQ / GD head classes ship at 41-50 px/m after the metric-UV repack (the girders' bevel
# faces lowest): r 0.027 (floor 37 px/m)
D_BOLT_FAM = {"FG": 0.022, "DQ": 0.027, "GD": 0.027}
# M2: every value below clears the 2-texel floor at its part class's shipped px/m (measured
# per variant in the build report; DT-C8 asserts 0 floor skips in the bake)
DP1_RELIEF = {
    "edge_r_head": 0.048,       # DQ/GD rocker_sub, yoke, endplate, hanger, gantry post/brace/saddle
    "edge_r_ingest": 0.050,     # the Kagelok gantry_00 (smart-project islands: 45.8 px/m lowest)
    "edge_r_fg_small": 0.040,   # FG liftpost_ / cwbrace_ (weight 1.0: 0.020 skipped; FG20 57 px/m)
    "edge_r_tipdefl": 0.033,    # FG tipdeflector (weight 1.25: 0.020 skipped)
    "edge_r_pad": {"FG": 0.06, "DQ": 0.095, "GD": 0.095},   # concrete pad (weight 0.75: 25 px/m on GD20)
    "weld_half_w": {"FG": 0.02, "DQ": 0.035, "GD": 0.035},  # mast weld bands (FG 0.015 skipped on FG16/20)
    "boltring": {"FG": 0.022, "DQ": 0.024, "GD": 0.024},    # stiff_ splice-flange bolt r (DQ/GD weight 1.5)
    "boltring_n": 12, "boltring_dr": 0.02,
    "grate_m": 0.055,           # was 0.05: 2.06 texels on GD20 / DQ18 decks (weight 1.25)
    "hub": dict(r=0.075, h=0.006),                          # sheave hub caps
}
# compose tone (M7 / S8: the design's +-0.02 L was below a JND; amplitudes raised so the paint
# mottle is a 1.2 % luminance std and MaskMap A gains MOTTLE_STD * 0.10 = 0.023)
DP1_TONE = {
    "paint": dict(mottle_m=0.60, mottle_amp=0.05, mottle_smooth=0.10, chalk_amp=0.06, chalk_smooth=0.08),
    "galv": dict(mottle_m=0.15, mottle_stretch=2.5, mottle_amp=0.06, mottle_smooth=0.10),
}
DP1_TONE_FLOOR_TEXELS = 4.4     # galv bloom cell >= this many texels (cs2_compose.TONE_FLOOR 4)
DP1_YELLOW = (0.95, 0.74, 0.05)  # hazard band (sRGB)
DP1_BLACK = 0.05                 # sRGB


def detail_marks(spec, pv, frame="net"):
    """World-space Marks (design 3.4) in the frame of the part vertices `pv` (the bake sees
    the NET frame): mast weld bands (flange top, both sides of every joint ring, under the
    arm cap / the head), splice-flange boltrings on the joint rings' top and bottom faces,
    sheave hub caps on both discs of every rim. Returns {prefix: (Mark, ...)}."""
    M = cs2_compose.Mark
    R = DP1_RELIEF
    ti, _ts, gi = _frame_axes(frame)
    hw = R["weld_half_w"][spec.family]
    rmax = spec.mast_r + 0.02
    height = spec.tube_top - (0.14 if spec.family == "FG" else 0.0)
    joints = _joint_zs(spec, spec.tube_top)
    zs = [flange_top(spec) + hw]
    for zj in joints:
        zs += [zj - 0.04 - hw, zj + 0.04 + hw]
    zs.append(height - hw if spec.family == "FG" else spec.axle - 0.10 - hw)
    out = {"mast": tuple(M("band", (0.0, 0.0, z), n=(0.0, 0.0, 1.0), half_w=hw, r_max=rmax, h=0.004)
                         for z in zs)}
    rings = []
    for zj in joints:
        for sz in (1.0, -1.0):
            rings.append(M("boltring", (0.0, 0.0, zj + sz * 0.04), u=(1.0, 0.0, 0.0), v=(0.0, 1.0, 0.0),
                           n=(0.0, 0.0, sz), count=R["boltring_n"], radius=spec.mast_r + R["boltring_dr"],
                           bolt_r=R["boltring"][spec.family], h=0.004))
    out["stiff_"] = tuple(rings)
    hub = []
    dress = []
    e = [0.0, 0.0, 0.0]
    e[gi] = 1.0
    for nm in sorted(pv):
        if nm.startswith("sheave_") and nm.endswith("_rim"):
            c = (pv[nm].min(axis=0) + pv[nm].max(axis=0)) / 2.0
            for sg in (1.0, -1.0):
                cc = [float(c[k] + sg * e[k] * spec.sheave_w / 2.0) for k in range(3)]
                hub.append(M("ring", tuple(cc), u=(0.0, 0.0, 1.0), v=tuple(e if ti != 2 else (1.0, 0.0, 0.0)),
                             n=tuple(sg * x for x in e), r_in=0.0, r_out=R["hub"]["r"], h=R["hub"]["h"]))
                if S.wheel_dress():     # the wheel dress: a recessed web ring and a raised shoulder lip per disc face
                    for D_ in S.WHEEL_DRESS_RINGS:
                        dress.append(M("ring", tuple(cc), u=(0.0, 0.0, 1.0), v=tuple(e if ti != 2 else (1.0, 0.0, 0.0)),
                                       n=tuple(sg * x for x in e), r_in=D_["r_in"] * spec.sheave_r,
                                       r_out=D_["r_out"] * spec.sheave_r, h=D_["h"]))
    out["sheave_rims"] = tuple(hub)
    out["sheave_dress"] = tuple(dress)          # empty unless the wheel dress is on
    return out


def tower_detail_specs(spec=None, pv=None, frame="net"):
    """
    Relief specs for detail_height (and the weather layer's lattice rust), prefix -> DetailSpec
    (design 3.4 with M2). `spec` None = FG8 (the statics only; tests/test_compose.py TC1 calls
    it without arguments and then runs its pre-pass snapshot). With `pv` (part vertices in
    `frame`) the marks are added: weld bands on `mast`, boltrings on `stiff_`, hub caps on
    every `sheave_*_rim` (keyed by the full rim names so `sheave_arm_*` never matches).
    """
    D = cs2_compose.DetailSpec
    spec = spec or VARIANTS["FG8"]
    fam = spec.family
    R = DP1_RELIEF
    er = R["edge_r_head"]
    bolt = dict(D_BOLT, bolt_r_m=D_BOLT_FAM[fam])
    out = {"crossarm": D(**bolt), "rocker_main_": D(**bolt), "gantry_girder_": D(**bolt),
           "gantry_beam": D(**bolt),
           "yoke_": D(edge_round_m=er, **bolt), "endplate_": D(edge_round_m=er, **bolt),
           "grate_": D(grate_m=R["grate_m"]), "pad": D(edge_round_m=R["edge_r_pad"][fam])}
    for nm in ("catwalk", "deckp", "deckn", "landp", "landn"):
        out[f"grate_{nm}_lip"] = D()
    if fam == "FG":
        # M2 "raise or drop": kick_ is DROPPED - a 0.04 round on the 0.02 x 0.10 kick plate
        # rounds its whole height and the plate read as a pipe in the 2x close-up
        for p in ("liftpost_", "cwbrace_"):
            out[p] = D(edge_round_m=R["edge_r_fg_small"])
        out["tipdeflector"] = D(edge_round_m=R["edge_r_tipdefl"])
    else:
        for p in ("rocker_sub_", "hanger_", "gantry_post_", "gantry_brace", "gantry_saddle", "gusset_apex"):
            out[p] = D(edge_round_m=er)
        out["gantry_00"] = D(edge_round_m=R["edge_r_ingest"])
    if pv is not None:
        mk = detail_marks(spec, pv, frame)
        out["mast"] = D(marks=mk["mast"])
        if mk["stiff_"]:
            out["stiff_"] = D(marks=mk["stiff_"])
        if not S.wheels_v2():       # T62 round 2: the v2 wheel's hub is the geometric boss (no normal-map ring)
            for nm in sorted(pv):
                if nm.startswith("sheave_") and nm.endswith("_rim"):
                    out[nm] = D(marks=mk["sheave_rims"] + mk["sheave_dress"])
        elif mk["sheave_dress"]:    # the wheel dress on a v2 wheel: only its two disc-face rings are marks
            for nm in sorted(pv):
                if nm.startswith("sheave_") and nm.endswith("_rim"):
                    out[nm] = D(marks=mk["sheave_dress"])
    return out


def tone_specs(table, part_material, ppm_of):
    """detail_tone specs keyed by FULL part name (the material split does not follow the
    relief prefixes): paint -> DP1_TONE paint; galv -> bloom with the cell raised to
    DP1_TONE_FLOOR_TEXELS / px/m where the part ships below 0.15 m x 4 texels (M7 b)."""
    D = cs2_compose.DetailSpec
    out = {}
    for i, nm in enumerate(table, start=1):
        key = part_material.get(nm)
        if key == "paint":
            out[nm] = D(**DP1_TONE["paint"])
        elif key == "galv":
            g = dict(DP1_TONE["galv"])
            ppm = ppm_of.get(i, 0.0)
            if ppm > 0:
                g["mottle_m"] = max(g["mottle_m"], DP1_TONE_FLOOR_TEXELS / ppm)
            out[nm] = D(**g)
    return out


def _srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def paint_warning_triangle(rect, base, ctl):
    """
    The legacy crash-pad stamp (cs2_asset.paint_pad_strip) on the crashpad_outer island:
    T = 0.62 h, W = 1.12 T (no anisotropy pre-stretch: the unique island keeps its 3D
    aspect), base row 0.16 h, centered on the island (theta 0 = the +X uphill face). White
    display 0.92, exclamation bar + dot display 0.10. ControlMask R and G zeroed under it.
    (Pre-pass painter, kept for the record; the shipping hook calls paint_pad_stamps.)
    """
    r0, r1, c0, c1 = rect
    h, w = r1 - r0, c1 - c0
    T = int(round(0.62 * h))
    W = 1.12 * T
    cx = w / 2.0
    y0 = int(round(0.16 * h))
    white = _srgb_to_lin(0.92)
    dark = _srgb_to_lin(0.10)
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


def _ss_grid(h, w, ss):
    """Sub-sample centers (rows, cols) in texel units of an (h, w) raster, ss x ss per texel."""
    o = (np.arange(ss) + 0.5) / ss
    ys = (np.arange(h)[:, None] + o[None, :]).ravel()
    xs = (np.arange(w)[:, None] + o[None, :]).ravel()
    return np.meshgrid(ys, xs, indexing="ij")


def _ss_mean(mask, h, w, ss):
    return mask.reshape(h, ss, w, ss).mean(axis=(1, 3))


def triangle_geom(h, w):
    """The warning triangle in the crashpad_outer rect (the legacy layout, design 3.4)."""
    T = 0.62 * h
    return {"T": T, "W": 1.12 * T, "cx": w / 2.0, "y0": 0.16 * h, "bw": 0.05 * T}


def triangle_coverage(h, w, ss=4):
    """(tri, mark) anti-aliased coverage (h, w), rows bottom-up: the white triangle and the
    dark exclamation bar + dot (design 3.4, DT-C7: fractional edge texels)."""
    g = triangle_geom(h, w)
    Y, X = _ss_grid(h, w, ss)
    t = (Y - g["y0"]) / g["T"]
    tri = (t >= 0.0) & (t <= 1.0) & (np.abs(X - g["cx"]) <= g["W"] / 2.0 * (1.0 - t))
    mark = np.zeros_like(tri)
    for f0, f1 in ((0.30, 0.72), (0.10, 0.22)):
        mark |= (t >= f0) & (t <= f1) & (np.abs(X - g["cx"]) <= g["bw"])
    return _ss_mean(tri, h, w, ss), _ss_mean(mark & tri, h, w, ss)


def hazard_coverage(h, w, pad_h, ss=4):
    """(band, yellow) coverage (h, w), rows bottom-up, of the crash pad's top hazard band
    (design 3.4 / Q7): 0.12 m tall, its top 0.02 m under the rim, 45 deg stripes of period
    0.15 m; the island is metric (u = R theta, v = z), so h rows span the pad height pad_h."""
    ppm = h / pad_h
    Y, X = _ss_grid(h, w, ss)
    zt, zb = pad_h - 0.02, pad_h - 0.14
    band = (Y >= zb * ppm) & (Y <= zt * ppm)
    s = (X + Y) / (0.15 * ppm)
    yellow = band & ((s - np.floor(s)) < 0.5)
    return _ss_mean(band, h, w, ss), _ss_mean(yellow, h, w, ss)


def make_pad_painter(pad_h):
    """crashpad_outer painter: the anti-aliased warning triangle + the hazard band. ControlMask
    R and G scale by (1 - coverage), 0 at >= 0.99 (the decal stamp rule): fixed colors."""
    white = _srgb_to_lin(0.92)
    dark = _srgb_to_lin(0.10)
    yel = np.array([_srgb_to_lin(c) for c in DP1_YELLOW], dtype=np.float32)
    blk = _srgb_to_lin(DP1_BLACK)

    def painter(rect, base, ctl):
        r0, r1, c0, c1 = rect
        h, w = r1 - r0, c1 - c0
        tri, mark = triangle_coverage(h, w)
        band, yc = hazard_coverage(h, w, pad_h)
        b = base[..., 0:3]
        b[...] = b * (1.0 - tri[..., None]) + white * tri[..., None]
        b[...] = b * (1.0 - mark[..., None]) + dark * mark[..., None]
        col = yel[None, None, :] * yc[..., None] + blk * (band - yc)[..., None]
        b[...] = b * (1.0 - band[..., None]) + col
        cov = np.maximum(tri, band)
        cg = ctl[..., 0:2]
        cg[...] = cg * (1.0 - cov[..., None])
        cg[cov >= 0.99] = 0.0
        painter.last = {"tri": tri, "mark": mark, "band": band, "yellow": yc, "rect": rect}
    painter.last = None
    return painter


def pad_triangle_rect(mesh, table, A):
    """The warning triangle's texel rect (+ 2 px) on crashpad_outer at bake size A: the weather
    layer's protect_px (FP6: the triangle only; the hazard band wears)."""
    r = cs2_compose.uv_bbox_px(mesh, table, "crashpad_outer", A)
    if r is None:
        return []
    r0, r1, c0, c1 = r
    g = triangle_geom(r1 - r0, c1 - c0)
    return [(int(r0 + g["y0"]) - 2, int(math.ceil(r0 + g["y0"] + g["T"])) + 2,
             int(c0 + g["cx"] - g["W"] / 2.0) - 2, int(math.ceil(c0 + g["cx"] + g["W"] / 2.0)) + 2)]


def plate_numeral(spec):
    """The tower plate's numeral (design 3.4 / Q2): the variant's height numeral on line
    members (FG8 -> 8, DQ12 -> 12, GD20 -> 20); hosted terminal towers carry the glyph only."""
    if spec.role != "line":
        return None
    return "".join(ch for ch in spec.key if ch.isdigit())


def plate_raster(spec, h, w):
    """(rgb_linear (h, w, 3), info) of one tower plate, rows bottom-up: white 0.85 field, a
    6 mm black border, the resort glyph (ski_launcher_glyph cand_c) in the left square, the
    numeral in black on the right (welcome_sign.text_triangles at the plate's px/m)."""
    import ski_launcher_glyph as G
    import welcome_sign as WS
    pw, ph = DP1_PLATE[spec.family]
    ppm = h / ph
    white, blk = _srgb_to_lin(0.85), _srgb_to_lin(DP1_BLACK)
    Y, X = _ss_grid(h, w, 4)
    bpx = 0.006 * ppm
    inner = (X >= bpx) & (X <= w - bpx) & (Y >= bpx) & (Y <= h - bpx)
    field = _ss_mean(inner, h, w, 4)
    ink = 1.0 - field                                         # the border
    inner_h = h - 2.0 * bpx
    sq = int(math.floor(inner_h - 2.0))
    num = plate_numeral(spec)
    gx0 = int(round(bpx + 1.0)) if num else int(round((w - sq) / 2.0))
    gy0 = int(round((h - sq) / 2.0))
    if sq >= 1:     # T62 round 2: a test atlas (256) can leave the plate < 3 px tall (sq 0: render_alpha divides by it)
        glyph = G.render_alpha(G.cand_c(), sq, ss=8)[::-1]        # top-down -> bottom-up
        ink[gy0:gy0 + sq, gx0:gx0 + sq] = np.maximum(ink[gy0:gy0 + sq, gx0:gx0 + sq], glyph)
    cap_px = 0.0
    if num and sq >= 1 and int(w - bpx - 2) - (gx0 + sq + 2) >= 1 and inner_h - 6.0 >= 1.0:
        # (T62 round 2/3 guard: a test atlas can leave no room for the numeral; the shipping atlas always has it)
        cap_px = float(math.floor(inner_h - 6.0))
        tris, _wm = WS.text_triangles(num, cap_px / ppm)
        rx0 = gx0 + sq + 2
        rw, rh = int(w - bpx - 2) - rx0, h
        cov = WS._tri_coverage(tris, rw, rh, ppm, ppm, ss=4)
        ink[:, rx0:rx0 + rw] = np.maximum(ink[:, rx0:rx0 + rw], cov)
    rgb = white * (1.0 - ink) + blk * ink
    return np.repeat(rgb[..., None], 3, axis=2).astype(np.float32), {
        "px": (h, w), "ppm": round(ppm, 1), "glyph_px": sq, "cap_px": cap_px, "numeral": num}


def make_plate_painter(spec, log):
    def painter(rect, base, ctl):
        r0, r1, c0, c1 = rect
        rgb, inf = plate_raster(spec, r1 - r0, c1 - c0)
        base[..., 0:3] = rgb
        ctl[..., 0:2] = 0.0
        log.append(dict(inf, rect=list(rect)))
    return painter


def data_plate_decals(spec):
    """The data plate (design 3.4, M9c): a 0.25 x 0.15 m galv-gray plate with 4 dark text bars
    (no brand) on the mast at z 2.30, 45 deg off the conduit (+Y) toward the uphill +X face.
    Authoring frame; the hook maps it to the net frame (cs2_compose.transform_marks)."""
    d = DP1_DATAPLATE
    th = math.radians(d["theta_deg"])
    n = (math.cos(th), math.sin(th), 0.0)
    c = (spec.mast_r * n[0], spec.mast_r * n[1], d["z"])
    u = (-math.sin(th), math.cos(th), 0.0)                    # the viewer's right (z x n)
    v = (0.0, 0.0, 1.0)
    H, W = 60, 100
    Y, X = np.mgrid[0:H, 0:W]
    plate = np.ones((H, W), np.float32)
    plate[[0, -1], :] = 0.5
    plate[:, [0, -1]] *= 0.5
    bars = np.zeros((H, W), np.float32)
    for k, (f0, f1) in enumerate(((0.12, 0.80), (0.12, 0.62), (0.12, 0.88), (0.12, 0.45))):
        r0 = int(H * (0.74 - 0.17 * k))
        bars[r0:r0 + 5, int(W * f0):int(W * f1)] = 1.0
    grey = tuple(_srgb_to_lin(x) for x in (0.70, 0.71, 0.72))
    dark = tuple(_srgb_to_lin(x) for x in (0.08, 0.08, 0.09))
    D = cs2_compose.Decal
    return (D(c, u, v, d["w"] / 2.0, d["h"] / 2.0, plate, grey, reach_m=0.10, min_cos=0.5),
            D(c, u, v, d["w"] / 2.0 - 0.02, d["h"] / 2.0 - 0.02, bars[3:-3, 2:-2], dark, reach_m=0.10,
              min_cos=0.5))


# -- Follow-up 2026-10-11, integration finding F3 (+ F3b): black-sampled sub-texel faces -------
# A face whose UV footprint contains NO texel center (deck kick_deck* end faces, the 6 mm
# platerim_ strips, rail end caps, gusset edges) gets no EMIT sample; where no neighboring
# island's bake margin reaches it, it samples BLACK in game (DQ18 24 faces / 243 cm2, GD16 32 /
# 326 cm2, GD12 14, GD20 4, ETGD 4, DQ12 / DQ14 / DQ16 4-6 in the 18:01-18:10 exports). The
# lattice towers fixed the same defect generator-locally (tram_tower.fill_subtexel_faces:
# 8-164 black faces before, 0-2 after); this is the same fill, ported: the uncovered texels of
# the face's 1-texel footprint take its part's mean COVERED color (BaseColor, MaskMap,
# ControlMask - the slot tint still applies), or, for a part with no covered texel at all (a
# part made only of such faces), the mean of its material's parts. Covered texels are never
# touched; geometry and UVs do not change (a flat color fill: the UV merge / pad options of
# the finding would move islands in a packer that is byte-stable on 16 shipped variants).
# One deliberate difference from the lattice fill: it repairs the F3 DEFECT, not every face
# without a texel center. A face is filled only if its UV area is under SUBTEXEL_TEXEL2 AND its
# uncovered footprint holds a black texel (SUBTEXEL_ONLY_BLACK). The other variants - whose
# no-center faces are covered by a neighbor's margin and read a plausible color - keep their
# approved, shipped PNGs byte-for-byte (the unconditional fill rewrote 5-187 faces on all 16:
# FG8 5 ... GD20 187). Set SUBTEXEL_ONLY_BLACK False for the lattice behavior.
# The unit is the TRIANGLE the game draws: cs2_asset.export_fbx writes use_triangles (bmesh
# triangulate, default BEAUTY), so a quad whose area is fine can still split into a 2-texel
# triangle with a black centroid; both the fill and the contract run on uv_triangles(), the
# same triangulation (measured: the polygon-level fill left DQ18 4 / GD16 8 / DQ20 4 such
# triangles black in the written FBX).
#
# F3b (2026-10-11, the checker's finding F-1): the F3 class stopped at SUBTEXEL_TEXEL2 and the
# long one-texel slivers (UV area >= 4 texel2 but shortest altitude < 1 texel: the fall-arrest
# channel's 2 cm side walls, gusset_apex, kick_deck, platerim edges) sampled BLACK as well - 16
# of 17 variants had 2-22 of them (DQ20 2 / 3,681 cm2, GD16 22 / 5,538 cm2) - because they hold
# no texel center either (FG8's platerim strips even have centers exactly ON an edge: the bake
# left them black, and the part mean of those covered-but-black texels is black too). The
# corrected rule is area-free: AFTER the F3 pass (so every F3 texel is exactly the shipped one)
# a second pass takes EVERY triangle of any UV area and paints each BLACK texel its square
# overlaps (exact separating-axis test, SUBTEXEL_OVERLAP_EPS slack, so whatever 400-sample grid a
# counter lays on the triangle lands on a texel that is not black) with the mean color of the
# part's BAKED (non-black) covered texels, or its material's. Only black texels are written, so
# every texel that was not black is unchanged. A face whose part and material have no baked
# texel is counted in `slivers_unfilled` and the contract fails. SUBTEXEL_TEXEL2 now only
# limits the F3 footprint fill; it is no longer a limit on what the contract covers.
SUBTEXEL_TEXEL2 = 4.0          # F3 footprint-fill class: UV area under 2 x 2 texels (every no-center
                               # face measured black is 1-4 texel2; the "under 1 texel" faces are margin-covered)
SUBTEXEL_BLACK = 0.02          # sRGB max(rgb) under this = black (tram_tower test black_faces)
SUBTEXEL_BLACK_LIN = SUBTEXEL_BLACK / 12.92   # the same threshold on the linear maps (c < 0.04045)
SUBTEXEL_FILL_LIN = (6.0 / 255.0) / 12.92     # F3b: a texel that WRITES as byte 0-5 is black (0.0196 < 0.02);
                                              # one byte of slack over BLACK_LIN for the rounding of the PNG write
SUBTEXEL_ONLY_BLACK = True
SUBTEXEL_OVERLAP_EPS = 1.0e-3  # texels of slack on the overlap test (FBX float round trip)


def uv_triangles(mesh, uv_layer):
    """The triangles the FBX export writes (cs2_asset.export_fbx: use_triangles -> bmesh.ops.triangulate
    with its defaults, as io_scene_fbx does): [(parent polygon index, UV (3, 2) float64 in 0..1,
    3D area m2)], in bmesh face order (deterministic)."""
    bm = bmesh.new()
    bm.from_mesh(mesh)
    orig = bm.faces.layers.int.new("f3_orig")
    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        f[orig] = f.index
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    uvl = bm.loops.layers.uv[uv_layer]
    out = [(int(f[orig]), np.array([lp[uvl].uv[:] for lp in f.loops], dtype=np.float64), float(f.calc_area()))
           for f in bm.faces]
    bm.free()
    return out


def _tri_area_texel2(q):
    """Area of a UV triangle / polygon in texel2 (q in texels, (n, 2))."""
    return 0.5 * abs(float(np.dot(q[:, 0], np.roll(q[:, 1], -1)) - np.dot(q[:, 1], np.roll(q[:, 0], -1))))


def _tri_texel_overlap(q, X, Y, eps=SUBTEXEL_OVERLAP_EPS):
    """Texels whose unit square (center X, Y, in texels) intersects the UV triangle q (3, 2), touching
    included: exact separating-axis test (the two square axes + the three edge normals), `eps` texels of
    slack. Every sample point of the triangle, on any grid, lies in one of these texels."""
    m = ((X + 0.5 >= q[:, 0].min() - eps) & (X - 0.5 <= q[:, 0].max() + eps)
         & (Y + 0.5 >= q[:, 1].min() - eps) & (Y - 0.5 <= q[:, 1].max() + eps))
    for i in range(3):
        a, b = q[i], q[(i + 1) % 3]
        nx, ny = -(b[1] - a[1]), (b[0] - a[0])
        ln = math.hypot(nx, ny)
        if ln < 1e-12:
            continue
        nx, ny = nx / ln, ny / ln
        pr = q @ np.array([nx, ny])
        c = X * nx + Y * ny
        h = 0.5 * (abs(nx) + abs(ny)) + eps
        m &= (c + h >= pr.min()) & (c - h <= pr.max())
    return m


def fill_subtexel_faces(base, msk, ctl, mesh, A, part_map, table, part_material, uv_layer="UVUnique"):
    """tram_tower.fill_subtexel_faces for the tube towers, on the exported TRIANGLES (arrays are
    linear (A, A, 4), bottom-up rows; `part_map` = cs2_compose.rasterise_face_fields, 1-based
    table indices, 0 = gutter). Pass 1 (F3, the shipped behavior): triangles under SUBTEXEL_TEXEL2
    with no texel center inside get their uncovered 1-texel footprint painted when it holds a black
    texel (SUBTEXEL_ONLY_BLACK). Pass 2 (F3b): every triangle, any UV area, gets each black texel
    it overlaps painted with the mean BAKED color of its part (material fallback). Returns the stats
    {faces, texels, by_class (pass 1), f3b_faces, f3b_texels, f3b_by_class (pass 2), slivers_unfilled
    (triangles that still overlap a black texel for want of any baked color: must be {})}."""
    pid = cs2_compose.part_indices(mesh)
    pm = np.asarray(part_map)
    cov = pm > 0
    n_parts = len(table) + 1
    mat_of = {i + 1: part_material.get(nm) for i, nm in enumerate(table)}

    def part_means(sel):
        cnt = np.bincount(pm[sel], minlength=n_parts).astype(np.float64)
        means = {}
        for name_, arr in (("base", base), ("msk", msk), ("ctl", ctl)):
            m = np.zeros((n_parts, 4), np.float64)
            for ch in range(4):
                m[:, ch] = np.bincount(pm[sel], weights=arr[..., ch][sel].astype(np.float64), minlength=n_parts)
            means[name_] = m
        by_mat = {}
        for p_ in range(1, n_parts):
            if cnt[p_] > 0:
                acc = by_mat.setdefault(mat_of[p_], [0.0, np.zeros((3, 4))])
                acc[0] += cnt[p_]
                acc[1] += np.stack([means[k][p_] for k in ("base", "msk", "ctl")])
        return cnt, means, by_mat

    def colour(p_, cnt, means, by_mat):
        if cnt[p_] > 0:
            return [means[k][p_] / cnt[p_] for k in ("base", "msk", "ctl")]
        acc = by_mat.get(mat_of.get(p_))
        return None if acc is None else list(acc[1] / acc[0])

    # both color sets are taken BEFORE any fill: pass 1 = every covered texel (the shipped F3 colors),
    # pass 2 = the covered texels that are not black (a texel the bake sampled but left black, e.g. a
    # center on a sliver's edge, must not darken the part's color)
    cnt1, means1, by_mat1 = part_means(cov)
    cnt2, means2, by_mat2 = part_means(cov & (base[..., 0:3].max(axis=-1) >= SUBTEXEL_FILL_LIN))
    tris = uv_triangles(mesh, uv_layer)

    def window(q):
        x0 = max(0, int(math.floor(q[:, 0].min() - 1.0)))
        x1 = min(A, int(math.ceil(q[:, 0].max() + 1.0)))
        y0 = max(0, int(math.floor(q[:, 1].min() - 1.0)))
        y1 = min(A, int(math.ceil(q[:, 1].max() + 1.0)))
        return x0, x1, y0, y1

    faces = texels = 0
    classes = {}
    for orig, uv, _a3 in tris:                       # pass 1: F3, unchanged
        q = uv * A
        if _tri_area_texel2(q) >= SUBTEXEL_TEXEL2:   # the long ones: pass 2
            continue
        x0, x1, y0, y1 = window(q)
        if x1 <= x0 or y1 <= y0:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        if cs2_compose._poly_mask(q, X, Y, 0.0).any():
            continue                                    # the bake sampled this triangle
        foot = cs2_compose._poly_mask(q, X, Y, 1.0)
        if not foot.any():
            cx, cy = q.mean(axis=0)
            foot = (np.abs(X - cx) <= 0.5) & (np.abs(Y - cy) <= 0.5)
        sl = (slice(y0, y1), slice(x0, x1))
        tgt = foot & ~cov[sl]
        if not tgt.any():
            continue
        if SUBTEXEL_ONLY_BLACK and not (tgt & (base[sl][..., 0:3].max(axis=-1) < SUBTEXEL_BLACK_LIN)).any():
            continue                                    # margin-covered: reads a color, not black
        p_ = int(pid[orig])
        vals = colour(p_, cnt1, means1, by_mat1)
        if vals is None:
            continue
        for arr, v in zip((base, msk, ctl), vals):
            arr[sl][tgt] = v.astype(np.float32)
        faces += 1
        texels += int(tgt.sum())
        k = _class_of(table[p_ - 1])
        classes[k] = classes.get(k, 0) + 1
    f3b_faces = f3b_texels = 0
    f3b_classes = {}
    left = {}
    for orig, uv, _a3 in tris:                       # pass 2: F3b, every triangle, black texels only
        q = uv * A
        x0, x1, y0, y1 = window(q)
        if x1 <= x0 or y1 <= y0:
            continue
        sl = (slice(y0, y1), slice(x0, x1))
        dark = base[sl][..., 0:3].max(axis=-1) < SUBTEXEL_FILL_LIN
        if not dark.any():
            continue
        X, Y = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        tgt = dark & _tri_texel_overlap(q, X, Y)
        if not tgt.any():
            continue
        p_ = int(pid[orig])
        k = _class_of(table[p_ - 1])
        vals = colour(p_, cnt2, means2, by_mat2)
        if vals is None:
            left[k] = left.get(k, 0) + 1
            continue
        for arr, v in zip((base, msk, ctl), vals):
            arr[sl][tgt] = v.astype(np.float32)
        f3b_faces += 1
        f3b_texels += int(tgt.sum())
        f3b_classes[k] = f3b_classes.get(k, 0) + 1
    print(f"[tower2] sub-texel triangles filled (F3): {faces} faces, {texels} texels {dict(sorted(classes.items()))}; "
          f"black texels under any-area triangles filled (F3b): {f3b_faces} faces, {f3b_texels} texels "
          f"{dict(sorted(f3b_classes.items()))}; unfillable {dict(sorted(left.items()))}")
    return {"faces": faces, "texels": texels, "by_class": dict(sorted(classes.items())),
            "f3b_faces": f3b_faces, "f3b_texels": f3b_texels, "f3b_by_class": dict(sorted(f3b_classes.items())),
            "slivers_unfilled": dict(sorted(left.items()))}


def subtexel_black_faces(mesh, base_px, uv_layer=None, max_texel2=float("inf")):
    """The F3 / F3b contract's counter, on the SHIPPED BaseColor and the exported triangles
    (uv_triangles): triangles of ANY UV area (`max_texel2` limits it, default none) that overlap a
    texel which samples black (sRGB max(rgb) < SUBTEXEL_BLACK) - so a 400-sample or any other grid on
    the triangle cannot land on black. `base_px` = cs2_validate.load_png_pixels (bottom-up rows, the
    UV v order). Returns [(parent polygon index, uv area in texel2, area cm2)]; mesh coordinates are
    meters (net frame)."""
    A = base_px.shape[0]
    layer = uv_layer or mesh.uv_layers.active.name
    mx = base_px[..., :3].max(axis=-1)
    out = []
    for orig, uv, a3 in uv_triangles(mesh, layer):
        q = uv * A
        area = _tri_area_texel2(q)
        if area >= max_texel2:
            continue
        x0 = max(0, int(math.floor(q[:, 0].min() - 1.0)))
        x1 = min(A, int(math.ceil(q[:, 0].max() + 1.0)))
        y0 = max(0, int(math.floor(q[:, 1].min() - 1.0)))
        y1 = min(A, int(math.ceil(q[:, 1].max() + 1.0)))
        if x1 <= x0 or y1 <= y0:
            continue
        dark = mx[y0:y1, x0:x1] < SUBTEXEL_BLACK
        if not dark.any():
            continue
        X, Y = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        if (dark & _tri_texel_overlap(q, X, Y)).any():
            out.append((orig, round(area, 3), round(a3 * 1e4, 2)))
    return out


def make_post_compose_v2(obj, table, spec=None, info=None):
    """
    cs2_bake post_compose hook (lift_terminal.make_post_compose + the pad stamp), detail pass 1:
    relief (bolts r 0.022, edge rounds, weld bands, splice boltrings, hub caps), compose tone
    (paint mottle + chalk, galv bloom), then the stamps - the crash pad's anti-aliased triangle
    and hazard band, the two tower plates PER PART (M3: plate_n, plate_p) and the data-plate
    decal on the mast. Records: hook.relief / tone / tone_gate (M7) / plates / decal_rects.
    spec None = the pre-pass FG8 behavior is NOT reproduced (tests use spec)."""
    spec = spec or VARIANTS["FG8"]
    info = info or {}
    part_material = info.get("part_material") or {}
    rot = mathutils.Matrix.Rotation(-math.pi / 2.0, 4, "Z")      # authoring -> net (orient_for_net)

    def hook(base, nrm, msk, ctl, ao, mesh, A):
        hook.density = cs2_uv.density_report(obj, A, reserved_rects=[])
        part_map, ppm_map = cs2_compose.rasterise_face_fields(mesh, A)
        pv = part_vertices(obj, table)
        specs = tower_detail_specs(spec, pv, "net")
        hook.specs = specs
        h = cs2_compose.detail_height(mesh, A, specs)
        hook.relief = {k: dict(v) for k, v in cs2_compose.LAST_DETAIL_STATS.items()}
        if hook.relief.get("skipped"):
            hook.relief["skipped_parts"] = relief_floor_report(mesh, A, specs)
        hook.height_range = (float(h.min()), float(h.max()))
        ppm_of = {}
        for p in np.unique(part_map):
            if p > 0:
                ppm_of[int(p)] = float(np.median(ppm_map[part_map == p]))
        hook.ppm_of = ppm_of
        tspecs = tone_specs(table, part_material, ppm_of)
        dl, ds = cs2_compose.detail_tone(mesh, A, tspecs)
        hook.tone = {k: dict(v) for k, v in cs2_compose.LAST_TONE_STATS.items()}
        uncovered = nrm[..., 0:3].max(axis=-1) < 1e-3
        opts = {"reserved": (), "part_map": part_map, "seed": 0}
        b1, n1, m1, c1 = cs2_compose.compose_maps(base, nrm, msk, ctl, None, h, ppm_map,
                                                  dict(opts, tone_l=dl, tone_s=ds))
        # M7 (restated DT-C9): the same compose without tone, per paint / galv class
        _b0, _n0, m0, _c0 = cs2_compose.compose_maps(base, nrm, msk, ctl, None, h, ppm_map, opts)
        hook.tone_gate = tone_gate(table, part_material, tspecs, part_map, ppm_map, m0, m1)
        base, nrm, msk, ctl = b1, n1, m1, c1
        n = nrm[..., 0:3] * 2.0 - 1.0
        n[..., 2] = np.maximum(n[..., 2], 0.05)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        nrm[..., 0:3] = n * 0.5 + 0.5
        nrm[uncovered, 0:3] = (0.5, 0.5, 1.0)
        nrm[..., 3] = 1.0
        # F3: faces with no texel center would sample black; fill BEFORE the stamps and the weather layer
        hook.subtexel = fill_subtexel_faces(base, msk, ctl, mesh, A, part_map, table, part_material)
        pad_h = cs2_asset.CRASHPAD["z1"] - crashpad_z0(spec)
        pad = make_pad_painter(pad_h)
        cs2_compose.stamp_on_face(base, ctl, mesh, table, "crashpad_outer", pad)
        hook.pad = pad.last
        hook.plates = []
        for side in ("plate_n", "plate_p"):                      # M3: per part, never "plate_"
            cs2_compose.stamp_on_face(base, ctl, mesh, table, side, make_plate_painter(spec, hook.plates))
        rects = []
        for dec in cs2_compose.transform_marks(data_plate_decals(spec), rot):
            rects += cs2_compose.decal_on_faces(base, ctl, mesh, A, table, "mast", dec)
        hook.decal_rects = sorted(set(rects))
        return base, nrm, msk, ctl
    hook.density = hook.height_range = hook.relief = hook.tone = hook.tone_gate = None
    hook.pad = hook.plates = hook.specs = hook.ppm_of = hook.subtexel = None
    hook.decal_rects = []
    return hook


def relief_floor_report(mesh, A, specs):
    """Which parts a relief kind skips below the 2-texel floor (detail_height counts faces
    only): edge rounds on faces that HAVE a convex crease, bolt rows; {kind: {part: [faces,
    min texels]}} - the DT-C8 diagnostic."""
    table = cs2_compose.read_part_table(mesh)
    parts = cs2_compose.part_indices(mesh)
    topo = cs2_compose._EdgeTopology(mesh, parts)
    out = {}
    for poly, _sl, _m, _X, _Y, _q, _P, ppm in cs2_compose.iter_face_rasters(mesh, A, "UVUnique", 0.0):
        pi = int(parts[poly.index])
        if pi < 1 or pi > len(table):
            continue
        nm = table[pi - 1]
        sp = cs2_compose._spec_for(nm, specs)
        if sp is None:
            continue
        for kind, size in (("edge_round", sp.edge_round_m), ("bolts", 2.0 * sp.bolt_r_m if sp.bolt_pitch_m else None)):
            if size is None or size * ppm >= cs2_compose.TEXEL_FLOOR - 1e-6:
                continue
            if kind == "edge_round" and not topo.creases(poly.index, sp.edge_round_deg):
                continue
            row = out.setdefault(kind, {}).setdefault(nm, [0, 99.0])
            row[0] += 1
            row[1] = round(min(row[1], size * ppm), 2)
    return out


def _class_of(name):
    toks = []
    for t in name.split("_"):
        t2 = "".join(ch for ch in t if not ch.isdigit())
        if t2 in ("", "n", "p", "a", "b", "m", "s", "rim"):
            continue
        toks.append(t2)
    return "_".join(toks) or name


def tone_gate(table, part_material, tspecs, part_map, ppm_map, m_off, m_on, min_texels=200):
    """
    M7 - DT-C9 restated to what the noise can deliver (the design's >= 0.03 needs mottle_smooth
    ~0.12 from a 0.012 base; the shared report's MOTTLE_STD 0.234). Measured at the COMPOSE
    stage (pre-weather) as the tone ON vs OFF compose of the same bake and seed:
      gate (per MATERIAL class, paint and galv, every part above the tone floor pooled - enough
      noise cells for the population std): s1 >= 0.9 sqrt(s0^2 + (MOTTLE_STD smooth)^2);
      report (per PART class, >= min_texels): s0, s1 and the delivered tone std
      sqrt(s1^2 - s0^2) / (MOTTLE_STD smooth) - small classes sample few cells (FG8 run:
      0.55-0.95), so they are reported, not gated.
    Returns {"material": {paint|galv: {...ok}}, "class": {class: {...}}}.
    """
    out = {"material": {}, "class": {}}
    by_mat, by_cls = {}, {}
    for i, nm in enumerate(table, start=1):
        key = part_material.get(nm)
        if key in ("paint", "galv") and nm in tspecs:
            by_mat.setdefault(key, []).append(i)
            by_cls.setdefault(_class_of(nm), []).append(i)

    def stats(idx):
        sel = np.isin(part_map, idx)
        n = int(sel.sum())
        if n < min_texels:
            return None
        sp = tspecs[table[idx[0] - 1]]
        if float(np.median(ppm_map[sel])) * float(sp.mottle_m) < cs2_compose.TONE_FLOOR:
            return None
        s0 = float(m_off[..., 3][sel].std())
        s1 = float(m_on[..., 3][sel].std())
        th = cs2_compose.MOTTLE_STD * float(sp.mottle_smooth)
        want = 0.9 * math.sqrt(s0 * s0 + th * th)
        deliv = math.sqrt(max(s1 * s1 - s0 * s0, 0.0)) / th if th > 0 else 0.0
        return {"texels": n, "s0": round(s0, 4), "s1": round(s1, 4), "want": round(want, 4),
                "delivered": round(deliv, 3), "ok": bool(s1 >= want)}
    for k, idx in sorted(by_mat.items()):
        st = stats(idx)
        if st is not None:
            out["material"][k] = st
    for c, idx in sorted(by_cls.items()):
        st = stats(idx)
        if st is not None:
            st.pop("ok")
            out["class"][c] = st
    return out


def weather_spec(spec, name, intensity):
    """FP6 + design 3.5: preset 'tower' (crashpad only), free slot wear, no snow."""
    import cs2_weather as CW
    PW = CW.PartWeather
    return CW.WeatherSpec.preset(
        "tower", name, intensity=intensity, slot_wear="free", snow=0.0,
        mat_roles_extra=(("paint", "paint_steel"), ("pad", "pad"), (LINER_KEY, "none"), (PLATE_KEY, "none"))
        + tuple(S.wheel_roles().items()),     # T62 round 2: one clean wheel on every family (+ the dress keys)
        overrides=(("pivot_", PW(role="galv", rust_source=True)),
                   ("axlecap_", PW(role="galv", rust_source=True)),
                   ("stiff_", PW(rust_source=True)),
                   ("jbox_", PW(role="galv", rust_source=True))))


def make_weather_post(inner, spec, name, keys, table, intensity, probe=()):
    """The weathering wrap (FP6 recipe) around the detail hook: stamped = the two plates PER
    PART (M3), protect_px = the warning triangle + the data-plate decal rects. `probe`: extra
    intensities evaluated on the SAME composed maps (the --weather-probe step: the family
    constant is the highest step every member keeps vanilla.ok); the shipped maps are the
    `intensity` run. Returns the post_compose callable (.report / .probe / inner attrs)."""
    import cs2_weather as CW

    def protect(mesh, A):
        return pad_triangle_rect(mesh, table, A) + list(inner.decal_rects)

    def run(cached, I):
        hk = CW.make_weather_hook(lambda *a: tuple(np.array(x, copy=True) for x in cached),
                                  weather_spec(spec, name, I), mat_keys=keys,
                                  detail_specs=inner.specs, stamped=["plate_n", "plate_p"],
                                  protect_px=protect)
        return hk

    def post(base, nrm, msk, ctl, ao, mesh, A):
        cached = inner(base, nrm, msk, ctl, ao, mesh, A)
        post.probe = {}
        for I in probe:
            hk = run(cached, I)
            hk(base, nrm, msk, ctl, ao, mesh, A)
            v = hk.report["vanilla"]
            post.probe[f"{I:.2f}"] = {"ok": v["ok"], "VL1": v["VL1_mean_rel_dL"],
                                      "VL2": v["VL2_share_rel_dL_gt_0.15"], "VL4": v["VL4_max"],
                                      "failed": v["failed"],
                                      "dLstar": hk.report["perceptual"]["mean_abs_dLstar"]}
        hk = run(cached, intensity)
        out = hk(base, nrm, msk, ctl, ao, mesh, A)
        post.report = hk.report
        post.ctl_in = cached[3]
        return out
    post.report = None
    post.probe = {}
    post.inner = inner
    return post


# --------------------------------------------------------------------------------------
# LOD chain + C11 / C12
# --------------------------------------------------------------------------------------

def lod_chain_v2(t, name, spec):
    l1p, l2p = lod_prefixes(spec)
    # T62 round 2: the wheels take their WHEEL_LOD forms (LOD1 8-segment flat, never decimated; LOD2 deleted by the
    # family tables); spec.lod1 / lod2 are then the targets of the REST, the LOD1 wheels come on top (v1: fixed None)
    lods = cs2_asset.make_lod_chain(t, name, targets=(spec.lod1, spec.lod2),
                                    delete_prefixes=(l1p, l2p), tol=LOD_TOL,
                                    shade_angle=SHADE_ANGLE_V2, fixed=S.lod_fixed())
    return lods


def assert_budgets(t, lods, spec):
    """C11 budgets and C12 LOD tri-only + bounds-top drift (D9 as corrected by M5)."""
    n0 = cs2_asset.tri_count(t)
    _check(n0 <= spec.cap, f"[{spec.key}] C11 LOD0 <= cap {spec.cap}", f"({n0})")
    dev = abs(n0 - spec.lod0) / spec.lod0
    if dev > 0.15:
        print(f"[tower2] WARN [{spec.key}] C11 LOD0 {n0} is {dev:.0%} off the target {spec.lod0}")
    else:
        print(f"[tower2] ok [{spec.key}] C11 LOD0 {n0} within 15 % of target {spec.lod0} ({dev:.1%})")
    out = {"tris": {0: n0}, "drift": {}}
    zt = max(v.co.z for v in t.data.vertices)
    for level, lod in enumerate(lods, start=1):
        n = cs2_asset.tri_count(lod)
        out["tris"][level] = n
        tri_only = all(len(p.vertices) == 3 for p in lod.data.polygons)
        _check(tri_only, f"[{spec.key}] C12 LOD{level} tri-only")
        drift = zt - max(v.co.z for v in lod.data.vertices)
        out["drift"][level] = round(drift, 5)
        if level == 1:
            _check(n <= 0.5 * n0, f"[{spec.key}] C11 LOD1 <= 50 % LOD0", f"({n} <= {0.5 * n0:.0f})")
            _check(abs(drift) <= 0.01, f"[{spec.key}] C12 LOD1 bounds-top drift <= 0.01", f"({drift:.4f} m)")
        else:
            _check(n <= spec.lod2 * 1.10, f"[{spec.key}] C11 LOD2 <= target x 1.10", f"({n} <= {spec.lod2 * 1.1:.0f})")
            exp = LOD2_DRIFT_EXPECT[spec.family]
            _check(abs(drift - exp) <= LOD2_DRIFT_RAISE,
                   f"[{spec.key}] C12 LOD2 bounds-top drift = expected {exp:.3f} +-{LOD2_DRIFT_RAISE}",
                   f"({drift:.4f} m)")
            if spec.family in GANTRY_FAMILIES:
                f5 = lod_keep_report(t, lod, spec)
                out["lod2_keep"] = f5
                _check(f5["deck_top_m2"] >= 0.95 * f5["deck_top_want_m2"],
                       f"[{spec.key}] C12 LOD2 keeps both deck tops (review F5)",
                       f"({f5['deck_top_m2']:.3f} of {f5['deck_top_want_m2']:.3f} m2)")
                if f5["braces_lod0"]:
                    _check(f5["braces"] == f5["braces_lod0"] and f5["brace_dz"] >= 0.9 * f5["brace_dz_lod0"],
                           f"[{spec.key}] C12 LOD2 keeps the rebuild head's A-braces (review F5)",
                           f"({f5['braces']} of {f5['braces_lod0']} parts, z extent {f5['brace_dz']:.3f} "
                           f"of {f5['brace_dz_lod0']:.3f} m)")
    return out


def lod_keep_report(t, lod, spec):
    """Review F5 data for one DQ LOD: up-facing deck-plate area at the deck top (vs the two
    kit plates' (L - 2 lip) x (strip - 2 lip)) and the gantry_brace parts with their z extent
    (rebuild heads; 0 on ingest heads, whose braces live inside gantry_00)."""
    table = json.loads(t.data[cs2_asset.PART_TABLE_PROP])
    me = lod.data
    part = np.zeros(len(me.polygons), dtype=np.int32)
    me.attributes["part"].data.foreach_get("value", part)
    deck_z = spec.axle + DQ_DECK
    area = 0.0
    for p, q in zip(me.polygons, part):
        nm = table[q - 1]
        if nm.startswith(DQ_LOD2_KEEP) and not nm.endswith("_lip") and p.normal.z > 0.9 \
                and abs(p.center.z - deck_z) <= 2e-3:
            area += p.area
    lip = 0.03                                           # cs2_kit.grating default
    y0, y1 = deck_y(spec)
    want = 2.0 * (dq_deck_length(spec) - 2 * lip) * (y1 - y0 - 2 * lip)

    def braces(obj):
        pv = {n: v for n, v in part_vertices(obj, table).items() if n.startswith("gantry_brace")}
        if not pv:
            return 0, 0.0
        z = np.concatenate(list(pv.values()))[:, 2]
        return len(pv), float(z.max() - z.min())
    nb0, dz0 = braces(t)
    nb, dz = braces(lod)
    return {"deck_top_m2": round(area, 4), "deck_top_want_m2": round(want, 4), "braces": nb,
            "braces_lod0": nb0, "brace_dz": round(dz, 4), "brace_dz_lod0": round(dz0, 4)}


# --------------------------------------------------------------------------------------
# Targeted preview views (M6): a generator-local camera helper on the cs2_preview rig
# --------------------------------------------------------------------------------------

def tower_views(spec):
    """Net-frame targeted views (the legacy render_preview set + the DQ head views)."""
    A = spec.axle
    v = [dict(name="detail_head", dir=(1.0, -1.10, 0.30), target=(0.0, 0.0, spec.top - 1.0),
              lens=85.0, dist=7.5 if spec.family == "FG" else 10.0),
         dict(name="pad_closeup", dir=(-0.45, -1.0, 0.30), target=(0.0, 0.0, 1.0), lens=85.0, dist=5.5),
         dict(name="pier_view", dir=(1.0, -1.0, 0.10), target=(0.0, 0.0, -0.35 * spec.pier),
              lens=42.0, dist=13.0 if spec.pier <= 4.0 else 17.0, ground=False, bounce=True)]
    if spec.family == "FG":
        v.append(dict(name="mast_head_closeup", dir=(0.85, 1.0, 0.30),
                      target=(0.0, 0.30, spec.tube_top - 0.25 - 1.05), lens=85.0, dist=5.0))
    else:
        v.append(dict(name="gantry", dir=(1.0, -1.1, 0.25), target=(0.0, 0.0, A + 0.8), lens=85.0, dist=9.0))
        v.append(dict(name="train", dir=(1.0, -0.45, 0.12), target=(spec.gauge / 2.0, 0.0, A + 0.05),
                      lens=85.0, dist=5.5))
        v.append(dict(name="ladder_landing", dir=(0.55, 1.0, 0.45), target=(0.0, 0.55, A - 0.2),
                      lens=60.0, dist=6.0))
    if spec.family == "GD":
        # Revision 1c: the middle sub-rocker's link from INBOARD and below (the "train" view
        # looks through the sheaves from outboard and hid the 0.125 m gap)
        v.append(dict(name="train_inboard", dir=(-0.45, -1.2, -0.10),
                      target=(spec.gauge / 2.0 - 0.17, 0.0, A + 0.05), lens=30.0, dist=1.35))
    # detail pass 1 (design 6.2): the tower plate (authoring +X face = net -Y), the base
    # (hazard band, data plate, conduit foot) and the sheave liners
    zp = spec.tube_top if spec.family == "FG" else A + 1.809 + head_kit(spec)["rise"]
    v.append(dict(name="plate", dir=(0.25, -1.0, 0.12), target=(0.0, -0.17, zp), lens=85.0, dist=1.8))
    v.append(dict(name="base_closeup", dir=(1.0, -0.85, 0.30), target=(0.30, -0.30, 1.75), lens=60.0,
                  dist=3.6))
    v.append(dict(name="train_liners", dir=(0.40, -1.0, 0.18), target=(spec.gauge / 2.0, 0.0, A),
                  lens=60.0, dist=2.6))
    return v


def render_tower_views(t, asset_dir, name, out_dir, views, resolution=(1300, 1000), extras=(), light=None):
    """
    M6: cs2_preview.preview frames whole-object views only (direction, auto distance), so
    the targeted closeups (target, lens, fixed distance, pier bounce light) run here on the
    same rig pieces (Nishita noon, pbr material from the shipped maps, slot tints). The
    rig restores the scene. Files: <name>_view_<view>.png. `extras` (phase 2 skinned views:
    the ropes) render beside `t` with their own materials (default none = as before). `light` (phase 2 round 2,
    SKIN_LIGHT; None = the noon rig as before): {elevation_deg, sun_energy, azimuth_deg (the sun's direction turned
    about Z from the sky's), ground_albedo, look (the AgX look)}.
    """
    import cs2_preview as P
    os.makedirs(out_dir, exist_ok=True)
    rig = P._Rig(t, None)
    for e in extras:
        e.hide_render = False
    written = []
    try:
        cam = P._make_camera(rig)
        sc = rig.scene
        if light is None:
            P._make_world(rig, **P.NOON)
        else:
            _w, sun = P._make_world(rig, light["elevation_deg"], light["sun_energy"])
            sun.rotation_euler = (mathutils.Matrix.Rotation(math.radians(light.get("azimuth_deg", 0.0)), 3, "Z")
                                  @ sun.rotation_euler.to_matrix()).to_euler()
        gnd = P._make_ground(rig)
        if light is not None and "ground_albedo" in light:
            gb = next(n for n in gnd.data.materials[0].node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            a = light["ground_albedo"]
            gb.inputs["Base Color"].default_value = (a, a, a, 1.0)
        P._assign(t, P._pbr_material(rig, asset_dir, name, None))
        bounce = None
        for v in views:
            sc.render.resolution_x, sc.render.resolution_y = resolution
            cam.data.lens = v["lens"]
            P._aim(cam, v["target"], v["dir"], v["dist"])
            gnd.hide_render = not v.get("ground", True)
            if v.get("bounce"):
                if bounce is None:
                    bpy.ops.object.light_add(type="SUN", location=(8.0, -8.0, -6.0))
                    bounce = rig.track(bpy.context.active_object)
                    bounce.data.energy = 2.0
                    bounce.rotation_euler = (math.radians(115.0), 0.0, math.radians(45.0))
                bounce.hide_render = False
            elif bounce is not None:
                bounce.hide_render = True
            path = os.path.join(out_dir, f"{name}_view_{v['name']}.png")
            written.append(P._render(rig, path, engine="EEVEE", look=(light or {}).get("look", "None")))
            print(f"[tower2] view -> {path}")
    finally:
        rig.restore()
    return written


# --------------------------------------------------------------------------------------
# Detail pass 1: report, asserts (DT-C7..C10 on the bake), the ETGD prefab block
# --------------------------------------------------------------------------------------

STEEL_SLOT1_COMPENSATED = (0.25, 0.26, 0.275)   # towers-transfer G5 (settled 2026-10-11)
DP1_PLATE_MIN = {"ppm": 300.0, "glyph_px": 44, "cap_px": 36}   # DT-C7 (tube towers)


def _edge_fraction(cov):
    """Share of a stamp's EDGE texels that are fractional (anti-aliased): fractional texels
    over fractional + full texels touching a texel below full (DT-C7 >= 0.20)."""
    full = cov >= 0.99
    frac = (cov > 0.01) & ~full
    nb = np.zeros_like(full)
    pad = np.pad(cov < 0.99, 1, constant_values=True)
    for dr, dc in ((0, 1), (2, 1), (1, 0), (1, 2)):
        nb |= pad[dr:dr + cov.shape[0], dc:dc + cov.shape[1]]
    edge_full = full & nb
    n = int(frac.sum()) + int(edge_full.sum())
    return round(float(frac.sum()) / n, 3) if n else 0.0


def detail_pass_report(spec, compose, post, intensity):
    """The bake's detail-pass numbers (manifest `detail_pass_1`)."""
    rel = compose.relief or {}
    pad = compose.pad or {}
    rep = {"relief": rel, "tone": compose.tone, "tone_gate": compose.tone_gate,
           "tone_gate_ok": bool((compose.tone_gate or {}).get("material"))
                           and all(v["ok"] for v in compose.tone_gate["material"].values()),
           "plates": compose.plates, "decal_rects": len(compose.decal_rects or []),
           "numeral": plate_numeral(spec), "subtexel_fill": dict(compose.subtexel or {})}
    if pad:
        tri, band, yc = pad["tri"], pad["band"], pad["yellow"]
        full_band = band >= 0.99
        rep["pad"] = {"triangle_texels": int((tri > 0.01).sum()),
                      "triangle_edge_fractional": _edge_fraction(tri),
                      "band_texels": int(full_band.sum()),
                      "band_yellow": int((full_band & (yc / np.maximum(band, 1e-6) > 0.99)).sum()),
                      "band_black": int((full_band & (yc / np.maximum(band, 1e-6) < 0.01)).sum())}
    if post is not None and post.report is not None:
        r = post.report
        v = r["vanilla"]
        rep["weather"] = {"intensity": intensity, "slot_wear": r["slot_wear"], "snow": r["snow"],
                          "ok": v["ok"], "failed": v["failed"], "VL1": v["VL1_mean_rel_dL"],
                          "VL2": v["VL2_share_rel_dL_gt_0.15"], "VL3": v["VL3_mean_dsmooth"],
                          "VL4": v["VL4_max"], "dLstar": r["perceptual"]["mean_abs_dLstar"],
                          "share_dLstar_gt_2": r["perceptual"]["share_dLstar_gt_2"],
                          "G5_ctl_identity": r["guards"]["G5_ctl_identity"],
                          "G6_protect_identical": r["guards"]["G6_protect_identical"],
                          "roles": r["roles"], "protect_texels": r["protect_texels"]}
        if post.probe:
            rep["weather_probe"] = post.probe
    return rep


def assert_detail_pass(spec, rep, strict_weather=True, shipping=True):
    """DT-C7 (stamps), DT-C8 (relief drawn, 0 floor skips), DT-C9 (M7 tone gate), DT-C10
    (weathering vanilla.ok at the family intensity, ControlMask untouched, protected texels
    identical). A failing bake raises: nothing is exported."""
    tag = f"[{spec.key}]"
    if not shipping:
        w = rep.get("weather")
        if w is not None:
            _check(w["G5_ctl_identity"] and w["G6_protect_identical"],
                   f"{tag} DT-C10 weather guards (non-shipping atlas)")
        return
    sk = (rep["relief"] or {}).get("skipped") or {}
    _check(not sk, f"{tag} DT-C8 relief: 0 texel-floor skips",
           f"(skipped {sk}; parts {(rep['relief'] or {}).get('skipped_parts')})")
    dr = (rep["relief"] or {}).get("drawn") or {}
    # T62 round 2: "marks:ring" was the sheave hub ring only; the v2 wheel's hub is a geometric boss (v1 still needs it)
    need = ["bolts", "edge_round", "marks:band"] + ([] if S.wheels_v2() else ["marks:ring"])         + (["marks:boltring"] if _joint_zs(spec, spec.tube_top) else [])
    miss = [k for k in need if not dr.get(k)]
    _check(not miss, f"{tag} DT-C8 relief kinds drawn {need}", f"(missing {miss}; drawn {dr})")
    _check(rep["tone_gate_ok"], f"{tag} DT-C9 (M7) MaskMap A std with tone >= 0.9 sqrt(s0^2 + (0.234 smooth)^2)",
           f"({ {k: (v['s0'], v['s1'], v['want']) for k, v in ((rep['tone_gate'] or {}).get('material') or {}).items()} })")
    pl = rep["plates"] or []
    _check(len(pl) == 2, f"{tag} DT-C7 two plates stamped per part (M3)", f"({len(pl)})")
    for p in pl:
        _check(p["ppm"] >= DP1_PLATE_MIN["ppm"] and p["glyph_px"] >= DP1_PLATE_MIN["glyph_px"]
               and (p["numeral"] is None or p["cap_px"] >= DP1_PLATE_MIN["cap_px"]),
               f"{tag} DT-C7 plate >= 300 px/m, glyph >= 44 px, numeral cap >= 36 px",
               f"({p['ppm']} px/m, glyph {p['glyph_px']}, cap {p['cap_px']}, '{p['numeral']}')")
    pad = rep.get("pad") or {}
    _check(pad.get("band_yellow", 0) > 0 and pad.get("band_black", 0) > 0
           and pad.get("triangle_edge_fractional", 0.0) >= 0.20,
           f"{tag} DT-C7 hazard band yellow + black, triangle edge >= 20 % fractional",
           f"(yellow {pad.get('band_yellow')}, black {pad.get('band_black')}, "
           f"AA {pad.get('triangle_edge_fractional')})")
    _check(rep["decal_rects"] > 0, f"{tag} data plate decal drawn", f"({rep['decal_rects']} rects)")
    sf = rep.get("subtexel_fill") or {}
    _check(sf.get("black_after") == 0 and not sf.get("slivers_unfilled"),
           f"{tag} DT-F3 no triangle of any UV area overlaps a black texel in the written BaseColor",
           f"(F3 filled {sf.get('faces')} faces / {sf.get('texels')} texels; F3b filled {sf.get('f3b_faces')} faces / "
           f"{sf.get('f3b_texels')} texels; unfillable {sf.get('slivers_unfilled')}; black after "
           f"{sf.get('black_after')} {sf.get('black_after_faces')})")
    w = rep.get("weather")
    if w is not None:
        _check(w["G5_ctl_identity"] and w["G6_protect_identical"] and w["slot_wear"] == "free" and w["snow"] == 0.0,
               f"{tag} DT-C10 weather guards: ControlMask untouched, protected texels identical, free, no snow",
               f"(G5 {w['G5_ctl_identity']}, G6 {w['G6_protect_identical']})")
        if strict_weather:
            _check(w["ok"], f"{tag} DT-C10 weather vanilla budget ok at {w['intensity']}",
                   f"(failed {w['failed']}, VL1 {w['VL1']}, VL2 {w['VL2']}, VL4 {w['VL4']})")


def etgd_prefab_block(spec, name, info):
    """Surgery numbers for the gondola entry compression tower (towers-transfer G2), the ETDQ
    pattern: a hosted object (UtilityObject + StandingObject ONLY, never PillarObject:
    PIPELINE 6), the station carries the anchors. New block (not frozen)."""
    return {
        "tower_prefab": "SkiLiftTerminalTowerGD", "mesh": name, "placeholder": None,
        "copy_from": "SkiLiftTerminalTowerDQ (the ETDQ prefab, itself SkiLiftTerminalTowerBase): "
                     "UtilityObject + StandingObject only, never PillarObject",
        "utility_object": {"type": 64, "position": [0.0, 0.0, 0.0]},
        "stubs": [], "stubs_note": "none on the tower: SkiGondolaBaseStationBuilding's ObjectSubLanes carry the "
                                   "HV anchors (x +-3.00, y 4.85 -> 4.88, z -11.125 -> -10.875, lane "
                                   "SkiGondolaHaulRope) and the LV comm (dq-terminal.md 12)",
        "standing_object": {"m_LegSize": [spec.pad, spec.tube_top, spec.pad],
                            "m_CircularLeg": "copy the template",
                            "note": "y = tube top (axle 4.85 + 0.50), the towers-transfer G4 pattern"},
        "hosting": {"station": "SkiGondolaBaseStationBuilding (gondola BASE only)",
                    "position": [0.0, 0.0, spec.hosted_z], "rotation_quat": [0, 1, 0, 0]},
        "rope_y_required": spec.axle, "gauge_m": spec.gauge, "hang_m": abs(GD_HANGER_COL[1]["z0"]),
        "bounds_top": round(spec.top, 6), "raw_bounds_min_y": -spec.pier, "clamp_bounds_min_y": -0.01,
        "station_inboard_z": info["contracts_authoring"].get("station_inboard_z"),
        "station_keepout": {"x_max_authoring": GD_STATION_INBOARD_MAX, "abs_y_max": 4.60, "z_max": 7.0,
                            "source": "lift_terminal_detach.GD_TOWER_KEEPOUT"},
        "color_slot1_default": list(STEEL_SLOT1_COMPENSATED),
        "color_slot2_default": list(cs2_asset.PAD_RGB),
        "color_note": "slot 1 = the compensated dark steel (towers-transfer G5); LOD0 mesh prefab only",
    }


# --------------------------------------------------------------------------------------
# Entry point (v2)
# --------------------------------------------------------------------------------------

def parse_args_v2(argv):
    ap = argparse.ArgumentParser(prog="lift_tower.py (v2)")
    ap.add_argument("--variant", required=True, choices=sorted(VARIANTS))
    ap.add_argument("--out", required=True, help="letter folder; asset folder = <out>/<Name>")
    ap.add_argument("--letter", default=V2_LETTER)
    ap.add_argument("--name", default=None, help="override (A/B packs); validate_asset_name applies")
    ap.add_argument("--head", default=None, choices=("ingest", "rebuild"), help="DQ only")
    ap.add_argument("--kagelok", default=KAGELOK_BLEND)
    ap.add_argument("--preview", default=None, help="preview PNG folder")
    ap.add_argument("--ab", default=None,
                    help="previous generation asset folder for the ab frames (opt-in: O14 lays "
                         "below-grade N towers on their back)")
    ap.add_argument("--review", nargs="?", const="", default=None,
                    help="write a cs2_review pack (default dir assets/preview/review)")
    ap.add_argument("--export", action="store_true",
                    help="required to write into the assets/import shipping tree")
    ap.add_argument("--finish", dest="finish", action="store_true", default=True)
    ap.add_argument("--no-finish", dest="finish", action="store_false")
    ap.add_argument("--lods", dest="lods", action="store_true", default=True)
    ap.add_argument("--no-lods", dest="lods", action="store_false")
    ap.add_argument("--no-validate", action="store_true")
    ap.add_argument("--atlas", type=int, default=ATLAS_V2)
    ap.add_argument("--ao-samples", type=int, default=AO_SAMPLES_V2)
    ap.add_argument("--weather", choices=("on", "off"), default="on",
                    help="detail pass 1 weathering wrap (FP6); off = the inner hook only (G5 check)")
    ap.add_argument("--weather-i", type=float, default=None,
                    help="override the family intensity WEATHER_I (probe / A-B only)")
    ap.add_argument("--weather-probe", action="store_true",
                    help="also evaluate WEATHER_PROBE intensities on the same composed maps "
                         "(manifest weather_probe; the family constant's evidence)")
    ap.add_argument("--rope-on-crown", dest="rope_on_crown", action="store_true",
                    help="T27 preview flag (ROPE_ON_CROWN): the manifest hands the lane on the sheave crown to the "
                         "surgery and the carrier contracts sweep there; the mesh is unchanged")
    ap.add_argument("--wheel-dress", dest="wheel_dress", action=argparse.BooleanOptionalAction, default=None,
                    help="the wheel dress (dark wheel rubber in the groove, a recessed web ring and a raised shoulder "
                         "lip; cs2_sheaves.WHEEL_DRESS) - the same mesh, 0 extra tris; default: on with --skinned")
    ap.add_argument("--wheels", choices=("v1", "v2"), default="v2",
                    help="T62 sheave wheel generation: v2 (default) = cs2_sheaves.wheel, the 16-segment grooved wheel "
                         "(round 3) on every head; v1 = the pre-T62 per-family wheels, byte-identical")
    ap.add_argument("--skinned", action="store_true",
                    help="lift alignment phase 2 (SKINNED): every LOD skinned to Root / TrainFront / TrainBack, two half "
                         "trains a lane hinged on the rope line; implies --rope-on-crown; FG Yan / DQ ingest / GD "
                         "rebuild heads only")
    ap.add_argument("--skin-views", default=None,
                    help="with --skinned: folder for the round 2 views (skin_views: level, slope, crest, the 20 deg "
                         "limit, steep, dip), each half's rope drawn (needs the finish)")
    ap.add_argument("--gd-sheaves", dest="gd_sheaves", type=int, choices=(8, 10, 12), default=None,
                    help="with --skinned: GD sheaves a side (SKIN_GD sheaves, default 8); 10 = the shipped count, each "
                         "half a 4-wheel bogie + its inner wheel on a 1-wheel rocker hung on a link")
    return ap.parse_args(argv)


def main_v2(argv):
    import time
    t0 = time.perf_counter()
    args = parse_args_v2(argv)
    S.set_wheels(args.wheels)          # T62: before any part is built
    # the wheel dress (materials + relief rings, 0 tris): on by default for skinned builds, unskinned stay as shipped
    S.set_wheel_dress(args.skinned if args.wheel_dress is None else args.wheel_dress)
    if args.rope_on_crown:
        set_rope_on_crown(True)        # T27: lanes on the crowns; the compression terminal heads (ETDQ / ETGD) rise
    if args.skinned:
        set_skinned(True)              # phase 2: the pinned trains, the rope on the crowns (the Train pivot line)
    elif args.skin_views or args.gd_sheaves:
        raise SystemExit("--skin-views / --gd-sheaves need --skinned")
    if args.gd_sheaves:
        SKIN_GD["sheaves"] = args.gd_sheaves      # round 4: the GD 10-a-side option (the default stays 8)
    spec = skin_spec(VARIANTS[args.variant])      # SKINNED GD: SKIN_GD sheaves a side
    if args.head is not None and spec.family == "FG":
        raise SystemExit(f"--head applies to DQ variants only ({spec.key} is FG)")
    head = args.head or ("yan" if spec.family == "FG" else spec.heads[0])
    if args.skinned and head not in SKIN_HEADS.get(spec.family, ()):
        raise SystemExit(f"--skinned: no skin recipe for the {spec.family} head {head!r} (SKIN_HEADS {SKIN_HEADS})")
    name = cs2_asset.validate_asset_name(args.name or f"{spec.name_stem}{args.letter}")
    import cs2_validate
    rep = cs2_validate._Report(name)
    cs2_validate.lint_asset_name(name, rep)
    if any(f[0] == "ERROR" for f in rep):
        raise SystemExit(f"asset name {name!r} fails lint_asset_name")
    out = os.path.abspath(args.out)
    shipping = os.path.normcase(os.path.join(_REPO, "assets", "import"))
    if os.path.normcase(out).startswith(shipping) and not args.export:
        raise SystemExit(f"--out {out} is in the shipping tree: pass --export (after the verdict)")
    asset_dir = os.path.join(out, name)
    if args.finish:
        os.makedirs(asset_dir, exist_ok=True)

    clear_scene()
    t, table, sources, info = build_tower_v2(spec, head, finish=args.finish, kagelok=args.kagelok,
                                             name=name)
    n0 = cs2_asset.tri_count(t)
    print(f"[tower2] {name}: {spec.family} {spec.role} {spec.kind}, head {head}, {len(table)} parts, "
          f"LOD0 {n0} tris, axle {spec.axle}, lanes +-{spec.gauge / 2}, top {spec.top}, pier {spec.pier}")

    bake = None
    compose = None
    post = None
    w_int = WEATHER_I[spec.family] if args.weather_i is None else float(args.weather_i)
    if args.finish:
        import cs2_bake
        keys = [m.name[len(cs2_bake.SRC_MAT_PREFIX):] for m in t.data.materials]   # BEFORE the bake
        compose = make_post_compose_v2(t, table, spec, info)
        post = compose
        if args.weather == "on":
            post = make_weather_post(compose, spec, name, keys, table, w_int,
                                     probe=WEATHER_PROBE if args.weather_probe else ())
        bake = cs2_bake.bake_unique(t, asset_dir, name, args.atlas, sources, reserved=(),
                                    margin_px=PACK_MARGIN_V2, ao_samples=args.ao_samples,
                                    post_compose=post, src_uv="UVMap")
        dp1 = detail_pass_report(spec, compose, post if args.weather == "on" else None, w_int)
        # F3 contract, measured on the WRITTEN BaseColor (after the weather layer and the stamps)
        bad = subtexel_black_faces(t.data, cs2_validate.load_png_pixels(
            os.path.join(asset_dir, f"{name}_BaseColor.png")))
        dp1["subtexel_fill"]["black_after"] = len(bad)
        dp1["subtexel_fill"]["black_after_faces"] = bad[:8]
        print(f"[tower2] detail pass 1: relief {dp1['relief']}; tone {dp1['tone']}; plates "
              f"{[(p['glyph_px'], p['cap_px'], p['ppm']) for p in dp1['plates']]}; tone gate "
              f"{dp1['tone_gate_ok']}; weather {dp1.get('weather')}")
        # the DT-C7 / C8 / C10 gates are defined at the shipping atlas (texel floors, 300 px/m
        # plates, the vanilla budget); test runs at --atlas 256 check the guards only
        assert_detail_pass(spec, dp1, strict_weather=args.weather == "on" and args.weather_i is None,
                           shipping=args.atlas == ATLAS_V2)
    lods = []
    if args.lods:
        lods = lod_chain_v2(t, name, spec)
    if args.finish and lods and bake is not None and args.atlas == ATLAS_V2:
        # DT-F3 at LOD1 / LOD2 as well (their triangles are re-cut from LOD0's UVs, so they can overlap
        # a texel LOD0's triangles do not): the same counter on the same written BaseColor
        base_px = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{name}_BaseColor.png"))
        lod_bad = {i: subtexel_black_faces(lod.data, base_px) for i, lod in enumerate(lods, start=1)}
        dp1["subtexel_fill"]["black_after_lods"] = {str(i): len(b) for i, b in lod_bad.items()}
        _check(not any(lod_bad.values()),
               f"[{spec.key}] DT-F3 no LOD1 / LOD2 triangle overlaps a black texel in the written BaseColor",
               f"({dp1['subtexel_fill']['black_after_lods']} {[b[:4] for b in lod_bad.values()]})")
    budget = assert_budgets(t, lods, spec)
    arm = None
    if SKINNED:
        # phase 2: one rig for every level, each vertex 1.0 on its part's bone (the 'part' attribute rides the chain)
        # (the rig is built in the net frame the meshes' vertices are in: their object transforms must be identity)
        if any(o.matrix_world != mathutils.Matrix.Identity(4) for o in [t] + lods):
            raise TowerContractError(f"{name}: a level carries an object transform; the rig assumes none")
        arm = build_skin_armature(spec, name + "Rig")
        info["skin"]["weights"] = {str(i): skin_assign(o, table, info["skin_part_bone"])
                                   for i, o in enumerate([t] + lods)}
        for o in [t] + lods:
            cs2_asset.check_skin_weights(o, arm)
        print(f"[tower2] skin: TrainFront / TrainBack heads game (0, {lane_y(spec, True)}, 0); weights per level "
              f"{info['skin']['weights']}")
    if not args.finish:
        print(f"[tower2] --no-finish: geometry, contracts and LOD chain only; nothing exported")
        print(f"PASS lift_tower v2 (no finish): {name} tris {budget['tris']}")
        return 0
    if arm is None:
        cs2_asset.export_fbx(t, os.path.join(asset_dir, f"{name}.fbx"))
        for i, lod in enumerate(lods, start=1):
            cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{i}.fbx"))
        cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    else:
        cs2_asset.export_fbx_skinned(t, arm, os.path.join(asset_dir, f"{name}.fbx"))
        for i, lod in enumerate(lods, start=1):
            cs2_asset.export_fbx_skinned(lod, arm, os.path.join(asset_dir, f"{name}_LOD{i}.fbx"))
        cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)),
                                 procedural_animation=True)
        info["skin"]["roundtrip"] = skin_roundtrip(asset_dir, name, len(lods), spec)

    import cs2_preview
    _c, size = world_bounds(t)
    area = float(cs2_uv.face_world_areas(t.data).sum())
    switch = cs2_preview.lod_switch_table(size, [budget["tris"][k] for k in sorted(budget["tris"])],
                                          area=area) if lods else None
    previews = []
    if args.preview:
        prev = os.path.abspath(args.preview)
        # ab is opt-in: cs2_preview.import_generation guesses Y-up from min y ~ 0, so the
        # N towers (pier to -4.0) come back lying on their back (p0a-integration O14).
        ab = args.ab
        modes = ["pbr", "dusk", "normals", "density"] + (["lod"] if lods else []) + (["ab"] if ab else [])
        previews += cs2_preview.preview(t, asset_dir, name, prev, modes=tuple(modes),
                                        views=list(cs2_preview.DEFAULT_VIEWS), lods=lods or None,
                                        prev_generation=ab)
        previews += render_tower_views(t, asset_dir, name, prev, tower_views(spec))
        t.data.materials.clear()
    if args.skin_views:
        previews += render_skin_views(t, arm, spec, asset_dir, name, os.path.abspath(args.skin_views))
    contracts = {"axle": spec.axle, "lane_x": spec.gauge / 2.0,
                 "h_top": round(info["contracts_authoring"]["top"] - spec.axle, 5),
                 "top_anchor_y": spec.top, "pier": spec.pier, "comm_lv_y": spec.comm_y}
    pack = None
    if args.review is not None:
        import cs2_review
        pack = cs2_review.write_pack(
            name, asset_dir, dict(enumerate([t] + lods)), {},
            meta={"generator": "lift_tower.py", "args": vars(args), "kind": "tower",
                  "caps": {0: spec.cap, 1: spec.lod1, 2: spec.lod2}, "contracts": contracts,
                  "head": head},
            out_dir=args.review or None)
    for lod in lods:
        me = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(me)

    import cs2_review
    derived = None
    if spec.family in GANTRY_FAMILIES:
        derived = {"source": "Kagelok 'Chair Lift' (CC BY 4.0)", "uid": KAGELOK_UID, "url": KAGELOK_URL,
                   "mode": "derived geometry (line head kit)" if head == "ingest" else "traced proportions"}
        if spec.family == "GD":
            derived["mode"] = "traced proportions (the DQ rebuild head kit, stretched to the 6.0 m gauge)"
    density = compose.density if compose is not None else None
    manifest = {
        "generator": f"lift_tower.py@{cs2_review.git_sha()}",
        "spec": "towers-v2 + skeptic" if spec.family != "GD" else
                "towers-v2 + skeptic; Revision 1b GD (tram_tower.GD / lattice-towers D2 contract)",
        "variant": spec.key, "family": spec.family, "role": spec.role, "kind": spec.kind,
        "head": head, "derived_from": derived, "tris": budget["tris"], "lod_drift_top_m": budget["drift"],
        "parts": len(table), "axle": spec.axle, "gauge": spec.gauge, "h_top": contracts["h_top"],
        "bounds_m": {"min_z": info["contracts_net"]["min"], "max_z": info["contracts_net"]["top"],
                     "lateral": info["contracts_net"]["lateral"], "travel": info["contracts_net"]["travel"]},
        "top_anchor_y": spec.top, "pier": spec.pier, "comm_lv_y": spec.comm_y,
        "atlas": args.atlas, "ao_samples": args.ao_samples, "pack_margin_px": PACK_MARGIN_V2,
        "sheave_profile": "lite (lift_tower.lite_sheave, 6 x segs tris)",
        **({"wheels": {"generation": "v2 (T62 round 3: cs2_sheaves.wheel, 16 segments, groove, seen hub bosses)",
                       "builder": S.WHEEL_ID, "r": spec.sheave_r, "w": spec.sheave_w, "segs": sheave_segs(spec),
                       "profile": dict(S.WHEEL_PROFILE), "tris_per_wheel_max": S.WHEEL_TRIS,
                       "discs": "n-gon", "groove": "V (max(0.02, 0.18 r))", "hub_boss": "seen faces only",
                       "material": S.WHEEL_KEY, "lod": {str(k): v["form"] for k, v in S.WHEEL_LOD.items()}}}
           if S.wheels_v2() else {}),
        "sheaves_per_side": spec.sheaves, "train_sag_m": spec.sag,
        "density_worst": [{"part": table[p - 1] if 0 < p <= len(table) else p,
                           "source_px_per_m": round(s, 1), "shipped_px_per_m": round(d, 1)}
                          for p, s, d in (density or [])[:8]],
        "lod_switch_m": switch, "ingest": info.get("ingest"),
        "bake": {k: bake[k] for k in ("A", "margin_px", "ao", "uncovered_texels")} if bake else None,
        "prefab_notes": {
            "size_y": "ObjectUtils.GetSize y = bounds.max.y (1.6.0f1 ObjectUtils.cs:51-58)",
            "switch_points": switch_points_note("GD" if spec.family == "GD" else None,
                                                post_freeze=POST_FREEZE_STAGE.get(spec.key, 0)),
            "leg_size_y": "StandingObject.m_LegSize.y = leg zone above the origin "
                          "(ObjectInitializeSystem.cs:1293); pick it on purpose (FG/ET use the mast height)",
        },
    }
    if ROPE_ON_CROWN:
        manifest["rope_on_crown"] = rope_on_crown_block(spec)
    if SKINNED:
        manifest["skin"] = skin_manifest_block(spec, info["skin"])
    if spec.family == "DQ" and spec.role == "line":
        manifest["catalog_handoff"] = {"AimHeightM": DQ_AIM_HEIGHT_M, "piece_lanes": [2.6, 8.0],
                                       "piece_comm_lv_y": 9.2,
                                       "line_net_subobject_y": VARIANTS["DQ10"].top,
                                       "member_axle_y": spec.axle, "member_comm_lv_y": spec.comm_y}
    if spec.family == "GD" and spec.role == "line":
        A = spec.axle
        manifest["catalog_handoff"] = {"AimHeightM": GD_AIM_HEIGHT_M, "piece_lanes": [GD_LANE, VARIANTS["GD12"].axle],
                                       "piece_comm_lv_y": VARIANTS["GD12"].comm_y,
                                       "line_net_subobject_y": VARIANTS["GD12"].top,
                                       "member_axle_y": A, "member_comm_lv_y": spec.comm_y}
        manifest["gd_numbers"] = gd_numbers(spec, info["contracts_authoring"])
        manifest["prefab"] = {
            "tower_prefab": f"SkiLiftTower{spec.key}", "mesh": name, "placeholder": PLACEHOLDER["GD"],
            "copy_from": "SkiLiftTowerDQ16 (tubular sibling) with the GD stubs of SkiGondolaLattice25",
            "utility_object": {"type": 8, "position": [0.0, A, 0.0]},
            "stubs": [{"lane": "HV haul", "lane_prefab": GD_STUB_LANES["hv"][0], "lane_fallback": GD_STUB_LANES["hv"][1],
                       "pos": [-GD_LANE, A, 0.0], "node_index": [0, 0]},
                      {"lane": "HV haul", "lane_prefab": GD_STUB_LANES["hv"][0], "lane_fallback": GD_STUB_LANES["hv"][1],
                       "pos": [GD_LANE, A, 0.0], "node_index": [1, 1]},
                      {"lane": "LV comm", "lane_prefab": GD_STUB_LANES["lv"][0], "lane_fallback": GD_STUB_LANES["lv"][1],
                       "pos": [0.0, spec.comm_y, 0.0], "node_index": [2, 2]}],
            "standing_object": {"m_LegSize": [spec.pad, spec.pier, spec.pad], "m_CircularLeg": False,
                                "m_LegGap": [0, 0], "note": "SkiLiftTowerFG12 pattern (pad, pier, pad) = (1.8, 4, 1.8)"},
            "pillar_object": {"m_Type": 2, "m_AnchorOffset": 0, "m_VerticalPillarOffsetRange": [-1, 1]},
            "terraform_override": "copy SkiLiftTowerFG12",
            "bounds_top": spec.top, "raw_bounds_min_y": -spec.pier, "clamp_bounds_min_y": -0.01,
            "color_slot1_default": list(cs2_asset.STEEL_DEFAULT_RGB),
            "net_subobjects": {"line": "SkiGondolaHV", "m_Position": [0.0, VARIANTS["GD12"].top, 0.0],
                               "m_Placement": 0, "m_AnchorTop": False,
                               "note": "spec y = the GD12 bounds top 12.000 (tram_tower.GD tubular_tops[0])"},
            "elevation_range": [0, 28.0],
            "piece_lanes": {"hv": [GD_LANE, VARIANTS["GD12"].axle], "lv_comm": [0.0, VARIANTS["GD12"].comm_y]},
        }
    if spec.family == "GD" and spec.role == "terminal":
        manifest["gd_numbers"] = gd_numbers(spec, info["contracts_authoring"])
        manifest["prefab"] = etgd_prefab_block(spec, name, info)
    if spec.key in R1_NEW_ROWS or spec.key in R1B_NEW_ROWS or spec.key in POST_FREEZE:
        sc = pillar_scoring(spec.family, post_freeze=POST_FREEZE_STAGE.get(spec.key, 0))
        row = next(m for m in sc["members"] if m["variant"] == spec.key)
        manifest["placeholder_row_new"] = {"row": row, "family": sc}
        print(f"[tower2] placeholder row NEW: {sc['placeholder']} <- {row['member_prefab']} "
              f"(mesh {row['mesh']}), top-anchor y {row['top_anchor_y']}, UtilityObject y "
              f"{row['utility_object_y']}, LV {row['lv_comm_y']}; line NetSubObjects y stays "
              f"{sc['net_subobject_y']}; wins e {row['wins_e_m']}; switch points {sc['switch_points_e_m']}")
        for m in sc["members"]:
            print(f"[tower2]   {m['variant']:5s} top-anchor y {m['top_anchor_y']:7.3f}  base node-{m['base_below_node_m']:.3f}"
                  f"  wins e {m['wins_e_m']}")
    if spec.role == "terminal" and spec.family in GANTRY_FAMILIES:
        manifest["hosting"] = {"station": "detachable BASE only" if spec.family == "DQ" else "gondola BASE only",
                               "position": [0.0, 0.0, spec.hosted_z],
                               "rotation_quat": [0, 1, 0, 0], "rope_y": spec.axle,
                               "head_comm_stub": None,
                               "station_inboard_z": info["contracts_authoring"].get("station_inboard_z")}
    if args.finish:
        manifest["detail_pass_1"] = dp1
        if post is not None and post is not compose:
            import cs2_weather
            wrep = json.loads(cs2_weather.report_json(post.report))
            for k in ("seconds", "fields_seconds"):          # timings: not part of a reproducible manifest
                wrep.pop(k, None)
            manifest["weather"] = wrep
            if post.probe:
                manifest["weather_probe"] = post.probe
    cs2_validate.write_manifest(asset_dir, name, manifest)
    n_err = n_warn = None
    if not args.no_validate:
        t.name, t.data.name = name + "Src", name + "Src"
        findings = cs2_validate.validate_asset_folder(asset_dir, name, {
            "kind": "tower", "lod0_cap": spec.cap, "lod1_ratio": 0.5,
            "lod2_cap": int(round(spec.lod2 * 1.10)), "hero": False, "expected_lods": (1, 2) if lods else (),
            "top_y": spec.top, "pier": spec.pier})
        n_err = sum(1 for f in findings if f[0] == "ERROR")
        n_warn = sum(1 for f in findings if f[0] == "WARN")
        print(f"[tower2] validate_asset_folder: {n_err} errors, {n_warn} warnings")
    print(f"[tower2] asset folder: {asset_dir}" + (f"; review pack {pack}" if pack else ""))
    print(f"PASS lift_tower v2: {name} head {head} tris {budget['tris']} drift {budget['drift']} "
          f"top {spec.top} axle {spec.axle} ({time.perf_counter() - t0:.0f} s)")
    return 0


def _entry():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--legacy" in argv:
        sys.argv = [a for a in sys.argv if a != "--legacy"]
        main()
        return
    try:
        main_v2(argv)
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        import traceback
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    _entry()
