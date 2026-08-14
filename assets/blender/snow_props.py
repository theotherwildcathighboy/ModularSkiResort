"""
snow_props.py — piste-edge snowmaking props (generation A).

    blender --background --factory-startup --python-exit-code 1 \
            --python snow_props.py -- --out ../import/props/A --variants
    blender --background --factory-startup --python snow_props.py -- --lineup ../preview
    blender --background --factory-startup --python snow_props.py -- --row ../preview

ALWAYS PASS --python-exit-code 1 WHEN EXPORTING. Blender swallows a Python exception in
background mode and still exits 0: the texture-budget guard below fired on a real
over-spend and the build reported success anyway. Without that flag a failing guard is
indistinguishable from a passing one.

The family placed automatically along ski-run borders by the vanilla per-vertex spawner
(DECISIONS 71/73). Four assets this round, in the build order the design spec argues for:

    tower    SkiSnowGunTowerA   fan head on a mast — reads at max camera distance
    hydrant  SkiHydrantBoxA     orange valve box + standpipe — cheapest realism in the pack
    lance    SkiSnowGunLanceA   bare pole + tiny head — the anti-repetition silhouette
    fan      SkiSnowGunFanA     carriage gun — the hero close-up

Dimensions come from docs/research/art/snowmaking-props-spec.md (manufacturer-documented:
TechnoAlpin TF10/TT10/TL8, SMI, DemacLenko). Budgets from
docs/research/art/snow-gun-pipeline-plan.md, measured against 20,610 vanilla RenderPrefabs.

THREE CONVENTIONS THAT ARE LOAD-BEARING
  1. AUTHOR WITH +X = FORWARD, then cs2_asset.orient_for_net maps it to CS2 +Z. The area
     spawner puts local +Z along the run border (SecondaryObjectSystem.cs:2990-2992), so
     "forward" here means "along the piste edge".
  2. NEVER AIM A BARREL HARD PERPENDICULAR. Polygon winding is recorded but never
     normalised (area-prop-variation.md C2), so which side the piste sits on flips with
     the direction the player drew the run. A perpendicular barrel fires into the trees on
     half of all runs. Guns get GunParams.yaw_deg of 15-30 off the border tangent, which
     reads plausibly either way.
  3. BASE-PIVOTED, origin at ground level on the machine's own footprint centre. Geometry
     below z=0 is treated as underground and clipped (the lesson lift_chair paid for).

TEXTURE BUDGET — the top risk in the pipeline plan. quad_size is picked per asset from its
physical size, targeting 60-150 px/m; copying the towers' 1024 default would cost ~112 MB
for this family instead of ~9 MB, on a project already overspending an order of magnitude.
atlas_size is ALWAYS derived as 2 x quad_size, never written as a literal — a mismatch
silently corrupts the AO rebake and only shows up in game.
"""

import argparse
import math
import os
import sys
from dataclasses import dataclass

import bmesh
import bpy
import mathutils

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import cs2_asset  # noqa: E402
import lift_tower  # noqa: E402  — make_lod, frame_camera, pick_engine


# Atlas ZONES — cs2_asset.snow_prop_zones, the family-private recipe
# (docs/research/art/snow-prop-materials.md). Parts are tagged with a zone NAME, not a
# quad index, because quads 2 and 3 are sub-divided: quad 2 into rubber / grate / safety
# plastic, quad 3 into livery paint (recolour slot 1) and accent paint (slot 3).
#
# Two material bugs the materials plan caught in the first draft, fixed here:
#   * tyres, castor, hoses and the fan intake were on the GLASS quad at 0.92 smoothness —
#     mirror-finish rubber. They are now on `rubber` at 0.18.
#   * the orange hydrant lid and handwheel were on recolour slot 1, so the safety cap
#     would have recoloured with the manufacturer livery. They are now on `plastic`,
#     which is slot 2 (safety orange) — a separate picker.
Z_CONCRETE = "concrete"   # pads, plinths
Z_GALV     = "galv"       # masts, frames, ladders, hardware
Z_LIVERY   = "livery"     # housing paint, recolour slot 1
Z_ACCENT   = "accent"     # chassis/trim paint, recolour slot 3
Z_RUBBER   = "rubber"     # tyres, hoses, conduit
Z_GRATE    = "grate"      # intake mesh, platform grating, louvres
Z_PLASTIC  = "plastic"    # safety plastic, recolour slot 2 (orange)
Z_AMBER    = "amber"      # beacon lens (snow guns)
Z_FLOODLENS = "floodlens" # cool-white floodlight lens (night-skiing masts)

# Tri budgets: (target, hard cap) from the pipeline plan's vanilla measurements.
BUDGETS = {
    "tower":   (1200, 1800),
    "lance":   (350, 600),
    "fan":     (900, 1400),
    "hydrant": (250, 450),
    "flood":   (660, 900),
}

# LOD chains — decimate ratios for (LOD1, LOD2), or () for "ship LOD0 only".
#
# Only the two GUNS get chains (prop-lod-and-lighting.md): they sit above their size
# band's p75-p90 for LOD0 tris and get placed 200-400 times per city. The lance (192) and
# hydrant (136) are already BELOW their band's median LOD0, so a chain would cost more in
# extra RenderPrefabs, mesh memory and batch slots than the ~70 triangles it saves — which
# is why 91-98% of comparable vanilla props ship none.
#
# Ratios from a fresh dump census: LOD1/LOD0 median is 0.51-0.57 in every size band,
# LOD2/LOD0 is 0.16-0.30. LODs are DECIMATED FROM THE FINISHED MESH, never rebuilt at
# lower detail — apply_finish_textures repacks UVs, so a rebuilt LOD would sample the
# wrong atlas texels.
LOD_CHAIN = {
    "tower":   (0.52, 0.20),
    "fan":     (0.52, 0.20),
    "lance":   (),
    "hydrant": (),
    "flood":   (0.42, 0.12),
}

# Parts that take the finishing-pass bevel — the BOXY ones only, same rule as
# lift_tower.BEVEL_PREFIXES. Cylinders already read round; thin bars (rails, rungs, ribs,
# clamps, jack feet) are too small to catch a specular line and the bevel would cost more
# tris than it returns. Razor-sharp box edges are the single biggest procedural tell
# against vanilla assets, so the parts a player can actually see a corner on get 0.02 m.
BEVEL_PREFIXES = ("Pad", "BasePlate", "Cabinet", "Platform", "Body", "Lid",
                  "Compressor", "DriveHousing", "PushPlate")

# Assets that ship the fifth (emissive) map. The floodlight's lens is the whole point of
# the asset; the snow-gun tower has an amber beacon on its cabinet. The lance, hydrant and
# carriage gun have no lit surface, and a black emissive map would cost memory for nothing.
EMISSIVE_ASSETS = {"flood", "tower"}


# --------------------------------------------------------------------------------------
# Primitives (same idiom as lift_chair.py)
# --------------------------------------------------------------------------------------

def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def add_box(name, size, location, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return o


def add_tube(name, radius, depth, location, rotation=(0, 0, 0), segments=8):
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments, radius=radius, depth=depth,
                                        location=location, rotation=rotation)
    o = bpy.context.active_object
    o.name = name
    return o


def add_cone(name, r1, r2, depth, location, rotation=(0, 0, 0), segments=16):
    bpy.ops.mesh.primitive_cone_add(vertices=segments, radius1=r1, radius2=r2, depth=depth,
                                    location=location, rotation=rotation)
    o = bpy.context.active_object
    o.name = name
    return o


def add_link(name, p0, p1, radius, segments=6, extend=0.02):
    """Tube spanning two points, over-long at both ends so bends overlap cleanly."""
    a, b = mathutils.Vector(p0), mathutils.Vector(p1)
    d = b - a
    o = add_tube(name, radius, d.length + 2.0 * extend, tuple((a + b) / 2.0),
                 segments=segments)
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    return o


def tri_count(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    n = len(bm.faces)
    bm.free()
    return n


# --------------------------------------------------------------------------------------
# Parameters
# --------------------------------------------------------------------------------------

@dataclass
class TowerParams:
    """TechnoAlpin TT10-class head on a mast. Column heights 1.6/3.5/4.5 m are documented;
    6 m and 9 m are the industry norm (SMI). 6.5 is our default, variants 4.5 / 6.5 / 8.0."""
    mast_h: float = 6.5
    mast_r_base: float = 0.11        # 0.22 outer dia
    mast_r_top: float = 0.09
    pad: tuple = (0.80, 0.80, 0.25)  # concrete pad L,W,H
    base_plate: tuple = (0.60, 0.60, 0.05)
    head_len: float = 1.25
    head_r_muzzle: float = 0.45      # 0.90 dia
    head_r_throat: float = 0.40
    head_rise: float = 0.45          # head centreline above mast top
    pitch_deg: float = 22.0          # nose-up
    yaw_deg: float = 22.0            # off the border tangent — see convention 2
    platform: tuple = (0.90, 0.90, 0.04)
    rail_h: float = 1.05
    cabinet: tuple = (0.45, 0.30, 0.70)   # W(lateral) x D x H
    cabinet_off: float = 0.62
    nozzles: int = 12                # 24 real; 12 reads identically and halves the cost


@dataclass
class LanceParams:
    """TechnoAlpin TL8: 4.50 / 7.17 / 10.06 m working heights. Default 7.0."""
    height: float = 7.0
    pipe_r: float = 0.0275           # 0.055 dia
    # Anatomy study corrects the lean: real working lances sit at 25-40 degrees off
    # vertical, not the 15 the first spec assumed.
    lean_deg: float = 30.0
    head_len: float = 0.18
    head_r: float = 0.045
    column_r: float = 0.07           # 0.14 dia rotating column
    column_h: float = 0.55
    base_plate: tuple = (0.35, 0.35, 0.12)
    pad: tuple = (0.60, 0.60, 0.18)
    compressor: tuple = (0.70, 0.50, 0.55)
    with_compressor: bool = True


@dataclass
class FanParams:
    """TechnoAlpin TF10 Manual: 2.70 L (incl. drawbar) x 1.55 W x 2.55 H, turbine 0.90."""
    barrel_len: float = 1.45
    barrel_r_muzzle: float = 0.525   # 1.05 dia
    barrel_r_throat: float = 0.475
    barrel_z: float = 1.65           # centreline height
    pitch_deg: float = 30.0
    yaw_deg: float = 20.0            # see convention 2
    wheel_r: float = 0.30
    wheel_w: float = 0.20
    track_w: float = 1.20            # between wheel centres
    frame_w: float = 0.08
    drawbar_len: float = 0.90
    castor_r: float = 0.10
    jack_h: float = 0.35
    cabinet: tuple = (0.40, 0.25, 0.55)
    nozzles: int = 12
    nucleators: int = 6
    tank_r: float = 0.16             # the blue accent cylinder under the barrel


@dataclass
class FloodlightParams:
    """
    Piste floodlight for night skiing (docs/research/art/piste-floodlight-spec.md).
    Dimensions from dimensioned German catalogue drawings (megamast.de 12 m mast).

    THE DESIGN GOAL IS TELLING IT APART FROM THE SNOW-GUN TOWER AT A GLANCE, and the
    spec's answer is INVERTED MASS DISTRIBUTION: this mast's widest point is its CROWN
    (a 1.5 m cross-arm carrying three heads at 12 m) over a completely BARE tapered
    shaft; the snow gun's widest point is its BASE (cabinet, standpipe, hoses, orange
    box) under a busy shaft (platform + caged ladder). The bare shaft is authentic, not
    a shortcut — real piste masts have no ladder and no platform, only an 85 x 400 mm
    base access door.
    """
    height: float = 12.0             # light point height; variants 8 / 12 / 16
    # Taper rule that holds at any height: ~10 mm of diameter per metre of mast.
    r_base: float = 0.114            # 0.228 dia at ground
    r_top: float = 0.054             # 0.108 dia at the top
    pad: tuple = (0.90, 0.90, 0.20)
    door: tuple = (0.085, 0.400)     # w x h, at 0.60 m — the ONLY shaft detail
    door_z: float = 0.60
    arm_span: float = 1.50           # 2 x 0.750
    arm_r: float = 0.045
    lum: tuple = (0.60, 0.25, 0.19)  # luminaire body L x W x H
    lum_count: int = 3
    lum_pitch_deg: float = 25.0      # below horizontal
    with_lowering: bool = False      # winch gear — 16 m variant only


@dataclass
class HydrantParams:
    """Snowmaking hydrant cover + standpipe. The visible slope furniture is the COVER."""
    box: tuple = (0.50, 0.50, 0.35)
    lid_h: float = 0.06
    pipe_r: float = 0.045            # 0.09 dia
    pipe_h: float = 0.90
    wheel_r: float = 0.125           # 0.25 handwheel
    hose_r: float = 0.03
    with_hose: bool = True


# --------------------------------------------------------------------------------------
# Shared: the fan-gun head (tower and carriage share one form, at different scales)
# --------------------------------------------------------------------------------------

def gun_axes(pitch_deg, yaw_deg):
    """Forward / side / perpendicular unit vectors for an aimed barrel."""
    pitch, yaw = math.radians(pitch_deg), math.radians(yaw_deg)
    fwd = mathutils.Vector((math.cos(pitch) * math.cos(yaw),
                            math.cos(pitch) * math.sin(yaw),
                            math.sin(pitch)))
    side = fwd.cross(mathutils.Vector((0, 0, 1))).normalized()
    perp = side.cross(fwd).normalized()
    return fwd, side, perp


def build_gun_head(prefix, origin, r_muzzle, r_throat, length, pitch_deg, yaw_deg,
                   nozzles, nucleators=0, scale=1.0, manifold=False):
    """
    Barrel pointing +X before rotation, then pitched nose-up and yawed off the border.
    Returns [(obj, quad), ...]. Origin is the barrel's own centre of mass.

    Detail pass: the first version was a bare cone, which reads as a bucket. Real fan
    guns have a distinct MUZZLE COLLAR (the nozzle-ring housing, a step wider than the
    barrel), a recessed dark INTAKE at the rear, and longitudinal RIBS down the housing.
    Those three are what separate "snow gun" from "cylinder" at 20 m; the nozzle ring
    itself only pays off close up.
    """
    parts = []
    ox, oy, oz = origin

    # Barrel: cone with the wide end at the muzzle (+X). primitive_cone's axis is +Z,
    # so rotate +90 about Y to lay it along +X, then apply pitch/yaw.
    rot = mathutils.Euler((0.0, math.radians(90.0), 0.0), "XYZ").to_matrix().to_4x4()
    aim = mathutils.Euler((0.0, -math.radians(pitch_deg), math.radians(yaw_deg)),
                          "XYZ").to_matrix().to_4x4()

    barrel = add_cone(f"{prefix}Barrel", r_throat, r_muzzle, length, (0, 0, 0), segments=16)
    barrel.matrix_world = aim @ rot
    bpy.ops.object.select_all(action="DESELECT")
    barrel.select_set(True)
    bpy.context.view_layer.objects.active = barrel
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    barrel.location = (ox, oy, oz)
    parts.append((barrel, Z_LIVERY))

    fwd, side, perp = gun_axes(pitch_deg, yaw_deg)
    aim_rot = fwd.to_track_quat("Z", "Y").to_euler()
    centre = mathutils.Vector((ox, oy, oz))
    muzzle = centre + fwd * (length * 0.5)
    throat = centre - fwd * (length * 0.5)

    # Muzzle collar — the step-wider band carrying the nozzle ring. Biggest single
    # readability win on the barrel.
    collar = add_tube(f"{prefix}Collar", r_muzzle * 1.10, 0.10 * scale,
                      tuple(muzzle - fwd * 0.04), segments=16)
    collar.rotation_euler = aim_rot
    parts.append((collar, Z_GALV))

    # Longitudinal ribs down the housing (4 is plenty — they read as shading breaks).
    for i in range(4):
        a = 2.0 * math.pi * i / 4.0 + math.pi / 4.0
        radial = (side * math.cos(a) + perp * math.sin(a))
        rib_r = (r_muzzle + r_throat) * 0.5 * 0.99
        rib = add_box(f"{prefix}Rib{i}", (length * 0.80, 0.035 * scale, 0.045 * scale),
                      (0, 0, 0))
        # The rib's long axis is local X; track X onto the barrel direction.
        rib.rotation_euler = fwd.to_track_quat("X", "Z").to_euler()
        rib.location = tuple(centre + radial * rib_r)
        parts.append((rib, Z_LIVERY))

    # Nozzle ring on the muzzle face.
    ring_r = r_muzzle * 0.82
    for i in range(nozzles):
        a = 2.0 * math.pi * i / nozzles
        p = muzzle + (side * math.cos(a) + perp * math.sin(a)) * ring_r + fwd * 0.02
        n = add_tube(f"{prefix}Nozzle{i}", 0.022 * scale, 0.08 * scale, tuple(p), segments=6)
        n.rotation_euler = aim_rot
        parts.append((n, Z_GALV))

    # Inner nucleator ring (carriage gun — the detail the TF10 diagram calls out).
    for i in range(nucleators):
        a = 2.0 * math.pi * i / nucleators + math.pi / nucleators
        p = muzzle + (side * math.cos(a) + perp * math.sin(a)) * (r_muzzle * 0.32) + fwd * 0.02
        n = add_tube(f"{prefix}Nuc{i}", 0.016 * scale, 0.07 * scale, tuple(p), segments=6)
        n.rotation_euler = aim_rot
        parts.append((n, Z_GALV))

    # Rear intake: a recessed dark disc inside a rim. The dark circular void is what
    # tells the eye which end is the back.
    rim = add_tube(f"{prefix}IntakeRim", r_throat * 1.06, 0.07 * scale,
                   tuple(throat + fwd * 0.02), segments=16)
    rim.rotation_euler = aim_rot
    parts.append((rim, Z_GALV))
    guard = add_tube(f"{prefix}Guard", r_throat * 0.90, 0.03 * scale,
                     tuple(throat + fwd * 0.06), segments=12)
    guard.rotation_euler = aim_rot
    parts.append((guard, Z_GRATE))

    if manifold:
        # Chrome pipework and valve bank slung under the barrel — clearly visible in the
        # user's reference image 4 and the single most "machine-like" detail available.
        base = centre - perp * (r_muzzle * 0.85)
        parts.append((add_link(f"{prefix}ManifoldPipe",
                               tuple(base - fwd * (length * 0.32)),
                               tuple(base + fwd * (length * 0.36)),
                               0.032 * scale, segments=6), Z_GALV))
        for i, t in enumerate((-0.18, 0.06, 0.28)):
            vp = base + fwd * (length * t)
            v = add_tube(f"{prefix}Valve{i}", 0.045 * scale, 0.10 * scale,
                         tuple(vp - perp * 0.05), segments=6)
            parts.append((v, Z_GALV))
        # Feed line dropping away toward the hydrant.
        parts.append((add_link(f"{prefix}Feed",
                               tuple(base - fwd * (length * 0.30)),
                               tuple(base - fwd * (length * 0.30) - perp * 0.42),
                               0.028 * scale, segments=5), Z_RUBBER))

    return parts


# --------------------------------------------------------------------------------------
# Assets
# --------------------------------------------------------------------------------------

def build_tower(p: TowerParams):
    parts = []

    parts.append((add_box("Pad", p.pad, (0, 0, p.pad[2] / 2.0)), Z_CONCRETE))
    parts.append((add_box("BasePlate", p.base_plate,
                          (0, 0, p.pad[2] + p.base_plate[2] / 2.0)), Z_GALV))

    # Mast: tapered 12-gon in two segments so the taper reads without a lathe.
    mast_bottom = p.pad[2] + p.base_plate[2]
    mid_z = mast_bottom + (p.mast_h - mast_bottom) * 0.5
    r_mid = (p.mast_r_base + p.mast_r_top) * 0.5
    seg1 = add_cone("Mast1", p.mast_r_base, r_mid, mid_z - mast_bottom,
                    (0, 0, (mast_bottom + mid_z) / 2.0), segments=12)
    parts.append((seg1, Z_GALV))
    seg2 = add_cone("Mast2", r_mid, p.mast_r_top, p.mast_h - mid_z,
                    (0, 0, (mid_z + p.mast_h) / 2.0), segments=12)
    parts.append((seg2, Z_GALV))

    # Service platform + rail at three-quarter height — breaks the mast line, which is
    # silhouette feature 3 in the spec.
    plat_z = p.pad[2] + (p.mast_h - p.pad[2]) * 0.72
    parts.append((add_box("Platform", p.platform, (0.30, 0, plat_z)), Z_GRATE))
    rail_top = plat_z + p.rail_h
    for sx, sy in ((0.30 + p.platform[0] / 2, 0), (0.30 - p.platform[0] / 2, 0),
                   (0.30, p.platform[1] / 2), (0.30, -p.platform[1] / 2)):
        parts.append((add_link(f"RailPost{sx:.2f}{sy:.2f}", (sx, sy, plat_z),
                               (sx, sy, rail_top), 0.018, segments=4), Z_GALV))
    # Rail as a PERIMETER of bars, not a slab — a solid top box reads as a second shelf
    # floating above the platform, which is exactly what the first render showed.
    # Anatomy study: top rail 1.10, knee rail at half height, and a 0.10 KICK PLATE at
    # deck level (EN ISO 14122-3). The kick plate is what makes a platform look walked-on.
    hw, hd = p.platform[0] / 2.0, p.platform[1] / 2.0
    edges = ((hw, 0, 0.035, p.platform[1]), (-hw, 0, 0.035, p.platform[1]),
             (0, hd, p.platform[0], 0.035), (0, -hd, p.platform[0], 0.035))
    for dx, dy, sx, sy in edges:
        parts.append((add_box(f"Rail{dx:.2f}{dy:.2f}", (sx, sy, 0.035),
                              (0.30 + dx, dy, rail_top)), Z_GALV))
        parts.append((add_box(f"Knee{dx:.2f}{dy:.2f}", (sx, sy, 0.028),
                              (0.30 + dx, dy, plat_z + p.rail_h * 0.50)), Z_GALV))
        parts.append((add_box(f"Kick{dx:.2f}{dy:.2f}", (sx, sy, 0.10),
                              (0.30 + dx, dy, plat_z + 0.06)), Z_GALV))

    # Ladder: two stiles + rungs at the documented 0.28 m pitch, plus a SAFETY CAGE.
    # The cage is legally required above 3 m fall height (EN ISO 14122-4) and the anatomy
    # study ranks it the single biggest silhouette change available on this asset — a
    # caged ladder reads as industrial equipment, a bare one reads as a pole.
    for sy in (-0.16, 0.16):
        parts.append((add_link(f"Stile{sy}", (-0.22, sy, p.pad[2]),
                               (-0.22, sy, plat_z), 0.020, segments=4), Z_GALV))
    rung_pitch = 0.28
    rungs = max(2, int((plat_z - p.pad[2]) / rung_pitch))
    for i in range(rungs):
        z = p.pad[2] + (i + 0.6) * (plat_z - p.pad[2]) / rungs
        parts.append((add_box(f"Rung{i}", (0.045, 0.32, 0.024), (-0.22, 0, z)), Z_GALV))
    cage_z0 = p.pad[2] + 2.4                     # cage starts at the 2.4 m fall threshold
    if plat_z - cage_z0 > 0.8:
        hoops = max(2, int((plat_z - cage_z0) / 1.5))
        cage_r = 0.36
        for i in range(hoops + 1):
            z = cage_z0 + i * (plat_z - cage_z0) / hoops
            parts.append((add_tube(f"CageHoop{i}", cage_r, 0.030, (-0.30, 0, z),
                                   segments=10), Z_GALV))
        # Three longitudinal stringers tying the hoops — the part that actually reads.
        for ang in (-58, 0, 58):
            a = math.radians(ang)
            sx, sy = -0.30 + cage_r * math.cos(a), cage_r * math.sin(a)
            parts.append((add_link(f"CageBar{ang}", (sx, sy, cage_z0), (sx, sy, plat_z),
                                   0.016, segments=4), Z_GALV))

    # Mast flange joints every ~2.5 m — real masts ship in sections.
    n_flange = max(1, int((p.mast_h - p.pad[2]) / 2.5))
    for i in range(1, n_flange + 1):
        z = p.pad[2] + i * (p.mast_h - p.pad[2]) / (n_flange + 1)
        parts.append((add_tube(f"Flange{i}", p.mast_r_base * 1.35, 0.045, (0, 0, z),
                               segments=12), Z_GALV))

    # Head on top.
    head_z = p.mast_h + p.head_rise
    parts += build_gun_head("Head", (0.10, 0, head_z), p.head_r_muzzle, p.head_r_throat,
                            p.head_len, p.pitch_deg, p.yaw_deg, p.nozzles, scale=0.85)
    # Yoke tying head to mast top, plus the oscillation drive housing that sits in the
    # crook of every real tower gun.
    parts.append((add_link("Yoke", (0, 0, p.mast_h - 0.05), (0.10, 0, head_z), 0.055,
                           segments=6), Z_GALV))
    parts.append((add_box("DriveHousing", (0.26, 0.30, 0.24), (0.02, 0, p.mast_h + 0.06)),
                  Z_LIVERY))

    # Service conduit running the mast, cabinet to head — a vertical line that breaks the
    # bare tube and is visible at any distance the mast itself is.
    parts.append((add_link("Conduit", (0.14, 0.05, p.pad[2] + 0.10),
                           (0.14, 0.05, p.mast_h - 0.20), 0.026, segments=5), Z_RUBBER))
    for i in range(3):
        z = p.pad[2] + 0.9 + i * (p.mast_h - p.pad[2] - 1.6) / 2.0
        parts.append((add_box(f"Clamp{i}", (0.10, 0.14, 0.045), (0.13, 0.05, z)),
                      Z_GALV))

    # Base plate bolt bosses — free realism at the one place the eye lands on a mast.
    for bx, by in ((0.22, 0.22), (-0.22, 0.22), (0.22, -0.22), (-0.22, -0.22)):
        parts.append((add_tube(f"Bolt{bx}{by}", 0.026, 0.05,
                               (bx, by, p.pad[2] + p.base_plate[2] + 0.02), segments=6),
                      Z_GALV))

    # Base cabinet + beacon — "cluttered base" is silhouette feature 4.
    cab_w, cab_d, cab_h = p.cabinet
    cab_z = p.pad[2] + cab_h / 2.0
    parts.append((add_box("Cabinet", (cab_d, cab_w, cab_h), (p.cabinet_off, 0, cab_z)),
                  Z_GALV))
    parts.append((add_tube("Beacon", 0.06, 0.12, (p.cabinet_off, 0, p.pad[2] + cab_h + 0.06),
                           segments=8), Z_AMBER))
    return parts


def build_lance(p: LanceParams):
    parts = []
    parts.append((add_box("Pad", p.pad, (0, 0, p.pad[2] / 2.0)), Z_CONCRETE))
    parts.append((add_box("BasePlate", p.base_plate,
                          (0, 0, p.pad[2] + p.base_plate[2] / 2.0)), Z_GALV))

    col_bottom = p.pad[2] + p.base_plate[2]
    parts.append((add_tube("Column", p.column_r, p.column_h,
                           (0, 0, col_bottom + p.column_h / 2.0), segments=8), Z_LIVERY))

    # The lance itself: one leaning pipe. The lean is the whole silhouette.
    lean = math.radians(p.lean_deg)
    top_z = p.height
    run = (top_z - (col_bottom + p.column_h)) * math.tan(lean)
    a = (0.0, 0.0, col_bottom + p.column_h)
    b = (run, 0.0, top_z)
    # Two-tone per the anatomy study: the LANCE PIPE is painted white/livery, the column
    # under it is galvanised. "The machine is painted, the structure holding it is
    # galvanised" is the rule that holds across all three manufacturers.
    parts.append((add_link("Lance", a, b, p.pipe_r, segments=8, extend=0.0), Z_LIVERY))

    # Insulated base jacket — bright orange, 0.45 x 0.90. The anatomy study rates this the
    # most eye-catching element on a lance at 150 m, for about 40 tris. It is also the
    # thing that stops a lance reading as a bare flagpole at distance.
    parts.append((add_tube("BaseJacket", 0.225, 0.90, (0.0, 0.0, col_bottom + 0.45),
                           segments=10), Z_PLASTIC))

    # Head cluster at the tip: a slight bulge plus three nucleator stubs.
    d = (mathutils.Vector(b) - mathutils.Vector(a)).normalized()
    head_c = mathutils.Vector(b) - d * (p.head_len * 0.4)
    head = add_tube("Head", p.head_r, p.head_len, tuple(head_c), segments=8)
    head.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    parts.append((head, Z_LIVERY))
    for i in range(3):
        ang = 2.0 * math.pi * i / 3.0
        off = mathutils.Vector((math.cos(ang), math.sin(ang), 0)) * (p.head_r * 1.15)
        stub = add_tube(f"Stub{i}", 0.011, 0.10, tuple(head_c + off + d * 0.05), segments=4)
        stub.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        parts.append((stub, Z_GALV))

    if p.with_compressor:
        cw, cd, ch = p.compressor
        parts.append((add_box("Compressor", (cd, cw, ch), (-0.62, 0.0, ch / 2.0)), Z_GALV))
    return parts


def build_fan(p: FanParams):
    parts = []
    fw = p.frame_w

    # Chassis rails running fore-aft (+X = forward = along the border).
    for sy in (-p.track_w / 2.0, p.track_w / 2.0):
        parts.append((add_box(f"Rail{sy:.2f}", (1.60, fw, fw), (0.0, sy, 0.42)), Z_GALV))
    for sx in (-0.62, 0.62):
        parts.append((add_box(f"Cross{sx:.2f}", (fw, p.track_w, fw), (sx, 0.0, 0.42)),
                      Z_GALV))

    # Wheels.
    for sy in (-p.track_w / 2.0 - p.wheel_w / 2.0, p.track_w / 2.0 + p.wheel_w / 2.0):
        w = add_tube(f"Wheel{sy:.2f}", p.wheel_r, p.wheel_w, (-0.10, sy, p.wheel_r),
                     rotation=(math.radians(90), 0, 0), segments=12)
        parts.append((w, Z_RUBBER))

    # Drawbar forward + castor.
    parts.append((add_link("Drawbar", (0.62, 0, 0.42), (0.62 + p.drawbar_len, 0, 0.26),
                           0.045, segments=6), Z_GALV))
    parts.append((add_tube("Castor", p.castor_r, 0.07,
                           (0.62 + p.drawbar_len, 0, p.castor_r),
                           rotation=(math.radians(90), 0, 0), segments=8), Z_RUBBER))
    # Groomer push plate at the drawbar apex — the documented "three-point transport
    # system" that lets a snowcat shove the gun up the hill. Missing from the first pass
    # and it is Tier-1 silhouette: it says "this machine gets moved" at a glance.
    parts.append((add_box("PushPlate", (0.06, 0.30, 0.30),
                          (0.62 + p.drawbar_len + 0.05, 0, 0.30)), Z_GALV))

    # Screw jacks at the rear.
    for sy in (-0.44, 0.44):
        parts.append((add_link(f"Jack{sy}", (-0.62, sy, 0.42), (-0.62, sy, 0.02), 0.035,
                               segments=6), Z_GALV))
        parts.append((add_box(f"JackFoot{sy}", (0.18, 0.18, 0.03), (-0.62, sy, 0.03)),
                      Z_GALV))

    # Pedestal carrying the barrel.
    parts.append((add_link("Pedestal", (0.0, 0, 0.42), (0.0, 0, p.barrel_z - 0.30), 0.085,
                           segments=8), Z_GALV))

    # Barrel + muzzle furniture + the chrome manifold under it.
    parts += build_gun_head("Gun", (0.0, 0.0, p.barrel_z), p.barrel_r_muzzle,
                            p.barrel_r_throat, p.barrel_len, p.pitch_deg, p.yaw_deg,
                            p.nozzles, nucleators=p.nucleators, scale=1.0, manifold=True)

    # Turntable under the pedestal — real fan guns rotate 360 degrees and the disc reads.
    parts.append((add_tube("Turntable", 0.20, 0.07, (0.0, 0, p.barrel_z - 0.34),
                           segments=12), Z_GALV))

    # Hose coiled on the frame flank, and a laid run trailing off the back — the
    # "operational" cue the reference photos never lack.
    for i in range(2):
        r = 0.19 - i * 0.04
        parts.append((add_tube(f"Coil{i}", r, 0.05, (-0.30, -0.50, 0.56 + i * 0.05),
                               rotation=(math.radians(90), 0, 0), segments=8), Z_RUBBER))
    # Short laid run only. The first pass trailed 2.2 m of hose, which stretched the
    # asset's footprint to 3.9 m for a 2.7 m machine — placement spacing is set by the
    # bounding box, so a decorative tail would push real guns apart.
    hose = [(-0.62, 0.30, 0.06), (-1.00, 0.48, 0.05), (-1.32, 0.30, 0.05)]
    for i in range(len(hose) - 1):
        parts.append((add_link(f"Hose{i}", hose[i], hose[i + 1], 0.032, segments=5),
                      Z_RUBBER))

    # Blue accent tank under the barrel (reference image 2) + control cabinet.
    # Accent tank on the second recolour slot — the blue cylinder in reference image 2 is
    # a different colour from the housing on every real machine.
    parts.append((add_tube("Tank", p.tank_r, 0.70, (0.10, 0.0, 0.86),
                           rotation=(0, math.radians(90), 0), segments=8), Z_ACCENT))
    cw, cd, ch = p.cabinet
    parts.append((add_box("Cabinet", (cd, cw, ch), (-0.45, 0.46, 0.42 + ch / 2.0)),
                  Z_GALV))
    return parts


def build_floodlight(p: FloodlightParams):
    """
    Tapered shaft + crown cluster. Geometrically trivial on purpose: height costs no
    triangles (a 12 m mast is the same 12-gon as a 6.5 m one with a different Z), so the
    whole budget goes into the crown, which is the part that carries the identity.
    """
    parts = []
    parts.append((add_box("Pad", p.pad, (0, 0, p.pad[2] / 2.0)), Z_CONCRETE))

    # Shaft in three tapered segments — enough to read the 2.1:1 taper without a lathe.
    z0 = p.pad[2]
    segs = 3
    for i in range(segs):
        za = z0 + (p.height - z0) * i / segs
        zb = z0 + (p.height - z0) * (i + 1) / segs
        ra = p.r_base + (p.r_top - p.r_base) * (i / segs)
        rb = p.r_base + (p.r_top - p.r_base) * ((i + 1) / segs)
        parts.append((add_cone(f"Shaft{i}", ra, rb, zb - za, (0, 0, (za + zb) / 2.0),
                               segments=12), Z_GALV))

    # Base access door — the ONLY thing on the shaft, and the detail that says
    # "lighting mast" rather than "pole".
    dw, dh = p.door
    parts.append((add_box("Door", (0.02, dw, dh), (p.r_base, 0.0, p.door_z + dh / 2.0)),
                  Z_ACCENT))

    # Crown: horizontal cross-arm carrying the luminaires.
    parts.append((add_box("Arm", (0.07, p.arm_span, 0.09), (0, 0, p.height)), Z_GALV))
    pitch = math.radians(p.lum_pitch_deg)
    lw, ld, lh = p.lum
    for i in range(p.lum_count):
        # Spread across the arm; odd counts put one on centre.
        t = (i - (p.lum_count - 1) / 2.0) / max(1.0, (p.lum_count - 1) / 2.0)
        y = t * (p.arm_span / 2.0 - 0.10)
        z = p.height + 0.16
        body = add_box(f"Lum{i}", (lw, ld, lh), (0.0, y, z),
                       rotation=(0.0, pitch, 0.0))
        parts.append((body, Z_ACCENT))
        # Lens on the down-slope face — this is what the emissive map will drive.
        lens = add_box(f"Lens{i}", (0.03, ld * 0.88, lh * 0.80),
                       (lw * 0.5 * math.cos(pitch) + 0.015,
                        y,
                        z - lw * 0.5 * math.sin(pitch)),
                       rotation=(0.0, pitch, 0.0))
        parts.append((lens, Z_FLOODLENS))
        # Short knuckle tying head to arm.
        parts.append((add_link(f"Knuckle{i}", (0.0, y, p.height),
                               (0.0, y, z - lh * 0.4), 0.030, segments=5), Z_GALV))

    if p.with_lowering:
        # Winch gear at the base — tall masts lower their crown for servicing rather
        # than being climbed.
        parts.append((add_box("Winch", (0.26, 0.22, 0.30),
                              (p.r_base + 0.16, 0.0, p.pad[2] + 0.15)), Z_ACCENT))

    return parts


def build_hydrant(p: HydrantParams):
    parts = []
    bw, bd, bh = p.box
    body_h = bh - p.lid_h
    parts.append((add_box("Body", (bd, bw, body_h), (0, 0, body_h / 2.0)), Z_GALV))
    # Lid on the recolour quad: this is the orange cap that dots every real piste.
    # Lid and handwheel are SAFETY plastic (recolour slot 2, orange by default) — not the
    # manufacturer livery slot. Getting this wrong would have made every hydrant cap
    # change colour with the guns.
    parts.append((add_box("Lid", (bd * 1.06, bw * 1.06, p.lid_h),
                          (0, 0, body_h + p.lid_h / 2.0)), Z_PLASTIC))
    parts.append((add_tube("Pipe", p.pipe_r, p.pipe_h, (0.34, 0.0, p.pipe_h / 2.0),
                           segments=8), Z_GALV))
    wheel = add_tube("Wheel", p.wheel_r, 0.035, (0.34, 0.0, p.pipe_h), segments=10)
    parts.append((wheel, Z_PLASTIC))
    if p.with_hose:
        # A short laid hose stub — strongest realism cue per tri in the whole set.
        pts = [(0.34, 0.06, 0.10), (0.75, 0.28, 0.06), (1.25, 0.20, 0.05), (1.70, -0.10, 0.05)]
        for i in range(len(pts) - 1):
            parts.append((add_link(f"Hose{i}", pts[i], pts[i + 1], p.hose_r, segments=5),
                          Z_RUBBER))
    return parts


# --------------------------------------------------------------------------------------
# Assembly
# --------------------------------------------------------------------------------------

def finish(parts, name, atlas_size, base_pivot=True, bevel=False):
    """Per-part atlas UVs, join, weld, base-pivot, and rotate into the CS2 net frame."""
    # Bevel BEFORE unwrap so the new bevel faces get atlas UVs like everything else —
    # beveling after would leave them unmapped.
    if bevel:
        for obj, _zone in parts:
            if not obj.name.startswith(BEVEL_PREFIXES):
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

    zones = cs2_asset.snow_prop_zones(atlas_size)
    for obj, zone in parts:
        cs2_asset.unwrap_into_rect(obj, zones[zone], atlas_size)

    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()

    obj = bpy.context.active_object
    obj.name = name

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")

    if base_pivot:
        lo = min((obj.matrix_world @ v.co).z for v in obj.data.vertices)
        obj.location.z -= lo
        bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    cs2_asset.orient_for_net(obj)
    return obj


ASSETS = {
    "tower":   ("SkiSnowGunTowerA", lambda a: build_tower(TowerParams(mast_h=a.mast)), 256),
    "lance":   ("SkiSnowGunLanceA", lambda a: build_lance(LanceParams(height=a.lance)), 128),
    "fan":     ("SkiSnowGunFanA",   lambda a: build_fan(FanParams()), 256),
    "hydrant": ("SkiHydrantBoxA",   lambda a: build_hydrant(HydrantParams()), 128),
    "flood":   ("SkiPisteFloodlightA", lambda a: build_floodlight(FloodlightParams()), 256),
}


# ── Variants ────────────────────────────────────────────────────────────────────────
# The placeholder spawn group picks ONE of these per vertex, weighted
# (area-prop-variation.md). Mast/lance height is the variety axis the design spec ranks
# 5th but it is nearly free here — same builder, one parameter — and it is the axis that
# breaks the SKYLINE rhythm, which is what the eye actually reads along a run.
# Naming keeps the generation letter last (PIPELINE.md): never reuse a name.
VARIANTS = {
    "tower":   [("SkiSnowGunTowerA",     dict(mast_h=6.5), 256),
                ("SkiSnowGunTowerLowA",  dict(mast_h=4.5), 256),
                ("SkiSnowGunTowerTallA", dict(mast_h=8.0), 256)],
    "lance":   [("SkiSnowGunLanceA",     dict(height=7.0), 128),
                ("SkiSnowGunLanceTallA", dict(height=10.0, pipe_r=0.035), 128),
                # Maintenance pose: folded to ~1.4 m. Reads completely differently and
                # costs one parameter — the off-season / idle member of the group.
                # Folded pose has a fraction of the surface area of an erect lance, so it
                # needs a smaller atlas to stay in the texel budget — the guard caught
                # this at 268 px/m when it inherited the standing lance's size.
                ("SkiSnowGunLanceDownA", dict(height=1.4, lean_deg=62.0,
                                              with_compressor=False), 128)],
    "fan":     [("SkiSnowGunFanA",       dict(), 256),
                # Parked. CORRECTED by the anatomy study: an idle fan gun sits barrel
                # NOSE-UP at 30-45 degrees (water drains, snow does not sit in the
                # muzzle), not dropped level as the first pass assumed. Distinguished
                # from the working variant by yaw swung back along the frame.
                ("SkiSnowGunFanIdleA",   dict(pitch_deg=42.0, yaw_deg=-6.0), 256)],
    "hydrant": [("SkiHydrantBoxA",       dict(), 128),
                ("SkiHydrantBoxBareA",   dict(with_hose=False), 128)],
    # Three mast heights. Real piste spacing is 30-50 m on a single side, so a run gets
    # far fewer of these than snow guns — height variety carries most of the interest.
    "flood":   [("SkiPisteFloodlightA",     dict(height=12.0), 256),
                ("SkiPisteFloodlightLowA",  dict(height=8.0), 256),
                ("SkiPisteFloodlightTallA", dict(height=16.0, with_lowering=True), 256)],
}

PARAM_CLASS = {"tower": TowerParams, "lance": LanceParams,
               "fan": FanParams, "hydrant": HydrantParams,
               "flood": FloodlightParams}
BUILDER = {"tower": build_tower, "lance": build_lance,
           "fan": build_fan, "hydrant": build_hydrant,
           "flood": build_floodlight}


def build_one(key, args):
    name, builder, quad_size = ASSETS[key]
    parts = builder(args)
    obj = finish(parts, name, quad_size * 2)
    return obj, name


def report(key, obj):
    tris = tri_count(obj)
    target, cap = BUDGETS[key]
    d = obj.dimensions
    status = "OK" if tris <= target else ("OVER TARGET" if tris <= cap else "OVER CAP")
    print(f"[{key}] {obj.name}: {tris} tris (target {target}, cap {cap}) -> {status}")
    print(f"[{key}]   size (m): {d.x:.2f} x {d.y:.2f} x {d.z:.2f}")
    if tris > cap:
        raise RuntimeError(f"{obj.name} is {tris} tris, over the {cap} hard cap")
    return tris


def mesh_area(obj):
    """Total world-space surface area — the denominator the texture budget is actually
    defined against."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    a = sum(f.calc_area() for f in bm.faces)
    bm.free()
    return a


def export_one(key, obj, name, out_root, quad_size, do_finish=False):
    asset_dir = os.path.join(os.path.abspath(out_root), name)
    os.makedirs(asset_dir, exist_ok=True)
    tex_root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "textures")
    quad_sets, control_quads = cs2_asset.snow_prop_atlas_quads(tex_root)
    A = quad_size * 2                      # DERIVED, never a literal — a mismatch here
                                           # silently corrupts the AO rebake
    cs2_asset.make_texture_set_atlas(asset_dir, name, quad_sets, quad_size=quad_size,
                                     control_quads=control_quads,
                                     post=cs2_asset.snow_prop_post)
    # Fifth map, emissive — only for assets that actually have a lit surface. Shipping a
    # black emissive on the lance or the hydrant would cost texture memory for nothing.
    if key in EMISSIVE_ASSETS:
        cs2_asset.make_snow_prop_emissive(asset_dir, name, quad_size)

    # FINISH PASS — must run BEFORE the FBX export and before LOD decimation: it repacks
    # UVs into a unique layout and rebakes all four maps, so anything exported earlier
    # would carry the old atlas coordinates. The stamped detail strip is passed as a
    # reserved rect so its texels keep their authored density through the repack.
    # ao_samples 24 rather than lift_tower's 48: our atlases are 256-512 px, not 2048.
    if do_finish:
        zones = cs2_asset.snow_prop_zones(A)
        ok = lift_tower.apply_finish_textures(obj, asset_dir, name, atlas_size=A,
                                              reserved_strips=[zones["strip"]],
                                              ao_samples=24)
        print(f"[{key}]   finish: AO rebake {'ok' if ok else 'FAILED (shipping bevels only)'}")

    cs2_asset.export_fbx(obj, os.path.join(asset_dir, f"{name}.fbx"))

    # Authored LOD chain, decimated from the finished mesh. Supplying an LOD FBX makes the
    # importer skip generation for that level (they are mutually exclusive), so an asset
    # either ships a full chain or relies on the default — which, with stock settings,
    # generates NOTHING. See the write_settings docstring correction.
    ratios = LOD_CHAIN.get(key, ())
    for i, ratio in enumerate(ratios, start=1):
        lod = lift_tower.make_lod(obj, ratio, f"{name}_LOD{i}")
        print(f"[{key}]   LOD{i}: {tri_count(lod)} tris (ratio {ratio})")
        cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{i}.fbx"))
        bpy.data.objects.remove(lod, do_unlink=True)
    cs2_asset.write_settings(asset_dir, name,
                             supplied_lods=tuple(range(1, len(ratios) + 1)))

    # TEXTURE BUDGET GUARD. The first version divided quad_size by the LONGEST DIMENSION,
    # which is not the metric the budget was set with and passed everything trivially
    # (the tower scored 69 and sailed through while actually running 250). The budget is
    # atlas_px / sqrt(surface_area) — texels per metre of SKIN, not of bounding box.
    area = mesh_area(obj)
    px_per_m = (quad_size * 2) / math.sqrt(max(area, 1e-3))
    print(f"[{key}]   atlas {quad_size * 2}px over {area:.1f} m^2 -> {px_per_m:.0f} px/m")
    if px_per_m > 200:
        raise RuntimeError(f"{name}: {px_per_m:.0f} px/m exceeds the 200 px/m ceiling — "
                           f"halve quad_size (snow-prop-materials.md section 5)")
    print(f"[{key}]   asset folder: {asset_dir}")
    return asset_dir


# --------------------------------------------------------------------------------------
# Preview: one lineup image beats four separate renders when judging VARIETY
# --------------------------------------------------------------------------------------

# Manufacturer liveries as TARGET colours (slot 1 housing, slot 2 safety, slot 3 accent).
# Art direction read off product photography, not certified brand fact — see
# snowmaking-props-spec.md section 6.1. Every channel caps at RECOLOR_ALBEDO (0.80):
# recolour tints MULTIPLY a light-neutral albedo, so 0.80 is the brightest reachable value.
LIVERIES = [
    ("technoalpin", (0.80, 0.62, 0.04), (0.80, 0.24, 0.09), (0.22, 0.23, 0.25)),
    ("legacyblue",  (0.10, 0.30, 0.70), (0.80, 0.24, 0.09), (0.72, 0.74, 0.76)),
    ("demaclenko",  (0.10, 0.10, 0.12), (0.80, 0.24, 0.09), (0.62, 0.14, 0.10)),
    ("smiblue",     (0.14, 0.38, 0.66), (0.80, 0.24, 0.09), (0.74, 0.75, 0.77)),
]


def make_tinted_preview(asset_dir, name, out_dir, livery):
    """
    Bake the recolour tints into a PREVIEW-ONLY BaseColor.

    In game the albedo under every recolour mask is light-neutral RECOLOR_ALBEDO grey and
    the picker tint multiplies it, so a straight preview render shows grey machines and
    tells you nothing about livery. This reproduces what CS2 will actually display:
    read the ControlMask, build a per-texel tint from the three slot colours, multiply.
    Tint = target / RECOLOR_ALBEDO, exactly the compensation the prefab VariationSet needs.
    Never called on the export path — the shipped textures stay light-neutral.
    """
    import numpy as np
    _, slot1, slot2, slot3 = livery
    base_img = bpy.data.images.load(os.path.join(asset_dir, f"{name}_BaseColor.png"))
    ctl_img = bpy.data.images.load(os.path.join(asset_dir, f"{name}_ControlMask.png"))
    ctl_img.colorspace_settings.name = "Non-Color"
    w, h = base_img.size
    b = np.array(base_img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    c = np.array(ctl_img.pixels[:], dtype=np.float32).reshape(h, w, 4)

    tint = np.ones((h, w, 3), dtype=np.float32)
    for ch, target in ((0, slot1), (1, slot2), (2, slot3)):
        m = np.clip(c[..., ch], 0.0, 1.0)[..., None]
        col = np.array([min(v / cs2_asset.RECOLOR_ALBEDO, 1.0) for v in target],
                       dtype=np.float32)
        tint = tint * (1.0 - m) + col * m
    b[..., 0:3] = np.clip(b[..., 0:3] * tint, 0.0, 1.0)

    os.makedirs(out_dir, exist_ok=True)
    out = bpy.data.images.new(f"{name}_TintedPreview", w, h, alpha=True)
    out.pixels = b.reshape(-1).tolist()
    out.filepath_raw = os.path.join(out_dir, f"{name}_BaseColor.png")
    out.file_format = "PNG"
    out.save()
    bpy.data.images.remove(base_img)
    bpy.data.images.remove(ctl_img)
    return out_dir


def _set_base_color(mat, rgba):
    """Node lookup by TYPE, not by name — the shader node's name is localisation- and
    version-dependent, and a silent KeyError here just leaves the default white."""
    for node in mat.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED":
            node.inputs["Base Color"].default_value = rgba
            return True
    return False


def render_lineup(objs, out_dir, stem="SnowPropsLineup"):
    import lift_tower
    scene = bpy.context.scene
    scene.render.engine = lift_tower.pick_engine()
    scene.render.image_settings.file_format = "PNG"

    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.68, 0.78, 1.0)
    scene.world = world

    # Ground plane so the base-pivot and the base clutter read honestly. Mid-tone, not
    # white — these are pale galvanised props and they vanish against a white floor.
    bpy.ops.mesh.primitive_plane_add(size=26, location=(0, 0, 0))
    ground = bpy.context.active_object
    gm = bpy.data.materials.new("Ground")
    gm.use_nodes = True
    _set_base_color(gm, (0.42, 0.46, 0.52, 1))
    ground.data.materials.append(gm)

    bpy.ops.object.light_add(type="SUN", location=(10, -12, 20))
    key = bpy.context.active_object
    key.data.energy = 5.0
    key.rotation_euler = (math.radians(52), 0, math.radians(35))
    bpy.ops.object.light_add(type="SUN", location=(-12, 8, 12))
    fill = bpy.context.active_object
    fill.data.energy = 2.0
    fill.rotation_euler = (math.radians(65), 0, math.radians(215))

    bpy.ops.object.camera_add(location=(0, 0, 0))
    cam = bpy.context.active_object
    cam.data.lens = 55
    scene.camera = cam

    # A 1.8 m human silhouette for scale — the single most useful thing in a prop lineup.
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.17, depth=1.8,
                                        location=(0, 0, 0.9))
    human = bpy.context.active_object
    human.name = "ScaleFigure"
    hm = bpy.data.materials.new("ScaleFig")
    hm.use_nodes = True
    _set_base_color(hm, (0.09, 0.11, 0.14, 1))
    human.data.materials.append(hm)

    # Lay the row out along X, and yaw every asset 90 degrees for the CAMERA ONLY:
    # post-orient_for_net a gun aims along -Y (the border tangent), which points straight
    # at a front camera and shows nothing but the muzzle face. Turning them puts the
    # barrels in profile, which is the silhouette we are actually judging. Preview-only —
    # the exported FBX is untouched.
    spacing = 2.8
    line = [human] + list(objs)
    for i, o in enumerate(line):
        o.location.x = (i - (len(line) - 1) / 2.0) * spacing
        if o is not human:
            o.rotation_euler = (0.0, 0.0, math.radians(90.0))

    group = objs[0]
    bpy.ops.object.select_all(action="DESELECT")
    for o in line:
        o.select_set(True)
    bpy.context.view_layer.objects.active = group

    # Framing proxy: frame_camera reads bound_box, and an Empty has none — so span the
    # row with a real (render-hidden) cube and frame on that.
    row_w = spacing * (len(line) - 1) + 2.2
    row_h = max(o.dimensions.z for o in line) + 0.4
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, row_h / 2.0))
    proxy = bpy.context.active_object
    proxy.name = "FrameProxy"
    # Thin in depth: frame_camera fits the bounding SPHERE, so any depth we give the
    # proxy is wasted zoom.
    proxy.scale = (row_w, 0.4, row_h)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    proxy.hide_render = True

    written = []
    # frame_camera fits the bounding SPHERE against the NARROWER field of view, so a wide
    # flat row gets framed by its diagonal and wastes most of the frame on sky. The margin
    # compensates: sphere radius is ~sqrt(w^2+h^2)/2 but we only need h vertically.
    # 1.06 framed the diagonal and wasted the frame on sky; the pure height ratio clipped
    # the ends on the oblique view (the row projects wider than its own width off-axis).
    # 0.80 is the field-tuned middle.
    row_margin = 0.80
    views = [("lineup_front", mathutils.Vector((0.02, -1.0, 0.16)), (2400, 1000)),
             ("lineup_three_quarter", mathutils.Vector((0.55, -1.0, 0.30)), (2400, 1050))]
    os.makedirs(out_dir, exist_ok=True)
    for vname, direction, res in views:
        scene.render.resolution_x, scene.render.resolution_y = res
        lift_tower.frame_camera(cam, proxy, direction, res, margin=row_margin)
        path = os.path.join(out_dir, f"{stem}_{vname}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
    bpy.data.objects.remove(proxy, do_unlink=True)
    return written


def render_row(objs_by_key, out_dir, stem="SnowPropsRow", vertices=11, seed=7):
    """
    The decision-relevant preview: what a LINE of these looks like along a piste edge,
    drawn the way the game will actually place them — one draw per polygon vertex, the
    variant chosen from a weighted spawnable group, some vertices empty.

    Weights and gap rate mirror the prefab wiring the research recommends
    (area-prop-variation.md): tower 45 / lance 30 / carriage 25, presence ~75%.
    If this row reads as copy-paste, the group weights are wrong, not the models.
    """
    import random as _random
    import lift_tower
    rng = _random.Random(seed)

    scene = bpy.context.scene
    scene.render.engine = lift_tower.pick_engine()
    scene.render.image_settings.file_format = "PNG"
    world = bpy.data.worlds.new("RowWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.68, 0.78, 1.0)
    scene.world = world

    bpy.ops.mesh.primitive_plane_add(size=90, location=(0, 0, 0))
    ground = bpy.context.active_object
    gm = bpy.data.materials.new("RowGround")
    gm.use_nodes = True
    _set_base_color(gm, (0.80, 0.83, 0.88, 1))
    ground.data.materials.append(gm)

    bpy.ops.object.light_add(type="SUN", location=(20, -25, 40))
    k = bpy.context.active_object
    k.data.energy = 4.6
    k.rotation_euler = (math.radians(54), 0, math.radians(38))
    bpy.ops.object.light_add(type="SUN", location=(-25, 15, 25))
    f = bpy.context.active_object
    f.data.energy = 1.8
    f.rotation_euler = (math.radians(66), 0, math.radians(215))

    # Weighted by TYPE first (tower 45 / lance 30 / carriage 25), then uniformly among
    # that type's height/pose variants — which is exactly what a placeholder spawn group
    # does, so this preview is a faithful rehearsal of the shipped behaviour.
    weights = [("tower", 45), ("lance", 30), ("fan", 25)]
    pool = [k for k, w in weights for _ in range(w)]
    by_type = {}
    for key in ("tower", "lance", "fan", "hydrant"):
        by_type[key] = [o for k, o in objs_by_key.items() if k.split(":")[0] == key]
    spacing = 7.0
    placed = []
    for i in range(vertices):
        x = (i - (vertices - 1) / 2.0) * spacing
        # Border wander, so the line is not a ruler.
        y = rng.uniform(-1.2, 1.2)
        if rng.random() > 0.75:          # presence gate — the gaps matter most
            continue
        key = rng.choice(pool)
        src = rng.choice(by_type[key])
        dup = src.copy()
        dup.data = src.data
        bpy.context.collection.objects.link(dup)
        dup.location = (x, y, 0.0)
        dup.rotation_euler = (0.0, 0.0, math.radians(rng.uniform(-14, 14)))
        placed.append(dup)
        # Base clutter on ~55% of guns, offset to the piste side.
        if rng.random() < 0.55:
            hsrc = rng.choice(by_type["hydrant"])
            hb = hsrc.copy()
            hb.data = hsrc.data
            bpy.context.collection.objects.link(hb)
            hb.location = (x + rng.uniform(-1.6, 1.6), y + rng.uniform(1.4, 2.6), 0.0)
            hb.rotation_euler = (0.0, 0.0, math.radians(rng.uniform(0, 360)))
            placed.append(hb)

    # Hide the four source objects — they sit at the origin and would photobomb the row.
    for o in objs_by_key.values():
        o.hide_render = True

    # Manual camera placement, not frame_camera: framing a 70 m row by its bounding sphere
    # puts the camera so far back the props are specks. What we need to judge repetition is
    # a viewer-height look DOWN the line, the way a player's camera actually sees a piste.
    span = spacing * (vertices - 1)
    bpy.ops.object.camera_add(location=(0, 0, 0))
    cam = bpy.context.active_object
    cam.data.lens = 42
    scene.camera = cam

    def aim(loc, target):
        cam.location = loc
        d = mathutils.Vector(target) - mathutils.Vector(loc)
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()

    written = []
    os.makedirs(out_dir, exist_ok=True)
    shots = [
        # Down the line from just off the end — the repetition test.
        ("row_along", (-span / 2 - 11.0, -9.0, 4.2), (span * 0.18, 0.0, 2.6), (2400, 1000)),
        # Broadside on the middle few, closer — reads individual silhouettes.
        ("row_broadside", (-2.0, -26.0, 6.0), (2.0, 0.0, 3.0), (2400, 1000)),
    ]
    for vname, loc, target, res in shots:
        scene.render.resolution_x, scene.render.resolution_y = res
        aim(loc, target)
        path = os.path.join(out_dir, f"{stem}_{vname}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
    print(f"[row] {len(placed)} props across {vertices} vertices, span {span:.0f} m")
    return written


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=None, help="import folder, e.g. ../import/props/A")
    ap.add_argument("--lineup", default=None, help="render a scale-lineup preview here")
    ap.add_argument("--row", default=None,
                    help="render a simulated piste-edge ROW here (variety judgement)")
    ap.add_argument("--assets", default="tower,hydrant,lance,fan")
    ap.add_argument("--finish", action="store_true",
                    help="bevel boxy parts + bake AO into the atlas (slow, shipping default)")
    ap.add_argument("--variants", action="store_true",
                    help="export every VARIANTS member, not just the default asset")
    ap.add_argument("--mast", type=float, default=6.5, help="tower gun mast height")
    ap.add_argument("--lance", type=float, default=7.0, help="lance working height")
    return ap.parse_args(argv)


def main():
    args = parse_args()
    keys = [k.strip() for k in args.assets.split(",") if k.strip()]
    for k in keys:
        if k not in ASSETS:
            raise SystemExit(f"unknown asset '{k}' (have: {', '.join(ASSETS)})")

    if args.lineup or args.row:
        clear_scene()
        objs = []
        by_key = {}
        total = 0
        for k in keys:
            jobs = VARIANTS[k] if args.variants else [(ASSETS[k][0], {}, ASSETS[k][2])]
            for idx, (name, kwargs, qs) in enumerate(jobs):
                parts = BUILDER[k](PARAM_CLASS[k](**kwargs))
                obj = finish(parts, name, qs * 2)
                total += report(k, obj)
                cs2_asset.validate_asset_name(name)
                objs.append(obj)
                by_key[f"{k}:{idx}"] = obj
        print(f"[lineup] family total: {total} tris across {len(objs)} assets")
        # Preview materials, ONE PER LIVERY. The shipped albedo is light-neutral because
        # recolour tints multiply, so a raw preview shows grey machines and says nothing
        # about colour. make_tinted_preview bakes each livery's tint through the
        # ControlMask exactly as CS2 will at runtime, and assets are dealt round-robin so
        # the row shows a mixed-manufacturer fleet — which is what a real resort looks
        # like after twenty years of buying whatever was on sale.
        tex_root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "textures")
        quad_sets, control_quads = cs2_asset.snow_prop_atlas_quads(tex_root)
        root = os.path.abspath(args.lineup or args.row)
        tmp = os.path.join(root, "_atlas")
        os.makedirs(tmp, exist_ok=True)
        cs2_asset.make_texture_set_atlas(tmp, "SnowPropsPreview", quad_sets, quad_size=512,
                                         control_quads=control_quads,
                                         post=cs2_asset.snow_prop_post)
        livery_dirs = []
        for liv in LIVERIES:
            d = os.path.join(root, "_liv_" + liv[0])
            make_tinted_preview(tmp, "SnowPropsPreview", d, liv)
            livery_dirs.append(d)
        for i, o in enumerate(objs):
            cs2_asset.preview_material(o, livery_dirs[i % len(livery_dirs)],
                                       "SnowPropsPreview")
        if args.row:
            present = {k.split(":")[0] for k in by_key}
            if not {"tower", "lance", "fan", "hydrant"} <= present:
                raise SystemExit("--row needs all four asset types built (default --assets)")
            for png in render_row(by_key, os.path.abspath(args.row)):
                print(f"[row] preview -> {png}")
        if args.lineup:
            for png in render_lineup(objs, os.path.abspath(args.lineup)):
                print(f"[lineup] preview -> {png}")
        return

    if not args.out:
        raise SystemExit("need --out (export), --lineup or --row (preview)")

    total = 0
    count = 0
    for k in keys:
        jobs = VARIANTS[k] if args.variants else [(ASSETS[k][0], {}, ASSETS[k][2])]
        for name, kwargs, quad_size in jobs:
            clear_scene()
            parts = BUILDER[k](PARAM_CLASS[k](**kwargs))
            obj = finish(parts, name, quad_size * 2, bevel=args.finish)
            cs2_asset.validate_asset_name(name)
            total += report(k, obj)
            export_one(k, obj, name, args.out, quad_size, do_finish=args.finish)
            count += 1
    print(f"[snow_props] family total: {total} tris across {count} asset(s)")


if __name__ == "__main__":
    main()
