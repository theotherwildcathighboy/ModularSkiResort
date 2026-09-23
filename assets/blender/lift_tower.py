"""
Procedural chairlift tower generator for the Modular Ski Resort mod.

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
    """All tower dimensions in metres. CS2 uses 1 unit = 1 metre, same as Blender default."""

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
    0.40 over 0.50 m, centred at axle - 0.42); its skirt passed straight through the
    service ladder, whose rails run up the -X face at x = -(mast_radius + 0.10) = -0.48.
    The assembly drawing's real part is a bent plate bolted AROUND the mast - and on a
    real tower it stops at the ladder, exactly like our crash pad's open back.

    So: a conical shell arc of 280 degrees, gap centred on -X. The gap's 80-degree
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
    # maintenance). Two posts on the arm's centreline plus a top bar spanning them.
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
    # centreline (img_6914: the comm/power line rides over every tower top; the wire
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
    centre lands exactly on the +X (uphill) face. Inner wall, rims and end caps map to
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
    via the pad strip - recolourable on its own slot (2), default orange-red, with the
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
# v 0.5 is near-black, and bilinear/mip filtering averages neighbours across the quad
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
    noise only. Not recolourable (ctl zeroed).
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

    # join() inherits the origin of the first object joined - the foundation pad, whose centre
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
            # exactly. MID-ATLAS strips (the lodge's wood/stone bands) are honoured too:
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
    the FOV maths is unambiguous rather than depending on Blender's AUTO heuristics.
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
        help="mast height in metres, overriding TowerParams.mast_height (default 8.0 - "
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
    print(f"[tower] CS2 m_Bounds should match these metres once unit scale is right")
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
    # Shared lift atlas: dark painted steel (recolourable slot 1) for mast/crossarm/rockers,
    # galvanized photo steel for sheaves/ladder, tinted concrete for the pad. Matches the
    # stations so the whole lift line reads as one machine.
    # post=paint_pad_strip: the crash-pad material + warning triangle live in the strip
    # reserved out of the glass quadrant (authored in cs2_asset, slot-2 recolourable).
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


if __name__ == "__main__":
    main()
